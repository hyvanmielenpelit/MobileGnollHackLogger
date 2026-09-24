import {
  AfterViewChecked,
  AfterViewInit,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  NgZone,
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
import { NG_CHARTS_CONFIGURATION } from 'ng2-charts';
import { Chart, defaults as chartDefaults } from 'chart.js';
import type { ChartConfiguration, ChartType, Plugin } from 'chart.js';
import type { Subscription } from 'rxjs';

import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import {
  FigureChrome,
  FigureFooter,
  FigureNote,
  figureSummary,
  formatComputedAt
} from './figure-chrome';
import {
  DEFAULT_FIGURE_RESOLUTION_ID,
  FigureSizeSettings,
  defaultFigureSize,
  readStoredFigureSize,
  sameFigureSize,
  writeStoredFigureSize
} from './figure-size';
import { DEFAULT_FIGURE_STYLE, FigureStyle, normalizeFigureStyle } from './figure-style';
import { FigureStylePanelComponent, FigureStylePanelKind } from './figure-style-panel.component';
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
  buildNumberSamples,
  formatQuestionsAsked,
  glyphFor,
  normalizeProfile
} from './model-comparison-charts';
import type { NumberSamples } from './measure-format';
import { MAX_COMPARISON_SOURCES } from './comparison-source-picker.component';
import {
  BenchmarkModelComparisonDto,
  BenchmarkModelComparisonEntryDto,
  BenchmarkModelComparisonPricingBasis,
  ComparisonSelectedSource,
  ComparisonSelectionNotice,
  orderedNotices,
  questionCoverageNotes,
  sourceLabel,
  toChartContext,
  toChartEntries,
  unmeasuredAxes
} from './model-comparison.models';
import {
  DEFAULT_WEBP_QUALITY,
  FIGURE_EXPORT_DENSITY_PRESETS,
  FIGURE_EXPORT_MAX_DENSITY_PERCENT,
  FIGURE_EXPORT_MAX_DIMENSION,
  FIGURE_EXPORT_MAX_TEXT_SCALE_PERCENT,
  FIGURE_EXPORT_MIN_DENSITY_PERCENT,
  FIGURE_EXPORT_MIN_DIMENSION,
  FIGURE_EXPORT_MIN_TEXT_SCALE_PERCENT,
  FIGURE_EXPORT_PRESETS,
  FIGURE_EXPORT_PRESET_GROUPS,
  FigureArchiveEntry,
  FigureExportFormat,
  FigureExportLayout,
  FigureExportRequest,
  FigureExportResolution,
  FigureExportResult,
  PreviewStage,
  WEBP_QUALITY_OPTIONS,
  WebpQuality,
  aspectRatioLabel,
  bitmapRefusal,
  buildFigureArchive,
  composeFigureImage,
  copyImageToClipboard,
  densityPercentLabel,
  displayDensity,
  encodeFigureImage,
  figureArchiveFilename,
  figureExportFilename,
  layoutBoxFor,
  previewLayoutFor,
  renderPlotOffscreen,
  resolveFigureLayout,
  saveFigureBlob
} from './figure-export';
import {
  PREVIEW_SLIDER_STEPS,
  PreviewViewRequest,
  PreviewZoomRange,
  anchoredScrollDelta,
  canZoomPreviewIn,
  canZoomPreviewOut,
  clampPreviewZoom,
  fitHeightZoom,
  formatPreviewZoom,
  nextPreviewZoomStop,
  previewRasterZoom,
  previewZoomRange,
  previousPreviewZoomStop,
  resolvePreviewZoom,
  sliderToZoom,
  wheelZoomFactor,
  zoomToSlider
} from './preview-view';
import { ProviderBadgeComponent } from '../../../shared/provider-badge/provider-badge.component';
import { ToastComponent, ToastNotice } from '../../../shared/toast/toast.component';
import {
  COMPARISON_TABLE_COLUMNS,
  ComparisonTableProvenance,
  TableExportFormat,
  buildComparisonTableModel,
  comparisonStateLabel,
  encodeComparisonTable,
  formatIndexText,
  formatMsText,
  formatUsdText,
  populatedColumnKeys,
  tableExportFilename,
  toMarkdown
} from './table-export';

/**
 * Which wizard step is on screen. Four, in a fixed order: sources, then filters, then the table,
 * then the figures.
 *
 * The table comes before the figures deliberately. It is the artefact that carries the numbers, so
 * a reader who steps through in order meets the record before the pictures drawn from it.
 */
export type ComparisonWizardStep = 1 | 2 | 3 | 4;

/**
 * Every wizard step with its title and a one-line summary of what it is for.
 *
 * Exported so the launcher lists the same steps under the same names as the wizard's own stepper.
 */
export const COMPARISON_WIZARD_STEPS: readonly {
  readonly step: ComparisonWizardStep; readonly title: string; readonly summary: string;
}[] = [
  { step: 1, title: 'Sources', summary: 'Choose the runs or groups to compare.' },
  { step: 2, title: 'Comparability & filters', summary: 'See which of them can be charted together.' },
  { step: 3, title: 'Table', summary: 'Read the results side by side.' },
  { step: 4, title: 'Figures', summary: 'View the charts and export them.' }
];

/** How long a comparison may run before the footer offers the ways out. */
const SLOW_COMPARISON_MS = 15_000;

/** Where the figure style is kept, per browser. Read and written in `try/catch`; never required. */
export const FIGURE_STYLE_STORAGE_KEY = 'overseer.modelComparison.figureStyle';

/**
 * Step 4's two views of the figures, both composed by the export pipeline: every figure, or one
 * figure with zoom and pan.
 */
export type FigureViewTab = 'all' | 'single';

/** The figure settings sidebar's three tabs, in order. */
export type FigureSidebarTab = 'emphasis' | 'style' | 'download';

/**
 * Where the sidebar's collapsed state and tab, the figure view and whether the Style tab's Figure
 * size section is open are kept, per browser, as `{ version: 1, collapsed, tab, view,
 * figureSizeOpen }`. Read and written in `try/catch`; never required.
 */
export const FIGURE_SIDEBAR_STORAGE_KEY = 'overseer.modelComparison.figureSidebar';

const FIGURE_SIDEBAR_TABS: readonly FigureSidebarTab[] = ['emphasis', 'style', 'download'];

/** The stored workspace layout, field by field. */
interface StoredFigureSidebar {
  readonly collapsed: boolean;
  readonly tab: FigureSidebarTab;
  readonly view: FigureViewTab;
  readonly figureSizeOpen: boolean;
}

/** The stored sidebar state, field by field; the default wherever storage is absent or unreadable. */
function readStoredFigureSidebar(): StoredFigureSidebar {
  const fallback: StoredFigureSidebar = { collapsed: false, tab: 'emphasis', view: 'all', figureSizeOpen: true };
  try {
    const raw = localStorage.getItem(FIGURE_SIDEBAR_STORAGE_KEY);
    const stored: unknown = raw === null ? null : JSON.parse(raw);
    if (stored === null || typeof stored !== 'object') {
      return fallback;
    }
    const { collapsed, tab: storedTab, view: storedView, figureSizeOpen } =
      stored as { collapsed?: unknown; tab?: unknown; view?: unknown; figureSizeOpen?: unknown };
    // 'export' is the Download tab's earlier stored name.
    const tab = storedTab === 'export' ? 'download' : storedTab;
    // 'charts' and 'preview' are the All and Single views' earlier stored names.
    const view = storedView === 'charts' ? 'all' : storedView === 'preview' ? 'single' : storedView;
    return {
      collapsed: typeof collapsed === 'boolean' ? collapsed : fallback.collapsed,
      tab: FIGURE_SIDEBAR_TABS.includes(tab as FigureSidebarTab) ? tab as FigureSidebarTab : fallback.tab,
      view: view === 'all' || view === 'single' ? view : fallback.view,
      figureSizeOpen: typeof figureSizeOpen === 'boolean' ? figureSizeOpen : fallback.figureSizeOpen
    };
  } catch {
    return fallback;
  }
}

/** The All tab's scroller padding, in CSS px; `.mc-all-viewport` in the stylesheet matches it. */
const ALL_VIEWPORT_PADDING = 16;

/** The All tab's gap between tiles, in CSS px; `.mc-all-grid` matches it. */
const ALL_TILE_GAP = 16;

/** How long the All tab waits for changes to stop before it re-composes its tiles. */
const ALL_COMPOSE_QUIET_MS = 120;

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

/** One plotted model in the emphasis selector: the name it is drawn under, and its thinking level. */
export interface EmphasisOption {
  readonly key: string;
  readonly name: string;
  readonly thinkingLevel: string | null;
}

/** One statistic in the single-entry KPI row, where a chart would be one bar and say nothing. */
export interface ComparisonStatTile {
  readonly label: string;
  readonly value: string;
  readonly detail: string;
}

/**
 * One figure ready to compose: its chart.js inputs beside the chrome the export composer draws.
 *
 * The chart core types each figure by its own chart type and datum shape, which is what makes its
 * builders type-safe; the views compose them in one loop, so the card widens them back to the
 * offscreen renderer's erased inputs. The chrome stays structured data rather than a chart.js
 * plugin, so the composer lays it out and the tile's accessible name summarises it.
 */
export interface ComparisonFigureCard {
  readonly id: string;
  readonly title: string;
  readonly chrome: FigureChrome;
  readonly ariaLabel: string;
  readonly type: ChartType;
  readonly data: ChartConfiguration['data'];
  readonly options: ChartConfiguration['options'];
  readonly plugins: Plugin[];
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
 * 1. **The table has a step of its own, is never behind a toggle, and opens before the figures
 *    do.** A chart is far more persuasive than a table, and a reader will trust six figures
 *    without checking twenty-three comparability keys. The canvases are `role="img"` summaries;
 *    the table is the artefact that carries the numbers, the states and the differing keys, and
 *    its step opens over a set no figure can draw.
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
  imports: [CommonModule, FormsModule, SortHeaderComponent, TablePagerComponent, ProviderBadgeComponent, ToastComponent, FigureStylePanelComponent],
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

  /** How many single runs the host currently has selected. */
  @Input() selectedRunCount = 0;

  /** How many analysis groups the host currently has selected. */
  @Input() selectedGroupCount = 0;

  /**
   * What the host has to say about the current selection: what cannot be charted, what will be
   * excluded, and what is still being computed. Empty while the selection is unremarkable.
   */
  @Input() selectionNotices: readonly ComparisonSelectionNotice[] = [];

  /** Every source the host has selected, named for the selection band's chips. */
  @Input() selectedSources: readonly ComparisonSelectedSource[] = [];

  /** One chip's remove button, emitted for the host to drop from its selection. */
  @Output() removeSource = new EventEmitter<ComparisonSelectedSource>();

  /** The selection band's Clear selection button, emitted for the host to drop every source at once. */
  @Output() clearSelection = new EventEmitter<void>();

  /** The two counts together, which is what both caps and Next are judged on. */
  get selectedSourceCount(): number {
    return this.selectedRunCount + this.selectedGroupCount;
  }

  /** The request cap. Above it Compare is refused rather than truncated. */
  @Input() maxSources = MAX_COMPARISON_SOURCES;

  /**
   * Compare, emitted by the wizard footer rather than by the picker.
   *
   * Two Compare affordances on one screen would disagree the moment one of them was disabled, so
   * the picker offers none and this is the only one.
   */
  @Output() compare = new EventEmitter<void>();

  /** Abandons the comparison in flight. Emitted only on step 1 while `comparing`. */
  @Output() cancelCompare = new EventEmitter<void>();

  /** The last step's Close, and the header's close control. The host owns the dialog element. */
  @Output() closeRequested = new EventEmitter<void>();

  /** Focused by the host after showModal(), which would otherwise focus the close button. */
  @ViewChild('wizardHeading') wizardHeading?: ElementRef<HTMLElement>;

  /** The footer's Compare / Next / Close button, which takes focus when Cancel Comparison goes away. */
  @ViewChild('nextButton') nextButton?: ElementRef<HTMLButtonElement>;

  @ViewChild('cancelCompareButton') cancelCompareButton?: ElementRef<HTMLButtonElement>;

  /** The request has run past `SLOW_COMPARISON_MS`, and the footer says how to leave it. */
  slowLoading = false;

  private slowLoadingTimer: ReturnType<typeof setTimeout> | null = null;

  /** Set when the result lands while Cancel Comparison has focus; consumed after the next render. */
  private restoreFocusToNext = false;

  /** The figure views' box, measured to decide P1's bar orientation. */
  @ViewChild('chartsHost') chartsHost?: ElementRef<HTMLElement>;

  /**
   * The chart types the figures are plotted with, from the application's `provideCharts`.
   *
   * No `BaseChartDirective` renders on this view — every figure is plotted offscreen — so the
   * registration that directive performs on construction is performed here instead.
   */
  private readonly chartsConfig = inject(NG_CHARTS_CONFIGURATION, { optional: true });

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
   * Mean model time per question by default, not time to first token and not Speed Index.
   *
   * Model time is turn duration with tool I/O subtracted out — the figure the scoring profile
   * targets and the one a candidate's own speed is judged on. Time to first token stays offered
   * because it is the latency a chat user actually perceives, which model time does not capture.
   * Speed Index saturates — half the scored answers finish inside their difficulty-scaled target,
   * so several models sit at the ceiling and read as equally fast when their real latency differs
   * severalfold — and the server classes it as a table figure for that reason; it stays offered
   * because the index is what the run report scores on, and selecting it raises the saturation
   * notice on the panel.
   */
  speedMeasure: SpeedMeasure = 'meanModelTime';

  /**
   * Candidate cost for the whole suite. There is no second measure to switch to: the endpoint's cost
   * object is candidate-only by design, so no run total including grading roles exists to plot, and
   * the control shows that option disabled rather than omitting it silently.
   */
  costMeasure: CostMeasure = 'candidateSuite';

  /**
   * Names every scatter mark on the canvas instead of in the legend below it.
   *
   * Off by default: a reader who wants the names on the marks asks for them, and the plugin behind
   * it places them without collisions — which the automatic rule this replaced, direct-labelling
   * at four or more models from a fixed offset, never did.
   */
  scatterDirectLabels = false;

  /** Draws each scatter mark's two measured values beside it, so an exported figure states them. */
  scatterInlineValues = true;

  /** Bar and trade-off styling, applied to the page and to every export alike. */
  figureStyle: FigureStyle = DEFAULT_FIGURE_STYLE;

  /** Pending rebuild after a style change, so a range drag rebuilds once it pauses. */
  private styleTimer: ReturnType<typeof setTimeout> | null = null;

  /** The model under the pointer or the keyboard, highlighted in every panel and in the profile. */
  highlightedKey: string | null = null;

  /** Models pinned into the accent, so a comparison survives the pointer leaving the row. */
  emphasisKeys: string[] = [];

  // --- Derived render state ---

