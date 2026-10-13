using Microsoft.AspNetCore.Identity;

namespace GnollHackServer.Data.Security;

/// <summary>
/// Turns the result of a game-client password check into an outcome, and says whether it counts toward
/// <see cref="GameApiSignInThrottle"/>. Pure. Only a wrong password or an unknown user counts; a banned or unconfirmed
/// account is named only when the password was correct.
/// </summary>
public static class GameApiSignInClassifier
{
    /// <param name="result">The result of <c>CheckPasswordSignInAsync</c>.</param>
    /// <param name="passwordCorrect">
    /// Whether the password is correct, computed by the caller only when <paramref name="result"/> is IsNotAllowed:
    /// Identity refuses before it checks the password. Null when it was not computed.
    /// </param>
    public static (GameApiSignInOutcome Outcome, bool CountsAsFailure) Classify(SignInResult result, bool? passwordCorrect,
        bool isBanned, bool emailConfirmed)
    {
        if (result.Succeeded)
        {
            return (GameApiSignInOutcome.Success, false);
        }
        if (result.IsLockedOut)
        {
            return (GameApiSignInOutcome.LockedOut, false);
        }
        if (result.IsNotAllowed && passwordCorrect == true)
        {
            if (isBanned)
            {
                return (GameApiSignInOutcome.Banned, false);
            }
            if (!emailConfirmed)
            {
                return (GameApiSignInOutcome.EmailNotConfirmed, false);
            }
        }
        return (GameApiSignInOutcome.Failed, true);
    }
}
