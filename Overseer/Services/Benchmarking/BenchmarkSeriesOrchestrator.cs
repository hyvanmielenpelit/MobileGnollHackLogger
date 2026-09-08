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
/// The outcome of asking to start or resume a series, in the vocabulary the controller maps to HTTP.
/// </summary>
public enum BenchmarkSeriesStartOutcome
{
    Started = 0,
    Conflict = 1,
    NotFound = 2,
    Invalid = 3,
    SpendDenied = 4,

    /// <summary>
    /// A resume was refused because an instrument hash moved since member 1.
    /// <see cref="BenchmarkSeriesStartResult.ChangedInstrumentHashes"/> names which.
    /// </summary>
    InstrumentChanged = 5,

    /// <summary>Candidate and assessor share a provider and the request did not acknowledge it.</summary>
    SameProviderNotAcknowledged = 6
}

public sealed record BenchmarkSeriesStartResult
{
    public BenchmarkSeriesStartOutcome Outcome { get; init; }
    public long? SeriesId { get; init; }
    public string? Error { get; init; }
    public SameProviderWarningDto? SameProviderWarning { get; init; }

    /// <summary>Which of the three instrument hashes moved. Empty unless <see cref="Outcome"/> is InstrumentChanged.</summary>
    public IReadOnlyList<string> ChangedInstrumentHashes { get; init; } = Array.Empty<string>();

    public bool Started => Outcome == BenchmarkSeriesStartOutcome.Started;

    public static BenchmarkSeriesStartResult Ok(long seriesId) =>
        new() { Outcome = BenchmarkSeriesStartOutcome.Started, SeriesId = seriesId };

    public static BenchmarkSeriesStartResult Fail(BenchmarkSeriesStartOutcome outcome, string error) =>
        new() { Outcome = outcome, Error = error };
}

/// <summary>
/// Drives a <see cref="BenchmarkRunSeries"/>: launch member <i>n</i>, wait for it to reach a terminal
/// state, launch member <i>n</i>+1.
///
/// <para><b>Why sequential is not merely a constraint.</b> <see cref="BenchmarkRunManager"/> permits
/// exactly one run at a time — <c>TryStart</c> returns false otherwise — so the orchestrator could
/// not overlap members even if it wanted to. But it is also the right shape: the report already
/// labels this timing mode <i>"Sequential (comparable speed)"</i>, and replicate speed measurement
/// is only meaningful when every member ran under the same contention. The orchestrator therefore
/// goes through <see cref="BenchmarkRunLauncher"/>, which goes through <c>TryStart</c>, and never
/// around that gate.</para>
///
/// <para><b>Why the state lives in the database.</b> This class is an in-memory singleton; a series
/// is a row. An operator resumes a stopped series hours later, possibly after a service restart, and
/// the row is what <see cref="BenchmarkRunSeries.CompletedRunCount"/> is read from. The orchestrator
/// is reconstructed from the row, never the other way round — which is also why
/// <see cref="ReconcileOrphanedSeriesAsync"/> exists.</para>
/// </summary>
public class BenchmarkSeriesOrchestrator
{
    /// <summary>
    /// How long to wait between cap re-checks while a series sits in <c>WaitingForCap</c>. The hourly
    /// window is the one that realistically blocks a series, and it is 60 minutes wide, so polling
    /// faster than this buys nothing but database queries.
    /// </summary>
    private static readonly TimeSpan CapRetryInterval = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Total time a series may sit waiting on the cap before it gives up and stops (resumably). A
    /// series that has waited this long is one an operator should decide about, not one that should
    /// keep a background task alive indefinitely.
    /// </summary>
    private static readonly TimeSpan CapWaitBudget = TimeSpan.FromHours(26);

    /// <summary>How often to check whether the in-flight member has reached a terminal state.</summary>
    private static readonly TimeSpan MemberPollInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BenchmarkRunManager _runManager;
    private readonly ILogger<BenchmarkSeriesOrchestrator> _logger;

    /// <summary>
    /// Cancellation for the series currently being driven, keyed by series id. A series is only ever
    /// present here while this process is driving it — a resume after a restart adds a fresh entry.
    /// </summary>
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _active = new();

