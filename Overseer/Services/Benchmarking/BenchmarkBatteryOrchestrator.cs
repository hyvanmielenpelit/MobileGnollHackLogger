namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Sentry;

/// <summary>
/// The outcome of asking to start or resume a battery run, in the vocabulary the controller maps to HTTP.
/// </summary>
public enum BenchmarkBatteryStartOutcome
{
    Started = 0,
    Conflict = 1,
    NotFound = 2,
    Invalid = 3,
    SpendDenied = 4,

    /// <summary>
    /// A resume was refused because the instrument moved under the battery run.
    /// <see cref="BenchmarkBatteryStartResult.ChangedInstrumentHashes"/> names what moved, where it is known.
    /// </summary>
    InstrumentChanged = 5,

    /// <summary>
    /// Candidate and assessor, or candidate and report writer, share a provider and the request did not
    /// acknowledge it; the warning's role says which.
    /// </summary>
    SameProviderNotAcknowledged = 6,

    /// <summary>
    /// The battery run plans more launches than <c>Benchmark:Battery:MaxMembers</c>, or more than the
    /// daily run cap without <c>AllowCapWait</c>.
    /// </summary>
    TooManyMembers = 7
}

public sealed record BenchmarkBatteryStartResult
{
    public BenchmarkBatteryStartOutcome Outcome { get; init; }
    public long? BatteryRunId { get; init; }
    public string? Error { get; init; }
    public SameProviderWarningDto? SameProviderWarning { get; init; }

    /// <summary>The 0-based suite index a per-suite refusal is about; null otherwise.</summary>
    public int? SuiteIndex { get; init; }

    /// <summary>
    /// The instrument hashes that moved, each as <c>HashName (suite 'name')</c>. Empty unless
    /// <see cref="Outcome"/> is InstrumentChanged and the refusal came from a fingerprint.
    /// </summary>
    public IReadOnlyList<string> ChangedInstrumentHashes { get; init; } = Array.Empty<string>();

    public bool Started => Outcome == BenchmarkBatteryStartOutcome.Started;

    public static BenchmarkBatteryStartResult Ok(long batteryRunId) =>
        new() { Outcome = BenchmarkBatteryStartOutcome.Started, BatteryRunId = batteryRunId };

    public static BenchmarkBatteryStartResult Fail(BenchmarkBatteryStartOutcome outcome, string error) =>
        new() { Outcome = outcome, Error = error };
}

/// <summary>The outcome of an attach, a candidate list or a reuse preview, in the vocabulary the controller maps to HTTP.</summary>
public enum BenchmarkBatteryAttachOutcome
{
    Ok = 0,

    /// <summary>The battery, the battery run or the run does not exist.</summary>
    NotFound = 1,

    /// <summary>The battery run is running, being driven, or in a state that takes no attached run.</summary>
    Conflict = 2,

    /// <summary>The run or the slot does not qualify, or the request cannot be judged; the error says why.</summary>
    Ineligible = 3
}

/// <summary>
/// The result of <see cref="BenchmarkBatteryOrchestrator.AttachAsync"/>,
/// <see cref="BenchmarkBatteryOrchestrator.GetAttachCandidatesAsync"/> or
/// <see cref="BenchmarkBatteryOrchestrator.PreviewReuseAsync"/>; the payload of the last two is set
/// only on <see cref="BenchmarkBatteryAttachOutcome.Ok"/>.
/// </summary>
public sealed record BenchmarkBatteryAttachResult
{
    public BenchmarkBatteryAttachOutcome Outcome { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<BenchmarkBatteryAttachCandidateDto>? Candidates { get; init; }
    public BenchmarkBatteryReusePreviewDto? Preview { get; init; }

    public bool Succeeded => Outcome == BenchmarkBatteryAttachOutcome.Ok;

    public static BenchmarkBatteryAttachResult Fail(BenchmarkBatteryAttachOutcome outcome, string error) =>
        new() { Outcome = outcome, Error = error };
}

/// <summary>
/// Drives a <see cref="BenchmarkBatteryRun"/>: every suite of the battery for one model
/// configuration, one member at a time, in round-robin order (<see cref="BenchmarkBatteryPlanner"/>).
///
/// <para>Structured like <see cref="BenchmarkSeriesOrchestrator"/>: the row is the state, the
/// orchestrator is rebuilt from it after a restart, members are launched through
/// <see cref="BenchmarkRunLauncher"/> and therefore through the run gate, and the run manager's
/// orchestrator claim (<c>battery:{id}</c>) keeps every other launch off the gate between members.
/// The claim is taken only once nothing can refuse the start any more, and released in the drive
/// task's <c>finally</c>.</para>
///
/// <para>After every member that finishes usable, two guards run: its five instrument hashes
/// against those recorded for its suite at start, and the battery comparability verdict over every
/// usable member so far. A guard that fails is written to the member's
/// <see cref="BenchmarkBatteryRunMember.GuardFailure"/>, which makes it unusable, and stops the
/// battery run with <see cref="BenchmarkRunSeriesStopReason.InstrumentChanged"/>.</para>
/// </summary>
public class BenchmarkBatteryOrchestrator
{
    private const int GuardFailureMaxLength = 512;
    private const int ErrorMessageMaxLength = 2048;

    /// <summary>The newest runs per suite considered for a slot by the candidate list and the reuse preview.</summary>
    internal const int MaxAttachCandidates = 50;

    private static readonly JsonSerializerOptions StateJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BenchmarkRunManager _runManager;
    private readonly ILogger<BenchmarkBatteryOrchestrator> _logger;

    /// <summary>Cancellation for the battery run being driven, keyed by battery run id.</summary>
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _active = new();

    public BenchmarkBatteryOrchestrator(
        IServiceScopeFactory scopeFactory,
        BenchmarkRunManager runManager,
        ILogger<BenchmarkBatteryOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _runManager = runManager;
        _logger = logger;
    }

    /// <summary>The battery run this process is currently driving, if any.</summary>
    public long? ActiveBatteryRunId => _active.IsEmpty ? null : _active.Keys.First();

    public bool IsDriving(long batteryRunId) => _active.ContainsKey(batteryRunId);

    // ---------------------------------------------------------------------------------------
    // Start
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Validates a battery start, creates the battery run row and begins driving it in the background.
    /// Every suite with a slot to launch is validated through the launcher before anything is written,
    /// so a suite the launcher would refuse fails here rather than on its member.
    ///
    /// <para>The runs of <see cref="StartBenchmarkBatteryRunRequest.Attach"/> are judged by
    /// <see cref="BenchmarkBatteryComparability.CheckAttach"/> against the fingerprints this start
    /// records, in planner order, and inserted with the row as <c>Attached</c> members. One that does
    /// not qualify refuses the whole start. A battery run whose every slot is attached launches
    /// nothing: it is finished here, its analysis included, without taking the claim.</para>
    /// </summary>
    public async Task<BenchmarkBatteryStartResult> StartAsync(
        StartBenchmarkBatteryRunRequest request,
        string? userId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_runManager.CurrentRunId.HasValue)
        {
            return BenchmarkBatteryStartResult.Fail(
                BenchmarkBatteryStartOutcome.Conflict, "A benchmark run is already in progress.");
        }

        if (!_active.IsEmpty)
        {
            return BenchmarkBatteryStartResult.Fail(
                BenchmarkBatteryStartOutcome.Conflict, "A battery run is already in progress.");
        }

        // Refused without taking the claim; it is taken only once the row exists.
        if (_runManager.OrchestratorOwner is { } claimOwner)
        {
            return BenchmarkBatteryStartResult.Fail(
                BenchmarkBatteryStartOutcome.Conflict, BenchmarkRunManager.ClaimConflictMessage(claimOwner));
        }

        if (request.Run == null)
        {
            return BenchmarkBatteryStartResult.Fail(
                BenchmarkBatteryStartOutcome.Invalid, "The run settings are missing from the request.");
        }

