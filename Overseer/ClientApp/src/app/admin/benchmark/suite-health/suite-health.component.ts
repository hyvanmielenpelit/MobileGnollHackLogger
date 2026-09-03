import { Component, EventEmitter, HostListener, Input, OnChanges, OnInit, Output, SimpleChanges, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import {
  AdminBenchmarkService,
  BenchmarkSuiteItemAnalysisDto,
  BenchmarkItemStatisticsDto,
  BenchmarkRubricGapReportDto,
  BenchmarkCitationReportDto,
  BenchmarkCoverageReportDto
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';

export type SuiteHealthTab = 'items' | 'gaps' | 'citations' | 'coverage';

/**
 * Suite health: what the stored runs say about the *suite* rather than about the models.
 *
 * Read-only by construction. The panel's only outward action is
 * {@link SuiteHealthComponent.editQuestion}, which asks the host to open a question for editing —
 * there is no write endpoint behind any of these reports, and in particular no action that copies
 * an item's empirical difficulty into its assessed difficulty. That number weights the
 * Intelligence Index, so deriving it from the scores it weights would be circular, and it would
 * let a model that did badly on an item retroactively reduce that item's weight.
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
export class SuiteHealthComponent implements OnInit, OnChanges {
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);

  @Input() suiteId: number | null = null;
  @Input() suiteName = '';

  /** Benchmark-capable configurations, for the coverage analysis model selector. */
  @Input() benchmarkCapableConfigs: SystemAiConfigDto[] = [];

  /** The host opens the question editor; this component never writes anything itself. */
  @Output() editQuestionRequested = new EventEmitter<number>();

  activeTab: SuiteHealthTab = 'items';

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

  private readonly tabOrder: SuiteHealthTab[] = ['items', 'gaps', 'citations', 'coverage'];

  ngOnInit(): void {
    // The flag and verdict explanations are interestfor + popover="hint" tooltips rather than
    // title attributes, which are unstyleable and never appear on keyboard focus. This
    // feature-detects and lazily imports, so a supporting browser downloads nothing.
    ensureOverlayPolyfills();
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
    return verdict === 'LikelyRubricGap' ? 'Likely rubric gap' : 'Likely hallucination';
  }

  verdictTitle(verdict: string): string {
    return verdict === 'LikelyRubricGap'
      ? 'Raised by two or more independent model families. Two unrelated models inventing the same specific fact is unlikely; a rubric that omits a fact both know is likely.'
      : 'Raised by one model family only. This is a finding about that model, already visible on its run — not a suite issue.';
  }

  get selectedCoverageModel(): SystemAiConfigDto | undefined {
    return this.benchmarkCapableConfigs.find(c => c.id === this.coverageModelConfigId);
  }
}
