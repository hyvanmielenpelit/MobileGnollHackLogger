namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// One distinct board a benchmark run was asked and graded with: the sanitized board text and the
/// digest beside it. Append-only and content-addressed: identical text and digest share one row, and
/// no suite operation updates or deletes a row. Runs reference it through
/// <see cref="BenchmarkRun.BoardSnapshotId"/>; the mutable <see cref="BenchmarkGameSnapshot"/> a suite
/// carries is never read to re-grade a run, so editing, replacing or deleting that board leaves every
/// earlier run's board unchanged.
/// </summary>
public class BenchmarkRunBoardSnapshot
{
    public long Id { get; set; }

    /// <summary>
    /// Lower-case hex SHA-256 of the UTF-16LE bytes of
    /// <c>SanitizedText + "\0" + (DigestText ?? "")</c>. Unique. Not the board's own
    /// <see cref="BenchmarkGameSnapshot.Sha256"/>, which covers the text alone; the digest can change
    /// while the text does not.
    /// </summary>
    [MaxLength(64)]
    [Column(TypeName = "char(64)")]
    public string Sha256 { get; set; } = default!;

    public string SanitizedText { get; set; } = default!;

    public string? DigestText { get; set; }

    public int CharCount { get; set; }

    /// <summary>When the row was first captured. Not hashed.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
