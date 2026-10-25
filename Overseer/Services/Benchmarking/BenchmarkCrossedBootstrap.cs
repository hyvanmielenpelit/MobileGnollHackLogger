namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using Overseer.Services.ChatConsistency;

/// <summary>
/// One question of a crossed run × question array: the fixed weight it carries in every run's index,
/// and its score in each run that answered it. A run absent from <see cref="ScoreByRun"/> skipped it.
/// </summary>
public readonly record struct CrossedItem(double Weight, IReadOnlyDictionary<long, double> ScoreByRun);

/// <summary>
/// One question answered by both groups of a comparison: one weight for both sides, and each side's
/// score in each of its runs that answered it.
/// </summary>
public readonly record struct CrossedPair(
    double Weight,
    IReadOnlyDictionary<long, double> BaselineScoreByRun,
    IReadOnlyDictionary<long, double> TreatmentScoreByRun);

/// <summary>
/// The pigeonhole bootstrap for a crossed run × question design (Owen 2007, <i>The pigeonhole
/// bootstrap</i>, Ann. Appl. Stat. 1(2); Owen &amp; Eckles 2012). Pure: no I/O, and every draw comes
/// from a <see cref="Random"/> seeded by a parameter, so fixed inputs give fixed results.
///
/// <para>The statistic is the multi-run Intelligence Index: the mean, over the runs, of each run's
/// weighted index <c>Σ w·score / Σ w</c> over the questions it answered. Each replicate draws the
/// questions with replacement once and the runs with replacement once, and recomputes that statistic
/// on the resampled array. Both random effects and their residual are then represented once each,
/// so the variance is mildly conservative and never anti-conservative, unlike adding a run-level and
/// a question-level standard error, which counts the residual twice.</para>
///
/// <para>The question draw and each group's run draw use separate random streams derived from the
/// seed, so switching run resampling off leaves the question draws unchanged.</para>
/// </summary>
public static class BenchmarkCrossedBootstrap
{
    /// <summary>The default number of bootstrap replicates.</summary>
    public const int DefaultReplicates = 10_000;

    /// <summary>The default seed, so a fixed group gives a fixed interval.</summary>
    public const int DefaultSeed = 20261025;

    /// <summary>
    /// The bootstrap distribution of one group's index. With <paramref name="resampleRuns"/> each
    /// replicate redraws the questions and the runs; without it, only the questions, which is the
    /// item-sampling interval alone.
    ///
    /// <para>A run with no score on any question is not a cluster and is left out. A drawn run that
    /// answered none of the drawn questions is left out of that replicate's mean, and a replicate with
    /// no run left yields no value. Null when no run has a score.</para>
    /// </summary>
    public static BootstrapDistribution? IndexDistribution(
        IReadOnlyList<CrossedItem> items,
        IReadOnlyList<long> runIds,
        bool resampleRuns,
        int replicates = DefaultReplicates,
        int seed = DefaultSeed)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(runIds);
        RequireReplicates(replicates);

        var weights = Weights(items.Select(i => i.Weight));
        var grid = Grid.Build(weights, items.Select(i => i.ScoreByRun).ToList(), runIds);
        if (grid.Items == 0 || grid.Runs == 0) return null;

        int n = grid.Items;
        var questionIndices = new int[n];
        var questionCounts = new int[n];
        var runIndices = new int[grid.Runs];
        var runCounts = new int[grid.Runs];

        Identity(questionIndices, questionCounts);
        Identity(runIndices, runCounts);
        double estimate = GroupIndex(grid, questionCounts, runCounts);
        if (double.IsNaN(estimate)) return null;

        var questionRng = new Random(seed);
        var runRng = new Random(StreamSeed(seed, 1));
        var values = new double[replicates];
        for (int b = 0; b < replicates; b++)
        {
            Draw(questionRng, questionIndices, questionCounts);
            if (resampleRuns) Draw(runRng, runIndices, runCounts);
            values[b] = GroupIndex(grid, questionCounts, runCounts);
        }

