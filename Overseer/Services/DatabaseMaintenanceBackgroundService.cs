using Microsoft.Extensions.Hosting;
using Overseer.Models;

namespace Overseer.Services;

public class DatabaseMaintenanceBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseMaintenanceBackgroundService> _logger;
    private readonly ChatRetentionSettings _settings;

    public DatabaseMaintenanceBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DatabaseMaintenanceBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
        
        _settings = new ChatRetentionSettings();
        _configuration.GetSection("ChatRetentionSettings").Bind(_settings);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DatabaseMaintenanceBackgroundService started. Scheduled run hour: {Hour}:00 UTC", _settings.MaintenanceRunHourUtc);

        // Initial warm-up delay (60 seconds) after app starts
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            await RunMaintenancePassAsync(MaintenanceTriggers.Startup, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during initial database maintenance pass");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var nextRun = DatabaseStorageMetricsService.ComputeNextRunUtc(now, _settings.MaintenanceRunHourUtc);

                var delay = nextRun - now;
                _logger.LogInformation("Next database maintenance pass scheduled at {NextRun} UTC (in {Hours:N1} hours)", nextRun, delay.TotalHours);

                await Task.Delay(delay, stoppingToken);

                await RunMaintenancePassAsync(MaintenanceTriggers.Scheduled, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in DatabaseMaintenanceBackgroundService loop");
                // Wait 1 hour before retrying on crash
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    private async Task RunMaintenancePassAsync(string trigger, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting {Trigger} database maintenance pass...", trigger);

        using var scope = _scopeFactory.CreateScope();
        var retentionService = scope.ServiceProvider.GetRequiredService<ChatRetentionService>();
        var metricsService = scope.ServiceProvider.GetRequiredService<DatabaseStorageMetricsService>();

        var result = await retentionService.RunFullMaintenanceAsync(new MaintenanceRequestDto { DryRun = false }, trigger, stoppingToken);
        _logger.LogInformation("Daily maintenance completed in {Elapsed} ms. Purged {Sessions} sessions, {Messages} messages, deleted {Folders} disk folders.",
            result.ElapsedMilliseconds, result.PurgedSessionCount, result.PurgedMessageCount, result.DeletedDiskFolderCount);

        // Check storage capacity and send warning email if threshold exceeded
        await metricsService.CheckAndSendAlertEmailIfNeededAsync(stoppingToken);
    }
}