  entries: readonly BenchmarkModelComparisonEntryDto[] = [];
  figures: ComparisonFigureSet | null = null;
  context: ModelComparisonContext = {
    scoredItemsMin: 0, scoredItemsMax: 0, examItemCount: 0, questionsAskedPerRun: null,
    pricingBasisLabel: '', pricingBasis: '', pricedOn: '', suiteName: ''
  };
  orientation: BarOrientation = 'vertical';

  /** The profile's axis endpoints, printed beside P2 so its normalized heights stay anchored. */
  profileAxes: ProfileNormalization | null = null;

  /** What each family's Number format options preview on; replaced only by a rebuild. */
  numberSamples: Readonly<Record<FigureStylePanelKind, NumberSamples>> = { bar: {}, scatter: {}, profile: {} };

  /** The hard ceiling the chart core enforces; the picker names it so the cap is never a surprise. */
  readonly maxPlottedEntries = MAX_PLOTTED_ENTRIES;

  private chartEntries: readonly ModelComparisonEntry[] = [];
  private readonly reducedMotion = new ReducedMotionWatcher();
  private unsubscribeReducedMotion: (() => void) | null = null;
  private resizeObserver: ResizeObserver | null = null;

  /** The element `resizeObserver` watches; a new one, after step 4 is rendered again, is re-observed. */
  private observedChartsHost: HTMLElement | null = null;

  /**
   * Sort, filter and page state for the table view.
   *
   * State sorts on a rank rather than the label so the first click puts the entries a reader has to
   * check — excluded, then degraded — at the top instead of ordering them alphabetically.
   *
   * The three timings share one column and one sort key, the mean model time per question: it is the
   * figure the scoring profile targets, and the suite total is that mean multiplied by a constant
   * item count. Sorting on the suite total or on TTFT P50 is available in the exported table, which
   * carries every timing as a column of its own.
   */
  readonly entryTable = new TableState<BenchmarkModelComparisonEntryDto>('state', 'desc').registerAccessors(
    {
      label: e => e.label,
      runCount: e => e.runCount,
      state: e => this.stateOrder(e),
      intelligenceIndex: e => e.quality?.pointEstimate ?? null,
      modelTimeMeanMs: e => e.speed?.modelTimeMeanMs ?? null,
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
    if (this.chartsConfig?.registerables) {
      Chart.register(...this.chartsConfig.registerables);
    }
    if (this.chartsConfig?.defaults) {
      chartDefaults.set(this.chartsConfig.defaults);
    }
    this.figureStyle = this.readStoredFigureStyle();
    this.unsubscribeReducedMotion = this.reducedMotion.subscribe(() => this.rebuild());
    this.rebuild();
  }

  ngOnChanges(changes: SimpleChanges): void {
    const loadingChange = changes['loading'];
    if (loadingChange) {
      this.onLoadingChange(!!loadingChange.previousValue, this.loading);
    }

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
    if (this.chartsHost) {
      this.observeContainerWidth();
    }
    // The panel renders behind the host's @if, so the polyfill's first scan never saw these anchors.
    refreshAnchorPositioning();
  }

  /**
   * The figure views' box lives inside the figure branch, so it does not exist on the pass that
   * runs `ngAfterViewInit` for a set that starts empty or incomparable, and it is a new element each
   * time step 4 is rendered again. The observer is attached whenever the element it watches is not
   * the one on the page.
   *
   * Both views can leave the DOM or come back without a tab click — a refetch takes the figures
   * away or returns them, and leaving step 4 removes them. Leaving tears down only unbound state,
   * since a bound field changed inside this hook faults the check; returning re-attaches outside the
   * check pass.
   */
  ngAfterViewChecked(): void {
    const host = this.chartsHost?.nativeElement ?? null;
    if (host !== null && host !== this.observedChartsHost) {
      this.observeContainerWidth();
    }
    if (this.restoreFocusToNext) {
      this.restoreFocusToNext = false;
      this.nextButton?.nativeElement.focus();
    }
    if (this.previewActive && !this.previewStage) {
      this.cancelScheduledPreview();
      this.disconnectStageObserver();
      this.previewActive = false;
      this.previewSeq++;
    } else if (this.figureTab === 'single' && this.previewStage && !this.previewActive && !this.previewAttachQueued) {
      this.previewAttachQueued = true;
      queueMicrotask(() => {
        this.previewAttachQueued = false;
        if (this.figureTab === 'single' && this.previewStage && !this.previewActive) {
          this.attachPreview();
          this.cdr.markForCheck();
        }
      });
    }
    if (this.allActive && !this.allViewport) {
      this.teardownAll();
    } else if (this.figureTab === 'all' && this.allViewport && !this.allActive && !this.allAttachQueued) {
      this.allAttachQueued = true;
      queueMicrotask(() => {
        this.allAttachQueued = false;
        if (this.figureTab === 'all' && this.allViewport && !this.allActive) {
          this.attachAll();
          this.cdr.markForCheck();
        }
      });
    }
  }

  /** A re-attach is waiting for its microtask, so later checks in the same turn do not queue another. */
  private previewAttachQueued = false;

  ngOnDestroy(): void {
    this.clearSlowLoadingTimer();
    this.unsubscribeReducedMotion?.();
    this.reducedMotion.dispose();
    this.resizeObserver?.disconnect();
    this.resizeObserver = null;
    this.observedChartsHost = null;
    this.disconnectStageObserver();
    this.cancelScheduledPreview();
    this.cancelScheduledStyle();
    this.teardownAll();
  }

  // ---------------------------------------------------------------------------------------------
  // The wizard
  //
  // Four steps in a fixed order, with the step header as a tablist and Previous / Next as the
  // primary traversal. Next is enabled only when the current step's selection is valid, and where
  // it is not, the reason is rendered as text beside it rather than left to a disabled button.
  // ---------------------------------------------------------------------------------------------

  step: ComparisonWizardStep = 1;

  readonly steps: readonly ComparisonWizardStep[] = [1, 2, 3, 4];

  readonly stepTitles = Object.fromEntries(
    COMPARISON_WIZARD_STEPS.map(entry => [entry.step, entry.title])
  ) as Record<ComparisonWizardStep, string>;

  /**
   * Steps 2 and 3 need a computed comparison; step 4 additionally needs something chartable.
   *
   * The table step is reachable over a set no figure can draw, which is the point of it: an
   * incomparable set still has measures, states and differing keys to read, and the step that
   * carries them must not close behind the same gate as the pictures.
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
    return step === 2 || step === 3 || this.showFigures;
  }

  get canGoPrevious(): boolean {
    return this.step > 1;
  }

  get canGoNext(): boolean {
    if (this.step === 1) {
      return !this.loading && this.selectedSourceCount > 0 && this.selectedSourceCount <= this.maxSources;
    }
    if (this.step === 2) {
      return this.comparison !== null;
    }
    if (this.step === 3) {
      return this.showFigures;
    }
    return true;
  }

  /**
   * A comparison is being computed for the selection on step 1.
   *
   * Step-scoped rather than the bare `loading` flag: a pricing-basis refetch from step 2 loads too,
   * and the footer button there is Next, which the request does not block.
   */
  get comparing(): boolean {
    return this.loading && this.step === 1;
  }

  /** Compare while the current selection has no computed comparison; Next once it does. */
  get nextLabel(): string {
    if (this.step === 4) {
      return 'Close';
    }
    if (this.comparing) {
      return 'Comparing…';
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
        const computing = 'Computing the comparison — pricing every entry server-side.';
        return this.slowLoading
          ? computing + ' This is taking longer than usual: cancel it, or close the wizard — the ' +
            'comparison keeps computing and is here when you reopen it.'
          : computing;
      }
      if (this.selectedSourceCount === 0) {
        return 'Select at least one run or analysis group.';
      }
      return `${this.selectedSourceCount} sources selected — at most ${this.maxSources} may be ` +
        'compared in one request. A comparison over every stored run is a slow query and an ' +
        'unreadable figure.';
    }
    // Step 2 has no blocked state once a comparison exists, and step 4 is the last one, so what is
    // left is step 3 refusing to open the figures: the shape of the set is the reason.
    return this.shape === 'single'
      ? 'Only one entry is plotted; a comparison needs two.'
      : 'Nothing in this set may be charted together.';
  }

  /**
   * The busy line of a refetch from steps 2–4, where Next is not blocked and carries no spinner.
   * Empty on step 1, whose busy state is the footer button and `nextBlockedReason`.
   */
  get refetchStatus(): string {
    if (!this.loading || this.comparing) {
      return '';
    }
    const recomputing = 'Recomputing the comparison — pricing every entry server-side.';
    return this.slowLoading
      ? recomputing + ' This is taking longer than usual; you can close the wizard and come back.'
      : recomputing;
  }

  /**
   * Cancel Comparison. The host drops `loading` synchronously, which removes this button while it
   * holds focus, so focus moves to Next rather than falling to the body of the modal.
   */
  onCancelCompare(): void {
    this.cancelCompare.emit();
    this.nextButton?.nativeElement.focus();
  }

  private onLoadingChange(wasLoading: boolean, isLoading: boolean): void {
    if (isLoading && !wasLoading) {
      this.clearSlowLoadingTimer();
      this.slowLoading = false;
      this.slowLoadingTimer = setTimeout(() => {
        this.slowLoadingTimer = null;
        this.slowLoading = true;
        this.cdr.markForCheck();
      }, SLOW_COMPARISON_MS);
    } else if (!isLoading && wasLoading) {
      this.clearSlowLoadingTimer();
      this.slowLoading = false;
      const cancel = this.cancelCompareButton?.nativeElement;
      if (cancel && document.activeElement === cancel) {
        this.restoreFocusToNext = true;
      }
    }
  }

  private clearSlowLoadingTimer(): void {
    if (this.slowLoadingTimer !== null) {
      clearTimeout(this.slowLoadingTimer);
      this.slowLoadingTimer = null;
    }
  }

  /**
   * The band's headline count, in words, from the two selection counts rather than the chip
   * array — it does not mention the request cap; `nextBlockedReason` already does.
   */
  get selectionSummary(): string {
    const runs = this.selectedRunCount;
    const groups = this.selectedGroupCount;
    if (runs === 0 && groups === 0) {
      return 'Nothing selected yet';
    }
    const parts = [
      runs > 0 ? `${runs} ${runs === 1 ? 'run' : 'runs'}` : '',
      groups > 0 ? `${groups} ${groups === 1 ? 'group' : 'groups'}` : ''
    ].filter(part => part !== '');
    return `${parts.join(' and ')} selected`;
  }

  /** One chip's tooltip anchor id, for its remove button's `interestfor` / `position-anchor` pair. */
  sourceChipId(source: ComparisonSelectedSource): string {
    return `mc-sel-${source.kind}-${source.id}`;
  }

  /**
   * That the selection exceeds what the figures draw, or null below the cap.
   *
   * Derived rather than passed in: the plot cap is the constant this component already charts by,
   * and the selection size already arrives for Next to gate on. Silent above the request cap,
   * where the comparison is refused outright and the plot cap is no longer the reader's problem.
   */
  get plotCapNotice(): ComparisonSelectionNotice | null {
    if (this.selectedSourceCount <= this.maxPlottedEntries || this.selectedSourceCount > this.maxSources) {
      return null;
    }
    return {
      id: 'plot-cap',
      severity: 'info',
      heading: 'More sources selected than the figures plot',
      body: `${this.selectedSourceCount} sources selected — the figures plot at most `
        + `${this.maxPlottedEntries}. The rest stay in the comparison table with their measures, and `
        + 'the view names which were left out.'
    };
  }

  /**
   * That nothing is ticked yet, or null once something is.
   *
   * The band explains; the footer's `nextBlockedReason` names the control. Silent while a
   * comparison is being computed, where an empty selection is a transient state of the request
   * rather than something the reader has to act on.
   */
  get nothingSelectedNotice(): ComparisonSelectionNotice | null {
    if (this.loading || this.selectedSourceCount > 0) {
      return null;
    }
    return {
      id: 'nothing-selected',
      severity: 'warning',
      heading: 'Nothing is selected yet',
      body: 'Tick at least one completed run or analysis group in the tables above. Compare stays '
        + 'unavailable until you do.'
    };
  }

  /**
   * Everything the band renders: the host's index-derived notices plus this component's own
   * empty-selection and plot-cap notices, re-ordered so nothing that blocks a figure sits below
   * something that only shrinks one.
   */
  get bandNotices(): ComparisonSelectionNotice[] {
    const own = [this.nothingSelectedNotice, this.plotCapNotice]
      .filter((notice): notice is ComparisonSelectionNotice => notice !== null);
    return orderedNotices(own.length === 0 ? this.selectionNotices : [...this.selectionNotices, ...own]);
  }

