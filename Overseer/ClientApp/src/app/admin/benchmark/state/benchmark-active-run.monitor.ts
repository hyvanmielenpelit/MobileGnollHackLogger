import { OnDestroy, inject, Injectable } from '@angular/core';
import {
  AdminBenchmarkService,
  BenchmarkQuestionDto,
  BenchmarkRunDetailDto,
  StartBenchmarkRunRequest,
  SameProviderWarningDto,
  BenchmarkRunSeriesDto,
  BenchmarkBatteryRunDto,
  BenchmarkBatteryResumeMode,
  BenchmarkRunReportDocumentsStatus,
  BenchmarkRunReportJobDto
} from '../../../services/admin-benchmark.service';
import { BenchmarkCompletionSoundService } from '../../../services/benchmark-completion-sound.service';
import {
  BenchmarkCompletionNotificationService,
  BenchmarkNotificationPermissionOutcome,
  BenchmarkNotifyOutcome
} from '../../../services/benchmark-completion-notification.service';
import { BenchmarkBackgroundActivityService } from '../../../services/benchmark-background-activity.service';
import { BenchmarkPollTickerService, BenchmarkPollTickerHandle } from '../../../services/benchmark-poll-ticker.service';
import { parseServerUtcDate } from '../../../utils/date.util';
import { Subject, Subscription } from 'rxjs';
import { refusalText, reportDocumentsStatusOf, formatStatus } from '../benchmark-run-format';
import { BenchmarkWorkspaceStore } from './benchmark-workspace.store';
import { BenchmarkLauncherState } from './benchmark-launcher.state';
import { BenchmarkViewSync } from './benchmark-view-sync.service';
import { BenchmarkShellBridge } from './benchmark-shell-bridge.service';

/** How a start request ended: started, or held for a same-provider acknowledgment. */
export type BenchmarkStartOutcome = { kind: 'started' } | { kind: 'sameProvider'; warning: SameProviderWarningDto };

/** A series or battery poller that keeps failing to reach the server, for the shell's Lost contact notice. */
export interface BenchmarkLostContactNotice {
  readonly kind: 'series' | 'battery';
  /** The series id or battery run id the poller follows. */
  readonly id: number;
  /** Consecutive failed polls. */
  readonly failureCount: number;
  /** When the streak's first failed poll was made (client clock, ms). */
  readonly sinceMs: number;
  /** The gap before the next attempt; null once the poller has given up. */
  readonly retryIntervalMs: number | null;
  /** The failures lasted {@link BenchmarkActiveRunMonitor.LOST_CONTACT_GIVE_UP_MS} and polling stopped. */
  readonly gaveUp: boolean;
}

/** Starts runs, series and battery runs, follows them with their pollers, and signals their completion on every sub-tab. */
@Injectable()
export class BenchmarkActiveRunMonitor implements OnDestroy {
  private readonly viewSync = inject(BenchmarkViewSync);
  private readonly bridge = inject(BenchmarkShellBridge);
  private readonly workspace = inject(BenchmarkWorkspaceStore);
  private readonly launcher = inject(BenchmarkLauncherState);
  private benchmarkService = inject(AdminBenchmarkService);
  private backgroundActivity = inject(BenchmarkBackgroundActivityService);
  private pollTicker = inject(BenchmarkPollTickerService);
  private completionSoundService = inject(BenchmarkCompletionSoundService);
  private completionNotificationService = inject(BenchmarkCompletionNotificationService);


  startingRun = false;

  /** How each start request ended, for the Run tab's same-provider dialog. */
  readonly startOutcome$ = new Subject<BenchmarkStartOutcome>();

  runErrorMessage: string | null = null;

  // Active Run Tracking
  static readonly RUN_POLL_INTERVAL_MS = 2000;

  static readonly RUN_ELAPSED_TICK_MS = 1000;

  /**
   * A single failed poll is noise — a dropped request, a momentary 502 — and stopping the tab's
   * only view of a run over one of those is worse than the failure itself. Five in a row, at the
   * run poller's 2 s cadence, is ~10 s of the server genuinely not answering, which is when
   * polling gives up rather than looping silently forever. This is the run poller's limit; the
   * series and battery pollers back off instead (see {@link LOST_CONTACT_BACKOFF_MS}).
   */
  private static readonly MAX_CONSECUTIVE_POLL_FAILURES = 5;

  /**
   * The gaps between a series or battery poller's attempts while its polls keep failing: 5, 10, 20
   * and 40 s, then 60 s for as long as the failures last. A series or battery runs for hours, and a
   * server restart or a network drop of a few minutes must not end the tab's view of it.
   */
  static readonly LOST_CONTACT_BACKOFF_MS: readonly number[] = [5000, 10000, 20000, 40000, 60000];

  /** How long a series or battery poller keeps failing before it stops. */
  static readonly LOST_CONTACT_GIVE_UP_MS = 10 * 60_000;

  /** The consecutive failures that raise the Lost contact notice; a single failed poll is noise. */
  static readonly LOST_CONTACT_NOTICE_AFTER_FAILURES = 2;

  /**
   * Set while the series or battery poller is failing to reach the server, and kept after it gives
   * up. Cleared by that poller's next successful poll or restart.
   */
  lostContact: BenchmarkLostContactNotice | null = null;

  runElapsedInterval: any = null;

  lastRunPollAtUtc: string | null = null;

  lastRunPollError: string | null = null;

  runPollFailureCount = 0;

  /**
   * The member run whose poller gave up on failures while its series or battery poller kept going;
   * that poller's next successful poll restarts it.
   */
  private runPollGaveUpRunId: number | null = null;

  runQuestionsLoadError: string | null = null;

  activeRunId: number | null = null;

  activeRunDetail: BenchmarkRunDetailDto | null = null;

  pollTickerHandle: BenchmarkPollTickerHandle | null = null;

  /**
   * Kept separate from visibilityChangeHandler, which belongs to difficulty polling.
   * One shared field would let whichever poller stops last detach the other's listener.
   */
  private runVisibilityChangeHandler: (() => void) | null = null;

  /**
   * How long after a run that names a report writer is first seen Completed its poll continues while
   * the run's report status is still NotRequested: the server queues the writing job just after
   * scoring, and a job that never appears must not keep the poll going.
   */
  static readonly RUN_REPORT_STAGE_GRACE_MS = 30000;

  /** When the active run was first seen terminal (client clock), for the report stage's start grace. */
  private runTerminalSeenAt: { runId: number; atMs: number } | null = null;

  /** The report stage's start grace was still open at the last terminal poll. */
  private runReportGraceOpen = false;

  /** The active run's report writing job, polled alongside the run while stage 4 is current. */
  activeRunReportJob: BenchmarkRunReportJobDto | null = null;

  /** When the last job view arrived (client clock), to advance the server's clock between polls. */
  activeRunReportJobReceivedAtMs = 0;

  private runReportJobSub: Subscription | null = null;

  // Run Progress Dialog
  isRunProgressDialogOpen = false;

  returnToSeriesOnClose = false;

  runProgressQuestions: BenchmarkQuestionDto[] = [];

