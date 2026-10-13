using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace GnollHackServer.Data.Security;

/// <summary>
/// Writes security events from the website, which cannot use Overseer's queued recorder: directly through the given
/// context, with the same ten-minute run aggregation, and at most <see cref="MaxWritesPerIpPerMinute"/> writes per IP
/// address a minute so an attack cannot turn into a database flood. Holds identifiers and codes only. Never throws.
/// </summary>
public static class SecurityEventWriter
{
    public const int MaxWritesPerIpPerMinute = 30;
    public static readonly TimeSpan RunWindow = TimeSpan.FromMinutes(10);

    private const int MaxTrackedIps = 10_000;
    private static readonly ConcurrentDictionary<string, (DateTime WindowStart, int Count)> _writesPerIp = new();

    public static async Task RecordAsync(ApplicationDbContext db, SecurityEventKind kind, string? userId = null, string? ipAddress = null,
        string? code = null, string? detail = null, DateTime? utcNow = null, CancellationToken ct = default)
    {
        DateTime now = utcNow ?? DateTime.UtcNow;
        if (!AllowWrite(ipAddress, now)) return;

        SecurityEvent? added = null;
        try
        {
            string? ip = Cut(ipAddress, 45);
            string? cutCode = Cut(code, 64);
            DateTime windowStart = now - RunWindow;

            var query = db.SecurityEvents.Where(e => e.Kind == kind && e.OccurredUtc >= windowStart
                && e.SystemAiApiConfigurationId == null && e.Code == cutCode);
            query = userId != null
                ? query.Where(e => e.AspNetUserId == userId)
                : query.Where(e => e.AspNetUserId == null && e.IpAddress == ip);
            var open = await query.OrderByDescending(e => e.OccurredUtc).FirstOrDefaultAsync(ct);

            if (open != null)
            {
                open.Count = open.Count == int.MaxValue ? int.MaxValue : open.Count + 1;
                if (now > open.LastOccurredUtc) open.LastOccurredUtc = now;
            }
            else
            {
                added = new SecurityEvent
                {
                    OccurredUtc = now,
                    LastOccurredUtc = now,
                    Kind = kind,
                    Count = 1,
                    AspNetUserId = userId,
                    IpAddress = ip,
                    Code = cutCode,
                    Detail = Cut(detail, 256)
                };
                db.SecurityEvents.Add(added);
            }
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Leave the caller's context usable: a rejected event row would fail every later save.
            if (added != null)
            {
                try { db.Entry(added).State = EntityState.Detached; } catch { }
            }
        }
    }

    /// <summary>Counts one write for the IP's current minute; false when the IP has used its writes.</summary>
    public static bool AllowWrite(string? ipAddress, DateTime now)
    {
        string key = ipAddress ?? "unknown";
        if (_writesPerIp.Count > MaxTrackedIps)
        {
            foreach (var stale in _writesPerIp.Where(p => now - p.Value.WindowStart >= TimeSpan.FromMinutes(1)).Select(p => p.Key).ToList())
            {
                _writesPerIp.TryRemove(stale, out _);
            }
        }

        var entry = _writesPerIp.AddOrUpdate(key,
            _ => (now, 1),
            (_, current) => now - current.WindowStart >= TimeSpan.FromMinutes(1) ? (now, 1) : (current.WindowStart, current.Count + 1));
        return entry.Count <= MaxWritesPerIpPerMinute;
    }

    private static string? Cut(string? value, int max) =>
        value == null ? null : value.Length <= max ? value : value[..max];
}
