namespace Overseer.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.Pdf;
using Overseer.Services.Benchmarking.Word;

/// <summary>
/// Stored report-pack documents: list, detail, render (Markdown, PDF or Word) and delete. Its only dependency is
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

    public const string ComparisonError = "The comparison must be a comma-separated list of run:<id> and group:<id> keys.";
    public const string OriginError = "origin must be reportPack or runCompletion.";

    /// <summary>
    /// Newest first. <paramref name="comparison"/> is the comparison's entry keys
    /// (<c>run:1,run:2,group:4</c>), matched against each document's stored comparison key;
    /// <paramref name="origin"/> is <c>reportPack</c> or <c>runCompletion</c>; <paramref name="runId"/>
    /// matches a run of the subject, never of a peer.
    /// </summary>
    [HttpGet("report-documents")]
    public async Task<IActionResult> List(
        [FromQuery] long? suiteId, [FromQuery] long? runId, [FromQuery] int? take, CancellationToken ct,
        [FromQuery] string? comparison = null, [FromQuery] string? origin = null)
    {
        string? comparisonKey = null;
        if (comparison != null)
        {
            if (!BenchmarkReportComparisonKey.TryFromEntryKeys(comparison.Split(','), out var key))
            {
                return BadRequest(new { error = ComparisonError });
            }
            comparisonKey = key;
        }

        MobileGnollHackLogger.Data.BenchmarkReportDocumentOrigin? originFilter = null;
        if (origin != null)
        {
            if (string.Equals(origin, "reportPack", StringComparison.OrdinalIgnoreCase)) originFilter = MobileGnollHackLogger.Data.BenchmarkReportDocumentOrigin.ReportPack;
            else if (string.Equals(origin, "runCompletion", StringComparison.OrdinalIgnoreCase)) originFilter = MobileGnollHackLogger.Data.BenchmarkReportDocumentOrigin.RunCompletion;
            else return BadRequest(new { error = OriginError });
        }

        return Ok(await _renderService.ListAsync(new BenchmarkReportDocumentListFilter
        {
            SuiteId = suiteId,
            RunId = runId,
            ComparisonKey = comparisonKey,
            Origin = originFilter,
            Take = take
        }, ct));
    }

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
    /// With <paramref name="inline"/> the PDF is sent for viewing in the browser (<c>Content-Disposition:
    /// inline</c>) under the same name; otherwise it is an attachment.
    /// </summary>
    [HttpGet("report-documents/{id:long}/render/pdf")]
    public async Task<IActionResult> RenderPdf(
        long id, [FromQuery] string? disclosure, [FromQuery] string? peers, [FromQuery] string? paper, CancellationToken ct,
        [FromQuery] bool inline = false)
    {
        var (options, invalid) = ParseRenderOptions(disclosure, peers);
        if (invalid != null) return invalid;
        if (!BenchmarkPdfDocumentInfo.TryParsePaper(paper, out var pdfPaper))
        {
            return BadRequest(new { error = BenchmarkPdfDocumentInfo.PaperError });
        }

        var (markdown, document, notFound, refusal) = await _renderService.RenderWithDocumentAsync(id, ForNativeDocument(options!), ct);
        if (notFound) return NotFound();
        if (refusal != null) return BadRequest(new { error = refusal });

        if (BenchmarkPdfRenderer.IsTooLarge(markdown))
        {
            return StatusCode(413, new { error = BenchmarkPdfRenderer.TooLargeMessage(markdown!.Length) });
        }

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document!, options!, pdfPaper);
        byte[] pdf = await Task.Run(() => BenchmarkPdfRenderer.RenderMarkdown(markdown!, info, ct), ct);
        string name = BenchmarkPdfFileNames.ForReportDocument(document!, options!);

        if (inline)
        {
            // No download name on the result, so ASP.NET adds no attachment disposition of its own.
            Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = name }.ToString();
            return File(pdf, "application/pdf");
        }
        return File(pdf, "application/pdf", name);
    }

    /// <summary>
    /// The same document as a Word document on <c>a4</c> (the default) or <c>letter</c> paper, with the
    /// same validation, refusals and name as <see cref="RenderPdf"/>. Rendered by the static
    /// <see cref="BenchmarkWordRenderer"/>, so this path stays as free of model clients as the Markdown one.
    /// </summary>
    [HttpGet("report-documents/{id:long}/render/docx")]
    public async Task<IActionResult> RenderDocx(
        long id, [FromQuery] string? disclosure, [FromQuery] string? peers, [FromQuery] string? paper, CancellationToken ct)
    {
        var (options, invalid) = ParseRenderOptions(disclosure, peers);
        if (invalid != null) return invalid;
        if (!BenchmarkPdfDocumentInfo.TryParsePaper(paper, out var wordPaper))
        {
            return BadRequest(new { error = BenchmarkPdfDocumentInfo.PaperError });
        }

        var (markdown, document, notFound, refusal) = await _renderService.RenderWithDocumentAsync(id, ForNativeDocument(options!), ct);
        if (notFound) return NotFound();
        if (refusal != null) return BadRequest(new { error = refusal });

        if (BenchmarkWordRenderer.IsTooLarge(markdown))
        {
            return StatusCode(413, new { error = BenchmarkWordRenderer.TooLargeMessage(markdown!.Length) });
        }

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document!, options!, wordPaper);
        byte[] docx = await Task.Run(() => BenchmarkWordRenderer.RenderMarkdown(markdown!, info, ct), ct);

        return File(docx, BenchmarkWordRenderer.ContentType, BenchmarkPdfFileNames.ForReportDocument(document!, options!, "docx"));
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

    /// <summary>
    /// The options for a PDF or Word download, whose cover prints the stamp, the facts the front
    /// matter lists and the document ID, version, writer and provenance the footer states.
    /// </summary>
    private static BenchmarkReportRenderOptions ForNativeDocument(BenchmarkReportRenderOptions options) => new()
    {
        Disclosure = options.Disclosure,
        PeerNaming = options.PeerNaming,
        IncludeFrontMatter = false,
        IncludeDocumentFooter = false
    };
}
