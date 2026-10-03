namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Services;

/// <summary>
/// Loads a battery run, computes its composite (Statistical Method M2–M7) and persists the result.
///
/// <para>The only place the battery statistics meet the database. The arithmetic is in
/// <see cref="BenchmarkBatteryStatistics"/> and <see cref="BenchmarkGroupStatistics"/>, the
/// comparability rules in <see cref="BenchmarkBatteryComparability"/>, and the usability rule in
/// <see cref="BenchmarkBatteryPlanner"/>; all are pure. This class does the I/O and refuses a
/// composite the comparability verdict refuses.</para>
///
/// <para>Admin- or orchestrator-initiated, and it makes no AI calls.</para>
/// </summary>
public class BenchmarkBatteryAnalysisService
{
    /// <summary>Reason given for a member whose suite index lies outside the definition snapshot.</summary>
    public const string SuiteIndexOutOfRangeReason = "suite index outside the battery definition";

    private readonly ApplicationDbContext _db;
    private readonly ModelPricingService? _pricingService;
    private readonly ILogger<BenchmarkBatteryAnalysisService> _logger;

    public BenchmarkBatteryAnalysisService(
        ApplicationDbContext db,
        ILogger<BenchmarkBatteryAnalysisService> logger,
        ModelPricingService? pricingService = null)
    {
        _db = db;
        _logger = logger;
        _pricingService = pricingService;
    }

    /// <summary>One suite of a loaded battery run: its usable members, which alone enter the statistics, and the rest.</summary>
    public sealed record LoadedBatterySuite
    {
        public BenchmarkBatteryDefinitionSuite Suite { get; init; } = default!;

        public int SuiteIndex => Suite.Index;

        /// <summary>The usable member runs (M4), with answers, in run-id order.</summary>
        public IReadOnlyList<BenchmarkRun> UsableRuns { get; init; } = Array.Empty<BenchmarkRun>();

        /// <summary>The round of every usable member run.</summary>
        public IReadOnlyDictionary<long, int> RoundByRunId { get; init; } = new Dictionary<long, int>();

        /// <summary>The suite's non-superseded members that are not usable, with their reasons.</summary>
        public IReadOnlyList<BenchmarkBatteryExcludedMember> Excluded { get; init; } = Array.Empty<BenchmarkBatteryExcludedMember>();
    }

    /// <summary>Everything a caller needs to analyse or report on one battery run, loaded once.</summary>
    public sealed record LoadedBatteryRun
    {
        /// <summary>The battery run row, tracked, with all its member rows.</summary>
        public BenchmarkBatteryRun BatteryRun { get; init; } = default!;

        /// <summary>The definition snapshot the battery run executes.</summary>
        public BenchmarkBatteryDefinition Definition { get; init; } = default!;

        /// <summary>One entry per definition suite, in suite-index order.</summary>
        public IReadOnlyList<LoadedBatterySuite> Suites { get; init; } = Array.Empty<LoadedBatterySuite>();

        /// <summary>Every non-superseded member that is not usable, with its reason, in suite and round order.</summary>
        public IReadOnlyList<BenchmarkBatteryExcludedMember> Excluded { get; init; } = Array.Empty<BenchmarkBatteryExcludedMember>();

        /// <summary>The run rows of every non-superseded member, usable or not, in run-id order.</summary>
        public IReadOnlyList<BenchmarkRun> MemberRuns { get; init; } = Array.Empty<BenchmarkRun>();

        /// <summary>The usable member run ids, ascending.</summary>
        public IReadOnlyList<long> UsableMemberRunIds { get; init; } = Array.Empty<long>();

        /// <summary>The M8 verdict over the usable members, re-resolved at load.</summary>
        public BenchmarkBatteryComparabilityResult Comparability { get; init; } = default!;

        public bool HasUsableMembers => UsableMemberRunIds.Count > 0;

        /// <summary>The usable runs per suite index, the shape <see cref="BenchmarkBatteryComparability.Resolve"/> takes.</summary>
        public IReadOnlyList<(int SuiteIndex, IReadOnlyList<BenchmarkRun> Runs)> UsableRunsBySuiteIndex
            => Suites.Select(s => (s.SuiteIndex, s.UsableRuns)).ToList();
    }

    // --- Loading -----------------------------------------------------------------------------------

