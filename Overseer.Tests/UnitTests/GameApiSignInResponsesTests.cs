using System;
using GnollHackServer.Data.Security;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The one mapping from a game API sign-in outcome to a response: every refusal a player can trigger is a 403,
/// 2FA is a 412 and an antiforgery mismatch a 401, each with a plain-text body.
/// </summary>
public class GameApiSignInResponsesTests
{
    [Fact]
    public void TheContentTypeIsPlainUtf8Text()
    {
        Assert.Equal("text/plain; charset=utf-8", GameApiResponse.ContentType);
    }

    [Fact]
    public void ThrottledIsA403WithMinutesAndRetryAfter()
    {
        var response = GameApiSignInResponses.For(GameApiSignInOutcome.Throttled, TimeSpan.FromMinutes(15));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal("Too many failed sign-in attempts. Try again in 15 minutes.", response.Body);
        Assert.Equal(900, response.RetryAfterSeconds);
    }

    [Fact]
    public void LockedOutIsA403AndNeverA423()
    {
        var response = GameApiSignInResponses.For(GameApiSignInOutcome.LockedOut, TimeSpan.FromMinutes(5));

        Assert.Equal(403, response.StatusCode);
        Assert.NotEqual(423, response.StatusCode);
        Assert.Equal("This account is temporarily locked. Try again in 5 minutes.", response.Body);
        Assert.Equal(300, response.RetryAfterSeconds);
    }

    [Fact]
    public void LockedOutWithoutAnEndSaysOneMinuteAndSendsNoRetryAfter()
    {
        var response = GameApiSignInResponses.For(GameApiSignInOutcome.LockedOut);

        Assert.Equal(403, response.StatusCode);
        Assert.Equal("This account is temporarily locked. Try again in 1 minute.", response.Body);
        Assert.Null(response.RetryAfterSeconds);
    }

    [Theory]
    [InlineData(30.0, 30, "1 minute")]
    [InlineData(60.0, 60, "1 minute")]
    [InlineData(61.0, 61, "2 minutes")]
    [InlineData(840.5, 841, "15 minutes")]
    [InlineData(0.2, 1, "1 minute")]
    [InlineData(0.0, 1, "1 minute")]
    public void RetryAfterAndMinutesAreRoundedUp(double seconds, int expectedSeconds, string expectedMinutes)
    {
        var response = GameApiSignInResponses.For(GameApiSignInOutcome.Throttled, TimeSpan.FromSeconds(seconds));

        Assert.Equal(expectedSeconds, response.RetryAfterSeconds);
        Assert.Equal($"Too many failed sign-in attempts. Try again in {expectedMinutes}.", response.Body);
    }

    [Fact]
    public void TwoFactorIsA412WithoutRetryAfter()
    {
        var response = GameApiSignInResponses.For(GameApiSignInOutcome.RequiresTwoFactor, TimeSpan.FromMinutes(3));

        Assert.Equal(412, response.StatusCode);
        Assert.Equal("This account requires two-factor authentication.", response.Body);
        Assert.Null(response.RetryAfterSeconds);
    }

    [Fact]
    public void AntiForgeryMismatchIsA401()
    {
        var response = GameApiSignInResponses.For(GameApiSignInOutcome.AntiForgeryMismatch);

        Assert.Equal(401, response.StatusCode);
        Assert.Equal("AntiForgeryToken is invalid.", response.Body);
        Assert.Null(response.RetryAfterSeconds);
    }

    [Fact]
    public void FailedIsA403WithAGenericBody()
    {
        var response = GameApiSignInResponses.For(GameApiSignInOutcome.Failed);

        Assert.Equal(403, response.StatusCode);
        Assert.Equal("Sign-in failed.", response.Body);
        Assert.Null(response.RetryAfterSeconds);
    }

    [Fact]
    public void BannedIsA403WithoutRetryAfter()
    {
        var response = GameApiSignInResponses.For(GameApiSignInOutcome.Banned, TimeSpan.FromMinutes(3));

        // A 403, so GnollHack stops posting instead of looping on the controllers' 423.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal("This account has been banned.", response.Body);
        Assert.Null(response.RetryAfterSeconds);
    }

    [Fact]
    public void EmailNotConfirmedIsA403WithoutRetryAfter()
    {
        var response = GameApiSignInResponses.For(GameApiSignInOutcome.EmailNotConfirmed);

        Assert.Equal(403, response.StatusCode);
        Assert.Equal("Confirm your email address before signing in to GnollHack online features.", response.Body);
        Assert.Null(response.RetryAfterSeconds);
    }

    [Fact]
    public void ALockoutOfADayOrLessIsStatedInMinutes()
    {
        var now = new DateTimeOffset(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);
        var response = GameApiSignInResponses.For(GameApiSignInOutcome.LockedOut, TimeSpan.FromHours(24), now);

        Assert.Equal("This account is temporarily locked. Try again in 1440 minutes.", response.Body);
        Assert.Equal(86400, response.RetryAfterSeconds);
    }

    [Fact]
    public void ALockoutLongerThanADayIsStatedAsItsEndInUtc()
    {
        var now = new DateTimeOffset(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);
        var response = GameApiSignInResponses.For(GameApiSignInOutcome.LockedOut, TimeSpan.FromDays(7), now);

        Assert.Equal(403, response.StatusCode);
        Assert.Equal("This account is locked until 2026-10-20 15:00 UTC.", response.Body);
        Assert.Equal(7 * 86400, response.RetryAfterSeconds);
    }

    [Fact]
    public void ALongLockoutEndIsRoundedUpToTheMinuteAndIgnoresTheCallersOffset()
    {
        var now = new DateTimeOffset(2026, 10, 13, 18, 0, 0, TimeSpan.FromHours(3));
        var response = GameApiSignInResponses.For(GameApiSignInOutcome.LockedOut,
            TimeSpan.FromDays(2) + TimeSpan.FromSeconds(20), now);

        Assert.Equal("This account is locked until 2026-10-15 15:01 UTC.", response.Body);
    }

    [Fact]
    public void TheRetryAfterClampDoesNotOverflowAtTheLargestLockout()
    {
        var now = new DateTimeOffset(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);
        var response = GameApiSignInResponses.For(GameApiSignInOutcome.LockedOut, DateTimeOffset.MaxValue - now, now);

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(366 * 86400, response.RetryAfterSeconds);
        Assert.Equal("This account is locked until 2027-10-14 15:00 UTC.", response.Body);
    }

    [Fact]
    public void TheRetryAfterClampAppliesToEveryOutcomeThatSendsOne()
    {
        var response = GameApiSignInResponses.For(GameApiSignInOutcome.Throttled, TimeSpan.MaxValue);

        Assert.Equal(366 * 86400, response.RetryAfterSeconds);
        Assert.Equal($"Too many failed sign-in attempts. Try again in {366 * 24 * 60} minutes.", response.Body);
    }
}
