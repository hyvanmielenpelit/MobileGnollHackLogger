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
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Services.Privacy;

/// <summary>
/// A finished battery run's own battery-completion documents: write on request, estimate, progress,
/// cancel and delete, with the request and response shapes of a run's run-completion documents
/// (<see cref="AdminBenchmarkReportPacksController"/>). The writer call runs in
/// <see cref="BenchmarkBatteryReportDocumentService"/>; stored documents are served by
/// <see cref="AdminBenchmarkReportDocumentsController"/>, which cannot reach a model.
/// </summary>
[Route("api/admin/benchmark/batteries/runs/{batteryRunId:long}/report-documents")]
[Authorize(Policy = "AdminOnly")]
[ApiController]
public class AdminBenchmarkBatteryReportsController : ControllerBase
{
    /// <summary>Rough output sizes per document, for the estimate's cost only, as the Report Pack preview prices them.</summary>
    private static readonly IReadOnlyDictionary<BenchmarkReportAudience, int> EstimatedOutputTokens = new Dictionary<BenchmarkReportAudience, int>
    {
        [BenchmarkReportAudience.ExecutiveSummary] = 2400,
        [BenchmarkReportAudience.TechnicalReport] = 7400,
        [BenchmarkReportAudience.InternalBrief] = 7000
    };

    public const string AlreadyWritingMessage = "The reports of this battery run are already being written.";
    public const string AllWrittenMessage = "This battery run already has every AI-written report. Delete one first to write it again.";
    public const string InvalidAudienceMessage =
        "Only the Executive Summary, the Report for AI Researchers and Developers and the Internal Improvement Brief are written for a battery run.";
    public const string NothingInProgressMessage = "No report writing is in progress for this battery run.";
    public const string DeleteWhileWritingMessage = "Wait for the writing to finish, or cancel it, before deleting a report.";

    private readonly ApplicationDbContext _db;
    private readonly BenchmarkComplianceGuard _complianceGuard;
    private readonly BenchmarkModelComparisonService _comparisonService;
    private readonly ModelPricingService _pricingService;
    private readonly EndpointPolicy _endpointPolicy;
    private readonly IConfiguration _configuration;
    private readonly BenchmarkBatteryReportDocumentService? _batteryReportDocuments;

    public AdminBenchmarkBatteryReportsController(
        ApplicationDbContext db,
        BenchmarkComplianceGuard complianceGuard,
        BenchmarkModelComparisonService comparisonService,
        ModelPricingService pricingService,
        EndpointPolicy endpointPolicy,
        IConfiguration configuration,
        BenchmarkBatteryReportDocumentService? batteryReportDocuments = null)
    {
        _db = db;
        _complianceGuard = complianceGuard;
        _comparisonService = comparisonService;
        _pricingService = pricingService;
        _endpointPolicy = endpointPolicy;
        _configuration = configuration;
        _batteryReportDocuments = batteryReportDocuments;
    }

