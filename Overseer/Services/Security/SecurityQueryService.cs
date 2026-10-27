using GnollHackServer.Data.Privacy;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Privacy;

namespace Overseer.Services.Security;

/// <summary>Reads the security event log for the Admin page's Security tab.</summary>
public class SecurityQueryService
{
    /// <summary>The newest kept texts an event's detail returns.</summary>
    public const int MaxQuarantineItems = 50;

    /// <summary>The newest rows each related list returns.</summary>
    public const int MaxRelatedRows = 20;

    /// <summary>How far the related records' window reaches before the run's first and after its last occurrence.</summary>
    public static readonly TimeSpan RelatedWindowMargin = TimeSpan.FromMinutes(10);

    private static readonly HashSet<SecurityEventKind> AccountKinds = new()
    {
        SecurityEventKind.AutoBlocked, SecurityEventKind.AdminBlocked, SecurityEventKind.AdminUnblocked,
        SecurityEventKind.AccountHoldPlaced, SecurityEventKind.AccountHoldReleased, SecurityEventKind.AccountDeletionHeld,
        SecurityEventKind.AdminLockedOut, SecurityEventKind.AdminUnlocked, SecurityEventKind.AdminBanned,
        SecurityEventKind.AdminUnbanned, SecurityEventKind.AdminSignedOut, SecurityEventKind.AdminEmailConfirmed,
        SecurityEventKind.AdminTwoFactorReset, SecurityEventKind.AdminEmailChanged,
    };

    private static readonly HashSet<SecurityEventKind> ProviderKinds = new()
    {
        SecurityEventKind.ProviderRefusal, SecurityEventKind.ProviderSafetyBlock,
        SecurityEventKind.ProviderPolicyRejection, SecurityEventKind.QuotaRefused,
    };

    private static readonly HashSet<SecurityEventKind> AddressKinds = new()
    {
        SecurityEventKind.LoginFailed, SecurityEventKind.LoginLockedOut,
        SecurityEventKind.TwoFactorFailed, SecurityEventKind.RateLimitRejected,
    };

