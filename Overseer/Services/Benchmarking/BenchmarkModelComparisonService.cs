namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;

/// <summary>
/// One point of a comparison with everything needed to measure it already loaded: the suite and its
/// items, the runs with their answers, and the price card each run's candidate spend is costed at.
///
/// <para>Pricing is resolved by the I/O layer and handed in, so the arithmetic that turns stored
/// token totals into a comparable cost is pure and can be asserted against a fixture with known
/// token counts.</para>
/// </summary>
public sealed record BenchmarkModelComparisonSource
{
    /// <summary>`run:12` or `group:3`.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>`Run` or `Group`.</summary>
    public string SourceKind { get; init; } = string.Empty;

    public long SourceId { get; init; }

    public string? SourceName { get; init; }

    public BenchmarkSuite? Suite { get; init; }

    public IReadOnlyList<BenchmarkQuestion> Questions { get; init; } = Array.Empty<BenchmarkQuestion>();

    public IReadOnlyList<BenchmarkRun> Runs { get; init; } = Array.Empty<BenchmarkRun>();

    /// <summary>
    /// The candidate price card per run id, on the comparison's basis. A run with no resolvable card
    /// maps to null and contributes no cost — an unknown cost is reported as unknown, never as zero.
    /// </summary>
    public IReadOnlyDictionary<long, ModelPricing?> CandidatePricing { get; init; }
        = new Dictionary<long, ModelPricing?>();

    /// <summary>
    /// Set when the point cannot be measured at all — a group whose own members do not pool, or a
    /// run with no usable result. Such a source is excluded before comparability is resolved, so it
    /// can neither be charted nor drag the baseline towards its own condition.
    /// </summary>
    public string? Refusal { get; init; }
}

/// <summary>
/// Turns loaded comparison sources into the view's DTO. Pure arithmetic: no I/O, no AI calls, no
/// writes.
///
/// <para>Two rules govern the whole class:</para>
/// <list type="bullet">
/// <item><b>Models are never pooled.</b> Each source becomes exactly one point, measured through
/// <see cref="BenchmarkGroupStatistics.Compute"/> over its own runs. Nothing here recomputes an
/// index, a percentile or a standard deviation.</item>
/// <item><b>An excluded point carries no measures.</b> <see cref="BenchmarkCrossModelComparability"/>
/// decides which points share an instrument, and a point that does not is emitted with null
/// quality, speed and cost. A chart cannot render what is not there, which is a stronger guarantee
/// than a flag a renderer might forget to test.</item>
/// </list>
/// </summary>
public static class BenchmarkModelComparison
{
    /// <summary>
    /// A Speed Index at or above this is at the ceiling: the answer finished inside its
    /// difficulty-scaled target and the index can no longer tell it from a much faster one.
    /// </summary>
    public const int SpeedIndexCeiling = 100;

    /// <summary>How far a scheduled price change may be in the future and still be worth warning about.</summary>
    public const int ScheduledPriceChangeHorizonMonths = 12;

    /// <summary>
    /// The three measures this view refuses to chart, each with the reason and where to look
    /// instead. Carried into the DTO rather than left as comments, because a reader who cannot see
    /// why a measure is missing will eventually add it back.
    /// </summary>
    public static IReadOnlyList<BenchmarkModelComparisonExcludedMeasureDto> ExcludedMeasures()
        => new[]
        {
            new BenchmarkModelComparisonExcludedMeasureDto
            {
                Measure = "Speed Index",
                Reason = "Saturated — nearly every answer sits at the ceiling — and comparable only "
                    + "within one thinking level. As a bar chart it would show several models tied at "
                    + "100 that differ severalfold in the latency a user perceives.",
                Instead = "Time to first token P50, which is the speed axis."
            },
            new BenchmarkModelComparisonExcludedMeasureDto
            {
                Measure = "Cost per index point",
                Reason = "A ratio of two noisy estimators. It has no simple confidence interval and "
                    + "inverts its meaning as the index approaches zero, which is why the group cost "
                    + "statistics already guard it against a non-positive index.",
                Instead = "The table column, read beside the quality interval."
            },
            new BenchmarkModelComparisonExcludedMeasureDto
            {
                Measure = "Pairwise significance",
                Reason = "An overview of several models must not sprout an unadjusted pairwise test "
                    + "matrix. Wilcoxon signed-rank, the paired t-test, Cohen's dz and "
                    + "Benjamini-Hochberg control already exist for a chosen pair.",
                Instead = "The two-group comparison, run on the pair you care about."
            }
        };

