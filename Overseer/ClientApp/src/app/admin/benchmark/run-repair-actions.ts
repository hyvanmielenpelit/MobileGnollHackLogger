import { BenchmarkRunAnswerDto, BenchmarkRunDetailDto } from '../../services/admin-benchmark.service';
import { formatStatus, isAbortedRun } from './benchmark-run-format';

/**
 * Whether a run repair action is offered, and why it cannot run now. A visible action with a reason is
 * shown `aria-disabled` with that reason; the gates mirror the server's refusals (AdminBenchmarkController
 * and BenchmarkService), which stay authoritative.
 */
export interface RepairActionGate {
  visible: boolean;
  disabledReason: string | null;
}

/** BenchmarkService.AbortedRunRefusal, in sentence form. */
export const REPAIR_REASON_ABORTED = 'The run stopped before finishing its suite.';

/** BenchmarkService.ScoringMethodRefusal, in sentence form. Re-score is the one action it allows. */
export const REPAIR_REASON_SCORING_METHOD = 'Scored under an older scoring method; re-score it first.';

/** The run is executing, or a repair of it has been sent and not yet seen running. */
export const REPAIR_REASON_BUSY = 'A retry is already running on this run.';

/** RescoreRunAsync: a run graded before anchored dimensional levels has nothing to recompute. */
export const REPAIR_REASON_NO_LEVELS = 'The run has no dimensional level ratings to re-score.';

/** RerunAnswer: the stored question text is empty. */
export const REPAIR_REASON_EMPTY_QUESTION = 'The question text is empty.';

/** The run statuses after which the failed questions of a run can be re-run. */
const FAILED_QUESTION_RERUN_STATUSES = ['Failed', 'Canceled', 'CompletedWithErrors'];

function answerStatus(status: string | number): string {
  if (status === 1 || status === 'Ok') return 'Ok';
  if (status === 2 || status === 'ProviderError') return 'ProviderError';
  if (status === 3 || status === 'Failed') return 'Failed';
  if (status === 4 || status === 'Skipped') return 'Skipped';
  if (status === 5 || status === 'EmptyAnswer') return 'EmptyAnswer';
  if (status === 6 || status === 'Canceled') return 'Canceled';
  return String(status);
}

/** An absent assessment status reads Scored, as the run report reads it. */
function assessmentStatus(status: string | number | null | undefined): string {
  if (status === 1 || status === 'Pending') return 'Pending';
  if (status === 2 || status === 'Assessing') return 'Assessing';
  if (status === 3 || status === 'Scored') return 'Scored';
  if (status === 4 || status === 'Failed') return 'Failed';
  return status != null ? String(status) : 'Scored';
}

/** An answer the failed-question re-run executes again (BenchmarkRunFinalizer.NeedsReExecution). */
export function isRepairableFailedAnswer(answer: BenchmarkRunAnswerDto): boolean {
  const s = answerStatus(answer.status);
  return s === 'ProviderError' || s === 'Failed' || s === 'Skipped' || s === 'EmptyAnswer' || s === 'Canceled';
}

/** An answer whose verdict, or in a panel run member B's, is not Scored. */
function isUnscored(run: BenchmarkRunDetailDto, answer: BenchmarkRunAnswerDto): boolean {
  const own = assessmentStatus(answer.assessmentStatus);
  if (own === 'Failed' || own === 'Pending' || own === 'Assessing') return true;
  return !!run.isPanelRun && answer.coAssessmentStatus != null && assessmentStatus(answer.coAssessmentStatus) !== 'Scored';
}

function hasDimensionalLevels(answer: BenchmarkRunAnswerDto): boolean {
  return answer.accuracyLevel != null && answer.completenessLevel != null &&
    answer.concisenessLevel != null && answer.readabilityLevel != null;
}

/** An older server omits the flag; that run is taken as graded under the current method. */
function isOlderScoringMethod(run: BenchmarkRunDetailDto): boolean {
  return run.isCurrentScoringMethod === false;
}

