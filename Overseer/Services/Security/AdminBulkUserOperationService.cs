using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Extensions;
using Overseer.Models;

namespace Overseer.Services.Security;

/// <summary>Hands a queued bulk operation to the runner.</summary>
public interface IAdminBulkOperationQueue
{
    void Enqueue(long operationId);
}

/// <summary>A bulk operation's options as stored in <see cref="AdminBulkOperation.OptionsJson"/>.</summary>
public sealed class AdminBulkStoredOptions
{
    public DateTimeOffset? Until { get; set; }
    public string? Reason { get; set; }

    /// <summary>Set by the runner when it stops before the last account.</summary>
    public string? StopMessage { get; set; }
}

/// <summary>What a preview left out, as stored in <see cref="AdminBulkOperation.RefusedSummaryJson"/>.</summary>
public sealed class AdminBulkStoredSummary
{
    public List<AdminBulkRefusedGroupDto> Refused { get; set; } = new();
    public int NoOpCount { get; set; }
    public int TruncatedCount { get; set; }
    public int AdvancedCount { get; set; }
}

/// <summary>The acknowledgment keys an execute request carries.</summary>
public static class AdminBulkAcknowledgments
{
    public const string Reviewed = "reviewed";
    public const string Irreversible = "irreversible";
    public const string Advanced = "advanced";
}

/// <summary>How the runner left one account.</summary>
public sealed record AdminBulkItemResult(AdminBulkItemOutcome Outcome, string? Message, string? BeforeStatus = null, string? AfterStatus = null);

/// <summary>
/// The Bulk Actions wizard. A preview fixes the item list, refuses protected accounts outright and expires; an
/// execute checks the actor, the expiry, the acknowledgments and, for delete, the typed phrase, and queues the run.
/// The runner calls <see cref="ProcessItemAsync"/> once per account, which re-checks the criteria and the
/// protections against the account as it is then, and calls the same action a single-user dialog does.
/// </summary>
public class AdminBulkUserOperationService
{
    public const int MaxExampleNames = 50;
    public const string ChangedSincePreview = "Changed since the preview: ";
    public const string AlreadyDone = "Already done: ";

    public const string DeleteNarrowingMessage =
        "Bulk deletion needs at least one of: Email not confirmed, Last sign-in, Registered more than 30 days ago.";

    private static readonly JsonSerializerOptions StoredJson = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly AdminUserManagementOptions _options;
    private readonly AdminUserManagementService _users;
    private readonly AccountDeletionReviewService _review;
    private readonly IAdminBulkOperationQueue _queue;
    private readonly TimeProvider _time;

