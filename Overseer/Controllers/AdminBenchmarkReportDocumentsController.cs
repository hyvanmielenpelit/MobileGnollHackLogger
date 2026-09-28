namespace Overseer.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Overseer.Models;
using Overseer.Services.Benchmarking;

/// <summary>
/// Stored report-pack documents: list, detail, render and delete. Its only dependency is
/// <see cref="BenchmarkReportRenderService"/>, which holds no provider, key or agent loop, so no
/// action here can make a model call; a test pins the constructor.
/// </summary>
[Route("api/admin/benchmark")]
[Authorize(Policy = "AdminOnly")]
[ApiController]
public class AdminBenchmarkReportDocumentsController : ControllerBase
{
    private readonly BenchmarkReportRenderService _renderService;

    public AdminBenchmarkReportDocumentsController(BenchmarkReportRenderService renderService)
    {
        _renderService = renderService;
    }

    [HttpGet("report-documents")]
    public async Task<IActionResult> List([FromQuery] long? suiteId, [FromQuery] long? runId, [FromQuery] int? take, CancellationToken ct)
        => Ok(await _renderService.ListAsync(suiteId, runId, take, ct));

    [HttpGet("report-documents/{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var detail = await _renderService.GetAsync(id, ct);
        return detail == null ? NotFound() : Ok(detail);
    }

    /// <summary>
    /// The document as Markdown at the requested disclosure (<c>summary</c>, <c>detailed</c>,
    /// <c>full</c>) and peer naming (<c>named</c>, <c>anonymized</c>). Deterministic: the same
    /// document and options always produce the same bytes.
    /// </summary>
    [HttpGet("report-documents/{id:long}/render")]
    public async Task<IActionResult> Render(long id, [FromQuery] string? disclosure, [FromQuery] string? peers, CancellationToken ct)
    {
        if (!Enum.TryParse<BenchmarkReportDisclosure>(disclosure, ignoreCase: true, out var level) || !Enum.IsDefined(level)
            || int.TryParse(disclosure, out _))
        {
            return BadRequest(new { error = "disclosure must be summary, detailed or full." });
        }
        if (!Enum.TryParse<BenchmarkReportPeerNaming>(peers, ignoreCase: true, out var naming) || !Enum.IsDefined(naming)
            || int.TryParse(peers, out _))
        {
            return BadRequest(new { error = "peers must be named or anonymized." });
        }

        var (markdown, notFound, refusal) = await _renderService.RenderAsync(
            id, new BenchmarkReportRenderOptions { Disclosure = level, PeerNaming = naming }, ct);
        if (notFound) return NotFound();
        if (refusal != null) return BadRequest(new { error = refusal });

        return Content(markdown!, "text/markdown; charset=utf-8");
    }

    [HttpDelete("report-documents/{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
        => await _renderService.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
