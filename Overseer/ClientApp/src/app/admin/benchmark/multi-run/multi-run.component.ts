import { ChangeDetectorRef, Component, Input, OnChanges, OnInit, SimpleChanges, inject } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import {
  AdminBenchmarkService,
  BenchmarkComparabilityResultDto,
  BenchmarkComparabilityTier,
  BenchmarkGroupAnalysisDto,
  BenchmarkRunGroupDto,
  BenchmarkRunGroupTierPreviewDto,
  BenchmarkRunSummaryDto
} from '../../../services/admin-benchmark.service';

// ---------------------------------------------------------------------------------------------
// The shapes of BenchmarkGroupAnalysisDto.result and .comparison.
//
// Those two fields are typed `any` in the service because the server passes its statistics
// records straight through rather than mirroring them field by field — which is what keeps the
// two sides from drifting. The cost of that is a client with no types at all, so the render
// surface declares the subset it reads here. These are *readers*, deliberately: every field is
// optional or nullable exactly where the C# record says it may be null, and nothing is computed
// from them that the server did not already compute.
// ---------------------------------------------------------------------------------------------

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
  criticalErrorCount?: number;
  /** k/R. A rate strictly between 0 and 1 is the signal worth acting on. */
  criticalErrorRate: number;
  medianModelTimeMs?: number | null;
  unstable?: boolean;
  insufficientRuns?: boolean;
}

/** The pooled index and its two independent uncertainty components. Mirrors `BenchmarkGroupIndexStatistics`. */
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
  combinedHalfWidth?: number | null;
  combinedLower?: number | null;
  combinedUpper?: number | null;
  /** False below three runs, where no reproducibility figure is reported at all. */
  reproducibilityAvailable?: boolean;
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
  costPerQuestion?: number | null;
  costPerIndexPoint?: number | null;
  degraded?: boolean;
  degradedReason?: string | null;
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
  unstableQuestionIds?: number[];
  pooledIndexReportable?: boolean;
  varianceDecompositionCaveat?: string | null;
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
  /** Always true on the server, and present precisely so no surface can render one unlabelled. */
  exploratory?: boolean;
}

/** Mirrors `BenchmarkGroupComparison`. */
export interface MultiRunComparison {
  baselineRunIds?: number[];
  treatmentRunIds?: number[];
  pairedItemCount: number;
  unpairedItemCount?: number;
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
  + 'two requires re-grading identical answers — second opinion on every answer, or a re-assessment '
  + 'pass over stored answers.';

/** The standing label on per-item differences. Same reason as above: it may never be absent. */
export const EXPLORATORY_ITEM_TEST_NOTE =
  'Per-item differences are exploratory, under Benjamini–Hochberg false-discovery-rate control. '
  + 'The adjusted column is a q-value, not a p-value. Eighteen simultaneous item tests without '
  + 'correction would manufacture findings on data with no effect in it.';

/**
 * Multi-Run Analysis: replicate sets and what may be said about them.
 *
 * Three surfaces in one panel — the group list, the group builder, and the group detail with its
 * statistics — because they are three steps of one operation and splitting them would mean an
 * operator switching views to find out why a set was refused.
 *
 * Two display rules here are load-bearing rather than cosmetic, and are the reason this component
 * exists at all instead of a table of numbers:
 *
 * 1. **The two uncertainty components are rendered separately, each labelled with the question it
 *    answers, before the combined interval.** The item-sampling component does **not** shrink with
 *    *R*, because every run answers the same items, and the UI says so in as many words. A reader
 *    who expects the whole interval to fall as √*R* concludes the code is broken; this is a named
 *    risk in the plan, and the label is the mitigation.
 * 2. **A tier verdict always carries its reasons.** A refusal renders which comparability keys
 *    differ and which runs carry each variant. "Not comparable" with no reason is unusable — the
 *    operator cannot tell a suite mismatch from a tool-guide edit, and those want opposite actions.
 *
 * The component writes nothing except through the group endpoints it is given: it creates groups
 * and starts analyses, and never touches a run, an answer or a rubric.
 */
@Component({
  selector: 'app-benchmark-multi-run',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './multi-run.component.html',
  styleUrls: ['./multi-run.component.scss']
})
export class MultiRunComponent implements OnInit, OnChanges {
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);

