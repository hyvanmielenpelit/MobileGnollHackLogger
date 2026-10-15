namespace Overseer.Services.Benchmarking.RootCause;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.ChatConsistency;
using Overseer.Services.Telemetry;

/// <summary>
/// What the root-cause engine reads of one answer: its item, outcome, failure signals, timing from the
/// answer columns and its candidate <see cref="ModelCallTelemetry"/> rows, tokens and cost components.
/// A null field was not recorded; it is never read as zero.
/// </summary>
public sealed class RootCauseAnswerRecord
{
    public long RunId { get; init; }

    /// <summary><see cref="BenchmarkReportFacts.ItemKeyOf"/>: question and revision.</summary>
    public string ItemKey { get; init; } = string.Empty;

    /// <summary>The question without its revision: speed and cost pair on it, as the paired tests do.</summary>
    public string QuestionKey { get; init; } = string.Empty;

    public string Band { get; init; } = string.Empty;

    /// <summary>A battery member's suite number, from 1; null on a run or group.</summary>
    public int? Suite { get; init; }

    public bool Ok { get; init; }

    /// <summary>Counts toward the Intelligence Index.</summary>
    public bool Counting { get; init; }

    /// <summary>The published quality of a counting answer; null when unscored.</summary>
    public double? Quality { get; init; }

    /// <summary>Panel member A's (or the assessor's) score and member B's, for the panel-agreement check.</summary>
    public double? MemberAQuality { get; init; }

    public double? MemberBQuality { get; init; }

    public bool CriticalError { get; init; }
    public bool NoTool { get; init; }
    public bool BudgetOrDefect { get; init; }
    public int RefutedClaims { get; init; }

    /// <summary>The outcome class from scoring method 13; null before it.</summary>
    public BenchmarkOutcomeClass? Outcome { get; init; }

    public double? Accuracy { get; init; }
    public double? Completeness { get; init; }
    public double? Conciseness { get; init; }
    public double? Readability { get; init; }

    public double DurationMs { get; init; }
    public double ModelTimeMs { get; init; }
    public double? ToolTimeMs { get; init; }
    public double? TtftAnswerMs { get; init; }

    /// <summary>Permit and backoff waits plus the candidate calls' failed attempts.</summary>
    public double? OwnWaitMs { get; init; }

    public int? RetryAttempts { get; init; }
    public int? ModelCalls { get; init; }

    /// <summary>The answer carries candidate call telemetry.</summary>
    public bool Telemetry { get; init; }

    /// <summary>Sum over candidate calls of the time to first visible output; null without telemetry.</summary>
    public double? TtftSumMs { get; init; }

    /// <summary>Sum over candidate calls of the time from first output to the end of the stream; null without telemetry.</summary>
    public double? DecodeSumMs { get; init; }

    /// <summary>Each candidate call's time to first visible output.</summary>
    public IReadOnlyList<double> CallTtftMs { get; init; } = Array.Empty<double>();

    /// <summary>The final call's visible decode rate, tokens per second.</summary>
    public double? DecodeRate { get; init; }

    /// <summary>A candidate call was served at another tier than requested.</summary>
    public bool ServedTierMismatch { get; init; }

    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? ReasoningTokens { get; init; }
    public int? CacheReadTokens { get; init; }
    public int? CacheWriteTokens { get; init; }

    /// <summary>Tokens by cost component (<see cref="RootCauseSegments.CostComponents"/> order); null without token counts.</summary>
    public double[]? ComponentTokens { get; init; }

    /// <summary>USD by cost component; null without token counts or a price card.</summary>
    public double[]? ComponentCost { get; init; }
}

/// <summary>The launcher settings the runs of one model share; null where they differ.</summary>
public sealed class RootCauseRunSettings
{
    public long? SuiteId { get; init; }
    public string? SuiteName { get; init; }
    public long? BatteryId { get; init; }
    public string? BatteryName { get; init; }
    public long? TestedModelConfigurationId { get; init; }
    public long? ScoringProfileId { get; init; }
    public bool? VerboseMode { get; init; }
    public bool? AllowSourceCodeReferences { get; init; }
    public long? AssessorModelConfigurationId { get; init; }
    public long? CoAssessorModelConfigurationId { get; init; }
    public long? SecondOpinionAssessorModelConfigurationId { get; init; }
    public int? SecondOpinionMode { get; init; }
    public long? ClaimVerifierModelConfigurationId { get; init; }

    /// <summary>Settings whose value differs between the runs, by launcher field name.</summary>
    public IReadOnlyList<string> Differing { get; init; } = Array.Empty<string>();
}

/// <summary>One question's figures for a model, means over its runs.</summary>
public sealed class RootCauseItem
{
    public string Key { get; init; } = string.Empty;
    public string Band { get; init; } = string.Empty;
    public int? Suite { get; init; }

    // Speed: means over the answers with candidate telemetry.
    public double ModelTimeMs { get; init; }
    public double DurationMs { get; init; }
    public double TtftMs { get; init; }
    public double DecodeMs { get; init; }
    public double ToolMs { get; init; }
    public double WaitMs { get; init; }
    public double Calls { get; init; }
    public double OutputTokens { get; init; }
    public double? ReasoningTokens { get; init; }

    // Cost: means over the answers with token counts and a price card.
    public double[] Tokens { get; init; } = new double[5];
    public double[] Cost { get; init; } = new double[5];
    public double TotalCost => Cost.Sum();
}

