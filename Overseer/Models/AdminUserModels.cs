namespace Overseer.Models;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MobileGnollHackLogger.Data;
using Overseer.Services.Security;

/// <summary>One account as the admin Users tab shows it.</summary>
public class AdminUserDto
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EmailConfirmed { get; set; }
    public List<AdminGroupDto> Groups { get; set; } = new List<AdminGroupDto>();

    /// <summary>The status badges, in display order; serialized by name.</summary>
    public List<AdminUserStatusKind> Statuses { get; set; } = new();
    public string StatusText { get; set; } = string.Empty;

    public bool IsAdministrator { get; set; }

    /// <summary>The account of the administrator viewing the list.</summary>
    public bool IsSelf { get; set; }

    /// <summary>When the account was registered; with <see cref="CreatedUtcIsEstimate"/> an upper bound ("on or before").</summary>
    public DateTime? CreatedUtc { get; set; }
    public bool CreatedUtcIsEstimate { get; set; }
    public DateTime? LastSignInUtc { get; set; }

    public DateTimeOffset? LockoutEnd { get; set; }

    /// <summary>True while the lockout is the one an administrator set; false for an automatic lockout.</summary>
    public bool LockoutIsAdminSet { get; set; }
    public string? AdminLockoutReason { get; set; }
    public int AccessFailedCount { get; set; }
    public bool TwoFactorEnabled { get; set; }

    public bool IsBanned { get; set; }
    public DateTime? BannedUtc { get; set; }
    public string? BanReason { get; set; }
    public bool IsGameLogBanned { get; set; }
    public bool IsBonesBanned { get; set; }

    public bool IsOverseerBlocked { get; set; }
    public DateTime? OverseerBlockedUtc { get; set; }
    public string? OverseerBlockReason { get; set; }

    public bool HasAccountHold { get; set; }
    public string? AccountHoldReason { get; set; }
    public DateTime? AccountHoldPlacedUtc { get; set; }
    public string? AccountHoldPlacedByUserName { get; set; }
    public long? PendingDeletionRequestId { get; set; }

    public int GameLogCount { get; set; }
    public int BonesCount { get; set; }
    public int ChatSessionCount { get; set; }

    /// <summary>The newest accepted version of each Terms document the user has accepted; empty when none.</summary>
    public List<AdminTermsAcceptanceDto> TermsAcceptances { get; set; } = new();
}

/// <summary>A user's acceptance of one Terms document.</summary>
public class AdminTermsAcceptanceDto
{
    /// <summary>"overseer-terms" or "account-terms".</summary>
    public string Document { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTime AcceptedUtc { get; set; }

    /// <summary>"Web", "GnollHackApp" or "AccountSite".</summary>
    public string Source { get; set; } = string.Empty;
}

public class AdminUsersPageDto
{
    public int TotalCount { get; set; }
    public List<AdminUserDto> Rows { get; set; } = new();

    /// <summary>The server's clock when the page was read, for "in 3 days" and "2 hours ago".</summary>
    public DateTime NowUtc { get; set; }
}

/// <summary>The query of the user list: the criteria plus sort and page.</summary>
public class AdminUserListQuery : AdminUserCriteria
{
    /// <summary>newest, oldest, name, email, signin-recent or signin-oldest.</summary>
    public string? Sort { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public class AdminFacetCountDto
{
    /// <summary>The option's query value: a status name, "yes"/"no", or "withinDays:N"/"olderThanDays:N"/"never".</summary>
    public string Key { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>A count per filter option, under the search only.</summary>
public class AdminUserFacetsDto
{
    public int Total { get; set; }
    public List<AdminFacetCountDto> Statuses { get; set; } = new();
    public List<AdminFacetCountDto> EmailConfirmed { get; set; } = new();
    public List<AdminFacetCountDto> Registered { get; set; } = new();
    public List<AdminFacetCountDto> LastSignIn { get; set; } = new();
    public List<AdminFacetCountDto> HasGameData { get; set; } = new();
    public List<AdminFacetCountDto> HasOverseerData { get; set; } = new();
}

public class AdminUserStatusSetDto
{
    public List<AdminUserStatusKind> Statuses { get; set; } = new();
    public string Text { get; set; } = string.Empty;

    public static AdminUserStatusSetDto From(AdminUserStatusSet set) => new()
    {
        Statuses = new List<AdminUserStatusKind>(set.Statuses),
        Text = set.Text
    };
}

/// <summary>What a per-user action did: the status before and after, and the refreshed account (null once deleted).</summary>
public class AdminUserActionResultDto
{
    public AdminUserDto? User { get; set; }
    public AdminUserStatusSetDto Before { get; set; } = new();
    public AdminUserStatusSetDto After { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public bool Deleted { get; set; }
}

public class AdminLockoutRequest
{
    public DateTimeOffset? Until { get; set; }
    public string? Reason { get; set; }
}

public class AdminGameRestrictionsRequest
{
    public bool GameLogBanned { get; set; }
    public bool BonesBanned { get; set; }
    public string? Reason { get; set; }
}

/// <summary>An optional admin-only reason.</summary>
public class AdminOptionalReasonRequest
{
    public string? Reason { get; set; }
}

public class AdminDeleteUserRequest
{
    /// <summary>The account's user name, typed exactly.</summary>
    public string? ConfirmUserName { get; set; }
    public string? Reason { get; set; }

    /// <summary>The keys of exactly the important categories with a count above zero.</summary>
    public List<string>? AcknowledgedCategories { get; set; }
}

public class AdminRefusalDto
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class AdminDeletionCategoryDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }

    /// <summary>Must be acknowledged before deletion when the count is above zero.</summary>
    public bool Important { get; set; }

    /// <summary>What deletion does to these rows.</summary>
    public string? Note { get; set; }
}

/// <summary>What deleting the account would erase, and why it is refused, if it is.</summary>
public class AccountDeletionImpactDto
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public List<AdminRefusalDto> Refusals { get; set; } = new();
    public List<AdminDeletionCategoryDto> Categories { get; set; } = new();
}

// --- Admin log ---

public class AdminUserActionDto
{
    public long Id { get; set; }
    public DateTime OccurredUtc { get; set; }
    public string ActorUserId { get; set; } = string.Empty;
    public string ActorUserName { get; set; } = string.Empty;
    public string SubjectUserId { get; set; } = string.Empty;
    public string SubjectUserName { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AdminUserActionKind Kind { get; set; }
    public string? Reason { get; set; }
    public string? Detail { get; set; }
    public long? BulkOperationId { get; set; }
}

public class AdminUserActionLogPageDto
{
    public int TotalCount { get; set; }
    public List<AdminUserActionDto> Rows { get; set; } = new();
}

// --- Bulk operations ---

/// <summary>The action's options: the lockout's end and the reason.</summary>
public class AdminBulkOptionsDto
{
    public DateTimeOffset? Until { get; set; }
    public string? Reason { get; set; }
}

public class AdminBulkPreviewRequest
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AdminBulkActionKind Action { get; set; }
    public AdminBulkOptionsDto? Options { get; set; }
    public AdminUserCriteria? Criteria { get; set; }
}

public class AdminBulkPreviewItemDto
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<AdminUserStatusKind> Statuses { get; set; } = new();
    public string StatusText { get; set; } = string.Empty;
    public DateTime? CreatedUtc { get; set; }
    public bool CreatedUtcIsEstimate { get; set; }
    public DateTime? LastSignInUtc { get; set; }
    public bool IsAdvanced { get; set; }
}

/// <summary>Accounts the operation refuses for one reason: the count and up to 50 example user names.</summary>
public class AdminBulkRefusedGroupDto
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int Count { get; set; }
    public List<string> ExampleUserNames { get; set; } = new();
}

public class AdminBulkPreviewDto
{
    public long OperationId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AdminBulkActionKind Action { get; set; }
    public DateTime ExpiresUtc { get; set; }

