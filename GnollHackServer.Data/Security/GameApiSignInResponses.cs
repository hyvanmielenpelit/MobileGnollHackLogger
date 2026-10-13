using System.Globalization;

namespace GnollHackServer.Data.Security;

/// <summary>Why a game API sign-in did not succeed.</summary>
public enum GameApiSignInOutcome
{
    /// <summary>Wrong password, unknown user or unconfirmed account.</summary>
    Failed,
    Throttled,
    LockedOut,
    RequiresTwoFactor,
    AntiForgeryMismatch
}

/// <summary>A game API response: status, a plain-text body (always present), and an optional Retry-After in seconds.</summary>
public sealed record GameApiResponse(int StatusCode, string Body, int? RetryAfterSeconds)
{
    public const string ContentType = "text/plain; charset=utf-8";
}

/// <summary>
/// The one place a game API sign-in outcome becomes a response. Every refusal a player can trigger is a 403, the one
/// status every GnollHack client stops on; 2FA keeps 412 and an antiforgery mismatch keeps 401. Bans (423) stay in
/// the controllers.
/// </summary>
public static class GameApiSignInResponses
{
    public static GameApiResponse For(GameApiSignInOutcome outcome, TimeSpan? retryAfter = null)
    {
        int? seconds = retryAfter is TimeSpan wait ? (int)Math.Max(1, Math.Ceiling(wait.TotalSeconds)) : null;
        int minutes = retryAfter is TimeSpan w ? (int)Math.Max(1, Math.Ceiling(w.TotalMinutes)) : 1;
        string minutesText = minutes.ToString(CultureInfo.InvariantCulture) + (minutes == 1 ? " minute" : " minutes");

        return outcome switch
        {
            GameApiSignInOutcome.Throttled => new GameApiResponse(403,
                $"Too many failed sign-in attempts. Try again in {minutesText}.", seconds),
            GameApiSignInOutcome.LockedOut => new GameApiResponse(403,
                $"This account is temporarily locked. Try again in {minutesText}.", seconds),
            GameApiSignInOutcome.RequiresTwoFactor => new GameApiResponse(412,
                "This account requires two-factor authentication.", null),
            GameApiSignInOutcome.AntiForgeryMismatch => new GameApiResponse(401,
                "AntiForgeryToken is invalid.", null),
            _ => new GameApiResponse(403, "Sign-in failed.", null)
        };
    }
}
