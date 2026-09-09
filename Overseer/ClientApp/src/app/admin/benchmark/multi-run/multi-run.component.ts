import {
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
  inject
} from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { exactFilter, TableState } from '../../../shared/data-table/table-state';
import { SortHeaderComponent } from '../../../shared/data-table/sort-header.component';
import { TablePagerComponent } from '../../../shared/data-table/table-pager.component';
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
  meanConfidenceLower?: number | null;
  meanConfidenceUpper?: number | null;
  /** A bound hit the [0, 100] score range and was clamped; the half-width is the honest spread. */
  meanConfidenceTruncated?: boolean;
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
  /** A combined bound hit the [0, 100] score range and was clamped. */
  combinedIntervalTruncated?: boolean;
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
  /**
   * Pooled over the answers carrying a time to first token, which is a subset of
   * `pooledAnswerCount`: model time is always recorded and TTFT is not, so the two counts differ
   * and `ttftAnswerCount` is the denominator these three percentiles belong to.
   */
  ttftP50Ms?: number | null;
  ttftP90Ms?: number | null;
  ttftMaxMs?: number | null;
  ttftAnswerCount?: number;
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
  costPerIndexPoint?: number | null;
  degraded?: boolean;
  degradedReason?: string | null;
}

/**
 * One scoring dimension across the runs. Mirrors `BenchmarkGroupDimensionStatistics`.
 *
 * These means are unweighted while the index is difficulty-weighted, so the two are not expected
 * to agree — the template says so wherever it renders them together.
 */
export interface MultiRunDimensionStatistics {
  dimension: string;
  perRunMeans?: number[];
  mean?: number | null;
  standardDeviation?: number | null;
  /** Null below three runs, on the same rule the index's reproducibility component uses. */
  confidenceHalfWidth?: number | null;
  min?: number | null;
  max?: number | null;
  /** Question id → cross-run mean on this dimension. */
  itemMeans?: { [questionId: string]: number };
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
  /** 0 Disabled, 1 OnRequest, 2 Enabled — the numeric enum as the server serialises it. */
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
 * One line of a run's instrument fingerprint stack in the run picker: a short visible label, a
 * colour class, the eight-character hash prefix, and the tooltip carrying the corpus name and the
 * full hash.
 *
 * The label is what makes the stack readable in greyscale and to a colour-blind reader, so the
 * colour class only reinforces it. Declared here rather than imported from the benchmark component,
 * which imports this one; the labels and classes match its own deliberately, so the two surfaces
 * read identically.
 */
export interface MultiRunFingerprintEntry {
  label: 'PROMPT' | 'GUIDES' | 'KB' | 'WIKI' | 'SRC';
  cssClass: 'fp-prompt' | 'fp-guides' | 'fp-kb' | 'fp-wiki' | 'fp-source';
  short: string;
  title: string;
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
  imports: [CommonModule, FormsModule, SortHeaderComponent, TablePagerComponent],
  templateUrl: './multi-run.component.html',
  styleUrls: ['./multi-run.component.scss']
})
export class MultiRunComponent implements OnInit, OnChanges {
  private benchmarkService = inject(AdminBenchmarkService);

  /** Protected rather than private: the filter-row template calls this directly after `setFilter`. */
  protected cdr = inject(ChangeDetectorRef);

  /**
   * The suite whose groups and runs are shown. Null means "no suite selected" — the panel then
   * shows nothing rather than every group on the server, because a group list spanning suites
   * invites building a set across two of them, which is the one thing tiering must refuse.
   */
  @Input() suiteId: number | null = null;

  /**
   * A completed series the host should open its progress dialog for.
   *
   * The group table's Series badge is the permanent route to a finished series: the Run Benchmark
   * banner that used to be the only one disappears when the series ends, and the series dialog is
   * where both diagnostics captures come from.
   */
  @Output() openSeries = new EventEmitter<number>();

  @ViewChild('groupDetailDialog') groupDetailDialog?: ElementRef<HTMLDialogElement>;

  // --- Group list ---
  groups: BenchmarkRunGroupDto[] = [];
  loadingGroups = false;
  groupsError: string | null = null;

  /**
   * Sort, filter and page state for the analysis-group table. Created desc by default, so the
   * newest set an operator just built is the first row without them having to look for it. Tier
   * sorts on the comparability enum's own numeric order rather than the label — see
   * {@link tierOrder}, which puts Tier A first on the first click — and the analysis-state filter
   * is derived per row by {@link analysisState}.
   */
  readonly groupTable = new TableState<BenchmarkRunGroupDto>('createdAtUtc', 'desc').registerAccessors(
    {
      name: g => g.name,
      tier: g => this.tierOrder(g.tier),
      runCount: g => g.runCount,
      createdAtUtc: g => new Date(g.createdAtUtc),
      analysisDate: g => g.latestAnalysisAtUtc ? new Date(g.latestAnalysisAtUtc) : null
    },
    {
      name: g => g.name,
      tier: exactFilter(g => g.tier),
      analysisState: exactFilter(g => this.analysisState(g))
    }
  );

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

