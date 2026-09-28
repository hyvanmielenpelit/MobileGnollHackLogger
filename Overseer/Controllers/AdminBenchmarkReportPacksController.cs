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
/// Report-pack generation: preview (no model call), start, progress and cancel, and writing a
/// finished run's own run-completion documents on request. The writer call runs in
/// <see cref="BenchmarkReportPackService"/>, resolved per job from a fresh scope; stored documents
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
        [BenchmarkReportAudience.ExecutiveSummary] = 2000,
        [BenchmarkReportAudience.TechnicalReport] = 7000,
        [BenchmarkReportAudience.InternalBrief] = 7000
    };

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
    /// The subject, its peers, the estimated cost of each document, the same-provider warning and any
    /// refusal. Computes the fact sheet and the prompts; makes no model call.
    /// </summary>
    [HttpPost("report-packs/preview")]
    public async Task<IActionResult> Preview([FromBody] BenchmarkReportPackRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "A request body is required." });

        int excerptChars = BenchmarkReportPackPreparation.AnswerExcerptChars(_configuration);
        var (prep, refusal) = await BenchmarkReportPackPreparation.PrepareAsync(_db, _comparisonService, request, excerptChars, ct);
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

        int maxOutputTokens = BenchmarkReportPackPreparation.MaxOutputTokens(_configuration);
        var pricing = writer != null ? _pricingService.Resolve(writer) : null;
        var audiences = (request.Audiences ?? new List<BenchmarkReportAudience>()).Distinct().OrderBy(a => a).ToList();
        foreach (var audience in audiences)
        {
            var prompt = BenchmarkReportPackPrompt.Build(audience, prep.Sheet, prep.Content);
            int chars = prompt.SystemPrompt.Length + prompt.UserMessage.Length;
            int input = (chars + 3) / 4;
            int output = Math.Min(maxOutputTokens, EstimatedOutputTokens[audience]);
            preview.Estimates.Add(new BenchmarkReportPackAudienceEstimateDto
            {
                Audience = audience,
                PromptChars = chars,
                EstimatedInputTokens = input,
                EstimatedOutputTokens = output,
                EstimatedCostUsd = pricing == null ? null : (double)ModelPricingService.ComputeCost(pricing, input, output, 0, 0)
            });
        }
        preview.EstimatedTotalCostUsd = pricing == null || preview.Estimates.Count == 0
            ? null
            : preview.Estimates.Sum(e => e.EstimatedCostUsd ?? 0);

        return Ok(preview);
    }

    /// <summary>
    /// Starts a job. Refusals, in order: unknown or Excluded subject (400); unusable writer (400);
    /// the writer is the subject's model (400); no document (400); spend cap (429); same provider,
    /// unacknowledged (409 with the warning); a job already running (409 with its state).
    /// </summary>
    [HttpPost("report-packs")]
    public async Task<IActionResult> Start([FromBody] BenchmarkReportPackRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "A request body is required." });

        var (_, subject, refusal) = await BenchmarkReportPackPreparation.CompareAsync(_comparisonService, request, ct);
        if (refusal != null) return BadRequest(new { error = refusal });

        var writer = await _db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == request.WriterModelConfigurationId, ct);
        string? writerRefusal = WriterRefusal(writer, subject!);
        if (writerRefusal != null) return BadRequest(new { error = writerRefusal });

        var audiences = (request.Audiences ?? new List<BenchmarkReportAudience>())
            .Where(a => Enum.IsDefined(a))
            .Distinct()
            .OrderBy(a => a)
            .ToList();
        if (audiences.Count == 0) return BadRequest(new { error = "Choose at least one document to write." });

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
            SuiteName = subject.SuiteName ?? string.Empty,
            WriterConfigId = writer.Id,
            WriterDisplayName = writer.DisplayName,
            WriterSnapshotId = snapshot.Id,
            SameProviderAcknowledged = sameProvider && request.AcknowledgeSameProvider,
            Request = new BenchmarkReportPackRequest
            {
                RunIds = (request.RunIds ?? new List<long>()).ToList(),
                GroupIds = (request.GroupIds ?? new List<long>()).ToList(),
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
    /// the run's report writer. Answers 202 with the run's Pending status. Refusals, in order: no body
    /// (400); unknown run (404); the run has no final synthesis yet (400); both documents exist, or a
    /// job for the run is Pending or Writing (409); an unusable writer, the model under test, a writer of
    /// its provider, or a refused endpoint (400); the spend cap (429).
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
            return BadRequest(new { error = "The run has not finished with a final synthesis, so there is nothing to write about yet." });
        }

        if (BenchmarkRunReportDocumentService.IsInProgress(run.ReportDocumentsStatus) || _runReportDocuments.IsActive(runId))
        {
            return Conflict(new { error = "The reports of this run are already being written." });
        }

        var missing = await BenchmarkRunReportDocumentService.MissingAudiencesAsync(_db, runId, ct);
        if (missing.Count == 0)
        {
            return Conflict(new { error = "This run already has both AI-written reports. Delete them first to write them again." });
        }

        var writer = await _db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == request.WriterModelConfigurationId, ct);
        string? refusal = BenchmarkRunReportDocumentService.WriterRefusal(
            writer, BenchmarkRunReportDocumentService.CandidateIdentity(run), _complianceGuard);
        if (refusal != null) return BadRequest(new { error = refusal });
        if (!_endpointPolicy.TryResolveStrict(writer!.BaseUrl, writer.CustomHeadersJson, writer.ApiVersion, out _, out var endpointError))
        {
            return BadRequest(new { error = $"Report writer configuration '{writer.DisplayName}': its custom endpoint is not allowed by the endpoint policy: {endpointError}" });
        }

        var (canSpend, denialReason) = await _complianceGuard.CanSpendAsync(ct: ct);
        if (!canSpend) return StatusCode(StatusCodes.Status429TooManyRequests, denialReason);

        run.ReportWriterModelConfigurationId = writer.Id;
        run.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Pending;
        run.ReportDocumentsMessage = null;
        await _db.SaveChangesAsync(ct);

        string? userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!_runReportDocuments.TryStart(runId, userId, out _))
        {
            return Conflict(new { error = "The reports of this run are already being written." });
        }

        return Accepted(new WriteRunReportDocumentsResponse
        {
            RunId = runId,
            Status = BenchmarkRunReportDocumentsStatus.Pending
        });
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
