import { Component, EventEmitter, HostListener, Input, OnChanges, OnInit, OnDestroy, Output, SimpleChanges, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import {
  AdminBenchmarkService,
  BenchmarkSuiteItemAnalysisDto,
  BenchmarkItemStatisticsDto,
  BenchmarkRubricGapReportDto,
  BenchmarkCitationReportDto,
  BenchmarkCoverageReportDto,
  RubricCheckJobDto,
  RubricCheckJobItemDto,
  RubricCheckFindingDto
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import {
  RubricGapAuthorService,
  RubricGapAuthorJobDto,
  RubricGapAuthorDraftDto,
  RubricAdditionAcceptanceDto
} from './rubric-gap-author.service';

export type SuiteHealthTab = 'items' | 'gaps' | 'citations' | 'coverage' | 'board-facts';

export type ItemSortColumn =
  | 'orderIndex'
  | 'meanQuality'
  | 'spread'
  | 'assessedDifficulty'
  | 'empiricalDifficulty'
  | 'difficultyDelta'
  | 'discrimination'
  | 'meanToolCalls'
  | 'flags'
  | 'sample';

export type SortDirection = 'asc' | 'desc';

/**
 * Suite health: what the stored runs say about the *suite* rather than about the models.
 *
 * Read-only by construction, with one deliberate exception named below. The panel's ordinary
 * outward action is {@link SuiteHealthComponent.editQuestion}, which asks the host to open a
 * question for editing — there is no write endpoint behind any of these reports, and in particular
 * no action that copies an item's empirical difficulty into its assessed difficulty. That number
 * weights the Intelligence Index, so deriving it from the scores it weights would be circular, and
 * it would let a model that did badly on an item retroactively reduce that item's weight.
 *
 * The exception is {@link SuiteHealthComponent.acceptDraft}, which appends one rubric addition to
 * one question. It is a write the *operator* performs, not one the panel derives: the text sent is
 * whatever stands in that draft's textarea when Accept is pressed, and there is no control that
 * accepts more than one draft. That restriction is the feature, not an omission — curated knowledge
 * has to be human-authored, so an accept-all button would defeat the boundary the drafting job
 * exists to respect.
 *
 * Statistical honesty is a requirement of this UI, not a nicety: every row carries its sample size
 * and both confound counts, discrimination reads "insufficient data" below four runs, and the
 * banner says when the suite's runs mix assessors or scoring method versions.
 */
@Component({
  selector: 'app-benchmark-suite-health',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './suite-health.component.html',
  styleUrls: ['./suite-health.component.scss']
})
export class SuiteHealthComponent implements OnInit, OnChanges, OnDestroy {
  private benchmarkService = inject(AdminBenchmarkService);
  private gapAuthorService = inject(RubricGapAuthorService);
  private cdr = inject(ChangeDetectorRef);

  @Input() suiteId: number | null = null;
  @Input() suiteName = '';

  /** Benchmark-capable configurations, for the coverage analysis model selector. */
  @Input() benchmarkCapableConfigs: SystemAiConfigDto[] = [];

  /** The host opens the question editor; this component never writes anything itself. */
  @Output() editQuestionRequested = new EventEmitter<number>();
  @Input() gameSnapshotId?: number | null;
  @Input() gameSnapshotName?: string | null;
  @Input() hasGeneratedQuestions?: boolean;
  @Input() initialTab?: SuiteHealthTab;
  @Output() verifyQuestionRequested = new EventEmitter<number>();

  rubricCheckJob: RubricCheckJobDto | null = null;
  runningRubricCheck = false;
  rubricCheckError: string | null = null;
  rubricCheckerConfigId: number | null = null;
  isRubricCheckerDropdownOpen = false;
  cancellingRubricCheck = false;
  private rubricCheckPollInterval: any = null;

  // --- Rubric Gap Author ---

  rubricGapAuthorJob: RubricGapAuthorJobDto | null = null;
  runningRubricGapAuthor = false;
  rubricGapAuthorError: string | null = null;
  rubricGapAuthorConfigId: number | null = null;
  isRubricGapAuthorDropdownOpen = false;
  cancellingRubricGapAuthor = false;
  rubricGapAuthorInstructions = '';
  private rubricGapAuthorPollInterval: any = null;

  /**
   * The live textarea contents per cluster key. This is the authoritative text an acceptance
   * submits — the draft is only what seeded it — so polling must never overwrite a key the
   * operator has already been given, or an edit would vanish mid-typing.
   */
  draftEdits: Record<string, string> = {};

  acceptingClusterKey: string | null = null;
  acceptedDrafts: Record<string, RubricAdditionAcceptanceDto> = {};
  draftAcceptErrors: Record<string, string> = {};

  activeTab: SuiteHealthTab = 'items';

  sortColumn: ItemSortColumn = 'orderIndex';
  sortDirection: SortDirection = 'asc';

  itemAnalysis: BenchmarkSuiteItemAnalysisDto | null = null;
  loadingItems = false;
  itemsError: string | null = null;

  rubricGaps: BenchmarkRubricGapReportDto | null = null;
  loadingGaps = false;
  gapsError: string | null = null;

  citations: BenchmarkCitationReportDto | null = null;
  loadingCitations = false;
  citationsError: string | null = null;

  coverage: BenchmarkCoverageReportDto | null = null;
  analyzingCoverage = false;
  coverageError: string | null = null;
  coverageModelConfigId: number | null = null;

  /** Open state of the Coverage analysis-model dropdown. */
  isCoverageModelDropdownOpen = false;

  private readonly tabOrder: SuiteHealthTab[] = ['items', 'gaps', 'citations', 'coverage', 'board-facts'];

  ngOnInit(): void {
    ensureOverlayPolyfills();
    if (this.initialTab) {
      this.activeTab = this.initialTab;
    }
  }

  ngOnDestroy(): void {
    this.stopRubricCheckPolling();
    this.stopRubricGapAuthorPolling();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['suiteId'] && this.suiteId != null) {
      this.reset();
      this.loadItemAnalysis();
      this.loadRubricGaps();
    }
    if (changes['benchmarkCapableConfigs'] && this.coverageModelConfigId == null) {
      this.coverageModelConfigId = this.benchmarkCapableConfigs[0]?.id ?? null;
    }
    if (changes['benchmarkCapableConfigs'] && this.rubricCheckerConfigId == null) {
      this.rubricCheckerConfigId = this.benchmarkCapableConfigs[0]?.id ?? null;
    }
    if (changes['benchmarkCapableConfigs'] && this.rubricGapAuthorConfigId == null) {
      this.rubricGapAuthorConfigId = this.benchmarkCapableConfigs[0]?.id ?? null;
    }
    if (changes['initialTab'] && this.initialTab) {
      this.activeTab = this.initialTab;
    }
  }

  private reset(): void {
    this.itemAnalysis = null;
    this.rubricGaps = null;
    this.citations = null;
    this.coverage = null;
    this.itemsError = null;
    this.gapsError = null;
    this.citationsError = null;
    this.coverageError = null;
    this.isCoverageModelDropdownOpen = false;
    this.rubricCheckJob = null;
    this.runningRubricCheck = false;
    this.rubricCheckError = null;
    this.isRubricCheckerDropdownOpen = false;
    this.stopRubricCheckPolling();
    this.rubricGapAuthorJob = null;
    this.runningRubricGapAuthor = false;
    this.rubricGapAuthorError = null;
    this.isRubricGapAuthorDropdownOpen = false;
    this.rubricGapAuthorInstructions = '';
    this.draftEdits = {};
    this.acceptedDrafts = {};
    this.draftAcceptErrors = {};
    this.acceptingClusterKey = null;
    this.stopRubricGapAuthorPolling();
    this.sortColumn = 'orderIndex';
    this.sortDirection = 'asc';
  }

  /**
   * Closes the analysis-model dropdown on any click outside it. Scoped by the
   * `.coverage-model-selector` marker class exactly as the benchmark tab scopes its own
   * selectors, so a click inside the dropdown does not close it before the option is taken.
   */
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (this.isCoverageModelDropdownOpen && !target.closest('.coverage-model-selector')) {
      this.isCoverageModelDropdownOpen = false;
      this.cdr.detectChanges();
    }
    if (this.isRubricCheckerDropdownOpen && !target.closest('.rubric-checker-selector')) {
      this.isRubricCheckerDropdownOpen = false;
      this.cdr.detectChanges();
    }
    if (this.isRubricGapAuthorDropdownOpen && !target.closest('.rubric-gap-author-selector')) {
      this.isRubricGapAuthorDropdownOpen = false;
      this.cdr.detectChanges();
    }
  }

  toggleCoverageModelDropdown(event: Event): void {
    // Without this the same click reaches onDocumentClick above and closes the dropdown in
    // the same tick, so the trigger appears not to work at all.
    event.stopPropagation();
    this.isCoverageModelDropdownOpen = !this.isCoverageModelDropdownOpen;
    this.cdr.detectChanges();
  }

  selectCoverageModel(config: SystemAiConfigDto): void {
    this.coverageModelConfigId = config.id;
    this.isCoverageModelDropdownOpen = false;
    this.cdr.detectChanges();
  }

  // Identical to BenchmarkComponent.formatThinkingLevel and .showReasoningBadge, so the two
  // selectors can never disagree about what they display. If either changes, change both.
  formatThinkingLevel(level: string | null | undefined): string {
    if (!level) return 'Default';
    return level.charAt(0).toUpperCase() + level.slice(1);
  }

  showReasoningBadge(mode: string | null | undefined): boolean {
    if (!mode) return false;
    const lower = mode.toLowerCase();
    return lower !== 'default' && lower !== 'standard';
  }

  selectTab(tab: SuiteHealthTab): void {
    this.activeTab = tab;
    this.cdr.detectChanges();
  }

  onTabKeydown(event: KeyboardEvent, index: number): void {
    let next = index;
    if (event.key === 'ArrowRight') next = (index + 1) % this.tabOrder.length;
    else if (event.key === 'ArrowLeft') next = (index - 1 + this.tabOrder.length) % this.tabOrder.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = this.tabOrder.length - 1;
    else return;

    event.preventDefault();
    this.activeTab = this.tabOrder[next];
    const target = document.getElementById(`sh-tab-${this.activeTab}`);
    target?.focus();
  }

  /**
   * Re-fetches the active tab's stored analysis. Citations and Coverage are deliberately
   * excluded: both are explicit actions with their own buttons, and Coverage spends AI tokens,
   * so neither may be re-triggered by a generic Refresh.
   */
  refreshActiveTab(): void {
    if (this.activeTab === 'items') this.loadItemAnalysis();
    else if (this.activeTab === 'gaps') this.loadRubricGaps();
  }

  get canRefreshActiveTab(): boolean {
    return this.activeTab === 'items' || this.activeTab === 'gaps';
  }

  loadItemAnalysis(): void {
    if (this.suiteId == null) return;

    this.loadingItems = true;
    this.itemsError = null;
    this.benchmarkService.getItemAnalysis(this.suiteId).subscribe({
      next: (data) => {
        this.itemAnalysis = data;
        this.loadingItems = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingItems = false;
        this.itemsError = err?.error || 'Failed to load the item analysis.';
        this.cdr.detectChanges();
      }
    });
  }

  loadRubricGaps(): void {
    if (this.suiteId == null) return;

    this.loadingGaps = true;
    this.gapsError = null;
    this.benchmarkService.getRubricGaps(this.suiteId).subscribe({
      next: (data) => {
        this.rubricGaps = data;
        this.loadingGaps = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingGaps = false;
        this.gapsError = err?.error || 'Failed to load the rubric gap report.';
        this.cdr.detectChanges();
      }
    });
  }

  validateCitations(): void {
    if (this.suiteId == null) return;

    this.loadingCitations = true;
    this.citationsError = null;
    this.benchmarkService.validateCitations(this.suiteId).subscribe({
      next: (data) => {
        this.citations = data;
        this.loadingCitations = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingCitations = false;
        this.citationsError = err?.error || 'Failed to validate the rubric citations.';
        this.cdr.detectChanges();
      }
    });
  }

  analyzeCoverage(): void {
    if (this.suiteId == null || this.coverageModelConfigId == null || this.analyzingCoverage) return;

    this.analyzingCoverage = true;
    this.coverageError = null;
    this.benchmarkService.analyzeCoverage(this.suiteId, this.coverageModelConfigId).subscribe({
      next: (data) => {
        this.coverage = data;
        this.coverageError = data.errorMessage ?? null;
        this.analyzingCoverage = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.analyzingCoverage = false;
        this.coverageError = err?.error || 'The coverage analysis failed.';
        this.cdr.detectChanges();
      }
    });
  }

  editQuestion(questionId: number): void {
    this.editQuestionRequested.emit(questionId);
  }

  // --- Banner ---

  /**
   * True when nothing in the item table should be read as a measurement: too few runs, or runs
   * graded by more than one assessor, or runs scored under more than one scoring method version.
   * The banner says which of the three it is.
   */
  get itemsAdvisory(): boolean {
    const a = this.itemAnalysis;
    if (!a) return false;
    return a.runCount < a.minRunsForMeasurement
      || a.distinctAssessorCount > 1
      || a.distinctScoringMethodVersionCount > 1;
  }

  get assessorMixed(): boolean {
    return (this.itemAnalysis?.distinctAssessorCount ?? 0) > 1;
  }

  get scoringMethodMixed(): boolean {
    return (this.itemAnalysis?.distinctScoringMethodVersionCount ?? 0) > 1;
  }

  // --- Summary tiles ---
  //
  // Counts only, and never a mean or any other derived measurement. A suite-level average
  // quality tile would be the exact mistake the advisory banner exists to prevent: a figure
  // large and legible enough to be read before the caveat that says it is not a measurement.
  // A count of items in a state is true regardless of sample size.

  get itemsWithRunsCount(): number {
    return this.itemAnalysis?.items.filter(i => i.runCount > 0).length ?? 0;
  }

  get itemsBelowFloorCount(): number {
    const floor = this.itemAnalysis?.minRunsForMeasurement ?? 4;
    return this.itemAnalysis?.items.filter(i => i.runCount < floor).length ?? 0;
  }

  get flaggedItemCount(): number {
    return this.itemAnalysis?.items.filter(i => i.flagNames.length > 0).length ?? 0;
  }

  get confoundedItemCount(): number {
    return this.itemAnalysis?.items.filter(i => i.confounded).length ?? 0;
  }

  // --- Tooltip anchors ---
  //
  // One id per row and per flag, so each interestfor anchor name is unique. The anchor pair is
  // written with [attr.style] in the template rather than [style.anchor-name]: Angular's style
  // binding silently discards properties the browser does not recognise, which is precisely
  // the case in the browsers that need the anchor-positioning polyfill.

  flagTipId(item: BenchmarkItemStatisticsDto, flag: string): string {
    return `sh-tip-flag-${item.questionId}-${flag}`;
  }

  verdictTipId(questionId: number, index: number): string {
    return `sh-tip-verdict-${questionId}-${index}`;
  }

  editTipId(item: BenchmarkItemStatisticsDto): string {
    return `sh-tip-edit-${item.questionId}`;
  }

  // --- Sorting ---

  sortBy(column: ItemSortColumn): void {
    if (this.sortColumn === column) {
      this.sortDirection = this.sortDirection === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortColumn = column;
      this.sortDirection = ['meanQuality', 'spread', 'discrimination', 'meanToolCalls', 'flags', 'sample'].includes(column) ? 'desc' : 'asc';
    }
    this.cdr.detectChanges();
  }

  getAriaSort(column: ItemSortColumn): 'ascending' | 'descending' | 'none' {
    if (this.sortColumn !== column) return 'none';
    return this.sortDirection === 'asc' ? 'ascending' : 'descending';
  }

  getSortAriaLabel(column: ItemSortColumn, label: string): string {
    if (this.sortColumn !== column) {
      return `Sort by ${label}`;
    }
    return this.sortDirection === 'asc' ? `Sort by ${label} descending` : `Sort by ${label} ascending`;
  }

  get sortedItems(): BenchmarkItemStatisticsDto[] {
    if (!this.itemAnalysis?.items) return [];
    const items = [...this.itemAnalysis.items];
    const dir = this.sortDirection === 'asc' ? 1 : -1;
    const col = this.sortColumn;

    return items.sort((a, b) => {
      let valA: number | null | undefined;
      let valB: number | null | undefined;

      switch (col) {
        case 'orderIndex':
          valA = a.orderIndex;
          valB = b.orderIndex;
          break;
        case 'meanQuality':
          valA = a.runCount > 0 ? a.meanQuality : null;
          valB = b.runCount > 0 ? b.meanQuality : null;
          break;
        case 'spread':
          valA = a.runCount > 1 ? (a.maxQuality - a.minQuality) : (a.runCount === 1 ? 0 : null);
          valB = b.runCount > 1 ? (b.maxQuality - b.minQuality) : (b.runCount === 1 ? 0 : null);
          break;
        case 'assessedDifficulty':
          valA = a.assessedDifficulty ?? null;
          valB = b.assessedDifficulty ?? null;
          break;
        case 'empiricalDifficulty':
          valA = a.runCount > 0 ? (a.empiricalDifficulty ?? null) : null;
          valB = b.runCount > 0 ? (b.empiricalDifficulty ?? null) : null;
          break;
        case 'difficultyDelta':
          valA = a.difficultyDelta ?? null;
          valB = b.difficultyDelta ?? null;
          break;
        case 'discrimination':
          valA = a.discrimination ?? null;
          valB = b.discrimination ?? null;
          break;
        case 'meanToolCalls':
          valA = a.runCount > 0 ? a.meanToolCalls : null;
          valB = b.runCount > 0 ? b.meanToolCalls : null;
          break;
        case 'flags':
          valA = a.flagNames ? a.flagNames.length : 0;
          valB = b.flagNames ? b.flagNames.length : 0;
          break;
        case 'sample':
          valA = a.runCount;
          valB = b.runCount;
          break;
        default:
          valA = a.orderIndex;
          valB = b.orderIndex;
      }

      if (valA == null && valB == null) return a.orderIndex - b.orderIndex;
      if (valA == null) return 1;
      if (valB == null) return -1;

      if (valA !== valB) {
        return (valA < valB ? -1 : 1) * dir;
      }
      return a.orderIndex - b.orderIndex;
    });
  }

  // --- Row rendering ---

  discriminationLabel(item: BenchmarkItemStatisticsDto): string {
    if (item.discrimination == null) {
      const floor = this.itemAnalysis?.minRunsForDiscrimination ?? 4;
      return `insufficient data (< ${floor} runs)`;
    }
    return item.discrimination.toFixed(1);
  }

  deltaLabel(item: BenchmarkItemStatisticsDto): string {
    if (item.difficultyDelta == null) return 'not rated';
    return `${item.difficultyDelta > 0 ? '+' : ''}${item.difficultyDelta}`;
  }

  spreadLabel(item: BenchmarkItemStatisticsDto): string {
    if (item.runCount === 0) return '—';
    return item.runCount === 1
      ? `${item.minQuality}`
      : `${item.minQuality}–${item.maxQuality}`;
  }

  /** One line per row, so a reader cannot see a figure without its sample. */
  sampleLabel(item: BenchmarkItemStatisticsDto): string {
    return `${item.runCount} run(s) / ${item.distinctModelCount} model(s) / `
      + `${item.distinctAssessorCount} assessor(s) / ${item.distinctScoringMethodVersionCount} scoring method(s)`;
  }

  formatFlagName(flag: string): string {
    switch (flag) {
      case 'AssessorConfounded': return 'Assessor Confounded';
      case 'ScoringMethodMixed': return 'Scoring Method Mixed';
      case 'BudgetBound': return 'Budget Bound';
      default:
        return flag.replace(/([a-z])([A-Z])/g, '$1 $2');
    }
  }

  flagTitle(flag: string): string {
    switch (flag) {
      case 'Saturated': return 'Every model scores near the ceiling; the item carries little information.';
      case 'Miscalibrated': return 'The assessed difficulty and the empirical one disagree materially, and the assessed one weights the Intelligence Index.';
      case 'Unstable': return 'Wide spread across runs — either genuinely discriminating or ambiguous.';
      case 'BudgetBound': return 'Most runs reached or nearly reached the tool call budget; the cap may be setting the score.';
      case 'AssessorConfounded': return 'More than one assessor graded these runs, so the spread mixes candidate ability with grader severity.';
      case 'ScoringMethodMixed': return 'More than one scoring method version, which grade accuracy by different rules — the scores are not the same measurement.';
      default: return flag;
    }
  }

  verdictLabel(verdict: string): string {
    switch (verdict) {
      case 'VerifiedRubricGap': return 'Verified rubric gap';
      case 'LikelyRubricGap': return 'Likely rubric gap';
      default: return 'Likely hallucination';
    }
  }

  verdictTitle(verdict: string): string {
    switch (verdict) {
      case 'VerifiedRubricGap':
        return 'Verified against the source code or wiki by a claim verifier with a citation. Stronger evidence than cross-model agreement; consider updating the rubric to cover this fact.';
      case 'LikelyRubricGap':
        return 'Raised by two or more independent model families. Two unrelated models inventing the same specific fact is unlikely; a rubric that omits a fact both know is likely.';
      default:
        return 'Raised by one model family only, or refuted by source code citation. This is a finding about that model, already visible on its run — not a suite issue.';
    }
  }

  get selectedCoverageModel(): SystemAiConfigDto | undefined {
    return this.benchmarkCapableConfigs.find(c => c.id === this.coverageModelConfigId);
  }

  toggleRubricCheckerDropdown(event: Event): void {
    event.stopPropagation();
    this.isRubricCheckerDropdownOpen = !this.isRubricCheckerDropdownOpen;
    this.cdr.detectChanges();
  }

  selectRubricChecker(config: SystemAiConfigDto): void {
    this.rubricCheckerConfigId = config.id;
    this.isRubricCheckerDropdownOpen = false;
    this.cdr.detectChanges();
  }

  get selectedRubricChecker(): SystemAiConfigDto | undefined {
    return this.benchmarkCapableConfigs.find(c => c.id === this.rubricCheckerConfigId);
  }

  startRubricCheck(questionIds?: number[]): void {
    if (this.suiteId == null || this.rubricCheckerConfigId == null) return;
    this.runningRubricCheck = true;
    this.rubricCheckError = null;

    this.benchmarkService.startRubricCheck({
      suiteId: this.suiteId,
      checkerModelConfigurationId: this.rubricCheckerConfigId,
      questionIds: questionIds && questionIds.length > 0 ? questionIds : null
    }).subscribe({
      next: (res) => {
        this.startRubricCheckPolling(res.jobId);
      },
      error: (err) => {
        this.runningRubricCheck = false;
        this.rubricCheckError = err?.error?.message || err?.error || 'Failed to start rubric check.';
        this.cdr.detectChanges();
      }
    });
  }

  startRubricCheckPolling(jobId: string): void {
    this.stopRubricCheckPolling();
    this.pollRubricCheck(jobId);

    this.rubricCheckPollInterval = setInterval(() => {
      if (typeof document !== 'undefined' && document.hidden) return;
      this.pollRubricCheck(jobId);
    }, 2000);
  }

  stopRubricCheckPolling(): void {
    if (this.rubricCheckPollInterval) {
      clearInterval(this.rubricCheckPollInterval);
      this.rubricCheckPollInterval = null;
    }
  }

  pollRubricCheck(jobId: string): void {
    this.benchmarkService.getRubricCheck(jobId).subscribe({
      next: (job) => {
        this.rubricCheckJob = job;
        if (job.status !== 'Running') {
          this.runningRubricCheck = false;
          this.stopRubricCheckPolling();
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.rubricCheckError = err?.error?.message || err?.error || 'Failed to poll rubric check job.';
        this.runningRubricCheck = false;
        this.stopRubricCheckPolling();
        this.cdr.detectChanges();
      }
    });
  }

  cancelRubricCheck(): void {
    if (!this.rubricCheckJob || this.rubricCheckJob.status !== 'Running') return;
    this.cancellingRubricCheck = true;
    this.benchmarkService.cancelRubricCheck(this.rubricCheckJob.id).subscribe({
      next: () => {
        this.cancellingRubricCheck = false;
        this.runningRubricCheck = false;
        this.stopRubricCheckPolling();
        this.pollRubricCheck(this.rubricCheckJob!.id);
      },
      error: (err) => {
        this.cancellingRubricCheck = false;
        this.cdr.detectChanges();
      }
    });
  }

  // --- Rubric Gap Author ---
  //
  // The drafting half mirrors the rubric checker above: same selector markup, same single-job
  // polling lifecycle, same cancel path. The acceptance half is what differs, and every difference
  // is there to keep authorship with the operator — see acceptDraft.

  toggleRubricGapAuthorDropdown(event: Event): void {
    event.stopPropagation();
    this.isRubricGapAuthorDropdownOpen = !this.isRubricGapAuthorDropdownOpen;
    this.cdr.detectChanges();
  }

  selectRubricGapAuthorModel(config: SystemAiConfigDto): void {
    this.rubricGapAuthorConfigId = config.id;
    this.isRubricGapAuthorDropdownOpen = false;
    this.cdr.detectChanges();
  }

  get selectedRubricGapAuthorModel(): SystemAiConfigDto | undefined {
    return this.benchmarkCapableConfigs.find(c => c.id === this.rubricGapAuthorConfigId);
  }

  startRubricGapAuthor(clusterKeys?: string[]): void {
    if (this.suiteId == null || this.rubricGapAuthorConfigId == null || this.runningRubricGapAuthor) return;

    this.runningRubricGapAuthor = true;
    this.rubricGapAuthorError = null;
    this.draftAcceptErrors = {};

    this.gapAuthorService.startRubricGapAuthor({
      suiteId: this.suiteId,
      authorModelConfigurationId: this.rubricGapAuthorConfigId,
      instructions: this.rubricGapAuthorInstructions.trim() ? this.rubricGapAuthorInstructions.trim() : null,
      clusterKeys: clusterKeys && clusterKeys.length > 0 ? clusterKeys : null
    }).subscribe({
      next: (res) => {
        this.startRubricGapAuthorPolling(res.jobId);
      },
      error: (err) => {
        this.runningRubricGapAuthor = false;
        this.rubricGapAuthorError = err?.error?.error || err?.error?.message || err?.error
          || 'Failed to start the rubric gap author.';
        this.cdr.detectChanges();
      }
    });
  }

  startRubricGapAuthorPolling(jobId: string): void {
    this.stopRubricGapAuthorPolling();
    this.pollRubricGapAuthor(jobId);

    // Only arm the timer if that first poll did not already find a terminal job. Arming it
    // unconditionally would install an interval the poll's own stop call had already run past,
    // leaving a job that finished immediately being re-fetched forever.
    if (!this.runningRubricGapAuthor) return;

    this.rubricGapAuthorPollInterval = setInterval(() => {
      if (typeof document !== 'undefined' && document.hidden) return;
      this.pollRubricGapAuthor(jobId);
    }, 2000);
  }

  stopRubricGapAuthorPolling(): void {
    if (this.rubricGapAuthorPollInterval) {
      clearInterval(this.rubricGapAuthorPollInterval);
      this.rubricGapAuthorPollInterval = null;
    }
  }

  pollRubricGapAuthor(jobId: string): void {
    this.gapAuthorService.getRubricGapAuthor(jobId).subscribe({
      next: (job) => {
        this.applyRubricGapAuthorJob(job);
        if (job.status !== 'Running') {
          this.runningRubricGapAuthor = false;
          this.stopRubricGapAuthorPolling();
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.rubricGapAuthorError = err?.error?.error || err?.error?.message || err?.error
          || 'Failed to poll the rubric gap author job.';
        this.runningRubricGapAuthor = false;
        this.stopRubricGapAuthorPolling();
        this.cdr.detectChanges();
      }
    });
  }

  /**
   * Seeds a textarea for every draft that has just acquired proposed text, and leaves every key
   * that already exists alone. A poll arrives every two seconds; re-seeding unconditionally would
   * throw away whatever the operator typed since the last one.
   */
  private applyRubricGapAuthorJob(job: RubricGapAuthorJobDto): void {
    this.rubricGapAuthorJob = job;
    for (const draft of job.drafts) {
      if (draft.proposedText != null && !(draft.clusterKey in this.draftEdits)) {
        this.draftEdits[draft.clusterKey] = draft.proposedText;
      }
    }
  }

  cancelRubricGapAuthor(): void {
    if (!this.rubricGapAuthorJob || this.rubricGapAuthorJob.status !== 'Running') return;
    this.cancellingRubricGapAuthor = true;
    const jobId = this.rubricGapAuthorJob.id;
    this.gapAuthorService.cancelRubricGapAuthor(jobId).subscribe({
      next: () => {
        this.cancellingRubricGapAuthor = false;
        this.runningRubricGapAuthor = false;
        this.stopRubricGapAuthorPolling();
        this.pollRubricGapAuthor(jobId);
      },
      error: () => {
        this.cancellingRubricGapAuthor = false;
        this.cdr.detectChanges();
      }
    });
  }

  // --- Draft editing and acceptance ---

  /**
   * A cluster key is `questionId:index`, and a colon is legal in an id but has to be escaped in
   * every selector that looks the element up. Sanitising here keeps the label/textarea pairing
   * trivially addressable from tests and from the browser's own accessibility tooling.
   */
  draftTextareaId(draft: RubricGapAuthorDraftDto): string {
    return 'sh-draft-' + draft.clusterKey.replace(/[^A-Za-z0-9_-]/g, '-');
  }

  /** The textarea's current contents: the operator's edit if there is one, else the draft. */
  draftTextFor(draft: RubricGapAuthorDraftDto): string {
    const edited = this.draftEdits[draft.clusterKey];
    return edited !== undefined ? edited : (draft.proposedText ?? '');
  }

  onDraftTextInput(draft: RubricGapAuthorDraftDto, event: Event): void {
    this.draftEdits[draft.clusterKey] = (event.target as HTMLTextAreaElement).value;
    // Clearing the stale error here keeps a rejected acceptance from labelling text that has since
    // been rewritten to fix exactly what the server complained about.
    delete this.draftAcceptErrors[draft.clusterKey];
  }

  /**
   * Compared on trimmed text with an exact match, because that is how the server decides whether an
   * acceptance was verbatim. A marker that disagreed with the stored provenance flag would be worse
   * than no marker at all.
   */
  isDraftModified(draft: RubricGapAuthorDraftDto): boolean {
    return this.draftTextFor(draft).trim() !== (draft.proposedText ?? '').trim();
  }

  revertDraft(draft: RubricGapAuthorDraftDto): void {
    this.draftEdits[draft.clusterKey] = draft.proposedText ?? '';
    delete this.draftAcceptErrors[draft.clusterKey];
    this.cdr.detectChanges();
  }

  isDraftAccepted(draft: RubricGapAuthorDraftDto): boolean {
    return draft.clusterKey in this.acceptedDrafts;
  }

  acceptanceFor(draft: RubricGapAuthorDraftDto): RubricAdditionAcceptanceDto | undefined {
    return this.acceptedDrafts[draft.clusterKey];
  }

  canAcceptDraft(draft: RubricGapAuthorDraftDto): boolean {
    return draft.status === 'Completed'
      && this.draftTextFor(draft).trim().length > 0
      && !this.isDraftAccepted(draft)
      && this.acceptingClusterKey == null;
  }

  /**
   * Accepts exactly one draft, as the text currently standing in its textarea.
   *
   * There is no list form of this and there must not be one. The rubric is curated knowledge, and
   * the authorship claim behind it rests on a human having read and submitted each addition
   * individually; a bulk action would leave the same database rows behind while making that claim
   * false. Sending the textarea rather than the draft is the other half of the same point — the
   * server stores what was submitted and derives the verbatim-or-edited flag from it.
   */
  acceptDraft(draft: RubricGapAuthorDraftDto): void {
    if (!this.canAcceptDraft(draft)) return;

    const acceptedText = this.draftTextFor(draft).trim();
    this.acceptingClusterKey = draft.clusterKey;
    delete this.draftAcceptErrors[draft.clusterKey];

    this.gapAuthorService.acceptRubricAddition(draft.questionId, {
      acceptedText,
      jobId: this.rubricGapAuthorJob?.id ?? null,
      clusterKey: draft.clusterKey
    }).subscribe({
      next: (acceptance) => {
        this.acceptedDrafts[draft.clusterKey] = acceptance;
        this.acceptingClusterKey = null;
        // The acceptance bumped the question's item revision, so the stored gap report and the item
        // statistics both describe a suite that no longer exists. Re-fetch rather than let the
        // panel keep rendering figures for the previous revision.
        this.loadRubricGaps();
        this.loadItemAnalysis();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.acceptingClusterKey = null;
        this.draftAcceptErrors[draft.clusterKey] = err?.error?.error || err?.error?.message || err?.error
          || 'Failed to accept the rubric addition.';
        this.cdr.detectChanges();
      }
    });
  }

  draftStatusLabel(draft: RubricGapAuthorDraftDto): string {
    switch (draft.status) {
      case 'Pending': return 'Queued';
      case 'Drafting': return 'Drafting';
      case 'Completed': return 'Draft ready';
      case 'Skipped': return 'Skipped';
      default: return 'Failed';
    }
  }

  onVerifyQuestion(questionId: number): void {
    this.verifyQuestionRequested.emit(questionId);
  }

  formatQuestionIndices(indices: number[]): string {
    if (!indices || indices.length === 0) return 'None';
    return indices.map(i => `Q${i}`).join(', ');
  }
}
