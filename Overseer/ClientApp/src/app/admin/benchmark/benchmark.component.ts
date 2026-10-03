import {
  Component,
  OnInit,
  OnDestroy,
  OnChanges,
  AfterViewInit,
  SimpleChanges,
  Input,
  Output,
  EventEmitter,
  ChangeDetectorRef,
  ViewChild,
  ElementRef,
  inject
} from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import {
  AdminBenchmarkService,
  BenchmarkSuiteDto,
  BenchmarkQuestionDto,
  BenchmarkRunSummaryDto,
  BenchmarkRunDetailDto,
  BenchmarkRunAnswerDto,
  BenchmarkRunOutcomeSummaryDto,
  DifficultyAssessmentJobDto,
  BenchmarkAssessorCalibrationDto,
  BenchmarkCalibrationTarget,
  BenchmarkCoAssessmentRecord,
  BenchmarkPanelMember,
  BenchmarkSecondOpinionMode,
  BENCHMARK_SECOND_OPINION_MODES,
  BENCHMARK_REFERENCE_READER_COVERAGE,
  BenchmarkGameSnapshotDto,
  BenchmarkToolCallDto,
  BoardFactsCheckDto,
  BoardFactIssueDto,
  BenchmarkRunReportDocumentsStatus
} from '../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../services/admin.service';
import { MultiRunComponent } from './multi-run/multi-run.component';
import { MultiRunProgressDialogComponent } from './multi-run/multi-run-progress-dialog.component';
import { BenchmarkBatteriesComponent } from './batteries/batteries.component';
import { BatteryProgressDialogComponent } from './batteries/battery-progress-dialog.component';
import { BatteryRunReportDialogComponent } from './batteries/battery-run-report-dialog.component';
import { RunPairedTestComponent } from './run-paired-test/run-paired-test.component';
import { BenchmarkCostPanelComponent, apportionWholePercentShares } from './cost-panel/benchmark-cost-panel.component';
import {
  BenchmarkGraderGuideComponent,
  GraderGuideProfile,
  GraderGuideSection
} from './grader-guide/benchmark-grader-guide.component';
import {
  BenchmarkFamilyRelation,
  BenchmarkSynthesisPanelComponent,
  BenchmarkSynthesisView
} from './synthesis-panel/benchmark-synthesis-panel.component';
import { SnapshotViewerComponent } from '../../shared/snapshot-viewer/snapshot-viewer.component';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';
import { SystemService } from '../../services/system.service';
import { BenchmarkCompletionSoundService } from '../../services/benchmark-completion-sound.service';
import { BenchmarkCompletionNotificationService } from '../../services/benchmark-completion-notification.service';
import { BenchmarkBackgroundActivityService } from '../../services/benchmark-background-activity.service';
import { parseServerUtcDate, elapsedMsBetween } from '../../utils/date.util';
import { formatThinkingLevel, showReasoningBadge, formatServiceTier, formatDifficulty } from '../../utils/model-badge-format.util';
import {
  ModelComparisonComponent
} from './model-comparison/model-comparison.component';
import {
  ComparisonSourcePickerComponent
} from './model-comparison/comparison-source-picker.component';
import { ProviderBadgeComponent } from '../../shared/provider-badge/provider-badge.component';
import {
  ModelPickerComponent,
  ModelPickerKey,
  ModelPickerModel,
  ModelPickerOption
} from '../../shared/model-picker/model-picker.component';
import { InfoTipComponent } from '../../shared/info-tip/info-tip.component';
import { firstValueFrom, of } from 'rxjs';
import { RunReportFrameComponent } from './run-report-frame/run-report-frame.component';
import { KeyFigureCardActionsComponent, KeyFigureCardExportRequest } from './run-report-frame/key-figure-card-actions.component';
import {
  ImageContext,
  KeyFigureKey,
  KeyFiguresAction,
  exportKeyFiguresImage,
  readKeyFigureCells,
  readStoredImageDetailExclusions,
  readStoredKeyFigureExclusions,
  statusImageTone,
  storeImageDetailExclusions,
  storeKeyFigureExclusions,
  toImageFactRows
} from './run-report-frame/key-figures-image';
import { KeyFiguresChooserComponent } from './run-report-frame/key-figures-chooser.component';
import { RunFactsComponent } from './run-report-frame/run-facts.component';
import {
  RUN_FACT_PRIMARY_KEYS,
  RunFactRow,
  buildRunFacts,
  candidatePromptParts,
  formatCandidatePrompt,
  runFactPlainText,
  runFactsReadout
} from './run-report-frame/run-facts';
import { BenchmarkDownloadCenterComponent } from './download-center/benchmark-download-center.component';
import { audienceLabel } from './report-pack/report-document-format';
import { RunAiReportsComponent, RunReportStatusChange } from './run-ai-reports/run-ai-reports.component';
import { copyToClipboard } from '../../utils/clipboard.util';
import { downloadTextFile, safeFileName } from '../../utils/download.util';
import { jobStatusLabel } from '../../utils/job-status-label.util';
import {
  COPY_STATUS_MS,
  RUN_REPORT_TABS,
  RunReportTabKey,
  RUN_REPORT_TAB_STORAGE_KEY,
  RUN_REPORT_HEADER_STORAGE_KEY,
  readStoredRunHeaderDetailsOpen,
  BenchmarkRunProgressRow,
  BenchmarkToolFamilyName,
  BENCHMARK_TOOL_FAMILY_LABELS,
  classifyBenchmarkTool,
  BenchmarkToolFamilyRow,
  BenchmarkSourceShareCorrelations,
  BenchmarkRunStage,
  RunReportQuestionFilter,
  RunReportRerunAction,
  RunDiagnosticsFacts,
  RunReportConfigRow,
  BenchmarkNavigationRequest
} from './benchmark.models';
import {
  formatCostAmount,
  formatRunEstimatedCost,
  reportDocumentsStatusOf,
  formatStatus,
  formatStatusLabel,
  statusBadgeClass,
  getScoreBadgeClass,
  isAbortedRun,
  elapsedBetweenTimestamps,
  answerShortfallOf,
  formatDuration,
  formatElapsed,
  DELIBERATING_THINKING_LEVELS,
  INTERACTIVE_SPEED_TARGET_MAX_MS,
  MISSING_BOARD_QUOTE_LIST_CAP
} from './benchmark-run-format';
import { BenchmarkWorkspaceStore } from './state/benchmark-workspace.store';
import { BenchmarkLauncherState } from './state/benchmark-launcher.state';
import { BenchmarkDifficultyJobService } from './state/benchmark-difficulty-job.service';
import { BenchmarkComparisonState } from './state/benchmark-comparison.state';
import { BenchmarkActiveRunMonitor } from './state/benchmark-active-run.monitor';
import { BenchmarkViewSync } from './state/benchmark-view-sync.service';
import { BenchmarkShellBridge, BenchmarkSubTab } from './state/benchmark-shell-bridge.service';
import { BenchmarkRunTabComponent } from './run-tab/benchmark-run-tab.component';
import { BenchmarkHistoryTabComponent } from './history-tab/benchmark-history-tab.component';
import { BenchmarkSuitesTabComponent } from './suites-tab/benchmark-suites-tab.component';
import { BenchmarkProfilesTabComponent } from './profiles-tab/benchmark-profiles-tab.component';
import { BenchmarkComparisonTabComponent } from './comparison-tab/benchmark-comparison-tab.component';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

export * from './benchmark.models';

@Component({
  selector: 'app-admin-benchmark',
  standalone: true,
  imports: [
    CommonModule, DecimalPipe, SnapshotViewerComponent, MultiRunComponent, MultiRunProgressDialogComponent, BenchmarkBatteriesComponent, BatteryProgressDialogComponent, BatteryRunReportDialogComponent, RunPairedTestComponent, ModelComparisonComponent, ComparisonSourcePickerComponent, BenchmarkCostPanelComponent, BenchmarkSynthesisPanelComponent, ProviderBadgeComponent, ModelPickerComponent, InfoTipComponent, BenchmarkGraderGuideComponent, RunReportFrameComponent, KeyFigureCardActionsComponent, KeyFiguresChooserComponent, RunFactsComponent, BenchmarkDownloadCenterComponent, RunAiReportsComponent, BenchmarkRunTabComponent, BenchmarkHistoryTabComponent, BenchmarkSuitesTabComponent, BenchmarkProfilesTabComponent, BenchmarkComparisonTabComponent
  ],
  templateUrl: './benchmark.component.html',
  styleUrls: ['./benchmark.component.scss'],
  providers: [
    BenchmarkViewSync, BenchmarkShellBridge, BenchmarkWorkspaceStore, BenchmarkLauncherState,
    BenchmarkDifficultyJobService, BenchmarkComparisonState, BenchmarkActiveRunMonitor
  ]
})
export class AdminBenchmarkComponent implements OnInit, AfterViewInit, OnDestroy, OnChanges {
  readonly workspace = inject(BenchmarkWorkspaceStore);
  readonly launcher = inject(BenchmarkLauncherState);
  readonly difficulty = inject(BenchmarkDifficultyJobService);
  readonly comparison = inject(BenchmarkComparisonState);
  readonly monitor = inject(BenchmarkActiveRunMonitor);
  private benchmarkService = inject(AdminBenchmarkService);
  private completionSoundService = inject(BenchmarkCompletionSoundService);
  private completionNotificationService = inject(BenchmarkCompletionNotificationService);
  private backgroundActivity = inject(BenchmarkBackgroundActivityService);
  private systemService = inject(SystemService);
  readonly secondOpinionModeOptions = BENCHMARK_SECOND_OPINION_MODES;
  formatDifficulty(diff: string | number): string {
    return formatDifficulty(diff);
  }
  readonly jobStatusLabel = jobStatusLabel;
  formatThinkingLevel(level: string | null | undefined): string {
    return formatThinkingLevel(level);
  }
  formatServiceTier(tier: string | null | undefined): string {
    return formatServiceTier(tier);
  }
  showReasoningBadge(mode: string | null | undefined): boolean {
    return showReasoningBadge(mode);
  }
  readonly formatCostAmount = formatCostAmount;
  readonly formatStatus = formatStatus;
  readonly reportDocumentsStatusOf = reportDocumentsStatusOf;
  readonly formatDuration = formatDuration;
  readonly formatElapsed = formatElapsed;
  readonly isAbortedRun = isAbortedRun;
  readonly formatStatusLabel = formatStatusLabel;
  readonly statusBadgeClass = statusBadgeClass;
  readonly elapsedBetweenTimestamps = elapsedBetweenTimestamps;
  readonly formatRunEstimatedCost = formatRunEstimatedCost;
  readonly getScoreBadgeClass = getScoreBadgeClass;
  readonly answerShortfallOf = answerShortfallOf;

  private readonly viewSync = inject(BenchmarkViewSync);
  private readonly bridge = inject(BenchmarkShellBridge);

  constructor() {
    // Service state changes outside this component's own events; OnPush needs telling.
    this.viewSync.changed$.pipe(takeUntilDestroyed()).subscribe(() => {
      this.cdr.markForCheck();
      this.cdr.detectChanges();
    });
    // The sub-tabs and the state services reach the tab row and the dialogs hosted here.
    const bridge = this.bridge;
    bridge.selectSubTab$.pipe(takeUntilDestroyed()).subscribe(tab => this.selectSubTab(tab));
    bridge.openRunReport$.pipe(takeUntilDestroyed()).subscribe(runId => this.viewRunDetail(runId));
    bridge.openRunProgress$.pipe(takeUntilDestroyed()).subscribe(fromSeries => this.openRunProgressDialog(fromSeries));
    bridge.openDifficultyAssessor$.pipe(takeUntilDestroyed())
      .subscribe(request => this.openDifficultyAssessorDialog(request.suite, request.question));
    bridge.openSnapshotViewer$.pipe(takeUntilDestroyed()).subscribe(snapshotId => this.openSnapshotViewer(snapshotId));
    bridge.closeSnapshotViewerFor$.pipe(takeUntilDestroyed()).subscribe(snapshotId => {
      if (this.snapshotViewer?.snapshotId === snapshotId && this.snapshotViewer.viewerDialog?.nativeElement?.open) {
        this.snapshotViewer.close();
      }
    });
    bridge.openGraderGuide$.pipe(takeUntilDestroyed()).subscribe(request => this.openGraderGuide(request.section, request.profile));
    bridge.confirm$.pipe(takeUntilDestroyed()).subscribe(options => this.openConfirmDialog(options));
    bridge.openComparisonWizard$.pipe(takeUntilDestroyed()).subscribe(preset => {
      if (preset) {
        this.comparison.applyComparisonPreset(preset);
      }
      this.openComparisonWizard();
    });
    bridge.openBatteryRunReport$.pipe(takeUntilDestroyed()).subscribe(batteryRunId => this.openBatteryRunReport(batteryRunId));
    bridge.runDeleted$.pipe(takeUntilDestroyed()).subscribe(runId => {
      if (this.selectedRunDetail?.id === runId) {
        this.closeRunDetail();
      }
    });
  }

  /** The admin page's configurations; the workspace store holds them for every sub-tab. */
  @Input() set systemConfigs(configs: SystemAiConfigDto[]) {
    this.workspace.setSystemConfigs(configs);
  }

  get systemConfigs(): SystemAiConfigDto[] {
    return this.workspace.systemConfigs;
  }

  /**
   * A run the host page asks to open in the run detail dialog. Opened once the view exists, after
   * which `openRunHandled` tells the host to clear its request.
   */
  @Input() set openRunId(id: number | null | undefined) {
    this.pendingOpenRunId = id ?? null;
    if (this.viewInitialised) {
      this.openPendingRun();
    }
  }

  @Output() openRunHandled = new EventEmitter<void>();

  private pendingOpenRunId: number | null = null;

  private viewInitialised = false;

  /**
   * A sub-tab, and on Manage Suites a suite, the host page asks to show. Applied once `ngOnInit`
   * has run, after which `navigationHandled` tells the host to clear its request.
   */
  @Input() set navigation(request: BenchmarkNavigationRequest | null | undefined) {
    this.pendingNavigation = request ?? null;
    if (this.initialised && this.pendingNavigation) {
      this.applyNavigation(this.pendingNavigation);
    }
  }

  @Output() navigationHandled = new EventEmitter<void>();

  private pendingNavigation: BenchmarkNavigationRequest | null = null;

  private initialised = false;

  @ViewChild('runDetailDialog') runDetailDialog!: ElementRef<HTMLDialogElement>;

  @ViewChild('runDownloadCenter') runDownloadCenter?: BenchmarkDownloadCenterComponent;

  @ViewChild(RunReportFrameComponent) runReportFrame?: RunReportFrameComponent;

  @ViewChild('rerunPopover') rerunPopover?: ElementRef<HTMLElement>;

  @ViewChild('rerunTrigger') rerunTrigger?: ElementRef<HTMLButtonElement>;

  @ViewChild('confirmActionDialog') confirmActionDialog!: ElementRef<HTMLDialogElement>;

  @ViewChild('difficultyAssessorDialog') difficultyAssessorDialog!: ElementRef<HTMLDialogElement>;

  @ViewChild('difficultyProgressHeading') difficultyProgressHeading?: ElementRef<HTMLElement>;

  @ViewChild('retryDialog') retryDialog!: ElementRef<HTMLDialogElement>;

  @ViewChild('runProgressDialog') runProgressDialog!: ElementRef<HTMLDialogElement>;

  @ViewChild('runProgressHeading') runProgressHeading?: ElementRef<HTMLElement>;

  @ViewChild('comparisonWizardDialog') comparisonWizardDialog?: ElementRef<HTMLDialogElement>;

  /**
   * The wizard instance, for the two things the host cannot reach through the DOM: focusing the
   * heading it owns, and asking whether an export is in flight before allowing a close.
   */
  @ViewChild(ModelComparisonComponent) comparisonWizard?: ModelComparisonComponent;

  @ViewChild('snapshotViewer') snapshotViewer?: SnapshotViewerComponent;

  @ViewChild('multiRunPanel') multiRunPanel?: MultiRunComponent;

  @ViewChild('batteriesPanel') batteriesPanel?: BenchmarkBatteriesComponent;

  @ViewChild('batteryRunReport') batteryRunReport?: BatteryRunReportDialogComponent;

  @ViewChild('graderGuide') graderGuide?: BenchmarkGraderGuideComponent;

  /** The scoring profile whose settings the grader guide prints; null prints the Standard defaults. */
  graderGuideProfile: GraderGuideProfile | null = null;

  // Confirm Action Dialog State
  confirmDialogTitle = '';

  confirmDialogMessage = '';

  confirmDialogDangerNotice = '';

  confirmDialogButtonText = 'Delete';

  confirmDialogButtonClass = 'btn-gh btn-gh-delete';

  /**
   * Whether the confirm dialog's affirmative button carries a trash icon.
   * 'none' for a plain confirmation, where the label alone is clearer than a
   * generic tick — see the frontend_ui_controls skill on when an icon earns
   * its place.
   */
  confirmDialogIcon: 'delete' | 'none' = 'delete';

  pendingConfirmAction: (() => void) | null = null;

  cdr = inject(ChangeDetectorRef);

  activeSubTab: 'run' | 'history' | 'multirun' | 'multisuite' | 'suites' | 'profiles' | 'modelcomparison' = 'run';

  /**
   * Tab order, and the source of truth for arrow-key navigation indices. Multi-Run Analysis sits
   * immediately right of Run History because a group is built out of the runs listed there, so the
   * two are read in that order, and Multi-Suite follows them. Scoring Profiles sits right of Manage
   * Suites.
   */
  readonly subTabs = ['run', 'history', 'multirun', 'multisuite', 'suites', 'profiles', 'modelcomparison'] as const;

  /**
   * BenchmarkAnswerFlags bits that mean the graded text was corrupted in transport:
   * Empty = 1, HarnessArtifacts = 2, Truncated = 4.
   */
  private static readonly TRANSPORT_DEFECT_FLAGS = 1 | 2 | 4;

  /**
   * BenchmarkAnswerFlags bits that are advisory only and must never be presented as a
   * failure: ReasoningBleed = 8, RepeatedFragments = 16, ContestedVerdict = 32,
   * UnevidencedDeduction = 64, RefutedClaim = 128, OmissionAsAccuracy = 256,
   * OutOfRubricAccuracyDeduction = 512, AnswerFramingOpener = 1024, ContestedCriticalError = 2048,
   * ContestedAccuracyDeduction = 4096, DimensionOutlier = 8192. Must track
   * BenchmarkRunFinalizer.AdvisoryFlags on the server as the source of truth.
   */
  private static readonly ADVISORY_FLAGS = 8 | 16 | 32 | 64 | 128 | 256 | 512 | 1024 | 2048 | 4096 | 8192;

  /** The same advisory members by name, as they arrive in answerFlagNames. */
  private static readonly ADVISORY_FLAG_NAMES: readonly string[] = [
    'ReasoningBleed',
    'RepeatedFragments',
    'ContestedVerdict',
    'UnevidencedDeduction',
    'RefutedClaim',
    'OmissionAsAccuracy',
    'OutOfRubricAccuracyDeduction',
    'AnswerFramingOpener',
    'ContestedCriticalError',
    'ContestedAccuracyDeduction',
    'DimensionOutlier'
  ];

  /**
   * The harness version from which the second opinion and the final synthesis carry costs of
   * their own. Mirrors BenchmarkReportBuilder's PredatesHarnessVersion constants.
   */
  private static readonly PER_ROLE_COST_HARNESS_VERSION = 15;

  copiedRunDiagnostics = false;

  runDiagnosticsPanelOpen = false;

  private copiedRunDiagnosticsTimer: ReturnType<typeof setTimeout> | null = null;

  /** Which series the progress dialog shows: an explicitly opened one, else the live one. */
  get dialogSeriesId(): number | null {
    return this.monitor.seriesDialogId ?? this.monitor.activeSeriesId;
  }

  /** Which battery run the progress dialog shows: an explicitly opened one, else the live one. */
  get dialogBatteryRunId(): number | null {
    return this.monitor.batteryDialogRunId ?? this.monitor.activeBatteryRunId;
  }

  // Detail Modal
  selectedRunDetail: BenchmarkRunDetailDto | null = null;

  loadingDetail = false;

  /** The run the report dialog is showing or loading; the header names it while the detail is absent. */
  runDetailRequestedId: number | null = null;

  /** Why the run detail could not be loaded, or null. */
  runDetailLoadError: string | null = null;

  /** Discards a detail response that arrives after the dialog closed or moved to another run. */
  private runDetailLoadToken = 0;

  /** The pressed question filters of the run report. */
  questionFilters = new Set<RunReportQuestionFilter>();

  /** Whether the Re-run popover is open, from its toggle event; the trigger's aria-expanded. */
  rerunPopoverOpen = false;

  /** The run report's Copy diagnostics announcement. */
  runReportCopyStatus = '';

  private runReportCopyTimer: ReturnType<typeof setTimeout> | null = null;

  /** A key-figures image is being composed; another export is refused until it finishes. */
  keyFiguresExporting = false;

  /** The key figures the Summary panel and the whole-strip image leave out, remembered for every run report. */
  keyFigureExclusions: string[] = readStoredKeyFigureExclusions();

  /** The run-fact rows the key-figures images leave out, remembered for every run report. */
  imageDetailExclusions: string[] = readStoredImageDetailExclusions();

  @ViewChild(KeyFiguresChooserComponent) keyFiguresChooser?: KeyFiguresChooserComponent;

  /** The run whose facts `selectedRunFacts` last built, and the rows it built. */
  private runFactsSource: BenchmarkRunDetailDto | null = null;

  private runFactsRows: RunFactRow[] = [];

  expandedQuestions = new Set<number>();

  expandedThoughts = new Set<number>();

  expandedArtifacts = new Set<number>();

  /**
   * Tool-call disclosure state, all keyed by `orderIndex` like the sets above — the fetch
   * itself needs the answer's database id, never `orderIndex`, so `toggleToolCalls` takes
   * the whole answer rather than a bare number.
   */
  expandedToolCalls = new Set<number>();

  /** Loaded rows per answer. Present as a key (even for an empty run) means "already fetched" — that is what stops a second expand from refetching. */
  toolCallsByAnswer = new Map<number, BenchmarkToolCallDto[]>();

  loadingToolCalls = new Set<number>();

  toolCallsErrorByAnswer = new Map<number, string>();

  /** Which individual call's arguments/result panel is open, keyed by `orderIndex:callId:field`. */
  expandedToolCallFields = new Set<string>();

  rescoringRun = false;

  detailPollInterval: any = null;

  reassessingAnswerId: number | null = null;

  trialReassessingAnswerId: number | null = null;

  rerunningAnswerId: number | null = null;

  // Calibration panel. A calibration grades a finished run with another model and records only
  // the agreement statistics — no score, level, flag or index moves — so this is where a
  // prospective assessor earns its promotion, beside what it cost.
  calibrations: BenchmarkAssessorCalibrationDto[] = [];

  loadingCalibrations = false;

  calibrating = false;

  calibrationErrorMessage: string | null = null;

  calibrationAssessorConfigId: number | null = null;

  /** What a panel run's calibration compares against. A single-assessor run sends none. */
  calibrationTarget: BenchmarkCalibrationTarget = 'Assessor';

  readonly calibrationTargetOptions: readonly { value: BenchmarkCalibrationTarget; label: string }[] = [
    { value: 'Assessor', label: 'Assessor A' },
    { value: 'CoAssessor', label: 'Co-assessor B' },
    { value: 'Panel', label: 'Panel (mean of A and B)' }
  ];

  private calibrationTargetOptionsSource: BenchmarkRunDetailDto | null = null;

  private calibrationTargetOptionsCache: ModelPickerOption<ModelPickerModel>[] = [];

  runningSynthesis = false;

  retryingAssessments = false;

  retryingClaimVerification = false;

  // Retry Dialog
  /**
   * 'assessment' replaces the verdict and moves the published index; 'trial' records a verdict in
   * the second-opinion slot and moves nothing. That is the most consequential distinction on this
   * screen, so the two are separate scopes rather than a flag on one.
   */
  retryScope: 'assessment' | 'trial' | 'question' | 'synthesis' | 'assessments' | 'claim-verification' | null = null;

  retryRunId: number | null = null;

  retryAnswer: BenchmarkRunAnswerDto | null = null;

  retryAssessorConfigId: number | null = null;

  /** The members a panel run's re-assessment re-grades. */
  retryPanelMember: BenchmarkPanelMember = 'Both';

  readonly retryPanelMemberOptions: readonly { value: BenchmarkPanelMember; label: string }[] = [
    { value: 'Both', label: 'Both members' },
    { value: 'A', label: 'Member A (assessor) only' },
    { value: 'B', label: 'Member B (co-assessor) only' }
  ];

  // Difficulty Assessor Dialog State
  suiteForDifficultyAssessment: BenchmarkSuiteDto | null = null;

  difficultyAssessorConfigId: number | null = null;

  difficultyAssessmentScope: 'suite' | 'unassessed' | 'question' = 'suite';

  questionIdForDifficultyAssessment: number | null = null;

  difficultyDialogPhase: 'select' | 'progress' = 'select';

  difficultyJobStarting = false;

  difficultyDialogError: string | null = null;

  copiedDiagnostics = false;

  private copiedDiagnosticsTimer: ReturnType<typeof setTimeout> | null = null;

  get suiteIsPartiallyAssessed(): boolean {
    const s = this.suiteForDifficultyAssessment;
    return s != null && s.assessedQuestionCount > 0 && !s.difficultyFullyAssessed;
  }

  get unassessedQuestionCount(): number {
    const s = this.suiteForDifficultyAssessment;
    return s == null ? 0 : Math.max(0, s.questionCount - s.assessedQuestionCount);
  }

  get difficultyAssessmentTargetDescription(): string {
    const s = this.suiteForDifficultyAssessment;
    const total = s?.questionCount || 0;
    switch (this.difficultyAssessmentScope) {
      case 'unassessed':
        return `the ${this.unassessedQuestionCount} of ${total} questions in ${s?.name} that do not yet have an assessed difficulty`;
      case 'suite':
        return `${s?.name} — ${total} questions`;
      default:
        return 'this question';
    }
  }

  get difficultyDiagnosticsText(): string {
    if (!this.difficulty.difficultyJob) return '';
    const lines: string[] = [];
    lines.push(`Job ID: ${this.difficulty.difficultyJob.id}`);
    lines.push(`Suite: ${this.difficulty.difficultyJob.suiteName} (ID: ${this.difficulty.difficultyJob.suiteId})`);
    lines.push(`Assessor: ${this.difficulty.difficultyJob.assessorDisplayName}`);
    lines.push(`Status: ${this.difficulty.difficultyJob.status}`);
    lines.push(`Model Calls: ${this.difficulty.difficultyJob.totalModelCalls}`);
    lines.push(`Prompt Tokens: ${this.difficulty.difficultyJob.promptTokens}, Output Tokens: ${this.difficulty.difficultyJob.outputTokens}`);
    lines.push('');
    lines.push('--- LOG ---');
    for (const entry of this.difficulty.difficultyJob.log) {
      lines.push(`[${entry.timestampUtc}] [${entry.severity.toUpperCase()}] ${entry.message}`);
      if (entry.rawExcerpt) {
        lines.push(`  Excerpt: ${entry.rawExcerpt}`);
      }
    }
    return lines.join('\n');
  }

  async copyDifficultyDiagnostics(): Promise<void> {
    const text = this.difficultyDiagnosticsText;
    if (!text) { return; }
    try {
      await navigator.clipboard.writeText(text);
      this.copiedDiagnostics = true;
      if (this.copiedDiagnosticsTimer) { clearTimeout(this.copiedDiagnosticsTimer); }
      this.copiedDiagnosticsTimer = setTimeout(() => {
        this.copiedDiagnostics = false;
        this.copiedDiagnosticsTimer = null;
        this.viewSync.notify();
      }, 2000);
    } catch {
      this.difficultyDialogError = 'Could not copy the diagnostics to the clipboard.';
    }
  }

  ngOnInit() {
    ensureOverlayPolyfills();
    // Read before the loaders run: each one applies the field it owns as it picks its own fallback.
    this.launcher.restoreRunSettings();
    this.workspace.loadSuites();
    this.workspace.loadProfiles();
    this.workspace.loadHistory();
    this.launcher.setDefaultModelSelections();
    this.difficulty.checkActiveDifficultyAssessment();
    this.monitor.checkActiveRun();
    // The Number of runs field cannot bound itself until the caps arrive, and a series already
    // running must reattach its banner exactly as a single run does.
    this.workspace.loadRunLimits();
    this.monitor.checkActiveRunSeries();
    this.workspace.loadBatteries();
    this.monitor.checkActiveBatteryRun();
    this.initialised = true;
    if (this.pendingNavigation) {
      this.applyNavigation(this.pendingNavigation);
    }
  }

  /**
   * Shows the requested sub-tab, through `selectSubTab` so its entry loads run. A suite id leads to
   * Manage Suites when no valid sub-tab is named, and is ignored on any other sub-tab.
   */
  private applyNavigation(request: BenchmarkNavigationRequest): void {
    this.pendingNavigation = null;
    const named = this.subTabs.find(t => t === request.subTab);
    const target = named ?? (request.suiteId != null ? 'suites' : null);
    if (target === 'suites' && request.suiteId != null) {
      this.workspace.pendingFocusSuiteId = request.suiteId;
    }
    if (target) {
      this.selectSubTab(target);
    }
    // Deferred: the host must not clear its binding inside the check that set it.
    queueMicrotask(() => this.navigationHandled.emit());
  }

  ngAfterViewInit(): void {
    this.viewInitialised = true;
    // Deferred: opening emits to the host, which must not change its bindings inside this check.
    if (this.pendingOpenRunId != null) {
      queueMicrotask(() => this.openPendingRun());
    }
  }

  /**
   * Switches the visible sub-tab. Each sub-tab's component loads what it shows when it is created.
   * Marked for check, since a request through the bridge comes from outside this view's own events.
   */
  selectSubTab(tab: BenchmarkSubTab): void {
    this.activeSubTab = tab;
    this.cdr.markForCheck();
  }
  // ---------------------------------------------------------------------------------------------
  // The comparison wizard dialog
  //
  // Mount-once, destroy-never: the content is behind @if (comparisonWizardMounted), set true on the
  // first open and never reset. Deliberately the opposite of the suite health dialog, which
  // re-creates its content on every opening to force a reload — here reopening must preserve the
  // picker's two TableState instances, the wizard step, the filters, the entry selection and the
  // rendered charts, and nothing of it is built for an operator who never opens it.
  // ---------------------------------------------------------------------------------------------

  comparisonWizardMounted = false;

  openComparisonWizard(): void {
    this.comparisonWizardMounted = true;
    // The dialog's @if content has to exist before showModal(), or an empty dialog opens.
    this.viewSync.notify();
    this.comparisonWizardDialog?.nativeElement.showModal();
    // showModal() would otherwise focus the close button, which announces "Close" as the first
    // thing a screen-reader user hears in a dialog full of tables.
    this.comparisonWizard?.focusHeading();
    // The anchor-positioning polyfill does not observe DOM mutations, and the wizard is full of
    // interestfor tooltips that were behind the @if until this call.
    refreshAnchorPositioning();
  }

  closeComparisonWizard(): void {
    // close() fires the dialog's (close) event, so the state is handled in one place.
    this.comparisonWizardDialog?.nativeElement.close();
  }

