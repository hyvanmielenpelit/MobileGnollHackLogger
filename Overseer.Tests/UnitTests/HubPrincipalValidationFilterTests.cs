using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MobileGnollHackLogger.Data;
using Overseer.Hubs;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The hub filter that re-checks a connected caller's account and security stamp: the per-connection
/// cache, the two rejections, the incognito erasure on a deleted account, and failing open.
/// </summary>
public sealed class HubPrincipalValidationFilterTests : IDisposable
{
    private const string UserId = "hub-user";
    private const string OtherUserId = "hub-other";
    private const string Stamp = "stamp-a";
    private const string NewStamp = "stamp-b";

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly string StampClaimType = new IdentityOptions().ClaimsIdentity.SecurityStampClaimType;

    private readonly ServiceProvider _provider;
    private readonly MutableTimeProvider _time = new();
    private readonly EphemeralSessionStore _store;
    private readonly HubPrincipalValidationFilter _filter;

    /// <summary>How many times the filter handed on to the hub method or the rest of the connect pipeline.</summary>
    private int _nextRuns;

    public HubPrincipalValidationFilterTests()
    {
        _store = new EphemeralSessionStore(new ConfigurationBuilder().Build(), logger: null, startSweeper: false);
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton(_store);
        _provider = services.BuildServiceProvider();

        _filter = new HubPrincipalValidationFilter(
            Options.Create(new SecurityStampValidatorOptions { ValidationInterval = Interval }),
            Options.Create(new IdentityOptions()),
            NullLogger<HubPrincipalValidationFilter>.Instance,
            _time);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Users.Add(new ApplicationUser { Id = UserId, UserName = UserId, SecurityStamp = Stamp });
        db.SaveChanges();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _store.Dispose();
    }

    private sealed class FakeHubCallerContext : HubCallerContext
    {
        private readonly ClaimsPrincipal _user;

        public FakeHubCallerContext(string userId, string? stamp)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
            if (stamp != null) claims.Add(new Claim(StampClaimType, stamp));
            _user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }

        public int AbortCount { get; private set; }

