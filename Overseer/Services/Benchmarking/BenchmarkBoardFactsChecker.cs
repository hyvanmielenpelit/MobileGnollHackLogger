namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>
/// Checks that every double-quoted literal in a suite's <c>**BOARD FACTS**</c> rubric sections occurs
/// verbatim in the board text the graders receive. Pure and deterministic; advisory only.
///
/// A section starts at a line that is exactly <c>**BOARD FACTS**</c> once trimmed and ends at the
/// next line starting with <c>**</c>. A bullet is a line starting with <c>- </c>; a following
/// non-empty line that is neither a bullet nor a heading continues it. Straight quotes pair
/// sequentially (first with second, third with fourth; an unpaired last quote is ignored), and
/// typographic <c>“…”</c> pairs are read the same way, so a quoted board line that itself contains
/// quote characters splits into fragments that are each still verbatim substrings of the board.
/// Comparison is ordinal after converting CRLF and lone CR to LF; nothing else is normalised,
/// because the authoring contract is verbatim.
/// </summary>
public static class BenchmarkBoardFactsChecker
{
    public const string SectionHeading = "**BOARD FACTS**";
    public const int MaxExcerptLength = 160;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static BoardFactsCheckDto Check(string boardText, IEnumerable<BenchmarkQuestion> questions)
        => Check(boardText, questions.Select(q => (q.Id, q.OrderIndex, q.ExpectedPoints)));

    public static BoardFactsCheckDto Check(
        string boardText,
        IEnumerable<(long Id, int OrderIndex, string? ExpectedPoints)> questions)
    {
        string board = NormalizeNewlines(boardText ?? string.Empty);
        var result = new BoardFactsCheckDto();

        foreach (var (id, orderIndex, expectedPoints) in questions.OrderBy(q => q.OrderIndex).ThenBy(q => q.Id))
        {
            foreach (string bullet in ExtractBullets(expectedPoints))
            {
                result.BulletCount++;
                List<string> literals = ExtractLiterals(bullet);
                if (literals.Count == 0)
                {
                    result.UnquotedBulletCount++;
                    result.UnquotedBullets.Add(new BoardFactIssueDto
                    {
                        QuestionId = id,
                        OrderIndex = orderIndex,
                        LineExcerpt = Excerpt(bullet),
                    });
                    continue;
                }

                foreach (string literal in literals)
                {
                    result.CheckedLiteralCount++;
                    if (!board.Contains(NormalizeNewlines(literal), StringComparison.Ordinal))
                    {
                        result.MissingLiterals.Add(new BoardFactIssueDto
                        {
                            QuestionId = id,
                            OrderIndex = orderIndex,
                            Literal = literal,
                            LineExcerpt = Excerpt(bullet),
                        });
                    }
                }
            }
        }

        return result;
    }

    public static string Serialize(BoardFactsCheckDto check) => JsonSerializer.Serialize(check, JsonOptions);

    /// <summary>Null for null, blank or unreadable JSON.</summary>
    public static BoardFactsCheckDto? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BoardFactsCheckDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The bullets of every BOARD FACTS section in <paramref name="expectedPoints"/>, each with its
    /// leading <c>- </c> removed and continuation lines joined by a single space.</summary>
    internal static List<string> ExtractBullets(string? expectedPoints)
    {
        var bullets = new List<string>();
        if (string.IsNullOrEmpty(expectedPoints))
        {
            return bullets;
        }

        bool inSection = false;
        StringBuilder? current = null;

        void Flush()
        {
            if (current != null)
            {
                bullets.Add(current.ToString());
                current = null;
            }
        }

        foreach (string rawLine in NormalizeNewlines(expectedPoints).Split('\n'))
        {
            string trimmed = rawLine.Trim();
            if (trimmed == SectionHeading)
            {
                Flush();
                inSection = true;
                continue;
            }

            if (!inSection)
            {
                continue;
            }

            if (trimmed.StartsWith("**", StringComparison.Ordinal))
            {
                Flush();
                inSection = false;
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                Flush();
                current = new StringBuilder(trimmed.Substring(2));
            }
            else if (trimmed.Length > 0 && current != null)
            {
                current.Append(' ').Append(trimmed);
            }
        }

        Flush();
        return bullets;
    }

    /// <summary>Non-empty spans between sequentially paired straight quotes, then between typographic pairs.</summary>
    internal static List<string> ExtractLiterals(string bullet)
    {
        var literals = new List<string>();

        int open = -1;
        for (int i = 0; i < bullet.Length; i++)
        {
            if (bullet[i] != '"')
            {
                continue;
            }

            if (open < 0)
            {
                open = i;
            }
            else
            {
                AddSpan(literals, bullet, open + 1, i);
                open = -1;
            }
        }

        open = -1;
        for (int i = 0; i < bullet.Length; i++)
        {
            if (bullet[i] == '“' && open < 0)
            {
                open = i;
            }
            else if (bullet[i] == '”' && open >= 0)
            {
                AddSpan(literals, bullet, open + 1, i);
                open = -1;
            }
        }

        return literals;
    }

    private static void AddSpan(List<string> literals, string text, int start, int end)
    {
        if (end > start)
        {
            literals.Add(text.Substring(start, end - start));
        }
    }

    private static string Excerpt(string bullet)
        => bullet.Length <= MaxExcerptLength ? bullet : bullet.Substring(0, MaxExcerptLength - 1) + "…";

    private static string NormalizeNewlines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
