namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;

public enum BenchmarkCitationKind
{
    SourceFile = 1,
    Symbol = 2,
    WikiArticle = 3
}

public enum BenchmarkCitationStatus
{
    Resolved = 1,
    Unresolved = 2,

    /// <summary>
    /// No lookup exists for this kind of citation in this deployment. Reported explicitly rather
    /// than silently passing: "we did not check" and "we checked and it is fine" are different
    /// facts, and a panel that conflates them is worse than one that omits the row.
    /// </summary>
    NotValidated = 3
}

public sealed record BenchmarkCitation
{
    public BenchmarkCitationKind Kind { get; init; }

    /// <summary>The citation as written in the rubric.</summary>
    public string Value { get; init; } = string.Empty;

    public BenchmarkCitationStatus Status { get; init; }

    /// <summary>
    /// A line number written beside a file path, parsed and reported but **never validated**.
    /// Line numbers in GnollHack's C source drift with every commit, so validating them would
    /// produce permanent false alarms — and an operator who learns to ignore one line of a panel
    /// learns to ignore the panel.
    /// </summary>
    public int? LineNumber { get; init; }
}

public sealed record BenchmarkQuestionCitations
{
    public long QuestionId { get; init; }
    public int OrderIndex { get; init; }
    public IReadOnlyList<BenchmarkCitation> Citations { get; init; } = Array.Empty<BenchmarkCitation>();

    public int UnresolvedCount { get; init; }
    public int NotValidatedCount { get; init; }

    /// <summary>
    /// True when the rubric carries no <c>**SOURCE**</c> line at all. Not an error — plenty of
    /// rubrics are self-contained — but it is what a reader needs to know before reading a clean
    /// citation report as evidence that the rubric is grounded.
    /// </summary>
    public bool HasNoCitations { get; init; }
}

