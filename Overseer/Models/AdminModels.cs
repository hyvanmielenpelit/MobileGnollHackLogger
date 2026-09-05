namespace Overseer.Models;

using System;
using System.Collections.Generic;

public class AdminGroupDto
{
    public long Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public int UserCount { get; set; }
}

public class CreateGroupRequest
{
    public string DisplayName { get; set; } = string.Empty;
}

public class AdminUserDto
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<AdminGroupDto> Groups { get; set; } = new List<AdminGroupDto>();
}

public class AssignGroupRequest
{
    public long GroupId { get; set; }
}

public class SystemAiApiConfigurationDto
{
    public long Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameMode { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? ThinkingLevel { get; set; }
    public string? ReasoningMode { get; set; }
    public string? ReasoningSummary { get; set; }
    public string? ServiceTier { get; set; }
    public int? MaxInputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
    public int OrderIndex { get; set; }
    public bool IsEnabled { get; set; }
    public bool HasApiKey { get; set; }
    public bool IsSystemWide { get; set; }
    public int? MaxDailyChatRequests { get; set; }
    public int? MaxMonthlyChatRequests { get; set; }
    public int? MaxTotalChatRequests { get; set; }
    public int DailyChatRequestsCount { get; set; }
    public int MonthlyChatRequestsCount { get; set; }
    public int TotalChatRequestsCount { get; set; }
    public int? MaxDailyTitleRequests { get; set; }
    public int? MaxMonthlyTitleRequests { get; set; }
    public int? MaxTotalTitleRequests { get; set; }
    public int DailyTitleRequestsCount { get; set; }
    public int MonthlyTitleRequestsCount { get; set; }
    public int TotalTitleRequestsCount { get; set; }
    public long? MaxDailyChatTokens { get; set; }
    public long? MaxMonthlyChatTokens { get; set; }
    public long? MaxTotalChatTokens { get; set; }
    public long DailyChatTokensCount { get; set; }
    public long MonthlyChatTokensCount { get; set; }
    public long TotalChatTokensCount { get; set; }
    public long? MaxDailyTitleTokens { get; set; }
    public long? MaxMonthlyTitleTokens { get; set; }
    public long? MaxTotalTitleTokens { get; set; }
    public long DailyTitleTokensCount { get; set; }
    public long MonthlyTitleTokensCount { get; set; }
    public long TotalTitleTokensCount { get; set; }
    public int ModelRole { get; set; }
    public int ParallelExecutionMode { get; set; } = 2;
    public string? Note { get; set; }
    public int UserAssignmentCount { get; set; }
    public int GroupAssignmentCount { get; set; }

    public string? PricingMode { get; set; }
    public decimal? InputPricePerMillion { get; set; }
    public decimal? OutputPricePerMillion { get; set; }
    public decimal? CachedInputPricePerMillion { get; set; }

    public decimal? EffectiveInputPricePerMillion { get; set; }
    public decimal? EffectiveOutputPricePerMillion { get; set; }
    public decimal? EffectiveCachedInputPricePerMillion { get; set; }

    /// <summary>"custom", "catalog", or "unknown" when no price resolves at all.</summary>
    public string PricingSource { get; set; } = "unknown";
    public string? PricingCurrency { get; set; }
    public string? PricingAsOf { get; set; }
}

public class CreateSystemAiApiConfigurationRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameMode { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? ThinkingLevel { get; set; }
    public string? ReasoningMode { get; set; }
    public string? ReasoningSummary { get; set; }
    public string? ServiceTier { get; set; }
    public int? MaxInputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
    public bool IsEnabled { get; set; }
    public string? ApiKey { get; set; }
    public bool IsSystemWide { get; set; }
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
    public int ModelRole { get; set; } = 3;
    public int ParallelExecutionMode { get; set; } = 2;
    public string? Note { get; set; }
    public string? PricingMode { get; set; }
    public decimal? InputPricePerMillion { get; set; }
    public decimal? OutputPricePerMillion { get; set; }
    public decimal? CachedInputPricePerMillion { get; set; }
}

public class UpdateSystemAiApiConfigurationRequest : CreateSystemAiApiConfigurationRequest
{
    // ApiKey is optional. If null, don't update it. If empty string, clear it.
}

public class ReorderRequest
{
    public long[] OrderedIds { get; set; } = Array.Empty<long>();
}

public class UserSystemAiConfigDto
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public long SystemAiApiConfigurationId { get; set; }
    public bool IsEnabled { get; set; }
    public int? OrderIndex { get; set; }
    public int? MaxDailyChatRequests { get; set; }
    public int? MaxMonthlyChatRequests { get; set; }
    public int? MaxTotalChatRequests { get; set; }
    public int DailyChatRequestsCount { get; set; }
    public int MonthlyChatRequestsCount { get; set; }
    public int TotalChatRequestsCount { get; set; }
    public int? MaxDailyTitleRequests { get; set; }
    public int? MaxMonthlyTitleRequests { get; set; }
    public int? MaxTotalTitleRequests { get; set; }
    public int DailyTitleRequestsCount { get; set; }
    public int MonthlyTitleRequestsCount { get; set; }
    public int TotalTitleRequestsCount { get; set; }
    public long? MaxDailyChatTokens { get; set; }
    public long? MaxMonthlyChatTokens { get; set; }
    public long? MaxTotalChatTokens { get; set; }
    public long DailyChatTokensCount { get; set; }
    public long MonthlyChatTokensCount { get; set; }
    public long TotalChatTokensCount { get; set; }
    public long? MaxDailyTitleTokens { get; set; }
    public long? MaxMonthlyTitleTokens { get; set; }
    public long? MaxTotalTitleTokens { get; set; }
    public long DailyTitleTokensCount { get; set; }
    public long MonthlyTitleTokensCount { get; set; }
    public long TotalTitleTokensCount { get; set; }
    public int ModelRole { get; set; }
}

