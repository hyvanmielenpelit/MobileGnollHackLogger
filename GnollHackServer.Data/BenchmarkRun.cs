namespace MobileGnollHackLogger.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public enum BenchmarkDifficulty
{
    Simple = 1,
    Intermediate = 2,
    Advanced = 3
}

public enum BenchmarkRunStatus
{
    Running = 1,
    Completed = 2,
    CompletedWithErrors = 3,
    Failed = 4,
    Canceled = 5,
    // The run is valid; it only hit an operator-configured harness cap (currently the
    // per-question tool call budget). Distinct from CompletedWithErrors, which means a
    // transport or provider defect compromised answer validity.
    CompletedWithLimits = 6
}

public enum BenchmarkAnswerStatus
{
    Ok = 1,
    ProviderError = 2,
    Failed = 3,
    Skipped = 4,
    EmptyAnswer = 5
}

[Flags]
public enum BenchmarkAnswerFlags
{
    None = 0,

    // Transport defects. These compromise answer validity and drive the run status.
    Empty = 1,
    Truncated = 4,

    // A recoverable transport defect: leaked tool-call payloads and routing markers. The
    // scrubber removes them and the answer beneath is graded normally, so this classifies as
    // BenchmarkAnswerIntegrity.Recovered and never fails a run on its own. It still points at
    // a real provider-path bug, which is why it is a flag and not silence.
    HarnessArtifacts = 2,

    // Advisory flags. Reported, but they never change the run status and never remove an
    // answer from the Clean count: the affected text is removed before grading, so the
    // graded answer is unaffected. See BenchmarkRunFinalizer.HasAdvisoryFlag.
    ReasoningBleed = 8,
    RepeatedFragments = 16,

    // The assessor's own comment or evidence describes a fabrication ("hallucinates",
    // "invents", "does not exist") while its criticalError flag is false. Advisory, and
    // deliberately grouped here rather than with the transport defects: the answer is intact and
    // the verdict may well be right. What it is not is unambiguous, so it becomes a
    // second-opinion trigger and a report line for a human. Adding it to the defect flags would
    // flip healthy runs to CompletedWithErrors, which is the regression harness version 4 exists
    // to undo. See BenchmarkVerdictConsistency.
    ContestedVerdict = 32
}

/// <summary>
/// How a run uses its second-opinion assessor. The values increase monotonically in coverage and
/// in assessor cost, and each maps to exactly one execution shape — which is why the outlier
/// sweep is a mode of its own rather than a numeric toggle inside <see cref="Flagged"/>: the run
/// progress dialog has to know whether a third stage exists, and that must be a mode comparison
/// rather than a check on a delta field.
///
/// Every value is inert without a second-opinion assessor configured on the run. That hard gate
/// is why the 2026-09-03 run produced no second verdicts at all.
/// </summary>
public enum BenchmarkSecondOpinionMode
{
    /// <summary>Never. Equivalent to selecting no second-opinion assessor.</summary>
    Off = 0,

    /// <summary>Per-answer triggers only. Two execution stages, as today.</summary>
    Flagged = 1,

    /// <summary>
    /// The triggers, plus a post-scoring sweep for answers far below the run's own median. The
    /// sweep needs the median, so it runs after every answer is scored: this is the only mode
    /// with a third execution stage.
    /// </summary>
    FlaggedAndOutliers = 2,

    /// <summary>
    /// Every answer graded twice. Two stages — the second verdict is produced per-answer exactly
    /// like the first — and the only mode that yields an *unbiased* grader agreement rate. Under
    /// the trigger-based modes the disagreement rate is conditioned on the first assessor's own
    /// uncertainty, so it measures nothing about the instrument.
    /// </summary>
    All = 3
}

/// <summary>
/// Classification of a single answer for run integrity accounting. Every answer falls into
/// exactly one bucket, so Clean + TransportDefect + Recovered + HarnessLimit always equals the
/// question count. Advisory flags are tracked separately and may overlap any bucket.
/// </summary>
public enum BenchmarkAnswerIntegrity
{
    Clean = 0,

    /// <summary>Corrupted beyond recovery: empty, truncated, or a provider error.</summary>
    TransportDefect = 1,

    /// <summary>An operator-configured cap was reached. The answer is valid.</summary>
    HarnessLimit = 2,