    public BenchmarkSeriesOrchestrator(
        IServiceScopeFactory scopeFactory,
        BenchmarkRunManager runManager,
        ILogger<BenchmarkSeriesOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _runManager = runManager;
        _logger = logger;
    }

    /// <summary>The series this process is currently driving, if any.</summary>
    public long? ActiveSeriesId => _active.IsEmpty ? null : _active.Keys.First();

    public bool IsDriving(long seriesId) => _active.ContainsKey(seriesId);

    // ---------------------------------------------------------------------------------------
    // Start
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Validates a multi-run request, creates the series row and begins driving it in the background.
    /// </summary>
    /// <remarks>
    /// <paramref name="request"/>.RunCount is validated against the <b>live</b> configured
    /// <c>MaxRunsPerDay</c>, never a constant. Raising the series ceiling and raising the daily spend
    /// cap are then the same action, which is the correct coupling: a series may never be a way
    /// around the cap.
    /// </remarks>
    public async Task<BenchmarkSeriesStartResult> StartSeriesAsync(
        StartBenchmarkRunRequest request,
        string? userId,
        CancellationToken ct = default)
    {
        if (_runManager.CurrentRunId.HasValue)
        {
            return BenchmarkSeriesStartResult.Fail(
                BenchmarkSeriesStartOutcome.Conflict, "A benchmark run is already in progress.");
        }

        if (!_active.IsEmpty)
        {
            return BenchmarkSeriesStartResult.Fail(
                BenchmarkSeriesStartOutcome.Conflict, "A benchmark run series is already in progress.");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<BenchmarkComplianceGuard>();

        int maxRunsPerDay = guard.MaxRunsPerDay;
        if (request.RunCount < 1)
        {
            return BenchmarkSeriesStartResult.Fail(
                BenchmarkSeriesStartOutcome.Invalid, "RunCount must be at least 1.");
        }

        if (request.RunCount > maxRunsPerDay)
        {
            return BenchmarkSeriesStartResult.Fail(
                BenchmarkSeriesStartOutcome.Invalid,
                $"RunCount ({request.RunCount}) exceeds the configured daily run cap of {maxRunsPerDay}. " +
                "A series may not exceed the cap; raise Benchmark:Compliance:MaxRunsPerDay to raise the series ceiling.");
        }

        var (canSpend, denialReason) = await guard.CanSpendAsync(db, ct);
        if (!canSpend)
        {
            return BenchmarkSeriesStartResult.Fail(
                BenchmarkSeriesStartOutcome.SpendDenied,
                denialReason ?? "The benchmark spend guard refused this series.");
        }

        var suite = await db.BenchmarkSuites.FirstOrDefaultAsync(s => s.Id == request.SuiteId, ct);
        if (suite == null)
        {
            return BenchmarkSeriesStartResult.Fail(
                BenchmarkSeriesStartOutcome.NotFound, "Benchmark suite not found.");
        }

        // Validate the whole request here, before any row is written, through the same rules a
        // single run is admitted under. Without this, a request the launcher would reject — an
        // unacknowledged same-provider pair, an unassessed suite, a disabled verifier — would create
        // a series, launch member 1, fail it, and stop: the operator would get a broken series
        // instead of the 409 they can confirm through, or the error they can act on.
        var launcher = scope.ServiceProvider.GetRequiredService<BenchmarkRunLauncher>();
        var invalid = await launcher.ValidateRequestAsync(request, ct);
        if (invalid != null)
        {
            return new BenchmarkSeriesStartResult
            {
                Outcome = invalid.Outcome switch
                {
                    BenchmarkRunLaunchOutcome.NotFound => BenchmarkSeriesStartOutcome.NotFound,
                    BenchmarkRunLaunchOutcome.SpendDenied => BenchmarkSeriesStartOutcome.SpendDenied,
                    BenchmarkRunLaunchOutcome.SameProviderNotAcknowledged
                        => BenchmarkSeriesStartOutcome.SameProviderNotAcknowledged,
                    _ => BenchmarkSeriesStartOutcome.Invalid
                },
                Error = invalid.Error,
                SameProviderWarning = invalid.SameProviderWarning
            };
        }

        var series = new BenchmarkRunSeries
        {
            BenchmarkSuiteId = suite.Id,
            SuiteName = suite.Name,
            RequestedRunCount = request.RunCount,
            CompletedRunCount = 0,
            FailedRunCount = 0,
            Status = BenchmarkRunSeriesStatus.Pending,
            StartRequestJson = JsonSerializer.Serialize(request),
            AllowCapWait = request.AllowCapWait,
            StartedByUserId = string.IsNullOrEmpty(userId) ? null : userId,
            StartedAtUtc = DateTime.UtcNow,
            LastProgressAtUtc = DateTime.UtcNow
        };

        db.BenchmarkRunSeries.Add(series);
        await db.SaveChangesAsync(ct);

        BeginDriving(series.Id);
        return BenchmarkSeriesStartResult.Ok(series.Id);
    }

    // ---------------------------------------------------------------------------------------
    // Resume
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Continues a stopped series from <see cref="BenchmarkRunSeries.CompletedRunCount"/> + 1.
    ///
    /// <para>Reconstructs everything it needs from the row, so a resume works after a process
    /// restart — the original orchestrator instance is gone, and nothing in the resume path depends
    /// on it. Refuses for <c>Cancelled</c>, <c>Completed</c> and <c>Failed</c>: the operator said
    /// stop, or there is nothing left to do.</para>
    ///
    /// <para>The instrument guard is the important part. If any of the three hashes recorded at
    /// member 1 has moved, the resume is <b>refused</b> and the moved hash is named, because the
    /// remaining members would answer under a different instrument. The
    /// <paramref name="acknowledgeInstrumentChange"/> override continues anyway and marks the
    /// series, which makes the auto-created group Tier C — so those runs stay usable for comparison
    /// and can never be pooled into one index.</para>
    /// </summary>
    public async Task<BenchmarkSeriesStartResult> ResumeSeriesAsync(
        long seriesId,
        bool acknowledgeInstrumentChange,
        CancellationToken ct = default)
    {
        if (_runManager.CurrentRunId.HasValue)
        {
            return BenchmarkSeriesStartResult.Fail(
                BenchmarkSeriesStartOutcome.Conflict, "A benchmark run is already in progress.");
        }

        if (_active.ContainsKey(seriesId))
        {
            return BenchmarkSeriesStartResult.Fail(
                BenchmarkSeriesStartOutcome.Conflict, "This series is already running.");
        }

        if (!_active.IsEmpty)
        {
            return BenchmarkSeriesStartResult.Fail(
                BenchmarkSeriesStartOutcome.Conflict, "Another benchmark run series is already in progress.");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<BenchmarkComplianceGuard>();

        var series = await db.BenchmarkRunSeries.FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series == null)
        {
            return BenchmarkSeriesStartResult.Fail(
                BenchmarkSeriesStartOutcome.NotFound, "Benchmark run series not found.");
        }

        if (!IsResumableStatus(series.Status))
        {
            return BenchmarkSeriesStartResult.Fail(
                BenchmarkSeriesStartOutcome.Invalid,
                $"A {series.Status} series cannot be resumed. Start a new series instead.");
        }

        if (series.CompletedRunCount >= series.RequestedRunCount)
        {
            return BenchmarkSeriesStartResult.Fail(
                BenchmarkSeriesStartOutcome.Invalid,
                "Every requested member of this series has already completed.");
        }

        // Like any other launch, and for the same reason: the cap is about spend, not about who is
        // asking. A resume is a new run.
        var (canSpend, denialReason) = await guard.CanSpendAsync(db, ct);
        if (!canSpend)
        {
            return BenchmarkSeriesStartResult.Fail(
                BenchmarkSeriesStartOutcome.SpendDenied,
                denialReason ?? "The benchmark spend guard refused this resume.");
        }

        var changed = await GetChangedInstrumentHashesAsync(scope.ServiceProvider, db, series, ct);
        if (changed.Count > 0 && !acknowledgeInstrumentChange)
        {
            return new BenchmarkSeriesStartResult
            {
                Outcome = BenchmarkSeriesStartOutcome.InstrumentChanged,
                SeriesId = series.Id,
                ChangedInstrumentHashes = changed,
                Error =
                    "This series cannot be resumed because the instrument changed since its first member: " +
                    string.Join(", ", changed) + ". Runs on either side of that change are not replicates of " +
                    "each other. Start a new series, or continue anyway to mark the resulting group " +
                    "cross-condition (Tier C), which can never be pooled into one index."
            };
        }

        if (changed.Count > 0)
        {
            series.InstrumentChangeAcknowledged = true;
        }

        series.Status = BenchmarkRunSeriesStatus.Pending;
        series.StopReason = null;
        series.ErrorMessage = null;
        series.LastProgressAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        BeginDriving(series.Id);
        return BenchmarkSeriesStartResult.Ok(series.Id);
    }

    private static bool IsResumableStatus(BenchmarkRunSeriesStatus status) =>
        status == BenchmarkRunSeriesStatus.Stopped
        || status == BenchmarkRunSeriesStatus.Pending
        || status == BenchmarkRunSeriesStatus.CompletedWithErrors;

    /// <summary>
    /// Which of the three instrument hashes recorded at member 1 differ from the ones a run launched
    /// now would carry. Empty means the instrument has not moved.
    /// </summary>
    /// <remarks>
    /// A hash the series never recorded — member 1 has not run yet, or the value was unavailable —
    /// is not a change. Treating "unknown" as "moved" would make a series that failed on its first
    /// member permanently unresumable.
    /// </remarks>
    public async Task<IReadOnlyList<string>> GetChangedInstrumentHashesAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        BenchmarkRunSeries series,
        CancellationToken ct = default)
    {
        var request = DeserializeRequest(series);
        if (request == null) return Array.Empty<string>();

        // Nothing was recorded at member 1, so there is nothing any current value could differ
        // from. Compare below would return early on all three anyway; short-circuiting here just
        // avoids building a whole system prompt to produce a result that is discarded.
        if (string.IsNullOrEmpty(series.FirstMemberCandidateSystemPromptSha256)
            && string.IsNullOrEmpty(series.FirstMemberToolGuidesSha256)
            && string.IsNullOrEmpty(series.FirstMemberKnowledgeBaseHeadSha))
        {
            return Array.Empty<string>();
        }

        var benchmarkService = services.GetRequiredService<BenchmarkService>();
        var current = await benchmarkService.ComputeCurrentInstrumentFingerprintAsync(
            db, request.SuiteId, request.TestedModelConfigurationId, request.VerboseMode ?? false, ct);

        if (current == null) return Array.Empty<string>();

        var changed = new List<string>();

        void Compare(string name, string? recorded, string? now)
        {
            if (string.IsNullOrEmpty(recorded)) return;
            if (!string.Equals(recorded, now, StringComparison.OrdinalIgnoreCase))
            {
                changed.Add(name);
            }
        }

        Compare("CandidateSystemPromptSha256", series.FirstMemberCandidateSystemPromptSha256, current.Value.CandidateSystemPromptSha256);
        Compare("ToolGuidesSha256", series.FirstMemberToolGuidesSha256, current.Value.ToolGuidesSha256);
        Compare("KnowledgeBaseHeadSha", series.FirstMemberKnowledgeBaseHeadSha, current.Value.KnowledgeBaseHeadSha);

        return changed;
    }

