namespace Overseer.Controllers;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
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
using Overseer.Services.Benchmarking.Pdf;
using Overseer.Services.Privacy;

/// <summary>
/// Report-pack generation, per model (one or several subjects) or comparison-wide over the covered
/// models: preview and layout preview (no model call), start, progress and cancel; and for a finished
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

    /// <summary>Rough output sizes per comparison-scope document, to be recalibrated after the first real jobs.</summary>
    private static readonly IReadOnlyDictionary<BenchmarkReportAudience, int> ComparisonEstimatedOutputTokens = new Dictionary<BenchmarkReportAudience, int>
    {
        [BenchmarkReportAudience.ExecutiveSummary] = 3000,
        [BenchmarkReportAudience.TechnicalReport] = 9500,
        [BenchmarkReportAudience.InternalBrief] = 9000
    };

    /// <summary>A prompt above this share of the writer configuration's context window is refused.</summary>
    public const double ContextWindowRefusalShare = 0.9;

    public const string AllWrittenMessage = "This run already has every AI-written report. Delete one first to write it again.";
    public const string InvalidAudienceMessage =
        "Only the Executive Summary, the Report for AI Researchers and Developers and the Internal Improvement Brief are written for a run.";

    public const string LayoutPreviewMissingRequest = "The layout preview needs its request field.";
    public const string LayoutPreviewInvalidRequest = "The request field is not a valid layout preview request.";
    public const string LayoutPreviewAudienceError = "audience must be 1 (Executive Summary), 2 (Report for AI Researchers and Developers) or 3 (Internal Improvement Brief).";
    public const string LayoutPreviewNamingError = "naming must be named or anonymized.";

    /// <summary>The layout preview's <c>request</c> field: the web defaults, enums by number or by name.</summary>
    private static readonly JsonSerializerOptions LayoutPreviewJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
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
    private readonly BenchmarkComparisonIdentityService _comparisonIdentity;
    private readonly ModelMetadataService _modelMetadata;

    public AdminBenchmarkReportPacksController(
        ApplicationDbContext db,
        BenchmarkReportPackJobManager jobManager,
        BenchmarkComplianceGuard complianceGuard,
        BenchmarkModelComparisonService comparisonService,
        ModelPricingService pricingService,
        EndpointPolicy endpointPolicy,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        BenchmarkRunReportDocumentService? runReportDocuments = null,
        BenchmarkComparisonIdentityService? comparisonIdentity = null,
        ModelMetadataService? modelMetadata = null)
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
        _comparisonIdentity = comparisonIdentity ?? new BenchmarkComparisonIdentityService(db, comparisonService);
        _modelMetadata = modelMetadata ?? new ModelMetadataService();
    }

    /// <summary>
    /// What a request would write, with no model call. Model scope: the first subject, its peers, the
    /// documents already written for this comparison and that subject, and each subject's documents; a
    /// subject with no peer is refused with no estimate. Comparison scope: the covered models with their
    /// letters, this covered set's documents, the comparison's other covered sets and its per-model
    /// documents. Both: the numbered comparison when it is identified, each document's estimated cost,
    /// prompt size and share of the writer's context window, the same-provider warning and any
    /// refusal. Computes the fact sheets and the prompts. Battery results mixed with runs or groups are
    /// a 400; a request the client aborts is a 499.
    /// </summary>
    [HttpPost("report-packs/preview")]
    public async Task<IActionResult> Preview([FromBody] BenchmarkReportPackRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "A request body is required." });
        if (BenchmarkReportPackPreparation.MixesSources(request)) return BadRequest(new { error = BenchmarkBatteryModelComparison.MixedSourcesError });

        try
        {
            return Ok(request.Scope == BenchmarkReportScope.Comparison
                ? await ComparisonPreviewAsync(request, ct)
                : await ModelPreviewAsync(request, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// One document as it would print, with no AI text: multipart, its <c>request</c> field a preview
    /// request with <c>audience</c>, <c>paper</c>, <c>naming</c>, <c>layout</c> and <c>charts</c>, and up
    /// to <see cref="BenchmarkReportLayoutPreview.MaxCharts"/> files named <c>&lt;figureKey&gt;.png</c>.
    /// Prepares the request as <see cref="Preview"/> does (a model-scope request, its first subject),
    /// builds the document in memory with placeholder text in the writer's slots and lists, and renders
    /// it as a PDF at Full disclosure with the charts placed by the layout. Makes no model call and
    /// stores nothing. 200 <c>application/pdf</c>; 400 for a missing or invalid request field, a mix of
    /// battery results with runs or groups, an unknown audience, paper or naming, an invalid layout, a
    /// chart or file refused as a chart upload would be, a file without a chart entry or the reverse,
    /// and a preparation refusal (a subject with no peer, a covered entry that is not in the comparison
    /// or is Excluded); 409 for fewer than two or more than twelve covered models; 413 for a document
    /// too large for a PDF; 499 when the client aborts.
    /// </summary>
    [HttpPost("report-packs/layout-preview")]
    [RequestSizeLimit(40_000_000)]
    public async Task<IActionResult> LayoutPreview(
        [FromForm(Name = "request")] string? request, [FromForm(Name = "files")] List<IFormFile>? files, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request)) return BadRequest(new { error = LayoutPreviewMissingRequest });

        BenchmarkReportLayoutPreviewRequest? body;
        try
        {
            body = JsonSerializer.Deserialize<BenchmarkReportLayoutPreviewRequest>(request, LayoutPreviewJson);
        }
        catch (JsonException)
        {
            body = null;
        }
        if (body == null) return BadRequest(new { error = LayoutPreviewInvalidRequest });

        if (BenchmarkReportPackPreparation.MixesSources(body)) return BadRequest(new { error = BenchmarkBatteryModelComparison.MixedSourcesError });
        if (!Enum.IsDefined(body.Audience)) return BadRequest(new { error = LayoutPreviewAudienceError });
        if (!BenchmarkPdfDocumentInfo.TryParsePaper(body.Paper, out var paper)) return BadRequest(new { error = BenchmarkPdfDocumentInfo.PaperError });
        if (!TryParseNaming(body.Naming, out var naming)) return BadRequest(new { error = LayoutPreviewNamingError });

        try
        {
            BenchmarkReportChartLayout? layout;
            IReadOnlyList<BenchmarkReportRenderChart> charts;
            try
            {
                layout = BenchmarkReportChartStore.ValidateLayout(body.Layout);
                charts = await LayoutPreviewChartsAsync(body.Charts, files, ct);
            }
            catch (ChartStoreException ex)
            {
                return BadRequest(new { error = ex.Message });
            }

            body.Audiences = new List<BenchmarkReportAudience> { body.Audience };
            int excerptChars = BenchmarkReportPackPreparation.AnswerExcerptChars(_configuration);
            var numbered = await FindComparisonAsync(body, ct);
            var (prep, refusal) = await BenchmarkReportPackPreparation.PrepareForRequestAsync(
                _db, _comparisonService, body, excerptChars, numbered?.Id, ct, _configuration, PairedTests());
            if (prep == null)
            {
                string message = refusal ?? "The document could not be prepared.";
                return message == BenchmarkComparisonReportFacts.TooFewRefusal || message == BenchmarkComparisonReportFacts.TooManyRefusal
                    ? Conflict(new { error = message })
                    : BadRequest(new { error = message });
            }
            if (prep.Scope == BenchmarkReportScope.Model && prep.Sheet.Peers.Count == 0)
            {
                return BadRequest(new { error = BenchmarkReportPackPreparation.PeerlessReportRefusal });
            }

            var writer = body.WriterModelConfigurationId > 0
                ? await _db.SystemAiApiConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == body.WriterModelConfigurationId, ct)
                : null;
            var document = BenchmarkReportLayoutPreview.BuildDocument(prep, body.Audience, numbered, writer, body, excerptChars, DateTime.UtcNow);
            byte[] pdf = await Task.Run(() => BenchmarkReportLayoutPreview.RenderPdf(document, naming, paper, charts, layout, ct), ct);
            return File(pdf, "application/pdf");
        }
        catch (BenchmarkPdfSourceTooLargeException ex)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = ex.Message });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary><c>named</c> (also when empty) or <c>anonymized</c>, ignoring case.</summary>
    private static bool TryParseNaming(string? value, out BenchmarkReportPeerNaming naming)
    {
        naming = BenchmarkReportPeerNaming.Named;
        if (string.IsNullOrWhiteSpace(value)) return true;
        switch (value.Trim().ToLowerInvariant())
        {
            case BenchmarkReportChartStore.Named:
                return true;
            case BenchmarkReportChartStore.Anonymized:
                naming = BenchmarkReportPeerNaming.Anonymized;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The layout preview's charts in request order, each checked as an uploaded chart is
    /// (<see cref="BenchmarkReportChartStore.ValidateRenderChart"/>) with its image from the file named
    /// <c>&lt;figureKey&gt;.png</c>. Refuses more than <see cref="BenchmarkReportLayoutPreview.MaxCharts"/>
    /// charts or files, a figure key or file name given twice, a file over the size limit, and a file
    /// that no chart names.
    /// </summary>
    private static async Task<IReadOnlyList<BenchmarkReportRenderChart>> LayoutPreviewChartsAsync(
        IReadOnlyList<BenchmarkReportLayoutPreviewChart>? charts, IReadOnlyList<IFormFile>? files, CancellationToken ct)
    {
        var entries = charts ?? Array.Empty<BenchmarkReportLayoutPreviewChart>();
        var parts = files ?? (IReadOnlyList<IFormFile>)Array.Empty<IFormFile>();
        int max = BenchmarkReportLayoutPreview.MaxCharts;
        if (entries.Count > max || parts.Count > max)
        {
            throw new ChartStoreException($"{Math.Max(entries.Count, parts.Count)} charts were sent; a layout preview takes at most {max}.");
        }

        var byName = new Dictionary<string, IFormFile>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            string name = Path.GetFileName(part.FileName ?? string.Empty);
            if (!byName.TryAdd(name, part))
            {
                throw new ChartStoreException($"The file \"{name}\" is sent more than once.");
            }
        }

        var result = new List<BenchmarkReportRenderChart>(entries.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i] ?? throw new ChartStoreException($"Chart {i + 1} is empty.");
            string key = entry.FigureKey ?? string.Empty;
            if (!seen.Add(key))
            {
                throw new ChartStoreException($"Chart {i + 1} ({key}) is sent more than once.");
            }

            byte[]? png = null;
            if (byName.Remove(key + ".png", out var file))
            {
                if (file.Length > BenchmarkReportChartStore.MaxPngBytes)
                {
                    throw new ChartStoreException(
                        $"Chart {i + 1} ({key}) is {file.Length:N0} bytes; the limit is {BenchmarkReportChartStore.MaxPngBytes:N0}.");
                }
                using var stream = file.OpenReadStream();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct);
                png = buffer.ToArray();
            }
            result.Add(BenchmarkReportChartStore.ValidateRenderChart(i, key, entry.Title, entry.Caption, entry.AltText, png));
        }

        if (byName.Count > 0)
        {
            throw new ChartStoreException(
                $"The file \"{byName.Keys.First()}\" belongs to no chart of the request; each file is named <figureKey>.png after one.");
        }
        return result;
    }

    private async Task<BenchmarkReportPackPreviewDto> ModelPreviewAsync(BenchmarkReportPackRequest request, CancellationToken ct)
    {
        int excerptChars = BenchmarkReportPackPreparation.AnswerExcerptChars(_configuration);
        var subjects = request.ModelSubjectKeys();
        string first = subjects.Count > 0 ? subjects[0] : request.SubjectKey ?? string.Empty;

        var (prep, refusal) = await BenchmarkReportPackPreparation.PrepareAsync(
            _db, _comparisonService, SubjectRequest(request, first), excerptChars, ct, _configuration);
        if (prep == null)
        {
            return new BenchmarkReportPackPreviewDto { SubjectKey = first, Refusal = refusal };
        }

        var preview = new BenchmarkReportPackPreviewDto
        {
            SubjectKey = prep.Subject.Key,
            SubjectLabel = prep.Sheet.SubjectLabel,
            SubjectState = prep.Sheet.SubjectState,
            SuiteName = prep.Sheet.SuiteName,
            Peers = PeerDtos(prep.Sheet),
            Scope = BenchmarkReportScope.Model,
            ComparisonEntryCount = prep.ComparisonEntryCount
        };
        var numbered = await FindComparisonAsync(request, ct);
        preview.ComparisonId = numbered?.Id;
        preview.ComparisonName = numbered?.DisplayName;

        if (preview.Peers.Count == 0)
        {
            preview.Refusal = BenchmarkReportPackPreparation.PeerlessReportRefusal;
            return preview;
        }
        preview.WrittenDocuments = await WrittenDocumentsAsync(prep.Subject.Key, request, ct);

        var preps = new List<BenchmarkReportPackPreparation> { prep };
        foreach (string key in subjects.Skip(1))
        {
            var (next, nextRefusal) = await BenchmarkReportPackPreparation.PrepareAsync(
                _db, _comparisonService, SubjectRequest(request, key), excerptChars, ct, _configuration, prep.Comparison);
            if (next == null)
            {
                preview.Refusal ??= nextRefusal;
                break;
            }
            if (!BenchmarkReportPackPreparation.HasPeers(next.Comparison, next.Subject))
            {
                preview.Refusal ??= BenchmarkReportPackPreparation.PeerlessReportRefusal;
                break;
            }
            preps.Add(next);
        }

        preview.CoveredModels = preps
            .Select(p => new BenchmarkReportCoveredModelDto { EntryKey = p.Subject.Key, Label = p.Subject.Label, Provider = p.Subject.Provider })
            .ToList();
        preview.SubjectDocuments = await SubjectDocumentsAsync(
            numbered?.Id, ComparisonKeyOf(request), preps.Select(p => (p.Subject.Key, p.Subject.Label)).ToList(), ct);

        SystemAiApiConfiguration? writer = null;
        if (request.WriterModelConfigurationId > 0)
        {
            writer = await _db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == request.WriterModelConfigurationId, ct);
            preview.WriterDisplayName = writer?.DisplayName;
            preview.WriterContextWindowTokens = ContextWindowOf(writer);
            preview.Refusal ??= preps.Select(p => WriterRefusal(writer, p.Subject)).FirstOrDefault(r => r != null);
            var sharing = preps.FirstOrDefault(p => _complianceGuard.IsSameProvider(writer?.Provider, p.Subject.Provider));
            if (preview.Refusal == null && sharing != null)
            {
                preview.SameProviderWarning = BenchmarkReportPackPreparation.SameProviderWarning(sharing.Subject, writer!);
            }
        }

        var audiences = (request.Audiences ?? new List<BenchmarkReportAudience>()).Distinct().OrderBy(a => a).ToList();
        foreach (var p in preps)
        {
            preview.Estimates.AddRange(EstimateAudiences(p, writer, audiences, p.Subject.Key));
        }
        preview.Refusal ??= ContextWindowRefusal(preview.Estimates, writer);
        preview.EstimatedTotalCostUsd = TotalCost(preview.Estimates);

        return preview;
    }

    private async Task<BenchmarkReportPackPreviewDto> ComparisonPreviewAsync(BenchmarkReportPackRequest request, CancellationToken ct)
    {
        var preview = new BenchmarkReportPackPreviewDto { Scope = BenchmarkReportScope.Comparison };

        var (comparison, error) = await _comparisonService.CompareAsync(BenchmarkReportPackPreparation.ComparisonRequest(request), ct);
        if (comparison == null)
        {
            preview.Refusal = error ?? "The comparison could not be computed.";
            return preview;
        }

        preview.ComparisonEntryCount = comparison.Entries.Count(e => !e.Excluded);
        var numbered = await FindComparisonAsync(request, ct);
        preview.ComparisonId = numbered?.Id;
        preview.ComparisonName = numbered?.DisplayName;
        string comparisonKey = ComparisonKeyOf(request);

        var covered = BenchmarkComparisonReportFacts.CoveredKeysOf(comparison, request.CoveredEntryKeys);
        string? refusal = BenchmarkComparisonReportFacts.CoveredRefusal(comparison, covered)
            ?? BenchmarkComparisonReportFacts.BoundsRefusal(covered.Count);
        if (refusal != null)
        {
            preview.Refusal = refusal;
            preview.OtherModelSets = await CoveredSetsAsync(numbered?.Id, comparisonKey, exceptSetKey: null, ct);
            return preview;
        }

        int excerptChars = BenchmarkReportPackPreparation.AnswerExcerptChars(_configuration);
        var (prep, prepRefusal) = await BenchmarkReportPackPreparation.PrepareComparisonAsync(
            _db, _comparisonService, request, excerptChars, numbered?.Id, ct, _configuration, PairedTests(), comparison);
        if (prep == null)
        {
            preview.Refusal = prepRefusal;
            preview.OtherModelSets = await CoveredSetsAsync(numbered?.Id, comparisonKey, exceptSetKey: null, ct);
            return preview;
        }

        preview.SubjectKey = prep.Sheet.SubjectKey;
        preview.SubjectLabel = prep.Sheet.SubjectLabel;
        preview.SubjectState = prep.Sheet.SubjectState;
        preview.SuiteName = prep.Sheet.SuiteName;
        preview.Peers = PeerDtos(prep.Sheet);
        preview.CoversAllEntries = prep.CoversAllEntries;
        preview.CoveredSetKey = prep.CoveredSetKey;
        preview.CoveredModels = prep.Sheet.Peers
            .OrderBy(p => p.Letter.Length).ThenBy(p => p.Letter, StringComparer.Ordinal)
            .Select(p => new BenchmarkReportCoveredModelDto { EntryKey = p.EntryKey, Label = p.Label, Provider = p.Provider, Letter = p.Letter })
            .ToList();
        preview.WrittenDocuments = await CoveredSetDocumentsAsync(numbered?.Id, comparisonKey, prep.CoveredSetKey, ct);
        preview.OtherModelSets = await CoveredSetsAsync(numbered?.Id, comparisonKey, prep.CoveredSetKey, ct);
        preview.SubjectDocuments = await SubjectDocumentsAsync(
            numbered?.Id, comparisonKey, prep.Covered.Select(e => (e.Key, e.Label)).ToList(), ct);

        SystemAiApiConfiguration? writer = null;
        if (request.WriterModelConfigurationId > 0)
        {
            writer = await _db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == request.WriterModelConfigurationId, ct);
            preview.WriterDisplayName = writer?.DisplayName;
            preview.WriterContextWindowTokens = ContextWindowOf(writer);
            preview.Refusal = ComparisonWriterRefusal(writer, prep.Covered);
            var sharing = SharingProvider(writer, prep.Covered);
            if (preview.Refusal == null && sharing.Count > 0)
            {
                preview.SameProviderWarning = BenchmarkReportPackPreparation.SameProviderWarning(sharing, writer!);
            }
        }

        var audiences = (request.Audiences ?? new List<BenchmarkReportAudience>()).Distinct().OrderBy(a => a).ToList();
        preview.Estimates.AddRange(EstimateAudiences(prep, writer, audiences, prep.Sheet.SubjectKey));
        preview.Refusal ??= ContextWindowRefusal(preview.Estimates, writer);
        preview.EstimatedTotalCostUsd = TotalCost(preview.Estimates);
        return preview;
    }

    /// <summary>
    /// Starts a job. Model scope, each subject checked in turn; refusals, in order: battery results
    /// mixed with runs or groups (400); unknown or Excluded subject (400); a subject with no peer
    /// (400); unusable writer (400); the writer is a subject's model (400); no document (400); a
    /// prompt above 90 % of the writer's context window (400); a document named in
    /// <c>replaceDocumentIds</c> that this job does not write again (400); a requested document already
    /// written for this comparison and subject and not named in <c>replaceDocumentIds</c> (409); spend
    /// cap (429); same provider, unacknowledged (409 with the warning); a job already running (409 with
    /// its state). Comparison scope: see <see cref="StartComparisonAsync"/>. The comparison is numbered
    /// before the job starts, so every document it writes belongs to it.
    /// </summary>
    [HttpPost("report-packs")]
    public async Task<IActionResult> Start([FromBody] BenchmarkReportPackRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "A request body is required." });
        if (BenchmarkReportPackPreparation.MixesSources(request)) return BadRequest(new { error = BenchmarkBatteryModelComparison.MixedSourcesError });
        if (request.Scope == BenchmarkReportScope.Comparison) return await StartComparisonAsync(request, ct);

        var keys = request.ModelSubjectKeys();
        string firstKey = keys.Count > 0 ? keys[0] : request.SubjectKey ?? string.Empty;
        var (comparison, firstSubject, refusal) = await BenchmarkReportPackPreparation.CompareAsync(_comparisonService, SubjectRequest(request, firstKey), ct);
        if (refusal != null) return BadRequest(new { error = refusal });

        var subjects = new List<BenchmarkModelComparisonEntryDto> { firstSubject! };
        foreach (string key in keys.Skip(1))
        {
            var (subject, subjectRefusal) = BenchmarkReportPackPreparation.SubjectOf(comparison!, key);
            if (subjectRefusal != null) return BadRequest(new { error = subjectRefusal });
            subjects.Add(subject!);
        }
        if (subjects.Any(s => !BenchmarkReportPackPreparation.HasPeers(comparison!, s)))
        {
            return BadRequest(new { error = BenchmarkReportPackPreparation.PeerlessReportRefusal });
        }

        var writer = await _db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == request.WriterModelConfigurationId, ct);
        string? writerRefusal = subjects.Select(s => WriterRefusal(writer, s)).FirstOrDefault(r => r != null);
        if (writerRefusal != null) return BadRequest(new { error = writerRefusal });

        var audiences = RequestedReportAudiences(request);
        if (audiences.Count == 0) return BadRequest(new { error = "Choose at least one document to write." });

        if (ContextWindowOf(writer) != null)
        {
            int excerptChars = BenchmarkReportPackPreparation.AnswerExcerptChars(_configuration);
            var estimates = new List<BenchmarkReportPackAudienceEstimateDto>();
            foreach (var subject in subjects)
            {
                var (prep, prepRefusal) = await BenchmarkReportPackPreparation.PrepareAsync(
                    _db, _comparisonService, SubjectRequest(request, subject.Key), excerptChars, ct, _configuration, comparison);
                if (prep == null) return BadRequest(new { error = prepRefusal });
                estimates.AddRange(EstimateAudiences(prep, writer, audiences, subject.Key));
            }
            if (ContextWindowRefusal(estimates, writer) is string contextRefusal) return BadRequest(new { error = contextRefusal });
        }

        var replaceIds = (request.ReplaceDocumentIds ?? new List<long>()).Distinct().ToList();
        string comparisonKey = ComparisonKeyOf(request);
        var replaceRefusal = await ReplaceRefusalAsync(replaceIds, audiences, comparisonKey,
            d => d.Scope == BenchmarkReportScope.Model && subjects.Any(s => s.Key == d.SubjectKey), ct);
        if (replaceRefusal != null) return BadRequest(new { error = replaceRefusal });

        foreach (var subject in subjects)
        {
            var written = (await WrittenDocumentsAsync(subject.Key, request, ct))
                .FirstOrDefault(d => audiences.Contains(d.Audience) && !replaceIds.Contains(d.DocumentId));
            if (written != null)
            {
                return Conflict(new
                {
                    error = $"The {BenchmarkReportRenderService.AudienceName(written.Audience)} about {subject.Label} is already written for this comparison. "
                        + "Delete it in step 4 to write it again."
                });
            }
        }

        var (canSpend, denialReason) = await _complianceGuard.CanSpendAsync(ct: ct);
        if (!canSpend) return StatusCode(StatusCodes.Status429TooManyRequests, denialReason);

        var sameProvider = subjects.FirstOrDefault(s => _complianceGuard.IsSameProvider(writer!.Provider, s.Provider));
        if (sameProvider != null && !request.AcknowledgeSameProvider)
        {
            return StatusCode(StatusCodes.Status409Conflict, new SameProviderWarningDto
            {
                SameProvider = true,
                Provider = sameProvider.Provider,
                TestedModelDisplayName = sameProvider.Label,
                AssessorModelDisplayName = writer!.DisplayName,
                Message = BenchmarkReportPackPreparation.SameProviderWarning(sameProvider, writer)
            });
        }

        var running = _jobManager.Current;
        if (running != null && running.Status == BenchmarkReportPackJobStatus.Running)
        {
            return StatusCode(StatusCodes.Status409Conflict, running.ToDto());
        }

        string startedByUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var (numbered, identityError) = await _comparisonIdentity.EnsureAsync(
            request.RunIds, request.GroupIds, request.BatteryRunIds, string.IsNullOrEmpty(startedByUserId) ? null : startedByUserId, ct);
        if (numbered == null) return BadRequest(new { error = identityError ?? "The comparison could not be numbered." });

        var job = NewJob(request, writer!, sameProvider != null, startedByUserId, numbered.Id, await SnapshotIdAsync(writer!, ct));
        job.Scope = BenchmarkReportScope.Model;
        job.SubjectKey = subjects[0].Key;
        job.SubjectLabel = subjects[0].Label;
        job.SuiteId = subjects[0].SuiteId;
        job.SuiteName = subjects[0].SuiteName ?? subjects[0].BatteryName ?? string.Empty;
        job.Request.Scope = BenchmarkReportScope.Model;
        job.Request.SubjectKey = subjects[0].Key;
        job.Request.SubjectKeys = subjects.Select(s => s.Key).ToList();
        job.Request.Audiences = audiences;
        job.Documents = subjects
            .SelectMany(s => audiences.Select(a => new BenchmarkReportPackDocumentProgress { Audience = a, SubjectKey = s.Key, SubjectLabel = s.Label }))
            .ToList();

        return Launch(job);
    }

    /// <summary>
    /// Starts a comparison-scope job over the covered entries. Refusals, in order: the comparison cannot
    /// be computed (400); a covered entry is not in the comparison or is Excluded (400); fewer than two
    /// or more than twelve covered models (409); unusable writer (400); the writer is a covered model,
    /// with its provider, model id and thinking level (400); no document (400); a prompt above 90 % of the
    /// writer's context window (400); a document named in <c>replaceDocumentIds</c> that this job does not
    /// write again (400); a requested document already written for this covered set and not named in
    /// <c>replaceDocumentIds</c> (409); spend cap (429); a writer sharing a covered model's provider,
    /// unacknowledged (409 with the warning naming those models); a job already running (409).
    /// </summary>
    private async Task<IActionResult> StartComparisonAsync(BenchmarkReportPackRequest request, CancellationToken ct)
    {
        var (comparison, error) = await _comparisonService.CompareAsync(BenchmarkReportPackPreparation.ComparisonRequest(request), ct);
        if (comparison == null) return BadRequest(new { error = error ?? "The comparison could not be computed." });

        var coveredKeys = BenchmarkComparisonReportFacts.CoveredKeysOf(comparison, request.CoveredEntryKeys);
        string? coveredRefusal = BenchmarkComparisonReportFacts.CoveredRefusal(comparison, coveredKeys);
        if (coveredRefusal != null) return BadRequest(new { error = coveredRefusal });
        string? bounds = BenchmarkComparisonReportFacts.BoundsRefusal(coveredKeys.Count);
        if (bounds != null) return Conflict(new { error = bounds });

        var covered = BenchmarkComparisonReportFacts.InLetterOrder(comparison.Entries.Where(e => coveredKeys.Contains(e.Key)));
        var writer = await _db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == request.WriterModelConfigurationId, ct);
        string? writerRefusal = ComparisonWriterRefusal(writer, covered);
        if (writerRefusal != null) return BadRequest(new { error = writerRefusal });

        var audiences = RequestedReportAudiences(request);
        if (audiences.Count == 0) return BadRequest(new { error = "Choose at least one document to write." });

        var numberedBefore = await FindComparisonAsync(request, ct);
        int excerptChars = BenchmarkReportPackPreparation.AnswerExcerptChars(_configuration);
        var (prep, prepRefusal) = await BenchmarkReportPackPreparation.PrepareComparisonAsync(
            _db, _comparisonService, request, excerptChars, numberedBefore?.Id, ct, _configuration, PairedTests(), comparison);
        if (prep == null) return BadRequest(new { error = prepRefusal });

        if (ContextWindowRefusal(EstimateAudiences(prep, writer, audiences, prep.Sheet.SubjectKey), writer) is string contextRefusal)
        {
            return BadRequest(new { error = contextRefusal });
        }

        var replaceIds = (request.ReplaceDocumentIds ?? new List<long>()).Distinct().ToList();
        string comparisonKey = ComparisonKeyOf(request);
        var replaceRefusal = await ReplaceRefusalAsync(replaceIds, audiences, comparisonKey,
            d => d.Scope == BenchmarkReportScope.Comparison && d.CoveredSetKey == prep.CoveredSetKey, ct);
        if (replaceRefusal != null) return BadRequest(new { error = replaceRefusal });

        var written = (await CoveredSetDocumentsAsync(numberedBefore?.Id, comparisonKey, prep.CoveredSetKey, ct))
            .FirstOrDefault(d => audiences.Contains(d.Audience) && !replaceIds.Contains(d.DocumentId));
        if (written != null)
        {
            return Conflict(new
            {
                error = $"The {BenchmarkReportRenderService.AudienceName(written.Audience)} of these models is already written for this comparison. "
                    + "Delete it, or rewrite it to replace it."
            });
        }

        var (canSpend, denialReason) = await _complianceGuard.CanSpendAsync(ct: ct);
        if (!canSpend) return StatusCode(StatusCodes.Status429TooManyRequests, denialReason);

        var sharing = SharingProvider(writer, covered);
        if (sharing.Count > 0 && !request.AcknowledgeSameProvider)
        {
            return StatusCode(StatusCodes.Status409Conflict, new SameProviderWarningDto
            {
                SameProvider = true,
                Provider = writer!.Provider,
                TestedModelDisplayName = BenchmarkReportFormat.LetterList(sharing.Select(e => e.Label).ToList()),
                AssessorModelDisplayName = writer.DisplayName,
                Message = BenchmarkReportPackPreparation.SameProviderWarning(sharing, writer)
            });
        }

        var running = _jobManager.Current;
        if (running != null && running.Status == BenchmarkReportPackJobStatus.Running)
        {
            return StatusCode(StatusCodes.Status409Conflict, running.ToDto());
        }

        string startedByUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var (numbered, identityError) = await _comparisonIdentity.EnsureAsync(
            request.RunIds, request.GroupIds, request.BatteryRunIds, string.IsNullOrEmpty(startedByUserId) ? null : startedByUserId, ct);
        if (numbered == null) return BadRequest(new { error = identityError ?? "The comparison could not be numbered." });

        string subjectKey = BenchmarkComparisonReportFacts.SubjectKeyOf(numbered.Id, prep.CoveredSetKey, prep.CoversAllEntries);
        string subjectLabel = BenchmarkComparisonReportFacts.SubjectLabelOf(numbered.Id, prep.CoversAllEntries, covered.Count, prep.ComparisonEntryCount);

        var job = NewJob(request, writer!, sharing.Count > 0, startedByUserId, numbered.Id, await SnapshotIdAsync(writer!, ct));
        job.Scope = BenchmarkReportScope.Comparison;
        job.SubjectKey = subjectKey;
        job.SubjectLabel = subjectLabel;
        job.SuiteId = prep.Sheet.SuiteId;
        job.SuiteName = prep.Sheet.SuiteName;
        job.Request.Scope = BenchmarkReportScope.Comparison;
        job.Request.CoveredEntryKeys = prep.CoveredEntryKeys.ToList();
        job.Request.Audiences = audiences;
        job.Documents = audiences
            .Select(a => new BenchmarkReportPackDocumentProgress { Audience = a, SubjectKey = subjectKey, SubjectLabel = subjectLabel })
            .ToList();

        return Launch(job);
    }

    /// <summary>A job of the request's sources and writer, holding the Report Pack slot once started.</summary>
    private static BenchmarkReportPackJob NewJob(
        BenchmarkReportPackRequest request, SystemAiApiConfiguration writer, bool sameProvider, string startedByUserId, int comparisonId, long snapshotId)
        => new()
        {
            ComparisonId = comparisonId,
            WriterConfigId = writer.Id,
            WriterDisplayName = writer.DisplayName,
            WriterSnapshotId = snapshotId,
            SameProviderAcknowledged = sameProvider && request.AcknowledgeSameProvider,
            Request = new BenchmarkReportPackRequest
            {
                RunIds = (request.RunIds ?? new List<long>()).ToList(),
                GroupIds = (request.GroupIds ?? new List<long>()).ToList(),
                BatteryRunIds = (request.BatteryRunIds ?? new List<long>()).ToList(),
                PricingBasis = request.PricingBasis,
                WriterModelConfigurationId = writer.Id,
                AcknowledgeSameProvider = request.AcknowledgeSameProvider,
                ReplaceDocumentIds = (request.ReplaceDocumentIds ?? new List<long>()).Distinct().ToList()
            },
            StartedByUserId = string.IsNullOrEmpty(startedByUserId) ? null : startedByUserId,
            Cts = new CancellationTokenSource()
        };

    private async Task<long> SnapshotIdAsync(SystemAiApiConfiguration writer, CancellationToken ct)
        => (await SystemAiConfigurationSnapshotStore.CaptureAndSaveAsync(_db, writer, ct)).Id;

    /// <summary>Takes the single Report Pack slot and runs the job from a fresh scope; 409 with the job holding the slot.</summary>
    private IActionResult Launch(BenchmarkReportPackJob job)
    {
        if (!_jobManager.TryStart(job, out var existing))
        {
            return StatusCode(StatusCodes.Status409Conflict, existing?.ToDto());
        }

        var cts = job.Cts;
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

    /// <summary>A model-scope request's copy for one subject.</summary>
    private static BenchmarkReportPackRequest SubjectRequest(BenchmarkReportPackRequest request, string subjectKey) => new()
    {
        RunIds = request.RunIds ?? new List<long>(),
        GroupIds = request.GroupIds ?? new List<long>(),
        BatteryRunIds = request.BatteryRunIds ?? new List<long>(),
        PricingBasis = request.PricingBasis,
        SubjectKey = subjectKey,
        Audiences = request.Audiences ?? new List<BenchmarkReportAudience>(),
        WriterModelConfigurationId = request.WriterModelConfigurationId,
        AcknowledgeSameProvider = request.AcknowledgeSameProvider,
        ReplaceDocumentIds = request.ReplaceDocumentIds
    };

    /// <summary>The request's defined audiences, distinct, in audience order.</summary>
    private static List<BenchmarkReportAudience> RequestedReportAudiences(BenchmarkReportPackRequest request)
        => (request.Audiences ?? new List<BenchmarkReportAudience>())
            .Where(a => Enum.IsDefined(a))
            .Distinct()
            .OrderBy(a => a)
            .ToList();

    private static List<BenchmarkReportPackPeerDto> PeerDtos(BenchmarkReportFactSheet sheet)
        => sheet.Peers.Select(p => new BenchmarkReportPackPeerDto
        {
            Letter = p.Letter,
            EntryKey = p.EntryKey,
            Label = p.Label,
            Provider = p.Provider,
            State = p.State
        }).ToList();

    /// <summary>The request's comparison key, as the stored rows were keyed (<see cref="BenchmarkReportComparisonKey.From"/>).</summary>
    private static string ComparisonKeyOf(BenchmarkReportPackRequest request)
        => BenchmarkReportComparisonKey.From(
            request.RunIds ?? new List<long>(), request.GroupIds ?? new List<long>(), request.BatteryRunIds ?? new List<long>());

    /// <summary>The numbered comparison of the request's sources when it has been identified; null before. Creates nothing.</summary>
    private async Task<BenchmarkComparison?> FindComparisonAsync(BenchmarkReportPackRequest request, CancellationToken ct)
    {
        string key = ComparisonKeyOf(request);
        return await _db.BenchmarkComparisons.AsNoTracking().FirstOrDefaultAsync(c => c.ComparisonKey == key, ct);
    }

    /// <summary>The paired-test service the wizard's Paired tests view uses, with price cards, without its response cache.</summary>
    private BenchmarkPairedTestsService PairedTests() => new(_db, _comparisonService, null, _pricingService);

    /// <summary>The writer configuration's context window in tokens from the model catalog; null when unknown.</summary>
    private int? ContextWindowOf(SystemAiApiConfiguration? writer)
    {
        if (writer == null || string.IsNullOrWhiteSpace(writer.ModelId)) return null;
        int size = _modelMetadata.GetMetadata(writer.Provider ?? string.Empty, writer.ModelId).ContextWindowSize;
        return size > 0 ? size : null;
    }

    /// <summary>
    /// Why a document's prompt is too large for the writer: its estimated input above
    /// <see cref="ContextWindowRefusalShare"/> of the writer's context window; null when every one fits
    /// or the window is unknown.
    /// </summary>
    private static string? ContextWindowRefusal(IEnumerable<BenchmarkReportPackAudienceEstimateDto> estimates, SystemAiApiConfiguration? writer)
    {
        var over = estimates.FirstOrDefault(e => e.ContextWindowShare is double share && share > ContextWindowRefusalShare);
        if (over == null) return null;

        string share = Math.Round(over.ContextWindowShare!.Value * 100, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        return $"The {BenchmarkReportRenderService.AudienceName(over.Audience)} prompt is about "
            + over.EstimatedInputTokens.ToString("#,0", CultureInfo.InvariantCulture) + " tokens, " + share
            + $" % of the context window of {writer?.DisplayName ?? "the writer"}; above "
            + Math.Round(ContextWindowRefusalShare * 100).ToString("0", CultureInfo.InvariantCulture)
            + " % it is not written. Cover fewer models, or choose a writer with a larger context window.";
    }

    /// <summary>The covered models whose provider the writer shares (trimmed, ignoring case), in letter order.</summary>
    private List<BenchmarkModelComparisonEntryDto> SharingProvider(SystemAiApiConfiguration? writer, IReadOnlyList<BenchmarkModelComparisonEntryDto> covered)
        => writer == null
            ? new List<BenchmarkModelComparisonEntryDto>()
            : covered.Where(e => _complianceGuard.IsSameProvider(writer.Provider, e.Provider)).ToList();

    /// <summary>Why the configuration cannot write any report document, or null when it can.</summary>
    private string? WriterUsabilityRefusal(SystemAiApiConfiguration? writer)
    {
        if (writer == null || !writer.IsEnabled || string.IsNullOrWhiteSpace(writer.EncryptedApiKey) || (writer.ModelRole & 4) != 4)
        {
            return "The selected writer model is invalid, disabled, missing an API key, or not configured with the Benchmark role.";
        }
        if (!_endpointPolicy.TryResolveStrict(writer.BaseUrl, writer.CustomHeadersJson, writer.ApiVersion, out _, out var endpointError))
        {
            return $"Configuration '{writer.DisplayName}': its custom endpoint is not allowed by the endpoint policy: {endpointError}";
        }
        return null;
    }

    /// <summary>Why the configuration cannot write this subject's documents, or null when it can.</summary>
    private string? WriterRefusal(SystemAiApiConfiguration? writer, BenchmarkModelComparisonEntryDto subject)
    {
        if (WriterUsabilityRefusal(writer) is string refusal) return refusal;
        if (_complianceGuard.IsSameModel(writer, BenchmarkReportPackPreparation.SubjectIdentity(subject)))
        {
            return $"{writer!.DisplayName} is the model under report and cannot write its own report. Choose a writer from another model, preferably another family.";
        }
        return null;
    }

    /// <summary>
    /// Why the configuration cannot write a comparison-scope document over <paramref name="covered"/>:
    /// unusable, or the same configuration as a covered model (provider, model id and thinking level);
    /// null when it can.
    /// </summary>
    private string? ComparisonWriterRefusal(SystemAiApiConfiguration? writer, IReadOnlyList<BenchmarkModelComparisonEntryDto> covered)
    {
        if (WriterUsabilityRefusal(writer) is string refusal) return refusal;
        if (BenchmarkReportPackPreparation.WriterAsCoveredModel(covered, writer!) is { } same)
        {
            return $"{writer!.DisplayName} is {same.Label}, a model this document covers, and cannot write about itself. Choose a writer from another model, preferably another family.";
        }
        return null;
    }

    /// <summary>
    /// Why a document named in <c>replaceDocumentIds</c> cannot be replaced by this job: it does not
    /// exist, or it is not a Report Pack document of this comparison, of a requested type and of the
    /// job's subjects or covered set (<paramref name="matches"/>); null when every one can.
    /// </summary>
    private async Task<string?> ReplaceRefusalAsync(
        IReadOnlyList<long> ids, IReadOnlyList<BenchmarkReportAudience> audiences, string comparisonKey,
        Func<(BenchmarkReportScope Scope, string SubjectKey, string? CoveredSetKey), bool> matches, CancellationToken ct)
    {
        if (ids.Count == 0) return null;

        var rows = await _db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(d => ids.Contains(d.Id))
            .Select(d => new { d.Id, d.Origin, d.Audience, d.ComparisonKey, d.Scope, d.SubjectKey, d.CoveredSetKey })
            .ToListAsync(ct);

        foreach (long id in ids)
        {
            var row = rows.FirstOrDefault(r => r.Id == id);
            if (row == null) return $"Document #{id.ToString(CultureInfo.InvariantCulture)} does not exist.";
            if (row.Origin != BenchmarkReportDocumentOrigin.ReportPack
                || !string.Equals(row.ComparisonKey, comparisonKey, StringComparison.Ordinal)
                || !audiences.Contains(row.Audience)
                || !matches((row.Scope, row.SubjectKey, row.CoveredSetKey)))
            {
                return $"Document #{id.ToString(CultureInfo.InvariantCulture)} is not one this job writes again: only a requested document of this comparison and of the same models can be replaced.";
            }
        }
        return null;
    }

    /// <summary>One stored document as a written-document row.</summary>
    private sealed record WrittenRow(
        long Id, BenchmarkReportAudience Audience, DateTime CreatedAtUtc, string? WriterDisplayName, string SubjectKey,
        BenchmarkReportDocumentStatus Status, string? WriterProvider, string? WriterModelId, string? WriterThinkingLevel,
        long DurationMs, decimal? CostUsd, string? CoveredSetKey);

    /// <summary>The newest row per audience, in audience order.</summary>
    private static List<BenchmarkReportPackWrittenDocumentDto> NewestPerAudience(IEnumerable<WrittenRow> rows)
        => rows
            .GroupBy(d => d.Audience)
            .Select(g => g.OrderByDescending(d => d.CreatedAtUtc).ThenByDescending(d => d.Id).First())
            .OrderBy(d => d.Audience)
            .Select(d => new BenchmarkReportPackWrittenDocumentDto
            {
                Audience = d.Audience,
                DocumentId = d.Id,
                CreatedAtUtc = d.CreatedAtUtc,
                WriterDisplayName = d.WriterDisplayName,
                SubjectKey = d.SubjectKey,
                Status = d.Status.ToString(),
                WriterProvider = d.WriterProvider,
                WriterModelId = d.WriterModelId,
                WriterThinkingLevel = d.WriterThinkingLevel,
                DurationMs = d.DurationMs,
                CostUsd = d.CostUsd
            })
            .ToList();

    /// <summary>The Report Pack documents of a comparison, by its number or else its key, as written-document rows.</summary>
    private IQueryable<BenchmarkReportDocument> ComparisonDocuments(int? comparisonId, string comparisonKey)
        => _db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(d => d.Origin == BenchmarkReportDocumentOrigin.ReportPack
                        && ((comparisonId != null && d.ComparisonId == comparisonId) || d.ComparisonKey == comparisonKey));

    private static IQueryable<WrittenRow> AsRows(IQueryable<BenchmarkReportDocument> documents)
        => documents.Select(d => new WrittenRow(
            d.Id, d.Audience, d.CreatedAtUtc, d.WriterDisplayName, d.SubjectKey, d.Status,
            d.WriterProvider, d.WriterModelId, d.WriterThinkingLevel, d.DurationMs, d.CostUsd, d.CoveredSetKey));

    /// <summary>
    /// The Report Pack documents stored for the subject in the comparison of the request's sources,
    /// keyed as the stored rows were (<see cref="BenchmarkReportComparisonKey.From"/>): the newest per
    /// audience, in audience order.
    /// </summary>
    private async Task<List<BenchmarkReportPackWrittenDocumentDto>> WrittenDocumentsAsync(
        string subjectKey, BenchmarkReportPackRequest request, CancellationToken ct)
    {
        string comparisonKey = ComparisonKeyOf(request);
        var stored = await AsRows(_db.BenchmarkReportDocuments
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(d => d.Origin == BenchmarkReportDocumentOrigin.ReportPack && d.SubjectKey == subjectKey && d.ComparisonKey == comparisonKey))
            .ToListAsync(ct);
        return NewestPerAudience(stored);
    }

    /// <summary>The comparison-scope documents of one covered set of the comparison: the newest per audience.</summary>
    private async Task<List<BenchmarkReportPackWrittenDocumentDto>> CoveredSetDocumentsAsync(
        int? comparisonId, string comparisonKey, string coveredSetKey, CancellationToken ct)
    {
        var stored = await AsRows(ComparisonDocuments(comparisonId, comparisonKey)
                .Where(d => d.Scope == BenchmarkReportScope.Comparison && d.CoveredSetKey == coveredSetKey))
            .ToListAsync(ct);
        return NewestPerAudience(stored);
    }

    /// <summary>
    /// Every covered set of the comparison with comparison-scope documents, but <paramref name="exceptSetKey"/>:
    /// its models, labeled from its newest document's fact sheet, and its newest document per audience.
    /// The set covering every entry comes first, then the others by their newest document, newest first.
    /// </summary>
    private async Task<List<BenchmarkReportPackCoveredSetDto>> CoveredSetsAsync(
        int? comparisonId, string comparisonKey, string? exceptSetKey, CancellationToken ct)
    {
        var stored = await AsRows(ComparisonDocuments(comparisonId, comparisonKey)
                .Where(d => d.Scope == BenchmarkReportScope.Comparison && d.CoveredSetKey != null))
            .ToListAsync(ct);

        var sets = new List<(BenchmarkReportPackCoveredSetDto Set, DateTime Newest)>();
        foreach (var group in stored.Where(d => d.CoveredSetKey != exceptSetKey).GroupBy(d => d.CoveredSetKey!))
        {
            var newest = group.OrderByDescending(d => d.CreatedAtUtc).ThenByDescending(d => d.Id).First();
            var source = await _db.BenchmarkReportDocuments
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(d => d.Id == newest.Id)
                .Select(d => new { d.FactsJson, d.CoveredEntryKeysJson })
                .FirstAsync(ct);
            var facts = BenchmarkReportRenderService.ReadFacts(source.FactsJson);

            sets.Add((new BenchmarkReportPackCoveredSetDto
            {
                CoveredSetKey = group.Key,
                SubjectKey = newest.SubjectKey,
                CoversAllEntries = facts.CoversAllEntries,
                CoveredModels = BenchmarkReportRenderService.CoveredModels(
                    BenchmarkReportScope.Comparison, newest.SubjectKey, null, source.CoveredEntryKeysJson, facts),
                Documents = NewestPerAudience(group)
            }, newest.CreatedAtUtc));
        }

        return sets
            .OrderBy(s => s.Set.CoversAllEntries ? 0 : 1)
            .ThenByDescending(s => s.Newest)
            .Select(s => s.Set)
            .ToList();
    }

    /// <summary>Each subject's per-model documents of the comparison, the newest per audience, in subject order.</summary>
    private async Task<List<BenchmarkReportPackSubjectDocumentsDto>> SubjectDocumentsAsync(
        int? comparisonId, string comparisonKey, IReadOnlyList<(string Key, string Label)> subjects, CancellationToken ct)
    {
        var keys = subjects.Select(s => s.Key).ToList();
        var stored = await AsRows(ComparisonDocuments(comparisonId, comparisonKey)
                .Where(d => d.Scope == BenchmarkReportScope.Model && keys.Contains(d.SubjectKey)))
            .ToListAsync(ct);

        return subjects
            .Select(s => new BenchmarkReportPackSubjectDocumentsDto
            {
                SubjectKey = s.Key,
                SubjectLabel = s.Label,
                Documents = NewestPerAudience(stored.Where(d => d.SubjectKey == s.Key))
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

    /// <summary>
    /// Each document's prompt size, estimated tokens, first-call cost and share of the writer's context
    /// window, for <paramref name="subjectKey"/>; comparison scope uses its own output sizes. Makes no
    /// model call.
    /// </summary>
    private BenchmarkReportPackAudienceEstimateDto[] EstimateAudiences(
        BenchmarkReportPackPreparation prep, SystemAiApiConfiguration? writer, IEnumerable<BenchmarkReportAudience> audiences,
        string? subjectKey = null)
    {
        int maxOutputTokens = BenchmarkReportPackPreparation.MaxOutputTokens(_configuration);
        var pricing = writer != null ? _pricingService.Resolve(writer) : null;
        int? window = ContextWindowOf(writer);
        var outputs = prep.Scope == BenchmarkReportScope.Comparison ? ComparisonEstimatedOutputTokens : EstimatedOutputTokens;
        return audiences.Select(audience =>
        {
            var prompt = BenchmarkReportPackPrompt.Build(audience, prep.Sheet, prep.Content);
            int chars = prompt.SystemPrompt.Length + prompt.UserMessage.Length;
            int input = (chars + 3) / 4;
            int output = Math.Min(maxOutputTokens, outputs[audience]);
            return new BenchmarkReportPackAudienceEstimateDto
            {
                Audience = audience,
                SubjectKey = subjectKey,
                PromptChars = chars,
                EstimatedInputTokens = input,
                EstimatedOutputTokens = output,
                EstimatedCostUsd = pricing == null ? null : (double)ModelPricingService.ComputeCost(pricing, input, output, 0, 0),
                ContextWindowShare = window is int size ? (double)input / size : null
            };
        }).ToArray();
    }

    /// <summary>The sum of the estimates' costs; null when there is none or a document has no price.</summary>
    private static double? TotalCost(IReadOnlyCollection<BenchmarkReportPackAudienceEstimateDto> estimates)
        => estimates.Count == 0 || estimates.Any(e => e.EstimatedCostUsd == null) ? null : estimates.Sum(e => e.EstimatedCostUsd!.Value);

    private static string EndpointRefusal(SystemAiApiConfiguration writer, string? endpointError)
        => $"Report writer configuration '{writer.DisplayName}': its custom endpoint is not allowed by the endpoint policy: {endpointError}";
}