    /// <summary>
    /// The provider leaked transport artifacts, the harness removed them, and the answer
    /// beneath graded normally. Reported, because it is a real provider-path defect; not an
    /// error, because the result is intact.
    /// </summary>
    Recovered = 3
}

public class BenchmarkRun
{
    public long Id { get; set; }

    public long? BenchmarkSuiteId { get; set; }
    public BenchmarkSuite? BenchmarkSuite { get; set; }

    [MaxLength(128)]
    public string SuiteName { get; set; } = default!;

    // Tested Model Config snapshot
    public long? TestedModelConfigurationId { get; set; }
    public SystemAiApiConfiguration? TestedModelConfiguration { get; set; }

    [MaxLength(64)]
    public string TestedModelProviderUsed { get; set; } = default!;

    [MaxLength(128)]
    public string TestedModelIdUsed { get; set; } = default!;

    [MaxLength(256)]
    public string TestedModelDisplayNameUsed { get; set; } = default!;

    [MaxLength(32)]
    public string? TestedModelThinkingLevelUsed { get; set; }

    [MaxLength(32)]
    public string? TestedModelReasoningModeUsed { get; set; }

    [MaxLength(32)]
    public string? TestedModelReasoningSummaryUsed { get; set; }

    [MaxLength(64)]
    public string? TestedModelServiceTierUsed { get; set; }

    public int? TestedModelMaxOutputTokensUsed { get; set; }

    public ParallelExecutionMode TestedModelParallelExecutionModeUsed { get; set; } = ParallelExecutionMode.Enabled;

    // Assessor Model Config snapshot
    public long? AssessorModelConfigurationId { get; set; }
    public SystemAiApiConfiguration? AssessorModelConfiguration { get; set; }

    [MaxLength(64)]
    public string AssessorModelProviderUsed { get; set; } = default!;

    [MaxLength(128)]
    public string AssessorModelIdUsed { get; set; } = default!;

    [MaxLength(256)]
    public string AssessorModelDisplayNameUsed { get; set; } = default!;

    [MaxLength(32)]
    public string? AssessorModelThinkingLevelUsed { get; set; }

    [MaxLength(32)]
    public string? AssessorModelReasoningModeUsed { get; set; }

    [MaxLength(32)]
    public string? AssessorModelReasoningSummaryUsed { get; set; }

    [MaxLength(64)]
    public string? AssessorModelServiceTierUsed { get; set; }

    public int? AssessorModelMaxOutputTokensUsed { get; set; }

    public ParallelExecutionMode AssessorModelParallelExecutionModeUsed { get; set; } = ParallelExecutionMode.Enabled;

    // Second Opinion Assessor snapshot.
    //
    // Null means this run performs no second-opinion re-grading. There is deliberately no
    // fallback to the assessor above: asking one model to check its own verdict buys
    // agreement, not a second reading. Like every other model choice for a run, this one is
    // made in the start dialog and recorded here — a SystemAiApiConfiguration id is a database
    // identity and belongs nowhere near a settings file.
    public long? SecondOpinionAssessorModelConfigurationId { get; set; }
    public SystemAiApiConfiguration? SecondOpinionAssessorModelConfiguration { get; set; }

    [MaxLength(64)]
    public string? SecondOpinionAssessorModelProviderUsed { get; set; }

    [MaxLength(128)]
    public string? SecondOpinionAssessorModelIdUsed { get; set; }

    [MaxLength(256)]
    public string? SecondOpinionAssessorModelDisplayNameUsed { get; set; }

    [MaxLength(32)]
    public string? SecondOpinionAssessorModelThinkingLevelUsed { get; set; }

    [MaxLength(32)]
    public string? SecondOpinionAssessorModelReasoningModeUsed { get; set; }