  /**
   * Escape and platform back gestures, which reach the dialog as (cancel) before (close).
   *
   * Refused while an export is running: it re-renders charts and writes files in sequence, and
   * tearing the DOM out from under it would leave a detached chart and a half-written batch. The
   * wizard's export status line says so, and its own close controls are disabled for the same
   * duration, so this is not a silent refusal. Refused as well while the wizard draws and uploads
   * the charts of report documents, which it composes the same way.
   */
  onComparisonWizardCancel(event: Event): void {
    if (this.comparisonWizard?.exporting || this.comparisonWizard?.chartsPublishing) {
      event.preventDefault();
    }
  }

  /**
   * Nothing is torn down here: the mounted content is what reopening is supposed to preserve. The
   * Comparison reports card counts again, since the wizard's Reports step may have written documents.
   */
  onComparisonWizardClose(): void {
    this.comparison.comparisonReportsReloadToken++;
    this.viewSync.notify();
  }

  /**
   * Roving-tabindex keyboard support required by role="tablist": Left/Right
   * move between tabs and wrap around, Home/End jump to the ends. Enter and
   * Space need no handling because each tab is a real <button>.
   *
   * Focus follows selection in the same turn, so the tab that just became
   * tabindex="0" is the one holding focus.
   */
  onTabKeydown(event: KeyboardEvent, index: number): void {
    const targets: Record<string, number> = {
      ArrowRight: index + 1,
      ArrowLeft: index - 1,
      Home: 0,
      End: this.subTabs.length - 1
    };
    const requested = targets[event.key];
    if (requested === undefined) {
      return;
    }

    event.preventDefault();
    const next = (requested + this.subTabs.length) % this.subTabs.length;
    const tab = this.subTabs[next];
    this.selectSubTab(tab);
    document.getElementById(`bm-tab-${tab}`)?.focus();
  }

  ngOnChanges(changes: SimpleChanges) {
    // The input's setter has already handed the configurations to the workspace store.
    if (changes['systemConfigs']) {
      this.launcher.setDefaultModelSelections();
    }
  }

  ngOnDestroy() {
    // The state services stop their own pollers and timers as this component's injector is destroyed.
    this.stopDetailPolling();
    if (this.copiedDiagnosticsTimer) { clearTimeout(this.copiedDiagnosticsTimer); }
    if (this.copiedRunDiagnosticsTimer) { clearTimeout(this.copiedRunDiagnosticsTimer); }
    if (this.runReportCopyTimer) { clearTimeout(this.runReportCopyTimer); }
  }
  get retryOriginalAssessorAvailable(): boolean {
    return this.selectedRunDetail?.assessorAvailable === true;
  }

  get retryAssessorDiffersFromRun(): boolean {
    return this.retryAssessorConfigId !== this.selectedRunDetail?.assessorModelConfigurationId;
  }

  selectDifficultyAssessorModel(config: SystemAiConfigDto | null) {
    if (!config) return;
    this.difficultyAssessorConfigId = config.id;
  }

  selectRetryAssessorModel(config: SystemAiConfigDto | null) {
    if (!config) return;
    this.retryAssessorConfigId = config.id;
  }

  /**
   * H5. What the model under test alone cost, beside the catalog total. The two are a pair: on run
   * 13 the candidate was 28 % of the spend, so the total on its own invites the reading that a
   * benchmark's cost is the model it grades.
   */
  formatRunCandidateCost(run: BenchmarkRunSummaryDto | BenchmarkRunDetailDto): string {
    return formatCostAmount(run.estimatedCandidateCost);
  }

  /**
   * The candidate's share of the estimated total, apportioned across the same role amounts and in
   * the same order the cost panel receives them (candidate, assessor, co-assessor, second opinion,
   * claim verifier, synthesis, co-assessor synthesis), by the same largest-remainder rule — so the
   * card and the panel below it always print the same whole percent. With incomplete pricing there
   * is no total, so the share is of the priced roles. `'share unknown'` when the candidate figure
   * itself is missing.
   */
  candidateCostShareLabel(run: BenchmarkRunDetailDto): string {
    const candidate = run.estimatedCandidateCost;
    if (candidate == null || !Number.isFinite(candidate)) {
      return 'share unknown';
    }
    const roleAmounts: { key: string; amount: number | null | undefined }[] = [
      { key: 'candidate', amount: run.estimatedCandidateCost },
      { key: 'assessor', amount: run.estimatedAssessorCost },
      { key: 'coAssessor', amount: run.estimatedCoAssessorCost },
      { key: 'secondOpinion', amount: run.estimatedSecondOpinionCost },
      { key: 'claimVerifier', amount: run.estimatedVerifierCost },
      { key: 'synthesis', amount: run.estimatedSynthesisCost },
      { key: 'coSynthesis', amount: run.estimatedCoSynthesisCost }
    ];
    const present = roleAmounts.filter((role): role is { key: string; amount: number } =>
      role.amount != null && Number.isFinite(role.amount)
    );
    const shares = apportionWholePercentShares(present.map(role => role.amount));
    const candidateIndex = present.findIndex(role => role.key === 'candidate');
    if (candidateIndex === -1) {
      return 'share unknown';
    }
    return run.pricingIncomplete
      ? `${shares[candidateIndex]} % of the priced roles`
      : `${shares[candidateIndex]} % of estimated total`;
  }

  /**
   * The candidate's spend per question asked: every answer row, whatever its status, as the report's
   * At a Glance cost divides. Null without a candidate cost or without answer rows.
   */
  candidateCostPerQuestionLabel(run: BenchmarkRunDetailDto): string | null {
    const candidate = run.estimatedCandidateCost;
    const asked = run.answers?.length ?? 0;
    if (candidate == null || !Number.isFinite(candidate) || asked === 0) {
      return null;
    }
    return `${formatCostAmount(candidate / asked)} per question · ${asked} asked`;
  }

  /** The Total Cost card's note: what the total covers and which prices it uses, or why there is none. */
  totalCostNote(run: BenchmarkRunDetailDto): string {
    if (run.pricingIncomplete) {
      return 'no single total — a role has no price';
    }
    switch (run.pricingSource) {
      case 'catalog': return 'estimated · all roles · catalog prices';
      case 'custom': return 'estimated · all roles · custom prices';
      case 'mixed': return 'estimated · all roles · catalog and custom prices';
      case null:
      case undefined:
      case '':
        return 'pricing unknown';
      default: return `estimated · all roles · ${run.pricingSource} prices`;
    }
  }

  /**
   * Whether a run predates the harness version that costed the second opinion and the final
   * synthesis as roles of their own. Such a run's assessor line still carries the second
   * opinion's spend and its synthesis is uncosted, so the cost panel says so rather than
   * presenting the lines as a complete split.
   *
   * An unparseable version is not evidence of age and is therefore not treated as legacy.
   */
  isLegacyCostRun(run: BenchmarkRunDetailDto | null | undefined): boolean {
    const version = Number.parseInt(run?.harnessVersion ?? '', 10);
    return Number.isInteger(version) && version < AdminBenchmarkComponent.PER_ROLE_COST_HARNESS_VERSION;
  }

  /**
   * Board delivery per grading role, in the report's own wording. Null for a run with no board or
   * one before harness 30, which recorded none of it.
   */
  boardDeliveryLine(run: BenchmarkRunDetailDto | null | undefined): string | null {
    const figures = run?.boardDelivery ?? [];
    if (figures.length === 0) {
      return null;
    }
    const of = (...roles: string[]) => {
      const f = figures.find(x => roles.includes(x.role));
      return f ? `${f.delivered} of ${f.total}` : 'n/a';
    };
    const reader = run?.isPanelRun ? 'reference reader' : 'second reader';
    const coAssessor = run?.isPanelRun ? `co-assessor ${of('co-assessor')}, ` : '';
    return `Board delivered — assessor ${of('assessor')} graded, ${coAssessor}${reader} ${of('second reader', 'reference reader')}, `
      + `claim verifier ${of('claim verifier')}; synthesis: yes; difficulty assessment: digest (no map).`;
  }

  /** Roles whose recorded verdicts include one graded without the board, with the questions. */
  boardDeliveryGaps(run: BenchmarkRunDetailDto | null | undefined): string[] {
    return (run?.boardDelivery ?? [])
      .filter(f => f.missingQuestions.length > 0)
      .map(f => `${f.role}: ${f.missingQuestions.map(q => `Q${q}`).join(', ')}`);
  }

  /** The per-answer board figures as `assessor/second/verifier`, or null when none was recorded. */
  answerBoardChars(ans: BenchmarkRunAnswerDto): string | null {
    if (ans.assessorBoardChars == null && ans.secondOpinionBoardChars == null && ans.verifierBoardChars == null) {
      return null;
    }
    const fmt = (v: number | null | undefined) => (v == null ? '-' : `${v}`);
    return `${fmt(ans.assessorBoardChars)}/${fmt(ans.secondOpinionBoardChars)}/${fmt(ans.verifierBoardChars)}`;
  }

  /**
   * A panel run's per-answer board figures as `A/B/reader/verifier`: member A, member B, the
   * reference reader and the claim verifier. Null when none was recorded.
   */
  panelAnswerBoardChars(ans: BenchmarkRunAnswerDto): string | null {
    const figures = [ans.assessorBoardChars, ans.coAssessorBoardChars, ans.secondOpinionBoardChars, ans.verifierBoardChars];
    if (figures.every(v => v == null)) {
      return null;
    }
    return figures.map(v => (v == null ? '-' : `${v}`)).join('/');
  }

  /** The panel's agreement figures for the diagnostics capture, `n/a` where one is not recorded. */
  private panelDiagnosticsLine(run: BenchmarkRunDetailDto): string {
    const signed = run.panelMeanSignedDelta;
    const signedStr = signed == null
      ? 'n/a'
      : `${signed > 0 ? '+' : signed < 0 ? '−' : ''}${Math.abs(signed).toFixed(1)}`;
    return `panel: A-alone ${run.assessorOnlyQualityIndex ?? 'n/a'}, B-alone ${run.coAssessorOnlyQualityIndex ?? 'n/a'}, `
      + `mean |B−A| ${run.panelMeanAbsDelta != null ? run.panelMeanAbsDelta.toFixed(1) : 'n/a'}, mean B−A ${signedStr}, `
      + `ICC ${run.panelIntraclassCorrelation != null ? run.panelIntraclassCorrelation.toFixed(2) : 'n/a'}, `
      + `disagreements ${run.panelDisagreementCount ?? 0}, critical-error splits ${run.panelCriticalErrorSplitCount ?? 0}`;
  }

  /** Member B's advisory flags, counted per answer from `coAssessmentJson.flags`. */
  private memberBFlagsLine(run: BenchmarkRunDetailDto): string {
    type Flags = NonNullable<BenchmarkCoAssessmentRecord['flags']>;
    const count = (flag: keyof Flags) =>
      run.answers.filter(a => this.coAssessmentOf(a)?.flags?.[flag] === true).length;
    return `member B flags: contested verdicts: ${count('contestedVerdict')}, unevidenced deductions: ${count('unevidencedDeduction')}, `
      + `omission as accuracy: ${count('omissionAsAccuracy')}, out-of-rubric accuracy: ${count('outOfRubricAccuracy')}, `
      + `dimension outliers: ${count('dimensionOutlier')}, completeness out of scope: ${count('completenessOutOfScope')}, `
      + `readability form only: ${count('readabilityFormOnly')}, contested critical errors: ${count('contestedCriticalError')}, `
      + `contested accuracy deductions: ${count('contestedAccuracyDeduction')}`;
  }

  /**
   * A panel run's unverified claims: the union of both members' ordinary claims as the verifier
   * received them (`claimVerificationJson`), split by who raised each one. Without any verified
   * answer there is no union, so each member's own recorded count is printed instead.
   */
  private panelUnverifiedClaimsLabel(run: BenchmarkRunDetailDto): string {
    let union = 0;
    let onlyA = 0;
    let onlyB = 0;
    let both = 0;
    let unattributed = 0;
    let anyVerified = false;
    for (const answer of run.answers) {
      if (!answer.claimVerificationJson) continue;
      anyVerified = true;
      for (const v of this.claimVerificationsOf(answer)) {
        if (!v || !AdminBenchmarkComponent.isOrdinaryClaim(v)) continue;
        union++;
        switch (this.claimMemberPhrase(v.raisedBy)) {
          case 'member A': onlyA++; break;
          case 'member B': onlyB++; break;
          case 'both members': both++; break;
          default: unattributed++; break;
        }
      }
    }
    if (!anyVerified) {
      const recordedA = run.answers.reduce((sum, a) => sum + (a.unverifiedClaimCount ?? 0), 0);
      const recordedB = run.answers.reduce((sum, a) => sum + (this.coAssessmentOf(a)?.unverifiedClaims?.length ?? 0), 0);
      return `not verified (member A ${recordedA}, member B ${recordedB} recorded)`;
    }
    const rest = unattributed > 0 ? `, unattributed ${unattributed}` : '';
    return `${union} (member A ${onlyA}, member B ${onlyB}, both ${both}${rest})`;
  }

  /** The `withdrawn` list of an evidence-informed re-grade; empty when absent or malformed. */
  evidenceInformedWithdrawn(ans: BenchmarkRunAnswerDto): string[] {
    if (!ans.evidenceInformedJson) {
      return [];
    }
    try {
      const parsed = JSON.parse(ans.evidenceInformedJson) as { withdrawn?: unknown };
      return Array.isArray(parsed.withdrawn)
        ? parsed.withdrawn.filter((w): w is string => typeof w === 'string' && w.trim().length > 0)
        : [];
    } catch {
      return [];
    }
  }

  formatSecondOpinionMode(mode: number | null | undefined): string {
    const resolvedMode = mode ?? this.launcher.secondOpinionMode;
    const option = this.secondOpinionModeOptions.find(o => o.value === resolvedMode);
    return option && option.value !== BenchmarkSecondOpinionMode.Off ? option.label : '';
  }

  secondOpinionModeHintOf(mode: number | null | undefined): string {
    const resolvedMode = mode ?? this.launcher.secondOpinionMode;
    const option = this.secondOpinionModeOptions.find(o => o.value === resolvedMode);
    return option?.hint ?? '';
  }

  /** The run manifest's coverage badge. A panel run's reference reader always reads every answer, blind. */
  runSecondOpinionModeLabel(run: BenchmarkRunDetailDto): string {
    return run.isPanelRun
      ? BENCHMARK_REFERENCE_READER_COVERAGE.label
      : this.formatSecondOpinionMode(run.secondOpinionModeUsed);
  }

  runSecondOpinionModeHint(run: BenchmarkRunDetailDto): string {
    return run.isPanelRun
      ? BENCHMARK_REFERENCE_READER_COVERAGE.hint
      : this.secondOpinionModeHintOf(run.secondOpinionModeUsed);
  }

  /** Opens the grader guide with the given profile's values; an undefined profile prints the launcher's selected profile. */
  openGraderGuide(section: GraderGuideSection, profile?: GraderGuideProfile | null): void {
    this.graderGuideProfile = profile !== undefined ? profile : (this.launcher.selectedScoringProfile ?? null);
    this.graderGuide?.open(section);
  }

  openConfirmDialog(options: {
    title: string;
    message: string;
    dangerNotice?: string;
    buttonText?: string;
    buttonClass?: string;
    icon?: 'delete' | 'none';
    action: () => void;
  }) {
    this.confirmDialogTitle = options.title;
    this.confirmDialogMessage = options.message;
    this.confirmDialogDangerNotice = options.dangerNotice || '';
    this.confirmDialogButtonText = options.buttonText || 'Delete';
    this.confirmDialogButtonClass = options.buttonClass || 'btn-gh btn-gh-delete';
    this.confirmDialogIcon = options.icon || 'delete';
    this.pendingConfirmAction = options.action;
    this.confirmActionDialog?.nativeElement.showModal();
  }

  closeConfirmDialog() {
    this.confirmActionDialog?.nativeElement.close();
    this.pendingConfirmAction = null;
  }

  executeConfirmAction() {
    const action = this.pendingConfirmAction;
    this.closeConfirmDialog();
    if (action) {
      action();
    }
  }

  openDifficultyAssessorDialog(suite?: BenchmarkSuiteDto | null, question: BenchmarkQuestionDto | null = null) {
    this.workspace.actionErrorMessage = null;
    this.difficultyDialogError = null;
    this.difficulty.isDifficultyAssessorDialogOpen = true;

    if (this.difficulty.difficultyJobIsRunning) {
      this.difficultyDialogPhase = 'progress';
    } else {
      if (suite) {
        this.suiteForDifficultyAssessment = suite;
      }
      this.difficultyAssessmentScope = question != null
        ? 'question'
        : (this.suiteIsPartiallyAssessed ? 'unassessed' : 'suite');
      this.questionIdForDifficultyAssessment = question?.id ?? null;
      this.difficultyAssessorConfigId = this.resolveDefaultDifficultyAssessor(question);
      this.difficultyDialogPhase = 'select';
    }

    this.difficultyAssessorDialog?.nativeElement.showModal();
  }

  closeDifficultyAssessorDialog() {
    this.difficulty.isDifficultyAssessorDialogOpen = false;
    this.difficultyAssessorDialog?.nativeElement.close();
    if (this.difficulty.difficultyJobIsTerminal) {
      this.difficultyDialogPhase = 'select';
    }
  }

  resolveDefaultDifficultyAssessor(question: BenchmarkQuestionDto | null): number | null {
    if (question?.assessedDifficultyModelConfigurationId && this.workspace.benchmarkCapableConfigs.some(c => c.id === question.assessedDifficultyModelConfigurationId)) {
      return question.assessedDifficultyModelConfigurationId;
    }

    if (this.workspace.currentSuiteForQuestions?.id === this.suiteForDifficultyAssessment?.id && this.workspace.questions.length > 0) {
      const assessed = this.workspace.questions
        .filter(q => q.assessedDifficultyModelConfigurationId != null && q.assessedDifficultyAtUtc != null && this.workspace.benchmarkCapableConfigs.some(c => c.id === q.assessedDifficultyModelConfigurationId))
        .sort((a, b) => new Date(b.assessedDifficultyAtUtc!).getTime() - new Date(a.assessedDifficultyAtUtc!).getTime());
      if (assessed.length > 0 && assessed[0].assessedDifficultyModelConfigurationId != null) {
        return assessed[0].assessedDifficultyModelConfigurationId;
      }
    }

    if (this.launcher.assessorConfigId && this.workspace.benchmarkCapableConfigs.some(c => c.id === this.launcher.assessorConfigId)) {
      return this.launcher.assessorConfigId;
    }

    return this.workspace.benchmarkCapableConfigs[0]?.id ?? null;
  }

  confirmDifficultyAssessment() {
    if (!this.difficultyAssessorConfigId) return;
    this.difficultyJobStarting = true;
    this.difficultyDialogError = null;

    const suiteId = this.suiteForDifficultyAssessment?.id || (this.difficulty.difficultyJob?.suiteId ?? 0);
    const questionIds = this.difficultyAssessmentScope === 'question' && this.questionIdForDifficultyAssessment != null
      ? [this.questionIdForDifficultyAssessment]
      : null;

    this.benchmarkService.startDifficultyAssessment({
      suiteId,
      questionIds,
      onlyUnassessed: this.difficultyAssessmentScope === 'unassessed',
      assessorModelConfigurationId: this.difficultyAssessorConfigId
    }).subscribe({
      next: (res) => {
        this.difficultyJobStarting = false;
        this.difficultyDialogPhase = 'progress';
        this.difficulty.startDifficultyPolling(res.jobId);
        this.viewSync.notify();
        this.difficultyProgressHeading?.nativeElement.focus();
      },
      error: (err) => {
        this.difficultyJobStarting = false;
        if (err.status === 409 && err.error) {
          this.difficulty.difficultyJob = err.error as DifficultyAssessmentJobDto;
          this.difficultyDialogPhase = 'progress';
          this.difficulty.startDifficultyPolling(this.difficulty.difficultyJob.id);
          this.viewSync.notify();
          this.difficultyProgressHeading?.nativeElement.focus();
        } else {
          this.difficultyDialogError = err?.error || 'Failed to start difficulty assessment.';
          this.viewSync.notify();
        }
      }
    });
  }

  assessAgain() {
    // The suite list is reloaded when a job ends, so take the current counts from it.
    const suiteId = this.suiteForDifficultyAssessment?.id ?? this.difficulty.difficultyJob?.suiteId;
    const currentSuite = this.workspace.suites.find(s => s.id === suiteId);
    if (currentSuite) {
      this.suiteForDifficultyAssessment = currentSuite;
    }
    if (this.difficultyAssessmentScope !== 'question') {
      this.difficultyAssessmentScope = this.suiteIsPartiallyAssessed ? 'unassessed' : 'suite';
    }
    this.difficultyDialogPhase = 'select';
    this.viewSync.notify();
  }

  retryFailedQuestions() {
    if (!this.difficulty.difficultyJob || this.difficulty.failedDifficultyItems.length === 0) return;
    this.difficultyJobStarting = true;
    this.difficultyDialogError = null;

    const failedIds = this.difficulty.failedDifficultyItems.map(i => i.questionId);
    this.benchmarkService.startDifficultyAssessment({
      suiteId: this.difficulty.difficultyJob.suiteId,
      questionIds: failedIds,
      assessorModelConfigurationId: this.difficulty.difficultyJob.assessorConfigId
    }).subscribe({
      next: (res) => {
        this.difficultyJobStarting = false;
        this.difficultyDialogPhase = 'progress';
        this.difficulty.startDifficultyPolling(res.jobId);
        this.viewSync.notify();
        this.difficultyProgressHeading?.nativeElement.focus();
      },
      error: (err) => {
        this.difficultyJobStarting = false;
        if (err.status === 409 && err.error) {
          this.difficulty.difficultyJob = err.error as DifficultyAssessmentJobDto;
          this.difficultyDialogPhase = 'progress';
          this.difficulty.startDifficultyPolling(this.difficulty.difficultyJob.id);
          this.viewSync.notify();
          this.difficultyProgressHeading?.nativeElement.focus();
        } else {
          this.difficultyDialogError = err?.error || 'Failed to retry failed questions.';
          this.viewSync.notify();
        }
      }
    });
  }

  /**
   * The dialog's own Continue resumed a stopped series without going through
   * `resumeActiveSeries`, so this page's series poll — stopped when the series went Stopped —
   * never restarted on its own. Restarting it here is what lets the series chime again and keeps
   * the banner and `activeSeries` current.
   */
  onSeriesResumedFromDialog(seriesId: number): void {
    this.monitor.activeSeriesId = seriesId;
    this.monitor.seriesSeenLive.add(seriesId);
    this.monitor.startSeriesPolling(seriesId);
    this.viewSync.notify();
  }

  /**
   * The hand-off the multi-run dialog makes rather than embedding a second per-question view. Two
   * stacked native dialogs trap focus in the inner one, so this closes the multi-run dialog as it
   * opens the single-run one — never both at once.
   */
  onOpenRunProgressFromSeries(runId: number): void {
    this.monitor.multiRunDialogVisible = false;
    this.monitor.activeRunId = runId;
    this.monitor.startPolling(runId);
    this.openRunProgressDialog(true);
  }

  /**
   * The hand-off `viewGroupReport` makes from the multi-run progress dialog: switch to the
   * Multi-Run Analysis tab and open that group there, rather than stacking a second dialog on
   * top of this page.
   */
  onOpenGroupAnalysisFromSeries(groupId: number): void {
    this.monitor.multiRunDialogVisible = false;
    this.monitor.seriesDialogId = null;
    this.selectSubTab('multirun');
    // The panel lives inside @if (activeSubTab === 'multirun'); the ViewChild does not resolve
    // until that block has rendered, so the tab switch is flushed before the panel is addressed.
    this.viewSync.notify();
    this.multiRunPanel?.openGroupById(groupId);
  }

  /** The dialog continued or re-ran a battery run; this page's poller follows it again. */
  onBatteryResumedFromDialog(batteryRunId: number): void {
    this.monitor.activeBatteryRunId = batteryRunId;
    this.monitor.batteryDialogRunId = null;
    this.monitor.batteriesSeenLive.add(batteryRunId);
    this.monitor.startBatteryPolling(batteryRunId);
    this.viewSync.notify();
  }

  /** The dialog canceled a battery run; the banner learns it from the next poll. */
  onBatteryCanceledFromDialog(batteryRunId: number): void {
    if (batteryRunId === this.monitor.activeBatteryRunId) {
      this.monitor.pollBatteryRun(batteryRunId);
    }
  }

  /** The dialog attached a run, which may have finished the battery run; the banner reads it again. */
  onBatteryMemberAttachedFromDialog(batteryRunId: number): void {
    if (batteryRunId === this.monitor.activeBatteryRunId) {
      this.monitor.pollBatteryRun(batteryRunId);
    }
  }

  /** Hands a member over to the run progress dialog, closing the battery dialog: never two stacked. */
  onOpenRunProgressFromBattery(runId: number): void {
    this.monitor.batteryDialogVisible = false;
    this.monitor.activeRunId = runId;
    this.monitor.startPolling(runId);
    this.openRunProgressDialog();
    this.monitor.returnToBatteryOnClose = true;
    this.viewSync.notify();
  }

  /** The battery progress dialog's Open Analysis: the Battery Run Report replaces the dialog. */
  onOpenBatteryAnalysis(batteryRunId: number): void {
    this.monitor.batteryDialogVisible = false;
    this.monitor.batteryDialogRunId = null;
    this.openBatteryRunReport(batteryRunId);
  }

  /** Opens the Battery Run Report over whatever is showing. */
  openBatteryRunReport(batteryRunId: number): void {
    this.batteryRunReport?.open(batteryRunId);
  }

  /** The Multi-Suite tab changed a battery; the launcher's list follows. */
  onBatteriesChanged(): void {
    this.workspace.loadBatteries();
  }

  // --- Run Progress Dialog ---

  /**
   * Which pass of the run is executing, named as the server names it. `BenchmarkService` answers
   * a question, assesses it, then checks its claims and takes a second opinion on it, all inside
   * the answering loop — so 'answering' covers far more than producing text. What follows every
   * answer being graded is a serial tail of run-level passes: remaining claim checks, then the
   * outlier sweep or the sample top-up, then remaining claim checks again, then the synthesis.
   *
   * The rail built on this shows three scoring stages, not five, because 'verifying' and 'secondopinion'
   * are both part of that one tail and the server marks `Verifying` a second time after the
   * second-opinion pass — a rail with an item each would step backwards. The pass name survives
   * in `runStageLabel` and in the diagnostics, where a backwards step is information, not a bug.
   *
   * The server's own `stage` is preferred because that is the only thing that can tell the tail's
   * passes apart — nothing in the answer rows moves while they run. The derivation below is the
   * fallback for a run this process is not executing, or a run detail fetched from a server
   * predating the field, and it can only ever reach 'finalizing' as a catch-all for the whole
   * post-answering span.
   */
  get runStage(): BenchmarkRunStage {
    const run = this.monitor.activeRunDetail;
    if (!run) return 'answering';
    if (this.monitor.rerunLaunchPending) return 'answering';
    return this.runStageOf(run);
  }

  /** The stage of any run from its own detail, with no re-run launch pending. */
  runStageOf(run: BenchmarkRunDetailDto): BenchmarkRunStage {
    if (formatStatus(run.status) !== 'Running') return 'terminal';

    switch (run.stage) {
      case 'Answering': return 'answering';
      case 'Verifying': return 'verifying';
      case 'SecondOpinion': return 'secondopinion';
      case 'Synthesizing': return 'finalizing';
      case 'Terminal': return 'terminal';
    }

    if (run.answers.length < run.totalQuestionCount) return 'answering';
    if (run.answers.some(a => this.isAssessmentIncomplete(a))) return 'answering';
    return 'finalizing';
  }

  /**
   * The rail item the run is on: 4 while a terminal run's reports are being written, otherwise 0 for
   * a terminal run, which highlights nothing. Both follow-up passes map to item 2 so the rail cannot
   * move backwards when the server revisits `Verifying`.
   */
  get runRailStage(): 0 | 1 | 2 | 3 | 4 {
    switch (this.runStage) {
      case 'answering': return 1;
      case 'verifying':
      case 'secondopinion': return 2;
      case 'finalizing': return 3;
      default: return this.runReportStage === 'current' ? 4 : 0;
    }
  }

  /** The active run names a report writer, so the rail and the stage labels have a fourth stage. */
  get runHasReportStage(): boolean {
    return this.monitor.activeRunDetail?.reportWriterModelConfigurationId != null;
  }

  /** The stage labels' denominator: 4 with a report writer, else 3. */
  get runStageCount(): 3 | 4 {
    return this.runHasReportStage ? 4 : 3;
  }

  /**
   * Where stage 4, the writing of the run's AI-written reports, stands. `none` without a writer;
   * `pending` until the run is terminal; `current` while the poll follows it; `done` once written;
   * `ended` when it failed, was skipped or was canceled; `notWritten` when no writing followed the
   * run (another terminal status, or nothing queued within the start grace).
   */
  get runReportStage(): 'none' | 'pending' | 'current' | 'done' | 'ended' | 'notWritten' {
    const run = this.monitor.activeRunDetail;
    if (!run || run.reportWriterModelConfigurationId == null) return 'none';
    if (!this.runIsTerminal) return 'pending';
    if (this.monitor.runAwaitsReports(run)) return 'current';
    switch (reportDocumentsStatusOf(run)) {
      case BenchmarkRunReportDocumentsStatus.Completed:
      case BenchmarkRunReportDocumentsStatus.CompletedWithWarnings:
        return 'done';
      case BenchmarkRunReportDocumentsStatus.Failed:
      case BenchmarkRunReportDocumentsStatus.Skipped:
      case BenchmarkRunReportDocumentsStatus.Canceled:
        return 'ended';
      default:
        return 'notWritten';
    }
  }

  /** Stages 1 to 3 are behind the run: stage 4 is current, or it has ended one way or another. */
  get runRailReportsReached(): boolean {
    const stage = this.runReportStage;
    return stage === 'current' || stage === 'done' || stage === 'ended';
  }

  /** What stage 4 is doing, from the job view polled alongside the run. */
  private get runReportWritingDetail(): string {
    const job = this.monitor.activeRunReportJob;
    const status = reportDocumentsStatusOf(this.monitor.activeRunDetail);
    if (!job || job.runId !== this.monitor.activeRunDetail?.id) {
      return status === BenchmarkRunReportDocumentsStatus.Writing ? 'writing the reports' : 'waiting for the report writer';
    }
    switch (job.phase) {
      case 'Queued': {
        const ahead = job.jobsAhead;
        return ahead != null && ahead > 0
          ? `waiting for the report writer (${ahead} ${ahead === 1 ? 'job' : 'jobs'} ahead)`
          : 'waiting for the report writer';
      }
      case 'Preparing':
        return 'preparing the fact sheet';
      case 'Writing': {
        const documents = job.job?.documents ?? [];
        const audiences = job.audiences?.length ? job.audiences : documents.map(doc => doc.audience);
        const active = documents.find(doc => doc.status === 'Writing' || doc.status === 'Repairing');
        if (!active) return 'writing the reports';
        const position = audiences.indexOf(active.audience) + 1;
        const verb = active.status === 'Repairing' ? 'repairing' : 'writing';
        const count = position > 0 ? ` (${position} of ${audiences.length})` : '';
        return `${verb} the ${audienceLabel(active.audience)}${count}`;
      }
      default:
        return 'finishing';
    }
  }

  /** The documents stored, and how long they took: `2 documents, 1m 12s`. */
  private get runReportsWrittenSummary(): string {
    const run = this.monitor.activeRunDetail;
    const count = run?.reportDocumentsWrittenCount ?? 0;
    const documents = `${count} ${count === 1 ? 'document' : 'documents'}`;
    const durationMs = run?.reportDocumentsDurationMs;
    return durationMs != null ? `${documents}, ${formatDuration(durationMs)}` : documents;
  }

