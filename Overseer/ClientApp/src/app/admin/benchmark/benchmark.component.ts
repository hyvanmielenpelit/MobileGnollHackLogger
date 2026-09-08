import { Component, OnInit, OnDestroy, OnChanges, SimpleChanges, Input, ChangeDetectorRef, HostListener, ViewChild, ElementRef, inject } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  AdminBenchmarkService,
  BenchmarkSuiteDto,
  BenchmarkQuestionDto,
  BenchmarkRunSummaryDto,
  BenchmarkRunDetailDto,
  BenchmarkRunAnswerDto,
  BenchmarkScoringProfileDto,
  CreateBenchmarkSuiteRequest,
  UpdateBenchmarkSuiteRequest,
  CreateBenchmarkQuestionRequest,
  UpdateBenchmarkQuestionRequest,
  CreateBenchmarkScoringProfileRequest,
  UpdateBenchmarkScoringProfileRequest,
  StartBenchmarkRunRequest,
  SameProviderWarningDto,
  StartDifficultyAssessmentRequest,
  DifficultyAssessmentJobDto,
  DifficultyAssessmentJobItemDto,
  DifficultyAssessmentJobLogEntryDto,
  BenchmarkFootprintDto,
  BenchmarkAssessorCalibrationDto,
  BenchmarkLastAssessorDto,
  BenchmarkSecondOpinionMode,
  BENCHMARK_SECOND_OPINION_MODES,
  BenchmarkGameSnapshotDto,
  QuestionGenerationJobDto,
  QuestionGenerationJobItemDto,
  QuestionGenerationJobLogEntryDto,
  BenchmarkRunLimitsDto,
  BenchmarkRunSeriesDto,
  BenchmarkRunGroupDto,
  BenchmarkRunGroupTierPreviewDto,
  BenchmarkComparabilityResultDto,
  BenchmarkComparabilityIndexDto,
  BenchmarkModelComparisonDto,
  BenchmarkModelComparisonPricingBasis
} from '../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../services/admin.service';

import { CollapsibleMarkdownComponent } from '../../shared/collapsible-markdown/collapsible-markdown.component';
import { SuiteHealthComponent, SuiteHealthTab } from './suite-health/suite-health.component';
import { MultiRunComponent } from './multi-run/multi-run.component';
import { MultiRunProgressDialogComponent } from './multi-run/multi-run-progress-dialog.component';
import { SnapshotViewerComponent } from '../../shared/snapshot-viewer/snapshot-viewer.component';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';
import { SystemService } from '../../services/system.service';
import { parseServerUtcDate, elapsedMsBetween } from '../../utils/date.util';
import { TableState, exactFilter } from '../../shared/data-table/table-state';
import { SortHeaderComponent } from '../../shared/data-table/sort-header.component';
import { TablePagerComponent } from '../../shared/data-table/table-pager.component';
import { ModelComparisonComponent } from './model-comparison/model-comparison.component';
import {
  ComparisonSourcePickerComponent,
  ModelComparisonSelection
} from './model-comparison/comparison-source-picker.component';
import {
  ComparisonSelectionNotice,
  selectionNotices
} from './model-comparison/model-comparison.models';

/**
 * The Model Comparison selection, as it is remembered between visits and between sessions.
 *
 * The selection is persisted rather than the comparison: a stored payload would be re-priced stale
 * the moment the catalog moved, and re-issuing the request is cheap next to showing costs that are
 * no longer true.
 */
interface BenchmarkComparisonSelection {
  runIds: number[];
  groupIds: number[];
  suiteId: number | null;
  pricingBasis: BenchmarkModelComparisonPricingBasis;
}

/**
 * One row of the run progress list: a suite question merged with its answer, if the run
 * has produced one yet. The executor writes an answer row only after the model replies, so
 * a question with no answer row is either dispatched or not: `BenchmarkRunDetailDto.
 * inFlightOrderIndexes` — server-side state kept by `BenchmarkRunManager` — is what tells
 * the two apart. In flight is 'Answering'; everything else with no answer is 'Pending'.
 */
export interface BenchmarkRunProgressRow {
  orderIndex: number;
  questionText: string;
  /**
   * Formatted answer status, or 'Answering' while the provider request is in flight, or
   * 'Pending' when the run has not dispatched this question yet.
   */
  status: string;
  /** Formatted assessment status, or '' when there is no answer yet. */
  assessmentStatus: string;
  errorMessage: string | null;
}

/**
 * The functional families a benchmark tool belongs to. Mirrors `BenchmarkToolFamily` in
 * `Overseer/Services/Benchmarking/BenchmarkChatTransfer.cs`, which is what the report builder's
 * Tool Routing table classifies against.
 */
export type BenchmarkToolFamilyName =
  'SourceCode' | 'Wiki' | 'StructuredLookup' | 'KnowledgeBase' | 'Other';

/**
 * U1. The one place tool-name membership is written on the client.
 *
 * This is a deliberate mirror of `BenchmarkChatTransfer.ClassifyTool`, and it exists as a single
 * exported constant rather than as lists spelled out at each call site because the report and this
 * screen must classify the same call the same way. Two inline copies of "which tools are source
 * tools" would agree on the day they were written and disagree the first time a tool is added — and
 * the disagreement would surface as an operator reading two different source shares for one run.
 *
 * When a tool is added on the server, it is added here in the same change.
 */
export const BENCHMARK_TOOL_FAMILY_MEMBERSHIP: ReadonlyArray<readonly [BenchmarkToolFamilyName, readonly string[]]> = [
  ['SourceCode', ['source_code_search', 'source_code_view', 'search_definitions',
    'get_function_definition', 'get_constants', 'list_indexed_files']],
  ['Wiki', ['wiki_search', 'wiki_view', 'nethack_wiki_search', 'nethack_wiki_view']],
  ['StructuredLookup', ['monster_lookup', 'item_lookup', 'get_monster_stats', 'get_item_stats']],
  ['KnowledgeBase', ['get_knowledge_article']]
];

/** Display order and labels, matching the report's Tool Routing table exactly. */
export const BENCHMARK_TOOL_FAMILY_LABELS: ReadonlyArray<readonly [BenchmarkToolFamilyName, string]> = [
  ['SourceCode', 'Source Code'],
  ['Wiki', 'Wiki'],
  ['StructuredLookup', 'Structured Lookup'],
  ['KnowledgeBase', 'Knowledge Base'],
  ['Other', 'Other']
];

/** One tool name to its family. Case- and whitespace-insensitive, as the server's switch is. */
export function classifyBenchmarkTool(toolName: string | null | undefined): BenchmarkToolFamilyName {
  const key = (toolName ?? '').trim().toLowerCase();
  for (const [family, members] of BENCHMARK_TOOL_FAMILY_MEMBERSHIP) {
    if (members.includes(key)) return family;
  }
  return 'Other';
}

/** One row of the Tool Routing block: a family, its call count, and its share of the run. */
export interface BenchmarkToolFamilyRow {
  family: BenchmarkToolFamilyName;
  label: string;
  count: number;
  sharePercentage: number;
}

/**
 * U1. Pearson *r* of per-answer source-family share against model time and against quality, with
 * the sample size that produced them. A correlation without its *n* is not a finding.
 */
export interface BenchmarkSourceShareCorrelations {
  modelTimeR: number | null;
  qualityR: number | null;
  sampleSize: number;
}

/**
 * The run setup an operator last started, remembered across reloads. Exactly the fields that make up a
 * run: not the same-provider acknowledgement, which is a per-run safety gate, and not the
 * difficulty-assessor, retry-assessor, generation-model or calibration-assessor selections, which belong
 * to other workflows on the same screen.
 *
 * Every field is nullable because a stored blob may predate a field, and because every id is re-validated
 * against the currently available list before it is applied.
 */
interface BenchmarkRunSettings {
  suiteId: number | null;
  testedConfigId: number | null;
  assessorConfigId: number | null;
  secondOpinionConfigId: number | null;
  claimVerifierConfigId: number | null;
  /** The operator's explicit override, or null to keep following the scoring profile's own default. */
  secondOpinionMode: number | null;
  scoringProfileId: number | null;
  verboseMode: boolean | null;
  runCount: number | null;
}

@Component({
  selector: 'app-admin-benchmark',
  standalone: true,
  imports: [
    CommonModule, DecimalPipe, FormsModule, CollapsibleMarkdownComponent, SuiteHealthComponent,
    SnapshotViewerComponent, MultiRunComponent, MultiRunProgressDialogComponent,
    SortHeaderComponent, TablePagerComponent, ModelComparisonComponent,
    ComparisonSourcePickerComponent
  ],
  templateUrl: './benchmark.component.html',
  styleUrls: ['./benchmark.component.scss']
})
export class AdminBenchmarkComponent implements OnInit, OnDestroy, OnChanges {
  @Input() systemConfigs: SystemAiConfigDto[] = [];

  @ViewChild('suiteDialog') suiteDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('questionsDialog') questionsDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('questionFormDialog') questionFormDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('runDetailDialog') runDetailDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('scoringProfileFormDialog') scoringProfileFormDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('sameProviderDialog') sameProviderDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('bulkDeleteDialog') bulkDeleteDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('confirmActionDialog') confirmActionDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('difficultyAssessorDialog') difficultyAssessorDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('difficultyProgressHeading') difficultyProgressHeading?: ElementRef<HTMLElement>;
  @ViewChild('retryDialog') retryDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('runProgressDialog') runProgressDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('runProgressHeading') runProgressHeading?: ElementRef<HTMLElement>;
  @ViewChild('suiteHealthDialog') suiteHealthDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('suiteHealthHeading') suiteHealthHeading?: ElementRef<HTMLElement>;

  @ViewChild('comparisonWizardDialog') comparisonWizardDialog?: ElementRef<HTMLDialogElement>;

  /**
   * The wizard instance, for the two things the host cannot reach through the DOM: focusing the
   * heading it owns, and asking whether an export is in flight before allowing a close.
   */
  @ViewChild(ModelComparisonComponent) comparisonWizard?: ModelComparisonComponent;
  @ViewChild('snapshotViewer') snapshotViewer?: SnapshotViewerComponent;
  @ViewChild('multiRunPanel') multiRunPanel?: MultiRunComponent;
  @ViewChild('generationDialog') generationDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('generationProgressHeading') generationProgressHeading?: ElementRef<HTMLElement>;
  suiteHealthInitialTab: SuiteHealthTab = 'items';

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
  private pendingConfirmAction: (() => void) | null = null;

  private benchmarkService = inject(AdminBenchmarkService);
  private systemService = inject(SystemService);
  private cdr = inject(ChangeDetectorRef);

  activeSubTab: 'run' | 'history' | 'multirun' | 'suites' | 'profiles' | 'modelcomparison' = 'run';

  /**
   * Tab order, and the source of truth for arrow-key navigation indices. Multi-Run Analysis sits
   * immediately right of Run History because a group is built out of the runs listed there, so the
   * two are read in that order. Scoring Profiles sits right of Manage Suites.
   */
  readonly subTabs = ['run', 'history', 'multirun', 'suites', 'profiles', 'modelcomparison'] as const;

  /**
   * BenchmarkAnswerFlags bits that mean the graded text was corrupted in transport:
   * Empty = 1, HarnessArtifacts = 2, Truncated = 4.
   */
  private static readonly TRANSPORT_DEFECT_FLAGS = 1 | 2 | 4;

  /**
   * BenchmarkAnswerFlags bits that are advisory only and must never be presented as a
   * failure: ReasoningBleed = 8, RepeatedFragments = 16, ContestedVerdict = 32,
   * UnevidencedDeduction = 64, RefutedClaim = 128, OmissionAsAccuracy = 256. Must track
   * BenchmarkRunFinalizer.AdvisoryFlags on the server as the source of truth.
   */
  private static readonly ADVISORY_FLAGS = 8 | 16 | 32 | 64 | 128 | 256;

  /** The same advisory members by name, as they arrive in answerFlagNames. */
  private static readonly ADVISORY_FLAG_NAMES: readonly string[] = [
    'ReasoningBleed',
    'RepeatedFragments',
    'ContestedVerdict',
    'UnevidencedDeduction',
    'RefutedClaim',
    'OmissionAsAccuracy'
  ];

  // Suites
  suites: BenchmarkSuiteDto[] = [];
  selectedSuiteId: number | null = null;
  loadingSuites = false;

  // Scoring Profiles
  scoringProfiles: BenchmarkScoringProfileDto[] = [];
  selectedScoringProfileId: number | null = null;
  loadingProfiles = false;
  editingProfileId: number | null = null;
  profileForm: CreateBenchmarkScoringProfileRequest = {
    name: '',
    isDefault: false,
    weightAccuracy: 0.55,
    weightCompleteness: 0.25,
    weightConciseness: 0.10,
    weightReadability: 0.10,
    levelScoresJson: '[1, 15, 35, 55, 72, 87, 100]',
    criticalErrorCeiling: 25,
    secondOpinionQualityThreshold: 50,
    secondOpinionMode: BenchmarkSecondOpinionMode.Flagged,
    secondOpinionOutlierDeltaPoints: 25,
    secondOpinionBlind: true,
    speedTargetMs: 15000,
    speedDecayK: 20.0,
    speedDifficultyScaling: 1.0,
    maxParallelQuestions: 1
  };
  profileValidationErrors: string[] = [];

  // Run Setup
  testedConfigId: number | null = null;
  assessorConfigId: number | null = null;
  /**
   * Optional third model: re-grades answers the assessor flagged with a critical error or
   * scored below the profile's threshold. Null means no second opinion for this run, which is
   * the default — it spends tokens, and one model checking its own verdict is not a second
   * reading, so there is deliberately no fallback to the assessor.
   */
  secondOpinionConfigId: number | null = null;

  /**
   * Optional model: verifies unverified factual claims against the game source code and wiki
   * using read-only tools. Null means no claim verification for this run, which is the default.
   */
  claimVerifierConfigId: number | null = null;

  /**
   * Candidate system prompt response style: false for concise (the production default), true for
   * detailed.
   */
  candidateVerboseMode = false;

  get candidateResponseStyleHint(): string {
    return this.candidateVerboseMode
      ? "The candidate is told to give detailed explanations with background, edge cases and headers. This run will NOT be comparable with concise runs on Completeness, Conciseness or Readability — only Accuracy carries over. Use it to find out whether a completeness gap comes from the prompt or from the model."
      : "The candidate is told 'Default to 2–5 sentences per response' — the production chat default, and what every run so far has used. Keep it here unless you are deliberately testing the other style.";
  }

  /**
   * Per-run override of the scoring profile's second-opinion mode. Null follows the profile, so
   * changing the profile changes the shown default until the operator picks something.
   */
  private secondOpinionModeOverride: number | null = null;

  readonly secondOpinionModeOptions = BENCHMARK_SECOND_OPINION_MODES;

  /**
   * The assessor of the suite's most recent completed run, for the assessor-change advisory.
   * Null until the lookup returns, and carries a null runId for a suite with no completed run.
   */
  lastAssessor: BenchmarkLastAssessorDto | null = null;

  isTestedModelDropdownOpen = false;
  isAssessorModelDropdownOpen = false;
  isSecondOpinionModelDropdownOpen = false;
  isClaimVerifierModelDropdownOpen = false;
  startingRun = false;
  runErrorMessage: string | null = null;
  sameProviderWarning: SameProviderWarningDto | null = null;

  // Stored Footprint & Bulk Deletion
  footprints: { [suiteId: number]: BenchmarkFootprintDto } = {};
  suiteForBulkDelete: BenchmarkSuiteDto | null = null;
  deletingSuiteRuns = false;

  // Active Run Tracking
  private static readonly RUN_POLL_INTERVAL_MS = 2000;
  private static readonly RUN_ELAPSED_TICK_MS = 1000;
  private runElapsedInterval: any = null;
  lastRunPollAtUtc: string | null = null;
  lastRunPollError: string | null = null;
  runQuestionsLoadError: string | null = null;
  overseerBuildVersion: string | null = null;

  activeRunId: number | null = null;
  activeRunDetail: BenchmarkRunDetailDto | null = null;
  private pollInterval: any = null;
  /**
   * Kept separate from visibilityChangeHandler, which belongs to difficulty polling.
   * One shared field would let whichever poller stops last detach the other's listener.
   */
  private runVisibilityChangeHandler: (() => void) | null = null;

  // Run Progress Dialog
  isRunProgressDialogOpen = false;
  returnToSeriesOnClose = false;
  runProgressQuestions: BenchmarkQuestionDto[] = [];
  /**
   * Suite the cached runProgressQuestions belong to; null means nothing is loaded. This,
   * not the array's length, is what gates the fetch — a suite that genuinely has no
   * questions would otherwise be re-fetched on every dialog open.
   */
  private runProgressQuestionsSuiteId: number | null = null;
  copiedRunDiagnostics = false;
  private copiedRunDiagnosticsTimer: ReturnType<typeof setTimeout> | null = null;

  // --- Multi-run series ---
  //
  // A series is N executions of one identical request, strictly one at a time. Everything here is
  // inert at runCount 1: no series row is created, startRun() is posted exactly as before, and the
  // single-run banner and dialog are the only progress surfaces. That is the regression that
  // matters most about this feature, so the branch is one `if` in startBenchmark and nowhere else.

  private static readonly SERIES_POLL_INTERVAL_MS = 5000;

  /**
   * How many times to execute the configured request. Bound to a `type="number"` field whose max is
   * `runLimits.maxRunCountPerSeries`, never a literal: raising the configured daily cap must raise
   * the field with it, and the server re-checks against the live guard regardless.
   */
  runCount = 1;

  /**
   * On a cap denial: pause the series in WaitingForCap and retry, rather than stopping it. Either
   * way every completed member is kept and the series stays resumable.
   */
  allowCapWait = false;

  /** The caps and the live rolling-window counts. Null until GET runs/limits answers. */
  runLimits: BenchmarkRunLimitsDto | null = null;

  activeSeriesId: number | null = null;
  activeSeries: BenchmarkRunSeriesDto | null = null;
  private seriesPollInterval: any = null;
  private seriesVisibilityChangeHandler: (() => void) | null = null;

  /** The Multi-Run Progress dialog's visibility. The dialog element itself belongs to that component. */
  multiRunDialogVisible = false;

  /**
   * A series the operator asked to look at that this component is not driving — set by the
   * Multi-Run Analysis tab's Series badge and cleared when the dialog closes. Kept apart from
   * <see cref="activeSeriesId"/> so opening someone else's finished series cannot be mistaken for
   * this page having one in flight.
   */
  seriesDialogId: number | null = null;

  /** Which series the progress dialog shows: an explicitly opened one, else the live one. */
  get dialogSeriesId(): number | null {
    return this.seriesDialogId ?? this.activeSeriesId;
  }

  seriesErrorMessage: string | null = null;
  resumingSeries = false;

  // History
  historyRuns: BenchmarkRunSummaryDto[] = [];
  historySuiteFilter: number | null = null;
  loadingHistory = false;

  /**
   * Sort, filter and page state for the Run History table. `historyRuns` itself stays in the
   * server's own order — `instrumentChangeOf` and `completedRunsOfSelectedSuite` both locate a
   * run by its position in that list, and a user-chosen sort would make either misread the data.
   * `view()` never mutates its input, so both keep reading `historyRuns` unaffected by this.
   */
  readonly historyTable = new TableState<BenchmarkRunSummaryDto>('id', 'desc').registerAccessors(
    {
      id: r => r.id,
      suiteName: r => r.suiteName,
      testedModelDisplayNameUsed: r => r.testedModelDisplayNameUsed,
      assessorModelDisplayNameUsed: r => r.assessorModelDisplayNameUsed,
      status: r => this.formatStatusLabel(r.status),
      // Null sorts last automatically, which is right for a run that never scored.
      qualityIndex: r => r.qualityIndex ?? r.finalScore,
      speedIndex: r => r.speedIndex,
      // The same expression the Duration cell displays, so the column sorts by what it shows.
      durationMs: r => this.runDurationMs(r),
      estimatedCost: r => r.estimatedCost,
      startedAtUtc: r => new Date(r.startedAtUtc)
    },
    {
      suiteName: r => r.suiteName,
      testedModelDisplayNameUsed: r => r.testedModelDisplayNameUsed,
      assessorModelDisplayNameUsed: r => r.assessorModelDisplayNameUsed,
      // Keyed on the same label the Status cell shows, so the <select> options and the cell text
      // always agree.
      status: exactFilter(r => this.formatStatusLabel(r.status))
    }
  );

  get historyView(): BenchmarkRunSummaryDto[] {
    return this.historyTable.view(this.historyRuns);
  }

  /** The statuses actually present in the loaded history, so a retired status drops out on its own. */
  get historyStatusOptions(): string[] {
    const seen = new Set<string>();
    for (const run of this.historyRuns) {
      seen.add(this.formatStatusLabel(run.status));
    }
    return Array.from(seen).sort();
  }

  onHistoryTableChanged(): void {
    this.cdr.detectChanges();
  }

  // --- Run History: series badge, group column and the group builder ---
  //
  // A run belongs to at most one series and to any number of analysis groups, so the badge is a
  // property of the row while the group column is a lookup over the loaded groups.

  runGroups: BenchmarkRunGroupDto[] = [];
  loadingRunGroups = false;

  /** Runs ticked in the history table, in selection order. A group is built out of exactly these. */
  selectedRunIds = new Set<number>();

  /**
   * The tier the current selection would resolve to, previewed before anything is created. On a
   * refusal it carries the keys that differ and the runs carrying them: a "no" with no reason is
   * unusable in a group builder, which is why the preview endpoint exists at all.
   */
  groupTierPreview: BenchmarkComparabilityResultDto | null = null;
  groupPreviewError: string | null = null;
  previewingGroupTier = false;

