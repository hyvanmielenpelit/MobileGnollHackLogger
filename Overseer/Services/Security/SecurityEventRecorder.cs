using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace Overseer.Services.Security;

/// <summary>One security event to record. Identifiers and codes only, never user content.</summary>
public sealed record SecurityEventEntry(
    SecurityEventKind Kind,
    string? UserId = null,
    string? IpAddress = null,
    long? SystemAiApiConfigurationId = null,
    string? Provider = null,
    string? ModelId = null,
    string? Code = null,
    string? Detail = null,
    DateTime? OccurredUtc = null);

/// <summary>Records security events without making the caller wait for the database or see its failures.</summary>
public interface ISecurityEventRecorder
{
    void Record(SecurityEventEntry entry);
}

/// <summary>
/// Queues events to a bounded channel (oldest dropped when full) drained by a background loop that writes
/// in its own scope. Identical events — same kind, user (or IP when there is no user), configuration and
/// code — within <see cref="RunWindow"/> of a run's first occurrence extend that run's row.
/// </summary>
public sealed class SecurityEventRecorder : BackgroundService, ISecurityEventRecorder
{
    public const int QueueCapacity = 1000;
    public static readonly TimeSpan RunWindow = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SecurityEventRecorder> _logger;
    private readonly TimeProvider _time;
    private readonly Channel<SecurityEventEntry> _queue;

    public SecurityEventRecorder(IServiceScopeFactory scopeFactory, ILogger<SecurityEventRecorder> logger, TimeProvider? time = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _queue = Channel.CreateBounded<SecurityEventEntry>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Record(SecurityEventEntry entry)
    {
        var stamped = entry.OccurredUtc.HasValue ? entry : entry with { OccurredUtc = _time.GetUtcNow().UtcDateTime };
        _queue.Writer.TryWrite(stamped);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                await ProcessPendingAsync(stoppingToken);
            }
        }
        catch (Exception ex) when (ex.IsCanceledBy(stoppingToken))
        {
        }
    }

    /// <summary>Writes every event queued so far. Never throws.</summary>
    internal async Task ProcessPendingAsync(CancellationToken ct = default)
    {
        var batch = new List<SecurityEventEntry>();
        while (batch.Count < 200 && _queue.Reader.TryRead(out var entry))
        {
            batch.Add(entry);
        }
        if (batch.Count == 0) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var entry in batch)
            {
                await AddOrExtendAsync(db, entry, ct);
            }
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (!ex.IsCanceledBy(ct))
        {
            _logger.LogWarning(ex, "Could not record {Count} security event(s).", batch.Count);
        }
    }

    private static async Task AddOrExtendAsync(ApplicationDbContext db, SecurityEventEntry entry, CancellationToken ct)
    {
        DateTime at = entry.OccurredUtc ?? DateTime.UtcNow;
        string? userId = Cut(entry.UserId, 450);
        string? ip = Cut(entry.IpAddress, 45);
        string? code = Cut(entry.Code, 64);
        DateTime windowStart = at - RunWindow;

        // Rows added earlier in this batch are not in the database yet.
        var open = db.SecurityEvents.Local.FirstOrDefault(e => Matches(e, entry.Kind, userId, ip, entry.SystemAiApiConfigurationId, code) && e.OccurredUtc >= windowStart);
        if (open == null)
        {
            var query = db.SecurityEvents.Where(e => e.Kind == entry.Kind && e.OccurredUtc >= windowStart
                && e.SystemAiApiConfigurationId == entry.SystemAiApiConfigurationId && e.Code == code);
            query = userId != null
                ? query.Where(e => e.AspNetUserId == userId)
                : query.Where(e => e.AspNetUserId == null && e.IpAddress == ip);
            open = await query.OrderByDescending(e => e.OccurredUtc).FirstOrDefaultAsync(ct);
        }

        if (open != null)
        {
            open.Count = open.Count == int.MaxValue ? int.MaxValue : open.Count + 1;
            if (at > open.LastOccurredUtc) open.LastOccurredUtc = at;
            return;
        }

        db.SecurityEvents.Add(new SecurityEvent
        {
            OccurredUtc = at,
            LastOccurredUtc = at,
            Kind = entry.Kind,
            Count = 1,
            AspNetUserId = userId,
            IpAddress = ip,
            SystemAiApiConfigurationId = entry.SystemAiApiConfigurationId,
            Provider = Cut(entry.Provider, 32),
            ModelId = Cut(entry.ModelId, 128),
            Code = code,
            Detail = Cut(entry.Detail, 256)
        });
    }

    private static bool Matches(SecurityEvent e, SecurityEventKind kind, string? userId, string? ip, long? configId, string? code) =>
        e.Kind == kind && e.SystemAiApiConfigurationId == configId && e.Code == code
        && (userId != null ? e.AspNetUserId == userId : e.AspNetUserId == null && e.IpAddress == ip);

    private static string? Cut(string? value, int max) =>
        value == null ? null : value.Length <= max ? value : value[..max];
}
