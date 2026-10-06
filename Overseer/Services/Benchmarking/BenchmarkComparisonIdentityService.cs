namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>One comparison as the comparison list shows it.</summary>
public sealed record BenchmarkComparisonListItem(
    int Id,
    string DisplayName,
    string? Name,
    string DefaultName,
    int EntryCount,
    BenchmarkComparisonSubjectKind SubjectKind,
    int DocumentCount,
    DateTime? LastDocumentAtUtc,
    DateTime CreatedAtUtc);

/// <summary>
/// Numbers model comparisons: one <see cref="BenchmarkComparison"/> per entry set, found or created by
/// <see cref="BenchmarkReportComparisonKey.From"/>, named once from its entries' labels and renamable
/// by an administrator. Makes no AI calls.
/// </summary>
public class BenchmarkComparisonIdentityService
{
    public const int MaxNameLength = 160;

    /// <summary>Entries named by the default name; a larger comparison is named by its count.</summary>
    public const int MaxNamedEntries = 3;

    public const string EmptySelectionError = "A comparison needs at least one run, group or battery result.";

    public const string NotFoundError = "The comparison was not found.";

    public static readonly string NameTooLongError = $"A comparison name is at most {MaxNameLength} characters.";

    private readonly ApplicationDbContext _db;
    private readonly BenchmarkModelComparisonService _comparisons;
    private readonly ILogger<BenchmarkComparisonIdentityService>? _logger;

    public BenchmarkComparisonIdentityService(
        ApplicationDbContext db,
        BenchmarkModelComparisonService? comparisons = null,
        ILogger<BenchmarkComparisonIdentityService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
        _comparisons = comparisons ?? new BenchmarkModelComparisonService(db);
        _logger = logger;
    }

    /// <summary>
    /// The comparison of the entry set, created when it is new. A new comparison is named from the
    /// comparison computed over its entries, so a selection that cannot be compared (an empty or mixed
    /// selection, or a run, group or battery run that does not exist) is refused with that error and
    /// nothing is stored. An existing comparison is returned as it is.
    /// </summary>
    public Task<(BenchmarkComparison? Comparison, string? Error)> EnsureAsync(
        IEnumerable<long>? runIds,
        IEnumerable<long>? groupIds,
        IEnumerable<long>? batteryRunIds,
        string? userId,
        CancellationToken ct = default)
        => EnsureCoreAsync(runIds, groupIds, batteryRunIds, userId, requireComparable: true, ct);

    /// <summary>
    /// The comparison of a stored document's <c>ComparisonRequestJson</c>. Unlike
    /// <see cref="EnsureAsync"/>, an entry set that can no longer be compared, because a run was deleted
    /// since, is still numbered, named by its entry keys. An empty, unreadable, empty-selection or
    /// mixed request is refused.
    /// </summary>
    public async Task<(BenchmarkComparison? Comparison, string? Error)> EnsureFromRequestJsonAsync(
        string? comparisonRequestJson, string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(comparisonRequestJson))
        {
            return (null, "No comparison request is stored.");
        }

        BenchmarkModelComparisonRequest request;
        try
        {
            request = BenchmarkReportJson.Deserialize<BenchmarkModelComparisonRequest>(comparisonRequestJson);
        }
        catch (JsonException)
        {
            return (null, "The stored comparison request is unreadable.");
        }

