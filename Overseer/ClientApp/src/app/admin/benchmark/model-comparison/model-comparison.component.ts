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
  SimpleChanges,
  ViewChild,
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
import {
  BenchmarkModelComparisonDto,
  BenchmarkModelComparisonEntryDto,
  BenchmarkModelComparisonPricingBasis,
  toChartContext,
  toChartEntries,
  unmeasuredAxes
} from './model-comparison.models';

/** One selectable suite for the query control. Structural, so any suite DTO with these two fits. */
export interface ModelComparisonSuiteOption {
  readonly id: number;
  readonly name: string;
}

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
 * The component owns no fetching. It renders the response the host hands it and emits the two
 * controls that change what is fetched — the suite and the pricing basis — so the query lives with
 * the host that already owns a suite selection, and this view stays a pure function of one payload.
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
 * Every control that narrows the data sits in one filter row above every figure. A filter inside a
 * chart card would leave the six figures describing different slices of the same set.
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

  /** Suites the query control offers. Empty renders the control disabled rather than absent. */
  @Input() suites: readonly ModelComparisonSuiteOption[] = [];

  /** The suite the current payload was computed over. Server-side: changing it refetches. */
  @Input() suiteId: number | null = null;

  /** The price card every candidate cost is computed from. Server-side: changing it refetches. */
  @Input() pricingBasis: BenchmarkModelComparisonPricingBasis = 'Current';

  @Output() suiteIdChange = new EventEmitter<number | null>();
  @Output() pricingBasisChange = new EventEmitter<BenchmarkModelComparisonPricingBasis>();
  @Output() refresh = new EventEmitter<void>();

  /** The container query root, measured to decide P1's bar orientation. */
  @ViewChild('chartsHost') chartsHost?: ElementRef<HTMLElement>;

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
    this.unsubscribeReducedMotion = this.reducedMotion.subscribe(() => {
      this.rebuild();
      this.cdr.markForCheck();
    });
    this.rebuild();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['comparison']) {
      // A new payload is a new set of models, so the entry selection is re-seeded rather than
      // carried: a key held over from the previous suite would silently plot nothing.
      this.includedKeys = (this.comparison?.entries ?? [])
        .filter(entry => !entry.excluded)
        .map(entry => entry.key);
      this.emphasisKeys = [];
      this.highlightedKey = null;
      this.entryTable.page = 1;
      this.rebuild();
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
  // Query controls — these change what the host fetches
  // ---------------------------------------------------------------------------------------------

  onSuiteChange(value: number | null): void {
    this.suiteId = value == null || !Number.isFinite(value) ? null : value;
    this.suiteIdChange.emit(this.suiteId);
  }

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
