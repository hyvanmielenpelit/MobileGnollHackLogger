namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>One grader's verdict on an answer, as a model's failure profile keeps it for the weakest answers.</summary>
public sealed record BenchmarkComparisonModelGraderNote(string Role, bool CriticalError, string Comment);

/// <summary>
/// One item a covered model answered: its question and item revision, the lowest published score its
/// runs gave it, that run, and each grader's comment on that run's answer.
/// </summary>
public sealed record BenchmarkComparisonModelItem(
    string QuestionKey, int? ItemRevisionUsed, double LowestScore, long RunId, IReadOnlyList<BenchmarkComparisonModelGraderNote> Notes);

/// <summary>
/// The failure profile of one covered model of a comparison-scope document, or of a per-model sheet's
/// subject, counted over its scored
/// answers in all its runs: what the graders recorded went wrong, never why. In a panel run a level is
/// the mean of both members' levels, otherwise the assessor's own. Pure: no I/O, no clock, no culture.
///
/// Fact keys, under <c>model.&lt;L&gt;.profile.</c> (<see cref="AddFacts"/> and <see cref="AddMatrixFacts"/>), or
/// <c>profile.</c> on a per-model sheet (<see cref="AddModelScopeFacts"/>):
///
///   answers                                        the scored answers, the denominator
///   mayMislead, mayMisleadPer20                    accuracy level below 4, or a critical error
///   outcome.correct, .partial, .incorrect, .notAttempted, .noAnswer, .wrongInsteadOfAbstaining
///                                                  <see cref="BenchmarkOutcomeSummary.Classify"/>; from scoring method 13
///   deficit.accuracy, .completeness, .conciseness, .readability   level 2 or below on the dimension
///   top.accuracy, .completeness, .conciseness, .readability       at level 6 on the dimension; in a panel run the
///                                                  mean of both members' levels at 5.5 or above. A per-model sheet
///                                                  also carries each as dimension.&lt;d&gt;.topLevel, for the Executive Summary
///   truncated, outputDefects, noToolAnswers, toolLimitHits
///   answerChars.median                             the median answer length in characters
///   isolatedMisses, sharedMisses, lowestScorer     from the question matrix; three covered models or more
///   findings.runsWithSynthesis, findings.&lt;category&gt;   runs whose final synthesis raised a weakness of the category
/// </summary>
public sealed class BenchmarkComparisonModelProfile
{
    /// <summary>The part of a model's fact key after its letter: <c>model.A.profile.answers</c>.</summary>
    public const string KeyPrefix = "profile.";

    /// <summary>An answer whose accuracy level is below this may mislead a player.</summary>
    public const double MayMisleadAccuracyBelow = 4.0;

    /// <summary>A dimension at this level or below is a deficit.</summary>
    public const double DeficitLevelAtMost = 2.0;

    /// <summary>A dimension at this level or above is at the top level, 6; a panel run's mean of the two members' levels counts from here.</summary>
    public const double TopLevelAtLeast = 5.5;

    /// <summary>An isolated miss: the model scored at most this…</summary>
    public const double IsolatedMissAtMost = 50.0;

    /// <summary>…where the other models' median is at least this…</summary>
    public const double IsolatedMissOthersMedianAtLeast = 75.0;

    /// <summary>…and at least this many other models scored the question.</summary>
    public const int IsolatedMissMinOthers = 2;

    /// <summary>A shared miss: the model scored below this, and so did at least half the others.</summary>
    public const double SharedMissBelow = 50.0;

    /// <summary>The matrix counts need at least this many covered models.</summary>
    public const int MatrixMinModels = 3;

    /// <summary>A weakest answer's grader comment is cut at a word boundary to at most this many characters.</summary>
    public const int CommentChars = 300;

    /// <summary>The answers per which <c>mayMisleadPer20</c> is stated.</summary>
    public const int Per = 20;

    /// <summary><c>outcome.wrongInsteadOfAbstaining</c> is stated over at least this many incorrect or not attempted answers.</summary>
    public const int WrongInsteadOfAbstainingMinAnswers = 3;