/// <summary>Everything the engine reads of one model's runs.</summary>
public sealed class RootCauseModelEvidence
{
    public IReadOnlyList<RootCauseAnswerRecord> Answers { get; init; } = Array.Empty<RootCauseAnswerRecord>();

    public int RunCount { get; init; }

    /// <summary>Fewest runs of any suite (for a run or group, the run count).</summary>
    public int RunsPerSuite { get; init; }

    /// <summary>Every run was graded by a panel.</summary>
    public bool Panel { get; init; }

    /// <summary>Every run was scored under method 13 or later, so critical errors are confirmed.</summary>
    public bool CriticalErrorsConfirmed { get; init; }

    /// <summary>Every run has a price card on the basis.</summary>
    public bool Priced { get; init; }

    /// <summary>The dimension weights of the lowest-id run: accuracy, completeness, conciseness, readability.</summary>
    public double[] Weights { get; init; } = { 0.25, 0.25, 0.25, 0.25 };

    /// <summary>The nominal candidate prices of the lowest-id run's card, USD per million tokens, by cost component; null where the card has none.</summary>
    public double?[] PricesPerMillion { get; init; } = new double?[5];

    /// <summary>The card's prices differ between the runs.</summary>
    public bool PricesDiffer { get; init; }

    public string? ServedTier { get; init; }
    public string? RequestedTier { get; init; }
    public string? ReasoningMode { get; init; }
    public string? ParallelMode { get; init; }
    public int? MaxOutputTokens { get; init; }

    /// <summary>The panel's disagreements and the answers both members graded, summed over runs; null for a single-assessor model.</summary>
    public int? PanelDisagreements { get; init; }

    public int? PanelAnswers { get; init; }

    /// <summary>The highest CallTelemetryVersion of the runs; null when none recorded call telemetry.</summary>
    public int? CallTelemetryVersion { get; init; }

    public double? MeanMinutesPerRun { get; init; }

    public RootCauseRunSettings Settings { get; init; } = new();

    /// <summary>The per-question speed figures, by question key.</summary>
    public IReadOnlyList<RootCauseItem> SpeedItems { get; init; } = Array.Empty<RootCauseItem>();

    /// <summary>The per-question cost figures, by question key.</summary>
    public IReadOnlyList<RootCauseItem> CostItems { get; init; } = Array.Empty<RootCauseItem>();

    public IEnumerable<RootCauseAnswerRecord> Scored => Answers.Where(a => a.Counting && a.Quality.HasValue);
}

/// <summary>
/// Builds the per-model and per-question timing, token, price and configuration aggregates the
/// root-cause engine reads, from answers and their candidate <see cref="ModelCallTelemetry"/> rows, and
/// states them as the unprefixed per-model facts of § A3. Pure: the telemetry and the price cards are
/// loaded by the caller.
///
/// Fact keys (unprefixed on a per-model sheet, <c>model.&lt;L&gt;.*</c> on a comparison-scope sheet):
///   speed.ttftP90, .toolTimeP50, .modelCallsPerAnswer, .ttftPerCallP50, .decodeRate, .ownWaitShare,
///   .retriesPerAnswer, .servedTierMismatch
///   tokens.reasoningPerQuestion, .reasoningShare, .cacheReadShare
///   cost.component.&lt;c&gt;.perQuestion           c = uncachedInput, cacheRead, cacheWrite, output, reasoning
///   price.&lt;c&gt;.perMillion                      the card's nominal price
///   servedTier
///   score.withTools, score.withoutTools
///   outcome.confirmedCriticalRate                 with its Wilson interval in the display
///   config.serviceTier, .reasoningMode, .parallelMode, .maxOutputTokens
/// </summary>
public static class RootCauseEvidence
{
    /// <summary>The termination reasons of an answer that ran into the tool or iteration limit.</summary>
    private static readonly string[] LimitTerminations = { "budget_exhausted", "iteration_limit" };

    private const BenchmarkAnswerFlags DefectFlags = BenchmarkAnswerFlags.Empty | BenchmarkAnswerFlags.Truncated
        | BenchmarkAnswerFlags.HarnessArtifacts | BenchmarkAnswerFlags.ReasoningBleed | BenchmarkAnswerFlags.RepeatedFragments;

    public const string NoTelemetryReason = "No answer of this model recorded candidate call telemetry, so its time cannot be split.";
    public const string NoTokensReason = "No answer of this model recorded its token counts.";
    public const string NoPriceReason = "No price card was resolved for every run behind this model.";

    /// <summary>
    /// The evidence of one model over <paramref name="runs"/>, which carry their answers, their tool calls
    /// and their candidate telemetry rows where loaded. <paramref name="pricing"/> holds each run's
    /// candidate card on the comparison's basis; <paramref name="suiteOfRun"/> a battery member's suite
    /// number; <paramref name="batteryId"/> and <paramref name="batteryName"/> the battery a battery subject ran.
    /// </summary>
    public static RootCauseModelEvidence Build(
        IReadOnlyList<BenchmarkRun> runs,
        IReadOnlyDictionary<long, ModelPricing?>? pricing,
        IReadOnlyDictionary<long, int>? suiteOfRun = null,
        long? batteryId = null,
        string? batteryName = null)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var ordered = runs.Where(r => r != null).OrderBy(r => r.Id).ToList();
        var records = new List<RootCauseAnswerRecord>();
        bool priced = ordered.Count > 0;