    /// <summary>
    /// Builds the comparison.
    /// </summary>
    /// <param name="today">
    /// The date the <c>Current</c> pricing basis and the scheduled-change horizon are evaluated
    /// against. Injected so a test can stand either side of an announced change without waiting for
    /// the calendar, exactly as <see cref="ModelPricingService.ResolveDefault"/> allows.
    /// </param>
    public static BenchmarkModelComparisonDto Build(
        IReadOnlyList<BenchmarkModelComparisonSource>? sources,
        BenchmarkModelComparisonPricingBasis basis,
        DateOnly today,
        DateTime computedAtUtc)
    {
        var members = (sources ?? Array.Empty<BenchmarkModelComparisonSource>())
            .Where(s => s != null)
            .ToList();

        var measurable = members
            .Where(s => s.Refusal == null && s.Suite != null && s.Runs.Count > 0)
            .ToList();

        // The Current basis recomputes every entry's cost from one catalog, so the stored snapshots
        // are no longer what the cost axis shows and cannot degrade it.
        bool repriced = basis == BenchmarkModelComparisonPricingBasis.Current;

        var comparability = BenchmarkCrossModelComparability.Resolve(
            measurable.Select(s => new BenchmarkCrossModelEntry { Key = s.Key, Runs = s.Runs }).ToList(),
            repriced);

        var verdicts = comparability.Entries.ToDictionary(v => v.Key, v => v, StringComparer.Ordinal);

        var entries = new List<BenchmarkModelComparisonEntryDto>();
        foreach (var source in members)
        {
            entries.Add(source.Refusal != null || source.Suite == null || source.Runs.Count == 0
                ? BuildRefusedEntry(source)
                : BuildEntry(source, verdicts[source.Key], comparability, basis, today));
        }

        var baselineSource = measurable
            .FirstOrDefault(s => comparability.BaselineEntryKeys.Contains(s.Key, StringComparer.Ordinal));

        return new BenchmarkModelComparisonDto
        {
            PricingBasis = basis.ToString(),
            PricingBasisLabel = DescribeBasis(basis, today),
            ComputedAtUtc = computedAtUtc,
            BaselineSuiteId = baselineSource?.Suite?.Id,
            BaselineSuiteName = baselineSource?.Suite?.Name,
            BaselineEntryKeys = comparability.BaselineEntryKeys.ToList(),
            BaselineKeyValues = comparability.BaselineKeyValues.ToDictionary(kv => kv.Key, kv => kv.Value),
            ModelAxisKeys = BenchmarkCrossModelComparability.ModelAxisKeys.ToList(),
            Entries = entries,
            ComparableCount = entries.Count(e => !e.Excluded),
            ExcludedCount = entries.Count(e => e.Excluded),
            ThinkingLevelsDiffer = comparability.ThinkingLevelsDiffer,
            SpeedAxisCaveat = comparability.SpeedAxisCaveat,
            Explanation = comparability.Explanation,
            ExcludedMeasures = ExcludedMeasures().ToList()
        };
    }

    /// <summary>A source that could not be measured at all: identity, the reason, and no figures.</summary>
    private static BenchmarkModelComparisonEntryDto BuildRefusedEntry(BenchmarkModelComparisonSource source)
    {
        var entry = Identity(source, thinkingLevelInLabel: false);
        entry.State = "Excluded";
        entry.Excluded = true;
        entry.Comparable = false;
        entry.Explanation = source.Refusal
            ?? "Excluded: this entry has no suite or no runs, so nothing about it can be measured.";
        return entry;
    }