    /// <summary>
    /// Writes a finished battery run's missing battery-completion documents with the given writer,
    /// which becomes the battery run's report writer: the requested ones, or every missing one when
    /// none is named. Answers 202 with the battery run's Pending status (its id in
    /// <see cref="WriteRunReportDocumentsResponse.RunId"/>) and the documents the job will write.
    /// Refusals, in order: no body (400); unknown battery run (404); the battery run has not finished,
    /// or has no complete, current analysis (400); a job for it is Pending or Writing (409); a
    /// requested document that is not a battery-completion document (400); a requested document
    /// already written (409), or with none requested, every one written (409); an unusable writer or the
    /// model under test (400); a writer of the candidate's provider, unacknowledged (409 with the
    /// warning); a refused endpoint (400); the spend cap (429).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Write(long batteryRunId, [FromBody] WriteRunReportDocumentsRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "A request body is required." });
        if (_batteryReportDocuments == null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        var source = await BenchmarkBatteryReportDocumentService.LoadSourceAsync(_db, batteryRunId, ct);
        if (source == null) return NotFound();

        string? subjectRefusal = BenchmarkBatteryReportDocumentService.SubjectRefusal(source);
        if (subjectRefusal != null) return BadRequest(new { error = subjectRefusal });

        var batteryRun = await _db.BenchmarkBatteryRuns.IgnoreAutoIncludes().FirstOrDefaultAsync(r => r.Id == batteryRunId, ct);
        if (batteryRun == null) return NotFound();

        if (BenchmarkRunReportDocumentService.IsInProgress(batteryRun.ReportDocumentsStatus) || _batteryReportDocuments.IsActive(batteryRunId))
        {
            return Conflict(new { error = AlreadyWritingMessage });
        }

        var (requested, invalidAudience) = RequestedAudiences(request.Audiences);
        if (invalidAudience != null) return invalidAudience;

        var missing = await BenchmarkBatteryReportDocumentService.MissingAudiencesAsync(_db, batteryRunId, ct);
        List<BenchmarkReportAudience> toWrite;
        if (requested == null)
        {
            if (missing.Count == 0) return Conflict(new { error = AllWrittenMessage });
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
        var candidate = BenchmarkBatteryReportDocumentService.CandidateIdentity(source);
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

        batteryRun.ReportWriterModelConfigurationId = writer.Id;
        batteryRun.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Pending;
        batteryRun.ReportDocumentsMessage = null;
        await _db.SaveChangesAsync(ct);

        string? userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!_batteryReportDocuments.TryStart(batteryRunId, userId, toWrite, request.AcknowledgeSameProvider, out _))
        {
            return Conflict(new { error = AlreadyWritingMessage });
        }

        return Accepted(new WriteRunReportDocumentsResponse
        {
            RunId = batteryRunId,
            Status = BenchmarkRunReportDocumentsStatus.Pending,
            Audiences = toWrite.ToList()
        });
    }

