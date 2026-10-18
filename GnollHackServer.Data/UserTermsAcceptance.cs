namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// One user's acceptance of one version of a Terms document. Append-only: never updated, and a
/// repeat of the same version is not written again. Holds no IP address and no user agent.
/// </summary>
public class UserTermsAcceptance
{
    public long Id { get; set; }

    [MaxLength(450)]
    public string AspNetUserId { get; set; } = "";
    public ApplicationUser AspNetUser { get; set; } = default!;

    /// <summary>"overseer-terms" or "account-terms".</summary>
    [MaxLength(64)]
    public string DocumentKey { get; set; } = "";

    /// <summary>"2026-10-18", or "2026-10-18.2" for a second revision on one day.</summary>
    [MaxLength(32)]
    public string Version { get; set; } = "";

    /// <summary>Lower-case hex SHA-256 of the file bytes served; null when the GnollHack app accepted.</summary>
    [MaxLength(64)]
    public string? ContentSha256 { get; set; }

    public DateTime AcceptedUtc { get; set; }

    /// <summary>"Web", "GnollHackApp" or "AccountSite".</summary>
    [MaxLength(16)]
    public string Source { get; set; } = "";

    /// <summary>The app version, for GnollHackApp acceptances.</summary>
    [MaxLength(64)]
    public string? ClientVersion { get; set; }
}
