namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;

/// <summary>
/// Advisory findings about one suite item. Every flag is a prompt for a human to look, never a
/// score adjustment and never a suite edit: the panel that shows these has no write action.
/// </summary>
[Flags]
public enum BenchmarkItemFlags
{
    None = 0,

    /// <summary>Every model scores near the ceiling. The item carries little information.</summary>
    Saturated = 1,

    /// <summary>
    /// The a priori difficulty and the empirical one disagree materially. This matters because
    /// the a priori figure is what weights the Intelligence Index.
    /// </summary>
    Miscalibrated = 2,

    /// <summary>Wide spread. Either genuinely discriminating or ambiguous; a human decides.</summary>
    Unstable = 4,

    /// <summary>Most runs reached or nearly reached the tool budget. The cap may be scoring.</summary>
    BudgetBound = 8,

    /// <summary>
    /// More than one assessor graded this item's runs, so its spread mixes candidate ability with
    /// grader severity. Confounds every other statistic on the row.
    /// </summary>
    AssessorConfounded = 16,

    /// <summary>
    /// More than one scoring method version. Method 5 permitted an Accuracy deduction for a claim
    /// the assessor could not verify and method 6 forbids it, so two runs across that boundary are
    /// not the same measurement. Confounds every other statistic on the row.
    /// </summary>
    ScoringMethodMixed = 32
}

/// <summary>
/// Statistics for one item, over the runs of its suite. All advisory; see the non-writeback rule
/// on <see cref="BenchmarkItemStatistics.EmpiricalDifficulty"/>.
/// </summary>
public sealed record BenchmarkItemStatistics
{
    public long QuestionId { get; init; }
    public int OrderIndex { get; init; }
    public string QuestionText { get; init; } = string.Empty;
    public BenchmarkDifficulty AuthoredDifficulty { get; init; }
    public int ItemRevision { get; init; }

    /// <summary>
    /// Sample size and its confounds. All four travel together everywhere these are shown: a mean
    /// over three runs by one model says something quite different from the same mean over twelve
    /// runs by four models, and neither is visible from the mean.
    /// </summary>
    public int RunCount { get; init; }
    public int DistinctModelCount { get; init; }
    public int DistinctAssessorCount { get; init; }
    public int DistinctScoringMethodVersionCount { get; init; }

    /// <summary>
    /// Answers included whose <c>ItemRevisionUsed</c> was null — produced before the column
    /// existed, so the revision they were answered against is unknowable. Reported rather than
    /// assumed to be the current revision, and rather than dropped, which would empty the table
    /// for every existing suite.
    /// </summary>
    public int UnknownRevisionCount { get; init; }

    public double MeanQuality { get; init; }
    public int MinQuality { get; init; }
    public int MaxQuality { get; init; }

    /// <summary>Population standard deviation. 0 for a single run.</summary>
    public double StdDev { get; init; }

    /// <summary>
    /// <c>100 − MeanQuality</c>: how hard the item turned out to be.
    ///
    /// **This is never written back into <see cref="BenchmarkQuestion.AssessedDifficulty"/>** —
    /// not automatically, and not by a one-click action, because no such action exists.
    /// `AssessedDifficulty` weights the Intelligence Index, so deriving it from the scores it
    /// weights is circular: a model that did badly on an item would retroactively reduce that
    /// item's weight, flattering the very run that produced the number. The delta is *reported*
    /// so a human can re-author the question or re-rate it deliberately.
    /// </summary>
    public int EmpiricalDifficulty { get; init; }

    public int? AssessedDifficulty { get; init; }
    public int? DifficultyDelta { get; init; }

    /// <summary>
    /// Mean quality among the runs in the top half by Intelligence Index, minus the bottom half.
    /// Null below <see cref="BenchmarkItemAnalysis.MinRunsForDiscrimination"/> runs, where the
    /// split is between one run and one run and the figure means nothing.
    /// </summary>
    public double? Discrimination { get; init; }

