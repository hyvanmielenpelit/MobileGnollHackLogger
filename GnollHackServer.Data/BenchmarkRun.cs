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
    // per-question tool call budget). Distinct from CompletedWithErrors, which means either a
    // transport or provider defect compromised answer validity, or the model failed to answer
    // a question.
    CompletedWithLimits = 6
}

public enum BenchmarkAnswerStatus
{
    Ok = 1,
    ProviderError = 2,
    Failed = 3,
    Skipped = 4,
    EmptyAnswer = 5,

    // The operator canceled the run while this question's request was in flight; there is
    // nothing authored to grade, and a failed-question re-run re-executes it.
    Canceled = 6
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
    ContestedVerdict = 32,

    // The assessor docked ACCURACY or COMPLETENESS to UnevidencedDeductionMaxLevel or below while
    // its stated evidence names no defect ("Matches rubric."), or docked ACCURACY to that level
    // citing only unverifiability when unverified claims are present. Scoring method v7 forbids both
    // as accuracy deductions: a no-fault evidence string may only accompany level 6, and unverifiable
    // claims belong in unverifiedClaims rather than grounding an accuracy reduction. This flag is a
    // compliance check on those instructions rather than a guess about grader intent.
    //
    // Advisory, and grouped here for the same reason as ContestedVerdict: the answer is intact and
    // the verdict may well be right. Accuracy carries 55% of the quality weight, so a level docked on
    // an impermissible basis is the cheapest place this harness loses points to its own grader.
    // Nothing here changes a score.
    // See BenchmarkVerdictConsistency.HasUnevidencedDeduction and IsUnverifiabilityGroundedDeduction.
    UnevidencedDeduction = 64,

    // A claim the assessor recorded as unadjudicable was checked against the source or wiki by the
    // claim verifier and came back Refuted, with a citation. A real accuracy finding the first grader
    // was not equipped to make — and still advisory, for the same reason every other finding here is:
    // it arrives after scoring, from a model the rubric never sanctioned as a grader, and a metric
    // that can be revised after the fact by a later pass is not reproducible. Reported, never applied.
    RefutedClaim = 128,

    // The assessor docked ACCURACY to UnevidencedDeductionMaxLevel or below citing only an omission
    // (vocabulary like "omit", "fails to mention", "instead of") with no assertion of falsehood.
    // An omission is graded through COMPLETENESS; charging it on ACCURACY docks the answer on both
    // dimensions, costing 80% of the quality weight for one defect.
    //
    // Advisory, and grouped here for the same reason as UnevidencedDeduction: the answer is intact
    // and the verdict may well be right. What it is not is attributable to the dimension it was
    // charged on. Nothing here changes a score.
    // See BenchmarkVerdictConsistency.IsOmissionGroundedAccuracyDeduction.
    OmissionAsAccuracy = 256,

    // The assessor prefixed an ACCURACY deduction with the prompted marker "Not in rubric:",
    // declaring that the basis for it came from its own knowledge rather than from the rubric.
    // Scoring method v9 grades against the rubric, so a deduction the grader itself places outside
    // it is a deduction the instrument did not sanction.
    //
    // Advisory, and grouped here for the same reason as UnevidencedDeduction: the answer is intact
    // and the observation may well be correct. What it is not is a rubric finding. Nothing here
    // changes a score; it routes the verdict to a second reader.
    // See BenchmarkAssessmentParser.OutOfRubricAccuracyMarker.
    OutOfRubricAccuracyDeduction = 512,

    // The answer's first sentence claims sufficiency ("I now have all the pieces I need…") rather
    // than stating the finding, which Overseer/ToolGuides/_policy.md forbids outright.
    //
    // Advisory, and detected only: unlike the narration flags above, the text is NOT removed. The
    // opener reaches production chat unmodified, so removing it here would grade an answer no user
    // ever sees and would move the scoring method. See BenchmarkArtifactScrubber.AnswerFramingRegex.
    AnswerFramingOpener = 1024,

