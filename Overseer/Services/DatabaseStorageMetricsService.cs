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

    private const string EditionCacheKey = "SqlServerEditionInfo";

    /// <summary>
    /// How long a successful edition detection is cached. An instance's edition changes
    /// only across a service restart, so an hour is generous; after an upgrade the panel
    /// corrects itself within that window, or immediately if Overseer is restarted too.
    /// </summary>
    private static readonly TimeSpan EditionCacheDuration = TimeSpan.FromHours(1);

    /// <summary>
    /// How long a failed detection is cached. Kept short so a transient connection problem
    /// does not pin the panel to the conservative fallback figure for a full hour.
    /// </summary>
    private static readonly TimeSpan EditionFailureCacheDuration = TimeSpan.FromMinutes(5);

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

        if (SqlServerCapacity.HasInvalidThresholds(_settings))
        {
            _logger.LogWarning(
                "ChatRetentionSettings storage thresholds are invalid or inverted " +
                "(Warning {WarningPercent}%, Critical {CriticalPercent}%). Out-of-range values fall back " +
                "to {DefaultWarning}%/{DefaultCritical}%, and the critical band is held at no lower than the warning band.",
                _settings.DatabaseWarningThresholdPercent,
                _settings.DatabaseCriticalThresholdPercent,
                SqlServerCapacity.DefaultWarningThresholdPercent,
                SqlServerCapacity.DefaultCriticalThresholdPercent);
        }

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

    /// <summary>Wrapper so that a cached "detection failed" result is distinguishable from a cache miss.</summary>
    private sealed record EditionCacheEntry(SqlServerEditionInfo? Info);

    /// <summary>
    /// Reads the instance's edition and version via SERVERPROPERTY, cached so that the
    /// metadata query does not run on every metrics refresh. Returns null when detection
    /// fails, which the capacity resolver reports as a "Fallback" limit rather than
    /// silently guessing.
    /// </summary>
    private async Task<SqlServerEditionInfo?> GetEditionInfoAsync(CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue(EditionCacheKey, out EditionCacheEntry? cached) && cached != null)
        {
            return cached.Info;
        }

        SqlServerEditionInfo? info = null;

        try
        {
            var conn = _dbContext.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync(cancellationToken);
            }

            using var cmd = conn.CreateCommand();
            // TRY_CAST on ProductMajorVersion: the property is NULL before SQL Server 2014 SP2.
            // The resolver falls back to parsing ProductVersion, which every version reports.
            cmd.CommandText = @"
                SELECT
                    CAST(SERVERPROPERTY('Edition') AS NVARCHAR(128)) AS Edition,
                    TRY_CAST(SERVERPROPERTY('EngineEdition') AS INT) AS EngineEdition,
                    TRY_CAST(SERVERPROPERTY('ProductMajorVersion') AS INT) AS ProductMajorVersion,
                    CAST(SERVERPROPERTY('ProductVersion') AS NVARCHAR(128)) AS ProductVersion,
                    CAST(SERVERPROPERTY('ProductLevel') AS NVARCHAR(128)) AS ProductLevel;";

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                info = new SqlServerEditionInfo(
                    ReadNullableString(reader["Edition"]),
                    ReadNullableInt(reader["EngineEdition"]),
                    ReadNullableInt(reader["ProductMajorVersion"]),
                    ReadNullableString(reader["ProductVersion"]),
                    ReadNullableString(reader["ProductLevel"]));

                _logger.LogInformation(
                    "Detected SQL Server instance: Edition={Edition}, EngineEdition={EngineEdition}, Version={ProductVersion} ({ProductLevel})",
                    info.Edition, info.EngineEdition, info.ProductVersion, info.ProductLevel);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect SQL Server edition via SERVERPROPERTY; assuming the conservative Express limit");
            info = null;
        }

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(info != null ? EditionCacheDuration : EditionFailureCacheDuration)
            .SetSize(1); // CRITICAL: SizeLimit is configured globally in Program.cs

        _memoryCache.Set(EditionCacheKey, new EditionCacheEntry(info), cacheOptions);

        return info;
    }

    private static double ReadDouble(object? value)
        => value == null || value == DBNull.Value ? 0 : Convert.ToDouble(value);

    private static int? ReadNullableInt(object? value)
        => value == null || value == DBNull.Value ? null : Convert.ToInt32(value);

    private static string? ReadNullableString(object? value)
        => value == null || value == DBNull.Value ? null : Convert.ToString(value);

    public async Task<DatabaseStorageMetricsDto> GetStorageMetricsAsync(CancellationToken cancellationToken = default)
    {
        var dto = new DatabaseStorageMetricsDto
        {
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
            // The Express cap applies to the sum of the database's data files, so this must
            // aggregate rather than read a single row. type = 0 (ROWS) already excludes the
            // log, FILESTREAM containers, and full-text files, which the cap does not govern.
            cmd.CommandText = @"
                SELECT
                    SUM(CAST(size AS BIGINT)) * 8 / 1024.0 AS AllocatedMb,
                    SUM(CAST(FILEPROPERTY(name, 'SpaceUsed') AS BIGINT)) * 8 / 1024.0 AS UsedMb
                FROM sys.database_files
                WHERE type = 0;";

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                dto.AllocatedDataSizeMb = ReadDouble(reader["AllocatedMb"]);
                dto.UsedDataSizeMb = ReadDouble(reader["UsedMb"]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query database file sizes from sys.database_files");
        }

        // 1b. Resolve the capacity ceiling from the instance edition, or from the override.
        var editionInfo = await GetEditionInfoAsync(cancellationToken);
        var capacity = SqlServerCapacity.Resolve(editionInfo, _settings.DatabaseMaxSizeMbOverride);

        dto.MaxLimitMb = capacity.MaxLimitMb;
        dto.HasEngineSizeLimit = capacity.HasEngineSizeLimit;
        dto.LimitSource = capacity.LimitSource;
        dto.ServerProductLabel = capacity.ProductLabel;
        dto.ServerEditionName = capacity.EditionName;
        dto.ServerProductVersion = capacity.ProductVersion;

        dto.FreeSpaceWithinLimitMb = capacity.MaxLimitMb > 0
            ? Math.Max(0, capacity.MaxLimitMb - dto.AllocatedDataSizeMb)
            : 0;
        dto.UsedPercentage = capacity.MaxLimitMb > 0
            ? Math.Round((dto.AllocatedDataSizeMb / capacity.MaxLimitMb) * 100.0, 1)
            : 0;

        dto.StatusLevel = SqlServerCapacity.ResolveStatusLevel(dto.AllocatedDataSizeMb, capacity, _settings);

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

        var throttleOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromHours(24))
            .SetSize(1); // CRITICAL: SizeLimit is configured globally in Program.cs

        _memoryCache.Set(cacheKey, true, throttleOptions);
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
        string productLabel = string.IsNullOrWhiteSpace(metrics.ServerProductLabel) ? "SQL Server" : metrics.ServerProductLabel;
        bool hasLimit = metrics.MaxLimitMb > 0;

        string subject = hasLimit
            ? $"[Overseer Storage {alertType.ToUpperInvariant()}] {productLabel} at {metrics.UsedPercentage}% capacity"
            : $"[Overseer Storage {alertType.ToUpperInvariant()}] {productLabel} at {metrics.AllocatedDataSizeMb:N0} MB allocated";

        var sb = new StringBuilder();
        sb.AppendLine("<html><body style='font-family: Arial, sans-serif; color: #333;'>");
        sb.AppendLine($"<h2 style='color: {(alertType == "Critical" ? "#dc3545" : "#ffc107")};'>Overseer Database Storage {alertType} Report</h2>");

        if (hasLimit)
        {
            string limitWord = metrics.HasEngineSizeLimit ? "limit" : "configured budget";
            sb.AppendLine($"<p>The shared {productLabel} database has reached <strong>{metrics.AllocatedDataSizeMb:N1} MB ({metrics.UsedPercentage}%)</strong> of the {metrics.MaxLimitMb:N0} MB {limitWord}.</p>");
        }
        else
        {
            sb.AppendLine($"<p>The shared {productLabel} database has reached <strong>{metrics.AllocatedDataSizeMb:N1} MB</strong>. This edition imposes no per-database size limit.</p>");
        }

        sb.AppendLine("<table border='1' cellpadding='6' cellspacing='0' style='border-collapse: collapse; margin-top: 15px;'>");
        sb.AppendLine("<tr style='background: #f2f2f2;'><th>Metric</th><th>Value</th></tr>");
        sb.AppendLine($"<tr><td><strong>Allocated DB Size</strong></td><td>{metrics.AllocatedDataSizeMb:N1} MB</td></tr>");
        if (hasLimit)
        {
            sb.AppendLine($"<tr><td><strong>Free Headroom (below {metrics.MaxLimitMb / 1024.0:N0} GB)</strong></td><td>{metrics.FreeSpaceWithinLimitMb:N1} MB</td></tr>");
        }
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
