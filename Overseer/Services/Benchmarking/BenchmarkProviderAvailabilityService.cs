namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>A lineage link of a pool: <paramref name="RunId"/> was replaced by <paramref name="ReplacedByRunId"/>.</summary>
public sealed record ProviderAvailabilityLink(long RunId, long ReplacedByRunId, BenchmarkReplacementCause? Cause);

/// <summary>
/// The provider availability of a pool of runs, read from the attempt ledger: the summary, the runs of
/// the pool (the selected runs and their lineage predecessors), the lineage links between them, and the
/// HTTP statuses seen per outcome class, most frequent first, at most three.
/// </summary>
public sealed record ProviderAvailabilityPool(
    ProviderAvailabilitySummary Summary,
    IReadOnlyList<RunLineageRow> Runs,
    IReadOnlyList<ProviderAvailabilityLink> Replaces,
    IReadOnlyDictionary<ProviderAttemptOutcome, IReadOnlyList<int>> HttpStatusesByOutcome);

/// <summary>
/// One member row of a battery run with its own run's figures. Re-runs are the run's own repair passes
/// plus one when it replaced a run of the pool, so the rows sum to the battery's re-runs needed.
/// </summary>
public sealed record ProviderAvailabilityMemberRow(
    long MemberId,
    long RunId,
    int SuiteIndex,
    int Round,
    bool Superseded,
    bool Recorded,
    int ValidAttempts,
    int ProviderErrors,
    int UnattributedErrors,
    double? ProviderErrorRate,
    int RerunsNeeded,
    int ProviderRerunsNeeded);

/// <summary>A battery run's pool (every member run, superseded ones included, and their predecessors) and its member rows.</summary>
public sealed record BatteryProviderAvailability(ProviderAvailabilityPool Pool, IReadOnlyList<ProviderAvailabilityMemberRow> Members);

