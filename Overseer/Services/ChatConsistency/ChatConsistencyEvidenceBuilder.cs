namespace Overseer.Services.ChatConsistency;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Services.Telemetry;

/// <summary>When an answer started and the time-of-week stratum it falls in.</summary>
/// <param name="StartUtc">The answer's start: recorded, or estimated.</param>
/// <param name="Stratum">The stratum index of <paramref name="StartUtc"/>.</param>
/// <param name="Estimated">
/// True when the start was not recorded: a sequential run's answer is placed at the run start plus the
/// durations of the answers before it in id order, a parallel run's at the run start.
/// </param>
public readonly record struct ChatConsistencyAnswerTiming(DateTime StartUtc, int Stratum, bool Estimated);

/// <summary>
/// The per-answer measures a chat consistency analysis and its timeline read, in one place so the two
/// cannot disagree. Pure; every measure is null when what it needs was not recorded.
/// </summary>
public static class ChatConsistencyMeasures
{
    /// <summary>The run statuses whose answers are analyzed.</summary>
    public static readonly IReadOnlyList<BenchmarkRunStatus> UsableStatuses = new[]
    {
        BenchmarkRunStatus.Completed, BenchmarkRunStatus.CompletedWithLimits, BenchmarkRunStatus.CompletedWithErrors
    };

    /// <summary>The published per-answer quality: the panel's mean on a panel run, else the assessor's score.</summary>
    public static double? NativeQuality(BenchmarkRunAnswer answer)
        => answer.PanelQualityScore ?? (answer.QualityScore is int q ? q : null);

    /// <summary>The answer was delivered: the measures of speed, work and cost read only these.</summary>
    public static bool IsDelivered(BenchmarkRunAnswer answer) => answer.Status == BenchmarkAnswerStatus.Ok;

    /// <summary>The provider never delivered the answer.</summary>
    public static bool IsTerminalFailure(BenchmarkRunAnswer answer)
        => answer.Status is BenchmarkAnswerStatus.ProviderError or BenchmarkAnswerStatus.Failed;

    /// <summary>
    /// The turn ran out of time: a candidate call ended with error kind <c>timeout</c>, or the answer's
    /// error text names a timeout.
    /// </summary>
    public static bool IsTimeout(BenchmarkRunAnswer answer)
    {
        if (CallTelemetryMeasures.CandidateCalls(answer).Any(c => string.Equals(c.ErrorKind, "timeout", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return ContainsTimeout(answer.ErrorMessage) || ContainsTimeout(answer.ProviderErrorDetail);
    }

    /// <summary>The model ended its turn with no answer text.</summary>
    public static bool IsEmptyAnswer(BenchmarkRunAnswer answer) => answer.Status == BenchmarkAnswerStatus.EmptyAnswer;

    /// <summary>A candidate call reported a refusal.</summary>
    public static bool IsRefusal(BenchmarkRunAnswer answer)
        => CallTelemetryMeasures.CandidateCalls(answer).Any(c => c.IsRefusal);

    /// <summary>The per-question tool budget ran out.</summary>
    public static bool IsToolBudgetExhausted(BenchmarkRunAnswer answer) => answer.ToolBudgetExhausted;

    /// <summary>The turn retried a failed attempt.</summary>
    public static bool HadRetry(BenchmarkRunAnswer answer)
        => (answer.RetryAttemptCount ?? 0) > 0
           || CallTelemetryMeasures.CandidateCalls(answer).Any(c => c.AttemptCount > 1 || c.Http429Count > 0 || c.Http5xxCount > 0);

    /// <summary>Tool calls of the turn: the recorded count, else the stored rows.</summary>
    public static int ToolCalls(BenchmarkRunAnswer answer) => answer.ToolCallCount ?? answer.ToolCalls?.Count ?? 0;

    /// <summary>The item key as text, <c>&lt;question&gt;@&lt;revision&gt;</c>.</summary>
    public static string ItemKey(BenchmarkRunAnswer answer) => ChatConsistencyComparability.ItemKey(answer).ToString();

    /// <summary>
    /// True when the tier a call was served at matches the one requested. A request for no tier,
    /// <c>auto</c>, <c>default</c> or <c>standard</c> matches any of those served; an unreported served
    /// tier matches anything, because it cannot be shown to differ.
    /// </summary>
    public static bool TierMatches(string? requested, string? served)
    {
        if (string.IsNullOrWhiteSpace(served)) return true;
        string r = (requested ?? string.Empty).Trim().ToLowerInvariant();
        string s = served.Trim().ToLowerInvariant();
        bool requestedDefault = r.Length == 0 || r is "auto" or "default" or "standard";
        bool servedDefault = s is "auto" or "default" or "standard";
        if (requestedDefault) return servedDefault;
        return string.Equals(r, s, StringComparison.Ordinal);
    }

    /// <summary>The answer's served tier matches its request: its final candidate call's, else the answer column against the snapshot.</summary>
    public static bool AnswerTierMatches(BenchmarkRunAnswer answer, BenchmarkRun run)
    {
        var final = CallTelemetryMeasures.FinalCandidateCall(answer);
        if (final != null) return TierMatches(final.ServiceTierRequested, final.ServedServiceTier);
        return TierMatches(run.TestedModelSnapshot?.ServiceTier, answer.ActualServiceTierUsed);
    }

    /// <summary>The candidate cost of one answer at <paramref name="card"/>; null without a card or token counts.</summary>
    public static decimal? AnswerCost(BenchmarkRunAnswer answer, BenchmarkRun run, ModelPricing? card)
    {
        if (card == null || (!answer.InputTokens.HasValue && !answer.OutputTokens.HasValue)) return null;

        return ModelPricingService.ComputeCostFromTotals(
            card,
            answer.InputTokens ?? 0, answer.OutputTokens ?? 0,
            answer.CacheReadInputTokens ?? 0, answer.CacheCreationInputTokens ?? 0,
            answer.LongContextInputTokens ?? 0, answer.LongContextOutputTokens ?? 0,
            answer.LongContextCacheReadTokens ?? 0, answer.LongContextCacheCreationTokens ?? 0,
            actualServiceTier: answer.ActualServiceTierUsed,
            requestedServiceTier: run.TestedModelSnapshot?.ServiceTier);
    }

    /// <summary>
    /// The model ids the provider reported serving a run's candidate calls, with call counts: the run's
    /// <see cref="BenchmarkRun.ServedModelIdsJson"/>, else the answers' <see cref="BenchmarkRunAnswer.ServedModelId"/>.
    /// Ordered by id.
    /// </summary>
    public static IReadOnlyList<ChatConsistencyServedModelCount> ServedModels(BenchmarkRun run)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(run.ServedModelIdsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(run.ServedModelIdsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {
                        int n = p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int v) ? v : 1;
                        counts[p.Name] = counts.TryGetValue(p.Name, out int c) ? c + n : n;
                    }
                }
            }
            catch (JsonException)
            {
                counts.Clear();
            }
        }

        if (counts.Count == 0)
        {
            foreach (var answer in run.Answers ?? new List<BenchmarkRunAnswer>())
            {
                if (string.IsNullOrWhiteSpace(answer.ServedModelId)) continue;
                string id = answer.ServedModelId.Trim();
                counts[id] = counts.TryGetValue(id, out int c) ? c + 1 : 1;
            }
        }

        return counts.Select(p => new ChatConsistencyServedModelCount(p.Key, p.Value)).ToList();
    }

    /// <summary>
    /// When each answer of <paramref name="run"/> started and its stratum. A recorded
    /// <see cref="BenchmarkRunAnswer.StartedAtUtc"/> is used as is; otherwise a sequential run's answer
    /// is estimated at the run start plus the durations of the answers before it in id order, and a
    /// parallel run's at the run start.
    /// </summary>
    public static IReadOnlyDictionary<long, ChatConsistencyAnswerTiming> AnswerTimings(BenchmarkRun run)
    {
        var timings = new Dictionary<long, ChatConsistencyAnswerTiming>();
        DateTime runStart = AsUtc(run.StartedAtUtc);
        long cumulativeMs = 0;
        foreach (var answer in (run.Answers ?? new List<BenchmarkRunAnswer>()).OrderBy(a => a.Id))
        {
            DateTime start;
            bool estimated;
            if (answer.StartedAtUtc.HasValue)
            {
                start = AsUtc(answer.StartedAtUtc.Value);
                estimated = false;
            }
            else
            {
                start = run.MaxParallelQuestionsUsed <= 1 ? runStart.AddMilliseconds(cumulativeMs) : runStart;
                estimated = true;
            }

            cumulativeMs += Math.Max(0L, answer.DurationMs);
            timings[answer.Id] = new ChatConsistencyAnswerTiming(start, ChatConsistencyStatistics.AssignStratum(start).Index, estimated);
        }

        return timings;
    }

    /// <summary>The instant read as UTC; an unspecified kind is UTC as stored.</summary>
    public static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <summary>A readable name of an Overseer event kind.</summary>
    public static string EventDescription(string kind) => kind switch
    {
        OverseerEventKinds.CandidateSystemPrompt => "system prompt changed",
        OverseerEventKinds.ToolGuides => "tool guides edited",
        OverseerEventKinds.KnowledgeBase => "knowledge base updated",
        OverseerEventKinds.Wiki => "wiki updated",
        OverseerEventKinds.SourceCode => "source code corpus updated",
        OverseerEventKinds.CorpusIndex => "corpus index changed",
        OverseerEventKinds.CandidatePromptOptions => "candidate prompt options changed",
        OverseerEventKinds.ToolIterationCaps => "tool iteration caps changed",
        OverseerEventKinds.TotalModelCallCaps => "model call caps changed",
        OverseerEventKinds.QuestionTimeouts => "question timeouts changed",
        OverseerEventKinds.MaxToolCallsPerQuestion => "tool call budget changed",
        OverseerEventKinds.HarnessVersion => "harness changed candidate input",
        _ => kind + " changed"
    };