    private static BenchmarkModelComparisonEntryDto BuildEntry(
        BenchmarkModelComparisonSource source,
        BenchmarkCrossModelVerdict verdict,
        BenchmarkCrossModelComparabilityResult comparability,
        BenchmarkModelComparisonPricingBasis basis,
        DateOnly today)
    {
        var entry = Identity(source, comparability.ThinkingLevelsDiffer);

        entry.State = verdict.IsExcluded ? "Excluded" : verdict.IsComparable ? "Comparable" : "Degraded";
        entry.Comparable = verdict.IsComparable;
        entry.Excluded = verdict.IsExcluded;
        entry.SpeedDegraded = verdict.IsSpeedDegraded;
        entry.CostDegraded = verdict.IsCostDegraded;
        entry.ExcludingKeys = verdict.ExcludingKeys.ToList();
        entry.SpeedDegradingKeys = verdict.SpeedDegradingKeys.ToList();
        entry.CostDegradingKeys = verdict.CostDegradingKeys.ToList();
        entry.Explanation = verdict.Explanation;
        entry.Differences = verdict.Differences.Select(d => new BenchmarkComparabilityDifferenceDto
        {
            Name = d.Name,
            Kind = d.Kind.ToString(),
            Description = d.Describe(),
            Variants = d.Variants.Select(v => new BenchmarkComparabilityVariantDto
            {
                Value = v.Value,
                RunIds = v.RunIds.ToList()
            }).ToList()
        }).ToList();

        // The measures stop here for an excluded entry. This is the whole exclusion rule: not a flag
        // on a number, but the absence of the number.
        if (verdict.IsExcluded) return entry;

        var costs = BuildCosts(source, out bool pricingResolved, out ModelPricing? card);
        var options = BuildStatisticsOptions(source, verdict);
        var statistics = BenchmarkGroupStatistics.Compute(
            source.Suite!, source.Questions, source.Runs, costs, options);

        entry.Quality = BuildQuality(statistics);
        entry.Speed = BuildSpeed(statistics);
        entry.Cost = BuildCost(statistics, basis, card, pricingResolved, today);
        entry.Table = BuildTable(source, statistics);

        return entry;
    }

    /// <summary>
    /// The identity fields, read from the newest run so a point is labelled by the configuration it
    /// most recently ran under. A poolable set agrees on all of them anyway.
    /// </summary>
    private static BenchmarkModelComparisonEntryDto Identity(
        BenchmarkModelComparisonSource source, bool thinkingLevelInLabel)
    {
        var ordered = source.Runs.OrderBy(r => r.StartedAtUtc).ToList();
        var representative = ordered.LastOrDefault();

        string displayName = representative?.TestedModelDisplayNameUsed
            ?? representative?.TestedModelIdUsed
            ?? source.SourceName
            ?? source.Key;

        string label = displayName;
        if (thinkingLevelInLabel && !string.IsNullOrWhiteSpace(representative?.TestedModelThinkingLevelUsed))
        {
            label = $"{displayName} ({representative!.TestedModelThinkingLevelUsed})";
        }

        return new BenchmarkModelComparisonEntryDto
        {
            Key = source.Key,
            SourceKind = source.SourceKind,
            SourceId = source.SourceId,
            SourceName = source.SourceName,
            RunIds = source.Runs.Select(r => r.Id).OrderBy(id => id).ToList(),
            RunCount = source.Runs.Count,
            SuiteId = source.Suite?.Id ?? representative?.BenchmarkSuiteId,
            SuiteName = source.Suite?.Name ?? representative?.SuiteName,
            Provider = representative?.TestedModelProviderUsed ?? string.Empty,
            ModelId = representative?.TestedModelIdUsed ?? string.Empty,
            ModelDisplayName = displayName,
            ThinkingLevel = representative?.TestedModelThinkingLevelUsed,
            ReasoningMode = representative?.TestedModelReasoningModeUsed,
            ReasoningSummary = representative?.TestedModelReasoningSummaryUsed,
            ServiceTier = representative?.TestedModelServiceTierUsed,
            MaxOutputTokens = representative?.TestedModelMaxOutputTokensUsed,
            // Qualified: System.Linq declares a type of the same name.
            ParallelExecutionMode = (representative?.TestedModelParallelExecutionModeUsed
                ?? MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled).ToString(),
            Label = label,
            FirstRunStartedAtUtc = ordered.Count > 0 ? ordered[0].StartedAtUtc : default,
            LastRunStartedAtUtc = representative?.StartedAtUtc ?? default
        };
    }

