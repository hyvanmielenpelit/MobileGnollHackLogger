using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MobileGnollHackLogger.Data;
using Overseer.Extensions;
using Overseer.Models;

namespace Overseer.Services.Security;

/// <summary>
/// Per-user daily ceilings on operator-funded AI use, counted from the user's system usage rows since 00:00 UTC
/// in the chat (1), title (2) and sub-agent (3) roles. GnollBench roles (4-8) are not counted, as they are not
/// by the configuration quotas. Administrators are exempt while <see cref="AiUsageGuardOptions.ExemptAdmins"/> is set.
/// </summary>
public class AiUsageGuard
{
    internal const string RefusalMessage = "You have reached today's limit for AI use in the Overseer. It resets at 00:00 UTC.";

    private readonly ApplicationDbContext _db;
    private readonly IOptions<AiUsageGuardOptions> _options;
    private readonly IConfiguration _configuration;
    private readonly ISecurityEventRecorder? _securityEvents;
    private readonly ILogger<AiUsageGuard>? _logger;

    public AiUsageGuard(ApplicationDbContext db, IOptions<AiUsageGuardOptions> options, IConfiguration configuration,
        ISecurityEventRecorder? securityEvents = null, ILogger<AiUsageGuard>? logger = null)
    {
        _db = db;
        _options = options;
        _configuration = configuration;
        _securityEvents = securityEvents;
        _logger = logger;
    }

    /// <summary>
    /// Null when <paramref name="userId"/> may make another operator-funded call today, or the refusal message.
    /// A refusal is recorded as a <see cref="SecurityEventKind.QuotaRefused"/> event coded <c>guard-cost</c> or
    /// <c>guard-runs</c>. A ceiling of 0 is off; unpriced rows add nothing to the cost but still count as runs.
    /// </summary>
    public async Task<string?> CheckAsync(string userId, long? systemConfigId, string? provider, string? modelId, CancellationToken ct = default)
    {
        var options = _options.Value;
        if (string.IsNullOrEmpty(userId) || (options.MaxUserDailyCostUsd <= 0 && options.MaxUserDailyRuns <= 0))
        {
            return null;
        }

        if (options.ExemptAdmins)
        {
            var userName = await _db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.UserName)
                .FirstOrDefaultAsync(ct);
            if (_configuration.IsAdmin(userName))
            {
                return null;
            }
        }

        var dayStart = DateTime.UtcNow.Date;
        var today = _db.SystemAiUsageLogs.Where(l => l.AspNetUserId == userId && l.TimestampUtc >= dayStart
            && (l.RoleContext == 1 || l.RoleContext == 2 || l.RoleContext == 3));

        if (options.MaxUserDailyCostUsd > 0)
        {
            decimal spent = await today.SumAsync(l => l.EstimatedCost ?? 0m, ct);
            if (spent >= options.MaxUserDailyCostUsd)
            {
                return Refuse("guard-cost", userId, systemConfigId, provider, modelId);
            }
        }

        if (options.MaxUserDailyRuns > 0)
        {
            int runs = await today.CountAsync(l => l.RoleContext == 1 || l.RoleContext == 3, ct);
            if (runs >= options.MaxUserDailyRuns)
            {
                return Refuse("guard-runs", userId, systemConfigId, provider, modelId);
            }
        }

        return null;
    }

    private string Refuse(string code, string userId, long? systemConfigId, string? provider, string? modelId)
    {
        _securityEvents?.Record(new SecurityEventEntry(SecurityEventKind.QuotaRefused, UserId: userId,
            SystemAiApiConfigurationId: systemConfigId, Provider: provider, ModelId: modelId, Code: code));
        _logger?.LogInformation("AI usage guard refused user {UserId} ({Code}) for {Provider}/{ModelId}.", userId, code, provider, modelId);
        return RefusalMessage;
    }
}
