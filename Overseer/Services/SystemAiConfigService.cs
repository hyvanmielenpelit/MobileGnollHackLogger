using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace Overseer.Services;

public class SystemAiConfigService
{
    private readonly ApplicationDbContext _dbContext;

    public SystemAiConfigService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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

        if (IsRateLimited(config.DailyRequestsCount, config.MaxDailyRequests) ||
            IsRateLimited(config.MonthlyRequestsCount, config.MaxMonthlyRequests) ||
            IsRateLimited(config.TotalRequestsCount, config.MaxTotalRequests))
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
            if (IsRateLimited(userAssignment.DailyRequestsCount, userAssignment.MaxDailyRequests) ||
                IsRateLimited(userAssignment.MonthlyRequestsCount, userAssignment.MaxMonthlyRequests) ||
                IsRateLimited(userAssignment.TotalRequestsCount, userAssignment.MaxTotalRequests))
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

            var allowedGroup = groupAssignments.FirstOrDefault(g => 
                !IsRateLimited(g.DailyRequestsCount, g.MaxDailyRequests) &&
                !IsRateLimited(g.MonthlyRequestsCount, g.MaxMonthlyRequests) &&
                !IsRateLimited(g.TotalRequestsCount, g.MaxTotalRequests));

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

    public async Task RecordUsageAsync(long configId, string userId, int inputTokens, int outputTokens)
    {
        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(configId);
        if (config == null) return;
        
        var now = DateTime.UtcNow;
        ResetCountersIfNeeded(config, now);

        config.DailyRequestsCount++;
        config.MonthlyRequestsCount++;
        config.TotalRequestsCount++;

        // User specific limit
        var userAssignment = await _dbContext.UserSystemAiApiConfigurations
            .FirstOrDefaultAsync(u => u.SystemAiApiConfigurationId == configId && u.AspNetUserId == userId);
        if (userAssignment != null && userAssignment.IsEnabled)
        {
            ResetCountersIfNeeded(userAssignment, now);
            userAssignment.DailyRequestsCount++;
            userAssignment.MonthlyRequestsCount++;
            userAssignment.TotalRequestsCount++;
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
            g.DailyRequestsCount++;
            g.MonthlyRequestsCount++;
            g.TotalRequestsCount++;
        }

        var log = new SystemAiUsageLog
        {
            SystemAiApiConfigurationId = configId,
            AspNetUserId = userId,
            TimestampUtc = now,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            ModelId = config.ModelId,
            Provider = config.Provider
        };
        _dbContext.SystemAiUsageLogs.Add(log);

        await _dbContext.SaveChangesAsync();
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

    private void ResetCountersIfNeeded(IRateLimitedEntity entity, DateTime now)
    {
        if (entity.LastDailyReset == null || entity.LastDailyReset.Value.Date < now.Date)
        {
            entity.DailyRequestsCount = 0;
            entity.LastDailyReset = now;
        }

        if (entity.LastMonthlyReset == null || entity.LastMonthlyReset.Value.Year < now.Year || entity.LastMonthlyReset.Value.Month < now.Month)
        {
            entity.MonthlyRequestsCount = 0;
            entity.LastMonthlyReset = now;
        }
    }
}
