using System;
using System.Linq;
using System.Threading.Tasks;
using GnollHackServer.Data.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The game sign-in block mirror: a block of an existing account is written as a row and an unknown name's is not; a
/// cleared row releases the block, an uncleared one refuses with its account; the lookup interval; a pair and an
/// account block cleared together; a clear in the same process; a cleared row of an earlier block; and the start-up
/// cleanup of one process's rows. SQLite in memory, each step in a context of its own as requests have.
/// </summary>
public sealed class GameSignInBlockSyncTests : IDisposable
{
    private const string UserId = "user-alice";
    private const string UserName = "alice";
    private const string Ip = "203.0.113.7";
    private const string OtherIp = "198.51.100.250";

    private readonly AdminUserTestHost _host = new();
    private readonly GameApiSignInThrottle _throttle;
    private readonly GameSignInBlockSync _sync;

    public GameSignInBlockSyncTests()
    {
        _throttle = new GameApiSignInThrottle(time: _host.Time);
        _sync = new GameSignInBlockSync(_throttle, GameSignInBlockSource.Website, _host.Time);
    }

    public void Dispose() => _host.Dispose();

    private async Task<T> InScopeAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = _host.Provider.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private Task InScopeAsync(Func<ApplicationDbContext, Task> action) => InScopeAsync(async db => { await action(db); return 0; });

    private Task FailAsync(string? userId, string ip, int times = 1) => InScopeAsync(async db =>
    {
        for (int i = 0; i < times; i++)
        {
            await _sync.RecordFailureAsync(db, UserName, ip, userId, ct: TestContext.Current.CancellationToken);
        }
    });

    private Task<GameSignInCheck> CheckAsync(string ip) =>
        InScopeAsync(db => _sync.CheckAsync(db, UserName, UserName.ToUpperInvariant(), ip, ct: TestContext.Current.CancellationToken));

    private Task<GameSignInBlock[]> RowsAsync() =>
        InScopeAsync(db => db.GameSignInBlocks.AsNoTracking().OrderBy(b => b.Id).ToArrayAsync(TestContext.Current.CancellationToken));

    private Task ClearAllAsync() => InScopeAsync(db => db.GameSignInBlocks
        .ExecuteUpdateAsync(s => s.SetProperty(b => b.ClearedUtc, _host.NowUtc).SetProperty(b => b.ClearedByUserId, "admin"),
            TestContext.Current.CancellationToken));

    [Fact]
    public async Task APairBlockOfAnExistingAccountIsWrittenAsAnActiveRow()
    {
        await _host.AddUserAsync(UserId, UserName);
        DateTime now = _host.NowUtc;

        await FailAsync(UserId, Ip, 9);
        Assert.Empty(await RowsAsync());
        await FailAsync(UserId, Ip);

        var row = Assert.Single(await RowsAsync());
        Assert.Equal((GameSignInBlockSource.Website, GameSignInBlockKind.Pair, UserId, Ip),
            (row.Source, row.Kind, row.AspNetUserId, row.IpAddress));
        Assert.Equal((now, now.AddMinutes(15)), (row.BlockedUtc, row.BlockedUntilUtc));
        Assert.Null(row.ClearedUtc);
    }

    [Fact]
    public async Task AnUnknownNameWritesNoRow()
    {
        await FailAsync(userId: null, Ip, 10);

        Assert.False(_throttle.Check(UserName, Ip).Allowed);
        Assert.Empty(await RowsAsync());
        var check = await CheckAsync(Ip);
        Assert.Equal((false, "pair", (string?)null), (check.Allowed, check.Reason, check.UserId));
    }

    [Fact]
    public async Task AnUnclearedRowRefusesWithItsAccount()
    {
        await _host.AddUserAsync(UserId, UserName);
        await FailAsync(UserId, Ip, 10);

        var check = await CheckAsync(Ip);

        Assert.False(check.Allowed);
        Assert.Equal(("pair", UserId), (check.Reason, check.UserId));
        Assert.Equal(TimeSpan.FromMinutes(15), check.RetryAfter);
        Assert.True((await CheckAsync(OtherIp)).Allowed);
    }

    [Fact]
    public async Task AClearedRowReleasesTheBlockAndTheRequestGoesOn()
    {
        await _host.AddUserAsync(UserId, UserName);
        await FailAsync(UserId, Ip, 10);
        await ClearAllAsync();

        var check = await CheckAsync(Ip);

        Assert.True(check.Allowed);
        Assert.True(_throttle.Check(UserName, Ip).Allowed);
    }

    [Fact]
    public async Task AClearMadeRightAfterALookupIsSeenOnlyAfterTheLookupInterval()
    {
        await _host.AddUserAsync(UserId, UserName);
        await FailAsync(UserId, Ip, 10);
        Assert.False((await CheckAsync(Ip)).Allowed);

        await ClearAllAsync();
        _host.Time.Now += GameSignInBlockSync.LookupInterval - TimeSpan.FromSeconds(1);
        var cached = await CheckAsync(Ip);
        _host.Time.Now += TimeSpan.FromSeconds(1);
        var fresh = await CheckAsync(Ip);

        Assert.Equal((false, UserId), (cached.Allowed, cached.UserId));
        Assert.True(fresh.Allowed);
    }

