namespace Overseer.Services.Telemetry;

using GnollHackServer.Data.Privacy;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>Reads chat turn limit events for AI Telemetry's Limits sub-tab, and the journaled read of a turn's prompt.</summary>
public class ChatTurnLimitQueryService
{
    public const int MaxPromptChars = 4000;
    public const int MaxTake = 100;

    public const string PromptAvailable = "available";
    public const string PromptConfidential = "confidential";
    public const string PromptIncognito = "incognito";
    public const string PromptDeleted = "deleted";

    /// <summary>Why a prompt read did not return text.</summary>
    public enum PromptReadStatus { Ok, NotFound, Confidential, JournalFailed }

    private readonly ApplicationDbContext _db;

    public ChatTurnLimitQueryService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ChatTurnLimitSummaryDto> GetSummaryAsync(DateTime? startDate, DateTime? endDate, string? keys, CancellationToken ct = default)
    {
        var rows = await Filter(startDate, endDate, keys)
            .Select(e => new { e.Code, e.Severity, e.AspNetUserId, e.OccurredUtc, e.AssistantMessageId, e.Id })
            .AsNoTracking()
            .ToListAsync(ct);

        return new ChatTurnLimitSummaryDto
        {
            TotalHits = rows.Count,
            TurnsWithoutAnswer = rows
                .Where(r => r.Severity == 1)
                .Select(r => r.AssistantMessageId ?? -r.Id)
                .Distinct()
                .Count(),
            UsersAffected = rows.Where(r => r.AspNetUserId != null).Select(r => r.AspNetUserId).Distinct().Count(),
            ByCode = rows
                .GroupBy(r => r.Code)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new ChatTurnLimitCodeCountDto
                {
                    Code = g.Key,
                    Hits = g.Count(),
                    Users = g.Where(r => r.AspNetUserId != null).Select(r => r.AspNetUserId).Distinct().Count(),
                    LastSeenUtc = DateTime.SpecifyKind(g.Max(r => r.OccurredUtc), DateTimeKind.Utc)
                })
                .ToList(),
            BySeverity = rows
                .GroupBy(r => r.Severity)
                .OrderBy(g => g.Key)
                .Select(g => new ChatTurnLimitSeverityCountDto
                {
                    Severity = SeverityName(g.Key),
                    Hits = g.Count()
                })
                .ToList()
        };
    }

    public async Task<ChatTurnLimitPageDto> GetEventsAsync(
        DateTime? startDate, DateTime? endDate, string? keys, string? codes, string? severities, string? search,
        int skip, int take, CancellationToken ct = default)
    {
        var query = Filter(startDate, endDate, keys);

        var codeList = SplitList(codes);
        if (codeList.Count > 0)
        {
            query = query.Where(e => codeList.Contains(e.Code));
        }

        var severityList = SplitList(severities);
        if (severityList.Count > 0)
        {
            var values = severityList.Select(s => s == ChatTurnNoticeCatalog.SeverityError ? (byte)1 : (byte)0).Distinct().ToList();
            query = query.Where(e => values.Contains(e.Severity));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(e => e.AspNetUser != null && e.AspNetUser.UserName != null && e.AspNetUser.UserName.Contains(term));
        }

        int totalCount = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(e => e.OccurredUtc).ThenByDescending(e => e.Id)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, MaxTake))
            .Select(e => new { Event = e, UserName = e.AspNetUser != null ? e.AspNetUser.UserName : null })
            .AsNoTracking()
            .ToListAsync(ct);

        return new ChatTurnLimitPageDto
        {
            TotalCount = totalCount,
            Items = rows.Select(r =>
            {
                var item = new ChatTurnLimitEventDto();
                Fill(item, r.Event, r.UserName);
                return item;
            }).ToList()
        };
    }

    /// <summary>All facts of one event and whether its prompt can be read; null when there is no such event.</summary>
    public async Task<ChatTurnLimitEventDetailDto?> GetEventAsync(long id, CancellationToken ct = default)
    {
        var row = await _db.ChatTurnLimitEvents
            .Where(e => e.Id == id)
            .Select(e => new { Event = e, UserName = e.AspNetUser != null ? e.AspNetUser.UserName : null })
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);
        if (row == null) return null;

        var e = row.Event;
        var detail = new ChatTurnLimitEventDetailDto
        {
            ThinkingLevel = e.ThinkingLevel,
            IterationsUsed = e.IterationsUsed,
            ModelCallCount = e.ModelCallCount,
            ToolCallsBlocked = e.ToolCallsBlocked,
            ToolTimeMs = e.ToolTimeMs,
            PermitWaitMs = e.PermitWaitMs,
            BackoffWaitMs = e.BackoffWaitMs,
            RetryAttemptCount = e.RetryAttemptCount,
            TotalPromptTokens = e.TotalPromptTokens,
            OutputTokens = e.OutputTokens,
            EstimatedCost = e.EstimatedCost,
            ProviderFinishReason = e.ProviderFinishReason,
            ProviderErrorCode = e.ProviderErrorCode,
            HttpStatus = e.HttpStatus,
            AssistantPhase = e.AssistantPhase,
            ChatSessionId = e.ChatSessionId,
            PromptState = await PromptStateAsync(e, ct)
        };
        Fill(detail, e, row.UserName);
        return detail;
    }

    /// <summary>
    /// The turn's prompt, journaled. A confidential session is refused without decrypting anything, and
    /// a read the journal could not record returns no text, because the privacy notice promises each read is recorded.
    /// </summary>
    public async Task<(PromptReadStatus Status, ChatTurnLimitPromptDto? Prompt)> ReadPromptAsync(
        long id, string? adminUserId, string? adminUserName, string? ipAddress, CancellationToken ct = default)
    {
        var limitEvent = await _db.ChatTurnLimitEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (limitEvent == null) return (PromptReadStatus.NotFound, null);
        if (limitEvent.WasConfidential) return (PromptReadStatus.Confidential, null);
        if (limitEvent.UserMessageId == null || limitEvent.ChatSessionId == null) return (PromptReadStatus.NotFound, null);

        var message = await _db.ChatMessage.AsNoTracking()
            .Where(m => m.Id == limitEvent.UserMessageId.Value && m.ChatSessionId == limitEvent.ChatSessionId.Value)
            .Select(m => new { m.Content, m.ChatSession!.IsConfidential })
            .FirstOrDefaultAsync(ct);
        if (message == null) return (PromptReadStatus.NotFound, null);
        if (message.IsConfidential) return (PromptReadStatus.Confidential, null);

        bool journaled = await ChatAccessAudit.RecordAsync(
            _db,
            ChatAccessAction.AdminReadLimitPrompt,
            adminUserId,
            actorUserName: adminUserName,
            actorWasAdmin: true,
            subjectUserId: limitEvent.AspNetUserId,
            chatSessionId: limitEvent.ChatSessionId,
            sessionRef: limitEvent.ChatSessionId.Value.ToString(),
            ipAddress: ipAddress,
            detail: $"limit event {limitEvent.Id}",
            cancellationToken: ct);
        if (!journaled) return (PromptReadStatus.JournalFailed, null);

        string text = message.Content ?? string.Empty;
        bool truncated = text.Length > MaxPromptChars;
        return (PromptReadStatus.Ok, new ChatTurnLimitPromptDto
        {
            Text = truncated ? text.Substring(0, MaxPromptChars) : text,
            Truncated = truncated
        });
    }

    private async Task<string> PromptStateAsync(ChatTurnLimitEvent e, CancellationToken ct)
    {
        if (e.WasEphemeral) return PromptIncognito;
        if (e.WasConfidential) return PromptConfidential;
        if (e.UserMessageId == null) return PromptDeleted;

        bool exists = await _db.ChatMessage.AnyAsync(m => m.Id == e.UserMessageId.Value, ct);
        return exists ? PromptAvailable : PromptDeleted;
    }

    private IQueryable<ChatTurnLimitEvent> Filter(DateTime? startDate, DateTime? endDate, string? keys)
    {
        IQueryable<ChatTurnLimitEvent> query = _db.ChatTurnLimitEvents;
        if (startDate.HasValue) query = query.Where(e => e.OccurredUtc >= startDate.Value);
        if (endDate.HasValue) query = query.Where(e => e.OccurredUtc <= endDate.Value);

        return (keys ?? "system").Trim().ToLowerInvariant() switch
        {
            "both" => query,
            "user" => query.Where(e => e.KeySource == AiKeySource.UserKey),
            _ => query.Where(e => e.KeySource == AiKeySource.System)
        };
    }

    private static List<string> SplitList(string? value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string SeverityName(byte severity) =>
        severity == 1 ? ChatTurnNoticeCatalog.SeverityError : ChatTurnNoticeCatalog.SeverityWarning;

    private static void Fill(ChatTurnLimitEventDto dto, ChatTurnLimitEvent e, string? userName)
    {
        dto.Id = e.Id;
        dto.OccurredUtc = DateTime.SpecifyKind(e.OccurredUtc, DateTimeKind.Utc);
        dto.Code = e.Code;
        dto.Severity = SeverityName(e.Severity);
        dto.UserId = e.AspNetUserId;
        dto.UserName = userName;
        dto.WasConfidential = e.WasConfidential;
        dto.WasEphemeral = e.WasEphemeral;
        dto.KeySource = e.KeySource == AiKeySource.UserKey ? "user" : "system";
        dto.Provider = e.Provider;
        dto.RequestedModelId = e.RequestedModelId;
        dto.ModelDisplayName = e.ModelDisplayName;
        dto.LimitValue = e.LimitValue;
        dto.ObservedValue = e.ObservedValue;
        dto.TotalDurationMs = e.TotalDurationMs;
        dto.ToolCallCount = e.ToolCallCount;
    }
}