  /** The status line's report sentence once stage 4 is over, or null while it is not. */
  private get runReportOutcomeSentence(): string | null {
    const run = this.monitor.activeRunDetail;
    const message = run?.reportDocumentsMessage?.trim();
    const sentence = (text: string): string => /[.!?]$/.test(text) ? text : `${text}.`;
    switch (this.runReportStage) {
      case 'done':
        return reportDocumentsStatusOf(run) === BenchmarkRunReportDocumentsStatus.CompletedWithWarnings
          ? `Reports written with warnings: ${this.runReportsWrittenSummary}.`
          : `Reports written: ${this.runReportsWrittenSummary}.`;
      case 'ended':
        switch (reportDocumentsStatusOf(run)) {
          case BenchmarkRunReportDocumentsStatus.Failed:
            return sentence(`Report writing failed: ${message || 'no reason was recorded'}`);
          case BenchmarkRunReportDocumentsStatus.Skipped:
            return sentence(`Report writing skipped: ${message || 'no reason was recorded'}`);
          default:
            return sentence(message ? `Report writing canceled: ${message}` : 'Report writing canceled');
        }
      default:
        return null;
    }
  }

  /** The server's clock now: the job view's reading plus the client time since it arrived. */
  private runReportServerNowMs(): number | null {
    const job = this.monitor.activeRunReportJob;
    if (!job?.serverTimeUtc) return null;
    const server = parseServerUtcDate(job.serverTimeUtc).getTime();
    if (Number.isNaN(server)) return null;
    return server + Math.max(0, Date.now() - this.monitor.activeRunReportJobReceivedAtMs);
  }

  /**
   * The stat strip's Reports cell: live elapsed since the writer took the slot while stage 4 is
   * current, then the stored documents' duration and count.
   */
  get runReportsStatLabel(): string {
    switch (this.runReportStage) {
      case 'current': {
        const job = this.monitor.activeRunReportJob;
        const now = this.runReportServerNowMs();
        if (job?.runId === this.monitor.activeRunDetail?.id && job?.slotAcquiredAtUtc && now !== null) {
          const started = parseServerUtcDate(job.slotAcquiredAtUtc).getTime();
          if (!Number.isNaN(started)) {
            return `${formatElapsed(Math.max(0, now - started))} · writing`;
          }
        }
        return 'Waiting';
      }
      case 'done':
      case 'ended':
        return (this.monitor.activeRunDetail?.reportDocumentsWrittenCount ?? 0) > 0 ? this.runReportsWrittenSummary : 'None written';
      case 'notWritten':
        return 'Not written';
      default:
        return '—';
    }
  }

  /** The cost panel's report writer figure: the job's running cost while writing, then the stored documents' total. */
  get runReportWriterCost(): number | null {
    switch (this.runReportStage) {
      case 'current': {
        const job = this.monitor.activeRunReportJob;
        return job?.runId === this.monitor.activeRunDetail?.id ? (job?.job?.costUsd ?? null) : null;
      }
      case 'done':
      case 'ended':
        return this.monitor.activeRunDetail?.reportDocumentsCostUsd ?? null;
      default:
        return null;
    }
  }

  /** Answers the claim verifier is reading right now. */
  get runVerifyingCount(): number {
    return (this.monitor.activeRunDetail?.inFlightVerificationOrderIndexes ?? []).length;
  }

  /** Answers the second-opinion assessor is reading right now. */
  get runSecondOpinionInFlightCount(): number {
    return (this.monitor.activeRunDetail?.inFlightSecondOpinionOrderIndexes ?? []).length;
  }

  /** Answers the claim verifier has produced a verdict or an error for, scoped to a re-run. */
  get runVerifiedCount(): number {
    const verified = (this.monitor.activeRunDetail?.answers ?? []).filter(
      a => a.claimVerificationJson != null || a.claimVerificationError != null);
    if (!this.runHasRerunScope) return verified.length;
    const scope = new Set(this.effectiveRerunScope);
    return verified.filter(a => scope.has(a.orderIndex)).length;
  }

  /** Answers carrying a second verdict, scoped to a re-run. */
  get runSecondOpinionCount(): number {
    const graded = (this.monitor.activeRunDetail?.answers ?? []).filter(
      a => a.secondOpinionQualityScore != null || a.secondOpinionError != null);
    if (!this.runHasRerunScope) return graded.length;
    const scope = new Set(this.effectiveRerunScope);
    return graded.filter(a => scope.has(a.orderIndex)).length;
  }

  /**
   * Whether the stat strip carries a "Claims verified" cell. The condition is the model strip's,
   * so a run that names a claim verifier counts its work, and a run without one shows no cell
   * rather than a zero that reads as a failure.
   */
  get runShowsClaimVerifierCounter(): boolean {
    const run = this.monitor.activeRunDetail;
    return !!(run?.claimVerifierDisplayNameUsed || run?.claimVerifierModelIdUsed);
  }

  /** Whether the stat strip carries a "Second opinions" cell; the model strip's condition. */
  get runShowsSecondOpinionCounter(): boolean {
    const run = this.monitor.activeRunDetail;
    return !!(run?.secondOpinionAssessorModelDisplayNameUsed || run?.secondOpinionAssessorModelIdUsed)
      && run?.secondOpinionModeUsed !== 0;
  }

  get runStageLabel(): string {
    const run = this.monitor.activeRunDetail;
    if (!run) return '';
    if (this.monitor.rerunLaunchPending) return 'Starting failed-question re-run…';
    const total = this.runTotalQuestionCount;
    const scoped = this.runHasRerunScope;
    const stageCount = this.runStageCount;
    switch (this.runStage) {
      case 'answering':
        return scoped
          ? `Re-run stage 1 of ${stageCount} — Answering and grading. Answered ${this.runMeterAnswered} of ${this.runMeterTotal} re-run questions, scored ${this.runMeterScored} of ${this.runMeterTotal}.`
          : `Stage 1 of ${stageCount} — Answering and grading. Answered ${this.runAnsweredCount} of ${total}, scored ${this.runScoredCount} of ${total}.`;
      case 'verifying':
        // The label names the pass the rail's stage 2 cannot, and carries a count so it moves
        // during the minutes the answer rows are static. runVerifiedCount is itself scoped to
        // a re-run, so this needs no wording change under one.
        return `Stage 2 of ${stageCount} — Follow-up grading passes: verifying remaining claims. ${this.runVerifiedCount} claims verified so far.`;
      case 'secondopinion':
        return run.isPanelRun
          ? `Stage 2 of ${stageCount} — Follow-up grading passes: reference-reader sweep. ${this.runSecondOpinionCount} reference readings so far.`
          : `Stage 2 of ${stageCount} — Follow-up grading passes: second-reader sweep. ${this.runSecondOpinionCount} second readings so far.`;
      case 'finalizing':
        return scoped
          ? `Stage 3 of ${stageCount} — Synthesis and scoring. All ${this.runMeterTotal} re-run answers assessed.`
          : `Stage 3 of ${stageCount} — Synthesis and scoring. All ${total} answers assessed.`;
      default: {
        if (this.runReportStage === 'current') {
          return `Stage 4 of 4 — Writing reports: ${this.runReportWritingDetail}`;
        }
        const status = formatStatus(run.status);
        const label = status === 'CompletedWithErrors'
          ? 'Completed with errors'
          : (status === 'CompletedWithLimits' ? 'Completed with limits' : status);
        let result: string;
        if (scoped) {
          const failed = this.runMeterFailed;
          result = failed > 0
            ? `${label}. Re-run answered ${this.runMeterAnswered} of ${this.runMeterTotal}, ${failed} failed.`
            : `${label}. Re-run answered ${this.runMeterAnswered} of ${this.runMeterTotal}.`;
        } else {
          const failed = this.runFailedAnswerCount;
          result = failed > 0
            ? `${label}. Answered ${this.runAnsweredCount} of ${total}, ${failed} failed.`
            : `${label}. Answered ${this.runAnsweredCount} of ${total}.`;
        }
        const reports = this.runReportOutcomeSentence;
        return reports ? `${result} ${reports}` : result;
      }
    }
  }

  get runAnsweredCount(): number {
    return this.monitor.activeRunDetail?.answers.length ?? 0;
  }

  /** Answers that reached a terminal assessment state — scored or failed to assess. */
  get runScoredCount(): number {
    return (this.monitor.activeRunDetail?.answers ?? []).filter(a => {
      const s = this.formatAssessmentStatus(a.assessmentStatus);
      return s === 'Scored' || s === 'Failed';
    }).length;
  }

  get runFailedAnswerCount(): number {
    return this.runFailedAnswers.length;
  }

  get runFailedAnswers(): BenchmarkRunAnswerDto[] {
    return (this.monitor.activeRunDetail?.answers ?? []).filter(a => this.isAnswerFailed(a));
  }

  /** The selected run detail's answers that failed, for the run-detail dialog's alert and integrity notice. */
  failedAnswers(): BenchmarkRunAnswerDto[] {
    return (this.selectedRunDetail?.answers ?? []).filter(a => this.isAnswerFailed(a));
  }

  /**
   * Failed answers grouped by their error message, each with the shared status code and the
   * questions it hit, ascending. Defaults to the selected run detail's own failures; the run
   * diagnostics capture passes the active run's failures instead, since it describes a
   * different run.
   */
  failedAnswerGroups(answers: BenchmarkRunAnswerDto[] = this.failedAnswers())
    : { message: string; httpStatusCode: number | null; questions: number[] }[] {
    const groups = new Map<string, { message: string; httpStatusCode: number | null; questions: number[] }>();
    for (const ans of answers) {
      const message = ans.errorMessage ?? '(no error message)';
      let group = groups.get(message);
      if (!group) {
        group = { message, httpStatusCode: ans.httpStatusCode ?? null, questions: [] };
        groups.set(message, group);
      }
      group.questions.push(ans.orderIndex);
    }
    for (const group of groups.values()) {
      group.questions.sort((a, b) => a - b);
    }
    return Array.from(groups.values()).sort((a, b) => a.questions[0] - b.questions[0]);
  }

  /**
   * The transport-defect count with the terminal provider failures already counted in the run
   * integrity notice's first sentence removed, so the two sentences partition the total rather
   * than double-count the answers that both crashed at the provider and arrived empty.
   */
  nonTerminalTransportDefectCount(run: BenchmarkRunDetailDto): number {
    return Math.max(0, (run.transportDefectAnswerCount ?? 0) - (run.terminalFailureAnswerCount ?? 0));
  }

  /** [1,2,3,5,7,8,9] -> "Q1–Q3, Q5, Q7–Q9". Used where the failure list needs to stay compact. */
  formatQuestionRanges(questions: number[]): string {
    const parts: string[] = [];
    let start = 0;
    for (let i = 0; i < questions.length; i++) {
      if (i + 1 < questions.length && questions[i + 1] === questions[i] + 1) {
        continue;
      }
      const rangeStart = questions[start];
      const rangeEnd = questions[i];
      parts.push(rangeStart === rangeEnd ? `Q${rangeStart}` : `Q${rangeStart}–Q${rangeEnd}`);
      start = i + 1;
    }
    return parts.join(', ');
  }

  get runTotalQuestionCount(): number {
    return this.monitor.activeRunDetail?.totalQuestionCount ?? 0;
  }

  /**
   * Progress-meter denominator: the whole suite normally, or just the re-run's scope while one
   * is in effect — a re-run overwrites answer rows in place rather than adding any, so counting
   * the meters against the whole suite read as instantly complete the moment the re-run started.
   */
  get runMeterTotal(): number {
    return this.runHasRerunScope ? this.effectiveRerunScope.length : this.runTotalQuestionCount;
  }

  /** Answered count for the progress meter: the whole run, or only the re-run's own answers. */
  get runMeterAnswered(): number {
    if (!this.runHasRerunScope) return this.runAnsweredCount;
    return this.monitor.activeRunDetail?.rerunAnsweredOrderIndexes?.length ?? 0;
  }

  /** Scored count for the progress meter: the whole run, or only the re-run's own answers. */
  get runMeterScored(): number {
    if (!this.runHasRerunScope) return this.runScoredCount;
    return this.monitor.activeRunDetail?.rerunScoredOrderIndexes?.length ?? 0;
  }

  /**
   * Failed count for the progress meter. Under a re-run scope a question only counts once the
   * re-run has actually answered it again — a scope member still waiting its turn keeps the
   * failure it already carries, and counting it here would fail it a second time before the
   * re-run even touched it.
   */
  get runMeterFailed(): number {
    if (!this.runHasRerunScope) return this.runFailedAnswerCount;
    const scope = new Set(this.effectiveRerunScope);
    const reAnswered = new Set(this.monitor.activeRunDetail?.rerunAnsweredOrderIndexes ?? []);
    return this.runFailedAnswers.filter(a => scope.has(a.orderIndex) && reAnswered.has(a.orderIndex)).length;
  }

  get runIsRunning(): boolean {
    return this.monitor.activeRunDetail != null && formatStatus(this.monitor.activeRunDetail.status) === 'Running';
  }

  get runIsTerminal(): boolean {
    return this.monitor.activeRunDetail != null && !this.monitor.rerunLaunchPending && formatStatus(this.monitor.activeRunDetail.status) !== 'Running';
  }

  /**
   * True while the run's first pass is executing: Running with no re-run scope, no re-run being
   * launched and no re-run ever started. Only then can the suite hold questions the run has yet
   * to answer; a re-run rewrites answer rows the run already has.
   */
  get runIsFirstPass(): boolean {
    return this.runIsRunning
      && !this.runHasRerunScope
      && !this.monitor.rerunLaunchPending
      && this.monitor.activeRunDetail?.rerunStartedAtUtc == null;
  }

  /**
   * The run's own answers, each under its stored order index and question text, so a suite that
   * has since lost or gained questions never changes what a run shows. While the first pass is
   * executing, the suite's questions with no answer yet are added as Pending or Answering,
   * matched to the answers by question id.
   */
  get runProgressRows(): BenchmarkRunProgressRow[] {
    const run = this.monitor.activeRunDetail;
    if (!run) return [];

    const source: { orderIndex: number; questionText: string; answer: BenchmarkRunAnswerDto | null }[] =
      run.answers.map(a => ({ orderIndex: a.orderIndex, questionText: a.questionText, answer: a }));

    if (this.runIsFirstPass) {
      const answeredQuestionIds = new Set<number>();
      const takenIndexes = new Set<number>();
      for (const a of run.answers) {
        takenIndexes.add(a.orderIndex);
        if (a.benchmarkQuestionId != null) {
          answeredQuestionIds.add(a.benchmarkQuestionId);
        }
      }
      // An index an answer already holds is never listed twice: the rows are tracked by it.
      for (const q of this.monitor.runProgressQuestions) {
        if ((q.id != null && answeredQuestionIds.has(q.id)) || takenIndexes.has(q.orderIndex)) {
          continue;
        }
        takenIndexes.add(q.orderIndex);
        source.push({ orderIndex: q.orderIndex, questionText: q.questionText, answer: null });
      }
    }

    const inFlight = new Set<number>(run.inFlightOrderIndexes ?? []);
    const verifying = new Set<number>(run.inFlightVerificationOrderIndexes ?? []);
    const secondOpinion = new Set<number>(run.inFlightSecondOpinionOrderIndexes ?? []);
    const rerunScope = new Set<number>(this.effectiveRerunScope);
    const rerunAnswered = new Set<number>(run.rerunAnsweredOrderIndexes ?? []);
    const rerunRunning = this.runIsRunning && this.runHasRerunScope;

    return source
      .sort((a, b) => a.orderIndex - b.orderIndex)
      .map(q => {
        const ans = q.answer;
        if (!ans) {
          return {
            orderIndex: q.orderIndex,
            questionText: q.questionText,
            answer: null,
            status: inFlight.has(q.orderIndex) ? 'Answering' : 'Pending',
            assessmentStatus: '',
            errorMessage: null
          };
        }

        // Ahead of the answer's own status, because a row under active re-grading already
        // carries a score and would otherwise read as finished for the whole pass.
        if (verifying.has(q.orderIndex) || secondOpinion.has(q.orderIndex)) {
          return {
            orderIndex: q.orderIndex,
            questionText: q.questionText,
            answer: ans,
            status: verifying.has(q.orderIndex) ? 'Verifying' : 'SecondOpinion',
            assessmentStatus: this.formatAssessmentStatus(ans.assessmentStatus),
            errorMessage: ans.errorMessage ?? null
          };
        }

        // An answer row is written only after the provider replies, so an in-flight row that
        // already has one is being re-executed in place.
        if (inFlight.has(q.orderIndex)) {
          return {
            orderIndex: q.orderIndex,
            questionText: q.questionText,
            answer: ans,
            status: 'Answering',
            assessmentStatus: '',
            errorMessage: null
          };
        }

        // A question inside a pending re-run's scope stops showing the failure it is about to
        // be re-run for.
        if (this.monitor.rerunLaunchPending && this.effectiveRerunScope.includes(q.orderIndex)) {
          return {
            orderIndex: q.orderIndex,
            questionText: q.questionText,
            answer: ans,
            status: 'Pending',
            assessmentStatus: '',
            errorMessage: null
          };
        }

        // While the re-run is Running, a scope member it has not answered again is still queued,
        // the same rule runMeterFailed applies.
        if (rerunRunning && rerunScope.has(q.orderIndex) && !rerunAnswered.has(q.orderIndex)) {
          return {
            orderIndex: q.orderIndex,
            questionText: q.questionText,
            answer: ans,
            status: 'Pending',
            assessmentStatus: '',
            errorMessage: null
          };
        }

        return {
          orderIndex: q.orderIndex,
          questionText: q.questionText,
          answer: ans,
          status: this.formatAnswerStatus(ans.status),
          assessmentStatus: this.formatAssessmentStatus(ans.assessmentStatus),
          errorMessage: ans.errorMessage ?? null
        };
      });
  }

  /** Server-reported scope once the re-run is Running; the client-captured list during launch. */
  get effectiveRerunScope(): number[] {
    const server = this.monitor.activeRunDetail?.rerunScopeOrderIndexes ?? [];
    return server.length > 0 ? server : this.monitor.rerunScopeOrderIndexes;
  }

  get runHasRerunScope(): boolean {
    return this.effectiveRerunScope.length > 0;
  }

  isRerunScope(row: BenchmarkRunProgressRow): boolean {
    return this.effectiveRerunScope.includes(row.orderIndex);
  }

  /**
   * The chip's word, never a hue alone. 'Answered' rather than 'Assessing' while the
   * assessment is merely queued — claiming work that has not started would be a guess.
   */
  runRowChipLabel(row: BenchmarkRunProgressRow): string {
    if (row.status === 'Pending') return 'Pending';
    if (row.status === 'Answering') return 'Answering';
    if (row.status === 'Verifying') return 'Verifying';
    if (row.status === 'SecondOpinion') return this.monitor.activeRunDetail?.isPanelRun ? 'Reference reader' : 'Second reader';
    if (row.status === 'ProviderError') return 'Provider Error';
    if (row.status === 'Canceled') return 'Canceled';
    if (row.status !== 'Ok') return row.status;
    if (row.assessmentStatus === 'Scored') return 'Scored';
    if (row.assessmentStatus === 'Failed') return 'Assessment Failed';
    if (row.assessmentStatus === 'Assessing') return 'Assessing';
    return 'Answered';
  }

  /**
   * The published score of a scored row: the panel score in a panel run, else the quality score.
   * Null while the row is unscored, queued or being answered, and in a panel run until both
   * members have scored.
   */
  runRowScore(row: BenchmarkRunProgressRow): number | null {
    const ans = row.answer;
    if (!ans || row.status === 'Pending' || row.status === 'Answering' || row.assessmentStatus !== 'Scored') {
      return null;
    }
    const score = this.monitor.activeRunDetail?.isPanelRun ? ans.panelQualityScore : ans.qualityScore;
    return score ?? null;
  }

  runRowChipClass(row: BenchmarkRunProgressRow): string {
    if (row.status === 'Pending') return 'status-pending';
    if (row.status === 'Answering') return 'status-answering';
    if (row.status === 'Verifying') return 'status-verifying';
    if (row.status === 'SecondOpinion') return 'status-secondopinion';
    if (row.status === 'ProviderError') return 'status-providererror';
    if (row.status === 'Failed') return 'status-failed';
    if (row.status === 'Skipped') return 'status-skipped';
    if (row.status === 'Canceled') return 'status-canceled';
    if (row.assessmentStatus === 'Scored') return 'status-scored';
    if (row.assessmentStatus === 'Failed') return 'status-failed';
    if (row.assessmentStatus === 'Assessing') return 'status-assessing';
    return 'status-ok';
  }

  /**
   * Recomputed each second by the elapsed ticker (and on each poll tick). Under a re-run scope it
   * measures the re-run's own span; the run's CompletedAtUtc stays fixed across a re-run.
   */
  get runElapsedLabel(): string {
    const run = this.monitor.activeRunDetail;
    if (!run) return '—';
    if (this.runElapsedIsRerun) {
      // While running, any completion stamp is a previous re-run's, earlier than this start, and
      // would clamp the count to 0; measure against now instead.
      const rerunEnd = this.runIsRunning ? null : run.rerunCompletedAtUtc;
      return formatElapsed(elapsedMsBetween(run.rerunStartedAtUtc, rerunEnd));
    }
    if (!run.startedAtUtc) return '—';
    const ms = elapsedMsBetween(run.startedAtUtc, run.completedAtUtc);
    return formatElapsed(ms);
  }

  get runElapsedIsRerun(): boolean {
    return this.runHasRerunScope && !!this.monitor.activeRunDetail?.rerunStartedAtUtc;
  }

  get runAverageAnswerDurationLabel(): string {
    const run = this.monitor.activeRunDetail;
    if (!run || run.answers.length === 0) return '—';
    return formatDuration(Math.round(run.totalAnswerDurationMs / run.answers.length));
  }

  /**
   * Whether the Cache Creation stat is a real zero or the provider simply does not report the
   * counter. Mirrors BenchmarkReportBuilder's rule server-side: OpenAI reports cache reads but
   * not cache creation, so a zero total for that provider beside a nonzero cache-read total is
   * "not reported", not "no cache ever warmed".
   */
  get runCacheCreationUnreported(): boolean {
    const run = this.monitor.activeRunDetail;
    if (!run) return false;
    return (run.totalCacheCreationTokens ?? 0) === 0 &&
      (run.totalCacheReadTokens ?? 0) > 0 &&
      (run.testedModelProviderUsed ?? '').toLowerCase() === 'openai';
  }

  /** The diagnostics capture of the active run, which the progress dialog copies and downloads. */
  get runDiagnosticsText(): string {
    return this.runDiagnosticsTextFor(this.monitor.activeRunDetail, this.runStage);
  }

  /** The text the Diagnostics panel shows: empty while it is closed, stamped when it last refreshed. */
  get runDiagnosticsPanelText(): string {
    return this.runDiagnosticsPanelOpen
      ? this.runDiagnosticsTextFor(this.monitor.activeRunDetail, this.runStage, this.monitor.runDiagnosticsPanelCapturedAt)
      : '';
  }

  onRunDiagnosticsToggle(event: Event): void {
    this.runDiagnosticsPanelOpen = (event.target as HTMLDetailsElement).open;
    if (this.runDiagnosticsPanelOpen) {
      this.monitor.runDiagnosticsPanelCapturedAt = new Date();
    }
  }

  /**
   * Everything an operator would paste into a bug report, assembled from a run detail.
   * Answer text, thought text, and assessor comments are deliberately excluded: they are
   * long model-generated content already reachable through the run detail dialog and the
   * Markdown report. No credential or connection string appears in the DTO.
   *
   * The active run's progress figures are the progress dialog's own, re-run scope and launch
   * state included. Any other run, such as the one the run report shows, is described from its
   * detail alone, so the run report and the Download Center capture the same text for it.
   */
  runDiagnosticsTextFor(run: BenchmarkRunDetailDto | null, stage: BenchmarkRunStage, capturedAt: Date = new Date()): string {
    const live = run === this.monitor.activeRunDetail;
    const facts = live || !run ? this.activeRunDiagnosticsFacts() : this.detailDiagnosticsFacts(run);
    const lines: string[] = [];

    // Header
    lines.push('=== BENCHMARK RUN DIAGNOSTICS ===');
    lines.push(`Captured:         ${capturedAt.toISOString()}`);
    lines.push(`Overseer build:   ${this.workspace.overseerBuildVersion || 'unknown'}`);
    const userAgent = typeof navigator !== 'undefined' ? navigator.userAgent : 'unknown';
    lines.push(`Client:           ${userAgent}`);

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
      // ignore
    }
    lines.push(`Client timezone:  ${tz}${offsetStr}`);
    const pathname = typeof location !== 'undefined' ? location.pathname : '';
    lines.push(`Page:             ${pathname}`);
    lines.push('');

