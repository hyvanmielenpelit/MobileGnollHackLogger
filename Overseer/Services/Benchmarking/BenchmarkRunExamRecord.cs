namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;

/// <summary>
/// The rubric and board a run was asked and graded with, as the run itself recorded them. Every
/// grading and re-run path reads them through here, never through the live suite, so editing,
/// deleting or reordering questions, or replacing or editing the suite's board, changes nothing an
/// existing run is graded against. The accessors throw rather than fall back: an operation on an
/// existing run checks <see cref="RefusalFor"/> first, so a path that reaches an unrecorded value is a
/// defect, not a condition to recover from.
/// </summary>
public static class BenchmarkRunExamRecord
{
    public const string RubricNotRecordedRefusal =
        "Refused: the rubric these answers were graded against was not recorded, and the question has since been edited or deleted. "
        + "Re-grading against today's rubric would grade an old answer against a different answer key. Start a new run instead.";

    public const string BoardNotRecordedRefusal =
        "Refused: the board this run was made with was not recorded, and the suite's board has since been replaced, edited or deleted. "
        + "Re-grading against another board would grade an old answer against different ground truth. Start a new run instead.";

    /// <summary>The rubric <paramref name="answer"/> is graded against; null when the question had no rubric points.</summary>
    public static string? Rubric(BenchmarkRunAnswer answer)
    {
        if (!answer.ExpectedPointsRecorded)
        {
            throw new InvalidOperationException(
                $"Benchmark answer {answer.Id} (Q{answer.OrderIndex}) has no recorded rubric; the operation should have been refused.");
        }

        return answer.ExpectedPointsUsed;
    }

    /// <summary>True when the run was made with a board whose record is not stored.</summary>
    public static bool BoardUnknown(BenchmarkRun run)
        => run.GameSnapshotSha256Used != null && run.BoardSnapshotId == null;

    /// <summary>
    /// The board <paramref name="run"/> was made with, or null when it had none. Throws when the board
    /// is unknown, or recorded but not loaded with the run.
    /// </summary>
    public static BenchmarkRunBoardSnapshot? Board(BenchmarkRun run)
    {
        if (run.GameSnapshotSha256Used == null)
            return null;

        if (run.BoardSnapshotId == null)
        {
            throw new InvalidOperationException(
                $"Benchmark run {run.Id}: the board it was made with was not recorded; the operation should have been refused.");
        }

        if (run.BoardSnapshot == null)
        {
            throw new InvalidOperationException(
                $"Benchmark run {run.Id}: board record {run.BoardSnapshotId} was not loaded with the run; refusing to grade without the board.");
        }

        return run.BoardSnapshot;
    }

    /// <summary>
    /// Null when every rubric in <paramref name="answers"/> and the run's board are recorded;
    /// otherwise the refusal, naming the questions by their stored order index.
    /// </summary>
    public static string? RefusalFor(BenchmarkRun run, IEnumerable<BenchmarkRunAnswer> answers)
    {
        if (BoardUnknown(run))
            return BoardNotRecordedRefusal;

        var unrecorded = answers
            .Where(a => !a.ExpectedPointsRecorded)
            .Select(a => a.OrderIndex)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        if (unrecorded.Count == 0)
            return null;

        return RubricNotRecordedRefusal + " Questions: " + string.Join(", ", unrecorded.Select(i => "Q" + i)) + ".";
    }
}
