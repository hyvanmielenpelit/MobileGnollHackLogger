namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MobileGnollHackLogger.Data;

/// <summary>One model a battery run used, with the settings the manifest's Graders block prints.</summary>
public sealed record BenchmarkBatteryRoleModel
{
    public string? DisplayName { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string? ThinkingLevel { get; init; }
    public string? ReasoningMode { get; init; }
    public string? ServiceTier { get; init; }

    /// <summary>A run's recorded snapshot of the role; null when the run recorded none.</summary>
    public static BenchmarkBatteryRoleModel? Of(SystemAiConfigurationSnapshot? snapshot)
        => snapshot == null
            ? null
            : new BenchmarkBatteryRoleModel
            {
                DisplayName = snapshot.DisplayName,
                Provider = snapshot.Provider,
                ModelId = snapshot.ModelId,
                ThinkingLevel = snapshot.ThinkingLevel,
                ReasoningMode = snapshot.ReasoningMode,
                ServiceTier = snapshot.ServiceTier
            };
}

/// <summary>
/// The models a battery run was measured and written with: every grading role as the newest usable
/// member run recorded it, and the report writer from the battery run's own configuration.
/// </summary>
public sealed record BenchmarkBatteryGraders
{
    /// <summary>The member run the roles were read from; null when there is no usable member.</summary>
    public long? SourceRunId { get; init; }

    /// <summary>The source run was graded by an assessor panel.</summary>
    public bool Panel { get; init; }

    public BenchmarkBatteryRoleModel? ModelUnderTest { get; init; }
    public BenchmarkBatteryRoleModel? Assessor { get; init; }

    /// <summary>Panel member B; null on a single-assessor run.</summary>
    public BenchmarkBatteryRoleModel? CoAssessor { get; init; }

    /// <summary>The reference reader of a panel run, the second reader of a single-assessor run.</summary>
    public BenchmarkBatteryRoleModel? Reader { get; init; }

    /// <summary>How the source run used its reader.</summary>
    public BenchmarkSecondOpinionMode ReaderMode { get; init; }

    /// <summary>Answers the source run's reader graded, and the source run's questions.</summary>
    public int ReaderGradedAnswerCount { get; init; }
    public int ReaderQuestionCount { get; init; }

    public BenchmarkBatteryRoleModel? ClaimVerifier { get; init; }

    /// <summary>The battery run's report-writer configuration id; null when none was chosen.</summary>
    public long? ReportWriterConfigurationId { get; init; }

    /// <summary>The report writer's configuration; null when none was chosen or it has been deleted.</summary>
    public BenchmarkBatteryRoleModel? ReportWriter { get; init; }
}

/// <summary>An earlier finished battery run of the same battery, as the manifest's history table lists it.</summary>
public sealed record BenchmarkBatteryEarlierRun
{
    public long BatteryRunId { get; init; }
    public DateTime? FinishedAtUtc { get; init; }

    /// <summary>The battery run has a stored analysis; the fields below are read from its latest.</summary>
    public bool Analysed { get; init; }

    public string? HarnessVersion { get; init; }

    /// <summary>Null when the latest analysis is incomplete or carries no headline.</summary>
    public double? OverallIndex { get; init; }

    public double? OverallIndexHalfWidth { get; init; }

    /// <summary>Null while the latest analysis is incomplete.</summary>
    public string? ComparabilityClassSha256 { get; init; }
}

/// <summary>
/// Builds the Markdown report of one battery analysis (multi-suite method, M1–M10).
///
/// <para>Arithmetic only: there is no AI-written synthesis, for the reason the multi-run report has
/// none. Every figure is read from a persisted <see cref="BenchmarkBatteryStatisticsResult"/>, never
/// from live runs, so the report stays reproducible after a member run is deleted. The one exception
/// is the usage section's tool-call outcomes and refuted answer sentences, which the caller counts
/// over the member runs' rows (<see cref="BenchmarkBatteryAnswerOutcomes"/>).</para>
/// </summary>
public static class BenchmarkBatteryReportBuilder
{
    private static string Inv(double value, string format = "F2")
        => value.ToString(format, CultureInfo.InvariantCulture);

    private static string Inv(double? value, string format = "F2", string nullText = "—")
        => value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : nullText;

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Stamp(DateTime value, string format = "yyyy-MM-dd HH:mm:ss")
        => value.ToString(format, CultureInfo.InvariantCulture);

    private static string Seconds(double? ms)
        => ms.HasValue ? Inv(ms.Value / 1000.0, "F1") + " s" : "—";

    private static string Money(double? value)
        => value.HasValue ? "$" + Inv(value.Value, value.Value >= 1.0 ? "F2" : "F4") : "—";

    private static string Percent(double? fraction)
        => fraction.HasValue ? Inv(fraction.Value * 100.0, "F1") + " %" : "—";

