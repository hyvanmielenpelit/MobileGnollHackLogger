namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MobileGnollHackLogger.Data;

/// <summary>
/// Builds the Markdown multi-run report for one analysis group.
///
/// <para><b>There is deliberately no AI-written synthesis here.</b> Every figure in this report is
/// reproducible arithmetic over stored answers, which is exactly what makes it usable as an
/// instrument for verifying a change. A cross-run narrative would add cost, a new AI path, and a
/// second place for a synthesis to contradict its own evidence — the defect the single-run report's
/// Synthesis Accuracy Divergence block exists to catch.</para>
///
/// <para>The report is built from a <see cref="BenchmarkGroupStatisticsResult"/> that a caller has
/// already computed and persisted, never from live runs. That is what keeps a group report
/// reproducible after one of its member runs has been deleted.</para>
/// </summary>
public static class BenchmarkGroupReportBuilder
{
    private static string Inv(double value, string format = "F2")
        => value.ToString(format, CultureInfo.InvariantCulture);

    private static string Inv(double? value, string format = "F2", string nullText = "—")
        => value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : nullText;

    private static string Seconds(double? ms)
        => ms.HasValue ? Inv(ms.Value / 1000.0, "F1") + " s" : "—";

    private static string Money(double? value)
        => value.HasValue ? "$" + Inv(value.Value, value.Value >= 1.0 ? "F2" : "F4") : "—";

    private static string PValue(double? p)
    {
        if (!p.HasValue) return "—";
        return p.Value < 0.0001
            ? "< 0.0001"
            : p.Value.ToString("F4", CultureInfo.InvariantCulture);
    }

    private static string TierLabel(BenchmarkComparabilityTier tier) => tier switch
    {
        BenchmarkComparabilityTier.Replicate => "Tier A — Replicate",
        BenchmarkComparabilityTier.QualityComparable => "Tier B — Quality-comparable",
        BenchmarkComparabilityTier.CrossCondition => "Tier C — Cross-condition",
        _ => "Below Tier B — not comparable"
    };

