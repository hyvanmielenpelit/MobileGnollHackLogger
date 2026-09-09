using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The RAM-only store behind incognito chats: ownership, the sliding timeout, and what closing
/// one actually erases.
/// </summary>
public class EphemeralSessionStoreTests
{
    private const string Owner = "user-1";
    private const string Other = "user-2";

    private static IConfiguration Config(params (string Key, string? Value)[] settings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    // startSweeper: false throughout. A background timer in a unit test makes eviction a race;
    // Sweep() is called explicitly where expiry is the thing under test.
    private static EphemeralSessionStore Store(params (string, string?)[] settings)
        => new(Config(settings), logger: null, startSweeper: false);

    private static ChatSession Template(string userId = Owner) => new()
    {
        AspNetUserId = userId,
        Title = "Incognito chat",
        CreatedUtc = DateTime.UtcNow,
        LastMessageUtc = DateTime.UtcNow,
        IsConfidential = true
    };

    [Fact]
    public void TheDefaultTimeoutIsAnHourAndConfigurationOverridesIt()
    {
        Assert.Equal(TimeSpan.FromMinutes(EphemeralSessionStore.DefaultTimeoutMinutes), Store().Timeout);
        Assert.Equal(TimeSpan.FromMinutes(5), Store(("PrivacySettings:Ephemeral:TimeoutMinutes", "5")).Timeout);
        // A nonsensical value falls back rather than expiring every session instantly.
        Assert.Equal(TimeSpan.FromMinutes(EphemeralSessionStore.DefaultTimeoutMinutes),
            Store(("PrivacySettings:Ephemeral:TimeoutMinutes", "0")).Timeout);
        Assert.Equal(TimeSpan.FromMinutes(EphemeralSessionStore.DefaultTimeoutMinutes),
            Store(("PrivacySettings:Ephemeral:TimeoutMinutes", "-30")).Timeout);
    }

    [Fact]
    public void OnlyTheOwnerCanReachASession()
    {
        using var store = Store();
        var held = store.Create(Owner, Template());

        Assert.True(store.IsOwnedBy(held.Ref, Owner));
        Assert.NotNull(store.Get(held.Ref, Owner));

        // This is the check that stands in for the ChatSession row lookup a persisted session
        // gets, so it is the whole of the authorisation story for an incognito chat.
        Assert.False(store.IsOwnedBy(held.Ref, Other));
        Assert.Null(store.Get(held.Ref, Other));
        Assert.False(store.IsOwnedBy(held.Ref, null));
    }

    [Fact]
    public void APersistentReferenceIsNeverFoundInTheStore()
    {
        using var store = Store();
        store.Create(Owner, Template());

        Assert.False(store.IsOwnedBy(Overseer.Services.Privacy.SessionRef.Persistent(1), Owner));
        Assert.Null(store.Get(default, Owner));
    }

    [Fact]
    public void AnUnknownEphemeralReferenceIsSimplyNotThere()
    {
        using var store = Store();

        Assert.False(store.IsOwnedBy(Overseer.Services.Privacy.SessionRef.NewEphemeral(), Owner));
    }

    [Fact]
    public void ClosingErasesTheContentAndTheSecondCloseReportsNothingToDo()
    {
        using var store = Store();
        var held = store.Create(Owner, Template());
        held.AddMessage(id => new EphemeralMessage { Id = id, Role = "user", Content = "a secret question" });
        var attachment = held.AddAttachment(1, "notes.txt", "text/plain", Encoding.UTF8.GetBytes("secret bytes"));
        var buffer = attachment.Bytes;

        Assert.True(store.Close(held.Ref, Owner));

        Assert.Null(store.Get(held.Ref, Owner));
        Assert.Equal(0, store.Count);
        Assert.True(held.IsErased);
        Assert.Empty(held.Messages);
        Assert.Empty(held.Attachments);
        /* The buffer the test still holds a reference to was overwritten in place, which is the
           only part of the teardown that is observable -- and the only part that is more than a
           dropped reference. */
        Assert.All(buffer, b => Assert.Equal(0, b));

        // A repeated close is what a double-click or a retried request looks like.
        Assert.False(store.Close(held.Ref, Owner));
    }

    [Fact]
    public void SomebodyElseCannotCloseYourSession()
    {
        using var store = Store();
        var held = store.Create(Owner, Template());

        Assert.False(store.Close(held.Ref, Other));
        Assert.NotNull(store.Get(held.Ref, Owner));
    }

    [Fact]
    public void ExpiryEvictsAndTouchingTheSessionPostponesIt()
    {
        using var store = Store(("PrivacySettings:Ephemeral:TimeoutMinutes", "30"));
        var stale = store.Create(Owner, Template());
        var fresh = store.Create(Owner, Template());

        stale.LastAccessUtc = DateTime.UtcNow.AddMinutes(-31);
        fresh.LastAccessUtc = DateTime.UtcNow.AddMinutes(-29);

        Assert.Equal(1, store.Sweep());

        Assert.Null(store.Get(stale.Ref, Owner));
        Assert.True(stale.IsErased);
        Assert.NotNull(store.Get(fresh.Ref, Owner));
    }

    [Fact]
    public void AnExpiredSessionIsGoneEvenBeforeTheSweeperRuns()
    {
        using var store = Store(("PrivacySettings:Ephemeral:TimeoutMinutes", "30"));
        var held = store.Create(Owner, Template());
        held.LastAccessUtc = DateTime.UtcNow.AddMinutes(-31);

        // The sweeper runs once a minute, so a read has to enforce the deadline itself or the
        // window would be a minute wider than it says.
        Assert.Null(store.Get(held.Ref, Owner));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void ReadingASessionSlidesItsDeadlineForward()
    {
        using var store = Store(("PrivacySettings:Ephemeral:TimeoutMinutes", "30"));
        var held = store.Create(Owner, Template());
        held.LastAccessUtc = DateTime.UtcNow.AddMinutes(-20);

        Assert.NotNull(store.Get(held.Ref, Owner));
        Assert.True(held.LastAccessUtc > DateTime.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public void DeletingAnAccountTakesItsOpenIncognitoChatsWithIt()
    {
        using var store = Store();
        var mine1 = store.Create(Owner, Template());
        var mine2 = store.Create(Owner, Template());
        var theirs = store.Create(Other, Template(Other));

        Assert.Equal(2, store.CloseAllForUser(Owner));

        Assert.True(mine1.IsErased);
        Assert.True(mine2.IsErased);
        Assert.False(theirs.IsErased);
        Assert.NotNull(store.Get(theirs.Ref, Other));
    }

    [Fact]
    public void MessageAndAttachmentIdsAreAssignedPerSession()
    {
        using var store = Store();
        var a = store.Create(Owner, Template());
        var b = store.Create(Owner, Template());

        Assert.Equal(1, a.AddMessage(id => new EphemeralMessage { Id = id, Role = "user", Content = "one" }));
        Assert.Equal(2, a.AddMessage(id => new EphemeralMessage { Id = id, Role = "assistant", Content = "two" }));
        // Independent counters: the ids are indices within one session, not keys in a shared
        // space, which is why they are only meaningful under a session reference.
        Assert.Equal(1, b.AddMessage(id => new EphemeralMessage { Id = id, Role = "user", Content = "one" }));

        Assert.Equal(1, a.AddAttachment(1, "a.png", "image/png", new byte[] { 1 }).Id);
        Assert.Equal(2, a.AddAttachment(1, "b.png", "image/png", new byte[] { 2 }).Id);
        Assert.Equal(1, b.AddAttachment(1, "a.png", "image/png", new byte[] { 1 }).Id);
        Assert.Equal("b.png", a.FindAttachment(2)!.FileName);
        Assert.Null(a.FindAttachment(99));
    }

    [Fact]
    public void AMessageCarriesEveryColumnAPersistedOneWould()
    {
        var source = new ChatMessage
        {
            Role = "assistant",
            Content = "the answer",
            ProviderUsed = "openai",
            ModelUsed = "gpt-x",
            ModelDisplayNameUsed = "GPT X",
            ThinkingLevelUsed = "high",
            TimeToFirstTokenMs = 12,
            TotalDurationMs = 345,
            InputTokens = 100,
            OutputTokens = 200,
            EstimatedCost = 0.0042m,
            PricingSource = "catalog",
            SystemAiConfigurationIdUsed = 9,
            IsGameSnapshot = true
        };
        source.ToolCalls.Add(new ChatMessageToolCall
        {
            ToolCallId = "call-1",
            Name = "search",
            ArgsText = "{\"q\":\"private\"}",
            Result = "found",
            Status = "completed",
            SortOrder = 3
        });

        var stored = EphemeralMessage.From(7, source);

        Assert.Equal(7, stored.Id);
        Assert.Equal("the answer", stored.Content);
        Assert.Equal("openai", stored.ProviderUsed);
        Assert.Equal("GPT X", stored.ModelDisplayNameUsed);
        Assert.Equal(0.0042m, stored.EstimatedCost);
        Assert.Equal(9, stored.SystemAiConfigurationIdUsed);
        Assert.True(stored.IsGameSnapshot);

        var call = Assert.Single(stored.ToolCalls);
        Assert.Equal("call-1", call.ToolCallId);
        Assert.Equal("{\"q\":\"private\"}", call.ArgsText);
        Assert.Equal("found", call.Result);
        Assert.Null(call.Error);
        Assert.Equal(3, call.SortOrder);

        // Round-tripping back into the entity shape is how the read paths stay written once.
        var back = stored.ToChatMessage();
        Assert.Equal(source.Content, back.Content);
        Assert.Equal(source.ProviderUsed, back.ProviderUsed);
        Assert.Equal(source.EstimatedCost, back.EstimatedCost);
    }

    [Fact]
    public void ToolCallPayloadsAreErasedToo()
    {
        using var store = Store();
        var held = store.Create(Owner, Template());
        var source = new ChatMessage { Role = "assistant", Content = "reply" };
        source.ToolCalls.Add(new ChatMessageToolCall
        {
            ToolCallId = "call-1",
            Name = "read_file",
            ArgsText = "{\"path\":\"C:/secret.txt\"}",
            Result = "the file contents",
            Error = "and an error"
        });
        held.AddMessage(id => EphemeralMessage.From(id, source));

        store.Close(held.Ref, Owner);

        Assert.Empty(held.Messages);
        Assert.True(held.IsErased);
    }

    [Fact]
    public void DisposingTheStoreErasesEverythingLeftInIt()
    {
        var store = Store();
        var held = store.Create(Owner, Template());
        held.AddMessage(id => new EphemeralMessage { Id = id, Role = "user", Content = "still open at shutdown" });

        store.Dispose();

        Assert.True(held.IsErased);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void CreatingRequiresAnOwnerAndATemplate()
    {
        using var store = Store();

        Assert.Throws<ArgumentException>(() => store.Create("", Template()));
        Assert.Throws<ArgumentNullException>(() => store.Create(Owner, null!));
    }
}
