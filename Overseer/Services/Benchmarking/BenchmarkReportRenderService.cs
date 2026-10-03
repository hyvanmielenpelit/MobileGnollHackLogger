namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>
/// Lists, renders and deletes stored report-pack documents, and stores their chart images through
/// <see cref="BenchmarkReportChartStore"/>. Rendering is plain code over the stored row and files:
/// this service holds no provider, key or agent loop, so no download can reach a model.
/// </summary>
public class BenchmarkReportRenderService
{
    public const int MaxListSize = 500;

    public const string StandaloneChartsRefusal =
        "This document has no peers; charts are drawn only for documents that compare models.";

    private readonly ApplicationDbContext _db;
    private readonly BenchmarkReportChartStore _charts;
    private readonly ILogger<BenchmarkReportRenderService> _logger;

    public BenchmarkReportRenderService(
        ApplicationDbContext db, BenchmarkReportChartStore charts, ILogger<BenchmarkReportRenderService> logger)
    {
        _db = db;
        _charts = charts;
        _logger = logger;
    }

    /// <summary>The name audience 2 was shown under before it was relabeled, still in older documents' stored titles.</summary>
    public const string LegacyTechnicalReportName = "Technical Report";

    public static string AudienceName(BenchmarkReportAudience audience) => audience switch
    {
        BenchmarkReportAudience.ExecutiveSummary => "Executive Summary",
        BenchmarkReportAudience.TechnicalReport => "Report for AI Researchers and Developers",
        BenchmarkReportAudience.InternalBrief => "Internal Improvement Brief",
        _ => audience.ToString()
    };

    /// <summary>
    /// A stored title as it is shown now: a Report for AI Researchers and Developers written under the
    /// legacy name has its trailing <c>— Technical Report</c> replaced by the current one.
    /// </summary>
    public static string CurrentTitle(BenchmarkReportAudience audience, string? title)
    {
        title ??= string.Empty;
        string legacySuffix = " — " + LegacyTechnicalReportName;
        return audience == BenchmarkReportAudience.TechnicalReport && title.EndsWith(legacySuffix, StringComparison.Ordinal)
            ? title[..^legacySuffix.Length] + " — " + AudienceName(audience)
            : title;
    }

