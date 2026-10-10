import { Observable, catchError, forkJoin, map, of } from 'rxjs';

import type {
  AdminBenchmarkService,
  BenchmarkModelBatchMemberDto,
  BenchmarkModelBatchMemberStatus,
  BenchmarkModelBatchResumeMode,
  BenchmarkModelBatchResumeOptionDto,
  BenchmarkModelBatchRunDto,
  BenchmarkModelBatchStopReason
} from '../../../services/admin-benchmark.service';
import { MAX_COMPARISON_SOURCES } from '../model-comparison/comparison-source-picker.component';
import { RunFactBadge, runFactBadges } from '../run-report-frame/run-facts';
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

/** The reason Open in Model Comparison waits, shown beside the button while it is unavailable. */
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
export const MODEL_BATCH_OWNED_REASON = 'Use the model batch\'s progress dialog.';
