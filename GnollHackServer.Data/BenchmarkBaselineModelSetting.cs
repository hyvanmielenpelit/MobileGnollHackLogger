namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// One change of the GnollBench baseline model: the model the Overseer team currently prefers, which
/// Model Comparison and the comparison-wide documents judge the other models against.
///
/// <para>Append-only: the current baseline model is the row with the highest <see cref="Id"/>, and a row
/// with a null <see cref="ModelSnapshotId"/> records that it was cleared. Rows are never updated or
/// deleted, so the history stays readable.</para>
/// </summary>
public class BenchmarkBaselineModelSetting
{
    public long Id { get; set; }

    /// <summary>The baseline model's settings; null when this row clears it.</summary>
    public long? ModelSnapshotId { get; set; }
    public SystemAiConfigurationSnapshot? ModelSnapshot { get; set; }

    /// <summary>The configuration it was chosen from, as a plain id for the UI; the snapshot is what identifies the model.</summary>
    public long? SourceConfigurationId { get; set; }

    public DateTime SetAtUtc { get; set; }

    [MaxLength(450)]
    public string? SetByUserId { get; set; }
}
