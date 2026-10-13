namespace GnollHackServer.Data.Security;

/// <summary>
/// The <c>AccountHold</c> configuration section, identical in the website's and Overseer's appsettings: when an
/// account deletion is held for administrator review, and who is told.
/// </summary>
public class AccountHoldOptions
{
    public static readonly IReadOnlyList<string> DefaultSeriousProviderCodes = new[] { "PROHIBITED_CONTENT", "BLOCKLIST", "invalid_prompt" };

    public bool AutoHoldEnabled { get; set; } = true;

    /// <summary>Serious provider safety events (summing each run's count) within the window that hold a deletion.</summary>
    public int AutoHoldEventThreshold { get; set; } = 3;

    public int AutoHoldWindowDays { get; set; } = 30;

    /// <summary>
    /// <c>ProviderSafetyBlock</c> / <c>ProviderPolicyRejection</c> codes that name prohibited content, compared
    /// case-insensitively. Null or empty: <see cref="DefaultSeriousProviderCodes"/>. Refusals, usage volume and limit
    /// refusals never count.
    /// </summary>
    public List<string>? SeriousProviderCodes { get; set; }

    /// <summary>A pending request at least this many days old is highlighted as overdue.</summary>
    public int ReviewHighlightDays { get; set; } = 25;

    /// <summary>Address emailed when a deletion is held for review; empty disables the email.</summary>
    public string? ReviewNotificationEmail { get; set; } = "gnollhack@hyvanmielenpelit.fi";

    /// <summary>At most one review email per user account within this many hours.</summary>
    public int ReviewEmailMinIntervalHours { get; set; } = 6;

    /// <summary>Website only: the Overseer admin page the review email links to; empty means no link.</summary>
    public string? OverseerAdminUrl { get; set; }

    public IReadOnlyList<string> EffectiveSeriousProviderCodes =>
        SeriousProviderCodes is { Count: > 0 } ? SeriousProviderCodes : DefaultSeriousProviderCodes;
}
