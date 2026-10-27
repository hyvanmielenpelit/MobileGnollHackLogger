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
    /// <summary>The catalog release date (yyyy-MM-dd) of the model; null when the catalog does not describe it.</summary>
    public string? ReleaseDate { get; set; }
    public string? ThinkingLevel { get; set; }
    public string? ReasoningMode { get; set; }
    public string? ReasoningSummary { get; set; }
    public string? ServiceTier { get; set; }
    public int? MaxInputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
    public int OrderIndex { get; set; }
    public bool IsEnabled { get; set; }
    public bool HasApiKey { get; set; }

    /// <summary>The config's key columns hold a copy of the provider's default key.</summary>
    public bool UseDefaultApiKey { get; set; }
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

    /// <summary>A ProviderConfidentialityPosture name, or null for a row where nothing is established.</summary>
    public string? ConfidentialityPosture { get; set; }
    public string? ConfidentialityNote { get; set; }
    public string? PostureAgreementRef { get; set; }

    /// <summary>
    /// Non-null is what makes the posture operator-verified, and only a verified posture can
    /// produce a green privacy badge.
    /// </summary>
    public DateTime? PostureVerifiedUtc { get; set; }
    public string? DataRegion { get; set; }

    /// <summary>Custom endpoint. Null or empty means the provider's official public endpoint.</summary>
    public string? BaseUrl { get; set; }
    public string? CustomHeadersJson { get; set; }
    public string? ApiVersion { get; set; }

    public decimal? EffectiveInputPricePerMillion { get; set; }
    public decimal? EffectiveOutputPricePerMillion { get; set; }
    public decimal? EffectiveCachedInputPricePerMillion { get; set; }

    /// <summary>"custom", "catalog", or "unknown" when no price resolves at all.</summary>
    public string PricingSource { get; set; } = "unknown";
    public string? PricingAsOf { get; set; }

    /// <summary>The model's long-prompt rate card, or null for a flat-rate model — which is every
    /// Anthropic model, every Gemini Flash model, and every custom price override.</summary>
    public int? EffectiveLongContextThresholdTokens { get; set; }
    public decimal? EffectiveLongContextInputPricePerMillion { get; set; }
    public decimal? EffectiveLongContextOutputPricePerMillion { get; set; }

    /// <summary>Price multipliers keyed by the provider's served service tier. A tier not listed —
    /// including "default" and "standard" — costs 1.0.</summary>
    public IReadOnlyDictionary<string, decimal>? EffectiveServiceTierMultipliers { get; set; }

    /// <summary>An already-announced future price change, or null. <see cref="PricingScheduleElapsed"/>
    /// says the date has passed and the rates above are already the scheduled ones — not an error, but a
    /// sign the catalog entry should be folded down and re-verified.</summary>
    public string? PricingScheduledChangeFrom { get; set; }
    public decimal? PricingScheduledChangeInputPricePerMillion { get; set; }
    public decimal? PricingScheduledChangeOutputPricePerMillion { get; set; }
    public string? PricingScheduledChangeNote { get; set; }
    public bool PricingScheduleElapsed { get; set; }

    /// <summary>"catalog" or "custom"; a legacy null row reads as "catalog".</summary>
    public string ModelCatalogMode { get; set; } = ModelCatalogModes.Catalog;

    /// <summary>Whether the configuration still names a model the catalog offers.</summary>
    public ModelAvailabilityDto? ModelAvailability { get; set; }
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

    /// <summary>Use the provider's default key instead of <see cref="ApiKey"/>, which is then ignored.</summary>
    public bool UseDefaultApiKey { get; set; }
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

    /// <summary>A ProviderConfidentialityPosture name, or null. An unrecognised name is refused.</summary>
    public string? ConfidentialityPosture { get; set; }
    public string? ConfidentialityNote { get; set; }
    public string? PostureAgreementRef { get; set; }

    /// <summary>Setting this is what marks the posture operator-verified. Clearing it withdraws that.</summary>
    public DateTime? PostureVerifiedUtc { get; set; }
    public string? DataRegion { get; set; }

    /// <summary>
    /// Custom endpoint. Validated by <c>EndpointPolicy</c> before it is stored — an
    /// unallowlisted host, a non-https scheme or a denied header is a 400, never a stored value
    /// that is quietly ignored.
    /// </summary>
    public string? BaseUrl { get; set; }
    public string? CustomHeadersJson { get; set; }
    public string? ApiVersion { get; set; }
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

