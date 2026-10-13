namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>Who pays for an AI call: the operator's system configuration (or server key), or the user's own key.</summary>
public enum AiKeySource : byte { System = 0, UserKey = 1 }

/// <summary>
/// One provider attempt of an Overseer chat or title-generation model call: one HTTP request, or one
/// governor refusal before a request was sent. Holds no message text, headers or secrets; a user id only
/// on user-key rows. Pruned by ChatRetentionSettings.PruneChatProviderAttemptDays.
/// </summary>
public class ChatProviderAttempt
{
    public long Id { get; set; }

    public DateTime StartedAtUtc { get; set; }

    /// <summary>One id per agent run or title request; random, linked to nothing.</summary>
    public Guid AgentRunId { get; set; }

    /// <summary>The usage-log RoleContext: 1 chat (turns and sub-agents), 2 title generation.</summary>
    public int RoleContext { get; set; }

    /// <summary>0 for a turn's own loop or a title; 1 and up for a sub-agent.</summary>
    public byte AgentDepth { get; set; }

    public AiKeySource KeySource { get; set; }

    /// <summary>Attribution only, not a foreign key; set on system rows.</summary>
    public long? SystemAiApiConfigurationId { get; set; }

    /// <summary>Set on user-key rows only.</summary>
    [MaxLength(450)]
    public string? AspNetUserId { get; set; }
    public ApplicationUser? AspNetUser { get; set; }

    [MaxLength(32)]
    public string Provider { get; set; } = default!;

    [MaxLength(128)]
    public string RequestedModelId { get; set; } = default!;

    [MaxLength(32)]
    public string ThinkingLevel { get; set; } = string.Empty;

    public BenchmarkEndpointKind EndpointKind { get; set; }

    public int CallIndex { get; set; }

    public byte AttemptIndex { get; set; }

    public int DurationMs { get; set; }

    public ProviderAttemptOutcome Outcome { get; set; }

    public short? HttpStatus { get; set; }

    /// <summary>Restricted to [A-Za-z0-9_.-].</summary>
    [MaxLength(64)]
    public string? ProviderErrorCode { get; set; }

    /// <summary>The call went on to another attempt after this one.</summary>
    public bool Retried { get; set; }

    public int? RetryAfterMs { get; set; }
}