    /// <summary>The event as the result shows it.</summary>
    public static ChatConsistencyEventView EventView(OverseerEvent e, bool inTargetSeries) => new()
    {
        AtUtc = AsUtc(e.AtUtc),
        Kind = e.Kind,
        Label = EventDescription(e.Kind) + " on " + AsUtc(e.AtUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            + " (run #" + e.RunId.ToString(CultureInfo.InvariantCulture) + ")",
        From = e.From,
        To = e.To,
        RunId = e.RunId,
        PreviousRunId = e.PreviousRunId,
        SubjectKey = e.SubjectKey,
        InTargetSeries = inTargetSeries
    };

    /// <summary>The annotation as the result shows it.</summary>
    public static ChatConsistencyAnnotationView AnnotationView(ChatConsistencyAnnotation a) => new()
    {
        Id = a.Id,
        AtUtc = AsUtc(a.AtUtc),
        Provider = a.Provider,
        ModelId = a.ModelId,
        Kind = a.Kind,
        Text = a.Text,
        SourceUrl = a.SourceUrl,
        CreatedAtUtc = AsUtc(a.CreatedAtUtc)
    };

    /// <summary>True when the annotation applies to <paramref name="provider"/>'s <paramref name="modelId"/>.</summary>
    public static bool AnnotationApplies(ChatConsistencyAnnotation a, string provider, string modelId)
        => (a.Provider == null || string.Equals(a.Provider.Trim(), provider, StringComparison.OrdinalIgnoreCase))
           && (a.ModelId == null || string.Equals(a.ModelId.Trim(), modelId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The subject a run measured, for display.</summary>
    public static ChatConsistencySubject SubjectOf(string key, BenchmarkRun? latest)
    {
        var snapshot = latest?.TestedModelSnapshot;
        string display = snapshot?.DisplayName ?? snapshot?.ModelId ?? key;
        if (!string.IsNullOrWhiteSpace(snapshot?.ThinkingLevel)) display += " (" + snapshot!.ThinkingLevel + ")";
        return new ChatConsistencySubject
        {
            Key = key,
            DisplayName = display,
            Provider = snapshot?.Provider ?? string.Empty,
            ModelId = snapshot?.ModelId ?? string.Empty,
            ThinkingLevel = snapshot?.ThinkingLevel,
            ServiceTier = snapshot?.ServiceTier,
            ConfigurationId = latest?.TestedModelConfigurationId
        };
    }

    /// <summary>The suite a run was launched against: its id when recorded, else its name.</summary>
    public static string SuiteIdentity(BenchmarkRun run)
        => (run.BenchmarkSuiteIdUsed ?? run.BenchmarkSuiteId) is long id
            ? "id:" + id.ToString(CultureInfo.InvariantCulture)
            : "name:" + (run.SuiteName ?? string.Empty);

    /// <summary>
    /// The verdicts of a calibration keyed by order index: quality, critical error and the four levels,
    /// read from <see cref="BenchmarkAssessorCalibration.VerdictsJson"/>. Empty when the JSON is absent or malformed.
    /// </summary>
    public static IReadOnlyDictionary<int, ChatConsistencyCalibrationVerdict> CalibrationVerdicts(BenchmarkAssessorCalibration calibration)
    {
        var verdicts = new Dictionary<int, ChatConsistencyCalibrationVerdict>();
        if (string.IsNullOrWhiteSpace(calibration.VerdictsJson)) return verdicts;

        try
        {
            using var doc = JsonDocument.Parse(calibration.VerdictsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return verdicts;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("orderIndex", out var oi) || !oi.TryGetInt32(out int orderIndex)) continue;
                double? quality = Number(item, "calibrationQualityScore");
                if (!quality.HasValue) continue;

                verdicts[orderIndex] = new ChatConsistencyCalibrationVerdict(
                    quality.Value,
                    item.TryGetProperty("calibrationCriticalError", out var ce) && ce.ValueKind == JsonValueKind.True,
                    Number(item, "accuracyLevel"),
                    Number(item, "completenessLevel"),
                    Number(item, "concisenessLevel"),
                    Number(item, "readabilityLevel"));
            }
        }
        catch (JsonException)
        {
            verdicts.Clear();
        }

        return verdicts;
    }

    private static double? Number(JsonElement item, string name)
        => item.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out double v) ? v : null;

    private static bool ContainsTimeout(string? text)
        => !string.IsNullOrEmpty(text)
           && (text.Contains("timeout", StringComparison.OrdinalIgnoreCase) || text.Contains("timed out", StringComparison.OrdinalIgnoreCase));
}

/// <summary>One answer's verdict in an assessor calibration.</summary>
public sealed record ChatConsistencyCalibrationVerdict(
    double Quality,
    bool CriticalError,
    double? AccuracyLevel,
    double? CompletenessLevel,
    double? ConcisenessLevel,
    double? ReadabilityLevel);

/// <summary>
/// The stored data one chat consistency analysis reads: the subject's runs in each period, the candidate
/// control runs, the calibrations of those runs and of the anchor runs, the matching annotations, the
/// answers' start times and strata, the Overseer events, and the price card. Built once by
/// <see cref="ChatConsistencyEvidenceBuilder.LoadAsync"/>; read-only afterwards.
/// </summary>
public sealed class ChatConsistencyEvidence
{
    public string SubjectKey { get; init; } = string.Empty;
    public ChatConsistencySubject Subject { get; init; } = new();
    public IReadOnlyList<BenchmarkRun> BaselineRuns { get; init; } = Array.Empty<BenchmarkRun>();
    public IReadOnlyList<BenchmarkRun> ComparisonRuns { get; init; } = Array.Empty<BenchmarkRun>();

    /// <summary>Candidate control runs of other subjects, from either period.</summary>
    public IReadOnlyList<BenchmarkRun> ControlRuns { get; init; } = Array.Empty<BenchmarkRun>();

    /// <summary>Calibrations of target and control runs, ordered by run, then creation, then id.</summary>
    public IReadOnlyList<BenchmarkAssessorCalibration> Calibrations { get; init; } = Array.Empty<BenchmarkAssessorCalibration>();

    /// <summary>Calibrations of runs marked <see cref="BenchmarkRun.IsConsistencyAnchor"/>.</summary>
    public IReadOnlyList<BenchmarkAssessorCalibration> AnchorCalibrations { get; init; } = Array.Empty<BenchmarkAssessorCalibration>();
    public IReadOnlyList<ChatConsistencyAnnotation> Annotations { get; init; } = Array.Empty<ChatConsistencyAnnotation>();

    /// <summary>Start and stratum of every target and control answer, by answer id.</summary>
    public IReadOnlyDictionary<long, ChatConsistencyAnswerTiming> AnswerTimings { get; init; } = new Dictionary<long, ChatConsistencyAnswerTiming>();

    /// <summary>Overseer events inside the compared span, from the target series and the control series, deduplicated.</summary>
    public IReadOnlyList<ChatConsistencyEventView> Events { get; init; } = Array.Empty<ChatConsistencyEventView>();

    /// <summary>Target and control runs without call telemetry.</summary>
    public IReadOnlyList<long> LegacyRunIds { get; init; } = Array.Empty<long>();
    public ChatConsistencyPriceCard PriceCard { get; init; } = new();
    public ModelPricing? Pricing { get; init; }

    /// <summary>What the load had to leave out, and why.</summary>
    public IReadOnlyList<ChatConsistencyNote> Notes { get; init; } = Array.Empty<ChatConsistencyNote>();

    /// <summary>
    /// Usable runs of the subject inside a period that are not target runs, each with why, ordered by start,
    /// then id. Empty unless the request gave both periods' runs (in a battery set, battery runs) explicitly.
    /// In a battery set every usable member of an unanalyzed battery run of the set is listed with its
    /// <see cref="ChatConsistencyUnanalyzedRun.BatteryRunId"/> and the battery run's reason, the members of
    /// one battery run together, ordered by the battery run's start, then its id, then suite order.
    /// </summary>
    public IReadOnlyList<ChatConsistencyUnanalyzedRun> UnanalyzedRuns { get; init; } = Array.Empty<ChatConsistencyUnanalyzedRun>();

    /// <summary>The battery or suite compared within; null for a run-by-run analysis.</summary>
    public ChatConsistencyComparedSet? ComparisonSet { get; init; }

    /// <summary><c>run</c> or <c>batteryRun</c>.</summary>
    public string UnitKind { get; init; } = ChatConsistencyComparisonSetKinds.RunUnit;

    /// <summary>Target run id → unit id: its battery run in a battery set, the run itself otherwise. Covers every target run.</summary>
    public IReadOnlyDictionary<long, long> UnitOf { get; init; } = new Dictionary<long, long>();

    /// <summary>Unit id → the unit's start: the battery run's <c>StartedAtUtc</c>, or the run's.</summary>
    public IReadOnlyDictionary<long, DateTime> UnitStartedAtUtc { get; init; } = new Dictionary<long, DateTime>();

    public IEnumerable<BenchmarkRun> TargetRuns => BaselineRuns.Concat(ComparisonRuns);

    /// <summary>The unit a target run belongs to; the run itself when <see cref="UnitOf"/> has no entry.</summary>
    public long UnitIdOf(long runId) => UnitOf.TryGetValue(runId, out var unit) ? unit : runId;

    /// <summary>The measurement segmentation of target and control runs, with grading bridged for <paramref name="commonGraderCovers"/>.</summary>
    public ComparabilityAssessment Assess(IEnumerable<long>? commonGraderCovers)
        => ChatConsistencyComparability.AssessMeasurement(TargetRuns.Concat(ControlRuns), commonGraderCovers);
}

/// <summary>
/// Loads the stored runs, calibrations and annotations a chat consistency analysis reads, and answers the
/// read-only queries the API serves (model axes, timeline, run table, battery-run table, comparison sets).
/// Reads only; spends nothing.
/// </summary>
public class ChatConsistencyEvidenceBuilder
{
    private readonly ApplicationDbContext _db;
    private readonly ModelPricingService? _pricingService;

    public ChatConsistencyEvidenceBuilder(ApplicationDbContext db, ModelPricingService? pricingService = null)
    {
        _db = db;
        _pricingService = pricingService;
    }

    // --- Analysis evidence -----------------------------------------------------------------------

