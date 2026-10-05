namespace Overseer.Services.Benchmarking;

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
using Overseer.Models;
using Overseer.Services;

/// <summary>
/// Writes a battery run's three AI-written battery-completion documents, the Executive Summary, the
/// Report for AI Researchers and Developers and the Internal Improvement Brief, once, after the
/// battery finishes and its analysis is complete, with the report writer the battery run names.
///
/// <para>Each document is an immutable <see cref="BenchmarkReportDocument"/> row with
/// <see cref="BenchmarkReportDocumentOrigin.BatteryCompletion"/> and subject <c>battery:&lt;id&gt;</c>:
/// a one-entry Model Comparison of the battery result, with no peers. Nothing is rewritten: writing
/// again means deleting the documents first.</para>
///
/// <para>A job shares the report-pack slot with the Report Pack and the run-completion documents: it
/// queues behind a running job, checks the compliance guard and the writer, then writes whichever of
/// the requested documents the battery run does not have yet. The battery run's
/// <see cref="BenchmarkBatteryRun.ReportDocumentsStatus"/> tracks it, and
/// <see cref="TryGetJob(long)"/> shows it while it runs and for
/// <see cref="BenchmarkRunReportDocumentService.FinishedJobRetention"/> after, in memory only.</para>
///
/// <para>Singleton: it opens its own scope for every job and every status update.</para>
/// </summary>
public sealed class BenchmarkBatteryReportDocumentService
{
    public const string NotFinishedMessage = "The battery run has not finished, so there is nothing to write about yet.";
    public const string NoCurrentAnalysisMessage =
        "The battery run has no complete, current battery analysis to write about. Compute the battery analysis first.";
    public const string RestartMessage = "Overseer restarted before the battery reports were written.";

    /// <summary>The length <see cref="BenchmarkBatteryRun.ReportDocumentsMessage"/> holds.</summary>
    public const int MaxMessageLength = BenchmarkRunReportDocumentService.MaxMessageLength;

    /// <summary>How long <see cref="SettleAfterDeleteAsync"/> waits for a canceled job to stop before deleting.</summary>
    public static readonly TimeSpan DeleteWaitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The battery-completion documents of every battery run, in the order they are written.</summary>
    public static IReadOnlyList<BenchmarkReportAudience> Audiences => BenchmarkRunReportDocumentService.Audiences;

    private enum JobPhase { Queued, Preparing, Writing, Finished }

    /// <summary>One battery run's job, as the battery run report dialog shows it. Mutable fields change under <see cref="_lock"/>.</summary>
    private sealed class BatteryReportJobState
    {
        public required BenchmarkReportPackJob Job { get; init; }
        public required long BatteryRunId { get; init; }
        public JobPhase Phase { get; set; } = JobPhase.Queued;
        public DateTime QueuedAtUtc { get; set; }
        public DateTime? SlotAcquiredAtUtc { get; set; }
        public DateTime? FinishedAtUtc { get; set; }
        public DateTime? CancelRequestedAtUtc { get; set; }
        public required List<BenchmarkReportAudience> Audiences { get; init; }
        public long WriterConfigId { get; init; }
        public string WriterDisplayName { get; init; } = string.Empty;
        public string WriterProvider { get; init; } = string.Empty;
        public string WriterModelId { get; init; } = string.Empty;
        public string? WriterThinkingLevel { get; init; }
    }

    /// <summary>A claimed battery run whose job is not registered yet; empty <see cref="Audiences"/> requests every missing one.</summary>
    private sealed record BatteryClaim(DateTime ClaimedAtUtc, List<BenchmarkReportAudience> Audiences);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BenchmarkReportPackJobManager _jobManager;
    private readonly ILogger<BenchmarkBatteryReportDocumentService> _logger;
    private readonly TimeProvider _time;
    private readonly object _lock = new();
    /// <summary>The battery runs this process holds a job for, each with the signal its job sets when it ends.</summary>
    private readonly Dictionary<long, TaskCompletionSource> _active = new();
    private readonly Dictionary<long, BatteryReportJobState> _jobs = new();
    private readonly Dictionary<long, BatteryClaim> _claims = new();

