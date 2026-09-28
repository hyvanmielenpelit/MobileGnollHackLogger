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
/// Writes a run's two AI-written run-completion documents, the Executive Summary and the Report for
/// AI Researchers and Developers, once, after the run completes, with the report writer the run names.
///
/// <para>Each document is an immutable <see cref="BenchmarkReportDocument"/> row with
/// <see cref="BenchmarkReportDocumentOrigin.RunCompletion"/>, about the run alone, with no peers.
/// Downloads render the stored row and never reach this service. Nothing is rewritten: a later
/// re-synthesis only marks the documents as changed, and writing again means deleting them first.</para>
///
/// <para>A job queues for the report-pack slot behind a running job, checks the compliance guard and
/// the writer, then writes whichever of the two documents the run does not have yet. The run's
/// <see cref="BenchmarkRun.ReportDocumentsStatus"/> tracks it.</para>
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
    public const string SameProviderMessage = "Choose a report writer from another provider than the model under test.";

    /// <summary>The length <see cref="BenchmarkRun.ReportDocumentsMessage"/> holds.</summary>
    public const int MaxMessageLength = 1000;

    /// <summary>The run-completion documents of every run, in the order they are written.</summary>
    public static readonly IReadOnlyList<BenchmarkReportAudience> Audiences = new[]
    {
        BenchmarkReportAudience.ExecutiveSummary,
        BenchmarkReportAudience.TechnicalReport
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BenchmarkReportPackJobManager _jobManager;
    private readonly ILogger<BenchmarkRunReportDocumentService> _logger;
    private readonly object _lock = new();
    private readonly HashSet<long> _active = new();

    public BenchmarkRunReportDocumentService(
        IServiceScopeFactory scopeFactory,
        BenchmarkReportPackJobManager jobManager,
        ILogger<BenchmarkRunReportDocumentService> logger)
    {
        _scopeFactory = scopeFactory;
        _jobManager = jobManager;
        _logger = logger;
    }

    /// <summary>The comparison entry key the run's own documents are stored under.</summary>
    public static string SubjectKeyOf(long runId) => "run:" + runId.ToString(CultureInfo.InvariantCulture);

    /// <summary>A job is queued or writing for the run is Pending or Writing.</summary>
    public static bool IsInProgress(BenchmarkRunReportDocumentsStatus status)
        => status is BenchmarkRunReportDocumentsStatus.Pending or BenchmarkRunReportDocumentsStatus.Writing;

    /// <summary>The run completed with a final synthesis to write about.</summary>
    public static bool IsFinishedWithSynthesis(BenchmarkRun run)
        => (run.Status is BenchmarkRunStatus.Completed or BenchmarkRunStatus.CompletedWithErrors or BenchmarkRunStatus.CompletedWithLimits)
           && !string.IsNullOrWhiteSpace(run.AssessmentJson);

    /// <summary>
    /// Why the configuration cannot write the run's documents, or null when it can: an unusable
    /// configuration, the model under test itself, or a model of the same provider. The endpoint policy
    /// is the caller's to check.
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
        if (complianceGuard.IsSameProvider(writer, candidate))
        {
            return SameProviderMessage;
        }
        return null;
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
    /// nothing is due.
    /// </summary>
    public Task ScheduleIfDue(long runId) => Task.Run(() => ScheduleIfDueCoreAsync(runId));

    /// <summary>
    /// Starts a job for whatever run-completion documents the run is missing, with the writer already
    /// recorded on the run. False when this process already holds a job for the run.
    /// </summary>
    public bool TryStart(long runId, string? userId, out Task completion)
    {
        if (!TryClaim(runId))
        {
            completion = Task.CompletedTask;
            return false;
        }

        completion = Task.Run(() => RunClaimedAsync(runId, userId));
        return true;
    }

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

            if (!TryClaim(runId)) return;
            await RunClaimedAsync(runId, userId: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduling the run-completion documents of run {RunId} failed.", runId);
        }
    }

    /// <summary>The job itself. The caller holds the claim; this releases it.</summary>
    private async Task RunClaimedAsync(long runId, string? userId)
    {
        BenchmarkReportPackJob? job = null;
        try
        {
            await SetStatusAsync(runId, BenchmarkRunReportDocumentsStatus.Pending, null);

            job = await CreateJobAsync(runId, userId);
            if (job == null) return;

            await _jobManager.WaitForSlotAsync(job, CancellationToken.None);

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

            SetOn(run, BenchmarkRunReportDocumentsStatus.Writing, null);
            await db.SaveChangesAsync();
            job.AddLog($"Writing the run-completion documents of run {runId.ToString(CultureInfo.InvariantCulture)} with {job.WriterDisplayName}.");

            var reportWriter = scope.ServiceProvider.GetRequiredService<IBenchmarkRunReportWriter>();
            await reportWriter.WriteRunCompletionDocumentsAsync(job, job.Cts.Token);

            var (status, message) = OutcomeOf(job);
            await SetStatusAsync(runId, status, message);
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
            Release(runId);
        }
    }

    /// <summary>
    /// The job for the documents the run is missing, labeled as the Report Pack dialog shows a job; null
    /// when the run is gone or has every document, which settles its status.
    /// </summary>
    private async Task<BenchmarkReportPackJob?> CreateJobAsync(long runId, string? userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var run = await db.BenchmarkRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId);
        if (run == null) return null;

        var missing = await MissingAudiencesAsync(db, runId, CancellationToken.None);
        if (missing.Count == 0)
        {
            await SetStatusAsync(runId, BenchmarkRunReportDocumentsStatus.Completed, null);
            return null;
        }

        string? writerName = run.ReportWriterModelConfigurationId is long writerId
            ? await db.SystemAiApiConfigurations.Where(c => c.Id == writerId).Select(c => c.DisplayName).FirstOrDefaultAsync()
            : null;
        string subjectKey = SubjectKeyOf(runId);

        return new BenchmarkReportPackJob
        {
            SubjectKey = subjectKey,
            SubjectLabel = run.TestedModelSnapshot.Label() ?? subjectKey,
            SuiteId = run.BenchmarkSuiteIdUsed ?? run.BenchmarkSuiteId,
            SuiteName = run.SuiteName ?? string.Empty,
            WriterConfigId = run.ReportWriterModelConfigurationId ?? 0,
            WriterDisplayName = writerName ?? string.Empty,
            Request = new BenchmarkReportPackRequest
            {
                RunIds = new List<long> { runId },
                GroupIds = new List<long>(),
                SubjectKey = subjectKey,
                Audiences = missing,
                WriterModelConfigurationId = run.ReportWriterModelConfigurationId ?? 0
            },
            // Usage rows need a user: an automatic job is charged to the user who launched the run.
            StartedByUserId = string.IsNullOrEmpty(userId) ? run.StartedByUserId : userId,
            Cts = new CancellationTokenSource(),
            Documents = missing.Select(a => new BenchmarkReportPackDocumentProgress { Audience = a }).ToList()
        };
    }

    /// <summary>
    /// Completed when every document of the job was stored, with warnings when one carries them;
    /// Failed with the first document's error otherwise. A stored document is kept either way.
    /// </summary>
    internal static (BenchmarkRunReportDocumentsStatus Status, string? Message) OutcomeOf(BenchmarkReportPackJob job)
    {
        var documents = job.ToDto().Documents;
        var failed = documents
            .Where(d => d.Status != nameof(BenchmarkReportPackDocumentStatus.Completed)
                && d.Status != nameof(BenchmarkReportPackDocumentStatus.CompletedWithWarnings))
            .ToList();

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

    private bool TryClaim(long runId)
    {
        lock (_lock)
        {
            return _active.Add(runId);
        }
    }

    private void Release(long runId)
    {
        lock (_lock)
        {
            _active.Remove(runId);
        }
    }

    private static string? Truncate(string? message)
        => message == null || message.Length <= MaxMessageLength ? message : message[..MaxMessageLength];
}
