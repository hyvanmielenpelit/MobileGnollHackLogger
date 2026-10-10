import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
  inject
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

import { Subscription } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkModelBatchMemberDto,
  BenchmarkModelBatchResumeOptionDto,
  BenchmarkModelBatchRunDto
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import { IndexBadgeComponent } from '../../../shared/index-badge/index-badge.component';
import { ProviderBadgeComponent } from '../../../shared/provider-badge/provider-badge.component';
import { elapsedMsBetween, parseServerUtcDate } from '../../../utils/date.util';
import { downloadTextFile } from '../../../utils/download.util';
import { startElapsedTicker } from '../../../utils/elapsed-ticker';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { formatCostAmount, formatElapsed, formatModelTime } from '../benchmark-run-format';
import { formatCost, httpErrorText } from '../batteries/battery.models';
import { RunFactBadge, RunFactModel, RunFactRow } from '../run-report-frame/run-facts';
import { RunFactsComponent } from '../run-report-frame/run-facts.component';
import { runStageCaption } from '../run-stage-labels';
import { BenchmarkActiveRunMonitor } from '../state/benchmark-active-run.monitor';
import { BenchmarkShellBridge, ComparisonWizardPreset } from '../state/benchmark-shell-bridge.service';
import { BenchmarkWorkspaceStore } from '../state/benchmark-workspace.store';
import {
  MODEL_BATCH_COMPARE_REASON,
  MODEL_BATCH_CORPUS_QUIET_TEXT,
  MODEL_BATCH_RESUME_ORDER,
  canCompareModelBatch,
  isLiveModelBatchStatus,
  modelBatchCurrentMember,
  modelBatchDiagnosticsFileName,
  modelBatchFinishedMemberCount,
  modelBatchLaunchedRunCount,
  modelBatchMemberBadges,
  modelBatchMemberChipClass,
  modelBatchMemberName,
  modelBatchMemberStatusLabel,
  modelBatchOrderText,
  modelBatchPlanText,
  modelBatchPlannedRunCount,
  modelBatchResumeLabel,
  modelBatchStatusLabel,
  modelBatchStopReasonLabel,
  modelBatchTargetText,
  resolveModelBatchComparisonPreset
} from './model-batch.models';

/** What *Open run progress* hands the host: a member's run and the batch it belongs to. */
export interface ModelBatchRunProgressRequest {
  readonly runId: number;
  readonly modelBatchRunId: number;
}

/** What *Open battery progress* hands the host: a member's battery run and the batch it belongs to. */
export interface ModelBatchBatteryProgressRequest {
  readonly batteryRunId: number;
  readonly modelBatchRunId: number;
}

/** One stage of the batch progress rail. */
export interface ModelBatchRailItem {
  readonly key: 'runs' | 'compare';
  readonly name: string;
  readonly state: 'pending' | 'current' | 'done' | 'ended';
  readonly note: string | null;
}

/** One member as its card shows it. */
export interface ModelBatchMemberView {
  readonly member: BenchmarkModelBatchMemberDto;
  readonly position: number;
  readonly name: string;
  readonly badges: RunFactBadge[];
  readonly chipClass: string;
  readonly statusLabel: string;
  /** *Run #79 · Stage 1 of 3 — Answering and grading*, or null when the member runs nothing. */
  readonly stageCaption: string | null;
  /** *Suite 2 of 3 · round 1: Board Suite* for a battery member, *Run 2 of 3* for a series member. */
  readonly stepText: string | null;
  readonly indexValue: number | null;
  readonly indexLabel: string;
  readonly facts: { readonly label: string; readonly value: string }[];
  /** *Run #55*, *Series #8* or *Battery run #12*; null before the member launched anything. */
  readonly refText: string | null;
  /** Why a skipped, failed, stopped or canceled member ended; null otherwise. */
  readonly reason: string | null;
  readonly canSkip: boolean;
  /** The run the member's View report opens, or the battery run whose report it opens. */
  readonly report: { readonly kind: 'run' | 'battery'; readonly id: number } | null;
}

/** What the footer's in-flight request is doing, for the reason the other actions wait. */
type ModelBatchBusy = 'resume' | 'skip' | 'cancel' | 'compare';

