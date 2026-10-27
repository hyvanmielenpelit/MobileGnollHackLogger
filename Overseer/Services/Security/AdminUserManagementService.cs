using System.ComponentModel.DataAnnotations;
using GnollHackServer.Data.Privacy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Extensions;
using Overseer.Models;
using Overseer.Services.Privacy;

namespace Overseer.Services.Security;

/// <summary>The administrator acting: the id from the NameIdentifier claim and the user name.</summary>
public sealed record AdminActor(string UserId, string UserName);

/// <summary>How an admin user-management call ended; the controller maps it to 200, 404, 400, 409, 403 or 500.</summary>
public enum AdminResultStatus { Ok, NotFound, BadRequest, Conflict, Forbidden, Failed }

/// <param name="Code">A refusal code (409) or a validation code (400) the client can switch on.</param>
public sealed record AdminResult<T>(AdminResultStatus Status, T? Value = default, string? Message = null, string? Code = null)
{
    public bool IsOk => Status == AdminResultStatus.Ok;
}

/// <summary>The refusal and validation codes of the admin user-management endpoints.</summary>
public static class AdminRefusalCodes
{
    public const string NotFound = "not-found";
    public const string Self = "self";
    public const string Administrator = "administrator";
    public const string Hold = "hold";
    public const string PendingRequest = "pending-request";
    public const string ErasurePaths = "erasure-paths";
    public const string AlreadyDone = "already-done";
    public const string Reason = "reason";
    public const string Until = "until";
    public const string ConfirmName = "confirm-name";
    public const string Acknowledgments = "acknowledgments";
    public const string ErasureFailed = "erasure-failed";
    public const string Identity = "identity";
    public const string Email = "email";
    public const string EmailTaken = "email-taken";
}

/// <summary>Builds rows of the durable admin log, <see cref="AdminUserAction"/>.</summary>
public static class AdminUserActionLog
{
    public static AdminUserAction Entry(AdminActor actor, string subjectUserId, string? subjectUserName, AdminUserActionKind kind,
        string? reason, string? detail, DateTime occurredUtc, long? bulkOperationId = null) => new()
    {
        OccurredUtc = occurredUtc,
        ActorUserId = Cut(actor.UserId, 450),
        ActorUserName = Cut(actor.UserName, 256),
        SubjectUserId = Cut(subjectUserId, 450),
        SubjectUserName = Cut(subjectUserName ?? string.Empty, 256),
        Kind = kind,
        Reason = string.IsNullOrWhiteSpace(reason) ? null : Cut(reason, 512),
        Detail = string.IsNullOrWhiteSpace(detail) ? null : Cut(detail, 1024),
        BulkOperationId = bulkOperationId
    };

    public static string Detail(AdminUserStatusSet before, AdminUserStatusSet after) => $"Before: {before.Text}. After: {after.Text}.";

    /// <summary>The actor with its user name read from the database; the id stands in when the account is not found.</summary>
    public static async Task<AdminActor> ResolveActorAsync(ApplicationDbContext db, string? actorUserId, CancellationToken ct = default)
    {
        string id = actorUserId ?? string.Empty;
        string? name = id.Length == 0
            ? null
            : await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.UserName).FirstOrDefaultAsync(ct);
        return new AdminActor(id, name ?? id);
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>
/// The admin Users tab: the list with its criteria and facet counts, and every per-user action. Each action loads
/// the account, refuses a protected target, changes state, and writes one <see cref="AdminUserAction"/> row and
/// its security event. The bulk runner calls the same methods with a <c>bulkOperationId</c>.
/// </summary>
public class AdminUserManagementService
{
    public const int MaxReasonLength = 512;
    public const int MaxEmailLength = 256;
    public const int MaxTake = 100;
    public const int DefaultTake = 20;

    public const string RefusedSelfMessage = "This action cannot be applied to your own account.";
    public const string RefusedAdministratorMessage = "Administrators are protected from this action. Remove the account from Admins first.";

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly AdminUserManagementOptions _options;
    private readonly AccountDeletionReviewService _review;
    private readonly OverseerAccessService _access;
    private readonly ILogger<AdminUserManagementService> _logger;
    private readonly ISecurityEventRecorder? _securityEvents;
    private readonly TimeProvider _time;
    private readonly EphemeralSessionStore? _ephemeralSessions;
    private readonly IServiceProvider? _services;
    private readonly GnollHackServer.Data.Security.GameSignInBlockSync? _signInBlocks;

    /// <param name="services">Resolves the email sender only when an email is sent: its constructor throws when email is not configured.</param>
    public AdminUserManagementService(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IConfiguration configuration,
        AdminUserManagementOptions options, AccountDeletionReviewService review, OverseerAccessService access,
        ILogger<AdminUserManagementService> logger, ISecurityEventRecorder? securityEvents = null, TimeProvider? time = null,
        EphemeralSessionStore? ephemeralSessions = null, IServiceProvider? services = null,
        GnollHackServer.Data.Security.GameSignInBlockSync? signInBlocks = null)
    {
        _db = db;
        _userManager = userManager;
        _configuration = configuration;
        _options = options;
        _review = review;
        _access = access;
        _logger = logger;
        _securityEvents = securityEvents;
        _time = time ?? TimeProvider.System;
        _ephemeralSessions = ephemeralSessions;
        _services = services;
        _signInBlocks = signInBlocks;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    private IQueryable<ApplicationUser> Users => _db.Users.OfType<ApplicationUser>();

    public IReadOnlyList<string> AdminNormalizedNames => AdminUserStatus.AdminNormalizedNames(_configuration);

    public bool IsAdministrator(ApplicationUser user) => _configuration.IsAdmin(user.UserName);

    // --- Reading ---

    public async Task<AdminUsersPageDto> ListAsync(AdminActor actor, AdminUserCriteria criteria, string? sort, int skip, int take,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);
        skip = Math.Max(0, skip);
        DateTime now = UtcNow;

        var query = AdminUserStatus.Apply(Users.AsNoTracking(), _db, criteria, AdminNormalizedNames, now);
        int total = await query.CountAsync(ct);
        var users = await Sort(query, sort).Skip(skip).Take(take).ToListAsync(ct);

        return new AdminUsersPageDto
        {
            TotalCount = total,
            Rows = await BuildDtosAsync(actor, users, now, ct),
            NowUtc = DateTime.SpecifyKind(now, DateTimeKind.Utc)
        };
    }

