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
/// Writes a run's three AI-written run-completion documents, the Executive Summary, the Report for
/// AI Researchers and Developers and the Internal Improvement Brief, once, after the run completes,
/// with the report writer the run names.
///
/// <para>Each document is an immutable <see cref="BenchmarkReportDocument"/> row with
/// <see cref="BenchmarkReportDocumentOrigin.RunCompletion"/>, about the run alone, with no peers.
/// Downloads render the stored row and never reach this service. Nothing is rewritten: a later
/// re-synthesis only marks the documents as changed, and writing again means deleting them first.</para>
///
/// <para>A job queues for the report-pack slot behind a running job, checks the compliance guard and
/// the writer, then writes whichever of the requested documents the run does not have yet. One job has
/// one writer; two documents with two writers are two jobs. The run's
/// <see cref="BenchmarkRun.ReportDocumentsStatus"/> tracks it, and <see cref="TryGetJob(long)"/> shows
/// it while it runs and for <see cref="FinishedJobRetention"/> after, in memory only.</para>
///
/// <para>Singleton: it opens its own scope for every job and every status update.</para>
/// </summary>
public sealed class BenchmarkRunReportDocumentService
{
    public const string WriterUnavailableMessage = "The report writer configuration is no longer available.";
    public const string RestartMessage = "Overseer restarted before the reports were written.";
    public const string InvalidWriterMessage =
        "Report writer configuration is invalid, disabled, missing an API key, or not configured with the Benchmark role.";
    public const string ModelUnderTestMessage = "The model under test cannot write its own reports.";
    public const string CanceledBeforeWritingMessage = "Canceled before the writing began.";
    public const string CanceledWhileWritingPrefix = "Canceled while writing. ";

    /// <summary>The length <see cref="BenchmarkRun.ReportDocumentsMessage"/> holds.</summary>
    public const int MaxMessageLength = 1000;

    /// <summary>How long a finished job stays visible, unless the run's next job replaces it first.</summary>
    public static readonly TimeSpan FinishedJobRetention = TimeSpan.FromHours(6);

    /// <summary>The run-completion documents of every run, in the order they are written.</summary>
    public static readonly IReadOnlyList<BenchmarkReportAudience> Audiences = new[]
    {
        BenchmarkReportAudience.ExecutiveSummary,
        BenchmarkReportAudience.TechnicalReport,
        BenchmarkReportAudience.InternalBrief
    };

    /// <summary>What <see cref="TryCancel"/> did.</summary>
    public enum CancelOutcome
    {
        /// <summary>This process knows no job for the run.</summary>
        NotFound,

        /// <summary>The run's last job has finished.</summary>
        NotInProgress,

        /// <summary>The job was asked to stop; it settles the run as Canceled.</summary>
        Requested
    }

    private enum JobPhase { Queued, Preparing, Writing, Finished }

    /// <summary>One run's job, as the run report dialog shows it. Mutable fields change under <see cref="_lock"/>.</summary>
    private sealed class RunReportJobState
    {
        public required BenchmarkReportPackJob Job { get; init; }
        public required long RunId { get; init; }
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

    /// <summary>A claimed run whose job is not registered yet; empty <see cref="Audiences"/> requests every missing one.</summary>
    private sealed record RunClaim(DateTime ClaimedAtUtc, List<BenchmarkReportAudience> Audiences);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BenchmarkReportPackJobManager _jobManager;
    private readonly ILogger<BenchmarkRunReportDocumentService> _logger;
    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private readonly HashSet<long> _active = new();
    private readonly Dictionary<long, RunReportJobState> _jobs = new();
    private readonly Dictionary<long, RunClaim> _claims = new();

