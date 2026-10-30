namespace Overseer.Services.Telemetry;

using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;

/// <summary>What ties a turn's limit events to its user, chat and model.</summary>
public sealed record ChatTurnLimitLinks(
    string? UserId,
    long? ChatSessionId,
    long? UserMessageId,
    long? AssistantMessageId,
    bool WasConfidential,
    bool WasEphemeral,
    long? SystemModelId,
    string? Provider,
    string? RequestedModelId,
    string? ModelDisplayName,
    string? ThinkingLevel,
    decimal? EstimatedCost,
    string Severity);

/// <summary>
/// Saves a turn's limit events in a scope of its own, so a rejected row never poisons the caller's context.
/// Never throws.
/// </summary>
public static class ChatTurnLimitRecorder
{
    /// <summary>One row per hit of <paramref name="result"/>. Identifiers, codes and numbers only.</summary>
    public static List<ChatTurnLimitEvent> Map(AgentRunResult result, ChatTurnLimitLinks links)
    {
        var occurredUtc = DateTime.UtcNow;
        return result.LimitHits.Select(hit => new ChatTurnLimitEvent
        {
            OccurredUtc = occurredUtc,
            Code = hit.Code,
            Severity = links.Severity == ChatTurnNoticeCatalog.SeverityError ? (byte)1 : (byte)0,
            AspNetUserId = links.UserId,
            ChatSessionId = links.ChatSessionId,
            UserMessageId = links.UserMessageId,
            AssistantMessageId = links.AssistantMessageId,
            WasConfidential = links.WasConfidential,
            WasEphemeral = links.WasEphemeral,
            KeySource = links.SystemModelId.HasValue ? AiKeySource.System : AiKeySource.UserKey,
            SystemAiApiConfigurationId = links.SystemModelId,
            Provider = Bound(links.Provider, 32),
            RequestedModelId = Bound(links.RequestedModelId, 128),
            ModelDisplayName = Bound(links.ModelDisplayName, 256),
            ThinkingLevel = Bound(links.ThinkingLevel, 32),
            LimitValue = hit.LimitValue,
            ObservedValue = hit.ObservedValue,
            IterationsUsed = result.IterationsUsed,
            ModelCallCount = result.ModelCallCount,
            ToolCallCount = result.ToolCallCount,
            ToolCallsBlocked = result.ToolCallsBlocked,
            TotalDurationMs = result.TotalDurationMs,
            ToolTimeMs = result.ToolTimeMs,
            PermitWaitMs = result.PermitWaitMs,
            BackoffWaitMs = result.BackoffWaitMs,
            RetryAttemptCount = result.RetryAttemptCount,
            TotalPromptTokens = result.TotalPromptTokens,
            OutputTokens = result.OutputTokens,
            EstimatedCost = links.EstimatedCost,
            ProviderFinishReason = Bound(result.ProviderFinishReason, 64),
            ProviderErrorCode = SanitizeCode(hit.ProviderErrorCode),
            HttpStatus = hit.HttpStatus,
            AssistantPhase = Bound(result.LastAssistantPhase, 16)
        }).ToList();
    }

    /// <summary>Saves the rows and returns the id of the row of <paramref name="primaryCode"/>, the reference the user sees.</summary>
    public static async Task<long?> TryRecordAsync(
        IServiceScopeFactory scopes, ILogger logger, IReadOnlyList<ChatTurnLimitEvent> rows, string? primaryCode)
    {
        if (rows == null || rows.Count == 0) return null;

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChatTurnLimitEvents.AddRange(rows);
            await db.SaveChangesAsync(CancellationToken.None);
            return rows.FirstOrDefault(r => r.Code == primaryCode)?.Id;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record {Count} chat turn limit event(s).", rows.Count);
            return null;
        }
    }

    /// <summary>Restricted to [A-Za-z0-9_.-], as on <see cref="ChatProviderAttempt.ProviderErrorCode"/>.</summary>
    public static string? SanitizeCode(string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        var sanitized = Regex.Replace(code, "[^A-Za-z0-9_.-]", string.Empty);
        return sanitized.Length == 0 ? null : Bound(sanitized, 64);
    }

    private static string? Bound(string? value, int max) =>
        value != null && value.Length > max ? value.Substring(0, max) : value;
}
