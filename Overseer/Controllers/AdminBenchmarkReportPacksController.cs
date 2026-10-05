namespace Overseer.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Services.Privacy;

/// <summary>
/// Report-pack generation: preview (no model call), start, progress and cancel; and for a finished
/// run's own run-completion documents, write on request, estimate, progress, cancel and delete. The
/// writer call runs in <see cref="BenchmarkReportPackService"/>, resolved per job from a fresh scope; stored documents
/// are served by <see cref="AdminBenchmarkReportDocumentsController"/>, which cannot reach a model.
/// </summary>
[Route("api/admin/benchmark")]
[Authorize(Policy = "AdminOnly")]
[ApiController]
public class AdminBenchmarkReportPacksController : ControllerBase
{
    /// <summary>Rough output sizes per document, for the preview's cost estimate only.</summary>
    private static readonly IReadOnlyDictionary<BenchmarkReportAudience, int> EstimatedOutputTokens = new Dictionary<BenchmarkReportAudience, int>
    {
        [BenchmarkReportAudience.ExecutiveSummary] = 2400,
        [BenchmarkReportAudience.TechnicalReport] = 7400,
        [BenchmarkReportAudience.InternalBrief] = 7000
    };

    public const string AllWrittenMessage = "This run already has every AI-written report. Delete one first to write it again.";
    public const string InvalidAudienceMessage =
        "Only the Executive Summary, the Report for AI Researchers and Developers and the Internal Improvement Brief are written for a run.";

    private readonly ApplicationDbContext _db;
    private readonly BenchmarkReportPackJobManager _jobManager;
    private readonly BenchmarkComplianceGuard _complianceGuard;
    private readonly BenchmarkModelComparisonService _comparisonService;
    private readonly ModelPricingService _pricingService;
    private readonly EndpointPolicy _endpointPolicy;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly BenchmarkRunReportDocumentService? _runReportDocuments;

    public AdminBenchmarkReportPacksController(
        ApplicationDbContext db,
        BenchmarkReportPackJobManager jobManager,
        BenchmarkComplianceGuard complianceGuard,
        BenchmarkModelComparisonService comparisonService,
        ModelPricingService pricingService,
        EndpointPolicy endpointPolicy,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        BenchmarkRunReportDocumentService? runReportDocuments = null)
    {
        _db = db;
        _jobManager = jobManager;
        _complianceGuard = complianceGuard;
        _comparisonService = comparisonService;
        _pricingService = pricingService;
        _endpointPolicy = endpointPolicy;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _runReportDocuments = runReportDocuments;
    }

