namespace MobileGnollHackLogger.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class SystemAiApiConfiguration : IRateLimitedEntity
{
    public long Id { get; set; }

    [MaxLength(256)]
    public string DisplayName { get; set; } = default!;

    [MaxLength(64)]
    public string Provider { get; set; } = default!;

    [MaxLength(128)]
    public string ModelId { get; set; } = default!;

    [MaxLength(32)]
    public string? ThinkingLevel { get; set; }

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

    public int ModelRole { get; set; } = 3; // 1 = Chat, 2 = Title Generation, 3 = Both

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Rate limits
    public int? MaxDailyRequests { get; set; }
    public int? MaxMonthlyRequests { get; set; }
    public int? MaxTotalRequests { get; set; }

    // Usage tracking
    public int DailyRequestsCount { get; set; }
    public int MonthlyRequestsCount { get; set; }
    public int TotalRequestsCount { get; set; }

    public DateTime? LastDailyReset { get; set; }
    public DateTime? LastMonthlyReset { get; set; }

    public DateTime? LastBudgetNotificationSentUtc { get; set; }
    public bool IsBudgetExhausted { get; set; }

    public ICollection<UserSystemAiApiConfiguration>? UserAssignments { get; set; }
    public ICollection<GroupSystemAiApiConfiguration>? GroupAssignments { get; set; }
    public ICollection<SystemAiUsageLog>? UsageLogs { get; set; }
    public ICollection<SystemAiErrorLog>? ErrorLogs { get; set; }
}