public class GroupSystemAiConfigDto
{
    public long Id { get; set; }
    public long GroupId { get; set; }
    public long SystemAiApiConfigurationId { get; set; }
    public bool IsEnabled { get; set; }
    public int OrderIndex { get; set; }
    public int? MaxDailyChatRequests { get; set; }
    public int? MaxMonthlyChatRequests { get; set; }
    public int? MaxTotalChatRequests { get; set; }
    public int DailyChatRequestsCount { get; set; }
    public int MonthlyChatRequestsCount { get; set; }
    public int TotalChatRequestsCount { get; set; }
    public int? MaxDailyTitleRequests { get; set; }
    public int? MaxMonthlyTitleRequests { get; set; }
    public int? MaxTotalTitleRequests { get; set; }
    public int DailyTitleRequestsCount { get; set; }
    public int MonthlyTitleRequestsCount { get; set; }
    public int TotalTitleRequestsCount { get; set; }
    public long? MaxDailyChatTokens { get; set; }
    public long? MaxMonthlyChatTokens { get; set; }
    public long? MaxTotalChatTokens { get; set; }
    public long DailyChatTokensCount { get; set; }
    public long MonthlyChatTokensCount { get; set; }
    public long TotalChatTokensCount { get; set; }
    public long? MaxDailyTitleTokens { get; set; }
    public long? MaxMonthlyTitleTokens { get; set; }
    public long? MaxTotalTitleTokens { get; set; }
    public long DailyTitleTokensCount { get; set; }
    public long MonthlyTitleTokensCount { get; set; }
    public long TotalTitleTokensCount { get; set; }
    public int ModelRole { get; set; }
}

public class AssignConfigToUserRequest
{
    public long SystemAiApiConfigurationId { get; set; }
    public bool IsEnabled { get; set; }
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
    public int ModelRole { get; set; } = 3;
}

public class AssignConfigToGroupRequest : AssignConfigToUserRequest
{
}

public class UpdateUserSystemAiConfigRequest : AssignConfigToUserRequest
{
}

public class UpdateGroupSystemAiConfigRequest : AssignConfigToGroupRequest
{
}

public class SystemAiErrorLogDto
{
    public long Id { get; set; }
    public long SystemAiApiConfigurationId { get; set; }
    public string ConfigurationName { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public int? HttpStatusCode { get; set; }
    public DateTime TimestampUtc { get; set; }
}

public class ResetCounterRequest
{
    public string? CounterName { get; set; }
}

public class AnalyticsUserRow
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public int ChatRequests { get; set; }
    public int TitleRequests { get; set; }
    public int BenchmarkRequests { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheCreationTokens { get; set; }
    public int AvgDurationMs { get; set; }
}

public class AnalyticsResponse
{
    public List<AnalyticsUserRow> Rows { get; set; } = new();
    public int TotalCount { get; set; }
}

public class AiTelemetrySummaryDto
{
    public long TotalRequests { get; set; }
    public long TotalChatRequests { get; set; }
    public long TotalTitleRequests { get; set; }
    public long TotalBenchmarkRequests { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalCacheReadTokens { get; set; }
    public long TotalCacheCreationTokens { get; set; }
    public double CacheHitRatio { get; set; }
    public int AvgDurationMs { get; set; }
    public List<AiModelUsageBreakdownDto> Models { get; set; } = new();
}

public class AiModelUsageBreakdownDto
{
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public long Requests { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheCreationTokens { get; set; }
    public double CacheHitRatio { get; set; }
    public int AvgDurationMs { get; set; }
}

public class AiGovernorStatusDto
{
    public int MaxConcurrentCalls { get; set; }
    public int MaxRetryAfterSeconds { get; set; }
    public List<AiGovernorKeyStatusDto> ActiveKeys { get; set; } = new();
}

public class AiGovernorKeyStatusDto
{
    public string CredentialKey { get; set; } = string.Empty;
    public bool IsRateLimited { get; set; }
    public double RemainingCooldownSeconds { get; set; }
}

public class ResetCooldownRequest
{
    public string? CredentialKey { get; set; }
}