    /// <summary>
    /// The battery run's current or last battery-completion job: 200 with its view, 204 when this
    /// process knows none for it (none since the last restart, or its finished job has expired), 404
    /// for an unknown battery run.
    /// </summary>
    [HttpGet("job")]
    public async Task<IActionResult> GetJob(long batteryRunId, CancellationToken ct)
    {
        if (_batteryReportDocuments == null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        var batteryRun = await _db.BenchmarkBatteryRuns.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(r => r.Id == batteryRunId, ct);
        if (batteryRun == null) return NotFound();

        var view = JobView(batteryRun);
        return view == null ? NoContent() : Ok(view);
    }

    /// <summary>
    /// Cancels the battery run's battery-completion job: 202 with its view once asked; 409 when no job
    /// for it is in progress; 404 for an unknown battery run. Documents written before the cancellation
    /// are kept.
    /// </summary>
    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel(long batteryRunId, CancellationToken ct)
    {
        if (_batteryReportDocuments == null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        var batteryRun = await _db.BenchmarkBatteryRuns.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(r => r.Id == batteryRunId, ct);
        if (batteryRun == null) return NotFound();

        if (_batteryReportDocuments.TryCancel(batteryRunId) != BenchmarkRunReportDocumentService.CancelOutcome.Requested)
        {
            return Conflict(new { error = NothingInProgressMessage });
        }
        return Accepted(JobView(batteryRun));
    }

    /// <summary>
    /// What writing the battery run's documents with the writer would cost, by the Report Pack
    /// preview's arithmetic over the battery prompt, with the writer's refusal or same-provider
    /// warning. Computes the fact sheet and the prompts; makes no model call. 404 for an unknown
    /// battery run; a document that is not a battery-completion document is a 400; a request the client
    /// aborts is a 499.
    /// </summary>
    [HttpPost("estimate")]
    public async Task<IActionResult> Estimate(long batteryRunId, [FromBody] BenchmarkRunReportEstimateRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "A request body is required." });

        try
        {
            var source = await BenchmarkBatteryReportDocumentService.LoadSourceAsync(_db, batteryRunId, ct);
            if (source == null) return NotFound();

            var (requested, invalidAudience) = RequestedAudiences(request.Audiences);
            if (invalidAudience != null) return invalidAudience;
            var audiences = requested ?? await BenchmarkBatteryReportDocumentService.MissingAudiencesAsync(_db, batteryRunId, ct);

            var writer = request.WriterModelConfigurationId > 0
                ? await _db.SystemAiApiConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.WriterModelConfigurationId, ct)
                : null;
            var candidate = BenchmarkBatteryReportDocumentService.CandidateIdentity(source);
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

            string? subjectRefusal = BenchmarkBatteryReportDocumentService.SubjectRefusal(source);
            if (subjectRefusal != null)
            {
                estimate.Refusal ??= subjectRefusal;
                return Ok(estimate);
            }

            int excerptChars = BenchmarkReportPackPreparation.AnswerExcerptChars(_configuration);
            var (prep, prepRefusal) = await BenchmarkReportPackPreparation.PrepareAsync(
                _db, _comparisonService, BenchmarkReportPackPreparation.BatteryRequest(batteryRunId, audiences, writer?.Id ?? 0),
                excerptChars, ct, _configuration);
            if (prep == null)
            {
                estimate.Refusal ??= prepRefusal ?? "The reports could not be prepared.";
                return Ok(estimate);
            }

            estimate.Estimates.AddRange(EstimateAudiences(prep, writer, audiences));
            estimate.EstimatedTotalCostUsd = estimate.Estimates.Count == 0 || estimate.Estimates.Any(e => e.EstimatedCostUsd == null)
                ? null
                : estimate.Estimates.Sum(e => e.EstimatedCostUsd!.Value);
            return Ok(estimate);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Deletes one of the battery run's own battery-completion documents and settles the battery run's
    /// documents status. 404 when the battery run or the document is unknown, or the document is not
    /// this battery run's battery-completion document; 409 while the battery run's documents are
    /// being written.
    /// </summary>
    [HttpDelete("{documentId:long}")]
    public async Task<IActionResult> DeleteDocument(long batteryRunId, long documentId, CancellationToken ct)
    {
        var batteryRun = await _db.BenchmarkBatteryRuns.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(r => r.Id == batteryRunId, ct);
        if (batteryRun == null) return NotFound();

        string subjectKey = BenchmarkBatteryReportDocumentService.SubjectKeyOf(batteryRunId);
        bool isBatteryDocument = await _db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .AnyAsync(d => d.Id == documentId && d.SubjectKey == subjectKey && d.Origin == BenchmarkReportDocumentOrigin.BatteryCompletion, ct);
        if (!isBatteryDocument) return NotFound();

        if (BenchmarkRunReportDocumentService.IsInProgress(batteryRun.ReportDocumentsStatus) || (_batteryReportDocuments?.IsActive(batteryRunId) ?? false))
        {
            return Conflict(new { error = DeleteWhileWritingMessage });
        }

        return await BenchmarkReportRenderService.DeleteDocumentAsync(_db, documentId, ct) ? NoContent() : NotFound();
    }

    /// <summary>
    /// The requested battery-completion documents in <see cref="BenchmarkBatteryReportDocumentService.Audiences"/>
    /// order, or null when none is named; a 400 when one is not a battery-completion document.
    /// </summary>
    private (List<BenchmarkReportAudience>? Audiences, IActionResult? Invalid) RequestedAudiences(List<BenchmarkReportAudience>? audiences)
    {
        if (audiences == null || audiences.Count == 0) return (null, null);
        if (audiences.Any(a => !BenchmarkBatteryReportDocumentService.Audiences.Contains(a)))
        {
            return (null, BadRequest(new { error = InvalidAudienceMessage }));
        }
        return (BenchmarkBatteryReportDocumentService.Audiences.Where(audiences.Contains).ToList(), null);
    }

    /// <summary>The battery run's job view with its persisted status and message, or null when this process knows no job for it.</summary>
    private BenchmarkRunReportJobDto? JobView(BenchmarkBatteryRun batteryRun)
    {
        var view = _batteryReportDocuments?.TryGetJob(batteryRun.Id);
        if (view == null) return null;
        view.Status = batteryRun.ReportDocumentsStatus;
        view.Message = batteryRun.ReportDocumentsMessage;
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

    private static string EndpointRefusal(SystemAiApiConfiguration writer, string? endpointError)
        => $"Report writer configuration '{writer.DisplayName}': its custom endpoint is not allowed by the endpoint policy: {endpointError}";
}
