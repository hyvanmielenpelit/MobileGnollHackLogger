import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { DecimalPipe } from '@angular/common';
import {
  AdminBenchmarkService,
  BenchmarkComparabilityResultDto,
  BenchmarkComparabilityTier,
  BenchmarkGroupAnalysisDto,
  BenchmarkRunGroupDto
} from '../../../../services/admin-benchmark.service';
import { InfoTipComponent } from '../../../../shared/info-tip/info-tip.component';
import { ModelIdentityComponent } from '../../../../shared/model-identity/model-identity.component';
import { parseServerUtcDate } from '../../../../utils/date.util';
import { ensureOverlayPolyfills } from '../../../../utils/polyfills.util';
import { rovingTabIndex } from '../../../../utils/roving-tab.util';
import { formatCostAmount } from '../../benchmark-run-format';
import { httpErrorText } from '../../batteries/battery.models';
import { runGroupTierClass, runGroupTierName } from '../../shared/run-group-format';

// ---------------------------------------------------------------------------------------------
// The shapes of BenchmarkGroupAnalysisDto.result and .comparison.
//
// Those two fields are typed `any` in the service because the server passes its statistics
// records straight through rather than mirroring them field by field — which is what keeps the
// two sides from drifting. The cost of that is a client with no types at all, so the render
// surface declares the subset it reads here. These are *readers*, deliberately: every field is
// optional or nullable exactly where the C# record says it may be null, and nothing is computed
// from them that the server did not already compute. Fields marked "method 2" are absent from
// analyses computed under statistics method 1.
// ---------------------------------------------------------------------------------------------

/** Method 2's interval methods: runs and questions redrawn, or questions alone below three runs. */
export type MultiRunIntervalMethod = 'crossedBootstrap' | 'questionBootstrap';

/** Cross-run statistics for one suite item. Mirrors `BenchmarkGroupItemStatistics`. */
export interface MultiRunItemStatistics {
  questionId: number;
  orderIndex: number;
  questionText?: string | null;
  runCount: number;
  mean: number;
  median: number;
  min: number;
  max: number;
  /** Sample SD (n−1). Null below two runs, where it is undefined rather than zero. */
  standardDeviation?: number | null;
  interquartileRange?: number | null;
  /** SD / mean, as a fraction. Null below two runs and at a mean of zero. */
  coefficientOfVariation?: number | null;
  meanConfidenceHalfWidth?: number | null;
  meanConfidenceLower?: number | null;
  meanConfidenceUpper?: number | null;
  /** A bound hit the [0, 100] score range and was clamped; the half-width is the honest spread. */
  meanConfidenceTruncated?: boolean;
  criticalErrorCount?: number;
  /** k/R. A rate strictly between 0 and 1 is the signal worth acting on. */
  criticalErrorRate: number;
  medianModelTimeMs?: number | null;
  /** Under method 2 set only from three runs; below that `insufficientRuns` carries the state. */
  unstable?: boolean;
  insufficientRuns?: boolean;
}

/** The pooled index and its two uncertainty components. Mirrors `BenchmarkGroupIndexStatistics`. */
export interface MultiRunIndexStatistics {
  runCount: number;
  pointEstimate: number;
  weightedMeanOfItemMeans?: number;
  /** False only on a ragged item set, where the two routes to the point estimate diverge. */
  identityHolds?: boolean;
  perRunIndices?: number[];
  meanStoredQualityIndex?: number | null;
  reproducibilityStandardDeviation?: number | null;
  reproducibilityStandardError?: number | null;
  reproducibilityCriticalValue?: number | null;
  reproducibilityHalfWidth?: number | null;
  itemSamplingStandardError?: number | null;
  itemSamplingCriticalValue?: number;
  itemSamplingHalfWidth?: number | null;
  /** Method 1: the quadrature half-width. Method 2: half the distance between the bootstrap bounds. */
  combinedHalfWidth?: number | null;
  combinedLower?: number | null;
  combinedUpper?: number | null;
  /** A combined bound hit the [0, 100] score range and was clamped. Always false under method 2. */
  combinedIntervalTruncated?: boolean;
  /** False below three runs, where no reproducibility figure is reported at all. */
  reproducibilityAvailable?: boolean;
  /** Method 2: how the combined interval was computed. */
  combinedIntervalMethod?: MultiRunIntervalMethod | null;
  /** Method 2: bootstrap replicates drawn for the combined interval. */
  bootstrapReplicates?: number | null;
  /** Method 2: the bootstrap seed, so the interval can be reproduced exactly. */
  bootstrapSeed?: number | null;
  /** Method 2: the R beyond which more questions narrow the interval more than more runs. */
  runsWhereRunComponentMatchesQuestions?: number | null;
  /** Method 2: hours between the earliest and the latest member's start. */
  runSpanHours?: number | null;
}

