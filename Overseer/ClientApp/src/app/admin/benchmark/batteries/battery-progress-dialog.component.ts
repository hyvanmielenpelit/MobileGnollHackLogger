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
  BenchmarkBatterySlotDto
} from '../../../services/admin-benchmark.service';
import { elapsedMsBetween, parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import {
  INDEX_WITHHELD_HINT,
  batteryRunStatusLabel,
  formatMs,
  httpErrorText,
  isFinishedBatteryRunStatus,
  isLiveBatteryRunStatus
} from './battery.models';

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
 * Progress of one battery run: a suite × round grid of status chips, the stop reason in words and
 * the actions that move a stopped battery on. It loads the battery run itself and polls it while it
 * is visible and live, as the multi-run progress dialog does for a series; the host owns `visible`.
 *
 * It never embeds the single-run progress view: two modal dialogs in the top layer trap focus
 * between them, so *Open run progress* closes this dialog and hands the run id to the host.
 */
@Component({
  selector: 'app-battery-progress-dialog',
  standalone: true,
  imports: [],
  templateUrl: './battery-progress-dialog.component.html',
  styleUrls: ['./battery-progress-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BatteryProgressDialogComponent implements OnInit, OnChanges, OnDestroy {
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);

  static readonly POLL_INTERVAL_MS = 2000;
  /** Poll delays after consecutive failures; the last repeats. */
  static readonly POLL_BACKOFF_MS = [4000, 8000, 16000, 30000] as const;
  static readonly ELAPSED_TICK_MS = 1000;

  @Input() batteryRunId: number | null = null;
  @Input() visible = false;

  /** Escape, the close button, *Close* and *Run in Background*; the host lowers `visible`. */
  @Output() closed = new EventEmitter<void>();
  /** A member's run id; the host opens the single-run progress dialog on it. */
  @Output() openRunProgress = new EventEmitter<number>();
  /** The battery run id whose analysis the host shows. */
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
    this.requestClose();
    this.openRunProgress.emit(member.runId);
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
        if (this.isOpen && isLiveBatteryRunStatus(run.status)) {
          this.schedulePoll(BatteryProgressDialogComponent.POLL_INTERVAL_MS);
        } else {
          this.stopElapsedTicker();
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

  /** The one polite announcement: changes with the stage, never with the clock. */
  get stageLine(): string {
    const run = this.batteryRun;
    if (!run) {
      return this.loadError ? 'Could not load the battery run.' : 'Loading battery run…';
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
        return `Completed: ${run.completedSuiteCount} of ${run.suiteCount} suites`;
      case 'CompletedWithErrors':
        return `Completed with errors: ${run.completedSuiteCount} of ${run.suiteCount} suites have a usable result`;
      default:
        return batteryRunStatusLabel(run.status);
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