  groupBuilderName = '';
  groupBuilderNotes = '';
  /** Required to persist a Tier C group. Without it a cross-condition set is refused server-side. */
  groupBuilderCrossCondition = false;
  /** An existing group to add the selection to, or null to create a new one. */
  groupBuilderTargetId: number | null = null;
  creatingGroup = false;
  groupBuilderError: string | null = null;
  groupBuilderSuccess: string | null = null;

  // --- Model Comparison ---
  //
  // The host owns the selection, the request and the two lists the picker offers; the picker and
  // the comparison view are both presentational. One owner is what keeps the two panels of this
  // sub-tab from disagreeing about what is selected.

  comparison: BenchmarkModelComparisonDto | null = null;
  comparisonLoading = false;
  comparisonError: string | null = null;
  comparisonRunIds: number[] = [];
  comparisonGroupIds: number[] = [];
  /** The picker's suite scope. Null offers every suite; independent of the Run Benchmark selection. */
  comparisonSuiteId: number | null = null;
  comparisonPricingBasis: BenchmarkModelComparisonPricingBasis = 'Current';

  /**
   * The wizard band's notices. Derived from the index and the selection this component owns, so
   * the picker and the band cannot disagree about what the selection costs.
   */
  get comparisonSelectionNotices(): ComparisonSelectionNotice[] {
    return selectionNotices({
      index: this.comparabilityIndex,
      indexLoading: this.comparabilityIndexLoading,
      indexError: this.comparabilityIndexError,
      runIds: this.comparisonRunIds,
      groupIds: this.comparisonGroupIds,
      pricingBasis: this.comparisonPricingBasis
    });
  }

  /**
   * Guards against an out-of-order comparison response. Compare can be clicked faster than the
   * round trip returns, and an older payload rendered over a newer selection is worse than none —
   * it charts models the operator is no longer looking at.
   */
  private comparisonToken = 0;

  /**
   * The comparability index for the sources on offer: which of them agree on every must-match key
   * and may therefore be charted together. Read-only, and only ever a disclosure aid — the
   * comparison endpoint decides what is actually comparable.
   */
  comparabilityIndex: BenchmarkComparabilityIndexDto | null = null;
  comparabilityIndexLoading = false;
  comparabilityIndexError: string | null = null;

  /** The index's own out-of-order guard, for the same reason comparisonToken exists. */
  private comparabilityIndexToken = 0;

  // Detail Modal
  selectedRunDetail: BenchmarkRunDetailDto | null = null;
  loadingDetail = false;
  expandedQuestions = new Set<number>();
  expandedThoughts = new Set<number>();
  expandedArtifacts = new Set<number>();
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
  isCalibrationAssessorDropdownOpen = false;
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
  isRetryAssessorDropdownOpen = false;

  // Suite Health. The suite whose full-screen dialog is open, or null. One at a time by
  // construction: there is a single dialog element for every suite card.
  suiteHealthSuiteId: number | null = null;

  /**
   * A question the Suite Health panel asked to edit, opened once the suite's questions have
   * loaded. Cleared on use, so a later manual open of the same list does not reopen the editor.
   */
  private pendingQuestionEditId: number | null = null;

  // Suite Dialogs
  editingSuiteId: number | null = null;
  suiteForm: CreateBenchmarkSuiteRequest = { name: '', description: '' };

  // Questions Dialog
  currentSuiteForQuestions: BenchmarkSuiteDto | null = null;
  questions: BenchmarkQuestionDto[] = [];
  loadingQuestions = false;
  // Difficulty Assessor Dialog State
  suiteForDifficultyAssessment: BenchmarkSuiteDto | null = null;
  difficultyAssessorConfigId: number | null = null;
  isDifficultyAssessorDropdownOpen = false;
  difficultyAssessmentScope: 'suite' | 'question' = 'suite';
  questionIdForDifficultyAssessment: number | null = null;

  difficultyDialogPhase: 'select' | 'progress' = 'select';
  difficultyJob: DifficultyAssessmentJobDto | null = null;
  difficultyJobStarting = false;
  terminatingDifficultyJob = false;
  private difficultyPollInterval: any = null;
  private visibilityChangeHandler: (() => void) | null = null;

  actionErrorMessage: string | null = null;
  difficultyDialogError: string | null = null;
  copiedDiagnostics = false;
  private copiedDiagnosticsTimer: ReturnType<typeof setTimeout> | null = null;

  get ratingDifficulty(): boolean {
    return this.difficultyJobIsRunning;
  }

  get ratingQuestionId(): number | null {
    if (this.difficultyJobIsRunning && this.difficultyJob?.scope === 'questions' && this.difficultyJob.items.length === 1) {
      return this.difficultyJob.items[0].questionId;
    }
    return null;
  }

  get difficultyJobIsRunning(): boolean {
    return this.difficultyJob != null && this.difficultyJob.status === 'Running';
  }

  get difficultyJobIsTerminal(): boolean {
    return this.difficultyJob != null && this.difficultyJob.status !== 'Running';
  }

  get difficultyProgressValue(): number {
    if (!this.difficultyJob) return 0;
    return this.difficultyJob.ratedCount + this.difficultyJob.failedCount;
  }

  get difficultyProgressMax(): number {
    return this.difficultyJob?.totalCount || 100;
  }

  get difficultyJobProgressLabel(): string {
    return this.difficultyProgressLabel();
  }

  get failedDifficultyItems(): DifficultyAssessmentJobItemDto[] {
    return this.difficultyJob?.items.filter(i => i.status === 'Failed') || [];
  }