/// <summary>
/// Validates the citations rubrics carry under the <c>**SOURCE**</c> convention against the
/// running source and wiki indexes. No AI calls.
///
/// The problem this solves is quiet rot: a rubric that cites <c>src/role.c</c> and a wiki article
/// keeps grading answers long after the file is renamed or the article retitled, and nothing says
/// the answer key's own evidence no longer resolves.
///
/// The resolvers are injected rather than taken as service dependencies, so this class stays a
/// pure function of its inputs and a test can assert the parsing without an index.
/// </summary>
public static class BenchmarkRubricCitationValidator
{
    /// <summary>
    /// The rubric's source section. Bounded at the next bold label so a `**SOURCE**` line does not
    /// swallow the rest of the rubric.
    /// </summary>
    private static readonly Regex SourceSectionRegex = new(
        @"\*\*SOURCE\*\*:?(?<body>[\s\S]*?)(?=\n\s*\*\*[A-Z][A-Z ]{2,}\*\*|\z)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// A repository-relative source path: at least one directory segment and a C/C++/header
    /// extension. Anchored on the extension rather than on a leading directory list, so a new
    /// top-level directory in GnollHack does not silently stop being recognised.
    /// </summary>
    private static readonly Regex FilePathRegex = new(
        @"(?<path>[A-Za-z0-9_.\-]+(?:/[A-Za-z0-9_.\-]+)+\.(?:c|h|cpp|hpp|cs))",
        RegexOptions.Compiled);

    /// <summary>`line 1217`, `L1217`, `:1217` — parsed for display, never validated.</summary>
    private static readonly Regex LineNumberRegex = new(
        @"(?:around\s+)?(?:lines?\s+|L|:)(?<line>\d{1,6})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>A backticked symbol: `MH_GNOLL`, `objects[]`, `role_init`.</summary>
    private static readonly Regex BacktickRegex = new(
        @"`(?<symbol>[^`\n]{1,120})`",
        RegexOptions.Compiled);

    /// <summary>A quoted wiki article title: GnollHack wiki, "How GnollHack differs from NetHack".</summary>
    private static readonly Regex QuotedTitleRegex = new(
        @"[""“](?<title>[^""”\n]{3,160})[""”]",
        RegexOptions.Compiled);

    /// <summary>
    /// A backticked span that is prose rather than a symbol — it has whitespace and no identifier
    /// shape. Sending those to a definition lookup produces unresolvable noise.
    /// </summary>
    private static readonly Regex SymbolShapeRegex = new(
        @"^[A-Za-z_][A-Za-z0-9_]*(?:\[\])?(?:\(\))?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Validates every question's rubric.
    ///
    /// <paramref name="filePathResolver"/> and <paramref name="symbolResolver"/> answer "does this
    /// resolve in the index". <paramref name="wikiTitleResolver"/> may return null for "no lookup
    /// available", which becomes <see cref="BenchmarkCitationStatus.NotValidated"/>; passing null
    /// for the delegate itself means the same for every title.
    /// </summary>
    public static IReadOnlyList<BenchmarkQuestionCitations> Validate(
        IEnumerable<BenchmarkQuestion> questions,
        Func<string, bool> filePathResolver,
        Func<string, bool> symbolResolver,
        Func<string, bool?>? wikiTitleResolver = null)
    {
        ArgumentNullException.ThrowIfNull(filePathResolver);
        ArgumentNullException.ThrowIfNull(symbolResolver);
        if (questions == null) return Array.Empty<BenchmarkQuestionCitations>();

        var results = new List<BenchmarkQuestionCitations>();

        foreach (var question in questions.OrderBy(q => q.OrderIndex))
        {
            var citations = Parse(question.ExpectedPoints).ToList();
            var validated = new List<BenchmarkCitation>(citations.Count);

            foreach (var citation in citations)
            {
                BenchmarkCitationStatus status = citation.Kind switch
                {
                    BenchmarkCitationKind.SourceFile =>
                        Resolve(() => filePathResolver(citation.Value)),
                    BenchmarkCitationKind.Symbol =>
                        Resolve(() => symbolResolver(citation.Value)),
                    _ => ResolveWiki(wikiTitleResolver, citation.Value)
                };

                validated.Add(citation with { Status = status });
            }

            results.Add(new BenchmarkQuestionCitations
            {
                QuestionId = question.Id,
                OrderIndex = question.OrderIndex,
                // Unresolved first: this panel exists for them.
                Citations = validated
                    .OrderBy(c => c.Status == BenchmarkCitationStatus.Unresolved ? 0
                        : c.Status == BenchmarkCitationStatus.NotValidated ? 1 : 2)
                    .ThenBy(c => c.Kind)
                    .ThenBy(c => c.Value, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                UnresolvedCount = validated.Count(c => c.Status == BenchmarkCitationStatus.Unresolved),
                NotValidatedCount = validated.Count(c => c.Status == BenchmarkCitationStatus.NotValidated),
                HasNoCitations = validated.Count == 0
            });
        }

        return results;
    }

    /// <summary>
    /// Extracts the citations from one rubric's <c>**SOURCE**</c> section. Public so the parsing
    /// can be tested without resolvers, and so a caller can count citations without validating.
    /// </summary>
    public static IReadOnlyList<BenchmarkCitation> Parse(string? rubric)
    {
        if (string.IsNullOrWhiteSpace(rubric)) return Array.Empty<BenchmarkCitation>();

        var citations = new List<BenchmarkCitation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match section in SourceSectionRegex.Matches(rubric))
        {
            string body = section.Groups["body"].Value;

            foreach (Match m in FilePathRegex.Matches(body))
            {
                string path = m.Groups["path"].Value;
                if (!seen.Add("f:" + path)) continue;

                // Looked for only in the text following the path, so a line number belonging to
                // the next citation is not attributed to this one.
                string tail = body.Substring(m.Index + m.Length);
                int cut = tail.IndexOfAny(new[] { ';', '\n' });
                if (cut >= 0) tail = tail.Substring(0, cut);

                var lineMatch = LineNumberRegex.Match(tail);
                citations.Add(new BenchmarkCitation
                {
                    Kind = BenchmarkCitationKind.SourceFile,
                    Value = path,
                    LineNumber = lineMatch.Success && int.TryParse(lineMatch.Groups["line"].Value, out int line)
                        ? line
                        : null
                });
            }

            foreach (Match m in BacktickRegex.Matches(body))
            {
                string symbol = m.Groups["symbol"].Value.Trim();
                if (symbol.Length == 0 || !SymbolShapeRegex.IsMatch(symbol)) continue;
                if (!seen.Add("s:" + symbol)) continue;

                citations.Add(new BenchmarkCitation
                {
                    Kind = BenchmarkCitationKind.Symbol,
                    Value = symbol
                });
            }

            foreach (Match m in QuotedTitleRegex.Matches(body))
            {
                string title = m.Groups["title"].Value.Trim();
                if (title.Length == 0 || !seen.Add("w:" + title)) continue;

                citations.Add(new BenchmarkCitation
                {
                    Kind = BenchmarkCitationKind.WikiArticle,
                    Value = title
                });
            }
        }

        return citations;
    }

    /// <summary>
    /// A resolver that throws — an index still initialising, say — must not fail the whole report:
    /// the citation is reported as not validated, which is exactly what happened.
    /// </summary>
    private static BenchmarkCitationStatus Resolve(Func<bool> probe)
    {
        try
        {
            return probe() ? BenchmarkCitationStatus.Resolved : BenchmarkCitationStatus.Unresolved;
        }
        catch (Exception)
        {
            return BenchmarkCitationStatus.NotValidated;
        }
    }

    private static BenchmarkCitationStatus ResolveWiki(Func<string, bool?>? resolver, string title)
    {
        if (resolver == null) return BenchmarkCitationStatus.NotValidated;

        try
        {
            bool? resolved = resolver(title);
            return resolved switch
            {
                true => BenchmarkCitationStatus.Resolved,
                false => BenchmarkCitationStatus.Unresolved,
                _ => BenchmarkCitationStatus.NotValidated
            };
        }
        catch (Exception)
        {
            return BenchmarkCitationStatus.NotValidated;
        }
    }
}
