using System.Globalization;

namespace GnollHackServer.Data.Security;

/// <summary>The outcome of a game API sign-in; every value but <see cref="Success"/> is a refusal.</summary>
public enum GameApiSignInOutcome
{
    /// <summary>Wrong password or unknown user.</summary>
    Failed,
    Throttled,
    LockedOut,
    RequiresTwoFactor,
    AntiForgeryMismatch,

    /// <summary>The password was correct; <see cref="GameApiSignInClassifier"/> reports it, and it has no response.</summary>
    Success,

    /// <summary>The account is banned; told only to a caller with the correct password.</summary>
    Banned,

    /// <summary>The email address is not confirmed; told only to a caller with the correct password.</summary>
    EmailNotConfirmed
}

/// <summary>A game API response: status, a plain-text body (always present), and an optional Retry-After in seconds.</summary>
public sealed record GameApiResponse(int StatusCode, string Body, int? RetryAfterSeconds)
{
    public const string ContentType = "text/plain; charset=utf-8";
}

/// <summary>
/// The one place a game API sign-in outcome becomes a response. Every refusal a player can trigger is a 403, the one
/// status every GnollHack client stops on; 2FA keeps 412 and an antiforgery mismatch keeps 401. The controllers' own
/// ban checks (423) remain behind sign-in.
/// </summary>
public static class GameApiSignInResponses
{
    /// <summary>The longest wait a response states; longer waits are cut to it before any conversion.</summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromDays(366);

    /// <summary>A lockout longer than this is stated as an end time rather than in minutes.</summary>
    public static readonly TimeSpan LongLockoutThreshold = TimeSpan.FromHours(24);

    /// <param name="nowUtc">The time a long lockout's end is computed from; the current time when null.</param>
    public static GameApiResponse For(GameApiSignInOutcome outcome, TimeSpan? retryAfter = null, DateTimeOffset? nowUtc = null)
    {
        TimeSpan? clamped = retryAfter is TimeSpan raw ? (raw > MaxRetryAfter ? MaxRetryAfter : raw) : null;
        int? seconds = clamped is TimeSpan wait ? (int)Math.Max(1, Math.Ceiling(wait.TotalSeconds)) : null;
        int minutes = clamped is TimeSpan w ? (int)Math.Max(1, Math.Ceiling(w.TotalMinutes)) : 1;
        string minutesText = minutes.ToString(CultureInfo.InvariantCulture) + (minutes == 1 ? " minute" : " minutes");

        return outcome switch
        {
            GameApiSignInOutcome.Throttled => new GameApiResponse(403,
                $"Too many failed sign-in attempts. Try again in {minutesText}.", seconds),
            GameApiSignInOutcome.LockedOut when clamped is TimeSpan longWait && longWait > LongLockoutThreshold =>
                new GameApiResponse(403, $"This account is locked until {FormatUntil(nowUtc ?? DateTimeOffset.UtcNow, longWait)}.", seconds),
            GameApiSignInOutcome.LockedOut => new GameApiResponse(403,
                $"This account is temporarily locked. Try again in {minutesText}.", seconds),
            GameApiSignInOutcome.Banned => new GameApiResponse(403,
                "This account has been banned.", null),
            GameApiSignInOutcome.EmailNotConfirmed => new GameApiResponse(403,
                "Confirm your email address before signing in to GnollHack online features.", null),
            GameApiSignInOutcome.RequiresTwoFactor => new GameApiResponse(412,
                "This account requires two-factor authentication.", null),
            GameApiSignInOutcome.AntiForgeryMismatch => new GameApiResponse(401,
                "AntiForgeryToken is invalid.", null),
            _ => new GameApiResponse(403, "Sign-in failed.", null)
        };
    }

    /// <summary>The end of the wait in UTC, rounded up to the minute, e.g. "2026-10-20 15:00 UTC".</summary>
    private static string FormatUntil(DateTimeOffset nowUtc, TimeSpan wait)
    {
        DateTime until = nowUtc.UtcDateTime + wait;
        long remainder = until.Ticks % TimeSpan.TicksPerMinute;
        if (remainder != 0)
        {
            until = until.AddTicks(TimeSpan.TicksPerMinute - remainder);
        }
        return until.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
    }
}
