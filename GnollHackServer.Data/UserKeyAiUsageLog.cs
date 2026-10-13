namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// One recorded call, turn or title made on a user's own API key: who, which model, tokens, duration and
/// estimated cost. Holds no session, message or content. Deleted with the user.
/// </summary>
public class UserKeyAiUsageLog
{
    public long Id { get; set; }

    [MaxLength(450)]
    public string AspNetUserId { get; set; } = default!;
    public ApplicationUser AspNetUser { get; set; } = default!;

    /// <summary>Attribution only, not a foreign key.</summary>
    public long? UserAiModelId { get; set; }

    public DateTime TimestampUtc { get; set; }

    [MaxLength(64)]
    public string Provider { get; set; } = default!;

    [MaxLength(128)]
    public string ModelId { get; set; } = default!;

    [MaxLength(256)]
    public string? ModelDisplayName { get; set; }

    /// <summary>As SystemAiUsageLog.ThinkingLevel.</summary>
    [MaxLength(32)]
    public string ThinkingLevel { get; set; } = string.Empty;

    /// <summary>Same meaning per role as SystemAiUsageLog.InputTokens (role 1: prompt minus cache reads; role 2: whole prompt).</summary>
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? CacheReadInputTokens { get; set; }
    public int? CacheCreationInputTokens { get; set; }
    public int? TotalDurationMs { get; set; }

    /// <summary>1 = Chat (turns and sub-agents), 2 = Title Generation.</summary>
    public int RoleContext { get; set; } = 1;

    public decimal? EstimatedCost { get; set; }

    [MaxLength(16)]
    public string? PricingSource { get; set; }
}