    /// <summary>What each kind means, who writes it, and what its code and detail are (security-monitoring.md § 5).</summary>
    private static readonly Dictionary<SecurityEventKind, string> Explanations = new()
    {
        [SecurityEventKind.LoginFailed] = "A sign-in failed: a wrong password, or a user name no account has (Detail unknown-user). Written by Overseer's login and session create, the website login and the game APIs; the IP address is kept, the attempted user name is not.",
        [SecurityEventKind.LoginLockedOut] = "A sign-in was refused because the account is locked out after repeated failures. Written by the same sign-in paths as LoginFailed; the IP address is kept.",
        [SecurityEventKind.TwoFactorFailed] = "A wrong two-factor code was entered at sign-in. The IP address is kept.",
        [SecurityEventKind.RateLimitRejected] = "A rate limiter refused a request. Code is the limiter's policy (for example game-api-signin, session-create or session-create-turn) and Detail, where given, the limit that tripped: pair, account or ip.",
        [SecurityEventKind.QuotaRefused] = "A usage limit refused an AI call before it reached the provider. Code is the limit: user, group or system for a configuration's quota, guard-cost or guard-runs for the AI usage guard.",
        [SecurityEventKind.AccessDenied] = "An AI call was refused because the user may not use the configuration. Code is disabled (the configuration is switched off) or not-assigned (the user has no access to it).",
        [SecurityEventKind.ProviderRefusal] = "The provider ended an operator-funded chat turn or sub-agent run with a refusal. Code is the provider's finish reason; incognito turns are included, title generation never.",
        [SecurityEventKind.ProviderSafetyBlock] = "The provider's safety filter blocked an operator-funded chat turn or sub-agent run. Code is the block or finish reason; incognito turns are included, title generation never.",
        [SecurityEventKind.ProviderPolicyRejection] = "The provider rejected the final attempt of an operator-funded call with a 4xx error naming a policy. Code is the sanitized provider error code.",
        [SecurityEventKind.AttachmentRejected] = "The chat refused an attachment. Code is the check that failed: count, validation, content, malware or scan-failed.",
        [SecurityEventKind.ChatReported] = "The user reported a chat message, recorded after the report email was sent. Code names the message (msg:<id>); the report itself is not stored.",
        [SecurityEventKind.SnapshotRejected] = "The snapshot guard found injected instructions or suspicious text in game data from session create, a snapshot attach or a client tool result. Code is injection-own, injection-foreign, suspicious-own or suspicious-foreign; Detail lists the channels' codes, beginning observed; in Observe mode.",
        [SecurityEventKind.AutoBlocked] = "The snapshot guard's rule blocked the user from the AI chat automatically after repeated injections in their own text. Code is snapshot-injection and Detail the reason.",
        [SecurityEventKind.AdminBlocked] = "An administrator blocked the user from the AI chat. Detail is the reason; the admin log names the administrator.",
        [SecurityEventKind.AdminUnblocked] = "An administrator lifted the user's AI chat block. The admin log names the administrator.",
        [SecurityEventKind.AccountHoldPlaced] = "An administrator placed a hold on deleting the account. Detail is the reason.",
        [SecurityEventKind.AccountHoldReleased] = "An administrator released the hold on deleting the account. Detail is the reason.",
        [SecurityEventKind.AccountDeletionHeld] = "The user's request to delete their account was held for review. Code is manual (an active hold) or automatic (the automatic rule on recent events).",
        [SecurityEventKind.AdminLockedOut] = "An administrator locked the account out, from the Users tab or a bulk action. Detail is the reason; the admin log names the administrator.",
        [SecurityEventKind.AdminUnlocked] = "An administrator ended the account's lockout, from the Users tab or a bulk action. Detail is the reason; the admin log names the administrator.",
        [SecurityEventKind.AdminBanned] = "An administrator banned the account, from the Users tab or a bulk action. Detail is the reason; the admin log names the administrator.",
        [SecurityEventKind.AdminUnbanned] = "An administrator lifted the account's ban, from the Users tab or a bulk action. Detail is the reason; the admin log names the administrator.",
        [SecurityEventKind.AdminSignedOut] = "An administrator signed the user out everywhere, from the Users tab or a bulk action. Detail is the reason; the admin log names the administrator.",
        [SecurityEventKind.AdminEmailConfirmed] = "An administrator confirmed the account's email address, from the Users tab or a bulk action. Detail is the reason; the admin log names the administrator.",
        [SecurityEventKind.AdminTwoFactorReset] = "An administrator reset the account's two-factor authentication, from the Users tab or a bulk action. Detail is the reason; the admin log names the administrator.",
        [SecurityEventKind.AdminEmailChanged] = "An administrator changed the account's email address, from the Users tab. Detail is the reason; the admin log names the administrator.",
    };

    private readonly ApplicationDbContext _db;
    private readonly IContentKeyRing _keyRing;
    private readonly ILogger<SecurityQueryService>? _logger;

    public SecurityQueryService(ApplicationDbContext db, IContentKeyRing keyRing, ILogger<SecurityQueryService>? logger = null)
    {
        _db = db;
        _keyRing = keyRing;
        _logger = logger;
    }

    /// <summary>What <paramref name="kind"/> means; a generic sentence for a kind without one.</summary>
    public static string Explain(SecurityEventKind kind)
        => Explanations.TryGetValue(kind, out var text) ? text : "No explanation is recorded for this kind.";

    /// <summary>Whether <paramref name="kind"/> has its own explanation.</summary>
    public static bool HasExplanation(SecurityEventKind kind) => Explanations.ContainsKey(kind);

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? Utc(DateTime? value) => value.HasValue ? Utc(value.Value) : null;

