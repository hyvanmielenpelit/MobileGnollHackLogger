namespace MobileGnollHackLogger.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class SystemAiApiConfiguration : IRateLimitedEntity
{
    public long Id { get; set; }

    [MaxLength(256)]
    public string DisplayName { get; set; } = default!;

    [MaxLength(32)]
    public string? DisplayNameMode { get; set; }  // "model_name" | "model_id" | "custom"; null = legacy row

    [MaxLength(64)]
    public string Provider { get; set; } = default!;

    [MaxLength(128)]
    public string ModelId { get; set; } = default!;

    [MaxLength(32)]
    public string? ThinkingLevel { get; set; }

    [MaxLength(32)]
    public string? ReasoningMode { get; set; }

    [MaxLength(32)]
    public string? ReasoningSummary { get; set; }

    [MaxLength(64)]
    public string? ServiceTier { get; set; }

    public int? MaxInputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }

    public int OrderIndex { get; set; }
    public bool IsEnabled { get; set; }

    [MaxLength(2048)]
    public string? EncryptedApiKey { get; set; }

    [MaxLength(32)]
    public string? ApiKeyNonce { get; set; }

    [MaxLength(32)]
    public string? ApiKeyTag { get; set; }

    public bool IsSystemWide { get; set; }

    public ParallelExecutionMode ParallelExecutionMode { get; set; } = ParallelExecutionMode.Enabled;

    public int ModelRole { get; set; } = 3; // 1 = Chat, 2 = Title Generation, 3 = Both

    [MaxLength(2048)]
    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Rate limits
    public int? MaxDailyChatRequests { get; set; }
    public int? MaxMonthlyChatRequests { get; set; }
    public int? MaxTotalChatRequests { get; set; }

    public int? MaxDailyTitleRequests { get; set; }
    public int? MaxMonthlyTitleRequests { get; set; }
    public int? MaxTotalTitleRequests { get; set; }

    public long? MaxDailyChatTokens { get; set; }
    public long? MaxMonthlyChatTokens { get; set; }
    public long? MaxTotalChatTokens { get; set; }

    public long? MaxDailyTitleTokens { get; set; }
    public long? MaxMonthlyTitleTokens { get; set; }
    public long? MaxTotalTitleTokens { get; set; }

    // Usage tracking
    public int DailyChatRequestsCount { get; set; }
    public int MonthlyChatRequestsCount { get; set; }
    public int TotalChatRequestsCount { get; set; }

    public int DailyTitleRequestsCount { get; set; }
    public int MonthlyTitleRequestsCount { get; set; }
    public int TotalTitleRequestsCount { get; set; }

    public long DailyChatTokensCount { get; set; }
    public long MonthlyChatTokensCount { get; set; }
    public long TotalChatTokensCount { get; set; }

    public long DailyTitleTokensCount { get; set; }
    public long MonthlyTitleTokensCount { get; set; }
    public long TotalTitleTokensCount { get; set; }

    public DateTime? LastDailyReset { get; set; }
    public DateTime? LastMonthlyReset { get; set; }

    public DateTime? LastBudgetNotificationSentUtc { get; set; }
    public bool IsBudgetExhausted { get; set; }

    public ICollection<UserSystemAiApiConfiguration>? UserAssignments { get; set; }
    public ICollection<GroupSystemAiApiConfiguration>? GroupAssignments { get; set; }
    public ICollection<SystemAiUsageLog>? UsageLogs { get; set; }
    public ICollection<SystemAiErrorLog>? ErrorLogs { get; set; }
}