/** The refusals every repair but Re-score shares, in the server's order. */
function sharedReason(run: BenchmarkRunDetailDto, busy: boolean): string | null {
  if (busy) return REPAIR_REASON_BUSY;
  if (isAbortedRun(run)) return REPAIR_REASON_ABORTED;
  if (isOlderScoringMethod(run)) return REPAIR_REASON_SCORING_METHOD;
  return null;
}

function gate(visible: boolean, disabledReason: string | null): RepairActionGate {
  return { visible, disabledReason: visible ? disabledReason : null };
}

// --- Run-level actions ---

/** Re-score run: always listed; allowed under an older scoring method, which it brings forward. */
export function rescore(run: BenchmarkRunDetailDto, busy: boolean): RepairActionGate {
  if (busy) return gate(true, REPAIR_REASON_BUSY);
  if (isAbortedRun(run)) return gate(true, REPAIR_REASON_ABORTED);
  if (!(run.answers ?? []).some(hasDimensionalLevels)) return gate(true, REPAIR_REASON_NO_LEVELS);
  return gate(true, null);
}

/** Re-run final synthesis: listed while the run has answers to synthesize. */
export function rerunSynthesis(run: BenchmarkRunDetailDto, busy: boolean): RepairActionGate {
  return gate((run.answers ?? []).length > 0, sharedReason(run, busy));
}

/** Retry failed assessments: listed while an answer's verdict is not Scored. */
export function retryAssessments(run: BenchmarkRunDetailDto, busy: boolean): RepairActionGate {
  return gate((run.answers ?? []).some(a => isUnscored(run, a)), sharedReason(run, busy));
}

/** Retry claim verification: listed while an answer's claim verification failed. */
export function retryClaimVerification(run: BenchmarkRunDetailDto, busy: boolean): RepairActionGate {
  const visible = (run.answers ?? []).some(a => !!a.claimVerificationError && a.claimVerificationError.trim().length > 0);
  return gate(visible, sharedReason(run, busy));
}

/** Re-run failed questions: listed while a run that did not finish cleanly has failed answers. */
export function rerunFailedQuestions(run: BenchmarkRunDetailDto, busy: boolean): RepairActionGate {
  const visible = FAILED_QUESTION_RERUN_STATUSES.includes(formatStatus(run.status)) &&
    (run.answers ?? []).some(isRepairableFailedAnswer);
  return gate(visible, sharedReason(run, busy));
}

// --- Per-question actions ---

/** Re-run question: asks the candidate again and re-grades the answer. */
export function rerunQuestion(run: BenchmarkRunDetailDto, answer: BenchmarkRunAnswerDto, busy: boolean): RepairActionGate {
  const reason = sharedReason(run, busy) ??
    (answer.questionText?.trim() ? null : REPAIR_REASON_EMPTY_QUESTION);
  return gate(true, reason);
}

/** Re-assess question: grades the stored answer again, replacing its verdict. */
export function reassess(run: BenchmarkRunDetailDto, _answer: BenchmarkRunAnswerDto, busy: boolean): RepairActionGate {
  return gate(true, sharedReason(run, busy));
}

/**
 * Try another assessor: records a second verdict and changes no score. In a panel run the slot it
 * writes holds the reference reader's verdict, so it is listed only where that slot is empty.
 */
export function trialReassess(run: BenchmarkRunDetailDto, answer: BenchmarkRunAnswerDto, busy: boolean): RepairActionGate {
  const visible = !run.isPanelRun || answer.secondOpinionQualityScore == null;
  return gate(visible, sharedReason(run, busy));
}

// --- Run state ---

/** A re-run of the run's answers is executing, as opposed to the run's own first execution. */
export function isRerunInProgress(run: BenchmarkRunDetailDto): boolean {
  return formatStatus(run.status) === 'Running' && !!run.rerunStartedAtUtc;
}
