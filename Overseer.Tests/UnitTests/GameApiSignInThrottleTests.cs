using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GnollHackServer.Data.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The game API sign-in throttle: sliding failure windows per account and address, per account across addresses and
/// per IP, fixed-length blocks, refused checks that count nothing, and the tracked-key cap.
/// </summary>
public class GameApiSignInThrottleTests
{
    private const string Ip = "203.0.113.7";
    private const string OtherIp = "198.51.100.1";

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
    public void NineFailuresAreAllowedAndTheTenthOpensAFifteenMinutePairBlock()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);

        Fail(throttle, "alice", Ip, 9);
        Assert.True(throttle.Check("alice", Ip).Allowed);

        throttle.RecordFailure("alice", Ip);
        var decision = throttle.Check("alice", Ip);

        Assert.False(decision.Allowed);
        Assert.Equal("pair", decision.Reason);
        Assert.Equal(TimeSpan.FromMinutes(15), decision.RetryAfter);
    }

    [Fact]
    public void APairBlockLeavesTheSameAccountFromAnotherAddressAlone()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);

        Fail(throttle, "alice", Ip, 10);

        Assert.False(throttle.Check("alice", Ip).Allowed);
        Assert.True(throttle.Check("alice", OtherIp).Allowed);
        Assert.True(throttle.Check("bob", Ip).Allowed);
    }

    [Fact]
    public void FiftyFailuresAcrossAddressesOpenAThirtyMinuteAccountBlock()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);

        // Five from each of ten addresses: no pair reaches its limit.
        for (int address = 0; address < 10; address++)
        {
            int failures = address == 9 ? 4 : 5;
            Fail(throttle, "alice", $"192.0.2.{address}", failures);
        }
        Assert.True(throttle.Check("alice", "192.0.2.200").Allowed);

        throttle.RecordFailure("alice", "192.0.2.9");
        var decision = throttle.Check("alice", "192.0.2.200");

        Assert.False(decision.Allowed);
        Assert.Equal("account", decision.Reason);
        Assert.Equal(TimeSpan.FromMinutes(30), decision.RetryAfter);

        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.True(throttle.Check("alice", "192.0.2.200").Allowed);
    }

    [Fact]
    public void AccountFailuresOlderThanAnHourStopCounting()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);

        for (int address = 0; address < 7; address++)
        {
            Fail(throttle, "alice", $"192.0.2.{address}", 7);
        }
        clock.Advance(TimeSpan.FromMinutes(60));
        Fail(throttle, "alice", "192.0.2.100", 9);

        Assert.True(throttle.Check("alice", "192.0.2.200").Allowed);
    }

    [Fact]
    public void OneOtherAddressRetryingAsFastAsItMayNeverBlocksThePlayer()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);

        // Three hours of an attacker sending every failure the throttle lets through, minute by minute.
        for (int minute = 0; minute < 180; minute++)
        {
            while (throttle.Check("alice", Ip).Allowed)
            {
                throttle.RecordFailure("alice", Ip);
            }

            Assert.True(throttle.Check("alice", OtherIp).Allowed);
            clock.Advance(TimeSpan.FromMinutes(1));
        }
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
    public void RefusedChecksDuringAnAccountBlockDoNotExtendIt()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);
        for (int address = 0; address < 10; address++)
        {
            Fail(throttle, "alice", $"192.0.2.{address}", 5);
        }

        clock.Advance(TimeSpan.FromMinutes(10));
        for (int i = 0; i < 20; i++)
        {
            Assert.False(throttle.Check("alice", $"192.0.2.{100 + i}").Allowed);
        }
        Assert.Equal(TimeSpan.FromMinutes(20), throttle.Check("alice", OtherIp).RetryAfter);
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
    public void AfterTheBlockThePairStartsWithAFreshWindow()
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
    public void SuccessClearsThePairsFailures()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);
        Fail(throttle, "alice", null, 9);

        throttle.RecordSuccess("alice", null);
        Fail(throttle, "alice", null, 9);
        Assert.True(throttle.Check("alice", null).Allowed);

        throttle.RecordFailure("alice", null);
        Assert.False(throttle.Check("alice", null).Allowed);
    }

    [Fact]
    public void SuccessClearsTheAccountsFailures()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);
        for (int address = 0; address < 7; address++)
        {
            Fail(throttle, "alice", $"192.0.2.{address}", 7);
        }

        throttle.RecordSuccess("alice", OtherIp);
        Fail(throttle, "alice", "192.0.2.100", 9);

        Assert.True(throttle.Check("alice", "192.0.2.200").Allowed);
    }

    [Fact]
    public void SuccessLeavesAnotherAddressesPairBlockInPlace()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(time: clock);
        Fail(throttle, "alice", Ip, 10);

        throttle.RecordSuccess("alice", OtherIp);

        var decision = throttle.Check("alice", Ip);
        Assert.False(decision.Allowed);
        Assert.Equal("pair", decision.Reason);
    }

    [Fact]
    public void SuccessKeepsTheIpsFailures()
    {
        var clock = new FakeClock();
        var throttle = new GameApiSignInThrottle(new GameApiSignInThrottleOptions(IpFailureLimit: 5), clock);
        Fail(throttle, "alice", Ip, 4);

        throttle.RecordSuccess("alice", Ip);
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
        Assert.True(throttle.Check("newcomer", OtherIp).Allowed);
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
        Assert.Equal(3, throttle.TrackedPairCount);

        // "a" was dropped, so its earlier failure no longer counts toward the limit of two.
        throttle.RecordFailure("a", null);
        Assert.True(throttle.Check("a", null).Allowed);
        Assert.Equal(3, throttle.TrackedAccountCount);

        // "c" was kept, so its second failure blocks it.
        throttle.RecordFailure("c", null);
        Assert.False(throttle.Check("c", null).Allowed);
        Assert.Equal(3, throttle.TrackedAccountCount);
        Assert.Equal(3, throttle.TrackedPairCount);
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

    // ── Options from configuration ──────────────────────────────────────────────

    private static IConfiguration Config(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void FromConfigurationWithoutTheSectionGivesTheDefaults()
    {
        var options = GameApiSignInThrottleOptions.FromConfiguration(Config(new()));

        Assert.Equal(new GameApiSignInThrottleOptions(
            PairFailureLimit: 10, PairWindow: TimeSpan.FromMinutes(15), PairBlock: TimeSpan.FromMinutes(15),
            AccountFailureLimit: 50, AccountWindow: TimeSpan.FromMinutes(60), AccountBlock: TimeSpan.FromMinutes(30),
            IpFailureLimit: 100, IpWindow: TimeSpan.FromMinutes(15), IpBlock: TimeSpan.FromMinutes(15),
            PerIpLimitsEnabled: true), options);
    }

    [Fact]
    public void FromConfigurationReadsEveryKey()
    {
        var options = GameApiSignInThrottleOptions.FromConfiguration(Config(new()
        {
            { "GameApiSecurity:PairFailureLimit", "3" },
            { "GameApiSecurity:PairWindowMinutes", "4" },
            { "GameApiSecurity:PairBlockMinutes", "5" },
            { "GameApiSecurity:AccountFailureLimit", "6" },
            { "GameApiSecurity:AccountWindowMinutes", "7" },
            { "GameApiSecurity:AccountBlockMinutes", "8" },
            { "GameApiSecurity:IpFailureLimit", "9" },
            { "GameApiSecurity:IpWindowMinutes", "11" },
            { "GameApiSecurity:IpBlockMinutes", "12" },
            { "GameApiSecurity:PerIpLimitsEnabled", "false" }
        }));

        Assert.Equal(new GameApiSignInThrottleOptions(
            PairFailureLimit: 3, PairWindow: TimeSpan.FromMinutes(4), PairBlock: TimeSpan.FromMinutes(5),
            AccountFailureLimit: 6, AccountWindow: TimeSpan.FromMinutes(7), AccountBlock: TimeSpan.FromMinutes(8),
            IpFailureLimit: 9, IpWindow: TimeSpan.FromMinutes(11), IpBlock: TimeSpan.FromMinutes(12),
            PerIpLimitsEnabled: false), options);
    }

    [Fact]
    public void FromConfigurationKeepsTheDefaultForAnUnreadableValue()
    {
        var options = GameApiSignInThrottleOptions.FromConfiguration(Config(new()
        {
            { "GameApiSecurity:PairFailureLimit", "ten" },
            { "GameApiSecurity:PerIpLimitsEnabled", "maybe" }
        }));

        Assert.Equal(10, options.PairFailureLimit);
        Assert.True(options.PerIpLimitsEnabled);
    }
}
