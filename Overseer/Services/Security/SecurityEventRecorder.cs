using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace Overseer.Services.Security;

/// <summary>One security event to record. Identifiers and codes only, never user content; <see cref="Quarantine"/> carries encrypted game text only.</summary>
public sealed record SecurityEventEntry(
    SecurityEventKind Kind,
    string? UserId = null,
    string? IpAddress = null,
    long? SystemAiApiConfigurationId = null,
    string? Provider = null,
    string? ModelId = null,
    string? Code = null,
    string? Detail = null,
    DateTime? OccurredUtc = null,
    IReadOnlyList<SecurityEventQuarantineItem>? Quarantine = null);

/// <summary>
/// One game text the snapshot guard found something in, already sealed by <see cref="SnapshotQuarantineCipher"/>;
/// written as a <see cref="SnapshotGuardQuarantine"/> row of the event. The plaintext never enters the queue.
/// </summary>
public sealed record SecurityEventQuarantineItem(
    string Channel,
    string Subject,
    string TextKind,
    string Level,
    string Codes,
    bool PlayerAuthored,
    bool Withheld,
    string SessionMode,
    string? SessionRef,
    int PlainLength,
    int StoredLength,
    bool Truncated,
    string Sha256,
    string KeyVersion,
    string Ciphertext,
    string Nonce,
    string Tag);

/// <summary>Records security events without making the caller wait for the database or see its failures.</summary>
public interface ISecurityEventRecorder
{
    void Record(SecurityEventEntry entry);
}

/// <summary>
/// Queues events to a bounded channel (oldest dropped when full) drained by a background loop that writes
/// in its own scope. Identical events — same kind, user (or IP when there is no user), configuration and
/// code — within <see cref="RunWindow"/> of a run's first occurrence extend that run's row. Each entry's quarantine
/// items become rows of the event it was added to or extended. The quarantine ciphertext the queue holds is capped
/// by <see cref="QuarantineQueueBudgetBytes"/>; an entry over the budget is queued without its quarantine.
/// </summary>
public sealed class SecurityEventRecorder : BackgroundService, ISecurityEventRecorder
{
    public const int QueueCapacity = 1000;
    public static readonly TimeSpan RunWindow = TimeSpan.FromMinutes(10);

    /// <summary>The UTF-16 bytes of quarantine ciphertext the queue may hold at once.</summary>
    public const long QuarantineQueueBudgetBytes = 64L * 1024 * 1024;

    private static readonly TimeSpan BudgetWarningInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SecurityEventRecorder> _logger;
    private readonly TimeProvider _time;
    private readonly Channel<SecurityEventEntry> _queue;
    private readonly long _quarantineBudgetBytes;
    private long _queuedQuarantineBytes;
    private long _lastBudgetWarningTicks;

    public SecurityEventRecorder(IServiceScopeFactory scopeFactory, ILogger<SecurityEventRecorder> logger, TimeProvider? time = null)
        : this(scopeFactory, logger, time, QuarantineQueueBudgetBytes)
    {
    }

