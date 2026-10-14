import { Observable, catchError, forkJoin, map, of } from 'rxjs';

import type {
  AdminBenchmarkService,
  BenchmarkModelBatchMemberDto,
  BenchmarkModelBatchMemberStatus,
  BenchmarkModelBatchResumeMode,
  BenchmarkModelBatchResumeOptionDto,
  BenchmarkModelBatchRunDto,
  BenchmarkModelBatchStopReason,
  BenchmarkModelBatchTargetKind
} from '../../../services/admin-benchmark.service';
import { elapsedMsBetween } from '../../../utils/date.util';
import { BATTERY_REPAIRING_LABEL, formatCost } from '../batteries/battery.models';
import { formatCostAmount, formatElapsed, formatModelTime } from '../benchmark-run-format';
import { MAX_COMPARISON_SOURCES } from '../model-comparison/comparison-source-picker.component';
import { RunFactBadge, runFactBadges } from '../run-report-frame/run-facts';
import { NOT_RECORDED_TEXT, formatInteger } from '../shared/provider-availability/provider-availability';
import type { ComparisonWizardPreset } from '../state/benchmark-shell-bridge.service';

/** Pending, Running and WaitingForCap: the orchestrator may still launch a member. */
export function isLiveModelBatchStatus(status: string | null | undefined): boolean {
  return status === 'Pending' || status === 'Running' || status === 'WaitingForCap';
}

/** Completed, CompletedWithErrors, Cancelled and Failed: nothing more runs and nothing resumes it. */
export function isFinalModelBatchStatus(status: string | null | undefined): boolean {
  return status === 'Completed' || status === 'CompletedWithErrors' || status === 'Cancelled' || status === 'Failed';
}

/** A batch's status in words. */
export function modelBatchStatusLabel(status: string | null | undefined): string {
  switch (status) {
    case 'WaitingForCap': return 'Waiting for run cap';
    case 'CompletedWithErrors': return 'Completed with errors';
    case 'Cancelled': return 'Canceled';
    default: return status ?? '';
  }
}

/** Why a batch stopped, as a clause: *the instrument changed*. */
export const MODEL_BATCH_STOP_REASON_LABELS: Readonly<Record<BenchmarkModelBatchStopReason, string>> = {
  MemberStopped: 'a model\'s run stopped',
  InstrumentChanged: 'the instrument changed',
  GraderConfigChanged: 'a grader configuration changed',
  RunCapReached: 'the run cap was reached',
  SpendDenied: 'the spend guard refused a run',
  RestartReconciled: 'the server restarted'
};

/** The stop reason as a clause, else the server's wording, else a fallback. */
export function modelBatchStopReasonLabel(batch: Pick<BenchmarkModelBatchRunDto, 'stopReason' | 'stopReasonText'>): string {
  const reason = batch.stopReason;
  if (reason && MODEL_BATCH_STOP_REASON_LABELS[reason]) {
    return MODEL_BATCH_STOP_REASON_LABELS[reason];
  }
  return batch.stopReasonText?.trim() || reason || 'no reason recorded';
}

/** A member's status in words. */
export function modelBatchMemberStatusLabel(status: BenchmarkModelBatchMemberStatus | string): string {
  return status === 'CompletedWithErrors' ? 'Completed with errors' : status;
}

/** A member's status chip classes, from the shared `.job-status-chip` vocabulary. */
export function modelBatchMemberChipClass(status: BenchmarkModelBatchMemberStatus | string): string {
  switch (status) {
    case 'Pending': return 'job-status-chip status-pending';
    case 'Running': return 'job-status-chip status-answering';
    case 'Completed': return 'job-status-chip status-completed';
    case 'CompletedWithErrors':
    case 'Stopped': return 'job-status-chip status-partial';
    case 'Failed': return 'job-status-chip status-failed';
    default: return 'job-status-chip status-canceled';
  }
}

/** A member whose result a comparison can use. */
export function isComparableModelBatchMember(member: BenchmarkModelBatchMemberDto): boolean {
  return member.status === 'Completed' || member.status === 'CompletedWithErrors';
}

/** The members a comparison would take, in run order. */
export function comparableModelBatchMembers(batch: BenchmarkModelBatchRunDto): BenchmarkModelBatchMemberDto[] {
  return batch.members.filter(isComparableModelBatchMember);
}

/** Model Comparison needs two members with a result. */
export function canCompareModelBatch(batch: BenchmarkModelBatchRunDto | null | undefined): boolean {
  return !!batch && comparableModelBatchMembers(batch).length >= 2;
}

/** The reason Open in Model Comparison waits while it is unavailable. */
export const MODEL_BATCH_COMPARE_REASON = 'Two models with a completed result are needed to compare.';

