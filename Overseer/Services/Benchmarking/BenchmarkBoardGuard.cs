namespace Overseer.Services.Benchmarking;

using System;
using MobileGnollHackLogger.Data;

/// <summary>
/// Refuses to grade a run made with a board without that board. Every grading prompt reads the
/// board through the run's own record (<see cref="BenchmarkRunExamRecord.Board"/>), which is null
/// when the run had no board; a record that is unknown, or that the load did not include, would
/// otherwise grade rubric-only without a word, because lazy loading is off.
/// </summary>
public static class BenchmarkBoardGuard
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the run was made with a board whose record
    /// is not stored, or was not loaded with the run.
    /// </summary>
    public static void RequireBoardLoaded(BenchmarkRun run)
    {
        if (run.GameSnapshotSha256Used == null)
        {
            return;
        }

        if (run.BoardSnapshotId == null)
        {
            throw new InvalidOperationException(
                $"Benchmark run {run.Id}: {BenchmarkRunExamRecord.BoardNotRecordedRefusal}");
        }

        if (run.BoardSnapshot == null)
        {
            throw new InvalidOperationException(
                $"Benchmark run {run.Id}: board record {run.BoardSnapshotId} ('{run.GameSnapshotNameUsed}') was not loaded with the run; "
                + "refusing to grade without the board.");
        }
    }

    /// <summary>
    /// The board characters a grading prompt built from <paramref name="run"/> carries: the recorded
    /// text's length, zero when the run had a board that did not reach the prompt, and null when the
    /// run had no board.
    /// </summary>
    public static int? BoardCharsSent(BenchmarkRun run)
    {
        if (run.GameSnapshotSha256Used == null)
        {
            return null;
        }

        string? text = run.BoardSnapshot?.SanitizedText;
        return string.IsNullOrWhiteSpace(text) ? 0 : text.Length;
    }
}