    /// <summary>Events whose run overlaps the range: last occurrence on or after the start, first on or before the end day.</summary>
    private IQueryable<SecurityEvent> InRange(DateTime? start, DateTime? end)
    {
        var query = _db.SecurityEvents.AsNoTracking();
        if (start.HasValue)
        {
            DateTime from = start.Value.Date;
            query = query.Where(e => e.LastOccurredUtc >= from);
        }
        if (end.HasValue)
        {
            DateTime to = end.Value.Date.AddDays(1);
            query = query.Where(e => e.OccurredUtc < to);
        }
        return query;
    }

    public async Task<SecuritySummaryDto> GetSummaryAsync(DateTime? start, DateTime? end, CancellationToken ct = default)
    {
        var rows = await InRange(start, end)
            .Select(e => new { e.Kind, e.Count, e.AspNetUserId, e.IpAddress })
            .ToListAsync(ct);

        var kinds = rows
            .GroupBy(r => r.Kind)
            .Select(g => new SecurityKindCountDto
            {
                Kind = g.Key.ToString(),
                Count = g.Sum(r => (long)r.Count),
                DistinctUsers = g.Where(r => r.AspNetUserId != null).Select(r => r.AspNetUserId).Distinct().Count(),
                DistinctIps = g.Where(r => r.IpAddress != null).Select(r => r.IpAddress).Distinct().Count()
            })
            .OrderBy(k => k.Kind, StringComparer.Ordinal)
            .ToList();

        return new SecuritySummaryDto
        {
            Kinds = kinds,
            PendingDeletionRequests = await _db.AccountDeletionRequests.CountAsync(r => r.Status == AccountDeletionRequestStatus.Pending, ct),
            ActiveAccountHolds = await _db.AccountHolds.CountAsync(h => h.ReleasedUtc == null, ct)
        };
    }

    /// <param name="kinds">Comma-separated <see cref="SecurityEventKind"/> names; unknown names are ignored.</param>
    /// <param name="search">Matches user names.</param>
    public async Task<SecurityEventsPageDto> GetEventsAsync(DateTime? start, DateTime? end, string? kinds, string? userId,
        string? search, int skip, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take <= 0 ? 20 : take, 1, 100);
        skip = Math.Max(0, skip);

        var query = InRange(start, end);

        var kindFilter = (kinds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => Enum.TryParse<SecurityEventKind>(k, ignoreCase: true, out var kind) ? (SecurityEventKind?)kind : null)
            .Where(k => k.HasValue)
            .Select(k => k!.Value)
            .ToList();
        if (kindFilter.Count > 0)
        {
            query = query.Where(e => kindFilter.Contains(e.Kind));
        }

        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(e => e.AspNetUserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            var matchingUsers = _db.Users.Where(u => u.UserName != null && u.UserName.Contains(term)).Select(u => u.Id);
            query = query.Where(e => e.AspNetUserId != null && matchingUsers.Contains(e.AspNetUserId));
        }