/// <summary>The AI Telemetry tab's figures for a date range. Token figures are normalized: see AiUsageRoles.</summary>
public class AiTelemetrySummaryDto
{
    /// <summary>The key-source filter applied: system | user | both.</summary>
    public string KeySource { get; set; } = "system";
    public long TotalRequests { get; set; }
    public long TotalChatRequests { get; set; }
    public long TotalTitleRequests { get; set; }
    public long TotalBenchmarkRequests { get; set; }
    public long TotalPromptTokens { get; set; }
    public long TotalUncachedInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalCacheReadTokens { get; set; }
    public long TotalCacheCreationTokens { get; set; }

    /// <summary>Cache read ÷ prompt.</summary>
    public double CacheHitRatio { get; set; }
    public int AvgDurationMs { get; set; }

    /// <summary>Over priced rows only.</summary>
    public decimal TotalEstimatedCost { get; set; }
    public long PricedRequests { get; set; }

    /// <summary>The earliest priced row in the selected logs.</summary>
    public DateTime? CostRecordedSinceUtc { get; set; }
    public List<AiUsageBreakdownRowDto> Rows { get; set; } = new();
    public ChatProviderAvailabilityDto ChatAvailability { get; set; } = new();

    /// <summary>Independent of the key-source filter.</summary>
    public OverseerActivityDto Activity { get; set; } = new();
}

/// <summary>One breakdown row: a key source, user, model, role context and thinking level.</summary>
public class AiUsageBreakdownRowDto
{
    /// <summary>system | user.</summary>
    public string KeySource { get; set; } = "system";

    /// <summary>User-key rows only.</summary>
    public string? UserId { get; set; }

    /// <summary>The user's current name; user-key rows only.</summary>
    public string? UserName { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;

    /// <summary>The catalog name, else the retired entry's, else null.</summary>
    public string? ModelName { get; set; }
    public int RoleContext { get; set; }

    /// <summary>chat | title | benchmark | other.</summary>
    public string RoleGroup { get; set; } = string.Empty;

    /// <summary>"" when none was sent, null when not recorded.</summary>
    public string? ThinkingLevel { get; set; }
    public long Requests { get; set; }
    public long PromptTokens { get; set; }
    public long UncachedInputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheCreationTokens { get; set; }
    public long OutputTokens { get; set; }
    public long DurationSumMs { get; set; }
    public long DurationCount { get; set; }
    public decimal EstimatedCost { get; set; }
    public long PricedRequests { get; set; }
    public int ValidAttempts { get; set; }
    public int ProviderErrors { get; set; }
    public int UnattributedErrors { get; set; }
    public int OurSideFailures { get; set; }
}

/// <summary>The users of one model in one key source and role group, over a date range.</summary>
public class AiTelemetryModelUsersDto
{
    /// <summary>system | user.</summary>
    public string KeySource { get; set; } = "system";
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;

    /// <summary>The catalog name, else the retired entry's, else null.</summary>
    public string? ModelName { get; set; }

    /// <summary>The role groups included: chat | title | benchmark | other.</summary>
    public List<string> Roles { get; set; } = new();
    public DateTime? CostRecordedSinceUtc { get; set; }
    public List<AiTelemetryModelUserRowDto> Rows { get; set; } = new();
}

/// <summary>One user's usage of the model in one role context and thinking level.</summary>
public class AiTelemetryModelUserRowDto
{
    public string UserId { get; set; } = string.Empty;

