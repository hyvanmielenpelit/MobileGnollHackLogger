namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>
/// The verbatim material a report-pack document may print at Detailed and Full disclosure, captured
/// from the subject's answer rows: the question text as asked, the rubric as graded, an answer
/// excerpt and, when the excerpt was cut, the complete answer, each grader's comment and evidence,
/// and the claim verifier's rulings. Never read from the live suite. Pure: no I/O and no clock.
/// </summary>
public static class BenchmarkReportContent
{
    public const string Ellipsis = "…";

    /// <summary>What an excerpt cut before a table ends with.</summary>
    public const string TableFollows = "… (a table follows in the answer)";

    // A claim ruling's role, from the stored verification's BenchmarkClaimRoles. The answer's own
    // text: an unverified claim, a sentence a grader accused (accusedQuote) and the quote a critical
    // error rests on. The grader's text: a sentence of its own evidence (assessorStatement) and the
    // basis of an out-of-rubric deduction, where Refuted means the verifier sided with the answer.
    public const string ClaimRole = "claim";
    public const string AccusedSentenceRole = "accusedSentence";
    public const string CriticalErrorQuoteRole = "criticalErrorQuote";
    public const string AssessorStatementRole = "assessorStatement";
    public const string OutOfRubricBasisRole = "outOfRubricBasis";

    /// <summary>An excerpt cut at a sentence end or line break keeps at least this share of the limit.</summary>
    private const double MinimumSentenceCutShare = 0.6;

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly Regex TableSeparatorLine = new(
        @"^\s*\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)*\|?\s*$", RegexOptions.CultureInvariant);

    /// <summary>A ruling role that is the answer's own text: an ordinary claim, an accused sentence or a critical-error quote.</summary>
    public static bool IsAnswerSentenceRole(string? role)
        => role is ClaimRole or AccusedSentenceRole or CriticalErrorQuoteRole;

    /// <summary>
    /// The ruling role of one verification in its list. Only a list in which some record carries
    /// roles is read by role (<see cref="BenchmarkClaimRoles.HasRoles"/>); there a record without
    /// roles is an ordinary claim. In a list without roles every record's role is null.
    /// </summary>
    public static string? RoleOf(BenchmarkClaimVerification verification, bool listHasRoles)
    {
        ArgumentNullException.ThrowIfNull(verification);
        if (!listHasRoles) return null;
        if (verification.Roles == null) return ClaimRole;

        if (BenchmarkClaimRoles.HasRole(verification, BenchmarkClaimRoles.CriticalErrorQuote)) return CriticalErrorQuoteRole;
        if (BenchmarkClaimRoles.HasRole(verification, BenchmarkClaimRoles.AccusedQuote)) return AccusedSentenceRole;
        if (BenchmarkClaimRoles.HasRole(verification, BenchmarkClaimRoles.UnverifiedClaim)) return ClaimRole;
        if (BenchmarkClaimRoles.HasRole(verification, BenchmarkClaimRoles.AssessorStatement)) return AssessorStatementRole;
        if (BenchmarkClaimRoles.HasRole(verification, BenchmarkClaimRoles.OutOfRubricBasis)) return OutOfRubricBasisRole;
        return null;
    }

    /// <summary>
    /// A ruling's verdict with its role in words, and whose side the verifier took where the role
    /// makes that plain: <c>Answer sentence accused by a grader — supported (the verifier sided with
    /// the answer)</c>. A ruling without a role is its verdict alone.
    /// </summary>
    public static string RulingLabel(string? role, string? verdict)
    {
        string v = (verdict ?? string.Empty).Trim();
        bool refuted = string.Equals(v, "refuted", StringComparison.Ordinal);
        bool supported = string.Equals(v, "supported", StringComparison.Ordinal);

        const string SidedWithGrader = " (the verifier sided with the grader)";
        const string SidedWithAnswer = " (the verifier sided with the answer)";

        return role switch
        {
            ClaimRole => "Answer sentence — " + v,
            AccusedSentenceRole => "Answer sentence accused by a grader — " + v
                + (refuted ? SidedWithGrader : supported ? SidedWithAnswer : string.Empty),
            CriticalErrorQuoteRole => "Answer sentence a grader flagged as a critical error — " + v
                + (refuted ? SidedWithGrader : supported ? SidedWithAnswer : string.Empty),
            AssessorStatementRole => "Grader's statement — " + v
                + (refuted ? SidedWithAnswer : supported ? SidedWithGrader : string.Empty),
            OutOfRubricBasisRole => "Grader's basis for an out-of-rubric deduction — " + v
                + (refuted ? SidedWithAnswer : supported ? SidedWithGrader : string.Empty),
            _ => v
        };
    }

