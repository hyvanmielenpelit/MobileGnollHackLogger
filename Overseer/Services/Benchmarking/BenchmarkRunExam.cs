namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;

/// <summary>
/// The exam a set of runs sat, built from their own answer rows: one question per
/// <see cref="BenchmarkItemAnalysis.QuestionKey"/>, with the text, order, rubric, revision and weight
/// the runs recorded. Statistics over runs compute over this rather than the live suite, so adding,
/// editing, deleting or reordering questions, and renaming or deleting the suite, change nothing an
/// earlier set of runs shows.
/// </summary>
/// <remarks>
/// The returned suite and questions are <b>detached</b>: created here, never added to a
/// <c>DbContext</c>, and never to be assigned to a tracked entity's navigation. They carry only what
/// <see cref="BenchmarkItemAnalysis"/> and <see cref="BenchmarkGroupStatistics"/> read.
/// The runs must carry full answer rows; a projection that omits the question key, revision or
/// scores is not a valid input.
/// </remarks>
public static class BenchmarkRunExam
{
    public static (BenchmarkSuite Suite, IReadOnlyList<BenchmarkQuestion> Questions) Build(IReadOnlyCollection<BenchmarkRun> runs)
    {
        runs ??= Array.Empty<BenchmarkRun>();

        var newestFirst = runs
            .OrderByDescending(r => r.StartedAtUtc)
            .ThenByDescending(r => r.Id)
            .ToList();

        var newestRun = newestFirst.FirstOrDefault();
        var suite = new BenchmarkSuite
        {
            Id = newestRun?.BenchmarkSuiteIdUsed ?? newestRun?.BenchmarkSuiteId ?? 0,
            Name = newestRun?.SuiteName ?? string.Empty
        };

        // Answers in newest-run-first order, so the first answer per key is the newest run's. Each
        // carries its run's panel flag, which decides which score is its published one.
        var byKey = newestFirst
            .SelectMany(r =>
            {
                bool isPanelRun = BenchmarkRunFinalizer.IsPanelRun(r);
                return (r.Answers ?? new List<BenchmarkRunAnswer>()).Select(a => (Answer: a, IsPanelRun: isPanelRun));
            })
            .Select(x => (Key: BenchmarkItemAnalysis.QuestionKey(x.Answer), x.Answer, x.IsPanelRun))
            .Where(x => x.Key.HasValue)
            .GroupBy(x => x.Key!.Value, x => (x.Answer, x.IsPanelRun));

        var questions = new List<BenchmarkQuestion>();
        foreach (var group in byKey)
        {
            var entries = group.ToList();
            var answers = entries.Select(e => e.Answer).ToList();
            var newest = answers[0];

            var counting = entries
                .Where(e => BenchmarkRunFinalizer.CountsTowardQualityIndex(e.Answer)
                            && BenchmarkScoring.IndexQuality(e.Answer, e.IsPanelRun).HasValue)
                .Select(e => e.Answer)
                .ToList();

            // Revisions only increase, so the highest graded revision is the one the set's counted
            // answers were graded under whenever they agree. 0 means unknown.
            int itemRevision = counting.Select(a => a.ItemRevisionUsed).Where(r => r.HasValue).Max()
                ?? answers.Select(a => a.ItemRevisionUsed).Where(r => r.HasValue).Max()
                ?? 0;

            var weights = answers.Where(a => a.AssessedDifficulty.HasValue).Select(a => a.AssessedDifficulty!.Value).ToList();

            questions.Add(new BenchmarkQuestion
            {
                Id = group.Key,
                BenchmarkSuiteId = suite.Id,
                OrderIndex = newest.OrderIndex,
                QuestionText = newest.QuestionText,
                Difficulty = newest.Difficulty,
                ExpectedPoints = newest.ExpectedPointsUsed,
                ItemRevision = itemRevision,
                AssessedDifficulty = weights.Count > 0
                    ? (int)Math.Round(weights.Average(), MidpointRounding.AwayFromZero)
                    : null
            });
        }

        return (suite, questions.OrderBy(q => q.OrderIndex).ThenBy(q => q.Id).ToList());
    }
}