    /// <summary>The user's current name; null when the account is gone.</summary>
    public string? UserName { get; set; }
    public int RoleContext { get; set; }
    public string RoleGroup { get; set; } = string.Empty;

    /// <summary>"" when none was sent, null when not recorded.</summary>
    public string? ThinkingLevel { get; set; }
    public long Requests { get; set; }
    public long PromptTokens { get; set; }
    public long UncachedInputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheCreationTokens { get; set; }
    public long OutputTokens { get; set; }
    public long DurationSumMs { get; set; }
    public long DurationCount { get; set; }
    public decimal EstimatedCost { get; set; }
    public long PricedRequests { get; set; }
    public DateTime? LastRequestUtc { get; set; }
}

/// <summary>
/// Provider availability of the Overseer chat and title generation, from the provider-attempt ledger, with the
/// same outcome classes, denominator and 10-minute cluster interval as GnollBench.
/// </summary>
public class ChatProviderAvailabilityDto
{
    /// <summary>The ledger has any row at all.</summary>
    public bool Recorded { get; set; }
    public DateTime? RecordedSinceUtc { get; set; }
    public int ValidAttempts { get; set; }
    public int ProviderErrors { get; set; }
    public int UnattributedErrors { get; set; }
    public int OurSideFailures { get; set; }

    /// <summary>Provider errors ÷ valid attempts; null at no valid attempt.</summary>
    public double? ProviderErrorRate { get; set; }

    /// <summary>The cluster-adjusted Wilson 95% interval of the rate.</summary>
    public double? Low { get; set; }
    public double? High { get; set; }

    /// <summary>(Provider + unattributed) ÷ valid attempts; null at no valid attempt.</summary>
    public double? InclusiveRate { get; set; }
    public int Overloaded { get; set; }
    public int ServerErrors { get; set; }
    public int Timeouts { get; set; }
    public int StreamFailures { get; set; }
    public int RateLimitedUnattributed { get; set; }
    public int TransportUnattributed { get; set; }

    /// <summary>Model calls: attempt 0 rows.</summary>
    public int Calls { get; set; }
    public int FirstAttemptSuccessCalls { get; set; }

    /// <summary>Calls whose final attempt failed.</summary>
    public int FailedCalls { get; set; }

    /// <summary>Chat turns: distinct agent runs at depth 0.</summary>
    public int ChatTurns { get; set; }
    public int ChatTurnsEndedByProviderError { get; set; }
    public int TitleValidAttempts { get; set; }
    public int TitleProviderErrors { get; set; }

    /// <summary>Title calls.</summary>
    public int TitleRequests { get; set; }

    /// <summary>Title calls whose final attempt failed.</summary>
    public int TitleRequestsFailed { get; set; }
}

/// <summary>How many users use Overseer and how many chats and messages they create.</summary>
public class OverseerActivityDto
{
    /// <summary>Distinct users with a chat or a visible message in the range.</summary>
    public int ActiveUsers { get; set; }

    /// <summary>Active users whose first activity day ever falls in the range.</summary>
    public int NewUsers { get; set; }
    public int ChatsCreated { get; set; }
    public int ConfidentialChatsCreated { get; set; }
    public int GnollHackChatsCreated { get; set; }
    public int UserMessages { get; set; }
    public int AssistantMessages { get; set; }

    /// <summary>From the anonymous rows.</summary>
    public int IncognitoChatsCreated { get; set; }
    public int IncognitoUserMessages { get; set; }

    /// <summary>The earliest activity row (the deployment day).</summary>
    public DateOnly? CountedSinceUtc { get; set; }
}

/// <summary>One page of per-user usage of the selected key sources.</summary>
public class AiTelemetryUsersPageDto
{
    /// <summary>system | user | both.</summary>
    public string KeySource { get; set; } = "system";
    public List<AiTelemetryUserRowDto> Users { get; set; } = new();

