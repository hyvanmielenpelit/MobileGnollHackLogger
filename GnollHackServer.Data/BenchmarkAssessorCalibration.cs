namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// One non-destructive re-grading of a completed run's answers by an alternative assessor,
/// recording how that assessor's verdicts compare with the ones the run actually scored.
///
/// It exists because the assessor decision — which model grades every future run — was otherwise
/// made from assumption. A calibration makes <b>no candidate calls at all</b>: it is a single
/// assessor pass over answers already stored, which makes it the cheapest AI operation in the
/// subsystem and lets a prospective assessor be compared like-for-like against the recorded cost
/// of the one it would replace.
///
/// Deliberately a separate table rather than a bulk application of trial mode: a calibration must
/// never write to <see cref="BenchmarkRunAnswer"/>, so it can neither collide with a real second
/// opinion nor move a published index. For the same reason it is absent from the Markdown report
/// — it is an experiment about graders, not a property of the run, and printing it would invite
/// reading a calibration verdict as a result.
/// </summary>
public class BenchmarkAssessorCalibration
{
    public long Id { get; set; }

    public long BenchmarkRunId { get; set; }
    public BenchmarkRun BenchmarkRun { get; set; } = default!;

    // The assessor under evaluation, snapshotted exactly as the run snapshots its own models:
    // a configuration can be edited or deleted afterwards, and a calibration that cannot say
    // what produced it is worthless as evidence.
    public long? AssessorModelConfigurationId { get; set; }
    public SystemAiApiConfiguration? AssessorModelConfiguration { get; set; }

    [MaxLength(256)]
    public string? AssessorDisplayNameUsed { get; set; }

    [MaxLength(64)]
    public string? AssessorProviderUsed { get; set; }

    [MaxLength(128)]
    public string? AssessorModelIdUsed { get; set; }

    [MaxLength(32)]
    public string? AssessorThinkingLevelUsed { get; set; }

    [MaxLength(32)]
    public string? AssessorReasoningModeUsed { get; set; }

    [MaxLength(64)]
    public string? AssessorServiceTierUsed { get; set; }

    public int? AssessorMaxOutputTokensUsed { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(256)]
    public string? CreatedByUserName { get; set; }

    /// <summary>Answers actually graded. Answers whose status is not Ok are skipped.</summary>
    public int AnswerCount { get; set; }

    /// <summary>Answers skipped because their status was not Ok, reported so the coverage is legible.</summary>
    public int SkippedAnswerCount { get; set; }

    /// <summary>
    /// Mean |calibration - original| quality across graded answers, and the count of material
    /// disagreements. Both use the same definitions as a live run's agreement figures — a gap
    /// above 15 quality points, or a split on criticalError — so a calibration and an All-mode
    /// run are read the same way.
    /// </summary>
    public double? MeanAbsDelta { get; set; }

    public int DisagreementCount { get; set; }

    // The calibration's own cost, for comparison against the run's recorded assessor cost.
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public long DurationMs { get; set; }

    /// <summary>
    /// The per-answer verdicts, keyed by order index, as JSON. Kept whole rather than reduced to
    /// the aggregates above so that a disagreement can be read rather than merely counted.
    /// </summary>
    public string? VerdictsJson { get; set; }

    [MaxLength(2048)]
    public string? ErrorMessage { get; set; }
}