    public double MeanToolCalls { get; init; }

    /// <summary>Fraction of runs that reached at least 90% of this question's tool budget.</summary>
    public double BudgetBoundFraction { get; init; }

    public BenchmarkItemFlags Flags { get; init; }

    /// <summary>
    /// True when a confound flag fired. Every other statistic on the row is then a mixture of
    /// sources that cannot be separated, and must be presented as confounded rather than as a
    /// measurement.
    /// </summary>
    public bool Confounded { get; init; }

    /// <summary>True when the sample is too small for any of this to be read as a measurement.</summary>
    public bool InsufficientData { get; init; }
}

/// <summary>Suite-level context for an item table. Read the banner before the rows.</summary>
public sealed record BenchmarkSuiteItemAnalysis
{
    public long SuiteId { get; init; }
    public string SuiteName { get; init; } = string.Empty;
    public int QuestionCount { get; init; }

    public int RunCount { get; init; }
    public int DistinctModelCount { get; init; }
    public int DistinctAssessorCount { get; init; }
    public int DistinctScoringMethodVersionCount { get; init; }

    /// <summary>
    /// Answers that could be tied to a question and answers that could not. An unlinked answer is
    /// excluded from every statistic here — a historical answer the backfill could not match
    /// unambiguously, or one whose question has since been deleted.
    /// </summary>
    public int LinkedAnswerCount { get; init; }
    public int UnlinkedAnswerCount { get; init; }

    public IReadOnlyList<BenchmarkItemStatistics> Items { get; init; } = Array.Empty<BenchmarkItemStatistics>();
}

/// <summary>
/// Item analysis over stored runs. Pure computation: no AI calls, no database access, no writes.
///
/// The whole point of this class is to say where a suite is weak — items every model aces, items
/// whose a priori weight does not match how they actually behave, items whose score may be set by
/// a tool budget rather than by the model. It deliberately reports rather than acts: see the
/// non-writeback rule on <see cref="BenchmarkItemStatistics.EmpiricalDifficulty"/>.
/// </summary>
public static class BenchmarkItemAnalysis
{
    /// <summary>Mean quality at or above which an item is treated as saturated.</summary>
    public const double SaturatedMeanQuality = 97.0;

    /// <summary>Spread at or below which a high mean means saturation rather than luck.</summary>
    public const int SaturatedSpread = 3;

    /// <summary>Difficulty gap at which the a priori rating and the empirical one disagree.</summary>
    public const int MiscalibratedDelta = 25;

    /// <summary>Spread at or above which an item is unstable.</summary>
    public const int UnstableSpread = 30;

    /// <summary>Budget fraction at which a question counts as budget-bound for one run.</summary>
    public const double BudgetPressureFraction = 0.90;

    /// <summary>Fraction of runs that must be budget-bound before the item is flagged.</summary>
    public const double BudgetBoundRunFraction = 0.5;

    /// <summary>
    /// Below this, <see cref="BenchmarkItemStatistics.Discrimination"/> is suppressed. A top-half
    /// minus bottom-half comparison over three runs is a comparison of one run with one run.
    /// </summary>
    public const int MinRunsForDiscrimination = 4;

    /// <summary>
    /// Below this, every statistic on a row is marked <c>InsufficientData</c>. The row is still
    /// shown — one run is worth seeing — but nothing on it is a measurement.
    /// </summary>
    public const int MinRunsForMeasurement = 4;

