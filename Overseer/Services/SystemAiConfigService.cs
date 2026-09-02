using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;

namespace Overseer.Services;

public class SystemAiConfigService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<SystemAiConfigService> _logger;

    public SystemAiConfigService(ApplicationDbContext dbContext, ILogger<SystemAiConfigService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<(SystemAiApiConfiguration? Config, string? ErrorMessage)> GetAndCheckSystemConfigAsync(long configId, string userId, int? requiredRoleFilter = null)
    {
        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(configId);
        if (config == null || !config.IsEnabled)
        {
            return (null, "System AI Configuration is disabled or not found.");
        }

        if (config.IsBudgetExhausted)
        {
            return (null, "The budget for this model has been exhausted.");
        }

        var now = DateTime.UtcNow;

        // Check if config limits need reset
        ResetCountersIfNeeded(config, now);

        int checkRole = requiredRoleFilter ?? 1;

        if (IsRateLimitedForRole(config, checkRole))
        {
            return (null, "System-wide rate limit exceeded for this model.");
        }

        // Calculate resolved role
        int resolvedModelRole = config.ModelRole;

        // Check User specific limit
        var userAssignment = await _dbContext.UserSystemAiApiConfigurations
            .FirstOrDefaultAsync(u => u.SystemAiApiConfigurationId == configId && u.AspNetUserId == userId);

        if (userAssignment != null && userAssignment.IsEnabled)
        {
            ResetCountersIfNeeded(userAssignment, now);
            if (IsRateLimitedForRole(userAssignment, checkRole))
            {
                return (null, "User rate limit exceeded for this model.");
            }
            
            resolvedModelRole = userAssignment.ModelRole;
            if (requiredRoleFilter.HasValue && (resolvedModelRole & requiredRoleFilter.Value) != requiredRoleFilter.Value)
            {
                return (null, "Model not authorized for this role.");
            }
            
            return (config, null); // Has specific user access
        }

        // Check Group specific limit
        var userGroupIds = await _dbContext.UserGroups
            .Where(ug => ug.AspNetUserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync();

        var groupAssignments = await _dbContext.GroupSystemAiApiConfigurations
            .Where(g => g.SystemAiApiConfigurationId == configId && userGroupIds.Contains(g.GroupId) && g.IsEnabled)
            .ToListAsync();

        if (groupAssignments.Any())
        {
            foreach(var g in groupAssignments)
            {
                ResetCountersIfNeeded(g, now);
            }

            var allowedGroup = groupAssignments.FirstOrDefault(g => !IsRateLimitedForRole(g, checkRole));

            if (allowedGroup == null)
            {
                return (null, "Group rate limit exceeded for this model.");
            }
            
            resolvedModelRole = allowedGroup.ModelRole;
            if (requiredRoleFilter.HasValue && (resolvedModelRole & requiredRoleFilter.Value) != requiredRoleFilter.Value)
            {
                return (null, "Model not authorized for this role.");
            }
            
            return (config, null); // Has group access
        }

        if (!config.IsSystemWide)
        {
            return (null, "You do not have access to this model.");
        }

        if (requiredRoleFilter.HasValue && (resolvedModelRole & requiredRoleFilter.Value) != requiredRoleFilter.Value)
        {
            return (null, "Model not authorized for this role.");
        }

        return (config, null);
    }

    private bool IsRateLimitedForRole(IRateLimitedEntity entity, int role)
    {
        if (role == 2) // Title Generation
        {
            return IsRateLimited(entity.DailyTitleRequestsCount, entity.MaxDailyTitleRequests) ||
                   IsRateLimited(entity.MonthlyTitleRequestsCount, entity.MaxMonthlyTitleRequests) ||
                   IsRateLimited(entity.TotalTitleRequestsCount, entity.MaxTotalTitleRequests) ||
                   IsTokenRateLimited(entity.DailyTitleTokensCount, entity.MaxDailyTitleTokens) ||
                   IsTokenRateLimited(entity.MonthlyTitleTokensCount, entity.MaxMonthlyTitleTokens) ||
                   IsTokenRateLimited(entity.TotalTitleTokensCount, entity.MaxTotalTitleTokens);
        }
        else // Chat
        {
            return IsRateLimited(entity.DailyChatRequestsCount, entity.MaxDailyChatRequests) ||
                   IsRateLimited(entity.MonthlyChatRequestsCount, entity.MaxMonthlyChatRequests) ||
                   IsRateLimited(entity.TotalChatRequestsCount, entity.MaxTotalChatRequests) ||
                   IsTokenRateLimited(entity.DailyChatTokensCount, entity.MaxDailyChatTokens) ||
                   IsTokenRateLimited(entity.MonthlyChatTokensCount, entity.MaxMonthlyChatTokens) ||
                   IsTokenRateLimited(entity.TotalChatTokensCount, entity.MaxTotalChatTokens);
        }
    }

    public async Task RecordUsageAsync(
        long configId,
        string? userId,
        int inputTokens,
        int outputTokens,
        int roleContext = 1,
        int? cacheReadTokens = null,
        int? cacheCreationTokens = null,
        int? totalDurationMs = null)
    {
        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(configId);
        if (config == null) return;
        
        var now = DateTime.UtcNow;
        ResetCountersIfNeeded(config, now);

        long totalTokens = inputTokens + outputTokens;

        IncrementUsage(config, roleContext, totalTokens);

        bool hasUser = !string.IsNullOrEmpty(userId);

        if (hasUser)
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
                RoleContext = roleContext
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

    public async Task RecordErrorAsync(long configId, string errorMessage)
    {
        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(configId);
        if (config == null) return;

        var log = new SystemAiErrorLog
        {
            SystemAiApiConfigurationId = configId,
            TimestampUtc = DateTime.UtcNow,
            ErrorMessage = errorMessage
        };
        _dbContext.SystemAiErrorLogs.Add(log);

        // Parse for budget exhaustion (402 or 429 quota) - simplistic check
        if (errorMessage.Contains("402") || errorMessage.Contains("insufficient_quota") || errorMessage.Contains("budget"))
        {
            config.IsBudgetExhausted = true;
            config.LastBudgetNotificationSentUtc = DateTime.UtcNow; // This would hook into email sending later
        }

        await _dbContext.SaveChangesAsync();
    }

    private bool IsRateLimited(int currentCount, int? maxCount)
    {
        if (maxCount.HasValue && maxCount.Value > 0)
        {
            return currentCount >= maxCount.Value;
        }
        return false;
    }

    private bool IsTokenRateLimited(long currentCount, long? maxCount)
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
