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

    /// <summary>
    /// The suite question this answer was produced for. Null for a historical answer the
    /// backfill could not match unambiguously, and null once the question is deleted —
    /// <c>DeleteBehavior.SetNull</c>, because a run is a historical record and removing a
    /// question from a suite is suite maintenance, not history revision.
    ///
    /// Before this column existed, an answer was tied to its question by
    /// <see cref="OrderIndex"/> alone, and reordering a suite's questions rewrote those indexes
    /// while touching no stored answer — so every earlier run then displayed its answers against
    /// the wrong questions, silently. Everything downstream treats null as "unlinked" and
    /// excludes it from statistics rather than guessing: a wrong link would corrupt every figure
    /// built on it, and unlike a missing one, invisibly.
    /// </summary>
    public long? BenchmarkQuestionId { get; set; }
    public BenchmarkQuestion? BenchmarkQuestion { get; set; }

    /// <summary>
    /// The question this answer was produced for, retained after the question row is gone.
    /// Deleting a suite cascades its questions and nulls the foreign key above; the item-revision
    /// comparability signature is derived from this instead, so it survives suite maintenance.
    /// Not a foreign key, deliberately, and not a substitute for
    /// <see cref="BenchmarkQuestionId"/>: a null there still means "unlinked" everywhere
    /// downstream, and item analysis still excludes it.
    /// </summary>
    public long? BenchmarkQuestionIdUsed { get; set; }

    /// <summary>
    /// <see cref="BenchmarkQuestion.ItemRevision"/> as it stood when this answer was produced.
    /// Null for answers that predate the column; item analysis groups on it, so a null is
    /// excluded rather than assumed to be revision 1.
    /// </summary>
    public int? ItemRevisionUsed { get; set; }

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
    public int? AssessmentCacheReadTokens { get; set; }
    public int? AssessmentCacheCreationTokens { get; set; }
    public long? AssessmentDurationMs { get; set; }

    /// <summary>
    /// The assessor's own justification, as JSON: for each of accuracy and completeness, the
    /// rubric point a deduction rests on, or an explicit statement that it rests on the
    /// assessor's own knowledge instead. That distinction is the thing a disputed score turns
    /// on, and before this column nothing recorded it.
    /// </summary>
    public string? AssessmentEvidenceJson { get; set; }

    /// <summary>
    /// The assessor recorded a rubric point that lies outside what the question asked, under the
    /// <c>OUT-OF-SCOPE:</c> marker scoring method v8 requires, and did not deduct for it.
    ///
    /// Not a defect: a measurement. Completeness has been this suite's weakest dimension on every
    /// run, roughly ten points below Accuracy, and that gap is part model and part instrument —
    /// a rubric enumerating more ground truth than its question asked for. Counting the points the
    /// assessor itself placed out of scope is what separates the two halves, and without the count
    /// no Completeness prompt change could be justified against the gap.
    ///
    /// Non-nullable, unlike <see cref="NarrationBlockCount"/> and <see cref="UnverifiedClaimCount"/>:
    /// an answer graded before scoring method v8 reads false because the marker did not exist, and
    /// <c>BenchmarkRun.ScoringMethodVersion</c> is what tells those runs apart from a v8 run whose
    /// assessor genuinely found nothing out of scope. A nullable column would duplicate a fact the
    /// run already carries.
    /// </summary>
    public bool CompletenessOutOfScope { get; set; }

    /// <summary>
    /// The assessor recorded a rubric format suggestion the answer did not follow, under the
    /// <c>FORM:</c> marker scoring method v9 requires, and did not deduct Readability for it.
    ///
    /// Not a defect: a measurement, and the Readability counterpart of
    /// <see cref="CompletenessOutOfScope"/>. Readability's level anchors grade how an answer
    /// reads; a rubric FORM criterion states how its author would have laid the material out, and
    /// the suite grades a chat prompt that asks for concise prose — so a rubric asking for a table
    /// docked the candidate for obeying its own system prompt. Counting the suggestions the
    /// assessor set aside is what tells the rubric's share of a Readability shortfall from the
    /// answer's.
    ///
    /// Non-nullable for the same reason as <see cref="CompletenessOutOfScope"/>: an answer graded
    /// before scoring method v9 reads false because the marker did not exist, and
    /// <c>BenchmarkRun.ScoringMethodVersion</c> is what tells those runs apart from a v9 run whose
    /// assessor found no format suggestion to set aside.
    /// </summary>
    public bool ReadabilityFormOnly { get; set; }

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

    public int? SecondOpinionInputTokens { get; set; }
    public int? SecondOpinionOutputTokens { get; set; }
    public int? SecondOpinionCacheReadTokens { get; set; }
    public int? SecondOpinionCacheCreationTokens { get; set; }
    public long? SecondOpinionDurationMs { get; set; }

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

    /// <summary>
    /// What selected this answer for a second verdict: <c>CriticalError</c>,
    /// <c>ContestedVerdict</c>, <c>UnverifiedClaims</c>, <c>BelowThreshold</c>, <c>Outlier</c>,
    /// <c>All</c>, or <c>Manual</c> for an operator's trial re-assessment. The report claimed to
    /// say what produced its second verdicts and previously could not.
    ///
    /// <c>Manual</c> verdicts are excluded from the run's agreement aggregates: those measure the
    /// run's own two graders, and a third model an operator chose by hand is not part of that
    /// measurement.
    /// </summary>
    [MaxLength(64)]
    public string? SecondOpinionTrigger { get; set; }

    /// <summary>
    /// Why a second verdict was requested but never obtained. Distinguishes "no answer met a
    /// trigger" from "a trigger fired and the call failed" — the report previously rendered
    /// both as the former. Advisory; the first verdict stands and no score changes.
    /// </summary>
    [MaxLength(2048)]
    public string? SecondOpinionError { get; set; }

    /// <summary>
    /// Claims the assessor could neither confirm nor refute against the rubric, verbatim from
    /// the answer, as a JSON array. Under scoring method v6 these do not reduce Accuracy — the
    /// assessor declares them instead of deducting for them.
    ///
    /// Their value is cross-run: a claim several unrelated model families make independently is
    /// evidence the rubric is incomplete, while one a single model makes once is evidence that
    /// model invented it. Neither reading is available from one run.
    /// </summary>
    public string? UnverifiedClaimsJson { get; set; }

    /// <summary>
    /// How many unverified claims were recorded. Nullable because runs before harness version 7
    /// never recorded any: <b>null means "not recorded", never zero</b>, following the
    /// <see cref="NarrationBlockCount"/> precedent. Reporting a null as 0 would assert that an
    /// assessor found none when it was never asked.
    /// </summary>
    public int? UnverifiedClaimCount { get; set; }

    /// <summary>
    /// Per-claim verdicts from the claim verifier, as a JSON array of
    /// { claim, verdict, citation, basis }. Advisory: nothing here is read by any scoring path.
    /// </summary>
    public string? ClaimVerificationJson { get; set; }

    public int? ClaimsSupportedCount { get; set; }
    public int? ClaimsRefutedCount { get; set; }
    public int? ClaimsIndeterminateCount { get; set; }

    [MaxLength(256)]
    public string? ClaimVerificationByModelDisplayNameUsed { get; set; }

    public int? ClaimVerificationInputTokens { get; set; }
    public int? ClaimVerificationOutputTokens { get; set; }
    public int? ClaimVerificationCacheReadTokens { get; set; }
    public int? ClaimVerificationCacheCreationTokens { get; set; }
    public long? ClaimVerificationDurationMs { get; set; }
    public int? ClaimVerificationToolCallCount { get; set; }

    /// <summary>Why verification did not complete for this answer. Never fails the run.</summary>
    [MaxLength(1024)]
    public string? ClaimVerificationError { get; set; }

    /// <summary>
    /// The raw response text from the claim verifier when parsing fails, retained for recovery and diagnosis.
    /// </summary>
    public string? ClaimVerificationRawText { get; set; }

    /// <summary>
    /// When this answer's verdict was replaced by the re-assess action, and by which model. A
    /// trial re-assessment sets neither: it does not replace the verdict.
    ///
    /// Without these, a published Intelligence Index could change after publication while the
    /// run's assessor snapshot still named the model that graded everything — a subsystem that
    /// snapshots every model it touches had no record of the one case where the grader changed.
    /// </summary>
    public DateTime? ReassessedAtUtc { get; set; }

    [MaxLength(256)]
    public string? ReassessedByModelDisplayNameUsed { get; set; }

    /// <summary>
    /// The quality score this answer carried before the <b>first</b> re-assessment. A later
    /// re-assessment increments <see cref="ReassessmentCount"/> and leaves this alone, so the
    /// original verdict is always recoverable rather than the most recent one it displaced.
    /// </summary>
    public int? PreviousQualityScore { get; set; }

    public int ReassessmentCount { get; set; }

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

    /// <summary>
    /// The subset of the four token columns above that came from model calls large enough to bill at the
    /// model's long-context rate. Not additional tokens — a portion of the same ones. Null for answers
    /// recorded before tiered pricing existed, and zero for a flat-rate model. Persisted because benchmark
    /// cost is recomputed from stored totals rather than snapshotted, so without these the surcharge could
    /// not be reproduced after the run.
    /// </summary>
    public int? LongContextInputTokens { get; set; }
    public int? LongContextOutputTokens { get; set; }
    public int? LongContextCacheReadTokens { get; set; }
    public int? LongContextCacheCreationTokens { get; set; }

    public int? ModelCallCount { get; set; }
    public int? ToolCallCount { get; set; }
    public bool ToolBudgetExhausted { get; set; }

    /// <summary>
    /// How many tool calls were blocked because the per-question tool budget was exhausted.
    /// Nullable because runs before harness version 11 never recorded it: null means "not recorded",
    /// never zero. Reports fall back to ToolCallCount - executed inference for pre-v11 runs.
    /// </summary>
    public int? ToolCallsBlocked { get; set; }

    // The per-question tool call budget that actually applied to this answer. The budget is
    // resolved per difficulty band, so it differs between questions in the same run and cannot
    // be read off BenchmarkRun.MaxToolCallsPerQuestionUsed.
    public int? ToolCallBudgetUsed { get; set; }
    [MaxLength(32)]
    public string? TerminationReason { get; set; }

    /// <summary>
    /// The provider's verbatim finish reason for the final model call. Null for answers recorded before
    /// this was captured, which is "not recorded" and never "stopped normally". Read with
    /// <see cref="TerminationReason"/>: the pair is what separates an empty answer the model produced
    /// from one a transport defect destroyed, and therefore what scoring method 10 scores 0.
    /// </summary>
    [MaxLength(64)]
    public string? ProviderFinishReason { get; set; }

    public int AnswerFlags { get; set; }
}
