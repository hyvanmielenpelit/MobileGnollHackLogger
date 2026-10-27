namespace Overseer.Models;

using System;
using System.Collections.Generic;

/// <summary>Body of an admin action that requires a written reason (block, deletion hold).</summary>
public class AdminReasonRequest
{
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Counts of one security event kind in a date range.</summary>
public class SecurityKindCountDto
{
    /// <summary>The <c>SecurityEventKind</c> name.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Sum of the runs' counts.</summary>
    public long Count { get; set; }

    public int DistinctUsers { get; set; }
    public int DistinctIps { get; set; }
}

public class SecuritySummaryDto
{
    public List<SecurityKindCountDto> Kinds { get; set; } = new();
    public int PendingDeletionRequests { get; set; }
    public int ActiveAccountHolds { get; set; }
}

public class SecurityEventDto
{
    public long Id { get; set; }
    public DateTime OccurredUtc { get; set; }
    public DateTime LastOccurredUtc { get; set; }

    /// <summary>The <c>SecurityEventKind</c> name.</summary>
    public string Kind { get; set; } = string.Empty;

    public int Count { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public bool UserIsOverseerBlocked { get; set; }
    public string? IpAddress { get; set; }
    public long? SystemAiApiConfigurationId { get; set; }
    public string? Provider { get; set; }
    public string? ModelId { get; set; }
    public string? Code { get; set; }
    public string? Detail { get; set; }

    /// <summary>Game texts the snapshot guard kept for this event.</summary>
    public int QuarantineCount { get; set; }
}

/// <summary>One security event with its kept game texts, its related records and what its kind means.</summary>
public class SecurityEventDetailDto : SecurityEventDto
{
    /// <summary>The newest kept texts, at most 50, decrypted.</summary>
    public List<SecurityQuarantineItemDto> Quarantine { get; set; } = new();

    /// <summary>Every kept text of the event, of which <see cref="Quarantine"/> holds the newest.</summary>
    public int QuarantineTotalCount { get; set; }

    public SecurityRelatedDto Related { get; set; } = new();