    private static string Excerpt(string? text, int max = 90)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string flat = text.Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|").Trim();
        return flat.Length <= max ? flat : flat[..max].TrimEnd() + "…";
    }

    /// <summary>
    /// Renders the full report.
    /// </summary>
    /// <param name="group">The analysed group, for its name, notes and provenance.</param>
    /// <param name="result">The persisted statistics.</param>
    /// <param name="comparability">The tier resolution, so the manifest can name what matched and what did not.</param>
    /// <param name="members">The member runs, for the manifest table. May be empty if they were deleted.</param>
    /// <param name="comparison">A paired comparison against another group, when one was requested.</param>
    /// <param name="comparisonGroupName">The other group's name, for the comparison heading.</param>
    /// <param name="overseerVersion">The build that produced the report, for the provenance line.</param>
    /// <param name="computedAtUtc">When the statistics were computed, which is not when this file was written.</param>
    public static string BuildMarkdownReport(
        BenchmarkRunGroup group,
        BenchmarkGroupStatisticsResult result,
        BenchmarkComparabilityResult? comparability,
        IReadOnlyList<BenchmarkRun>? members,
        BenchmarkGroupComparison? comparison = null,
        string? comparisonGroupName = null,
        string? overseerVersion = null,
        DateTime? computedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(result);

        var sb = new StringBuilder();

        AppendHeader(sb, group, result, comparability, overseerVersion, computedAtUtc);
        AppendManifest(sb, group, result, comparability, members);
        AppendIndex(sb, result);
        AppendItems(sb, result);
        AppendSpeed(sb, result);
        AppendCost(sb, result);

        if (comparison != null)
        {
            AppendComparison(sb, comparison, group.Name, comparisonGroupName);
        }

        AppendLimits(sb, result, comparison);

        return sb.ToString();
    }

    private static void AppendHeader(
        StringBuilder sb,
        BenchmarkRunGroup group,
        BenchmarkGroupStatisticsResult result,
        BenchmarkComparabilityResult? comparability,
        string? overseerVersion,
        DateTime? computedAtUtc)
    {
        sb.AppendLine($"# Multi-Run Benchmark Analysis — {group.Name}");
        sb.AppendLine();
        sb.AppendLine($"**Suite:** {result.SuiteName}  ");
        sb.AppendLine($"**Runs (*R*):** {result.RunCount}  ");
        sb.AppendLine($"**Items (*Q*):** {result.ItemCount}" +
                      (result.UnansweredItemCount > 0 ? $" ({result.UnansweredItemCount} unanswered by every member and excluded)" : string.Empty) + "  ");
        sb.AppendLine($"**Comparability:** {TierLabel(comparability?.Tier ?? (BenchmarkComparabilityTier)group.Tier)}  ");
        sb.AppendLine($"**Analysis computed:** {(computedAtUtc ?? DateTime.UtcNow):yyyy-MM-dd HH:mm:ss} UTC  ");
        if (!string.IsNullOrWhiteSpace(overseerVersion))
        {
            sb.AppendLine($"**Overseer version:** {overseerVersion}  ");
        }
        sb.AppendLine();
        sb.AppendLine("> Every figure in this report is arithmetic over the members' stored answers. There is no AI-written synthesis anywhere in it, by design: a report used to decide whether a change is kept has to be reproducible from its inputs.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(group.Notes))
        {
            sb.AppendLine("**Notes:** " + group.Notes.Replace("\r\n", " ").Replace("\n", " "));
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void AppendManifest(
        StringBuilder sb,
        BenchmarkRunGroup group,
        BenchmarkGroupStatisticsResult result,
        BenchmarkComparabilityResult? comparability,
        IReadOnlyList<BenchmarkRun>? members)
    {
        sb.AppendLine("## 1. Group Manifest");
        sb.AppendLine();

        if (members != null && members.Count > 0)
        {
            sb.AppendLine("| Run | Started (UTC) | Status | Index | Speed | Prompt SHA | Guides SHA | KB SHA |");
            sb.AppendLine("|---:|---|---|---:|---:|---|---|---|");
            foreach (var run in members.OrderBy(r => r.Id))
            {
                sb.AppendLine(
                    $"| {run.Id} " +
                    $"| {run.StartedAtUtc:yyyy-MM-dd HH:mm} " +
                    $"| {run.Status} " +
                    $"| {(run.QualityIndex.HasValue ? run.QualityIndex.Value.ToString(CultureInfo.InvariantCulture) : "—")} " +
                    $"| {(run.SpeedIndex.HasValue ? run.SpeedIndex.Value.ToString(CultureInfo.InvariantCulture) : "—")} " +
                    $"| `{Short(run.CandidateSystemPromptSha256)}` " +
                    $"| `{Short(run.ToolGuidesSha256)}` " +
                    $"| `{Short(run.KnowledgeBaseHeadSha)}` |");
            }
        }
        else
        {
            sb.AppendLine("Member runs: " + string.Join(", ", result.RunIds));
            sb.AppendLine();
            sb.AppendLine("*The run rows are unavailable — one or more members have been deleted since the analysis was computed. The statistics below are the persisted result and are unaffected.*");
        }
        sb.AppendLine();

        if (comparability != null)
        {
            sb.AppendLine($"**Tier:** {TierLabel(comparability.Tier)}. {comparability.Explanation}");
            sb.AppendLine();
            sb.AppendLine($"**Pooling permitted:** {(comparability.PoolingPermitted ? "yes" : "**no**")}  ");
            sb.AppendLine($"**Comparability key hash:** `{comparability.ComparabilityKeyHash}`  ");
            sb.AppendLine($"**Keys compared:** {comparability.MatchedKeys.Count + comparability.Differences.Count} — {comparability.MatchedKeys.Count} matched, {comparability.Differences.Count} differed");
            sb.AppendLine();

            if (comparability.Differences.Count > 0)
            {
                sb.AppendLine("Keys that differ across the members:");
                sb.AppendLine();
                foreach (var diff in comparability.Differences)
                {
                    sb.AppendLine($"- **{diff.Name}** ({diff.Kind}) — {diff.Describe()}");
                }
                sb.AppendLine();
            }

            if (comparability.Tier == BenchmarkComparabilityTier.CrossCondition)
            {
                sb.AppendLine("> **Cross-condition set — the aggregates below are not a replicate measurement.** One instrument key was deliberately moved across these runs. A pooled index over them would average a before and an after into a number describing neither, so pooling is refused; use the paired comparison against the other condition's group instead.");
                sb.AppendLine();
            }
        }
        else if (group.Tier != BenchmarkRunGroupTier.Replicate)
        {
            sb.AppendLine($"**Tier:** {TierLabel((BenchmarkComparabilityTier)group.Tier)} (recorded at group creation; the members were not re-resolved for this report).");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static string Short(string? sha)
        => string.IsNullOrWhiteSpace(sha) ? "—" : (sha.Length <= 8 ? sha : sha[..8]);

    private static void AppendIndex(StringBuilder sb, BenchmarkGroupStatisticsResult result)
    {
        var ix = result.Index;

        sb.AppendLine("## 2. Multi-Run Intelligence Index");
        sb.AppendLine();
        sb.AppendLine($"- **Point estimate:** {Inv(ix.PointEstimate, "F2")} / 100 — the mean of the {ix.RunCount} per-run difficulty-weighted indices.");
        sb.AppendLine($"- **Difficulty-weighted mean of per-item cross-run means:** {Inv(ix.WeightedMeanOfItemMeans, "F2")}");
        sb.AppendLine(ix.IdentityHolds
            ? "  - The two routes agree, as they must for a fixed item set. That identity is what makes the two variance components below coherent."
            : "  - **The two routes disagree**, which happens only when the item set is ragged: some member failed to produce a scored answer to some item, and the two routes weight the missing cells differently. The point estimate above is the mean of the per-run indices.");
        if (ix.MeanStoredQualityIndex.HasValue)
        {
            sb.AppendLine($"- **Mean of the stored per-run `QualityIndex` values:** {Inv(ix.MeanStoredQualityIndex, "F2")} — a cross-check only; it differs by integer rounding and per-run difficulty weights, and nothing here is computed from it.");
        }
        sb.AppendLine($"- **Per-run indices:** {string.Join(", ", ix.PerRunIndices.Select(v => Inv(v, "F1")))}");
        sb.AppendLine();

        sb.AppendLine("### 2.1 The two uncertainty components");
        sb.AppendLine();
        sb.AppendLine("They are reported separately before they are combined because they answer different questions and behave differently as runs are added. Only one of them can be bought down with more runs.");
        sb.AppendLine();
        sb.AppendLine("| Component | Question it answers | SD | SE | 95 % half-width | Shrinks with *R*? |");
        sb.AppendLine("|---|---|---:|---:|---:|---|");
        sb.AppendLine(
            "| **Reproducibility** | *Would a re-run move this?* " +
            $"| {Inv(ix.ReproducibilityStandardDeviation)} " +
            $"| {Inv(ix.ReproducibilityStandardError)} " +
            $"| {Inv(ix.ReproducibilityHalfWidth)} " +
            "| **Yes**, as 1/√*R* |");
        sb.AppendLine(
            "| **Item sampling** | *Would a different set of questions move this?* " +
            "| — " +
            $"| {Inv(ix.ItemSamplingStandardError)} " +
            $"| {Inv(ix.ItemSamplingHalfWidth)} " +
            "| **No** |");
        sb.AppendLine();

        if (!ix.ReproducibilityAvailable)
        {
            sb.AppendLine($"> **No reproducibility figure is reported at *R* = {ix.RunCount}.** Below {BenchmarkGroupStatistics.MinRunsForReproducibility} runs a run-to-run standard deviation is not a measurement, and reporting one would invite a reader to act on noise. This mirrors the `n < 3 → null` convention already used by `BenchmarkScoring.QualityIndexStandardError` and `BenchmarkItemAnalysis.MinRunsForMeasurement`. The interval below therefore covers **one** source, not two.");
            sb.AppendLine();
        }

        sb.AppendLine($"- **Combined 95 % interval:** {Inv(ix.PointEstimate, "F2")} ± {Inv(ix.CombinedHalfWidth)}" +
                      (ix.CombinedLower.HasValue && ix.CombinedUpper.HasValue
                          ? $"  →  [{Inv(ix.CombinedLower)}, {Inv(ix.CombinedUpper)}]"
                          : string.Empty));
        sb.AppendLine(ix.ReproducibilityAvailable
            ? "  - Computed as √((*t*·SE_repro)² + (1.96·SE_item)²) — the two independent sources added in quadrature. It covers **both** run-to-run variation and item selection."
            : "  - This is the item-sampling half-width alone. It covers item selection only.");
        sb.AppendLine();
        sb.AppendLine("> **The item-sampling component does not shrink as more runs are added, and that is correct.** Every run answers the same items, so extra runs sharpen the estimate of each item's mean and do nothing whatever about the fact that the suite drew those particular questions. A reader who expects the whole interval to fall as √*R* will conclude the code is broken; it is not.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void AppendItems(StringBuilder sb, BenchmarkGroupStatisticsResult result)
    {
        sb.AppendLine("## 3. Per-Item Statistics");
        sb.AppendLine();
        sb.AppendLine("Quality score per item across the group's runs. *CV* is SD / mean; *CE rate* is the share of runs in which the assessor flagged a critical error on this item.");
        sb.AppendLine();
        sb.AppendLine("| Q | Runs | Mean | Median | SD | Min | Max | IQR | CV | 95 % CI | CE rate | Stability | Question |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---|---|");

        foreach (var item in result.Items.OrderBy(i => i.OrderIndex))
        {
            string ci = item.MeanConfidenceLower.HasValue && item.MeanConfidenceUpper.HasValue
                ? $"[{Inv(item.MeanConfidenceLower, "F1")}, {Inv(item.MeanConfidenceUpper, "F1")}]"
                : "—";

            string stability = item.InsufficientRuns
                ? "n/a"
                : item.Unstable ? "**unstable**" : "stable";

            sb.AppendLine(
                $"| {item.OrderIndex} " +
                $"| {item.RunCount} " +
                $"| {Inv(item.Mean, "F1")} " +
                $"| {Inv(item.Median, "F1")} " +
                $"| {Inv(item.StandardDeviation, "F1")} " +
                $"| {item.Min} " +
                $"| {item.Max} " +
                $"| {Inv(item.InterquartileRange, "F1")} " +
                $"| {(item.CoefficientOfVariation.HasValue ? Inv(item.CoefficientOfVariation.Value * 100.0, "F1") + " %" : "—")} " +
                $"| {ci} " +
                $"| {Inv(item.CriticalErrorRate * 100.0, "F0")} % " +
                $"| {stability} " +
                $"| {Excerpt(item.QuestionText)} |");
        }
        sb.AppendLine();

        var unstable = result.Items.Where(i => i.Unstable).OrderBy(i => i.OrderIndex).ToList();
        if (unstable.Count > 0)
        {
            sb.AppendLine($"**Unstable items ({unstable.Count}):** {string.Join(", ", unstable.Select(i => "Q" + i.OrderIndex))}. These are the items where a single run's verdict is least trustworthy — which is a suite-health signal as much as a model one: an item whose rubric cannot decide a borderline answer will swing between runs no matter which model answers it.");
            sb.AppendLine();
        }

        var borderlineCriticals = result.Items
            .Where(i => i.CriticalErrorRate > 0.0 && i.CriticalErrorRate < 1.0)
            .OrderBy(i => i.OrderIndex)
            .ToList();
        if (borderlineCriticals.Count > 0)
        {
            sb.AppendLine($"**Items whose critical-error verdict is not stable across runs:** {string.Join(", ", borderlineCriticals.Select(i => $"Q{i.OrderIndex} ({i.CriticalErrorCount}/{i.RunCount})"))}. A rate strictly between 0 and 1 means the same question sometimes does and sometimes does not trip the score ceiling — either a genuinely borderline answer or a rubric that does not decide the case.");
            sb.AppendLine();
        }

        if (result.Items.Any(i => i.InsufficientRuns))
        {
            sb.AppendLine($"*Items marked `n/a` for stability were answered by fewer than {BenchmarkGroupStatistics.MinRunsForReproducibility} runs; their spread figures are shown but are not measurements.*");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void AppendSpeed(StringBuilder sb, BenchmarkGroupStatisticsResult result)
    {
        var sp = result.Speed;

        sb.AppendLine("## 4. Speed");
        sb.AppendLine();

        if (sp.Degraded)
        {
            sb.AppendLine($"> **Degraded — these figures mix timing conditions.** {sp.DegradedReason}");
            sb.AppendLine();
        }

        sb.AppendLine($"- **Mean Speed Index:** {Inv(sp.MeanSpeedIndex, "F1")}" +
                      (sp.SpeedIndexStandardDeviation.HasValue ? $" ± {Inv(sp.SpeedIndexStandardDeviation, "F1")} (SD across {sp.RunCount} runs)" : string.Empty));
        sb.AppendLine($"- **Per-run Speed Indices:** {string.Join(", ", sp.PerRunSpeedIndices.Select(v => Inv(v, "F0")))}");
        sb.AppendLine($"- **Pooled per-answer model time** over {sp.PooledAnswerCount} answers — P50 {Seconds(sp.ModelTimeP50Ms)}, P90 {Seconds(sp.ModelTimeP90Ms)}, max {Seconds(sp.ModelTimeMaxMs)}");
        sb.AppendLine("  - Pooled across all *R* × *Q* cells rather than averaged per run: the question is what a single slow turn looks like, and a mean of per-run medians cannot answer it.");
        sb.AppendLine();
        sb.AppendLine($"> {sp.Caveat}");
        sb.AppendLine();

        var slowest = result.Items
            .Where(i => i.MedianModelTimeMs.HasValue)
            .OrderByDescending(i => i.MedianModelTimeMs!.Value)
            .Take(5)
            .ToList();
        if (slowest.Count > 0)
        {
            sb.AppendLine("**Slowest items by median model time:** " +
                          string.Join(", ", slowest.Select(i => $"Q{i.OrderIndex} ({Seconds(i.MedianModelTimeMs)})")));
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void AppendCost(StringBuilder sb, BenchmarkGroupStatisticsResult result)
    {
        sb.AppendLine("## 5. Cost");
        sb.AppendLine();

        if (result.Cost == null)
        {
            sb.AppendLine("*No pricing was resolvable for the member runs, so no cost aggregate is reported. An absent figure is reported as absent rather than as zero.*");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            return;
        }

        var cost = result.Cost;

        if (cost.Degraded)
        {
            sb.AppendLine($"> **Degraded — these figures mix pricing conditions.** {cost.DegradedReason} A pricing mismatch degrades **cost only**; it cannot move a quality score.");
            sb.AppendLine();
        }

        sb.AppendLine($"- **Total across {cost.RunCount} runs:** {Money(cost.TotalCost)}");
        sb.AppendLine($"- **Mean per run:** {Money(cost.MeanCostPerRun)}" +
                      (cost.CostStandardDeviation.HasValue ? $" ± {Money(cost.CostStandardDeviation)} (SD)" : string.Empty));
        sb.AppendLine($"- **Cost per question:** {Money(cost.CostPerQuestion)}");
        sb.AppendLine($"- **Cost per index point:** {Money(cost.CostPerIndexPoint)}");
        sb.AppendLine();

        if (cost.TotalCostByRole.Count > 0)
        {
            sb.AppendLine("| Role | Total | Mean per run | Share |");
            sb.AppendLine("|---|---:|---:|---:|");
            foreach (var kv in cost.TotalCostByRole.OrderByDescending(k => k.Value))
            {
                double share = cost.TotalCost > 0 ? kv.Value / cost.TotalCost * 100.0 : 0.0;
                cost.MeanCostByRole.TryGetValue(kv.Key, out double mean);
                sb.AppendLine($"| {kv.Key} | {Money(kv.Value)} | {Money(mean)} | {Inv(share, "F0")} % |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void AppendComparison(
        StringBuilder sb,
        BenchmarkGroupComparison cmp,
        string treatmentName,
        string? baselineName)
    {
        sb.AppendLine("## 6. Paired Group Comparison");
        sb.AppendLine();
        sb.AppendLine($"**Baseline:** {baselineName ?? "(unnamed group)"} — runs {string.Join(", ", cmp.BaselineRunIds)}  ");
        sb.AppendLine($"**Treatment:** {treatmentName} — runs {string.Join(", ", cmp.TreatmentRunIds)}");
        sb.AppendLine();
        sb.AppendLine($"Paired by question on per-item cross-run mean quality. Differences are **treatment minus baseline**, so a positive value means the treatment scored higher. {cmp.PairedItemCount} item(s) paired" +
                      (cmp.UnpairedItemCount > 0 ? $"; {cmp.UnpairedItemCount} present on one side only and excluded — a pair needs two halves, and imputing one would invent the finding." : "."));
        sb.AppendLine();

        sb.AppendLine($"- **Mean paired difference:** {Inv(cmp.MeanDifference, "F2")} points" +
                      (cmp.DifferenceConfidenceLower.HasValue && cmp.DifferenceConfidenceUpper.HasValue
                          ? $" (95 % CI [{Inv(cmp.DifferenceConfidenceLower, "F2")}, {Inv(cmp.DifferenceConfidenceUpper, "F2")}])"
                          : string.Empty));
        sb.AppendLine($"- **SD of the differences:** {Inv(cmp.DifferenceStandardDeviation, "F2")}");
        sb.AppendLine($"- **Cohen's *d*z (effect size):** {Inv(cmp.CohensDz, "F3")} — the paired effect size: mean difference over the SD of those differences, not a pooled between-group *d*, which would describe a comparison nobody made here.");
        sb.AppendLine();

        sb.AppendLine("### 6.1 Tests");
        sb.AppendLine();
        sb.AppendLine($"**Wilcoxon signed-rank (primary).** *n* = {cmp.Wilcoxon.SampleSize}" +
                      (cmp.Wilcoxon.ZeroDifferenceCount > 0 ? $" after discarding {cmp.Wilcoxon.ZeroDifferenceCount} zero difference(s)" : string.Empty) +
                      $"; W+ = {Inv(cmp.Wilcoxon.PositiveRankSum, "F1")}, W− = {Inv(cmp.Wilcoxon.NegativeRankSum, "F1")}, W = {Inv(cmp.Wilcoxon.Statistic, "F1")}; " +
                      $"**p = {PValue(cmp.Wilcoxon.PValue)}** ({cmp.Wilcoxon.Method}" +
                      (cmp.Wilcoxon.TiesPresent ? ", tie-corrected ranks" : string.Empty) + ").");
        sb.AppendLine();
        sb.AppendLine("Non-parametric, which is what a bounded 0–100 scale over this many items warrants.");
        sb.AppendLine();
        sb.AppendLine($"**Paired *t* (secondary).** *n* = {cmp.PairedT.SampleSize}, mean difference {Inv(cmp.PairedT.MeanDifference, "F2")}, SE {Inv(cmp.PairedT.StandardError, "F3")}, *t*({Inv(cmp.PairedT.DegreesOfFreedom, "F0")}) = {Inv(cmp.PairedT.TStatistic, "F3")}, p = {PValue(cmp.PairedT.PValue)}. Reported beside Wilcoxon and never instead of it: it assumes a normality nobody has checked.");
        sb.AppendLine();

        if (cmp.ItemComparisons.Count > 0)
        {
            sb.AppendLine("### 6.2 Per-item differences — **exploratory**");
            sb.AppendLine();
            sb.AppendLine($"> {cmp.ExploratoryNote}");
            sb.AppendLine();
            sb.AppendLine($"Benjamini–Hochberg step-up control at a false discovery rate of {Inv(cmp.FalseDiscoveryRate, "F2")}. The adjusted column is a q-value, not a p-value.");
            sb.AppendLine();
            sb.AppendLine("| Q | Baseline mean | Treatment mean | Δ | *t* | df | p | q (BH) | Rejected | Question |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---|---|");
            foreach (var item in cmp.ItemComparisons.OrderBy(i => i.OrderIndex))
            {
                sb.AppendLine(
                    $"| {item.OrderIndex} " +
                    $"| {Inv(item.BaselineMean, "F1")} " +
                    $"| {Inv(item.TreatmentMean, "F1")} " +
                    $"| {(item.Difference >= 0 ? "+" : string.Empty)}{Inv(item.Difference, "F1")} " +
                    $"| {Inv(item.TStatistic, "F2")} " +
                    $"| {Inv(item.DegreesOfFreedom, "F1")} " +
                    $"| {PValue(item.PValue)} " +
                    $"| {PValue(item.AdjustedPValue)} " +
                    $"| {(item.RejectedAtFdr ? "yes" : "no")} " +
                    $"| {Excerpt(item.QuestionText, 60)} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void AppendLimits(
        StringBuilder sb,
        BenchmarkGroupStatisticsResult result,
        BenchmarkGroupComparison? comparison)
    {
        sb.AppendLine("## " + (comparison != null ? "7" : "6") + ". What This Analysis Cannot Decompose");
        sb.AppendLine();
        sb.AppendLine($"> {result.VarianceDecompositionCaveat}");
        sb.AppendLine();
        sb.AppendLine("Concretely: an item flagged unstable above may be unstable because the *candidate* answers it differently each time, or because the *grader* scores the same quality of answer differently each time. Nothing in a replicate set separates the two, because each run produces a new answer that is graded once. Separating them requires re-grading identical answers — `SecondOpinionMode = All`, or a re-assessment pass over stored answers — and the run's sampled assessor agreement is the partial measurement available today.");
        sb.AppendLine();
        sb.AppendLine("*Report generated by `BenchmarkGroupReportBuilder`. No part of it was written by a model.*");
    }
}
