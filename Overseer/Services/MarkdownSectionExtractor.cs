using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Overseer.Services;

/// <summary>
/// Resolves a <c>section</c> request against a Markdown article's headings, for both the GnollHack
/// and the NetHack wiki services, so <c>wiki_view</c> and <c>nethack_wiki_view</c> answer the same
/// request the same way.
/// </summary>
internal static class MarkdownSectionExtractor
{
    // The bound the section-miss marker line's heading list is truncated at.
    internal const int SectionMissHeadingListMaxChars = 600;

    /// <summary>The section-miss marker line's opening, up to the requested section's name.</summary>
    internal const string SectionMissMarkerOpening = "[Section '";

    /// <summary>The section-miss marker line's ending when the whole article follows it.</summary>
    internal const string SectionMissFullTextEnding = " Returning full text.]";

    /// <summary>What separates the section-miss marker line from the whole article after it.</summary>
    internal const string SectionMissSeparator = "\n\n";

    /// <summary>
    /// The breadcrumb separator <c>wiki_search</c>'s snippet header joins heading levels with
    /// (<c>A › B</c>), so a <c>section</c> argument copied from that header carries it too.
    /// </summary>
    private const char BreadcrumbSeparator = '›';

    /// <summary>
    /// The requested section's text: the matched heading line, then every line up to but not
    /// including the next heading of the same or a higher level. Matching runs in three passes over
    /// the article's headings — case-insensitive equality on the heading text, then equality with
    /// both sides normalised, then a normalised request contained in exactly one normalised heading —
    /// so a heading carrying an emoji prefix (<c>## 🔮 Elbereth</c>) answers a request for
    /// <c>Elbereth</c>, <c>Spell failure</c> answers <c>Minimum spell failure rates</c>, and an
    /// earlier pass always beats a later one. The first two passes take the first heading they
    /// match; the third takes a heading only when it is the sole one containing the request. When a
    /// request carries the <c>A › B</c> breadcrumb <c>wiki_search</c>'s snippet header prints, and
    /// all three passes miss on the full string, the same three passes run again on the text after
    /// the breadcrumb's last <c>›</c>, trimmed. A section no pass resolves yields a marker line
    /// naming the article's headings, followed by the whole article.
    /// </summary>
    public static string Extract(string content, string section)
    {
        return Extract(content, section, out _);
    }

