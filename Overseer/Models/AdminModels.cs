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
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? ThinkingLevel { get; set; }
    public int? MaxInputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
    public int OrderIndex { get; set; }
    public bool IsEnabled { get; set; }
    public bool HasApiKey { get; set; }
    public bool IsSystemWide { get; set; }
    public int? MaxDailyRequests { get; set; }
    public int? MaxMonthlyRequests { get; set; }
    public int? MaxTotalRequests { get; set; }
    public int DailyRequestsCount { get; set; }
    public int MonthlyRequestsCount { get; set; }
    public int TotalRequestsCount { get; set; }
    public int ModelRole { get; set; }
}

public class CreateSystemAiApiConfigurationRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? ThinkingLevel { get; set; }
    public int? MaxInputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
    public bool IsEnabled { get; set; }
    public string? ApiKey { get; set; }
    public bool IsSystemWide { get; set; }
    public int? MaxDailyRequests { get; set; }
    public int? MaxMonthlyRequests { get; set; }
    public int? MaxTotalRequests { get; set; }
    public int ModelRole { get; set; } = 3;
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
    public int OrderIndex { get; set; }
    public int? MaxDailyRequests { get; set; }
    public int? MaxMonthlyRequests { get; set; }
    public int? MaxTotalRequests { get; set; }
    public int DailyRequestsCount { get; set; }
    public int MonthlyRequestsCount { get; set; }
    public int TotalRequestsCount { get; set; }
    public int ModelRole { get; set; }
}

public class GroupSystemAiConfigDto
{
    public long Id { get; set; }
    public long GroupId { get; set; }
    public long SystemAiApiConfigurationId { get; set; }
    public bool IsEnabled { get; set; }
    public int OrderIndex { get; set; }
    public int? MaxDailyRequests { get; set; }
    public int? MaxMonthlyRequests { get; set; }
    public int? MaxTotalRequests { get; set; }
    public int DailyRequestsCount { get; set; }
    public int MonthlyRequestsCount { get; set; }
    public int TotalRequestsCount { get; set; }
    public int ModelRole { get; set; }
}

public class AssignConfigToUserRequest
{
    public long SystemAiApiConfigurationId { get; set; }
    public bool IsEnabled { get; set; }
    public int? MaxDailyRequests { get; set; }
    public int? MaxMonthlyRequests { get; set; }
    public int? MaxTotalRequests { get; set; }
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