        foreach (var run in ordered)
        {
            bool panel = BenchmarkRunFinalizer.IsPanelRun(run);
            ModelPricing? card = null;
            if (pricing == null || !pricing.TryGetValue(run.Id, out card) || card == null) priced = false;
            string? servedTier = BenchmarkRunFinalizer.ResolveServedServiceTier(run.Answers ?? new List<BenchmarkRunAnswer>());
            int? suite = suiteOfRun != null && suiteOfRun.TryGetValue(run.Id, out int s) ? s : null;

            foreach (var answer in (run.Answers ?? new List<BenchmarkRunAnswer>()).OrderBy(a => a.OrderIndex).ThenBy(a => a.Id))
            {
                records.Add(Record(run, answer, panel, card, servedTier, suite));
            }
        }

        var first = ordered.FirstOrDefault();
        var cardFirst = first != null && pricing != null && pricing.TryGetValue(first.Id, out var c0) ? c0 : null;
        var prices = PricesOf(cardFirst);
        bool pricesDiffer = ordered.Skip(1).Any(r =>
            pricing != null && pricing.TryGetValue(r.Id, out var c) && c != null && !PricesOf(c).SequenceEqual(prices));

        bool allPanel = ordered.Count > 0 && ordered.All(BenchmarkRunFinalizer.IsPanelRun);
        int runsPerSuite = suiteOfRun == null
            ? ordered.Count
            : ordered.GroupBy(r => suiteOfRun.TryGetValue(r.Id, out int n) ? n : 0).Select(g => g.Count()).DefaultIfEmpty(0).Min();

        var minutes = ordered
            .Where(r => r.CompletedAtUtc.HasValue && r.CompletedAtUtc.Value > r.StartedAtUtc)
            .Select(r => (r.CompletedAtUtc!.Value - r.StartedAtUtc).TotalMinutes)
            .ToList();