    /// <summary>
    /// The evidence for <paramref name="request"/>. Target runs are the subject's usable runs started
    /// inside each period (inclusive bounds), or exactly the explicit ids when given; in a suite set only
    /// runs of that suite. In a battery set the targets are the usable members, on the subject's axis, of
    /// the set's complete battery runs started inside each period, or of exactly the explicit battery runs.
    /// Candidate controls are the explicit control ids, or every other subject's usable run on a target
    /// suite in either period. Refuses, with <see cref="ChatConsistencyRequestException"/>, a malformed
    /// comparison set, run ids with a battery set and battery run ids without one.
    /// </summary>
    public async Task<ChatConsistencyEvidence> LoadAsync(ChatConsistencyAnalysisRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var set = ParseComparisonSet(request.ComparisonSet);
        bool batterySet = set?.Kind == ChatConsistencyComparisonSetKinds.Battery;
        bool suiteSet = set?.Kind == ChatConsistencyComparisonSetKinds.Suite;
        if (batterySet && (request.BaselineRunIds != null || request.ComparisonRunIds != null))
        {
            throw new ChatConsistencyRequestException("A battery comparison takes battery run ids.");
        }

        if (!batterySet && (request.BaselineBatteryRunIds != null || request.ComparisonBatteryRunIds != null))
        {
            throw new ChatConsistencyRequestException("Battery run ids need a battery comparison set.");
        }

        var notes = new List<ChatConsistencyNote>();
        DateTime bs = request.BaselineStartUtc, be = request.BaselineEndUtc;
        DateTime cs = request.ComparisonStartUtc, ce = request.ComparisonEndUtc;
        var explicitBaseline = request.BaselineRunIds?.Distinct().ToHashSet();
        var explicitComparison = request.ComparisonRunIds?.Distinct().ToHashSet();
        var explicitControls = request.ControlRunIds?.Distinct().ToHashSet();
        var explicitBaselineBatteries = request.BaselineBatteryRunIds?.Distinct().ToHashSet();
        var explicitComparisonBatteries = request.ComparisonBatteryRunIds?.Distinct().ToHashSet();
        var explicitAll = new HashSet<long>();
        if (explicitBaseline != null) explicitAll.UnionWith(explicitBaseline);
        if (explicitComparison != null) explicitAll.UnionWith(explicitComparison);
        if (explicitControls != null) explicitAll.UnionWith(explicitControls);
        var explicitIds = explicitAll.ToList();

        // The battery runs of the set started in either period, and every battery run given explicitly.
        var batteries = new List<BatteryRunState>();
        var batteryMemberRuns = new Dictionary<long, BenchmarkRun>();
        if (batterySet)
        {
            string sha = set!.Value;
            var givenBatteries = new HashSet<long>();
            if (explicitBaselineBatteries != null) givenBatteries.UnionWith(explicitBaselineBatteries);
            if (explicitComparisonBatteries != null) givenBatteries.UnionWith(explicitComparisonBatteries);
            var given = givenBatteries.ToList();
            (batteries, batteryMemberRuns) = await LoadBatteryStatesAsync(
                request.SubjectModelKey,
                q => q.Where(b => given.Contains(b.Id)
                                  || (b.DefinitionSha256 == sha
                                      && ((b.StartedAtUtc >= bs && b.StartedAtUtc <= be) || (b.StartedAtUtc >= cs && b.StartedAtUtc <= ce)))),
                ct);
        }

        var statuses = ChatConsistencyMeasures.UsableStatuses.ToList();
        var headers = await _db.BenchmarkRuns
            .AsNoTracking()
            .Where(r => explicitIds.Contains(r.Id)
                        || (statuses.Contains(r.Status)
                            && ((r.StartedAtUtc >= bs && r.StartedAtUtc <= be) || (r.StartedAtUtc >= cs && r.StartedAtUtc <= ce))))
            .ToListAsync(ct);
        var headerIds = headers.Select(r => r.Id).ToHashSet();
        headers.AddRange(batteryMemberRuns.Values.Where(r => headerIds.Add(r.Id)));

        var keyOf = headers.ToDictionary(r => r.Id, ChatConsistencyComparability.ModelAxisKey);
        bool Usable(BenchmarkRun r) => statuses.Contains(r.Status);
        bool InWindow(BenchmarkRun r, DateTime start, DateTime end) => r.StartedAtUtc >= start && r.StartedAtUtc <= end;
        bool InSuite(BenchmarkRun r) => !suiteSet || string.Equals(ChatConsistencyMeasures.SuiteIdentity(r), set!.Value, StringComparison.Ordinal);
        bool InBatterySet(BatteryRunState b) => batterySet && string.Equals(b.Row.DefinitionSha256, set!.Value, StringComparison.Ordinal);

        List<long> PickTargets(HashSet<long>? explicitSet, DateTime start, DateTime end, string period)
        {
            if (explicitSet == null)
            {
                return headers
                    .Where(r => Usable(r) && InWindow(r, start, end) && keyOf[r.Id] == request.SubjectModelKey && InSuite(r))
                    .Select(r => r.Id)
                    .ToList();
            }

            var picked = new List<long>();
            foreach (long id in explicitSet.OrderBy(i => i))
            {
                var run = headers.FirstOrDefault(r => r.Id == id);
                if (run == null)
                {
                    notes.Add(Note("excludedRun", "Run #" + Inv(id) + " given for the " + period + " was not found."));
                }
                else if (keyOf[id] != request.SubjectModelKey)
                {
                    notes.Add(Note("excludedRun", "Run #" + Inv(id) + " given for the " + period + " measured another model axis and was left out."));
                }
                else if (!Usable(run))
                {
                    notes.Add(Note("excludedRun", "Run #" + Inv(id) + " given for the " + period + " has status " + run.Status + " and was left out."));
                }
                else if (!InSuite(run))
                {
                    notes.Add(Note("excludedRun", "Run #" + Inv(id) + " answered another suite and was left out."));
                }
                else
                {
                    picked.Add(id);
                }
            }

            return picked;
        }

        List<BatteryRunState> PickBatteries(HashSet<long>? explicitSet, DateTime start, DateTime end)
        {
            if (explicitSet == null)
            {
                return batteries
                    .Where(b => InBatterySet(b) && b.Complete && b.Row.StartedAtUtc >= start && b.Row.StartedAtUtc <= end)
                    .ToList();
            }

            var picked = new List<BatteryRunState>();
            foreach (long id in explicitSet.OrderBy(i => i))
            {
                var battery = batteries.FirstOrDefault(b => b.Row.Id == id);
                string head = "Battery run #" + Inv(id);
                if (battery == null)
                {
                    notes.Add(Note("excludedBatteryRun", head + " was not found."));
                }
                else if (!InBatterySet(battery))
                {
                    notes.Add(Note("excludedBatteryRun", head + " belongs to another battery definition and was left out."));
                }
                else if (!battery.OnAxis)
                {
                    notes.Add(Note("excludedBatteryRun", head + " measured another model axis and was left out."));
                }
                else if (!battery.Complete)
                {
                    notes.Add(Note("excludedBatteryRun", head + " is incomplete (" + battery.IncompleteReason + ") and was left out."));
                }
                else
                {
                    picked.Add(battery);
                }
            }

            return picked;
        }

        List<long> baselineIds;
        List<long> comparisonIds;
        var unitOf = new Dictionary<long, long>();
        var unitStarts = new Dictionary<long, DateTime>();
        var keptBatteries = new HashSet<long>();
        if (batterySet)
        {
            var baselineBatteries = PickBatteries(explicitBaselineBatteries, bs, be);
            var baselineBatteryIds = baselineBatteries.Select(b => b.Row.Id).ToHashSet();
            var comparisonBatteries = PickBatteries(explicitComparisonBatteries, cs, ce).Where(b => !baselineBatteryIds.Contains(b.Row.Id)).ToList();

            // A run serving several picked battery runs stays with the first by start, then id; the later ones are refused.
            var claimedBy = new Dictionary<long, long>();
            foreach (var battery in baselineBatteries.Concat(comparisonBatteries).OrderBy(b => b.Row.StartedAtUtc).ThenBy(b => b.Row.Id))
            {
                long? shared = battery.MemberRunIds.Where(claimedBy.ContainsKey).Select(id => (long?)id).FirstOrDefault();
                if (shared is long sharedRunId)
                {
                    notes.Add(Note("excludedBatteryRun", "Battery run #" + Inv(battery.Row.Id) + " shares run #" + Inv(sharedRunId)
                        + " with battery run #" + Inv(claimedBy[sharedRunId]) + " and was left out."));
                    continue;
                }

                foreach (long runId in battery.MemberRunIds) claimedBy[runId] = battery.Row.Id;
                keptBatteries.Add(battery.Row.Id);
                unitStarts[battery.Row.Id] = ChatConsistencyMeasures.AsUtc(battery.Row.StartedAtUtc);
            }

            List<long> Members(IEnumerable<BatteryRunState> picked)
            {
                var members = new List<long>();
                foreach (var battery in picked.Where(b => keptBatteries.Contains(b.Row.Id)).OrderBy(b => b.Row.StartedAtUtc).ThenBy(b => b.Row.Id))
                {
                    foreach (long runId in battery.MemberRunIds)
                    {
                        unitOf[runId] = battery.Row.Id;
                        members.Add(runId);
                    }
                }

                return members;
            }

            baselineIds = Members(baselineBatteries);
            comparisonIds = Members(comparisonBatteries);
        }
        else
        {
            baselineIds = PickTargets(explicitBaseline, bs, be, "baseline");
            comparisonIds = PickTargets(explicitComparison, cs, ce, "comparison").Where(id => !baselineIds.Contains(id)).ToList();
            foreach (var run in headers.Where(r => baselineIds.Contains(r.Id) || comparisonIds.Contains(r.Id)))
            {
                unitOf[run.Id] = run.Id;
                unitStarts[run.Id] = ChatConsistencyMeasures.AsUtc(run.StartedAtUtc);
            }
        }

        var targetIds = baselineIds.Concat(comparisonIds).ToHashSet();
        var targetHeaders = headers.Where(r => targetIds.Contains(r.Id)).ToList();
        var targetSuites = targetHeaders.Select(ChatConsistencyMeasures.SuiteIdentity).ToHashSet(StringComparer.Ordinal);
        if (set == null && targetSuites.Count > 1)
        {
            var names = targetHeaders
                .GroupBy(ChatConsistencyMeasures.SuiteIdentity, StringComparer.Ordinal)
                .Select(g => g.First().SuiteName ?? string.Empty)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            notes.Add(Note("mixedSuites", "The analyzed runs answered " + Inv(targetSuites.Count) + " suites (" + string.Join(", ", names)
                + "); their items pair by question and revision across them. Choose a battery or a suite to compare within."));
        }

        List<long> controlIds;
        if (explicitControls != null)
        {
            controlIds = new List<long>();
            foreach (long id in explicitControls.OrderBy(i => i))
            {
                var run = headers.FirstOrDefault(r => r.Id == id);
                if (run == null || !Usable(run) || keyOf[id] == request.SubjectModelKey || targetIds.Contains(id))
                {
                    notes.Add(Note("excludedControl", "Control run #" + Inv(id) + " was not found, is not usable, or measured the subject itself, and was left out."));
                    continue;
                }

                controlIds.Add(id);
            }
        }
        else
        {
            controlIds = headers
                .Where(r => Usable(r)
                            && keyOf[r.Id] != request.SubjectModelKey
                            && (InWindow(r, bs, be) || InWindow(r, cs, ce))
                            && targetSuites.Contains(ChatConsistencyMeasures.SuiteIdentity(r)))
                .Select(r => r.Id)
                .ToList();
        }

        var subjectOthers = headers.Where(r => Usable(r) && keyOf[r.Id] == request.SubjectModelKey && !targetIds.Contains(r.Id)).ToList();
        List<ChatConsistencyUnanalyzedRun> unanalyzed;
        if (batterySet && explicitBaselineBatteries != null && explicitComparisonBatteries != null)
        {
            unanalyzed = await ClassifyUnanalyzedBatteriesAsync(
                request,
                batteries.Where(b => InBatterySet(b) && b.Usable.Count > 0 && !keptBatteries.Contains(b.Row.Id)),
                subjectOthers,
                targetIds,
                notes,
                ct);
        }
        else if (!batterySet && explicitBaseline != null && explicitComparison != null)
        {
            unanalyzed = await ClassifyUnanalyzedAsync(
                request,
                subjectOthers.Where(InSuite),
                subjectOthers.Where(r => !InSuite(r)),
                notes,
                ct);
        }
        else
        {
            unanalyzed = new List<ChatConsistencyUnanalyzedRun>();
        }

        var full = await LoadFullAsync(targetIds.Concat(controlIds).ToList(), includeTelemetry: true, ct);
        var byId = full.ToDictionary(r => r.Id);
        var baseline = Ordered(baselineIds.Where(byId.ContainsKey).Select(id => byId[id]));
        var comparison = Ordered(comparisonIds.Where(byId.ContainsKey).Select(id => byId[id]));
        var controls = Ordered(controlIds.Where(byId.ContainsKey).Select(id => byId[id]));

        var allRunIds = byId.Keys.ToList();
        var calibrations = await LoadCalibrationsAsync(allRunIds, ct);

        var anchorIds = await _db.BenchmarkRuns.AsNoTracking().Where(r => r.IsConsistencyAnchor).Select(r => r.Id).ToListAsync(ct);
        var anchorCalibrations = await LoadCalibrationsAsync(anchorIds, ct);

        var latest = baseline.Concat(comparison).OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id).LastOrDefault();
        var subject = ChatConsistencyMeasures.SubjectOf(request.SubjectModelKey, latest);

        DateTime annotationFrom = bs < cs ? bs : cs;
        DateTime annotationTo = be > ce ? be : ce;
        var annotations = (await _db.ChatConsistencyAnnotations
                .AsNoTracking()
                .Where(a => a.AtUtc >= annotationFrom && a.AtUtc <= annotationTo)
                .ToListAsync(ct))
            .Where(a => ChatConsistencyMeasures.AnnotationApplies(a, subject.Provider, subject.ModelId))
            .OrderBy(a => a.AtUtc)
            .ThenBy(a => a.Id)
            .ToList();

        var timings = new Dictionary<long, ChatConsistencyAnswerTiming>();
        foreach (var run in baseline.Concat(comparison).Concat(controls))
        {
            foreach (var pair in ChatConsistencyMeasures.AnswerTimings(run)) timings[pair.Key] = pair.Value;
        }

        var (priceCard, pricing) = latest == null
            ? (new ChatConsistencyPriceCard { Available = false, Source = "none" }, (ModelPricing?)null)
            : await ResolvePriceCardAsync(latest);

        ChatConsistencyComparedSet? compared = set == null
            ? null
            : new ChatConsistencyComparedSet
            {
                Kind = set.Kind,
                Key = set.Key,
                Label = batterySet
                    ? await BatterySetLabelAsync(set.Value, batteries.Where(InBatterySet).ToList(), ct)
                    : SuiteSetLabel(set.Value, headers)
            };
        var analyzedIds = baseline.Concat(comparison).Select(r => r.Id).ToHashSet();
        var analyzedUnitOf = unitOf.Where(p => analyzedIds.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        var analyzedUnits = analyzedUnitOf.Values.ToHashSet();

        return new ChatConsistencyEvidence
        {
            SubjectKey = request.SubjectModelKey,
            Subject = subject,
            BaselineRuns = baseline,
            ComparisonRuns = comparison,
            ControlRuns = controls,
            Calibrations = calibrations,
            AnchorCalibrations = anchorCalibrations,
            Annotations = annotations,
            AnswerTimings = timings,
            Events = SpanEvents(baseline, comparison, controls),
            LegacyRunIds = baseline.Concat(comparison).Concat(controls).Where(r => !r.CallTelemetryVersion.HasValue).Select(r => r.Id).OrderBy(i => i).ToList(),
            PriceCard = priceCard,
            Pricing = pricing,
            Notes = notes,
            UnanalyzedRuns = unanalyzed,
            ComparisonSet = compared,
            UnitKind = batterySet ? ChatConsistencyComparisonSetKinds.BatteryRunUnit : ChatConsistencyComparisonSetKinds.RunUnit,
            UnitOf = analyzedUnitOf,
            UnitStartedAtUtc = unitStarts.Where(p => analyzedUnits.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value)
        };
    }

    // --- Comparison sets -------------------------------------------------------------------------

    /// <summary>A comparison set as the request names it: its kind, its key, and the key after its prefix.</summary>
    private sealed record SetRef(string Kind, string Key, string Value);