    /// <summary>Users with usage of the selected key sources in the range (matching the search).</summary>
    public int TotalUsers { get; set; }

    /// <summary>The security event log exists, so the refusal counts are meaningful.</summary>
    public bool SecurityEventsAvailable { get; set; }
}

public class AiTelemetryUserRowDto
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;

    /// <summary>All rows of the selected key sources.</summary>
    public long Requests { get; set; }

    /// <summary>The system-configuration share of <see cref="Requests"/>.</summary>
    public long SystemRequests { get; set; }

    /// <summary>The own-key share of <see cref="Requests"/>.</summary>
    public long UserKeyRequests { get; set; }
    public long ChatRequests { get; set; }
    public long TitleRequests { get; set; }
    public long BenchmarkRequests { get; set; }
    public long PromptTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal EstimatedCost { get; set; }

    /// <summary>The system-configuration share of <see cref="EstimatedCost"/>.</summary>
    public decimal SystemEstimatedCost { get; set; }

    /// <summary>The own-key share of <see cref="EstimatedCost"/>.</summary>
    public decimal UserKeyEstimatedCost { get; set; }
    public long PricedRequests { get; set; }

    /// <summary>Most requests in one UTC clock hour; information for setting limits, never a misuse signal.</summary>
    public int PeakHourRequests { get; set; }
    public DateTime? PeakHourStartUtc { get; set; }

    /// <summary>Mean over hours with at least one request.</summary>
    public double RequestsPerActiveHour { get; set; }

    /// <summary>Days with any request of the selected key sources.</summary>
    public int ActiveDays { get; set; }
    public DateTime? LastRequestUtc { get; set; }
    public int ChatsCreated { get; set; }
    public int UserMessages { get; set; }

    /// <summary>Quota and per-minute refusals in the range.</summary>
    public int LimitRefusals { get; set; }

    /// <summary>Provider refusals, safety blocks and policy rejections in the range.</summary>
    public int ProviderRefusals { get; set; }
    public bool IsOverseerBlocked { get; set; }

    /// <summary>An active deletion hold or a pending deletion request.</summary>
    public bool HasAccountHold { get; set; }
    public List<AiTelemetryUserConfigDto> Configs { get; set; } = new();
}

/// <summary>One system configuration, or one of the user's own-key models, that the user's usage rows name.</summary>
public class AiTelemetryUserConfigDto
{
    /// <summary>system | user.</summary>
    public string KeySource { get; set; } = "system";

    /// <summary>The SystemAiApiConfiguration id, or the UserAiModel id (0 when not recorded); unique only within <see cref="KeySource"/>.</summary>
    public long ConfigId { get; set; }

    /// <summary>The current display name, else the usage rows' ModelDisplayName, else (own key) the model id.</summary>
    public string ConfigName { get; set; } = string.Empty;

    /// <summary>The configuration's (or own-key model's) current settings, for its badges; null when it is gone.</summary>
    public string? Provider { get; set; }
    public string? ThinkingLevel { get; set; }
    public string? ReasoningMode { get; set; }
    public string? ServiceTier { get; set; }
    public long Requests { get; set; }
    public decimal EstimatedCost { get; set; }

    /// <summary>The user's UserSystemAiApiConfiguration, if any; null on an own-key line.</summary>
    public long? AssignmentId { get; set; }
    public bool? AssignmentEnabled { get; set; }

    /// <summary>Effective (lazily reset) counter.</summary>
    public int? DailyChatRequests { get; set; }
    public int? MaxDailyChatRequests { get; set; }
    public int? MonthlyChatRequests { get; set; }
    public int? MaxMonthlyChatRequests { get; set; }
    public long? DailyChatTokens { get; set; }
    public long? MaxDailyChatTokens { get; set; }

    /// <summary>user | group | systemWide | ownKey | none.</summary>
    public string LimitSource { get; set; } = "none";
}