/** A member's model as its badges show it: thinking level, reasoning mode, provider, tier, custom endpoint. */
export function modelBatchMemberBadges(member: BenchmarkModelBatchMemberDto): RunFactBadge[] {
  const model = member.model;
  return runFactBadges({
    name: model.displayName || model.modelId,
    provider: model.provider || null,
    thinkingLevel: model.thinkingLevel ?? null,
    reasoningMode: model.reasoningMode ?? null,
    serviceTier: model.serviceTier ?? null,
    customEndpoint: !!model.endpoint && model.endpoint !== 'official'
  });
}

/** A member's model name: the display name, else the model id. */
export function modelBatchMemberName(member: BenchmarkModelBatchMemberDto | null | undefined): string {
  return member ? (member.model.displayName || member.model.modelId || `Model ${member.orderIndex + 1}`) : '';
}

/** *Random order, seed 4711* or *As listed*. */
export function modelBatchOrderText(batch: Pick<BenchmarkModelBatchRunDto, 'order' | 'orderSeed'>): string {
  if (batch.order === 'AsListed') return 'As listed';
  return batch.orderSeed != null ? `Random order, seed ${batch.orderSeed}` : 'Random order';
}

/** *3 models × 2 suites × 2 runs*, or *3 models × 2 runs* on a suite. */
export function modelBatchPlanText(batch: BenchmarkModelBatchRunDto): string {
  const models = batch.requestedMemberCount || batch.members.length;
  const runs = `${batch.runsPerModel} ${batch.runsPerModel === 1 ? 'run' : 'runs'}`;
  const modelText = `${models} ${models === 1 ? 'model' : 'models'}`;
  if (batch.targetKind === 'Battery') {
    const suites = batch.suiteNames.length;
    return `${modelText} × ${suites} ${suites === 1 ? 'suite' : 'suites'} × ${runs}`;
  }
  return `${modelText} × ${runs}`;
}

/** The battery revision as a suffix, *, revision 3*, or empty for a suite. */
export function modelBatchTargetText(batch: BenchmarkModelBatchRunDto): string {
  const name = batch.targetName || (batch.targetKind === 'Battery' ? 'Battery' : batch.suiteNames[0] || 'Suite');
  return batch.targetKind === 'Battery' && batch.batteryRevision != null ? `${name}, revision ${batch.batteryRevision}` : name;
}

/** The members the batch has finished with: completed, failed or skipped. */
export function modelBatchFinishedMemberCount(batch: BenchmarkModelBatchRunDto): number {
  return batch.completedMemberCount + batch.failedMemberCount + batch.skippedMemberCount;
}

/** The runs the batch plans: each member's steps. */
export function modelBatchPlannedRunCount(batch: BenchmarkModelBatchRunDto): number {
  return batch.members.reduce((sum, member) => sum + Math.max(0, member.stepCount), 0);
}

/** The runs the batch has launched so far. */
export function modelBatchLaunchedRunCount(batch: BenchmarkModelBatchRunDto): number {
  return batch.members.reduce((sum, member) => sum + (member.runIds?.length ?? 0), 0);
}

/** The member running, else the next to start; null when neither applies. */
export function modelBatchCurrentMember(batch: BenchmarkModelBatchRunDto | null | undefined): BenchmarkModelBatchMemberDto | null {
  const index = batch?.currentMemberIndex;
  return batch && index != null ? batch.members[index] ?? null : null;
}

/** A resume button's label by mode. */
export function modelBatchResumeLabel(option: BenchmarkModelBatchResumeOptionDto, batch: BenchmarkModelBatchRunDto): string {
  switch (option.mode) {
    case 'Continue': {
      const reason = batch.stopReasonText?.trim() || (batch.stopReason ? MODEL_BATCH_STOP_REASON_LABELS[batch.stopReason] : '');
      return reason ? `Continue — ${reason}` : 'Continue';
    }
    case 'SkipCurrent': return 'Skip this model';
    case 'AcceptInstrumentChange': return 'Continue — accept the change';
    case 'RerunUnderCurrentInstrument': return 'Re-run under current instrument';
    default: return option.label || option.mode;
  }
}

/** The resume modes in the order the footer shows them. */
export const MODEL_BATCH_RESUME_ORDER: readonly BenchmarkModelBatchResumeMode[] = [
  'RerunUnderCurrentInstrument', 'SkipCurrent', 'AcceptInstrumentChange', 'Continue'
];

/**
 * The comparison preset of a batch's members with a result: a battery's battery runs, a suite's runs
 * (one run per model) or a series' analysis groups (two or more runs per model). At most
 * `MAX_COMPARISON_SOURCES`.
 */