        if (request.RunsPerSuite < 1)
        {
            return BenchmarkBatteryStartResult.Fail(
                BenchmarkBatteryStartOutcome.Invalid, "Runs per suite must be at least 1.");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<BenchmarkComplianceGuard>();

        var battery = await db.BenchmarkBatteries
            .Include(b => b.Suites)
            .FirstOrDefaultAsync(b => b.Id == request.BatteryId, ct);
        if (battery == null)
        {
            return BenchmarkBatteryStartResult.Fail(BenchmarkBatteryStartOutcome.NotFound, "Battery not found.");
        }

        if (battery.IsArchived)
        {
            return BenchmarkBatteryStartResult.Fail(
                BenchmarkBatteryStartOutcome.Invalid, $"Battery '{battery.Name}' is archived.");
        }

        var definitionErrors = BenchmarkBatteryDefinition.Validate(battery);
        if (definitionErrors.Count > 0)
        {
            return BenchmarkBatteryStartResult.Fail(
                BenchmarkBatteryStartOutcome.Invalid,
                $"Battery '{battery.Name}' cannot be run: {string.Join(" ", definitionErrors)}");
        }

        var definition = BenchmarkBatteryDefinition.FromEntity(battery);

        var attach = request.Attach ?? new List<BenchmarkBatteryAttachDto>();
        string? attachShapeRefusal = AttachListRefusal(attach, definition.Suites.Count, request.RunsPerSuite);
        if (attachShapeRefusal != null)
        {
            return BenchmarkBatteryStartResult.Fail(BenchmarkBatteryStartOutcome.Invalid, attachShapeRefusal);
        }

        string? boundRefusal = LaunchBoundRefusal(
            definition.Suites.Count, request.RunsPerSuite, attachedCount: attach.Count,
            guard.MaxBatteryMembers, guard.MaxRunsPerDay, request.AllowCapWait);
        if (boundRefusal != null)
        {
            return BenchmarkBatteryStartResult.Fail(BenchmarkBatteryStartOutcome.TooManyMembers, boundRefusal);
        }

        int launches = definition.Suites.Count * request.RunsPerSuite - attach.Count;
        if (launches > 0)
        {
            var (canSpend, denialReason) = await guard.CanSpendAsync(db, ct);
            if (!canSpend)
            {
                return BenchmarkBatteryStartResult.Fail(
                    BenchmarkBatteryStartOutcome.SpendDenied,
                    denialReason ?? "The benchmark spend guard refused this battery run.");
            }
        }

        var attachedSlots = attach.Select(a => (a.SuiteIndex, a.Round)).ToHashSet();
        var launcher = scope.ServiceProvider.GetRequiredService<BenchmarkRunLauncher>();
        foreach (var suite in definition.Suites)
        {
            if (Enumerable.Range(1, request.RunsPerSuite).All(round => attachedSlots.Contains((suite.Index, round))))
            {
                continue;
            }

            var suiteRequest = CloneRequest(request.Run);
            suiteRequest.SuiteId = suite.SuiteId;
            suiteRequest.RunCount = 1;

            var invalid = await launcher.ValidateRequestAsync(suiteRequest, ct);
            if (invalid != null)
            {
                return new BenchmarkBatteryStartResult
                {
                    Outcome = MapLaunchRefusal(invalid.Outcome),
                    Error = invalid.Error == null ? null : $"Suite '{suite.SuiteName}': {invalid.Error}",
                    SameProviderWarning = invalid.SameProviderWarning,
                    SuiteIndex = suite.Index
                };
            }
        }

        var stored = ResolveStoredRequest(request, definition);

        var fingerprints = new Dictionary<int, BenchmarkInstrumentFingerprint?>();
        foreach (var suite in definition.Suites)
        {
            fingerprints[suite.Index] = await ComputeFingerprintAsync(scope.ServiceProvider, db, suite.SuiteId, stored, ct);
        }

        var (attachedMembers, attachRefusal) = await ValidateAttachAtStartAsync(db, definition, stored, fingerprints, attach, ct);
        if (attachRefusal != null)
        {
            return BenchmarkBatteryStartResult.Fail(BenchmarkBatteryStartOutcome.Invalid, attachRefusal);
        }

        var batteryRun = new BenchmarkBatteryRun
        {
            BenchmarkBatteryId = battery.Id,
            BatteryName = battery.Name,
            DefinitionJson = definition.ToJson(),
            DefinitionSha256 = definition.DefinitionSha256,
            RunsPerSuite = request.RunsPerSuite,
            RequestedMemberCount = definition.Suites.Count * request.RunsPerSuite,
            CompletedMemberCount = attachedMembers.Count,
            FailedMemberCount = 0,
            Status = BenchmarkRunSeriesStatus.Pending,
            StartRequestJson = JsonSerializer.Serialize(stored),
            AllowCapWait = request.AllowCapWait,
            SuiteFingerprintsJson = WriteFingerprints(fingerprints),
            StartedByUserId = string.IsNullOrEmpty(userId) ? null : userId,
            StartedAtUtc = DateTime.UtcNow,
            LastProgressAtUtc = DateTime.UtcNow,
            Members = attachedMembers
        };

        db.BenchmarkBatteryRuns.Add(batteryRun);
        await db.SaveChangesAsync(ct);

        if (launches <= 0)
        {
            await FinishAsync(scope.ServiceProvider, db, batteryRun, definition, ct);
            return BenchmarkBatteryStartResult.Ok(batteryRun.Id);
        }

        // The owner token needs the row id, so the claim follows the save; a claim lost to a race
        // removes the row again and refuses the start.
        if (!_runManager.TryClaimOrchestrator(BenchmarkRunManager.BatteryOwner(batteryRun.Id)))
        {
            db.BenchmarkBatteryRuns.Remove(batteryRun);
            await db.SaveChangesAsync(CancellationToken.None);

            return BenchmarkBatteryStartResult.Fail(
                BenchmarkBatteryStartOutcome.Conflict,
                BenchmarkRunManager.ClaimConflictMessage(_runManager.OrchestratorOwner));
        }

        BeginDriving(batteryRun.Id);
        return BenchmarkBatteryStartResult.Ok(batteryRun.Id);
    }

    /// <summary>
    /// Why the attach list of a start cannot be used as given, or null: every entry names a slot of
    /// the grid, no slot twice, and no run twice.
    /// </summary>
    public static string? AttachListRefusal(IReadOnlyList<BenchmarkBatteryAttachDto> attach, int suiteCount, int runsPerSuite)
    {
        var slots = new HashSet<(int, int)>();
        var runs = new HashSet<long>();

        foreach (var entry in attach ?? Array.Empty<BenchmarkBatteryAttachDto>())
        {
            if (entry == null) return "The attach list holds an empty entry.";

            string? outside = BenchmarkBatteryComparability.AttachSlotRefusal(
                suiteCount, runsPerSuite, entry.SuiteIndex, entry.Round, occupant: null, occupantRun: null);
            if (outside != null) return $"Run #{entry.RunId} cannot be attached: {outside}";

            if (!slots.Add((entry.SuiteIndex, entry.Round)))
            {
                return $"Suite {entry.SuiteIndex + 1}, round {entry.Round} is named twice in the attach list.";
            }

            if (!runs.Add(entry.RunId))
            {
                return $"Run #{entry.RunId} is named for two slots; one run fills at most one slot.";
            }
        }

        return null;
    }

    /// <summary>
    /// Judges the attach list of a start in planner order, each run against the fingerprints the
    /// start records and the runs accepted before it, and returns the member rows to insert, or the
    /// refusal naming the first run that does not qualify.
    /// </summary>
    private static async Task<(List<BenchmarkBatteryRunMember> Members, string? Refusal)> ValidateAttachAtStartAsync(
        ApplicationDbContext db,
        BenchmarkBatteryDefinition definition,
        StartBenchmarkRunRequest stored,
        IReadOnlyDictionary<int, BenchmarkInstrumentFingerprint?> fingerprints,
        IReadOnlyList<BenchmarkBatteryAttachDto> attach,
        CancellationToken ct)
    {
        var members = new List<BenchmarkBatteryRunMember>();
        if (attach.Count == 0) return (members, null);

        var runIds = attach.Select(a => a.RunId).Distinct().ToList();
        var runs = await db.BenchmarkRuns.AsNoTracking().Where(r => runIds.Contains(r.Id)).ToListAsync(ct);
        await BenchmarkSeriesOrchestrator.HydrateItemRevisionsAsync(db, runs, ct);
        var byId = runs.ToDictionary(r => r.Id);

        var accepted = new List<(int SuiteIndex, BenchmarkRun Run)>();
        foreach (var entry in attach.OrderBy(a => a.Round).ThenBy(a => a.SuiteIndex))
        {
            var suite = definition.Suites[entry.SuiteIndex];
            string slot = $"suite '{suite.SuiteName}', round {entry.Round}";

            if (!byId.TryGetValue(entry.RunId, out var run))
            {
                return (members, $"Run #{entry.RunId}, chosen for {slot}, was not found. Preview the reuse again.");
            }

            fingerprints.TryGetValue(suite.Index, out var fingerprint);
            var check = BenchmarkBatteryComparability.CheckAttach(
                run, AttachTarget(suite, stored.TestedModelConfigurationId, fingerprint, accepted, accepted.Select(a => a.Run.Id)));
            if (!check.Eligible)
            {
                return (members, $"Run #{entry.RunId}, chosen for {slot}, no longer qualifies: {check.Reason} " +
                                 "Preview the reuse again, or start without reusing it.");
            }

            accepted.Add((suite.Index, run));
            members.Add(new BenchmarkBatteryRunMember
            {
                BenchmarkRunId = run.Id,
                SuiteIndex = suite.Index,
                Round = entry.Round,
                Origin = BenchmarkBatteryMemberOrigin.Attached,
                AddedAtUtc = DateTime.UtcNow
            });
        }

        return (members, null);
    }

    /// <summary>What <see cref="BenchmarkBatteryComparability.CheckAttach"/> judges a run for one suite's slot against.</summary>
    private static BenchmarkBatteryAttachTarget AttachTarget(
        BenchmarkBatteryDefinitionSuite suite,
        long? testedModelConfigurationId,
        BenchmarkInstrumentFingerprint? fingerprint,
        IEnumerable<(int SuiteIndex, BenchmarkRun Run)> usableMembers,
        IEnumerable<long> occupiedRunIds)
        => new()
        {
            SuiteIndex = suite.Index,
            SuiteId = suite.SuiteId,
            SuiteName = suite.SuiteName,
            TestedModelConfigurationId = testedModelConfigurationId,
            Fingerprint = fingerprint,
            ScoringMethodVersion = BenchmarkAssessmentPrompt.ScoringMethodVersion,
            HarnessVersion = BenchmarkAssessmentPrompt.HarnessVersion,
            UsableMembers = usableMembers
                .GroupBy(m => m.SuiteIndex)
                .Select(g => (g.Key, (IReadOnlyList<BenchmarkRun>)g.Select(m => m.Run).ToList()))
                .ToList(),
            OccupiedRunIds = occupiedRunIds.ToHashSet()
        };

    /// <summary>
    /// Why a battery run of this size may not start, or null. The launches it plans (suites × runs
    /// per suite, less the attached slots) may not exceed <paramref name="maxMembers"/>, nor the
    /// daily run cap unless the battery run may wait on the cap.
    /// </summary>
    public static string? LaunchBoundRefusal(
        int suiteCount,
        int runsPerSuite,
        int attachedCount,
        int maxMembers,
        int maxRunsPerDay,
        bool allowCapWait)
    {
        long launches = Math.Max(0L, (long)suiteCount * runsPerSuite - attachedCount);

        if (launches > maxMembers)
        {
            return $"This battery run plans {launches} launches ({suiteCount} suites × {runsPerSuite} runs per suite), " +
                   $"more than the configured maximum of {maxMembers}. Lower Runs per Suite, or raise " +
                   "Benchmark:Battery:MaxMembers.";
        }

        if (launches > maxRunsPerDay && !allowCapWait)
        {
            return $"This battery run plans {launches} launches, more than the daily run cap of {maxRunsPerDay}. " +
                   "Select Allow cap wait to let it run over more than a day, or lower Runs per Suite.";
        }

        return null;
    }

