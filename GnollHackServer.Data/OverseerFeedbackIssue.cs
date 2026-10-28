namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>Triage state of an <see cref="OverseerFeedbackIssue"/>. Stored as its name.</summary>
public enum OverseerFeedbackStatus
{
    /// <summary>Not yet looked at. Set by the system.</summary>
    New,

    /// <summary>Seen; being investigated or waiting on a fix. Set by an administrator.</summary>
    Acknowledged,

    /// <summary>Fixed. A new occurrence turns it into <see cref="Regressed"/>.</summary>
    Resolved,

    /// <summary>Was resolved and came back. Set by the system, never by an administrator.</summary>
    Regressed,

    /// <summary>Will not fix, or noise. Stays dismissed when it recurs.</summary>
    Dismissed,
}

/// <summary>
/// One problem the Overseer chat model reported through <c>report_feedback</c>, grouping every
/// <see cref="OverseerFeedbackReport"/> with the same <see cref="Fingerprint"/>.
///
/// <para>Holds no user, conversation or message reference, so it outlives its occurrences:
/// <see cref="OccurrenceCount"/> is historical and does not drop when a purge or an account
/// erasure removes occurrence rows. An administrator's delete removes it with its occurrences.</para>
/// </summary>
public class OverseerFeedbackIssue
{
    public long Id { get; set; }

    /// <summary>SHA-256 of the category and normalized subject, 64 lowercase hex characters.</summary>
    [Required]
    [MaxLength(64)]
    public string Fingerprint { get; set; } = "";

    /// <summary>Wire value: <c>missing_information</c>, <c>tool_problem</c> or <c>own_mistake</c>.</summary>
    [Required]
    [MaxLength(32)]
    public string Category { get; set; } = "";

    /// <summary>From the first occurrence.</summary>
    [Required]
    [MaxLength(120)]
    public string Subject { get; set; } = "";

    /// <summary>From the first occurrence.</summary>
    [Required]
    [MaxLength(300)]
    public string Summary { get; set; } = "";

    public DateTime FirstSeenUtc { get; set; }

    public DateTime LastSeenUtc { get; set; }

    /// <summary>Every occurrence ever recorded; survives occurrence deletion.</summary>
    public int OccurrenceCount { get; set; }

    /// <summary>Times a <see cref="OverseerFeedbackStatus.Resolved"/> issue recurred.</summary>
    public int RegressionCount { get; set; }

    public DateTime? LastRegressionUtc { get; set; }

    public OverseerFeedbackStatus Status { get; set; } = OverseerFeedbackStatus.New;

    [MaxLength(1000)]
    public string? TriageNote { get; set; }

    /// <summary>What was done about the issue.</summary>
    [MaxLength(4000)]
    public string? Resolution { get; set; }

    [MaxLength(64)]
    public string? FixedInVersion { get; set; }

    /// <summary>The channels the fix went through: comma-separated ids, in <c>FeedbackResolvedVia</c> order.</summary>
    [MaxLength(200)]
    public string? ResolvedVia { get; set; }

    [MaxLength(450)]
    public string? TriagedByUserId { get; set; }

    public DateTime? TriagedUtc { get; set; }
}