/**
 * Progress of one model batch: a stage rail (the model runs, then the comparison they make ready),
 * the stop reason and the resume options in words, a stat strip, one card per model in run order and
 * the batch diagnostics. It loads the batch itself and polls it while it is visible and live or
 * resumable, one request at a time, backing off on failures and pausing while the tab is hidden; the
 * host owns `visible`.
 *
 * It never embeds a member's run or battery progress: two modal dialogs in the top layer trap focus
 * between them, so *Open run progress* and *Open battery progress* close this dialog and hand the id
 * to the host, which reopens this dialog when that one closes.
 */
@Component({
  selector: 'app-model-batch-progress-dialog',
  standalone: true,
  imports: [NgTemplateOutlet, ProviderBadgeComponent, IndexBadgeComponent, RunFactsComponent],
  templateUrl: './model-batch-progress-dialog.component.html',
  styleUrls: ['./model-batch-progress-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ModelBatchProgressDialogComponent implements OnInit, OnChanges, OnDestroy {
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);
  /** The confirmations of Cancel Batch and Re-run under current instrument. */
  private readonly bridge = inject(BenchmarkShellBridge, { optional: true });
  /** Arms the end signals under a resume click's gesture. */
  private readonly monitor = inject(BenchmarkActiveRunMonitor, { optional: true });
  /** The configurations, for the graders' names. */
  private readonly workspace = inject(BenchmarkWorkspaceStore, { optional: true });

  static readonly POLL_INTERVAL_MS = 2000;
  /** Poll delays after consecutive failures; the last repeats. */
  static readonly POLL_BACKOFF_MS = [4000, 8000, 16000, 30000] as const;
  private static readonly COPIED_RESET_MS = 2000;

  @Input() modelBatchRunId: number | null = null;
  @Input() visible = false;

  /** Escape, the close button, *Close* and *Run in Background*; the host lowers `visible`. */
  @Output() closed = new EventEmitter<void>();
  /** A member's run; the host opens the run progress dialog on it, with *Back to Batch*. */
  @Output() openRunProgress = new EventEmitter<ModelBatchRunProgressRequest>();
  /** A member's battery run; the host opens the battery progress dialog on it, with *Back to Batch*. */
  @Output() openBatteryProgress = new EventEmitter<ModelBatchBatteryProgressRequest>();
  /** A run whose report the host opens over this dialog. */
  @Output() openRunReport = new EventEmitter<number>();
  /** A battery run whose Battery Run Report the host opens over this dialog. */
  @Output() openBatteryRunReport = new EventEmitter<number>();
  /** The members' results, for the comparison wizard; this dialog has closed. */
  @Output() openComparison = new EventEmitter<ComparisonWizardPreset>();
  /** The batch id after a successful Cancel Batch. */
  @Output() batchCanceled = new EventEmitter<number>();
  /** The batch id after a successful resume. */
  @Output() batchResumed = new EventEmitter<number>();

  @ViewChild('modelBatchDialog') dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('mbTitle') heading?: ElementRef<HTMLElement>;

  batch: BenchmarkModelBatchRunDto | null = null;
  members: ModelBatchMemberView[] = [];
  loadError: string | null = null;
  actionError: string | null = null;
  busy: ModelBatchBusy | null = null;
  /** The pending member whose Skip model request is in flight. */
  skippingMemberId: number | null = null;

  diagnosticsText = '';
  diagnosticsLoading = false;
  diagnosticsError: string | null = null;
  diagnosticsCopyStatus = '';
  copiedDiagnostics = false;
  private diagnosticsOpen = false;
  /** The batch state the diagnostics were last loaded for; a change reloads them while they are open. */
  private diagnosticsStateKey: string | null = null;
  private diagnosticsSubscription: Subscription | null = null;
  private copiedTimer: ReturnType<typeof setTimeout> | null = null;

  readonly corpusQuietText = MODEL_BATCH_CORPUS_QUIET_TEXT;
  readonly compareReason = MODEL_BATCH_COMPARE_REASON;
  readonly statusLabel = modelBatchStatusLabel;

  private isOpen = false;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  /** Stops the elapsed ticker; null while none runs. */
  private elapsedTimer: (() => void) | null = null;
  private visibilityHandler: (() => void) | null = null;
  private pollInFlight = false;
  private failureCount = 0;
  private presetSubscription: Subscription | null = null;
  private actionSubscription: Subscription | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['visible'] && !changes['modelBatchRunId']) {
      return;
    }
    if (this.visible && this.modelBatchRunId != null) {
      const idChange = changes['modelBatchRunId'];
      if (idChange && idChange.previousValue !== idChange.currentValue) {
        this.reset();
        if (this.isOpen) {
          this.startPolling();
        }
      }
      this.openDialog();
    } else if (this.isOpen) {
      this.closeDialog();
    }
  }

  ngOnDestroy(): void {
    this.stopPolling();
    this.stopElapsedTicker();
    this.presetSubscription?.unsubscribe();
    this.actionSubscription?.unsubscribe();
    this.diagnosticsSubscription?.unsubscribe();
    if (this.copiedTimer !== null) {
      clearTimeout(this.copiedTimer);
      this.copiedTimer = null;
    }
  }

  // --- Dialog lifecycle ------------------------------------------------------------------------

  private reset(): void {
    this.stopElapsedTicker();
    this.batch = null;
    this.members = [];
    this.loadError = null;
    this.actionError = null;
    this.busy = null;
    this.skippingMemberId = null;
    this.diagnosticsSubscription?.unsubscribe();
    this.diagnosticsText = '';
    this.diagnosticsLoading = false;
    this.diagnosticsError = null;
    this.diagnosticsCopyStatus = '';
    this.diagnosticsStateKey = null;
  }

  private openDialog(): void {
    if (this.isOpen) {
      return;
    }
    this.isOpen = true;
    this.startPolling();
    const dialog = this.dialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    this.cdr.detectChanges();
    this.heading?.nativeElement.focus();
  }

  private closeDialog(): void {
    this.isOpen = false;
    this.stopPolling();
    this.stopElapsedTicker();
    this.presetSubscription?.unsubscribe();
    this.presetSubscription = null;
    if (this.busy === 'compare') {
      this.busy = null;
    }
    const dialog = this.dialog?.nativeElement;
    if (dialog?.open) {
      dialog.close();
    }
    this.cdr.markForCheck();
  }

  requestClose(): void {
    this.closeDialog();
    this.closed.emit();
  }

  /** The native `cancel` (Escape): the close goes through `requestClose`, so the host hears of it. */
  onCancel(event: Event): void {
    event.preventDefault();
    this.requestClose();
  }

  // --- Polling ---------------------------------------------------------------------------------

  private startPolling(): void {
    this.stopPolling();
    this.failureCount = 0;
    if (this.modelBatchRunId == null) {
      return;
    }
    this.poll();
    if (typeof document !== 'undefined') {
      this.visibilityHandler = () => {
        if (!document.hidden && this.isOpen && this.pollTimer !== null) {
          this.schedulePoll(0);
        }
      };
      document.addEventListener('visibilitychange', this.visibilityHandler);
    }
  }

  private stopPolling(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
    if (this.visibilityHandler && typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.visibilityHandler);
      this.visibilityHandler = null;
    }
  }

  private schedulePoll(delayMs: number): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
    }
    this.pollTimer = setTimeout(() => {
      this.pollTimer = null;
      if (typeof document !== 'undefined' && document.hidden) {
        // A hidden tab is not read; the visibility handler catches up when it returns.
        this.pollTimer = setTimeout(() => this.schedulePoll(0), ModelBatchProgressDialogComponent.POLL_INTERVAL_MS);
        return;
      }
      this.poll();
    }, delayMs);
  }

  /** One request at a time; the next is scheduled only when this one has answered. */
  private poll(): void {
    const id = this.modelBatchRunId;
    if (id == null || this.pollInFlight) {
      return;
    }
    this.pollInFlight = true;
    this.benchmarkService.getModelBatch(id).subscribe({
      next: (batch) => {
        this.pollInFlight = false;
        if (id !== this.modelBatchRunId) {
          return;
        }
        this.failureCount = 0;
        this.loadError = null;
        this.applyBatch(batch);
        this.afterPoll(batch);
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.pollInFlight = false;
        if (id !== this.modelBatchRunId) {
          return;
        }
        this.loadError = httpErrorText(err, 'Could not load the model batch.');
        const backoff = ModelBatchProgressDialogComponent.POLL_BACKOFF_MS;
        const delay = backoff[Math.min(this.failureCount, backoff.length - 1)];
        this.failureCount++;
        if (this.isOpen) {
          this.schedulePoll(delay);
        }
        this.cdr.markForCheck();
      }
    });
  }

  /** Keeps polling while the batch is live or resumable, and the elapsed ticker while it is live. */
  private afterPoll(batch: BenchmarkModelBatchRunDto): void {
    const live = isLiveModelBatchStatus(batch.status);
    if (this.isOpen && (live || batch.resumable)) {
      this.schedulePoll(ModelBatchProgressDialogComponent.POLL_INTERVAL_MS);
    }
    if (!live) {
      this.stopElapsedTicker();
    } else if (this.isOpen && this.elapsedTimer === null) {
      this.startElapsedTicker();
    }
    if (this.diagnosticsOpen && this.diagnosticsStateKey !== this.diagnosticsKeyOf(batch)) {
      this.loadDiagnostics();
    }
  }

  private startElapsedTicker(): void {
    this.stopElapsedTicker();
    this.elapsedTimer = startElapsedTicker(() => this.batch?.startedAtUtc, () => this.cdr.markForCheck());
  }

  private stopElapsedTicker(): void {
    if (this.elapsedTimer !== null) {
      this.elapsedTimer();
      this.elapsedTimer = null;
    }
  }

  private applyBatch(batch: BenchmarkModelBatchRunDto): void {
    this.batch = batch;
    this.members = [...batch.members]
      .sort((a, b) => a.orderIndex - b.orderIndex)
      .map(member => this.memberView(batch, member));
  }

  // --- Read-outs -------------------------------------------------------------------------------

  get isLive(): boolean {
    return isLiveModelBatchStatus(this.batch?.status);
  }

  get titleText(): string {
    return `Model Batch #${this.batch?.id ?? this.modelBatchRunId ?? ''}`;
  }

  /** *Core Battery, revision 3 · 3 models × 2 suites × 2 runs · Random order, seed 4711*. */
  get subtitle(): string {
    const batch = this.batch;
    if (!batch) return '';
    return `${modelBatchTargetText(batch)} · ${modelBatchPlanText(batch)} · ${modelBatchOrderText(batch)}`;
  }

  private graderCache: { run: unknown; configs: unknown; rows: RunFactRow[] } | null = null;

  /** The scoring graders of every member's request: *Assessor*, or *Assessors* for a panel. */
  get graderRows(): RunFactRow[] {
    const run = this.batch?.run ?? null;
    const configs = this.workspace?.systemConfigs ?? null;
    if (this.graderCache && this.graderCache.run === run && this.graderCache.configs === configs) {
      return this.graderCache.rows;
    }
    const rows: RunFactRow[] = [];
    if (run) {
      const model = (id: number, role?: 'A' | 'B'): RunFactModel => {
        const config: SystemAiConfigDto | undefined = configs?.find(c => c.id === id);
        return {
          ...(role ? { role } : {}),
          name: config ? (config.displayName || config.modelId) : `Configuration #${id}`,
          provider: config?.provider || null,
          thinkingLevel: config?.thinkingLevel ?? null,
          reasoningMode: config?.reasoningMode ?? null,
          serviceTier: null,
          customEndpoint: false
        };
      };
      const panel = run.coAssessorModelConfigurationId != null;
      rows.push({
        key: 'assessor',
        label: panel ? 'Assessors' : 'Assessor',
        item: {
          kind: 'models',
          models: panel
            ? [model(run.assessorModelConfigurationId, 'A'), model(run.coAssessorModelConfigurationId!, 'B')]
            : [model(run.assessorModelConfigurationId)]
        }
      });
    }
    this.graderCache = { run, configs, rows };
    return rows;
  }

  /** The rail: the model runs, then the comparison their results make ready. */
  get railItems(): ModelBatchRailItem[] {
    const batch = this.batch;
    if (!batch) return [];
    const total = batch.requestedMemberCount || batch.members.length;
    const finished = modelBatchFinishedMemberCount(batch);
    const note = `${finished} of ${total} models`;
    let runs: ModelBatchRailItem;
    if (this.isLive) {
      runs = { key: 'runs', name: 'Model runs', state: 'current', note };
    } else if (batch.status === 'Completed' || batch.status === 'CompletedWithErrors') {
      runs = { key: 'runs', name: 'Model runs', state: 'done', note };
    } else {
      runs = { key: 'runs', name: 'Model runs', state: 'ended', note: `${modelBatchStatusLabel(batch.status)} at ${note}` };
    }
    const comparable = canCompareModelBatch(batch);
    const compare: ModelBatchRailItem = runs.state === 'done'
      ? {
        key: 'compare', name: 'Ready to compare', state: comparable ? 'done' : 'ended',
        note: comparable ? `${batch.completedMemberCount} models with a result` : 'Fewer than two models have a result'
      }
      : { key: 'compare', name: 'Ready to compare', state: 'pending', note: this.isLive ? null : 'Not reached' };
    return [runs, compare];
  }

  /** The visually hidden word that names a rail item's state. */
  railStateWord(state: ModelBatchRailItem['state']): string {
    switch (state) {
      case 'current': return '(current)';
      case 'done': return '(done)';
      case 'ended': return '(ended)';
      default: return '';
    }
  }

  get finishedCount(): number {
    return this.batch ? modelBatchFinishedMemberCount(this.batch) : 0;
  }

  get requestedCount(): number {
    return this.batch ? (this.batch.requestedMemberCount || this.batch.members.length) : 0;
  }

  /** The one polite announcement: changes with the stage, never with the clock. */
  get stageLine(): string {
    const batch = this.batch;
    if (!batch) {
      return this.loadError ? 'Could not load the model batch.' : 'Loading the model batch…';
    }
    const total = this.requestedCount;
    const current = modelBatchCurrentMember(batch);
    switch (batch.status) {
      case 'Pending':
        return 'Starting the model batch…';
      case 'Running':
        return current
          ? `Running model ${current.orderIndex + 1} of ${total}: ${modelBatchMemberName(current)}`
          : 'Running';
      case 'WaitingForCap':
        return 'Waiting for the run cap';
      case 'Stopped':
        return `Stopped: ${modelBatchStopReasonLabel(batch)}`;
      case 'Completed':
        return `Completed: ${batch.completedMemberCount} of ${total} models`;
      case 'CompletedWithErrors':
        return `Completed with errors: ${batch.completedMemberCount} of ${total} models have a result`;
      default:
        return modelBatchStatusLabel(batch.status);
    }
  }

  get elapsedLabel(): string {
    const batch = this.batch;
    if (!batch?.startedAtUtc) return '—';
    return formatElapsed(elapsedMsBetween(batch.startedAtUtc, batch.completedAtUtc));
  }

  get runsText(): string {
    const batch = this.batch;
    return batch ? `${modelBatchLaunchedRunCount(batch)} of ${modelBatchPlannedRunCount(batch)}` : '';
  }

  get candidateCostLabel(): string | null {
    const value = this.batch?.liveCandidateCostUsd;
    return value != null && Number.isFinite(value) ? formatCost(value) : null;
  }

  get totalCostLabel(): string | null {
    const value = this.batch?.liveTotalCostUsd;
    return value != null && Number.isFinite(value) ? formatCost(value) : null;
  }

  /** The first member the instrument moved before, with the keys that moved; null when none did. */
  get instrumentDrift(): { readonly model: string; readonly keys: string } | null {
    const member = this.batch?.members.find(m => (m.instrumentDriftKeys?.length ?? 0) > 0);
    return member ? { model: modelBatchMemberName(member), keys: member.instrumentDriftKeys.join(', ') } : null;
  }

  /** *No progress for 17 min*, while the server flags the batch stalled. */
  get stallText(): string | null {
    const batch = this.batch;
    if (!batch?.stalled) return null;
    const since = batch.lastProgressAtUtc ? parseServerUtcDate(batch.lastProgressAtUtc).getTime() : NaN;
    const minutes = Number.isFinite(since)
      ? Math.max(batch.stallMinutes, Math.floor((Date.now() - since) / 60_000))
      : batch.stallMinutes;
    return `No progress for ${minutes} min`;
  }

  /** The resume options in footer order. */
  get resumeOptions(): BenchmarkModelBatchResumeOptionDto[] {
    const batch = this.batch;
    if (!batch || this.isLive) return [];
    return [...(batch.resumeOptions ?? [])]
      .sort((a, b) => MODEL_BATCH_RESUME_ORDER.indexOf(a.mode) - MODEL_BATCH_RESUME_ORDER.indexOf(b.mode));
  }

  resumeLabel(option: BenchmarkModelBatchResumeOptionDto): string {
    return this.batch ? modelBatchResumeLabel(option, this.batch) : option.label;
  }

  get canCancel(): boolean {
    return this.isLive || this.batch?.status === 'Stopped';
  }

  get canCompare(): boolean {
    return canCompareModelBatch(this.batch);
  }

  /** Why the footer's actions wait; empty while no request is in flight. */
  get busyReason(): string {
    switch (this.busy) {
      case 'resume': return 'Waiting for the server to resume the batch.';
      case 'skip': return 'Waiting for the server to skip the model.';
      case 'cancel': return 'Waiting for the server to cancel the batch.';
      case 'compare': return 'Gathering the results to compare.';
      default: return '';
    }
  }

  // --- Members ---------------------------------------------------------------------------------

  private memberView(batch: BenchmarkModelBatchRunDto, member: BenchmarkModelBatchMemberDto): ModelBatchMemberView {
    const battery = batch.targetKind === 'Battery';
    const running = member.status === 'Running';
    const result = member.result ?? null;
    const facts: { label: string; value: string }[] = [];
    if (result?.medianModelTimeMs != null) facts.push({ label: 'Median model time', value: formatModelTime(result.medianModelTimeMs) });
    if (result?.ttftP50Ms != null) facts.push({ label: 'TTFT P50', value: formatModelTime(result.ttftP50Ms) });
    if (result?.candidateCostPerQuestionUsd != null) {
      facts.push({ label: 'Cost per question', value: formatCostAmount(result.candidateCostPerQuestionUsd) });
    }
    const index = battery ? result?.overallIndex ?? null : result?.intelligenceIndex ?? null;
    const lastRunId = member.runIds?.length ? member.runIds[member.runIds.length - 1] : member.runId ?? null;
    const finished = member.status === 'Completed' || member.status === 'CompletedWithErrors';
    let report: ModelBatchMemberView['report'] = null;
    if (finished) {
      if (battery && member.batteryRunId != null) report = { kind: 'battery', id: member.batteryRunId };
      if (!battery && lastRunId != null) report = { kind: 'run', id: lastRunId };
    }
    const ended = ['Skipped', 'Failed', 'Stopped', 'Canceled'].includes(member.status);
    return {
      member,
      position: member.orderIndex + 1,
      name: modelBatchMemberName(member),
      badges: modelBatchMemberBadges(member),
      chipClass: modelBatchMemberChipClass(member.status),
      statusLabel: modelBatchMemberStatusLabel(member.status),
      stageCaption: running && member.currentRunId != null
        ? `Run #${member.currentRunId}` + this.stageSuffix(member)
        : null,
      stepText: running ? this.stepText(batch, member) : null,
      indexValue: finished && index != null && Number.isFinite(index) ? index : null,
      indexLabel: battery ? 'Overall Index' : 'Intelligence Index',
      facts: finished ? facts : [],
      refText: battery
        ? (member.batteryRunId != null ? `Battery run #${member.batteryRunId}` : null)
        : member.seriesId != null ? `Series #${member.seriesId}` : member.runId != null ? `Run #${member.runId}` : null,
      reason: ended ? (member.errorMessage?.trim() || this.endedReason(member.status)) : null,
      canSkip: member.status === 'Pending' && (this.isLive || batch.status === 'Stopped'),
      report
    };
  }

  private stageSuffix(member: BenchmarkModelBatchMemberDto): string {
    const stage = runStageCaption(member.currentStage, member.answeredQuestionCount);
    return stage ? ` · ${stage}` : '';
  }

  /** A battery member's slot as suite and round, a series member's run as k of R. */
  private stepText(batch: BenchmarkModelBatchRunDto, member: BenchmarkModelBatchMemberDto): string | null {
    const step = member.currentStepIndex;
    if (step == null || step < 1) return null;
    if (batch.targetKind === 'Battery') {
      const suiteCount = Math.max(1, batch.suiteNames.length);
      const suite = ((step - 1) % suiteCount) + 1;
      const round = Math.floor((step - 1) / suiteCount) + 1;
      const name = batch.suiteNames[suite - 1];
      return `Suite ${suite} of ${suiteCount} · round ${round}${name ? `: ${name}` : ''}`;
    }
    return member.stepCount > 1 ? `Run ${step} of ${member.stepCount}` : null;
  }

  private endedReason(status: string): string {
    switch (status) {
      case 'Skipped': return 'Skipped by the operator.';
      case 'Failed': return 'The model\'s run failed.';
      case 'Stopped': return 'The model\'s run stopped.';
      default: return 'Canceled.';
    }
  }

  /** A running member's elapsed time from its own start; null without one. */
  memberElapsedLabel(member: BenchmarkModelBatchMemberDto): string | null {
    return member.startedAtUtc ? formatElapsed(elapsedMsBetween(member.startedAtUtc, null)) : null;
  }

  // --- Actions ---------------------------------------------------------------------------------

  openMemberRunProgress(runId: number): void {
    // Read before the close: the host may stop pointing this dialog at the batch.
    const batchId = this.batch?.id ?? this.modelBatchRunId;
    this.requestClose();
    if (batchId != null) {
      this.openRunProgress.emit({ runId, modelBatchRunId: batchId });
    }
  }

  openMemberBatteryProgress(member: BenchmarkModelBatchMemberDto): void {
    const batchId = this.batch?.id ?? this.modelBatchRunId;
    if (member.batteryRunId == null) return;
    this.requestClose();
    if (batchId != null) {
      this.openBatteryProgress.emit({ batteryRunId: member.batteryRunId, modelBatchRunId: batchId });
    }
  }

  /** View report: the run report or the Battery Run Report, opened over this dialog. */
  viewMemberReport(view: ModelBatchMemberView): void {
    const report = view.report;
    if (!report) return;
    if (report.kind === 'battery') {
      this.openBatteryRunReport.emit(report.id);
    } else {
      this.openRunReport.emit(report.id);
    }
  }

  /** Skip model: a pending member is marked Skipped and never runs. */
  skipMember(member: BenchmarkModelBatchMemberDto): void {
    const batch = this.batch;
    if (!batch || this.busy || member.status !== 'Pending') return;
    this.busy = 'skip';
    this.skippingMemberId = member.id;
    this.actionError = null;
    this.actionSubscription = this.benchmarkService.skipModelBatchMember(batch.id, member.id).subscribe({
      next: (updated) => {
        this.busy = null;
        this.skippingMemberId = null;
        if (updated?.id === this.modelBatchRunId) {
          this.applyBatch(updated);
        }
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.busy = null;
        this.skippingMemberId = null;
        this.actionError = httpErrorText(err, `${modelBatchMemberName(member)} could not be skipped.`);
        this.cdr.markForCheck();
      }
    });
  }

  /**
   * A resume button. Re-run under current instrument asks first, with the cost of re-running every
   * completed member; the others resume at once. The end signals are armed under the gesture.
   */
  resume(option: BenchmarkModelBatchResumeOptionDto): void {
    const batch = this.batch;
    if (!batch || this.busy) return;
    if (option.mode === 'RerunUnderCurrentInstrument' && this.bridge) {
      this.bridge.openConfirmDialog({
        title: `Re-run model batch #${batch.id} under the current instrument?`,
        message: this.rerunConfirmText(batch),
        buttonText: 'Re-run Every Model',
        buttonClass: 'btn-gh',
        icon: 'none',
        action: () => this.sendResume(option)
      });
      return;
    }
    this.sendResume(option);
  }

  /** *Every completed model runs again: 3 models, about $4.20 at their last cost.* */
  private rerunConfirmText(batch: BenchmarkModelBatchRunDto): string {
    const completed = batch.members.filter(m => m.status === 'Completed' || m.status === 'CompletedWithErrors');
    const costs = completed.map(m => m.result?.totalCostUsd).filter((c): c is number => c != null && Number.isFinite(c));
    const count = `${completed.length} ${completed.length === 1 ? 'model' : 'models'}`;
    const cost = costs.length > 0 ? `, about ${formatCost(costs.reduce((a, b) => a + b, 0))} at their last cost` : '';
    return `Every completed model runs again under the current instrument: ${count}${cost}. `
      + 'Their earlier results stay in Run History but leave the batch.';
  }

  private sendResume(option: BenchmarkModelBatchResumeOptionDto): void {
    const batch = this.batch;
    if (!batch || this.busy) return;
    // Synchronous, inside the click's gesture, so either sound may play later from a hidden tab.
    this.monitor?.armCompletionSignalsFromGesture();
    this.busy = 'resume';
    this.actionError = null;
    this.actionSubscription = this.benchmarkService.resumeModelBatch(batch.id, option.mode).subscribe({
      next: (updated) => {
        this.busy = null;
        if (updated?.id === this.modelBatchRunId) {
          this.applyBatch(updated);
        }
        this.batchResumed.emit(batch.id);
        if (this.isOpen) {
          this.startPolling();
        }
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.busy = null;
        this.actionError = err?.status === 409 && err.error?.instrumentChanged
          ? (err.error.message || `The instrument changed since the first model: ${(err.error.changedKeys ?? []).join(', ')}.`)
          : httpErrorText(err, 'The model batch could not be resumed.');
        if (this.isOpen) {
          this.startPolling();
        }
        this.cdr.markForCheck();
      }
    });
  }

  /** Cancel Batch, after the shell's confirmation. */
  cancelBatch(): void {
    const batch = this.batch;
    if (!batch || this.busy || !this.canCancel) return;
    if (!this.bridge) {
      this.sendCancel(batch.id);
      return;
    }
    this.bridge.openConfirmDialog({
      title: `Cancel model batch #${batch.id}?`,
      message: 'The model in flight is canceled and no further model starts. Completed models keep their results.',
      buttonText: 'Cancel Batch',
      buttonClass: 'btn-gh btn-gh-delete',
      icon: 'none',
      action: () => this.sendCancel(batch.id)
    });
  }

  private sendCancel(batchId: number): void {
    if (this.busy) return;
    this.busy = 'cancel';
    this.actionError = null;
    this.actionSubscription = this.benchmarkService.cancelModelBatch(batchId).subscribe({
      next: () => {
        this.busy = null;
        this.batchCanceled.emit(batchId);
        if (this.isOpen) {
          this.startPolling();
        }
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.busy = null;
        this.actionError = httpErrorText(err, 'The model batch could not be canceled.');
        this.cdr.markForCheck();
      }
    });
  }

  /** Open in Model Comparison: the members' results selected, this dialog closed. */
  openInComparison(): void {
    const batch = this.batch;
    if (!batch || this.busy || !this.canCompare) return;
    this.busy = 'compare';
    this.actionError = null;
    this.presetSubscription?.unsubscribe();
    this.presetSubscription = resolveModelBatchComparisonPreset(batch, this.benchmarkService).subscribe({
      next: (preset) => {
        this.busy = null;
        this.requestClose();
        this.openComparison.emit(preset);
      },
      error: (err) => {
        this.busy = null;
        this.actionError = httpErrorText(err, 'The results could not be gathered for Model Comparison.');
        this.cdr.markForCheck();
      }
    });
  }

  // --- Diagnostics -----------------------------------------------------------------------------

  private diagnosticsKeyOf(batch: BenchmarkModelBatchRunDto): string {
    return `${batch.status}:${batch.currentMemberIndex ?? '-'}:${batch.lastProgressAtUtc ?? '-'}:${modelBatchFinishedMemberCount(batch)}`;
  }

  onDiagnosticsToggle(event: Event): void {
    this.diagnosticsOpen = (event.target as HTMLDetailsElement).open;
    if (this.diagnosticsOpen && (this.diagnosticsStateKey === null || (this.batch && this.diagnosticsStateKey !== this.diagnosticsKeyOf(this.batch)))) {
      this.loadDiagnostics();
    }
  }

  private loadDiagnostics(): void {
    const id = this.batch?.id ?? this.modelBatchRunId;
    if (id == null) return;
    this.diagnosticsStateKey = this.batch ? this.diagnosticsKeyOf(this.batch) : '';
    this.diagnosticsLoading = true;
    this.diagnosticsError = null;
    this.diagnosticsSubscription?.unsubscribe();
    this.diagnosticsSubscription = this.benchmarkService.getModelBatchDiagnostics(id).subscribe({
      next: (text) => {
        this.diagnosticsLoading = false;
        this.diagnosticsText = text ?? '';
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.diagnosticsLoading = false;
        this.diagnosticsStateKey = null;
        this.diagnosticsError = httpErrorText(err, 'The batch diagnostics could not be loaded.');
        this.cdr.markForCheck();
      }
    });
  }

  async copyDiagnostics(): Promise<void> {
    const text = this.diagnosticsText;
    if (!text) return;
    try {
      if (typeof navigator === 'undefined' || !navigator.clipboard) {
        throw new Error('Clipboard unavailable');
      }
      await navigator.clipboard.writeText(text);
      this.copiedDiagnostics = true;
      this.diagnosticsCopyStatus = 'Copied the batch diagnostics.';
      if (this.copiedTimer !== null) {
        clearTimeout(this.copiedTimer);
      }
      this.copiedTimer = setTimeout(() => {
        this.copiedTimer = null;
        this.copiedDiagnostics = false;
        this.diagnosticsCopyStatus = '';
        this.cdr.markForCheck();
      }, ModelBatchProgressDialogComponent.COPIED_RESET_MS);
    } catch {
      this.copiedDiagnostics = false;
      this.diagnosticsCopyStatus = 'Could not copy the batch diagnostics to the clipboard.';
    }
    this.cdr.markForCheck();
  }

  downloadDiagnostics(): void {
    const id = this.batch?.id ?? this.modelBatchRunId;
    if (id == null || !this.diagnosticsText) return;
    downloadTextFile(modelBatchDiagnosticsFileName(id), this.diagnosticsText, 'text/plain;charset=utf-8');
  }
}
