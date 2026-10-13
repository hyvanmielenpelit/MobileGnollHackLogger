using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GnollHackServer.Data.Security;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The game API sign-in throttle: sliding failure windows per account and per IP, fixed-length blocks,
/// refused checks that count nothing, and the tracked-key cap.
/// </summary>
public class GameApiSignInThrottleTests
{
    private const string Ip = "203.0.113.7";

    /// <summary>A clock the test moves by hand.</summary>
    private sealed class FakeClock : TimeProvider
    {
        private readonly object _lock = new();
        private DateTimeOffset _now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by)
        {
            lock (_lock) _now += by;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_lock) return _now;
        }
    }

    private static void Fail(GameApiSignInThrottle throttle, string userName, string? ip, int times)
    {
        for (int i = 0; i < times; i++)
        {
            throttle.RecordFailure(userName, ip);
        }
    }

    [Fact]
    public void NineFailuresAreAllowedAndTheTenthOpensAFifteenMinuteBlock()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);

        Fail(throttle, "alice", Ip, 9);
        Assert.True(throttle.Check("alice", Ip).Allowed);

        throttle.RecordFailure("alice", Ip);
        var decision = throttle.Check("alice", Ip);

        Assert.False(decision.Allowed);
        Assert.Equal("account", decision.Reason);
        Assert.Equal(TimeSpan.FromMinutes(15), decision.RetryAfter);
    }

    [Fact]
    public void RefusedChecksDuringTheBlockDoNotExtendIt()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);
        Fail(throttle, "alice", Ip, 10);

        clock.Advance(TimeSpan.FromMinutes(5));
        for (int i = 0; i < 20; i++)
        {
            Assert.False(throttle.Check("alice", Ip).Allowed);
        }
        Assert.Equal(TimeSpan.FromMinutes(10), throttle.Check("alice", Ip).RetryAfter);

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(5), throttle.Check("alice", Ip).RetryAfter);
    }

    [Fact]
    public void FailuresRecordedDuringTheBlockDoNotExtendIt()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);
        Fail(throttle, "alice", null, 10);

        clock.Advance(TimeSpan.FromMinutes(10));
        Fail(throttle, "alice", null, 5);

        Assert.Equal(TimeSpan.FromMinutes(5), throttle.Check("alice", null).RetryAfter);
    }

    [Fact]
    public void TheBlockEndsOnTime()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);
        Fail(throttle, "alice", Ip, 10);

        clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1));
        Assert.False(throttle.Check("alice", Ip).Allowed);

        clock.Advance(TimeSpan.FromSeconds(1));
        var decision = throttle.Check("alice", Ip);
        Assert.True(decision.Allowed);
        Assert.Equal(TimeSpan.Zero, decision.RetryAfter);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public void AfterTheBlockTheAccountStartsWithAFreshWindow()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);
        Fail(throttle, "alice", null, 10);
        clock.Advance(TimeSpan.FromMinutes(15));

        Fail(throttle, "alice", null, 9);
        Assert.True(throttle.Check("alice", null).Allowed);

        throttle.RecordFailure("alice", null);
        Assert.False(throttle.Check("alice", null).Allowed);
    }

    [Fact]
    public void FailuresOlderThanTheWindowStopCounting()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);
        Fail(throttle, "alice", null, 9);

        clock.Advance(TimeSpan.FromMinutes(15));
        throttle.RecordFailure("alice", null);

        Assert.True(throttle.Check("alice", null).Allowed);
    }

    [Fact]
    public void SuccessClearsTheAccountsFailures()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);
        Fail(throttle, "alice", null, 9);

        throttle.RecordSuccess("alice");
        Fail(throttle, "alice", null, 9);
        Assert.True(throttle.Check("alice", null).Allowed);

        throttle.RecordFailure("alice", null);
        Assert.False(throttle.Check("alice", null).Allowed);
    }

    [Fact]
    public void SuccessKeepsTheIpsFailures()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(new GameApiSignInThrottleOptions(IpFailureLimit: 5), clock);
        Fail(throttle, "alice", Ip, 4);

        throttle.RecordSuccess("alice");
        throttle.RecordFailure("bob", Ip);

        var decision = throttle.Check("carol", Ip);
        Assert.False(decision.Allowed);
        Assert.Equal("ip", decision.Reason);
    }

    [Fact]
    public void AHundredFailuresFromOneIpAcrossManyNamesBlockTheIp()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);

        for (int i = 0; i < 99; i++)
        {
            throttle.RecordFailure($"user{i}", Ip);
        }
        Assert.True(throttle.Check("newcomer", Ip).Allowed);

        throttle.RecordFailure("user99", Ip);
        var decision = throttle.Check("newcomer", Ip);

        Assert.False(decision.Allowed);
        Assert.Equal("ip", decision.Reason);
        Assert.Equal(TimeSpan.FromMinutes(15), decision.RetryAfter);
        Assert.True(throttle.Check("newcomer", "198.51.100.1").Allowed);
    }

    [Fact]
    public void PerIpLimitsDisabledNeverBlockByIp()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(new GameApiSignInThrottleOptions(PerIpLimitsEnabled: false), clock);

        for (int i = 0; i < 300; i++)
        {
            throttle.RecordFailure($"user{i}", Ip);
        }

        Assert.True(throttle.Check("newcomer", Ip).Allowed);
    }

    [Fact]
    public void UserNamesAreComparedCaseInsensitively()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);

        Fail(throttle, "Alice", null, 4);
        Fail(throttle, "ALICE", null, 3);
        Fail(throttle, " alice ", null, 3);

        Assert.False(throttle.Check("alice", null).Allowed);
        Assert.False(throttle.Check("aLiCe", null).Allowed);
    }

    [Fact]
    public void AnUnknownNameIsThrottledLikeARealOne()
    {
        // The throttle never looks accounts up, so a name with no account behaves exactly as one with an account.
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);

        Fail(throttle, "no-such-account-anywhere", null, 10);
        Fail(throttle, "alice", null, 10);

        var unknown = throttle.Check("no-such-account-anywhere", null);
        var known = throttle.Check("alice", null);
        Assert.False(unknown.Allowed);
        Assert.Equal(known, unknown);
    }

    [Fact]
    public void TheKeyCapDropsTheOldestKey()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(
            new GameApiSignInThrottleOptions(AccountFailureLimit: 2, MaxTrackedKeys: 3), clock);

        foreach (var name in new[] { "a", "b", "c", "d" })
        {
            throttle.RecordFailure(name, null);
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.Equal(3, throttle.TrackedAccountCount);

        // "a" was dropped, so its earlier failure no longer counts toward the limit of two.
        throttle.RecordFailure("a", null);
        Assert.True(throttle.Check("a", null).Allowed);
        Assert.Equal(3, throttle.TrackedAccountCount);

        // "c" was kept, so its second failure blocks it.
        throttle.RecordFailure("c", null);
        Assert.False(throttle.Check("c", null).Allowed);
        Assert.Equal(3, throttle.TrackedAccountCount);
    }

    [Fact]
    public async Task ConcurrentFailuresAreCountedExactly()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);
        using var start = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, 9)
            .Select(_ => Task.Run(() =>
            {
                start.Wait(TestContext.Current.CancellationToken);
                throttle.RecordFailure("alice", null);
            }, TestContext.Current.CancellationToken))
            .ToArray();
        start.Set();
        await Task.WhenAll(tasks);

        Assert.True(throttle.Check("alice", null).Allowed);

        throttle.RecordFailure("alice", null);
        Assert.False(throttle.Check("alice", null).Allowed);
    }

    [Fact]
    public void ConcurrentFailuresAcrossManyAccountsAreCountedExactly()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(new GameApiSignInThrottleOptions(PerIpLimitsEnabled: false), clock);
        var names = Enumerable.Range(0, 40).Select(i => $"player{i}").ToArray();

        Parallel.For(0, names.Length * 9,
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = TestContext.Current.CancellationToken },
            i => throttle.RecordFailure(names[i % names.Length], Ip));

        Assert.All(names, name => Assert.True(throttle.Check(name, Ip).Allowed));

        Parallel.ForEach(names,
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = TestContext.Current.CancellationToken },
            name => throttle.RecordFailure(name, Ip));

        Assert.All(names, name => Assert.False(throttle.Check(name, Ip).Allowed));
    }
}
