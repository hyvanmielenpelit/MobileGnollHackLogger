namespace MobileGnollHackLogger.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// Why a series stopped. Set whenever <see cref="BenchmarkRunSeries.Status"/> is
/// <see cref="BenchmarkRunSeriesStatus.Stopped"/>, and never otherwise: a stopped series with no
/// reason is unactionable in a dialog, which is the whole point of storing it.
/// </summary>
public enum BenchmarkRunSeriesStopReason
{
    /// <summary>A member run reached a terminal failure. Completed members are kept.</summary>
    MemberFailed = 1,

    /// <summary>The rolling run cap blocked the next member and the caller did not allow waiting.</summary>
    RunCapReached = 2,

    /// <summary>The compliance guard denied the spend for a reason other than the run cap.</summary>
    SpendDenied = 3
}

public enum BenchmarkRunSeriesStatus
{
    Pending = 1,
    Running = 2,

    /// <summary>Paused on the rolling cap, retrying with a bounded back-off. Not terminal.</summary>
    WaitingForCap = 3,

    /// <summary>
    /// Halted with completed members intact and resumable through the Continue button. The only
    /// non-terminal-looking status that is actually a rest state; <see cref="Cancelled"/> is not.
    /// </summary>
    Stopped = 4,

    Completed = 5,
    CompletedWithErrors = 6,

    /// <summary>Terminal and deliberately **not** resumable — the operator said stop.</summary>
    Cancelled = 7,

    Failed = 8
}

/// <summary>
/// One launched series of benchmark runs: *N* executions of one identical request, run strictly
/// one at a time, so their results form a replicate set.
///
/// <para>The series lives in the database rather than in the orchestrator because everything an
/// operator needs from it outlives the process: a series stopped on a failed member is resumed
/// from the Continue button hours later, possibly after a restart, and the row is what
/// <see cref="CompletedRunCount"/> is read from. The orchestrator is reconstructed from this row,
/// never the other way round.</para>
///
/// <para>The three instrument hashes are recorded from the <b>first</b> member and re-checked on
/// resume. A replicate set whose members straddle a deployment is not a replicate set, and that
/// failure is silent — every downstream statistic would still compute, confidently, over
/// incomparable runs.</para>
/// </summary>
public class BenchmarkRunSeries
{
    public long Id { get; set; }

    public long? BenchmarkSuiteId { get; set; }
    public BenchmarkSuite? BenchmarkSuite { get; set; }

    [MaxLength(128)]
    public string SuiteName { get; set; } = default!;

    public int RequestedRunCount { get; set; }

    /// <summary>Members that reached a successful terminal state. A resume continues at this + 1.</summary>
    public int CompletedRunCount { get; set; }

    public int FailedRunCount { get; set; }

    public BenchmarkRunSeriesStatus Status { get; set; } = BenchmarkRunSeriesStatus.Pending;

    public BenchmarkRunSeriesStopReason? StopReason { get; set; }

    /// <summary>
    /// The validated <c>StartBenchmarkRunRequest</c> as JSON. Every member is launched from this one
    /// snapshot rather than from the live UI state, so the members cannot drift apart between the
    /// first launch and a resume days later.
    /// </summary>
    public string StartRequestJson { get; set; } = default!;

    /// <summary>True when a cap denial should pause and retry rather than stop the series.</summary>
    public bool AllowCapWait { get; set; }

    // The first member's instrument fingerprint, for the resume guard.

    [MaxLength(64)]
    public string? FirstMemberCandidateSystemPromptSha256 { get; set; }

    [MaxLength(64)]
    public string? FirstMemberToolGuidesSha256 { get; set; }

    [MaxLength(64)]
    public string? FirstMemberKnowledgeBaseHeadSha { get; set; }

    [MaxLength(40)]
    public string? FirstMemberWikiHeadSha { get; set; }

    [MaxLength(40)]
    public string? FirstMemberSourceCodeHeadSha { get; set; }

    /// <summary>
    /// True once a resume proceeded over a changed instrument through the explicit
    /// <c>acknowledgeInstrumentChange</c> override. The auto-created group is then Tier C and can
    /// never be pooled into one index — the dangerous case made impossible by construction rather
    /// than by discipline.
    /// </summary>
    public bool InstrumentChangeAcknowledged { get; set; }

    [MaxLength(450)]
    public string? StartedByUserId { get; set; }
    public ApplicationUser? StartedByUser { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Last time the orchestrator advanced this series. Used to spot an orphaned row.</summary>
    public DateTime? LastProgressAtUtc { get; set; }

    [MaxLength(2048)]
    public string? ErrorMessage { get; set; }

    /// <summary>The group auto-created from the completed members, when there were at least two.</summary>
    public long? AutoCreatedGroupId { get; set; }

    public ICollection<BenchmarkRun> Runs { get; set; } = new List<BenchmarkRun>();
}
