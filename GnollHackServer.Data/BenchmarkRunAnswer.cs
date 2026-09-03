namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public enum BenchmarkAssessmentStatus
{
    Pending = 1,
    Assessing = 2,
    Scored = 3,
    Failed = 4
}

public class BenchmarkRunAnswer
{
    public long Id { get; set; }

    public long BenchmarkRunId { get; set; }
    public BenchmarkRun BenchmarkRun { get; set; } = default!;

    public int OrderIndex { get; set; }

    public string QuestionText { get; set; } = default!;

    public BenchmarkDifficulty Difficulty { get; set; } = BenchmarkDifficulty.Simple;

    public string AnswerText { get; set; } = default!;

    public string? ThoughtText { get; set; }

    // Transport artifacts removed from AnswerText before grading (leaked tool-call payloads,
    // channel literals, reasoning narration). Retained verbatim so that a destructive
    // transformation stays auditable; shown in the admin UI, never sent to the assessor.
    public string? ScrubbedArtifactText { get; set; }

    public int ScrubbedArtifactCount { get; set; }

    // Reasoning-narration blocks removed from AnswerText, counted separately from
    // ScrubbedArtifactCount, which counts leaked tool-argument payloads. Nullable because
    // runs before harness version 6 never recorded it: null means "not recorded", not zero.
    // Without this column the report had to infer removal from ScrubbedArtifactText being
    // non-empty, which is also true when only a payload was removed - so it claimed
    // narration had been removed from answers that still carried it.
    public int? NarrationBlockCount { get; set; }

    public BenchmarkAnswerStatus Status { get; set; } = BenchmarkAnswerStatus.Ok;

    [MaxLength(2048)]
    public string? ErrorMessage { get; set; }

    public int? HttpStatusCode { get; set; }

    // Superseded by QualityScore and dimensional level scoring
    public int? Score { get; set; }

    public int? AccuracyLevel { get; set; }
    public int? CompletenessLevel { get; set; }
    public int? ConcisenessLevel { get; set; }
    public int? ReadabilityLevel { get; set; }

    public bool CriticalError { get; set; }

    public int? AccuracyScore { get; set; }
    public int? CompletenessScore { get; set; }
    public int? ConcisenessScore { get; set; }
    public int? ReadabilityScore { get; set; }

    public int? QualityScore { get; set; }
    public int? RawQualityScore { get; set; }
    public int? SpeedScore { get; set; }

    public int? AssessedDifficulty { get; set; }

    public BenchmarkAssessmentStatus AssessmentStatus { get; set; } = BenchmarkAssessmentStatus.Pending;

    [MaxLength(2048)]
    public string? AssessmentError { get; set; }

    // Assessor snapshot for THIS answer's score. Populated on every per-question
    // assessment; it differs from BenchmarkRun.Assessor*Used only when a manual retry
    // was run with a different assessor because the original was unavailable.
    public long? AssessedByModelConfigurationId { get; set; }
    public SystemAiApiConfiguration? AssessedByModelConfiguration { get; set; }

    [MaxLength(256)]
    public string? AssessedByModelDisplayNameUsed { get; set; }

    [MaxLength(64)]
    public string? AssessedByModelProviderUsed { get; set; }

    [MaxLength(128)]
    public string? AssessedByModelIdUsed { get; set; }

    public DateTime? AssessedAtUtc { get; set; }

    public string? ReviewComment { get; set; }

    // --- Assessment provenance and cost -----------------------------------------------------

    // What the grading call itself consumed. Never folded into the candidate's InputTokens /
    // OutputTokens below: those describe the model under test.
    public int? AssessmentInputTokens { get; set; }
    public int? AssessmentOutputTokens { get; set; }
    public long? AssessmentDurationMs { get; set; }

    /// <summary>
    /// The assessor's own justification, as JSON: for each of accuracy and completeness, the
    /// rubric point a deduction rests on, or an explicit statement that it rests on the
    /// assessor's own knowledge instead. That distinction is the thing a disputed score turns
    /// on, and before this column nothing recorded it.
    /// </summary>
    public string? AssessmentEvidenceJson { get; set; }

    /// <summary>
    /// The verbatim claim the assessor says is a critical error, quoted from the graded answer.
    /// A critical error caps quality at 25, so it must point at text the answer actually
    /// asserts; an unquoted one is demoted by the parser.
    /// </summary>
    [MaxLength(2048)]
    public string? CriticalErrorQuote { get; set; }

    // --- Second opinion ----------------------------------------------------------------------

    /// <summary>
    /// A second assessor's full verdict as JSON, produced when the first flagged a critical
    /// error or scored the answer below the configured threshold. Advisory: the first verdict
    /// stays authoritative for scoring, because silently replacing a score with whichever
    /// grader spoke last is not an improvement in accuracy, only in agreeableness.
    /// </summary>
    public string? SecondOpinionJson { get; set; }

    [MaxLength(256)]
    public string? SecondOpinionByModelDisplayNameUsed { get; set; }

    public int? SecondOpinionQualityScore { get; set; }

    public bool? SecondOpinionCriticalError { get; set; }

    /// <summary>
    /// The two verdicts disagree materially — more than 15 quality points apart, or split on
    /// the critical-error flag. Surfaced in the report and the UI so an operator can re-assess
    /// with full information rather than discovering it by reading comments.
    /// </summary>
    public bool SecondOpinionDisagreed { get; set; }

    public long DurationMs { get; set; }

    // Wall-clock time spent executing tool batches within this turn. Measured per batch, not
    // summed per tool, so concurrent tools inside one batch are not counted twice.
    public long? ToolTimeMs { get; set; }

    // Model-attributable time: the turn duration with harness tool I/O removed. This is what
    // the speed score is computed from.
    [NotMapped]
    public long ModelTimeMs => Math.Max(0L, DurationMs - (ToolTimeMs ?? 0L));

    public long? TimeToFirstTokenMs { get; set; }

    [MaxLength(64)]
    public string? ActualServiceTierUsed { get; set; }

    public string? ToolCallSummary { get; set; }

    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? CacheReadInputTokens { get; set; }
    public int? CacheCreationInputTokens { get; set; }

    public int? ModelCallCount { get; set; }
    public int? ToolCallCount { get; set; }
    public bool ToolBudgetExhausted { get; set; }

    // The per-question tool call budget that actually applied to this answer. The budget is
    // resolved per difficulty band, so it differs between questions in the same run and cannot
    // be read off BenchmarkRun.MaxToolCallsPerQuestionUsed.
    public int? ToolCallBudgetUsed { get; set; }
    [MaxLength(32)]
    public string? TerminationReason { get; set; }
    public int AnswerFlags { get; set; }
}
