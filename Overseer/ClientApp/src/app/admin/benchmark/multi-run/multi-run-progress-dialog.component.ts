import {
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
import { CommonModule } from '@angular/common';

import {
  AdminBenchmarkService,
  BENCHMARK_SECOND_OPINION_MODES,
  BenchmarkComparabilityResultDto,
  BenchmarkGroupAnalysisDto,
  BenchmarkInstrumentChangedDto,
  BenchmarkRunDetailDto,
  BenchmarkRunLimitsDto,
  BenchmarkRunSeriesDto,
  BenchmarkRunSeriesMemberDto,
  BenchmarkSecondOpinionMode
} from '../../../services/admin-benchmark.service';
import { SystemService } from '../../../services/system.service';
import { elapsedMsBetween, parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';

/**
 * The stages of a multi-run operation. `waitingForCap` and `stopped` are stages in their own
 * right rather than decorations on `running`: a series parked behind the daily run cap or halted
 * by a failed member looks exactly like a hung run unless the dialog names the state, and the
 * whole reason the single-run dialog is legible is that it never shows a spinner it cannot
 * explain.
 */
export type MultiRunStage =
  | 'launching'
  | 'running'
  | 'waitingForCap'
  | 'stopped'
  | 'analysing'
  | 'complete';

/**
 * The subset of `BenchmarkGroupStatisticsResult` the diagnostics capture reads. The DTO carries
 * the server's statistics record verbatim (`result: any`), so this is a reader for it rather than
 * a second definition of it: every field is optional and every read is defensive, because a
 * mirror that must be kept in step with the server record is the thing the DTO comment warns
 * against.
 */
interface GroupStatisticsResultShape {
  runIds?: number[];
  runCount?: number;
  itemCount?: number;
  unansweredItemCount?: number;
  pooledIndexReportable?: boolean;
  unstableQuestionIds?: number[];
  varianceDecompositionCaveat?: string;
  items?: Array<{
    questionId?: number;
    orderIndex?: number;
    runCount?: number;
    mean?: number;
    standardDeviation?: number | null;
    criticalErrorRate?: number;
    unstable?: boolean;
    insufficientRuns?: boolean;
    /** The item's per-run scores, positionally aligned with `runIds`. */
    scores?: number[];
    /** The runs `scores` came from, in the same order. */
    runIds?: number[];
  }>;
  index?: {
    runCount?: number;
    pointEstimate?: number;
    weightedMeanOfItemMeans?: number;
    identityHolds?: boolean;
    perRunIndices?: number[];
    reproducibilityStandardDeviation?: number | null;
    reproducibilityStandardError?: number | null;
    reproducibilityCriticalValue?: number | null;
    reproducibilityHalfWidth?: number | null;
    reproducibilityAvailable?: boolean;
    itemSamplingStandardError?: number | null;
    itemSamplingCriticalValue?: number | null;
    itemSamplingHalfWidth?: number | null;
    combinedHalfWidth?: number | null;
    combinedLower?: number | null;
    combinedUpper?: number | null;
  };
}

/** The comparison half of a stored analysis, read the same defensive way. */
interface GroupComparisonShape {
  baselineRunIds?: number[];
  treatmentRunIds?: number[];
  pairedItemCount?: number;
  unpairedItemCount?: number;
  meanDifference?: number;
  differenceStandardDeviation?: number | null;
  differenceConfidenceHalfWidth?: number | null;
  differenceConfidenceLower?: number | null;
  differenceConfidenceUpper?: number | null;
  cohensDz?: number | null;
  falseDiscoveryRate?: number;
  itemComparisons?: unknown[];
  wilcoxon?: {
    sampleSize?: number;
    zeroDifferenceCount?: number;
    statistic?: number;
    pValue?: number | null;
    method?: string;
    tiesPresent?: boolean;
  };
  pairedT?: {
    sampleSize?: number;
    meanDifference?: number;
    standardError?: number | null;
    tStatistic?: number | null;
    degreesOfFreedom?: number | null;
    pValue?: number | null;
  };
}

/**
 * Progress for a multi-run series: the whole operation, not the arithmetic at the end of it.
 *
 * The group analysis is pure arithmetic over at most twenty runs and completes in well under a
 * second, so a dialog for the *computation* would show nothing. What has duration is the series —
 * *N* runs of roughly half an hour each — and that is where `WaitingForCap` and `Stopped` live.
 * The analysis appears here as the final stage.
 *
 * Modelled file-for-file on the single-run progress dialog in `benchmark.component.html`, down to
 * the stage rail, the model strip, the diagnostics `<details>` and the footer verbs, so an
 * operator who knows one knows the other. It deliberately does **not** embed the single-run
 * per-question view: two stacked modal dialogs fight over focus, so the running member's row
 * carries an *Open run progress* button that asks the host to hand over instead.
 */
@Component({
  selector: 'app-multi-run-progress-dialog',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './multi-run-progress-dialog.component.html',
  styleUrls: ['./multi-run-progress-dialog.component.scss']
})
export class MultiRunProgressDialogComponent implements OnInit, OnChanges, OnDestroy {
  private benchmarkService = inject(AdminBenchmarkService);
  private systemService = inject(SystemService);
  private cdr = inject(ChangeDetectorRef);

  /** Matches the single-run dialog's cadence: the two dialogs poll the same server. */
  static readonly SERIES_POLL_INTERVAL_MS = 2000;
  static readonly ELAPSED_TICK_MS = 1000;
  private static readonly COPIED_RESET_MS = 2000;

  @Input() seriesId: number | null = null;
  @Input() visible = false;

  /** Emitted by Escape, the close button and *Run in Background*; the host owns `visible`. */
  @Output() closed = new EventEmitter<void>();

  /**
   * The **run id** of the member whose per-question progress the operator asked for. The host
   * closes this dialog and opens the existing single-run dialog on that run.
   */
  @Output() openRunProgress = new EventEmitter<number>();

  /**
   * The **group id** whose multi-run analysis the operator asked to view. The host closes this
   * dialog and switches to the Multi-Run Analysis tab on that group — the dialog hands off rather
   * than embedding, because two native modals in the top layer trap focus between them.
   */
  @Output() openGroupAnalysis = new EventEmitter<number>();

  @ViewChild('multiRunProgressDialog') dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('multiRunProgressHeading') heading?: ElementRef<HTMLElement>;

  series: BenchmarkRunSeriesDto | null = null;
  limits: BenchmarkRunLimitsDto | null = null;

  /**
   * Member 1's run detail, used for nothing but the model strip: the series DTO carries the suite
   * and the counts but no model fields, and the configuration under test is exactly what an
   * operator wants to read without opening the report.
   */
  firstMemberRun: BenchmarkRunDetailDto | null = null;
  private firstMemberRunId: number | null = null;

  groupAnalysis: BenchmarkGroupAnalysisDto | null = null;
  groupComparability: BenchmarkComparabilityResultDto | null = null;
  analysisError: string | null = null;
  /** The group id whose analysis has been fetched, requested and had its comparability resolved. */
  private analysisFetchedForGroupId: number | null = null;
  private analysisRequestedForGroupId: number | null = null;
  private comparabilityForGroupId: number | null = null;
  analysisInFlight = false;

  /** The 409 body of a refused resume. Non-null puts the instrument-change choice on screen. */
  instrumentChange: BenchmarkInstrumentChangedDto | null = null;
  resumeInFlight = false;
  cancelInFlight = false;
  errorMessage: string | null = null;

  overseerBuildVersion: string | null = null;

  copiedSeriesDiagnostics = false;
  seriesDiagnosticsCopyFailed = false;
  copiedGroupDiagnostics = false;
  groupDiagnosticsCopyFailed = false;
  private copiedSeriesTimer: ReturnType<typeof setTimeout> | null = null;
  private copiedGroupTimer: ReturnType<typeof setTimeout> | null = null;

  lastPollAtUtc: string | null = null;
  lastPollError: string | null = null;

  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private elapsedTimer: ReturnType<typeof setInterval> | null = null;
  private visibilityChangeHandler: (() => void) | null = null;
  private isOpen = false;

  readonly secondOpinionModeOptions = BENCHMARK_SECOND_OPINION_MODES;

  ngOnInit(): void {
    // The tooltips on the copy and row-action buttons are interestfor + popover="hint"; Firefox
    // and Safari need the polyfills before either resolves.
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['visible'] && !changes['seriesId']) {
      return;
    }

    if (this.visible && this.seriesId != null) {
      const seriesChanged = !!changes['seriesId']
        && changes['seriesId'].previousValue !== changes['seriesId'].currentValue;
      if (seriesChanged) {
        this.resetForNewSeries();
      }
      this.openDialog();
    } else if (this.isOpen) {
      // The host lowered `visible`; close without emitting, or a close would echo back as a
      // second close request.
      this.closeDialog();
    }
  }

  ngOnDestroy(): void {
    this.stopPolling();
    this.stopElapsedTicker();
    if (this.copiedSeriesTimer) { clearTimeout(this.copiedSeriesTimer); }
    if (this.copiedGroupTimer) { clearTimeout(this.copiedGroupTimer); }
  }

  // -------------------------------------------------------------------------------------------
  // Dialog lifecycle
  // -------------------------------------------------------------------------------------------

  private resetForNewSeries(): void {
    this.series = null;
    this.firstMemberRun = null;
    this.firstMemberRunId = null;
    this.groupAnalysis = null;
    this.groupComparability = null;
    this.analysisError = null;
    this.analysisFetchedForGroupId = null;
    this.analysisRequestedForGroupId = null;
    this.comparabilityForGroupId = null;
    this.instrumentChange = null;
    this.errorMessage = null;
    this.lastPollError = null;
  }

  private openDialog(): void {
    if (this.isOpen) {
      return;
    }
    this.isOpen = true;
    this.seriesDiagnosticsCopyFailed = false;
    this.groupDiagnosticsCopyFailed = false;

    if (this.overseerBuildVersion === null) {
      this.systemService.getVersion().subscribe({
        next: (version) => {
          this.overseerBuildVersion = version;
          this.cdr.detectChanges();
        },
        error: () => {
          this.overseerBuildVersion = 'unknown';
        }
      });
    }

    // The caps move only when a run starts or ages out of the rolling window, so once per open is
    // enough; the diagnostics text reports whichever snapshot it has.
    this.loadLimits();
    this.startPolling();
    this.startElapsedTicker();

    this.dialog?.nativeElement.showModal();
    this.cdr.detectChanges();
    this.heading?.nativeElement.focus();
  }

  /** Closes the dialog without telling the host — used when the host itself lowered `visible`. */
  private closeDialog(): void {
    this.isOpen = false;
    this.stopPolling();
    this.stopElapsedTicker();
    this.dialog?.nativeElement.close();
    this.cdr.detectChanges();
  }

  /** Escape, the header close button and *Run in Background* all land here. */
  requestClose(): void {
    this.closeDialog();
    this.closed.emit();
  }

  /**
   * Hands the operator to the existing single-run dialog. This dialog closes first, because two
   * modal dialogs in the top layer trap focus between them.
   */
  openMemberRunProgress(member: BenchmarkRunSeriesMemberDto): void {
    this.closeDialog();
    this.closed.emit();
    this.openRunProgress.emit(member.runId);
  }

  // -------------------------------------------------------------------------------------------
  // Polling
  // -------------------------------------------------------------------------------------------

  private startPolling(): void {
    this.stopPolling();
    if (this.seriesId == null) {
      return;
    }
    const seriesId = this.seriesId;
    this.pollSeries(seriesId);
    this.pollTimer = setInterval(() => {
      // A hidden tab is a tab nobody is reading. The visibility handler below catches it up the
      // moment it comes back, so nothing is lost by skipping the tick.
      if (typeof document !== 'undefined' && document.hidden) {
        return;
      }
      this.pollSeries(seriesId);
    }, MultiRunProgressDialogComponent.SERIES_POLL_INTERVAL_MS);

    if (typeof document !== 'undefined') {
      this.visibilityChangeHandler = () => {
        if (!document.hidden) {
          this.pollSeries(seriesId);
        }
      };
      document.addEventListener('visibilitychange', this.visibilityChangeHandler);
    }
  }

  private stopPolling(): void {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
    if (this.visibilityChangeHandler && typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.visibilityChangeHandler);
      this.visibilityChangeHandler = null;
    }
  }

  private startElapsedTicker(): void {
    this.stopElapsedTicker();
    this.elapsedTimer = setInterval(() => {
      if (typeof document !== 'undefined' && document.hidden) {
        return;
      }
      this.cdr.detectChanges();
    }, MultiRunProgressDialogComponent.ELAPSED_TICK_MS);
  }

  private stopElapsedTicker(): void {
    if (this.elapsedTimer) {
      clearInterval(this.elapsedTimer);
      this.elapsedTimer = null;
    }
  }

  private pollSeries(seriesId: number): void {
    this.benchmarkService.getRunSeries(seriesId).subscribe({
      next: (series) => {
        this.lastPollAtUtc = new Date().toISOString();
        this.lastPollError = null;
        this.series = series;

        this.loadFirstMemberRunIfNeeded(series);
        this.resolveGroupAnalysis(series);

        if (!this.seriesIsLive) {
          // Nothing moves again without an operator action: a stopped series waits for Continue,
          // and a terminal one waits for nothing at all.
          this.stopPolling();
          this.stopElapsedTicker();
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.lastPollAtUtc = new Date().toISOString();
        this.lastPollError = this.describeError(err, 'Polling failed');
        console.error('Failed to poll benchmark run series', err);
        this.stopPolling();
        this.cdr.detectChanges();
      }
    });
  }

  private loadLimits(): void {
    this.benchmarkService.getRunLimits().subscribe({
      next: (limits) => {
        this.limits = limits;
        this.cdr.detectChanges();
      },
      // A missing limits snapshot degrades the diagnostics text by three lines and nothing else,
      // so it is never allowed to fail the dialog.
      error: (err) => console.warn('Failed to load benchmark run limits', err)
    });
  }

  private loadFirstMemberRunIfNeeded(series: BenchmarkRunSeriesDto): void {
    const first = series.members?.length ? series.members[0] : null;
    if (!first || this.firstMemberRunId === first.runId) {
      return;
    }
    this.firstMemberRunId = first.runId;
    this.benchmarkService.getRun(first.runId).subscribe({
      next: (run) => {
        this.firstMemberRun = run;
        this.cdr.detectChanges();
      },
      error: (err) => {
        // The strip disappears; the rest of the dialog is unaffected.
        this.firstMemberRunId = null;
        console.warn('Failed to load the first series member for the model strip', err);
      }
    });
  }

  /**
   * Resolves the final stage. The series orchestrator creates the analysis group but deliberately
   * does not compute the analysis — that is admin-initiated — so this dialog asks for it once,
   * when the series lands terminal with a group. Without that the *Analysing* stage could never
   * advance and *Download report* could never enable, which is the same stalled spinner the stage
   * rail exists to avoid.
   */
  private resolveGroupAnalysis(series: BenchmarkRunSeriesDto): void {
    const groupId = series.autoCreatedGroupId ?? null;
    if (groupId == null || !this.seriesIsTerminal) {
      return;
    }

    if (this.analysisFetchedForGroupId !== groupId) {
      this.analysisFetchedForGroupId = groupId;
      this.benchmarkService.getRunGroupAnalysis(groupId).subscribe({
        next: (analysis) => {
          this.groupAnalysis = analysis;
          if (!analysis) {
            this.requestGroupAnalysis(groupId);
          }
          this.cdr.detectChanges();
        },
        error: (err) => {
          this.analysisError = this.describeError(err, 'Failed to read the group analysis');
          this.cdr.detectChanges();
        }
      });
    }

    if (this.comparabilityForGroupId !== groupId && (series.members?.length ?? 0) >= 2) {
      this.loadComparability(groupId, series);
    }
  }

  private requestGroupAnalysis(groupId: number): void {
    if (this.analysisRequestedForGroupId === groupId) {
      return;
    }
    this.analysisRequestedForGroupId = groupId;
    this.analysisInFlight = true;
    this.benchmarkService.analyseRunGroup(groupId).subscribe({
      next: (analysis) => {
        this.analysisInFlight = false;
        this.groupAnalysis = analysis;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.analysisInFlight = false;
        this.analysisError = this.describeError(err, 'Failed to compute the group analysis');
        this.cdr.detectChanges();
      }
    });
  }

  /**
   * The comparability verdict with its reasons. Neither the group DTO nor the stored analysis
   * carries the matched keys, and the tier preview is the endpoint that does — it creates nothing,
   * which is why it is safe to call for a group that already exists. A tier verdict with no reasons
   * is unusable in a bug report, and that text is the entire purpose of the group capture.
   */
  private loadComparability(groupId: number, series: BenchmarkRunSeriesDto): void {
    this.comparabilityForGroupId = groupId;
    const runIds = series.members.map(m => m.runId);
    this.benchmarkService.previewRunGroupTier({
      name: `Series ${series.id}`,
      runIds,
      // Set so a genuinely cross-condition set answers with its comparability rather than with a
      // refusal; nothing is persisted either way.
      crossCondition: true
    }).subscribe({
      next: (preview) => {
        this.groupComparability = preview.comparability ?? null;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.comparabilityForGroupId = null;
        console.warn('Failed to resolve the group comparability keys', err);
      }
    });
  }

  // -------------------------------------------------------------------------------------------
  // Stage model
  // -------------------------------------------------------------------------------------------

  get seriesIsLive(): boolean {
    const status = this.series?.status;
    return status === 'Pending' || status === 'Running' || status === 'WaitingForCap';
  }

  get seriesIsTerminal(): boolean {
    const status = this.series?.status;
    return status === 'Completed' || status === 'CompletedWithErrors'
      || status === 'Cancelled' || status === 'Failed';
  }

  get stage(): MultiRunStage {
    const series = this.series;
    if (!series) return 'launching';
    switch (series.status) {
      case 'Pending':
        return 'launching';
      case 'Running':
        return series.members?.length ? 'running' : 'launching';
      case 'WaitingForCap':
        return 'waitingForCap';
      case 'Stopped':
        return 'stopped';
      case 'Completed':
      case 'CompletedWithErrors':
        return this.analysisPending ? 'analysing' : 'complete';
      default:
        // Cancelled and Failed: finished, however unhappily. The status line names which.
        return 'complete';
    }
  }

  /** A group exists, no analysis has come back, and nothing has failed trying to get one. */
  get analysisPending(): boolean {
    return this.series?.autoCreatedGroupId != null
      && !this.groupAnalysis
      && !this.analysisError;
  }

  /**
   * Which rail step is lit. `waitingForCap` and `stopped` keep step 2 current — the series is
   * between runs, not past them — and the state line beneath says which.
   */
  get stageIndex(): number {
    switch (this.stage) {
      case 'launching': return 0;
      case 'running':
      case 'waitingForCap':
      case 'stopped': return 1;
      case 'analysing': return 2;
      default: return 3;
    }
  }

  get stageLabel(): string {
    const series = this.series;
    switch (this.stage) {
      case 'launching':
        return 'Launching the series…';
      case 'running':
        return `Running run ${this.currentMemberIndex} of ${series?.requestedRunCount ?? 0}`;
      case 'waitingForCap':
        return 'Waiting for the run cap to clear';
      case 'stopped':
        return `Stopped — ${series?.stopReasonText || series?.stopReason || 'reason not recorded'}`;
      case 'analysing':
        return 'Analysing the replicate set';
      default:
        return this.terminalLabel;
    }
  }

  private get terminalLabel(): string {
    const series = this.series;
    if (!series) return 'Complete';
    switch (series.status) {
      case 'Cancelled': return 'Cancelled';
      case 'Failed': return 'Failed';
      case 'CompletedWithErrors':
        return `Complete with errors — ${series.completedRunCount} of ${series.requestedRunCount} runs, ${series.failedRunCount} failed`;
      default:
        return `Complete — ${series.completedRunCount} of ${series.requestedRunCount} runs`;
    }
  }

  /**
   * The 1-based member the series is on. Members appear as they are launched, so the count of
   * members is the current index while one is running and the completed count once none is.
   */
  get currentMemberIndex(): number {
    const series = this.series;
    if (!series) return 0;
    const running = series.members?.find(m => m.status === 'Running');
    if (running) return running.index;
    return Math.min(series.completedRunCount + 1, series.requestedRunCount);
  }

  get runningMemberRunId(): number | null {
    return this.series?.members?.find(m => m.status === 'Running')?.runId ?? null;
  }

  /** True while the series is parked or halted: rendered as a named state, never as a spinner. */
  get isNamedPause(): boolean {
    return this.stage === 'waitingForCap' || this.stage === 'stopped';
  }

  get namedPauseDetail(): string {
    if (this.stage === 'waitingForCap') {
      return 'The daily run cap is reached. The series is paused and resumes on its own as runs age out of the rolling 24-hour window.';
    }
    if (this.stage === 'stopped') {
      const reason = this.series?.stopReasonText || this.series?.stopReason || 'The series stopped.';
      return `${reason} Completed runs are kept; Continue resumes from the next member.`;
    }
    return '';
  }

  get canContinue(): boolean {
    return !!this.series?.resumable;
  }

  get continueLabel(): string {
    const reason = this.series?.stopReasonText || this.series?.stopReason;
    return reason ? `Continue — ${reason}` : 'Continue';
  }

  get canViewReport(): boolean {
    return this.series?.autoCreatedGroupId != null && !!this.groupAnalysis;
  }

  get viewReportTooltip(): string {
    if (this.canViewReport) {
      return 'Open the multi-run analysis for this series';
    }
    if (this.series?.autoCreatedGroupId == null) {
      return 'No analysis group exists for this series yet — a group is created once two or more members complete.';
    }
    return 'The group analysis has not finished yet. The report becomes available as soon as it has.';
  }

  // -------------------------------------------------------------------------------------------
  // Actions
  // -------------------------------------------------------------------------------------------

  cancelSeries(): void {
    const seriesId = this.series?.id ?? this.seriesId;
    if (seriesId == null || this.cancelInFlight) return;
    this.cancelInFlight = true;
    this.errorMessage = null;
    this.benchmarkService.cancelRunSeries(seriesId).subscribe({
      next: () => {
        this.cancelInFlight = false;
        this.pollSeries(seriesId);
      },
      error: (err) => {
        this.cancelInFlight = false;
        this.errorMessage = this.describeError(err, 'Failed to cancel the series');
        this.cdr.detectChanges();
      }
    });
  }

  /**
   * Resumes a stopped series. A 409 means an instrument hash moved while the series was stopped:
   * a replicate set whose members straddle a deployment is not a replicate set, so the server
   * refuses and the dialog names the hash rather than retrying silently.
   */
  continueSeries(acknowledgeInstrumentChange = false): void {
    const seriesId = this.series?.id ?? this.seriesId;
    if (seriesId == null || this.resumeInFlight) return;
    this.resumeInFlight = true;
    this.errorMessage = null;
    if (acknowledgeInstrumentChange) {
      this.instrumentChange = null;
    }

    this.benchmarkService.resumeRunSeries(
      seriesId,
      acknowledgeInstrumentChange ? { acknowledgeInstrumentChange: true } : undefined
    ).subscribe({
      next: () => {
        this.resumeInFlight = false;
        this.instrumentChange = null;
        // A resumed series is live again, so the poll and the ticker have to come back with it.
        this.startPolling();
        this.startElapsedTicker();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.resumeInFlight = false;
        if (err?.status === 409 && err.error?.instrumentChanged) {
          this.instrumentChange = err.error as BenchmarkInstrumentChangedDto;
        } else {
          this.errorMessage = this.describeError(err, 'Failed to continue the series');
        }
        this.cdr.detectChanges();
      }
    });
  }

  /**
   * The instrument moved and the operator chose a fresh series instead. This dialog has no way to
   * start one — that lives on the Run Benchmark tab — so it closes and leaves the operator there,
   * which is also what dismissing the dialog does everywhere else.
   */
  startNewSeriesInstead(): void {
    this.instrumentChange = null;
    this.requestClose();
  }

  /**
   * Hands the operator to the multi-run analysis for this series rather than embedding it here:
   * two native modals in the top layer trap focus between them, the same reason
   * `openMemberRunProgress` closes this dialog before opening the single-run one.
   */
  viewGroupReport(): void {
    if (!this.canViewReport) return;
    const groupId = this.series?.autoCreatedGroupId;
    if (groupId == null) return;
    this.openGroupAnalysis.emit(groupId);
    this.requestClose();
  }

  // -------------------------------------------------------------------------------------------
  // Formatting helpers. Local copies rather than imports: the originals are instance members of
  // AdminBenchmarkComponent, which is a component and not a utility.
  // -------------------------------------------------------------------------------------------

  formatThinkingLevel(level: string | null | undefined): string {
    if (!level) return 'Default';
    return level.charAt(0).toUpperCase() + level.slice(1);
  }

  showReasoningBadge(mode: string | null | undefined): boolean {
    if (!mode) return false;
    const lower = mode.toLowerCase();
    return lower !== 'default' && lower !== 'standard';
  }

  formatServiceTier(tier: string | null | undefined): string {
    if (!tier) return 'None';
    if (tier.toLowerCase() === 'standard_only') return 'Standard Only';
    return tier.charAt(0).toUpperCase() + tier.slice(1);
  }

  formatSecondOpinionMode(mode: number | null | undefined): string {
    const option = this.secondOpinionModeOptions.find(o => o.value === mode);
    return option && option.value !== BenchmarkSecondOpinionMode.Off ? option.label : '';
  }

  secondOpinionModeHintOf(mode: number | null | undefined): string {
    return this.secondOpinionModeOptions.find(o => o.value === mode)?.hint ?? '';
  }

  formatDuration(ms: number | null | undefined): string {
    if (!ms) return '0s';
    const totalSecs = Math.floor(ms / 1000);
    const mins = Math.floor(totalSecs / 60);
    const secs = totalSecs % 60;
    if (mins > 0) {
      return `${mins}m ${secs}s`;
    }
    return `${(ms / 1000).toFixed(1)}s`;
  }

  formatElapsed(ms: number): string {
    if (!ms || ms < 0) return '0s';
    const totalSecs = Math.floor(ms / 1000);
    const hours = Math.floor(totalSecs / 3600);
    const mins = Math.floor((totalSecs % 3600) / 60);
    const secs = totalSecs % 60;
    const pad = (n: number) => (n < 10 ? '0' + n : '' + n);
    if (hours > 0) return `${hours}h ${pad(mins)}m ${pad(secs)}s`;
    if (mins > 0) return `${mins}m ${pad(secs)}s`;
    return `${secs}s`;
  }

  get elapsedLabel(): string {
    const series = this.series;
    if (!series?.startedAtUtc) return '—';
    return this.formatElapsed(elapsedMsBetween(series.startedAtUtc, series.completedAtUtc));
  }

  get totalEstimatedCost(): number | null {
    const members = this.series?.members ?? [];
    const costs = members.map(m => m.estimatedCost).filter((c): c is number => c != null);
    if (costs.length === 0) return null;
    return costs.reduce((sum, c) => sum + c, 0);
  }

  memberStatusClass(member: BenchmarkRunSeriesMemberDto): string {
    switch (member.status) {
      case 'Running': return 'status-answering';
      case 'Completed': return 'status-scored';
      case 'CompletedWithErrors':
      case 'CompletedWithLimits': return 'status-ok';
      case 'Failed':
      case 'ProviderError': return 'status-failed';
      case 'Cancelled':
      case 'Canceled': return 'status-skipped';
      default: return 'status-pending';
    }
  }

  shortFingerprint(sha: string | null | undefined): string {
    return sha ? sha.substring(0, 8) : '-';
  }

  private describeError(err: any, fallback: string): string {
    const httpStatus = err?.status ? ` (HTTP ${err.status})` : '';
    const msg = typeof err?.error === 'string'
      ? err.error
      : (err?.error?.message || err?.message || fallback);
    return `${msg}${httpStatus}`;
  }

  private num(value: number | null | undefined, digits = 2): string {
    return value == null || Number.isNaN(value) ? 'n/a' : value.toFixed(digits);
  }

  /**
   * `run #<id>=<score>` pairs for one item, naming which run produced which score so a copied
   * diagnostics blob answers "which run collapsed" too. Rendered only when both arrays are
   * populated and the same length — a mismatched pairing would blame the wrong run, which is
   * worse than leaving the line without a vector at all.
   */
  private scoreVectorOf(item: { scores?: number[]; runIds?: number[] }): string {
    const scores = item.scores ?? [];
    const runIds = item.runIds ?? [];
    if (scores.length === 0 || runIds.length === 0 || scores.length !== runIds.length) {
      return '';
    }
    return runIds.map((runId, i) => `run #${runId}=${this.num(scores[i])}`).join(', ');
  }

  // -------------------------------------------------------------------------------------------
  // Diagnostics. Ids, hashes, counts, statuses and statistics only: never an API key, never a
  // prompt, never an answer. Operators paste these into bug reports.
  // -------------------------------------------------------------------------------------------

  private captureHeader(title: string): string[] {
    const lines: string[] = [];
    lines.push(`=== ${title} ===`);
    lines.push(`Captured:         ${new Date().toISOString()}`);
    lines.push(`Overseer build:   ${this.overseerBuildVersion || 'unknown'}`);
    lines.push(`Client:           ${typeof navigator !== 'undefined' ? navigator.userAgent : 'unknown'}`);

    let tz = 'unknown';
    let offsetStr = '';
    try {
      tz = Intl.DateTimeFormat().resolvedOptions().timeZone;
      const offsetMin = -new Date().getTimezoneOffset();
      const sign = offsetMin >= 0 ? '+' : '-';
      const absMin = Math.abs(offsetMin);
      const h = String(Math.floor(absMin / 60)).padStart(2, '0');
      const m = String(absMin % 60).padStart(2, '0');
      offsetStr = ` (UTC${sign}${h}:${m})`;
    } catch {
      // A locale without a resolvable zone is not worth failing a capture over.
    }
    lines.push(`Client timezone:  ${tz}${offsetStr}`);
    lines.push(`Page:             ${typeof location !== 'undefined' ? location.pathname : ''}`);
    lines.push('');
    return lines;
  }

  /**
   * Everything an operator would paste into a bug report about a series. The member-1 instrument
   * SHAs sit beside the current ones on purpose: a refused resume is then self-explaining from the
   * capture alone, without anyone having to re-derive which hash moved.
   */
  get seriesDiagnosticsText(): string {
    const series = this.series;
    const lines = this.captureHeader('BENCHMARK SERIES DIAGNOSTICS');

    if (!series) {
      lines.push(`No series detail received yet (series id: ${this.seriesId ?? 'none'}).`);
      lines.push('');
    } else {
      lines.push('--- SERIES ---');
      lines.push(`Series ID: ${series.id}, Suite: ${series.suiteName} (${series.benchmarkSuiteId ?? 'n/a'}), Status: ${series.status}`);
      lines.push(`Stop reason:      ${series.stopReason ?? 'none'}${series.stopReasonText ? ` (${series.stopReasonText})` : ''}`);
      lines.push(`Analysis stage:   ${this.stage} — ${this.stageLabel}`);
      lines.push(`Started (raw):    ${series.startedAtUtc}`);
      const startedParsed = series.startedAtUtc ? parseServerUtcDate(series.startedAtUtc).toISOString() : 'n/a';
      lines.push(`Started (parsed): ${startedParsed}`);
      lines.push(`Completed:        ${series.completedAtUtc ?? 'n/a'}`);
      lines.push(`Elapsed:          ${this.elapsedLabel}`);
      lines.push(`Requested ${series.requestedRunCount}, completed ${series.completedRunCount}, failed ${series.failedRunCount}`);
      lines.push(`allowCapWait=${series.allowCapWait}, resumable=${series.resumable}`);
      lines.push('');

      lines.push('--- INSTRUMENT ---');
      lines.push(`Member 1 candidate prompt SHA: ${series.firstMemberCandidateSystemPromptSha256 ?? 'not recorded'}`);
      lines.push(`Member 1 tool guides SHA:      ${series.firstMemberToolGuidesSha256 ?? 'not recorded'}`);
      lines.push(`Member 1 knowledge base SHA:   ${series.firstMemberKnowledgeBaseHeadSha ?? 'not recorded'}`);
      lines.push(`Current candidate prompt SHA:  ${series.currentCandidateSystemPromptSha256 ?? 'not recorded'}`);
      lines.push(`Current tool guides SHA:       ${series.currentToolGuidesSha256 ?? 'not recorded'}`);
      lines.push(`Current knowledge base SHA:    ${series.currentKnowledgeBaseHeadSha ?? 'not recorded'}`);
      const changed = series.changedInstrumentHashes ?? [];
      lines.push(`Changed since member 1: ${changed.length > 0 ? changed.join(', ') : 'none'}`);
      lines.push(`Instrument change acknowledged: ${series.instrumentChangeAcknowledged}`);
      if (this.instrumentChange) {
        lines.push(`Resume refused: ${this.instrumentChange.message}`);
      }
      lines.push('');
    }

    lines.push('--- COMPLIANCE LIMITS ---');
    if (this.limits) {
      const l = this.limits;
      lines.push(`Max runs per hour: ${l.maxRunsPerHour}, max runs per day: ${l.maxRunsPerDay}, max run count per series: ${l.maxRunCountPerSeries}`);
      lines.push(`Rolling window: ${l.runsInLastHour} run(s) in the last hour, ${l.runsInLast24Hours} in the last 24 hours`);
      lines.push(`Remaining daily headroom: ${l.remainingDailyHeadroom}`);
    } else {
      lines.push('not loaded');
    }
    lines.push('');

    lines.push('--- MEMBERS ---');
    const members = series?.members ?? [];
    if (members.length === 0) {
      lines.push('none launched yet');
    } else {
      for (const m of members) {
        lines.push(
          `${m.index}. run #${m.runId}, status ${m.status}, index ${m.qualityIndex ?? 'n/a'}, `
          + `speed ${m.speedIndex ?? 'n/a'}, cost ${m.estimatedCost != null ? '$' + this.num(m.estimatedCost, 4) : 'n/a'}, `
          + `duration ${m.durationMs != null ? this.formatDuration(m.durationMs) : 'n/a'}, `
          + `fingerprint ${this.shortFingerprint(m.shortFingerprint)}, `
          + `answered ${m.answeredQuestionCount} of ${m.totalQuestionCount}`);
      }
    }
    lines.push('');

    lines.push('--- GROUP ---');
    if (series?.autoCreatedGroupId != null) {
      lines.push(`Auto-created group: ${series.autoCreatedGroupId}, tier: ${series.autoCreatedGroupTier ?? 'not resolved'}`);
      if (this.groupAnalysis) {
        lines.push(`Analysis: id ${this.groupAnalysis.id}, computed ${this.groupAnalysis.computedAtUtc}, R=${this.groupAnalysis.runCount}, stale=${this.groupAnalysis.stale}`);
      } else if (this.analysisError) {
        lines.push(`Analysis: ${this.analysisError}`);
      } else {
        lines.push('Analysis: not computed yet');
      }
    } else {
      lines.push('Auto-created group: none — a group is created only for a series with two or more completed members');
    }
    lines.push('');

    lines.push('--- POLLING ---');
    const pollStr = this.pollTimer
      ? `active every ${MultiRunProgressDialogComponent.SERIES_POLL_INTERVAL_MS} ms`
      : 'stopped';
    lines.push(`Series poll: ${pollStr}`);
    const tickerStr = this.elapsedTimer
      ? `active every ${MultiRunProgressDialogComponent.ELAPSED_TICK_MS} ms`
      : 'stopped';
    lines.push(`Elapsed ticker: ${tickerStr}`);
    if (this.lastPollAtUtc) {
      const agoSec = Math.max(0, Math.floor((Date.now() - new Date(this.lastPollAtUtc).getTime()) / 1000));
      lines.push(`Last poll: ${this.lastPollAtUtc} (${agoSec}s ago)`);
    }
    if (this.lastPollError) {
      lines.push(`Last poll error: ${this.lastPollError}`);
    }
    lines.push(`Document hidden: ${typeof document !== 'undefined' ? document.hidden : false}`);
    lines.push('');

    lines.push('--- ERRORS ---');
    let hasError = false;
    if (series?.errorMessage) { lines.push(`Series error: ${series.errorMessage}`); hasError = true; }
    if (this.errorMessage) { lines.push(`Dialog error: ${this.errorMessage}`); hasError = true; }
    if (this.analysisError) { lines.push(`Analysis error: ${this.analysisError}`); hasError = true; }
    if (!hasError) { lines.push('none'); }
    lines.push('');

    return lines.join('\n');
  }

  /**
   * The statistical half. A tier verdict with no reasons is unusable in a bug report, so this
   * names the keys that were compared and the ones that matched, and both interval components
   * separately before the combined one — the whole point of the decomposition is that one shrinks
   * with *R* and the other does not.
   */
  get groupAnalysisDiagnosticsText(): string {
    const lines = this.captureHeader('BENCHMARK GROUP ANALYSIS DIAGNOSTICS');
    const analysis = this.groupAnalysis;
    const groupId = this.series?.autoCreatedGroupId ?? analysis?.groupId ?? null;

    lines.push('--- GROUP ---');
    if (!analysis) {
      lines.push(`Group ID: ${groupId ?? 'none'}, tier: ${this.series?.autoCreatedGroupTier ?? 'not resolved'}`);
      lines.push(this.analysisError
        ? `No analysis: ${this.analysisError}`
        : 'No analysis has been computed for this group yet.');
      lines.push('');
    } else {
      lines.push(`Group ID: ${analysis.groupId}, Name: ${analysis.groupName}`);
      lines.push(`Tier: ${analysis.tierLabel} (${analysis.tier})`);
      lines.push(`Analysis id ${analysis.id}, computed ${analysis.computedAtUtc}, stale=${analysis.stale}`);
      lines.push(`Harness version: ${analysis.harnessVersion ?? 'n/a'}, scoring method version: ${analysis.scoringMethodVersion}`);
      lines.push(`Member run ids: ${analysis.memberRunIds?.join(', ') || 'none'}`);
      lines.push(`R: ${analysis.runCount}`);
      lines.push('');
    }

    lines.push('--- COMPARABILITY ---');
    const comparability = this.groupComparability;
    if (!comparability) {
      lines.push('not resolved — the comparability keys could not be read for this group');
    } else {
      lines.push(`Verdict: ${comparability.tierLabel} (${comparability.tier}), key hash ${comparability.comparabilityKeyHash}`);
      lines.push(`Pooling permitted: ${comparability.poolingPermitted}, speed aggregates degraded: ${comparability.speedAggregatesDegraded}, cost aggregates degraded: ${comparability.costAggregatesDegraded}`);
      lines.push(`Explanation: ${comparability.explanation}`);
      lines.push(`Runs compared: ${comparability.runIds?.join(', ') || 'none'}`);
      const matched = comparability.matchedKeys ?? [];
      lines.push(`Keys matched (${matched.length}): ${matched.length > 0 ? matched.join(', ') : 'none'}`);
      const differences = comparability.differences ?? [];
      lines.push(`Keys differing (${differences.length}):`);
      if (differences.length === 0) {
        lines.push('  none');
      } else {
        for (const d of differences) {
          lines.push(`  ${d.name} [${d.kind}]: ${d.description}`);
          for (const v of d.variants ?? []) {
            lines.push(`    ${v.value} — runs ${v.runIds?.join(', ')}`);
          }
        }
      }
    }
    lines.push('');

    const result = analysis?.result as GroupStatisticsResultShape | undefined | null;
    lines.push('--- ITEMS ---');
    if (!result) {
      lines.push('no statistics record');
    } else {
      lines.push(`Items with at least one scored answer: ${result.itemCount ?? 'n/a'}`);
      lines.push(`Items excluded because no member answered them: ${result.unansweredItemCount ?? 0}`);
      // Labelled with the Q number, as the per-item lines below and the report both are. A bare
      // list of question ids under this heading reads as a count: one unstable item with id 70
      // printed as "70" says seventy items are unstable.
      const unstable = (result.unstableQuestionIds ?? []).map(questionId => {
        const item = (result.items ?? []).find(i => i.questionId === questionId);
        return item?.orderIndex != null ? `Q${item.orderIndex} (id ${questionId})` : `id ${questionId}`;
      });
      lines.push(`Unstable items (SD above threshold): ${unstable.length > 0 ? unstable.join(', ') : 'none'}`);
      lines.push(`Pooled index reportable: ${result.pooledIndexReportable ?? false}`);
      for (const item of result.items ?? []) {
        const scoreVector = this.scoreVectorOf(item);
        lines.push(
          `  Q${item.orderIndex} (id ${item.questionId}): n=${item.runCount ?? 0}, mean ${this.num(item.mean)}, `
          + `SD ${item.standardDeviation == null ? 'n/a' : this.num(item.standardDeviation)}, `
          + `CE rate ${this.num(item.criticalErrorRate)}, unstable=${item.unstable ?? false}, `
          + `insufficient=${item.insufficientRuns ?? false}`
          + (scoreVector ? `, scores: ${scoreVector}` : ''));
      }
    }
    lines.push('');

    lines.push('--- INDEX ---');
    const index = result?.index;
    if (!index) {
      lines.push('no index statistics');
    } else {
      lines.push(`Point estimate: ${this.num(index.pointEstimate)} (weighted mean of item means ${this.num(index.weightedMeanOfItemMeans)}, identity holds: ${index.identityHolds ?? false})`);
      lines.push(`Per-run indices: ${(index.perRunIndices ?? []).map(v => this.num(v)).join(', ') || 'none'}`);
      lines.push(`Reproducibility: SD ${index.reproducibilityStandardDeviation == null ? 'n/a' : this.num(index.reproducibilityStandardDeviation)}, `
        + `SE ${index.reproducibilityStandardError == null ? 'n/a' : this.num(index.reproducibilityStandardError)}, `
        + `t ${index.reproducibilityCriticalValue == null ? 'n/a' : this.num(index.reproducibilityCriticalValue, 3)}, `
        + `half-width ${index.reproducibilityHalfWidth == null ? 'n/a' : this.num(index.reproducibilityHalfWidth)} `
        + `(available: ${index.reproducibilityAvailable ?? false}; shrinks with R)`);
      lines.push(`Item sampling: SE ${index.itemSamplingStandardError == null ? 'n/a' : this.num(index.itemSamplingStandardError)}, `
        + `critical ${index.itemSamplingCriticalValue == null ? 'n/a' : this.num(index.itemSamplingCriticalValue, 3)}, `
        + `half-width ${index.itemSamplingHalfWidth == null ? 'n/a' : this.num(index.itemSamplingHalfWidth)} `
        + '(does NOT shrink with R: every run answers the same items)');
      lines.push(`Combined 95% interval: half-width ${index.combinedHalfWidth == null ? 'n/a' : this.num(index.combinedHalfWidth)}, `
        + `[${index.combinedLower == null ? 'n/a' : this.num(index.combinedLower)}, ${index.combinedUpper == null ? 'n/a' : this.num(index.combinedUpper)}]`);
    }
    lines.push('');

    lines.push('--- COMPARISON ---');
    const comparison = analysis?.comparison as GroupComparisonShape | undefined | null;
    if (!comparison) {
      lines.push(analysis?.comparedWithGroupId != null
        ? `compared with group ${analysis.comparedWithGroupId}, but no comparison record was stored`
        : 'no comparison group selected');
    } else {
      lines.push(`Compared with group ${analysis?.comparedWithGroupId ?? 'n/a'} (${analysis?.comparedWithGroupName ?? 'n/a'})`);
      lines.push(`Baseline runs: ${comparison.baselineRunIds?.join(', ') || 'none'}`);
      lines.push(`Treatment runs: ${comparison.treatmentRunIds?.join(', ') || 'none'}`);
      lines.push(`Paired items: ${comparison.pairedItemCount ?? 0}, unpaired and excluded: ${comparison.unpairedItemCount ?? 0}`);
      lines.push(`Mean paired difference: ${this.num(comparison.meanDifference)}, `
        + `SD ${comparison.differenceStandardDeviation == null ? 'n/a' : this.num(comparison.differenceStandardDeviation)}, `
        + `95% half-width ${comparison.differenceConfidenceHalfWidth == null ? 'n/a' : this.num(comparison.differenceConfidenceHalfWidth)}, `
        + `[${comparison.differenceConfidenceLower == null ? 'n/a' : this.num(comparison.differenceConfidenceLower)}, `
        + `${comparison.differenceConfidenceUpper == null ? 'n/a' : this.num(comparison.differenceConfidenceUpper)}]`);
      const w = comparison.wilcoxon;
      lines.push(`Wilcoxon signed-rank (primary): n=${w?.sampleSize ?? 0}, statistic ${this.num(w?.statistic)}, `
        + `p ${w?.pValue == null ? 'n/a' : this.num(w.pValue, 4)}, zero differences ${w?.zeroDifferenceCount ?? 0}, `
        + `ties ${w?.tiesPresent ?? false}, method ${w?.method ?? 'n/a'}`);
      const t = comparison.pairedT;
      lines.push(`Paired t (secondary): n=${t?.sampleSize ?? 0}, t ${t?.tStatistic == null ? 'n/a' : this.num(t.tStatistic, 3)}, `
        + `df ${t?.degreesOfFreedom == null ? 'n/a' : this.num(t.degreesOfFreedom, 1)}, `
        + `SE ${t?.standardError == null ? 'n/a' : this.num(t.standardError)}, `
        + `p ${t?.pValue == null ? 'n/a' : this.num(t.pValue, 4)}`);
      lines.push(`Cohen's dz: ${comparison.cohensDz == null ? 'n/a' : this.num(comparison.cohensDz, 3)}`);
      lines.push(`Per-item comparisons: ${comparison.itemComparisons?.length ?? 0}, exploratory under BH FDR ${this.num(comparison.falseDiscoveryRate, 3)}`);
    }
    lines.push('');

    const caveat = result?.varianceDecompositionCaveat;
    if (caveat) {
      lines.push('--- CAVEAT ---');
      lines.push(caveat);
      lines.push('');
    }

    return lines.join('\n');
  }

  get seriesDiagnosticsCopyStatus(): string {
    if (this.copiedSeriesDiagnostics) return 'Series diagnostics copied to clipboard';
    return this.seriesDiagnosticsCopyFailed ? 'Could not copy the series diagnostics to the clipboard.' : '';
  }

  get groupDiagnosticsCopyStatus(): string {
    if (this.copiedGroupDiagnostics) return 'Group analysis diagnostics copied to clipboard';
    return this.groupDiagnosticsCopyFailed ? 'Could not copy the group analysis diagnostics to the clipboard.' : '';
  }

  async copySeriesDiagnostics(): Promise<void> {
    const text = this.seriesDiagnosticsText;
    if (!text) return;
    try {
      await navigator.clipboard.writeText(text);
      this.seriesDiagnosticsCopyFailed = false;
      this.copiedSeriesDiagnostics = true;
      if (this.copiedSeriesTimer) { clearTimeout(this.copiedSeriesTimer); }
      this.copiedSeriesTimer = setTimeout(() => {
        this.copiedSeriesDiagnostics = false;
        this.copiedSeriesTimer = null;
        this.cdr.detectChanges();
      }, MultiRunProgressDialogComponent.COPIED_RESET_MS);
    } catch {
      // Surfaced, never swallowed: a clipboard the browser refused is exactly the case where a
      // silent no-op leaves the operator pasting the previous capture into a bug report.
      this.copiedSeriesDiagnostics = false;
      this.seriesDiagnosticsCopyFailed = true;
      this.errorMessage = 'Could not copy the series diagnostics to the clipboard.';
    }
    this.cdr.detectChanges();
  }

  async copyGroupAnalysisDiagnostics(): Promise<void> {
    const text = this.groupAnalysisDiagnosticsText;
    if (!text) return;
    try {
      await navigator.clipboard.writeText(text);
      this.groupDiagnosticsCopyFailed = false;
      this.copiedGroupDiagnostics = true;
      if (this.copiedGroupTimer) { clearTimeout(this.copiedGroupTimer); }
      this.copiedGroupTimer = setTimeout(() => {
        this.copiedGroupDiagnostics = false;
        this.copiedGroupTimer = null;
        this.cdr.detectChanges();
      }, MultiRunProgressDialogComponent.COPIED_RESET_MS);
    } catch {
      this.copiedGroupDiagnostics = false;
      this.groupDiagnosticsCopyFailed = true;
      this.errorMessage = 'Could not copy the group analysis diagnostics to the clipboard.';
    }
    this.cdr.detectChanges();
  }
}