    /// <summary>safe, caution or danger.</summary>
    public string DangerLevel { get; set; } = "safe";

    /// <summary>The acknowledgment keys the execute request must carry (plus "advanced" when it includes the advanced accounts).</summary>
    public List<string> RequiredAcknowledgments { get; set; } = new();

    public int MatchedCount { get; set; }
    public int EligibleCount { get; set; }
    public int AdvancedCount { get; set; }
    public int RefusedCount { get; set; }
    public int NoOpCount { get; set; }

    /// <summary>Eligible and advanced accounts beyond the cap, which this operation does not take.</summary>
    public int TruncatedCount { get; set; }
    public int MaxUsers { get; set; }

    public List<AdminBulkPreviewItemDto> Eligible { get; set; } = new();
    public List<AdminBulkPreviewItemDto> Advanced { get; set; } = new();
    public List<AdminBulkRefusedGroupDto> Refused { get; set; } = new();

    /// <summary>Delete only: the data the eligible accounts hold (zero by construction).</summary>
    public List<AdminDeletionCategoryDto>? DataTotals { get; set; }

    /// <summary>Delete only: the phrase for the eligible count with nothing excluded; the client recomputes it from its final count.</summary>
    public string? RequiredPhrase { get; set; }
}

public class AdminBulkExecuteRequest
{
    public List<string>? ExcludedUserIds { get; set; }
    public bool IncludeAdvanced { get; set; }
    public List<string>? Acknowledgments { get; set; }
    public string? ConfirmationPhrase { get; set; }
}

public class AdminBulkItemDto
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool IsAdvanced { get; set; }
    public bool Excluded { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AdminBulkItemOutcome Outcome { get; set; }
    public string? Message { get; set; }
    public string? BeforeStatus { get; set; }
    public string? AfterStatus { get; set; }
    public DateTime? ProcessedUtc { get; set; }
}

public class AdminBulkOperationDto
{
    public long Id { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AdminBulkActionKind Action { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AdminBulkOperationStatus Status { get; set; }
    public string ActorUserName { get; set; } = string.Empty;
    public string DangerLevel { get; set; } = "safe";
    public string? Reason { get; set; }
    public DateTimeOffset? Until { get; set; }

    /// <summary>Why the run stopped early, when it did.</summary>
    public string? StopMessage { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }

    public int MatchedCount { get; set; }
    public int EligibleCount { get; set; }
    public int RefusedCount { get; set; }
    public int ChangedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }

    /// <summary>Included accounts not yet processed.</summary>
    public int PendingCount { get; set; }
    public int ExcludedCount { get; set; }
    public bool IncludedAdvanced { get; set; }

    public List<AdminBulkRefusedGroupDto> Refused { get; set; } = new();
    public int NoOpCount { get; set; }
    public int TruncatedCount { get; set; }

    /// <summary>The page of items asked for; empty in the history list.</summary>
    public int ItemsTotalCount { get; set; }
    public List<AdminBulkItemDto> Items { get; set; } = new();
}

public class AdminBulkHistoryPageDto
{
    public int TotalCount { get; set; }
    public List<AdminBulkOperationDto> Rows { get; set; } = new();
}
