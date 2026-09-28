namespace Overseer.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.Pdf;

/// <summary>
/// Stored report-pack documents: list, detail, render (Markdown or PDF) and delete. Its only dependency is
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
        var (options, invalid) = ParseRenderOptions(disclosure, peers);
        if (invalid != null) return invalid;

        var (markdown, notFound, refusal) = await _renderService.RenderAsync(id, options!, ct);
        if (notFound) return NotFound();
        if (refusal != null) return BadRequest(new { error = refusal });

        return Content(markdown!, "text/markdown; charset=utf-8");
    }

    /// <summary>
    /// The same document as a tagged PDF on <c>a4</c> (the default) or <c>letter</c> paper, with the
    /// same validation and refusals as <see cref="Render"/>. Rendered by the static
    /// <see cref="BenchmarkPdfRenderer"/>, so this path stays as free of model clients as the Markdown one.
    /// </summary>
    [HttpGet("report-documents/{id:long}/render/pdf")]
    public async Task<IActionResult> RenderPdf(
        long id, [FromQuery] string? disclosure, [FromQuery] string? peers, [FromQuery] string? paper, CancellationToken ct)
    {
        var (options, invalid) = ParseRenderOptions(disclosure, peers);
        if (invalid != null) return invalid;
        if (!BenchmarkPdfDocumentInfo.TryParsePaper(paper, out var pdfPaper))
        {
            return BadRequest(new { error = BenchmarkPdfDocumentInfo.PaperError });
        }

        var (markdown, document, notFound, refusal) = await _renderService.RenderWithDocumentAsync(id, options!, ct);
        if (notFound) return NotFound();
        if (refusal != null) return BadRequest(new { error = refusal });

        if (BenchmarkPdfRenderer.IsTooLarge(markdown))
        {
            return StatusCode(413, new { error = BenchmarkPdfRenderer.TooLargeMessage(markdown!.Length) });
        }

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document!, options!, pdfPaper);
        byte[] pdf = await Task.Run(() => BenchmarkPdfRenderer.RenderMarkdown(markdown!, info, ct), ct);

        return File(pdf, "application/pdf", BenchmarkPdfFileNames.ForReportDocument(document!, options!));
    }

    [HttpDelete("report-documents/{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
        => await _renderService.DeleteAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>
    /// The disclosure (<c>summary</c>, <c>detailed</c>, <c>full</c>) and peer naming (<c>named</c>,
    /// <c>anonymized</c>), by name only: a number or an unknown name is a 400.
    /// </summary>
    private (BenchmarkReportRenderOptions? Options, IActionResult? Invalid) ParseRenderOptions(string? disclosure, string? peers)
    {
        if (!Enum.TryParse<BenchmarkReportDisclosure>(disclosure, ignoreCase: true, out var level) || !Enum.IsDefined(level)
            || int.TryParse(disclosure, out _))
        {
            return (null, BadRequest(new { error = "disclosure must be summary, detailed or full." }));
        }
        if (!Enum.TryParse<BenchmarkReportPeerNaming>(peers, ignoreCase: true, out var naming) || !Enum.IsDefined(naming)
            || int.TryParse(peers, out _))
        {
            return (null, BadRequest(new { error = "peers must be named or anonymized." }));
        }

        return (new BenchmarkReportRenderOptions { Disclosure = level, PeerNaming = naming }, null);
    }
}
