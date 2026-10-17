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
using Overseer.Services.Benchmarking.Charts;

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

    public const string RedrawNotRootCauseRefusal = "Only a Root Cause Analysis has charts drawn by Overseer.";

    public const string RedrawUnreadableFactsRefusal = "The document's stored fact sheet could not be read.";

    private readonly ApplicationDbContext _db;
    private readonly BenchmarkReportChartStore _charts;
    private readonly RootCauseChartPublisher _publisher;
    private readonly ILogger<BenchmarkReportRenderService> _logger;
    private readonly BenchmarkReportDocumentFactsCache _factsCache;

    public BenchmarkReportRenderService(
        ApplicationDbContext db, BenchmarkReportChartStore charts, ILogger<BenchmarkReportRenderService> logger,
        RootCauseChartPublisher? publisher = null, BenchmarkReportDocumentFactsCache? factsCache = null)
    {
        _db = db;
        _charts = charts;
        _logger = logger;
        _publisher = publisher ?? new RootCauseChartPublisher(charts);
        _factsCache = factsCache ?? BenchmarkReportDocumentFactsCache.None;
    }

    public static string AudienceName(BenchmarkReportAudience audience) => audience switch
    {
        BenchmarkReportAudience.ExecutiveSummary => "Executive Summary",
        BenchmarkReportAudience.ProviderIssueReport => "Provider Issue Report",
        BenchmarkReportAudience.RootCauseAnalysis => "Root Cause Analysis",
        _ => audience.ToString()
    };

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
    /// comparison key or id, by origin, by subject key, or any combination.
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
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int limit = Math.Clamp(filter.Take ?? 200, 1, MaxListSize);
        long? suiteId = filter.SuiteId;
        long? runId = filter.RunId;
        string? comparisonKey = filter.ComparisonKey;
        int? comparisonId = filter.ComparisonId;
        BenchmarkReportDocumentOrigin? origin = filter.Origin;

        var query = _db.BenchmarkReportDocuments.AsNoTracking().IgnoreAutoIncludes();
        if (suiteId != null) query = query.Where(d => d.SuiteId == suiteId);
        if (runId != null) query = query.Where(d => d.Runs.Any(r => r.RunId == runId && !r.IsPeer));
        if (comparisonKey != null) query = query.Where(d => d.ComparisonKey == comparisonKey);
        if (comparisonId != null) query = query.Where(d => d.ComparisonId == comparisonId);
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
                d.ComparisonKey, d.ComparisonRequestJson,
                d.Scope, d.ComparisonId, d.CoveredEntryKeysJson, d.CoveredSetKey, d.ReferenceEntryKey, d.ChatConsistencyAnalysisId,
                Runs = d.Runs.Select(r => new { r.RunId, r.IsPeer, r.FinalScore, r.QualityIndex, r.SpeedIndex, r.ScoringMethodVersion, r.RerunCompletedAtUtc, r.SynthesisSha256 }).ToList()
            })
            .ToListAsync(ct);

        var (factsById, factsHits) = await _factsCache.GetAsync(_db, rows.Select(r => r.Id).ToList(), ct);
        var current = await CurrentFingerprintsAsync(rows.SelectMany(r => r.Runs.Select(x => x.RunId)).Distinct().ToList(), ct);
        var comparisonNames = await ComparisonNamesAsync(rows.Select(r => r.ComparisonId), ct);

        var items = rows.Select(d =>
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
            item.ChatConsistencyAnalysisId = d.ChatConsistencyAnalysisId;
            var facts = factsById.TryGetValue(d.Id, out var found) ? found : StoredFacts.Empty;
            FillComparison(item, d.ComparisonKey, d.ComparisonRequestJson, facts);
            FillScope(item, d.Scope, d.ComparisonId, DisplayNameOf(comparisonNames, d.ComparisonId), d.CoveredEntryKeysJson, d.CoveredSetKey, d.ReferenceEntryKey, facts);
            FillCharts(item);
            return item;
        }).ToList();

        _logger.LogInformation(
            "Listed {RowCount} report documents, {CacheHits} fact sheets from the cache, in {ElapsedMs} ms.",
            items.Count, factsHits, stopwatch.ElapsedMilliseconds);
        return items;
    }

    /// <summary>The display name of each existing comparison among <paramref name="ids"/>, read in one query.</summary>
    private async Task<Dictionary<int, string>> ComparisonNamesAsync(IEnumerable<int?> ids, CancellationToken ct)
    {
        var wanted = ids.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (wanted.Count == 0) return new Dictionary<int, string>();

        var comparisons = await _db.BenchmarkComparisons
            .AsNoTracking()
            .Where(c => wanted.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.DefaultName })
            .ToListAsync(ct);
        return comparisons.ToDictionary(c => c.Id, c => c.Name ?? c.DefaultName);
    }

    private static string? DisplayNameOf(IReadOnlyDictionary<int, string> names, int? id)
        => id is int value && names.TryGetValue(value, out var name) ? name : null;

    /// <summary>
    /// The document's scope, its numbered comparison and that comparison's display name now, its covered
    /// set, whether it covers every entry (comparison scope only) and the models it covers.
    /// </summary>
    private static void FillScope(
        BenchmarkReportDocumentListItemDto item, BenchmarkReportScope scope, int? comparisonId, string? comparisonName,
        string? coveredEntryKeysJson, string? coveredSetKey, string? referenceEntryKey, StoredFacts facts)
    {
        item.Scope = scope;
        item.ComparisonId = comparisonId;
        item.ComparisonName = comparisonId == null ? null : comparisonName;
        item.CoveredSetKey = coveredSetKey;
        item.ReferenceEntryKey = scope == BenchmarkReportScope.Comparison ? referenceEntryKey : null;
        item.CoversAllEntries = scope == BenchmarkReportScope.Comparison && facts.CoversAllEntries;
        item.ComparisonModelCount = scope == BenchmarkReportScope.Comparison ? facts.ComparisonEntryCount : null;
        item.CoveredModels = CoveredModels(scope, item.SubjectKey, item.SubjectLabel, coveredEntryKeysJson, facts);
    }

    /// <summary>
    /// The models a stored document covers. Model scope: its subject, with the stored subject label and
    /// the fact sheet's subject provider. Comparison scope: each key of
    /// <see cref="BenchmarkReportDocument.CoveredEntryKeysJson"/> in stored order, labeled through
    /// <see cref="StoredFacts.EntryLabels"/>, the key itself where the sheet has no label for it, each
    /// with its letter from <see cref="StoredFacts.PeerLetters"/>.
    /// </summary>
    public static List<BenchmarkReportCoveredModelDto> CoveredModels(
        BenchmarkReportScope scope, string? subjectKey, string? subjectLabel, string? coveredEntryKeysJson, StoredFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (scope != BenchmarkReportScope.Comparison)
        {
            return new List<BenchmarkReportCoveredModelDto>
            {
                new()
                {
                    EntryKey = subjectKey ?? string.Empty,
                    Label = subjectLabel ?? string.Empty,
                    Provider = string.IsNullOrWhiteSpace(facts.SubjectProvider) ? null : facts.SubjectProvider
                }
            };
        }

        return CoveredEntryKeys(coveredEntryKeysJson)
            .Select(key => facts.EntryLabels.TryGetValue(key, out var known)
                ? new BenchmarkReportCoveredModelDto { EntryKey = key, Label = known.Label, Provider = known.Provider }
                : new BenchmarkReportCoveredModelDto { EntryKey = key, Label = key })
            .Select(model =>
            {
                model.Letter = facts.PeerLetters.TryGetValue(model.EntryKey, out var letter) ? letter : null;
                return model;
            })
            .ToList();
    }

    /// <summary>The entry keys of <see cref="BenchmarkReportDocument.CoveredEntryKeysJson"/>, in stored order; empty when absent or unreadable.</summary>
    public static List<string> CoveredEntryKeys(string? coveredEntryKeysJson)
    {
        if (string.IsNullOrWhiteSpace(coveredEntryKeysJson)) return new List<string>();
        try
        {
            return (BenchmarkReportJson.Deserialize<List<string>>(coveredEntryKeysJson) ?? new List<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .ToList();
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// What the list, the covers and the file names read from a stored fact sheet without
    /// deserializing it whole.
    /// </summary>
    /// <param name="PeerCount">The length of the <c>peers</c> array.</param>
    /// <param name="PeerLetters">Each peer's entry key mapped to its letter.</param>
    /// <param name="SubjectProvider">The root <c>subjectProvider</c>.</param>
    /// <param name="CoversAllEntries">The root <c>coversAllEntries</c> of a comparison-scope sheet; false when absent.</param>
    /// <param name="ComparisonEntryCount">The root <c>comparisonEntryCount</c> of a comparison-scope sheet: the comparison's non-excluded entries when it was written; null when absent.</param>
    /// <param name="EntryLabels">
    /// Entry key → label and provider, from the objects of the <c>models</c> array and then of the
    /// <c>peers</c> array that carry <c>entryKey</c> and <c>label</c>; the first object of a key wins.
    /// </param>
    public sealed record StoredFacts(
        int PeerCount,
        Dictionary<string, string> PeerLetters,
        string? SubjectProvider,
        bool CoversAllEntries,
        int? ComparisonEntryCount,
        Dictionary<string, (string Label, string? Provider)> EntryLabels)
    {
        public static StoredFacts Empty => new(
            0, new Dictionary<string, string>(StringComparer.Ordinal), null, false, null,
            new Dictionary<string, (string Label, string? Provider)>(StringComparer.Ordinal));
    }

    /// <summary>The <see cref="StoredFacts"/> of a fact sheet's JSON; <see cref="StoredFacts.Empty"/> when absent or unreadable.</summary>
    public static StoredFacts ReadFacts(string? factsJson)
    {
        if (string.IsNullOrWhiteSpace(factsJson)) return StoredFacts.Empty;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(factsJson);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return StoredFacts.Empty;

            int peerCount = 0;
            var letters = new Dictionary<string, string>(StringComparer.Ordinal);
            var labels = new Dictionary<string, (string Label, string? Provider)>(StringComparer.Ordinal);
            System.Text.Json.JsonElement? peers = null;
            System.Text.Json.JsonElement? models = null;
            bool coversAll = false;
            int? entryCount = null;

            foreach (var property in root.EnumerateObject())
            {
                var value = property.Value;
                if (Named(property, "peers") && value.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    peers ??= value;
                }
                else if (Named(property, "models") && value.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    models ??= value;
                }
                else if (Named(property, "coversAllEntries"))
                {
                    coversAll = value.ValueKind == System.Text.Json.JsonValueKind.True;
                }
                else if (Named(property, "comparisonEntryCount") && value.ValueKind == System.Text.Json.JsonValueKind.Number
                         && value.TryGetInt32(out int count))
                {
                    entryCount = count;
                }
            }

            if (peers is { } peerArray)
            {
                peerCount = peerArray.GetArrayLength();
                foreach (var peer in peerArray.EnumerateArray())
                {
                    string? entryKey = StringProperty(peer, "entryKey");
                    string? letter = StringProperty(peer, "letter");
                    if (!string.IsNullOrEmpty(entryKey) && !string.IsNullOrEmpty(letter))
                    {
                        letters.TryAdd(entryKey, letter);
                    }
                }
            }

            foreach (var array in new[] { models, peers })
            {
                if (array is not { } entries) continue;
                foreach (var entry in entries.EnumerateArray())
                {
                    string? entryKey = StringProperty(entry, "entryKey");
                    string? label = StringProperty(entry, "label");
                    if (!string.IsNullOrEmpty(entryKey) && !string.IsNullOrEmpty(label))
                    {
                        string? provider = StringProperty(entry, "provider");
                        labels.TryAdd(entryKey, (label, string.IsNullOrWhiteSpace(provider) ? null : provider));
                    }
                }
            }

            return new StoredFacts(peerCount, letters, StringProperty(root, "subjectProvider"), coversAll, entryCount, labels);
        }
        catch (System.Text.Json.JsonException)
        {
            return StoredFacts.Empty;
        }

        static bool Named(System.Text.Json.JsonProperty property, string name)
            => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A stored run row: whose it is, and its fingerprint at generation.</summary>
    private sealed record StoredRun(long RunId, bool IsPeer, RunFingerprint Fingerprint);

    /// <summary>True when any run is gone or its fingerprint differs from the run as it is now.</summary>
    private static bool AnyChanged(IEnumerable<StoredRun> runs, IReadOnlyDictionary<long, RunFingerprint> current)
        => runs.Any(r => !current.TryGetValue(r.RunId, out var now) || now != r.Fingerprint);

    /// <summary>The comparison's identity and shape, read from the stored request and fact sheet.</summary>
    private static void FillComparison(BenchmarkReportDocumentListItemDto item, string? comparisonKey, string? comparisonRequestJson, StoredFacts facts)
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

        item.PeerCount = facts.PeerCount;
        item.PeerLetters = new Dictionary<string, string>(facts.PeerLetters, StringComparer.Ordinal);
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
            ValidationIssues = DeserializeNotes(d.ValidationNotesJson, d.Id)
                .Select(n => BenchmarkReportRuleCatalog.ToIssueDto(n, BenchmarkReportRuleCatalog.SlotTitlesFor(d.Audience)))
                .ToList(),
            FactsJson = d.FactsJson,
            MissingRunIds = missing,
            ChatConsistencyAnalysisId = d.ChatConsistencyAnalysisId,
            RunChangedSinceGeneration = AnyChanged(subjectRuns, current),
            PeersChangedSinceGeneration = AnyChanged(stored.Where(r => r.IsPeer), current)
        };
        Fill(dto, d.Id, d.PackId, d.Audience, d.Origin, d.Title, d.SubjectKey, d.SubjectLabel, d.SubjectRunIdsJson,
            d.SuiteId, d.SuiteName, d.WriterDisplayName, d.WriterProvider, d.WriterModelId, d.WriterThinkingLevel,
            d.SameProviderAcknowledged, d.Status, d.ReportFormatVersion, d.CreatedAtUtc,
            d.InputTokens, d.OutputTokens, d.DurationMs, d.CostUsd);
        var facts = ReadFacts(d.FactsJson);
        FillComparison(dto, d.ComparisonKey, d.ComparisonRequestJson, facts);
        var comparisonNames = await ComparisonNamesAsync(new[] { d.ComparisonId }, ct);
        FillScope(dto, d.Scope, d.ComparisonId, DisplayNameOf(comparisonNames, d.ComparisonId), d.CoveredEntryKeysJson, d.CoveredSetKey, d.ReferenceEntryKey, facts);
        FillCharts(dto);
        FillRootCause(dto, d.Audience, d.FactsJson, d.Id);
        return dto;
    }

    /// <summary>
    /// A Root Cause Analysis's recommended runs, checks and controls, from its stored fact sheet; left
    /// null on every other document and on one whose sheet holds no root-cause result.
    /// </summary>
    private void FillRootCause(BenchmarkReportDocumentDetailDto dto, BenchmarkReportAudience audience, string? factsJson, long id)
    {
        if (audience != BenchmarkReportAudience.RootCauseAnalysis) return;

        var rootCause = ReadSheet(factsJson, id)?.RootCause;
        if (rootCause == null) return;

        dto.RecommendedRuns = rootCause.RecommendedRuns.ToList();
        dto.Checks = rootCause.Checks.ToList();
        dto.Controls = rootCause.Controls.Select(RootCauseControlDto.From).ToList();
    }

    /// <summary>The stored fact sheet, deserialized whole; null, with a warning, when absent or unreadable.</summary>
    private BenchmarkReportFactSheet? ReadSheet(string? factsJson, long id)
    {
        if (string.IsNullOrWhiteSpace(factsJson)) return null;
        try
        {
            return BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(factsJson);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Report document {DocumentId} has an unreadable fact sheet.", id);
            return null;
        }
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
    /// PDF download takes its title block and metadata from. The row carries its
    /// <see cref="BenchmarkReportDocument.Comparison"/>, so the Markdown, the cover, the running header
    /// and the file name print the comparison's number and its name as it is now. A chat consistency
    /// document rendered with charts places them by its chart layout when the stored set has one
    /// (<see cref="ReadRenderLayout"/>), and by the legacy placement otherwise.
    /// </summary>
    public async Task<(string? Markdown, BenchmarkReportDocument? Document, bool NotFound, string? Refusal)> RenderWithDocumentAsync(
        long id, BenchmarkReportRenderOptions options, CancellationToken ct)
    {
        var d = await _db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Include(x => x.Comparison)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d == null) return (null, null, true, null);

        if (!BenchmarkReportPackRenderer.IsAllowed(d.Audience, options))
        {
            return (null, d, false,
                $"The {AudienceName(d.Audience)} does not render at {options.Disclosure} disclosure.");
        }

        bool chartLayoutPresent = d.Scope == BenchmarkReportScope.ChatConsistency
                                  && options.Charts is { Count: > 0 }
                                  && ReadRenderLayout(d.Id) != null;
        return (BenchmarkReportPackRenderer.Render(d, options, chartLayoutPresent), d, false, null);
    }

    /// <summary>
    /// Removes a stored document as <see cref="DeleteDocumentAsync"/> does, then its chart folder. A
    /// folder that cannot be removed is logged and left for the chart storage maintenance to clear.
    /// </summary>
    public async Task<bool> DeleteAsync(long id, CancellationToken ct)
    {
        if (!await DeleteDocumentAsync(_db, id, ct)) return false;
        _factsCache.Remove(id);

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
    /// Replaces a document's uploaded chart set and its layout, keeping the charts Overseer drew; a
    /// request without a layout stores none. NotFound for an unknown id; a refusal for a stand-alone
    /// document, for chart storage that is not configured, for any upload
    /// <see cref="BenchmarkReportChartStore.ValidateCharts"/> refuses (a server-drawn figure among them)
    /// and for a layout <see cref="BenchmarkReportChartStore.ValidateLayout"/> refuses. Everything is
    /// checked before anything is written. A comparison-scope document always covers two or more models,
    /// so it is never stand-alone; a chat consistency document plots the model under test over time, so
    /// it takes charts with or without control models. The stand-alone refusal is for uploads only: the
    /// server-drawn charts of a stand-alone Root Cause Analysis are stored by <see cref="RedrawServerChartsAsync"/>
    /// and when the document is written.
    /// </summary>
    public async Task<(ReportDocumentChartsSummaryDto? Summary, bool NotFound, string? Refusal)> SetChartsAsync(
        long documentId, PutReportDocumentChartsRequest? request, CancellationToken ct)
    {
        var d = await _db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(x => x.Id == documentId)
            .Select(x => new { x.FactsJson, x.Scope })
            .FirstOrDefaultAsync(ct);
        if (d == null) return (null, true, null);

        if (d.Scope is not (BenchmarkReportScope.Comparison or BenchmarkReportScope.ChatConsistency) && PeersOf(d.FactsJson).Count == 0)
        {
            return (null, false, StandaloneChartsRefusal);
        }
        if (!_charts.IsConfigured) return (null, false, BenchmarkReportChartStore.NotConfiguredMessage);

        try
        {
            var validated = BenchmarkReportChartStore.ValidateCharts(request?.Charts);
            var layout = BenchmarkReportChartStore.ValidateLayout(request?.Layout);
            return (await _charts.SetChartsAsync(documentId, validated, layout, ct), false, null);
        }
        catch (ChartStoreException ex)
        {
            return (null, false, ex.Message);
        }
    }

    /// <summary>
    /// Draws a stored Root Cause Analysis's server charts again from its <c>FactsJson</c> and stores them
    /// in place of the ones drawn before (<see cref="RootCauseChartPublisher"/>), keeping the uploaded
    /// charts. Returns the stored set's summary; NotFound for an unknown id; a refusal for a document that
    /// is not a Root Cause Analysis, for chart storage that is not configured, for a fact sheet that
    /// cannot be read or holds no root-cause result, and for a drawing or storage failure.
    /// </summary>
    public async Task<(ReportDocumentChartsSummaryDto? Summary, bool NotFound, string? Refusal)> RedrawServerChartsAsync(
        long documentId, CancellationToken ct)
    {
        var d = await _db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(x => x.Id == documentId)
            .Select(x => new { x.Audience, x.FactsJson })
            .FirstOrDefaultAsync(ct);
        if (d == null) return (null, true, null);

        if (d.Audience != BenchmarkReportAudience.RootCauseAnalysis) return (null, false, RedrawNotRootCauseRefusal);
        if (!_charts.IsConfigured) return (null, false, BenchmarkReportChartStore.NotConfiguredMessage);

        var sheet = ReadSheet(d.FactsJson, documentId);
        if (sheet == null) return (null, false, RedrawUnreadableFactsRefusal);
        if (sheet.RootCause == null) return (null, false, RootCauseChartPublisher.NoRootCauseMessage);

        var published = await _publisher.PublishAsync(documentId, sheet, ct);
        if (!published.Succeeded) return (null, false, published.Error);

        return (_charts.ReadSummary(documentId) ?? new ReportDocumentChartsSummaryDto { DocumentId = documentId }, false, null);
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

    /// <summary>
    /// A document's stored charts in one peer naming, for a PDF or Word render; empty when it has none.
    /// A chat consistency document's figures plot the model under test alone and are drawn once, named,
    /// so every copy of it carries them. A stand-alone model-scope document names no peer, so its only
    /// charts, the server-drawn charts of a Root Cause Analysis, are drawn once, named, too.
    /// </summary>
    public async Task<IReadOnlyList<BenchmarkReportRenderChart>> LoadRenderChartsAsync(
        long documentId, BenchmarkReportPeerNaming naming, CancellationToken ct)
    {
        var d = await _db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(x => x.Id == documentId)
            .Select(x => new { x.Scope, x.FactsJson })
            .FirstOrDefaultAsync(ct);
        bool namedOnly = d != null
            && (d.Scope == BenchmarkReportScope.ChatConsistency
                || (d.Scope != BenchmarkReportScope.Comparison && PeersOf(d.FactsJson).Count == 0));
        string variant = naming == BenchmarkReportPeerNaming.Anonymized && !namedOnly
            ? BenchmarkReportChartStore.Anonymized
            : BenchmarkReportChartStore.Named;
        return await _charts.LoadAsync(documentId, variant, ct);
    }

    /// <summary>The layout a document's charts render with, from its chart manifest; null for the default layout.</summary>
    public BenchmarkReportChartLayout? ReadRenderLayout(long documentId) => _charts.ReadLayout(documentId);

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

    /// <summary>
    /// The scoring fingerprint of each run that still exists, its synthesis part read from
    /// <see cref="BenchmarkRun.SynthesisFingerprint"/>. A run whose column is still null has its two
    /// synthesis columns read and hashed in one second query, and the hash written back.
    /// </summary>
    private async Task<Dictionary<long, RunFingerprint>> CurrentFingerprintsAsync(List<long> runIds, CancellationToken ct)
    {
        if (runIds.Count == 0) return new Dictionary<long, RunFingerprint>();

        var runs = await _db.BenchmarkRuns
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(r => runIds.Contains(r.Id))
            .Select(r => new { r.Id, r.FinalScore, r.QualityIndex, r.SpeedIndex, r.ScoringMethodVersion, r.RerunCompletedAtUtc, r.SynthesisFingerprint })
            .ToListAsync(ct);

        var unfilled = runs.Where(r => r.SynthesisFingerprint == null).Select(r => r.Id).ToList();
        var filled = new Dictionary<long, string>();
        if (unfilled.Count > 0)
        {
            var syntheses = await _db.BenchmarkRuns
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(r => unfilled.Contains(r.Id))
                .Select(r => new { r.Id, r.AssessmentJson, r.CoAssessorSynthesisJson })
                .ToListAsync(ct);
            foreach (var synthesis in syntheses)
            {
                string fingerprint = SynthesisSha256(synthesis.AssessmentJson, synthesis.CoAssessorSynthesisJson);
                filled[synthesis.Id] = fingerprint;
                await BackfillSynthesisFingerprintAsync(synthesis.Id, fingerprint, ct);
            }
        }

        return runs.ToDictionary(
            r => r.Id,
            r => new RunFingerprint(r.FinalScore, r.QualityIndex, r.SpeedIndex, r.ScoringMethodVersion, r.RerunCompletedAtUtc,
                r.SynthesisFingerprint ?? filled.GetValueOrDefault(r.Id) ?? SynthesisSha256(null, null)));
    }

    /// <summary>
    /// Writes a run's missing <see cref="BenchmarkRun.SynthesisFingerprint"/> as one set-based update that
    /// leaves a value already written alone; no entity is tracked. Two concurrent backfills write the same
    /// value. A provider that cannot translate a set-based update (the in-memory test provider) writes it
    /// through the change tracker.
    /// </summary>
    private async Task BackfillSynthesisFingerprintAsync(long runId, string fingerprint, CancellationToken ct)
    {
        if (_db.Database.IsRelational())
        {
            await _db.BenchmarkRuns
                .Where(r => r.Id == runId && r.SynthesisFingerprint == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.SynthesisFingerprint, fingerprint), ct);
            return;
        }

        var run = _db.ChangeTracker.Entries<BenchmarkRun>().Select(e => e.Entity).FirstOrDefault(r => r.Id == runId)
            ?? await _db.BenchmarkRuns.IgnoreAutoIncludes().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run == null || run.SynthesisFingerprint != null) return;
        run.SynthesisFingerprint = fingerprint;
        await _db.SaveChangesAsync(ct);
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
        item.Title = title ?? string.Empty;
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
