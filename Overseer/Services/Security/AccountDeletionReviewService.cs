using GnollHackServer.Data;
using GnollHackServer.Data.Privacy;
using GnollHackServer.Data.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;

namespace Overseer.Services.Security;

/// <summary>How an administrator's deletion-review action ended.</summary>
public enum ReviewActionStatus { Ok, NotFound, BadRequest, Conflict }

public sealed record ReviewActionResult(ReviewActionStatus Status, string? Message = null, AccountDeletionDecisionResultDto? Decision = null);

/// <summary>
/// Account-deletion holds and the requests they held: listing them for review, approving (which erases the account
/// through the same code as self-service deletion), declining, and placing and releasing holds. Overseer sends the
/// decision emails; the website sends the review email. Each decision and each hold change is written to the admin log.
/// </summary>
public class AccountDeletionReviewService
{
    public const int MaxHoldReasonLength = 512;
    public const int MaxDecisionNoteLength = 512;

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly AccountHoldOptions _options;
    private readonly ILogger<AccountDeletionReviewService> _logger;
    private readonly ISecurityEventRecorder? _securityEvents;
    private readonly IServiceProvider _services;

    public AccountDeletionReviewService(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IConfiguration configuration,
        AccountHoldOptions options, ILogger<AccountDeletionReviewService> logger, IServiceProvider services,
        ISecurityEventRecorder? securityEvents = null)
    {
        _db = db;
        _userManager = userManager;
        _configuration = configuration;
        _options = options;
        _logger = logger;
        _services = services;
        _securityEvents = securityEvents;
    }

    /// <summary>The erasure paths Overseer reads, which must equal the website's.</summary>
    public AccountErasurePaths ErasurePaths => new(_configuration["DumpLogPath"], _configuration["ConversationsDataLocation"]);

