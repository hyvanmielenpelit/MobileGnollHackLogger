namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// Stored triage state of a GnollBench feedback issue. Stored as its name. Regressed is never stored:
/// it is derived when a self-report is newer than <see cref="BenchmarkFeedbackTriage.ResolvedUtc"/>.
/// </summary>
public enum BenchmarkFeedbackTriageStatus
{
    New,
    Acknowledged,
    Resolved,
    Dismissed,
}

/// <summary>
/// Triage of a GnollBench feedback issue: every accepted <see cref="BenchmarkRunAnswerSelfReport"/>
/// with the same <see cref="Fingerprint"/>.
///
/// <para>Keyed by fingerprint, with no foreign key, because a re-run deletes and recreates an answer's
/// self-reports. A fingerprint with no row reads as New.</para>
/// </summary>
public class BenchmarkFeedbackTriage
{
    public long Id { get; set; }

    /// <summary>64 lowercase hex characters, as on <see cref="BenchmarkRunAnswerSelfReport.Fingerprint"/>.</summary>
    [Required]
    [MaxLength(64)]
    public string Fingerprint { get; set; } = "";

    public BenchmarkFeedbackTriageStatus Status { get; set; } = BenchmarkFeedbackTriageStatus.New;

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

    /// <summary>Plain column: the triage record outlives the account.</summary>
    [MaxLength(450)]
    public string? TriagedByUserId { get; set; }

    public DateTime? TriagedUtc { get; set; }

    /// <summary>Set when the status becomes Resolved, cleared otherwise.</summary>
    public DateTime? ResolvedUtc { get; set; }
}
