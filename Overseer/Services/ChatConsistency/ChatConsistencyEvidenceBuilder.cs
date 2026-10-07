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
    /// then id. Empty unless the request gave both periods' runs explicitly.
    /// </summary>
    public IReadOnlyList<ChatConsistencyUnanalyzedRun> UnanalyzedRuns { get; init; } = Array.Empty<ChatConsistencyUnanalyzedRun>();

    public IEnumerable<BenchmarkRun> TargetRuns => BaselineRuns.Concat(ComparisonRuns);

    /// <summary>The measurement segmentation of target and control runs, with grading bridged for <paramref name="commonGraderCovers"/>.</summary>
    public ComparabilityAssessment Assess(IEnumerable<long>? commonGraderCovers)
        => ChatConsistencyComparability.AssessMeasurement(TargetRuns.Concat(ControlRuns), commonGraderCovers);
}

/// <summary>
/// Loads the stored runs, calibrations and annotations a chat consistency analysis reads, and answers the
/// read-only queries the API serves (model axes, timeline, run table). Reads only; spends nothing.
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
    /// inside each period (inclusive bounds), or exactly the explicit ids when given; candidate controls
    /// are the explicit control ids, or every other subject's usable run on a target suite in either period.
    /// </summary>
    public async Task<ChatConsistencyEvidence> LoadAsync(ChatConsistencyAnalysisRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var notes = new List<ChatConsistencyNote>();
        DateTime bs = request.BaselineStartUtc, be = request.BaselineEndUtc;
        DateTime cs = request.ComparisonStartUtc, ce = request.ComparisonEndUtc;
        var explicitBaseline = request.BaselineRunIds?.Distinct().ToHashSet();
        var explicitComparison = request.ComparisonRunIds?.Distinct().ToHashSet();
        var explicitControls = request.ControlRunIds?.Distinct().ToHashSet();
        var explicitAll = new HashSet<long>();
        if (explicitBaseline != null) explicitAll.UnionWith(explicitBaseline);
        if (explicitComparison != null) explicitAll.UnionWith(explicitComparison);
        if (explicitControls != null) explicitAll.UnionWith(explicitControls);
        var explicitIds = explicitAll.ToList();

        var statuses = ChatConsistencyMeasures.UsableStatuses.ToList();
        var headers = await _db.BenchmarkRuns
            .AsNoTracking()
            .Where(r => explicitIds.Contains(r.Id)
                        || (statuses.Contains(r.Status)
                            && ((r.StartedAtUtc >= bs && r.StartedAtUtc <= be) || (r.StartedAtUtc >= cs && r.StartedAtUtc <= ce))))
            .ToListAsync(ct);

        var keyOf = headers.ToDictionary(r => r.Id, ChatConsistencyComparability.ModelAxisKey);
        bool Usable(BenchmarkRun r) => statuses.Contains(r.Status);
        bool InWindow(BenchmarkRun r, DateTime start, DateTime end) => r.StartedAtUtc >= start && r.StartedAtUtc <= end;

        List<long> PickTargets(HashSet<long>? explicitSet, DateTime start, DateTime end, string period)
        {
            if (explicitSet == null)
            {
                return headers
                    .Where(r => Usable(r) && InWindow(r, start, end) && keyOf[r.Id] == request.SubjectModelKey)
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
                else
                {
                    picked.Add(id);
                }
            }

            return picked;
        }

        var baselineIds = PickTargets(explicitBaseline, bs, be, "baseline");
        var comparisonIds = PickTargets(explicitComparison, cs, ce, "comparison").Where(id => !baselineIds.Contains(id)).ToList();
        var targetIds = baselineIds.Concat(comparisonIds).ToHashSet();
        var targetSuites = headers.Where(r => targetIds.Contains(r.Id)).Select(ChatConsistencyMeasures.SuiteIdentity).ToHashSet(StringComparer.Ordinal);

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

        var unanalyzed = explicitBaseline != null && explicitComparison != null
            ? await ClassifyUnanalyzedAsync(
                request,
                headers.Where(r => Usable(r) && keyOf[r.Id] == request.SubjectModelKey && !targetIds.Contains(r.Id)),
                notes,
                ct)
            : new List<ChatConsistencyUnanalyzedRun>();

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
            UnanalyzedRuns = unanalyzed
        };
    }

    /// <summary>
    /// The <paramref name="candidates"/> inside a period (the baseline's window first), each with the
    /// first reason of <see cref="ChatConsistencyUnanalyzedReasons.All"/> that applies, ordered by start,
    /// then id. A first or last run of the selection that is not found is ignored with a
    /// <c>runSelection</c> note.
    /// </summary>
    private async Task<List<ChatConsistencyUnanalyzedRun>> ClassifyUnanalyzedAsync(
        ChatConsistencyAnalysisRequest request, IEnumerable<BenchmarkRun> candidates, List<ChatConsistencyNote> notes, CancellationToken ct)
    {
        var selection = request.RunSelection;
        var leftOut = (selection?.LeftOutRunIds ?? Array.Empty<long>()).ToHashSet();
        var markIds = new[] { selection?.FirstRunId, selection?.LastRunId }
            .Where(i => i.HasValue)
            .Select(i => i!.Value)
            .Distinct()
            .ToList();
        var markStarts = markIds.Count == 0
            ? new Dictionary<long, DateTime>()
            : (await _db.BenchmarkRuns.AsNoTracking()
                    .Where(r => markIds.Contains(r.Id))
                    .Select(r => new { r.Id, r.StartedAtUtc })
                    .ToListAsync(ct))
                .ToDictionary(r => r.Id, r => r.StartedAtUtc);

        (DateTime Start, long Id)? Mark(long? id, string which)
        {
            if (!id.HasValue) return null;
            if (markStarts.TryGetValue(id.Value, out var start)) return (start, id.Value);
            notes.Add(Note("runSelection", "The " + which + " run of the selection, #" + Inv(id.Value) + ", was not found."));
            return null;
        }

        var first = Mark(selection?.FirstRunId, "first");
        var last = Mark(selection?.LastRunId, "last");

        static int Order(BenchmarkRun run, (DateTime Start, long Id) mark)
        {
            int byStart = run.StartedAtUtc.CompareTo(mark.Start);
            return byStart != 0 ? byStart : run.Id.CompareTo(mark.Id);
        }

        string Reason(BenchmarkRun run)
        {
            if (leftOut.Contains(run.Id)) return ChatConsistencyUnanalyzedReasons.LeftOut;
            if ((selection?.RangeFromUtc is DateTime rangeFrom && run.StartedAtUtc < rangeFrom)
                || (selection?.RangeToUtc is DateTime rangeTo && run.StartedAtUtc > rangeTo))
            {
                return ChatConsistencyUnanalyzedReasons.OutsideDateRange;
            }

            if (first is { } f && Order(run, f) < 0) return ChatConsistencyUnanalyzedReasons.BeforeFirstRun;
            if (last is { } l && Order(run, l) > 0) return ChatConsistencyUnanalyzedReasons.AfterLastRun;
            return ChatConsistencyUnanalyzedReasons.NotSelected;
        }

        var list = new List<ChatConsistencyUnanalyzedRun>();
        foreach (var run in candidates.OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id))
        {
            string? period = run.StartedAtUtc >= request.BaselineStartUtc && run.StartedAtUtc <= request.BaselineEndUtc ? "baseline"
                : run.StartedAtUtc >= request.ComparisonStartUtc && run.StartedAtUtc <= request.ComparisonEndUtc ? "comparison"
                : null;
            if (period == null) continue;

            list.Add(new ChatConsistencyUnanalyzedRun
            {
                RunId = run.Id,
                Period = period,
                StartedAtUtc = ChatConsistencyMeasures.AsUtc(run.StartedAtUtc),
                Reason = Reason(run)
            });
        }

        return list;
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

        return runs
            .GroupBy(ChatConsistencyComparability.ModelAxisKey, StringComparer.Ordinal)
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
                    SuiteNames = ordered.Select(r => r.SuiteName ?? string.Empty).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList()
                };
            })
            .OrderByDescending(a => a.LastRunAtUtc)
            .ThenBy(a => a.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>One point per usable run of the subject between the bounds (inclusive, either may be null), with the subject's events and annotations.</summary>
    public async Task<ChatConsistencyTimeline> GetTimelineAsync(string modelKey, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);
        var runs = await LoadSubjectRunsAsync(modelKey, fromUtc, toUtc, includeTelemetry: true, ct);
        var latest = runs.LastOrDefault();
        var subject = ChatConsistencyMeasures.SubjectOf(modelKey, latest);
        var calibrations = await LoadCalibrationsAsync(runs.Select(r => r.Id).ToList(), ct);

        var (priceCard, pricing) = latest == null
            ? (new ChatConsistencyPriceCard { Available = false, Source = "none" }, (ModelPricing?)null)
            : await ResolvePriceCardAsync(latest);

        var points = runs.Select(run => TimelinePoint(run, calibrations.Where(c => c.BenchmarkRunId == run.Id).ToList(), pricing)).ToList();

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
            Events = events,
            Annotations = annotations,
            PriceCard = priceCard
        };
    }

    /// <summary>One row per usable run of the subject between the bounds: eligibility and segment per axis, re-grade coverage, controls, served model.</summary>
    public async Task<IReadOnlyList<ChatConsistencyRunRow>> GetRunTableAsync(string modelKey, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);
        var runs = await LoadSubjectRunsAsync(modelKey, fromUtc, toUtc, includeTelemetry: false, ct);
        if (runs.Count == 0) return Array.Empty<ChatConsistencyRunRow>();

        var calibrations = await LoadCalibrationsAsync(runs.Select(r => r.Id).ToList(), ct);

        DateTime from = fromUtc ?? runs[0].StartedAtUtc;
        DateTime to = toUtc ?? runs[^1].StartedAtUtc;
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
                ServedModelIds = ChatConsistencyMeasures.ServedModels(run)
            };
        }).ToList();
    }

    // --- Loading ---------------------------------------------------------------------------------

    private async Task<List<BenchmarkRun>> LoadSubjectRunsAsync(
        string modelKey, DateTime? fromUtc, DateTime? toUtc, bool includeTelemetry, CancellationToken ct)
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

        var ids = (await query.ToListAsync(ct))
            .Where(r => ChatConsistencyComparability.ModelAxisKey(r) == modelKey)
            .Select(r => r.Id)
            .ToList();

        return Ordered(await LoadFullAsync(ids, includeTelemetry, ct));
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

    private static ChatConsistencyTimelinePoint TimelinePoint(BenchmarkRun run, List<BenchmarkAssessorCalibration> calibrations, ModelPricing? pricing)
    {
        var answers = run.Answers;
        var delivered = answers.Where(ChatConsistencyMeasures.IsDelivered).ToList();
        bool legacy = !run.CallTelemetryVersion.HasValue;
        var timings = ChatConsistencyMeasures.AnswerTimings(run);

        var native = answers.Select(ChatConsistencyMeasures.NativeQuality).Where(q => q.HasValue).Select(q => q!.Value).ToList();
        var ttfat = delivered.Select(CallTelemetryMeasures.TimeToFirstAnswerTextMs).Where(v => v.HasValue).Select(v => (double)v!.Value).ToList();
        var rates = delivered.Select(CallTelemetryMeasures.AnswerStreamingRate).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var modelTimes = delivered.Select(a => (double)a.ModelTimeMs).ToList();
        var outputs = delivered.Where(a => a.OutputTokens.HasValue).Select(a => (double)a.OutputTokens!.Value).ToList();
        var costs = answers.Select(a => ChatConsistencyMeasures.AnswerCost(a, run, pricing)).Where(c => c.HasValue).Select(c => (double)c!.Value).ToList();

        var common = calibrations
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

        double? Rate(Func<BenchmarkRunAnswer, bool> predicate) => answers.Count == 0 ? null : answers.Count(predicate) / (double)answers.Count;

        return new ChatConsistencyTimelinePoint
        {
            RunId = run.Id,
            StartedAtUtc = ChatConsistencyMeasures.AsUtc(run.StartedAtUtc),
            SuiteName = run.SuiteName ?? string.Empty,
            SuiteId = run.BenchmarkSuiteIdUsed ?? run.BenchmarkSuiteId,
            HarnessVersion = run.HarnessVersion,
            Status = run.Status,
            IsLegacy = legacy,
            IsAnchor = run.IsConsistencyAnchor,
            QualityIndex = run.QualityIndex,
            NativeMeanQuality = native.Count > 0 ? native.Average() : null,
            CommonGraderQuality = common,
            MedianTimeToFirstAnswerTextMs = BenchmarkGroupStatistics.Median(ttfat),
            MedianStreamingRate = BenchmarkGroupStatistics.Median(rates.Select(r => r.TokensPerSecond).ToList()),
            StreamingRateEstimated = rates.Any(r => r.Estimated),
            MedianModelTimeMs = BenchmarkGroupStatistics.Median(modelTimes),
            LatencyLabel = legacy ? "legacy proxy" : "telemetry",
            OutputTokensPerAnswer = outputs.Count > 0 ? outputs.Average() : null,
            ToolCallsPerAnswer = delivered.Count > 0 ? delivered.Average(a => (double)ChatConsistencyMeasures.ToolCalls(a)) : null,
            CostPerQuestionUsd = costs.Count > 0 ? costs.Average() : null,
            TerminalFailureRate = Rate(ChatConsistencyMeasures.IsTerminalFailure),
            TimeoutRate = Rate(ChatConsistencyMeasures.IsTimeout),
            EmptyAnswerRate = Rate(ChatConsistencyMeasures.IsEmptyAnswer),
            RefusalRate = legacy ? null : Rate(ChatConsistencyMeasures.IsRefusal),
            ToolBudgetExhaustedRate = Rate(ChatConsistencyMeasures.IsToolBudgetExhausted),
            ServedModelIds = ChatConsistencyMeasures.ServedModels(run),
            Strata = timings.Values.Select(t => t.Stratum).Distinct().OrderBy(s => s).Select(ChatConsistencyStatistics.StratumLabel).ToList(),
            StrataEstimated = timings.Values.Any(t => t.Estimated),
            AnswerCount = answers.Count,
            MaxParallelQuestions = run.MaxParallelQuestionsUsed
        };
    }

    private static ChatConsistencyNote Note(string kind, string text) => new() { Kind = kind, Text = text };

    private static string Inv(long value) => value.ToString(CultureInfo.InvariantCulture);
}