    /// <summary>
    /// Computes the table. <paramref name="runs"/> must have their <c>Answers</c> loaded; only
    /// runs of this suite are considered, and only answers linked to one of
    /// <paramref name="questions"/>.
    /// </summary>
    public static BenchmarkSuiteItemAnalysis Compute(
        BenchmarkSuite suite,
        IReadOnlyCollection<BenchmarkQuestion> questions,
        IReadOnlyCollection<BenchmarkRun> runs)
    {
        ArgumentNullException.ThrowIfNull(suite);
        questions ??= Array.Empty<BenchmarkQuestion>();
        runs ??= Array.Empty<BenchmarkRun>();

        var questionIds = new HashSet<long>(questions.Select(q => q.Id));

        // Every answer of every considered run, whether or not it is usable, so the banner can
        // say how much of the record this analysis had to leave out.
        var allAnswers = runs.SelectMany(r => r.Answers ?? new List<BenchmarkRunAnswer>()).ToList();
        int linked = allAnswers.Count(a => a.BenchmarkQuestionId.HasValue && questionIds.Contains(a.BenchmarkQuestionId.Value));
        int unlinked = allAnswers.Count - linked;

        var items = new List<BenchmarkItemStatistics>(questions.Count);
        foreach (var question in questions.OrderBy(q => q.OrderIndex))
        {
            items.Add(ComputeItem(question, runs));
        }

        return new BenchmarkSuiteItemAnalysis
        {
            SuiteId = suite.Id,
            SuiteName = suite.Name,
            QuestionCount = questions.Count,
            RunCount = runs.Count,
            DistinctModelCount = DistinctCount(runs.Select(r => r.TestedModelIdUsed)),
            DistinctAssessorCount = DistinctCount(runs.Select(r => r.AssessorModelIdUsed)),
            DistinctScoringMethodVersionCount = runs.Select(r => r.ScoringMethodVersion).Distinct().Count(),
            LinkedAnswerCount = linked,
            UnlinkedAnswerCount = unlinked,
            Items = items.OrderByDescending(i => Math.Abs(i.DifficultyDelta ?? 0))
                .ThenBy(i => i.OrderIndex)
                .ToList()
        };
    }

