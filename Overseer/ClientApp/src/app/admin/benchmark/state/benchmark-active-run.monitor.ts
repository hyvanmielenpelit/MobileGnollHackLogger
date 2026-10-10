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
  BenchmarkRunReportJobDto,
  BenchmarkModelBatchRunDto
} from '../../../services/admin-benchmark.service';
import {
  BenchmarkCompletionSoundKind,
  BenchmarkCompletionSoundOutcome,
  BenchmarkCompletionSoundService
} from '../../../services/benchmark-completion-sound.service';
import {
  BenchmarkCompletionNotificationService,
  BenchmarkNotificationPermissionOutcome,
  BenchmarkNotifyOutcome
} from '../../../services/benchmark-completion-notification.service';
import { BenchmarkBackgroundActivityService } from '../../../services/benchmark-background-activity.service';
import { BenchmarkPollTickerService, BenchmarkPollTickerHandle } from '../../../services/benchmark-poll-ticker.service';
import { parseServerUtcDate } from '../../../utils/date.util';
import { ELAPSED_TICK_MS, startElapsedTicker } from '../../../utils/elapsed-ticker';
import { Subject, Subscription } from 'rxjs';
import { refusalText, reportDocumentsStatusOf, formatStatus } from '../benchmark-run-format';
import { batteryAwaitsPostRun, batteryReportDocumentsStatusName, isLiveBatteryRunStatus } from '../batteries/battery.models';
import { BenchmarkWorkspaceStore } from './benchmark-workspace.store';
import { BenchmarkLauncherState } from './benchmark-launcher.state';
import { BenchmarkViewSync } from './benchmark-view-sync.service';
import { BenchmarkShellBridge } from './benchmark-shell-bridge.service';
import {
  BenchmarkEndKind,
  batteryEndBody,
  benchmarkEndSignal,
  modelBatchEndBody,
  modelBatchSignalKey,
  runEndBody,
  seriesEndBody
} from './benchmark-end-signal';
import { isLiveModelBatchStatus } from '../model-batch/model-batch.models';

/** How a start request ended: started, or held for a same-provider acknowledgment. */
export type BenchmarkStartOutcome = { kind: 'started' } | { kind: 'sameProvider'; warning: SameProviderWarningDto };

/** The pollers that back off rather than give up at once: a series, a battery run, a model batch. */
export type BenchmarkLostContactKind = 'series' | 'battery' | 'modelBatch';

/** A series, battery or model batch poller that keeps failing to reach the server, for the Lost contact notice. */
export interface BenchmarkLostContactNotice {
  readonly kind: BenchmarkLostContactKind;
  /** The series id, battery run id or model batch id the poller follows. */
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

/**
 * Starts runs, series and battery runs, follows them and model batches with their pollers, and signals
 * their ends on every sub-tab: the completion chime or the failure sound, by `benchmarkEndSignal`.
 */
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

  static readonly RUN_ELAPSED_TICK_MS = ELAPSED_TICK_MS;

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

  /** Stops the dialog's elapsed ticker; null while none runs. */
  runElapsedInterval: (() => void) | null = null;

  lastRunPollAtUtc: string | null = null;

  lastRunPollError: string | null = null;

  runPollFailureCount = 0;

  /**
   * The member run whose poller gave up on failures while its series or battery poller kept going;
   * that poller's next successful poll restarts it.
   */
  private runPollGaveUpRunId: number | null = null;

  runQuestionsLoadError: string | null = null;

  private liveRunId: number | null = null;

  /**
   * The live run: the one this page started, reattached to, or follows as a series or battery member.
   * The banner, the completion signals and the report-job poll follow it.
   */
  get activeRunId(): number | null {
    return this.liveRunId;
  }

  set activeRunId(runId: number | null) {
    this.liveRunId = runId;
    this.syncViewedPoller();
  }

  activeRunDetail: BenchmarkRunDetailDto | null = null;

  pollTickerHandle: BenchmarkPollTickerHandle | null = null;

  /** Bumped by every start and stop of the run poller; a response from an earlier one is discarded. */
  private runPollGeneration = 0;

  // --- The viewed run ---
  //
  // The run progress dialog shows `viewedRunId` when one is set, else the live run. While the viewed
  // run is not the live run, a reduced poller of its own follows it: it never signals completion,
  // takes the background lock, applies the re-run grace or loads history.

  /** The run the progress dialog was opened for; null while the dialog follows the live run. */
  viewedRunId: number | null = null;

  /** The viewed run's detail, polled while it is not the live run. */
  viewedRunDetail: BenchmarkRunDetailDto | null = null;

  /** The viewed run's report writing job, polled alongside it while its stage 4 is current. */
  viewedRunReportJob: BenchmarkRunReportJobDto | null = null;

  /** When the viewed run's last job view arrived (client clock). */
  viewedRunReportJobReceivedAtMs = 0;

  private viewedPollTickerHandle: BenchmarkPollTickerHandle | null = null;

  /** The run the viewed poller was last started for; kept after it stops itself on a finished run. */
  private viewedPollRunId: number | null = null;

  /** Bumped by every start and stop of the viewed poller; a response from an earlier one is discarded. */
  private viewedPollGeneration = 0;

  private viewedPollFailureCount = 0;

  /** When the viewed run was first seen terminal (client clock), for its report stage's start grace. */
  private viewedTerminalSeenAt: { runId: number; atMs: number } | null = null;

  private viewedReportJobSub: Subscription | null = null;

  /** The progress dialog shows the live run. */
  get dialogFollowsLiveRun(): boolean {
    return this.viewedRunId == null || this.viewedRunId === this.activeRunId;
  }

  /** The run the progress dialog shows. */
  get dialogRunId(): number | null {
    return this.viewedRunId ?? this.activeRunId;
  }

