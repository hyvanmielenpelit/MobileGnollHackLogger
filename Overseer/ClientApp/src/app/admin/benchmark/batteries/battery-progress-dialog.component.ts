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

import { Subscription } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkBatteryAttachCandidateDto,
  BenchmarkBatteryMemberDto,
  BenchmarkBatteryResumeMode,
  BenchmarkBatteryRunDto,
  BenchmarkBatterySlotDto,
  BenchmarkRunReportJobDto
} from '../../../services/admin-benchmark.service';
import { ProviderBadgeComponent } from '../../../shared/provider-badge/provider-badge.component';
import { elapsedMsBetween, parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { RunFactBadge } from '../run-report-frame/run-facts';
import { BenchmarkWorkspaceStore } from '../state/benchmark-workspace.store';
import {
  INDEX_WITHHELD_HINT,
  batteryAnalysisPending,
  batteryAwaitsPostRun,
  batteryAwaitsReports,
  batteryModelBadges,
  batteryModelName,
  batteryPostRunGraceOpen,
  batteryReportDocumentsStatusName,
  batteryRunStatusLabel,
  formatMs,
  formatNumber,
  httpErrorText,
  isFinishedBatteryRunStatus,
  isLiveBatteryRunStatus
} from './battery.models';

/** What *Open run progress* hands the host: the member's run and the battery run it belongs to. */
export interface BatteryMemberRunProgressRequest {
  readonly runId: number;
  readonly batteryRunId: number;
}

/** One stage of the battery progress rail. */
export interface BatteryRailItem {
  readonly key: 'runs' | 'analysis' | 'reports';
  readonly name: string;
  readonly state: 'pending' | 'current' | 'done' | 'ended';
  readonly note: string | null;
}

/** The state a (suite, round) cell of the member grid shows. */
export type BatterySlotState =
  | 'pending'
  | 'running'
  | 'completed'
  | 'indexWithheld'
  | 'instrumentChanged'
  | 'failed'
  | 'superseded';

export const BATTERY_SLOT_STATE_LABELS: Record<BatterySlotState, string> = {
  pending: 'Pending',
  running: 'Running',
  completed: 'Completed with index',
  indexWithheld: 'Index withheld',
  instrumentChanged: 'Instrument changed',
  failed: 'Failed',
  superseded: 'Superseded'
};

const SUCCESSFUL_RUN_STATUSES = ['Completed', 'CompletedWithLimits', 'CompletedWithErrors'];

/** The battery run states in which the server takes an attached run, while it is not being driven. */
const ATTACHABLE_BATTERY_STATUSES: readonly string[] = ['Stopped', 'Completed', 'CompletedWithErrors'];

/**
 * The state of one slot. The occupying member decides it; an empty slot is Pending, unless a
 * superseded member held it, in which case it is Failed when that member's run failed or was
 * canceled, and Superseded otherwise.
 */
export function batterySlotState(
  slot: BenchmarkBatterySlotDto,
  members: readonly BenchmarkBatteryMemberDto[]
): BatterySlotState {
  const member = slot.member;
  if (member) {
    if (member.guardFailure) return 'instrumentChanged';
    if (member.runStatus === 'Running') return 'running';
    if (member.runStatus === 'Failed' || member.runStatus === 'Canceled') return 'failed';
    if (SUCCESSFUL_RUN_STATUSES.includes(member.runStatus)) {
      return member.usable && member.qualityIndex != null ? 'completed' : 'indexWithheld';
    }
    return member.usable ? 'completed' : 'failed';
  }
  const previous = supersededMembersOf(slot, members);
  if (previous.length === 0) return 'pending';
  const newest = previous[previous.length - 1];
  return newest.runStatus === 'Failed' || newest.runStatus === 'Canceled' ? 'failed' : 'superseded';
}

/** The superseded members that held a slot, oldest first. */
export function supersededMembersOf(
  slot: BenchmarkBatterySlotDto,
  members: readonly BenchmarkBatteryMemberDto[]
): BenchmarkBatteryMemberDto[] {
  return members.filter(m => m.superseded && m.suiteIndex === slot.suiteIndex && m.round === slot.round);
}

interface GridRow {
  readonly suiteIndex: number;
  readonly suiteName: string;
  readonly cells: readonly GridCell[];
}

interface GridCell {
  readonly slot: BenchmarkBatterySlotDto;
  readonly state: BatterySlotState;
  readonly superseded: readonly BenchmarkBatteryMemberDto[];
}

/** The slot whose attach candidates the panel lists. */
interface AttachSlot {
  readonly suiteIndex: number;
  readonly round: number;
  readonly suiteName: string;
}

/**
 * Progress of one battery run: a stage rail (the suite runs, the battery analysis and, with a report
 * writer, the AI-written reports), a suite × round grid of status chips, the stop reason in words and
 * the actions that move a stopped battery on. It loads the battery run itself and polls it while it
 * is visible and live or its post-run work is under way, as the multi-run progress dialog does for a
 * series; the host owns `visible`.
 *
 * It never embeds the single-run progress view: two modal dialogs in the top layer trap focus
 * between them, so *Open run progress* closes this dialog and hands the run id to the host.
 */
@Component({
  selector: 'app-battery-progress-dialog',
  standalone: true,
  imports: [ProviderBadgeComponent],
  templateUrl: './battery-progress-dialog.component.html',
  styleUrls: ['./battery-progress-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BatteryProgressDialogComponent implements OnInit, OnChanges, OnDestroy {
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);
  /** The configurations, for the report writer's name before a job view names it. */
  private readonly workspace = inject(BenchmarkWorkspaceStore, { optional: true });

  static readonly POLL_INTERVAL_MS = 2000;
  /** Poll delays after consecutive failures; the last repeats. */
  static readonly POLL_BACKOFF_MS = [4000, 8000, 16000, 30000] as const;
  static readonly ELAPSED_TICK_MS = 1000;

  @Input() batteryRunId: number | null = null;
  @Input() visible = false;

  /** Escape, the close button, *Close* and *Run in Background*; the host lowers `visible`. */
  @Output() closed = new EventEmitter<void>();
  /** A member's run and its battery run; the host opens the single-run progress dialog on the run. */
  @Output() openRunProgress = new EventEmitter<BatteryMemberRunProgressRequest>();
  /** Open Analysis: the battery run id whose Battery Run Report the host opens. */
  @Output() openAnalysis = new EventEmitter<number>();
  /** The battery run id after a successful Continue or Re-run under current instrument. */
  @Output() batteryResumed = new EventEmitter<number>();
  /** The battery run id after a successful Cancel Battery. */
  @Output() batteryCanceled = new EventEmitter<number>();
  /** The battery run id after an existing run was attached to one of its slots. */
  @Output() memberAttached = new EventEmitter<number>();

  @ViewChild('batteryProgressDialog') dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('batteryProgressHeading') heading?: ElementRef<HTMLElement>;
  @ViewChild('attachPanelHeading') attachPanelHeading?: ElementRef<HTMLElement>;

  /** The slot whose candidates the attach panel lists; null while the panel is closed. */
  attachSlot: AttachSlot | null = null;
  attachCandidates: BenchmarkBatteryAttachCandidateDto[] = [];
  attachCandidatesLoading = false;
  attachError: string | null = null;
  /** The candidate being attached; every Attach button waits for it. */
  attachInFlightRunId: number | null = null;
  private attachCandidatesSubscription: Subscription | null = null;
  /** The cell button that opened the panel, focused again when it closes. */
  private attachOpener: HTMLElement | null = null;

  batteryRun: BenchmarkBatteryRunDto | null = null;
  /** The battery run's report writing job, polled while its reports stage is current. */
  reportJob: BenchmarkRunReportJobDto | null = null;
  private reportJobSubscription: Subscription | null = null;
  grid: GridRow[] = [];
  rounds: number[] = [];

  loadError: string | null = null;
  actionError: string | null = null;
  /** A Continue refused for a moved instrument: the re-run is offered even without that stop reason. */
  resumeRefusedForInstrument = false;
  resumeInFlight = false;
  cancelInFlight = false;

  readonly slotLabels = BATTERY_SLOT_STATE_LABELS;
  readonly indexWithheldHint = INDEX_WITHHELD_HINT;
  readonly statusLabel = batteryRunStatusLabel;

  private isOpen = false;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private elapsedTimer: ReturnType<typeof setInterval> | null = null;
  private visibilityHandler: (() => void) | null = null;
  private pollInFlight = false;
  private failureCount = 0;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['visible'] && !changes['batteryRunId']) {
      return;
    }
    if (this.visible && this.batteryRunId != null) {
      const idChange = changes['batteryRunId'];
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
    this.attachCandidatesSubscription?.unsubscribe();
  }

  // --- Dialog lifecycle ------------------------------------------------------------------------

  private reset(): void {
    this.batteryRun = null;
    this.reportJob = null;
    this.grid = [];
    this.rounds = [];
    this.loadError = null;
    this.actionError = null;
    this.resumeRefusedForInstrument = false;
    this.resetAttach();
  }

  private openDialog(): void {
    if (this.isOpen) {
      return;
    }
    this.isOpen = true;
    this.startPolling();
    this.startElapsedTicker();
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
    this.resetAttach();
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

  openMemberRunProgress(member: BenchmarkBatteryMemberDto): void {
    // Read before the close: the host may stop pointing this dialog at the battery run.
    const batteryRunId = this.batteryRun?.id ?? this.batteryRunId;
    this.requestClose();
    if (batteryRunId != null) {
      this.openRunProgress.emit({ runId: member.runId, batteryRunId });
    }
  }

  showAnalysis(): void {
    const run = this.batteryRun;
    if (!run || !this.canOpenAnalysis) {
      return;
    }
    this.requestClose();
    this.openAnalysis.emit(run.id);
  }

  // --- Polling ---------------------------------------------------------------------------------

  private startPolling(): void {
    this.stopPolling();
    this.failureCount = 0;
    if (this.batteryRunId == null) {
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
    this.reportJobSubscription?.unsubscribe();
    this.reportJobSubscription = null;
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
        this.pollTimer = setTimeout(() => this.schedulePoll(0), BatteryProgressDialogComponent.POLL_INTERVAL_MS);
        return;
      }
      this.poll();
    }, delayMs);
  }

  /** One request at a time; the next is scheduled only when this one has answered. */
  private poll(): void {
    const id = this.batteryRunId;
    if (id == null || this.pollInFlight) {
      return;
    }
    this.pollInFlight = true;
    this.benchmarkService.getBatteryRun(id).subscribe({
      next: (run) => {
        this.pollInFlight = false;
        if (id !== this.batteryRunId) {
          return;
        }
        this.failureCount = 0;
        this.loadError = null;
        this.applyRun(run);
        const live = isLiveBatteryRunStatus(run.status);
        if (this.isOpen && (live || batteryAwaitsPostRun(run))) {
          this.schedulePoll(BatteryProgressDialogComponent.POLL_INTERVAL_MS);
        }
        if (!live) {
          this.stopElapsedTicker();
        }
        if (this.isOpen && this.reportsStageCurrent) {
          this.pollReportJob(id);
        }
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.pollInFlight = false;
        if (id !== this.batteryRunId) {
          return;
        }
        this.loadError = httpErrorText(err, 'Could not load the battery run.');
        const backoff = BatteryProgressDialogComponent.POLL_BACKOFF_MS;
        const delay = backoff[Math.min(this.failureCount, backoff.length - 1)];
        this.failureCount++;
        if (this.isOpen) {
          this.schedulePoll(delay);
        }
        this.cdr.markForCheck();
      }
    });
  }

  /** One request at a time: a slow answer is superseded by the next poll's. A failure leaves the last view. */
  private pollReportJob(batteryRunId: number): void {
    this.reportJobSubscription?.unsubscribe();
    this.reportJobSubscription = this.benchmarkService.getBatteryReportJob(batteryRunId).subscribe({
      next: (view) => {
        if (batteryRunId !== this.batteryRunId) {
          return;
        }
        this.reportJob = view;
        this.cdr.markForCheck();
      },
      error: (err) => console.warn('Failed to poll the battery report writing job', err)
    });
  }

  private startElapsedTicker(): void {
    this.stopElapsedTicker();
    this.elapsedTimer = setInterval(() => {
      if (typeof document !== 'undefined' && document.hidden) {
        return;
      }
      this.cdr.markForCheck();
    }, BatteryProgressDialogComponent.ELAPSED_TICK_MS);
  }

  private stopElapsedTicker(): void {
    if (this.elapsedTimer !== null) {
      clearInterval(this.elapsedTimer);
      this.elapsedTimer = null;
    }
  }

  private applyRun(run: BenchmarkBatteryRunDto): void {
    this.batteryRun = run;
    const roundCount = Math.max(1, run.runsPerSuite || 1);
    this.rounds = Array.from({ length: roundCount }, (_, i) => i + 1);
    const suites = [...(run.suites ?? [])].sort((a, b) => a.index - b.index);
    const members = run.members ?? [];
    this.grid = suites.map(suite => ({
      suiteIndex: suite.index,
      suiteName: suite.suiteName,
      cells: this.rounds.map(round => {
        const slot = (run.slots ?? []).find(s => s.suiteIndex === suite.index && s.round === round)
          ?? { suiteIndex: suite.index, round, member: null };
        return {
          slot,
          state: batterySlotState(slot, members),
          superseded: supersededMembersOf(slot, members)
        };
      })
    }));
  }

  // --- Actions ---------------------------------------------------------------------------------

  get isLive(): boolean {
    return isLiveBatteryRunStatus(this.batteryRun?.status);
  }

  get isFinished(): boolean {
    return isFinishedBatteryRunStatus(this.batteryRun?.status);
  }

  get stoppedOnInstrument(): boolean {
    return this.batteryRun?.stopReason === 'InstrumentChanged';
  }

  /** Continue: a resumable battery run that did not stop for a moved instrument (decision 6). */
  get canContinue(): boolean {
    return !!this.batteryRun?.resumable && !this.stoppedOnInstrument && !this.resumeRefusedForInstrument;
  }

  get canRerun(): boolean {
    const run = this.batteryRun;
    if (!run || this.isLive) return false;
    return (this.stoppedOnInstrument && (run.resumable || run.status === 'Stopped')) || this.resumeRefusedForInstrument;
  }

  get canCancel(): boolean {
    return this.isLive || this.batteryRun?.status === 'Stopped';
  }

  get canOpenAnalysis(): boolean {
    return this.isFinished;
  }

  resume(mode: BenchmarkBatteryResumeMode): void {
    const run = this.batteryRun;
    if (!run || this.resumeInFlight) {
      return;
    }
    this.resumeInFlight = true;
    this.actionError = null;
    this.benchmarkService.resumeBatteryRun(run.id, mode).subscribe({
      next: () => {
        this.resumeInFlight = false;
        this.resumeRefusedForInstrument = false;
        this.batteryResumed.emit(run.id);
        this.startPolling();
        this.startElapsedTicker();
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.resumeInFlight = false;
        this.actionError = httpErrorText(err, 'The battery run could not be resumed.');
        if (mode === 'Continue' && err?.status === 409) {
          this.resumeRefusedForInstrument = true;
        }
        this.cdr.markForCheck();
      }
    });
  }

  cancelBattery(): void {
    const run = this.batteryRun;
    if (!run || this.cancelInFlight) {
      return;
    }
    this.cancelInFlight = true;
    this.actionError = null;
    this.benchmarkService.cancelBatteryRun(run.id).subscribe({
      next: () => {
        this.cancelInFlight = false;
        this.batteryCanceled.emit(run.id);
        this.startPolling();
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.cancelInFlight = false;
        this.actionError = httpErrorText(err, 'The battery run could not be canceled.');
        this.cdr.markForCheck();
      }
    });
  }

  // --- Attaching existing runs -------------------------------------------------------------------

  /** Attaching is offered in the states the server accepts it in: stopped or finished, and not driven. */
  get canAttach(): boolean {
    const run = this.batteryRun;
    return !!run && !run.isDriving && ATTACHABLE_BATTERY_STATUSES.includes(run.status);
  }

  /** An empty cell (never filled, or its member superseded) or one whose member's index was withheld. */
  canAttachTo(cell: GridCell): boolean {
    return this.canAttach && (!cell.slot.member || cell.state === 'indexWithheld');
  }

  isAttachOpenFor(row: GridRow, cell: GridCell): boolean {
    return this.attachSlot?.suiteIndex === row.suiteIndex && this.attachSlot?.round === cell.slot.round;
  }

  /** Opens the panel listing the runs that may fill this slot, each with whether it qualifies. */
  openAttach(row: GridRow, cell: GridCell, event?: Event): void {
    const run = this.batteryRun;
    if (!run || !this.canAttachTo(cell)) {
      return;
    }
    this.attachCandidatesSubscription?.unsubscribe();
    this.attachOpener = (event?.currentTarget as HTMLElement | null) ?? null;
    this.attachSlot = { suiteIndex: row.suiteIndex, round: cell.slot.round, suiteName: row.suiteName };
    this.attachCandidates = [];
    this.attachError = null;
    this.attachCandidatesLoading = true;
    this.cdr.detectChanges();
    this.attachPanelHeading?.nativeElement.focus();

    this.attachCandidatesSubscription = this.benchmarkService
      .getBatteryAttachCandidates(run.id, row.suiteIndex, cell.slot.round)
      .subscribe({
        next: (candidates) => {
          this.attachCandidatesLoading = false;
          this.attachCandidates = candidates ?? [];
          this.cdr.markForCheck();
        },
        error: (err) => {
          this.attachCandidatesLoading = false;
          this.attachError = httpErrorText(err, 'Could not load the runs that may be attached.');
          this.cdr.markForCheck();
        }
      });
  }

  closeAttach(): void {
    const opener = this.attachOpener;
    this.resetAttach();
    this.cdr.detectChanges();
    if (opener?.isConnected) {
      opener.focus();
    } else {
      this.heading?.nativeElement.focus();
    }
  }

  /** Attaches the chosen run to the open slot and shows the battery run the server returns. */
  attachCandidate(candidate: BenchmarkBatteryAttachCandidateDto): void {
    const run = this.batteryRun;
    const target = this.attachSlot;
    if (!run || !target || !candidate.eligible || this.attachInFlightRunId != null) {
      return;
    }
    this.attachInFlightRunId = candidate.runId;
    this.attachError = null;
    this.benchmarkService.attachBatteryMember(run.id, {
      suiteIndex: target.suiteIndex,
      round: target.round,
      runId: candidate.runId
    }).subscribe({
      next: (updated) => {
        this.attachInFlightRunId = null;
        if (updated && updated.id === this.batteryRunId) {
          this.applyRun(updated);
        } else {
          this.poll();
        }
        // Closed after the grid shows the new member: the cell's button is gone, so focus goes to the heading.
        this.closeAttach();
        this.memberAttached.emit(run.id);
      },
      error: (err) => {
        this.attachInFlightRunId = null;
        this.attachError = httpErrorText(err, 'The run could not be attached.');
        this.cdr.markForCheck();
      }
    });
  }

  /** The candidate's start time in the reader's locale. */
  candidateStartedLabel(candidate: BenchmarkBatteryAttachCandidateDto): string {
    return candidate.startedAtUtc ? parseServerUtcDate(candidate.startedAtUtc).toLocaleString() : '—';
  }

  private resetAttach(): void {
    this.attachCandidatesSubscription?.unsubscribe();
    this.attachCandidatesSubscription = null;
    this.attachSlot = null;
    this.attachCandidates = [];
    this.attachCandidatesLoading = false;
    this.attachError = null;
    this.attachInFlightRunId = null;
    this.attachOpener = null;
  }

  // --- Read-outs -------------------------------------------------------------------------------

  /** The model under test, by the same rules as Run History's battery card. */
  get modelName(): string {
    return this.batteryRun ? batteryModelName(this.batteryRun) : '';
  }

  get modelBadges(): RunFactBadge[] {
    return this.batteryRun ? batteryModelBadges(this.batteryRun) : [];
  }

  /** The battery run names a report writer, so the rail has a third stage. */
  get hasReportWriter(): boolean {
    return this.batteryRun?.reportWriterModelConfigurationId != null;
  }

  /** The report writer's name: the job's, else the configuration's, else its id. */
  get reportWriterName(): string {
    const writerId = this.batteryRun?.reportWriterModelConfigurationId;
    if (writerId == null) return '';
    if (this.reportJob?.writerConfigId === writerId && this.reportJob.writerDisplayName) {
      return this.reportJob.writerDisplayName;
    }
    const config = this.workspace?.systemConfigs.find(c => c.id === writerId);
    return config?.displayName || `Configuration #${writerId}`;
  }

  /** The battery analysis of the finished run is still being computed, within the grace. */
  private get analysisStageCurrent(): boolean {
    const run = this.batteryRun;
    return !!run && this.isFinished && batteryAnalysisPending(run) && batteryPostRunGraceOpen(run);
  }

  /** The AI-written reports of the finished run are queued or written, or still to be queued within the grace. */
  get reportsStageCurrent(): boolean {
    const run = this.batteryRun;
    return !!run && this.isFinished && batteryAwaitsReports(run);
  }

  /**
   * The rail: the suite runs, the battery analysis and, with a report writer, the AI-written reports.
   * A stage's word is never carried by its color alone: each has a note, and the markup a hidden word.
   */
  get railItems(): BatteryRailItem[] {
    const run = this.batteryRun;
    if (!run) return [];
    const items: BatteryRailItem[] = [this.runsRailItem(run), this.analysisRailItem(run)];
    if (this.hasReportWriter) {
      items.push(this.reportsRailItem(run));
    }
    return items;
  }

  private runsRailItem(run: BenchmarkBatteryRunDto): BatteryRailItem {
    const finished = (run.members ?? []).filter(m => !m.superseded && m.runStatus !== 'Running').length;
    const note = `${Math.min(finished, run.requestedMemberCount)} of ${run.requestedMemberCount} runs`;
    const name = 'Suite runs';
    if (this.isFinished) return { key: 'runs', name, state: 'done', note };
    if (this.isLive) return { key: 'runs', name, state: 'current', note };
    return { key: 'runs', name, state: 'ended', note: `${batteryRunStatusLabel(run.status)} at ${note}` };
  }

  private analysisRailItem(run: BenchmarkBatteryRunDto): BatteryRailItem {
    const name = 'Battery analysis';
    if (!this.isFinished) {
      return { key: 'analysis', name, state: 'pending', note: this.isLive ? null : 'Not reached' };
    }
    if (!batteryAnalysisPending(run)) {
      return run.overallIndex != null
        ? { key: 'analysis', name, state: 'done', note: `Overall Index ${formatNumber(run.overallIndex)}` }
        : {
          key: 'analysis', name, state: 'ended',
          note: `No Overall Index: ${run.completedSuiteCount} of ${run.suiteCount} suites have a usable result`
        };
    }
    return batteryPostRunGraceOpen(run)
      ? { key: 'analysis', name, state: 'current', note: 'Computing the Overall Index…' }
      : { key: 'analysis', name, state: 'ended', note: 'Not computed: use Recompute in the Battery Run Report' };
  }

  private reportsRailItem(run: BenchmarkBatteryRunDto): BatteryRailItem {
    const name = 'AI-written reports';
    if (!this.isFinished) {
      return { key: 'reports', name, state: 'pending', note: this.isLive ? null : 'Not reached' };
    }
    const message = run.reportDocumentsMessage?.trim() || null;
    switch (batteryReportDocumentsStatusName(run.reportDocumentsStatus)) {
      case 'Pending': {
        const ahead = this.reportJob?.jobsAhead;
        const queue = ahead != null && ahead > 0 ? ` (${ahead} ${ahead === 1 ? 'job' : 'jobs'} ahead)` : '';
        return { key: 'reports', name, state: 'current', note: `Waiting for the report writer${queue}` };
      }
      case 'Writing':
        return { key: 'reports', name, state: 'current', note: 'Writing the Executive Summary and the Researcher report' };
      case 'Completed': {
        const written = (this.reportJob?.job?.documents ?? []).filter(doc => doc.documentId != null).length;
        return {
          key: 'reports', name, state: 'done',
          note: written > 0 ? `${written} ${written === 1 ? 'document' : 'documents'} written` : 'Written'
        };
      }
      case 'CompletedWithWarnings':
        return { key: 'reports', name, state: 'done', note: 'Written with warnings' };
      case 'Failed':
        return { key: 'reports', name, state: 'ended', note: message ? `Failed: ${message}` : 'Failed' };
      case 'Skipped':
        return { key: 'reports', name, state: 'ended', note: message ? `Skipped: ${message}` : 'Skipped' };
      case 'Canceled':
        return { key: 'reports', name, state: 'ended', note: message ? `Canceled: ${message}` : 'Canceled' };
      default:
        return batteryPostRunGraceOpen(run)
          ? { key: 'reports', name, state: 'current', note: 'Waiting for the report writer' }
          : { key: 'reports', name, state: 'ended', note: 'Not started' };
    }
  }

  /** The visually hidden word that names a rail item's state. */
  railStateWord(state: BatteryRailItem['state']): string {
    switch (state) {
      case 'current': return '(current)';
      case 'done': return '(done)';
      case 'ended': return '(ended)';
      default: return '';
    }
  }

  /** The one polite announcement: changes with the stage, never with the clock. */
  get stageLine(): string {
    const run = this.batteryRun;
    if (!run) {
      return this.loadError ? 'Could not load the battery run.' : 'Loading battery run…';
    }
    if (this.analysisStageCurrent) {
      return 'Computing the battery analysis…';
    }
    if (this.reportsStageCurrent) {
      return 'Writing the AI reports…';
    }
    switch (run.status) {
      case 'Pending':
        return 'Starting the battery run…';
      case 'Running':
        return run.currentSuitePosition != null
          ? `Running suite ${run.currentSuitePosition} of ${run.suiteCount}`
            + ` · round ${run.currentRound ?? 1} of ${run.runsPerSuite}`
            + (run.currentSuiteName ? `: ${run.currentSuiteName}` : '')
          : 'Running';
      case 'WaitingForCap':
        return 'Waiting for the run cap';
      case 'Stopped':
        return `Stopped: ${run.stopReasonText || run.stopReason || 'no reason recorded'}`;
      case 'Completed':
        return `Completed: ${run.completedSuiteCount} of ${run.suiteCount} suites${this.reportsOutcome}`;
      case 'CompletedWithErrors':
        return `Completed with errors: ${run.completedSuiteCount} of ${run.suiteCount} suites have a usable result${this.reportsOutcome}`;
      default:
        return batteryRunStatusLabel(run.status);
    }
  }

  /** The stage line's ending about the AI-written reports once they are settled; empty without a writer. */
  private get reportsOutcome(): string {
    const run = this.batteryRun;
    if (!run || !this.hasReportWriter) return '';
    switch (batteryReportDocumentsStatusName(run.reportDocumentsStatus)) {
      case 'Completed':
      case 'CompletedWithWarnings':
        return ' · reports written';
      case 'Failed':
        return ' · reports failed';
      default:
        return ' · reports not written';
    }
  }

  get elapsedLabel(): string {
    const run = this.batteryRun;
    if (!run?.startedAtUtc) return '—';
    return formatMs(elapsedMsBetween(run.startedAtUtc, run.completedAtUtc));
  }

  chipClass(state: BatterySlotState): string {
    switch (state) {
      case 'pending': return 'job-status-chip status-pending';
      case 'running': return 'job-status-chip status-answering';
      case 'completed': return 'job-status-chip status-completed';
      case 'indexWithheld': return 'job-status-chip status-partial';
      case 'instrumentChanged': return 'job-status-chip bp-chip-instrument';
      case 'failed': return 'job-status-chip status-failed';
      case 'superseded': return 'job-status-chip status-canceled';
    }
  }
}
