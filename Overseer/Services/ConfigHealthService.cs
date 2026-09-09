using Microsoft.Extensions.DependencyInjection;
using Overseer.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;

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

        /* The content keyring. A missing or malformed ring must surface here, at startup, and
           not as an exception on a user's first confidential turn -- which is the worst
           possible moment to discover it, and would look like a bug in the chat rather than a
           configuration gap. Key material belongs in User Secrets, so only the problem is
           reported, never a value. */
        if (_scopeFactory != null)
        {
            try
            {
                using var keyRingScope = _scopeFactory.CreateScope();
                var keyRing = keyRingScope.ServiceProvider.GetRequiredService<Privacy.ConfigurationContentKeyRing>();
                if (!keyRing.IsUsable)
                {
                    alerts.Add(new SystemAlert
                    {
                        Id = "content-keyring-unusable",
                        Type = "error",
                        Message = "Confidential chats cannot store encrypted content: " + keyRing.ValidationError
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to evaluate the content keyring alert in ConfigHealthService");
            }
        }

        /* Custom endpoints. A configuration whose base URL no longer validates silently falls
           back to the provider's public endpoint (EndpointPolicy.Resolve logs it), so without
           this alert an operator would see requests succeeding against the wrong host and
           conclude the deployment was working. */
        if (_scopeFactory != null)
        {
            try
            {
                using var endpointScope = _scopeFactory.CreateScope();
                var policy = endpointScope.ServiceProvider.GetRequiredService<Privacy.EndpointPolicy>();
                var dbContext = endpointScope.ServiceProvider
                    .GetRequiredService<MobileGnollHackLogger.Data.ApplicationDbContext>();

                var configured = dbContext.SystemAiApiConfigurations
                    .Where(c => c.BaseUrl != null && c.BaseUrl != "")
                    .Select(c => new { c.Id, c.DisplayName, c.BaseUrl, c.CustomHeadersJson, c.ApiVersion })
                    .ToList();

                foreach (var c in configured)
                {
                    var result = policy.Validate(c.BaseUrl, c.CustomHeadersJson, c.ApiVersion);
                    if (!result.IsValid)
                    {
                        alerts.Add(new SystemAlert
                        {
                            Id = $"custom-endpoint-invalid-{c.Id}",
                            Type = "error",
                            Message = $"The custom endpoint on AI configuration \"{c.DisplayName}\" is not usable, "
                                + $"so its requests are going to the provider's public endpoint instead: {result.Error}"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to evaluate custom endpoint alerts in ConfigHealthService");
            }
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