    // ---------------------------------------------------------------------------------------
    // Cancel
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Cancels the in-flight member <b>and</b> the series. <c>Cancelled</c> is terminal and
    /// deliberately not resumable — the operator said stop, which is a different statement from a
    /// series that halted on its own and left a Continue button.
    /// </summary>
    public async Task<bool> CancelSeriesAsync(long seriesId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var series = await db.BenchmarkRunSeries.FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series == null) return false;

        if (_active.TryRemove(seriesId, out var cts))
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        // The member is cancelled through the run manager, exactly as a single run would be, so the
        // run's own finalisation path runs rather than being bypassed.
        var inFlight = await db.BenchmarkRuns
            .Where(r => r.RunSeriesId == seriesId && r.Status == BenchmarkRunStatus.Running)
            .ToListAsync(ct);

        foreach (var run in inFlight)
        {
            _runManager.TryCancel(run.Id);
        }

        series.Status = BenchmarkRunSeriesStatus.Cancelled;
        series.StopReason = null;
        series.CompletedAtUtc = DateTime.UtcNow;
        series.LastProgressAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return true;
    }

    // ---------------------------------------------------------------------------------------
    // Startup reconciliation
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Moves any series left <c>Running</c> or <c>WaitingForCap</c> by an unclean shutdown to
    /// <c>Stopped</c>, so the Continue button appears rather than the row sitting forever in a state
    /// no live orchestrator is advancing.
    ///
    /// <para>Called once at startup. Safe to call when nothing is orphaned.</para>
    /// </summary>
    public async Task<int> ReconcileOrphanedSeriesAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var orphaned = await db.BenchmarkRunSeries
            .Where(s => s.Status == BenchmarkRunSeriesStatus.Running
                        || s.Status == BenchmarkRunSeriesStatus.WaitingForCap
                        || s.Status == BenchmarkRunSeriesStatus.Pending)
            .ToListAsync(ct);

        if (orphaned.Count == 0) return 0;

        foreach (var series in orphaned)
        {
            series.Status = BenchmarkRunSeriesStatus.Stopped;
            series.StopReason = BenchmarkRunSeriesStopReason.MemberFailed;
            series.ErrorMessage =
                "The Overseer service restarted while this series was running. Completed members are intact; " +
                "continue the series to launch the remaining ones.";
            series.LastProgressAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogWarning("Reconciled {Count} orphaned benchmark run series to Stopped.", orphaned.Count);
        return orphaned.Count;
    }

    // ---------------------------------------------------------------------------------------
    // The drive loop
    // ---------------------------------------------------------------------------------------

    private void BeginDriving(long seriesId)
    {
        var cts = new CancellationTokenSource();
        if (!_active.TryAdd(seriesId, cts))
        {
            cts.Dispose();
            return;
        }

        _ = Task.Run(async () =>
        {
            // The loop outlives the request that started it, and Sentry's scope stack is
            // async-local: without a scope of its own it reports under the start request's context
            // and accretes a tag from every log scope the run machinery opens beneath it, for the
            // whole series. A borrowed provider URL also reaches AuthSentryEventProcessor, which
            // reads the Uri tag and can drop a harness error as an upstream transient.
            using var sentryScope = SentrySdk.PushScope();
            SentrySdk.ConfigureScope(scope =>
            {
                scope.Clear();
                scope.SetTag("SeriesId", seriesId.ToString(CultureInfo.InvariantCulture));
            });

            try
            {
                await DriveAsync(seriesId, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Benchmark run series {SeriesId} was cancelled.", seriesId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Benchmark run series {SeriesId} failed.", seriesId);
                await MarkSeriesFailedAsync(seriesId, ex.Message);
            }
            finally
            {
                if (_active.TryRemove(seriesId, out var removed))
                {
                    removed.Dispose();
                }
            }
        });
    }

    private async Task DriveAsync(long seriesId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var guard = scope.ServiceProvider.GetRequiredService<BenchmarkComplianceGuard>();
            var launcher = scope.ServiceProvider.GetRequiredService<BenchmarkRunLauncher>();

            var series = await db.BenchmarkRunSeries.FirstOrDefaultAsync(s => s.Id == seriesId, ct);
            if (series == null) return;

            if (series.Status == BenchmarkRunSeriesStatus.Cancelled) return;

            if (series.CompletedRunCount >= series.RequestedRunCount)
            {
                await FinishSeriesAsync(scope.ServiceProvider, db, series, ct);
                return;
            }

            var request = DeserializeRequest(series);
            if (request == null)
            {
                await StopSeriesAsync(db, series, BenchmarkRunSeriesStopReason.MemberFailed,
                    "The stored start request could not be read, so no further member can be launched.", ct);
                return;
            }

            // How many more members are needed is CompletedRunCount vs RequestedRunCount — that is
            // what the loop condition above tests, and what a resume continues from.
            //
            // The index *label* counts failures too, so a retry after a failed member 2 is labelled
            // 3 rather than reusing 2. Two rows sharing an index within one series would make the
            // member table and every diagnostics capture ambiguous about which run is which, and the
            // failed run's row is kept deliberately.
            int nextIndex = series.CompletedRunCount + series.FailedRunCount + 1;

            // Re-checked per member, not once at the top: a series of 20 runs spans many hours and
            // the window it is measured against moves the whole time.
            if (!await WaitForCapAsync(db, guard, series, ct))
            {
                return;
            }

            series.Status = BenchmarkRunSeriesStatus.Running;
            series.LastProgressAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var launch = await launcher.CreateAndLaunchRunAsync(
                request, series.StartedByUserId, series.Id, nextIndex, ct);

            if (!launch.Started)
            {
                var reason = launch.Outcome == BenchmarkRunLaunchOutcome.SpendDenied
                    ? BenchmarkRunSeriesStopReason.SpendDenied
                    : BenchmarkRunSeriesStopReason.MemberFailed;

                await StopSeriesAsync(db, series, reason,
                    launch.Error ?? "The next member could not be launched.", ct);
                return;
            }

            long runId = launch.RunId!.Value;

            // Member 1's fingerprint is the series' instrument of record. Captured after the run has
            // stamped it, which is why this waits for the run rather than reading it at launch.
            var status = await AwaitMemberTerminalAsync(runId, ct);

            using var afterScope = _scopeFactory.CreateScope();
            var afterDb = afterScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var afterSeries = await afterDb.BenchmarkRunSeries.FirstOrDefaultAsync(s => s.Id == seriesId, ct);
            if (afterSeries == null) return;

            if (afterSeries.Status == BenchmarkRunSeriesStatus.Cancelled) return;

            var run = await afterDb.BenchmarkRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);

            if (nextIndex == 1 && run != null)
            {
                afterSeries.FirstMemberCandidateSystemPromptSha256 = run.CandidateSystemPromptSha256;
                afterSeries.FirstMemberToolGuidesSha256 = run.ToolGuidesSha256;
                afterSeries.FirstMemberKnowledgeBaseHeadSha = run.KnowledgeBaseHeadSha;
            }

            afterSeries.LastProgressAtUtc = DateTime.UtcNow;

            if (IsSuccessfulTerminal(status))
            {
                afterSeries.CompletedRunCount++;
                await afterDb.SaveChangesAsync(ct);
                continue;
            }

            // A member failed. Stop rather than press on: the remaining members would be launched
            // into whatever condition produced the failure, and the completed ones are worth keeping.
            afterSeries.FailedRunCount++;
            await StopSeriesAsync(afterDb, afterSeries, BenchmarkRunSeriesStopReason.MemberFailed,
                $"Member {nextIndex} (run {runId}) finished as {status}. " +
                $"{afterSeries.CompletedRunCount} completed member(s) were kept.", ct);
            return;
        }
    }

    /// <summary>
    /// Blocks until the spend guard allows the next member, or decides the series cannot proceed.
    /// Returns false when the caller should stop driving — the series has already been moved to a
    /// terminal or resumable state by then.
    /// </summary>
    private async Task<bool> WaitForCapAsync(
        ApplicationDbContext db,
        BenchmarkComplianceGuard guard,
        BenchmarkRunSeries series,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + CapWaitBudget;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var (canSpend, denialReason) = await guard.CanSpendAsync(db, ct);
            if (canSpend) return true;

            if (!series.AllowCapWait)
            {
                await StopSeriesAsync(db, series, BenchmarkRunSeriesStopReason.RunCapReached,
                    denialReason ?? "The run cap blocked the next member.", ct);
                return false;
            }

            if (DateTime.UtcNow >= deadline)
            {
                await StopSeriesAsync(db, series, BenchmarkRunSeriesStopReason.RunCapReached,
                    "The series waited on the run cap for longer than its budget allows. " +
                    "Completed members are intact; continue the series when there is headroom.", ct);
                return false;
            }

            if (series.Status != BenchmarkRunSeriesStatus.WaitingForCap)
            {
                series.Status = BenchmarkRunSeriesStatus.WaitingForCap;
                series.LastProgressAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            await Task.Delay(CapRetryInterval, ct);
        }
    }

    /// <summary>
    /// Waits for a member to leave <see cref="BenchmarkRunStatus.Running"/>.
    /// </summary>
    /// <remarks>
    /// Polls the row rather than the run manager, because the row is what survives a race between the
    /// manager marking the run complete and the service writing its final status — and the row is
    /// what every later reader sees.
    /// </remarks>
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

            await Task.Delay(MemberPollInterval, ct);
        }
    }

    private static bool IsSuccessfulTerminal(BenchmarkRunStatus status) =>
        status == BenchmarkRunStatus.Completed
        || status == BenchmarkRunStatus.CompletedWithLimits
        || status == BenchmarkRunStatus.CompletedWithErrors;

    private async Task StopSeriesAsync(
        ApplicationDbContext db,
        BenchmarkRunSeries series,
        BenchmarkRunSeriesStopReason reason,
        string message,
        CancellationToken ct)
    {
        series.Status = BenchmarkRunSeriesStatus.Stopped;
        series.StopReason = reason;
        series.ErrorMessage = message;
        series.LastProgressAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Benchmark run series {SeriesId} stopped ({Reason}): {Message}", series.Id, reason, message);
    }

    private async Task MarkSeriesFailedAsync(long seriesId, string message)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var series = await db.BenchmarkRunSeries.FirstOrDefaultAsync(s => s.Id == seriesId);
            if (series == null) return;

            series.Status = BenchmarkRunSeriesStatus.Failed;
            series.ErrorMessage = message.Length > 2048 ? message.Substring(0, 2048) : message;
            series.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not mark benchmark run series {SeriesId} as failed.", seriesId);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Completion and the auto-created group
    // ---------------------------------------------------------------------------------------

    private async Task FinishSeriesAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        BenchmarkRunSeries series,
        CancellationToken ct)
    {
        series.Status = series.FailedRunCount > 0
            ? BenchmarkRunSeriesStatus.CompletedWithErrors
            : BenchmarkRunSeriesStatus.Completed;
        series.CompletedAtUtc = DateTime.UtcNow;
        series.LastProgressAtUtc = DateTime.UtcNow;

        if (series.AutoCreatedGroupId == null)
        {
            series.AutoCreatedGroupId = await CreateGroupForSeriesAsync(db, series, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Creates the analysis group for a finished series' successful members.
    ///
    /// <para><b>The tier is resolved, then asserted — never assumed.</b> A series is Tier A by
    /// construction: one request, one instrument, launched sequentially. But "by construction" is an
    /// argument, and the whole point of the tier machinery is that this argument is checked against
    /// the runs rather than trusted. A series that resumed over an acknowledged instrument change is
    /// the case where the argument is knowingly false, and it is marked cross-condition so the group
    /// resolves Tier C and can never be pooled. Any <i>other</i> disagreement is logged loudly
    /// rather than silently persisted as a lower tier: it means either something outside the request
    /// moved between members, or the harness is wrong. The differing keys distinguish the two.</para>
    /// </summary>
    /// <returns>The new group's id, or null when there were fewer than two successful members.</returns>
    private async Task<long?> CreateGroupForSeriesAsync(
        ApplicationDbContext db,
        BenchmarkRunSeries series,
        CancellationToken ct)
    {
        // Untracked: only ids and the display name are used, and the stub answer graph below must
        // not be mistaken for new answers by the two SaveChanges calls at the end of this method.
        var members = await db.BenchmarkRuns
            .AsNoTracking()
            .Where(r => r.RunSeriesId == series.Id
                        && (r.Status == BenchmarkRunStatus.Completed
                            || r.Status == BenchmarkRunStatus.CompletedWithLimits
                            || r.Status == BenchmarkRunStatus.CompletedWithErrors))
            .OrderBy(r => r.RunSeriesIndex)
            .ToListAsync(ct);

        // One run is not a replicate set, and a group of one supports no statistic multi-run adds.
        if (members.Count < 2) return null;

        await HydrateItemRevisionsAsync(db, members, ct);

        var comparability = BenchmarkComparabilityKey.Resolve(members);
        var tier = (BenchmarkRunGroupTier)(int)comparability.Tier;

        if (series.InstrumentChangeAcknowledged)
        {
            // The operator was told the instrument moved and continued anyway. Tier C is the honest
            // description of what came out, and it is enforced here rather than left to the resolver:
            // the members may still hash identically if the change was reverted mid-series.
            if (tier > BenchmarkRunGroupTier.CrossCondition)
            {
                tier = BenchmarkRunGroupTier.CrossCondition;
            }
        }
        else if (tier != BenchmarkRunGroupTier.Replicate)
        {
            _logger.LogError(
                "Benchmark run series {SeriesId} produced a group that resolved {Tier}, not Tier A, " +
                "without an acknowledged instrument change. Differing keys: {Keys}. A series launches " +
                "every member from one identical request, so either something outside the request " +
                "moved between members — a model pricing or catalog edit is the usual one — or this " +
                "is a harness defect. The differing keys above say which.",
                series.Id, tier, string.Join(", ", comparability.Differences.Select(d => d.Describe())));
        }

        string modelName = members[0].TestedModelDisplayNameUsed ?? members[0].TestedModelIdUsed ?? "Unknown model";
        string name = $"{series.SuiteName} · {modelName} · {series.StartedAtUtc:yyyy-MM-dd} · R={members.Count}";

        var group = new BenchmarkRunGroup
        {
            Name = name.Length > 256 ? name.Substring(0, 256) : name,
            BenchmarkSuiteId = series.BenchmarkSuiteId,
            Tier = tier,
            ComparabilityKeyHash = comparability.ComparabilityKeyHash,
            TierReasonsJson = JsonSerializer.Serialize(new
            {
                matchedKeys = comparability.MatchedKeys,
                differences = comparability.Differences.Select(d => new
                {
                    name = d.Name,
                    kind = d.Kind.ToString(),
                    description = d.Describe()
                }),
                explanation = comparability.Explanation
            }),
            CrossCondition = tier == BenchmarkRunGroupTier.CrossCondition,
            Notes = series.InstrumentChangeAcknowledged
                ? "Auto-created from a series that was resumed over an acknowledged instrument change. " +
                  "Marked cross-condition: these members did not all answer under the same instrument."
                : "Auto-created from a completed benchmark run series.",
            CreatedFromSeriesId = series.Id,
            CreatedByUserId = series.StartedByUserId,
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow
        };

        db.BenchmarkRunGroups.Add(group);
        await db.SaveChangesAsync(ct);

        foreach (var run in members)
        {
            db.BenchmarkRunGroupMembers.Add(new BenchmarkRunGroupMember
            {
                BenchmarkRunGroupId = group.Id,
                BenchmarkRunId = run.Id,
                AddedAtUtc = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);
        return group.Id;
    }

    /// <summary>
    /// Fills each member's <see cref="BenchmarkRun.Answers"/> with the stubs
    /// <c>BenchmarkComparabilityKey.ItemRevisionSignature</c> needs, so the tier persisted on the
    /// auto-created group reads the item-revision key rather than rendering it as absent.
    ///
    /// <para><b>Only for untracked runs</b>: the stubs have no key, so attaching them to a tracked
    /// run makes the next <c>SaveChanges</c> insert them as new answers.</para>
    /// </summary>
    private static async Task HydrateItemRevisionsAsync(
        ApplicationDbContext db,
        List<BenchmarkRun> members,
        CancellationToken ct)
    {
        var runIds = members.Select(r => r.Id).ToList();

        var revisions = await db.BenchmarkRunAnswers
            .AsNoTracking()
            .Where(a => runIds.Contains(a.BenchmarkRunId)
                        && (a.BenchmarkQuestionIdUsed != null || a.BenchmarkQuestionId != null))
            .Select(a => new
            {
                a.BenchmarkRunId,
                a.BenchmarkQuestionIdUsed,
                a.BenchmarkQuestionId,
                a.ItemRevisionUsed
            })
            .ToListAsync(ct);

        var byRun = revisions.GroupBy(a => a.BenchmarkRunId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var run in members)
        {
            if (!byRun.TryGetValue(run.Id, out var rows)) continue;

            run.Answers = rows
                .Select(a => new BenchmarkRunAnswer
                {
                    BenchmarkRunId = a.BenchmarkRunId,
                    BenchmarkQuestionIdUsed = a.BenchmarkQuestionIdUsed,
                    BenchmarkQuestionId = a.BenchmarkQuestionId,
                    ItemRevisionUsed = a.ItemRevisionUsed
                })
                .ToList();
        }
    }

    internal static StartBenchmarkRunRequest? DeserializeRequest(BenchmarkRunSeries series)
    {
        try
        {
            return JsonSerializer.Deserialize<StartBenchmarkRunRequest>(series.StartRequestJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
