namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>
/// Lists, renders and deletes stored report-pack documents. Rendering is plain code over the stored
/// row: this service holds no provider, key or agent loop, so no download can reach a model.
/// </summary>
public class BenchmarkReportRenderService
{
    public const int MaxListSize = 500;

    private readonly ApplicationDbContext _db;
    private readonly ILogger<BenchmarkReportRenderService> _logger;

    public BenchmarkReportRenderService(ApplicationDbContext db, ILogger<BenchmarkReportRenderService> logger)
    {
        _db = db;
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

    /// <summary>Newest first; filtered by suite, by a run the subject includes, or both.</summary>
    public async Task<List<BenchmarkReportDocumentListItemDto>> ListAsync(long? suiteId, long? runId, int? take, CancellationToken ct)
    {
        int limit = Math.Clamp(take ?? 200, 1, MaxListSize);

        var query = _db.BenchmarkReportDocuments.AsNoTracking().IgnoreAutoIncludes();
        if (suiteId != null) query = query.Where(d => d.SuiteId == suiteId);
        if (runId != null) query = query.Where(d => d.Runs.Any(r => r.RunId == runId));

        var rows = await query
            .OrderByDescending(d => d.CreatedAtUtc).ThenByDescending(d => d.Id)
            .Take(limit)
            .Select(d => new
            {
                d.Id, d.PackId, d.Audience, d.Origin, d.Title, d.SubjectKey, d.SubjectLabel, d.SubjectRunIdsJson,
                d.SuiteId, d.SuiteName, d.WriterDisplayName, d.WriterProvider, d.WriterModelId, d.WriterThinkingLevel,
                d.SameProviderAcknowledged, d.Status, d.ReportFormatVersion, d.CreatedAtUtc,
                d.InputTokens, d.OutputTokens, d.DurationMs, d.CostUsd,
                Runs = d.Runs.Select(r => new { r.RunId, r.FinalScore, r.QualityIndex, r.SpeedIndex, r.ScoringMethodVersion, r.RerunCompletedAtUtc, r.SynthesisSha256 }).ToList()
            })
            .ToListAsync(ct);

        var current = await CurrentFingerprintsAsync(rows.SelectMany(r => r.Runs.Select(x => x.RunId)).Distinct().ToList(), ct);

        return rows.Select(d =>
        {
            var missing = d.Runs.Where(r => !current.ContainsKey(r.RunId)).Select(r => r.RunId).OrderBy(id => id).ToList();
            bool changed = missing.Count > 0 || d.Runs.Any(r => current.TryGetValue(r.RunId, out var now)
                && now != new RunFingerprint(r.FinalScore, r.QualityIndex, r.SpeedIndex, r.ScoringMethodVersion, r.RerunCompletedAtUtc, r.SynthesisSha256));

            var item = new BenchmarkReportDocumentListItemDto();
            Fill(item, d.Id, d.PackId, d.Audience, d.Origin, d.Title, d.SubjectKey, d.SubjectLabel, d.SubjectRunIdsJson,
                d.SuiteId, d.SuiteName, d.WriterDisplayName, d.WriterProvider, d.WriterModelId, d.WriterThinkingLevel,
                d.SameProviderAcknowledged, d.Status, d.ReportFormatVersion, d.CreatedAtUtc,
                d.InputTokens, d.OutputTokens, d.DurationMs, d.CostUsd);
            item.RunChangedSinceGeneration = changed;
            item.MissingRunIds = missing;
            return item;
        }).ToList();
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
        var missing = d.Runs.Where(r => !current.ContainsKey(r.RunId)).Select(r => r.RunId).OrderBy(x => x).ToList();

        var dto = new BenchmarkReportDocumentDetailDto
        {
            PricingSource = d.PricingSource,
            AnswerExcerptChars = d.AnswerExcerptChars,
            WriterPromptSha256 = d.WriterPromptSha256,
            ValidationNotes = DeserializeNotes(d.ValidationNotesJson, d.Id),
            FactsJson = d.FactsJson,
            MissingRunIds = missing,
            RunChangedSinceGeneration = missing.Count > 0 || d.Runs.Any(r => current.TryGetValue(r.RunId, out var now)
                && now != new RunFingerprint(r.FinalScore, r.QualityIndex, r.SpeedIndex, r.ScoringMethodVersion, r.RerunCompletedAtUtc, r.SynthesisSha256))
        };
        Fill(dto, d.Id, d.PackId, d.Audience, d.Origin, d.Title, d.SubjectKey, d.SubjectLabel, d.SubjectRunIdsJson,
            d.SuiteId, d.SuiteName, d.WriterDisplayName, d.WriterProvider, d.WriterModelId, d.WriterThinkingLevel,
            d.SameProviderAcknowledged, d.Status, d.ReportFormatVersion, d.CreatedAtUtc,
            d.InputTokens, d.OutputTokens, d.DurationMs, d.CostUsd);
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

    public async Task<bool> DeleteAsync(long id, CancellationToken ct)
    {
        var d = await _db.BenchmarkReportDocuments.IgnoreAutoIncludes().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d == null) return false;
        _db.BenchmarkReportDocuments.Remove(d);
        await _db.SaveChangesAsync(ct);
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
