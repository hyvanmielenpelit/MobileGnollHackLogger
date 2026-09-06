namespace MobileGnollHackLogger.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// How comparable a set of runs is, and therefore what may be computed over it.
///
/// <para>The tiers are the guard against this feature's worst failure: a confident pooled index
/// computed over runs that were never comparable. A tier is resolved from the runs themselves by
/// <c>Overseer.Services.Benchmarking.BenchmarkComparabilityKey</c>, never asserted by whoever built
/// the group.</para>
///
/// <para><b>The member names and numeric values are identical to that resolver's
/// <c>BenchmarkComparabilityTier</c>, deliberately.</b> This is the persisted form of the same
/// concept — it exists separately only because the data assembly cannot reference Overseer — and
/// keeping the two in exact correspondence makes the conversion a cast rather than a lookup table
/// that could drift. The ordering is meaningful: a higher value is more comparable, so a minimum
/// tier can be expressed as a comparison.</para>
/// </summary>
public enum BenchmarkRunGroupTier
{
    /// <summary>
    /// Below Tier B. The runs measure different things and no aggregate over them means anything.
    /// A group at this tier must not be persisted; the differing keys say why.
    /// </summary>
    NotComparable = 0,

    /// <summary>
    /// Tier C — cross-condition. The candidate specification is identical and exactly one
    /// instrument key was deliberately moved. **Never pooled into one index:** such a set is really
    /// two groups, and the tool's job is to compare them.
    /// </summary>
    CrossCondition = 1,

    /// <summary>
    /// Tier B — quality-comparable. Tier A relaxed on the keys that affect speed and cost only.
    /// Quality aggregates are valid; speed and cost aggregates carry a degraded flag.
    /// </summary>
    QualityComparable = 2,

    /// <summary>Tier A — replicate. Every key matches. The only tier at which pooling is sound.</summary>
    Replicate = 3
}

/// <summary>
/// A named set of runs analysed together. Membership is many-to-many with
/// <see cref="BenchmarkRun"/>, so one run may sit in several analysis groups — a baseline run
/// belongs both to its own replicate set and to the cross-condition pair it anchors — while
/// belonging to at most one <see cref="BenchmarkRunSeries"/>.
/// </summary>
public class BenchmarkRunGroup
{
    public long Id { get; set; }

    [MaxLength(256)]
    public string Name { get; set; } = default!;

    public long? BenchmarkSuiteId { get; set; }
    public BenchmarkSuite? BenchmarkSuite { get; set; }

    public BenchmarkRunGroupTier Tier { get; set; } = BenchmarkRunGroupTier.NotComparable;

    /// <summary>
    /// Hash of the Tier A key set the members agreed on, so two groups can be recognised as sharing
    /// a condition without re-deriving every key.
    /// </summary>
    [MaxLength(64)]
    public string? ComparabilityKeyHash { get; set; }

    /// <summary>
    /// The keys that differed when the tier was resolved, as JSON, with the runs they differed on.
    /// A tier verdict with no reasons is unusable in a dialog or a bug report.
    /// </summary>
    public string? TierReasonsJson { get; set; }

    /// <summary>
    /// True when the operator explicitly declared this a cross-condition comparison. Tier C is only
    /// permitted with this flag set, so a set that quietly stopped being a replicate set cannot
    /// become one by accident.
    /// </summary>
    public bool CrossCondition { get; set; }

    public string? Notes { get; set; }

    /// <summary>Set when the group was created automatically at the end of a series.</summary>
    public long? CreatedFromSeriesId { get; set; }

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<BenchmarkRunGroupMember> Members { get; set; } = new List<BenchmarkRunGroupMember>();
}