  /**
   * Suite the cached runProgressQuestions belong to; null means nothing is loaded. This,
   * not the array's length, is what gates the fetch — a suite that genuinely has no
   * questions would otherwise be re-fetched on every dialog open.
   */
  runProgressQuestionsSuiteId: number | null = null;

  runDiagnosticsPanelCapturedAt = new Date(0);

  // --- Multi-run series ---
  //
  // A series is N executions of one identical request, strictly one at a time. Everything here is
  // inert at runCount 1: no series row is created, startRun() is posted exactly as before, and the
  // single-run banner and dialog are the only progress surfaces. That is the regression that
  // matters most about this feature, so the branch is one `if` in startBenchmark and nowhere else.

  private static readonly SERIES_POLL_INTERVAL_MS = 5000;

  activeSeriesId: number | null = null;

  activeSeries: BenchmarkRunSeriesDto | null = null;

  private seriesPollTickerHandle: BenchmarkPollTickerHandle | null = null;

  private seriesPollFailureCount = 0;

  private seriesPollFailureSinceMs = 0;

  /** While the series poller is failing, ticks before this time (client clock, ms) are skipped. */
  private seriesNextPollDueAtMs = 0;

  private seriesVisibilityChangeHandler: (() => void) | null = null;

  /**
   * The series whose poller is live, set before its first poll. While it is set the series owns the
   * background lock, and the run poller that follows each member neither takes nor releases it.
   */
  private lockedSeriesId: number | null = null;

  /** The Multi-Run Progress dialog's visibility. The dialog element itself belongs to that component. */
  multiRunDialogVisible = false;

  /**
   * A series the operator asked to look at that this component is not driving — set by the
   * Multi-Run Analysis tab's Series badge and cleared when the dialog closes. Kept apart from
   * <see cref="activeSeriesId"/> so opening someone else's finished series cannot be mistaken for
   * this page having one in flight.
   */
  seriesDialogId: number | null = null;

  seriesErrorMessage: string | null = null;

  resumingSeries = false;

  activeBatteryRunId: number | null = null;

  activeBatteryRun: BenchmarkBatteryRunDto | null = null;

  private batteryPollTickerHandle: BenchmarkPollTickerHandle | null = null;

  private batteryPollFailureCount = 0;

  private batteryPollFailureSinceMs = 0;

  /** While the battery poller is failing, ticks before this time (client clock, ms) are skipped. */
  private batteryNextPollDueAtMs = 0;

  private batteryVisibilityChangeHandler: (() => void) | null = null;

  private lastBatteryPollAttemptAtMs = 0;

  /** The battery run whose poller is live; while set it owns the background lock, as a series does. */
  private lockedBatteryRunId: number | null = null;

  /** Battery runs the poller has observed live; a terminal poll chimes only for one of these. */
  batteriesSeenLive = new Set<number>();

  /** The Battery Progress dialog's visibility. The dialog element itself belongs to that component. */
  batteryDialogVisible = false;

  /** A battery run opened from the Multi-Suite tab rather than the one this page drives. */
  batteryDialogRunId: number | null = null;

  batteryErrorMessage: string | null = null;

  resumingBattery = false;

  /** Closing the run progress dialog reopens the Battery Progress dialog it was opened from. */
  returnToBatteryOnClose = false;

  // --- Completion sound ---
  //
  // The chime plays once per run or series that was actually watched live, never for one opened
  // from history already terminal. runsSeenLive and seriesSeenLive hold the ids pollRunDetail and
  // pollSeries have observed Running/live; a terminal poll only chimes if its id is still in the
  // set, and removes it either way so a later poll of the same id cannot chime twice. A run that is
  // a member of a still-live series never chimes on its own — the series chimes once for all of
  // them.

  private static readonly HIDDEN_POLL_INTERVAL_MS = 15000;

  /** Set only when the last chime attempt was blocked by the browser's autoplay policy, or deferred by it. */
  completionSoundStatus: string | null = null;

  lastCompletionSoundOutcome: 'played' | 'blocked' | 'unsupported' | 'duplicate' | 'deferred' | null = null;

  /** The reason a notification permission request did not end in `completionNotification` being on. */
  completionNotificationStatus: string | null = null;

  /** One `notify()` call per record, kept for the run diagnostics capture; last 10, oldest dropped first. */
  readonly notificationAttempts: {
    atUtc: string; key: string; hidden: boolean; focused: boolean; outcome: BenchmarkNotifyOutcome;
  }[] = [];

  private static readonly MAX_NOTIFICATION_ATTEMPTS = 10;

  private runsSeenLive = new Set<number>();

  seriesSeenLive = new Set<number>();

  /** Live-watched runs this page asked the server to cancel; consumed at their terminal poll. */
  readonly operatorCancelledRunIds = new Set<number>();

  /** Last time either poller actually polled, hidden or not — what the hidden-tab cadence gates on. */
  private lastRunPollAttemptAtMs = 0;

  private lastSeriesPollAttemptAtMs = 0;

  /**
   * Set while the tab is hidden at the moment a chime-eligible completion is seen, so the tab strip
   * shows the completion even when the sound itself is off or blocked. Restored, and the listener
   * detached, the next time the tab becomes visible.
   */
  private originalDocumentTitleBeforeCompletion: string | null = null;

  titleRestoreVisibilityHandler: (() => void) | null = null;

  sendStartRequest(): void {
    const suiteId = this.launcher.launchSuiteId;
    if (suiteId == null || this.launcher.testedConfigId == null || this.launcher.assessorConfigId == null) return;

    this.startingRun = true;
    this.runErrorMessage = null;
    this.seriesErrorMessage = null;
    this.batteryErrorMessage = null;
    // A new run replaces any re-run state a previous, now-superseded run left behind.
    this.rerunLaunchPending = false;
    this.rerunLaunchedAtMs = null;
    this.rerunScopeOrderIndexes = [];

    const req = this.launcher.buildRunRequest(suiteId, this.launcher.testedConfigId, this.launcher.assessorConfigId);

    // Before the request, not after it: the operator's choices are worth remembering whether or not the
    // server accepts the run.
    this.launcher.persistRunSettings();

    if (this.launcher.isBatteryTarget) {
      this.startBenchmarkBatteryRun(req);
      return;
    }

    // The one branch multi-run adds to the start path. At 1 the request is posted to the same
    // endpoint with the same body it has always carried — runCount and allowCapWait are not even
    // sent — so a single run creates no series and no group, exactly as before.
    if (this.launcher.effectiveRunCount > 1) {
      this.startBenchmarkSeries({
        ...req,
        runCount: this.launcher.effectiveRunCount,
        allowCapWait: this.launcher.allowCapWait
      });
      return;
    }

    this.benchmarkService.startRun(req).subscribe({
      next: (res) => {
        this.startingRun = false;
        this.startOutcome$.next({ kind: 'started' });
        this.lastRunPollError = null;
        this.runQuestionsLoadError = null;
        this.runDiagnosticsCopyFailed = false;
        this.activeRunId = res.runId;
        this.startPolling(res.runId);
        this.workspace.loadHistory();
        this.workspace.loadAllFootprints();
        this.viewSync.notify();
        this.bridge.openRunProgressDialog();
      },
      error: (err) => {
        this.startingRun = false;
        if (this.isSameProviderPrompt(err, req)) {
          this.startOutcome$.next({ kind: 'sameProvider', warning: err.error as SameProviderWarningDto });
        } else {
          this.runErrorMessage = err?.error || 'Failed to start benchmark run.';
        }
        this.viewSync.notify();
      }
    });
  }

