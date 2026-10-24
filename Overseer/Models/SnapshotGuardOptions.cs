namespace Overseer.Models;

using System;
using System.Globalization;
using Microsoft.Extensions.Configuration;

/// <summary>What the snapshot guard does with game text addressed to the AI.</summary>
public enum SnapshotGuardMode
{
    /// <summary>Nothing is scanned.</summary>
    Off,

    /// <summary>Every finding is recorded and alerted, and the text is still delivered.</summary>
    Observe,

    /// <summary>A text with a finding is withheld; the player and the administrator are told.</summary>
    Enforce,
}

/// <summary>The <c>SnapshotGuard</c> configuration section.</summary>
/// <param name="Mode">
/// The guard's mode until an administrator first sets it in the Security tab; once a
/// <c>SnapshotGuardSettings</c> row exists it is not consulted again.
/// </param>
/// <param name="AutoBlockEnabled">Whether repeated injection-level refusals of player-written text block the AI chat.</param>
/// <param name="AutoBlockDistinctDays">The distinct UTC days with such a refusal that block.</param>
/// <param name="AutoBlockWindowDays">The days, today included, those refusals are counted over.</param>
/// <param name="NotificationEmail">Where an automatic block is reported; null for no e-mail.</param>
public sealed record SnapshotGuardOptions(
    SnapshotGuardMode Mode,
    bool AutoBlockEnabled,
    int AutoBlockDistinctDays,
    int AutoBlockWindowDays,
    string? NotificationEmail)
{
    public const string SectionName = "SnapshotGuard";
    public const int DefaultAutoBlockDistinctDays = 3;
    public const int DefaultAutoBlockWindowDays = 30;

    /// <summary>
    /// Reads the section by hand rather than with <c>GetValue&lt;T&gt;</c>, which throws on an unconvertible
    /// value. A missing or unparseable <c>Mode</c> is <see cref="SnapshotGuardMode.Enforce"/>, the closed end; an
    /// unparseable <c>AutoBlockEnabled</c> is true; a day count outside its range is its default; a blank e-mail is none.
    /// </summary>
    public static SnapshotGuardOptions Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        var mode = ParseMode(section["Mode"]) ?? SnapshotGuardMode.Enforce;
        bool autoBlock = !bool.TryParse(section["AutoBlockEnabled"]?.Trim(), out bool parsedAutoBlock) || parsedAutoBlock;
        int days = Days(section["AutoBlockDistinctDays"], 1, 30, DefaultAutoBlockDistinctDays);
        int window = Days(section["AutoBlockWindowDays"], 1, 365, DefaultAutoBlockWindowDays);
        if (window < days) window = days;
        string? email = section["NotificationEmail"]?.Trim();

        return new SnapshotGuardOptions(mode, autoBlock, days, window, string.IsNullOrEmpty(email) ? null : email);
    }

    /// <summary>A mode name, ignoring case and surrounding whitespace; null for anything else.</summary>
    public static SnapshotGuardMode? ParseMode(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        foreach (var mode in Enum.GetValues<SnapshotGuardMode>())
        {
            if (string.Equals(trimmed, mode.ToString(), StringComparison.OrdinalIgnoreCase)) return mode;
        }
        return null;
    }

    private static int Days(string? value, int min, int max, int fallback)
        => int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed >= min && parsed <= max
            ? parsed
            : fallback;
}
