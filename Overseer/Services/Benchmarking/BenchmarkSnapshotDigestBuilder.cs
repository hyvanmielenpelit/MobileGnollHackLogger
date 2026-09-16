namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Builds a board's digest: a deterministic extract of the labelled sections the difficulty
/// assessor needs, taken from the snapshot's own text. The map grid, the symbol legend and the
/// long reference lists are omitted; every other reader of a board receives the full text.
/// </summary>
public static class BenchmarkSnapshotDigestBuilder
{
    /// <summary>The cap the digest, the <c>DigestText</c> column and the editor all share.</summary>
    public const int MaxDigestChars = 6000;

    /// <summary>The first line of every extract, so a reader knows what the text is.</summary>
    public const string DigestHeaderLine =
        "Board digest (extract of the snapshot; the map grid and symbol legend are omitted):";

    /// <summary>The snapshot's HTML title, when the sanitizer carried it into the text.</summary>
    private const string SnapshotTitleLine = "GnollHack AI Snapshot";

    private const int PreambleLineCap = 3;
    private const int DefaultInventoryLineCap = 60;
    private const int DefaultNotableLocationsLineCap = 40;

    private const string InventoryHeader = "Inventory:";
    private const string NotableLocationsHeader = "Notable locations:";

    /// <param name="KeepLast">Emit the tail of the body rather than its head.</param>
    private sealed record KnownSection(string Header, int LineCap, bool KeepLast = false);

    /// <summary>The sections the digest emits, in the order it emits them.</summary>
    private static readonly KnownSection[] Sections =
    {
        new("Status:", int.MaxValue),
        new("Background:", int.MaxValue),
        new("Current Status:", int.MaxValue),
        new("Latest messages:", 5, KeepLast: true),
        new("Pets:", 10),
        new(NotableLocationsHeader, DefaultNotableLocationsLineCap),
        new(InventoryHeader, DefaultInventoryLineCap)
    };

    private static readonly HashSet<string> KnownHeaders =
        new(Sections.Select(s => s.Header), StringComparer.Ordinal);

    /// <summary>
    /// The digest for already-normalized board text. Never longer than
    /// <see cref="MaxDigestChars"/>, and never empty for non-empty input: text carrying none of
    /// the known headers falls back to the leading characters cut at a line boundary.
    /// </summary>
    public static string Build(string sanitizedText)
    {
        if (string.IsNullOrWhiteSpace(sanitizedText))
        {
            return string.Empty;
        }

        string[] lines = sanitizedText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var headerLines = new Dictionary<string, int>(StringComparer.Ordinal);
        int firstHeaderLine = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!IsSectionHeader(lines[i]))
            {
                continue;
            }

            if (firstHeaderLine < 0)
            {
                firstHeaderLine = i;
            }

