using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Hubs;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Hub authorisation for both kinds of session reference.
/// </summary>
/// <remarks>
/// This is the regression the plan calls out as R-12. Four hub methods authorised by looking up
/// a <c>ChatSession</c> row, which an ephemeral session has none of by construction: the lookup
/// returned null, the caller was never added to the group, and the client then received no
/// streamed tokens, no tool events and no completion. That failure reads as a hung model rather
/// than a refused connection, which is exactly the kind of bug a test has to catch before a
/// user does.
/// </remarks>
public class ChatHubSessionRefTests
{
    private const string Owner = "owner-user";
    private const string Other = "other-user";
    private const string CallerConnectionId = "conn-1";

    /// <summary>Records what the hub asked the group manager to do.</summary>
    private sealed class RecordingGroupManager : IGroupManager
    {
        public List<(string ConnectionId, string GroupName)> Added { get; } = new();
        public List<(string ConnectionId, string GroupName)> Removed { get; } = new();

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Removed.Add((connectionId, groupName));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHubCallerContext : HubCallerContext
    {
        private readonly string? _userId;

        public FakeHubCallerContext(string? userId) => _userId = userId;

        public override string ConnectionId => CallerConnectionId;
        public override string? UserIdentifier => _userId;
        public override ClaimsPrincipal? User => _userId == null
            ? null
            : new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, _userId) }, "Test"));
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private static IConfiguration Config()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) }
            })
            .Build();

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static (ChatHub Hub, RecordingGroupManager Groups) CreateHub(
        ApplicationDbContext db, EphemeralSessionStore store, string? callerUserId)
    {
        var groups = new RecordingGroupManager();
        var hub = new ChatHub(db, null!, store)
        {
            Context = new FakeHubCallerContext(callerUserId),
            Groups = groups
        };
        return (hub, groups);
    }

    private static ChatSession Template(string userId) => new()
    {
        AspNetUserId = userId,
        Title = "Incognito chat",
        CreatedUtc = DateTime.UtcNow,
        LastMessageUtc = DateTime.UtcNow,
        IsConfidential = true
    };

    [Fact]
    public async Task JoinSessionAdmitsTheOwnerOfAnEphemeralReference()
    {
        using var db = NewDb();
        using var store = new EphemeralSessionStore(Config(), logger: null, startSweeper: false);
        var held = store.Create(Owner, Template(Owner));
        var (hub, groups) = CreateHub(db, store, Owner);

        await hub.JoinSession(held.Ref.ToWireString());

        var added = Assert.Single(groups.Added);
        Assert.Equal(CallerConnectionId, added.ConnectionId);
        Assert.Equal(held.Ref.GroupName, added.GroupName);
    }

    [Fact]
    public async Task JoinSessionRefusesADifferentUsersEphemeralReference()
    {
        using var db = NewDb();
        using var store = new EphemeralSessionStore(Config(), logger: null, startSweeper: false);
        var held = store.Create(Owner, Template(Owner));
        var (hub, groups) = CreateHub(db, store, Other);

        await hub.JoinSession(held.Ref.ToWireString());

        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task JoinSessionStillAuthorisesAPersistedSessionAgainstItsRow()
    {
        using var db = NewDb();
        using var store = new EphemeralSessionStore(Config(), logger: null, startSweeper: false);
        db.ChatSession.Add(new ChatSession { Id = 42, AspNetUserId = Owner, Title = "Saved" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (ownerHub, ownerGroups) = CreateHub(db, store, Owner);
        await ownerHub.JoinSession("42");
        // Unchanged from before the reference became a type: the group name is still the id.
        Assert.Equal("42", Assert.Single(ownerGroups.Added).GroupName);

        var (otherHub, otherGroups) = CreateHub(db, store, Other);
        await otherHub.JoinSession("42");
        Assert.Empty(otherGroups.Added);
    }

    [Fact]
    public async Task JoinSessionRefusesAClosedOrExpiredEphemeralReference()
    {
        using var db = NewDb();
        using var store = new EphemeralSessionStore(Config(), logger: null, startSweeper: false);
        var held = store.Create(Owner, Template(Owner));
        var wire = held.Ref.ToWireString();
        store.Close(held.Ref, Owner);

        var (hub, groups) = CreateHub(db, store, Owner);
        await hub.JoinSession(wire);

        Assert.Empty(groups.Added);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("eph_garbage")]
    public async Task JoinSessionRefusesAReferenceThatDoesNotParse(string? wire)
    {
        using var db = NewDb();
        using var store = new EphemeralSessionStore(Config(), logger: null, startSweeper: false);
        var (hub, groups) = CreateHub(db, store, Owner);

        await hub.JoinSession(wire!);

        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task JoinSessionRefusesAnUnauthenticatedCaller()
    {
        using var db = NewDb();
        using var store = new EphemeralSessionStore(Config(), logger: null, startSweeper: false);
        var held = store.Create(Owner, Template(Owner));
        var (hub, groups) = CreateHub(db, store, callerUserId: null);

        await hub.JoinSession(held.Ref.ToWireString());

        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task LeaveSessionRemovesTheSameGroupNameJoinSessionAdded()
    {
        // V-13: the one hub method that authorises nothing, and should not -- removing your own
        // connection from a group needs no permission. What it does need is the parse, or the
        // group name would not match and an ephemeral connection would keep receiving events.
        using var db = NewDb();
        using var store = new EphemeralSessionStore(Config(), logger: null, startSweeper: false);
        var held = store.Create(Owner, Template(Owner));
        var wire = held.Ref.ToWireString();

        var (hub, groups) = CreateHub(db, store, Owner);
        await hub.JoinSession(wire);
        await hub.LeaveSession(wire);

        Assert.Equal(Assert.Single(groups.Added).GroupName, Assert.Single(groups.Removed).GroupName);
    }

    [Fact]
    public async Task LeaveSessionWorksWithoutAnAuthenticatedCallerAndAfterTheSessionIsGone()
    {
        using var db = NewDb();
        using var store = new EphemeralSessionStore(Config(), logger: null, startSweeper: false);
        var held = store.Create(Owner, Template(Owner));
        var wire = held.Ref.ToWireString();
        store.Close(held.Ref, Owner);

        var (hub, groups) = CreateHub(db, store, callerUserId: null);
        await hub.LeaveSession(wire);

        // A disconnect arriving after the chat was closed still has to clean up the group.
        Assert.Equal(wire, Assert.Single(groups.Removed).GroupName);
    }

    [Fact]
    public async Task LeaveSessionStillRefusesAReferenceThatDoesNotParse()
    {
        using var db = NewDb();
        using var store = new EphemeralSessionStore(Config(), logger: null, startSweeper: false);
        var (hub, groups) = CreateHub(db, store, Owner);

        await hub.LeaveSession("not-a-reference");

        Assert.Empty(groups.Removed);
    }
}