    /// <summary>
    /// <see cref="Extract(string, string)"/>, also reporting through
    /// <paramref name="sectionMissed"/> whether no heading matched, so the returned text is the
    /// section-miss marker line followed by the whole article.
    /// </summary>
    public static string Extract(string content, string section, out bool sectionMissed)
    {
        sectionMissed = false;
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var headings = ParseHeadings(lines);

        int selected = FindHeadingIndex(headings, section);

        if (selected < 0 && section.IndexOf(BreadcrumbSeparator) >= 0)
        {
            string lastSegment = section.Substring(section.LastIndexOf(BreadcrumbSeparator) + 1).Trim();
            if (lastSegment.Length > 0)
            {
                selected = FindHeadingIndex(headings, lastSegment);
            }
        }

        if (selected < 0)
        {
            sectionMissed = true;
            return $"{SectionMissMarkerOpening}{section}' not found in article.{BuildHeadingListFragment(Headings(content))}{SectionMissFullTextEnding}{SectionMissSeparator}{content}";
        }

        int sectionLevel = headings[selected].Level;
        int endLine = lines.Length;

        for (int i = selected + 1; i < headings.Count; i++)
        {
            if (headings[i].Level <= sectionLevel)
            {
                endLine = headings[i].LineIndex;
                break;
            }
        }

        var sb = new StringBuilder();
        for (int i = headings[selected].LineIndex; i < endLine; i++)
        {
            sb.AppendLine(lines[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// The three-pass match a <c>section</c> request runs against <paramref name="headings"/> —
    /// exact heading equality, then equality with both sides normalised, then a normalised request
    /// contained in exactly one normalised heading — returning the winning heading's index, or -1
    /// when no pass resolves one. Two headings containing the request is a miss, not a pick.
    /// </summary>
    private static int FindHeadingIndex(List<(int LineIndex, int Level, string Title)> headings, string section)
    {
        int selected = headings.FindIndex(h => h.Title.Equals(section, StringComparison.OrdinalIgnoreCase));

        if (selected < 0)
        {
            string normalizedSection = NormalizeHeadingTitle(section);
            if (normalizedSection.Length > 0)
            {
                selected = headings.FindIndex(
                    h => NormalizeHeadingTitle(h.Title).Equals(normalizedSection, StringComparison.OrdinalIgnoreCase));

                if (selected < 0)
                {
                    var containing = headings
                        .Select((h, index) => (Index: index, Title: NormalizeHeadingTitle(h.Title)))
                        .Where(h => h.Title.Contains(normalizedSection, StringComparison.OrdinalIgnoreCase))
                        .Select(h => h.Index)
                        .Take(2)
                        .ToList();

                    if (containing.Count == 1)
                    {
                        selected = containing[0];
                    }
                }
            }
        }

        return selected;
    }

    /// <summary>
    /// Every Markdown heading line in <paramref name="lines"/> — one starting with one or more
    /// <c>#</c> — as its line index, level (the run of <c>#</c>) and title (the rest of the line,
    /// trimmed), in document order.
    /// </summary>
    private static List<(int LineIndex, int Level, string Title)> ParseHeadings(string[] lines)
    {
        var headings = new List<(int LineIndex, int Level, string Title)>();

        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith("#"))
            {
                continue;
            }

            var match = Regex.Match(lines[i], @"^(#+)\s+(.*)");
            if (match.Success)
            {
                headings.Add((i, match.Groups[1].Value.Length, match.Groups[2].Value.Trim()));
            }
        }

        return headings;
    }

    /// <summary>
    /// Every Markdown heading in <paramref name="content"/>, in document order, as its title alone
    /// (leading <c>#</c>'s stripped, trimmed). Shared by the section-miss marker line and
    /// <c>wiki_view</c>'s too-long-article notice, so both list one article's headings the same way.
    /// </summary>
    internal static IReadOnlyList<string> Headings(string content)
    {
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        return ParseHeadings(lines).Select(h => h.Title).ToList();
    }

    /// <summary>
    /// A heading title reduced to what a <c>section</c> request is compared against: every leading
    /// character that is not a letter or digit removed — emoji, variation selectors, punctuation
    /// and whitespace — internal whitespace runs collapsed to one space, and the result trimmed.
    /// Stripping is leading only, so a plain heading stays distinct from a decorated one of the
    /// same name.
    /// </summary>
    internal static string NormalizeHeadingTitle(string title)
    {
        int start = 0;
        while (start < title.Length && !char.IsLetterOrDigit(title[start]))
        {
            start++;
        }

        var sb = new StringBuilder(title.Length - start);
        bool pendingSpace = false;

        for (int i = start; i < title.Length; i++)
        {
            char c = title[i];

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// The <c>" Headings: a; b; c."</c> fragment the section-miss marker line carries, listing the
    /// article's headings in document order exactly as written so one can be copied straight back
    /// into a <c>section</c> argument. Capped, because the marker line precedes the whole article
    /// and every tool result is re-sent to the model on each subsequent round. An article with no
    /// headings contributes nothing.
    /// </summary>
    internal static string BuildHeadingListFragment(IEnumerable<string> titles)
    {
        string list = string.Join("; ", titles);
        if (list.Length == 0)
        {
            return string.Empty;
        }

        if (list.Length > SectionMissHeadingListMaxChars)
        {
            int cut = SectionMissHeadingListMaxChars;

            // A heading list is mostly emoji at the boundaries, and cutting between the two halves
            // of a surrogate pair leaves a lone surrogate the serializer renders as U+FFFD.
            if (char.IsHighSurrogate(list[cut - 1])) cut--;

            list = list.Substring(0, cut) + "…";
        }

        return $" Headings: {list}.";
    }

    /// <summary>
    /// A section-miss result — <paramref name="rendered"/>: any text before the marker line (an
    /// article header), the marker line, a blank line, then the whole article — cut to
    /// <paramref name="budget"/> characters. The text before the marker and the marker's heading
    /// list are kept; the marker's "Returning full text." ending becomes how many of the article's
    /// characters are shown and a pointer to call <paramref name="toolName"/> again with one of the
    /// headings; one newline then the article's opening characters fill the rest, so the result
    /// lands at exactly the budget. Null when <paramref name="rendered"/> is within the budget or
    /// carries no marker line of this form.
    /// </summary>
    internal static string? CapSectionMiss(string rendered, int budget, string toolName)
    {
        if (rendered.Length <= budget)
        {
            return null;
        }

        int markerStart = rendered.IndexOf(SectionMissMarkerOpening, StringComparison.Ordinal);
        if (markerStart < 0)
        {
            return null;
        }

        string terminator = SectionMissFullTextEnding + SectionMissSeparator;
        int endingStart = rendered.IndexOf(terminator, markerStart, StringComparison.Ordinal);
        if (endingStart < 0)
        {
            return null;
        }

        string prefix = rendered.Substring(0, markerStart);
        string markerHead = rendered.Substring(markerStart, endingStart - markerStart);
        string article = rendered.Substring(endingStart + terminator.Length);
        bool hasHeadings = Headings(article).Count > 0;

        string ComposeMarker(int shown) => hasHeadings
            ? $"{markerHead} Showing the first {shown} of {article.Length} characters; call {toolName} again with one of the headings above as section.]"
            : $"{markerHead} Showing the first {shown} of {article.Length} characters.]";

        var (marker, shownCount) = FitShownCount(ComposeMarker, budget - prefix.Length);

        // Only a marker grown past the text it replaced can leave room for more than the article.
        if (shownCount > article.Length)
        {
            shownCount = article.Length;
            marker = ComposeMarker(shownCount);
        }

        return prefix + marker + "\n" + article.Substring(0, shownCount);
    }

    /// <summary>
    /// A line naming its own shown-count, and that count, found by fixed point so the line, one
    /// newline and the shown characters total exactly <paramref name="budget"/>: growing the count
    /// by a digit can grow the line itself, so the count is recomputed from the line's length until
    /// it stops moving.
    /// </summary>
    internal static (string Line, int Shown) FitShownCount(Func<int, string> composeLine, int budget)
    {
        int shown = 0;
        for (int i = 0; i < 4; i++)
        {
            string probe = composeLine(shown);
            int candidate = Math.Max(0, budget - probe.Length - 1);
            if (candidate == shown)
            {
                break;
            }
            shown = candidate;
        }

        return (composeLine(shown), shown);
    }
}
