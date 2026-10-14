namespace Overseer.Services;

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
}
