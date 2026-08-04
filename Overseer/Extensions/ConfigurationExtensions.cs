using Microsoft.Extensions.Configuration;
using System;
using System.Linq;

namespace Overseer.Extensions;

public static class ConfigurationExtensions
{
    public static bool ShouldShowDebugLog(this IConfiguration configuration, string? userName)
    {
        bool showDebugLog = configuration.GetValue<bool>("ShowDebugLog", false);
        if (!showDebugLog)
        {
            string showDebugLogForUsers = configuration.GetValue<string>("ShowDebugLogForUsers", "") ?? "";
            if (!string.IsNullOrEmpty(showDebugLogForUsers))
            {
                var users = showDebugLogForUsers.Split(',').Select(u => u.Trim()).ToList();
                if (userName != null && users.Contains(userName, StringComparer.OrdinalIgnoreCase))
                {
                    showDebugLog = true;
                }
            }
        }
        return showDebugLog;
    }
}
