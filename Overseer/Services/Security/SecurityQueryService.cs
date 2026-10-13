using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;

namespace Overseer.Services.Security;

/// <summary>Reads the security event log for the Admin page's Security tab.</summary>
public class SecurityQueryService
{
    private readonly ApplicationDbContext _db;

    public SecurityQueryService(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>Events whose run overlaps the range: last occurrence on or after the start, first on or before the end day.</summary>
    private IQueryable<SecurityEvent> InRange(DateTime? start, DateTime? end)
    {
        var query = _db.SecurityEvents.AsNoTracking();
        if (start.HasValue)
        {
            DateTime from = start.Value.Date;
            query = query.Where(e => e.LastOccurredUtc >= from);
        }
        if (end.HasValue)
        {
            DateTime to = end.Value.Date.AddDays(1);
            query = query.Where(e => e.OccurredUtc < to);
        }
        return query;
    }

    public async Task<SecuritySummaryDto> GetSummaryAsync(DateTime? start, DateTime? end, CancellationToken ct = default)
    {
        var rows = await InRange(start, end)
            .Select(e => new { e.Kind, e.Count, e.AspNetUserId, e.IpAddress })
            .ToListAsync(ct);

        var kinds = rows
            .GroupBy(r => r.Kind)
            .Select(g => new SecurityKindCountDto
            {
                Kind = g.Key.ToString(),
                Count = g.Sum(r => (long)r.Count),
                DistinctUsers = g.Where(r => r.AspNetUserId != null).Select(r => r.AspNetUserId).Distinct().Count(),
                DistinctIps = g.Where(r => r.IpAddress != null).Select(r => r.IpAddress).Distinct().Count()
            })
            .OrderBy(k => k.Kind, StringComparer.Ordinal)
            .ToList();

        return new SecuritySummaryDto
        {
            Kinds = kinds,
            PendingDeletionRequests = await _db.AccountDeletionRequests.CountAsync(r => r.Status == AccountDeletionRequestStatus.Pending, ct),
            ActiveAccountHolds = await _db.AccountHolds.CountAsync(h => h.ReleasedUtc == null, ct)
        };
    }

    /// <param name="kinds">Comma-separated <see cref="SecurityEventKind"/> names; unknown names are ignored.</param>
    /// <param name="search">Matches user names.</param>
    public async Task<SecurityEventsPageDto> GetEventsAsync(DateTime? start, DateTime? end, string? kinds, string? userId,
        string? search, int skip, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take <= 0 ? 20 : take, 1, 100);
        skip = Math.Max(0, skip);

        var query = InRange(start, end);

        var kindFilter = (kinds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => Enum.TryParse<SecurityEventKind>(k, ignoreCase: true, out var kind) ? (SecurityEventKind?)kind : null)
            .Where(k => k.HasValue)
            .Select(k => k!.Value)
            .ToList();
        if (kindFilter.Count > 0)
        {
            query = query.Where(e => kindFilter.Contains(e.Kind));
        }

        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(e => e.AspNetUserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            var matchingUsers = _db.Users.Where(u => u.UserName != null && u.UserName.Contains(term)).Select(u => u.Id);
            query = query.Where(e => e.AspNetUserId != null && matchingUsers.Contains(e.AspNetUserId));
        }

        int total = await query.CountAsync(ct);
        var page = await query
            .OrderByDescending(e => e.LastOccurredUtc)
            .ThenByDescending(e => e.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var userIds = page.Where(e => e.AspNetUserId != null).Select(e => e.AspNetUserId!).Distinct().ToList();
        var users = await _db.Users.OfType<ApplicationUser>().AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName, Blocked = u.OverseerBlockedUtc != null })
            .ToDictionaryAsync(u => u.Id, ct);

        return new SecurityEventsPageDto
        {
            TotalCount = total,
            Rows = page.Select(e =>
            {
                var user = e.AspNetUserId != null && users.TryGetValue(e.AspNetUserId, out var u) ? u : null;
                return new SecurityEventDto
                {
                    Id = e.Id,
                    OccurredUtc = DateTime.SpecifyKind(e.OccurredUtc, DateTimeKind.Utc),
                    LastOccurredUtc = DateTime.SpecifyKind(e.LastOccurredUtc, DateTimeKind.Utc),
                    Kind = e.Kind.ToString(),
                    Count = e.Count,
                    UserId = e.AspNetUserId,
                    UserName = user?.UserName,
                    UserIsOverseerBlocked = user?.Blocked ?? false,
                    IpAddress = e.IpAddress,
                    SystemAiApiConfigurationId = e.SystemAiApiConfigurationId,
                    Provider = e.Provider,
                    ModelId = e.ModelId,
                    Code = e.Code,
                    Detail = e.Detail
                };
            }).ToList()
        };
    }
}
