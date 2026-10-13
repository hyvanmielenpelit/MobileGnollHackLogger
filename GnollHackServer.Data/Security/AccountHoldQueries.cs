using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace GnollHackServer.Data.Security;

/// <summary>What <see cref="AccountHoldEvaluator"/> reads about one account.</summary>
public sealed record AccountHoldEvidence(AccountHold? ActiveHold, string? HoldPlacedByName, IReadOnlyList<SecurityEvent> ProviderSafetyEvents);

public static class AccountHoldQueries
{
    /// <summary>The account's active hold, and its provider safety and policy events of the window.</summary>
    public static async Task<AccountHoldEvidence> LoadAsync(ApplicationDbContext db, string userId, AccountHoldOptions options,
        DateTime utcNow, CancellationToken ct = default)
    {
        var hold = await db.AccountHolds.AsNoTracking()
            .Where(h => h.AspNetUserId == userId && h.ReleasedUtc == null)
            .OrderByDescending(h => h.PlacedUtc)
            .FirstOrDefaultAsync(ct);

        string? placedBy = null;
        if (hold != null)
        {
            placedBy = await db.Users.AsNoTracking()
                .Where(u => u.Id == hold.PlacedByUserId)
                .Select(u => u.UserName)
                .FirstOrDefaultAsync(ct);
        }

        DateTime windowStart = utcNow.AddDays(-options.AutoHoldWindowDays);
        var events = await db.SecurityEvents.AsNoTracking()
            .Where(e => e.AspNetUserId == userId
                && (e.Kind == SecurityEventKind.ProviderSafetyBlock || e.Kind == SecurityEventKind.ProviderPolicyRejection)
                && e.LastOccurredUtc >= windowStart)
            .ToListAsync(ct);

        return new AccountHoldEvidence(hold, placedBy, events);
    }

    /// <summary>Loads the evidence and evaluates it.</summary>
    public static async Task<AccountHoldVerdict> EvaluateAsync(ApplicationDbContext db, string userId, AccountHoldOptions options,
        DateTime utcNow, CancellationToken ct = default)
    {
        var evidence = await LoadAsync(db, userId, options, utcNow, ct);
        return AccountHoldEvaluator.Evaluate(evidence.ActiveHold, evidence.ProviderSafetyEvents, options, utcNow, evidence.HoldPlacedByName);
    }

    /// <summary>Users whose security events are kept past retention: an active hold, or a pending deletion request.</summary>
    public static IQueryable<string> HeldUserIds(ApplicationDbContext db) =>
        db.AccountHolds.Where(h => h.ReleasedUtc == null).Select(h => h.AspNetUserId)
            .Concat(db.AccountDeletionRequests
                .Where(r => r.Status == AccountDeletionRequestStatus.Pending && r.AspNetUserId != null)
                .Select(r => r.AspNetUserId!));
}