    public AdminBulkUserOperationService(ApplicationDbContext db, IConfiguration configuration, AdminUserManagementOptions options,
        AdminUserManagementService users, AccountDeletionReviewService review, IAdminBulkOperationQueue queue, TimeProvider? time = null)
    {
        _db = db;
        _configuration = configuration;
        _options = options;
        _users = users;
        _review = review;
        _queue = queue;
        _time = time ?? TimeProvider.System;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    private IQueryable<ApplicationUser> Users => _db.Users.OfType<ApplicationUser>();

    public static string DangerLevel(AdminBulkActionKind action) => action switch
    {
        AdminBulkActionKind.Delete => "danger",
        AdminBulkActionKind.LockOut or AdminBulkActionKind.Ban => "caution",
        _ => "safe"
    };

    /// <summary>The acknowledgments an execute needs, before the "advanced" one that including the advanced accounts adds.</summary>
    public static IReadOnlyList<string> RequiredAcknowledgments(AdminBulkActionKind action) => DangerLevel(action) switch
    {
        "danger" => new[] { AdminBulkAcknowledgments.Reviewed, AdminBulkAcknowledgments.Irreversible },
        "caution" => new[] { AdminBulkAcknowledgments.Reviewed },
        _ => Array.Empty<string>()
    };

    /// <summary>The phrase a bulk delete of <paramref name="count"/> accounts is confirmed with.</summary>
    public static string RequiredPhrase(int count) => count == 1 ? "DELETE 1 ACCOUNT" : $"DELETE {count} ACCOUNTS";

    /// <summary>True when the criteria narrow a bulk delete enough to be allowed.</summary>
    public static bool NarrowsDeletion(AdminUserCriteria criteria)
    {
        if (AdminUserCriteria.ParseYesNo(criteria.EmailConfirmed) == false) return true;
        if (criteria.RegisteredOlderThanDays is >= 30) return true;
        return AdminUserCriteria.TryParseLastSignIn(criteria.LastSignIn, out var signIn)
            && (signIn.Kind == AdminLastSignInFilterKind.Never
                || (signIn.Kind == AdminLastSignInFilterKind.OlderThanDays && signIn.Days >= 30));
    }

    public static AdminBulkStoredOptions ReadOptions(AdminBulkOperation op) =>
        (string.IsNullOrEmpty(op.OptionsJson) ? null : JsonSerializer.Deserialize<AdminBulkStoredOptions>(op.OptionsJson, StoredJson))
        ?? new AdminBulkStoredOptions();

    public static AdminUserCriteria ReadCriteria(AdminBulkOperation op) =>
        (string.IsNullOrEmpty(op.CriteriaJson) ? null : JsonSerializer.Deserialize<AdminUserCriteria>(op.CriteriaJson, StoredJson))
        ?? new AdminUserCriteria();

    public static string WriteOptions(AdminBulkStoredOptions options) => JsonSerializer.Serialize(options, StoredJson);

    private static AdminBulkStoredSummary ReadSummary(AdminBulkOperation op) =>
        (string.IsNullOrEmpty(op.RefusedSummaryJson) ? null : JsonSerializer.Deserialize<AdminBulkStoredSummary>(op.RefusedSummaryJson, StoredJson))
        ?? new AdminBulkStoredSummary();

    // --- Classification, shared by the preview and the runner ---

    private sealed record AccountFacts(bool HasHold, bool HasPending, bool HasGameData, bool HasOverseerData);

    private sealed record Classification(string? RefusalCode, string? RefusalMessage, string? NoOp, bool Advanced);

    private Classification Classify(AdminActor actor, AdminBulkActionKind action, AdminBulkStoredOptions options, ApplicationUser user,
        AccountFacts facts, DateTime now)
    {
        if (string.Equals(user.Id, actor.UserId, StringComparison.Ordinal))
            return new(AdminRefusalCodes.Self, "Your own account", null, false);
        if (_configuration.IsAdmin(user.UserName))
            return new(AdminRefusalCodes.Administrator, "Administrators", null, false);

        if (action == AdminBulkActionKind.Delete)
        {
            if (facts.HasHold) return new(AdminRefusalCodes.Hold, "An active deletion hold", null, false);
            if (facts.HasPending) return new(AdminRefusalCodes.PendingRequest, "A pending deletion request", null, false);
            if (facts.HasGameData) return new("game-data", "Game data (game logs, bones or save files)", null, false);
            if (facts.HasOverseerData) return new("overseer-data", "Overseer chats, AI usage or benchmark records", null, false);
            if (user.LastSignInUtc != null && user.LastSignInUtc >= now.AddDays(-_options.BulkDeleteRecentSignInDays))
                return new("recent-sign-in", $"Signed in within {_options.BulkDeleteRecentSignInDays} days", null, false);
            // An account without a registration date is treated as new.
            if (user.CreatedUtc == null || user.CreatedUtc > now.AddDays(-_options.BulkDeleteMinAccountAgeDays))
                return new("too-new", $"Registered within {_options.BulkDeleteMinAccountAgeDays} days", null, false);
            return new(null, null, null, user.EmailConfirmed);
        }

        var nowOffset = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc), TimeSpan.Zero);
        bool lockedOut = user.LockoutEnd != null && user.LockoutEnd > nowOffset;
        string? noOp = action switch
        {
            AdminBulkActionKind.Unlock when !lockedOut => "not locked out",
            AdminBulkActionKind.LockOut when options.Until != null && user.LockoutEnd != null && user.LockoutEnd >= options.Until => "already locked out until then",
            AdminBulkActionKind.Ban when user.IsBanned == true => "already banned",
            AdminBulkActionKind.Unban when user.IsBanned != true => "not banned",
            _ => null
        };
        return new(null, null, noOp, false);
    }

    // --- Preview ---

    public async Task<AdminResult<AdminBulkPreviewDto>> PreviewAsync(AdminActor actor, AdminBulkPreviewRequest request, CancellationToken ct = default)
    {
        var criteria = request.Criteria ?? new AdminUserCriteria();
        if (criteria.Validate() is string criteriaError) return Bad<AdminBulkPreviewDto>(criteriaError, "criteria");
        if (!Enum.IsDefined(request.Action)) return Bad<AdminBulkPreviewDto>("Choose an action.", "action");

        DateTime now = UtcNow;
        var options = new AdminBulkStoredOptions
        {
            Until = request.Options?.Until?.ToUniversalTime(),
            Reason = string.IsNullOrWhiteSpace(request.Options?.Reason) ? null : request.Options!.Reason!.Trim()
        };
        bool needsReason = request.Action is AdminBulkActionKind.Delete or AdminBulkActionKind.LockOut or AdminBulkActionKind.Ban;
        if (needsReason && options.Reason == null)
            return Bad<AdminBulkPreviewDto>($"A reason of 1 to {AdminUserManagementService.MaxReasonLength} characters is required.", AdminRefusalCodes.Reason);
        if (options.Reason != null && options.Reason.Length > AdminUserManagementService.MaxReasonLength)
            return Bad<AdminBulkPreviewDto>($"The reason can be at most {AdminUserManagementService.MaxReasonLength} characters.", AdminRefusalCodes.Reason);
        if (request.Action == AdminBulkActionKind.LockOut)
        {
            var nowOffset = _time.GetUtcNow();
            if (options.Until == null) return Bad<AdminBulkPreviewDto>("Choose when the lockout ends.", AdminRefusalCodes.Until);
            if (options.Until <= nowOffset) return Bad<AdminBulkPreviewDto>("The lockout must end in the future.", AdminRefusalCodes.Until);
            if (options.Until > nowOffset.AddDays(_options.LockoutMaxDays))
                return Bad<AdminBulkPreviewDto>($"A lockout can last at most {_options.LockoutMaxDays} days.", AdminRefusalCodes.Until);
        }
        else
        {
            options.Until = null;
        }

        if (request.Action == AdminBulkActionKind.Delete)
        {
            if (!NarrowsDeletion(criteria)) return Bad<AdminBulkPreviewDto>(DeleteNarrowingMessage, "narrowing");
            if (_review.MissingErasurePathsMessage() is string missing)
                return new(AdminResultStatus.Conflict, Message: missing, Code: AdminRefusalCodes.ErasurePaths);
        }

        var admins = _users.AdminNormalizedNames;
        var matched = AdminUserStatus.Apply(Users.AsNoTracking(), _db, criteria, admins, now);
        var users = await matched
            .OrderBy(u => u.CreatedUtc == null).ThenBy(u => u.CreatedUtc).ThenBy(u => u.Id)
            .ToListAsync(ct);

        async Task<HashSet<string>> IdsAsync(AdminUserCriteria narrower) =>
            (await AdminUserStatus.Apply(matched, _db, narrower, admins, now).Select(u => u.Id).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

        var holdIds = await IdsAsync(new AdminUserCriteria { Statuses = new() { AdminUserStatusKind.DeletionHold } });
        var pendingIds = await IdsAsync(new AdminUserCriteria { Statuses = new() { AdminUserStatusKind.DeletionPending } });
        // Active game sign-in blocks are few: every one is read, and the latest end kept per account.
        var signInBlockEnds = (await _db.GameSignInBlocks.AsNoTracking()
                .Where(b => b.ClearedUtc == null && b.BlockedUntilUtc > now)
                .Select(b => new { b.AspNetUserId, b.BlockedUntilUtc })
                .ToListAsync(ct))
            .GroupBy(b => b.AspNetUserId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(b => b.BlockedUntilUtc), StringComparer.Ordinal);
        bool delete = request.Action == AdminBulkActionKind.Delete;
        var gameIds = delete ? await IdsAsync(new AdminUserCriteria { HasGameData = "yes" }) : new HashSet<string>();
        var overseerIds = delete ? await IdsAsync(new AdminUserCriteria { HasOverseerData = "yes" }) : new HashSet<string>();

        var refused = new Dictionary<string, AdminBulkRefusedGroupDto>(StringComparer.Ordinal);
        var taken = new List<(ApplicationUser User, bool Advanced)>();
        int noOp = 0;
        foreach (var user in users)
        {
            var facts = new AccountFacts(holdIds.Contains(user.Id), pendingIds.Contains(user.Id), gameIds.Contains(user.Id), overseerIds.Contains(user.Id));
            var c = Classify(actor, request.Action, options, user, facts, now);
            if (c.RefusalCode != null)
            {
                if (!refused.TryGetValue(c.RefusalCode, out var group))
                {
                    group = new AdminBulkRefusedGroupDto { Code = c.RefusalCode, Message = c.RefusalMessage ?? c.RefusalCode };
                    refused[c.RefusalCode] = group;
                }
                group.Count++;
                if (group.ExampleUserNames.Count < MaxExampleNames) group.ExampleUserNames.Add(user.UserName ?? user.Id);
            }
            else if (c.NoOp != null)
            {
                noOp++;
            }
            else
            {
                taken.Add((user, c.Advanced));
            }
        }

        int max = Math.Max(1, _options.BulkMaxUsers);
        int truncated = Math.Max(0, taken.Count - max);
        var capped = taken.Take(max).ToList();
        int refusedCount = refused.Values.Sum(g => g.Count);
        int eligibleCount = capped.Count(t => !t.Advanced);
        int advancedCount = capped.Count(t => t.Advanced);

        var op = new AdminBulkOperation
        {
            ActorUserId = actor.UserId,
            ActorUserName = actor.UserName,
            Action = request.Action,
            OptionsJson = WriteOptions(options),
            CriteriaJson = JsonSerializer.Serialize(criteria, StoredJson),
            Status = AdminBulkOperationStatus.Previewed,
            CreatedUtc = now,
            ExpiresUtc = now.AddMinutes(_options.BulkPreviewMinutes),
            MatchedCount = users.Count,
            EligibleCount = eligibleCount,
            RefusedCount = refusedCount,
            RefusedSummaryJson = JsonSerializer.Serialize(new AdminBulkStoredSummary
            {
                Refused = refused.Values.ToList(),
                NoOpCount = noOp,
                TruncatedCount = truncated,
                AdvancedCount = advancedCount
            }, StoredJson),
            Items = capped.Select(t => new AdminBulkOperationItem
            {
                UserId = t.User.Id,
                UserName = t.User.UserName ?? t.User.Id,
                IsAdvanced = t.Advanced,
                Outcome = AdminBulkItemOutcome.Pending
            }).ToList()
        };
        _db.AdminBulkOperations.Add(op);
        await _db.SaveChangesAsync(ct);

        AdminBulkPreviewItemDto Item((ApplicationUser User, bool Advanced) t)
        {
            var set = AdminUserStatus.Evaluate(t.User, new AdminUserFacts(holdIds.Contains(t.User.Id), pendingIds.Contains(t.User.Id),
                signInBlockEnds.TryGetValue(t.User.Id, out var blockedUntil) ? blockedUntil : null), admins, now);
            return new AdminBulkPreviewItemDto
            {
                UserId = t.User.Id,
                UserName = t.User.UserName ?? string.Empty,
                Email = t.User.Email ?? string.Empty,
                Statuses = set.Statuses.ToList(),
                StatusText = set.Text,
                CreatedUtc = t.User.CreatedUtc.HasValue ? DateTime.SpecifyKind(t.User.CreatedUtc.Value, DateTimeKind.Utc) : null,
                CreatedUtcIsEstimate = t.User.CreatedUtcIsEstimate,
                LastSignInUtc = t.User.LastSignInUtc.HasValue ? DateTime.SpecifyKind(t.User.LastSignInUtc.Value, DateTimeKind.Utc) : null,
                IsAdvanced = t.Advanced
            };
        }

        var preview = new AdminBulkPreviewDto
        {
            OperationId = op.Id,
            Action = op.Action,
            ExpiresUtc = DateTime.SpecifyKind(op.ExpiresUtc, DateTimeKind.Utc),
            DangerLevel = DangerLevel(op.Action),
            RequiredAcknowledgments = RequiredAcknowledgments(op.Action).ToList(),
            MatchedCount = users.Count,
            EligibleCount = eligibleCount,
            AdvancedCount = advancedCount,
            RefusedCount = refusedCount,
            NoOpCount = noOp,
            TruncatedCount = truncated,
            MaxUsers = max,
            Eligible = capped.Where(t => !t.Advanced).Select(Item).ToList(),
            Advanced = capped.Where(t => t.Advanced).Select(Item).ToList(),
            Refused = refused.Values.ToList(),
            RequiredPhrase = delete ? RequiredPhrase(eligibleCount) : null
        };
        if (delete)
        {
            // Accounts with any of this data are refused, so every total is zero; they are shown so the admin sees it.
            preview.DataTotals = new List<AdminDeletionCategoryDto>
            {
                new() { Key = AdminUserManagementService.CategoryGameLogs, Label = "Game logs", Important = true },
                new() { Key = AdminUserManagementService.CategoryBones, Label = "Bones", Important = true },
                new() { Key = AdminUserManagementService.CategorySaveFiles, Label = "Save-file tracking", Important = true },
                new() { Key = AdminUserManagementService.CategoryChats, Label = "Overseer chats", Important = true },
                new() { Key = AdminUserManagementService.CategoryAiUsage, Label = "AI usage records" },
                new() { Key = AdminUserManagementService.CategoryBenchmarkRecords, Label = "Benchmark records" }
            };
        }
        return new(AdminResultStatus.Ok, preview);
    }

    private static AdminResult<T> Bad<T>(string message, string code) => new(AdminResultStatus.BadRequest, Message: message, Code: code);

    // --- Execute and cancel ---

    public async Task<AdminResult<AdminBulkOperationDto>> ExecuteAsync(AdminActor actor, long operationId, AdminBulkExecuteRequest request,
        CancellationToken ct = default)
    {
        var op = await _db.AdminBulkOperations.FirstOrDefaultAsync(o => o.Id == operationId, ct);
        if (op == null) return NotFound<AdminBulkOperationDto>();
        if (!string.Equals(op.ActorUserId, actor.UserId, StringComparison.Ordinal))
            return new(AdminResultStatus.Forbidden, Message: "Only the administrator who made the preview can run it.", Code: "actor");

        await ExpireIfDueAsync(op, ct);
        if (op.Status == AdminBulkOperationStatus.Expired)
            return new(AdminResultStatus.Conflict, Message: "The preview has expired. Preview again.", Code: "expired");
        if (op.Status != AdminBulkOperationStatus.Previewed)
            return new(AdminResultStatus.Conflict, Message: "This operation has already been run or canceled.", Code: "not-previewed");

        bool busy = await _db.AdminBulkOperations.AnyAsync(o => o.Id != op.Id
            && (o.Status == AdminBulkOperationStatus.Queued || o.Status == AdminBulkOperationStatus.Running
                || o.Status == AdminBulkOperationStatus.CancelRequested), ct);
        if (busy)
            return new(AdminResultStatus.Conflict, Message: "Another bulk operation is running. Wait for it to finish.", Code: "busy");

        var items = await _db.AdminBulkOperationItems.Where(i => i.OperationId == op.Id).ToListAsync(ct);
        var itemIds = items.Select(i => i.UserId).ToHashSet(StringComparer.Ordinal);
        var excluded = (request.ExcludedUserIds ?? new List<string>()).Where(id => !string.IsNullOrEmpty(id)).ToHashSet(StringComparer.Ordinal);
        if (!excluded.IsSubsetOf(itemIds))
            return Bad<AdminBulkOperationDto>("An excluded account is not in this preview.", "excluded");

        bool includeAdvanced = request.IncludeAdvanced && op.Action == AdminBulkActionKind.Delete;
        var acknowledged = (request.Acknowledgments ?? new List<string>())
            .Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var required = RequiredAcknowledgments(op.Action).ToList();
        if (includeAdvanced) required.Add(AdminBulkAcknowledgments.Advanced);
        if (!required.All(acknowledged.Contains))
            return Bad<AdminBulkOperationDto>("Check every acknowledgment before running.", AdminRefusalCodes.Acknowledgments);

        int count = items.Count(i => !excluded.Contains(i.UserId) && (includeAdvanced || !i.IsAdvanced));
        if (count == 0) return Bad<AdminBulkOperationDto>("No accounts are left to run.", "empty");
        if (op.Action == AdminBulkActionKind.Delete
            && !string.Equals((request.ConfirmationPhrase ?? string.Empty).Trim(), RequiredPhrase(count), StringComparison.Ordinal))
        {
            return Bad<AdminBulkOperationDto>($"Type {RequiredPhrase(count)} exactly to confirm.", "phrase");
        }

        foreach (var item in items)
        {
            item.Excluded = excluded.Contains(item.UserId) || (item.IsAdvanced && !includeAdvanced);
        }
        op.IncludedAdvanced = includeAdvanced;
        op.Status = AdminBulkOperationStatus.Queued;
        await _db.SaveChangesAsync(ct);
        _queue.Enqueue(op.Id);

        return await GetAsync(op.Id, null, 0, 50, ct);
    }

    /// <summary>A preview is discarded; a queued or running operation stops after the account it is on.</summary>
    public async Task<AdminResult<AdminBulkOperationDto>> CancelAsync(long operationId, CancellationToken ct = default)
    {
        var op = await _db.AdminBulkOperations.FirstOrDefaultAsync(o => o.Id == operationId, ct);
        if (op == null) return NotFound<AdminBulkOperationDto>();
        switch (op.Status)
        {
            case AdminBulkOperationStatus.Previewed:
                op.Status = AdminBulkOperationStatus.Canceled;
                op.CompletedUtc = UtcNow;
                break;
            case AdminBulkOperationStatus.Queued:
            case AdminBulkOperationStatus.Running:
                op.Status = AdminBulkOperationStatus.CancelRequested;
                break;
            case AdminBulkOperationStatus.CancelRequested:
                break;
            default:
                return new(AdminResultStatus.Conflict, Message: "The operation has already finished.", Code: "finished");
        }
        await _db.SaveChangesAsync(ct);
        return await GetAsync(op.Id, null, 0, 50, ct);
    }

    private async Task ExpireIfDueAsync(AdminBulkOperation op, CancellationToken ct)
    {
        if (op.Status == AdminBulkOperationStatus.Previewed && UtcNow >= op.ExpiresUtc)
        {
            op.Status = AdminBulkOperationStatus.Expired;
            await _db.SaveChangesAsync(ct);
        }
    }

    private static AdminResult<T> NotFound<T>() => new(AdminResultStatus.NotFound, Message: "Bulk operation not found.", Code: AdminRefusalCodes.NotFound);

    // --- Reading ---

    /// <param name="outcome">"pending", "changed", "skipped", "failed" or "excluded"; null: every item.</param>
    public async Task<AdminResult<AdminBulkOperationDto>> GetAsync(long operationId, string? outcome, int skip, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take <= 0 ? 50 : take, 1, 200);
        skip = Math.Max(0, skip);
        var op = await _db.AdminBulkOperations.FirstOrDefaultAsync(o => o.Id == operationId, ct);
        if (op == null) return NotFound<AdminBulkOperationDto>();
        await ExpireIfDueAsync(op, ct);

        var dto = ToDto(op);
        var all = _db.AdminBulkOperationItems.AsNoTracking().Where(i => i.OperationId == op.Id);
        dto.PendingCount = await all.CountAsync(i => !i.Excluded && i.Outcome == AdminBulkItemOutcome.Pending, ct);
        dto.ExcludedCount = await all.CountAsync(i => i.Excluded, ct);

        var filtered = all;
        if (string.Equals(outcome, "excluded", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(i => i.Excluded);
        }
        else if (Enum.TryParse<AdminBulkItemOutcome>(outcome, ignoreCase: true, out var parsed))
        {
            filtered = filtered.Where(i => !i.Excluded && i.Outcome == parsed);
        }

        dto.ItemsTotalCount = await filtered.CountAsync(ct);
        dto.Items = (await filtered.OrderBy(i => i.Id).Skip(skip).Take(take).ToListAsync(ct)).Select(ToItemDto).ToList();
        return new(AdminResultStatus.Ok, dto);
    }

    /// <summary>Operations newest first; previews that were never run are left out unless asked for.</summary>
    public async Task<AdminBulkHistoryPageDto> HistoryAsync(int skip, int take, bool includePreviews = false, CancellationToken ct = default)
    {
        take = Math.Clamp(take <= 0 ? 20 : take, 1, 100);
        skip = Math.Max(0, skip);
        var query = _db.AdminBulkOperations.AsNoTracking();
        if (!includePreviews)
        {
            query = query.Where(o => o.Status != AdminBulkOperationStatus.Previewed && o.Status != AdminBulkOperationStatus.Expired);
        }
        int total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(o => o.CreatedUtc).ThenByDescending(o => o.Id).Skip(skip).Take(take).ToListAsync(ct);
        return new AdminBulkHistoryPageDto { TotalCount = total, Rows = rows.Select(ToDto).ToList() };
    }

    /// <summary>The results report: one line per account, the excluded ones included.</summary>
    public async Task<AdminResult<string>> ReportCsvAsync(long operationId, CancellationToken ct = default)
    {
        var op = await _db.AdminBulkOperations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == operationId, ct);
        if (op == null) return NotFound<string>();
        var items = await _db.AdminBulkOperationItems.AsNoTracking().Where(i => i.OperationId == op.Id).OrderBy(i => i.Id).ToListAsync(ct);

        var csv = new StringBuilder();
        csv.Append("Operation,Action,UserName,UserId,Outcome,Excluded,Advanced,Before,After,Message,ProcessedUtc\r\n");
        foreach (var i in items)
        {
            csv.Append(string.Join(",", new[]
            {
                op.Id.ToString(CultureInfo.InvariantCulture),
                op.Action.ToString(),
                Csv(i.UserName),
                Csv(i.UserId),
                i.Excluded ? "Excluded" : i.Outcome.ToString(),
                i.Excluded ? "yes" : "no",
                i.IsAdvanced ? "yes" : "no",
                Csv(i.BeforeStatus),
                Csv(i.AfterStatus),
                Csv(i.Message),
                i.ProcessedUtc?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty
            }));
            csv.Append("\r\n");
        }
        return new(AdminResultStatus.Ok, csv.ToString());
    }

    /// <summary>Quotes a field and neutralizes a leading formula character.</summary>
    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if ("=+-@\t\r".IndexOf(value[0]) >= 0) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private AdminBulkOperationDto ToDto(AdminBulkOperation op)
    {
        var options = ReadOptions(op);
        var summary = ReadSummary(op);
        var status = op.Status == AdminBulkOperationStatus.Previewed && UtcNow >= op.ExpiresUtc ? AdminBulkOperationStatus.Expired : op.Status;
        return new AdminBulkOperationDto
        {
            Id = op.Id,
            Action = op.Action,
            Status = status,
            ActorUserName = op.ActorUserName,
            DangerLevel = DangerLevel(op.Action),
            Reason = options.Reason,
            Until = options.Until,
            StopMessage = options.StopMessage,
            CreatedUtc = DateTime.SpecifyKind(op.CreatedUtc, DateTimeKind.Utc),
            ExpiresUtc = DateTime.SpecifyKind(op.ExpiresUtc, DateTimeKind.Utc),
            StartedUtc = op.StartedUtc.HasValue ? DateTime.SpecifyKind(op.StartedUtc.Value, DateTimeKind.Utc) : null,
            CompletedUtc = op.CompletedUtc.HasValue ? DateTime.SpecifyKind(op.CompletedUtc.Value, DateTimeKind.Utc) : null,
            MatchedCount = op.MatchedCount,
            EligibleCount = op.EligibleCount,
            RefusedCount = op.RefusedCount,
            ChangedCount = op.ChangedCount,
            SkippedCount = op.SkippedCount,
            FailedCount = op.FailedCount,
            IncludedAdvanced = op.IncludedAdvanced,
            Refused = summary.Refused,
            NoOpCount = summary.NoOpCount,
            TruncatedCount = summary.TruncatedCount
        };
    }

    private static AdminBulkItemDto ToItemDto(AdminBulkOperationItem i) => new()
    {
        Id = i.Id,
        UserId = i.UserId,
        UserName = i.UserName,
        IsAdvanced = i.IsAdvanced,
        Excluded = i.Excluded,
        Outcome = i.Outcome,
        Message = i.Message,
        BeforeStatus = i.BeforeStatus,
        AfterStatus = i.AfterStatus,
        ProcessedUtc = i.ProcessedUtc.HasValue ? DateTime.SpecifyKind(i.ProcessedUtc.Value, DateTimeKind.Utc) : null
    };

    // --- One account of a run ---

    /// <summary>
    /// Re-checks the account against the operation's criteria and the hard protections as it is now, then applies
    /// the action through <see cref="AdminUserManagementService"/>. An account that changed since the preview is
    /// skipped, never acted on.
    /// </summary>
    internal async Task<AdminBulkItemResult> ProcessItemAsync(AdminBulkOperation op, AdminBulkStoredOptions options, AdminUserCriteria criteria,
        string userId, CancellationToken ct = default)
    {
        var actor = new AdminActor(op.ActorUserId, op.ActorUserName);
        DateTime now = UtcNow;
        var admins = _users.AdminNormalizedNames;

        var user = await Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return new(AdminBulkItemOutcome.Skipped, ChangedSincePreview + "the account no longer exists.");

        var single = Users.AsNoTracking().Where(u => u.Id == userId);
        if (!await AdminUserStatus.Apply(single, _db, criteria, admins, now).AnyAsync(ct))
            return new(AdminBulkItemOutcome.Skipped, ChangedSincePreview + "the account no longer matches the criteria.");

        Task<bool> HasAsync(AdminUserCriteria narrower) => AdminUserStatus.Apply(single, _db, narrower, admins, now).AnyAsync(ct);
        bool delete = op.Action == AdminBulkActionKind.Delete;
        var facts = new AccountFacts(
            await HasAsync(new AdminUserCriteria { Statuses = new() { AdminUserStatusKind.DeletionHold } }),
            await HasAsync(new AdminUserCriteria { Statuses = new() { AdminUserStatusKind.DeletionPending } }),
            delete && await HasAsync(new AdminUserCriteria { HasGameData = "yes" }),
            delete && await HasAsync(new AdminUserCriteria { HasOverseerData = "yes" }));

        var c = Classify(actor, op.Action, options, user, facts, now);
        if (c.RefusalCode != null) return new(AdminBulkItemOutcome.Skipped, ChangedSincePreview + LowerFirst(c.RefusalMessage) + ".");
        if (c.Advanced && !op.IncludedAdvanced)
            return new(AdminBulkItemOutcome.Skipped, ChangedSincePreview + "the email address is now confirmed.");
        if (c.NoOp != null) return new(AdminBulkItemOutcome.Skipped, AlreadyDone + c.NoOp + ".");

        var result = op.Action switch
        {
            AdminBulkActionKind.Delete => await _users.DeleteForBulkAsync(actor, userId, options.Reason, op.Id, ct),
            AdminBulkActionKind.LockOut => await _users.LockOutAsync(actor, userId, options.Until, options.Reason, op.Id, ct),
            AdminBulkActionKind.Unlock => await _users.UnlockAsync(actor, userId, op.Id, ct),
            AdminBulkActionKind.Ban => await _users.BanAsync(actor, userId, options.Reason, op.Id, ct),
            AdminBulkActionKind.Unban => await _users.UnbanAsync(actor, userId, op.Id, ct),
            AdminBulkActionKind.SignOutEverywhere => await _users.SignOutEverywhereAsync(actor, userId, op.Id, ct),
            _ => new AdminResult<AdminUserActionResultDto>(AdminResultStatus.BadRequest, Message: "Unknown action.")
        };

        return result.Status switch
        {
            AdminResultStatus.Ok => new(AdminBulkItemOutcome.Changed, result.Value?.Summary, result.Value?.Before.Text, result.Value?.After.Text),
            AdminResultStatus.NotFound => new(AdminBulkItemOutcome.Skipped, ChangedSincePreview + "the account no longer exists."),
            AdminResultStatus.Conflict when result.Code == AdminRefusalCodes.AlreadyDone => new(AdminBulkItemOutcome.Skipped, AlreadyDone + result.Message),
            AdminResultStatus.Conflict => new(AdminBulkItemOutcome.Skipped, ChangedSincePreview + result.Message),
            _ => new(AdminBulkItemOutcome.Failed, result.Message)
        };
    }

    private static string LowerFirst(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : char.ToLowerInvariant(value[0]) + value[1..];
}
