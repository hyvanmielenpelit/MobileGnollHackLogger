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

    /// <summary>
    /// The requested section's text: the matched heading line, then every line up to but not
    /// including the next heading of the same or a higher level. Matching runs in three passes over
    /// the article's headings — case-insensitive equality on the heading text, then equality with
    /// both sides normalised, then a normalised request contained in exactly one normalised heading —
    /// so a heading carrying an emoji prefix (<c>## 🔮 Elbereth</c>) answers a request for
    /// <c>Elbereth</c>, <c>Spell failure</c> answers <c>Minimum spell failure rates</c>, and an
    /// earlier pass always beats a later one. The first two passes take the first heading they
    /// match; the third takes a heading only when it is the sole one containing the request. A
    /// section no pass resolves yields a marker line naming the article's headings, followed by the
    /// whole article.
    /// </summary>
    public static string Extract(string content, string section)
    {
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
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

        if (selected < 0)
        {
            return $"[Section '{section}' not found in article.{BuildHeadingListFragment(headings.Select(h => h.Title))} Returning full text.]\n\n{content}";
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
}
