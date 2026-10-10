namespace MobileGnollHackLogger.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

/// <summary>What every member of a model batch runs: one suite, or one battery.</summary>
public enum BenchmarkModelBatchTargetKind
{
    Suite = 1,
    Battery = 2
}

/// <summary>The order the members of a model batch run in.</summary>
public enum BenchmarkModelBatchOrder
{
    /// <summary>The order the operator listed the models in.</summary>
    AsListed = 1,

    /// <summary>A Fisher–Yates shuffle with <see cref="BenchmarkModelBatchRun.OrderSeed"/>.</summary>
    Randomized = 2
}

/// <summary>
/// Why a model batch stopped. Set whenever <see cref="BenchmarkModelBatchRun.Status"/> is
/// <see cref="BenchmarkRunSeriesStatus.Stopped"/>, and never otherwise.
/// </summary>
public enum BenchmarkModelBatchStopReason
{
    /// <summary>A member failed, was canceled, or its series or battery run stopped (MB-R3).</summary>
    MemberStopped = 1,

    /// <summary>An instrument hash, the harness or the scoring method moved since the first member (MB-R1).</summary>
    InstrumentChanged = 2,

    /// <summary>A grader's recorded configuration differs from the first member's (MB-R2).</summary>
    GraderConfigChanged = 3,

    /// <summary>The run cap blocked a launch and waiting was not allowed, or the wait ran out.</summary>
    RunCapReached = 4,

    /// <summary>The spend guard refused a launch for a reason other than the run cap.</summary>
    SpendDenied = 5,

    /// <summary>The service restarted while the batch was running (MB-R4).</summary>
    RestartReconciled = 6
}

/// <summary>Where one member of a model batch stands.</summary>
public enum BenchmarkModelBatchMemberStatus
{
    Pending = 1,
    Running = 2,
    Completed = 3,
    CompletedWithErrors = 4,
    Stopped = 5,
    Failed = 6,
    Skipped = 7,
    Canceled = 8
}

/// <summary>
/// Several models under test, run one after another under one start request: a single run, a
/// replicate series or a battery run per model. Like <see cref="BenchmarkBatteryRun"/>, the row is
/// the state: the orchestrator is rebuilt from it after a restart, and a stopped batch is resumed
/// from it.
/// </summary>
public class BenchmarkModelBatchRun
{
    public long Id { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>The admin who started the batch. Attribution only, not a foreign key.</summary>
    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    public BenchmarkModelBatchTargetKind TargetKind { get; set; } = BenchmarkModelBatchTargetKind.Suite;

    /// <summary>The suite every member runs; set for a suite target. Not a foreign key.</summary>
    public long? BenchmarkSuiteId { get; set; }

    /// <summary>The battery every member runs; set for a battery target. Not a foreign key.</summary>
    public long? BenchmarkBatteryId { get; set; }

    /// <summary>The suite or battery name at start.</summary>
    [MaxLength(128)]
    public string? TargetName { get; set; }

    /// <summary>The battery's revision at start; null for a suite target.</summary>
    public int? BatteryRevision { get; set; }

    /// <summary>The battery's definition hash at start; a member is refused once it moves.</summary>
    [MaxLength(64)]
    public string? BatteryDefinitionSha256 { get; set; }

    /// <summary>R: runs per model on a suite, or runs per suite on a battery.</summary>
    public int RunsPerModel { get; set; } = 1;

    public BenchmarkModelBatchOrder Order { get; set; } = BenchmarkModelBatchOrder.Randomized;

    /// <summary>The shuffle seed of a <see cref="BenchmarkModelBatchOrder.Randomized"/> order.</summary>
    public int? OrderSeed { get; set; }

    /// <summary>
    /// The shared <c>StartBenchmarkRunRequest</c> template as JSON, its tested configuration id 0.
    /// Every member is launched from it with only the tested configuration set.
    /// </summary>
    public string StartRequestJson { get; set; } = default!;

    /// <summary>True when a run-cap denial pauses and retries rather than stopping the batch.</summary>
    public bool AllowCapWait { get; set; }

    public BenchmarkRunSeriesStatus Status { get; set; } = BenchmarkRunSeriesStatus.Pending;

    public BenchmarkModelBatchStopReason? StopReason { get; set; }

    [MaxLength(1024)]
    public string? StopDetail { get; set; }

    /// <summary>The warnings acknowledged at start: code, title and affected model ids, as JSON.</summary>
    public string AcknowledgedFindingsJson { get; set; } = "[]";

    /// <summary>The advice shown at start, as JSON.</summary>
    public string AdviceAtStartJson { get; set; } = "[]";

    // The first member's instrument, for the run-time guard (MB-R1).

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

    [MaxLength(64)]
    public string? FirstMemberHarnessVersion { get; set; }

    public int? FirstMemberScoringMethodVersion { get; set; }

    /// <summary>
    /// True once the operator continued over a moved instrument; the batch is then not comparable
    /// across the change.
    /// </summary>
    public bool InstrumentChangeAcknowledged { get; set; }

    public int RequestedMemberCount { get; set; }

    public int CompletedMemberCount { get; set; }

    public int FailedMemberCount { get; set; }

    public int SkippedMemberCount { get; set; }

    /// <summary>The <see cref="BenchmarkModelBatchMember.OrderIndex"/> of the member in flight or last launched.</summary>
    public int? CurrentMemberIndex { get; set; }

    /// <summary>Last time the orchestrator advanced this batch. Read by the stall flag (MB-R5).</summary>
    public DateTime? LastProgressAtUtc { get; set; }

    /// <summary>
    /// The member links a Re-run under current instrument replaced, as JSON: each member's earlier
    /// run, series and battery run ids, so the earlier results stay traceable.
    /// </summary>
    public string? SupersededMembersJson { get; set; }

    public List<BenchmarkModelBatchMember> Members { get; set; } = new();
}

/// <summary>One model of a model batch, and the run, series or battery run it produced.</summary>
public class BenchmarkModelBatchMember
{
    public long Id { get; set; }

    public long BenchmarkModelBatchRunId { get; set; }
    public BenchmarkModelBatchRun BenchmarkModelBatchRun { get; set; } = default!;

    /// <summary>0-based position in run order.</summary>
    public int OrderIndex { get; set; }

    /// <summary>The model configuration under test. Not a foreign key: a configuration may be deleted later.</summary>
    public long TestedModelConfigurationId { get; set; }

    /// <summary>
    /// The configuration's display name, provider, model id, thinking level, reasoning mode, service
    /// tier, parallel mode, endpoint kind and max output tokens at start, as JSON.
    /// </summary>
    public string TestedModelSnapshotJson { get; set; } = "{}";

    public BenchmarkModelBatchMemberStatus Status { get; set; } = BenchmarkModelBatchMemberStatus.Pending;

    /// <summary>The single run of a suite member with one run per model.</summary>
    public long? BenchmarkRunId { get; set; }
    public BenchmarkRun? BenchmarkRun { get; set; }

    /// <summary>The replicate series of a suite member with two or more runs per model.</summary>
    public long? BenchmarkRunSeriesId { get; set; }
    public BenchmarkRunSeries? BenchmarkRunSeries { get; set; }

    /// <summary>The battery run of a battery member.</summary>
    public long? BenchmarkBatteryRunId { get; set; }
    public BenchmarkBatteryRun? BenchmarkBatteryRun { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    [MaxLength(1024)]
    public string? ErrorMessage { get; set; }
}
