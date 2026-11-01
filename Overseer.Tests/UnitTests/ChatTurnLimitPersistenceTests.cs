using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// A chat turn's notice through <see cref="ChatService"/>: the two message columns, the event rows, the
/// last <c>turn_notice</c> event, and the cancel-cause decision. No network.
/// </summary>
public sealed class ChatTurnLimitPersistenceTests : ChatTurnHarness
{
    private const string Marker = "SECRET-MARKER-IN-PROVIDER-BODY";

    [Fact]
    public async Task AnEmptyReply_StoresEmptyContent_TheNoticeAndAnEventRow_AndEmitsTheNoticeLast()
    {
        var provider = new ScriptedAiProvider { Script = _ => new[] { Completed() } };
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);

        var events = await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

        var stored = Read(container, db => db.ChatMessage.Single(m => m.Role == "assistant"));
        Assert.Equal(string.Empty, stored.Content);
        Assert.Equal(TurnLimitCodes.NoFinalAnswer, stored.TurnNoticeCode);
        Assert.Contains("\"severity\":\"error\"", stored.TurnNoticeFactsJson);

        var row = Read(container, db => db.ChatTurnLimitEvents.Single());
        Assert.Equal(TurnLimitCodes.NoFinalAnswer, row.Code);
        Assert.Equal((byte)1, row.Severity);
        Assert.Equal(userId, row.AspNetUserId);
        Assert.Equal(sessionId, row.ChatSessionId);
        Assert.Equal(stored.Id, row.AssistantMessageId);
        Assert.NotNull(row.UserMessageId);
        Assert.Equal(AiKeySource.UserKey, row.KeySource);