export function modelBatchComparisonPreset(
  batch: BenchmarkModelBatchRunDto,
  groupIdBySeriesId: ReadonlyMap<number, number | null> = new Map()
): ComparisonWizardPreset {
  const members = comparableModelBatchMembers(batch);
  if (batch.targetKind === 'Battery') {
    const batteryRunIds = members.map(m => m.batteryRunId).filter((id): id is number => id != null);
    return { batteryRunIds: batteryRunIds.slice(0, MAX_COMPARISON_SOURCES) };
  }
  const runIds: number[] = [];
  const groupIds: number[] = [];
  for (const member of members) {
    const groupId = member.seriesId != null ? groupIdBySeriesId.get(member.seriesId) ?? null : null;
    if (groupId != null) {
      groupIds.push(groupId);
    } else if (member.runId != null) {
      runIds.push(member.runId);
    } else {
      // A series without its group: its runs, so the comparison still sees the model.
      runIds.push(...(member.runIds ?? []));
    }
  }
  const cappedRuns = runIds.slice(0, MAX_COMPARISON_SOURCES);
  const cappedGroups = groupIds.slice(0, Math.max(0, MAX_COMPARISON_SOURCES - cappedRuns.length));
  return { batteryRunIds: [], runIds: cappedRuns, groupIds: cappedGroups };
}

/**
 * The comparison preset, with each series member's analysis group read from its series. A series
 * that cannot be read, or has no group yet, contributes its runs instead.
 */
export function resolveModelBatchComparisonPreset(
  batch: BenchmarkModelBatchRunDto,
  service: Pick<AdminBenchmarkService, 'getRunSeries'>
): Observable<ComparisonWizardPreset> {
  const seriesIds = comparableModelBatchMembers(batch)
    .map(m => m.seriesId)
    .filter((id): id is number => id != null);
  if (batch.targetKind === 'Battery' || seriesIds.length === 0) {
    return of(modelBatchComparisonPreset(batch));
  }
  return forkJoin(seriesIds.map(id => service.getRunSeries(id).pipe(
    map(series => [id, series?.autoCreatedGroupId ?? null] as const),
    catchError(() => of([id, null] as const))
  ))).pipe(map(pairs => modelBatchComparisonPreset(batch, new Map(pairs))));
}

/** `model-batch-12-diagnostics.txt`. */
export function modelBatchDiagnosticsFileName(batchId: number): string {
  return `model-batch-${batchId}-diagnostics.txt`;
}

/** The MB-T04 line: the corpora must stay quiet while the batch runs. */
export const MODEL_BATCH_CORPUS_QUIET_TEXT =
  'Do not push the wiki, source or knowledge base until the batch ends; a change stops it.';

/** The batch-owned notice's reason for a gated action in the battery and series progress dialogs. */
export function modelBatchOwnedReasonText(batchId: number, kind: 'battery' | 'series'): string {
  const child = kind === 'battery' ? 'battery run' : 'series';
  return `Model batch #${batchId} controls this ${child}. Cancel, continue or re-run it from the batch.`;
}

/** A member card's chip text while a repair that is not a re-run of failed questions runs on its run. */
export const MODEL_BATCH_REPAIR_LABEL = 'Repair in progress';

/** A member run running in place (a re-run, re-assessment, re-synthesis or retry) while the batch is not live. */
export function isModelBatchMemberRepairing(member: BenchmarkModelBatchMemberDto): boolean {
  return member.repairingRunId != null;
}

/** The first member whose run is being repaired; null when none is. */
export function modelBatchRepairingMember(batch: BenchmarkModelBatchRunDto | null | undefined): BenchmarkModelBatchMemberDto | null {
  return batch?.members.find(isModelBatchMemberRepairing) ?? null;
}

/** A repairing member's chip text: *Re-run in progress* for a re-run of failed questions, else *Repair in progress*. */
export function modelBatchMemberRepairLabel(member: BenchmarkModelBatchMemberDto): string | null {
  if (!isModelBatchMemberRepairing(member)) return null;
  return member.rerunStartedAtUtc ? BATTERY_REPAIRING_LABEL : MODEL_BATCH_REPAIR_LABEL;
}

/** *Re-running 2 failed questions*, or *Re-running failed questions* without a count; null for any other repair. */
export function modelBatchMemberRepairText(member: BenchmarkModelBatchMemberDto): string | null {
  if (!isModelBatchMemberRepairing(member) || !member.rerunStartedAtUtc) return null;
  const count = member.rerunQuestionCount;
  if (count == null || !Number.isFinite(count) || count <= 0) return 'Re-running failed questions';
  return `Re-running ${count} failed ${count === 1 ? 'question' : 'questions'}`;
}

/** One figure of a finished member's card. */
export interface ModelBatchMemberFigure {
  readonly key: string;
  readonly label: string;
  readonly value: string;
  /** A count that needs attention. */
  readonly tone?: 'warning';
}

function isFiniteNumber(value: number | null | undefined): value is number {
  return value != null && Number.isFinite(value);
}