    // The claim verifier checked the assessor's own criticalErrorQuote against the source code and
    // wiki and returned Supported with a citation: the quoted claim is true, so the critical error
    // rests on a grader judgement the game's own code contradicts.
    //
    // Advisory, and grouped here for the same reason as every flag above: the quality cap the
    // critical error imposed stands, no index moves, and the verifier is a model whose verdict the
    // rubric never sanctioned as a grader. What the flag says is that the finding is *contested*,
    // never that it is overturned — a refutation and a support are both advisory evidence, and a
    // human reads the cited code path before anything rests on either. A second-opinion trigger is
    // already implied by CriticalError itself, so this adds no trigger of its own.
    ContestedCriticalError = 2048,

    // The claim verifier checked the assessor's own-knowledge basis for an ACCURACY deduction (the
    // text after "Not in rubric:") against the source code and wiki and returned Refuted with a
    // citation: the statement the deduction rests on is false.
    //
    // Advisory, and grouped here for the same reason as ContestedCriticalError: the deduction and
    // every score stand, and the verifier is a model the rubric never sanctioned as a grader. The flag
    // says the deduction is *contested*, never that it is overturned; a human reads the cited code
    // path before anything rests on it. OutOfRubricAccuracyDeduction already routes the verdict to a
    // second reader, so this adds no trigger of its own.
    ContestedAccuracyDeduction = 4096
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
    All = 3,

    /// <summary>
    /// <see cref="Flagged"/>, plus a deterministic top-up: after the per-answer triggers resolve,
    /// if fewer than <see cref="BenchmarkScoringProfile.SecondOpinionMinimumSample"/> answers were
    /// graded twice, the lowest-scoring untouched answers are graded twice as well, ties broken by
    /// ascending order index so the same data selects the same answers on every run. Two execution
    /// stages, like <see cref="Flagged"/> — there is no post-scoring sweep keyed off a run
    /// statistic such as the median, unlike <see cref="FlaggedAndOutliers"/>; the top-up target is
    /// a fixed count, not a computed one.
    ///
    /// This exists because under <see cref="Flagged"/> alone, coverage falls to zero exactly as a
    /// candidate gets good — a run with no answer below the threshold and no critical error
    /// produces no agreement figure at all. The top-up guarantees a sample every run, at a bounded
    /// cost. It still yields a *conditioned-plus-sample* agreement rate, not the unbiased rate
    /// <see cref="All"/> gives: the answers a run's own per-answer triggers pick are conditioned on
    /// the first assessor's own uncertainty exactly as under <see cref="Flagged"/>, and the
    /// deterministic top-up only adds a fixed number of the run's lowest scorers on top of that.
    /// Measuring instrument reliability without that conditioning still requires <see cref="All"/>.
    /// </summary>
    FlaggedPlusSample = 4
}

/// <summary>
/// Classification of a single answer for run integrity accounting. Every answer falls into
/// exactly one bucket, so Clean + TransportDefect + Recovered + HarnessLimit + Unanswered always
/// equals the question count. Advisory flags are tracked separately and may overlap any bucket.
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
    Recovered = 3,

    /// <summary>
    /// The model ended its turn normally and produced no answer. Scored 0 rather than excluded: the
    /// candidate failed the question, and excusing it let a model improve its index by not answering.
    /// Disjoint from <see cref="TransportDefect"/>, which is the harness's or the provider path's
    /// fault. Both keep the run at <see cref="BenchmarkRunStatus.CompletedWithErrors"/>; the buckets
    /// differ on cause, not on severity.
    /// </summary>
    Unanswered = 4
}

public class BenchmarkRun
{
    public long Id { get; set; }

    public long? BenchmarkSuiteId { get; set; }
    public BenchmarkSuite? BenchmarkSuite { get; set; }

    /// <summary>
    /// The suite this run was launched against, retained after the suite row is gone. Deleting a
    /// suite clears the foreign key above, so it cannot identify the exam; this can, and two runs
    /// from two different deleted suites stay distinguishable in the comparability keys. Not a
    /// foreign key, deliberately.
    /// </summary>
    public long? BenchmarkSuiteIdUsed { get; set; }

    [MaxLength(128)]
    public string SuiteName { get; set; } = default!;