  /** Starts the battery run with the launcher's run request as every member's template. */
  private startBenchmarkBatteryRun(run: StartBenchmarkRunRequest): void {
    const batteryId = this.launcher.selectedBatteryId;
    if (batteryId == null) {
      this.startingRun = false;
      return;
    }
    const attach = this.launcher.reuseAttachList;
    this.benchmarkService.startBatteryRun({
      batteryId,
      runsPerSuite: this.launcher.effectiveRunCount,
      allowCapWait: this.launcher.allowCapWait,
      run,
      // Only when reusing, so every other start carries the body it always has.
      ...(attach.length > 0 ? { attach } : {})
    }).subscribe({
      next: (res) => {
        this.startingRun = false;
        this.startOutcome$.next({ kind: 'started' });
        this.lastRunPollError = null;
        this.runQuestionsLoadError = null;
        this.runDiagnosticsCopyFailed = false;
        this.activeBatteryRunId = res.batteryRunId;
        // Reuse is decided per start: the next start asks again.
        this.launcher.reuseEarlierRuns = false;
        this.launcher.refreshReusePreview();
        this.startBatteryPolling(res.batteryRunId);
        this.workspace.loadHistory();
        this.workspace.loadAllFootprints();
        this.workspace.loadRunLimits();
        this.viewSync.notify();
        this.openBatteryDialog();
      },
      error: (err) => {
        this.startingRun = false;
        if (this.isSameProviderPrompt(err, run)) {
          this.startOutcome$.next({ kind: 'sameProvider', warning: err.error as SameProviderWarningDto });
        } else {
          this.runErrorMessage = refusalText(err, 'Failed to start the battery run.');
          // A refusal may name a reused run that stopped qualifying; the preview is judged again.
          this.launcher.refreshReusePreview();
        }
        this.viewSync.notify();
      }
    });
  }

  private startBenchmarkSeries(req: StartBenchmarkRunRequest): void {
    this.benchmarkService.startRunSeries(req).subscribe({
      next: (res) => {
        this.startingRun = false;
        this.startOutcome$.next({ kind: 'started' });
        this.lastRunPollError = null;
        this.runQuestionsLoadError = null;
        this.runDiagnosticsCopyFailed = false;
        this.activeSeriesId = res.seriesId;
        this.startSeriesPolling(res.seriesId);
        this.workspace.loadHistory();
        this.workspace.loadAllFootprints();
        this.workspace.loadRunLimits();
        this.viewSync.notify();
        this.openMultiRunDialog();
      },
      error: (err) => {
        this.startingRun = false;
        if (this.isSameProviderPrompt(err, req)) {
          this.startOutcome$.next({ kind: 'sameProvider', warning: err.error as SameProviderWarningDto });
        } else {
          this.runErrorMessage = err?.error?.message || err?.error || 'Failed to start benchmark run series.';
        }
        this.viewSync.notify();
      }
    });
  }

  /**
   * A 409 that asks for a same-provider acknowledgment. The assessor's never comes for a panel run,
   * where the server blocks self-grading outright; the report writer's can come for any run.
   */
  private isSameProviderPrompt(err: any, req: StartBenchmarkRunRequest): boolean {
    if (err?.status !== 409 || !err.error?.sameProvider) {
      return false;
    }
    return err.error.role === 'reportWriter' || req.coAssessorModelConfigurationId == null;
  }

  /**
   * Reattaches the series banner to a series already executing when the page loads, mirroring
   * checkActiveRun. The dialog stays closed for the same reason: opening a modal unbidden steals
   * focus from whatever the operator was doing.
   */
  checkActiveRunSeries(): void {
    this.benchmarkService.getActiveRunSeries().subscribe({
      next: (series) => {
        if (series) {
          this.activeSeries = series;
          this.activeSeriesId = series.id;
          if (this.seriesIsLive) {
            this.startSeriesPolling(series.id);
          }
          this.viewSync.notify();
        }
      },
      error: (err) => console.error('Failed to check active benchmark run series', err)
    });
  }

  startSeriesPolling(seriesId: number): void {
    this.stopSeriesPolling();
    this.seriesPollFailureCount = 0;
    this.seriesNextPollDueAtMs = 0;
    this.clearLostContact('series');
    this.lockedSeriesId = seriesId;
    this.backgroundActivity.acquireForSeries(seriesId);
    this.lastSeriesPollAttemptAtMs = Date.now();
    this.pollSeries(seriesId);
    this.seriesPollTickerHandle = this.pollTicker.start(BenchmarkActiveRunMonitor.SERIES_POLL_INTERVAL_MS, () => {
      if (Date.now() < this.seriesNextPollDueAtMs) {
        return;
      }
      if (typeof document !== 'undefined' && document.hidden) {
        const hiddenPollDue = (this.launcher.completionSound || this.launcher.completionNotification)
          && (Date.now() - this.lastSeriesPollAttemptAtMs) >= BenchmarkActiveRunMonitor.HIDDEN_POLL_INTERVAL_MS;
        if (!hiddenPollDue) {
          return;
        }
      }
      this.lastSeriesPollAttemptAtMs = Date.now();
      this.pollSeries(seriesId);
    });

    if (typeof document !== 'undefined') {
      this.seriesVisibilityChangeHandler = () => {
        if (!document.hidden) {
          this.pollSeries(seriesId);
        }
      };
      document.addEventListener('visibilitychange', this.seriesVisibilityChangeHandler);
    }
  }