  get difficultyDiagnosticsText(): string {
    if (!this.difficultyJob) return '';
    const lines: string[] = [];
    lines.push(`Job ID: ${this.difficultyJob.id}`);
    lines.push(`Suite: ${this.difficultyJob.suiteName} (ID: ${this.difficultyJob.suiteId})`);
    lines.push(`Assessor: ${this.difficultyJob.assessorDisplayName}`);
    lines.push(`Status: ${this.difficultyJob.status}`);
    lines.push(`Model Calls: ${this.difficultyJob.totalModelCalls}`);
    lines.push(`Prompt Tokens: ${this.difficultyJob.promptTokens}, Output Tokens: ${this.difficultyJob.outputTokens}`);
    lines.push('');
    lines.push('--- LOG ---');
    for (const entry of this.difficultyJob.log) {
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
        this.cdr.detectChanges();
      }, 2000);
    } catch {
      this.difficultyDialogError = 'Could not copy the diagnostics to the clipboard.';
    }
  }

  // Question Form Dialog
  editingQuestionId: number | null = null;
  questionForm: CreateBenchmarkQuestionRequest = { questionText: '', difficulty: 1, expectedPoints: '' };

  ngOnInit() {
    ensureOverlayPolyfills();
    // Read before the loaders run: each one applies the field it owns as it picks its own fallback.
    this.restoreRunSettings();
    this.loadSuites();
    this.loadProfiles();
    this.loadHistory();
    this.setDefaultModelSelections();
    this.checkActiveDifficultyAssessment();
    this.checkActiveRun();
    // The Number of runs field cannot bound itself until the caps arrive, and a series already
    // running must reattach its banner exactly as a single run does.
    this.loadRunLimits();
    this.checkActiveRunSeries();
  }

  checkActiveDifficultyAssessment(): void {
    this.benchmarkService.getActiveDifficultyAssessment().subscribe({
      next: (job) => {
        if (job) {
          this.difficultyJob = job;
          if (job.status === 'Running') {
            this.startDifficultyPolling(job.id);
          }
          this.cdr.detectChanges();
        }
      },
      error: (err) => console.error('Failed to check active difficulty assessment', err)
    });
  }

  /**
   * Switches the visible sub-tab and loads whatever that panel needs. The data
   * loads live here rather than in the template so the tab row carries one
   * statement per handler.
   */
  selectSubTab(tab: 'run' | 'history' | 'multirun' | 'suites' | 'profiles' | 'modelcomparison'): void {
    this.activeSubTab = tab;
    if (tab === 'history') {
      this.loadHistory();
      // The group column needs the groups, and the panel is where a group is built from a
      // selection, so both loads belong to entering the tab rather than to the first click.
      this.loadRunGroups();
    }
    if (tab === 'suites') {
      this.loadSuites();
    }
    if (tab === 'profiles') {
      this.loadProfiles();
    }
    if (tab === 'modelcomparison') {
      // The three lists the picker offers. No comparison is fetched here: an unattended request on
      // tab entry re-prices every entry for a selection the operator has not confirmed.
      this.loadHistory();
      this.loadRunGroups();
      this.loadSuites();
      this.restoreComparisonSelection();
      // The lists arrive asynchronously, so this indexes whatever is already in memory and runs
      // again from onComparisonSuiteChange as the scope narrows.
      this.loadComparabilityIndex();
    }
    // 'multirun' loads nothing here: the panel is the MultiRunComponent's own, and it owns its
    // fetches. Loading them from the host would give that data two owners.
  }

  // ---------------------------------------------------------------------------------------------
  // Model Comparison
  // ---------------------------------------------------------------------------------------------

  /** Completed-or-not runs inside the current suite scope. The picker decides which are selectable. */
  get comparisonRunOptions(): BenchmarkRunSummaryDto[] {
    return this.comparisonSuiteId == null
      ? this.historyRuns
      : this.historyRuns.filter(run => run.benchmarkSuiteId === this.comparisonSuiteId);
  }

  get comparisonGroupOptions(): BenchmarkRunGroupDto[] {
    return this.comparisonSuiteId == null
      ? this.runGroups
      : this.runGroups.filter(group => group.benchmarkSuiteId === this.comparisonSuiteId);
  }

  onComparisonSelectionChange(selection: ModelComparisonSelection): void {
    this.comparisonRunIds = [...selection.runIds];
    this.comparisonGroupIds = [...selection.groupIds];
    this.persistComparisonSelection();
    // The payload on hand describes the previous set of sources, so it is dropped rather than left
    // beside a changed selection. It is also what the wizard reads to know Compare has not run for
    // this selection yet, which is what puts Compare back on its Next button.
    this.comparison = null;
    this.comparisonError = null;
  }

  /**
   * Narrows the offered sources, and drops whatever the new scope no longer offers.
   *
   * Leaving a hidden out-of-scope id selected is how a figure ends up carrying a model the picker
   * does not show. Refetches only if something survives: a request with an empty selection is
   * refused server-side anyway.
   */
  onComparisonSuiteChange(suiteId: number | null): void {
    this.comparisonSuiteId = suiteId;
    this.loadComparabilityIndex();

    const runsInScope = new Set(this.comparisonRunOptions.map(run => run.id));
    const groupsInScope = new Set(this.comparisonGroupOptions.map(group => group.id));
    const runIds = this.comparisonRunIds.filter(id => runsInScope.has(id));
    const groupIds = this.comparisonGroupIds.filter(id => groupsInScope.has(id));
    const dropped = runIds.length !== this.comparisonRunIds.length
      || groupIds.length !== this.comparisonGroupIds.length;

    this.comparisonRunIds = runIds;
    this.comparisonGroupIds = groupIds;
    this.persistComparisonSelection();

    if (runIds.length + groupIds.length > 0) {
      this.runComparison();
    } else if (dropped) {
      // Nothing survives the new scope, so the figures on screen describe a set that is no longer
      // selected. Clearing them is more honest than leaving them beside an empty picker.
      this.comparison = null;
      this.comparisonError = null;
    }
    this.cdr.detectChanges();
  }

  /** Changes the cost arithmetic over an unchanged set, so it refetches at once where one exists. */
  onComparisonPricingBasisChange(basis: BenchmarkModelComparisonPricingBasis): void {
    this.comparisonPricingBasis = basis;
    this.persistComparisonSelection();
    if (this.comparisonRunIds.length + this.comparisonGroupIds.length > 0) {
      this.runComparison();
    }
  }

  clearComparisonSelection(): void {
    this.comparisonRunIds = [];
    this.comparisonGroupIds = [];
    this.comparison = null;
    this.comparisonError = null;
    this.persistComparisonSelection();
    this.cdr.detectChanges();
  }

  runComparison(): void {
    if (this.comparisonRunIds.length + this.comparisonGroupIds.length === 0) {
      this.comparisonError = 'Select at least one run or analysis group to compare.';
      this.cdr.detectChanges();
      return;
    }

    const token = ++this.comparisonToken;
    this.comparisonLoading = true;
    this.comparisonError = null;
    this.cdr.detectChanges();

    this.benchmarkService.compareModels({
      runIds: [...this.comparisonRunIds],
      groupIds: [...this.comparisonGroupIds],
      pricingBasis: this.comparisonPricingBasis
    }).subscribe({
      next: (result) => {
        if (token !== this.comparisonToken) { return; }
        this.comparison = result;
        this.comparisonLoading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        if (token !== this.comparisonToken) { return; }
        this.comparison = null;
        this.comparisonError = err?.error || 'The comparison could not be computed.';
        this.comparisonLoading = false;
        this.cdr.detectChanges();
      }
    });
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
    this.cdr.detectChanges();
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
   * duration, so this is not a silent refusal.
   */
  onComparisonWizardCancel(event: Event): void {
    if (this.comparisonWizard?.exporting) {
      event.preventDefault();
    }
  }

  /** Nothing is torn down here: the mounted content is what reopening is supposed to preserve. */
  onComparisonWizardClose(): void {
    this.cdr.detectChanges();
  }

  /**
   * Loads the comparability index for the runs and groups currently on offer.
   *
   * A failure is non-fatal: the Condition column falls back to a dash and Compare still works. The
   * index is a disclosure aid, and a picker made unusable because an aid failed is worse than one
   * that discloses less.
   */
  private loadComparabilityIndex(): void {
    const runIds = this.comparisonRunOptions.slice(0, 200).map(run => run.id);
    const groupIds = this.comparisonGroupOptions.slice(0, 100).map(group => group.id);
    if (runIds.length + groupIds.length === 0) {
      this.comparabilityIndex = null;
      this.comparabilityIndexLoading = false;
      this.comparabilityIndexError = null;
      return;
    }

    const token = ++this.comparabilityIndexToken;
    this.comparabilityIndexLoading = true;
    this.comparabilityIndexError = null;

    this.benchmarkService.getComparabilityIndex({ runIds, groupIds }).subscribe({
      next: (result) => {
        // A slow response for a suite scope the operator has already left must not overwrite a
        // newer one, exactly as with the comparison itself.
        if (token !== this.comparabilityIndexToken) { return; }
        this.comparabilityIndex = result;
        this.comparabilityIndexLoading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        if (token !== this.comparabilityIndexToken) { return; }
        this.comparabilityIndex = null;
        this.comparabilityIndexLoading = false;
        this.comparabilityIndexError =
          err?.error || 'The comparability index could not be loaded, so the Condition column is ' +
          'unavailable. The comparison itself is unaffected.';
        this.cdr.detectChanges();
      }
    });
  }

  private static readonly COMPARISON_SELECTION_STORAGE_KEY =
    'overseer_admin_benchmark_comparison_selection';

  private persistComparisonSelection(): void {
    try {
      const selection: BenchmarkComparisonSelection = {
        runIds: this.comparisonRunIds,
        groupIds: this.comparisonGroupIds,
        suiteId: this.comparisonSuiteId,
        pricingBasis: this.comparisonPricingBasis
      };
      localStorage.setItem(
        AdminBenchmarkComponent.COMPARISON_SELECTION_STORAGE_KEY, JSON.stringify(selection));
    } catch {
      // Storage throws in private-browsing modes. Failing to remember a selection is not worth
      // surfacing to the operator.
    }
  }

  /**
   * Restores the remembered selection, dropping every id the loaded lists no longer carry.
   *
   * Validated rather than trusted: a run deleted since the last visit would otherwise be sent, and
   * the server would answer `Run(s) not found` for a selection the operator never made. The lists
   * arrive asynchronously, so this runs again on each entry to the tab as they land.
   */
  private restoreComparisonSelection(): void {
    let parsed: unknown;
    try {
      const stored = localStorage.getItem(AdminBenchmarkComponent.COMPARISON_SELECTION_STORAGE_KEY);
      if (!stored) { return; }
      parsed = JSON.parse(stored);
    } catch {
      return;                                   // every default stands
    }

    const raw = parsed as Partial<BenchmarkComparisonSelection> | null;
    if (!raw || typeof raw !== 'object') { return; }

    const ids = (value: unknown): number[] => Array.isArray(value)
      ? value.filter((id): id is number => typeof id === 'number' && Number.isFinite(id))
      : [];

    this.comparisonSuiteId = typeof raw.suiteId === 'number' && Number.isFinite(raw.suiteId)
      ? raw.suiteId
      : null;
    this.comparisonPricingBasis = raw.pricingBasis === 'AsRun' ? 'AsRun' : 'Current';
    this.comparisonRunIds = ids(raw.runIds);
    this.comparisonGroupIds = ids(raw.groupIds);
    this.pruneComparisonSelection();
  }

  /** Drops selected ids the loaded lists do not carry. Called as each list arrives. */
  private pruneComparisonSelection(): void {
    if (this.historyRuns.length > 0) {
      const known = new Set(this.comparisonRunOptions.map(run => run.id));
      this.comparisonRunIds = this.comparisonRunIds.filter(id => known.has(id));
    }
    if (this.runGroups.length > 0) {
      const known = new Set(this.comparisonGroupOptions.map(group => group.id));
      this.comparisonGroupIds = this.comparisonGroupIds.filter(id => known.has(id));
    }
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
    if (changes['systemConfigs']) {
      this.setDefaultModelSelections();
    }
  }

  ngOnDestroy() {
    this.stopPolling();
    this.stopRunElapsedTicker();
    this.stopDetailPolling();
    this.stopDifficultyPolling();
    this.stopSeriesPolling();
    if (this.copiedDiagnosticsTimer) { clearTimeout(this.copiedDiagnosticsTimer); }
    if (this.copiedRunDiagnosticsTimer) { clearTimeout(this.copiedRunDiagnosticsTimer); }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    const target = event.target as HTMLElement;
    if (this.isTestedModelDropdownOpen && !target.closest('.tested-model-selector')) {
      this.isTestedModelDropdownOpen = false;
    }
    if (this.isAssessorModelDropdownOpen && !target.closest('.assessor-model-selector')) {
      this.isAssessorModelDropdownOpen = false;
    }
    if (this.isSecondOpinionModelDropdownOpen && !target.closest('.second-opinion-model-selector')) {
      this.isSecondOpinionModelDropdownOpen = false;
    }
    if (this.isClaimVerifierModelDropdownOpen && !target.closest('.claim-verifier-model-selector')) {
      this.isClaimVerifierModelDropdownOpen = false;
    }
    if (this.isDifficultyAssessorDropdownOpen && !target.closest('.difficulty-assessor-model-selector')) {
      this.isDifficultyAssessorDropdownOpen = false;
    }
    if (this.isRetryAssessorDropdownOpen && !target.closest('.retry-assessor-model-selector')) {
      this.isRetryAssessorDropdownOpen = false;
    }
    if (this.isGenerationModelDropdownOpen && !target.closest('.generation-model-selector')) {
      this.isGenerationModelDropdownOpen = false;
    }
  }

  get benchmarkCapableConfigs(): SystemAiConfigDto[] {
    return this.systemConfigs.filter(c => (c.modelRole & 4) === 4 && c.hasApiKey && c.isEnabled);
  }

  get selectedTestedModel(): SystemAiConfigDto | undefined {
    return this.benchmarkCapableConfigs.find(c => c.id === this.testedConfigId);
  }

  get selectedAssessorModel(): SystemAiConfigDto | undefined {
    return this.benchmarkCapableConfigs.find(c => c.id === this.assessorConfigId);
  }

  get selectedSecondOpinionModel(): SystemAiConfigDto | undefined {
    return this.benchmarkCapableConfigs.find(c => c.id === this.secondOpinionConfigId);
  }

  get selectedClaimVerifierModel(): SystemAiConfigDto | undefined {
    return this.benchmarkCapableConfigs.find(c => c.id === this.claimVerifierConfigId);
  }

  /**
   * The verifier is the candidate model itself. Tools supply the evidence rather than the model's
   * memory, so this is not worthless — but it is the weakest available pairing.
   */
  get showClaimVerifierCandidateAdvisory(): boolean {
    return this.claimVerifierConfigId != null &&
      this.testedConfigId != null &&
      this.claimVerifierConfigId === this.testedConfigId;
  }

  /**
   * The mode that will apply to this run: the operator's override, else the selected profile's
   * default. Inert without a second-opinion assessor — which is the hard gate that silently
   * produced the 2026-09-03 run's zero second verdicts, so the control says so rather than
   * looking configured.
   */
  get secondOpinionMode(): number {
    return this.secondOpinionModeOverride
      ?? this.selectedScoringProfile?.secondOpinionMode
      ?? BenchmarkSecondOpinionMode.Flagged;
  }

  set secondOpinionMode(value: number) {
    this.secondOpinionModeOverride = Number(value);
  }

  /** The outlier sweep is the only thing the delta configures, so nothing else enables it. */
  get outlierDeltaEnabled(): boolean {
    return this.profileForm.secondOpinionMode === BenchmarkSecondOpinionMode.FlaggedAndOutliers;
  }

  get secondOpinionModeDisabled(): boolean {
    return this.secondOpinionConfigId == null;
  }

  get secondOpinionModeHint(): string {
    if (this.secondOpinionModeDisabled) {
      return 'Select a second opinion assessor first — the mode does nothing without one.';
    }
    return this.secondOpinionModeOptions.find(o => o.value === this.secondOpinionMode)?.hint ?? '';
  }

  /**
   * Both graders from one provider. The second verdict is still worth having, but it is a weaker
   * check than a cross-provider one: two models from one family share training data and failure
   * modes, and can agree for reasons that have nothing to do with the answer.
   */
  get showAssessorPairingAdvisory(): boolean {
    const assessor = this.selectedAssessorModel?.provider;
    const second = this.selectedSecondOpinionModel?.provider;
    return !!assessor && !!second && assessor.toLowerCase() === second.toLowerCase();
  }

  /**
   * The assessor differs from the one that graded this suite's last completed run. A suite's runs
   * are comparable to each other only while the grader is the same one, so this fires on exactly
   * the deliberate promotion the staged assessor migration calls for — which is when it should.
   */
  get showAssessorChangeAdvisory(): boolean {
    const previous = this.lastAssessor?.assessorModelConfigurationId;
    return previous != null && this.assessorConfigId != null && previous !== this.assessorConfigId;
  }

  onSelectedSuiteChanged(): void {
    this.loadLastAssessor();
  }

  loadLastAssessor(): void {
    const suiteId = this.selectedSuiteId;
    if (suiteId == null) {
      this.lastAssessor = null;
      return;
    }

    this.benchmarkService.getLastAssessor(suiteId).subscribe({
      next: (dto) => {
        this.lastAssessor = dto;
        this.cdr.detectChanges();
      },
      // An advisory that cannot be computed is simply not shown: the run must not be blocked
      // because a comparison lookup failed.
      error: () => {
        this.lastAssessor = null;
      }
    });
  }

  get selectedDifficultyAssessorModel(): SystemAiConfigDto | undefined {
    return this.benchmarkCapableConfigs.find(c => c.id === this.difficultyAssessorConfigId);
  }

  get selectedRetryAssessorModel(): SystemAiConfigDto | undefined {
    return this.benchmarkCapableConfigs.find(c => c.id === this.retryAssessorConfigId);
  }

  get retryOriginalAssessorAvailable(): boolean {
    return this.selectedRunDetail?.assessorAvailable === true;
  }

  get retryAssessorDiffersFromRun(): boolean {
    return this.retryAssessorConfigId !== this.selectedRunDetail?.assessorModelConfigurationId;
  }

  toggleTestedModelDropdown(event: Event) {
    event.stopPropagation();
    this.isTestedModelDropdownOpen = !this.isTestedModelDropdownOpen;
    if (this.isTestedModelDropdownOpen) {
      this.isAssessorModelDropdownOpen = false;
      this.isSecondOpinionModelDropdownOpen = false;
      this.isClaimVerifierModelDropdownOpen = false;
      this.isDifficultyAssessorDropdownOpen = false;
      this.isRetryAssessorDropdownOpen = false;
    }
  }

  toggleAssessorModelDropdown(event: Event) {
    event.stopPropagation();
    this.isAssessorModelDropdownOpen = !this.isAssessorModelDropdownOpen;
    if (this.isAssessorModelDropdownOpen) {
      this.isTestedModelDropdownOpen = false;
      this.isSecondOpinionModelDropdownOpen = false;
      this.isClaimVerifierModelDropdownOpen = false;
      this.isDifficultyAssessorDropdownOpen = false;
      this.isRetryAssessorDropdownOpen = false;
    }
  }

  toggleSecondOpinionModelDropdown(event: Event) {
    event.stopPropagation();
    this.isSecondOpinionModelDropdownOpen = !this.isSecondOpinionModelDropdownOpen;
    if (this.isSecondOpinionModelDropdownOpen) {
      this.isTestedModelDropdownOpen = false;
      this.isAssessorModelDropdownOpen = false;
      this.isClaimVerifierModelDropdownOpen = false;
      this.isDifficultyAssessorDropdownOpen = false;
      this.isRetryAssessorDropdownOpen = false;
    }
  }

  toggleClaimVerifierModelDropdown(event: Event) {
    event.stopPropagation();
    this.isClaimVerifierModelDropdownOpen = !this.isClaimVerifierModelDropdownOpen;
    if (this.isClaimVerifierModelDropdownOpen) {
      this.isTestedModelDropdownOpen = false;
      this.isAssessorModelDropdownOpen = false;
      this.isSecondOpinionModelDropdownOpen = false;
      this.isDifficultyAssessorDropdownOpen = false;
      this.isRetryAssessorDropdownOpen = false;
    }
  }

  toggleDifficultyAssessorDropdown(event: Event) {
    event.stopPropagation();
    this.isDifficultyAssessorDropdownOpen = !this.isDifficultyAssessorDropdownOpen;
    if (this.isDifficultyAssessorDropdownOpen) {
      this.isTestedModelDropdownOpen = false;
      this.isAssessorModelDropdownOpen = false;
      this.isSecondOpinionModelDropdownOpen = false;
      this.isClaimVerifierModelDropdownOpen = false;
      this.isRetryAssessorDropdownOpen = false;
    }
  }

  toggleRetryAssessorDropdown(event: Event) {
    event.stopPropagation();
    this.isRetryAssessorDropdownOpen = !this.isRetryAssessorDropdownOpen;
    if (this.isRetryAssessorDropdownOpen) {
      this.isTestedModelDropdownOpen = false;
      this.isAssessorModelDropdownOpen = false;
      this.isSecondOpinionModelDropdownOpen = false;
      this.isClaimVerifierModelDropdownOpen = false;
      this.isDifficultyAssessorDropdownOpen = false;
    }
  }

  selectTestedModel(config: SystemAiConfigDto) {
    this.testedConfigId = config.id;
    this.isTestedModelDropdownOpen = false;
  }

  selectAssessorModel(config: SystemAiConfigDto) {
    this.assessorConfigId = config.id;
    this.isAssessorModelDropdownOpen = false;
  }

  selectSecondOpinionModel(config: SystemAiConfigDto | null) {
    this.secondOpinionConfigId = config?.id ?? null;
    this.isSecondOpinionModelDropdownOpen = false;
  }

  selectClaimVerifierModel(config: SystemAiConfigDto | null) {
    this.claimVerifierConfigId = config?.id ?? null;
    this.isClaimVerifierModelDropdownOpen = false;
  }

  selectDifficultyAssessorModel(config: SystemAiConfigDto) {
    this.difficultyAssessorConfigId = config.id;
    this.isDifficultyAssessorDropdownOpen = false;
  }

  selectRetryAssessorModel(config: SystemAiConfigDto) {
    this.retryAssessorConfigId = config.id;
    this.isRetryAssessorDropdownOpen = false;
  }

  formatThinkingLevel(level: string | null | undefined): string {
    if (!level) return 'Default';
    return level.charAt(0).toUpperCase() + level.slice(1);
  }

  showReasoningBadge(mode: string | null | undefined): boolean {
    if (!mode) return false;
    const lower = mode.toLowerCase();
    return lower !== 'default' && lower !== 'standard';
  }

  formatPickerPrice(config: SystemAiConfigDto): string {
    if (config.effectiveInputPricePerMillion == null || config.effectiveOutputPricePerMillion == null) return '';
    const numPipe = new DecimalPipe('en-US');
    const inPrice = numPipe.transform(config.effectiveInputPricePerMillion, '1.2-2');
    const outPrice = numPipe.transform(config.effectiveOutputPricePerMillion, '1.2-2');
    return `$${inPrice}/$${outPrice} per 1M`;
  }

  get estimatedRunCostSoFar(): number | null {
    if (!this.activeRunDetail || !this.activeRunDetail.testedModelConfigurationId) return null;
    const config = this.benchmarkCapableConfigs.find(c => c.id === this.activeRunDetail!.testedModelConfigurationId);
    if (!config || config.effectiveInputPricePerMillion == null || config.effectiveOutputPricePerMillion == null) return null;

    let inputTokens = this.activeRunDetail.totalInputTokens ?? 0;
    const outputTokens = this.activeRunDetail.totalOutputTokens ?? 0;
    const cacheReadTokens = this.activeRunDetail.totalCacheReadTokens ?? 0;

    let cost = 0;
    cost += (outputTokens / 1000000) * config.effectiveOutputPricePerMillion;

    if (config.effectiveCachedInputPricePerMillion != null && cacheReadTokens > 0 && inputTokens >= cacheReadTokens) {
      const uncached = inputTokens - cacheReadTokens;
      cost += (uncached / 1000000) * config.effectiveInputPricePerMillion;
      cost += (cacheReadTokens / 1000000) * config.effectiveCachedInputPricePerMillion;
    } else {
      cost += (inputTokens / 1000000) * config.effectiveInputPricePerMillion;
    }

    return cost;
  }

  /**
   * U3. A dollar amount at the precision the figure deserves.
   *
   * `'1.2-4'` everywhere rendered a $2.5311 run as `$2.5311` while the Markdown report read $2.53,
   * and two different-looking numbers for one run is a defect whichever of them is "right". Four
   * decimals exist for the sub-cent case — a cancelled run costing $0.0007 must not collapse to
   * `$0.00` — so the rule is precision by magnitude: two decimals at or above a dollar, four below.
   */
  formatCostAmount(amount: number | null | undefined): string {
    if (amount == null || !Number.isFinite(amount)) return '-';
    const numPipe = new DecimalPipe('en-US');
    const digits = Math.abs(amount) >= 1 ? '1.2-2' : '1.2-4';
    return `$${numPipe.transform(amount, digits)}`;
  }

  formatRunEstimatedCost(run: BenchmarkRunSummaryDto | BenchmarkRunDetailDto): string {
    return this.formatCostAmount(run.estimatedCost);
  }

  /**
   * H2. The first eight hex characters of a run's candidate system-prompt hash — enough to tell two
   * instruments apart at a glance, and short enough to sit in a table cell. The full hash is on the title.
   */
  shortFingerprint(sha: string | null | undefined): string {
    return sha ? sha.substring(0, 8) : '-';
  }

  /**
   * H2. Whether this run's instrument differs from the next older completed run of the same suite, and
   * which of the three hashes moved.
   *
   * Two runs form a reproduction only if the candidate prompt, the tool guides and the knowledge base all
   * match. The report has stated that rule for some time, but the run list could not support it, so the
   * check was done by hand — and run 13's T8 verification is exactly the case where getting it wrong
   * misattributes a change. Compared client-side over the already-loaded history; no new endpoint.
   *
   * Returns null when there is no older run of the same suite, or when either run is missing a hash: "not
   * recorded" is not "unchanged", and badging it as a change would be a claim the data cannot support.
   *
   * The candidate hash covers the prompt as built, so a run option that changes the prompt text —
   * verboseMode is the usual one — moves it without anything in the instrument having moved. Two runs
   * with different prompt options are not a candidate reproduction on any axis, so the option
   * difference is reported as itself rather than as instrument drift; only runs whose options match
   * can say anything about whether the instrument held still. Where the options cannot be compared —
   * either run missing them, or either one unparseable — the hashes are the only claim available.
   */
  instrumentChangeOf(run: BenchmarkRunSummaryDto):
    { kind: 'instrument' | 'options'; description: string; comparedToRunId: number } | null {
    const index = this.historyRuns.indexOf(run);
    if (index < 0) return null;

    const previous = this.historyRuns
      .slice(index + 1)
      .find(r => r.benchmarkSuiteId === run.benchmarkSuiteId && this.formatStatus(r.status) !== 'Running');
    if (!previous) return null;

    const changedOptions = this.changedPromptOptionKeys(run, previous);
    if (changedOptions && changedOptions.length > 0) {
      return {
        kind: 'options',
        comparedToRunId: previous.id,
        description: `Run options differ from run #${previous.id}: ${changedOptions.join(', ')}. ` +
          'The prompt is built from these, so the candidate hash moves with them. The two runs are not a reproduction.'
      };
    }

    if (!run.candidateSystemPromptSha256 && !run.toolGuidesSha256 && !run.knowledgeBaseHeadSha) {
      return null;
    }

    const moved: string[] = [];
    if (run.candidateSystemPromptSha256 && previous.candidateSystemPromptSha256 &&
        run.candidateSystemPromptSha256 !== previous.candidateSystemPromptSha256) {
      moved.push('candidate system prompt');
    }
    if (run.toolGuidesSha256 && previous.toolGuidesSha256 &&
        run.toolGuidesSha256 !== previous.toolGuidesSha256) {
      moved.push('tool guides');
    }
    if (run.knowledgeBaseHeadSha && previous.knowledgeBaseHeadSha &&
        run.knowledgeBaseHeadSha !== previous.knowledgeBaseHeadSha) {
      moved.push('knowledge base');
    }

    if (moved.length === 0) return null;

    return {
      kind: 'instrument',
      comparedToRunId: previous.id,
      description: `Changed since run #${previous.id}: ${moved.join(', ')}. The two runs are a controlled pair, not a reproduction.`
    };
  }

  /**
   * The prompt-option keys whose values differ between two runs, or null when the two records cannot
   * be compared at all — either run missing its options, or either one unparseable. An empty array
   * means the comparison was made and the options match.
   */
  private changedPromptOptionKeys(run: BenchmarkRunSummaryDto, previous: BenchmarkRunSummaryDto): string[] | null {
    const current = this.parsePromptOptions(run.candidatePromptOptionsJson);
    const older = this.parsePromptOptions(previous.candidatePromptOptionsJson);
    if (!current || !older) return null;

    const keys = Array.from(new Set([...Object.keys(current), ...Object.keys(older)])).sort();
    return keys.filter(key => JSON.stringify(current[key] ?? null) !== JSON.stringify(older[key] ?? null));
  }

  private parsePromptOptions(json: string | null | undefined): Record<string, unknown> | null {
    if (!json) return null;
    try {
      const parsed = JSON.parse(json);
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return null;
      return parsed as Record<string, unknown>;
    } catch {
      return null;
    }
  }

  formatSecondOpinionMode(mode: number | null | undefined): string {
    const resolvedMode = mode ?? this.secondOpinionMode;
    const option = this.secondOpinionModeOptions.find(o => o.value === resolvedMode);
    return option && option.value !== BenchmarkSecondOpinionMode.Off ? option.label : '';
  }

  secondOpinionModeHintOf(mode: number | null | undefined): string {
    const resolvedMode = mode ?? this.secondOpinionMode;
    const option = this.secondOpinionModeOptions.find(o => o.value === resolvedMode);
    return option?.hint ?? '';
  }

  private setDefaultModelSelections() {
    const benchmarkModels = this.benchmarkCapableConfigs;
    if (benchmarkModels.length > 0) {
      // A remembered configuration wins over the first one, but only while it still qualifies:
      // benchmarkCapableConfigs filters on the Benchmark role bit, hasApiKey and isEnabled, so one that
      // was disabled or lost its key falls back rather than leaving a selection the server would reject.
      const remembered = this.pendingRunSettings;
      const qualifies = (id: number | null | undefined): boolean =>
        id != null && benchmarkModels.some(m => m.id === id);

      if (qualifies(remembered?.testedConfigId)) {
        this.testedConfigId = remembered!.testedConfigId;
      } else if (!this.testedConfigId || !benchmarkModels.some(m => m.id === this.testedConfigId)) {
        this.testedConfigId = benchmarkModels[0].id;
      }

      if (qualifies(remembered?.assessorConfigId)) {
        this.assessorConfigId = remembered!.assessorConfigId;
      } else if (!this.assessorConfigId || !benchmarkModels.some(m => m.id === this.assessorConfigId)) {
        this.assessorConfigId = benchmarkModels[0].id;
      }

      // The two optional roles restore to null when their configuration no longer qualifies, which is the
      // same as "not selected" and is what the run request already means by a null id.
      if (remembered) {
        if (remembered.secondOpinionConfigId != null) {
          this.secondOpinionConfigId = qualifies(remembered.secondOpinionConfigId)
            ? remembered.secondOpinionConfigId
            : null;
        }
        if (remembered.claimVerifierConfigId != null) {
          this.claimVerifierConfigId = qualifies(remembered.claimVerifierConfigId)
            ? remembered.claimVerifierConfigId
            : null;
        }
      }
    } else {
      this.testedConfigId = null;
      this.assessorConfigId = null;
    }

    // Only counts as applied when there was actually a list to validate against: called from ngOnInit
    // before the systemConfigs input has arrived, this method has done nothing.
    if (benchmarkModels.length > 0) {
      this.markRunSettingsApplied('configs');
    }
  }

  // --- Scoring Profiles Management ---

  loadProfiles() {
    this.loadingProfiles = true;
    this.benchmarkService.getScoringProfiles().subscribe({
      next: (data) => {
        this.scoringProfiles = data;
        this.loadingProfiles = false;
        // A remembered profile wins over the default one, but only if it still exists.
        const rememberedProfileId = this.pendingRunSettings?.scoringProfileId ?? null;
        const defaultProf = this.scoringProfiles.find(p => p.isDefault);
        if (rememberedProfileId != null && this.scoringProfiles.some(p => p.id === rememberedProfileId)) {
          this.selectedScoringProfileId = rememberedProfileId;
        } else if (defaultProf && !this.selectedScoringProfileId) {
          this.selectedScoringProfileId = defaultProf.id;
        } else if (this.scoringProfiles.length > 0 && !this.selectedScoringProfileId) {
          this.selectedScoringProfileId = this.scoringProfiles[0].id;
        }
        this.markRunSettingsApplied('profile');
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingProfiles = false;
        console.error('Failed to load scoring profiles', err);
        this.cdr.detectChanges();
      }
    });
  }

  formatProfileOption(profile: BenchmarkScoringProfileDto): string {
    const cleanName = (profile.name || '').replace(/\s*\(Default\)$/i, '').trim();
    return profile.isDefault ? `${cleanName} (Default)` : cleanName;
  }

  openManageProfiles() {
    this.selectSubTab('profiles');
  }

  openCreateProfile() {
    this.editingProfileId = null;
    this.profileValidationErrors = [];
    this.profileForm = {
      name: '',
      isDefault: false,
      weightAccuracy: 0.55,
      weightCompleteness: 0.25,
      weightConciseness: 0.10,
      weightReadability: 0.10,
      levelScoresJson: '[1, 15, 35, 55, 72, 87, 100]',
      criticalErrorCeiling: 25,
      secondOpinionQualityThreshold: 50,
      secondOpinionMode: BenchmarkSecondOpinionMode.Flagged,
      secondOpinionOutlierDeltaPoints: 25,
      secondOpinionBlind: true,
      speedTargetMs: 15000,
      speedDecayK: 20.0,
      speedDifficultyScaling: 1.0,
      maxParallelQuestions: 1
    };
    this.scoringProfileFormDialog?.nativeElement.showModal();
  }

  openEditProfile(profile: BenchmarkScoringProfileDto) {
    this.editingProfileId = profile.id;
    this.profileValidationErrors = [];
    this.profileForm = {
      name: profile.name,
      isDefault: profile.isDefault,
      weightAccuracy: profile.weightAccuracy,
      weightCompleteness: profile.weightCompleteness,
      weightConciseness: profile.weightConciseness,
      weightReadability: profile.weightReadability,
      levelScoresJson: profile.levelScoresJson,
      criticalErrorCeiling: profile.criticalErrorCeiling,
      secondOpinionQualityThreshold: profile.secondOpinionQualityThreshold ?? 50,
      secondOpinionMode: profile.secondOpinionMode ?? BenchmarkSecondOpinionMode.Flagged,
      secondOpinionOutlierDeltaPoints: profile.secondOpinionOutlierDeltaPoints ?? 25,
      secondOpinionBlind: profile.secondOpinionBlind ?? true,
      speedTargetMs: profile.speedTargetMs,
      speedDecayK: profile.speedDecayK,
      speedDifficultyScaling: profile.speedDifficultyScaling,
      maxParallelQuestions: profile.maxParallelQuestions
    };
    this.scoringProfileFormDialog?.nativeElement.showModal();
  }

  saveProfile() {
    this.profileValidationErrors = [];
    if (!this.profileForm.name.trim()) {
      this.profileValidationErrors.push('Profile name is required.');
      return;
    }

    // Mirrors the server-side range in BenchmarkScoringProfileService.ValidateProfile, so a
    // plainly out-of-range value is reported without a round trip. The server remains the
    // authority; everything else on this form is validated there only.
    const scaling = this.profileForm.speedDifficultyScaling;
    if (scaling == null || !isFinite(scaling) || scaling < 0 || scaling > 5) {
      this.profileValidationErrors.push('Speed difficulty scaling must be between 0.0 and 5.0.');
      return;
    }

    // 0 is meaningful: it disables the score trigger and leaves second opinions to critical
    // errors alone. Mirrors BenchmarkScoringProfileService.ValidateProfile.
    const threshold = this.profileForm.secondOpinionQualityThreshold;
    if (threshold == null || threshold < 0 || threshold > 100) {
      this.profileValidationErrors.push('Second opinion threshold must be between 0 and 100.');
      return;
    }

    // Only meaningful under FlaggedAndOutliers, and a zero there would disable the sweep while
    // the mode claims to run it. Mirrors BenchmarkScoringProfileService.ValidateProfile.
    if (this.profileForm.secondOpinionMode === BenchmarkSecondOpinionMode.FlaggedAndOutliers) {
      const delta = this.profileForm.secondOpinionOutlierDeltaPoints;
      if (delta == null || delta <= 0 || delta > 100) {
        this.profileValidationErrors.push('Outlier delta must be between 1 and 100 when the second opinion mode is "Flagged answers and statistical outliers".');
        return;
      }
    }

    if (this.editingProfileId) {
      this.benchmarkService.updateScoringProfile(this.editingProfileId, this.profileForm as UpdateBenchmarkScoringProfileRequest).subscribe({
        next: () => {
          this.scoringProfileFormDialog?.nativeElement.close();
          this.loadProfiles();
        },
        error: (err) => {
          if (err?.error?.errors) {
            this.profileValidationErrors = err.error.errors;
          } else {
            this.profileValidationErrors = [err?.error || 'Failed to update profile.'];
          }
          this.cdr.detectChanges();
        }
      });
    } else {
      this.benchmarkService.createScoringProfile(this.profileForm).subscribe({
        next: (created) => {
          this.scoringProfileFormDialog?.nativeElement.close();
          this.loadProfiles();
          this.selectedScoringProfileId = created.id;
        },
        error: (err) => {
          if (err?.error?.errors) {
            this.profileValidationErrors = err.error.errors;
          } else {
            this.profileValidationErrors = [err?.error || 'Failed to create profile.'];
          }
          this.cdr.detectChanges();
        }
      });
    }
  }

  setDefaultProfile(profileId: number) {
    this.benchmarkService.setDefaultScoringProfile(profileId).subscribe({
      next: () => this.loadProfiles(),
      error: (err) => console.error('Failed to set default profile', err)
    });
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

  deleteProfile(profileId: number) {
    const profile = this.scoringProfiles.find(p => p.id === profileId);
    const name = profile ? `"${profile.name}"` : 'this scoring profile';
    this.openConfirmDialog({
      title: 'Delete Scoring Profile',
      message: `Are you sure you want to delete ${name}?`,
      dangerNotice: 'This action is permanent and cannot be undone.',
      buttonText: 'Delete Profile',
      buttonClass: 'btn-gh btn-gh-delete',
      action: () => {
        this.benchmarkService.deleteScoringProfile(profileId).subscribe({
          next: () => this.loadProfiles(),
          error: (err) => console.error('Failed to delete profile', err)
        });
      }
    });
  }

  // --- Suites Management ---

  loadSuites() {
    this.loadingSuites = true;
    this.benchmarkService.getSuites().subscribe({
      next: (data) => {
        this.suites = data;
        this.loadingSuites = false;
        // A remembered suite wins over the first one, but only if it still exists.
        const rememberedSuiteId = this.pendingRunSettings?.suiteId ?? null;
        if (rememberedSuiteId != null && this.suites.some(s => s.id === rememberedSuiteId)) {
          this.selectedSuiteId = rememberedSuiteId;
        } else if (this.suites.length > 0 && (!this.selectedSuiteId || !this.suites.some(s => s.id === this.selectedSuiteId))) {
          this.selectedSuiteId = this.suites[0].id;
        }
        this.markRunSettingsApplied('suite');
        this.loadLastAssessor();
        this.loadAllFootprints();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingSuites = false;
        console.error('Failed to load benchmark suites', err);
        this.cdr.detectChanges();
      }
    });
  }

  loadAllFootprints() {
    for (const suite of this.suites) {
      this.benchmarkService.getSuiteRunsFootprint(suite.id).subscribe({
        next: (fp) => {
          this.footprints[suite.id] = fp;
          this.cdr.detectChanges();
        },
        error: (err) => console.error(`Failed to load footprint for suite ${suite.id}`, err)
      });
    }
  }

  openBulkDeleteDialog(suite: BenchmarkSuiteDto) {
    this.suiteForBulkDelete = suite;
    this.bulkDeleteDialog?.nativeElement.showModal();
  }

  closeBulkDeleteDialog() {
    this.suiteForBulkDelete = null;
    this.bulkDeleteDialog?.nativeElement.close();
  }

  confirmDeleteSuiteRuns() {
    if (!this.suiteForBulkDelete) return;
    const suiteId = this.suiteForBulkDelete.id;
    this.deletingSuiteRuns = true;
    this.actionErrorMessage = null;

    this.benchmarkService.deleteSuiteRuns(suiteId).subscribe({
      next: () => {
        this.deletingSuiteRuns = false;
        this.closeBulkDeleteDialog();
        this.loadHistory();
        this.loadAllFootprints();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.deletingSuiteRuns = false;
        this.actionErrorMessage = err?.error || 'Failed to delete suite runs.';
        this.cdr.detectChanges();
      }
    });
  }

  openCreateSuite() {
    this.editingSuiteId = null;
    this.suiteForm = { name: '', description: '' };
    this.suiteDialog?.nativeElement.showModal();
  }

  openEditSuite(suite: BenchmarkSuiteDto) {
    this.editingSuiteId = suite.id;
    this.suiteForm = { name: suite.name, description: suite.description };
    this.suiteDialog?.nativeElement.showModal();
  }

  saveSuite() {
    if (!this.suiteForm.name.trim()) return;

    if (this.editingSuiteId) {
      this.benchmarkService.updateSuite(this.editingSuiteId, this.suiteForm).subscribe({
        next: () => {
          this.suiteDialog?.nativeElement.close();
          this.loadSuites();
        },
        error: (err) => console.error('Failed to update suite', err)
      });
    } else {
      this.benchmarkService.createSuite(this.suiteForm).subscribe({
        next: (created) => {
          this.suiteDialog?.nativeElement.close();
          this.loadSuites();
          this.selectedSuiteId = created.id;
        },
        error: (err) => console.error('Failed to create suite', err)
      });
    }
  }

  deleteSuite(id: number) {
    const suite = this.suites.find(s => s.id === id);
    const name = suite ? `"${suite.name}"` : 'this benchmark suite';
    this.openConfirmDialog({
      title: 'Delete Benchmark Suite',
      message: `Are you sure you want to delete ${name}?`,
      dangerNotice: 'This action is permanent and will delete the suite and all its questions.',
      buttonText: 'Delete Suite',
      buttonClass: 'btn-gh btn-gh-delete',
      action: () => {
        this.benchmarkService.deleteSuite(id).subscribe({
          next: () => this.loadSuites(),
          error: (err) => console.error('Failed to delete suite', err)
        });
      }
    });
  }

  duplicateSuite(id: number) {
    this.benchmarkService.duplicateSuite(id).subscribe({
      next: () => this.loadSuites(),
      error: (err) => console.error('Failed to duplicate suite', err)
    });
  }

  importDefaultSuite() {
    this.benchmarkService.importDefaultSuite().subscribe({
      next: () => this.loadSuites(),
      error: (err) => console.error('Failed to import default suite', err)
    });
  }

  // --- Difficulty Assessor Dialog Actions ---

  isDifficultyAssessorDialogOpen = false;

  openDifficultyAssessorDialog(suite?: BenchmarkSuiteDto | null, question: BenchmarkQuestionDto | null = null) {
    this.actionErrorMessage = null;
    this.difficultyDialogError = null;
    this.isDifficultyAssessorDialogOpen = true;

    if (this.difficultyJobIsRunning) {
      this.difficultyDialogPhase = 'progress';
    } else {
      if (suite) {
        this.suiteForDifficultyAssessment = suite;
      }
      this.difficultyAssessmentScope = question == null ? 'suite' : 'question';
      this.questionIdForDifficultyAssessment = question?.id ?? null;
      this.isDifficultyAssessorDropdownOpen = false;
      this.difficultyAssessorConfigId = this.resolveDefaultDifficultyAssessor(question);
      this.difficultyDialogPhase = 'select';
    }

    this.difficultyAssessorDialog?.nativeElement.showModal();
  }

  closeDifficultyAssessorDialog() {
    this.isDifficultyAssessorDialogOpen = false;
    this.difficultyAssessorDialog?.nativeElement.close();
    this.isDifficultyAssessorDropdownOpen = false;
    if (this.difficultyJobIsTerminal) {
      this.difficultyDialogPhase = 'select';
    }
  }

  resolveDefaultDifficultyAssessor(question: BenchmarkQuestionDto | null): number | null {
    if (question?.assessedDifficultyModelConfigurationId && this.benchmarkCapableConfigs.some(c => c.id === question.assessedDifficultyModelConfigurationId)) {
      return question.assessedDifficultyModelConfigurationId;
    }

    if (this.currentSuiteForQuestions?.id === this.suiteForDifficultyAssessment?.id && this.questions.length > 0) {
      const assessed = this.questions
        .filter(q => q.assessedDifficultyModelConfigurationId != null && q.assessedDifficultyAtUtc != null && this.benchmarkCapableConfigs.some(c => c.id === q.assessedDifficultyModelConfigurationId))
        .sort((a, b) => new Date(b.assessedDifficultyAtUtc!).getTime() - new Date(a.assessedDifficultyAtUtc!).getTime());
      if (assessed.length > 0 && assessed[0].assessedDifficultyModelConfigurationId != null) {
        return assessed[0].assessedDifficultyModelConfigurationId;
      }
    }

    if (this.assessorConfigId && this.benchmarkCapableConfigs.some(c => c.id === this.assessorConfigId)) {
      return this.assessorConfigId;
    }

    return this.benchmarkCapableConfigs[0]?.id ?? null;
  }

  confirmDifficultyAssessment() {
    if (!this.difficultyAssessorConfigId) return;
    this.difficultyJobStarting = true;
    this.difficultyDialogError = null;

    const suiteId = this.suiteForDifficultyAssessment?.id || (this.difficultyJob?.suiteId ?? 0);
    const questionIds = this.difficultyAssessmentScope === 'question' && this.questionIdForDifficultyAssessment != null
      ? [this.questionIdForDifficultyAssessment]
      : null;

    this.benchmarkService.startDifficultyAssessment({
      suiteId,
      questionIds,
      assessorModelConfigurationId: this.difficultyAssessorConfigId
    }).subscribe({
      next: (res) => {
        this.difficultyJobStarting = false;
        this.difficultyDialogPhase = 'progress';
        this.isDifficultyAssessorDropdownOpen = false;
        this.startDifficultyPolling(res.jobId);
        this.cdr.detectChanges();
        this.difficultyProgressHeading?.nativeElement.focus();
      },
      error: (err) => {
        this.difficultyJobStarting = false;
        if (err.status === 409 && err.error) {
          this.difficultyJob = err.error as DifficultyAssessmentJobDto;
          this.difficultyDialogPhase = 'progress';
          this.isDifficultyAssessorDropdownOpen = false;
          this.startDifficultyPolling(this.difficultyJob.id);
          this.cdr.detectChanges();
          this.difficultyProgressHeading?.nativeElement.focus();
        } else {
          this.difficultyDialogError = err?.error || 'Failed to start difficulty assessment.';
          this.cdr.detectChanges();
        }
      }
    });
  }

  startDifficultyPolling(jobId: string) {
    this.stopDifficultyPolling();

    this.pollDifficultyJob(jobId);

    this.difficultyPollInterval = setInterval(() => {
      if (typeof document !== 'undefined' && document.hidden) {
        return;
      }
      this.pollDifficultyJob(jobId);
    }, 1500);

    if (typeof document !== 'undefined') {
      this.visibilityChangeHandler = () => {
        if (!document.hidden) {
          this.pollDifficultyJob(jobId);
        }
      };
      document.addEventListener('visibilitychange', this.visibilityChangeHandler);
    }
  }

  private pollDifficultyJob(jobId: string) {
    this.benchmarkService.getDifficultyAssessment(jobId).subscribe({
      next: (job) => {
        this.difficultyJob = job;
        if (job.status !== 'Running') {
          this.stopDifficultyPolling();
          this.loadSuites();
          if (this.currentSuiteForQuestions) {
            this.loadQuestions(this.currentSuiteForQuestions.id);
          }
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        console.error('Failed to poll difficulty job', err);
      }
    });
  }

  stopDifficultyPolling() {
    if (this.difficultyPollInterval) {
      clearInterval(this.difficultyPollInterval);
      this.difficultyPollInterval = null;
    }
    if (this.visibilityChangeHandler && typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.visibilityChangeHandler);
      this.visibilityChangeHandler = null;
    }
  }

  terminateDifficultyAssessment() {
    if (!this.difficultyJob) return;
    this.terminatingDifficultyJob = true;
    this.benchmarkService.cancelDifficultyAssessment(this.difficultyJob.id).subscribe({
      next: () => {
        this.terminatingDifficultyJob = false;
        this.pollDifficultyJob(this.difficultyJob!.id);
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.terminatingDifficultyJob = false;
        this.actionErrorMessage = err?.error || 'Failed to cancel assessment.';
        this.cdr.detectChanges();
      }
    });
  }

  assessAgain() {
    this.difficultyDialogPhase = 'select';
    this.cdr.detectChanges();
  }

  retryFailedQuestions() {
    if (!this.difficultyJob || this.failedDifficultyItems.length === 0) return;
    this.difficultyJobStarting = true;
    this.difficultyDialogError = null;

    const failedIds = this.failedDifficultyItems.map(i => i.questionId);
    this.benchmarkService.startDifficultyAssessment({
      suiteId: this.difficultyJob.suiteId,
      questionIds: failedIds,
      assessorModelConfigurationId: this.difficultyJob.assessorConfigId
    }).subscribe({
      next: (res) => {
        this.difficultyJobStarting = false;
        this.difficultyDialogPhase = 'progress';
        this.startDifficultyPolling(res.jobId);
        this.cdr.detectChanges();
        this.difficultyProgressHeading?.nativeElement.focus();
      },
      error: (err) => {
        this.difficultyJobStarting = false;
        if (err.status === 409 && err.error) {
          this.difficultyJob = err.error as DifficultyAssessmentJobDto;
          this.difficultyDialogPhase = 'progress';
          this.startDifficultyPolling(this.difficultyJob.id);
          this.cdr.detectChanges();
          this.difficultyProgressHeading?.nativeElement.focus();
        } else {
          this.difficultyDialogError = err?.error || 'Failed to retry failed questions.';
          this.cdr.detectChanges();
        }
      }
    });
  }

  // --- Questions Management ---

  openManageQuestions(suite: BenchmarkSuiteDto) {
    this.currentSuiteForQuestions = suite;
    this.questionsDialog?.nativeElement.showModal();
    this.loadQuestions(suite.id);
  }

  loadQuestions(suiteId: number) {
    this.loadingQuestions = true;
    this.benchmarkService.getQuestions(suiteId).subscribe({
      next: (data) => {
        this.questions = data;
        this.loadingQuestions = false;

        if (this.pendingQuestionEditId != null) {
          const question = this.questions.find(q => q.id === this.pendingQuestionEditId);
          this.pendingQuestionEditId = null;
          if (question) {
            this.openEditQuestion(question);
          }
        }

        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingQuestions = false;
        this.pendingQuestionEditId = null;
        console.error('Failed to load questions', err);
        this.cdr.detectChanges();
      }
    });
  }

  openCreateQuestion() {
    this.editingQuestionId = null;
    this.questionForm = { questionText: '', difficulty: 1, expectedPoints: '' };
    this.questionFormDialog?.nativeElement.showModal();
  }

  openEditQuestion(q: BenchmarkQuestionDto) {
    this.editingQuestionId = q.id;
    this.questionForm = {
      questionText: q.questionText,
      difficulty: typeof q.difficulty === 'number' ? q.difficulty : this.parseDifficulty(q.difficulty),
      expectedPoints: q.expectedPoints || ''
    };
    this.questionFormDialog?.nativeElement.showModal();
  }

  saveQuestion() {
    if (!this.questionForm.questionText.trim() || !this.currentSuiteForQuestions) return;

    if (this.editingQuestionId) {
      this.benchmarkService.updateQuestion(this.editingQuestionId, this.questionForm).subscribe({
        next: () => {
          this.questionFormDialog?.nativeElement.close();
          this.loadQuestions(this.currentSuiteForQuestions!.id);
          this.loadSuites();
        },
        error: (err) => console.error('Failed to update question', err)
      });
    } else {
      this.benchmarkService.createQuestion(this.currentSuiteForQuestions.id, this.questionForm).subscribe({
        next: () => {
          this.questionFormDialog?.nativeElement.close();
          this.loadQuestions(this.currentSuiteForQuestions!.id);
          this.loadSuites();
        },
        error: (err) => console.error('Failed to create question', err)
      });
    }
  }

  deleteQuestion(id: number) {
    this.openConfirmDialog({
      title: 'Delete Benchmark Question',
      message: 'Are you sure you want to delete this question?',
      dangerNotice: 'This action is permanent and cannot be undone.',
      buttonText: 'Delete Question',
      buttonClass: 'btn-gh btn-gh-delete',
      action: () => {
        this.benchmarkService.deleteQuestion(id).subscribe({
          next: () => {
            if (this.currentSuiteForQuestions) {
              this.loadQuestions(this.currentSuiteForQuestions.id);
              this.loadSuites();
            }
          },
          error: (err) => console.error('Failed to delete question', err)
        });
      }
    });
  }

  // --- Question Drag & Drop Reordering ---

  onQuestionDragStart(event: DragEvent, index: number) {
    if (event.dataTransfer) {
      event.dataTransfer.setData('text/plain', JSON.stringify({ index }));
      event.dataTransfer.effectAllowed = 'move';
      const target = (event.target as HTMLElement).closest('.question-list-item') as HTMLElement;
      if (target) {
        setTimeout(() => target.classList.add('dragging'), 0);
      }
    }
  }

  onQuestionDragEnd(event: DragEvent) {
    const target = (event.target as HTMLElement).closest('.question-list-item') as HTMLElement;
    if (target) {
      target.classList.remove('dragging');
    }
    const items = document.querySelectorAll('.question-list-item');
    items.forEach(item => item.classList.remove('drag-over', 'drag-over-top', 'drag-over-bottom'));
  }

  onQuestionDragOver(event: DragEvent) {
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
    const targetItem = (event.target as HTMLElement).closest('.question-list-item');
    if (targetItem) {
      const rect = targetItem.getBoundingClientRect();
      const midY = rect.top + rect.height / 2;
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
      if (event.clientY < midY) {
        targetItem.classList.add('drag-over-top');
      } else {
        targetItem.classList.add('drag-over-bottom');
      }
    }
  }

  onQuestionDragLeave(event: DragEvent) {
    const targetItem = (event.target as HTMLElement).closest('.question-list-item');
    if (targetItem) {
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
    }
  }

  onQuestionDrop(event: DragEvent, dropIndex: number) {
    event.preventDefault();
    const targetItem = (event.target as HTMLElement).closest('.question-list-item');
    if (targetItem) {
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
    }

    if (event.dataTransfer && this.currentSuiteForQuestions) {
      const dataStr = event.dataTransfer.getData('text/plain');
      if (dataStr) {
        try {
          const data = JSON.parse(dataStr);
          const dragIndex = data.index;
          if (dragIndex !== undefined && dragIndex !== dropIndex) {
            const item = this.questions[dragIndex];
            this.questions.splice(dragIndex, 1);

            let insertIndex = dropIndex;
            if (targetItem) {
              const rect = targetItem.getBoundingClientRect();
              const midY = rect.top + rect.height / 2;
              if (event.clientY >= midY) {
                insertIndex++;
              }
              if (dragIndex < dropIndex && event.clientY < midY) {
                // Dragging down but dropped on top half
              } else if (dragIndex < dropIndex) {
                insertIndex--;
              }
            }

            this.questions.splice(insertIndex, 0, item);

            // Re-assign order numbers locally
            this.questions.forEach((q, idx) => q.orderIndex = idx + 1);

            const orderedIds = this.questions.map(q => q.id);
            this.benchmarkService.reorderQuestions(this.currentSuiteForQuestions.id, orderedIds).subscribe({
              next: () => this.loadQuestions(this.currentSuiteForQuestions!.id),
              error: (err) => console.error('Failed to reorder questions', err)
            });
          }
        } catch (e) {
          console.error('Failed to parse drag data', e);
        }
      }
    }
  }

  // --- Run setting recall ---
  //
  // Follows AdminComponent.persistConfigFilter / restoreConfigFilter: a private static key, try/catch
  // around every localStorage access because it throws in private-browsing modes, and a whitelisting
  // restore that drops anything unrecognised.
  //
  // Every restored id is validated against the list it must come from — benchmarkCapableConfigs filters on
  // the Benchmark role bit, hasApiKey and isEnabled — so a configuration that was disabled, lost its key or
  // lost its role falls back to the existing default rather than leaving a dangling selection that fails
  // server-side at run time.

  private static readonly RUN_SETTINGS_STORAGE_KEY = 'overseer_admin_benchmark_run_settings';

  /**
   * The stored settings, read once in ngOnInit and applied by whichever loader owns each field, because
   * the restore cannot run before the data it validates against exists: suites arrive from loadSuites,
   * profiles from loadProfiles, and configurations from the systemConfigs input via ngOnChanges.
   *
   * Cleared once applied, so a later ngOnChanges cannot resurrect a stale selection over one the operator
   * has since made by hand.
   */
  private pendingRunSettings: BenchmarkRunSettings | null = null;

  /**
   * Saved in startBenchmark before the request is sent: the operator's choices are worth remembering
   * whether or not the server accepts the run.
   *
   * acknowledgeSameProvider is deliberately not persisted. It is a per-run safety acknowledgement, and
   * silently remembering it would defeat the warning dialog it exists to gate. Neither are the
   * difficulty-assessor, retry-assessor, generation-model or calibration-assessor selections, which are
   * not part of setting up a run.
   */
  private persistRunSettings(): void {
    try {
      const settings: BenchmarkRunSettings = {
        suiteId: this.selectedSuiteId,
        testedConfigId: this.testedConfigId,
        assessorConfigId: this.assessorConfigId,
        secondOpinionConfigId: this.secondOpinionConfigId,
        claimVerifierConfigId: this.claimVerifierConfigId,
        // The override, not the getter: a run left on the profile default must keep following the
        // profile, and persisting the resolved value would freeze it at whatever the profile said today.
        secondOpinionMode: this.secondOpinionModeOverride,
        scoringProfileId: this.selectedScoringProfileId,
        verboseMode: this.candidateVerboseMode,
        runCount: this.effectiveRunCount
      };
      localStorage.setItem(
        AdminBenchmarkComponent.RUN_SETTINGS_STORAGE_KEY, JSON.stringify(settings));
    } catch {
      // Storage throws in private-browsing modes. Failing to remember a selection is not worth
      // surfacing to the operator.
    }
  }

  /** Reads the stored blob into pendingRunSettings, and restores the fields no loader owns. */
  private restoreRunSettings(): void {
    let parsed: unknown;
    try {
      const stored = localStorage.getItem(AdminBenchmarkComponent.RUN_SETTINGS_STORAGE_KEY);
      if (!stored) { return; }
      parsed = JSON.parse(stored);
    } catch {
      return;                                   // every default stands
    }

    const raw = parsed as Partial<BenchmarkRunSettings> | null;
    if (!raw || typeof raw !== 'object') { return; }

    const num = (v: unknown): number | null => (typeof v === 'number' && Number.isFinite(v)) ? v : null;

    this.pendingRunSettings = {
      suiteId: num(raw.suiteId),
      testedConfigId: num(raw.testedConfigId),
      assessorConfigId: num(raw.assessorConfigId),
      secondOpinionConfigId: num(raw.secondOpinionConfigId),
      claimVerifierConfigId: num(raw.claimVerifierConfigId),
      secondOpinionMode: num(raw.secondOpinionMode),
      scoringProfileId: num(raw.scoringProfileId),
      verboseMode: typeof raw.verboseMode === 'boolean' ? raw.verboseMode : null,
      runCount: num(raw.runCount)
    };

    // These need no list to validate against, so they restore immediately.
    if (this.pendingRunSettings.verboseMode !== null) {
      this.candidateVerboseMode = this.pendingRunSettings.verboseMode;
    }
    const count = this.pendingRunSettings.runCount;
    if (count !== null && count >= 1) {
      const intCount = Math.floor(count);
      this.runCount = (this.maxRunCountPerSeries != null && intCount > this.maxRunCountPerSeries)
        ? this.maxRunCountPerSeries
        : intCount;
    }
    const mode = this.pendingRunSettings.secondOpinionMode;
    if (mode !== null && this.secondOpinionModeOptions.some(o => o.value === mode)) {
      this.secondOpinionModeOverride = mode;
    }
  }

  /**
   * Which of the three list-backed fields have been applied. The loaders complete in whatever order their
   * requests return, and setDefaultModelSelections runs from ngOnInit before either has answered, so the
   * stored blob can only be dropped once all three have had their turn — dropping it as soon as any one of
   * them finishes would leave the others falling back to their defaults.
   */
  private runSettingsApplied = { suite: false, profile: false, configs: false };

  /** Marks one part applied, and drops the stored blob once all three are. */
  private markRunSettingsApplied(part: 'suite' | 'profile' | 'configs'): void {
    if (!this.pendingRunSettings) return;
    this.runSettingsApplied[part] = true;
    const done = this.runSettingsApplied;
    if (done.suite && done.profile && done.configs) {
      // Cleared so a later ngOnChanges cannot resurrect a stale selection over one the operator has
      // since made by hand.
      this.pendingRunSettings = null;
    }
  }

  // --- Run Execution ---

  startBenchmark(acknowledgeSameProvider: boolean = false) {
    if (!this.selectedSuiteId || !this.testedConfigId || !this.assessorConfigId) return;

    this.startingRun = true;
    this.runErrorMessage = null;
    this.seriesErrorMessage = null;

    const req: StartBenchmarkRunRequest = {
      suiteId: this.selectedSuiteId,
      testedModelConfigurationId: this.testedConfigId,
      assessorModelConfigurationId: this.assessorConfigId,
      secondOpinionAssessorModelConfigurationId: this.secondOpinionConfigId,
      // Sent only when an assessor is selected: without one the mode is inert, and sending Off
      // would be indistinguishable from "the operator chose Never".
      secondOpinionMode: this.secondOpinionConfigId != null ? this.secondOpinionMode : null,
      claimVerifierModelConfigurationId: this.claimVerifierConfigId,
      verboseMode: this.candidateVerboseMode,
      scoringProfileId: this.selectedScoringProfileId,
      acknowledgeSameProvider: acknowledgeSameProvider
    };

    // Before the request, not after it: the operator's choices are worth remembering whether or not the
    // server accepts the run.
    this.persistRunSettings();

    // The one branch multi-run adds to the start path. At 1 the request is posted to the same
    // endpoint with the same body it has always carried — runCount and allowCapWait are not even
    // sent — so a single run creates no series and no group, exactly as before.
    if (this.effectiveRunCount > 1) {
      this.startBenchmarkSeries({
        ...req,
        runCount: this.effectiveRunCount,
        allowCapWait: this.allowCapWait
      });
      return;
    }

    this.benchmarkService.startRun(req).subscribe({
      next: (res) => {
        this.startingRun = false;
        this.sameProviderDialog?.nativeElement.close();
        this.sameProviderWarning = null;
        this.lastRunPollError = null;
        this.runQuestionsLoadError = null;
        this.runDiagnosticsCopyFailed = false;
        this.activeRunId = res.runId;
        this.startPolling(res.runId);
        this.loadHistory();
        this.loadAllFootprints();
        this.cdr.detectChanges();
        this.openRunProgressDialog();
      },
      error: (err) => {
        this.startingRun = false;
        if (err?.status === 409 && err.error?.sameProvider) {
          this.sameProviderWarning = err.error as SameProviderWarningDto;
          this.sameProviderDialog?.nativeElement.showModal();
        } else {
          this.runErrorMessage = err?.error || 'Failed to start benchmark run.';
        }
        this.cdr.detectChanges();
      }
    });
  }

  // --- Multi-run series execution ---

  /**
   * The run count actually in force. A non-numeric or out-of-range field value resolves to 1 rather
   * than to an error, because the field is a courtesy and the server is the authority: the worst a
   * bad value here may do is start one run, never N of them.
   */
  get effectiveRunCount(): number {
    const n = Math.floor(Number(this.runCount));
    if (!Number.isFinite(n) || n < 1) return 1;
    const max = this.maxRunCountPerSeries;
    return max != null && n > max ? max : n;
  }

  /**
   * The Number of runs field's `max`, from GET runs/limits. Null until the caps arrive, which
   * leaves the field unbounded on the client and bounded on the server — the safe direction, since
   * an unknown cap must not silently become 1.
   */
  get maxRunCountPerSeries(): number | null {
    return this.runLimits?.maxRunCountPerSeries ?? null;
  }

  /** True once the operator has asked for more than one run, which is what reveals the projections. */
  get isMultiRunRequested(): boolean {
    return this.effectiveRunCount > 1;
  }

  loadRunLimits(): void {
    this.benchmarkService.getRunLimits().subscribe({
      next: (limits) => {
        this.runLimits = limits;
        if (limits?.maxRunCountPerSeries != null && this.runCount > limits.maxRunCountPerSeries) {
          this.runCount = limits.maxRunCountPerSeries;
        }
        this.cdr.detectChanges();
      },
      // A field that cannot bound itself is still usable, because the server re-checks. Blocking the
      // run because a courtesy lookup failed would be the wrong trade.
      error: (err) => console.error('Failed to load benchmark run limits', err)
    });
  }

  /**
   * The suite's recent mean run duration, in milliseconds, over its completed runs in the loaded
   * history. Null when the history holds none: a projection with no basis is worse than no
   * projection, because it looks like a measurement.
   */
  get recentMeanRunDurationMs(): number | null {
    const runs = this.completedRunsOfSelectedSuite;
    if (runs.length === 0) return null;
    const total = runs.reduce((sum, r) => sum + (r.totalDurationMs || r.totalAnswerDurationMs || 0), 0);
    return total > 0 ? Math.round(total / runs.length) : null;
  }

  /** The same basis for money: the mean estimated cost of the suite's recent completed runs. */
  get recentMeanRunCost(): number | null {
    const priced = this.completedRunsOfSelectedSuite.filter(r => r.estimatedCost != null);
    if (priced.length === 0) return null;
    return priced.reduce((sum, r) => sum + (r.estimatedCost ?? 0), 0) / priced.length;
  }

  /**
   * Completed runs of the selected suite, newest first, capped at five. Five rather than all of
   * them because a projection should describe the instrument as it is now, and a run from before a
   * model change says nothing useful about how long the next one takes.
   */
  private get completedRunsOfSelectedSuite(): BenchmarkRunSummaryDto[] {
    const suiteId = this.selectedSuiteId;
    if (suiteId == null) return [];
    return this.historyRuns
      .filter(r => r.benchmarkSuiteId === suiteId)
      .filter(r => {
        const s = this.formatStatus(r.status);
        return s === 'Completed' || s === 'CompletedWithErrors' || s === 'CompletedWithLimits';
      })
      .slice(0, 5);
  }

  /** RunCount × the suite's recent mean run duration, or null when there is nothing to project from. */
  get projectedSeriesDurationLabel(): string | null {
    const mean = this.recentMeanRunDurationMs;
    if (mean == null) return null;
    return this.formatElapsed(mean * this.effectiveRunCount);
  }

  /** RunCount × the suite's recent mean run cost. Formatted like every other cost on this screen. */
  get projectedSeriesCostLabel(): string | null {
    const mean = this.recentMeanRunCost;
    if (mean == null) return null;
    return this.formatCostAmount(mean * this.effectiveRunCount);
  }

  /**
   * Whether the requested series exceeds what the rolling 24-hour window still allows. Advisory: the
   * guard is re-checked per member, and with AllowCapWait a series that outruns the window pauses
   * rather than failing.
   */
  get seriesExceedsDailyHeadroom(): boolean {
    const headroom = this.runLimits?.remainingDailyHeadroom;
    return headroom != null && this.effectiveRunCount > headroom;
  }

  private startBenchmarkSeries(req: StartBenchmarkRunRequest): void {
    this.benchmarkService.startRunSeries(req).subscribe({
      next: (res) => {
        this.startingRun = false;
        this.sameProviderDialog?.nativeElement.close();
        this.sameProviderWarning = null;
        this.lastRunPollError = null;
        this.runQuestionsLoadError = null;
        this.runDiagnosticsCopyFailed = false;
        this.activeSeriesId = res.seriesId;
        this.startSeriesPolling(res.seriesId);
        this.loadHistory();
        this.loadAllFootprints();
        this.loadRunLimits();
        this.cdr.detectChanges();
        this.openMultiRunDialog();
      },
      error: (err) => {
        this.startingRun = false;
        if (err?.status === 409 && err.error?.sameProvider) {
          this.sameProviderWarning = err.error as SameProviderWarningDto;
          this.sameProviderDialog?.nativeElement.showModal();
        } else {
          this.runErrorMessage = err?.error?.message || err?.error || 'Failed to start benchmark run series.';
        }
        this.cdr.detectChanges();
      }
    });
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
          this.cdr.detectChanges();
        }
      },
      error: (err) => console.error('Failed to check active benchmark run series', err)
    });
  }

  private startSeriesPolling(seriesId: number): void {
    this.stopSeriesPolling();
    this.pollSeries(seriesId);
    this.seriesPollInterval = setInterval(() => {
      if (typeof document !== 'undefined' && document.hidden) {
        return;
      }
      this.pollSeries(seriesId);
    }, AdminBenchmarkComponent.SERIES_POLL_INTERVAL_MS);

    if (typeof document !== 'undefined') {
      this.seriesVisibilityChangeHandler = () => {
        if (!document.hidden) {
          this.pollSeries(seriesId);
        }
      };
      document.addEventListener('visibilitychange', this.seriesVisibilityChangeHandler);
    }
  }

  private stopSeriesPolling(): void {
    if (this.seriesPollInterval) {
      clearInterval(this.seriesPollInterval);
      this.seriesPollInterval = null;
    }
    if (this.seriesVisibilityChangeHandler && typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.seriesVisibilityChangeHandler);
      this.seriesVisibilityChangeHandler = null;
    }
  }

  private pollSeries(seriesId: number): void {
    this.benchmarkService.getRunSeries(seriesId).subscribe({
      next: (series) => {
        this.activeSeries = series;
        // The member currently running is what the single-run banner and dialog describe, so the
        // run poller follows the series rather than being started again per member.
        const running = series.members.find(m => this.formatStatus(m.status) === 'Running');
        if (running && running.runId !== this.activeRunId) {
          this.activeRunId = running.runId;
          this.startPolling(running.runId);
        }
        if (!this.seriesIsLive) {
          this.stopSeriesPolling();
          this.loadHistory();
          this.loadRunGroups();
          this.loadRunLimits();
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        console.error('Failed to poll benchmark run series', err);
        this.stopSeriesPolling();
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

  /**
   * Whether the Run Benchmark tab shows the series banner.
   *
   * <p>A banner describing a series that has finished is an alert with nothing to alert about, and
   * it outlives the work by however long the page stays open. `activeSeries` itself is kept — the
   * progress dialog and the run-to-series labelling read it after completion — so this gates the
   * rendering rather than clearing the state.</p>
   *
   * <p>The completed series stays reachable from the Multi-Run Analysis tab, whose group rows carry
   * a Series badge that opens the same dialog. That matters because the dialog is the only place
   * either diagnostics capture can be copied from.</p>
   */
  get seriesBannerVisible(): boolean {
    return this.activeSeries != null && !this.multiRunDialogVisible && !this.seriesIsFinished;
  }

  /** Stopped is the one non-terminal end state, and the only one the Continue button appears for. */
  get seriesIsStopped(): boolean {
    return this.activeSeries?.status === 'Stopped';
  }

  get seriesIsWaitingForCap(): boolean {
    return this.activeSeries?.status === 'WaitingForCap';
  }

  /** *Run n of N* — the count the operator actually watches, rather than a bare percentage. */
  get seriesProgressLabel(): string {
    const series = this.activeSeries;
    if (!series) return '';
    const current = Math.min(series.completedRunCount + 1, series.requestedRunCount);
    switch (series.status) {
      case 'WaitingForCap':
        return `Waiting for run cap — ${series.completedRunCount} of ${series.requestedRunCount} runs completed.`;
      case 'Stopped':
        return `Stopped — ${series.stopReasonText || series.stopReason || 'reason not recorded'}. `
          + `${series.completedRunCount} of ${series.requestedRunCount} runs completed.`;
      case 'Pending':
        return `Launching run 1 of ${series.requestedRunCount}.`;
      case 'Running':
        return `Run ${current} of ${series.requestedRunCount}.`;
      default:
        return `${series.status} — ${series.completedRunCount} of ${series.requestedRunCount} runs completed.`;
    }
  }

  /** The Continue button's label, which names the stop reason rather than hiding it behind a verb. */
  get seriesContinueLabel(): string {
    const reason = this.activeSeries?.stopReasonText || this.activeSeries?.stopReason;
    return reason ? `Continue (${reason})` : 'Continue';
  }

  openMultiRunDialog(): void {
    this.multiRunDialogVisible = true;
    this.cdr.detectChanges();
  }

  /**
   * Opens the progress dialog for a series this component is not driving — the Multi-Run Analysis
   * tab's Series badge, which is how a completed series is reached now that its banner hides itself.
   */
  openSeriesDialog(seriesId: number): void {
    this.seriesDialogId = seriesId;
    this.multiRunDialogVisible = true;
    this.cdr.detectChanges();
  }

  onMultiRunDialogClosed(): void {
    this.returnToSeriesOnClose = false;
    this.multiRunDialogVisible = false;
    this.seriesDialogId = null;
    this.cdr.detectChanges();
  }

  /**
   * The hand-off the multi-run dialog makes rather than embedding a second per-question view. Two
   * stacked native dialogs trap focus in the inner one, so this closes the multi-run dialog as it
   * opens the single-run one — never both at once.
   */
  onOpenRunProgressFromSeries(runId: number): void {
    this.multiRunDialogVisible = false;
    this.activeRunId = runId;
    this.startPolling(runId);
    this.openRunProgressDialog(true);
  }

  /**
   * The hand-off `viewGroupReport` makes from the multi-run progress dialog: switch to the
   * Multi-Run Analysis tab and open that group there, rather than stacking a second dialog on
   * top of this page.
   */
  onOpenGroupAnalysisFromSeries(groupId: number): void {
    this.multiRunDialogVisible = false;
    this.seriesDialogId = null;
    this.selectSubTab('multirun');
    // The panel lives inside @if (activeSubTab === 'multirun'); the ViewChild does not resolve
    // until that block has rendered, so the tab switch is flushed before the panel is addressed.
    this.cdr.detectChanges();
    this.multiRunPanel?.openGroupById(groupId);
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
    this.resumingSeries = true;
    this.seriesErrorMessage = null;
    this.benchmarkService.resumeRunSeries(seriesId, { acknowledgeInstrumentChange }).subscribe({
      next: () => {
        this.resumingSeries = false;
        this.startSeriesPolling(seriesId);
        this.cdr.detectChanges();
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
        this.cdr.detectChanges();
      }
    });
  }

  /** True once a refused resume has named a moved hash, which is what offers the override. */
  get seriesInstrumentChanged(): boolean {
    return (this.activeSeries?.changedInstrumentHashes?.length ?? 0) > 0;
  }

  closeSameProviderDialog() {
    this.sameProviderDialog?.nativeElement.close();
    this.sameProviderWarning = null;
  }

  confirmSameProviderRun() {
    this.startBenchmark(true);
  }

  cancelActiveRun() {
    if (!this.activeRunId) return;
    this.benchmarkService.cancelRun(this.activeRunId).subscribe({
      next: () => {
        this.pollRunDetail(this.activeRunId!);
      },
      error: (err) => console.error('Failed to cancel run', err)
    });
  }

  private startPolling(runId: number) {
    this.stopPolling();
    this.pollRunDetail(runId);
    this.pollInterval = setInterval(() => {
      if (typeof document !== 'undefined' && document.hidden) {
        return;
      }
      this.pollRunDetail(runId);
    }, AdminBenchmarkComponent.RUN_POLL_INTERVAL_MS);

    if (typeof document !== 'undefined') {
      this.runVisibilityChangeHandler = () => {
        if (!document.hidden) {
          this.pollRunDetail(runId);
        }
      };
      document.addEventListener('visibilitychange', this.runVisibilityChangeHandler);
    }
  }

  private stopPolling() {
    if (this.pollInterval) {
      clearInterval(this.pollInterval);
      this.pollInterval = null;
    }
    if (this.runVisibilityChangeHandler && typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.runVisibilityChangeHandler);
      this.runVisibilityChangeHandler = null;
    }
  }

  private startRunElapsedTicker(): void {
    this.stopRunElapsedTicker();
    this.runElapsedInterval = setInterval(() => {
      if (typeof document !== 'undefined' && document.hidden) {
        return;
      }
      this.cdr.detectChanges();
    }, AdminBenchmarkComponent.RUN_ELAPSED_TICK_MS);
  }

  private stopRunElapsedTicker(): void {
    if (this.runElapsedInterval) {
      clearInterval(this.runElapsedInterval);
      this.runElapsedInterval = null;
    }
  }

  private pollRunDetail(runId: number) {
    this.benchmarkService.getRun(runId).subscribe({
      next: (run) => {
        this.lastRunPollAtUtc = new Date().toISOString();
        this.lastRunPollError = null;
        this.activeRunDetail = run;
        const statusStr = this.formatStatus(run.status);
        if (statusStr !== 'Running') {
          this.stopPolling();
          this.stopRunElapsedTicker();
          this.loadHistory();
        } else if (this.isRunProgressDialogOpen && !this.runElapsedInterval) {
          this.startRunElapsedTicker();
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.lastRunPollAtUtc = new Date().toISOString();
        const httpStatus = err?.status ? ` (HTTP ${err.status})` : '';
        const msg = typeof err?.error === 'string' ? err.error : (err?.error?.message || err?.message || 'Polling failed');
        this.lastRunPollError = `${msg}${httpStatus}`;
        console.error('Failed to poll run detail', err);
        this.stopPolling();
      }
    });
  }

  // --- Run Progress Dialog ---

  /**
   * Which of the run's two sequential stages is executing. `BenchmarkService` assesses each
   * answer immediately after producing it, inside the same loop, in both the sequential and
   * the parallel branch — so answering and assessing are one stage in wall-clock terms, and
   * only the holistic synthesis is separate.
   */
  get runStage(): 'answering' | 'finalizing' | 'terminal' {
    const run = this.activeRunDetail;
    if (!run) return 'answering';
    if (this.formatStatus(run.status) !== 'Running') return 'terminal';
    if (run.answers.length < run.totalQuestionCount) return 'answering';
    if (run.answers.some(a => this.isAssessmentIncomplete(a))) return 'answering';
    return 'finalizing';
  }

  get runStageLabel(): string {
    const run = this.activeRunDetail;
    if (!run) return '';
    const total = this.runTotalQuestionCount;
    switch (this.runStage) {
      case 'answering':
        return `Stage 1 of 2 — Collecting and assessing answers. Answered ${this.runAnsweredCount} of ${total}, scored ${this.runScoredCount} of ${total}.`;
      case 'finalizing': {
        const stageName = run.claimVerifierModelConfigurationId != null
          ? 'Verification and synthesis'
          : 'Synthesis and scoring';
        return `Stage 2 of 2 — ${stageName}. All ${total} answers assessed.`;
      }
      default: {
        const status = this.formatStatus(run.status);
        const label = status === 'CompletedWithErrors'
          ? 'Completed with errors'
          : (status === 'CompletedWithLimits' ? 'Completed with limits' : status);
        const failed = this.runFailedAnswerCount;
        return failed > 0
          ? `${label}. Answered ${this.runAnsweredCount} of ${total}, ${failed} failed.`
          : `${label}. Answered ${this.runAnsweredCount} of ${total}.`;
      }
    }
  }

  get runAnsweredCount(): number {
    return this.activeRunDetail?.answers.length ?? 0;
  }

  /** Answers that reached a terminal assessment state — scored or failed to assess. */
  get runScoredCount(): number {
    return (this.activeRunDetail?.answers ?? []).filter(a => {
      const s = this.formatAssessmentStatus(a.assessmentStatus);
      return s === 'Scored' || s === 'Failed';
    }).length;
  }

  get runFailedAnswerCount(): number {
    return this.runFailedAnswers.length;
  }

  get runFailedAnswers(): BenchmarkRunAnswerDto[] {
    return (this.activeRunDetail?.answers ?? []).filter(a => this.isAnswerFailed(a));
  }

  get runTotalQuestionCount(): number {
    return this.activeRunDetail?.totalQuestionCount ?? 0;
  }

  get runIsRunning(): boolean {
    return this.activeRunDetail != null && this.formatStatus(this.activeRunDetail.status) === 'Running';
  }

  get runIsTerminal(): boolean {
    return this.activeRunDetail != null && this.formatStatus(this.activeRunDetail.status) !== 'Running';
  }

  /**
   * The suite's questions merged with whatever answers the run has produced. Falls back to
   * the answers alone when the suite fetch has not landed (or failed), so the list is never
   * empty while the run is visibly progressing.
   */
  get runProgressRows(): BenchmarkRunProgressRow[] {
    const run = this.activeRunDetail;
    if (!run) return [];

    // Keyed both ways, because the question id is the reliable key and not every answer has
    // one: a suite reorder rewrites order indexes and touches no stored answer, so matching on
    // the index alone rendered a reordered suite's earlier runs against the wrong questions.
    const answersByQuestionId = new Map<number, BenchmarkRunAnswerDto>();
    const answersByIndex = new Map<number, BenchmarkRunAnswerDto>();
    for (const a of run.answers) {
      answersByIndex.set(a.orderIndex, a);
      if (a.benchmarkQuestionId != null) {
        answersByQuestionId.set(a.benchmarkQuestionId, a);
      }
    }

    const source = this.runProgressQuestions.length > 0
      ? this.runProgressQuestions.map(q => ({ id: q.id, orderIndex: q.orderIndex, questionText: q.questionText }))
      : run.answers.map(a => ({ id: a.benchmarkQuestionId ?? null, orderIndex: a.orderIndex, questionText: a.questionText }));

    const inFlight = new Set<number>(run.inFlightOrderIndexes ?? []);

    return [...source]
      .sort((a, b) => a.orderIndex - b.orderIndex)
      .map(q => {
        const ans = (q.id != null ? answersByQuestionId.get(q.id) : undefined) ?? answersByIndex.get(q.orderIndex);
        if (!ans) {
          return {
            orderIndex: q.orderIndex,
            questionText: q.questionText,
            status: inFlight.has(q.orderIndex) ? 'Answering' : 'Pending',
            assessmentStatus: '',
            errorMessage: null
          };
        }
        return {
          orderIndex: q.orderIndex,
          questionText: q.questionText,
          status: this.formatAnswerStatus(ans.status),
          assessmentStatus: this.formatAssessmentStatus(ans.assessmentStatus),
          errorMessage: ans.errorMessage ?? null
        };
      });
  }

  /**
   * The chip's word, never a hue alone. 'Answered' rather than 'Assessing' while the
   * assessment is merely queued — claiming work that has not started would be a guess.
   */
  runRowChipLabel(row: BenchmarkRunProgressRow): string {
    if (row.status === 'Pending') return 'Pending';
    if (row.status === 'Answering') return 'Answering';
    if (row.status === 'ProviderError') return 'Provider Error';
    if (row.status !== 'Ok') return row.status;
    if (row.assessmentStatus === 'Scored') return 'Scored';
    if (row.assessmentStatus === 'Failed') return 'Assessment Failed';
    if (row.assessmentStatus === 'Assessing') return 'Assessing';
    return 'Answered';
  }

  runRowChipClass(row: BenchmarkRunProgressRow): string {
    if (row.status === 'Pending') return 'status-pending';
    if (row.status === 'Answering') return 'status-answering';
    if (row.status === 'ProviderError') return 'status-providererror';
    if (row.status === 'Failed') return 'status-failed';
    if (row.status === 'Skipped') return 'status-skipped';
    if (row.assessmentStatus === 'Scored') return 'status-scored';
    if (row.assessmentStatus === 'Failed') return 'status-failed';
    if (row.assessmentStatus === 'Assessing') return 'status-assessing';
    return 'status-ok';
  }

  /** Recomputed each second by the elapsed ticker (and on each poll tick). */
  get runElapsedLabel(): string {
    const run = this.activeRunDetail;
    if (!run?.startedAtUtc) return '—';
    const ms = elapsedMsBetween(run.startedAtUtc, run.completedAtUtc);
    return this.formatElapsed(ms);
  }

  get runAverageAnswerDurationLabel(): string {
    const run = this.activeRunDetail;
    if (!run || run.answers.length === 0) return '—';
    return this.formatDuration(Math.round(run.totalAnswerDurationMs / run.answers.length));
  }

  /**
   * Everything an operator would paste into a bug report, assembled from the run detail.
   * Answer text, thought text, and assessor comments are deliberately excluded: they are
   * long model-generated content already reachable through the run detail dialog and the
   * Markdown report. No credential or connection string appears in the DTO.
   */
  get runDiagnosticsText(): string {
    const run = this.activeRunDetail;
    const lines: string[] = [];

    // Header
    lines.push('=== BENCHMARK RUN DIAGNOSTICS ===');
    lines.push(`Captured:         ${new Date().toISOString()}`);
    lines.push(`Overseer build:   ${this.overseerBuildVersion || 'unknown'}`);
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
      const stageStr = this.runStage === 'terminal' ? 'terminal' : (this.runStage === 'answering' ? '1' : '2');
      lines.push(`Run ID: ${run.id}, Suite: ${run.suiteName} (${run.benchmarkSuiteId ?? 'n/a'}), Status: ${this.formatStatus(run.status)}, Stage: ${stageStr}, Started by: ${run.startedByUserName || 'unknown'}`);
      lines.push(`Started (raw):    ${run.startedAtUtc}`);
      const startedParsed = run.startedAtUtc ? parseServerUtcDate(run.startedAtUtc).toISOString() : 'n/a';
      lines.push(`Started (parsed): ${startedParsed}`);
      lines.push(`Completed:        ${run.completedAtUtc ?? 'n/a'}`);
      lines.push(`Elapsed:          ${this.runElapsedLabel}`);
      lines.push('');

      // --- MODELS ---
      lines.push('--- MODELS ---');
      lines.push(`Tested:   ${run.testedModelDisplayNameUsed} (${run.testedModelProviderUsed} / ${run.testedModelIdUsed})`);
      lines.push(`          thinking: ${run.testedModelThinkingLevelUsed ?? 'default'}, reasoning: ${run.testedModelReasoningModeUsed ?? 'default'}, service tier: ${this.formatServiceTier(run.testedModelServiceTierUsed)}, max output tokens: ${run.testedModelMaxOutputTokensUsed ?? 'default'}, parallel mode: ${run.testedModelParallelExecutionModeUsed}`);
      lines.push(`Assessor: ${run.assessorModelDisplayNameUsed} (${run.assessorModelProviderUsed} / ${run.assessorModelIdUsed}), thinking: ${run.assessorModelThinkingLevelUsed ?? 'default'}, reasoning: ${run.assessorModelReasoningModeUsed ?? 'default'}, available=${run.assessorAvailable}`);
      // The third role, named whether or not one was used: "no second opinion" is itself a fact
      // about how the run was graded, and the capture used to omit it entirely.
      if (run.secondOpinionAssessorModelConfigurationId != null) {
        lines.push(`Second:   ${run.secondOpinionAssessorModelDisplayNameUsed} (${run.secondOpinionAssessorModelProviderUsed} / ${run.secondOpinionAssessorModelIdUsed}), thinking: ${run.secondOpinionAssessorModelThinkingLevelUsed ?? 'default'}, reasoning: ${run.secondOpinionAssessorModelReasoningModeUsed ?? 'default'}`);
      } else {
        lines.push('Second:   none selected');
      }
      lines.push('');

      // --- SCORING ---
      lines.push('--- SCORING ---');
      lines.push(`Profile: ${run.scoringProfileName ?? 'n/a'} (${run.scoringProfileId ?? 'n/a'}), harness version: ${run.harnessVersion ?? '1 (unversioned legacy)'}, scoring method version: ${run.scoringMethodVersion}, max parallel questions: ${run.maxParallelQuestionsUsed}`);
      // Read out of the run's own profile snapshot server-side, so this describes the run in
      // front of it rather than whatever the default profile says today.
      lines.push(`Speed: target ${run.scoringProfileSpeedTargetMs ?? 'n/a'} ms, decay k ${run.scoringProfileSpeedDecayK ?? 'n/a'}`);
      lines.push(`Second opinion: mode ${this.diagnosticsModeName(run.secondOpinionModeUsed)}, threshold ${run.scoringProfileSecondOpinionQualityThreshold ?? 'n/a'}, outlier delta ${run.scoringProfileSecondOpinionOutlierDeltaPoints ?? 'n/a'}`);
      lines.push(`Tool call budget: ${run.maxToolCallsPerQuestionUsed ?? 'not recorded'}`);
      lines.push('');

      // --- PROGRESS ---
      lines.push('--- PROGRESS ---');
      lines.push(`Answered ${this.runAnsweredCount} of ${this.runTotalQuestionCount}, scored ${this.runScoredCount} of ${this.runTotalQuestionCount}, failed ${this.runFailedAnswerCount}`);
      const inFlight = run.inFlightOrderIndexes ?? [];
      lines.push(`In flight: ${inFlight.length > 0 ? inFlight.map(i => `Q${i}`).join(', ') : 'none'}`);
      if (this.runProgressQuestions.length > 0 && this.runProgressQuestionsSuiteId != null) {
        lines.push(`Suite questions loaded: ${this.runProgressQuestions.length} for suite ${this.runProgressQuestionsSuiteId}`);
      } else {
        lines.push('Suite questions loaded: not loaded — list degraded to answers only');
      }
      lines.push('');

      // --- TOKENS ---
      lines.push('--- TOKENS ---');
      lines.push(`input: ${run.totalInputTokens}, output: ${run.totalOutputTokens}, cache read: ${run.totalCacheReadTokens}, cache creation: ${run.totalCacheCreationTokens}`);
      lines.push(`total duration: ${this.formatDuration(run.totalDurationMs)}, total answer duration: ${this.formatDuration(run.totalAnswerDurationMs)}`);
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
      if (this.runIsTerminal) {
        lines.push('--- SCORES ---');
        // `finalScore` is the Holistic Assessor Score, which feeds no aggregate — labelling it
        // "final" read as the canonical result, which is the Intelligence Index (quality index).
        // `computed` is the superseded ComputedScore column, which current runs never write:
        // printing "computed: n/a" on every capture read as a missing value rather than a
        // retired one, so it appears only where a historical run actually has it.
        const scoreParts = [
          `holistic: ${run.finalScore ?? 'n/a'}`,
          `quality index: ${run.qualityIndex ?? 'n/a'}`,
          `unweighted mean: ${run.unweightedQualityIndex ?? 'not recorded'}`,
          `raw quality index: ${run.rawQualityIndex ?? 'n/a'}`,
          `speed index: ${run.speedIndex ?? 'n/a'}`
        ];
        if (run.computedScore != null) {
          scoreParts.splice(1, 0, `computed (superseded): ${run.computedScore}`);
        }
        lines.push(scoreParts.join(', '));
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
        lines.push(`advisory flags: ${run.advisoryFlagAnswerCount ?? 0}, scrubbed: ${run.scrubbedArtifactAnswerCount ?? 0}, contested verdicts: ${run.contestedVerdictAnswerCount ?? 0}, unevidenced deductions: ${run.unevidencedDeductionAnswerCount ?? 0}, refuted claims: ${run.refutedClaimAnswerCount ?? 0}, re-assessed: ${run.reassessedAnswerCount ?? 0}`);
        // Computed from `run`, not from the run-detail getters: this capture describes the
        // *active* run, and those getters read whichever run the detail dialog has open.
        const criticalHere = run.answers.filter(a => a.criticalError).map(a => a.orderIndex);
        const unverifiedHere = run.answers.reduce((sum, a) => sum + (a.unverifiedClaimCount ?? 0), 0);
        lines.push(`critical errors: ${criticalHere.length}${criticalHere.length > 0 ? ` (${criticalHere.map(i => 'Q' + i).join(', ')})` : ''}, unverified claims: ${unverifiedHere}`);
        const signedDeltaStr = run.secondOpinionMeanSignedDelta != null
          ? `, ${run.secondOpinionMeanSignedDelta > 0 ? '+' : run.secondOpinionMeanSignedDelta < 0 ? '−' : ''}${Math.abs(run.secondOpinionMeanSignedDelta).toFixed(1)} mean signed delta`
          : '';
        const splitsStr = (run.secondOpinionCriticalErrorSplitCount ?? 0) > 0
          ? `, critical-error splits: ${run.secondOpinionCriticalErrorSplitCount}`
          : '';
        lines.push(`agreement: ${run.secondOpinionMeanAbsDelta != null ? run.secondOpinionMeanAbsDelta.toFixed(1) + ' mean abs delta' : 'not measured'}${signedDeltaStr} over ${run.secondOpinionGradedAnswerCount ?? 0} of ${run.answeredQuestionCount} answered, disagreements: ${run.secondOpinionDisagreementCount ?? 0}${splitsStr}`);
        if (run.secondOpinionAssessorModelConfigurationId != null && (run.secondOpinionGradedAnswerCount ?? 0) === 0) {
          lines.push(`second opinion: selected (${run.secondOpinionAssessorModelDisplayNameUsed ?? 'configured'}) but no answer met a trigger — 0 graded`);
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
        lines.push(`parallelMode: ${run.testedModelParallelExecutionModeUsed}`);
        if (run.candidatePromptOptionsJson) {
          try {
            const opts = JSON.parse(run.candidatePromptOptionsJson);
            lines.push(`options: mode=${opts.overseerMode ?? 0}, verbose=${opts.verboseMode ?? false}, spoilerFree=${opts.spoilerFreeMode ?? false}, tools=${opts.enableToolUse ?? true}, webSearch=${opts.enableWebSearch ?? false}, subagents=${opts.enableSubAgents ?? false}, sourceCodeRefs=${opts.allowSourceCodeReferences ?? true}`);
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
        lines.push(`answers with 0 knowledge base calls: ${zeroKbAnswers} of ${run.answers.length}`);
        lines.push('  (prompt-compliant on game-mechanics topics — ChatService.cs "Information Routing" scopes the');
        lines.push('   knowledge base to app navigation, settings, controls, replay, vault and troubleshooting)');
        lines.push('');
      }

      // --- FLAGS ---
      lines.push('--- FLAGS ---');
      const purposePresent = !!run.purposeStatementUsed;
      lines.push(`difficultyFallbackUsed=${run.difficultyFallbackUsed}, speedMeasurementDegraded=${run.speedMeasurementDegraded}, assessmentParseFailed=${run.assessmentParseFailed}, sameProviderAcknowledged=${run.sameProviderAcknowledged ?? false}, purposeStatement present=${purposePresent}`);
      lines.push('');
    }

    // --- POLLING ---
    lines.push('--- POLLING ---');
    const runPollStr = this.pollInterval ? `active every ${AdminBenchmarkComponent.RUN_POLL_INTERVAL_MS} ms` : 'stopped';
    lines.push(`Run poll: ${runPollStr}`);
    const tickerStr = this.runElapsedInterval ? `active every ${AdminBenchmarkComponent.RUN_ELAPSED_TICK_MS} ms` : 'stopped';
    lines.push(`Elapsed ticker: ${tickerStr}`);
    if (this.lastRunPollAtUtc) {
      const pollAgoSec = Math.max(0, Math.floor((Date.now() - new Date(this.lastRunPollAtUtc).getTime()) / 1000));
      lines.push(`Last poll: ${this.lastRunPollAtUtc} (${pollAgoSec}s ago)`);
    }
    if (this.lastRunPollError) {
      lines.push(`Last poll error: ${this.lastRunPollError}`);
    }
    lines.push(`Document hidden: ${typeof document !== 'undefined' ? document.hidden : false}`);
    lines.push('');

    // --- ERRORS ---
    lines.push('--- ERRORS ---');
    let hasError = false;
    if (run?.errorMessage) {
      lines.push(`Run error: ${run.errorMessage}`);
      hasError = true;
    }
    if (this.runQuestionsLoadError) {
      lines.push(`Questions fetch error: ${this.runQuestionsLoadError}`);
      hasError = true;
    }
    if (!hasError) {
      lines.push('none');
    }
    lines.push('');

    // --- QUESTIONS ---
    if (run && this.runProgressRows.length > 0) {
      lines.push('--- QUESTIONS ---');
      for (const row of this.runProgressRows) {
        const ans = run.answers.find(a => a.orderIndex === row.orderIndex);
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
        if (ans.score != null) parts.push(`score=${ans.score}`);

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
      }
    }

    return lines.join('\n');
  }

  /** Mode names for the diagnostics capture, which is read as plain text and not localised. */
  private diagnosticsModeName(mode: number | null | undefined): string {
    switch (mode) {
      case 0: return 'Off';
      case 1: return 'Flagged';
      case 2: return 'FlaggedAndOutliers';
      case 3: return 'All';
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
    return this.runDiagnosticsCopyFailed ? 'Could not copy the diagnostics to the clipboard.' : '';
  }

  runDiagnosticsCopyFailed = false;

  async copyRunDiagnostics(): Promise<void> {
    const text = this.runDiagnosticsText;
    if (!text) { return; }
    try {
      await navigator.clipboard.writeText(text);
      this.runDiagnosticsCopyFailed = false;
      this.copiedRunDiagnostics = true;
      if (this.copiedRunDiagnosticsTimer) { clearTimeout(this.copiedRunDiagnosticsTimer); }
      this.copiedRunDiagnosticsTimer = setTimeout(() => {
        this.copiedRunDiagnostics = false;
        this.copiedRunDiagnosticsTimer = null;
        this.cdr.detectChanges();
      }, 2000);
    } catch {
      this.copiedRunDiagnostics = false;
      this.runDiagnosticsCopyFailed = true;
      this.runErrorMessage = 'Could not copy the benchmark run diagnostics to the clipboard.';
    }
  }

  openRunProgressDialog(fromSeries = false): void {
    this.returnToSeriesOnClose = fromSeries;
    this.isRunProgressDialogOpen = true;
    this.runDiagnosticsCopyFailed = false;

    if (this.overseerBuildVersion === null) {
      this.systemService.getVersion().subscribe({
        next: (version) => {
          this.overseerBuildVersion = version;
        },
        error: (err) => {
          console.warn('Failed to get Overseer build version', err);
          this.overseerBuildVersion = 'unknown';
        }
      });
    }

    if (this.runIsRunning) {
      this.startRunElapsedTicker();
    }

    // Resolved here and not in the poll handler: the suite's questions are static for the
    // life of a run, so one fetch per dialog open is one more than strictly necessary.
    const suiteId = this.activeRunDetail?.benchmarkSuiteId ?? this.selectedSuiteId;
    if (suiteId != null && this.runProgressQuestionsSuiteId !== suiteId) {
      this.loadRunProgressQuestions(suiteId);
    }

    this.runProgressDialog?.nativeElement.showModal();
    this.cdr.detectChanges();
    this.runProgressHeading?.nativeElement.focus();
  }

  closeRunProgressDialog(returnToSeries: boolean = this.returnToSeriesOnClose): void {
    const shouldReturn = returnToSeries;
    this.returnToSeriesOnClose = false;
    this.isRunProgressDialogOpen = false;
    this.stopRunElapsedTicker();
    this.runProgressDialog?.nativeElement.close();
    if (shouldReturn && this.activeSeriesId != null) {
      this.openMultiRunDialog();
    }
    this.cdr.detectChanges();
  }

  private loadRunProgressQuestions(suiteId: number): void {
    // Claimed before the request so a second open while it is in flight does not refire it.
    this.runProgressQuestionsSuiteId = suiteId;
    this.benchmarkService.getQuestions(suiteId).subscribe({
      next: (data) => {
        this.runQuestionsLoadError = null;
        this.runProgressQuestions = data;
        this.cdr.detectChanges();
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
          this.cdr.detectChanges();
        }
      },
      error: (err) => console.error('Failed to check active benchmark run', err)
    });
  }

  /** Terminal-state action: hand the operator over to the existing full run detail dialog. */
  viewActiveRunDetail(): void {
    const runId = this.activeRunDetail?.id ?? this.activeRunId;
    if (runId == null) return;
    this.closeRunProgressDialog(false);
    this.viewRunDetail(runId);
  }

  /** Re-runs the failed questions without leaving the dialog, so the retry stays watchable. */
  rerunFailedFromProgress(): void {
    const runId = this.activeRunDetail?.id ?? this.activeRunId;
    if (runId == null) return;
    this.runErrorMessage = null;
    this.benchmarkService.rerunFailedQuestions(runId).subscribe({
      next: () => {
        this.activeRunId = runId;
        this.startPolling(runId);
        this.loadHistory();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.runErrorMessage = err?.error || 'Failed to re-run failed questions.';
        this.cdr.detectChanges();
      }
    });
  }

  // --- History & Details ---

  loadHistory() {
    this.loadingHistory = true;
    // The endpoint clamps to 200 regardless, so asking for exactly that keeps a full page rather
    // than leaving the table half-empty behind its own pager.
    this.benchmarkService.getRuns(this.historySuiteFilter || undefined, 200).subscribe({
      next: (data) => {
        this.historyRuns = data;
        this.loadingHistory = false;
        // A remembered comparison selection is validated against the list that has just arrived,
        // because a run deleted since the last visit must not be sent to the compare endpoint.
        this.pruneComparisonSelection();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingHistory = false;
        console.error('Failed to load history runs', err);
        this.cdr.detectChanges();
      }
    });
  }

  // --- Run History: series badge, group column, group builder ---

  loadRunGroups(): void {
    this.loadingRunGroups = true;
    this.benchmarkService.getRunGroups().subscribe({
      next: (groups) => {
        this.runGroups = groups;
        this.loadingRunGroups = false;
        this.pruneComparisonSelection();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingRunGroups = false;
        console.error('Failed to load benchmark run groups', err);
        this.cdr.detectChanges();
      }
    });
  }

  /**
   * The analysis groups a run belongs to. A run may sit in several — a replicate set and a
   * cross-condition comparison, say — so this is a list rather than a single value.
   */
  groupsOfRun(runId: number): BenchmarkRunGroupDto[] {
    return this.runGroups.filter(g => g.members.some(m => m.runId === runId));
  }

  /**
   * The series a run belongs to, or null.
   *
   * Derived rather than read off the row: `BenchmarkRunSummaryDto` carries no `runSeriesId`, so the
   * two available sources are the series-created group's `createdFromSeriesId` and the live series'
   * own member list. Together those cover every series a member can be in — one that finished (its
   * group exists) and the one currently executing (it is the active series) — but a series that
   * stopped before producing a group is invisible here. Reading a series id off the run row would
   * be the direct answer and needs a DTO field that does not exist yet.
   */
  seriesIdOfRun(runId: number): number | null {
    const fromGroup = this.runGroups.find(
      g => g.createdFromSeriesId != null && g.members.some(m => m.runId === runId));
    if (fromGroup?.createdFromSeriesId != null) return fromGroup.createdFromSeriesId;
    if (this.activeSeries?.members.some(m => m.runId === runId)) return this.activeSeries.id;
    return null;
  }

  /** The member's 1-based position in its series, for the badge's *n of N*. */
  seriesBadgeLabelOf(runId: number): string | null {
    const seriesId = this.seriesIdOfRun(runId);
    if (seriesId == null) return null;
    const member = this.activeSeries?.id === seriesId
      ? this.activeSeries.members.find(m => m.runId === runId)
      : undefined;
    return member
      ? `Series #${seriesId} · run ${member.index} of ${this.activeSeries!.requestedRunCount}`
      : `Series #${seriesId}`;
  }

  isRunSelected(runId: number): boolean {
    return this.selectedRunIds.has(runId);
  }

  /**
   * Ticking a row re-previews the tier. The preview is a server call because comparability spans
   * keys the summary row does not carry — the profile snapshot, the assessor configuration, the
   * per-question budgets — so deciding it client-side would decide it on a subset of the evidence.
   */
  toggleRunSelection(runId: number): void {
    if (this.selectedRunIds.has(runId)) {
      this.selectedRunIds.delete(runId);
    } else {
      this.selectedRunIds.add(runId);
    }
    this.groupBuilderError = null;
    this.groupBuilderSuccess = null;
    this.previewGroupTier();
  }

  clearRunSelection(): void {
    this.selectedRunIds.clear();
    this.groupTierPreview = null;
    this.groupPreviewError = null;
    this.groupBuilderError = null;
    this.groupBuilderSuccess = null;
  }

  get selectedRunIdList(): number[] {
    return Array.from(this.selectedRunIds).sort((a, b) => a - b);
  }

  /** Two runs is the smallest set a comparability verdict says anything about. */
  get canBuildGroup(): boolean {
    return this.selectedRunIds.size >= 2 && !this.creatingGroup;
  }

  previewGroupTier(): void {
    const runIds = this.selectedRunIdList;
    if (runIds.length < 2) {
      this.groupTierPreview = null;
      this.groupPreviewError = null;
      return;
    }

    this.previewingGroupTier = true;
    this.benchmarkService.previewRunGroupTier({
      name: this.groupBuilderName.trim() || 'Preview',
      runIds,
      crossCondition: this.groupBuilderCrossCondition
    }).subscribe({
      next: (preview) => {
        this.previewingGroupTier = false;
        this.groupTierPreview = preview.comparability ?? null;
        this.groupPreviewError = preview.accepted ? null : (preview.error ?? null);
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.previewingGroupTier = false;
        this.groupTierPreview = null;
        this.groupPreviewError = err?.error?.message || err?.error || 'Could not compute the tier for this selection.';
        this.cdr.detectChanges();
      }
    });
  }

  /**
   * Creates a group from the selection, or adds the selection to an existing one. A refusal is
   * rendered with the differing keys the server named, never as a bare "rejected": the operator's
   * next action depends entirely on *which* key differs.
   */
  addSelectionToGroup(): void {
    const runIds = this.selectedRunIdList;
    if (runIds.length < 2) return;

    this.creatingGroup = true;
    this.groupBuilderError = null;
    this.groupBuilderSuccess = null;

    const targetId = this.groupBuilderTargetId;
    const request$ = targetId != null
      ? this.benchmarkService.updateRunGroup(targetId, {
          runIds: Array.from(new Set([
            ...runIds,
            ...(this.runGroups.find(g => g.id === targetId)?.members.map(m => m.runId) ?? [])
          ])).sort((a, b) => a - b),
          crossCondition: this.groupBuilderCrossCondition
        })
      : this.benchmarkService.createRunGroup({
          name: this.groupBuilderName.trim() || this.defaultGroupName,
          runIds,
          notes: this.groupBuilderNotes.trim() || null,
          crossCondition: this.groupBuilderCrossCondition
        });

    request$.subscribe({
      next: (result: BenchmarkRunGroupTierPreviewDto) => {
        this.creatingGroup = false;
        this.groupTierPreview = result.comparability ?? this.groupTierPreview;
        if (!result.accepted) {
          this.groupBuilderError = result.error ?? 'The selected runs are not comparable enough to form a group.';
        } else {
          this.groupBuilderSuccess = result.group
            ? `${result.group.name} — ${result.group.tierLabel}, ${result.group.runCount} run(s).`
            : 'Group saved.';
          this.selectedRunIds.clear();
          this.groupBuilderName = '';
          this.groupBuilderNotes = '';
          this.loadRunGroups();
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.creatingGroup = false;
        this.groupBuilderError = err?.error?.message || err?.error || 'Failed to save the group.';
        this.cdr.detectChanges();
      }
    });
  }

  /** `<Suite> · R=<n>` — the same shape the orchestrator names an auto-created series group. */
  private get defaultGroupName(): string {
    const suite = this.selectedSuite?.name
      ?? this.historyRuns.find(r => this.selectedRunIds.has(r.id))?.suiteName
      ?? 'Runs';
    return `${suite} · R=${this.selectedRunIds.size}`;
  }

  viewRunDetail(runId: number) {
    this.loadingDetail = true;
    this.selectedRunDetail = null;
    this.expandedQuestions.clear();
    this.expandedThoughts.clear();
    this.expandedArtifacts.clear();
    this.calibrations = [];
    this.calibrationErrorMessage = null;
    this.calibrationAssessorConfigId = this.benchmarkCapableConfigs[0]?.id ?? null;
    this.runDetailDialog?.nativeElement.showModal();
    this.loadCalibrations(runId);

    this.benchmarkService.getRun(runId).subscribe({
      next: (data) => {
        this.selectedRunDetail = data;
        this.loadingDetail = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingDetail = false;
        console.error('Failed to load run details', err);
        this.cdr.detectChanges();
      }
    });
  }

  closeRunDetail() {
    this.stopDetailPolling();
    this.selectedRunDetail = null;
    this.calibrations = [];
    this.calibrationErrorMessage = null;
    this.isCalibrationAssessorDropdownOpen = false;
    this.runDetailDialog?.nativeElement.close();
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
        const statusStr = this.formatStatus(data.status);
        if (statusStr !== 'Running') {
          this.stopDetailPolling();
          this.reassessingAnswerId = null;
          this.rerunningAnswerId = null;
          this.runningSynthesis = false;
          this.retryingAssessments = false;
          this.retryingClaimVerification = false;
          this.loadHistory();
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        console.error('Failed to refresh run details', err);
        this.stopDetailPolling();
        this.reassessingAnswerId = null;
        this.rerunningAnswerId = null;
        this.runningSynthesis = false;
        this.retryingAssessments = false;
        this.retryingClaimVerification = false;
        this.cdr.detectChanges();
      }
    });
  }

  cancelRunById(runId: number) {
    this.benchmarkService.cancelRun(runId).subscribe({
      next: () => {
        this.refreshRunDetail(runId);
      },
      error: (err) => {
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
    this.isRetryAssessorDropdownOpen = false;
    this.retryDialog?.nativeElement.showModal();
    this.cdr.detectChanges();
  }

  closeRetryDialog() {
    this.retryScope = null;
    this.retryRunId = null;
    this.retryAnswer = null;
    this.retryAssessorConfigId = null;
    this.isRetryAssessorDropdownOpen = false;
    this.retryDialog?.nativeElement.close();
    this.cdr.detectChanges();
  }

  private resolveRetryAssessor(): number | null {
    const runAssessorId = this.selectedRunDetail?.assessorModelConfigurationId;
    if (runAssessorId != null && this.benchmarkCapableConfigs.some(c => c.id === runAssessorId)) {
      return runAssessorId;
    }
    return this.benchmarkCapableConfigs[0]?.id ?? null;
  }

  private resolveRetryClaimVerifier(): number | null {
    const runVerifierId = this.selectedRunDetail?.claimVerifierModelConfigurationId;
    if (runVerifierId != null && this.benchmarkCapableConfigs.some(c => c.id === runVerifierId)) {
      return runVerifierId;
    }
    return this.selectedRunDetail?.assessorModelConfigurationId ?? this.benchmarkCapableConfigs[0]?.id ?? null;
  }

  confirmRetry() {
    if (!this.retryRunId || !this.retryScope) return;

    const runId = this.retryRunId;
    const scope = this.retryScope;
    const assessorId = this.retryAssessorConfigId;
    const answer = this.retryAnswer;

    this.closeRetryDialog();

    if (scope === 'assessment') {
      if (!answer) return;
      this.actionErrorMessage = null;
      this.reassessingAnswerId = answer.id;
      this.benchmarkService.reassessAnswer(runId, answer.id, assessorId).subscribe({
        next: () => {
          this.startDetailPolling(runId);
        },
        error: (err) => {
          this.reassessingAnswerId = null;
          this.actionErrorMessage = err?.error || 'Failed to start reassessment.';
          this.cdr.detectChanges();
        }
      });
    } else if (scope === 'trial') {
      if (!answer) return;
      this.actionErrorMessage = null;
      this.trialReassessingAnswerId = answer.id;
      // Overwriting an existing automatic second opinion is refused server-side unless asked
      // for: that verdict is run evidence, and an experiment must not erase it by accident. The
      // operator confirms the replacement here before the call, not after the refusal.
      const replaceExisting = answer.secondOpinionQualityScore != null &&
        answer.secondOpinionTrigger !== 'Manual';
      this.benchmarkService
        .trialReassessAnswer(runId, answer.id, assessorId, replaceExisting)
        .subscribe({
          next: () => {
            this.startDetailPolling(runId);
          },
          error: (err) => {
            this.trialReassessingAnswerId = null;
            this.actionErrorMessage = err?.error || 'Failed to start the trial assessment.';
            this.cdr.detectChanges();
          }
        });
    } else if (scope === 'question') {
      if (!answer) return;
      this.actionErrorMessage = null;
      this.rerunningAnswerId = answer.id;
      this.benchmarkService.rerunAnswer(runId, answer.id, assessorId).subscribe({
        next: () => {
          this.startDetailPolling(runId);
        },
        error: (err) => {
          this.rerunningAnswerId = null;
          this.actionErrorMessage = err?.error || 'Failed to start rerun.';
          this.cdr.detectChanges();
        }
      });
    } else if (scope === 'synthesis') {
      this.actionErrorMessage = null;
      this.runningSynthesis = true;
      this.benchmarkService.rerunFinalSynthesis(runId, assessorId).subscribe({
        next: () => {
          this.startDetailPolling(runId);
        },
        error: (err) => {
          this.runningSynthesis = false;
          this.actionErrorMessage = err?.error || 'Failed to start final synthesis.';
          this.cdr.detectChanges();
        }
      });
    } else if (scope === 'assessments') {
      this.actionErrorMessage = null;
      this.retryingAssessments = true;
      this.benchmarkService.retryFailedAssessments(runId, assessorId).subscribe({
        next: () => {
          this.startDetailPolling(runId);
        },
        error: (err) => {
          this.retryingAssessments = false;
          this.actionErrorMessage = err?.error || 'Failed to retry failed assessments.';
          this.cdr.detectChanges();
        }
      });
    } else if (scope === 'claim-verification') {
      this.actionErrorMessage = null;
      this.retryingClaimVerification = true;
      this.benchmarkService.retryClaimVerification(runId, assessorId).subscribe({
        next: () => {
          this.startDetailPolling(runId);
        },
        error: (err) => {
          this.retryingClaimVerification = false;
          this.actionErrorMessage = err?.error || 'Failed to retry claim verification.';
          this.cdr.detectChanges();
        }
      });
    }
  }

  rescoreRun(runId: number) {
    this.actionErrorMessage = null;
    this.rescoringRun = true;
    this.benchmarkService.rescoreRun(runId, this.selectedScoringProfileId).subscribe({
      next: () => {
        this.rescoringRun = false;
        this.viewRunDetail(runId);
        this.loadHistory();
      },
      error: (err) => {
        this.rescoringRun = false;
        this.actionErrorMessage = err?.error || 'Failed to rescore run.';
        this.cdr.detectChanges();
      }
    });
  }

  reassessAnswer(runId: number, answerId: number) {
    this.actionErrorMessage = null;
    this.reassessingAnswerId = answerId;
    this.benchmarkService.reassessAnswer(runId, answerId, this.assessorConfigId).subscribe({
      next: () => {
        this.reassessingAnswerId = null;
        this.viewRunDetail(runId);
        this.loadHistory();
      },
      error: (err) => {
        this.reassessingAnswerId = null;
        this.actionErrorMessage = err?.error || 'Failed to reassess answer.';
        this.cdr.detectChanges();
      }
    });
  }

  rerunFailed(runId: number) {
    this.benchmarkService.rerunFailedQuestions(runId).subscribe({
      next: () => {
        this.activeRunId = runId;
        this.startPolling(runId);
        this.activeSubTab = 'run';
        this.closeRunDetail();
        this.loadHistory();
      },
      error: (err) => console.error('Failed to re-run failed questions', err)
    });
  }

  downloadReport(runId: number) {
    window.open(this.benchmarkService.getRunReportUrl(runId), '_blank');
  }

  deleteRun(runId: number) {
    this.openConfirmDialog({
      title: 'Delete Benchmark Run',
      message: `Are you sure you want to delete benchmark run #${runId}?`,
      dangerNotice: 'This action is permanent and cannot be undone.',
      buttonText: 'Delete Run',
      buttonClass: 'btn-gh btn-gh-delete',
      action: () => {
        this.benchmarkService.deleteRun(runId).subscribe({
          next: () => {
            if (this.selectedRunDetail?.id === runId) {
              this.closeRunDetail();
            }
            this.loadHistory();
            this.loadAllFootprints();
          },
          error: (err) => console.error('Failed to delete run', err)
        });
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

  // --- Predicates ---

  isAnswerFailed(ans: BenchmarkRunAnswerDto): boolean {
    const s = this.formatAnswerStatus(ans.status);
    return s === 'ProviderError' || s === 'Failed' || s === 'Skipped' || s === 'EmptyAnswer';
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
    this.expandedQuestions.add(orderIndex);
    this.cdr.detectChanges();
    if (typeof document === 'undefined') return;
    const target = document.getElementById(this.answerAnchorId(orderIndex));
    target?.scrollIntoView({ block: 'start', behavior: 'smooth' });
    target?.focus?.();
  }

  // The Run Integrity Notice's question references as numbers rather than as a joined string, so
  // each one can be rendered as its own link. The joined getters stay: they are what the
  // diagnostics capture and the plain-text clauses use, and one of the two shapes had to remain.

  get criticalErrorQuestionIndexes(): number[] {
    return (this.selectedRunDetail?.answers ?? []).filter(a => a.criticalError).map(a => a.orderIndex);
  }

  get advisoryFlagQuestionIndexes(): number[] {
    return (this.selectedRunDetail?.answers ?? []).filter(a => this.hasAdvisoryFlag(a)).map(a => a.orderIndex);
  }

  get toolBudgetQuestionIndexes(): number[] {
    return (this.selectedRunDetail?.answers ?? []).filter(a => a.toolBudgetExhausted).map(a => a.orderIndex);
  }

  // --- U4: the run's wall-clock duration ---

  /**
   * Wall-clock time from start to completion, which the Answer Duration card renders as its note.
   *
   * The card's headline is the summed answer duration; on run 14 that read 25 m 29 s while the run
   * itself took 29 m 33 s, and nothing on the screen said the two were different quantities. The
   * gap is grading, verification and synthesis, and reading the first figure as the second
   * understates every one of them.
   */
  get runWallClockDurationLabel(): string | null {
    const run = this.selectedRunDetail;
    if (!run?.startedAtUtc) return null;
    const ms = elapsedMsBetween(run.startedAtUtc, run.completedAtUtc);
    return ms > 0 ? this.formatElapsed(ms) : null;
  }

  isAssessmentFailed(ans: BenchmarkRunAnswerDto): boolean {
    return this.formatAssessmentStatus(ans.assessmentStatus) === 'Failed';
  }

  isAssessmentIncomplete(ans: BenchmarkRunAnswerDto): boolean {
    const s = this.formatAssessmentStatus(ans.assessmentStatus);
    return s === 'Pending' || s === 'Assessing';
  }

  hasUnscoredAssessments(): boolean {
    return (this.selectedRunDetail?.answers ?? []).some(ans => this.isAssessmentFailed(ans) || this.isAssessmentIncomplete(ans));
  }

  hasFailedClaimVerifications(): boolean {
    return (this.selectedRunDetail?.answers ?? []).some(
      ans => !!ans.claimVerificationError && ans.claimVerificationError.trim().length > 0
    );
  }

  isRunBusy(): boolean {
    return this.formatStatus(this.selectedRunDetail?.status ?? '') === 'Running';
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
   */
  get criticalErrorAnswerCount(): number {
    return (this.selectedRunDetail?.answers ?? []).filter(a => a.criticalError).length;
  }

  /**
   * Of the flagged answers above, the ones whose raw score the cap actually lowered. A flagged
   * answer already at or below the ceiling before the cap applied is not counted here, so this
   * figure can be smaller than criticalErrorAnswerCount — it is the one "capped by" describes.
   */
  get criticalErrorCapBindingCount(): number {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => a.rawQualityScore != null && a.qualityScore != null && a.rawQualityScore > a.qualityScore)
      .length;
  }

  /** The question numbers of the critical-error answers, comma separated, for the integrity notice. */
  get criticalErrorQuestionNumbers(): string {
    return (this.selectedRunDetail?.answers ?? [])
      .filter(a => a.criticalError)
      .map(a => a.orderIndex)
      .join(', ');
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

  // --- Formatting Helpers ---

  formatStatus(status: string | number): string {
    if (status === 1 || status === 'Running') return 'Running';
    if (status === 2 || status === 'Completed') return 'Completed';
    if (status === 3 || status === 'CompletedWithErrors') return 'CompletedWithErrors';
    if (status === 4 || status === 'Failed') return 'Failed';
    if (status === 5 || status === 'Canceled') return 'Canceled';
    if (status === 6 || status === 'CompletedWithLimits') return 'CompletedWithLimits';
    return String(status);
  }

  formatStatusLabel(status: string | number): string {
    const s = this.formatStatus(status);
    if (s === 'CompletedWithLimits') return 'Completed with limits';
    if (s === 'CompletedWithErrors') return 'Completed with errors';
    return s;
  }

  formatAnswerStatus(status: string | number): string {
    if (status === 1 || status === 'Ok') return 'Ok';
    if (status === 2 || status === 'ProviderError') return 'ProviderError';
    if (status === 3 || status === 'Failed') return 'Failed';
    if (status === 4 || status === 'Skipped') return 'Skipped';
    if (status === 5 || status === 'EmptyAnswer') return 'EmptyAnswer';
    return String(status);
  }

  formatAssessmentStatus(status: string | number | undefined): string {
    if (status === 1 || status === 'Pending') return 'Pending';
    if (status === 2 || status === 'Assessing') return 'Assessing';
    if (status === 3 || status === 'Scored') return 'Scored';
    if (status === 4 || status === 'Failed') return 'Failed';
    return status != null ? String(status) : 'Scored';
  }

  formatDifficulty(diff: string | number): string {
    if (diff === 1 || diff === 'Simple') return 'Simple';
    if (diff === 2 || diff === 'Intermediate') return 'Intermediate';
    if (diff === 3 || diff === 'Advanced') return 'Advanced';
    return String(diff);
  }

  parseDifficulty(diff: string | number): number {
    if (typeof diff === 'number') return diff;
    if (diff === 'Intermediate') return 2;
    if (diff === 'Advanced') return 3;
    return 1;
  }

  getScoreBadgeClass(score: number | null | undefined): string {
    if (score == null) return 'badge-score-na';
    if (score >= 80) return 'badge-score-high';
    if (score >= 50) return 'badge-score-mid';
    return 'badge-score-low';
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

  /** A run that stopped before finishing its suite: the operator cancelled it, or it died. */
  isAbortedRun(run: BenchmarkRunSummaryDto | BenchmarkRunDetailDto): boolean {
    const status = this.formatStatus(run.status);
    return status === 'Canceled' || status === 'Failed';
  }

  /**
   * What the Duration column shows. A run that reached the end is measured by the time its answers
   * took; one that stopped early by the wall clock up to the stop, because the questions that never
   * ran are part of what was cancelled. Runs stopped before either figure was recorded fall back to
   * the two timestamps, which are always present on a terminal run.
   */
  runDurationMs(run: BenchmarkRunSummaryDto): number {
    if (this.isAbortedRun(run)) {
      return run.totalDurationMs || this.elapsedBetweenTimestamps(run);
    }
    return run.totalAnswerDurationMs || run.totalDurationMs || this.elapsedBetweenTimestamps(run);
  }

  private elapsedBetweenTimestamps(run: BenchmarkRunSummaryDto): number {
    if (!run.completedAtUtc) return 0;
    return Math.max(0, new Date(run.completedAtUtc).getTime() - new Date(run.startedAtUtc).getTime());
  }

  /**
   * How many questions a terminal run actually answered, when that is fewer than the suite holds.
   * Null while a run is still going, and null for a run that answered everything.
   *
   * `answeredQuestionCount` counts answers whose status is Ok, matching the report's "Answered
   * Questions" line: an answer that came back empty is not an answered question, even though scoring
   * method 10 scores it 0. The status already says a run had errors; this says how many, which is
   * what separates an index of 74 over 16 of 18 questions from 74 over 18.
   */
  answerShortfallOf(run: BenchmarkRunSummaryDto): { answered: number; total: number } | null {
    if (this.formatStatus(run.status) === 'Running') return null;
    const total = run.totalQuestionCount ?? 0;
    const answered = run.answeredQuestionCount ?? 0;
    if (total <= 0 || answered >= total) return null;
    return { answered, total };
  }

  formatDuration(ms: number): string {
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
    if (hours > 0) {
      return `${hours}h ${pad(mins)}m ${pad(secs)}s`;
    }
    if (mins > 0) {
      return `${mins}m ${pad(secs)}s`;
    }
    return `${secs}s`;
  }

  formatServiceTier(tier: string | null | undefined): string {
    if (!tier) return 'None';
    if (tier.toLowerCase() === 'standard_only') return 'Standard Only';
    return tier.charAt(0).toUpperCase() + tier.slice(1);
  }

  difficultyProgressLabel(suite?: BenchmarkSuiteDto | null): string {
    if (suite) {
      return `Difficulty ${suite.assessedQuestionCount}/${suite.questionCount} Assessed`;
    }
    if (!this.difficultyJob) return '';
    const total = this.difficultyJob.totalCount;
    const rated = this.difficultyJob.ratedCount;
    const failed = this.difficultyJob.failedCount;
    if (this.difficultyJob.status === 'Cancelled') {
      return `Assessment cancelled. Rated ${rated} of ${total} questions.`;
    }
    if (this.difficultyJob.status === 'Failed') {
      return `Assessment failed. Rated ${rated} of ${total} questions.`;
    }
    if (failed > 0) {
      return `Rated ${rated} of ${total} questions (${failed} failed).`;
    }
    return `Rated ${rated} of ${total} questions.`;
  }

  difficultyProgressClass(suite: BenchmarkSuiteDto): string {
    if (suite.difficultyFullyAssessed) return 'complete';
    if (suite.assessedQuestionCount === 0) return 'none';
    return 'partial';
  }

  get selectedSuite(): BenchmarkSuiteDto | undefined {
    return this.suites.find(s => s.id === this.selectedSuiteId);
  }

  /** Opens the full-screen Suite Health dialog for one suite. */
  openSuiteHealth(suite: BenchmarkSuiteDto): void {
    this.suiteHealthSuiteId = suite.id;
    // The dialog's @if content has to exist before showModal(), or an empty dialog opens.
    this.cdr.detectChanges();
    this.suiteHealthDialog?.nativeElement.showModal();
    // showModal() would otherwise focus the close button, which announces "Close" as the
    // first thing a screen-reader user hears in a dialog full of statistics.
    this.suiteHealthHeading?.nativeElement.focus();
  }

  closeSuiteHealth(): void {
    // close() fires the dialog's (close) event, so the state is cleared in one place.
    this.suiteHealthDialog?.nativeElement.close();
  }

  /** Also reached by Escape and by platform back gestures, which bypass closeSuiteHealth(). */
  onSuiteHealthDialogClose(): void {
    this.suiteHealthSuiteId = null;
    this.cdr.detectChanges();
  }

  get suiteHealthSuite(): BenchmarkSuiteDto | undefined {
    return this.suites.find(s => s.id === this.suiteHealthSuiteId);
  }

  /**
   * The panel's only outward action. It opens the question editor and writes nothing itself —
   * every finding in that panel is advisory, and a human decides what to change.
   */
  onSuiteHealthEditQuestion(suite: BenchmarkSuiteDto, questionId: number): void {
    // Close first: openManageQuestions() calls showModal() on another dialog, and two stacked
    // modals leave the user pressing Escape twice to get back to the page.
    this.suiteHealthDialog?.nativeElement.close();
    // The list loads asynchronously, so the editor cannot be opened here: it is opened by
    // loadQuestions once the question this id names actually exists in memory.
    this.pendingQuestionEditId = questionId;
    this.openManageQuestions(suite);
  }

  get selectedScoringProfile(): BenchmarkScoringProfileDto | undefined {
    return this.scoringProfiles.find(p => p.id === this.selectedScoringProfileId);
  }

  // --- Profile fit ---
  //
  // A deliberating model measured against a profile tuned for interactive latency scores badly on
  // Speed Index for a reason that says nothing about the model: the target it is compared against
  // was chosen for a different kind of workload. The pairing is legitimate, so it is an advisory
  // rather than a block — but it is shown before the run, not explained after it.
  private static readonly DELIBERATING_THINKING_LEVELS: readonly string[] = ['high', 'max'];
  private static readonly INTERACTIVE_SPEED_TARGET_MAX_MS = 30000;

  get showProfileFitAdvisory(): boolean {
    const thinkingLevel = this.selectedTestedModel?.thinkingLevel;
    const speedTargetMs = this.selectedScoringProfile?.speedTargetMs;
    if (!thinkingLevel || speedTargetMs == null) return false;

    return AdminBenchmarkComponent.DELIBERATING_THINKING_LEVELS.includes(thinkingLevel.toLowerCase()) &&
      speedTargetMs < AdminBenchmarkComponent.INTERACTIVE_SPEED_TARGET_MAX_MS;
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

    return AdminBenchmarkComponent.DELIBERATING_THINKING_LEVELS.includes(thinkingLevel.toLowerCase()) &&
      speedTargetMs < AdminBenchmarkComponent.INTERACTIVE_SPEED_TARGET_MAX_MS;
  }

  get runProfileFitAdvisoryTitle(): string {
    const level = this.selectedRunDetail?.testedModelThinkingLevelUsed ?? 'high';
    const target = this.selectedRunDetail?.scoringProfileSpeedTargetMs ?? 0;
    return `Profile targets interactive latency (${target.toLocaleString('en-US')} ms); this run used thinking level ${level} — read the Speed Index as advisory`;
  }

  /** True while any advisory makes the Speed Index non-comparable, for the shared `*` marker. */
  get speedIndexIsAdvisory(): boolean {
    return this.selectedRunDetail?.speedMeasurementDegraded === true || this.showRunProfileFitAdvisory;
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
    return `${sign}${Math.abs(delta).toFixed(1)} signed`;
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
    return `${run.secondOpinionGradedAnswerCount ?? 0}/${run.answeredQuestionCount}`;
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

  get agreementAdvisoryTitle(): string {
    const run = this.selectedRunDetail;
    const graded = run?.secondOpinionGradedAnswerCount ?? 0;
    const answered = run?.answeredQuestionCount ?? 0;
    return 'Coverage is selected by trigger, so this is conditioned on the first assessor’s own ' +
      `uncertainty, not an unbiased agreement rate. n = ${graded} of ${answered}.`;
  }

  /**
   * H5. The per-role split behind the Estimated Cost figure, which the card otherwise hides. On run 13 it
   * is the finding: the candidate was 28 % of the cost and grading plus verification 72 %, so the cost of
   * a benchmark is mostly the harness, not the model under test.
   *
   * Empty when no per-role figure is available, in which case the tooltip is not rendered at all rather
   * than shown with zeros.
   */
  get costBreakdownLabel(): string {
    const run = this.selectedRunDetail;
    if (!run) return '';

    const candidate = run.estimatedCandidateCost;
    const assessor = run.estimatedAssessorCost;
    const verifier = run.estimatedVerifierCost;
    if (candidate == null && assessor == null && verifier == null) return '';

    const total = (candidate ?? 0) + (assessor ?? 0) + (verifier ?? 0);
    const numPipe = new DecimalPipe('en-US');
    const part = (label: string, value: number | null | undefined): string | null => {
      if (value == null) return null;
      const share = total > 0 ? ` (${Math.round((value / total) * 100)}%)` : '';
      return `${label} $${numPipe.transform(value, '1.2-4')}${share}`;
    };

    const parts = [
      part('Candidate', candidate),
      part('Assessor', assessor),
      part('Claim verifier', verifier)
    ].filter((p): p is string => p !== null);

    return parts.join(' · ');
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

  // --- Integrity notice completeness ---

  get toolBudgetAnswerCount(): number {
    return (this.selectedRunDetail?.answers ?? []).filter(a => a.toolBudgetExhausted).length;
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
    );
    if (verified.length === 0) return '';
    const totalSupported = verified.reduce((sum, a) => sum + (a.claimsSupportedCount ?? 0), 0);
    const totalRefuted = verified.reduce((sum, a) => sum + (a.claimsRefutedCount ?? 0), 0);
    const totalIndeterminate = verified.reduce((sum, a) => sum + (a.claimsIndeterminateCount ?? 0), 0);
    if (verified.length === 1) {
      return `Claim verification for Q${verified[0].orderIndex}: ${totalSupported} supported, ${totalRefuted} refuted, ${totalIndeterminate} indeterminate.`;
    }
    return `Claim verification for disputed answer(s): ${totalSupported} supported, ${totalRefuted} refuted, ${totalIndeterminate} indeterminate.`;
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

  get hasInstrumentMeasurements(): boolean {
    return this.completenessOutOfScopeCount > 0 || this.readabilityFormOnlyCount > 0;
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

  get unevidencedDeductionAnswerCount(): number {
    return this.selectedRunDetail?.unevidencedDeductionAnswerCount ?? 0;
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
    if (!run?.candidatePromptOptionsJson) return null;
    try {
      const opts = JSON.parse(run.candidatePromptOptionsJson);
      const style = opts.verboseMode ? 'detailed' : 'concise';
      const tools = opts.enableToolUse !== false ? 'tools on' : 'tools off';
      return `Gameplay Help · ${style} (${tools})`;
    } catch {
      return run.candidatePromptSourceUsed || 'ChatService.BuildSystemPrompt';
    }
  }

  get candidatePromptSummary(): string | null {
    return this.candidatePromptSummaryOf(this.selectedRunDetail);
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

  /** The per-claim verifications for this answer. Empty on a malformed or absent blob. */
  claimVerificationsOf(answer: BenchmarkRunAnswerDto): { claimIndex?: number; claim: string; verdict: string; citation?: string | null; basis?: string | null }[] {
    if (!answer.claimVerificationJson) return [];
    try {
      const parsed = JSON.parse(answer.claimVerificationJson);
      return Array.isArray(parsed) ? parsed : [];
    } catch {
      return [];
    }
  }

  /** Trigger names as stored, in the words this screen uses for them. */
  secondOpinionTriggerLabel(trigger: string | null | undefined): string {
    switch (trigger) {
      case 'CriticalError': return 'critical error';
      case 'RefutedClaim': return 'refuted claim';
      case 'ContestedVerdict': return 'contested verdict';
      case 'UnevidencedDeduction': return 'unevidenced deduction';
      case 'OmissionAsAccuracy': return 'omission docked as accuracy';
      case 'UnverifiedClaims': return 'unverifiable claims';
      case 'BelowThreshold': return 'below profile threshold';
      case 'Outlier': return 'outlier below run median';
      case 'All': return 'double grading';
      case 'Manual': return 'manual trial';
      default: return trigger ?? '';
    }
  }

  // --- Calibration ---

  get selectedCalibrationAssessorModel(): SystemAiConfigDto | undefined {
    return this.benchmarkCapableConfigs.find(c => c.id === this.calibrationAssessorConfigId);
  }

  toggleCalibrationAssessorDropdown(event: Event) {
    event.stopPropagation();
    this.isCalibrationAssessorDropdownOpen = !this.isCalibrationAssessorDropdownOpen;
  }

  selectCalibrationAssessorModel(config: SystemAiConfigDto) {
    this.calibrationAssessorConfigId = config.id;
    this.isCalibrationAssessorDropdownOpen = false;
  }

  loadCalibrations(runId: number): void {
    this.loadingCalibrations = true;
    this.benchmarkService.getCalibrations(runId).subscribe({
      next: (rows) => {
        this.calibrations = rows;
        this.loadingCalibrations = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.calibrations = [];
        this.loadingCalibrations = false;
        this.calibrationErrorMessage = err?.error || 'Failed to load calibrations.';
        this.cdr.detectChanges();
      }
    });
  }

  runCalibration(runId: number): void {
    if (this.calibrationAssessorConfigId == null || this.calibrating) return;

    this.calibrating = true;
    this.calibrationErrorMessage = null;
    this.benchmarkService.calibrateAssessor(runId, this.calibrationAssessorConfigId).subscribe({
      next: () => {
        this.calibrating = false;
        this.loadCalibrations(runId);
      },
      error: (err) => {
        this.calibrating = false;
        this.calibrationErrorMessage = err?.error || 'Calibration failed.';
        this.cdr.detectChanges();
      }
    });
  }

  get canStartRun(): boolean {
    return !this.startingRun &&
      !!this.selectedSuiteId &&
      !!this.testedConfigId &&
      !!this.assessorConfigId &&
      !(this.activeRunDetail && this.formatStatus(this.activeRunDetail.status) === 'Running') &&
      !!this.selectedSuite?.difficultyFullyAssessed;
  }

  // --- Question Generation & Board Snapshot State ---
  generationDialogPhase: 'select' | 'progress' = 'select';
  isGenerationModelDropdownOpen = false;
  generationModelConfigId: number | null = null;
  generationSuiteForJob: BenchmarkSuiteDto | null = null;
  generationJobStarting = false;
  generationDialogError: string | null = null;
  generationJob: QuestionGenerationJobDto | null = null;
  generationPollInterval: any = null;
  cancellingGeneration = false;
  generationSimpleCount = 6;
  generationIntermediateCount = 6;
  generationAdvancedCount = 6;
  generationInstructions = `Write benchmark questions a GnollHack player would actually ask while looking at this exact game state. Each question must be unanswerable without the board — if it could be answered from general GnollHack knowledge alone, it belongs in the knowledge suite, not here. Vary the decision type across questions; do not ask the same thing twice in different words. In each rubric, state only board facts you can point to in the snapshot, and mark anything you infer as an inference.`;

  // --- Game Snapshot & Question Generation & Review Handlers ---

  openSnapshotViewer(snapshotId: number): void {
    this.snapshotViewer?.open(snapshotId);
  }

  onSnapshotUpdated(updated: BenchmarkGameSnapshotDto): void {
    const suite = this.suites.find(s => s.gameSnapshotId === updated.id);
    if (suite) {
      suite.gameSnapshotName = updated.name;
      suite.gameSnapshotCharCount = updated.charCount;
    }
    this.cdr.detectChanges();
  }

  get selectedRunGameSnapshotId(): number | null {
    if (!this.selectedRunDetail) return null;
    const suite = this.suites.find(s => s.id === this.selectedRunDetail!.benchmarkSuiteId);
    return suite?.gameSnapshotId ?? null;
  }

  openSuiteHealthForRubrics(suite: BenchmarkSuiteDto): void {
    this.suiteHealthInitialTab = 'board-facts';
    this.openSuiteHealth(suite);
  }

  checkSingleQuestionRubric(suite: BenchmarkSuiteDto, question: BenchmarkQuestionDto): void {
    this.suiteHealthInitialTab = 'board-facts';
    this.openSuiteHealth(suite);
  }

  toggleQuestionReview(question: BenchmarkQuestionDto): void {
    const newReviewedState = !question.isReviewed;
    this.benchmarkService.reviewQuestion(question.id, newReviewedState).subscribe({
      next: (updated) => {
        question.isReviewed = updated.isReviewed;
        question.reviewedAtRevision = updated.reviewedAtRevision;
        question.reviewedAtUtc = updated.reviewedAtUtc;
        question.reviewedByUserId = updated.reviewedByUserId;
        if (this.currentSuiteForQuestions) {
          const genQuestions = this.questions.filter(q => q.isGenerated);
          this.currentSuiteForQuestions.reviewedQuestionCount = genQuestions.filter(q => q.isReviewed).length;
        }
        this.cdr.detectChanges();
      }
    });
  }

  confirmVerifyAll(suite: BenchmarkSuiteDto): void {
    const unreviewedCount = (suite.questionCount || 0) - (suite.reviewedQuestionCount || 0);
    this.confirmDialogTitle = 'Verify All Questions';
    this.confirmDialogMessage = `Attest that you have read and verified all ${unreviewedCount} unreviewed questions in '${suite.name}' against the game board snapshot.`;
    this.confirmDialogDangerNotice = 'This records a human review attestation in the benchmark audit manifest.';
    this.confirmDialogButtonText = 'Verify All';
    this.confirmDialogButtonClass = 'btn-gh btn-gh-primary';
    this.confirmDialogIcon = 'none';
    this.pendingConfirmAction = () => {
      this.benchmarkService.reviewAllQuestions(suite.id).subscribe({
        next: (res) => {
          suite.reviewedQuestionCount = res.suite.reviewedQuestionCount;
          suite.hasGeneratedQuestions = res.suite.hasGeneratedQuestions;
          if (this.currentSuiteForQuestions?.id === suite.id) {
            this.loadQuestions(suite.id);
          }
          this.cdr.detectChanges();
        }
      });
    };
    this.confirmActionDialog?.nativeElement.showModal();
  }

  openGenerationDialog(suite: BenchmarkSuiteDto): void {
    this.generationSuiteForJob = suite;
    this.generationDialogPhase = 'select';
    this.generationDialogError = null;
    this.generationJob = null;
    this.isGenerationModelDropdownOpen = false;
    if (this.assessorConfigId && this.benchmarkCapableConfigs.some(c => c.id === this.assessorConfigId)) {
      this.generationModelConfigId = this.assessorConfigId;
    } else {
      this.generationModelConfigId = this.benchmarkCapableConfigs[0]?.id ?? null;
    }
    this.generationDialog?.nativeElement.showModal();
    this.cdr.detectChanges();
  }

  closeGenerationDialog(): void {
    this.generationDialog?.nativeElement.close();
    this.isGenerationModelDropdownOpen = false;
    this.stopGenerationPolling();
    if (this.generationJob?.status === 'Completed' && this.generationSuiteForJob) {
      this.loadSuites();
      if (this.currentSuiteForQuestions?.id === this.generationSuiteForJob.id) {
        this.loadQuestions(this.generationSuiteForJob.id);
      }
    }
  }

  toggleGenerationModelDropdown(event: Event): void {
    event.stopPropagation();
    this.isGenerationModelDropdownOpen = !this.isGenerationModelDropdownOpen;
    this.cdr.detectChanges();
  }

  selectGenerationModel(config: SystemAiConfigDto): void {
    this.generationModelConfigId = config.id;
    this.isGenerationModelDropdownOpen = false;
    this.cdr.detectChanges();
  }

  get selectedGenerationModel(): SystemAiConfigDto | undefined {
    return this.benchmarkCapableConfigs.find(c => c.id === this.generationModelConfigId);
  }

  get totalGenerationCount(): number {
    return (this.generationSimpleCount || 0) + (this.generationIntermediateCount || 0) + (this.generationAdvancedCount || 0);
  }

  confirmGeneration(): void {
    if (!this.generationSuiteForJob || !this.generationModelConfigId) return;
    if (this.totalGenerationCount <= 0) {
      this.generationDialogError = 'Please request at least one question.';
      return;
    }
    this.generationJobStarting = true;
    this.generationDialogError = null;

    this.benchmarkService.startQuestionGeneration({
      suiteId: this.generationSuiteForJob.id,
      generatorModelConfigurationId: this.generationModelConfigId,
      simpleCount: this.generationSimpleCount,
      intermediateCount: this.generationIntermediateCount,
      advancedCount: this.generationAdvancedCount,
      instructions: this.generationInstructions.trim() || undefined
    }).subscribe({
      next: (res) => {
        this.generationJobStarting = false;
        this.generationDialogPhase = 'progress';
        this.startGenerationPolling(res.jobId);
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.generationJobStarting = false;
        if (err.status === 409 && err.error) {
          this.generationJob = err.error as QuestionGenerationJobDto;
          this.generationDialogPhase = 'progress';
          this.startGenerationPolling(this.generationJob.id);
        } else {
          this.generationDialogError = err?.error?.message || err?.error || 'Failed to start question generation.';
        }
        this.cdr.detectChanges();
      }
    });
  }

  startGenerationPolling(jobId: string): void {
    this.stopGenerationPolling();
    this.pollGenerationJob(jobId);

    this.generationPollInterval = setInterval(() => {
      if (typeof document !== 'undefined' && document.hidden) return;
      this.pollGenerationJob(jobId);
    }, 2000);
  }

  stopGenerationPolling(): void {
    if (this.generationPollInterval) {
      clearInterval(this.generationPollInterval);
      this.generationPollInterval = null;
    }
  }

  pollGenerationJob(jobId: string): void {
    this.benchmarkService.getQuestionGeneration(jobId).subscribe({
      next: (job) => {
        this.generationJob = job;
        if (job.status !== 'Running') {
          this.stopGenerationPolling();
          this.loadSuites();
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.generationDialogError = err?.error?.message || err?.error || 'Failed to poll generation job.';
        this.stopGenerationPolling();
        this.cdr.detectChanges();
      }
    });
  }

  cancelGenerationJob(): void {
    if (!this.generationJob || this.generationJob.status !== 'Running') return;
    this.cancellingGeneration = true;
    this.benchmarkService.cancelQuestionGeneration(this.generationJob.id).subscribe({
      next: () => {
        this.cancellingGeneration = false;
        this.stopGenerationPolling();
        this.pollGenerationJob(this.generationJob!.id);
      },
      error: (err) => {
        this.cancellingGeneration = false;
        this.cdr.detectChanges();
      }
    });
  }

}