    /// <summary>
    /// The subject, its peers, the documents already written for this comparison and subject, the
    /// estimated cost of each document, the same-provider warning and any refusal; a subject with no
    /// peer is refused with no estimate. Computes the fact sheet and the prompts; makes no model call.
    /// Battery results mixed with runs or groups are a 400; a request the client aborts is a 499.
    /// </summary>
    [HttpPost("report-packs/preview")]
    public async Task<IActionResult> Preview([FromBody] BenchmarkReportPackRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "A request body is required." });
        if (BenchmarkReportPackPreparation.MixesSources(request)) return BadRequest(new { error = BenchmarkBatteryModelComparison.MixedSourcesError });

        try
        {
            int excerptChars = BenchmarkReportPackPreparation.AnswerExcerptChars(_configuration);
            var (prep, refusal) = await BenchmarkReportPackPreparation.PrepareAsync(_db, _comparisonService, request, excerptChars, ct, _configuration);
            if (prep == null)
            {
                return Ok(new BenchmarkReportPackPreviewDto { SubjectKey = request.SubjectKey ?? string.Empty, Refusal = refusal });
            }

            var preview = new BenchmarkReportPackPreviewDto
            {
                SubjectKey = prep.Subject.Key,
                SubjectLabel = prep.Sheet.SubjectLabel,
                SubjectState = prep.Sheet.SubjectState,
                SuiteName = prep.Sheet.SuiteName,
                Peers = prep.Sheet.Peers.Select(p => new BenchmarkReportPackPeerDto
                {
                    Letter = p.Letter,
                    EntryKey = p.EntryKey,
                    Label = p.Label,
                    Provider = p.Provider,
                    State = p.State
                }).ToList()
            };

            if (preview.Peers.Count == 0)
            {
                preview.Refusal = BenchmarkReportPackPreparation.PeerlessReportRefusal;
                return Ok(preview);
            }
            preview.WrittenDocuments = await WrittenDocumentsAsync(prep.Subject.Key, request, ct);

            SystemAiApiConfiguration? writer = null;
            if (request.WriterModelConfigurationId > 0)
            {
                writer = await _db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == request.WriterModelConfigurationId, ct);
                preview.WriterDisplayName = writer?.DisplayName;
                preview.Refusal = WriterRefusal(writer, prep.Subject);
                if (preview.Refusal == null && _complianceGuard.IsSameProvider(writer!.Provider, prep.Subject.Provider))
                {
                    preview.SameProviderWarning = BenchmarkReportPackPreparation.SameProviderWarning(prep.Subject, writer);
                }
            }

            var audiences = (request.Audiences ?? new List<BenchmarkReportAudience>()).Distinct().OrderBy(a => a).ToList();
            preview.Estimates.AddRange(EstimateAudiences(prep, writer, audiences));
            preview.EstimatedTotalCostUsd = TotalCost(preview.Estimates);

            return Ok(preview);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Starts a job. Refusals, in order: battery results mixed with runs or groups (400); unknown or
    /// Excluded subject (400); a subject with no peer (400); unusable writer (400); the writer is the
    /// subject's model (400); no document (400); a requested document already written for this
    /// comparison and subject (409); spend cap (429); same provider, unacknowledged (409 with the
    /// warning); a job already running (409 with its state).
    /// </summary>
    [HttpPost("report-packs")]
    public async Task<IActionResult> Start([FromBody] BenchmarkReportPackRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "A request body is required." });
        if (BenchmarkReportPackPreparation.MixesSources(request)) return BadRequest(new { error = BenchmarkBatteryModelComparison.MixedSourcesError });

        var (comparison, subject, refusal) = await BenchmarkReportPackPreparation.CompareAsync(_comparisonService, request, ct);
        if (refusal != null) return BadRequest(new { error = refusal });
        if (!BenchmarkReportPackPreparation.HasPeers(comparison!, subject!))
        {
            return BadRequest(new { error = BenchmarkReportPackPreparation.PeerlessReportRefusal });
        }

        var writer = await _db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == request.WriterModelConfigurationId, ct);
        string? writerRefusal = WriterRefusal(writer, subject!);
        if (writerRefusal != null) return BadRequest(new { error = writerRefusal });

        var audiences = (request.Audiences ?? new List<BenchmarkReportAudience>())
            .Where(a => Enum.IsDefined(a))
            .Distinct()
            .OrderBy(a => a)
            .ToList();
        if (audiences.Count == 0) return BadRequest(new { error = "Choose at least one document to write." });

        var written = (await WrittenDocumentsAsync(subject!.Key, request, ct)).FirstOrDefault(d => audiences.Contains(d.Audience));
        if (written != null)
        {
            return Conflict(new
            {
                error = $"The {BenchmarkReportRenderService.AudienceName(written.Audience)} about {subject.Label} is already written for this comparison. "
                    + "Delete it in step 4 to write it again."
            });
        }

        var (canSpend, denialReason) = await _complianceGuard.CanSpendAsync(ct: ct);
        if (!canSpend) return StatusCode(StatusCodes.Status429TooManyRequests, denialReason);

        bool sameProvider = _complianceGuard.IsSameProvider(writer!.Provider, subject!.Provider);
        if (sameProvider && !request.AcknowledgeSameProvider)
        {
            return StatusCode(StatusCodes.Status409Conflict, new SameProviderWarningDto
            {
                SameProvider = true,
                Provider = subject.Provider,
                TestedModelDisplayName = subject.Label,
                AssessorModelDisplayName = writer.DisplayName,
                Message = BenchmarkReportPackPreparation.SameProviderWarning(subject, writer)
            });
        }

        var running = _jobManager.Current;
        if (running != null && running.Status == BenchmarkReportPackJobStatus.Running)
        {
            return StatusCode(StatusCodes.Status409Conflict, running.ToDto());
        }

        var snapshot = await SystemAiConfigurationSnapshotStore.CaptureAndSaveAsync(_db, writer, ct);
        string startedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var cts = new CancellationTokenSource();

        var job = new BenchmarkReportPackJob
        {
            SubjectKey = subject.Key,
            SubjectLabel = subject.Label,
            SuiteId = subject.SuiteId,
            SuiteName = subject.SuiteName ?? subject.BatteryName ?? string.Empty,
            WriterConfigId = writer.Id,
            WriterDisplayName = writer.DisplayName,
            WriterSnapshotId = snapshot.Id,
            SameProviderAcknowledged = sameProvider && request.AcknowledgeSameProvider,
            Request = new BenchmarkReportPackRequest
            {
                RunIds = (request.RunIds ?? new List<long>()).ToList(),
                GroupIds = (request.GroupIds ?? new List<long>()).ToList(),
                BatteryRunIds = (request.BatteryRunIds ?? new List<long>()).ToList(),
                PricingBasis = request.PricingBasis,
                SubjectKey = subject.Key,
                Audiences = audiences,
                WriterModelConfigurationId = writer.Id,
                AcknowledgeSameProvider = request.AcknowledgeSameProvider
            },
            StartedByUserId = string.IsNullOrEmpty(startedByUserId) ? null : startedByUserId,
            Cts = cts,
            Documents = audiences.Select(a => new BenchmarkReportPackDocumentProgress { Audience = a }).ToList()
        };

        if (!_jobManager.TryStart(job, out var existing))
        {
            return StatusCode(StatusCodes.Status409Conflict, existing?.ToDto());
        }

        // The job outlives the request; nothing request-scoped may be used past this point.
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<BenchmarkReportPackService>();
            await service.RunAsync(job.Id, cts.Token);
        });

        return Accepted(new BenchmarkReportPackStartResponse { JobId = job.Id });
    }

    /// <summary>
    /// Writes a finished run's missing run-completion documents with the given writer, which becomes
    /// the run's report writer: the requested ones, or every missing one when none is named. Answers 202
    /// with the run's Pending status and the documents the job will write. Refusals, in order: no body
    /// (400); unknown run (404); the run has no final synthesis yet (400); a job for the run is Pending
    /// or Writing (409); a requested document that is not a run-completion document (400); a requested
    /// document already written (409), or with none requested, every one written (409); an unusable writer or
    /// the model under test (400); a writer of the candidate's provider, unacknowledged (409 with the
    /// warning); a refused endpoint (400); the spend cap (429).
    /// </summary>
    [HttpPost("runs/{runId:long}/report-documents")]
    public async Task<IActionResult> WriteRunReportDocuments(long runId, [FromBody] WriteRunReportDocumentsRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "A request body is required." });
        if (_runReportDocuments == null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        var run = await _db.BenchmarkRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run == null) return NotFound();

        if (!BenchmarkRunReportDocumentService.IsFinishedWithSynthesis(run))
        {
            return BadRequest(new { error = NoSynthesisMessage });
        }

        if (BenchmarkRunReportDocumentService.IsInProgress(run.ReportDocumentsStatus) || _runReportDocuments.IsActive(runId))
        {
            return Conflict(new { error = "The reports of this run are already being written." });
        }

        var (requested, invalidAudience) = RequestedAudiences(request.Audiences);
        if (invalidAudience != null) return invalidAudience;

        var missing = await BenchmarkRunReportDocumentService.MissingAudiencesAsync(_db, runId, ct);
        List<BenchmarkReportAudience> toWrite;
        if (requested == null)
        {
            if (missing.Count == 0)
            {
                return Conflict(new { error = AllWrittenMessage });
            }
            toWrite = missing;
        }
        else
        {
            var written = requested.Where(a => !missing.Contains(a)).ToList();
            if (written.Count > 0)
            {
                return Conflict(new { error = $"The {BenchmarkReportRenderService.AudienceName(written[0])} is already written. Delete it first to write it again." });
            }
            toWrite = requested;
        }

        var writer = await _db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == request.WriterModelConfigurationId, ct);
        var candidate = BenchmarkRunReportDocumentService.CandidateIdentity(run);
        string? refusal = BenchmarkRunReportDocumentService.WriterRefusal(writer, candidate, _complianceGuard);
        if (refusal != null) return BadRequest(new { error = refusal });

        string? warning = BenchmarkRunReportDocumentService.WriterWarning(writer, candidate, _complianceGuard);
        if (warning != null && !request.AcknowledgeSameProvider)
        {
            return StatusCode(StatusCodes.Status409Conflict, BenchmarkRunReportDocumentService.WriterWarningDto(writer!, candidate, warning));
        }

        if (!_endpointPolicy.TryResolveStrict(writer!.BaseUrl, writer.CustomHeadersJson, writer.ApiVersion, out _, out var endpointError))
        {
            return BadRequest(new { error = EndpointRefusal(writer, endpointError) });
        }

        var (canSpend, denialReason) = await _complianceGuard.CanSpendAsync(ct: ct);
        if (!canSpend) return StatusCode(StatusCodes.Status429TooManyRequests, denialReason);

        run.ReportWriterModelConfigurationId = writer.Id;
        run.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Pending;
        run.ReportDocumentsMessage = null;
        await _db.SaveChangesAsync(ct);

        string? userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!_runReportDocuments.TryStart(runId, userId, toWrite, request.AcknowledgeSameProvider, out _))
        {
            return Conflict(new { error = "The reports of this run are already being written." });
        }

        return Accepted(new WriteRunReportDocumentsResponse
        {
            RunId = runId,
            Status = BenchmarkRunReportDocumentsStatus.Pending,
            Audiences = toWrite.ToList()
        });
    }

    /// <summary>
    /// The run's current or last run-completion job: 200 with its view, 204 when this process knows
    /// none for the run (none since the last restart, or its finished job has expired), 404 for an
    /// unknown run.
    /// </summary>
    [HttpGet("runs/{runId:long}/report-documents/job")]
    public async Task<IActionResult> GetRunReportJob(long runId, CancellationToken ct)
    {
        if (_runReportDocuments == null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        var run = await _db.BenchmarkRuns.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run == null) return NotFound();

        var view = RunJobView(run);
        return view == null ? NoContent() : Ok(view);
    }

    /// <summary>
    /// Cancels the run's run-completion job: 202 with its view once asked; 409 when no job for the run
    /// is in progress; 404 for an unknown run. Documents written before the cancellation are kept.
    /// </summary>
    [HttpPost("runs/{runId:long}/report-documents/cancel")]
    public async Task<IActionResult> CancelRunReportJob(long runId, CancellationToken ct)
    {
        if (_runReportDocuments == null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        var run = await _db.BenchmarkRuns.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run == null) return NotFound();

        if (_runReportDocuments.TryCancel(runId) != BenchmarkRunReportDocumentService.CancelOutcome.Requested)
        {
            return Conflict(new { error = "No report writing is in progress for this run." });
        }
        return Accepted(RunJobView(run));
    }

    /// <summary>
    /// What writing the run's documents with the writer would cost, by the preview's arithmetic, with
    /// the writer's refusal or same-provider warning. Computes the fact sheet and the prompts; makes no
    /// model call. 404 for an unknown run; a document that is not a run-completion document is a 400; a
    /// request the client aborts is a 499.
    /// </summary>
    [HttpPost("runs/{runId:long}/report-documents/estimate")]
    public async Task<IActionResult> EstimateRunReportDocuments(long runId, [FromBody] BenchmarkRunReportEstimateRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "A request body is required." });

        try
        {
            var run = await _db.BenchmarkRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
            if (run == null) return NotFound();

            var (requested, invalidAudience) = RequestedAudiences(request.Audiences);
            if (invalidAudience != null) return invalidAudience;
            var audiences = requested ?? await BenchmarkRunReportDocumentService.MissingAudiencesAsync(_db, runId, ct);

            var writer = request.WriterModelConfigurationId > 0
                ? await _db.SystemAiApiConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.WriterModelConfigurationId, ct)
                : null;
            var candidate = BenchmarkRunReportDocumentService.CandidateIdentity(run);
            var estimate = new BenchmarkRunReportEstimateDto
            {
                Refusal = BenchmarkRunReportDocumentService.WriterRefusal(writer, candidate, _complianceGuard)
            };
            if (estimate.Refusal == null
                && !_endpointPolicy.TryResolveStrict(writer!.BaseUrl, writer.CustomHeadersJson, writer.ApiVersion, out _, out var endpointError))
            {
                estimate.Refusal = EndpointRefusal(writer, endpointError);
            }
            string? warning = BenchmarkRunReportDocumentService.WriterWarning(writer, candidate, _complianceGuard);
            if (estimate.Refusal == null && warning != null)
            {
                estimate.SameProviderWarning = BenchmarkRunReportDocumentService.WriterWarningDto(writer!, candidate, warning);
            }

            if (!BenchmarkRunReportDocumentService.IsFinishedWithSynthesis(run))
            {
                estimate.Refusal ??= NoSynthesisMessage;
                return Ok(estimate);
            }

            int excerptChars = BenchmarkReportPackPreparation.AnswerExcerptChars(_configuration);
            var (prep, prepRefusal) = await BenchmarkReportPackPreparation.PrepareAsync(
                _db, _comparisonService, BenchmarkRunReportDocumentService.RunRequest(runId, audiences, writer?.Id ?? 0), excerptChars, ct, _configuration);
            if (prep == null)
            {
                estimate.Refusal ??= prepRefusal ?? "The reports could not be prepared.";
                return Ok(estimate);
            }

            estimate.Estimates.AddRange(EstimateAudiences(prep, writer, audiences));
            estimate.EstimatedTotalCostUsd = TotalCost(estimate.Estimates);
            return Ok(estimate);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Deletes one of the run's own run-completion documents and settles the run's documents status.
    /// 404 when the run or the document is unknown, or the document is not this run's run-completion
    /// document; 409 while the run's documents are being written.
    /// </summary>
    [HttpDelete("runs/{runId:long}/report-documents/{documentId:long}")]
    public async Task<IActionResult> DeleteRunReportDocument(long runId, long documentId, CancellationToken ct)
    {
        var run = await _db.BenchmarkRuns.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run == null) return NotFound();

        string subjectKey = BenchmarkRunReportDocumentService.SubjectKeyOf(runId);
        bool isRunDocument = await _db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .AnyAsync(d => d.Id == documentId && d.SubjectKey == subjectKey && d.Origin == BenchmarkReportDocumentOrigin.RunCompletion, ct);
        if (!isRunDocument) return NotFound();

        if (BenchmarkRunReportDocumentService.IsInProgress(run.ReportDocumentsStatus) || (_runReportDocuments?.IsActive(runId) ?? false))
        {
            return Conflict(new { error = "Wait for the writing to finish, or cancel it, before deleting a report." });
        }

        return await BenchmarkReportRenderService.DeleteDocumentAsync(_db, documentId, ct) ? NoContent() : NotFound();
    }

    [HttpGet("report-packs/jobs/{jobId}")]
    public IActionResult GetJob(string jobId)
    {
        var job = _jobManager.TryGet(jobId);
        if (job == null) return NotFound();
        return Ok(job.ToDto());
    }

    [HttpGet("report-packs/jobs/active")]
    public IActionResult GetActiveJob()
    {
        var current = _jobManager.Current;
        if (current == null || current.Status != BenchmarkReportPackJobStatus.Running) return NoContent();
        return Ok(current.ToDto());
    }

    [HttpPost("report-packs/jobs/{jobId}/cancel")]
    public IActionResult Cancel(string jobId)
    {
        var job = _jobManager.TryGet(jobId);
        if (job == null) return NotFound();
        return Ok(new { cancelled = _jobManager.TryCancel(jobId) });
    }

    private const string NoSynthesisMessage = "The run has not finished with a final synthesis, so there is nothing to write about yet.";

    /// <summary>
    /// The requested run-completion documents in <see cref="BenchmarkRunReportDocumentService.Audiences"/>
    /// order, or null when none is named; a 400 when one is not a run-completion document.
    /// </summary>
    private (List<BenchmarkReportAudience>? Audiences, IActionResult? Invalid) RequestedAudiences(List<BenchmarkReportAudience>? audiences)
    {
        if (audiences == null || audiences.Count == 0) return (null, null);
        if (audiences.Any(a => !BenchmarkRunReportDocumentService.Audiences.Contains(a)))
        {
            return (null, BadRequest(new { error = InvalidAudienceMessage }));
        }
        return (BenchmarkRunReportDocumentService.Audiences.Where(audiences.Contains).ToList(), null);
    }

    /// <summary>
    /// The Report Pack documents stored for the subject in the comparison of the request's sources,
    /// keyed as the stored rows were (<see cref="BenchmarkReportComparisonKey.From"/>): the newest per
    /// audience, in audience order.
    /// </summary>
    private async Task<List<BenchmarkReportPackWrittenDocumentDto>> WrittenDocumentsAsync(
        string subjectKey, BenchmarkReportPackRequest request, CancellationToken ct)
    {
        string comparisonKey = BenchmarkReportComparisonKey.From(
            request.RunIds ?? new List<long>(), request.GroupIds ?? new List<long>(), request.BatteryRunIds ?? new List<long>());

        var stored = await _db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(d => d.Origin == BenchmarkReportDocumentOrigin.ReportPack && d.SubjectKey == subjectKey && d.ComparisonKey == comparisonKey)
            .Select(d => new { d.Id, d.Audience, d.CreatedAtUtc, d.WriterDisplayName })
            .ToListAsync(ct);

        return stored
            .GroupBy(d => d.Audience)
            .Select(g => g.OrderByDescending(d => d.CreatedAtUtc).ThenByDescending(d => d.Id).First())
            .OrderBy(d => d.Audience)
            .Select(d => new BenchmarkReportPackWrittenDocumentDto
            {
                Audience = d.Audience,
                DocumentId = d.Id,
                CreatedAtUtc = d.CreatedAtUtc,
                WriterDisplayName = d.WriterDisplayName
            })
            .ToList();
    }

    /// <summary>The run's job view with the run's persisted status and message, or null when this process knows no job for it.</summary>
    private BenchmarkRunReportJobDto? RunJobView(BenchmarkRun run)
    {
        var view = _runReportDocuments?.TryGetJob(run.Id);
        if (view == null) return null;
        view.Status = run.ReportDocumentsStatus;
        view.Message = run.ReportDocumentsMessage;
        return view;
    }

    /// <summary>Each document's prompt size, estimated tokens and first-call cost; makes no model call.</summary>
    private BenchmarkReportPackAudienceEstimateDto[] EstimateAudiences(
        BenchmarkReportPackPreparation prep, SystemAiApiConfiguration? writer, IEnumerable<BenchmarkReportAudience> audiences)
    {
        int maxOutputTokens = BenchmarkReportPackPreparation.MaxOutputTokens(_configuration);
        var pricing = writer != null ? _pricingService.Resolve(writer) : null;
        return audiences.Select(audience =>
        {
            var prompt = BenchmarkReportPackPrompt.Build(audience, prep.Sheet, prep.Content);
            int chars = prompt.SystemPrompt.Length + prompt.UserMessage.Length;
            int input = (chars + 3) / 4;
            int output = Math.Min(maxOutputTokens, EstimatedOutputTokens[audience]);
            return new BenchmarkReportPackAudienceEstimateDto
            {
                Audience = audience,
                PromptChars = chars,
                EstimatedInputTokens = input,
                EstimatedOutputTokens = output,
                EstimatedCostUsd = pricing == null ? null : (double)ModelPricingService.ComputeCost(pricing, input, output, 0, 0)
            };
        }).ToArray();
    }

    /// <summary>The sum of the estimates' costs; null when there is none or a document has no price.</summary>
    private static double? TotalCost(IReadOnlyCollection<BenchmarkReportPackAudienceEstimateDto> estimates)
        => estimates.Count == 0 || estimates.Any(e => e.EstimatedCostUsd == null) ? null : estimates.Sum(e => e.EstimatedCostUsd!.Value);

    private static string EndpointRefusal(SystemAiApiConfiguration writer, string? endpointError)
        => $"Report writer configuration '{writer.DisplayName}': its custom endpoint is not allowed by the endpoint policy: {endpointError}";

    /// <summary>Why the configuration cannot write this subject's documents, or null when it can.</summary>
    private string? WriterRefusal(SystemAiApiConfiguration? writer, BenchmarkModelComparisonEntryDto subject)
    {
        if (writer == null || !writer.IsEnabled || string.IsNullOrWhiteSpace(writer.EncryptedApiKey) || (writer.ModelRole & 4) != 4)
        {
            return "The selected writer model is invalid, disabled, missing an API key, or not configured with the Benchmark role.";
        }
        if (!_endpointPolicy.TryResolveStrict(writer.BaseUrl, writer.CustomHeadersJson, writer.ApiVersion, out _, out var endpointError))
        {
            return $"Configuration '{writer.DisplayName}': its custom endpoint is not allowed by the endpoint policy: {endpointError}";
        }
        if (_complianceGuard.IsSameModel(writer, BenchmarkReportPackPreparation.SubjectIdentity(subject)))
        {
            return $"{writer.DisplayName} is the model under report and cannot write its own report. Choose a writer from another model, preferably another family.";
        }
        return null;
    }
}
