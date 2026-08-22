using Microsoft.Extensions.DependencyInjection;
using Overseer.Models;

namespace Overseer.Services;

public class ConfigHealthService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ILogger<ConfigHealthService>? _logger;

    public ConfigHealthService(IConfiguration configuration)
        : this(configuration, null, null)
    {
    }

    public ConfigHealthService(
        IConfiguration configuration,
        IServiceScopeFactory? scopeFactory = null,
        ILogger<ConfigHealthService>? logger = null)
    {
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
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

        if (string.IsNullOrWhiteSpace(_configuration["NetHackSourceCodePath"]))
        {
            alerts.Add(new SystemAlert
            {
                Id = "nethack-source-code-path-missing",
                Type = "warning",
                Message = "NetHack source code path is not configured. Set NetHackSourceCodePath in configuration settings."
            });
        }

        // Database Storage Health Alert
        if (_scopeFactory != null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var metricsService = scope.ServiceProvider.GetRequiredService<DatabaseStorageMetricsService>();
                var metrics = metricsService.GetStorageMetricsAsync(CancellationToken.None).GetAwaiter().GetResult();

                if (metrics.StatusLevel == "Critical")
                {
                    alerts.Add(new SystemAlert
                    {
                        Id = "db-storage-critical",
                        Type = "error",
                        Message = $"Database data file is at {metrics.UsedPercentage}% of the 10 GB limit ({metrics.AllocatedDataSizeMb:N1} MB allocated). Run database maintenance immediately."
                    });
                }
                else if (metrics.StatusLevel == "Warning")
                {
                    alerts.Add(new SystemAlert
                    {
                        Id = "db-storage-warning",
                        Type = "warning",
                        Message = $"Database data file is at {metrics.UsedPercentage}% of the 10 GB limit ({metrics.AllocatedDataSizeMb:N1} MB allocated). Maintenance is recommended."
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to evaluate database storage alert in ConfigHealthService");
            }
        }

        return alerts;
    }
}
