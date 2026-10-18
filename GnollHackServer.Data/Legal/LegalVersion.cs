using System.Globalization;
using System.Text.RegularExpressions;

namespace GnollHackServer.Data.Legal;

/// <summary>
/// Terms versions: <c>yyyy-MM-dd</c>, plus <c>.N</c> for the Nth revision of one day (N ≥ 2).
/// <see cref="Compare"/> is the only correct ordering; the strings do not sort ordinally.
/// </summary>
public static partial class LegalVersion
{
    private static readonly string[] DateFormats = { "d MMMM yyyy", "yyyy-MM-dd" };

    [GeneratedRegex(@"^\s*\*{0,2}Last updated:\*{0,2}\s*(?<date>.+?)(?:\s*\(revision\s+(?<rev>\d+)\))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LastUpdatedLine();

    /// <summary>Splits a version string into its date and revision; a version without a revision is revision 1.</summary>
    public static bool TryParse(string? version, out DateOnly date, out int revision)
    {
        date = default;
        revision = 1;
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        string text = version.Trim();
        int dot = text.IndexOf('.');
        string datePart = dot < 0 ? text : text[..dot];
        if (!DateOnly.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return false;
        }

        if (dot >= 0)
        {
            if (!int.TryParse(text[(dot + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out revision) || revision < 1)
            {
                revision = 1;
                return false;
            }
        }

        return true;
    }

    /// <summary>Orders by date, then by revision. An unparsable version sorts before every parsable one.</summary>
    public static int Compare(string? a, string? b)
    {
        bool okA = TryParse(a, out var dateA, out int revA);
        bool okB = TryParse(b, out var dateB, out int revB);

        if (okA && okB)
        {
            int byDate = dateA.CompareTo(dateB);
            return byDate != 0 ? byDate : revA.CompareTo(revB);
        }

        if (okA != okB)
        {
            return okA ? 1 : -1;
        }

        return string.CompareOrdinal(a, b);
    }

    /// <summary>The version string for a date and revision; revision 1 has no suffix.</summary>
    public static string Format(DateOnly date, int revision = 1)
    {
        string text = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return revision >= 2 ? text + "." + revision.ToString(CultureInfo.InvariantCulture) : text;
    }

    /// <summary>A version written the way the documents write it: <c>18 October 2026 (revision 2)</c>.</summary>
    public static string? FormatLabel(string? version)
    {
        if (!TryParse(version, out var date, out int revision))
        {
            return version;
        }

        string text = date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        return revision >= 2 ? $"{text} (revision {revision.ToString(CultureInfo.InvariantCulture)})" : text;
    }

    /// <summary>The outcome of reading one line as a <c>Last updated</c> line.</summary>
    public enum LineMatch
    {
        NoMatch,
        Parsed,

        /// <summary>The line is a <c>Last updated</c> line, but its date is in no accepted form.</summary>
        Unparsable
    }

    /// <summary>
    /// Reads <c>**Last updated:** 18 October 2026</c>, <c>Last updated: 2026-10-18</c> or either with
    /// <c>(revision N)</c>. English month names only, independent of the current culture.
    /// </summary>
    /// <param name="label">The text after <c>Last updated:</c>, without bold markers; set for an unparsable line too.</param>
    public static LineMatch TryParseLastUpdatedLine(string line, out string? version, out string? label)
    {
        version = null;
        label = null;

        var match = LastUpdatedLine().Match(line);
        if (!match.Success)
        {
            return LineMatch.NoMatch;
        }

        var dateGroup = match.Groups["date"];
        label = line[dateGroup.Index..].Replace("*", string.Empty).Trim();

        string dateText = dateGroup.Value.Replace("*", string.Empty).Trim();
        if (!DateOnly.TryParseExact(dateText, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return LineMatch.Unparsable;
        }

        int revision = 1;
        var revGroup = match.Groups["rev"];
        if (revGroup.Success && !int.TryParse(revGroup.Value, NumberStyles.None, CultureInfo.InvariantCulture, out revision))
        {
            return LineMatch.Unparsable;
        }

        version = Format(date, Math.Max(revision, 1));
        return LineMatch.Parsed;
    }
}
