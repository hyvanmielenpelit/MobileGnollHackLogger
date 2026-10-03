namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;

/// <summary>One suite of a battery result: its usable member runs, loaded without their answers.</summary>
public sealed record BenchmarkBatteryComparisonSourceSuite
{
    /// <summary>0-based position in the battery run's definition snapshot.</summary>
    public int SuiteIndex { get; init; }

    /// <summary>Null when the definition snapshot cannot be read.</summary>
    public long? SuiteId { get; init; }

    public string? SuiteName { get; init; }

    /// <summary>The usable member runs of the suite, each once, in run-id order.</summary>
    public IReadOnlyList<BenchmarkRun> Runs { get; init; } = Array.Empty<BenchmarkRun>();
}

/// <summary>What the comparison reads from one member run's answer rows, in place of the rows.</summary>
public sealed record BenchmarkBatteryMemberAnswerSummary
{
    /// <summary>Every answer row, whatever its status: the questions the run's spend paid for.</summary>
    public int AnswerRowCount { get; init; }

    /// <summary>As <see cref="BenchmarkRunFinalizer.ResolveServedServiceTier"/> resolves it.</summary>
    public string? ServedServiceTier { get; init; }

    /// <summary>Ok answers carrying a speed score.</summary>
    public int SpeedScoredAnswerCount { get; init; }

    /// <summary>Those of them at or above <see cref="BenchmarkModelComparison.SpeedIndexCeiling"/>.</summary>
    public int SpeedCeilingAnswerCount { get; init; }
}

/// <summary>
/// One battery result of a comparison with everything needed to measure it loaded: the battery run,
/// its latest analysis and persisted result, its usable member runs by suite (without answers), and
/// their price cards on the comparison's basis.
/// </summary>
public sealed record BenchmarkBatteryComparisonSource
{
    /// <summary>`battery:7`.</summary>
    public string Key { get; init; } = string.Empty;

    public long BatteryRunId { get; init; }

    public string BatteryName { get; init; } = string.Empty;

    /// <summary>The latest analysis's definition hash, else the battery run's own.</summary>
    public string DefinitionSha256 { get; init; } = string.Empty;

    /// <summary>The revision recorded in the definition snapshot; null when it cannot be read.</summary>
    public int? DefinitionRevision { get; init; }

    public BenchmarkRunSeriesStatus Status { get; init; }

    public int SuiteCount { get; init; }

    public int RunsPerSuite { get; init; }

    /// <summary>The battery run's latest analysis; null when none was computed. Detached.</summary>
    public BenchmarkBatteryAnalysis? Analysis { get; init; }

    /// <summary>The persisted result of <see cref="Analysis"/>; null when absent or unreadable.</summary>
    public BenchmarkBatteryStatisticsResult? Result { get; init; }

    public string? ComparabilityClassSha256 => Analysis?.ComparabilityClassSha256;

    /// <summary>One entry per definition suite, in suite-index order.</summary>
    public IReadOnlyList<BenchmarkBatteryComparisonSourceSuite> Suites { get; init; } = Array.Empty<BenchmarkBatteryComparisonSourceSuite>();

    /// <summary>The usable member runs of every suite, each once, in run-id order. No answers.</summary>
    public IReadOnlyList<BenchmarkRun> Runs { get; init; } = Array.Empty<BenchmarkRun>();

    /// <summary>The runs the entry's identity is read from: the usable members, else every member run.</summary>
    public IReadOnlyList<BenchmarkRun> IdentityRuns { get; init; } = Array.Empty<BenchmarkRun>();

    /// <summary>Per usable member run id, what its answer rows contribute. Empty on a refused source.</summary>
    public IReadOnlyDictionary<long, BenchmarkBatteryMemberAnswerSummary> AnswerSummaries { get; init; }
        = new Dictionary<long, BenchmarkBatteryMemberAnswerSummary>();

    /// <summary>The candidate price card per usable member run id; null when unresolvable.</summary>
    public IReadOnlyDictionary<long, ModelPricing?> CandidatePricing { get; init; }
        = new Dictionary<long, ModelPricing?>();

    /// <summary>Every role's price card per usable member run id; null when unresolvable.</summary>
    public IReadOnlyDictionary<long, BenchmarkRunPricing?> RunPricing { get; init; }
        = new Dictionary<long, BenchmarkRunPricing?>();

    /// <summary>
    /// Set when the result cannot be measured: the battery has not finished, or its latest analysis
    /// is missing, unreadable, stale or incomplete. Such a source is shown with its identity only.
    /// </summary>
    public string? Refusal { get; init; }

    /// <summary>This source with its members' price cards taken from <paramref name="runPricing"/>.</summary>
    public BenchmarkBatteryComparisonSource WithPricing(IReadOnlyDictionary<long, BenchmarkRunPricing?> runPricing)
    {
        ArgumentNullException.ThrowIfNull(runPricing);

        return this with
        {
            RunPricing = Runs.ToDictionary(r => r.Id, r => runPricing.GetValueOrDefault(r.Id)),
            CandidatePricing = Runs.ToDictionary(r => r.Id, r => runPricing.GetValueOrDefault(r.Id)?.Candidate)
        };
    }
}

/// <summary>The condition the charted battery results share: one definition and one comparability class.</summary>
public sealed record BenchmarkBatteryComparisonBaseline(
    string DefinitionSha256,
    string ComparabilityClassSha256,
    IReadOnlyList<string> EntryKeys);

