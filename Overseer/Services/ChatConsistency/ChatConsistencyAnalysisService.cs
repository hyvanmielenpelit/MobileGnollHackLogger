namespace Overseer.Services.ChatConsistency;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Services.Telemetry;

/// <summary>
/// The GnollBench chat consistency analysis: did the Overseer chat with one model change between a
/// baseline and a comparison period, and which side does the change fit. Reads stored runs only; it
/// makes no model call and spends nothing.
///
/// <para>The steps, in order: evidence; events and segments; the five primary endpoints; Holm across
/// them; verdicts, robustness checks and grades; attribution; minimum detectable effects; the scope
/// statement; data quality; next-run suggestions; persistence of one <see cref="ChatConsistencyAnalysis"/>
/// row. Fixed inputs give fixed results: every resampling is seeded from the protocol and every
/// collection is ordered.</para>
///
/// <para>p-values. Item-paired endpoints (P1, P4, P5) use the Wilcoxon signed-rank test on the per-item
/// differences of the period means, the primary paired test of <see cref="BenchmarkGroupStatistics.Compare"/>;
/// their intervals come from the two-level run-then-item bootstrap. The stratified speed endpoints (P2,
/// P3) use a run-cluster bootstrap p: twice the smaller of the shares of replicates at or below and at or
/// above 0, each with one added to numerator and denominator, capped at 1.</para>
///
/// <para>Units. The statistics count and resample units: a battery run in a battery set, whose member runs
/// are merged into one unit, and a run in a suite set or without a set. A run unit is its run unchanged,
/// so a run-by-run analysis computes exactly what code version 3 did.</para>
/// </summary>
public class ChatConsistencyAnalysisService
{
    /// <summary>The version of this analysis code; stored with every analysis.</summary>
    public const int CurrentAnalysisCodeVersion = 6;

    private const int MaxNameLength = 200;
    private const int MaxSubjectKeyLength = 512;
    private const int MaxRangeLabelLength = 64;
    private const int MaxLeftOutRunIds = 5000;
    private const int MaxRunSelectionNoteRuns = 20;

    private readonly ApplicationDbContext _db;
    private readonly ChatConsistencyEvidenceBuilder _evidence;
    private readonly ILogger<ChatConsistencyAnalysisService> _logger;

    public ChatConsistencyAnalysisService(
        ApplicationDbContext db,
        ChatConsistencyEvidenceBuilder evidence,
        ILogger<ChatConsistencyAnalysisService> logger)
    {
        _db = db;
        _evidence = evidence;
        _logger = logger;
    }

    // --- Analyses --------------------------------------------------------------------------------

    /// <summary>
    /// Runs the analysis and saves it. Refuses, with <see cref="ChatConsistencyRequestException"/>, a
    /// request whose periods are malformed or overlap, whose comparison set or battery run ids are
    /// malformed, whose run selection is malformed or leaves out a selected run or battery run, or whose
    /// periods hold no usable run of the subject. The run selection's bounds are taken as UTC.
    /// </summary>
    public async Task<ChatConsistencyAnalysisResult> AnalyzeAsync(ChatConsistencyAnalysisRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        if (request.RunSelection is { } selection)
        {
            request = request with
            {
                RunSelection = selection with { RangeFromUtc = ToUtc(selection.RangeFromUtc), RangeToUtc = ToUtc(selection.RangeToUtc) }
            };
        }

        var protocol = ChatConsistencyProtocol.V1.WithOverrides(request.ProtocolOverrides);
        var evidence = await _evidence.LoadAsync(request, ct);
        if (evidence.BaselineRuns.Count == 0)
        {
            throw new ChatConsistencyRequestException("The baseline period holds no usable run of this model.");
        }

        if (evidence.ComparisonRuns.Count == 0)
        {
            throw new ChatConsistencyRequestException("The comparison period holds no usable run of this model.");
        }

        var result = Compute(request, protocol, evidence);

        var row = new ChatConsistencyAnalysis
        {
            Name = Truncate(result.Name, MaxNameLength),
            SubjectModelKey = StoredSubjectKey(request.SubjectModelKey),
            SubjectConfigurationId = result.Subject.ConfigurationId,
            BaselineStartUtc = request.BaselineStartUtc,
            BaselineEndUtc = request.BaselineEndUtc,
            ComparisonStartUtc = request.ComparisonStartUtc,
            ComparisonEndUtc = request.ComparisonEndUtc,
            TargetRunIdsJson = JsonSerializer.Serialize(result.Baseline.RunIds.Concat(result.Comparison.RunIds).OrderBy(i => i).ToList()),
            ControlRunIdsJson = JsonSerializer.Serialize(result.Controls.ControlRunIds),
            ProtocolVersion = protocol.ProtocolVersion,
            ProtocolJson = protocol.ToJson(),
            RelaxedPooling = request.RelaxedPooling,
            CommonGraderSnapshotId = result.CommonGrader?.SnapshotId,
            ResultJson = JsonSerializer.Serialize(result, ChatConsistencyJson.Options),
            InputSha256 = result.InputSha256,
            AnalysisCodeVersion = CurrentAnalysisCodeVersion,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ChatConsistencyAnalyses.Add(row);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Chat consistency analysis {AnalysisId} saved for {Subject}: {Headline}",
            row.Id, result.Subject.DisplayName, result.Headline);

        return result with { AnalysisId = row.Id, CreatedAtUtc = row.CreatedAtUtc };
    }

    /// <summary>
    /// Whether saved analysis <paramref name="id"/> is out of date: saved under an earlier analysis code
    /// version, or its inputs changed since. The inputs are checked by repeating the stored request's evidence
    /// load and comparing the input fingerprint with the stored one; nothing is saved and no statistic is
    /// computed. Null when there is no analysis with <paramref name="id"/>.
    /// </summary>
    public async Task<ChatConsistencyFreshness?> CheckFreshnessAsync(int id, CancellationToken ct = default)
    {
        var row = await _db.ChatConsistencyAnalyses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row == null) return null;

        bool earlier = row.AnalysisCodeVersion < CurrentAnalysisCodeVersion;
        bool? changed = null;
        string? note = null;
        if (earlier)
        {
            note = "Not checked: the analysis code changed, so its input fingerprint is not comparable.";
        }
        else
        {
            ChatConsistencyAnalysisRequest? request = null;
            try
            {
                request = JsonSerializer.Deserialize<ChatConsistencyAnalysisResult>(row.ResultJson, ChatConsistencyJson.Options)?.Request;
            }
            catch (JsonException)
            {
            }

            if (request == null)
            {
                note = "Not checked: this analysis does not record how its runs were chosen.";
            }
            else
            {
                try
                {
                    request = WithEndpointMarginKeys(request);
                    var protocol = ChatConsistencyProtocol.V1.WithOverrides(request.ProtocolOverrides);
                    var evidence = await _evidence.LoadAsync(request, ct);
                    changed = !string.Equals(ComputeInputSha256(request, protocol, evidence), row.InputSha256, StringComparison.Ordinal);
                }
                catch (ChatConsistencyRequestException ex)
                {
                    note = "Not checked: " + ex.Message;
                }
            }
        }