    /// <summary>
    /// Candidate-only per-run costs, on the comparison's basis. The arithmetic mirrors the group
    /// analysis exactly — long-context buckets included, and the <i>served</i> service tier rather
    /// than the requested one — so a figure here is the same figure an operator already read on the
    /// individual run whenever the basis is <c>AsRun</c>.
    /// </summary>
    private static List<BenchmarkGroupRunCost> BuildCosts(
        BenchmarkModelComparisonSource source, out bool pricingResolved, out ModelPricing? card)
    {
        var costs = new List<BenchmarkGroupRunCost>();
        pricingResolved = source.Runs.Count > 0;
        card = null;

        foreach (var run in source.Runs)
        {
            source.CandidatePricing.TryGetValue(run.Id, out var pricing);
            if (pricing == null)
            {
                pricingResolved = false;
                continue;
            }

            card ??= pricing;

            string? servedTier = BenchmarkRunFinalizer.ResolveServedServiceTier(run.Answers);
            decimal candidate = ModelPricingService.ComputeCostFromTotals(
                pricing,
                run.TotalInputTokens, run.TotalOutputTokens,
                run.TotalCacheReadTokens, run.TotalCacheCreationTokens,
                run.TotalLongContextInputTokens, run.TotalLongContextOutputTokens,
                run.TotalLongContextCacheReadTokens, run.TotalLongContextCacheCreationTokens,
                actualServiceTier: servedTier,
                requestedServiceTier: run.TestedModelServiceTierUsed);

            costs.Add(new BenchmarkGroupRunCost
            {
                RunId = run.Id,
                CostByRole = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    [BenchmarkGroupAnalysisService.CandidateRole] = (double)candidate
                }
            });
        }