    if (!run) {
      lines.push('No run detail received yet.');
      lines.push('');
    } else {
      // --- RUN ---
      lines.push('--- RUN ---');
      const readerName = run.isPanelRun ? 'reference reader' : 'second reader';
      // The rail's stage number, plus the pass the rail collapses away, plus whether the server
      // reported it or the client derived it — the derivation cannot see the follow-up passes at
      // all, so which of the two produced the figure changes how much it is worth.
      const stageCount = run.reportWriterModelConfigurationId != null ? 4 : 3;
      const stageNumbers: Record<string, string> = {
        answering: `1 of ${stageCount}`, verifying: `2 of ${stageCount} (verifying)`,
        secondopinion: `2 of ${stageCount} (${readerName})`, finalizing: `3 of ${stageCount}`
      };
      const stageStr = stage === 'terminal'
        ? 'terminal'
        : `${stageNumbers[stage]} (${run.stage ? 'server' : 'derived'})`;
      lines.push(`Run ID: ${run.id}, Suite: ${run.suiteName} (${run.benchmarkSuiteId ?? 'n/a'}), Status: ${formatStatus(run.status)}, Stage: ${stageStr}, Started by: ${run.startedByUserName || 'unknown'}`);
      lines.push(`Started (raw):    ${run.startedAtUtc}`);
      const startedParsed = run.startedAtUtc ? parseServerUtcDate(run.startedAtUtc).toISOString() : 'n/a';
      lines.push(`Started (parsed): ${startedParsed}`);
      lines.push(`Completed:        ${run.completedAtUtc ?? 'n/a'}`);
      if (run.rerunStartedAtUtc) {
        lines.push(`Re-run started:   ${run.rerunStartedAtUtc}`);
        lines.push(`Re-run completed: ${run.rerunCompletedAtUtc ?? 'n/a'}`);
      }
      // The run's own span and the re-run's are separate lines: CompletedAtUtc stays fixed across a
      // re-run, so either figure printed alone under one label misstates the other.
      const runSpan = run.startedAtUtc
        ? formatElapsed(elapsedMsBetween(run.startedAtUtc, run.completedAtUtc))
        : '—';
      lines.push(`Elapsed (run):    ${runSpan}`);
      if (run.rerunStartedAtUtc) {
        const rerunSpan = live && this.runElapsedIsRerun
          ? this.runElapsedLabel
          : formatElapsed(elapsedMsBetween(run.rerunStartedAtUtc, run.rerunCompletedAtUtc));
        lines.push(`Re-run elapsed:   ${rerunSpan}`);
      }
      lines.push('');

      // --- MODELS ---
      lines.push('--- MODELS ---');
      lines.push(`Tested:   ${run.testedModelDisplayNameUsed} (${run.testedModelProviderUsed} / ${run.testedModelIdUsed})`);
      lines.push(`          thinking: ${run.testedModelThinkingLevelUsed ?? 'default'}, reasoning: ${run.testedModelReasoningModeUsed ?? 'default'}, service tier: ${this.diagnosticsServiceTierLabel(run.testedModelServiceTierUsed)}, max output tokens: ${run.testedModelMaxOutputTokensUsed ?? 'default'}, parallel mode: ${run.testedModelParallelExecutionModeUsed}`);
      lines.push(`Assessor: ${run.assessorModelDisplayNameUsed} (${run.assessorModelProviderUsed} / ${run.assessorModelIdUsed}), thinking: ${run.assessorModelThinkingLevelUsed ?? 'default'}, reasoning: ${run.assessorModelReasoningModeUsed ?? 'default'}, available=${run.assessorAvailable}`);
      if (run.isPanelRun) {
        lines.push(`Co-assessor: ${run.coAssessorModelDisplayNameUsed} (${run.coAssessorModelProviderUsed} / ${run.coAssessorModelIdUsed}), thinking: ${run.coAssessorModelThinkingLevelUsed ?? 'default'}, reasoning: ${run.coAssessorModelReasoningModeUsed ?? 'default'}`);
      }
      // The third role, named whether or not one was used: "no second opinion" is itself a fact
      // about how the run was graded, and the capture used to omit it entirely.
      // A panel run's third role is the non-scoring reference reader.
      const readerLabel = run.isPanelRun ? 'Reference reader: ' : 'Second:   ';
      if (run.secondOpinionAssessorModelConfigurationId != null) {
        lines.push(`${readerLabel}${run.secondOpinionAssessorModelDisplayNameUsed} (${run.secondOpinionAssessorModelProviderUsed} / ${run.secondOpinionAssessorModelIdUsed}), thinking: ${run.secondOpinionAssessorModelThinkingLevelUsed ?? 'default'}, reasoning: ${run.secondOpinionAssessorModelReasoningModeUsed ?? 'default'}`);
      } else {
        lines.push(`${readerLabel}none selected`);
      }
      if (run.claimVerifierModelConfigurationId != null) {
        lines.push(`Verifier: ${run.claimVerifierDisplayNameUsed} (${run.claimVerifierProviderUsed} / ${run.claimVerifierModelIdUsed}), thinking: ${run.claimVerifierThinkingLevelUsed ?? 'default'}, reasoning: ${run.claimVerifierReasoningModeUsed ?? 'default'}`);
      } else {
        lines.push('Verifier: none selected');
      }
      lines.push('');

      // --- SCORING ---
      lines.push('--- SCORING ---');
      lines.push(`Profile: ${run.scoringProfileName ?? 'n/a'} (${run.scoringProfileId ?? 'n/a'}), harness version: ${run.harnessVersion ?? '1 (unversioned legacy)'}, scoring method version: ${run.scoringMethodVersion}, max parallel questions: ${run.maxParallelQuestionsUsed}`);
      // Read out of the run's own profile snapshot server-side, so this describes the run in
      // front of it rather than whatever the default profile says today.
      lines.push(`Speed: target ${run.scoringProfileSpeedTargetMs ?? 'n/a'} ms, decay k ${run.scoringProfileSpeedDecayK ?? 'n/a'}`);
      // The outlier delta is read only by the FlaggedAndOutliers trigger, so under any other mode it
      // is a setting that governed nothing and reads as a threshold the run applied.
      const secondOpinionParts = [
        `mode ${this.diagnosticsModeName(run.secondOpinionModeUsed)}`,
        `threshold ${run.scoringProfileSecondOpinionQualityThreshold ?? 'n/a'}`
      ];
      if (run.secondOpinionModeUsed === BenchmarkSecondOpinionMode.FlaggedAndOutliers) {
        secondOpinionParts.push(`outlier delta ${run.scoringProfileSecondOpinionOutlierDeltaPoints ?? 'n/a'}`);
      }
      lines.push(`${run.isPanelRun ? 'Reference reader' : 'Second reader'}: ${secondOpinionParts.join(', ')}`);
      lines.push(`Tool call budget: ${run.maxToolCallsPerQuestionUsed ?? 'not recorded'}`);
      const preRunProbe = run.candidateDeliveryVerifiedAtUtc ? `verified at ${run.candidateDeliveryVerifiedAtUtc}` : 'not recorded';
      const reRunProbe = run.rerunCandidateDeliveryVerifiedAtUtc ? `; re-verified before the re-run at ${run.rerunCandidateDeliveryVerifiedAtUtc}` : '';
      lines.push(`Candidate delivery probe: ${preRunProbe}${reRunProbe}`);
      const boardDelivery = this.boardDeliveryLine(run);
      if (boardDelivery) {
        lines.push(boardDelivery);
        for (const gap of this.boardDeliveryGaps(run)) {
          lines.push(`Board not delivered — ${gap}`);
        }
      }
      if (run.boardFactsCheck != null) {
        // Recorded from harness 33; an earlier run's null is not a board that states no format.
        const formatRecorded = Number.parseInt(run.harnessVersion ?? '', 10) >= 33;
        const format = run.gameSnapshotFormatVersionUsed
          ?? (formatRecorded ? 'not stated' : 'not recorded (before harness 33)');
        lines.push(`Board format: ${format}`);
      }
      lines.push('');

      // --- PROGRESS ---
      lines.push('--- PROGRESS ---');
      lines.push(`Answered ${facts.answered} of ${facts.total}, scored ${facts.scored} of ${facts.total}, failed ${facts.failed.length}`);
      // The same population the report's own per-answer denominators use — an answer the
      // quality index excludes. Printed alongside the raw answer count so the capture and the
      // report cannot disagree the way run 29's 15-of-17 and 15-of-18 did.
      lines.push(`Gradeable answers (index population): ${facts.gradeable} of ${facts.total}`);
      const inFlight = run.inFlightOrderIndexes ?? [];
      lines.push(`In flight: ${inFlight.length > 0 ? inFlight.map(i => `Q${i}`).join(', ') : 'none'}`);
      const verifyingNow = run.inFlightVerificationOrderIndexes ?? [];
      const secondOpinionNow = run.inFlightSecondOpinionOrderIndexes ?? [];
      lines.push(`Verifying now: ${verifyingNow.length > 0 ? verifyingNow.map(i => `Q${i}`).join(', ') : 'none'}; ${readerName} now: ${secondOpinionNow.length > 0 ? secondOpinionNow.map(i => `Q${i}`).join(', ') : 'none'}`);
      // Run-wide figures are the finalizer's once the run is terminal; before that they are stale or
      // absent, so the answer rows are counted instead. The meters' re-run-scoped pair follows.
      const answersVerified = run.answers.filter(a => a.claimVerificationJson != null || a.claimVerificationError != null).length;
      const answersSecondGraded = run.answers.filter(a => a.secondOpinionQualityScore != null || a.secondOpinionError != null).length;
      const verifiedRunWide = facts.terminal ? (run.claimVerifiedAnswerCount ?? answersVerified) : answersVerified;
      const secondGradedRunWide = facts.terminal ? (run.secondOpinionGradedAnswerCount ?? answersSecondGraded) : answersSecondGraded;
      const hasRerunScope = facts.rerunScope.length > 0;
      const scopedPair = hasRerunScope ? ` (re-run scope: ${facts.verifiedInScope}, ${facts.secondOpinionInScope})` : '';
      lines.push(`Answers with verified claims: ${verifiedRunWide}, ${run.isPanelRun ? 'reference-read' : 'second-graded'} ${secondGradedRunWide}${scopedPair}`);
      if (hasRerunScope) {
        const scopeLabel = facts.terminal ? 'Failed-question re-run covered' : 'Failed-question re-run in progress over';
        lines.push(`${scopeLabel}: ${facts.rerunScope.map(i => `Q${i}`).join(', ')}`);
        const reAnswered = run.rerunAnsweredOrderIndexes ?? [];
        const reScored = run.rerunScoredOrderIndexes ?? [];
        const qList = (xs: number[]) => xs.length > 0 ? xs.map(i => `Q${i}`).join(', ') : 'none';
        lines.push(`Re-run answered: ${qList(reAnswered)} (${reAnswered.length} of ${facts.rerunScope.length}); re-run scored: ${qList(reScored)} (${reScored.length} of ${facts.rerunScope.length})`);
      }
      // The launch state and the suite question list belong to the progress dialog's run.
      if (live) {
        lines.push(`Re-run launch pending: ${this.monitor.rerunLaunchPending}`);
        if (this.monitor.runProgressQuestions.length > 0 && this.monitor.runProgressQuestionsSuiteId != null) {
          lines.push(`Suite questions loaded: ${this.monitor.runProgressQuestions.length} for suite ${this.monitor.runProgressQuestionsSuiteId}`);
        } else {
          lines.push('Suite questions loaded: not loaded — list degraded to answers only');
        }
      }
      lines.push('');

      // --- TOKENS ---
      lines.push('--- TOKENS ---');
      lines.push(`input: ${run.totalInputTokens}, output: ${run.totalOutputTokens}, cache read: ${run.totalCacheReadTokens}, cache creation: ${run.totalCacheCreationTokens}`);
      lines.push(`total duration: ${formatDuration(run.totalDurationMs)}, total answer duration: ${formatDuration(run.totalAnswerDurationMs)}`);
      // H1: the run-level model-call figure, and input tokens per call — the figure that attributes
      // input-token growth to call count rather than context size. Omitted entirely for runs that never
      // recorded a model-call count.
      const modelCallAnswers = (run.answers ?? []).filter(a => a.modelCallCount != null && a.modelCallCount > 0);
      if (modelCallAnswers.length > 0) {
        const totalModelCalls = modelCallAnswers.reduce((sum, a) => sum + (a.modelCallCount ?? 0), 0);
        const meanModelCalls = totalModelCalls / modelCallAnswers.length;
        const perCall = totalModelCalls > 0 ? Math.round(run.totalInputTokens / totalModelCalls) : 0;
        lines.push(`model calls: ${totalModelCalls} (mean ${meanModelCalls.toFixed(1)}), input tokens per model call: ${perCall}`);
      }
      lines.push('');

      // --- SCORES --- (only when terminal)
      if (facts.terminal) {
        lines.push('--- SCORES ---');
        // `finalScore` is the Holistic Assessor Score, which feeds no aggregate — labelling it
        // "final" read as the canonical result, which is the Intelligence Index (quality index).
        // `computed` is the superseded ComputedScore column, which current runs never write:
        // printing "computed: n/a" on every capture read as a missing value rather than a
        // retired one, so it appears only where a historical run actually has it.
        // A panel run has two holistic scores, one per member's own synthesis.
        const scoreParts = [
          run.isPanelRun
            ? `holistic: A ${run.finalScore ?? 'n/a'}, B ${run.coAssessorFinalScore ?? 'n/a'}`
            : `holistic: ${run.finalScore ?? 'n/a'}`,
          `quality index: ${run.qualityIndex ?? 'n/a'}`,
          `unweighted mean: ${run.unweightedQualityIndex ?? 'not recorded'}`,
          `raw quality index: ${run.rawQualityIndex ?? 'n/a'}`,
          `speed index: ${run.speedIndex ?? 'n/a'}`
        ];
        if (run.computedScore != null) {
          scoreParts.splice(1, 0, `computed (superseded): ${run.computedScore}`);
        }
        lines.push(scoreParts.join(', '));
        if (run.isPanelRun) {
          lines.push(this.panelDiagnosticsLine(run));
        }
        lines.push('');

        // --- INTEGRITY ---
        //
        // The report's four-class accounting, which partitions every answer, plus the advisory
        // counts that overlap it and the agreement figures. Without these the capture could not
        // say why a run's status was what it was.
        lines.push('--- INTEGRITY ---');
        const clean = run.totalQuestionCount
          - (run.transportDefectAnswerCount ?? 0)
          - (run.recoveredAnswerCount ?? 0)
          - (run.toolStarvedAnswerCount ?? 0);
        lines.push(`clean: ${clean}, transport defects: ${run.transportDefectAnswerCount ?? 0}, recovered: ${run.recoveredAnswerCount ?? 0}, harness limits: ${run.toolStarvedAnswerCount ?? 0} (sums to ${run.totalQuestionCount})`);
        // A null contested-accuracy-deduction count is a run before harness 20: not recorded, never 0.
        lines.push(`advisory flags: ${run.advisoryFlagAnswerCount ?? 0}, scrubbed: ${run.scrubbedArtifactAnswerCount ?? 0}, contested verdicts: ${run.contestedVerdictAnswerCount ?? 0}, unevidenced deductions: ${run.unevidencedDeductionAnswerCount ?? 0}, refuted claims: ${run.refutedClaimAnswerCount ?? 0}, contested critical errors: ${run.contestedCriticalErrorAnswerCount ?? 0}, contested accuracy deductions: ${run.contestedAccuracyDeductionAnswerCount ?? 'not recorded'}, dimension outliers: ${run.dimensionOutlierAnswerCount ?? 'not recorded'}, re-assessed: ${run.reassessedAnswerCount ?? 0}`);
        // The run-level counts above are member A's; member B's come from its own record.
        if (run.isPanelRun) {
          lines.push(this.memberBFlagsLine(run));
        }
        // Computed from `run`, not from the run-detail getters: this capture describes the
        // *active* run, and those getters read whichever run the detail dialog has open.
        const criticalAnswersHere = this.criticalErrorAnswersOf(run);
        const criticalHere = criticalAnswersHere.map(a => a.orderIndex);
        const criticalSplitHere = run.isPanelRun
          ? `; member A ${criticalAnswersHere.filter(a => a.criticalError).length}, member B ${criticalAnswersHere.filter(a => a.coAssessmentCriticalError === true).length}`
          : '';
        const unverifiedHere = run.isPanelRun
          ? this.panelUnverifiedClaimsLabel(run)
          : `${run.answers.reduce((sum, a) => sum + (a.unverifiedClaimCount ?? 0), 0)}`;
        lines.push(`critical errors: ${criticalHere.length}${criticalHere.length > 0 ? ` (${criticalHere.map(i => 'Q' + i).join(', ')}${criticalSplitHere})` : ''}, unverified claims: ${unverifiedHere}`);
        const outcomeSummary = this.outcomeSummaryOf(run);
        if (outcomeSummary) {
          lines.push(this.criticalErrorResolutionDiagnosticsLine(outcomeSummary));
          lines.push(this.outcomeDiagnosticsLine(outcomeSummary));
        }
        const signedDeltaStr = run.secondOpinionMeanSignedDelta != null
          ? `, ${run.secondOpinionMeanSignedDelta > 0 ? '+' : run.secondOpinionMeanSignedDelta < 0 ? '−' : ''}${Math.abs(run.secondOpinionMeanSignedDelta).toFixed(1)} mean signed delta`
          : '';
        const splitsStr = (run.secondOpinionCriticalErrorSplitCount ?? 0) > 0
          ? `, critical-error splits: ${run.secondOpinionCriticalErrorSplitCount}`
          : '';
        // In a panel run the reference reader is compared with the panel score.
        lines.push(`${run.isPanelRun ? 'reference reader vs panel' : 'agreement'}: ${run.secondOpinionMeanAbsDelta != null ? run.secondOpinionMeanAbsDelta.toFixed(1) + ' mean abs delta' : 'not measured'}${signedDeltaStr} over ${run.secondOpinionGradedAnswerCount ?? 0} of ${run.answeredQuestionCount} answered, disagreements: ${run.secondOpinionDisagreementCount ?? 0}${splitsStr}`);
        if (run.secondOpinionAssessorModelConfigurationId != null && (run.secondOpinionGradedAnswerCount ?? 0) === 0) {
          lines.push(`${readerName}: selected (${run.secondOpinionAssessorModelDisplayNameUsed ?? 'configured'}) but no answer met a trigger — 0 graded`);
        }
        if ((run.secondOpinionGradedAnswerCount ?? 0) > 0 && run.secondOpinionModeUsed !== 3) {
          lines.push('  (coverage selected by trigger — conditioned on the first assessor\'s own uncertainty, not an unbiased agreement rate)');
        }
        lines.push('');

        // --- CLAIM VERIFICATION ---
        if (run.claimVerifierModelConfigurationId != null || (run.claimVerifiedAnswerCount ?? 0) > 0) {
          lines.push('--- CLAIM VERIFICATION ---');
          const verifierName = run.claimVerifierDisplayNameUsed ?? (run.claimVerifierModelConfigurationId != null ? 'configured' : 'none');
          lines.push(`verifier: ${verifierName} (${run.claimVerifierProviderUsed ?? 'n/a'}, ${run.claimVerifierModelIdUsed ?? 'n/a'})`);
          lines.push(`outcomes: ${run.claimsSupportedCount ?? 0} supported, ${run.claimsRefutedCount ?? 0} refuted, ${run.claimsIndeterminateCount ?? 0} indeterminate across ${run.claimVerifiedAnswerCount ?? 0} answer(s)`);
          const verifiedAnswersWithErrors = run.answers.filter(a => a.claimVerificationError);
          if (verifiedAnswersWithErrors.length > 0) {
            lines.push(`errors (${verifiedAnswersWithErrors.length}):`);
            for (const a of verifiedAnswersWithErrors) {
              const captured = a.claimVerificationRawText ? ' (raw response captured)' : '';
              lines.push(`  Q${a.orderIndex}: ${a.claimVerificationError}${captured}`);
            }
          }
          lines.push('');
        }

        // --- CHAT PROMPT ---
        lines.push('--- CHAT PROMPT ---');
        lines.push(`source: ${run.candidatePromptSourceUsed ?? 'not recorded'}`);
        lines.push(`promptSha256: ${run.candidateSystemPromptSha256 ?? 'not recorded'}`);
        lines.push(`toolGuidesSha256: ${run.toolGuidesSha256 ?? 'not recorded'}`);
        lines.push(`knowledgeBaseHeadSha: ${run.knowledgeBaseHeadSha ?? 'not recorded'}`);
        lines.push(`wikiHeadSha: ${run.wikiHeadSha ?? 'not recorded'}`);
        lines.push(`sourceCodeHeadSha: ${run.sourceCodeHeadSha ?? 'not recorded'}`);
        lines.push(`parallelMode: ${run.testedModelParallelExecutionModeUsed}`);
        if (run.candidatePromptOptionsJson) {
          try {
            const opts = JSON.parse(run.candidatePromptOptionsJson);
            lines.push(`options: mode=${opts.overseerMode ?? 0}, verbose=${opts.verboseMode ?? false}, spoilerFree=${opts.spoilerFreeMode ?? false}, tools=${opts.enableToolUse ?? true}, webSearch=${opts.enableWebSearch ?? false}, subagents=${opts.enableSubAgents ?? false}, sourceCodeRefs=${opts.allowSourceCodeReferences ?? true}, snapshot=${opts.hasGameSnapshot ?? false}`);
          } catch {
            lines.push(`options: ${run.candidatePromptOptionsJson}`);
          }
        } else {
          lines.push('options: not recorded for this run');
        }
        lines.push('');

        // --- TOOL ROUTING ---
        lines.push('--- TOOL ROUTING ---');

        // H3: read the server's classification. It comes from BenchmarkChatTransfer.ClassifyTool, which is
        // also what the run report reads, so the two artifacts can no longer disagree about which family a
        // tool belongs to. The client-side loop below survives only as a fallback for a run detail served
        // before toolFamilyCounts existed; it is the copy that drifted every time a tool was added, and
        // nothing new should be added to it.
        const serverFamilies = run.toolFamilyCounts;
        let totalCalls = 0;
        let sourceCalls = 0;
        let wikiCalls = 0;
        let lookupCalls = 0;
        let kbCalls = 0;
        let otherCalls = 0;
        let zeroKbAnswers = 0;

        if (serverFamilies) {
          sourceCalls = serverFamilies['source'] ?? 0;
          wikiCalls = serverFamilies['wiki'] ?? 0;
          lookupCalls = serverFamilies['lookup'] ?? 0;
          kbCalls = serverFamilies['knowledgeBase'] ?? 0;
          otherCalls = serverFamilies['other'] ?? 0;
          totalCalls = sourceCalls + wikiCalls + lookupCalls + kbCalls + otherCalls;
          zeroKbAnswers = run.zeroKnowledgeBaseAnswerCount ?? 0;
        } else {
          for (const ans of run.answers) {
            let ansKb = 0;
            if (ans.toolCallSummary) {
              const parts = ans.toolCallSummary.split(',').map(s => s.trim()).filter(s => s.length > 0);
              for (const part of parts) {
                const match = part.match(/^([a-zA-Z0-9_-]+)(?:×(\d+))?$/);
                if (match) {
                  const name = match[1].toLowerCase();
                  const count = match[2] ? parseInt(match[2], 10) : 1;
                  totalCalls += count;
                  if (['source_code_search', 'source_code_view', 'search_definitions', 'get_function_definition', 'get_constants', 'list_indexed_files'].includes(name)) {
                    sourceCalls += count;
                  } else if (['wiki_search', 'wiki_view', 'nethack_wiki_search', 'nethack_wiki_view'].includes(name)) {
                    wikiCalls += count;
                  } else if (['monster_lookup', 'item_lookup', 'get_monster_stats', 'get_item_stats'].includes(name)) {
                    lookupCalls += count;
                  } else if (['get_knowledge_article'].includes(name)) {
                    kbCalls += count;
                    ansKb += count;
                  } else {
                    otherCalls += count;
                  }
                }
              }
            }
            if (ansKb === 0) zeroKbAnswers++;
          }
        }
        lines.push(`total calls: ${totalCalls} (source: ${sourceCalls}, wiki: ${wikiCalls}, lookup: ${lookupCalls}, kb: ${kbCalls}, other: ${otherCalls})`);
        // Qualified to match the report. Unqualified, this is the artifact most likely to be pasted into an
        // analysis, and it reads as a knowledge-base under-use finding that the transfer skill has already
        // withdrawn twice (run 11, T4): the prompt scopes the knowledge base away from game mechanics, so a
        // game-mechanics suite making no knowledge-base calls is compliance, not under-use.
        // Over the gradeable-answer population, which is what the report's own knowledge-base
        // routing lines use. Against run.answers.length the two artifacts printed different
        // denominators for one run.
        lines.push(`answers with 0 knowledge base calls: ${zeroKbAnswers} of ${facts.gradeable} gradeable (${run.answers.length} answer row(s))`);
        // A suite that asks a knowledge-base topic (the server's HasKnowledgeBaseRoutingQuestion, over the
        // question texts) gets no compliance qualification: the report judges it instead.
        if (run.hasKnowledgeBaseRoutingQuestion) {
          lines.push("  (the suite has knowledge-base topics; see the report's Tool Routing section)");
        } else {
          lines.push('  (prompt-compliant on game-mechanics topics — ChatService.cs "Information Routing" scopes the');
          lines.push('   knowledge base to app navigation, settings, controls, replay, vault and troubleshooting)');
        }
        lines.push('');
      }

      // --- REPORTS ---
      if (run.reportWriterModelConfigurationId != null) {
        lines.push(...this.reportsDiagnosticsLines(run, live));
      }

      // --- FLAGS ---
      lines.push('--- FLAGS ---');
      const purposePresent = !!run.purposeStatementUsed;
      // The launcher skips the same-provider gate for a panel run, so its stored false means nothing.
      const sameProvider = run.isPanelRun ? 'n/a (panel run)' : `${run.sameProviderAcknowledged ?? false}`;
      lines.push(`difficultyFallbackUsed=${run.difficultyFallbackUsed}, speedMeasurementDegraded=${run.speedMeasurementDegraded}, assessmentParseFailed=${run.assessmentParseFailed}, sameProviderAcknowledged=${sameProvider}, purposeStatement present=${purposePresent}`);
      lines.push('');
    }

    // --- POLLING ---
    lines.push('--- POLLING ---');
    const runPollStr = this.monitor.pollTickerHandle
      ? `active every ${BenchmarkActiveRunMonitor.RUN_POLL_INTERVAL_MS} ms (${this.monitor.pollTickerHandle.mode})`
      : 'stopped';
    lines.push(`Run poll: ${runPollStr}`);
    const tickerStr = this.monitor.runElapsedInterval ? `active every ${BenchmarkActiveRunMonitor.RUN_ELAPSED_TICK_MS} ms` : 'stopped';
    lines.push(`Elapsed ticker: ${tickerStr}`);
    if (this.monitor.lastRunPollAtUtc) {
      const pollAgoSec = Math.max(0, Math.floor((Date.now() - new Date(this.monitor.lastRunPollAtUtc).getTime()) / 1000));
      lines.push(`Last poll: ${this.monitor.lastRunPollAtUtc} (${pollAgoSec}s ago)`);
    }
    if (this.monitor.lastRunPollError) {
      lines.push(`Last poll error: ${this.monitor.lastRunPollError} (consecutive failures: ${this.monitor.runPollFailureCount})`);
    }
    lines.push(`Document hidden: ${typeof document !== 'undefined' ? document.hidden : false}, focused: ${typeof document !== 'undefined' ? document.hasFocus() : false}`);
    const soundDiag = this.completionSoundService.diagnostics;
    lines.push(`Completion sound: ${this.launcher.completionSound ? 'enabled' : 'disabled'}, last outcome ${this.monitor.lastCompletionSoundOutcome ?? 'n/a'}`);
    lines.push(`  armed=${soundDiag.armed}, arming=${soundDiag.arming}, AudioContext state=${soundDiag.audioContextState ?? 'n/a'}, `
      + `path=${soundDiag.lastPlayPath ?? 'n/a'}, deferred settle=${soundDiag.lastDeferredSettleMs != null ? soundDiag.lastDeferredSettleMs + ' ms' : 'n/a'}`);
    lines.push('  attempts:');
    for (const a of soundDiag.attempts) {
      lines.push(`    ${a.atUtc} key=${a.key} hidden=${a.hidden} focused=${a.focused} path=${a.path} `
        + `ctxBefore=${a.contextStateBefore ?? 'n/a'} ctxAfter=${a.contextStateAfter ?? 'n/a'} `
        + `clockAdvanced=${a.clockAdvanced ?? 'n/a'} rebuilt=${a.rebuilt} outcome=${a.outcome}`);
    }
    lines.push(`  context states: ${soundDiag.contextStateEvents.length
      ? soundDiag.contextStateEvents.map(e => `${e.atUtc} ${e.state}`).join(', ')
      : 'none'}`);
    const notificationSupported = this.completionNotificationService.isSupported();
    const notificationPermission = notificationSupported && typeof Notification !== 'undefined' ? Notification.permission : 'n/a';
    lines.push(`Completion notification: ${this.launcher.completionNotification ? 'enabled' : 'disabled'}, supported=${notificationSupported}, `
      + `permission=${notificationPermission}, status=${this.monitor.completionNotificationStatus ?? 'n/a'}`
      + `${this.completionNotificationService.lastError ? `, last error: ${this.completionNotificationService.lastError}` : ''}`);
    lines.push('  attempts:');
    for (const a of this.monitor.notificationAttempts) {
      lines.push(`    ${a.atUtc} key=${a.key} hidden=${a.hidden} focused=${a.focused} outcome=${a.outcome}`);
    }
    lines.push(`Background lock: ${this.backgroundActivity.state}`
      + `${this.backgroundActivity.heldName ? ` (${this.backgroundActivity.heldName})` : ''}`
      + `${this.backgroundActivity.lastError ? `, error: ${this.backgroundActivity.lastError}` : ''}`);
    lines.push('');

    // --- ERRORS ---
    lines.push('--- ERRORS ---');
    let hasError = false;
    if (run?.errorMessage) {
      lines.push(`Run error: ${run.errorMessage}`);
      hasError = true;
    }
    if (live && this.monitor.runQuestionsLoadError) {
      lines.push(`Questions fetch error: ${this.monitor.runQuestionsLoadError}`);
      hasError = true;
    }
    const failureGroups = this.failedAnswerGroups(facts.failed);
    for (const group of failureGroups) {
      const statusPrefix = group.httpStatusCode != null ? `HTTP ${group.httpStatusCode} — ` : '';
      const questionList = group.questions.map(q => `Q${q}`).join(', ');
      lines.push(`${questionList}: ${statusPrefix}${group.message}`);
      hasError = true;
    }
    if (!hasError) {
      lines.push('none');
    }
    lines.push('');

    // --- QUESTIONS ---
    if (run && facts.rows.length > 0) {
      lines.push('--- QUESTIONS ---');
      for (const row of facts.rows) {
        const ans = row.answer;
        if (!ans) {
          lines.push(`[Q${row.orderIndex}] status=${row.status === 'Answering' ? 'Answering' : 'Pending'}`);
          continue;
        }
        const parts = [
          `status=${this.formatAnswerStatus(ans.status)}`,
          `assessment=${this.formatAssessmentStatus(ans.assessmentStatus)}`,
          `duration=${ans.durationMs}ms`,
          `finishReason=${ans.providerFinishReason ?? 'n/a'}`
        ];
        if (ans.timeToFirstTokenMs != null) parts.push(`ttft=${ans.timeToFirstTokenMs}ms`);
        if (ans.inputTokens != null) parts.push(`in=${ans.inputTokens}`);
        if (ans.outputTokens != null) parts.push(`out=${ans.outputTokens}`);
        if (ans.cacheReadInputTokens != null) parts.push(`cacheR=${ans.cacheReadInputTokens}`);
        if (ans.cacheCreationInputTokens != null) parts.push(`cacheC=${ans.cacheCreationInputTokens}`);
        if (ans.actualServiceTierUsed) parts.push(`tier=${ans.actualServiceTierUsed}`);
        if (ans.httpStatusCode != null) parts.push(`http=${ans.httpStatusCode}`);
        // score=, quality= and levels= are member A's in a panel run; panel=, b= and bLevels= follow.
        if (ans.score != null) parts.push(`score=${ans.score}`);
        if (run.isPanelRun) {
          parts.push(`panel=${ans.panelQualityScore ?? 'n/a'}`, `b=${ans.coAssessmentQualityScore ?? 'n/a'}`);
          const co = this.coAssessmentOf(ans);
          if (co?.accuracyLevel != null) {
            parts.push(`bLevels=${co.accuracyLevel}/${co.completenessLevel ?? '?'}/${co.concisenessLevel ?? '?'}/${co.readabilityLevel ?? '?'}`);
          }
        }

        // Everything below is already on the DTO; the capture simply did not print it, which is
        // why an old diagnostics file could not reconstruct why any answer scored what it did.
        parts.push(`band=${this.formatDifficulty(ans.difficulty)}`);
        if (ans.assessedDifficulty != null) parts.push(`assessedDiff=${ans.assessedDifficulty}`);
        if (ans.qualityScore != null) parts.push(`quality=${ans.qualityScore}`);
        if (ans.rawQualityScore != null && ans.rawQualityScore !== ans.qualityScore) parts.push(`rawQuality=${ans.rawQualityScore}`);
        if (ans.speedScore != null) parts.push(`speed=${ans.speedScore}`);
        if (ans.accuracyLevel != null) {
          parts.push(`levels=${ans.accuracyLevel}/${ans.completenessLevel ?? '?'}/${ans.concisenessLevel ?? '?'}/${ans.readabilityLevel ?? '?'}`);
        }
        parts.push(`critical=${ans.criticalError === true}`);
        const budget = ans.toolCallBudgetUsed != null ? ans.toolCallBudgetUsed : 'n/a';
        const blocked = this.blockedToolCallsOf(ans);
        parts.push(`tools=${ans.toolCallCount ?? 0}/${budget}${blocked > 0 ? ` (${blocked} blocked)` : ''}${ans.toolBudgetExhausted ? ' exhausted' : ''}`);
        // H1: beside tools=, because "many calls" and "large context" produce the same input-token total
        // and call for opposite responses. Omitted where it was never recorded, never printed as 0.
        if (ans.modelCallCount != null) parts.push(`modelCalls=${ans.modelCallCount}`);
        if (ans.narrationBlockCount != null) parts.push(`narration=${ans.narrationBlockCount}`);
        if (ans.unverifiedClaimCount != null) parts.push(`unverified=${ans.unverifiedClaimCount}`);
        if ((ans.answerFlagNames ?? []).length > 0) parts.push(`flags=${(ans.answerFlagNames ?? []).join('|')}`);
        if (ans.secondOpinionQualityScore != null) {
          parts.push(`secondOpinion=${ans.secondOpinionQualityScore}/${ans.secondOpinionTrigger ?? 'unknown'}${ans.secondOpinionDisagreed ? ' disagreed' : ''}`);
        }
        const boardChars = run.isPanelRun ? this.panelAnswerBoardChars(ans) : this.answerBoardChars(ans);
        if (boardChars) parts.push(`boardChars=${boardChars}`);
        if (ans.evidenceInformedQualityScore != null) {
          parts.push(`evidenceInformed=${ans.evidenceInformedQualityScore}${ans.evidenceInformedCriticalError ? ' critical' : ''} withdrew=${this.evidenceInformedWithdrawn(ans).length}`);
        }
        if ((ans.reassessmentCount ?? 0) > 0) {
          parts.push(`reassessed=${ans.previousQualityScore ?? '?'}→${ans.qualityScore ?? '?'}/${ans.reassessedByModelDisplayNameUsed ?? 'unknown'}`);
        }
        lines.push(`[Q${row.orderIndex}] ${parts.join(' ')}`);
        if (ans.errorMessage) {
          lines.push(`     error: ${ans.errorMessage}`);
        }
        if (ans.assessmentError) {
          lines.push(`     assessment error: ${ans.assessmentError}`);
        }
        if (ans.assessedByModelDisplayNameUsed || ans.assessedAtUtc) {
          const assessor = `${ans.assessedByModelDisplayNameUsed || 'unknown'} (${ans.assessedByModelProviderUsed || 'unknown'} / ${ans.assessedByModelIdUsed || 'unknown'})`;
          lines.push(`     assessed by: ${assessor} at ${ans.assessedAtUtc ?? 'unknown'}`);
        }
        if (ans.rerunAtUtc) {
          const rerunError = ans.rerunOfErrorMessage ? ` — ${ans.rerunOfErrorMessage}` : '';
          lines.push(`     re-executed at ${ans.rerunAtUtc}: was ${ans.rerunOfStatus ?? 'unknown'}${rerunError}`);
        }
      }
    }