/// <summary>The usage of one user by model, role context and thinking level, over a date range.</summary>
public class AiTelemetryUserModelsDto
{
    /// <summary>system | user | both.</summary>
    public string KeySource { get; set; } = "system";
    public string UserId { get; set; } = string.Empty;

    /// <summary>The user's current name; null when the account is gone.</summary>
    public string? UserName { get; set; }
    public DateTime? CostRecordedSinceUtc { get; set; }
    public List<AiTelemetryUserModelRowDto> Rows { get; set; } = new();
}

/// <summary>The user's usage of one model in one key source, role context and thinking level.</summary>
public class AiTelemetryUserModelRowDto
{
    /// <summary>system | user.</summary>
    public string KeySource { get; set; } = "system";
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;

    /// <summary>The catalog name, else the retired entry's, else null.</summary>
    public string? ModelName { get; set; }
    public int RoleContext { get; set; }
    public string RoleGroup { get; set; } = string.Empty;

    /// <summary>"" when none was sent, null when not recorded.</summary>
    public string? ThinkingLevel { get; set; }
    public long Requests { get; set; }
    public long PromptTokens { get; set; }
    public long UncachedInputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheCreationTokens { get; set; }
    public long OutputTokens { get; set; }
    public long DurationSumMs { get; set; }
    public long DurationCount { get; set; }
    public decimal EstimatedCost { get; set; }
    public long PricedRequests { get; set; }
    public DateTime? LastRequestUtc { get; set; }
}

/// <summary>Telemetry over time, one line per series key, aligned with <see cref="BucketStartsUtc"/>.</summary>
public class AiTelemetrySeriesDto
{
    /// <summary>day | week | month.</summary>
    public string Bucket { get; set; } = "day";
    public string SplitBy { get; set; } = "none";
    public List<DateTime> BucketStartsUtc { get; set; } = new();
    public List<AiTelemetrySeriesLineDto> Series { get; set; } = new();
    public DateTime? CostRecordedSinceUtc { get; set; }
    public DateTime? AttemptsRecordedSinceUtc { get; set; }

    /// <summary>The user filter applied, in request order; empty when users are not filtered.</summary>
    public List<AiTelemetrySeriesUserRefDto> Users { get; set; } = new();

    /// <summary>The model filter applied, in request order; empty when models are not filtered.</summary>
    public List<AiTelemetrySeriesModelRefDto> Models { get; set; } = new();

    /// <summary>The group filter applied, in request order; empty when groups are not filtered.</summary>
    public List<AiTelemetrySeriesGroupRefDto> Groups { get; set; } = new();

    /// <summary>Activity per bucket, aligned with BucketStartsUtc.</summary>
    public List<AiTelemetryActivityPointDto>? Activity { get; set; }

    /// <summary>Buckets before it have no activity data.</summary>
    public DateOnly? ActivityCountedSinceUtc { get; set; }
}

public class AiTelemetrySeriesUserRefDto
{
    public string UserId { get; set; } = string.Empty;

    /// <summary>Null when the account is gone.</summary>
    public string? UserName { get; set; }
}

public class AiTelemetrySeriesModelRefDto
{
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;

    /// <summary>The catalog name, else the retired entry's, else null.</summary>
    public string? ModelName { get; set; }
}

public class AiTelemetrySeriesGroupRefDto
{
    /// <summary>Null is "No group": the users who belong to no group.</summary>
    public long? GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
}

/// <summary>The models, users and groups the charts dialog offers for a date range, key sources and roles.</summary>
public class AiTelemetrySeriesOptionsDto
{
    public List<AiTelemetrySeriesModelOptionDto> Models { get; set; } = new();

    /// <summary>The users with the most requests, at most AiTelemetrySummaryService.MaxSeriesOptionUsers.</summary>
    public List<AiTelemetrySeriesUserOptionDto> Users { get; set; } = new();