    /// <summary>
    /// The request's comparison set, trimmed; null for a run-by-run analysis. Refuses a kind other than
    /// <c>battery</c> or <c>suite</c>, and a key that does not start with its kind's prefix or ends there.
    /// </summary>
    private static SetRef? ParseComparisonSet(ChatConsistencyComparisonSetRef? set)
    {
        if (set == null) return null;

        string kind = (set.Kind ?? string.Empty).Trim();
        string key = (set.Key ?? string.Empty).Trim();
        string prefix = kind switch
        {
            ChatConsistencyComparisonSetKinds.Battery => ChatConsistencyComparisonSetKinds.BatteryKeyPrefix,
            ChatConsistencyComparisonSetKinds.Suite => ChatConsistencyComparisonSetKinds.SuiteKeyPrefix,
            _ => throw new ChatConsistencyRequestException("A comparison set is a battery or a suite.")
        };

        if (!key.StartsWith(prefix, StringComparison.Ordinal) || key.Length == prefix.Length)
        {
            throw new ChatConsistencyRequestException("A " + kind + " comparison set's key is " + prefix + " followed by "
                + (kind == ChatConsistencyComparisonSetKinds.Battery ? "the battery definition hash." : "the suite identity."));
        }

        return new SetRef(kind, key, key[prefix.Length..]);
    }

    /// <summary>"Two initial suites (revision 1)", or "(revisions 1, 2)" when several revisions share the definition; the name alone without a known revision.</summary>
    public static string BatteryLabel(string? name, IEnumerable<int> revisions)
    {
        string label = (name ?? string.Empty).Trim();
        var known = revisions.Where(r => r > 0).Distinct().OrderBy(r => r).ToList();
        if (known.Count == 0) return label;
        return label + (known.Count == 1 ? " (revision " : " (revisions ")
            + string.Join(", ", known.Select(r => r.ToString(CultureInfo.InvariantCulture))) + ")";
    }

    /// <summary>
    /// The battery set's label from its loaded battery runs, the newest one's name; without one, from every
    /// battery run with the definition hash; without any, the hash's first twelve characters.
    /// </summary>
    private async Task<string> BatterySetLabelAsync(string sha, List<BatteryRunState> loaded, CancellationToken ct)
    {
        var rows = loaded
            .Select(b => (Id: b.Row.Id, Start: b.Row.StartedAtUtc, Name: b.Row.BatteryName, Revision: b.Definition?.Revision ?? 0))
            .ToList();
        if (rows.Count == 0)
        {
            rows = (await _db.BenchmarkBatteryRuns.AsNoTracking()
                    .Where(b => b.DefinitionSha256 == sha)
                    .Select(b => new { b.Id, b.StartedAtUtc, b.BatteryName, b.DefinitionJson })
                    .ToListAsync(ct))
                .Select(b => (Id: b.Id, Start: b.StartedAtUtc, Name: b.BatteryName, Revision: TryReadDefinition(b.DefinitionJson)?.Revision ?? 0))
                .ToList();
        }

        if (rows.Count == 0) return "battery " + sha[..Math.Min(12, sha.Length)];

        var newest = rows.OrderByDescending(r => r.Start).ThenByDescending(r => r.Id).First();
        return BatteryLabel(newest.Name, rows.Select(r => r.Revision));
    }

    /// <summary>The suite set's label: the suite name of its newest run among <paramref name="runs"/>, else its identity.</summary>
    private static string SuiteSetLabel(string suiteIdentity, IEnumerable<BenchmarkRun> runs)
    {
        var newest = runs
            .Where(r => string.Equals(ChatConsistencyMeasures.SuiteIdentity(r), suiteIdentity, StringComparison.Ordinal))
            .OrderByDescending(r => r.StartedAtUtc)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();
        return !string.IsNullOrWhiteSpace(newest?.SuiteName) ? newest!.SuiteName : suiteIdentity;
    }

    // --- Battery runs ----------------------------------------------------------------------------

    /// <summary>A battery run read for one model axis.</summary>
    private sealed class BatteryRunState
    {
        public BenchmarkBatteryRun Row { get; init; } = default!;

        /// <summary>The definition snapshot; null when it cannot be read.</summary>
        public BenchmarkBatteryDefinition? Definition { get; init; }

        /// <summary>A non-superseded member's run is on the axis.</summary>
        public bool OnAxis { get; init; }

        /// <summary>The usable members on the axis, in suite order, then round.</summary>
        public IReadOnlyList<(BenchmarkBatteryRunMember Member, BenchmarkRun Run)> Usable { get; init; } = Array.Empty<(BenchmarkBatteryRunMember, BenchmarkRun)>();

        /// <summary>Every (suite, round) slot of the definition holds a usable member on the axis.</summary>
        public bool Complete { get; init; }

        /// <summary>For example "1 of 2 suites usable"; null when complete.</summary>
        public string? IncompleteReason { get; init; }

        /// <summary>The usable members' run ids, distinct, in suite order.</summary>
        public IEnumerable<long> MemberRunIds => Usable.Select(u => u.Run.Id).Distinct();
    }

    /// <summary>
    /// The battery runs <paramref name="filter"/> selects, read for <paramref name="modelKey"/> and ordered by
    /// start, then id; with the runs of their non-superseded members by id.
    /// </summary>
    private async Task<(List<BatteryRunState> States, Dictionary<long, BenchmarkRun> MemberRuns)> LoadBatteryStatesAsync(
        string modelKey, Func<IQueryable<BenchmarkBatteryRun>, IQueryable<BenchmarkBatteryRun>> filter, CancellationToken ct)
    {
        var rows = await filter(_db.BenchmarkBatteryRuns.AsNoTracking().Include(b => b.Members)).ToListAsync(ct);
        var runIds = rows.SelectMany(b => b.Members).Where(m => !m.Superseded).Select(m => m.BenchmarkRunId).Distinct().ToList();
        var memberRuns = runIds.Count == 0
            ? new Dictionary<long, BenchmarkRun>()
            : (await _db.BenchmarkRuns.AsNoTracking().Where(r => runIds.Contains(r.Id)).ToListAsync(ct)).ToDictionary(r => r.Id);
        var axisOf = memberRuns.Values.ToDictionary(r => r.Id, ChatConsistencyComparability.ModelAxisKey);

        var states = rows
            .Select(b => BatteryState(b, memberRuns, axisOf, modelKey))
            .OrderBy(s => s.Row.StartedAtUtc)
            .ThenBy(s => s.Row.Id)
            .ToList();
        return (states, memberRuns);
    }

    /// <summary><paramref name="query"/> narrowed to the battery runs started between the bounds (inclusive, either may be null).</summary>
    private static IQueryable<BenchmarkBatteryRun> StartedBetween(IQueryable<BenchmarkBatteryRun> query, DateTime? fromUtc, DateTime? toUtc)
    {
        if (fromUtc.HasValue)
        {
            DateTime from = fromUtc.Value;
            query = query.Where(b => b.StartedAtUtc >= from);
        }

        if (toUtc.HasValue)
        {
            DateTime to = toUtc.Value;
            query = query.Where(b => b.StartedAtUtc <= to);
        }

        return query;
    }

    /// <summary>
    /// <paramref name="row"/> read for <paramref name="modelKey"/>: a member is usable when
    /// <see cref="BenchmarkBatteryPlanner.UnusableReason"/> finds nothing against it and its run is on the
    /// axis. The battery run is complete when every slot holds a usable member; otherwise the reason counts
    /// the suites with one, or the slots when every suite has one.
    /// </summary>
    private static BatteryRunState BatteryState(
        BenchmarkBatteryRun row, IReadOnlyDictionary<long, BenchmarkRun> runs, IReadOnlyDictionary<long, string> axisOf, string modelKey)
    {
        var definition = TryReadDefinition(row.DefinitionJson);
        int suiteCount = definition?.Suites.Count ?? 0;
        int rounds = Math.Max(1, row.RunsPerSuite);

        var onAxis = (row.Members ?? new List<BenchmarkBatteryRunMember>())
            .Where(m => !m.Superseded && axisOf.TryGetValue(m.BenchmarkRunId, out var key) && key == modelKey)
            .ToList();
        var usable = onAxis
            .Where(m => BenchmarkBatteryPlanner.UnusableReason(m, runs[m.BenchmarkRunId]) == null)
            .OrderBy(m => m.SuiteIndex)
            .ThenBy(m => m.Round)
            .ThenBy(m => m.Id)
            .Select(m => (Member: m, Run: runs[m.BenchmarkRunId]))
            .ToList();

        string? reason;
        if (suiteCount == 0)
        {
            reason = "the battery definition cannot be read";
        }
        else
        {
            var slots = usable
                .Select(u => (u.Member.SuiteIndex, u.Member.Round))
                .Where(s => s.SuiteIndex >= 0 && s.SuiteIndex < suiteCount && s.Round >= 1 && s.Round <= rounds)
                .ToHashSet();
            int suites = slots.Select(s => s.SuiteIndex).Distinct().Count();
            reason = suites < suiteCount
                ? Inv(suites) + " of " + Inv(suiteCount) + (suiteCount == 1 ? " suite usable" : " suites usable")
                : slots.Count < suiteCount * rounds
                    ? Inv(slots.Count) + " of " + Inv(suiteCount * rounds) + " slots usable"
                    : null;
        }

        return new BatteryRunState
        {
            Row = row,
            Definition = definition,
            OnAxis = onAxis.Count > 0,
            Usable = usable,
            Complete = reason == null,
            IncompleteReason = reason
        };
    }