    /// <summary>Newest and oldest sort by registration, sign-in sorts by last sign-in; an unknown date sorts last.</summary>
    public static IQueryable<ApplicationUser> Sort(IQueryable<ApplicationUser> query, string? sort) => (sort ?? "newest").Trim().ToLowerInvariant() switch
    {
        "oldest" => query.OrderBy(u => u.CreatedUtc == null).ThenBy(u => u.CreatedUtc).ThenBy(u => u.Id),
        "name" => query.OrderBy(u => u.UserName).ThenBy(u => u.Id),
        "email" => query.OrderBy(u => u.Email).ThenBy(u => u.Id),
        "signin-recent" => query.OrderBy(u => u.LastSignInUtc == null).ThenByDescending(u => u.LastSignInUtc).ThenBy(u => u.Id),
        "signin-oldest" => query.OrderBy(u => u.LastSignInUtc == null).ThenBy(u => u.LastSignInUtc).ThenBy(u => u.Id),
        _ => query.OrderBy(u => u.CreatedUtc == null).ThenByDescending(u => u.CreatedUtc).ThenBy(u => u.Id)
    };

    /// <summary>One count per filter option, under the search only: the counts do not follow the other filters.</summary>
    public async Task<AdminUserFacetsDto> FacetCountsAsync(string? search, CancellationToken ct = default)
    {
        DateTime now = UtcNow;
        var admins = AdminNormalizedNames;
        var baseQuery = AdminUserStatus.Apply(Users.AsNoTracking(), _db, AdminUserCriteria.SearchOnly(search), admins, now);
        Task<int> Count(AdminUserCriteria criteria) => AdminUserStatus.Apply(baseQuery, _db, criteria, admins, now).CountAsync(ct);

        var facets = new AdminUserFacetsDto { Total = await baseQuery.CountAsync(ct) };
        foreach (var kind in Enum.GetValues<AdminUserStatusKind>())
        {
            facets.Statuses.Add(new AdminFacetCountDto { Key = kind.ToString(), Count = await Count(new AdminUserCriteria { Statuses = new() { kind } }) });
        }
        foreach (string yesNo in new[] { "yes", "no" })
        {
            facets.EmailConfirmed.Add(new AdminFacetCountDto { Key = yesNo, Count = await Count(new AdminUserCriteria { EmailConfirmed = yesNo }) });
            facets.HasGameData.Add(new AdminFacetCountDto { Key = yesNo, Count = await Count(new AdminUserCriteria { HasGameData = yesNo }) });
            facets.HasOverseerData.Add(new AdminFacetCountDto { Key = yesNo, Count = await Count(new AdminUserCriteria { HasOverseerData = yesNo }) });
        }
        foreach (int days in new[] { 7, 30 })
        {
            facets.Registered.Add(new AdminFacetCountDto { Key = $"withinDays:{days}", Count = await Count(new AdminUserCriteria { RegisteredWithinDays = days }) });
        }
        foreach (int days in new[] { 30, 365 })
        {
            facets.Registered.Add(new AdminFacetCountDto { Key = $"olderThanDays:{days}", Count = await Count(new AdminUserCriteria { RegisteredOlderThanDays = days }) });
        }
        foreach (string option in new[] { "never", "withinDays:30", "withinDays:90", "olderThanDays:90", "olderThanDays:365" })
        {
            facets.LastSignIn.Add(new AdminFacetCountDto { Key = option, Count = await Count(new AdminUserCriteria { LastSignIn = option }) });
        }
        return facets;
    }

    public async Task<AdminResult<AdminUserDto>> GetAsync(AdminActor actor, string id, CancellationToken ct = default)
    {
        var user = await Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null) return new(AdminResultStatus.NotFound, Message: "User not found.", Code: AdminRefusalCodes.NotFound);
        return new(AdminResultStatus.Ok, (await BuildDtosAsync(actor, new List<ApplicationUser> { user }, UtcNow, ct))[0]);
    }