/**
 * A finished member's figures in card order, each omitted when missing: *Speed Index*, *Duration*,
 * *Median model time*, *TTFT P50*, *Cost / question*, *Total cost*, *Refuted claims* (whenever there is
 * a result), *Flagged answers* and *Failed answers* above zero, *Provider errors* (the attributed
 * provider-class attempts, or *Not recorded*) whenever there is a result, and *Re-runs* when recorded.
 */
export function modelBatchMemberFigures(member: BenchmarkModelBatchMemberDto): ModelBatchMemberFigure[] {
  const result = member.result ?? null;
  const figures: ModelBatchMemberFigure[] = [];
  const speedIndex = result?.speedIndex;
  if (isFiniteNumber(speedIndex)) {
    figures.push({ key: 'speed-index', label: 'Speed Index', value: String(Math.round(speedIndex)) });
  }
  if (member.startedAtUtc && member.completedAtUtc) {
    figures.push({ key: 'duration', label: 'Duration', value: formatElapsed(elapsedMsBetween(member.startedAtUtc, member.completedAtUtc)) });
  }
  const medianModelTime = result?.medianModelTimeMs;
  if (isFiniteNumber(medianModelTime)) {
    figures.push({ key: 'median-model-time', label: 'Median model time', value: formatModelTime(medianModelTime) });
  }
  const ttft = result?.ttftP50Ms;
  if (isFiniteNumber(ttft)) {
    figures.push({ key: 'ttft-p50', label: 'TTFT P50', value: formatModelTime(ttft) });
  }
  const costPerQuestion = result?.candidateCostPerQuestionUsd;
  if (isFiniteNumber(costPerQuestion)) {
    figures.push({ key: 'cost-per-question', label: 'Cost / question', value: formatCostAmount(costPerQuestion) });
  }
  const totalCost = result?.totalCostUsd;
  if (isFiniteNumber(totalCost)) {
    figures.push({ key: 'total-cost', label: 'Total cost', value: formatCost(totalCost) });
  }
  if (!result) return figures;
  figures.push({ key: 'refuted-claims', label: 'Refuted claims', value: String(result.refutedClaims ?? 0) });
  const flagged = result.flaggedAnswers ?? 0;
  if (flagged > 0) {
    figures.push({ key: 'flagged-answers', label: 'Flagged answers', value: String(flagged) });
  }
  const failed = result.failedAnswers ?? 0;
  if (failed > 0) {
    figures.push({ key: 'failed-answers', label: 'Failed answers', value: String(failed), tone: 'warning' });
  }
  const providerErrors = result.providerErrors;
  figures.push({
    key: 'provider-errors',
    label: 'Provider errors',
    value: isFiniteNumber(providerErrors) ? formatInteger(providerErrors) : NOT_RECORDED_TEXT
  });
  const reruns = result.rerunsNeeded;
  if (isFiniteNumber(reruns)) {
    figures.push({ key: 'reruns', label: 'Re-runs', value: formatInteger(reruns) });
  }
  return figures;
}

/**
 * Why a finished member shows no index: *No index — 2 answers failed. Open the battery progress to
 * re-run failed questions.* (*the run progress* for a suite member), or *No index for this result.*
 * when no answer failed.
 */
export function modelBatchIndexWithheldText(member: BenchmarkModelBatchMemberDto, targetKind: BenchmarkModelBatchTargetKind): string {
  const failed = member.result?.failedAnswers ?? 0;
  if (!(failed > 0)) return 'No index for this result.';
  const answers = failed === 1 ? '1 answer failed' : `${failed} answers failed`;
  const progress = targetKind === 'Battery' ? 'the battery progress' : 'the run progress';
  return `No index — ${answers}. Open ${progress} to re-run failed questions.`;
}

/**
 * What an index over several runs stands on when some of them withhold their own index because a
 * question failed at the provider: over their scored answers for a group analysis, the mean of the
 * other runs for a run mean. Null without an index, without such a run, or for any other source.
 */
export function modelBatchWithheldRunsNote(member: BenchmarkModelBatchMemberDto): string | null {
  const result = member.result ?? null;
  const count = result?.indexWithheldRunCount ?? 0;
  if (!result || !(count > 0)) return null;
  if (!isFiniteNumber(result.intelligenceIndex) && !isFiniteNumber(result.overallIndex)) return null;
  const one = count === 1;
  const lead = one
    ? '1 run withholds its own index (a question failed at the provider)'
    : `${count} runs withhold their own index (a question failed at the provider)`;
  switch (result.indexSource) {
    case 'groupAnalysis': return `${lead}; this index is over ${one ? 'its' : 'their'} scored answers.`;
    case 'runMean': return `${lead}; this index is the mean of the other runs.`;
    default: return null;
  }
}
