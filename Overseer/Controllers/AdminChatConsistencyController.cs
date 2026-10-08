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
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.ChatConsistency;

/// <summary>
/// The GnollBench chat consistency API: the model axes, a subject's timeline, run table, battery-run
/// table and comparison sets, saved
/// analyses and their AI-written report documents, the common-grader re-grade, grader anchors and
/// timeline annotations. HTTP mapping only; the work is done by <see cref="ChatConsistencyAnalysisService"/>,
/// <see cref="ChatConsistencyEvidenceBuilder"/>, <see cref="ChatConsistencyRegradeService"/> and
/// <see cref="BenchmarkReportPackService"/>. Every record is returned in
/// <see cref="ChatConsistencyJson.Options"/> (camelCase, enums as camelCase strings), the format the
/// analyses are stored in, except the report-document endpoints, which answer in the run
/// report-documents contract (enums as numbers). A refused request is a 400 with <c>{ error }</c>.
/// </summary>
[Route("api/admin/benchmark/chat-consistency")]
[Authorize(Policy = "AdminOnly")]
[ApiController]
public class AdminChatConsistencyController : ControllerBase
{
    public const string BodyRequiredError = "A request body is required.";
    public const string ModelKeyRequiredError = "modelKey is required.";
    public const string RangeError = "from must not be after to.";
    public const string NoRegradeRunsError = "Choose at least one run to re-grade.";
    public const string NoRegradeRunningError = "No re-grade is running.";

    private readonly ChatConsistencyAnalysisService _analysis;
    private readonly ChatConsistencyEvidenceBuilder _evidence;
    private readonly ChatConsistencyRegradeService _regrade;
    private readonly BenchmarkReportPackService _reports;

    public AdminChatConsistencyController(
        ChatConsistencyAnalysisService analysis,
        ChatConsistencyEvidenceBuilder evidence,
        ChatConsistencyRegradeService regrade,
        BenchmarkReportPackService reports)
    {
        _analysis = analysis;
        _evidence = evidence;
        _regrade = regrade;
        _reports = reports;
    }

    // --- Model axes, timeline and run table ------------------------------------------------------