        int total = await query.CountAsync(ct);
        var page = await query
            .OrderByDescending(e => e.LastOccurredUtc)
            .ThenByDescending(e => e.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var userIds = page.Where(e => e.AspNetUserId != null).Select(e => e.AspNetUserId!).Distinct().ToList();
        var users = await _db.Users.OfType<ApplicationUser>().AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName, Blocked = u.OverseerBlockedUtc != null })
            .ToDictionaryAsync(u => u.Id, ct);

        var eventIds = page.Select(e => e.Id).ToList();
        var quarantineCounts = eventIds.Count == 0
            ? new Dictionary<long, int>()
            : await _db.SnapshotGuardQuarantines.AsNoTracking()
                .Where(q => eventIds.Contains(q.SecurityEventId))
                .GroupBy(q => q.SecurityEventId)
                .Select(g => new { EventId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.EventId, g => g.Count, ct);

        return new SecurityEventsPageDto
        {
            TotalCount = total,
            Rows = page.Select(e =>
            {
                var user = e.AspNetUserId != null && users.TryGetValue(e.AspNetUserId, out var u) ? u : null;
                var dto = new SecurityEventDto();
                Fill(dto, e, user?.UserName, user?.Blocked ?? false);
                dto.QuarantineCount = quarantineCounts.TryGetValue(e.Id, out int count) ? count : 0;
                return dto;
            }).ToList()
        };
    }

    private static void Fill(SecurityEventDto dto, SecurityEvent e, string? userName, bool userIsBlocked)
    {
        dto.Id = e.Id;
        dto.OccurredUtc = Utc(e.OccurredUtc);
        dto.LastOccurredUtc = Utc(e.LastOccurredUtc);
        dto.Kind = e.Kind.ToString();
        dto.Count = e.Count;
        dto.UserId = e.AspNetUserId;
        dto.UserName = userName;
        dto.UserIsOverseerBlocked = userIsBlocked;
        dto.IpAddress = e.IpAddress;
        dto.SystemAiApiConfigurationId = e.SystemAiApiConfigurationId;
        dto.Provider = e.Provider;
        dto.ModelId = e.ModelId;
        dto.Code = e.Code;
        dto.Detail = e.Detail;
    }

    /// <summary>
    /// One event with its newest kept game texts (decrypted), their total, its related records and its kind's
    /// explanation; null when no event has <paramref name="id"/>. Returning any text writes one
    /// <see cref="ChatAccessAction.SecurityContentRead"/> journal row naming identifiers only.
    /// </summary>
    public async Task<SecurityEventDetailDto?> GetEventDetailAsync(long id, string actorUserId, string? actorUserName,
        string? ipAddress, CancellationToken ct = default)
    {
        var e = await _db.SecurityEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e == null)
        {
            return null;
        }

        var user = e.AspNetUserId == null
            ? null
            : await _db.Users.OfType<ApplicationUser>().AsNoTracking()
                .Where(u => u.Id == e.AspNetUserId)
                .Select(u => new { u.UserName, Blocked = u.OverseerBlockedUtc != null })
                .FirstOrDefaultAsync(ct);

        var detail = new SecurityEventDetailDto();
        Fill(detail, e, user?.UserName, user?.Blocked ?? false);
        detail.Explanation = Explain(e.Kind);

        if (e.SystemAiApiConfigurationId is long configurationId)
        {
            detail.SystemConfigurationName = await _db.SystemAiApiConfigurations.AsNoTracking()
                .Where(c => c.Id == configurationId)
                .Select(c => c.DisplayName)
                .FirstOrDefaultAsync(ct);
        }

        detail.QuarantineTotalCount = await _db.SnapshotGuardQuarantines.CountAsync(q => q.SecurityEventId == id, ct);
        detail.QuarantineCount = detail.QuarantineTotalCount;
        if (detail.QuarantineTotalCount > 0)
        {
            var rows = await _db.SnapshotGuardQuarantines.AsNoTracking()
                .Where(q => q.SecurityEventId == id)
                .OrderByDescending(q => q.OccurredUtc)
                .ThenByDescending(q => q.Id)
                .Take(MaxQuarantineItems)
                .ToListAsync(ct);
            detail.Quarantine = rows.Select(ToItem).ToList();
        }

        detail.Related = await GetRelatedAsync(e, ct);

        if (detail.Quarantine.Count > 0)
        {
            string? firstRef = detail.Quarantine[0].SessionRef;
            bool sharedRef = detail.Quarantine.All(q => string.Equals(q.SessionRef, firstRef, StringComparison.Ordinal));
            bool written = await ChatAccessAudit.RecordAsync(
                _db,
                ChatAccessAction.SecurityContentRead,
                actorUserId,
                actorUserName,
                actorWasAdmin: true,
                subjectUserId: e.AspNetUserId,
                sessionRef: sharedRef ? firstRef : null,
                wasConfidential: detail.Quarantine.Any(q => !string.Equals(q.SessionMode, "normal", StringComparison.OrdinalIgnoreCase)),
                ipAddress: ipAddress,
                detail: $"security-event:{id}; texts:{detail.Quarantine.Count}",
                cancellationToken: ct);
            if (!written)
            {
                _logger?.LogWarning("The access journal row for security event {EventId}'s quarantined texts could not be written", id);
            }
        }

        return detail;
    }

    private SecurityQuarantineItemDto ToItem(SnapshotGuardQuarantine q) => new()
    {
        Id = q.Id,
        OccurredUtc = Utc(q.OccurredUtc),
        Channel = q.Channel,
        Subject = q.Subject,
        TextKind = q.TextKind,
        Level = q.Level,
        Codes = (q.Codes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        PlayerAuthored = q.PlayerAuthored,
        Withheld = q.Withheld,
        SessionMode = q.SessionMode,
        SessionRef = q.SessionRef,
        PlainLength = q.PlainLength,
        StoredLength = q.StoredLength,
        Truncated = q.Truncated,
        Sha256 = q.Sha256,
        Text = SnapshotQuarantineCipher.Open(_keyRing, q)
    };

    /// <summary>The message id of a <c>ChatReported</c> code <c>msg:&lt;id&gt;</c>; null for any other code.</summary>
    public static long? ReportedMessageId(string? code)
    {
        const string prefix = "msg:";
        if (code == null || !code.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }
        return long.TryParse(code.AsSpan(prefix.Length), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out long messageId) ? messageId : null;
    }

    /// <summary>
    /// The records around <paramref name="e"/>, each list the newest <see cref="MaxRelatedRows"/> over the run's window
    /// widened by <see cref="RelatedWindowMargin"/> unless the list is a state (the hold, the deletion request, the
    /// terms acceptances), and filled only for the kinds it applies to.
    /// </summary>
    private async Task<SecurityRelatedDto> GetRelatedAsync(SecurityEvent e, CancellationToken ct)
    {
        var related = new SecurityRelatedDto();
        DateTime from = e.OccurredUtc - RelatedWindowMargin;
        DateTime to = e.LastOccurredUtc + RelatedWindowMargin;
        string? userId = e.AspNetUserId;

        if (userId != null)
        {
            related.TermsAcceptances = (await _db.UserTermsAcceptances.AsNoTracking()
                .Where(t => t.AspNetUserId == userId)
                .OrderByDescending(t => t.AcceptedUtc)
                .ThenByDescending(t => t.Id)
                .Take(MaxRelatedRows)
                .Select(t => new { t.DocumentKey, t.Version, t.AcceptedUtc, t.Source })
                .ToListAsync(ct))
                .Select(t => new SecurityTermsAcceptanceDto
                {
                    DocumentKey = t.DocumentKey,
                    Version = t.Version,
                    AcceptedUtc = Utc(t.AcceptedUtc),
                    Source = t.Source
                })
                .ToList();

            related.SameUserEvents = (await _db.SecurityEvents.AsNoTracking()
                .Where(x => x.AspNetUserId == userId && x.Id != e.Id && x.LastOccurredUtc >= from && x.OccurredUtc <= to)
                .OrderByDescending(x => x.LastOccurredUtc)
                .ThenByDescending(x => x.Id)
                .Take(MaxRelatedRows)
                .Select(x => new { x.Id, x.Kind, x.Code, x.Detail })
                .ToListAsync(ct))
                .Select(x => new SecuritySameUserEventDto { Id = x.Id, Kind = x.Kind.ToString(), Code = x.Code, Detail = x.Detail })
                .ToList();
        }

        if (userId != null && AccountKinds.Contains(e.Kind))
        {
            related.AdminActions = (await _db.AdminUserActions.AsNoTracking()
                .Where(a => a.SubjectUserId == userId && a.OccurredUtc >= from && a.OccurredUtc <= to)
                .OrderByDescending(a => a.OccurredUtc)
                .ThenByDescending(a => a.Id)
                .Take(MaxRelatedRows)
                .Select(a => new { a.Id, a.OccurredUtc, a.Kind, a.ActorUserName, a.Reason, a.Detail, a.BulkOperationId })
                .ToListAsync(ct))
                .Select(a => new SecurityAdminActionDto
                {
                    Id = a.Id,
                    OccurredUtc = Utc(a.OccurredUtc),
                    Kind = a.Kind.ToString(),
                    ActorUserName = a.ActorUserName,
                    Reason = a.Reason,
                    Detail = a.Detail,
                    BulkOperationId = a.BulkOperationId
                })
                .ToList();

            var hold = await _db.AccountHolds.AsNoTracking()
                .Where(h => h.AspNetUserId == userId && h.ReleasedUtc == null)
                .OrderByDescending(h => h.PlacedUtc)
                .ThenByDescending(h => h.Id)
                .Select(h => new { h.Id, h.Reason, h.PlacedByUserId, h.PlacedUtc })
                .FirstOrDefaultAsync(ct);
            if (hold != null)
            {
                string? placedBy = await _db.Users.AsNoTracking()
                    .Where(u => u.Id == hold.PlacedByUserId)
                    .Select(u => u.UserName)
                    .FirstOrDefaultAsync(ct);
                related.Hold = new SecurityHoldDto
                {
                    Id = hold.Id,
                    Reason = hold.Reason,
                    PlacedByUserName = placedBy ?? hold.PlacedByUserId,
                    PlacedUtc = Utc(hold.PlacedUtc)
                };
            }

            var request = await _db.AccountDeletionRequests.AsNoTracking()
                .Where(r => r.AspNetUserId == userId)
                .OrderByDescending(r => r.RequestedUtc)
                .ThenByDescending(r => r.Id)
                .Select(r => new { r.Id, r.Status, r.RequestedUtc, r.HoldReasons })
                .FirstOrDefaultAsync(ct);
            if (request != null)
            {
                related.DeletionRequest = new SecurityDeletionRequestDto
                {
                    Id = request.Id,
                    Status = request.Status.ToString(),
                    RequestedUtc = Utc(request.RequestedUtc),
                    HoldReasons = request.HoldReasons
                };
            }
        }

        if (userId != null && e.Kind == SecurityEventKind.SnapshotRejected)
        {
            // Never the title: it can be the user's own text, and is enveloped in a confidential session.
            related.Sessions = (await _db.ChatSession.AsNoTracking()
                .Where(s => s.AspNetUserId == userId && s.SnapshotRefusedUtc != null
                    && s.SnapshotRefusedUtc >= from && s.SnapshotRefusedUtc <= to)
                .OrderByDescending(s => s.SnapshotRefusedUtc)
                .ThenByDescending(s => s.Id)
                .Take(MaxRelatedRows)
                .Select(s => new { s.Id, s.CreatedUtc, s.SnapshotRefusedUtc, s.SnapshotRefusalCode, s.IsConfidential })
                .ToListAsync(ct))
                .Select(s => new SecuritySessionDto
                {
                    Id = s.Id,
                    CreatedUtc = Utc(s.CreatedUtc),
                    SnapshotRefusedUtc = Utc(s.SnapshotRefusedUtc),
                    SnapshotRefusalCode = s.SnapshotRefusalCode,
                    IsConfidential = s.IsConfidential
                })
                .ToList();
        }

        if (e.Kind == SecurityEventKind.ChatReported && ReportedMessageId(e.Code) is long messageId)
        {
            // Never the content.
            var message = await _db.ChatMessage.AsNoTracking()
                .Where(m => m.Id == messageId)
                .Select(m => new { m.ChatSessionId, m.TimestampUtc, m.Role })
                .FirstOrDefaultAsync(ct);
            related.ReportedMessage = new SecurityReportedMessageDto
            {
                MessageId = messageId,
                SessionId = message?.ChatSessionId,
                TimestampUtc = message == null ? null : Utc(message.TimestampUtc),
                Role = message?.Role,
                Exists = message != null
            };
        }

        if (userId != null && ProviderKinds.Contains(e.Kind))
        {
            // The same user as well as the same configuration: a shared configuration carries other users' calls.
            long? configurationId = e.SystemAiApiConfigurationId;
            related.ProviderAttempts = (await _db.ChatProviderAttempts.AsNoTracking()
                .Where(a => a.AspNetUserId == userId && a.SystemAiApiConfigurationId == configurationId
                    && a.StartedAtUtc >= from && a.StartedAtUtc <= to)
                .OrderByDescending(a => a.StartedAtUtc)
                .ThenByDescending(a => a.Id)
                .Take(MaxRelatedRows)
                .Select(a => new { a.StartedAtUtc, a.RoleContext, a.RequestedModelId, a.HttpStatus, a.ProviderErrorCode, a.Outcome, a.Retried })
                .ToListAsync(ct))
                .Select(a => new SecurityProviderAttemptDto
                {
                    StartedAtUtc = Utc(a.StartedAtUtc),
                    RoleContext = a.RoleContext,
                    RequestedModelId = a.RequestedModelId,
                    HttpStatus = a.HttpStatus,
                    ProviderErrorCode = a.ProviderErrorCode,
                    Outcome = a.Outcome.ToString(),
                    Retried = a.Retried
                })
                .ToList();

            related.UsageRows = (await _db.SystemAiUsageLogs.AsNoTracking()
                .Where(l => l.AspNetUserId == userId && l.TimestampUtc >= from && l.TimestampUtc <= to)
                .OrderByDescending(l => l.TimestampUtc)
                .ThenByDescending(l => l.Id)
                .Take(MaxRelatedRows)
                .Select(l => new { l.TimestampUtc, l.ModelId, l.RoleContext, l.InputTokens, l.OutputTokens, l.EstimatedCost })
                .ToListAsync(ct))
                .Select(l => new SecurityUsageRowDto
                {
                    TimestampUtc = Utc(l.TimestampUtc),
                    ModelId = l.ModelId,
                    RoleContext = l.RoleContext,
                    Tokens = (l.InputTokens ?? 0) + (l.OutputTokens ?? 0),
                    Cost = l.EstimatedCost
                })
                .ToList();
        }

        if (e.IpAddress is string address && AddressKinds.Contains(e.Kind))
        {
            // SecurityEvents has no address index: the OccurredUtc index narrows the rows first.
            related.IpEvents = (await _db.SecurityEvents.AsNoTracking()
                .Where(x => x.OccurredUtc <= to && x.LastOccurredUtc >= from && x.IpAddress == address && x.Id != e.Id)
                .OrderByDescending(x => x.LastOccurredUtc)
                .ThenByDescending(x => x.Id)
                .Take(MaxRelatedRows)
                .Select(x => new { x.Id, x.Kind, x.Code, x.Count })
                .ToListAsync(ct))
                .Select(x => new SecurityIpEventDto { Id = x.Id, Kind = x.Kind.ToString(), Code = x.Code, Count = x.Count })
                .ToList();

            // Never Message or RequestData.
            related.RequestLogs = (await _db.RequestLogs.AsNoTracking()
                .Where(r => r.UserIPAddress == address && r.FirstDate <= to && r.LastDate >= from)
                .OrderByDescending(r => r.LastDate)
                .ThenByDescending(r => r.Id)
                .Take(MaxRelatedRows)
                .Select(r => new { r.LastDate, r.Count, r.RequestPath, r.RequestMethod, r.ResponseCode, r.RequestUserName })
                .ToListAsync(ct))
                .Select(r => new SecurityRequestLogDto
                {
                    LastDate = Utc(r.LastDate),
                    Count = r.Count,
                    RequestPath = r.RequestPath,
                    RequestMethod = r.RequestMethod,
                    ResponseCode = r.ResponseCode,
                    RequestUserName = r.RequestUserName
                })
                .ToList();
        }

        return related;
    }
}
