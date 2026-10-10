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

/// <summary>The outcome of a model batch start, resume, cancel or skip, in the vocabulary the controller maps to HTTP.</summary>
public enum BenchmarkModelBatchOutcome
{
    Ok = 0,

    /// <summary>Something else is running, or the batch is being driven.</summary>
    Conflict = 1,

    NotFound = 2,

    /// <summary>The request or the batch's state does not allow it; the error says why.</summary>
    Invalid = 3,

    /// <summary>A guardrail blocker refused the start; the findings say which.</summary>
    Blocked = 4,

    /// <summary>A warning was not acknowledged; the findings list the ones to acknowledge.</summary>
    NeedsAcknowledgment = 5,

    /// <summary>A resume was refused because the instrument or a grader moved; the keys say what.</summary>
    InstrumentChanged = 6
}

public sealed record BenchmarkModelBatchResult
{
    public BenchmarkModelBatchOutcome Outcome { get; init; }
    public long? BatchId { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<BenchmarkModelBatchFindingDto> Findings { get; init; } = Array.Empty<BenchmarkModelBatchFindingDto>();
    public IReadOnlyList<string> ChangedKeys { get; init; } = Array.Empty<string>();

    public bool Succeeded => Outcome == BenchmarkModelBatchOutcome.Ok;

    public static BenchmarkModelBatchResult Ok(long batchId) => new() { Outcome = BenchmarkModelBatchOutcome.Ok, BatchId = batchId };

    public static BenchmarkModelBatchResult Fail(BenchmarkModelBatchOutcome outcome, string error, long? batchId = null)
        => new() { Outcome = outcome, Error = error, BatchId = batchId };
}

/// <summary>How a member's launch went.</summary>
public enum BenchmarkModelBatchLaunchKind
{
    Started = 0,

    /// <summary>The run cap refused it; a cap wait outlasts this.</summary>
    CapDenied = 1,

    /// <summary>The spend guard refused it for another reason.</summary>
    SpendDenied = 2,

    /// <summary>The launcher or the child orchestrator refused it.</summary>
    Refused = 3
}

/// <summary>A member's launch: the run, series or battery run it created, or why there is none.</summary>
public sealed record BenchmarkModelBatchLaunch
{
    public BenchmarkModelBatchLaunchKind Kind { get; init; }
    public long? RunId { get; init; }
    public long? SeriesId { get; init; }
    public long? BatteryRunId { get; init; }
    public string? Error { get; init; }

    public bool Started => Kind == BenchmarkModelBatchLaunchKind.Started;

    public static BenchmarkModelBatchLaunch Refused(BenchmarkModelBatchLaunchKind kind, string? error) => new() { Kind = kind, Error = error };
}

/// <summary>A member's run, series or battery run as the batch reads it.</summary>
public sealed record BenchmarkModelBatchChildState
{
    /// <summary>The child has ended and holds no claim; for a battery run, its post-run work is done too.</summary>
    public bool Terminal { get; init; }

    /// <summary>What the member's status becomes; meaningful only when <see cref="Terminal"/>.</summary>
    public BenchmarkModelBatchMemberStatus Status { get; init; } = BenchmarkModelBatchMemberStatus.Running;

    public string? Error { get; init; }

    /// <summary>A stopped series or battery run that its own Continue would resume.</summary>
    public bool Resumable { get; init; }

    /// <summary>Why a stopped series or battery run stopped.</summary>
    public BenchmarkRunSeriesStopReason? StopReason { get; init; }

    /// <summary>Changes whenever the child advances; the batch's last progress follows it.</summary>
    public string? ProgressMarker { get; init; }
}

/// <summary>What moved under a batch, and the stop reason it calls for.</summary>
public sealed record BenchmarkModelBatchDrift(BenchmarkModelBatchStopReason Reason, IReadOnlyList<string> Keys, string Detail);

/// <summary>
/// Drives a <see cref="BenchmarkModelBatchRun"/>: one member at a time, in the stored order, each
/// launched as a single run (suite, one run per model), a replicate series (suite, two or more) or a
/// battery run (battery), and awaited to its end before the next member starts.
///
/// <para>Structured like <see cref="BenchmarkBatteryOrchestrator"/>: the row is the state, the
/// orchestrator is rebuilt from it after a restart, and the run manager's batch claim
/// (<c>modelbatch:{id}</c>) keeps every other launch off the gate while the batch lives, above the
/// child's own orchestrator claim. The claim is released in the drive task's <c>finally</c> and by
/// cancel, even when canceling the child throws.</para>
///
/// <para>Before each member after the first, the run-time guards run: the five instrument hashes,
/// the harness and the scoring method against the first member's (MB-R1), and the grader and exam
/// keys of the members' recorded runs against the first member's (MB-R2). A member that does not end
/// Completed or CompletedWithErrors stops the batch (MB-R3).</para>
/// </summary>
public class BenchmarkModelBatchOrchestrator
{
    private const int StopDetailMaxLength = 1024;
    private const int ErrorMessageMaxLength = 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly BenchmarkRunStatus[] CompletedRunStatuses =
    {
        BenchmarkRunStatus.Completed,
        BenchmarkRunStatus.CompletedWithLimits,
        BenchmarkRunStatus.CompletedWithErrors
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BenchmarkRunManager _runManager;
    private readonly BenchmarkSeriesOrchestrator _seriesOrchestrator;
    private readonly BenchmarkBatteryOrchestrator _batteryOrchestrator;
    private readonly ILogger<BenchmarkModelBatchOrchestrator> _logger;

    /// <summary>Cancellation for the batch being driven, keyed by batch id.</summary>
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _active = new();

    /// <summary>Serializes start and resume, so two of them cannot both pass the checks.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BenchmarkModelBatchOrchestrator(
        IServiceScopeFactory scopeFactory,
        BenchmarkRunManager runManager,
        BenchmarkSeriesOrchestrator seriesOrchestrator,
        BenchmarkBatteryOrchestrator batteryOrchestrator,
        ILogger<BenchmarkModelBatchOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _runManager = runManager;
        _seriesOrchestrator = seriesOrchestrator;
        _batteryOrchestrator = batteryOrchestrator;
        _logger = logger;
    }

    /// <summary>How often a member's run, series or battery run is polled.</summary>
    public TimeSpan MemberPollInterval { get; set; } = BenchmarkSeriesOrchestrator.MemberPollInterval;

    /// <summary>How long to wait between run-cap re-checks.</summary>
    public TimeSpan CapRetryInterval { get; set; } = BenchmarkSeriesOrchestrator.CapRetryInterval;

    /// <summary>The longest one cap wait may last before the batch stops with <c>RunCapReached</c>.</summary>
    public TimeSpan CapWaitBudget { get; set; } = BenchmarkSeriesOrchestrator.CapWaitBudget;

    /// <summary>The batch this process is driving, if any.</summary>
    public long? ActiveBatchId => _active.IsEmpty ? null : _active.Keys.First();

    public bool IsDriving(long batchId) => _active.ContainsKey(batchId);

    // ---------------------------------------------------------------------------------------
    // Start
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Evaluates the guardrails, writes the batch and its members in run order, takes the batch claim
    /// and begins driving. Blockers refuse with Blocked (MB-B09 with Conflict); a warning whose key is
    /// not acknowledged refuses with NeedsAcknowledgment. A <c>Randomized</c> order is a Fisher–Yates
    /// shuffle with a new seed, stored on the batch.
    /// </summary>
    public async Task<BenchmarkModelBatchResult> StartAsync(
        StartBenchmarkModelBatchRequest request,
        string? userId,
        CancellationToken ct = default)
    {
        if (request == null) return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.Invalid, "The request is missing.");
        request.Run ??= new StartBenchmarkRunRequest();
        request.TestedModelConfigurationIds ??= new List<long>();
        request.AcknowledgedFindingKeys ??= new List<string>();

        await _gate.WaitAsync(ct);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var findings = await EvaluateGuardrailsAsync(scope.ServiceProvider, request, ActiveBatchDescription(), ct);

            var blockers = BenchmarkModelBatchGuardrails.Blockers(findings);
            if (blockers.Count > 0)
            {
                bool busy = blockers.Any(f => f.Code == "MB-B09");
                return new BenchmarkModelBatchResult
                {
                    Outcome = busy ? BenchmarkModelBatchOutcome.Conflict : BenchmarkModelBatchOutcome.Blocked,
                    Error = busy ? "A benchmark is already running; wait for it or cancel it." : blockers[0].Detail,
                    Findings = blockers
                };
            }

            var unacknowledged = BenchmarkModelBatchGuardrails.UnacknowledgedWarnings(findings, request.AcknowledgedFindingKeys);
            if (unacknowledged.Count > 0)
            {
                return new BenchmarkModelBatchResult
                {
                    Outcome = BenchmarkModelBatchOutcome.NeedsAcknowledgment,
                    Error = "Acknowledge each warning before starting the batch.",
                    Findings = unacknowledged
                };
            }

            var ids = request.TestedModelConfigurationIds.Distinct().ToList();
            int? seed = request.Order == BenchmarkModelBatchOrder.Randomized ? NewSeed() : null;
            var ordered = seed.HasValue ? Shuffle(ids, seed.Value) : ids;

            var configs = await db.SystemAiApiConfigurations
                .AsNoTracking()
                .Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct);