    /// <summary>Users with usage in the scope, before the cap.</summary>
    public int TotalUsers { get; set; }
    public bool UsersTruncated { get; set; }
    public List<AiTelemetrySeriesGroupOptionDto> Groups { get; set; } = new();
}

public class AiTelemetrySeriesModelOptionDto
{
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;

    /// <summary>The catalog name, else the retired entry's, else null.</summary>
    public string? ModelName { get; set; }

    /// <summary>"system" and/or "user": the key sources the model was used through.</summary>
    public List<string> KeySources { get; set; } = new();

    /// <summary>0 for a model seen only in the attempts ledger.</summary>
    public long Requests { get; set; }
    public decimal EstimatedCost { get; set; }
    public long PricedRequests { get; set; }
    public long Tokens { get; set; }
    public DateTime? LastRequestUtc { get; set; }
}

public class AiTelemetrySeriesUserOptionDto
{
    public string UserId { get; set; } = string.Empty;

    /// <summary>Null when the account is gone.</summary>
    public string? UserName { get; set; }
    public long Requests { get; set; }
    public long SystemRequests { get; set; }
    public long UserKeyRequests { get; set; }
    public decimal EstimatedCost { get; set; }
    public long PricedRequests { get; set; }
    public DateTime? LastRequestUtc { get; set; }
}

public class AiTelemetrySeriesGroupOptionDto
{
    /// <summary>Null is "No group": the users who belong to no group.</summary>
    public long? GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;

    /// <summary>Current members; for "No group", the accounts in no group.</summary>
    public int MemberCount { get; set; }

    /// <summary>Summed over the current members' usage in the scope.</summary>
    public long Requests { get; set; }
    public decimal EstimatedCost { get; set; }
    public long PricedRequests { get; set; }
    public DateTime? LastRequestUtc { get; set; }
}

/// <summary>Per-group usage of the selected key sources: the usage of each group's current members over the range.</summary>
public class AiTelemetryGroupsDto
{
    /// <summary>system | user | both.</summary>
    public string KeySource { get; set; } = "system";

    /// <summary>Every group, then "No group"; a member of two groups counts in both.</summary>
    public List<AiTelemetryGroupRowDto> Groups { get; set; } = new();

    /// <summary>The security event log exists, so the refusal counts are meaningful.</summary>
    public bool SecurityEventsAvailable { get; set; }
}

public class AiTelemetryGroupRowDto
{
    /// <summary>Null is "No group": the users who belong to no group.</summary>
    public long? GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;

    /// <summary>Current members; for "No group", the accounts in no group.</summary>
    public int MemberCount { get; set; }

    /// <summary>Members with a request in the scope.</summary>
    public int ActiveMembers { get; set; }

    /// <summary>Members blocked from Overseer.</summary>
    public int BlockedMembers { get; set; }

    /// <summary>All rows of the selected key sources, summed over members.</summary>
    public long Requests { get; set; }
    public long SystemRequests { get; set; }
    public long UserKeyRequests { get; set; }
    public decimal SystemEstimatedCost { get; set; }
    public decimal UserKeyEstimatedCost { get; set; }
    public long ChatRequests { get; set; }
    public long TitleRequests { get; set; }
    public long BenchmarkRequests { get; set; }
    public long PromptTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal EstimatedCost { get; set; }
    public long PricedRequests { get; set; }

    /// <summary>Most requests in one UTC clock hour, members' requests summed per hour.</summary>
    public int PeakHourRequests { get; set; }
    public DateTime? PeakHourStartUtc { get; set; }

    /// <summary>Mean over hours with at least one request by any member.</summary>
    public double RequestsPerActiveHour { get; set; }

    /// <summary>Distinct days with any member's request of the selected key sources.</summary>
    public int ActiveDays { get; set; }
    public DateTime? LastRequestUtc { get; set; }
    public int ChatsCreated { get; set; }
    public int UserMessages { get; set; }
    public int LimitRefusals { get; set; }
    public int ProviderRefusals { get; set; }
    public List<AiTelemetryGroupConfigDto> Configs { get; set; } = new();
}