  goToStep(step: ComparisonWizardStep): void {
    if (!this.isStepReachable(step)) {
      return;
    }
    if (this.step === 4 && step !== 4) {
      // While the views still exist. `figureTab` is kept, so returning re-attaches the one shown.
      this.detachPreview();
      this.detachAll();
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
    if (this.step === 4) {
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
   * sources are: every later step has nothing to render without one.
   */
  private applyComparisonToStep(previous: BenchmarkModelComparisonDto | null | undefined): void {
    const next: ComparisonWizardStep = this.comparison === null ? 1 : !previous ? 2 : this.step;
    if (this.step === 4 && next !== 4) {
      this.detachPreview();
      this.detachAll();
    }
    this.step = next;
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

  /**
   * Adds or removes one entry from the plotted set.
   *
   * Ticking more entries than the figures plot is allowed: the chart core takes the first
   * {@link MAX_PLOTTED_ENTRIES} and names the rest in an overflow notice, which is also the state
   * a fresh payload seeds, so a refusal here would leave that state unreachable once left.
   */
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

  /**
   * Both scatter toggles live in the sidebar's Style tab and, through `rebuild`, re-compose
   * whichever figure view is shown.
   */
  onScatterDirectLabelsChange(on: boolean): void {
    this.scatterDirectLabels = on;
    this.rebuild();
  }

  onScatterInlineValuesChange(on: boolean): void {
    this.scatterInlineValues = on;
    this.rebuild();
  }

  /**
   * Stores the style at once, so the panel's own controls follow it, persists it, and rebuilds the
   * six figures once the change pauses: a range drag fires on every step.
   */
  onFigureStyleChange(style: FigureStyle): void {
    this.figureStyle = normalizeFigureStyle(style);
    this.writeStoredFigureStyle(this.figureStyle);
    this.cancelScheduledStyle();
    this.styleTimer = setTimeout(() => {
      this.styleTimer = null;
      this.rebuild();
    }, this.previewDebounceMs);
    this.cdr.markForCheck();
  }

  /** The forced orientation, or the container-driven one while the style says Automatic. */
  get effectiveOrientation(): BarOrientation {
    const choice = this.figureStyle.bar.orientation;
    return choice === 'auto' ? this.orientation : choice;
  }

  private cancelScheduledStyle(): void {
    if (this.styleTimer !== null) {
      clearTimeout(this.styleTimer);
      this.styleTimer = null;
    }
  }

  /** The stored style, repaired field by field; the default wherever storage is absent or unreadable. */
  private readStoredFigureStyle(): FigureStyle {
    try {
      const raw = localStorage.getItem(FIGURE_STYLE_STORAGE_KEY);
      return raw === null ? DEFAULT_FIGURE_STYLE : normalizeFigureStyle(JSON.parse(raw));
    } catch {
      return DEFAULT_FIGURE_STYLE;
    }
  }

  private writeStoredFigureStyle(style: FigureStyle): void {
    try {
      localStorage.setItem(FIGURE_STYLE_STORAGE_KEY, JSON.stringify({ version: 1, ...style }));
    } catch {
      // Private mode or blocked storage: the style still applies for this session.
    }
  }

  /**
   * Hover and keyboard focus on an Emphasis entry light the same model in all three panels and in
   * the profile; on the All tab the tiles re-compose once the pointer settles.
   *
   * Refused on the Single tab, where a transient hover would be composed into the figure about to
   * be exported and recompose on every pass of the pointer. Clearing is always accepted.
   */
  setHighlight(key: string | null): void {
    if (this.highlightedKey === key || (key !== null && this.figureTab === 'single')) {
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

  /** Drops every pin at once, which restores the plain figures. */
  clearEmphasis(): void {
    this.emphasisKeys = [];
    this.rebuild();
  }

  /**
   * The plotted entries as the emphasis selector names them.
   *
   * The chart entry carries only the axis label, so the display name and the thinking level are
   * read off the payload beside it — the same two facts the comparison table's model cell shows.
   */
  get emphasisOptions(): EmphasisOption[] {
    return this.plotted.map(entry => {
      const dto = this.entries.find(candidate => candidate.key === entry.key);
      return {
        key: entry.key,
        name: dto?.modelDisplayName || entry.label,
        thinkingLevel: dto?.thinkingLevel ?? null
      };
    });
  }

  /** `Run 48` or `Analysis group 3` — the run line under a model name in the table. */
  sourceLabel(entry: { sourceKind: string; sourceId: number }): string {
    return sourceLabel(entry);
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

  /** Entries that carry measures: every entry that is not excluded. */
  get measuredEntryCount(): number {
    return this.entries.filter(entry => !entry.excluded).length;
  }

  /** Measured entries whose Speed Index sits at the ceiling. */
  get speedIndexSaturatedCount(): number {
    return this.entries.filter(entry => !entry.excluded && entry.table?.speedIndexSaturated).length;
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
          + (entry.cost?.questionsAskedPerRun != null
            ? ` — over ${formatQuestionsAsked(entry.cost.questionsAskedPerRun)} asked per run`
            : '')
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
    return [this.toCard(panels.quality), this.toCard(panels.speed), this.toCard(panels.cost)];
  }

  /** P2 — the normalized profile, suppressed below three entries. */
  get profileCard(): ComparisonFigureCard | null {
    return this.figures && this.showProfile ? this.toCard(this.figures.profile) : null;
  }

  /** S1-S3 — the three scatters, which render from two entries upward. */
  get scatterCards(): ComparisonFigureCard[] {
    const figures = this.figures;
    if (!figures || !this.showFigures) {
      return [];
    }
    return [this.toCard(figures.qualitySpeed), this.toCard(figures.qualityCost), this.toCard(figures.speedCost)];
  }

  private toCard(spec: {
    id: string;
    title: string;
    chrome: FigureChrome;
    config: { type: string; data: unknown; options?: unknown };
    plugins: Plugin[];
  }): ComparisonFigureCard {
    return {
      id: spec.id,
      title: spec.title,
      chrome: spec.chrome,
      ariaLabel: this.chartAriaLabel(spec),
      type: spec.config.type as ChartType,
      data: spec.config.data as ChartConfiguration['data'],
      options: spec.config.options as ChartConfiguration['options'],
      plugins: spec.plugins
    };
  }

  // ---------------------------------------------------------------------------------------------
  // Figure export
  //
  // The composited image carries the card's chrome — title, badges, direction, detail, key, highlight and
  // notes — and a footer naming the suite and the time the comparison was computed. That is the
  // point of exporting through a composer rather than reading the canvas directly: a bare plot
  // pasted into a document would drop exactly the caveats that stop it being misread.
  // ---------------------------------------------------------------------------------------------

  exportFormat: FigureExportFormat = 'png';

  /** The qualities a WebP export may be written at, offered whenever WebP is the chosen format. */
  readonly webpQualityOptions = WEBP_QUALITY_OPTIONS;

  /**
   * WebP quality for the figures and for the table, held separately.
   *
   * Two settings rather than one: the two exports already choose their formats independently, and a
   * shared quality would make a figure's setting silently rewrite the table's.
   */
  figureWebpQuality: WebpQuality = DEFAULT_WEBP_QUALITY;

  tableWebpQuality: WebpQuality = DEFAULT_WEBP_QUALITY;

  /**
   * Set while any export is running — figure download, table download, either clipboard copy.
   *
   * One flag rather than one per path, because it is what the host's close guard reads: a dialog
   * torn down mid-export leaves a detached chart and a half-written file whichever of the four
   * started it.
   */
  exporting = false;

  /** The last export's outcome, shown as a toast: how many files, at what size, and any refusal. */
  exportNotice: ToastNotice | null = null;

  private exportNoticeSerial = 0;

  /** The outcome's text alone, for the specs and for anything that only needs the words. */
  get exportStatus(): string {
    return this.exportNotice?.message ?? '';
  }

  private announce(message: string, kind: 'success' | 'error'): void {
    this.exportNotice = { id: ++this.exportNoticeSerial, kind, message };
  }

  clearExportNotice(): void {
    this.exportNotice = null;
  }

  // --- Figure size ---
  //
  // Presets plus a custom width and height, a pixel density and a text size, held as one
  // `FigureSizeSettings` in the Style tab. They shape every figure: the All tab, the Single tab and
  // every download compose at them. Every size composes at one layout width and scales, so a 4K
  // figure and a Full HD figure differ in pixels and not in relative type size; the density then
  // multiplies the bitmap of whichever size was chosen, leaving the composition alone.

  readonly exportPresets = FIGURE_EXPORT_PRESETS;

  /** The same presets as the picker renders them: one `<optgroup>` per aspect ratio. */
  readonly exportPresetGroups = FIGURE_EXPORT_PRESET_GROUPS;

  readonly minExportDimension = FIGURE_EXPORT_MIN_DIMENSION;
  readonly maxExportDimension = FIGURE_EXPORT_MAX_DIMENSION;

  readonly exportDensityPresets = FIGURE_EXPORT_DENSITY_PRESETS;
  readonly minExportDensityPercent = FIGURE_EXPORT_MIN_DENSITY_PERCENT;
  readonly maxExportDensityPercent = FIGURE_EXPORT_MAX_DENSITY_PERCENT;

  /**
   * The display's density when the component initialised, which the matching option is labelled
   * with and which the control opens on.
   *
   * Sampled once and then left alone: a reader who chose 300 % and dragged the window to another
   * display keeps 300 %, and the labelled option is how they find their way back.
   */
  readonly displayDensity = displayDensity();

  /**
   * Size, density and text size, as the Style tab's Figure size section sets them. Read from
   * storage when the component is created and written on every change; replaced, never mutated.
   */
  figureSize: FigureSizeSettings = readStoredFigureSize(this.displayDensity);

  /** On, one custom side follows the other so the shape survives a change of size. */
  customRatioLocked = false;

  /** The ratio the lock captured, which is what the unedited side is derived from. */
  private customRatio = 16 / 9;

  get isCustomResolution(): boolean {
    return this.figureSize.resolutionId === 'custom';
  }

  /** The chosen preset, or the custom pair clamped into the supported range. */
  get exportResolution(): FigureExportResolution {
    if (!this.isCustomResolution) {
      return this.exportPresets.find(preset => preset.id === this.figureSize.resolutionId)
        ?? this.exportPresets.find(preset => preset.id === DEFAULT_FIGURE_RESOLUTION_ID)
        ?? this.exportPresets[0];
    }
    return {
      id: 'custom',
      label: 'Custom',
      group: 'Custom',
      widthPx: this.clampDimension(this.figureSize.customWidthPx),
      heightPx: this.clampDimension(this.figureSize.customHeightPx)
    };
  }

  get isCustomDensity(): boolean {
    return this.figureSize.densitySelection === 'custom';
  }

  /** The chosen factor: a listed preset, or the custom percentage clamped into its bounds. */
  get exportDensity(): number {
    const selection = this.figureSize.densitySelection;
    if (selection !== 'custom') {
      return selection;
    }
    return this.clampDensityPercent(this.figureSize.customDensityPercent) / 100;
  }

  /** The percentage the read-outs name the current density by. */
  get exportDensityLabel(): string {
    return densityPercentLabel(this.exportDensity);
  }

  /** One option's text, with the one that matches the reader's own display marked as such. */
  densityOptionLabel(preset: number): string {
    return preset === this.displayDensity
      ? `${densityPercentLabel(preset)} (this display)`
      : densityPercentLabel(preset);
  }

  /** The current size's shape, e.g. `16:9`. */
  get exportAspectLabel(): string {
    const resolution = this.exportResolution;
    return aspectRatioLabel(resolution.widthPx, resolution.heightPx);
  }

  /**
   * The size and format in one line, for the *Download all* tooltip.
   *
   * The figure bar carries the settings as a read-out rather than as controls: the size lives in
   * the sidebar's Style tab and the format in its Download tab, beside the figures they shape.
   */
  get exportSummary(): string {
    const format = this.exportFormat === 'webp'
      ? `WebP q${this.figureWebpQuality}`
      : 'PNG';
    return `${this.exportResolution.label} · ${this.exportDensityLabel} · ${format}`;
  }

  /** What *Download all* would do now, or why it will not. */
  get downloadAllTooltip(): string {
    if (this.exporting) {
      return 'An export is running.';
    }
    if (this.exportSizeError !== '') {
      return this.exportSizeError;
    }
    return `All figures as one archive — ${this.exportSummary}`;
  }

  /** An out-of-range custom size, named. Empty while the current setting is usable. */
  get customResolutionError(): string {
    if (!this.isCustomResolution) {
      return '';
    }
    const bad = [
      this.isUsableDimension(this.figureSize.customWidthPx) ? '' : 'width',
      this.isUsableDimension(this.figureSize.customHeightPx) ? '' : 'height'
    ].filter(name => name !== '');
    if (bad.length === 0) {
      return '';
    }
    return `The figure ${bad.join(' and ')} must be between ${this.minExportDimension} and ` +
      `${this.maxExportDimension} px.`;
  }

  /** An out-of-range custom density, named. Empty while the current setting is usable. */
  get customDensityError(): string {
    if (!this.isCustomDensity) {
      return '';
    }
    const percent = this.figureSize.customDensityPercent;
    const usable = Number.isFinite(percent)
      && percent >= this.minExportDensityPercent
      && percent <= this.maxExportDensityPercent;
    if (usable) {
      return '';
    }
    return `The pixel density must be between ${this.minExportDensityPercent} and ` +
      `${this.maxExportDensityPercent} %.`;
  }

  /**
   * A written bitmap no browser can allocate, named. Empty while either input is out of range,
   * which is the more specific complaint and is what the reader has to fix first.
   */
  get exportDensityError(): string {
    if (this.customResolutionError !== '' || this.customDensityError !== '') {
      return '';
    }
    const resolution = this.exportResolution;
    return bitmapRefusal(resolution.widthPx, resolution.heightPx, this.exportDensity) ?? '';
  }

  /** The one message the size, the custom sides and the density all describe themselves by. */
  get exportSizeError(): string {
    return this.customResolutionError || this.customDensityError || this.exportDensityError;
  }

  /** What the current setting will actually write, in the reader's own units. */
  get exportDimensionsLabel(): string {
    const resolution = this.exportResolution;
    const percent = this.exportDensityLabel;
    const density = this.exportDensity;
    const box = layoutBoxFor(resolution.widthPx, resolution.heightPx, this.exportTextScale);
    const written = `${Math.round(resolution.widthPx * density)} × ` +
      `${Math.round(resolution.heightPx * density)} px`;
    // At 100 % the requested size and the written one are the same number, and printing it twice
    // would read as an error rather than as a multiplication.
    const requested = density === 1
      ? `at ${percent}`
      : `(${resolution.widthPx} × ${resolution.heightPx} at ${percent})`;
    return `${written} ${requested} — laid out at ` +
      `${Math.round(box.layoutWidth)} × ${Math.round(box.layoutHeight)}, ` +
      `${this.formatDensity(box.density * density)}× density`;
  }

  /** The closed Figure size section's one-line read-out. */
  get figureSizeReadout(): string {
    const resolution = this.exportResolution;
    const size = this.isCustomResolution
      ? `Custom ${resolution.widthPx} × ${resolution.heightPx}`
      : resolution.label;
    return `${size} · ${this.exportDensityLabel} · text ${this.figureSize.textScalePercent} %`;
  }

  /** Whether the Figure size section holds its defaults, which is when its reset has nothing to do. */
  get figureSizeIsDefault(): boolean {
    return sameFigureSize(this.figureSize, defaultFigureSize(this.displayDensity));
  }

  /** What the Figure size section's status line announces after a reset; cleared by the next change. */
  figureSizeResetStatus = '';

  /** Restores Full HD, the display's own density and 100 % text. The custom ratio lock is left alone. */
  resetFigureSize(): void {
    if (this.figureSizeIsDefault) {
      return;
    }
    this.setFigureSize(defaultFigureSize(this.displayDensity));
    this.figureSizeResetStatus = 'Figure size reset to defaults.';
  }

  onExportResolutionChange(value: string): void {
    this.setFigureSize({ resolutionId: value });
  }

  onExportDensityChange(value: number | 'custom'): void {
    this.setFigureSize({ densitySelection: value });
  }

  onCustomDensityChange(percent: number): void {
    this.setFigureSize({ customDensityPercent: percent });
  }

  /**
   * Captures the current shape when the lock goes on, and releases it when it goes off.
   *
   * Captured rather than held from the preset the reader came from: the two fields are what is on
   * screen, and a lock that snapped them to some earlier ratio would change the size it was asked
   * to preserve.
   */
  lockCustomRatio(locked: boolean): void {
    this.customRatioLocked = locked;
    if (locked) {
      const width = this.clampDimension(this.figureSize.customWidthPx);
      const height = this.clampDimension(this.figureSize.customHeightPx);
      this.customRatio = height > 0 ? width / height : 1;
    }
    this.cdr.markForCheck();
  }

  onCustomWidthChange(width: number): void {
    this.setFigureSize(this.customRatioLocked
      ? { customWidthPx: width, customHeightPx: this.clampDimension(Math.round(width / this.customRatio)) }
      : { customWidthPx: width });
  }

  onCustomHeightChange(height: number): void {
    this.setFigureSize(this.customRatioLocked
      ? { customHeightPx: height, customWidthPx: this.clampDimension(Math.round(height * this.customRatio)) }
      : { customHeightPx: height });
  }

  onFigureWebpQualityChange(value: WebpQuality): void {
    this.figureWebpQuality = value;
  }

  readonly minExportTextScalePercent = FIGURE_EXPORT_MIN_TEXT_SCALE_PERCENT;
  readonly maxExportTextScalePercent = FIGURE_EXPORT_MAX_TEXT_SCALE_PERCENT;

  /** The text size as the factor the layout resolver takes. */
  get exportTextScale(): number {
    return this.figureSize.textScalePercent / 100;
  }

  /** Composition text size as a percentage: larger composes in a smaller box, at the same pixel size. */
  onExportTextScaleChange(percent: number): void {
    const value = Number.isFinite(percent) ? Math.round(percent) : 100;
    this.setFigureSize({
      textScalePercent: Math.min(this.maxExportTextScalePercent, Math.max(this.minExportTextScalePercent, value))
    });
  }

  /**
   * Replaces the size, stores it and re-composes whichever view is shown. The All tab's tiles take
   * their new box at once; their bitmaps follow once the change pauses.
   */
  private setFigureSize(patch: Partial<FigureSizeSettings>): void {
    this.figureSize = { ...this.figureSize, ...patch };
    writeStoredFigureSize(this.figureSize);
    this.figureSizeResetStatus = '';
    this.schedulePreview();
    if (this.allActive) {
      this.refreshAllGeometry();
      this.markAllStale();
      this.scheduleAllCompose();
    }
    this.cdr.markForCheck();
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
    return !this.exporting && this.exportableCards.length > 0 && this.exportSizeError === '';
  }

  onExportFormatChange(value: FigureExportFormat): void {
    this.exportFormat = value;
    this.schedulePreview();
  }

  async downloadFigure(card: ComparisonFigureCard): Promise<void> {
    await this.downloadFigures([card], 'file');
  }

  async downloadAllFigures(): Promise<void> {
    await this.downloadFigures(this.exportableCards, 'archive');
  }

  /**
   * Puts one figure on the system clipboard, composed exactly as its download composes it — at the
   * figure size, chrome and footer inside the same bitmap — and always as a PNG.
   *
   * There is deliberately no *Copy all figures*: an operating-system clipboard holds one image, so
   * a batch would appear to copy six and silently keep the last.
   */
  async copyFigure(card: ComparisonFigureCard): Promise<void> {
    if (this.exporting || this.exportSizeError !== '') {
      return;
    }
    this.exporting = true;
    this.exportNotice = null;
    this.cdr.markForCheck();

    try {
      const figure = await this.exportOneFigure(card, this.exportResolution, 'png');
      if (!figure.result) {
        this.announce(figure.refusal ?? 'The figure could not be copied.', 'error');
        return;
      }
      const outcome = await copyImageToClipboard(figure.result.blob);
      if (outcome === 'copied') {
        this.announce(`Copied ${card.title} to the clipboard.`, 'success');
      } else if (outcome === 'unsupported') {
        this.announce(
          'This browser cannot copy images to the clipboard — download the figure instead.',
          'error'
        );
      } else {
        this.announce('The clipboard write was refused.', 'error');
      }
    } catch {
      this.announce('The figure could not be copied.', 'error');
    } finally {
      this.exporting = false;
      this.cdr.markForCheck();
    }
  }

  /**
   * Writes the cards as one file each, or as one archive holding them all.
   *
   * An archive rather than one save per card: several browsers prompt before allowing a second
   * save from one gesture, and a batch that trips the prompt writes an unpredictable subset.
   *
   * A card the target size cannot fit is skipped with its refusal collected rather than aborting
   * the batch, so a partially-refused export names both what it wrote and what it would not.
   */
  private async downloadFigures(
    cards: readonly ComparisonFigureCard[],
    mode: 'file' | 'archive'
  ): Promise<void> {
    if (this.exporting || cards.length === 0 || this.exportSizeError !== '') {
      return;
    }
    const resolution = this.exportResolution;
    this.exporting = true;
    this.exportNotice = null;
    this.cdr.markForCheck();

    const stamp = new Date();
    const entries: FigureArchiveEntry[] = [];
    let fellBack = false;
    let pixels = '';
    const refusals: string[] = [];
    try {
      for (const card of cards) {
        const outcome = await this.exportOneFigure(card, resolution);
        if (outcome.refusal) {
          refusals.push(outcome.refusal);
          continue;
        }
        if (!outcome.result) {
          continue;
        }
        fellBack = fellBack || outcome.result.fellBackToPng;
        pixels = outcome.pixels || pixels;
        entries.push({
          name: figureExportFilename(card.id, outcome.result.format, stamp),
          blob: outcome.result.blob
        });
      }
      let archive = '';
      if (entries.length === 1 && mode === 'file') {
        saveFigureBlob(entries[0].blob, entries[0].name);
      } else if (entries.length > 0) {
        archive = figureArchiveFilename(stamp);
        saveFigureBlob(await buildFigureArchive(entries), archive);
      }
      this.announce(
        this.exportOutcomeSummary(entries.length, cards.length, pixels, fellBack, refusals, archive),
        entries.length > 0 && refusals.length === 0 ? 'success' : 'error'
      );
    } catch {
      this.announce('The figures could not be exported.', 'error');
    } finally {
      this.exporting = false;
      this.cdr.markForCheck();
    }
  }

  /**
   * Encodes one figure at the requested resolution, or refuses it.
   *
   * The plot is rendered in a transient offscreen chart sized to the layout's own plot box — the
   * same composition the All and Single tabs show. A figure whose chart cannot be built is refused
   * in words, like a figure whose caveats do not fit.
   *
   * The format is a parameter rather than the control's value because the clipboard path is fixed
   * at PNG: every engine that implements `ClipboardItem` rejects `image/webp` in one.
   */
  private async exportOneFigure(
    card: ComparisonFigureCard,
    resolution: FigureExportResolution,
    format: FigureExportFormat = this.exportFormat
  ): Promise<{
    result: FigureExportResult | null;
    refusal: string | null;
    pixels: string;
  }> {
    const chrome = this.exportChrome(card);
    const { layout, refusal } = resolveFigureLayout(chrome, resolution, this.exportDensity, this.exportTextScale);
    if (!layout) {
      return { result: null, refusal, pixels: '' };
    }

    const composed = await this.composeFigure(card, chrome, layout, format);
    if (!composed) {
      return { result: null, refusal: `${card.title} could not be composed: its chart could not be built.`, pixels: '' };
    }
    return {
      // Quality is read only by the WebP encoder, so the clipboard's fixed PNG ignores it.
      result: await encodeFigureImage(composed, format, this.figureWebpQuality),
      refusal: null,
      pixels: `${layout.pixelWidth} × ${layout.pixelHeight} px`
    };
  }

  /**
   * The one composition every figure goes through — a tile on the All tab, the Single tab's stage,
   * a download and a copy: the plot rendered offscreen at `layout`, and the chrome drawn around it.
   * Null where the offscreen chart could not be built.
   */
  private async composeFigure(
    card: ComparisonFigureCard,
    chrome: FigureExportChrome,
    layout: FigureExportLayout,
    format: FigureExportFormat = this.exportFormat
  ): Promise<HTMLCanvasElement | null> {
    const plot = await renderPlotOffscreen(
      { type: card.type, data: card.data, options: card.options, plugins: card.plugins },
      layout
    );
    return plot ? composeFigureImage({ ...chrome, canvas: plot, format, layout }) : null;
  }

  /**
   * One card's chrome: everything the exported image carries besides the plot itself, at its
   * family's caption sizes, with the footer emptied while the family hides it.
   */
  private exportChrome(card: ComparisonFigureCard): FigureExportChrome {
    const style = this.figureStyle[this.familyOf(card)];
    return {
      chrome: { ...card.chrome, notes: [...card.chrome.notes, ...this.setFigureNotes] },
      footer: style.footer ? this.figureFooter : { suite: '', computedAt: '' },
      textSizes: {
        titlePx: style.titleSizePx,
        badgePx: style.badgeTextSizePx,
        footerPx: style.footerTextSizePx
      }
    };
  }

  /** Which style family a card draws from: the bar panels, the trade-off scatters or the profile. */
  private familyOf(card: ComparisonFigureCard | null | undefined): FigureStylePanelKind {
    const type = card?.type;
    return type === 'bar' ? 'bar' : type === 'scatter' ? 'scatter' : 'profile';
  }

  private clampDimension(value: number): number {
    if (!Number.isFinite(value)) {
      return this.minExportDimension;
    }
    return Math.min(this.maxExportDimension, Math.max(this.minExportDimension, Math.round(value)));
  }

  private clampDensityPercent(value: number): number {
    if (!Number.isFinite(value)) {
      return 100;
    }
    return Math.min(
      this.maxExportDensityPercent,
      Math.max(this.minExportDensityPercent, Math.round(value))
    );
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

  /**
   * Suite, pricing basis, entry count, the reference condition and the computation time — the
   * table export's provenance of this comparison.
   *
   * The condition segment is twelve hex characters of the baseline's must-match signature. It
   * travels in the table export files, where a reader holding only one exported file can compare it
   * against another export's own segment. A comparison that reached no baseline carries no
   * signature, and the segment is omitted rather than printed empty.
   */
  get tableProvenance(): ComparisonTableProvenance {
    const dto = this.comparison;
    return {
      suite: dto?.baselineSuiteName || 'Suite not set',
      pricingBasis: dto?.pricingBasisLabel || dto?.pricingBasis || 'Unknown pricing basis',
      conditionSignature: (dto?.baselineSignature ?? '').trim().slice(0, 12),
      computedAt: dto?.computedAtUtc ? new Date(dto.computedAtUtc).toLocaleString() : 'unknown time',
      plottedOfTotal: `${this.plotted.length} of ${this.entries.length} entries charted`,
      notices: this.setNotices
    };
  }

  /** The computation time, the one part of the provenance the wizard header does not show. */
  get tableComputedAtLine(): string {
    return `Computed ${this.tableProvenance.computedAt}`;
  }

  /**
   * The figure footer: the suite and the computation time, in the composer's own two-sided layout,
   * under each figure whose family has the footer on.
   */
  get figureFooter(): FigureFooter {
    const dto = this.comparison;
    return {
      suite: dto?.baselineSuiteName || 'Suite not set',
      computedAt: formatComputedAt(dto?.computedAtUtc ?? '')
    };
  }

  /**
   * What the export actually did, including everything it would not do.
   *
   * A refused figure is named in full: a batch that silently wrote five of six files reads as a
   * success, and the missing one is exactly the figure whose caveats did not fit.
   */
  private exportOutcomeSummary(
    written: number,
    requested: number,
    pixels: string,
    fellBack: boolean,
    refusals: readonly string[],
    archive: string
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
      if (archive !== '') {
        parts.push(`${written}${shortfall} ${noun} saved to ${archive}${size}.`);
      } else {
        parts.push(`${written}${shortfall} ${noun} saved${size}.`);
      }
    }
    if (fellBack) {
      parts.push('This browser cannot encode WebP, so the file was written as PNG.');
    }
    parts.push(...refusals);
    return parts.join(' ');
  }

  // ---------------------------------------------------------------------------------------------
  // The step-4 workspace
  //
  // A collapsible settings sidebar (Emphasis, Style, Download) beside two views of the figures:
  // All, every figure, and Single, one figure with zoom and pan. Both show figures composed by the
  // export pipeline at the figure size, so the page and a download are the same image. Every
  // control exists once.
  // ---------------------------------------------------------------------------------------------

  private readonly storedSidebar = readStoredFigureSidebar();

  /** All by default; kept across steps and remembered per browser. */
  figureTab: FigureViewTab = this.storedSidebar.view;

  readonly figureTabs: readonly { readonly id: FigureViewTab; readonly label: string; readonly name: string }[] = [
    { id: 'all', label: 'All', name: 'All figures' },
    { id: 'single', label: 'Single', name: 'Single figure' }
  ];

  sidebarCollapsed = this.storedSidebar.collapsed;

  sidebarTab: FigureSidebarTab = this.storedSidebar.tab;

  /** Whether the Style tab's Figure size section is open. Open by default. */
  figureSizeOpen = this.storedSidebar.figureSizeOpen;

  /** Follows the native `toggle`, which fires for a click, a key and a bound `open` alike. */
  onFigureSizeToggle(event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (open === this.figureSizeOpen) {
      return;
    }
    this.figureSizeOpen = open;
    this.writeStoredSidebar();
  }

  readonly sidebarTabs: readonly { readonly id: FigureSidebarTab; readonly label: string }[] = [
    { id: 'emphasis', label: 'Emphasis' },
    { id: 'style', label: 'Style' },
    { id: 'download', label: 'Download' }
  ];

  /** Focus stays on the toggle, which sits outside the sidebar and is always rendered. */
  toggleSidebar(): void {
    this.sidebarCollapsed = !this.sidebarCollapsed;
    this.writeStoredSidebar();
    this.cdr.markForCheck();
  }

  selectSidebarTab(tab: FigureSidebarTab): void {
    this.sidebarTab = tab;
    this.writeStoredSidebar();
    this.cdr.markForCheck();
  }

  /** Left/Right move and wrap, Home/End jump to the ends; focus follows selection. */
  onSidebarTabKeydown(event: KeyboardEvent, index: number): void {
    const next = this.rovingTabIndex(event, index, this.sidebarTabs.length);
    if (next === null) {
      return;
    }
    const tab = this.sidebarTabs[next].id;
    this.selectSidebarTab(tab);
    this.cdr.detectChanges();
    document.getElementById(`mc-side-tab-${tab}`)?.focus();
  }

  /**
   * Switches the figure view. Each view attaches once its panel exists, and detaches while its
   * elements still do. Single opens on the figure last activated on All, or the first.
   */
  selectFigureTab(tab: FigureViewTab): void {
    if (tab === this.figureTab) {
      return;
    }
    if (tab === 'single') {
      this.detachAll();
      if (this.previewCard === null) {
        this.previewCardId = this.exportableCards[0]?.id ?? null;
      }
      const card = this.previewCard;
      if (card) {
        this.styleFamily = this.familyOf(card);
      }
      this.setHighlight(null);
      this.figureTab = 'single';
      this.writeStoredSidebar();
      this.cdr.detectChanges();
      this.attachPreview();
    } else {
      this.detachPreview();
      this.figureTab = 'all';
      this.writeStoredSidebar();
      this.cdr.detectChanges();
      this.attachAll();
    }
  }

  onFigureTabKeydown(event: KeyboardEvent, index: number): void {
    const next = this.rovingTabIndex(event, index, this.figureTabs.length);
    if (next === null) {
      return;
    }
    const tab = this.figureTabs[next].id;
    this.selectFigureTab(tab);
    this.cdr.detectChanges();
    document.getElementById(`mc-fig-tab-${tab}`)?.focus();
  }

  /**
   * A tile on the All tab, clicked, entered or opened by its eye button: the Single tab on that
   * figure, with focus on the figure select.
   */
  openInSingle(card: ComparisonFigureCard): void {
    this.selectPreviewCard(card.id);
    if (this.figureTab === 'single') {
      this.cdr.detectChanges();
    } else {
      this.selectFigureTab('single');
    }
    document.getElementById('mc-preview-figure')?.focus();
  }

  /** Enter on the tile itself; a key pressed on the eye button inside it is the button's own. */
  onAllTileKeydown(event: KeyboardEvent, card: ComparisonFigureCard): void {
    if (event.key !== 'Enter' || event.target !== event.currentTarget) {
      return;
    }
    event.preventDefault();
    this.openInSingle(card);
  }

  /** The §5 tab keyboard model's target index, or null for a key it does not handle. */
  private rovingTabIndex(event: KeyboardEvent, index: number, count: number): number | null {
    const targets: Record<string, number> = {
      ArrowRight: index + 1,
      ArrowLeft: index - 1,
      Home: 0,
      End: count - 1
    };
    const requested = targets[event.key];
    if (requested === undefined) {
      return null;
    }
    event.preventDefault();
    return (requested + count) % count;
  }

  private writeStoredSidebar(): void {
    try {
      localStorage.setItem(FIGURE_SIDEBAR_STORAGE_KEY, JSON.stringify({
        version: 1,
        collapsed: this.sidebarCollapsed,
        tab: this.sidebarTab,
        view: this.figureTab,
        figureSizeOpen: this.figureSizeOpen
      }));
    } catch {
      // Private mode or blocked storage: the layout still applies for this session.
    }
  }

  // --- Style family ---

  /** Which family the Style tab edits. Follows the figure on the Single tab. */
  styleFamily: FigureStylePanelKind = 'bar';

  /** The families with a rendered card, in card order. */
  get styleFamilies(): { kind: FigureStylePanelKind; label: string }[] {
    const families: { kind: FigureStylePanelKind; label: string }[] = [{ kind: 'bar', label: 'Bar panels' }];
    if (this.profileCard) {
      families.push({ kind: 'profile', label: 'Profile' });
    }
    if (this.scatterCards.length > 0) {
      families.push({ kind: 'scatter', label: 'Trade-offs' });
    }
    return families;
  }

  /** The family the Style tab renders: `bar` where the chosen one has no card in this set. */
  get effectiveStyleFamily(): FigureStylePanelKind {
    return this.styleFamilies.some(family => family.kind === this.styleFamily) ? this.styleFamily : 'bar';
  }

  /** On the Single tab, the stage moves to the first figure of the chosen family. */
  selectStyleFamily(kind: FigureStylePanelKind): void {
    this.styleFamily = kind;
    if (this.figureTab === 'single' && this.familyOf(this.previewCard) !== kind) {
      const target = this.exportableCards.find(card => this.familyOf(card) === kind);
      if (target) {
        this.selectPreviewCard(target.id);
      }
    }
    this.cdr.markForCheck();
  }

  /** Left/Right move and wrap, Home/End jump to the ends; focus follows selection. */
  onStyleFamilyKeydown(event: KeyboardEvent, index: number): void {
    const families = this.styleFamilies;
    const next = this.rovingTabIndex(event, index, families.length);
    if (next === null) {
      return;
    }
    const kind = families[next].kind;
    this.selectStyleFamily(kind);
    this.cdr.detectChanges();
    document.getElementById(`mc-style-family-tab-${kind}`)?.focus();
  }

  // ---------------------------------------------------------------------------------------------
  // The Single tab
  //
  // One figure at a time, composed by the same pipeline the download uses and drawn onto a canvas
  // with zoom and pan: the size, the aspect ratio and the text size are chosen against the image
  // they produce rather than against a file already on disk. A figure whose caveats do not fit the
  // chosen box is refused here, in the same words the download would refuse it in. The `preview`
  // names below are this tab's.
  // ---------------------------------------------------------------------------------------------

  /** The stage the composed image is drawn onto. Present while the Single tab is shown. */
  @ViewChild('previewCanvas') previewCanvas?: ElementRef<HTMLCanvasElement>;

  /** The box the stage canvas is fitted into, and the element whose size the preview follows. */
  @ViewChild('previewStage') previewStage?: ElementRef<HTMLElement>;

  /** Which card the Single tab shows. Null before it has ever been shown or a tile activated. */
  previewCardId: string | null = null;

  /**
   * True while the stage is attached and composing, which is what a control change checks before
   * composing. Not bound in the template, so the view hooks may change it.
   */
  previewActive = false;

  previewBusy = false;

  /** The target size's refusal, in the words the download refuses it in. Empty while it fits. */
  previewRefusal = '';

  /** A composition is asynchronous, so a slow one must not paint over a newer one behind it. */
  private previewSeq = 0;

  private previewTimer: ReturnType<typeof setTimeout> | null = null;

  /** Watches the stage, so a resized window, a split screen or a zoom re-composes rather than scales. */
  private previewResizeObserver: ResizeObserver | null = null;

  /** Long enough that a held arrow key in a size field composes once, short enough to feel live. */
  private readonly previewDebounceMs = 150;

  /** The scroller inside the stage. The stage measures; this pans. */
  @ViewChild('previewViewport') previewViewport?: ElementRef<HTMLElement>;

  private zone = inject(NgZone);

  /** What the reader asked the stage to show. Never reaches an export. */
  previewView: PreviewViewRequest = 'default';

  /** The zoom that fills the stage, from the last fitted layout. */
  previewScreenFitZoom = 1;

  /** True where the raster budget, not the zoom, set the stage's bitmap size. */
  previewRasterCapped = false;

  readonly previewSliderSteps = PREVIEW_SLIDER_STEPS;

  /** `"<width>x<height>"` of the last composed target; a new one resets an explicit zoom. */
  private previewTargetKey: string | null = null;

  /** The last composed target's pixels and ratio, so a zoom resizes the stage before it recomposes. */
  private previewTargetPixels: { width: number; height: number } | null = null;
  private previewDpr = 1;

  /** The fraction of the target the painted bitmap carries; a zoom needing another one recomposes. */
  private previewPaintedRasterZoom: number | null = null;

  private previewWheelFactor = 1;
  private previewWheelAnchor: { clientX: number; clientY: number } | null = null;
  private previewWheelFrame: number | null = null;

  private previewPan: { pointerId: number; x: number; y: number; left: number; top: number } | null = null;

  /** Registered outside Angular on attach and removed on detach. */
  private previewViewportListeners: [string, EventListener, AddEventListenerOptions?][] = [];

  /**
   * The element those listeners are on. Kept rather than re-read from `previewViewport`, which is
   * already gone when the panel leaves the DOM before the teardown runs.
   */
  private previewListenedViewport: HTMLElement | null = null;

  /** The card the stage is showing, or null where the current slice no longer draws it. */
  get previewCard(): ComparisonFigureCard | null {
    return this.exportableCards.find(card => card.id === this.previewCardId) ?? null;
  }

  /** The stage is a `role="img"`, so it carries the card's own summary rather than a bare noun. */
  get previewAriaLabel(): string {
    const card = this.previewCard;
    return card ? `Preview of ${card.ariaLabel}` : 'Figure preview';
  }

  /**
   * Starts composing onto a stage that has just been rendered — after a tab change, or when step 4
   * opens on a remembered Single tab, where no figure has been chosen yet.
   *
   * The toolbar's tooltip anchors render behind the panel's @if, which the polyfill's first scan
   * never saw.
   */
  private attachPreview(): void {
    if (this.previewCard === null) {
      this.previewCardId = this.exportableCards[0]?.id ?? null;
    }
    this.previewView = 'default';
    this.previewRefusal = '';
    this.previewActive = true;
    this.observeStage();
    refreshAnchorPositioning();
    this.schedulePreview();
  }

  /**
   * Stops composing and drops the composition. The stage is blanked rather than left holding the
   * last figure: returning on another card would show the previous one until the first composition
   * landed.
   */
  private detachPreview(): void {
    this.previewActive = false;
    this.cancelScheduledPreview();
    this.disconnectStageObserver();
    this.previewSeq++;
    this.previewBusy = false;
    this.previewView = 'default';
    this.previewTargetKey = null;
    this.previewTargetPixels = null;
    this.previewPaintedRasterZoom = null;
    this.previewRasterCapped = false;
    this.blankPreview();
    this.cdr.markForCheck();
  }

  previewPrevious(): void {
    this.stepPreview(-1);
  }

  previewNext(): void {
    this.stepPreview(1);
  }

  /** The Style tab follows, so it always edits the figure on the stage. */
  selectPreviewCard(id: string): void {
    this.previewCardId = id;
    const card = this.previewCard;
    if (card) {
      this.styleFamily = this.familyOf(card);
    }
    this.schedulePreview();
    this.cdr.markForCheck();
  }

  /** Delegates, so the preview and *Download all* cannot drift apart in what they write. */
  async downloadPreviewedFigure(): Promise<void> {
    const card = this.previewCard;
    if (card && this.canExport) {
      await this.downloadFigure(card);
    }
  }

  async copyPreviewedFigure(): Promise<void> {
    const card = this.previewCard;
    if (card && this.canExport) {
      await this.copyFigure(card);
    }
  }

  get previewZoomRange(): PreviewZoomRange {
    return previewZoomRange(this.previewScreenFitZoom);
  }

  /** The requested view as device pixels per export pixel: 1 is 100 %. */
  get previewZoomValue(): number {
    return resolvePreviewZoom(this.previewView, this.previewScreenFitZoom);
  }

  get previewSliderValue(): number {
    return zoomToSlider(this.previewZoomValue, this.previewZoomRange);
  }

  get previewZoomLabel(): string {
    const zoom = formatPreviewZoom(this.previewZoomValue);
    return this.previewView === 'fitScreen' ? `${zoom} · Fit to screen` : zoom;
  }

  get previewZoomValueText(): string {
    const percent = `${formatPreviewZoom(this.previewZoomValue).replace('%', '')} percent`;
    if (this.previewView === 'fitScreen') {
      return `${percent}, fitted to the screen`;
    }
    return this.previewView === 'default' ? `${percent}, default view` : percent;
  }

  get canZoomPreviewIn(): boolean {
    return canZoomPreviewIn(this.previewZoomValue, this.previewZoomRange);
  }

  get canZoomPreviewOut(): boolean {
    return canZoomPreviewOut(this.previewZoomValue, this.previewZoomRange);
  }

  zoomPreviewIn(): void {
    if (this.canZoomPreviewIn) {
      this.setPreviewView(
        nextPreviewZoomStop(this.previewZoomValue, this.previewScreenFitZoom, this.previewZoomRange));
    }
  }

  zoomPreviewOut(): void {
    if (this.canZoomPreviewOut) {
      this.setPreviewView(
        previousPreviewZoomStop(this.previewZoomValue, this.previewScreenFitZoom, this.previewZoomRange));
    }
  }

  onPreviewSliderInput(value: number): void {
    this.setPreviewView(sliderToZoom(value, this.previewZoomRange));
  }

  fitPreviewToScreen(): void {
    this.setPreviewView('fitScreen');
  }

  showPreviewActualPixels(): void {
    this.setPreviewView(1);
  }

  /** The default view always fits, so CSS centres it once the scroll is back at the origin. */
  resetPreviewView(): void {
    this.setPreviewView('default');
    this.previewViewport?.nativeElement.scrollTo(0, 0);
  }

  /** Unmodified keys only: Ctrl / ⌘ / Alt with + − 0 stay the browser's own zoom. */
  onPreviewViewportKeydown(event: KeyboardEvent): void {
    if (event.ctrlKey || event.metaKey || event.altKey) {
      return;
    }
    const actions: Record<string, () => void> = {
      '+': () => this.zoomPreviewIn(),
      '=': () => this.zoomPreviewIn(),
      '-': () => this.zoomPreviewOut(),
      '0': () => this.fitPreviewToScreen(),
      '1': () => this.showPreviewActualPixels()
    };
    const action = actions[event.key];
    if (action) {
      event.preventDefault();
      action();
    }
  }

  /**
   * Applies a view at once, stretching the bitmap already painted, and recomposes only where the
   * new zoom needs a different raster.
   *
   * The point under `anchor` — the viewport's centre where none is given — stays under it.
   */
  setPreviewView(request: PreviewViewRequest, anchor?: { clientX: number; clientY: number }): void {
    const range = this.previewZoomRange;
    const next = typeof request === 'number' ? clampPreviewZoom(request, range) : request;
    const before = this.previewZoomValue;
    const after = resolvePreviewZoom(next, this.previewScreenFitZoom);
    const sameKind = typeof next === typeof this.previewView &&
      (typeof next === 'number' || next === this.previewView);
    if (sameKind && Math.abs(after - before) <= before * 1e-9) {
      return;
    }

    const viewport = this.previewViewport?.nativeElement;
    const canvas = this.previewCanvas?.nativeElement;
    const beforeRect = canvas?.getBoundingClientRect();
    const point = anchor ?? this.viewportCentre();

    this.previewView = next;
    this.applyPreviewViewSize();

    if (viewport && canvas && beforeRect && point) {
      const afterRect = canvas.getBoundingClientRect();
      viewport.scrollLeft += anchoredScrollDelta(
        beforeRect.left, beforeRect.width, afterRect.left, afterRect.width, point.clientX);
      viewport.scrollTop += anchoredScrollDelta(
        beforeRect.top, beforeRect.height, afterRect.top, afterRect.height, point.clientY);
    }

    const pixels = this.previewTargetPixels;
    const raster = pixels ? previewRasterZoom(after, pixels.width, pixels.height).zoom : null;
    if (raster === null || this.previewPaintedRasterZoom === null ||
        Math.abs(raster - this.previewPaintedRasterZoom) > this.previewPaintedRasterZoom * 1e-9) {
      this.schedulePreview();
    }
    this.cdr.markForCheck();
  }

  /** Wrapping: seven figures in a ring, so neither end of the set is a dead control. */
  private stepPreview(delta: number): void {
    const cards = this.exportableCards;
    if (cards.length === 0) {
      return;
    }
    const current = cards.findIndex(card => card.id === this.previewCardId);
    const next = ((current < 0 ? 0 : current + delta) + cards.length) % cards.length;
    this.selectPreviewCard(cards[next].id);
  }

  /** Coalesces a burst of control changes — a held arrow key, a typed size — into one composition. */
  private schedulePreview(): void {
    if (!this.previewActive) {
      return;
    }
    this.cancelScheduledPreview();
    this.previewTimer = setTimeout(() => {
      this.previewTimer = null;
      void this.renderPreview();
    }, this.previewDebounceMs);
  }

  private cancelScheduledPreview(): void {
    if (this.previewTimer !== null) {
      clearTimeout(this.previewTimer);
      this.previewTimer = null;
    }
  }

  /**
   * Composes the current card at the current settings and draws it onto the stage.
   *
   * The target layout is what Download would write, and it alone decides the pixel count and any
   * refusal. The stage then gets that same composition re-rendered at the density its own box
   * affords, so what the reader judges a size by is the export itself rather than a bitmap CSS has
   * squeezed into the box after the fact.
   *
   * Nothing here produces a blob or an object URL — the composed canvas is drawn straight onto the
   * stage's — so a detached stage leaves nothing to revoke.
   */
  private async renderPreview(): Promise<void> {
    const card = this.previewCard;
    if (!card || !this.previewCanvas) {
      return;
    }

    const sequence = ++this.previewSeq;
    this.previewBusy = true;
    this.previewRefusal = '';
    this.cdr.markForCheck();

    try {
      const chrome = this.exportChrome(card);
      const target = resolveFigureLayout(chrome, this.exportResolution, this.exportDensity, this.exportTextScale);
      if (!target.layout) {
        this.previewRefusal = target.refusal ?? '';
        this.blankPreview();
        return;
      }

      // A new pixel size makes an explicit zoom meaningless; the two fitted views follow the stage.
      const targetKey = `${target.layout.pixelWidth}x${target.layout.pixelHeight}`;
      if (targetKey !== this.previewTargetKey) {
        this.previewTargetKey = targetKey;
        if (typeof this.previewView === 'number') {
          this.previewView = 'default';
        }
      }

      const stage = this.measureStage();
      const fit = stage ? previewLayoutFor(target.layout, stage, this.previewView) : null;
      if (!fit) {
        this.blankPreview();
        return;
      }

      const composed = await this.composeFigure(card, chrome, fit.layout);
      if (sequence !== this.previewSeq) {
        return;
      }
      if (composed) {
        const centre = this.captureViewCentre();
        this.previewScreenFitZoom = fit.screenFitZoom;
        this.previewTargetPixels = { width: target.layout.pixelWidth, height: target.layout.pixelHeight };
        this.previewDpr = Math.min(4, Math.max(1, stage!.devicePixelRatio));
        this.previewPaintedRasterZoom = fit.rasterZoom;
        this.previewRasterCapped = fit.rasterCapped;
        this.paintPreview(composed);
        this.applyPreviewViewSize();
        this.restoreViewCentre(centre);
        // The reader may have zoomed while this composed, past what its raster serves.
        const wanted = previewRasterZoom(
          this.previewZoomValue, target.layout.pixelWidth, target.layout.pixelHeight).zoom;
        if (Math.abs(wanted - fit.rasterZoom) > fit.rasterZoom * 1e-9) {
          this.schedulePreview();
        }
      } else {
        // The download refuses the same figure in the same words.
        this.previewRefusal = `${card.title} could not be composed: its chart could not be built.`;
        this.blankPreview();
      }
    } catch {
      this.previewRefusal = 'This figure could not be composed at that size.';
      this.blankPreview();
    } finally {
      if (sequence === this.previewSeq) {
        this.previewBusy = false;
      }
      this.cdr.markForCheck();
    }
  }

  /**
   * The stage's content box and the ratio to rasterise at, or null where the element is absent.
   *
   * The stage is measured rather than the viewport inside it, because the stage never scrolls: a
   * scrollbar appearing on zoom cannot change the fit and feed back into the observer. The
   * viewport's padding is subtracted from the stage's box rather than read as a content box,
   * because that is the number the canvas actually has to fit inside; `window.devicePixelRatio` is
   * read here on every composition, so a window dragged to another display re-rasterises instead
   * of softening.
   */
  measureStage(): PreviewStage | null {
    const element = this.previewStage?.nativeElement;
    if (!element) {
      return null;
    }
    const box = element.getBoundingClientRect();
    const style = getComputedStyle(this.previewViewport?.nativeElement ?? element);
    const horizontal = parseFloat(style.paddingLeft || '0') + parseFloat(style.paddingRight || '0');
    const vertical = parseFloat(style.paddingTop || '0') + parseFloat(style.paddingBottom || '0');
    return {
      width: Math.max(0, box.width - (Number.isFinite(horizontal) ? horizontal : 0)),
      height: Math.max(0, box.height - (Number.isFinite(vertical) ? vertical : 0)),
      devicePixelRatio: window.devicePixelRatio || 1
    };
  }

  /**
   * Re-composes whenever the stage changes size.
   *
   * Window resizes, a split screen and browser zoom all arrive here rather than through three
   * separate listeners, and the debounce behind `schedulePreview` collapses a drag into one
   * composition. Absent outside a browser, where the stage is never laid out to begin with.
   */
  private observeStage(): void {
    this.disconnectStageObserver();
    this.listenOnViewport();
    const element = this.previewStage?.nativeElement;
    if (!element || typeof ResizeObserver === 'undefined') {
      return;
    }
    this.previewResizeObserver = new ResizeObserver(() => {
      this.updatePannable();
      this.schedulePreview();
    });
    this.previewResizeObserver.observe(element);
  }

  private disconnectStageObserver(): void {
    this.previewResizeObserver?.disconnect();
    this.previewResizeObserver = null;
    const viewport = this.previewListenedViewport;
    for (const [type, listener, options] of this.previewViewportListeners) {
      viewport?.removeEventListener(type, listener, options);
    }
    this.previewViewportListeners = [];
    if (this.previewWheelFrame !== null) {
      cancelAnimationFrame(this.previewWheelFrame);
      this.previewWheelFrame = null;
    }
    this.previewWheelFactor = 1;
    this.previewWheelAnchor = null;
    this.endPreviewPan();
    this.previewListenedViewport = null;
  }

  /**
   * Pointer panning and Ctrl + wheel zoom on the viewport, outside Angular.
   *
   * A pan is pure DOM, so no change detection runs per pointer move; a wheel zoom re-enters Angular
   * once per animation frame. Touch keeps the browser's own scrolling, and a plain wheel scrolls.
   */
  private listenOnViewport(): void {
    const viewport = this.previewViewport?.nativeElement;
    if (!viewport) {
      return;
    }

    const onPointerDown = (event: PointerEvent): void => {
      if (event.pointerType === 'touch' || (event.button !== 0 && event.button !== 1) ||
          !this.isViewportPannable(viewport)) {
        return;
      }
      // A press on a scrollbar is the scrollbar's own.
      const box = viewport.getBoundingClientRect();
      if (event.clientX - box.left - viewport.clientLeft >= viewport.clientWidth ||
          event.clientY - box.top - viewport.clientTop >= viewport.clientHeight) {
        return;
      }
      // Also keeps a middle press from starting the browser's autoscroll.
      event.preventDefault();
      viewport.setPointerCapture?.(event.pointerId);
      this.previewPan = {
        pointerId: event.pointerId,
        x: event.clientX,
        y: event.clientY,
        left: viewport.scrollLeft,
        top: viewport.scrollTop
      };
      viewport.classList.add('is-panning');
    };

    const onPointerMove = (event: PointerEvent): void => {
      const pan = this.previewPan;
      if (!pan || pan.pointerId !== event.pointerId) {
        return;
      }
      viewport.scrollLeft = pan.left - (event.clientX - pan.x);
      viewport.scrollTop = pan.top - (event.clientY - pan.y);
    };

    const onPointerEnd = (event: PointerEvent): void => {
      if (this.previewPan?.pointerId === event.pointerId) {
        this.endPreviewPan();
      }
    };

    // Chromium and Firefox deliver a trackpad pinch as a Ctrl + wheel.
    const onWheel = (event: WheelEvent): void => {
      if (!event.ctrlKey && !event.metaKey) {
        return;
      }
      event.preventDefault();
      this.previewWheelFactor *= wheelZoomFactor(event.deltaY, event.deltaMode);
      this.previewWheelAnchor = { clientX: event.clientX, clientY: event.clientY };
      if (this.previewWheelFrame === null) {
        this.previewWheelFrame = requestAnimationFrame(() => {
          this.previewWheelFrame = null;
          const factor = this.previewWheelFactor;
          const anchor = this.previewWheelAnchor ?? undefined;
          this.previewWheelFactor = 1;
          this.previewWheelAnchor = null;
          this.zone.run(() => this.setPreviewView(this.previewZoomValue * factor, anchor));
        });
      }
    };

    const listeners: [string, EventListener, AddEventListenerOptions?][] = [
      ['pointerdown', onPointerDown as EventListener],
      ['pointermove', onPointerMove as EventListener],
      ['pointerup', onPointerEnd as EventListener],
      ['pointercancel', onPointerEnd as EventListener],
      ['lostpointercapture', onPointerEnd as EventListener],
      ['wheel', onWheel as EventListener, { passive: false }]
    ];
    this.zone.runOutsideAngular(() => {
      for (const [type, listener, options] of listeners) {
        viewport.addEventListener(type, listener, options);
      }
    });
    this.previewViewportListeners = listeners;
    this.previewListenedViewport = viewport;
  }

  private endPreviewPan(): void {
    const pan = this.previewPan;
    if (!pan) {
      return;
    }
    this.previewPan = null;
    const viewport = this.previewListenedViewport ?? this.previewViewport?.nativeElement;
    viewport?.classList.remove('is-panning');
    if (viewport?.hasPointerCapture?.(pan.pointerId)) {
      viewport.releasePointerCapture(pan.pointerId);
    }
  }

  private isViewportPannable(viewport: HTMLElement): boolean {
    return viewport.scrollWidth > viewport.clientWidth || viewport.scrollHeight > viewport.clientHeight;
  }

  private updatePannable(): void {
    const viewport = this.previewViewport?.nativeElement;
    viewport?.classList.toggle('is-pannable', this.isViewportPannable(viewport));
  }

  /** The viewport's centre in client px, or null where it has no layout. */
  private viewportCentre(): { clientX: number; clientY: number } | null {
    const viewport = this.previewViewport?.nativeElement;
    if (!viewport) {
      return null;
    }
    const box = viewport.getBoundingClientRect();
    return { clientX: box.left + box.width / 2, clientY: box.top + box.height / 2 };
  }

  /** The canvas's box and the viewport's centre before a repaint, for {@link restoreViewCentre}. */
  private captureViewCentre(): { rect: DOMRect; point: { clientX: number; clientY: number } } | null {
    const canvas = this.previewCanvas?.nativeElement;
    const point = this.viewportCentre();
    return canvas && point ? { rect: canvas.getBoundingClientRect(), point } : null;
  }

  /** Scrolls so the part of the figure that was at the viewport's centre is there again. */
  private restoreViewCentre(
    before: { rect: DOMRect; point: { clientX: number; clientY: number } } | null
  ): void {
    const viewport = this.previewViewport?.nativeElement;
    const canvas = this.previewCanvas?.nativeElement;
    if (!before || !viewport || !canvas) {
      return;
    }
    const after = canvas.getBoundingClientRect();
    viewport.scrollLeft += anchoredScrollDelta(
      before.rect.left, before.rect.width, after.left, after.width, before.point.clientX);
    viewport.scrollTop += anchoredScrollDelta(
      before.rect.top, before.rect.height, after.top, after.height, before.point.clientY);
  }

  /** Draws the composition at its own bitmap size; {@link applyPreviewViewSize} states its box. */
  private paintPreview(composed: HTMLCanvasElement): void {
    const stage = this.previewCanvas?.nativeElement;
    if (!stage) {
      return;
    }
    stage.width = composed.width;
    stage.height = composed.height;
    stage.getContext('2d')?.drawImage(composed, 0, 0);
  }

  /**
   * Sizes the canvas's CSS box for the view requested now, whatever zoom its bitmap was composed
   * for, so a composition that lands late is still shown at the current zoom.
   *
   * The box is stated in CSS px rather than left to a percentage rule. From two displayed device
   * pixels per bitmap pixel the canvas is drawn nearest-neighbour, so single export pixels can be
   * inspected; below that, smoothing avoids uneven pixel widths.
   */
  private applyPreviewViewSize(): void {
    const stage = this.previewCanvas?.nativeElement;
    const pixels = this.previewTargetPixels;
    if (!stage || !pixels || stage.width === 0) {
      return;
    }
    const zoom = this.previewZoomValue;
    const cssWidth = pixels.width / this.previewDpr * zoom;
    const cssHeight = cssWidth * pixels.height / pixels.width;
    stage.style.width = `${cssWidth}px`;
    stage.style.height = `${cssHeight}px`;
    stage.classList.toggle('is-pixelated', pixels.width * zoom / stage.width >= 2 - 1e-9);
    this.updatePannable();
  }

  /** A refused size shows no image at all: the last one that fitted is not what was asked for. */
  private blankPreview(): void {
    const stage = this.previewCanvas?.nativeElement;
    if (!stage) {
      return;
    }
    stage.getContext('2d')?.clearRect(0, 0, stage.width, stage.height);
    stage.width = 0;
    stage.height = 0;
    // The CSS box goes with the bitmap, or a blanked stage keeps the footprint of the last figure.
    stage.style.width = '';
    stage.style.height = '';
    stage.classList.remove('is-pixelated');
    this.previewViewport?.nativeElement.classList.remove('is-pannable');
  }

  // ---------------------------------------------------------------------------------------------
  // The All tab
  //
  // Every figure as a tile, each composed by the same pipeline as the download and drawn onto its
  // own canvas, so the page shows the file a reader would get. A tile's box is the figure's export
  // size over the display ratio, times the All zoom — Single's zoom, where 100 % is actual pixels —
  // and its bitmap is that box at the display's density, never more than the export itself.
  // Tiles re-compose once changes pause, and only once they come within a viewport's height of view.
  // ---------------------------------------------------------------------------------------------

  /** The scroller the tiles wrap in. Present while the All tab is shown. */
  @ViewChild('allViewport') allViewport?: ElementRef<HTMLElement>;

  /** The tiles, observed for visibility again whenever the set of figures changes. */
  @ViewChildren('allTile') allTileElements?: QueryList<ElementRef<HTMLElement>>;

  /** True while the tiles are attached and composing. Not bound in the template, so the view hooks may change it. */
  allActive = false;

  private allAttachQueued = false;

  /** What the reader asked the tiles to show: Fit height, or an explicit zoom. Never reaches an export. */
  allView: 'fitHeight' | number = 'fitHeight';

  /** The zoom at which one figure's full height fits the viewport, from the last measurement. */
  allFitHeightZoom = 1;

  /** The viewport's width less its padding, which decides how many tiles share the first row. */
  private allContentWidth = 0;

  /** The ratio tiles are sized and rasterised at, clamped as the Single tab clamps it. */
  private allDpr = 1;

  /** Each refused tile's reason, in the words the download refuses it in. */
  allTileRefusals: Record<string, string> = {};

  /** Tiles within one viewport height of view; every tile where IntersectionObserver is absent. */
  private allVisibleIds = new Set<string>();

  /** Tiles whose bitmap no longer matches the figure or its settings, composed once visible. */
  private allStaleIds = new Set<string>();

  /** The fraction of the target the painted tiles carry; a zoom needing another one re-composes. */
  private allPaintedRasterZoom: number | null = null;

  /** Bumped by every request, so a composition that finishes after a newer request is discarded. */
  private allGeneration = 0;

  private allComposeTimer: ReturnType<typeof setTimeout> | null = null;
  private allComposeFrame: number | null = null;
  private allResizeObserver: ResizeObserver | null = null;
  private allIntersectionObserver: IntersectionObserver | null = null;
  private allTileChanges: Subscription | null = null;

  /** The viewport height the IntersectionObserver's one-viewport margin was built for. */
  private allObservedHeight = 0;

  /** The export's own pixel size, which every tile shares; null while the size is unusable. */
  get allTargetPixels(): { width: number; height: number } | null {
    if (this.exportSizeError !== '') {
      return null;
    }
    const resolution = this.exportResolution;
    const density = this.exportDensity;
    return { width: Math.round(resolution.widthPx * density), height: Math.round(resolution.heightPx * density) };
  }

  get allZoomRange(): PreviewZoomRange {
    return previewZoomRange(this.allFitHeightZoom);
  }

  /** The requested view as device pixels per export pixel: 1 is 100 %, as on the Single tab. */
  get allZoomValue(): number {
    return clampPreviewZoom(this.allView === 'fitHeight' ? this.allFitHeightZoom : this.allView, this.allZoomRange);
  }

  get allTileCssWidth(): number {
    const pixels = this.allTargetPixels;
    return pixels ? pixels.width / this.allDpr * this.allZoomValue : 0;
  }

  get allTileCssHeight(): number {
    const pixels = this.allTargetPixels;
    return pixels ? pixels.height / this.allDpr * this.allZoomValue : 0;
  }

  /** How many tiles the first row holds; the rest defer their rendering until scrolled near. */
  get allTilesPerRow(): number {
    const width = this.allTileCssWidth;
    if (!(width > 0) || !(this.allContentWidth > 0)) {
      return 1;
    }
    return Math.max(1, Math.floor((this.allContentWidth + ALL_TILE_GAP) / (width + ALL_TILE_GAP)));
  }

  /** A deferred tile's `contain-intrinsic-size`, so the scroll height holds while it is skipped. */
  get allTileIntrinsicSize(): string {
    return `auto ${Math.round(this.allTileCssWidth)}px auto ${Math.round(this.allTileCssHeight)}px`;
  }

  get allSliderValue(): number {
    return zoomToSlider(this.allZoomValue, this.allZoomRange);
  }

  get allZoomLabel(): string {
    const zoom = formatPreviewZoom(this.allZoomValue);
    return this.allView === 'fitHeight' ? `${zoom} · Fit height` : zoom;
  }

  get allZoomValueText(): string {
    const percent = `${formatPreviewZoom(this.allZoomValue).replace('%', '')} percent`;
    return this.allView === 'fitHeight' ? `${percent}, fitted to the height` : percent;
  }

  get canZoomAllIn(): boolean {
    return canZoomPreviewIn(this.allZoomValue, this.allZoomRange);
  }

  get canZoomAllOut(): boolean {
    return canZoomPreviewOut(this.allZoomValue, this.allZoomRange);
  }

  zoomAllIn(): void {
    if (this.canZoomAllIn) {
      this.setAllView(nextPreviewZoomStop(this.allZoomValue, this.allFitHeightZoom, this.allZoomRange));
    }
  }

  zoomAllOut(): void {
    if (this.canZoomAllOut) {
      this.setAllView(previousPreviewZoomStop(this.allZoomValue, this.allFitHeightZoom, this.allZoomRange));
    }
  }

  onAllSliderInput(value: number): void {
    this.setAllView(sliderToZoom(value, this.allZoomRange));
  }

  fitAllHeight(): void {
    this.setAllView('fitHeight');
  }

  /** Applies a view at once, stretching the painted bitmaps; they re-compose where the raster changes. */
  setAllView(view: 'fitHeight' | number): void {
    const next = typeof view === 'number' ? clampPreviewZoom(view, this.allZoomRange) : view;
    if (next === this.allView) {
      return;
    }
    this.allView = next;
    this.onAllZoomChange();
    this.cdr.markForCheck();
  }

  /**
   * `+` / `=` zoom in, `-` out and `0` fits the height, anywhere in the panel but a form field — the
   * slider keeps its own keys. Unmodified keys only: Ctrl / ⌘ / Alt with them stay the browser's zoom.
   */
  onAllPanelKeydown(event: KeyboardEvent): void {
    if (event.ctrlKey || event.metaKey || event.altKey || this.isFormField(event.target)) {
      return;
    }
    const actions: Record<string, () => void> = {
      '+': () => this.zoomAllIn(),
      '=': () => this.zoomAllIn(),
      '-': () => this.zoomAllOut(),
      '0': () => this.fitAllHeight()
    };
    const action = actions[event.key];
    if (action) {
      event.preventDefault();
      action();
    }
  }

  /**
   * The viewport's border box in CSS px and the display's ratio, or null where it is absent.
   *
   * The border box rather than the client box, so a scrollbar that a zoom brings or takes away never
   * changes the fit it was zoomed to. Public so a spec can stand in a laid-out viewport.
   */
  measureAllViewport(): PreviewStage | null {
    const element = this.allViewport?.nativeElement;
    if (!element) {
      return null;
    }
    const box = element.getBoundingClientRect();
    return { width: box.width, height: box.height, devicePixelRatio: window.devicePixelRatio || 1 };
  }

  /**
   * Starts composing onto tiles that have just been rendered, at Fit height.
   *
   * The toolbar's tooltip anchors render behind the panel's @if, which the polyfill's first scan
   * never saw.
   */
  private attachAll(): void {
    const viewport = this.allViewport?.nativeElement;
    if (!viewport) {
      return;
    }
    this.allActive = true;
    this.allView = 'fitHeight';
    this.allTileRefusals = {};
    this.allPaintedRasterZoom = null;
    this.refreshAllGeometry();
    this.observeAllViewport(viewport);
    this.observeAllTiles();
    this.allTileChanges = this.allTileElements?.changes.subscribe(() => this.observeAllTiles()) ?? null;
    refreshAnchorPositioning();
    this.markAllStale();
    this.scheduleAllCompose();
    this.cdr.markForCheck();
  }

  /** Stops composing and forgets the view, so returning opens at Fit height again. */
  private detachAll(): void {
    this.teardownAll();
    this.allView = 'fitHeight';
    this.allTileRefusals = {};
    this.cdr.markForCheck();
  }

  /**
   * Cancels every timer, frame and observer and discards any composition in flight. Touches no bound
   * field, so the view hooks may call it once the panel has left the DOM.
   */
  private teardownAll(): void {
    this.allActive = false;
    this.allGeneration++;
    this.cancelScheduledAllCompose();
    this.allResizeObserver?.disconnect();
    this.allResizeObserver = null;
    this.allIntersectionObserver?.disconnect();
    this.allIntersectionObserver = null;
    this.allTileChanges?.unsubscribe();
    this.allTileChanges = null;
    this.allVisibleIds.clear();
    this.allStaleIds.clear();
    this.allPaintedRasterZoom = null;
  }

  /**
   * Measures the viewport: the display ratio, the row width and the Fit height zoom. A viewport with
   * no height left once padded — a closed dialog, a fixture never laid out — keeps the last fit.
   */
  private refreshAllGeometry(): void {
    const stage = this.measureAllViewport();
    if (!stage) {
      return;
    }
    this.allDpr = Math.min(4, Math.max(1, stage.devicePixelRatio));
    this.allContentWidth = Math.max(0, stage.width - 2 * ALL_VIEWPORT_PADDING);
    const pixels = this.allTargetPixels;
    if (pixels && stage.height > 2 * ALL_VIEWPORT_PADDING) {
      this.allFitHeightZoom = fitHeightZoom(stage.height, pixels.height, this.allDpr, ALL_VIEWPORT_PADDING);
    }
  }

  /**
   * Re-fits on every viewport resize — a window, a split screen, the sidebar, browser zoom. While
   * the reader has not zoomed, Fit height follows; an explicit zoom is kept. Absent outside a
   * browser, where the viewport is never laid out to begin with.
   */
  private observeAllViewport(viewport: HTMLElement): void {
    this.allResizeObserver?.disconnect();
    this.allResizeObserver = null;
    if (typeof ResizeObserver === 'undefined') {
      return;
    }
    this.allResizeObserver = new ResizeObserver(() => this.zone.run(() => {
      if (!this.allActive) {
        return;
      }
      const before = this.allZoomValue;
      this.refreshAllGeometry();
      if (Math.abs(viewport.getBoundingClientRect().height - this.allObservedHeight) >= 1) {
        this.observeAllTiles();
      }
      if (this.allZoomValue !== before) {
        this.onAllZoomChange();
      }
      this.cdr.markForCheck();
    }));
    this.allResizeObserver.observe(viewport, { box: 'border-box' });
  }

  /**
   * Watches which tiles are within one viewport height of view, so only those compose. Without
   * IntersectionObserver every tile counts as near.
   */
  private observeAllTiles(): void {
    this.allIntersectionObserver?.disconnect();
    this.allIntersectionObserver = null;
    const viewport = this.allViewport?.nativeElement;
    const tiles = this.allTileElements?.map(ref => ref.nativeElement) ?? [];
    const ids = new Set(tiles.map(tile => tile.dataset['figureId'] ?? '').filter(id => id !== ''));
    for (const id of [...this.allVisibleIds]) {
      if (!ids.has(id)) {
        this.allVisibleIds.delete(id);
      }
    }

    const height = viewport ? Math.max(0, Math.round(viewport.getBoundingClientRect().height)) : 0;
    this.allObservedHeight = height;
    if (!viewport || typeof IntersectionObserver === 'undefined') {
      let arrived = false;
      for (const id of ids) {
        if (!this.allVisibleIds.has(id)) {
          this.allVisibleIds.add(id);
          arrived = arrived || this.allStaleIds.has(id);
        }
      }
      if (arrived) {
        this.scheduleAllCompose();
      }
      return;
    }
    const observer = new IntersectionObserver(
      entries => this.zone.run(() => this.onAllTilesIntersect(entries)),
      { root: viewport, rootMargin: `${height}px 0px` }
    );
    tiles.forEach(tile => observer.observe(tile));
    this.allIntersectionObserver = observer;
  }

  private onAllTilesIntersect(entries: IntersectionObserverEntry[]): void {
    let arrived = false;
    for (const entry of entries) {
      const id = (entry.target as HTMLElement).dataset['figureId'];
      if (!id) {
        continue;
      }
      if (entry.isIntersecting) {
        if (!this.allVisibleIds.has(id)) {
          this.allVisibleIds.add(id);
          arrived = arrived || this.allStaleIds.has(id);
        }
      } else {
        this.allVisibleIds.delete(id);
      }
    }
    if (arrived) {
      this.scheduleAllCompose();
    }
  }

  /** Re-composes only where the new zoom needs a different raster; otherwise the bitmaps stretch. */
  private onAllZoomChange(): void {
    const pixels = this.allTargetPixels;
    const raster = pixels ? previewRasterZoom(this.allZoomValue, pixels.width, pixels.height).zoom : null;
    if (raster === null || this.allPaintedRasterZoom === null ||
        Math.abs(raster - this.allPaintedRasterZoom) > this.allPaintedRasterZoom * 1e-9) {
      this.markAllStale();
      this.scheduleAllCompose();
    }
  }

  private markAllStale(): void {
    this.allStaleIds = new Set(this.exportableCards.map(card => card.id));
  }

  /**
   * Composes the stale, visible tiles on the first animation frame after the changes have been
   * quiet for {@link ALL_COMPOSE_QUIET_MS}. Every request supersedes the one before it.
   */
  private scheduleAllCompose(): void {
    if (!this.allActive) {
      return;
    }
    this.allGeneration++;
    this.cancelScheduledAllCompose();
    this.allComposeTimer = setTimeout(() => {
      this.allComposeTimer = null;
      this.allComposeFrame = requestAnimationFrame(() => {
        this.allComposeFrame = null;
        void this.composeAllTiles();
      });
    }, ALL_COMPOSE_QUIET_MS);
  }

  private cancelScheduledAllCompose(): void {
    if (this.allComposeTimer !== null) {
      clearTimeout(this.allComposeTimer);
      this.allComposeTimer = null;
    }
    if (this.allComposeFrame !== null) {
      cancelAnimationFrame(this.allComposeFrame);
      this.allComposeFrame = null;
    }
  }

  /**
   * Composes every stale tile within reach at the current settings and paints it.
   *
   * The target layout is what Download would write, and it alone decides any refusal; the tile then
   * gets that same composition at the raster its box affords, as the Single stage does.
   */
  private async composeAllTiles(): Promise<void> {
    const generation = this.allGeneration;
    if (!this.allActive || this.allTargetPixels === null) {
      return;
    }
    const resolution = this.exportResolution;
    const density = this.exportDensity;
    const textScale = this.exportTextScale;
    const zoom = this.allZoomValue;
    const stage = this.allStage();
    const cards = this.exportableCards
      .filter(card => this.allStaleIds.has(card.id) && this.allVisibleIds.has(card.id));

    for (const card of cards) {
      const canvas = this.allTileCanvas(card.id);
      if (!canvas) {
        continue;
      }
      const chrome = this.exportChrome(card);
      const target = resolveFigureLayout(chrome, resolution, density, textScale);
      if (!target.layout) {
        this.setAllTileRefusal(card.id, target.refusal ?? '');
        this.paintTile(canvas, null);
        this.allStaleIds.delete(card.id);
        continue;
      }
      const fit = previewLayoutFor(target.layout, stage, zoom);
      if (!fit) {
        continue;
      }
      let composed: HTMLCanvasElement | null;
      try {
        composed = await this.composeFigure(card, chrome, fit.layout);
      } catch {
        composed = null;
      }
      if (generation !== this.allGeneration || !this.allActive) {
        return;
      }
      this.setAllTileRefusal(card.id, composed ? '' : `${card.title} could not be composed: its chart could not be built.`);
      this.paintTile(canvas, composed);
      this.allPaintedRasterZoom = fit.rasterZoom;
      this.allStaleIds.delete(card.id);
    }
    this.cdr.markForCheck();
  }

  /** The viewport's padded content box, at least 1 × 1, as the layout fitter takes a stage. */
  private allStage(): PreviewStage {
    const stage = this.measureAllViewport();
    return {
      width: Math.max(1, (stage?.width ?? 0) - 2 * ALL_VIEWPORT_PADDING),
      height: Math.max(1, (stage?.height ?? 0) - 2 * ALL_VIEWPORT_PADDING),
      devicePixelRatio: this.allDpr
    };
  }

  private allTileCanvas(id: string): HTMLCanvasElement | null {
    return this.allViewport?.nativeElement
      .querySelector<HTMLCanvasElement>(`canvas[data-figure-id="${CSS.escape(id)}"]`) ?? null;
  }

  private setAllTileRefusal(id: string, refusal: string): void {
    if ((this.allTileRefusals[id] ?? '') === refusal) {
      return;
    }
    const next = { ...this.allTileRefusals };
    if (refusal === '') {
      delete next[id];
    } else {
      next[id] = refusal;
    }
    this.allTileRefusals = next;
  }

  /** Draws a composition at its own bitmap size, or blanks the tile; its box is the tile's. */
  private paintTile(canvas: HTMLCanvasElement, composed: HTMLCanvasElement | null): void {
    if (!composed) {
      canvas.width = 0;
      canvas.height = 0;
      return;
    }
    canvas.width = composed.width;
    canvas.height = composed.height;
    canvas.getContext('2d')?.drawImage(composed, 0, 0);
  }

  /** `input`, `select`, `textarea` or editable text, whose keys are their own. */
  private isFormField(target: EventTarget | null): boolean {
    const element = target as HTMLElement | null;
    if (!element || typeof element.closest !== 'function') {
      return false;
    }
    return element.isContentEditable || element.closest('input, select, textarea') !== null;
  }

  // ---------------------------------------------------------------------------------------------
  // Table export
  //
  // The scope is every entry passing the current filters, in the current sort, across all pages —
  // never the visible page, which is why this reads `viewAll` rather than `view`. The status line
  // says so in as many words: a file holding ten of forty rows, with nothing on it to say which
  // ten, is worse than no file.
  // ---------------------------------------------------------------------------------------------

  tableExportFormat: TableExportFormat = 'xlsx';

  /** Nothing to write from an empty comparison, and never two writes at once. */
  get canExportTable(): boolean {
    return this.entries.length > 0 && !this.exporting;
  }

  onTableExportFormatChange(value: TableExportFormat): void {
    this.tableExportFormat = value;
  }

  /** The format select's option text for each format. */
  private static readonly TABLE_EXPORT_FORMAT_NAMES: Readonly<Record<TableExportFormat, string>> = {
    xlsx: 'Excel (.xlsx)', csv: 'CSV', tsv: 'TSV', md: 'Markdown',
    json: 'JSON', html: 'HTML', png: 'PNG', webp: 'WebP',
  };

  /** Names the download button after the chosen format; the ellipsis says a column dialog opens first. */
  get tableDownloadLabel(): string {
    const name = ModelComparisonComponent.TABLE_EXPORT_FORMAT_NAMES[this.tableExportFormat] ?? this.tableExportFormat;
    return `Download the table as ${name}…`;
  }

  /** Encodes the filtered, sorted, unpaged table in the chosen format and saves it. */
  async downloadTable(): Promise<void> {
    await this.writeTable(this.entryTable.viewAll(this.entries));
  }

  // ---------------------------------------------------------------------------------------------
  // The download column chooser
  //
  // The exported table carries twenty-six columns, of which a given comparison populates rather
  // fewer: a comparable, fully priced set leaves the differing-key and scheduled-price columns
  // empty on every row. The chooser opens with exactly the populated ones ticked and marks the rest
  // as empty, so a reader who wants them has to ask for them and never has to guess which blank
  // column is a missing measure and which is a column this set never fills.
  // ---------------------------------------------------------------------------------------------

  @ViewChild('tableColumnsDialog') tableColumnsDialogRef?: ElementRef<HTMLDialogElement>;

  /** Every exportable column, in the order the file writes them. */
  readonly tableColumns = COMPARISON_TABLE_COLUMNS;

  /** Keys chosen in the column dialog; null until the first download seeds it. */
  private tableColumnSelection: Set<string> | null = null;

  /**
   * The rows the open dialog will write, captured when it opened so a filter change mid-dialog
   * cannot desync them.
   */
  private pendingTableRows: BenchmarkModelComparisonEntryDto[] = [];

  /** Columns with no value on any row of the captured set, marked as empty in the chooser. */
  tableColumnEmpty = new Set<string>();

  /** The control the chooser was opened from, so focus returns where the reader left it. */
  private tableColumnTrigger: HTMLElement | null = null;

  openTableColumnDialog(event?: Event): void {
    if (!this.canExportTable) {
      return;
    }
    this.tableColumnTrigger = (event?.currentTarget as HTMLElement | null) ?? null;

    const rows = this.entryTable.viewAll(this.entries);
    this.pendingTableRows = rows;
    const populated = populatedColumnKeys(buildComparisonTableModel(rows, this.tableProvenance));
    const populatedKeys = new Set(populated);
    this.tableColumnEmpty = new Set(
      this.tableColumns.filter(column => !populatedKeys.has(column.key)).map(column => column.key)
    );

    // Seeded once and then kept for the wizard's lifetime: a reader who ticked four columns for one
    // download wants the same four for the next, not the default back again.
    if (this.tableColumnSelection === null) {
      this.tableColumnSelection = new Set(populated);
    }

    // The checkboxes are rendered from state this method has just changed, so they have to hold it
    // before the dialog is promoted to the top layer.
    this.cdr.detectChanges();
    this.tableColumnsDialogRef?.nativeElement.showModal();
  }

  isTableColumnSelected(key: string): boolean {
    return this.tableColumnSelection?.has(key) ?? false;
  }

  toggleTableColumn(key: string): void {
    const selection = this.tableColumnSelection ?? new Set<string>();
    if (selection.has(key)) {
      selection.delete(key);
    } else {
      selection.add(key);
    }
    this.tableColumnSelection = selection;
    this.cdr.markForCheck();
  }

  selectAllTableColumns(): void {
    this.tableColumnSelection = new Set(this.tableColumns.map(column => column.key));
    this.cdr.markForCheck();
  }

  selectPopulatedTableColumns(): void {
    this.tableColumnSelection = new Set(
      this.tableColumns
        .filter(column => !this.tableColumnEmpty.has(column.key))
        .map(column => column.key)
    );
    this.cdr.markForCheck();
  }

  get tableColumnSelectedCount(): number {
    return this.tableColumnSelection?.size ?? 0;
  }

  /** Closes the chooser and writes the rows it was opened over, in the chosen columns. */
  async confirmTableDownload(): Promise<void> {
    const rows = this.pendingTableRows.length > 0
      ? this.pendingTableRows
      : this.entryTable.viewAll(this.entries);
    this.tableColumnsDialogRef?.nativeElement.close();
    await this.writeTable(rows);
  }

  /**
   * Keeps the chooser's own close and cancel events off the wizard dialog that contains it, and
   * returns focus to the control it was opened from.
   *
   * The host closes the whole wizard from its own dialog's `close`, and this one is a descendant of
   * it. Escape fires `cancel` and then `close`; only the second is acted on, because the chooser is
   * still modal during the first. `cancel` is never prevented — a close request the dialog refuses
   * to honour is a trapped reader.
   */
  onTableColumnsDialogClose(event: Event): void {
    event.stopPropagation();
    if (event.type !== 'close') {
      return;
    }
    const trigger = this.tableColumnTrigger;
    this.tableColumnTrigger = null;
    if (trigger?.isConnected) {
      trigger.focus();
    }
    this.cdr.markForCheck();
  }

  /**
   * The columns one download writes: what the chooser holds, or every populated column where the
   * chooser has never been opened.
   *
   * Both branches come out in the order {@link COMPARISON_TABLE_COLUMNS} declares, which is the
   * order the file is read in; the order a reader ticked boxes in is not it.
   */
  private tableColumnKeys(rows: readonly BenchmarkModelComparisonEntryDto[]): string[] {
    const selection = this.tableColumnSelection;
    if (selection !== null) {
      return this.tableColumns.filter(column => selection.has(column.key)).map(column => column.key);
    }
    return populatedColumnKeys(buildComparisonTableModel(rows, this.tableProvenance));
  }

  /** The one write path, shared by the direct download and by the chooser's Download. */
  private async writeTable(rows: readonly BenchmarkModelComparisonEntryDto[]): Promise<void> {
    if (!this.canExportTable) {
      return;
    }
    const format = this.tableExportFormat;
    this.exporting = true;
    this.exportNotice = null;
    this.cdr.markForCheck();

    try {
      const columnKeys = this.tableColumnKeys(rows);
      const model = buildComparisonTableModel(rows, this.tableProvenance, columnKeys);
      const encoded = await encodeComparisonTable(model, format, { webpQuality: this.tableWebpQuality });
      const filename = tableExportFilename(encoded.format);
      saveFigureBlob(encoded.blob, filename);

      const noun = rows.length === 1 ? 'entry' : 'entries';
      const fallback = encoded.fellBackToPng
        ? ' This browser cannot encode WebP, so the file was written as PNG.'
        : '';
      this.announce(
        `Table saved as ${filename} — ${rows.length} ${noun}, current sort, filters applied, ` +
        `all pages, ${columnKeys.length} of ${this.tableColumns.length} columns.${fallback}`,
        'success'
      );
    } catch {
      this.announce('The table could not be exported.', 'error');
    } finally {
      this.exporting = false;
      this.cdr.markForCheck();
    }
  }

  /**
   * Copies the same rows as a GFM table.
   *
   * Markdown rather than the chosen format: the clipboard's destination is a document or a chat
   * message, and a pasted spreadsheet or a pasted HTML document is not one. Both the refusal and
   * the absence of the API land as inline text — a bare console error tells the reader nothing.
   */
  async copyTableMarkdown(): Promise<void> {
    if (!this.canExportTable) {
      return;
    }
    const rows = this.entryTable.viewAll(this.entries);
    this.exporting = true;
    this.exportNotice = null;
    this.cdr.markForCheck();

    try {
      const clipboard = navigator.clipboard as Clipboard | undefined;
      if (!clipboard || typeof clipboard.writeText !== 'function') {
        this.announce(
          'This browser cannot copy text to the clipboard — download the table instead.',
          'error'
        );
        return;
      }
      await clipboard.writeText(toMarkdown(buildComparisonTableModel(rows, this.tableProvenance)));
      this.announce(
        `Copied ${rows.length} ${rows.length === 1 ? 'entry' : 'entries'} as Markdown.`,
        'success'
      );
    } catch {
      this.announce('The clipboard write was refused.', 'error');
    } finally {
      this.exporting = false;
      this.cdr.markForCheck();
    }
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
    const notices = this.setFigureNotes.map(note => note.text);

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

  /**
   * The set-level facts that qualify every figure, tagged for the figure chrome: the cap and the
   * exclusions are warnings, strict withholding and deselection are informational, and the plotted
   * entries' question coverage closes the list (see {@link questionCoverageNotes}). Appended to every
   * card's own notes on export and on preview, and the first of {@link setNotices}.
   */
  get setFigureNotes(): FigureNote[] {
    const notes: FigureNote[] = (this.figures?.selection.notices ?? [])
      .map(text => ({ text, tone: 'warning' as const }));

    const withheld = this.strictlyWithheldEntries;
    if (withheld.length > 0) {
      notes.push({
        text: `Strict comparability is on: ${withheld.length} degraded ` +
          `${withheld.length === 1 ? 'entry is' : 'entries are'} in the table only ` +
          `(${withheld.map(e => e.label).join(', ')}).`,
        tone: 'info'
      });
    }

    const deselected = this.deselectedEntries;
    if (deselected.length > 0) {
      notes.push({
        text: `${deselected.length} ${deselected.length === 1 ? 'entry is' : 'entries are'} deselected and ` +
          `not plotted: ${deselected.map(e => e.label).join(', ')}.`,
        tone: 'info'
      });
    }

    const plottedKeys = new Set(this.plotted.map(entry => entry.key));
    notes.push(...questionCoverageNotes(this.entries.filter(entry => plottedKeys.has(entry.key))));

    return notes;
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
  chartAriaLabel(spec: { chrome: FigureChrome } | null | undefined): string {
    if (!spec) {
      return '';
    }
    const chrome = spec.chrome;
    return `${chrome.title}: ${figureSummary(chrome)}. Values for every entry are in the comparison table below.`;
  }

  /** A DOM id and anchor name derived from an entry key, which carries a `run:12` style colon. */
  tipId(prefix: string, key: string): string {
    return `mc-tip-${prefix}-${key.replace(/[^A-Za-z0-9_-]/g, '-')}`;
  }

  stateLabel(entry: BenchmarkModelComparisonEntryDto): string {
    return comparisonStateLabel(entry);
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

  // The three cell formats live in `table-export`, not here: an exported table and the screen it
  // was taken from have to agree character for character, including what an absent measure is
  // printed as, and one of the two would drift the moment there were two copies of the rule.

  formatIndex(value: number | null | undefined): string {
    return formatIndexText(value);
  }

  formatMs(value: number | null | undefined): string {
    return formatMsText(value);
  }

  formatUsd(value: number | null | undefined): string {
    return formatUsdText(value);
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
      orientation: this.effectiveOrientation,
      directLabels: this.scatterDirectLabels,
      inlineValues: this.scatterInlineValues,
      reducedMotion: this.reducedMotion.matches,
      highlightedKey: this.highlightedKey,
      selectedKeys: this.emphasisKeys,
      glyphSource: this.chartEntries,
      style: this.figureStyle
    });

    const plotted = this.figures.selection.plotted;
    this.profileAxes = plotted.length >= 2
      ? normalizeProfile(plotted, {
        context: this.context,
        speedMeasure: this.speedMeasure,
        costMeasure: this.costMeasure,
        numbers: this.figureStyle.numbers
      })
      : null;

    const sampleOptions = {
      context: this.context,
      speedMeasure: this.speedMeasure,
      costMeasure: this.costMeasure,
      style: this.figureStyle
    };
    this.numberSamples = {
      bar: buildNumberSamples(plotted, sampleOptions, 'bar'),
      scatter: buildNumberSamples(plotted, sampleOptions, 'scatter'),
      profile: buildNumberSamples(plotted, sampleOptions, 'profile')
    };

    // A refetch can take the previewed figure out of the set.
    if (this.previewCardId !== null && this.previewCard === null) {
      this.previewCardId = this.exportableCards[0]?.id ?? null;
    }

    // Every caller of this method changes what the template renders, and several of them are
    // outside change detection: a filter control, the reduced-motion listener, the resize
    // observer. Marking here is what makes the figures, the notices and the table agree.
    this.cdr.markForCheck();
    // No-ops unless the Single stage or the All tiles are composing, which follow every rebuild.
    this.schedulePreview();
    if (this.allActive) {
      this.markAllStale();
      this.scheduleAllCompose();
    }
  }

  /**
   * Watches the figure views' own width, not the window's: below {@link P1_STACK_BREAKPOINT_PX}
   * P1's bars turn horizontal, so eight model names read as left-aligned labels.
   */
  private observeContainerWidth(): void {
    const host = this.chartsHost?.nativeElement;
    this.resizeObserver?.disconnect();
    this.resizeObserver = null;
    this.observedChartsHost = host ?? null;
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