        return new ChatConsistencyFreshness
        {
            AnalysisId = row.Id,
            AnalysisCodeVersion = row.AnalysisCodeVersion,
            CurrentAnalysisCodeVersion = CurrentAnalysisCodeVersion,
            EarlierAnalysisCode = earlier,
            InputsChanged = changed,
            InputsNote = note,
            OutOfDate = earlier || changed == true
        };
    }

    /// <summary>
    /// The stored request with its margin overrides keyed by endpoint id again: the result's JSON camelCases
    /// dictionary keys (<c>p1</c>), and the protocol matches ids exactly (<c>P1</c>).
    /// </summary>
    private static ChatConsistencyAnalysisRequest WithEndpointMarginKeys(ChatConsistencyAnalysisRequest request)
    {
        if (request.ProtocolOverrides?.Margins is not { Count: > 0 } margins) return request;

        var ids = ChatConsistencyProtocol.V1.Endpoints.Select(e => e.Id).ToList();
        var keyed = margins.ToDictionary(
            p => ids.FirstOrDefault(id => string.Equals(id, p.Key, StringComparison.OrdinalIgnoreCase)) ?? p.Key,
            p => p.Value,
            StringComparer.Ordinal);
        return request with { ProtocolOverrides = request.ProtocolOverrides with { Margins = keyed } };
    }

    /// <summary>A saved analysis, or null when there is none with <paramref name="id"/>.</summary>
    public async Task<ChatConsistencyAnalysisResult?> GetAnalysisAsync(int id, CancellationToken ct = default)
    {
        var row = await _db.ChatConsistencyAnalyses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row == null) return null;

        var result = JsonSerializer.Deserialize<ChatConsistencyAnalysisResult>(row.ResultJson, ChatConsistencyJson.Options)
                     ?? throw new JsonException("Chat consistency analysis " + id.ToString(CultureInfo.InvariantCulture) + " has no result.");
        return result with { AnalysisId = row.Id, CreatedAtUtc = ChatConsistencyMeasures.AsUtc(row.CreatedAtUtc) };
    }

    /// <summary>Every saved analysis, newest first, without the full results.</summary>
    public async Task<IReadOnlyList<ChatConsistencyAnalysisSummary>> ListAnalysesAsync(CancellationToken ct = default)
    {
        var rows = await _db.ChatConsistencyAnalyses.AsNoTracking().OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id).ToListAsync(ct);
        var ids = rows.Select(r => (int?)r.Id).ToList();
        var documentCounts = (await _db.BenchmarkReportDocuments.AsNoTracking()
                .Where(d => ids.Contains(d.ChatConsistencyAnalysisId))
                .Select(d => d.ChatConsistencyAnalysisId!.Value)
                .ToListAsync(ct))
            .GroupBy(i => i)
            .ToDictionary(g => g.Key, g => g.Count());

        return rows.Select(r =>
        {
            string? headline = null;
            string subjectKey = r.SubjectModelKey;
            string? setKey = null;
            string? setLabel = null;
            ChatConsistencySubjectBrief? subject = null;
            var endpoints = new List<ChatConsistencyEndpointBrief>();
            try
            {
                using var doc = JsonDocument.Parse(r.ResultJson);
                if (doc.RootElement.TryGetProperty("headline", out var h) && h.ValueKind == JsonValueKind.String) headline = h.GetString();
                if (doc.RootElement.TryGetProperty("subject", out var s) && s.ValueKind == JsonValueKind.Object)
                {
                    if (s.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String) subjectKey = k.GetString() ?? subjectKey;
                    subject = new ChatConsistencySubjectBrief
                    {
                        DisplayName = JsonString(s, "displayName") ?? string.Empty,
                        Provider = JsonString(s, "provider") ?? string.Empty,
                        ModelId = JsonString(s, "modelId") ?? string.Empty,
                        ThinkingLevel = JsonString(s, "thinkingLevel"),
                        ServiceTier = JsonString(s, "serviceTier")
                    };
                }

                if (doc.RootElement.TryGetProperty("comparisonSet", out var set) && set.ValueKind == JsonValueKind.Object)
                {
                    if (set.TryGetProperty("key", out var sk) && sk.ValueKind == JsonValueKind.String) setKey = sk.GetString();
                    if (set.TryGetProperty("label", out var sl) && sl.ValueKind == JsonValueKind.String) setLabel = sl.GetString();
                }

                if (doc.RootElement.TryGetProperty("endpoints", out var eps) && eps.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in eps.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
                    {
                        endpoints.Add(new ChatConsistencyEndpointBrief
                        {
                            Id = JsonString(e, "id") ?? string.Empty,
                            Name = JsonString(e, "name") ?? string.Empty,
                            Computed = e.TryGetProperty("computed", out var c) && c.ValueKind == JsonValueKind.True,
                            VerdictLabel = JsonString(e, "verdictLabel") ?? string.Empty,
                            Grade = JsonGrade(e)
                        });
                    }
                }
            }
            catch (JsonException)
            {
            }

            return new ChatConsistencyAnalysisSummary
            {
                Id = r.Id,
                Name = r.Name,
                SubjectModelKey = subjectKey,
                BaselineStartUtc = ChatConsistencyMeasures.AsUtc(r.BaselineStartUtc),
                BaselineEndUtc = ChatConsistencyMeasures.AsUtc(r.BaselineEndUtc),
                ComparisonStartUtc = ChatConsistencyMeasures.AsUtc(r.ComparisonStartUtc),
                ComparisonEndUtc = ChatConsistencyMeasures.AsUtc(r.ComparisonEndUtc),
                ProtocolVersion = r.ProtocolVersion,
                RelaxedPooling = r.RelaxedPooling,
                CommonGraderSnapshotId = r.CommonGraderSnapshotId,
                Headline = headline,
                InputSha256 = r.InputSha256,
                AnalysisCodeVersion = r.AnalysisCodeVersion,
                CreatedAtUtc = ChatConsistencyMeasures.AsUtc(r.CreatedAtUtc),
                ReportDocumentCount = documentCounts.TryGetValue(r.Id, out int n) ? n : 0,
                ComparisonSetKey = setKey,
                ComparisonSetLabel = setLabel,
                Subject = subject,
                Endpoints = endpoints
            };
        }).ToList();
    }

    /// <summary>The string property <paramref name="name"/> of <paramref name="element"/>; null when absent or not a string.</summary>
    private static string? JsonString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// The <c>grade</c> of a stored endpoint, written as a camelCase name (<see cref="ChatConsistencyJson.Options"/>)
    /// or a number; <see cref="ChatConsistencyEvidenceGrade.NotEstablished"/> when absent or unknown.
    /// </summary>
    private static ChatConsistencyEvidenceGrade JsonGrade(JsonElement endpoint)
    {
        if (!endpoint.TryGetProperty("grade", out var value)) return ChatConsistencyEvidenceGrade.NotEstablished;

        if (value.ValueKind == JsonValueKind.String
            && Enum.TryParse<ChatConsistencyEvidenceGrade>(value.GetString(), ignoreCase: true, out var named)
            && Enum.IsDefined(named))
        {
            return named;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)
            && Enum.IsDefined(typeof(ChatConsistencyEvidenceGrade), number))
        {
            return (ChatConsistencyEvidenceGrade)number;
        }

        return ChatConsistencyEvidenceGrade.NotEstablished;
    }

    /// <summary>Deletes a saved analysis; refused while report documents written from it exist.</summary>
    public async Task<ChatConsistencyDeleteResult> DeleteAnalysisAsync(int id, CancellationToken ct = default)
    {
        var row = await _db.ChatConsistencyAnalyses.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row == null) return new ChatConsistencyDeleteResult(false, false, null);

        int documents = await _db.BenchmarkReportDocuments.CountAsync(d => d.ChatConsistencyAnalysisId == id, ct);
        if (documents > 0)
        {
            return new ChatConsistencyDeleteResult(true, false,
                "Refused: " + documents.ToString(CultureInfo.InvariantCulture) + " report document"
                + (documents == 1 ? " was" : "s were") + " written from this analysis. Delete "
                + (documents == 1 ? "it" : "them") + " first.");
        }

        _db.ChatConsistencyAnalyses.Remove(row);
        await _db.SaveChangesAsync(ct);
        return new ChatConsistencyDeleteResult(true, true, null);
    }

    // --- Annotations and anchors -----------------------------------------------------------------

    /// <summary>Annotations, oldest first; filtered to those applying to <paramref name="provider"/> and <paramref name="modelId"/> when given.</summary>
    public async Task<IReadOnlyList<ChatConsistencyAnnotationView>> ListAnnotationsAsync(string? provider = null, string? modelId = null, CancellationToken ct = default)
    {
        var rows = await _db.ChatConsistencyAnnotations.AsNoTracking().ToListAsync(ct);
        return rows
            .Where(a => provider == null || ChatConsistencyMeasures.AnnotationApplies(a, provider, modelId ?? a.ModelId ?? string.Empty))
            .OrderBy(a => a.AtUtc)
            .ThenBy(a => a.Id)
            .Select(ChatConsistencyMeasures.AnnotationView)
            .ToList();
    }

    /// <summary>Adds an annotation. Refuses empty or over-long text and a source that is not an absolute http(s) URL.</summary>
    public async Task<ChatConsistencyAnnotationView> AddAnnotationAsync(ChatConsistencyAnnotationInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        string text = (input.Text ?? string.Empty).Trim();
        if (text.Length == 0) throw new ChatConsistencyRequestException("An annotation needs text.");
        if (text.Length > 1000) throw new ChatConsistencyRequestException("An annotation's text is at most 1,000 characters.");
        if (!Enum.IsDefined(input.Kind)) throw new ChatConsistencyRequestException("Unknown annotation kind.");

        string? provider = Clean(input.Provider);
        string? modelId = Clean(input.ModelId);
        string? source = Clean(input.SourceUrl);
        if (provider != null && provider.Length > 64) throw new ChatConsistencyRequestException("The provider is at most 64 characters.");
        if (modelId != null && modelId.Length > 128) throw new ChatConsistencyRequestException("The model id is at most 128 characters.");
        if (source != null)
        {
            if (source.Length > 512
                || !Uri.TryCreate(source, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ChatConsistencyRequestException("The source must be an absolute http or https URL of at most 512 characters.");
            }
        }

        var row = new ChatConsistencyAnnotation
        {
            AtUtc = ChatConsistencyMeasures.AsUtc(input.AtUtc),
            Provider = provider,
            ModelId = modelId,
            Kind = input.Kind,
            Text = text,
            SourceUrl = source,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.ChatConsistencyAnnotations.Add(row);
        await _db.SaveChangesAsync(ct);
        return ChatConsistencyMeasures.AnnotationView(row);
    }

    /// <summary>Deletes an annotation; false when there is none with <paramref name="id"/>.</summary>
    public async Task<bool> DeleteAnnotationAsync(int id, CancellationToken ct = default)
    {
        var row = await _db.ChatConsistencyAnnotations.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row == null) return false;
        _db.ChatConsistencyAnnotations.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Marks or unmarks a run as the grader anchor; false when there is no run with <paramref name="runId"/>.</summary>
    public async Task<bool> SetAnchorAsync(long runId, bool isAnchor, CancellationToken ct = default)
    {
        var run = await _db.BenchmarkRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run == null) return false;
        if (run.IsConsistencyAnchor != isAnchor)
        {
            run.IsConsistencyAnchor = isAnchor;
            await _db.SaveChangesAsync(ct);
        }

        return true;
    }

    // --- Computation -----------------------------------------------------------------------------

    /// <summary>The analysis of <paramref name="evidence"/> under <paramref name="protocol"/>. Pure.</summary>
    public static ChatConsistencyAnalysisResult Compute(
        ChatConsistencyAnalysisRequest request, ChatConsistencyProtocol protocol, ChatConsistencyEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.BaselineRuns.Count == 0 || evidence.ComparisonRuns.Count == 0)
        {
            throw new ChatConsistencyRequestException("Each period needs at least one usable run of the model.");
        }

        return new Engine(request, protocol, evidence).Run();
    }

    /// <summary>
    /// The input fingerprint <see cref="Compute"/> stores as <see cref="ChatConsistencyAnalysisResult.InputSha256"/>,
    /// from the request, the protocol and the evidence alone, without the statistics. Pure.
    /// </summary>
    public static string ComputeInputSha256(
        ChatConsistencyAnalysisRequest request, ChatConsistencyProtocol protocol, ChatConsistencyEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(evidence);
        return new Engine(request, protocol, evidence).InputSha256();
    }

    /// <summary>Refuses a malformed request.</summary>
    public static void Validate(ChatConsistencyAnalysisRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SubjectModelKey)) throw new ChatConsistencyRequestException("Choose the model to analyze.");
        if (request.BaselineStartUtc > request.BaselineEndUtc) throw new ChatConsistencyRequestException("The baseline period ends before it starts.");
        if (request.ComparisonStartUtc > request.ComparisonEndUtc) throw new ChatConsistencyRequestException("The comparison period ends before it starts.");
        if (request.BaselineStartUtc <= request.ComparisonEndUtc && request.ComparisonStartUtc <= request.BaselineEndUtc)
        {
            throw new ChatConsistencyRequestException("The baseline and comparison periods overlap.");
        }

        if (request.Name != null && request.Name.Length > MaxNameLength)
        {
            throw new ChatConsistencyRequestException("The name is at most 200 characters.");
        }

        if (request.BaselineRunIds != null && request.ComparisonRunIds != null
            && request.BaselineRunIds.Intersect(request.ComparisonRunIds).Any())
        {
            throw new ChatConsistencyRequestException("A run cannot be in both periods.");
        }

        if (request.ComparisonSet is { } set)
        {
            string? prefix = set.Kind switch
            {
                ChatConsistencyComparisonSetKinds.Battery => ChatConsistencyComparisonSetKinds.BatteryKeyPrefix,
                ChatConsistencyComparisonSetKinds.Suite => ChatConsistencyComparisonSetKinds.SuiteKeyPrefix,
                _ => null
            };
            if (prefix == null)
            {
                throw new ChatConsistencyRequestException("The comparison set's kind must be \"battery\" or \"suite\".");
            }

            if (set.Key == null || set.Key.Length <= prefix.Length || !set.Key.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new ChatConsistencyRequestException("The comparison set's key must start with \"" + prefix + "\".");
            }
        }

        bool battery = IsBatterySet(request);
        if (!battery && (request.BaselineBatteryRunIds is { Count: > 0 } || request.ComparisonBatteryRunIds is { Count: > 0 }))
        {
            throw new ChatConsistencyRequestException("Battery run ids need a battery comparison set.");
        }

        if (battery && (request.BaselineRunIds is { Count: > 0 } || request.ComparisonRunIds is { Count: > 0 }))
        {
            throw new ChatConsistencyRequestException("A battery comparison takes battery run ids.");
        }

        if (request.BaselineBatteryRunIds != null && request.ComparisonBatteryRunIds != null
            && request.BaselineBatteryRunIds.Intersect(request.ComparisonBatteryRunIds).Any())
        {
            throw new ChatConsistencyRequestException("A battery run cannot be in both periods.");
        }

        if (request.RunSelection is { } selection) ValidateRunSelection(request, selection);
    }

    private static bool IsBatterySet(ChatConsistencyAnalysisRequest request)
        => request.ComparisonSet?.Kind == ChatConsistencyComparisonSetKinds.Battery;

    /// <summary>Refuses a run selection that is malformed or contradicts the selected runs.</summary>
    private static void ValidateRunSelection(ChatConsistencyAnalysisRequest request, ChatConsistencyRunSelection selection)
    {
        if (selection.RangeLabel != null && selection.RangeLabel.Length > MaxRangeLabelLength)
        {
            throw new ChatConsistencyRequestException("The run selection's date label is at most 64 characters.");
        }

        if (ToUtc(selection.RangeFromUtc) is DateTime rangeFrom && ToUtc(selection.RangeToUtc) is DateTime rangeTo && rangeFrom > rangeTo)
        {
            throw new ChatConsistencyRequestException("The run selection's dates end before they start.");
        }

        var leftOut = selection.LeftOutRunIds ?? Array.Empty<long>();
        if (leftOut.Count > MaxLeftOutRunIds)
        {
            throw new ChatConsistencyRequestException("The run selection leaves out at most 5,000 runs.");
        }

        var baseline = (request.BaselineRunIds ?? Array.Empty<long>()).ToHashSet();
        var comparison = (request.ComparisonRunIds ?? Array.Empty<long>()).ToHashSet();
        foreach (long id in leftOut.Distinct().OrderBy(i => i))
        {
            string? period = baseline.Contains(id) ? "baseline" : comparison.Contains(id) ? "comparison" : null;
            if (period != null)
            {
                throw new ChatConsistencyRequestException("Run #" + Inv(id) + " is left out in step 1 but selected for the " + period + ".");
            }
        }

        var leftOutBatteries = selection.LeftOutBatteryRunIds ?? Array.Empty<long>();
        if (leftOutBatteries.Count > MaxLeftOutRunIds)
        {
            throw new ChatConsistencyRequestException("The run selection leaves out at most 5,000 battery runs.");
        }

        bool namesBatteries = selection.FirstBatteryRunId.HasValue || selection.LastBatteryRunId.HasValue || leftOutBatteries.Count > 0;
        if (namesBatteries && !IsBatterySet(request))
        {
            throw new ChatConsistencyRequestException("The run selection names battery runs, which only a battery comparison set takes.");
        }

        var baselineBatteries = (request.BaselineBatteryRunIds ?? Array.Empty<long>()).ToHashSet();
        var comparisonBatteries = (request.ComparisonBatteryRunIds ?? Array.Empty<long>()).ToHashSet();
        foreach (long id in leftOutBatteries.Distinct().OrderBy(i => i))
        {
            string? period = baselineBatteries.Contains(id) ? "baseline" : comparisonBatteries.Contains(id) ? "comparison" : null;
            if (period != null)
            {
                throw new ChatConsistencyRequestException("Battery run #" + Inv(id) + " is left out in step 1 but selected for the " + period + ".");
            }
        }
    }

    /// <summary>The instant in UTC: a local time is converted, an unspecified kind is UTC as given.</summary>
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static DateTime? ToUtc(DateTime? value) => value.HasValue ? ToUtc(value.Value) : null;

    /// <summary>The selection's left-out run ids, distinct and ascending.</summary>
    private static List<long> LeftOutIds(ChatConsistencyRunSelection? selection)
        => (selection?.LeftOutRunIds ?? Array.Empty<long>()).Distinct().OrderBy(i => i).ToList();

    /// <summary>The selection's left-out battery run ids, distinct and ascending.</summary>
    private static List<long> LeftOutBatteryIds(ChatConsistencyRunSelection? selection)
        => (selection?.LeftOutBatteryRunIds ?? Array.Empty<long>()).Distinct().OrderBy(i => i).ToList();

    /// <summary>The subject key as stored: the key itself, or its SHA-256 when it exceeds the column; the result keeps the full key.</summary>
    public static string StoredSubjectKey(string key)
        => key.Length <= MaxSubjectKeyLength ? key : "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static string Truncate(string value, int length) => value.Length <= length ? value : value.Substring(0, length);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // --- The engine ------------------------------------------------------------------------------

    /// <summary>One endpoint's working state.</summary>
    private sealed class EndpointWork
    {
        public EndpointWork(ChatConsistencyEndpointProtocol protocol) => Protocol = protocol;

        public ChatConsistencyEndpointProtocol Protocol { get; }
        public bool Computed { get; set; }
        public string? NotComputedReason { get; set; }

        /// <summary>One of <see cref="ChatConsistencyNotComputedKinds"/>; null when computed.</summary>
        public string? NotComputedKind { get; set; }

        /// <summary>The compared units' member runs, unit by unit.</summary>
        public List<BenchmarkRun> Baseline { get; set; } = new();
        public List<BenchmarkRun> Comparison { get; set; } = new();
        public List<AnalysisUnit> BaselineUnits { get; set; } = new();
        public List<AnalysisUnit> ComparisonUnits { get; set; } = new();
        public bool Pooled { get; set; }
        public bool LegacyProxy { get; set; }
        public bool UsesLegacy { get; set; }
        public bool CommonGrader { get; set; }
        public Func<BenchmarkRun, BenchmarkRunAnswer, double?> Value { get; set; } = (_, _) => null;
        public bool LogScale { get; set; }
        public BootstrapStatistic Statistic { get; set; } = BootstrapStatistic.HodgesLehmann;
        public double Estimate { get; set; }
        public BootstrapDistribution? Distribution { get; set; }
        public double? PValue { get; set; }
        public double? AdjustedPValue { get; set; }
        public string PValueMethod { get; set; } = string.Empty;
        public int ItemCount { get; set; }
        public List<int> Strata { get; set; } = new();
        public double? StratumExcludedShare { get; set; }
        public Dictionary<int, double> StratumShifts { get; set; } = new();
        public Dictionary<int, (int Baseline, int Comparison)> StratumRunCounts { get; set; } = new();
        public bool MinimumSampleMet { get; set; }
        public string MinimumSampleDetail { get; set; } = string.Empty;
        public List<(long UnitId, double? Estimate)> LeaveOneOut { get; set; } = new();
        public bool RetriesPresent { get; set; }
        public double? RetryFreeEstimate { get; set; }
        public bool TierDataPresent { get; set; }
        public int TierMismatchCount { get; set; }
        public double? TierMatchEstimate { get; set; }
        public MinimumDetectableEffectResult? Mde { get; set; }
        public double? RunStandardDeviation { get; set; }
        public IReadOnlyList<ShiftFunctionDecile>? ShiftFunction { get; set; }
        public ConsistencyVerdict? Verdict { get; set; }
        public ChatConsistencyEvidenceGrade Grade { get; set; } = ChatConsistencyEvidenceGrade.NotEstablished;
        public List<string> GradeReasons { get; set; } = new();
        public List<ChatConsistencyCheck> Checks { get; set; } = new();
        public List<string> Notes { get; set; } = new();
    }

    /// <summary>One timed speed observation, with the unit it belongs to.</summary>
    private readonly record struct SpeedObservation(long UnitId, string Item, int Stratum, double LogValue, bool Retry, bool TierMatch);

    /// <summary>One analyzed unit: a battery run with its member runs, or one run with itself as the member.</summary>
    private sealed record AnalysisUnit(long Id, DateTime StartedAtUtc, IReadOnlyList<BenchmarkRun> Members);

    private sealed class Engine
    {
        private readonly ChatConsistencyAnalysisRequest _request;
        private readonly ChatConsistencyProtocol _protocol;
        private readonly ChatConsistencyEvidence _evidence;
        private readonly List<BenchmarkRun> _baseline;
        private readonly List<BenchmarkRun> _comparison;
        private readonly bool _battery;
        private readonly List<AnalysisUnit> _baselineUnits;
        private readonly List<AnalysisUnit> _comparisonUnits;
        private readonly List<ChatConsistencyNote> _dataQuality = new();
        private readonly Dictionary<long, IReadOnlyDictionary<int, ChatConsistencyCalibrationVerdict>> _commonVerdicts = new();
        private ChatConsistencyCommonGrader? _commonGrader;
        private ComparabilityAssessment _assessment = new(Array.Empty<RunSegments>(), Array.Empty<MeasurementBoundary>(), Array.Empty<SpeedExclusion>());
        private readonly Dictionary<string, EndpointWork> _work = new(StringComparer.Ordinal);
        private int _seedOffset;

        public Engine(ChatConsistencyAnalysisRequest request, ChatConsistencyProtocol protocol, ChatConsistencyEvidence evidence)
        {
            _request = request;
            _protocol = protocol;
            _evidence = evidence;
            _baseline = evidence.BaselineRuns.ToList();
            _comparison = evidence.ComparisonRuns.ToList();
            _battery = evidence.UnitKind == ChatConsistencyComparisonSetKinds.BatteryRunUnit;
            _baselineUnits = UnitsOf(_baseline);
            _comparisonUnits = UnitsOf(_comparison);
        }

        private int NextSeed() => unchecked(_protocol.BootstrapSeed + 7919 * _seedOffset++);

        // --- Units -----------------------------------------------------------------------------

        private string UnitKind => _battery ? ChatConsistencyComparisonSetKinds.BatteryRunUnit : ChatConsistencyComparisonSetKinds.RunUnit;

        /// <summary>"run" or "battery run", as the texts name one unit.</summary>
        private string UnitNoun => _battery ? "battery run" : "run";

        private string UnitsNoun => _battery ? "battery runs" : "runs";

        /// <summary>
        /// The units of <paramref name="runs"/>. Without a battery set, one per run in the given order. In a
        /// battery set, one per battery run, its members in the given order, ordered by start, then id.
        /// </summary>
        private List<AnalysisUnit> UnitsOf(IEnumerable<BenchmarkRun> runs)
        {
            if (!_battery) return runs.Select(r => new AnalysisUnit(r.Id, r.StartedAtUtc, new[] { r })).ToList();

            return runs
                .GroupBy(r => _evidence.UnitIdOf(r.Id))
                .Select(g => new AnalysisUnit(
                    g.Key,
                    _evidence.UnitStartedAtUtc.TryGetValue(g.Key, out var start) ? start : g.Min(r => r.StartedAtUtc),
                    g.ToList()))
                .OrderBy(u => ChatConsistencyMeasures.AsUtc(u.StartedAtUtc))
                .ThenBy(u => u.Id)
                .ToList();
        }

        private static List<BenchmarkRun> MembersOf(IEnumerable<AnalysisUnit> units) => units.SelectMany(u => u.Members).ToList();

        /// <summary>Distinct UTC days the units started on.</summary>
        private static int Days(IEnumerable<AnalysisUnit> units)
            => units.Select(u => ChatConsistencyMeasures.AsUtc(u.StartedAtUtc).Date).Distinct().Count();

        /// <summary>"run #98 is", or "runs #98, #99 are".</summary>
        private static string MemberPhrase(IReadOnlyList<BenchmarkRun> runs)
            => (runs.Count == 1 ? "run " : "runs ") + string.Join(", ", runs.Select(r => "#" + Inv(r.Id))) + (runs.Count == 1 ? " is" : " are");

        /// <summary>What a next run is made on: the battery in a battery set, otherwise the suite of <paramref name="latest"/>.</summary>
        private string RunTarget(BenchmarkRun latest)
            => _battery && _evidence.ComparisonSet is { } set && set.Label.Length > 0 ? set.Label : latest.SuiteName;

        private List<ChatConsistencyUnitView> UnitViews()
        {
            IEnumerable<ChatConsistencyUnitView> Views(string period, List<AnalysisUnit> units) => units
                .OrderBy(u => ChatConsistencyMeasures.AsUtc(u.StartedAtUtc))
                .ThenBy(u => u.Id)
                .Select(u => new ChatConsistencyUnitView
                {
                    UnitId = u.Id,
                    Kind = UnitKind,
                    Period = period,
                    StartedAtUtc = ChatConsistencyMeasures.AsUtc(u.StartedAtUtc),
                    MemberRunIds = u.Members.Select(r => r.Id).ToList()
                });

            return Views("baseline", _baselineUnits).Concat(Views("comparison", _comparisonUnits)).ToList();
        }

        public ChatConsistencyAnalysisResult Run()
        {
            _dataQuality.AddRange(_evidence.Notes);

            // 1–2. Evidence, the common grader, events and segments.
            SelectCommonGrader();
            _assessment = _evidence.Assess(_commonGrader?.CoveredRunIds);

            // 3. Endpoints.
            ComputeQuality();
            ComputeTimeToFirstAnswerText();
            ComputeStreamingRate();
            ComputeWork();
            ComputeCost();

            // 4. Multiplicity: Holm across the primary endpoints that produced a p-value.
            var tested = ChatConsistencyEndpointIds.All.Select(id => _work[id]).Where(w => w.Computed && w.PValue.HasValue).ToList();
            var adjusted = ChatConsistencyStatistics.Holm(tested.Select(w => w.PValue!.Value).ToList());
            for (int i = 0; i < tested.Count; i++) tested[i].AdjustedPValue = adjusted[i];

            // 5. Verdicts, then the secondaries the checks read, then robustness and grades.
            foreach (var w in _work.Values) Decide(w);

            var reliability = ComputeReliability();
            var ownWaits = new[] { OwnWaits("baseline", _baseline), OwnWaits("comparison", _comparison) };
            var drift = ComputeGraderDrift();
            var grossP2 = ComputeGrossTimeToFirstAnswerText();

            foreach (var id in ChatConsistencyEndpointIds.All)
            {
                var w = _work[id];
                if (!w.Computed) continue;
                w.Checks = Checks(w, ownWaits, drift);
                Grade(w);
            }

            // Secondary families.
            var controls = ComputeControls();
            var families = SecondaryFamilies(reliability, grossP2, controls.Effects);

            // 6. Attribution.
            var served = ServedModels();
            var scope = Scope();
            var attribution = ChatConsistencyAttribution.Attribute(AttributionInput(served, scope, families, reliability, ownWaits, grossP2, controls));

            // 7–10. Scope, data quality, limitations, next runs, headline.
            var endpoints = ChatConsistencyEndpointIds.All.Select(id => EndpointResult(_work[id])).ToList();
            AddDataQuality();
            var nextRuns = NextRuns(controls);
            var increases = reliability.Where(r => r.EstablishedIncrease).Select(r => r.Name + " up from "
                + Percent(r.BaselineRate) + " to " + Percent(r.ComparisonRate)).ToList();

            var subject = _evidence.Subject;
            string headline = Headline(subject, endpoints, scope, increases);

            var result = new ChatConsistencyAnalysisResult
            {
                Name = string.IsNullOrWhiteSpace(_request.Name) ? "Chat consistency: " + subject.DisplayName : _request.Name.Trim(),
                Headline = headline,
                HeadlineReliabilityIncreases = increases,
                Subject = subject,
                Scope = scope,
                Baseline = PeriodSummary("baseline", _request.BaselineStartUtc, _request.BaselineEndUtc, _baseline),
                Comparison = PeriodSummary("comparison", _request.ComparisonStartUtc, _request.ComparisonEndUtc, _comparison),
                Protocol = _protocol,
                ProtocolLabel = _protocol.Label,
                Endpoints = endpoints,
                SecondaryFamilies = families,
                RobustnessChecks = endpoints.SelectMany(e => e.RobustnessChecks).ToList(),
                Reliability = reliability,
                Events = _evidence.Events,
                Boundaries = _assessment.Boundaries.Select(b => new ChatConsistencyBoundaryView
                {
                    SubjectKey = b.SubjectKey,
                    FromRunId = b.FromRunId,
                    ToRunId = b.ToRunId,
                    AtUtc = ChatConsistencyMeasures.AsUtc(b.AtUtc),
                    Kind = b.Kind,
                    Axes = b.Axes,
                    Bridged = b.Bridged,
                    Reason = b.Reason
                }).ToList(),
                Segments = SegmentViews(),
                Controls = controls,
                Attribution = attribution,
                ServedModels = served,
                OwnWaits = ownWaits,
                CommonGrader = _commonGrader,
                GraderDrift = drift,
                PriceCard = _evidence.PriceCard,
                Annotations = _evidence.Annotations.Select(ChatConsistencyMeasures.AnnotationView).ToList(),
                DataQuality = _dataQuality,
                Limitations = Limitations(),
                NextRuns = nextRuns,
                RunSelection = RunSelectionView(),
                ComparisonSet = _evidence.ComparisonSet,
                UnitKind = UnitKind,
                Units = UnitViews(),
                PeriodHours = new[] { PeriodHoursOf("baseline", _baseline), PeriodHoursOf("comparison", _comparison) },
                PeriodLevels = new[] { PeriodLevelsOf("baseline", _baseline, _baselineUnits), PeriodLevelsOf("comparison", _comparison, _comparisonUnits) },
                Request = _request,
                AnalysisCodeVersion = CurrentAnalysisCodeVersion
            };

            return result with { InputSha256 = InputSha256() };
        }

        // --- Common grader ---------------------------------------------------------------------

        private void SelectCommonGrader()
        {
            var targetIds = _baseline.Concat(_comparison).Select(r => r.Id).ToHashSet();
            var controlIds = _evidence.ControlRuns.Select(r => r.Id).ToHashSet();

            var usable = _evidence.Calibrations
                .Where(c => c.AssessorModelSnapshotId.HasValue && c.ErrorMessage == null)
                .Select(c => (Calibration: c, Verdicts: ChatConsistencyMeasures.CalibrationVerdicts(c)))
                .Where(x => x.Verdicts.Count > 0)
                .ToList();

            var bySnapshot = usable
                .GroupBy(x => x.Calibration.AssessorModelSnapshotId!.Value)
                .Select(g => new
                {
                    SnapshotId = g.Key,
                    Rows = g.ToList(),
                    Runs = g.Select(x => x.Calibration.BenchmarkRunId).ToHashSet(),
                    Latest = g.Max(x => x.Calibration.CreatedAtUtc)
                })
                .ToList();

            var covering = bySnapshot.Where(s => targetIds.All(s.Runs.Contains)).ToList();
            bool requested = _request.CommonGraderSnapshotId.HasValue;
            var chosen = requested
                ? covering.FirstOrDefault(s => s.SnapshotId == _request.CommonGraderSnapshotId!.Value)
                : covering
                    .OrderByDescending(s => s.Runs.Count(controlIds.Contains))
                    .ThenByDescending(s => s.Latest)
                    .ThenBy(s => s.SnapshotId)
                    .FirstOrDefault();

            if (requested && chosen == null)
            {
                _dataQuality.Add(Note("commonGrader",
                    "The requested common grader (snapshot " + Inv(_request.CommonGraderSnapshotId!.Value)
                    + ") does not cover every target run; native grades are compared."));
            }

            if (chosen == null) return;

            var calibrationIds = new List<long>();
            foreach (var group in chosen.Rows.GroupBy(x => x.Calibration.BenchmarkRunId).OrderBy(g => g.Key))
            {
                var latest = group.OrderBy(x => x.Calibration.CreatedAtUtc).ThenBy(x => x.Calibration.Id).Last();
                _commonVerdicts[group.Key] = latest.Verdicts;
                calibrationIds.Add(latest.Calibration.Id);
            }

            var display = chosen.Rows[0].Calibration.AssessorModelSnapshot.Label() ?? ("snapshot " + Inv(chosen.SnapshotId));
            _commonGrader = new ChatConsistencyCommonGrader
            {
                SnapshotId = chosen.SnapshotId,
                Display = display,
                Requested = requested,
                CalibrationIds = calibrationIds.OrderBy(i => i).ToList(),
                CoveredRunIds = chosen.Runs.OrderBy(i => i).ToList(),
                UncoveredControlRunIds = controlIds.Where(id => !chosen.Runs.Contains(id)).OrderBy(i => i).ToList()
            };
        }

        private double? Quality(BenchmarkRun run, BenchmarkRunAnswer answer)
        {
            if (_commonGrader == null) return ChatConsistencyMeasures.NativeQuality(answer);
            return _commonVerdicts.TryGetValue(run.Id, out var verdicts) && verdicts.TryGetValue(answer.OrderIndex, out var v)
                ? v.Quality
                : null;
        }

        private (bool Critical, double? Accuracy, double? Completeness, double? Conciseness, double? Readability)? Detail(BenchmarkRun run, BenchmarkRunAnswer answer)
        {
            if (_commonGrader == null)
            {
                if (ChatConsistencyMeasures.NativeQuality(answer) == null) return null;
                return (answer.CriticalError, answer.AccuracyLevel, answer.CompletenessLevel, answer.ConcisenessLevel, answer.ReadabilityLevel);
            }

            return _commonVerdicts.TryGetValue(run.Id, out var verdicts) && verdicts.TryGetValue(answer.OrderIndex, out var v)
                ? (v.CriticalError, v.AccuracyLevel, v.CompletenessLevel, v.ConcisenessLevel, v.ReadabilityLevel)
                : null;
        }

        // --- Run selection -----------------------------------------------------------------------

        /// <summary>
        /// The runs of each period usable on <paramref name="axis"/>, within one measurement segment:
        /// the latest segment both periods share. Without one, the runs are pooled under relaxed pooling
        /// and refused otherwise.
        /// </summary>
        private (List<BenchmarkRun> Baseline, List<BenchmarkRun> Comparison, bool Pooled, string? Refusal, string? RefusalKind) SelectRuns(
            ChatConsistencyAxis axis, Func<BenchmarkRun, bool>? filter, string endpointName)
        {
            int? Segment(BenchmarkRun r) => _assessment.RunOf(r.Id)?.SegmentOf(axis);
            var b = _baseline.Where(r => Segment(r).HasValue && (filter == null || filter(r))).ToList();
            var c = _comparison.Where(r => Segment(r).HasValue && (filter == null || filter(r))).ToList();

            if (b.Count == 0 || c.Count == 0)
            {
                string period = b.Count == 0 ? "baseline" : "comparison";
                var excluded = (b.Count == 0 ? _baseline : _comparison)
                    .SelectMany(r => _assessment.SpeedExclusions.Where(x => x.RunId == r.Id && x.Axes.Contains(axis)).Select(x => "#" + Inv(r.Id) + ": " + x.Detail))
                    .ToList();
                return (b, c, false, "No run of the " + period + " period is usable for " + endpointName.ToLowerInvariant()
                    + (excluded.Count > 0 ? Parenthesized(excluded) : string.Empty) + ".", UnusableKind(axis));
            }

            var sb = b.Select(r => Segment(r)!.Value).Distinct().ToList();
            var sc = c.Select(r => Segment(r)!.Value).Distinct().ToList();
            if (sb.Count == 1 && sc.Count == 1 && sb[0] == sc[0]) return (b, c, false, null, null);

            if (_request.RelaxedPooling) return (b, c, true, null, null);

            var common = sb.Intersect(sc).ToList();
            if (common.Count > 0)
            {
                int segment = common.Max();
                var keptB = b.Where(r => Segment(r) == segment).ToList();
                var keptC = c.Where(r => Segment(r) == segment).ToList();
                var dropped = b.Concat(c).Where(r => Segment(r) != segment).Select(r => "#" + Inv(r.Id)).ToList();
                _dataQuality.Add(Note("segment",
                    endpointName + ": runs " + string.Join(", ", dropped) + " were measured differently from the rest and were left out; "
                    + "the comparison uses the latest measurement segment both periods share."));
                return (keptB, keptC, false, null, null);
            }

            return (b, c, false, MeasurementChanged(axis, endpointName), ChatConsistencyNotComputedKinds.MeasurementChanged);
        }

        /// <summary>The kind of a refusal for want of a usable run on <paramref name="axis"/>.</summary>
        private static string UnusableKind(ChatConsistencyAxis axis)
            => axis == ChatConsistencyAxis.SpeedTelemetry ? ChatConsistencyNotComputedKinds.NoTelemetry : ChatConsistencyNotComputedKinds.Other;

        /// <summary>The refusal when the periods share no measurement segment on <paramref name="axis"/>.</summary>
        private string MeasurementChanged(ChatConsistencyAxis axis, string endpointName)
        {
            var reasons = _assessment.Boundaries
                .Where(x => !x.Bridged && x.Axes.Contains(axis) && x.SubjectKey == _evidence.SubjectKey)
                .Select(x => x.Reason.Trim().TrimEnd('.').TrimEnd())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return "The measurement of " + endpointName.ToLowerInvariant() + " changed between the periods"
                + (reasons.Count > 0 ? " (" + string.Join("; ", reasons) + ")" : string.Empty) + ". "
                + (axis == ChatConsistencyAxis.Quality
                    ? "Re-grade every compared run with one assessor (a common grader), or choose relaxed pooling."
                    : "Choose relaxed pooling to pool across the change.");
        }

        /// <summary>
        /// The units of each period usable on <paramref name="axis"/>, within one measurement segment: the
        /// units of <see cref="SelectRuns"/> without a battery set, <see cref="SelectBatteryUnits"/> in one.
        /// </summary>
        private (List<AnalysisUnit> Baseline, List<AnalysisUnit> Comparison, bool Pooled, string? Refusal, string? RefusalKind) SelectUnits(
            ChatConsistencyAxis axis, string endpointName)
        {
            if (_battery) return SelectBatteryUnits(axis, endpointName);
            var (b, c, pooled, refusal, kind) = SelectRuns(axis, null, endpointName);
            return (UnitsOf(b), UnitsOf(c), pooled, refusal, kind);
        }

        /// <summary>
        /// <see cref="SelectRuns"/> for battery runs. A battery run is usable when every member is, and is
        /// kept only when every member lies in the chosen segment: the latest one holding whole battery runs
        /// of both periods. A battery run that loses a member either way is left out whole, with a
        /// <c>segment</c> note; one with no usable member is left out silently, as a run is.
        /// </summary>
        private (List<AnalysisUnit> Baseline, List<AnalysisUnit> Comparison, bool Pooled, string? Refusal, string? RefusalKind) SelectBatteryUnits(
            ChatConsistencyAxis axis, string endpointName)
        {
            int? Segment(BenchmarkRun r) => _assessment.RunOf(r.Id)?.SegmentOf(axis);

            List<AnalysisUnit> Usable(List<AnalysisUnit> units)
            {
                var kept = new List<AnalysisUnit>();
                foreach (var u in units)
                {
                    var unusable = u.Members.Where(r => !Segment(r).HasValue).ToList();
                    if (unusable.Count == 0)
                    {
                        kept.Add(u);
                    }
                    else if (unusable.Count < u.Members.Count)
                    {
                        _dataQuality.Add(Note("segment", endpointName + ": battery run #" + Inv(u.Id) + " was left out: its "
                            + MemberPhrase(unusable) + " not usable for this endpoint."));
                    }
                }

                return kept;
            }

            var b = Usable(_baselineUnits);
            var c = Usable(_comparisonUnits);
            if (b.Count == 0 || c.Count == 0)
            {
                string period = b.Count == 0 ? "baseline" : "comparison";
                var excluded = (b.Count == 0 ? _baseline : _comparison)
                    .SelectMany(r => _assessment.SpeedExclusions.Where(x => x.RunId == r.Id && x.Axes.Contains(axis)).Select(x => "#" + Inv(r.Id) + ": " + x.Detail))
                    .ToList();
                return (b, c, false, "No battery run of the " + period + " period is usable for " + endpointName.ToLowerInvariant()
                    + (excluded.Count > 0 ? Parenthesized(excluded) : string.Empty) + ".", UnusableKind(axis));
            }

            List<int> Segments(List<AnalysisUnit> units) => units.SelectMany(u => u.Members).Select(r => Segment(r)!.Value).Distinct().ToList();
            int? UnitSegment(AnalysisUnit u)
            {
                var segments = u.Members.Select(r => Segment(r)!.Value).Distinct().ToList();
                return segments.Count == 1 ? segments[0] : null;
            }

            var sb = Segments(b);
            var sc = Segments(c);
            if (sb.Count == 1 && sc.Count == 1 && sb[0] == sc[0]) return (b, c, false, null, null);

            if (_request.RelaxedPooling) return (b, c, true, null, null);

            var common = b.Select(UnitSegment).Where(s => s.HasValue).Select(s => s!.Value)
                .Intersect(c.Select(UnitSegment).Where(s => s.HasValue).Select(s => s!.Value))
                .ToList();
            if (common.Count > 0)
            {
                int segment = common.Max();
                foreach (var u in b.Concat(c).Where(x => UnitSegment(x) != segment))
                {
                    var outside = u.Members.Where(r => Segment(r) != segment).ToList();
                    _dataQuality.Add(Note("segment", endpointName + ": battery run #" + Inv(u.Id) + " was left out: its "
                        + MemberPhrase(outside) + " outside the measurement segment; the comparison uses the latest measurement segment "
                        + "both periods share."));
                }

                return (b.Where(u => UnitSegment(u) == segment).ToList(), c.Where(u => UnitSegment(u) == segment).ToList(), false, null, null);
            }

            return (b, c, false, MeasurementChanged(axis, endpointName), ChatConsistencyNotComputedKinds.MeasurementChanged);
        }

        // --- Item-paired endpoints -------------------------------------------------------------

        private void ComputeQuality()
        {
            var w = new EndpointWork(_protocol.Endpoint(ChatConsistencyEndpointIds.Quality))
            {
                Value = Quality,
                LogScale = false,
                Statistic = BootstrapStatistic.Mean,
                CommonGrader = _commonGrader != null
            };
            _work[w.Protocol.Id] = w;

            var (b, c, pooled, refusal, kind) = SelectUnits(ChatConsistencyAxis.Quality, w.Protocol.Name);
            if (refusal != null)
            {
                NotComputed(w, refusal, kind ?? ChatConsistencyNotComputedKinds.Other);
                return;
            }

            ItemPaired(w, b, c, pooled);
        }

        private void ComputeWork()
        {
            var w = new EndpointWork(_protocol.Endpoint(ChatConsistencyEndpointIds.Work))
            {
                Value = (_, a) => ChatConsistencyMeasures.IsDelivered(a) && a.OutputTokens is int o ? o : null,
                LogScale = true,
                Statistic = BootstrapStatistic.HodgesLehmann
            };
            _work[w.Protocol.Id] = w;

            var (b, c, pooled, refusal, kind) = SelectUnits(ChatConsistencyAxis.Work, w.Protocol.Name);
            if (refusal != null)
            {
                NotComputed(w, refusal, kind ?? ChatConsistencyNotComputedKinds.Other);
                return;
            }

            ItemPaired(w, b, c, pooled);
        }

        private void ComputeCost()
        {
            var card = _evidence.Pricing;
            var w = new EndpointWork(_protocol.Endpoint(ChatConsistencyEndpointIds.Cost))
            {
                Value = (run, a) => ChatConsistencyMeasures.IsDelivered(a) && ChatConsistencyMeasures.AnswerCost(a, run, card) is decimal cost
                    ? (double)cost
                    : null,
                LogScale = true,
                Statistic = BootstrapStatistic.HodgesLehmann
            };
            _work[w.Protocol.Id] = w;

            if (card == null)
            {
                NotComputed(w, "No price card resolves for this model: neither its configuration's pricing nor the latest run's pricing snapshot.",
                    ChatConsistencyNotComputedKinds.NoPricing);
                return;
            }

            var (b, c, pooled, refusal, kind) = SelectUnits(ChatConsistencyAxis.Cost, w.Protocol.Name);
            if (refusal != null)
            {
                NotComputed(w, refusal, kind ?? ChatConsistencyNotComputedKinds.Other);
                return;
            }

            ItemPaired(w, b, c, pooled);
        }

        private void ItemPaired(EndpointWork w, List<AnalysisUnit> b, List<AnalysisUnit> c, bool pooled)
        {
            w.BaselineUnits = b;
            w.ComparisonUnits = c;
            w.Baseline = MembersOf(b);
            w.Comparison = MembersOf(c);
            w.Pooled = pooled;
            w.UsesLegacy = w.Baseline.Concat(w.Comparison).Any(r => !r.CallTelemetryVersion.HasValue);

            var mb = b.Select(u => UnitItems(u, w.Value, w.LogScale, null)).ToList();
            var mc = c.Select(u => UnitItems(u, w.Value, w.LogScale, null)).ToList();
            var differences = ChatConsistencyResampling.PairedDifferences(mb, mc);
            if (differences == null)
            {
                NotComputed(w, "No item has a value in both periods.", ChatConsistencyNotComputedKinds.TooFewPairs);
                return;
            }

            var distribution = ChatConsistencyResampling.PairedBootstrap(mb, mc, w.Statistic, true, _protocol.BootstrapReplicates, NextSeed());
            if (distribution == null || distribution.Replicates.Count == 0)
            {
                NotComputed(w, "The bootstrap produced no replicate.", ChatConsistencyNotComputedKinds.Other);
                return;
            }

            w.Computed = true;
            w.Distribution = distribution;
            w.Estimate = distribution.Estimate;
            w.ItemCount = differences.Value.Keys.Length;
            var wilcoxon = ChatConsistencyStatistics.WilcoxonSignedRank(differences.Value.Differences);
            w.PValue = wilcoxon.PValue ?? 1.0;
            w.PValueMethod = "Wilcoxon signed-rank on per-item differences of the period means (" + wilcoxon.Method + ")";

            bool runsOk = b.Count >= _protocol.MinimumRunsPerPeriod && c.Count >= _protocol.MinimumRunsPerPeriod;
            bool daysOk = Days(b) >= _protocol.MinimumDaysPerPeriod && Days(c) >= _protocol.MinimumDaysPerPeriod;
            bool itemsOk = w.ItemCount >= _protocol.MinimumPairedItems;
            w.MinimumSampleMet = runsOk && daysOk && itemsOk;
            w.MinimumSampleDetail = "baseline " + Plural(b.Count, UnitNoun) + " on " + Plural(Days(b), "day") + ", comparison "
                + Plural(c.Count, UnitNoun) + " on " + Plural(Days(c), "day") + ", " + Plural(w.ItemCount, "paired item") + "; the minimum is "
                + Inv(_protocol.MinimumRunsPerPeriod) + " " + UnitsNoun + " on " + Inv(_protocol.MinimumDaysPerPeriod) + " days per period and "
                + Inv(_protocol.MinimumPairedItems) + " paired items.";

            // Leave one unit out.
            if (b.Count >= 2)
            {
                for (int i = 0; i < b.Count; i++)
                {
                    var rest = mb.Where((_, j) => j != i).ToList();
                    w.LeaveOneOut.Add((b[i].Id, ChatConsistencyResampling.PairedPoint(rest, mc, w.Statistic)));
                }
            }

            if (c.Count >= 2)
            {
                for (int i = 0; i < c.Count; i++)
                {
                    var rest = mc.Where((_, j) => j != i).ToList();
                    w.LeaveOneOut.Add((c[i].Id, ChatConsistencyResampling.PairedPoint(mb, rest, w.Statistic)));
                }
            }

            // Retry-free and served-tier sensitivity.
            w.RetriesPresent = w.Baseline.Concat(w.Comparison).SelectMany(r => r.Answers).Any(ChatConsistencyMeasures.HadRetry);
            if (w.RetriesPresent)
            {
                w.RetryFreeEstimate = ChatConsistencyResampling.PairedPoint(
                    b.Select(u => UnitItems(u, w.Value, w.LogScale, (_, a) => !ChatConsistencyMeasures.HadRetry(a))).ToList(),
                    c.Select(u => UnitItems(u, w.Value, w.LogScale, (_, a) => !ChatConsistencyMeasures.HadRetry(a))).ToList(),
                    w.Statistic);
            }

            TierSensitivity(w, filter => ChatConsistencyResampling.PairedPoint(
                b.Select(u => UnitItems(u, w.Value, w.LogScale, filter)).ToList(),
                c.Select(u => UnitItems(u, w.Value, w.LogScale, filter)).ToList(),
                w.Statistic));

            // Minimum detectable effect from the unit-to-unit spread of the unit means; with one unit in a
            // period, from the spread of the per-item paired differences, a floor.
            w.Mde = b.Count >= 2 && c.Count >= 2
                ? ChatConsistencyStatistics.MinimumDetectableEffect(
                    mb.Select(m => (IReadOnlyList<double>)m.Values.ToList()).ToList(),
                    mc.Select(m => (IReadOnlyList<double>)m.Values.ToList()).ToList(),
                    _protocol.Alpha, _protocol.Power)
                : ChatConsistencyStatistics.PairedMinimumDetectableEffect(differences.Value.Differences, _protocol.Alpha, _protocol.Power);
        }

        /// <summary>The served-tier check's data; <paramref name="estimate"/> recomputes the estimate over the answers a filter keeps.</summary>
        private void TierSensitivity(EndpointWork w, Func<Func<BenchmarkRun, BenchmarkRunAnswer, bool>, double?> estimate)
        {
            var answers = w.Baseline.Concat(w.Comparison)
                .SelectMany(r => r.Answers.Where(ChatConsistencyMeasures.IsDelivered).Select(a => (Run: r, Answer: a)))
                .ToList();
            w.TierDataPresent = answers.Any(x =>
                CallTelemetryMeasures.FinalCandidateCall(x.Answer)?.ServedServiceTier != null || x.Answer.ActualServiceTierUsed != null);
            if (!w.TierDataPresent) return;

            w.TierMismatchCount = answers.Count(x => !ChatConsistencyMeasures.AnswerTierMatches(x.Answer, x.Run));
            if (w.TierMismatchCount > 0)
            {
                w.TierMatchEstimate = estimate((r, a) => ChatConsistencyMeasures.AnswerTierMatches(a, r));
            }
        }

        /// <summary>
        /// A unit's item means: its one member's <see cref="RunItems"/> unchanged, or for several members the
        /// mean, per item, of the members' item means.
        /// </summary>
        private static IReadOnlyDictionary<string, double> UnitItems(
            AnalysisUnit unit, Func<BenchmarkRun, BenchmarkRunAnswer, double?> value, bool log, Func<BenchmarkRun, BenchmarkRunAnswer, bool>? filter)
        {
            IReadOnlyDictionary<string, double> Items(BenchmarkRun run)
                => RunItems(run, value, log, filter is { } f ? a => f(run, a) : null);

            if (unit.Members.Count == 1) return Items(unit.Members[0]);

            var sums = new SortedDictionary<string, (double Sum, int Count)>(StringComparer.Ordinal);
            foreach (var member in unit.Members)
            {
                foreach (var pair in Items(member))
                {
                    sums[pair.Key] = sums.TryGetValue(pair.Key, out var s) ? (s.Sum + pair.Value, s.Count + 1) : (pair.Value, 1);
                }
            }

            var items = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var pair in sums) items[pair.Key] = pair.Value.Sum / pair.Value.Count;
            return items;
        }

        private static IReadOnlyDictionary<string, double> RunItems(
            BenchmarkRun run, Func<BenchmarkRun, BenchmarkRunAnswer, double?> value, bool log, Func<BenchmarkRunAnswer, bool>? filter)
        {
            var sums = new SortedDictionary<string, (double Sum, int Count)>(StringComparer.Ordinal);
            foreach (var answer in run.Answers)
            {
                if (filter != null && !filter(answer)) continue;
                if (value(run, answer) is not double v || !double.IsFinite(v)) continue;
                if (log)
                {
                    if (v <= 0.0) continue;
                    v = Math.Log(v);
                }

                string key = ChatConsistencyMeasures.ItemKey(answer);
                sums[key] = sums.TryGetValue(key, out var s) ? (s.Sum + v, s.Count + 1) : (v, 1);
            }

            var items = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var pair in sums) items[pair.Key] = pair.Value.Sum / pair.Value.Count;
            return items;
        }

        // --- Stratified speed endpoints --------------------------------------------------------

        private bool SpeedEligible(BenchmarkRun run, ChatConsistencyAxis axis) => _assessment.RunOf(run.Id)?.SegmentOf(axis).HasValue == true;

        private static double? TelemetryTimeToFirstAnswerText(BenchmarkRun run, BenchmarkRunAnswer answer)
            => ChatConsistencyMeasures.IsDelivered(answer) && CallTelemetryMeasures.TimeToFirstAnswerTextMs(answer) is long t ? t : null;

        private static double? LegacyModelTime(BenchmarkRun run, BenchmarkRunAnswer answer)
            => ChatConsistencyMeasures.IsDelivered(answer) ? answer.ModelTimeMs : null;

        private void ComputeTimeToFirstAnswerText()
        {
            var p = _protocol.Endpoint(ChatConsistencyEndpointIds.TimeToFirstAnswerText);
            var nonParallel = _baseline.Concat(_comparison).Where(r => r.MaxParallelQuestionsUsed <= 1).ToList();
            bool baselineTelemetry = _baseline.Any(r => SpeedEligible(r, ChatConsistencyAxis.SpeedTelemetry));
            bool comparisonTelemetry = _comparison.Any(r => SpeedEligible(r, ChatConsistencyAxis.SpeedTelemetry));
            bool allTelemetry = nonParallel.All(r => r.CallTelemetryVersion.HasValue);
            bool telemetry = baselineTelemetry && comparisonTelemetry && allTelemetry;

            var w = new EndpointWork(p)
            {
                LegacyProxy = !telemetry,
                LogScale = true,
                Value = telemetry ? TelemetryTimeToFirstAnswerText : LegacyModelTime
            };
            _work[p.Id] = w;

            var axis = telemetry ? ChatConsistencyAxis.SpeedTelemetry : ChatConsistencyAxis.SpeedLegacy;
            var (b, c, pooled, refusal, kind) = SelectUnits(axis, p.Name);
            if (refusal != null)
            {
                NotComputed(w, refusal, kind ?? ChatConsistencyNotComputedKinds.Other);
                return;
            }

            if (!telemetry)
            {
                w.Notes.Add("Measured as model time per item (legacy proxy): some compared runs recorded no call telemetry.");
            }

            Stratified(w, b, c, pooled);
        }

        private void ComputeStreamingRate()
        {
            var p = _protocol.Endpoint(ChatConsistencyEndpointIds.StreamingRate);
            var w = new EndpointWork(p)
            {
                LogScale = true,
                Value = (_, a) => ChatConsistencyMeasures.IsDelivered(a) && CallTelemetryMeasures.AnswerStreamingRate(a) is { } rate
                    ? rate.TokensPerSecond
                    : null
            };
            _work[p.Id] = w;

            var (b, c, pooled, refusal, kind) = SelectUnits(ChatConsistencyAxis.SpeedTelemetry, p.Name);
            if (refusal != null)
            {
                NotComputed(w, "The streaming rate needs call telemetry. " + refusal, kind ?? ChatConsistencyNotComputedKinds.NoTelemetry);
                return;
            }

            var compared = MembersOf(b.Concat(c)).SelectMany(r => r.Answers).ToList();
            int unmeasurable = compared.Count(a => ChatConsistencyMeasures.IsDelivered(a) && CallTelemetryMeasures.IsStreamingRateUnmeasurable(a));
            string? unmeasurableNote = unmeasurable > 0
                ? Plural(unmeasurable, "delivered answer") + " " + Agree(unmeasurable, "has", "have") + " no rate: the visible text arrived in one burst after thinking "
                  + $"(a decode span under {CallTelemetryMeasures.MinMeasurableDecodeSpanMs.ToString(CultureInfo.InvariantCulture)} ms "
                  + $"or a rate over {CallTelemetryMeasures.MaxPlausibleTokensPerSecond.ToString("N0", CultureInfo.InvariantCulture)} tokens/s)."
                : null;
            if (compared.Any(a => CallTelemetryMeasures.AnswerStreamingRate(a) is { } rate && rate.Estimated))
            {
                w.Notes.Add("Some rates are estimated from visible characters at 4 characters per token (Anthropic counts thinking inside output tokens)."
                    + (unmeasurableNote != null ? " " + unmeasurableNote : string.Empty));
            }
            else if (unmeasurableNote != null)
            {
                w.Notes.Add(unmeasurableNote);
            }

            Stratified(w, b, c, pooled);
        }

        /// <summary>The timed observations of each unit, its members' in member order.</summary>
        private List<List<SpeedObservation>> SpeedUnits(List<AnalysisUnit> units, Func<BenchmarkRun, BenchmarkRunAnswer, double?> value)
        {
            var result = new List<List<SpeedObservation>>(units.Count);
            foreach (var unit in units)
            {
                var list = new List<SpeedObservation>();
                foreach (var run in unit.Members)
                {
                    foreach (var answer in run.Answers)
                    {
                        if (value(run, answer) is not double v || !double.IsFinite(v)) continue;
                        if (!_evidence.AnswerTimings.TryGetValue(answer.Id, out var timing)) continue;
                        list.Add(new SpeedObservation(
                            unit.Id, ChatConsistencyMeasures.ItemKey(answer), timing.Stratum, Math.Log(Math.Max(1e-9, v)),
                            ChatConsistencyMeasures.HadRetry(answer), ChatConsistencyMeasures.AnswerTierMatches(answer, run)));
                    }
                }

                result.Add(list);
            }

            return result;
        }

        /// <summary>
        /// Item-paired centering: only items observed in both periods stay, and each log value is
        /// centered on its item's mean over both periods, which removes the item's own level.
        /// </summary>
        private static (List<IReadOnlyList<StratifiedObservation>> Baseline, List<IReadOnlyList<StratifiedObservation>> Comparison, int ItemCount) Center(
            List<List<SpeedObservation>> b, List<List<SpeedObservation>> c, Func<SpeedObservation, bool>? filter = null)
        {
            var fb = b.Select(r => r.Where(o => filter == null || filter(o)).ToList()).ToList();
            var fc = c.Select(r => r.Where(o => filter == null || filter(o)).ToList()).ToList();
            var itemsB = fb.SelectMany(r => r).Select(o => o.Item).ToHashSet(StringComparer.Ordinal);
            var itemsC = fc.SelectMany(r => r).Select(o => o.Item).ToHashSet(StringComparer.Ordinal);
            itemsB.IntersectWith(itemsC);

            var centers = fb.Concat(fc).SelectMany(r => r)
                .Where(o => itemsB.Contains(o.Item))
                .GroupBy(o => o.Item, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Average(o => o.LogValue), StringComparer.Ordinal);

            List<IReadOnlyList<StratifiedObservation>> Map(List<List<SpeedObservation>> runs) => runs
                .Select(r => (IReadOnlyList<StratifiedObservation>)r
                    .Where(o => itemsB.Contains(o.Item))
                    .Select(o => new StratifiedObservation(o.Stratum, o.LogValue - centers[o.Item]))
                    .ToList())
                .ToList();

            return (Map(fb), Map(fc), itemsB.Count);
        }

        private static double? StratifiedPoint(
            List<List<SpeedObservation>> b, List<List<SpeedObservation>> c, Func<SpeedObservation, bool>? filter = null)
        {
            var (cb, cc, items) = Center(b, c, filter);
            if (items == 0) return null;
            return ChatConsistencyStatistics.StratifiedShift(cb.SelectMany(r => r).ToList(), cc.SelectMany(r => r).ToList()).Shift;
        }

        private void Stratified(EndpointWork w, List<AnalysisUnit> b, List<AnalysisUnit> c, bool pooled)
        {
            w.BaselineUnits = b;
            w.ComparisonUnits = c;
            w.Baseline = MembersOf(b);
            w.Comparison = MembersOf(c);
            w.Pooled = pooled;
            w.UsesLegacy = w.Baseline.Concat(w.Comparison).Any(r => !r.CallTelemetryVersion.HasValue);

            var ob = SpeedUnits(b, w.Value);
            var oc = SpeedUnits(c, w.Value);
            var (cb, cc, items) = Center(ob, oc);
            if (items == 0)
            {
                NotComputed(w, "No item has a value in both periods.", ChatConsistencyNotComputedKinds.TooFewPairs);
                return;
            }

            var (point, distribution) = ChatConsistencyResampling.StratifiedRunBootstrap(cb, cc, _protocol.BootstrapReplicates, NextSeed());
            if (!point.Shift.HasValue || distribution == null || distribution.Replicates.Count == 0)
            {
                NotComputed(w, "The periods share no time-of-week stratum, so their times cannot be compared on common support.",
                    ChatConsistencyNotComputedKinds.NoCommonStratum);
                return;
            }

            w.Computed = true;
            w.Distribution = distribution;
            w.Estimate = point.Shift.Value;
            w.ItemCount = items;
            w.Strata = point.StrataUsed.ToList();
            w.StratumShifts = point.StratumShifts.ToDictionary(p => p.Key, p => p.Value);
            w.StratumExcludedShare = point.ExcludedShare;
            w.PValue = ChatConsistencyResampling.BootstrapPValue(distribution);
            w.PValueMethod = (_battery ? "battery-run-cluster" : "run-cluster") + " bootstrap, two-sided (share of replicates on the far side of 0, doubled)";

            foreach (int s in w.Strata)
            {
                int nb = cb.Count(r => r.Any(o => o.Stratum == s));
                int nc = cc.Count(r => r.Any(o => o.Stratum == s));
                w.StratumRunCounts[s] = (nb, nc);
            }

            var best = w.StratumRunCounts.OrderByDescending(p => Math.Min(p.Value.Baseline, p.Value.Comparison)).ThenBy(p => p.Key).First();
            w.MinimumSampleMet = w.StratumRunCounts.Values.Any(v => v.Baseline >= _protocol.MinimumSpeedRunsPerStratum && v.Comparison >= _protocol.MinimumSpeedRunsPerStratum);
            w.MinimumSampleDetail = "best common stratum " + ChatConsistencyStatistics.StratumLabel(best.Key) + ": baseline "
                + Plural(best.Value.Baseline, UnitNoun) + ", comparison " + Plural(best.Value.Comparison, UnitNoun) + "; the minimum is "
                + Inv(_protocol.MinimumSpeedRunsPerStratum) + " " + UnitsNoun + " per period in at least one common stratum.";

            if (b.Count >= 2)
            {
                for (int i = 0; i < b.Count; i++)
                {
                    w.LeaveOneOut.Add((b[i].Id, StratifiedPoint(ob.Where((_, j) => j != i).ToList(), oc)));
                }
            }

            if (c.Count >= 2)
            {
                for (int i = 0; i < c.Count; i++)
                {
                    w.LeaveOneOut.Add((c[i].Id, StratifiedPoint(ob, oc.Where((_, j) => j != i).ToList())));
                }
            }

            w.RetriesPresent = ob.Concat(oc).SelectMany(r => r).Any(o => o.Retry);
            if (w.RetriesPresent) w.RetryFreeEstimate = StratifiedPoint(ob, oc, o => !o.Retry);

            TierSensitivity(w, _ => StratifiedPoint(ob, oc, o => o.TierMatch));

            // Unit medians of the centered values, for the minimum detectable effect and the unit-to-unit spread.
            var common = w.Strata.ToHashSet();
            List<IReadOnlyList<double>> RunValues(List<IReadOnlyList<StratifiedObservation>> runs) => runs
                .Select(r => (IReadOnlyList<double>)r.Where(o => common.Contains(o.Stratum)).Select(o => o.Value).ToList())
                .Where(r => r.Count > 0)
                .ToList();
            var rb = RunValues(cb);
            var rc = RunValues(cc);
            w.Mde = ChatConsistencyStatistics.MinimumDetectableEffect(rb, rc, _protocol.Alpha, _protocol.Power);
            w.RunStandardDeviation = PooledStandardDeviation(
                rb.Select(r => BenchmarkGroupStatistics.Median(r)!.Value).ToList(),
                rc.Select(r => BenchmarkGroupStatistics.Median(r)!.Value).ToList());

            var xs = cb.SelectMany(r => r).Where(o => common.Contains(o.Stratum)).Select(o => o.Value).ToList();
            var ys = cc.SelectMany(r => r).Where(o => common.Contains(o.Stratum)).Select(o => o.Value).ToList();
            w.ShiftFunction = ChatConsistencyStatistics.ShiftFunction(xs, ys, 0.95, _protocol.BootstrapReplicates, NextSeed());
        }

        /// <summary>P2 with Overseer's own waits left in, for the infrastructure row of the attribution table.</summary>
        private ChatConsistencySecondaryResult? ComputeGrossTimeToFirstAnswerText()
        {
            var net = _work[ChatConsistencyEndpointIds.TimeToFirstAnswerText];
            if (!net.Computed || net.LegacyProxy) return null;

            Func<BenchmarkRun, BenchmarkRunAnswer, double?> gross = (_, a) =>
                ChatConsistencyMeasures.IsDelivered(a) && CallTelemetryMeasures.TimeToFirstAnswerTextMs(a) is long t
                    ? t + (a.PermitWaitMs ?? 0L) + (a.BackoffWaitMs ?? 0L)
                    : null;

            var (cb, cc, items) = Center(SpeedUnits(net.BaselineUnits, gross), SpeedUnits(net.ComparisonUnits, gross));
            if (items == 0) return null;
            var (point, distribution) = ChatConsistencyResampling.StratifiedRunBootstrap(cb, cc, _protocol.BootstrapReplicates, NextSeed());
            if (!point.Shift.HasValue || distribution == null || distribution.Replicates.Count == 0) return null;

            double p = ChatConsistencyResampling.BootstrapPValue(distribution);
            var verdict = ChatConsistencyStatistics.Verdict(p, distribution, net.Protocol.Margin, false, _protocol.Alpha);
            return new ChatConsistencySecondaryResult
            {
                Id = "grossTimeToFirstAnswerText",
                Name = "Time to first answer text, own waits included",
                Unit = "log ratio",
                Estimate = Fin(point.Shift.Value),
                Ci95 = Interval(distribution.Interval95),
                PValue = Fin(p),
                Method = "as P2, without subtracting permit and retry-backoff waits; unadjusted p",
                ItemCount = items,
                Verdict = verdict
            };
        }

        // --- Verdicts, checks and grades -------------------------------------------------------

        private void Decide(EndpointWork w)
        {
            if (!w.Computed || w.Distribution == null) return;
            w.Verdict = ChatConsistencyStatistics.Verdict(
                w.AdjustedPValue ?? double.NaN, w.Distribution, w.Protocol.Margin, w.Protocol.HigherIsBetter, _protocol.Alpha);
        }

        /// <summary>
        /// Whether a sensitivity estimate keeps the verdict: inside the margin for Equivalent, the same
        /// sign for a change; null when there is no decisive verdict or no estimate.
        /// </summary>
        private static bool? Holds(EndpointWork w, double? sensitivity)
        {
            if (!sensitivity.HasValue || w.Verdict is null or ConsistencyVerdict.Inconclusive) return null;
            if (w.Verdict == ConsistencyVerdict.Equivalent) return Math.Abs(sensitivity.Value) < w.Protocol.Margin;
            return sensitivity.Value != 0.0 && Math.Sign(sensitivity.Value) == Math.Sign(w.Estimate);
        }

        private List<ChatConsistencyCheck> Checks(EndpointWork w, IReadOnlyList<ChatConsistencyOwnWaits> ownWaits, IReadOnlyList<ChatConsistencyGraderDrift> drift)
        {
            var checks = new List<ChatConsistencyCheck>();
            ChatConsistencyCheck Check(string name, ChatConsistencyCheckStatus status, string detail)
                => new() { EndpointId = w.Protocol.Id, Name = name, Status = status, Detail = detail };
            bool decisive = w.Verdict is not null and not ConsistencyVerdict.Inconclusive;

            // Leave one run out.
            if (!decisive)
            {
                checks.Add(Check("Leave-one-run-out stability", ChatConsistencyCheckStatus.NotAssessable, "No decisive verdict to check."));
            }
            else if (w.LeaveOneOut.Count == 0)
            {
                checks.Add(Check("Leave-one-run-out stability", ChatConsistencyCheckStatus.NotAssessable, "Each period has one " + UnitNoun + "."));
            }
            else
            {
                var broken = w.LeaveOneOut.Where(x => Holds(w, x.Estimate) == false).ToList();
                checks.Add(broken.Count == 0
                    ? Check("Leave-one-run-out stability", ChatConsistencyCheckStatus.Passed,
                        "The verdict holds without any one of the " + Inv(w.LeaveOneOut.Count) + " " + UnitsNoun + ".")
                    : Check("Leave-one-run-out stability", ChatConsistencyCheckStatus.Failed,
                        "Without " + UnitNoun + " " + string.Join(", ", broken.Select(x => "#" + Inv(x.UnitId) + " (estimate " + Number(x.Estimate) + ")"))
                        + " the verdict does not hold."));
            }

            // Across sampled strata (speed).
            if (w.Protocol.Stratified)
            {
                var eligible = EligibleStrata(w);
                if (!decisive)
                {
                    checks.Add(Check("Across sampled strata", ChatConsistencyCheckStatus.NotAssessable, "No decisive verdict to check."));
                }
                else if (eligible.Count < 2)
                {
                    checks.Add(Check("Across sampled strata", ChatConsistencyCheckStatus.NotAssessable,
                        w.Strata.Count == 1
                            ? "One time stratum: " + ChatConsistencyStatistics.StratumLabel(w.Strata[0]) + "."
                            : "Fewer than two common strata hold " + Inv(_protocol.MinimumRunsPerStratumForSignCheck) + " " + UnitsNoun + " in each period."));
                }
                else
                {
                    var broken = eligible.Where(s => Holds(w, w.StratumShifts[s]) == false).ToList();
                    checks.Add(broken.Count == 0
                        ? Check("Across sampled strata", ChatConsistencyCheckStatus.Passed,
                            "The verdict holds in each of " + Inv(eligible.Count) + " common strata.")
                        : Check("Across sampled strata", ChatConsistencyCheckStatus.Failed,
                            "The verdict does not hold in " + string.Join(", ", broken.Select(ChatConsistencyStatistics.StratumLabel)) + "."));
                }
            }

            // Own-side explanations: retries, and for speed the own waits.
            var parts = new List<(bool? Holds, string Text)>();
            if (decisive && w.RetriesPresent)
            {
                bool? held = Holds(w, w.RetryFreeEstimate);
                parts.Add((held, "restricted to answers without retries the estimate is " + Number(w.RetryFreeEstimate)
                    + (held == false ? " and the verdict does not hold" : string.Empty)));
            }
            else if (decisive)
            {
                parts.Add((true, "no answer was retried"));
            }

            if (decisive && w.Protocol.Stratified)
            {
                var baselineShare = ownWaits[0].OwnWaitShare;
                var comparisonShare = ownWaits[1].OwnWaitShare;
                if (baselineShare.HasValue && comparisonShare.HasValue)
                {
                    bool moved = Math.Abs(comparisonShare.Value - baselineShare.Value) >= _protocol.OwnWaitShareMaterialChange;
                    bool netMeasure = w.Protocol.Id == ChatConsistencyEndpointIds.TimeToFirstAnswerText && !w.LegacyProxy;
                    parts.Add((!moved || netMeasure,
                        "the own-wait share moved from " + Percent(baselineShare) + " to " + Percent(comparisonShare)
                        + (netMeasure ? ", and the measure is net of own waits" : string.Empty)));
                }

                parts.Add((true, "runs answering questions in parallel are excluded"));
            }

            if (!decisive)
            {
                checks.Add(Check("No unexamined own-side explanation", ChatConsistencyCheckStatus.NotAssessable, "No decisive verdict to check."));
            }
            else
            {
                var status = parts.Any(p => p.Holds == false) ? ChatConsistencyCheckStatus.Failed : ChatConsistencyCheckStatus.Passed;
                checks.Add(Check("No unexamined own-side explanation", status, Capitalize(string.Join("; ", parts.Select(p => p.Text))) + "."));
            }

            // Grader stability (quality).
            if (w.Protocol.Id == ChatConsistencyEndpointIds.Quality)
            {
                if (_commonGrader != null)
                {
                    checks.Add(Check("Grader stability", ChatConsistencyCheckStatus.Passed,
                        "Common grader " + _commonGrader.Display + " re-graded every compared run."));
                }
                else if (drift.Count > 0)
                {
                    var beyond = drift.Where(d => !d.WithinMargin).ToList();
                    checks.Add(beyond.Count == 0
                        ? Check("Grader stability", ChatConsistencyCheckStatus.Passed,
                            "The anchor shows no grader drift beyond ±" + Number(_protocol.GraderDriftMargin) + " points ("
                            + string.Join(", ", drift.Select(d => "run #" + Inv(d.AnchorRunId) + ": " + Number(d.Drift))) + ").")
                        : Check("Grader stability", ChatConsistencyCheckStatus.Failed,
                            "The anchor shows grader drift beyond ±" + Number(_protocol.GraderDriftMargin) + " points ("
                            + string.Join(", ", beyond.Select(d => "run #" + Inv(d.AnchorRunId) + ": " + Number(d.Drift))) + ")."));
                }
                else
                {
                    checks.Add(Check("Grader stability", ChatConsistencyCheckStatus.Failed,
                        "No common-grader re-grade covers every compared run, and no anchor run measures grader drift."));
                }
            }

            // Two units on two days per period.
            var ub = w.BaselineUnits;
            var uc = w.ComparisonUnits;
            bool spread = ub.Count >= _protocol.MinimumRunsPerPeriod && uc.Count >= _protocol.MinimumRunsPerPeriod
                && Days(ub) >= _protocol.MinimumDaysPerPeriod && Days(uc) >= _protocol.MinimumDaysPerPeriod;
            checks.Add(Check("Runs on separate days", spread ? ChatConsistencyCheckStatus.Passed : ChatConsistencyCheckStatus.Failed,
                "Baseline " + Plural(ub.Count, UnitNoun) + " on " + Plural(Days(ub), "day") + "; comparison "
                + Plural(uc.Count, UnitNoun) + " on " + Plural(Days(uc), "day") + "."));

            // Served tier.
            if (!w.TierDataPresent)
            {
                checks.Add(Check("Served-tier match", ChatConsistencyCheckStatus.NotAssessable, "No served service tier was recorded."));
            }
            else if (w.TierMismatchCount == 0)
            {
                checks.Add(Check("Served-tier match", ChatConsistencyCheckStatus.Passed, "Every answer was served at the requested tier."));
            }
            else
            {
                bool? held = Holds(w, w.TierMatchEstimate);
                checks.Add(Check("Served-tier match",
                    held == false ? ChatConsistencyCheckStatus.Failed : held == true ? ChatConsistencyCheckStatus.Passed : ChatConsistencyCheckStatus.NotAssessable,
                    Plural(w.TierMismatchCount, "answer") + " " + Agree(w.TierMismatchCount, "was", "were") + " served at another tier; restricted to matching answers the estimate is "
                    + Number(w.TierMatchEstimate) + "."));
            }

            return checks;
        }

        private List<int> EligibleStrata(EndpointWork w)
            => w.StratumRunCounts
                .Where(p => p.Value.Baseline >= _protocol.MinimumRunsPerStratumForSignCheck && p.Value.Comparison >= _protocol.MinimumRunsPerStratumForSignCheck)
                .Select(p => p.Key)
                .OrderBy(s => s)
                .ToList();

        private void Grade(EndpointWork w)
        {
            if (!w.Computed)
            {
                w.Grade = ChatConsistencyEvidenceGrade.NotEstablished;
                return;
            }

            if (w.Verdict is null or ConsistencyVerdict.Inconclusive)
            {
                w.Grade = ChatConsistencyEvidenceGrade.NotEstablished;
                w.GradeReasons.Add("Inconclusive: the data cannot tell a change from no change at the margin " + w.Protocol.MarginText + ".");
                return;
            }

            foreach (var check in w.Checks.Where(c => c.Status == ChatConsistencyCheckStatus.Failed))
            {
                w.GradeReasons.Add("Robustness check failed: " + check.Name + ".");
            }

            if (w.LegacyProxy) w.GradeReasons.Add("Legacy latency proxy: model time per item, not telemetry.");
            else if (w.UsesLegacy) w.GradeReasons.Add("Legacy data: some compared runs recorded no call telemetry.");
            if (!w.MinimumSampleMet) w.GradeReasons.Add("Below the minimum sample: " + w.MinimumSampleDetail);
            if (w.Pooled) w.GradeReasons.Add("Relaxed pooling across a measurement boundary.");

            w.Grade = w.GradeReasons.Count == 0 ? ChatConsistencyEvidenceGrade.Established : ChatConsistencyEvidenceGrade.Indicated;
        }

        private void NotComputed(EndpointWork w, string reason, string kind)
        {
            w.Computed = false;
            w.NotComputedReason = reason;
            w.NotComputedKind = kind;
            w.Grade = ChatConsistencyEvidenceGrade.NotEstablished;
        }

        private ChatConsistencyEndpointResult EndpointResult(EndpointWork w)
        {
            var p = w.Protocol;
            bool log = p.Scale == ChatConsistencyEffectScale.LogRatio;
            string direction = p.WorkDirection ? "work" : p.HigherIsBetter ? "higherIsBetter" : "lowerIsBetter";
            if (!w.Computed)
            {
                return new ChatConsistencyEndpointResult
                {
                    Id = p.Id, Name = p.Name, Unit = p.Unit, Scale = p.Scale, Margin = p.Margin, MarginText = p.MarginText,
                    Direction = direction, Computed = false, NotComputedReason = w.NotComputedReason, NotComputedKind = w.NotComputedKind,
                    VerdictLabel = "not computable",
                    Grade = ChatConsistencyEvidenceGrade.NotEstablished, GradeReasons = new[] { "Not computable: " + w.NotComputedReason },
                    LegacyProxy = w.LegacyProxy, CommonGrader = w.CommonGrader
                };
            }

            var ci95 = w.Distribution!.Interval95;
            var ci90 = w.Distribution.Interval90;
            int? runsForMargin = null;
            if (w.Mde is { CapNote: false } mde && mde.StandardDeviation > 0.0)
            {
                double z = ChatConsistencyStatistics.NormalQuantile(1.0 - _protocol.Alpha / 2.0) + ChatConsistencyStatistics.NormalQuantile(_protocol.Power);
                double n = 2.0 * Math.Pow(z * mde.StandardDeviation / p.Margin, 2.0);
                runsForMargin = Math.Max(2, (int)Math.Ceiling(n));
            }

            return new ChatConsistencyEndpointResult
            {
                Id = p.Id,
                Name = p.Name,
                Unit = p.Unit,
                Scale = p.Scale,
                Margin = p.Margin,
                MarginText = p.MarginText,
                Direction = direction,
                Computed = true,
                Estimate = Fin(w.Estimate),
                EstimatePercent = log ? Fin(ToPercent(w.Estimate)) : null,
                Ci95 = Interval(ci95),
                Ci90 = Interval(ci90),
                Ci95Percent = log ? Interval((ToPercent(ci95.Lower), ToPercent(ci95.Upper))) : null,
                PValue = Fin(w.PValue),
                AdjustedPValue = Fin(w.AdjustedPValue),
                PValueMethod = w.PValueMethod,
                Verdict = w.Verdict,
                VerdictLabel = VerdictLabel(w.Verdict, p.WorkDirection),
                Grade = w.Grade,
                GradeReasons = w.GradeReasons.Concat(w.Notes).ToList(),
                MinimumDetectableEffect = w.Mde == null ? null : Fin(w.Mde.Effect),
                MinimumDetectableEffectPercent = w.Mde == null || !log ? null : Fin(ToPercent(w.Mde.Effect)),
                MinimumDetectableEffectNote = w.Mde?.Note,
                RunsPerPeriodForMargin = runsForMargin,
                MinimumSampleMet = w.MinimumSampleMet,
                MinimumSampleDetail = w.MinimumSampleDetail,
                LegacyProxy = w.LegacyProxy,
                UsesLegacyData = w.UsesLegacy,
                CommonGrader = w.CommonGrader,
                RelaxedPooling = w.Pooled,
                BaselineRunCount = w.Baseline.Count,
                ComparisonRunCount = w.Comparison.Count,
                BaselineRunIds = w.Baseline.Select(r => r.Id).ToList(),
                ComparisonRunIds = w.Comparison.Select(r => r.Id).ToList(),
                ItemCount = w.ItemCount,
                StrataUsed = w.Strata.Select(ChatConsistencyStatistics.StratumLabel).ToList(),
                StratumExcludedShare = Fin(w.StratumExcludedShare),
                RobustnessChecks = w.Checks
            };
        }

        // --- Reliability, own waits, served models -------------------------------------------

        private List<ChatConsistencyRateResult> ComputeReliability()
        {
            var ab = _baseline.SelectMany(r => r.Answers).ToList();
            var ac = _comparison.SelectMany(r => r.Answers).ToList();
            var cb = _baseline.Where(r => r.CallTelemetryVersion.HasValue).SelectMany(r => r.Answers).SelectMany(CallTelemetryMeasures.CandidateCalls).ToList();
            var cc = _comparison.Where(r => r.CallTelemetryVersion.HasValue).SelectMany(r => r.Answers).SelectMany(CallTelemetryMeasures.CandidateCalls).ToList();
            var tb = _baseline.Where(r => r.CallTelemetryVersion.HasValue).SelectMany(r => r.Answers).ToList();
            var tc = _comparison.Where(r => r.CallTelemetryVersion.HasValue).SelectMany(r => r.Answers).ToList();

            var rates = new List<ChatConsistencyRateResult>
            {
                RateOf("terminalFailures", "Terminal failures", "answers", ab.Count(ChatConsistencyMeasures.IsTerminalFailure), ab.Count, ac.Count(ChatConsistencyMeasures.IsTerminalFailure), ac.Count),
                RateOf("timeouts", "Timeouts", "answers", ab.Count(ChatConsistencyMeasures.IsTimeout), ab.Count, ac.Count(ChatConsistencyMeasures.IsTimeout), ac.Count),
                RateOf("emptyAnswers", "Empty answers", "answers", ab.Count(ChatConsistencyMeasures.IsEmptyAnswer), ab.Count, ac.Count(ChatConsistencyMeasures.IsEmptyAnswer), ac.Count),
                RateOf("refusals", "Refusals", "answers", tb.Count(ChatConsistencyMeasures.IsRefusal), tb.Count, tc.Count(ChatConsistencyMeasures.IsRefusal), tc.Count),
                RateOf("toolBudgetExhausted", "Tool-budget exhaustion", "answers", ab.Count(ChatConsistencyMeasures.IsToolBudgetExhausted), ab.Count, ac.Count(ChatConsistencyMeasures.IsToolBudgetExhausted), ac.Count),
                RateOf("http429", "429 responses", "calls", cb.Count(c => c.Http429Count > 0), cb.Count, cc.Count(c => c.Http429Count > 0), cc.Count),
                RateOf("http5xx", "5xx responses", "calls", cb.Count(c => c.Http5xxCount > 0), cb.Count, cc.Count(c => c.Http5xxCount > 0), cc.Count)
            };

            var tested = rates.Select((r, i) => (Rate: r, Index: i)).Where(x => x.Rate.PValue.HasValue).ToList();
            var bh = ChatConsistencyStatistics.BenjaminiHochberg(tested.Select(x => x.Rate.PValue!.Value).ToList(), _protocol.SecondaryFalseDiscoveryRate);

            bool sample = _baselineUnits.Count >= _protocol.MinimumRunsPerPeriod && _comparisonUnits.Count >= _protocol.MinimumRunsPerPeriod
                && Days(_baselineUnits) >= _protocol.MinimumDaysPerPeriod && Days(_comparisonUnits) >= _protocol.MinimumDaysPerPeriod;

            foreach (var fdr in bh)
            {
                var (rate, index) = tested[fdr.Index];
                rates[index] = rate with
                {
                    AdjustedPValue = Fin(fdr.AdjustedPValue),
                    EstablishedIncrease = fdr.Rejected && rate.Increased && sample
                };
            }

            return rates;
        }

        private static ChatConsistencyRateResult RateOf(string id, string name, string denominator, int kb, int nb, int kc, int nc)
        {
            double? rb = nb > 0 ? kb / (double)nb : null;
            double? rc = nc > 0 ? kc / (double)nc : null;
            double? p = nb > 0 && nc > 0 ? ChatConsistencyStatistics.FisherExactTwoSided(kb, nb - kb, kc, nc - kc) : null;
            return new ChatConsistencyRateResult
            {
                Id = id,
                Name = name,
                Denominator = denominator,
                BaselineCount = kb,
                BaselineTotal = nb,
                BaselineRate = rb,
                BaselineCi95 = Wilson(kb, nb),
                ComparisonCount = kc,
                ComparisonTotal = nc,
                ComparisonRate = rc,
                ComparisonCi95 = Wilson(kc, nc),
                PValue = p,
                Increased = rb.HasValue && rc.HasValue && rc.Value > rb.Value
            };
        }

        private static ChatConsistencyInterval? Wilson(int k, int n)
            => n > 0 && ChatConsistencyStatistics.Wilson95(k, n) is { } ci ? new ChatConsistencyInterval(ci.Low, ci.High) : null;

        private static ChatConsistencyOwnWaits OwnWaits(string period, List<BenchmarkRun> runs)
        {
            var answers = runs.SelectMany(r => r.Answers).Where(CallTelemetryMeasures.HasCandidateTelemetry).ToList();
            return new ChatConsistencyOwnWaits
            {
                Period = period,
                PermitWaitMs = answers.Sum(a => a.PermitWaitMs ?? 0L),
                BackoffWaitMs = answers.Sum(a => a.BackoffWaitMs ?? 0L),
                ModelTimeMs = answers.Sum(a => a.ModelTimeMs),
                OwnWaitShare = Fin(CallTelemetryMeasures.OwnWaitShare(answers)),
                RetryAttemptCount = answers.Sum(a => a.RetryAttemptCount ?? 0),
                AnswersWithTelemetry = answers.Count
            };
        }

        private ChatConsistencyServedModels ServedModels()
        {
            IReadOnlyList<ChatConsistencyServedModelCount> Aggregate(List<BenchmarkRun> runs) => runs
                .SelectMany(ChatConsistencyMeasures.ServedModels)
                .GroupBy(s => s.ModelId, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new ChatConsistencyServedModelCount(g.Key, g.Sum(s => s.CallCount)))
                .ToList();

            var b = Aggregate(_baseline);
            var c = Aggregate(_comparison);
            bool changed = b.Count > 0 && c.Count > 0
                && !b.Select(s => s.ModelId).ToHashSet(StringComparer.Ordinal).SetEquals(c.Select(s => s.ModelId));

            var callsB = _baseline.SelectMany(r => r.Answers).SelectMany(CallTelemetryMeasures.CandidateCalls).ToList();
            var callsC = _comparison.SelectMany(r => r.Answers).SelectMany(CallTelemetryMeasures.CandidateCalls).ToList();
            int Mismatch(List<ModelCallTelemetry> calls) => calls.Count(x => !ChatConsistencyMeasures.TierMatches(x.ServiceTierRequested, x.ServedServiceTier));
            int Fallback(List<ModelCallTelemetry> calls) => calls.Count(x => !string.IsNullOrWhiteSpace(x.FallbackModelId));
            List<string> Speeds(List<ModelCallTelemetry> calls) => calls
                .Where(x => !string.IsNullOrWhiteSpace(x.ServedSpeed))
                .Select(x => x.ServedSpeed!.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            int mb = Mismatch(callsB), mc = Mismatch(callsC), fb = Fallback(callsB), fc = Fallback(callsC);
            return new ChatConsistencyServedModels
            {
                Baseline = b,
                Comparison = c,
                Changed = changed,
                BaselineCalls = callsB.Count,
                ComparisonCalls = callsC.Count,
                BaselineTierMismatchCalls = mb,
                ComparisonTierMismatchCalls = mc,
                BaselineFallbackCalls = fb,
                ComparisonFallbackCalls = fc,
                BaselineServedSpeeds = Speeds(callsB),
                ComparisonServedSpeeds = Speeds(callsC),
                ServedConfigurationDiffers = mb + mc + fb + fc > 0
            };
        }

        // --- Grader drift --------------------------------------------------------------------

        private List<ChatConsistencyGraderDrift> ComputeGraderDrift()
        {
            var quality = _work[ChatConsistencyEndpointIds.Quality];
            var snapshots = _commonGrader != null
                ? new HashSet<long> { _commonGrader.SnapshotId }
                : quality.Baseline.Concat(quality.Comparison).Select(r => r.AssessorModelSnapshotId).ToHashSet();

            var drift = new List<ChatConsistencyGraderDrift>();
            var groups = _evidence.AnchorCalibrations
                .Where(c => c.AssessorModelSnapshotId.HasValue && snapshots.Contains(c.AssessorModelSnapshotId.Value) && c.ErrorMessage == null)
                .GroupBy(c => (c.BenchmarkRunId, Snapshot: c.AssessorModelSnapshotId!.Value))
                .OrderBy(g => g.Key.BenchmarkRunId)
                .ThenBy(g => g.Key.Snapshot);

            foreach (var g in groups)
            {
                var rows = g.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id).ToList();
                if (rows.Select(c => ChatConsistencyMeasures.AsUtc(c.CreatedAtUtc).Date).Distinct().Count() < 2) continue;

                var earliest = ChatConsistencyMeasures.CalibrationVerdicts(rows[0]);
                var latest = ChatConsistencyMeasures.CalibrationVerdicts(rows[^1]);
                var shared = earliest.Keys.Intersect(latest.Keys).OrderBy(k => k).ToList();
                if (shared.Count == 0) continue;

                double d = shared.Average(k => latest[k].Quality) - shared.Average(k => earliest[k].Quality);
                drift.Add(new ChatConsistencyGraderDrift
                {
                    AnchorRunId = g.Key.BenchmarkRunId,
                    SnapshotId = g.Key.Snapshot,
                    Display = rows[0].AssessorModelSnapshot.Label() ?? ("snapshot " + Inv(g.Key.Snapshot)),
                    EarliestAtUtc = ChatConsistencyMeasures.AsUtc(rows[0].CreatedAtUtc),
                    LatestAtUtc = ChatConsistencyMeasures.AsUtc(rows[^1].CreatedAtUtc),
                    Drift = d,
                    ItemCount = shared.Count,
                    WithinMargin = Math.Abs(d) <= _protocol.GraderDriftMargin
                });
            }

            return drift;
        }

        // --- Controls --------------------------------------------------------------------------

        private ChatConsistencyControls ComputeControls()
        {
            var periods = new[]
            {
                new ChatConsistencyPeriod("baseline", _baseline),
                new ChatConsistencyPeriod("comparison", _comparison)
            };
            var matching = ChatConsistencyComparability.MatchControlRuns(
                periods, _evidence.ControlRuns, _request.AvailableOtherProviderModels, BenchmarkAssessmentPrompt.HarnessVersion);
            var controlsById = _evidence.ControlRuns.ToDictionary(r => r.Id);
            var effects = new List<ChatConsistencyControlEffect>();

            var bySubject = matching.Matches
                .GroupBy(m => m.ControlSubjectKey, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal);

            foreach (var subject in bySubject)
            {
                var controlB = subject.Where(m => m.Period == "baseline").Select(m => m.ControlRunId).Distinct().OrderBy(i => i)
                    .Select(i => controlsById[i]).Where(r => ControlPeriod(r) == "baseline").ToList();
                var controlC = subject.Where(m => m.Period == "comparison").Select(m => m.ControlRunId).Distinct().OrderBy(i => i)
                    .Select(i => controlsById[i]).Where(r => ControlPeriod(r) == "comparison").ToList();
                if (controlB.Count == 0 || controlC.Count == 0) continue;

                var latest = controlB.Concat(controlC).OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id).Last();
                var display = ChatConsistencyMeasures.SubjectOf(subject.Key, latest);

                foreach (var id in ChatConsistencyEndpointIds.All)
                {
                    var w = _work[id];
                    if (!w.Computed) continue;
                    var effect = ControlEffect(w, subject.Key, display, controlB, controlC);
                    if (effect != null) effects.Add(effect);
                }
            }

            return new ChatConsistencyControls
            {
                Matches = matching.Matches.Select(m => new ChatConsistencyControlMatchView
                {
                    Period = m.Period,
                    TargetRunId = m.TargetRunId,
                    ControlRunId = m.ControlRunId,
                    ControlSubjectKey = m.ControlSubjectKey,
                    PairedItemCount = m.PairedItemCount
                }).ToList(),
                Effects = effects,
                MissingControls = _battery
                    ? BatteryMissingControls(matching.MissingControls)
                    : matching.MissingControls.Select(n => new ChatConsistencyMissingControlView
                    {
                        Period = n.Period,
                        SuiteName = n.SuiteName,
                        Fingerprint = n.Fingerprint,
                        SuggestedText = n.SuggestedText,
                        TargetRunId = n.TargetRunId,
                        BuildReplaced = n.BuildReplaced
                    }).ToList(),
                ControlRunIds = _evidence.ControlRuns.Select(r => r.Id).OrderBy(i => i).ToList()
            };
        }

        /// <summary>
        /// The missing-control notes of a battery comparison, one per period and battery run: its suites joined,
        /// its latest member as the target, and a battery-level text. A battery run with any member whose build
        /// was replaced gets the replaced-build text.
        /// </summary>
        private List<ChatConsistencyMissingControlView> BatteryMissingControls(IReadOnlyList<MissingControlNote> notes)
        {
            var startOf = _baseline.Concat(_comparison).ToDictionary(r => r.Id, r => (r.StartedAtUtc, r.Id));
            var label = _evidence.ComparisonSet?.Label is { Length: > 0 } l ? l : "the battery";
            var views = new List<ChatConsistencyMissingControlView>();
            foreach (var group in notes
                .GroupBy(n => (n.Period, Unit: _evidence.UnitIdOf(n.TargetRunId)))
                .OrderBy(g => g.Key.Period == "baseline" ? 0 : 1)
                .ThenBy(g => _evidence.UnitStartedAtUtc.TryGetValue(g.Key.Unit, out var s) ? ChatConsistencyMeasures.AsUtc(s) : DateTime.MaxValue)
                .ThenBy(g => g.Key.Unit))
            {
                var members = group.OrderBy(n => startOf.TryGetValue(n.TargetRunId, out var s) ? s : (DateTime.MaxValue, n.TargetRunId)).ToList();
                var suites = members.Select(n => n.SuiteName).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                var replaced = members.Where(n => n.BuildReplaced).ToList();
                string harness = string.Join(", ", replaced.Select(n => n.HarnessVersion ?? "unrecorded").Distinct(StringComparer.Ordinal));
                string text = replaced.Count > 0
                    ? ChatConsistencyComparability.ReplacedBuildControlText(group.Key.Period, harness)
                    : "No control run for period " + group.Key.Period + ": make "
                      + ChatConsistencyComparability.ControlSubjectPhrase(members[0].Provider, _request.AvailableOtherProviderModels, "battery run")
                      + " on " + label + " under the same Overseer build as battery run #" + Inv(group.Key.Unit)
                      + (suites.Count > 0 ? " (suites " + string.Join(", ", suites) + ")" : string.Empty) + ".";

                views.Add(new ChatConsistencyMissingControlView
                {
                    Period = group.Key.Period,
                    SuiteName = string.Join(", ", suites),
                    Fingerprint = members[^1].Fingerprint,
                    SuggestedText = text,
                    TargetRunId = members[^1].TargetRunId,
                    BatteryRunId = group.Key.Unit,
                    BuildReplaced = replaced.Count > 0
                });
            }

            return views;
        }

        /// <summary>
        /// The period a control run's own start falls in, so that a control made under an unchanged build in
        /// the other period never enters the wrong arm: the window holding it, else the nearer window.
        /// </summary>
        private string ControlPeriod(BenchmarkRun run)
        {
            DateTime t = run.StartedAtUtc;
            if (t >= _request.BaselineStartUtc && t <= _request.BaselineEndUtc) return "baseline";
            if (t >= _request.ComparisonStartUtc && t <= _request.ComparisonEndUtc) return "comparison";

            static double Distance(DateTime at, DateTime start, DateTime end)
                => at < start ? (start - at).TotalSeconds : (at - end).TotalSeconds;

            return Distance(t, _request.BaselineStartUtc, _request.BaselineEndUtc) <= Distance(t, _request.ComparisonStartUtc, _request.ComparisonEndUtc)
                ? "baseline"
                : "comparison";
        }

        private ChatConsistencyControlEffect? ControlEffect(
            EndpointWork w, string controlKey, ChatConsistencySubject control, List<BenchmarkRun> controlB, List<BenchmarkRun> controlC)
        {
            Func<BenchmarkRun, bool> eligible = w.Protocol.Id switch
            {
                ChatConsistencyEndpointIds.Quality => r => _commonGrader == null || _commonVerdicts.ContainsKey(r.Id),
                ChatConsistencyEndpointIds.TimeToFirstAnswerText => r => r.MaxParallelQuestionsUsed <= 1 && (w.LegacyProxy || r.CallTelemetryVersion.HasValue),
                ChatConsistencyEndpointIds.StreamingRate => r => r.MaxParallelQuestionsUsed <= 1 && r.CallTelemetryVersion.HasValue,
                _ => _ => true
            };

            var cb = controlB.Where(eligible).ToList();
            var cc = controlC.Where(eligible).ToList();
            if (cb.Count == 0 || cc.Count == 0) return null;

            bool log = w.LogScale;
            var statistic = w.Statistic;
            bool resampleItems = !w.Protocol.RunClustersOnly;

            var tb = w.BaselineUnits.Select(u => UnitItems(u, w.Value, log, null)).ToList();
            var tc = w.ComparisonUnits.Select(u => UnitItems(u, w.Value, log, null)).ToList();
            var mb = cb.Select(r => RunItems(r, w.Value, log, null)).ToList();
            var mc = cc.Select(r => RunItems(r, w.Value, log, null)).ToList();

            var did = ChatConsistencyStatistics.ItemDifferenceInDifferences(
                new DifferenceInDifferencesArm(tb, tc), new DifferenceInDifferencesArm(mb, mc),
                statistic, resampleItems, _protocol.BootstrapReplicates, NextSeed());
            var own = ChatConsistencyResampling.PairedBootstrap(mb, mc, statistic, resampleItems, _protocol.BootstrapReplicates, NextSeed());
            if (did == null || own == null || did.Distribution.Replicates.Count == 0) return null;

            var didCi = did.Distribution.Interval95;
            bool includesZero = didCi.Lower <= 0.0 && didCi.Upper >= 0.0;
            int targetSign = Math.Sign(w.Estimate);
            bool separates = !includesZero && targetSign != 0 && Math.Sign(did.Distribution.Estimate) == targetSign;
            bool sameWay = targetSign != 0 && Math.Sign(own.Estimate) == targetSign;

            return new ChatConsistencyControlEffect
            {
                EndpointId = w.Protocol.Id,
                ControlSubjectKey = controlKey,
                ControlDisplay = control.DisplayName,
                ControlProvider = control.Provider,
                SameProvider = string.Equals(control.Provider, _evidence.Subject.Provider, StringComparison.OrdinalIgnoreCase),
                ControlBaselineRunIds = cb.Select(r => r.Id).ToList(),
                ControlComparisonRunIds = cc.Select(r => r.Id).ToList(),
                ItemCount = did.ItemCount,
                ControlChange = Fin(own.Estimate),
                ControlChangeCi95 = Interval(own.Interval95),
                DidEstimate = Fin(did.Distribution.Estimate),
                DidCi95 = Interval(didCi),
                DidPValue = Fin(ChatConsistencyResampling.BootstrapPValue(did.Distribution)),
                DidIncludesZero = includesZero,
                DidSeparatesTarget = separates,
                ControlMovedSameWay = sameWay
            };
        }

        // --- Secondary families --------------------------------------------------------------

        private List<ChatConsistencyFamilyResult> SecondaryFamilies(
            List<ChatConsistencyRateResult> reliability, ChatConsistencySecondaryResult? grossP2, IReadOnlyList<ChatConsistencyControlEffect> effects)
        {
            var families = new List<ChatConsistencyFamilyResult>();
            var quality = _work[ChatConsistencyEndpointIds.Quality];
            var work = _work[ChatConsistencyEndpointIds.Work];
            var p2 = _work[ChatConsistencyEndpointIds.TimeToFirstAnswerText];
            var p3 = _work[ChatConsistencyEndpointIds.StreamingRate];

            // Quality detail.
            var qualityDetail = new List<ChatConsistencySecondaryResult>();
            if (quality.Computed)
            {
                var b = quality.BaselineUnits;
                var c = quality.ComparisonUnits;
                qualityDetail.Add(Paired("accuracyLevel", "Accuracy level", "level", b, c, (r, a) => Detail(r, a)?.Accuracy, false, BootstrapStatistic.Mean));
                qualityDetail.Add(Paired("completenessLevel", "Completeness level", "level", b, c, (r, a) => Detail(r, a)?.Completeness, false, BootstrapStatistic.Mean));
                qualityDetail.Add(Paired("concisenessLevel", "Conciseness level", "level", b, c, (r, a) => Detail(r, a)?.Conciseness, false, BootstrapStatistic.Mean));
                qualityDetail.Add(Paired("readabilityLevel", "Readability level", "level", b, c, (r, a) => Detail(r, a)?.Readability, false, BootstrapStatistic.Mean));

                int kb = 0, nb = 0, kc = 0, nc = 0;
                foreach (var run in quality.Baseline) foreach (var a in run.Answers) if (Detail(run, a) is { } d) { nb++; if (d.Critical) kb++; }
                foreach (var run in quality.Comparison) foreach (var a in run.Answers) if (Detail(run, a) is { } d) { nc++; if (d.Critical) kc++; }
                var critical = RateOf("criticalErrors", "Critical-error rate", "answers", kb, nb, kc, nc);
                qualityDetail.Add(new ChatConsistencySecondaryResult
                {
                    Id = critical.Id,
                    Name = critical.Name,
                    Unit = "share of graded answers",
                    BaselineValue = critical.BaselineRate,
                    ComparisonValue = critical.ComparisonRate,
                    Estimate = critical.BaselineRate.HasValue && critical.ComparisonRate.HasValue ? critical.ComparisonRate - critical.BaselineRate : null,
                    PValue = critical.PValue,
                    Method = "Fisher's exact test, two-sided"
                });

                qualityDetail.Add(Flips(b, c));
            }

            families.Add(Family("qualityDetail", "Quality detail", qualityDetail,
                quality.Computed ? null : "Not computed: quality was not computable."));

            // Reliability, already adjusted within its family.
            families.Add(new ChatConsistencyFamilyResult
            {
                Id = "reliability",
                Name = "Reliability",
                Results = reliability.Select(r => new ChatConsistencySecondaryResult
                {
                    Id = r.Id,
                    Name = r.Name,
                    Unit = "share of " + r.Denominator,
                    BaselineValue = r.BaselineRate,
                    ComparisonValue = r.ComparisonRate,
                    Estimate = r.BaselineRate.HasValue && r.ComparisonRate.HasValue ? r.ComparisonRate - r.BaselineRate : null,
                    PValue = r.PValue,
                    AdjustedPValue = r.AdjustedPValue,
                    Rejected = r.AdjustedPValue.HasValue && r.AdjustedPValue.Value <= _protocol.SecondaryFalseDiscoveryRate,
                    Method = "Fisher's exact test, two-sided",
                    Note = r.EstablishedIncrease ? "Established increase." : null
                }).ToList()
            });

            // Tool use.
            var toolUse = new List<ChatConsistencySecondaryResult>();
            var wb = work.Computed ? work.BaselineUnits : _baselineUnits;
            var wc = work.Computed ? work.ComparisonUnits : _comparisonUnits;
            toolUse.Add(Paired("toolCallsPerAnswer", "Tool calls per answer", "calls", wb, wc,
                (_, a) => ChatConsistencyMeasures.IsDelivered(a) ? ChatConsistencyMeasures.ToolCalls(a) : null, false, BootstrapStatistic.Mean));
            toolUse.Add(Paired("modelCallsPerAnswer", "Model calls per answer", "calls", wb, wc,
                (_, a) => ChatConsistencyMeasures.IsDelivered(a)
                    ? a.ModelCallCount ?? (CallTelemetryMeasures.CandidateCalls(a).Count is int n && n > 0 ? n : (int?)null)
                    : null,
                false, BootstrapStatistic.Mean));
            toolUse.AddRange(ToolMix(MembersOf(wb), MembersOf(wc)));
            families.Add(Family("toolUse", "Tool use", toolUse, null));

            families.Add(Family("reasoningTokens", "Reasoning tokens", new List<ChatConsistencySecondaryResult>
            {
                Paired("reasoningTokens", "Reasoning tokens per item", "log ratio", wb, wc,
                    (_, a) => ChatConsistencyMeasures.IsDelivered(a) && a.ReasoningTokens is int t ? t : null, true, BootstrapStatistic.HodgesLehmann)
            }, null));

            families.Add(Family("answerLength", "Answer length", new List<ChatConsistencySecondaryResult>
            {
                Paired("answerLength", "Answer length in characters", "log ratio", wb, wc,
                    (_, a) => ChatConsistencyMeasures.IsDelivered(a) && !string.IsNullOrEmpty(a.AnswerText) ? a.AnswerText.Length : null, true, BootstrapStatistic.HodgesLehmann)
            }, null));

            // Net model time, and P2 with own waits left in.
            var netModel = new List<ChatConsistencySecondaryResult>();
            var nb2 = p2.Computed ? p2.BaselineUnits : _baselineUnits.Where(u => u.Members.All(r => r.MaxParallelQuestionsUsed <= 1)).ToList();
            var nc2 = p2.Computed ? p2.ComparisonUnits : _comparisonUnits.Where(u => u.Members.All(r => r.MaxParallelQuestionsUsed <= 1)).ToList();
            netModel.Add(Paired("netModelTime", "Net model time per item", "log ratio", nb2, nc2,
                (_, a) => ChatConsistencyMeasures.IsDelivered(a) && CallTelemetryMeasures.NetModelTimeMs(a) is long t ? t : null, true, BootstrapStatistic.HodgesLehmann));
            if (grossP2 != null) netModel.Add(grossP2);
            families.Add(Family("netModelTime", "Net model time", netModel, null));

            // Shift functions.
            var shift = new List<ChatConsistencySecondaryResult>();
            foreach (var w in new[] { p2, p3 })
            {
                if (w.ShiftFunction == null) continue;
                foreach (var d in w.ShiftFunction)
                {
                    shift.Add(new ChatConsistencySecondaryResult
                    {
                        Id = w.Protocol.Id + ".d" + ((int)Math.Round(d.Probability * 100)).ToString("00", CultureInfo.InvariantCulture),
                        Name = w.Protocol.Name + ", decile " + d.Probability.ToString("0.0", CultureInfo.InvariantCulture),
                        Unit = "log ratio",
                        BaselineValue = Fin(d.QuantileX),
                        ComparisonValue = Fin(d.QuantileY),
                        Estimate = Fin(d.Difference),
                        Ci95 = Interval((d.Lower, d.Upper)),
                        Method = "Harrell–Davis deciles of the item-centered log values; pointwise percentile bootstrap"
                    });
                }
            }

            families.Add(Family("shiftFunction", "Shift function", shift,
                "Pointwise 95 % intervals, not adjusted across deciles; descriptive."));

            // Time of day.
            var timeOfDay = new List<ChatConsistencySecondaryResult>();
            foreach (var w in new[] { p2, p3 })
            {
                if (!w.Computed || w.Strata.Count < 2) continue;
                foreach (int s in w.Strata)
                {
                    timeOfDay.Add(new ChatConsistencySecondaryResult
                    {
                        Id = w.Protocol.Id + ".stratum" + Inv(s),
                        Name = w.Protocol.Name + ", " + ChatConsistencyStatistics.StratumLabel(s),
                        Unit = "log ratio",
                        Estimate = Fin(w.StratumShifts[s]),
                        Method = "within-stratum Hodges–Lehmann shift",
                        Note = "baseline " + Plural(w.StratumRunCounts[s].Baseline, UnitNoun) + ", comparison "
                            + Plural(w.StratumRunCounts[s].Comparison, UnitNoun)
                    });
                }

                var inside = w.Strata.Where(s => _protocol.UsBusinessHourStrata.Contains(s)).ToList();
                var outside = w.Strata.Where(s => !_protocol.UsBusinessHourStrata.Contains(s)).ToList();
                if (inside.Count > 0 && outside.Count > 0)
                {
                    timeOfDay.Add(new ChatConsistencySecondaryResult
                    {
                        Id = w.Protocol.Id + ".usBusinessHoursContrast",
                        Name = w.Protocol.Name + ", US business hours minus other hours",
                        Unit = "log ratio",
                        BaselineValue = Fin(outside.Average(s => w.StratumShifts[s])),
                        ComparisonValue = Fin(inside.Average(s => w.StratumShifts[s])),
                        Estimate = Fin(inside.Average(s => w.StratumShifts[s]) - outside.Average(s => w.StratumShifts[s])),
                        Method = "difference of the mean stratum shifts; descriptive"
                    });
                }
            }

            families.Add(Family("timeOfDay", "Time-of-day contrast", timeOfDay,
                timeOfDay.Count == 0 ? "Not assessable: the periods share fewer than two time strata." : _protocol.UsBusinessHoursDefinition));

            // Difference in differences per control.
            families.Add(Family("differenceInDifferences", "Difference in differences per control", effects.Select(e => new ChatConsistencySecondaryResult
            {
                Id = e.EndpointId + "." + ShortKey(e.ControlSubjectKey),
                Name = _work[e.EndpointId].Protocol.Name + " against " + e.ControlDisplay,
                Unit = _work[e.EndpointId].Protocol.Unit,
                BaselineValue = e.ControlChange,
                Estimate = e.DidEstimate,
                Ci95 = e.DidCi95,
                PValue = e.DidPValue,
                ItemCount = e.ItemCount,
                Method = "item difference-in-differences, run-cluster bootstrap",
                Note = "baseline value is the control's own change"
            }).ToList(), effects.Count == 0 ? "No control subject has matched runs in both periods." : null));

            // Implied generation rate.
            families.Add(Family("generationRate", "Implied generation rate", GenerationRate(), "Theil–Sen slope of decode time on output tokens; descriptive."));

            return families;
        }

        private ChatConsistencyFamilyResult Family(string id, string name, List<ChatConsistencySecondaryResult> results, string? note)
        {
            var tested = results.Select((r, i) => (Result: r, Index: i)).Where(x => x.Result.PValue.HasValue).ToList();
            var bh = ChatConsistencyStatistics.BenjaminiHochberg(tested.Select(x => x.Result.PValue!.Value).ToList(), _protocol.SecondaryFalseDiscoveryRate);
            var adjusted = results.ToList();
            foreach (var fdr in bh)
            {
                var (result, index) = tested[fdr.Index];
                adjusted[index] = result with { AdjustedPValue = Fin(fdr.AdjustedPValue), Rejected = fdr.Rejected };
            }

            return new ChatConsistencyFamilyResult { Id = id, Name = name, Results = adjusted, Note = note };
        }

        private ChatConsistencySecondaryResult Paired(
            string id, string name, string unit, List<AnalysisUnit> b, List<AnalysisUnit> c,
            Func<BenchmarkRun, BenchmarkRunAnswer, double?> value, bool log, BootstrapStatistic statistic)
        {
            var mb = b.Select(u => UnitItems(u, value, log, null)).ToList();
            var mc = c.Select(u => UnitItems(u, value, log, null)).ToList();
            var differences = ChatConsistencyResampling.PairedDifferences(mb, mc);
            var distribution = differences == null
                ? null
                : ChatConsistencyResampling.PairedBootstrap(mb, mc, statistic, true, _protocol.BootstrapReplicates, NextSeed());
            if (differences == null || distribution == null)
            {
                return new ChatConsistencySecondaryResult { Id = id, Name = name, Unit = unit, Method = "not computable", Note = "No item has a value in both periods." };
            }

            var keys = differences.Value.Keys;
            double PeriodMean(List<IReadOnlyDictionary<string, double>> maps)
                => keys.Select(k => maps.Where(m => m.ContainsKey(k)).Average(m => m[k])).Average();
            double bm = PeriodMean(mb), cm = PeriodMean(mc);
            var wilcoxon = ChatConsistencyStatistics.WilcoxonSignedRank(differences.Value.Differences);

            return new ChatConsistencySecondaryResult
            {
                Id = id,
                Name = name,
                Unit = unit,
                BaselineValue = Fin(log ? Math.Exp(bm) : bm),
                ComparisonValue = Fin(log ? Math.Exp(cm) : cm),
                Estimate = Fin(distribution.Estimate),
                Ci95 = Interval(distribution.Interval95),
                PValue = Fin(wilcoxon.PValue ?? 1.0),
                Method = (statistic == BootstrapStatistic.Mean ? "mean" : "Hodges–Lehmann") + " of per-item differences"
                    + (log ? " of log values" : string.Empty) + "; Wilcoxon signed-rank",
                ItemCount = keys.Length
            };
        }

        /// <summary>Per-item pass flips across the periods against flips between answers of different baseline units.</summary>
        private ChatConsistencySecondaryResult Flips(List<AnalysisUnit> b, List<AnalysisUnit> c)
        {
            Dictionary<string, List<(long UnitId, bool Pass)>> Passes(List<AnalysisUnit> units)
            {
                var map = new Dictionary<string, List<(long, bool)>>(StringComparer.Ordinal);
                foreach (var unit in units)
                {
                    foreach (var run in unit.Members)
                    {
                        foreach (var a in run.Answers)
                        {
                            if (Quality(run, a) is not double q || Detail(run, a) is not { } d) continue;
                            string key = ChatConsistencyMeasures.ItemKey(a);
                            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<(long, bool)>();
                            list.Add((unit.Id, q >= _protocol.FlipPassThreshold && !d.Critical));
                        }
                    }
                }

                return map;
            }

            var pb = Passes(b);
            var pc = Passes(c);
            var across = new List<(bool, bool)>();
            var within = new List<(bool, bool)>();
            foreach (var key in pb.Keys.Where(pc.ContainsKey).OrderBy(k => k, StringComparer.Ordinal))
            {
                foreach (var x in pb[key]) foreach (var y in pc[key]) across.Add((x.Pass, y.Pass));
                var list = pb[key];
                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        if (list[i].UnitId != list[j].UnitId) within.Add((list[i].Pass, list[j].Pass));
                    }
                }
            }

            double? flip = ChatConsistencyStatistics.FlipRate(across);
            double? nullFlip = ChatConsistencyStatistics.NullFlipRate(within);
            if (!flip.HasValue || !nullFlip.HasValue)
            {
                return new ChatConsistencySecondaryResult
                {
                    Id = "flipRate",
                    Name = "Per-item flips against the null flip rate",
                    Unit = "share of answer pairs",
                    ComparisonValue = Fin(flip),
                    Method = "not computable",
                    Note = "The null flip rate needs two baseline " + UnitsNoun + " answering the same items."
                };
            }

            int fa = across.Count(p => p.Item1 != p.Item2);
            int fw = within.Count(p => p.Item1 != p.Item2);
            return new ChatConsistencySecondaryResult
            {
                Id = "flipRate",
                Name = "Per-item flips against the null flip rate",
                Unit = "share of answer pairs",
                BaselineValue = Fin(nullFlip),
                ComparisonValue = Fin(flip),
                Estimate = Fin(flip.Value - nullFlip.Value),
                PValue = ChatConsistencyStatistics.FisherExactTwoSided(fa, across.Count - fa, fw, within.Count - fw),
                Method = "pass at quality ≥ " + Inv(_protocol.FlipPassThreshold) + " without a critical error; Fisher's exact test of flips across periods against flips between baseline replicates",
                Note = "baseline value is the null flip rate inside the baseline"
            };
        }

        private List<ChatConsistencySecondaryResult> ToolMix(List<BenchmarkRun> b, List<BenchmarkRun> c)
        {
            List<string> Names(List<BenchmarkRun> runs) => runs
                .SelectMany(r => r.Answers.Where(ChatConsistencyMeasures.IsDelivered))
                .SelectMany(a => a.ToolCalls ?? new List<BenchmarkRunAnswerToolCall>())
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .Select(t => t.Name!.Trim())
                .ToList();

            var nb = Names(b);
            var nc = Names(c);
            if (nb.Count == 0 || nc.Count == 0) return new List<ChatConsistencySecondaryResult>();

            var top = nb.Concat(nc)
                .GroupBy(n => n, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Take(8)
                .Select(g => g.Key)
                .ToList();

            return top.Select(name =>
            {
                var rate = RateOf("toolMix." + name, "Share of tool calls to " + name, "tool calls",
                    nb.Count(n => n == name), nb.Count, nc.Count(n => n == name), nc.Count);
                return new ChatConsistencySecondaryResult
                {
                    Id = rate.Id,
                    Name = rate.Name,
                    Unit = "share of tool calls",
                    BaselineValue = rate.BaselineRate,
                    ComparisonValue = rate.ComparisonRate,
                    Estimate = rate.ComparisonRate - rate.BaselineRate,
                    PValue = rate.PValue,
                    Method = "Fisher's exact test, two-sided"
                };
            }).ToList();
        }

        private List<ChatConsistencySecondaryResult> GenerationRate()
        {
            const int MaxPoints = 1500;
            double? Slope(List<BenchmarkRun> runs)
            {
                var points = runs
                    .Where(r => r.CallTelemetryVersion.HasValue)
                    .SelectMany(r => r.Answers.Where(ChatConsistencyMeasures.IsDelivered))
                    .Select(CallTelemetryMeasures.FinalCandidateCall)
                    .Where(f => f != null && f.CompletedMs.HasValue && f.FirstEventMs.HasValue && f.OutputTokens is > 0)
                    .Select(f => (X: (double)f!.OutputTokens!.Value, Y: (double)(f.CompletedMs!.Value - f.FirstEventMs!.Value)))
                    .Take(MaxPoints)
                    .ToList();
                if (points.Count < 2) return null;
                return ChatConsistencyStatistics.TheilSenSlope(points.Select(p => p.X).ToList(), points.Select(p => p.Y).ToList());
            }

            double? sb = Slope(_baseline);
            double? sc = Slope(_comparison);
            double? rb = sb is > 0 ? 1000.0 / sb.Value : null;
            double? rc = sc is > 0 ? 1000.0 / sc.Value : null;
            return new List<ChatConsistencySecondaryResult>
            {
                new()
                {
                    Id = "impliedGenerationRate",
                    Name = "Implied generation rate (tokens per second)",
                    Unit = "log ratio",
                    BaselineValue = Fin(rb),
                    ComparisonValue = Fin(rc),
                    Estimate = rb.HasValue && rc.HasValue ? Fin(Math.Log(rc.Value / rb.Value)) : null,
                    Method = "1000 ÷ Theil–Sen slope of (completed − first event) milliseconds on output tokens, final candidate calls",
                    Note = rb.HasValue && rc.HasValue ? null : "Not computable: a period has fewer than two final calls with both marks."
                }
            };
        }

        // --- Attribution input ---------------------------------------------------------------

        private ChatConsistencyAttributionInput AttributionInput(
            ChatConsistencyServedModels served,
            ChatConsistencyScope scope,
            List<ChatConsistencyFamilyResult> families,
            List<ChatConsistencyRateResult> reliability,
            IReadOnlyList<ChatConsistencyOwnWaits> ownWaits,
            ChatConsistencySecondaryResult? grossP2,
            ChatConsistencyControls controls)
        {
            var p2 = _work[ChatConsistencyEndpointIds.TimeToFirstAnswerText];
            var p3 = _work[ChatConsistencyEndpointIds.StreamingRate];

            bool? SameSign(EndpointWork w)
            {
                if (!w.Computed) return null;
                var eligible = EligibleStrata(w);
                if (eligible.Count < 2) return null;
                return eligible.All(s => Math.Sign(w.StratumShifts[s]) == Math.Sign(w.Estimate) && w.StratumShifts[s] != 0.0);
            }

            bool Differ(EndpointWork w)
            {
                if (!w.Computed) return false;
                var eligible = EligibleStrata(w);
                if (eligible.Count < 2) return false;
                var shifts = eligible.Select(s => w.StratumShifts[s]).ToList();
                return shifts.Select(Math.Sign).Distinct().Count() > 1 || shifts.Max() - shifts.Min() > w.Protocol.Margin;
            }

            bool? everyDecile = null;
            if (p3.Computed && p3.ShiftFunction != null && p3.ShiftFunction.Count > 0)
            {
                everyDecile = p3.Estimate > 0
                    ? p3.ShiftFunction.All(d => d.Lower > 0.0)
                    : p3.Estimate < 0 && p3.ShiftFunction.All(d => d.Upper < 0.0);
            }

            var strata = p3.Computed ? p3.Strata : scope.StrataIndexes.ToList();
            bool us = strata.Any(s => _protocol.UsBusinessHourStrata.Contains(s));
            bool outside = strata.Any(s => !_protocol.UsBusinessHourStrata.Contains(s));

            bool Rejected(string family, string id) => families
                .Where(f => f.Id == family)
                .SelectMany(f => f.Results)
                .Any(r => (r.Id == id || id.Length == 0) && r.Rejected);

            bool ownWaitMoved = ownWaits[0].OwnWaitShare.HasValue && ownWaits[1].OwnWaitShare.HasValue
                && Math.Abs(ownWaits[1].OwnWaitShare!.Value - ownWaits[0].OwnWaitShare!.Value) >= _protocol.OwnWaitShareMaterialChange;

            bool retries = p2.Computed && p2.Verdict is ConsistencyVerdict.ChangedDegraded or ConsistencyVerdict.ChangedImproved
                && p2.RetriesPresent && Holds(p2, p2.RetryFreeEstimate) == false;

            DateTime spanStart = _baseline.Min(r => ChatConsistencyMeasures.AsUtc(r.StartedAtUtc));
            DateTime spanEnd = _comparison.Max(r => ChatConsistencyMeasures.AsUtc(r.StartedAtUtc));
            var confirmed = _evidence.Annotations
                .Where(a => a.Kind == ChatConsistencyAnnotationKind.ProviderConfirmedCause)
                .Select(ChatConsistencyMeasures.AnnotationView)
                .ToList();

            return new ChatConsistencyAttributionInput
            {
                Endpoints = ChatConsistencyEndpointIds.All.Select(id =>
                {
                    var w = _work[id];
                    return new ChatConsistencyAttributionEndpoint
                    {
                        Id = id,
                        Name = w.Protocol.Name,
                        Computed = w.Computed,
                        Verdict = w.Verdict,
                        VerdictLabel = VerdictLabel(w.Verdict, w.Protocol.WorkDirection),
                        Grade = w.Grade,
                        Estimate = w.Computed ? Fin(w.Estimate) : null
                    };
                }).ToList(),
                Events = _evidence.Events.Where(e => e.AtUtc >= spanStart && e.AtUtc <= spanEnd).ToList(),
                ControlEffects = controls.Effects,
                MissingControls = controls.MissingControls,
                ServedModels = served,
                SubjectProvider = _evidence.Subject.Provider,
                StreamingChangedAtEveryDecile = everyDecile,
                TimeToFirstAnswerTextStrataSameSign = SameSign(p2),
                StreamingRateStrataSameSign = SameSign(p3),
                TimeToFirstAnswerTextStrataDiffer = Differ(p2),
                StreamingRateStrataDiffer = Differ(p3),
                TimeToFirstAnswerTextHighRunVariance = p2.Computed && p2.RunStandardDeviation > p2.Protocol.Margin,
                StreamingRateHighRunVariance = p3.Computed && p3.RunStandardDeviation > p3.Protocol.Margin,
                RateLimitOrServerErrorIncrease = reliability.Any(r => (r.Id == "http429" || r.Id == "http5xx")
                    && r.Increased && r.AdjustedPValue.HasValue && r.AdjustedPValue.Value <= _protocol.SecondaryFalseDiscoveryRate),
                UsBusinessHoursCovered = us,
                OutsideBusinessHoursCovered = outside,
                GrossTimeToFirstAnswerTextVerdict = grossP2?.Verdict,
                OwnWaitShareMovedMaterially = ownWaitMoved,
                RetriesAccountForSpeedChange = retries,
                ReasoningTokensChanged = Rejected("reasoningTokens", "reasoningTokens"),
                ModelCallsChanged = Rejected("toolUse", "modelCallsPerAnswer"),
                ToolMixChanged = families.Where(f => f.Id == "toolUse").SelectMany(f => f.Results).Any(r => r.Id.StartsWith("toolMix.", StringComparison.Ordinal) && r.Rejected),
                ProviderConfirmedCauses = confirmed
            };
        }

        // --- Scope, data quality, limitations, next runs, headline --------------------------

        private ChatConsistencyScope Scope()
        {
            HashSet<int> StrataOf(List<BenchmarkRun> runs) => runs
                .SelectMany(r => r.Answers.Where(ChatConsistencyMeasures.IsDelivered))
                .Where(a => _evidence.AnswerTimings.ContainsKey(a.Id))
                .Select(a => _evidence.AnswerTimings[a.Id].Stratum)
                .ToHashSet();

            var sb = StrataOf(_baseline);
            var sc = StrataOf(_comparison);
            var common = sb.Intersect(sc).OrderBy(s => s).ToList();

            var timed = _baseline.Concat(_comparison)
                .SelectMany(r => r.Answers.Where(ChatConsistencyMeasures.IsDelivered))
                .Where(a => _evidence.AnswerTimings.ContainsKey(a.Id))
                .ToList();
            int outsideCommon = timed.Count(a => !common.Contains(_evidence.AnswerTimings[a.Id].Stratum));
            bool us = common.Any(s => _protocol.UsBusinessHourStrata.Contains(s));
            bool other = common.Any(s => !_protocol.UsBusinessHourStrata.Contains(s));

            return new ChatConsistencyScope
            {
                Text = ScopeText(common),
                StrataIndexes = common,
                StrataUsed = common.Select(ChatConsistencyStatistics.StratumLabel).ToList(),
                ExcludedShare = timed.Count == 0 ? 0.0 : outsideCommon / (double)timed.Count,
                OneTimeStratum = common.Count == 1,
                TimeOfDayAssessable = us && other,
                UsBusinessHoursCovered = us,
                OutsideBusinessHoursCovered = other
            };
        }

        /// <summary>"weekdays 04–12 UTC; weekends 16–20 UTC", from the stratum indexes; "no common time stratum" when empty.</summary>
        public static string ScopeText(IReadOnlyList<int> strata)
        {
            if (strata.Count == 0) return "no common time stratum";
            return StrataText(strata) + (strata.Count == 1 ? " (one time stratum)" : string.Empty);
        }

        /// <summary>"weekdays 04–12 UTC; weekends 16–20 UTC", from the stratum indexes; empty when there is none.</summary>
        public static string StrataText(IReadOnlyList<int> strata)
        {
            if (strata.Count == 0) return string.Empty;

            string Ranges(IEnumerable<int> blocks)
            {
                var sorted = blocks.OrderBy(b => b).ToList();
                var parts = new List<string>();
                int start = sorted[0], previous = sorted[0];
                foreach (int block in sorted.Skip(1).Append(int.MaxValue))
                {
                    if (block == previous + 1)
                    {
                        previous = block;
                        continue;
                    }

                    parts.Add((start * 4).ToString("00", CultureInfo.InvariantCulture) + "–" + (previous * 4 + 4).ToString("00", CultureInfo.InvariantCulture));
                    start = previous = block;
                }

                return string.Join(", ", parts);
            }

            var texts = new List<string>();
            var weekday = strata.Where(s => s < 6).ToList();
            var weekend = strata.Where(s => s >= 6).Select(s => s - 6).ToList();
            if (weekday.Count > 0) texts.Add("weekdays " + Ranges(weekday) + " UTC");
            if (weekend.Count > 0) texts.Add("weekends " + Ranges(weekend) + " UTC");
            return string.Join("; ", texts);
        }

        private void AddDataQuality()
        {
            var targets = _baseline.Concat(_comparison).ToList();
            // Counted by question: a revised question is one baseline-only and one comparison-only item.
            var pairing = ChatConsistencyComparability.PairItems(_baseline, _comparison);
            var questions = pairing.Paired.Select(p => p.Key.Question)
                .Concat(pairing.BaselineOnly.Select(k => k.Question))
                .Concat(pairing.ComparisonOnly.Select(k => k.Question))
                .ToHashSet(StringComparer.Ordinal);
            var pairedQuestions = pairing.Paired.Select(p => p.Key.Question).ToHashSet(StringComparer.Ordinal);
            var revised = pairing.RevisedQuestions.ToHashSet(StringComparer.Ordinal);
            int oneSided = pairing.BaselineOnly.Concat(pairing.ComparisonOnly)
                .Select(k => k.Question)
                .Where(q => !revised.Contains(q) && !pairedQuestions.Contains(q))
                .Distinct(StringComparer.Ordinal)
                .Count();
            int total = questions.Count;

            if (revised.Count > 0)
            {
                bool allLeftOut = revised.All(q => !pairedQuestions.Contains(q));
                _dataQuality.Add(Note("revisedQuestions", Inv(revised.Count) + " of " + Plural(total, "question")
                    + " (" + Percent(total == 0 ? null : revised.Count / (double)total) + ") " + Agree(revised.Count, "was", "were")
                    + " revised between the periods"
                    + (allLeftOut
                        ? " and " + Agree(revised.Count, "is", "are") + " left out of the paired comparison"
                        : "; their answers pair only within one revision")
                    + "; " + Inv(pairedQuestions.Count) + " " + Agree(pairedQuestions.Count, "is", "are") + " paired."));
            }

            if (oneSided > 0)
            {
                _dataQuality.Add(Note("unpairedItems", Inv(oneSided) + " of " + Plural(total, "question") + " "
                    + Agree(oneSided, "was", "were") + " answered in one period only and " + Agree(oneSided, "is", "are") + " not paired."));
            }

            if (pairing.NullRevisionItems.Count > 0)
            {
                _dataQuality.Add(Note("nullRevisions", Plural(pairing.NullRevisionItems.Count, "item") + " " + Agree(pairing.NullRevisionItems.Count, "has", "have") + " no recorded revision; they pair only with another unrecorded revision."));
            }

            int legacy = targets.Count(r => !r.CallTelemetryVersion.HasValue);
            if (legacy > 0)
            {
                _dataQuality.Add(Note("legacy", Inv(legacy) + " of " + Inv(targets.Count) + " target runs ("
                    + Percent(legacy / (double)targets.Count) + ") recorded no call telemetry."));
            }

            var targetAnswers = targets.SelectMany(r => r.Answers).ToList();
            int estimated = targetAnswers.Count(a => _evidence.AnswerTimings.TryGetValue(a.Id, out var t) && t.Estimated);
            if (estimated > 0)
            {
                _dataQuality.Add(Note("estimatedStarts", Plural(estimated, "answer start time") + " " + Agree(estimated, "was", "were") + " estimated from the run start and the answers' durations; they are used only for strata."));
            }

            int missing = targets.Where(r => r.CallTelemetryVersion.HasValue)
                .SelectMany(r => r.Answers.Where(ChatConsistencyMeasures.IsDelivered))
                .Count(a => CallTelemetryMeasures.TimeToFirstAnswerTextMs(a) == null);
            if (missing > 0)
            {
                _dataQuality.Add(Note("missingTelemetry", Plural(missing, "delivered answer") + " of telemetry runs " + Agree(missing, "has", "have") + " no time to first answer text."));
            }

            foreach (var exclusion in _assessment.SpeedExclusions.Where(x => x.Reason == SpeedExclusionReason.ParallelQuestions && targets.Any(r => r.Id == x.RunId)))
            {
                _dataQuality.Add(Note("parallelRun", "Run #" + Inv(exclusion.RunId) + " is left out of speed: " + exclusion.Detail));
            }

            foreach (var w in new[] { _work[ChatConsistencyEndpointIds.TimeToFirstAnswerText], _work[ChatConsistencyEndpointIds.StreamingRate] })
            {
                if (w.Computed && w.StratumExcludedShare is > 0)
                {
                    _dataQuality.Add(Note("strata", w.Protocol.Name + ": " + Percent(w.StratumExcludedShare) + " of observations fall in time strata only one period sampled and are excluded."));
                }
            }

            if (!_evidence.PriceCard.Available)
            {
                _dataQuality.Add(Note("priceCard", "No price card resolved, so cost was not computed."));
            }
            else
            {
                _dataQuality.Add(Note("priceCard", "Every compared run is costed at one price card: " + _evidence.PriceCard.Source
                    + " (input " + Money(_evidence.PriceCard.InputPerMillion) + ", output " + Money(_evidence.PriceCard.OutputPerMillion) + " per million tokens)."));
            }

            AddRunSelectionNote();
        }

        /// <summary>
        /// One <c>runSelection</c> note naming the usable runs of the subject in the periods that were not
        /// analyzed, each with its battery run when it has one, grouped by reason in <see cref="ChatConsistencyUnanalyzedReasons.All"/> order; at most
        /// <see cref="MaxRunSelectionNoteRuns"/> runs are named, the rest counted.
        /// </summary>
        private void AddRunSelectionNote()
        {
            var runs = _evidence.UnanalyzedRuns;
            if (runs.Count == 0) return;

            var groups = new List<string>();
            int listed = 0;
            foreach (string reason in ChatConsistencyUnanalyzedReasons.All)
            {
                var named = runs
                    .Where(r => r.Reason == reason)
                    .Take(MaxRunSelectionNoteRuns - listed)
                    .Select(r => "#" + Inv(r.RunId) + (r.BatteryRunId is long battery ? " of battery run #" + Inv(battery) : string.Empty) + " (" + r.Period + ")")
                    .ToList();
                if (named.Count == 0) continue;
                listed += named.Count;
                groups.Add(UnanalyzedReasonText(reason) + ": " + string.Join(", ", named));
            }

            int more = runs.Count - listed;
            _dataQuality.Add(Note("runSelection", UnanalyzedCountText(runs.Count) + " — " + string.Join("; ", groups)
                + (more > 0 ? "; and " + Inv(more) + " more" : string.Empty) + "."));
        }

        private ChatConsistencyRunSelectionView RunSelectionView()
        {
            var selection = _request.RunSelection;
            return new ChatConsistencyRunSelectionView
            {
                Recorded = selection != null,
                RangeLabel = selection?.RangeLabel,
                RangeFromUtc = ToUtc(selection?.RangeFromUtc),
                RangeToUtc = ToUtc(selection?.RangeToUtc),
                FirstRunId = selection?.FirstRunId,
                LastRunId = selection?.LastRunId,
                LeftOutRunIds = LeftOutIds(selection),
                FirstBatteryRunId = selection?.FirstBatteryRunId,
                LastBatteryRunId = selection?.LastBatteryRunId,
                LeftOutBatteryRunIds = LeftOutBatteryIds(selection),
                UnanalyzedRuns = _evidence.UnanalyzedRuns
            };
        }

        private List<string> Limitations()
        {
            var list = new List<string>
            {
                "Benchmark runs sample the chat only at the hours they ran; every verdict holds within the stated scope, not for hours no run covered.",
                "Attribution names the side a change fits, never a mechanism or an intent; only a provider-confirmed cause annotation is cited for a mechanism.",
                "Graders never see the candidate's tool results; unless a common grader covers every run, a quality change can reflect grading as well as answers.",
                "Control runs are not segmented for measurement changes; a difference-in-differences assumes each control was measured alike in both periods.",
                "Speed endpoints pair items by centering each answer on its item's mean; this removes item levels but not a change in which items each period sampled at which hour.",
                "Item-paired endpoints use the Wilcoxon signed-rank test on per-item differences of the period means; speed endpoints use a run-cluster bootstrap p-value.",
                "Cost is computed at one price card for every compared run, so a price change does not register as a cost change."
            };

            if (_battery)
            {
                list.Add("Each battery run is one unit: its member runs are merged, and the minimum sample, the bootstrap and leave-one-out "
                    + "count battery runs; a battery run with a member outside the measurement segment is left out whole.");
            }

            if (_work[ChatConsistencyEndpointIds.TimeToFirstAnswerText].LegacyProxy)
            {
                list.Add("P2 uses model time per item as a legacy proxy: it spans the provider's whole turn, not the wait for the first answer text.");
            }

            if (_evidence.UnanalyzedRuns.Count > 0)
            {
                list.Add("The operator chose the runs: " + UnanalyzedCountText(_evidence.UnanalyzedRuns.Count)
                    + " (see the run selection). The verdicts hold for the analyzed runs; leaving runs out after looking at the timeline can bias them.");
            }

            return list;
        }

        private List<ChatConsistencyNextRun> NextRuns(ChatConsistencyControls controls)
        {
            var list = new List<ChatConsistencyNextRun>();
            var subject = _evidence.Subject.DisplayName;
            var latestComparison = _comparison.OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id).Last();
            var latestBaseline = _baseline.OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id).Last();
            string comparisonTarget = RunTarget(latestComparison);

            // Widening the baseline helps only when an unused unit of the subject lies before its first one.
            DateTime firstBaseline = _baselineUnits.Count > 0
                ? _baselineUnits.Min(u => ChatConsistencyMeasures.AsUtc(u.StartedAtUtc))
                : _baseline.Min(r => ChatConsistencyMeasures.AsUtc(r.StartedAtUtc));
            bool earlierUnitExists = _evidence.UnanalyzedRuns.Any(u => u.Period == "baseline"
                && u.Reason is ChatConsistencyUnanalyzedReasons.OutsideDateRange or ChatConsistencyUnanalyzedReasons.BeforeFirstRun
                && ChatConsistencyMeasures.AsUtc(u.StartedAtUtc) < firstBaseline);
            string noEarlierUnit = "No other " + UnitNoun + " of " + subject + " exists for the baseline period. "
                + "A later analysis can take this comparison period as its baseline.";

            foreach (var id in ChatConsistencyEndpointIds.All)
            {
                var w = _work[id];
                if (!w.Computed && w.Protocol.Stratified && w.NotComputedKind == ChatConsistencyNotComputedKinds.NoCommonStratum)
                {
                    var stratum = MostPopulatedStratum(_baseline);
                    if (stratum.HasValue)
                    {
                        int runs = _protocol.MinimumSpeedRunsPerStratum;
                        list.Add(new ChatConsistencyNextRun
                        {
                            Kind = "stratum",
                            Period = "comparison",
                            EndpointId = id,
                            Reason = "The periods share no time-of-week stratum, so speed cannot be compared.",
                            Suggestion = Plural(runs, UnitNoun) + " of " + subject + " on " + comparisonTarget + " starting in "
                                + StrataText(new[] { stratum.Value }) + ", in the comparison period.",
                            RepeatRunId = latestComparison.Id
                        });
                    }

                    continue;
                }

                if (!w.Computed) continue;
                bool unresolved = w.Verdict is null or ConsistencyVerdict.Inconclusive || !w.MinimumSampleMet;
                if (!unresolved) continue;

                if (w.Protocol.Stratified)
                {
                    if (!w.MinimumSampleMet && w.StratumRunCounts.Count > 0)
                    {
                        var best = w.StratumRunCounts.OrderByDescending(p => Math.Min(p.Value.Baseline, p.Value.Comparison)).ThenBy(p => p.Key).First();
                        int more = Math.Max(0, _protocol.MinimumSpeedRunsPerStratum - best.Value.Comparison);
                        if (more > 0)
                        {
                            list.Add(new ChatConsistencyNextRun
                            {
                                Kind = "stratum",
                                Period = "comparison",
                                EndpointId = id,
                                Reason = w.Protocol.Name + " is below the minimum speed sample.",
                                Suggestion = Inv(more) + " more " + Agree(more, UnitNoun, UnitsNoun) + " of " + subject + " on " + comparisonTarget + " starting in "
                                    + StrataText(new[] { best.Key }) + " in the comparison period.",
                                RepeatRunId = latestComparison.Id
                            });
                        }

                        if (best.Value.Baseline < _protocol.MinimumSpeedRunsPerStratum)
                        {
                            list.Add(new ChatConsistencyNextRun
                            {
                                Kind = "stratum",
                                Period = "baseline",
                                EndpointId = id,
                                Reason = "The baseline has " + Plural(best.Value.Baseline, UnitNoun) + " in " + StrataText(new[] { best.Key }) + ".",
                                Suggestion = earlierUnitExists
                                    ? "Widen the baseline period to include more " + UnitsNoun + " of " + subject + " in " + StrataText(new[] { best.Key }) + "."
                                    : noEarlierUnit,
                                RepeatRunId = earlierUnitExists ? latestBaseline.Id : null
                            });
                        }
                    }
                }
                else
                {
                    var ub = w.BaselineUnits;
                    var uc = w.ComparisonUnits;
                    if (uc.Count < _protocol.MinimumRunsPerPeriod || Days(uc) < _protocol.MinimumDaysPerPeriod)
                    {
                        list.Add(new ChatConsistencyNextRun
                        {
                            Kind = "checkpoint",
                            Period = "comparison",
                            EndpointId = id,
                            Reason = "The comparison has " + Plural(uc.Count, UnitNoun) + " on " + Plural(Days(uc), "day") + ".",
                            Suggestion = "1 more " + UnitNoun + " of " + subject + " on " + comparisonTarget + " on another day in the comparison period.",
                            RepeatRunId = latestComparison.Id
                        });
                    }

                    if (ub.Count < _protocol.MinimumRunsPerPeriod || Days(ub) < _protocol.MinimumDaysPerPeriod)
                    {
                        list.Add(new ChatConsistencyNextRun
                        {
                            Kind = "checkpoint",
                            Period = "baseline",
                            EndpointId = id,
                            Reason = "The baseline has " + Plural(ub.Count, UnitNoun) + " on " + Plural(Days(ub), "day") + ".",
                            Suggestion = earlierUnitExists
                                ? "Widen the baseline period to include " + UnitsNoun + " of " + subject + " on another day."
                                : noEarlierUnit,
                            RepeatRunId = earlierUnitExists ? latestBaseline.Id : null
                        });
                    }

                    if (w.ItemCount < _protocol.MinimumPairedItems)
                    {
                        list.Add(new ChatConsistencyNextRun
                        {
                            Kind = "checkpoint",
                            Period = "comparison",
                            EndpointId = id,
                            Reason = "Only " + Plural(w.ItemCount, "item") + " " + Agree(w.ItemCount, "is", "are") + " paired.",
                            Suggestion = "A run of " + subject + " on a suite sharing at least " + Inv(_protocol.MinimumPairedItems) + " items with the baseline.",
                            RepeatRunId = latestBaseline.Id
                        });
                    }
                }

                if (w.Verdict == ConsistencyVerdict.Inconclusive && w.Mde is { CapNote: false } mde && mde.StandardDeviation > 0.0)
                {
                    double z = ChatConsistencyStatistics.NormalQuantile(1.0 - _protocol.Alpha / 2.0) + ChatConsistencyStatistics.NormalQuantile(_protocol.Power);
                    int needed = Math.Max(2, (int)Math.Ceiling(2.0 * Math.Pow(z * mde.StandardDeviation / w.Protocol.Margin, 2.0)));
                    int more = needed - w.ComparisonUnits.Count;
                    if (more > 0)
                    {
                        list.Add(new ChatConsistencyNextRun
                        {
                            Kind = "checkpoint",
                            Period = "comparison",
                            EndpointId = id,
                            Reason = w.Protocol.Name + " is inconclusive; the minimum detectable effect is " + Number(mde.Effect) + " against a margin of " + w.Protocol.MarginText + ".",
                            Suggestion = "About " + Inv(needed) + " " + UnitsNoun + " per period would bring the minimum detectable effect to the margin: "
                                + Inv(more) + " more " + Agree(more, UnitNoun, UnitsNoun) + " of " + subject + " on " + comparisonTarget + " on separate days in the comparison period.",
                            RepeatRunId = latestComparison.Id
                        });
                    }
                }
            }

            bool changedWithEvent = _evidence.Events.Count > 0
                && ChatConsistencyEndpointIds.All.Select(id => _work[id]).Any(w => w.Computed && w.Verdict is ConsistencyVerdict.ChangedDegraded or ConsistencyVerdict.ChangedImproved);
            if (changedWithEvent || controls.Effects.Count == 0)
            {
                foreach (var note in controls.MissingControls)
                {
                    list.Add(new ChatConsistencyNextRun
                    {
                        Kind = "control",
                        Period = note.Period,
                        Reason = note.BuildReplaced
                            ? "The " + note.Period + " period's Overseer build has been replaced."
                            : "No control run of another provider under the same Overseer build in the " + note.Period + " period.",
                        Suggestion = note.SuggestedText,
                        RepeatRunId = note.TargetRunId
                    });
                }
            }

            var quality = _work[ChatConsistencyEndpointIds.Quality];
            if (quality.Checks.Any(c => c.Name == "Grader stability" && c.Status == ChatConsistencyCheckStatus.Failed))
            {
                list.Add(new ChatConsistencyNextRun
                {
                    Kind = "regrade",
                    Period = "both",
                    EndpointId = ChatConsistencyEndpointIds.Quality,
                    Reason = "Quality rests on native grades with no measured grader stability.",
                    Suggestion = "Re-grade every compared run with one assessor (a common grader), or mark a run as the consistency anchor and re-grade it on different dates."
                });
            }

            return list
                .GroupBy(n => (n.Kind, n.Period, n.Suggestion))
                .Select(g => g.First())
                .ToList();
        }

        /// <summary>The stratum most of <paramref name="runs"/>' delivered, timed answers started in; the lower index on a tie; null without any.</summary>
        private int? MostPopulatedStratum(IEnumerable<BenchmarkRun> runs)
        {
            var counts = runs
                .SelectMany(r => r.Answers.Where(ChatConsistencyMeasures.IsDelivered))
                .Where(a => _evidence.AnswerTimings.ContainsKey(a.Id))
                .GroupBy(a => _evidence.AnswerTimings[a.Id].Stratum)
                .Select(g => (Stratum: g.Key, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Stratum)
                .ToList();
            return counts.Count == 0 ? null : counts[0].Stratum;
        }

        private static string Headline(ChatConsistencySubject subject, List<ChatConsistencyEndpointResult> endpoints, ChatConsistencyScope scope, List<string> increases)
        {
            ChatConsistencyEndpointResult E(string id) => endpoints.First(e => e.Id == id);
            string V(ChatConsistencyEndpointResult e) => e.Computed ? e.VerdictLabel + " (" + GradeText(e.Grade) + ")" : "not computable";

            var p2 = E(ChatConsistencyEndpointIds.TimeToFirstAnswerText);
            var p3 = E(ChatConsistencyEndpointIds.StreamingRate);
            string speed = p2.Computed ? V(p2) : V(p3);
            if (p2.Computed && p3.Computed && p3.VerdictLabel != p2.VerdictLabel) speed += ", streaming rate " + V(p3);
            if (p2.LegacyProxy) speed += " [legacy proxy]";

            string headline = "Overseer chat with " + subject.DisplayName + ": quality " + V(E(ChatConsistencyEndpointIds.Quality))
                + "; speed " + speed
                + "; work " + V(E(ChatConsistencyEndpointIds.Work))
                + "; cost " + V(E(ChatConsistencyEndpointIds.Cost))
                + (scope.StrataIndexes.Count == 0 ? "; the periods share no common time stratum" : " within " + scope.Text);
            if (increases.Count > 0) headline += "; reliability: " + string.Join(", ", increases) + " (established)";
            return headline;
        }

        private static string GradeText(ChatConsistencyEvidenceGrade grade) => grade switch
        {
            ChatConsistencyEvidenceGrade.Established => "established",
            ChatConsistencyEvidenceGrade.Indicated => "indicated",
            _ => "not established"
        };

        private ChatConsistencyPeriodSummary PeriodSummary(string name, DateTime start, DateTime end, List<BenchmarkRun> runs) => new()
        {
            Name = name,
            StartUtc = ChatConsistencyMeasures.AsUtc(start),
            EndUtc = ChatConsistencyMeasures.AsUtc(end),
            RunIds = runs.Select(r => r.Id).ToList(),
            RunCount = runs.Count,
            Days = runs.Select(r => ChatConsistencyMeasures.AsUtc(r.StartedAtUtc).Date).Distinct().OrderBy(d => d)
                .Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList(),
            AnswerCount = runs.Sum(r => r.Answers.Count),
            ItemCount = runs.SelectMany(r => r.Answers).Select(ChatConsistencyMeasures.ItemKey).Distinct(StringComparer.Ordinal).Count(),
            SuiteNames = runs.Select(r => r.SuiteName ?? string.Empty).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList(),
            LegacyRunCount = runs.Count(r => !r.CallTelemetryVersion.HasValue)
        };

        /// <summary>The time-of-week strata the delivered, timed answers of <paramref name="runs"/> started in.</summary>
        private List<int> StrataOf(IEnumerable<BenchmarkRun> runs) => runs
            .SelectMany(r => r.Answers.Where(ChatConsistencyMeasures.IsDelivered))
            .Where(a => _evidence.AnswerTimings.ContainsKey(a.Id))
            .Select(a => _evidence.AnswerTimings[a.Id].Stratum)
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        private ChatConsistencyPeriodHours PeriodHoursOf(string period, List<BenchmarkRun> runs)
        {
            var strata = StrataOf(runs);
            return new ChatConsistencyPeriodHours
            {
                Period = period,
                Strata = strata.Select(ChatConsistencyStatistics.StratumLabel).ToList(),
                Text = strata.Count == 0 ? "no timed answers" : StrataText(strata)
            };
        }

        /// <summary>
        /// The period's levels over its analyzed answers, each with its endpoint's value function: delivered
        /// answers for time, rate, output tokens and cost (at the analysis's price card). The Overall Index
        /// only when every unit is a battery run with one.
        /// </summary>
        private ChatConsistencyPeriodLevels PeriodLevelsOf(string period, List<BenchmarkRun> runs, List<AnalysisUnit> units)
        {
            var pairs = runs.SelectMany(r => r.Answers.Select(a => (Run: r, Answer: a))).ToList();
            var delivered = pairs.Where(p => ChatConsistencyMeasures.IsDelivered(p.Answer)).ToList();
            var native = pairs.Select(p => ChatConsistencyMeasures.NativeQuality(p.Answer)).Where(q => q.HasValue).Select(q => q!.Value).ToList();
            var times = delivered
                .Where(p => p.Run.CallTelemetryVersion.HasValue)
                .Select(p => TelemetryTimeToFirstAnswerText(p.Run, p.Answer))
                .Where(v => v.HasValue).Select(v => v!.Value).ToList();
            var rates = delivered
                .Select(p => CallTelemetryMeasures.AnswerStreamingRate(p.Answer))
                .Where(v => v.HasValue).Select(v => v!.Value.TokensPerSecond).ToList();
            var outputs = delivered.Where(p => p.Answer.OutputTokens.HasValue).Select(p => (double)p.Answer.OutputTokens!.Value).ToList();
            var card = _evidence.Pricing;
            var costs = card == null
                ? new List<double>()
                : delivered.Select(p => ChatConsistencyMeasures.AnswerCost(p.Answer, p.Run, card)).Where(c => c.HasValue).Select(c => (double)c!.Value).ToList();

            double? index = null, half = null;
            string? intervalNote = null;
            if (_battery && units.Count > 0)
            {
                var indexes = units
                    .Select(u => _evidence.UnitOverallIndexes.TryGetValue(u.Id, out var x) ? x : null)
                    .ToList();
                if (indexes.All(x => x?.Index is double))
                {
                    index = Fin(indexes.Average(x => x!.Index!.Value));
                    if (indexes.Count == 1)
                    {
                        half = Fin(indexes[0]!.HalfWidth);
                        intervalNote = half.HasValue ? indexes[0]!.IntervalNote : null;
                    }
                }
            }

            return new ChatConsistencyPeriodLevels
            {
                Period = period,
                AnswerCount = pairs.Count,
                NativeMeanQuality = native.Count > 0 ? Fin(native.Average()) : null,
                OverallIndex = index,
                OverallIndexHalfWidth = half,
                OverallIndexIntervalNote = intervalNote,
                MedianTimeToFirstAnswerTextMs = Fin(BenchmarkGroupStatistics.Median(times)),
                MedianStreamingRate = Fin(BenchmarkGroupStatistics.Median(rates)),
                MeanOutputTokensPerAnswer = outputs.Count > 0 ? Fin(outputs.Average()) : null,
                MeanCostPerQuestionUsd = costs.Count > 0 ? Fin(costs.Average()) : null,
                FailedAnswerCount = pairs.Count(p => ChatConsistencyMeasures.IsTerminalFailure(p.Answer))
            };
        }

        private List<ChatConsistencySegmentView> SegmentViews()
        {
            var baselineIds = _baseline.Select(r => r.Id).ToHashSet();
            var comparisonIds = _comparison.Select(r => r.Id).ToHashSet();
            return _assessment.Runs.Select(s => new ChatConsistencySegmentView
            {
                RunId = s.RunId,
                StartedAtUtc = ChatConsistencyMeasures.AsUtc(s.StartedAtUtc),
                Role = baselineIds.Contains(s.RunId) ? "baseline" : comparisonIds.Contains(s.RunId) ? "comparison" : "control",
                Quality = s.Quality,
                SpeedTelemetry = s.SpeedTelemetry,
                SpeedLegacy = s.SpeedLegacy,
                Work = s.Work,
                Cost = s.Cost
            }).ToList();
        }

        // --- Input fingerprint -----------------------------------------------------------------

        /// <summary>
        /// Lower-case hex SHA-256 over a canonical serialization of every input: the request with its run
        /// selection and comparison set, the protocol, each run with the fields and per-answer values the
        /// analysis reads, the calibrations, the annotations and the price card, all in id order, the runs not
        /// analyzed and why, by start, and the compared set with the unit kind and each unit's member runs.
        /// </summary>
        public string InputSha256()
        {
            var roles = new Dictionary<long, string>();
            foreach (var r in _baseline) roles[r.Id] = "baseline";
            foreach (var r in _comparison) roles[r.Id] = "comparison";
            foreach (var r in _evidence.ControlRuns) roles.TryAdd(r.Id, "control");

            var runs = _baseline.Concat(_comparison).Concat(_evidence.ControlRuns)
                .DistinctBy(r => r.Id)
                .OrderBy(r => r.Id)
                .Select(r => new
                {
                    r.Id,
                    Role = roles[r.Id],
                    StartedAtUtc = ChatConsistencyMeasures.AsUtc(r.StartedAtUtc),
                    r.Status,
                    r.ScoringMethodVersion,
                    r.HarnessVersion,
                    r.RerunHarnessVersion,
                    r.CallTelemetryVersion,
                    ModelAxisKey = ChatConsistencyComparability.ModelAxisKey(r),
                    Fingerprint = ChatConsistencyComparability.OverseerInstrumentFingerprint(r),
                    r.AssessorModelSnapshotId,
                    r.CoAssessorModelSnapshotId,
                    r.SecondOpinionAssessorModelSnapshotId,
                    r.ClaimVerifierModelSnapshotId,
                    r.ServedModelIdsJson,
                    r.MaxParallelQuestionsUsed,
                    Suite = ChatConsistencyMeasures.SuiteIdentity(r),
                    r.IsConsistencyAnchor,
                    Answers = r.Answers.OrderBy(a => a.Id).Select(a => new
                    {
                        a.Id,
                        a.OrderIndex,
                        Item = ChatConsistencyMeasures.ItemKey(a),
                        a.Status,
                        a.QualityScore,
                        a.PanelQualityScore,
                        a.AccuracyLevel,
                        a.CompletenessLevel,
                        a.ConcisenessLevel,
                        a.ReadabilityLevel,
                        a.CriticalError,
                        a.InputTokens,
                        a.OutputTokens,
                        a.ReasoningTokens,
                        a.CacheReadInputTokens,
                        a.CacheCreationInputTokens,
                        a.LongContextInputTokens,
                        a.LongContextOutputTokens,
                        a.LongContextCacheReadTokens,
                        a.LongContextCacheCreationTokens,
                        a.DurationMs,
                        a.ToolTimeMs,
                        StartedAtUtc = a.StartedAtUtc.HasValue ? ChatConsistencyMeasures.AsUtc(a.StartedAtUtc.Value) : (DateTime?)null,
                        a.PermitWaitMs,
                        a.BackoffWaitMs,
                        a.RetryAttemptCount,
                        a.ToolCallCount,
                        a.ModelCallCount,
                        a.ToolBudgetExhausted,
                        AnswerLength = a.AnswerText?.Length ?? 0,
                        a.TerminationReason,
                        Timeout = ChatConsistencyMeasures.IsTimeout(a),
                        a.ServedModelId,
                        a.ActualServiceTierUsed,
                        Tools = (a.ToolCalls ?? new List<BenchmarkRunAnswerToolCall>()).Select(t => t.Name).ToList(),
                        Calls = CallTelemetryMeasures.CandidateCalls(a).Select(c => new
                        {
                            c.CallIndex,
                            StartedAtUtc = ChatConsistencyMeasures.AsUtc(c.StartedAtUtc),
                            c.Provider,
                            c.ServiceTierRequested,
                            c.ServedServiceTier,
                            c.ServedSpeed,
                            c.ServedModelId,
                            c.FallbackModelId,
                            c.IsRefusal,
                            c.ErrorKind,
                            c.AttemptCount,
                            c.Http429Count,
                            c.Http5xxCount,
                            c.PermitWaitMs,
                            c.BackoffWaitMs,
                            c.FailedAttemptMs,
                            c.FirstEventMs,
                            c.FirstOutputMs,
                            c.CompletedMs,
                            c.Last80DecodeSpanMs,
                            c.Last80VisibleChars,
                            c.VisibleOutputChars,
                            c.OutputTokens,
                            c.ReasoningTokens
                        }).ToList()
                    }).ToList()
                })
                .ToList();

            object Calibration(BenchmarkAssessorCalibration c) => new
            {
                c.Id,
                c.BenchmarkRunId,
                c.AssessorModelSnapshotId,
                CreatedAtUtc = ChatConsistencyMeasures.AsUtc(c.CreatedAtUtc),
                Failed = c.ErrorMessage != null,
                Verdicts = Sha(c.VerdictsJson ?? string.Empty)
            };

            var manifest = new
            {
                CodeVersion = CurrentAnalysisCodeVersion,
                Request = new
                {
                    _request.SubjectModelKey,
                    BaselineStartUtc = ChatConsistencyMeasures.AsUtc(_request.BaselineStartUtc),
                    BaselineEndUtc = ChatConsistencyMeasures.AsUtc(_request.BaselineEndUtc),
                    ComparisonStartUtc = ChatConsistencyMeasures.AsUtc(_request.ComparisonStartUtc),
                    ComparisonEndUtc = ChatConsistencyMeasures.AsUtc(_request.ComparisonEndUtc),
                    BaselineRunIds = _request.BaselineRunIds?.Distinct().OrderBy(i => i).ToList(),
                    ComparisonRunIds = _request.ComparisonRunIds?.Distinct().OrderBy(i => i).ToList(),
                    ControlRunIds = _request.ControlRunIds?.Distinct().OrderBy(i => i).ToList(),
                    _request.RelaxedPooling,
                    _request.CommonGraderSnapshotId,
                    _request.AvailableOtherProviderModels,
                    RunSelection = _request.RunSelection is { } selection
                        ? new
                        {
                            selection.RangeLabel,
                            RangeFromUtc = ToUtc(selection.RangeFromUtc),
                            RangeToUtc = ToUtc(selection.RangeToUtc),
                            selection.FirstRunId,
                            selection.LastRunId,
                            LeftOutRunIds = LeftOutIds(selection),
                            selection.FirstBatteryRunId,
                            selection.LastBatteryRunId,
                            LeftOutBatteryRunIds = LeftOutBatteryIds(selection)
                        }
                        : null,
                    ComparisonSet = _request.ComparisonSet is { } set ? new { set.Kind, set.Key } : null,
                    BaselineBatteryRunIds = _request.BaselineBatteryRunIds?.Distinct().OrderBy(i => i).ToList(),
                    ComparisonBatteryRunIds = _request.ComparisonBatteryRunIds?.Distinct().OrderBy(i => i).ToList()
                },
                Protocol = _protocol.ToJson(),
                Runs = runs,
                UnanalyzedRuns = _evidence.UnanalyzedRuns.Select(u => new { u.RunId, u.Period, u.Reason, u.BatteryRunId }).ToList(),
                ComparisonSet = _evidence.ComparisonSet,
                UnitKind,
                Units = UnitViews().Select(u => new { u.Period, u.UnitId, u.StartedAtUtc, u.MemberRunIds }).ToList(),
                Calibrations = _evidence.Calibrations.OrderBy(c => c.Id).Select(Calibration).ToList(),
                AnchorCalibrations = _evidence.AnchorCalibrations.OrderBy(c => c.Id).Select(Calibration).ToList(),
                Annotations = _evidence.Annotations.OrderBy(a => a.Id).Select(a => new
                {
                    a.Id,
                    AtUtc = ChatConsistencyMeasures.AsUtc(a.AtUtc),
                    a.Provider,
                    a.ModelId,
                    a.Kind,
                    a.Text,
                    a.SourceUrl
                }).ToList(),
                PriceCard = _evidence.PriceCard
            };

            return Sha(JsonSerializer.Serialize(manifest, ChatConsistencyJson.Options));
        }
    }

    // --- Formatting helpers ----------------------------------------------------------------------

    private static string VerdictLabel(ConsistencyVerdict? verdict, bool work) => verdict switch
    {
        ConsistencyVerdict.ChangedDegraded => work ? "more work" : "degraded",
        ConsistencyVerdict.ChangedImproved => work ? "less work" : "improved",
        ConsistencyVerdict.ChangedNegligible => "changed, negligible",
        ConsistencyVerdict.Equivalent => "equivalent",
        ConsistencyVerdict.Inconclusive => "inconclusive",
        _ => "not computable"
    };

    /// <summary>A <see cref="ChatConsistencyUnanalyzedReasons"/> value as the notes write it.</summary>
    private static string UnanalyzedReasonText(string reason) => reason switch
    {
        ChatConsistencyUnanalyzedReasons.LeftOut => "left out in step 1",
        ChatConsistencyUnanalyzedReasons.OutsideDateRange => "outside the step-1 dates",
        ChatConsistencyUnanalyzedReasons.BeforeFirstRun => "before the first run",
        ChatConsistencyUnanalyzedReasons.AfterLastRun => "after the last run",
        ChatConsistencyUnanalyzedReasons.NotSelected => "not assigned to a period",
        _ => reason
    };

    /// <summary>"1 usable run of the model inside the periods was not analyzed", or the plural.</summary>
    private static string UnanalyzedCountText(int count)
        => count == 1
            ? "1 usable run of the model inside the periods was not analyzed"
            : Inv(count) + " usable runs of the model inside the periods were not analyzed";

    private static double ToPercent(double logRatio) => 100.0 * (Math.Exp(logRatio) - 1.0);

    private static double? Fin(double? value) => value.HasValue && double.IsFinite(value.Value) ? value : null;

    private static ChatConsistencyInterval? Interval((double Lower, double Upper) ci)
        => double.IsFinite(ci.Lower) && double.IsFinite(ci.Upper) ? new ChatConsistencyInterval(ci.Lower, ci.Upper) : null;

    private static double? PooledStandardDeviation(List<double> a, List<double> b)
    {
        double weighted = 0.0, df = 0.0;
        foreach (var values in new[] { a, b })
        {
            if (values.Count < 2) continue;
            weighted += (values.Count - 1) * BenchmarkGroupStatistics.SampleVariance(values)!.Value;
            df += values.Count - 1;
        }

        return df > 0 ? Math.Sqrt(weighted / df) : null;
    }

    private static string Number(double? value)
        => value.HasValue && double.IsFinite(value.Value) ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "not computable";

    private static string Percent(double? share)
        => share.HasValue && double.IsFinite(share.Value) ? (100.0 * share.Value).ToString("0.#", CultureInfo.InvariantCulture) + " %" : "not recorded";

    private static string Money(decimal? value)
        => value.HasValue ? "$" + value.Value.ToString("0.00##", CultureInfo.InvariantCulture) : "not priced";

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

    private static string ShortKey(string key) => Sha(key).Substring(0, 12);

    private static string Sha(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static ChatConsistencyNote Note(string kind, string text) => new() { Kind = kind, Text = text };

    private static string Inv(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>"1 day", "2 days": the count and the noun, plural unless the count is one.</summary>
    private static string Plural(long count, string noun) => Inv(count) + " " + noun + (count == 1 ? string.Empty : "s");

    /// <summary>" (a. b)": the texts joined by spaces in parentheses, without the last one's terminal period.</summary>
    private static string Parenthesized(IEnumerable<string> texts) => " (" + string.Join(" ", texts).TrimEnd().TrimEnd('.') + ")";

    /// <summary><paramref name="singular"/> for a count of one, else <paramref name="plural"/>.</summary>
    private static string Agree(long count, string singular, string plural) => count == 1 ? singular : plural;
}

/// <summary>
/// The resampling the analysis adds on top of <see cref="ChatConsistencyStatistics"/>: the paired item
/// bootstrap of two periods of runs and the run-cluster bootstrap of a stratified shift. Seeded; pure.
/// </summary>
internal static class ChatConsistencyResampling
{
    /// <summary>The statistic over <paramref name="values"/>; NaN when empty.</summary>
    public static double Statistic(BootstrapStatistic statistic, IReadOnlyList<double> values)
    {
        if (values.Count == 0) return double.NaN;
        return statistic switch
        {
            BootstrapStatistic.Mean => values.Average(),
            BootstrapStatistic.Median => BenchmarkGroupStatistics.Median(values)!.Value,
            _ => ChatConsistencyStatistics.HodgesLehmannShift(values)!.Value
        };
    }

    /// <summary>Items with a value in at least one run of each period, ordinal order.</summary>
    public static string[] PairedKeys(
        IReadOnlyList<IReadOnlyDictionary<string, double>> baseline, IReadOnlyList<IReadOnlyDictionary<string, double>> comparison)
        => baseline.SelectMany(m => m.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(k => comparison.Any(m => m.ContainsKey(k)))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Per paired item, the comparison's cross-run mean minus the baseline's; null when no item pairs.</summary>
    public static (string[] Keys, double[] Differences)? PairedDifferences(
        IReadOnlyList<IReadOnlyDictionary<string, double>> baseline, IReadOnlyList<IReadOnlyDictionary<string, double>> comparison)
    {
        var keys = PairedKeys(baseline, comparison);
        if (keys.Length == 0) return null;

        var differences = keys
            .Select(k => comparison.Where(m => m.ContainsKey(k)).Average(m => m[k]) - baseline.Where(m => m.ContainsKey(k)).Average(m => m[k]))
            .ToArray();
        return (keys, differences);
    }

    /// <summary>The statistic over the per-item differences; null when no item pairs.</summary>
    public static double? PairedPoint(
        IReadOnlyList<IReadOnlyDictionary<string, double>> baseline, IReadOnlyList<IReadOnlyDictionary<string, double>> comparison,
        BootstrapStatistic statistic)
    {
        if (baseline.Count == 0 || comparison.Count == 0) return null;
        var d = PairedDifferences(baseline, comparison);
        return d == null ? null : Statistic(statistic, d.Value.Differences);
    }

    /// <summary>
    /// The paired item bootstrap of two periods: each replicate resamples each period's runs with
    /// replacement, recomputes the item means and the per-item differences, and with
    /// <paramref name="resampleItems"/> then resamples the items (the two-stage bootstrap, Davison &amp;
    /// Hinkley 1997, § 3.8). A replicate in which no item survives in both periods is dropped. Null
    /// when a period has no run or no item pairs.
    /// </summary>
    public static BootstrapDistribution? PairedBootstrap(
        IReadOnlyList<IReadOnlyDictionary<string, double>> baseline,
        IReadOnlyList<IReadOnlyDictionary<string, double>> comparison,
        BootstrapStatistic statistic,
        bool resampleItems,
        int replicates,
        int seed)
    {
        if (baseline.Count == 0 || comparison.Count == 0) return null;
        var keys = PairedKeys(baseline, comparison);
        if (keys.Length == 0) return null;

        double[][] Matrix(IReadOnlyList<IReadOnlyDictionary<string, double>> runs)
            => runs.Select(m => keys.Select(k => m.TryGetValue(k, out double v) ? v : double.NaN).ToArray()).ToArray();

        var mb = Matrix(baseline);
        var mc = Matrix(comparison);
        int n = keys.Length;
        var meanB = new double[n];
        var meanC = new double[n];
        ItemMeans(mb, Enumerable.Range(0, mb.Length).ToArray(), meanB);
        ItemMeans(mc, Enumerable.Range(0, mc.Length).ToArray(), meanC);
        var point = new List<double>(n);
        for (int i = 0; i < n; i++)
        {
            double v = meanC[i] - meanB[i];
            if (!double.IsNaN(v)) point.Add(v);
        }

        double estimate = Statistic(statistic, point);
        var rng = new Random(seed);
        var drawnB = new int[mb.Length];
        var drawnC = new int[mc.Length];
        var buffer = new double[n];
        var resampled = new double[n];
        var values = new double[replicates];
        for (int r = 0; r < replicates; r++)
        {
            for (int j = 0; j < drawnB.Length; j++) drawnB[j] = rng.Next(mb.Length);
            for (int j = 0; j < drawnC.Length; j++) drawnC[j] = rng.Next(mc.Length);
            ItemMeans(mb, drawnB, meanB);
            ItemMeans(mc, drawnC, meanC);

            int count = 0;
            for (int i = 0; i < n; i++)
            {
                double v = meanC[i] - meanB[i];
                if (!double.IsNaN(v)) buffer[count++] = v;
            }

            if (count == 0)
            {
                values[r] = double.NaN;
                continue;
            }

            if (resampleItems)
            {
                for (int i = 0; i < count; i++) resampled[i] = buffer[rng.Next(count)];
                values[r] = Statistic(statistic, new ArraySegment<double>(resampled, 0, count));
            }
            else
            {
                values[r] = Statistic(statistic, new ArraySegment<double>(buffer, 0, count));
            }
        }

        return new BootstrapDistribution(estimate, values, replicates, new[] { mb.Length, mc.Length });
    }

    /// <summary>
    /// The run-cluster bootstrap of the stratified shift: each replicate draws each period's runs with
    /// replacement, keeps each drawn run whole, and recomputes <see cref="ChatConsistencyStatistics.StratifiedShift"/>.
    /// The distribution is null when the point estimate is undefined.
    /// </summary>
    public static (StratifiedShiftResult Point, BootstrapDistribution? Distribution) StratifiedRunBootstrap(
        IReadOnlyList<IReadOnlyList<StratifiedObservation>> baseline,
        IReadOnlyList<IReadOnlyList<StratifiedObservation>> comparison,
        int replicates,
        int seed)
    {
        var point = ChatConsistencyStatistics.StratifiedShift(baseline.SelectMany(r => r).ToList(), comparison.SelectMany(r => r).ToList());
        if (!point.Shift.HasValue || baseline.Count == 0 || comparison.Count == 0) return (point, null);

        var rng = new Random(seed);
        var values = new double[replicates];
        var sb = new List<StratifiedObservation>();
        var sc = new List<StratifiedObservation>();
        for (int r = 0; r < replicates; r++)
        {
            sb.Clear();
            sc.Clear();
            for (int j = 0; j < baseline.Count; j++) sb.AddRange(baseline[rng.Next(baseline.Count)]);
            for (int j = 0; j < comparison.Count; j++) sc.AddRange(comparison[rng.Next(comparison.Count)]);
            values[r] = ChatConsistencyStatistics.StratifiedShift(sb, sc).Shift ?? double.NaN;
        }

        return (point, new BootstrapDistribution(point.Shift.Value, values, replicates, new[] { baseline.Count, comparison.Count }));
    }

    /// <summary>
    /// The two-sided bootstrap p-value of "no shift": twice the smaller of (replicates ≤ 0, plus one) and
    /// (replicates ≥ 0, plus one) over (replicates, plus one), capped at 1.
    /// </summary>
    public static double BootstrapPValue(BootstrapDistribution distribution)
    {
        var replicates = distribution.Replicates;
        if (replicates.Count == 0) return 1.0;
        int below = replicates.Count(v => v <= 0.0);
        int above = replicates.Count(v => v >= 0.0);
        return Math.Min(1.0, 2.0 * (Math.Min(below, above) + 1) / (replicates.Count + 1));
    }

    private static void ItemMeans(double[][] matrix, int[] runs, double[] means)
    {
        for (int i = 0; i < means.Length; i++)
        {
            double sum = 0.0;
            int count = 0;
            foreach (int r in runs)
            {
                double v = matrix[r][i];
                if (double.IsNaN(v)) continue;
                sum += v;
                count++;
            }

            means[i] = count > 0 ? sum / count : double.NaN;
        }
    }
}