    /// <summary>Every model axis with usable runs, with run counts and the dates of its first and last run; 499 when the client aborts.</summary>
    [HttpGet("models")]
    public async Task<IActionResult> ListModels(CancellationToken ct)
    {
        try
        {
            return Payload(await _evidence.ListModelAxesAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// One point per usable run of <paramref name="modelKey"/> between <paramref name="from"/> and
    /// <paramref name="to"/> (inclusive, either optional, UTC), with the subject's events and
    /// annotations. 400 without a model key or when <paramref name="from"/> is after <paramref name="to"/>;
    /// 499 when the client aborts.
    /// </summary>
    [HttpGet("timeline")]
    public async Task<IActionResult> Timeline([FromQuery] string? modelKey, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var (fromUtc, toUtc, invalid) = ValidateRange(modelKey, from, to);
        if (invalid != null) return invalid;

        try
        {
            return Payload(await _evidence.GetTimelineAsync(modelKey!, fromUtc, toUtc, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>The run table of <paramref name="modelKey"/> over the range, validated as <see cref="Timeline"/> is; 499 when the client aborts.</summary>
    [HttpGet("runs")]
    public async Task<IActionResult> Runs([FromQuery] string? modelKey, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var (fromUtc, toUtc, invalid) = ValidateRange(modelKey, from, to);
        if (invalid != null) return invalid;

        try
        {
            return Payload(await _evidence.GetRunTableAsync(modelKey!, fromUtc, toUtc, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// The battery runs of <paramref name="modelKey"/> started in the range, newest first, with their
    /// completeness and usable members, validated as <see cref="Timeline"/> is; 499 when the client aborts.
    /// </summary>
    [HttpGet("battery-runs")]
    public async Task<IActionResult> BatteryRuns([FromQuery] string? modelKey, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var (fromUtc, toUtc, invalid) = ValidateRange(modelKey, from, to);
        if (invalid != null) return invalid;

        try
        {
            return Payload(await _evidence.GetBatteryRunTableAsync(modelKey!, fromUtc, toUtc, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// The batteries and suites <paramref name="modelKey"/> can be compared within over the range, with the
    /// default set, validated as <see cref="Timeline"/> is; 499 when the client aborts.
    /// </summary>
    [HttpGet("comparison-sets")]
    public async Task<IActionResult> ComparisonSets([FromQuery] string? modelKey, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var (fromUtc, toUtc, invalid) = ValidateRange(modelKey, from, to);
        if (invalid != null) return invalid;

        try
        {
            return Payload(await _evidence.GetComparisonSetsAsync(modelKey!, fromUtc, toUtc, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    // --- Analyses --------------------------------------------------------------------------------

    /// <summary>
    /// Runs and saves an analysis; 200 with the saved result, its <c>analysisId</c> set. The periods'
    /// bounds are taken as UTC. 400 with the refusal for malformed or overlapping periods and for a
    /// period without a usable run; 499 when the client aborts.
    /// </summary>
    [HttpPost("analyses")]
    public async Task<IActionResult> Analyze([FromBody] ChatConsistencyAnalysisRequest? request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = BodyRequiredError });

        var utc = request with
        {
            BaselineStartUtc = ToUtc(request.BaselineStartUtc),
            BaselineEndUtc = ToUtc(request.BaselineEndUtc),
            ComparisonStartUtc = ToUtc(request.ComparisonStartUtc),
            ComparisonEndUtc = ToUtc(request.ComparisonEndUtc)
        };

        try
        {
            return Payload(await _analysis.AnalyzeAsync(utc, ct));
        }
        catch (ChatConsistencyRequestException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>Every saved analysis, newest first, without the full results; 499 when the client aborts.</summary>
    [HttpGet("analyses")]
    public async Task<IActionResult> ListAnalyses(CancellationToken ct)
    {
        try
        {
            return Payload(await _analysis.ListAnalysesAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>One saved analysis with its full result; 404 when there is none; 499 when the client aborts.</summary>
    [HttpGet("analyses/{id:int}")]
    public async Task<IActionResult> GetAnalysis(int id, CancellationToken ct)
    {
        try
        {
            var result = await _analysis.GetAnalysisAsync(id, ct);
            return result == null ? NotFound() : Payload(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>Deletes a saved analysis: 204; 404 when there is none; 409 with the refusal while report documents written from it exist; 499 when the client aborts.</summary>
    [HttpDelete("analyses/{id:int}")]
    public async Task<IActionResult> DeleteAnalysis(int id, CancellationToken ct)
    {
        try
        {
            var outcome = await _analysis.DeleteAnalysisAsync(id, ct);
            if (!outcome.Found) return NotFound();
            if (!outcome.Deleted) return Conflict(new { error = outcome.Refusal });
            return NoContent();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// What writing the analysis's documents with the writer would cost (the run report estimate), with
    /// <c>providerIssueReportAvailable</c> and <c>providerIssueReportReason</c>. Makes no model call. 404
    /// for an unknown analysis; 400 for a document that is not a chat consistency document; 499 when the
    /// client aborts.
    /// </summary>
    [HttpPost("analyses/{id:int}/report-documents/estimate")]
    public async Task<IActionResult> EstimateReports(int id, [FromBody] BenchmarkRunReportEstimateRequest? request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = BodyRequiredError });

        try
        {
            return ReportResult(await _reports.EstimateChatConsistencyDocumentsAsync(id, request.WriterModelConfigurationId, request.Audiences, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Writes the analysis's requested documents (every one it can still have when none is named) with
    /// the writer: 202 with the run report-documents response, its <c>runId</c> the analysis id. 404 for
    /// an unknown analysis; 409 while its documents are being written, for a document already written,
    /// and with the same-provider warning for an unacknowledged writer of the model's provider; 400 for a
    /// document that is not a chat consistency document, for the Provider Issue Report while it is not
    /// available (with the reason), for an unusable writer or the model under report and for a refused
    /// endpoint; 429 at the spend cap; 499 when the client aborts.
    /// </summary>
    [HttpPost("analyses/{id:int}/report-documents")]
    public async Task<IActionResult> WriteReports(int id, [FromBody] WriteRunReportDocumentsRequest? request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = BodyRequiredError });

        string? userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        try
        {
            return ReportResult(await _reports.WriteChatConsistencyDocumentsAsync(
                id, request.WriterModelConfigurationId, request.Audiences, request.AcknowledgeSameProvider, userId, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// The analysis's current or last report-writing job: 200 with the run report job view, 204 when this
    /// process knows none, 404 for an unknown analysis, 499 when the client aborts.
    /// </summary>
    [HttpGet("analyses/{id:int}/report-documents/job")]
    public async Task<IActionResult> GetReportJob(int id, CancellationToken ct)
    {
        try
        {
            return ReportResult(await _reports.GetChatConsistencyJobAsync(id, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Cancels the analysis's report-writing job: 202 with its view once asked; 409 when none is in
    /// progress; 404 for an unknown analysis; 499 when the client aborts. Documents written before the
    /// cancellation are kept.
    /// </summary>
    [HttpPost("analyses/{id:int}/report-documents/cancel")]
    public async Task<IActionResult> CancelReportJob(int id, CancellationToken ct)
    {
        try
        {
            return ReportResult(await _reports.CancelChatConsistencyJobAsync(id, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    // --- Common-grader re-grade ------------------------------------------------------------------

    /// <summary>
    /// What re-grading the runs with one assessor configuration is expected to cost, per run with each
    /// run's eligibility. Makes no model call. 400 without runs or with more than
    /// <see cref="ChatConsistencyRegradeService.MaxRunsPerJob"/>; 499 when the client aborts.
    /// </summary>
    [HttpPost("regrade/estimate")]
    public async Task<IActionResult> EstimateRegrade([FromBody] ChatConsistencyRegradeEstimateRequest? request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = BodyRequiredError });

        var ids = (request.RunIds ?? new List<long>()).Distinct().ToList();
        if (ids.Count == 0) return BadRequest(new { error = NoRegradeRunsError });
        if (ids.Count > ChatConsistencyRegradeService.MaxRunsPerJob)
        {
            return BadRequest(new { error = "One re-grade job takes at most " + ChatConsistencyRegradeService.MaxRunsPerJob + " runs." });
        }

        try
        {
            return Payload(await _regrade.EstimateAsync(ids, request.AssessorConfigId, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Starts a re-grade: 202 with the job. 400 with the refusal without <c>confirmed: true</c>, for an
    /// invalid assessor or an ineligible run, while a benchmark run or another re-grade is in progress,
    /// and when the spending guard denies it; 499 when the client aborts.
    /// </summary>
    [HttpPost("regrade")]
    public async Task<IActionResult> StartRegrade([FromBody] ChatConsistencyRegradeRequest? request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = BodyRequiredError });

        try
        {
            var outcome = await _regrade.StartAsync(request, User?.Identity?.Name, ct);
            return outcome.Started
                ? Payload(outcome.Job, StatusCodes.Status202Accepted)
                : BadRequest(new { error = outcome.Refusal });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>The current or last re-grade job: 200 with its view, 204 when none has run since start-up.</summary>
    [HttpGet("regrade/job")]
    public IActionResult GetRegradeJob()
    {
        var job = _regrade.GetJob();
        return job == null ? NoContent() : Payload(job);
    }

    /// <summary>Cancels the running re-grade: 202 with the job once asked; 409 when none runs.</summary>
    [HttpPost("regrade/cancel")]
    public IActionResult CancelRegrade()
    {
        if (!_regrade.Cancel()) return Conflict(new { error = NoRegradeRunningError });
        return Payload(_regrade.GetJob(), StatusCodes.Status202Accepted);
    }

    // --- Grader anchors --------------------------------------------------------------------------

    /// <summary>Marks or unmarks a run as the grader anchor: 200 with the run's mark; 404 when the run is unknown; 499 when the client aborts.</summary>
    [HttpPut("runs/{id:long}/anchor")]
    public async Task<IActionResult> SetAnchor(long id, [FromBody] ChatConsistencyAnchorRequest? request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = BodyRequiredError });
        try
        {
            if (!await _analysis.SetAnchorAsync(id, request.IsAnchor, ct)) return NotFound();
            return Payload(new ChatConsistencyAnchorResponse { RunId = id, IsAnchor = request.IsAnchor });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    // --- Annotations -----------------------------------------------------------------------------

    /// <summary>Annotations, oldest first; those applying to <paramref name="provider"/> and <paramref name="modelId"/> when a provider is given; 499 when the client aborts.</summary>
    [HttpGet("annotations")]
    public async Task<IActionResult> ListAnnotations([FromQuery] string? provider, [FromQuery] string? modelId, CancellationToken ct)
    {
        try
        {
            return Payload(await _analysis.ListAnnotationsAsync(Clean(provider), Clean(modelId), ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Adds an annotation: 200 with it. 400 with the refusal for empty text or text over 1,000
    /// characters, a provider over 64 or a model id over 128 characters, an undefined kind, and a source
    /// that is not an absolute http or https URL of at most 512 characters; 499 when the client aborts.
    /// </summary>
    [HttpPost("annotations")]
    public async Task<IActionResult> AddAnnotation([FromBody] ChatConsistencyAnnotationRequest? request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = BodyRequiredError });

        try
        {
            return Payload(await _analysis.AddAnnotationAsync(new ChatConsistencyAnnotationInput
            {
                AtUtc = ToUtc(request.AtUtc),
                Provider = request.Provider,
                ModelId = request.ModelId,
                Kind = request.Kind,
                Text = request.Text ?? string.Empty,
                SourceUrl = request.SourceUrl
            }, ct));
        }
        catch (ChatConsistencyRequestException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>Deletes an annotation: 204, or 404 when there is none; 499 when the client aborts.</summary>
    [HttpDelete("annotations/{id:int}")]
    public async Task<IActionResult> DeleteAnnotation(int id, CancellationToken ct)
    {
        try
        {
            return await _analysis.DeleteAnnotationAsync(id, ct) ? NoContent() : NotFound();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    // --- Helpers ---------------------------------------------------------------------------------

    /// <summary>
    /// <paramref name="value"/> as UTC: a UTC time as it is, a local time converted (query binding
    /// turns a <c>Z</c> or offset time into one), and a time without a kind taken as UTC.
    /// </summary>
    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static DateTime? ToUtc(DateTime? value) => value.HasValue ? ToUtc(value.Value) : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The model key and the range in UTC, or the 400 that refuses them.</summary>
    private (DateTime? From, DateTime? To, IActionResult? Invalid) ValidateRange(string? modelKey, DateTime? from, DateTime? to)
    {
        if (string.IsNullOrWhiteSpace(modelKey)) return (null, null, BadRequest(new { error = ModelKeyRequiredError }));

        var fromUtc = ToUtc(from);
        var toUtc = ToUtc(to);
        if (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value > toUtc.Value)
        {
            return (null, null, BadRequest(new { error = RangeError }));
        }

        return (fromUtc, toUtc, null);
    }

    /// <summary>
    /// A report-document answer in the run contract: the value of a 200 or 202, no content for a 204, a
    /// bare 404, the same-provider warning of a 409, and <c>{ error }</c> for any other refusal.
    /// </summary>
    private IActionResult ReportResult<T>(BenchmarkChatConsistencyReportResult<T> result) where T : class
    {
        switch (result.StatusCode)
        {
            case StatusCodes.Status200OK:
                return Ok(result.Value);
            case StatusCodes.Status202Accepted:
                return Accepted(result.Value);
            case StatusCodes.Status204NoContent:
                return NoContent();
            case StatusCodes.Status404NotFound:
                return NotFound();
        }
        if (result.SameProviderWarning != null) return StatusCode(result.StatusCode, result.SameProviderWarning);
        return StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary><paramref name="value"/> serialized with <see cref="ChatConsistencyJson.Options"/>.</summary>
    private static JsonResult Payload(object? value, int statusCode = StatusCodes.Status200OK)
        => new(value, ChatConsistencyJson.Options) { StatusCode = statusCode };
}