    /// <summary>
    /// Loads a battery run, its non-superseded members with their runs and answers, splits them by
    /// <see cref="BenchmarkBatteryPlanner.IsUsable"/>, and re-resolves the comparability verdict over
    /// the usable ones. Null when the battery run does not exist.
    /// </summary>
    /// <exception cref="JsonException">The stored definition snapshot cannot be read.</exception>
    public async Task<LoadedBatteryRun?> LoadAsync(long batteryRunId, CancellationToken ct = default)
    {
        var batteryRun = await _db.BenchmarkBatteryRuns
            .Include(b => b.Members)
            .FirstOrDefaultAsync(b => b.Id == batteryRunId, ct);

        if (batteryRun == null) return null;

        var definition = BenchmarkBatteryDefinition.FromJson(batteryRun.DefinitionJson);

        var members = batteryRun.Members
            .Where(m => !m.Superseded)
            .OrderBy(m => m.SuiteIndex)
            .ThenBy(m => m.Round)
            .ThenBy(m => m.Id)
            .ToList();

        var runIds = members.Select(m => m.BenchmarkRunId).Distinct().ToList();

        var runs = await _db.BenchmarkRuns
            .AsNoTracking()
            .Include(r => r.Answers)
            .Where(r => runIds.Contains(r.Id))
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var runById = runs.ToDictionary(r => r.Id);
        var definitionSuites = definition.Suites.OrderBy(s => s.Index).ToList();
        var suiteIndices = new HashSet<int>(definitionSuites.Select(s => s.Index));

        var usableBySuite = definitionSuites.ToDictionary(s => s.Index, _ => new List<(BenchmarkRun Run, int Round)>());
        var excludedBySuite = definitionSuites.ToDictionary(s => s.Index, _ => new List<BenchmarkBatteryExcludedMember>());
        var outOfRange = new List<BenchmarkBatteryExcludedMember>();

        foreach (var member in members)
        {
            // A deleted run cascades its member row away, so a missing run here is a race with a delete.
            if (!runById.TryGetValue(member.BenchmarkRunId, out var run)) continue;

            if (!suiteIndices.Contains(member.SuiteIndex))
            {
                outOfRange.Add(new BenchmarkBatteryExcludedMember(member.SuiteIndex, member.Round, run.Id, SuiteIndexOutOfRangeReason));
                continue;
            }

            string? reason = BenchmarkBatteryPlanner.UnusableReason(member, run);
            if (reason == null)
            {
                usableBySuite[member.SuiteIndex].Add((run, member.Round));
            }
            else
            {
                excludedBySuite[member.SuiteIndex].Add(new BenchmarkBatteryExcludedMember(member.SuiteIndex, member.Round, run.Id, reason));
            }
        }

        var suites = definitionSuites
            .Select(s =>
            {
                // A run attached to two slots of one suite would count twice; it enters once, at its first round.
                var usable = usableBySuite[s.Index]
                    .GroupBy(x => x.Run.Id)
                    .Select(g => g.OrderBy(x => x.Round).First())
                    .OrderBy(x => x.Run.Id)
                    .ToList();

                return new LoadedBatterySuite
                {
                    Suite = s,
                    UsableRuns = usable.Select(x => x.Run).ToList(),
                    RoundByRunId = usable.ToDictionary(x => x.Run.Id, x => x.Round),
                    Excluded = excludedBySuite[s.Index]
                };
            })
            .ToList();

        var usableIds = suites
            .SelectMany(s => s.UsableRuns.Select(r => r.Id))
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        var excluded = suites.SelectMany(s => s.Excluded).Concat(outOfRange)
            .OrderBy(e => e.SuiteIndex)
            .ThenBy(e => e.Round)
            .ThenBy(e => e.RunId)
            .ToList();

        return new LoadedBatteryRun
        {
            BatteryRun = batteryRun,
            Definition = definition,
            Suites = suites,
            Excluded = excluded,
            MemberRuns = runs,
            UsableMemberRunIds = usableIds,
            Comparability = BenchmarkBatteryComparability.Resolve(
                suites.Select(s => (s.SuiteIndex, s.UsableRuns)).ToList())
        };
    }

    // --- Analysis ----------------------------------------------------------------------------------