/// <summary>One system configuration the group grants or its members used, or the members' own keys.</summary>
public class AiTelemetryGroupConfigDto
{
    /// <summary>system | user.</summary>
    public string KeySource { get; set; } = "system";

    /// <summary>The SystemAiApiConfiguration id; null on the own-key line.</summary>
    public long? ConfigId { get; set; }
    public string ConfigName { get; set; } = string.Empty;

    /// <summary>The configuration's current settings, for its badges; null when it is gone and on the own-key line.</summary>
    public string? Provider { get; set; }
    public string? ThinkingLevel { get; set; }
    public string? ReasoningMode { get; set; }
    public string? ServiceTier { get; set; }
    public long Requests { get; set; }
    public decimal EstimatedCost { get; set; }

    /// <summary>The GroupSystemAiApiConfiguration, if the group grants the configuration.</summary>
    public long? AssignmentId { get; set; }
    public bool? AssignmentEnabled { get; set; }
    public bool GrantedByGroup { get; set; }

    /// <summary>The group's pooled effective (lazily reset) counters; null when not granted and on the own-key line.</summary>
    public int? DailyChatRequests { get; set; }
    public int? MaxDailyChatRequests { get; set; }
    public int? MonthlyChatRequests { get; set; }
    public int? MaxMonthlyChatRequests { get; set; }
    public long? DailyChatTokens { get; set; }
    public long? MaxDailyChatTokens { get; set; }
}

/// <summary>The usage of one group's current members by model, role context and thinking level, over a date range.</summary>
public class AiTelemetryGroupModelsDto
{
    /// <summary>system | user | both.</summary>
    public string KeySource { get; set; } = "system";

    /// <summary>Null is "No group".</summary>
    public long? GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public DateTime? CostRecordedSinceUtc { get; set; }
    public List<AiTelemetryUserModelRowDto> Rows { get; set; } = new();
}

public class AiTelemetryActivityPointDto
{
    /// <summary>Distinct in the bucket, not a sum of days.</summary>
    public int ActiveUsers { get; set; }
    public int NewUsers { get; set; }
    public int ChatsCreated { get; set; }
    public int ConfidentialChatsCreated { get; set; }
    public int GnollHackChatsCreated { get; set; }
    public int IncognitoChatsCreated { get; set; }
    public int UserMessages { get; set; }
    public int AssistantMessages { get; set; }
    public int IncognitoUserMessages { get; set; }
}

public class AiTelemetrySeriesLineDto
{
    public string Key { get; set; } = string.Empty;

    /// <summary>A model name, role, provider, key source or user name.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>For provider colors.</summary>
    public string? Provider { get; set; }
    public bool IsOther { get; set; }

    /// <summary>Aligned with BucketStartsUtc, zeros filled.</summary>
    public List<AiTelemetryPointDto> Points { get; set; } = new();
}

public class AiTelemetryPointDto
{
    public long Requests { get; set; }
    public long PromptTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal EstimatedCost { get; set; }
    public long PricedRequests { get; set; }
    public long DurationSumMs { get; set; }
    public long DurationCount { get; set; }
    public int ValidAttempts { get; set; }
    public int ProviderErrors { get; set; }
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
    public int InFlightCalls { get; set; }
}

public class ResetCooldownRequest
{
    public string? CredentialKey { get; set; }
}

/// <summary>
/// What the server's custom-endpoint policy permits, so the admin model form can state it
/// rather than let every save be refused by a rule the form never mentioned.
/// </summary>
public class EndpointPolicySummaryDto
{
    /// <summary>False when no host is allowlisted, which refuses every base URL.</summary>
    public bool CustomEndpointsEnabled { get; set; }
    public List<string> AllowedHostPatterns { get; set; } = new();
    public List<string> AllowedHeaderNames { get; set; } = new();
    public bool AllowLoopback { get; set; }
}

