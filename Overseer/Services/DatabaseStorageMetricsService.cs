using System.Data;
using System.Data.Common;
using System.Text;
using Azure.Communication.Email;
using GnollHackServer.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MobileGnollHackLogger.Data;
using Overseer.Models;

namespace Overseer.Services;

public class DatabaseStorageMetricsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<DatabaseStorageMetricsService> _logger;
    private readonly EmailSender? _emailSender;
    private readonly ChatRetentionSettings _settings;

    private static DateTime? _lastMaintenanceRunUtc;

    public DatabaseStorageMetricsService(
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        IMemoryCache memoryCache,
        ILogger<DatabaseStorageMetricsService> logger,
        IServiceProvider serviceProvider)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _memoryCache = memoryCache;
        _logger = logger;
        
        _settings = new ChatRetentionSettings();
        _configuration.GetSection("ChatRetentionSettings").Bind(_settings);

        try
        {
            _emailSender = serviceProvider.GetService<EmailSender>();
        }
        catch
        {
            _emailSender = null;
        }
    }

    public static void RecordMaintenanceRun()
    {
        _lastMaintenanceRunUtc = DateTime.UtcNow;
    }

    public async Task<DatabaseStorageMetricsDto> GetStorageMetricsAsync(CancellationToken cancellationToken = default)
    {
        var dto = new DatabaseStorageMetricsDto
        {
            MaxLimitMb = 10240,
            LastMaintenanceRunUtc = _lastMaintenanceRunUtc
        };

        // 1. Query Database File Size via DMVs
        try
        {
            var conn = _dbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync(cancellationToken);
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    CAST(size AS BIGINT) * 8 / 1024.0 AS AllocatedMb,
                    CAST(FILEPROPERTY(name, 'SpaceUsed') AS BIGINT) * 8 / 1024.0 AS UsedMb
                FROM sys.database_files
                WHERE type = 0;";

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                dto.AllocatedDataSizeMb = Convert.ToDouble(reader["AllocatedMb"]);
                dto.UsedDataSizeMb = Convert.ToDouble(reader["UsedMb"]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query database file sizes from sys.database_files");
        }

        dto.FreeSpaceWithin10GbMb = Math.Max(0, dto.MaxLimitMb - dto.AllocatedDataSizeMb);
        dto.UsedPercentage = Math.Round((dto.AllocatedDataSizeMb / dto.MaxLimitMb) * 100.0, 1);

        if (dto.AllocatedDataSizeMb >= _settings.DatabaseCriticalThresholdMb)
        {
            dto.StatusLevel = "Critical";
        }
        else if (dto.AllocatedDataSizeMb >= _settings.DatabaseWarningThresholdMb)
        {
            dto.StatusLevel = "Warning";
        }
        else
        {
            dto.StatusLevel = "Normal";
        }

        // 2. Query Table Metrics
        try
        {
            var conn = _dbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync(cancellationToken);
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    t.NAME AS TableName,
                    SUM(p.rows) AS [RowCount],
                    CAST(SUM(a.total_pages) * 8 / 1024.0 AS FLOAT) AS TotalSpaceMB,
                    CAST(SUM(a.used_pages) * 8 / 1024.0 AS FLOAT) AS UsedSpaceMB
                FROM sys.tables t
                INNER JOIN sys.indexes i ON t.OBJECT_ID = i.object_id
                INNER JOIN sys.partitions p ON i.object_id = p.OBJECT_ID AND i.index_id = p.index_id
                INNER JOIN sys.allocation_units a ON p.partition_id = a.container_id
                WHERE t.NAME IN ('ChatSession', 'ChatMessage', 'ChatMessageToolCall', 'ChatMessageAttachment', 'GameLog', 'RequestInfo', 'SystemAiUsageLog', 'Bones')
                  AND i.index_id < 2
                GROUP BY t.NAME
                ORDER BY TotalSpaceMB DESC;";

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                dto.TableMetrics.Add(new TableStorageMetricDto
                {
                    TableName = reader["TableName"].ToString() ?? "",
                    RowCount = Convert.ToInt64(reader["RowCount"]),
                    TotalSpaceMb = Math.Round(Convert.ToDouble(reader["TotalSpaceMB"]), 2),
                    UsedSpaceMb = Math.Round(Convert.ToDouble(reader["UsedSpaceMB"]), 2)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query table storage stats from sys.partitions");
        }

        // 3. Query Session Retention Stats
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-_settings.InactivityTtlDays);

            dto.ActiveSessionCount = await _dbContext.ChatSession.CountAsync(s => !s.IsDeleted, cancellationToken);
            dto.SoftDeletedSessionCount = await _dbContext.ChatSession.CountAsync(s => s.IsDeleted, cancellationToken);
            dto.InactiveSessionCount = await _dbContext.ChatSession.CountAsync(s => !s.IsDeleted && s.LastMessageUtc < cutoff, cancellationToken);
            dto.PinnedSessionCount = await _dbContext.ChatSession.CountAsync(s => s.IsPinned && !s.IsDeleted, cancellationToken);

            var chatTableSize = dto.TableMetrics.FirstOrDefault(t => t.TableName == "ChatMessage")?.TotalSpaceMb ?? 0;
            var toolCallTableSize = dto.TableMetrics.FirstOrDefault(t => t.TableName == "ChatMessageToolCall")?.TotalSpaceMb ?? 0;
            var totalChatSize = chatTableSize + toolCallTableSize;

            if (dto.ActiveSessionCount + dto.SoftDeletedSessionCount > 0)
            {
                var avgSessionMb = totalChatSize / (dto.ActiveSessionCount + dto.SoftDeletedSessionCount);
                dto.EstimatedReclaimableMb = Math.Round(avgSessionMb * dto.SoftDeletedSessionCount, 2);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query chat session counts");
        }

        // 4. Query Disk Attachments Storage
        var baseDir = _configuration["ConversationsDataLocation"];
        if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir))
        {
            try
            {
                long totalBytes = 0;
                int folderCount = 0;
                int fileCount = 0;

                var dirs = Directory.GetDirectories(baseDir);
                folderCount = dirs.Length;

                foreach (var dir in dirs)
                {
                    var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
                    fileCount += files.Length;
                    foreach (var file in files)
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            totalBytes += fi.Length;
                        }
                        catch {}
                    }
                }

                dto.DiskAttachmentsSizeBytes = totalBytes;
                dto.DiskAttachmentsSizeMb = Math.Round(totalBytes / (1024.0 * 1024.0), 2);
                dto.DiskAttachmentsFolderCount = folderCount;
                dto.DiskAttachmentsFileCount = fileCount;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to inspect ConversationsDataLocation on disk");
            }
        }

        return dto;
    }

    public async Task CheckAndSendAlertEmailIfNeededAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.EnableStorageWarningEmails)
            return;

        var metrics = await GetStorageMetricsAsync(cancellationToken);
        if (metrics.StatusLevel == "Normal")
            return;

        string cacheKey = $"LastStorageAlertEmailSent_{metrics.StatusLevel}";
        if (_memoryCache.TryGetValue(cacheKey, out _))
        {
            // Already sent an alert for this level within 24 hours
            return;
        }

        await SendStorageWarningReportEmailAsync(metrics, metrics.StatusLevel, cancellationToken);
        _memoryCache.Set(cacheKey, true, TimeSpan.FromHours(24));
    }

    public async Task<bool> SendStorageWarningReportEmailAsync(
        DatabaseStorageMetricsDto metrics, 
        string alertType, 
        CancellationToken cancellationToken = default)
    {
        if (_emailSender == null)
        {
            _logger.LogWarning("EmailSender not configured; skipping storage warning email");
            return false;
        }

        string toAddress = _configuration["ReportEmailAddress"] ?? "gnollhack@hyvanmielenpelit.fi";
        string subject = $"[Overseer Storage {alertType.ToUpperInvariant()}] SQL Server Express at {metrics.UsedPercentage}% capacity";

        var sb = new StringBuilder();
        sb.AppendLine("<html><body style='font-family: Arial, sans-serif; color: #333;'>");
        sb.AppendLine($"<h2 style='color: {(alertType == "Critical" ? "#dc3545" : "#ffc107")};'>Overseer Database Storage {alertType} Report</h2>");
        sb.AppendLine($"<p>The shared SQL Server Express database has reached <strong>{metrics.AllocatedDataSizeMb:N1} MB ({metrics.UsedPercentage}%)</strong> of the 10,240 MB limit.</p>");
        
        sb.AppendLine("<table border='1' cellpadding='6' cellspacing='0' style='border-collapse: collapse; margin-top: 15px;'>");
        sb.AppendLine("<tr style='background: #f2f2f2;'><th>Metric</th><th>Value</th></tr>");
        sb.AppendLine($"<tr><td><strong>Allocated DB Size</strong></td><td>{metrics.AllocatedDataSizeMb:N1} MB</td></tr>");
        sb.AppendLine($"<tr><td><strong>Free Headroom (<10 GB)</strong></td><td>{metrics.FreeSpaceWithin10GbMb:N1} MB</td></tr>");
        sb.AppendLine($"<tr><td><strong>Active Sessions</strong></td><td>{metrics.ActiveSessionCount}</td></tr>");
        sb.AppendLine($"<tr><td><strong>Soft-Deleted in Trash</strong></td><td>{metrics.SoftDeletedSessionCount} (est. ~{metrics.EstimatedReclaimableMb:N1} MB)</td></tr>");
        sb.AppendLine($"<tr><td><strong>Disk Attachments</strong></td><td>{metrics.DiskAttachmentsSizeMb:N1} MB ({metrics.DiskAttachmentsFileCount} files)</td></tr>");
        sb.AppendLine("</table>");

        if (metrics.TableMetrics.Count > 0)
        {
            sb.AppendLine("<h3 style='margin-top: 20px;'>Top Table Allocations</h3>");
            sb.AppendLine("<table border='1' cellpadding='6' cellspacing='0' style='border-collapse: collapse;'>");
            sb.AppendLine("<tr style='background: #f2f2f2;'><th>Table</th><th>Rows</th><th>Total MB</th></tr>");
            foreach (var tm in metrics.TableMetrics)
            {
                sb.AppendLine($"<tr><td>{tm.TableName}</td><td>{tm.RowCount:N0}</td><td>{tm.TotalSpaceMb:N1} MB</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("<p style='margin-top: 20px;'><strong>Recommended Action:</strong> Log in to the Overseer Admin Dashboard and trigger a database maintenance pass or purge expired sessions.</p>");
        sb.AppendLine("</body></html>");

        try
        {
            var emailContent = new EmailContent(subject)
            {
                Html = sb.ToString()
            };
            var emailMessage = new EmailMessage("donotreply@gnollhack.com", toAddress, emailContent);
            await _emailSender.SendAsync(Azure.WaitUntil.Started, emailMessage, cancellationToken);
            _logger.LogInformation("Dispatched storage report email to {ToAddress}", toAddress);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send storage report email to {ToAddress}", toAddress);
            return false;
        }
    }
}
