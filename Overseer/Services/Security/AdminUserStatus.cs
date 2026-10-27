using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;

namespace Overseer.Services.Security;

/// <summary>A status badge of an account in the admin Users tab. Serialized by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AdminUserStatusKind
{
    GoodStanding,
    LockedOut,
    GameSignInBlocked,
    Banned,
    GameLogBanned,
    BonesBanned,
    AiChatBlocked,
    DeletionHold,
    DeletionPending,
    EmailUnconfirmed,
    Administrator,
}

/// <summary>
/// The facts about an account that live outside its user row. <paramref name="GameSignInBlockedUntil"/> is the latest
/// end of the account's active game sign-in blocks, null when there is none.
/// </summary>
public sealed record AdminUserFacts(bool HasActiveHold, bool HasPendingDeletionRequest, DateTime? GameSignInBlockedUntil);

/// <summary>The statuses of one account at one moment, with the text the admin log and the change note show.</summary>
public sealed class AdminUserStatusSet
{
    public AdminUserStatusSet(IReadOnlyList<AdminUserStatusKind> statuses, DateTimeOffset? lockedOutUntil,
        DateTimeOffset? gameSignInBlockedUntil = null)
    {
        Statuses = statuses;
        LockedOutUntil = lockedOutUntil;
        GameSignInBlockedUntil = gameSignInBlockedUntil;
    }

    public IReadOnlyList<AdminUserStatusKind> Statuses { get; }

    /// <summary>The lockout's end while <see cref="AdminUserStatusKind.LockedOut"/> is set.</summary>
    public DateTimeOffset? LockedOutUntil { get; }

    /// <summary>The latest game sign-in block's end while <see cref="AdminUserStatusKind.GameSignInBlocked"/> is set.</summary>
    public DateTimeOffset? GameSignInBlockedUntil { get; }

    public bool Has(AdminUserStatusKind kind) => Statuses.Contains(kind);

    public string Text => AdminUserStatus.Describe(this);

    /// <summary>The set of a deleted account.</summary>
    public static AdminUserStatusSet Deleted { get; } = new(Array.Empty<AdminUserStatusKind>(), null);
}

/// <summary>How the last-sign-in criterion is read.</summary>
public enum AdminLastSignInFilterKind { Never, OlderThanDays, WithinDays }

public readonly record struct AdminLastSignInFilter(AdminLastSignInFilterKind Kind, int Days);

/// <summary>
/// The criteria shared by the user list, the facet counts and the bulk wizard. Every criterion is optional; the
/// tri-state ones take "yes" or "no" (anything else empty means any). Stored as JSON with a bulk operation.
/// </summary>
public class AdminUserCriteria
{
    public const int MaxSearchLength = 256;
    public const int MaxDays = 36500;

    /// <summary>User name or email, contains, case-insensitive.</summary>
    public string? Search { get; set; }

    /// <summary>Any of these.</summary>
    public List<AdminUserStatusKind>? Statuses { get; set; }

    /// <summary>"yes" or "no".</summary>
    public string? EmailConfirmed { get; set; }

    public int? RegisteredOlderThanDays { get; set; }

    public int? RegisteredWithinDays { get; set; }

    /// <summary>"never", "olderThanDays:N" or "withinDays:N".</summary>
    public string? LastSignIn { get; set; }

    /// <summary>"yes" or "no": any game log, bones or save-file tracking row.</summary>
    public string? HasGameData { get; set; }

    /// <summary>"yes" or "no": any chat, AI usage record or benchmark attribution.</summary>
    public string? HasOverseerData { get; set; }

    /// <summary>Null when the criteria are well formed; otherwise what is wrong.</summary>
    public string? Validate()
    {
        if (Search != null && Search.Length > MaxSearchLength) return $"The search can be at most {MaxSearchLength} characters.";
        if (!IsYesNo(EmailConfirmed)) return "emailConfirmed must be yes or no.";
        if (!IsYesNo(HasGameData)) return "hasGameData must be yes or no.";
        if (!IsYesNo(HasOverseerData)) return "hasOverseerData must be yes or no.";
        if (RegisteredOlderThanDays is < 0 or > MaxDays) return $"registeredOlderThanDays must be 0 to {MaxDays}.";
        if (RegisteredWithinDays is < 0 or > MaxDays) return $"registeredWithinDays must be 0 to {MaxDays}.";
        if (!string.IsNullOrWhiteSpace(LastSignIn) && !TryParseLastSignIn(LastSignIn, out _))
            return "lastSignIn must be never, olderThanDays:N or withinDays:N.";
        return null;
    }