    // Run metadata
    [MaxLength(450)]
    public string? StartedByUserId { get; set; }
    public ApplicationUser? StartedByUser { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    public BenchmarkRunStatus Status { get; set; } = BenchmarkRunStatus.Running;

    [MaxLength(2048)]
    public string? ErrorMessage { get; set; }

    public int? FinalScore { get; set; }

    // Superseded by QualityIndex
    public int? ComputedScore { get; set; }

    public int? QualityIndex { get; set; }

    public int? SpeedIndex { get; set; }

    public long TotalAnswerDurationMs { get; set; }

    public long? ScoringProfileId { get; set; }
    public BenchmarkScoringProfile? ScoringProfile { get; set; }

    public string? ScoringProfileSnapshotJson { get; set; }

    public int ScoringMethodVersion { get; set; }

    public bool DifficultyFallbackUsed { get; set; }

    public bool SpeedMeasurementDegraded { get; set; }

    public int MaxParallelQuestionsUsed { get; set; } = 1;

    public int AnsweredQuestionCount { get; set; }

    public int TotalQuestionCount { get; set; }

    public int DegradedAnswerCount { get; set; }

    public int ToolStarvedAnswerCount { get; set; }

    // Answers whose validity is compromised beyond recovery (empty or truncated).
    // Disjoint from RecoveredAnswerCount and ToolStarvedAnswerCount.
    public int TransportDefectAnswerCount { get; set; }

    // Answers the harness repaired: leaked transport artifacts were removed and the answer
    // beneath was graded normally. A provider-path defect worth reporting, not a run failure.
    public int RecoveredAnswerCount { get; set; }

    // Answers carrying an advisory flag (reasoning bleed, repeated fragments). May overlap
    // both counts above, so it is reported separately and never summed with them.
    public int AdvisoryFlagAnswerCount { get; set; }

    // Answers from which the harness removed at least one transport artifact block.
    public int ScrubbedArtifactAnswerCount { get; set; }

    // Answers whose assessor described a fabrication while leaving criticalError false.
    // Advisory; overlaps the counts above and is never summed with them.
    public int ContestedVerdictAnswerCount { get; set; }

    // Answers whose verdict was replaced after the run finished, by the re-assess action.
    // A published index can move after publication, so the report says when it has.
    public int ReassessedAnswerCount { get; set; }

    // The equal-weight mean of the per-question quality scores, beside the difficulty-weighted
    // QualityIndex above. Nullable because runs before harness version 7 never recorded it:
    // null means "not recorded", not zero.
    //
    // Both numbers are needed because they answer different questions and can differ by
    // several points. On the 2026-09-03 run the weighted index read 94 and the plain mean 92:
    // the two weakest answers were also the two easiest questions, so difficulty weighting
    // lifted the headline *because* the model failed easy questions. That is a legitimate
    // property of a difficulty-weighted metric, and it should be visible rather than implicit.
    public int? UnweightedQualityIndex { get; set; }

    // How the second-opinion assessor was used, snapshotted from the scoring profile or the
    // start dialog's per-run override. Stored on the run so a report can always say what
    // produced its second verdicts, and what coverage the agreement figures rest on.
    public int SecondOpinionModeUsed { get; set; }

    // Answers that received a second verdict, and the mean |first - second| quality gap across
    // them. Read together with the mode: a mean delta over 4 of 18 trigger-selected answers is
    // conditioned on the first assessor's own uncertainty and is not an unbiased estimate of
    // grader agreement, while the same figure over 18 of 18 is.
    public int SecondOpinionGradedAnswerCount { get; set; }

    public double? SecondOpinionMeanAbsDelta { get; set; }

    // Total wall-clock time spent executing tool batches across the run. Subtracting this
    // from TotalAnswerDurationMs gives the model-attributable time that speed is scored on.
    public long? ToolOverheadMs { get; set; }

    public int? MaxToolCallsPerQuestionUsed { get; set; }

    [MaxLength(32)]
    public string? HarnessVersion { get; set; }

    [MaxLength(2048)]
    public string? PurposeStatementUsed { get; set; }

    public bool SameProviderAcknowledged { get; set; }

    public string? AssessmentJson { get; set; }

    public string? AssessmentText { get; set; }

    public bool AssessmentParseFailed { get; set; }

    // Token totals and duration
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalCacheReadTokens { get; set; }
    public long TotalCacheCreationTokens { get; set; }
    public long TotalDurationMs { get; set; }

    // Assessor-side usage, deliberately kept apart from the candidate totals above. Those
    // measure the model under test and must not absorb the grader's consumption; together the
    // two are what the run actually cost, which was previously not recorded anywhere.
    public long TotalAssessmentInputTokens { get; set; }
    public long TotalAssessmentOutputTokens { get; set; }
    public long TotalAssessmentDurationMs { get; set; }

    public List<BenchmarkRunAnswer> Answers { get; set; } = new();
}
