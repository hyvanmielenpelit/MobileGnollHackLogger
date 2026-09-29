namespace Overseer.Services.Benchmarking;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>Fills <see cref="BenchmarkReportDocument.ComparisonKey"/> on rows stored without one. Idempotent.</summary>
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
