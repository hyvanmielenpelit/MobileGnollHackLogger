namespace Overseer.Services;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;

public class ParallelExecutionResolver
{
    private readonly IConfiguration _configuration;

    public ParallelExecutionResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public ParallelExecutionMode Resolve(SystemAiApiConfiguration? systemConfig, UserAiApiKey? userApiKey)
    {
        bool enforcePerKey = _configuration.GetValue<bool>("ParallelExecutionSettings:EnforcePerKeyMode", true);
        if (!enforcePerKey)
        {
            return ParallelExecutionMode.Enabled;
        }

        if (systemConfig != null)
        {
            return systemConfig.ParallelExecutionMode;
        }

        if (userApiKey != null)
        {
            return userApiKey.ParallelExecutionMode;
        }

        int defaultMode = _configuration.GetValue<int>("ParallelExecutionSettings:DefaultMode", (int)ParallelExecutionMode.Enabled);
        return (ParallelExecutionMode)defaultMode;
    }

    public async Task<ParallelExecutionMode> ResolveAsync(string? userId, string? provider, long? systemModelId, ApplicationDbContext dbContext, CancellationToken ct = default)
    {
        bool enforcePerKey = _configuration.GetValue<bool>("ParallelExecutionSettings:EnforcePerKeyMode", true);
        if (!enforcePerKey)
        {
            return ParallelExecutionMode.Enabled;
        }

        if (systemModelId.HasValue)
        {
            var config = await dbContext.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == systemModelId.Value, ct);
            if (config != null)
            {
                return config.ParallelExecutionMode;
            }
        }
        else if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(provider))
        {
            var userKey = await dbContext.UserAiApiKeys.FirstOrDefaultAsync(k => k.AspNetUserId == userId && k.Provider == provider, ct);
            if (userKey != null)
            {
                return userKey.ParallelExecutionMode;
            }
        }

        int defaultMode = _configuration.GetValue<int>("ParallelExecutionSettings:DefaultMode", (int)ParallelExecutionMode.Enabled);
        return (ParallelExecutionMode)defaultMode;
    }
}