    /// <summary>
    /// The cards of a page: groups, holds with the placer's name, pending requests, active game sign-in blocks and data
    /// counts, one query each.
    /// </summary>
    private async Task<List<AdminUserDto>> BuildDtosAsync(AdminActor actor, List<ApplicationUser> users, DateTime now, CancellationToken ct)
    {
        var ids = users.Select(u => u.Id).ToList();
        if (ids.Count == 0) return new List<AdminUserDto>();

        var groups = await _db.UserGroups.AsNoTracking()
            .Where(ug => ids.Contains(ug.AspNetUserId))
            .Select(ug => new { ug.AspNetUserId, ug.GroupId, ug.Group.DisplayName })
            .ToListAsync(ct);
        var holds = await (
            from h in _db.AccountHolds.AsNoTracking()
            where ids.Contains(h.AspNetUserId) && h.ReleasedUtc == null
            join p in _db.Users.AsNoTracking() on h.PlacedByUserId equals p.Id into placers
            from p in placers.DefaultIfEmpty()
            select new { h.AspNetUserId, h.Reason, h.PlacedUtc, PlacedBy = p == null ? null : p.UserName })
            .ToListAsync(ct);
        var pending = await _db.AccountDeletionRequests.AsNoTracking()
            .Where(r => r.AspNetUserId != null && ids.Contains(r.AspNetUserId) && r.Status == AccountDeletionRequestStatus.Pending)
            .Select(r => new { r.AspNetUserId, r.Id })
            .ToListAsync(ct);
        var signInBlocks = await _db.GameSignInBlocks.AsNoTracking()
            .Where(b => ids.Contains(b.AspNetUserId) && b.ClearedUtc == null && b.BlockedUntilUtc > now)
            .OrderByDescending(b => b.BlockedUntilUtc)
            .Select(b => new { b.AspNetUserId, b.Source, b.Kind, b.IpAddress, b.BlockedUntilUtc })
            .ToListAsync(ct);
        var gameLogs = await _db.GameLog.AsNoTracking()
            .Where(g => g.AspNetUserId != null && ids.Contains(g.AspNetUserId))
            .GroupBy(g => g.AspNetUserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var bones = await _db.Bones.AsNoTracking()
            .Where(b => b.AspNetUserId != null && ids.Contains(b.AspNetUserId))
            .GroupBy(b => b.AspNetUserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var chats = await _db.ChatSession.AsNoTracking()
            .Where(s => s.AspNetUserId != null && ids.Contains(s.AspNetUserId))
            .GroupBy(s => s.AspNetUserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var acceptances = await _db.UserTermsAcceptances.AsNoTracking()
            .Where(a => ids.Contains(a.AspNetUserId))
            .Select(a => new { a.AspNetUserId, a.DocumentKey, a.Version, a.AcceptedUtc, a.Source })
            .ToListAsync(ct);
        var versionOrder = Comparer<string>.Create(GnollHackServer.Data.Legal.LegalVersion.Compare);

        var admins = AdminNormalizedNames;
        var rows = new List<AdminUserDto>(users.Count);
        foreach (var u in users)
        {
            var hold = holds.FirstOrDefault(h => h.AspNetUserId == u.Id);
            long? pendingId = pending.Where(r => r.AspNetUserId == u.Id).Select(r => (long?)r.Id).FirstOrDefault();
            var blocks = signInBlocks.Where(b => b.AspNetUserId == u.Id).ToList();
            var set = AdminUserStatus.Evaluate(u, new AdminUserFacts(hold != null, pendingId != null,
                blocks.Count == 0 ? null : blocks.Max(b => b.BlockedUntilUtc)), admins, now);
            bool lockedOut = set.Has(AdminUserStatusKind.LockedOut);
            bool adminSet = lockedOut && u.AdminLockoutEndUtc != null && u.AdminLockoutEndUtc == u.LockoutEnd;

            rows.Add(new AdminUserDto
            {
                Id = u.Id,
                UserName = u.UserName ?? string.Empty,
                Email = u.Email ?? string.Empty,
                EmailConfirmed = u.EmailConfirmed,
                Groups = groups.Where(g => g.AspNetUserId == u.Id)
                    .Select(g => new AdminGroupDto { Id = g.GroupId, DisplayName = g.DisplayName })
                    .ToList(),
                Statuses = set.Statuses.ToList(),
                StatusText = set.Text,
                IsAdministrator = IsAdministrator(u),
                IsSelf = string.Equals(u.Id, actor.UserId, StringComparison.Ordinal),
                CreatedUtc = Utc(u.CreatedUtc),
                CreatedUtcIsEstimate = u.CreatedUtcIsEstimate,
                LastSignInUtc = Utc(u.LastSignInUtc),
                LockoutEnd = u.LockoutEnd,
                LockoutIsAdminSet = adminSet,
                AdminLockoutReason = adminSet ? u.AdminLockoutReason : null,
                AccessFailedCount = u.AccessFailedCount,
                TwoFactorEnabled = u.TwoFactorEnabled,
                IsBanned = u.IsBanned == true,
                BannedUtc = Utc(u.BannedUtc),
                BanReason = u.BanReason,
                IsGameLogBanned = u.IsGameLogBanned == true,
                IsBonesBanned = u.IsBonesBanned == true,
                IsOverseerBlocked = u.OverseerBlockedUtc != null,
                OverseerBlockedUtc = Utc(u.OverseerBlockedUtc),
                OverseerBlockReason = u.OverseerBlockReason,
                HasAccountHold = hold != null,
                AccountHoldReason = hold?.Reason,
                AccountHoldPlacedUtc = hold == null ? null : Utc(hold.PlacedUtc),
                AccountHoldPlacedByUserName = hold?.PlacedBy,
                PendingDeletionRequestId = pendingId,
                GameSignInBlocks = blocks.Select(b => new AdminGameSignInBlockDto
                {
                    Source = b.Source.ToString(),
                    Kind = b.Kind.ToString(),
                    IpAddress = b.IpAddress,
                    BlockedUntilUtc = DateTime.SpecifyKind(b.BlockedUntilUtc, DateTimeKind.Utc)
                }).ToList(),
                GameLogCount = gameLogs.Where(g => g.UserId == u.Id).Select(g => g.Count).FirstOrDefault(),
                BonesCount = bones.Where(g => g.UserId == u.Id).Select(g => g.Count).FirstOrDefault(),
                ChatSessionCount = chats.Where(g => g.UserId == u.Id).Select(g => g.Count).FirstOrDefault(),
                JunetHackUserName = u.JunetHackUserName,
                TermsAcceptances = acceptances.Where(a => a.AspNetUserId == u.Id)
                    .GroupBy(a => a.DocumentKey)
                    .Select(g => g.OrderByDescending(a => a.Version, versionOrder).First())
                    .OrderBy(a => a.DocumentKey, StringComparer.Ordinal)
                    .Select(a => new AdminTermsAcceptanceDto
                    {
                        Document = a.DocumentKey,
                        Version = a.Version,
                        AcceptedUtc = DateTime.SpecifyKind(a.AcceptedUtc, DateTimeKind.Utc),
                        Source = a.Source
                    })
                    .ToList()
            });
        }
        return rows;
    }

    private static DateTime? Utc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

    /// <summary>The admin log, newest first.</summary>
    /// <param name="kinds">Comma-separated <see cref="AdminUserActionKind"/> names; unknown names are ignored.</param>
    /// <param name="actor">The actor's user id, or part of the actor's user name.</param>
    public async Task<AdminUserActionLogPageDto> ListLogAsync(string? search, string? kinds, string? actor, string? subjectUserId,
        long? bulkOperationId, int skip, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take <= 0 ? 50 : take, 1, MaxTake);
        skip = Math.Max(0, skip);

        var query = _db.AdminUserActions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(a => a.SubjectUserName.Contains(term) || a.ActorUserName.Contains(term)
                || (a.Reason != null && a.Reason.Contains(term)) || (a.Detail != null && a.Detail.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(kinds))
        {
            var parsed = kinds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(k => Enum.TryParse<AdminUserActionKind>(k, ignoreCase: true, out var kind) ? kind : (AdminUserActionKind?)null)
                .Where(k => k.HasValue)
                .Select(k => k!.Value)
                .Distinct()
                .ToList();
            if (parsed.Count > 0) query = query.Where(a => parsed.Contains(a.Kind));
        }
        if (!string.IsNullOrWhiteSpace(actor))
        {
            string term = actor.Trim();
            query = query.Where(a => a.ActorUserId == term || a.ActorUserName.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(subjectUserId))
        {
            string subject = subjectUserId.Trim();
            query = query.Where(a => a.SubjectUserId == subject);
        }
        if (bulkOperationId.HasValue)
        {
            long opId = bulkOperationId.Value;
            query = query.Where(a => a.BulkOperationId == opId);
        }

        int total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.OccurredUtc).ThenByDescending(a => a.Id).Skip(skip).Take(take).ToListAsync(ct);
        return new AdminUserActionLogPageDto
        {
            TotalCount = total,
            Rows = rows.Select(a => new AdminUserActionDto
            {
                Id = a.Id,
                OccurredUtc = DateTime.SpecifyKind(a.OccurredUtc, DateTimeKind.Utc),
                ActorUserId = a.ActorUserId,
                ActorUserName = a.ActorUserName,
                SubjectUserId = a.SubjectUserId,
                SubjectUserName = a.SubjectUserName,
                Kind = a.Kind,
                Reason = a.Reason,
                Detail = a.Detail,
                BulkOperationId = a.BulkOperationId
            }).ToList()
        };
    }

    // --- Shared action steps ---

    private enum GuardPolicy { RefuseSelfAndAdministrators, AllowSelfRefuseAdministrators, AllowAll }

    private static AdminResult<AdminUserActionResultDto> Refuse(AdminResultStatus status, string message, string? code) => new(status, Message: message, Code: code);

    private static AdminResult<AdminUserActionResultDto> NotFoundResult() =>
        Refuse(AdminResultStatus.NotFound, "User not found.", AdminRefusalCodes.NotFound);

    /// <summary>Null when the target may be acted on; otherwise the 409 refusal.</summary>
    private AdminResult<AdminUserActionResultDto>? Guard(AdminActor actor, ApplicationUser target, GuardPolicy policy)
    {
        if (policy == GuardPolicy.AllowAll) return null;
        bool self = string.Equals(target.Id, actor.UserId, StringComparison.Ordinal);
        if (self)
        {
            return policy == GuardPolicy.RefuseSelfAndAdministrators
                ? Refuse(AdminResultStatus.Conflict, RefusedSelfMessage, AdminRefusalCodes.Self)
                : null;
        }
        return IsAdministrator(target)
            ? Refuse(AdminResultStatus.Conflict, RefusedAdministratorMessage, AdminRefusalCodes.Administrator)
            : null;
    }

    private static AdminResult<AdminUserActionResultDto>? IdentityFailure(IdentityResult result) => result.Succeeded
        ? null
        : Refuse(AdminResultStatus.Failed, string.Join(" ", result.Errors.Select(e => e.Description)), AdminRefusalCodes.Identity);

    private static string? ValidateReason(ref string? reason, bool required)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason == null) return required ? $"A reason of 1 to {MaxReasonLength} characters is required." : null;
        return reason.Length > MaxReasonLength ? $"The reason can be at most {MaxReasonLength} characters." : null;
    }

    private async Task<AdminUserFacts> FactsAsync(string userId, CancellationToken ct)
    {
        DateTime now = UtcNow;
        return new(
            await _db.AccountHolds.AnyAsync(h => h.AspNetUserId == userId && h.ReleasedUtc == null, ct),
            await _db.AccountDeletionRequests.AnyAsync(r => r.AspNetUserId == userId && r.Status == AccountDeletionRequestStatus.Pending, ct),
            await ActiveSignInBlocks(userId, now).MaxAsync(b => (DateTime?)b.BlockedUntilUtc, ct));
    }

    private IQueryable<GameSignInBlock> ActiveSignInBlocks(string userId, DateTime nowUtc) =>
        _db.GameSignInBlocks.Where(b => b.AspNetUserId == userId && b.ClearedUtc == null && b.BlockedUntilUtc > nowUtc);

    private AdminUserStatusSet StatusOf(ApplicationUser user, AdminUserFacts facts) =>
        AdminUserStatus.Evaluate(user, facts, AdminNormalizedNames, UtcNow);

    /// <summary>The account's statuses now, for the bulk runner's before-and-after record.</summary>
    public async Task<AdminUserStatusSet?> CurrentStatusAsync(string userId, CancellationToken ct = default)
    {
        var user = await Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user == null ? null : StatusOf(user, await FactsAsync(userId, ct));
    }

    /// <summary>Writes the audit row and the security event, and returns the before, after and refreshed card.</summary>
    private async Task<AdminResult<AdminUserActionResultDto>> FinishAsync(AdminActor actor, ApplicationUser user, AdminUserFacts facts,
        AdminUserStatusSet before, AdminUserActionKind kind, string? reason, string summary, SecurityEventKind? eventKind,
        long? bulkOperationId, CancellationToken ct)
    {
        var after = StatusOf(user, facts);
        _db.AdminUserActions.Add(AdminUserActionLog.Entry(actor, user.Id, user.UserName, kind, reason,
            AdminUserActionLog.Detail(before, after), UtcNow, bulkOperationId));
        await _db.SaveChangesAsync(ct);
        if (eventKind is SecurityEventKind recorded)
        {
            _securityEvents?.Record(new SecurityEventEntry(recorded, UserId: user.Id, Detail: reason));
        }
        return await ResultAsync(actor, user.Id, before, after, summary, ct);
    }

    private async Task<AdminResult<AdminUserActionResultDto>> ResultAsync(AdminActor actor, string userId, AdminUserStatusSet before,
        AdminUserStatusSet after, string summary, CancellationToken ct)
    {
        var card = await GetAsync(actor, userId, ct);
        return new(AdminResultStatus.Ok, new AdminUserActionResultDto
        {
            User = card.Value,
            Before = AdminUserStatusSetDto.From(before),
            After = AdminUserStatusSetDto.From(after),
            Summary = summary
        });
    }

    // --- Actions ---

    /// <summary>Locks the account out until <paramref name="until"/>, at most <see cref="AdminUserManagementOptions.LockoutMaxDays"/> ahead.</summary>
    public async Task<AdminResult<AdminUserActionResultDto>> LockOutAsync(AdminActor actor, string id, DateTimeOffset? until, string? reason,
        long? bulkOperationId = null, CancellationToken ct = default)
    {
        if (ValidateReason(ref reason, required: true) is string reasonError)
            return Refuse(AdminResultStatus.BadRequest, reasonError, AdminRefusalCodes.Reason);
        DateTimeOffset now = _time.GetUtcNow();
        if (until == null)
            return Refuse(AdminResultStatus.BadRequest, "Choose when the lockout ends.", AdminRefusalCodes.Until);
        DateTimeOffset end = until.Value.ToUniversalTime();
        if (end <= now)
            return Refuse(AdminResultStatus.BadRequest, "The lockout must end in the future.", AdminRefusalCodes.Until);
        if (end > now.AddDays(_options.LockoutMaxDays))
            return Refuse(AdminResultStatus.BadRequest, $"A lockout can last at most {_options.LockoutMaxDays} days. Ban the account instead.", AdminRefusalCodes.Until);

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFoundResult();
        if (Guard(actor, user, GuardPolicy.RefuseSelfAndAdministrators) is { } refused) return refused;
        var facts = await FactsAsync(user.Id, ct);
        var before = StatusOf(user, facts);

        if (IdentityFailure(await _userManager.SetLockoutEnabledAsync(user, true)) is { } failed1) return failed1;
        if (IdentityFailure(await _userManager.SetLockoutEndDateAsync(user, end)) is { } failed2) return failed2;
        user.AdminLockoutEndUtc = end;
        user.AdminLockoutReason = reason;
        user.AdminLockoutByUserId = actor.UserId;
        // Renews the security stamp, which ends signed-in sessions, and saves the admin columns.
        if (IdentityFailure(await _userManager.UpdateSecurityStampAsync(user)) is { } failed3) return failed3;

        return await FinishAsync(actor, user, facts, before, AdminUserActionKind.LockedOut, reason,
            $"Locked out until {AdminUserStatus.FormatUtc(end)}.", SecurityEventKind.AdminLockedOut, bulkOperationId, ct);
    }

    /// <summary>Ends any lockout, admin-set or automatic, and resets the failed sign-in count.</summary>
    public async Task<AdminResult<AdminUserActionResultDto>> UnlockAsync(AdminActor actor, string id, long? bulkOperationId = null,
        CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFoundResult();
        if (Guard(actor, user, GuardPolicy.RefuseSelfAndAdministrators) is { } refused) return refused;
        var facts = await FactsAsync(user.Id, ct);
        var before = StatusOf(user, facts);
        if (!before.Has(AdminUserStatusKind.LockedOut) && user.AccessFailedCount == 0 && user.AdminLockoutEndUtc == null)
            return Refuse(AdminResultStatus.Conflict, "The account is not locked out.", AdminRefusalCodes.AlreadyDone);

        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        user.AdminLockoutEndUtc = null;
        user.AdminLockoutReason = null;
        user.AdminLockoutByUserId = null;
        if (IdentityFailure(await _userManager.UpdateAsync(user)) is { } failed) return failed;

        return await FinishAsync(actor, user, facts, before, AdminUserActionKind.Unlocked, null,
            "Unlocked. The failed sign-in count is reset.", SecurityEventKind.AdminUnlocked, bulkOperationId, ct);
    }

    /// <summary>
    /// Clears the account's active game sign-in blocks, in the website and in Overseer. Overseer's own blocks end at
    /// once; the website releases its blocks on GnollHack's next retry, within <see cref="GnollHackServer.Data.Security.GameSignInBlockSync.LookupInterval"/>.
    /// Allowed on any account: it only lifts a temporary throttle. Not a bulk action.
    /// </summary>
    public async Task<AdminResult<AdminUserActionResultDto>> ClearGameSignInBlocksAsync(AdminActor actor, string id,
        CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFoundResult();
        if (Guard(actor, user, GuardPolicy.AllowAll) is { } refused) return refused;
        DateTime now = UtcNow;
        var rows = await ActiveSignInBlocks(user.Id, now).ToListAsync(ct);
        if (rows.Count == 0)
            return Refuse(AdminResultStatus.Conflict, "The account has no active game sign-in block.", AdminRefusalCodes.AlreadyDone);
        var facts = await FactsAsync(user.Id, ct);
        var before = StatusOf(user, facts);

        foreach (var row in rows)
        {
            row.ClearedUtc = now;
            row.ClearedByUserId = actor.UserId;
        }

        var result = await FinishAsync(actor, user, facts with { GameSignInBlockedUntil = null }, before,
            AdminUserActionKind.GameSignInBlockCleared, null,
            "Game sign-in block cleared. GnollHack can sign in again within 15 seconds.",
            SecurityEventKind.AdminGameSignInBlockCleared, null, ct);

        if (_signInBlocks != null)
        {
            foreach (var row in rows.Where(r => r.Source == _signInBlocks.Source))
            {
                _signInBlocks.ReleaseLocal(row.Kind, user.UserName ?? string.Empty, row.IpAddress);
            }
        }
        return result;
    }

    /// <summary>Bans the account indefinitely: no sign-in anywhere, and signed-in sessions end.</summary>
    public async Task<AdminResult<AdminUserActionResultDto>> BanAsync(AdminActor actor, string id, string? reason, long? bulkOperationId = null,
        CancellationToken ct = default)
    {
        if (ValidateReason(ref reason, required: true) is string reasonError)
            return Refuse(AdminResultStatus.BadRequest, reasonError, AdminRefusalCodes.Reason);
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFoundResult();
        if (Guard(actor, user, GuardPolicy.RefuseSelfAndAdministrators) is { } refused) return refused;
        if (user.IsBanned == true)
            return Refuse(AdminResultStatus.Conflict, "The account is already banned.", AdminRefusalCodes.AlreadyDone);
        var facts = await FactsAsync(user.Id, ct);
        var before = StatusOf(user, facts);

        user.IsBanned = true;
        user.BannedUtc = UtcNow;
        user.BanReason = reason;
        user.BannedByUserId = actor.UserId;
        if (IdentityFailure(await _userManager.UpdateSecurityStampAsync(user)) is { } failed) return failed;

        return await FinishAsync(actor, user, facts, before, AdminUserActionKind.Banned, reason,
            "Banned.", SecurityEventKind.AdminBanned, bulkOperationId, ct);
    }

    public async Task<AdminResult<AdminUserActionResultDto>> UnbanAsync(AdminActor actor, string id, long? bulkOperationId = null,
        CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFoundResult();
        if (Guard(actor, user, GuardPolicy.RefuseSelfAndAdministrators) is { } refused) return refused;
        if (user.IsBanned != true)
            return Refuse(AdminResultStatus.Conflict, "The account is not banned.", AdminRefusalCodes.AlreadyDone);
        var facts = await FactsAsync(user.Id, ct);
        var before = StatusOf(user, facts);

        user.IsBanned = false;
        user.BannedUtc = null;
        user.BanReason = null;
        user.BannedByUserId = null;
        if (IdentityFailure(await _userManager.UpdateAsync(user)) is { } failed) return failed;

        return await FinishAsync(actor, user, facts, before, AdminUserActionKind.Unbanned, null,
            "Unbanned.", SecurityEventKind.AdminUnbanned, bulkOperationId, ct);
    }

    /// <summary>Sets the game log and bones upload bans. Allowed on any account, the administrator's own included.</summary>
    public async Task<AdminResult<AdminUserActionResultDto>> SetGameRestrictionsAsync(AdminActor actor, string id, bool gameLogBanned,
        bool bonesBanned, string? reason, CancellationToken ct = default)
    {
        if (ValidateReason(ref reason, required: false) is string reasonError)
            return Refuse(AdminResultStatus.BadRequest, reasonError, AdminRefusalCodes.Reason);
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFoundResult();
        if (Guard(actor, user, GuardPolicy.AllowAll) is { } refused) return refused;
        if ((user.IsGameLogBanned == true) == gameLogBanned && (user.IsBonesBanned == true) == bonesBanned)
            return Refuse(AdminResultStatus.Conflict, "Nothing changed.", AdminRefusalCodes.AlreadyDone);
        var facts = await FactsAsync(user.Id, ct);
        var before = StatusOf(user, facts);

        user.IsGameLogBanned = gameLogBanned;
        user.IsBonesBanned = bonesBanned;
        if (IdentityFailure(await _userManager.UpdateAsync(user)) is { } failed) return failed;

        string summary = (gameLogBanned ? "Game log uploads banned" : "Game log uploads allowed")
            + (bonesBanned ? ", bones banned." : ", bones allowed.");
        return await FinishAsync(actor, user, facts, before, AdminUserActionKind.GameRestrictionsChanged, reason,
            summary, null, null, ct);
    }

    /// <summary>Renews the security stamp, so every signed-in session ends at its next validation. Allowed on the administrator's own account.</summary>
    public async Task<AdminResult<AdminUserActionResultDto>> SignOutEverywhereAsync(AdminActor actor, string id, long? bulkOperationId = null,
        CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFoundResult();
        if (Guard(actor, user, GuardPolicy.AllowSelfRefuseAdministrators) is { } refused) return refused;
        var facts = await FactsAsync(user.Id, ct);
        var before = StatusOf(user, facts);

        if (IdentityFailure(await _userManager.UpdateSecurityStampAsync(user)) is { } failed) return failed;

        int minutes = _configuration.GetValue("IdentityLockout:SecurityStampValidationMinutes", 5);
        return await FinishAsync(actor, user, facts, before, AdminUserActionKind.SignedOutEverywhere, null,
            $"Signed out everywhere. Signed-in sessions end within {minutes} minutes.", SecurityEventKind.AdminSignedOut, bulkOperationId, ct);
    }

    public async Task<AdminResult<AdminUserActionResultDto>> ConfirmEmailAsync(AdminActor actor, string id, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFoundResult();
        if (Guard(actor, user, GuardPolicy.RefuseSelfAndAdministrators) is { } refused) return refused;
        if (user.EmailConfirmed)
            return Refuse(AdminResultStatus.Conflict, "The email address is already confirmed.", AdminRefusalCodes.AlreadyDone);
        var facts = await FactsAsync(user.Id, ct);
        var before = StatusOf(user, facts);

        string token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        if (IdentityFailure(await _userManager.ConfirmEmailAsync(user, token)) is { } failed) return failed;

        return await FinishAsync(actor, user, facts, before, AdminUserActionKind.EmailConfirmed, null,
            "Email marked confirmed.", SecurityEventKind.AdminEmailConfirmed, null, ct);
    }

    /// <summary>
    /// Replaces the email address, which renews the security stamp. Unless <paramref name="markConfirmed"/>, the
    /// account cannot sign in until the user confirms the new address, and the new address is emailed instructions
    /// for the website's Resend email confirmation page.
    /// </summary>
    public async Task<AdminResult<AdminUserActionResultDto>> ChangeEmailAsync(AdminActor actor, string id, string? email,
        bool markConfirmed, string? reason, CancellationToken ct = default)
    {
        if (ValidateReason(ref reason, required: true) is string reasonError)
            return Refuse(AdminResultStatus.BadRequest, reasonError, AdminRefusalCodes.Reason);
        email = email?.Trim() ?? string.Empty;
        if (email.Length == 0 || email.Length > MaxEmailLength || !new EmailAddressAttribute().IsValid(email))
            return Refuse(AdminResultStatus.BadRequest, $"Enter a valid email address of at most {MaxEmailLength} characters.", AdminRefusalCodes.Email);

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFoundResult();
        if (Guard(actor, user, GuardPolicy.RefuseSelfAndAdministrators) is { } refused) return refused;
        if (_userManager.NormalizeEmail(email) == user.NormalizedEmail)
            return Refuse(AdminResultStatus.Conflict, "The account already has this email address.", AdminRefusalCodes.AlreadyDone);
        // Checked here rather than left to RequireUniqueEmail, which not every Identity setup enables.
        var owner = await _userManager.FindByEmailAsync(email);
        if (owner != null && owner.Id != user.Id)
            return Refuse(AdminResultStatus.Conflict, "Another account already uses this email address.", AdminRefusalCodes.EmailTaken);
        var facts = await FactsAsync(user.Id, ct);
        var before = StatusOf(user, facts);

        // Leaves the address unconfirmed and renews the security stamp, which also voids pending confirmation links.
        if (IdentityFailure(await _userManager.SetEmailAsync(user, email)) is { } failed1) return failed1;

        int minutes = _configuration.GetValue("IdentityLockout:SecurityStampValidationMinutes", 5);
        string summary;
        if (markConfirmed)
        {
            string token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            if (IdentityFailure(await _userManager.ConfirmEmailAsync(user, token)) is { } failed2) return failed2;
            summary = $"Email changed and marked confirmed. Signed-in sessions end within {minutes} minutes.";
        }
        else if (await SendChangeEmailInstructionsAsync(user, email))
        {
            summary = "Email changed. Instructions for confirming the new address were emailed to it; the account cannot sign in until the user confirms it.";
        }
        else
        {
            summary = "Email changed, but the instructions email could not be sent. Ask the user to confirm the new address from the "
                + "Resend email confirmation page of the account website; the account cannot sign in until then.";
        }

        return await FinishAsync(actor, user, facts, before, AdminUserActionKind.EmailChanged, reason,
            summary, SecurityEventKind.AdminEmailChanged, null, ct);
    }

    /// <summary>Emails the new address how to confirm it; false when no sender is configured or the send fails. Never logs the address.</summary>
    private async Task<bool> SendChangeEmailInstructionsAsync(ApplicationUser user, string email)
    {
        IEmailSender? sender;
        try
        {
            sender = _services?.GetService<IEmailSender>();
        }
        catch (Exception)
        {
            sender = null;
        }
        if (sender == null)
        {
            _logger.LogWarning("No email sender is configured; the change-email instructions for user {UserId} were not sent.", user.Id);
            return false;
        }

        var (subject, html) = AdminEmailChangeEmailBuilder.Build(user.UserName ?? string.Empty, email,
            AdminEmailChangeEmailBuilder.ResendConfirmationUrl(_options.AccountSiteBaseUrl));
        try
        {
            await sender.SendEmailAsync(email, subject, html);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The change-email instructions for user {UserId} could not be sent.", user.Id);
            return false;
        }
    }

    /// <summary>Turns two-factor authentication off and replaces the authenticator key, which also renews the security stamp.</summary>
    public async Task<AdminResult<AdminUserActionResultDto>> ResetTwoFactorAsync(AdminActor actor, string id, string? reason,
        CancellationToken ct = default)
    {
        if (ValidateReason(ref reason, required: false) is string reasonError)
            return Refuse(AdminResultStatus.BadRequest, reasonError, AdminRefusalCodes.Reason);
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFoundResult();
        if (Guard(actor, user, GuardPolicy.RefuseSelfAndAdministrators) is { } refused) return refused;
        var facts = await FactsAsync(user.Id, ct);
        var before = StatusOf(user, facts);

        if (IdentityFailure(await _userManager.SetTwoFactorEnabledAsync(user, false)) is { } failed1) return failed1;
        if (IdentityFailure(await _userManager.ResetAuthenticatorKeyAsync(user)) is { } failed2) return failed2;

        return await FinishAsync(actor, user, facts, before, AdminUserActionKind.TwoFactorReset, reason,
            "Two-factor authentication reset.", SecurityEventKind.AdminTwoFactorReset, null, ct);
    }

    // --- Moved actions: the AI-chat block and the deletion hold; their services write the audit row ---

    public async Task<AdminResult<AdminUserActionResultDto>> BlockFromAiChatAsync(AdminActor actor, string id, string? reason,
        CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFoundResult();
        if (Guard(actor, user, GuardPolicy.RefuseSelfAndAdministrators) is { } refused) return refused;
        var facts = await FactsAsync(user.Id, ct);
        var before = StatusOf(user, facts);

        string? error = await _access.BlockAsync(user.Id, reason ?? string.Empty, actor.UserId);
        if (error == "User not found.") return NotFoundResult();
        if (error != null) return Refuse(AdminResultStatus.BadRequest, error, AdminRefusalCodes.Reason);

        await _db.Entry(user).ReloadAsync(ct);
        return await ResultAsync(actor, user.Id, before, StatusOf(user, facts), "Blocked from the AI chat.", ct);
    }

    public async Task<AdminResult<AdminUserActionResultDto>> UnblockFromAiChatAsync(AdminActor actor, string id, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFoundResult();
        var facts = await FactsAsync(user.Id, ct);
        var before = StatusOf(user, facts);

        string? error = await _access.UnblockAsync(user.Id, actor.UserId);
        if (error != null) return NotFoundResult();

        await _db.Entry(user).ReloadAsync(ct);
        return await ResultAsync(actor, user.Id, before, StatusOf(user, facts), "Unblocked from the AI chat.", ct);
    }

    public async Task<AdminResult<AdminUserActionResultDto>> PlaceHoldAsync(AdminActor actor, string id, string? reason, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFoundResult();
        var before = StatusOf(user, await FactsAsync(user.Id, ct));

        var review = await _review.PlaceHoldAsync(user.Id, reason, actor.UserId, ct);
        if (FromReview(review) is { } failed) return failed;

        return await ResultAsync(actor, user.Id, before, StatusOf(user, await FactsAsync(user.Id, ct)), "Deletion hold placed.", ct);
    }

    public async Task<AdminResult<AdminUserActionResultDto>> ReleaseHoldAsync(AdminActor actor, string id, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFoundResult();
        var before = StatusOf(user, await FactsAsync(user.Id, ct));

        var review = await _review.ReleaseHoldAsync(user.Id, actor.UserId, ct);
        if (FromReview(review) is { } failed) return failed;

        return await ResultAsync(actor, user.Id, before, StatusOf(user, await FactsAsync(user.Id, ct)),
            "Deletion hold released. A pending deletion request still needs a decision.", ct);
    }

    private static AdminResult<AdminUserActionResultDto>? FromReview(ReviewActionResult result) => result.Status switch
    {
        ReviewActionStatus.Ok => null,
        ReviewActionStatus.NotFound => Refuse(AdminResultStatus.NotFound, result.Message ?? "Not found.", AdminRefusalCodes.NotFound),
        ReviewActionStatus.Conflict => Refuse(AdminResultStatus.Conflict, result.Message ?? "Conflict.", AdminRefusalCodes.AlreadyDone),
        _ => Refuse(AdminResultStatus.BadRequest, result.Message ?? "Bad request.", AdminRefusalCodes.Reason)
    };

    // --- Deletion ---

    public const string CategoryGameLogs = "game-logs";
    public const string CategoryBones = "bones";
    public const string CategorySaveFiles = "save-files";
    public const string CategoryChats = "chats";
    public const string CategoryAiUsage = "ai-usage";
    public const string CategorySecurityEvents = "security-events";
    public const string CategoryGroups = "groups";
    public const string CategoryConfigurations = "configurations";
    public const string CategoryBenchmarkRecords = "benchmark-records";

    public async Task<AdminResult<AccountDeletionImpactDto>> GetDeletionImpactAsync(AdminActor actor, string id, CancellationToken ct = default)
    {
        var user = await Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null) return new(AdminResultStatus.NotFound, Message: "User not found.", Code: AdminRefusalCodes.NotFound);
        return new(AdminResultStatus.Ok, await ComputeImpactAsync(actor, user, ct));
    }