    public BenchmarkBatteryReportDocumentService(
        IServiceScopeFactory scopeFactory,
        BenchmarkReportPackJobManager jobManager,
        ILogger<BenchmarkBatteryReportDocumentService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _jobManager = jobManager;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The service's clock, which stamps the job view.</summary>
    public DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    /// <summary>The comparison entry key the battery run's own documents are stored under.</summary>
    public static string SubjectKeyOf(long batteryRunId) => BenchmarkReportPackPreparation.BatterySubjectKeyOf(batteryRunId);

    /// <summary>The battery run id of a <c>battery:&lt;id&gt;</c> subject key; false for anything else.</summary>
    public static bool TryParseSubjectKey(string? subjectKey, out long batteryRunId)
    {
        batteryRunId = 0;
        string prefix = BenchmarkBatteryModelComparison.KeyPrefix;
        // NumberStyles.None admits digits only: no sign, whitespace or separator.
        return subjectKey != null
            && subjectKey.StartsWith(prefix, StringComparison.Ordinal)
            && long.TryParse(subjectKey.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out batteryRunId);
    }

    /// <summary>
    /// The battery run as a comparison source, with its latest analysis and usable member runs; null
    /// when it does not exist.
    /// </summary>
    public static async Task<BenchmarkBatteryComparisonSource?> LoadSourceAsync(ApplicationDbContext db, long batteryRunId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var (sources, _) = await BenchmarkBatteryModelComparison.LoadAsync(db, new[] { batteryRunId }, ct);
        return sources?.SingleOrDefault();
    }

    /// <summary>
    /// Why the battery result cannot be written about, or null when it can: the battery has not
    /// finished (<see cref="NotFinishedMessage"/>), or its latest analysis is missing, unreadable,
    /// stale or incomplete (<see cref="NoCurrentAnalysisMessage"/>), as the model comparison decides.
    /// </summary>
    public static string? SubjectRefusal(BenchmarkBatteryComparisonSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!BenchmarkBatteryModelComparison.IsFinished(source.Status)) return NotFinishedMessage;
        return source.Refusal != null ? NoCurrentAnalysisMessage : null;
    }