    /// <summary>Null when both erasure paths are configured; otherwise names the missing settings.</summary>
    public string? MissingErasurePathsMessage()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(_configuration["DumpLogPath"])) missing.Add("DumpLogPath");
        if (string.IsNullOrWhiteSpace(_configuration["ConversationsDataLocation"])) missing.Add("ConversationsDataLocation");
        return missing.Count == 0
            ? null
            : $"Account erasure needs {string.Join(" and ", missing)} in Overseer's configuration, with the same value as the website's.";
    }

    /// <param name="status">"Pending", "Completed" or "Declined"; null or unknown: every status.</param>
    public async Task<AccountDeletionRequestsPageDto> ListAsync(string? status, string? search, int skip, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take <= 0 ? 20 : take, 1, 100);
        skip = Math.Max(0, skip);
        DateTime now = DateTime.UtcNow;

        var query = _db.AccountDeletionRequests.AsNoTracking();
        if (Enum.TryParse<AccountDeletionRequestStatus>(status, ignoreCase: true, out var parsed))
        {
            query = query.Where(r => r.Status == parsed);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(r => r.UserNameSnapshot != null && r.UserNameSnapshot.Contains(term));
        }

        int total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(r => r.Status)
            .ThenBy(r => r.RequestedUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var deciderIds = rows.Where(r => r.DecidedByUserId != null).Select(r => r.DecidedByUserId!).Distinct().ToList();
        var deciderNames = await _db.Users.AsNoTracking()
            .Where(u => deciderIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UserName, ct);

        var dtos = new List<AccountDeletionRequestDto>();
        foreach (var r in rows)
        {
            int ageDays = Math.Max(0, (int)(now - r.RequestedUtc).TotalDays);
            bool pending = r.Status == AccountDeletionRequestStatus.Pending;

            string? currentReasons = null;
            bool manual = r.HoldReasons.Contains("Manual hold", StringComparison.Ordinal);
            bool automatic = r.HoldReasons.Contains("Automatic:", StringComparison.Ordinal);
            if (pending && r.AspNetUserId != null)
            {
                var verdict = await AccountHoldQueries.EvaluateAsync(_db, r.AspNetUserId, _options, now, ct);
                currentReasons = verdict.Reasons;
                manual |= verdict.Manual;
                automatic |= verdict.Automatic;
            }

            dtos.Add(new AccountDeletionRequestDto
            {
                Id = r.Id,
                UserId = r.AspNetUserId,
                UserName = r.UserNameSnapshot,
                RequestedUtc = DateTime.SpecifyKind(r.RequestedUtc, DateTimeKind.Utc),
                AgeDays = ageDays,
                IsOverdue = pending && ageDays >= _options.ReviewHighlightDays,
                Status = r.Status.ToString(),
                HoldReasons = r.HoldReasons,
                CurrentReasons = currentReasons,
                ManualHold = manual,
                AutomaticRule = automatic,
                DecidedByUserName = r.DecidedByUserId != null && deciderNames.TryGetValue(r.DecidedByUserId, out var name) ? name : null,
                DecidedUtc = r.DecidedUtc.HasValue ? DateTime.SpecifyKind(r.DecidedUtc.Value, DateTimeKind.Utc) : null,
                DecisionNote = r.DecisionNote
            });
        }

        DateTime overdueBefore = now.AddDays(-_options.ReviewHighlightDays);
        string? missing = MissingErasurePathsMessage();
        return new AccountDeletionRequestsPageDto
        {
            TotalCount = total,
            PendingCount = await _db.AccountDeletionRequests.CountAsync(r => r.Status == AccountDeletionRequestStatus.Pending, ct),
            OverdueCount = await _db.AccountDeletionRequests.CountAsync(r => r.Status == AccountDeletionRequestStatus.Pending && r.RequestedUtc <= overdueBefore, ct),
            ErasurePathsConfigured = missing == null,
            ErasurePathsMessage = missing,
            Rows = dtos
        };
    }

    /// <summary>Erases the account behind a pending request. The typed name must equal the request's user name.</summary>
    public async Task<ReviewActionResult> ApproveAsync(long id, string? confirmUserName, string adminId, CancellationToken ct = default)
    {
        var request = await _db.AccountDeletionRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request == null) return new(ReviewActionStatus.NotFound, "Deletion request not found.");
        if (request.Status != AccountDeletionRequestStatus.Pending)
            return new(ReviewActionStatus.Conflict, "This deletion request has already been decided.");

        string? missing = MissingErasurePathsMessage();
        if (missing != null) return new(ReviewActionStatus.Conflict, missing);

        if (string.IsNullOrEmpty(request.UserNameSnapshot)
            || !string.Equals((confirmUserName ?? string.Empty).Trim(), request.UserNameSnapshot, StringComparison.Ordinal))
        {
            return new(ReviewActionStatus.BadRequest, "Type the account's user name exactly to confirm the deletion.");
        }

        var user = request.AspNetUserId == null ? null : await _userManager.FindByIdAsync(request.AspNetUserId);
        var actor = await AdminUserActionLog.ResolveActorAsync(_db, adminId, ct);
        string subjectUserId = request.AspNetUserId ?? string.Empty;
        string? subjectUserName = request.UserNameSnapshot;
        var decision = new AccountDeletionDecisionResultDto();
        if (user != null)
        {
            var erased = await AccountErasure.EraseAsync(_db, _userManager, user, ErasurePaths, ct);
            decision.BonesFilesMissing = erased.BonesFilesMissing;
            decision.FoldersMissing = erased.FoldersMissing;
        }

        string? email = request.EmailSnapshot;
        request.Status = AccountDeletionRequestStatus.Completed;
        request.DecidedByUserId = adminId;
        request.DecidedUtc = DateTime.UtcNow;
        request.AspNetUserId = null;
        request.UserNameSnapshot = null;
        request.EmailSnapshot = null;
        _db.AdminUserActions.Add(AdminUserActionLog.Entry(actor, subjectUserId, subjectUserName, AdminUserActionKind.DeletionRequestApproved,
            null, $"Deletion request {request.Id} approved; the account is erased.", request.DecidedUtc.Value));
        await _db.SaveChangesAsync(ct);

        await SendDecisionEmailAsync(email, "Your GnollHack account has been deleted",
            "<p>Your account has been deleted.</p>", decision);
        return new(ReviewActionStatus.Ok, Decision: decision);
    }

    /// <summary>Declines a pending request: the account and any hold remain, and the user is emailed. The note is not emailed.</summary>
    public async Task<ReviewActionResult> DeclineAsync(long id, string? note, string adminId, CancellationToken ct = default)
    {
        note = (note ?? string.Empty).Trim();
        if (note.Length == 0 || note.Length > MaxDecisionNoteLength)
            return new(ReviewActionStatus.BadRequest, $"A note of 1 to {MaxDecisionNoteLength} characters is required.");

        var request = await _db.AccountDeletionRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request == null) return new(ReviewActionStatus.NotFound, "Deletion request not found.");
        if (request.Status != AccountDeletionRequestStatus.Pending)
            return new(ReviewActionStatus.Conflict, "This deletion request has already been decided.");

        request.Status = AccountDeletionRequestStatus.Declined;
        request.DecidedByUserId = adminId;
        request.DecidedUtc = DateTime.UtcNow;
        request.DecisionNote = note;
        var actor = await AdminUserActionLog.ResolveActorAsync(_db, adminId, ct);
        _db.AdminUserActions.Add(AdminUserActionLog.Entry(actor, request.AspNetUserId ?? string.Empty, request.UserNameSnapshot,
            AdminUserActionKind.DeletionRequestDeclined, note, $"Deletion request {request.Id} declined; the account is kept.", request.DecidedUtc.Value));
        await _db.SaveChangesAsync(ct);

        string operatorAddress = _configuration["ReportEmailAddress"] ?? "gnollhack@hyvanmielenpelit.fi";
        var decision = new AccountDeletionDecisionResultDto();
        await SendDecisionEmailAsync(request.EmailSnapshot, "Your GnollHack account deletion request",
            $"<p>Your request to delete your account could not be completed. Please contact {System.Net.WebUtility.HtmlEncode(operatorAddress)}.</p>",
            decision);
        return new(ReviewActionStatus.Ok, Decision: decision);
    }

    public async Task<ReviewActionResult> PlaceHoldAsync(string userId, string? reason, string adminId, CancellationToken ct = default)
    {
        reason = (reason ?? string.Empty).Trim();
        if (reason.Length == 0 || reason.Length > MaxHoldReasonLength)
            return new(ReviewActionStatus.BadRequest, $"A reason of 1 to {MaxHoldReasonLength} characters is required.");
        if (!await _db.Users.AnyAsync(u => u.Id == userId, ct))
            return new(ReviewActionStatus.NotFound, "User not found.");
        if (await _db.AccountHolds.AnyAsync(h => h.AspNetUserId == userId && h.ReleasedUtc == null, ct))
            return new(ReviewActionStatus.Conflict, "The account already has an active deletion hold.");

        var hold = new AccountHold
        {
            AspNetUserId = userId,
            Reason = reason,
            PlacedByUserId = adminId,
            PlacedUtc = DateTime.UtcNow
        };
        _db.AccountHolds.Add(hold);
        var actor = await AdminUserActionLog.ResolveActorAsync(_db, adminId, ct);
        string? subjectName = await _db.Users.Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync(ct);
        _db.AdminUserActions.Add(AdminUserActionLog.Entry(actor, userId, subjectName, AdminUserActionKind.HoldPlaced,
            reason, "Deletion hold placed.", hold.PlacedUtc));
        await _db.SaveChangesAsync(ct);
        _securityEvents?.Record(new SecurityEventEntry(SecurityEventKind.AccountHoldPlaced, UserId: userId, Detail: reason));
        return new(ReviewActionStatus.Ok);
    }

    /// <summary>Releases the active hold. A pending request still needs an explicit decision.</summary>
    public async Task<ReviewActionResult> ReleaseHoldAsync(string userId, string adminId, CancellationToken ct = default)
    {
        var holds = await _db.AccountHolds.Where(h => h.AspNetUserId == userId && h.ReleasedUtc == null).ToListAsync(ct);
        if (holds.Count == 0) return new(ReviewActionStatus.NotFound, "The account has no active deletion hold.");

        DateTime releasedUtc = DateTime.UtcNow;
        foreach (var hold in holds)
        {
            hold.ReleasedUtc = releasedUtc;
            hold.ReleasedByUserId = adminId;
        }
        var actor = await AdminUserActionLog.ResolveActorAsync(_db, adminId, ct);
        string? subjectName = await _db.Users.Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync(ct);
        _db.AdminUserActions.Add(AdminUserActionLog.Entry(actor, userId, subjectName, AdminUserActionKind.HoldReleased,
            holds[0].Reason, "Deletion hold released.", releasedUtc));
        await _db.SaveChangesAsync(ct);
        _securityEvents?.Record(new SecurityEventEntry(SecurityEventKind.AccountHoldReleased, UserId: userId, Detail: holds[0].Reason));
        return new(ReviewActionStatus.Ok);
    }

    /// <summary>The pending requests and how many are overdue, for the admin alert.</summary>
    public static async Task<(int Pending, int Overdue, long? NewestId)> CountPendingAsync(ApplicationDbContext db, AccountHoldOptions options,
        DateTime utcNow, CancellationToken ct = default)
    {
        var pending = db.AccountDeletionRequests.AsNoTracking().Where(r => r.Status == AccountDeletionRequestStatus.Pending);
        int count = await pending.CountAsync(ct);
        if (count == 0) return (0, 0, null);
        DateTime overdueBefore = utcNow.AddDays(-options.ReviewHighlightDays);
        int overdue = await pending.CountAsync(r => r.RequestedUtc <= overdueBefore, ct);
        long newest = await pending.MaxAsync(r => r.Id, ct);
        return (count, overdue, newest);
    }

    /// <summary>
    /// Sends a decision email when an address and a sender exist. A send that cannot be started is logged and reported
    /// in <paramref name="decision"/>; the decision stands.
    /// </summary>
    private async Task SendDecisionEmailAsync(string? to, string subject, string body, AccountDeletionDecisionResultDto decision)
    {
        if (string.IsNullOrWhiteSpace(to))
        {
            decision.EmailError = "The account has no email address.";
            return;
        }

        EmailSender? sender;
        try
        {
            sender = _services.GetService<EmailSender>();
        }
        catch
        {
            sender = null;
        }
        if (sender == null)
        {
            decision.EmailError = "No email sender is configured.";
            return;
        }

        try
        {
            await sender.SendEmailAsync(to, subject, body);
            decision.EmailSent = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The account deletion decision email could not be sent.");
            decision.EmailError = ex.Message;
        }
    }
}