/// <summary>
/// Battery results as entries of the model comparison: one point per battery run, read from its
/// latest persisted analysis rather than recomputed, with its cost recomputed from its member runs on
/// the comparison's pricing basis.
///
/// <para>Battery results are compared only with battery results. A battery result spans several
/// suites, so the per-run must-match keys of <see cref="BenchmarkCrossModelComparability"/> cannot
/// judge it; the battery's own definition hash and comparability class (Statistical Method M9) do,
/// exactly as the battery leaderboard ranks them. The degrading keys are still read from every
/// member run, so a mixed timing or pricing condition flags its axis as it does for runs.</para>
///
/// <para><see cref="LoadAsync"/> is the I/O; <see cref="ChooseBaseline"/> and <see cref="Build"/> are
/// pure.</para>
/// </summary>
public static class BenchmarkBatteryModelComparison
{
    public const string SourceKind = "Battery";

    public const string KeyPrefix = "battery:";

    public const string MixedSourcesError = "A comparison holds either battery results or runs and analysis groups.";

    public const string RecomputeAnalysisInstruction = "Recompute the battery analysis first.";

    /// <summary>The excluding key of a result that ran another battery definition.</summary>
    public const string BatteryDefinitionKey = "BatteryDefinition";

    /// <summary>The excluding key of a result in another comparability class, when no must-match key names the difference.</summary>
    public const string BatteryComparabilityClassKey = "BatteryComparabilityClass";

    public const string IntervalBasisAcrossRounds =
        "Battery composite: item sampling (t) and reproducibility across rounds";

    public const string IntervalBasisPerSuiteFallback =
        "Battery composite: item sampling (t) and reproducibility from each suite's own runs, because the rounds are ragged";

    public const string IntervalBasisItemSamplingOnly =
        "Battery composite: item sampling (t) only. Below three complete rounds there is no reproducibility estimate";

    public static string KeyOf(long batteryRunId)
        => KeyPrefix + batteryRunId.ToString(CultureInfo.InvariantCulture);

    /// <summary>False while the battery run can still add members: pending, running or waiting for the run cap.</summary>
    public static bool IsFinished(BenchmarkRunSeriesStatus status)
        => status is not (BenchmarkRunSeriesStatus.Pending
            or BenchmarkRunSeriesStatus.Running
            or BenchmarkRunSeriesStatus.WaitingForCap);

    // --- Loading -----------------------------------------------------------------------------------

    /// <summary>
    /// Loads each named battery run, in request order, with its latest analysis and its usable member
    /// runs (<see cref="BenchmarkBatteryPlanner.UnusableReason"/>), never their answers. Pricing is not
    /// resolved; see <see cref="BenchmarkBatteryComparisonSource.WithPricing"/>. Null sources, with the
    /// error, when a battery run does not exist.
    /// </summary>
    public static async Task<(IReadOnlyList<BenchmarkBatteryComparisonSource>? Sources, string? Error)> LoadAsync(
        ApplicationDbContext db,
        IReadOnlyList<long> batteryRunIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var ids = (batteryRunIds ?? Array.Empty<long>()).Distinct().ToList();

        var batteryRuns = await db.BenchmarkBatteryRuns
            .AsNoTracking()
            .Include(b => b.Members)
            .Where(b => ids.Contains(b.Id))
            .ToListAsync(ct);

        var missing = ids.Except(batteryRuns.Select(b => b.Id)).ToList();
        if (missing.Count > 0)
        {
            return (null, $"Battery run(s) not found: {string.Join(", ", missing)}.");
        }

        var analyses = await LoadLatestAnalysesAsync(db, ids, ct);

        var memberRunIds = batteryRuns
            .SelectMany(b => b.Members)
            .Where(m => !m.Superseded)
            .Select(m => m.BenchmarkRunId)
            .Distinct()
            .ToList();

        var runById = memberRunIds.Count == 0
            ? new Dictionary<long, BenchmarkRun>()
            : await db.BenchmarkRuns
                .AsNoTracking()
                .Where(r => memberRunIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, ct);

        var sources = new List<BenchmarkBatteryComparisonSource>(ids.Count);
        foreach (long id in ids)
        {
            var batteryRun = batteryRuns.Single(b => b.Id == id);
            sources.Add(BuildSource(batteryRun, analyses.GetValueOrDefault(id), runById));
        }

        var summaryRunIds = sources
            .Where(s => s.Refusal == null)
            .SelectMany(s => s.Runs.Select(r => r.Id))
            .Distinct()
            .ToList();

        if (summaryRunIds.Count == 0) return (sources, null);

        var summaries = await LoadAnswerSummariesAsync(db, summaryRunIds, ct);

        return (sources
            .Select(s => s.Refusal != null
                ? s
                : s with
                {
                    AnswerSummaries = s.Runs.ToDictionary(
                        r => r.Id,
                        r => summaries.GetValueOrDefault(r.Id) ?? new BenchmarkBatteryMemberAnswerSummary())
                })
            .ToList(), null);
    }