        return new RootCauseModelEvidence
        {
            Answers = records,
            RunCount = ordered.Count,
            RunsPerSuite = runsPerSuite,
            Panel = allPanel,
            CriticalErrorsConfirmed = ordered.Count > 0 && ordered.All(BenchmarkCriticalErrorResolver.Applies),
            Priced = priced,
            Weights = WeightsOf(first),
            PricesPerMillion = prices,
            PricesDiffer = pricesDiffer,
            ServedTier = BenchmarkRunFinalizer.ResolveServedServiceTier(ordered.SelectMany(r => r.Answers ?? new List<BenchmarkRunAnswer>())),
            RequestedTier = first?.TestedModelSnapshot?.ServiceTier,
            ReasoningMode = first?.TestedModelSnapshot?.ReasoningMode,
            ParallelMode = first?.TestedModelSnapshot?.ParallelExecutionMode.ToString(),
            MaxOutputTokens = first?.TestedModelSnapshot?.MaxOutputTokens,
            PanelDisagreements = allPanel ? ordered.Sum(r => r.PanelDisagreementCount ?? 0) : null,
            PanelAnswers = allPanel ? ordered.Sum(r => r.PanelGradedAnswerCount ?? 0) : null,
            CallTelemetryVersion = ordered.Select(r => r.CallTelemetryVersion).Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(-1).Max() is int v2 && v2 >= 0 ? v2 : null,
            MeanMinutesPerRun = minutes.Count > 0 ? minutes.Average() : null,
            Settings = SettingsOf(ordered, batteryId, batteryName),
            SpeedItems = SpeedItemsOf(records),
            CostItems = CostItemsOf(records)
        };
    }

    private static RootCauseAnswerRecord Record(
        BenchmarkRun run, BenchmarkRunAnswer answer, bool panel, ModelPricing? card, string? servedTier, int? suite)
    {
        bool counting = BenchmarkRunFinalizer.CountsTowardQualityIndex(answer);
        bool ok = answer.Status == BenchmarkAnswerStatus.Ok;
        var flags = (BenchmarkAnswerFlags)answer.AnswerFlags;
        int toolCalls = answer.ToolCallCount ?? BenchmarkChatTransfer.ToolCallCountsFor(answer).Values.Sum();

        var calls = CallTelemetryMeasures.CandidateCalls(answer);
        bool telemetry = calls.Count > 0;
        double? ttftSum = null, decodeSum = null;
        var callTtft = new List<double>();
        double failedAttempts = 0;
        if (telemetry)
        {
            double ttft = 0, decode = 0;
            bool complete = true;
            foreach (var call in calls)
            {
                failedAttempts += call.FailedAttemptMs;
                int? firstOutput = call.FirstOutputMs ?? call.FirstEventMs;
                int? end = call.StreamEndMs ?? call.CompletedMs ?? call.LastDeltaMs;
                if (firstOutput is not int f || end is not int e)
                {
                    complete = false;
                    continue;
                }
                callTtft.Add(f);
                ttft += f;
                decode += Math.Max(0, e - f);
            }
            if (complete)
            {
                ttftSum = ttft;
                decodeSum = decode;
            }
        }

        double? ownWait = answer.PermitWaitMs.HasValue || answer.BackoffWaitMs.HasValue || telemetry
            ? (answer.PermitWaitMs ?? 0) + (answer.BackoffWaitMs ?? 0) + failedAttempts
            : null;

        double[]? componentTokens = null;
        double[]? componentCost = null;
        if (answer.InputTokens.HasValue || answer.OutputTokens.HasValue)
        {
            double input = answer.InputTokens ?? 0;
            double cacheRead = answer.CacheReadInputTokens ?? 0;
            double cacheWrite = answer.CacheCreationInputTokens ?? 0;
            double output = answer.OutputTokens ?? 0;
            double reasoning = Math.Clamp((double)(answer.ReasoningTokens ?? 0), 0, output);
            componentTokens = new[] { Math.Max(0, input - cacheRead - cacheWrite), cacheRead, cacheWrite, output - reasoning, reasoning };

            if (card != null)
            {
                var breakdown = ModelPricingService.ComputeCostBreakdownFromTotals(
                    card,
                    answer.InputTokens ?? 0, answer.OutputTokens ?? 0,
                    answer.CacheReadInputTokens ?? 0, answer.CacheCreationInputTokens ?? 0,
                    answer.LongContextInputTokens ?? 0, answer.LongContextOutputTokens ?? 0,
                    answer.LongContextCacheReadTokens ?? 0, answer.LongContextCacheCreationTokens ?? 0,
                    actualServiceTier: servedTier,
                    requestedServiceTier: run.TestedModelSnapshot?.ServiceTier);
                double outputCost = (double)breakdown.Output;
                double reasoningCost = output > 0 ? outputCost * reasoning / output : 0;
                componentCost = new[]
                {
                    (double)breakdown.UncachedInput, (double)breakdown.CacheRead, (double)breakdown.CacheWrite,
                    outputCost - reasoningCost, reasoningCost
                };
            }
        }

        double? quality = counting ? BenchmarkScoring.IndexQuality(answer, panel) : null;

        return new RootCauseAnswerRecord
        {
            RunId = run.Id,
            ItemKey = BenchmarkReportFacts.ItemKeyOf(answer),
            QuestionKey = BenchmarkReportFacts.QuestionKeyOf(answer),
            Band = BenchmarkReportFacts.BandNameOf(answer),
            Suite = suite,
            Ok = ok,
            Counting = counting,
            Quality = quality,
            MemberAQuality = counting ? answer.QualityScore : null,
            MemberBQuality = counting && panel ? answer.CoAssessmentQualityScore : null,
            CriticalError = counting && (BenchmarkCriticalErrorResolver.Applies(run)
                ? BenchmarkCriticalErrorResolver.IsConfirmed(answer)
                : answer.CriticalError || answer.CoAssessmentCriticalError == true),
            NoTool = toolCalls == 0,
            BudgetOrDefect = answer.ToolBudgetExhausted
                || LimitTerminations.Contains((answer.TerminationReason ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                || (flags & DefectFlags) != 0
                || !ok,
            RefutedClaims = answer.ClaimsRefutedCount ?? 0,
            Outcome = counting && BenchmarkCriticalErrorResolver.Applies(run) ? BenchmarkOutcomeSummary.Classify(run, answer, panel) : null,
            Accuracy = answer.AccuracyScore,
            Completeness = answer.CompletenessScore,
            Conciseness = answer.ConcisenessScore,
            Readability = answer.ReadabilityScore,
            DurationMs = answer.DurationMs,
            ModelTimeMs = answer.ModelTimeMs,
            ToolTimeMs = answer.ToolTimeMs,
            TtftAnswerMs = answer.TimeToFirstTokenMs,
            OwnWaitMs = ownWait,
            RetryAttempts = answer.RetryAttemptCount,
            ModelCalls = answer.ModelCallCount ?? (telemetry ? (int?)calls.Count : null),
            Telemetry = telemetry,
            TtftSumMs = ttftSum,
            DecodeSumMs = decodeSum,
            CallTtftMs = callTtft,
            DecodeRate = CallTelemetryMeasures.AnswerStreamingRate(answer)?.TokensPerSecond,
            ServedTierMismatch = !ChatConsistencyMeasures.AnswerTierMatches(answer, run),
            InputTokens = answer.InputTokens,
            OutputTokens = answer.OutputTokens,
            ReasoningTokens = answer.ReasoningTokens,
            CacheReadTokens = answer.CacheReadInputTokens,
            CacheWriteTokens = answer.CacheCreationInputTokens,
            ComponentTokens = componentTokens,
            ComponentCost = componentCost
        };
    }

    /// <summary>Per question, the means over the Ok answers whose candidate calls all recorded their marks.</summary>
    private static List<RootCauseItem> SpeedItemsOf(IReadOnlyList<RootCauseAnswerRecord> records)
        => records
            .Where(r => r.Ok && r.TtftSumMs.HasValue && r.DecodeSumMs.HasValue)
            .GroupBy(r => r.QuestionKey, StringComparer.Ordinal)
            .Select(g =>
            {
                var list = g.ToList();
                var reasoning = list.Where(r => r.ReasoningTokens.HasValue).Select(r => (double)r.ReasoningTokens!.Value).ToList();
                return new RootCauseItem
                {
                    Key = g.Key,
                    Band = list[0].Band,
                    Suite = list[0].Suite,
                    ModelTimeMs = list.Average(r => r.ModelTimeMs),
                    DurationMs = list.Average(r => r.DurationMs),
                    TtftMs = list.Average(r => r.TtftSumMs!.Value),
                    DecodeMs = list.Average(r => r.DecodeSumMs!.Value),
                    ToolMs = list.Average(r => r.ToolTimeMs ?? 0),
                    WaitMs = list.Average(r => r.OwnWaitMs ?? 0),
                    Calls = list.Average(r => (double)(r.ModelCalls ?? 0)),
                    OutputTokens = list.Average(r => (double)(r.OutputTokens ?? 0)),
                    ReasoningTokens = reasoning.Count == list.Count ? reasoning.Average() : null
                };
            })
            .OrderBy(i => i.Key, StringComparer.Ordinal)
            .ToList();

    /// <summary>Per question, the means over the answer rows with token counts and a price card.</summary>
    private static List<RootCauseItem> CostItemsOf(IReadOnlyList<RootCauseAnswerRecord> records)
        => records
            .Where(r => r.ComponentTokens != null && r.ComponentCost != null)
            .GroupBy(r => r.QuestionKey, StringComparer.Ordinal)
            .Select(g =>
            {
                var list = g.ToList();
                var tokens = new double[5];
                var cost = new double[5];
                for (int k = 0; k < 5; k++)
                {
                    tokens[k] = list.Average(r => r.ComponentTokens![k]);
                    cost[k] = list.Average(r => r.ComponentCost![k]);
                }
                return new RootCauseItem
                {
                    Key = g.Key,
                    Band = list[0].Band,
                    Suite = list[0].Suite,
                    Tokens = tokens,
                    Cost = cost,
                    Calls = list.Average(r => (double)(r.ModelCalls ?? 0))
                };
            })
            .OrderBy(i => i.Key, StringComparer.Ordinal)
            .ToList();

    /// <summary>The nominal prices of a card, USD per million, by cost component.</summary>
    private static double?[] PricesOf(ModelPricing? card)
    {
        if (card == null) return new double?[5];
        double input = (double)card.InputPerMillion;
        double output = (double)card.OutputPerMillion;
        return new double?[]
        {
            input,
            (double)(card.CachedInputPerMillion ?? card.InputPerMillion),
            card.CacheWritePerMillion is decimal write ? (double)write : null,
            output,
            output
        };
    }

    /// <summary>The weights from the run's scoring-profile snapshot, else the defaults.</summary>
    private static double[] WeightsOf(BenchmarkRun? run)
    {
        var defaults = BenchmarkScoringConstants.Default;
        var weights = new[] { defaults.WeightAccuracy, defaults.WeightCompleteness, defaults.WeightConciseness, defaults.WeightReadability };
        if (run == null || string.IsNullOrWhiteSpace(run.ScoringProfileSnapshotJson)) return weights;

        try
        {
            using var doc = JsonDocument.Parse(run.ScoringProfileSnapshotJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return weights;
            string[] names = { "WeightAccuracy", "WeightCompleteness", "WeightConciseness", "WeightReadability" };
            for (int i = 0; i < names.Length; i++)
            {
                if (root.TryGetProperty(names[i], out var p) && p.TryGetDouble(out double v)) weights[i] = v;
            }
        }
        catch (JsonException)
        {
            // An unreadable snapshot reads as the defaults.
        }
        return weights;
    }

    /// <summary>The settings every run shares, null where they differ, with the differing fields named.</summary>
    public static RootCauseRunSettings SettingsOf(IReadOnlyList<BenchmarkRun> runs, long? batteryId = null, string? batteryName = null)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var differing = new List<string>();

        T? Shared<T>(string field, Func<BenchmarkRun, T?> read) where T : struct
        {
            var values = runs.Select(read).Distinct().ToList();
            if (values.Count == 1) return values[0];
            if (values.Count > 1) differing.Add(field);
            return null;
        }

        string? SharedText(string field, Func<BenchmarkRun, string?> read)
        {
            var values = runs.Select(read).Distinct(StringComparer.Ordinal).ToList();
            if (values.Count == 1) return values[0];
            if (values.Count > 1) differing.Add(field);
            return null;
        }

        var options = runs.Select(r => BenchmarkCandidatePromptOptions.FromJson(r.CandidatePromptOptionsJson)).ToList();
        bool? verbose = options.Select(o => o.VerboseMode).Distinct().Count() == 1 && options.Count > 0 ? options[0].VerboseMode : null;
        if (verbose == null && options.Count > 0) differing.Add("VerboseMode");
        bool? source = options.Select(o => o.AllowSourceCodeReferences).Distinct().Count() == 1 && options.Count > 0 ? options[0].AllowSourceCodeReferences : null;
        if (source == null && options.Count > 0) differing.Add("AllowSourceCodeReferences");

        long? suiteId = batteryId.HasValue ? null : Shared("SuiteId", r => r.BenchmarkSuiteId ?? r.BenchmarkSuiteIdUsed);
        string? suiteName = batteryId.HasValue ? null : SharedText("SuiteName", r => r.SuiteName);

        return new RootCauseRunSettings
        {
            SuiteId = suiteId,
            SuiteName = suiteName,
            BatteryId = batteryId,
            BatteryName = batteryName,
            TestedModelConfigurationId = Shared("TestedModelConfigurationId", r => r.TestedModelConfigurationId),
            ScoringProfileId = Shared("ScoringProfileId", r => r.ScoringProfileId),
            VerboseMode = verbose,
            AllowSourceCodeReferences = source,
            AssessorModelConfigurationId = Shared("AssessorModelConfigurationId", r => r.AssessorModelConfigurationId),
            CoAssessorModelConfigurationId = Shared("CoAssessorModelConfigurationId", r => r.CoAssessorModelConfigurationId),
            SecondOpinionAssessorModelConfigurationId = Shared("SecondOpinionAssessorModelConfigurationId", r => r.SecondOpinionAssessorModelConfigurationId),
            SecondOpinionMode = Shared("SecondOpinionMode", r => (int?)r.SecondOpinionModeUsed),
            ClaimVerifierModelConfigurationId = Shared("ClaimVerifierModelConfigurationId", r => r.ClaimVerifierModelConfigurationId),
            Differing = differing
        };
    }

    // ---------------------------------------------------------------------------------------------
    // The per-model facts of § A3
    // ---------------------------------------------------------------------------------------------

    /// <summary>The keys <see cref="AddFacts"/> adds, without a model prefix.</summary>
    public static readonly IReadOnlyList<string> FactKeys = new[]
    {
        "speed.ttftP90", "speed.toolTimeP50", "speed.modelCallsPerAnswer", "speed.ttftPerCallP50", "speed.decodeRate",
        "speed.ownWaitShare", "speed.retriesPerAnswer", "speed.servedTierMismatch",
        "tokens.reasoningPerQuestion", "tokens.reasoningShare", "tokens.cacheReadShare",
        "cost.component.uncachedInput.perQuestion", "cost.component.cacheRead.perQuestion", "cost.component.cacheWrite.perQuestion",
        "cost.component.output.perQuestion", "cost.component.reasoning.perQuestion",
        "price.uncachedInput.perMillion", "price.cacheRead.perMillion", "price.cacheWrite.perMillion",
        "price.output.perMillion", "price.reasoning.perMillion",
        "servedTier", "score.withTools", "score.withoutTools", "outcome.confirmedCriticalRate",
        "config.serviceTier", "config.reasoningMode", "config.parallelMode", "config.maxOutputTokens"
    };

    /// <summary>
    /// The per-model facts of § A3 for <paramref name="evidence"/>, unprefixed. A figure whose source
    /// columns are missing is unavailable with the reason, never zero; a model degraded on an axis has
    /// that axis' figures unavailable with <paramref name="speedDegradedReason"/> or <paramref name="costDegradedReason"/>.
    /// </summary>
    internal static void AddFacts(
        BenchmarkReportFacts.FactList facts,
        RootCauseModelEvidence evidence,
        string? speedDegradedReason = null,
        string? costDegradedReason = null,
        string pricingBasisKind = BenchmarkReportFacts.PricingBasisSnapshot)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(evidence);
        var ok = evidence.Answers.Where(a => a.Ok).ToList();

        void Speed(string key, Func<double?> value, Func<double, string> display, string reason)
        {
            if (speedDegradedReason != null) facts.Unavailable(key, speedDegradedReason);
            else if (value() is double v) facts.Add(key, v, display(v));
            else facts.Unavailable(key, reason);
        }

        Speed("speed.ttftP90",
            () => BenchmarkGroupStatistics.Percentile(ok.Where(a => a.TtftAnswerMs.HasValue).Select(a => a.TtftAnswerMs!.Value).ToList(), 90),
            BenchmarkReportFormat.Seconds, "No answer recorded a time to first token.");
        Speed("speed.toolTimeP50",
            () => RootCauseStatistics.Median(ok.Where(a => a.ToolTimeMs.HasValue).Select(a => a.ToolTimeMs!.Value).ToList()),
            BenchmarkReportFormat.Seconds, "No answer recorded its tool time.");
        Speed("speed.modelCallsPerAnswer",
            () => ok.Any(a => a.ModelCalls.HasValue) ? ok.Where(a => a.ModelCalls.HasValue).Average(a => (double)a.ModelCalls!.Value) : null,
            BenchmarkReportFormat.OneDecimal, "No answer recorded its model calls.");
        Speed("speed.ttftPerCallP50",
            () => RootCauseStatistics.Median(ok.SelectMany(a => a.CallTtftMs).ToList()),
            BenchmarkReportFormat.Seconds, NoTelemetryReason);
        Speed("speed.decodeRate",
            () => RootCauseStatistics.Median(ok.Where(a => a.DecodeRate.HasValue).Select(a => a.DecodeRate!.Value).ToList()),
            v => BenchmarkReportFormat.Whole(v) + " visible tokens per second", "No answer's final call recorded a measurable decode rate.");
        Speed("speed.ownWaitShare",
            () => OwnWaitShare(evidence),
            v => BenchmarkReportFormat.Percent(v * 100) + " of model time", NoTelemetryReason);
        Speed("speed.retriesPerAnswer",
            () => ok.Any(a => a.RetryAttempts.HasValue) ? ok.Where(a => a.RetryAttempts.HasValue).Average(a => (double)a.RetryAttempts!.Value) : null,
            v => BenchmarkReportFormat.OneDecimal(v) + " retried attempts per answer", "No answer recorded its retried attempts.");

        if (speedDegradedReason != null)
        {
            facts.Unavailable("speed.servedTierMismatch", speedDegradedReason);
        }
        else
        {
            int mismatched = ok.Count(a => a.ServedTierMismatch);
            facts.Add("speed.servedTierMismatch", mismatched,
                mismatched == 0 ? "every answer served at the requested tier"
                    : Inv(mismatched) + " of " + Inv(ok.Count) + (ok.Count == 1 ? " answer" : " answers") + " served at another tier than requested");
        }

        var withReasoning = evidence.Answers.Where(a => a.ReasoningTokens.HasValue && a.OutputTokens.HasValue).ToList();
        if (withReasoning.Count == 0)
        {
            facts.Unavailable("tokens.reasoningPerQuestion", "No answer recorded its reasoning tokens separately.");
            facts.Unavailable("tokens.reasoningShare", "No answer recorded its reasoning tokens separately.");
        }
        else
        {
            double perQuestion = withReasoning.Average(a => (double)a.ReasoningTokens!.Value);
            facts.Add("tokens.reasoningPerQuestion", perQuestion, BenchmarkReportFormat.Count(perQuestion));
            double output = withReasoning.Sum(a => (double)a.OutputTokens!.Value);
            if (output > 0)
            {
                double share = withReasoning.Sum(a => (double)a.ReasoningTokens!.Value) / output;
                facts.Add("tokens.reasoningShare", share, BenchmarkReportFormat.Percent(share * 100) + " of output tokens");
            }
            else
            {
                facts.Unavailable("tokens.reasoningShare", "The answers recorded no output tokens.");
            }
        }

        var withInput = evidence.Answers.Where(a => a.InputTokens is > 0).ToList();
        if (withInput.Count == 0)
        {
            facts.Unavailable("tokens.cacheReadShare", NoTokensReason);
        }
        else
        {
            double share = withInput.Sum(a => (double)(a.CacheReadTokens ?? 0)) / withInput.Sum(a => (double)a.InputTokens!.Value);
            facts.Add("tokens.cacheReadShare", share, BenchmarkReportFormat.Percent(share * 100) + " of input tokens read from the cache");
        }

        var costed = evidence.Answers.Where(a => a.ComponentCost != null).ToList();
        for (int k = 0; k < 5; k++)
        {
            string component = RootCauseSegments.CostComponents[k];
            string key = "cost.component." + component + ".perQuestion";
            if (costDegradedReason != null) facts.Unavailable(key, costDegradedReason);
            else if (!evidence.Priced) facts.Unavailable(key, NoPriceReason);
            else if (costed.Count == 0) facts.Unavailable(key, NoTokensReason);
            else
            {
                double mean = costed.Average(a => a.ComponentCost![k]);
                facts.Add(key, mean, BenchmarkReportFormat.Usd(mean));
            }

            string priceKey = "price." + component + ".perMillion";
            if (evidence.PricesPerMillion[k] is double price)
            {
                string basis = pricingBasisKind == BenchmarkReportFacts.PricingBasisCatalog ? "today's catalog" : "the run's stored card";
                facts.Add(priceKey, price, "$" + price.ToString("0.00##", CultureInfo.InvariantCulture) + " per million tokens ("
                    + basis + (evidence.PricesDiffer ? "; the runs' cards differ, the lowest-id run's shown" : string.Empty) + ")");
            }
            else
            {
                facts.Unavailable(priceKey, evidence.Priced || evidence.PricesPerMillion.Any(p => p.HasValue)
                    ? "The price card publishes no price for this component."
                    : NoPriceReason);
            }
        }

        if (string.IsNullOrWhiteSpace(evidence.ServedTier)) facts.Unavailable("servedTier", "No answer recorded the tier it was served at.");
        else facts.Add("servedTier", evidence.ServedTier, evidence.ServedTier);

        var scored = evidence.Scored.ToList();
        var withTools = scored.Where(a => !a.NoTool).Select(a => a.Quality!.Value).ToList();
        var withoutTools = scored.Where(a => a.NoTool).Select(a => a.Quality!.Value).ToList();
        if (withTools.Count > 0) facts.Add("score.withTools", withTools.Average(), BenchmarkReportFormat.Whole(withTools.Average()) + " (" + Answers(withTools.Count) + ")");
        else facts.Unavailable("score.withTools", "No scored answer made a tool call.");
        if (withoutTools.Count > 0) facts.Add("score.withoutTools", withoutTools.Average(), BenchmarkReportFormat.Whole(withoutTools.Average()) + " (" + Answers(withoutTools.Count) + ")");
        else facts.Unavailable("score.withoutTools", "Every scored answer made a tool call.");

        var counting = evidence.Answers.Where(a => a.Counting).ToList();
        if (counting.Count == 0)
        {
            facts.Unavailable("outcome.confirmedCriticalRate", "No answer counts toward the index.");
        }
        else
        {
            int critical = counting.Count(a => a.CriticalError);
            double rate = critical / (double)counting.Count;
            var wilson = RootCauseStatistics.Wilson(critical, counting.Count);
            string interval = wilson is { } w
                ? " (95 % interval " + BenchmarkReportFormat.Percent(w.Low * 100) + "–" + BenchmarkReportFormat.Percent(w.High * 100) + ")"
                : string.Empty;
            facts.Add("outcome.confirmedCriticalRate", rate,
                BenchmarkReportFormat.Percent(rate * 100) + interval
                + (evidence.CriticalErrorsConfirmed ? string.Empty : "; before scoring method 13, flagged rather than confirmed"));
        }

        Text(facts, "config.serviceTier", evidence.RequestedTier, "not set");
        Text(facts, "config.reasoningMode", evidence.ReasoningMode, "not set");
        Text(facts, "config.parallelMode", evidence.ParallelMode, "not recorded");
        if (evidence.MaxOutputTokens is int max) facts.Add("config.maxOutputTokens", max, BenchmarkReportFormat.Count(max) + " tokens");
        else facts.Text("config.maxOutputTokens", "not set");
    }

    /// <summary>Own waits and retries ÷ model time over the answers with candidate telemetry; null without any.</summary>
    public static double? OwnWaitShare(RootCauseModelEvidence evidence)
    {
        var timed = evidence.Answers.Where(a => a.Ok && a.Telemetry).ToList();
        double model = timed.Sum(a => a.ModelTimeMs);
        return timed.Count == 0 || model <= 0 ? null : timed.Sum(a => a.OwnWaitMs ?? 0) / model;
    }

    // ---------------------------------------------------------------------------------------------
    // Engine inputs
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// One model's engine input from its comparison entry, its runs' evidence and its sheet figures: the
    /// dimension means (accuracy to readability), the response-style conflict and, for a battery result,
    /// the advisory verification-cleared Overall Index.
    /// </summary>
    public static RootCauseModelInput ModelInput(
        BenchmarkModelComparisonEntryDto entry,
        string token,
        string? letter,
        bool isSubject,
        IReadOnlyList<BenchmarkRun> runs,
        RootCauseModelEvidence evidence,
        IReadOnlyList<double?> dimensions,
        bool styleConflict,
        BenchmarkModelComparisonDto comparison,
        double? verificationClearedIndex = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(comparison);
        var panel = comparison.PanelDiagnostics is { Applicable: true } diagnostics
            ? diagnostics.Entries.FirstOrDefault(e => string.Equals(e.EntryKey, entry.Key, StringComparison.Ordinal))
            : null;

        return new RootCauseModelInput
        {
            Token = token,
            Letter = letter,
            Label = entry.Label,
            Provider = entry.Provider,
            ModelId = entry.ModelId,
            ThinkingLevel = entry.ThinkingLevel,
            ReasoningMode = entry.ReasoningMode,
            ServiceTier = entry.ServiceTier,
            MaxOutputTokens = entry.MaxOutputTokens,
            ParallelMode = entry.ParallelExecutionMode,
            HarnessVersion = BenchmarkReportFacts.EntryHarnessVersion(runs),
            EntryKey = entry.Key,
            IsSubject = isSubject,
            RunIds = entry.RunIds.OrderBy(id => id).ToList(),
            QualityIndex = entry.Quality?.PointEstimate,
            QualityLower = entry.Quality?.IntervalLower,
            QualityUpper = entry.Quality?.IntervalUpper,
            Dimensions = dimensions,
            SpeedDegraded = entry.SpeedDegraded,
            SpeedDegradedReason = entry.SpeedDegraded ? entry.Explanation : null,
            CostDegraded = entry.CostDegraded,
            CostDegradedReason = entry.CostDegraded ? entry.Explanation : null,
            ModelTimeP50Ms = entry.SpeedDegraded ? null : entry.Speed?.ModelTimeP50Ms,
            CostPerQuestionUsd = entry.CostDegraded ? null : entry.Cost?.CandidateCostPerQuestionUsd,
            CostPerRunUsd = entry.Cost?.TotalRunCostPerRunUsd ?? entry.Cost?.CandidateCostPerRunUsd,
            StyleConflict = styleConflict,
            DifferenceKeys = entry.Differences.Select(d => d.Name).Distinct(StringComparer.Ordinal).ToList(),
            VerificationClearedIndex = verificationClearedIndex,
            MemberAIndex = panel?.MemberAIndex,
            MemberBIndex = panel?.MemberBIndex,
            Evidence = evidence
        };
    }

    /// <summary>The comparison's judge-dependent pairs between models of <paramref name="tokenOf"/> (entry key → token).</summary>
    public static IReadOnlyList<RootCausePair> JudgeDependentPairs(BenchmarkModelComparisonDto comparison, IReadOnlyDictionary<string, string> tokenOf)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        if (comparison.PanelDiagnostics is not { Applicable: true } diagnostics) return Array.Empty<RootCausePair>();
        return diagnostics.JudgeDependentPairs
            .Where(p => tokenOf.ContainsKey(p.FirstEntryKey) && tokenOf.ContainsKey(p.SecondEntryKey))
            .Select(p => new RootCausePair(tokenOf[p.FirstEntryKey], tokenOf[p.SecondEntryKey]))
            .ToList();
    }

    /// <summary>The sheet's questions keyed as the evidence keys their answers, with their references.</summary>
    public static IReadOnlyList<RootCauseQuestionRef> QuestionRefs(IEnumerable<BenchmarkReportQuestion> questions)
        => questions
            .Select(q => new RootCauseQuestionRef
            {
                Number = q.Number,
                Reference = string.IsNullOrWhiteSpace(q.Reference) ? "Q" + Inv(q.Number) : q.Reference!,
                ItemKey = long.TryParse(q.QuestionKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)
                    ? RootCauseControls.ItemKey(id, q.ItemRevisionUsed)
                    : "o" + Inv(q.OrderIndex) + "@" + (q.ItemRevisionUsed?.ToString(CultureInfo.InvariantCulture) ?? "?")
            })
            .ToList();

    private static void Text(BenchmarkReportFacts.FactList facts, string key, string? value, string empty)
    {
        if (string.IsNullOrWhiteSpace(value)) facts.Text(key, empty);
        else facts.Add(key, value, value);
    }

    private static string Answers(int n) => Inv(n) + (n == 1 ? " answer" : " answers");

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);
}
