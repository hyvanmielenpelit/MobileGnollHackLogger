namespace Overseer.Services.ChatConsistency;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;

/// <summary>
/// Runs one assessor calibration of one stored run. The default runs
/// <see cref="BenchmarkService.RunAssessorCalibrationAsync"/> in its own scope; tests pass a fake.
/// </summary>
public delegate Task ChatConsistencyCalibrationRunner(long runId, long assessorConfigId, string? userName, CancellationToken cancellationToken);

/// <summary>The state of a re-grade job.</summary>
public enum ChatConsistencyRegradeJobStatus
{
    Running,
    Completed,
    CompletedWithErrors,
    Canceled,
    Failed
}

/// <summary>One re-grade job: the runs to calibrate in order, the assessor, and the progress. Thread-safe.</summary>
public sealed class ChatConsistencyRegradeJob
{
    private readonly object _lock = new();
    private readonly List<ChatConsistencyRegradeRunError> _errors = new();
    private int _done;
    private long? _currentRunId;
    private DateTime? _completedAtUtc;

    public ChatConsistencyRegradeJob(IReadOnlyList<long> runIds, long assessorConfigId, string assessorDisplay, string? startedByUserName)
    {
        RunIds = runIds;
        AssessorConfigId = assessorConfigId;
        AssessorDisplay = assessorDisplay;
        StartedByUserName = startedByUserName;
    }

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public IReadOnlyList<long> RunIds { get; }
    public long AssessorConfigId { get; }
    public string AssessorDisplay { get; }
    public string? StartedByUserName { get; }
    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;
    public CancellationTokenSource Cts { get; } = new();
    public ChatConsistencyRegradeJobStatus Status { get; private set; } = ChatConsistencyRegradeJobStatus.Running;

    public void SetCurrent(long? runId)
    {
        lock (_lock) _currentRunId = runId;
    }

    public void MarkDone()
    {
        lock (_lock) _done++;
    }

    public void AddError(long runId, string message)
    {
        lock (_lock) _errors.Add(new ChatConsistencyRegradeRunError(runId, message));
    }

    public bool HasErrors
    {
        get
        {
            lock (_lock) return _errors.Count > 0;
        }
    }

    public void SetStatus(ChatConsistencyRegradeJobStatus status)
    {
        lock (_lock)
        {
            Status = status;
            if (status != ChatConsistencyRegradeJobStatus.Running)
            {
                _currentRunId = null;
                _completedAtUtc ??= DateTime.UtcNow;
            }
        }
    }

    public ChatConsistencyRegradeJobView ToView()
    {
        lock (_lock)
        {
            return new ChatConsistencyRegradeJobView
            {
                Id = Id,
                Status = Status switch
                {
                    ChatConsistencyRegradeJobStatus.Running => "running",
                    ChatConsistencyRegradeJobStatus.Completed => "completed",
                    ChatConsistencyRegradeJobStatus.CompletedWithErrors => "completedWithErrors",
                    ChatConsistencyRegradeJobStatus.Canceled => "canceled",
                    _ => "failed"
                },
                AssessorConfigId = AssessorConfigId,
                AssessorDisplay = AssessorDisplay,
                RunIds = RunIds,
                Total = RunIds.Count,
                Done = _done,
                CurrentRunId = _currentRunId,
                Errors = _errors.ToList(),
                StartedAtUtc = StartedAtUtc,
                CompletedAtUtc = _completedAtUtc,
                StartedByUserName = StartedByUserName
            };
        }
    }
}

/// <summary>
/// The single active re-grade job, a singleton like <see cref="BenchmarkDifficultyJobManager"/>: one job
/// at a time; the last one stays readable after it ends.
/// </summary>
public class ChatConsistencyRegradeJobManager
{
    private readonly object _lock = new();
    private ChatConsistencyRegradeJob? _current;

    public ChatConsistencyRegradeJob? Current
    {
        get
        {
            lock (_lock) return _current;
        }
    }