  /**
   * The suite whose groups and runs are shown. Null means "no suite selected" — the panel then
   * shows nothing rather than every group on the server, because a group list spanning suites
   * invites building a set across two of them, which is the one thing tiering must refuse.
   */
  @Input() suiteId: number | null = null;

  // --- Group list ---
  groups: BenchmarkRunGroupDto[] = [];
  loadingGroups = false;
  groupsError: string | null = null;

  // --- Group detail ---
  selectedGroup: BenchmarkRunGroupDto | null = null;
  loadingGroup = false;
  groupError: string | null = null;

  /**
   * The selected group's comparability, re-derived through the read-only preview endpoint over its
   * own member run ids. `BenchmarkRunGroupDto` carries the tier but not the reasons behind it, and
   * the reasons are what the detail view has to show — including which of speed and cost the tier
   * degrades.
   */
  detailComparability: BenchmarkComparabilityResultDto | null = null;

  // --- Analysis ---
  analysis: BenchmarkGroupAnalysisDto | null = null;
  loadingAnalysis = false;
  analysing = false;
  analysisError: string | null = null;
  /** The baseline half of a paired comparison. Null computes the group on its own. */
  compareWithGroupId: number | null = null;

  // --- Group builder ---
  builderName = '';
  builderNotes = '';
  availableRuns: BenchmarkRunSummaryDto[] = [];
  loadingRuns = false;
  runsError: string | null = null;
  selectedRunIds: number[] = [];
  preview: BenchmarkRunGroupTierPreviewDto | null = null;
  previewing = false;
  previewError: string | null = null;
  /** Only ever offered when the preview says Tier C; a Tier C group is refused without it. */
  crossCondition = false;
  creating = false;
  createError: string | null = null;

  /**
   * Guards against an out-of-order preview response. Checkboxes can be clicked faster than the
   * round trip returns, and a stale tier verdict rendered over a newer selection is worse than no
   * verdict at all — it names keys the operator is no longer looking at.
   */
  private previewToken = 0;

  readonly varianceCaveatFallback = VARIANCE_DECOMPOSITION_CAVEAT;
  readonly exploratoryNoteFallback = EXPLORATORY_ITEM_TEST_NOTE;

  ngOnInit(): void {
    // Every icon-only control in this panel carries an interestfor tooltip, and three of the
    // primitives behind those are not yet baseline. Feature-detected inside; a supporting browser
    // downloads none of them.
    ensureOverlayPolyfills();
    if (this.suiteId != null) {
      this.reload();
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['suiteId']) {
      this.reset();
      if (this.suiteId != null) {
        this.reload();
      }
    }
  }

  private reset(): void {
    this.groups = [];
    this.groupsError = null;
    this.selectedGroup = null;
    this.groupError = null;
    this.detailComparability = null;
    this.analysis = null;
    this.analysisError = null;
    this.compareWithGroupId = null;
    this.availableRuns = [];
    this.runsError = null;
    this.selectedRunIds = [];
    this.preview = null;
    this.previewError = null;
    this.crossCondition = false;
    this.createError = null;
    this.builderName = '';
    this.builderNotes = '';
  }

  reload(): void {
    this.loadGroups();
    this.loadRuns();
  }

  loadGroups(): void {
    this.loadingGroups = true;
    this.groupsError = null;
    this.benchmarkService.getRunGroups().subscribe({
      next: (groups) => {
        // Filtered client-side: the endpoint returns every group, and a group is bound to one
        // suite by construction, so there is nothing to ask the server for.
        this.groups = (groups ?? []).filter(g => this.suiteId == null || g.benchmarkSuiteId === this.suiteId);
        this.loadingGroups = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingGroups = false;
        this.groupsError = err?.error?.message || err?.error || 'Failed to load the run groups.';
        this.cdr.detectChanges();
      }
    });
  }

  loadRuns(): void {
    if (this.suiteId == null) return;
    this.loadingRuns = true;
    this.runsError = null;
    this.benchmarkService.getRuns(this.suiteId, 50).subscribe({
      next: (runs) => {
        this.availableRuns = runs ?? [];
        this.loadingRuns = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingRuns = false;
        this.runsError = err?.error?.message || err?.error || 'Failed to load the suite runs.';
        this.cdr.detectChanges();
      }
    });
  }