    /// <summary>
    /// A stand-in configuration carrying the provider and model id recorded on the battery's newest
    /// identity run, for the writer checks; empty when the battery run has no member run.
    /// </summary>
    public static SystemAiApiConfiguration CandidateIdentity(BenchmarkBatteryComparisonSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var newest = source.IdentityRuns.OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id).LastOrDefault();
        return newest != null
            ? BenchmarkRunReportDocumentService.CandidateIdentity(newest)
            : new SystemAiApiConfiguration { Provider = string.Empty, ModelId = string.Empty, DisplayName = string.Empty };
    }

    /// <summary>The audiences of <see cref="Audiences"/> the battery run has no battery-completion document for.</summary>
    public static async Task<List<BenchmarkReportAudience>> MissingAudiencesAsync(ApplicationDbContext db, long batteryRunId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        string subjectKey = SubjectKeyOf(batteryRunId);
        var existing = await db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(d => d.SubjectKey == subjectKey && d.Origin == BenchmarkReportDocumentOrigin.BatteryCompletion)
            .Select(d => d.Audience)
            .Distinct()
            .ToListAsync(ct);
        return Audiences.Where(a => !existing.Contains(a)).ToList();
    }

    /// <summary>True while this process holds a job for the battery run, queued or writing.</summary>
    public bool IsActive(long batteryRunId)
    {
        lock (_lock)
        {
            return _active.ContainsKey(batteryRunId);
        }
    }

    /// <summary>
    /// Writes the battery run's documents in the background when they are due: the battery has
    /// finished, its latest analysis is complete and current, it names a report writer, it has no
    /// battery-completion document yet, and no job for it is Pending or Writing. Returns at once; the
    /// task ends when the job does, or at once when nothing is due. A writer of the candidate's
    /// provider was acknowledged at battery start, so its documents record the acknowledgment.
    /// </summary>
    public Task ScheduleIfDue(long batteryRunId) => Task.Run(() => ScheduleIfDueCoreAsync(batteryRunId));

    /// <summary>
    /// Starts a job with the writer already recorded on the battery run, for the requested audiences
    /// the battery run is missing, in <see cref="Audiences"/> order; null or empty requests every
    /// missing one. A writer of the candidate's provider needs <paramref name="sameProviderAcknowledged"/>,
    /// or the job settles the battery run as Failed with the warning. False when this process already
    /// holds a job for the battery run.
    /// </summary>
    public bool TryStart(
        long batteryRunId,
        string? userId,
        IReadOnlyCollection<BenchmarkReportAudience>? audiences,
        bool sameProviderAcknowledged,
        out Task completion)
    {
        var requested = audiences?.ToList();
        if (!TryClaim(batteryRunId, requested))
        {
            completion = Task.CompletedTask;
            return false;
        }

        completion = Task.Run(() => RunClaimedAsync(batteryRunId, userId, requested, sameProviderAcknowledged));
        return true;
    }

    /// <summary>
    /// Asks the battery run's job to stop. A queued job leaves the queue; a job that is writing keeps
    /// every document already stored. Either way the battery run settles as
    /// <see cref="BenchmarkRunReportDocumentsStatus.Canceled"/>.
    /// </summary>
    public BenchmarkRunReportDocumentService.CancelOutcome TryCancel(long batteryRunId)
    {
        BenchmarkReportPackJob job;
        lock (_lock)
        {
            if (!_jobs.TryGetValue(batteryRunId, out var state)) return BenchmarkRunReportDocumentService.CancelOutcome.NotFound;
            if (state.Phase == JobPhase.Finished) return BenchmarkRunReportDocumentService.CancelOutcome.NotInProgress;

            job = state.Job;
            if (state.CancelRequestedAtUtc == null)
            {
                state.CancelRequestedAtUtc = UtcNow;
                job.AddLog("Cancellation requested.", "warning");
            }
        }

        // Outside the lock: cancellation callbacks may run the job's continuations inline.
        try
        {
            job.Cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        return BenchmarkRunReportDocumentService.CancelOutcome.Requested;
    }

    /// <summary>The battery run's job as the service's clock sees it now; see <see cref="TryGetJob(long, DateTime)"/>.</summary>
    public BenchmarkRunReportJobDto? TryGetJob(long batteryRunId) => TryGetJob(batteryRunId, UtcNow);

    /// <summary>
    /// The battery run's current or last job, with the battery run id in
    /// <see cref="BenchmarkRunReportJobDto.RunId"/>, or null when this process knows none, or its
    /// finished job is older than <see cref="BenchmarkRunReportDocumentService.FinishedJobRetention"/>.
    /// A claimed battery run whose job is still being created shows as Queued since the claim. The
    /// persisted <see cref="BenchmarkRunReportJobDto.Status"/> and <see cref="BenchmarkRunReportJobDto.Message"/>
    /// are the caller's to fill from the battery run row.
    /// </summary>
    public BenchmarkRunReportJobDto? TryGetJob(long batteryRunId, DateTime nowUtc)
    {
        BatteryReportJobState state;
        JobPhase phase;
        DateTime queuedAt;
        DateTime? slotAcquiredAt, finishedAt, cancelRequestedAt;
        lock (_lock)
        {
            Prune(nowUtc);
            if (!_jobs.TryGetValue(batteryRunId, out var found))
            {
                return _claims.TryGetValue(batteryRunId, out var claim) ? StartingView(batteryRunId, claim, nowUtc) : null;
            }
            state = found;
            phase = state.Phase;
            queuedAt = state.QueuedAtUtc;
            slotAcquiredAt = state.SlotAcquiredAtUtc;
            finishedAt = state.FinishedAtUtc;
            cancelRequestedAt = state.CancelRequestedAtUtc;
        }

        // Outside the lock: the job manager has its own, and neither lock is taken inside the other.
        var (ahead, running) = _jobManager.QueueInfo(state.Job);
        bool queued = phase == JobPhase.Queued;

        return new BenchmarkRunReportJobDto
        {
            RunId = batteryRunId,
            Phase = phase.ToString(),
            QueuedAtUtc = queuedAt,
            SlotAcquiredAtUtc = slotAcquiredAt,
            FinishedAtUtc = finishedAt,
            CancelRequestedAtUtc = cancelRequestedAt,
            JobsAhead = queued ? ahead : null,
            BlockingJobLabel = queued && running != null && !ReferenceEquals(running, state.Job) ? BlockingLabel(running) : null,
            Audiences = state.Audiences.ToList(),
            WriterConfigId = state.WriterConfigId,
            WriterDisplayName = state.WriterDisplayName,
            WriterProvider = state.WriterProvider,
            WriterModelId = state.WriterModelId,
            WriterThinkingLevel = state.WriterThinkingLevel,
            Job = state.Job.ToDto(),
            ServerTimeUtc = nowUtc
        };
    }

    /// <summary>The view of a claimed battery run before its job is registered: Queued since the claim, with no writer and no progress.</summary>
    private static BenchmarkRunReportJobDto StartingView(long batteryRunId, BatteryClaim claim, DateTime nowUtc) => new()
    {
        RunId = batteryRunId,
        Phase = nameof(JobPhase.Queued),
        QueuedAtUtc = claim.ClaimedAtUtc,
        Audiences = claim.Audiences.ToList(),
        WriterConfigId = 0,
        WriterDisplayName = string.Empty,
        WriterProvider = string.Empty,
        WriterModelId = string.Empty,
        WriterThinkingLevel = null,
        Job = new BenchmarkReportPackJobDto(),
        ServerTimeUtc = nowUtc
    };

    /// <summary>
    /// Settles battery runs a previous process left Pending or Writing: no job survives a restart, so
    /// each is marked Failed with <see cref="RestartMessage"/>, and Write Reports writes what is missing.
    /// </summary>
    public static async Task<int> SettleInterruptedAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var interrupted = await db.BenchmarkBatteryRuns
            .IgnoreAutoIncludes()
            .Where(r => r.ReportDocumentsStatus == BenchmarkRunReportDocumentsStatus.Pending
                || r.ReportDocumentsStatus == BenchmarkRunReportDocumentsStatus.Writing)
            .ToListAsync(ct);

        foreach (var batteryRun in interrupted)
        {
            batteryRun.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Failed;
            batteryRun.ReportDocumentsMessage = RestartMessage;
        }

        if (interrupted.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        return interrupted.Count;
    }

    /// <summary>
    /// After one of the battery run's documents is deleted: a battery run whose documents are not
    /// being written returns to <see cref="BenchmarkRunReportDocumentsStatus.NotRequested"/> with no
    /// message. The battery run keeps its report writer. Nothing happens when the battery run is gone.
    /// </summary>
    public static async Task SettleAfterDocumentDeleteAsync(ApplicationDbContext db, long batteryRunId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var batteryRun = await db.BenchmarkBatteryRuns.IgnoreAutoIncludes().FirstOrDefaultAsync(r => r.Id == batteryRunId, ct);
        if (batteryRun == null || BenchmarkRunReportDocumentService.IsInProgress(batteryRun.ReportDocumentsStatus)) return;

        batteryRun.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.NotRequested;
        batteryRun.ReportDocumentsMessage = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Clears what the battery run's documents leave behind when the battery run is deleted: cancels
    /// its job and waits up to <see cref="DeleteWaitTimeout"/> for it to stop, deletes every
    /// battery-completion document with subject <c>battery:&lt;id&gt;</c> together with its charts,
    /// and forgets the job. Returns the number of documents deleted. Safe to call before or after the
    /// battery run row is removed, and when it has no documents.
    /// </summary>
    public async Task<int> SettleAfterDeleteAsync(long batteryRunId, CancellationToken ct = default)
    {
        TryCancel(batteryRunId);

        Task? stopping;
        lock (_lock)
        {
            stopping = _active.TryGetValue(batteryRunId, out var done) ? done.Task : null;
        }
        if (stopping != null)
        {
            try
            {
                await stopping.WaitAsync(DeleteWaitTimeout, ct);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("The battery-completion job of battery run {BatteryRunId} did not stop before its documents were deleted.", batteryRunId);
            }
        }

        int deleted = 0;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            string subjectKey = SubjectKeyOf(batteryRunId);
            var documentIds = await db.BenchmarkReportDocuments
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(d => d.SubjectKey == subjectKey && d.Origin == BenchmarkReportDocumentOrigin.BatteryCompletion)
                .Select(d => d.Id)
                .ToListAsync(ct);

            var renderService = scope.ServiceProvider.GetService<BenchmarkReportRenderService>();
            foreach (long documentId in documentIds)
            {
                bool removed = renderService != null
                    ? await renderService.DeleteAsync(documentId, ct)
                    : await BenchmarkReportRenderService.DeleteDocumentAsync(db, documentId, ct);
                if (removed) deleted++;
            }
        }

        lock (_lock)
        {
            if (_jobs.TryGetValue(batteryRunId, out var state) && state.Phase == JobPhase.Finished)
            {
                _jobs.Remove(batteryRunId);
            }
        }
        return deleted;
    }

    private async Task ScheduleIfDueCoreAsync(long batteryRunId)
    {
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var batteryRun = await db.BenchmarkBatteryRuns
                    .AsNoTracking()
                    .IgnoreAutoIncludes()
                    .FirstOrDefaultAsync(r => r.Id == batteryRunId);

                if (batteryRun == null
                    || batteryRun.ReportWriterModelConfigurationId == null
                    || !BenchmarkBatteryModelComparison.IsFinished(batteryRun.Status)
                    || BenchmarkRunReportDocumentService.IsInProgress(batteryRun.ReportDocumentsStatus))
                {
                    return;
                }

                var source = await LoadSourceAsync(db, batteryRunId, CancellationToken.None);
                if (source == null || SubjectRefusal(source) != null) return;

                string subjectKey = SubjectKeyOf(batteryRunId);
                bool anyWritten = await db.BenchmarkReportDocuments
                    .IgnoreAutoIncludes()
                    .AnyAsync(d => d.SubjectKey == subjectKey && d.Origin == BenchmarkReportDocumentOrigin.BatteryCompletion);
                if (anyWritten) return;
            }

            if (!TryClaim(batteryRunId, audiences: null)) return;
            await RunClaimedAsync(batteryRunId, userId: null, audiences: null, sameProviderAcknowledged: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduling the battery-completion documents of battery run {BatteryRunId} failed.", batteryRunId);
        }
    }

    /// <summary>
    /// The job itself. The caller holds the claim; this releases it. A null
    /// <paramref name="sameProviderAcknowledged"/> is the automatic job's: the battery start already asked.
    /// </summary>
    private async Task RunClaimedAsync(
        long batteryRunId, string? userId, IReadOnlyCollection<BenchmarkReportAudience>? audiences, bool? sameProviderAcknowledged)
    {
        BatteryReportJobState? state = null;
        BenchmarkReportPackJob? job = null;
        try
        {
            state = await CreateJobAsync(batteryRunId, userId, audiences, sameProviderAcknowledged);
            if (state == null) return;
            job = state.Job;
            Register(state);

            await SetStatusAsync(batteryRunId, BenchmarkRunReportDocumentsStatus.Pending, null);
            await _jobManager.WaitForSlotAsync(job, job.Cts.Token);
            SetPhase(state, JobPhase.Preparing);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var batteryRun = await db.BenchmarkBatteryRuns.IgnoreAutoIncludes().FirstOrDefaultAsync(r => r.Id == batteryRunId);
            if (batteryRun == null) return;

            var complianceGuard = scope.ServiceProvider.GetRequiredService<BenchmarkComplianceGuard>();
            var (canSpend, denialReason) = await complianceGuard.CanSpendAsync(db);
            if (!canSpend)
            {
                SetOn(batteryRun, BenchmarkRunReportDocumentsStatus.Skipped, denialReason ?? "The benchmark spend guard refused the reports.");
                await db.SaveChangesAsync();
                return;
            }

            var writer = batteryRun.ReportWriterModelConfigurationId is long writerId
                ? await db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == writerId)
                : null;
            if (writer == null || !writer.IsEnabled || string.IsNullOrWhiteSpace(writer.EncryptedApiKey))
            {
                SetOn(batteryRun, BenchmarkRunReportDocumentsStatus.Failed, BenchmarkRunReportDocumentService.WriterUnavailableMessage);
                await db.SaveChangesAsync();
                return;
            }

            var snapshot = await SystemAiConfigurationSnapshotStore.CaptureAndSaveAsync(db, writer, CancellationToken.None);
            job.WriterConfigId = writer.Id;
            job.WriterDisplayName = writer.DisplayName ?? writer.ModelId;
            job.WriterSnapshotId = snapshot.Id;
            job.Request.WriterModelConfigurationId = writer.Id;

            job.Cts.Token.ThrowIfCancellationRequested();
            SetPhase(state, JobPhase.Writing);

            SetOn(batteryRun, BenchmarkRunReportDocumentsStatus.Writing, null);
            await db.SaveChangesAsync();
            job.AddLog($"Writing the battery-completion documents of battery run {batteryRunId.ToString(CultureInfo.InvariantCulture)} with {job.WriterDisplayName}.");

            var reportWriter = scope.ServiceProvider.GetRequiredService<IBenchmarkRunReportWriter>();
            await reportWriter.WriteBatteryCompletionDocumentsAsync(batteryRunId, job, job.Cts.Token);

            var (status, message) = BenchmarkRunReportDocumentService.OutcomeOf(job);
            if (status == BenchmarkRunReportDocumentsStatus.Canceled) job.AddLog(message!, "warning");
            await SetStatusAsync(batteryRunId, status, message);
        }
        catch (OperationCanceledException) when (job != null && job.Cts.IsCancellationRequested)
        {
            bool writing = PhaseOf(state!) == JobPhase.Writing;
            foreach (var d in job.Documents.ToList().Where(d => d.Status is BenchmarkReportPackDocumentStatus.Pending
                         or BenchmarkReportPackDocumentStatus.Writing or BenchmarkReportPackDocumentStatus.Repairing))
            {
                job.SetDocumentStatus(d.Audience, BenchmarkReportPackDocumentStatus.Canceled);
            }
            job.SetStatus(BenchmarkReportPackJobStatus.Canceled);

            string message = writing
                ? BenchmarkRunReportDocumentService.OutcomeOf(job).Message!
                : BenchmarkRunReportDocumentService.CanceledBeforeWritingMessage;
            job.AddLog(message, "warning");
            await TrySetStatusAsync(batteryRunId, BenchmarkRunReportDocumentsStatus.Canceled, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The battery-completion documents of battery run {BatteryRunId} failed.", batteryRunId);
            await TrySetStatusAsync(batteryRunId, BenchmarkRunReportDocumentsStatus.Failed,
                "The reports could not be written: " + ExceptionDetails.DescribeShort(ex));
        }
        finally
        {
            if (job != null && job.Status == BenchmarkReportPackJobStatus.Running)
            {
                job.SetStatus(BenchmarkReportPackJobStatus.Failed);
            }
            if (state != null) SetPhase(state, JobPhase.Finished);
            Release(batteryRunId);
        }
    }

    /// <summary>
    /// The job for the requested documents the battery run is missing, labeled <c>Battery run #N</c>,
    /// with the writer identity the job view shows. Null when the battery run is gone, has none of the
    /// requested documents missing, or names an unacknowledged writer of the candidate's provider;
    /// each settles the battery run's status.
    /// </summary>
    private async Task<BatteryReportJobState?> CreateJobAsync(
        long batteryRunId, string? userId, IReadOnlyCollection<BenchmarkReportAudience>? audiences, bool? sameProviderAcknowledged)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var batteryRun = await db.BenchmarkBatteryRuns.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(r => r.Id == batteryRunId);
        if (batteryRun == null) return null;

        var missing = await MissingAudiencesAsync(db, batteryRunId, CancellationToken.None);
        var toWrite = audiences == null || audiences.Count == 0
            ? missing
            : missing.Where(audiences.Contains).ToList();
        if (toWrite.Count == 0)
        {
            await SetStatusAsync(batteryRunId,
                missing.Count == 0 ? BenchmarkRunReportDocumentsStatus.Completed : BenchmarkRunReportDocumentsStatus.NotRequested, null);
            return null;
        }

        var writer = batteryRun.ReportWriterModelConfigurationId is long writerId
            ? await db.SystemAiApiConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == writerId)
            : null;
        var source = await LoadSourceAsync(db, batteryRunId, CancellationToken.None);
        var candidate = source != null
            ? CandidateIdentity(source)
            : new SystemAiApiConfiguration { Provider = string.Empty, ModelId = string.Empty, DisplayName = string.Empty };
        var complianceGuard = scope.ServiceProvider.GetRequiredService<BenchmarkComplianceGuard>();
        bool sameProvider = writer != null && complianceGuard.IsSameProvider(writer, candidate);
        if (sameProvider && sameProviderAcknowledged == false)
        {
            await SetStatusAsync(batteryRunId, BenchmarkRunReportDocumentsStatus.Failed,
                BenchmarkRunReportDocumentService.WriterWarning(writer, candidate, complianceGuard));
            return null;
        }

        long writerConfigId = batteryRun.ReportWriterModelConfigurationId ?? 0;
        var job = new BenchmarkReportPackJob
        {
            SubjectKey = SubjectKeyOf(batteryRunId),
            SubjectLabel = BenchmarkReportPackPreparation.BatteryJobLabel(batteryRunId),
            SuiteId = null,
            SuiteName = batteryRun.BatteryName ?? string.Empty,
            WriterConfigId = writerConfigId,
            WriterDisplayName = writer?.DisplayName ?? string.Empty,
            // Only an acknowledged writer of the candidate's provider reaches this point.
            SameProviderAcknowledged = sameProvider,
            Request = BenchmarkReportPackPreparation.BatteryRequest(batteryRunId, toWrite, writerConfigId),
            // Usage rows need a user: an automatic job is charged to the user who started the battery run.
            StartedByUserId = string.IsNullOrEmpty(userId) ? batteryRun.StartedByUserId : userId,
            Cts = new CancellationTokenSource(),
            Documents = toWrite.Select(a => new BenchmarkReportPackDocumentProgress { Audience = a }).ToList()
        };

        return new BatteryReportJobState
        {
            Job = job,
            BatteryRunId = batteryRunId,
            Audiences = toWrite.ToList(),
            WriterConfigId = writerConfigId,
            WriterDisplayName = writer == null ? string.Empty : writer.DisplayName ?? writer.ModelId,
            WriterProvider = writer?.Provider ?? string.Empty,
            WriterModelId = writer?.ModelId ?? string.Empty,
            WriterThinkingLevel = writer?.ThinkingLevel
        };
    }

    /// <summary>
    /// The running job that holds the slot, as a queued job's view names it: <c>Battery run #N</c> for
    /// another battery run's job, <c>Run #N: …</c> for a run's run-completion job, and
    /// <c>Report Pack: …</c> otherwise.
    /// </summary>
    private string BlockingLabel(BenchmarkReportPackJob running)
    {
        long? runningBatteryRunId = null;
        lock (_lock)
        {
            foreach (var state in _jobs.Values)
            {
                if (ReferenceEquals(state.Job, running))
                {
                    runningBatteryRunId = state.BatteryRunId;
                    break;
                }
            }
        }

        if (runningBatteryRunId is long batteryRunId) return BenchmarkReportPackPreparation.BatteryJobLabel(batteryRunId);

        var request = running.Request;
        if (request != null
            && request.RunIds?.Count == 1
            && (request.GroupIds?.Count ?? 0) == 0
            && (request.BatteryRunIds?.Count ?? 0) == 0
            && BenchmarkRunReportDocumentService.TryParseSubjectKey(running.SubjectKey, out long runId)
            && request.RunIds[0] == runId)
        {
            return $"Run #{runId.ToString(CultureInfo.InvariantCulture)}: {running.SubjectLabel}";
        }
        return "Report Pack: " + running.SubjectLabel;
    }

    /// <summary>Makes the job the battery run's current one, replacing its previous entry and its claim, and prunes old finished ones.</summary>
    private void Register(BatteryReportJobState state)
    {
        lock (_lock)
        {
            var now = UtcNow;
            Prune(now);
            state.Phase = JobPhase.Queued;
            state.QueuedAtUtc = now;
            _jobs[state.BatteryRunId] = state;
            _claims.Remove(state.BatteryRunId);
        }
        state.Job.AddLog("Queued for the report writer.");
    }

    private void SetPhase(BatteryReportJobState state, JobPhase phase)
    {
        lock (_lock)
        {
            if (state.Phase == JobPhase.Finished) return;
            state.Phase = phase;
            if (phase == JobPhase.Preparing) state.SlotAcquiredAtUtc = UtcNow;
            if (phase == JobPhase.Finished) state.FinishedAtUtc = UtcNow;
        }
    }

    private JobPhase PhaseOf(BatteryReportJobState state)
    {
        lock (_lock)
        {
            return state.Phase;
        }
    }

    /// <summary>Drops finished entries older than the retention. The caller holds the lock.</summary>
    private void Prune(DateTime nowUtc)
    {
        var expired = _jobs
            .Where(e => e.Value.Phase == JobPhase.Finished && e.Value.FinishedAtUtc is DateTime finished
                && nowUtc - finished >= BenchmarkRunReportDocumentService.FinishedJobRetention)
            .Select(e => e.Key)
            .ToList();
        foreach (long batteryRunId in expired)
        {
            _jobs.Remove(batteryRunId);
        }
    }

    private static void SetOn(BenchmarkBatteryRun batteryRun, BenchmarkRunReportDocumentsStatus status, string? message)
    {
        batteryRun.ReportDocumentsStatus = status;
        batteryRun.ReportDocumentsMessage = Truncate(message);
    }

    private async Task SetStatusAsync(long batteryRunId, BenchmarkRunReportDocumentsStatus status, string? message)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var batteryRun = await db.BenchmarkBatteryRuns.IgnoreAutoIncludes().FirstOrDefaultAsync(r => r.Id == batteryRunId);
        if (batteryRun == null) return;

        SetOn(batteryRun, status, message);
        await db.SaveChangesAsync();
    }

    private async Task TrySetStatusAsync(long batteryRunId, BenchmarkRunReportDocumentsStatus status, string? message)
    {
        try
        {
            await SetStatusAsync(batteryRunId, status, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recording the battery-completion documents status of battery run {BatteryRunId} failed.", batteryRunId);
        }
    }

    /// <summary>
    /// Claims the battery run for one job and records the claim the job view shows until the job is
    /// registered. The battery run's finished previous job leaves the view at once. Null or empty
    /// <paramref name="audiences"/> requests every missing one.
    /// </summary>
    private bool TryClaim(long batteryRunId, IReadOnlyCollection<BenchmarkReportAudience>? audiences)
    {
        lock (_lock)
        {
            if (_active.ContainsKey(batteryRunId)) return false;

            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _active[batteryRunId] = done;
            _claims[batteryRunId] = new BatteryClaim(UtcNow, audiences?.ToList() ?? new List<BenchmarkReportAudience>());
            if (_jobs.TryGetValue(batteryRunId, out var previous) && previous.Phase == JobPhase.Finished)
            {
                _jobs.Remove(batteryRunId);
            }
            return true;
        }
    }

    private void Release(long batteryRunId)
    {
        TaskCompletionSource? done;
        lock (_lock)
        {
            _active.Remove(batteryRunId, out done);
            _claims.Remove(batteryRunId);
        }
        done?.TrySetResult();
    }

    private static string? Truncate(string? message)
        => message == null || message.Length <= MaxMessageLength ? message : message[..MaxMessageLength];
}
