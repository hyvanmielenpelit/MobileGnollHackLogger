using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Services.Providers;
using Overseer.Services.Security;

namespace Overseer.Services;

public class SystemAiConfigService
{
    public const string DisabledAssignmentMessage = "Your access to this model has been turned off by an administrator.";
    public const string NotAssignedMessage = "You do not have access to this model.";

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<SystemAiConfigService> _logger;
    private readonly ISecurityEventRecorder? _securityEvents;
    private readonly ModelPricingService? _pricing;
    private readonly AiUsageGuard? _usageGuard;

    /// <param name="usageGuard">The per-user daily ceiling on chat access granted through a group or system-wide; null leaves it off.</param>
    public SystemAiConfigService(ApplicationDbContext dbContext, ILogger<SystemAiConfigService> logger, ISecurityEventRecorder? securityEvents = null,
        ModelPricingService? pricing = null, AiUsageGuard? usageGuard = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _securityEvents = securityEvents;
        _pricing = pricing;
        _usageGuard = usageGuard;
    }

    /// <summary>
    /// Whether <paramref name="userId"/> may use the configuration now. The configuration-wide limits come
    /// first; a disabled user assignment denies outright; an enabled one must be under its own limits and,
    /// when the user also has enabled group assignments for the configuration, at least one of those must be
    /// under its limits; without a user assignment an enabled group assignment under its limits grants access;
    /// otherwise only a system-wide configuration does. A limit of null or 0 means no limit. Chat access through a
    /// group or a system-wide configuration is also subject to the per-user <see cref="AiUsageGuard"/>.
    /// </summary>
    public async Task<(SystemAiApiConfiguration? Config, string? ErrorMessage)> GetAndCheckSystemConfigAsync(long configId, string userId, int? requiredRoleFilter = null)
    {
        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(configId);
        if (config == null || !config.IsEnabled)
        {
            return (null, "System AI Configuration is disabled or not found.");
        }

        var now = DateTime.UtcNow;

        ResetCountersIfNeeded(config, now);

        int checkRole = requiredRoleFilter ?? 1;

        var configLimit = TrippedLimit(config, checkRole);
        if (configLimit != null)
        {
            RecordRefusal(SecurityEventKind.QuotaRefused, "system", config, userId);
            return (null, LimitMessage(LimitScope.System, configLimit.Value));
        }

        int resolvedModelRole = config.ModelRole;

        var userAssignment = await _dbContext.UserSystemAiApiConfigurations
            .FirstOrDefaultAsync(u => u.SystemAiApiConfigurationId == configId && u.AspNetUserId == userId);

        if (userAssignment != null && !userAssignment.IsEnabled)
        {
            RecordRefusal(SecurityEventKind.AccessDenied, "disabled", config, userId);
            return (null, DisabledAssignmentMessage);
        }

        var userGroupIds = await _dbContext.UserGroups
            .Where(ug => ug.AspNetUserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync();

        var groupAssignments = await _dbContext.GroupSystemAiApiConfigurations
            .Where(g => g.SystemAiApiConfigurationId == configId && userGroupIds.Contains(g.GroupId) && g.IsEnabled)
            .ToListAsync();

        foreach (var g in groupAssignments)
        {
            ResetCountersIfNeeded(g, now);
        }

        if (userAssignment != null)
        {
            ResetCountersIfNeeded(userAssignment, now);
            var userLimit = TrippedLimit(userAssignment, checkRole);
            if (userLimit != null)
            {
                RecordRefusal(SecurityEventKind.QuotaRefused, "user", config, userId);
                return (null, LimitMessage(LimitScope.User, userLimit.Value));
            }

            if (groupAssignments.Count > 0 && groupAssignments.All(g => TrippedLimit(g, checkRole) != null))
            {
                RecordRefusal(SecurityEventKind.QuotaRefused, "group", config, userId);
                return (null, LimitMessage(LimitScope.Group, TrippedLimit(groupAssignments[0], checkRole)!.Value));
            }

            resolvedModelRole = userAssignment.ModelRole;
            if (requiredRoleFilter.HasValue && (resolvedModelRole & requiredRoleFilter.Value) != requiredRoleFilter.Value)
            {
                return (null, "Model not authorized for this role.");
            }

            return (config, null);
        }

        if (groupAssignments.Count > 0)
        {
            var allowedGroup = groupAssignments.FirstOrDefault(g => TrippedLimit(g, checkRole) == null);

            if (allowedGroup == null)
            {
                RecordRefusal(SecurityEventKind.QuotaRefused, "group", config, userId);
                return (null, LimitMessage(LimitScope.Group, TrippedLimit(groupAssignments[0], checkRole)!.Value));
            }

            resolvedModelRole = allowedGroup.ModelRole;
            if (requiredRoleFilter.HasValue && (resolvedModelRole & requiredRoleFilter.Value) != requiredRoleFilter.Value)
            {
                return (null, "Model not authorized for this role.");
            }

            return await GuardedAsync(config, userId, checkRole);
        }

        if (!config.IsSystemWide)
        {
            RecordRefusal(SecurityEventKind.AccessDenied, "not-assigned", config, userId);
            return (null, NotAssignedMessage);
        }

        if (requiredRoleFilter.HasValue && (resolvedModelRole & requiredRoleFilter.Value) != requiredRoleFilter.Value)
        {
            return (null, "Model not authorized for this role.");
        }

        return await GuardedAsync(config, userId, checkRole);
    }

    /// <summary>The configuration, unless the usage guard refuses this chat access; title checks are not guarded.</summary>
    private async Task<(SystemAiApiConfiguration? Config, string? ErrorMessage)> GuardedAsync(SystemAiApiConfiguration config, string userId, int checkRole)
    {
        if (_usageGuard != null && checkRole == 1)
        {
            var refusal = await _usageGuard.CheckAsync(userId, config.Id, config.Provider, config.ModelId);
            if (refusal != null)
            {
                return (null, refusal);
            }
        }

        return (config, null);
    }

    private void RecordRefusal(SecurityEventKind kind, string code, SystemAiApiConfiguration config, string userId) =>
        _securityEvents?.Record(new SecurityEventEntry(kind, UserId: userId, SystemAiApiConfigurationId: config.Id,
            Provider: config.Provider, ModelId: config.ModelId, Code: code));

    internal enum LimitScope { User, Group, System }

    internal enum LimitPeriod { Daily, Monthly, Total }

    /// <summary>A limit that was reached: its period, whether it counts tokens, its value, and the role it belongs to.</summary>
    internal readonly record struct TrippedLimitInfo(LimitPeriod Period, bool Tokens, long Max, int Role);

    /// <summary>The first limit of the role that the entity has reached, or null.</summary>
    internal static TrippedLimitInfo? TrippedLimit(IRateLimitedEntity entity, int role)
    {
        if (role == 2) // Title Generation
        {
            if (IsRateLimited(entity.DailyTitleRequestsCount, entity.MaxDailyTitleRequests)) return new(LimitPeriod.Daily, false, entity.MaxDailyTitleRequests!.Value, role);
            if (IsRateLimited(entity.MonthlyTitleRequestsCount, entity.MaxMonthlyTitleRequests)) return new(LimitPeriod.Monthly, false, entity.MaxMonthlyTitleRequests!.Value, role);
            if (IsRateLimited(entity.TotalTitleRequestsCount, entity.MaxTotalTitleRequests)) return new(LimitPeriod.Total, false, entity.MaxTotalTitleRequests!.Value, role);
            if (IsTokenRateLimited(entity.DailyTitleTokensCount, entity.MaxDailyTitleTokens)) return new(LimitPeriod.Daily, true, entity.MaxDailyTitleTokens!.Value, role);
            if (IsTokenRateLimited(entity.MonthlyTitleTokensCount, entity.MaxMonthlyTitleTokens)) return new(LimitPeriod.Monthly, true, entity.MaxMonthlyTitleTokens!.Value, role);
            if (IsTokenRateLimited(entity.TotalTitleTokensCount, entity.MaxTotalTitleTokens)) return new(LimitPeriod.Total, true, entity.MaxTotalTitleTokens!.Value, role);
            return null;
        }

        // Chat
        if (IsRateLimited(entity.DailyChatRequestsCount, entity.MaxDailyChatRequests)) return new(LimitPeriod.Daily, false, entity.MaxDailyChatRequests!.Value, role);
        if (IsRateLimited(entity.MonthlyChatRequestsCount, entity.MaxMonthlyChatRequests)) return new(LimitPeriod.Monthly, false, entity.MaxMonthlyChatRequests!.Value, role);
        if (IsRateLimited(entity.TotalChatRequestsCount, entity.MaxTotalChatRequests)) return new(LimitPeriod.Total, false, entity.MaxTotalChatRequests!.Value, role);
        if (IsTokenRateLimited(entity.DailyChatTokensCount, entity.MaxDailyChatTokens)) return new(LimitPeriod.Daily, true, entity.MaxDailyChatTokens!.Value, role);
        if (IsTokenRateLimited(entity.MonthlyChatTokensCount, entity.MaxMonthlyChatTokens)) return new(LimitPeriod.Monthly, true, entity.MaxMonthlyChatTokens!.Value, role);
        if (IsTokenRateLimited(entity.TotalChatTokensCount, entity.MaxTotalChatTokens)) return new(LimitPeriod.Total, true, entity.MaxTotalChatTokens!.Value, role);
        return null;
    }

    /// <summary>The refusal message for a reached limit, naming the limit and when it resets.</summary>
    internal static string LimitMessage(LimitScope scope, TrippedLimitInfo limit)
    {
        string period = limit.Period switch
        {
            LimitPeriod.Daily => "daily",
            LimitPeriod.Monthly => "monthly",
            _ => "lifetime"
        };
        string kind = limit.Role == 2 ? $"{period} title-generation limit" : $"{period} limit";
        string amount = limit.Max.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
            + (limit.Tokens ? (limit.Max == 1 ? " token" : " tokens") : limit.Max == 1 ? " request" : " requests");
        string reset = limit.Period switch
        {
            LimitPeriod.Daily => "It resets at 00:00 UTC.",
            LimitPeriod.Monthly => "It resets on the 1st of the month (UTC).",
            _ => "It does not reset."
        };
        string subject = scope switch
        {
            LimitScope.User => $"You have reached your {kind} for this model",
            LimitScope.Group => $"Your group has reached its {kind} for this model",
            _ => $"This model has reached its {kind} for all users"
        };
        return $"{subject} ({amount}). {reset}";
    }

    public async Task RecordUsageAsync(
        long configId,
        string? userId,
        int inputTokens,
        int outputTokens,
        int roleContext = 1,
        int? cacheReadTokens = null,
        int? cacheCreationTokens = null,
        int? totalDurationMs = null,
        UsageThinkingLevel? thinkingLevel = null,
        decimal? estimatedCost = null,
        string? pricingSource = null)
    {
        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(configId);
        if (config == null) return;

        var now = DateTime.UtcNow;

        long totalTokens = inputTokens + outputTokens;

        // GnollBench roles (4-8) are logged and priced but never consume the chat or title quotas.
        bool countsTowardLimits = roleContext is < 4 or > 8;
        if (countsTowardLimits)
        {
            ResetCountersIfNeeded(config, now);
            IncrementUsage(config, roleContext, totalTokens);
        }

        bool hasUser = !string.IsNullOrEmpty(userId);

        if (hasUser && countsTowardLimits)
        {
            // User specific limit
            var userAssignment = await _dbContext.UserSystemAiApiConfigurations
                .FirstOrDefaultAsync(u => u.SystemAiApiConfigurationId == configId && u.AspNetUserId == userId);
            if (userAssignment != null && userAssignment.IsEnabled)
            {
                ResetCountersIfNeeded(userAssignment, now);
                IncrementUsage(userAssignment, roleContext, totalTokens);
            }

            // Group specific limit
            var userGroupIds = await _dbContext.UserGroups
                .Where(ug => ug.AspNetUserId == userId)
                .Select(ug => ug.GroupId)
                .ToListAsync();
            var groupAssignments = await _dbContext.GroupSystemAiApiConfigurations
                .Where(g => g.SystemAiApiConfigurationId == configId && userGroupIds.Contains(g.GroupId) && g.IsEnabled)
                .ToListAsync();
            foreach (var g in groupAssignments)
            {
                ResetCountersIfNeeded(g, now);
                IncrementUsage(g, roleContext, totalTokens);
            }
        }

        // SystemAiUsageLog.AspNetUserId is a required foreign key to AspNetUsers, so a row
        // without a user cannot be stored. Attempting it anyway is not merely futile: the
        // rejected insert stays in the change tracker in Added state and every later
        // SaveChanges on this shared context fails on it too.
        SystemAiUsageLog? log = null;
        if (hasUser)
        {
            log = new SystemAiUsageLog
            {
                SystemAiApiConfigurationId = configId,
                AspNetUserId = userId!,
                TimestampUtc = now,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CacheReadInputTokens = cacheReadTokens,
                CacheCreationInputTokens = cacheCreationTokens,
                TotalDurationMs = totalDurationMs,
                ModelId = config.ModelId,
                Provider = config.Provider,
                ModelDisplayName = config.DisplayName,
                RoleContext = roleContext,
                // Omitted: what the configuration sends, as every GnollBench request is built with it.
                ThinkingLevel = Cut(thinkingLevel is { } level ? level.Value ?? string.Empty : config.ThinkingLevel ?? string.Empty, 32),
                EstimatedCost = estimatedCost,
                PricingSource = estimatedCost == null ? null : Cut(pricingSource, 16)
            };
            _dbContext.SystemAiUsageLogs.Add(log);
        }
        else
        {
            _logger.LogWarning(
                "Usage for system AI configuration {ConfigId} (role context {RoleContext}) was counted but not logged: no user id was supplied.",
                configId, roleContext);
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            // Leave the caller's context usable: the counter updates are valid and will
            // commit with the next save, but a rejected log row would fail forever.
            if (log != null)
            {
                _dbContext.Entry(log).State = EntityState.Detached;
            }
            throw;
        }
    }

    /// <summary>
    /// Logs a sub-agent run on an AI:* server key: one role-3 row with configuration id 0 (attribution only), named
    /// "Server key: model", which the usage guard and the per-user telemetry count. No configuration counter changes.
    /// </summary>
    public async Task RecordServerKeyUsageAsync(string userId, string provider, string modelId, int inputTokens, int outputTokens,
        decimal? estimatedCost, string? pricingSource, CancellationToken ct = default)
    {
        // A row without a user cannot be stored; see RecordUsageAsync.
        if (string.IsNullOrEmpty(userId)) return;

        var log = new SystemAiUsageLog
        {
            SystemAiApiConfigurationId = 0,
            AspNetUserId = userId,
            TimestampUtc = DateTime.UtcNow,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            ModelId = Cut(modelId, 128)!,
            Provider = Cut(provider, 64)!,
            ModelDisplayName = Cut($"Server key: {modelId}", 256),
            RoleContext = 3,
            EstimatedCost = estimatedCost,
            PricingSource = estimatedCost == null ? null : Cut(pricingSource, 16)
        };
        _dbContext.SystemAiUsageLogs.Add(log);

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch
        {
            // A rejected log row would fail every later save on this shared context.
            _dbContext.Entry(log).State = EntityState.Detached;
            throw;
        }
    }

    /// <summary>
    /// Records the usage of exactly <paramref name="calls"/>: whole-prompt input, output and cache tokens summed
    /// over them, and their cost priced per call on the configuration's current card. An empty list records nothing.
    /// </summary>
    public async Task RecordCallsUsageAsync(long configId, string? userId, int roleContext,
        IReadOnlyList<TokenUsageReport> calls, string? actualServiceTier, int? totalDurationMs,
        UsageThinkingLevel? thinkingLevel = null)
    {
        if (calls == null || calls.Count == 0) return;

        long input = 0, output = 0, cacheRead = 0, cacheWrite = 0;
        foreach (var call in calls)
        {
            input += call.TotalPromptTokens;
            output += call.OutputTokens;
            cacheRead += call.CacheReadTokens;
            cacheWrite += call.CacheCreationTokens;
        }

        decimal? cost = null;
        string? source = null;
        if (_pricing != null)
        {
            var config = await _dbContext.SystemAiApiConfigurations.FindAsync(configId);
            var pricing = config == null ? null : _pricing.Resolve(config);
            if (pricing != null)
            {
                // ComputeCost returns 0 for no card; the null check is what marks a row unpriced.
                cost = ModelPricingService.ComputeCost(pricing, calls, actualServiceTier, config!.ServiceTier);
                source = pricing.Source == ModelPricingSource.Custom ? "custom" : "catalog";
            }
        }

        await RecordUsageAsync(configId, userId, Saturate(input), Saturate(output), roleContext,
            Saturate(cacheRead), Saturate(cacheWrite), totalDurationMs, thinkingLevel, cost, source);
    }

    /// <summary>The usage of the calls that reported any; see the <see cref="TokenUsageReport"/> overload.</summary>
    public Task RecordCallsUsageAsync(long configId, string? userId, int roleContext,
        IReadOnlyList<Agents.ModelCallRecord>? calls, string? actualServiceTier, int? totalDurationMs,
        UsageThinkingLevel? thinkingLevel = null)
        => RecordCallsUsageAsync(configId, userId, roleContext,
            (calls ?? new List<Agents.ModelCallRecord>()).Where(c => c.Usage != null).Select(c => c.Usage!).ToList(),
            actualServiceTier, totalDurationMs, thinkingLevel);

    private static int Saturate(long value) => value > int.MaxValue ? int.MaxValue : (int)value;

    private static string? Cut(string? value, int max) =>
        value == null ? null : value.Length <= max ? value : value[..max];

    private void IncrementUsage(IRateLimitedEntity entity, int roleContext, long totalTokens)
    {
        if (roleContext == 2)
        {
            entity.DailyTitleRequestsCount++;
            entity.MonthlyTitleRequestsCount++;
            entity.TotalTitleRequestsCount++;
            entity.DailyTitleTokensCount += totalTokens;
            entity.MonthlyTitleTokensCount += totalTokens;
            entity.TotalTitleTokensCount += totalTokens;
        }
        else
        {
            entity.DailyChatRequestsCount++;
            entity.MonthlyChatRequestsCount++;
            entity.TotalChatRequestsCount++;
            entity.DailyChatTokensCount += totalTokens;
            entity.MonthlyChatTokensCount += totalTokens;
            entity.TotalChatTokensCount += totalTokens;
        }
    }

    // SystemAiErrorLog.ErrorMessage column width.
    private const int MaxErrorMessageLength = 2048;

    public async Task RecordErrorAsync(long configId, string errorMessage)
    {
        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(configId);
        if (config == null) return;

        var log = new SystemAiErrorLog
        {
            SystemAiApiConfigurationId = configId,
            Provider = config.Provider,
            ModelId = config.ModelId,
            ModelDisplayName = config.DisplayName,
            TimestampUtc = DateTime.UtcNow,
            ErrorMessage = errorMessage.Length <= MaxErrorMessageLength ? errorMessage : errorMessage[..MaxErrorMessageLength]
        };
        _dbContext.SystemAiErrorLogs.Add(log);

        await _dbContext.SaveChangesAsync();
    }

    private static bool IsRateLimited(int currentCount, int? maxCount)
    {
        if (maxCount.HasValue && maxCount.Value > 0)
        {
            return currentCount >= maxCount.Value;
        }
        return false;
    }

    private static bool IsTokenRateLimited(long currentCount, long? maxCount)
    {
        if (maxCount.HasValue && maxCount.Value > 0)
        {
            return currentCount >= maxCount.Value;
        }
        return false;
    }

    private void ResetCountersIfNeeded(IRateLimitedEntity entity, DateTime now)
    {
        if (entity.LastDailyReset == null || entity.LastDailyReset.Value.Date < now.Date)
        {
            entity.DailyChatRequestsCount = 0;
            entity.DailyTitleRequestsCount = 0;
            entity.DailyChatTokensCount = 0;
            entity.DailyTitleTokensCount = 0;
            entity.LastDailyReset = now;
        }

        if (entity.LastMonthlyReset == null || entity.LastMonthlyReset.Value.Year < now.Year || entity.LastMonthlyReset.Value.Month < now.Month)
        {
            entity.MonthlyChatRequestsCount = 0;
            entity.MonthlyTitleRequestsCount = 0;
            entity.MonthlyChatTokensCount = 0;
            entity.MonthlyTitleTokensCount = 0;
            entity.LastMonthlyReset = now;
        }
    }
}