    /// <summary>The latest analysis of each battery run, by battery run id, as the battery screens read it.</summary>
    private static async Task<Dictionary<long, BenchmarkBatteryAnalysis>> LoadLatestAnalysesAsync(
        ApplicationDbContext db, List<long> batteryRunIds, CancellationToken ct)
    {
        var heads = await db.BenchmarkBatteryAnalyses
            .AsNoTracking()
            .Where(a => batteryRunIds.Contains(a.BenchmarkBatteryRunId))
            .Select(a => new { a.Id, a.BenchmarkBatteryRunId, a.ComputedAtUtc })
            .ToListAsync(ct);

        var latestIds = heads
            .GroupBy(a => a.BenchmarkBatteryRunId)
            .Select(g => g.OrderByDescending(a => a.ComputedAtUtc).ThenByDescending(a => a.Id).First().Id)
            .ToList();

        if (latestIds.Count == 0) return new Dictionary<long, BenchmarkBatteryAnalysis>();

        return await db.BenchmarkBatteryAnalyses
            .AsNoTracking()
            .Where(a => latestIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.BenchmarkBatteryRunId, ct);
    }

    /// <summary>Per run id, the summary of its answer rows, read as four columns rather than whole rows.</summary>
    private static async Task<Dictionary<long, BenchmarkBatteryMemberAnswerSummary>> LoadAnswerSummariesAsync(
        ApplicationDbContext db, List<long> runIds, CancellationToken ct)
    {
        var rows = await db.BenchmarkRunAnswers
            .AsNoTracking()
            .Where(a => runIds.Contains(a.BenchmarkRunId))
            .OrderBy(a => a.Id)
            .Select(a => new { a.BenchmarkRunId, a.ActualServiceTierUsed, a.Status, a.SpeedScore })
            .ToListAsync(ct);

        return rows
            .GroupBy(a => a.BenchmarkRunId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var speedScores = g
                        .Where(a => a.Status == BenchmarkAnswerStatus.Ok && a.SpeedScore.HasValue)
                        .Select(a => a.SpeedScore!.Value)
                        .ToList();

                    return new BenchmarkBatteryMemberAnswerSummary
                    {
                        AnswerRowCount = g.Count(),
                        ServedServiceTier = BenchmarkRunFinalizer.ResolveServedServiceTier(
                            g.Select(a => new BenchmarkRunAnswer { ActualServiceTierUsed = a.ActualServiceTierUsed })),
                        SpeedScoredAnswerCount = speedScores.Count,
                        SpeedCeilingAnswerCount = speedScores.Count(s => s >= BenchmarkModelComparison.SpeedIndexCeiling)
                    };
                });
    }

    /// <summary>
    /// One battery run as a comparison source. The usable members and their suites follow
    /// <see cref="BenchmarkBatteryAnalysisService.LoadAsync"/>: non-superseded, inside the definition,
    /// usable, and a run held by two slots of one suite counted once.
    /// </summary>
    internal static BenchmarkBatteryComparisonSource BuildSource(
        BenchmarkBatteryRun batteryRun,
        BenchmarkBatteryAnalysis? analysis,
        IReadOnlyDictionary<long, BenchmarkRun> runById)
    {
        var definition = TryReadDefinition(batteryRun.DefinitionJson);

        var definitionSuites = definition != null
            ? definition.Suites.OrderBy(s => s.Index).Select(s => (Index: s.Index, SuiteId: (long?)s.SuiteId, SuiteName: (string?)s.SuiteName)).ToList()
            : Enumerable.Range(0, batteryRun.RunsPerSuite > 0 ? batteryRun.RequestedMemberCount / batteryRun.RunsPerSuite : 0)
                .Select(i => (Index: i, SuiteId: (long?)null, SuiteName: (string?)null))
                .ToList();
        var suiteIndices = new HashSet<int>(definitionSuites.Select(s => s.Index));

        var members = batteryRun.Members
            .Where(m => !m.Superseded && runById.ContainsKey(m.BenchmarkRunId))
            .OrderBy(m => m.SuiteIndex)
            .ThenBy(m => m.Round)
            .ThenBy(m => m.Id)
            .ToList();

        var usable = members
            .Where(m => suiteIndices.Contains(m.SuiteIndex)
                        && BenchmarkBatteryPlanner.UnusableReason(m, runById[m.BenchmarkRunId]) == null)
            .ToList();

        var suites = definitionSuites
            .Select(s => new BenchmarkBatteryComparisonSourceSuite
            {
                SuiteIndex = s.Index,
                SuiteId = s.SuiteId,
                SuiteName = s.SuiteName,
                Runs = usable
                    .Where(m => m.SuiteIndex == s.Index)
                    .Select(m => m.BenchmarkRunId)
                    .Distinct()
                    .OrderBy(id => id)
                    .Select(id => runById[id])
                    .ToList()
            })
            .ToList();

        var usableRuns = usable
            .Select(m => m.BenchmarkRunId)
            .Distinct()
            .OrderBy(id => id)
            .Select(id => runById[id])
            .ToList();

        var memberRuns = members
            .Select(m => m.BenchmarkRunId)
            .Distinct()
            .OrderBy(id => id)
            .Select(id => runById[id])
            .ToList();

        var result = BenchmarkBatteryAnalysisService.DeserializeResult(analysis);
        var usableIds = usableRuns.Select(r => r.Id).ToList();

        return new BenchmarkBatteryComparisonSource
        {
            Key = KeyOf(batteryRun.Id),
            BatteryRunId = batteryRun.Id,
            BatteryName = batteryRun.BatteryName ?? string.Empty,
            DefinitionSha256 = !string.IsNullOrWhiteSpace(analysis?.DefinitionSha256)
                ? analysis!.DefinitionSha256
                : batteryRun.DefinitionSha256 ?? string.Empty,
            DefinitionRevision = definition?.Revision,
            Status = batteryRun.Status,
            SuiteCount = result?.SuiteCount ?? definitionSuites.Count,
            RunsPerSuite = batteryRun.RunsPerSuite,
            Analysis = analysis,
            Result = result,
            Suites = suites,
            Runs = usableRuns,
            IdentityRuns = usableRuns.Count > 0 ? usableRuns : memberRuns,
            Refusal = Refuse(batteryRun.Status, analysis, result, usableIds)
        };
    }

    /// <summary>
    /// Null when the battery result may become a point: the battery has finished, and its latest
    /// analysis is readable, current over the usable members, and ranked by the leaderboard's rule.
    /// </summary>
    internal static string? Refuse(
        BenchmarkRunSeriesStatus status,
        BenchmarkBatteryAnalysis? analysis,
        BenchmarkBatteryStatisticsResult? result,
        IReadOnlyList<long> usableRunIds)
    {
        if (!IsFinished(status))
        {
            return "Excluded: this battery run has not finished, so its result is not settled.";
        }

        if (analysis == null)
        {
            return "Excluded: this battery run has no battery analysis. Compute the battery analysis first.";
        }

        if (result == null)
        {
            return "Excluded: the stored battery analysis cannot be read. " + RecomputeAnalysisInstruction;
        }

        if (BenchmarkBatteryAnalysisService.IsStale(usableRunIds, analysis))
        {
            return "Excluded: the battery analysis was computed over other member runs than the battery "
                + "run now holds. " + RecomputeAnalysisInstruction;
        }

        if (usableRunIds.Count == 0
            || !BenchmarkBatteryLeaderboardService.IsRanked(
                analysis.Complete, analysis.ComparabilityClassSha256, result.OverallIndex?.PointEstimate))
        {
            return "Excluded: the battery analysis is incomplete. Not every suite has a usable member run, "
                + "so there is no Overall Index to compare.";
        }

        return null;
    }

    private static BenchmarkBatteryDefinition? TryReadDefinition(string? json)
    {
        try
        {
            return BenchmarkBatteryDefinition.FromJson(json ?? string.Empty);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // --- The condition -----------------------------------------------------------------------------

    /// <summary>
    /// The largest set of measurable results sharing one definition hash and one comparability
    /// class. Ties go to the set with the most member runs, then to the one whose first member came
    /// first in the input, as <see cref="BenchmarkCrossModelComparability.Resolve"/> breaks them.
    /// Null when no source is measurable.
    /// </summary>
    public static BenchmarkBatteryComparisonBaseline? ChooseBaseline(IReadOnlyList<BenchmarkBatteryComparisonSource>? sources)
    {
        var members = (sources ?? Array.Empty<BenchmarkBatteryComparisonSource>())
            .Where(s => s != null)
            .ToList();

        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < members.Count; i++)
        {
            order.TryAdd(members[i].Key, i);
        }

        var bucket = members
            .Where(IsMeasurable)
            .GroupBy(s => (s.DefinitionSha256, ClassSha256: s.ComparabilityClassSha256!))
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Sum(s => s.Runs.Count))
            .ThenBy(g => g.Min(s => order[s.Key]))
            .FirstOrDefault();

        if (bucket == null) return null;

        return new BenchmarkBatteryComparisonBaseline(
            bucket.Key.DefinitionSha256,
            bucket.Key.ClassSha256,
            bucket.OrderBy(s => order[s.Key]).Select(s => s.Key).ToList());
    }

    private static bool IsMeasurable(BenchmarkBatteryComparisonSource source)
        => source.Refusal == null
           && source.Runs.Count > 0
           && !string.IsNullOrEmpty(source.ComparabilityClassSha256)
           && source.Result?.OverallIndex != null;

    // --- Building ----------------------------------------------------------------------------------

    /// <summary>
    /// Builds the comparison of battery results. Pure.
    /// </summary>
    /// <param name="distinguishingKeys">
    /// Per entry key of a result on the baseline's definition but in another comparability class,
    /// the must-match keys that distinguish the two classes. A missing entry names the class itself.
    /// </param>
    public static BenchmarkModelComparisonDto Build(
        IReadOnlyList<BenchmarkBatteryComparisonSource>? sources,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? distinguishingKeys,
        BenchmarkModelComparisonPricingBasis basis,
        DateOnly today,
        DateTime computedAtUtc)
    {
        var members = (sources ?? Array.Empty<BenchmarkBatteryComparisonSource>())
            .Where(s => s != null)
            .ToList();

        bool repriced = basis == BenchmarkModelComparisonPricingBasis.Current;

        var baseline = ChooseBaseline(members);
        var baselineKeys = new HashSet<string>(baseline?.EntryKeys ?? Array.Empty<string>(), StringComparer.Ordinal);
        var baselineSources = members.Where(s => baselineKeys.Contains(s.Key)).ToList();

        var (speedKeys, costKeys) = SetDegradation(baselineSources, repriced);

        bool thinkingLevelsDiffer = baselineSources
            .Select(s => ThinkingLevelOf(Representative(s)))
            .Distinct(StringComparer.Ordinal)
            .Count() > 1;

        var entries = new List<BenchmarkModelComparisonEntryDto>(members.Count);
        foreach (var source in members)
        {
            if (!IsMeasurable(source))
            {
                entries.Add(BuildRefusedEntry(source));
            }
            else if (!baselineKeys.Contains(source.Key))
            {
                entries.Add(BuildExcludedEntry(source, baseline!, baselineSources, distinguishingKeys));
            }
            else
            {
                entries.Add(BuildEntry(source, thinkingLevelsDiffer, speedKeys, costKeys, basis, today));
            }
        }

        int comparableCount = entries.Count(e => !e.Excluded);
        int excludedCount = entries.Count - comparableCount;

        var baselineKeyValues = new Dictionary<string, string>(StringComparer.Ordinal);
        if (baseline != null)
        {
            baselineKeyValues[BatteryDefinitionKey] = baseline.DefinitionSha256;
            baselineKeyValues[BatteryComparabilityClassKey] = baseline.ComparabilityClassSha256;
        }

        return new BenchmarkModelComparisonDto
        {
            SubjectKind = BenchmarkModelComparisonSubjectKinds.Batteries,
            PricingBasis = basis.ToString(),
            PricingBasisLabel = BenchmarkModelComparison.DescribeBasis(basis, today),
            ComputedAtUtc = computedAtUtc,
            BaselineSuiteId = null,
            BaselineSuiteName = null,
            BaselineBatteryName = baselineSources.FirstOrDefault()?.BatteryName,
            BaselineEntryKeys = baseline?.EntryKeys.ToList() ?? new List<string>(),
            BaselineKeyValues = baselineKeyValues,
            BaselineSignature = baseline?.ComparabilityClassSha256 ?? string.Empty,
            ModelAxisKeys = BenchmarkCrossModelComparability.ModelAxisKeys.ToList(),
            Entries = entries,
            ComparableCount = comparableCount,
            ExcludedCount = excludedCount,
            ThinkingLevelsDiffer = thinkingLevelsDiffer,
            SpeedAxisCaveat = thinkingLevelsDiffer ? BenchmarkCrossModelComparability.ThinkingLevelSpeedCaveat : null,
            Explanation = members.Count == 0
                ? "No entries: a comparison is undefined over an empty set."
                : excludedCount == 0
                    ? $"All {comparableCount} battery results share one battery definition and comparability class and may be charted together."
                    : $"{comparableCount} of {members.Count} battery results share one battery definition and comparability "
                      + $"class and may be charted; {excludedCount} cannot and are listed with the reason.",
            ExcludedMeasures = BenchmarkModelComparison.ExcludedMeasures(comparableCount).ToList(),
            PanelDiagnostics = null
        };
    }

    /// <summary>
    /// The degrading keys whose value differs across the member runs of the baseline results, each
    /// flagging the axes its own key entry says it affects. The pricing snapshot is ignored on a
    /// repriced comparison.
    /// </summary>
    private static (List<string> Speed, List<string> Cost) SetDegradation(
        IReadOnlyList<BenchmarkBatteryComparisonSource> baselineSources, bool repriced)
    {
        var speed = new List<string>();
        var cost = new List<string>();

        var keysByRun = baselineSources
            .SelectMany(s => s.Runs)
            .GroupBy(r => r.Id)
            .Select(g => BenchmarkComparabilityKey.Extract(g.First())
                .Where(k => BenchmarkCrossModelComparability.IsDegradingKey(k.Name))
                .ToList())
            .ToList();

        foreach (string name in BenchmarkCrossModelComparability.DegradingKeys)
        {
            if (repriced && name == BenchmarkComparabilityKey.PricingSnapshotKey) continue;

            var values = keysByRun
                .Select(keys => keys.FirstOrDefault(k => k.Name == name))
                .Where(k => k != null)
                .Select(k => k!)
                .ToList();

            if (values.Select(k => k.Value).Distinct(StringComparer.Ordinal).Count() <= 1) continue;

            if (values[0].DegradesSpeed) speed.Add(name);
            if (values[0].DegradesCost) cost.Add(name);
        }

        return (speed, cost);
    }

    /// <summary>The newest identity run, which labels the entry.</summary>
    private static BenchmarkRun? Representative(BenchmarkBatteryComparisonSource source)
        => source.IdentityRuns.OrderBy(r => r.StartedAtUtc).LastOrDefault();

    private static string ThinkingLevelOf(BenchmarkRun? run)
        => run == null
            ? BenchmarkComparabilityKey.NoValue
            : BenchmarkComparabilityKey.Extract(run)
                .FirstOrDefault(k => k.Name == BenchmarkComparabilityKey.CandidateThinkingLevelKey)?.Value
              ?? BenchmarkComparabilityKey.NoValue;

    /// <summary>
    /// The identity fields as a run or group entry carries them, read from the newest usable member,
    /// with the battery's own fields beside them and no suite.
    /// </summary>
    private static BenchmarkModelComparisonEntryDto Identity(BenchmarkBatteryComparisonSource source, bool thinkingLevelInLabel)
    {
        var entry = BenchmarkModelComparison.Identity(
            new BenchmarkModelComparisonSource
            {
                Key = source.Key,
                SourceKind = SourceKind,
                SourceId = source.BatteryRunId,
                SourceName = source.BatteryName,
                Runs = source.IdentityRuns
            },
            thinkingLevelInLabel);

        entry.RunIds = source.Runs.Select(r => r.Id).OrderBy(id => id).ToList();
        entry.RunCount = entry.RunIds.Count;
        entry.SuiteId = null;
        entry.SuiteName = null;
        entry.BatteryRunId = source.BatteryRunId;
        entry.BatteryName = source.BatteryName;
        entry.BatteryDefinitionSha256 = source.DefinitionSha256;
        entry.BatteryComparabilityClassSha256 = source.ComparabilityClassSha256;
        entry.SuiteCount = source.SuiteCount;
        entry.RunsPerSuite = source.RunsPerSuite;
        return entry;
    }

    /// <summary>A result that cannot be measured: identity, the reason, and no figures.</summary>
    private static BenchmarkModelComparisonEntryDto BuildRefusedEntry(BenchmarkBatteryComparisonSource source)
    {
        var entry = Identity(source, thinkingLevelInLabel: false);
        entry.State = "Excluded";
        entry.Excluded = true;
        entry.Comparable = false;
        entry.Explanation = source.Refusal
            ?? "Excluded: this battery result has no usable member runs, so nothing about it can be measured.";
        return entry;
    }

    /// <summary>A measurable result outside the baseline condition: identity, what differs, and no figures.</summary>
    private static BenchmarkModelComparisonEntryDto BuildExcludedEntry(
        BenchmarkBatteryComparisonSource source,
        BenchmarkBatteryComparisonBaseline baseline,
        IReadOnlyList<BenchmarkBatteryComparisonSource> baselineSources,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? distinguishingKeys)
    {
        var entry = Identity(source, thinkingLevelInLabel: false);
        entry.State = "Excluded";
        entry.Excluded = true;
        entry.Comparable = false;

        var baselineRunIds = baselineSources
            .SelectMany(s => s.Runs.Select(r => r.Id))
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        var ownRunIds = source.Runs.Select(r => r.Id).OrderBy(id => id).ToList();

        BenchmarkComparabilityKeyDifference difference;
        if (!string.Equals(source.DefinitionSha256, baseline.DefinitionSha256, StringComparison.Ordinal))
        {
            entry.ExcludingKeys = new List<string> { BatteryDefinitionKey };
            difference = Difference(BatteryDefinitionKey, BenchmarkComparabilityKeyKind.Fundamental,
                baseline.DefinitionSha256, baselineRunIds, source.DefinitionSha256, ownRunIds);

            var baselineSource = baselineSources.FirstOrDefault();
            entry.Explanation = "Excluded from every chart: this result ran another battery definition, "
                + $"{DescribeDefinition(source.BatteryName, source.DefinitionRevision, source.DefinitionSha256)}, "
                + "than the baseline's "
                + $"{DescribeDefinition(baselineSource?.BatteryName, baselineSource?.DefinitionRevision, baseline.DefinitionSha256)}, "
                + "so its Overall Index is a composite of other suites or weights.";
        }
        else
        {
            var keys = distinguishingKeys != null && distinguishingKeys.TryGetValue(source.Key, out var found)
                ? found.ToList()
                : new List<string>();

            entry.ExcludingKeys = keys.Count > 0 ? keys : new List<string> { BatteryComparabilityClassKey };
            difference = Difference(BatteryComparabilityClassKey, BenchmarkComparabilityKeyKind.Instrument,
                baseline.ComparabilityClassSha256, baselineRunIds, source.ComparabilityClassSha256!, ownRunIds);

            entry.Explanation = "Excluded from every chart: this result was measured in another comparability "
                + "class of the battery, so its suites were produced by a different instrument. "
                + (keys.Count > 0
                    ? "Differing keys: " + string.Join(", ", keys) + "."
                    : "No single must-match key names the difference.");
        }

        entry.Differences = new List<BenchmarkComparabilityDifferenceDto>
        {
            new()
            {
                Name = difference.Name,
                Kind = difference.Kind.ToString(),
                Description = difference.Describe(),
                Variants = difference.Variants.Select(v => new BenchmarkComparabilityVariantDto
                {
                    Value = v.Value,
                    RunIds = v.RunIds.ToList()
                }).ToList()
            }
        };

        return entry;
    }

    private static BenchmarkComparabilityKeyDifference Difference(
        string name,
        BenchmarkComparabilityKeyKind kind,
        string baselineValue,
        IReadOnlyList<long> baselineRunIds,
        string value,
        IReadOnlyList<long> runIds)
        => new()
        {
            Name = name,
            Kind = kind,
            Variants = new[]
            {
                new BenchmarkComparabilityKeyVariant { Value = baselineValue, RunIds = baselineRunIds },
                new BenchmarkComparabilityKeyVariant { Value = value, RunIds = runIds }
            }
        };

    private static string DescribeDefinition(string? name, int? revision, string sha256)
    {
        string hash = sha256.Length > 12 ? sha256[..12] : sha256;
        string label = string.IsNullOrWhiteSpace(name) ? "an unnamed battery" : $"\"{name}\"";
        return revision.HasValue
            ? $"{label} revision {revision.Value} ({hash})"
            : $"{label} ({hash})";
    }

    /// <summary>A result in the baseline condition, measured from its persisted analysis and repriced members.</summary>
    private static BenchmarkModelComparisonEntryDto BuildEntry(
        BenchmarkBatteryComparisonSource source,
        bool thinkingLevelsDiffer,
        IReadOnlyList<string> speedKeys,
        IReadOnlyList<string> costKeys,
        BenchmarkModelComparisonPricingBasis basis,
        DateOnly today)
    {
        var entry = Identity(source, thinkingLevelsDiffer);
        var result = source.Result!;

        bool speedDegraded = speedKeys.Count > 0;
        bool costDegraded = costKeys.Count > 0;

        entry.State = speedDegraded || costDegraded ? "Degraded" : "Comparable";
        entry.Comparable = !speedDegraded && !costDegraded;
        entry.Excluded = false;
        entry.SpeedDegraded = speedDegraded;
        entry.CostDegraded = costDegraded;
        entry.SpeedDegradingKeys = speedKeys.ToList();
        entry.CostDegradingKeys = costKeys.ToList();
        entry.Explanation = Explain(speedKeys, costKeys);

        string? speedReason = speedDegraded ? "Across the compared battery results: " + string.Join(", ", speedKeys) : null;
        string? costReason = costDegraded ? "Across the compared battery results: " + string.Join(", ", costKeys) : null;

        var cost = BuildCost(source, basis, today, costDegraded, costReason);

        entry.Quality = BuildQuality(result);
        entry.Speed = BuildSpeed(result, speedDegraded, speedReason);
        entry.Cost = cost.Dto;
        entry.Table = BuildTable(source, result, cost.CandidatePerPass);

        return entry;
    }

    private static string Explain(IReadOnlyList<string> speedKeys, IReadOnlyList<string> costKeys)
    {
        if (speedKeys.Count == 0 && costKeys.Count == 0)
        {
            return "Comparable: the battery definition and comparability class match the baseline.";
        }

        var parts = new List<string>();
        if (speedKeys.Count > 0) parts.Add("speed (" + string.Join(", ", speedKeys) + ")");
        if (costKeys.Count > 0) parts.Add("cost (" + string.Join(", ", costKeys) + ")");

        return "Plotted with a degraded axis: quality is sound, and " + string.Join(" and ", parts)
            + " mix conditions across the set.";
    }

    private static BenchmarkModelComparisonQualityDto BuildQuality(BenchmarkBatteryStatisticsResult result)
    {
        var index = result.OverallIndex!;
        int examItems = result.Suites.Sum(s => s.ExamItemCount);
        int scoredItems = result.Suites.Sum(s => s.ScoredItemCount);

        return new BenchmarkModelComparisonQualityDto
        {
            PointEstimate = index.PointEstimate,
            ItemCount = scoredItems,
            ExamItemCount = examItems,
            UnscoredItemCount = Math.Max(0, examItems - scoredItems),
            IntervalHalfWidth = index.CombinedHalfWidth,
            IntervalLower = index.CombinedLower,
            IntervalUpper = index.CombinedUpper,
            IntervalTruncated = index.CombinedIntervalTruncated,
            ItemSamplingHalfWidth = index.ItemSamplingHalfWidth,
            ReproducibilityHalfWidth = index.ReproducibilityHalfWidth,
            ReproducibilityStandardDeviation = RoundStandardDeviation(index),
            ReproducibilityAvailable = index.ReproducibilitySource != BenchmarkBatteryReproducibilitySource.NotAvailable,
            IntervalBasis = index.ReproducibilitySource switch
            {
                BenchmarkBatteryReproducibilitySource.Rounds => IntervalBasisAcrossRounds,
                BenchmarkBatteryReproducibilitySource.PerSuiteFallback => IntervalBasisPerSuiteFallback,
                _ => IntervalBasisItemSamplingOnly
            }
        };
    }

    /// <summary>Sample standard deviation of the per-round composites; null unless reproducibility comes from rounds.</summary>
    private static double? RoundStandardDeviation(BenchmarkBatteryOverallIndex index)
    {
        return index.ReproducibilitySource == BenchmarkBatteryReproducibilitySource.Rounds
            ? BenchmarkGroupStatistics.SampleStandardDeviation(index.PerRoundIndices.Select(r => r.Index).ToList())
            : null;
    }

    /// <summary>
    /// The battery's pooled speed figures. Total model time is per battery pass: the sum over suites
    /// of each suite's mean per-run total, with no standard deviation.
    /// </summary>
    private static BenchmarkModelComparisonSpeedDto BuildSpeed(
        BenchmarkBatteryStatisticsResult result, bool degraded, string? degradedReason)
    {
        var speed = result.Speed;

        return new BenchmarkModelComparisonSpeedDto
        {
            TtftP50Ms = speed?.TtftP50Ms,
            TtftP90Ms = speed?.TtftP90Ms,
            TtftAnswerCount = speed?.TtftAnswerCount ?? 0,
            ModelTimeP50Ms = speed?.ModelTimeP50Ms,
            ModelTimeP90Ms = speed?.ModelTimeP90Ms,
            ModelTimeMeanMs = speed?.ModelTimeMeanMs,
            TotalModelTimePerRunMeanMs = SumOrNull(result.Suites.Select(s => s.Statistics?.Speed.TotalModelTimePerRunMeanMs)),
            TotalModelTimeSdMs = null,
            PooledAnswerCount = speed?.PooledAnswerCount ?? 0,
            Degraded = degraded,
            DegradedReason = degradedReason,
            Caveat = BenchmarkGroupStatistics.SpeedCaveat + (degraded
                ? BenchmarkGroupStatistics.SpeedDegradedCaveatPrefix + degradedReason
                : BenchmarkGroupStatistics.SpeedNotDegradedCaveat)
        };
    }

    /// <summary>
    /// Cost recomputed from the member runs on the comparison's basis. A battery pass is the sum
    /// over suites of the suite's mean per run, for the candidate, the questions asked and the run
    /// total alike; an unknown cost anywhere leaves the figure unknown rather than understated.
    /// </summary>
    private static (BenchmarkModelComparisonCostDto Dto, double? CandidatePerPass) BuildCost(
        BenchmarkBatteryComparisonSource source,
        BenchmarkModelComparisonPricingBasis basis,
        DateOnly today,
        bool degraded,
        string? degradedReason)
    {
        var servedTiers = source.AnswerSummaries.ToDictionary(kv => kv.Key, kv => kv.Value.ServedServiceTier);

        bool resolved = source.Suites.Any(s => s.Runs.Count > 0);
        ModelPricing? card = null;
        double candidatePass = 0.0;
        double candidateTotal = 0.0;

        double askedPass = 0.0;
        bool askedKnown = true;

        double? totalPass = 0.0;
        string? totalReason = null;

        foreach (var suite in source.Suites.Where(s => s.Runs.Count > 0))
        {
            var suiteSource = new BenchmarkModelComparisonSource
            {
                Key = source.Key,
                SourceKind = SourceKind,
                SourceId = source.BatteryRunId,
                SourceName = source.BatteryName,
                Runs = suite.Runs,
                CandidatePricing = source.CandidatePricing,
                RunPricing = source.RunPricing
            };

            var costs = BenchmarkModelComparison.BuildCosts(suiteSource, out bool suiteResolved, out var suiteCard, servedTiers);
            card ??= suiteCard;
            if (suiteResolved && costs.Count > 0)
            {
                candidatePass += costs.Average(c => c.Total);
                candidateTotal += costs.Sum(c => c.Total);
            }
            else
            {
                resolved = false;
            }

            if (suite.Runs.All(r => source.AnswerSummaries.ContainsKey(r.Id)))
            {
                askedPass += suite.Runs.Average(r => (double)source.AnswerSummaries[r.Id].AnswerRowCount);
            }
            else
            {
                askedKnown = false;
            }

            if (totalPass.HasValue)
            {
                var total = BenchmarkModelComparison.BuildTotalRunCost(suiteSource, servedTiers);
                if (total.Mean.HasValue)
                {
                    totalPass += total.Mean.Value;
                }
                else
                {
                    totalPass = null;
                    totalReason = total.Reason;
                }
            }
        }

        if (!source.Suites.Any(s => s.Runs.Count > 0))
        {
            totalPass = null;
            totalReason = "The entry has no runs.";
        }

        double? asked = resolved && askedKnown && askedPass > 0.0 ? askedPass : null;
        var scheduled = BenchmarkModelComparison.UpcomingScheduledChange(card, today);

        var dto = new BenchmarkModelComparisonCostDto
        {
            CandidateCostPerQuestionUsd = resolved && asked.HasValue ? candidatePass / asked.Value : null,
            CandidateCostPerRunUsd = resolved ? candidatePass : null,
            CandidateTotalCostUsd = resolved ? candidateTotal : null,
            TotalRunCostPerRunUsd = totalPass,
            TotalRunCostSdUsd = null,
            TotalRunCostUnavailableReason = totalPass.HasValue ? null : totalReason,
            QuestionsAskedPerRun = asked,
            Basis = basis.ToString(),
            PricingAsOf = card?.AsOf,
            PricingResolved = resolved,
            Degraded = degraded,
            DegradedReason = degradedReason,
            ScheduledChangeEffectiveFrom = scheduled?.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ScheduledChangeNote = scheduled?.Note
        };

        return (dto, resolved ? candidatePass : null);
    }

    private static BenchmarkModelComparisonTableDto BuildTable(
        BenchmarkBatteryComparisonSource source,
        BenchmarkBatteryStatisticsResult result,
        double? candidatePerPass)
    {
        // The single-run report's saturation predicate, over every usable member's Ok answers.
        int scored = source.Runs.Sum(r => source.AnswerSummaries.GetValueOrDefault(r.Id)?.SpeedScoredAnswerCount ?? 0);
        int ceiling = source.Runs.Sum(r => source.AnswerSummaries.GetValueOrDefault(r.Id)?.SpeedCeilingAnswerCount ?? 0);

        double index = result.OverallIndex?.PointEstimate ?? 0.0;

        return new BenchmarkModelComparisonTableDto
        {
            MeanSpeedIndex = result.Speed?.OverallSpeedIndex,
            SpeedIndexSaturated = scored > 0 && ceiling * 2 >= scored,
            SpeedIndexCeilingAnswerCount = ceiling,
            SpeedIndexScoredAnswerCount = scored,
            CostPerIndexPointUsd = candidatePerPass.HasValue && index > 0.0 ? candidatePerPass.Value / index : null,
            MeanStoredQualityIndex = CountWeighted(result, s => s.Statistics?.Index.MeanStoredQualityIndex),
            UnstableItemCount = result.Suites.Sum(s => s.Statistics?.UnstableQuestionIds.Count ?? 0)
        };
    }

    /// <summary><c>Σ v_s · x_s / Σ v_s</c> over the suites; null when any suite lacks its count weight or its figure.</summary>
    private static double? CountWeighted(
        BenchmarkBatteryStatisticsResult result, Func<BenchmarkBatterySuiteProfile, double?> figure)
    {
        if (result.Suites.Count == 0) return null;

        double weighted = 0.0;
        double weights = 0.0;
        for (int i = 0; i < result.Suites.Count; i++)
        {
            var suite = result.Suites[i];
            double? weight = suite.CountWeight ?? (i < result.CountWeights.Count ? (double?)result.CountWeights[i] : null);
            double? value = figure(suite);
            if (!weight.HasValue || !value.HasValue) return null;

            weighted += weight.Value * value.Value;
            weights += weight.Value;
        }

        return weights > 0.0 ? weighted / weights : null;
    }

    private static double? SumOrNull(IEnumerable<double?> values)
    {
        double sum = 0.0;
        bool any = false;
        foreach (var value in values)
        {
            if (!value.HasValue) return null;
            sum += value.Value;
            any = true;
        }

        return any ? sum : null;
    }
}
