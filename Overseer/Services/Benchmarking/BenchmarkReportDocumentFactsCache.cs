namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MobileGnollHackLogger.Data;

/// <summary>
/// What the document lists read from a stored document's fact sheet
/// (<see cref="BenchmarkReportRenderService.StoredFacts"/>: the peers and their letters, the covered
/// models' labels, covers-all-entries), kept per document id so a list parses each sheet once. A stored
/// document's <c>FactsJson</c> is written once, with the row, and never updated in place, so an entry
/// stays right for as long as the document exists. A singleton over the global
/// <see cref="IMemoryCache"/>, one size unit per entry, with a 30-minute sliding expiry.
/// </summary>
public sealed class BenchmarkReportDocumentFactsCache
{
    public static readonly TimeSpan SlidingExpiration = TimeSpan.FromMinutes(30);

    /// <summary>A cache that holds nothing: every read goes to the database.</summary>
    public static BenchmarkReportDocumentFactsCache None { get; } = new(null);

    private const string KeyPrefix = "benchmark-report-document-facts:";

    private readonly IMemoryCache? _cache;

    public BenchmarkReportDocumentFactsCache(IMemoryCache? cache)
    {
        _cache = cache;
    }

    private static string KeyOf(long documentId) => KeyPrefix + documentId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public bool TryGet(long documentId, out BenchmarkReportRenderService.StoredFacts facts)
    {
        if (_cache != null && _cache.TryGetValue(KeyOf(documentId), out BenchmarkReportRenderService.StoredFacts? found) && found != null)
        {
            facts = found;
            return true;
        }
        facts = BenchmarkReportRenderService.StoredFacts.Empty;
        return false;
    }

    public void Set(long documentId, BenchmarkReportRenderService.StoredFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        _cache?.Set(KeyOf(documentId), facts, new MemoryCacheEntryOptions
        {
            SlidingExpiration = SlidingExpiration,
            Size = 1
        });
    }

    public void Remove(long documentId) => _cache?.Remove(KeyOf(documentId));

    /// <summary>
    /// The stored facts of each existing document of <paramref name="documentIds"/>, from the cache where
    /// it holds them, the others read in one query and parsed once; and how many came from the cache.
    /// </summary>
    public async Task<(Dictionary<long, BenchmarkReportRenderService.StoredFacts> Facts, int Hits)> GetAsync(
        ApplicationDbContext db, IReadOnlyCollection<long> documentIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(documentIds);

        var facts = new Dictionary<long, BenchmarkReportRenderService.StoredFacts>();
        var misses = new List<long>();
        foreach (long id in documentIds.Distinct())
        {
            if (TryGet(id, out var found)) facts[id] = found;
            else misses.Add(id);
        }
        int hits = facts.Count;
        if (misses.Count == 0) return (facts, hits);

        var rows = await db.BenchmarkReportDocuments
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(d => misses.Contains(d.Id))
            .Select(d => new { d.Id, d.FactsJson })
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            var read = BenchmarkReportRenderService.ReadFacts(row.FactsJson);
            facts[row.Id] = read;
            Set(row.Id, read);
        }
        return (facts, hits);
    }
}