    /// <summary>The definition snapshot a battery run stores; null when absent or malformed.</summary>
    private static BenchmarkBatteryDefinition? TryReadDefinition(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return BenchmarkBatteryDefinition.FromJson(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The <paramref name="candidates"/> inside a period (the baseline's window first), each with the
    /// first reason of <see cref="ChatConsistencyUnanalyzedReasons.All"/> that applies, and the runs of
    /// <paramref name="outsideSet"/> inside a period as <c>outsideComparisonSet</c>; ordered by start, then
    /// id. A first or last run of the selection that is not found is ignored with a <c>runSelection</c> note.
    /// </summary>
    private async Task<List<ChatConsistencyUnanalyzedRun>> ClassifyUnanalyzedAsync(
        ChatConsistencyAnalysisRequest request, IEnumerable<BenchmarkRun> candidates, IEnumerable<BenchmarkRun> outsideSet,
        List<ChatConsistencyNote> notes, CancellationToken ct)
    {
        var selection = request.RunSelection;
        var markIds = new[] { selection?.FirstRunId, selection?.LastRunId }.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        var markStarts = markIds.Count == 0
            ? new Dictionary<long, DateTime>()
            : (await _db.BenchmarkRuns.AsNoTracking()
                    .Where(r => markIds.Contains(r.Id))
                    .Select(r => new { r.Id, r.StartedAtUtc })
                    .ToListAsync(ct))
                .ToDictionary(r => r.Id, r => r.StartedAtUtc);
        var marks = SelectionMarks.Of(
            selection?.LeftOutRunIds, selection?.FirstRunId, selection?.LastRunId, selection, markStarts, "run", notes);

        var entries = new List<UnanalyzedEntry>();
        foreach (var run in candidates)
        {
            if (PeriodOf(request, run.StartedAtUtc) is not string period) continue;
            entries.Add(new UnanalyzedEntry(run.StartedAtUtc, run.Id, 0, new ChatConsistencyUnanalyzedRun
            {
                RunId = run.Id,
                Period = period,
                StartedAtUtc = ChatConsistencyMeasures.AsUtc(run.StartedAtUtc),
                Reason = marks.Reason(run.Id, run.StartedAtUtc)
            }));
        }

        entries.AddRange(OutsideSetEntries(request, outsideSet));
        return Sorted(entries);
    }

    /// <summary>
    /// The usable members of the <paramref name="candidates"/> battery runs inside a period, each listed with
    /// its battery run and the battery run's reason, computed from the battery selection; then every run of
    /// <paramref name="subjectOthers"/> inside a period not listed so far, as <c>outsideComparisonSet</c>. A
    /// member that is a target run is skipped, and a run serving several candidates is listed under the first
    /// by start, then id. A first or last battery run of the selection that is not found is ignored with a
    /// <c>runSelection</c> note.
    /// </summary>
    private async Task<List<ChatConsistencyUnanalyzedRun>> ClassifyUnanalyzedBatteriesAsync(
        ChatConsistencyAnalysisRequest request, IEnumerable<BatteryRunState> candidates, IEnumerable<BenchmarkRun> subjectOthers,
        ISet<long> targetIds, List<ChatConsistencyNote> notes, CancellationToken ct)
    {
        var selection = request.RunSelection;
        var markIds = new[] { selection?.FirstBatteryRunId, selection?.LastBatteryRunId }.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        var markStarts = markIds.Count == 0
            ? new Dictionary<long, DateTime>()
            : (await _db.BenchmarkBatteryRuns.AsNoTracking()
                    .Where(b => markIds.Contains(b.Id))
                    .Select(b => new { b.Id, b.StartedAtUtc })
                    .ToListAsync(ct))
                .ToDictionary(b => b.Id, b => b.StartedAtUtc);
        var marks = SelectionMarks.Of(
            selection?.LeftOutBatteryRunIds, selection?.FirstBatteryRunId, selection?.LastBatteryRunId, selection, markStarts, "battery run", notes);

        var listed = new HashSet<long>();
        var entries = new List<UnanalyzedEntry>();
        foreach (var battery in candidates.OrderBy(b => b.Row.StartedAtUtc).ThenBy(b => b.Row.Id))
        {
            if (PeriodOf(request, battery.Row.StartedAtUtc) is not string period) continue;
            string reason = marks.Reason(battery.Row.Id, battery.Row.StartedAtUtc);
            int order = 0;
            foreach (var (_, run) in battery.Usable)
            {
                if (targetIds.Contains(run.Id) || !listed.Add(run.Id)) continue;
                entries.Add(new UnanalyzedEntry(battery.Row.StartedAtUtc, battery.Row.Id, order++, new ChatConsistencyUnanalyzedRun
                {
                    RunId = run.Id,
                    Period = period,
                    StartedAtUtc = ChatConsistencyMeasures.AsUtc(run.StartedAtUtc),
                    Reason = reason,
                    BatteryRunId = battery.Row.Id
                }));
            }
        }

        entries.AddRange(OutsideSetEntries(request, subjectOthers.Where(r => !listed.Contains(r.Id))));
        return Sorted(entries);
    }

    /// <summary>One unanalyzed run with the keys it is ordered by: a start and an id (its battery run's in a battery set), then its place in the battery run.</summary>
    private sealed record UnanalyzedEntry(DateTime SortStart, long SortId, int Order, ChatConsistencyUnanalyzedRun Run);

    private static List<ChatConsistencyUnanalyzedRun> Sorted(IEnumerable<UnanalyzedEntry> entries)
        => entries.OrderBy(e => e.SortStart).ThenBy(e => e.SortId).ThenBy(e => e.Order).Select(e => e.Run).ToList();

    /// <summary>The runs inside a period as <c>outsideComparisonSet</c>.</summary>
    private static IEnumerable<UnanalyzedEntry> OutsideSetEntries(ChatConsistencyAnalysisRequest request, IEnumerable<BenchmarkRun> runs)
    {
        foreach (var run in runs)
        {
            if (PeriodOf(request, run.StartedAtUtc) is not string period) continue;
            yield return new UnanalyzedEntry(run.StartedAtUtc, run.Id, 0, new ChatConsistencyUnanalyzedRun
            {
                RunId = run.Id,
                Period = period,
                StartedAtUtc = ChatConsistencyMeasures.AsUtc(run.StartedAtUtc),
                Reason = ChatConsistencyUnanalyzedReasons.OutsideComparisonSet
            });
        }
    }

    /// <summary><c>baseline</c> or <c>comparison</c> for a start inside that period (the baseline's window first); null outside both.</summary>
    private static string? PeriodOf(ChatConsistencyAnalysisRequest request, DateTime start)
        => start >= request.BaselineStartUtc && start <= request.BaselineEndUtc ? "baseline"
            : start >= request.ComparisonStartUtc && start <= request.ComparisonEndUtc ? "comparison"
            : null;

    /// <summary>The step-1 choices a reason is tested against: the left-out ids, the first and last marks, and the dates.</summary>
    private sealed class SelectionMarks
    {
        private readonly HashSet<long> _leftOut;
        private readonly (DateTime Start, long Id)? _first;
        private readonly (DateTime Start, long Id)? _last;
        private readonly DateTime? _rangeFrom;
        private readonly DateTime? _rangeTo;

        private SelectionMarks(HashSet<long> leftOut, (DateTime, long)? first, (DateTime, long)? last, DateTime? rangeFrom, DateTime? rangeTo)
        {
            _leftOut = leftOut;
            _first = first;
            _last = last;
            _rangeFrom = rangeFrom;
            _rangeTo = rangeTo;
        }

        /// <summary>The marks of <paramref name="selection"/>; a first or last <paramref name="noun"/> missing from <paramref name="markStarts"/> is ignored with a note.</summary>
        public static SelectionMarks Of(
            IReadOnlyList<long>? leftOut, long? firstId, long? lastId, ChatConsistencyRunSelection? selection,
            IReadOnlyDictionary<long, DateTime> markStarts, string noun, List<ChatConsistencyNote> notes)
        {
            (DateTime, long)? Mark(long? id, string which)
            {
                if (!id.HasValue) return null;
                if (markStarts.TryGetValue(id.Value, out var start)) return (start, id.Value);
                notes.Add(Note("runSelection", "The " + which + " " + noun + " of the selection, #" + Inv(id.Value) + ", was not found."));
                return null;
            }

            var first = Mark(firstId, "first");
            var last = Mark(lastId, "last");
            return new SelectionMarks((leftOut ?? Array.Empty<long>()).ToHashSet(), first, last, selection?.RangeFromUtc, selection?.RangeToUtc);
        }

        /// <summary>The first reason of <see cref="ChatConsistencyUnanalyzedReasons.All"/> that applies to the run or battery run.</summary>
        public string Reason(long id, DateTime start)
        {
            if (_leftOut.Contains(id)) return ChatConsistencyUnanalyzedReasons.LeftOut;
            if ((_rangeFrom is DateTime from && start < from) || (_rangeTo is DateTime to && start > to))
            {
                return ChatConsistencyUnanalyzedReasons.OutsideDateRange;
            }

            if (_first is { } f && Compare(start, id, f) < 0) return ChatConsistencyUnanalyzedReasons.BeforeFirstRun;
            if (_last is { } l && Compare(start, id, l) > 0) return ChatConsistencyUnanalyzedReasons.AfterLastRun;
            return ChatConsistencyUnanalyzedReasons.NotSelected;
        }

        private static int Compare(DateTime start, long id, (DateTime Start, long Id) mark)
        {
            int byStart = start.CompareTo(mark.Start);
            return byStart != 0 ? byStart : id.CompareTo(mark.Id);
        }
    }

    /// <summary>
    /// The Overseer events between the first baseline run and the last comparison run, from the target
    /// series and every control series, one per kind and change: the earliest, the target's on a tie.
    /// </summary>
    public static IReadOnlyList<ChatConsistencyEventView> SpanEvents(
        IReadOnlyList<BenchmarkRun> baseline, IReadOnlyList<BenchmarkRun> comparison, IReadOnlyList<BenchmarkRun> controls)
    {
        var targets = baseline.Concat(comparison).ToList();
        if (targets.Count == 0) return Array.Empty<ChatConsistencyEventView>();

        DateTime spanStart = targets.Min(r => r.StartedAtUtc);
        DateTime spanEnd = targets.Max(r => r.StartedAtUtc);
        var targetIds = targets.Select(r => r.Id).ToHashSet();

        return ChatConsistencyComparability.DetectOverseerEvents(targets.Concat(controls))
            .Where(e => e.AtUtc >= spanStart && e.AtUtc <= spanEnd)
            .Select(e => ChatConsistencyMeasures.EventView(e, targetIds.Contains(e.RunId)))
            .GroupBy(e => (e.Kind, e.From, e.To))
            .Select(g => g.OrderBy(e => e.AtUtc).ThenBy(e => e.InTargetSeries ? 0 : 1).ThenBy(e => e.RunId).First())
            .OrderBy(e => e.AtUtc)
            .ThenBy(e => e.RunId)
            .ThenBy(e => e.Kind, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The one price card every compared run is costed at: the current pricing of the subject's latest
    /// configuration, falling back to the latest run's own pricing (its snapshot when recorded).
    /// </summary>
    public async Task<(ChatConsistencyPriceCard Card, ModelPricing? Pricing)> ResolvePriceCardAsync(BenchmarkRun latest)
    {
        ArgumentNullException.ThrowIfNull(latest);
        ModelPricing? pricing = null;
        string source = "none";

        if (_pricingService != null)
        {
            pricing = await _pricingService.ResolveForConfigurationAsync(
                latest.TestedModelConfigurationId, latest.TestedModelSnapshot?.Provider, latest.TestedModelSnapshot?.ModelId);
            if (pricing != null)
            {
                source = "current configuration pricing";
            }
            else
            {
                var runPricing = await _pricingService.ResolveForRunAsync(latest);
                pricing = runPricing.Candidate;
                if (pricing != null) source = runPricing.IsSnapshot ? "latest run's pricing snapshot" : "latest run's catalog pricing";
            }
        }
        else
        {
            pricing = SnapshotCandidateCard(latest.PricingSnapshotJson);
            if (pricing != null) source = "latest run's pricing snapshot";
        }

        var card = new ChatConsistencyPriceCard
        {
            Available = pricing != null,
            Source = source,
            RunId = latest.Id,
            InputPerMillion = pricing?.InputPerMillion,
            OutputPerMillion = pricing?.OutputPerMillion,
            CachedInputPerMillion = pricing?.CachedInputPerMillion,
            CacheWritePerMillion = pricing?.CacheWritePerMillion,
            AsOf = pricing?.AsOf
        };
        return (card, pricing);
    }

    /// <summary>The candidate card of a run's pricing snapshot, flat rates only; null when absent or malformed.</summary>
    public static ModelPricing? SnapshotCandidateCard(string? pricingSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(pricingSnapshotJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(pricingSnapshotJson);
            if (!doc.RootElement.TryGetProperty("candidate", out var c) || c.ValueKind != JsonValueKind.Object) return null;
            decimal? Dec(string name) => c.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() : null;
            if (Dec("inputPerMillion") is not decimal input || Dec("outputPerMillion") is not decimal output) return null;
            return new ModelPricing(input, output, Dec("cachedInputPerMillion"), Dec("cacheWritePerMillion"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // --- API queries -----------------------------------------------------------------------------

    /// <summary>Every model axis with usable runs, newest activity first.</summary>
    public async Task<IReadOnlyList<ChatConsistencyModelAxis>> ListModelAxesAsync(CancellationToken ct = default)
    {
        var statuses = ChatConsistencyMeasures.UsableStatuses.ToList();
        var runs = await _db.BenchmarkRuns.AsNoTracking().Where(r => statuses.Contains(r.Status)).ToListAsync(ct);
        var axisOf = runs.ToDictionary(r => r.Id, ChatConsistencyComparability.ModelAxisKey);

        // Battery runs per axis: those with a non-superseded member on it, whatever the member run's status.
        var memberships = await _db.BenchmarkBatteryRunMembers.AsNoTracking()
            .Where(m => !m.Superseded)
            .Select(m => new { m.BenchmarkBatteryRunId, m.BenchmarkRunId })
            .ToListAsync(ct);
        var unknown = memberships.Select(m => m.BenchmarkRunId).Where(id => !axisOf.ContainsKey(id)).Distinct().ToList();
        if (unknown.Count > 0)
        {
            foreach (var run in await _db.BenchmarkRuns.AsNoTracking().Where(r => unknown.Contains(r.Id)).ToListAsync(ct))
            {
                axisOf[run.Id] = ChatConsistencyComparability.ModelAxisKey(run);
            }
        }

        var batteryRunCounts = memberships
            .Where(m => axisOf.ContainsKey(m.BenchmarkRunId))
            .GroupBy(m => axisOf[m.BenchmarkRunId], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(m => m.BenchmarkBatteryRunId).Distinct().Count(), StringComparer.Ordinal);

        return runs
            .GroupBy(r => axisOf[r.Id], StringComparer.Ordinal)
            .Select(g =>
            {
                var ordered = g.OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id).ToList();
                var latest = ordered[^1];
                var subject = ChatConsistencyMeasures.SubjectOf(g.Key, latest);
                return new ChatConsistencyModelAxis
                {
                    Key = g.Key,
                    DisplayName = subject.DisplayName,
                    Provider = subject.Provider,
                    ModelId = subject.ModelId,
                    ThinkingLevel = subject.ThinkingLevel,
                    ServiceTier = subject.ServiceTier,
                    RunCount = ordered.Count,
                    TelemetryRunCount = ordered.Count(r => r.CallTelemetryVersion.HasValue),
                    FirstRunAtUtc = ChatConsistencyMeasures.AsUtc(ordered[0].StartedAtUtc),
                    LastRunAtUtc = ChatConsistencyMeasures.AsUtc(latest.StartedAtUtc),
                    LatestRunId = latest.Id,
                    SuiteNames = ordered.Select(r => r.SuiteName ?? string.Empty).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList(),
                    BatteryRunCount = batteryRunCounts.TryGetValue(g.Key, out int batteryRuns) ? batteryRuns : 0
                };
            })
            .OrderByDescending(a => a.LastRunAtUtc)
            .ThenBy(a => a.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// One point per usable run of the subject between the bounds (inclusive, either may be null), and one
    /// per battery run of any definition started between them with a non-superseded member on the subject's
    /// axis, with the subject's events and annotations.
    /// </summary>
    public async Task<ChatConsistencyTimeline> GetTimelineAsync(string modelKey, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);
        var runs = await LoadSubjectRunsAsync(modelKey, fromUtc, toUtc, includeTelemetry: true, ct);
        var latest = runs.LastOrDefault();
        var subject = ChatConsistencyMeasures.SubjectOf(modelKey, latest);

        // The battery runs' members come from the runs in the dates, and any member started after them.
        var (batteryStates, memberHeaders) = await LoadBatteryStatesAsync(modelKey, q => StartedBetween(q, fromUtc, toUtc), ct);
        batteryStates = batteryStates.Where(s => s.OnAxis).ToList();
        var loaded = runs.Select(r => r.Id).ToHashSet();
        var later = await LoadFullAsync(
            batteryStates.SelectMany(s => s.MemberRunIds).Where(id => !loaded.Contains(id)).Distinct().ToList(), includeTelemetry: true, ct);
        var runById = runs.Concat(later).ToDictionary(r => r.Id);
        var calibrations = await LoadCalibrationsAsync(runById.Keys.ToList(), ct);

        var (priceCard, pricing) = latest == null
            ? (new ChatConsistencyPriceCard { Available = false, Source = "none" }, (ModelPricing?)null)
            : await ResolvePriceCardAsync(latest);

        var points = runs.Select(run => TimelinePoint(run, calibrations.Where(c => c.BenchmarkRunId == run.Id).ToList(), pricing)).ToList();
        var overallIndexes = await BatteryOverallIndexesAsync(batteryStates, ct);
        var batteryPoints = batteryStates
            .Select(s => BatteryTimelinePoint(s, modelKey, runById, memberHeaders, calibrations, pricing, overallIndexes[s.Row.Id]))
            .ToList();

        var events = runs.Count == 0
            ? new List<ChatConsistencyEventView>()
            : ChatConsistencyComparability.DetectOverseerEvents(runs).Select(e => ChatConsistencyMeasures.EventView(e, true)).ToList();

        var annotations = new List<ChatConsistencyAnnotationView>();
        if (runs.Count > 0)
        {
            DateTime from = fromUtc ?? runs[0].StartedAtUtc;
            DateTime to = toUtc ?? runs[^1].StartedAtUtc;
            annotations = (await _db.ChatConsistencyAnnotations.AsNoTracking().Where(a => a.AtUtc >= from && a.AtUtc <= to).ToListAsync(ct))
                .Where(a => ChatConsistencyMeasures.AnnotationApplies(a, subject.Provider, subject.ModelId))
                .OrderBy(a => a.AtUtc)
                .ThenBy(a => a.Id)
                .Select(ChatConsistencyMeasures.AnnotationView)
                .ToList();
        }

        return new ChatConsistencyTimeline
        {
            Subject = subject,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Points = points,
            BatteryPoints = batteryPoints,
            Events = events,
            Annotations = annotations,
            PriceCard = priceCard
        };
    }

    /// <summary>
    /// One row per usable run of the subject between the bounds: eligibility and segment per axis, re-grade
    /// coverage, controls, served model, suite, and the run's battery.
    /// </summary>
    public async Task<IReadOnlyList<ChatConsistencyRunRow>> GetRunTableAsync(string modelKey, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);
        var runs = await LoadSubjectRunsAsync(modelKey, fromUtc, toUtc, includeTelemetry: false, ct);
        return await BuildRunRowsAsync(modelKey, runs, fromUtc, toUtc, ct);
    }

    /// <summary>
    /// One row per battery run started between the bounds with a non-superseded member on the subject's
    /// axis, newest first: completeness, the usable members as run-table rows in suite order, and per axis
    /// the members' eligibility combined.
    /// </summary>
    public async Task<IReadOnlyList<ChatConsistencyBatteryRunRow>> GetBatteryRunTableAsync(
        string modelKey, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);
        var (states, _) = await LoadBatteryStatesAsync(modelKey, q => StartedBetween(q, fromUtc, toUtc), ct);
        states = states.Where(s => s.OnAxis).ToList();
        if (states.Count == 0) return Array.Empty<ChatConsistencyBatteryRunRow>();

        // The members' rows come from the run table over the subject's runs in the dates and any member started after them.
        var runs = await LoadSubjectRunsAsync(modelKey, fromUtc, toUtc, includeTelemetry: false, ct);
        var loaded = runs.Select(r => r.Id).ToHashSet();
        var later = await LoadFullAsync(states.SelectMany(s => s.MemberRunIds).Where(id => !loaded.Contains(id)).Distinct().ToList(), includeTelemetry: false, ct);
        var rowOf = (await BuildRunRowsAsync(modelKey, Ordered(runs.Concat(later)), fromUtc, toUtc, ct)).ToDictionary(r => r.RunId);

        return states
            .OrderByDescending(s => s.Row.StartedAtUtc)
            .ThenByDescending(s => s.Row.Id)
            .Select(s => BatteryRunRow(s, rowOf))
            .ToList();
    }

    /// <summary>
    /// The batteries and suites the subject can be compared within over the dates, from the battery-run and
    /// run tables: battery sets first, then suite sets, each group newest first. A battery set counts every
    /// listed battery run, complete or not, and the distinct usable members behind them; a suite set counts
    /// the subject's runs of the suite, battery members included.
    /// </summary>
    public async Task<ChatConsistencyComparisonSets> GetComparisonSetsAsync(
        string modelKey, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);
        var batteryRows = await GetBatteryRunTableAsync(modelKey, fromUtc, toUtc, ct);
        var runs = await LoadSubjectHeadersAsync(modelKey, fromUtc, toUtc, ct);

        var batterySets = batteryRows
            .GroupBy(b => b.SetKey, StringComparer.Ordinal)
            .Select(g =>
            {
                var newest = g.OrderByDescending(b => b.StartedAtUtc).ThenByDescending(b => b.BatteryRunId).First();
                return new ChatConsistencyComparisonSet
                {
                    Kind = ChatConsistencyComparisonSetKinds.Battery,
                    Key = g.Key,
                    Label = BatteryLabel(newest.BatteryName, g.Select(b => b.DefinitionRevision)),
                    UnitCount = g.Count(),
                    MemberRunCount = g.SelectMany(b => b.Members).Select(m => m.RunId).Distinct().Count(),
                    LatestStartedAtUtc = newest.StartedAtUtc
                };
            });

        var suiteSets = runs
            .GroupBy(ChatConsistencyMeasures.SuiteIdentity, StringComparer.Ordinal)
            .Select(g =>
            {
                var newest = g.OrderByDescending(r => r.StartedAtUtc).ThenByDescending(r => r.Id).First();
                return new ChatConsistencyComparisonSet
                {
                    Kind = ChatConsistencyComparisonSetKinds.Suite,
                    Key = ChatConsistencyComparisonSetKinds.SuiteKeyPrefix + g.Key,
                    Label = string.IsNullOrWhiteSpace(newest.SuiteName) ? g.Key : newest.SuiteName,
                    UnitCount = g.Count(),
                    MemberRunCount = g.Count(),
                    LatestStartedAtUtc = ChatConsistencyMeasures.AsUtc(newest.StartedAtUtc)
                };
            });

        static IEnumerable<ChatConsistencyComparisonSet> NewestFirst(IEnumerable<ChatConsistencyComparisonSet> sets)
            => sets.OrderByDescending(s => s.LatestStartedAtUtc).ThenBy(s => s.Key, StringComparer.Ordinal);

        var newestRun = runs.OrderByDescending(r => r.StartedAtUtc).ThenByDescending(r => r.Id).FirstOrDefault();
        return new ChatConsistencyComparisonSets
        {
            Sets = NewestFirst(batterySets).Concat(NewestFirst(suiteSets)).ToList(),
            DefaultKey = batteryRows.Count > 0
                ? batteryRows[0].SetKey
                : newestRun == null ? null : ChatConsistencyComparisonSetKinds.SuiteKeyPrefix + ChatConsistencyMeasures.SuiteIdentity(newestRun)
        };
    }

    /// <summary>
    /// The battery-run row of <paramref name="state"/>: its members' rows from <paramref name="rowOf"/> with
    /// this battery run's position, and per axis eligible when every member is, otherwise the ineligible
    /// members' reasons, each prefixed <c>#&lt;run id&gt;: </c>.
    /// </summary>
    private static ChatConsistencyBatteryRunRow BatteryRunRow(BatteryRunState state, IReadOnlyDictionary<long, ChatConsistencyRunRow> rowOf)
    {
        var row = state.Row;
        int suiteCount = state.Definition?.Suites.Count ?? 0;
        var members = state.Usable
            .Where(u => rowOf.ContainsKey(u.Run.Id))
            .Select(u => rowOf[u.Run.Id] with
            {
                BatteryRunId = row.Id,
                BatteryName = row.BatteryName,
                BatterySuitePosition = u.Member.SuiteIndex + 1,
                BatterySuiteCount = suiteCount > 0 ? suiteCount : null
            })
            .ToList();

        var eligibility = Enum.GetValues<ChatConsistencyAxis>().Select(axis =>
        {
            var perMember = members.Select(m => (m.RunId, Eligibility: m.Eligibility.FirstOrDefault(e => e.Axis == axis))).ToList();
            bool eligible = perMember.Count > 0 && perMember.All(p => p.Eligibility?.Eligible == true);
            var segments = perMember.Select(p => p.Eligibility?.Segment).Distinct().ToList();
            string reason = string.Join("; ", perMember
                .Where(p => p.Eligibility?.Eligible != true)
                .Select(p => "#" + Inv(p.RunId) + ": " + (p.Eligibility?.Reason ?? "not eligible")));
            return new ChatConsistencyAxisEligibility
            {
                Axis = axis,
                Eligible = eligible,
                Segment = eligible && segments.Count == 1 ? segments[0] : null,
                Reason = reason.Length == 0 ? null : reason
            };
        }).ToList();

        return new ChatConsistencyBatteryRunRow
        {
            BatteryRunId = row.Id,
            BatteryId = row.BenchmarkBatteryId,
            BatteryName = row.BatteryName,
            DefinitionSha256 = row.DefinitionSha256,
            DefinitionRevision = state.Definition?.Revision ?? 0,
            SetKey = ChatConsistencyComparisonSetKinds.BatteryKeyPrefix + row.DefinitionSha256,
            StartedAtUtc = ChatConsistencyMeasures.AsUtc(row.StartedAtUtc),
            CompletedAtUtc = row.CompletedAtUtc.HasValue ? ChatConsistencyMeasures.AsUtc(row.CompletedAtUtc.Value) : null,
            Status = row.Status,
            SuiteCount = suiteCount,
            Complete = state.Complete,
            IncompleteReason = state.IncompleteReason,
            HarnessVersions = members
                .Select(m => m.HarnessVersion)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList(),
            Members = members,
            Eligibility = eligibility
        };
    }

    private const string NoBatteryAnalysisNote = "No battery analysis. Compute it from the battery report.";
    private const string StaleBatteryAnalysisNote = "The battery analysis was computed over other member runs. Recompute it from the battery report.";
    private const string NoOverallIndexNote = "The stored battery analysis has no Overall Index. Recompute it from the battery report.";

    /// <summary>
    /// The timeline point of <paramref name="state"/>: the measures pooled over its usable members' answers
    /// (<see cref="PooledPoint"/>), identified by the battery run and labeled with the battery's name and
    /// revision. Without a usable member, the status is the newest on-axis member's.
    /// </summary>
    private static ChatConsistencyBatteryTimelinePoint BatteryTimelinePoint(
        BatteryRunState state,
        string modelKey,
        IReadOnlyDictionary<long, BenchmarkRun> runById,
        IReadOnlyDictionary<long, BenchmarkRun> memberHeaders,
        IReadOnlyList<BenchmarkAssessorCalibration> calibrations,
        ModelPricing? pricing,
        (double? Index, string? Note) overall)
    {
        var row = state.Row;
        var memberIds = state.MemberRunIds.ToList();
        var members = memberIds.Where(runById.ContainsKey).Select(id => runById[id]).ToList();
        var memberSet = memberIds.ToHashSet();
        var pooled = PooledPoint(members, calibrations.Where(c => memberSet.Contains(c.BenchmarkRunId)).ToList(), pricing);

        var status = pooled.Status;
        if (members.Count == 0)
        {
            var newestOnAxis = (row.Members ?? new List<BenchmarkBatteryRunMember>())
                .Where(m => !m.Superseded && memberHeaders.ContainsKey(m.BenchmarkRunId))
                .Select(m => memberHeaders[m.BenchmarkRunId])
                .Where(r => ChatConsistencyComparability.ModelAxisKey(r) == modelKey)
                .OrderBy(r => r.StartedAtUtc)
                .ThenBy(r => r.Id)
                .LastOrDefault();
            if (newestOnAxis != null) status = newestOnAxis.Status;
        }

        int revision = state.Definition?.Revision ?? 0;
        return new ChatConsistencyBatteryTimelinePoint(pooled)
        {
            RunId = row.Id,
            StartedAtUtc = ChatConsistencyMeasures.AsUtc(row.StartedAtUtc),
            SuiteName = BatteryLabel(row.BatteryName, new[] { revision }),
            SuiteId = null,
            Status = status,
            QualityIndex = null,
            IsAnchor = false,
            SetKey = ChatConsistencyComparisonSetKinds.BatteryKeyPrefix + row.DefinitionSha256,
            BatteryName = row.BatteryName,
            DefinitionRevision = state.Definition?.Revision,
            CompletedAtUtc = row.CompletedAtUtc.HasValue ? ChatConsistencyMeasures.AsUtc(row.CompletedAtUtc.Value) : null,
            BatteryStatus = row.Status,
            SuiteCount = state.Definition?.Suites.Count ?? 0,
            Complete = state.Complete,
            IncompleteReason = state.IncompleteReason,
            MemberRunIds = memberIds,
            OverallIndex = overall.Index,
            OverallIndexNote = overall.Note
        };
    }

    /// <summary>
    /// Each battery run's Overall Index, or why it has none. A complete battery run reads its latest stored
    /// battery analysis, by computation time, then id (<see cref="BenchmarkBatteryAnalysisService.GetLatestAsync"/>),
    /// when that analysis is current over the usable members on the axis and complete; an incomplete battery
    /// run has none. Two queries for all the battery runs.
    /// </summary>
    private async Task<Dictionary<long, (double? Index, string? Note)>> BatteryOverallIndexesAsync(
        IReadOnlyList<BatteryRunState> states, CancellationToken ct)
    {
        var ids = states.Where(s => s.Complete).Select(s => s.Row.Id).Distinct().ToList();
        var latest = new Dictionary<long, BenchmarkBatteryAnalysis>();
        if (ids.Count > 0)
        {
            var heads = await _db.BenchmarkBatteryAnalyses.AsNoTracking()
                .Where(a => ids.Contains(a.BenchmarkBatteryRunId))
                .Select(a => new { a.Id, a.BenchmarkBatteryRunId, a.ComputedAtUtc })
                .ToListAsync(ct);
            var latestIds = heads
                .GroupBy(a => a.BenchmarkBatteryRunId)
                .Select(g => g.OrderByDescending(a => a.ComputedAtUtc).ThenByDescending(a => a.Id).First().Id)
                .ToList();
            if (latestIds.Count > 0)
            {
                latest = await _db.BenchmarkBatteryAnalyses.AsNoTracking()
                    .Where(a => latestIds.Contains(a.Id))
                    .ToDictionaryAsync(a => a.BenchmarkBatteryRunId, ct);
            }
        }

        var result = new Dictionary<long, (double? Index, string? Note)>();
        foreach (var state in states)
        {
            result[state.Row.Id] = OverallIndexOf(state, latest.GetValueOrDefault(state.Row.Id));
        }

        return result;
    }

    /// <summary>The Overall Index of <paramref name="state"/> from <paramref name="analysis"/>, its latest stored analysis, or why there is none.</summary>
    private static (double? Index, string? Note) OverallIndexOf(BatteryRunState state, BenchmarkBatteryAnalysis? analysis)
    {
        if (!state.Complete) return (null, "The battery run is incomplete (" + state.IncompleteReason + "), so it has no Overall Index.");
        if (analysis == null) return (null, NoBatteryAnalysisNote);
        if (BenchmarkBatteryAnalysisService.IsStale(state.MemberRunIds.ToList(), analysis)) return (null, StaleBatteryAnalysisNote);

        var overall = analysis.Complete ? BenchmarkBatteryAnalysisService.DeserializeResult(analysis)?.OverallIndex : null;
        return overall == null ? (null, NoOverallIndexNote) : (overall.PointEstimate, null);
    }

    /// <summary>
    /// The run-table rows of <paramref name="runs"/> (the subject's, ordered): segments assessed over these
    /// runs, controls of other subjects on their suites between the bounds widened to the runs, and each
    /// run's battery.
    /// </summary>
    private async Task<List<ChatConsistencyRunRow>> BuildRunRowsAsync(
        string modelKey, List<BenchmarkRun> runs, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct)
    {
        if (runs.Count == 0) return new List<ChatConsistencyRunRow>();

        var calibrations = await LoadCalibrationsAsync(runs.Select(r => r.Id).ToList(), ct);
        var batteryOf = await BatteryOfRunsAsync(runs.Select(r => r.Id).ToList(), ct);

        DateTime from = fromUtc.HasValue && fromUtc.Value < runs[0].StartedAtUtc ? fromUtc.Value : runs[0].StartedAtUtc;
        DateTime to = toUtc.HasValue && toUtc.Value > runs[^1].StartedAtUtc ? toUtc.Value : runs[^1].StartedAtUtc;
        var statuses = ChatConsistencyMeasures.UsableStatuses.ToList();
        var suites = runs.Select(ChatConsistencyMeasures.SuiteIdentity).ToHashSet(StringComparer.Ordinal);
        var controlHeaders = (await _db.BenchmarkRuns.AsNoTracking()
                .Where(r => statuses.Contains(r.Status) && r.StartedAtUtc >= from && r.StartedAtUtc <= to)
                .ToListAsync(ct))
            .Where(r => ChatConsistencyComparability.ModelAxisKey(r) != modelKey && suites.Contains(ChatConsistencyMeasures.SuiteIdentity(r)))
            .Select(r => r.Id)
            .ToList();
        var controls = await LoadFullAsync(controlHeaders, includeTelemetry: false, ct);

        var assessment = ChatConsistencyComparability.AssessMeasurement(runs);
        var matching = ChatConsistencyComparability.MatchControlRuns(
            new[] { new ChatConsistencyPeriod("all", runs) }, controls);

        return runs.Select(run =>
        {
            var segments = assessment.RunOf(run.Id);
            var exclusions = assessment.SpeedExclusions.Where(x => x.RunId == run.Id).ToList();
            var eligibility = Enum.GetValues<ChatConsistencyAxis>().Select(axis =>
            {
                int? segment = segments?.SegmentOf(axis);
                string? reason = segment.HasValue
                    ? null
                    : string.Join(" ", exclusions.Where(x => x.Axes.Contains(axis)).Select(x => x.Detail));
                return new ChatConsistencyAxisEligibility
                {
                    Axis = axis,
                    Eligible = segment.HasValue,
                    Segment = segment,
                    Reason = string.IsNullOrEmpty(reason) ? null : reason
                };
            }).ToList();

            var coverage = calibrations
                .Where(c => c.BenchmarkRunId == run.Id && c.AssessorModelSnapshotId.HasValue && c.ErrorMessage == null)
                .GroupBy(c => c.AssessorModelSnapshotId!.Value)
                .OrderBy(g => g.Key)
                .Select(g => new ChatConsistencyRegradeCoverage
                {
                    SnapshotId = g.Key,
                    Display = g.First().AssessorModelSnapshot.Label() ?? ("snapshot " + Inv(g.Key)),
                    CalibrationIds = g.Select(c => c.Id).OrderBy(i => i).ToList(),
                    LatestAtUtc = ChatConsistencyMeasures.AsUtc(g.Max(c => c.CreatedAtUtc))
                })
                .ToList();

            var battery = batteryOf.GetValueOrDefault(run.Id);
            return new ChatConsistencyRunRow
            {
                RunId = run.Id,
                StartedAtUtc = ChatConsistencyMeasures.AsUtc(run.StartedAtUtc),
                SuiteName = run.SuiteName ?? string.Empty,
                HarnessVersion = run.HarnessVersion,
                ScoringMethodVersion = run.ScoringMethodVersion,
                Status = run.Status,
                IsLegacy = !run.CallTelemetryVersion.HasValue,
                IsAnchor = run.IsConsistencyAnchor,
                Eligibility = eligibility,
                RegradeCoverage = coverage,
                MatchedControlRunIds = matching.Matches.Where(m => m.TargetRunId == run.Id).Select(m => m.ControlRunId).Distinct().OrderBy(i => i).ToList(),
                ServedModelIds = ChatConsistencyMeasures.ServedModels(run),
                SuiteId = run.BenchmarkSuiteIdUsed ?? run.BenchmarkSuiteId,
                SuiteKey = ChatConsistencyMeasures.SuiteIdentity(run),
                BatteryRunId = battery?.BatteryRunId,
                BatteryName = battery?.BatteryName,
                BatterySuitePosition = battery?.Position,
                BatterySuiteCount = battery?.SuiteCount
            };
        }).ToList();
    }

    /// <summary>A run's battery: the battery run, its name, the run's 1-based suite position and the definition's suite count.</summary>
    private sealed record RunBattery(long BatteryRunId, string BatteryName, int Position, int? SuiteCount);

    /// <summary>
    /// Each run's battery, from one membership query: its newest non-superseded membership, by the battery
    /// run's start, then id (the Run History convention). Runs without one are absent.
    /// </summary>
    private async Task<Dictionary<long, RunBattery>> BatteryOfRunsAsync(List<long> runIds, CancellationToken ct)
    {
        if (runIds.Count == 0) return new Dictionary<long, RunBattery>();

        var rows = await _db.BenchmarkBatteryRunMembers.AsNoTracking()
            .Where(m => !m.Superseded && runIds.Contains(m.BenchmarkRunId))
            .Select(m => new
            {
                m.BenchmarkRunId,
                m.BenchmarkBatteryRunId,
                m.SuiteIndex,
                m.BenchmarkBatteryRun.StartedAtUtc,
                m.BenchmarkBatteryRun.BatteryName,
                m.BenchmarkBatteryRun.DefinitionJson
            })
            .ToListAsync(ct);

        var suiteCounts = new Dictionary<long, int?>();
        foreach (var row in rows)
        {
            if (!suiteCounts.ContainsKey(row.BenchmarkBatteryRunId))
            {
                int? count = TryReadDefinition(row.DefinitionJson)?.Suites.Count;
                suiteCounts[row.BenchmarkBatteryRunId] = count > 0 ? count : null;
            }
        }

        return rows
            .GroupBy(r => r.BenchmarkRunId)
            .ToDictionary(g => g.Key, g =>
            {
                var newest = g.OrderByDescending(r => r.StartedAtUtc).ThenByDescending(r => r.BenchmarkBatteryRunId).First();
                return new RunBattery(newest.BenchmarkBatteryRunId, newest.BatteryName, newest.SuiteIndex + 1, suiteCounts[newest.BenchmarkBatteryRunId]);
            });
    }

    // --- Loading ---------------------------------------------------------------------------------

    private async Task<List<BenchmarkRun>> LoadSubjectRunsAsync(
        string modelKey, DateTime? fromUtc, DateTime? toUtc, bool includeTelemetry, CancellationToken ct)
    {
        var ids = (await LoadSubjectHeadersAsync(modelKey, fromUtc, toUtc, ct)).Select(r => r.Id).ToList();
        return Ordered(await LoadFullAsync(ids, includeTelemetry, ct));
    }

    /// <summary>The usable runs of the subject between the bounds (inclusive, either may be null), without their answers, in a stable order.</summary>
    private async Task<List<BenchmarkRun>> LoadSubjectHeadersAsync(string modelKey, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct)
    {
        var statuses = ChatConsistencyMeasures.UsableStatuses.ToList();
        var query = _db.BenchmarkRuns.AsNoTracking().Where(r => statuses.Contains(r.Status));
        if (fromUtc.HasValue)
        {
            DateTime from = fromUtc.Value;
            query = query.Where(r => r.StartedAtUtc >= from);
        }

        if (toUtc.HasValue)
        {
            DateTime to = toUtc.Value;
            query = query.Where(r => r.StartedAtUtc <= to);
        }

        return Ordered((await query.ToListAsync(ct)).Where(r => ChatConsistencyComparability.ModelAxisKey(r) == modelKey));
    }

    /// <summary>The runs with their answers (and the answers' call telemetry and tool calls), in a stable order.</summary>
    private async Task<List<BenchmarkRun>> LoadFullAsync(List<long> ids, bool includeTelemetry, CancellationToken ct)
    {
        if (ids.Count == 0) return new List<BenchmarkRun>();

        IQueryable<BenchmarkRun> query = _db.BenchmarkRuns.AsNoTracking().AsSplitQuery();
        if (includeTelemetry)
        {
            query = query.Include(r => r.Answers).ThenInclude(a => a.ModelCalls)
                .Include(r => r.Answers).ThenInclude(a => a.ToolCalls);
        }
        else
        {
            query = query.Include(r => r.Answers);
        }

        var runs = await query.Where(r => ids.Contains(r.Id)).ToListAsync(ct);
        foreach (var run in runs)
        {
            run.Answers = run.Answers.OrderBy(a => a.OrderIndex).ThenBy(a => a.Id).ToList();
            foreach (var answer in run.Answers)
            {
                answer.ModelCalls = (answer.ModelCalls ?? new List<ModelCallTelemetry>())
                    .OrderBy(c => c.Source).ThenBy(c => c.CallIndex).ThenBy(c => c.StartedAtUtc).ThenBy(c => c.Id).ToList();
                answer.ToolCalls = (answer.ToolCalls ?? new List<BenchmarkRunAnswerToolCall>())
                    .OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToList();
            }
        }

        return Ordered(runs);
    }

    private async Task<List<BenchmarkAssessorCalibration>> LoadCalibrationsAsync(List<long> runIds, CancellationToken ct)
    {
        if (runIds.Count == 0) return new List<BenchmarkAssessorCalibration>();
        var rows = await _db.BenchmarkAssessorCalibrations.AsNoTracking().Where(c => runIds.Contains(c.BenchmarkRunId)).ToListAsync(ct);
        return rows.OrderBy(c => c.BenchmarkRunId).ThenBy(c => c.CreatedAtUtc).ThenBy(c => c.Id).ToList();
    }

    private static List<BenchmarkRun> Ordered(IEnumerable<BenchmarkRun> runs)
        => runs.OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id).ToList();

    /// <summary>The timeline point of one run.</summary>
    private static ChatConsistencyTimelinePoint TimelinePoint(BenchmarkRun run, List<BenchmarkAssessorCalibration> calibrations, ModelPricing? pricing)
        => PooledPoint(new[] { run }, calibrations, pricing) with
        {
            RunId = run.Id,
            StartedAtUtc = ChatConsistencyMeasures.AsUtc(run.StartedAtUtc),
            SuiteName = run.SuiteName ?? string.Empty,
            SuiteId = run.BenchmarkSuiteIdUsed ?? run.BenchmarkSuiteId,
            IsAnchor = run.IsConsistencyAnchor,
            QualityIndex = run.QualityIndex
        };

    /// <summary>
    /// The measures of <paramref name="runs"/> pooled over the union of their answers: medians, means and
    /// rates over every member's answers together, each answer costed with its own run, and the refusal rate
    /// over the answers of the members with call telemetry. Legacy only when every member is; the latency
    /// label <c>mixed</c> when members differ. Common-grader quality only for a snapshot calibrated on every
    /// member: the item-weighted mean of the members' figures, their item counts summed, the newest
    /// calibration's id and time. Served models summed per model, strata united, the most parallel questions,
    /// and the harness version and status of the newest member. The run, start, suite, quality index and
    /// anchor are left for the caller.
    /// </summary>
    private static ChatConsistencyTimelinePoint PooledPoint(
        IReadOnlyList<BenchmarkRun> runs, IReadOnlyList<BenchmarkAssessorCalibration> calibrations, ModelPricing? pricing)
    {
        var pairs = runs.SelectMany(r => r.Answers.Select(a => (Run: r, Answer: a))).ToList();
        var answers = pairs.Select(p => p.Answer).ToList();
        var delivered = answers.Where(ChatConsistencyMeasures.IsDelivered).ToList();
        int legacyRuns = runs.Count(r => !r.CallTelemetryVersion.HasValue);
        bool legacy = runs.Count > 0 && legacyRuns == runs.Count;
        var telemetryAnswers = pairs.Where(p => p.Run.CallTelemetryVersion.HasValue).Select(p => p.Answer).ToList();
        var timings = runs.SelectMany(r => ChatConsistencyMeasures.AnswerTimings(r).Values).ToList();
        var newest = runs.OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id).LastOrDefault();

        var native = answers.Select(ChatConsistencyMeasures.NativeQuality).Where(q => q.HasValue).Select(q => q!.Value).ToList();
        var ttfat = delivered.Select(CallTelemetryMeasures.TimeToFirstAnswerTextMs).Where(v => v.HasValue).Select(v => (double)v!.Value).ToList();
        var rates = delivered.Select(CallTelemetryMeasures.AnswerStreamingRate).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var modelTimes = delivered.Select(a => (double)a.ModelTimeMs).ToList();
        var outputs = delivered.Where(a => a.OutputTokens.HasValue).Select(a => (double)a.OutputTokens!.Value).ToList();
        var costs = pairs.Select(p => ChatConsistencyMeasures.AnswerCost(p.Answer, p.Run, pricing)).Where(c => c.HasValue).Select(c => (double)c!.Value).ToList();

        var perRun = runs.Select(r => CommonGraderPoints(calibrations.Where(c => c.BenchmarkRunId == r.Id))).ToList();
        var common = perRun.Count == 1
            ? perRun[0]
            : perRun.Count == 0
                ? new List<ChatConsistencyCommonGraderPoint>()
                : perRun[0]
                    .Select(p => p.SnapshotId)
                    .Where(id => perRun.All(points => points.Any(p => p.SnapshotId == id)))
                    .Select(id => MergedGraderPoint(perRun.Select(points => points.First(p => p.SnapshotId == id)).ToList()))
                    .ToList();

        var served = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var count in runs.SelectMany(r => ChatConsistencyMeasures.ServedModels(r)))
        {
            served[count.ModelId] = served.TryGetValue(count.ModelId, out int known) ? known + count.CallCount : count.CallCount;
        }

        static double? Rate(List<BenchmarkRunAnswer> pool, Func<BenchmarkRunAnswer, bool> predicate)
            => pool.Count == 0 ? null : pool.Count(predicate) / (double)pool.Count;

        return new ChatConsistencyTimelinePoint
        {
            HarnessVersion = newest?.HarnessVersion,
            Status = newest?.Status ?? default,
            IsLegacy = legacy,
            NativeMeanQuality = native.Count > 0 ? native.Average() : null,
            CommonGraderQuality = common,
            MedianTimeToFirstAnswerTextMs = BenchmarkGroupStatistics.Median(ttfat),
            MedianStreamingRate = BenchmarkGroupStatistics.Median(rates.Select(r => r.TokensPerSecond).ToList()),
            StreamingRateEstimated = rates.Any(r => r.Estimated),
            MedianModelTimeMs = BenchmarkGroupStatistics.Median(modelTimes),
            LatencyLabel = legacyRuns == 0 ? "telemetry" : legacy ? "legacy proxy" : "mixed",
            OutputTokensPerAnswer = outputs.Count > 0 ? outputs.Average() : null,
            ToolCallsPerAnswer = delivered.Count > 0 ? delivered.Average(a => (double)ChatConsistencyMeasures.ToolCalls(a)) : null,
            CostPerQuestionUsd = costs.Count > 0 ? costs.Average() : null,
            TerminalFailureRate = Rate(answers, ChatConsistencyMeasures.IsTerminalFailure),
            TimeoutRate = Rate(answers, ChatConsistencyMeasures.IsTimeout),
            EmptyAnswerRate = Rate(answers, ChatConsistencyMeasures.IsEmptyAnswer),
            RefusalRate = legacy ? null : Rate(telemetryAnswers, ChatConsistencyMeasures.IsRefusal),
            ToolBudgetExhaustedRate = Rate(answers, ChatConsistencyMeasures.IsToolBudgetExhausted),
            ServedModelIds = served.Select(p => new ChatConsistencyServedModelCount(p.Key, p.Value)).ToList(),
            Strata = timings.Select(t => t.Stratum).Distinct().OrderBy(s => s).Select(ChatConsistencyStatistics.StratumLabel).ToList(),
            StrataEstimated = timings.Any(t => t.Estimated),
            AnswerCount = answers.Count,
            MaxParallelQuestions = runs.Count > 0 ? runs.Max(r => r.MaxParallelQuestionsUsed) : 0
        };
    }

    /// <summary>
    /// One run's common-grader quality per assessor snapshot, ordered by snapshot: the latest error-free
    /// calibration's mean verdict quality over the answers it graded; a snapshot without verdicts is left out.
    /// </summary>
    private static List<ChatConsistencyCommonGraderPoint> CommonGraderPoints(IEnumerable<BenchmarkAssessorCalibration> calibrations)
        => calibrations
            .Where(c => c.AssessorModelSnapshotId.HasValue && c.ErrorMessage == null)
            .GroupBy(c => c.AssessorModelSnapshotId!.Value)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var latest = g.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id).Last();
                var verdicts = ChatConsistencyMeasures.CalibrationVerdicts(latest);
                return verdicts.Count == 0
                    ? null
                    : new ChatConsistencyCommonGraderPoint
                    {
                        SnapshotId = g.Key,
                        Display = latest.AssessorModelSnapshot.Label() ?? ("snapshot " + Inv(g.Key)),
                        CalibrationId = latest.Id,
                        CalibratedAtUtc = ChatConsistencyMeasures.AsUtc(latest.CreatedAtUtc),
                        MeanQuality = verdicts.Values.Average(v => v.Quality),
                        ItemCount = verdicts.Count
                    };
            })
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();

    /// <summary>
    /// One snapshot's common-grader quality over several members: the mean over all their graded items
    /// (<c>Σ mean·items / Σ items</c>), the item counts summed, and the newest calibration's id and time.
    /// </summary>
    private static ChatConsistencyCommonGraderPoint MergedGraderPoint(IReadOnlyList<ChatConsistencyCommonGraderPoint> parts)
    {
        var newest = parts.OrderBy(p => p.CalibratedAtUtc).ThenBy(p => p.CalibrationId).Last();
        int items = parts.Sum(p => p.ItemCount);
        return newest with
        {
            MeanQuality = parts.Sum(p => p.MeanQuality * p.ItemCount) / items,
            ItemCount = items
        };
    }

    private static ChatConsistencyNote Note(string kind, string text) => new() { Kind = kind, Text = text };

    private static string Inv(long value) => value.ToString(CultureInfo.InvariantCulture);
}