    /// <summary>The dimension keys, in report order.</summary>
    public static readonly IReadOnlyList<string> Dimensions = new[] { "accuracy", "completeness", "conciseness", "readability" };

    /// <summary>The synthesis finding categories (<see cref="BenchmarkAssessmentParser.SynthesisFindingCategories"/>).</summary>
    public static IReadOnlyList<string> FindingCategories => BenchmarkAssessmentParser.SynthesisFindingCategories;

    /// <summary>The termination reasons of an answer that ran into the tool or iteration limit.</summary>
    private static readonly string[] LimitTerminations = { "budget_exhausted", "iteration_limit" };

    private const BenchmarkAnswerFlags DefectFlags = BenchmarkAnswerFlags.Empty | BenchmarkAnswerFlags.Truncated
        | BenchmarkAnswerFlags.HarnessArtifacts | BenchmarkAnswerFlags.ReasoningBleed | BenchmarkAnswerFlags.RepeatedFragments;

    /// <summary>The scored answers: those counting toward the index with a published score.</summary>
    public int Answers { get; init; }

    /// <summary>Scored answers with an accuracy level below <see cref="MayMisleadAccuracyBelow"/> or a critical error.</summary>
    public int MayMislead { get; init; }

    /// <summary>Every run was scored under method 13 or later, so its answers have outcome classes.</summary>
    public bool OutcomesRecorded { get; init; }

    public int Correct { get; init; }
    public int Partial { get; init; }
    public int Incorrect { get; init; }
    public int NotAttempted { get; init; }
    public int NoAnswer { get; init; }