    /// <summary>The refusals that stop a deletion and the counts of what it would erase.</summary>
    private async Task<AccountDeletionImpactDto> ComputeImpactAsync(AdminActor actor, ApplicationUser user, CancellationToken ct)
    {
        string id = user.Id;
        var impact = new AccountDeletionImpactDto { UserId = id, UserName = user.UserName ?? string.Empty };
        var facts = await FactsAsync(id, ct);

        if (string.Equals(id, actor.UserId, StringComparison.Ordinal))
            impact.Refusals.Add(new AdminRefusalDto { Code = AdminRefusalCodes.Self, Message = "You cannot delete your own account here." });
        else if (IsAdministrator(user))
            impact.Refusals.Add(new AdminRefusalDto { Code = AdminRefusalCodes.Administrator, Message = "Administrators cannot be deleted. Remove the account from Admins first." });
        if (facts.HasActiveHold)
            impact.Refusals.Add(new AdminRefusalDto { Code = AdminRefusalCodes.Hold, Message = "Release the deletion hold first." });
        if (facts.HasPendingDeletionRequest)
            impact.Refusals.Add(new AdminRefusalDto { Code = AdminRefusalCodes.PendingRequest, Message = "The account has a pending deletion request. Decide it in Deletion requests." });
        if (_review.MissingErasurePathsMessage() is string missing)
            impact.Refusals.Add(new AdminRefusalDto { Code = AdminRefusalCodes.ErasurePaths, Message = missing });

        int benchmark =
            await _db.BenchmarkRuns.CountAsync(r => r.StartedByUserId == id, ct)
            + await _db.BenchmarkRunSeries.CountAsync(r => r.StartedByUserId == id, ct)
            + await _db.BenchmarkRunGroups.CountAsync(r => r.CreatedByUserId == id, ct)
            + await _db.BenchmarkGroupAnalyses.CountAsync(r => r.ComputedByUserId == id, ct)
            + await _db.BenchmarkBatteries.CountAsync(r => r.CreatedByUserId == id, ct)
            + await _db.BenchmarkBatteryRuns.CountAsync(r => r.StartedByUserId == id, ct)
            + await _db.BenchmarkBatteryAnalyses.CountAsync(r => r.ComputedByUserId == id, ct);

        impact.Categories.AddRange(new[]
        {
            Category(CategoryGameLogs, "Game logs", await _db.GameLog.CountAsync(g => g.AspNetUserId == id, ct), true,
                "Scrubbed and detached; their top-score entries disappear."),
            Category(CategoryBones, "Bones", await _db.Bones.CountAsync(b => b.AspNetUserId == id, ct), true,
                "The rows and the bones files are deleted."),
            Category(CategorySaveFiles, "Save-file tracking", await _db.SaveFileTrackings.CountAsync(s => s.AspNetUserId == id, ct), true,
                "Deleted."),
            Category(CategoryChats, "Overseer chats", await _db.ChatSession.CountAsync(s => s.AspNetUserId == id, ct), true,
                "Deleted with their messages and attachments."),
            Category(CategoryAiUsage, "AI usage records",
                await _db.SystemAiUsageLogs.CountAsync(l => l.AspNetUserId == id, ct) + await _db.UserKeyAiUsageLogs.CountAsync(l => l.AspNetUserId == id, ct),
                false, "Deleted."),
            Category(CategorySecurityEvents, "Security events", await _db.SecurityEvents.CountAsync(e => e.AspNetUserId == id, ct), false,
                "Deleted. The admin log keeps its record."),
            Category(CategoryGroups, "Group memberships", await _db.UserGroups.CountAsync(g => g.AspNetUserId == id, ct), false,
                "Removed."),
            Category(CategoryConfigurations, "Configuration assignments",
                await _db.UserSystemAiApiConfigurations.CountAsync(c => c.AspNetUserId == id, ct), false, "Removed."),
            Category(CategoryBenchmarkRecords, "Benchmark records", benchmark, false, "Kept; the attribution is removed.")
        });
        return impact;
    }

