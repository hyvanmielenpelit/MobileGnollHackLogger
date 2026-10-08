import {
  AfterViewInit,
  ChangeDetectionStrategy,
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
import { Subscription } from 'rxjs';

import { PaneResizerComponent } from '../../../../shared/pane-resizer/pane-resizer.component';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../../utils/polyfills.util';
import { ExportFormatSectionComponent } from '../../model-comparison/export-format-section.component';
import { ExportSizeSectionComponent } from '../../model-comparison/export-size-section.component';
import {
  DEFAULT_WEBP_QUALITY,
  FigureArchiveEntry,
  FigureExportFormat,
  FigureExportResult,
  OffscreenPlotConfig,
  WEBP_QUALITY_OPTIONS,
  WebpQuality,
  buildFigureArchive,
  copyImageToClipboard,
  densityPercentLabel,
  displayDensity,
  encodeFigureImage,
  renderPlotOffscreen,
  saveFigureBlob
} from '../../model-comparison/figure-export';
import {
  FigureSizeSettings,
  defaultFigureSize,
  readStoredSizeSettings,
  resolveSizeDensity,
  resolveSizeResolution,
  sameFigureSize,
  sizeErrors,
  writeStoredSizeSettings
} from '../../model-comparison/figure-size';
import { FigureLogo, ensureFigureLogo, figureLogoAspect } from '../../model-comparison/figure-logo';
import {
  PREVIEW_SLIDER_STEPS,
  PreviewZoomRange,
  canZoomPreviewIn,
  canZoomPreviewOut,
  formatPreviewZoom,
  nextPreviewZoomStop,
  previousPreviewZoomStop,
  sliderToZoom,
  zoomToSlider
} from '../../model-comparison/preview-view';
import { CcAnnotationsPanelComponent } from '../annotations/annotations-panel.component';
import {
  CC_FIGURE_KEYS,
  CC_FIGURE_SERIES,
  CC_HEADER_LOGO_PX,
  CC_SCREEN_THEME,
  CcChartOptions,
  CcChartTheme,
  CcFigure,
  CcFigureInput,
  CcFigureKey,
  CcFigureSeries,
  buildCcFigure,
  prefersReducedMotion
} from '../chat-consistency-charts';
import {
  CcEventChange,
  CcEventDay,
  CcEventGroup,
  CcMarkerFilter,
  CcMarkerKind,
  CcServedChange,
  buildEventDays,
  eventKindSummary,
  groupOverseerEvents,
  servedModelChanges
} from '../chat-consistency-events';
import { formatUtcDate } from '../chat-consistency-format';
import { CC_INCLUSION_TEXT, CcRunInclusion, unitNoun } from '../chat-consistency-scope';
import { CcBatteryRunRow, CcModelAxis, CcTimeline, CcTimelinePoint, CcUnitKind } from '../chat-consistency.models';
import { CcEventListComponent } from '../event-list/cc-event-list.component';
import { CcChartFigureComponent } from './cc-chart-figure.component';
import { CcExportPlan, ccChartArchiveFilename, ccChartFilename, ccExportLayout, ccExportTheme } from './cc-chart-export';
import {
  CcChartBox,
  CcZoomView,
  ccCanvasRatio,
  ccChartBox,
  ccFitHeightZoom,
  ccFitScreenZoom,
  ccFitWidthZoom,
  ccResolveZoom,
  ccZoomRange
} from './cc-chart-zoom';

// --- Stored layout ---

export type CcTimelineSidebarTab = 'data' | 'events' | 'annotations' | 'download';
export type CcTimelineViewTab = 'all' | 'single';
export type CcImageTheme = 'screen' | 'print';
/** In a battery set, what one point is: a battery run, or one of its member runs. */
export type CcPlotBy = 'batteryRuns' | 'memberRuns';

/** The workspace layout and the chart settings, per browser. Read and written in `try/catch`. */
export const CC_TIMELINE_STORAGE_KEY = 'overseer.benchmark.chatConsistency.timeline';

/** The chart size, per browser, apart from Model Comparison's own. */
export const CC_CHART_SIZE_STORAGE_KEY = 'overseer.benchmark.chatConsistency.chartSize';

/** The settings sidebar's width, in CSS px: 26 rem by default, adjustable from 18 rem to 40 rem or half the workspace. */
export const CC_SIDEBAR_WIDTH_DEFAULT = 416;
export const CC_SIDEBAR_WIDTH_MIN = 288;
export const CC_SIDEBAR_WIDTH_MAX = 640;

export const CC_TIMELINE_SIDEBAR_TABS: readonly { readonly id: CcTimelineSidebarTab; readonly label: string }[] = [
  { id: 'data', label: 'Data' },
  { id: 'events', label: 'Events' },
  { id: 'annotations', label: 'Annotations' },
  { id: 'download', label: 'Download' }
];

export const CC_TIMELINE_VIEW_TABS: readonly { readonly id: CcTimelineViewTab; readonly label: string }[] = [
  { id: 'all', label: 'All charts' },
  { id: 'single', label: 'Single chart' }
];

/** The marker kinds the Events tab shows or hides on the charts, in marker order. */
export const CC_MARKER_KIND_OPTIONS: readonly { readonly kind: CcMarkerKind; readonly label: string }[] = [
  { kind: 'event', label: 'Overseer changes' },
  { kind: 'annotation', label: 'Annotations' },
  { kind: 'served', label: 'Served-model changes' }
];

export interface CcTimelineLayout {
  readonly sidebarCollapsed: boolean;
  readonly sidebarWidth: number;
  readonly sidebarTab: CcTimelineSidebarTab;
  readonly view: CcTimelineViewTab;
  /** The charts shown, in `CC_FIGURE_KEYS` order. */
  readonly figures: readonly CcFigureKey[];
  /** Series ids (`CC_FIGURE_SERIES`) left out of the drawing. */
  readonly hiddenSeries: readonly string[];
  readonly markerKinds: readonly CcMarkerKind[];
  readonly hiddenEventKinds: readonly string[];
  readonly zeroBaseline: boolean;
  /** Runs not in the analysis are drawn as gray crosses with dotted segments. */
  readonly markNotAnalyzed: boolean;
  readonly imageTheme: CcImageTheme;
  readonly imageFormat: FigureExportFormat;
  readonly webpQuality: WebpQuality;
  readonly chartSizeOpen: boolean;
  readonly imageFormatOpen: boolean;
  readonly singleFigure: CcFigureKey;
  /** The GnollBench logo on the charts and in every image. */
  readonly logo: boolean;
}

/** The version `writeStoredTimelineLayout` writes. Version 1 had one *Work per answer* chart, `work`, with `work.tools`. */
export const CC_TIMELINE_LAYOUT_VERSION = 2;

const FIGURE_KEY_ORDER: readonly CcFigureKey[] = CC_FIGURE_KEYS.map(entry => entry.key);
const SERIES_IDS: ReadonlySet<string> = new Set(
  Object.values(CC_FIGURE_SERIES).flatMap(series => series.map(entry => entry.id)));
const MARKER_KINDS: readonly CcMarkerKind[] = CC_MARKER_KIND_OPTIONS.map(option => option.kind);

/** Every chart, series and marker shown; the sidebar open at 416 px on Data; All charts; PNG at 85. */
export function defaultTimelineLayout(): CcTimelineLayout {
  return {
    sidebarCollapsed: false,
    sidebarWidth: CC_SIDEBAR_WIDTH_DEFAULT,
    sidebarTab: 'data',
    view: 'all',
    figures: FIGURE_KEY_ORDER,
    hiddenSeries: [],
    markerKinds: MARKER_KINDS,
    hiddenEventKinds: [],
    zeroBaseline: false,
    markNotAnalyzed: true,
    imageTheme: 'screen',
    imageFormat: 'png',
    webpQuality: DEFAULT_WEBP_QUALITY,
    chartSizeOpen: false,
    imageFormatOpen: false,
    singleFigure: 'quality',
    logo: true
  };
}

/** The values of `value` that `accept` takes, in `order` where one is given; null when it is not an array. */
function listOf<T extends string>(value: unknown, accept: (item: string) => boolean, order?: readonly T[]): T[] | null {
  if (!Array.isArray(value)) return null;
  const items = new Set(value.filter((item): item is string => typeof item === 'string' && accept(item)));
  return order ? order.filter(item => items.has(item)) : [...items] as T[];
}

/**
 * A stored layout read field by field: a missing field, or one of the wrong kind, takes its default.
 * A layout before version 2 that shows `work` shows `tools` too; its retired `work.tools` series id is
 * dropped with every other unknown id.
 */
export function parseTimelineLayout(stored: unknown): CcTimelineLayout {
  const fallback = defaultTimelineLayout();
  if (stored === null || typeof stored !== 'object' || Array.isArray(stored)) return fallback;
  const record = { ...(stored as Record<string, unknown>) };
  const version = typeof record['version'] === 'number' ? record['version'] as number : 1;
  if (version < CC_TIMELINE_LAYOUT_VERSION && Array.isArray(record['figures']) && record['figures'].includes('work')) {
    record['figures'] = [...record['figures'], 'tools'];
  }
  const flag = (key: string, otherwise: boolean): boolean => typeof record[key] === 'boolean' ? record[key] as boolean : otherwise;
  const oneOf = <T extends string | number>(key: string, options: readonly T[], otherwise: T): T =>
    options.includes(record[key] as T) ? record[key] as T : otherwise;
  const width = record['sidebarWidth'];
  return {
    sidebarCollapsed: flag('sidebarCollapsed', fallback.sidebarCollapsed),
    sidebarWidth: typeof width === 'number' && Number.isFinite(width)
      ? Math.min(CC_SIDEBAR_WIDTH_MAX, Math.max(CC_SIDEBAR_WIDTH_MIN, Math.round(width)))
      : fallback.sidebarWidth,
    sidebarTab: oneOf('sidebarTab', CC_TIMELINE_SIDEBAR_TABS.map(tab => tab.id), fallback.sidebarTab),
    view: oneOf('view', CC_TIMELINE_VIEW_TABS.map(tab => tab.id), fallback.view),
    figures: listOf<CcFigureKey>(record['figures'], item => FIGURE_KEY_ORDER.includes(item as CcFigureKey), FIGURE_KEY_ORDER)
      ?? fallback.figures,
    hiddenSeries: listOf<string>(record['hiddenSeries'], item => SERIES_IDS.has(item)) ?? fallback.hiddenSeries,
    markerKinds: listOf<CcMarkerKind>(record['markerKinds'], item => MARKER_KINDS.includes(item as CcMarkerKind), MARKER_KINDS)
      ?? fallback.markerKinds,
    hiddenEventKinds: listOf<string>(record['hiddenEventKinds'], item => item !== '') ?? fallback.hiddenEventKinds,
    zeroBaseline: flag('zeroBaseline', fallback.zeroBaseline),
    markNotAnalyzed: flag('markNotAnalyzed', fallback.markNotAnalyzed),
    imageTheme: oneOf<CcImageTheme>('imageTheme', ['screen', 'print'], fallback.imageTheme),
    imageFormat: oneOf<FigureExportFormat>('imageFormat', ['png', 'webp'], fallback.imageFormat),
    webpQuality: oneOf<WebpQuality>('webpQuality', WEBP_QUALITY_OPTIONS, fallback.webpQuality),
    chartSizeOpen: flag('chartSizeOpen', fallback.chartSizeOpen),
    imageFormatOpen: flag('imageFormatOpen', fallback.imageFormatOpen),
    singleFigure: oneOf('singleFigure', FIGURE_KEY_ORDER, fallback.singleFigure),
    logo: flag('logo', fallback.logo)
  };
}

/** The stored layout; the default wherever storage is absent, unreadable or throws. */
export function readStoredTimelineLayout(): CcTimelineLayout {
  try {
    const raw = localStorage.getItem(CC_TIMELINE_STORAGE_KEY);
    return parseTimelineLayout(raw === null ? null : JSON.parse(raw));
  } catch {
    return defaultTimelineLayout();
  }
}

export function writeStoredTimelineLayout(layout: CcTimelineLayout): void {
  try {
    localStorage.setItem(CC_TIMELINE_STORAGE_KEY, JSON.stringify({ version: CC_TIMELINE_LAYOUT_VERSION, ...layout }));
  } catch {
    // Private mode or blocked storage: the layout still applies for this session.
  }
}

/** `every date`, `2026-09-01 to 2026-10-07`, `from 2026-09-01`, `until 2026-10-07`. */
export function ccRangeText(fromUtc: string | null | undefined, toUtc: string | null | undefined): string {
  const from = fromUtc ? formatUtcDate(fromUtc) : null;
  const to = toUtc ? formatUtcDate(toUtc) : null;
  if (from && to) return from === to ? from : `${from} to ${to}`;
  if (from) return `from ${from}`;
  if (to) return `until ${to}`;
  return 'every date';
}

function eventItemCount(days: readonly CcEventDay[]): number {
  return days.reduce((sum, day) => sum + day.items.length, 0);
}

function figureTitle(key: CcFigureKey): string {
  return CC_FIGURE_KEYS.find(entry => entry.key === key)?.title ?? key;
}

/** One copy, download or archive attempt: the encoded image, or why there is none. */
interface CcExportSnapshot {
  readonly plan: CcExportPlan;
  readonly sizeError: string;
  readonly input: CcFigureInput | null;
  readonly theme: CcChartTheme;
  readonly hiddenSeries: ReadonlySet<string>;
  readonly zeroBaseline: boolean;
  readonly subject: string | null;
  readonly logo: FigureLogo | null;
  readonly webpQuality: WebpQuality;
  readonly modelKey: string;
}

interface CcChartImage {
  readonly result: FigureExportResult | null;
  /** The size is refused; the same for every chart. */
  readonly refusal: string | null;
  /** The chart has nothing to draw with the current series and range. */
  readonly empty: boolean;
}

/** The figure's chrome around its chart box, in CSS px, until the first tile is measured. */
const DEFAULT_CHROME = { width: 26, height: 120 } as const;

/** The sidebar tab row's height until it is measured. */
const DEFAULT_TAB_BAR_HEIGHT = 44;

/** Below this, a measured length is the same as the last one. */
const MEASURE_TOLERANCE = 0.5;

/**
 * The Timeline step of the Chat Consistency wizard: a resizable, collapsible settings sidebar (Data,
 * Events, Annotations, Download) beside the *All charts* and *Single chart* views of the live charts,
 * with zoom, Copy, Download and Download all. The chart size sets the charts' shape on screen as in
 * the downloads: at 100 % zoom a chart's box is its download's layout box.
 */
@Component({
  selector: 'app-cc-timeline-workspace',
  standalone: true,
  imports: [
    CcAnnotationsPanelComponent,
    CcChartFigureComponent,
    CcEventListComponent,
    ExportFormatSectionComponent,
    ExportSizeSectionComponent,
    PaneResizerComponent
  ],
  templateUrl: './timeline-workspace.component.html',
  styleUrls: ['./timeline-workspace.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcTimelineWorkspaceComponent implements OnInit, OnChanges, AfterViewInit, OnDestroy {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly zone = inject(NgZone);

  @Input() axis: CcModelAxis | null = null;
  @Input() timeline: CcTimeline | null = null;
  @Input() loading = false;
  @Input() error: string | null = null;
  /** The runs of the timeline that are not in the analysis, with why; null or empty when every run is. */
  @Input() notAnalyzed: ReadonlyMap<number, CcRunInclusion> | null = null;
  /** The step-1 dates as step 1 names them, for the readout; the timeline's bounds when empty. */
  @Input() rangeLabel = '';
  /** What step 1 counts: battery runs in a battery set, which the charts then plot; runs otherwise. */
  @Input() unitKind: CcUnitKind = 'run';
  /** The compared set's key; its battery points are the ones plotted in a battery set. */
  @Input() setKey: string | null = null;
  /** The battery runs of the compared set, for the member runs' suite names. */
  @Input() batteryRows: readonly CcBatteryRunRow[] = [];
  /** The battery runs of the set not in the analysis, keyed by battery run id, with why. */
  @Input() notAnalyzedUnits: ReadonlyMap<number, CcRunInclusion> | null = null;
  /** The model and the compared set, `Claude 5.5 Haiku (xhigh) · Two initial suites (revision 1)`; the charts' subject line adds what a point is. */
  @Input() subjectLabel = '';

  /** An annotation was added or deleted; the host reads the timeline again. */
  @Output() readonly annotationsChanged = new EventEmitter<void>();
  /** `exporting` changed; the host's close guard reads it. */
  @Output() readonly exportingChange = new EventEmitter<boolean>();

  /** Set while a copy, a download or *Download all* runs; the dialog must not close meanwhile. */
  exporting = false;

  readonly sidebarTabs = CC_TIMELINE_SIDEBAR_TABS;
  readonly viewTabs = CC_TIMELINE_VIEW_TABS;
  readonly figureKeys = CC_FIGURE_KEYS;
  readonly markerKindOptions = CC_MARKER_KIND_OPTIONS;
  readonly sliderSteps = PREVIEW_SLIDER_STEPS;
  readonly SIDEBAR_WIDTH_MIN = CC_SIDEBAR_WIDTH_MIN;
  readonly SIDEBAR_WIDTH_DEFAULT = CC_SIDEBAR_WIDTH_DEFAULT;
  readonly textSizeHint = '100 % is the text size the charts have on screen at 100 % zoom.';
  readonly formatNote = 'Every chart is downloaded in this format. Copy always writes a PNG.';

  /** Sampled once: the density option the size section marks *(this display)*. */
  readonly displayDensity = displayDensity();
  readonly defaultChartSize: FigureSizeSettings = defaultFigureSize(this.displayDensity);
  readonly defaultWebpQuality = DEFAULT_WEBP_QUALITY;

  private readonly reducedMotion = prefersReducedMotion();
  private readonly stored = readStoredTimelineLayout();

  // --- Layout state ---

  sidebarCollapsed = this.stored.sidebarCollapsed;
  sidebarWidth = this.stored.sidebarWidth;
  sidebarWidthMax = CC_SIDEBAR_WIDTH_MAX;
  sidebarTab: CcTimelineSidebarTab = this.stored.sidebarTab;
  /** The Annotations panel, once shown, stays mounted. */
  annotationsMounted = this.sidebarTab === 'annotations';
  view: CcTimelineViewTab = this.stored.view;
  chartSizeOpen = this.stored.chartSizeOpen;
  imageFormatOpen = this.stored.imageFormatOpen;
  /** The sticky sidebar tab row's height, the event list's sticky day-heading offset. */
  tabBarHeight = DEFAULT_TAB_BAR_HEIGHT;

  // --- Chart settings ---

  private shownFigures = new Set<CcFigureKey>(this.stored.figures);
  hiddenSeries = new Set<string>(this.stored.hiddenSeries);
  markerKinds = new Set<CcMarkerKind>(this.stored.markerKinds);
  hiddenEventKinds = new Set<string>(this.stored.hiddenEventKinds);
  zeroBaseline = this.stored.zeroBaseline;
  markNotAnalyzed = this.stored.markNotAnalyzed;
  imageTheme: CcImageTheme = this.stored.imageTheme;
  imageFormat: FigureExportFormat = this.stored.imageFormat;
  webpQuality: WebpQuality = this.stored.webpQuality;
  showLogo = this.stored.logo;
  private singleKey: CcFigureKey = this.stored.singleFigure;
  /** In a battery set, what a point is; kept per component, since the set changes with step 1. */
  plotBy: CcPlotBy = 'batteryRuns';
  /** The decoded wide logo; null until it loads, and when it fails to. */
  private logoImage: FigureLogo | null = null;

  chartSize: FigureSizeSettings = readStoredSizeSettings(CC_CHART_SIZE_STORAGE_KEY, this.defaultChartSize, false);
  /** The chart box at 100 %: the last usable size's layout box. */
  private box: CcChartBox = ccChartBox(sizeErrors(this.chartSize, 'chart').any === '' ? this.chartSize : this.defaultChartSize);

  // --- Derived from the timeline and the settings ---

  /** The shown charts in order, drawn in the screen theme. */
  figures: CcFigure[] = [];
  /** The series each chart draws with none hidden, by chart; what the *Series* checklist offers. */
  private presentSeries: Partial<Record<CcFigureKey, CcFigureSeries[]>> = {};
  /** Shown charts with more than one series, for the *Series* checklist. */
  seriesGroups: { key: CcFigureKey; title: string; series: CcFigureSeries[] }[] = [];
  private eventGroups: CcEventGroup[] = [];
  private servedChanges: CcServedChange[] = [];
  /** The Overseer change kinds present, with the number of composite events holding each. */
  kindSummary: CcEventChange[] = [];
  eventDays: CcEventDay[] = [];
  /** Event list items the filters hide. */
  hiddenEventItems = 0;
  private totalEventItems = 0;

  /** The outcome of the last copy or download. */
  exportStatus = '';

  // --- Zoom ---

  private allView: CcZoomView = 'fitScreen';
  private singleView: CcZoomView = 'fitScreen';
  /** The viewport's content box, in CSS px; null until it has been measured with a size. */
  private viewportBox: { width: number; height: number } | null = null;
  private chrome: { width: number; height: number } = { ...DEFAULT_CHROME };
  /** Tiles within one viewport height of the All view; every tile without IntersectionObserver. */
  private nearKeys = new Set<CcFigureKey>();
  private readonly nearAll = typeof IntersectionObserver === 'undefined';

  @ViewChild('workspace') private workspaceRef?: ElementRef<HTMLElement>;
  @ViewChild('sideTabs') private sideTabsRef?: ElementRef<HTMLElement>;
  @ViewChildren('tile') private tileRefs?: QueryList<ElementRef<HTMLElement>>;

  /** The shown view's scroller; observed for its size and, in All, for the tiles near it. */
  @ViewChild('viewport')
  set viewportRef(ref: ElementRef<HTMLElement> | undefined) {
    const element = ref?.nativeElement ?? null;
    if (element === this.viewportEl) return;
    this.viewportEl = element;
    this.observeViewport();
  }

  private viewportEl: HTMLElement | null = null;
  private viewportObserver: ResizeObserver | null = null;
  private tileObserver: IntersectionObserver | null = null;
  private tabBarObserver: ResizeObserver | null = null;
  private tileChanges: Subscription | null = null;
  private measureFrame: number | null = null;
  private measureFollowUps = 0;
  private zoomFrame: number | null = null;
  private pendingZoom: number | null = null;
  private destroyed = false;

  // --- Lifecycle ---

  ngOnInit(): void {
    ensureOverlayPolyfills();
    void ensureFigureLogo('wide').then(image => {
      if (!image || this.destroyed) return;
      this.logoImage = { image, aspectRatio: figureLogoAspect('wide'), heightPx: CC_HEADER_LOGO_PX };
      if (this.showLogo) {
        this.rebuildFigures();
        this.cdr.markForCheck();
        this.scheduleMeasure();
      }
    });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['timeline'] || changes['unitKind'] || changes['setKey']) {
      if (changes['setKey'] && !changes['setKey'].firstChange) this.plotBy = 'batteryRuns';
      this.rebuildTimeline();
      this.rebuildFigures();
      this.rebuildEvents();
      this.scheduleMeasure();
    } else if (changes['notAnalyzed'] || changes['notAnalyzedUnits'] || changes['batteryRows'] || changes['subjectLabel']) {
      this.rebuildFigures();
      this.scheduleMeasure();
    }
    if (changes['axis'] && !changes['axis'].firstChange && changes['axis'].previousValue?.key !== this.axis?.key) {
      this.exportStatus = '';
    }
  }

  ngAfterViewInit(): void {
    this.observeTabBar();
    this.tileChanges = this.tileRefs?.changes.subscribe(() => {
      this.observeTiles();
      this.scheduleMeasure();
      refreshAnchorPositioning();
    }) ?? null;
    refreshAnchorPositioning();
    this.scheduleMeasure();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.viewportObserver?.disconnect();
    this.viewportObserver = null;
    this.tileObserver?.disconnect();
    this.tileObserver = null;
    this.tabBarObserver?.disconnect();
    this.tabBarObserver = null;
    this.tileChanges?.unsubscribe();
    this.tileChanges = null;
    if (this.measureFrame !== null) cancelAnimationFrame(this.measureFrame);
    if (this.zoomFrame !== null) cancelAnimationFrame(this.zoomFrame);
    this.measureFrame = null;
    this.zoomFrame = null;
  }

  // --- Derived data ---

  private get markerFilter(): CcMarkerFilter {
    return { kinds: this.markerKinds, hiddenEventKinds: this.hiddenEventKinds };
  }

  /** A battery set is compared: the *Plot by* choice is offered. */
  get batterySet(): boolean {
    return this.unitKind === 'batteryRun';
  }

  /** The charts plot battery runs: a battery set, plotted by battery run. */
  get plotsBatteryRuns(): boolean {
    return this.batterySet && this.plotBy === 'batteryRuns';
  }

  /** The plotted points: the set's battery points, or the runs. */
  private plotPoints(): readonly CcTimelinePoint[] {
    const timeline = this.timeline;
    if (!timeline) return [];
    return this.plotsBatteryRuns
      ? (timeline.batteryPoints ?? []).filter(point => point.setKey === this.setKey)
      : timeline.points;
  }

  /** Member run id → its suite name, from the set's battery runs. Kept per input list. */
  private memberLabels(): ReadonlyMap<number, string> {
    if (this.memberLabelsMemo?.source !== this.batteryRows) {
      const labels = new Map<number, string>();
      for (const row of this.batteryRows) {
        for (const member of row.members) labels.set(member.runId, member.suiteName);
      }
      this.memberLabelsMemo = { source: this.batteryRows, labels };
    }
    return this.memberLabelsMemo.labels;
  }

  private memberLabelsMemo: { source: readonly CcBatteryRunRow[]; labels: ReadonlyMap<number, string> } | null = null;

  private figureInput(): CcFigureInput | null {
    const timeline = this.timeline;
    if (!timeline) return null;
    const battery = this.plotsBatteryRuns;
    const input: CcFigureInput = {
      points: this.plotPoints(),
      events: timeline.events,
      annotations: timeline.annotations,
      markerFilter: this.markerFilter,
      // Battery points carry no harness of their own, so the runs group and number the events.
      ...(battery ? { unitKind: 'batteryRun' as const, harnessPoints: timeline.points, memberLabels: this.memberLabels() } : {})
    };
    const notAnalyzed = this.markNotAnalyzed ? this.notAnalyzedText(battery ? this.notAnalyzedUnits : this.notAnalyzed) : null;
    return notAnalyzed ? { ...input, notAnalyzed } : input;
  }

  /** `plural noun`: `battery runs`, `member runs` or `runs`, the subject line's last part. */
  private get plottedNoun(): string {
    if (!this.batterySet) return `${unitNoun('run')}s`;
    return this.plotsBatteryRuns ? `${unitNoun('batteryRun')}s` : 'member runs';
  }

  /** The charts' subject line: `Claude 5.5 Haiku (xhigh) · Two initial suites (revision 1) · battery runs`. */
  get chartSubject(): string | null {
    const label = this.subjectLabel || this.axis?.displayName || this.timeline?.subject.displayName || '';
    return label ? `${label} · ${this.plottedNoun}` : null;
  }

  /** The chart options shared by the screen and every export, in `theme`. */
  private chartOptions(theme: CcChartTheme, reducedMotion: boolean, key: CcFigureKey): CcChartOptions {
    return {
      theme,
      reducedMotion,
      hiddenSeries: this.hiddenSeries,
      zeroBaseline: this.zeroBaseline,
      header: { title: figureTitle(key), subject: this.chartSubject },
      logo: this.showLogo ? this.logoImage : null
    };
  }

  /** `source` as the charts take it, point id to reason text; null when empty. Kept per input map. */
  private notAnalyzedText(source: ReadonlyMap<number, CcRunInclusion> | null): ReadonlyMap<number, string> | null {
    if (!source || source.size === 0) return null;
    if (this.notAnalyzedMemo?.source !== source) {
      const text = new Map<number, string>();
      for (const [runId, inclusion] of source) {
        if (inclusion !== 'included') text.set(runId, CC_INCLUSION_TEXT[inclusion]);
      }
      this.notAnalyzedMemo = { source, text };
    }
    return this.notAnalyzedMemo.text.size > 0 ? this.notAnalyzedMemo.text : null;
  }

  private notAnalyzedMemo: { source: ReadonlyMap<number, CcRunInclusion>; text: ReadonlyMap<number, string> } | null = null;

  /** The keys of the shown charts, in chart order. */
  get shownKeys(): CcFigureKey[] {
    return FIGURE_KEY_ORDER.filter(key => this.shownFigures.has(key));
  }

  private rebuildTimeline(): void {
    const timeline = this.timeline;
    this.presentSeries = {};
    if (!timeline) {
      this.eventGroups = [];
      this.servedChanges = [];
      this.kindSummary = [];
      this.totalEventItems = 0;
      return;
    }
    // The served-model changes are over the plotted points, so the list's S tags are the charts'.
    const points = this.plotPoints();
    this.eventGroups = groupOverseerEvents(timeline.events, timeline.points);
    this.servedChanges = servedModelChanges(points);
    this.kindSummary = eventKindSummary(this.eventGroups);
    this.totalEventItems = eventItemCount(buildEventDays(this.eventGroups, timeline.annotations, this.servedChanges));
    const bare: CcFigureInput = { points, unitKind: this.plotsBatteryRuns ? 'batteryRun' : 'run', events: [], annotations: [] };
    for (const { key } of CC_FIGURE_KEYS) {
      const config = buildCcFigure(key, bare, { reducedMotion: true }).config;
      this.presentSeries[key] = config
        ? config.data.datasets.map(dataset => {
          const label = dataset.label ?? dataset.seriesId;
          return { id: dataset.seriesId, label, shortLabel: CC_FIGURE_SERIES[key].find(series => series.id === dataset.seriesId)?.shortLabel ?? label };
        })
        : [];
    }
  }

  private rebuildFigures(): void {
    const input = this.figureInput();
    this.figures = input
      ? this.shownKeys.map(key => buildCcFigure(key, input, this.chartOptions(CC_SCREEN_THEME, this.reducedMotion, key)))
      : [];
    this.seriesGroups = this.shownKeys
      .map(key => ({ key, title: figureTitle(key), series: this.presentSeries[key] ?? [] }))
      .filter(group => group.series.length > 1);
  }

  private rebuildEvents(): void {
    const timeline = this.timeline;
    this.eventDays = timeline
      ? buildEventDays(this.eventGroups, timeline.annotations, this.servedChanges, this.markerFilter)
      : [];
    this.hiddenEventItems = Math.max(0, this.totalEventItems - eventItemCount(this.eventDays));
  }

  /** `GPT-5.6 Luna (max) · Last 30 days · change in step 1`. */
  get readout(): string {
    const name = this.axis?.displayName ?? this.timeline?.subject.displayName ?? null;
    if (name === null) return 'No model chosen · choose one in step 1';
    const range = this.rangeLabel || ccRangeText(this.timeline?.fromUtc, this.timeline?.toUtc);
    return `${name} · ${range} · change in step 1`;
  }

  // --- Sidebar ---

  measureSidebarWidthMax(): void {
    const workspace = this.workspaceRef?.nativeElement.clientWidth ?? 0;
    this.sidebarWidthMax = workspace > 0
      ? Math.max(CC_SIDEBAR_WIDTH_MIN, Math.min(CC_SIDEBAR_WIDTH_MAX, Math.floor(workspace / 2)))
      : CC_SIDEBAR_WIDTH_MAX;
    this.cdr.markForCheck();
  }

  /** Live while dragging; the views re-fit through their own observer. */
  onSidebarWidthChange(width: number): void {
    this.sidebarWidth = width;
    this.cdr.markForCheck();
  }

  onSidebarWidthCommit(width: number): void {
    this.sidebarWidth = width;
    this.persist();
    this.cdr.markForCheck();
  }

  /** Focus stays on the toggle, which sits outside the sidebar and is always rendered. */
  toggleSidebar(): void {
    this.sidebarCollapsed = !this.sidebarCollapsed;
    this.persist();
    this.cdr.detectChanges();
    refreshAnchorPositioning();
    this.scheduleMeasure();
  }

  selectSidebarTab(tab: CcTimelineSidebarTab): void {
    if (tab === this.sidebarTab) return;
    this.sidebarTab = tab;
    if (tab === 'annotations') this.annotationsMounted = true;
    this.persist();
    this.cdr.detectChanges();
    refreshAnchorPositioning();
  }

  /** Left/Right move and wrap, Home/End jump to the ends; focus follows selection. */
  onSidebarTabKeydown(event: KeyboardEvent, index: number): void {
    const next = this.rovingTabIndex(event, index, this.sidebarTabs.length);
    if (next === null) return;
    const tab = this.sidebarTabs[next].id;
    this.selectSidebarTab(tab);
    document.getElementById(`cc-tl-side-tab-${tab}`)?.focus();
  }

  /** A figure's *Show events*: the Events tab, the sidebar opened if collapsed, focus on the list. */
  onShowEvents(): void {
    this.sidebarCollapsed = false;
    this.sidebarTab = 'events';
    this.persist();
    this.cdr.detectChanges();
    refreshAnchorPositioning();
    this.scheduleMeasure();
    const list = document.getElementById('cc-tl-events');
    if (list) {
      list.focus({ preventScroll: true });
      list.scrollIntoView({ block: 'start', behavior: this.reducedMotion ? 'auto' : 'smooth' });
    }
  }

  // --- Data tab ---

  isFigureShown(key: CcFigureKey): boolean {
    return this.shownFigures.has(key);
  }

  setFigureShown(key: CcFigureKey, shown: boolean): void {
    if (shown === this.shownFigures.has(key)) return;
    const next = new Set(this.shownFigures);
    if (shown) next.add(key); else next.delete(key);
    this.shownFigures = next;
    this.afterFigureSettings();
  }

  showAllFigures(): void {
    this.shownFigures = new Set(FIGURE_KEY_ORDER);
    this.afterFigureSettings();
  }

  showNoFigures(): void {
    this.shownFigures = new Set();
    this.afterFigureSettings();
  }

  isSeriesShown(id: string): boolean {
    return !this.hiddenSeries.has(id);
  }

  setSeriesShown(id: string, shown: boolean): void {
    const next = new Set(this.hiddenSeries);
    if (shown) next.delete(id); else next.add(id);
    this.hiddenSeries = next;
    this.afterFigureSettings();
  }

  setZeroBaseline(on: boolean): void {
    this.zeroBaseline = on;
    this.afterFigureSettings();
  }

  setMarkNotAnalyzed(on: boolean): void {
    this.markNotAnalyzed = on;
    this.afterFigureSettings();
  }

  /** Battery runs, or their member runs; not stored, so a new set opens on battery runs. */
  setPlotBy(plotBy: CcPlotBy): void {
    if (plotBy === this.plotBy) return;
    this.plotBy = plotBy;
    this.rebuildTimeline();
    this.rebuildEvents();
    this.afterFigureSettings();
  }

  /** An element id for a series id, which carries a dot. */
  seriesDomId(id: string): string {
    return `cc-tl-series-${id.replace(/[^A-Za-z0-9_-]/g, '-')}`;
  }

  private afterFigureSettings(): void {
    this.rebuildFigures();
    this.persist();
    this.cdr.markForCheck();
    this.scheduleMeasure();
  }

  // --- Events tab ---

  isMarkerKindShown(kind: CcMarkerKind): boolean {
    return this.markerKinds.has(kind);
  }

  setMarkerKindShown(kind: CcMarkerKind, shown: boolean): void {
    const next = new Set(this.markerKinds);
    if (shown) next.add(kind); else next.delete(kind);
    this.markerKinds = next;
    this.afterMarkerSettings();
  }

  isEventKindShown(kind: string): boolean {
    return !this.hiddenEventKinds.has(kind);
  }

  setEventKindShown(kind: string, shown: boolean): void {
    const next = new Set(this.hiddenEventKinds);
    if (shown) next.delete(kind); else next.add(kind);
    this.hiddenEventKinds = next;
    this.afterMarkerSettings();
  }

  /** The charts and the event list redraw from the same filter, so they always agree. */
  private afterMarkerSettings(): void {
    this.rebuildFigures();
    this.rebuildEvents();
    this.persist();
    this.cdr.markForCheck();
    this.scheduleMeasure();
  }

  // --- Download tab ---

  get sizeError(): string {
    return sizeErrors(this.chartSize, 'chart').any;
  }

  onChartSizeChange(settings: FigureSizeSettings): void {
    this.setChartSize(settings);
  }

  resetChartSize(): void {
    if (!sameFigureSize(this.chartSize, this.defaultChartSize)) {
      this.setChartSize(this.defaultChartSize);
    }
  }

  onChartSizeOpenChange(open: boolean): void {
    if (open !== this.chartSizeOpen) {
      this.chartSizeOpen = open;
      this.persist();
    }
  }

  /** A usable size gives the charts their new box; a numeric zoom keeps its value, a fit re-fits. */
  private setChartSize(settings: FigureSizeSettings): void {
    this.chartSize = { ...settings };
    writeStoredSizeSettings(CC_CHART_SIZE_STORAGE_KEY, this.chartSize);
    if (sizeErrors(this.chartSize, 'chart').any === '') {
      this.box = ccChartBox(this.chartSize);
    }
    this.cdr.markForCheck();
    this.scheduleMeasure();
  }

  onImageFormatChange(format: FigureExportFormat): void {
    this.imageFormat = format;
    this.persist();
    this.cdr.markForCheck();
  }

  onWebpQualityChange(quality: WebpQuality): void {
    this.webpQuality = quality;
    this.persist();
    this.cdr.markForCheck();
  }

  onImageFormatOpenChange(open: boolean): void {
    if (open !== this.imageFormatOpen) {
      this.imageFormatOpen = open;
      this.persist();
    }
  }

  resetImageFormat(): void {
    this.imageFormat = 'png';
    this.webpQuality = DEFAULT_WEBP_QUALITY;
    this.persist();
    this.cdr.markForCheck();
  }

  setImageTheme(theme: CcImageTheme): void {
    this.imageTheme = theme;
    this.persist();
    this.cdr.markForCheck();
  }

  /** The logo on the charts and in every image. */
  setShowLogo(on: boolean): void {
    if (on === this.showLogo) return;
    this.showLogo = on;
    this.afterFigureSettings();
  }

  // --- Views ---

  /** Each view opens at *Fit to screen*: All charts and Single chart alike. */
  selectView(view: CcTimelineViewTab): void {
    if (view === this.view) return;
    this.view = view;
    if (view === 'all') {
      this.allView = 'fitScreen';
    } else {
      this.singleView = 'fitScreen';
    }
    this.persist();
    this.cdr.detectChanges();
    refreshAnchorPositioning();
    this.scheduleMeasure();
  }

  onViewTabKeydown(event: KeyboardEvent, index: number): void {
    const next = this.rovingTabIndex(event, index, this.viewTabs.length);
    if (next === null) return;
    const view = this.viewTabs[next].id;
    this.selectView(view);
    document.getElementById(`cc-tl-view-tab-${view}`)?.focus();
  }

  /** The chart Single chart shows: the chosen one while it is shown, else the first shown. */
  get singleFigure(): CcFigure | null {
    return this.figures.find(figure => figure.key === this.singleKey) ?? this.figures[0] ?? null;
  }

  selectSingleFigure(key: string): void {
    if (!FIGURE_KEY_ORDER.includes(key as CcFigureKey)) return;
    this.singleKey = key as CcFigureKey;
    this.persist();
    this.cdr.markForCheck();
    this.scheduleMeasure();
  }

  /** Wrapping, so neither end of the set is a dead control. */
  stepSingleFigure(delta: number): void {
    const figures = this.figures;
    if (figures.length === 0) return;
    const current = figures.findIndex(figure => figure.key === this.singleFigure?.key);
    const next = ((current < 0 ? 0 : current + delta) + figures.length) % figures.length;
    this.selectSingleFigure(figures[next].key);
  }

  /** A tile's *Open in Single view*: Single chart on that chart, focus on the chart select. */
  openInSingle(key: CcFigureKey): void {
    this.selectSingleFigure(key);
    if (this.view === 'single') {
      this.cdr.detectChanges();
    } else {
      this.selectView('single');
    }
    document.getElementById('cc-tl-single-figure')?.focus();
  }

  /** In All charts, whether the tile is near enough to the view to hold a canvas. */
  isNear(key: CcFigureKey): boolean {
    return this.nearAll || this.nearKeys.has(key);
  }

  // --- Zoom ---

  private get activeZoomView(): CcZoomView {
    return this.view === 'all' ? this.allView : this.singleView;
  }

  private get fits(): { width: number; height: number; screen: number } {
    const viewport = this.viewportBox;
    if (!viewport) return { width: 1, height: 1, screen: 1 };
    const width = viewport.width - this.chrome.width;
    return {
      width: ccFitWidthZoom(this.box, width),
      height: ccFitHeightZoom(this.box, viewport.height, this.chrome.height),
      screen: ccFitScreenZoom(this.box, width, viewport.height, this.chrome.height)
    };
  }

  /** The fit the view's stops and slider are built around: Fit to screen, in both views. */
  private get viewFit(): number {
    return this.fits.screen;
  }

  get zoomRange(): PreviewZoomRange {
    const fits = this.fits;
    return ccZoomRange(Math.min(fits.width, fits.height, fits.screen));
  }

  get zoom(): number {
    return ccResolveZoom(this.activeZoomView, this.fits);
  }

  /** The chart box at the current zoom, in whole CSS px. */
  get boxWidth(): number {
    return Math.max(1, Math.floor(this.box.width * this.zoom));
  }

  get boxHeight(): number {
    return Math.max(1, Math.floor(this.box.height * this.zoom));
  }

  /** The display's ratio, lowered so no chart canvas passes 8 M device pixels. */
  get deviceRatio(): number {
    const ratio = typeof window !== 'undefined' ? window.devicePixelRatio || 1 : 1;
    return ccCanvasRatio(this.boxWidth, this.boxHeight, ratio);
  }

  get sliderValue(): number {
    return zoomToSlider(this.zoom, this.zoomRange);
  }

  private get fitName(): { label: string; text: string } | null {
    switch (this.activeZoomView) {
      case 'fitWidth': return { label: 'Fit width', text: 'fitted to the width' };
      case 'fitScreen': return { label: 'Fit to screen', text: 'fitted to the screen' };
      default: return null;
    }
  }

  /** `150% · Fit width`. */
  get zoomLabel(): string {
    const zoom = formatPreviewZoom(this.zoom);
    const fit = this.fitName;
    return fit ? `${zoom} · ${fit.label}` : zoom;
  }

  /** `150 percent, fitted to the width`. */
  get zoomValueText(): string {
    const percent = `${formatPreviewZoom(this.zoom).replace('%', '')} percent`;
    const fit = this.fitName;
    return fit ? `${percent}, ${fit.text}` : percent;
  }

  get canZoomIn(): boolean {
    return canZoomPreviewIn(this.zoom, this.zoomRange);
  }

  get canZoomOut(): boolean {
    return canZoomPreviewOut(this.zoom, this.zoomRange);
  }

  zoomIn(): void {
    if (this.canZoomIn) {
      this.setZoomView(nextPreviewZoomStop(this.zoom, this.viewFit, this.zoomRange));
    }
  }

  zoomOut(): void {
    if (this.canZoomOut) {
      this.setZoomView(previousPreviewZoomStop(this.zoom, this.viewFit, this.zoomRange));
    }
  }

  /** Applied once per animation frame, so a dragged slider resizes the charts at most once a frame. */
  onSliderInput(value: number): void {
    this.pendingZoom = sliderToZoom(value, this.zoomRange);
    if (this.zoomFrame !== null) return;
    this.zoomFrame = requestAnimationFrame(() => {
      this.zoomFrame = null;
      const zoom = this.pendingZoom;
      this.pendingZoom = null;
      if (zoom !== null && !this.destroyed) this.setZoomView(zoom);
    });
  }

  fitWidth(): void {
    this.setZoomView('fitWidth');
    this.scheduleMeasure();
  }

  fitScreen(): void {
    this.setZoomView('fitScreen');
    this.scheduleMeasure();
  }

  /** 100 %: the on-screen box is the download's layout box. */
  actualSize(): void {
    this.setZoomView(1);
  }

  private setZoomView(view: CcZoomView): void {
    if (this.view === 'all') {
      this.allView = view;
    } else {
      this.singleView = view;
    }
    this.cdr.markForCheck();
  }

  /**
   * `+` / `=` zoom in, `-` out, `0` fits one whole chart to the screen and, in
   * Single, `1` is 100 %; anywhere in the panel but a form field, and without Ctrl, ⌘ or Alt, which
   * stay the browser's zoom.
   */
  onPanelKeydown(event: KeyboardEvent): void {
    if (event.ctrlKey || event.metaKey || event.altKey || this.isFormField(event.target)) return;
    const actions: Record<string, () => void> = {
      '+': () => this.zoomIn(),
      '=': () => this.zoomIn(),
      '-': () => this.zoomOut(),
      '0': () => this.fitScreen()
    };
    if (this.view === 'single') {
      actions['1'] = () => this.actualSize();
    }
    const action = actions[event.key];
    if (action) {
      event.preventDefault();
      action();
    }
  }

  private isFormField(target: EventTarget | null): boolean {
    const element = target as HTMLElement | null;
    if (!element || typeof element.closest !== 'function') return false;
    return element.isContentEditable || element.closest('input, select, textarea') !== null;
  }

  // --- Observers and measuring ---

  private observeViewport(): void {
    this.viewportObserver?.disconnect();
    this.viewportObserver = null;
    const viewport = this.viewportEl;
    if (viewport && typeof ResizeObserver !== 'undefined') {
      // A size of zero (a hidden step) keeps the last fit; the observer fires again once shown.
      this.viewportObserver = new ResizeObserver(() => this.zone.run(() => this.scheduleMeasure()));
      this.viewportObserver.observe(viewport);
    }
    this.observeTiles();
  }

  /** Only tiles within one viewport height of view hold a canvas; the rest keep their sized box. */
  private observeTiles(): void {
    this.tileObserver?.disconnect();
    this.tileObserver = null;
    const viewport = this.viewportEl;
    if (!viewport || this.view !== 'all' || typeof IntersectionObserver === 'undefined') return;
    const observer = new IntersectionObserver(
      entries => this.zone.run(() => this.onTilesIntersect(entries)),
      { root: viewport, rootMargin: '100% 0px' }
    );
    viewport.querySelectorAll<HTMLElement>('.cc-tl-tile').forEach(tile => observer.observe(tile));
    this.tileObserver = observer;
  }

  private onTilesIntersect(entries: IntersectionObserverEntry[]): void {
    const next = new Set(this.nearKeys);
    for (const entry of entries) {
      const key = (entry.target as HTMLElement).dataset['figure'] as CcFigureKey | undefined;
      if (!key) continue;
      if (entry.isIntersecting) next.add(key); else next.delete(key);
    }
    if (next.size !== this.nearKeys.size || [...next].some(key => !this.nearKeys.has(key))) {
      this.nearKeys = next;
      this.cdr.markForCheck();
    }
  }

  private observeTabBar(): void {
    const tabs = this.sideTabsRef?.nativeElement;
    if (!tabs || typeof ResizeObserver === 'undefined') return;
    this.tabBarObserver = new ResizeObserver(() => this.zone.run(() => {
      const height = Math.ceil(tabs.getBoundingClientRect().height);
      if (height > 0 && height !== this.tabBarHeight) {
        this.tabBarHeight = height;
        this.cdr.markForCheck();
      }
    }));
    this.tabBarObserver.observe(tabs);
  }

  /**
   * Measures on the next frame. A change that moves the charts' boxes can rewrap their captions, so
   * a measurement that changed something measures again, at most `followUps` times.
   */
  private scheduleMeasure(followUps = 2): void {
    this.measureFollowUps = followUps;
    if (this.measureFrame !== null || this.destroyed) return;
    this.measureFrame = requestAnimationFrame(() => {
      this.measureFrame = null;
      if (this.destroyed || !this.measure()) return;
      this.cdr.markForCheck();
      if (this.measureFollowUps > 0) {
        this.scheduleMeasure(this.measureFollowUps - 1);
      }
    });
  }

  /** The viewport's content box and the figure chrome; false when nothing changed or nothing has a size. */
  private measure(): boolean {
    const viewport = this.viewportEl;
    if (!viewport) return false;
    const style = getComputedStyle(viewport);
    const px = (value: string) => Number.parseFloat(value) || 0;
    const width = Math.floor(viewport.clientWidth - px(style.paddingLeft) - px(style.paddingRight) + 1e-6);
    const height = Math.floor(viewport.clientHeight - px(style.paddingTop) - px(style.paddingBottom) + 1e-6);
    if (!(width > 0) || !(height > 0)) return false;

    const chrome = this.measureChrome(viewport) ?? this.chrome;
    const before = this.viewportBox;
    const changed = !before
      || Math.abs(before.width - width) >= MEASURE_TOLERANCE
      || Math.abs(before.height - height) >= MEASURE_TOLERANCE
      || Math.abs(this.chrome.width - chrome.width) >= MEASURE_TOLERANCE
      || Math.abs(this.chrome.height - chrome.height) >= MEASURE_TOLERANCE;
    if (!changed) return false;
    this.viewportBox = { width, height };
    this.chrome = chrome;
    return true;
  }

  /**
   * The first drawn figure's size less its chart box: the caption, marker line and *Show data*
   * summary across and down, an open *Show data* table left out.
   */
  private measureChrome(viewport: HTMLElement): { width: number; height: number } | null {
    for (const figure of Array.from(viewport.querySelectorAll<HTMLElement>('.cc-figure'))) {
      const chartBox = figure.querySelector<HTMLElement>('.cc-chart-box');
      if (!chartBox) continue;
      const outer = figure.getBoundingClientRect();
      const inner = chartBox.getBoundingClientRect();
      let table = 0;
      const data = figure.querySelector<HTMLDetailsElement>('details.cc-figure-data');
      if (data?.open) {
        const summary = data.querySelector('summary');
        table = data.getBoundingClientRect().height - (summary?.getBoundingClientRect().height ?? 0);
      }
      return {
        width: Math.max(0, Math.ceil(outer.width - inner.width)),
        height: Math.max(0, Math.ceil(outer.height - inner.height - table))
      };
    }
    return null;
  }

  // --- Copy and Download ---

  /** Why nothing can be exported now, or empty. */
  get exportBlockReason(): string {
    if (this.exporting) return 'An export is running.';
    return this.sizeError;
  }

  /** Why the chart has nothing to draw, or empty. */
  emptyReason(figure: CcFigure): string {
    if (figure.config) return '';
    const present = this.presentSeries[figure.key] ?? [];
    return present.length > 0 && present.every(series => this.hiddenSeries.has(series.id))
      ? `Every series of ${figure.title} is hidden.`
      : `${figure.title} has nothing to draw in this range.`;
  }

  /** Why the chart cannot be copied or downloaded now, or empty. */
  chartBlockReason(figure: CcFigure | null): string {
    if (!figure) return 'No chart is shown. Choose charts under Data.';
    return this.exportBlockReason || this.emptyReason(figure);
  }

  get downloadAllBlockReason(): string {
    if (this.exportBlockReason) return this.exportBlockReason;
    if (this.figures.length === 0) return 'No chart is shown. Choose charts under Data.';
    return this.figures.some(figure => figure.config) ? '' : 'No shown chart has anything to draw.';
  }

  /** `Full HD — 1920 × 1080 · 100% · PNG`. */
  get exportSummary(): string {
    const format = this.imageFormat === 'webp' ? `WebP q${this.webpQuality}` : 'PNG';
    return `${resolveSizeResolution(this.chartSize).label} · ${densityPercentLabel(resolveSizeDensity(this.chartSize))} · ${format}`;
  }

  copyTooltip(figure: CcFigure | null): string {
    return this.chartBlockReason(figure) || 'Copy as a PNG image';
  }

  downloadTooltip(figure: CcFigure | null): string {
    return this.chartBlockReason(figure) || `Download this chart — ${this.exportSummary}`;
  }

  get downloadAllTooltip(): string {
    return this.downloadAllBlockReason
      || (this.figures.length > 1 ? `All shown charts as one ZIP — ${this.exportSummary}` : `The shown chart — ${this.exportSummary}`);
  }

  private get modelKey(): string {
    return this.axis?.key ?? this.timeline?.subject.key ?? 'model';
  }

  private setExporting(exporting: boolean): void {
    this.exporting = exporting;
    this.exportingChange.emit(exporting);
    this.cdr.markForCheck();
  }

  /** The data and settings of one export, read once so a change during it cannot mix two states. */
  private exportSnapshot(): CcExportSnapshot {
    return {
      plan: ccExportLayout(this.chartSize),
      sizeError: this.sizeError,
      input: this.figureInput(),
      theme: ccExportTheme(this.imageTheme),
      hiddenSeries: this.hiddenSeries,
      zeroBaseline: this.zeroBaseline,
      subject: this.chartSubject,
      logo: this.showLogo ? this.logoImage : null,
      webpQuality: this.webpQuality,
      modelKey: this.modelKey
    };
  }

  /**
   * Composes one chart off-screen as the screen draws it — header band, logo, series and markers — in
   * the snapshot's theme.
   */
  private async chartImage(key: CcFigureKey, format: FigureExportFormat, snapshot: CcExportSnapshot): Promise<CcChartImage> {
    const { plan, input } = snapshot;
    if (!plan.layout) return { result: null, refusal: plan.refusal ?? snapshot.sizeError, empty: false };
    if (!input) return { result: null, refusal: null, empty: true };
    const figure = buildCcFigure(key, input, {
      theme: snapshot.theme,
      reducedMotion: true,
      hiddenSeries: snapshot.hiddenSeries,
      zeroBaseline: snapshot.zeroBaseline,
      header: { title: figureTitle(key), subject: snapshot.subject },
      logo: snapshot.logo
    });
    if (!figure.config) return { result: null, refusal: null, empty: true };
    const canvas = await renderPlotOffscreen(figure.config as unknown as OffscreenPlotConfig, plan.layout);
    if (!canvas) return { result: null, refusal: null, empty: false };
    return { result: await encodeFigureImage(canvas, format, snapshot.webpQuality), refusal: null, empty: false };
  }

  /** Always a PNG, the image type clipboards take. There is no *Copy all*: a clipboard holds one image. */
  async copyChart(figure: CcFigure | null): Promise<void> {
    if (!figure || this.chartBlockReason(figure)) return;
    const title = figure.title;
    this.exportStatus = '';
    this.setExporting(true);
    try {
      const image = await this.chartImage(figure.key, 'png', this.exportSnapshot());
      if (!image.result) {
        this.exportStatus = image.refusal ?? `${title} could not be copied.`;
        return;
      }
      const outcome = await copyImageToClipboard(image.result.blob);
      this.exportStatus = outcome === 'copied'
        ? `Copied ${title} to the clipboard.`
        : outcome === 'unsupported'
          ? 'This browser cannot copy images to the clipboard — download the chart instead.'
          : 'The clipboard write was refused.';
    } catch {
      this.exportStatus = `${title} could not be copied.`;
    } finally {
      this.setExporting(false);
    }
  }

  async downloadChart(figure: CcFigure | null): Promise<void> {
    if (!figure || this.chartBlockReason(figure)) return;
    const title = figure.title;
    this.exportStatus = '';
    this.setExporting(true);
    try {
      const snapshot = this.exportSnapshot();
      const image = await this.chartImage(figure.key, this.imageFormat, snapshot);
      if (!image.result) {
        this.exportStatus = image.refusal ?? `${title} could not be downloaded.`;
        return;
      }
      saveFigureBlob(image.result.blob, ccChartFilename(snapshot.modelKey, figure.key, image.result.format, new Date()));
      this.exportStatus = `Downloaded ${title}.${image.result.fellBackToPng ? ' Written as PNG: this browser cannot encode WebP.' : ''}`;
    } catch {
      this.exportStatus = `${title} could not be downloaded.`;
    } finally {
      this.setExporting(false);
    }
  }

  /**
   * Every shown chart, in order: one chart as its file, more than one as one ZIP. A chart with nothing
   * to draw is skipped and named.
   */
  async downloadAllCharts(): Promise<void> {
    if (this.downloadAllBlockReason) return;
    const figures = [...this.figures];
    const format = this.imageFormat;
    const snapshot = this.exportSnapshot();
    this.exportStatus = '';
    this.setExporting(true);
    const stamp = new Date();
    const entries: (FigureArchiveEntry & { title: string })[] = [];
    const skipped: string[] = [];
    const failed: string[] = [];
    let fellBack = false;
    try {
      for (const figure of figures) {
        const image = await this.chartImage(figure.key, format, snapshot);
        if (image.refusal) {
          this.exportStatus = image.refusal;
          return;
        }
        if (!image.result) {
          (image.empty ? skipped : failed).push(figure.title);
          continue;
        }
        fellBack = fellBack || image.result.fellBackToPng;
        entries.push({
          name: ccChartFilename(snapshot.modelKey, figure.key, image.result.format, stamp),
          blob: image.result.blob,
          title: figure.title
        });
      }
      let outcome: string;
      if (entries.length === 1) {
        saveFigureBlob(entries[0].blob, entries[0].name);
        outcome = `Downloaded ${entries[0].title}.`;
      } else if (entries.length > 1) {
        const archive = await buildFigureArchive(entries.map(entry => ({ name: entry.name, blob: entry.blob })));
        saveFigureBlob(archive, ccChartArchiveFilename(snapshot.modelKey, stamp));
        outcome = `Downloaded ${entries.length} charts as a ZIP.`;
      } else {
        outcome = 'No chart was downloaded.';
      }
      if (skipped.length > 0) outcome += ` Skipped, with nothing to draw: ${skipped.join(', ')}.`;
      if (failed.length > 0) outcome += ` Could not be drawn: ${failed.join(', ')}.`;
      if (fellBack) outcome += ' Written as PNG: this browser cannot encode WebP.';
      this.exportStatus = outcome;
    } catch {
      this.exportStatus = 'The charts could not be downloaded.';
    } finally {
      this.setExporting(false);
    }
  }

  // --- Tooltips and storage ---

  /** A tooltip id, unique per action and chart. */
  tipId(action: string, key: string): string {
    return `cc-tl-tip-${action}-${key}`;
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
    if (requested === undefined) return null;
    event.preventDefault();
    return (requested + count) % count;
  }

  private persist(): void {
    writeStoredTimelineLayout({
      sidebarCollapsed: this.sidebarCollapsed,
      sidebarWidth: this.sidebarWidth,
      sidebarTab: this.sidebarTab,
      view: this.view,
      figures: this.shownKeys,
      hiddenSeries: [...this.hiddenSeries],
      markerKinds: MARKER_KINDS.filter(kind => this.markerKinds.has(kind)),
      hiddenEventKinds: [...this.hiddenEventKinds],
      zeroBaseline: this.zeroBaseline,
      markNotAnalyzed: this.markNotAnalyzed,
      imageTheme: this.imageTheme,
      imageFormat: this.imageFormat,
      webpQuality: this.webpQuality,
      chartSizeOpen: this.chartSizeOpen,
      imageFormatOpen: this.imageFormatOpen,
      singleFigure: this.singleKey,
      logo: this.showLogo
    });
  }
}