    public BenchmarkRunReportDocumentService(
        IServiceScopeFactory scopeFactory,
        BenchmarkReportPackJobManager jobManager,
        ILogger<BenchmarkRunReportDocumentService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _jobManager = jobManager;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The service's clock, which stamps the job view.</summary>
    public DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    /// <summary>The comparison entry key the run's own documents are stored under.</summary>
    public static string SubjectKeyOf(long runId) => "run:" + runId.ToString(CultureInfo.InvariantCulture);

    /// <summary>The run id of a <c>run:&lt;id&gt;</c> subject key; false for a group or anything else.</summary>
    public static bool TryParseSubjectKey(string? subjectKey, out long runId)
    {
        runId = 0;
        // NumberStyles.None admits digits only: no sign, whitespace or separator.
        return subjectKey != null
            && subjectKey.StartsWith("run:", StringComparison.Ordinal)
            && long.TryParse(subjectKey.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out runId);
    }

    /// <summary>A job is queued or writing for the run is Pending or Writing.</summary>
    public static bool IsInProgress(BenchmarkRunReportDocumentsStatus status)
        => status is BenchmarkRunReportDocumentsStatus.Pending or BenchmarkRunReportDocumentsStatus.Writing;

    /// <summary>The run completed with a final synthesis to write about.</summary>
    public static bool IsFinishedWithSynthesis(BenchmarkRun run)
        => (run.Status is BenchmarkRunStatus.Completed or BenchmarkRunStatus.CompletedWithErrors or BenchmarkRunStatus.CompletedWithLimits)
           && !string.IsNullOrWhiteSpace(run.AssessmentJson);

    /// <summary>
    /// Why the configuration cannot write the run's documents, or null when it can: an unusable
    /// configuration or the model under test. A model of the same provider may write after an
    /// acknowledgment (<see cref="WriterWarning"/>). The endpoint policy is the caller's to check.
    /// </summary>
    public static string? WriterRefusal(
        SystemAiApiConfiguration? writer, SystemAiApiConfiguration candidate, BenchmarkComplianceGuard complianceGuard)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(complianceGuard);

        if (writer == null || !writer.IsEnabled || string.IsNullOrWhiteSpace(writer.EncryptedApiKey) || (writer.ModelRole & 4) != 4)
        {
            return InvalidWriterMessage;
        }
        if (complianceGuard.IsSameModel(writer, candidate))
        {
            return ModelUnderTestMessage;
        }
        return null;
    }

    /// <summary>
    /// The warning an operator acknowledges before a writer of the candidate's provider writes the
    /// run's documents, or null when the providers differ or there is no writer.
    /// </summary>
    public static string? WriterWarning(
        SystemAiApiConfiguration? writer, SystemAiApiConfiguration candidate, BenchmarkComplianceGuard complianceGuard)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(complianceGuard);

        if (writer == null || !complianceGuard.IsSameProvider(writer, candidate))
        {
            return null;
        }
        string name = string.IsNullOrWhiteSpace(writer.DisplayName) ? writer.ModelId : writer.DisplayName;
        string provider = string.IsNullOrWhiteSpace(candidate.Provider) ? writer.Provider : candidate.Provider;
        return $"{name} is from {provider}, the provider of the model under test. "
            + "Its reports may describe that model more favorably than an independent writer would.";
    }