    /// <summary>
    /// Computes the battery composite over the usable members and persists it, with a paired
    /// comparison against <paramref name="compareWithBatteryRunId"/> (the baseline) when one is named.
    ///
    /// <para>Refused, with nothing persisted, when the battery run does not exist, when the usable
    /// members do not form one composite (M8), and when a requested comparison is not eligible (M7);
    /// the error is the explanation to show. An incomplete battery is persisted with
    /// <c>Complete = false</c> and no headline (M4).</para>
    /// </summary>
    public async Task<(BenchmarkBatteryAnalysis? Analysis, BenchmarkBatteryStatisticsResult? Result, BenchmarkBatteryComparison? Comparison, string? Error)>
        AnalyseAsync(long batteryRunId, string? userId, long? compareWithBatteryRunId, CancellationToken ct = default)
    {
        LoadedBatteryRun? loaded;
        try
        {
            loaded = await LoadAsync(batteryRunId, ct);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Battery run {BatteryRunId} has an unreadable definition snapshot.", batteryRunId);
            return (null, null, null, "The battery run's stored definition cannot be read, so it cannot be analysed.");
        }

        if (loaded == null)
        {
            return (null, null, null, "Battery run not found.");
        }

        string? refusal = CompositeRefusal(loaded);
        if (refusal != null)
        {
            return (null, null, null, refusal);
        }

        LoadedBatteryRun? baseline = null;
        bool comparing = compareWithBatteryRunId.HasValue && compareWithBatteryRunId.Value != batteryRunId;
        if (comparing)
        {
            try
            {
                baseline = await LoadAsync(compareWithBatteryRunId!.Value, ct);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Battery run {BatteryRunId} has an unreadable definition snapshot.", compareWithBatteryRunId);
                return (null, null, null, "The baseline battery run's stored definition cannot be read, so no comparison is possible.");
            }

            if (baseline == null)
            {
                return (null, null, null, "The battery run to compare with was not found.");
            }

            var eligibility = Eligibility(baseline, loaded);
            if (!eligibility.Allowed)
            {
                return (null, null, null, eligibility.Explanation);
            }
        }

        var result = await ComputeAsync(loaded, ct);

        BenchmarkBatteryComparison? comparison = null;
        if (baseline != null)
        {
            var baselineResult = await ComputeAsync(baseline, ct);
            comparison = BenchmarkBatteryStatistics.Compare(baselineResult, result);
        }

        string? comparabilityClass = result.Complete
            ? BenchmarkBatteryComparability.ComparabilityClass(
                loaded.Suites.Select(s => (s.Suite.SuiteId, s.UsableRuns)).ToList())
            : null;

        var newest = NewestRun(loaded);

        var analysis = new BenchmarkBatteryAnalysis
        {
            BenchmarkBatteryRunId = loaded.BatteryRun.Id,
            ComputedAtUtc = DateTime.UtcNow,
            MemberRunIdsJson = JsonSerializer.Serialize(loaded.UsableMemberRunIds),
            ResultJson = JsonSerializer.Serialize(result),
            DefinitionSha256 = DefinitionSha256Of(loaded),
            ComparabilityClassSha256 = comparabilityClass,
            Complete = result.Complete,
            HarnessVersion = newest?.HarnessVersion,
            ScoringMethodVersion = newest?.ScoringMethodVersion ?? 0,
            ComparedWithBatteryRunId = comparison != null ? compareWithBatteryRunId : null,
            ComparisonJson = comparison != null ? JsonSerializer.Serialize(comparison) : null,
            ComputedByUserId = string.IsNullOrEmpty(userId) ? null : userId
        };

        _db.BenchmarkBatteryAnalyses.Add(analysis);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Computed battery analysis {AnalysisId} for battery run {BatteryRunId}: {Completed} of {SuiteCount} suites complete over {RunCount} usable runs.",
            analysis.Id, loaded.BatteryRun.Id, result.CompletedSuiteCount, result.SuiteCount, loaded.UsableMemberRunIds.Count);