    [Fact]
    public async Task APairAndAnAccountBlockClearedTogetherAreBothReleasedInOneCheck()
    {
        await _host.AddUserAsync(UserId, UserName);
        await FailAsync(UserId, Ip, 10);
        for (int i = 0; i < 40; i++)
        {
            await FailAsync(UserId, $"198.51.100.{i}");
        }
        var rows = await RowsAsync();
        Assert.Equal(new[] { (GameSignInBlockKind.Pair, (string?)Ip), (GameSignInBlockKind.Account, (string?)null) },
            rows.Select(r => (r.Kind, r.IpAddress)).ToArray());
        Assert.Equal("account", (await CheckAsync(OtherIp)).Reason);

        await ClearAllAsync();
        _host.Time.Now += GameSignInBlockSync.LookupInterval;

        Assert.True((await CheckAsync(Ip)).Allowed);
        Assert.True(_throttle.Check(UserName, OtherIp).Allowed);
    }

    [Fact]
    public async Task ReleaseLocalEndsTheBlockAndForgetsTheLookup()
    {
        await _host.AddUserAsync(UserId, UserName);
        await FailAsync(UserId, Ip, 10);
        Assert.False((await CheckAsync(Ip)).Allowed);

        _sync.ReleaseLocal(GameSignInBlockKind.Pair, "Alice", Ip);

        Assert.True((await CheckAsync(Ip)).Allowed);
    }

    [Fact]
    public async Task AReopenedBlockMakesItsClearedRowActiveAgain()
    {
        await _host.AddUserAsync(UserId, UserName);
        await FailAsync(UserId, Ip, 10);
        await ClearAllAsync();
        Assert.True((await CheckAsync(Ip)).Allowed);

        _host.Time.Now += TimeSpan.FromMinutes(1);
        await FailAsync(UserId, Ip, 10);

        var row = Assert.Single(await RowsAsync());
        Assert.Null(row.ClearedUtc);
        Assert.Null(row.ClearedByUserId);
        Assert.Equal(_host.NowUtc.AddMinutes(15), row.BlockedUntilUtc);
        Assert.Equal(UserId, (await CheckAsync(Ip)).UserId);
    }

    [Fact]
    public async Task AClearedRowOfAnEarlierBlockNeverReleasesANewerOne()
    {
        await _host.AddUserAsync(UserId, UserName);
        await _host.AddSignInBlockAsync(UserId, GameSignInBlockSource.Website, GameSignInBlockKind.Pair, Ip,
            _host.NowUtc.AddMinutes(5), clearedUtc: _host.NowUtc);
        // The new block's row is never written: the failures are counted without the account.
        await FailAsync(userId: null, Ip, 10);

        var check = await CheckAsync(Ip);

        Assert.Equal((false, UserId), (check.Allowed, check.UserId));
    }

    [Fact]
    public async Task RowsThatEndedMoreThanADayAgoAreRemovedWithTheNextBlock()
    {
        await _host.AddUserAsync(UserId, UserName);
        await _host.AddSignInBlockAsync(UserId, GameSignInBlockSource.Website, GameSignInBlockKind.Account, null, _host.NowUtc.AddDays(-2));
        await _host.AddSignInBlockAsync(UserId, GameSignInBlockSource.Overseer, GameSignInBlockKind.Account, null, _host.NowUtc.AddDays(-2));

        await FailAsync(UserId, Ip, 10);

        Assert.Equal(new[] { (GameSignInBlockSource.Overseer, GameSignInBlockKind.Account), (GameSignInBlockSource.Website, GameSignInBlockKind.Pair) },
            (await RowsAsync()).Select(r => (r.Source, r.Kind)).ToArray());
    }

    [Fact]
    public async Task EndAllOwnEndsOnlyThisProcesssActiveRowsAndRemovesItsOldOnes()
    {
        await _host.AddUserAsync(UserId, UserName);
        DateTime now = _host.NowUtc;
        await _host.AddSignInBlockAsync(UserId, GameSignInBlockSource.Website, GameSignInBlockKind.Pair, Ip, now.AddMinutes(10));
        await _host.AddSignInBlockAsync(UserId, GameSignInBlockSource.Website, GameSignInBlockKind.Account, null, now.AddMinutes(20), clearedUtc: now);
        await _host.AddSignInBlockAsync(UserId, GameSignInBlockSource.Website, GameSignInBlockKind.Pair, OtherIp, now.AddDays(-2));
        await _host.AddSignInBlockAsync(UserId, GameSignInBlockSource.Overseer, GameSignInBlockKind.Pair, Ip, now.AddMinutes(10));

        await InScopeAsync(db => _sync.EndAllOwnAsync(db, TestContext.Current.CancellationToken));

        var rows = await RowsAsync();
        Assert.Equal(new[]
            {
                (GameSignInBlockSource.Website, (string?)Ip, now),
                (GameSignInBlockSource.Website, (string?)null, now.AddMinutes(20)),
                (GameSignInBlockSource.Overseer, (string?)Ip, now.AddMinutes(10))
            },
            rows.Select(r => (r.Source, r.IpAddress, r.BlockedUntilUtc)).ToArray());
    }
}