        return costs;
    }

    /// <summary>
    /// The degraded flags a point's own statistics are computed under: its internal comparability
    /// tier, widened by whatever the cross-model set degrades. A point whose own runs are Tier A can
    /// still sit on a degraded axis, because the axis is a property of the set.
    /// </summary>
    private static BenchmarkGroupStatisticsOptions BuildStatisticsOptions(
        BenchmarkModelComparisonSource source, BenchmarkCrossModelVerdict verdict)
    {
        var within = BenchmarkComparabilityKey.Resolve(source.Runs);
        var options = BenchmarkGroupStatisticsOptions.FromComparability(within);

        string setSpeedReason = "Across the compared models: " + string.Join(", ", verdict.SpeedDegradingKeys);
        string setCostReason = "Across the compared models: " + string.Join(", ", verdict.CostDegradingKeys);

        return options with
        {
            SpeedDegraded = options.SpeedDegraded || verdict.IsSpeedDegraded,
            SpeedDegradedReason = verdict.IsSpeedDegraded
                ? Join(options.SpeedDegradedReason, setSpeedReason)
                : options.SpeedDegradedReason,
            CostDegraded = options.CostDegraded || verdict.IsCostDegraded,
            CostDegradedReason = verdict.IsCostDegraded
                ? Join(options.CostDegradedReason, setCostReason)
                : options.CostDegradedReason
        };
    }

    private static BenchmarkModelComparisonQualityDto BuildQuality(BenchmarkGroupStatisticsResult statistics)
    {
        var index = statistics.Index;

        return new BenchmarkModelComparisonQualityDto
        {
            PointEstimate = index.PointEstimate,
            ItemCount = statistics.ItemCount,
            IntervalHalfWidth = index.CombinedHalfWidth,
            IntervalLower = index.CombinedLower,
            IntervalUpper = index.CombinedUpper,
            IntervalTruncated = index.CombinedIntervalTruncated,
            ItemSamplingHalfWidth = index.ItemSamplingHalfWidth,
            ReproducibilityHalfWidth = index.ReproducibilityHalfWidth,
            ReproducibilityStandardDeviation = index.ReproducibilityStandardDeviation,
            ReproducibilityAvailable = index.ReproducibilityAvailable,
            IntervalBasis = index.ReproducibilityAvailable
                ? "Item sampling and run-to-run reproducibility, combined in quadrature."
                : $"Item sampling only. Below {BenchmarkGroupStatistics.MinRunsForReproducibility} runs "
                  + "there is no reproducibility estimate, so this interval covers one source of "
                  + "variation rather than two."
        };
    }

    private static BenchmarkModelComparisonSpeedDto BuildSpeed(BenchmarkGroupStatisticsResult statistics)
    {
        var speed = statistics.Speed;

        return new BenchmarkModelComparisonSpeedDto
        {
            TtftP50Ms = speed.TtftP50Ms,
            TtftP90Ms = speed.TtftP90Ms,
            TtftAnswerCount = speed.TtftAnswerCount,
            ModelTimeP50Ms = speed.ModelTimeP50Ms,
            ModelTimeP90Ms = speed.ModelTimeP90Ms,
            PooledAnswerCount = speed.PooledAnswerCount,
            Degraded = speed.Degraded,
            DegradedReason = speed.DegradedReason,
            Caveat = speed.Caveat
        };
    }

    private static BenchmarkModelComparisonCostDto BuildCost(
        BenchmarkGroupStatisticsResult statistics,
        BenchmarkModelComparisonPricingBasis basis,
        ModelPricing? card,
        bool pricingResolved,
        DateOnly today)
    {
        var cost = statistics.Cost;
        var scheduled = UpcomingScheduledChange(card, today);

        return new BenchmarkModelComparisonCostDto
        {
            CandidateCostPerQuestionUsd = pricingResolved ? cost?.CostPerQuestion : null,
            CandidateCostPerRunUsd = pricingResolved ? cost?.MeanCostPerRun : null,
            CandidateTotalCostUsd = pricingResolved ? cost?.TotalCost : null,
            Basis = basis.ToString(),
            PricingAsOf = card?.AsOf,
            PricingResolved = pricingResolved && cost != null,
            Degraded = cost?.Degraded ?? false,
            DegradedReason = cost?.DegradedReason,
            ScheduledChangeEffectiveFrom = scheduled?.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ScheduledChangeNote = scheduled?.Note
        };
    }

    private static BenchmarkModelComparisonTableDto BuildTable(
        BenchmarkModelComparisonSource source, BenchmarkGroupStatisticsResult statistics)
    {
        // The saturation predicate the single-run report already applies to its Speed Index heading:
        // at least half the answers that carry a speed score sit at the ceiling.
        var speedScores = source.Runs
            .SelectMany(r => r.Answers ?? new List<BenchmarkRunAnswer>())
            .Where(a => a.Status == BenchmarkAnswerStatus.Ok && a.SpeedScore.HasValue)
            .Select(a => a.SpeedScore!.Value)
            .ToList();

        int ceiling = speedScores.Count(s => s >= SpeedIndexCeiling);

        return new BenchmarkModelComparisonTableDto
        {
            MeanSpeedIndex = statistics.Speed.MeanSpeedIndex,
            SpeedIndexSaturated = speedScores.Count > 0 && ceiling * 2 >= speedScores.Count,
            SpeedIndexCeilingAnswerCount = ceiling,
            SpeedIndexScoredAnswerCount = speedScores.Count,
            CostPerIndexPointUsd = statistics.Cost?.CostPerIndexPoint,
            MeanStoredQualityIndex = statistics.Index.MeanStoredQualityIndex,
            UnstableItemCount = statistics.UnstableQuestionIds.Count
        };
    }

    /// <summary>
    /// The model's announced price change, when it falls inside the warning horizon. An elapsed
    /// schedule is not returned: the base card has already absorbed it, so it is history rather than
    /// a warning about a conclusion that is about to expire.
    /// </summary>
    private static ScheduledPricingChange? UpcomingScheduledChange(ModelPricing? card, DateOnly today)
    {
        var scheduled = card?.ScheduledChange;
        if (scheduled == null) return null;

        if (scheduled.EffectiveFrom <= today) return null;
        if (scheduled.EffectiveFrom > today.AddMonths(ScheduledPriceChangeHorizonMonths)) return null;

        return scheduled;
    }

    private static string DescribeBasis(BenchmarkModelComparisonPricingBasis basis, DateOnly today)
        => basis == BenchmarkModelComparisonPricingBasis.Current
            ? $"Priced from the catalog as of {today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}. "
              + "Comparable across dates; not what was actually spent."
            : "Priced from each run's own stored snapshot. What was actually spent; not comparable "
              + "across snapshot dates.";

    private static string Join(string? first, string second)
        => string.IsNullOrWhiteSpace(first) ? second : first + " " + second;
}