    /// <summary>The stored verifications of an answer; null when none is stored or it is unreadable.</summary>
    public static List<BenchmarkClaimVerification>? ReadVerifications(string? claimVerificationJson)
    {
        if (string.IsNullOrWhiteSpace(claimVerificationJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(claimVerificationJson, ReadOptions)?
                .Where(v => v != null)
                .ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// One block per run in run-id order, each with its answers in order-index order, numbered as
    /// <see cref="BenchmarkReportFacts.NumberQuestions"/> numbers them.
    /// </summary>
    public static BenchmarkReportContentSnapshot Build(IReadOnlyList<BenchmarkRun> subjectRuns, int answerExcerptChars)
    {
        var runs = (subjectRuns ?? Array.Empty<BenchmarkRun>()).OrderBy(r => r.Id).ToList();
        var numberByItem = BenchmarkReportFacts.NumberQuestions(runs)
            .ToDictionary(s => s.ItemKey, s => s.Number, StringComparer.Ordinal);

        var snapshot = new BenchmarkReportContentSnapshot { AnswerExcerptChars = Math.Max(0, answerExcerptChars) };

        foreach (var run in runs)
        {
            var block = new BenchmarkReportContentRun { RunId = run.Id };
            bool panel = BenchmarkRunFinalizer.IsPanelRun(run);

            foreach (var answer in (run.Answers ?? new List<BenchmarkRunAnswer>()).OrderBy(a => a.OrderIndex).ThenBy(a => a.Id))
            {
                var (excerpt, cut) = Excerpt(answer.AnswerText, snapshot.AnswerExcerptChars);

                block.Questions.Add(new BenchmarkReportContentQuestion
                {
                    Number = numberByItem[BenchmarkReportFacts.ItemKeyOf(answer)],
                    QuestionKey = BenchmarkReportFacts.QuestionKeyOf(answer),
                    ItemRevisionUsed = answer.ItemRevisionUsed,
                    OrderIndex = answer.OrderIndex,
                    Band = BenchmarkReportFacts.BandNameOf(answer),
                    QuestionText = answer.QuestionText ?? string.Empty,
                    ExpectedPoints = answer.ExpectedPointsRecorded ? answer.ExpectedPointsUsed : null,
                    ExpectedPointsRecorded = answer.ExpectedPointsRecorded,
                    AnswerExcerpt = excerpt,
                    AnswerExcerptCut = cut,
                    AnswerText = cut ? answer.AnswerText ?? string.Empty : null,
                    Graders = GradersOf(run, answer, panel),
                    ClaimRulings = RulingsOf(answer)
                });
            }

            snapshot.Runs.Add(block);
        }

        return snapshot;
    }

    /// <summary>A question every covered model scored below this is a shared gap, third in the excerpt order.</summary>
    public const double SharedLowScore = 50.0;

    /// <summary>A question whose covered models' scores span at least this many points is second in the excerpt order.</summary>
    public const double WideSpreadPoints = 20.0;

    /// <summary>
    /// One covered model's own content for a comparison-scope document: its per-model content
    /// snapshot, numbered as its own sheet numbers its questions, and each of those numbers mapped to
    /// the comparison's question number.
    /// </summary>
    public sealed record ComparisonEntry(
        string Letter, string EntryKey, BenchmarkReportContentSnapshot Content, IReadOnlyDictionary<int, int> NumberMap);

    /// <summary>
    /// The content of a comparison-scope document, over <paramref name="questions"/> (the comparison's
    /// questions with their per-model cells) and each covered model's own content:
    /// <list type="bullet">
    /// <item>every question as asked, with its rubric, once, from the first model in letter order that
    /// has it (<see cref="BenchmarkReportContentSnapshot.Questions"/>);</item>
    /// <item>answer excerpts under one total budget of <paramref name="answerExcerptChars"/> characters
    /// per question, allotted question by question in <see cref="ExcerptPriority"/> order and, within a
    /// question, to the models with a critical error or a refuted sentence first, then the lowest
    /// scores; an excerpt that no longer fits is skipped. Each excerpt comes from the model's run whose
    /// graders' mean score is closest to its score on the question, ties to the lower run id.</item>
    /// </list>
    /// </summary>
    public static BenchmarkReportContentSnapshot BuildComparison(
        IReadOnlyList<BenchmarkReportQuestion> questions, IReadOnlyList<ComparisonEntry> entries, int answerExcerptChars)
    {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(entries);

        int chars = Math.Max(0, answerExcerptChars);
        var ordered = entries.OrderBy(e => e.Letter.Length).ThenBy(e => e.Letter, StringComparer.Ordinal).ToList();
        var snapshot = new BenchmarkReportContentSnapshot
        {
            AnswerExcerptChars = chars,
            Questions = new List<BenchmarkReportContentQuestion>()
        };

        // The blocks of each entry by comparison number: (run id, the entry's question).
        var blocks = ordered.ToDictionary(
            e => e.Letter,
            e => e.Content.Runs
                .OrderBy(r => r.RunId)
                .SelectMany(r => r.Questions.Select(q => (r.RunId, Question: q)))
                .Where(b => e.NumberMap.ContainsKey(b.Question.Number))
                .GroupBy(b => e.NumberMap[b.Question.Number])
                .ToDictionary(g => g.Key, g => g.ToList()),
            StringComparer.Ordinal);

        foreach (var question in questions.OrderBy(q => q.Number))
        {
            var source = ordered
                .Select(e => blocks[e.Letter].TryGetValue(question.Number, out var list) ? list[0].Question : null)
                .FirstOrDefault(q => q != null);
            if (source == null) continue;

            snapshot.Questions.Add(new BenchmarkReportContentQuestion
            {
                Number = question.Number,
                QuestionKey = source.QuestionKey,
                ItemRevisionUsed = source.ItemRevisionUsed,
                OrderIndex = source.OrderIndex,
                Band = source.Band,
                QuestionText = source.QuestionText,
                ExpectedPoints = source.ExpectedPoints,
                ExpectedPointsRecorded = source.ExpectedPointsRecorded
            });
        }

        long budget = (long)chars * questions.Count;
        var chosen = new List<(string Letter, string EntryKey, long RunId, BenchmarkReportContentQuestion Question)>();
        foreach (var question in ExcerptPriority(questions))
        {
            var cells = (question.Models ?? new List<BenchmarkReportQuestionModelScore>())
                .OrderBy(c => c.CriticalError || RefutedOf(c) > 0 ? 0 : 1)
                .ThenBy(c => c.Score.HasValue ? 0 : 1)
                .ThenBy(c => c.Score ?? 0)
                .ThenBy(c => c.Letter.Length)
                .ThenBy(c => c.Letter, StringComparer.Ordinal)
                .ToList();

            foreach (var cell in cells)
            {
                var entry = ordered.FirstOrDefault(e => string.Equals(e.Letter, cell.Letter, StringComparison.Ordinal));
                if (entry == null || !blocks[entry.Letter].TryGetValue(question.Number, out var candidates)) continue;

                var (runId, item) = Representative(candidates, cell.Score);
                if (chars <= 0 || item.AnswerExcerpt.Length > budget) continue;

                budget -= item.AnswerExcerpt.Length;
                chosen.Add((entry.Letter, entry.EntryKey, runId, item));
            }
        }

        foreach (var run in chosen
            .GroupBy(c => (c.Letter, c.EntryKey, c.RunId))
            .OrderBy(g => g.Key.Letter.Length)
            .ThenBy(g => g.Key.Letter, StringComparer.Ordinal)
            .ThenBy(g => g.Key.RunId))
        {
            var entry = ordered.First(e => string.Equals(e.Letter, run.Key.Letter, StringComparison.Ordinal));
            snapshot.Runs.Add(new BenchmarkReportContentRun
            {
                RunId = run.Key.RunId,
                EntryKey = run.Key.EntryKey,
                Letter = run.Key.Letter,
                Questions = run
                    .Select(c => Renumbered(c.Question, entry.NumberMap[c.Question.Number]))
                    .OrderBy(q => q.Number)
                    .ToList()
            });
        }

        return snapshot;
    }

    /// <summary>
    /// The order a comparison-scope document allots its excerpt budget in: first the questions on which
    /// any covered model made a critical error or had a refuted answer sentence, by number; then those
    /// whose scores span at least <see cref="WideSpreadPoints"/> points, widest first; then those every
    /// scored model scored below <see cref="SharedLowScore"/>, lowest best score first; then the rest,
    /// widest spread first. Ties by number.
    /// </summary>
    public static IReadOnlyList<BenchmarkReportQuestion> ExcerptPriority(IReadOnlyList<BenchmarkReportQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);

        static double Spread(BenchmarkReportQuestion q)
        {
            var scores = (q.Models ?? new List<BenchmarkReportQuestionModelScore>())
                .Where(c => c.Score.HasValue).Select(c => c.Score!.Value).ToList();
            return scores.Count < 2 ? 0 : scores.Max() - scores.Min();
        }

        static double? Best(BenchmarkReportQuestion q)
        {
            var scores = (q.Models ?? new List<BenchmarkReportQuestionModelScore>())
                .Where(c => c.Score.HasValue).Select(c => c.Score!.Value).ToList();
            return scores.Count == 0 ? null : scores.Max();
        }

        int Group(BenchmarkReportQuestion q)
        {
            var cells = q.Models ?? new List<BenchmarkReportQuestionModelScore>();
            if (cells.Any(c => c.CriticalError || RefutedOf(c) > 0)) return 0;
            if (Spread(q) >= WideSpreadPoints) return 1;
            if (Best(q) is double best && best < SharedLowScore) return 2;
            return 3;
        }

        return questions
            .Select(q => (Question: q, Group: Group(q), Spread: Spread(q), Best: Best(q) ?? double.MaxValue))
            .OrderBy(x => x.Group)
            .ThenBy(x => x.Group is 1 or 3 ? -x.Spread : 0)
            .ThenBy(x => x.Group == 2 ? x.Best : 0)
            .ThenBy(x => x.Question.Number)
            .Select(x => x.Question)
            .ToList();
    }

    /// <summary>A model's refuted answer sentences on a question, else its refuted claims.</summary>
    private static int RefutedOf(BenchmarkReportQuestionModelScore cell) => cell.RefutedAnswerSentences ?? cell.RefutedClaims;

    /// <summary>The block whose graders' mean score is closest to <paramref name="score"/>; the first when unscored. Ties to the lower run id.</summary>
    private static (long RunId, BenchmarkReportContentQuestion Question) Representative(
        IReadOnlyList<(long RunId, BenchmarkReportContentQuestion Question)> candidates, double? score)
    {
        if (score is not double target || candidates.Count == 1) return candidates[0];

        return candidates
            .Select(c => (Block: c, Mean: c.Question.Graders.Where(g => g.Score.HasValue).Select(g => (double)g.Score!.Value).DefaultIfEmpty(double.NaN).Average()))
            .OrderBy(x => double.IsNaN(x.Mean) ? double.MaxValue : Math.Abs(x.Mean - target))
            .ThenBy(x => x.Block.RunId)
            .First()
            .Block;
    }

    /// <summary>A copy of <paramref name="question"/> under the comparison's question number.</summary>
    private static BenchmarkReportContentQuestion Renumbered(BenchmarkReportContentQuestion question, int number) => new()
    {
        Number = number,
        QuestionKey = question.QuestionKey,
        ItemRevisionUsed = question.ItemRevisionUsed,
        OrderIndex = question.OrderIndex,
        Band = question.Band,
        QuestionText = question.QuestionText,
        ExpectedPoints = question.ExpectedPoints,
        ExpectedPointsRecorded = question.ExpectedPointsRecorded,
        AnswerExcerpt = question.AnswerExcerpt,
        AnswerExcerptCut = question.AnswerExcerptCut,
        AnswerText = question.AnswerText,
        Graders = question.Graders,
        ClaimRulings = question.ClaimRulings
    };

    /// <summary>
    /// The whole text when it fits in <paramref name="maxChars"/>. Otherwise the text cut back to the
    /// last sentence end or line break at or before that position, or, when neither lies in the last
    /// 40 % of it, to the last whitespace (a hard cut when there is none), with trailing whitespace
    /// removed and an ellipsis appended. A cut never falls inside a Markdown table: when the limit
    /// does, the excerpt ends before the table with <see cref="TableFollows"/>.
    /// </summary>
    public static (string Excerpt, bool Cut) Excerpt(string? text, int maxChars)
    {
        text ??= string.Empty;
        if (text.Length <= maxChars) return (text, false);
        if (maxChars <= 0) return (Ellipsis, true);

        var tables = TableSpans(text);
        bool InTable(int position) => tables.Any(t => position > t.Start && position < t.End);

        // After a table the ellipsis is a paragraph of its own, so it never reads as a table cell.
        string Finish(int position, string separator)
        {
            string head = text[..position].TrimEnd();
            bool afterTable = tables.Any(t => position >= t.End && text[t.End..position].Trim().Length == 0);
            return afterTable ? head + "\n\n" + Ellipsis : head + separator + Ellipsis;
        }

        foreach (var (start, end) in tables)
        {
            if (maxChars > start && maxChars < end)
            {
                string before = text[..start].TrimEnd();
                return (before.Length == 0 ? TableFollows : before + "\n\n" + TableFollows, true);
            }
        }

        int minimum = (int)Math.Ceiling(maxChars * MinimumSentenceCutShare);
        for (int i = maxChars; i >= minimum && i > 0; i--)
        {
            if (InTable(i)) continue;
            if ((text[i - 1] == '\n' || IsSentenceEnd(text, i)) && text[..i].Trim().Length > 0)
            {
                return (Finish(i, " "), true);
            }
        }

        int cut = maxChars;
        for (int i = maxChars; i > 0; i--)
        {
            if (!InTable(i) && char.IsWhiteSpace(text[i]))
            {
                cut = i;
                break;
            }
        }

        return (Finish(cut, string.Empty), true);
    }

    /// <summary>
    /// Whether <c>text[..position]</c> ends a sentence: whitespace at <paramref name="position"/>, and
    /// before it a full stop, question mark or exclamation mark, optionally followed by closing quotes,
    /// brackets or emphasis marks.
    /// </summary>
    private static bool IsSentenceEnd(string text, int position)
    {
        if (position >= text.Length || !char.IsWhiteSpace(text[position])) return false;

        int i = position - 1;
        while (i >= 0 && text[i] is '"' or '\'' or '”' or '’' or ')' or ']' or '*' or '_') i--;
        return i >= 0 && text[i] is '.' or '!' or '?';
    }

    /// <summary>
    /// The Markdown tables of <paramref name="text"/> as character ranges, from the start of a table's
    /// first line to the end of its last line: runs of lines that start with a pipe, delimiter rows,
    /// and lines holding a pipe next to either.
    /// </summary>
    private static List<(int Start, int End)> TableSpans(string text)
    {
        var lines = new List<(int Start, int End)>();
        int lineStart = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || text[i] == '\n')
            {
                lines.Add((lineStart, i));
                lineStart = i + 1;
            }
        }

        string LineAt(int k) => text[lines[k].Start..lines[k].End].TrimEnd('\r');

        var marked = new bool[lines.Count];
        for (int k = 0; k < lines.Count; k++)
        {
            string line = LineAt(k);
            marked[k] = line.TrimStart().StartsWith('|') || (line.Contains('|') && TableSeparatorLine.IsMatch(line));
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int k = 0; k < lines.Count; k++)
            {
                if (marked[k] || !LineAt(k).Contains('|')) continue;
                if ((k > 0 && marked[k - 1]) || (k + 1 < lines.Count && marked[k + 1]))
                {
                    marked[k] = true;
                    changed = true;
                }
            }
        }