    private static AdminDeletionCategoryDto Category(string key, string label, int count, bool important, string note) =>
        new() { Key = key, Label = label, Count = count, Important = important, Note = note };

    /// <summary>
    /// Deletes the account permanently. The typed name must equal the user name, and the acknowledged categories
    /// must be exactly the important ones with a count above zero, so a stale dialog never deletes more than it showed.
    /// </summary>
    public async Task<AdminResult<AdminUserActionResultDto>> DeleteAsync(AdminActor actor, string id, string? confirmUserName, string? reason,
        IReadOnlyCollection<string>? acknowledgedCategories, CancellationToken ct = default)
    {
        if (ValidateReason(ref reason, required: true) is string reasonError)
            return Refuse(AdminResultStatus.BadRequest, reasonError, AdminRefusalCodes.Reason);
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFoundResult();

        var impact = await ComputeImpactAsync(actor, user, ct);
        if (impact.Refusals.Count > 0)
            return Refuse(AdminResultStatus.Conflict, impact.Refusals[0].Message, impact.Refusals[0].Code);

        if (!string.Equals((confirmUserName ?? string.Empty).Trim(), (user.UserName ?? string.Empty).Trim(), StringComparison.Ordinal)
            || string.IsNullOrEmpty(user.UserName))
        {
            return Refuse(AdminResultStatus.BadRequest, "Type the account's user name exactly to confirm the deletion.", AdminRefusalCodes.ConfirmName);
        }

        var required = impact.Categories.Where(c => c.Important && c.Count > 0).Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        var given = (acknowledgedCategories ?? Array.Empty<string>())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .ToHashSet(StringComparer.Ordinal);
        if (!required.SetEquals(given))
        {
            return Refuse(AdminResultStatus.BadRequest,
                "What the account holds has changed. Review what will be erased and confirm again.", AdminRefusalCodes.Acknowledgments);
        }

        return await EraseAsync(actor, user, impact, reason!, null, ct);
    }