export interface MultiRunSpeedStatistics {
  runCount: number;
  meanSpeedIndex?: number | null;
  speedIndexStandardDeviation?: number | null;
  perRunSpeedIndices?: number[];
  pooledAnswerCount?: number;
  modelTimeP50Ms?: number | null;
  modelTimeP90Ms?: number | null;
  modelTimeMaxMs?: number | null;
  /**
   * Pooled over the answers carrying a time to first token, which is a subset of
   * `pooledAnswerCount`: model time is always recorded and TTFT is not, so the two counts differ
   * and `ttftAnswerCount` is the denominator these three percentiles belong to.
   */
  ttftP50Ms?: number | null;
  ttftP90Ms?: number | null;
  ttftMaxMs?: number | null;
  ttftAnswerCount?: number;
  /**
   * The candidate's own working time: `modelTimeMeanMs` is pooled over the same answers as the
   * percentiles above, while the two totals are per member run — `perRunTotalModelTimeMs` in member
   * order, their mean, and their sample standard deviation, which is null below two runs.
   */
  modelTimeMeanMs?: number | null;
  totalModelTimePerRunMeanMs?: number | null;
  perRunTotalModelTimeMs?: number[];
  totalModelTimeStandardDeviationMs?: number | null;
  degraded?: boolean;
  degradedReason?: string | null;
  caveat?: string | null;
}

export interface MultiRunCostStatistics {
  runCount: number;
  totalCost: number;
  meanCostPerRun: number;
  costStandardDeviation?: number | null;
  totalCostByRole?: { [role: string]: number };
  meanCostByRole?: { [role: string]: number };
  /** The per-run totals. Cost is the least reproducible quantity a replicate set measures. */
  perRunTotals?: number[];
  costStandardDeviationByRole?: { [role: string]: number | null };
  minCostByRole?: { [role: string]: number };
  maxCostByRole?: { [role: string]: number };
  costPerQuestion?: number | null;
  /** Answer rows per costed run, averaged — the per-question cost's denominator. Absent on analyses stored before it existed. */
  questionsAskedPerRun?: number | null;
  costPerIndexPoint?: number | null;
  degraded?: boolean;
  degradedReason?: string | null;
}

/**
 * One scoring dimension across the runs. Mirrors `BenchmarkGroupDimensionStatistics`.
 *
 * These means are unweighted while the index is difficulty-weighted, so the two are not expected
 * to agree — the Dimensions panel says so beside its heading.
 */
export interface MultiRunDimensionStatistics {
  dimension: string;
  perRunMeans?: number[];
  mean?: number | null;
  standardDeviation?: number | null;
  /** Method 1: run-to-run t half-width, null below three runs. Method 2: half the bootstrap interval. */
  confidenceHalfWidth?: number | null;
  min?: number | null;
  max?: number | null;
  /** Question id → cross-run mean on this dimension. */
  itemMeans?: { [questionId: string]: number };
  /** Method 2: the 95% percentile interval on the mean, every question weighted equally. */
  bootstrapLower?: number | null;
  bootstrapUpper?: number | null;
  intervalMethod?: MultiRunIntervalMethod | null;
}

/** Pooled token, tool and claim-verification totals. Mirrors `BenchmarkGroupUsageStatistics`. */
export interface MultiRunUsageStatistics {
  runCount: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalCacheReadTokens: number;
  cacheReadSharePercentage?: number | null;
  inputOutputRatio?: number | null;
  perRunInputTokens?: number[];
  inputTokenStandardDeviation?: number | null;
  totalAssessmentInputTokens: number;
  totalAssessmentOutputTokens: number;
  totalClaimVerificationInputTokens: number;
  totalClaimVerificationOutputTokens: number;
  totalToolCalls: number;
  meanToolCallsPerRun?: number | null;
  toolCallStandardDeviation?: number | null;
  /**
   * Null rather than zero when no answer reported the counter, and a null entry in
   * `perRunModelCalls` is a member that reported none — neither enters the mean or the SD. Input
   * cost tracks these rather than the tool-call totals above, because every model call resends the
   * whole conversation.
   */
  totalModelCalls?: number | null;
  meanModelCallsPerRun?: number | null;
  modelCallStandardDeviation?: number | null;
  perRunModelCalls?: (number | null)[];
  toolCallsByFamily?: { [family: string]: number };
  toolFamilyShares?: { [family: string]: number };
  claimsSupported: number;
  claimsRefuted: number;
  claimsIndeterminate: number;
  claimsChecked: number;
  answersWithVerification: number;
}

/**
 * The prompt configuration the group was graded under. Mirrors `BenchmarkGroupPromptUnderTest`.
 *
 * Read from the first member; `divergent` is the server's assertion that the others match, which a
 * poolable group guarantees and a hand-built Tier C group does not.
 */
export interface MultiRunPromptUnderTest {
  recorded: boolean;
  divergent: boolean;
  overseerMode: number;
  verboseMode: boolean;
  spoilerFreeMode: boolean;
  enableToolUse: boolean;
  enableWebSearch: boolean;
  enableSubAgents: boolean;
  allowSourceCodeReferences: boolean;
  isGameOn: boolean;
  developerMode: boolean;
  hasMessageHistory: boolean;
  hasWikiContext: boolean;
  hasGameSnapshot: boolean;
  /** 0 Disabled, 1 OnRequest, 2 Enabled — the numeric enum as the server serializes it. */
  parallelMode: number;
}

/** Mirrors `BenchmarkGroupStatisticsResult`. */
export interface MultiRunStatisticsResult {
  suiteId?: number;
  suiteName?: string;
  runIds?: number[];
  runCount: number;
  itemCount: number;
  unansweredItemCount?: number;
  items?: MultiRunItemStatistics[];
  index?: MultiRunIndexStatistics;
  speed?: MultiRunSpeedStatistics;
  cost?: MultiRunCostStatistics | null;
  /** Always four entries when present; a dimension nothing scored carries empty `perRunMeans`. */
  dimensions?: MultiRunDimensionStatistics[];
  /** Null when no member recorded any token or tool usage. */
  usage?: MultiRunUsageStatistics | null;
  promptUnderTest?: MultiRunPromptUnderTest | null;
  unstableQuestionIds?: number[];
  pooledIndexReportable?: boolean;
  varianceDecompositionCaveat?: string | null;
  /** 2 under statistics method 2; absent or null is method 1. */
  statisticsMethodVersion?: number | null;
}

