namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// A computed multi-run analysis, persisted so a group report stays reproducible after a member run
/// is deleted.
///
/// <para><see cref="MemberRunIdsJson"/> is the load-bearing field: it records which runs the numbers
/// were computed over, which is how a later reader can tell that a group whose membership has since
/// changed carries a **stale** analysis rather than a wrong one. The harness and scoring method
/// versions are stored for the same reason — an analysis is only meaningful against the instrument
/// that produced it.</para>
///
/// <para>Nothing here is AI-written. Every figure in the report built from this row is reproducible
/// arithmetic, which is what makes the report usable as an instrument for verifying a change.</para>
/// </summary>
public class BenchmarkGroupAnalysis
{
    public long Id { get; set; }

    public long BenchmarkRunGroupId { get; set; }
    public BenchmarkRunGroup BenchmarkRunGroup { get; set; } = default!;

    public DateTime ComputedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>The member run ids the result was computed over, as a JSON array of longs.</summary>
    public string MemberRunIdsJson { get; set; } = default!;

    /// <summary>Number of member runs — *R* — kept out of the JSON so it can be listed and sorted.</summary>
    public int RunCount { get; set; }

    /// <summary>The full statistics result as JSON.</summary>
    public string ResultJson { get; set; } = default!;

    public BenchmarkRunGroupTier TierAtComputation { get; set; }

    [MaxLength(64)]
    public string? HarnessVersion { get; set; }

    public int ScoringMethodVersion { get; set; }

    /// <summary>The other group this analysis was compared against, when it carries a comparison.</summary>
    public long? ComparedWithGroupId { get; set; }

    /// <summary>
    /// The paired comparison against <see cref="ComparedWithGroupId"/>, as JSON, or null when the
    /// group was analysed alone. Stored beside the result rather than recomputed on demand for the
    /// same reason the result is: the baseline group's membership may change afterwards, and a
    /// report must keep describing the comparison that was actually run.
    /// </summary>
    public string? ComparisonJson { get; set; }

    [MaxLength(450)]
    public string? ComputedByUserId { get; set; }
    public ApplicationUser? ComputedByUser { get; set; }
}