    public bool TryStart(ChatConsistencyRegradeJob job, out ChatConsistencyRegradeJob? existing)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_lock)
        {
            if (_current != null && _current.Status == ChatConsistencyRegradeJobStatus.Running)
            {
                existing = _current;
                return false;
            }

            _current = job;
            existing = null;
            return true;
        }
    }

    public bool TryCancel()
    {
        lock (_lock)
        {
            if (_current == null || _current.Status != ChatConsistencyRegradeJobStatus.Running) return false;
            try
            {
                _current.Cts.Cancel();
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }
}

/// <summary>
/// Re-grades stored runs with one assessor configuration, for the chat consistency analysis's common
/// grader and grader-drift measurement. The only spending path of the analysis: it runs the existing
/// assessor calibration (<see cref="BenchmarkService.RunAssessorCalibrationAsync"/>) sequentially over
/// the selected runs, writing <see cref="BenchmarkAssessorCalibration"/> rows the analysis then reads.
/// Shows an estimate first (<see cref="EstimateAsync"/>), and starts only on a confirmed request.
/// Scoped; the job itself lives in the singleton <see cref="ChatConsistencyRegradeJobManager"/> and
/// opens its own scopes, so it outlives the request that started it.
/// </summary>
public class ChatConsistencyRegradeService
{
    /// <summary>The most runs one job re-grades.</summary>
    public const int MaxRunsPerJob = 200;

    private readonly ApplicationDbContext _db;
    private readonly ChatConsistencyRegradeJobManager _jobs;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BenchmarkRunManager _runManager;
    private readonly ILogger<ChatConsistencyRegradeService> _logger;
    private readonly ModelPricingService? _pricingService;
    private readonly BenchmarkComplianceGuard? _complianceGuard;
    private readonly ChatConsistencyCalibrationRunner _calibrationRunner;

    public ChatConsistencyRegradeService(
        ApplicationDbContext db,
        ChatConsistencyRegradeJobManager jobs,
        IServiceScopeFactory scopeFactory,
        BenchmarkRunManager runManager,
        ILogger<ChatConsistencyRegradeService> logger,
        ModelPricingService? pricingService = null,
        BenchmarkComplianceGuard? complianceGuard = null,
        ChatConsistencyCalibrationRunner? calibrationRunner = null)
    {
        _db = db;
        _jobs = jobs;
        _scopeFactory = scopeFactory;
        _runManager = runManager;
        _logger = logger;
        _pricingService = pricingService;
        _complianceGuard = complianceGuard;
        _calibrationRunner = calibrationRunner ?? DefaultRunner(scopeFactory);
    }

    /// <summary>
    /// Why <paramref name="run"/> cannot be re-graded, or null when it can: it must be usable, have
    /// finished its suite, be graded under scoring method <see cref="BenchmarkAssessmentPrompt.ScoringMethodVersion"/>,
    /// and have its board recorded.
    /// </summary>
    public static string? Refusal(BenchmarkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.ScoringMethodVersion != BenchmarkAssessmentPrompt.ScoringMethodVersion)
        {
            return "Re-grading needs scoring method " + Inv(BenchmarkAssessmentPrompt.ScoringMethodVersion)
                + "; run #" + Inv(run.Id) + " is at method " + Inv(run.ScoringMethodVersion) + ".";
        }

        if (!ChatConsistencyMeasures.UsableStatuses.Contains(run.Status))
        {
            return "Run #" + Inv(run.Id) + " has status " + run.Status + "; only completed runs are re-graded.";
        }

        if (BenchmarkRunFinalizer.IsAbortedRun(run, run.Answers))
        {
            return "Run #" + Inv(run.Id) + ": " + BenchmarkService.AbortedRunRefusal;
        }

        if (BenchmarkRunExamRecord.BoardUnknown(run))
        {
            return "Run #" + Inv(run.Id) + ": " + BenchmarkRunExamRecord.BoardNotRecordedRefusal;
        }

        return null;
    }

    /// <summary>
    /// What re-grading <paramref name="runIds"/> with <paramref name="assessorConfigId"/> is expected to
    /// cost: each run's recorded assessor token totals priced at the chosen assessor's current price. An
    /// estimate: the new assessor's verbosity differs, and only answers the original assessor scored are re-graded.
    /// </summary>
    public async Task<ChatConsistencyRegradeEstimate> EstimateAsync(IReadOnlyList<long> runIds, long assessorConfigId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runIds);
        var (config, configRefusal) = await ValidateAssessorAsync(assessorConfigId, ct);
        ModelPricing? card = config != null && _pricingService != null ? _pricingService.Resolve(config) : null;

        var ids = runIds.Distinct().OrderBy(i => i).ToList();
        var runs = await _db.BenchmarkRuns.AsNoTracking().Include(r => r.Answers).Where(r => ids.Contains(r.Id)).ToListAsync(ct);
        var byId = runs.ToDictionary(r => r.Id);

        var rows = new List<ChatConsistencyRegradeRunEstimate>();
        foreach (long id in ids)
        {
            if (!byId.TryGetValue(id, out var run))
            {
                rows.Add(new ChatConsistencyRegradeRunEstimate { RunId = id, Eligible = false, Refusal = "Run #" + Inv(id) + " was not found." });
                continue;
            }

            string? refusal = Refusal(run);
            decimal? cost = card != null && refusal == null
                ? ModelPricingService.ComputeCostFromTotals(
                    card,
                    run.TotalAssessmentInputTokens, run.TotalAssessmentOutputTokens,
                    run.TotalAssessmentCacheReadTokens, run.TotalAssessmentCacheCreationTokens,
                    requestedServiceTier: config!.ServiceTier)
                : null;

            rows.Add(new ChatConsistencyRegradeRunEstimate
            {
                RunId = id,
                Eligible = refusal == null,
                Refusal = refusal,
                GradableAnswerCount = run.Answers.Count(a => a.Status == BenchmarkAnswerStatus.Ok && a.QualityScore.HasValue && a.ExpectedPointsRecorded),
                RecordedAssessorInputTokens = run.TotalAssessmentInputTokens,
                RecordedAssessorOutputTokens = run.TotalAssessmentOutputTokens,
                RecordedAssessorCacheReadTokens = run.TotalAssessmentCacheReadTokens,
                RecordedAssessorCacheCreationTokens = run.TotalAssessmentCacheCreationTokens,
                EstimatedCostUsd = cost
            });
        }

        var eligible = rows.Where(r => r.Eligible).ToList();
        bool priced = card != null;
        return new ChatConsistencyRegradeEstimate
        {
            AssessorConfigId = assessorConfigId,
            AssessorDisplay = config?.DisplayName ?? config?.ModelId ?? ("configuration " + Inv(assessorConfigId)),
            AssessorRefusal = configRefusal,
            Runs = rows,
            EligibleRunCount = eligible.Count,
            EstimatedTotalCostUsd = priced ? eligible.Sum(r => r.EstimatedCostUsd ?? 0m) : null,
            PricingAvailable = priced,
            Note = "Estimate: each run's recorded assessor token totals priced at the chosen assessor's current price. "
                + "The re-grade makes one assessor call per answer the original assessor scored, and no candidate call; "
                + "the chosen assessor's verbosity can make the actual cost differ."
                + (priced ? string.Empty : " The chosen assessor has no published price, so no cost is shown.")
        };
    }

    /// <summary>
    /// Starts a re-grade job. Refused without <see cref="ChatConsistencyRegradeRequest.Confirmed"/>, for an
    /// invalid assessor, for any ineligible run (each reason listed), while a benchmark run or another
    /// re-grade is in progress, and when the spending guard denies it.
    /// </summary>
    public async Task<ChatConsistencyRegradeStartResult> StartAsync(ChatConsistencyRegradeRequest request, string? userName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Confirmed)
        {
            return Refused("Confirm the estimate first: re-grading spends assessor tokens.");
        }

        var ids = (request.RunIds ?? Array.Empty<long>()).Distinct().ToList();
        if (ids.Count == 0) return Refused("Choose at least one run to re-grade.");
        if (ids.Count > MaxRunsPerJob) return Refused("One re-grade job takes at most " + Inv(MaxRunsPerJob) + " runs.");

        var (config, configRefusal) = await ValidateAssessorAsync(request.AssessorConfigId, ct);
        if (config == null) return Refused(configRefusal ?? "The assessor configuration was not found.");

        var runs = await _db.BenchmarkRuns.AsNoTracking().Include(r => r.Answers).Where(r => ids.Contains(r.Id)).ToListAsync(ct);
        var refusals = new List<string>();
        foreach (long id in ids)
        {
            var run = runs.FirstOrDefault(r => r.Id == id);
            string? refusal = run == null ? "Run #" + Inv(id) + " was not found." : Refusal(run);
            if (refusal != null) refusals.Add(refusal);
        }

        if (refusals.Count > 0) return Refused(string.Join(" ", refusals));

        if (_runManager.CurrentRunId.HasValue) return Refused("A benchmark run is already in progress.");
        if (_runManager.ClaimHolder is { } owner) return Refused(BenchmarkRunManager.ClaimConflictMessage(owner));

        if (_complianceGuard != null)
        {
            var (allowed, denial) = await _complianceGuard.CanSpendAsync(_db, ct);
            if (!allowed) return Refused(denial ?? "Spending is not allowed right now.");
        }

        var ordered = runs.OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id).Select(r => r.Id).ToList();
        var job = new ChatConsistencyRegradeJob(ordered, config.Id, config.DisplayName ?? config.ModelId, userName);
        if (!_jobs.TryStart(job, out _)) return Refused("A re-grade job is already running.");

        var runner = _calibrationRunner;
        var scopeFactory = _scopeFactory;
        var runManager = _runManager;
        var logger = _logger;
        _ = Task.Run(() => ExecuteAsync(job, runner, scopeFactory, runManager, logger));

        return new ChatConsistencyRegradeStartResult(true, null, job.ToView());
    }

    /// <summary>The current or last re-grade job, or null when none has run since start-up.</summary>
    public ChatConsistencyRegradeJobView? GetJob() => _jobs.Current?.ToView();

    /// <summary>Cancels the running job; false when none runs.</summary>
    public bool Cancel() => _jobs.TryCancel();

    /// <summary>
    /// Calibrates the job's runs one at a time, each under the benchmark run gate, and checks that each
    /// wrote a calibration row. Never throws.
    /// </summary>
    internal static async Task ExecuteAsync(
        ChatConsistencyRegradeJob job,
        ChatConsistencyCalibrationRunner runner,
        IServiceScopeFactory scopeFactory,
        BenchmarkRunManager runManager,
        ILogger logger)
    {
        var token = job.Cts.Token;
        try
        {
            foreach (long runId in job.RunIds)
            {
                if (token.IsCancellationRequested)
                {
                    job.SetStatus(ChatConsistencyRegradeJobStatus.Canceled);
                    return;
                }

                job.SetCurrent(runId);
                using var runCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                if (!runManager.TryStart(runId, runCts, out _))
                {
                    job.AddError(runId, "A benchmark run started meanwhile; the re-grade stopped before this run.");
                    job.SetStatus(ChatConsistencyRegradeJobStatus.Failed);
                    return;
                }

                DateTime startedAt = DateTime.UtcNow;
                try
                {
                    await runner(runId, job.AssessorConfigId, job.StartedByUserName, runCts.Token);
                    string? problem = await CalibrationProblemAsync(scopeFactory, runId, job.AssessorConfigId, startedAt);
                    if (problem != null) job.AddError(runId, problem);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    job.SetStatus(ChatConsistencyRegradeJobStatus.Canceled);
                    return;
                }
                catch (OperationCanceledException)
                {
                    job.AddError(runId, "The calibration of this run was canceled.");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Chat consistency re-grade of run {RunId} failed.", runId);
                    job.AddError(runId, "The calibration failed: " + ex.Message);
                }
                finally
                {
                    runManager.Complete(runId);
                }

                job.MarkDone();
            }

            job.SetStatus(job.HasErrors ? ChatConsistencyRegradeJobStatus.CompletedWithErrors : ChatConsistencyRegradeJobStatus.Completed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chat consistency re-grade job {JobId} failed.", job.Id);
            job.SetStatus(ChatConsistencyRegradeJobStatus.Failed);
        }
    }

    /// <summary>Null when the run has a calibration row by the assessor written since <paramref name="since"/> without an error; otherwise why not.</summary>
    private static async Task<string?> CalibrationProblemAsync(IServiceScopeFactory scopeFactory, long runId, long assessorConfigId, DateTime since)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var latest = await db.BenchmarkAssessorCalibrations
            .AsNoTracking()
            .Where(c => c.BenchmarkRunId == runId && c.AssessorModelConfigurationId == assessorConfigId && c.CreatedAtUtc >= since)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync();

        if (latest == null) return "No calibration was written: the calibration refused the run (see the server log).";
        if (latest.ErrorMessage != null) return latest.ErrorMessage;
        if (latest.AnswerCount == 0) return "The calibration graded no answer.";
        return null;
    }

    private async Task<(SystemAiApiConfiguration? Config, string? Refusal)> ValidateAssessorAsync(long assessorConfigId, CancellationToken ct)
    {
        var config = await _db.SystemAiApiConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == assessorConfigId, ct);
        if (config == null) return (null, "The assessor configuration was not found.");
        if (!config.IsEnabled) return (null, "The assessor configuration is disabled.");
        if (string.IsNullOrWhiteSpace(config.EncryptedApiKey)) return (null, "The assessor configuration has no API key.");
        if ((config.ModelRole & 4) != 4) return (null, "The assessor configuration does not have the Benchmark role.");
        return (config, null);
    }

    private static ChatConsistencyCalibrationRunner DefaultRunner(IServiceScopeFactory scopeFactory)
        => async (runId, assessorConfigId, userName, ct) =>
        {
            using var scope = scopeFactory.CreateScope();
            var benchmark = scope.ServiceProvider.GetRequiredService<BenchmarkService>();
            await benchmark.RunAssessorCalibrationAsync(runId, assessorConfigId, userName, ct);
        };

    private static ChatConsistencyRegradeStartResult Refused(string reason) => new(false, reason, null);

    private static string Inv(long value) => value.ToString(CultureInfo.InvariantCulture);
}
