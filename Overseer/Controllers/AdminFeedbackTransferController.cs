using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Feedback;

namespace Overseer.Controllers;

/// <summary>
/// The admin Feedback tab's transfer to and from an AI coding assistant: the YAML or Markdown export of selected issues
/// (<see cref="FeedbackExportService"/>), the format examples, and the preview and apply of a resolution file
/// (<see cref="FeedbackResolutionService"/>), for both sub-tabs. Errors are <c>{ message }</c>; a resolution file that
/// breaks the format's rules is 400 with <c>{ message, errors }</c>, and an apply that finds issues changed since the
/// preview is 409 with <c>{ message, conflicts }</c> and writes nothing. Reads answer 499 when the client aborts; apply
/// takes no request token, so a dropped connection never stops it part-way.
/// </summary>
[ApiController]
[Route("api/admin/feedback")]
[Authorize(Policy = "AdminOnly")]
public class AdminFeedbackTransferController : ControllerBase
{
    private readonly FeedbackExportService _export;
    private readonly FeedbackResolutionService _resolutions;

    public AdminFeedbackTransferController(FeedbackExportService export, FeedbackResolutionService resolutions)
    {
        _export = export;
        _resolutions = resolutions;
    }

    private string? CurrentUserId => User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// The YAML or Markdown export of at most 200 issues, with the requested ones that no longer exist. 400 for an
    /// invalid request; 404 with <c>{ message, missingIds, missingFingerprints }</c> when none of them exists.
    /// </summary>
    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] AdminFeedbackExportRequestDto? request, CancellationToken ct = default)
    {
        try
        {
            var result = await _export.ExportAsync(request, OverseerVersion(), ct);
            return result.Outcome switch
            {
                FeedbackExportOutcome.Invalid => BadRequest(new { message = result.Message }),
                FeedbackExportOutcome.NotFound => NotFound(new { message = result.Message, missingIds = result.MissingIds, missingFingerprints = result.MissingFingerprints }),
                _ => Ok(result.Export)
            };
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// The example issues file and resolution file of <paramref name="source"/> (<c>overseer</c> or <c>gnollbench</c>),
    /// as <c>{ issues, resolutions }</c>, each <c>{ fileName, content, mediaType }</c>. 400 for an unknown source.
    /// </summary>
    [HttpGet("format-examples")]
    public IActionResult FormatExamples([FromQuery] string? source = null)
    {
        if (!FeedbackTransferSources.TryParse(source, out var parsed))
            return BadRequest(new { message = $"Unknown source '{source}'. Use overseer or gnollbench." });
        return Ok(FeedbackFormatExamples.For(parsed));
    }

    /// <summary>Validates a resolution file and compares each item with the stored issue; nothing is written.</summary>
    [HttpPost("resolutions/preview")]
    public async Task<IActionResult> PreviewResolutions([FromBody] AdminFeedbackResolutionRequestDto? request, CancellationToken ct = default)
    {
        try
        {
            var result = await _resolutions.PreviewAsync(request, ct);
            return result.Outcome == FeedbackResolutionOutcome.Ok ? Ok(result.Preview) : BadRequest(result.Invalid);
        }
        catch (Exception ex) when (ex.IsCanceledBy(ct))
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Applies a previewed resolution file in one transaction and returns <c>{ changed, unchanged }</c>. 409 with the
    /// changed issues, and nothing written, when an issue's status or triage time differs from the expected one.
    /// </summary>
    [HttpPost("resolutions/apply")]
    public async Task<IActionResult> ApplyResolutions([FromBody] AdminFeedbackResolutionRequestDto? request)
    {
        var result = await _resolutions.ApplyAsync(request, CurrentUserId, CancellationToken.None);
        return result.Outcome switch
        {
            FeedbackResolutionOutcome.Invalid => BadRequest(result.Invalid),
            FeedbackResolutionOutcome.Conflict => Conflict(result.Conflict),
            _ => Ok(result.Applied)
        };
    }

    /// <summary>The entry assembly's informational version, as the occurrences record it; <c>unknown</c> without one.</summary>
    private static string OverseerVersion()
        => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
}