    /// <summary>The criteria with only the search kept: what the facet counts follow.</summary>
    public static AdminUserCriteria SearchOnly(string? search) => new() { Search = search };

    public static bool? ParseYesNo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    private static bool IsYesNo(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("any", StringComparison.OrdinalIgnoreCase) || ParseYesNo(value).HasValue;

    public static bool TryParseLastSignIn(string? value, out AdminLastSignInFilter filter)
    {
        filter = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string v = value.Trim();
        if (v.Equals("never", StringComparison.OrdinalIgnoreCase))
        {
            filter = new AdminLastSignInFilter(AdminLastSignInFilterKind.Never, 0);
            return true;
        }
        int colon = v.IndexOf(':');
        if (colon <= 0) return false;
        string name = v[..colon];
        if (!int.TryParse(v[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int days) || days > MaxDays) return false;
        if (name.Equals("olderThanDays", StringComparison.OrdinalIgnoreCase))
        {
            filter = new AdminLastSignInFilter(AdminLastSignInFilterKind.OlderThanDays, days);
            return true;
        }
        if (name.Equals("withinDays", StringComparison.OrdinalIgnoreCase))
        {
            filter = new AdminLastSignInFilter(AdminLastSignInFilterKind.WithinDays, days);
            return true;
        }
        return false;
    }
}

/// <summary>
/// The status rules of the admin Users tab, twice: <see cref="Evaluate"/> in memory for one account, and
/// <see cref="Apply"/> as SQL for a list. A test pins that the two agree.
/// </summary>
public static class AdminUserStatus
{
    /// <summary>The badge text the client shows for each status.</summary>
    public static string Label(AdminUserStatusKind kind) => kind switch
    {
        AdminUserStatusKind.GoodStanding => "In good standing",
        AdminUserStatusKind.LockedOut => "Locked out",
        AdminUserStatusKind.GameSignInBlocked => "Game sign-in blocked",
        AdminUserStatusKind.Banned => "Banned",
        AdminUserStatusKind.GameLogBanned => "Game log uploads banned",
        AdminUserStatusKind.BonesBanned => "Bones banned",
        AdminUserStatusKind.AiChatBlocked => "Blocked from AI chat",
        AdminUserStatusKind.DeletionHold => "Deletion hold",
        AdminUserStatusKind.DeletionPending => "Deletion pending",
        AdminUserStatusKind.EmailUnconfirmed => "Email not confirmed",
        AdminUserStatusKind.Administrator => "Administrator",
        _ => kind.ToString()
    };

    /// <summary>The normalized user names of the <c>Admins</c> configuration value.</summary>
    public static IReadOnlyList<string> AdminNormalizedNames(IConfiguration configuration) =>
        (configuration["Admins"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => n.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

    /// <summary>"In good standing", or the statuses joined, with the lockout's and the game sign-in block's ends.</summary>
    public static string Describe(AdminUserStatusSet set)
    {
        if (set.Statuses.Count == 0) return "Deleted";
        return string.Join(", ", set.Statuses.Select(s => s switch
        {
            AdminUserStatusKind.LockedOut when set.LockedOutUntil is DateTimeOffset until => "Locked out until " + FormatUtc(until),
            AdminUserStatusKind.GameSignInBlocked when set.GameSignInBlockedUntil is DateTimeOffset blockedUntil =>
                "Game sign-in blocked until " + FormatUtc(blockedUntil),
            _ => Label(s)
        }));
    }

    private static DateTimeOffset NowOffset(DateTime nowUtc) => new(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), TimeSpan.Zero);

    /// <summary>The account's statuses, in the order of <see cref="AdminUserStatusKind"/>.</summary>
    public static AdminUserStatusSet Evaluate(ApplicationUser user, AdminUserFacts facts, IReadOnlyCollection<string> adminNormalizedNames, DateTime nowUtc)
    {
        var now = NowOffset(nowUtc);
        bool lockedOut = user.LockoutEnd != null && user.LockoutEnd > now;
        bool banned = user.IsBanned == true;
        bool gameLogBanned = user.IsGameLogBanned == true;
        bool bonesBanned = user.IsBonesBanned == true;
        bool blocked = user.OverseerBlockedUtc != null;
        bool unconfirmed = !user.EmailConfirmed;
        bool admin = user.NormalizedUserName != null && adminNormalizedNames.Contains(user.NormalizedUserName);
        bool signInBlocked = facts.GameSignInBlockedUntil is DateTime blockedUntil && blockedUntil > nowUtc;

        var statuses = new List<AdminUserStatusKind>();
        if (!unconfirmed && !lockedOut && !signInBlocked && !banned && !gameLogBanned && !bonesBanned && !blocked
            && !facts.HasActiveHold && !facts.HasPendingDeletionRequest)
        {
            statuses.Add(AdminUserStatusKind.GoodStanding);
        }
        if (lockedOut) statuses.Add(AdminUserStatusKind.LockedOut);
        if (signInBlocked) statuses.Add(AdminUserStatusKind.GameSignInBlocked);
        if (banned) statuses.Add(AdminUserStatusKind.Banned);
        if (gameLogBanned) statuses.Add(AdminUserStatusKind.GameLogBanned);
        if (bonesBanned) statuses.Add(AdminUserStatusKind.BonesBanned);
        if (blocked) statuses.Add(AdminUserStatusKind.AiChatBlocked);
        if (facts.HasActiveHold) statuses.Add(AdminUserStatusKind.DeletionHold);
        if (facts.HasPendingDeletionRequest) statuses.Add(AdminUserStatusKind.DeletionPending);
        if (unconfirmed) statuses.Add(AdminUserStatusKind.EmailUnconfirmed);
        if (admin) statuses.Add(AdminUserStatusKind.Administrator);

        return new AdminUserStatusSet(statuses, lockedOut ? user.LockoutEnd : null,
            signInBlocked ? NowOffset(facts.GameSignInBlockedUntil!.Value) : null);
    }

    /// <summary>The SQL form of one status.</summary>
    public static Expression<Func<ApplicationUser, bool>> Predicate(AdminUserStatusKind kind, ApplicationDbContext db,
        IReadOnlyCollection<string> adminNormalizedNames, DateTime nowUtc)
    {
        var now = NowOffset(nowUtc);
        var admins = adminNormalizedNames.ToList();
        return kind switch
        {
            AdminUserStatusKind.GoodStanding => u => u.EmailConfirmed
                && !(u.LockoutEnd != null && u.LockoutEnd > now)
                && !db.GameSignInBlocks.Any(b => b.AspNetUserId == u.Id && b.ClearedUtc == null && b.BlockedUntilUtc > nowUtc)
                && u.IsBanned != true && u.IsGameLogBanned != true && u.IsBonesBanned != true
                && u.OverseerBlockedUtc == null
                && !db.AccountHolds.Any(h => h.AspNetUserId == u.Id && h.ReleasedUtc == null)
                && !db.AccountDeletionRequests.Any(r => r.AspNetUserId == u.Id && r.Status == AccountDeletionRequestStatus.Pending),
            AdminUserStatusKind.LockedOut => u => u.LockoutEnd != null && u.LockoutEnd > now,
            AdminUserStatusKind.GameSignInBlocked => u =>
                db.GameSignInBlocks.Any(b => b.AspNetUserId == u.Id && b.ClearedUtc == null && b.BlockedUntilUtc > nowUtc),
            AdminUserStatusKind.Banned => u => u.IsBanned == true,
            AdminUserStatusKind.GameLogBanned => u => u.IsGameLogBanned == true,
            AdminUserStatusKind.BonesBanned => u => u.IsBonesBanned == true,
            AdminUserStatusKind.AiChatBlocked => u => u.OverseerBlockedUtc != null,
            AdminUserStatusKind.DeletionHold => u => db.AccountHolds.Any(h => h.AspNetUserId == u.Id && h.ReleasedUtc == null),
            AdminUserStatusKind.DeletionPending => u => db.AccountDeletionRequests.Any(r => r.AspNetUserId == u.Id && r.Status == AccountDeletionRequestStatus.Pending),
            AdminUserStatusKind.EmailUnconfirmed => u => !u.EmailConfirmed,
            AdminUserStatusKind.Administrator => u => u.NormalizedUserName != null && admins.Contains(u.NormalizedUserName),
            _ => u => false
        };
    }

    /// <summary>Any game log, bones or save-file tracking row.</summary>
    public static Expression<Func<ApplicationUser, bool>> HasGameData(ApplicationDbContext db) => u =>
        db.GameLog.Any(g => g.AspNetUserId == u.Id)
        || db.Bones.Any(b => b.AspNetUserId == u.Id)
        || db.SaveFileTrackings.Any(s => s.AspNetUserId == u.Id);

    /// <summary>Any chat, AI usage record, or benchmark row the account is credited with.</summary>
    public static Expression<Func<ApplicationUser, bool>> HasOverseerData(ApplicationDbContext db) => u =>
        db.ChatSession.Any(s => s.AspNetUserId == u.Id)
        || db.SystemAiUsageLogs.Any(l => l.AspNetUserId == u.Id)
        || db.UserKeyAiUsageLogs.Any(l => l.AspNetUserId == u.Id)
        || db.BenchmarkRuns.Any(r => r.StartedByUserId == u.Id)
        || db.BenchmarkRunSeries.Any(r => r.StartedByUserId == u.Id)
        || db.BenchmarkRunGroups.Any(r => r.CreatedByUserId == u.Id)
        || db.BenchmarkGroupAnalyses.Any(r => r.ComputedByUserId == u.Id)
        || db.BenchmarkBatteries.Any(r => r.CreatedByUserId == u.Id)
        || db.BenchmarkBatteryRuns.Any(r => r.StartedByUserId == u.Id)
        || db.BenchmarkBatteryAnalyses.Any(r => r.ComputedByUserId == u.Id);

    /// <summary>
    /// Filters by the criteria. A registration-age criterion reads <c>CreatedUtc</c>, which for a backfilled account
    /// is an upper bound: "registered more than N days ago" is then true no earlier than the true date would make it,
    /// and an account without a date never satisfies it.
    /// </summary>
    public static IQueryable<ApplicationUser> Apply(IQueryable<ApplicationUser> query, ApplicationDbContext db, AdminUserCriteria criteria,
        IReadOnlyCollection<string> adminNormalizedNames, DateTime nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            string term = criteria.Search.Trim().ToUpperInvariant();
            query = query.Where(u => (u.NormalizedUserName != null && u.NormalizedUserName.Contains(term))
                || (u.NormalizedEmail != null && u.NormalizedEmail.Contains(term)));
        }

        if (criteria.Statuses is { Count: > 0 })
        {
            Expression<Func<ApplicationUser, bool>>? any = null;
            foreach (var kind in criteria.Statuses.Distinct())
            {
                var predicate = Predicate(kind, db, adminNormalizedNames, nowUtc);
                any = any == null ? predicate : Or(any, predicate);
            }
            query = query.Where(any!);
        }

        if (AdminUserCriteria.ParseYesNo(criteria.EmailConfirmed) is bool confirmed)
        {
            query = query.Where(u => u.EmailConfirmed == confirmed);
        }

        if (criteria.RegisteredOlderThanDays is int olderDays)
        {
            DateTime cutoff = nowUtc.AddDays(-olderDays);
            query = query.Where(u => u.CreatedUtc != null && u.CreatedUtc <= cutoff);
        }
        if (criteria.RegisteredWithinDays is int withinDays)
        {
            DateTime cutoff = nowUtc.AddDays(-withinDays);
            query = query.Where(u => u.CreatedUtc != null && u.CreatedUtc >= cutoff);
        }

        if (AdminUserCriteria.TryParseLastSignIn(criteria.LastSignIn, out var signIn))
        {
            DateTime cutoff = nowUtc.AddDays(-signIn.Days);
            query = signIn.Kind switch
            {
                AdminLastSignInFilterKind.Never => query.Where(u => u.LastSignInUtc == null),
                AdminLastSignInFilterKind.OlderThanDays => query.Where(u => u.LastSignInUtc != null && u.LastSignInUtc < cutoff),
                _ => query.Where(u => u.LastSignInUtc != null && u.LastSignInUtc >= cutoff)
            };
        }

        if (AdminUserCriteria.ParseYesNo(criteria.HasGameData) is bool gameData)
        {
            var predicate = HasGameData(db);
            query = query.Where(gameData ? predicate : Not(predicate));
        }
        if (AdminUserCriteria.ParseYesNo(criteria.HasOverseerData) is bool overseerData)
        {
            var predicate = HasOverseerData(db);
            query = query.Where(overseerData ? predicate : Not(predicate));
        }

        return query;
    }

    private static Expression<Func<T, bool>> Or<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var parameter = left.Parameters[0];
        var rightBody = new ParameterReplacer(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(left.Body, rightBody), parameter);
    }

    private static Expression<Func<T, bool>> Not<T>(Expression<Func<T, bool>> predicate) =>
        Expression.Lambda<Func<T, bool>>(Expression.Not(predicate.Body), predicate.Parameters);

    private sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        public ParameterReplacer(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node) => node == _from ? _to : base.VisitParameter(node);
    }
}