  /**
   * Sort, filter and page state for the run picker. ID desc by default, so the most recently run
   * candidate leads. The selection column's sort key reads component state rather than the row —
   * `isRunSelected` — which puts every ticked run first regardless of what else changes about it;
   * the same accessor doubles as the `Show selected only` filter definition, since both are the
   * same underlying fact about a run.
   */
  readonly runPickerTable = new TableState<BenchmarkRunSummaryDto>('id', 'desc').registerAccessors(
    {
      selected: r => this.isRunSelected(r.id) ? 0 : 1,
      id: r => r.id,
      testedModel: r => r.testedModelDisplayNameUsed,
      date: r => new Date(r.startedAtUtc),
      qualityIndex: r => r.qualityIndex ?? r.finalScore,
      index: r => r.qualityIndex ?? r.finalScore,
      speedIndex: r => r.speedIndex
    },
    {
      testedModel: r => r.testedModelDisplayNameUsed,
      qualityIndex: exactFilter(r => (r.qualityIndex ?? r.finalScore) != null ? 'present' : 'absent'),
      index: exactFilter(r => (r.qualityIndex ?? r.finalScore) != null ? 'present' : 'absent'),
      speedIndex: exactFilter(r => r.speedIndex != null ? 'present' : 'absent'),
      selected: exactFilter(r => this.isRunSelected(r.id) ? 'yes' : 'no')
    }
  );

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
    this.benchmarkService.getRuns(this.suiteId, 200).subscribe({
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

  /**
   * Re-renders after a sort, page or filter change on either table. Neither `SortHeaderComponent`
   * nor `TablePagerComponent` calls change detection itself, and this component drives its own
   * throughout, so every `(changed)` output on both tables lands here.
   */
  onTableChanged(): void {
    this.cdr.detectChanges();
  }

  // ---------------------------------------------------------------------------------------
  // Group list
  // ---------------------------------------------------------------------------------------

  /**
   * Loads a group's detail without showing it. Kept separate from <see cref="openGroup"/> because
   * the builder's success path selects the group it just created, and opening a modal there would
   * steal focus from an operator who is still working in the builder.
   */
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

  /** Loads a group and shows its analysis in the modal — what the row's eye control does. */
  openGroup(group: BenchmarkRunGroupDto): void {
    this.selectGroup(group);
    // detectChanges first: showModal() on a dialog whose @if content does not exist yet opens an
    // empty box, and the content is gated on selectedGroup.
    this.cdr.detectChanges();
    this.groupDetailDialog?.nativeElement.showModal();
  }

  /**
   * Opens a group's analysis by id, for a caller that has an id and not a row — the series
   * progress dialog's View Report control. Searches the full group list rather than the paged
   * table view, so a group sitting on a page the operator is not looking at still opens; falls
   * back to fetching the group when the list does not hold it at all, which is the ordinary case
   * when the panel is filtered to another suite.
   */
  openGroupById(groupId: number): void {
    const listed = this.groups.find(g => g.id === groupId);
    if (listed) {
      this.openGroup(listed);
      return;
    }
    this.benchmarkService.getRunGroup(groupId).subscribe({
      next: (group) => { if (group) this.openGroup(group); },
      error: (err) => {
        this.groupsError = err?.error?.message || err?.error || 'Failed to load the run groups.';
        this.cdr.detectChanges();
      }
    });
  }

  closeGroup(): void {
    const dialog = this.groupDetailDialog?.nativeElement;
    if (dialog?.open) {
      dialog.close();
    }

    this.selectedGroup = null;
    this.analysis = null;
    this.detailComparability = null;
    this.cdr.detectChanges();
  }

  /** Hands a group's originating series to the host, which owns the series progress dialog. */
  viewSeries(group: BenchmarkRunGroupDto): void {
    if (group.createdFromSeriesId == null) return;
    this.openSeries.emit(group.createdFromSeriesId);
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

  get selectedRunCount(): number {
    return this.selectedRunIds.length;
  }

  /**
   * Selected ids absent from the run picker's current page. Selection is by id, so it already
   * survives paging and filtering underneath — but an operator who filtered the list down to one
   * ticked row has no way to see the others are still selected without this count.
   */
  get selectedOffPageCount(): number {
    const onPage = new Set(this.runPickerTable.view(this.availableRuns).map(r => r.id));
    return this.selectedRunIds.filter(id => !onPage.has(id)).length;
  }

  /** Empty when nothing is selected — the line above the picker is never rendered in that case. */
  get selectionSummary(): string {
    const total = this.selectedRunCount;
    if (total === 0) return '';
    const offPage = this.selectedOffPageCount;
    return offPage > 0
      ? `${total} selected — ${offPage} not on this page`
      : `${total} selected`;
  }

  get showSelectedOnly(): boolean {
    return this.runPickerTable.filters['selected'] === 'yes';
  }

  /**
   * Filters the picker down to the ticked runs and back. This is the one column filter driven by
   * a toggle button rather than a text input or a select, because its two states are "selected"
   * and "everything" rather than an open set of values — clearing it clears only this column, not
   * the operator's text or index filters alongside it.
   */
  toggleShowSelectedOnly(): void {
    this.runPickerTable.setFilter('selected', this.showSelectedOnly ? '' : 'yes');
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
   * The three weakest items on one dimension, labelled with the Q numbers the rest of the panel
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
   * The comparability enum's own numeric order, mirroring `BenchmarkComparabilityTier` on the
   * server: `NotComparable` 0 through `Replicate` 3. The label happens to sort correctly today
   * only because the letters A/B/C were chosen to match; a renamed tier would break an
   * alphabetical sort silently, so the table sorts on this instead.
   *
   * Replicate being the highest value is what puts Tier A at the top on the first click, since a
   * newly sorted column starts descending.
   */
  tierOrder(tier: BenchmarkComparabilityTier | string | null | undefined): number {
    switch (tier) {
      case 'Replicate': return 3;
      case 'QualityComparable': return 2;
      case 'CrossCondition': return 1;
      default: return 0;
    }
  }

  /**
   * One of three mutually exclusive, exhaustive buckets for the group table's analysis-state
   * filter. A group with no analysis at all is `notAnalysed`; a group whose latest analysis no
   * longer describes its current membership is `stale` rather than `analysed` — filtering to
   * Analysed shows only a group whose stored result still matches what it contains today.
   */
  analysisState(group: BenchmarkRunGroupDto): 'analysed' | 'notAnalysed' | 'stale' {
    if (group.latestAnalysisId == null) return 'notAnalysed';
    return group.analysisStale ? 'stale' : 'analysed';
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

  /**
   * The five fingerprints of a run's instrument, in the fixed order the picker stacks them.
   *
   * The shape is always five rows: a hash that was never recorded shows as a dash under its own
   * label rather than dropping out, so two runs' stacks line up row for row.
   */
  fingerprintEntries(run: BenchmarkRunSummaryDto): MultiRunFingerprintEntry[] {
    return [
      {
        label: 'PROMPT',
        cssClass: 'fp-prompt',
        short: this.shortFingerprint(run.candidateSystemPromptSha256),
        title: run.candidateSystemPromptSha256
          ? 'Candidate system prompt SHA-256: ' + run.candidateSystemPromptSha256
          : 'Candidate system prompt SHA-256: not recorded'
      },
      {
        label: 'GUIDES',
        cssClass: 'fp-guides',
        short: this.shortFingerprint(run.toolGuidesSha256),
        title: run.toolGuidesSha256
          ? 'Tool guides SHA-256: ' + run.toolGuidesSha256
          : 'Tool guides SHA-256: not recorded'
      },
      {
        label: 'KB',
        cssClass: 'fp-kb',
        short: this.shortFingerprint(run.knowledgeBaseHeadSha),
        title: run.knowledgeBaseHeadSha
          ? 'Knowledge base Git HEAD SHA: ' + run.knowledgeBaseHeadSha
          : 'Knowledge base Git HEAD SHA: not recorded'
      },
      {
        label: 'WIKI',
        cssClass: 'fp-wiki',
        short: this.shortFingerprint(run.wikiHeadSha),
        title: run.wikiHeadSha
          ? 'GnollHack wiki Git HEAD SHA: ' + run.wikiHeadSha
          : 'GnollHack wiki Git HEAD SHA: not recorded'
      },
      {
        label: 'SRC',
        cssClass: 'fp-source',
        short: this.shortFingerprint(run.sourceCodeHeadSha),
        title: run.sourceCodeHeadSha
          ? 'GnollHack source Git HEAD SHA: ' + run.sourceCodeHeadSha
          : 'GnollHack source Git HEAD SHA: not recorded'
      }
    ];
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

  getScoreBadgeClass(score: number | null | undefined): string {
    if (score == null) return 'badge-score-na';
    if (score >= 80) return 'badge-score-high';
    if (score >= 50) return 'badge-score-mid';
    return 'badge-score-low';
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
    return `${this.formatNumber(value, digits)} %`;
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

  /** Tooltip anchor ids. One per row, so no two anchors in the panel share a name. */
  downloadTipId(groupId: number): string {
    return `mr-tip-download-${groupId}`;
  }

  viewTipId(groupId: number): string {
    return `mr-tip-view-${groupId}`;
  }

  seriesTipId(groupId: number): string {
    return `mr-tip-series-${groupId}`;
  }

  deleteTipId(groupId: number): string {
    return `mr-tip-delete-${groupId}`;
  }

  runIdsLabel(runIds: number[] | null | undefined): string {
    if (!runIds || runIds.length === 0) return '—';
    return runIds.map(id => `#${id}`).join(', ');
  }
}