    [MaxLength(128)]
    public string? GameSnapshotNameUsed { get; set; }

    [MaxLength(64)]
    public string? GameSnapshotSha256Used { get; set; }

    public int? GameSnapshotCharCountUsed { get; set; }

    [MaxLength(32)]
    public string? GameSnapshotCaptureMethodUsed { get; set; }

    /// <summary>Every question in the suite carried a human review stamp when this run started.</summary>
    public bool SuiteQuestionsReviewed { get; set; }

    public int SuiteReviewedQuestionCountAtStart { get; set; }

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

    // Claim Verifier snapshot.
    //
    // Null means this run performs no claim verification. Like every other model choice for a run,
    // this one is made in the start dialog and recorded here — a SystemAiApiConfiguration id is a
    // database identity and belongs nowhere near a settings file.
    public long? ClaimVerifierModelConfigurationId { get; set; }
    public SystemAiApiConfiguration? ClaimVerifierModelConfiguration { get; set; }

    [MaxLength(64)]
    public string? ClaimVerifierProviderUsed { get; set; }

    [MaxLength(128)]
    public string? ClaimVerifierModelIdUsed { get; set; }

    [MaxLength(256)]
    public string? ClaimVerifierDisplayNameUsed { get; set; }

    [MaxLength(32)]
    public string? ClaimVerifierThinkingLevelUsed { get; set; }

    [MaxLength(32)]
    public string? ClaimVerifierReasoningModeUsed { get; set; }

    // Run metadata
    [MaxLength(450)]
    public string? StartedByUserId { get; set; }
    public ApplicationUser? StartedByUser { get; set; }

    /// <summary>
    /// The series this run was launched as a member of, or null for a standalone run. A run belongs
    /// to at most one series — a series is how the run was produced, and that cannot change — while
    /// it may sit in any number of analysis groups, which are how it is later read.
    /// </summary>
    public long? RunSeriesId { get; set; }
    public BenchmarkRunSeries? RunSeries { get; set; }

    /// <summary>
    /// 1-based position within <see cref="RunSeriesId"/>. Null for a standalone run. Member 1 is the
    /// run whose instrument hashes the series records for its resume guard.
    /// </summary>
    public int? RunSeriesIndex { get; set; }

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

    /// <summary>
    /// Questions the model failed to answer: it ended its turn normally and produced no text. Scored 0
    /// from scoring method 10; zero on every earlier run, where such an answer was excluded from the
    /// index instead. Not the complement of <see cref="AnsweredQuestionCount"/> — a provider error and
    /// a question that never ran are neither answered nor unanswered in this sense.
    /// </summary>
    public int UnansweredQuestionCount { get; set; }

    public int TotalQuestionCount { get; set; }

    public int DegradedAnswerCount { get; set; }

    public int ToolStarvedAnswerCount { get; set; }

    // Answers whose validity is compromised beyond recovery (empty or truncated).
    // Disjoint from RecoveredAnswerCount and ToolStarvedAnswerCount.
    public int TransportDefectAnswerCount { get; set; }

    /// <summary>
    /// Answers the provider never delivered: the request ended in a provider error or the answer
    /// failed outright. Such answers are not graded and, while any of them exists, the run
    /// publishes no quality or speed index. Null means not recorded: every run finalised before
    /// harness version 21.
    /// </summary>
    public int? TerminalFailureAnswerCount { get; set; }

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

    /// <summary>Answers carrying <see cref="BenchmarkAnswerFlags.UnevidencedDeduction"/>. Advisory.</summary>
    public int UnevidencedDeductionAnswerCount { get; set; }

    /// <summary>Answers carrying <see cref="BenchmarkAnswerFlags.RefutedClaim"/>. Advisory.</summary>
    public int RefutedClaimAnswerCount { get; set; }

    /// <summary>
    /// Answers carrying <see cref="BenchmarkAnswerFlags.ContestedCriticalError"/>. Advisory.
    /// Zero on every run recorded before harness 19, which never adjudicated a critical-error quote.
    /// </summary>
    public int ContestedCriticalErrorAnswerCount { get; set; }