    /// <summary>What the kind means, who writes it, and what its code and detail are.</summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>The display name of <see cref="SecurityEventDto.SystemAiApiConfigurationId"/>; null when it is gone.</summary>
    public string? SystemConfigurationName { get; set; }
}

/// <summary>One game text the snapshot guard kept.</summary>
public class SecurityQuarantineItemDto
{
    public long Id { get; set; }
    public DateTime OccurredUtc { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string TextKind { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string[] Codes { get; set; } = Array.Empty<string>();
    public bool PlayerAuthored { get; set; }
    public bool Withheld { get; set; }

    /// <summary>normal, confidential or incognito.</summary>
    public string SessionMode { get; set; } = string.Empty;

    public string? SessionRef { get; set; }
    public int PlainLength { get; set; }
    public int StoredLength { get; set; }
    public bool Truncated { get; set; }

    /// <summary>Lower-case hex SHA-256 of the full text's UTF-8.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>The kept text, or the unreadable notice when it cannot be decrypted.</summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Records around one security event, each list the newest 20 and filled only for the kinds it applies to. Holds
/// identifiers and metadata, never message content or session titles.
/// </summary>
public class SecurityRelatedDto
{
    public List<SecurityTermsAcceptanceDto> TermsAcceptances { get; set; } = new();
    public List<SecurityAdminActionDto> AdminActions { get; set; } = new();
    public SecurityHoldDto? Hold { get; set; }
    public SecurityDeletionRequestDto? DeletionRequest { get; set; }
    public List<SecuritySessionDto> Sessions { get; set; } = new();
    public SecurityReportedMessageDto? ReportedMessage { get; set; }
    public List<SecurityProviderAttemptDto> ProviderAttempts { get; set; } = new();
    public List<SecurityUsageRowDto> UsageRows { get; set; } = new();
    public List<SecurityIpEventDto> IpEvents { get; set; } = new();
    public List<SecurityRequestLogDto> RequestLogs { get; set; } = new();
    public List<SecuritySameUserEventDto> SameUserEvents { get; set; } = new();
}

public class SecurityTermsAcceptanceDto
{
    public string DocumentKey { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTime AcceptedUtc { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class SecurityAdminActionDto
{
    public long Id { get; set; }
    public DateTime OccurredUtc { get; set; }

    /// <summary>The <c>AdminUserActionKind</c> name.</summary>
    public string Kind { get; set; } = string.Empty;

    public string ActorUserName { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? Detail { get; set; }
    public long? BulkOperationId { get; set; }
}

public class SecurityHoldDto
{
    public long Id { get; set; }
    public string Reason { get; set; } = string.Empty;

    /// <summary>The placing administrator's user name, or their id when the account is gone.</summary>
    public string PlacedByUserName { get; set; } = string.Empty;

    public DateTime PlacedUtc { get; set; }
}

public class SecurityDeletionRequestDto
{
    public long Id { get; set; }

    /// <summary>"Pending", "Completed" or "Declined".</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime RequestedUtc { get; set; }
    public string HoldReasons { get; set; } = string.Empty;
}

public class SecuritySessionDto
{
    public long Id { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? SnapshotRefusedUtc { get; set; }
    public string? SnapshotRefusalCode { get; set; }
    public bool IsConfidential { get; set; }
}

/// <summary>The message a <c>ChatReported</c> event names; never its content.</summary>
public class SecurityReportedMessageDto
{
    public long MessageId { get; set; }

    /// <summary>Null when the message no longer exists.</summary>
    public long? SessionId { get; set; }

    public DateTime? TimestampUtc { get; set; }
    public string? Role { get; set; }

    /// <summary>Whether the message row still exists.</summary>
    public bool Exists { get; set; }
}

public class SecurityProviderAttemptDto
{
    public DateTime StartedAtUtc { get; set; }
    public int RoleContext { get; set; }
    public string RequestedModelId { get; set; } = string.Empty;
    public int? HttpStatus { get; set; }
    public string? ProviderErrorCode { get; set; }

    /// <summary>The <c>ProviderAttemptOutcome</c> name.</summary>
    public string Outcome { get; set; } = string.Empty;

    public bool Retried { get; set; }
}

public class SecurityUsageRowDto
{
    public DateTime TimestampUtc { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public int RoleContext { get; set; }

    /// <summary>Input plus output tokens.</summary>
    public int Tokens { get; set; }

    /// <summary>Estimated cost in USD; null when unpriced.</summary>
    public decimal? Cost { get; set; }
}

public class SecurityIpEventDto
{
    public long Id { get; set; }

    /// <summary>The <c>SecurityEventKind</c> name.</summary>
    public string Kind { get; set; } = string.Empty;

    public string? Code { get; set; }
    public int Count { get; set; }
}

/// <summary>A website request log row; never its message or request data.</summary>
public class SecurityRequestLogDto
{
    public DateTime LastDate { get; set; }
    public long Count { get; set; }
    public string? RequestPath { get; set; }
    public string? RequestMethod { get; set; }
    public int? ResponseCode { get; set; }
    public string? RequestUserName { get; set; }
}

public class SecuritySameUserEventDto
{
    public long Id { get; set; }

    /// <summary>The <c>SecurityEventKind</c> name.</summary>
    public string Kind { get; set; } = string.Empty;

    public string? Code { get; set; }
    public string? Detail { get; set; }
}

public class SecurityEventsPageDto
{
    public int TotalCount { get; set; }
    public List<SecurityEventDto> Rows { get; set; } = new();
}

public class SafetyIdentifierLookupDto
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
}

/// <summary>The snapshot guard's mode and the configured block rule, as the Security tab's section shows them.</summary>
public class AdminSnapshotGuardDto
{
    /// <summary><c>Off</c>, <c>Observe</c> or <c>Enforce</c>.</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>When an administrator last set the mode; null while it is seeded from the configuration.</summary>
    public DateTime? SetAtUtc { get; set; }

    public string? SetByUserName { get; set; }

    /// <summary>True while no setting exists and <c>SnapshotGuard:Mode</c> decides.</summary>
    public bool Seeded { get; set; }

    public bool AutoBlockEnabled { get; set; }
    public int AutoBlockDistinctDays { get; set; }
    public int AutoBlockWindowDays { get; set; }
    public bool NotificationEmailConfigured { get; set; }

    /// <summary>Whether a refused game text is kept, encrypted, for an administrator's review.</summary>
    public bool QuarantineEnabled { get; set; }

    public int QuarantineMaxChars { get; set; }

    /// <summary><c>ChatRetentionSettings:PruneSnapshotQuarantineDays</c>; zero or negative keeps the texts with their event.</summary>
    public int QuarantineRetentionDays { get; set; }
}

public class AdminSnapshotGuardUpdateDto
{
    public string? Mode { get; set; }
}

public class AccountDeletionRequestDto
{
    public long Id { get; set; }

    /// <summary>Null once the request is completed.</summary>
    public string? UserId { get; set; }

    /// <summary>Null once the request is completed (shown as <i>Deleted account</i>).</summary>
    public string? UserName { get; set; }

    public DateTime RequestedUtc { get; set; }
    public int AgeDays { get; set; }

    /// <summary>Pending and at least <c>AccountHold:ReviewHighlightDays</c> old.</summary>
    public bool IsOverdue { get; set; }

    /// <summary>"Pending", "Completed" or "Declined".</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>The reasons stored when the request was held.</summary>
    public string HoldReasons { get; set; } = string.Empty;

    /// <summary>The reasons as evaluated now; null when the request is decided or the account is gone.</summary>
    public string? CurrentReasons { get; set; }

    public bool ManualHold { get; set; }
    public bool AutomaticRule { get; set; }

    public string? DecidedByUserName { get; set; }
    public DateTime? DecidedUtc { get; set; }
    public string? DecisionNote { get; set; }
}

public class AccountDeletionRequestsPageDto
{
    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int OverdueCount { get; set; }

    /// <summary>False while DumpLogPath or ConversationsDataLocation is missing; Approve is refused then.</summary>
    public bool ErasurePathsConfigured { get; set; }

    /// <summary>Names the missing settings when <see cref="ErasurePathsConfigured"/> is false.</summary>
    public string? ErasurePathsMessage { get; set; }

    public List<AccountDeletionRequestDto> Rows { get; set; } = new();
}

public class ApproveAccountDeletionRequest
{
    public string ConfirmUserName { get; set; } = string.Empty;
}

public class DeclineAccountDeletionRequest
{
    public string Note { get; set; } = string.Empty;
}

public class AccountDeletionDecisionResultDto
{
    public bool EmailSent { get; set; }

    /// <summary>Why the decision email was not sent, when it was not.</summary>
    public string? EmailError { get; set; }

    /// <summary>Approve only: bones files whose rows were deleted but which were not found on disk.</summary>
    public int BonesFilesMissing { get; set; }

    /// <summary>Approve only: dumplog and attachment folders that were not found on disk.</summary>
    public int FoldersMissing { get; set; }
}
