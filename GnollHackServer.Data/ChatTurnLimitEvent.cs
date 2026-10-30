namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// One limit or provider failure that ended or cut an Overseer chat turn. Holds identifiers, codes and
/// numbers and never content. An administrator reads the prompt on demand through the message id, which
/// is journaled. Deleted with the user's account; a purge clears the chat and message ids and keeps the
/// row. Pruned by ChatRetentionSettings.PruneChatTurnLimitEventDays.
/// </summary>
public class ChatTurnLimitEvent
{
    /// <summary>The reference shown to the user.</summary>
    public long Id { get; set; }

    public DateTime OccurredUtc { get; set; }

    [MaxLength(32)]
    public string Code { get; set; } = default!;

    /// <summary>0 warning: the turn has a visible answer; 1 error: it has none.</summary>
    public byte Severity { get; set; }

    [MaxLength(450)]
    public string? AspNetUserId { get; set; }
    public ApplicationUser? AspNetUser { get; set; }

    /// <summary>Not a foreign key. Null for an incognito turn and after a purge.</summary>
    public long? ChatSessionId { get; set; }

    /// <summary>Not a foreign key. Null for an incognito turn and after a purge.</summary>
    public long? UserMessageId { get; set; }

    /// <summary>Not a foreign key. Null for an incognito turn and after a purge.</summary>
    public long? AssistantMessageId { get; set; }

    public bool WasConfidential { get; set; }

    public bool WasEphemeral { get; set; }

    public AiKeySource KeySource { get; set; }

    /// <summary>Attribution only, not a foreign key; set on system rows.</summary>
    public long? SystemAiApiConfigurationId { get; set; }

    [MaxLength(32)]
    public string? Provider { get; set; }

    [MaxLength(128)]
    public string? RequestedModelId { get; set; }

    [MaxLength(256)]
    public string? ModelDisplayName { get; set; }

    [MaxLength(32)]
    public string? ThinkingLevel { get; set; }

    /// <summary>The limit that applied, for example 22 rounds.</summary>
    public int? LimitValue { get; set; }

    /// <summary>The figure the turn reached, for example 22 rounds.</summary>
    public int? ObservedValue { get; set; }

    public int IterationsUsed { get; set; }

    /// <summary>Includes sub-agent calls and refused budget increments.</summary>
    public int ModelCallCount { get; set; }

    /// <summary>Includes sub-agent tool calls.</summary>
    public int ToolCallCount { get; set; }

    public int ToolCallsBlocked { get; set; }

    public int? TotalDurationMs { get; set; }

    public long ToolTimeMs { get; set; }

    public long PermitWaitMs { get; set; }

    public long BackoffWaitMs { get; set; }

    public int RetryAttemptCount { get; set; }

    public int TotalPromptTokens { get; set; }

    public int OutputTokens { get; set; }

    public decimal? EstimatedCost { get; set; }

    [MaxLength(64)]
    public string? ProviderFinishReason { get; set; }

    /// <summary>Restricted to [A-Za-z0-9_.-].</summary>
    [MaxLength(64)]
    public string? ProviderErrorCode { get; set; }

    public int? HttpStatus { get; set; }

    /// <summary>The phase label of the turn's final assistant message, when the provider sends one.</summary>
    [MaxLength(16)]
    public string? AssistantPhase { get; set; }
}