    /// <summary>Scored answers at level 2 or below, by dimension key.</summary>
    public IReadOnlyDictionary<string, int> Deficits { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Scored answers at the top level (<see cref="TopLevelAtLeast"/>), by dimension key.</summary>
    public IReadOnlyDictionary<string, int> TopLevels { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Scored answers flagged <c>Truncated</c>.</summary>
    public int Truncated { get; init; }

    /// <summary>Scored answers flagged Empty, Truncated, HarnessArtifacts, ReasoningBleed or RepeatedFragments, or not <c>Ok</c>.</summary>
    public int OutputDefects { get; init; }

    /// <summary>Scored answers with no tool call.</summary>
    public int NoToolAnswers { get; init; }

    /// <summary>Scored answers that exhausted the tool budget or ended at the budget or iteration limit.</summary>
    public int ToolLimitHits { get; init; }

    /// <summary>The median scored answer's length in characters; null without a scored answer.</summary>
    public double? MedianAnswerChars { get; init; }

    /// <summary>Scored answers both panel members graded, when every run was a panel run; null otherwise.</summary>
    public int? PanelAnswers { get; init; }

    /// <summary>Of <see cref="PanelAnswers"/>, those the panel disagreed on; null as for it.</summary>
    public int? PanelDisagreements { get; init; }

    /// <summary>The model's runs.</summary>
    public int Runs { get; init; }

    /// <summary>Runs whose final synthesis holds structured findings, member A's or member B's.</summary>
    public int RunsWithSynthesis { get; init; }

    /// <summary>Of <see cref="RunsWithSynthesis"/>, those in which either member raised a weakness of the category, by category.</summary>
    public IReadOnlyDictionary<string, int> WeaknessRunsByCategory { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Every scored item, in question-key and item-revision order.</summary>
    public IReadOnlyList<BenchmarkComparisonModelItem> Items { get; init; } = Array.Empty<BenchmarkComparisonModelItem>();

    /// <summary>The profile of a covered model over its runs, each loaded with its answers.</summary>
    public static BenchmarkComparisonModelProfile FromRuns(IReadOnlyList<BenchmarkRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var ordered = runs.Where(r => r != null).OrderBy(r => r.Id).ToList();
        var stats = BenchmarkReportFacts.EntryStats.Of(ordered);

        bool outcomes = ordered.Count > 0 && ordered.All(BenchmarkCriticalErrorResolver.Applies);
        var classes = new Dictionary<BenchmarkOutcomeClass, int>();
        var deficits = Dimensions.ToDictionary(d => d, _ => 0, StringComparer.Ordinal);
        var tops = Dimensions.ToDictionary(d => d, _ => 0, StringComparer.Ordinal);
        int mayMislead = 0, truncated = 0, defects = 0, noTool = 0, limitHits = 0, panelAnswers = 0, disagreements = 0;
        var lengths = new List<double>();

        foreach (var (run, answer, _) in stats.Scored)
        {
            bool panel = BenchmarkRunFinalizer.IsPanelRun(run);
            var levels = LevelsOf(answer, panel);

            if ((levels.TryGetValue("accuracy", out double accuracy) && accuracy < MayMisleadAccuracyBelow) || HasCriticalError(run, answer))
            {
                mayMislead++;
            }
            foreach (var (dimension, level) in levels)
            {
                if (level <= DeficitLevelAtMost) deficits[dimension]++;
                if (level >= TopLevelAtLeast) tops[dimension]++;
            }

            if (outcomes && BenchmarkOutcomeSummary.Classify(run, answer, panel) is { } outcome)
            {
                classes[outcome] = classes.GetValueOrDefault(outcome) + 1;
            }

            var flags = (BenchmarkAnswerFlags)answer.AnswerFlags;
            if (flags.HasFlag(BenchmarkAnswerFlags.Truncated)) truncated++;
            if ((flags & DefectFlags) != 0 || answer.Status != BenchmarkAnswerStatus.Ok) defects++;
            if (ToolCallsOf(answer) == 0) noTool++;
            if (answer.ToolBudgetExhausted
                || LimitTerminations.Contains((answer.TerminationReason ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase))
            {
                limitHits++;
            }
            if (!string.IsNullOrEmpty(answer.AnswerText)) lengths.Add(answer.AnswerText.Length);
            if (panel && answer.PanelDisagreed.HasValue)
            {
                panelAnswers++;
                if (answer.PanelDisagreed == true) disagreements++;
            }
        }

        var (withSynthesis, byCategory) = Findings(ordered);

        return new BenchmarkComparisonModelProfile
        {
            Answers = stats.Scored.Count,
            MayMislead = mayMislead,
            OutcomesRecorded = outcomes,
            Correct = classes.GetValueOrDefault(BenchmarkOutcomeClass.Correct),
            Partial = classes.GetValueOrDefault(BenchmarkOutcomeClass.Partial),
            Incorrect = classes.GetValueOrDefault(BenchmarkOutcomeClass.Incorrect),
            NotAttempted = classes.GetValueOrDefault(BenchmarkOutcomeClass.NotAttempted),
            NoAnswer = classes.GetValueOrDefault(BenchmarkOutcomeClass.NoAnswer),
            Deficits = deficits,
            TopLevels = tops,
            Truncated = truncated,
            OutputDefects = defects,
            NoToolAnswers = noTool,
            ToolLimitHits = limitHits,
            MedianAnswerChars = Median(lengths),
            PanelAnswers = stats.Panel ? panelAnswers : null,
            PanelDisagreements = stats.Panel ? disagreements : null,
            Runs = ordered.Count,
            RunsWithSynthesis = withSynthesis,
            WeaknessRunsByCategory = byCategory,
            Items = ItemsOf(stats)
        };
    }

    /// <summary>
    /// The answer's level on each dimension: the mean of both members' levels in a panel run where
    /// member B recorded them, otherwise the assessor's own; empty when no verdict carries levels.
    /// </summary>
    private static Dictionary<string, double> LevelsOf(BenchmarkRunAnswer answer, bool panel)
    {
        var levels = new Dictionary<string, double>(StringComparer.Ordinal);
        var memberA = BenchmarkVerdictView.FromPrimary(answer);
        if (memberA == null) return levels;

        var memberB = panel ? BenchmarkVerdictView.FromCoAssessment(answer) : null;
        double Level(Func<BenchmarkVerdictView, int> of) => memberB == null ? of(memberA) : (of(memberA) + of(memberB)) / 2.0;

        levels["accuracy"] = Level(v => v.AccuracyLevel);
        levels["completeness"] = Level(v => v.CompletenessLevel);
        levels["conciseness"] = Level(v => v.ConcisenessLevel);
        levels["readability"] = Level(v => v.ReadabilityLevel);
        return levels;
    }

    /// <summary>
    /// A critical error as the sheet counts it: a confirmed one from scoring method 13
    /// (<see cref="BenchmarkCriticalErrorResolver.IsConfirmed"/>); before it, either grader's flag.
    /// </summary>
    private static bool HasCriticalError(BenchmarkRun run, BenchmarkRunAnswer answer)
        => BenchmarkCriticalErrorResolver.Applies(run)
            ? BenchmarkCriticalErrorResolver.IsConfirmed(answer)
            : answer.CriticalError || answer.CoAssessmentCriticalError == true;

    /// <summary>The answer's attempted tool calls, else its succeeded ones.</summary>
    private static int ToolCallsOf(BenchmarkRunAnswer answer)
        => answer.ToolCallCount ?? BenchmarkChatTransfer.ToolCallCountsFor(answer).Values.Sum();

    /// <summary>Runs whose final synthesis has structured findings, and by category those of them that raised a weakness of it.</summary>
    private static (int WithSynthesis, Dictionary<string, int> ByCategory) Findings(IReadOnlyList<BenchmarkRun> runs)
    {
        var byCategory = FindingCategories.ToDictionary(c => c, _ => 0, StringComparer.Ordinal);
        int withSynthesis = 0;
        foreach (var run in runs)
        {
            var findings = BenchmarkAssessmentParser.ParseSynthesisFindings(run.AssessmentJson)
                .Concat(BenchmarkRunFinalizer.IsPanelRun(run)
                    ? BenchmarkAssessmentParser.ParseSynthesisFindings(run.CoAssessorSynthesisJson)
                    : Array.Empty<BenchmarkSynthesisFinding>())
                .ToList();
            if (findings.Count == 0) continue;

            withSynthesis++;
            foreach (string category in findings
                .Where(f => string.Equals(f.Kind, "weakness", StringComparison.Ordinal))
                .Select(f => f.Category)
                .Distinct(StringComparer.Ordinal))
            {
                if (byCategory.ContainsKey(category)) byCategory[category]++;
            }
        }
        return (withSynthesis, byCategory);
    }

    /// <summary>Each scored item with its lowest-scoring run's answer (ties to the lower run id) and that answer's grader comments.</summary>
    private static List<BenchmarkComparisonModelItem> ItemsOf(BenchmarkReportFacts.EntryStats stats)
        => stats.Scored
            .GroupBy(s => (Key: BenchmarkReportFacts.QuestionKeyOf(s.Answer), Revision: s.Answer.ItemRevisionUsed))
            .Select(g => g.OrderBy(s => s.Quality).ThenBy(s => s.Run.Id).ThenBy(s => s.Answer.Id).First())
            .OrderBy(s => s.Answer.OrderIndex)
            .ThenBy(s => BenchmarkReportFacts.QuestionKeyOf(s.Answer), StringComparer.Ordinal)
            .ThenBy(s => s.Answer.ItemRevisionUsed ?? -1)
            .Select(s => new BenchmarkComparisonModelItem(
                BenchmarkReportFacts.QuestionKeyOf(s.Answer), s.Answer.ItemRevisionUsed, s.Quality, s.Run.Id, NotesOf(s.Run, s.Answer)))
            .ToList();

    /// <summary>Each grader's comment on the answer, cut to <see cref="CommentChars"/>, with its own critical-error flag; blank comments left out.</summary>
    private static List<BenchmarkComparisonModelGraderNote> NotesOf(BenchmarkRun run, BenchmarkRunAnswer answer)
    {
        var notes = new List<BenchmarkComparisonModelGraderNote>();
        bool panel = BenchmarkRunFinalizer.IsPanelRun(run);

        if (answer.AssessmentStatus == BenchmarkAssessmentStatus.Scored && !string.IsNullOrWhiteSpace(answer.ReviewComment))
        {
            notes.Add(new BenchmarkComparisonModelGraderNote(
                panel ? BenchmarkReportFacts.PanelMemberARole : BenchmarkReportFacts.AssessorRole,
                answer.CriticalError,
                Cut(answer.ReviewComment, CommentChars)));
        }

        if (panel && answer.CoAssessmentStatus == BenchmarkAssessmentStatus.Scored
            && BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson) is { } record
            && !string.IsNullOrWhiteSpace(record.Comment))
        {
            notes.Add(new BenchmarkComparisonModelGraderNote(BenchmarkReportFacts.PanelMemberBRole, record.CriticalError, Cut(record.Comment, CommentChars)));
        }

        return notes;
    }

    /// <summary>
    /// <paramref name="text"/> on one line, trimmed, whole when it fits in <paramref name="maxChars"/>;
    /// otherwise cut back to the last whitespace that leaves room for an ellipsis (a hard cut when
    /// there is none), with the ellipsis appended, so the result is never longer than the limit.
    /// </summary>
    public static string Cut(string? text, int maxChars)
    {
        string line = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length <= maxChars) return line;
        if (maxChars <= 1) return BenchmarkReportContent.Ellipsis;

        int limit = maxChars - BenchmarkReportContent.Ellipsis.Length;
        int cut = line.LastIndexOf(' ', limit);
        string head = (cut > 0 ? line[..cut] : line[..limit]).TrimEnd();
        return head + BenchmarkReportContent.Ellipsis;
    }

    /// <summary>The median of <paramref name="values"/>, the mean of the middle two at an even count; null for none.</summary>
    public static double? Median(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0) return null;

        var sorted = values.OrderBy(v => v).ToList();
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    // ---------------------------------------------------------------------------------------------
    // Facts
    // ---------------------------------------------------------------------------------------------

    /// <summary>Why the outcome facts are unavailable for a model with a run scored before method 13.</summary>
    public const string OutcomesNotRecordedReason = "Outcome classes are not recorded before scoring method 13.";

    /// <summary>Why the findings categories are unavailable for a model whose runs hold no structured findings.</summary>
    public const string NoStructuredFindingsReason = "No run of this model recorded structured grader findings; they are recorded from harness 40.";

    /// <summary>Why the matrix counts are unavailable below <see cref="MatrixMinModels"/> covered models.</summary>
    public const string TooFewModelsReason = "Fewer than three models are covered, so a miss cannot be set against the others.";

    /// <summary>The profile's facts under <paramref name="prefix"/>, the model's own (<c>model.A.</c>); the matrix counts are <see cref="AddMatrixFacts"/>'s.</summary>
    internal void AddFacts(BenchmarkReportFacts.FactList facts, string prefix)
    {
        string p = prefix + KeyPrefix;
        string OfAnswers(int n, string qualifier) => BenchmarkReportFormat.CountOf(n, Answers, "answers", qualifier);

        facts.Add(p + "answers", Answers, Inv(Answers) + (Answers == 1 ? " answer" : " answers"));
        facts.Add(p + "mayMislead", MayMislead, OfAnswers(MayMislead, "that may mislead"));
        if (Answers > 0)
        {
            double per = (double)MayMislead / Answers * Per;
            facts.Add(p + "mayMisleadPer20", per,
                (MayMislead == 0 ? string.Empty : "about ") + BenchmarkReportFormat.CompactDecimal(per) + " in " + Inv(Per) + " answers");
        }
        else
        {
            facts.Unavailable(p + "mayMisleadPer20", "The model has no scored answer.");
        }

        var outcomes = new (string Key, int Count, string Qualifier)[]
        {
            ("correct", Correct, "graded correct"), ("partial", Partial, "graded partly correct"), ("incorrect", Incorrect, "graded incorrect"),
            ("notAttempted", NotAttempted, "graded not attempted"), ("noAnswer", NoAnswer, "that were empty")
        };
        foreach (var (key, count, qualifier) in outcomes)
        {
            if (OutcomesRecorded) facts.Add(p + "outcome." + key, count, OfAnswers(count, qualifier));
            else facts.Unavailable(p + "outcome." + key, OutcomesNotRecordedReason);
        }
        if (!OutcomesRecorded)
        {
            facts.Unavailable(p + "outcome.wrongInsteadOfAbstaining", OutcomesNotRecordedReason);
        }
        else if (Incorrect + NotAttempted == 0)
        {
            facts.Unavailable(p + "outcome.wrongInsteadOfAbstaining", "No answer was incorrect or not attempted.");
        }
        else if (Incorrect + NotAttempted < WrongInsteadOfAbstainingMinAnswers)
        {
            facts.Unavailable(p + "outcome.wrongInsteadOfAbstaining", "Fewer than " + Inv(WrongInsteadOfAbstainingMinAnswers)
                + " answers were incorrect or not attempted (" + Inv(Incorrect + NotAttempted) + "), too few for a rate.");
        }
        else
        {
            int denominator = Incorrect + NotAttempted;
            double rate = (double)Incorrect / denominator;
            facts.Add(p + "outcome.wrongInsteadOfAbstaining", rate,
                BenchmarkReportFormat.Percent(rate * 100) + " (" + Inv(Incorrect) + " of the " + Inv(denominator)
                + (denominator == 1 ? " answer that was" : " answers that were") + " incorrect or not attempted)");
        }

        foreach (string dimension in Dimensions)
        {
            int count = Deficits.GetValueOrDefault(dimension);
            facts.Add(p + "deficit." + dimension, count, OfAnswers(count, "at level " + Inv((int)DeficitLevelAtMost) + " or below on " + dimension));
        }
        foreach (string dimension in Dimensions)
        {
            int count = TopLevels.GetValueOrDefault(dimension);
            facts.Add(p + "top." + dimension, count, TopLevelDisplay(count, dimension));
        }

        facts.Add(p + "truncated", Truncated, OfAnswers(Truncated, "that were truncated"));
        facts.Add(p + "outputDefects", OutputDefects, OfAnswers(OutputDefects, "with output defects"));
        facts.Add(p + "noToolAnswers", NoToolAnswers, OfAnswers(NoToolAnswers, "without a tool call"));
        facts.Add(p + "toolLimitHits", ToolLimitHits, OfAnswers(ToolLimitHits, "that hit the tool limit"));

        if (MedianAnswerChars is double median)
        {
            facts.Add(p + "answerChars.median", median, BenchmarkReportFormat.Count(median) + " characters");
        }
        else
        {
            facts.Unavailable(p + "answerChars.median", "The model has no scored answer with text.");
        }

        facts.Add(p + "findings.runsWithSynthesis", RunsWithSynthesis,
            BenchmarkReportFormat.CountOf(RunsWithSynthesis, Runs, "runs", "with structured grader findings"));
        foreach (string category in FindingCategories)
        {
            if (RunsWithSynthesis == 0)
            {
                facts.Unavailable(p + "findings." + category, NoStructuredFindingsReason);
                continue;
            }
            int count = WeaknessRunsByCategory.GetValueOrDefault(category);
            facts.Add(p + "findings." + category, count,
                BenchmarkReportFormat.CountOf(count, RunsWithSynthesis, "runs", "with " + BenchmarkReportFactLabels.FindingCategoryWeakness(category) + " in the grader findings"));
        }
    }

    /// <summary>Why the matrix counts are unavailable on a per-model sheet.</summary>
    public const string ModelScopeMatrixReason = "A per-model report sets no miss against a question matrix of other models.";

    /// <summary>The display of a dimension's top-level count: <c>12 of 36 answers at level 6 on accuracy</c>.</summary>
    private string TopLevelDisplay(int count, string dimension)
        => BenchmarkReportFormat.CountOf(count, Answers, "answers", "at level 6 on " + dimension);

    /// <summary>
    /// The profile's facts on a per-model sheet, unprefixed (<c>profile.*</c>): every fact of
    /// <see cref="AddFacts"/>, with the matrix counts unavailable, because they need the question matrix
    /// of at least <see cref="MatrixMinModels"/> covered models; and each dimension's top-level count again as
    /// <c>dimension.&lt;d&gt;.topLevel</c>, which the Executive Summary's writer sees where it does not see the profile.
    /// </summary>
    internal void AddModelScopeFacts(BenchmarkReportFacts.FactList facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        AddFacts(facts, string.Empty);
        facts.Unavailable(KeyPrefix + "isolatedMisses", ModelScopeMatrixReason);
        facts.Unavailable(KeyPrefix + "sharedMisses", ModelScopeMatrixReason);
        facts.Unavailable(KeyPrefix + "lowestScorer", ModelScopeMatrixReason);
        foreach (string dimension in Dimensions)
        {
            int count = TopLevels.GetValueOrDefault(dimension);
            facts.Add("dimension." + dimension + BenchmarkReportFacts.TopLevelSuffix, count, TopLevelDisplay(count, dimension));
        }
    }

    /// <summary>
    /// One model's misses against the others on the matrix: isolated misses (it scored at most
    /// <see cref="IsolatedMissAtMost"/> where at least <see cref="IsolatedMissMinOthers"/> others scored the
    /// question and their median is at least <see cref="IsolatedMissOthersMedianAtLeast"/>), shared misses (it
    /// scored below <see cref="SharedMissBelow"/> and so did at least half the others that scored it), and the
    /// questions where its score is the lowest (ties count for each), each over the questions it scored and at
    /// least one other model did; the scored questions too. Null with fewer than <see cref="MatrixMinModels"/> models.
    /// </summary>
    public static (int Isolated, int Shared, int Lowest, int Scored)? MatrixCounts(
        IReadOnlyList<BenchmarkReportQuestion> questions, string letter, int coveredModels)
    {
        ArgumentNullException.ThrowIfNull(questions);
        if (coveredModels < MatrixMinModels) return null;

        int isolated = 0, shared = 0, lowest = 0, scored = 0;
        foreach (var question in questions)
        {
            var cells = (question.Models ?? new List<BenchmarkReportQuestionModelScore>()).Where(c => c.Score.HasValue).ToList();
            var own = cells.FirstOrDefault(c => string.Equals(c.Letter, letter, StringComparison.Ordinal));
            if (own == null) continue;

            var others = cells.Where(c => !string.Equals(c.Letter, letter, StringComparison.Ordinal)).Select(c => c.Score!.Value).ToList();
            if (others.Count == 0) continue;

            double score = own.Score!.Value;
            scored++;
            if (score <= IsolatedMissAtMost && others.Count >= IsolatedMissMinOthers && Median(others) >= IsolatedMissOthersMedianAtLeast) isolated++;
            if (score < SharedMissBelow && others.Count(o => o < SharedMissBelow) * 2 >= others.Count) shared++;
            if (others.All(o => o >= score)) lowest++;
        }
        return (isolated, shared, lowest, scored);
    }

    /// <summary>The matrix counts' facts under <paramref name="prefix"/>, unavailable with fewer than <see cref="MatrixMinModels"/> models.</summary>
    internal static void AddMatrixFacts(
        BenchmarkReportFacts.FactList facts, string prefix, IReadOnlyList<BenchmarkReportQuestion> questions, string letter, int coveredModels)
    {
        string p = prefix + KeyPrefix;
        var counts = MatrixCounts(questions, letter, coveredModels);
        if (counts is not { } c)
        {
            facts.Unavailable(p + "isolatedMisses", TooFewModelsReason);
            facts.Unavailable(p + "sharedMisses", TooFewModelsReason);
            facts.Unavailable(p + "lowestScorer", TooFewModelsReason);
            return;
        }

        string Of(int n, string qualifier) => BenchmarkReportFormat.CountOf(n, c.Scored, "questions", qualifier);
        facts.Add(p + "isolatedMisses", c.Isolated, Of(c.Isolated, "this model missed while the others scored well"));
        facts.Add(p + "sharedMisses", c.Shared, Of(c.Shared, "missed by this model and at least half the others"));
        facts.Add(p + "lowestScorer", c.Lowest, Of(c.Lowest, "on which this model scored lowest"));
    }

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);
}