    private static string Tokens(long value)
    {
        if (value >= 1_000_000) return Inv(value / 1_000_000.0, "F2") + " M";
        if (value >= 1_000) return Inv(value / 1_000.0, "F1") + " k";
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string PValue(double? p)
    {
        if (!p.HasValue) return "—";
        return p.Value < 0.0001 ? "< 0.0001" : p.Value.ToString("F4", CultureInfo.InvariantCulture);
    }

    private static string Signed(double? value, string format = "F2")
        => value.HasValue ? (value.Value >= 0 ? "+" : string.Empty) + Inv(value.Value, format) : "—";

    private static string Short(string? sha)
        => string.IsNullOrWhiteSpace(sha) ? "—" : (sha.Length <= 12 ? sha : sha[..12]);

    /// <summary>The Fingerprints column's label for each corpus key of <see cref="CorpusIndexFingerprintProvider.CorpusKeys"/>, in that order.</summary>
    private static readonly (string Label, string Key)[] CorpusIndexLabels =
    {
        ("WIKI-IDX", "gnollhackWiki"),
        ("SRC-IDX", "gnollhackSource"),
        ("KB-IDX", "knowledgeBase"),
        ("NHW-IDX", "nethackWiki"),
        ("NHS-IDX", "nethackSource")
    };

    /// <summary>
    /// The run's corpus index fingerprints as Fingerprints-column lines, "—" for a corpus not indexed
    /// yet and for every corpus of a run that did not record them.
    /// </summary>
    private static string CorpusIndexFingerprints(BenchmarkRun run)
    {
        var fingerprints = CorpusIndexFingerprintProvider.Parse(run.CorpusIndexFingerprintsJson);
        return string.Join("<br>", CorpusIndexLabels.Select(c =>
            $"{c.Label} `{Short(fingerprints != null && fingerprints.TryGetValue(c.Key, out var fingerprint) ? fingerprint?.Sha256 : null)}`"));
    }

    private static string Cell(string? text)
        => string.IsNullOrWhiteSpace(text) ? string.Empty : text.Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|").Trim();

    /// <summary>The UI label of a weighting scheme.</summary>
    public static string SchemeLabel(BenchmarkBatteryWeightingScheme scheme) => scheme switch
    {
        BenchmarkBatteryWeightingScheme.DifficultyMass => "Questions and difficulty",
        BenchmarkBatteryWeightingScheme.ItemCount => "Questions only",
        BenchmarkBatteryWeightingScheme.Equal => "Equal per suite",
        BenchmarkBatteryWeightingScheme.Custom => "Custom",
        _ => scheme.ToString()
    };

    /// <summary>A wall-clock span as <c>2 d 3 h 04 min</c>, <c>3 h 04 min</c> or <c>4 min 05 s</c>.</summary>
    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalDays >= 1)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} d {1} h {2:00} min", (int)span.TotalDays, span.Hours, span.Minutes);
        }

        if (span.TotalHours >= 1)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} h {1:00} min", (int)span.TotalHours, span.Minutes);
        }

        return string.Format(CultureInfo.InvariantCulture, "{0} min {1:00} s", (int)span.TotalMinutes, span.Seconds);
    }

    /// <summary>
    /// The download file name: <c>{battery}_{model}_battery_R{n}_{yyyyMMdd_HHmmss}.md</c>, each part
    /// stripped of characters a file name cannot hold and with spaces turned into underscores.
    /// </summary>
    public static string BuildFileName(string? batteryName, string? modelName, int runsPerSuite, DateTime computedAtUtc)
        => $"{SanitizeFileNamePart(batteryName, "battery")}_{SanitizeFileNamePart(modelName, "model")}_battery_R"
           + $"{runsPerSuite.ToString(CultureInfo.InvariantCulture)}_{computedAtUtc.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.md";

    private static string SanitizeFileNamePart(string? name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean.Replace(' ', '_');
    }

    /// <summary>Renders the full report.</summary>
    /// <param name="batteryRun">The battery run, for its name, status, timing and member rows (when loaded).</param>
    /// <param name="definition">The definition snapshot the battery run executed.</param>
    /// <param name="result">The persisted statistics.</param>
    /// <param name="analysis">The persisted analysis row, for its hashes, versions and computation time.</param>
    /// <param name="memberRuns">The member runs, for the manifest table. May be empty if they were deleted.</param>
    /// <param name="comparability">The M8 verdict over the usable members, when re-resolved for the report.</param>
    /// <param name="comparison">A paired comparison against a baseline battery run, when one was computed.</param>
    /// <param name="comparisonLabel">The baseline's label for the comparison heading.</param>
    /// <param name="overseerVersion">The build that produced the report.</param>
    /// <param name="answerOutcomes">
    /// The usable members' tool-call outcomes and refuted answer sentences, from
    /// <see cref="BenchmarkBatteryAnswerOutcomes.LoadAsync"/>; when null, the usage section leaves them out.
    /// </param>
    /// <param name="graders">
    /// The manifest's Graders block, from <see cref="BenchmarkBatteryAnalysisService.LoadGradersAsync"/>;
    /// when null, the block is left out.
    /// </param>
    /// <param name="earlierRuns">
    /// Earlier finished battery runs of the same battery, newest first, from
    /// <see cref="BenchmarkBatteryAnalysisService.LoadEarlierRunsAsync"/>; the manifest's history table
    /// is left out when there is none.
    /// </param>
    public static string BuildMarkdownReport(
        BenchmarkBatteryRun batteryRun,
        BenchmarkBatteryDefinition definition,
        BenchmarkBatteryStatisticsResult result,
        BenchmarkBatteryAnalysis? analysis = null,
        IReadOnlyList<BenchmarkRun>? memberRuns = null,
        BenchmarkBatteryComparabilityResult? comparability = null,
        BenchmarkBatteryComparison? comparison = null,
        string? comparisonLabel = null,
        string? overseerVersion = null,
        BenchmarkBatteryAnswerOutcomes? answerOutcomes = null,
        BenchmarkBatteryGraders? graders = null,
        IReadOnlyList<BenchmarkBatteryEarlierRun>? earlierRuns = null)
    {
        ArgumentNullException.ThrowIfNull(batteryRun);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(result);

        var runs = memberRuns ?? Array.Empty<BenchmarkRun>();
        var sb = new StringBuilder();
        int section = 0;

        AppendHeader(sb, batteryRun, result, analysis, runs, overseerVersion);
        AppendManifest(sb, ++section, batteryRun, definition, result, analysis, runs, comparability, graders, earlierRuns);
        AppendOverall(sb, ++section, result);
        AppendProfile(sb, ++section, result);
        AppendSensitivity(sb, ++section, result);
        AppendLeaveOneOut(sb, ++section, result);
        AppendDimensions(sb, ++section, result);
        AppendSpeed(sb, ++section, batteryRun, result);
        AppendCost(sb, ++section, result);

        if (result.Usage != null)
        {
            AppendUsage(sb, ++section, result.Usage, answerOutcomes);
        }

        if (comparison != null)
        {
            AppendComparison(sb, ++section, comparison, batteryRun, comparisonLabel);
        }

        AppendLimits(sb, ++section, result);

        return sb.ToString();
    }

    // --- Header and manifest -----------------------------------------------------------------------

    private static void AppendHeader(
        StringBuilder sb,
        BenchmarkBatteryRun batteryRun,
        BenchmarkBatteryStatisticsResult result,
        BenchmarkBatteryAnalysis? analysis,
        IReadOnlyList<BenchmarkRun> runs,
        string? overseerVersion)
    {
        string? model = runs.Select(r => r.TestedModelSnapshot.Label()).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));

        sb.AppendLine($"# Multi-Suite Benchmark Analysis — {batteryRun.BatteryName}");
        sb.AppendLine();
        sb.AppendLine($"**Battery run:** #{batteryRun.Id} · **Status:** {batteryRun.Status}"
                      + (batteryRun.StopReason.HasValue ? $" ({batteryRun.StopReason.Value})" : string.Empty) + "  ");
        if (!string.IsNullOrWhiteSpace(model))
        {
            sb.AppendLine($"**Model under test:** {model}  ");
        }
        sb.AppendLine($"**Suites (*K*):** {Int(result.SuiteCount)} · **Runs per suite (*R*):** {Int(batteryRun.RunsPerSuite)}  ");
        sb.AppendLine($"**Completeness:** " + (result.Complete
            ? $"complete ({Int(result.CompletedSuiteCount)} of {Int(result.SuiteCount)} suites)"
            : $"**incomplete ({Int(result.CompletedSuiteCount)} of {Int(result.SuiteCount)} suites)**") + "  ");
        sb.AppendLine($"**Analysis computed:** {Stamp(analysis?.ComputedAtUtc ?? DateTime.UtcNow)} UTC  ");
        if (!string.IsNullOrWhiteSpace(overseerVersion))
        {
            sb.AppendLine($"**Overseer version:** {overseerVersion}  ");
        }
        sb.AppendLine();
        sb.AppendLine("> Every figure in this report is arithmetic over the members' stored answers. There is no AI-written synthesis in it, by design: a report used to decide between models or to keep a change has to be reproducible from its inputs.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void AppendManifest(
        StringBuilder sb,
        int section,
        BenchmarkBatteryRun batteryRun,
        BenchmarkBatteryDefinition definition,
        BenchmarkBatteryStatisticsResult result,
        BenchmarkBatteryAnalysis? analysis,
        IReadOnlyList<BenchmarkRun> runs,
        BenchmarkBatteryComparabilityResult? comparability,
        BenchmarkBatteryGraders? graders,
        IReadOnlyList<BenchmarkBatteryEarlierRun>? earlierRuns)
    {
        sb.AppendLine($"## {section}. Battery Manifest");
        sb.AppendLine();

        sb.AppendLine($"- **Battery:** {definition.Name}" + (definition.BatteryId.HasValue ? $" (#{definition.BatteryId.Value})" : string.Empty)
                      + $", revision {Int(definition.Revision)}");
        sb.AppendLine($"- **Weighting scheme:** {SchemeLabel(result.Scheme)} — declared in the definition before any result existed; the weights below are the ones this analysis used.");
        sb.AppendLine($"- **Definition hash:** `{analysis?.DefinitionSha256 ?? batteryRun.DefinitionSha256}`");
        sb.AppendLine(string.IsNullOrWhiteSpace(analysis?.ComparabilityClassSha256)
            ? "- **Comparability class:** — *recorded only for a complete battery.*"
            : $"- **Comparability class:** `{analysis!.ComparabilityClassSha256}` — two results stand in one ranked list only when this and the definition hash both agree.");
        if (analysis != null)
        {
            sb.AppendLine($"- **Harness version:** {analysis.HarnessVersion ?? "—"} · **Scoring method version:** {Int(analysis.ScoringMethodVersion)}");
        }

        sb.AppendLine($"- **Started:** {Stamp(batteryRun.StartedAtUtc)} UTC");
        if (batteryRun.CompletedAtUtc.HasValue)
        {
            sb.AppendLine($"- **Finished:** {Stamp(batteryRun.CompletedAtUtc.Value)} UTC");
            sb.AppendLine($"- **Battery wall clock:** {Duration(batteryRun.CompletedAtUtc.Value - batteryRun.StartedAtUtc)} from start to finish, cap waits and pauses included.");
        }
        else
        {
            sb.AppendLine("- **Battery wall clock:** — *the battery run has not finished.*");
        }
        sb.AppendLine();

        AppendGraders(sb, graders);

        sb.AppendLine("| # | Suite | Suite id | Declared weight | Count weight | Exam items | Difficulty mass | Usable runs | Game board |");
        sb.AppendLine("|---:|---|---:|---:|---:|---:|---:|---:|---|");
        foreach (var def in definition.Suites.OrderBy(s => s.Index))
        {
            var profile = result.Suites.FirstOrDefault(p => p.SuiteIndex == def.Index);
            var prompt = profile?.Statistics?.PromptUnderTest;
            string board = prompt == null || !prompt.Recorded ? "—" : prompt.HasGameSnapshot ? "yes" : "no";
            sb.AppendLine(
                $"| {Int(def.Index + 1)} " +
                $"| {Cell(def.SuiteName)} " +
                $"| {def.SuiteId.ToString(CultureInfo.InvariantCulture)} " +
                $"| {Percent(profile?.Weight)} " +
                $"| {Percent(profile?.CountWeight)} " +
                $"| {(profile != null ? Int(profile.ExamItemCount) + (profile.ExamIncomplete ? $" of {Int(profile.ExpectedQuestionCount)} †" : string.Empty) : "—")} " +
                $"| {Inv(profile?.DifficultyMass, "F1")} " +
                $"| {(profile != null ? Int(profile.UsableMemberCount) : "0")} " +
                $"| {board} |");
        }
        sb.AppendLine();
        if (result.Suites.Any(p => p.ExamIncomplete))
        {
            sb.AppendLine("*† The exam built from the members' answers holds fewer questions than the members were asked, so the suite's difficulty mass and the pooled identity are approximate.*");
            sb.AppendLine();
        }
        if (definition.Scheme == BenchmarkBatteryWeightingScheme.Custom)
        {
            sb.AppendLine("Declared custom weights (before normalization): "
                + string.Join(", ", definition.Suites.OrderBy(s => s.Index).Select(s => $"{Cell(s.SuiteName)} {Inv(s.CustomWeight, "R")}")) + ".");
            sb.AppendLine();
        }

        AppendMembers(sb, batteryRun, result, runs);

        if (result.ExcludedMembers.Count > 0)
        {
            sb.AppendLine("**Members left out of every statistic (M4):**");
            sb.AppendLine();
            foreach (var excluded in result.ExcludedMembers.OrderBy(e => e.SuiteIndex).ThenBy(e => e.Round).ThenBy(e => e.RunId))
            {
                sb.AppendLine($"- Run {excluded.RunId.ToString(CultureInfo.InvariantCulture)} — suite '{SuiteName(definition, result, excluded.SuiteIndex)}', round {Int(excluded.Round)}: {excluded.Reason}.");
            }
            sb.AppendLine();
            sb.AppendLine("A member whose run is repaired in place (*Re-run Failed Questions*) keeps its run id; recompute the analysis after a repair.");
            sb.AppendLine();
        }

        if (comparability != null)
        {
            sb.AppendLine($"**Composite permitted:** {(comparability.CompositePermitted ? "yes" : "**no**")}. {comparability.Explanation}");
            sb.AppendLine();
            foreach (var difference in comparability.BatteryWideDifferences.Concat(comparability.PromptDifferences))
            {
                sb.AppendLine($"- **{difference.Name}** — {difference.Describe()}");
            }
            if (comparability.BatteryWideDifferences.Count + comparability.PromptDifferences.Count > 0)
            {
                sb.AppendLine();
            }
        }

        AppendPromptUnderTest(sb, section, result);
        AppendEarlierRuns(sb, section, analysis, earlierRuns);

        sb.AppendLine("---");
        sb.AppendLine();
    }

    /// <summary>The sentence under the history table of earlier battery runs.</summary>
    public const string EarlierRunsComparisonNote =
        "Only a run of the same class may be compared with this one; use the Paired Test tab for a test.";

    /// <summary>
    /// The models the battery run was measured and written with, one row per role, each with its
    /// thinking level. Left out when the caller loaded no roster.
    /// </summary>
    private static void AppendGraders(StringBuilder sb, BenchmarkBatteryGraders? graders)
    {
        if (graders == null) return;

        string source = graders.SourceRunId.HasValue
            ? $"as recorded on run {graders.SourceRunId.Value.ToString(CultureInfo.InvariantCulture)}, the newest usable member"
            : "no usable member run recorded them";
        sb.AppendLine($"**Graders** — {source}; the report writer is the battery run's own configuration.");
        sb.AppendLine();
        sb.AppendLine("| Role | Model | Provider | Thinking level | Settings |");
        sb.AppendLine("|---|---|---|---|---|");

        var tested = graders.ModelUnderTest;
        RoleRow(sb, "Model under test", tested,
            tested == null ? null : $"reasoning mode {Setting(tested.ReasoningMode)} · service tier {Setting(tested.ServiceTier)}",
            "not recorded");
        RoleRow(sb, graders.Panel ? "Assessor (panel member A)" : "Assessor", graders.Assessor, null, "not recorded");
        RoleRow(sb, "Co-assessor (panel member B)", graders.CoAssessor, null, "none — a single assessor graded every answer");

        string coverage = $"coverage: {ReaderModeLabel(graders.ReaderMode)} (`{graders.ReaderMode}`)"
            + (graders.SourceRunId.HasValue && graders.ReaderQuestionCount > 0
                ? $", {Int(graders.ReaderGradedAnswerCount)} of {Int(graders.ReaderQuestionCount)} answers on run {graders.SourceRunId.Value.ToString(CultureInfo.InvariantCulture)}"
                : string.Empty);
        RoleRow(sb, graders.Panel ? "Reference reader" : "Second reader", graders.Reader, coverage, "none");
        RoleRow(sb, "Claim verifier", graders.ClaimVerifier, null, "none");
        RoleRow(sb, "Report writer", graders.ReportWriter, "writes the AI-written battery documents; grades nothing",
            graders.ReportWriterConfigurationId.HasValue
                ? $"configuration #{graders.ReportWriterConfigurationId.Value.ToString(CultureInfo.InvariantCulture)}, since deleted"
                : "none chosen");
        sb.AppendLine();
    }

    private static void RoleRow(StringBuilder sb, string role, BenchmarkBatteryRoleModel? model, string? settings, string absentText)
    {
        if (model == null)
        {
            sb.AppendLine($"| {role} | *{absentText}* | — | — | — |");
            return;
        }

        string name = string.IsNullOrWhiteSpace(model.DisplayName) || string.Equals(model.DisplayName, model.ModelId, StringComparison.Ordinal)
            ? $"`{Cell(model.ModelId)}`"
            : $"{Cell(model.DisplayName)} (`{Cell(model.ModelId)}`)";
        sb.AppendLine($"| {role} | {name} | {Cell(model.Provider)} | {Setting(model.ThinkingLevel)} | {(string.IsNullOrWhiteSpace(settings) ? "—" : settings)} |");
    }

    /// <summary>A recorded setting, or <c>Default</c> when the configuration left it unset.</summary>
    private static string Setting(string? value) => string.IsNullOrWhiteSpace(value) ? "Default" : Cell(value);

    private static string ReaderModeLabel(BenchmarkSecondOpinionMode mode) => mode switch
    {
        BenchmarkSecondOpinionMode.Off => "off",
        BenchmarkSecondOpinionMode.Flagged => "flagged answers",
        BenchmarkSecondOpinionMode.FlaggedAndOutliers => "flagged answers and outliers",
        BenchmarkSecondOpinionMode.All => "every answer",
        BenchmarkSecondOpinionMode.FlaggedPlusSample => "flagged answers plus a sample",
        _ => mode.ToString()
    };

    /// <summary>
    /// Earlier finished battery runs of the same battery, newest first, each marked by whether it
    /// shares this analysis's comparability class. Left out when there is none.
    /// </summary>
    private static void AppendEarlierRuns(
        StringBuilder sb,
        int section,
        BenchmarkBatteryAnalysis? analysis,
        IReadOnlyList<BenchmarkBatteryEarlierRun>? earlierRuns)
    {
        if (earlierRuns == null || earlierRuns.Count == 0) return;

        string? ownClass = analysis?.ComparabilityClassSha256;

        sb.AppendLine($"### {section}.2 Earlier Runs of This Battery");
        sb.AppendLine();
        sb.AppendLine("| Battery run | Finished | Harness version | Overall Index | Same class |");
        sb.AppendLine("|---:|---|---|---:|---|");
        foreach (var earlier in earlierRuns.Take(BenchmarkBatteryAnalysisService.EarlierRunLimit))
        {
            string index = earlier.OverallIndex.HasValue
                ? Inv(earlier.OverallIndex.Value, "F2") + (earlier.OverallIndexHalfWidth.HasValue ? " ± " + Inv(earlier.OverallIndexHalfWidth.Value, "F2") : string.Empty)
                : earlier.Analysed ? "*Incomplete*" : "*not analysed*";
            bool sameClass = !string.IsNullOrEmpty(ownClass)
                             && string.Equals(ownClass, earlier.ComparabilityClassSha256, StringComparison.OrdinalIgnoreCase);

            sb.AppendLine(
                $"| #{earlier.BatteryRunId.ToString(CultureInfo.InvariantCulture)} " +
                $"| {(earlier.FinishedAtUtc.HasValue ? Stamp(earlier.FinishedAtUtc.Value, "yyyy-MM-dd HH:mm") + " UTC" : "—")} " +
                $"| {(string.IsNullOrWhiteSpace(earlier.HarnessVersion) ? "—" : Cell(earlier.HarnessVersion))} " +
                $"| {index} " +
                $"| {(sameClass ? "yes" : "no")} |");
        }
        sb.AppendLine();
        sb.AppendLine(EarlierRunsComparisonNote);
        sb.AppendLine();
    }

    private static void AppendMembers(
        StringBuilder sb,
        BenchmarkBatteryRun batteryRun,
        BenchmarkBatteryStatisticsResult result,
        IReadOnlyList<BenchmarkRun> runs)
    {
        var runById = runs.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        var members = (batteryRun.Members ?? new List<BenchmarkBatteryRunMember>())
            .Where(m => !m.Superseded)
            .OrderBy(m => m.Round)
            .ThenBy(m => m.SuiteIndex)
            .ToList();

        if (members.Count == 0 && runs.Count == 0)
        {
            var ids = result.Suites.SelectMany(s => s.RunIndices.Select(r => r.RunId)).Distinct().OrderBy(id => id).ToList();
            if (ids.Count > 0)
            {
                sb.AppendLine("Usable member runs: " + string.Join(", ", ids.Select(id => id.ToString(CultureInfo.InvariantCulture))));
                sb.AppendLine();
                sb.AppendLine("*The member rows are unavailable here. The statistics below are the persisted result and are unaffected.*");
                sb.AppendLine();
            }
            return;
        }

        var excludedIds = new HashSet<long>(result.ExcludedMembers.Select(e => e.RunId));

        sb.AppendLine("| Round | Suite | Run | Origin | Status | Index | Speed | Usable | Fingerprints |");
        sb.AppendLine("|---:|---:|---:|---|---|---:|---:|---|---|");

        IEnumerable<(int? Round, int? SuiteIndex, long RunId, string Origin)> rows = members.Count > 0
            ? members.Select(m => ((int?)m.Round, (int?)m.SuiteIndex, m.BenchmarkRunId, m.Origin.ToString()))
            : runs.OrderBy(r => r.Id).Select(r => ((int?)null, (int?)null, r.Id, "—"));

        foreach (var row in rows)
        {
            runById.TryGetValue(row.RunId, out var run);
            sb.AppendLine(
                $"| {(row.Round.HasValue ? Int(row.Round.Value) : "—")} " +
                $"| {(row.SuiteIndex.HasValue ? Int(row.SuiteIndex.Value + 1) : "—")} " +
                $"| {row.RunId.ToString(CultureInfo.InvariantCulture)} " +
                $"| {row.Origin} " +
                $"| {(run != null ? run.Status.ToString() : "deleted")} " +
                $"| {(run?.QualityIndex.HasValue == true ? run.QualityIndex.Value.ToString(CultureInfo.InvariantCulture) : "—")} " +
                $"| {(run?.SpeedIndex.HasValue == true ? run.SpeedIndex.Value.ToString(CultureInfo.InvariantCulture) : "—")} " +
                $"| {(excludedIds.Contains(row.RunId) ? "no" : "yes")} " +
                $"| {(run == null ? "—" : $"PROMPT `{Short(run.CandidateSystemPromptSha256)}`<br>GUIDES `{Short(run.ToolGuidesSha256)}`<br>KB `{Short(run.KnowledgeBaseHeadSha)}`<br>WIKI `{Short(run.WikiHeadSha)}`<br>SRC `{Short(run.SourceCodeHeadSha)}`<br>{CorpusIndexFingerprints(run)}")} |");
        }
        sb.AppendLine();
        sb.AppendLine("*Suite numbers are 1-based positions in the battery definition. Index is the run's stored integer `QualityIndex`; the profile below recomputes each suite from fixed item weights, unrounded.*");
        sb.AppendLine();
    }

    /// <summary>
    /// What the candidate was told, from the first suite with a recorded prompt configuration. The
    /// board flag differs by suite by design and is listed per suite in the table above.
    /// </summary>
    private static void AppendPromptUnderTest(StringBuilder sb, int section, BenchmarkBatteryStatisticsResult result)
    {
        var prompt = result.Suites
            .OrderBy(s => s.SuiteIndex)
            .Select(s => s.Statistics?.PromptUnderTest)
            .FirstOrDefault(p => p != null && p.Recorded);

        sb.AppendLine($"### {section}.1 Chat Prompt Under Test");
        sb.AppendLine();
        sb.AppendLine("The candidates answered under the **production Overseer chat system prompt**, not a benchmark-specific prompt, so every quality verdict below is a verdict on the prompt real users receive.");
        sb.AppendLine();

        if (prompt == null)
        {
            sb.AppendLine("- *The prompt configuration was not recorded for these runs.*");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"- **Mode:** {(prompt.OverseerMode == 0 ? "Gameplay Help" : $"Mode {prompt.OverseerMode}")} · " +
                      $"**Response style:** {(prompt.VerboseMode ? "detailed (`verboseMode: true`)" : "concise (`verboseMode: false`)")}");
        sb.AppendLine($"- **Tools:** {(prompt.EnableToolUse ? "enabled" : "disabled")} · " +
                      $"**Web search:** {(prompt.EnableWebSearch ? "enabled" : "disabled")} · " +
                      $"**Subagents:** {(prompt.EnableSubAgents ? "enabled" : "disabled")} · " +
                      $"**Source code references:** {(prompt.AllowSourceCodeReferences ? "allowed" : "disallowed")}");
        sb.AppendLine($"- **Spoiler-free mode:** {(prompt.SpoilerFreeMode ? "on" : "off")} · " +
                      $"**Pre-injected wiki context:** {(prompt.HasWikiContext ? "yes" : "no")} · " +
                      $"**Tool batching policy:** {prompt.ParallelMode}");
        sb.AppendLine();
        sb.AppendLine("> Every option except the game-board flag is a battery-wide comparability key, so every usable member was graded under this configuration; the board flag follows the suite.");
        sb.AppendLine();
    }

    // --- Headline ------------------------------------------------------------------------------------

    private static void AppendOverall(StringBuilder sb, int section, BenchmarkBatteryStatisticsResult result)
    {
        sb.AppendLine($"## {section}. Overall Intelligence Index");
        sb.AppendLine();

        var ix = result.OverallIndex;
        if (!result.Complete || ix == null)
        {
            sb.AppendLine($"> **Incomplete ({Int(result.CompletedSuiteCount)} of {Int(result.SuiteCount)} suites): no Overall Index is reported.** A suite without a usable member would have to be dropped and the remaining weights renormalized, which changes what the index estimates and makes a partial battery look complete. The per-suite figures below are reported as they stand.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"- **Overall Intelligence Index:** {Inv(ix.PointEstimate, "F2")} / 100 — Σ *w*ₛ · *I*ₛ under *{SchemeLabel(result.Scheme)}* weights.");
        sb.AppendLine($"- **Combined 95 % interval:** {Inv(ix.PointEstimate, "F2")} ± {Inv(ix.CombinedHalfWidth)}" +
                      (ix.CombinedLower.HasValue && ix.CombinedUpper.HasValue
                          ? $"  →  [{Inv(ix.CombinedLower)}, {Inv(ix.CombinedUpper)}]"
                          : string.Empty));
        if (ix.CombinedIntervalTruncated)
        {
            sb.AppendLine("  - **Truncated at the score bound.** The half-width is the honest one; the reported bound is pinned at 0 or 100.");
        }
        sb.AppendLine(result.PooledIdentityHolds
            ? "- **Pooled identity holds:** under the default scheme this index equals the difficulty-weighted mean over every question of every suite."
            : "- **Pooled identity is approximate:** some exam question has no scored answer, or an exam is shorter than its members' question count.");
        sb.AppendLine();

        sb.AppendLine($"### {section}.1 The two uncertainty components");
        sb.AppendLine();
        sb.AppendLine("| Component | Question it answers | SE | Degrees of freedom | Critical value | 95 % half-width | Source |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---|");
        sb.AppendLine(
            "| **Item sampling** | *Would different questions move this?* " +
            $"| {Inv(ix.ItemSamplingStandardError, "F3")} " +
            $"| {Inv(ix.EffectiveDegreesOfFreedom, "F1")} (Welch–Satterthwaite) " +
            $"| {Inv(ix.ItemSamplingCriticalValue, "F3")} " +
            $"| {Inv(ix.ItemSamplingHalfWidth)} " +
            "| suites as independent strata |");
        sb.AppendLine(
            "| **Reproducibility** | *Would re-running the battery move this?* " +
            $"| {Inv(ix.ReproducibilityStandardError, "F3")} " +
            $"| {Inv(ix.ReproducibilityDegreesOfFreedom, "F1")} " +
            $"| {Inv(ix.ReproducibilityCriticalValue, "F3")} " +
            $"| {Inv(ix.ReproducibilityHalfWidth)} " +
            $"| {ReproducibilityLabel(ix.ReproducibilitySource)} |");
        sb.AppendLine();

        if (ix.ItemSamplingWithheldBySuiteIndex.Count > 0)
        {
            sb.AppendLine($"> **Item-sampling component withheld:** {string.Join(", ", ix.ItemSamplingWithheldBySuiteIndex.Select(i => $"'{SuiteName(null, result, i)}'"))} has fewer than three scored items, so no standard error exists for it.");
            sb.AppendLine();
        }

        switch (ix.ReproducibilitySource)
        {
            case BenchmarkBatteryReproducibilitySource.NotAvailable:
                sb.AppendLine($"> {BenchmarkBatteryStatistics.NoReproducibilityCaveat}");
                sb.AppendLine();
                break;
            case BenchmarkBatteryReproducibilitySource.PerSuiteFallback:
                sb.AppendLine($"> {BenchmarkBatteryStatistics.PerSuiteFallbackCaveat}");
                sb.AppendLine();
                break;
        }

        if (ix.PerRoundIndices.Count > 0)
        {
            sb.AppendLine($"- **Per-round composites ({Int(ix.RoundCount)} complete rounds):** "
                + string.Join(", ", ix.PerRoundIndices.OrderBy(r => r.Round).Select(r => $"R{Int(r.Round)} {Inv(r.Index, "F2")}")));
            sb.AppendLine();
        }

        sb.AppendLine($"> {BenchmarkBatteryStatistics.CriticalValueCaveat}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static string ReproducibilityLabel(BenchmarkBatteryReproducibilitySource source) => source switch
    {
        BenchmarkBatteryReproducibilitySource.Rounds => "SD of the per-round composites",
        BenchmarkBatteryReproducibilitySource.PerSuiteFallback => "per-suite fallback (ragged rounds)",
        _ => "not available (*R* < 3)"
    };

    // --- Diagnostics ---------------------------------------------------------------------------------

    private static void AppendProfile(StringBuilder sb, int section, BenchmarkBatteryStatisticsResult result)
    {
        sb.AppendLine($"## {section}. Suite Profile");
        sb.AppendLine();
        sb.AppendLine("Each suite's own multi-run index *I*ₛ, unrounded, with its own interval on the group layer's normal 1.96 for item sampling. The contribution is *w*ₛ · *I*ₛ; the contributions of a complete battery sum to the Overall Index.");
        sb.AppendLine();
        sb.AppendLine("| # | Suite | *w*ₛ | *v*ₛ | Scored / exam items | Usable runs | *I*ₛ | 95 % interval | Contribution | Mean Speed Index | Critical-error rate |");
        sb.AppendLine("|---:|---|---:|---:|---:|---:|---:|---|---:|---:|---:|");

        foreach (var p in result.Suites.OrderBy(s => s.SuiteIndex))
        {
            string interval = p.CombinedLower.HasValue && p.CombinedUpper.HasValue
                ? $"[{Inv(p.CombinedLower, "F1")}, {Inv(p.CombinedUpper, "F1")}]" + (p.CombinedIntervalTruncated ? " ‡" : string.Empty)
                : "—";
            sb.AppendLine(
                $"| {Int(p.SuiteIndex + 1)} " +
                $"| {Cell(p.SuiteName)} " +
                $"| {Percent(p.Weight)} " +
                $"| {Percent(p.CountWeight)} " +
                $"| {Int(p.ScoredItemCount)} / {Int(p.ExamItemCount)} " +
                $"| {Int(p.UsableMemberCount)} " +
                $"| {(p.Complete ? Inv(p.Index, "F2") : "*no usable member*")} " +
                $"| {interval} " +
                $"| {Inv(p.Contribution, "F2")} " +
                $"| {Inv(p.MeanSpeedIndex, "F1")} " +
                $"| {Percent(p.CriticalErrorRate)} |");
        }
        sb.AppendLine();

        if (result.Suites.Any(p => p.CombinedIntervalTruncated))
        {
            sb.AppendLine("*‡ Truncated at the score bound.*");
            sb.AppendLine();
        }

        if (result.BetweenSuiteStandardDeviation.HasValue || result.BetweenSuiteRange.HasValue)
        {
            sb.AppendLine($"**Profile unevenness:** between-suite SD {Inv(result.BetweenSuiteStandardDeviation, "F2")}, range {Inv(result.BetweenSuiteRange, "F2")} points. A large spread says the model is strong in some task areas and weak in others, which a single index hides.");
            sb.AppendLine();
        }

        var withRuns = result.Suites.Where(p => p.RunIndices.Count > 1).OrderBy(p => p.SuiteIndex).ToList();
        if (withRuns.Count > 0)
        {
            sb.AppendLine("**Per-run suite indices** (from the item rows, fixed weights):");
            sb.AppendLine();
            foreach (var p in withRuns)
            {
                sb.AppendLine($"- {Cell(p.SuiteName)}: " + string.Join(", ", p.RunIndices
                    .OrderBy(r => r.Round ?? int.MaxValue)
                    .ThenBy(r => r.RunId)
                    .Select(r => $"run {r.RunId.ToString(CultureInfo.InvariantCulture)}" + (r.Round.HasValue ? $" (R{Int(r.Round.Value)})" : string.Empty) + $" {Inv(r.Index, "F1")}")));
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void AppendSensitivity(StringBuilder sb, int section, BenchmarkBatteryStatisticsResult result)
    {
        sb.AppendLine($"## {section}. Weighting Sensitivity");
        sb.AppendLine();

        if (result.WeightingSensitivity.Count == 0)
        {
            sb.AppendLine("*Not reported: the battery is incomplete.*");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("The Overall Index recomputed under every automatic scheme. Only the declared row is the result; the others are **sensitivity** figures, which show whether a ranking of two models depends on the weighting.");
        sb.AppendLine();
        sb.AppendLine("| Scheme | Role | Weights | Overall Index |");
        sb.AppendLine("|---|---|---|---:|");
        foreach (var row in result.WeightingSensitivity)
        {
            sb.AppendLine(
                $"| {SchemeLabel(row.Scheme)} " +
                $"| {(row.Declared ? "**declared**" : "sensitivity")} " +
                $"| {string.Join(" / ", row.Weights.Select(w => Percent(w)))} " +
                $"| {Inv(row.Index, "F2")} |");
        }
        sb.AppendLine();

        if (result.PanelVerificationClearedOverall is double panelOverall)
        {
            sb.AppendLine($"### {section}.1 Grading sensitivity (advisory)");
            sb.AppendLine();
            sb.AppendLine("The Overall Index with each panel member's Accuracy one level higher where the claim verifier supported every sentence that member charged, or every out-of-rubric claim it raised, under the declared weights; a suite without the figure keeps its published index. It is a lower bound — a charge the verifier wrongly refuted is not lifted — and it moves no score.");
            sb.AppendLine();
            sb.AppendLine("| Figure | Published | Panel verification-cleared |");
            sb.AppendLine("|---|---:|---:|");
            sb.AppendLine($"| Overall Index (published) | {Inv(result.OverallIndex?.PointEstimate, "F2")} | — |");
            sb.AppendLine($"| Panel verification-cleared Overall Index | — | {Inv(panelOverall, "F2")} |");
            foreach (var suite in result.Suites.OrderBy(s => s.SuiteIndex))
            {
                sb.AppendLine($"| {Cell(suite.SuiteName)} | {Inv(suite.Index, "F2")} | {Inv(suite.PanelVerificationClearedIndex, "F2")} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void AppendLeaveOneOut(StringBuilder sb, int section, BenchmarkBatteryStatisticsResult result)
    {
        sb.AppendLine($"## {section}. Leave-One-Suite-Out");
        sb.AppendLine();

        if (result.LeaveOneSuiteOut.Count == 0)
        {
            sb.AppendLine("*Not reported: the battery is incomplete.*");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("The Overall Index with each suite omitted and the remaining declared weights renormalized: how much any one suite drives the result.");
        sb.AppendLine();
        sb.AppendLine("| Suite omitted | Overall Index without it | Change |");
        sb.AppendLine("|---|---:|---:|");
        foreach (var row in result.LeaveOneSuiteOut.OrderBy(r => r.SuiteIndex))
        {
            sb.AppendLine($"| {Cell(row.SuiteName)} | {Inv(row.Index, "F2")} | {Signed(row.Change)} |");
        }
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void AppendDimensions(StringBuilder sb, int section, BenchmarkBatteryStatisticsResult result)
    {
        sb.AppendLine($"## {section}. Quality Dimensions");
        sb.AppendLine();

        if (result.Dimensions.Count == 0)
        {
            sb.AppendLine("*Not reported: the battery is incomplete.*");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("Each dimension is Σ *v*ₛ · dimₛ with the count weights *v*ₛ: a dimension mean is unweighted within a suite, so the count weights make the composite the pooled mean over every question, where the declared difficulty weights would mix two different averages.");
        sb.AppendLine();
        sb.AppendLine("| Dimension | Composite mean | Withheld by |");
        sb.AppendLine("|---|---:|---|");
        foreach (var dim in result.Dimensions)
        {
            sb.AppendLine($"| {dim.Dimension} | {Inv(dim.Mean, "F1")} " +
                          $"| {(dim.WithheldBySuiteIndex.Count == 0 ? "—" : string.Join(", ", dim.WithheldBySuiteIndex.Select(i => $"'{SuiteName(null, result, i)}'")))} |");
        }
        sb.AppendLine();
        sb.AppendLine($"**Critical-error rate:** {Percent(result.CriticalErrorRate)} — Σ *v*ₛ · rateₛ, each suite's rate the mean of its items' critical-error rates.");
        sb.AppendLine();
        sb.AppendLine("> A dimension can be low because the prompt instructed the model to answer that way. Check the response style in the manifest before reading a low figure here as a model weakness.");
        sb.AppendLine();

        if (result.PanelAgreement is { } panel)
        {
            AppendPanelAgreement(sb, section, panel);
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    /// <summary>The panel agreement block of a panel battery, pooled over its member runs.</summary>
    private static void AppendPanelAgreement(StringBuilder sb, int section, BenchmarkBatteryPanelAgreement panel)
    {
        int pairs = panel.PairCount ?? 0;
        sb.AppendLine($"### {section}.1 Panel Agreement");
        sb.AppendLine();
        sb.AppendLine("| Figure | Value |");
        sb.AppendLine("|---|---:|");
        sb.AppendLine($"| Member A alone, under the declared weights | {Inv(panel.MemberAAloneIndex, "F2")} |");
        sb.AppendLine($"| Member B alone, under the declared weights | {Inv(panel.MemberBAloneIndex, "F2")} |");
        sb.AppendLine($"| Reference reader (advisory), under the declared weights | {Inv(panel.ReferenceReaderIndex, "F2")} |");
        sb.AppendLine($"| Reference reader's mean offset from the panel | {Points(panel.ReferenceReaderOffset)} |");
        sb.AppendLine($"| Mean \\|B − A\\| | {Points(panel.MeanAbsoluteDelta, signed: false)} |");
        sb.AppendLine($"| Mean signed B − A | {Points(panel.MeanSignedDelta)} |");
        sb.AppendLine($"| Pooled ICC(A,1) | {Inv(panel.IntraclassCorrelation, "F2")} over {Int(pairs)} answers both members scored |");
        sb.AppendLine($"| Disagreements | {(panel.Disagreements is int disagreements ? $"{Int(disagreements)} of {Int(pairs)} answers" : "—")} |");
        sb.AppendLine();
        sb.AppendLine("The ICC is computed over the pooled answers; it is not the mean of the runs' ICCs.");
        sb.AppendLine();
    }

    private static string Points(double? value, bool signed = true)
        => value.HasValue ? (signed ? Signed(value) : Inv(value.Value)) + " points" : "—";

    private static void AppendSpeed(StringBuilder sb, int section, BenchmarkBatteryRun batteryRun, BenchmarkBatteryStatisticsResult result)
    {
        sb.AppendLine($"## {section}. Speed");
        sb.AppendLine();

        var sp = result.Speed;
        if (sp == null)
        {
            sb.AppendLine("*Not reported: the battery is incomplete.*");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            return;
        }

        if (sp.Degraded)
        {
            sb.AppendLine($"> **Degraded — these figures mix timing conditions.** {sp.DegradedReason}");
            sb.AppendLine();
        }

        sb.AppendLine($"- **Overall Speed Index:** {Inv(sp.OverallSpeedIndex, "F1")}" +
                      (sp.SpeedIndexWithheldBySuiteIndex.Count > 0
                          ? $" — withheld: {string.Join(", ", sp.SpeedIndexWithheldBySuiteIndex.Select(i => $"'{SuiteName(null, result, i)}'"))} has no Speed Index"
                          : " — Σ *v*ₛ · mean Speed Indexₛ, from the runs' stored integer Speed Indices, so it can differ from the pooled per-answer mean by up to 0.5"));
        sb.AppendLine($"- **Pooled per-answer model time** over {Int(sp.PooledAnswerCount)} answers — P50 {Seconds(sp.ModelTimeP50Ms)}, P90 {Seconds(sp.ModelTimeP90Ms)}, max {Seconds(sp.ModelTimeMaxMs)}, mean {Seconds(sp.ModelTimeMeanMs)}");
        sb.AppendLine(sp.TtftAnswerCount > 0
            ? $"- **Pooled time to first token** over {Int(sp.TtftAnswerCount)} answers — P50 {Seconds(sp.TtftP50Ms)}, P90 {Seconds(sp.TtftP90Ms)}, max {Seconds(sp.TtftMaxMs)}"
            : "- **Pooled time to first token:** — *no answer recorded one.*");
        sb.AppendLine($"- **Sum of candidate answer time:** {Seconds(sp.TotalModelTimeMs)} over every usable member");
        sb.AppendLine(batteryRun.CompletedAtUtc.HasValue
            ? $"- **Battery wall clock:** {Duration(batteryRun.CompletedAtUtc.Value - batteryRun.StartedAtUtc)}"
            : "- **Battery wall clock:** — *not finished.*");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void AppendCost(StringBuilder sb, int section, BenchmarkBatteryStatisticsResult result)
    {
        sb.AppendLine($"## {section}. Cost");
        sb.AppendLine();

        var cost = result.Cost;
        if (cost == null)
        {
            sb.AppendLine("*Not reported: the battery is incomplete.*");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            return;
        }

        if (cost.Degraded)
        {
            sb.AppendLine($"> **Degraded — these figures mix pricing conditions.** {cost.DegradedReason} A pricing difference degrades cost only; it cannot move a quality score.");
            sb.AppendLine();
        }

        if (!cost.Available)
        {
            sb.AppendLine($"*{cost.WithheldReason ?? "Cost unknown."} An unknown cost is reported as unknown, never as zero.*");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"- **Total across every usable member run:** {Money(cost.TotalCost)}");
        sb.AppendLine($"- **One battery pass** (Σ of the suites' mean cost per run): {Money(cost.PassCost)}");
        sb.AppendLine($"- **Cost per question asked:** {Money(cost.CostPerQuestion)}" +
                      (cost.AnswerRowCount.HasValue ? $" over {Int(cost.AnswerRowCount.Value)} answer rows" : string.Empty));
        sb.AppendLine($"- **Cost per Overall Index point** (pass cost ÷ index): {Money(cost.CostPerIndexPoint)}");
        sb.AppendLine();

        if (cost.TotalCostByRole is { Count: > 0 })
        {
            sb.AppendLine("| Role | Total | Per battery pass | Share |");
            sb.AppendLine("|---|---:|---:|---:|");
            foreach (var kv in cost.TotalCostByRole.OrderByDescending(k => k.Value))
            {
                double? pass = cost.PassCostByRole != null && cost.PassCostByRole.TryGetValue(kv.Key, out double p) ? p : null;
                double? share = cost.TotalCost.HasValue && cost.TotalCost.Value > 0.0 ? kv.Value / cost.TotalCost.Value : null;
                sb.AppendLine($"| {kv.Key} | {Money(kv.Value)} | {Money(pass)} | {Percent(share)} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("The report writer's cost is not included: it is spent after this analysis is computed, and the battery progress dialog's Total cost shows it once the AI-written reports exist.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void AppendUsage(
        StringBuilder sb, int section, BenchmarkBatteryUsageStatistics usage, BenchmarkBatteryAnswerOutcomes? answerOutcomes)
    {
        sb.AppendLine($"## {section}. Token and Tool Usage");
        sb.AppendLine();
        sb.AppendLine($"- **Candidate tokens:** {Tokens(usage.TotalInputTokens)} in / {Tokens(usage.TotalOutputTokens)} out; prompt cache reads {Tokens(usage.TotalCacheReadTokens)}");
        sb.AppendLine($"- **Grader tokens, kept separate:** assessor {Tokens(usage.TotalAssessmentInputTokens)} in / {Tokens(usage.TotalAssessmentOutputTokens)} out; " +
                      $"claim verifier {Tokens(usage.TotalClaimVerificationInputTokens)} in / {Tokens(usage.TotalClaimVerificationOutputTokens)} out");
        sb.AppendLine($"- **Model calls:** {(usage.TotalModelCalls.HasValue ? Int(usage.TotalModelCalls.Value) : "— *not recorded*")} · **Tool calls:** {Int(usage.TotalToolCalls)}");
        if (answerOutcomes != null)
        {
            sb.AppendLine(answerOutcomes.ToolCallsFailed is int failed && answerOutcomes.ToolCallsRefusedByBudget is int refused
                ? $"- **Tool call outcomes:** {Int(failed)} failed, {Int(refused)} refused by the tool budget"
                : $"- **Tool call outcomes:** — *not recorded: {answerOutcomes.ToolCallsUnavailableReason ?? "the per-call rows were not loaded."}*");
        }
        if (usage.ClaimsChecked > 0)
        {
            string sentences = answerOutcomes == null
                ? string.Empty
                : "; refuted answer sentences (accused ones included): "
                  + (answerOutcomes.RefutedAnswerSentences is int refutedSentences ? Int(refutedSentences) : "— *not countable: some verifications record no roles*");
            sb.AppendLine($"- **Claim verification:** {Int(usage.ClaimsChecked)} claims checked — {Int(usage.ClaimsSupported)} supported, **{Int(usage.ClaimsRefuted)} refuted**, {Int(usage.ClaimsIndeterminate)} indeterminate{sentences}");
        }
        sb.AppendLine();

        if (usage.TotalToolCalls > 0 && usage.ToolCallsByFamily.Count > 0)
        {
            sb.AppendLine("| Tool family | Calls | Share |");
            sb.AppendLine("|---|---:|---:|");
            foreach (var kv in usage.ToolCallsByFamily.OrderByDescending(k => k.Value))
            {
                sb.AppendLine($"| {kv.Key} | {Int(kv.Value)} | {Percent(kv.Value / (double)usage.TotalToolCalls)} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Sums over the suites with statistics, every usable member included.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    // --- Comparison ----------------------------------------------------------------------------------

    private static void AppendComparison(
        StringBuilder sb,
        int section,
        BenchmarkBatteryComparison cmp,
        BenchmarkBatteryRun treatment,
        string? baselineLabel)
    {
        sb.AppendLine($"## {section}. Paired Battery Comparison");
        sb.AppendLine();
        sb.AppendLine($"**Baseline:** {baselineLabel ?? "(unnamed battery run)"} — Overall Index {Inv(cmp.BaselineOverallIndex, "F2")}  ");
        sb.AppendLine($"**Treatment:** battery run #{treatment.Id} — Overall Index {Inv(cmp.TreatmentOverallIndex, "F2")}");
        sb.AppendLine();
        sb.AppendLine($"Items are paired by question within each suite on equal item revisions; differences are **treatment minus baseline**. {Int(cmp.PairedItemCount)} item(s) paired. The weights *w*ₛ and *d*_q are the baseline's, which equal the treatment's because both sat the same exams.");
        sb.AppendLine();

        sb.AppendLine($"- **Composite difference *D*:** {Signed(cmp.CompositeDifference)} points" +
                      (cmp.CompositeConfidenceLower.HasValue && cmp.CompositeConfidenceUpper.HasValue
                          ? $" (95 % CI [{Inv(cmp.CompositeConfidenceLower)}, {Inv(cmp.CompositeConfidenceUpper)}])"
                          : string.Empty));
        sb.AppendLine($"- **Standard error:** {Inv(cmp.CompositeStandardError, "F3")} · **ν:** {Inv(cmp.CompositeDegreesOfFreedom, "F1")} · **t:** {Inv(cmp.CompositeCriticalValue, "F3")}");
        string method = cmp.RandomizationMethod switch
        {
            BenchmarkBatteryRandomizationMethod.Exact => "exact enumeration of every sign assignment",
            BenchmarkBatteryRandomizationMethod.MonteCarlo =>
                $"{(cmp.MonteCarloResamples ?? 0).ToString(CultureInfo.InvariantCulture)} Monte Carlo resamples, seed {(cmp.Seed ?? 0).ToString(CultureInfo.InvariantCulture)}, Monte Carlo SE {Inv(cmp.MonteCarloStandardError, "F4")}",
            _ => "not computed"
        };
        sb.AppendLine($"- **Stratified paired sign-flip test (primary):** p = {PValue(cmp.RandomizationPValue)} — {method}.");
        sb.AppendLine();

        sb.AppendLine("| Suite | *w*ₛ | Paired items | Δ*I*ₛ (difficulty-weighted) | SE | Wilcoxon p | Holm-adjusted p | Note |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");
        foreach (var row in cmp.Suites.OrderBy(s => s.SuiteIndex))
        {
            sb.AppendLine(
                $"| {Cell(row.SuiteName)} " +
                $"| {Percent(row.Weight)} " +
                $"| {Int(row.PairedItemCount)} " +
                $"| {Signed(row.WeightedDifference)} " +
                $"| {Inv(row.WeightedDifferenceStandardError, "F3")} " +
                $"| {PValue(row.WilcoxonPValue)} " +
                $"| {PValue(row.HolmAdjustedPValue)} " +
                $"| {Cell(row.Note)} |");
        }
        sb.AppendLine();

        foreach (string note in cmp.Notes)
        {
            sb.AppendLine($"- {note}");
        }
        if (cmp.Notes.Count > 0) sb.AppendLine();

        sb.AppendLine("Comparisons are pairwise and on demand; no all-pairs significance matrix is computed over a leaderboard, so multiplicity cannot manufacture a finding.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    // --- Limits --------------------------------------------------------------------------------------

    private static void AppendLimits(StringBuilder sb, int section, BenchmarkBatteryStatisticsResult result)
    {
        sb.AppendLine($"## {section}. Method and Limits");
        sb.AppendLine();
        sb.AppendLine("- Run-to-run variation mixes candidate and grader stochasticity; nothing in a battery separates the two, exactly as in a single-suite replicate set.");
        sb.AppendLine("- Questions that share one game board are not independent, so a board suite's item-sampling standard error is optimistic — an inherited single-suite limit the battery cannot correct.");
        sb.AppendLine("- The Overall Index generalizes to **this battery's** suites under **these** weights, not to GnollHack knowledge at large.");
        sb.AppendLine();

        if (result.Caveats.Count > 0)
        {
            sb.AppendLine("**Caveats recorded with this result:**");
            sb.AppendLine();
            foreach (string caveat in result.Caveats.Distinct(StringComparer.Ordinal))
            {
                sb.AppendLine($"- {caveat}");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"*Method version {Int(result.MethodVersion)}. Report generated by `BenchmarkBatteryReportBuilder`. No part of it was written by a model.*");
    }

    private static string SuiteName(BenchmarkBatteryDefinition? definition, BenchmarkBatteryStatisticsResult result, int suiteIndex)
        => result.Suites.FirstOrDefault(s => s.SuiteIndex == suiteIndex)?.SuiteName
           ?? definition?.Suites.FirstOrDefault(s => s.Index == suiteIndex)?.SuiteName
           ?? $"#{Int(suiteIndex + 1)}";
}