    internal SecurityEventRecorder(IServiceScopeFactory scopeFactory, ILogger<SecurityEventRecorder> logger, TimeProvider? time, long quarantineBudgetBytes)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _quarantineBudgetBytes = quarantineBudgetBytes;
        // The callback runs under the channel's lock, so it only releases the dropped entry's bytes.
        _queue = Channel.CreateBounded<SecurityEventEntry>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        }, dropped => Release(dropped));
    }

    /// <summary>The quarantine bytes queued now.</summary>
    internal long QueuedQuarantineBytes => Interlocked.Read(ref _queuedQuarantineBytes);

    public void Record(SecurityEventEntry entry)
    {
        var stamped = entry.OccurredUtc.HasValue ? entry : entry with { OccurredUtc = _time.GetUtcNow().UtcDateTime };

        long size = QuarantineBytes(stamped);
        if (size > 0)
        {
            if (Interlocked.Add(ref _queuedQuarantineBytes, size) > _quarantineBudgetBytes)
            {
                Interlocked.Add(ref _queuedQuarantineBytes, -size);
                stamped = stamped with { Quarantine = null };
                size = 0;
                WarnBudgetFull();
            }
        }

        if (!_queue.Writer.TryWrite(stamped) && size > 0)
        {
            Interlocked.Add(ref _queuedQuarantineBytes, -size);
        }
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
            // Released on dequeue, so a failed write below does not keep the budget spent.
            Release(entry);
            batch.Add(entry);
        }
        if (batch.Count == 0) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var entry in batch)
            {
                var securityEvent = await AddOrExtendAsync(db, entry, ct);
                AddQuarantine(db, securityEvent, entry);
            }
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (!ex.IsCanceledBy(ct))
        {
            _logger.LogWarning(ex, "Could not record {Count} security event(s).", batch.Count);
        }
    }

    /// <summary>Adds a new event or extends the open run it belongs to, and returns that event; both are tracked.</summary>
    private static async Task<SecurityEvent> AddOrExtendAsync(ApplicationDbContext db, SecurityEventEntry entry, CancellationToken ct)
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
            return open;
        }

        var added = new SecurityEvent
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
        };
        db.SecurityEvents.Add(added);
        return added;
    }

    /// <summary>
    /// One row per quarantine item, linked by navigation so the event's id is filled in on save, for a new event and
    /// an extended run alike: every refusal of a folded run keeps its own text.
    /// </summary>
    private static void AddQuarantine(ApplicationDbContext db, SecurityEvent securityEvent, SecurityEventEntry entry)
    {
        if (entry.Quarantine is not { Count: > 0 } items) return;

        DateTime at = entry.OccurredUtc ?? DateTime.UtcNow;
        string? userId = Cut(entry.UserId, 450);
        foreach (var item in items)
        {
            db.SnapshotGuardQuarantines.Add(new SnapshotGuardQuarantine
            {
                SecurityEvent = securityEvent,
                OccurredUtc = at,
                AspNetUserId = userId,
                Channel = Cut(item.Channel, 64)!,
                Subject = Cut(item.Subject, 16)!,
                TextKind = Cut(item.TextKind, 16)!,
                Level = Cut(item.Level, 16)!,
                Codes = Cut(item.Codes, 256)!,
                PlayerAuthored = item.PlayerAuthored,
                Withheld = item.Withheld,
                SessionMode = Cut(item.SessionMode, 16)!,
                SessionRef = Cut(item.SessionRef, 64),
                PlainLength = item.PlainLength,
                StoredLength = item.StoredLength,
                Truncated = item.Truncated,
                Sha256 = Cut(item.Sha256, 64)!,
                KeyVersion = Cut(item.KeyVersion, 32)!,
                Ciphertext = item.Ciphertext,
                Nonce = Cut(item.Nonce, 64)!,
                Tag = Cut(item.Tag, 64)!
            });
        }
    }

    /// <summary>The UTF-16 bytes of an entry's quarantine ciphertext.</summary>
    private static long QuarantineBytes(SecurityEventEntry entry)
    {
        if (entry.Quarantine == null) return 0;
        long chars = 0;
        foreach (var item in entry.Quarantine)
        {
            chars += item.Ciphertext?.Length ?? 0;
        }
        return 2 * chars;
    }

    private void Release(SecurityEventEntry entry)
    {
        long size = QuarantineBytes(entry);
        if (size > 0) Interlocked.Add(ref _queuedQuarantineBytes, -size);
    }

    private void WarnBudgetFull()
    {
        long now = _time.GetUtcNow().UtcTicks;
        long last = Interlocked.Read(ref _lastBudgetWarningTicks);
        if (now - last < BudgetWarningInterval.Ticks) return;
        if (Interlocked.CompareExchange(ref _lastBudgetWarningTicks, now, last) != last) return;
        _logger.LogWarning("The security event queue's quarantine budget is full; refused game text is not being kept.");
    }

    private static bool Matches(SecurityEvent e, SecurityEventKind kind, string? userId, string? ip, long? configId, string? code) =>
        e.Kind == kind && e.SystemAiApiConfigurationId == configId && e.Code == code
        && (userId != null ? e.AspNetUserId == userId : e.AspNetUserId == null && e.IpAddress == ip);

    private static string? Cut(string? value, int max) =>
        value == null ? null : value.Length <= max ? value : value[..max];
}
