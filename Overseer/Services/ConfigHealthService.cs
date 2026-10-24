using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Overseer.Extensions;
using Overseer.Models;
using Overseer.Services.ConfigHealth;
using Overseer.Services.SourceIndexing;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Overseer.Services;

public class ConfigHealthService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ILogger<ConfigHealthService>? _logger;
    private readonly IOptions<AiUsageGuardOptions>? _usageGuardOptions;
    private readonly IHostEnvironment? _environment;

    public ConfigHealthService(IConfiguration configuration)
        : this(configuration, null, null)
    {
    }

    public ConfigHealthService(
        IConfiguration configuration,
        IServiceScopeFactory? scopeFactory = null,
        ILogger<ConfigHealthService>? logger = null,
        IOptions<AiUsageGuardOptions>? usageGuardOptions = null,
        IHostEnvironment? environment = null)
    {
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _usageGuardOptions = usageGuardOptions;
        _environment = environment;
    }

    /// <summary>
    /// Every catalog check of the configuration, with the provider each value comes from. A sensitive
    /// key's value is never included; computed per call, with no database query.
    /// </summary>
    public ConfigHealthReportDto GetHealthReport()
    {
        var sources = new ConfigurationSourceInspector(_configuration);
        var context = new ConfigHealthContext
        {
            Configuration = _configuration,
            Sources = sources,
            Makedefs = GetMakedefsState(),
            KeyRing = ResolveKeyRing(),
            LegalDocuments = ResolveLegalDocumentReader()
        };

        var checks = ConfigHealthCatalog.Evaluate(context, _logger);

        return new ConfigHealthReportDto
        {
            GeneratedAtUtc = DateTime.UtcNow,
            EnvironmentName = _environment?.EnvironmentName,
            UserSecretsExpected = _environment?.IsDevelopment() == true,
            Sources = sources.ListSources(),
            Summary = new ConfigHealthSummaryDto
            {
                Ok = checks.Count(c => c.Status == ConfigHealthStatus.Ok),
                Warning = checks.Count(c => c.Status == ConfigHealthStatus.Warning),
                Error = checks.Count(c => c.Status == ConfigHealthStatus.Error),
                Info = checks.Count(c => c.Status == ConfigHealthStatus.Info)
            },
            Checks = checks
        };
    }

    /// <summary>
    /// The GnollHack source service's last makedefs outcome and its missing generated headers, or null
    /// when that service cannot be resolved.
    /// </summary>
    protected virtual MakedefsHealthState? GetMakedefsState()
    {
        if (_scopeFactory == null)
        {
            return null;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sourceCode = scope.ServiceProvider.GetService<SourceCodeService>();
            if (sourceCode == null)
            {
                return null;
            }

            var outcome = sourceCode.LastMakedefsOutcome;
            return new MakedefsHealthState(outcome?.Kind, outcome?.Message, outcome?.FinishedAtUtc, sourceCode.GetMissingGeneratedHeaders());
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read the makedefs outcome in ConfigHealthService");
            return null;
        }
    }

    // The registered keyring when there is one, else one read from the same configuration.
    private Privacy.ConfigurationContentKeyRing? ResolveKeyRing()
    {
        try
        {
            if (_scopeFactory != null)
            {
                using var scope = _scopeFactory.CreateScope();
                var registered = scope.ServiceProvider.GetService<Privacy.ConfigurationContentKeyRing>();
                if (registered != null)
                {
                    return registered;
                }
            }

            return new Privacy.ConfigurationContentKeyRing(_configuration);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to evaluate the content keyring in ConfigHealthService");
            return null;
        }
    }

    private GnollHackServer.Data.Legal.LegalDocumentReader? ResolveLegalDocumentReader()
    {
        try
        {
            if (_scopeFactory == null)
            {
                return null;
            }

            using var scope = _scopeFactory.CreateScope();
            return scope.ServiceProvider.GetService<GnollHackServer.Data.Legal.LegalDocumentReader>();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to resolve the Terms document reader in ConfigHealthService");
            return null;
        }
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

        if (string.IsNullOrWhiteSpace(_configuration["SourceCodePath"]))
        {
            alerts.Add(new SystemAlert
            {
                Id = "source-code-path-missing",
                Type = "warning",
                Message = "GnollHack source code path is not configured. Set SourceCodePath in configuration settings."
            });
        }

        /* A failed makedefs matters while a generated header is missing: the index then lacks the
           monster and object constants. A failure over headers still on disk raises nothing. */
        var makedefs = GetMakedefsState();
        if (makedefs != null
            && (makedefs.LastKind == MakedefsOutcomeKind.BuildFailed || makedefs.LastKind == MakedefsOutcomeKind.RunFailed)
            && makedefs.MissingHeaders.Count > 0)
        {
            alerts.Add(new SystemAlert
            {
                Id = "gnollhack-makedefs-failed",
                Type = "error",
                Message = "makedefs could not generate the GnollHack headers (pm.h, onames.h), so the chat cannot see monster and object constants.",
                LinkUrl = "/admin/services",
                LinkText = "Open GnollHack Source"
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

        if (!Benchmarking.BenchmarkReportChartStore.IsUsableRoot(_configuration[Benchmarking.BenchmarkReportChartStore.ConfigurationKey]))
        {
            alerts.Add(new SystemAlert
            {
                Id = "report-charts-location-missing",
                Type = "warning",
                Message = "Report chart storage is not configured. Set Benchmark:ReportPack:ChartsDataLocation to an absolute folder; until then, charts cannot be added to report documents."
            });
        }

        /* API key failure alerts send email. Enabled is read the way ApiKeyAlertOptions reads it,
           so only an explicit false silences these warnings. */
        var apiKeyAlertOptions = new ApiKeyAlerts.ApiKeyAlertOptions(_configuration);
        if (apiKeyAlertOptions.Enabled)
        {
            if (string.IsNullOrWhiteSpace(_configuration["ConnectionStrings:EmailConnection"]))
            {
                alerts.Add(new SystemAlert
                {
                    Id = "api-key-alert-email-missing",
                    Type = "warning",
                    Message = "API key failure alerts are enabled but no email connection is configured; set ConnectionStrings:EmailConnection in User Secrets."
                });
            }

            if (string.IsNullOrWhiteSpace(apiKeyAlertOptions.RecipientEmail))
            {
                alerts.Add(new SystemAlert
                {
                    Id = "api-key-alert-recipient-missing",
                    Type = "warning",
                    Message = "API key failure alerts are enabled but no recipient is configured; set ApiKeyAlerts:RecipientEmail."
                });
            }
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

        /* Retired models: one alert per retired catalog entry that enabled configurations still
           name. A custom-mode or custom-endpoint configuration is not retired, and a model the
           catalog never described raises nothing here. */
        if (_scopeFactory != null)
        {
            try
            {
                using var retiredScope = _scopeFactory.CreateScope();
                var availability = retiredScope.ServiceProvider.GetRequiredService<ModelAvailabilityService>();
                var metadata = retiredScope.ServiceProvider.GetRequiredService<ModelMetadataService>();
                var dbContext = retiredScope.ServiceProvider
                    .GetRequiredService<MobileGnollHackLogger.Data.ApplicationDbContext>();

                var enabled = dbContext.SystemAiApiConfigurations
                    .Where(c => c.IsEnabled)
                    .Select(c => new { c.Provider, c.ModelId, c.ModelCatalogMode, c.BaseUrl })
                    .ToList();

                var retiredGroups = enabled
                    .Where(c => availability.Evaluate(c.Provider, c.ModelId, c.ModelCatalogMode, c.BaseUrl).Kind
                        == ModelAvailabilityStatus.Retired)
                    .Select(c => metadata.GetRetiredEntry(c.Provider, c.ModelId))
                    .Where(entry => entry != null && entry.Prefixes != null && entry.Prefixes.Count > 0)
                    .GroupBy(entry => entry!)
                    .OrderBy(g => g.Key.RetiredOn, StringComparer.Ordinal)
                    .ThenBy(g => g.Key.DisplayName, StringComparer.Ordinal);

                foreach (var group in retiredGroups)
                {
                    var entry = group.Key;
                    int count = group.Count();
                    string firstPrefix = entry.Prefixes[0];
                    string subject = count == 1
                        ? "1 system configuration uses"
                        : $"{count} system configurations use";

                    alerts.Add(new SystemAlert
                    {
                        Id = $"retired-model-{entry.Provider}-{firstPrefix}",
                        Type = "warning",
                        Message = $"{subject} {entry.DisplayName} ({firstPrefix}), which was removed from the model catalog on {entry.RetiredOn}.",
                        LinkUrl = "/admin/configs",
                        LinkText = "Review in System Configs"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to evaluate retired model alerts in ConfigHealthService");
            }
        }

        /* An approved account deletion erases files under both paths, which must equal the website's;
           Approve is refused while either is missing. */
        var missingErasurePaths = new[] { "DumpLogPath", "ConversationsDataLocation" }
            .Where(key => string.IsNullOrWhiteSpace(_configuration[key]))
            .ToList();
        if (missingErasurePaths.Count > 0)
        {
            alerts.Add(new SystemAlert
            {
                Id = "account-erasure-paths-missing",
                Type = "warning",
                Message = $"{string.Join(" and ", missingErasurePaths)} {(missingErasurePaths.Count == 1 ? "is" : "are")} not configured. Account deletions held for review cannot be approved until Overseer has the same value as the website."
            });
        }

        // Held account deletions wait for an administrator's decision.
        if (_scopeFactory != null)
        {
            try
            {
                using var holdScope = _scopeFactory.CreateScope();
                var dbContext = holdScope.ServiceProvider.GetRequiredService<MobileGnollHackLogger.Data.ApplicationDbContext>();
                var holdOptions = holdScope.ServiceProvider.GetService<GnollHackServer.Data.Security.AccountHoldOptions>()
                    ?? new GnollHackServer.Data.Security.AccountHoldOptions();
                var (pending, overdue, newestId) = Security.AccountDeletionReviewService
                    .CountPendingAsync(dbContext, holdOptions, DateTime.UtcNow).GetAwaiter().GetResult();

                if (pending > 0)
                {
                    string subject = pending == 1 ? "1 account deletion awaits review" : $"{pending} account deletions await review";
                    alerts.Add(new SystemAlert
                    {
                        // The newest request's id is in the id, so a dismissed alert reappears for a new request.
                        Id = $"account-deletion-review:{newestId}",
                        Type = overdue > 0 ? "error" : "warning",
                        Message = overdue > 0 ? $"{subject} ({overdue} overdue)." : $"{subject}.",
                        LinkUrl = "/admin/users/deletions",
                        LinkText = "Review"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to evaluate account deletion review alerts in ConfigHealthService");
            }
        }

        /* Operator-funded spend per user over the last 24 hours (chat, title and sub-agent rows), administrators
           excluded. It only alerts; the usage guard is what limits. */
        decimal spendThreshold = _usageGuardOptions?.Value.AlertUserDailyCostUsd ?? 0m;
        if (_scopeFactory != null && spendThreshold > 0)
        {
            try
            {
                using var spendScope = _scopeFactory.CreateScope();
                var dbContext = spendScope.ServiceProvider.GetRequiredService<MobileGnollHackLogger.Data.ApplicationDbContext>();
                var now = DateTime.UtcNow;
                var since = now.AddHours(-24);

                var spenders = dbContext.SystemAiUsageLogs
                    .Where(l => l.TimestampUtc >= since && (l.RoleContext == 1 || l.RoleContext == 2 || l.RoleContext == 3))
                    .GroupBy(l => new { l.AspNetUserId, l.AspNetUser.UserName })
                    .Select(g => new { g.Key.UserName, Total = g.Sum(l => l.EstimatedCost ?? 0m) })
                    .Where(s => s.Total >= spendThreshold)
                    .ToList()
                    .Where(s => !_configuration.IsAdmin(s.UserName))
                    .ToList();

                if (spenders.Count > 0)
                {
                    var money = System.Globalization.CultureInfo.InvariantCulture;
                    decimal highest = spenders.Max(s => s.Total);
                    string subject = spenders.Count == 1 ? "1 user" : $"{spenders.Count} users";
                    alerts.Add(new SystemAlert
                    {
                        // The date and count are in the id, so a dismissed alert reappears on a new day or a higher count.
                        Id = $"ai-spend:{now.ToString("yyyyMMdd", money)}:{spenders.Count}",
                        Type = "warning",
                        Message = $"{subject} spent over ${spendThreshold.ToString("0.00", money)} on AI in the last 24 hours (highest: ${highest.ToString("0.00", money)}).",
                        LinkUrl = "/admin/telemetry/users",
                        LinkText = "Review"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to evaluate AI spend alerts in ConfigHealthService");
            }
        }

        AddSnapshotGuardAlerts(alerts);

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

    /// <summary>
    /// The snapshot guard's mode while it is not Enforce, game snapshots refused in the last 7 days, and users it
    /// suspended in the last 7 days. The changing part of each id makes a dismissed alert reappear on new events.
    /// </summary>
    private void AddSnapshotGuardAlerts(List<SystemAlert> alerts)
    {
        try
        {
            SnapshotGuardMode mode;
            if (_scopeFactory != null)
            {
                using var modeScope = _scopeFactory.CreateScope();
                var modeService = modeScope.ServiceProvider.GetService<Security.SnapshotGuardModeService>();
                mode = modeService?.Mode() ?? SnapshotGuardOptions.Read(_configuration).Mode;
            }
            else
            {
                mode = SnapshotGuardOptions.Read(_configuration).Mode;
            }

            if (mode == SnapshotGuardMode.Observe)
            {
                alerts.Add(new SystemAlert
                {
                    Id = "snapshot-guard-observe",
                    Type = "warning",
                    Message = "The snapshot guard is observing only: game text addressed to the AI is recorded but still delivered to the model.",
                    LinkUrl = "/admin/security/settings",
                    LinkText = "Change"
                });
            }
            else if (mode == SnapshotGuardMode.Off)
            {
                alerts.Add(new SystemAlert
                {
                    Id = "snapshot-guard-off",
                    Type = "warning",
                    Message = "The snapshot guard is off: game text addressed to the AI is delivered to the model unchecked.",
                    LinkUrl = "/admin/security/settings",
                    LinkText = "Change"
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to evaluate the snapshot guard mode alert in ConfigHealthService");
        }

        if (_scopeFactory == null) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MobileGnollHackLogger.Data.ApplicationDbContext>();
            var now = DateTime.UtcNow;
            var since = now.AddDays(-7);
            var invariant = System.Globalization.CultureInfo.InvariantCulture;

            var refusals = dbContext.SecurityEvents.AsNoTracking()
                .Where(e => e.Kind == MobileGnollHackLogger.Data.SecurityEventKind.SnapshotRejected && e.LastOccurredUtc >= since)
                .Select(e => new { e.Count, e.AspNetUserId })
                .ToList();
            if (refusals.Count > 0)
            {
                int count = refusals.Sum(r => r.Count);
                int users = refusals.Select(r => r.AspNetUserId).Distinct().Count();
                alerts.Add(new SystemAlert
                {
                    Id = $"snapshot-refusals:{now.ToString("yyyyMMdd", invariant)}:{count.ToString(invariant)}",
                    Type = "warning",
                    Message = $"{(count == 1 ? "1 game snapshot was" : $"{count} game snapshots were")} refused for text addressed to the AI in the last 7 days ({(users == 1 ? "1 user" : $"{users} users")}).",
                    LinkUrl = "/admin/security/events",
                    LinkText = "Review"
                });
            }

            var blocks = dbContext.SecurityEvents.AsNoTracking()
                .Where(e => e.Kind == MobileGnollHackLogger.Data.SecurityEventKind.AutoBlocked && e.OccurredUtc >= since)
                .Select(e => new { e.Id, e.AspNetUserId })
                .ToList();
            if (blocks.Count > 0)
            {
                int users = blocks.Select(b => b.AspNetUserId).Distinct().Count();
                alerts.Add(new SystemAlert
                {
                    Id = $"snapshot-autoblock:{blocks.Max(b => b.Id).ToString(invariant)}",
                    Type = "error",
                    Message = $"{(users == 1 ? "1 user was" : $"{users} users were")} suspended from the AI chat automatically for repeated game text addressed to the AI.",
                    LinkUrl = "/admin/security/events",
                    LinkText = "Review"
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to evaluate snapshot guard alerts in ConfigHealthService");
        }
    }
}
