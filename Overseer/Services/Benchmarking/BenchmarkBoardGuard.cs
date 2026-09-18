namespace Overseer.Services.Benchmarking;

using System;
using MobileGnollHackLogger.Data;

/// <summary>
/// Refuses to grade a snapshot suite without its board. Every grading prompt reads the board
/// through <c>run.BenchmarkSuite?.GameSnapshot</c>, which is null both when the suite has no board
/// and when the load that fetched the run did not include it; lazy loading is off, so the second
/// case would otherwise grade rubric-only without a word.
/// </summary>
public static class BenchmarkBoardGuard
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the run's suite references a board that
    /// was not loaded with it.
    /// </summary>
    public static void RequireBoardLoaded(BenchmarkRun run)
    {
        if (run.BenchmarkSuite?.GameSnapshotId != null && run.BenchmarkSuite.GameSnapshot == null)
        {
            throw new InvalidOperationException(
                $"Benchmark run {run.Id}: suite {run.BenchmarkSuite.Id} ('{run.BenchmarkSuite.Name}') has game snapshot "
                + $"{run.BenchmarkSuite.GameSnapshotId} but it was not loaded with the run; refusing to grade without the board.");
        }
    }

    /// <summary>
    /// The board characters a grading prompt built from <paramref name="run"/> carries: the
    /// sanitized text's length, zero when the suite has a board that did not reach the prompt, and
    /// null when the suite has no board.
    /// </summary>
    public static int? BoardCharsSent(BenchmarkRun run)
    {
        var suite = run.BenchmarkSuite;
        if (suite == null || (suite.GameSnapshotId == null && suite.GameSnapshot == null))
        {
            return null;
        }

        string? text = suite.GameSnapshot?.SanitizedText;
        return string.IsNullOrWhiteSpace(text) ? 0 : text.Length;
    }
}