  // ---------------------------------------------------------------------------------------
  // Group list
  // ---------------------------------------------------------------------------------------

  selectGroup(group: BenchmarkRunGroupDto): void {
    this.selectedGroup = group;
    this.groupError = null;
    this.analysis = null;
    this.analysisError = null;
    this.detailComparability = null;
    this.compareWithGroupId = null;
    this.loadGroup(group.id);
    this.loadAnalysis(group.id);
    this.cdr.detectChanges();
  }

  closeGroup(): void {
    this.selectedGroup = null;
    this.analysis = null;
    this.detailComparability = null;
    this.cdr.detectChanges();
  }

  private loadGroup(id: number): void {
    this.loadingGroup = true;
    this.benchmarkService.getRunGroup(id).subscribe({
      next: (group) => {
        this.selectedGroup = group;
        this.loadingGroup = false;
        this.loadDetailComparability(group);
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingGroup = false;
        this.groupError = err?.error?.message || err?.error || 'Failed to load the group.';
        this.cdr.detectChanges();
      }
    });
  }

  /** Read-only: the preview endpoint creates nothing, which is what makes it safe to call here. */
  private loadDetailComparability(group: BenchmarkRunGroupDto): void {
    const runIds = (group.members ?? []).map(m => m.runId);
    if (runIds.length === 0) return;

    this.benchmarkService.previewRunGroupTier({
      name: group.name,
      runIds,
      crossCondition: group.crossCondition
    }).subscribe({
      next: (result) => {
        this.detailComparability = result?.comparability ?? null;
        this.cdr.detectChanges();
      },
      // A failure here costs the reasons behind the tier, not the tier itself, so it is silent
      // rather than an error banner over a detail view that is otherwise perfectly usable.
      error: () => { }
    });
  }

  private loadAnalysis(id: number): void {
    this.loadingAnalysis = true;
    this.benchmarkService.getRunGroupAnalysis(id).subscribe({
      next: (analysis) => {
        this.analysis = analysis;
        this.compareWithGroupId = analysis?.comparedWithGroupId ?? null;
        this.loadingAnalysis = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingAnalysis = false;
        this.analysisError = err?.error?.message || err?.error || 'Failed to load the stored analysis.';
        this.cdr.detectChanges();
      }
    });
  }

  analyse(): void {
    const group = this.selectedGroup;
    if (!group || this.analysing) return;

    this.analysing = true;
    this.analysisError = null;
    this.benchmarkService.analyseRunGroup(group.id, {
      compareWithGroupId: this.compareWithGroupId
    }).subscribe({
      next: (analysis) => {
        this.analysis = analysis;
        this.analysing = false;
        // The group row's latestAnalysisId and stale badge move with the analysis, so the list
        // behind the detail view has to be re-read rather than patched from here.
        this.loadGroups();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.analysing = false;
        this.analysisError = err?.error?.message || err?.error || 'The group analysis failed.';
        this.cdr.detectChanges();
      }
    });
  }

  deleteGroup(group: BenchmarkRunGroupDto): void {
    this.benchmarkService.deleteRunGroup(group.id).subscribe({
      next: () => {
        if (this.selectedGroup?.id === group.id) this.closeGroup();
        this.loadGroups();
      },
      error: (err) => {
        this.groupsError = err?.error?.message || err?.error || 'Failed to delete the group.';
        this.cdr.detectChanges();
      }
    });
  }

  // ---------------------------------------------------------------------------------------
  // Report download
  //
  // window.open against the URL the service builds, exactly as the single-run downloadReport
  // does. Not an XHR and not an <a download>: the endpoint answers with a Content-Disposition
  // filename the server chose, and fetching it into a blob would throw that name away.
  // ---------------------------------------------------------------------------------------

  /**
   * The report is built from the **persisted** analysis, so there is nothing to download until one
   * exists. The control stays present and focusable while unavailable — a keyboard user has to be
   * able to land on it and read why — which is why this gates the handler rather than setting
   * `disabled`.
   */
  canDownloadReport(group: BenchmarkRunGroupDto | null | undefined): boolean {
    return !!group && group.latestAnalysisId != null;
  }

