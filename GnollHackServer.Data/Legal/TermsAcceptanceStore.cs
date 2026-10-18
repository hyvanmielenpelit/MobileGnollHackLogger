using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;

namespace GnollHackServer.Data.Legal;

/// <summary>Whether a user has accepted the version of a document that is required now.</summary>
/// <param name="RequiredVersion">Null when the gate is off.</param>
public sealed record TermsAcceptanceStatus(
    string? RequiredVersion,
    string? AcceptedVersion,
    DateTime? AcceptedUtc,
    string? AcceptedSource,
    bool IsAccepted);

public enum TermsRecordResult
{
    Recorded,
    AlreadyAccepted,

    /// <summary>The version is not the one required now; nothing was written.</summary>
    StaleVersion
}

/// <summary>The required version of each Terms document, each user's status, and the acceptance record.</summary>
public sealed class TermsAcceptanceStore
{
    private static readonly TimeSpan StatusLifetime = TimeSpan.FromMinutes(10);

    private readonly ApplicationDbContext _db;
    private readonly LegalDocumentReader _reader;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TermsAcceptanceStore>? _logger;

    public TermsAcceptanceStore(ApplicationDbContext db, LegalDocumentReader reader, IMemoryCache cache, ILogger<TermsAcceptanceStore>? logger = null)
    {
        _db = db;
        _reader = reader;
        _cache = cache;
        _logger = logger;
    }

    private static string StatusCacheKey(string userId, string key) => $"terms-status:{key}:{userId}";

    /// <summary>
    /// The document's own version; when the file or its version cannot be read, the newest version
    /// anyone has accepted, so the gate does not fall open. Null when neither exists.
    /// </summary>
    public async Task<string?> GetRequiredVersionAsync(string key, CancellationToken ct = default)
    {
        var document = await _reader.GetAsync(key);
        if (document?.Version != null)
        {
            return document.Version;
        }

        return await GetNewestAcceptedVersionAsync(key, ct);
    }

    /// <summary>Loaded as distinct strings and compared in memory: SQL MAX would order them ordinally.</summary>
    private async Task<string?> GetNewestAcceptedVersionAsync(string key, CancellationToken ct)
    {
        var versions = await _db.UserTermsAcceptances
            .AsNoTracking()
            .Where(a => a.DocumentKey == key)
            .Select(a => a.Version)
            .Distinct()
            .ToListAsync(ct);

        string? newest = null;
        foreach (var version in versions)
        {
            if (newest == null || LegalVersion.Compare(version, newest) > 0)
            {
                newest = version;
            }
        }

        return newest;
    }

    /// <summary>Cached per user and document for ten minutes, and evicted when the user accepts.</summary>
    public async Task<TermsAcceptanceStatus> GetStatusAsync(string userId, string key, CancellationToken ct = default)
    {
        string required = await GetRequiredVersionAsync(key, ct) ?? string.Empty;
        string cacheKey = StatusCacheKey(userId, key);

        if (_cache.TryGetValue(cacheKey, out CachedStatus? cached) && cached != null && cached.RequiredVersion == required)
        {
            return cached.Status;
        }

        var status = await ComputeStatusAsync(userId, key, required.Length == 0 ? null : required, ct);
        _cache.Set(cacheKey, new CachedStatus(required, status), new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = StatusLifetime
        });

        return status;
    }

    private sealed record CachedStatus(string RequiredVersion, TermsAcceptanceStatus Status);

    private async Task<TermsAcceptanceStatus> ComputeStatusAsync(string userId, string key, string? required, CancellationToken ct)
    {
        var rows = await _db.UserTermsAcceptances
            .AsNoTracking()
            .Where(a => a.AspNetUserId == userId && a.DocumentKey == key)
            .Select(a => new { a.Version, a.AcceptedUtc, a.Source })
            .ToListAsync(ct);

        var exact = required == null ? null : rows.FirstOrDefault(r => r.Version == required);
        var latest = exact ?? rows
            .OrderByDescending(r => r.Version, Comparer<string>.Create(LegalVersion.Compare))
            .FirstOrDefault();

        return new TermsAcceptanceStatus(
            required,
            latest?.Version,
            latest?.AcceptedUtc,
            latest?.Source,
            IsAccepted: required == null || exact != null);
    }

    /// <summary>Writes an acceptance of the version required now. Append-only; a repeat writes nothing.</summary>
    public async Task<TermsRecordResult> RecordAsync(string userId, string key, string version, string source,
        string? contentSha256, string? clientVersion, CancellationToken ct = default)
    {
        string? required = await GetRequiredVersionAsync(key, ct);
        if (required == null || !string.Equals(version?.Trim(), required, StringComparison.Ordinal))
        {
            return TermsRecordResult.StaleVersion;
        }

        bool exists = await _db.UserTermsAcceptances
            .AnyAsync(a => a.AspNetUserId == userId && a.DocumentKey == key && a.Version == required, ct);
        if (exists)
        {
            _cache.Remove(StatusCacheKey(userId, key));
            return TermsRecordResult.AlreadyAccepted;
        }

        var row = new UserTermsAcceptance
        {
            AspNetUserId = userId,
            DocumentKey = key,
            Version = required,
            ContentSha256 = string.IsNullOrWhiteSpace(contentSha256) ? null : Truncate(contentSha256.Trim().ToLowerInvariant(), 64),
            AcceptedUtc = DateTime.UtcNow,
            Source = Truncate(source, 16)!,
            ClientVersion = Truncate(clientVersion, 64)
        };

        _db.UserTermsAcceptances.Add(row);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // A double submit loses the race on the unique index; the other request recorded it.
            _db.Entry(row).State = EntityState.Detached;
            bool recordedElsewhere = await _db.UserTermsAcceptances
                .AnyAsync(a => a.AspNetUserId == userId && a.DocumentKey == key && a.Version == required, ct);
            if (!recordedElsewhere)
            {
                throw;
            }

            _logger?.LogDebug(ex, "Terms acceptance for {Key} {Version} was already recorded", key, required);
            _cache.Remove(StatusCacheKey(userId, key));
            return TermsRecordResult.AlreadyAccepted;
        }

        _cache.Remove(StatusCacheKey(userId, key));
        return TermsRecordResult.Recorded;
    }

    private static string? Truncate(string? value, int max)
        => value == null ? null : value.Length <= max ? value : value[..max];
}