/** One item's exploratory between-group difference. Mirrors `BenchmarkGroupItemComparison`. */
export interface MultiRunItemComparison {
  questionId: number;
  orderIndex: number;
  questionText?: string | null;
  baselineRunCount?: number;
  treatmentRunCount?: number;
  baselineMean: number;
  treatmentMean: number;
  difference: number;
  pValue?: number | null;
  /** A Benjamini–Hochberg q-value, not a p-value. */
  adjustedPValue?: number | null;
  rejectedAtFdr?: boolean;
  /** Always true on the server, and present precisely so no surface can render one unlabeled. */
  exploratory?: boolean;
  /** Method 2: no p-value exists, and the item is left out of the false-discovery-rate step. */
  untestable?: boolean | null;
}

/** One comparability key whose value differs between the two groups. Mirrors `BenchmarkGroupPairKeyDifference`. */
export interface MultiRunPairKeyDifference {
  key: string;
  kind: string;
  description?: string | null;
  baselineValue?: string | null;
  treatmentValue?: string | null;
}

/** Mirrors `BenchmarkGroupComparison`. */
export interface MultiRunComparison {
  baselineRunIds?: number[];
  treatmentRunIds?: number[];
  pairedItemCount: number;
  /** Items present on one side only, excluded from pairing. */
  unpairedItemCount?: number;
  /** Items on both sides but graded under different rubric revisions, excluded from pairing. */
  revisionMismatchedItemCount?: number;
  meanDifference: number;
  differenceStandardDeviation?: number | null;
  differenceConfidenceHalfWidth?: number | null;
  differenceConfidenceLower?: number | null;
  differenceConfidenceUpper?: number | null;
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
    tStatistic?: number | null;
    pValue?: number | null;
  };
  cohensDz?: number | null;
  itemComparisons?: MultiRunItemComparison[];
  falseDiscoveryRate?: number;
  exploratoryNote?: string | null;
  varianceDecompositionCaveat?: string | null;
  /** 2 under statistics method 2; absent or null is method 1. */
  statisticsMethodVersion?: number | null;
  /** Method 2: treatment minus baseline index over the paired items, treatment weights on both sides. */
  indexDifference?: number | null;
  indexDifferenceLower?: number | null;
  indexDifferenceUpper?: number | null;
  indexDifferenceMethod?: MultiRunIntervalMethod | null;
  /** Method 2: false under the question bootstrap, where a run-wide shift is not in the interval. */
  runVarianceIncluded?: boolean | null;
  indexDifferenceBootstrapP?: number | null;
  indexDifferenceStandardError?: number | null;
  hodgesLehmannShift?: number | null;
  standardizedDifferenceDav?: number | null;
  minimumDetectableIndexDifference?: number | null;
  untestableItemCount?: number | null;
  pairKeyDifferences?: MultiRunPairKeyDifference[] | null;
  priorComparisonCount?: number | null;
}

/**
 * What multi-run cannot decompose, held here as a fallback so the sentence can never be missing.
 *
 * The server carries the authoritative text on the statistics result itself
 * (`BenchmarkGroupStatisticsResult.VarianceDecompositionCaveat`) for exactly this reason; this
 * copy exists only for the case where the result is present but that field is not — an analysis
 * persisted by an older build, say. Without the sentence an operator reads an unstable item as a
 * fact about the model, when it may be a fact about the grader.
 */
export const VARIANCE_DECOMPOSITION_CAVEAT =
  'Run-to-run variance mixes candidate stochasticity with grader stochasticity: each run produces a '
  + 'new answer, which is then graded once. An unstable item may mean the model answers it '
  + 'differently each time, or that the grader scores equivalent answers differently. Separating the '
  + 'two requires re-grading identical answers — a second reading of every answer, or a re-assessment '
  + 'pass over stored answers.';

/** The standing label on per-item differences. Same reason as above: it may never be absent. */
export const EXPLORATORY_ITEM_TEST_NOTE =
  'Per-item differences are exploratory, under Benjamini–Hochberg false-discovery-rate control. '
  + 'The adjusted column is a q-value, not a p-value. Eighteen simultaneous item tests without '
  + 'correction would manufacture findings on data with no effect in it.';

/** The dialog's sections, in tab order. */
export type GroupAnalysisSection = 'summary' | 'dimensions' | 'items' | 'speed' | 'tokens' | 'comparison';

export const GROUP_ANALYSIS_SECTIONS: readonly { id: GroupAnalysisSection; label: string }[] = [
  { id: 'summary', label: 'Summary' },
  { id: 'dimensions', label: 'Dimensions' },
  { id: 'items', label: 'Items' },
  { id: 'speed', label: 'Speed & cost' },
  { id: 'tokens', label: 'Tokens & tools' },
  { id: 'comparison', label: 'Comparison' }
];

/** The tiers a group may be paired against: the poolable ones. */
const POOLABLE_TIERS: readonly BenchmarkComparabilityTier[] = ['Replicate', 'QualityComparable'];

const DECIMAL = new DecimalPipe('en-US');

/** A stability verdict for one item row. */
export type MultiRunItemStability = 'unstable' | 'few' | 'stable';