    /// <summary>
    /// First 16 hex characters of SHA-256 over <c>AssessmentJson + "\n" + CoAssessorSynthesisJson</c>: part of a
    /// run's scoring fingerprint, so a re-run final synthesis shows as a change.
    /// </summary>
    public static string SynthesisSha256(string? assessmentJson, string? coAssessorSynthesisJson)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes((assessmentJson ?? string.Empty) + "\n" + (coAssessorSynthesisJson ?? string.Empty)));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    /// <summary>A document's run as it was when the document was written, and as it is now.</summary>
    private sealed record RunFingerprint(int? FinalScore, int? QualityIndex, int? SpeedIndex, int ScoringMethodVersion, DateTime? RerunCompletedAtUtc, string SynthesisSha256);

    /// <summary>
    /// Newest first; filtered by suite, by a run the subject includes (peer runs never match), by a
    /// comparison key, by origin, by subject key, or any combination.
    /// </summary>
    public async Task<List<BenchmarkReportDocumentListItemDto>> ListAsync(long? suiteId, long? runId, int? take, CancellationToken ct)
        => await ListAsync(new BenchmarkReportDocumentListFilter { SuiteId = suiteId, RunId = runId, Take = take }, ct);

    public Task<List<BenchmarkReportDocumentListItemDto>> ListAsync(BenchmarkReportDocumentListFilter filter, CancellationToken ct)
        => ListAsync(filter, subjectKey: null, ct);

    /// <summary>
    /// As <see cref="ListAsync(BenchmarkReportDocumentListFilter, CancellationToken)"/>, also filtered by
    /// <paramref name="subjectKey"/> (<c>run:1</c>, <c>group:4</c>, <c>battery:7</c>), matched exactly
    /// against each document's subject key; null does not filter.
    /// </summary>
    public async Task<List<BenchmarkReportDocumentListItemDto>> ListAsync(
        BenchmarkReportDocumentListFilter filter, string? subjectKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);
        int limit = Math.Clamp(filter.Take ?? 200, 1, MaxListSize);
        long? suiteId = filter.SuiteId;
        long? runId = filter.RunId;
        string? comparisonKey = filter.ComparisonKey;
        BenchmarkReportDocumentOrigin? origin = filter.Origin;

        var query = _db.BenchmarkReportDocuments.AsNoTracking().IgnoreAutoIncludes();
        if (suiteId != null) query = query.Where(d => d.SuiteId == suiteId);
        if (runId != null) query = query.Where(d => d.Runs.Any(r => r.RunId == runId && !r.IsPeer));
        if (comparisonKey != null) query = query.Where(d => d.ComparisonKey == comparisonKey);
        if (origin != null) query = query.Where(d => d.Origin == origin);
        if (subjectKey != null) query = query.Where(d => d.SubjectKey == subjectKey);

        var rows = await query
            .OrderByDescending(d => d.CreatedAtUtc).ThenByDescending(d => d.Id)
            .Take(limit)
            .Select(d => new
            {
                d.Id, d.PackId, d.Audience, d.Origin, d.Title, d.SubjectKey, d.SubjectLabel, d.SubjectRunIdsJson,
                d.SuiteId, d.SuiteName, d.WriterDisplayName, d.WriterProvider, d.WriterModelId, d.WriterThinkingLevel,
                d.SameProviderAcknowledged, d.Status, d.ReportFormatVersion, d.CreatedAtUtc,
                d.InputTokens, d.OutputTokens, d.DurationMs, d.CostUsd,
                d.ComparisonKey, d.ComparisonRequestJson, d.FactsJson,
                Runs = d.Runs.Select(r => new { r.RunId, r.IsPeer, r.FinalScore, r.QualityIndex, r.SpeedIndex, r.ScoringMethodVersion, r.RerunCompletedAtUtc, r.SynthesisSha256 }).ToList()
            })
            .ToListAsync(ct);

        var current = await CurrentFingerprintsAsync(rows.SelectMany(r => r.Runs.Select(x => x.RunId)).Distinct().ToList(), ct);

        return rows.Select(d =>
        {
            var stored = d.Runs.Select(r => new StoredRun(r.RunId, r.IsPeer,
                new RunFingerprint(r.FinalScore, r.QualityIndex, r.SpeedIndex, r.ScoringMethodVersion, r.RerunCompletedAtUtc, r.SynthesisSha256))).ToList();
            var subjectRuns = stored.Where(r => !r.IsPeer).ToList();
            var missing = subjectRuns.Where(r => !current.ContainsKey(r.RunId)).Select(r => r.RunId).OrderBy(id => id).ToList();

            var item = new BenchmarkReportDocumentListItemDto();
            Fill(item, d.Id, d.PackId, d.Audience, d.Origin, d.Title, d.SubjectKey, d.SubjectLabel, d.SubjectRunIdsJson,
                d.SuiteId, d.SuiteName, d.WriterDisplayName, d.WriterProvider, d.WriterModelId, d.WriterThinkingLevel,
                d.SameProviderAcknowledged, d.Status, d.ReportFormatVersion, d.CreatedAtUtc,
                d.InputTokens, d.OutputTokens, d.DurationMs, d.CostUsd);
            item.RunChangedSinceGeneration = AnyChanged(subjectRuns, current);
            item.PeersChangedSinceGeneration = AnyChanged(stored.Where(r => r.IsPeer), current);
            item.MissingRunIds = missing;
            FillComparison(item, d.ComparisonKey, d.ComparisonRequestJson, d.FactsJson);
            FillCharts(item);
            return item;
        }).ToList();
    }

    /// <summary>A stored run row: whose it is, and its fingerprint at generation.</summary>
    private sealed record StoredRun(long RunId, bool IsPeer, RunFingerprint Fingerprint);

    /// <summary>True when any run is gone or its fingerprint differs from the run as it is now.</summary>
    private static bool AnyChanged(IEnumerable<StoredRun> runs, IReadOnlyDictionary<long, RunFingerprint> current)
        => runs.Any(r => !current.TryGetValue(r.RunId, out var now) || now != r.Fingerprint);

    /// <summary>The comparison's identity and shape, read from the stored request and fact sheet.</summary>
    private static void FillComparison(BenchmarkReportDocumentListItemDto item, string? comparisonKey, string? comparisonRequestJson, string? factsJson)
    {
        item.ComparisonKey = comparisonKey;

        BenchmarkModelComparisonRequest? request = null;
        try
        {
            request = string.IsNullOrWhiteSpace(comparisonRequestJson) ? null : BenchmarkReportJson.Deserialize<BenchmarkModelComparisonRequest>(comparisonRequestJson);
        }
        catch (Exception)
        {
            request = null;
        }
        if (request != null)
        {
            item.ComparisonEntryCount = (request.RunIds?.Distinct().Count() ?? 0) + (request.GroupIds?.Distinct().Count() ?? 0)
                + (request.BatteryRunIds?.Distinct().Count() ?? 0);
            item.PricingBasis = request.PricingBasis.ToString();
        }

        var (peerCount, peerLetters) = PeersOf(factsJson);
        item.PeerCount = peerCount;
        item.PeerLetters = peerLetters;
    }

    /// <summary>The chart set's figures and settings hash, from its manifest alone; none when it has no charts.</summary>
    private void FillCharts(BenchmarkReportDocumentListItemDto item)
    {
        var summary = _charts.ReadSummary(item.Id);
        item.ChartCount = summary?.ChartCount ?? 0;
        item.ChartFigureKeys = summary?.FigureKeys ?? new List<string>();
        item.ChartSettingsHash = summary == null || string.IsNullOrEmpty(summary.SettingsHash) ? null : summary.SettingsHash;
    }

    /// <summary>
    /// The length of the stored fact sheet's <c>peers</c> array, and each peer's entry key mapped to its
    /// letter; 0 and empty when absent or unreadable.
    /// </summary>
    private static (int Count, Dictionary<string, string> Letters) PeersOf(string? factsJson)
    {
        var letters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(factsJson)) return (0, letters);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(factsJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return (0, letters);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "peers", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var peer in property.Value.EnumerateArray())
                    {
                        string? entryKey = StringProperty(peer, "entryKey");
                        string? letter = StringProperty(peer, "letter");
                        if (!string.IsNullOrEmpty(entryKey) && !string.IsNullOrEmpty(letter))
                        {
                            letters.TryAdd(entryKey, letter);
                        }
                    }
                    return (property.Value.GetArrayLength(), letters);
                }
            }
            return (0, letters);
        }
        catch (System.Text.Json.JsonException)
        {
            return (0, new Dictionary<string, string>(StringComparer.Ordinal));
        }
    }

    /// <summary>A string property of a JSON object, its name matched ignoring case; null when absent or not a string.</summary>
    private static string? StringProperty(System.Text.Json.JsonElement element, string name)
    {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }
        return null;
    }

    public async Task<BenchmarkReportDocumentDetailDto?> GetAsync(long id, CancellationToken ct)
    {
        var d = await _db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Include(x => x.Runs)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d == null) return null;

        var current = await CurrentFingerprintsAsync(d.Runs.Select(r => r.RunId).ToList(), ct);
        var stored = d.Runs.Select(r => new StoredRun(r.RunId, r.IsPeer,
            new RunFingerprint(r.FinalScore, r.QualityIndex, r.SpeedIndex, r.ScoringMethodVersion, r.RerunCompletedAtUtc, r.SynthesisSha256))).ToList();
        var subjectRuns = stored.Where(r => !r.IsPeer).ToList();
        var missing = subjectRuns.Where(r => !current.ContainsKey(r.RunId)).Select(r => r.RunId).OrderBy(x => x).ToList();

        var dto = new BenchmarkReportDocumentDetailDto
        {
            PricingSource = d.PricingSource,
            AnswerExcerptChars = d.AnswerExcerptChars,
            WriterPromptSha256 = d.WriterPromptSha256,
            ValidationNotes = DeserializeNotes(d.ValidationNotesJson, d.Id),
            FactsJson = d.FactsJson,
            MissingRunIds = missing,
            RunChangedSinceGeneration = AnyChanged(subjectRuns, current),
            PeersChangedSinceGeneration = AnyChanged(stored.Where(r => r.IsPeer), current)
        };
        Fill(dto, d.Id, d.PackId, d.Audience, d.Origin, d.Title, d.SubjectKey, d.SubjectLabel, d.SubjectRunIdsJson,
            d.SuiteId, d.SuiteName, d.WriterDisplayName, d.WriterProvider, d.WriterModelId, d.WriterThinkingLevel,
            d.SameProviderAcknowledged, d.Status, d.ReportFormatVersion, d.CreatedAtUtc,
            d.InputTokens, d.OutputTokens, d.DurationMs, d.CostUsd);
        FillComparison(dto, d.ComparisonKey, d.ComparisonRequestJson, d.FactsJson);
        FillCharts(dto);
        return dto;
    }

    /// <summary>
    /// The document rendered as Markdown, deterministically, from its stored row alone. NotFound for an
    /// unknown id; a refusal for a disclosure and naming the audience does not render at.
    /// </summary>
    public async Task<(string? Markdown, bool NotFound, string? Refusal)> RenderAsync(
        long id, BenchmarkReportRenderOptions options, CancellationToken ct)
    {
        var (markdown, _, notFound, refusal) = await RenderWithDocumentAsync(id, options, ct);
        return (markdown, notFound, refusal);
    }

    /// <summary>
    /// As <see cref="RenderAsync"/>, with the stored row the Markdown was rendered from, which the
    /// PDF download takes its title block and metadata from.
    /// </summary>
    public async Task<(string? Markdown, BenchmarkReportDocument? Document, bool NotFound, string? Refusal)> RenderWithDocumentAsync(
        long id, BenchmarkReportRenderOptions options, CancellationToken ct)
    {
        var d = await _db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d == null) return (null, null, true, null);

        if (!BenchmarkReportPackRenderer.IsAllowed(d.Audience, options))
        {
            return (null, d, false,
                $"The {AudienceName(d.Audience)} does not render at {options.Disclosure} disclosure.");
        }

        return (BenchmarkReportPackRenderer.Render(d, options), d, false, null);
    }

    /// <summary>
    /// Removes a stored document as <see cref="DeleteDocumentAsync"/> does, then its chart folder. A
    /// folder that cannot be removed is logged and left for the chart storage maintenance to clear.
    /// </summary>
    public async Task<bool> DeleteAsync(long id, CancellationToken ct)
    {
        if (!await DeleteDocumentAsync(_db, id, ct)) return false;

        if (_charts.IsConfigured)
        {
            try
            {
                await _charts.DeleteChartsAsync(id, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ChartStoreException)
            {
                _logger.LogWarning(ex, "The charts of deleted report document {DocumentId} could not be removed.", id);
            }
        }
        return true;
    }

    /// <summary>
    /// Replaces a document's whole chart set. NotFound for an unknown id; a refusal for a stand-alone
    /// document, for chart storage that is not configured and for any upload
    /// <see cref="BenchmarkReportChartStore.ValidateCharts"/> refuses. Every chart is checked before
    /// anything is written.
    /// </summary>
    public async Task<(ReportDocumentChartsSummaryDto? Summary, bool NotFound, string? Refusal)> SetChartsAsync(
        long documentId, PutReportDocumentChartsRequest? request, CancellationToken ct)
    {
        var d = await _db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(x => x.Id == documentId)
            .Select(x => new { x.FactsJson })
            .FirstOrDefaultAsync(ct);
        if (d == null) return (null, true, null);

        if (PeersOf(d.FactsJson).Count == 0) return (null, false, StandaloneChartsRefusal);
        if (!_charts.IsConfigured) return (null, false, BenchmarkReportChartStore.NotConfiguredMessage);

        try
        {
            var validated = BenchmarkReportChartStore.ValidateCharts(request?.Charts);
            return (await _charts.SetChartsAsync(documentId, validated, ct), false, null);
        }
        catch (ChartStoreException ex)
        {
            return (null, false, ex.Message);
        }
    }

    /// <summary>
    /// Deletes a document's charts; false for an unknown id. With chart storage not configured there is
    /// nothing to delete.
    /// </summary>
    public async Task<bool> DeleteChartsAsync(long documentId, CancellationToken ct)
    {
        bool exists = await _db.BenchmarkReportDocuments.AsNoTracking().IgnoreAutoIncludes().AnyAsync(x => x.Id == documentId, ct);
        if (!exists) return false;

        if (_charts.IsConfigured)
        {
            await _charts.DeleteChartsAsync(documentId, ct);
        }
        return true;
    }

    /// <summary>A document's stored charts in one peer naming, for a PDF or Word render; empty when it has none.</summary>
    public Task<IReadOnlyList<BenchmarkReportRenderChart>> LoadRenderChartsAsync(
        long documentId, BenchmarkReportPeerNaming naming, CancellationToken ct)
        => _charts.LoadAsync(
            documentId,
            naming == BenchmarkReportPeerNaming.Anonymized ? BenchmarkReportChartStore.Anonymized : BenchmarkReportChartStore.Named,
            ct);

    /// <summary>
    /// Removes a stored document; false for an unknown id. Deleting a run's own run-completion
    /// document also settles the run's documents status
    /// (<see cref="BenchmarkRunReportDocumentService.SettleAfterDeleteAsync"/>), and deleting a battery
    /// run's own battery-completion document settles the battery run's
    /// (<see cref="BenchmarkBatteryReportDocumentService.SettleAfterDocumentDeleteAsync"/>).
    /// </summary>
    public static async Task<bool> DeleteDocumentAsync(ApplicationDbContext db, long id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var d = await db.BenchmarkReportDocuments.IgnoreAutoIncludes().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d == null) return false;
        long? runId = d.Origin == BenchmarkReportDocumentOrigin.RunCompletion
            && BenchmarkRunReportDocumentService.TryParseSubjectKey(d.SubjectKey, out long subjectRunId)
                ? subjectRunId
                : null;
        long? batteryRunId = d.Origin == BenchmarkReportDocumentOrigin.BatteryCompletion
            && BenchmarkBatteryReportDocumentService.TryParseSubjectKey(d.SubjectKey, out long subjectBatteryRunId)
                ? subjectBatteryRunId
                : null;

        db.BenchmarkReportDocuments.Remove(d);
        await db.SaveChangesAsync(ct);

        if (runId != null)
        {
            await BenchmarkRunReportDocumentService.SettleAfterDeleteAsync(db, runId.Value, ct);
        }
        if (batteryRunId != null)
        {
            await BenchmarkBatteryReportDocumentService.SettleAfterDocumentDeleteAsync(db, batteryRunId.Value, ct);
        }
        return true;
    }

    /// <summary>The scoring fingerprint of each run that still exists.</summary>
    private async Task<Dictionary<long, RunFingerprint>> CurrentFingerprintsAsync(List<long> runIds, CancellationToken ct)
    {
        if (runIds.Count == 0) return new Dictionary<long, RunFingerprint>();

        var runs = await _db.BenchmarkRuns
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(r => runIds.Contains(r.Id))
            .Select(r => new { r.Id, r.FinalScore, r.QualityIndex, r.SpeedIndex, r.ScoringMethodVersion, r.RerunCompletedAtUtc, r.AssessmentJson, r.CoAssessorSynthesisJson })
            .ToListAsync(ct);

        return runs.ToDictionary(
            r => r.Id,
            r => new RunFingerprint(r.FinalScore, r.QualityIndex, r.SpeedIndex, r.ScoringMethodVersion, r.RerunCompletedAtUtc,
                SynthesisSha256(r.AssessmentJson, r.CoAssessorSynthesisJson)));
    }

    private List<BenchmarkReportValidationNote> DeserializeNotes(string json, long id)
    {
        try
        {
            return BenchmarkReportJson.Deserialize<List<BenchmarkReportValidationNote>>(json) ?? new List<BenchmarkReportValidationNote>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Report document {DocumentId} has unreadable validation notes.", id);
            return new List<BenchmarkReportValidationNote>();
        }
    }

    private static List<long> DeserializeRunIds(string json)
    {
        try
        {
            return BenchmarkReportJson.Deserialize<List<long>>(json) ?? new List<long>();
        }
        catch (Exception)
        {
            return new List<long>();
        }
    }

    private static void Fill(
        BenchmarkReportDocumentListItemDto item, long id, Guid packId, BenchmarkReportAudience audience,
        BenchmarkReportDocumentOrigin origin, string title,
        string subjectKey, string subjectLabel, string subjectRunIdsJson, long? suiteId, string suiteName,
        string writerDisplayName, string writerProvider, string writerModelId, string? writerThinkingLevel,
        bool sameProviderAcknowledged, BenchmarkReportDocumentStatus status, int reportFormatVersion, DateTime createdAtUtc,
        long inputTokens, long outputTokens, long durationMs, decimal? costUsd)
    {
        item.Id = id;
        item.PackId = packId;
        item.Audience = audience;
        item.Origin = origin;
        item.Title = CurrentTitle(audience, title);
        item.SubjectKey = subjectKey;
        item.SubjectLabel = subjectLabel;
        item.SubjectRunIds = DeserializeRunIds(subjectRunIdsJson);
        item.SuiteId = suiteId;
        item.SuiteName = suiteName;
        item.WriterDisplayName = writerDisplayName;
        item.WriterProvider = writerProvider;
        item.WriterModelId = writerModelId;
        item.WriterThinkingLevel = writerThinkingLevel;
        item.SameProviderAcknowledged = sameProviderAcknowledged;
        item.Status = status.ToString();
        item.ReportFormatVersion = reportFormatVersion;
        item.CreatedAtUtc = createdAtUtc;
        item.InputTokens = inputTokens;
        item.OutputTokens = outputTokens;
        item.DurationMs = durationMs;
        item.CostUsd = costUsd;
        item.AllowedDisclosures = BenchmarkReportPackRenderer.AllowedDisclosures(audience).ToList();
    }
}