  downloadReportTooltip(group: BenchmarkRunGroupDto | null | undefined): string {
    if (this.canDownloadReport(group)) {
      return group?.analysisStale
        ? 'Download the Markdown report. The analysis is stale: the membership changed after it was computed.'
        : 'Download the Markdown report';
    }
    return 'No analysis yet — run the analysis before downloading a report';
  }

  downloadGroupReport(group: BenchmarkRunGroupDto | null | undefined): void {
    if (!group || !this.canDownloadReport(group)) return;
    window.open(this.benchmarkService.getGroupReportUrl(group.id), '_blank');
  }

  // ---------------------------------------------------------------------------------------
  // Group builder
  // ---------------------------------------------------------------------------------------

  isRunSelected(runId: number): boolean {
    return this.selectedRunIds.includes(runId);
  }

  toggleRun(runId: number): void {
    this.selectedRunIds = this.isRunSelected(runId)
      ? this.selectedRunIds.filter(id => id !== runId)
      : [...this.selectedRunIds, runId];

    // The tier can change on every click, and an operator selecting a fourth run wants to know
    // before pressing Create whether the fourth one broke the set.
    this.previewTier();
    this.cdr.detectChanges();
  }

  clearSelection(): void {
    this.selectedRunIds = [];
    this.preview = null;
    this.previewError = null;
    this.crossCondition = false;
    this.cdr.detectChanges();
  }

  previewTier(): void {
    if (this.selectedRunIds.length < 2) {
      this.preview = null;
      this.previewError = null;
      this.crossCondition = false;
      return;
    }

    const token = ++this.previewToken;
    this.previewing = true;
    this.previewError = null;
    this.benchmarkService.previewRunGroupTier({
      name: this.builderName || 'Preview',
      runIds: [...this.selectedRunIds],
      crossCondition: this.crossCondition
    }).subscribe({
      next: (result) => {
        if (token !== this.previewToken) return;
        this.preview = result;
        this.previewing = false;
        // The cross-condition acknowledgement is offered only where it means something. Leaving
        // it checked after the set stops being Tier C would send a flag the server would then
        // have to ignore, and a checked box the operator cannot see is a lie about the request.
        if (!this.previewIsCrossCondition) this.crossCondition = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        if (token !== this.previewToken) return;
        this.previewing = false;
        this.preview = null;
        this.previewError = err?.error?.message || err?.error || 'Failed to compute the comparability tier.';
        this.cdr.detectChanges();
      }
    });
  }

  onCrossConditionChange(): void {
    this.previewTier();
  }

  get previewComparability(): BenchmarkComparabilityResultDto | null {
    return this.preview?.comparability ?? null;
  }

  get previewIsCrossCondition(): boolean {
    return this.previewComparability?.tier === 'CrossCondition';
  }

  get previewDifferences() {
    return this.previewComparability?.differences ?? [];
  }

  get canCreateGroup(): boolean {
    if (this.creating) return false;
    if (this.selectedRunIds.length < 2) return false;
    if (!this.builderName.trim()) return false;
    if (!this.preview) return false;
    return this.preview.accepted === true;
  }

  createGroup(): void {
    if (!this.canCreateGroup) return;

    this.creating = true;
    this.createError = null;
    this.benchmarkService.createRunGroup({
      name: this.builderName.trim(),
      runIds: [...this.selectedRunIds],
      notes: this.builderNotes.trim() || null,
      crossCondition: this.crossCondition
    }).subscribe({
      next: (result) => {
        this.creating = false;
        if (!result.accepted) {
          // A refusal is data, not an exception: it arrives with the differing keys, and those
          // are the whole value of the answer.
          this.preview = result;
          this.createError = result.error ?? 'The group was refused.';
          this.cdr.detectChanges();
          return;
        }
        this.clearSelection();
        this.builderName = '';
        this.builderNotes = '';
        this.loadGroups();
        if (result.group) this.selectGroup(result.group);
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.creating = false;
        this.createError = err?.error?.message || err?.error || 'Failed to create the group.';
        this.cdr.detectChanges();
      }
    });
  }

