import {
  AfterViewChecked,
  AfterViewInit,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  QueryList,
  SimpleChanges,
  ViewChild,
  ViewChildren,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { BaseChartDirective } from 'ng2-charts';
import type { ChartConfiguration, ChartType, Plugin } from 'chart.js';

import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { exactFilter, TableState } from '../../../shared/data-table/table-state';
import { SortHeaderComponent } from '../../../shared/data-table/sort-header.component';
import { TablePagerComponent } from '../../../shared/data-table/table-pager.component';
import {
  BarOrientation,
  ComparisonFigureSet,
  CostMeasure,
  DEFAULT_MODEL_SORT,
  IdentityGlyph,
  MAX_PLOTTED_ENTRIES,
  ModelComparisonContext,
  ModelComparisonEntry,
  ModelSort,
  ModelSortKey,
  P1_STACK_BREAKPOINT_PX,
  ProfileNormalization,
  ReducedMotionWatcher,
  SortDirection,
  SpeedMeasure,
  buildComparisonFigures,
  glyphFor,
  normalizeProfile
} from './model-comparison-charts';
import { MAX_COMPARISON_SOURCES } from './comparison-source-picker.component';
import {
  BenchmarkModelComparisonDto,
  BenchmarkModelComparisonEntryDto,
  BenchmarkModelComparisonPricingBasis,
  toChartContext,
  toChartEntries,
  unmeasuredAxes
} from './model-comparison.models';
import {
  FIGURE_EXPORT_LAYOUT_WIDTH,
  FIGURE_EXPORT_MAX_DIMENSION,
  FIGURE_EXPORT_MIN_DIMENSION,
  FIGURE_EXPORT_PRESETS,
  FigureExportFormat,
  FigureExportRequest,
  FigureExportResolution,
  FigureExportResult,
  composeFigureImage,
  encodeFigureImage,
  figureExportFilename,
  renderPlotOffscreen,
  resolveFigureLayout,
  saveFigureBlob
} from './figure-export';

/** Which wizard step is on screen. Three, in a fixed order: sources, then filters, then figures. */
export type ComparisonWizardStep = 1 | 2 | 3;

/** One figure's chrome, as the export composer and the layout resolver both take it. */
type FigureExportChrome = Omit<FigureExportRequest, 'canvas' | 'format' | 'layout'>;

/**
 * Which entries the figures are allowed to draw.
 *
 * `all` charts comparable and degraded entries alike, each degraded axis carrying the notice that
 * names it. `comparableOnly` keeps degraded entries in the table and out of every figure, for a
 * reader who wants the strict set. Neither setting can reach an excluded entry: the server returns
 * no measures for one at all.
 */
export type ComparabilityStrictness = 'all' | 'comparableOnly';

/** A degenerate shape the entry set can take, each of which is rendered differently. */
export type ComparisonShape = 'empty' | 'none' | 'single' | 'pair' | 'full';

/** One statistic in the single-entry KPI row, where a chart would be one bar and say nothing. */
export interface ComparisonStatTile {
  readonly label: string;
  readonly value: string;
  readonly detail: string;
}

/**
 * One figure ready to render: its chart.js inputs beside the chrome the template draws as real text.
 *
 * The chart core types each figure by its own chart type and datum shape, which is what makes its
 * builders type-safe; a template renders them in one loop, so the card widens them back to the
 * directive's own erased inputs. Titles, captions and notices stay strings rather than chart.js
 * plugins, so they are selectable text a screen reader reaches without touching the canvas.
 */
export interface ComparisonFigureCard {
  readonly id: string;
  readonly title: string;
  readonly subtitle: string;
  readonly caption: string;
  readonly notices: readonly string[];
  /** Which corner of a scatter is the good one, as a caption. Never a reversed axis. */
  readonly cornerLabel: string;
  readonly ariaLabel: string;
  readonly type: ChartType;
  readonly data: ChartConfiguration['data'];
  readonly options: ChartConfiguration['options'];
  readonly plugins: Plugin[];
  readonly heightPx: number;
}

/**
 * Cross-model comparison: six figures over one comparable set, and the table that is the accessible
 * record of them.
 *
 * The component owns no fetching. It renders the response the host hands it and emits the one
 * control that changes what is fetched — the pricing basis — so this view stays a pure function of
 * one payload.
 *
 * Three display rules here are load-bearing rather than cosmetic:
 *
 * 1. **The table is present, always, and is never behind a toggle.** A chart is far more persuasive
 *    than a table, and a reader will trust six figures without checking twenty-three comparability
 *    keys. The canvases are `role="img"` summaries; the table is the artefact that carries the
 *    numbers, the states and the differing keys, and it renders whether or not anything is plotted.
 * 2. **An excluded entry stays visible and stays explained.** The service refuses to return measures
 *    for one, so it can never reach a figure; dropping it from the view as well would make an
 *    unchartable model invisible, which is exactly how a reader concludes a set is comparable when
 *    it is not. Excluded entries are tabulated with the keys they differ on named.
 * 3. **A degraded axis says which axis and why.** Speed and cost degrade independently of quality,
 *    so a set can be trustworthy on one axis and not on another, and the notices are per figure.
 *
 * Every control that narrows the **figures** sits in one filter row above every one of them. A
 * filter inside a chart card would leave the six figures describing different slices of the same
 * set. Controls that decide which sources are in the request at all are a different stage of the
 * same task and belong to the source picker, next to the tables they scope — which is why suite
 * scope lives there and pricing basis, which changes only the cost arithmetic over an unchanged
 * set, lives here.
 */
@Component({
  selector: 'app-benchmark-model-comparison',
  standalone: true,
  imports: [CommonModule, FormsModule, BaseChartDirective, SortHeaderComponent, TablePagerComponent],
  templateUrl: './model-comparison.component.html',
  styleUrls: ['./model-comparison.component.scss']
})
export class ModelComparisonComponent implements OnInit, OnChanges, AfterViewInit, AfterViewChecked, OnDestroy {
  /** Protected rather than private: the filter row calls it directly after a `TableState` mutation. */
  protected cdr = inject(ChangeDetectorRef);

  /** The comparison to render. Null before the first fetch, and while one is in flight. */
  @Input() comparison: BenchmarkModelComparisonDto | null = null;

  @Input() loading = false;

  @Input() error: string | null = null;

  /** The price card every candidate cost is computed from. Server-side: changing it refetches. */
  @Input() pricingBasis: BenchmarkModelComparisonPricingBasis = 'Current';

  @Output() pricingBasisChange = new EventEmitter<BenchmarkModelComparisonPricingBasis>();
  @Output() refresh = new EventEmitter<void>();

  // --- Wizard inputs and outputs ---
  //
  // The source picker is projected rather than bound, so the host keeps owning the picker's inputs
  // and this component needs no pass-through of them. Step 1's validity is judged here, though, so
  // the two facts about the selection that Next reads do arrive as inputs.

  /** How many runs and analysis groups the host currently has selected. */
  @Input() selectedSourceCount = 0;

  /** The request cap. Above it Compare is refused rather than truncated. */
  @Input() maxSources = MAX_COMPARISON_SOURCES;

  /**
   * Compare, emitted by the wizard footer rather than by the picker.
   *
   * Two Compare affordances on one screen would disagree the moment one of them was disabled, so
   * the picker offers none and this is the only one.
   */
  @Output() compare = new EventEmitter<void>();

  /** Step 3's Close, and the header's close control. The host owns the dialog element. */
  @Output() closeRequested = new EventEmitter<void>();

  /** Focused by the host after showModal(), which would otherwise focus the close button. */
  @ViewChild('wizardHeading') wizardHeading?: ElementRef<HTMLElement>;

  /** The container query root, measured to decide P1's bar orientation. */
  @ViewChild('chartsHost') chartsHost?: ElementRef<HTMLElement>;

  /** The live chart directives, in template order, so an export reads the rendered canvas. */
  @ViewChildren(BaseChartDirective) chartDirectives?: QueryList<BaseChartDirective>;

  // --- Client-side filter state, all of it scoping every figure at once ---

  /** Entry keys the figures may draw. Excluded entries are never in it; the server gives them no numbers. */
  includedKeys: string[] = [];

  strictness: ComparabilityStrictness = 'all';

  /**
   * P1's model order, shared by all three panels and by every other figure's series order.
   *
   * One control, not three: panels that sorted independently would stop a row meaning one model,
   * which is the whole reason the three are drawn as small multiples rather than separately.
   */
  sort: ModelSort = DEFAULT_MODEL_SORT;

  /**
   * Time to first token by default, not Speed Index.
   *
   * Speed Index saturates — half the scored answers finish inside their difficulty-scaled target,
   * so several models sit at the ceiling and read as equally fast when their real latency differs
   * severalfold — and the server classes it as a table figure for that reason. The switch is offered
   * because the index is what the run report scores on, and selecting it raises the saturation
   * notice on the panel.
   */
  speedMeasure: SpeedMeasure = 'ttftP50';

  /**
   * Candidate cost for the whole suite. There is no second measure to switch to: the endpoint's cost
   * object is candidate-only by design, so no run total including grading roles exists to plot, and
   * the control shows that option disabled rather than omitting it silently.
   */
  costMeasure: CostMeasure = 'candidateSuite';

  /** The model under the pointer or the keyboard, highlighted in every panel and in the profile. */
  highlightedKey: string | null = null;

  /** Models pinned into the accent, so a comparison survives the pointer leaving the row. */
  emphasisKeys: string[] = [];

  // --- Derived render state ---

  entries: readonly BenchmarkModelComparisonEntryDto[] = [];
  figures: ComparisonFigureSet | null = null;
  context: ModelComparisonContext = { itemsPerRun: 0, pricingBasisLabel: '', suiteName: '' };
  orientation: BarOrientation = 'vertical';

  /** The profile's axis endpoints, printed beside P2 so its normalized heights stay anchored. */
  profileAxes: ProfileNormalization | null = null;

  /** The hard ceiling the chart core enforces; the picker names it so the cap is never a surprise. */
  readonly maxPlottedEntries = MAX_PLOTTED_ENTRIES;

  private chartEntries: readonly ModelComparisonEntry[] = [];
  private readonly reducedMotion = new ReducedMotionWatcher();
  private unsubscribeReducedMotion: (() => void) | null = null;
  private resizeObserver: ResizeObserver | null = null;

  /**
   * Sort, filter and page state for the table view.
   *
   * State sorts on a rank rather than the label so the first click puts the entries a reader has to
   * check — excluded, then degraded — at the top instead of ordering them alphabetically.
   */
  readonly entryTable = new TableState<BenchmarkModelComparisonEntryDto>('state', 'desc').registerAccessors(
    {
      label: e => e.label,
      runCount: e => e.runCount,
      state: e => this.stateOrder(e),
      intelligenceIndex: e => e.quality?.pointEstimate ?? null,
      ttftP50Ms: e => e.speed?.ttftP50Ms ?? null,
      speedIndex: e => e.table?.meanSpeedIndex ?? null,
      costPerQuestion: e => e.cost?.candidateCostPerQuestionUsd ?? null
    },
    {
      label: e => e.label,
      state: exactFilter(e => e.state)
    }
  );

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.unsubscribeReducedMotion = this.reducedMotion.subscribe(() => this.rebuild());
    this.rebuild();
  }

  ngOnChanges(changes: SimpleChanges): void {
    const change = changes['comparison'];
    if (!change) {
      return;
    }

    // A new payload is a new set of models, so the entry selection is re-seeded rather than
    // carried: a key held over from the previous suite would silently plot nothing.
    this.includedKeys = (this.comparison?.entries ?? [])
      .filter(entry => !entry.excluded)
      .map(entry => entry.key);
    this.emphasisKeys = [];
    this.highlightedKey = null;
    this.entryTable.page = 1;
    this.rebuild();

    // Not on the first change: that one is the initial binding, and step 1 is where the wizard
    // opens regardless of what the host already holds.
    if (!change.firstChange) {
      this.applyComparisonToStep(change.previousValue as BenchmarkModelComparisonDto | null);
    }
  }

  ngAfterViewInit(): void {
    this.observeContainerWidth();
    // The panel renders behind the host's @if, so the polyfill's first scan never saw these anchors.
    refreshAnchorPositioning();
  }

  /**
   * The chart container lives inside the figure branch, so it does not exist on the pass that runs
   * `ngAfterViewInit` for a set that starts empty or incomparable. The observer is attached the
   * first time the element appears instead, and never re-attached.
   */
  ngAfterViewChecked(): void {
    if (this.resizeObserver === null && this.chartsHost) {
      this.observeContainerWidth();
    }
  }

  ngOnDestroy(): void {
    this.unsubscribeReducedMotion?.();
    this.reducedMotion.dispose();
    this.resizeObserver?.disconnect();
    this.resizeObserver = null;
  }

  // ---------------------------------------------------------------------------------------------
  // The wizard
  //
  // Three steps in a fixed order, with the step header as a tablist and Previous / Next as the
  // primary traversal. Next is enabled only when the current step's selection is valid, and where
  // it is not, the reason is rendered as text beside it rather than left to a disabled button.
  // ---------------------------------------------------------------------------------------------

  step: ComparisonWizardStep = 1;

  readonly steps: readonly ComparisonWizardStep[] = [1, 2, 3];

  readonly stepTitles: Record<ComparisonWizardStep, string> = {
    1: 'Sources',
    2: 'Comparability & filters',
    3: 'Figures'
  };

  /**
   * Steps 2 and 3 need a computed comparison; step 3 additionally needs something chartable.
   *
   * An unreachable step is `aria-disabled`, not `disabled`: it stays in the focus order, so a
   * keyboard reader still learns the step exists and can read why it is unavailable.
   */
  isStepReachable(step: ComparisonWizardStep): boolean {
    if (step === 1) {
      return true;
    }
    if (this.comparison === null) {
      return false;
    }
    return step === 2 || this.showFigures;
  }

  get canGoPrevious(): boolean {
    return this.step > 1;
  }

  get canGoNext(): boolean {
    if (this.step === 1) {
      return !this.loading && this.selectedSourceCount > 0 && this.selectedSourceCount <= this.maxSources;
    }
    if (this.step === 2) {
      return this.showFigures;
    }
    return true;
  }

  /** Compare while the current selection has no computed comparison; Next once it does. */
  get nextLabel(): string {
    if (this.step === 3) {
      return 'Close';
    }
    return this.step === 1 && this.comparison === null ? 'Compare' : 'Next';
  }

  /** Why Next is unavailable, named beside it rather than left to a disabled button. */
  get nextBlockedReason(): string {
    if (this.canGoNext) {
      return '';
    }
    if (this.step === 1) {
      if (this.loading) {
        return 'A comparison is being computed.';
      }
      if (this.selectedSourceCount === 0) {
        return 'Select at least one run or analysis group.';
      }
      return `${this.selectedSourceCount} sources selected — at most ${this.maxSources} may be ` +
        'compared in one request.';
    }
    return this.shape === 'single'
      ? 'Only one entry is plotted; a comparison needs two.'
      : 'Nothing in this set may be charted together.';
  }

  goToStep(step: ComparisonWizardStep): void {
    if (!this.isStepReachable(step)) {
      return;
    }
    this.step = step;
    // Marked, like every other mutator here: several callers are outside a template event —
    // ngOnChanges, the keyboard handler, the host reopening the dialog.
    this.cdr.markForCheck();
  }

  previousStep(): void {
    if (this.canGoPrevious) {
      this.goToStep((this.step - 1) as ComparisonWizardStep);
    }
  }

  nextStep(): void {
    if (this.step === 3) {
      this.closeRequested.emit();
      return;
    }
    if (!this.canGoNext) {
      return;
    }
    if (this.step === 1 && this.comparison === null) {
      // The step advances in ngOnChanges when the payload lands, not here: advancing now would
      // show an empty step 2 for the length of the round trip.
      this.compare.emit();
      return;
    }
    this.goToStep((this.step + 1) as ComparisonWizardStep);
  }

  /**
   * Roving-tabindex keyboard support required by role="tablist": Left/Right move between steps
   * and wrap around, Home/End jump to the ends.
   *
   * Focus moves even onto a step that refuses to open — that is the whole point of marking it
   * `aria-disabled` rather than `disabled` — so the two calls here are deliberately independent.
   */
  onStepKeydown(event: KeyboardEvent, index: number): void {
    const targets: Record<string, number> = {
      ArrowRight: index + 1,
      ArrowLeft: index - 1,
      Home: 0,
      End: this.steps.length - 1
    };
    const requested = targets[event.key];
    if (requested === undefined) {
      return;
    }

    event.preventDefault();
    const next = this.steps[(requested + this.steps.length) % this.steps.length];
    this.goToStep(next);
    document.getElementById(`mc-step-tab-${next}`)?.focus();
  }

  /** Called by the host after showModal(), which would otherwise focus the close button. */
  focusHeading(): void {
    this.wizardHeading?.nativeElement.focus();
  }

  /**
   * Moves the wizard in step with the payload, and only where the payload changed state.
   *
   * A first comparison advances to step 2, because Compare on step 1 is what asked for it. A
   * refetch under an unchanged selection — the pricing basis control, which lives on step 2 —
   * replaces one non-null payload with another and must leave the step alone, or changing a cost
   * basis would yank the reader forward. Losing the payload drops back to step 1, where the
   * sources are: steps 2 and 3 have nothing to render without one.
   */
  private applyComparisonToStep(previous: BenchmarkModelComparisonDto | null | undefined): void {
    if (this.comparison === null) {
      this.step = 1;
      return;
    }
    if (!previous) {
      this.step = 2;
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Query controls — these change what the host fetches
  // ---------------------------------------------------------------------------------------------

  onPricingBasisChange(value: BenchmarkModelComparisonPricingBasis): void {
    this.pricingBasis = value;
    this.pricingBasisChange.emit(value);
  }

  onRefresh(): void {
    this.refresh.emit();
  }

  // ---------------------------------------------------------------------------------------------
  // Client-side filter controls — these re-render every figure against the same slice
  // ---------------------------------------------------------------------------------------------

  toggleEntry(key: string): void {
    this.includedKeys = this.includedKeys.includes(key)
      ? this.includedKeys.filter(k => k !== key)
      : [...this.includedKeys, key];
    this.rebuild();
  }

  isIncluded(key: string): boolean {
    return this.includedKeys.includes(key);
  }

  onStrictnessChange(value: ComparabilityStrictness): void {
    this.strictness = value;
    this.rebuild();
  }

  onSortKeyChange(value: ModelSortKey): void {
    this.sort = { key: value, direction: this.sort.direction };
    this.rebuild();
  }

  onSortDirectionChange(value: SortDirection): void {
    this.sort = { key: this.sort.key, direction: value };
    this.rebuild();
  }

  onSpeedMeasureChange(value: SpeedMeasure): void {
    this.speedMeasure = value;
    this.rebuild();
  }

  onCostMeasureChange(value: CostMeasure): void {
    this.costMeasure = value;
    this.rebuild();
  }

  /** Hover and keyboard focus light the same model in all three panels and in the profile. */
  setHighlight(key: string | null): void {
    if (this.highlightedKey === key) {
      return;
    }
    this.highlightedKey = key;
    this.rebuild();
  }

  toggleEmphasis(key: string): void {
    this.emphasisKeys = this.emphasisKeys.includes(key)
      ? this.emphasisKeys.filter(k => k !== key)
      : [...this.emphasisKeys, key];
    this.rebuild();
  }

  isEmphasised(key: string): boolean {
    return this.emphasisKeys.includes(key);
  }

  onTableChanged(): void {
    this.cdr.detectChanges();
  }

  // ---------------------------------------------------------------------------------------------
  // Shape of the set
  // ---------------------------------------------------------------------------------------------

  get plotted(): readonly ModelComparisonEntry[] {
    return this.figures?.selection.plotted ?? [];
  }

  /**
   * Which of the degenerate shapes this set has, each of which occurs in practice and each of which
   * is rendered differently. `pair` suppresses the profile plot: two polylines either cross once or
   * they do not, and the panels above already show that.
   */
  get shape(): ComparisonShape {
    if (this.entries.length === 0) {
      return 'empty';
    }
    const count = this.plotted.length;
    if (count === 0) {
      return 'none';
    }
    if (count === 1) {
      return 'single';
    }
    return count === 2 ? 'pair' : 'full';
  }

  get showFigures(): boolean {
    return this.shape === 'pair' || this.shape === 'full';
  }

  /** P2 needs three polylines before a crossing says anything the panels have not already said. */
  get showProfile(): boolean {
    return this.shape === 'full';
  }

  /** Every plotted entry rests on a single run, so no interval covers reproducibility at all. */
  get allSingleRun(): boolean {
    return this.plotted.length > 0 && this.plotted.every(entry => entry.runCount === 1);
  }

  get excludedEntries(): BenchmarkModelComparisonEntryDto[] {
    return this.entries.filter(entry => entry.excluded);
  }

  /** Comparable entries the strictness control is holding out of the figures. */
  get strictlyWithheldEntries(): BenchmarkModelComparisonEntryDto[] {
    return this.strictness === 'comparableOnly'
      ? this.entries.filter(entry => entry.state === 'Degraded')
      : [];
  }

  /** Entries the operator has taken out of the figures, which stay in the table regardless. */
  get deselectedEntries(): BenchmarkModelComparisonEntryDto[] {
    return this.entries.filter(entry => !entry.excluded && !this.includedKeys.includes(entry.key));
  }

  /** Selectable entries the current table page does not show, so a selection is never silently off-screen. */
  get offPageSelectionCount(): number {
    const onPage = new Set(this.entryTable.view(this.entries).map(e => e.key));
    return this.includedKeys.filter(key => !onPage.has(key)).length;
  }

  /** The three tiles that stand in for a one-bar bar chart, where the number is the chart. */
  get singleEntryTiles(): ComparisonStatTile[] {
    const plotted = this.plotted[0];
    const entry = plotted ? this.entries.find(e => e.key === plotted.key) : undefined;
    if (!entry) {
      return [];
    }
    const half = entry.quality?.intervalHalfWidth;
    return [
      {
        label: 'Intelligence Index',
        value: this.formatIndex(entry.quality?.pointEstimate ?? null),
        detail: half == null
          ? 'No interval could be computed for this entry.'
          : `95 % interval ± ${half.toFixed(1)} — ${entry.quality?.intervalBasis ?? ''}`
      },
      {
        label: 'Time to first token, P50',
        value: this.formatMs(entry.speed?.ttftP50Ms ?? null),
        detail: `P90 ${this.formatMs(entry.speed?.ttftP90Ms ?? null)} over ${entry.speed?.ttftAnswerCount ?? 0} answers`
      },
      {
        label: 'Candidate cost per question',
        value: this.formatUsd(entry.cost?.candidateCostPerQuestionUsd ?? null),
        detail: `${this.comparison?.pricingBasisLabel ?? ''} — candidate spend only, grading roles excluded`
      }
    ];
  }

  // ---------------------------------------------------------------------------------------------
  // The figures, as cards
  //
  // Card order is P1, then P2, then the three scatters: P1 answers the primary question and carries
  // real units, so it is where a reader should land. The order is fixed in the template rather than
  // in a control, because a reorder would change which figure a skimming reader trusts.
  // ---------------------------------------------------------------------------------------------

  /** P1 — three linked panels sharing one model order, each with its own axis title and unit. */
  get panelCards(): ComparisonFigureCard[] {
    const panels = this.figures?.smallMultiples;
    if (!panels || !this.showFigures) {
      return [];
    }
    const height = this.panelHeight();
    return [
      this.toCard(panels.quality, height),
      this.toCard(panels.speed, height),
      this.toCard(panels.cost, height)
    ];
  }

  /** P2 — the normalized profile, suppressed below three entries. */
  get profileCard(): ComparisonFigureCard | null {
    return this.figures && this.showProfile ? this.toCard(this.figures.profile, 340) : null;
  }

  /** S1-S3 — the three scatters, which render from two entries upward. */
  get scatterCards(): ComparisonFigureCard[] {
    const figures = this.figures;
    if (!figures || !this.showFigures) {
      return [];
    }
    const height = this.scatterHeight();
    return [
      this.toCard(figures.qualitySpeed, height),
      this.toCard(figures.qualityCost, height),
      this.toCard(figures.speedCost, height)
    ];
  }

  private toCard(
    spec: {
      id: string;
      title: string;
      subtitle: string;
      caption?: string;
      notices: readonly string[];
      preferredCorner?: { x: string; y: string; label: string };
      config: { type: string; data: unknown; options?: unknown };
      plugins: Plugin[];
    },
    heightPx: number
  ): ComparisonFigureCard {
    return {
      id: spec.id,
      title: spec.title,
      subtitle: spec.subtitle,
      caption: spec.caption ?? '',
      notices: spec.notices,
      cornerLabel: spec.preferredCorner
        ? `${spec.preferredCorner.label}: ${spec.preferredCorner.y} ${spec.preferredCorner.x}`
        : '',
      ariaLabel: this.chartAriaLabel(spec),
      type: spec.config.type as ChartType,
      data: spec.config.data as ChartConfiguration['data'],
      options: spec.config.options as ChartConfiguration['options'],
      plugins: spec.plugins,
      heightPx
    };
  }

  // ---------------------------------------------------------------------------------------------
  // Figure export
  //
  // The composited image carries the title, the subtitle, the caption, every notice and a footer
  // naming the suite, the pricing basis, the entry count and the time the comparison was computed.
  // That is the point of exporting through a composer rather than reading the canvas directly: a
  // bare plot pasted into a document would drop exactly the caveats that stop it being misread.
  // ---------------------------------------------------------------------------------------------

  exportFormat: FigureExportFormat = 'png';

  /** Set while an export is running, so a second click cannot interleave two canvas resizes. */
  exporting = false;

  /** The last export's outcome, announced politely: how many files, at what size, and any refusal. */
  exportStatus = '';

  // --- Export resolution ---
  //
  // Presets plus a custom width and height. Every explicit size composes at one layout width and
  // scales, so a 4K export and a Full HD export differ in pixels and not in relative type size;
  // the on-screen preset keeps the previous behaviour of following the rendered figure at 2x.

  readonly exportPresets = FIGURE_EXPORT_PRESETS;
  readonly minExportDimension = FIGURE_EXPORT_MIN_DIMENSION;
  readonly maxExportDimension = FIGURE_EXPORT_MAX_DIMENSION;

  exportResolutionId = 'onscreen';
  customExportWidth = 1920;
  customExportHeight = 1080;

  get isCustomResolution(): boolean {
    return this.exportResolutionId === 'custom';
  }

  /** The chosen preset, or the custom pair clamped into the supported range. */
  get exportResolution(): FigureExportResolution {
    if (!this.isCustomResolution) {
      return this.exportPresets.find(preset => preset.id === this.exportResolutionId)
        ?? this.exportPresets[0];
    }
    return {
      id: 'custom',
      label: 'Custom',
      widthPx: this.clampDimension(this.customExportWidth),
      heightPx: this.clampDimension(this.customExportHeight)
    };
  }

  /** An out-of-range custom size, named. Empty while the current setting is usable. */
  get customResolutionError(): string {
    if (!this.isCustomResolution) {
      return '';
    }
    const bad = [
      this.isUsableDimension(this.customExportWidth) ? '' : 'width',
      this.isUsableDimension(this.customExportHeight) ? '' : 'height'
    ].filter(name => name !== '');
    if (bad.length === 0) {
      return '';
    }
    return `The export ${bad.join(' and ')} must be between ${this.minExportDimension} and ` +
      `${this.maxExportDimension} px.`;
  }

  /** What the current setting will actually write, in the reader's own units. */
  get exportDimensionsLabel(): string {
    const resolution = this.exportResolution;
    if (resolution.widthPx === null || resolution.heightPx === null) {
      return 'Twice each figure’s on-screen size — the width follows the panel it is rendered in.';
    }
    const density = resolution.widthPx / FIGURE_EXPORT_LAYOUT_WIDTH;
    const layoutHeight = Math.round(resolution.heightPx / density);
    return `${resolution.widthPx} × ${resolution.heightPx} px — laid out at ` +
      `${FIGURE_EXPORT_LAYOUT_WIDTH} × ${layoutHeight}, ${this.formatDensity(density)}× density`;
  }

  onExportResolutionChange(value: string): void {
    this.exportResolutionId = value;
  }

  /** Every card currently rendered, in the order the template draws them. */
  get exportableCards(): ComparisonFigureCard[] {
    if (!this.showFigures) {
      return [];
    }
    const profile = this.profileCard;
    return [...this.panelCards, ...(profile ? [profile] : []), ...this.scatterCards];
  }

  get canExport(): boolean {
    return !this.exporting && this.exportableCards.length > 0 && this.customResolutionError === '';
  }

  onExportFormatChange(value: FigureExportFormat): void {
    this.exportFormat = value;
  }

  async downloadFigure(card: ComparisonFigureCard): Promise<void> {
    await this.downloadFigures([card]);
  }

  async downloadAllFigures(): Promise<void> {
    await this.downloadFigures(this.exportableCards);
  }

  /**
   * Writes one file per card, in sequence with a short gap.
   *
   * Sequential rather than parallel, and gapped: several browsers prompt once before allowing a
   * second save from one gesture, and a burst of simultaneous anchor clicks is what triggers the
   * prompt in the first place.
   *
   * A card the target size cannot fit is skipped with its refusal collected rather than aborting
   * the batch, so a partially-refused export names both what it wrote and what it would not.
   */
  private async downloadFigures(cards: readonly ComparisonFigureCard[]): Promise<void> {
    if (this.exporting || cards.length === 0 || this.customResolutionError !== '') {
      return;
    }
    const resolution = this.exportResolution;
    this.exporting = true;
    this.exportStatus = '';
    this.cdr.markForCheck();

    let written = 0;
    let fellBack = false;
    let liveFallback = false;
    let pixels = '';
    const refusals: string[] = [];
    try {
      for (const card of cards) {
        const canvas = this.canvasFor(card);
        if (!canvas) {
          continue;
        }
        const outcome = await this.exportOneFigure(card, canvas, resolution);
        if (outcome.refusal) {
          refusals.push(outcome.refusal);
          continue;
        }
        if (!outcome.result) {
          continue;
        }
        fellBack = fellBack || outcome.result.fellBackToPng;
        liveFallback = liveFallback || outcome.liveFallback;
        pixels = outcome.pixels || pixels;
        saveFigureBlob(outcome.result.blob, figureExportFilename(card.id, outcome.result.format));
        written++;
        if (written < cards.length) {
          await new Promise<void>(resolve => setTimeout(resolve, 250));
        }
      }
      this.exportStatus =
        this.exportSummary(written, cards.length, pixels, fellBack, liveFallback, refusals);
    } catch {
      this.exportStatus = 'The figures could not be exported.';
    } finally {
      this.exporting = false;
      this.cdr.markForCheck();
    }
  }

  /**
   * Encodes one figure at the requested resolution, or refuses it.
   *
   * An explicit size needs a plot box of its own, which the live chart cannot be given without
   * reflowing the visible page, so the plot is rebuilt in a transient offscreen chart. Where that
   * cannot be built the live canvas is composed instead and the status says so: an export never
   * simply fails.
   */
  private async exportOneFigure(
    card: ComparisonFigureCard,
    canvas: HTMLCanvasElement,
    resolution: FigureExportResolution
  ): Promise<{
    result: FigureExportResult | null;
    refusal: string | null;
    liveFallback: boolean;
    pixels: string;
  }> {
    const chrome = this.exportChrome(card);

    // The on-screen preset keeps the live-canvas path untouched: the composition follows the
    // rendered figure, so there is no target box to refuse and no offscreen chart to build.
    if (resolution.widthPx === null || resolution.heightPx === null) {
      return {
        result: await this.encodeFromLiveCanvas(chrome, canvas),
        refusal: null,
        liveFallback: false,
        pixels: ''
      };
    }

    const { layout, refusal } = resolveFigureLayout(chrome, resolution, this.onScreenSizeOf(canvas));
    if (!layout) {
      return { result: null, refusal, liveFallback: false, pixels: '' };
    }

    const plot = await renderPlotOffscreen(
      { type: card.type, data: card.data, options: card.options, plugins: card.plugins },
      layout
    );
    if (!plot) {
      return {
        result: await this.encodeFromLiveCanvas(chrome, canvas),
        refusal: null,
        liveFallback: true,
        pixels: ''
      };
    }

    const composed = composeFigureImage({ ...chrome, canvas: plot, format: this.exportFormat, layout });
    return {
      result: await encodeFigureImage(composed, this.exportFormat),
      refusal: null,
      liveFallback: false,
      pixels: `${layout.pixelWidth} × ${layout.pixelHeight} px`
    };
  }

  /**
   * Re-renders one live chart at twice its device pixels, composes it and encodes it.
   *
   * The previous `devicePixelRatio` is restored and the chart resized again in a `finally`, so a
   * thrown encode cannot strand the on-screen figure at export density.
   */
  private async encodeFromLiveCanvas(
    chrome: FigureExportChrome,
    canvas: HTMLCanvasElement
  ): Promise<FigureExportResult> {
    const chart = this.chartFor(canvas);
    const previousRatio = chart?.options?.devicePixelRatio;
    try {
      if (chart?.options) {
        chart.options.devicePixelRatio = 2;
        chart.resize();
      }
      const composed = composeFigureImage({ ...chrome, canvas, format: this.exportFormat, layout: null });
      return await encodeFigureImage(composed, this.exportFormat);
    } finally {
      if (chart?.options) {
        chart.options.devicePixelRatio = previousRatio;
        chart.resize();
      }
    }
  }

  /** One card's chrome: everything the exported image carries besides the plot itself. */
  private exportChrome(card: ComparisonFigureCard): FigureExportChrome {
    return {
      title: card.title,
      subtitle: card.subtitle,
      caption: card.caption,
      notices: [...card.notices, ...this.setNotices],
      footer: this.exportFooter()
    };
  }

  /** The live canvas's CSS box, which the on-screen layout is measured against. */
  private onScreenSizeOf(canvas: HTMLCanvasElement): { width: number; height: number } {
    return {
      width: canvas.clientWidth > 0 ? canvas.clientWidth : canvas.width,
      height: canvas.clientHeight > 0 ? canvas.clientHeight : canvas.height
    };
  }

  private clampDimension(value: number): number {
    if (!Number.isFinite(value)) {
      return this.minExportDimension;
    }
    return Math.min(this.maxExportDimension, Math.max(this.minExportDimension, Math.round(value)));
  }

  private isUsableDimension(value: number): boolean {
    return Number.isFinite(value)
      && value >= this.minExportDimension
      && value <= this.maxExportDimension;
  }

  /** `2` rather than `2.00`, and `1.33` rather than `1.3333333`. */
  private formatDensity(density: number): string {
    return Number.isInteger(density) ? String(density) : density.toFixed(2);
  }

  /** Suite, pricing basis, entry count and computation time — the provenance of one figure. */
  private exportFooter(): string {
    const dto = this.comparison;
    const suite = dto?.baselineSuiteName || 'Suite not set';
    const basis = dto?.pricingBasisLabel || dto?.pricingBasis || 'Unknown pricing basis';
    const plotted = this.plotted.length;
    const total = this.entries.length;
    const computed = dto?.computedAtUtc ? new Date(dto.computedAtUtc).toLocaleString() : 'unknown time';
    return `${suite} — ${basis} — ${plotted} of ${total} entries charted — computed ${computed}`;
  }

  /**
   * What the export actually did, including everything it would not do.
   *
   * A refused figure is named in full: a batch that silently wrote five of six files reads as a
   * success, and the missing one is exactly the figure whose caveats did not fit.
   */
  private exportSummary(
    written: number,
    requested: number,
    pixels: string,
    fellBack: boolean,
    liveFallback: boolean,
    refusals: readonly string[]
  ): string {
    const parts: string[] = [];
    if (written === 0) {
      parts.push(refusals.length > 0
        ? 'No figure was written at this size.'
        : 'No figure was written: none is currently rendered.');
    } else {
      const noun = written === 1 ? 'figure' : 'figures';
      const shortfall = written < requested ? ` of ${requested}` : '';
      const size = pixels ? ` at ${pixels}` : '';
      parts.push(`${written}${shortfall} ${noun} saved${size}.`);
    }
    if (fellBack) {
      parts.push('This browser cannot encode WebP, so the file was written as PNG.');
    }
    if (liveFallback) {
      parts.push('An offscreen chart could not be built for at least one figure, so it was ' +
        'written at its on-screen size instead.');
    }
    parts.push(...refusals);
    return parts.join(' ');
  }

  /** The canvas belonging to one card, located by the aria-label the card gave it. */
  private canvasFor(card: ComparisonFigureCard): HTMLCanvasElement | null {
    const directive = this.chartDirectives?.find(
      candidate => this.canvasOf(candidate)?.getAttribute('aria-label') === card.ariaLabel
    );
    return directive ? this.canvasOf(directive) : null;
  }

  private chartFor(canvas: HTMLCanvasElement): { options?: any; resize(): void } | null {
    const directive = this.chartDirectives?.find(candidate => this.canvasOf(candidate) === canvas);
    return (directive?.chart as unknown as { options?: any; resize(): void } | undefined) ?? null;
  }

  private canvasOf(directive: BaseChartDirective): HTMLCanvasElement | null {
    return (directive.chart?.canvas as HTMLCanvasElement | undefined) ?? null;
  }

  // ---------------------------------------------------------------------------------------------
  // Notices
  // ---------------------------------------------------------------------------------------------

  /**
   * Set-level notices: the eight-entry cap, the exclusions, whatever the strictness control and the
   * entry selection are withholding, and the two intervals this payload cannot supply.
   *
   * They render once, above the figures, because every one of them is a fact about the whole slice
   * rather than about one chart.
   */
  get setNotices(): string[] {
    const notices = [...(this.figures?.selection.notices ?? [])];

    const withheld = this.strictlyWithheldEntries;
    if (withheld.length > 0) {
      notices.push(
        `Strict comparability is on: ${withheld.length} degraded ` +
        `${withheld.length === 1 ? 'entry is' : 'entries are'} in the table only ` +
        `(${withheld.map(e => e.label).join(', ')}).`
      );
    }

    const deselected = this.deselectedEntries;
    if (deselected.length > 0) {
      notices.push(
        `${deselected.length} ${deselected.length === 1 ? 'entry is' : 'entries are'} deselected and ` +
        `not plotted: ${deselected.map(e => e.label).join(', ')}.`
      );
    }

    const unmeasured = this.entries
      .filter(entry => !entry.excluded)
      .map(entry => ({ entry, missing: unmeasuredAxes(entry) }))
      .filter(row => row.missing.length > 0);
    for (const row of unmeasured) {
      notices.push(
        `${row.entry.label} has no ${row.missing.join(' and ')} figure, so it is absent from those ` +
        'axes rather than drawn at zero.'
      );
    }

    if (this.plotted.some(entry => entry.runCount > 1)) {
      notices.push(
        'Cost bars carry no interval at any R: the comparison endpoint returns a candidate cost ' +
        'without a run-to-run dispersion, so the spread behind a multi-run cost is not available here.'
      );
    }
    if (this.speedMeasure === 'speedIndex') {
      notices.push(
        'Speed Index bars carry no interval: the endpoint returns the mean index without its ' +
        'run-to-run standard deviation.'
      );
    }
    return notices;
  }

  // ---------------------------------------------------------------------------------------------
  // Rendering helpers
  // ---------------------------------------------------------------------------------------------

  /** The identity glyph for an entry, so the table's marker matches the one in every figure. */
  glyph(key: string): IdentityGlyph | null {
    if (!this.figures) {
      return null;
    }
    return this.figures.glyphs.has(key) ? glyphFor(this.figures.glyphs, key) : null;
  }

  /**
   * One sentence naming a canvas and the n behind it.
   *
   * The canvas is a `role="img"` summary and nothing more — the table below carries the values, so
   * this says what the picture shows rather than trying to enumerate it.
   */
  chartAriaLabel(spec: { title: string; subtitle: string } | null | undefined): string {
    if (!spec) {
      return '';
    }
    return `${spec.title}: ${spec.subtitle}. Values for every entry are in the comparison table below.`;
  }

  /** A DOM id and anchor name derived from an entry key, which carries a `run:12` style colon. */
  tipId(prefix: string, key: string): string {
    return `mc-tip-${prefix}-${key.replace(/[^A-Za-z0-9_-]/g, '-')}`;
  }

  stateLabel(entry: BenchmarkModelComparisonEntryDto): string {
    if (entry.excluded) {
      return 'Excluded';
    }
    return entry.state === 'Degraded' ? 'Degraded' : 'Comparable';
  }

  stateClass(entry: BenchmarkModelComparisonEntryDto): string {
    if (entry.excluded) {
      return 'mc-state-excluded';
    }
    return entry.state === 'Degraded' ? 'mc-state-degraded' : 'mc-state-comparable';
  }

  /** Which axes an entry's degradation touches, named rather than left to a colour. */
  degradedAxes(entry: BenchmarkModelComparisonEntryDto): string[] {
    const axes: string[] = [];
    if (entry.speedDegraded) {
      axes.push('speed');
    }
    if (entry.costDegraded) {
      axes.push('cost');
    }
    return axes;
  }

  formatIndex(value: number | null | undefined): string {
    return value == null || !Number.isFinite(value) ? '—' : value.toFixed(1);
  }

  formatMs(value: number | null | undefined): string {
    if (value == null || !Number.isFinite(value)) {
      return '—';
    }
    return value >= 10000 ? `${(value / 1000).toFixed(1)} s` : `${Math.round(value)} ms`;
  }

  formatUsd(value: number | null | undefined): string {
    if (value == null || !Number.isFinite(value)) {
      return '—';
    }
    if (value === 0) {
      return '$0';
    }
    return value < 0.01 ? `$${value.toFixed(4)}` : `$${value.toFixed(2)}`;
  }

  /**
   * The canvas box height, in pixels.
   *
   * Set on the wrapper rather than left to the card, so the plot and its x-axis label band both fit:
   * a box sized to the plot alone clips the axis or produces a nested scrollbar. Horizontal panels
   * grow with the entry count, because eight horizontal bars in a fixed 320 px box are unreadable.
   */
  panelHeight(): number {
    return this.orientation === 'horizontal'
      ? Math.max(260, 140 + 36 * this.plotted.length)
      : 340;
  }

  scatterHeight(): number {
    return 360;
  }

  // ---------------------------------------------------------------------------------------------
  // Internals
  // ---------------------------------------------------------------------------------------------

  /** Excluded first, then degraded, then comparable — the reading order the table's first click wants. */
  private stateOrder(entry: BenchmarkModelComparisonEntryDto): number {
    if (entry.excluded) {
      return 2;
    }
    return entry.state === 'Degraded' ? 1 : 0;
  }

  /**
   * Rebuilds every figure from one entry set, one order and one glyph assignment.
   *
   * The glyph source is the whole payload rather than the current slice, so deselecting a model
   * never repaints the survivors — a reader who has learned a model's hue and shape keeps it.
   */
  private rebuild(): void {
    this.entries = this.comparison?.entries ?? [];
    this.chartEntries = toChartEntries(this.comparison);
    this.context = toChartContext(this.comparison);

    const stateByKey = new Map(this.entries.map(entry => [entry.key, entry.state] as const));
    const input = this.chartEntries.filter(entry => {
      // Excluded entries pass through so the selection counts and names them; the chart core drops
      // them before any figure is built.
      if (entry.excluded) {
        return true;
      }
      if (!this.includedKeys.includes(entry.key)) {
        return false;
      }
      return !(this.strictness === 'comparableOnly' && stateByKey.get(entry.key) === 'Degraded');
    });

    this.figures = buildComparisonFigures(input, {
      context: this.context,
      sort: this.sort,
      speedMeasure: this.speedMeasure,
      costMeasure: this.costMeasure,
      orientation: this.orientation,
      reducedMotion: this.reducedMotion.matches,
      highlightedKey: this.highlightedKey,
      selectedKeys: this.emphasisKeys,
      glyphSource: this.chartEntries
    });

    this.profileAxes = this.figures.selection.plotted.length >= 2
      ? normalizeProfile(this.figures.selection.plotted, {
        context: this.context,
        speedMeasure: this.speedMeasure,
        costMeasure: this.costMeasure
      })
      : null;

    // Every caller of this method changes what the template renders, and several of them are
    // outside change detection: a filter control, the reduced-motion listener, the resize
    // observer. Marking here is what makes the figures, the notices and the table agree.
    this.cdr.markForCheck();
  }

  /**
   * Watches the chart container's own width, not the window's.
   *
   * The same width drives the container queries in the stylesheet and P1's bar orientation, so the
   * panels stack and their bars turn horizontal together; the constant they share is
   * {@link P1_STACK_BREAKPOINT_PX}.
   */
  private observeContainerWidth(): void {
    const host = this.chartsHost?.nativeElement;
    if (!host || typeof ResizeObserver === 'undefined') {
      return;
    }
    this.resizeObserver = new ResizeObserver(entries => {
      const width = entries[0]?.contentRect.width ?? host.clientWidth;
      this.applyContainerWidth(width);
    });
    // No synchronous first measurement: `ResizeObserver` delivers one of its own after this frame,
    // and reading the width here would change the model inside the change-detection pass that is
    // already rendering from it.
    this.resizeObserver.observe(host);
  }

  /** Exposed to the spec, which cannot resize a real container inside a headless fixture. */
  applyContainerWidth(width: number): void {
    const orientation: BarOrientation = width > 0 && width < P1_STACK_BREAKPOINT_PX ? 'horizontal' : 'vertical';
    if (orientation === this.orientation) {
      return;
    }
    this.orientation = orientation;
    this.rebuild();
    // `markForCheck`, not `detectChanges`: the first measurement lands inside `ngAfterViewInit`,
    // where a synchronous re-check would fault on a value the same pass has already read.
    this.cdr.markForCheck();
  }
}
