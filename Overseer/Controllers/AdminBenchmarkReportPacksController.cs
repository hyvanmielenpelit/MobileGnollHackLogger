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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.Pdf;
using Overseer.Services.Benchmarking.RootCause;
using Overseer.Services.Privacy;

/// <summary>
/// Report-pack generation, per model (one or several subjects) or comparison-wide over the covered
/// models: preview, stored documents and layout preview (no model call), start, progress and cancel; and for a finished
/// run's own run-completion documents, write on request, estimate, progress, cancel and delete, and the
/// controls its Root Cause Analysis would use. The
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
        // To be recalibrated after the first real Root Cause Analysis jobs.
        [BenchmarkReportAudience.RootCauseAnalysis] = 10000
    };

    /// <summary>Rough output sizes per comparison-scope document, to be recalibrated after the first real jobs.</summary>
    private static readonly IReadOnlyDictionary<BenchmarkReportAudience, int> ComparisonEstimatedOutputTokens = new Dictionary<BenchmarkReportAudience, int>
    {
        [BenchmarkReportAudience.ExecutiveSummary] = 3800,
        // To be recalibrated after the first real Root Cause Analysis jobs.
        [BenchmarkReportAudience.RootCauseAnalysis] = 14000
    };

    /// <summary>The audiences a document of each scope is written for, in audience order.</summary>
    private static readonly IReadOnlyDictionary<BenchmarkReportScope, BenchmarkReportAudience[]> ScopeAudiences =
        new Dictionary<BenchmarkReportScope, BenchmarkReportAudience[]>
        {
            [BenchmarkReportScope.Model] = new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.RootCauseAnalysis },
            [BenchmarkReportScope.Comparison] = new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.RootCauseAnalysis },
            [BenchmarkReportScope.ChatConsistency] = new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.ProviderIssueReport },
        };

    /// <summary>A prompt above this share of the writer configuration's context window is refused.</summary>
    public const double ContextWindowRefusalShare = 0.9;

    public const string AllWrittenMessage = "This run already has every AI-written report. Delete one first to write it again.";
    public const string InvalidAudienceMessage =
        "Only the Executive Summary and the Root Cause Analysis are written for a run.";

    public const string LayoutPreviewMissingRequest = "The layout preview needs its request field.";
    public const string LayoutPreviewInvalidRequest = "The request field is not a valid layout preview request.";
    public const string LayoutPreviewAudienceError = "audience must be 1 (Executive Summary) or 5 (Root Cause Analysis).";
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
    private readonly BenchmarkReportPackPreviewCache? _previewCache;
    private readonly BenchmarkReportDocumentFactsCache _factsCache;
    private readonly ILogger<AdminBenchmarkReportPacksController> _logger;

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
        ModelMetadataService? modelMetadata = null,
        BenchmarkReportPackPreviewCache? previewCache = null,
        BenchmarkReportDocumentFactsCache? factsCache = null,
        ILogger<AdminBenchmarkReportPacksController>? logger = null)
    {
        _previewCache = previewCache;
        _factsCache = factsCache ?? BenchmarkReportDocumentFactsCache.None;
        _logger = logger ?? NullLogger<AdminBenchmarkReportPacksController>.Instance;
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
    /// documents. Both: the numbered comparison when it is identified, every offered document's
    /// estimated cost, prompt size and share of the writer's context window, flagged when it is above
    /// <see cref="ContextWindowRefusalShare"/> (the client refuses from the checked documents; Start
    /// refuses over the requested ones), the totals over the requested documents, the same-provider
    /// warning and any other refusal. Computes the fact sheets and the prompts, or reads them from
    /// <see cref="BenchmarkReportPackPreviewCache"/>; a comparison-scope preparation runs once per cache
    /// key in a scope of its own, joined by any identical preview while it runs, and goes on to fill the
    /// cache when the request that started it is aborted, up to
    /// <c>Benchmark:ReportPack:PreviewPreparationTimeoutSeconds</c>. Battery results mixed with runs or
    /// groups, and a comparison-scope reference model that is not a covered model, are a 400; a request
    /// the client aborts is a 499.
    /// </summary>
    [HttpPost("report-packs/preview")]
    public async Task<IActionResult> Preview([FromBody] BenchmarkReportPackRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "A request body is required." });
        if (BenchmarkReportPackPreparation.MixesSources(request)) return BadRequest(new { error = BenchmarkBatteryModelComparison.MixedSourcesError });

        try
        {
            if (request.Scope != BenchmarkReportScope.Comparison) return Ok(await ModelPreviewAsync(request, ct));

            var preview = await ComparisonPreviewAsync(request, ct);
            return preview.Refusal == BenchmarkComparisonReportFacts.ReferenceRefusal
                ? BadRequest(new { error = preview.Refusal })
                : Ok(preview);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// The stored Report Pack documents of a preview request, with nothing prepared. Comparison scope:
    /// the documents of the covered set that <c>coveredEntryKeys</c> names (none without it) and the
    /// comparison's other covered sets. Model scope: each subject's per-model documents, labeled by its
    /// entry key. The lists are those <see cref="Preview"/> answers; computes no comparison, fact sheet
    /// or prompt, reads no writer and leaves <see cref="BenchmarkReportPackPreviewCache"/> alone. Battery
    /// results mixed with runs or groups are a 400; a request the client aborts is a 499.
    /// </summary>
    [HttpPost("report-packs/documents")]
    public async Task<IActionResult> Documents([FromBody] BenchmarkReportPackRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "A request body is required." });
        if (BenchmarkReportPackPreparation.MixesSources(request)) return BadRequest(new { error = BenchmarkBatteryModelComparison.MixedSourcesError });

        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var numbered = await FindComparisonAsync(request, ct);
            string comparisonKey = ComparisonKeyOf(request);
            var documents = new BenchmarkReportPackDocumentsDto { Scope = request.Scope, ComparisonId = numbered?.Id };
            int count;
            if (request.Scope == BenchmarkReportScope.Comparison)
            {
                if (request.CoveredEntryKeys is { Count: > 0 } covered
                    && BenchmarkReportComparisonKey.TryFromEntryKeys(covered, out string coveredSetKey))
                {
                    documents.CoveredSetKey = coveredSetKey;
                    documents.WrittenDocuments = await CoveredSetDocumentsAsync(numbered?.Id, comparisonKey, coveredSetKey, ct);
                }
                documents.OtherModelSets = await CoveredSetsAsync(numbered?.Id, comparisonKey, documents.CoveredSetKey, ct);
                count = documents.WrittenDocuments.Count + documents.OtherModelSets.Sum(s => s.Documents.Count);
            }
            else
            {
                documents.SubjectDocuments = await SubjectDocumentsAsync(
                    numbered?.Id, comparisonKey, request.ModelSubjectKeys().Select(key => (Key: key, Label: key)).ToList(), ct);
                count = documents.SubjectDocuments.Sum(s => s.Documents.Count);
            }

            _logger.LogInformation(
                "Report Pack {Scope} documents: {Count} listed in {Ms} ms.", request.Scope, count, watch.ElapsedMilliseconds);
            return Ok(documents);
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
    /// or is Excluded, a reference model that is not a covered model); 409 for fewer than two or more
    /// than twelve covered models; 413 for a document
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
        if (!ScopeAudiences[BenchmarkReportScope.Model].Contains(body.Audience)) return BadRequest(new { error = LayoutPreviewAudienceError });
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
                _db, _comparisonService, body, excerptChars, numbered?.Id, ct, _configuration, PairedTests(), ReleaseDate);
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
        var timings = new PreviewTimings();
        int excerptChars = BenchmarkReportPackPreparation.AnswerExcerptChars(_configuration);
        var subjects = request.ModelSubjectKeys();
        string first = subjects.Count > 0 ? subjects[0] : request.SubjectKey ?? string.Empty;

        var firstEntry = await ModelEntryAsync(request, first, excerptChars, comparison: null, timings, ct);
        if (firstEntry.Entry == null)
        {
            LogPreview(BenchmarkReportScope.Model, timings);
            return new BenchmarkReportPackPreviewDto { SubjectKey = first, Refusal = firstEntry.Refusal };
        }
        var entry = firstEntry.Entry;
        var comparison = firstEntry.Comparison;

        var preview = new BenchmarkReportPackPreviewDto
        {
            SubjectKey = entry.Subject.Key,
            SubjectLabel = entry.SubjectLabel,
            SubjectState = entry.SubjectState,
            SuiteName = entry.SuiteName,
            Peers = entry.Peers.ToList(),
            Scope = BenchmarkReportScope.Model,
            ComparisonEntryCount = entry.ComparisonEntryCount
        };
        var lists = System.Diagnostics.Stopwatch.StartNew();
        var numbered = await FindComparisonAsync(request, ct);
        preview.ComparisonId = numbered?.Id;
        preview.ComparisonName = numbered?.DisplayName;

        if (preview.Peers.Count == 0)
        {
            preview.Refusal = BenchmarkReportPackPreparation.PeerlessReportRefusal;
            timings.ListsMs += lists.ElapsedMilliseconds;
            LogPreview(BenchmarkReportScope.Model, timings);
            return preview;
        }
        preview.WrittenDocuments = await WrittenDocumentsAsync(entry.Subject.Key, request, ct);
        timings.ListsMs += lists.ElapsedMilliseconds;

        var entries = new List<BenchmarkReportPackPreviewEntry> { entry };
        foreach (string key in subjects.Skip(1))
        {
            var next = await ModelEntryAsync(request, key, excerptChars, comparison, timings, ct);
            comparison ??= next.Comparison;
            if (next.Entry == null)
            {
                preview.Refusal ??= next.Refusal;
                break;
            }
            if (!next.Entry.HasPeers)
            {
                preview.Refusal ??= BenchmarkReportPackPreparation.PeerlessReportRefusal;
                break;
            }
            entries.Add(next.Entry);
        }

        preview.CoveredModels = entries
            .Select(e => new BenchmarkReportCoveredModelDto { EntryKey = e.Subject.Key, Label = e.Subject.Label, Provider = e.Subject.Provider })
            .ToList();
        lists.Restart();
        preview.SubjectDocuments = await SubjectDocumentsAsync(
            numbered?.Id, ComparisonKeyOf(request), entries.Select(e => (e.Subject.Key, e.Subject.Label)).ToList(), ct);
        timings.ListsMs += lists.ElapsedMilliseconds;

        SystemAiApiConfiguration? writer = null;
        if (request.WriterModelConfigurationId > 0)
        {
            writer = await _db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == request.WriterModelConfigurationId, ct);
            preview.WriterDisplayName = writer?.DisplayName;
            preview.WriterContextWindowTokens = ContextWindowOf(writer);
            preview.Refusal ??= entries.Select(e => WriterRefusal(writer, e.Subject)).FirstOrDefault(r => r != null);
            var sharing = entries.FirstOrDefault(e => _complianceGuard.IsSameProvider(writer?.Provider, e.Subject.Provider));
            if (preview.Refusal == null && sharing != null)
            {
                preview.SameProviderWarning = BenchmarkReportPackPreparation.SameProviderWarning(sharing.Subject, writer!);
            }
        }

        var estimate = System.Diagnostics.Stopwatch.StartNew();
        var (audiences, audienceRefusal) = RequestedReportAudiences(request, BenchmarkReportScope.Model);
        preview.Refusal ??= audienceRefusal;
        if (audienceRefusal == null)
        {
            foreach (var subjectEntry in entries)
            {
                preview.Estimates.AddRange(EstimateAudiences(
                    BenchmarkReportScope.Model, subjectEntry.PromptChars, writer, OfferedAudiences(BenchmarkReportScope.Model), subjectEntry.Subject.Key));
            }
        }
        SetTotals(preview, audienceRefusal == null ? audiences : new List<BenchmarkReportAudience>());
        timings.EstimateMs += estimate.ElapsedMilliseconds;

        LogPreview(BenchmarkReportScope.Model, timings);
        return preview;
    }

    private async Task<BenchmarkReportPackPreviewDto> ComparisonPreviewAsync(BenchmarkReportPackRequest request, CancellationToken ct)
    {
        var timings = new PreviewTimings();
        var preview = new BenchmarkReportPackPreviewDto { Scope = BenchmarkReportScope.Comparison };
        int excerptChars = BenchmarkReportPackPreparation.AnswerExcerptChars(_configuration);
        var numbered = await FindComparisonAsync(request, ct);
        string comparisonKey = ComparisonKeyOf(request);

        var stage = System.Diagnostics.Stopwatch.StartNew();
        string? cacheKey = _previewCache == null
            ? null
            : await BenchmarkReportPackPreviewCache.KeyAsync(_db, request, null, excerptChars, numbered?.Id, _configuration, ct);
        BenchmarkReportPackPreviewEntry? entry = null;
        if (cacheKey != null && _previewCache!.TryGet(cacheKey, out var cached)) entry = cached;
        timings.Hit = cacheKey == null ? null : entry != null;
        timings.KeyMs = stage.ElapsedMilliseconds;

        if (entry == null)
        {
            int? comparisonId = numbered?.Id;
            BenchmarkReportPackPreviewPreparationResult prepared;
            if (cacheKey == null)
            {
                prepared = await PrepareComparisonPreviewAsync(
                    _db, _comparisonService, PairedTests(), _configuration, ReleaseDate, request, excerptChars, comparisonId, ct);
            }
            else
            {
                // The preparation may outlive this request: it reads nothing request-scoped.
                var scopeFactory = _scopeFactory;
                var configuration = _configuration;
                var metadata = _modelMetadata;
                prepared = await _previewCache!.GetOrPrepareAsync(
                    cacheKey,
                    token => PrepareComparisonPreviewInScopeAsync(scopeFactory, configuration, metadata, request, excerptChars, comparisonId, token),
                    PreviewPreparationTimeout(),
                    ct);
            }

            if (prepared.Cached) timings.Hit = true;
            timings.Joined = prepared.Joined;
            timings.CompareMs = prepared.CompareMs;
            timings.PrepareMs = prepared.PrepareMs;
            timings.PairedMs = prepared.Timings?.PairedMs ?? 0;
            timings.LoadMs = prepared.Timings?.LoadMs ?? 0;
            timings.FactsMs = prepared.Timings?.FactsMs ?? 0;
            timings.PromptMs = prepared.Timings?.PromptMs ?? 0;
            if (prepared.ComparableEntryCount is int comparable)
            {
                preview.ComparisonEntryCount = comparable;
                preview.ComparisonId = numbered?.Id;
                preview.ComparisonName = numbered?.DisplayName;
            }

            if (prepared.Entry == null)
            {
                preview.Refusal = prepared.Refusal ?? "The comparison could not be computed.";
                if (prepared.CoveredRefusal)
                {
                    preview.OtherModelSets = await CoveredSetsAsync(numbered?.Id, comparisonKey, exceptSetKey: null, ct);
                }
                LogPreview(BenchmarkReportScope.Comparison, timings);
                return preview;
            }
            entry = prepared.Entry;
        }
        else
        {
            preview.ComparisonEntryCount = entry.ComparableEntryCount;
            preview.ComparisonId = numbered?.Id;
            preview.ComparisonName = numbered?.DisplayName;
        }

        preview.SubjectKey = entry.SheetSubjectKey;
        preview.SubjectLabel = entry.SubjectLabel;
        preview.SubjectState = entry.SubjectState;
        preview.SuiteName = entry.SuiteName;
        preview.Peers = entry.Peers.ToList();
        preview.CoversAllEntries = entry.CoversAllEntries;
        preview.CoveredSetKey = entry.CoveredSetKey;
        preview.CoveredModels = entry.CoveredModels.ToList();

        stage.Restart();
        preview.WrittenDocuments = await CoveredSetDocumentsAsync(numbered?.Id, comparisonKey, entry.CoveredSetKey, ct);
        preview.OtherModelSets = await CoveredSetsAsync(numbered?.Id, comparisonKey, entry.CoveredSetKey, ct);
        preview.SubjectDocuments = await SubjectDocumentsAsync(
            numbered?.Id, comparisonKey, entry.Covered.Select(e => (e.Key, e.Label)).ToList(), ct);
        timings.ListsMs = stage.ElapsedMilliseconds;

        SystemAiApiConfiguration? writer = null;
        if (request.WriterModelConfigurationId > 0)
        {
            writer = await _db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == request.WriterModelConfigurationId, ct);
            preview.WriterDisplayName = writer?.DisplayName;
            preview.WriterContextWindowTokens = ContextWindowOf(writer);
            preview.Refusal = ComparisonWriterRefusal(writer, entry.Covered);
            var sharing = SharingProvider(writer, entry.Covered);
            if (preview.Refusal == null && sharing.Count > 0)
            {
                preview.SameProviderWarning = BenchmarkReportPackPreparation.SameProviderWarning(sharing, writer!);
            }
        }

        stage.Restart();
        var (audiences, audienceRefusal) = RequestedReportAudiences(request, BenchmarkReportScope.Comparison);
        preview.Refusal ??= audienceRefusal;
        if (audienceRefusal == null)
        {
            preview.Estimates.AddRange(EstimateAudiences(
                BenchmarkReportScope.Comparison, entry.PromptChars, writer, OfferedAudiences(BenchmarkReportScope.Comparison), entry.SheetSubjectKey));
        }
        SetTotals(preview, audienceRefusal == null ? audiences : new List<BenchmarkReportAudience>());
        timings.EstimateMs = stage.ElapsedMilliseconds;

        LogPreview(BenchmarkReportScope.Comparison, timings);
        return preview;
    }

    /// <summary>
    /// A comparison-scope preview's cold preparation on <paramref name="db"/>: the comparison, the
    /// covered-model, bounds and reference refusals, then the preparation and its preview entry, with
    /// each stage's milliseconds. Reads nothing but its arguments.
    /// </summary>
    private static async Task<BenchmarkReportPackPreviewPreparationResult> PrepareComparisonPreviewAsync(
        ApplicationDbContext db,
        BenchmarkModelComparisonService comparisonService,
        BenchmarkPairedTestsService pairedTests,
        IConfiguration configuration,
        Func<string, string, string?> releaseDate,
        BenchmarkReportPackRequest request,
        int excerptChars,
        int? comparisonId,
        CancellationToken ct)
    {
        var stage = System.Diagnostics.Stopwatch.StartNew();
        var (comparison, error) = await comparisonService.CompareAsync(BenchmarkReportPackPreparation.ComparisonRequest(request), ct);
        long compareMs = stage.ElapsedMilliseconds;
        if (comparison == null)
        {
            return new BenchmarkReportPackPreviewPreparationResult
            {
                Refusal = error ?? "The comparison could not be computed.",
                CompareMs = compareMs
            };
        }

        int comparable = comparison.Entries.Count(e => !e.Excluded);
        var covered = BenchmarkComparisonReportFacts.CoveredKeysOf(comparison, request.CoveredEntryKeys);
        string? refusal = BenchmarkComparisonReportFacts.CoveredRefusal(comparison, covered)
            ?? BenchmarkComparisonReportFacts.BoundsRefusal(covered.Count)
            ?? BenchmarkComparisonReportFacts.ReferenceRefusalOf(comparison, covered, request.ReferenceEntryKey);
        if (refusal != null)
        {
            return new BenchmarkReportPackPreviewPreparationResult
            {
                Refusal = refusal,
                ComparableEntryCount = comparable,
                CoveredRefusal = true,
                CompareMs = compareMs
            };
        }

        stage.Restart();
        var timings = new BenchmarkReportPackPrepareTimings();
        var (prep, prepRefusal) = await BenchmarkReportPackPreparation.PrepareComparisonAsync(
            db, comparisonService, request, excerptChars, comparisonId, ct, configuration, pairedTests, comparison, releaseDate, timings);
        if (prep == null)
        {
            return new BenchmarkReportPackPreviewPreparationResult
            {
                Refusal = prepRefusal,
                ComparableEntryCount = comparable,
                CoveredRefusal = true,
                CompareMs = compareMs,
                PrepareMs = stage.ElapsedMilliseconds,
                Timings = timings
            };
        }

        var entry = PreviewEntryOf(prep, timings);
        return new BenchmarkReportPackPreviewPreparationResult
        {
            Entry = entry,
            ComparableEntryCount = comparable,
            CompareMs = compareMs,
            PrepareMs = stage.ElapsedMilliseconds,
            Timings = timings
        };
    }

    /// <summary>
    /// <see cref="PrepareComparisonPreviewAsync"/> in a scope of its own, with its own database context,
    /// comparison service and price cards, so it can go on after the request that started it is gone.
    /// </summary>
    private static async Task<BenchmarkReportPackPreviewPreparationResult> PrepareComparisonPreviewInScopeAsync(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ModelMetadataService metadata,
        BenchmarkReportPackRequest request,
        int excerptChars,
        int? comparisonId,
        CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var comparisonService = services.GetRequiredService<BenchmarkModelComparisonService>();
        var pairedTests = new BenchmarkPairedTestsService(db, comparisonService, null, services.GetRequiredService<ModelPricingService>());
        return await PrepareComparisonPreviewAsync(
            db, comparisonService, pairedTests, configuration, (p, m) => metadata.GetMetadata(p, m).ReleaseDate,
            request, excerptChars, comparisonId, ct);
    }

    /// <summary>How long a comparison-scope preview preparation may run: <c>Benchmark:ReportPack:PreviewPreparationTimeoutSeconds</c>, 180 s by default.</summary>
    private TimeSpan PreviewPreparationTimeout()
        => TimeSpan.FromSeconds(Math.Max(0,
            _configuration.GetValue<int?>("Benchmark:ReportPack:PreviewPreparationTimeoutSeconds")
                ?? BenchmarkReportPackPreviewCache.DefaultPreparationTimeoutSeconds));

    /// <summary>
    /// The milliseconds of one preview's stages, the comparison-scope preparation's own stages among them,
    /// whether its preparations came from the cache, and whether it joined a preparation already in flight.
    /// </summary>
    private sealed class PreviewTimings
    {
        public bool? Hit { get; set; }
        public long KeyMs { get; set; }
        public long CompareMs { get; set; }
        public long PrepareMs { get; set; }
        public long PairedMs { get; set; }
        public long LoadMs { get; set; }
        public long FactsMs { get; set; }
        public long PromptMs { get; set; }
        public bool Joined { get; set; }
        public long ListsMs { get; set; }
        public long EstimateMs { get; set; }
    }

    private void LogPreview(BenchmarkReportScope scope, PreviewTimings timings)
        => _logger.LogInformation(
            "Report Pack {Scope} preview: preparation cache {CacheResult}; key {KeyMs} ms, compare {CompareMs} ms, preparation {PrepareMs} ms (paired {PairedMs}, load {LoadMs}, facts {FactsMs}, prompts {PromptMs}), joined {Joined}, lists {ListsMs} ms, estimate {EstimateMs} ms.",
            scope, timings.Hit switch { true => "hit", false => "miss", null => "not used" },
            timings.KeyMs, timings.CompareMs, timings.PrepareMs, timings.PairedMs, timings.LoadMs, timings.FactsMs, timings.PromptMs,
            timings.Joined, timings.ListsMs, timings.EstimateMs);

    /// <summary>
    /// A model-scope subject's preview entry: from the preparation cache, else prepared over
    /// <paramref name="comparison"/> (computed when it is null) and stored there. Returns the refusal
    /// instead when the subject cannot be prepared, and the comparison it computed, if any.
    /// </summary>
    private async Task<(BenchmarkReportPackPreviewEntry? Entry, string? Refusal, BenchmarkModelComparisonDto? Comparison)> ModelEntryAsync(
        BenchmarkReportPackRequest request, string subjectKey, int excerptChars, BenchmarkModelComparisonDto? comparison,
        PreviewTimings timings, CancellationToken ct)
    {
        var subjectRequest = SubjectRequest(request, subjectKey);
        var stage = System.Diagnostics.Stopwatch.StartNew();
        string? cacheKey = null;
        if (_previewCache != null)
        {
            cacheKey = await BenchmarkReportPackPreviewCache.KeyAsync(_db, subjectRequest, subjectKey, excerptChars, null, _configuration, ct);
            bool hit = _previewCache.TryGet(cacheKey, out var cached);
            timings.Hit = (timings.Hit ?? true) && hit;
            timings.KeyMs += stage.ElapsedMilliseconds;
            if (hit) return (cached, null, comparison);
        }

        if (comparison == null)
        {
            stage.Restart();
            var (computed, _, compareRefusal) = await BenchmarkReportPackPreparation.CompareAsync(_comparisonService, subjectRequest, ct);
            timings.CompareMs += stage.ElapsedMilliseconds;
            if (computed == null) return (null, compareRefusal, null);
            comparison = computed;
        }

        stage.Restart();
        var (prep, refusal) = await BenchmarkReportPackPreparation.PrepareAsync(
            _db, _comparisonService, subjectRequest, excerptChars, ct, _configuration, comparison);
        if (prep == null)
        {
            timings.PrepareMs += stage.ElapsedMilliseconds;
            return (null, refusal, comparison);
        }

        var prepTimings = new BenchmarkReportPackPrepareTimings();
        var entry = PreviewEntryOf(prep, prepTimings);
        timings.PrepareMs += stage.ElapsedMilliseconds;
        timings.PromptMs += prepTimings.PromptMs;
        if (cacheKey != null) _previewCache!.Set(cacheKey, entry);
        return (entry, null, comparison);
    }

    /// <summary>
    /// A comparison-scope preparation's preview entry for Start's pre-flight: from the preparation cache,
    /// else prepared over <paramref name="comparison"/> and stored there; the refusal instead when it
    /// cannot be prepared.
    /// </summary>
    private async Task<(BenchmarkReportPackPreviewEntry? Entry, string? Refusal)> ComparisonEntryAsync(
        BenchmarkReportPackRequest request, BenchmarkModelComparisonDto comparison, int excerptChars, int? comparisonId, CancellationToken ct)
    {
        string? cacheKey = null;
        if (_previewCache != null)
        {
            cacheKey = await BenchmarkReportPackPreviewCache.KeyAsync(_db, request, null, excerptChars, comparisonId, _configuration, ct);
            if (_previewCache.TryGet(cacheKey, out var cached)) return (cached, null);
        }

        var (prep, refusal) = await BenchmarkReportPackPreparation.PrepareComparisonAsync(
            _db, _comparisonService, request, excerptChars, comparisonId, ct, _configuration, PairedTests(), comparison, ReleaseDate);
        if (prep == null) return (null, refusal);

        var entry = PreviewEntryOf(prep);
        if (cacheKey != null) _previewCache!.Set(cacheKey, entry);
        return (entry, null);
    }

    /// <summary>
    /// What the preview and Start's pre-flight read from a preparation, with each offered document's
    /// prompt length; <paramref name="timings"/>, when given, receives the prompt builds' milliseconds.
    /// </summary>
    private static BenchmarkReportPackPreviewEntry PreviewEntryOf(BenchmarkReportPackPreparation prep, BenchmarkReportPackPrepareTimings? timings = null)
    {
        var prompts = System.Diagnostics.Stopwatch.StartNew();
        var promptChars = OfferedAudiences(prep.Scope).ToDictionary(a => a, a => PromptCharsOf(prep, a));
        if (timings != null) timings.PromptMs = prompts.ElapsedMilliseconds;
        return new()
        {
            Scope = prep.Scope,
            Subject = prep.Subject,
            HasPeers = BenchmarkReportPackPreparation.HasPeers(prep.Comparison, prep.Subject),
            SheetSubjectKey = prep.Sheet.SubjectKey,
            SubjectLabel = prep.Sheet.SubjectLabel,
            SubjectState = prep.Sheet.SubjectState,
            SuiteId = prep.Sheet.SuiteId,
            SuiteName = prep.Sheet.SuiteName,
            Peers = PeerDtos(prep.Sheet),
            CoveredModels = prep.Sheet.Peers
                .OrderBy(p => p.Letter.Length).ThenBy(p => p.Letter, StringComparer.Ordinal)
                .Select(p => new BenchmarkReportCoveredModelDto { EntryKey = p.EntryKey, Label = p.Label, Provider = p.Provider, Letter = p.Letter })
                .ToList(),
            Covered = prep.Covered,
            CoveredEntryKeys = prep.CoveredEntryKeys,
            CoveredSetKey = prep.CoveredSetKey,
            CoversAllEntries = prep.CoversAllEntries,
            ComparisonEntryCount = prep.ComparisonEntryCount,
            ComparableEntryCount = prep.Comparison.Entries.Count(e => !e.Excluded),
            ReferenceEntryKey = prep.ReferenceEntryKey,
            PromptChars = promptChars
        };
    }

    /// <summary>The audiences a document of the scope is written for, in audience order.</summary>
    private static IReadOnlyList<BenchmarkReportAudience> OfferedAudiences(BenchmarkReportScope scope)
        => ScopeAudiences.TryGetValue(scope, out var list) ? list : Array.Empty<BenchmarkReportAudience>();

    /// <summary>The length of a document's writer prompt, system prompt plus user message, in characters.</summary>
    private static int PromptCharsOf(BenchmarkReportPackPreparation prep, BenchmarkReportAudience audience)
    {
        var prompt = BenchmarkReportPackPrompt.Build(audience, prep.Sheet, prep.Content);
        return prompt.SystemPrompt.Length + prompt.UserMessage.Length;
    }

    /// <summary>The preview's totals over the estimates of <paramref name="requested"/>, the documents the request would write.</summary>
    private static void SetTotals(BenchmarkReportPackPreviewDto preview, IReadOnlyCollection<BenchmarkReportAudience> requested)
    {
        var written = preview.Estimates.Where(e => requested.Contains(e.Audience)).ToList();
        preview.EstimatedTotalCostUsd = TotalCost(written);
        preview.EstimatedTotalCostWithRepairUsd = TotalCostWithRepair(written);
    }

    /// <summary>
    /// Starts a job. Model scope, each subject checked in turn; refusals, in order: battery results
    /// mixed with runs or groups (400); unknown or Excluded subject (400); a subject with no peer
    /// (400); unusable writer (400); the writer is a subject's model (400); a document the scope does
    /// not write (400); no document (400); a prompt above 90 % of the writer's context window (400); a document named in
    /// <c>replaceDocumentIds</c> that this job does not write again (400); a requested document already
    /// written for this comparison and subject and not named in <c>replaceDocumentIds</c> (409); spend
    /// cap (429); same provider, unacknowledged (409 with the warning); a job already running (409 with
    /// its state). Comparison scope: see <see cref="StartComparisonAsync"/>. The comparison is numbered
    /// before the job starts, so every document it writes belongs to it. A request the client aborts is
    /// a 499.
    /// </summary>
    [HttpPost("report-packs")]
    public async Task<IActionResult> Start([FromBody] BenchmarkReportPackRequest request, CancellationToken ct)
    {
        try
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

            var (audiences, audienceRefusal) = RequestedReportAudiences(request, BenchmarkReportScope.Model);
            if (audienceRefusal != null) return BadRequest(new { error = audienceRefusal });
            if (audiences.Count == 0) return BadRequest(new { error = "Choose at least one document to write." });

            if (ContextWindowOf(writer) != null)
            {
                int excerptChars = BenchmarkReportPackPreparation.AnswerExcerptChars(_configuration);
                var estimates = new List<BenchmarkReportPackAudienceEstimateDto>();
                var timings = new PreviewTimings();
                foreach (var subject in subjects)
                {
                    var (entry, prepRefusal, _) = await ModelEntryAsync(request, subject.Key, excerptChars, comparison, timings, ct);
                    if (entry == null) return BadRequest(new { error = prepRefusal });
                    estimates.AddRange(EstimateAudiences(BenchmarkReportScope.Model, entry.PromptChars, writer, audiences, subject.Key));
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Starts a comparison-scope job over the covered entries, against the request's reference model.
    /// Refusals, in order: the comparison cannot be computed (400); a covered entry is not in the
    /// comparison or is Excluded (400); fewer than two or more than twelve covered models (409); a
    /// reference model that is not a covered model (400); unusable writer (400); the writer is a covered model,
    /// with its provider, model id and thinking level (400); a document the scope does not write (400);
    /// no document (400); a prompt above 90 % of the
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
        string? referenceRefusal = BenchmarkComparisonReportFacts.ReferenceRefusalOf(comparison, coveredKeys, request.ReferenceEntryKey);
        if (referenceRefusal != null) return BadRequest(new { error = referenceRefusal });

        var covered = BenchmarkComparisonReportFacts.InLetterOrder(comparison.Entries.Where(e => coveredKeys.Contains(e.Key)));
        var writer = await _db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == request.WriterModelConfigurationId, ct);
        string? writerRefusal = ComparisonWriterRefusal(writer, covered);
        if (writerRefusal != null) return BadRequest(new { error = writerRefusal });

        var (audiences, audienceRefusal) = RequestedReportAudiences(request, BenchmarkReportScope.Comparison);
        if (audienceRefusal != null) return BadRequest(new { error = audienceRefusal });
        if (audiences.Count == 0) return BadRequest(new { error = "Choose at least one document to write." });

        var numberedBefore = await FindComparisonAsync(request, ct);
        int excerptChars = BenchmarkReportPackPreparation.AnswerExcerptChars(_configuration);
        var (prep, prepRefusal) = await ComparisonEntryAsync(request, comparison, excerptChars, numberedBefore?.Id, ct);
        if (prep == null) return BadRequest(new { error = prepRefusal });

        if (ContextWindowRefusal(EstimateAudiences(BenchmarkReportScope.Comparison, prep.PromptChars, writer, audiences, prep.SheetSubjectKey), writer) is string contextRefusal)
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
        job.SuiteId = prep.SuiteId;
        job.SuiteName = prep.SuiteName;
        job.Request.Scope = BenchmarkReportScope.Comparison;
        job.Request.CoveredEntryKeys = prep.CoveredEntryKeys.ToList();
        job.Request.Audiences = audiences;
        job.Documents = audiences
            .Select(a => new BenchmarkReportPackDocumentProgress { Audience = a, SubjectKey = subjectKey, SubjectLabel = subjectLabel })
            .ToList();

        return Launch(job);
    }

    /// <summary>A job of the request's sources, reference model and writer, holding the Report Pack slot once started.</summary>
    internal static BenchmarkReportPackJob NewJob(
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
                ReferenceEntryKey = string.IsNullOrWhiteSpace(request.ReferenceEntryKey) ? null : request.ReferenceEntryKey.Trim(),
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
    /// or Writing (409); a requested document that is not a run-completion document (400); a
    /// <c>replaceDocumentIds</c> entry that is not this run's run-completion document of a requested
    /// audience (400); a requested document already written and not replaced (409), or with none
    /// requested, every one written and none replaced (409); an unusable writer or
    /// the model under test (400); a writer of the candidate's provider, unacknowledged (409 with the
    /// warning); a refused endpoint (400); the spend cap (429). A request the client aborts is a 499.
    /// </summary>
    [HttpPost("runs/{runId:long}/report-documents")]
    public async Task<IActionResult> WriteRunReportDocuments(long runId, [FromBody] WriteRunReportDocumentsRequest request, CancellationToken ct)
    {
        if (request == null) return BadRequest(new { error = "A request body is required." });
        if (_runReportDocuments == null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        try
        {
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

            var (replaceable, replaceRefusal) = await BenchmarkRunReportDocumentService.ReplaceableAudiencesAsync(
                _db, BenchmarkRunReportDocumentService.SubjectKeyOf(runId), BenchmarkReportDocumentOrigin.RunCompletion,
                requested, request.ReplaceDocumentIds, ct);
            if (replaceRefusal != null) return BadRequest(new { error = replaceRefusal });

            var missing = await BenchmarkRunReportDocumentService.MissingAudiencesAsync(_db, runId, ct);
            List<BenchmarkReportAudience> toWrite;
            if (requested == null)
            {
                toWrite = BenchmarkRunReportDocumentService.Audiences.Where(a => missing.Contains(a) || replaceable.Contains(a)).ToList();
                if (toWrite.Count == 0)
                {
                    return Conflict(new { error = AllWrittenMessage });
                }
            }
            else
            {
                var written = requested.Where(a => !missing.Contains(a) && !replaceable.Contains(a)).ToList();
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
            if (!_runReportDocuments.TryStart(runId, userId, toWrite, request.AcknowledgeSameProvider, out _,
                    replaceable.Count > 0 ? request.ReplaceDocumentIds : null))
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// The run's current or last run-completion job: 200 with its view, 204 when this process knows
    /// none for the run (none since the last restart, or its finished job has expired), 404 for an
    /// unknown run, 499 when the client aborts.
    /// </summary>
    [HttpGet("runs/{runId:long}/report-documents/job")]
    public async Task<IActionResult> GetRunReportJob(long runId, CancellationToken ct)
    {
        if (_runReportDocuments == null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        try
        {
            var run = await _db.BenchmarkRuns.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(r => r.Id == runId, ct);
            if (run == null) return NotFound();

            var view = RunJobView(run);
            return view == null ? NoContent() : Ok(view);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// Cancels the run's run-completion job: 202 with its view once asked; 409 when no job for the run
    /// is in progress; 404 for an unknown run; 499 when the client aborts. Documents written before the
    /// cancellation are kept.
    /// </summary>
    [HttpPost("runs/{runId:long}/report-documents/cancel")]
    public async Task<IActionResult> CancelRunReportJob(long runId, CancellationToken ct)
    {
        if (_runReportDocuments == null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        try
        {
            var run = await _db.BenchmarkRuns.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(r => r.Id == runId, ct);
            if (run == null) return NotFound();

            if (_runReportDocuments.TryCancel(runId) != BenchmarkRunReportDocumentService.CancelOutcome.Requested)
            {
                return Conflict(new { error = "No report writing is in progress for this run." });
            }
            return Accepted(RunJobView(run));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
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
            estimate.EstimatedTotalCostWithRepairUsd = TotalCostWithRepair(estimate.Estimates);
            return Ok(estimate);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>
    /// The controls a Root Cause Analysis of the run would use if written now, found by
    /// <see cref="RootCauseControls.FindAsync"/> as the run-completion preparation finds them, with the
    /// control limit they set and, when fewer than two qualify, the <c>control</c> run that analysis would
    /// propose, taken from the same preparation (null when the run has no final synthesis yet or no
    /// other provider has a configured Benchmark model). Makes no model call. 404 for an unknown run;
    /// 499 when the client aborts.
    /// </summary>
    [HttpGet("runs/{runId:long}/root-cause-controls")]
    public async Task<IActionResult> GetRunRootCauseControls(long runId, CancellationToken ct)
    {
        try
        {
            var run = await _db.BenchmarkRuns.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(r => r.Id == runId, ct);
            if (run == null) return NotFound();

            var controls = await RootCauseControls.FindAsync(_db, BenchmarkRunReportDocumentService.SubjectKeyOf(runId), ct);
            RootCauseRecommendedRun? controlRun = null;
            if (controls.Controls.Count < 2 && BenchmarkRunReportDocumentService.IsFinishedWithSynthesis(run))
            {
                var (prep, _) = await BenchmarkReportPackPreparation.PrepareAsync(
                    _db, _comparisonService,
                    BenchmarkRunReportDocumentService.RunRequest(runId, BenchmarkRunReportDocumentService.Audiences, 0),
                    BenchmarkReportPackPreparation.AnswerExcerptChars(_configuration), ct, _configuration);
                controlRun = ControlRunOf(prep);
            }
            return Ok(RootCauseControlsDtoOf(controls, controlRun));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    /// <summary>The <c>control</c> run a preparation's root-cause analysis proposes; null when it proposes none.</summary>
    internal static RootCauseRecommendedRun? ControlRunOf(BenchmarkReportPackPreparation? prep)
        => prep?.Sheet?.RootCause?.RecommendedRuns?.FirstOrDefault(r => string.Equals(r.Kind, RootCauseRunKinds.Control, StringComparison.Ordinal));

    /// <summary>
    /// A control set as the AI reports tabs show it: each control with its start as
    /// <see cref="RootCauseControlDto.CompletedAtUtc"/>, the control limit against the subject's provider
    /// (<c>None</c> when it does not cap), and the proposed control run.
    /// </summary>
    internal static RootCauseControlsDto RootCauseControlsDtoOf(RootCauseControlSet set, RootCauseRecommendedRun? controlRun)
    {
        ArgumentNullException.ThrowIfNull(set);
        int otherProviders = set.Controls.Count(c => !string.Equals(
            (c.Provider ?? string.Empty).Trim(), (set.SubjectProvider ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
        var (cap, _) = RootCauseControls.Limit(set.Controls.Count, otherProviders);
        return new RootCauseControlsDto
        {
            Controls = set.Controls.Select(RootCauseControlDto.From).ToList(),
            ControlLimit = cap ?? "None",
            ControlRun = set.Controls.Count < 2 ? controlRun : null
        };
    }

    /// <summary>
    /// Deletes one of the run's own run-completion documents and settles the run's documents status.
    /// 404 when the run or the document is unknown, or the document is not this run's run-completion
    /// document; 409 while the run's documents are being written; 499 when the client aborts.
    /// </summary>
    [HttpDelete("runs/{runId:long}/report-documents/{documentId:long}")]
    public async Task<IActionResult> DeleteRunReportDocument(long runId, long documentId, CancellationToken ct)
    {
        try
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

            if (!await BenchmarkReportRenderService.DeleteDocumentAsync(_db, documentId, ct)) return NotFound();
            _factsCache.Remove(documentId);
            return NoContent();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    [HttpGet("report-packs/jobs/{jobId}")]
    public IActionResult GetJob(string jobId)
    {
        var job = _jobManager.TryGet(jobId);
        if (job == null) return NotFound();
        return Ok(job.ToDto());
    }

    /// <summary>
    /// The writer's raw replies of a held job as one Markdown file (<see cref="BenchmarkReportPackWriterReplies"/>): every
    /// call so far while the job runs, with the tokens each stored document's replies cite. Not found once a newer job or a
    /// restart has replaced it.
    /// </summary>
    [HttpGet("report-packs/jobs/{jobId}/writer-replies")]
    public async Task<IActionResult> GetWriterReplies(string jobId, CancellationToken ct = default)
    {
        var job = _jobManager.TryGet(jobId);
        if (job == null) return NotFound();
        try
        {
            var ids = job.WriterReplies().Select(d => d.Document.DocumentId).OfType<long>().Distinct().ToList();
            var stored = ids.Count == 0
                ? new Dictionary<long, BenchmarkReportDocument>()
                : await _db.BenchmarkReportDocuments.AsNoTracking().Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);
            return Content(BenchmarkReportPackWriterReplies.BuildMarkdown(job, DateTime.UtcNow, stored), "text/markdown; charset=utf-8");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
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

    /// <summary>
    /// The request's audiences, distinct, in audience order; the refusal <c>"&lt;name&gt; is not written for
    /// &lt;scope&gt; documents."</c> for the first one that <paramref name="scope"/> does not write.
    /// </summary>
    private static (List<BenchmarkReportAudience> Audiences, string? Refusal) RequestedReportAudiences(
        BenchmarkReportPackRequest request, BenchmarkReportScope scope)
    {
        var requested = (request.Audiences ?? new List<BenchmarkReportAudience>()).Distinct().OrderBy(a => a).ToList();
        var allowed = ScopeAudiences.TryGetValue(scope, out var list) ? list : Array.Empty<BenchmarkReportAudience>();
        foreach (var audience in requested)
        {
            if (!allowed.Contains(audience)) return (requested, AudienceRefusal(audience, scope));
        }
        return (requested, null);
    }

    /// <summary><c>"Provider Issue Report is not written for comparison documents."</c></summary>
    private static string AudienceRefusal(BenchmarkReportAudience audience, BenchmarkReportScope scope)
    {
        string scopeName = scope switch
        {
            BenchmarkReportScope.Comparison => "comparison",
            BenchmarkReportScope.ChatConsistency => "chat consistency",
            _ => "model"
        };
        return BenchmarkReportRenderService.AudienceName(audience) + " is not written for " + scopeName + " documents.";
    }

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

    /// <summary>A model's catalog release date (ISO); empty when the catalog does not know it.</summary>
    private string? ReleaseDate(string provider, string modelId) => _modelMetadata.GetMetadata(provider, modelId).ReleaseDate;

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
        long DurationMs, decimal? CostUsd, string? CoveredSetKey, string? ReferenceEntryKey);

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
                CostUsd = d.CostUsd,
                ReferenceEntryKey = d.ReferenceEntryKey
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
            d.WriterProvider, d.WriterModelId, d.WriterThinkingLevel, d.DurationMs, d.CostUsd, d.CoveredSetKey, d.ReferenceEntryKey));

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

        var groups = stored
            .Where(d => d.CoveredSetKey != exceptSetKey)
            .GroupBy(d => d.CoveredSetKey!)
            .Select(g => (Group: g, Newest: g.OrderByDescending(d => d.CreatedAtUtc).ThenByDescending(d => d.Id).First()))
            .ToList();
        if (groups.Count == 0) return new List<BenchmarkReportPackCoveredSetDto>();

        var newestIds = groups.Select(g => g.Newest.Id).ToList();
        var (factsById, _) = await _factsCache.GetAsync(_db, newestIds, ct);
        var coveredKeysById = await _db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(d => newestIds.Contains(d.Id))
            .Select(d => new { d.Id, d.CoveredEntryKeysJson })
            .ToDictionaryAsync(d => d.Id, d => d.CoveredEntryKeysJson, ct);

        var sets = new List<(BenchmarkReportPackCoveredSetDto Set, DateTime Newest)>();
        foreach (var (group, newest) in groups)
        {
            var facts = factsById.TryGetValue(newest.Id, out var found) ? found : BenchmarkReportRenderService.StoredFacts.Empty;

            sets.Add((new BenchmarkReportPackCoveredSetDto
            {
                CoveredSetKey = group.Key,
                SubjectKey = newest.SubjectKey,
                CoversAllEntries = facts.CoversAllEntries,
                CoveredModels = BenchmarkReportRenderService.CoveredModels(
                    BenchmarkReportScope.Comparison, newest.SubjectKey, null, coveredKeysById.GetValueOrDefault(newest.Id), facts),
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
    /// Each document's prompt size, estimated tokens, first-call cost and first call plus one repair turn
    /// (<see cref="BenchmarkReportPackService.EstimateCostWithRepair"/>), and share of the writer's context
    /// window, for <paramref name="subjectKey"/>; comparison scope uses its own output sizes. Makes no
    /// model call.
    /// </summary>
    private BenchmarkReportPackAudienceEstimateDto[] EstimateAudiences(
        BenchmarkReportPackPreparation prep, SystemAiApiConfiguration? writer, IEnumerable<BenchmarkReportAudience> audiences,
        string? subjectKey = null)
    {
        var requested = audiences.ToList();
        var chars = requested.Distinct().ToDictionary(a => a, a => PromptCharsOf(prep, a));
        return EstimateAudiences(prep.Scope, chars, writer, requested, subjectKey);
    }

    /// <summary>
    /// As <see cref="EstimateAudiences(BenchmarkReportPackPreparation, SystemAiApiConfiguration?, IEnumerable{BenchmarkReportAudience}, string?)"/>,
    /// from each document's prompt length in <paramref name="promptChars"/>; each estimate says whether it
    /// is above <see cref="ContextWindowRefusalShare"/> of the window.
    /// </summary>
    private BenchmarkReportPackAudienceEstimateDto[] EstimateAudiences(
        BenchmarkReportScope scope, IReadOnlyDictionary<BenchmarkReportAudience, int> promptChars, SystemAiApiConfiguration? writer,
        IEnumerable<BenchmarkReportAudience> audiences, string? subjectKey)
    {
        int maxOutputTokens = BenchmarkReportPackPreparation.MaxOutputTokens(_configuration);
        var pricing = writer != null ? _pricingService.Resolve(writer) : null;
        int? window = ContextWindowOf(writer);
        var outputs = scope == BenchmarkReportScope.Comparison ? ComparisonEstimatedOutputTokens : EstimatedOutputTokens;
        return audiences.Select(audience =>
        {
            int chars = promptChars[audience];
            int input = (chars + 3) / 4;
            int output = Math.Min(maxOutputTokens, outputs[audience]);
            double? share = window is int size ? (double)input / size : null;
            return new BenchmarkReportPackAudienceEstimateDto
            {
                Audience = audience,
                SubjectKey = subjectKey,
                PromptChars = chars,
                EstimatedInputTokens = input,
                EstimatedOutputTokens = output,
                EstimatedCostUsd = pricing == null ? null : (double)ModelPricingService.EstimateSingleRequestCost(pricing, input, output),
                EstimatedCostWithRepairUsd = pricing == null ? null : (double)BenchmarkReportPackService.EstimateCostWithRepair(pricing, input, output),
                ContextWindowShare = share,
                ExceedsContextWindow = share is double value && value > ContextWindowRefusalShare
            };
        }).ToArray();
    }

    /// <summary>The sum of the estimates' costs; null when there is none or a document has no price.</summary>
    private static double? TotalCost(IReadOnlyCollection<BenchmarkReportPackAudienceEstimateDto> estimates)
        => estimates.Count == 0 || estimates.Any(e => e.EstimatedCostUsd == null) ? null : estimates.Sum(e => e.EstimatedCostUsd!.Value);

    /// <summary>The sum of the estimates' first call plus one repair turn; null under the same rule as <see cref="TotalCost"/>.</summary>
    private static double? TotalCostWithRepair(IReadOnlyCollection<BenchmarkReportPackAudienceEstimateDto> estimates)
        => estimates.Count == 0 || estimates.Any(e => e.EstimatedCostWithRepairUsd == null) ? null : estimates.Sum(e => e.EstimatedCostWithRepairUsd!.Value);

    private static string EndpointRefusal(SystemAiApiConfiguration writer, string? endpointError)
        => $"Report writer configuration '{writer.DisplayName}': its custom endpoint is not allowed by the endpoint policy: {endpointError}";
}
