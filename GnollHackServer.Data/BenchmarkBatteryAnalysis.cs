namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// A computed battery composite, persisted so a battery report stays reproducible after a member run
/// is deleted. <see cref="MemberRunIdsJson"/> records which runs the numbers were computed over, so a
/// later reader can tell a stale analysis from a current one.
///
/// <para>Nothing here is AI-written: every figure is reproducible arithmetic.</para>
/// </summary>
public class BenchmarkBatteryAnalysis
{
    public long Id { get; set; }

    public long BenchmarkBatteryRunId { get; set; }
    public BenchmarkBatteryRun BenchmarkBatteryRun { get; set; } = default!;

    public DateTime ComputedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>The usable member run ids the result was computed over, as a JSON array of longs.</summary>
    public string MemberRunIdsJson { get; set; } = default!;

    /// <summary>The full statistics result as JSON.</summary>
    public string ResultJson { get; set; } = default!;

    [MaxLength(64)]
    public string DefinitionSha256 { get; set; } = default!;

    /// <summary>
    /// SHA-256 over the per-suite must-match signatures of the usable members; null when the battery
    /// is incomplete. Two results stand in one ranked list only when this and the definition hash agree.
    /// </summary>
    [MaxLength(64)]
    public string? ComparabilityClassSha256 { get; set; }

    /// <summary>True when every suite had at least one usable member and the headline was computed.</summary>
    public bool Complete { get; set; }

    [MaxLength(64)]
    public string? HarnessVersion { get; set; }

    public int ScoringMethodVersion { get; set; }

    /// <summary>The other battery run this analysis was compared against, when it carries a comparison.</summary>
    public long? ComparedWithBatteryRunId { get; set; }

    /// <summary>The paired comparison against <see cref="ComparedWithBatteryRunId"/>, as JSON.</summary>
    public string? ComparisonJson { get; set; }

    [MaxLength(450)]
    public string? ComputedByUserId { get; set; }
    public ApplicationUser? ComputedByUser { get; set; }
}
