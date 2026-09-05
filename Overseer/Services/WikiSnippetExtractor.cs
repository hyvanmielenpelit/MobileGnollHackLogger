namespace Overseer.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public class WikiSection
{
    public int Index { get; set; }
    public int Level { get; set; }
    public string HeadingText { get; set; } = string.Empty;
    public string HeadingPath { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public static class WikiSnippetExtractor
{
    public const int HeadingTermWeight = 10;
    public const int BodyTermWeight = 1;

    private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);

    public static List<string> ExtractQueryTerms(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<string>();
        }

        return Regex.Matches(query, @"[\w\d]+", RegexOptions.IgnoreCase)
            .Select(m => m.Value.Trim().ToLowerInvariant())
            .Where(t => t.Length > 1)
            .Distinct()
            .ToList();
    }

    public static List<WikiSection> SplitSections(string markdown)
    {
        var sections = new List<WikiSection>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return sections;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var headingStack = new List<(int Level, string Text)>();

        var currentBody = new StringBuilder();
        WikiSection? currentSection = null;
        int sectionIndex = 0;

        foreach (var rawLine in lines)
        {
            var match = HeadingRegex.Match(rawLine);
            if (match.Success)
            {
                int level = match.Groups[1].Value.Length;
                string rawHeadingText = match.Groups[2].Value.Trim();
                string headingText = Regex.Replace(rawHeadingText, @"\s*#+\s*$", "").Replace("[[", "").Replace("]]", "").Trim();

                if (currentSection == null)
                {
                    string preamble = currentBody.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(preamble))
                    {
                        sections.Add(new WikiSection
                        {
                            Index = sectionIndex++,
                            Level = 0,
                            HeadingText = string.Empty,
                            HeadingPath = string.Empty,
                            Body = preamble
                        });
                    }
                }
                else
                {
                    currentSection.Body = currentBody.ToString().Trim();
                    sections.Add(currentSection);
                }

                currentBody.Clear();

                while (headingStack.Count > 0 && headingStack[^1].Level >= level)
                {
                    headingStack.RemoveAt(headingStack.Count - 1);
                }
                headingStack.Add((level, headingText));

                string path = string.Join(" › ", headingStack.Select(h => h.Text));

                currentSection = new WikiSection
                {
                    Index = sectionIndex++,
                    Level = level,
                    HeadingText = headingText,
                    HeadingPath = path
                };
            }
            else
            {
                currentBody.AppendLine(rawLine);
            }
        }

        if (currentSection == null)
        {
            string fullBody = currentBody.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(fullBody))
            {
                sections.Add(new WikiSection
                {
                    Index = 0,
                    Level = 0,
                    HeadingText = string.Empty,
                    HeadingPath = string.Empty,
                    Body = fullBody
                });
            }
        }
        else
        {
            currentSection.Body = currentBody.ToString().Trim();
            sections.Add(currentSection);
        }

        return sections;
    }

    public static int Score(WikiSection section, IReadOnlyCollection<string> queryTerms)
    {
        if (queryTerms == null || queryTerms.Count == 0 || section == null)
        {
            return 0;
        }

        var distinctTerms = queryTerms
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();

        int score = 0;
        foreach (var term in distinctTerms)
        {
            bool inHeading = !string.IsNullOrEmpty(section.HeadingPath) &&
                             section.HeadingPath.Contains(term, StringComparison.OrdinalIgnoreCase);
            bool inBody = !string.IsNullOrEmpty(section.Body) &&
                          section.Body.Contains(term, StringComparison.OrdinalIgnoreCase);

            if (inHeading)
            {
                score += HeadingTermWeight;
            }
            else if (inBody)
            {
                score += BodyTermWeight;
            }
        }

        return score;
    }

    public static string BuildSnippet(string filename, string markdown, string query, int perResultChars)
    {
        var terms = ExtractQueryTerms(query);
        return BuildSnippet(filename, markdown, terms, perResultChars);
    }

    public static string BuildSnippet(string filename, string markdown, IReadOnlyCollection<string> queryTerms, int perResultChars)
    {
        if (perResultChars <= 0) perResultChars = 2500;
        string header = $"--- {filename} ---";

        var sections = SplitSections(markdown);
        if (sections.Count == 0)
        {
            return $"{header}\n[article: {filename} — complete]";
        }

        var scored = sections.Select(s => new { Section = s, Score = Score(s, queryTerms) }).ToList();
        bool anyPositive = scored.Any(x => x.Score > 0);

        List<WikiSection> selected;
        if (!anyPositive)
        {
            selected = new List<WikiSection>();
            if (sections[0].Level == 0)
            {
                selected.Add(sections[0]);
                if (sections.Count > 1)
                {
                    selected.Add(sections[1]);
                }
            }
            else
            {
                selected.Add(sections[0]);
            }
        }
        else
        {
            var ranked = scored
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Section.Index)
                .Select(x => x.Section)
                .ToList();

            selected = new List<WikiSection>();
            int currentEstimatedLength = header.Length + 100;

            foreach (var sec in ranked)
            {
                int secLength = FormatSectionLength(sec);
                if (selected.Count == 0 || currentEstimatedLength + secLength <= perResultChars)
                {
                    selected.Add(sec);
                    currentEstimatedLength += secLength;
                }
            }
        }

        var inOrder = selected.OrderBy(s => s.Index).ToList();
        int omittedCount = sections.Count - inOrder.Count;

        var sb = new StringBuilder();
        sb.Append(header);

        foreach (var sec in inOrder)
        {
            sb.AppendLine();
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(sec.HeadingPath))
            {
                sb.AppendLine($"### {sec.HeadingPath}");
            }
            sb.Append(sec.Body);
        }

        string footer = omittedCount > 0
            ? $"[article: {filename} — {omittedCount} further section(s) omitted; use wiki_view for the full text]"
            : $"[article: {filename} — complete]";

        int maxAllowedForBody = perResultChars - footer.Length - 4;
        if (maxAllowedForBody < header.Length)
        {
            maxAllowedForBody = header.Length;
        }

        if (sb.Length > maxAllowedForBody)
        {
            sb.Length = maxAllowedForBody;
            sb.AppendLine("...");
            if (omittedCount == 0) omittedCount = 1;
            footer = $"[article: {filename} — {omittedCount} further section(s) omitted; use wiki_view for the full text]";
        }

        sb.AppendLine();
        sb.AppendLine();
        sb.Append(footer);

        return sb.ToString();
    }

    private static int FormatSectionLength(WikiSection sec)
    {
        int len = 4 + sec.Body.Length;
        if (!string.IsNullOrWhiteSpace(sec.HeadingPath))
        {
            len += 5 + sec.HeadingPath.Length;
        }
        return len;
    }
}