        Assert.DoesNotContain(events, e => e.Type == "error");
        Assert.Equal("turn_notice", events[^1].Type);
        using var notice = JsonDocument.Parse(events[^1].Data);
        Assert.Equal(TurnLimitCodes.NoFinalAnswer, notice.RootElement.GetProperty("code").GetString());
        Assert.Equal("error", notice.RootElement.GetProperty("severity").GetString());
        Assert.Equal(row.Id, notice.RootElement.GetProperty("reference").GetInt64());
        Assert.False(notice.RootElement.TryGetProperty("adminFacts", out _));
    }

    [Fact]
    public async Task ANormalTurn_HasNoNoticeAndNoRow()
    {
        var provider = new ScriptedAiProvider
        {
            Script = _ => new[] { new ChatEvent { Type = "chunk", Data = "An answer." }, Completed() }
        };
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);

        var events = await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

        Assert.DoesNotContain(events, e => e.Type == "turn_notice");
        Assert.Null(Read(container, db => db.ChatMessage.Single(m => m.Role == "assistant")).TurnNoticeCode);
        Assert.Equal(0, Read(container, db => db.ChatTurnLimitEvents.Count()));
    }

    [Fact]
    public async Task AProviderError_LeavesNoMarkerAnywhereStored_AndOnlyAnAdministratorSeesItLive()
    {
        foreach (var (userName, isAdmin) in new[] { ("regular-user", false), (AdminName, true) })
        {
            var provider = new ScriptedAiProvider
            {
                Script = _ => new[] { new ChatEvent { Type = "error", Data = $"bad {Marker}", Detail = Marker }, Completed() }
            };
            using var container = BuildContainer(provider);
            var (userId, sessionId) = await SeedAsync(container, userName);

            var events = await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

            var stored = Read(container, db => db.ChatMessage.Single(m => m.Role == "assistant"));
            Assert.DoesNotContain(Marker, stored.Content);
            Assert.DoesNotContain(Marker, stored.TurnNoticeFactsJson);
            Assert.Equal(TurnLimitCodes.ProviderError, stored.TurnNoticeCode);

            var row = Read(container, db => db.ChatTurnLimitEvents.Single());
            Assert.DoesNotContain(Marker, JsonSerializer.Serialize(TextFields(row)));

            foreach (var evt in events.Where(e => e.Type != "turn_notice"))
            {
                Assert.DoesNotContain(Marker, evt.Data ?? "");
                Assert.DoesNotContain(Marker, evt.Detail ?? "");
            }

            var notice = Assert.Single(events, e => e.Type == "turn_notice");
            Assert.Equal(isAdmin, notice.Data.Contains(Marker));
        }
    }

    [Fact]
    public async Task TheTimerFiring_IsATimeLimitHit_WithThePartialTextSaved()
    {
        using var cts = new CancellationTokenSource();
        var provider = new ScriptedAiProvider
        {
            Script = _ => new[] { new ChatEvent { Type = "chunk", Data = "Partial" } },
            AfterEvents = async ct =>
            {
                cts.Cancel();
                await Task.Delay(Timeout.Infinite, ct);
            }
        };
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);
        var sessionRef = SessionRef.Persistent(sessionId);
        var manager = container.GetRequiredService<OngoingChatManager>();
        Assert.True(manager.TryStart(sessionRef, cts, out var state));
        state.DeadlineUtc = state.StartedAtUtc.AddSeconds(300);

        var events = await RunTurnAsync(container, sessionRef, userId, cts.Token);

        var stored = Read(container, db => db.ChatMessage.Single(m => m.Role == "assistant"));
        Assert.Contains("Partial", stored.Content);
        Assert.Equal(TurnLimitCodes.TimeLimit, stored.TurnNoticeCode);
        Assert.DoesNotContain("**Error:**", stored.Content);
        Assert.Equal(300, Read(container, db => db.ChatTurnLimitEvents.Single()).LimitValue);
        Assert.Equal(1, events.Count(e => e.Type == "turn_notice"));
        Assert.DoesNotContain(events, e => e.Type == "error");
    }

    [Fact]
    public async Task AUserCancel_IsNotALimit()
    {
        var provider = new ScriptedAiProvider();
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);
        var sessionRef = SessionRef.Persistent(sessionId);
        var manager = container.GetRequiredService<OngoingChatManager>();
        using var cts = new CancellationTokenSource();
        Assert.True(manager.TryStart(sessionRef, cts, out _));
        provider.Script = _ => new[] { new ChatEvent { Type = "chunk", Data = "Partial" } };
        provider.AfterEvents = async ct =>
        {
            manager.TryCancelAndRemove(sessionRef);
            await Task.Delay(Timeout.Infinite, ct);
        };

        var events = await RunTurnAsync(container, sessionRef, userId, cts.Token);

        Assert.DoesNotContain(events, e => e.Type == "turn_notice");
        Assert.Equal(0, Read(container, db => db.ChatTurnLimitEvents.Count()));
        Assert.Null(Read(container, db => db.ChatMessage.Single(m => m.Role == "assistant")).TurnNoticeCode);
    }

    [Fact]
    public async Task AnIncognitoTurnOnTheUsersOwnKey_ShowsTheNotice_AndWritesNoRow()
    {
        var provider = new ScriptedAiProvider { Script = _ => new[] { Completed() } };
        using var container = BuildContainer(provider);
        var (userId, _) = await SeedAsync(container);
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var held = store.Create(userId, new ChatSession { AspNetUserId = userId, Title = "Incognito", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow });

        var events = await RunTurnAsync(container, held.Ref, userId);

        Assert.Single(events, e => e.Type == "turn_notice");
        Assert.Equal(0, Read(container, db => db.ChatTurnLimitEvents.Count()));
        Assert.Equal(TurnLimitCodes.NoFinalAnswer, held.Messages.Single(m => m.Role == "assistant").TurnNoticeCode);
    }

    private static object TextFields(ChatTurnLimitEvent row) => new
    {
        row.Code, row.ProviderErrorCode, row.ProviderFinishReason, row.AssistantPhase, row.Provider, row.RequestedModelId
    };
}