    /// <summary>
    /// Answers carrying <see cref="BenchmarkAnswerFlags.ContestedAccuracyDeduction"/>. Advisory.
    /// Null means not recorded: every run before harness 20, which never adjudicated an
    /// out-of-rubric accuracy deduction.
    /// </summary>
    public int? ContestedAccuracyDeductionAnswerCount { get; set; }

    /// <summary>Answers carrying <see cref="BenchmarkAnswerFlags.OmissionAsAccuracy"/>. Advisory.</summary>
    public int OmissionAsAccuracyAnswerCount { get; set; }

    /// <summary>
    /// Answers carrying <see cref="BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction"/>. Advisory.
    /// Zero on every run recorded before harness 18, which never looked for the marker.
    /// </summary>
    public int OutOfRubricAccuracyAnswerCount { get; set; }

    /// <summary>
    /// Answers carrying <see cref="BenchmarkAnswerFlags.AnswerFramingOpener"/>. Advisory, and the
    /// text is not removed. Zero on every run recorded before harness 18.
    /// </summary>
    public int AnswerFramingOpenerAnswerCount { get; set; }

    /// <summary>
    /// Answers that consumed 100% of their tool call budget without any tool calls being blocked.
    /// Distinct from budget-exhausted answers where calls were refused.
    /// </summary>
    public int BudgetSaturatedAnswerCount { get; set; }

    /// <summary>Answers for which claim verification was run (at least one claim evaluated).</summary>
    public int ClaimVerifiedAnswerCount { get; set; }

    /// <summary>Total claims evaluated across all answers whose verdict was Supported.</summary>
    public int ClaimsSupportedCount { get; set; }

    /// <summary>Total claims evaluated across all answers whose verdict was Refuted.</summary>
    public int ClaimsRefutedCount { get; set; }

    /// <summary>Total claims evaluated across all answers whose verdict was Indeterminate.</summary>
    public int ClaimsIndeterminateCount { get; set; }

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

    /// <summary>
    /// Standard error of the difficulty-weighted Intelligence Index over the answered items,
    /// with the n/(n-1) correction. Null for runs before harness version 11, or below 3 items.
    /// Item-sampling error (uncertainty from item selection), NOT candidate run-to-run variance.
    /// </summary>
    public double? QualityIndexStandardError { get; set; }

    // How the second-opinion assessor was used, snapshotted from the scoring profile or the
    // start dialog's per-run override. Stored on the run so a report can always say what
    // produced its second verdicts, and what coverage the agreement figures rest on.
    public int SecondOpinionModeUsed { get; set; }

    /// <summary>
    /// Whether the second-opinion assessor was blinded to the first grader's score, critical-error
    /// flag, and comment. Snapshotted from the scoring profile.
    /// </summary>
    public bool SecondOpinionBlindUsed { get; set; }

    // Answers that received a second verdict, and the mean |first - second| quality gap across
    // them. Read together with the mode: a mean delta over 4 of 18 trigger-selected answers is
    // conditioned on the first assessor's own uncertainty and is not an unbiased estimate of
    // grader agreement, while the same figure over 18 of 18 is.
    public int SecondOpinionGradedAnswerCount { get; set; }

    public double? SecondOpinionMeanAbsDelta { get; set; }

    /// <summary>
    /// The mean signed difference (second − first quality score) across second-opinion answers.
    /// A negative value means the second reader graded lower. The absolute delta measures
    /// the magnitude of disagreement; the signed delta measures direction, distinguishing
    /// a noisy grader from a systematically lenient or harsh one.
    /// </summary>
    public double? SecondOpinionMeanSignedDelta { get; set; }

    /// <summary>
    /// Answers where the two readers disagreed on CriticalError.
    /// </summary>
    public int SecondOpinionCriticalErrorSplitCount { get; set; }

    /// <summary>
    /// The number of answers actually graded twice by the deterministic top-up under
    /// <see cref="BenchmarkSecondOpinionMode.FlaggedPlusSample"/> — the achieved sample count,
    /// which may fall short of <see cref="BenchmarkScoringProfile.SecondOpinionMinimumSample"/>
    /// when fewer answers exist than the target. Zero under every other mode: this column is
    /// meaningless outside <see cref="BenchmarkSecondOpinionMode.FlaggedPlusSample"/> and a report
    /// must not read it under a different mode.
    /// </summary>
    public int SecondOpinionSampleCountUsed { get; set; }