    private static BenchmarkItemStatistics ComputeItem(
        BenchmarkQuestion question,
        IReadOnlyCollection<BenchmarkRun> runs)
    {
        // One sample per run: the run, and its scored answer to this question.
        //
        // A null ItemRevisionUsed is included and counted, not dropped: it means the answer
        // predates the column, so the revision it was written against is unknowable. Dropping
        // those would leave the table empty for every suite that already has runs, and assuming
        // they match the current revision would be a claim the data does not support — so the
        // count is reported instead and the banner says what fraction of the sample it is.
        var samples = new List<(BenchmarkRun Run, BenchmarkRunAnswer Answer)>();
        foreach (var run in runs)
        {
            var answer = (run.Answers ?? new List<BenchmarkRunAnswer>())
                .FirstOrDefault(a => a.BenchmarkQuestionId == question.Id
                                     && a.Status == BenchmarkAnswerStatus.Ok
                                     && a.QualityScore.HasValue
                                     && (a.ItemRevisionUsed == null || a.ItemRevisionUsed == question.ItemRevision));

            if (answer != null)
            {
                samples.Add((run, answer));
            }
        }

        int runCount = samples.Count;
        if (runCount == 0)
        {
            return new BenchmarkItemStatistics
            {
                QuestionId = question.Id,
                OrderIndex = question.OrderIndex,
                QuestionText = question.QuestionText,
                AuthoredDifficulty = question.Difficulty,
                ItemRevision = question.ItemRevision,
                AssessedDifficulty = question.AssessedDifficulty,
                InsufficientData = true
            };
        }

        var scores = samples.Select(s => s.Answer.QualityScore!.Value).ToList();
        double mean = scores.Average();
        int min = scores.Min();
        int max = scores.Max();
        double variance = scores.Sum(s => (s - mean) * (s - mean)) / scores.Count;
        double stdDev = Math.Sqrt(variance);

        int empirical = (int)Math.Round(100.0 - mean, MidpointRounding.AwayFromZero);
        int? delta = question.AssessedDifficulty.HasValue
            ? empirical - question.AssessedDifficulty.Value
            : null;

        int distinctAssessors = DistinctCount(samples.Select(s => s.Answer.AssessedByModelIdUsed ?? s.Run.AssessorModelIdUsed));
        int distinctMethods = samples.Select(s => s.Run.ScoringMethodVersion).Distinct().Count();

        var budgetBound = samples.Where(s =>
            s.Answer.ToolBudgetExhausted ||
            (s.Answer.ToolCallBudgetUsed.HasValue && s.Answer.ToolCallBudgetUsed.Value > 0
             && (s.Answer.ToolCallCount ?? 0) >= s.Answer.ToolCallBudgetUsed.Value * BudgetPressureFraction)).ToList();
        double budgetBoundFraction = budgetBound.Count / (double)runCount;

        var flags = BenchmarkItemFlags.None;
        if (mean >= SaturatedMeanQuality && (max - min) <= SaturatedSpread) flags |= BenchmarkItemFlags.Saturated;
        if (delta.HasValue && Math.Abs(delta.Value) >= MiscalibratedDelta) flags |= BenchmarkItemFlags.Miscalibrated;
        if ((max - min) >= UnstableSpread) flags |= BenchmarkItemFlags.Unstable;
        if (budgetBoundFraction >= BudgetBoundRunFraction) flags |= BenchmarkItemFlags.BudgetBound;
        if (distinctAssessors > 1) flags |= BenchmarkItemFlags.AssessorConfounded;
        if (distinctMethods > 1) flags |= BenchmarkItemFlags.ScoringMethodMixed;

        bool confounded = (flags & (BenchmarkItemFlags.AssessorConfounded | BenchmarkItemFlags.ScoringMethodMixed)) != 0;

        return new BenchmarkItemStatistics
        {
            QuestionId = question.Id,
            OrderIndex = question.OrderIndex,
            QuestionText = question.QuestionText,
            AuthoredDifficulty = question.Difficulty,
            ItemRevision = question.ItemRevision,
            RunCount = runCount,
            DistinctModelCount = DistinctCount(samples.Select(s => s.Run.TestedModelIdUsed)),
            DistinctAssessorCount = distinctAssessors,
            DistinctScoringMethodVersionCount = distinctMethods,
            UnknownRevisionCount = samples.Count(s => s.Answer.ItemRevisionUsed == null),
            MeanQuality = mean,
            MinQuality = min,
            MaxQuality = max,
            StdDev = stdDev,
            EmpiricalDifficulty = empirical,
            AssessedDifficulty = question.AssessedDifficulty,
            DifficultyDelta = delta,
            Discrimination = Discrimination(samples),
            MeanToolCalls = samples.Average(s => (double)(s.Answer.ToolCallCount ?? 0)),
            BudgetBoundFraction = budgetBoundFraction,
            Flags = flags,
            Confounded = confounded,
            InsufficientData = runCount < MinRunsForMeasurement
        };
    }

    /// <summary>
    /// Top half minus bottom half, ranked by the run's own Intelligence Index. A positive value
    /// means the item separates stronger runs from weaker ones, which is what an item is for; a
    /// value near zero means it does not.
    ///
    /// Null below <see cref="MinRunsForDiscrimination"/>, and null when the runs carry no index to
    /// rank by — a rank over unranked runs would be an arbitrary split reported as a measurement.
    /// </summary>
    private static double? Discrimination(List<(BenchmarkRun Run, BenchmarkRunAnswer Answer)> samples)
    {
        if (samples.Count < MinRunsForDiscrimination) return null;

        var ranked = samples
            .Where(s => s.Run.QualityIndex.HasValue)
            .OrderByDescending(s => s.Run.QualityIndex!.Value)
            .ThenBy(s => s.Run.Id)
            .ToList();

        if (ranked.Count < MinRunsForDiscrimination) return null;

        int half = ranked.Count / 2;
        double top = ranked.Take(half).Average(s => (double)s.Answer.QualityScore!.Value);
        double bottom = ranked.Skip(ranked.Count - half).Average(s => (double)s.Answer.QualityScore!.Value);
        return top - bottom;
    }

    private static int DistinctCount(IEnumerable<string?> values)
    {
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }
}
