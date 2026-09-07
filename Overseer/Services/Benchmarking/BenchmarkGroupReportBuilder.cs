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

    /// <summary>
    /// Token counts, abbreviated. A benchmark set runs to millions of input tokens, and eight raw
    /// digits in a bullet list is a number nobody reads.
    /// </summary>
    private static string Tokens(long value)
    {
        if (value >= 1_000_000) return Inv(value / 1_000_000.0, "F2") + " M";
        if (value >= 1_000) return Inv(value / 1_000.0, "F1") + " k";
        return value.ToString(CultureInfo.InvariantCulture);
    }

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

        // Sections are numbered by a running counter rather than by literals. Two of them are
        // conditional, and hardcoded numbers had already produced one arithmetic expression in a
        // heading string.
        int section = 0;

        AppendHeader(sb, group, result, comparability, overseerVersion, computedAtUtc);
        AppendManifest(sb, ++section, group, result, comparability, members);
        AppendIndex(sb, ++section, result);
        AppendDimensions(sb, ++section, result);
        AppendItems(sb, ++section, result);
        AppendSpeed(sb, ++section, result);
        AppendCost(sb, ++section, result);

        if (result.Usage != null)
        {
            AppendUsage(sb, ++section, result);
        }

        if (comparison != null)
        {
            AppendComparison(sb, ++section, comparison, group.Name, comparisonGroupName);
        }

        AppendLimits(sb, ++section, result);

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
        int section,
        BenchmarkRunGroup group,
        BenchmarkGroupStatisticsResult result,
        BenchmarkComparabilityResult? comparability,
        IReadOnlyList<BenchmarkRun>? members)
    {
        sb.AppendLine($"## {section}. Group Manifest");
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

        AppendPromptUnderTest(sb, section, result);

        sb.AppendLine("---");
        sb.AppendLine();
    }

    /// <summary>
    /// What the candidate was told to do.
    ///
    /// <para>Without this a reader cannot perform the attribution check every dimensional finding
    /// depends on: a concise response instruction caps Completeness by design, so a low Completeness
    /// under one is prompt adherence rather than a model weakness. The prompt SHA in the table above
    /// proves the members agree; it does not say what they agreed on.</para>
    /// </summary>
    private static void AppendPromptUnderTest(
        StringBuilder sb, int section, BenchmarkGroupStatisticsResult result)
    {
        var prompt = result.PromptUnderTest;
        if (prompt == null) return;

        sb.AppendLine($"### {section}.1 Chat Prompt Under Test");
        sb.AppendLine();
        sb.AppendLine("The candidates answered under the **production Overseer chat system prompt** (`ChatService.BuildSystemPrompt`), not a benchmark-specific prompt. Every quality verdict below is a verdict on the prompt real users receive.");
        sb.AppendLine();

        if (!prompt.Recorded)
        {
            sb.AppendLine("- *The prompt configuration was not recorded for these runs.* No dimensional result below can be attributed to the model until it is: the options that cap a dimension are unknown for this group.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"- **Mode:** {(prompt.OverseerMode == 0 ? "Gameplay Help" : $"Mode {prompt.OverseerMode}")} · " +
                      $"**Response style:** {(prompt.VerboseMode ? "detailed (`verboseMode: true`)" : "concise (`verboseMode: false`)")}");
        sb.AppendLine($"- **Tools:** {OnOff(prompt.EnableToolUse, "enabled", "disabled")} · " +
                      $"**Web search:** {OnOff(prompt.EnableWebSearch, "enabled", "disabled")} · " +
                      $"**Subagents:** {OnOff(prompt.EnableSubAgents, "enabled", "disabled")} · " +
                      $"**Source code references:** {OnOff(prompt.AllowSourceCodeReferences, "allowed", "disallowed")}");
        sb.AppendLine($"- **Spoiler-free mode:** {OnOff(prompt.SpoilerFreeMode, "on", "off")} · " +
                      $"**Active game:** {OnOff(prompt.IsGameOn, "yes", "no")} · " +
                      $"**Developer mode:** {OnOff(prompt.DeveloperMode, "on", "off")} · " +
                      $"**Message history:** {OnOff(prompt.HasMessageHistory, "yes", "no")}");
        sb.AppendLine($"- **Pre-injected wiki context:** {OnOff(prompt.HasWikiContext, "yes", "no")} · " +
                      $"**Game snapshot:** {OnOff(prompt.HasGameSnapshot, "yes", "no")}");
        sb.AppendLine($"- **Tool batching policy:** {prompt.ParallelMode} — {ParallelPolicyDescription(prompt.ParallelMode)}");
        sb.AppendLine();

        sb.AppendLine(prompt.Divergent
            ? "> **The members disagree on these options.** The values above are the first member's. A group whose members were graded under different prompts cannot support a pooled dimensional claim, whatever its tier says."
            : "> The group's comparability key covers the prompt options, and the members were checked against each other: every one of them was graded under exactly this configuration.");
        sb.AppendLine();

        if (!prompt.HasWikiContext)
        {
            sb.AppendLine("> **Live chat pre-injects wiki articles and the benchmark does not.** Every retrieval and tool-routing figure in this report was therefore measured under a condition production does not share. Quality, speed and cost figures are unaffected as measurements; a *routing* conclusion drawn from them is evidence about the benchmark configuration until a run with wiki context says otherwise.");
            sb.AppendLine();
        }
    }

    private static string OnOff(bool value, string whenTrue, string whenFalse)
        => value ? whenTrue : whenFalse;

    /// <summary>
    /// What <see cref="Overseer.Services.Tools.ToolRegistry.GetParallelOverrideText"/> actually
    /// selects for a given mode, in report prose. <c>Enabled</c> loads no override file; the
    /// batching guidance in <c>Overseer/ToolGuides/_policy.md</c> still applies unchanged.
    /// </summary>
    private static string ParallelPolicyDescription(MobileGnollHackLogger.Data.ParallelExecutionMode mode) => mode switch
    {
        MobileGnollHackLogger.Data.ParallelExecutionMode.Disabled =>
            "selects `Overseer/ToolGuides/_policy_parallel_disabled.md`, so this is part of the prompt text.",
        MobileGnollHackLogger.Data.ParallelExecutionMode.OnRequest =>
            "selects `Overseer/ToolGuides/_policy_parallel_on_request.md`, so this is part of the prompt text.",
        _ =>
            "selects no override file; the batching guidance in `Overseer/ToolGuides/_policy.md` applies unchanged."
    };

    private static string Short(string? sha)
        => string.IsNullOrWhiteSpace(sha) ? "—" : (sha.Length <= 8 ? sha : sha[..8]);

    private static void AppendIndex(StringBuilder sb, int section, BenchmarkGroupStatisticsResult result)
    {
        var ix = result.Index;

        sb.AppendLine($"## {section}. Multi-Run Intelligence Index");
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

        sb.AppendLine($"### {section}.1 The two uncertainty components");
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

        // The SD's own interval. Without it a reader compares one group's 0.30 against another's
        // 3.34 as though both were measurements and concludes the instrument became ten times less
        // reproducible, when the two intervals overlap and nothing measurable changed.
        if (ix.ReproducibilityStandardDeviation.HasValue
            && ix.ReproducibilitySdLower.HasValue
            && ix.ReproducibilitySdUpper.HasValue)
        {
            double sd = ix.ReproducibilityStandardDeviation.Value;
            string factor = sd > 0.0
                ? Inv(ix.ReproducibilitySdUpper.Value / sd, "F1")
                : "—";

            sb.AppendLine($"**The reproducibility SD is itself an estimate, and it carries its own interval:** SD {Inv(sd)}, 95 % interval on σ [{Inv(ix.ReproducibilitySdLower)}, {Inv(ix.ReproducibilitySdUpper)}], from the χ²({ix.RunCount - 1}) distribution.");
            sb.AppendLine();
            sb.AppendLine($"At *R* = {ix.RunCount} the upper bound is {factor} × the point estimate, so **two groups' SDs are not comparable point estimates.** A set reporting an SD ten times another's can have an interval that overlaps it completely: the instrument did not become ten times less reproducible, and a reader comparing the two bare numbers across reports will conclude that it did. Compare the intervals, or add runs until the SD is worth comparing.");
            sb.AppendLine();
        }

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
        if (ix.CombinedIntervalTruncated)
        {
            sb.AppendLine("  - **Truncated at the score bound.** The half-width above is the honest one; the interval reaches past 0 or 100, where no score can go, so the reported bound is pinned there. The interval is not as tight as it looks.");
        }

        sb.AppendLine();
        sb.AppendLine("> **The item-sampling component does not shrink as more runs are added, and that is correct.** Every run answers the same items, so extra runs sharpen the estimate of each item's mean and do nothing whatever about the fact that the suite drew those particular questions. A reader who expects the whole interval to fall as √*R* will conclude the code is broken; it is not.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    /// <summary>
    /// The four dimensions across the runs.
    ///
    /// <para>A composite index cannot say which dimension moved, and a single run cannot say whether
    /// a dimensional gap survives a re-run. This section is where a replicate set answers both.</para>
    /// </summary>
    private static void AppendDimensions(StringBuilder sb, int section, BenchmarkGroupStatisticsResult result)
    {
        sb.AppendLine($"## {section}. Quality Dimensions");
        sb.AppendLine();

        var scored = result.Dimensions.Where(d => d.PerRunMeans.Count > 0).ToList();
        if (scored.Count == 0)
        {
            sb.AppendLine("*No member recorded per-dimension scores, so no dimensional aggregate is reported. An absent figure is reported as absent rather than as zero.*");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("Each dimension's per-run mean, pooled across the group. These means are **unweighted**, while the Intelligence Index is difficulty-weighted — there are no per-dimension difficulty weights to apply, so the two are not expected to agree.");
        sb.AppendLine();
        sb.AppendLine("| Dimension | Mean | SD | 95 % half-width | Min | Max | Per-run means | Lowest items |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---|---|");

        foreach (var dim in scored)
        {
            // The three weakest items on this dimension, named by the Q numbers the rest of the
            // report uses rather than by question id.
            var lowest = dim.ItemMeans
                .OrderBy(kv => kv.Value)
                .Take(3)
                .Select(kv =>
                {
                    var item = result.Items.FirstOrDefault(i => i.QuestionId == kv.Key);
                    string label = item != null ? $"Q{item.OrderIndex}" : $"id {kv.Key}";
                    return $"{label} ({Inv(kv.Value, "F1")})";
                })
                .ToList();

            sb.AppendLine(
                $"| **{dim.Dimension}** " +
                $"| {Inv(dim.Mean, "F1")} " +
                $"| {Inv(dim.StandardDeviation, "F1")} " +
                $"| {Inv(dim.ConfidenceHalfWidth, "F1")} " +
                $"| {Inv(dim.Min, "F1")} " +
                $"| {Inv(dim.Max, "F1")} " +
                $"| {string.Join(", ", dim.PerRunMeans.Select(v => Inv(v, "F1")))} " +
                $"| {(lowest.Count > 0 ? string.Join(", ", lowest) : "—")} |");
        }

        sb.AppendLine();

        var weakest = scored.OrderBy(d => d.Mean ?? double.MaxValue).First();
        var strongest = scored.OrderByDescending(d => d.Mean ?? double.MinValue).First();
        if (weakest.Dimension != strongest.Dimension && weakest.Mean.HasValue && strongest.Mean.HasValue)
        {
            double gap = strongest.Mean.Value - weakest.Mean.Value;
            sb.AppendLine($"**Lowest dimension:** {weakest.Dimension} at {Inv(weakest.Mean, "F1")}, trailing {strongest.Dimension} by {Inv(gap, "F1")} points.");
            sb.AppendLine();
            sb.AppendLine("> A dimension can be low because the prompt instructed the model to answer that way. Check the response style in the manifest before reading a low figure here as a model weakness.");
            sb.AppendLine();
        }

        if (!result.PooledIndexReportable)
        {
            sb.AppendLine("> Below three runs no half-width is reported for a dimension, on the same rule the index uses: a standard deviation over two runs is arithmetic rather than evidence.");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void AppendItems(StringBuilder sb, int section, BenchmarkGroupStatisticsResult result)
    {
        sb.AppendLine($"## {section}. Per-Item Statistics");
        sb.AppendLine();
        sb.AppendLine("Quality score per item across the group's runs. *CV* is SD / mean; *CE rate* is the share of runs in which the assessor flagged a critical error on this item. An interval marked † was truncated at the score bound.");
        sb.AppendLine();
        sb.AppendLine("| Q | Runs | Per-run scores | Mean | Median | SD | Min | Max | IQR | CV | 95 % CI | CE rate | Stability | Question |");
        sb.AppendLine("|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---|---:|---|---|");

        foreach (var item in result.Items.OrderBy(i => i.OrderIndex))
        {
            string ci = item.MeanConfidenceLower.HasValue && item.MeanConfidenceUpper.HasValue
                ? $"[{Inv(item.MeanConfidenceLower, "F1")}, {Inv(item.MeanConfidenceUpper, "F1")}]"
                  + (item.MeanConfidenceTruncated ? " †" : string.Empty)
                : "—";

            // Scores and run ids are positionally aligned. Rendered only when they agree in length:
            // a mis-paired score column would name the wrong run as the one that collapsed, which is
            // worse than naming none.
            string perRunScores = item.Scores.Count > 0 && item.Scores.Count == item.RunIds.Count
                ? string.Join(" / ", item.Scores.Select(s => Inv(s, "F0")))
                : "—";

            // An unstable item whose critical-error verdict moved across runs is a ceiling problem,
            // not a dimensional one, and the two demand opposite responses.
            bool ceilingDriven = item.Unstable
                && item.CriticalErrorRate > 0.0 && item.CriticalErrorRate < 1.0;

            string stability = item.InsufficientRuns
                ? "n/a"
                : item.Unstable
                    ? (ceilingDriven ? "**unstable — ceiling**" : "**unstable**")
                    : "stable";

            sb.AppendLine(
                $"| {item.OrderIndex} " +
                $"| {item.RunCount} " +
                $"| {perRunScores} " +
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

        // The order is stated once, under the table. Without it the score vector cannot be read
        // back to a run, which is the whole point of carrying it.
        if (result.RunIds.Count > 0)
        {
            sb.AppendLine($"*Per-run scores are in run-id order: {string.Join(", ", result.RunIds)}. An item answered by fewer runs than the group holds carries only the runs that answered it, in that same order; a `—` means the two vectors disagreed in length and no pairing could be trusted.*");
            sb.AppendLine();
        }

        var unstable = result.Items.Where(i => i.Unstable).OrderBy(i => i.OrderIndex).ToList();
        if (unstable.Count > 0)
        {
            sb.AppendLine($"**Unstable items ({unstable.Count}):** {string.Join(", ", unstable.Select(i => "Q" + i.OrderIndex))}. These are the items where a single run's verdict is least trustworthy — which is a suite-health signal as much as a model one: an item whose rubric cannot decide a borderline answer will swing between runs no matter which model answers it.");
            sb.AppendLine();

            // The two kinds of instability the table cannot tell apart on its own.
            var ceilingUnstable = unstable
                .Where(i => i.CriticalErrorRate > 0.0 && i.CriticalErrorRate < 1.0)
                .ToList();
            var dimensionalUnstable = unstable
                .Where(i => i.CriticalErrorRate <= 0.0)
                .ToList();

            if (ceilingUnstable.Count > 0)
            {
                sb.AppendLine($"**Unstable because the critical-error ceiling tripped in some runs and not others:** {string.Join(", ", ceilingUnstable.Select(i => $"Q{i.OrderIndex} ({i.CriticalErrorCount}/{i.RunCount} runs)"))}. On these the SD is the ceiling moving, not a score earned differently on the dimensions — expect the item's Accuracy mean to sit well above its total mean, and read the gap as the ceiling rather than as the prose.");
                sb.AppendLine();

                if (dimensionalUnstable.Count > 0)
                {
                    sb.AppendLine($"**Unstable with the critical-error rate at 0, so earned on the dimensions:** {string.Join(", ", dimensionalUnstable.Select(i => "Q" + i.OrderIndex))}. A low run here scored low on the rubric itself, with no ceiling involved. The two groups of items look identical in the table above and are entirely different problems: one is a rubric that cannot decide a borderline case, the other an answer that genuinely varies.");
                    sb.AppendLine();
                }
            }
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

        if (result.Items.Any(i => i.MeanConfidenceTruncated))
        {
            sb.AppendLine("*† The interval reached past 0 or 100, where no score can go, so the reported bound is pinned there. The SD column is the unclamped spread and is the figure to read.*");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void AppendSpeed(StringBuilder sb, int section, BenchmarkGroupStatisticsResult result)
    {
        var sp = result.Speed;

        sb.AppendLine($"## {section}. Speed");
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

        // Time to first token is the latency a chat user perceives, and it carries its own
        // denominator because the per-answer figure is nullable where model time is not.
        if (sp.TtftAnswerCount > 0 && sp.TtftP50Ms.HasValue)
        {
            sb.AppendLine($"- **Pooled time to first token** over {sp.TtftAnswerCount} answers — P50 {Seconds(sp.TtftP50Ms)}, P90 {Seconds(sp.TtftP90Ms)}, max {Seconds(sp.TtftMaxMs)}");
            sb.AppendLine("  - This is the latency a chat user actually waits through, and a thinking-heavy configuration dominates the model time above without touching it. A production speed claim rests on this line rather than on the one before it.");
        }
        else
        {
            sb.AppendLine("- **Pooled time to first token:** — *no member answer recorded one, so no figure is reported rather than a zero. Time to first token is the latency a chat user perceives, so a set without it cannot support a production speed claim.*");
        }

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

    private static void AppendCost(StringBuilder sb, int section, BenchmarkGroupStatisticsResult result)
    {
        sb.AppendLine($"## {section}. Cost");
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
        if (cost.PerRunTotals.Count > 0)
        {
            sb.AppendLine($"- **Per-run totals:** {string.Join(", ", cost.PerRunTotals.Select(t => Money(t)))}");
        }

        sb.AppendLine();

        if (cost.TotalCostByRole.Count > 0)
        {
            sb.AppendLine("| Role | Total | Mean per run | SD | Min | Max | Share |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (var kv in cost.TotalCostByRole.OrderByDescending(k => k.Value))
            {
                double share = cost.TotalCost > 0 ? kv.Value / cost.TotalCost * 100.0 : 0.0;
                cost.MeanCostByRole.TryGetValue(kv.Key, out double mean);
                cost.CostStandardDeviationByRole.TryGetValue(kv.Key, out double? sd);
                cost.MinCostByRole.TryGetValue(kv.Key, out double min);
                cost.MaxCostByRole.TryGetValue(kv.Key, out double max);

                sb.AppendLine($"| {kv.Key} | {Money(kv.Value)} | {Money(mean)} | {Money(sd)} " +
                              $"| {Money(min)} | {Money(max)} | {Inv(share, "F0")} % |");
            }

            sb.AppendLine();

            // Which role carries the spread is the actionable half. A replicate set whose quality
            // reproduces to a tenth of a point can still spend twice as much on one member as
            // another, and a mean with one SD on the total does not say where that came from.
            var widest = cost.CostStandardDeviationByRole
                .Where(kv => kv.Value.HasValue)
                .OrderByDescending(kv => kv.Value!.Value)
                .FirstOrDefault();
            if (widest.Key != null && widest.Value.HasValue && widest.Value.Value > 0.0)
            {
                sb.AppendLine($"**Cost dispersion sits mostly in `{widest.Key}`** (SD {Money(widest.Value)} across {cost.RunCount} runs). Cost is the least reproducible quantity a replicate set measures: an identical configuration can spend materially differently from run to run, so a cost difference between two groups needs the same spread treatment a quality difference gets.");
                sb.AppendLine();
            }
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    /// <summary>
    /// Tokens, tool routing and what the claim verifier's spend bought.
    ///
    /// <para>Rendered only when a member recorded any of it. The verifier can be most of a set's
    /// cost, and a cost table that names it without naming how many claims it checked leaves the
    /// reader unable to judge whether the spend was worth it.</para>
    /// </summary>
    private static void AppendUsage(StringBuilder sb, int section, BenchmarkGroupStatisticsResult result)
    {
        var usage = result.Usage;
        if (usage == null) return;

        sb.AppendLine($"## {section}. Token and Tool Usage");
        sb.AppendLine();

        sb.AppendLine($"- **Candidate tokens:** {Tokens(usage.TotalInputTokens)} in / {Tokens(usage.TotalOutputTokens)} out" +
                      (usage.InputOutputRatio.HasValue ? $" — a ratio of {Inv(usage.InputOutputRatio, "F1")} : 1" : string.Empty));
        if (usage.CacheReadSharePercentage.HasValue)
        {
            sb.AppendLine($"- **Prompt cache reads:** {Tokens(usage.TotalCacheReadTokens)} — {Inv(usage.CacheReadSharePercentage, "F1")} % of input. The segmented system prompt is what makes that share possible, and anything that changes its frozen segment invalidates it.");
        }

        if (usage.PerRunInputTokens.Count > 0)
        {
            sb.AppendLine($"- **Per-run candidate input:** {string.Join(", ", usage.PerRunInputTokens.Select(Tokens))}" +
                          (usage.InputTokenStandardDeviation.HasValue ? $" (SD {Tokens((long)Math.Round(usage.InputTokenStandardDeviation.Value))})" : string.Empty));
        }

        sb.AppendLine($"- **Grader tokens, kept separate:** assessor {Tokens(usage.TotalAssessmentInputTokens)} in / {Tokens(usage.TotalAssessmentOutputTokens)} out; " +
                      $"claim verifier {Tokens(usage.TotalClaimVerificationInputTokens)} in / {Tokens(usage.TotalClaimVerificationOutputTokens)} out");
        sb.AppendLine("  - Never folded into the candidate's totals above: those measure the model under test, and a grader's consumption is not the candidate's.");
        sb.AppendLine();

        if (usage.TotalToolCalls > 0)
        {
            sb.AppendLine($"**Tool calls:** {usage.TotalToolCalls} across {usage.RunCount} runs" +
                          (usage.MeanToolCallsPerRun.HasValue ? $", {Inv(usage.MeanToolCallsPerRun, "F1")} per run" : string.Empty) +
                          (usage.ToolCallStandardDeviation.HasValue ? $" (SD {Inv(usage.ToolCallStandardDeviation, "F1")})" : string.Empty));
            sb.AppendLine();
            sb.AppendLine("| Tool family | Calls | Share |");
            sb.AppendLine("|---|---:|---:|");
            foreach (var kv in usage.ToolCallsByFamily.OrderByDescending(k => k.Value))
            {
                usage.ToolFamilyShares.TryGetValue(kv.Key, out double share);
                sb.AppendLine($"| {kv.Key} | {kv.Value} | {Inv(share, "F1")} % |");
            }

            sb.AppendLine();
            sb.AppendLine("> A family's share is prompt-compliant or not only against the tool preference hierarchy in `Overseer/ToolGuides/_policy.md`. A high source-code share on a suite weighted toward exact mechanics is what that policy asks for, not a routing defect.");
            sb.AppendLine();
        }

        if (usage.TotalModelCalls.HasValue)
        {
            sb.AppendLine($"**Model calls:** {usage.TotalModelCalls.Value} across {usage.RunCount} runs" +
                          (usage.MeanModelCallsPerRun.HasValue ? $", {Inv(usage.MeanModelCallsPerRun, "F1")} per run" : string.Empty) +
                          (usage.ModelCallStandardDeviation.HasValue ? $" (SD {Inv(usage.ModelCallStandardDeviation, "F1")})" : string.Empty) + ".");
            sb.AppendLine($"- **Per-run model calls:** {string.Join(", ", usage.PerRunModelCalls.Select(c => c.HasValue ? c.Value.ToString(CultureInfo.InvariantCulture) : "—"))}");
            sb.AppendLine();
            sb.AppendLine("> **Input cost tracks model calls, not tool calls.** Every model call resends the whole conversation, so two tools batched into one call pay the input once and the same two tools in two calls pay it twice. A rising tool-call count beside a flat model-call count is cheaper rather than more expensive, and neither figure can be read for cost without the other.");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("**Model calls:** — *no member answer recorded a model-call count, so none is reported rather than a zero. Input cost tracks model calls rather than tool calls, so this set cannot show whether its input growth came from more calls or from a larger context.*");
            sb.AppendLine();
        }

        if (usage.ClaimsChecked > 0 || usage.AnswersWithVerification > 0)
        {
            sb.AppendLine($"**Claim verification:** {usage.ClaimsChecked} claims checked across {usage.AnswersWithVerification} answers — " +
                          $"{usage.ClaimsSupported} supported, **{usage.ClaimsRefuted} refuted**, {usage.ClaimsIndeterminate} indeterminate.");
            sb.AppendLine();
            sb.AppendLine("Read this beside the verifier's share of the cost table above: it is what that spend bought. A verifier that refutes nothing across a whole replicate set is either confirming the candidate is accurate or failing to test it, and the claim count is what separates those.");
            sb.AppendLine();

            AppendVerifierYield(sb, usage, result.Cost);
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    /// <summary>The cost role the claim verifier's spend is keyed under.</summary>
    private const string ClaimVerifierCostRole = "claimVerifier";

    /// <summary>
    /// The verifier's yield as division rather than as two counts placed side by side.
    ///
    /// <para>A section that shows the verifier taking half the spend and, separately, that it refuted
    /// nothing, has stated everything and concluded nothing. The ratios are what make the trade
    /// legible — and a zero denominator is said in words, because <c>∞</c>, <c>0</c> and a blank all
    /// read as a measurement.</para>
    /// </summary>
    private static void AppendVerifierYield(
        StringBuilder sb,
        BenchmarkGroupUsageStatistics usage,
        BenchmarkGroupCostStatistics? cost)
    {
        double? verifierCost = null;
        if (cost != null && cost.TotalCostByRole.TryGetValue(ClaimVerifierCostRole, out double resolved))
        {
            verifierCost = resolved;
        }

        sb.AppendLine("**What that spend bought, as arithmetic:**");
        sb.AppendLine();

        if (usage.ClaimsChecked <= 0)
        {
            sb.AppendLine("- **Cost per claim checked:** no claims were checked, so this ratio does not exist.");
        }
        else if (verifierCost.HasValue)
        {
            sb.AppendLine($"- **Cost per claim checked:** {Money(verifierCost.Value / usage.ClaimsChecked)} — {Money(verifierCost)} over {usage.ClaimsChecked} claims.");
        }
        else
        {
            sb.AppendLine($"- **Cost per claim checked:** — the verifier's cost is not resolvable for this group, so the {usage.ClaimsChecked} claims cannot be priced.");
        }

        if (usage.ClaimsRefuted <= 0)
        {
            sb.AppendLine("- **Cost per refutation:** no refutations, so this ratio does not exist. Every dollar the verifier spent bought a confirmation or a non-answer.");
        }
        else if (verifierCost.HasValue)
        {
            sb.AppendLine($"- **Cost per refutation:** {Money(verifierCost.Value / usage.ClaimsRefuted)} — {Money(verifierCost)} over {usage.ClaimsRefuted} refutation(s).");
        }
        else
        {
            sb.AppendLine($"- **Cost per refutation:** — {usage.ClaimsRefuted} refutation(s), but the verifier's cost is not resolvable for this group.");
        }

        sb.AppendLine(usage.ClaimsChecked > 0
            ? $"- **Indeterminate share:** {Inv(usage.ClaimsIndeterminate * 100.0 / usage.ClaimsChecked, "F1")} % — {usage.ClaimsIndeterminate} of {usage.ClaimsChecked}. A claim the verifier could not decide cost what a decided one cost and settled nothing, so this share is the part of the spend that bought no verdict."
            : "- **Indeterminate share:** no claims were checked, so there is no share to report.");
        sb.AppendLine();
    }

    private static void AppendComparison(
        StringBuilder sb,
        int section,
        BenchmarkGroupComparison cmp,
        string treatmentName,
        string? baselineName)
    {
        sb.AppendLine($"## {section}. Paired Group Comparison");
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
        int section,
        BenchmarkGroupStatisticsResult result)
    {
        sb.AppendLine($"## {section}. What This Analysis Cannot Decompose");
        sb.AppendLine();
        sb.AppendLine($"> {result.VarianceDecompositionCaveat}");
        sb.AppendLine();
        sb.AppendLine("Concretely: an item flagged unstable above may be unstable because the *candidate* answers it differently each time, or because the *grader* scores the same quality of answer differently each time. Nothing in a replicate set separates the two, because each run produces a new answer that is graded once. Separating them requires re-grading identical answers — `SecondOpinionMode = All`, or a re-assessment pass over stored answers — and the run's sampled assessor agreement is the partial measurement available today.");
        sb.AppendLine();
        sb.AppendLine("*Report generated by `BenchmarkGroupReportBuilder`. No part of it was written by a model.*");
    }
}