            string header = lines[i].TrimEnd();
            if (KnownHeaders.Contains(header) && !headerLines.ContainsKey(header))
            {
                headerLines[header] = i;
            }
        }

        if (headerLines.Count == 0)
        {
            return Fallback(sanitizedText);
        }

        int inventoryCap = DefaultInventoryLineCap;
        int notableCap = DefaultNotableLocationsLineCap;
        string digest = Assemble(lines, firstHeaderLine, headerLines, inventoryCap, notableCap);

        // The inventory is the one section that grows without bound, so it gives way first.
        while (digest.Length > MaxDigestChars && inventoryCap > 1)
        {
            inventoryCap /= 2;
            digest = Assemble(lines, firstHeaderLine, headerLines, inventoryCap, notableCap);
        }

        while (digest.Length > MaxDigestChars && notableCap > 1)
        {
            notableCap /= 2;
            digest = Assemble(lines, firstHeaderLine, headerLines, inventoryCap, notableCap);
        }

        return digest.Length > MaxDigestChars ? HardTruncate(digest) : digest;
    }

    /// <summary>
    /// An unindented line of 3 to 48 characters ending in a colon, the rule the snapshot reader's
    /// <c>detectSections</c> uses. A line that is not a known header is an ordinary body line.
    /// </summary>
    private static bool IsSectionHeader(string line)
    {
        string trimmed = line.TrimEnd();
        return trimmed.Length >= 3
            && trimmed.Length <= 48
            && !char.IsWhiteSpace(trimmed[0])
            && trimmed.EndsWith(':');
    }

    private static string Assemble(
        string[] lines,
        int firstHeaderLine,
        IReadOnlyDictionary<string, int> headerLines,
        int inventoryCap,
        int notableCap)
    {
        var blocks = new List<List<string>> { new() { DigestHeaderLine } };

        var preamble = Preamble(lines, firstHeaderLine);
        if (preamble.Count > 0)
        {
            blocks.Add(preamble);
        }

        foreach (var section in Sections)
        {
            if (!headerLines.TryGetValue(section.Header, out int headerLine))
            {
                continue;
            }

            var body = SectionBody(lines, headerLine);
            if (body.Count == 0)
            {
                continue;
            }

            int cap = section.Header switch
            {
                InventoryHeader => inventoryCap,
                NotableLocationsHeader => notableCap,
                _ => section.LineCap
            };

            var block = new List<string> { section.Header };
            block.AddRange(Cap(body, cap, section.KeepLast));
            blocks.Add(block);
        }

        return string.Join("\n\n", blocks.Select(b => string.Join("\n", b)));
    }

    /// <summary>
    /// The lines before the first section header -- the version banner, the game and snapshot
    /// dates, and the character line -- without the snapshot's own title.
    /// </summary>
    private static List<string> Preamble(string[] lines, int firstHeaderLine)
    {
        int end = firstHeaderLine >= 0 ? firstHeaderLine : lines.Length;
        var body = new List<string>();
        for (int i = 0; i < end; i++)
        {
            string line = lines[i].TrimEnd();
            if (line.Length == 0)
            {
                continue;
            }
            if (body.Count == 0 && string.Equals(line, SnapshotTitleLine, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            body.Add(line);
        }

        return Cap(body, PreambleLineCap, keepLast: false);
    }

    /// <summary>
    /// A section's body: the lines after its header, up to the first blank line or the next known
    /// header. A sub-header the digest does not know, such as <c>Contents of the sack:</c>, is an
    /// ordinary body line.
    /// </summary>
    private static List<string> SectionBody(string[] lines, int headerLine)
    {
        var body = new List<string>();
        for (int i = headerLine + 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd();
            if (line.Length == 0)
            {
                break;
            }
            if (KnownHeaders.Contains(line))
            {
                break;
            }
            body.Add(line);
        }

        return body;
    }

    private static List<string> Cap(List<string> body, int cap, bool keepLast)
    {
        if (body.Count <= cap)
        {
            return body;
        }
        if (cap <= 0)
        {
            return new List<string> { $"  (+{body.Count} more lines)" };
        }

        var kept = keepLast ? body.Skip(body.Count - cap).ToList() : body.Take(cap).ToList();
        kept.Add($"  (+{body.Count - cap} more lines)");
        return kept;
    }

    private static string HardTruncate(string digest)
    {
        const string marker = "\n[DIGEST TRUNCATED]";
        int limit = MaxDigestChars - marker.Length;
        int lastNewline = digest.LastIndexOf('\n', limit);
        string head = lastNewline > 0 ? digest.Substring(0, lastNewline) : digest.Substring(0, limit);
        return head + marker;
    }

    /// <summary>
    /// The pre-extract behaviour, kept for a dump carrying none of the known headers: the leading
    /// characters cut back to the last line boundary, so no capture path yields an empty digest.
    /// </summary>
    private static string Fallback(string sanitizedText)
    {
        if (sanitizedText.Length <= MaxDigestChars)
        {
            return sanitizedText;
        }

        int lastNewline = sanitizedText.LastIndexOf('\n', MaxDigestChars);
        return lastNewline > 0
            ? sanitizedText.Substring(0, lastNewline).Trim()
            : sanitizedText.Substring(0, MaxDigestChars).Trim();
    }
}