/// <summary>Something using a system configuration right now, which a delete would interrupt.</summary>
public class SystemConfigBlockerDto
{
    /// <summary>"run", "series", "difficultyJob", "generationJob", "rubricCheckJob", "rubricGapAuthorJob", "reportPackJob" or "runReportWriter".</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The run or series id, or the job id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>A benchmark run id when <see cref="Kind"/> is "run", so the UI can open it.</summary>
    public long? RunId { get; set; }

    public string Label { get; set; } = string.Empty;

    /// <summary>The roles the configuration plays, in words ("assessor", "claim verifier").</summary>
    public List<string> Roles { get; set; } = new();

    public DateTime StartedAtUtc { get; set; }
}

/// <summary>Whether a system configuration can be deleted now, and what a delete removes and keeps.</summary>
public class SystemConfigDeletionCheckDto
{
    public long ConfigId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool CanDelete { get; set; }
    public List<SystemConfigBlockerDto> Blockers { get; set; } = new();

    /// <summary>Benchmark runs naming the configuration. Informational: their history is kept.</summary>
    public int BenchmarkRunReferenceCount { get; set; }

    /// <summary>Stopped series naming the configuration. Informational: resuming them will be refused.</summary>
    public int StoppedSeriesCount { get; set; }

    /// <summary>Stopped battery runs naming the configuration. Informational: resuming them will be refused.</summary>
    public int StoppedBatteryRunCount { get; set; }

    /// <summary>Removed with the configuration.</summary>
    public int UserAssignmentCount { get; set; }

    /// <summary>Removed with the configuration.</summary>
    public int GroupAssignmentCount { get; set; }

    /// <summary>Users' confidentiality decisions for this model. Removed with the configuration.</summary>
    public int ConfidentialTrustCount { get; set; }
}

/// <summary>One provider's default API key, as the API Keys dialog shows it. Never carries the key.</summary>
public class DefaultApiKeyStatusDto
{
    public string Provider { get; set; } = string.Empty;
    public bool HasKey { get; set; }

    /// <summary>The last four characters of the key, or null when none is stored.</summary>
    public string? KeyHint { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Overseer.Services.ApiKeyVerificationDto Verification { get; set; } = default!;

    /// <summary>System configurations of this provider set to use the default key.</summary>
    public List<DefaultApiKeyUserDto> UsedBy { get; set; } = new();
}

/// <summary>A system configuration that uses a default key.</summary>
public class DefaultApiKeyUserDto
{
    public long Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }

    /// <summary>The configuration's settings, for its badges. No provider: it is the key's.</summary>
    public string? ThinkingLevel { get; set; }
    public string? ReasoningMode { get; set; }
    public string? ServiceTier { get; set; }
}

public class SaveDefaultApiKeyRequest
{
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Save the key even if its provider cannot confirm it (Save Anyway). The check still runs.</summary>
    public bool SaveUnverified { get; set; }
}

public class DefaultApiKeySaveResultDto
{
    public DefaultApiKeyStatusDto Status { get; set; } = new();

    /// <summary>System configurations whose copy of the key was rewritten.</summary>
    public int UpdatedConfigCount { get; set; }

    /// <summary>The provider's warning about a key it accepted, such as a rate limit; otherwise null.</summary>
    public string? Warning { get; set; }
}

public class DefaultApiKeyVerifyResultDto
{
    public DefaultApiKeyStatusDto Status { get; set; } = new();
}

/// <summary>What deleting a default key would disable.</summary>
public class DefaultApiKeyDeletionCheckDto
{
    public int Count { get; set; }
    public List<DefaultApiKeyUserDto> Configs { get; set; } = new();
}

public class DefaultApiKeyDeleteResultDto
{
    /// <summary>System configurations disabled because their copy of the key was removed.</summary>
    public int DisabledCount { get; set; }
}