        return (analysis, result, comparison, null);
    }

    /// <summary>
    /// Whether the two battery runs may be compared (M7), the baseline first. Null when either does
    /// not exist or has an unreadable definition. A side whose own members do not form one composite
    /// (M8) is refused here, before <see cref="BenchmarkBatteryComparability.CanCompare"/> is asked.
    /// </summary>
    public async Task<BenchmarkBatteryComparisonEligibility?> CheckComparisonAsync(
        long baselineBatteryRunId,
        long treatmentBatteryRunId,
        CancellationToken ct = default)
    {
        LoadedBatteryRun? baseline;
        LoadedBatteryRun? treatment;
        try
        {
            baseline = await LoadAsync(baselineBatteryRunId, ct);
            treatment = await LoadAsync(treatmentBatteryRunId, ct);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "A battery run named for comparison has an unreadable definition snapshot.");
            return null;
        }

        if (baseline == null || treatment == null) return null;

        return Eligibility(baseline, treatment);
    }

    /// <summary>The most recent stored analysis of a battery run, or null when none has been computed.</summary>
    public Task<BenchmarkBatteryAnalysis?> GetLatestAsync(long batteryRunId, CancellationToken ct = default)
        => _db.BenchmarkBatteryAnalyses
            .Where(a => a.BenchmarkBatteryRunId == batteryRunId)
            .OrderByDescending(a => a.ComputedAtUtc)
            .ThenByDescending(a => a.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// True when the usable member runs differ from the ones the analysis was computed over. A member
    /// run repaired in place keeps its id, so this cannot see the repair; the result's excluded
    /// members are what say a recompute may change it.
    /// </summary>
    public static bool IsStale(IReadOnlyList<long> usableMemberRunIds, BenchmarkBatteryAnalysis? analysis)
    {
        if (analysis == null) return false;

        long[] analysed;
        try
        {
            analysed = JsonSerializer.Deserialize<long[]>(analysis.MemberRunIdsJson) ?? Array.Empty<long>();
        }
        catch (JsonException)
        {
            return true;
        }

        var current = (usableMemberRunIds ?? Array.Empty<long>()).Distinct().OrderBy(id => id).ToArray();
        return !analysed.Distinct().OrderBy(id => id).SequenceEqual(current);
    }

    /// <inheritdoc cref="IsStale(IReadOnlyList{long}, BenchmarkBatteryAnalysis?)"/>
    public static bool IsStale(LoadedBatteryRun loaded, BenchmarkBatteryAnalysis? analysis)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        return IsStale(loaded.UsableMemberRunIds, analysis);
    }

    // --- Report manifest ---------------------------------------------------------------------------

    /// <summary>The most earlier battery runs <see cref="LoadEarlierRunsAsync"/> returns.</summary>
    public const int EarlierRunLimit = 5;

    /// <summary>
    /// The models the battery run was measured and written with, for the report's Graders block:
    /// every grading role from the newest of <paramref name="usableRuns"/> (see <see cref="GradersOf"/>),
    /// and the report writer from the battery run's configuration, loaded here.
    /// </summary>
    public async Task<BenchmarkBatteryGraders> LoadGradersAsync(
        BenchmarkBatteryRun batteryRun,
        IReadOnlyList<BenchmarkRun> usableRuns,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batteryRun);

        BenchmarkBatteryRoleModel? writer = null;
        if (batteryRun.ReportWriterModelConfigurationId is long writerId)
        {
            writer = await _db.SystemAiApiConfigurations
                .AsNoTracking()
                .Where(c => c.Id == writerId)
                .Select(c => new BenchmarkBatteryRoleModel
                {
                    DisplayName = c.DisplayName,
                    Provider = c.Provider,
                    ModelId = c.ModelId,
                    ThinkingLevel = c.ThinkingLevel,
                    ReasoningMode = c.ReasoningMode,
                    ServiceTier = c.ServiceTier
                })
                .FirstOrDefaultAsync(ct);
        }

        return GradersOf(batteryRun, usableRuns, writer);
    }

    /// <summary>
    /// The Graders block's roles: the model under test, the assessor, the co-assessor, the reader and
    /// the claim verifier as the newest of <paramref name="usableRuns"/> — by start time, then id, the
    /// run the analysis reads its harness version from — recorded them in its snapshots; the report
    /// writer as given.
    /// </summary>
    public static BenchmarkBatteryGraders GradersOf(
        BenchmarkBatteryRun batteryRun,
        IReadOnlyList<BenchmarkRun>? usableRuns,
        BenchmarkBatteryRoleModel? reportWriter)
    {
        ArgumentNullException.ThrowIfNull(batteryRun);

        var newest = (usableRuns ?? Array.Empty<BenchmarkRun>())
            .Where(r => r != null)
            .OrderByDescending(r => r.StartedAtUtc)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();

        var graders = new BenchmarkBatteryGraders
        {
            ReportWriterConfigurationId = batteryRun.ReportWriterModelConfigurationId,
            ReportWriter = reportWriter
        };

        if (newest == null) return graders;

        return graders with
        {
            SourceRunId = newest.Id,
            Panel = BenchmarkRunFinalizer.IsPanelRun(newest),
            ModelUnderTest = BenchmarkBatteryRoleModel.Of(newest.TestedModelSnapshot),
            Assessor = BenchmarkBatteryRoleModel.Of(newest.AssessorModelSnapshot),
            CoAssessor = BenchmarkRunFinalizer.IsPanelRun(newest) ? BenchmarkBatteryRoleModel.Of(newest.CoAssessorModelSnapshot) : null,
            Reader = BenchmarkBatteryRoleModel.Of(newest.SecondOpinionAssessorModelSnapshot),
            ReaderMode = Enum.IsDefined(typeof(BenchmarkSecondOpinionMode), newest.SecondOpinionModeUsed)
                ? (BenchmarkSecondOpinionMode)newest.SecondOpinionModeUsed
                : BenchmarkSecondOpinionMode.Off,
            ReaderGradedAnswerCount = newest.SecondOpinionGradedAnswerCount,
            ReaderQuestionCount = newest.TotalQuestionCount,
            ClaimVerifier = BenchmarkBatteryRoleModel.Of(newest.ClaimVerifierModelSnapshot)
        };
    }

    /// <summary>
    /// Up to <see cref="EarlierRunLimit"/> finished battery runs of the same battery that started
    /// before <paramref name="batteryRun"/>, newest first, each with its latest analysis's harness
    /// version, Overall Index and comparability class. Empty when the battery has been deleted.
    /// </summary>
    public async Task<IReadOnlyList<BenchmarkBatteryEarlierRun>> LoadEarlierRunsAsync(
        BenchmarkBatteryRun batteryRun,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batteryRun);
        if (batteryRun.BenchmarkBatteryId is not long batteryId) return Array.Empty<BenchmarkBatteryEarlierRun>();

        long ownId = batteryRun.Id;
        DateTime ownStart = batteryRun.StartedAtUtc;

        // Finished as BenchmarkBatteryModelComparison.IsFinished reads it.
        var earlier = await _db.BenchmarkBatteryRuns
            .AsNoTracking()
            .Where(r => r.BenchmarkBatteryId == batteryId
                        && r.Id != ownId
                        && r.StartedAtUtc < ownStart
                        && r.Status != BenchmarkRunSeriesStatus.Pending
                        && r.Status != BenchmarkRunSeriesStatus.Running
                        && r.Status != BenchmarkRunSeriesStatus.WaitingForCap)
            .OrderByDescending(r => r.StartedAtUtc)
            .ThenByDescending(r => r.Id)
            .Take(EarlierRunLimit)
            .Select(r => new { r.Id, r.CompletedAtUtc })
            .ToListAsync(ct);

        if (earlier.Count == 0) return Array.Empty<BenchmarkBatteryEarlierRun>();

        var ids = earlier.Select(r => r.Id).ToList();
        var heads = await _db.BenchmarkBatteryAnalyses
            .AsNoTracking()
            .Where(a => ids.Contains(a.BenchmarkBatteryRunId))
            .Select(a => new { a.Id, a.BenchmarkBatteryRunId, a.ComputedAtUtc })
            .ToListAsync(ct);

        var latestIds = heads
            .GroupBy(a => a.BenchmarkBatteryRunId)
            .Select(g => g.OrderByDescending(a => a.ComputedAtUtc).ThenByDescending(a => a.Id).First().Id)
            .ToList();

        var latest = latestIds.Count == 0
            ? new Dictionary<long, BenchmarkBatteryAnalysis>()
            : await _db.BenchmarkBatteryAnalyses
                .AsNoTracking()
                .Where(a => latestIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.BenchmarkBatteryRunId, ct);

        return earlier
            .Select(r =>
            {
                if (!latest.TryGetValue(r.Id, out var analysis))
                {
                    return new BenchmarkBatteryEarlierRun { BatteryRunId = r.Id, FinishedAtUtc = r.CompletedAtUtc };
                }

                var overall = analysis.Complete ? DeserializeResult(analysis)?.OverallIndex : null;
                return new BenchmarkBatteryEarlierRun
                {
                    BatteryRunId = r.Id,
                    FinishedAtUtc = r.CompletedAtUtc,
                    Analysed = true,
                    HarnessVersion = analysis.HarnessVersion,
                    OverallIndex = overall?.PointEstimate,
                    OverallIndexHalfWidth = overall?.CombinedHalfWidth,
                    ComparabilityClassSha256 = analysis.ComparabilityClassSha256
                };
            })
            .ToList();
    }

    /// <summary>Deserializes a stored result; null when absent or malformed.</summary>
    public static BenchmarkBatteryStatisticsResult? DeserializeResult(BenchmarkBatteryAnalysis? analysis)
    {
        if (analysis == null || string.IsNullOrWhiteSpace(analysis.ResultJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<BenchmarkBatteryStatisticsResult>(analysis.ResultJson);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.Message);
            return null;
        }
    }

    /// <summary>Deserializes a stored comparison; null when absent or malformed.</summary>
    public static BenchmarkBatteryComparison? DeserializeComparison(BenchmarkBatteryAnalysis? analysis)
    {
        if (analysis == null || string.IsNullOrWhiteSpace(analysis.ComparisonJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<BenchmarkBatteryComparison>(analysis.ComparisonJson);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.Message);
            return null;
        }
    }

    // --- Helpers -----------------------------------------------------------------------------------

    /// <summary>
    /// The refusal text when the usable members do not form one composite (M8); null when they do,
    /// or when there is no usable member at all — that battery is merely incomplete.
    /// </summary>
    private static string? CompositeRefusal(LoadedBatteryRun loaded)
    {
        if (!loaded.HasUsableMembers || loaded.Comparability.CompositePermitted) return null;

        return "The members of this battery run cannot be combined into one Overall Index. "
            + loaded.Comparability.Explanation;
    }

    /// <summary>M7 for two loaded battery runs, after each side's own M8 verdict.</summary>
    private static BenchmarkBatteryComparisonEligibility Eligibility(LoadedBatteryRun baseline, LoadedBatteryRun treatment)
    {
        string? baselineRefusal = CompositeRefusal(baseline);
        if (baselineRefusal != null)
        {
            return SideRefused($"Comparison refused. The baseline (battery run #{baseline.BatteryRun.Id}) is not one condition: {baselineRefusal}");
        }

        string? treatmentRefusal = CompositeRefusal(treatment);
        if (treatmentRefusal != null)
        {
            return SideRefused($"Comparison refused. The treatment (battery run #{treatment.BatteryRun.Id}) is not one condition: {treatmentRefusal}");
        }

        return BenchmarkBatteryComparability.CanCompare(Side(baseline), Side(treatment));
    }

    private static BenchmarkBatteryComparisonEligibility SideRefused(string explanation)
        => new() { Allowed = false, Kind = null, Explanation = explanation };

    private static BenchmarkBatteryComparisonSide Side(LoadedBatteryRun loaded)
        => new(
            DefinitionSha256Of(loaded),
            loaded.Suites
                .Select(s => new BenchmarkBatteryComparisonSuite(s.SuiteIndex, s.Suite.SuiteId, s.UsableRuns))
                .ToList());

    private static string DefinitionSha256Of(LoadedBatteryRun loaded)
        => string.IsNullOrWhiteSpace(loaded.BatteryRun.DefinitionSha256)
            ? loaded.Definition.DefinitionSha256
            : loaded.BatteryRun.DefinitionSha256;

    /// <summary>The newest usable member run, else the newest member run; the harness and scoring versions are read from it.</summary>
    private static BenchmarkRun? NewestRun(LoadedBatteryRun loaded)
    {
        var usable = loaded.Suites.SelectMany(s => s.UsableRuns).ToList();
        var pool = usable.Count > 0 ? usable : loaded.MemberRuns.ToList();
        return pool
            .OrderByDescending(r => r.StartedAtUtc)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// The statistics for a loaded battery run, not persisted: per suite the exam, the group
    /// statistics under that suite's own comparability, the mass and the pooled answer timings; then
    /// the composite under the battery-wide degrade flags, with the provenance caveats appended.
    /// </summary>
    private async Task<BenchmarkBatteryStatisticsResult> ComputeAsync(LoadedBatteryRun loaded, CancellationToken ct)
    {
        var verdicts = loaded.Comparability.Suites.ToDictionary(v => v.SuiteIndex);
        var inputs = new List<BenchmarkBatterySuiteInput>(loaded.Suites.Count);

        foreach (var suite in loaded.Suites)
        {
            ct.ThrowIfCancellationRequested();
            var runs = suite.UsableRuns;

            if (runs.Count == 0)
            {
                inputs.Add(new BenchmarkBatterySuiteInput(
                    suite.SuiteIndex,
                    null,
                    new BenchmarkBatterySuiteMass(0, 0.0),
                    suite.RoundByRunId,
                    Array.Empty<double>(),
                    Array.Empty<double>(),
                    0,
                    0,
                    suite.Excluded));
                continue;
            }

            var suiteComparability = verdicts.TryGetValue(suite.SuiteIndex, out var verdict)
                ? verdict.Comparability
                : BenchmarkComparabilityKey.Resolve(runs);
            var options = BenchmarkGroupStatisticsOptions.FromComparability(suiteComparability);
            var costs = await BenchmarkGroupAnalysisService.ResolveCostsAsync(_pricingService, _logger, runs);

            // The exam the members sat, from their own answers: the live suite may have changed since.
            var exam = BenchmarkRunExam.Build(runs);
            var statistics = BenchmarkGroupStatistics.Compute(exam.Suite, exam.Questions, runs, costs, options);
            var mass = BenchmarkBatteryDefinition.SuiteMass(exam.Questions, statistics);

            // The same answer set the group's own speed pool reads: Ok answers to the scored items.
            var itemIds = new HashSet<long>(statistics.Items.Select(i => i.QuestionId));
            var pooled = runs
                .SelectMany(r => r.Answers ?? new List<BenchmarkRunAnswer>())
                .Where(a => a.Status == BenchmarkAnswerStatus.Ok
                            && BenchmarkItemAnalysis.QuestionKey(a) is long key
                            && itemIds.Contains(key))
                .ToList();

            inputs.Add(new BenchmarkBatterySuiteInput(
                suite.SuiteIndex,
                statistics,
                mass,
                suite.RoundByRunId,
                pooled.Select(a => (double)a.ModelTimeMs).ToList(),
                pooled.Where(a => a.TimeToFirstTokenMs.HasValue).Select(a => (double)a.TimeToFirstTokenMs!.Value).ToList(),
                runs.Count,
                runs.Max(r => r.TotalQuestionCount),
                suite.Excluded));
        }

        var outOfRange = loaded.Excluded
            .Where(e => e.Reason == SuiteIndexOutOfRangeReason)
            .ToList();

        var result = BenchmarkBatteryStatistics.Compute(loaded.Definition, inputs, StatisticsOptions(loaded));

        var caveats = result.Caveats.ToList();
        if (loaded.HasUsableMembers)
        {
            caveats.AddRange(loaded.Comparability.Caveats);
        }

        return result with
        {
            Caveats = caveats,
            ExcludedMembers = result.ExcludedMembers.Concat(outOfRange).ToList()
        };
    }

    /// <summary>The battery-wide speed and cost degrade flags of the M8 verdict, with the differing keys as the reason.</summary>
    private static BenchmarkBatteryStatisticsOptions StatisticsOptions(LoadedBatteryRun loaded)
    {
        var verdict = loaded.Comparability;
        if (!verdict.CompositePermitted)
        {
            return new BenchmarkBatteryStatisticsOptions();
        }

        var sample = loaded.Suites.SelectMany(s => s.UsableRuns).FirstOrDefault();
        var taxonomy = sample == null
            ? new Dictionary<string, BenchmarkComparabilityKeyEntry>(StringComparer.Ordinal)
            : BenchmarkComparabilityKey.Extract(sample).ToDictionary(k => k.Name, StringComparer.Ordinal);

        string? Reason(Func<BenchmarkComparabilityKeyEntry, bool> affects)
        {
            var parts = verdict.SpeedAndCostDifferences
                .Where(d => taxonomy.TryGetValue(d.Name, out var key) && affects(key))
                .Select(d => d.Describe())
                .ToList();
            return parts.Count > 0 ? string.Join("; ", parts) : null;
        }

        return new BenchmarkBatteryStatisticsOptions
        {
            SpeedDegraded = verdict.SpeedDegraded,
            SpeedDegradedReason = verdict.SpeedDegraded ? Reason(k => k.DegradesSpeed) : null,
            CostDegraded = verdict.CostDegraded,
            CostDegradedReason = verdict.CostDegraded ? Reason(k => k.DegradesCost) : null
        };
    }
}