/// <summary>
/// Loads the runs and groups a cross-model comparison names, resolves a price card for each on the
/// requested basis, and hands them to <see cref="BenchmarkModelComparison"/>.
///
/// <para>This is the only place the comparison meets the database. Everything scientific happens in
/// <see cref="BenchmarkGroupStatistics"/> and everything about <i>which points may share a chart</i>
/// happens in <see cref="BenchmarkCrossModelComparability"/>, both of which are pure. What this
/// class adds is the one thing neither can do: refusing to build a point out of a group whose own
/// members do not pool.</para>
///
/// <para>Admin-initiated, and it makes no AI calls. A comparison over a handful of groups is
/// arithmetic over stored data — re-pricing included — and never triggers a re-run.</para>
/// </summary>
public class BenchmarkModelComparisonService
{
    private readonly ApplicationDbContext _db;
    private readonly ModelPricingService? _pricingService;
    private readonly ILogger<BenchmarkModelComparisonService>? _logger;

    public BenchmarkModelComparisonService(
        ApplicationDbContext db,
        ILogger<BenchmarkModelComparisonService>? logger = null,
        ModelPricingService? pricingService = null)
    {
        _db = db;
        _logger = logger;
        _pricingService = pricingService;
    }

    public async Task<(BenchmarkModelComparisonDto? Result, string? Error)> CompareAsync(
        BenchmarkModelComparisonRequest? request, CancellationToken ct = default)
    {
        var runIds = (request?.RunIds ?? new List<long>()).Distinct().ToList();
        var groupIds = (request?.GroupIds ?? new List<long>()).Distinct().ToList();

        if (runIds.Count == 0 && groupIds.Count == 0)
        {
            return (null, "A comparison needs at least one run or group.");
        }

        var basis = request?.PricingBasis ?? BenchmarkModelComparisonPricingBasis.Current;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var groups = groupIds.Count == 0
            ? new List<BenchmarkRunGroup>()
            : await _db.BenchmarkRunGroups
                .Include(g => g.Members)
                .Where(g => groupIds.Contains(g.Id))
                .ToListAsync(ct);

        var missingGroups = groupIds.Except(groups.Select(g => g.Id)).ToList();
        if (missingGroups.Count > 0)
        {
            return (null, $"Group(s) not found: {string.Join(", ", missingGroups)}.");
        }

        var wantedRunIds = runIds
            .Concat(groups.SelectMany(g => g.Members.Select(m => m.BenchmarkRunId)))
            .Distinct()
            .ToList();

        var runs = await _db.BenchmarkRuns
            .Include(r => r.Answers)
            .Where(r => wantedRunIds.Contains(r.Id))
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var runsById = runs.ToDictionary(r => r.Id);
        var missingRuns = runIds.Except(runsById.Keys).ToList();
        if (missingRuns.Count > 0)
        {
            return (null, $"Run(s) not found: {string.Join(", ", missingRuns)}.");
        }

        var suiteIds = runs.Where(r => r.BenchmarkSuiteId.HasValue).Select(r => r.BenchmarkSuiteId!.Value)
            .Concat(groups.Where(g => g.BenchmarkSuiteId.HasValue).Select(g => g.BenchmarkSuiteId!.Value))
            .Distinct()
            .ToList();

        var suites = suiteIds.Count == 0
            ? new List<BenchmarkSuite>()
            : await _db.BenchmarkSuites
                .Include(s => s.Questions)
                .Where(s => suiteIds.Contains(s.Id))
                .ToListAsync(ct);

        var suitesById = suites.ToDictionary(s => s.Id);
        var pricing = await ResolveCandidatePricingAsync(runs, basis, today);

        var sources = new List<BenchmarkModelComparisonSource>();

        foreach (long runId in runIds)
        {
            var run = runsById[runId];
            sources.Add(BuildSource(
                key: $"run:{runId}",
                kind: "Run",
                sourceId: runId,
                name: null,
                suite: run.BenchmarkSuiteId.HasValue ? suitesById.GetValueOrDefault(run.BenchmarkSuiteId.Value) : null,
                members: new[] { run },
                pricing: pricing,
                refusal: RefuseRun(run)));
        }

        foreach (long groupId in groupIds)
        {
            var group = groups.First(g => g.Id == groupId);
            var members = group.Members
                .Select(m => runsById.GetValueOrDefault(m.BenchmarkRunId))
                .Where(r => r != null)
                .Select(r => r!)
                .OrderBy(r => r.Id)
                .ToList();

            long? suiteId = group.BenchmarkSuiteId ?? members.FirstOrDefault()?.BenchmarkSuiteId;

            sources.Add(BuildSource(
                key: $"group:{groupId}",
                kind: "Group",
                sourceId: groupId,
                name: group.Name,
                suite: suiteId.HasValue ? suitesById.GetValueOrDefault(suiteId.Value) : null,
                members: members,
                pricing: pricing,
                refusal: RefuseGroup(group, members)));
        }

        var result = BenchmarkModelComparison.Build(sources, basis, today, DateTime.UtcNow);

        _logger?.LogInformation(
            "Computed a cross-model comparison over {EntryCount} entries on the {Basis} pricing basis: "
            + "{ComparableCount} charted, {ExcludedCount} excluded.",
            result.Entries.Count, basis, result.ComparableCount, result.ExcludedCount);

        return (result, null);
    }