/// <summary>
/// The card-list figures of a pool, summed from the runs' rollup columns, never from the ledger. The
/// attempt figures count recorded runs only; the re-run figures every run of the pool.
/// </summary>
public sealed record ProviderAvailabilityCard(
    int RecordedRuns,
    int TotalRuns,
    int ReplacedRunsIncluded,
    int ValidAttempts,
    int ProviderErrors,
    int UnattributedErrors,
    int OurSideFailures,
    int RerunsNeeded,
    int ProviderRerunsNeeded)
{
    public bool Recorded => RecordedRuns > 0;

    /// <summary>Provider errors ÷ valid attempts; null when not recorded or without a valid attempt.</summary>
    public double? ProviderErrorRate => Recorded && ValidAttempts > 0 ? (double)ProviderErrors / ValidAttempts : null;

    public static ProviderAvailabilityCard None { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Card figures for any pool of the runs one <see cref="BenchmarkProviderAvailabilityService.LoadCardsAsync"/>
/// call loaded: the rollup columns of those runs and of every lineage predecessor.
/// </summary>
public sealed class ProviderAvailabilityCardSource
{
    private readonly IReadOnlyDictionary<long, BenchmarkProviderAvailabilityService.RunFacts> _facts;

    internal ProviderAvailabilityCardSource(IReadOnlyDictionary<long, BenchmarkProviderAvailabilityService.RunFacts> facts)
    {
        _facts = facts;
    }

    /// <summary>One run and its lineage predecessors.</summary>
    public ProviderAvailabilityCard ForRun(long runId) => ForPool(new[] { runId });

    /// <summary>The given runs and their lineage predecessors, each run counted once.</summary>
    public ProviderAvailabilityCard ForPool(IEnumerable<long> runIds)
    {
        var selected = runIds.Where(_facts.ContainsKey).ToHashSet();
        if (selected.Count == 0) return ProviderAvailabilityCard.None;

        var pool = BenchmarkProviderAvailabilityService.WithPredecessors(selected, _facts);
        var recorded = pool.Select(id => _facts[id]).Where(f => f.Recorded).ToList();
        var reruns = BenchmarkProviderAvailability.CountReruns(pool.Select(id => _facts[id].ToLineageRow(selected.Contains(id), 0)));

        return new ProviderAvailabilityCard(
            RecordedRuns: recorded.Count,
            TotalRuns: pool.Count,
            ReplacedRunsIncluded: pool.Count(id => !selected.Contains(id)),
            ValidAttempts: recorded.Sum(f => f.ValidAttempts ?? 0),
            ProviderErrors: recorded.Sum(f => f.ProviderErrors ?? 0),
            UnattributedErrors: recorded.Sum(f => f.UnattributedErrors ?? 0),
            OurSideFailures: recorded.Sum(f => f.OurSideFailures ?? 0),
            RerunsNeeded: reruns.RerunsNeeded,
            ProviderRerunsNeeded: reruns.ProviderRerunsNeeded);
    }
}

/// <summary>
/// Loads GnollBench provider availability from the database (docs/overseer/ai-benchmark-provider-availability.md):
/// the attempt ledger for the dialogs and documents, the rollup columns for card lists. Every pool takes
/// in the lineage predecessors of its runs, walked through <see cref="BenchmarkRun.ReplacesRunId"/> and
/// loaded separately, since a comparison never admits a failed or canceled run itself. Read-only.
/// </summary>
public class BenchmarkProviderAvailabilityService
{
    private readonly ApplicationDbContext _db;

    public BenchmarkProviderAvailabilityService(ApplicationDbContext db)
    {
        _db = db;
    }

    // --- Ledger-based (dialogs, documents) ------------------------------------------------------------

    /// <summary>One run and its lineage predecessors. A missing run gives a pool with no runs.</summary>
    public Task<ProviderAvailabilityPool> ForRunAsync(long runId, CancellationToken ct = default)
        => ForRunsAsync(new[] { runId }, ct);

    /// <summary>A set of runs (a comparison entry's runs, a group) and their lineage predecessors.</summary>
    public Task<ProviderAvailabilityPool> ForRunsAsync(IEnumerable<long> runIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runIds);
        var selected = runIds.Distinct().ToList();
        return PoolAsync(selected, Array.Empty<long>(), ct);
    }

    /// <summary>
    /// A battery run: every member run, superseded ones included, and their lineage predecessors, with
    /// one row per member in suite, round and insertion order. A superseded member's run counts as a
    /// replaced run unless it is also a live member. Null when the battery run does not exist.
    /// </summary>
    public async Task<BatteryProviderAvailability?> ForBatteryRunAsync(long batteryRunId, CancellationToken ct = default)
    {
        bool exists = await _db.BenchmarkBatteryRuns.AsNoTracking().AnyAsync(b => b.Id == batteryRunId, ct);
        if (!exists) return null;

        var members = await _db.BenchmarkBatteryRunMembers
            .AsNoTracking()
            .Where(m => m.BenchmarkBatteryRunId == batteryRunId)
            .OrderBy(m => m.SuiteIndex)
            .ThenBy(m => m.Round)
            .ThenBy(m => m.AddedAtUtc)
            .ThenBy(m => m.Id)
            .Select(m => new { m.Id, m.BenchmarkRunId, m.SuiteIndex, m.Round, m.Superseded })
            .ToListAsync(ct);

        var live = members.Where(m => !m.Superseded).Select(m => m.BenchmarkRunId).Distinct().ToList();
        var superseded = members.Where(m => m.Superseded).Select(m => m.BenchmarkRunId).Distinct().ToList();

        var loaded = await LoadPoolAsync(live, superseded, ct);
        var pool = Build(loaded);

        var rowsByRun = loaded.Attempts.GroupBy(r => r.RunId).ToDictionary(g => g.Key, g => g.ToList());
        var rows = new List<ProviderAvailabilityMemberRow>();
        foreach (var member in members)
        {
            if (!loaded.Facts.TryGetValue(member.BenchmarkRunId, out var facts))
            {
                rows.Add(new ProviderAvailabilityMemberRow(
                    member.Id, member.BenchmarkRunId, member.SuiteIndex, member.Round, member.Superseded,
                    false, 0, 0, 0, null, 0, 0));
                continue;
            }

            var own = BenchmarkProviderAvailability.Summarize(
                rowsByRun.GetValueOrDefault(member.BenchmarkRunId) ?? new List<ProviderAttemptRow>(),
                new[] { facts.ToLineageRow(true, 0) });

            bool replacesInPool = facts.ReplacesRunId is long predecessor
                                  && predecessor != facts.RunId
                                  && loaded.Facts.ContainsKey(predecessor);
            int reruns = facts.RepairPassCount + (replacesInPool ? 1 : 0);
            int providerReruns = facts.ProviderRepairPassCount
                                 + (replacesInPool && facts.ReplacementCause == BenchmarkReplacementCause.ProviderFailure ? 1 : 0);

            rows.Add(new ProviderAvailabilityMemberRow(
                member.Id, member.BenchmarkRunId, member.SuiteIndex, member.Round, member.Superseded,
                own.Recorded, own.ValidAttempts, own.ProviderErrors, own.UnattributedErrors, own.ProviderErrorRate,
                reruns, providerReruns));
        }

        return new BatteryProviderAvailability(pool, rows);
    }

    // --- Rollup-based (card lists) --------------------------------------------------------------------

    /// <summary>
    /// The rollup columns of <paramref name="runIds"/> and of every lineage predecessor, in one query
    /// per lineage depth, for card figures of any pool of those runs. Never reads the ledger.
    /// </summary>
    public async Task<ProviderAvailabilityCardSource> LoadCardsAsync(IEnumerable<long> runIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runIds);
        var facts = await LoadFactsWithPredecessorsAsync(runIds.Distinct().ToList(), ct);
        return new ProviderAvailabilityCardSource(facts);
    }

    /// <summary>The card figures of one pool of runs and their predecessors.</summary>
    public async Task<ProviderAvailabilityCard> CardForPoolAsync(IEnumerable<long> runIds, CancellationToken ct = default)
    {
        var ids = runIds.Distinct().ToList();
        return (await LoadCardsAsync(ids, ct)).ForPool(ids);
    }

    // --- DTO projection ---------------------------------------------------------------------------------

    /// <summary>The full figure set the run and battery dialogs and the model comparison read.</summary>
    public static ProviderAvailabilityDto ToDto(ProviderAvailabilityPool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        var s = pool.Summary;

        return new ProviderAvailabilityDto
        {
            Recorded = s.Recorded,
            RecordedRuns = s.RecordedRuns,
            TotalRuns = s.TotalRuns,
            ReplacedRunsIncluded = s.ReplacedRunsIncluded,
            ValidAttempts = s.ValidAttempts,
            ProviderErrors = s.ProviderErrors,
            UnattributedErrors = s.UnattributedErrors,
            OurSideFailures = s.OurSideFailures,
            ProviderErrorRate = s.ProviderErrorRate,
            ProviderErrorRateLow = s.ProviderErrorRateLow,
            ProviderErrorRateHigh = s.ProviderErrorRateHigh,
            DesignEffect = s.DesignEffect,
            InclusiveRate = s.InclusiveRate,
            InclusiveRateLow = s.InclusiveRateLow,
            InclusiveRateHigh = s.InclusiveRateHigh,
            FewAttempts = s.FewAttempts,
            Overloaded = s.Overloaded,
            ServerErrors = s.ServerErrors,
            Timeouts = s.Timeouts,
            StreamFailures = s.StreamFailures,
            RateLimitedOurs = s.RateLimitedOurs,
            RateLimitedUnattributed = s.RateLimitedUnattributed,
            TransportUnattributed = s.TransportUnattributed,
            ByOutcome = Enum.GetValues<ProviderAttemptOutcome>()
                .OrderBy(o => (byte)o)
                .Select(o => new ProviderAvailabilityOutcomeDto
                {
                    Outcome = o.ToString(),
                    Code = (byte)o,
                    Side = SideOf(o),
                    Count = s.CountsByOutcome.GetValueOrDefault(o),
                    HttpStatuses = pool.HttpStatusesByOutcome.TryGetValue(o, out var statuses) ? statuses.ToList() : new List<int>()
                })
                .ToList(),
            QuestionsLost = s.QuestionsLost,
            Calls = s.Calls,
            FirstAttemptSuccessCalls = s.FirstAttemptSuccessCalls,
            RepairPasses = s.RepairPasses,
            ProviderRepairPasses = s.ProviderRepairPasses,
            ReplacementRuns = s.ReplacementRuns,
            ProviderReplacementRuns = s.ProviderReplacementRuns,
            RerunsNeeded = s.RerunsNeeded,
            ProviderRerunsNeeded = s.ProviderRerunsNeeded,
            GraderValidAttempts = s.GraderValidAttempts,
            GraderProviderErrors = s.GraderProviderErrors,
            Bins = s.Bins
                .Select(b => new ProviderAvailabilityBinDto
                {
                    StartUtc = b.StartUtc,
                    Valid = b.Valid,
                    Provider = b.Provider,
                    Unattributed = b.Unattributed,
                    Ours = b.Ours
                })
                .ToList(),
            Replaces = pool.Replaces
                .Select(l => new ProviderAvailabilityLinkDto
                {
                    RunId = l.RunId,
                    ReplacedByRunId = l.ReplacedByRunId,
                    Cause = l.Cause?.ToString()
                })
                .ToList()
        };
    }

    /// <summary>A battery member row; the figures are null when the member's run is not recorded.</summary>
    public static BenchmarkBatteryMemberAvailabilityDto ToDto(ProviderAvailabilityMemberRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new BenchmarkBatteryMemberAvailabilityDto
        {
            MemberId = row.MemberId,
            RunId = row.RunId,
            SuiteIndex = row.SuiteIndex,
            Round = row.Round,
            Superseded = row.Superseded,
            Recorded = row.Recorded,
            ValidAttempts = row.Recorded ? row.ValidAttempts : null,
            ProviderErrors = row.Recorded ? row.ProviderErrors : null,
            UnattributedErrors = row.Recorded ? row.UnattributedErrors : null,
            ProviderErrorRate = row.Recorded ? row.ProviderErrorRate : null,
            RerunsNeeded = row.Recorded ? row.RerunsNeeded : null,
            ProviderRerunsNeeded = row.Recorded ? row.ProviderRerunsNeeded : null
        };
    }

    /// <summary>
    /// Copies a card onto a DTO's compact fields. A pool that records no run reads "not recorded":
    /// <see cref="IProviderAvailabilityCardDto.ProviderAvailabilityRecorded"/> false and every figure null.
    /// </summary>
    public static void ApplyCard(IProviderAvailabilityCardDto dto, ProviderAvailabilityCard card)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(card);

        bool recorded = card.Recorded;
        dto.ProviderAvailabilityRecorded = recorded;
        dto.ProviderRecordedRunCount = card.RecordedRuns;
        dto.ProviderRunCount = card.TotalRuns;
        dto.ProviderValidAttempts = recorded ? card.ValidAttempts : null;
        dto.ProviderErrorCount = recorded ? card.ProviderErrors : null;
        dto.ProviderUnattributedErrorCount = recorded ? card.UnattributedErrors : null;
        dto.ProviderErrorRate = card.ProviderErrorRate;
        dto.RerunsNeeded = recorded ? card.RerunsNeeded : null;
        dto.ProviderRerunsNeeded = recorded ? card.ProviderRerunsNeeded : null;
    }

    /// <summary>The side of an outcome class: <c>ok</c>, <c>provider</c>, <c>unattributed</c> or <c>ours</c>.</summary>
    public static string SideOf(ProviderAttemptOutcome outcome)
        => outcome == ProviderAttemptOutcome.Ok ? "ok"
            : ProviderAttemptOutcomes.IsProvider(outcome) ? "provider"
            : ProviderAttemptOutcomes.IsUnattributed(outcome) ? "unattributed"
            : "ours";

    // --- Loading ---------------------------------------------------------------------------------------

    /// <summary>The columns of one run that lineage and card figures read.</summary>
    internal sealed record RunFacts(
        long RunId,
        long? ReplacesRunId,
        BenchmarkReplacementCause? ReplacementCause,
        bool Recorded,
        int RepairPassCount,
        int ProviderRepairPassCount,
        int? ValidAttempts,
        int? ProviderErrors,
        int? UnattributedErrors,
        int? OurSideFailures)
    {
        public RunLineageRow ToLineageRow(bool selected, int questionsLost) => new(
            RunId, selected, Recorded, ReplacesRunId, ReplacementCause, RepairPassCount, ProviderRepairPassCount, questionsLost);
    }

    private sealed record LoadedPool(
        IReadOnlyDictionary<long, RunFacts> Facts,
        IReadOnlySet<long> Selected,
        IReadOnlyDictionary<long, int> QuestionsLost,
        IReadOnlyList<ProviderAttemptRow> Attempts);

    /// <summary>
    /// <paramref name="ids"/> and every run reachable from them through
    /// <see cref="RunFacts.ReplacesRunId"/> among <paramref name="facts"/>; cycle-safe.
    /// </summary>
    internal static HashSet<long> WithPredecessors(IEnumerable<long> ids, IReadOnlyDictionary<long, RunFacts> facts)
    {
        var pool = new HashSet<long>();
        var stack = new Stack<long>(ids.Where(facts.ContainsKey));
        while (stack.Count > 0)
        {
            long id = stack.Pop();
            if (!pool.Add(id)) continue;

            if (facts[id].ReplacesRunId is long predecessor && facts.ContainsKey(predecessor) && !pool.Contains(predecessor))
            {
                stack.Push(predecessor);
            }
        }

        return pool;
    }

    private async Task<ProviderAvailabilityPool> PoolAsync(
        IReadOnlyCollection<long> selected, IReadOnlyCollection<long> alsoIncluded, CancellationToken ct)
        => Build(await LoadPoolAsync(selected, alsoIncluded, ct));

    private async Task<LoadedPool> LoadPoolAsync(
        IReadOnlyCollection<long> selected, IReadOnlyCollection<long> alsoIncluded, CancellationToken ct)
    {
        var facts = await LoadFactsWithPredecessorsAsync(selected.Concat(alsoIncluded).Distinct().ToList(), ct);
        var selectedSet = selected.Where(facts.ContainsKey).ToHashSet();

        var pool = WithPredecessors(selected.Concat(alsoIncluded), facts);
        var poolFacts = facts.Where(kv => pool.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        var recordedIds = poolFacts.Values.Where(f => f.Recorded).Select(f => f.RunId).ToList();

        var questionsLost = recordedIds.Count == 0
            ? new Dictionary<long, int>()
            : await _db.BenchmarkRunAnswers
                .AsNoTracking()
                .Where(a => recordedIds.Contains(a.BenchmarkRunId)
                            && a.Status == BenchmarkAnswerStatus.ProviderError
                            && a.FailureOrigin == BenchmarkFailureOrigin.Provider)
                .GroupBy(a => a.BenchmarkRunId)
                .Select(g => new { RunId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RunId, x => x.Count, ct);

        var attempts = recordedIds.Count == 0
            ? new List<ProviderAttemptRow>()
            : await _db.BenchmarkProviderAttempts
                .AsNoTracking()
                .Where(a => recordedIds.Contains(a.BenchmarkRunId))
                .Select(a => new ProviderAttemptRow(
                    a.BenchmarkRunId,
                    a.BenchmarkRunAnswerId,
                    a.Source,
                    a.Pass,
                    a.CallIndex,
                    a.AttemptIndex,
                    a.StartedAtUtc,
                    a.DurationMs,
                    a.Outcome,
                    (int?)a.HttpStatus,
                    a.ProviderErrorCode,
                    a.Retried))
                .ToListAsync(ct);

        return new LoadedPool(poolFacts, selectedSet, questionsLost, attempts);
    }

    private static ProviderAvailabilityPool Build(LoadedPool loaded)
    {
        var runs = loaded.Facts.Values
            .OrderBy(f => f.RunId)
            .Select(f => f.ToLineageRow(loaded.Selected.Contains(f.RunId), loaded.QuestionsLost.GetValueOrDefault(f.RunId)))
            .ToList();

        var summary = BenchmarkProviderAvailability.Summarize(loaded.Attempts, runs);

        var links = loaded.Facts.Values
            .Where(f => f.ReplacesRunId is long predecessor && predecessor != f.RunId && loaded.Facts.ContainsKey(predecessor))
            .OrderBy(f => f.RunId)
            .Select(f => new ProviderAvailabilityLink(f.ReplacesRunId!.Value, f.RunId, f.ReplacementCause))
            .ToList();

        var recorded = loaded.Facts.Values.Where(f => f.Recorded).Select(f => f.RunId).ToHashSet();
        var statuses = loaded.Attempts
            .Where(a => a.Source == ModelCallSource.BenchmarkCandidate && a.HttpStatus.HasValue && recorded.Contains(a.RunId))
            .GroupBy(a => a.Outcome)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<int>)g
                    .GroupBy(a => a.HttpStatus!.Value)
                    .OrderByDescending(s => s.Count())
                    .ThenBy(s => s.Key)
                    .Take(3)
                    .Select(s => s.Key)
                    .ToList());

        return new ProviderAvailabilityPool(summary, runs, links, statuses);
    }

    /// <summary>
    /// The facts of <paramref name="runIds"/> and of their predecessors, walked one lineage depth per
    /// query. An id is queried at most once, so a cycle or a deleted predecessor ends the walk.
    /// </summary>
    private async Task<Dictionary<long, RunFacts>> LoadFactsWithPredecessorsAsync(IReadOnlyCollection<long> runIds, CancellationToken ct)
    {
        var facts = new Dictionary<long, RunFacts>();
        var queried = new HashSet<long>();
        var frontier = runIds.Where(queried.Add).ToList();

        while (frontier.Count > 0)
        {
            var batch = await _db.BenchmarkRuns
                .AsNoTracking()
                .Where(r => frontier.Contains(r.Id))
                .Select(r => new RunFacts(
                    r.Id,
                    r.ReplacesRunId,
                    r.ReplacementCause,
                    r.ProviderLedgerVersion != null,
                    r.RepairPassCount,
                    r.ProviderRepairPassCount,
                    r.CandidateValidAttemptCount,
                    r.CandidateProviderErrorCount,
                    r.CandidateUnattributedErrorCount,
                    r.CandidateOurSideFailureCount))
                .ToListAsync(ct);

            foreach (var row in batch)
            {
                facts.TryAdd(row.RunId, row);
            }

            frontier = batch
                .Where(f => f.ReplacesRunId.HasValue)
                .Select(f => f.ReplacesRunId!.Value)
                .Where(queried.Add)
                .ToList();
        }

        return facts;
    }
}