    /// <summary>
    /// Canonical JSON representation of the candidate prompt options used to build the system prompt.
    /// </summary>
    public string? CandidatePromptOptionsJson { get; set; }

    /// <summary>
    /// The system prompt builder that produced the candidate prompt (e.g. "ChatService.BuildSystemPrompt").
    /// </summary>
    [MaxLength(256)]
    public string? CandidatePromptSourceUsed { get; set; }

    /// <summary>
    /// SHA-256 of the exact candidate system prompt string (lower-case hex).
    /// </summary>
    [MaxLength(64)]
    public string? CandidateSystemPromptSha256 { get; set; }

    /// <summary>
    /// Full candidate system prompt text stored when Benchmark:StoreSystemPromptText is enabled.
    /// </summary>
    public string? CandidateSystemPromptText { get; set; }

    /// <summary>
    /// SHA-256 over sorted relative path and file SHA-256 pairs under ToolGuides.
    /// </summary>
    [MaxLength(64)]
    public string? ToolGuidesSha256 { get; set; }

    /// <summary>
    /// Git commit HEAD SHA of the knowledge base repository at run time.
    /// </summary>
    [MaxLength(40)]
    public string? KnowledgeBaseHeadSha { get; set; }

    /// <summary>
    /// Git commit HEAD SHA of the GnollHack wiki repository at run time, the corpus configured
    /// under the <c>WikiPath</c> key.
    /// <b>Null means "not recorded", never "no corpus"</b>: the head was either never captured or
    /// could not be resolved, which says nothing about whether the wiki was there.
    /// </summary>
    [MaxLength(40)]
    public string? WikiHeadSha { get; set; }

    /// <summary>
    /// Git commit HEAD SHA of the GnollHack source code repository at run time, the corpus
    /// configured under the <c>SourceCodePath</c> key.
    /// <b>Null means "not recorded", never "no corpus"</b>, on the same reading as
    /// <see cref="WikiHeadSha"/>.
    /// </summary>
    [MaxLength(40)]
    public string? SourceCodeHeadSha { get; set; }

    /// <summary>
    /// The instrument a failed-question re-run executed under, recorded separately so the five
    /// fingerprints above keep describing the instrument the run's other answers were produced
    /// under. Those five are the only record that the prompt did not move between two runs, so a
    /// re-run must not overwrite them: it would falsify the provenance of every answer it did not
    /// touch. Null on every run that was never re-run.
    /// </summary>
    [MaxLength(64)]
    public string? RerunCandidateSystemPromptSha256 { get; set; }

    /// <summary>
    /// The tool-guide fingerprint the re-run executed under, on the same reading as
    /// <see cref="RerunCandidateSystemPromptSha256"/>.
    /// </summary>
    [MaxLength(64)]
    public string? RerunToolGuidesSha256 { get; set; }

    /// <summary>
    /// When the most recent failed-question re-run began. <see cref="CompletedAtUtc"/> and
    /// <see cref="TotalDurationMs"/> stay the original execution's, which is the run's elapsed wall
    /// time; a re-run started hours later would otherwise absorb the interval into it.
    /// </summary>
    public DateTime? RerunStartedAtUtc { get; set; }

    /// <summary>When the most recent failed-question re-run finished.</summary>
    public DateTime? RerunCompletedAtUtc { get; set; }

    // Total wall-clock time spent executing tool batches across the run. Subtracting this
    // from TotalAnswerDurationMs gives the model-attributable time that speed is scored on.
    public long? ToolOverheadMs { get; set; }

    public int? MaxToolCallsPerQuestionUsed { get; set; }

    /// <summary>
    /// The sequential tool-round cap that applied to this run, as canonical JSON over every
    /// difficulty band: <c>{"Simple":n,"Intermediate":n,"Advanced":n}</c>, bands in that order.
    /// The whole band table rather than one resolved figure, because a question takes its caps from
    /// the band its assessed difficulty falls in and one run spans several bands. Null for runs
    /// recorded before these caps were snapshotted; for those the harness does not know what
    /// applied.
    /// </summary>
    [MaxLength(256)]
    public string? ToolIterationCapsJson { get; set; }