  /** The detail of the run the progress dialog shows; null until it has arrived. */
  get dialogRunDetail(): BenchmarkRunDetailDto | null {
    const viewed = this.viewedRunId;
    if (viewed == null) {
      return this.activeRunDetail;
    }
    if (viewed === this.activeRunId) {
      return this.activeRunDetail?.id === viewed ? this.activeRunDetail : null;
    }
    return this.viewedRunDetail?.id === viewed ? this.viewedRunDetail : null;
  }

  /** The report writing job of the run the progress dialog shows. */
  get dialogRunReportJob(): BenchmarkRunReportJobDto | null {
    const viewed = this.viewedRunId;
    if (viewed == null) {
      return this.activeRunReportJob;
    }
    if (viewed === this.activeRunId) {
      return this.activeRunReportJob?.runId === viewed ? this.activeRunReportJob : null;
    }
    return this.viewedRunReportJob?.runId === viewed ? this.viewedRunReportJob : null;
  }

  /** When the dialog's job view arrived (client clock), to advance the server's clock between polls. */
  get dialogRunReportJobReceivedAtMs(): number {
    return this.dialogFollowsLiveRun ? this.activeRunReportJobReceivedAtMs : this.viewedRunReportJobReceivedAtMs;
  }

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

  /**
   * Battery runs this page saw move: a poll found them live or with post-run work, a start or
   * Continue request for them succeeded, or a member's re-run handed its signal to them
   * (`batteryTakesRunSignal`). A poll that finds one settled signals it, once.
   */
  batteriesSeenLive = new Set<number>();

  /**
   * For a run seen Running that belongs to a battery run: that battery run and its status then (null
   * while unknown). The run's terminal poll compares the battery run against it.
   */
  private readonly runBatteryAtStart = new Map<number, { batteryRunId: number; status: string | null }>();

  /** The Battery Progress dialog's visibility. The dialog element itself belongs to that component. */
  batteryDialogVisible = false;

  /** A battery run opened from the Multi-Suite tab rather than the one this page drives. */
  batteryDialogRunId: number | null = null;

  batteryErrorMessage: string | null = null;

  resumingBattery = false;

  /** The battery run whose Battery Progress dialog the run progress dialog was opened from; closing reopens it. */
  returnToBatteryRunId: number | null = null;

  // --- Model batches ---
  //
  // A batch runs its members one after another. Its banner and its signals stand for its members:
  // while the page follows a batch, none of its runs, series or battery runs signals its own end.

  /** The id of the batch the banner follows. */
  activeModelBatchRunId: number | null = null;

  /** The live or resumable batch the page follows; kept once it ends, for the banner's last state. */
  activeModelBatch: BenchmarkModelBatchRunDto | null = null;

  /** The Model Batch Progress dialog's visibility. The dialog element itself belongs to that component. */
  modelBatchDialogVisible = false;

  /** The batch the dialog shows: the active one, or one Run History opened. */
  modelBatchDialogId: number | null = null;

  modelBatchErrorMessage: string | null = null;

  /**
   * The batch whose progress dialog a member's run or battery progress dialog was opened from;
   * closing that dialog reopens the batch's.
   */
  returnToModelBatchId: number | null = null;

  /** Batches this page saw live; a poll that finds one ended signals it, once. */
  readonly batchesSeenLive = new Set<number>();

  /** Every batch this page followed live: their members never signal their own ends. */
  private readonly modelBatchesFollowed = new Set<number>();

  /** Member runs the operator re-ran by hand; their ends signal as any run's do. */
  private readonly operatorRerunRunIds = new Set<number>();

  private modelBatchPollTickerHandle: BenchmarkPollTickerHandle | null = null;

  private modelBatchPollFailureCount = 0;

  private modelBatchPollFailureSinceMs = 0;

  /** While the batch poller is failing, ticks before this time (client clock, ms) are skipped. */
  private modelBatchNextPollDueAtMs = 0;

  private modelBatchVisibilityChangeHandler: (() => void) | null = null;

  private lastModelBatchPollAttemptAtMs = 0;

  /** Pending, Running or WaitingForCap: the batch may still launch a member. */
  get modelBatchIsLive(): boolean {
    return isLiveModelBatchStatus(this.activeModelBatch?.status);
  }

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

  /** `'error'` when `play()` itself rejected, which the service is never meant to do. */
  lastCompletionSoundOutcome: BenchmarkCompletionSoundOutcome | 'error' | null = null;

  /** The reason a notification permission request did not end in `completionNotification` being on. */
  completionNotificationStatus: string | null = null;