  stopSeriesPolling(): void {
    if (this.seriesPollTickerHandle) {
      this.seriesPollTickerHandle();
      this.seriesPollTickerHandle = null;
    }
    if (this.seriesVisibilityChangeHandler && typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.seriesVisibilityChangeHandler);
      this.seriesVisibilityChangeHandler = null;
    }
    if (this.lockedSeriesId !== null) {
      this.lockedSeriesId = null;
      this.backgroundActivity.release();
      // A run poller still live after its series stopped keeps the tab's lock for itself.
      if (this.pollTickerHandle && this.activeRunId != null) {
        this.backgroundActivity.acquireForRun(this.activeRunId);
      }
    }
  }

  private pollSeries(seriesId: number): void {
    this.benchmarkService.getRunSeries(seriesId).subscribe({
      next: (series) => {
        this.seriesPollFailureCount = 0;
        this.seriesNextPollDueAtMs = 0;
        this.clearLostContact('series');
        this.activeSeries = series;
        // Chimes once per series actually watched live: seriesIsLive keeps re-adding the id while
        // it runs, and the transition into Completed/Cancelled/Failed or Stopped (which needs the
        // operator to continue it) fires the chime only for an id this poller has seen live —
        // never for a series opened from history already finished, nor for one the operator
        // cancelled.
        if (this.seriesIsLive) {
          this.seriesSeenLive.add(series.id);
        } else if (this.seriesSeenLive.has(series.id) && (this.seriesIsFinished || this.seriesIsStopped)) {
          this.seriesSeenLive.delete(series.id);
          if (series.status !== 'Cancelled') {
            this.signalCompletion(`series:${series.id}`);
          }
        }
        // The member currently running is what the single-run banner and dialog describe, so the
        // run poller follows the series rather than being started again per member. A member poller
        // that gave up on failures is restarted here once the server answers again.
        const running = series.members.find(m => formatStatus(m.status) === 'Running');
        if (running && (running.runId !== this.activeRunId || running.runId === this.runPollGaveUpRunId)) {
          this.activeRunId = running.runId;
          this.startPolling(running.runId);
        }
        if (!this.seriesIsLive) {
          this.stopSeriesPolling();
          this.workspace.loadHistory();
          this.workspace.loadRunGroups();
          this.workspace.loadRunLimits();
        }
        this.viewSync.notify();
      },
      error: (err) => {
        console.error('Failed to poll benchmark run series', err);
        if (this.seriesPollFailureCount === 0) {
          this.seriesPollFailureSinceMs = Date.now();
        }
        this.seriesPollFailureCount++;
        const nextDueAtMs = this.noteLostContact('series', seriesId, this.seriesPollFailureCount, this.seriesPollFailureSinceMs);
        if (nextDueAtMs === null) {
          this.stopSeriesPolling();
        } else {
          this.seriesNextPollDueAtMs = nextDueAtMs;
        }
        this.viewSync.notify();
      }
    });
  }

  /** Running, launching or waiting for the cap — anything that is still going to produce members. */
  get seriesIsLive(): boolean {
    const status = this.activeSeries?.status;
    return status === 'Pending' || status === 'Running' || status === 'WaitingForCap';
  }

  /**
   * Terminal: the series will produce nothing more and there is no action left to offer for it.
   * `Stopped` is deliberately not here — it is the one non-terminal end state, and the only one the
   * Continue button exists for.
   */
  get seriesIsFinished(): boolean {
    const status = this.activeSeries?.status;
    return status === 'Completed' || status === 'Cancelled' || status === 'Failed';
  }

  /** Stopped is the one non-terminal end state, and the only one the Continue button appears for. */
  get seriesIsStopped(): boolean {
    return this.activeSeries?.status === 'Stopped';
  }

  openMultiRunDialog(): void {
    this.multiRunDialogVisible = true;
    this.viewSync.notify();
  }

  /**
   * Opens the progress dialog for a series this component is not driving — the Multi-Run Analysis
   * tab's Series badge, which is how a completed series is reached now that its banner hides itself.
   */
  openSeriesDialog(seriesId: number): void {
    this.seriesDialogId = seriesId;
    this.multiRunDialogVisible = true;
    this.viewSync.notify();
  }

  onMultiRunDialogClosed(): void {
    this.returnToSeriesOnClose = false;
    this.multiRunDialogVisible = false;
    this.seriesDialogId = null;
    this.viewSync.notify();
  }

  cancelActiveSeries(): void {
    const seriesId = this.activeSeriesId;
    if (seriesId == null) return;
    this.benchmarkService.cancelRunSeries(seriesId).subscribe({
      next: () => this.pollSeries(seriesId),
      error: (err) => {
        console.error('Failed to cancel benchmark run series', err);
        this.pollSeries(seriesId);
      }
    });
  }

  /**
   * Continues a stopped series. A 409 carrying `instrumentChanged` is not a failure to report as
   * one: the instrument moved while the series was stopped, and continuing anyway is a decision the
   * operator makes with the changed hash named, which is what `acknowledgeInstrumentChange` records.
   */
  resumeActiveSeries(acknowledgeInstrumentChange = false): void {
    const seriesId = this.activeSeriesId;
    if (seriesId == null) return;
    this.armCompletionSignalsFromGesture();
    this.resumingSeries = true;
    this.seriesErrorMessage = null;
    this.benchmarkService.resumeRunSeries(seriesId, { acknowledgeInstrumentChange }).subscribe({
      next: () => {
        this.resumingSeries = false;
        this.startSeriesPolling(seriesId);
        this.viewSync.notify();
      },
      error: (err) => {
        this.resumingSeries = false;
        if (err?.status === 409 && err.error?.instrumentChanged) {
          const changed: string[] = err.error.changedHashes ?? [];
          this.seriesErrorMessage = err.error.message
            || `The instrument changed since this series began (${changed.join(', ')}). `
              + 'Start a new series, or continue anyway — which marks the resulting group cross-condition.';
        } else {
          this.seriesErrorMessage = err?.error?.message || err?.error || 'Failed to continue the series.';
        }
        this.viewSync.notify();
      }
    });
  }

  // --- Battery run banner and polling ---

  /**
   * Reattaches the battery banner to a battery run already live or stopped when the page loads, as
   * checkActiveRunSeries does for a series. The dialog stays closed.
   */
  checkActiveBatteryRun(): void {
    this.benchmarkService.getActiveBatteryRun().subscribe({
      next: (batteryRun) => {
        if (batteryRun) {
          this.activeBatteryRun = batteryRun;
          this.activeBatteryRunId = batteryRun.id;
          if (this.batteryIsLive) {
            this.startBatteryPolling(batteryRun.id);
          }
          this.viewSync.notify();
        }
      },
      error: (err) => console.error('Failed to check active benchmark battery run', err)
    });
  }

  startBatteryPolling(batteryRunId: number): void {
    this.stopBatteryPolling();
    this.batteryPollFailureCount = 0;
    this.batteryNextPollDueAtMs = 0;
    this.clearLostContact('battery');
    this.lockedBatteryRunId = batteryRunId;
    this.backgroundActivity.acquireForBattery(batteryRunId);
    this.lastBatteryPollAttemptAtMs = Date.now();
    this.pollBatteryRun(batteryRunId);
    this.batteryPollTickerHandle = this.pollTicker.start(BenchmarkActiveRunMonitor.SERIES_POLL_INTERVAL_MS, () => {
      if (Date.now() < this.batteryNextPollDueAtMs) {
        return;
      }
      if (typeof document !== 'undefined' && document.hidden) {
        const hiddenPollDue = (this.launcher.completionSound || this.launcher.completionNotification)
          && (Date.now() - this.lastBatteryPollAttemptAtMs) >= BenchmarkActiveRunMonitor.HIDDEN_POLL_INTERVAL_MS;
        if (!hiddenPollDue) {
          return;
        }
      }
      this.lastBatteryPollAttemptAtMs = Date.now();
      this.pollBatteryRun(batteryRunId);
    });

    if (typeof document !== 'undefined') {
      this.batteryVisibilityChangeHandler = () => {
        if (!document.hidden) {
          this.pollBatteryRun(batteryRunId);
        }
      };
      document.addEventListener('visibilitychange', this.batteryVisibilityChangeHandler);
    }
  }

  stopBatteryPolling(): void {
    if (this.batteryPollTickerHandle) {
      this.batteryPollTickerHandle();
      this.batteryPollTickerHandle = null;
    }
    if (this.batteryVisibilityChangeHandler && typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.batteryVisibilityChangeHandler);
      this.batteryVisibilityChangeHandler = null;
    }
    if (this.lockedBatteryRunId !== null) {
      this.lockedBatteryRunId = null;
      this.backgroundActivity.release();
      // A run poller still live after its battery run stopped keeps the tab's lock for itself.
      if (this.pollTickerHandle && this.activeRunId != null) {
        this.backgroundActivity.acquireForRun(this.activeRunId);
      }
    }
  }

  pollBatteryRun(batteryRunId: number): void {
    this.benchmarkService.getBatteryRun(batteryRunId).subscribe({
      next: (batteryRun) => {
        this.batteryPollFailureCount = 0;
        this.batteryNextPollDueAtMs = 0;
        this.clearLostContact('battery');
        this.activeBatteryRun = batteryRun;
        // One signal per battery run watched live, at whatever end it reaches except a cancel. Its
        // members' own completions are accounted for here, so none of them signals afterwards.
        if (this.batteryIsLive) {
          this.batteriesSeenLive.add(batteryRun.id);
        } else if (this.batteriesSeenLive.has(batteryRun.id)) {
          this.batteriesSeenLive.delete(batteryRun.id);
          for (const member of batteryRun.members ?? []) {
            this.runsSeenLive.delete(member.runId);
          }
          if (batteryRun.status !== 'Cancelled') {
            this.signalCompletion(`battery:${batteryRun.id}`);
          }
        }
        // The run banner and dialog follow the member in flight, as they do for a series.
        const runningId = batteryRun.currentRunId;
        if (runningId != null && (runningId !== this.activeRunId || runningId === this.runPollGaveUpRunId)) {
          this.activeRunId = runningId;
          this.startPolling(runningId);
        }
        if (!this.batteryIsLive) {
          this.stopBatteryPolling();
          this.workspace.loadHistory();
          this.workspace.loadRunLimits();
        }
        this.viewSync.notify();
      },
      error: (err) => {
        console.error('Failed to poll benchmark battery run', err);
        if (this.batteryPollFailureCount === 0) {
          this.batteryPollFailureSinceMs = Date.now();
        }
        this.batteryPollFailureCount++;
        const nextDueAtMs = this.noteLostContact('battery', batteryRunId, this.batteryPollFailureCount, this.batteryPollFailureSinceMs);
        if (nextDueAtMs === null) {
          this.stopBatteryPolling();
        } else {
          this.batteryNextPollDueAtMs = nextDueAtMs;
        }
        this.viewSync.notify();
      }
    });
  }

  /** Pending, Running or WaitingForCap: still going to launch members. */
  get batteryIsLive(): boolean {
    const status = this.activeBatteryRun?.status;
    return status === 'Pending' || status === 'Running' || status === 'WaitingForCap';
  }

  // --- Lost contact ---

  /**
   * Records a series or battery poller's failed poll and returns when its next attempt is due
   * (client clock, ms), or null once the failures have lasted {@link LOST_CONTACT_GIVE_UP_MS}. The
   * notice appears once {@link LOST_CONTACT_NOTICE_AFTER_FAILURES} polls in a row have failed.
   */
  private noteLostContact(kind: 'series' | 'battery', id: number, failureCount: number, sinceMs: number): number | null {
    const now = Date.now();
    const backoff = BenchmarkActiveRunMonitor.LOST_CONTACT_BACKOFF_MS;
    const retryIntervalMs = backoff[Math.min(failureCount, backoff.length) - 1];
    const gaveUp = now - sinceMs >= BenchmarkActiveRunMonitor.LOST_CONTACT_GIVE_UP_MS;
    if (gaveUp || failureCount >= BenchmarkActiveRunMonitor.LOST_CONTACT_NOTICE_AFTER_FAILURES) {
      this.lostContact = { kind, id, failureCount, sinceMs, retryIntervalMs: gaveUp ? null : retryIntervalMs, gaveUp };
    }
    return gaveUp ? null : now + retryIntervalMs;
  }

  private clearLostContact(kind: 'series' | 'battery'): void {
    if (this.lostContact?.kind === kind) {
      this.lostContact = null;
    }
  }

  /** The Lost contact notice's sentence, or null while both pollers reach the server. */
  get lostContactText(): string | null {
    const notice = this.lostContact;
    if (!notice) return null;
    const subject = notice.kind === 'battery' ? `Battery Run #${notice.id}` : `Series #${notice.id}`;
    const giveUpMinutes = BenchmarkActiveRunMonitor.LOST_CONTACT_GIVE_UP_MS / 60_000;
    if (notice.gaveUp) {
      return `Lost contact with the server for ${giveUpMinutes} minutes, so this page stopped following ${subject}. `
        + 'It may still be running on the server; reload the page to reattach.';
    }
    return `Lost contact with the server: the last ${notice.failureCount} polls for ${subject} failed. `
      + `Retrying every ${notice.retryIntervalMs! / 1000} s for up to ${giveUpMinutes} minutes.`;
  }

  /** Opens the Battery Progress dialog on the live battery run, or on one the Multi-Suite tab names. */
  openBatteryDialog(batteryRunId?: number): void {
    if (batteryRunId != null && batteryRunId !== this.activeBatteryRunId) {
      this.batteryDialogRunId = batteryRunId;
    }
    this.batteryDialogVisible = true;
    this.viewSync.notify();
  }

  onBatteryDialogClosed(): void {
    this.returnToBatteryOnClose = false;
    this.batteryDialogVisible = false;
    this.batteryDialogRunId = null;
    this.viewSync.notify();
  }

  cancelActiveBattery(): void {
    const batteryRunId = this.activeBatteryRunId;
    if (batteryRunId == null) return;
    this.benchmarkService.cancelBatteryRun(batteryRunId).subscribe({
      next: () => this.pollBatteryRun(batteryRunId),
      error: (err) => {
        console.error('Failed to cancel benchmark battery run', err);
        this.batteryErrorMessage = refusalText(err, 'Failed to cancel the battery run.');
        this.pollBatteryRun(batteryRunId);
      }
    });
  }

  /**
   * Continues a stopped battery run, or re-runs it under the current instrument after an instrument
   * change, which supersedes its completed members.
   */
  resumeActiveBattery(mode: BenchmarkBatteryResumeMode = 'Continue'): void {
    const batteryRunId = this.activeBatteryRunId;
    if (batteryRunId == null) return;
    this.armCompletionSignalsFromGesture();
    this.resumingBattery = true;
    this.batteryErrorMessage = null;
    this.benchmarkService.resumeBatteryRun(batteryRunId, mode).subscribe({
      next: () => {
        this.resumingBattery = false;
        this.startBatteryPolling(batteryRunId);
        this.viewSync.notify();
      },
      error: (err) => {
        this.resumingBattery = false;
        this.batteryErrorMessage = refusalText(err, 'Failed to continue the battery run.');
        this.pollBatteryRun(batteryRunId);
        this.viewSync.notify();
      }
    });
  }

  cancelActiveRun() {
    if (!this.activeRunId) return;
    const runId = this.activeRunId;
    this.noteOperatorCancel(runId);
    this.benchmarkService.cancelRun(runId).subscribe({
      next: () => {
        this.pollRunDetail(this.activeRunId!);
      },
      error: (err) => {
        this.operatorCancelledRunIds.delete(runId);
        console.error('Failed to cancel run', err);
      }
    });
  }

  /**
   * Recorded before the request, because the interval poll can see the terminal status before the
   * cancel response arrives. Only a run watched live is recorded, so every entry is consumed.
   */
  noteOperatorCancel(runId: number): void {
    if (this.runsSeenLive.has(runId)) {
      this.operatorCancelledRunIds.add(runId);
    }
  }

  startPolling(runId: number) {
    this.stopPolling();
    this.runPollFailureCount = 0;
    this.runPollGaveUpRunId = null;
    if (this.activeRunReportJob?.runId !== runId) {
      this.activeRunReportJob = null;
    }
    if (this.lockedSeriesId === null && this.lockedBatteryRunId === null) {
      this.backgroundActivity.acquireForRun(runId);
    }
    this.lastRunPollAttemptAtMs = Date.now();
    this.pollRunDetail(runId);
    this.pollTickerHandle = this.pollTicker.start(BenchmarkActiveRunMonitor.RUN_POLL_INTERVAL_MS, () => {
      if (typeof document !== 'undefined' && document.hidden) {
        const hiddenPollDue = (this.launcher.completionSound || this.launcher.completionNotification)
          && (Date.now() - this.lastRunPollAttemptAtMs) >= BenchmarkActiveRunMonitor.HIDDEN_POLL_INTERVAL_MS;
        if (!hiddenPollDue) {
          return;
        }
      }
      this.lastRunPollAttemptAtMs = Date.now();
      this.pollRunDetail(runId);
    });

    if (typeof document !== 'undefined') {
      this.runVisibilityChangeHandler = () => {
        if (!document.hidden) {
          this.pollRunDetail(runId);
        }
      };
      document.addEventListener('visibilitychange', this.runVisibilityChangeHandler);
    }
  }

  stopPolling() {
    if (this.pollTickerHandle) {
      this.pollTickerHandle();
      this.pollTickerHandle = null;
    }
    this.runReportJobSub?.unsubscribe();
    this.runReportJobSub = null;
    if (this.runVisibilityChangeHandler && typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.runVisibilityChangeHandler);
      this.runVisibilityChangeHandler = null;
    }
    if (this.lockedSeriesId === null && this.lockedBatteryRunId === null) {
      this.backgroundActivity.release();
    }
  }

  startRunElapsedTicker(): void {
    this.stopRunElapsedTicker();
    this.runElapsedInterval = setInterval(() => {
      if (typeof document !== 'undefined' && document.hidden) {
        return;
      }
      this.viewSync.notify();
    }, BenchmarkActiveRunMonitor.RUN_ELAPSED_TICK_MS);
  }

  stopRunElapsedTicker(): void {
    if (this.runElapsedInterval) {
      clearInterval(this.runElapsedInterval);
      this.runElapsedInterval = null;
    }
  }

  /**
   * The run half of transition detection: chimes only for an id this poller watched Running, and
   * only when it is not a member of a series still live — a series chimes once for the whole
   * group instead, via `signalCompletion` in `pollSeries`. A run ended by cancellation does not
   * signal at all.
   */
  private maybeSignalRunCompletion(run: BenchmarkRunDetailDto): void {
    if (!this.runsSeenLive.has(run.id)) return;
    this.runsSeenLive.delete(run.id);
    const cancelledByOperator = this.operatorCancelledRunIds.delete(run.id);
    if (this.activeSeries != null && this.seriesIsLive) return;
    if (this.activeBatteryRun != null && this.batteryIsLive) return;
    if (cancelledByOperator || this.runEndedByCancellation(run)) return;
    this.signalCompletion(`run:${run.id}`);
  }

  /**
   * `Canceled`, or a member of a series the operator cancelled: a member finishing just as its
   * series is cancelled can end `Completed`, after the live-series guard no longer applies.
   */
  private runEndedByCancellation(run: BenchmarkRunDetailDto): boolean {
    if (formatStatus(run.status) === 'Canceled') return true;
    const series = this.activeSeries;
    if (series?.status === 'Cancelled' && series.members.some(m => m.runId === run.id)) return true;
    const batteryRun = this.activeBatteryRun;
    return batteryRun?.status === 'Cancelled' && (batteryRun.members ?? []).some(m => m.runId === run.id);
  }

  /**
   * Marks the tab title even when both signals are off, so a hidden tab shows the completion
   * either way. The sound and the notification are then handled independently — either, both or
   * neither may be on, and the notification does not require the sound to have run. The
   * notification fires whenever the checkbox is ticked, whatever the tab's own focus: a completion
   * is worth surfacing on the desktop even for an operator looking straight at the tab, and a tab
   * that merely lacks focus (another window in front, not actually hidden) is not a case worth
   * special-casing away.
   */
  private signalCompletion(key: string): void {
    this.markTabTitleForCompletion();

    if (this.launcher.completionSound) {
      this.completionSoundService.play(key).then(outcome => {
        this.lastCompletionSoundOutcome = outcome;
        if (outcome === 'played') {
          this.completionSoundStatus = null;
        } else if (outcome === 'blocked') {
          this.completionSoundStatus = 'Playback was blocked by the browser — press Test sound once to allow it.';
        } else if (outcome === 'deferred') {
          this.completionSoundStatus = 'The browser held the sound until this tab was shown.';
        }
        this.viewSync.notify();
      });
    }

    if (this.launcher.completionNotification) {
      const body = this.completionNotificationBody(key);
      if (body) {
        const hidden = typeof document !== 'undefined' ? document.hidden : false;
        const focused = typeof document !== 'undefined' ? document.hasFocus() : true;
        const outcome = this.completionNotificationService.notify(key, 'AI Benchmark', body);
        this.recordNotificationAttempt({ atUtc: new Date().toISOString(), key, hidden, focused, outcome });
      }
    }
  }

  /** Keeps the last {@link MAX_NOTIFICATION_ATTEMPTS} notification attempts for the diagnostics capture. */
  private recordNotificationAttempt(attempt: {
    atUtc: string; key: string; hidden: boolean; focused: boolean; outcome: BenchmarkNotifyOutcome;
  }): void {
    this.notificationAttempts.push(attempt);
    if (this.notificationAttempts.length > BenchmarkActiveRunMonitor.MAX_NOTIFICATION_ATTEMPTS) {
      this.notificationAttempts.shift();
    }
  }

  /**
   * `Run #54 — <suite name> — <status>`, `Series #N — k of n runs — <status>` or
   * `Battery #N — <battery name> — k of K suites — <status>`.
   */
  private completionNotificationBody(key: string): string | null {
    if (key.startsWith('run:')) {
      const run = this.activeRunDetail;
      if (!run) return null;
      return `Run #${run.id} — ${run.suiteName} — ${formatStatus(run.status)}`;
    }
    if (key.startsWith('series:')) {
      const series = this.activeSeries;
      if (!series) return null;
      return `Series #${series.id} — ${series.completedRunCount} of ${series.requestedRunCount} runs — ${series.status}`;
    }
    if (key.startsWith('battery:')) {
      const batteryRun = this.activeBatteryRun;
      if (!batteryRun) return null;
      return `Battery #${batteryRun.id} — ${batteryRun.batteryName} — `
        + `${batteryRun.completedSuiteCount} of ${batteryRun.suiteCount} suites — ${batteryRun.status}`;
    }
    return null;
  }

  /**
   * Arms the completion sound under the calling handler's own user gesture, so the browser does
   * not defer this tab's playback to gesture-less code that later runs while the tab is hidden.
   * Skipped entirely when neither completion signal is enabled, since there is nothing to arm
   * for. Every call site is a synchronous `void` method bound directly to a template `(click)`,
   * never behind an awaited dialog, so the call runs inside the gesture.
   */
  armCompletionSignalsFromGesture(): void {
    if (!this.launcher.completionSound && !this.launcher.completionNotification) return;
    void this.completionSoundService.arm();
    this.settleNotificationPermissionFromGesture();
  }

  /**
   * A ticked notification box restored from saved settings may meet a browser that was never
   * asked (another browser or profile, or cleared site data). The prompt is then shown here, under
   * the Start gesture, so the run does not end in a notification that silently never fires. A
   * browser that already decided is not asked again: `granted` changes nothing, and a refusal
   * unticks the box with the reason.
   */
  private settleNotificationPermissionFromGesture(): void {
    if (!this.launcher.completionNotification) return;
    const permission = this.completionNotificationService.permission();
    if (permission === 'granted') return;
    if (permission !== 'default') {
      this.applyNotificationPermissionOutcome(permission);
      return;
    }
    this.completionNotificationService.requestPermission().then(outcome => {
      this.applyNotificationPermissionOutcome(outcome);
      this.viewSync.notify();
    });
  }

  /**
   * The desktop-notification checkbox's own change handler. Ticking it asks for permission at
   * once, while the operator is at the screen; the Start gesture asks only when this browser has
   * not decided yet. Never asked on page load. A result other than `granted` unticks the box and
   * explains why in the status line beside the checkboxes.
   */
  onCompletionNotificationChange(checked: boolean): void {
    if (!checked) {
      this.launcher.completionNotification = false;
      this.completionNotificationStatus = null;
      this.viewSync.notify();
      return;
    }

    this.completionNotificationService.requestPermission().then(outcome => {
      this.applyNotificationPermissionOutcome(outcome);
      this.viewSync.notify();
    });
  }

  private applyNotificationPermissionOutcome(outcome: BenchmarkNotificationPermissionOutcome): void {
    if (outcome === 'granted') {
      this.launcher.completionNotification = true;
      this.completionNotificationStatus = null;
      return;
    }
    this.launcher.completionNotification = false;
    this.completionNotificationStatus = outcome === 'denied'
      ? "Notifications are blocked for this site in the browser's settings."
      : outcome === 'default'
        ? 'The permission prompt was dismissed.'
        : 'This browser does not support desktop notifications here.';
  }

  /**
   * Prefixes the document title with a checkmark while the tab is hidden, so the tab strip shows a
   * run or series finished even when the operator never hears it. Restored, and the listener
   * detached, on the next visibilitychange that finds the tab visible again.
   */
  private markTabTitleForCompletion(): void {
    if (typeof document === 'undefined' || !document.hidden) return;
    if (this.originalDocumentTitleBeforeCompletion === null) {
      this.originalDocumentTitleBeforeCompletion = document.title;
      document.title = '✓ ' + document.title;
    }
    if (!this.titleRestoreVisibilityHandler) {
      this.titleRestoreVisibilityHandler = () => {
        if (document.hidden) return;
        if (this.originalDocumentTitleBeforeCompletion !== null) {
          document.title = this.originalDocumentTitleBeforeCompletion;
          this.originalDocumentTitleBeforeCompletion = null;
        }
        if (this.titleRestoreVisibilityHandler) {
          document.removeEventListener('visibilitychange', this.titleRestoreVisibilityHandler);
          this.titleRestoreVisibilityHandler = null;
        }
      };
      document.addEventListener('visibilitychange', this.titleRestoreVisibilityHandler);
    }
  }

  private pollRunDetail(runId: number) {
    this.benchmarkService.getRun(runId).subscribe({
      next: (run) => {
        this.lastRunPollAtUtc = new Date().toISOString();
        this.lastRunPollError = null;
        this.runPollFailureCount = 0;
        this.activeRunDetail = run;
        this.runDiagnosticsPanelCapturedAt = new Date();
        const statusStr = formatStatus(run.status);
        if (statusStr === 'Running') {
          this.runsSeenLive.add(run.id);
          this.rerunLaunchPending = false;
          this.rerunLaunchedAtMs = null;
          if (this.runTerminalSeenAt?.runId === run.id) {
            this.runTerminalSeenAt = null;
          }
          if (this.isRunProgressDialogOpen && !this.runElapsedInterval) {
            this.startRunElapsedTicker();
          }
        } else if (this.rerunLaunchPending) {
          // A re-run's first poll or two can still see the previous attempt's terminal status:
          // the server only flips the row to Running from inside the background task it starts.
          // Stay non-terminal and keep polling until either a rerunStartedAtUtc stamp at or after
          // the launch proves this status is fresh, or the grace period runs out.
          const rerunStartedMs = run.rerunStartedAtUtc ? parseServerUtcDate(run.rerunStartedAtUtc).getTime() : NaN;
          const stampedAfterLaunch = !Number.isNaN(rerunStartedMs) && rerunStartedMs >= this.rerunLaunchedAtMs! - 5000;
          const graceExpired = Date.now() - this.rerunLaunchedAtMs! > BenchmarkActiveRunMonitor.RERUN_LAUNCH_GRACE_MS;
          if (stampedAfterLaunch || graceExpired) {
            this.rerunLaunchPending = false;
            this.rerunLaunchedAtMs = null;
            if (!stampedAfterLaunch && graceExpired) {
              this.runErrorMessage = 'The failed-question re-run did not report starting within 60 seconds. '
                + 'Check the run history; if the run is still listed as running, reopen it from the banner.';
            }
            // The re-run's own terminal status: its report stage's grace starts now.
            this.runTerminalSeenAt = null;
            this.onRunTerminalPoll(run);
          } else {
            this.viewSync.notify();
            return;
          }
        } else {
          this.onRunTerminalPoll(run);
        }
        this.viewSync.notify();
      },
      error: (err) => {
        this.lastRunPollAtUtc = new Date().toISOString();
        const httpStatus = err?.status ? ` (HTTP ${err.status})` : '';
        const msg = typeof err?.error === 'string' ? err.error : (err?.error?.message || err?.message || 'Polling failed');
        this.lastRunPollError = `${msg}${httpStatus}`;
        console.error('Failed to poll run detail', err);
        this.runPollFailureCount++;
        // One failed poll is noise; only a run of MAX_CONSECUTIVE_POLL_FAILURES actually stops the
        // tab's only view of the run, and rerunLaunchPending is cleared at the same moment, not
        // on every transient failure.
        if (this.runPollFailureCount >= BenchmarkActiveRunMonitor.MAX_CONSECUTIVE_POLL_FAILURES) {
          this.rerunLaunchPending = false;
          this.rerunLaunchedAtMs = null;
          this.stopPolling();
          this.runPollGaveUpRunId = runId;
        }
      }
    });
  }

  /**
   * A terminal poll. A Completed run that names a report writer keeps its poll going through stage 4,
   * the writing of its AI-written reports, and its completion signal waits for that stage to end;
   * every other run stops polling and signals at once.
   */
  private onRunTerminalPoll(run: BenchmarkRunDetailDto): void {
    const firstSeen = this.runTerminalSeenAt?.runId !== run.id;
    if (firstSeen) {
      this.runTerminalSeenAt = { runId: run.id, atMs: Date.now() };
    }
    this.runReportGraceOpen = Date.now() - this.runTerminalSeenAt!.atMs < BenchmarkActiveRunMonitor.RUN_REPORT_STAGE_GRACE_MS;

    if (this.runAwaitsReports(run)) {
      if (firstSeen) {
        this.workspace.loadHistory();
      }
      this.pollActiveRunReportJob(run.id);
      return;
    }

    this.stopPolling();
    this.stopRunElapsedTicker();
    this.workspace.loadHistory();
    this.maybeSignalRunCompletion(run);
  }

  /**
   * The run's stage 4 is still to come or under way: it ended Completed, names a report writer, and its
   * reports are Pending or Writing, or still NotRequested within the start grace.
   */
  runAwaitsReports(run: BenchmarkRunDetailDto): boolean {
    if (run.reportWriterModelConfigurationId == null || formatStatus(run.status) !== 'Completed') {
      return false;
    }
    const status = reportDocumentsStatusOf(run);
    if (status === BenchmarkRunReportDocumentsStatus.Pending || status === BenchmarkRunReportDocumentsStatus.Writing) {
      return true;
    }
    return status === BenchmarkRunReportDocumentsStatus.NotRequested
      && this.runReportGraceOpen
      && this.runTerminalSeenAt?.runId === run.id;
  }

  /** One request at a time: a slow answer is superseded by the next poll's. A failure leaves the last view. */
  private pollActiveRunReportJob(runId: number): void {
    this.runReportJobSub?.unsubscribe();
    this.runReportJobSub = this.benchmarkService.getRunReportJob(runId).subscribe({
      next: view => {
        if (this.activeRunDetail?.id !== runId) return;
        this.activeRunReportJob = view;
        this.activeRunReportJobReceivedAtMs = Date.now();
        this.viewSync.notify();
      },
      error: err => console.warn('Failed to poll the run report writing job', err)
    });
  }

  /**
   * Order indexes a failed-question re-run is repairing, captured when the re-run is launched.
   * The row list itself is unchanged: every answer of the run stays listed, and a question
   * outside the re-run keeps the status it already has. Empty when no re-run is in progress.
   */
  rerunScopeOrderIndexes: number[] = [];

  /**
   * True from the moment a failed-question re-run is requested until a poll reports the run
   * Running, or reports a terminal status stamped by this launch, or the request is refused.
   * While true the dialog is neither running nor terminal: it is starting.
   */
  rerunLaunchPending = false;

  private rerunLaunchedAtMs: number | null = null;

  private static readonly RERUN_LAUNCH_GRACE_MS = 60_000;

  runDiagnosticsCopyFailed = false;

  loadRunProgressQuestions(suiteId: number): void {
    // Claimed before the request so a second open while it is in flight does not refire it.
    this.runProgressQuestionsSuiteId = suiteId;
    this.benchmarkService.getQuestions(suiteId).subscribe({
      next: (data) => {
        this.runQuestionsLoadError = null;
        this.runProgressQuestions = data;
        this.viewSync.notify();
      },
      error: (err) => {
        // The dialog degrades to the answers alone rather than failing to open.
        this.runProgressQuestionsSuiteId = null;
        const msg = typeof err?.error === 'string' ? err.error : (err?.error?.message || err?.message || 'Failed to load suite questions');
        this.runQuestionsLoadError = msg;
        console.error('Failed to load suite questions for run progress', err);
      }
    });
  }

  /**
   * Reattaches the banner to a run already executing when the admin page loads. The dialog
   * stays closed — opening a modal unbidden would steal focus from whatever the admin was
   * doing. The operator reopens it from the banner.
   */
  checkActiveRun(): void {
    this.benchmarkService.getActiveRun().subscribe({
      next: (res) => {
        if (res && res.runId != null) {
          this.activeRunId = res.runId;
          this.startPolling(res.runId);
          this.viewSync.notify();
        }
      },
      error: (err) => console.error('Failed to check active benchmark run', err)
    });
  }

  /**
   * The one re-run launch path. Both entry points clear the stale error and question-load state
   * before the request, because a re-run of a run that ended in an error would otherwise open
   * showing the previous attempt's error. The loaded run detail itself is kept rather than
   * nulled — it is what keeps the header (run number, suite, profile, model strip) legible
   * during the launch — and `rerunLaunchPending` is what suppresses the previous attempt's
   * stage label, terminal footer, and failure alert until a poll confirms the re-run has
   * actually started.
   */
  launchFailedQuestionRerun(runId: number, failedOrderIndexes: number[]): void {
    this.operatorCancelledRunIds.delete(runId);
    this.rerunScopeOrderIndexes = failedOrderIndexes;
    this.rerunLaunchPending = true;
    this.rerunLaunchedAtMs = Date.now();
    this.runErrorMessage = null;
    this.runQuestionsLoadError = null;
    this.activeRunId = runId;

    if (!this.isRunProgressDialogOpen) {
      this.bridge.openRunProgressDialog();
    }

    this.benchmarkService.rerunFailedQuestions(runId).subscribe({
      next: () => {
        this.activeRunId = runId;
        this.startPolling(runId);
        this.workspace.loadHistory();
        this.viewSync.notify();
      },
      error: (err) => {
        // Surfaced in the dialog rather than only in the run-detail view: the dialog is now
        // where the operator is watching from, and a refusal there was previously invisible.
        this.rerunLaunchPending = false;
        this.rerunLaunchedAtMs = null;
        this.rerunScopeOrderIndexes = [];
        this.runErrorMessage = typeof err?.error === 'string'
          ? err.error
          : (err?.error?.message || err?.message || 'Failed to re-run failed questions.');
        this.viewSync.notify();
      }
    });
  }

  ngOnDestroy(): void {
    this.stopPolling();
    this.stopRunElapsedTicker();
    this.stopSeriesPolling();
    this.stopBatteryPolling();
    if (this.titleRestoreVisibilityHandler && typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.titleRestoreVisibilityHandler);
      this.titleRestoreVisibilityHandler = null;
    }
  }
}