    /// <summary>
    /// The total provider-request cap that applied to this run, per difficulty band, in the same
    /// canonical JSON shape as <see cref="ToolIterationCapsJson"/>.
    /// </summary>
    [MaxLength(256)]
    public string? TotalModelCallCapsJson { get; set; }

    /// <summary>
    /// The per-question wall-clock timeout in seconds that applied to this run, per difficulty
    /// band, in the same canonical JSON shape as <see cref="ToolIterationCapsJson"/>.
    /// </summary>
    [MaxLength(256)]
    public string? QuestionTimeoutSecondsJson { get; set; }

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

    /// <summary>
    /// Run-level sums of the per-answer LongContext* columns — the subset of the totals above that was
    /// billed at the model's long-context rate. Zero for a flat-rate model and for every run recorded
    /// before tiered pricing existed. Denormalized exactly like the Total*Tokens columns beside them.
    /// </summary>
    public long TotalLongContextInputTokens { get; set; }
    public long TotalLongContextOutputTokens { get; set; }
    public long TotalLongContextCacheReadTokens { get; set; }
    public long TotalLongContextCacheCreationTokens { get; set; }

    public long TotalDurationMs { get; set; }

    // Assessor-side usage, deliberately kept apart from the candidate totals above. Those
    // measure the model under test and must not absorb the grader's consumption; together the
    // two are what the run actually cost, which was previously not recorded anywhere.
    public long TotalAssessmentInputTokens { get; set; }
    public long TotalAssessmentOutputTokens { get; set; }
    public long TotalAssessmentCacheReadTokens { get; set; }
    public long TotalAssessmentCacheCreationTokens { get; set; }
    public long TotalAssessmentDurationMs { get; set; }

    // Second-opinion-side usage, kept apart from candidate, assessor and claim-verifier totals.
    // The second opinion is a separate assessor call from the primary assessment above and is
    // never pooled into TotalAssessment* here.
    public long TotalSecondOpinionInputTokens { get; set; }
    public long TotalSecondOpinionOutputTokens { get; set; }
    public long TotalSecondOpinionCacheReadTokens { get; set; }
    public long TotalSecondOpinionCacheCreationTokens { get; set; }
    public long TotalSecondOpinionDurationMs { get; set; }

    // Claim-verifier-side usage, kept apart from candidate and assessor totals above.
    public long TotalClaimVerificationInputTokens { get; set; }
    public long TotalClaimVerificationOutputTokens { get; set; }
    public long TotalClaimVerificationCacheReadTokens { get; set; }
    public long TotalClaimVerificationCacheCreationTokens { get; set; }
    public long TotalClaimVerificationDurationMs { get; set; }

    /// <summary>
    /// Final-synthesis usage: the call that merges the assessor's and second opinion's verdicts
    /// into the run's published result. Recorded only here, at run level — there is no per-answer
    /// counterpart, because <c>BenchmarkRunFinalizer.ApplyTotals</c> recomputes the assessor totals
    /// above from the answer rows on every call, so a synthesis figure folded into
    /// <see cref="TotalAssessmentInputTokens"/> or its siblings would be erased by the next
    /// re-score.
    /// </summary>
    public long TotalSynthesisInputTokens { get; set; }
    public long TotalSynthesisOutputTokens { get; set; }
    public long TotalSynthesisCacheReadTokens { get; set; }
    public long TotalSynthesisCacheCreationTokens { get; set; }
    public long TotalSynthesisDurationMs { get; set; }

    /// <summary>
    /// Resolved per-million prices for every role, captured when the run started. A report
    /// regenerated later prices the run at what it actually cost, not at today's rates. Null
    /// for runs that predate this column; those are priced live and the report says so.
    /// </summary>
    public string? PricingSnapshotJson { get; set; }

    public List<BenchmarkRunAnswer> Answers { get; set; } = new();
}
