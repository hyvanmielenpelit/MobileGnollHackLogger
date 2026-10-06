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
/// Fills <see cref="BenchmarkReportDocument.ComparisonKey"/> on rows stored without one, and a Report
/// Pack row's comparison and covered entries. Idempotent.
/// </summary>
public static class BenchmarkReportDocumentBackfill
{
    public const int BatchSize = 200;

    /// <summary>
    /// Derives each missing key from the row's <c>ComparisonRequestJson</c>, saving every
    /// <see cref="BatchSize"/> rows. A row whose request cannot be read is logged and left null, and is
    /// not retried within the same call. Returns how many rows received a key.
    /// </summary>
    public static async Task<int> BackfillComparisonKeysAsync(ApplicationDbContext db, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        int filled = 0;
        long afterId = 0;
        while (true)
        {
            var batch = await db.BenchmarkReportDocuments
                .IgnoreAutoIncludes()
                .Where(d => d.ComparisonKey == null && d.Id > afterId)
                .OrderBy(d => d.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) break;

            foreach (var document in batch)
            {
                if (TryKeyOf(document.ComparisonRequestJson, out var key))
                {
                    document.ComparisonKey = key;
                    filled++;
                }
                else
                {
                    logger.LogWarning("Report document {DocumentId} has an unreadable comparison request; its comparison key stays empty.", document.Id);
                }
            }

            afterId = batch[^1].Id;
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        if (filled > 0)
        {
            logger.LogInformation("Backfilled the comparison key of {Count} report document(s).", filled);
        }
        return filled;
    }

    /// <summary>
    /// Links each Report Pack document that has a comparison key but no comparison to the
    /// <see cref="BenchmarkComparison"/> ensured from its <c>ComparisonRequestJson</c>, and gives each
    /// model-scope Report Pack document its subject alone as its covered entries and covered-set key
    /// where they are missing, saving every <see cref="BatchSize"/> rows. A row whose request cannot be
    /// read, or whose request names another entry set than its key, keeps a null comparison and is
    /// logged; it is not retried within the same call. Run- and battery-completion documents are left
    /// alone. Returns how many rows changed.
    /// </summary>
    public static async Task<int> BackfillComparisonsAsync(ApplicationDbContext db, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        var identity = new BenchmarkComparisonIdentityService(db, new BenchmarkModelComparisonService(db));

        // By the document's comparison key; null where no comparison could be ensured.
        var comparisonIds = new Dictionary<string, int?>(StringComparer.Ordinal);

        int changed = 0;
        long afterId = 0;
        while (true)
        {
            var batch = await db.BenchmarkReportDocuments
                .IgnoreAutoIncludes()
                .Where(d => d.Origin == BenchmarkReportDocumentOrigin.ReportPack
                            && d.Id > afterId
                            && ((d.ComparisonKey != null && d.ComparisonId == null)
                                || (d.Scope == BenchmarkReportScope.Model
                                    && (d.CoveredEntryKeysJson == null || d.CoveredSetKey == null))))
                .OrderBy(d => d.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) break;

            foreach (var document in batch)
            {
                bool rowChanged = false;

                if (document.ComparisonKey != null && document.ComparisonId == null)
                {
                    if (!comparisonIds.TryGetValue(document.ComparisonKey, out int? comparisonId))
                    {
                        comparisonId = await EnsureComparisonAsync(identity, document, logger, ct);
                        comparisonIds[document.ComparisonKey] = comparisonId;
                    }

                    if (comparisonId.HasValue)
                    {
                        document.ComparisonId = comparisonId;
                        rowChanged = true;
                    }
                }

                if (document.Scope == BenchmarkReportScope.Model
                    && (document.CoveredEntryKeysJson == null || document.CoveredSetKey == null))
                {
                    string subjectKey = document.SubjectKey.Trim();
                    try
                    {
                        string coveredSetKey = BenchmarkReportComparisonKey.ForCoveredSet(new[] { subjectKey });
                        document.CoveredEntryKeysJson ??= BenchmarkReportJson.Serialize(new[] { subjectKey });
                        document.CoveredSetKey ??= coveredSetKey;
                        rowChanged = true;
                    }
                    catch (ArgumentException)
                    {
                        logger.LogWarning("Report document {DocumentId} has an unreadable subject key; its covered entries stay empty.", document.Id);
                    }
                }

                if (rowChanged) changed++;
            }

            afterId = batch[^1].Id;
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        if (changed > 0)
        {
            logger.LogInformation("Backfilled the comparison or covered entries of {Count} report document(s).", changed);
        }
        return changed;
    }

    /// <summary>The id of the document's comparison, ensured from its stored request; null, logged, when there is none.</summary>
    private static async Task<int?> EnsureComparisonAsync(
        BenchmarkComparisonIdentityService identity, BenchmarkReportDocument document, ILogger logger, CancellationToken ct)
    {
        var (comparison, error) = await identity.EnsureFromRequestJsonAsync(
            document.ComparisonRequestJson, document.CreatedByUserId, ct);

        if (comparison == null)
        {
            logger.LogWarning("Report document {DocumentId} has no usable comparison request ({Error}); it stays without a comparison.", document.Id, error);
            return null;
        }

        if (!string.Equals(comparison.ComparisonKey, document.ComparisonKey, StringComparison.Ordinal))
        {
            logger.LogWarning("Report document {DocumentId} names another entry set in its comparison request than its comparison key; it stays without a comparison.", document.Id);
            return null;
        }

        return comparison.Id;
    }

    private static bool TryKeyOf(string? comparisonRequestJson, out string key)
    {
        key = string.Empty;
        if (string.IsNullOrWhiteSpace(comparisonRequestJson)) return false;
        try
        {
            var request = BenchmarkReportJson.Deserialize<BenchmarkModelComparisonRequest>(comparisonRequestJson);
            var runIds = request.RunIds ?? new();
            var groupIds = request.GroupIds ?? new();
            if (runIds.Count == 0 && groupIds.Count == 0) return false;
            key = BenchmarkReportComparisonKey.From(runIds, groupIds);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