        return new BootstrapDistribution(estimate, values, replicates, new[] { grid.Runs });
    }

    /// <summary>
    /// The bootstrap distribution of a difference between two groups' indices, treatment minus
    /// baseline. Each replicate draws the questions once and uses that draw for both groups, which
    /// keeps the pairing by question; with <paramref name="resampleRuns"/> it then redraws the
    /// baseline runs and the treatment runs independently.
    ///
    /// <para>Runs are handled as <see cref="IndexDistribution"/> handles them. Null when either group
    /// has no run with a score.</para>
    /// </summary>
    public static BootstrapDistribution? DifferenceDistribution(
        IReadOnlyList<CrossedPair> pairs,
        IReadOnlyList<long> baselineRunIds,
        IReadOnlyList<long> treatmentRunIds,
        bool resampleRuns,
        int replicates = DefaultReplicates,
        int seed = DefaultSeed)
        => DifferenceDistribution(pairs, baselineRunIds, treatmentRunIds, resampleRuns, replicates, seed, drawObserver: null);

    /// <summary>
    /// <see cref="DifferenceDistribution(IReadOnlyList{CrossedPair}, IReadOnlyList{long}, IReadOnlyList{long}, bool, int, int)"/>
    /// with an observer called after each replicate's draws with the drawn question positions in
    /// <paramref name="pairs"/>, the drawn baseline run ids and the drawn treatment run ids. The
    /// arrays are reused between replicates.
    /// </summary>
    internal static BootstrapDistribution? DifferenceDistribution(
        IReadOnlyList<CrossedPair> pairs,
        IReadOnlyList<long> baselineRunIds,
        IReadOnlyList<long> treatmentRunIds,
        bool resampleRuns,
        int replicates,
        int seed,
        Action<int[], long[], long[]>? drawObserver)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(baselineRunIds);
        ArgumentNullException.ThrowIfNull(treatmentRunIds);
        RequireReplicates(replicates);

        var weights = Weights(pairs.Select(p => p.Weight));
        var baseline = Grid.Build(weights, pairs.Select(p => p.BaselineScoreByRun).ToList(), baselineRunIds);
        var treatment = Grid.Build(weights, pairs.Select(p => p.TreatmentScoreByRun).ToList(), treatmentRunIds);
        if (weights.Length == 0 || baseline.Runs == 0 || treatment.Runs == 0) return null;

        int n = weights.Length;
        var questionIndices = new int[n];
        var questionCounts = new int[n];
        var baselineIndices = new int[baseline.Runs];
        var baselineCounts = new int[baseline.Runs];
        var treatmentIndices = new int[treatment.Runs];
        var treatmentCounts = new int[treatment.Runs];

        Identity(questionIndices, questionCounts);
        Identity(baselineIndices, baselineCounts);
        Identity(treatmentIndices, treatmentCounts);
        double estimate = GroupIndex(treatment, questionCounts, treatmentCounts)
                          - GroupIndex(baseline, questionCounts, baselineCounts);
        if (double.IsNaN(estimate)) return null;

        long[]? observedBaseline = drawObserver != null ? new long[baseline.Runs] : null;
        long[]? observedTreatment = drawObserver != null ? new long[treatment.Runs] : null;

        var questionRng = new Random(seed);
        var baselineRng = new Random(StreamSeed(seed, 1));
        var treatmentRng = new Random(StreamSeed(seed, 2));
        var values = new double[replicates];
        for (int b = 0; b < replicates; b++)
        {
            Draw(questionRng, questionIndices, questionCounts);
            if (resampleRuns)
            {
                Draw(baselineRng, baselineIndices, baselineCounts);
                Draw(treatmentRng, treatmentIndices, treatmentCounts);
            }

            if (drawObserver != null)
            {
                for (int i = 0; i < baselineIndices.Length; i++) observedBaseline![i] = baseline.RunIds[baselineIndices[i]];
                for (int i = 0; i < treatmentIndices.Length; i++) observedTreatment![i] = treatment.RunIds[treatmentIndices[i]];
                drawObserver(questionIndices, observedBaseline!, observedTreatment!);
            }

            values[b] = GroupIndex(treatment, questionCounts, treatmentCounts)
                        - GroupIndex(baseline, questionCounts, baselineCounts);
        }

        return new BootstrapDistribution(estimate, values, replicates, new[] { baseline.Runs, treatment.Runs });
    }

    // --- Internals ------------------------------------------------------------------------------

    /// <summary>
    /// The run × question array, row-major by run, NaN where a run did not answer. Only runs with
    /// at least one score are rows; the question axis keeps every input position.
    /// </summary>
    private sealed class Grid
    {
        public required double[] Cells { get; init; }
        public required double[] Weights { get; init; }
        public required long[] RunIds { get; init; }
        public int Runs => RunIds.Length;
        public int Items => Weights.Length;

        public static Grid Build(double[] weights, IReadOnlyList<IReadOnlyDictionary<long, double>> scoreMaps, IReadOnlyList<long> runIds)
        {
            var candidates = runIds.Distinct().ToList();
            var kept = candidates
                .Where(run => scoreMaps.Any(map => map != null && map.TryGetValue(run, out double s) && double.IsFinite(s)))
                .ToArray();

            var cells = new double[kept.Length * weights.Length];
            for (int r = 0; r < kept.Length; r++)
            {
                for (int q = 0; q < weights.Length; q++)
                {
                    var map = scoreMaps[q];
                    cells[r * weights.Length + q] = map != null && map.TryGetValue(kept[r], out double s) && double.IsFinite(s)
                        ? s
                        : double.NaN;
                }
            }

            return new Grid { Cells = cells, Weights = weights, RunIds = kept };
        }
    }

    /// <summary>
    /// The mean over the drawn runs, each counted as often as drawn, of each run's weighted index
    /// over the drawn questions, each counted as often as drawn. NaN when no drawn run answered a
    /// drawn question.
    /// </summary>
    private static double GroupIndex(Grid grid, int[] questionCounts, int[] runCounts)
    {
        double sum = 0.0;
        int count = 0;
        int n = grid.Items;

        for (int r = 0; r < grid.Runs; r++)
        {
            int multiplicity = runCounts[r];
            if (multiplicity == 0) continue;

            double weighted = 0.0;
            double weights = 0.0;
            int offset = r * n;
            for (int q = 0; q < n; q++)
            {
                int drawn = questionCounts[q];
                if (drawn == 0) continue;

                double score = grid.Cells[offset + q];
                if (double.IsNaN(score)) continue;

                double w = drawn * grid.Weights[q];
                weighted += w * score;
                weights += w;
            }

            if (weights <= 0.0) continue;

            sum += multiplicity * (weighted / weights);
            count += multiplicity;
        }

        return count > 0 ? sum / count : double.NaN;
    }

    /// <summary>Draws <c>indices.Length</c> positions with replacement and tallies them.</summary>
    private static void Draw(Random rng, int[] indices, int[] counts)
    {
        Array.Clear(counts);
        int n = indices.Length;
        for (int i = 0; i < n; i++)
        {
            int k = rng.Next(n);
            indices[i] = k;
            counts[k]++;
        }
    }

    /// <summary>Every position once: the original data.</summary>
    private static void Identity(int[] indices, int[] counts)
    {
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
            counts[i] = 1;
        }
    }

    /// <summary>The seed of a run stream, distinct from the question stream's.</summary>
    private static int StreamSeed(int seed, int stream) => unchecked(seed * 31 + stream);

    private static double[] Weights(IEnumerable<double> weights)
    {
        var result = weights.ToArray();
        foreach (double w in result)
        {
            if (!double.IsFinite(w) || w <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(weights), w, "A question weight must be a positive finite number.");
            }
        }

        return result;
    }

    private static void RequireReplicates(int replicates)
    {
        if (replicates < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(replicates), replicates, "At least one replicate is required.");
        }
    }
}