    private static BenchmarkBatteryStartOutcome MapLaunchRefusal(BenchmarkRunLaunchOutcome outcome) => outcome switch
    {
        BenchmarkRunLaunchOutcome.NotFound => BenchmarkBatteryStartOutcome.NotFound,
        BenchmarkRunLaunchOutcome.SpendDenied => BenchmarkBatteryStartOutcome.SpendDenied,
        BenchmarkRunLaunchOutcome.SameProviderNotAcknowledged
            or BenchmarkRunLaunchOutcome.ReportWriterSameProviderNotAcknowledged
            => BenchmarkBatteryStartOutcome.SameProviderNotAcknowledged,
        _ => BenchmarkBatteryStartOutcome.Invalid
    };

    /// <summary>
    /// The five instrument hashes a run of <paramref name="request"/> on suite
    /// <paramref name="suiteId"/> would carry if launched now. Null when the suite or the tested
    /// configuration no longer exists.
    /// </summary>
    protected virtual Task<BenchmarkInstrumentFingerprint?> ComputeFingerprintAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        long suiteId,
        StartBenchmarkRunRequest request,
        CancellationToken ct)
    {
        var benchmarkService = services.GetRequiredService<BenchmarkService>();
        var (verboseMode, allowSourceCodeReferences) = BenchmarkRunLauncher.ResolvePromptSwitches(request);
        return benchmarkService.ComputeCurrentInstrumentFingerprintAsync(
            db, suiteId, request.TestedModelConfigurationId, verboseMode, allowSourceCodeReferences, ct);
    }

    // ---------------------------------------------------------------------------------------
    // Attaching existing runs
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Places an existing run in one (suite, round) slot of a battery run that is not running: Pending
    /// and not driven, Stopped, Completed or CompletedWithErrors. The run must pass
    /// <see cref="BenchmarkBatteryComparability.CheckAttach"/> against the fingerprint recorded for
    /// its suite, and the slot must be empty or held by a member that is not usable, which is
    /// superseded. The member is added as <c>Attached</c> and the usable-slot count recomputed.
    ///
    /// <para>When every slot then holds a usable member, a Pending, Stopped or CompletedWithErrors
    /// battery run is finished as the drive loop finishes one: Completed, replicate groups and the
    /// analysis. A Completed one keeps its status; its latest analysis reads stale until recomputed.</para>
    /// </summary>
    public async Task<BenchmarkBatteryAttachResult> AttachAsync(
        long batteryRunId,
        int suiteIndex,
        int round,
        long runId,
        CancellationToken ct = default)
    {
        if (IsBeingDriven(batteryRunId))
        {
            return BenchmarkBatteryAttachResult.Fail(BenchmarkBatteryAttachOutcome.Conflict,
                "This battery run is running. Attach a run once it has stopped or finished.");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var batteryRun = await db.BenchmarkBatteryRuns.FirstOrDefaultAsync(b => b.Id == batteryRunId, ct);
        if (batteryRun == null)
        {
            return BenchmarkBatteryAttachResult.Fail(BenchmarkBatteryAttachOutcome.NotFound, "Battery run not found.");
        }

        string? statusRefusal = AttachStatusRefusal(batteryRun.Status);
        if (statusRefusal != null)
        {
            return BenchmarkBatteryAttachResult.Fail(BenchmarkBatteryAttachOutcome.Conflict, statusRefusal);
        }

        var (definition, stored, readError) = ReadStoredState(batteryRun);
        if (readError != null)
        {
            return BenchmarkBatteryAttachResult.Fail(BenchmarkBatteryAttachOutcome.Ineligible, readError);
        }

        var run = await db.BenchmarkRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run == null)
        {
            return BenchmarkBatteryAttachResult.Fail(BenchmarkBatteryAttachOutcome.NotFound, $"Run #{runId} not found.");
        }

        var slot = await LoadSlotContextAsync(db, batteryRun, definition!, suiteIndex, round, ct);
        if (slot.Refusal != null)
        {
            return BenchmarkBatteryAttachResult.Fail(BenchmarkBatteryAttachOutcome.Ineligible, slot.Refusal);
        }

        await BenchmarkSeriesOrchestrator.HydrateItemRevisionsAsync(db, new List<BenchmarkRun> { run }, ct);
        var check = BenchmarkBatteryComparability.CheckAttach(run, slot.Target!);
        if (!check.Eligible)
        {
            return BenchmarkBatteryAttachResult.Fail(BenchmarkBatteryAttachOutcome.Ineligible, check.Reason!);
        }

        foreach (var occupant in slot.Occupants)
        {
            occupant.Superseded = true;
        }

        db.BenchmarkBatteryRunMembers.Add(new BenchmarkBatteryRunMember
        {
            BenchmarkBatteryRunId = batteryRun.Id,
            BenchmarkRunId = run.Id,
            SuiteIndex = suiteIndex,
            Round = round,
            Origin = BenchmarkBatteryMemberOrigin.Attached,
            AddedAtUtc = DateTime.UtcNow
        });
        batteryRun.LastProgressAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await RefreshCountsAsync(db, batteryRun, ct);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Run {RunId} attached to battery run {BatteryRunId}, suite index {SuiteIndex}, round {Round}.",
            run.Id, batteryRun.Id, suiteIndex, round);

        bool finishable = batteryRun.Status is BenchmarkRunSeriesStatus.Pending
            or BenchmarkRunSeriesStatus.Stopped
            or BenchmarkRunSeriesStatus.CompletedWithErrors;
        if (finishable && batteryRun.CompletedMemberCount >= batteryRun.RequestedMemberCount)
        {
            await FinishAsync(scope.ServiceProvider, db, batteryRun, definition!, ct);
        }