  /** One `notify()` call per record, kept for the run diagnostics capture; last 10, oldest dropped first. */
  readonly notificationAttempts: {
    atUtc: string; key: string; hidden: boolean; focused: boolean; outcome: BenchmarkNotifyOutcome; kind: BenchmarkCompletionSoundKind;
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
        this.batteriesSeenLive.add(res.batteryRunId);
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
        // Signals once per series actually watched live: seriesIsLive keeps re-adding the id while
        // it runs, and the transition into an end, Stopped included (which needs the operator to
        // continue it), signals only for an id this poller has seen live — never for a
        // series opened from history already finished. The end rule picks the sound and silences a
        // cancel; a member of a followed model batch leaves its end to the batch.
        if (this.seriesIsLive) {
          this.seriesSeenLive.add(series.id);
        } else if (this.seriesSeenLive.has(series.id)
          && (this.seriesIsFinished || this.seriesIsStopped || series.status === 'CompletedWithErrors')) {
          this.seriesSeenLive.delete(series.id);
          if (!this.memberOfFollowedModelBatch(series.modelBatchRunId)) {
            this.signalEndOf('series', series.status, `series:${series.id}:${series.status}`, () => seriesEndBody(series));
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
          if (this.batteryIsLive || batteryAwaitsPostRun(batteryRun)) {
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
        // A battery run that is not live is followed while the server still works on it: a member
        // repair, the analysis, the AI-written reports.
        const awaitsPostRun = !this.batteryIsLive && batteryAwaitsPostRun(batteryRun);
        // One signal per chain of server work this page saw, at whatever end it reaches, once its
        // post-run work has ended; the end rule picks the sound and silences a cancel. Its members'
        // own ends are accounted for here, so none of them signals afterwards. A member of a
        // followed model batch leaves its end to the batch.
        if (this.batteryIsLive || awaitsPostRun) {
          this.batteriesSeenLive.add(batteryRun.id);
        } else if (this.batteriesSeenLive.has(batteryRun.id)) {
          this.batteriesSeenLive.delete(batteryRun.id);
          for (const member of batteryRun.members ?? []) {
            this.runsSeenLive.delete(member.runId);
            this.runBatteryAtStart.delete(member.runId);
          }
          if (!this.memberOfFollowedModelBatch(batteryRun.modelBatchRunId)) {
            this.signalEndOf('battery', batteryRun.status, BenchmarkActiveRunMonitor.batterySignalKey(batteryRun),
              () => batteryEndBody(batteryRun));
          }
        }
        // The run banner and dialog follow the member in flight, as they do for a series.
        const runningId = batteryRun.currentRunId;
        if (runningId != null && (runningId !== this.activeRunId || runningId === this.runPollGaveUpRunId)) {
          this.activeRunId = runningId;
          this.startPolling(runningId);
        }
        if (!this.batteryIsLive && !awaitsPostRun) {
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

  /**
   * Reads a battery run into the banner's cached copy without touching `batteriesSeenLive` or the
   * completion signal: what a refused request leaves behind.
   */
  private refreshBatteryRun(batteryRunId: number): void {
    this.benchmarkService.getBatteryRun(batteryRunId).subscribe({
      next: (batteryRun) => {
        if (batteryRunId === this.activeBatteryRunId || this.activeBatteryRun?.id === batteryRunId) {
          this.activeBatteryRun = batteryRun;
          this.viewSync.notify();
        }
      },
      error: (err) => console.error('Failed to refresh benchmark battery run', err)
    });
  }

  /** Pending, Running or WaitingForCap: still going to launch members. */
  get batteryIsLive(): boolean {
    return isLiveBatteryRunStatus(this.activeBatteryRun?.status);
  }

  /**
   * `battery:<id>:<latest analysis id>:<report documents status>`: a later chain of work on the same
   * battery run ends in another analysis or documents state, so its signal is not a duplicate.
   */
  static batterySignalKey(batteryRun: BenchmarkBatteryRunDto): string {
    return `battery:${batteryRun.id}:${batteryRun.latestAnalysisId ?? 0}`
      + `:${batteryReportDocumentsStatusName(batteryRun.reportDocumentsStatus)}`;
  }

  /** `run:<id>:<completion time>`, the re-run's when there is one; `run:<id>` while no time is recorded. */
  static runSignalKey(run: BenchmarkRunDetailDto): string {
    const generation = run.rerunCompletedAtUtc ?? run.completedAtUtc;
    return generation ? `run:${run.id}:${generation}` : `run:${run.id}`;
  }

  // --- Model batch banner and polling ---

  /**
   * Reattaches the batch banner to a batch already live or stopped when the page loads, as
   * checkActiveBatteryRun does for a battery run. The dialog stays closed.
   */
  checkActiveModelBatch(): void {
    this.benchmarkService.getActiveModelBatch().subscribe({
      next: (batch) => {
        if (!batch) return;
        this.activeModelBatch = batch;
        this.activeModelBatchRunId = batch.id;
        if (isLiveModelBatchStatus(batch.status)) {
          this.modelBatchesFollowed.add(batch.id);
          this.startModelBatchPolling(batch.id);
        }
        this.viewSync.notify();
      },
      error: (err) => console.error('Failed to check active model batch', err)
    });
  }

  /** Adopts a batch this page started as the one the banner follows, as seen live, and polls it. */
  followModelBatch(batch: BenchmarkModelBatchRunDto): void {
    this.activeModelBatch = batch;
    this.activeModelBatchRunId = batch.id;
    this.modelBatchErrorMessage = null;
    this.batchesSeenLive.add(batch.id);
    this.modelBatchesFollowed.add(batch.id);
    this.startModelBatchPolling(batch.id);
    this.viewSync.notify();
  }

  /** The progress dialog continued, skipped or re-ran a stopped batch: this page follows it again. */
  onModelBatchResumed(batchId: number): void {
    this.activeModelBatchRunId = batchId;
    this.modelBatchErrorMessage = null;
    this.batchesSeenLive.add(batchId);
    this.modelBatchesFollowed.add(batchId);
    this.startModelBatchPolling(batchId);
    this.viewSync.notify();
  }

  /** The progress dialog canceled a batch; the banner reads it again. */
  onModelBatchCanceled(batchId: number): void {
    if (batchId === this.activeModelBatchRunId) {
      this.pollModelBatch(batchId);
    }
  }

  startModelBatchPolling(batchId: number): void {
    this.stopModelBatchPolling();
    this.modelBatchPollFailureCount = 0;
    this.modelBatchNextPollDueAtMs = 0;
    this.clearLostContact('modelBatch');
    this.lastModelBatchPollAttemptAtMs = Date.now();
    this.pollModelBatch(batchId);
    this.modelBatchPollTickerHandle = this.pollTicker.start(BenchmarkActiveRunMonitor.SERIES_POLL_INTERVAL_MS, () => {
      if (Date.now() < this.modelBatchNextPollDueAtMs) {
        return;
      }
      if (typeof document !== 'undefined' && document.hidden) {
        const hiddenPollDue = (this.launcher.completionSound || this.launcher.completionNotification)
          && (Date.now() - this.lastModelBatchPollAttemptAtMs) >= BenchmarkActiveRunMonitor.HIDDEN_POLL_INTERVAL_MS;
        if (!hiddenPollDue) {
          return;
        }
      }
      this.lastModelBatchPollAttemptAtMs = Date.now();
      this.pollModelBatch(batchId);
    });

    if (typeof document !== 'undefined') {
      this.modelBatchVisibilityChangeHandler = () => {
        if (!document.hidden) {
          this.pollModelBatch(batchId);
        }
      };
      document.addEventListener('visibilitychange', this.modelBatchVisibilityChangeHandler);
    }
  }

  stopModelBatchPolling(): void {
    if (this.modelBatchPollTickerHandle) {
      this.modelBatchPollTickerHandle();
      this.modelBatchPollTickerHandle = null;
    }
    if (this.modelBatchVisibilityChangeHandler && typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.modelBatchVisibilityChangeHandler);
      this.modelBatchVisibilityChangeHandler = null;
    }
  }

  /**
   * One poll of the followed batch. A batch seen live that is found ended or stopped signals once,
   * by the end rule: the failure sound for a stop of any reason, a failure or a completion with
   * errors, the completion chime for a completion, nothing for a cancel. While it runs, the run
   * banner and its poller follow the member's run in flight.
   */
  pollModelBatch(batchId: number): void {
    this.benchmarkService.getModelBatch(batchId).subscribe({
      next: (batch) => {
        this.modelBatchPollFailureCount = 0;
        this.modelBatchNextPollDueAtMs = 0;
        this.clearLostContact('modelBatch');
        if (batchId === this.activeModelBatchRunId || this.activeModelBatch?.id === batchId) {
          this.activeModelBatch = batch;
        }
        const live = isLiveModelBatchStatus(batch.status);
        if (live) {
          this.batchesSeenLive.add(batch.id);
          this.modelBatchesFollowed.add(batch.id);
        } else if (this.batchesSeenLive.has(batch.id)) {
          this.batchesSeenLive.delete(batch.id);
          this.signalEndOf('modelBatch', batch.status, modelBatchSignalKey(batch), () => modelBatchEndBody(batch));
        }
        const member = batch.currentMemberIndex != null ? batch.members[batch.currentMemberIndex] : undefined;
        const runningId = live && member?.status === 'Running' ? member.currentRunId ?? null : null;
        if (runningId != null && (runningId !== this.activeRunId || runningId === this.runPollGaveUpRunId)) {
          this.activeRunId = runningId;
          this.startPolling(runningId);
        }
        if (!live) {
          this.stopModelBatchPolling();
          this.workspace.loadHistory();
          this.workspace.loadRunLimits();
        }
        this.viewSync.notify();
      },
      error: (err) => {
        console.error('Failed to poll the model batch', err);
        if (this.modelBatchPollFailureCount === 0) {
          this.modelBatchPollFailureSinceMs = Date.now();
        }
        this.modelBatchPollFailureCount++;
        const nextDueAtMs = this.noteLostContact('modelBatch', batchId, this.modelBatchPollFailureCount, this.modelBatchPollFailureSinceMs);
        if (nextDueAtMs === null) {
          this.stopModelBatchPolling();
        } else {
          this.modelBatchNextPollDueAtMs = nextDueAtMs;
        }
        this.viewSync.notify();
      }
    });
  }

  /** Opens the Model Batch Progress dialog on a batch, which also ends any way back to another one. */
  openModelBatchDialog(batchId: number): void {
    this.returnToModelBatchId = null;
    this.modelBatchDialogId = batchId;
    this.modelBatchDialogVisible = true;
    this.viewSync.notify();
  }

  onModelBatchDialogClosed(): void {
    this.modelBatchDialogVisible = false;
    this.modelBatchDialogId = null;
    this.viewSync.notify();
  }

  /**
   * After a member's progress dialog closed: reopens the batch's dialog it came from. Deferred, so a
   * close that hands over to another dialog in the same turn leaves the way back to that dialog.
   */
  reopenModelBatchAfterClose(): void {
    const batchId = this.returnToModelBatchId;
    if (batchId == null) return;
    queueMicrotask(() => {
      if (this.returnToModelBatchId !== batchId || this.isRunProgressDialogOpen
        || this.batteryDialogVisible || this.modelBatchDialogVisible) {
        return;
      }
      this.openModelBatchDialog(batchId);
    });
  }

  /** The banner's Cancel Batch: asks first, through the shell's confirmation. */
  cancelActiveModelBatch(): void {
    const batchId = this.activeModelBatch?.id ?? this.activeModelBatchRunId;
    if (batchId == null) return;
    this.bridge.openConfirmDialog({
      title: `Cancel model batch #${batchId}?`,
      message: 'The model in flight is canceled and no further model starts. Completed models keep their results.',
      buttonText: 'Cancel Batch',
      buttonClass: 'btn-gh btn-gh-delete',
      icon: 'none',
      action: () => this.cancelModelBatch(batchId)
    });
  }

  /** Cancels a batch; the banner learns the outcome from the next poll. */
  cancelModelBatch(batchId: number): void {
    this.modelBatchErrorMessage = null;
    this.benchmarkService.cancelModelBatch(batchId).subscribe({
      next: () => {
        if (batchId === this.activeModelBatchRunId) {
          this.pollModelBatch(batchId);
        }
        this.viewSync.notify();
      },
      error: (err) => {
        console.error('Failed to cancel the model batch', err);
        this.modelBatchErrorMessage = refusalText(err, 'Failed to cancel the model batch.');
        this.viewSync.notify();
      }
    });
  }

  // --- Lost contact ---

  /**
   * Records a series, battery or model batch poller's failed poll and returns when its next attempt
   * is due (client clock, ms), or null once the failures have lasted {@link LOST_CONTACT_GIVE_UP_MS}.
   * The notice appears once {@link LOST_CONTACT_NOTICE_AFTER_FAILURES} polls in a row have failed.
   */
  private noteLostContact(kind: BenchmarkLostContactKind, id: number, failureCount: number, sinceMs: number): number | null {
    const now = Date.now();
    const backoff = BenchmarkActiveRunMonitor.LOST_CONTACT_BACKOFF_MS;
    const retryIntervalMs = backoff[Math.min(failureCount, backoff.length) - 1];
    const gaveUp = now - sinceMs >= BenchmarkActiveRunMonitor.LOST_CONTACT_GIVE_UP_MS;
    if (gaveUp || failureCount >= BenchmarkActiveRunMonitor.LOST_CONTACT_NOTICE_AFTER_FAILURES) {
      this.lostContact = { kind, id, failureCount, sinceMs, retryIntervalMs: gaveUp ? null : retryIntervalMs, gaveUp };
    }
    return gaveUp ? null : now + retryIntervalMs;
  }

  private clearLostContact(kind: BenchmarkLostContactKind): void {
    if (this.lostContact?.kind === kind) {
      this.lostContact = null;
    }
  }

  /** The Lost contact notice's sentence, or null while every backing-off poller reaches the server. */
  get lostContactText(): string | null {
    const notice = this.lostContact;
    if (!notice) return null;
    const subject = notice.kind === 'battery'
      ? `Battery Run #${notice.id}`
      : notice.kind === 'modelBatch' ? `Model batch #${notice.id}` : `Series #${notice.id}`;
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
    this.returnToBatteryRunId = null;
    this.batteryDialogVisible = false;
    this.batteryDialogRunId = null;
    this.reopenModelBatchAfterClose();
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
        this.refreshBatteryRun(batteryRunId);
      }
    });
  }

  /**
   * Continues a stopped battery run, or re-runs it under the current instrument after an instrument
   * change, which supersedes its completed members. Only an accepted request makes the battery run
   * one this page signals; a refusal refreshes the banner's copy and nothing else.
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
        this.batteriesSeenLive.add(batteryRunId);
        this.startBatteryPolling(batteryRunId);
        this.viewSync.notify();
      },
      error: (err) => {
        this.resumingBattery = false;
        this.batteryErrorMessage = refusalText(err, 'Failed to continue the battery run.');
        this.refreshBatteryRun(batteryRunId);
        this.viewSync.notify();
      }
    });
  }

  /**
   * After a member run's re-run ended: a battery run the server is still working on, or one whose
   * status moved while the run ran, signals for it, once its work is done. The battery run is read
   * fresh, never from the banner's copy. Returns whether the battery run took over the signal.
   */
  private batteryTakesRunSignal(battery: BenchmarkBatteryRunDto, statusAtStart: string | null): boolean {
    const moved = statusAtStart != null && battery.status !== statusAtStart;
    if (!isLiveBatteryRunStatus(battery.status) && !batteryAwaitsPostRun(battery) && !moved
      && !this.batteriesSeenLive.has(battery.id)) {
      return false;
    }
    // One battery poller per page: one following another battery run that is still moving keeps it.
    if (this.lockedBatteryRunId !== null && this.lockedBatteryRunId !== battery.id
      && (this.batteryIsLive || batteryAwaitsPostRun(this.activeBatteryRun))) {
      return false;
    }
    this.batteriesSeenLive.add(battery.id);
    if (this.lockedBatteryRunId !== battery.id) {
      this.activeBatteryRunId = battery.id;
      this.activeBatteryRun = battery;
      this.startBatteryPolling(battery.id);
    }
    return true;
  }

  /**
   * The battery run a run belongs to, from what this page holds: the banner's battery run, the
   * battery runs and runs Run History loaded. Null when none names it.
   */
  private batteryRunIdOf(runId: number): number | null {
    const cached = this.activeBatteryRun;
    if (cached && (cached.currentRunId === runId
      || (cached.repairingRunIds ?? []).includes(runId)
      || (cached.members ?? []).some(m => m.runId === runId))) {
      return cached.id;
    }
    const listed = (this.workspace.batteryRuns ?? []).find(b =>
      (b.repairingRunIds ?? []).includes(runId) || (b.members ?? []).some(m => m.runId === runId));
    if (listed) return listed.id;
    return (this.workspace.historyRuns ?? []).find(r => r.id === runId)?.batteryRunId ?? null;
  }

  /** Records the battery run of a run first seen Running, with its status then, read fresh when not cached. */
  private noteRunBatteryAtStart(runId: number): void {
    const batteryRunId = this.batteryRunIdOf(runId);
    if (batteryRunId == null) return;
    const cached = this.activeBatteryRun?.id === batteryRunId ? this.activeBatteryRun : null;
    this.runBatteryAtStart.set(runId, { batteryRunId, status: cached?.status ?? null });
    if (cached) return;
    this.benchmarkService.getBatteryRun(batteryRunId).subscribe({
      next: (battery) => {
        const start = this.runBatteryAtStart.get(runId);
        if (start?.batteryRunId === batteryRunId && start.status == null) {
          start.status = battery.status;
        }
      },
      error: (err) => console.warn('Failed to read the battery run of a starting member run', err)
    });
  }

  /** Cancels a run: the live one by default, or the one the progress dialog shows. */
  cancelActiveRun(runId: number | null = this.activeRunId) {
    if (!runId) return;
    this.noteOperatorCancel(runId);
    this.benchmarkService.cancelRun(runId).subscribe({
      next: () => {
        if (runId === this.viewedRunId && runId !== this.activeRunId) {
          this.startViewedPolling(runId);
        } else if (this.activeRunId != null) {
          this.pollRunDetail(this.activeRunId);
        }
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
    this.runPollGeneration++;
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
    this.runPollGeneration++;
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

  /** Refreshes the dialog's elapsed time once per whole elapsed second (`startElapsedTicker`). */
  startRunElapsedTicker(): void {
    this.stopRunElapsedTicker();
    this.runElapsedInterval = startElapsedTicker(() => this.runElapsedStartUtc, () => this.viewSync.notify());
  }

  stopRunElapsedTicker(): void {
    if (this.runElapsedInterval) {
      this.runElapsedInterval();
      this.runElapsedInterval = null;
    }
  }

  /**
   * The start the dialog's elapsed time counts from: the re-run's under a re-run scope, else the run's.
   */
  private get runElapsedStartUtc(): string | null {
    const run = this.dialogRunDetail;
    if (!run) return null;
    const scoped = (run.rerunScopeOrderIndexes?.length ?? 0) > 0
      || (this.dialogFollowsLiveRun && this.rerunScopeOrderIndexes.length > 0);
    if (scoped && run.rerunStartedAtUtc) return run.rerunStartedAtUtc;
    return run.startedAtUtc ?? null;
  }

  /**
   * The run half of transition detection: signals only for an id this poller watched Running, and
   * only when it is not a member of a series still live — a series signals once for the whole
   * group instead, in `pollSeries` — nor of a model batch this page follows, which signals for its
   * members. A member of a battery run reads that battery run fresh first: when the battery run
   * takes the signal (`batteryTakesRunSignal`), it signals once for both, after its own work. A run
   * ended by cancellation does not signal at all; otherwise the end rule picks the sound.
   */
  private maybeSignalRunCompletion(run: BenchmarkRunDetailDto): void {
    if (!this.runsSeenLive.has(run.id)) return;
    this.runsSeenLive.delete(run.id);
    const cancelledByOperator = this.operatorCancelledRunIds.delete(run.id);
    const rerunByOperator = this.operatorRerunRunIds.delete(run.id);
    const start = this.runBatteryAtStart.get(run.id) ?? null;
    this.runBatteryAtStart.delete(run.id);
    if (this.activeSeries != null && this.seriesIsLive) return;
    if (cancelledByOperator || this.runEndedByCancellation(run)) return;
    if (!rerunByOperator && this.runOfFollowedModelBatch(run)) return;
    const batteryRunId = start?.batteryRunId ?? this.batteryRunIdOf(run.id);
    if (batteryRunId == null) {
      this.signalRunEnd(run);
      return;
    }
    this.benchmarkService.getBatteryRun(batteryRunId).subscribe({
      next: (battery) => {
        if (battery.status === 'Cancelled' || this.batteryTakesRunSignal(battery, start?.status ?? null)) return;
        this.signalRunEnd(run);
        this.viewSync.notify();
      },
      error: (err) => {
        console.warn('Failed to read the battery run of a finished member run', err);
        if (!this.batteriesSeenLive.has(batteryRunId)) {
          this.signalRunEnd(run);
          this.viewSync.notify();
        }
      }
    });
  }

  private signalRunEnd(run: BenchmarkRunDetailDto): void {
    this.signalEndOf('run', run.status, BenchmarkActiveRunMonitor.runSignalKey(run), () => runEndBody(run));
  }

  /** A model batch this page followed live owns the work: its members leave their ends to it. */
  private memberOfFollowedModelBatch(modelBatchRunId: number | null | undefined): boolean {
    return modelBatchRunId != null && this.modelBatchesFollowed.has(modelBatchRunId);
  }

  /** A run of a followed model batch: by its own record, or by the batch's member list. */
  private runOfFollowedModelBatch(run: BenchmarkRunDetailDto): boolean {
    if (this.memberOfFollowedModelBatch(run.modelBatchRunId)) return true;
    const batch = this.activeModelBatch;
    return !!batch && this.modelBatchesFollowed.has(batch.id)
      && batch.members.some(m => m.runIds?.includes(run.id) || m.currentRunId === run.id);
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

  /** Applies the end rule to `status` and signals the end it names; a `'none'` end signals nothing. */
  private signalEndOf(kind: BenchmarkEndKind, status: string | number | null | undefined, key: string, body: () => string): void {
    const outcome = benchmarkEndSignal(kind, status);
    if (outcome === 'none') return;
    this.signalEnd(key, outcome, body());
  }

  /**
   * Marks the tab title even when both signals are off, so a hidden tab shows the end either way.
   * The sound — the completion chime or the failure sound, by `end` — and the notification are
   * then handled independently: either, both or neither may be on, and the notification does not
   * require the sound to have run. The notification fires whenever the checkbox is checked,
   * whatever the tab's own focus: an end is worth surfacing on the desktop even for an operator
   * looking straight at the tab, and a tab that merely lacks focus (another window in front, not
   * actually hidden) is not a case worth special-casing away.
   */
  private signalEnd(key: string, end: 'complete' | 'failed', body: string | null): void {
    this.markTabTitleForCompletion();
    const kind: BenchmarkCompletionSoundKind = end === 'failed' ? 'failed' : 'complete';

    if (this.launcher.completionSound) {
      this.completionSoundService.play(key, kind).then(outcome => {
        this.lastCompletionSoundOutcome = outcome;
        if (outcome === 'played') {
          this.completionSoundStatus = null;
        } else if (outcome === 'blocked') {
          this.completionSoundStatus = 'Playback was blocked by the browser — press Test sound once to allow it.';
        } else if (outcome === 'deferred') {
          this.completionSoundStatus = 'The browser held the sound until this tab was shown.';
        }
        this.viewSync.notify();
      }).catch(() => {
        this.lastCompletionSoundOutcome = 'error';
        this.viewSync.notify();
      });
    }

    if (this.launcher.completionNotification && body) {
      const hidden = typeof document !== 'undefined' ? document.hidden : false;
      const focused = typeof document !== 'undefined' ? document.hasFocus() : true;
      const notified = this.completionNotificationService.notify(key, 'GnollBench', body);
      this.recordNotificationAttempt({ atUtc: new Date().toISOString(), key, hidden, focused, outcome: notified, kind });
    }
  }

  /** Keeps the last {@link MAX_NOTIFICATION_ATTEMPTS} notification attempts for the diagnostics capture. */
  private recordNotificationAttempt(attempt: {
    atUtc: string; key: string; hidden: boolean; focused: boolean; outcome: BenchmarkNotifyOutcome; kind: BenchmarkCompletionSoundKind;
  }): void {
    this.notificationAttempts.push(attempt);
    if (this.notificationAttempts.length > BenchmarkActiveRunMonitor.MAX_NOTIFICATION_ATTEMPTS) {
      this.notificationAttempts.shift();
    }
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
      this.launcher.persistRunSettings();
      this.viewSync.notify();
      return;
    }

    // Saved once the outcome is applied, so a refused prompt is stored as off.
    this.completionNotificationService.requestPermission().then(outcome => {
      this.applyNotificationPermissionOutcome(outcome);
      this.launcher.persistRunSettings();
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

  /**
   * A response is discarded when the poller that asked has since been stopped or restarted, or the
   * live run has moved to another run: `stopPolling` cannot cancel a request already in flight.
   */
  private runPollResponseIsStale(runId: number, generation: number): boolean {
    return generation !== this.runPollGeneration || (this.activeRunId != null && runId !== this.activeRunId);
  }

  private pollRunDetail(runId: number) {
    const generation = this.runPollGeneration;
    this.benchmarkService.getRun(runId).subscribe({
      next: (run) => {
        if (this.runPollResponseIsStale(runId, generation)) return;
        this.lastRunPollAtUtc = new Date().toISOString();
        this.lastRunPollError = null;
        this.runPollFailureCount = 0;
        this.activeRunDetail = run;
        if (this.dialogFollowsLiveRun) {
          this.runDiagnosticsPanelCapturedAt = new Date();
          this.syncDialogQuestions();
        }
        const statusStr = formatStatus(run.status);
        if (statusStr === 'Running') {
          if (!this.runsSeenLive.has(run.id)) {
            this.runsSeenLive.add(run.id);
            this.noteRunBatteryAtStart(run.id);
          }
          this.rerunLaunchPending = false;
          this.rerunLaunchedAtMs = null;
          if (this.runTerminalSeenAt?.runId === run.id) {
            this.runTerminalSeenAt = null;
          }
          if (this.isRunProgressDialogOpen && this.dialogFollowsLiveRun && !this.runElapsedInterval) {
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
        if (this.runPollResponseIsStale(runId, generation)) return;
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
    if (this.dialogFollowsLiveRun) {
      this.stopRunElapsedTicker();
    }
    this.workspace.loadHistory();
    this.maybeSignalRunCompletion(run);
  }

  /**
   * The run's stage 4 is still to come or under way: it ended Completed, names a report writer, and its
   * reports are Pending or Writing, or still NotRequested within the start grace of the poller that
   * first saw it terminal.
   */
  runAwaitsReports(run: BenchmarkRunDetailDto): boolean {
    if (run.reportWriterModelConfigurationId == null || formatStatus(run.status) !== 'Completed') {
      return false;
    }
    const status = reportDocumentsStatusOf(run);
    if (status === BenchmarkRunReportDocumentsStatus.Pending || status === BenchmarkRunReportDocumentsStatus.Writing) {
      return true;
    }
    if (status !== BenchmarkRunReportDocumentsStatus.NotRequested) {
      return false;
    }
    if (this.runTerminalSeenAt?.runId === run.id) {
      return this.runReportGraceOpen;
    }
    return this.viewedTerminalSeenAt?.runId === run.id
      && Date.now() - this.viewedTerminalSeenAt.atMs < BenchmarkActiveRunMonitor.RUN_REPORT_STAGE_GRACE_MS;
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

  // --- The viewed run ---

  /** Points the progress dialog at this run, and keeps it there when the live run moves on. */
  viewRun(runId: number): void {
    if (this.viewedRunId !== runId) {
      this.viewedRunId = runId;
      this.syncViewedPoller();
    }
    this.viewSync.notify();
  }

  /** The progress dialog follows the live run again; the viewed poller stops. */
  clearViewedRun(): void {
    this.viewedRunId = null;
    this.stopViewedPolling();
    this.viewedRunDetail = null;
    this.viewedRunReportJob = null;
    this.viewedTerminalSeenAt = null;
  }

  /** A viewed poller runs exactly while a viewed run is set and is not the live run. */
  private syncViewedPoller(): void {
    const runId = this.viewedRunId;
    if (runId == null || runId === this.activeRunId) {
      this.stopViewedPolling();
      return;
    }
    if (this.viewedPollRunId !== runId) {
      this.startViewedPolling(runId);
    }
  }

  /**
   * Fetches the viewed run at once, then every {@link RUN_POLL_INTERVAL_MS} while it is Running or
   * its reports are awaited, and stops after the first terminal response that awaits nothing. What
   * the live poller already holds for the run is shown until the first response.
   */
  private startViewedPolling(runId: number): void {
    this.stopViewedPolling();
    this.viewedPollRunId = runId;
    this.viewedPollFailureCount = 0;
    const generation = this.viewedPollGeneration;
    if (this.viewedRunDetail?.id !== runId) {
      this.viewedRunDetail = this.activeRunDetail?.id === runId ? this.activeRunDetail : null;
    }
    if (this.viewedRunReportJob?.runId !== runId) {
      const job = this.activeRunReportJob?.runId === runId ? this.activeRunReportJob : null;
      this.viewedRunReportJob = job;
      this.viewedRunReportJobReceivedAtMs = job ? this.activeRunReportJobReceivedAtMs : 0;
    }
    if (this.viewedTerminalSeenAt?.runId !== runId) {
      this.viewedTerminalSeenAt = this.runTerminalSeenAt?.runId === runId ? { ...this.runTerminalSeenAt } : null;
    }
    this.pollViewedRun(runId, generation);
    // The response above may already have stopped the poller.
    if (generation !== this.viewedPollGeneration) {
      return;
    }
    this.viewedPollTickerHandle = this.pollTicker.start(BenchmarkActiveRunMonitor.RUN_POLL_INTERVAL_MS, () => {
      if (typeof document !== 'undefined' && document.hidden) {
        return;
      }
      this.pollViewedRun(runId, generation);
    });
  }

  private stopViewedPolling(): void {
    this.viewedPollGeneration++;
    this.viewedPollRunId = null;
    this.haltViewedTicker();
  }

  /** Stops the viewed poller's ticks and job poll, keeping what it last read. */
  private haltViewedTicker(): void {
    if (this.viewedPollTickerHandle) {
      this.viewedPollTickerHandle();
      this.viewedPollTickerHandle = null;
    }
    this.viewedReportJobSub?.unsubscribe();
    this.viewedReportJobSub = null;
  }

  private pollViewedRun(runId: number, generation: number): void {
    this.benchmarkService.getRun(runId).subscribe({
      next: (run) => {
        if (generation !== this.viewedPollGeneration || runId !== this.viewedRunId || run?.id !== runId) return;
        this.viewedPollFailureCount = 0;
        this.viewedRunDetail = run;
        this.runDiagnosticsPanelCapturedAt = new Date();
        this.syncDialogQuestions();
        if (formatStatus(run.status) === 'Running') {
          this.viewedTerminalSeenAt = null;
          if (this.isRunProgressDialogOpen && !this.runElapsedInterval) {
            this.startRunElapsedTicker();
          }
        } else {
          if (this.viewedTerminalSeenAt?.runId !== run.id) {
            this.viewedTerminalSeenAt = { runId: run.id, atMs: Date.now() };
          }
          if (this.runAwaitsReports(run)) {
            this.pollViewedRunReportJob(runId, generation);
          } else {
            // The run id is kept, so a later change of the live run does not restart the poller.
            this.viewedPollGeneration++;
            this.haltViewedTicker();
            this.stopRunElapsedTicker();
          }
        }
        this.viewSync.notify();
      },
      error: (err) => {
        if (generation !== this.viewedPollGeneration || runId !== this.viewedRunId) return;
        console.warn('Failed to poll the viewed run', err);
        this.viewedPollFailureCount++;
        if (this.viewedPollFailureCount >= BenchmarkActiveRunMonitor.MAX_CONSECUTIVE_POLL_FAILURES) {
          this.viewedPollGeneration++;
          this.haltViewedTicker();
        }
      }
    });
  }

  /** One request at a time, as for the live run. A failure leaves the last view. */
  private pollViewedRunReportJob(runId: number, generation: number): void {
    this.viewedReportJobSub?.unsubscribe();
    this.viewedReportJobSub = this.benchmarkService.getRunReportJob(runId).subscribe({
      next: view => {
        if (generation !== this.viewedPollGeneration || runId !== this.viewedRunId) return;
        this.viewedRunReportJob = view;
        this.viewedRunReportJobReceivedAtMs = Date.now();
        this.viewSync.notify();
      },
      error: err => console.warn('Failed to poll the viewed run\'s report writing job', err)
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

  /** The suite the dialog's detail last asked questions for, so a failed load is retried only on a change. */
  private dialogQuestionsSuiteId: number | null = null;

  /**
   * Loads the questions of the dialog run's suite once its detail has arrived, and again whenever that
   * suite changes, while the progress dialog is open.
   */
  syncDialogQuestions(): void {
    if (!this.isRunProgressDialogOpen) return;
    const suiteId = this.dialogRunDetail?.benchmarkSuiteId ?? null;
    if (suiteId == null || suiteId === this.dialogQuestionsSuiteId) return;
    this.dialogQuestionsSuiteId = suiteId;
    if (suiteId !== this.runProgressQuestionsSuiteId) {
      this.loadRunProgressQuestions(suiteId);
    }
  }

  loadRunProgressQuestions(suiteId: number): void {
    // Claimed before the request so a second open while it is in flight does not refire it.
    this.runProgressQuestionsSuiteId = suiteId;
    this.benchmarkService.getQuestions(suiteId).subscribe({
      next: (data) => {
        // A slower answer for a suite the dialog has since left is dropped.
        if (this.runProgressQuestionsSuiteId !== suiteId) return;
        this.runQuestionsLoadError = null;
        this.runProgressQuestions = data;
        this.viewSync.notify();
      },
      error: (err) => {
        if (this.runProgressQuestionsSuiteId !== suiteId) return;
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
    this.operatorRerunRunIds.add(runId);
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
    this.clearViewedRun();
    this.stopRunElapsedTicker();
    this.stopSeriesPolling();
    this.stopBatteryPolling();
    this.stopModelBatchPolling();
    if (this.titleRestoreVisibilityHandler && typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.titleRestoreVisibilityHandler);
      this.titleRestoreVisibilityHandler = null;
    }
  }
}
