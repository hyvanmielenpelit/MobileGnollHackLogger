namespace Overseer.Services.Benchmarking;

using System;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;

/// <summary>
/// The fields a GnollHack AI snapshot's own header states: the <c>Snapshot format: N</c> the game
/// wrote the board in, the <c>GnollHack Version</c> that captured it, and the printed
/// <c>snapshot at</c> time. Any of the three is null when the header does not state it.
/// </summary>
public sealed record BenchmarkSnapshotHeader(int? SnapshotFormatVersion, string? GnollHackVersion, string? SnapshotTimestamp);

/// <summary>
/// Reads <see cref="BenchmarkSnapshotHeader"/> out of a board's raw text and applies it to a
/// <c>BenchmarkGameSnapshot</c>. A board is operator-supplied text, so every pattern is anchored to
/// the first 12 lines, and a miss or a match timeout yields null for that field rather than
/// throwing.
/// </summary>
public static class BenchmarkSnapshotHeaderParser
{
    private const int HeaderLineCount = 12;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(200);

    private static readonly Regex FormatVersionPattern = new(
        @"Snapshot format:\s*(\d+)", RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex GnollHackVersionPattern = new(
        @"GnollHack Version (\S+ \(Build \d+\))", RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex SnapshotTimestampPattern = new(
        @"snapshot at (\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})", RegexOptions.CultureInvariant, MatchTimeout);

    /// <summary>Reads the header fields from the first 12 lines of <paramref name="text"/>. Never throws.</summary>
    public static BenchmarkSnapshotHeader Parse(string? text)
    {
        string header = FirstLines(text, HeaderLineCount);

        return new BenchmarkSnapshotHeader(
            ParseInt(FormatVersionPattern, header),
            ParseString(GnollHackVersionPattern, header),
            ParseString(SnapshotTimestampPattern, header));
    }

    /// <summary>
    /// Stamps <paramref name="snapshot"/>'s <see cref="BenchmarkGameSnapshot.SnapshotFormatVersion"/>
    /// and <see cref="BenchmarkGameSnapshot.BoardHeaderTimestamp"/> from <paramref name="text"/>'s
    /// header, always, including to null when the header states neither. Its
    /// <see cref="BenchmarkGameSnapshot.SourceGnollHackVersion"/> is overwritten only when the
    /// header names a version and either the snapshot carries none yet or <paramref
    /// name="textReplaced"/> is true; a header with no version line never clears an existing value.
    /// </summary>
    public static void ApplyTo(BenchmarkGameSnapshot snapshot, string text, bool textReplaced)
    {
        var header = Parse(text);

        snapshot.SnapshotFormatVersion = header.SnapshotFormatVersion;
        snapshot.BoardHeaderTimestamp = Truncate(header.SnapshotTimestamp, 32);

        if (header.GnollHackVersion != null
            && (textReplaced || string.IsNullOrWhiteSpace(snapshot.SourceGnollHackVersion)))
        {
            snapshot.SourceGnollHackVersion = Truncate(header.GnollHackVersion, 64);
        }
    }

    private static string FirstLines(string? text, int count)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        int take = Math.Min(count, lines.Length);
        return string.Join('\n', lines, 0, take);
    }

    private static int? ParseInt(Regex pattern, string header)
    {
        string? value = ParseString(pattern, header);
        return value != null && int.TryParse(value, out int result) ? result : null;
    }

    private static string? ParseString(Regex pattern, string header)
    {
        try
        {
            Match match = pattern.Match(header);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        return value != null && value.Length > maxLength ? value[..maxLength] : value;
    }
}
