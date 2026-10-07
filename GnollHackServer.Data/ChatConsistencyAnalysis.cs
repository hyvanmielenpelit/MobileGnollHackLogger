namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// One saved GnollBench chat consistency analysis: whether the Overseer chat with one model changed
/// between a baseline and a comparison period, and who changed it. Immutable once written; a new
/// analysis is a new row.
///
/// <para>Runs are referenced by id, without foreign keys, so deleting a run keeps the analysis.
/// <see cref="InputSha256"/> fingerprints the inputs the result was computed from.</para>
/// </summary>
public class ChatConsistencyAnalysis
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = default!;

    /// <summary>The candidate model axis: provider, model, thinking level, tier, endpoint.</summary>
    [MaxLength(512)]
    public string SubjectModelKey { get; set; } = default!;

    /// <summary>Attribution only, not a foreign key.</summary>
    public long? SubjectConfigurationId { get; set; }

    public DateTime BaselineStartUtc { get; set; }
    public DateTime BaselineEndUtc { get; set; }
    public DateTime ComparisonStartUtc { get; set; }
    public DateTime ComparisonEndUtc { get; set; }

    /// <summary>The subject's runs analyzed, as a JSON array of run ids.</summary>
    public string TargetRunIdsJson { get; set; } = default!;

    /// <summary>The control runs (other model axes, same Overseer build), as a JSON array of run ids.</summary>
    public string ControlRunIdsJson { get; set; } = default!;

    [MaxLength(16)]
    public string ProtocolVersion { get; set; } = default!;

    /// <summary>The protocol as applied, overrides included.</summary>
    public string ProtocolJson { get; set; } = default!;

    /// <summary>Pooling across a measurement segment boundary was chosen; caps grades at Indicated.</summary>
    public bool RelaxedPooling { get; set; }

    /// <summary>
    /// The assessor snapshot whose calibration verdicts served as the common grader; null when native
    /// grades were compared. A plain id, not a foreign key.
    /// </summary>
    public long? CommonGraderSnapshotId { get; set; }

    public string ResultJson { get; set; } = default!;

    [MaxLength(64)]
    public string InputSha256 { get; set; } = default!;

    public int AnalysisCodeVersion { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