        return await EnsureCoreAsync(
            request.RunIds, request.GroupIds, request.BatteryRunIds, userId, requireComparable: false, ct);
    }

    private async Task<(BenchmarkComparison? Comparison, string? Error)> EnsureCoreAsync(
        IEnumerable<long>? runIds,
        IEnumerable<long>? groupIds,
        IEnumerable<long>? batteryRunIds,
        string? userId,
        bool requireComparable,
        CancellationToken ct)
    {
        var runs = Normalize(runIds);
        var groups = Normalize(groupIds);
        var batteries = Normalize(batteryRunIds);

        if (runs.Count == 0 && groups.Count == 0 && batteries.Count == 0)
        {
            return (null, EmptySelectionError);
        }

        if (batteries.Count > 0 && (runs.Count > 0 || groups.Count > 0))
        {
            return (null, BenchmarkBatteryModelComparison.MixedSourcesError);
        }

        string key = BenchmarkReportComparisonKey.From(runs, groups, batteries);
        var existing = await FindByKeyAsync(key, ct);
        if (existing != null) return (existing, null);

        var entryKeys = BenchmarkReportComparisonKey.CanonicalEntryKeys(runs, groups, batteries);

        var (computed, error) = await _comparisons.CompareAsync(new BenchmarkModelComparisonRequest
        {
            RunIds = runs,
            GroupIds = groups,
            BatteryRunIds = batteries,
            PricingBasis = BenchmarkModelComparisonPricingBasis.AsRun
        }, ct);

        string defaultName;
        if (computed != null)
        {
            defaultName = DefaultName(computed);
        }
        else if (requireComparable)
        {
            return (null, error ?? "The comparison could not be computed.");
        }
        else
        {
            _logger?.LogInformation(
                "Comparison {ComparisonKey} could not be computed ({Error}); it is named by its entry keys.", key, error);
            defaultName = FallbackName(entryKeys);
        }

        var comparison = new BenchmarkComparison
        {
            ComparisonKey = key,
            EntryKeysJson = BenchmarkReportJson.Serialize(entryKeys),
            SubjectKind = batteries.Count > 0 ? BenchmarkComparisonSubjectKind.Batteries : BenchmarkComparisonSubjectKind.Runs,
            EntryCount = entryKeys.Count,
            DefaultName = defaultName,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = userId
        };

        _db.BenchmarkComparisons.Add(comparison);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The unique index on the key decides a concurrent insert of the same entry set.
            _db.Entry(comparison).State = EntityState.Detached;
            var winner = await FindByKeyAsync(key, ct);
            if (winner == null) throw;
            return (winner, null);
        }

        return (comparison, null);
    }

    /// <summary>
    /// The default name of a computed comparison, from its entries' labels in canonical order (runs,
    /// then groups, then battery results, each by id): up to <see cref="MaxNamedEntries"/> entries
    /// <i>"A vs B vs C"</i>, more <i>"10 models · &lt;battery or suite name&gt;"</i>, the name left out
    /// when the entries share none. At most <see cref="MaxNameLength"/> characters.
    /// </summary>
    public static string DefaultName(BenchmarkModelComparisonDto comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        var entries = comparison.Entries
            .OrderBy(e => KindRank(e.SourceKind))
            .ThenBy(e => e.SourceId)
            .ToList();

        if (entries.Count == 0) return "Empty comparison";

        string name = entries.Count <= MaxNamedEntries
            ? string.Join(" vs ", entries.Select(e => string.IsNullOrWhiteSpace(e.Label) ? e.Key : e.Label))
            : CountName(entries.Count, ContextName(comparison, entries));

        return Truncate(name);
    }

    /// <summary>
    /// Sets the administrator's name, trimmed; an empty or whitespace name resets the comparison to its
    /// default name. <see cref="NotFoundError"/> for an unknown id, <see cref="NameTooLongError"/> past
    /// <see cref="MaxNameLength"/> characters.
    /// </summary>
    public async Task<(BenchmarkComparison? Comparison, string? Error)> RenameAsync(
        int id, string? name, CancellationToken ct = default)
    {
        string? trimmed = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (trimmed != null && trimmed.Length > MaxNameLength)
        {
            return (null, NameTooLongError);
        }

        var comparison = await _db.BenchmarkComparisons.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (comparison == null) return (null, NotFoundError);

        comparison.Name = trimmed;
        comparison.RenamedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return (comparison, null);
    }

    /// <summary>Every comparison, newest first, with how many report documents it has and when the last was written.</summary>
    public async Task<IReadOnlyList<BenchmarkComparisonListItem>> ListAsync(CancellationToken ct = default)
    {
        var comparisons = await _db.BenchmarkComparisons
            .AsNoTracking()
            .OrderByDescending(c => c.Id)
            .ToListAsync(ct);

        var documents = await _db.BenchmarkReportDocuments
            .IgnoreAutoIncludes()
            .Where(d => d.ComparisonId != null)
            .GroupBy(d => d.ComparisonId!.Value)
            .Select(g => new { ComparisonId = g.Key, Count = g.Count(), Last = g.Max(d => d.CreatedAtUtc) })
            .ToListAsync(ct);
        var byComparison = documents.ToDictionary(d => d.ComparisonId);

        return comparisons
            .Select(c =>
            {
                var docs = byComparison.GetValueOrDefault(c.Id);
                return new BenchmarkComparisonListItem(
                    c.Id,
                    c.DisplayName,
                    c.Name,
                    c.DefaultName,
                    c.EntryCount,
                    c.SubjectKind,
                    docs?.Count ?? 0,
                    docs?.Last,
                    c.CreatedAtUtc);
            })
            .ToList();
    }

    private Task<BenchmarkComparison?> FindByKeyAsync(string key, CancellationToken ct)
        => _db.BenchmarkComparisons.FirstOrDefaultAsync(c => c.ComparisonKey == key, ct);

    private static List<long> Normalize(IEnumerable<long>? ids)
        => (ids ?? Enumerable.Empty<long>()).Distinct().OrderBy(id => id).ToList();

    private static int KindRank(string sourceKind) => sourceKind switch
    {
        "Run" => 0,
        "Group" => 1,
        _ => 2
    };

    /// <summary>The battery name of a battery comparison, the suite name of any other; null when the entries share none.</summary>
    private static string? ContextName(BenchmarkModelComparisonDto comparison, IReadOnlyList<BenchmarkModelComparisonEntryDto> entries)
    {
        bool batteries = comparison.SubjectKind == BenchmarkModelComparisonSubjectKinds.Batteries;

        string? baseline = batteries ? comparison.BaselineBatteryName : comparison.BaselineSuiteName;
        if (!string.IsNullOrWhiteSpace(baseline)) return baseline;

        var names = entries
            .Select(e => batteries ? e.BatteryName : e.SuiteName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return names.Count == 1 ? names[0] : null;
    }

    private static string CountName(int count, string? context)
        => context == null ? $"{count} models" : $"{count} models · {context}";

    /// <summary>The name of an entry set that could not be computed: its entry keys, or its count.</summary>
    private static string FallbackName(IReadOnlyList<string> entryKeys)
        => Truncate(entryKeys.Count <= MaxNamedEntries
            ? string.Join(" vs ", entryKeys)
            : CountName(entryKeys.Count, null));

    private static string Truncate(string name)
        => name.Length <= MaxNameLength ? name : name[..(MaxNameLength - 1)].TrimEnd() + "…";
}