    private static BenchmarkModelComparisonSource BuildSource(
        string key,
        string kind,
        long sourceId,
        string? name,
        BenchmarkSuite? suite,
        IReadOnlyList<BenchmarkRun> members,
        IReadOnlyDictionary<long, ModelPricing?> pricing,
        string? refusal)
    {
        return new BenchmarkModelComparisonSource
        {
            Key = key,
            SourceKind = kind,
            SourceId = sourceId,
            SourceName = name,
            Suite = suite,
            Questions = suite?.Questions.OrderBy(q => q.OrderIndex).ToList() ?? new List<BenchmarkQuestion>(),
            Runs = members,
            CandidatePricing = members
                .ToDictionary(r => r.Id, r => pricing.GetValueOrDefault(r.Id)),
            Refusal = refusal ?? (suite == null
                ? "Excluded: this entry is not bound to a suite, so its items cannot be identified."
                : null)
        };
    }

    /// <summary>
    /// Null when the run may become a point. A run still in flight has no settled index, and a run
    /// that failed produced no measurement at all.
    /// </summary>
    private static string? RefuseRun(BenchmarkRun run)
    {
        if (run.Status == BenchmarkRunStatus.Running)
        {
            return "Excluded: this run is still in progress, so its figures are not settled.";
        }

        if (run.Status == BenchmarkRunStatus.Failed || run.Status == BenchmarkRunStatus.Canceled)
        {
            return $"Excluded: this run ended {run.Status}, so it produced no measurement to compare.";
        }

        return null;
    }

    /// <summary>
    /// Null when the group may be pooled into one point. A Tier C group is two conditions, and a set
    /// below Tier B is not one thing at all — either would put a number on the chart that describes
    /// no model.
    /// </summary>
    private static string? RefuseGroup(BenchmarkRunGroup group, IReadOnlyList<BenchmarkRun> members)
    {
        if (members.Count == 0)
        {
            return "Excluded: this group has no loadable member runs.";
        }

        var comparability = BenchmarkComparabilityKey.Resolve(members);
        if (comparability.PoolingPermitted) return null;

        return $"Excluded: the members of \"{group.Name}\" do not pool, so they cannot be one point. "
            + comparability.Explanation;
    }

    /// <summary>
    /// The candidate price card for every run, on the requested basis.
    ///
    /// <para><c>AsRun</c> reads each run's own stored snapshot; <c>Current</c> reads today's catalog
    /// for the run's provider and model id. Either way the token counts are the invariant, so
    /// switching basis is arithmetic over columns the run already carries.</para>
    /// </summary>
    private async Task<Dictionary<long, ModelPricing?>> ResolveCandidatePricingAsync(
        IReadOnlyList<BenchmarkRun> runs,
        BenchmarkModelComparisonPricingBasis basis,
        DateOnly today)
    {
        var cards = new Dictionary<long, ModelPricing?>();
        if (_pricingService == null) return cards;

        foreach (var run in runs)
        {
            try
            {
                cards[run.Id] = basis == BenchmarkModelComparisonPricingBasis.AsRun
                    ? (await _pricingService.ResolveForRunAsync(run)).Candidate
                    : _pricingService.ResolveDefault(run.TestedModelProviderUsed, run.TestedModelIdUsed, today);
            }
            catch (Exception ex)
            {
                // An unresolvable card costs one entry's cost axis, never the comparison.
                _logger?.LogWarning(ex,
                    "Could not resolve {Basis} pricing for benchmark run {RunId}; its cost is reported as unknown.",
                    basis, run.Id);
                cards[run.Id] = null;
            }
        }

        return cards;
    }
}