        var spans = new List<(int Start, int End)>();
        for (int k = 0; k < lines.Count; k++)
        {
            if (!marked[k]) continue;
            int first = k;
            while (k + 1 < lines.Count && marked[k + 1]) k++;
            spans.Add((lines[first].Start, lines[k].End));
        }
        return spans;
    }

    /// <summary>Each grader that recorded a verdict on the answer, in role order.</summary>
    private static List<BenchmarkReportContentGrader> GradersOf(BenchmarkRun run, BenchmarkRunAnswer answer, bool panel)
    {
        var graders = new List<BenchmarkReportContentGrader>();

        if (answer.AssessmentStatus == BenchmarkAssessmentStatus.Scored)
        {
            var evidence = EvidenceOf(
                BenchmarkAssessmentParser.ReadEvidenceField(answer.AssessmentEvidenceJson, "accuracy"),
                BenchmarkAssessmentParser.ReadEvidenceField(answer.AssessmentEvidenceJson, "completeness"),
                BenchmarkAssessmentParser.ReadEvidenceField(answer.AssessmentEvidenceJson, "readability"),
                answer.CriticalError ? answer.CriticalErrorQuote : null);

            graders.Add(new BenchmarkReportContentGrader
            {
                Role = panel ? BenchmarkReportFacts.PanelMemberARole : BenchmarkReportFacts.AssessorRole,
                Label = (answer.AssessedByModelSnapshot ?? run.AssessorModelSnapshot).Label() ?? string.Empty,
                Score = answer.QualityScore,
                Comment = NullIfBlank(answer.ReviewComment),
                Evidence = evidence
            });
        }

        if (panel && answer.CoAssessmentStatus == BenchmarkAssessmentStatus.Scored)
        {
            var record = BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson);
            graders.Add(new BenchmarkReportContentGrader
            {
                Role = BenchmarkReportFacts.PanelMemberBRole,
                Label = (answer.CoAssessedByModelSnapshot ?? run.CoAssessorModelSnapshot).Label() ?? string.Empty,
                Score = answer.CoAssessmentQualityScore ?? record?.QualityScore,
                Comment = NullIfBlank(record?.Comment),
                Evidence = EvidenceOf(
                    record?.AccuracyEvidence,
                    record?.CompletenessEvidence,
                    record?.ReadabilityEvidence,
                    record?.CriticalError == true ? record!.CriticalErrorQuote : null)
            });
        }

        if (!string.IsNullOrWhiteSpace(answer.SecondOpinionJson))
        {
            string json = answer.SecondOpinionJson;
            bool critical = ReadBool(json, "criticalError");
            graders.Add(new BenchmarkReportContentGrader
            {
                Role = panel ? BenchmarkReportFacts.ReferenceReaderRole : BenchmarkReportFacts.SecondReaderRole,
                Label = (answer.SecondOpinionByModelSnapshot ?? run.SecondOpinionAssessorModelSnapshot).Label() ?? string.Empty,
                Score = answer.SecondOpinionQualityScore,
                Comment = NullIfBlank(ReadString(json, "comment")),
                Evidence = EvidenceOf(
                    ReadString(json, "accuracyEvidence"),
                    ReadString(json, "completenessEvidence"),
                    ReadString(json, "readabilityEvidence"),
                    critical ? ReadString(json, "criticalErrorQuote") : null)
            });
        }

        return graders;
    }

    private static List<string> EvidenceOf(string? accuracy, string? completeness, string? readability, string? criticalQuote)
    {
        var evidence = new List<string>();
        if (!string.IsNullOrWhiteSpace(accuracy)) evidence.Add("Accuracy: " + accuracy.Trim());
        if (!string.IsNullOrWhiteSpace(completeness)) evidence.Add("Completeness: " + completeness.Trim());
        if (!string.IsNullOrWhiteSpace(readability)) evidence.Add("Readability: " + readability.Trim());
        if (!string.IsNullOrWhiteSpace(criticalQuote)) evidence.Add("Critical error: \"" + criticalQuote.Trim() + "\"");
        return evidence;
    }

    /// <summary>
    /// The verifier's rulings as stored, each with its effective verdict and its role
    /// (<see cref="RoleOf"/>); empty when none is stored or it is unreadable.
    /// </summary>
    private static List<BenchmarkReportContentClaimRuling> RulingsOf(BenchmarkRunAnswer answer)
    {
        var items = ReadVerifications(answer.ClaimVerificationJson);
        if (items == null) return new List<BenchmarkReportContentClaimRuling>();

        bool hasRoles = BenchmarkClaimRoles.HasRoles(items);
        return items
            .Select(v => new BenchmarkReportContentClaimRuling
            {
                Claim = v.Claim ?? string.Empty,
                Verdict = v.EffectiveVerdict.ToString().ToLowerInvariant(),
                Rationale = RationaleOf(v),
                Role = RoleOf(v, hasRoles)
            })
            .ToList();
    }

    /// <summary>The basis and, when it says something the basis does not, the citation in parentheses.</summary>
    internal static string? RationaleOf(BenchmarkClaimVerification v)
    {
        string? basis = NullIfBlank(v.Basis)?.Trim();
        string? citation = NullIfBlank(v.Citation)?.Trim();
        if (basis != null && citation != null && !string.Equals(basis, citation, StringComparison.Ordinal))
        {
            return basis + " (" + citation + ")";
        }
        return basis ?? citation;
    }

    private static string? ReadString(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(property, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool ReadBool(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(property, out var value)
                && value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