        return new BenchmarkBatteryAttachResult { Outcome = BenchmarkBatteryAttachOutcome.Ok };
    }

    /// <summary>
    /// The runs that might fill one (suite, round) slot of a battery run: the runs of that suite and
    /// of the battery run's tested configuration, newest first, at most
    /// <see cref="MaxAttachCandidates"/>, each judged by the rule the attach applies, with the reason
    /// when it does not qualify. A slot that takes no run makes every candidate ineligible with that
    /// reason.
    /// </summary>
    public async Task<BenchmarkBatteryAttachResult> GetAttachCandidatesAsync(
        long batteryRunId,
        int suiteIndex,
        int round,
        CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var batteryRun = await db.BenchmarkBatteryRuns.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batteryRunId, ct);
        if (batteryRun == null)
        {
            return BenchmarkBatteryAttachResult.Fail(BenchmarkBatteryAttachOutcome.NotFound, "Battery run not found.");
        }

        var (definition, stored, readError) = ReadStoredState(batteryRun);
        if (readError != null)
        {
            return BenchmarkBatteryAttachResult.Fail(BenchmarkBatteryAttachOutcome.Ineligible, readError);
        }

        string? outside = BenchmarkBatteryComparability.AttachSlotRefusal(
            definition!.Suites.Count, batteryRun.RunsPerSuite, suiteIndex, round, occupant: null, occupantRun: null);
        if (outside != null)
        {
            return BenchmarkBatteryAttachResult.Fail(BenchmarkBatteryAttachOutcome.Ineligible, outside);
        }

        var slot = await LoadSlotContextAsync(db, batteryRun, definition, suiteIndex, round, ct);
        string? stateRefusal = AttachStatusRefusal(batteryRun.Status)
                               ?? (IsBeingDriven(batteryRunId) ? "This battery run is running; attach a run once it has stopped or finished." : null);

        var runs = await CandidateRunsAsync(db, definition.Suites[suiteIndex].SuiteId, stored!.TestedModelConfigurationId, ct);

        var candidates = runs
            .Select(run =>
            {
                string? reason = stateRefusal ?? slot.Refusal;
                var check = reason == null
                    ? BenchmarkBatteryComparability.CheckAttach(run, slot.Target!)
                    : BenchmarkBatteryAttachCheck.Refused(reason);

                return new BenchmarkBatteryAttachCandidateDto
                {
                    RunId = run.Id,
                    RunStatus = run.Status.ToString(),
                    QualityIndex = run.QualityIndex,
                    StartedAtUtc = run.StartedAtUtc,
                    CompletedAtUtc = run.CompletedAtUtc,
                    TestedModelLabel = run.TestedModelSnapshot.Label(),
                    HarnessVersion = run.HarnessVersion,
                    ScoringMethodVersion = run.ScoringMethodVersion,
                    Eligible = check.Eligible,
                    Reason = check.Reason
                };
            })
            .ToList();

        return new BenchmarkBatteryAttachResult { Outcome = BenchmarkBatteryAttachOutcome.Ok, Candidates = candidates };
    }

    /// <summary>
    /// Which slots of a battery run not yet started existing runs would fill. Takes the start's own
    /// body; per slot in planner order it chooses the newest run that passes
    /// <see cref="BenchmarkBatteryComparability.CheckAttach"/> against the fingerprints a start would
    /// record now and the runs chosen before it, so no run fills two slots, or gives the reason the
    /// newest candidate was refused. Creates and spends nothing.
    /// </summary>
    public async Task<BenchmarkBatteryAttachResult> PreviewReuseAsync(
        StartBenchmarkBatteryRunRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Run == null)
        {
            return BenchmarkBatteryAttachResult.Fail(
                BenchmarkBatteryAttachOutcome.Ineligible, "The run settings are missing from the request.");
        }

        if (request.RunsPerSuite < 1)
        {
            return BenchmarkBatteryAttachResult.Fail(
                BenchmarkBatteryAttachOutcome.Ineligible, "Runs per suite must be at least 1.");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var battery = await db.BenchmarkBatteries
            .AsNoTracking()
            .Include(b => b.Suites)
            .FirstOrDefaultAsync(b => b.Id == request.BatteryId, ct);
        if (battery == null)
        {
            return BenchmarkBatteryAttachResult.Fail(BenchmarkBatteryAttachOutcome.NotFound, "Battery not found.");
        }

        if (battery.IsArchived)
        {
            return BenchmarkBatteryAttachResult.Fail(
                BenchmarkBatteryAttachOutcome.Ineligible, $"Battery '{battery.Name}' is archived.");
        }

        var definitionErrors = BenchmarkBatteryDefinition.Validate(battery);
        if (definitionErrors.Count > 0)
        {
            return BenchmarkBatteryAttachResult.Fail(
                BenchmarkBatteryAttachOutcome.Ineligible,
                $"Battery '{battery.Name}' cannot be run: {string.Join(" ", definitionErrors)}");
        }

        var definition = BenchmarkBatteryDefinition.FromEntity(battery);
        var stored = ResolveStoredRequest(request, definition);

        var fingerprints = new Dictionary<int, BenchmarkInstrumentFingerprint?>();
        var candidatesBySuite = new Dictionary<int, List<BenchmarkRun>>();
        foreach (var suite in definition.Suites)
        {
            fingerprints[suite.Index] = await ComputeFingerprintAsync(scope.ServiceProvider, db, suite.SuiteId, stored, ct);
            candidatesBySuite[suite.Index] = await CandidateRunsAsync(db, suite.SuiteId, stored.TestedModelConfigurationId, ct);
        }

        var chosen = new List<(int SuiteIndex, BenchmarkRun Run)>();
        var preview = new BenchmarkBatteryReusePreviewDto
        {
            BatteryId = battery.Id,
            SuiteCount = definition.Suites.Count,
            RunsPerSuite = request.RunsPerSuite
        };

        for (int round = 1; round <= request.RunsPerSuite; round++)
        {
            foreach (var suite in definition.Suites)
            {
                var target = AttachTarget(
                    suite, stored.TestedModelConfigurationId, fingerprints[suite.Index], chosen, chosen.Select(c => c.Run.Id));

                BenchmarkRun? pick = null;
                string? firstRefusal = null;
                foreach (var candidate in candidatesBySuite[suite.Index])
                {
                    var check = BenchmarkBatteryComparability.CheckAttach(candidate, target);
                    if (check.Eligible)
                    {
                        pick = candidate;
                        break;
                    }

                    firstRefusal ??= check.Reason;
                }

                var row = new BenchmarkBatteryReusePreviewSlotDto
                {
                    SuiteIndex = suite.Index,
                    SuiteId = suite.SuiteId,
                    SuiteName = suite.SuiteName,
                    Round = round
                };

                if (pick != null)
                {
                    chosen.Add((suite.Index, pick));
                    row.RunId = pick.Id;
                    row.RunStartedAtUtc = pick.StartedAtUtc;
                    row.QualityIndex = pick.QualityIndex;
                    preview.Attach.Add(new BenchmarkBatteryAttachDto { SuiteIndex = suite.Index, Round = round, RunId = pick.Id });
                }
                else
                {
                    row.Reason = firstRefusal
                        ?? $"No earlier run of '{suite.SuiteName}' tested this configuration.";
                }

                preview.Slots.Add(row);
            }
        }

        preview.ReusedCount = preview.Attach.Count;
        preview.LaunchCount = preview.Slots.Count - preview.ReusedCount;

        return new BenchmarkBatteryAttachResult { Outcome = BenchmarkBatteryAttachOutcome.Ok, Preview = preview };
    }

    /// <summary>
    /// Why a battery run in <paramref name="status"/> takes no attached run, or null: attaching is
    /// allowed while it is Pending, Stopped, Completed or CompletedWithErrors.
    /// </summary>
    public static string? AttachStatusRefusal(BenchmarkRunSeriesStatus status) => status switch
    {
        BenchmarkRunSeriesStatus.Pending
            or BenchmarkRunSeriesStatus.Stopped
            or BenchmarkRunSeriesStatus.Completed
            or BenchmarkRunSeriesStatus.CompletedWithErrors => null,
        BenchmarkRunSeriesStatus.Running
            or BenchmarkRunSeriesStatus.WaitingForCap =>
            "This battery run is running. Attach a run once it has stopped or finished.",
        _ => $"A {status} battery run takes no attached run. Start a new battery run instead."
    };

    /// <summary>This process drives the battery run, or its drive loop still holds the claim.</summary>
    private bool IsBeingDriven(long batteryRunId)
        => _active.ContainsKey(batteryRunId)
           || _runManager.OrchestratorOwner == BenchmarkRunManager.BatteryOwner(batteryRunId);

    /// <summary>The stored definition and start request of a battery run, or why they cannot be read.</summary>
    private static (BenchmarkBatteryDefinition? Definition, StartBenchmarkRunRequest? Stored, string? Error) ReadStoredState(
        BenchmarkBatteryRun batteryRun)
    {
        BenchmarkBatteryDefinition definition;
        try
        {
            definition = BenchmarkBatteryDefinition.FromJson(batteryRun.DefinitionJson);
        }
        catch (JsonException)
        {
            return (null, null, "This battery run's stored definition could not be read.");
        }

        var stored = DeserializeRequest(batteryRun);
        return stored == null
            ? (null, null, "This battery run's stored start request could not be read.")
            : (definition, stored, null);
    }

    /// <summary>
    /// What an attach to one slot of an existing battery run is judged against: the slot's live
    /// occupants (tracked, to be superseded), the slot refusal, and the target built from the
    /// recorded fingerprint and the other members.
    /// </summary>
    private sealed record SlotContext(
        IReadOnlyList<BenchmarkBatteryRunMember> Occupants,
        string? Refusal,
        BenchmarkBatteryAttachTarget? Target);

    private static async Task<SlotContext> LoadSlotContextAsync(
        ApplicationDbContext db,
        BenchmarkBatteryRun batteryRun,
        BenchmarkBatteryDefinition definition,
        int suiteIndex,
        int round,
        CancellationToken ct)
    {
        var state = await LoadSlotStateAsync(db, batteryRun.Id, ct);
        var inSlot = state
            .Where(s => !s.Member.Superseded && s.Member.SuiteIndex == suiteIndex && s.Member.Round == round)
            .OrderByDescending(s => s.Run != null && BenchmarkBatteryPlanner.IsUsable(s.Member, s.Run))
            .ThenByDescending(s => s.Member.AddedAtUtc)
            .ToList();

        var first = inSlot.FirstOrDefault();
        string? refusal = BenchmarkBatteryComparability.AttachSlotRefusal(
            definition.Suites.Count, batteryRun.RunsPerSuite, suiteIndex, round, first.Member, first.Run);
        if (refusal != null)
        {
            return new SlotContext(Array.Empty<BenchmarkBatteryRunMember>(), refusal, null);
        }

        var usable = await LoadUsableMembersAsync(db, batteryRun.Id, ct);
        var occupied = state
            .Where(s => !s.Member.Superseded && !(s.Member.SuiteIndex == suiteIndex && s.Member.Round == round))
            .Select(s => s.Member.BenchmarkRunId);

        var recorded = ReadFingerprints(batteryRun.SuiteFingerprintsJson);
        recorded.TryGetValue(suiteIndex, out var fingerprint);

        var target = AttachTarget(
            definition.Suites[suiteIndex],
            DeserializeRequest(batteryRun)?.TestedModelConfigurationId,
            fingerprint,
            usable.Select(u => (u.Member.SuiteIndex, u.Run)),
            occupied);

        return new SlotContext(inSlot.Select(s => s.Member).ToList(), null, target);
    }

    /// <summary>The runs of one suite and tested configuration, newest first, with their answer stubs.</summary>
    private static async Task<List<BenchmarkRun>> CandidateRunsAsync(
        ApplicationDbContext db,
        long suiteId,
        long testedModelConfigurationId,
        CancellationToken ct)
    {
        var runs = await db.BenchmarkRuns
            .AsNoTracking()
            .Where(r => (r.BenchmarkSuiteIdUsed ?? r.BenchmarkSuiteId) == suiteId
                        && r.TestedModelConfigurationId == testedModelConfigurationId)
            .OrderByDescending(r => r.StartedAtUtc)
            .ThenByDescending(r => r.Id)
            .Take(MaxAttachCandidates)
            .ToListAsync(ct);

        await BenchmarkSeriesOrchestrator.HydrateItemRevisionsAsync(db, runs, ct);
        return runs;
    }

    // ---------------------------------------------------------------------------------------
    // Resume
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Resumes a <c>Stopped</c> battery run, or a <c>CompletedWithErrors</c> one with a slot that holds
    /// no usable member.
    ///
    /// <para><see cref="BenchmarkBatteryResumeMode.Continue"/> keeps every usable member, supersedes
    /// the others and launches the free slots. It is refused with InstrumentChanged when a member
    /// carries a guard failure, when the usable members already refuse the composite, when this
    /// build's harness version differs from theirs, or when a remaining suite's fingerprint moved —
    /// in each case a member launched now could only trip the guard again.</para>
    ///
    /// <para><see cref="BenchmarkBatteryResumeMode.RerunUnderCurrentInstrument"/> supersedes every
    /// member, attached ones included, forgets the auto-created groups (they stay as ordinary
    /// groups), re-records every suite's fingerprint and starts over.</para>
    /// </summary>
    public async Task<BenchmarkBatteryStartResult> ResumeAsync(
        long batteryRunId,
        BenchmarkBatteryResumeMode mode,
        CancellationToken ct = default)
    {
        if (_runManager.CurrentRunId.HasValue)
        {
            return BenchmarkBatteryStartResult.Fail(
                BenchmarkBatteryStartOutcome.Conflict, "A benchmark run is already in progress.");
        }

        if (_active.ContainsKey(batteryRunId))
        {
            return BenchmarkBatteryStartResult.Fail(
                BenchmarkBatteryStartOutcome.Conflict, "This battery run is already running.");
        }

        if (!_active.IsEmpty)
        {
            return BenchmarkBatteryStartResult.Fail(
                BenchmarkBatteryStartOutcome.Conflict, "Another battery run is already in progress.");
        }

        string owner = BenchmarkRunManager.BatteryOwner(batteryRunId);
        if (_runManager.OrchestratorOwner is { } claimOwner && claimOwner != owner)
        {
            return BenchmarkBatteryStartResult.Fail(
                BenchmarkBatteryStartOutcome.Conflict, BenchmarkRunManager.ClaimConflictMessage(claimOwner));
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<BenchmarkComplianceGuard>();

        var batteryRun = await db.BenchmarkBatteryRuns.FirstOrDefaultAsync(b => b.Id == batteryRunId, ct);
        if (batteryRun == null)
        {
            return BenchmarkBatteryStartResult.Fail(BenchmarkBatteryStartOutcome.NotFound, "Battery run not found.");
        }

        BenchmarkBatteryDefinition definition;
        try
        {
            definition = BenchmarkBatteryDefinition.FromJson(batteryRun.DefinitionJson);
        }
        catch (JsonException)
        {
            return Refuse(BenchmarkBatteryStartOutcome.Invalid, batteryRun.Id,
                "This battery run cannot be resumed: its stored definition could not be read.");
        }

        var storedRequest = DeserializeRequest(batteryRun);
        if (storedRequest == null)
        {
            return Refuse(BenchmarkBatteryStartOutcome.Invalid, batteryRun.Id,
                "This battery run cannot be resumed: its stored start request could not be read.");
        }

        int suiteCount = definition.Suites.Count;
        var state = await LoadSlotStateAsync(db, batteryRun.Id, ct);
        int usableSlots = RecomputeCompletedMemberCount(state, suiteCount, batteryRun.RunsPerSuite);

        string? statusRefusal = ResumeStatusRefusal(batteryRun.Status, batteryRun.RequestedMemberCount, usableSlots);
        if (statusRefusal != null)
        {
            return Refuse(BenchmarkBatteryStartOutcome.Invalid, batteryRun.Id, statusRefusal);
        }

        bool rerun = mode == BenchmarkBatteryResumeMode.RerunUnderCurrentInstrument;
        var live = state.Where(s => !s.Member.Superseded).ToList();
        var kept = rerun
            ? new List<(BenchmarkBatteryRunMember Member, BenchmarkRun? Run)>()
            : live.Where(s => s.Run != null && BenchmarkBatteryPlanner.IsUsable(s.Member, s.Run)).ToList();

        // Not overridable: a member launched now is graded under this build's scoring method.
        var foreignMethods = ForeignScoringMethods(kept.Select(s => s.Run!), BenchmarkAssessmentPrompt.ScoringMethodVersion);
        if (foreignMethods.Count > 0)
        {
            return Refuse(BenchmarkBatteryStartOutcome.Invalid, batteryRun.Id,
                "This battery run cannot be resumed because the scoring method changed since its members were graded: " +
                $"they were graded under scoring method {string.Join(", ", foreignMethods)}, and this build grades " +
                $"under {BenchmarkAssessmentPrompt.ScoringMethodVersion}. Re-run it under the current instrument, " +
                "or start a new battery run.");
        }

        var (canSpend, denialReason) = await guard.CanSpendAsync(db, ct);
        if (!canSpend)
        {
            return Refuse(BenchmarkBatteryStartOutcome.SpendDenied, batteryRun.Id,
                denialReason ?? "The benchmark spend guard refused this resume.");
        }

        var keptSlots = kept.Select(s => (s.Member.SuiteIndex, s.Member.Round)).ToHashSet();
        var remainingSuites = definition.Suites
            .Where(suite => Enumerable.Range(1, batteryRun.RunsPerSuite).Any(round => !keptSlots.Contains((suite.Index, round))))
            .ToList();

        var launcher = scope.ServiceProvider.GetRequiredService<BenchmarkRunLauncher>();
        foreach (var suite in remainingSuites)
        {
            var suiteRequest = RequestForSuite(batteryRun, suite.SuiteId)!;
            var invalid = await launcher.ValidateRequestAsync(suiteRequest, ct);
            if (invalid != null)
            {
                return new BenchmarkBatteryStartResult
                {
                    Outcome = invalid.Outcome == BenchmarkRunLaunchOutcome.SpendDenied
                        ? BenchmarkBatteryStartOutcome.SpendDenied
                        : BenchmarkBatteryStartOutcome.Invalid,
                    BatteryRunId = batteryRun.Id,
                    SuiteIndex = suite.Index,
                    Error = $"This battery run cannot be resumed: suite '{suite.SuiteName}': {invalid.Error}"
                };
            }
        }

        if (!rerun)
        {
            string? guardRefusal = GuardFailureRefusal(live.Select(s => s.Member));
            if (guardRefusal != null)
            {
                return Refuse(BenchmarkBatteryStartOutcome.InstrumentChanged, batteryRun.Id, guardRefusal);
            }

            var usable = await LoadUsableMembersAsync(db, batteryRun.Id, ct);
            string? comparabilityRefusal = ComparabilityGuardFailure(usable);
            if (comparabilityRefusal != null)
            {
                return Refuse(BenchmarkBatteryStartOutcome.InstrumentChanged, batteryRun.Id,
                    "This battery run cannot be continued: its usable members already refuse the composite. " +
                    comparabilityRefusal + " Re-run it under the current instrument, or cancel it.");
            }

            string? harnessRefusal = HarnessVersionRefusal(usable.Select(u => u.Run), BenchmarkAssessmentPrompt.HarnessVersion);
            if (harnessRefusal != null)
            {
                return Refuse(BenchmarkBatteryStartOutcome.InstrumentChanged, batteryRun.Id, harnessRefusal);
            }

            var recorded = ReadFingerprints(batteryRun.SuiteFingerprintsJson);
            var changed = new List<string>();
            foreach (var suite in remainingSuites)
            {
                recorded.TryGetValue(suite.Index, out var recordedFingerprint);
                var current = await ComputeFingerprintAsync(scope.ServiceProvider, db, suite.SuiteId, storedRequest, ct);
                changed.AddRange(FingerprintDifferences(recordedFingerprint, current)
                    .Select(name => $"{name} (suite '{suite.SuiteName}')"));
            }

            if (changed.Count > 0)
            {
                return new BenchmarkBatteryStartResult
                {
                    Outcome = BenchmarkBatteryStartOutcome.InstrumentChanged,
                    BatteryRunId = batteryRun.Id,
                    ChangedInstrumentHashes = changed,
                    Error =
                        "This battery run cannot be continued because the instrument changed since it started: " +
                        string.Join(", ", changed) + ". Members launched now would not be comparable with the " +
                        "ones it holds. Re-run it under the current instrument, or cancel it."
                };
            }

            foreach (var (member, run) in live)
            {
                if (run == null || !BenchmarkBatteryPlanner.IsUsable(member, run))
                {
                    member.Superseded = true;
                }
            }
        }
        else
        {
            foreach (var (member, _) in live)
            {
                member.Superseded = true;
            }

            var fingerprints = new Dictionary<int, BenchmarkInstrumentFingerprint?>();
            foreach (var suite in definition.Suites)
            {
                fingerprints[suite.Index] = await ComputeFingerprintAsync(scope.ServiceProvider, db, suite.SuiteId, storedRequest, ct);
            }

            batteryRun.SuiteFingerprintsJson = WriteFingerprints(fingerprints);
            batteryRun.AutoCreatedGroupIdsJson = null;
        }

        batteryRun.CompletedMemberCount = RecomputeCompletedMemberCount(state, suiteCount, batteryRun.RunsPerSuite);
        batteryRun.Status = BenchmarkRunSeriesStatus.Pending;
        batteryRun.StopReason = null;
        batteryRun.ErrorMessage = null;
        batteryRun.CompletedAtUtc = null;
        batteryRun.LastProgressAtUtc = DateTime.UtcNow;

        // A claim this battery run already holds belongs to its live drive loop, which releases it.
        bool claimAlreadyHeld = _runManager.OrchestratorOwner == owner;
        if (!_runManager.TryClaimOrchestrator(owner))
        {
            return BenchmarkBatteryStartResult.Fail(
                BenchmarkBatteryStartOutcome.Conflict, BenchmarkRunManager.ClaimConflictMessage(_runManager.OrchestratorOwner));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            if (!claimAlreadyHeld) _runManager.ReleaseOrchestrator(owner);
            throw;
        }

        BeginDriving(batteryRun.Id);
        return BenchmarkBatteryStartResult.Ok(batteryRun.Id);
    }

    private static BenchmarkBatteryStartResult Refuse(BenchmarkBatteryStartOutcome outcome, long batteryRunId, string error)
        => new() { Outcome = outcome, BatteryRunId = batteryRunId, Error = error };

    /// <summary>
    /// Why a battery run in <paramref name="status"/> may not be resumed, or null. Resumable:
    /// <c>Stopped</c>, and <c>CompletedWithErrors</c> while a slot holds no usable member.
    /// </summary>
    public static string? ResumeStatusRefusal(BenchmarkRunSeriesStatus status, int requestedMemberCount, int usableSlotCount)
    {
        if (status == BenchmarkRunSeriesStatus.Stopped) return null;

        if (status == BenchmarkRunSeriesStatus.CompletedWithErrors)
        {
            return usableSlotCount < requestedMemberCount
                ? null
                : "Every slot of this battery run already holds a usable member; there is nothing to resume.";
        }

        return $"A {status} battery run cannot be resumed. Start a new battery run instead.";
    }

    /// <summary>The scoring method versions, ascending, of <paramref name="runs"/> that differ from <paramref name="currentMethod"/>.</summary>
    public static IReadOnlyList<int> ForeignScoringMethods(IEnumerable<BenchmarkRun> runs, int currentMethod)
        => runs.Select(r => r.ScoringMethodVersion).Where(m => m != currentMethod).Distinct().OrderBy(m => m).ToList();

    /// <summary>
    /// The refusal when a usable member was graded under another harness version than
    /// <paramref name="currentHarnessVersion"/>: any member launched now would differ from it on a
    /// battery-wide key. Null when they agree, or when there is no usable member.
    /// </summary>
    public static string? HarnessVersionRefusal(IEnumerable<BenchmarkRun> usableRuns, string currentHarnessVersion)
    {
        var foreign = usableRuns
            .Select(r => r.HarnessVersion)
            .Where(v => !string.Equals(v, currentHarnessVersion, StringComparison.Ordinal))
            .Select(v => v ?? "unrecorded")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        if (foreign.Count == 0) return null;

        return "This battery run cannot be continued because the harness changed since its members ran: they ran " +
               $"under harness {string.Join(", ", foreign)}, and this build is harness {currentHarnessVersion}. Members " +
               "launched now would not be comparable with them. Re-run it under the current instrument, or cancel it.";
    }

    /// <summary>The refusal when a live member carries a guard failure, naming it; null otherwise.</summary>
    public static string? GuardFailureRefusal(IEnumerable<BenchmarkBatteryRunMember> members)
    {
        var failed = members.Where(m => !m.Superseded && !string.IsNullOrWhiteSpace(m.GuardFailure)).ToList();
        if (failed.Count == 0) return null;

        var first = failed[0];
        return "This battery run cannot be continued: the instrument moved under it. " +
               $"Suite {first.SuiteIndex + 1}, round {first.Round} (run #{first.BenchmarkRunId}): {first.GuardFailure!.Trim()} " +
               "Re-run it under the current instrument, or cancel it.";
    }

    // ---------------------------------------------------------------------------------------
    // Cancel
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Cancels the in-flight member through the run manager, and the battery run. <c>Cancelled</c> is
    /// terminal and not resumable.
    /// </summary>
    public async Task<bool> CancelAsync(long batteryRunId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var batteryRun = await db.BenchmarkBatteryRuns.FirstOrDefaultAsync(b => b.Id == batteryRunId, ct);
        if (batteryRun == null) return false;

        if (_active.TryRemove(batteryRunId, out var cts))
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        var memberRunIds = await db.BenchmarkBatteryRunMembers
            .Where(m => m.BenchmarkBatteryRunId == batteryRunId && !m.Superseded)
            .Select(m => m.BenchmarkRunId)
            .ToListAsync(ct);

        var inFlight = await db.BenchmarkRuns
            .Where(r => memberRunIds.Contains(r.Id) && r.Status == BenchmarkRunStatus.Running)
            .Select(r => r.Id)
            .ToListAsync(ct);

        foreach (long runId in inFlight)
        {
            _runManager.TryCancel(runId);
        }

        batteryRun.Status = BenchmarkRunSeriesStatus.Cancelled;
        batteryRun.StopReason = null;
        batteryRun.CompletedAtUtc = DateTime.UtcNow;
        batteryRun.LastProgressAtUtc = DateTime.UtcNow;
        await RefreshCountsAsync(db, batteryRun, ct);
        await db.SaveChangesAsync(ct);

        return true;
    }

    // ---------------------------------------------------------------------------------------
    // Startup reconciliation
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Moves any battery run left <c>Running</c>, <c>WaitingForCap</c> or <c>Pending</c> by an unclean
    /// shutdown to <c>Stopped</c>, so Continue becomes available. The member that was in flight is
    /// left to the run cleanup; Continue supersedes it if that leaves it unusable. Called once at
    /// startup, after the series reconciliation.
    /// </summary>
    public async Task<int> ReconcileOrphanedAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var orphaned = await db.BenchmarkBatteryRuns
            .Where(b => b.Status == BenchmarkRunSeriesStatus.Running
                        || b.Status == BenchmarkRunSeriesStatus.WaitingForCap
                        || b.Status == BenchmarkRunSeriesStatus.Pending)
            .ToListAsync(ct);

        if (orphaned.Count == 0) return 0;

        foreach (var batteryRun in orphaned)
        {
            batteryRun.Status = BenchmarkRunSeriesStatus.Stopped;
            batteryRun.StopReason = BenchmarkRunSeriesStopReason.MemberFailed;
            batteryRun.ErrorMessage =
                "Service restarted while this battery run was running. Usable members are intact; continue the " +
                "battery run to launch the remaining ones.";
            batteryRun.LastProgressAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogWarning("Reconciled {Count} orphaned benchmark battery run(s) to Stopped.", orphaned.Count);
        return orphaned.Count;
    }

    // ---------------------------------------------------------------------------------------
    // The drive loop
    // ---------------------------------------------------------------------------------------

    private void BeginDriving(long batteryRunId)
    {
        var cts = new CancellationTokenSource();
        if (!_active.TryAdd(batteryRunId, cts))
        {
            cts.Dispose();
            return;
        }

        _ = Task.Run(async () =>
        {
            // A Sentry scope of its own, for the reasons BenchmarkSeriesOrchestrator gives.
            using var sentryScope = SentrySdk.PushScope();
            SentrySdk.ConfigureScope(scope =>
            {
                scope.Clear();
                scope.SetTag("BatteryRunId", batteryRunId.ToString(CultureInfo.InvariantCulture));
            });

            try
            {
                await DriveAsync(batteryRunId, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Benchmark battery run {BatteryRunId} was canceled.", batteryRunId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Benchmark battery run {BatteryRunId} failed.", batteryRunId);
                await MarkFailedAsync(batteryRunId, ex.Message);
            }
            finally
            {
                if (_active.TryRemove(batteryRunId, out var removed))
                {
                    removed.Dispose();
                }

                _runManager.ReleaseOrchestrator(BenchmarkRunManager.BatteryOwner(batteryRunId));
            }
        });
    }

    private async Task DriveAsync(long batteryRunId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var guard = scope.ServiceProvider.GetRequiredService<BenchmarkComplianceGuard>();
            var launcher = scope.ServiceProvider.GetRequiredService<BenchmarkRunLauncher>();

            var batteryRun = await db.BenchmarkBatteryRuns.FirstOrDefaultAsync(b => b.Id == batteryRunId, ct);
            if (batteryRun == null) return;

            if (batteryRun.Status == BenchmarkRunSeriesStatus.Cancelled) return;

            BenchmarkBatteryDefinition definition;
            try
            {
                definition = BenchmarkBatteryDefinition.FromJson(batteryRun.DefinitionJson);
            }
            catch (JsonException)
            {
                await StopAsync(db, batteryRun, BenchmarkRunSeriesStopReason.MemberFailed,
                    "The stored battery definition could not be read, so no further member can be launched.", ct);
                return;
            }

            // Planned on occupied slots, usable or not, so a member whose index was withheld is not
            // launched again in the same drive.
            var occupied = await db.BenchmarkBatteryRunMembers
                .Where(m => m.BenchmarkBatteryRunId == batteryRunId && !m.Superseded)
                .Select(m => new { m.SuiteIndex, m.Round })
                .ToListAsync(ct);

            var next = BenchmarkBatteryPlanner.NextMember(
                definition.Suites.Count, batteryRun.RunsPerSuite,
                occupied.Select(o => (o.SuiteIndex, o.Round)).ToList());

            if (next == null)
            {
                await FinishAsync(scope.ServiceProvider, db, batteryRun, definition, ct);
                return;
            }

            var (suiteIndex, round) = next.Value;
            var suite = definition.Suites[suiteIndex];

            var request = RequestForSuite(batteryRun, suite.SuiteId);
            if (request == null)
            {
                await StopAsync(db, batteryRun, BenchmarkRunSeriesStopReason.MemberFailed,
                    "The stored start request could not be read, so no further member can be launched.", ct);
                return;
            }

            // Re-checked per member: a battery run spans hours and the window moves the whole time.
            if (!await WaitForCapAsync(db, guard, batteryRun, ct))
            {
                return;
            }

            batteryRun.Status = BenchmarkRunSeriesStatus.Running;
            batteryRun.LastProgressAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var member = new BenchmarkBatteryRunMember
            {
                BenchmarkBatteryRunId = batteryRun.Id,
                SuiteIndex = suiteIndex,
                Round = round,
                Origin = BenchmarkBatteryMemberOrigin.Launched,
                AddedAtUtc = DateTime.UtcNow
            };

            var launch = await launcher.CreateAndLaunchRunAsync(
                request, batteryRun.StartedByUserId, null, null, ct, member);

            if (!launch.Started)
            {
                var reason = launch.Outcome == BenchmarkRunLaunchOutcome.SpendDenied
                    ? BenchmarkRunSeriesStopReason.SpendDenied
                    : BenchmarkRunSeriesStopReason.MemberFailed;

                await StopAsync(db, batteryRun, reason,
                    $"Suite '{suite.SuiteName}', round {round} could not be launched: " +
                    (launch.Error ?? "the launcher refused it."), ct);
                return;
            }

            long runId = launch.RunId!.Value;
            var status = await AwaitMemberTerminalAsync(runId, ct);

            using var afterScope = _scopeFactory.CreateScope();
            var afterDb = afterScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var afterRun = await afterDb.BenchmarkBatteryRuns.FirstOrDefaultAsync(b => b.Id == batteryRunId, ct);
            if (afterRun == null) return;

            if (afterRun.Status == BenchmarkRunSeriesStatus.Cancelled) return;

            afterRun.LastProgressAtUtc = DateTime.UtcNow;

            var run = await afterDb.BenchmarkRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
            var afterMember = await afterDb.BenchmarkBatteryRunMembers.FirstOrDefaultAsync(
                m => m.BenchmarkBatteryRunId == batteryRunId && m.BenchmarkRunId == runId, ct);

            string slot = $"Suite '{suite.SuiteName}', round {round} (run #{runId})";

            if (run == null || afterMember == null)
            {
                await StopAsync(afterDb, afterRun, BenchmarkRunSeriesStopReason.MemberFailed,
                    $"{slot} was deleted before it finished.", ct);
                return;
            }

            if (!IsSuccessfulTerminal(status))
            {
                // Stop rather than press on: the remaining members would be launched into whatever
                // condition produced the failure.
                afterMember.Superseded = true;
                afterRun.FailedMemberCount++;
                await StopAsync(afterDb, afterRun, BenchmarkRunSeriesStopReason.MemberFailed,
                    $"{slot} finished as {status}. Usable members were kept.", ct);
                return;
            }

            if (!BenchmarkBatteryPlanner.IsUsable(afterMember, run))
            {
                // Index withheld: the member keeps its slot without a result, and the battery goes on.
                await RefreshCountsAsync(afterDb, afterRun, ct);
                await afterDb.SaveChangesAsync(ct);
                continue;
            }

            var recorded = ReadFingerprints(afterRun.SuiteFingerprintsJson);
            recorded.TryGetValue(suiteIndex, out var recordedFingerprint);
            string? guardFailure = FingerprintGuardFailure(recordedFingerprint, run);

            if (guardFailure == null)
            {
                var usable = await LoadUsableMembersAsync(afterDb, batteryRunId, ct);
                guardFailure = ComparabilityGuardFailure(usable);
            }

            if (guardFailure != null)
            {
                afterMember.GuardFailure = Truncate(guardFailure, GuardFailureMaxLength);
                await StopAsync(afterDb, afterRun, BenchmarkRunSeriesStopReason.InstrumentChanged,
                    $"{slot}: {guardFailure} The member is kept but not counted. Re-run the battery run under the " +
                    "current instrument, or cancel it.", ct);
                return;
            }

            await RefreshCountsAsync(afterDb, afterRun, ct);
            await afterDb.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Blocks until the spend guard allows the next member, or decides the battery run cannot
    /// proceed. Returns false when the caller should stop driving.
    /// </summary>
    private async Task<bool> WaitForCapAsync(
        ApplicationDbContext db,
        BenchmarkComplianceGuard guard,
        BenchmarkBatteryRun batteryRun,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + BenchmarkSeriesOrchestrator.CapWaitBudget;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var (canSpend, denialReason) = await guard.CanSpendAsync(db, ct);
            if (canSpend) return true;

            if (!batteryRun.AllowCapWait)
            {
                await StopAsync(db, batteryRun, BenchmarkRunSeriesStopReason.RunCapReached,
                    denialReason ?? "The run cap blocked the next member.", ct);
                return false;
            }

            if (DateTime.UtcNow >= deadline)
            {
                await StopAsync(db, batteryRun, BenchmarkRunSeriesStopReason.RunCapReached,
                    "The battery run waited on the run cap for longer than its budget allows. " +
                    "Usable members are intact; continue the battery run when there is headroom.", ct);
                return false;
            }

            if (batteryRun.Status != BenchmarkRunSeriesStatus.WaitingForCap)
            {
                batteryRun.Status = BenchmarkRunSeriesStatus.WaitingForCap;
                batteryRun.LastProgressAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            await Task.Delay(BenchmarkSeriesOrchestrator.CapRetryInterval, ct);
        }
    }

    /// <summary>Waits for a member to leave <see cref="BenchmarkRunStatus.Running"/>, polling its row.</summary>
    private async Task<BenchmarkRunStatus> AwaitMemberTerminalAsync(long runId, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var status = await db.BenchmarkRuns
                .Where(r => r.Id == runId)
                .Select(r => (BenchmarkRunStatus?)r.Status)
                .FirstOrDefaultAsync(ct);

            if (status == null) return BenchmarkRunStatus.Failed;

            if (status != BenchmarkRunStatus.Running)
            {
                return status.Value;
            }

            await Task.Delay(BenchmarkSeriesOrchestrator.MemberPollInterval, ct);
        }
    }

    private static bool IsSuccessfulTerminal(BenchmarkRunStatus status) =>
        status == BenchmarkRunStatus.Completed
        || status == BenchmarkRunStatus.CompletedWithLimits
        || status == BenchmarkRunStatus.CompletedWithErrors;

    private async Task StopAsync(
        ApplicationDbContext db,
        BenchmarkBatteryRun batteryRun,
        BenchmarkRunSeriesStopReason reason,
        string message,
        CancellationToken ct)
    {
        batteryRun.Status = BenchmarkRunSeriesStatus.Stopped;
        batteryRun.StopReason = reason;
        batteryRun.ErrorMessage = Truncate(message, ErrorMessageMaxLength);
        batteryRun.LastProgressAtUtc = DateTime.UtcNow;
        await RefreshCountsAsync(db, batteryRun, ct);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Benchmark battery run {BatteryRunId} stopped ({Reason}): {Message}", batteryRun.Id, reason, message);
    }

    private async Task MarkFailedAsync(long batteryRunId, string message)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var batteryRun = await db.BenchmarkBatteryRuns.FirstOrDefaultAsync(b => b.Id == batteryRunId);
            if (batteryRun == null) return;

            batteryRun.Status = BenchmarkRunSeriesStatus.Failed;
            batteryRun.ErrorMessage = Truncate(message, ErrorMessageMaxLength);
            batteryRun.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not mark benchmark battery run {BatteryRunId} as failed.", batteryRunId);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Completion: status, per-suite replicate groups, analysis
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Every slot is occupied. <c>Completed</c> when every slot holds a usable member, otherwise
    /// <c>CompletedWithErrors</c>, naming the members without a result. With two or more runs per
    /// suite, one ordinary run group per suite with at least two usable members (once per suite);
    /// then the composite analysis, whose refusal is recorded and does not fail the battery run.
    /// </summary>
    private async Task FinishAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        BenchmarkBatteryRun batteryRun,
        BenchmarkBatteryDefinition definition,
        CancellationToken ct)
    {
        var state = await LoadSlotStateAsync(db, batteryRun.Id, ct);
        batteryRun.CompletedMemberCount = RecomputeCompletedMemberCount(state, definition.Suites.Count, batteryRun.RunsPerSuite);

        var unusable = state
            .Where(s => !s.Member.Superseded && (s.Run == null || !BenchmarkBatteryPlanner.IsUsable(s.Member, s.Run)))
            .OrderBy(s => s.Member.Round)
            .ThenBy(s => s.Member.SuiteIndex)
            .ToList();

        bool complete = batteryRun.CompletedMemberCount >= batteryRun.RequestedMemberCount;
        batteryRun.Status = complete
            ? BenchmarkRunSeriesStatus.Completed
            : BenchmarkRunSeriesStatus.CompletedWithErrors;
        batteryRun.StopReason = null;
        batteryRun.ErrorMessage = complete
            ? null
            : Truncate(
                "Without a usable result: " + string.Join("; ", unusable.Select(s =>
                    $"suite '{SuiteNameAt(definition, s.Member.SuiteIndex)}', round {s.Member.Round} (run #{s.Member.BenchmarkRunId}): " +
                    (s.Run == null ? "run deleted" : BenchmarkBatteryPlanner.UnusableReason(s.Member, s.Run)))) +
                ". Repair a run with Re-run Failed Questions and recompute, or continue the battery run to replace it.",
                ErrorMessageMaxLength);
        batteryRun.CompletedAtUtc = DateTime.UtcNow;
        batteryRun.LastProgressAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        if (batteryRun.RunsPerSuite >= 2)
        {
            var groupIds = ReadGroupIds(batteryRun.AutoCreatedGroupIdsJson).ToDictionary(p => p.Key, p => p.Value);

            foreach (var suite in definition.Suites)
            {
                if (groupIds.ContainsKey(suite.Index)) continue;

                var runIds = state
                    .Where(s => s.Member.SuiteIndex == suite.Index && s.Run != null && BenchmarkBatteryPlanner.IsUsable(s.Member, s.Run))
                    .OrderBy(s => s.Member.Round)
                    .Select(s => s.Member.BenchmarkRunId)
                    .Distinct()
                    .ToList();
                if (runIds.Count < 2) continue;

                // Untracked: CreateReplicateGroupAsync gives them answer stubs.
                var loaded = await db.BenchmarkRuns.AsNoTracking().Where(r => runIds.Contains(r.Id)).ToListAsync(ct);
                var members = runIds.Select(id => loaded.FirstOrDefault(r => r.Id == id)).OfType<BenchmarkRun>().ToList();
                if (members.Count < 2) continue;

                long? groupId = await BenchmarkSeriesOrchestrator.CreateReplicateGroupAsync(
                    db,
                    _logger,
                    members,
                    members[0].BenchmarkSuiteId,
                    suite.SuiteName,
                    batteryRun.StartedAtUtc,
                    batteryRun.StartedByUserId,
                    createdFromSeriesId: null,
                    instrumentChangeAcknowledged: false,
                    $"Auto-created from battery run #{batteryRun.Id}, {suite.SuiteName}.",
                    ct);

                if (groupId.HasValue)
                {
                    groupIds[suite.Index] = groupId.Value;
                    batteryRun.AutoCreatedGroupIdsJson = JsonSerializer.Serialize(groupIds, StateJsonOptions);
                    await db.SaveChangesAsync(ct);
                }
            }
        }

        await AnalyseAsync(services, batteryRun.Id, batteryRun.StartedByUserId, ct);
    }

    /// <summary>
    /// Computes and persists the composite analysis. A refusal or an exception is logged and appended
    /// to the battery run's error message, through a scope of its own so a half-written analysis is
    /// never saved with it.
    /// </summary>
    private async Task AnalyseAsync(IServiceProvider services, long batteryRunId, string? userId, CancellationToken ct)
    {
        string? problem;
        try
        {
            var analysisService = services.GetRequiredService<BenchmarkBatteryAnalysisService>();
            var (_, _, _, error) = await analysisService.AnalyseAsync(batteryRunId, userId, null, ct);
            problem = error;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The analysis of benchmark battery run {BatteryRunId} failed.", batteryRunId);
            problem = ex.Message;
        }

        if (problem == null) return;

        _logger.LogWarning("Benchmark battery run {BatteryRunId} has no analysis: {Problem}", batteryRunId, problem);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var batteryRun = await db.BenchmarkBatteryRuns.FirstOrDefaultAsync(b => b.Id == batteryRunId, ct);
            if (batteryRun == null) return;

            string note = "The analysis could not be computed: " + problem;
            batteryRun.ErrorMessage = Truncate(
                string.IsNullOrEmpty(batteryRun.ErrorMessage) ? note : batteryRun.ErrorMessage + " " + note,
                ErrorMessageMaxLength);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not record the analysis problem of benchmark battery run {BatteryRunId}.", batteryRunId);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Member state
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Every member row of the battery run, tracked, each with a stub of its run carrying what the
    /// usability rule and the resume checks read; the run is null when it was deleted.
    /// </summary>
    private static async Task<List<(BenchmarkBatteryRunMember Member, BenchmarkRun? Run)>> LoadSlotStateAsync(
        ApplicationDbContext db,
        long batteryRunId,
        CancellationToken ct)
    {
        var members = await db.BenchmarkBatteryRunMembers
            .Where(m => m.BenchmarkBatteryRunId == batteryRunId)
            .ToListAsync(ct);

        var runIds = members.Select(m => m.BenchmarkRunId).Distinct().ToList();
        var rows = await db.BenchmarkRuns
            .AsNoTracking()
            .Where(r => runIds.Contains(r.Id))
            .Select(r => new
            {
                r.Id,
                r.Status,
                r.QualityIndex,
                r.TerminalFailureAnswerCount,
                r.ScoringMethodVersion,
                r.HarnessVersion
            })
            .ToListAsync(ct);

        var runs = rows.ToDictionary(r => r.Id, r => new BenchmarkRun
        {
            Id = r.Id,
            Status = r.Status,
            QualityIndex = r.QualityIndex,
            TerminalFailureAnswerCount = r.TerminalFailureAnswerCount,
            ScoringMethodVersion = r.ScoringMethodVersion,
            HarnessVersion = r.HarnessVersion
        });

        return members
            .Select(m => (m, runs.TryGetValue(m.BenchmarkRunId, out var run) ? run : null))
            .ToList();
    }

    /// <summary>
    /// The usable members with their full runs, untracked, the runs carrying the answer stubs the
    /// comparability resolution reads.
    /// </summary>
    private static async Task<List<(BenchmarkBatteryRunMember Member, BenchmarkRun Run)>> LoadUsableMembersAsync(
        ApplicationDbContext db,
        long batteryRunId,
        CancellationToken ct)
    {
        var members = await db.BenchmarkBatteryRunMembers
            .AsNoTracking()
            .Where(m => m.BenchmarkBatteryRunId == batteryRunId && !m.Superseded)
            .ToListAsync(ct);

        var runIds = members.Select(m => m.BenchmarkRunId).Distinct().ToList();
        var runs = await db.BenchmarkRuns
            .AsNoTracking()
            .Where(r => runIds.Contains(r.Id))
            .ToListAsync(ct);
        var byId = runs.ToDictionary(r => r.Id);

        var usable = members
            .Where(m => byId.ContainsKey(m.BenchmarkRunId) && BenchmarkBatteryPlanner.IsUsable(m, byId[m.BenchmarkRunId]))
            .Select(m => (Member: m, Run: byId[m.BenchmarkRunId]))
            .ToList();

        await BenchmarkSeriesOrchestrator.HydrateItemRevisionsAsync(db, usable.Select(u => u.Run).Distinct().ToList(), ct);
        return usable;
    }

    private static async Task RefreshCountsAsync(ApplicationDbContext db, BenchmarkBatteryRun batteryRun, CancellationToken ct)
    {
        var state = await LoadSlotStateAsync(db, batteryRun.Id, ct);
        int suiteCount = batteryRun.RunsPerSuite > 0 ? batteryRun.RequestedMemberCount / batteryRun.RunsPerSuite : 0;
        batteryRun.CompletedMemberCount = RecomputeCompletedMemberCount(state, suiteCount, batteryRun.RunsPerSuite);
    }

    /// <summary>
    /// The number of (suite, round) slots of the grid that hold a usable member (M4). Recomputed from
    /// the member rows, never incremented. A member whose run was deleted is not usable.
    /// </summary>
    public static int RecomputeCompletedMemberCount(
        IEnumerable<(BenchmarkBatteryRunMember Member, BenchmarkRun? Run)> members,
        int suiteCount,
        int runsPerSuite)
        => (members ?? Array.Empty<(BenchmarkBatteryRunMember, BenchmarkRun?)>())
            .Where(m => m.Member.SuiteIndex >= 0 && m.Member.SuiteIndex < suiteCount
                        && m.Member.Round >= 1 && m.Member.Round <= runsPerSuite
                        && m.Run != null
                        && BenchmarkBatteryPlanner.IsUsable(m.Member, m.Run))
            .Select(m => (m.Member.SuiteIndex, m.Member.Round))
            .Distinct()
            .Count();

    // ---------------------------------------------------------------------------------------
    // The two guards
    // ---------------------------------------------------------------------------------------

    /// <summary>The five instrument hashes stamped on a run.</summary>
    public static BenchmarkInstrumentFingerprint FingerprintOf(BenchmarkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new BenchmarkInstrumentFingerprint(
            run.CandidateSystemPromptSha256,
            run.ToolGuidesSha256,
            run.KnowledgeBaseHeadSha,
            run.WikiHeadSha,
            run.SourceCodeHeadSha);
    }

    /// <summary>
    /// The names of the hashes that differ between <paramref name="recorded"/> and
    /// <paramref name="current"/>. A hash missing on either side is not recorded and never counts
    /// as a difference.
    /// </summary>
    public static IReadOnlyList<string> FingerprintDifferences(
        BenchmarkInstrumentFingerprint? recorded,
        BenchmarkInstrumentFingerprint? current)
    {
        if (recorded == null || current == null) return Array.Empty<string>();

        var changed = new List<string>();

        void Compare(string name, string? was, string? now)
        {
            if (string.IsNullOrEmpty(was) || string.IsNullOrEmpty(now)) return;
            if (!string.Equals(was, now, StringComparison.OrdinalIgnoreCase)) changed.Add(name);
        }

        Compare("CandidateSystemPromptSha256", recorded.CandidateSystemPromptSha256, current.CandidateSystemPromptSha256);
        Compare("ToolGuidesSha256", recorded.ToolGuidesSha256, current.ToolGuidesSha256);
        Compare("KnowledgeBaseHeadSha", recorded.KnowledgeBaseHeadSha, current.KnowledgeBaseHeadSha);
        Compare("WikiHeadSha", recorded.WikiHeadSha, current.WikiHeadSha);
        Compare("SourceCodeHeadSha", recorded.SourceCodeHeadSha, current.SourceCodeHeadSha);

        return changed;
    }

    /// <summary>
    /// The fingerprint guard: why <paramref name="run"/> does not match the fingerprint recorded for
    /// its suite at start, or null when it does.
    /// </summary>
    public static string? FingerprintGuardFailure(BenchmarkInstrumentFingerprint? recorded, BenchmarkRun run)
    {
        var changed = FingerprintDifferences(recorded, FingerprintOf(run));
        return changed.Count == 0
            ? null
            : $"The instrument moved since the battery run started: {string.Join(", ", changed)} differ from the hashes recorded for this suite.";
    }

    /// <summary>
    /// The comparability guard: the battery comparability verdict
    /// (<see cref="BenchmarkBatteryComparability.Resolve"/>) over the usable members among
    /// <paramref name="members"/>, by suite index. Returns the verdict's explanation when it refuses
    /// the composite, and null when it permits it or there is no usable member. The runs must carry
    /// their answers or the answer stubs.
    /// </summary>
    public static string? ComparabilityGuardFailure(IReadOnlyList<(BenchmarkBatteryRunMember Member, BenchmarkRun Run)> members)
    {
        var usable = (members ?? Array.Empty<(BenchmarkBatteryRunMember, BenchmarkRun)>())
            .Where(m => m.Run != null && BenchmarkBatteryPlanner.IsUsable(m.Member, m.Run))
            .ToList();
        if (usable.Count == 0) return null;

        var bySuite = usable
            .GroupBy(m => m.Member.SuiteIndex)
            .Select(g => (SuiteIndex: g.Key, Runs: (IReadOnlyList<BenchmarkRun>)g.Select(m => m.Run).ToList()))
            .ToList();

        var verdict = BenchmarkBatteryComparability.Resolve(bySuite);
        return verdict.CompositePermitted ? null : verdict.Explanation;
    }

    // ---------------------------------------------------------------------------------------
    // Stored state
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The stored start request of a battery run, as each member is launched from it: resolved
    /// (<c>AllowSourceCodeReferences</c> null reads as disallowed, the run count is 1). Null when it
    /// cannot be read. A fresh object on every call.
    /// </summary>
    internal static StartBenchmarkRunRequest? DeserializeRequest(BenchmarkBatteryRun batteryRun)
    {
        if (string.IsNullOrWhiteSpace(batteryRun.StartRequestJson)) return null;

        try
        {
            var request = JsonSerializer.Deserialize<StartBenchmarkRunRequest>(batteryRun.StartRequestJson);
            if (request != null)
            {
                request.AllowSourceCodeReferences ??= false;
                request.RunCount = 1;
            }
            return request;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A freshly deserialized stored request with <paramref name="suiteId"/> substituted.</summary>
    internal static StartBenchmarkRunRequest? RequestForSuite(BenchmarkBatteryRun batteryRun, long suiteId)
    {
        var request = DeserializeRequest(batteryRun);
        if (request != null) request.SuiteId = suiteId;
        return request;
    }

    /// <summary>
    /// The request a battery run stores: a copy of the operator's run settings with the run count
    /// forced to 1, the battery's cap preference, source code references resolved, and the first
    /// suite's id (ignored; each launch substitutes its own).
    /// </summary>
    internal static StartBenchmarkRunRequest ResolveStoredRequest(
        StartBenchmarkBatteryRunRequest request,
        BenchmarkBatteryDefinition definition)
    {
        var stored = CloneRequest(request.Run);
        stored.RunCount = 1;
        stored.AllowCapWait = request.AllowCapWait;
        stored.AllowSourceCodeReferences ??= false;
        stored.SuiteId = definition.Suites.Count > 0 ? definition.Suites[0].SuiteId : stored.SuiteId;
        return stored;
    }

    private static StartBenchmarkRunRequest CloneRequest(StartBenchmarkRunRequest request)
        => JsonSerializer.Deserialize<StartBenchmarkRunRequest>(JsonSerializer.Serialize(request))!;

    /// <summary>The per-suite fingerprints recorded at start, by suite index; empty when none were.</summary>
    public static IReadOnlyDictionary<int, BenchmarkInstrumentFingerprint> ReadFingerprints(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<int, BenchmarkInstrumentFingerprint>();

        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<int, BenchmarkInstrumentFingerprint?>>(json, StateJsonOptions);
            return (read ?? new Dictionary<int, BenchmarkInstrumentFingerprint?>())
                .Where(p => p.Value != null)
                .ToDictionary(p => p.Key, p => p.Value!);
        }
        catch (JsonException)
        {
            return new Dictionary<int, BenchmarkInstrumentFingerprint>();
        }
    }

    internal static string WriteFingerprints(IReadOnlyDictionary<int, BenchmarkInstrumentFingerprint?> fingerprints)
        => JsonSerializer.Serialize(fingerprints, StateJsonOptions);

    /// <summary>The auto-created run group ids, by suite index; empty when none were created.</summary>
    public static IReadOnlyDictionary<int, long> ReadGroupIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<int, long>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<int, long>>(json, StateJsonOptions) ?? new Dictionary<int, long>();
        }
        catch (JsonException)
        {
            return new Dictionary<int, long>();
        }
    }

    private static string SuiteNameAt(BenchmarkBatteryDefinition definition, int suiteIndex)
        => suiteIndex >= 0 && suiteIndex < definition.Suites.Count
            ? definition.Suites[suiteIndex].SuiteName
            : $"#{suiteIndex + 1}";

    private static string Truncate(string value, int maxLength)
        => value.Length > maxLength ? value.Substring(0, maxLength) : value;
}
