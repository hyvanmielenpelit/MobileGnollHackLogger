namespace MobileGnollHackLogger.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

/// <summary>How a run became a member of a battery run.</summary>
public enum BenchmarkBatteryMemberOrigin
{
    /// <summary>Launched by the battery orchestrator for this battery run.</summary>
    Launched = 1,

    /// <summary>An existing run attached to a slot.</summary>
    Attached = 2
}

/// <summary>
/// One execution of a battery for one model configuration: every suite of the battery, one after
/// another, <see cref="RunsPerSuite"/> times each in round-robin order.
///
/// <para>Like <see cref="BenchmarkRunSeries"/>, the row is the state: the orchestrator is rebuilt
/// from it after a restart, and a stopped battery run is resumed from it.</para>
/// </summary>
public class BenchmarkBatteryRun
{
    public long Id { get; set; }

    public long? BenchmarkBatteryId { get; set; }
    public BenchmarkBattery? BenchmarkBattery { get; set; }

    [MaxLength(128)]
    public string BatteryName { get; set; } = default!;

    /// <summary>The battery definition snapshot this run executes, as JSON.</summary>
    public string DefinitionJson { get; set; } = default!;

    [MaxLength(64)]
    public string DefinitionSha256 { get; set; } = default!;

    public int RunsPerSuite { get; set; } = 1;

    /// <summary>K × R.</summary>
    public int RequestedMemberCount { get; set; }

    /// <summary>
    /// Slots holding a usable member. Recomputed from the member rows on every save, never
    /// incremented: attaching, superseding and deleting a member run all change it.
    /// </summary>
    public int CompletedMemberCount { get; set; }

    /// <summary>Members whose run ended Failed or Canceled without the operator canceling the battery.</summary>
    public int FailedMemberCount { get; set; }

    public BenchmarkRunSeriesStatus Status { get; set; } = BenchmarkRunSeriesStatus.Pending;

    public BenchmarkRunSeriesStopReason? StopReason { get; set; }

    /// <summary>
    /// The validated <c>StartBenchmarkRunRequest</c>, resolved, as JSON. Its suite id is ignored and
    /// set per suite on a freshly deserialized copy at each launch.
    /// </summary>
    public string StartRequestJson { get; set; } = default!;

    /// <summary>True when a cap denial should pause and retry rather than stop the battery run.</summary>
    public bool AllowCapWait { get; set; }

    /// <summary>
    /// Per suite index, the five instrument hashes recorded at start, as JSON. A null hash means not
    /// recorded and never counts as a difference.
    /// </summary>
    public string? SuiteFingerprintsJson { get; set; }

    /// <summary>The per-suite run groups created on finish, by suite index, as JSON.</summary>
    public string? AutoCreatedGroupIdsJson { get; set; }

    [MaxLength(450)]
    public string? StartedByUserId { get; set; }
    public ApplicationUser? StartedByUser { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Last time the orchestrator advanced this battery run. Used to spot an orphaned row.</summary>
    public DateTime? LastProgressAtUtc { get; set; }

    [MaxLength(2048)]
    public string? ErrorMessage { get; set; }

    public List<BenchmarkBatteryRunMember> Members { get; set; } = new();
}

/// <summary>
/// One run filling one (suite, round) slot of a battery run. A run may serve several battery runs.
/// At most one non-superseded member occupies a slot.
/// </summary>
public class BenchmarkBatteryRunMember
{
    public long Id { get; set; }

    public long BenchmarkBatteryRunId { get; set; }
    public BenchmarkBatteryRun BenchmarkBatteryRun { get; set; } = default!;

    public long BenchmarkRunId { get; set; }
    public BenchmarkRun BenchmarkRun { get; set; } = default!;

    /// <summary>0-based position of the suite in the battery run's definition snapshot.</summary>
    public int SuiteIndex { get; set; }

    /// <summary>1-based replicate round.</summary>
    public int Round { get; set; }

    public BenchmarkBatteryMemberOrigin Origin { get; set; } = BenchmarkBatteryMemberOrigin.Launched;

    /// <summary>Replaced by another member, or freed for one; never counted in any statistic.</summary>
    public bool Superseded { get; set; }

    /// <summary>
    /// Why this member made the orchestrator stop with an instrument change. A member carrying one is
    /// not usable.
    /// </summary>
    [MaxLength(512)]
    public string? GuardFailure { get; set; }

    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
}
