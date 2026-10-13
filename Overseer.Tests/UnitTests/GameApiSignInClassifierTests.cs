using GnollHackServer.Data.Security;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The classification of a game-client password check, row by row: only a wrong password counts toward the throttle,
/// and a banned or unconfirmed account is named only when the password was correct.
/// </summary>
public class GameApiSignInClassifierTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void SucceededIsSuccessAndDoesNotCount(bool isBanned, bool emailConfirmed)
    {
        var (outcome, counts) = GameApiSignInClassifier.Classify(SignInResult.Success, null, isBanned, emailConfirmed);

        Assert.Equal(GameApiSignInOutcome.Success, outcome);
        Assert.False(counts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void LockedOutIsLockedOutAndDoesNotCount(bool? passwordCorrect)
    {
        var (outcome, counts) = GameApiSignInClassifier.Classify(SignInResult.LockedOut, passwordCorrect, isBanned: true,
            emailConfirmed: false);

        Assert.Equal(GameApiSignInOutcome.LockedOut, outcome);
        Assert.False(counts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NotAllowedWithTheCorrectPasswordAndABanIsBannedAndDoesNotCount(bool emailConfirmed)
    {
        var (outcome, counts) = GameApiSignInClassifier.Classify(SignInResult.NotAllowed, true, isBanned: true, emailConfirmed);

        Assert.Equal(GameApiSignInOutcome.Banned, outcome);
        Assert.False(counts);
    }

    [Fact]
    public void NotAllowedWithTheCorrectPasswordAndAnUnconfirmedEmailIsEmailNotConfirmedAndDoesNotCount()
    {
        var (outcome, counts) = GameApiSignInClassifier.Classify(SignInResult.NotAllowed, true, isBanned: false,
            emailConfirmed: false);

        Assert.Equal(GameApiSignInOutcome.EmailNotConfirmed, outcome);
        Assert.False(counts);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void NotAllowedWithAWrongPasswordIsFailedAndCounts(bool isBanned, bool emailConfirmed)
    {
        // Neither the ban nor the unconfirmed email is told to someone without the password.
        var (outcome, counts) = GameApiSignInClassifier.Classify(SignInResult.NotAllowed, false, isBanned, emailConfirmed);

        Assert.Equal(GameApiSignInOutcome.Failed, outcome);
        Assert.True(counts);
    }

    [Fact]
    public void NotAllowedWithoutAPasswordCheckIsFailedAndCounts()
    {
        var (outcome, counts) = GameApiSignInClassifier.Classify(SignInResult.NotAllowed, null, isBanned: true,
            emailConfirmed: false);

        Assert.Equal(GameApiSignInOutcome.Failed, outcome);
        Assert.True(counts);
    }

    [Fact]
    public void NotAllowedWithTheCorrectPasswordForAnotherReasonIsFailedAndCounts()
    {
        var (outcome, counts) = GameApiSignInClassifier.Classify(SignInResult.NotAllowed, true, isBanned: false,
            emailConfirmed: true);

        Assert.Equal(GameApiSignInOutcome.Failed, outcome);
        Assert.True(counts);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AnyOtherFailureIsFailedAndCounts(bool isBanned, bool emailConfirmed)
    {
        var (outcome, counts) = GameApiSignInClassifier.Classify(SignInResult.Failed, null, isBanned, emailConfirmed);

        Assert.Equal(GameApiSignInOutcome.Failed, outcome);
        Assert.True(counts);
    }

    [Fact]
    public void TwoFactorRequiredIsFailedAndCounts()
    {
        var (outcome, counts) = GameApiSignInClassifier.Classify(SignInResult.TwoFactorRequired, null, isBanned: false,
            emailConfirmed: true);

        Assert.Equal(GameApiSignInOutcome.Failed, outcome);
        Assert.True(counts);
    }
}
