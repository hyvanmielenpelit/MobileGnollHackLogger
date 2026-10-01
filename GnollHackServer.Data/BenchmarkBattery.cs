namespace MobileGnollHackLogger.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// How a battery weighs its suites in the Overall Intelligence Index. The weights are declared with
/// the battery, before any result exists, and never chosen afterwards.
/// </summary>
public enum BenchmarkBatteryWeightingScheme
{
    /// <summary>
    /// "Questions and difficulty": a suite weighs the sum of its questions' difficulty weights. The
    /// Overall Index then equals one difficulty-weighted index over every question of every suite.
    /// </summary>
    DifficultyMass = 1,

    /// <summary>"Questions only": a suite weighs its number of exam questions.</summary>
    ItemCount = 2,

    /// <summary>"Equal per suite": every suite weighs 1 / K.</summary>
    Equal = 3,

    /// <summary>"Custom": declared positive numbers, normalized.</summary>
    Custom = 4
}

/// <summary>
/// A named, fixed set of two or more suites with declared weights, run together for one model
/// configuration and scored as a composite.
///
/// <para><see cref="Revision"/> and <see cref="DefinitionSha256"/> change with every edit to the
/// suites or the weights. A battery run stores its own snapshot of the definition, so editing the
/// battery never changes an existing result.</para>
/// </summary>
public class BenchmarkBattery
{
    public long Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public BenchmarkBatteryWeightingScheme WeightingScheme { get; set; } = BenchmarkBatteryWeightingScheme.DifficultyMass;

    /// <summary>Starts at 1; incremented on every change to the suites or the weights.</summary>
    public int Revision { get; set; } = 1;

    /// <summary>SHA-256 over the scheme and the sorted (suite id, custom weight) pairs, lower-case hex.</summary>
    [MaxLength(64)]
    public string DefinitionSha256 { get; set; } = default!;

    /// <summary>Hidden from the launcher without being deleted.</summary>
    public bool IsArchived { get; set; }

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;

    public List<BenchmarkBatterySuite> Suites { get; set; } = new();
}

/// <summary>
/// One suite of a battery, in run order. When the suite is deleted the row stays with a null
/// <see cref="BenchmarkSuiteId"/> and its <see cref="SuiteName"/>, and the battery is broken until
/// it is edited.
/// </summary>
public class BenchmarkBatterySuite
{
    public long Id { get; set; }

    public long BenchmarkBatteryId { get; set; }
    public BenchmarkBattery BenchmarkBattery { get; set; } = default!;

    public long? BenchmarkSuiteId { get; set; }
    public BenchmarkSuite? BenchmarkSuite { get; set; }

    [MaxLength(128)]
    public string SuiteName { get; set; } = default!;

    public int OrderIndex { get; set; }

    /// <summary>The declared weight under <see cref="BenchmarkBatteryWeightingScheme.Custom"/>; null otherwise.</summary>
    public double? CustomWeight { get; set; }
}
