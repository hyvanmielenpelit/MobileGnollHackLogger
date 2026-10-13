using System;
using System.Collections.Generic;
using GnollHackServer.Data.Security;
using MobileGnollHackLogger.Data;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// When an account deletion is held for review: an active manual hold, or enough serious provider safety and
/// policy events in the window. Nothing else counts.
/// </summary>
public class AccountHoldEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private static SecurityEvent Event(SecurityEventKind kind, string? code, DateTime at, int count = 1,
        string? provider = "Google", DateTime? lastAt = null) => new()
    {
        Kind = kind,
        Code = code,
        Provider = provider,
        Count = count,
        OccurredUtc = at,
        LastOccurredUtc = lastAt ?? at,
        AspNetUserId = "held-user"
    };

    private static SecurityEvent Serious(DateTime at, int count = 1) =>
        Event(SecurityEventKind.ProviderSafetyBlock, "PROHIBITED_CONTENT", at, count);

    private static AccountHold Hold(DateTime placed, DateTime? released = null) => new()
    {
        AspNetUserId = "held-user",
        Reason = "Investigating an abuse report",
        PlacedByUserId = "admin-id",
        PlacedUtc = placed,
        ReleasedUtc = released
    };

    private static AccountHoldVerdict Evaluate(AccountHold? hold, IReadOnlyList<SecurityEvent> events,
        AccountHoldOptions? options = null, string? placedBy = "admin-name") =>
        AccountHoldEvaluator.Evaluate(hold, events, options ?? new AccountHoldOptions(), Now, placedBy);

    [Fact]
    public void AnActiveManualHoldHolds()
    {
        var verdict = Evaluate(Hold(new DateTime(2026, 10, 14, 9, 30, 0, DateTimeKind.Utc)), Array.Empty<SecurityEvent>());

        Assert.True(verdict.IsHeld);
        Assert.True(verdict.Manual);
        Assert.False(verdict.Automatic);
        Assert.Equal("Manual hold placed by admin-name on 2026-10-14: Investigating an abuse report", verdict.Reasons);
    }

    [Fact]
    public void AManualHoldWithoutAKnownAdminNamesAnAdministrator()
    {
        var verdict = Evaluate(Hold(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc)), Array.Empty<SecurityEvent>(), placedBy: null);

        Assert.Equal("Manual hold placed by an administrator on 2026-10-02: Investigating an abuse report", verdict.Reasons);
    }

    [Fact]
    public void AReleasedHoldDoesNotHold()
    {
        var verdict = Evaluate(Hold(Now.AddDays(-3), released: Now.AddDays(-1)), Array.Empty<SecurityEvent>());

        Assert.False(verdict.IsHeld);
        Assert.False(verdict.Manual);
        Assert.Equal(string.Empty, verdict.Reasons);
    }

    [Fact]
    public void ThreeSeriousEventsInThirtyDaysHold()
    {
        var verdict = Evaluate(null, new[] { Serious(Now.AddDays(-20)), Serious(Now.AddDays(-10)), Serious(Now.AddDays(-1)) });

        Assert.True(verdict.IsHeld);
        Assert.True(verdict.Automatic);
        Assert.False(verdict.Manual);
    }

    [Fact]
    public void TwoSeriousEventsDoNotHold()
    {
        var verdict = Evaluate(null, new[] { Serious(Now.AddDays(-10)), Serious(Now.AddDays(-1)) });

        Assert.False(verdict.IsHeld);
        Assert.Equal(string.Empty, verdict.Reasons);
    }

    [Fact]
    public void ARunWithCountThreeCountsAsThree()
    {
        var verdict = Evaluate(null, new[] { Serious(Now.AddDays(-2), count: 3) });

        Assert.True(verdict.IsHeld);
        Assert.True(verdict.Automatic);
    }

    [Fact]
    public void CodesAreComparedCaseInsensitively()
    {
        var verdict = Evaluate(null, new[]
        {
            Event(SecurityEventKind.ProviderSafetyBlock, "prohibited_content", Now.AddDays(-3)),
            Event(SecurityEventKind.ProviderSafetyBlock, "Blocklist", Now.AddDays(-2)),
            Event(SecurityEventKind.ProviderPolicyRejection, "INVALID_PROMPT", Now.AddDays(-1), provider: "OpenAI")
        });

        Assert.True(verdict.IsHeld);
    }

    [Fact]
    public void EventsOutsideTheWindowNeverCount()
    {
        var verdict = Evaluate(null, new[]
        {
            Serious(Now.AddDays(-45), count: 5),
            Serious(Now.AddDays(-31)),
            Serious(Now.AddDays(-1)),
            Serious(Now.AddDays(1))
        });

        Assert.False(verdict.IsHeld);
    }

    [Fact]
    public void ARunThatEndsInsideTheWindowCounts()
    {
        var verdict = Evaluate(null, new[]
        {
            Event(SecurityEventKind.ProviderSafetyBlock, "PROHIBITED_CONTENT", Now.AddDays(-31), count: 3, lastAt: Now.AddDays(-29))
        });

        Assert.True(verdict.IsHeld);
    }

    [Theory]
    [InlineData(SecurityEventKind.ProviderRefusal)]
    [InlineData(SecurityEventKind.QuotaRefused)]
    [InlineData(SecurityEventKind.RateLimitRejected)]
    [InlineData(SecurityEventKind.AccessDenied)]
    [InlineData(SecurityEventKind.LoginFailed)]
    public void OtherKindsNeverCountEvenWithASeriousCode(SecurityEventKind kind)
    {
        var verdict = Evaluate(null, new[] { Event(kind, "PROHIBITED_CONTENT", Now.AddDays(-1), count: 50) });

        Assert.False(verdict.IsHeld);
    }

    [Fact]
    public void ANonSeriousCodeNeverCounts()
    {
        var verdict = Evaluate(null, new[] { Event(SecurityEventKind.ProviderSafetyBlock, "SAFETY", Now.AddDays(-1), count: 10) });

        Assert.False(verdict.IsHeld);
    }

    [Fact]
    public void ConfiguredSeriousCodesReplaceTheDefaults()
    {
        var options = new AccountHoldOptions { SeriousProviderCodes = new List<string> { "SAFETY" } };

        var safety = Evaluate(null, new[] { Event(SecurityEventKind.ProviderSafetyBlock, "SAFETY", Now.AddDays(-1), count: 3) }, options);
        var prohibited = Evaluate(null, new[] { Serious(Now.AddDays(-1), count: 3) }, options);

        Assert.True(safety.IsHeld);
        Assert.False(prohibited.IsHeld);
    }

    [Fact]
    public void AutoHoldDisabledLeavesOnlyManualHolds()
    {
        var options = new AccountHoldOptions { AutoHoldEnabled = false };
        var events = new[] { Serious(Now.AddDays(-1), count: 10) };

        var withoutHold = Evaluate(null, events, options);
        var withHold = Evaluate(Hold(Now.AddDays(-1)), events, options);

        Assert.False(withoutHold.IsHeld);
        Assert.True(withHold.IsHeld);
        Assert.True(withHold.Manual);
        Assert.False(withHold.Automatic);
        Assert.DoesNotContain("Automatic", withHold.Reasons);
    }

    [Fact]
    public void TheAutomaticReasonNamesTheCodesProvidersAndDates()
    {
        var verdict = Evaluate(null, new[]
        {
            Event(SecurityEventKind.ProviderSafetyBlock, "PROHIBITED_CONTENT", new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), count: 2),
            Event(SecurityEventKind.ProviderSafetyBlock, "PROHIBITED_CONTENT", new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc)),
            Event(SecurityEventKind.ProviderPolicyRejection, "invalid_prompt", new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc), provider: "OpenAI")
        });

        Assert.Equal(
            "Automatic: 4 serious provider safety events in 30 days — PROHIBITED_CONTENT ×3 (Google, 2026-10-01 to 2026-10-05), invalid_prompt ×1 (OpenAI, 2026-10-10).",
            verdict.Reasons);
    }

    [Fact]
    public void AnEventWithoutAProviderIsAttributedToAnUnknownProvider()
    {
        var verdict = Evaluate(null, new[]
        {
            Event(SecurityEventKind.ProviderSafetyBlock, "BLOCKLIST", new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc), count: 3, provider: null)
        });

        Assert.Equal("Automatic: 3 serious provider safety events in 30 days — BLOCKLIST ×3 (unknown provider, 2026-10-12).", verdict.Reasons);
    }

    [Fact]
    public void ManualAndAutomaticReasonsAreBothListed()
    {
        var verdict = Evaluate(Hold(new DateTime(2026, 10, 14, 9, 30, 0, DateTimeKind.Utc)), new[]
        {
            Event(SecurityEventKind.ProviderSafetyBlock, "PROHIBITED_CONTENT", new DateTime(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc), count: 3)
        });

        Assert.True(verdict.Manual);
        Assert.True(verdict.Automatic);
        Assert.Equal(
            "Manual hold placed by admin-name on 2026-10-14: Investigating an abuse report\n" +
            "Automatic: 3 serious provider safety events in 30 days — PROHIBITED_CONTENT ×3 (Google, 2026-10-13).",
            verdict.Reasons);
    }
}