        public override string ConnectionId => "conn-1";
        public override string? UserIdentifier => _user.FindFirstValue(ClaimTypes.NameIdentifier);
        public override ClaimsPrincipal? User => _user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => AbortCount++;
    }

    private static Hub NewHub() => new ChatHub(null!, null!, null!);

    /// <summary>Runs one <c>JoinSession</c> invocation through the filter in a fresh scope.</summary>
    private async Task InvokeAsync(FakeHubCallerContext ctx, IServiceProvider? root = null)
    {
        using var scope = (root ?? _provider).CreateScope();
        var invocation = new HubInvocationContext(
            ctx, scope.ServiceProvider, NewHub(),
            typeof(ChatHub).GetMethod(nameof(ChatHub.JoinSession))!,
            new object?[] { "1" });

        await _filter.InvokeMethodAsync(invocation, _ =>
        {
            _nextRuns++;
            return ValueTask.FromResult<object?>(null);
        });
    }

    /// <summary>Runs the connect event through the filter in a fresh scope.</summary>
    private async Task ConnectAsync(FakeHubCallerContext ctx)
    {
        using var scope = _provider.CreateScope();
        await _filter.OnConnectedAsync(new HubLifetimeContext(ctx, scope.ServiceProvider, NewHub()), _ =>
        {
            _nextRuns++;
            return Task.CompletedTask;
        });
    }

    private async Task ChangeUserAsync(Action<ApplicationDbContext, IdentityUser> change)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == UserId, TestContext.Current.CancellationToken);
        change(db, user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
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
    public async Task AMatchingStampLetsTheCallThrough()
    {
        var ctx = new FakeHubCallerContext(UserId, Stamp);

        await InvokeAsync(ctx);

        Assert.Equal(1, _nextRuns);
        Assert.Equal(0, ctx.AbortCount);
    }

    [Fact]
    public async Task AChangedStampWithinTheIntervalStillPasses()
    {
        var ctx = new FakeHubCallerContext(UserId, Stamp);
        await ConnectAsync(ctx);

        await ChangeUserAsync((_, u) => u.SecurityStamp = NewStamp);
        _time.Now += Interval - TimeSpan.FromSeconds(1);
        await InvokeAsync(ctx);

        Assert.Equal(2, _nextRuns);
        Assert.Equal(0, ctx.AbortCount);
    }

    [Fact]
    public async Task AChangedStampPastTheIntervalEndsTheConnectionAndKeepsIncognitoSessions()
    {
        var held = _store.Create(UserId, Template(UserId));
        var ctx = new FakeHubCallerContext(UserId, Stamp);
        await ConnectAsync(ctx);

        await ChangeUserAsync((_, u) => u.SecurityStamp = NewStamp);
        _time.Now += Interval + TimeSpan.FromSeconds(1);

        var ex = await Assert.ThrowsAsync<HubException>(() => InvokeAsync(ctx));
        Assert.Equal(HubPrincipalValidationFilter.SignInEndedMessage, ex.Message);
        Assert.Equal(1, ctx.AbortCount);
        Assert.Equal(1, _nextRuns);
        // Ownership is by user id, so a renewed stamp alone leaves the incognito session.
        Assert.NotNull(_store.Get(held.Ref, UserId));
    }

    [Fact]
    public async Task ADeletedAccountPastTheIntervalEndsTheConnectionAndErasesItsIncognitoSessions()
    {
        var mine = _store.Create(UserId, Template(UserId));
        var theirs = _store.Create(OtherUserId, Template(OtherUserId));
        var ctx = new FakeHubCallerContext(UserId, Stamp);
        await ConnectAsync(ctx);

        await ChangeUserAsync((db, u) => db.Users.Remove(u));
        _time.Now += Interval + TimeSpan.FromSeconds(1);

        await Assert.ThrowsAsync<HubException>(() => InvokeAsync(ctx));
        Assert.Equal(1, ctx.AbortCount);
        Assert.Equal(1, _nextRuns);

        Assert.Null(_store.Get(mine.Ref, UserId));
        Assert.True(mine.IsErased);
        Assert.False(theirs.IsErased);
        Assert.NotNull(_store.Get(theirs.Ref, OtherUserId));
    }

    [Fact]
    public async Task ConnectingWithAStaleStampIsRefused()
    {
        await ChangeUserAsync((_, u) => u.SecurityStamp = NewStamp);
        var ctx = new FakeHubCallerContext(UserId, Stamp);

        await Assert.ThrowsAsync<HubException>(() => ConnectAsync(ctx));
        Assert.Equal(0, _nextRuns);
    }

    [Fact]
    public async Task AValidConnectIsCachedForTheInterval()
    {
        var ctx = new FakeHubCallerContext(UserId, Stamp);
        await ConnectAsync(ctx);
        Assert.Equal(1, _nextRuns);

        // A check would now find the account gone, so passing shows no query ran.
        await ChangeUserAsync((db, u) => db.Users.Remove(u));
        _time.Now += Interval - TimeSpan.FromSeconds(1);
        await InvokeAsync(ctx);

        Assert.Equal(2, _nextRuns);
        Assert.Equal(0, ctx.AbortCount);
    }

    [Fact]
    public async Task APrincipalWithoutAStampClaimIsRefused()
    {
        var ctx = new FakeHubCallerContext(UserId, stamp: null);

        await Assert.ThrowsAsync<HubException>(() => InvokeAsync(ctx));
        Assert.Equal(0, _nextRuns);
        Assert.Equal(1, ctx.AbortCount);
    }

    [Fact]
    public async Task AnUnreachableDatabaseFailsOpenAndCachesNothing()
    {
        // SQLite in memory with no schema: every query throws "no such table".
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite("Data Source=:memory:"));
        using var broken = services.BuildServiceProvider();
        var ctx = new FakeHubCallerContext(UserId, Stamp);

        await InvokeAsync(ctx, broken);

        Assert.Equal(1, _nextRuns);
        Assert.Equal(0, ctx.AbortCount);
        Assert.Empty(ctx.Items);
    }
}