    /// <summary>
    /// The 409 body of an unacknowledged same-provider report writer: role <c>reportWriter</c>, with the
    /// writer's name in <see cref="SameProviderWarningDto.AssessorModelDisplayName"/>.
    /// </summary>
    public static SameProviderWarningDto WriterWarningDto(SystemAiApiConfiguration writer, SystemAiApiConfiguration candidate, string message)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(candidate);
        return new SameProviderWarningDto
        {
            Role = "reportWriter",
            SameProvider = true,
            Provider = string.IsNullOrWhiteSpace(candidate.Provider) ? writer.Provider : candidate.Provider,
            TestedModelDisplayName = candidate.DisplayName ?? string.Empty,
            AssessorModelDisplayName = string.IsNullOrWhiteSpace(writer.DisplayName) ? writer.ModelId : writer.DisplayName,
            Message = message
        };
    }

    /// <summary>A stand-in configuration carrying the recorded candidate's provider and model id, for the writer checks.</summary>
    public static SystemAiApiConfiguration CandidateIdentity(BenchmarkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new SystemAiApiConfiguration
        {
            Provider = run.TestedModelSnapshot?.Provider ?? string.Empty,
            ModelId = run.TestedModelSnapshot?.ModelId ?? string.Empty,
            DisplayName = run.TestedModelSnapshot.Label() ?? string.Empty
        };
    }

    /// <summary>The report-pack request a run's job writes, and its estimate prices: the run alone, as its own subject.</summary>
    public static BenchmarkReportPackRequest RunRequest(long runId, IEnumerable<BenchmarkReportAudience> audiences, long writerConfigId)
    {
        ArgumentNullException.ThrowIfNull(audiences);
        return new BenchmarkReportPackRequest
        {
            RunIds = new List<long> { runId },
            GroupIds = new List<long>(),
            SubjectKey = SubjectKeyOf(runId),
            Audiences = audiences.ToList(),
            WriterModelConfigurationId = writerConfigId
        };
    }

    /// <summary>The audiences of <see cref="Audiences"/> the run has no run-completion document for.</summary>
    public static async Task<List<BenchmarkReportAudience>> MissingAudiencesAsync(ApplicationDbContext db, long runId, CancellationToken ct)
    {
        string subjectKey = SubjectKeyOf(runId);
        var existing = await db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(d => d.SubjectKey == subjectKey && d.Origin == BenchmarkReportDocumentOrigin.RunCompletion)
            .Select(d => d.Audience)
            .Distinct()
            .ToListAsync(ct);
        return Audiences.Where(a => !existing.Contains(a)).ToList();
    }

    /// <summary>True while this process holds a job for the run, queued or writing.</summary>
    public bool IsActive(long runId)
    {
        lock (_lock)
        {
            return _active.Contains(runId);
        }
    }

    /// <summary>
    /// Writes the run's documents in the background when they are due: the run completed, names a
    /// report writer and has a final synthesis, has no run-completion document yet, and no job for it
    /// is Pending or Writing. Returns at once; the task ends when the job does, or at once when
    /// nothing is due. A writer of the candidate's provider was acknowledged at launch, so its
    /// documents record the acknowledgment.
    /// </summary>
    public Task ScheduleIfDue(long runId) => Task.Run(() => ScheduleIfDueCoreAsync(runId));

    /// <summary>
    /// Starts a job with the writer already recorded on the run, for the requested audiences the run
    /// is missing, in <see cref="Audiences"/> order; null or empty requests every missing one. A writer
    /// of the candidate's provider needs <paramref name="sameProviderAcknowledged"/>, or the job
    /// settles the run as Failed with the warning. False when this process already holds a job for
    /// the run.
    /// </summary>
    public bool TryStart(
        long runId,
        string? userId,
        IReadOnlyCollection<BenchmarkReportAudience>? audiences,
        bool sameProviderAcknowledged,
        out Task completion)
    {
        var requested = audiences?.ToList();
        if (!TryClaim(runId, requested))
        {
            completion = Task.CompletedTask;
            return false;
        }

        completion = Task.Run(() => RunClaimedAsync(runId, userId, requested, sameProviderAcknowledged));
        return true;
    }

    /// <summary>
    /// Asks the run's job to stop. A queued job leaves the queue; a job that is writing keeps every
    /// document already stored. Either way the run settles as <see cref="BenchmarkRunReportDocumentsStatus.Canceled"/>.
    /// </summary>
    public CancelOutcome TryCancel(long runId)
    {
        BenchmarkReportPackJob job;
        lock (_lock)
        {
            if (!_jobs.TryGetValue(runId, out var state)) return CancelOutcome.NotFound;
            if (state.Phase == JobPhase.Finished) return CancelOutcome.NotInProgress;

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
        return CancelOutcome.Requested;
    }

    /// <summary>The run's job as the service's clock sees it now; see <see cref="TryGetJob(long, DateTime)"/>.</summary>
    public BenchmarkRunReportJobDto? TryGetJob(long runId) => TryGetJob(runId, UtcNow);

    /// <summary>
    /// The run's current or last job, or null when this process knows none, or its finished job is
    /// older than <see cref="FinishedJobRetention"/>. A claimed run whose job is still being created
    /// shows as Queued since the claim, with the requested audiences and no writer yet. The persisted
    /// <see cref="BenchmarkRunReportJobDto.Status"/> and <see cref="BenchmarkRunReportJobDto.Message"/>
    /// are the caller's to fill from the run row.
    /// </summary>
    public BenchmarkRunReportJobDto? TryGetJob(long runId, DateTime nowUtc)
    {
        RunReportJobState state;
        JobPhase phase;
        DateTime queuedAt;
        DateTime? slotAcquiredAt, finishedAt, cancelRequestedAt;
        lock (_lock)
        {
            Prune(nowUtc);
            if (!_jobs.TryGetValue(runId, out var found))
            {
                return _claims.TryGetValue(runId, out var claim) ? StartingView(runId, claim, nowUtc) : null;
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
            RunId = runId,
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

    /// <summary>The view of a claimed run before its job is registered: Queued since the claim, with no writer and no progress.</summary>
    private static BenchmarkRunReportJobDto StartingView(long runId, RunClaim claim, DateTime nowUtc) => new()
    {
        RunId = runId,
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
    /// Settles runs a previous process left Pending or Writing: no job survives a restart, so each is
    /// marked Failed with <see cref="RestartMessage"/>, and Write Reports writes what is missing.
    /// </summary>
    public static async Task<int> SettleInterruptedAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var interrupted = await db.BenchmarkRuns
            .IgnoreAutoIncludes()
            .Where(r => r.ReportDocumentsStatus == BenchmarkRunReportDocumentsStatus.Pending
                || r.ReportDocumentsStatus == BenchmarkRunReportDocumentsStatus.Writing)
            .ToListAsync(ct);

        foreach (var run in interrupted)
        {
            run.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Failed;
            run.ReportDocumentsMessage = RestartMessage;
        }

        if (interrupted.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        return interrupted.Count;
    }

    /// <summary>
    /// After one of the run's documents is deleted: a run whose documents are not being written returns
    /// to <see cref="BenchmarkRunReportDocumentsStatus.NotRequested"/> with no message, so its status no
    /// longer describes a document that is gone. The run keeps its report writer.
    /// </summary>
    public static async Task SettleAfterDeleteAsync(ApplicationDbContext db, long runId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var run = await db.BenchmarkRuns.IgnoreAutoIncludes().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run == null || IsInProgress(run.ReportDocumentsStatus)) return;

        run.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.NotRequested;
        run.ReportDocumentsMessage = null;
        await db.SaveChangesAsync(ct);
    }

    private async Task ScheduleIfDueCoreAsync(long runId)
    {
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var run = await db.BenchmarkRuns
                    .AsNoTracking()
                    .IgnoreAutoIncludes()
                    .FirstOrDefaultAsync(r => r.Id == runId);

                if (run == null
                    || run.Status != BenchmarkRunStatus.Completed
                    || run.ReportWriterModelConfigurationId == null
                    || string.IsNullOrWhiteSpace(run.AssessmentJson)
                    || IsInProgress(run.ReportDocumentsStatus))
                {
                    return;
                }

                string subjectKey = SubjectKeyOf(runId);
                bool anyWritten = await db.BenchmarkReportDocuments
                    .IgnoreAutoIncludes()
                    .AnyAsync(d => d.SubjectKey == subjectKey && d.Origin == BenchmarkReportDocumentOrigin.RunCompletion);
                if (anyWritten) return;
            }

            if (!TryClaim(runId, audiences: null)) return;
            await RunClaimedAsync(runId, userId: null, audiences: null, sameProviderAcknowledged: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduling the run-completion documents of run {RunId} failed.", runId);
        }
    }

    /// <summary>
    /// The job itself. The caller holds the claim; this releases it. A null
    /// <paramref name="sameProviderAcknowledged"/> is the automatic job's: its launch already asked.
    /// </summary>
    private async Task RunClaimedAsync(
        long runId, string? userId, IReadOnlyCollection<BenchmarkReportAudience>? audiences, bool? sameProviderAcknowledged)
    {
        RunReportJobState? state = null;
        BenchmarkReportPackJob? job = null;
        try
        {
            state = await CreateJobAsync(runId, userId, audiences, sameProviderAcknowledged);
            if (state == null) return;
            job = state.Job;
            Register(state);

            await SetStatusAsync(runId, BenchmarkRunReportDocumentsStatus.Pending, null);
            await _jobManager.WaitForSlotAsync(job, job.Cts.Token);
            SetPhase(state, JobPhase.Preparing);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var run = await db.BenchmarkRuns.FirstOrDefaultAsync(r => r.Id == runId);
            if (run == null) return;

            var complianceGuard = scope.ServiceProvider.GetRequiredService<BenchmarkComplianceGuard>();
            var (canSpend, denialReason) = await complianceGuard.CanSpendAsync(db);
            if (!canSpend)
            {
                SetOn(run, BenchmarkRunReportDocumentsStatus.Skipped, denialReason ?? "The benchmark spend guard refused the reports.");
                await db.SaveChangesAsync();
                return;
            }

            var writer = run.ReportWriterModelConfigurationId is long writerId
                ? await db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == writerId)
                : null;
            if (writer == null || !writer.IsEnabled || string.IsNullOrWhiteSpace(writer.EncryptedApiKey))
            {
                SetOn(run, BenchmarkRunReportDocumentsStatus.Failed, WriterUnavailableMessage);
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

            SetOn(run, BenchmarkRunReportDocumentsStatus.Writing, null);
            await db.SaveChangesAsync();
            job.AddLog($"Writing the run-completion documents of run {runId.ToString(CultureInfo.InvariantCulture)} with {job.WriterDisplayName}.");

            var reportWriter = scope.ServiceProvider.GetRequiredService<IBenchmarkRunReportWriter>();
            await reportWriter.WriteRunCompletionDocumentsAsync(job, job.Cts.Token);

            var (status, message) = OutcomeOf(job);
            if (status == BenchmarkRunReportDocumentsStatus.Canceled) job.AddLog(message!, "warning");
            await SetStatusAsync(runId, status, message);
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

            string message = writing ? OutcomeOf(job).Message! : CanceledBeforeWritingMessage;
            job.AddLog(message, "warning");
            await TrySetStatusAsync(runId, BenchmarkRunReportDocumentsStatus.Canceled, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The run-completion documents of run {RunId} failed.", runId);
            await TrySetStatusAsync(runId, BenchmarkRunReportDocumentsStatus.Failed,
                "The reports could not be written: " + ExceptionDetails.DescribeShort(ex));
        }
        finally
        {
            if (job != null && job.Status == BenchmarkReportPackJobStatus.Running)
            {
                job.SetStatus(BenchmarkReportPackJobStatus.Failed);
            }
            if (state != null) SetPhase(state, JobPhase.Finished);
            Release(runId);
        }
    }

    /// <summary>
    /// The job for the requested documents the run is missing, labeled as the Report Pack dialog shows
    /// a job, with the writer identity the job view shows. Null when the run is gone, has none of the
    /// requested documents missing, or names an unacknowledged writer of the candidate's provider;
    /// each settles the run's status.
    /// </summary>
    private async Task<RunReportJobState?> CreateJobAsync(
        long runId, string? userId, IReadOnlyCollection<BenchmarkReportAudience>? audiences, bool? sameProviderAcknowledged)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var run = await db.BenchmarkRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId);
        if (run == null) return null;

        var missing = await MissingAudiencesAsync(db, runId, CancellationToken.None);
        var toWrite = audiences == null || audiences.Count == 0
            ? missing
            : missing.Where(audiences.Contains).ToList();
        if (toWrite.Count == 0)
        {
            await SetStatusAsync(runId,
                missing.Count == 0 ? BenchmarkRunReportDocumentsStatus.Completed : BenchmarkRunReportDocumentsStatus.NotRequested, null);
            return null;
        }

        var writer = run.ReportWriterModelConfigurationId is long writerId
            ? await db.SystemAiApiConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == writerId)
            : null;
        var candidate = CandidateIdentity(run);
        var complianceGuard = scope.ServiceProvider.GetRequiredService<BenchmarkComplianceGuard>();
        bool sameProvider = writer != null && complianceGuard.IsSameProvider(writer, candidate);
        if (sameProvider && sameProviderAcknowledged == false)
        {
            await SetStatusAsync(runId, BenchmarkRunReportDocumentsStatus.Failed, WriterWarning(writer, candidate, complianceGuard));
            return null;
        }

        string subjectKey = SubjectKeyOf(runId);
        long writerConfigId = run.ReportWriterModelConfigurationId ?? 0;
        var job = new BenchmarkReportPackJob
        {
            SubjectKey = subjectKey,
            SubjectLabel = run.TestedModelSnapshot.Label() ?? subjectKey,
            SuiteId = run.BenchmarkSuiteIdUsed ?? run.BenchmarkSuiteId,
            SuiteName = run.SuiteName ?? string.Empty,
            WriterConfigId = writerConfigId,
            WriterDisplayName = writer?.DisplayName ?? string.Empty,
            // Only an acknowledged writer of the candidate's provider reaches this point.
            SameProviderAcknowledged = sameProvider,
            Request = RunRequest(runId, toWrite, writerConfigId),
            // Usage rows need a user: an automatic job is charged to the user who launched the run.
            StartedByUserId = string.IsNullOrEmpty(userId) ? run.StartedByUserId : userId,
            Cts = new CancellationTokenSource(),
            Documents = toWrite.Select(a => new BenchmarkReportPackDocumentProgress { Audience = a }).ToList()
        };

        return new RunReportJobState
        {
            Job = job,
            RunId = runId,
            Audiences = toWrite.ToList(),
            WriterConfigId = writerConfigId,
            WriterDisplayName = writer == null ? string.Empty : writer.DisplayName ?? writer.ModelId,
            WriterProvider = writer?.Provider ?? string.Empty,
            WriterModelId = writer?.ModelId ?? string.Empty,
            WriterThinkingLevel = writer?.ThinkingLevel
        };
    }

    /// <summary>
    /// Completed when every document of the job was stored, with warnings when one carries them;
    /// Canceled when an administrator stopped it, naming what was kept; Failed with the first
    /// document's error otherwise. A stored document is kept either way.
    /// </summary>
    internal static (BenchmarkRunReportDocumentsStatus Status, string? Message) OutcomeOf(BenchmarkReportPackJob job)
    {
        var documents = job.ToDto().Documents;
        bool IsStored(BenchmarkReportPackDocumentProgressDto d)
            => d.Status == nameof(BenchmarkReportPackDocumentStatus.Completed)
               || d.Status == nameof(BenchmarkReportPackDocumentStatus.CompletedWithWarnings);

        if (job.Status == BenchmarkReportPackJobStatus.Canceled)
        {
            var written = documents.Where(IsStored).Select(d => BenchmarkReportRenderService.AudienceName(d.Audience)).ToList();
            string kept = written.Count switch
            {
                0 => "Nothing was written.",
                1 => $"The {written[0]} was written and is kept.",
                _ => $"The {string.Join(", the ", written.Take(written.Count - 1))} and the {written[^1]} were written and are kept."
            };
            return (BenchmarkRunReportDocumentsStatus.Canceled, CanceledWhileWritingPrefix + kept);
        }

        var failed = documents.Where(d => !IsStored(d)).ToList();
        if (failed.Count > 0)
        {
            var first = failed[0];
            string name = BenchmarkReportRenderService.AudienceName(first.Audience);
            string reason = !string.IsNullOrWhiteSpace(first.ErrorMessage)
                ? first.ErrorMessage!
                : first.Status == nameof(BenchmarkReportPackDocumentStatus.Canceled)
                    ? "the writing was canceled."
                    : "it could not be written.";
            return (BenchmarkRunReportDocumentsStatus.Failed, name + ": " + reason);
        }

        return documents.Any(d => d.Status == nameof(BenchmarkReportPackDocumentStatus.CompletedWithWarnings))
            ? (BenchmarkRunReportDocumentsStatus.CompletedWithWarnings, null)
            : (BenchmarkRunReportDocumentsStatus.Completed, null);
    }

    /// <summary>The running job that holds the slot, as a queued job's view names it.</summary>
    private string BlockingLabel(BenchmarkReportPackJob running)
    {
        long? runningRunId = null;
        lock (_lock)
        {
            foreach (var state in _jobs.Values)
            {
                if (ReferenceEquals(state.Job, running))
                {
                    runningRunId = state.RunId;
                    break;
                }
            }
        }

        return runningRunId is long id
            ? $"Run #{id.ToString(CultureInfo.InvariantCulture)}: {running.SubjectLabel}"
            : "Report Pack: " + running.SubjectLabel;
    }

    /// <summary>Makes the job the run's current one, replacing its previous entry and its claim, and prunes old finished ones.</summary>
    private void Register(RunReportJobState state)
    {
        lock (_lock)
        {
            var now = UtcNow;
            Prune(now);
            state.Phase = JobPhase.Queued;
            state.QueuedAtUtc = now;
            _jobs[state.RunId] = state;
            _claims.Remove(state.RunId);
        }
        state.Job.AddLog("Queued for the report writer.");
    }

    private void SetPhase(RunReportJobState state, JobPhase phase)
    {
        lock (_lock)
        {
            if (state.Phase == JobPhase.Finished) return;
            state.Phase = phase;
            if (phase == JobPhase.Preparing) state.SlotAcquiredAtUtc = UtcNow;
            if (phase == JobPhase.Finished) state.FinishedAtUtc = UtcNow;
        }
    }

    private JobPhase PhaseOf(RunReportJobState state)
    {
        lock (_lock)
        {
            return state.Phase;
        }
    }

    /// <summary>Drops finished entries older than <see cref="FinishedJobRetention"/>. The caller holds the lock.</summary>
    private void Prune(DateTime nowUtc)
    {
        var expired = _jobs
            .Where(e => e.Value.Phase == JobPhase.Finished && e.Value.FinishedAtUtc is DateTime finished && nowUtc - finished >= FinishedJobRetention)
            .Select(e => e.Key)
            .ToList();
        foreach (long runId in expired)
        {
            _jobs.Remove(runId);
        }
    }

    private static void SetOn(BenchmarkRun run, BenchmarkRunReportDocumentsStatus status, string? message)
    {
        run.ReportDocumentsStatus = status;
        run.ReportDocumentsMessage = Truncate(message);
    }

    private async Task SetStatusAsync(long runId, BenchmarkRunReportDocumentsStatus status, string? message)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var run = await db.BenchmarkRuns.IgnoreAutoIncludes().FirstOrDefaultAsync(r => r.Id == runId);
        if (run == null) return;

        SetOn(run, status, message);
        await db.SaveChangesAsync();
    }

    private async Task TrySetStatusAsync(long runId, BenchmarkRunReportDocumentsStatus status, string? message)
    {
        try
        {
            await SetStatusAsync(runId, status, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recording the run-completion documents status of run {RunId} failed.", runId);
        }
    }

    /// <summary>
    /// Claims the run for one job and records the claim the job view shows until the job is
    /// registered. The run's finished previous job leaves the view at once. Null or empty
    /// <paramref name="audiences"/> requests every missing one.
    /// </summary>
    private bool TryClaim(long runId, IReadOnlyCollection<BenchmarkReportAudience>? audiences)
    {
        lock (_lock)
        {
            if (!_active.Add(runId)) return false;

            _claims[runId] = new RunClaim(UtcNow, audiences?.ToList() ?? new List<BenchmarkReportAudience>());
            if (_jobs.TryGetValue(runId, out var previous) && previous.Phase == JobPhase.Finished)
            {
                _jobs.Remove(runId);
            }
            return true;
        }
    }

    private void Release(long runId)
    {
        lock (_lock)
        {
            _active.Remove(runId);
            _claims.Remove(runId);
        }
    }

    private static string? Truncate(string? message)
        => message == null || message.Length <= MaxMessageLength ? message : message[..MaxMessageLength];
}