    return lines.join('\n');
  }

  /** The progress figures of the active run, as the progress dialog shows them. */
  private activeRunDiagnosticsFacts(): RunDiagnosticsFacts {
    return {
      answered: this.runAnsweredCount,
      total: this.runTotalQuestionCount,
      scored: this.runScoredCount,
      failed: this.runFailedAnswers,
      gradeable: this.runGradeableAnswerCount,
      terminal: this.runIsTerminal,
      rerunScope: this.effectiveRerunScope,
      verifiedInScope: this.runVerifiedCount,
      secondOpinionInScope: this.runSecondOpinionCount,
      rows: this.runProgressRows
    };
  }

  /** The progress figures of a run from its detail alone: its own answers, its server-reported re-run scope. */
  private detailDiagnosticsFacts(run: BenchmarkRunDetailDto): RunDiagnosticsFacts {
    const scope = run.rerunScopeOrderIndexes ?? [];
    const inScope = new Set(scope);
    const scoped = (answers: BenchmarkRunAnswerDto[]) =>
      scope.length > 0 ? answers.filter(a => inScope.has(a.orderIndex)).length : answers.length;
    return {
      answered: run.answers.length,
      total: run.totalQuestionCount,
      scored: run.answers.filter(a => {
        const s = this.formatAssessmentStatus(a.assessmentStatus);
        return s === 'Scored' || s === 'Failed';
      }).length,
      failed: run.answers.filter(a => this.isAnswerFailed(a)),
      gradeable: run.answers.filter(a => this.countsTowardQualityIndex(a)).length,
      terminal: formatStatus(run.status) !== 'Running',
      rerunScope: scope,
      verifiedInScope: scoped(run.answers.filter(a => a.claimVerificationJson != null || a.claimVerificationError != null)),
      secondOpinionInScope: scoped(run.answers.filter(a => a.secondOpinionQualityScore != null || a.secondOpinionError != null)),
      rows: [...run.answers]
        .sort((a, b) => a.orderIndex - b.orderIndex)
        .map(a => ({
          orderIndex: a.orderIndex,
          questionText: a.questionText,
          answer: a,
          status: this.formatAnswerStatus(a.status),
          assessmentStatus: this.formatAssessmentStatus(a.assessmentStatus),
          errorMessage: a.errorMessage ?? null
        }))
    };
  }

  /**
   * Mode names for the diagnostics capture, which is read as plain text and not localised. Switched
   * on the enum rather than on bare integers so a mode added to `BenchmarkSecondOpinionMode` cannot
   * be missing here while the two remain in the same file.
   */
  private diagnosticsModeName(mode: number | null | undefined): string {
    switch (mode) {
      case BenchmarkSecondOpinionMode.Off: return 'Off';
      case BenchmarkSecondOpinionMode.Flagged: return 'Flagged';
      case BenchmarkSecondOpinionMode.FlaggedAndOutliers: return 'FlaggedAndOutliers';
      case BenchmarkSecondOpinionMode.All: return 'All';
      case BenchmarkSecondOpinionMode.FlaggedPlusSample: return 'FlaggedPlusSample';
      default: return 'unknown';
    }
  }

  /**
   * Calls the budget refused, parsed from the tool summary the executor writes. `toolCallCount`
   * counts attempts, so printing it against the budget alone produced lines like "27 of 25".
   */
  blockedToolCallsOf(answer: BenchmarkRunAnswerDto): number {
    if (answer.toolCallsBlocked != null) return answer.toolCallsBlocked;
    const match = /\((\d+)\s+blocked by budget\)/i.exec(answer.toolCallSummary ?? '');
    if (!match) return 0;
    const parsed = Number(match[1]);
    return Number.isFinite(parsed) ? Math.min(parsed, answer.toolCallCount ?? parsed) : 0;
  }

  get runDiagnosticsCopyStatus(): string {
    if (this.copiedRunDiagnostics) return 'Diagnostics copied to clipboard';
    return this.monitor.runDiagnosticsCopyFailed ? 'Could not copy the diagnostics to the clipboard.' : '';
  }

  async copyRunDiagnostics(): Promise<void> {
    const text = this.runDiagnosticsText;
    if (!text) { return; }
    try {
      await navigator.clipboard.writeText(text);
      this.monitor.runDiagnosticsCopyFailed = false;
      this.copiedRunDiagnostics = true;
      if (this.copiedRunDiagnosticsTimer) { clearTimeout(this.copiedRunDiagnosticsTimer); }
      this.copiedRunDiagnosticsTimer = setTimeout(() => {
        this.copiedRunDiagnostics = false;
        this.copiedRunDiagnosticsTimer = null;
        this.viewSync.notify();
      }, 2000);
    } catch {
      this.copiedRunDiagnostics = false;
      this.monitor.runDiagnosticsCopyFailed = true;
      this.monitor.runErrorMessage = 'Could not copy the benchmark run diagnostics to the clipboard.';
    }
  }

  /**
   * The diagnostics' REPORTS block for a run that names a report writer: the writer, the stored status
   * and message, and the sums over the stored run-completion documents. The progress dialog's own run
   * adds the stage and the job view it polls.
   */
  private reportsDiagnosticsLines(run: BenchmarkRunDetailDto, live: boolean): string[] {
    const lines: string[] = ['--- REPORTS ---'];
    const writerName = run.reportWriterDisplayName ?? `configuration ${run.reportWriterModelConfigurationId} (deleted)`;
    lines.push(`Writer: ${writerName} (${run.reportWriterProvider ?? 'n/a'} / ${run.reportWriterModelId ?? 'n/a'}), thinking: ${run.reportWriterThinkingLevel ?? 'default'}`);
    lines.push(`Status: ${BenchmarkRunReportDocumentsStatus[reportDocumentsStatusOf(run)] ?? 'unknown'}`);
    lines.push(`Message: ${run.reportDocumentsMessage?.trim() || 'none'}`);
    lines.push(`Documents written: ${run.reportDocumentsWrittenCount ?? 0}`);
    lines.push(`Duration: ${run.reportDocumentsDurationMs != null ? formatDuration(run.reportDocumentsDurationMs) : 'n/a'}`);
    const tokens = run.reportDocumentsInputTokens != null || run.reportDocumentsOutputTokens != null
      ? `input ${run.reportDocumentsInputTokens ?? 0}, output ${run.reportDocumentsOutputTokens ?? 0}`
      : 'n/a';
    lines.push(`Tokens: ${tokens}`);
    lines.push(`Cost: ${run.reportDocumentsCostUsd != null ? `$${run.reportDocumentsCostUsd.toFixed(4)}` : 'n/a'} (outside the run's own cost)`);
    if (live) {
      lines.push(`Stage 4: ${this.runReportStage}`);
      const job = this.monitor.activeRunReportJob;
      if (job && job.runId === run.id) {
        lines.push(`Job: phase ${job.phase}, queued ${job.queuedAtUtc}, slot acquired ${job.slotAcquiredAtUtc ?? 'n/a'}, finished ${job.finishedAtUtc ?? 'n/a'}, jobs ahead ${job.jobsAhead ?? 'n/a'}, cost so far ${job.job?.costUsd != null ? `$${job.job.costUsd.toFixed(4)}` : 'n/a'}`);
      } else {
        lines.push('Job: not polled');
      }
    }
    lines.push('');
    return lines;
  }

  /** Suite, model and run, in the order of the server's report and tool-call-log file names. */
  get runDiagnosticsFileName(): string {
    const run = this.monitor.activeRunDetail;
    if (!run) return 'overseer-benchmark-run-diagnostics.txt';
    return `${safeFileName(run.suiteName)}_${safeFileName(run.testedModelDisplayNameUsed)}_run${run.id}_diagnostics.txt`;
  }

  /** Saves the text the copy button copies; the browser's own download UI is the feedback. */
  downloadRunDiagnostics(): void {
    downloadTextFile(this.runDiagnosticsFileName, this.runDiagnosticsText, 'text/plain;charset=utf-8');
  }

  openRunProgressDialog(fromSeries = false): void {
    this.monitor.returnToSeriesOnClose = fromSeries;
    this.monitor.returnToBatteryOnClose = false;
    this.monitor.isRunProgressDialogOpen = true;
    this.monitor.runDiagnosticsCopyFailed = false;

    if (this.workspace.overseerBuildVersion === null) {
      this.systemService.getVersion().subscribe({
        next: (version) => {
          this.workspace.overseerBuildVersion = version;
        },
        error: (err) => {
          console.warn('Failed to get Overseer build version', err);
          this.workspace.overseerBuildVersion = 'unknown';
        }
      });
    }

    if (this.runIsRunning || this.runReportStage === 'current') {
      this.monitor.startRunElapsedTicker();
    }

    // Resolved here and not in the poll handler: the suite's questions only supply the first
    // pass's not-yet-answered rows, so one fetch per dialog open is enough.
    const suiteId = this.monitor.activeRunDetail?.benchmarkSuiteId ?? this.launcher.selectedSuiteId;
    if (suiteId != null && this.monitor.runProgressQuestionsSuiteId !== suiteId) {
      this.monitor.loadRunProgressQuestions(suiteId);
    }

    this.runProgressDialog?.nativeElement.showModal();
    this.viewSync.notify();
    this.runProgressHeading?.nativeElement.focus();
  }

  closeRunProgressDialog(returnToSeries: boolean = this.monitor.returnToSeriesOnClose): void {
    const shouldReturn = returnToSeries;
    const shouldReturnToBattery = this.monitor.returnToBatteryOnClose;
    this.monitor.returnToSeriesOnClose = false;
    this.monitor.returnToBatteryOnClose = false;
    this.monitor.isRunProgressDialogOpen = false;
    this.monitor.stopRunElapsedTicker();
    this.runProgressDialog?.nativeElement.close();
    if (shouldReturn && this.monitor.activeSeriesId != null) {
      this.monitor.openMultiRunDialog();
    } else if (shouldReturnToBattery && this.dialogBatteryRunId != null) {
      this.monitor.openBatteryDialog();
    }
    this.viewSync.notify();
  }

  /** Terminal-state action: hand the operator over to the existing full run detail dialog. */
  viewActiveRunDetail(): void {
    const runId = this.monitor.activeRunDetail?.id ?? this.monitor.activeRunId;
    if (runId == null) return;
    this.monitor.returnToBatteryOnClose = false;
    this.closeRunProgressDialog(false);
    this.viewRunDetail(runId);
  }

  /** Re-runs the failed questions without leaving the dialog, so the retry stays watchable. */
  rerunFailedFromProgress(): void {
    const runId = this.monitor.activeRunDetail?.id ?? this.monitor.activeRunId;
    if (runId == null) return;
    this.monitor.armCompletionSignalsFromGesture();
    this.monitor.launchFailedQuestionRerun(runId, this.runFailedAnswers.map(a => a.orderIndex));
  }

  /**
   * Re-runs the failed questions from the run detail view, and opens the progress dialog to
   * watch it — the retry is the same execution either way, so it belongs in the one place that
   * shows a run's progress rather than behind a strip whose only other action was Cancel.
   */
  rerunFailedFromRunDetail(runId: number): void {
    this.monitor.armCompletionSignalsFromGesture();
    const failed = (this.selectedRunDetail?.answers ?? [])
      .filter(a => this.isAnswerFailed(a))
      .map(a => a.orderIndex);
    this.closeRunDetail();
    this.activeSubTab = 'run';
    this.monitor.launchFailedQuestionRerun(runId, failed);
  }

  /**
   * Hands the operator from the run-detail view to the progress dialog for the same run,
   * without starting anything. The strip that offers this appears exactly when the run is busy,
   * which is when the dialog is the only place its progress is legible.
   */
  openRunProgressForSelectedRun(): void {
    const runId = this.selectedRunDetail?.id;
    if (runId == null) return;
    this.closeRunDetail();
    this.activeSubTab = 'run';
    this.monitor.activeRunId = runId;
    this.monitor.startPolling(runId);
    if (!this.monitor.isRunProgressDialogOpen) {
      this.openRunProgressDialog();
    }
    this.viewSync.notify();
  }

  private openPendingRun(): void {
    if (this.pendingOpenRunId == null) {
      return;
    }
    const runId = this.pendingOpenRunId;
    this.pendingOpenRunId = null;
    this.viewRunDetail(runId);
    this.openRunHandled.emit();
  }

  viewRunDetail(runId: number) {
    const token = ++this.runDetailLoadToken;
    if (this.runDetailRequestedId !== runId) {
      this.questionFilters.clear();
    }
    this.runDetailRequestedId = runId;
    this.runDetailLoadError = null;
    this.loadingDetail = true;
    this.selectedRunDetail = null;
    this.expandedQuestions.clear();
    this.expandedThoughts.clear();
    this.expandedArtifacts.clear();
    this.expandedToolCalls.clear();
    this.toolCallsByAnswer.clear();
    this.loadingToolCalls.clear();
    this.toolCallsErrorByAnswer.clear();
    this.expandedToolCallFields.clear();
    this.calibrations = [];
    this.calibrationErrorMessage = null;
    this.calibrationAssessorConfigId = this.workspace.benchmarkCapableConfigs[0]?.id ?? null;
    this.calibrationTarget = 'Assessor';
    // Re-scoring reloads the run into the dialog that is already open, on the tab it shows.
    const dialog = this.runDetailDialog?.nativeElement;
    if (!dialog?.open) {
      this.runReportTab = this.storedRunReportTab();
    }
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    this.loadCalibrations(runId);

    this.benchmarkService.getRun(runId).subscribe({
      next: (data) => {
        if (token !== this.runDetailLoadToken) return;
        this.selectedRunDetail = data;
        this.loadingDetail = false;
        this.viewSync.notify();
        // The header's tooltips and the Re-run popover are new anchors to the polyfill.
        refreshAnchorPositioning();
      },
      error: (err) => {
        if (token !== this.runDetailLoadToken) return;
        this.loadingDetail = false;
        this.runDetailLoadError = this.runDetailLoadErrorOf(err, runId);
        console.error('Failed to load run details', err);
        this.viewSync.notify();
      }
    });
  }

  // --- Run report: tabs ---

  readonly runReportTabs = RUN_REPORT_TABS;

  runReportTab: RunReportTabKey = 'summary';

  /** The remembered tab, or Summary when none is stored, it is unknown, or storage is unavailable. */
  private storedRunReportTab(): RunReportTabKey {
    try {
      const stored = localStorage.getItem(RUN_REPORT_TAB_STORAGE_KEY);
      return RUN_REPORT_TABS.find(tab => tab.key === stored)?.key ?? 'summary';
    } catch {
      return 'summary';
    }
  }

  selectRunReportTab(key: RunReportTabKey): void {
    this.runReportTab = key;
    try {
      localStorage.setItem(RUN_REPORT_TAB_STORAGE_KEY, key);
    } catch {
      // Storage unavailable: the choice lasts until the page reloads.
    }
    this.runReportFrame?.scrollBodyToTop();
    this.cdr.markForCheck();
  }

  onRunReportTabKeydown(event: KeyboardEvent, index: number): void {
    const count = RUN_REPORT_TABS.length;
    const targets: Record<string, number> = { ArrowRight: index + 1, ArrowLeft: index - 1, Home: 0, End: count - 1 };
    const requested = targets[event.key];
    if (requested === undefined) {
      return;
    }
    event.preventDefault();
    const key = RUN_REPORT_TABS[(requested + count) % count].key;
    this.selectRunReportTab(key);
    document.getElementById(`rr-tab-${key}`)?.focus();
  }

  /** The run report's Try again after a failed load. */
  retryRunDetail(): void {
    if (this.runDetailRequestedId != null) {
      this.viewRunDetail(this.runDetailRequestedId);
    }
  }

  /** The failure line of the run report, from the server's message where it sent one. */
  private runDetailLoadErrorOf(err: any, runId: number): string {
    if (err?.status === 404) {
      return `Run #${runId} no longer exists.`;
    }
    if (err?.status === 0) {
      return `Run #${runId} could not be loaded: the server could not be reached.`;
    }
    const message = typeof err?.error === 'string' ? err.error : err?.error?.message;
    return message ? `Run #${runId} could not be loaded: ${message}` : `Run #${runId} could not be loaded.`;
  }

  /**
   * Closes the run report. An open dialog's close event runs {@link onRunDetailClosed}; a dialog
   * that is not open is cleaned up here, so the cleanup runs exactly once either way.
   */
  closeRunDetail() {
    const dialog = this.runDetailDialog?.nativeElement;
    if (dialog?.open) {
      dialog.close();
    } else {
      this.onRunDetailClosed();
    }
  }

  /**
   * The run report's cleanup, on the dialog's close event however it closed: the header's Close,
   * Escape, or code. Stops detail polling and clears the run. Idempotent.
   */
  onRunDetailClosed(event?: Event): void {
    const dialog = this.runDetailDialog?.nativeElement;
    // A nested element's event, or a queued close that arrives after the dialog was reopened.
    if ((event && event.target !== dialog) || dialog?.open) {
      return;
    }
    this.runDetailLoadToken++;
    this.stopDetailPolling();
    this.selectedRunDetail = null;
    this.runDetailRequestedId = null;
    this.runDetailLoadError = null;
    this.loadingDetail = false;
    this.calibrations = [];
    this.calibrationErrorMessage = null;
    this.rerunPopoverOpen = false;
    this.runReportCopyStatus = '';
  }

  // --- Run report: header actions ---

  /**
   * The Re-run popover's items, each listed under the condition that makes it apply. An item that
   * applies but cannot run now stays listed with its reason.
   */
  rerunActions(run: BenchmarkRunDetailDto): RunReportRerunAction[] {
    const busy = this.isRunBusy() ? 'A retry is already running on this run.' : null;
    const aborted = isAbortedRun(run) ? 'The run stopped before finishing its suite.' : null;
    const actions: RunReportRerunAction[] = [{
      key: 'rescore',
      label: this.rescoringRun ? 'Re-scoring...' : 'Re-score run',
      reason: this.rescoringRun ? 'Re-scoring is in progress.' : (busy ?? aborted),
      run: () => this.rescoreRun(run.id)
    }];
    if (run.answers.length > 0) {
      actions.push({
        key: 'synthesis',
        label: this.runningSynthesis ? 'Synthesizing...' : 'Re-run final synthesis',
        reason: busy,
        run: () => this.openRetryDialog('synthesis', run.id)
      });
    }
    if (this.hasUnscoredAssessments()) {
      actions.push({
        key: 'assessments',
        label: this.retryingAssessments ? 'Retrying...' : 'Retry failed assessments',
        reason: busy,
        run: () => this.openRetryDialog('assessments', run.id)
      });
    }
    if (this.hasFailedClaimVerifications()) {
      actions.push({
        key: 'claim-verification',
        label: this.retryingClaimVerification ? 'Retrying...' : 'Retry claim verification',
        reason: busy,
        run: () => this.openRetryDialog('claim-verification', run.id)
      });
    }
    const status = formatStatus(run.status);
    if (this.failedAnswers().length > 0 && (status === 'Failed' || status === 'Canceled' || status === 'CompletedWithErrors')) {
      actions.push({
        key: 'failed-questions',
        label: 'Re-run failed questions',
        reason: busy ?? aborted,
        run: () => this.rerunFailedFromRunDetail(run.id)
      });
    }
    return actions;
  }

  onRerunAction(action: RunReportRerunAction): void {
    if (action.reason) {
      return;
    }
    this.closeRerunPopover();
    action.run();
  }

  /** The popover's toggle event: aria-expanded, and focus into the popover or back to the trigger. */
  onRerunToggle(event: Event): void {
    const open = (event as ToggleEvent).newState === 'open';
    this.rerunPopoverOpen = open;
    const popover = this.rerunPopover?.nativeElement;
    if (open) {
      refreshAnchorPositioning();
      popover?.querySelector<HTMLElement>('.gh-action-popover-item:not([aria-disabled="true"])')?.focus();
      return;
    }
    const active = document.activeElement;
    if (!active || active === document.body || !!popover?.contains(active)) {
      this.rerunTrigger?.nativeElement.focus();
    }
  }

  /** Escape closes the popover only; the run report dialog stays open. */
  onRerunPopoverKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape') {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.closeRerunPopover();
  }

  private closeRerunPopover(): void {
    try {
      this.rerunPopover?.nativeElement.hidePopover();
    } catch {
      // Already hidden.
    }
    this.rerunPopoverOpen = false;
    this.rerunTrigger?.nativeElement.focus();
  }

  /** The button that last opened the Download Center over the run report, for focus to return to. */
  private runDownloadsOpener: HTMLElement | null = null;

  /**
   * Opens the Download Center on the viewed run; its diagnostics are captured when a download is
   * prepared. `opener` is the button that asked, when it is not the header's Downloads.
   */
  openRunDownloads(opener?: HTMLElement): void {
    const run = this.selectedRunDetail;
    if (!run) {
      return;
    }
    this.runDownloadsOpener = opener ?? null;
    this.runDownloadCenter?.open({
      kind: 'run',
      run: {
        id: run.id,
        suiteName: run.suiteName,
        modelLabel: run.testedModelDisplayNameUsed,
        startedAtUtc: run.startedAtUtc,
        completedAtUtc: run.completedAtUtc ?? null
      },
      diagnosticsText: () => {
        const selected = this.selectedRunDetail;
        const current = selected && selected.id === run.id ? selected : run;
        return this.runDiagnosticsTextFor(current, this.runStageOf(current));
      }
    });
  }

  /**
   * Focus lost when the Download Center closed over the run report goes back to the button that
   * opened it while that button is still shown, else to the header's Downloads.
   */
  onRunDownloadsClosed(): void {
    const opener = this.runDownloadsOpener;
    this.runDownloadsOpener = null;
    const active = document.activeElement;
    if (this.runDetailDialog?.nativeElement.open && (!active || active === document.body)) {
      const target = opener?.isConnected && opener.getClientRects().length > 0
        ? opener
        : document.getElementById('rr-downloads-trigger');
      target?.focus();
    }
  }

  /** The run report dialog is open; the AI Reports tab polls only then. */
  get runDetailDialogOpen(): boolean {
    return !!this.runDetailDialog?.nativeElement?.open;
  }

  /** The AI Reports tab read new report fields: the run the dialog shows follows them. */
  onRunReportStatusChange(change: RunReportStatusChange): void {
    const run = this.selectedRunDetail;
    if (!run) {
      return;
    }
    run.reportWriterModelConfigurationId = change.writerId;
    run.reportWriterDisplayName = change.writerName;
    run.reportDocumentsStatus = change.status;
    run.reportDocumentsMessage = change.message;
  }

  /** Copies the viewed run's diagnostics, the text the Download Center saves for it. */
  async copyRunReportDiagnostics(): Promise<void> {
    const run = this.selectedRunDetail;
    if (!run) {
      return;
    }
    const copied = await copyToClipboard(this.runDiagnosticsTextFor(run, this.runStageOf(run)));
    if (this.runReportCopyTimer) {
      clearTimeout(this.runReportCopyTimer);
      this.runReportCopyTimer = null;
    }
    if (copied) {
      this.runReportCopyStatus = 'Diagnostics copied to the clipboard.';
      this.runReportCopyTimer = setTimeout(() => {
        this.runReportCopyStatus = '';
        this.runReportCopyTimer = null;
        this.viewSync.notify();
      }, COPY_STATUS_MS);
    } else {
      this.runReportCopyStatus = 'Could not copy the diagnostics to the clipboard.';
    }
    this.viewSync.notify();
  }

  // --- Run report: key-figures images ---

  /**
   * The viewed run's settings as the dialog header lists them, rebuilt only when the run object is
   * replaced so the OnPush header keeps the same rows between change-detection passes.
   */
  get selectedRunFacts(): RunFactRow[] {
    const run = this.selectedRunDetail;
    if (!run) {
      return [];
    }
    if (run !== this.runFactsSource) {
      this.runFactsSource = run;
      this.runFactsRows = buildRunFacts(run, { gaps: this.boardDeliveryGaps(run) });
    }
    return this.runFactsRows;
  }

  /** The header facts always shown: Model and Assessor(s). Split once per `selectedRunFacts` rows. */
  get selectedRunPrimaryFacts(): RunFactRow[] {
    return this.splitRunFacts().primary;
  }

  /** The header facts inside Run details: Prompt, Scoring profile, Started and Board. */
  get selectedRunDetailFacts(): RunFactRow[] {
    return this.splitRunFacts().detail;
  }

  /** Run details' one-line read-out, shown in its summary while it is closed. */
  get runDetailsReadout(): string {
    return this.splitRunFacts().readout;
  }

  /** Whether a grading role saw the run without the board; the summary then says so while closed. */
  get runHeaderBoardHasGaps(): boolean {
    return this.splitRunFacts().boardHasGaps;
  }

  /** Whether the header's Run details is open, remembered per viewer. */
  runHeaderDetailsOpen = readStoredRunHeaderDetailsOpen();

  /** Follows Run details' native toggle, so a key press and the bound `open` are tracked too, and remembers it. */
  onRunHeaderDetailsToggle(event: Event): void {
    const details = event.target as HTMLDetailsElement | null;
    if (!details || details !== event.currentTarget) {
      return;
    }
    this.runHeaderDetailsOpen = details.open;
    try {
      localStorage.setItem(RUN_REPORT_HEADER_STORAGE_KEY, JSON.stringify({ version: 1, detailsOpen: details.open }));
    } catch {
      // Storage unavailable: the choice lasts until the page reloads.
    }
  }

  /** The rows `splitRunFacts` last split, and what it made of them. */
  private runFactsSplit: {
    rows: RunFactRow[];
    primary: RunFactRow[];
    detail: RunFactRow[];
    readout: string;
    boardHasGaps: boolean;
  } | null = null;

  private splitRunFacts(): { primary: RunFactRow[]; detail: RunFactRow[]; readout: string; boardHasGaps: boolean } {
    const rows = this.selectedRunFacts;
    let split = this.runFactsSplit;
    if (!split || split.rows !== rows) {
      const primaryKeys: readonly string[] = RUN_FACT_PRIMARY_KEYS;
      const detail = rows.filter(row => !primaryKeys.includes(row.key));
      split = {
        rows,
        primary: rows.filter(row => primaryKeys.includes(row.key)),
        detail,
        readout: runFactsReadout(detail),
        boardHasGaps: detail.some(row => row.item.kind === 'board' && row.item.gaps.length > 0)
      };
      this.runFactsSplit = split;
    }
    return split;
  }

  /** What the images say about the run: the header's run facts, limited to the chosen image details. */
  keyFiguresContext(detail: BenchmarkRunDetailDto): ImageContext {
    const rows = detail === this.selectedRunDetail
      ? this.selectedRunFacts
      : buildRunFacts(detail, { gaps: this.boardDeliveryGaps(detail) });
    return {
      title: `Run #${detail.id} · ${detail.suiteName}`,
      facts: toImageFactRows(
        rows.filter(row => !this.imageDetailExclusions.includes(row.key)),
        { text: formatStatusLabel(detail.status), tone: statusImageTone(statusBadgeClass(detail.status)) }
      ),
      runId: detail.id,
      overseerVersion: this.workspace.overseerBuildVersion,
      suiteName: detail.suiteName,
      modelName: detail.testedModelDisplayNameUsed
    };
  }

  copyKeyFigures(): Promise<void> {
    return this.exportKeyFigures('copy', null);
  }

  downloadKeyFigures(): Promise<void> {
    return this.exportKeyFigures('download', null);
  }

  exportKeyFigureCard(request: KeyFigureCardExportRequest): Promise<void> {
    return this.exportKeyFigures(request.action, request.card);
  }

  /** Whether the Raw Quality Index card shows: recorded, and different from the headline index. */
  get showRawQualityTile(): boolean {
    const run = this.selectedRunDetail;
    return run?.rawQualityIndex != null && run.rawQualityIndex !== (run.qualityIndex ?? run.finalScore);
  }

  /** The keys of the key-figure cards the viewed run shows, in display order; the template's conditions. */
  get shownKeyFigureKeys(): KeyFigureKey[] {
    const run = this.selectedRunDetail;
    if (!run) {
      return [];
    }
    const keys: KeyFigureKey[] = ['intelligence'];
    if (this.showRawQualityTile) keys.push('raw-quality');
    if (this.showUnweightedQualityTile) keys.push('unweighted-mean');
    if (this.showCriticalErrorsTile) keys.push('critical-errors');
    keys.push('answered', 'speed', 'mean-time');
    if (run.isPanelRun) keys.push('panel');
    if (this.showAgreementTile) keys.push('agreement');
    keys.push('holistic', 'answer-duration', 'wall-time');
    if (run.estimatedCandidateCost != null) keys.push('model-cost');
    keys.push('estimated-cost');
    return keys;
  }

  /** Whether the Summary panel shows a key-figure card. */
  isKeyFigureShown(key: KeyFigureKey): boolean {
    return !this.keyFigureExclusions.includes(key);
  }

  /** How many of the shown key figures are selected. */
  get selectedKeyFigureCount(): number {
    return this.shownKeyFigureKeys.filter(key => !this.keyFigureExclusions.includes(key)).length;
  }

  /** `9 of 12` while the selection leaves out a figure the run shows, else null. */
  get keyFiguresSelectionLabel(): string | null {
    const total = this.shownKeyFigureKeys.length;
    const selected = this.selectedKeyFigureCount;
    return selected < total ? `${selected} of ${total}` : null;
  }

  /** Opens the chooser on the cards the Summary panel shows, with their current values. */
  openKeyFiguresChooser(opener?: HTMLElement): void {
    const root = this.runDetailDialog?.nativeElement.querySelector('.rr-figures');
    if (!this.selectedRunDetail || !root || !this.keyFiguresChooser) return;
    const figures = readKeyFigureCells(root).map(cell => ({ key: cell.key, label: cell.label, value: cell.value }));
    const details = {
      rows: this.selectedRunFacts.map(row => ({ key: row.key, label: row.label, value: runFactPlainText(row) })),
      excluded: this.imageDetailExclusions
    };
    this.keyFiguresChooser.open(figures, this.keyFigureExclusions, opener ?? null, details);
  }

  /** The chooser's live selection: filters the Summary cards and is remembered at once. */
  onKeyFigureSelectionChange(excluded: string[]): void {
    this.keyFigureExclusions = [...excluded];
    storeKeyFigureExclusions(this.keyFigureExclusions);
    this.cdr.markForCheck();
  }

  /** The chooser's image details: the run settings the next export carries, remembered at once. */
  onImageDetailSelectionChange(excluded: string[]): void {
    this.imageDetailExclusions = [...excluded];
    storeImageDetailExclusions(this.imageDetailExclusions);
    this.cdr.markForCheck();
  }

  /**
   * Composes the key figures (`card` null, the remembered selection) or one card, copies or saves
   * it, and announces the outcome. The cards are read from the Summary panel whichever tab is shown.
   */
  private async exportKeyFigures(action: KeyFiguresAction, card: HTMLElement | null): Promise<void> {
    const run = this.selectedRunDetail;
    const root = this.runDetailDialog?.nativeElement.querySelector('.rr-figures');
    if (!run || !root || this.keyFiguresExporting) return;
    this.keyFiguresExporting = true;
    this.cdr.markForCheck();
    try {
      if (this.workspace.overseerBuildVersion === null) {
        this.workspace.overseerBuildVersion = await firstValueFrom(this.systemService.getVersion()).catch(() => 'unknown');
      }
      const excluded = new Set(this.keyFigureExclusions);
      const message = await exportKeyFiguresImage(action, root, card, this.keyFiguresContext(run), key => !excluded.has(key));
      if (this.runReportCopyTimer) clearTimeout(this.runReportCopyTimer);
      this.runReportCopyStatus = message;
      this.runReportCopyTimer = setTimeout(() => {
        this.runReportCopyStatus = '';
        this.runReportCopyTimer = null;
        this.cdr.markForCheck();
      }, COPY_STATUS_MS);
    } finally {
      this.keyFiguresExporting = false;
      this.cdr.markForCheck();
    }
  }

  // --- Run report: question filters ---

  readonly questionFilterOptions: readonly { key: RunReportQuestionFilter; label: string }[] = [
    { key: 'critical', label: 'Critical errors' },
    { key: 'disputed', label: 'Disputed' },
    { key: 'disagree', label: 'Members disagree' },
    { key: 'below70', label: 'Below 70' },
    { key: 'flagged', label: 'Flagged' }
  ];

  /** The filters a run offers: Members disagree only in a panel run. */
  questionFiltersOf(run: BenchmarkRunDetailDto): readonly { key: RunReportQuestionFilter; label: string }[] {
    return run.isPanelRun
      ? this.questionFilterOptions
      : this.questionFilterOptions.filter(option => option.key !== 'disagree');
  }

  /** Below 70 reads the published score: the panel score in a panel run, else the quality score. */
  matchesQuestionFilter(run: BenchmarkRunDetailDto, ans: BenchmarkRunAnswerDto, filter: RunReportQuestionFilter): boolean {
    switch (filter) {
      case 'critical': return this.isCriticalErrorApplied(run, ans);
      case 'disputed': return !!ans.secondOpinionDisagreed;
      case 'disagree': return !!ans.panelDisagreed;
      case 'below70': {
        const score = run.isPanelRun ? ans.panelQualityScore : ans.qualityScore;
        return score != null && score < 70;
      }
      case 'flagged': return (ans.answerFlagNames?.length ?? 0) > 0;
    }
  }

  questionFilterCount(run: BenchmarkRunDetailDto, filter: RunReportQuestionFilter): number {
    return run.answers.filter(ans => this.matchesQuestionFilter(run, ans, filter)).length;
  }

  toggleQuestionFilter(filter: RunReportQuestionFilter): void {
    if (this.questionFilters.has(filter)) {
      this.questionFilters.delete(filter);
    } else {
      this.questionFilters.add(filter);
    }
  }

  /** The answers the question list shows: every answer, or those matching any pressed filter. */
  visibleAnswers(run: BenchmarkRunDetailDto): BenchmarkRunAnswerDto[] {
    if (this.questionFilters.size === 0) {
      return run.answers;
    }
    const filters = Array.from(this.questionFilters);
    return run.answers.filter(ans => filters.some(filter => this.matchesQuestionFilter(run, ans, filter)));
  }

  clearQuestionFilters(): void {
    this.questionFilters.clear();
  }

  expandAllQuestions(run: BenchmarkRunDetailDto): void {
    for (const ans of this.visibleAnswers(run)) {
      this.expandedQuestions.add(ans.orderIndex);
    }
  }

  collapseAllQuestions(): void {
    this.expandedQuestions.clear();
  }

  // --- Run report: side column ---

  /** The Run configuration list: the model and its settings, every grading role, scoring and the instrument. */
  runConfigurationRows(run: BenchmarkRunDetailDto): RunReportConfigRow[] {
    const panel = !!run.isPanelRun;
    const grader = (name: string | null | undefined, provider: string | null | undefined, modelId: string | null | undefined,
      thinking: string | null | undefined, reasoning: string | null | undefined): string => {
      const relation = this.familyRelationOf(provider, run.testedModelProviderUsed);
      const family = relation === 'same-family'
        ? 'same family as the model under test'
        : relation === 'cross-family' ? 'different family from the model under test' : 'family not recorded';
      return `${name || modelId || 'not recorded'} (${provider ?? 'n/a'} / ${modelId ?? 'n/a'}) · thinking ${thinking ?? 'default'}, `
        + `reasoning ${reasoning ?? 'default'} · ${family}`;
    };

    const rows: RunReportConfigRow[] = [
      { term: 'Model under test', value: `${run.testedModelDisplayNameUsed} (${run.testedModelProviderUsed} / ${run.testedModelIdUsed})` },
      {
        term: 'Model settings',
        value: `thinking ${run.testedModelThinkingLevelUsed ?? 'default'}, reasoning ${run.testedModelReasoningModeUsed ?? 'default'}, `
          + `service tier ${run.testedModelServiceTierUsed ?? 'default'}, max output tokens ${run.testedModelMaxOutputTokensUsed ?? 'default'}`
      }
    ];
    if (run.testedModelEndpoint) {
      rows.push({ term: 'Endpoint', value: run.testedModelEndpoint });
    }
    rows.push({
      term: panel ? 'Member A' : 'Assessor',
      value: grader(run.assessorModelDisplayNameUsed, run.assessorModelProviderUsed, run.assessorModelIdUsed,
        run.assessorModelThinkingLevelUsed, run.assessorModelReasoningModeUsed)
    });
    if (panel) {
      rows.push({
        term: 'Member B',
        value: grader(run.coAssessorModelDisplayNameUsed, run.coAssessorModelProviderUsed, run.coAssessorModelIdUsed,
          run.coAssessorModelThinkingLevelUsed, run.coAssessorModelReasoningModeUsed)
      });
    }
    const readerTerm = panel ? 'Reference reader' : 'Second reader';
    if (run.secondOpinionAssessorModelConfigurationId != null || run.secondOpinionAssessorModelDisplayNameUsed) {
      rows.push({
        term: readerTerm,
        value: grader(run.secondOpinionAssessorModelDisplayNameUsed, run.secondOpinionAssessorModelProviderUsed,
          run.secondOpinionAssessorModelIdUsed, run.secondOpinionAssessorModelThinkingLevelUsed,
          run.secondOpinionAssessorModelReasoningModeUsed)
      });
      const coverage = this.runSecondOpinionModeLabel(run);
      if (coverage) {
        rows.push({ term: 'Coverage', value: coverage });
      }
    } else {
      rows.push({ term: readerTerm, value: 'None' });
    }
    rows.push({
      term: 'Claim verifier',
      value: run.claimVerifierModelConfigurationId != null || run.claimVerifierDisplayNameUsed
        ? grader(run.claimVerifierDisplayNameUsed, run.claimVerifierProviderUsed, run.claimVerifierModelIdUsed,
          run.claimVerifierThinkingLevelUsed, run.claimVerifierReasoningModeUsed)
        : 'None'
    });
    rows.push(
      { term: 'Scoring profile', value: run.scoringProfileName ?? 'not recorded' },
      { term: 'Harness version', value: run.harnessVersion ?? '1 (unversioned legacy)' },
      { term: 'Scoring method version', value: `${run.scoringMethodVersion}` },
      { term: 'Tool call budget', value: run.maxToolCallsPerQuestionUsed != null ? `${run.maxToolCallsPerQuestionUsed} per question` : 'not recorded' },
      { term: 'Parallel questions', value: `${run.maxParallelQuestionsUsed}` },
      { term: 'Prompt', value: this.candidatePromptSummaryOf(run) ?? 'not recorded' },
      { term: 'Prompt source', value: run.candidatePromptSourceUsed ?? 'not recorded' },
      { term: 'Prompt SHA-256', value: run.candidateSystemPromptSha256 ?? 'not recorded', code: !!run.candidateSystemPromptSha256 },
      { term: 'Tool guides SHA-256', value: run.toolGuidesSha256 ?? 'not recorded', code: !!run.toolGuidesSha256 },
      { term: 'Knowledge base HEAD', value: run.knowledgeBaseHeadSha ?? 'not recorded', code: !!run.knowledgeBaseHeadSha },
      { term: 'Wiki HEAD', value: run.wikiHeadSha ?? 'not recorded', code: !!run.wikiHeadSha },
      { term: 'Source code HEAD', value: run.sourceCodeHeadSha ?? 'not recorded', code: !!run.sourceCodeHeadSha },
      { term: 'Started by', value: run.startedByUserName || 'unknown' }
    );
    return rows;
  }

  /** A Pearson r for the tool routing line, signed with a true minus; n/a where it is undefined. */
  formatCorrelation(r: number | null): string {
    if (r == null) {
      return 'n/a';
    }
    const text = Math.abs(r).toFixed(2);
    return r < 0 ? `−${text}` : text;
  }

  startDetailPolling(runId: number) {
    this.stopDetailPolling();
    this.detailPollInterval = setInterval(() => {
      this.refreshRunDetail(runId);
    }, 2000);
  }

  stopDetailPolling() {
    if (this.detailPollInterval) {
      clearInterval(this.detailPollInterval);
      this.detailPollInterval = null;
    }
  }

  refreshRunDetail(runId: number) {
    this.benchmarkService.getRun(runId).subscribe({
      next: (data) => {
        this.selectedRunDetail = data;
        const statusStr = formatStatus(data.status);
        if (statusStr !== 'Running') {
          this.stopDetailPolling();
          this.reassessingAnswerId = null;
          this.rerunningAnswerId = null;
          this.runningSynthesis = false;
          this.retryingAssessments = false;
          this.retryingClaimVerification = false;
          this.workspace.loadHistory();
        }
        this.viewSync.notify();
      },
      error: (err) => {
        console.error('Failed to refresh run details', err);
        this.stopDetailPolling();
        this.reassessingAnswerId = null;
        this.rerunningAnswerId = null;
        this.runningSynthesis = false;
        this.retryingAssessments = false;
        this.retryingClaimVerification = false;
        this.viewSync.notify();
      }
    });
  }

  cancelRunById(runId: number) {
    this.monitor.noteOperatorCancel(runId);
    this.benchmarkService.cancelRun(runId).subscribe({
      next: () => {
        this.refreshRunDetail(runId);
      },
      error: (err) => {
        this.monitor.operatorCancelledRunIds.delete(runId);
        console.error('Failed to cancel run', err);
        this.refreshRunDetail(runId);
      }
    });
  }

  openRetryDialog(scope: 'assessment' | 'trial' | 'question' | 'synthesis' | 'assessments' | 'claim-verification', runId: number, answer?: BenchmarkRunAnswerDto) {
    this.retryScope = scope;
    this.retryRunId = runId;
    this.retryAnswer = answer ?? null;
    this.retryAssessorConfigId = scope === 'claim-verification'
      ? this.resolveRetryClaimVerifier()
      : this.resolveRetryAssessor();
    this.retryPanelMember = 'Both';
    this.retryDialog?.nativeElement.showModal();
    this.viewSync.notify();
  }

  closeRetryDialog() {
    this.retryScope = null;
    this.retryRunId = null;
    this.retryAnswer = null;
    this.retryAssessorConfigId = null;
    this.retryDialog?.nativeElement.close();
    this.viewSync.notify();
  }

  /**
   * A panel run's grader actions re-grade with the run's own members: the server refuses an
   * assessor override there, since a panel graded partly by a substitute model is a mixed
   * instrument. The trial and the claim-verification retry keep their picker; neither scores.
   */
  get retryUsesPanelMembers(): boolean {
    return !!this.selectedRunDetail?.isPanelRun &&
      (this.retryScope === 'assessment' || this.retryScope === 'question' ||
        this.retryScope === 'synthesis' || this.retryScope === 'assessments');
  }

  private resolveRetryAssessor(): number | null {
    const runAssessorId = this.selectedRunDetail?.assessorModelConfigurationId;
    if (runAssessorId != null && this.workspace.benchmarkCapableConfigs.some(c => c.id === runAssessorId)) {
      return runAssessorId;
    }
    return this.workspace.benchmarkCapableConfigs[0]?.id ?? null;
  }

  private resolveRetryClaimVerifier(): number | null {
    const runVerifierId = this.selectedRunDetail?.claimVerifierModelConfigurationId;
    if (runVerifierId != null && this.workspace.benchmarkCapableConfigs.some(c => c.id === runVerifierId)) {
      return runVerifierId;
    }
    return this.selectedRunDetail?.assessorModelConfigurationId ?? this.workspace.benchmarkCapableConfigs[0]?.id ?? null;
  }

  confirmRetry() {
    if (!this.retryRunId || !this.retryScope) return;

    const runId = this.retryRunId;
    const scope = this.retryScope;
    const panelMembers = this.retryUsesPanelMembers;
    // No override is ever sent for a panel run's grader actions; each member re-grades on its own.
    const assessorId = panelMembers ? null : this.retryAssessorConfigId;
    const member = this.retryPanelMember;
    const answer = this.retryAnswer;
    const isPanelRun = !!this.selectedRunDetail?.isPanelRun;

    this.closeRetryDialog();

    if (scope === 'assessment') {
      if (!answer) return;
      this.workspace.actionErrorMessage = null;
      this.reassessingAnswerId = answer.id;
      const request = panelMembers
        ? this.benchmarkService.reassessPanelAnswer(runId, answer.id, member)
        : this.benchmarkService.reassessAnswer(runId, answer.id, assessorId);
      request.subscribe({
        next: () => {
          this.startDetailPolling(runId);
        },
        error: (err) => {
          this.reassessingAnswerId = null;
          this.workspace.actionErrorMessage = err?.error || 'Failed to start reassessment.';
          this.viewSync.notify();
        }
      });
    } else if (scope === 'trial') {
      if (!answer) return;
      this.workspace.actionErrorMessage = null;
      this.trialReassessingAnswerId = answer.id;
      // Overwriting an existing automatic second opinion is refused server-side unless asked
      // for: that verdict is run evidence, and an experiment must not erase it by accident. The
      // operator confirms the replacement here before the call, not after the refusal. In a panel
      // run it is the reference reader's verdict, which is never replaced.
      const replaceExisting = !isPanelRun &&
        answer.secondOpinionQualityScore != null &&
        answer.secondOpinionTrigger !== 'Manual';
      this.benchmarkService
        .trialReassessAnswer(runId, answer.id, assessorId, replaceExisting)
        .subscribe({
          next: () => {
            this.startDetailPolling(runId);
          },
          error: (err) => {
            this.trialReassessingAnswerId = null;
            this.workspace.actionErrorMessage = err?.error || 'Failed to start the trial assessment.';
            this.viewSync.notify();
          }
        });
    } else if (scope === 'question') {
      if (!answer) return;
      this.workspace.actionErrorMessage = null;
      this.rerunningAnswerId = answer.id;
      this.benchmarkService.rerunAnswer(runId, answer.id, assessorId).subscribe({
        next: () => {
          this.startDetailPolling(runId);
        },
        error: (err) => {
          this.rerunningAnswerId = null;
          this.workspace.actionErrorMessage = err?.error || 'Failed to start rerun.';
          this.viewSync.notify();
        }
      });
    } else if (scope === 'synthesis') {
      this.workspace.actionErrorMessage = null;
      this.runningSynthesis = true;
      this.benchmarkService.rerunFinalSynthesis(runId, assessorId).subscribe({
        next: () => {
          this.startDetailPolling(runId);
        },
        error: (err) => {
          this.runningSynthesis = false;
          this.workspace.actionErrorMessage = err?.error || 'Failed to start final synthesis.';
          this.viewSync.notify();
        }
      });
    } else if (scope === 'assessments') {
      this.workspace.actionErrorMessage = null;
      this.retryingAssessments = true;
      this.benchmarkService.retryFailedAssessments(runId, assessorId).subscribe({
        next: () => {
          this.startDetailPolling(runId);
        },
        error: (err) => {
          this.retryingAssessments = false;
          this.workspace.actionErrorMessage = err?.error || 'Failed to retry failed assessments.';
          this.viewSync.notify();
        }
      });
    } else if (scope === 'claim-verification') {
      this.workspace.actionErrorMessage = null;
      this.retryingClaimVerification = true;
      this.benchmarkService.retryClaimVerification(runId, assessorId).subscribe({
        next: () => {
          this.startDetailPolling(runId);
        },
        error: (err) => {
          this.retryingClaimVerification = false;
          this.workspace.actionErrorMessage = err?.error || 'Failed to retry claim verification.';
          this.viewSync.notify();
        }
      });
    }
  }

  rescoreRun(runId: number) {
    this.workspace.actionErrorMessage = null;
    this.rescoringRun = true;
    this.benchmarkService.rescoreRun(runId, this.launcher.selectedScoringProfileId).subscribe({
      next: () => {
        this.rescoringRun = false;
        this.viewRunDetail(runId);
        this.workspace.loadHistory();
      },
      error: (err) => {
        this.rescoringRun = false;
        this.workspace.actionErrorMessage = err?.error || 'Failed to rescore run.';
        this.viewSync.notify();
      }
    });
  }

  reassessAnswer(runId: number, answerId: number) {
    this.workspace.actionErrorMessage = null;
    this.reassessingAnswerId = answerId;
    this.benchmarkService.reassessAnswer(runId, answerId, this.launcher.assessorConfigId).subscribe({
      next: () => {
        this.reassessingAnswerId = null;
        this.viewRunDetail(runId);
        this.workspace.loadHistory();
      },
      error: (err) => {
        this.reassessingAnswerId = null;
        this.workspace.actionErrorMessage = err?.error || 'Failed to reassess answer.';
        this.viewSync.notify();
      }
    });
  }

  toggleQuestion(orderIndex: number) {
    if (this.expandedQuestions.has(orderIndex)) {
      this.expandedQuestions.delete(orderIndex);
    } else {
      this.expandedQuestions.add(orderIndex);
    }
  }

  toggleThought(orderIndex: number) {
    if (this.expandedThoughts.has(orderIndex)) {
      this.expandedThoughts.delete(orderIndex);
    } else {
      this.expandedThoughts.add(orderIndex);
    }
  }

  toggleArtifact(orderIndex: number) {
    if (this.expandedArtifacts.has(orderIndex)) {
      this.expandedArtifacts.delete(orderIndex);
    } else {
      this.expandedArtifacts.add(orderIndex);
    }
  }

  /**
   * Collapses immediately when already expanded. On first expand, fetches the answer's full
   * per-call tool record and caches it under `orderIndex` — the card and disclosure state's
   * key — even though the request itself needs `ans.id`, the answer's database id. A cached
   * key (including one holding an empty array) means "already fetched", so a second expand
   * never refetches.
   */
  toggleToolCalls(ans: BenchmarkRunAnswerDto): void {
    const orderIndex = ans.orderIndex;
    if (this.expandedToolCalls.has(orderIndex)) {
      this.expandedToolCalls.delete(orderIndex);
      return;
    }
    this.expandedToolCalls.add(orderIndex);

    if (this.toolCallsByAnswer.has(orderIndex) || this.loadingToolCalls.has(orderIndex)) {
      return;
    }
    const runId = this.selectedRunDetail?.id;
    if (runId == null) return;

    this.loadingToolCalls.add(orderIndex);
    this.toolCallsErrorByAnswer.delete(orderIndex);
    this.benchmarkService.getAnswerToolCalls(runId, ans.id).subscribe({
      next: (rows) => {
        this.toolCallsByAnswer.set(orderIndex, rows);
        this.loadingToolCalls.delete(orderIndex);
        this.viewSync.notify();
      },
      error: (err) => {
        this.loadingToolCalls.delete(orderIndex);
        this.toolCallsErrorByAnswer.set(orderIndex, err?.error || 'Failed to load tool calls.');
        this.viewSync.notify();
      }
    });
  }

  /** Whether an individual call's arguments or result panel is open. */
  isToolCallFieldExpanded(orderIndex: number, callId: number, field: 'args' | 'result'): boolean {
    return this.expandedToolCallFields.has(`${orderIndex}:${callId}:${field}`);
  }

  toggleToolCallField(orderIndex: number, callId: number, field: 'args' | 'result'): void {
    const key = `${orderIndex}:${callId}:${field}`;
    if (this.expandedToolCallFields.has(key)) {
      this.expandedToolCallFields.delete(key);
    } else {
      this.expandedToolCallFields.add(key);
    }
  }

  /** Whether the answer has anything for the tool-call disclosure to show at all. */
  hasToolCallData(ans: BenchmarkRunAnswerDto): boolean {
    return this.hasRecordedToolCallCounts(ans) || !!ans.toolCallSummary;
  }

  /** Null on every one of the three counts means the answer predates the per-call record. */
  hasRecordedToolCallCounts(ans: BenchmarkRunAnswerDto): boolean {
    return ans.toolCallsSucceeded != null || ans.toolCallsFailed != null || ans.toolCallsRefused != null;
  }

  /**
   * `"N succeeded, N failed, N refused"`, including only the counts that are actually
   * recorded. Null for a legacy answer, never a string naming a fabricated zero.
   */
  toolCallCountsLabel(ans: BenchmarkRunAnswerDto): string | null {
    const parts: string[] = [];
    if (ans.toolCallsSucceeded != null) parts.push(`${ans.toolCallsSucceeded} succeeded`);
    if (ans.toolCallsFailed != null) parts.push(`${ans.toolCallsFailed} failed`);
    if (ans.toolCallsRefused != null) parts.push(`${ans.toolCallsRefused} refused`);
    return parts.length > 0 ? parts.join(', ') : null;
  }

  /** `'completed'` (case-insensitive) is the only status the record calls a success. */
  isToolCallSucceeded(call: BenchmarkToolCallDto): boolean {
    return (call.status ?? '').toLowerCase() === 'completed';
  }

  /**
   * Distinguishes a call that genuinely returned nothing from one whose result was pruned by
   * age. `result` is only ever null for one of those two reasons, and `resultLengthChars`
   * survives the prune, so it is what tells them apart.
   */
  toolCallResultState(call: BenchmarkToolCallDto): 'value' | 'pruned' | 'empty' {
    if (call.result != null) return 'value';
    return (call.resultLengthChars ?? 0) > 0 ? 'pruned' : 'empty';
  }

  // --- Predicates ---

  isAnswerFailed(ans: BenchmarkRunAnswerDto): boolean {
    const s = this.formatAnswerStatus(ans.status);
    return s === 'ProviderError' || s === 'Failed' || s === 'Skipped' || s === 'EmptyAnswer' || s === 'Canceled';
  }

  /**
   * Provider finish reasons that mean "the model chose to stop here". Mirrors
   * `BenchmarkRunFinalizer.NormalStopReasons`; keep the two in step.
   */
  private static readonly NORMAL_STOP_REASONS = ['stop', 'end_turn', 'completed', 'complete', 'stop_sequence'];

  /**
   * Whether this answer contributes to the quality indices — `BenchmarkRunFinalizer
   * .CountsTowardQualityIndex`, mirrored so a diagnostics capture and the run report cannot
   * print different denominators for one run. An Ok answer counts; so does one the model
   * finished normally and left empty, which scoring method 10 scores 0 rather than excusing.
   * An unrecognised or absent finish reason is not evidence of a normal stop.
   */
  countsTowardQualityIndex(ans: BenchmarkRunAnswerDto): boolean {
    const s = this.formatAnswerStatus(ans.status);
    if (s === 'Ok') return true;
    if (s !== 'EmptyAnswer') return false;
    const reason = (ans.providerFinishReason ?? '').trim().toLowerCase();
    return reason.length > 0 && AdminBenchmarkComponent.NORMAL_STOP_REASONS.includes(reason);
  }

  get runGradeableAnswerCount(): number {
    return (this.monitor.activeRunDetail?.answers ?? []).filter(a => this.countsTowardQualityIndex(a)).length;
  }

  /**
   * A transport or provider defect: the answer text that reached the assessor is not what
   * the model meant to produce, so the run's validity is compromised. This is the only
   * category that should read as a failure.
   */
  hasTransportDefect(ans: BenchmarkRunAnswerDto): boolean {
    const s = this.formatAnswerStatus(ans.status);
    if (s === 'EmptyAnswer') return true;
    const flags = ans.answerFlags ?? 0;
    return (flags & AdminBenchmarkComponent.TRANSPORT_DEFECT_FLAGS) !== 0;
  }

  /**
   * An operator-configured cap working as designed. The answer is valid; the cap may simply
   * need raising, so this must never be presented as a defect.
   */
  hasHarnessLimit(ans: BenchmarkRunAnswerDto): boolean {
    return !!ans.toolBudgetExhausted;
  }

  /**
   * Advisory only: reasoning bleed and repeated fragments describe what the harness noticed
   * and cleaned up, not a broken answer. Advisory flags may overlap the two categories above
   * and never remove an answer from the clean count.
   */
  hasAdvisoryFlag(ans: BenchmarkRunAnswerDto): boolean {
    const flags = ans.answerFlags ?? 0;
    return (flags & AdminBenchmarkComponent.ADVISORY_FLAGS) !== 0;
  }

  /** Whether one entry of answerFlagNames is an advisory flag rather than a defect. */
  isAdvisoryFlagName(flag: string): boolean {
    return AdminBenchmarkComponent.ADVISORY_FLAG_NAMES.includes(flag);
  }

  /** The text a flag badge shows: the flag's name, except where a short reading is clearer. */
  flagBadgeLabel(flag: string): string {
    return flag === 'ContestedAccuracyDeduction' ? 'contested deduction' : flag === 'DimensionOutlier' ? 'dimension outlier' : flag;
  }

  // --- Difficulty bands ---
  //
  // Must stay in step with BenchmarkDifficultyBands on the server. These are the boundaries the
  // difficulty assessor is told to rate against; the report previously bucketed at 33/66 while
  // the assessor was told 35/70, so a question rated 35 as Simple was reported as Intermediate.
  private static readonly BAND_SIMPLE_MAX = 35;

  private static readonly BAND_INTERMEDIATE_MAX = 70;

  bandOfDifficulty(difficulty: number): string {
    if (difficulty <= AdminBenchmarkComponent.BAND_SIMPLE_MAX) return 'Simple';
    if (difficulty <= AdminBenchmarkComponent.BAND_INTERMEDIATE_MAX) return 'Intermediate';
    return 'Advanced';
  }

  /**
   * The assessed band, but only when it disagrees with the authored one. Returns null on
   * agreement so the template can render the shift and nothing otherwise.
   */
  assessedBandOf(ans: BenchmarkRunAnswerDto): string | null {
    if (ans.assessedDifficulty == null) return null;

    const assessed = this.bandOfDifficulty(ans.assessedDifficulty);
    return assessed === this.formatDifficulty(ans.difficulty) ? null : assessed;
  }

  /** Answers whose assessed band differs from the band they were authored in. */
  bandDisagreements(): BenchmarkRunAnswerDto[] {
    return (this.selectedRunDetail?.answers ?? []).filter(a => this.assessedBandOf(a) !== null);
  }

  /**
   * The authored band's own midpoint, which a mismatch's signed delta is measured against. Mirrors
   * BenchmarkRunFinalizer.FallbackDifficulty on the server.
   */
  private authoredBandMidpoint(difficulty: string | number): number {
    switch (this.formatDifficulty(difficulty)) {
      case 'Simple': return 25;
      case 'Intermediate': return 55;
      case 'Advanced': return 85;
      default: return 50;
    }
  }

  /**
   * Signed band drift over the mismatches: how many were assessed harder than authored, how many
   * easier, and the mean signed delta against the authored midpoint. A list of per-question shifts
   * hides the case the operator has to see — every mismatch moving the same way — and assessed
   * difficulty is the Intelligence Index weight, so a one-directional drift moves the headline.
   */
  bandDriftSummary(): { up: number; down: number; meanDelta: number; oneDirection: 'up' | 'down' | null } | null {
    const mismatches = this.bandDisagreements();
    if (mismatches.length === 0) return null;

    let up = 0;
    let down = 0;
    let deltaSum = 0;
    for (const ans of mismatches) {
      const delta = (ans.assessedDifficulty ?? 0) - this.authoredBandMidpoint(ans.difficulty);
      deltaSum += delta;
      if (delta > 0) up++;
      else if (delta < 0) down++;
    }

    return {
      up,
      down,
      meanDelta: deltaSum / mismatches.length,
      oneDirection: up > 0 && down === 0 ? 'up' : down > 0 && up === 0 ? 'down' : null
    };
  }

  // --- Tool usage profile ---

  /**
   * Successful tool calls per tool across the run, aggregated from each answer's
   * toolCallSummary. The server reports the same tally in the Markdown report; there is no
   * run-level per-tool field on the DTO, so the client re-derives it from the same source
   * rather than adding one.
   *
   * Summary entries look like `wiki_search×11`. A trailing `(n blocked by budget)` note is
   * parenthesised and carries no tool name, so it is skipped.
   */
  toolUsageProfile(): { name: string; count: number }[] {
    const counts = new Map<string, number>();

    for (const ans of this.selectedRunDetail?.answers ?? []) {
      for (const [name, count] of this.parseToolCallSummary(ans.toolCallSummary)) {
        counts.set(name, (counts.get(name) ?? 0) + count);
      }
    }

    return Array.from(counts, ([name, count]) => ({ name, count }))
      .sort((a, b) => b.count - a.count || a.name.localeCompare(b.name));
  }

  /**
   * One answer's `toolCallSummary` as tool name to call count. Extracted so the usage table and the
   * routing families below parse the summary in exactly one place — mirroring
   * `BenchmarkChatTransfer.ParseToolCallCounts`, which the report builder uses for the same reason.
   */
  private parseToolCallSummary(summary: string | null | undefined): Map<string, number> {
    const counts = new Map<string, number>();
    if (!summary) return counts;

    for (const entry of summary.split(',')) {
      const trimmed = entry.trim();
      const sep = trimmed.indexOf('×');
      if (sep <= 0 || trimmed.startsWith('(')) continue;

      const name = trimmed.substring(0, sep).trim();
      const digits = trimmed.substring(sep + 1).match(/^\d+/);
      if (!name || !digits) continue;

      counts.set(name, (counts.get(name) ?? 0) + parseInt(digits[0], 10));
    }
    return counts;
  }

  totalToolCalls(): number {
    return (this.selectedRunDetail?.answers ?? []).reduce((sum, a) => sum + (a.toolCallCount ?? 0), 0);
  }

  meanToolCallsPerQuestion(): number {
    const answers = this.selectedRunDetail?.answers ?? [];
    return answers.length === 0 ? 0 : this.totalToolCalls() / answers.length;
  }

  /**
   * `"N succeeded, N failed, N refused by budget"` summed over the run's answers — the run-level
   * counterpart of the per-answer `toolCallCountsLabel`. Null unless *every* answer that made a
   * tool call has recorded outcomes: a partial sum would read as a run-wide figure while silently
   * omitting the answers whose outcomes predate the per-call record, and the difference between
   * that and zero failures is the whole point of the line.
   */
  toolCallOutcomeSummary(): string | null {
    const answers = (this.selectedRunDetail?.answers ?? []).filter(a => (a.toolCallCount ?? 0) > 0);
    if (answers.length === 0 || !answers.every(a => this.hasRecordedToolCallCounts(a))) {
      return null;
    }
    const sum = (pick: (a: BenchmarkRunAnswerDto) => number | null | undefined): number =>
      answers.reduce((total, a) => total + (pick(a) ?? 0), 0);
    return `${sum(a => a.toolCallsSucceeded)} succeeded, ${sum(a => a.toolCallsFailed)} failed, `
      + `${sum(a => a.toolCallsRefused)} refused by budget`;
  }

  /**
   * Mean tool rounds per answer with tool calls, and mean calls per round, over the per-call rows
   * this dialog has actually loaded. The rows arrive per answer as the operator expands each
   * disclosure, so this reads whatever is loaded and names how many answers it covers; null while
   * none are.
   */
  toolRoundsSummary(): string | null {
    const loaded = (this.selectedRunDetail?.answers ?? [])
      .map(a => this.toolCallsByAnswer.get(a.orderIndex))
      .filter((rows): rows is BenchmarkToolCallDto[] => !!rows && rows.length > 0);
    if (loaded.length === 0) {
      return null;
    }
    const rounds = loaded.map(rows => new Set(
      rows.filter(r => r.iterationIndex != null).map(r => r.iterationIndex)).size);
    const totalRounds = rounds.reduce((sum, r) => sum + r, 0);
    if (totalRounds === 0) {
      return null;
    }
    const totalCalls = loaded.reduce((sum, rows) => sum + rows.length, 0);
    const meanRounds = totalRounds / loaded.length;
    return `${meanRounds.toFixed(1)} tool round(s) per answer on average, `
      + `${(totalCalls / totalRounds).toFixed(1)} call(s) per round, `
      + `over ${loaded.length} answer(s) loaded`;
  }

  /** Answers that reached their tool call budget, for the tool usage panel. */
  budgetExhaustedAnswers(): BenchmarkRunAnswerDto[] {
    return (this.selectedRunDetail?.answers ?? []).filter(a => !!a.toolBudgetExhausted);
  }

  // --- U1: the four report lines this screen used to omit ---
  //
  // Every figure below is computed from answer DTOs already loaded for the run detail dialog, so no
  // endpoint was added for any of it. They exist because the Markdown report carried four lines the
  // page did not, and on run 14 those four lines are where both accuracy defects and the lowest
  // Intermediate answer live: an operator reading only this screen saw none of it.

  /**
   * Mirrors `BenchmarkReportBuilder.BudgetPressureFraction`. A question that stopped one call short
   * of its cap is not "exhausted" and is flagged nowhere, yet it may have been cut off
   * mid-investigation — an outcome indistinguishable from a model choosing to stop.
   */
  private static readonly BUDGET_PRESSURE_FRACTION = 0.90;

  /** Answers the run actually produced, which is what every routing figure is computed over. */
  private get answeredRunAnswers(): BenchmarkRunAnswerDto[] {
    return (this.selectedRunDetail?.answers ?? []).filter(a => this.formatAnswerStatus(a.status) === 'Ok');
  }

  /**
   * Tool calls by functional family, with each family's share of the run.
   *
   * Membership comes from {@link BENCHMARK_TOOL_FAMILY_MEMBERSHIP}, the same single list the
   * classification is written in once — not from tool names spelled out here, which would drift
   * from the report's table the first time a tool was added on one side only. Families with no
   * calls are omitted, exactly as the report's Tool Routing table omits them.
   */
  toolRoutingFamilies(): BenchmarkToolFamilyRow[] {
    const counts = new Map<BenchmarkToolFamilyName, number>();
    for (const [family] of BENCHMARK_TOOL_FAMILY_LABELS) {
      counts.set(family, 0);
    }

    let total = 0;
    for (const ans of this.answeredRunAnswers) {
      for (const [tool, count] of this.parseToolCallSummary(ans.toolCallSummary)) {
        const family = classifyBenchmarkTool(tool);
        counts.set(family, (counts.get(family) ?? 0) + count);
        total += count;
      }
    }

    return BENCHMARK_TOOL_FAMILY_LABELS
      .map(([family, label]) => ({
        family,
        label,
        count: counts.get(family) ?? 0,
        sharePercentage: total > 0 ? ((counts.get(family) ?? 0) * 100) / total : 0
      }))
      .filter(row => row.count > 0);
  }

  /** The denominator behind the family shares, so a reader can check the arithmetic. */
  totalRoutedToolCalls(): number {
    return this.toolRoutingFamilies().reduce((sum, row) => sum + row.count, 0);
  }

  /**
   * Answers that used at least 90 % of their tool call budget **without** reaching it.
   *
   * Rendered in addition to the exhausted list rather than instead of it: the two describe different
   * outcomes, and on run 14 the exhausted list was empty while Q11 (41/45, scored 81) and Q16
   * (43/45) sat just under the cap — so the page showed nothing at all about the run's two most
   * budget-constrained questions. The filter mirrors the report builder's exactly, including the
   * exclusions: an answer with blocked calls is exhausted, not pressured.
   */
  budgetPressuredAnswers(): BenchmarkRunAnswerDto[] {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => !a.toolBudgetExhausted
        && this.blockedToolCallsOf(a) === 0
        && a.toolCallBudgetUsed != null && a.toolCallBudgetUsed > 0
        && a.toolCallCount != null
        && a.toolCallCount >= a.toolCallBudgetUsed * AdminBenchmarkComponent.BUDGET_PRESSURE_FRACTION
        && a.toolCallCount < a.toolCallBudgetUsed)
      .sort((a, b) => a.orderIndex - b.orderIndex);
  }

  /**
   * Advanced-band answers produced with one tool call or fewer.
   *
   * Answering from memory is not necessarily wrong, but such a question is no longer testing source
   * retrieval — which is what the Advanced band exists for. This is a signal about the suite, not
   * about the model, and it is why it is reported rather than penalised.
   */
  ungroundedAdvancedAnswers(): BenchmarkRunAnswerDto[] {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => this.bandOfDifficulty(this.assessedDifficultyOf(a)) === 'Advanced'
        && (a.toolCallCount ?? 0) <= 1)
      .sort((a, b) => a.orderIndex - b.orderIndex);
  }

  /**
   * The assessed difficulty an answer is banded by, falling back to the authored band's midpoint
   * when the assessor never rated it. The fallbacks are `BenchmarkRunFinalizer.FallbackDifficulty`'s
   * — 25 / 55 / 85 — so this screen bands an unrated answer exactly where the report bands it.
   */
  private assessedDifficultyOf(ans: BenchmarkRunAnswerDto): number {
    if (ans.assessedDifficulty != null) return ans.assessedDifficulty;
    switch (this.formatDifficulty(ans.difficulty)) {
      case 'Simple': return 25;
      case 'Intermediate': return 55;
      case 'Advanced': return 85;
      default: return 50;
    }
  }

  /**
   * Pearson *r* of each answer's source-family share against its model time and against its quality,
   * with the sample size that produced them.
   *
   * The pairing is the finding: on run 14 more source calls bought time (*r* = 0.86) and not
   * accuracy (*r* = −0.05). Either coefficient alone would be a different, weaker claim, so both are
   * computed over one sample and the *n* is rendered beside them.
   *
   * Scope matches `BenchmarkChatTransfer.AnalyzeToolRouting`: answered answers carrying a quality
   * score. An unscored answer has no y value for one of the two correlations, and dropping it from
   * one but not the other would compute the pair over two different samples.
   */
  sourceShareCorrelations(): BenchmarkSourceShareCorrelations {
    const shares: number[] = [];
    const modelTimes: number[] = [];
    const qualities: number[] = [];

    for (const ans of this.answeredRunAnswers) {
      if (ans.qualityScore == null) continue;

      const counts = this.parseToolCallSummary(ans.toolCallSummary);
      let total = 0;
      let source = 0;
      for (const [tool, count] of counts) {
        total += count;
        if (classifyBenchmarkTool(tool) === 'SourceCode') source += count;
      }

      shares.push(total > 0 ? source / total : 0);
      modelTimes.push(this.modelTimeOf(ans));
      qualities.push(ans.qualityScore);
    }

    return {
      modelTimeR: this.pearson(shares, modelTimes),
      qualityR: this.pearson(shares, qualities),
      sampleSize: shares.length
    };
  }

  /**
   * Turn duration with tool I/O removed — what speed is scored on, and the right x-axis for "did
   * source calls cost time?". `modelTimeMs` is the recorded figure; the subtraction is the fallback
   * for a run recorded before it existed, where leaving the answer out would silently shrink the
   * sample rather than reporting it.
   */
  private modelTimeOf(ans: BenchmarkRunAnswerDto): number {
    if (ans.modelTimeMs > 0) return ans.modelTimeMs;
    return Math.max(0, (ans.durationMs ?? 0) - (ans.toolTimeMs ?? 0));
  }

  /** Null on fewer than two points or on a constant vector, where *r* is undefined rather than 0. */
  private pearson(xs: number[], ys: number[]): number | null {
    const n = Math.min(xs.length, ys.length);
    if (n < 2) return null;

    let mx = 0;
    let my = 0;
    for (let i = 0; i < n; i++) { mx += xs[i]; my += ys[i]; }
    mx /= n;
    my /= n;

    let sxy = 0;
    let sxx = 0;
    let syy = 0;
    for (let i = 0; i < n; i++) {
      const dx = xs[i] - mx;
      const dy = ys[i] - my;
      sxy += dx * dy;
      sxx += dx * dx;
      syy += dy * dy;
    }
    if (sxx <= 0 || syy <= 0) return null;
    return sxy / Math.sqrt(sxx * syy);
  }

  // --- U2: stable anchors for in-page question references ---

  /**
   * The DOM id of an answer's card. Stable across renders because it is derived from the order
   * index the whole screen already labels answers by, so a link written into the Run Integrity
   * Notice keeps working when the dialog is reopened.
   *
   * New ids only: nothing that already carried an id was renamed, because an existing id may be the
   * target of a link or a test somewhere this change cannot see.
   */
  answerAnchorId(orderIndex: number): string {
    return `bm-answer-${orderIndex}`;
  }

  /**
   * Scrolls to an answer and expands it, so a question reference lands on the answer's *content*
   * rather than on a collapsed header the reader then has to find and open.
   *
   * The default is prevented because this is in-page movement inside a modal dialog: letting the
   * fragment reach the router would navigate the application away from the run being read.
   */
  jumpToAnswer(orderIndex: number, event?: Event): void {
    event?.preventDefault();
    if (this.runReportTab !== 'questions') {
      this.selectRunReportTab('questions');
    }
    this.expandedQuestions.add(orderIndex);
    this.viewSync.notify();
    if (typeof document === 'undefined') return;
    const target = document.getElementById(this.answerAnchorId(orderIndex));
    target?.scrollIntoView({ block: 'start', behavior: 'smooth' });
    target?.focus?.();
  }

  // The Run Integrity Notice's question references as numbers rather than as a joined string, so
  // each one can be rendered as its own link. The joined getters stay: they are what the
  // diagnostics capture and the plain-text clauses use, and one of the two shapes had to remain.

  get criticalErrorQuestionIndexes(): number[] {
    return this.keyFigureCriticalErrorAnswers.map(a => a.orderIndex);
  }

  isAssessmentFailed(ans: BenchmarkRunAnswerDto): boolean {
    return this.formatAssessmentStatus(ans.assessmentStatus) === 'Failed';
  }

  isAssessmentIncomplete(ans: BenchmarkRunAnswerDto): boolean {
    const s = this.formatAssessmentStatus(ans.assessmentStatus);
    return s === 'Pending' || s === 'Assessing';
  }

  hasUnscoredAssessments(): boolean {
    return (this.selectedRunDetail?.answers ?? []).some(ans =>
      this.isAssessmentFailed(ans) || this.isAssessmentIncomplete(ans) || this.isCoAssessmentUnscored(ans));
  }

  /** In a panel run, member B's verdict is not Scored; the server retries it with the failed assessments. */
  isCoAssessmentUnscored(ans: BenchmarkRunAnswerDto): boolean {
    return !!this.selectedRunDetail?.isPanelRun &&
      ans.coAssessmentStatus != null &&
      this.formatAssessmentStatus(ans.coAssessmentStatus) !== 'Scored';
  }

  hasFailedClaimVerifications(): boolean {
    return (this.selectedRunDetail?.answers ?? []).some(
      ans => !!ans.claimVerificationError && ans.claimVerificationError.trim().length > 0
    );
  }

  isRunBusy(): boolean {
    return formatStatus(this.selectedRunDetail?.status ?? '') === 'Running';
  }

  wasAssessedByOther(ans: BenchmarkRunAnswerDto): boolean {
    return ans.assessedByModelConfigurationId != null &&
      ans.assessedByModelConfigurationId !== this.selectedRunDetail?.assessorModelConfigurationId;
  }

  /** Answers where a second assessor reached a materially different verdict. */
  get disputedAnswerCount(): number {
    return (this.selectedRunDetail?.answers ?? []).filter(a => a.secondOpinionDisagreed).length;
  }

  /**
   * Answers the assessor flagged with a critical error, and therefore capped at the scoring
   * profile's critical error ceiling. This is the failure mode the report tells readers to look
   * for, and it is deliberately reported as a count rather than left to the Intelligence Index:
   * the index weights by difficulty, so a critical error on an easy question barely moves it.
   * In a panel run a flag from either member counts, as in the report.
   */
  get criticalErrorAnswerCount(): number {
    return this.keyFigureCriticalErrorAnswers.length;
  }

  /** Of the critical-error answers, how many member A flagged. */
  get criticalErrorMemberACount(): number {
    return this.keyFigureCriticalErrorAnswers.filter(a => a.criticalError).length;
  }

  /** Of the critical-error answers, how many member B flagged; 0 outside a panel run. */
  get criticalErrorMemberBCount(): number {
    return this.keyFigureCriticalErrorAnswers.filter(a => a.coAssessmentCriticalError === true).length;
  }

  /** Whether the Critical Errors card shows: the run has answer rows to count over. */
  get showCriticalErrorsTile(): boolean {
    return (this.selectedRunDetail?.answers?.length ?? 0) > 0;
  }

  /**
   * The Critical Errors card's answers, by the report's rule: member A's flag, or in a panel run
   * either member's, since a cap from either lowers the panel score. In question order.
   */
  get keyFigureCriticalErrorAnswers(): BenchmarkRunAnswerDto[] {
    return this.criticalErrorAnswersOf(this.selectedRunDetail);
  }

  /**
   * The answers a critical-error cap applied to, by the report's rule: member A's flag, or in a
   * panel run either member's, since a cap from either lowers the panel score. From scoring method
   * 13, the confirmed critical errors. In question order.
   */
  private criticalErrorAnswersOf(run: BenchmarkRunDetailDto | null | undefined): BenchmarkRunAnswerDto[] {
    if (!run) return [];
    return (run.answers ?? [])
      .filter(a => this.isCriticalErrorApplied(run, a))
      .sort((x, y) => x.orderIndex - y.orderIndex);
  }

  /** From scoring method 13 the cap follows the answer's critical-error resolution, not the raw flags. */
  private isCriticalErrorApplied(run: BenchmarkRunDetailDto, a: BenchmarkRunAnswerDto): boolean {
    if (this.resolvesCriticalErrors(run)) {
      return a.criticalErrorResolution === 'Agreed'
        || a.criticalErrorResolution === 'UpheldByVerifier'
        || a.criticalErrorResolution === 'SingleAssessor';
    }
    return !!a.criticalError || (!!run.isPanelRun && a.coAssessmentCriticalError === true);
  }

  /**
   * Whether an answer header shows the CRITICAL ERROR badge: member A's flag, or from scoring
   * method 13 any critical-error resolution but None, including the unresolved and overturned ones.
   */
  showCriticalErrorBadge(ans: BenchmarkRunAnswerDto): boolean {
    if (this.resolvesCriticalErrors(this.selectedRunDetail)) {
      return ans.criticalErrorResolution != null && ans.criticalErrorResolution !== 'None';
    }
    return !!ans.criticalError;
  }

  /** The CRITICAL ERROR badge's title: from scoring method 13 it states the resolution. */
  criticalErrorBadgeTitle(ans: BenchmarkRunAnswerDto): string {
    const resolution = this.resolvesCriticalErrors(this.selectedRunDetail) ? ans.criticalErrorResolution : null;
    switch (resolution) {
      case 'Agreed':
        return 'Critical error confirmed by both panel members — Quality capped at 25';
      case 'UpheldByVerifier':
        return 'Critical error flagged by one panel member and upheld by the claim verifier — both members capped at 25';
      case 'SingleAssessor':
        return 'Critical error flagged by the assessor — Quality capped at 25';
      case 'Unresolved':
        return 'Critical error flagged by one panel member only, with no verifier ruling — split unresolved, the two members averaged';
      case 'OverturnedByVerifier':
        return 'Critical error flagged by one panel member and overturned by the claim verifier — not applied';
      default:
        return 'Critical error cap applied (Quality capped at 25)';
    }
  }

  /** Scoring method 13 on: critical errors carry a resolution and answers an outcome class. */
  private resolvesCriticalErrors(run: BenchmarkRunDetailDto | null | undefined): boolean {
    return (run?.scoringMethodVersion ?? 0) >= 13;
  }

  /** The run's outcome summary; null before scoring method 13. */
  private outcomeSummaryOf(run: BenchmarkRunDetailDto | null | undefined): BenchmarkRunOutcomeSummaryDto | null {
    return run && this.resolvesCriticalErrors(run) ? run.outcomeSummary ?? null : null;
  }

  /** A fraction as a whole percent, without the sign. */
  private wholePercent(fraction: number): number {
    return Math.round(fraction * 100);
  }

  /** *rate 6 % (95 % CI 1–26 %)*, or null when the summary has no rate. */
  private criticalErrorRateLabel(summary: BenchmarkRunOutcomeSummaryDto): string | null {
    if (summary.criticalErrorRate == null) return null;
    const interval = summary.criticalErrorRateLow != null && summary.criticalErrorRateHigh != null
      ? ` (95 % CI ${this.wholePercent(summary.criticalErrorRateLow)}–${this.wholePercent(summary.criticalErrorRateHigh)} %)`
      : '';
    return `rate ${this.wholePercent(summary.criticalErrorRate)} %${interval}`;
  }

  /** The diagnostics capture's critical-error resolution line, scoring method 13 on. */
  private criticalErrorResolutionDiagnosticsLine(summary: BenchmarkRunOutcomeSummaryDto): string {
    const parts = [
      `confirmed ${summary.confirmedCriticalErrorCount}${this.questionListSuffix(summary.confirmedCriticalErrorQuestions)}`,
      `unresolved ${summary.unresolvedCriticalErrorCount}${this.questionListSuffix(summary.unresolvedCriticalErrorQuestions)}`,
      `overturned ${summary.overturnedCriticalErrorCount}${this.questionListSuffix(summary.overturnedCriticalErrorQuestions)}`
    ];
    return `critical error resolution: ${parts.join(', ')}; ${this.criticalErrorRateLabel(summary) ?? 'rate n/a'} of ${summary.classifiedCount} classified`;
  }

  /** The diagnostics capture's outcome-class line, scoring method 13 on. */
  private outcomeDiagnosticsLine(summary: BenchmarkRunOutcomeSummaryDto): string {
    const percent = (fraction: number | null) => fraction == null ? 'n/a' : `${this.wholePercent(fraction)} %`;
    const counts = [
      `correct ${summary.correctCount}`,
      `partial ${summary.partialCount}`,
      `incorrect ${summary.incorrectCount}`,
      `not attempted ${summary.notAttemptedCount}${this.questionListSuffix(summary.notAttemptedQuestions)}`,
      `no answer ${summary.noAnswerCount}`
    ];
    return `answer outcomes: ${counts.join(', ')}; correct when attempted ${percent(summary.correctWhenAttempted)}, wrong instead of abstaining ${percent(summary.wrongInsteadOfAbstaining)}`;
  }

  /** ` (Q3, Q7)`, or empty without questions. */
  private questionListSuffix(questions: number[]): string {
    return questions.length > 0 ? ` (${questions.map(q => `Q${q}`).join(', ')})` : '';
  }

  /** The Critical Errors card's value: confirmed critical errors from scoring method 13, else capped answers. */
  get keyFigureCriticalErrorCount(): number {
    const summary = this.outcomeSummaryOf(this.selectedRunDetail);
    return summary ? summary.confirmedCriticalErrorCount : this.keyFigureCriticalErrorAnswers.length;
  }

  /**
   * The Critical Errors card's notes: the question numbers, then how many the second reader disputed.
   * From scoring method 13 also the rate with its interval, and the unresolved and overturned splits.
   */
  get keyFigureCriticalErrorNotes(): string[] {
    const answers = this.keyFigureCriticalErrorAnswers;
    const summary = this.outcomeSummaryOf(this.selectedRunDetail);
    if (summary) {
      const notes: string[] = [];
      if (summary.confirmedCriticalErrorQuestions.length > 0) {
        notes.push(summary.confirmedCriticalErrorQuestions.map(q => `Q${q}`).join(', '));
      }
      const rate = this.criticalErrorRateLabel(summary);
      if (rate) notes.push(rate);
      const splits: string[] = [];
      if (summary.unresolvedCriticalErrorCount > 0) {
        splits.push(`${summary.unresolvedCriticalErrorCount} unresolved${this.questionListSuffix(summary.unresolvedCriticalErrorQuestions)}`);
      }
      if (summary.overturnedCriticalErrorCount > 0) {
        splits.push(`${summary.overturnedCriticalErrorCount} overturned by the verifier${this.questionListSuffix(summary.overturnedCriticalErrorQuestions)}`);
      }
      if (splits.length > 0) notes.push(splits.join(' · '));
      const disputedConfirmed = answers.filter(a => a.secondOpinionCriticalError === false).length;
      if (disputedConfirmed > 0) {
        const reader = this.selectedRunDetail?.isPanelRun ? 'reference reader' : 'second reader';
        notes.push(`${disputedConfirmed} disputed by the ${reader}`);
      }
      return notes;
    }
    if (answers.length === 0) return [];
    const notes = [answers.map(a => `Q${a.orderIndex}`).join(', ')];
    const disputed = answers.filter(a => a.secondOpinionCriticalError === false).length;
    if (disputed > 0) {
      const reader = this.selectedRunDetail?.isPanelRun ? 'reference reader' : 'second reader';
      notes.push(`${disputed} disputed by the ${reader}`);
    }
    return notes;
  }

  /**
   * The Answered card's note: every question answered, or one clause per cause of the shortfall.
   * Each clause is capped by what remains, so the clauses always add up to total − answered.
   */
  get answeredNote(): string {
    const run = this.selectedRunDetail;
    if (!run) return '';
    const total = run.totalQuestionCount ?? 0;
    const answered = run.answeredQuestionCount ?? 0;
    let remaining = Math.max(0, total - answered);
    if (remaining === 0) return 'every question answered';
    const asked = run.answers?.length ?? 0;
    const causes: [number, string][] = [
      [run.unansweredQuestionCount ?? 0, 'without text'],
      [run.terminalFailureAnswerCount ?? 0, 'failed at the provider'],
      [asked > 0 ? Math.max(0, total - asked) : 0, 'never asked']
    ];
    const clauses: string[] = [];
    for (const [count, text] of causes) {
      const taken = Math.min(count, remaining);
      if (taken > 0) {
        clauses.push(`${taken} ${text}`);
        remaining -= taken;
      }
    }
    if (remaining > 0) {
      clauses.push(`${remaining} other error${remaining === 1 ? '' : 's'}`);
    }
    return clauses.join(' · ');
  }

  /**
   * Of the flagged answers above, the ones whose raw score the cap actually lowered. A flagged
   * answer already at or below the ceiling before the cap applied is not counted here, so this
   * figure can be smaller than criticalErrorAnswerCount — it is the one "capped by" describes.
   * In a panel run it compares the panel raw score (the mean of both members' raw scores) with the
   * panel score, as the report's cappedCount does.
   */
  get criticalErrorCapBindingCount(): number {
    const run = this.selectedRunDetail;
    if (!run) return 0;
    return (run.answers ?? []).filter(a => {
      if (!run.isPanelRun) {
        return a.rawQualityScore != null && a.qualityScore != null && a.rawQualityScore > a.qualityScore;
      }
      const rawA = a.rawQualityScore ?? a.qualityScore;
      const rawB = a.coAssessmentRawQualityScore ?? a.coAssessmentQualityScore;
      return a.panelQualityScore != null && rawA != null && rawB != null
        && (rawA + rawB) / 2 > a.panelQualityScore;
    }).length;
  }

  /** The question numbers of the critical-error answers, comma separated, for the integrity notice. */
  get criticalErrorQuestionNumbers(): string {
    return this.keyFigureCriticalErrorAnswers.map(a => a.orderIndex).join(', ');
  }

  /**
   * The question numbers of the answers carrying an advisory flag. The run-level
   * `advisoryFlagAnswerCount` says how many there are; this names them, using the same per-answer
   * test the question cards use so the two can never disagree.
   */
  get advisoryFlagQuestionNumbers(): string {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => this.hasAdvisoryFlag(a))
      .map(a => a.orderIndex)
      .join(', ');
  }

  /**
   * The assessor's stored justification. Returns null when there is nothing to show — a run
   * graded before evidence was collected, or a malformed blob — so the template's `@if` skips
   * the block entirely. Evidence is commentary and never a score input, so a parse failure
   * costs a panel and nothing else.
   */
  answerEvidence(ans: BenchmarkRunAnswerDto): { accuracy?: string; completeness?: string; criticalErrorDemoted?: boolean } | null {
    if (!ans.assessmentEvidenceJson) {
      return ans.criticalError && ans.criticalErrorQuote ? {} : null;
    }

    try {
      const parsed = JSON.parse(ans.assessmentEvidenceJson);
      const evidence = {
        accuracy: typeof parsed?.accuracy === 'string' ? parsed.accuracy : undefined,
        completeness: typeof parsed?.completeness === 'string' ? parsed.completeness : undefined,
        criticalErrorDemoted: parsed?.criticalErrorDemoted === true
      };
      const hasAnything = evidence.accuracy || evidence.completeness || evidence.criticalErrorDemoted ||
        (ans.criticalError && ans.criticalErrorQuote);
      return hasAnything ? evidence : null;
    } catch {
      return null;
    }
  }

  formatAnswerStatus(status: string | number): string {
    if (status === 1 || status === 'Ok') return 'Ok';
    if (status === 2 || status === 'ProviderError') return 'ProviderError';
    if (status === 3 || status === 'Failed') return 'Failed';
    if (status === 4 || status === 'Skipped') return 'Skipped';
    if (status === 5 || status === 'EmptyAnswer') return 'EmptyAnswer';
    if (status === 6 || status === 'Canceled') return 'Canceled';
    return String(status);
  }

  formatAssessmentStatus(status: string | number | undefined): string {
    if (status === 1 || status === 'Pending') return 'Pending';
    if (status === 2 || status === 'Assessing') return 'Assessing';
    if (status === 3 || status === 'Scored') return 'Scored';
    if (status === 4 || status === 'Failed') return 'Failed';
    return status != null ? String(status) : 'Scored';
  }

  getQuestionScoreBadgeClass(score: number | null | undefined): string {
    if (score == null) return 'badge-score-na';
    if (score >= 80) return 'badge-score-high';
    if (score >= 50) return 'badge-score-mid';
    return 'badge-score-low';
  }

  hasDivergence(finalScore?: number | null, computedScore?: number | null): boolean {
    if (finalScore == null || computedScore == null) return false;
    return Math.abs(finalScore - computedScore) > 10;
  }

  /**
   * What the run detail's Answer Duration card shows: the time the candidate spent producing
   * answers, or a dash.
   *
   * There is deliberately no fallback to the wall clock here. The two measure different things —
   * on run 24 they are 12m 45s and 23m 39s, because grading and the questions cancelled in flight
   * sit in the gap — so substituting one for the other would put a wall-clock figure under a label
   * that says answer time. A run with no answer time recorded says so.
   */
  runAnswerDurationLabel(run: BenchmarkRunDetailDto): string {
    return run.totalAnswerDurationMs ? formatDuration(run.totalAnswerDurationMs) : '—';
  }

  /**
   * What the run detail's Elapsed Wall Time card shows: start to finish, grading included. Falls
   * back to the two timestamps, which is the same measurement by another route rather than a
   * different one — a run interrupted by a restart records no wall clock of its own, because the
   * outage between the crash and the cleanup is not run time.
   */
  runWallClockLabel(run: BenchmarkRunDetailDto): string {
    const elapsed = run.totalDurationMs || elapsedBetweenTimestamps(run);
    return elapsed ? formatDuration(elapsed) : '—';
  }

  /**
   * The Elapsed Wall Time card's note. A repaired run's headline is the original execution's wall
   * time, so the note names the re-run's own span; without a completion stamp it runs to now.
   */
  runWallClockNote(run: BenchmarkRunDetailDto): string {
    const base = 'start to finish, grading included';
    if (!run.rerunStartedAtUtc) return base;
    const rerunMs = elapsedMsBetween(run.rerunStartedAtUtc, run.rerunCompletedAtUtc);
    return `${base} · plus re-run ${formatDuration(rerunMs)}`;
  }

  /**
   * The Answer Duration card's note. A repaired run's total includes every re-executed answer, so
   * it can exceed the original execution's wall time.
   */
  runAnswerDurationNote(run: BenchmarkRunDetailDto): string {
    const base = 'sum over answers, tools included';
    return run.rerunStartedAtUtc ? `${base} · includes re-executed answers` : base;
  }

  /**
   * H5. The diagnostics capture's own service tier wording: an unset tier reads as "default (none
   * requested)" rather than the badge wording's "None", because the capture is read by a person
   * troubleshooting a run rather than displayed as a compact badge.
   */
  private diagnosticsServiceTierLabel(tier: string | null | undefined): string {
    return tier ? formatServiceTier(tier) : 'default (none requested)';
  }

  // --- Results screen: profile fit, agreement, weighting ---

  /**
   * The same pairing showProfileFitAdvisory warns about before the run, read off the finished
   * run's own snapshot rather than today's selected profile. The results screen used to mark the
   * Speed Index advisory for concurrency only, so a max-thinking candidate on a 15,000 ms
   * interactive profile showed a bare "SPEED INDEX 67 / 100".
   */
  get showRunProfileFitAdvisory(): boolean {
    const thinkingLevel = this.selectedRunDetail?.testedModelThinkingLevelUsed;
    const speedTargetMs = this.selectedRunDetail?.scoringProfileSpeedTargetMs;
    if (!thinkingLevel || speedTargetMs == null) return false;

    return DELIBERATING_THINKING_LEVELS.includes(thinkingLevel.toLowerCase()) &&
      speedTargetMs < INTERACTIVE_SPEED_TARGET_MAX_MS;
  }

  get runProfileFitAdvisoryTitle(): string {
    const level = this.selectedRunDetail?.testedModelThinkingLevelUsed ?? 'high';
    const target = this.selectedRunDetail?.scoringProfileSpeedTargetMs ?? 0;
    return `Profile targets interactive latency (${target.toLocaleString('en-US')} ms); this run used thinking level ${level} — read the Speed Index as advisory`;
  }

  /** Scored answers, mirroring the report's Speed Index denominator: Ok status with a quality score. */
  private get speedIndexScoredAnswers(): BenchmarkRunAnswerDto[] {
    return this.answeredRunAnswers.filter(a => a.qualityScore != null);
  }

  /** Denominator for the saturation check below: every answer the Speed Index is scored over. */
  get speedIndexScoredAnswerCount(): number {
    return this.speedIndexScoredAnswers.length;
  }

  /** Of the scored answers, those whose Speed Score sits at the ceiling. */
  get speedIndexCeilingAnswerCount(): number {
    return this.speedIndexScoredAnswers.filter(a => a.speedScore != null && a.speedScore >= 100).length;
  }

  /**
   * True once at least half the scored answers sit at the Speed Index ceiling: every answer at
   * 100 looks identical to the index whether it finished at the target or well inside it, so past
   * this point the index cannot discriminate and median model time is the figure to read instead.
   */
  get showSpeedIndexSaturationAdvisory(): boolean {
    const scored = this.speedIndexScoredAnswerCount;
    return scored > 0 && this.speedIndexCeilingAnswerCount * 2 >= scored;
  }

  get speedIndexSaturationAdvisoryTitle(): string {
    return `Saturated — ${this.speedIndexCeilingAnswerCount} of ${this.speedIndexScoredAnswerCount} answers finished inside their difficulty-scaled target, so this index cannot discriminate at this speed. Compare median model time instead.`;
  }

  /**
   * Median model time over the answered questions, which is what still discriminates once the Speed
   * Index has run out of resolution. Mirrors the report's own median, from the same column.
   */
  get medianModelTimeMs(): number | null {
    const times = this.answeredRunAnswers.map(a => this.modelTimeOf(a)).sort((x, y) => x - y);
    if (times.length === 0) return null;

    const mid = Math.floor(times.length / 2);
    return times.length % 2 === 1 ? times[mid] : Math.round((times[mid - 1] + times[mid]) / 2);
  }

  /** Mean model time over the answered questions, in whole ms; null when no answer is Ok. */
  get meanModelTimeMs(): number | null {
    const times = this.answeredRunAnswers.map(a => this.modelTimeOf(a));
    if (times.length === 0) return null;
    return Math.round(times.reduce((sum, time) => sum + time, 0) / times.length);
  }

  /** The Mean Time per Question card's value: `17.2 s` under a minute, `1m 12s` from one; `—` with no answer. */
  get meanModelTimeLabel(): string {
    const mean = this.meanModelTimeMs;
    return mean == null ? '—' : this.formatModelTime(mean);
  }

  /**
   * The Mean Time per Question card's note, with the median beside the mean; names the answered
   * count when it is below the question count, since both figures are over answered questions only.
   */
  get meanModelTimeNote(): string {
    const median = this.medianModelTimeMs;
    if (median == null) return 'no answered question';
    const note = `model time, tools excluded · median ${this.formatModelTime(median)}`;
    const answered = this.answeredRunAnswers.length;
    const total = this.selectedRunDetail?.totalQuestionCount ?? 0;
    return answered < total ? `${note} · over ${answered} answered` : note;
  }

  /** The demoted Speed card's value: the median in the Mean Time card's units, or `—`. */
  get medianModelTimeLabel(): string {
    const median = this.medianModelTimeMs;
    return median == null ? '—' : this.formatModelTime(median);
  }

  /** One decimal of seconds under a minute (`17.2 s`), whole minutes and seconds from one (`1m 12s`). */
  formatModelTime(ms: number): string {
    const tenths = Math.round(Math.max(0, ms) / 100);
    if (tenths < 600) {
      return `${(tenths / 10).toFixed(1)} s`;
    }
    const totalSecs = Math.round(ms / 1000);
    return `${Math.floor(totalSecs / 60)}m ${totalSecs % 60}s`;
  }

  /**
   * H9. Where the index cannot discriminate — saturated, or a deliberating candidate on an
   * interactive-latency profile — the card leads with median model time and demotes the index to its
   * sub-line. Presentation only: no score, no scoring profile and no method version changes, because
   * re-tuning the speed target would mark every future run non-comparable on the quality dimensions
   * too.
   */
  get demoteSpeedIndex(): boolean {
    return (this.showSpeedIndexSaturationAdvisory || this.showRunProfileFitAdvisory) &&
      this.medianModelTimeMs != null;
  }

  get showAgreementTile(): boolean {
    return (this.selectedRunDetail?.secondOpinionGradedAnswerCount ?? 0) > 0;
  }

  get agreementMeanAbsDeltaLabel(): string {
    const delta = this.selectedRunDetail?.secondOpinionMeanAbsDelta;
    return delta == null ? 'N/A' : delta.toFixed(1);
  }

  get agreementMeanSignedDeltaLabel(): string {
    const delta = this.selectedRunDetail?.secondOpinionMeanSignedDelta;
    if (delta == null) return '';
    const sign = delta > 0 ? '+' : delta < 0 ? '−' : '';
    return `${sign}${Math.abs(delta).toFixed(1)}`;
  }

  /**
   * Never shown without this fraction beside it. A mean delta over trigger-selected answers is
   * conditioned on the first assessor's own uncertainty and says nothing about the instrument;
   * the same number over every answer is an inter-rater agreement rate. Only the coverage tells
   * a reader which one they are looking at.
   */
  get agreementCoverageLabel(): string {
    const run = this.selectedRunDetail;
    if (!run) return '';
    return `${this.secondOpinionCompletedAnswerCount} of ${run.answeredQuestionCount} answers graded twice`;
  }

  get agreementModeLabel(): string {
    switch (this.selectedRunDetail?.secondOpinionModeUsed) {
      case BenchmarkSecondOpinionMode.All: return 'Every answer';
      case BenchmarkSecondOpinionMode.FlaggedAndOutliers: return 'Flagged and outliers';
      case BenchmarkSecondOpinionMode.FlaggedPlusSample: return 'Flagged plus sample';
      case BenchmarkSecondOpinionMode.Flagged: return 'Flagged only';
      default: return 'Manual only';
    }
  }

  /** Coverage was selected by trigger, so the disagreement rate is not an instrument figure. */
  get agreementIsSelective(): boolean {
    return this.showAgreementTile &&
      this.selectedRunDetail?.secondOpinionModeUsed !== BenchmarkSecondOpinionMode.All;
  }

  /**
   * H4. The agreement figure carries an advisory whenever it cannot be read as an inter-rater agreement
   * rate: either coverage was selected by trigger, or the sample is too small for a mean to mean anything.
   * At n = 1 a displayed 0.0 is the arithmetic of a single point and reads as perfect agreement.
   */
  static readonly AGREEMENT_MIN_SAMPLE = 5;

  get showAgreementAdvisory(): boolean {
    if (!this.showAgreementTile) return false;
    const graded = this.selectedRunDetail?.secondOpinionGradedAnswerCount ?? 0;
    return this.agreementIsSelective || graded < AdminBenchmarkComponent.AGREEMENT_MIN_SAMPLE;
  }

  /** The agreement advisory's footnote: one sentence per cause that applies. */
  get agreementAdvisoryTitle(): string {
    const run = this.selectedRunDetail;
    const graded = run?.secondOpinionGradedAnswerCount ?? 0;
    const answered = run?.answeredQuestionCount ?? 0;
    const sentences: string[] = [];
    if (this.agreementIsSelective) {
      sentences.push('Coverage is selected by trigger, so this is conditioned on the first assessor’s own ' +
        `uncertainty, not an unbiased agreement rate. n = ${graded} of ${answered}.`);
    }
    if (graded < AdminBenchmarkComponent.AGREEMENT_MIN_SAMPLE) {
      sentences.push(`Only n = ${graded} of ${answered} answers were graded twice, too few for a mean to be an agreement rate.`);
    }
    return sentences.join(' ');
  }

  /**
   * Shown only where the two aggregations differ, following the Raw Quality Index tile. The gap
   * is how far difficulty weighting moved the headline: on the 2026-09-03 run it moved it *up*
   * two points, because the model's two weakest answers were two of its easiest questions.
   */
  get showUnweightedQualityTile(): boolean {
    const run = this.selectedRunDetail;
    return run?.unweightedQualityIndex != null && run.qualityIndex != null &&
      run.unweightedQualityIndex !== run.qualityIndex;
  }

  get weightingDeltaLabel(): string {
    const run = this.selectedRunDetail;
    if (run?.unweightedQualityIndex == null || run.qualityIndex == null) return '';
    const delta = run.qualityIndex - run.unweightedQualityIndex;
    return `${delta > 0 ? '+' : ''}${delta}`;
  }

  get toolBudgetQuestionNumbers(): string {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => a.toolBudgetExhausted)
      .map(a => a.orderIndex)
      .join(', ');
  }

  get contestedVerdictAnswerCount(): number {
    return this.selectedRunDetail?.contestedVerdictAnswerCount ?? 0;
  }

  get contestedVerdictQuestionNumbers(): string {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => (a.answerFlagNames ?? []).includes('ContestedVerdict'))
      .map(a => a.orderIndex)
      .join(', ');
  }

  get indexConfidenceLabel(): string {
    const se = this.selectedRunDetail?.qualityIndexStandardError;
    if (se == null || se <= 0) return '';
    return `± ${Math.round(1.96 * se)}`;
  }

  get secondOpinionBlindLabel(): string {
    return this.selectedRunDetail?.secondOpinionBlindUsed ? 'blind' : 'anchored';
  }

  get disputeVerificationLabel(): string {
    const disputed = (this.selectedRunDetail?.answers ?? [])
      .filter(a => a.secondOpinionDisagreed);
    const verified = disputed.filter(a =>
      a.claimsSupportedCount != null || a.claimsRefutedCount != null || a.claimsIndeterminateCount != null
      || a.accusedSupportedCount != null || a.accusedRefutedCount != null || a.accusedIndeterminateCount != null
    );
    if (verified.length === 0) return '';
    const totalSupported = verified.reduce((sum, a) => sum + (a.claimsSupportedCount ?? 0), 0);
    const totalRefuted = verified.reduce((sum, a) => sum + (a.claimsRefutedCount ?? 0), 0);
    const totalIndeterminate = verified.reduce((sum, a) => sum + (a.claimsIndeterminateCount ?? 0), 0);
    const totalAccusedSupported = verified.reduce((sum, a) => sum + (a.accusedSupportedCount ?? 0), 0);
    const totalAccusedRefuted = verified.reduce((sum, a) => sum + (a.accusedRefutedCount ?? 0), 0);
    const totalAccusedIndeterminate = verified.reduce((sum, a) => sum + (a.accusedIndeterminateCount ?? 0), 0);
    // The accused-sentence check runs only on the disputed sentences themselves, so it appears
    // only when it found something to report.
    const accusedPart = (totalAccusedSupported + totalAccusedRefuted + totalAccusedIndeterminate) > 0
      ? `; accused sentences: ${totalAccusedSupported} supported, ${totalAccusedRefuted} refuted, ${totalAccusedIndeterminate} indeterminate`
      : '';
    if (verified.length === 1) {
      return `Claim verification for Q${verified[0].orderIndex}: ${totalSupported} supported, ${totalRefuted} refuted, ${totalIndeterminate} indeterminate${accusedPart}.`;
    }
    return `Claim verification for disputed answer(s): ${totalSupported} supported, ${totalRefuted} refuted, ${totalIndeterminate} indeterminate${accusedPart}.`;
  }

  /**
   * The two counts the grading rules produce as measurements rather than defects: rubric points
   * the assessor placed outside the question's scope, and rubric format suggestions it set aside.
   * They are deliberately kept out of the integrity notice, which lists things that went wrong —
   * neither of these did. Each stays hidden at zero, because a run graded before its marker
   * existed and a run whose assessor found nothing both report zero.
   */
  get completenessOutOfScopeCount(): number {
    return this.selectedRunDetail?.completenessOutOfScopeCount ?? 0;
  }

  get readabilityFormOnlyCount(): number {
    return this.selectedRunDetail?.readabilityFormOnlyCount ?? 0;
  }

  /**
   * The subsets of the two counts above where the marker sits beside a docked level that names no
   * other defect — the point recorded as set aside and deducted for all the same. Sub-lines of
   * their own measurement rather than entries in the integrity notice: one of them is a second
   * reading worth taking, not a verdict that has gone wrong.
   */
  get completenessOutOfScopeDeductedCount(): number {
    return this.selectedRunDetail?.completenessOutOfScopeDeductedCount ?? 0;
  }

  get readabilityFormOnlyDeductedCount(): number {
    return this.selectedRunDetail?.readabilityFormOnlyDeductedCount ?? 0;
  }

  /** A panel run's notice gives each measurement per member. */
  get instrumentMeasurementsPerMember(): boolean {
    return !!this.selectedRunDetail?.isPanelRun;
  }

  /** Member B's answers marked `completenessOutOfScope` in its own verdict; 0 outside a panel run. */
  get memberBCompletenessOutOfScopeCount(): number {
    return this.memberBMeasurements().completenessOutOfScope;
  }

  /** Member B's answers marked `readabilityFormOnly` in its own verdict; 0 outside a panel run. */
  get memberBReadabilityFormOnlyCount(): number {
    return this.memberBMeasurements().readabilityFormOnly;
  }

  get hasCompletenessOutOfScopeMeasurement(): boolean {
    return this.completenessOutOfScopeCount > 0 || this.memberBCompletenessOutOfScopeCount > 0;
  }

  get hasReadabilityFormOnlyMeasurement(): boolean {
    return this.readabilityFormOnlyCount > 0 || this.memberBReadabilityFormOnlyCount > 0;
  }

  get hasInstrumentMeasurements(): boolean {
    return this.hasCompletenessOutOfScopeMeasurement || this.hasReadabilityFormOnlyMeasurement;
  }

  /** Whether the viewed run's Run Integrity Notice has a clause; the Integrity tab and the notice both read it. */
  get hasRunIntegrityNotice(): boolean {
    const run = this.selectedRunDetail;
    if (!run) return false;
    return this.criticalErrorAnswerCount > 0 || (run.transportDefectAnswerCount ?? 0) > 0
      || (run.recoveredAnswerCount ?? 0) > 0 || (run.toolStarvedAnswerCount ?? 0) > 0
      || (run.advisoryFlagAnswerCount ?? 0) > 0 || this.disputedAnswerCount > 0
      || (run.secondOpinionCriticalErrorSplitCount ?? 0) > 0 || this.contestedVerdictAnswerCount > 0
      || (run.unevidencedDeductionAnswerCount ?? 0) > 0 || this.omissionAsAccuracyAnswerCount > 0
      || this.refutedClaimAnswerCount > 0 || this.contestedCriticalErrorAnswerCount > 0
      || (this.contestedAccuracyDeductionAnswerCount ?? 0) > 0 || (this.dimensionOutlierAnswerCount ?? 0) > 0
      || this.claimVerificationFailedAnswerCount > 0 || this.secondOpinionSelectedButUnused
      || this.secondOpinionFailedAnswerCount > 0 || this.reassessedAnswerCount > 0
      || this.showRunProfileFitAdvisory || this.selectedRunMissingBoardQuotes.length > 0;
  }

  private memberBMeasurementCache: {
    answers: BenchmarkRunAnswerDto[];
    completenessOutOfScope: number;
    readabilityFormOnly: number;
  } | null = null;

  /** Member B's two measurement counts, from `coAssessmentJson.flags` as memberBFlagsLine counts them, parsed once per answer list. */
  private memberBMeasurements(): { completenessOutOfScope: number; readabilityFormOnly: number } {
    const run = this.selectedRunDetail;
    if (!run?.isPanelRun || !run.answers) {
      return { completenessOutOfScope: 0, readabilityFormOnly: 0 };
    }
    let cache = this.memberBMeasurementCache;
    if (!cache || cache.answers !== run.answers) {
      let completenessOutOfScope = 0;
      let readabilityFormOnly = 0;
      for (const answer of run.answers) {
        const flags = this.coAssessmentOf(answer)?.flags;
        if (flags?.completenessOutOfScope === true) completenessOutOfScope++;
        if (flags?.readabilityFormOnly === true) readabilityFormOnly++;
      }
      cache = { answers: run.answers, completenessOutOfScope, readabilityFormOnly };
      this.memberBMeasurementCache = cache;
    }
    return cache;
  }

  get omissionAsAccuracyAnswerCount(): number {
    return this.selectedRunDetail?.omissionAsAccuracyAnswerCount ?? 0;
  }

  get omissionAsAccuracyQuestionNumbers(): string {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => (a.answerFlagNames ?? []).includes('OmissionAsAccuracy'))
      .map(a => a.orderIndex)
      .join(', ');
  }

  get unevidencedDeductionQuestionNumbers(): string {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => (a.answerFlagNames ?? []).includes('UnevidencedDeduction'))
      .map(a => a.orderIndex)
      .join(', ');
  }

  get refutedClaimAnswerCount(): number {
    return this.selectedRunDetail?.refutedClaimAnswerCount ?? 0;
  }

  get refutedClaimQuestionNumbers(): string {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => (a.answerFlagNames ?? []).includes('RefutedClaim'))
      .map(a => a.orderIndex)
      .join(', ');
  }

  get contestedCriticalErrorAnswerCount(): number {
    return this.selectedRunDetail?.contestedCriticalErrorAnswerCount ?? 0;
  }

  get contestedCriticalErrorQuestionNumbers(): string {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => (a.answerFlagNames ?? []).includes('ContestedCriticalError'))
      .map(a => a.orderIndex)
      .join(', ');
  }

  /** Null on a run before harness 20, which never adjudicated an out-of-rubric deduction. */
  get contestedAccuracyDeductionAnswerCount(): number | null {
    return this.selectedRunDetail?.contestedAccuracyDeductionAnswerCount ?? null;
  }

  get contestedAccuracyDeductionQuestionNumbers(): string {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => (a.answerFlagNames ?? []).includes('ContestedAccuracyDeduction'))
      .map(a => a.orderIndex)
      .join(', ');
  }

  /** The BOARD FACTS quote check stamped at launch. Null for a run before it existed, or with no board. */
  get selectedRunBoardFactsCheck(): BoardFactsCheckDto | null {
    return this.selectedRunDetail?.boardFactsCheck ?? null;
  }

  /** The run's missing board quotes as listed in its notice, capped. */
  get selectedRunMissingBoardQuotes(): BoardFactIssueDto[] {
    return (this.selectedRunBoardFactsCheck?.missingLiterals ?? [])
      .slice(0, MISSING_BOARD_QUOTE_LIST_CAP);
  }

  /** How many missing board quotes the capped list leaves out. */
  get selectedRunMissingBoardQuotesOverflow(): number {
    const total = this.selectedRunBoardFactsCheck?.missingLiterals.length ?? 0;
    return Math.max(0, total - MISSING_BOARD_QUOTE_LIST_CAP);
  }

  /** Null on a run before the harness version that added the dimension-outlier check. */
  get dimensionOutlierAnswerCount(): number | null {
    return this.selectedRunDetail?.dimensionOutlierAnswerCount ?? null;
  }

  get dimensionOutlierQuestionNumbers(): string {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => (a.answerFlagNames ?? []).includes('DimensionOutlier'))
      .map(a => a.orderIndex)
      .join(', ');
  }

  get claimVerificationFailedAnswerCount(): number {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => !!a.claimVerificationError && a.claimVerificationError.trim().length > 0)
      .length;
  }

  get claimVerificationFailedQuestionNumbers(): string {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => !!a.claimVerificationError && a.claimVerificationError.trim().length > 0)
      .map(a => a.orderIndex)
      .join(', ');
  }

  get criticalErrorSplitQuestionNumbers(): string {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => a.secondOpinionCriticalError != null && a.secondOpinionCriticalError !== a.criticalError)
      .map(a => a.orderIndex)
      .join(', ');
  }

  candidatePromptSummaryOf(run?: BenchmarkRunDetailDto | BenchmarkRunSummaryDto | null): string | null {
    return formatCandidatePrompt(candidatePromptParts(run));
  }

  get secondOpinionSelectedButUnused(): boolean {
    const run = this.selectedRunDetail;
    return !!run?.secondOpinionAssessorModelConfigurationId &&
      (run.secondOpinionGradedAnswerCount ?? 0) === 0;
  }

  get secondOpinionFailedAnswerCount(): number {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => !!a.secondOpinionError && a.secondOpinionError.trim().length > 0)
      .length;
  }

  /** Numerator behind {@link agreementCoverageLabel}: answers whose second opinion completed. */
  get secondOpinionCompletedAnswerCount(): number {
    return this.selectedRunDetail?.secondOpinionGradedAnswerCount ?? 0;
  }

  get secondOpinionFailedQuestionNumbers(): string {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => !!a.secondOpinionError && a.secondOpinionError.trim().length > 0)
      .map(a => a.orderIndex)
      .join(', ');
  }

  get reassessedAnswerCount(): number {
    return this.selectedRunDetail?.reassessedAnswerCount ?? 0;
  }

  get reassessedQuestionNumbers(): string {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => (a.reassessmentCount ?? 0) > 0)
      .map(a => a.orderIndex)
      .join(', ');
  }

  get unverifiedClaimTotal(): number {
    return (this.selectedRunDetail?.answers ?? [])
      .reduce((sum, a) => sum + (a.unverifiedClaimCount ?? 0), 0);
  }

  /** The claims themselves, for the per-answer panel. Empty on a malformed or absent blob. */
  unverifiedClaimsOf(answer: BenchmarkRunAnswerDto): string[] {
    if (!answer.unverifiedClaimsJson) return [];
    try {
      const parsed = JSON.parse(answer.unverifiedClaimsJson);
      return Array.isArray(parsed) ? parsed.filter(c => typeof c === 'string' && c.trim().length > 0) : [];
    } catch {
      return [];
    }
  }

  /**
   * The per-claim verifications for this answer. Empty on a malformed or absent blob. `roles` is
   * set by the harness from harness 31 and absent on an older record.
   */
  claimVerificationsOf(answer: BenchmarkRunAnswerDto): { claimIndex?: number; claim: string; verdict: string; citation?: string | null; basis?: string | null; roles?: string[]; raisedBy?: string[] | null; accusedBy?: string[] | null; suspectedBy?: string[] | null }[] {
    if (!answer.claimVerificationJson) return [];
    try {
      const parsed = JSON.parse(answer.claimVerificationJson);
      return Array.isArray(parsed) ? parsed : [];
    } catch {
      return [];
    }
  }

  /**
   * One label per advisory role an entry was submitted for: the assessor's critical-error quote,
   * its out-of-rubric basis, or a sentence it charged as false. None for an ordinary claim or a
   * record without roles. In a panel run `raisedBy` names the member who submitted it, and
   * `accusedBy` the members who charged it as false (absent before harness 44: `raisedBy`).
   */
  claimRoleLabels(roles: string[] | null | undefined, raisedBy?: string[] | null, accusedBy?: string[] | null): string[] {
    if (!Array.isArray(roles)) return [];
    const member = this.claimMemberPhrase(raisedBy);
    const accuser = Array.isArray(accusedBy) ? this.claimMemberPhrase(accusedBy) : member;
    const labels: string[] = [];
    if (roles.includes('criticalErrorQuote')) labels.push(member ? `critical-error quote from ${member}` : 'critical-error quote');
    if (roles.includes('outOfRubricBasis')) labels.push(member ? `out-of-rubric basis from ${member}` : 'out-of-rubric basis');
    if (roles.includes('accusedQuote')) labels.push(`sentence ${accuser ?? 'the assessor'} charged as false`);
    return labels;
  }

  /**
   * Who submitted a claim-verification entry, from its `raisedBy`: `member A`, `member B` or
   * `both members`. Null without one, as in a single-assessor run or a record before harness 40.
   */
  claimMemberPhrase(raisedBy: string[] | null | undefined): string | null {
    if (!Array.isArray(raisedBy)) return null;
    const members = new Set(raisedBy.map(m => (typeof m === 'string' ? m.trim().toUpperCase() : '')));
    const a = members.has('A');
    const b = members.has('B');
    if (a && b) return 'both members';
    if (a) return 'member A';
    if (b) return 'member B';
    return null;
  }

  /**
   * A claim of the answer's own: a record without roles, or one carrying `unverifiedClaim`.
   * Mirrors `BenchmarkClaimRoles.IsOrdinaryClaim` on the server.
   */
  static isOrdinaryClaim(verification: { roles?: string[] | null }): boolean {
    return verification.roles == null || (Array.isArray(verification.roles) && verification.roles.includes('unverifiedClaim'));
  }

  /** Whether any entry was sent to check the assessor rather than the answer. */
  hasAdvisoryClaimRoles(verifications: { roles?: string[] }[]): boolean {
    return verifications.some(v => this.claimRoleLabels(v.roles).length > 0);
  }

  /** Trigger names as stored, in the words this screen uses for them. */
  secondOpinionTriggerLabel(trigger: string | null | undefined): string {
    switch (trigger) {
      case 'CriticalError': return 'critical error';
      case 'RefutedClaim': return 'refuted claim';
      case 'ContestedVerdict': return 'contested verdict';
      case 'OutOfRubricAccuracy': return 'out-of-rubric accuracy deduction';
      case 'UnevidencedDeduction': return 'unevidenced deduction';
      case 'OmissionAsAccuracy': return 'omission docked as accuracy';
      case 'DimensionOutlier': return 'dimension outlier';
      case 'UnverifiedClaims': return 'unverifiable claims';
      case 'BelowThreshold': return 'below profile threshold';
      case 'Outlier': return 'outlier below run median';
      case 'Sample': return 'sample top-up';
      case 'All': return 'double grading';
      case 'Manual': return 'manual trial';
      default: return trigger ?? '';
    }
  }

  // --- Calibration ---

  selectCalibrationAssessorModel(config: SystemAiConfigDto | null) {
    if (!config) return;
    this.calibrationAssessorConfigId = config.id;
  }

  loadCalibrations(runId: number): void {
    this.loadingCalibrations = true;
    this.benchmarkService.getCalibrations(runId).subscribe({
      next: (rows) => {
        this.calibrations = rows;
        this.loadingCalibrations = false;
        this.viewSync.notify();
      },
      error: (err) => {
        this.calibrations = [];
        this.loadingCalibrations = false;
        this.calibrationErrorMessage = err?.error || 'Failed to load calibrations.';
        this.viewSync.notify();
      }
    });
  }

  runCalibration(runId: number): void {
    if (this.calibrationAssessorConfigId == null || this.calibrating) return;

    this.calibrating = true;
    this.calibrationErrorMessage = null;
    // Only a panel run names a target; a single-assessor run's request stays as it always was.
    const request = this.selectedRunDetail?.isPanelRun
      ? this.benchmarkService.calibrateAssessor(runId, this.calibrationAssessorConfigId, this.calibrationTarget)
      : this.benchmarkService.calibrateAssessor(runId, this.calibrationAssessorConfigId);
    request.subscribe({
      next: () => {
        this.calibrating = false;
        this.loadCalibrations(runId);
      },
      error: (err) => {
        this.calibrating = false;
        this.calibrationErrorMessage = err?.error || 'Calibration failed.';
        this.viewSync.notify();
      }
    });
  }

  /** Compare against, one option per panel member plus the panel mean, each naming its model. */
  get calibrationTargetPickerOptions(): ModelPickerOption<ModelPickerModel>[] {
    const run = this.selectedRunDetail ?? null;
    if (run !== this.calibrationTargetOptionsSource) {
      this.calibrationTargetOptionsSource = run;
      this.calibrationTargetOptionsCache = run ? this.buildCalibrationTargetOptions(run) : [];
    }
    return this.calibrationTargetOptionsCache;
  }

  private buildCalibrationTargetOptions(run: BenchmarkRunDetailDto): ModelPickerOption<ModelPickerModel>[] {
    const a = run.assessorModelDisplayNameUsed || run.assessorModelIdUsed || 'Unknown model';
    const b = run.coAssessorModelDisplayNameUsed || run.coAssessorModelIdUsed || 'Unknown model';
    return [
      {
        key: 'Assessor', tag: 'Assessor A', model: {
          displayName: a, provider: run.assessorModelProviderUsed,
          thinkingLevel: run.assessorModelThinkingLevelUsed, reasoningMode: run.assessorModelReasoningModeUsed
        }
      },
      {
        key: 'CoAssessor', tag: 'Co-assessor B', model: {
          displayName: b, provider: run.coAssessorModelProviderUsed,
          thinkingLevel: run.coAssessorModelThinkingLevelUsed, reasoningMode: run.coAssessorModelReasoningModeUsed
        }
      },
      { key: 'Panel', tag: 'Panel', model: { displayName: `Mean of ${a} and ${b}` } }
    ];
  }

  selectCalibrationTarget(key: ModelPickerKey | null): void {
    if (key === 'Assessor' || key === 'CoAssessor' || key === 'Panel') {
      this.calibrationTarget = key;
    }
  }

  // --- Assessor panel (run detail) ---

  /** A calibration row's target, named as the panel names its members. Null reads as `Assessor`. */
  calibrationTargetLabel(target: string | null | undefined): string {
    const value = target ?? 'Assessor';
    return this.calibrationTargetOptions.find(o => o.value === value)?.label ?? value;
  }

  /** `same-family` when the grader's provider is the candidate's, the server's definition of family. */
  familyRelationOf(provider: string | null | undefined, candidateProvider: string | null | undefined): BenchmarkFamilyRelation | null {
    if (!provider || !candidateProvider) return null;
    return BenchmarkLauncherState.sameProvider(provider, candidateProvider) ? 'same-family' : 'cross-family';
  }

  /** Member B's full verdict from `coAssessmentJson`; null when absent or malformed. */
  coAssessmentOf(ans: BenchmarkRunAnswerDto): BenchmarkCoAssessmentRecord | null {
    if (!ans.coAssessmentJson) return null;
    try {
      const parsed = JSON.parse(ans.coAssessmentJson) as unknown;
      return parsed && typeof parsed === 'object' ? parsed as BenchmarkCoAssessmentRecord : null;
    } catch {
      return null;
    }
  }

  /** The `B − A` mean as a signed figure, the way the agreement tile signs its delta. */
  get panelSignedDeltaLabel(): string {
    const delta = this.selectedRunDetail?.panelMeanSignedDelta;
    if (delta == null) return 'n/a';
    const sign = delta > 0 ? '+' : delta < 0 ? '−' : '';
    return `${sign}${Math.abs(delta).toFixed(1)}`;
  }

  get panelIccLabel(): string {
    const icc = this.selectedRunDetail?.panelIntraclassCorrelation;
    return icc == null ? 'n/a' : icc.toFixed(2);
  }

  private synthesisViewCache: { run: BenchmarkRunDetailDto; views: BenchmarkSynthesisView[] } | null = null;

  /**
   * The syntheses the synthesis panel shows: the assessor's, and in a panel run the co-assessor's.
   * Cached per run object, since a fresh array on every check would re-bind the panel's input each
   * time and fail the development-mode stability check.
   */
  synthesisViewsOf(run: BenchmarkRunDetailDto): BenchmarkSynthesisView[] {
    if (this.synthesisViewCache?.run === run) {
      return this.synthesisViewCache.views;
    }

    const panel = !!run.isPanelRun;
    const views: BenchmarkSynthesisView[] = [];
    if (run.assessmentText || run.assessmentParseFailed) {
      views.push({
        key: 'A',
        memberLabel: panel ? 'Member A' : 'Assessor',
        modelLabel: run.assessorModelDisplayNameUsed || run.assessorModelIdUsed,
        provider: run.assessorModelProviderUsed || null,
        familyRelation: this.familyRelationOf(run.assessorModelProviderUsed, run.testedModelProviderUsed),
        text: run.assessmentText ?? null,
        findings: run.synthesisFindings ?? [],
        holisticScore: run.finalScore ?? null,
        parseFailed: !!run.assessmentParseFailed,
        rawJson: run.assessmentJson ?? null
      });
    }
    if (panel && (run.coAssessorSynthesisText || run.coAssessorSynthesisParseFailed)) {
      views.push({
        key: 'B',
        memberLabel: 'Member B',
        modelLabel: run.coAssessorModelDisplayNameUsed || run.coAssessorModelIdUsed || 'Co-assessor',
        provider: run.coAssessorModelProviderUsed ?? null,
        familyRelation: this.familyRelationOf(run.coAssessorModelProviderUsed, run.testedModelProviderUsed),
        text: run.coAssessorSynthesisText ?? null,
        findings: run.coAssessorSynthesisFindings ?? [],
        holisticScore: run.coAssessorFinalScore ?? null,
        parseFailed: !!run.coAssessorSynthesisParseFailed,
        rawJson: run.coAssessorSynthesisJson ?? null
      });
    }

    this.synthesisViewCache = { run, views };
    return views;
  }

  /**
   * The trial re-assessment writes to the reference-reader slot. In a panel run that verdict is
   * evidence and is never replaced, so the trial is offered only where the slot is empty.
   */
  canTrialReassess(ans: BenchmarkRunAnswerDto): boolean {
    return !this.selectedRunDetail?.isPanelRun || ans.secondOpinionQualityScore == null;
  }

  // --- Game Snapshot & Question Generation & Review Handlers ---

  openSnapshotViewer(snapshotId: number): void {
    this.workspace.refreshRunningGeneration();
    this.snapshotViewer?.open(snapshotId);
  }

  /** Why the open viewer may not delete its snapshot, or null when it may. */
  get snapshotDeleteBlockedReason(): string | null {
    const suiteId = this.workspace.suites.find(s => s.gameSnapshotId != null && s.gameSnapshotId === this.snapshotViewer?.snapshotId)?.id;
    return suiteId != null && suiteId === this.workspace.runningGenerationSuiteId
      ? 'A question generation job is running on this suite. Delete the snapshot after it finishes.'
      : null;
  }

  onSnapshotDeleted(snapshotId: number): void {
    const clear = (s: BenchmarkSuiteDto) => {
      s.gameSnapshotId = null;
      s.gameSnapshotName = null;
      s.gameSnapshotCharCount = null;
    };
    const suite = this.workspace.suites.find(s => s.gameSnapshotId === snapshotId);
    if (suite) {
      clear(suite);
    }
    if (this.workspace.currentSuiteForQuestions?.gameSnapshotId === snapshotId) {
      clear(this.workspace.currentSuiteForQuestions);
    }
    this.workspace.loadSuites();
    this.viewSync.notify();
  }

  onSnapshotUpdated(updated: BenchmarkGameSnapshotDto): void {
    const suite = this.workspace.suites.find(s => s.gameSnapshotId === updated.id);
    if (suite) {
      suite.gameSnapshotName = updated.name;
      suite.gameSnapshotCharCount = updated.charCount;
    }
    this.viewSync.notify();
  }

  /** The run was made with a board: it has a stored board record, or recorded the board's hash. */
  get selectedRunHasBoard(): boolean {
    const run = this.selectedRunDetail;
    return !!run && (run.hasBoardRecord === true || !!run.gameSnapshotSha256Used);
  }

  /** Opens the board this run was made with, read-only, from the run's own board record. */
  openRunBoard(runId: number): void {
    this.snapshotViewer?.openReadOnly(this.benchmarkService.getRunBoard(runId), 'The board this run was made with.');
  }
}