  // ---------------------------------------------------------------------------------------
  // Analysis readers
  // ---------------------------------------------------------------------------------------

  get result(): MultiRunStatisticsResult | null {
    return (this.analysis?.result as MultiRunStatisticsResult) ?? null;
  }

  get comparison(): MultiRunComparison | null {
    return (this.analysis?.comparison as MultiRunComparison) ?? null;
  }

  get indexStats(): MultiRunIndexStatistics | null {
    return this.result?.index ?? null;
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

  /** Groups the selected one may be paired against: same suite, and not itself. */
  get comparisonCandidates(): BenchmarkRunGroupDto[] {
    const selected = this.selectedGroup;
    if (!selected) return [];
    return this.groups.filter(g => g.id !== selected.id);
  }

  // ---------------------------------------------------------------------------------------
  // Formatting
  // ---------------------------------------------------------------------------------------

  tierClass(tier: BenchmarkComparabilityTier | string | null | undefined): string {
    switch (tier) {
      case 'Replicate': return 'tier-replicate';
      case 'QualityComparable': return 'tier-quality';
      case 'CrossCondition': return 'tier-cross';
      default: return 'tier-none';
    }
  }

  tierName(tier: BenchmarkComparabilityTier | string | null | undefined): string {
    switch (tier) {
      case 'Replicate': return 'Tier A — Replicate';
      case 'QualityComparable': return 'Tier B — Quality-comparable';
      case 'CrossCondition': return 'Tier C — Cross-condition';
      default: return 'Not comparable';
    }
  }

  /**
   * Money at the precision the figure warrants: two decimals at or above a dollar, where a third
   * would be noise, and four below it, where a sub-cent run would otherwise round to $0.00.
   * Matches `formatRunEstimatedCost` on the run cards.
   */
  formatCost(value: number | null | undefined): string {
    if (value == null) return '—';
    const pipe = new DecimalPipe('en-US');
    return `$${pipe.transform(value, value >= 1 ? '1.2-2' : '1.2-4')}`;
  }

  /** The first eight hex characters of an instrument hash, as the run list shows them. */
  shortFingerprint(sha: string | null | undefined): string {
    return sha ? sha.substring(0, 8) : '—';
  }

  formatNumber(value: number | null | undefined, digits = 1): string {
    if (value == null) return '—';
    const pipe = new DecimalPipe('en-US');
    return pipe.transform(value, `1.${digits}-${digits}`) ?? '—';
  }

  /** A coefficient of variation arrives as a fraction and reads as a percentage. */
  formatPercentFromFraction(value: number | null | undefined): string {
    if (value == null) return '—';
    return `${this.formatNumber(value * 100, 1)} %`;
  }

  formatRate(value: number | null | undefined): string {
    if (value == null) return '—';
    return `${this.formatNumber(value * 100, 0)} %`;
  }

  formatMs(value: number | null | undefined): string {
    if (value == null) return '—';
    if (value < 1000) return `${Math.round(value)} ms`;
    return `${this.formatNumber(value / 1000, 1)} s`;
  }

  formatDate(value: string | null | undefined): string {
    if (!value) return '—';
    const parsed = new Date(value);
    return isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString();
  }

  /**
   * p-values below the smallest figure the column can show are rendered as a bound rather than as
   * 0.000, which would claim a certainty no test produces.
   */
  formatPValue(value: number | null | undefined): string {
    if (value == null) return '—';
    if (value < 0.001) return '< 0.001';
    return this.formatNumber(value, 3);
  }

  formatSigned(value: number | null | undefined, digits = 1): string {
    if (value == null) return '—';
    const formatted = this.formatNumber(value, digits);
    return value > 0 ? `+${formatted}` : formatted;
  }

  /** Tooltip anchor ids. One per row, so no two anchors in the panel share a name. */
  downloadTipId(groupId: number): string {
    return `mr-tip-download-${groupId}`;
  }

  deleteTipId(groupId: number): string {
    return `mr-tip-delete-${groupId}`;
  }

  runIdsLabel(runIds: number[] | null | undefined): string {
    if (!runIds || runIds.length === 0) return '—';
    return runIds.map(id => `#${id}`).join(', ');
  }
}