            string? targetName = null;
            int? batteryRevision = null;
            string? definitionSha = null;
            if (request.TargetKind == BenchmarkModelBatchTargetKind.Battery)
            {
                var battery = await db.BenchmarkBatteries.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BatteryId, ct);
                if (battery == null) return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.NotFound, "Battery not found.");
                targetName = battery.Name;
                batteryRevision = battery.Revision;
                definitionSha = battery.DefinitionSha256;
            }
            else
            {
                targetName = await db.BenchmarkSuites.Where(s => s.Id == request.SuiteId).Select(s => s.Name).FirstOrDefaultAsync(ct);
                if (targetName == null) return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.NotFound, "Benchmark suite not found.");
            }

            var template = BenchmarkModelBatchGuardrailService.CloneRequest(request.Run);
            template.TestedModelConfigurationId = 0;
            template.SuiteId = request.TargetKind == BenchmarkModelBatchTargetKind.Suite ? request.SuiteId ?? 0 : 0;
            template.RunCount = 1;
            template.AllowCapWait = request.AllowCapWait;
            template.AcknowledgeSameProvider = false;
            template.AcknowledgeSameProviderReportWriter = false;
            template.AllowSourceCodeReferences ??= false;

            var acknowledged = findings.Where(f => f.Severity == BenchmarkModelBatchSeverity.Warning).ToList();
            var advice = findings.Where(f => f.Severity == BenchmarkModelBatchSeverity.Advice).ToList();

            var now = DateTime.UtcNow;
            var batch = new BenchmarkModelBatchRun
            {
                CreatedAtUtc = now,
                CreatedByUserId = string.IsNullOrEmpty(userId) ? null : userId,
                TargetKind = request.TargetKind,
                BenchmarkSuiteId = request.TargetKind == BenchmarkModelBatchTargetKind.Suite ? request.SuiteId : null,
                BenchmarkBatteryId = request.TargetKind == BenchmarkModelBatchTargetKind.Battery ? request.BatteryId : null,
                TargetName = Truncate(targetName, 128),
                BatteryRevision = batteryRevision,
                BatteryDefinitionSha256 = definitionSha,
                RunsPerModel = request.RunsPerModel,
                Order = request.Order,
                OrderSeed = seed,
                StartRequestJson = JsonSerializer.Serialize(template),
                AllowCapWait = request.AllowCapWait,
                Status = BenchmarkRunSeriesStatus.Pending,
                AcknowledgedFindingsJson = JsonSerializer.Serialize(acknowledged, JsonOptions),
                AdviceAtStartJson = JsonSerializer.Serialize(advice, JsonOptions),
                RequestedMemberCount = ordered.Count,
                LastProgressAtUtc = now,
                Members = ordered.Select((id, index) => new BenchmarkModelBatchMember
                {
                    OrderIndex = index,
                    TestedModelConfigurationId = id,
                    TestedModelSnapshotJson = JsonSerializer.Serialize(
                        configs.TryGetValue(id, out var config) ? ModelOf(config) : new BenchmarkModelBatchModelDto { ConfigurationId = id },
                        JsonOptions),
                    Status = BenchmarkModelBatchMemberStatus.Pending
                }).ToList()
            };

            db.BenchmarkModelBatchRuns.Add(batch);
            await db.SaveChangesAsync(ct);

            // The owner token needs the row id, so the claim follows the save; a claim lost to a race
            // removes the row again and refuses the start.
            string owner = BenchmarkRunManager.ModelBatchOwner(batch.Id);
            if (_runManager.CurrentRunId.HasValue || _runManager.OrchestratorOwner != null || !_runManager.TryClaimBatch(owner))
            {
                string message = BenchmarkRunManager.ClaimConflictMessage(_runManager.ClaimHolder);
                db.BenchmarkModelBatchRuns.Remove(batch);
                await db.SaveChangesAsync(CancellationToken.None);
                return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.Conflict, message);
            }

            BeginDriving(batch.Id);
            return BenchmarkModelBatchResult.Ok(batch.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The guardrail findings of a start. Overridable so a test supplies them.</summary>
    protected virtual async Task<IReadOnlyList<BenchmarkModelBatchFindingDto>> EvaluateGuardrailsAsync(
        IServiceProvider services,
        StartBenchmarkModelBatchRequest request,
        string? activeBatchDescription,
        CancellationToken ct)
    {
        var service = services.GetRequiredService<BenchmarkModelBatchGuardrailService>();
        var (findings, _) = await service.EvaluateAsync(request, activeBatchDescription, ct);
        return findings;
    }

    /// <summary>The batch this process drives, as MB-B09 names it; null when none.</summary>
    public string? ActiveBatchDescription()
        => ActiveBatchId is long id ? $"Model batch #{id.ToString(CultureInfo.InvariantCulture)} is running." : null;

    /// <summary>A new shuffle seed.</summary>
    protected virtual int NewSeed() => Random.Shared.Next();

    /// <summary>A Fisher–Yates shuffle of <paramref name="items"/> with <paramref name="seed"/>: the same seed gives the same order.</summary>
    public static IReadOnlyList<T> Shuffle<T>(IReadOnlyList<T> items, int seed)
    {
        var result = items.ToList();
        var random = new Random(seed);
        for (int i = result.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }
        return result;
    }

    /// <summary>A configuration as a member's snapshot records it.</summary>
    public static BenchmarkModelBatchModelDto ModelOf(SystemAiApiConfiguration config) => new()
    {
        ConfigurationId = config.Id,
        DisplayName = BenchmarkModelBatchGuardrails.Label(config),
        Provider = config.Provider ?? string.Empty,
        ModelId = config.ModelId ?? string.Empty,
        ThinkingLevel = config.ThinkingLevel,
        ReasoningMode = config.ReasoningMode,
        ServiceTier = config.ServiceTier,
        ParallelExecutionMode = config.ParallelExecutionMode.ToString(),
        Endpoint = SystemAiConfigurationSnapshotStore.DescribeEndpoint(SystemAiConfigurationSnapshotStore.FromConfiguration(config)),
        MaxOutputTokens = config.MaxOutputTokens
    };

    /// <summary>A member's stored snapshot; a placeholder carrying the configuration id when it cannot be read.</summary>
    public static BenchmarkModelBatchModelDto ReadModel(BenchmarkModelBatchMember member)
    {
        try
        {
            var model = JsonSerializer.Deserialize<BenchmarkModelBatchModelDto>(member.TestedModelSnapshotJson ?? "{}", JsonOptions);
            if (model != null)
            {
                model.ConfigurationId = member.TestedModelConfigurationId;
                return model;
            }
        }
        catch (JsonException)
        {
        }

        return new BenchmarkModelBatchModelDto { ConfigurationId = member.TestedModelConfigurationId };
    }

    /// <summary>A member's model name for messages.</summary>
    public static string MemberLabel(BenchmarkModelBatchMember member)
    {
        var model = ReadModel(member);
        return !string.IsNullOrWhiteSpace(model.DisplayName)
            ? model.DisplayName
            : !string.IsNullOrWhiteSpace(model.ModelId)
                ? model.ModelId
                : $"Configuration #{member.TestedModelConfigurationId.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>The findings stored on the batch as JSON; empty when they cannot be read.</summary>
    public static List<BenchmarkModelBatchFindingDto> ReadFindings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<BenchmarkModelBatchFindingDto>();
        try
        {
            return JsonSerializer.Deserialize<List<BenchmarkModelBatchFindingDto>>(json, JsonOptions) ?? new List<BenchmarkModelBatchFindingDto>();
        }
        catch (JsonException)
        {
            return new List<BenchmarkModelBatchFindingDto>();
        }
    }

    /// <summary>The stored run settings template, or null when it cannot be read.</summary>
    public static StartBenchmarkRunRequest? ReadTemplate(BenchmarkModelBatchRun batch)
    {
        try
        {
            return JsonSerializer.Deserialize<StartBenchmarkRunRequest>(batch.StartRequestJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The request a member is launched with: the template, its tested configuration, its suite, the
    /// run count, and the same-provider acknowledgments the batch's MB-W10 and MB-W08 carried.
    /// </summary>
    public static StartBenchmarkRunRequest? MemberRequest(BenchmarkModelBatchRun batch, BenchmarkModelBatchMember member)
    {
        var request = ReadTemplate(batch);
        if (request == null) return null;

        var acknowledged = ReadFindings(batch.AcknowledgedFindingsJson);
        bool Acknowledged(string code) => acknowledged.Any(f => f.Code == code && f.ModelConfigurationIds.Contains(member.TestedModelConfigurationId));

        request.TestedModelConfigurationId = member.TestedModelConfigurationId;
        request.SuiteId = batch.BenchmarkSuiteId ?? 0;
        request.RunCount = batch.TargetKind == BenchmarkModelBatchTargetKind.Suite ? Math.Max(1, batch.RunsPerModel) : 1;
        request.AllowCapWait = batch.AllowCapWait;
        request.AcknowledgeSameProvider = Acknowledged("MB-W10");
        request.AcknowledgeSameProviderReportWriter = Acknowledged("MB-W08");
        return request;
    }

    // ---------------------------------------------------------------------------------------
    // Child operations, overridable so a test runs a batch without a model
    // ---------------------------------------------------------------------------------------

    /// <summary>Launches a member: a single run, a series or a battery run, under the batch claim.</summary>
    protected virtual async Task<BenchmarkModelBatchLaunch> LaunchChildAsync(
        IServiceProvider services,
        BenchmarkModelBatchRun batch,
        BenchmarkModelBatchMember member,
        StartBenchmarkRunRequest request,
        string batchOwner,
        CancellationToken ct)
    {
        string? userId = batch.CreatedByUserId;

        if (batch.TargetKind == BenchmarkModelBatchTargetKind.Battery)
        {
            var result = await _batteryOrchestrator.StartAsync(new StartBenchmarkBatteryRunRequest
            {
                BatteryId = batch.BenchmarkBatteryId ?? 0,
                RunsPerSuite = Math.Max(1, batch.RunsPerModel),
                AllowCapWait = batch.AllowCapWait,
                Run = request
            }, userId, ct, batchOwner);

            return result.Started
                ? new BenchmarkModelBatchLaunch { Kind = BenchmarkModelBatchLaunchKind.Started, BatteryRunId = result.BatteryRunId }
                : BenchmarkModelBatchLaunch.Refused(
                    result.Outcome == BenchmarkBatteryStartOutcome.SpendDenied ? BenchmarkModelBatchLaunchKind.SpendDenied : BenchmarkModelBatchLaunchKind.Refused,
                    result.Error ?? result.SameProviderWarning?.Message ?? "The battery run was refused.");
        }

        if (batch.RunsPerModel >= 2)
        {
            var result = await _seriesOrchestrator.StartSeriesAsync(request, userId, ct, batchOwner);
            return result.Started
                ? new BenchmarkModelBatchLaunch { Kind = BenchmarkModelBatchLaunchKind.Started, SeriesId = result.SeriesId }
                : BenchmarkModelBatchLaunch.Refused(
                    result.Outcome == BenchmarkSeriesStartOutcome.SpendDenied ? BenchmarkModelBatchLaunchKind.SpendDenied : BenchmarkModelBatchLaunchKind.Refused,
                    result.Error ?? result.SameProviderWarning?.Message ?? "The series was refused.");
        }

        var launcher = services.GetRequiredService<BenchmarkRunLauncher>();
        var launch = await launcher.CreateAndLaunchRunAsync(request, userId, ct: ct, batchOwner: batchOwner);
        return launch.Started
            ? new BenchmarkModelBatchLaunch { Kind = BenchmarkModelBatchLaunchKind.Started, RunId = launch.RunId }
            : BenchmarkModelBatchLaunch.Refused(
                launch.Outcome == BenchmarkRunLaunchOutcome.SpendDenied ? BenchmarkModelBatchLaunchKind.SpendDenied : BenchmarkModelBatchLaunchKind.Refused,
                launch.Error ?? launch.SameProviderWarning?.Message ?? "The run was refused.");
    }

    /// <summary>Resumes a member's stopped series or battery run under the batch claim.</summary>
    protected virtual async Task<BenchmarkModelBatchLaunch> ResumeChildAsync(
        IServiceProvider services,
        BenchmarkModelBatchRun batch,
        BenchmarkModelBatchMember member,
        string batchOwner,
        CancellationToken ct)
    {
        if (member.BenchmarkBatteryRunId is long batteryRunId)
        {
            var result = await _batteryOrchestrator.ResumeAsync(batteryRunId, BenchmarkBatteryResumeMode.Continue, ct, batchOwner);
            return result.Started
                ? new BenchmarkModelBatchLaunch { Kind = BenchmarkModelBatchLaunchKind.Started, BatteryRunId = batteryRunId }
                : BenchmarkModelBatchLaunch.Refused(BenchmarkModelBatchLaunchKind.Refused, result.Error ?? "The battery run could not be resumed.");
        }

        if (member.BenchmarkRunSeriesId is long seriesId)
        {
            var result = await _seriesOrchestrator.ResumeSeriesAsync(seriesId, batch.InstrumentChangeAcknowledged, ct, batchOwner);
            return result.Started
                ? new BenchmarkModelBatchLaunch { Kind = BenchmarkModelBatchLaunchKind.Started, SeriesId = seriesId }
                : BenchmarkModelBatchLaunch.Refused(BenchmarkModelBatchLaunchKind.Refused, result.Error ?? "The series could not be resumed.");
        }

        return BenchmarkModelBatchLaunch.Refused(BenchmarkModelBatchLaunchKind.Refused, "The member has no series or battery run to resume.");
    }

    /// <summary>Where a member's run, series or battery run stands.</summary>
    protected virtual async Task<BenchmarkModelBatchChildState> GetChildStateAsync(
        IServiceProvider services,
        BenchmarkModelBatchMember member,
        CancellationToken ct)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();

        if (member.BenchmarkBatteryRunId is long batteryRunId)
        {
            var row = await db.BenchmarkBatteryRuns
                .AsNoTracking()
                .Where(b => b.Id == batteryRunId)
                .Select(b => new { b.Status, b.StopReason, b.ErrorMessage, b.ReportDocumentsStatus, b.CompletedMemberCount, b.LastProgressAtUtc, b.RequestedMemberCount })
                .FirstOrDefaultAsync(ct);
            if (row == null) return Ended(BenchmarkModelBatchMemberStatus.Failed, "The battery run was deleted.");

            bool live = row.Status is BenchmarkRunSeriesStatus.Pending or BenchmarkRunSeriesStatus.Running or BenchmarkRunSeriesStatus.WaitingForCap
                        || _batteryOrchestrator.IsDriving(batteryRunId)
                        || _runManager.OrchestratorOwner == BenchmarkRunManager.BatteryOwner(batteryRunId)
                        || _batteryOrchestrator.IsAnalysing(batteryRunId)
                        || row.ReportDocumentsStatus is BenchmarkRunReportDocumentsStatus.Pending or BenchmarkRunReportDocumentsStatus.Writing;
            var memberRunIds = db.BenchmarkBatteryRunMembers
                .Where(m => m.BenchmarkBatteryRunId == batteryRunId && !m.Superseded)
                .Select(m => m.BenchmarkRunId);
            int answering = await db.BenchmarkRuns
                .Where(r => memberRunIds.Contains(r.Id) && r.Status == BenchmarkRunStatus.Running)
                .Select(r => r.Answers.Count())
                .FirstOrDefaultAsync(ct);
            string marker = $"{row.Status}:{row.CompletedMemberCount}:{row.LastProgressAtUtc?.Ticks}:{answering}";
            if (live) return new BenchmarkModelBatchChildState { Terminal = false, ProgressMarker = marker };

            return new BenchmarkModelBatchChildState
            {
                Terminal = true,
                Status = MemberStatusOf(row.Status),
                Error = row.ErrorMessage,
                Resumable = row.Status is BenchmarkRunSeriesStatus.Stopped or BenchmarkRunSeriesStatus.CompletedWithErrors,
                StopReason = row.StopReason,
                ProgressMarker = marker
            };
        }

        if (member.BenchmarkRunSeriesId is long seriesId)
        {
            var row = await db.BenchmarkRunSeries
                .AsNoTracking()
                .Where(s => s.Id == seriesId)
                .Select(s => new { s.Status, s.StopReason, s.ErrorMessage, s.CompletedRunCount, s.RequestedRunCount, s.LastProgressAtUtc })
                .FirstOrDefaultAsync(ct);
            if (row == null) return Ended(BenchmarkModelBatchMemberStatus.Failed, "The series was deleted.");

            bool live = row.Status is BenchmarkRunSeriesStatus.Pending or BenchmarkRunSeriesStatus.Running or BenchmarkRunSeriesStatus.WaitingForCap
                        || _seriesOrchestrator.IsDriving(seriesId)
                        || _runManager.OrchestratorOwner == BenchmarkRunManager.SeriesOwner(seriesId);
            int answering = await db.BenchmarkRuns
                .Where(r => r.RunSeriesId == seriesId && r.Status == BenchmarkRunStatus.Running)
                .Select(r => r.Answers.Count())
                .FirstOrDefaultAsync(ct);
            string marker = $"{row.Status}:{row.CompletedRunCount}:{row.LastProgressAtUtc?.Ticks}:{answering}";
            if (live) return new BenchmarkModelBatchChildState { Terminal = false, ProgressMarker = marker };

            return new BenchmarkModelBatchChildState
            {
                Terminal = true,
                Status = MemberStatusOf(row.Status),
                Error = row.ErrorMessage,
                Resumable = row.Status is BenchmarkRunSeriesStatus.Stopped or BenchmarkRunSeriesStatus.CompletedWithErrors
                            && row.CompletedRunCount < row.RequestedRunCount,
                StopReason = row.StopReason,
                ProgressMarker = marker
            };
        }

        if (member.BenchmarkRunId is long runId)
        {
            var row = await db.BenchmarkRuns
                .AsNoTracking()
                .Where(r => r.Id == runId)
                .Select(r => new { r.Status, r.ErrorMessage, Answered = r.Answers.Count() })
                .FirstOrDefaultAsync(ct);
            if (row == null) return Ended(BenchmarkModelBatchMemberStatus.Failed, "The run was deleted.");

            string marker = $"{row.Status}:{row.Answered}";
            if (row.Status == BenchmarkRunStatus.Running || _runManager.CurrentRunId == runId)
            {
                return new BenchmarkModelBatchChildState { Terminal = false, ProgressMarker = marker };
            }

            return new BenchmarkModelBatchChildState
            {
                Terminal = true,
                Status = MemberStatusOf(row.Status),
                Error = row.ErrorMessage,
                ProgressMarker = marker
            };
        }

        return Ended(BenchmarkModelBatchMemberStatus.Failed, "The member has no run, series or battery run.");
    }

    /// <summary>Cancels a member's run, series or battery run.</summary>
    protected virtual async Task CancelChildAsync(IServiceProvider services, BenchmarkModelBatchMember member, CancellationToken ct)
    {
        if (member.BenchmarkBatteryRunId is long batteryRunId)
        {
            await _batteryOrchestrator.CancelAsync(batteryRunId, ct);
        }
        else if (member.BenchmarkRunSeriesId is long seriesId)
        {
            await _seriesOrchestrator.CancelSeriesAsync(seriesId, ct);
        }
        else if (member.BenchmarkRunId is long runId)
        {
            _runManager.TryCancel(runId);
        }
    }

    /// <summary>
    /// The five instrument hashes a run of <paramref name="template"/> on <paramref name="suiteId"/>
    /// for <paramref name="testedModelConfigurationId"/> would carry now; null when the suite or the
    /// configuration no longer exists.
    /// </summary>
    protected virtual Task<BenchmarkInstrumentFingerprint?> ComputeCurrentFingerprintAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        long suiteId,
        long testedModelConfigurationId,
        StartBenchmarkRunRequest template,
        CancellationToken ct)
    {
        var benchmarkService = services.GetRequiredService<BenchmarkService>();
        var (verboseMode, allowSourceCodeReferences) = BenchmarkRunLauncher.ResolvePromptSwitches(template);
        return benchmarkService.ComputeCurrentInstrumentFingerprintAsync(
            db, suiteId, testedModelConfigurationId, verboseMode, allowSourceCodeReferences, ct);
    }

    private static BenchmarkModelBatchChildState Ended(BenchmarkModelBatchMemberStatus status, string error)
        => new() { Terminal = true, Status = status, Error = error };

    /// <summary>A run's terminal status as a member status.</summary>
    public static BenchmarkModelBatchMemberStatus MemberStatusOf(BenchmarkRunStatus status) => status switch
    {
        BenchmarkRunStatus.Completed or BenchmarkRunStatus.CompletedWithLimits => BenchmarkModelBatchMemberStatus.Completed,
        BenchmarkRunStatus.CompletedWithErrors => BenchmarkModelBatchMemberStatus.CompletedWithErrors,
        BenchmarkRunStatus.Canceled => BenchmarkModelBatchMemberStatus.Canceled,
        BenchmarkRunStatus.Running => BenchmarkModelBatchMemberStatus.Running,
        _ => BenchmarkModelBatchMemberStatus.Failed
    };

    /// <summary>A series' or battery run's terminal status as a member status.</summary>
    public static BenchmarkModelBatchMemberStatus MemberStatusOf(BenchmarkRunSeriesStatus status) => status switch
    {
        BenchmarkRunSeriesStatus.Completed => BenchmarkModelBatchMemberStatus.Completed,
        BenchmarkRunSeriesStatus.CompletedWithErrors => BenchmarkModelBatchMemberStatus.CompletedWithErrors,
        BenchmarkRunSeriesStatus.Stopped => BenchmarkModelBatchMemberStatus.Stopped,
        BenchmarkRunSeriesStatus.Cancelled => BenchmarkModelBatchMemberStatus.Canceled,
        BenchmarkRunSeriesStatus.Failed => BenchmarkModelBatchMemberStatus.Failed,
        _ => BenchmarkModelBatchMemberStatus.Running
    };

    private static bool IsSuccessful(BenchmarkModelBatchMemberStatus status)
        => status is BenchmarkModelBatchMemberStatus.Completed or BenchmarkModelBatchMemberStatus.CompletedWithErrors;

    /// <summary>Completed, CompletedWithErrors, Cancelled and Failed: nothing follows them.</summary>
    public static bool IsTerminal(BenchmarkRunSeriesStatus status)
        => status is BenchmarkRunSeriesStatus.Completed or BenchmarkRunSeriesStatus.CompletedWithErrors
            or BenchmarkRunSeriesStatus.Cancelled or BenchmarkRunSeriesStatus.Failed;

    // ---------------------------------------------------------------------------------------
    // The drive loop
    // ---------------------------------------------------------------------------------------

    private void BeginDriving(long batchId)
    {
        var cts = new CancellationTokenSource();
        if (!_active.TryAdd(batchId, cts))
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
                scope.SetTag("ModelBatchId", batchId.ToString(CultureInfo.InvariantCulture));
            });

            try
            {
                await DriveAsync(batchId, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Model batch {BatchId} was canceled.", batchId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Model batch {BatchId} failed.", batchId);
                await MarkFailedAsync(batchId, ex.Message);
            }
            finally
            {
                if (_active.TryGetValue(batchId, out var current) && ReferenceEquals(current, cts))
                {
                    _active.TryRemove(batchId, out _);
                }

                cts.Dispose();
                _runManager.ReleaseBatch(BenchmarkRunManager.ModelBatchOwner(batchId));
            }
        });
    }

    private async Task DriveAsync(long batchId, CancellationToken ct)
    {
        string owner = BenchmarkRunManager.ModelBatchOwner(batchId);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            long memberId;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var batch = await db.BenchmarkModelBatchRuns.Include(b => b.Members).FirstOrDefaultAsync(b => b.Id == batchId, ct);
                if (batch == null || IsTerminal(batch.Status) || batch.Status == BenchmarkRunSeriesStatus.Stopped) return;

                var members = batch.Members.OrderBy(m => m.OrderIndex).ToList();
                var member = members.FirstOrDefault(m => m.Status == BenchmarkModelBatchMemberStatus.Running)
                             ?? members.FirstOrDefault(m => m.Status == BenchmarkModelBatchMemberStatus.Pending);

                if (member == null)
                {
                    await FinishAsync(db, batch, ct);
                    return;
                }

                if (member.Status == BenchmarkModelBatchMemberStatus.Pending)
                {
                    if (!batch.InstrumentChangeAcknowledged)
                    {
                        var drift = await CheckDriftAsync(scope.ServiceProvider, db, batch, ct);
                        if (drift != null)
                        {
                            await StopAsync(db, batch, drift.Reason, drift.Detail, ct);
                            return;
                        }
                    }

                    if (!await WaitForCapAsync(scope.ServiceProvider, db, batch, ct)) return;

                    var request = MemberRequest(batch, member);
                    if (request == null)
                    {
                        await StopAsync(db, batch, BenchmarkModelBatchStopReason.MemberStopped,
                            "The stored run settings could not be read, so no further member can be launched.", ct);
                        return;
                    }

                    var now = DateTime.UtcNow;
                    batch.Status = BenchmarkRunSeriesStatus.Running;
                    batch.StartedAtUtc ??= now;
                    batch.CurrentMemberIndex = member.OrderIndex;
                    batch.LastProgressAtUtc = now;
                    member.Status = BenchmarkModelBatchMemberStatus.Running;
                    member.StartedAtUtc = now;
                    member.CompletedAtUtc = null;
                    member.ErrorMessage = null;
                    await db.SaveChangesAsync(ct);

                    var launch = await LaunchChildAsync(scope.ServiceProvider, batch, member, request, owner, ct);
                    if (!launch.Started)
                    {
                        string label = MemberLabel(member);
                        string where = $"{label} ({Position(member, batch)})";
                        if (launch.Kind is BenchmarkModelBatchLaunchKind.CapDenied or BenchmarkModelBatchLaunchKind.SpendDenied)
                        {
                            member.Status = BenchmarkModelBatchMemberStatus.Pending;
                            member.StartedAtUtc = null;
                            await StopAsync(db, batch,
                                launch.Kind == BenchmarkModelBatchLaunchKind.CapDenied
                                    ? BenchmarkModelBatchStopReason.RunCapReached
                                    : BenchmarkModelBatchStopReason.SpendDenied,
                                $"{where} could not be launched: {launch.Error ?? "the spend guard refused it."}", ct);
                        }
                        else
                        {
                            member.Status = BenchmarkModelBatchMemberStatus.Failed;
                            member.CompletedAtUtc = DateTime.UtcNow;
                            member.ErrorMessage = Truncate(launch.Error, ErrorMessageMaxLength);
                            await StopAsync(db, batch, BenchmarkModelBatchStopReason.MemberStopped,
                                $"{where} could not be launched: {launch.Error ?? "it was refused."}", ct);
                        }

                        return;
                    }

                    member.BenchmarkRunId = launch.RunId;
                    member.BenchmarkRunSeriesId = launch.SeriesId;
                    member.BenchmarkBatteryRunId = launch.BatteryRunId;
                    batch.LastProgressAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }

                memberId = member.Id;
            }

            var state = await AwaitChildTerminalAsync(batchId, memberId, ct);

            using (var afterScope = _scopeFactory.CreateScope())
            {
                var db = afterScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var batch = await db.BenchmarkModelBatchRuns.Include(b => b.Members).FirstOrDefaultAsync(b => b.Id == batchId, ct);
                if (batch == null || batch.Status == BenchmarkRunSeriesStatus.Cancelled) return;

                var member = batch.Members.FirstOrDefault(m => m.Id == memberId);
                if (member == null) return;

                var now = DateTime.UtcNow;
                member.Status = state.Status;
                member.CompletedAtUtc = now;
                member.ErrorMessage = state.Status == BenchmarkModelBatchMemberStatus.Completed
                    ? null
                    : Truncate(state.Error, ErrorMessageMaxLength);
                batch.LastProgressAtUtc = now;

                await RecordFirstMemberAsync(db, batch, ct);

                if (IsSuccessful(state.Status))
                {
                    RefreshCounts(batch);
                    await db.SaveChangesAsync(ct);
                    continue;
                }

                // Stop rather than press on: the remaining members would be launched into whatever
                // condition produced the failure.
                var reason = state.StopReason switch
                {
                    BenchmarkRunSeriesStopReason.RunCapReached => BenchmarkModelBatchStopReason.RunCapReached,
                    BenchmarkRunSeriesStopReason.SpendDenied => BenchmarkModelBatchStopReason.SpendDenied,
                    _ => BenchmarkModelBatchStopReason.MemberStopped
                };

                await StopAsync(db, batch, reason,
                    $"{MemberLabel(member)} ({Position(member, batch)}) ended {state.Status}" +
                    (string.IsNullOrWhiteSpace(state.Error) ? "." : $": {state.Error}"), ct);
                return;
            }
        }
    }

    /// <summary>Polls a member's child until it ends, moving the batch's last progress whenever the child advances.</summary>
    private async Task<BenchmarkModelBatchChildState> AwaitChildTerminalAsync(long batchId, long memberId, CancellationToken ct)
    {
        string? lastMarker = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var member = await db.BenchmarkModelBatchMembers.AsNoTracking().FirstOrDefaultAsync(m => m.Id == memberId, ct);
                if (member == null) return Ended(BenchmarkModelBatchMemberStatus.Failed, "The member was deleted.");

                var state = await GetChildStateAsync(scope.ServiceProvider, member, ct);
                if (state.Terminal) return state;

                if (state.ProgressMarker != null && state.ProgressMarker != lastMarker)
                {
                    lastMarker = state.ProgressMarker;
                    var batch = await db.BenchmarkModelBatchRuns.FirstOrDefaultAsync(b => b.Id == batchId, ct);
                    if (batch != null && batch.Status != BenchmarkRunSeriesStatus.Cancelled)
                    {
                        batch.LastProgressAtUtc = DateTime.UtcNow;
                        await db.SaveChangesAsync(ct);
                    }
                }
            }

            await Task.Delay(MemberPollInterval, ct);
        }
    }

    /// <summary>
    /// Blocks until the spend guard allows the next member, or stops the batch. Only a run-cap denial
    /// is waited on, and only when the batch allows it. Returns false when driving should stop.
    /// </summary>
    private async Task<bool> WaitForCapAsync(IServiceProvider services, ApplicationDbContext db, BenchmarkModelBatchRun batch, CancellationToken ct)
    {
        var guard = services.GetRequiredService<BenchmarkComplianceGuard>();
        var deadline = DateTime.UtcNow + CapWaitBudget;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var spend = await guard.CheckSpendAsync(db, ct);
            if (spend.Allowed) return true;

            if (!spend.IsCapDenial)
            {
                await StopAsync(db, batch, BenchmarkModelBatchStopReason.SpendDenied,
                    spend.DenialReason ?? "The benchmark spend guard refused the next member.", ct);
                return false;
            }

            if (!batch.AllowCapWait)
            {
                await StopAsync(db, batch, BenchmarkModelBatchStopReason.RunCapReached,
                    spend.DenialReason ?? "The run cap blocked the next member.", ct);
                return false;
            }

            if (DateTime.UtcNow >= deadline)
            {
                await StopAsync(db, batch, BenchmarkModelBatchStopReason.RunCapReached,
                    "The batch waited on the run cap for longer than its budget allows. Finished members are intact; " +
                    "continue the batch when there is headroom.", ct);
                return false;
            }

            if (batch.Status != BenchmarkRunSeriesStatus.WaitingForCap)
            {
                batch.Status = BenchmarkRunSeriesStatus.WaitingForCap;
                batch.LastProgressAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            await Task.Delay(CapRetryInterval, ct);

            var reloaded = await db.BenchmarkModelBatchRuns.AsNoTracking()
                .Where(b => b.Id == batch.Id).Select(b => (BenchmarkRunSeriesStatus?)b.Status).FirstOrDefaultAsync(ct);
            if (reloaded is null or BenchmarkRunSeriesStatus.Cancelled) throw new OperationCanceledException();
        }
    }

    private async Task FinishAsync(ApplicationDbContext db, BenchmarkModelBatchRun batch, CancellationToken ct)
    {
        RefreshCounts(batch);
        bool clean = batch.Members.All(m => m.Status == BenchmarkModelBatchMemberStatus.Completed);
        batch.Status = clean ? BenchmarkRunSeriesStatus.Completed : BenchmarkRunSeriesStatus.CompletedWithErrors;
        batch.StopReason = null;
        batch.StopDetail = clean ? null : Truncate(UnfinishedMembersText(batch), StopDetailMaxLength);
        batch.CompletedAtUtc = DateTime.UtcNow;
        batch.LastProgressAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Model batch {BatchId} finished as {Status}.", batch.Id, batch.Status);
    }

    private static string UnfinishedMembersText(BenchmarkModelBatchRun batch)
        => "Without a clean result: " + string.Join("; ", batch.Members
            .Where(m => m.Status != BenchmarkModelBatchMemberStatus.Completed)
            .OrderBy(m => m.OrderIndex)
            .Select(m => $"{MemberLabel(m)} ({m.Status})")) + ".";

    private async Task StopAsync(
        ApplicationDbContext db,
        BenchmarkModelBatchRun batch,
        BenchmarkModelBatchStopReason reason,
        string detail,
        CancellationToken ct)
    {
        batch.Status = BenchmarkRunSeriesStatus.Stopped;
        batch.StopReason = reason;
        batch.StopDetail = Truncate(detail, StopDetailMaxLength);
        batch.LastProgressAtUtc = DateTime.UtcNow;
        RefreshCounts(batch);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Model batch {BatchId} stopped ({Reason}): {Detail}", batch.Id, reason, detail);
    }

    private async Task MarkFailedAsync(long batchId, string message)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var batch = await db.BenchmarkModelBatchRuns.Include(b => b.Members).FirstOrDefaultAsync(b => b.Id == batchId);
            if (batch == null || IsTerminal(batch.Status)) return;

            batch.Status = BenchmarkRunSeriesStatus.Failed;
            batch.StopReason = null;
            batch.StopDetail = Truncate(message, StopDetailMaxLength);
            batch.CompletedAtUtc = DateTime.UtcNow;
            batch.LastProgressAtUtc = DateTime.UtcNow;
            RefreshCounts(batch);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not mark model batch {BatchId} as failed.", batchId);
        }
    }

    /// <summary>The counts are recomputed from the member rows, never incremented.</summary>
    public static void RefreshCounts(BenchmarkModelBatchRun batch)
    {
        batch.RequestedMemberCount = batch.Members.Count;
        batch.CompletedMemberCount = batch.Members.Count(m => IsSuccessful(m.Status));
        batch.FailedMemberCount = batch.Members.Count(m => m.Status is BenchmarkModelBatchMemberStatus.Failed or BenchmarkModelBatchMemberStatus.Stopped);
        batch.SkippedMemberCount = batch.Members.Count(m => m.Status == BenchmarkModelBatchMemberStatus.Skipped);
    }

    private static string Position(BenchmarkModelBatchMember member, BenchmarkModelBatchRun batch)
        => $"{(member.OrderIndex + 1).ToString(CultureInfo.InvariantCulture)} of {batch.Members.Count.ToString(CultureInfo.InvariantCulture)}";

    // ---------------------------------------------------------------------------------------
    // The run-time guards: MB-R1, MB-R2, and the exam they rest on (MB-S2)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Records the first member's instrument once a member's first run is stamped: the five hashes,
    /// the harness and the scoring method of the earliest run any member produced.
    /// </summary>
    private static async Task RecordFirstMemberAsync(ApplicationDbContext db, BenchmarkModelBatchRun batch, CancellationToken ct)
    {
        if (HasBaseline(batch)) return;

        foreach (var member in batch.Members.OrderBy(m => m.OrderIndex))
        {
            var runs = await MemberRunsAsync(db, member, completedOnly: false, ct);
            var first = runs
                .Where(r => r.HarnessVersion != null || r.CandidateSystemPromptSha256 != null)
                .OrderBy(r => r.StartedAtUtc)
                .ThenBy(r => r.Id)
                .FirstOrDefault();
            if (first == null) continue;

            batch.FirstMemberCandidateSystemPromptSha256 = first.CandidateSystemPromptSha256;
            batch.FirstMemberToolGuidesSha256 = first.ToolGuidesSha256;
            batch.FirstMemberKnowledgeBaseHeadSha = first.KnowledgeBaseHeadSha;
            batch.FirstMemberWikiHeadSha = first.WikiHeadSha;
            batch.FirstMemberSourceCodeHeadSha = first.SourceCodeHeadSha;
            batch.FirstMemberHarnessVersion = first.HarnessVersion;
            batch.FirstMemberScoringMethodVersion = first.ScoringMethodVersion;
            return;
        }
    }

    private static bool HasBaseline(BenchmarkModelBatchRun batch)
        => batch.FirstMemberHarnessVersion != null
           || batch.FirstMemberScoringMethodVersion != null
           || batch.FirstMemberCandidateSystemPromptSha256 != null
           || batch.FirstMemberToolGuidesSha256 != null
           || batch.FirstMemberKnowledgeBaseHeadSha != null
           || batch.FirstMemberWikiHeadSha != null
           || batch.FirstMemberSourceCodeHeadSha != null;

    /// <summary>
    /// What moved since the first member, or null: MB-R1 against the recorded instrument (and a
    /// battery's definition), then MB-R2 and the exam keys over the finished members' recorded runs.
    /// </summary>
    protected internal async Task<BenchmarkModelBatchDrift?> CheckDriftAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        BenchmarkModelBatchRun batch,
        CancellationToken ct)
    {
        var keys = new List<string>();

        if (batch.TargetKind == BenchmarkModelBatchTargetKind.Battery && batch.BatteryDefinitionSha256 != null)
        {
            string? currentSha = await db.BenchmarkBatteries.AsNoTracking()
                .Where(b => b.Id == batch.BenchmarkBatteryId)
                .Select(b => b.DefinitionSha256)
                .FirstOrDefaultAsync(ct);
            if (!string.Equals(currentSha, batch.BatteryDefinitionSha256, StringComparison.OrdinalIgnoreCase))
            {
                keys.Add("BatteryDefinitionSha256");
            }
        }

        if (HasBaseline(batch))
        {
            if (batch.FirstMemberHarnessVersion != null
                && !string.Equals(batch.FirstMemberHarnessVersion, BenchmarkAssessmentPrompt.HarnessVersion, StringComparison.Ordinal))
            {
                keys.Add("HarnessVersion");
            }

            if (batch.FirstMemberScoringMethodVersion is int method && method != BenchmarkAssessmentPrompt.ScoringMethodVersion)
            {
                keys.Add("ScoringMethodVersion");
            }

            keys.AddRange(await ChangedHashesAsync(services, db, batch, ct));
        }

        if (keys.Count > 0)
        {
            return new BenchmarkModelBatchDrift(
                BenchmarkModelBatchStopReason.InstrumentChanged,
                keys,
                "The instrument moved since the first member: " + string.Join(", ", keys) + ". Members launched now " +
                "would not be comparable with the ones finished. Re-run under the current instrument, or continue and " +
                "accept the change.");
        }

        return await RecordedRunDriftAsync(db, batch, ct);
    }

    /// <summary>
    /// The instrument hashes recorded at the first member that a run launched now would not carry.
    /// Computed for the first member's configuration, so a prompt that differs only by a candidate's
    /// parallel mode is not a change; when that configuration is gone, the prompt hash is not compared.
    /// </summary>
    private async Task<IReadOnlyList<string>> ChangedHashesAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        BenchmarkModelBatchRun batch,
        CancellationToken ct)
    {
        var template = ReadTemplate(batch);
        if (template == null) return Array.Empty<string>();

        long? suiteId = batch.BenchmarkSuiteId;
        if (batch.TargetKind == BenchmarkModelBatchTargetKind.Battery)
        {
            suiteId = await db.BenchmarkBatterySuites.AsNoTracking()
                .Where(s => s.BenchmarkBatteryId == batch.BenchmarkBatteryId && s.BenchmarkSuiteId != null)
                .OrderBy(s => s.OrderIndex)
                .ThenBy(s => s.Id)
                .Select(s => s.BenchmarkSuiteId)
                .FirstOrDefaultAsync(ct);
        }

        if (suiteId == null) return Array.Empty<string>();

        var ordered = batch.Members.OrderBy(m => m.OrderIndex).ToList();
        var baseMember = ordered.FirstOrDefault(m => m.BenchmarkRunId != null || m.BenchmarkRunSeriesId != null || m.BenchmarkBatteryRunId != null)
                         ?? ordered.FirstOrDefault();
        if (baseMember == null) return Array.Empty<string>();

        bool comparePrompt = true;
        var current = await ComputeCurrentFingerprintAsync(services, db, suiteId.Value, baseMember.TestedModelConfigurationId, template, ct);
        if (current == null)
        {
            comparePrompt = false;
            var next = ordered.FirstOrDefault(m => m.Status == BenchmarkModelBatchMemberStatus.Pending);
            if (next != null)
            {
                current = await ComputeCurrentFingerprintAsync(services, db, suiteId.Value, next.TestedModelConfigurationId, template, ct);
            }
        }

        if (current == null) return Array.Empty<string>();

        var changed = new List<string>();
        void Compare(string name, string? recorded, string? now)
        {
            if (string.IsNullOrEmpty(recorded)) return;
            if (!string.Equals(recorded, now, StringComparison.OrdinalIgnoreCase)) changed.Add(name);
        }

        if (comparePrompt)
        {
            Compare("CandidateSystemPromptSha256", batch.FirstMemberCandidateSystemPromptSha256, current.CandidateSystemPromptSha256);
        }

        Compare("ToolGuidesSha256", batch.FirstMemberToolGuidesSha256, current.ToolGuidesSha256);
        Compare("KnowledgeBaseHeadSha", batch.FirstMemberKnowledgeBaseHeadSha, current.KnowledgeBaseHeadSha);
        Compare("WikiHeadSha", batch.FirstMemberWikiHeadSha, current.WikiHeadSha);
        Compare("SourceCodeHeadSha", batch.FirstMemberSourceCodeHeadSha, current.SourceCodeHeadSha);
        return changed;
    }

    /// <summary>The grader keys MB-R2 compares.</summary>
    public static readonly IReadOnlyList<string> GraderKeys = new[]
    {
        BenchmarkComparabilityKey.AssessorConfigurationKey,
        BenchmarkComparabilityKey.SecondOpinionConfigurationKey,
        BenchmarkComparabilityKey.ClaimVerifierConfigurationKey
    };

    /// <summary>The exam and scoring keys a batch holds fixed by construction (MB-S1, MB-S2).</summary>
    public static readonly IReadOnlyList<string> ExamKeys = new[]
    {
        BenchmarkComparabilityKey.ItemRevisionsKey,
        BenchmarkComparabilityKey.AssessedDifficultiesKey,
        BenchmarkComparabilityKey.ScoringProfileKey
    };

    /// <summary>
    /// Compares each finished member's first completed run per suite with the first finished member's,
    /// on <see cref="ExamKeys"/> (InstrumentChanged) and <see cref="GraderKeys"/> (GraderConfigChanged).
    /// </summary>
    private static async Task<BenchmarkModelBatchDrift?> RecordedRunDriftAsync(
        ApplicationDbContext db,
        BenchmarkModelBatchRun batch,
        CancellationToken ct)
    {
        var finished = batch.Members
            .Where(m => IsSuccessful(m.Status))
            .OrderBy(m => m.OrderIndex)
            .ToList();
        if (finished.Count < 2) return null;

        var bySuite = new List<(BenchmarkModelBatchMember Member, Dictionary<long, BenchmarkRun> Runs)>();
        foreach (var member in finished)
        {
            var runs = (await MemberRunsAsync(db, member, completedOnly: true, ct)).ToList();
            await BenchmarkSeriesOrchestrator.HydrateItemRevisionsAsync(db, runs, ct);
            bySuite.Add((member, runs
                .GroupBy(r => r.BenchmarkSuiteIdUsed ?? r.BenchmarkSuiteId ?? 0)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id).First())));
        }

        var baseline = bySuite[0];
        foreach (var (member, runs) in bySuite.Skip(1))
        {
            foreach (var (suiteId, run) in runs)
            {
                if (!baseline.Runs.TryGetValue(suiteId, out var baseRun)) continue;

                var differing = DifferingKeys(baseRun, run);
                var exam = differing.Where(ExamKeys.Contains).ToList();
                if (exam.Count > 0)
                {
                    return new BenchmarkModelBatchDrift(
                        BenchmarkModelBatchStopReason.InstrumentChanged,
                        exam,
                        $"{MemberLabel(member)}'s run #{run.Id.ToString(CultureInfo.InvariantCulture)} was graded against a " +
                        $"different exam than the first member's: {string.Join(", ", exam)}. Re-run under the current " +
                        "instrument, or continue and accept the change.");
                }

                var graders = differing.Where(GraderKeys.Contains).ToList();
                if (graders.Count > 0)
                {
                    return new BenchmarkModelBatchDrift(
                        BenchmarkModelBatchStopReason.GraderConfigChanged,
                        graders,
                        $"{MemberLabel(member)}'s run #{run.Id.ToString(CultureInfo.InvariantCulture)} was graded under a " +
                        $"different grader configuration than the first member's: {string.Join(", ", graders)}. Re-run under " +
                        "the current instrument, or continue and accept the change.");
                }
            }
        }

        return null;
    }

    /// <summary>The comparability keys on which two runs differ, by name.</summary>
    public static IReadOnlyList<string> DifferingKeys(BenchmarkRun first, BenchmarkRun second)
    {
        var a = BenchmarkComparabilityKey.Extract(first).ToDictionary(k => k.Name, k => k.Value, StringComparer.Ordinal);
        return BenchmarkComparabilityKey.Extract(second)
            .Where(k => !a.TryGetValue(k.Name, out var value) || !string.Equals(value, k.Value, StringComparison.Ordinal))
            .Select(k => k.Name)
            .ToList();
    }

    /// <summary>
    /// The runs a member produced, untracked and ascending by id: its run, its series' runs, or its
    /// battery run's live members' runs.
    /// </summary>
    public static async Task<List<BenchmarkRun>> MemberRunsAsync(
        ApplicationDbContext db,
        BenchmarkModelBatchMember member,
        bool completedOnly,
        CancellationToken ct)
    {
        IQueryable<BenchmarkRun> query;
        if (member.BenchmarkBatteryRunId is long batteryRunId)
        {
            var runIds = db.BenchmarkBatteryRunMembers
                .Where(m => m.BenchmarkBatteryRunId == batteryRunId && !m.Superseded)
                .Select(m => m.BenchmarkRunId);
            query = db.BenchmarkRuns.Where(r => runIds.Contains(r.Id));
        }
        else if (member.BenchmarkRunSeriesId is long seriesId)
        {
            query = db.BenchmarkRuns.Where(r => r.RunSeriesId == seriesId);
        }
        else if (member.BenchmarkRunId is long runId)
        {
            query = db.BenchmarkRuns.Where(r => r.Id == runId);
        }
        else
        {
            return new List<BenchmarkRun>();
        }

        if (completedOnly)
        {
            query = query.Where(r => CompletedRunStatuses.Contains(r.Status));
        }

        return await query.AsNoTracking().OrderBy(r => r.Id).ToListAsync(ct);
    }

    // ---------------------------------------------------------------------------------------
    // Cancel
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Cancels the batch: the drive loop, the member's run, series or battery run, and every pending
    /// member. <c>Cancelled</c> is terminal. The batch claim is released whatever canceling the child does.
    /// </summary>
    public async Task<BenchmarkModelBatchResult> CancelAsync(long batchId, CancellationToken ct = default)
    {
        string owner = BenchmarkRunManager.ModelBatchOwner(batchId);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var batch = await db.BenchmarkModelBatchRuns.Include(b => b.Members).FirstOrDefaultAsync(b => b.Id == batchId, ct);
            if (batch == null) return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.NotFound, "Model batch not found.");

            if (IsTerminal(batch.Status))
            {
                return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.Invalid,
                    $"A {batch.Status} model batch cannot be canceled.", batchId);
            }

            if (_active.TryRemove(batchId, out var cts))
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }

            var now = DateTime.UtcNow;
            foreach (var member in batch.Members.Where(m => m.Status == BenchmarkModelBatchMemberStatus.Running))
            {
                try
                {
                    await CancelChildAsync(scope.ServiceProvider, member, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Canceling member {MemberId} of model batch {BatchId} failed.", member.Id, batchId);
                }
            }

            foreach (var member in batch.Members.Where(m => m.Status is BenchmarkModelBatchMemberStatus.Running or BenchmarkModelBatchMemberStatus.Pending))
            {
                if (member.Status == BenchmarkModelBatchMemberStatus.Running) member.CompletedAtUtc = now;
                member.Status = BenchmarkModelBatchMemberStatus.Canceled;
            }

            batch.Status = BenchmarkRunSeriesStatus.Cancelled;
            batch.StopReason = null;
            batch.CompletedAtUtc = now;
            batch.LastProgressAtUtc = now;
            RefreshCounts(batch);
            await db.SaveChangesAsync(CancellationToken.None);

            return BenchmarkModelBatchResult.Ok(batchId);
        }
        finally
        {
            _runManager.ReleaseBatch(owner);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Skip a pending member
    // ---------------------------------------------------------------------------------------

    /// <summary>Marks a pending member Skipped; allowed while the batch is driven.</summary>
    public async Task<BenchmarkModelBatchResult> SkipPendingAsync(long batchId, long memberId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var batch = await db.BenchmarkModelBatchRuns.Include(b => b.Members).FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch == null) return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.NotFound, "Model batch not found.");

        var member = batch.Members.FirstOrDefault(m => m.Id == memberId);
        if (member == null) return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.NotFound, "Member not found.", batchId);

        if (IsTerminal(batch.Status))
        {
            return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.Invalid, $"A {batch.Status} model batch takes no skip.", batchId);
        }

        if (member.Status != BenchmarkModelBatchMemberStatus.Pending)
        {
            return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.Invalid,
                $"Only a pending member can be skipped; {MemberLabel(member)} is {member.Status}.", batchId);
        }

        member.Status = BenchmarkModelBatchMemberStatus.Skipped;
        member.CompletedAtUtc = DateTime.UtcNow;
        batch.LastProgressAtUtc = DateTime.UtcNow;
        RefreshCounts(batch);
        await db.SaveChangesAsync(ct);
        return BenchmarkModelBatchResult.Ok(batchId);
    }

    // ---------------------------------------------------------------------------------------
    // Resume
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The resume modes valid for a batch now, in the order the dialog shows them. None while it is
    /// driven or not Stopped. After an instrument or grader change: Re-run under current instrument
    /// first, then Continue — accept the change. After a member stopped or a restart: Continue and
    /// Skip this model. After a cap or spend stop: Continue.
    /// </summary>
    public static IReadOnlyList<BenchmarkModelBatchResumeOptionDto> ResumeOptionsFor(BenchmarkModelBatchRun batch, bool isDriving)
    {
        if (isDriving || batch.Status != BenchmarkRunSeriesStatus.Stopped) return Array.Empty<BenchmarkModelBatchResumeOptionDto>();

        static BenchmarkModelBatchResumeOptionDto Option(BenchmarkModelBatchResumeMode mode, string label, string reason)
            => new() { Mode = mode, Label = label, Reason = reason };

        bool hasCurrent = CurrentStoppedMember(batch) != null;

        return batch.StopReason switch
        {
            BenchmarkModelBatchStopReason.InstrumentChanged or BenchmarkModelBatchStopReason.GraderConfigChanged => new[]
            {
                Option(BenchmarkModelBatchResumeMode.RerunUnderCurrentInstrument, "Re-run under current instrument",
                    "Re-runs every member at full cost, so the whole batch is measured under one instrument."),
                Option(BenchmarkModelBatchResumeMode.AcceptInstrumentChange, "Continue — accept the change",
                    "Goes on with the remaining members; the batch is then not comparable across the change.")
            },
            BenchmarkModelBatchStopReason.RunCapReached or BenchmarkModelBatchStopReason.SpendDenied => new[]
            {
                Option(BenchmarkModelBatchResumeMode.Continue, "Continue",
                    "Launches the next member once the spend guard allows it.")
            },
            _ => hasCurrent
                ? new[]
                {
                    Option(BenchmarkModelBatchResumeMode.Continue, "Continue",
                        "Resumes the stopped member, re-checking the instrument and the graders first."),
                    Option(BenchmarkModelBatchResumeMode.SkipCurrent, "Skip this model",
                        "Marks the stopped member Skipped and goes on with the next one.")
                }
                : new[]
                {
                    Option(BenchmarkModelBatchResumeMode.Continue, "Continue",
                        "Launches the next member, re-checking the instrument and the graders first.")
                }
        };
    }

    /// <summary>The member a stop left unfinished: Running, Stopped, Failed or Canceled; null when the stop came between members.</summary>
    private static BenchmarkModelBatchMember? CurrentStoppedMember(BenchmarkModelBatchRun batch)
        => batch.Members
            .Where(m => m.Status is BenchmarkModelBatchMemberStatus.Running or BenchmarkModelBatchMemberStatus.Stopped
                or BenchmarkModelBatchMemberStatus.Failed or BenchmarkModelBatchMemberStatus.Canceled)
            .OrderBy(m => m.OrderIndex == batch.CurrentMemberIndex ? 0 : 1)
            .ThenBy(m => m.OrderIndex)
            .FirstOrDefault();

    /// <summary>
    /// Resumes a Stopped batch in one of the modes <see cref="ResumeOptionsFor"/> offers. Refused while
    /// it is driven or anything else is running. Continue re-checks MB-R1 and MB-R2 and, when the
    /// instrument moved, records that stop and refuses with InstrumentChanged.
    /// </summary>
    public async Task<BenchmarkModelBatchResult> ResumeAsync(long batchId, BenchmarkModelBatchResumeMode mode, CancellationToken ct = default)
    {
        string owner = BenchmarkRunManager.ModelBatchOwner(batchId);

        await _gate.WaitAsync(ct);
        try
        {
            if (_active.ContainsKey(batchId))
            {
                return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.Conflict, "This model batch is already running.", batchId);
            }

            if (!_active.IsEmpty)
            {
                return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.Conflict, "Another model batch is running.", batchId);
            }

            if (_runManager.CurrentRunId.HasValue)
            {
                return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.Conflict, "A benchmark run is already in progress.", batchId);
            }

            if (_runManager.ClaimHolder is { } holder && holder != owner)
            {
                return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.Conflict, BenchmarkRunManager.ClaimConflictMessage(holder), batchId);
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var batch = await db.BenchmarkModelBatchRuns.Include(b => b.Members).FirstOrDefaultAsync(b => b.Id == batchId, ct);
            if (batch == null) return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.NotFound, "Model batch not found.");

            if (batch.Status != BenchmarkRunSeriesStatus.Stopped)
            {
                return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.Invalid,
                    $"A {batch.Status} model batch cannot be resumed.", batchId);
            }

            var options = ResumeOptionsFor(batch, isDriving: false);
            if (options.All(o => o.Mode != mode))
            {
                return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.Invalid,
                    $"{mode} is not available for this batch now. Available: {string.Join(", ", options.Select(o => o.Label))}.", batchId);
            }

            var now = DateTime.UtcNow;
            BenchmarkModelBatchMember? resumeChildOf = null;

            switch (mode)
            {
                case BenchmarkModelBatchResumeMode.RerunUnderCurrentInstrument:
                    SupersedeLinks(batch, batch.Members);
                    foreach (var member in batch.Members)
                    {
                        ResetMember(member);
                    }

                    ClearBaseline(batch);
                    batch.InstrumentChangeAcknowledged = false;
                    batch.CurrentMemberIndex = null;
                    break;

                case BenchmarkModelBatchResumeMode.SkipCurrent:
                {
                    var current = CurrentStoppedMember(batch);
                    if (current == null)
                    {
                        return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.Invalid, "No member is stopped.", batchId);
                    }

                    current.Status = BenchmarkModelBatchMemberStatus.Skipped;
                    current.CompletedAtUtc = now;
                    break;
                }

                default:
                {
                    if (mode == BenchmarkModelBatchResumeMode.AcceptInstrumentChange)
                    {
                        batch.InstrumentChangeAcknowledged = true;
                    }
                    else if (!batch.InstrumentChangeAcknowledged)
                    {
                        var drift = await CheckDriftAsync(scope.ServiceProvider, db, batch, ct);
                        if (drift != null)
                        {
                            batch.StopReason = drift.Reason;
                            batch.StopDetail = Truncate(drift.Detail, StopDetailMaxLength);
                            batch.LastProgressAtUtc = now;
                            await db.SaveChangesAsync(ct);
                            return new BenchmarkModelBatchResult
                            {
                                Outcome = BenchmarkModelBatchOutcome.InstrumentChanged,
                                BatchId = batchId,
                                Error = drift.Detail,
                                ChangedKeys = drift.Keys
                            };
                        }
                    }

                    // The stopped member: a child still live is awaited again, a stopped series or
                    // battery run is resumed, anything else is launched afresh.
                    var current = CurrentStoppedMember(batch);
                    if (current != null)
                    {
                        bool hasChild = current.BenchmarkRunId != null || current.BenchmarkRunSeriesId != null || current.BenchmarkBatteryRunId != null;
                        var state = hasChild ? await GetChildStateAsync(scope.ServiceProvider, current, ct) : null;
                        if (state != null && !state.Terminal)
                        {
                            current.Status = BenchmarkModelBatchMemberStatus.Running;
                        }
                        else if (state != null && state.Resumable
                                 && (current.BenchmarkRunSeriesId != null || current.BenchmarkBatteryRunId != null))
                        {
                            resumeChildOf = current;
                        }
                        else
                        {
                            SupersedeLinks(batch, new[] { current });
                            ResetMember(current);
                        }
                    }

                    break;
                }
            }

            batch.Status = BenchmarkRunSeriesStatus.Pending;
            batch.StopReason = null;
            batch.StopDetail = null;
            batch.CompletedAtUtc = null;
            batch.LastProgressAtUtc = now;
            RefreshCounts(batch);

            if (!_runManager.TryClaimBatch(owner))
            {
                return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.Conflict,
                    BenchmarkRunManager.ClaimConflictMessage(_runManager.ClaimHolder), batchId);
            }

            try
            {
                if (resumeChildOf != null)
                {
                    var resumed = await ResumeChildAsync(scope.ServiceProvider, batch, resumeChildOf, owner, ct);
                    if (!resumed.Started)
                    {
                        _runManager.ReleaseBatch(owner);
                        return BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.Invalid,
                            $"{MemberLabel(resumeChildOf)} could not be resumed: {resumed.Error}", batchId);
                    }

                    resumeChildOf.Status = BenchmarkModelBatchMemberStatus.Running;
                    resumeChildOf.CompletedAtUtc = null;
                    resumeChildOf.ErrorMessage = null;
                    batch.Status = BenchmarkRunSeriesStatus.Running;
                    batch.CurrentMemberIndex = resumeChildOf.OrderIndex;
                    RefreshCounts(batch);
                }

                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch
            {
                _runManager.ReleaseBatch(owner);
                throw;
            }

            BeginDriving(batchId);
            return BenchmarkModelBatchResult.Ok(batchId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record SupersededLink(
        long MemberId,
        int OrderIndex,
        long TestedModelConfigurationId,
        string Status,
        long? RunId,
        long? SeriesId,
        long? BatteryRunId,
        DateTime SupersededAtUtc);

    /// <summary>Appends the members' current links to the batch's superseded record.</summary>
    private static void SupersedeLinks(BenchmarkModelBatchRun batch, IEnumerable<BenchmarkModelBatchMember> members)
    {
        var record = new List<SupersededLink>();
        if (!string.IsNullOrWhiteSpace(batch.SupersededMembersJson))
        {
            try
            {
                record = JsonSerializer.Deserialize<List<SupersededLink>>(batch.SupersededMembersJson, JsonOptions) ?? record;
            }
            catch (JsonException)
            {
            }
        }

        var now = DateTime.UtcNow;
        foreach (var member in members)
        {
            if (member.BenchmarkRunId == null && member.BenchmarkRunSeriesId == null && member.BenchmarkBatteryRunId == null) continue;
            record.Add(new SupersededLink(member.Id, member.OrderIndex, member.TestedModelConfigurationId, member.Status.ToString(),
                member.BenchmarkRunId, member.BenchmarkRunSeriesId, member.BenchmarkBatteryRunId, now));
        }

        batch.SupersededMembersJson = record.Count == 0 ? null : JsonSerializer.Serialize(record, JsonOptions);
    }

    private static void ResetMember(BenchmarkModelBatchMember member)
    {
        member.Status = BenchmarkModelBatchMemberStatus.Pending;
        member.BenchmarkRunId = null;
        member.BenchmarkRunSeriesId = null;
        member.BenchmarkBatteryRunId = null;
        member.StartedAtUtc = null;
        member.CompletedAtUtc = null;
        member.ErrorMessage = null;
    }

    private static void ClearBaseline(BenchmarkModelBatchRun batch)
    {
        batch.FirstMemberCandidateSystemPromptSha256 = null;
        batch.FirstMemberToolGuidesSha256 = null;
        batch.FirstMemberKnowledgeBaseHeadSha = null;
        batch.FirstMemberWikiHeadSha = null;
        batch.FirstMemberSourceCodeHeadSha = null;
        batch.FirstMemberHarnessVersion = null;
        batch.FirstMemberScoringMethodVersion = null;
    }

    // ---------------------------------------------------------------------------------------
    // Startup reconciliation
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Moves any batch left Running, Pending or WaitingForCap by an unclean shutdown to Stopped
    /// (<c>RestartReconciled</c>), its running member with it, so Continue becomes available (MB-R4).
    /// Called once at startup, after the series and battery reconciliations.
    /// </summary>
    public async Task<int> ReconcileOrphanedAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var orphaned = await db.BenchmarkModelBatchRuns
            .Include(b => b.Members)
            .Where(b => b.Status == BenchmarkRunSeriesStatus.Running
                        || b.Status == BenchmarkRunSeriesStatus.Pending
                        || b.Status == BenchmarkRunSeriesStatus.WaitingForCap)
            .ToListAsync(ct);

        if (orphaned.Count == 0) return 0;

        var now = DateTime.UtcNow;
        foreach (var batch in orphaned)
        {
            foreach (var member in batch.Members.Where(m => m.Status == BenchmarkModelBatchMemberStatus.Running))
            {
                member.Status = BenchmarkModelBatchMemberStatus.Stopped;
                member.ErrorMessage = "The Overseer service restarted while this member was running.";
            }

            batch.Status = BenchmarkRunSeriesStatus.Stopped;
            batch.StopReason = BenchmarkModelBatchStopReason.RestartReconciled;
            batch.StopDetail = "The Overseer service restarted while this batch was running. Finished members are intact; " +
                               "Continue re-checks the instrument and the graders, then resumes the stopped member.";
            batch.LastProgressAtUtc = now;
            RefreshCounts(batch);
        }

        await db.SaveChangesAsync(ct);
        _logger.LogWarning("Reconciled {Count} orphaned model batch(es) to Stopped.", orphaned.Count);
        return orphaned.Count;
    }

    /// <summary>The stop reason in words.</summary>
    public static string? DescribeStopReason(BenchmarkModelBatchStopReason? reason) => reason switch
    {
        BenchmarkModelBatchStopReason.MemberStopped => "A member did not finish",
        BenchmarkModelBatchStopReason.InstrumentChanged => "The instrument changed",
        BenchmarkModelBatchStopReason.GraderConfigChanged => "A grader's configuration changed",
        BenchmarkModelBatchStopReason.RunCapReached => "Run cap reached",
        BenchmarkModelBatchStopReason.SpendDenied => "Spend guard denied the next run",
        BenchmarkModelBatchStopReason.RestartReconciled => "The service restarted",
        _ => null
    };

    private static string? Truncate(string? value, int max)
        => value == null ? null : value.Length <= max ? value : value.Substring(0, max);
}