/**
 * GnollBench → Multi-Run Analysis: one group's stored analysis in a modal dialog, in six section
 * tabs, with Recompute, Compare with another group of the suite, and the Markdown report download.
 *
 * Renders both statistics methods. Method 1 (an absent or null `statisticsMethodVersion`) shows the
 * two uncertainty components and their quadrature sum; method 2 shows the crossed bootstrap's
 * percentile interval, with the two components kept as diagnostics, and a comparison whose primary
 * estimand is the difficulty-weighted index difference.
 *
 * Two display rules are load-bearing rather than cosmetic:
 *
 * 1. **The two uncertainty components are rendered separately, each labeled with the question it
 *    answers.** The item-sampling component does **not** shrink with *R*, because every run answers
 *    the same items, and the card says so.
 * 2. **Per-item differences are always labeled exploratory**, with the Benjamini–Hochberg note.
 *
 * It writes nothing except through the analysis endpoint, and tells the host through
 * `analysisChanged` when it has stored a new analysis.
 */
@Component({
  selector: 'app-group-analysis-dialog',
  standalone: true,
  imports: [InfoTipComponent, ModelIdentityComponent],
  templateUrl: './group-analysis-dialog.component.html',
  styleUrls: ['./group-analysis-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class GroupAnalysisDialogComponent implements OnInit {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);

  /** The suite's groups, for Compare with. */
  @Input() groups: BenchmarkRunGroupDto[] = [];

  /** The suite's name for the subtitle; the group's own when null. */
  @Input() suiteName: string | null = null;

  /** The group whose analysis was computed or recomputed here. */
  @Output() analysisChanged = new EventEmitter<number>();

  @ViewChild('dialog', { static: true }) dialogRef?: ElementRef<HTMLDialogElement>;

  readonly sections = GROUP_ANALYSIS_SECTIONS;
  readonly tierClass = runGroupTierClass;
  readonly tierName = runGroupTierName;

  group: BenchmarkRunGroupDto | null = null;
  groupError: string | null = null;

  /**
   * The group's comparability, re-derived through the read-only preview endpoint over its own
   * member run ids. The group DTO carries the tier but not which of speed and cost it degrades.
   */
  detailComparability: BenchmarkComparabilityResultDto | null = null;

  analysis: BenchmarkGroupAnalysisDto | null = null;
  loadingAnalysis = false;
  analysing = false;
  /** A failed load or Recompute. */
  analysisError: string | null = null;
  /** A refused or failed Compare, shown under the Compare with select. */
  compareError: string | null = null;
  /** The baseline Compare pairs this group against. */
  compareWithGroupId: number | null = null;

  activeSection: GroupAnalysisSection = 'summary';

  /** Moves on every open and close, so a late response for an earlier group is dropped. */
  private loadToken = 0;
  private candidateMemo: { groups: BenchmarkRunGroupDto[]; group: BenchmarkRunGroupDto; rows: BenchmarkRunGroupDto[] } | null = null;

  readonly varianceCaveatFallback = VARIANCE_DECOMPOSITION_CAVEAT;
  readonly exploratoryNoteFallback = EXPLORATORY_ITEM_TEST_NOTE;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  // --- Open and close -----------------------------------------------------------------------------

  /** Shows the dialog on the Summary tab and loads the group, its comparability and its latest analysis. */
  open(group: BenchmarkRunGroupDto): void {
    const token = ++this.loadToken;
    this.group = group;
    this.groupError = null;
    this.detailComparability = null;
    this.analysis = null;
    this.analysisError = null;
    this.compareError = null;
    this.compareWithGroupId = null;
    this.analysing = false;
    this.activeSection = 'summary';
    this.loadGroup(group.id, token);
    this.loadAnalysis(group.id, token);
    // The content is gated on `group`; rendered first, so showModal() never opens an empty box.
    this.cdr.detectChanges();
    const dialog = this.dialogRef?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
  }

  close(): void {
    const dialog = this.dialogRef?.nativeElement;
    if (dialog?.open) {
      dialog.close();
    }
    this.clear();
  }

  /** Escape and the close buttons; a close event arriving after a reopen is ignored. */
  onDialogClose(): void {
    if (this.dialogRef?.nativeElement.open) return;
    this.clear();
  }

  private clear(): void {
    this.loadToken++;
    this.group = null;
    this.groupError = null;
    this.detailComparability = null;
    this.analysis = null;
    this.analysisError = null;
    this.compareError = null;
    this.compareWithGroupId = null;
    this.loadingAnalysis = false;
    this.analysing = false;
    this.cdr.markForCheck();
  }

  private loadGroup(id: number, token: number): void {
    this.benchmarkService.getRunGroup(id).subscribe({
      next: (group) => {
        if (token !== this.loadToken || !group) return;
        this.group = group;
        this.loadDetailComparability(group, token);
        this.cdr.markForCheck();
      },
      error: (err) => {
        if (token !== this.loadToken) return;
        this.groupError = httpErrorText(err, 'Failed to load the group.');
        this.cdr.markForCheck();
      }
    });
  }

  /** Read-only: the preview endpoint creates nothing, which is what makes it safe to call here. */
  private loadDetailComparability(group: BenchmarkRunGroupDto, token: number): void {
    const runIds = (group.members ?? []).map(m => m.runId);
    if (runIds.length === 0) return;

    this.benchmarkService.previewRunGroupTier({
      name: group.name,
      runIds,
      crossCondition: group.crossCondition
    }).subscribe({
      next: (result) => {
        if (token !== this.loadToken) return;
        this.detailComparability = result?.comparability ?? null;
        this.cdr.markForCheck();
      },
      // A failure costs the degraded flags the tier adds, not the analysis, so it stays silent.
      error: () => { }
    });
  }

  private loadAnalysis(id: number, token: number): void {
    this.loadingAnalysis = true;
    this.benchmarkService.getRunGroupAnalysis(id).subscribe({
      next: (analysis) => {
        if (token !== this.loadToken) return;
        this.analysis = analysis;
        this.loadingAnalysis = false;
        this.compareWithGroupId = this.candidateOrNull(analysis?.comparedWithGroupId);
        this.cdr.markForCheck();
      },
      error: (err) => {
        if (token !== this.loadToken) return;
        this.loadingAnalysis = false;
        this.analysisError = httpErrorText(err, 'Failed to load the stored analysis.');
        this.cdr.markForCheck();
      }
    });
  }

  // --- Recompute and Compare ----------------------------------------------------------------------

  /** Computes the analysis again, against the same baseline as the stored one. */
  recompute(): void {
    this.runAnalysis(this.analysis?.comparedWithGroupId ?? null, 'recompute');
  }

  /** Computes the analysis paired against the chosen baseline; refused while none is chosen. */
  compare(): void {
    if (this.compareWithGroupId == null) return;
    this.runAnalysis(this.compareWithGroupId, 'compare');
  }

  onCompareChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    const id = value === '' ? null : Number(value);
    this.compareWithGroupId = id != null && Number.isFinite(id) ? id : null;
    this.compareError = null;
  }

  private runAnalysis(compareWithGroupId: number | null, source: 'recompute' | 'compare'): void {
    const group = this.group;
    if (!group || this.analysing) return;

    const token = this.loadToken;
    this.analysing = true;
    this.analysisError = null;
    this.compareError = null;
    this.cdr.markForCheck();
    this.benchmarkService.analyseRunGroup(group.id, { compareWithGroupId }).subscribe({
      next: (analysis) => {
        if (token !== this.loadToken) return;
        this.analysis = analysis;
        this.analysing = false;
        this.compareWithGroupId = this.candidateOrNull(analysis?.comparedWithGroupId) ?? this.compareWithGroupId;
        this.analysisChanged.emit(group.id);
        this.cdr.markForCheck();
      },
      error: (err) => {
        if (token !== this.loadToken) return;
        this.analysing = false;
        const message = httpErrorText(err, 'The group analysis failed.');
        if (source === 'compare') {
          this.compareError = message;
        } else {
          this.analysisError = message;
        }
        this.cdr.markForCheck();
      }
    });
  }

  /**
   * The groups this one may be paired against: poolable (Tier A or B), of the same suite, and not
   * itself. Kept as one array until the list or the group changes.
   */
  get comparisonCandidates(): BenchmarkRunGroupDto[] {
    const group = this.group;
    if (!group) return [];
    const memo = this.candidateMemo;
    if (memo && memo.groups === this.groups && memo.group === group) {
      return memo.rows;
    }
    const rows = (this.groups ?? []).filter(g =>
      g.id !== group.id
      && POOLABLE_TIERS.includes(g.tier)
      && (group.benchmarkSuiteId == null || g.benchmarkSuiteId === group.benchmarkSuiteId));
    this.candidateMemo = { groups: this.groups, group, rows };
    return rows;
  }

  /** `#12 · GPT-5.6 Luna · R = 3 · Tier A`. */
  candidateLabel(group: BenchmarkRunGroupDto): string {
    return `#${group.id} · ${this.modelName(group)} · R = ${group.runCount} · ${this.tierShort(group.tier)}`;
  }

  private candidateOrNull(id: number | null | undefined): number | null {
    if (id == null) return null;
    return this.comparisonCandidates.some(g => g.id === id) ? id : null;
  }

  // --- Section tabs -------------------------------------------------------------------------------

  selectSection(section: GroupAnalysisSection): void {
    this.activeSection = section;
  }

  onSectionKeydown(event: KeyboardEvent, index: number): void {
    const next = rovingTabIndex(event, index, this.sections.length);
    if (next === null) return;
    const section = this.sections[next].id;
    this.selectSection(section);
    this.cdr.detectChanges();
    document.getElementById(this.tabId(section))?.focus();
  }

  tabId(section: GroupAnalysisSection): string {
    return `mr-an-tab-${section}`;
  }

  panelId(section: GroupAnalysisSection): string {
    return `mr-an-panel-${section}`;
  }

  // --- Header -------------------------------------------------------------------------------------

  modelName(group: BenchmarkRunGroupDto): string {
    return group.testedModelDisplayName || group.testedModelId || 'Model not recorded';
  }

  /** `Tier A`, `Tier B`, `Tier C` or `Not comparable`. */
  tierShort(tier: BenchmarkComparabilityTier | string | null | undefined): string {
    return runGroupTierName(tier).split(' — ')[0];
  }

  /** The member run ids, ascending. */
  memberRunIds(group: BenchmarkRunGroupDto): number[] {
    if (group.memberRunIds?.length) {
      return group.memberRunIds;
    }
    return (group.members ?? []).map(member => member.runId).sort((a, b) => a - b);
  }

  /** `Group #12 · Suite · runs #41, #42, #43`. */
  subtitle(group: BenchmarkRunGroupDto): string {
    const parts = [`Group #${group.id}`, this.suiteName || group.suiteName || 'Suite unknown'];
    const ids = this.memberRunIds(group);
    if (ids.length > 0) {
      parts.push(`runs ${this.runIdsLabel(ids)}`);
    }
    return parts.join(' · ');
  }

  /** Whether the stored analysis no longer describes the membership. */
  get analysisStale(): boolean {
    return this.analysis ? this.analysis.stale : this.group?.analysisStale === true;
  }

  // --- Report download ----------------------------------------------------------------------------
  //
  // window.open against the URL the service builds, as the single-run report download does. Not an
  // XHR and not an <a download>: the endpoint answers with a Content-Disposition filename the server
  // chose, and fetching it into a blob would throw that name away.

  /** The report is built from the stored analysis, so there is nothing to download until one exists. */
  get canDownloadReport(): boolean {
    return this.analysis != null || this.group?.latestAnalysisId != null;
  }

  get downloadReportTooltip(): string {
    if (!this.canDownloadReport) {
      return 'No analysis yet — run the analysis before downloading a report';
    }
    return this.analysisStale
      ? 'Download the Markdown report. The analysis is stale: the membership changed after it was computed.'
      : 'Download the Markdown report';
  }

  downloadReport(): void {
    const group = this.group;
    if (!group || !this.canDownloadReport) return;
    window.open(this.benchmarkService.getGroupReportUrl(group.id), '_blank');
  }

  // --- Analysis readers ---------------------------------------------------------------------------

  get result(): MultiRunStatisticsResult | null {
    return (this.analysis?.result as MultiRunStatisticsResult) ?? null;
  }

  get comparison(): MultiRunComparison | null {
    return (this.analysis?.comparison as MultiRunComparison) ?? null;
  }

  /** The statistics method of the stored result: 1 when the field is absent. */
  get methodVersion(): number {
    return this.result?.statisticsMethodVersion ?? 1;
  }

  /** The statistics method of the stored comparison: 1 when the field is absent. */
  get comparisonMethodVersion(): number {
    return this.comparison?.statisticsMethodVersion ?? 1;
  }

  get indexStats(): MultiRunIndexStatistics | null {
    return this.result?.index ?? null;
  }

  /**
   * The note on analyzed members that withhold their own index because a question failed at the
   * provider; null when none does.
   */
  get withheldIndexNote(): string | null {
    const analyzed = new Set(this.analysis?.memberRunIds ?? []);
    const withheld = (this.group?.members ?? [])
      .filter(member => member.indexWithheld && analyzed.has(member.runId))
      .map(member => member.runId)
      .sort((a, b) => a - b);
    if (withheld.length === 0) return null;
    const ids = withheld.map(id => `#${id}`).join(', ');
    return withheld.length === 1
      ? `Run ${ids} withholds its own index (a question failed at the provider); its per-run index here is over its scored answers.`
      : `Runs ${ids} withhold their own index (a question failed at the provider); their per-run indices here are over their scored answers.`;
  }

  get itemStats(): MultiRunItemStatistics[] {
    return this.result?.items ?? [];
  }

  get speedStats(): MultiRunSpeedStatistics | null {
    return this.result?.speed ?? null;
  }

  get costStats(): MultiRunCostStatistics | null {
    return this.result?.cost ?? null;
  }

  /** Only the dimensions something actually scored. An unscored one has nothing to render. */
  get dimensionStats(): MultiRunDimensionStatistics[] {
    return (this.result?.dimensions ?? []).filter(d => (d.perRunMeans?.length ?? 0) > 0);
  }

  get usageStats(): MultiRunUsageStatistics | null {
    return this.result?.usage ?? null;
  }

  get promptStats(): MultiRunPromptUnderTest | null {
    return this.result?.promptUnderTest ?? null;
  }

  /**
   * The dimension trailing the strongest one, and by how much — null when there is nothing to
   * compare. Named separately because a gap is the finding, and a reader scanning four rows for it
   * is doing the panel's job.
   */
  get lowestDimension(): { dimension: string; mean: number; gap: number } | null {
    const scored = this.dimensionStats.filter(d => d.mean != null);
    if (scored.length < 2) return null;

    const sorted = [...scored].sort((a, b) => (a.mean ?? 0) - (b.mean ?? 0));
    const weakest = sorted[0];
    const strongest = sorted[sorted.length - 1];
    if (weakest.dimension === strongest.dimension) return null;

    return {
      dimension: weakest.dimension,
      mean: weakest.mean!,
      gap: (strongest.mean ?? 0) - (weakest.mean ?? 0)
    };
  }

  /**
   * The three weakest items on one dimension, labeled with the Q numbers the rest of the dialog
   * uses rather than with raw question ids.
   */
  lowestItemsFor(dimension: MultiRunDimensionStatistics): string {
    const means = dimension.itemMeans ?? {};
    const entries = Object.keys(means).map(key => ({ questionId: Number(key), mean: means[key] }));
    if (entries.length === 0) return '—';

    return entries
      .sort((a, b) => a.mean - b.mean)
      .slice(0, 3)
      .map(entry => {
        const item = this.itemStats.find(i => i.questionId === entry.questionId);
        const label = item ? `Q${item.orderIndex}` : `id ${entry.questionId}`;
        return `${label} (${this.formatNumber(entry.mean, 1)})`;
      })
      .join(', ');
  }

  /** The 95% column: the bootstrap bounds under method 2, the run-to-run half-width under method 1. */
  dimensionInterval(dimension: MultiRunDimensionStatistics): string {
    if (this.methodVersion >= 2) {
      return this.formatRange(dimension.bootstrapLower, dimension.bootstrapUpper, 1);
    }
    return this.formatNumber(dimension.confidenceHalfWidth, 1);
  }

  dimensionUsesQuestionBootstrap(dimension: MultiRunDimensionStatistics): boolean {
    return this.methodVersion >= 2 && dimension.intervalMethod === 'questionBootstrap';
  }

  /** Method 2 calls an item unstable only from three runs; below that it has too few runs to say. */
  itemStability(item: MultiRunItemStatistics): MultiRunItemStability {
    const tooFew = this.methodVersion >= 2 && item.runCount < 3;
    if (item.unstable && !tooFew) return 'unstable';
    if (item.insufficientRuns || tooFew) return 'few';
    return 'stable';
  }

  /** Tool families in descending call order, for the usage table. */
  get toolFamilyRows(): { family: string; calls: number; share: number | null }[] {
    const usage = this.usageStats;
    if (!usage?.toolCallsByFamily) return [];

    return Object.keys(usage.toolCallsByFamily)
      .map(family => ({
        family,
        calls: usage.toolCallsByFamily![family],
        share: usage.toolFamilyShares?.[family] ?? null
      }))
      .sort((a, b) => b.calls - a.calls);
  }

  /** A cost role's display name. Analyses stored before 2026-09-27 key the second reader as 'Second opinion'. */
  roleLabel(role: string): string {
    return role === 'Second opinion' ? 'Second reader' : role;
  }

  /** Cost roles in descending total order, with the dispersion figures beside each. */
  get costRoleRows(): {
    role: string; total: number; mean: number | null; sd: number | null;
    min: number | null; max: number | null; share: number | null;
  }[] {
    const cost = this.costStats;
    if (!cost?.totalCostByRole) return [];

    return Object.keys(cost.totalCostByRole)
      .map(role => ({
        role,
        total: cost.totalCostByRole![role],
        mean: cost.meanCostByRole?.[role] ?? null,
        sd: cost.costStandardDeviationByRole?.[role] ?? null,
        min: cost.minCostByRole?.[role] ?? null,
        max: cost.maxCostByRole?.[role] ?? null,
        share: cost.totalCost > 0 ? (cost.totalCostByRole![role] / cost.totalCost) * 100 : null
      }))
      .sort((a, b) => b.total - a.total);
  }

  /**
   * The role carrying most of the cost spread. A replicate set whose quality reproduces to a tenth
   * of a point can still spend twice as much on one member as another, and which role did that is
   * the actionable half.
   */
  get widestCostRole(): { role: string; sd: number } | null {
    const rows = this.costRoleRows.filter(r => r.sd != null && r.sd > 0);
    if (rows.length === 0) return null;

    const widest = rows.reduce((a, b) => ((b.sd ?? 0) > (a.sd ?? 0) ? b : a));
    return { role: widest.role, sd: widest.sd! };
  }

  get itemComparisons(): MultiRunItemComparison[] {
    return this.comparison?.itemComparisons ?? [];
  }

  /**
   * Speed figures mix timing conditions. Either source may say so — the statistics record for the
   * runs actually aggregated, and the tier for the set as a whole — and either one is enough to
   * flag the section, because an unflagged degraded aggregate is the failure that matters.
   */
  get speedDegraded(): boolean {
    return this.speedStats?.degraded === true
      || this.detailComparability?.speedAggregatesDegraded === true;
  }

  get costDegraded(): boolean {
    return this.costStats?.degraded === true
      || this.detailComparability?.costAggregatesDegraded === true;
  }

  get speedDegradedReason(): string {
    return this.speedStats?.degradedReason
      || 'The group is not a replicate set on timing: question parallelism or thinking level differs across members.';
  }

  get costDegradedReason(): string {
    return this.costStats?.degradedReason
      || 'The pricing snapshot or the timing mode differs across members. A cost mismatch degrades cost only — prices cannot move a quality score.';
  }

  /** Never empty. See {@link VARIANCE_DECOMPOSITION_CAVEAT}. */
  get varianceCaveat(): string {
    return this.result?.varianceDecompositionCaveat || this.varianceCaveatFallback;
  }

  /** Never empty, for the same reason. */
  get exploratoryNote(): string {
    return this.comparison?.exploratoryNote || this.exploratoryNoteFallback;
  }

  // --- Method 2 -----------------------------------------------------------------------------------

  /** Whether the combined interval redrew the runs as well as the questions. */
  get crossedInterval(): boolean {
    const index = this.indexStats;
    if (index?.combinedIntervalMethod) {
      return index.combinedIntervalMethod === 'crossedBootstrap';
    }
    return (index?.runCount ?? 0) >= 3;
  }

  get intervalMethodLine(): string {
    return this.crossedInterval
      ? 'Crossed bootstrap over runs and questions'
      : 'Question bootstrap — fewer than three runs, so run-to-run variation is not included';
  }

  /** The point past which more questions buy more than more runs; null when not computed. */
  get planningLine(): string | null {
    const runs = this.indexStats?.runsWhereRunComponentMatchesQuestions;
    if (runs == null) return null;
    return `From about ${runs} runs, more questions narrow the interval more than more runs.`;
  }

  /** The members ran back to back, so the run component measures re-running now. */
  get runSpanWarning(): string | null {
    const index = this.indexStats;
    const hours = index?.runSpanHours;
    if (this.methodVersion < 2 || hours == null || hours >= 24 || (index?.runCount ?? 0) < 3) return null;
    const whole = Math.max(1, Math.ceil(hours));
    return `All runs were made within ${whole} ${whole === 1 ? 'hour' : 'hours'}; the run component measures re-running now, not on another day.`;
  }

  get bootstrapReplicatesLabel(): string {
    const replicates = this.indexStats?.bootstrapReplicates ?? 10000;
    return DECIMAL.transform(replicates, '1.0-0') ?? String(replicates);
  }

  get pairKeyDifferences(): MultiRunPairKeyDifference[] {
    return this.comparison?.pairKeyDifferences ?? [];
  }

  get priorComparisonWarning(): string | null {
    const count = this.comparison?.priorComparisonCount;
    if (count == null || count < 2) return null;
    return `This group has been compared ${count} times before. A difference found after several looks is weaker evidence; decide what difference matters before looking.`;
  }

  get untestableNote(): string | null {
    const count = this.comparison?.untestableItemCount ?? this.itemComparisons.filter(i => i.untestable === true).length;
    if (!count) return null;
    return `${count} item(s) are untestable: a side has fewer than two runs, or neither side varies, so no p-value exists. They are left out of the false-discovery-rate step.`;
  }

  // --- Formatting ---------------------------------------------------------------------------------

  /**
   * Money at four decimals fixed, so a sub-cent cost like $0.0007 renders as $0.0007, not $0.00,
   * and zero renders as $0.0000; a missing or non-finite value renders as a dash. Matches
   * `formatRunEstimatedCost` on the run cards.
   */
  readonly formatCost = (value: number | null | undefined): string => formatCostAmount(value, '—');

  /** `1 question`, `18 questions`, `17.5 questions`. */
  formatQuestionsAsked(n: number): string {
    const number = Number.isInteger(n) ? String(n) : n.toFixed(1);
    return `${number} ${n === 1 ? 'question' : 'questions'}`;
  }

  /** The first eight hex characters of an instrument hash, as the run list shows them. */
  shortFingerprint(sha: string | null | undefined): string {
    return sha ? sha.substring(0, 8) : '—';
  }

  formatNumber(value: number | null | undefined, digits = 1): string {
    if (value == null || !Number.isFinite(value)) return '—';
    return DECIMAL.transform(value, `1.${digits}-${digits}`) ?? '—';
  }

  /** A coefficient of variation arrives as a fraction and reads as a percentage. */
  formatPercentFromFraction(value: number | null | undefined): string {
    if (value == null) return '—';
    return `${this.formatNumber(value * 100, 1)}%`;
  }

  formatRate(value: number | null | undefined): string {
    if (value == null) return '—';
    return `${this.formatNumber(value * 100, 0)}%`;
  }

  formatMs(value: number | null | undefined): string {
    if (value == null) return '—';
    if (value < 1000) return `${Math.round(value)} ms`;
    return `${this.formatNumber(value / 1000, 1)} s`;
  }

  /** A server timestamp in the browser's locale and time zone; one without a zone is UTC. */
  formatDate(value: string | null | undefined): string {
    if (!value) return '—';
    const parsed = parseServerUtcDate(value);
    return isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString();
  }

  /**
   * p-values below the smallest figure the column can show are rendered as a bound rather than as
   * 0.000, which would claim a certainty no test produces. A bootstrap p of 0 lands here too: it is
   * below the resampling's resolution, not zero.
   */
  formatPValue(value: number | null | undefined): string {
    if (value == null) return '—';
    if (value < 0.001) return '< 0.001';
    return this.formatNumber(value, 3);
  }

  formatSigned(value: number | null | undefined, digits = 1): string {
    if (value == null || !Number.isFinite(value)) return '—';
    const formatted = this.formatNumber(value, digits);
    return value > 0 ? `+${formatted}` : formatted;
  }

  /** `62.2 – 80.4`, signed when the bounds are differences; a dash when either is missing. */
  formatRange(lower: number | null | undefined, upper: number | null | undefined, digits = 1, signed = false): string {
    if (lower == null || upper == null) return '—';
    const format = (v: number) => signed ? this.formatSigned(v, digits) : this.formatNumber(v, digits);
    return `${format(lower)} – ${format(upper)}`;
  }

  /** Token counts, abbreviated: a benchmark set runs to millions and raw digits do not read. */
  formatTokens(value: number | null | undefined): string {
    if (value == null) return '—';
    if (value >= 1_000_000) return `${this.formatNumber(value / 1_000_000, 2)} M`;
    if (value >= 1000) return `${this.formatNumber(value / 1000, 1)} k`;
    return `${Math.round(value)}`;
  }

  /** A percentage that arrives already scaled, unlike `formatPercentFromFraction`. */
  formatPercent(value: number | null | undefined, digits = 1): string {
    if (value == null) return '—';
    return `${this.formatNumber(value, digits)}%`;
  }

  /** The batching mode, whose numeric enum selects a tool policy file and so is prompt text. */
  parallelModeLabel(mode: number | null | undefined): string {
    switch (mode) {
      case 0: return 'Disabled';
      case 1: return 'On request';
      case 2: return 'Enabled';
      default: return '—';
    }
  }

  runIdsLabel(runIds: number[] | null | undefined): string {
    if (!runIds || runIds.length === 0) return '—';
    return runIds.map(id => `#${id}`).join(', ');
  }
}
