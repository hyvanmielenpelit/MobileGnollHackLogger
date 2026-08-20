using Overseer.Models;

namespace Overseer.Services;

public class ConfigHealthService
{
    private readonly IConfiguration _configuration;

    public ConfigHealthService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IEnumerable<SystemAlert> GetSystemAlerts()
    {
        var alerts = new List<SystemAlert>();

        if (string.IsNullOrWhiteSpace(_configuration["SentryDSN"]))
        {
            alerts.Add(new SystemAlert
            {
                Id = "sentry-dsn-missing",
                Type = "warning",
                Message = "Sentry DSN is not configured. Set SentryDSN in configuration settings."
            });
        }

        if (string.IsNullOrWhiteSpace(_configuration["NetHackWikiPath"]))
        {
            alerts.Add(new SystemAlert
            {
                Id = "nethack-wiki-path-missing",
                Type = "warning",
                Message = "NetHack Wiki path is not configured. Set NetHackWikiPath in configuration settings."
            });
        }

        return alerts;
    }
}
