namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>What a model comparison compares. A comparison never holds both.</summary>
public enum BenchmarkComparisonSubjectKind
{
    /// <summary>Single runs and analysis groups.</summary>
    Runs = 1,

    /// <summary>Battery results.</summary>
    Batteries = 2,
}

/// <summary>
/// A model comparison with a number of its own: one row per distinct entry set, keyed by
/// <see cref="ComparisonKey"/>, so the same selection of runs, groups or battery results is always the
/// same comparison whatever its order, pricing basis or excluded entries. The <see cref="Id"/> is the
/// number shown as <i>Comparison #Id</i> and is never reused.
/// </summary>
public class BenchmarkComparison
{
    public int Id { get; set; }

    /// <summary>
    /// Lower-case hex SHA-256 of the sorted entry set, as <c>BenchmarkReportComparisonKey.From</c>
    /// computes it; the same value report documents store as their comparison key. Unique.
    /// </summary>
    [MaxLength(64)]
    public string ComparisonKey { get; set; } = default!;

    /// <summary>The entry keys (<c>run:&lt;id&gt;</c>, <c>group:&lt;id&gt;</c> or <c>battery:&lt;id&gt;</c>) as a JSON array: runs, then groups, then battery results, each by ascending id.</summary>
    public string EntryKeysJson { get; set; } = default!;

    public BenchmarkComparisonSubjectKind SubjectKind { get; set; }

    public int EntryCount { get; set; }

    /// <summary>The name computed when the comparison was first identified, from its entries' labels.</summary>
    [MaxLength(160)]
    public string DefaultName { get; set; } = default!;

    /// <summary>An administrator's name for the comparison; null shows <see cref="DefaultName"/>.</summary>
    [MaxLength(160)]
    public string? Name { get; set; }

    /// <summary><see cref="Name"/> when set, otherwise <see cref="DefaultName"/>.</summary>
    [NotMapped]
    public string DisplayName => Name ?? DefaultName;

    public DateTime CreatedAtUtc { get; set; }

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    /// <summary>When <see cref="Name"/> was last set or reset.</summary>
    public DateTime? RenamedAtUtc { get; set; }
}
