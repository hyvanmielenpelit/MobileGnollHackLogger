namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// One occurrence of an <see cref="OverseerFeedbackIssue"/>: a single <c>report_feedback</c> call
/// from a persistent, non-confidential chat, with its free-text fields scrubbed before storage.
///
/// <para>Removed with its issue (cascade), with its user (cascade), and with its conversation by the
/// permanent purge, which deletes it explicitly because <see cref="ChatSessionId"/> has no
/// relationship.</para>
/// </summary>
public class OverseerFeedbackReport
{
    public long Id { get; set; }

    public long IssueId { get; set; }

    /// <summary>Copy of the issue's fingerprint, for the one-occurrence-per-conversation index.</summary>
    [Required]
    [MaxLength(64)]
    public string Fingerprint { get; set; } = "";

    public DateTime CreatedUtc { get; set; }

    [Required]
    [MaxLength(32)]
    public string Category { get; set; } = "";

    /// <summary>As the model wrote it, after scrubbing.</summary>
    [Required]
    [MaxLength(120)]
    public string Subject { get; set; } = "";

    [Required]
    [MaxLength(300)]
    public string Summary { get; set; } = "";

    [MaxLength(2000)]
    public string? Details { get; set; }

    [MaxLength(500)]
    public string? SearchedFor { get; set; }

    [MaxLength(300)]
    public string? ExpectedLocation { get; set; }

    /// <summary>JSON array of validated tool names.</summary>
    [MaxLength(1000)]
    public string? RelatedToolsJson { get; set; }

    [MaxLength(32)]
    public string? NoticedVia { get; set; }

    /// <summary>The model's hypothesis, not a finding.</summary>
    [MaxLength(32)]
    public string? SuspectedCause { get; set; }

    [MaxLength(500)]
    public string? SuspectedCauseNote { get; set; }

    /// <summary>A field was cut to its maximum length.</summary>
    public bool Truncated { get; set; }

    /// <summary>The scrub replaced something.</summary>
    public bool Redacted { get; set; }

    /// <summary>Plain column, like <see cref="ChatAccessAuditLog.ChatSessionId"/>.</summary>
    public long ChatSessionId { get; set; }

    public long? TurnUserMessageId { get; set; }

    [MaxLength(128)]
    public string? ToolCallId { get; set; }

    [MaxLength(128)]
    public string? AgentName { get; set; }

    [MaxLength(64)]
    public string? Provider { get; set; }

    [MaxLength(256)]
    public string? ModelId { get; set; }

    [MaxLength(64)]
    public string? AppVersion { get; set; }

    [MaxLength(450)]
    public string? AspNetUserId { get; set; }
}