    /// <summary>
    /// The bulk runner's deletion: the operation's typed phrase and acknowledgments stand in for the per-account
    /// ones, but the guards and the impact refusals apply as they do to a single deletion.
    /// </summary>
    internal async Task<AdminResult<AdminUserActionResultDto>> DeleteForBulkAsync(AdminActor actor, string id, string? reason,
        long bulkOperationId, CancellationToken ct = default)
    {
        if (ValidateReason(ref reason, required: true) is string reasonError)
            return Refuse(AdminResultStatus.BadRequest, reasonError, AdminRefusalCodes.Reason);
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFoundResult();

        var impact = await ComputeImpactAsync(actor, user, ct);
        if (impact.Refusals.Count > 0)
            return Refuse(AdminResultStatus.Conflict, impact.Refusals[0].Message, impact.Refusals[0].Code);

        return await EraseAsync(actor, user, impact, reason!, bulkOperationId, ct);
    }

    /// <summary>Writes the audit row before erasing; a failed erasure appends a second row.</summary>
    private async Task<AdminResult<AdminUserActionResultDto>> EraseAsync(AdminActor actor, ApplicationUser user, AccountDeletionImpactDto impact,
        string reason, long? bulkOperationId, CancellationToken ct)
    {
        string userId = user.Id;
        string userName = user.UserName ?? string.Empty;
        var before = StatusOf(user, await FactsAsync(userId, ct));
        string detail = $"Deleted {userName}. " + string.Join(", ", impact.Categories.Select(c => $"{c.Label}: {c.Count}")) + ".";

        _db.AdminUserActions.Add(AdminUserActionLog.Entry(actor, userId, userName, AdminUserActionKind.Deleted, reason, detail, UtcNow, bulkOperationId));
        await _db.SaveChangesAsync(ct);

        try
        {
            await AccountErasure.EraseAsync(_db, _userManager, user, _review.ErasurePaths, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Deleting user {UserId} failed part-way.", userId);
            _db.ChangeTracker.Clear();
            _db.AdminUserActions.Add(AdminUserActionLog.Entry(actor, userId, userName, AdminUserActionKind.Deleted, reason,
                "Deletion failed: " + ex.Message, UtcNow, bulkOperationId));
            await _db.SaveChangesAsync(CancellationToken.None);
            return Refuse(AdminResultStatus.Failed, "Deletion failed: " + ex.Message, AdminRefusalCodes.ErasureFailed);
        }

        _ephemeralSessions?.CloseAllForUser(userId);
        return new(AdminResultStatus.Ok, new AdminUserActionResultDto
        {
            User = null,
            Before = AdminUserStatusSetDto.From(before),
            After = AdminUserStatusSetDto.From(AdminUserStatusSet.Deleted),
            Summary = $"Deleted {userName}.",
            Deleted = true
        });
    }
}
