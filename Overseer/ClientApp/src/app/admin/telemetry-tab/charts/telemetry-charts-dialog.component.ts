import {
  AfterViewChecked,
  AfterViewInit,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  NgZone,
  OnDestroy,
  OnInit,
  QueryList,
  ViewChild,
  ViewChildren,
  inject
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Subscription } from 'rxjs';

import {
  AdminService,
  AiTelemetryBucket,
  AiTelemetryKeySource,
  AiTelemetrySeriesDto,
  AiTelemetrySeriesQuery,
  AiTelemetrySplitBy,
  AiUsageRoleGroup
} from '../../../services/admin.service';
import { PaneResizerComponent } from '../../../shared/pane-resizer/pane-resizer.component';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { rovingTabIndex } from '../../../utils/roving-tab.util';
import {
  CcFigureChrome,
  CcTargetPixels,
  ccDisplaySize,
  ccFitScreenZoom,
  ccFitWidthWithScrollbar,
  ccPreviewDpr,
  ccPreviewLayout,
  ccTargetPixels
} from '../../benchmark/chat-consistency-tab/timeline-workspace/cc-chart-zoom';
import { ExportFormatSectionComponent } from '../../benchmark/model-comparison/export-format-section.component';
import { ExportSizeSectionComponent } from '../../benchmark/model-comparison/export-size-section.component';
import { figureFont } from '../../benchmark/model-comparison/figure-fonts';
import { ensureFigureFont } from '../../benchmark/model-comparison/figure-font-loader';
import {
  DEFAULT_WEBP_QUALITY,
  FigureArchiveEntry,
  FigureExportFormat,
  FigureExportResult,
  WEBP_QUALITY_OPTIONS,
  WebpQuality,
  buildFigureArchive,
  copyImageToClipboard,
  densityPercentLabel,
  displayDensity,
  encodeFigureImage,
  saveFigureBlob
} from '../../benchmark/model-comparison/figure-export';
import { FigureLogo, ensureFigureLogo, figureLogoAspect } from '../../benchmark/model-comparison/figure-logo';
import {
  FigureSizeSettings,
  defaultFigureSize,
  readStoredSizeSettings,
  resolveSizeDensity,
  resolveSizeResolution,
  sameFigureSize,
  sizeErrors,
  writeStoredSizeSettings
} from '../../benchmark/model-comparison/figure-size';
import { DEFAULT_FIGURE_STYLE, FigureLogoVariant, FigureStyle, normalizeFigureStyle } from '../../benchmark/model-comparison/figure-style';
import { FigureStylePanelComponent } from '../../benchmark/model-comparison/figure-style-panel.component';
import { ResolvedFigureTheme, resolveFigureTheme } from '../../benchmark/model-comparison/figure-theme';
import { previewStageContentBox } from '../../benchmark/model-comparison/preview-view';
import {
  TELEMETRY_FIGURE_KEYS,
  TelemetryComposeContext,
  TelemetryFigure,
  TelemetryFigureKey,
  buildTelemetryFigure,
  buildTelemetryFigures,
  composeTelemetryFigure,
  telemetryArchiveFilename,
  telemetryChartFilename,
  telemetryFigureChrome,
  telemetryFigureLayout,
  telemetryFigureSummary,
  telemetryKeysLabel,
  telemetryRangeLabel,
  telemetryUsageEmpty
} from './telemetry-charts';

// --- Request and stored settings ---

/** What the AI Telemetry tab opens the dialog with. */
export interface TelemetryChartsOpenRequest {
  readonly start: string;
  readonly end: string;
  readonly keys: AiTelemetryKeySource;
  readonly roles: AiUsageRoleGroup[];
  /** Opens filtered to one user; the filter is not stored. */
  readonly userId?: string;
  readonly userName?: string;
}

export type TelemetryChartsSidebarTab = 'data' | 'theme' | 'charts' | 'download';
export type TelemetryChartsView = 'all' | 'single';
export type TelemetryChartsDataSection = 'scope' | 'lines' | 'cost';
/** The Single chart zoom: as cc-chart-zoom.ts fits it. */
export type TelemetryChartsZoom = 'fitWidth' | 'fitScreen';

/** The layout and the data settings, per browser, as `{ version: 1, … }`. Read and written in `try/catch`. */
export const TL_CHARTS_STORAGE_KEY = 'overseer.admin.telemetry.charts';
/** The chart size, per browser, apart from the GnollBench figures' sizes. */
export const TL_CHARTS_SIZE_STORAGE_KEY = `${TL_CHARTS_STORAGE_KEY}.chartSize`;
/** The Theme and Charts tabs' figure style, as `{ version: 1, appearance, timeline }`. */
export const TL_CHARTS_FIGURE_STYLE_STORAGE_KEY = `${TL_CHARTS_STORAGE_KEY}.figureStyle`;
/** The figure style panel's open sections in this dialog. */
export const TL_CHARTS_FIGURE_STYLE_PANEL_OPEN_KEY = `${TL_CHARTS_STORAGE_KEY}.figureStylePanel.open`;
/** The image format, the WebP quality and the Download tab's open sections, as `{ version: 1, … }`. */
export const TL_CHARTS_DOWNLOAD_STORAGE_KEY = `${TL_CHARTS_STORAGE_KEY}.download`;

/** The settings sidebar's width, in CSS px: 26 rem by default, from 18 rem to 40 rem or half the workspace. */
export const TL_CHARTS_SIDEBAR_WIDTH_DEFAULT = 416;
export const TL_CHARTS_SIDEBAR_WIDTH_MIN = 288;
export const TL_CHARTS_SIDEBAR_WIDTH_MAX = 640;

/** The quiet time after a Data change before the series is fetched again. */
export const TL_CHARTS_FETCH_DEBOUNCE_MS = 300;

/** The quiet time after a style change, a zoom or a resize before the charts are composed again. */
const COMPOSE_DEBOUNCE_MS = 150;

export const TL_CHARTS_SIDEBAR_TABS: readonly { readonly id: TelemetryChartsSidebarTab; readonly label: string }[] = [
  { id: 'data', label: 'Data' },
  { id: 'theme', label: 'Theme' },
  { id: 'charts', label: 'Charts' },
  { id: 'download', label: 'Download' }
];

export const TL_CHARTS_VIEW_TABS: readonly { readonly id: TelemetryChartsView; readonly label: string }[] = [
  { id: 'all', label: 'All charts' },
  { id: 'single', label: 'Single chart' }
];

export const TL_CHARTS_KEY_OPTIONS: readonly { readonly value: AiTelemetryKeySource; readonly label: string }[] = [
  { value: 'system', label: 'System AI configurations' },
  { value: 'user', label: 'User keys' },
  { value: 'both', label: 'Both' }
];

export const TL_CHARTS_ROLE_OPTIONS: readonly { readonly value: AiUsageRoleGroup; readonly label: string }[] = [
  { value: 'chat', label: 'Chat' },
  { value: 'title', label: 'Title generation' },
  { value: 'benchmark', label: 'GnollBench' },
  { value: 'other', label: 'Other' }
];

export const TL_CHARTS_BUCKET_OPTIONS: readonly { readonly value: AiTelemetryBucket; readonly label: string }[] = [
  { value: 'auto', label: 'Auto' },
  { value: 'day', label: 'Day' },
  { value: 'week', label: 'Week' },
  { value: 'month', label: 'Month' }
];

export const TL_CHARTS_SPLIT_OPTIONS: readonly { readonly value: AiTelemetrySplitBy; readonly label: string }[] = [
  { value: 'none', label: 'None' },
  { value: 'role', label: 'Role' },
  { value: 'provider', label: 'Provider' },
  { value: 'model', label: 'Model' },
  { value: 'keySource', label: 'Key source' },
  { value: 'user', label: 'User' }
];

/** Every split but *User*, which needs user keys. */
const SPLIT_OPTIONS_WITHOUT_USER = TL_CHARTS_SPLIT_OPTIONS.filter(option => option.value !== 'user');

const DATA_SECTIONS: readonly { readonly name: TelemetryChartsDataSection; readonly title: string }[] = [
  { name: 'scope', title: 'Keys and roles' },
  { name: 'lines', title: 'Buckets and lines' },
  { name: 'cost', title: 'Cost' }
];

/** The three named role groups, which the server also reads an empty list as. */
const DEFAULT_ROLES: readonly AiUsageRoleGroup[] = ['chat', 'title', 'benchmark'];

/**
 * The Chat-Consistency-only timeline fields and the badge toggles that mean nothing on these charts;
 * the *dates* toggle stays, and the cost charts' pricing badge has no toggle.
 */
export const TL_CHARTS_HIDDEN_STYLE_KEYS: readonly string[] = Object.freeze([
  'markerNote', 'notAnalyzedNote', 'markerTagSizePx', 'betterBadgePlacement', 'badge:better', 'badge:model', 'badge:runs'
]);

/** The figure layer's default style, without the logo. */
export const TL_CHARTS_DEFAULT_FIGURE_STYLE: FigureStyle = normalizeFigureStyle({
  ...DEFAULT_FIGURE_STYLE,
  appearance: { ...DEFAULT_FIGURE_STYLE.appearance, logo: false }
});

export interface TelemetryChartsLayout {
  readonly collapsed: boolean;
  readonly width: number;
  readonly tab: TelemetryChartsSidebarTab;
  readonly view: TelemetryChartsView;
  /** The chart Single chart shows. */
  readonly figure: TelemetryFigureKey;
  readonly bucket: AiTelemetryBucket;
  readonly splitBy: AiTelemetrySplitBy;
  readonly cumulative: boolean;
  /** The Data tab's open sections. */
  readonly dataSections: readonly TelemetryChartsDataSection[];
}

export interface TelemetryChartsDownload {
  readonly format: FigureExportFormat;
  readonly webpQuality: WebpQuality;
  readonly chartSizeOpen: boolean;
  readonly imageFormatOpen: boolean;
}

/** The sidebar open at 416 px on Data; All charts; Auto buckets, no split, the cost per bucket; Keys and roles open. */
export function defaultTelemetryChartsLayout(): TelemetryChartsLayout {
  return {
    collapsed: false,
    width: TL_CHARTS_SIDEBAR_WIDTH_DEFAULT,
    tab: 'data',
    view: 'all',
    figure: 'requests',
    bucket: 'auto',
    splitBy: 'none',
    cumulative: false,
    dataSections: ['scope']
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

/** A stored layout read field by field: a missing field, or one of the wrong kind, takes its default. */
export function parseTelemetryChartsLayout(stored: unknown): TelemetryChartsLayout {
  const fallback = defaultTelemetryChartsLayout();
  if (!isRecord(stored)) return fallback;
  const flag = (key: string, otherwise: boolean): boolean => typeof stored[key] === 'boolean' ? stored[key] as boolean : otherwise;
  const oneOf = <T extends string>(key: string, options: readonly T[], otherwise: T): T =>
    options.includes(stored[key] as T) ? stored[key] as T : otherwise;
  const width = stored['width'];
  const sections = stored['dataSections'];
  return {
    collapsed: flag('collapsed', fallback.collapsed),
    width: typeof width === 'number' && Number.isFinite(width)
      ? Math.min(TL_CHARTS_SIDEBAR_WIDTH_MAX, Math.max(TL_CHARTS_SIDEBAR_WIDTH_MIN, Math.round(width)))
      : fallback.width,
    tab: oneOf('tab', TL_CHARTS_SIDEBAR_TABS.map(tab => tab.id), fallback.tab),
    view: oneOf('view', TL_CHARTS_VIEW_TABS.map(tab => tab.id), fallback.view),
    figure: oneOf('figure', TELEMETRY_FIGURE_KEYS, fallback.figure),
    bucket: oneOf('bucket', TL_CHARTS_BUCKET_OPTIONS.map(option => option.value), fallback.bucket),
    splitBy: oneOf('splitBy', TL_CHARTS_SPLIT_OPTIONS.map(option => option.value), fallback.splitBy),
    cumulative: flag('cumulative', fallback.cumulative),
    dataSections: Array.isArray(sections)
      ? DATA_SECTIONS.map(section => section.name).filter(name => sections.includes(name))
      : fallback.dataSections
  };
}

function readStoredRecord(key: string): unknown {
  try {
    const raw = localStorage.getItem(key);
    return raw === null ? null : JSON.parse(raw);
  } catch {
    return null;
  }
}

function writeStoredRecord(key: string, value: object): void {
  try {
    localStorage.setItem(key, JSON.stringify({ version: 1, ...value }));
  } catch {
    // Private mode or blocked storage: the setting still applies for this session.
  }
}

export function readStoredTelemetryChartsLayout(): TelemetryChartsLayout {
  return parseTelemetryChartsLayout(readStoredRecord(TL_CHARTS_STORAGE_KEY));
}

/** PNG at the default quality, both sections closed, where nothing usable is stored. */
export function parseTelemetryChartsDownload(stored: unknown): TelemetryChartsDownload {
  const record = isRecord(stored) ? stored : {};
  return {
    format: record['format'] === 'webp' ? 'webp' : 'png',
    webpQuality: WEBP_QUALITY_OPTIONS.includes(record['webpQuality'] as WebpQuality) ? record['webpQuality'] as WebpQuality : DEFAULT_WEBP_QUALITY,
    chartSizeOpen: record['chartSizeOpen'] === true,
    imageFormatOpen: record['imageFormatOpen'] === true
  };
}

/** The stored figure style, repaired by `normalizeFigureStyle`; the logo-less default where none is stored. */
export function readStoredTelemetryChartsFigureStyle(): FigureStyle {
  const stored = readStoredRecord(TL_CHARTS_FIGURE_STYLE_STORAGE_KEY);
  return isRecord(stored) ? normalizeFigureStyle({ ...TL_CHARTS_DEFAULT_FIGURE_STYLE, ...stored }) : TL_CHARTS_DEFAULT_FIGURE_STYLE;
}

/** The reason a series request failed, as the screen shows it: the bucket guard's own message for a 400. */
function seriesErrorText(error: unknown): { message: string; bucket: boolean } {
  const response = error as { status?: number; error?: { message?: unknown } } | null;
  const message = response?.error?.message;
  if (response?.status === 400 && typeof message === 'string' && message !== '') {
    return { message, bucket: true };
  }
  return { message: 'The AI usage series could not be loaded.', bucket: false };
}

/** One chart's composition on screen, and the composition version it was made at. */
export interface TelemetryChartsScreenImage {
  readonly canvas: HTMLCanvasElement | null;
  readonly refusal: string;
  readonly version: number;
}

/** One copy, download or archive attempt, read once so a change during it cannot mix two states. */
interface ExportSnapshot {
  readonly size: FigureSizeSettings;
  readonly dto: AiTelemetrySeriesDto | null;
  readonly context: TelemetryComposeContext;
  readonly webpQuality: WebpQuality;
}

interface ChartImage {
  readonly result: FigureExportResult | null;
  readonly refusal: string | null;
  readonly empty: boolean;
}

/** The figure has no HTML around its image: the tile actions sit over it. */
const NO_CHROME: CcFigureChrome = { width: 0, height: 0 };

/** Below this, a measured length is the same as the last one. */
const MEASURE_TOLERANCE = 0.5;

/**
 * The AI Telemetry Charts dialog: a full-screen figure workspace over the series endpoint, with a
 * resizable, collapsible settings sidebar (Data, Theme, Charts, Download) beside the *All charts* and
 * *Single chart* views. Every chart is composed by `telemetry-charts.ts` at the chart size and
 * rasterized for the screen, so each view shows its download. Self-contained: the tab calls `open()`.
 */
@Component({
  selector: 'app-telemetry-charts-dialog',
  standalone: true,
  imports: [ExportFormatSectionComponent, ExportSizeSectionComponent, FigureStylePanelComponent, NgTemplateOutlet, PaneResizerComponent],
  templateUrl: './telemetry-charts-dialog.component.html',
  styleUrls: ['./telemetry-charts-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TelemetryChartsDialogComponent implements OnInit, AfterViewInit, AfterViewChecked, OnDestroy {
  private readonly admin = inject(AdminService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly zone = inject(NgZone);

  readonly sidebarTabs = TL_CHARTS_SIDEBAR_TABS;
  readonly viewTabs = TL_CHARTS_VIEW_TABS;
  readonly keyOptions = TL_CHARTS_KEY_OPTIONS;
  readonly roleOptions = TL_CHARTS_ROLE_OPTIONS;
  readonly bucketOptions = TL_CHARTS_BUCKET_OPTIONS;
  readonly dataSections = DATA_SECTIONS;
  readonly hiddenStyleKeys = TL_CHARTS_HIDDEN_STYLE_KEYS;
  readonly figureStylePanelOpenKey = TL_CHARTS_FIGURE_STYLE_PANEL_OPEN_KEY;
  readonly SIDEBAR_WIDTH_MIN = TL_CHARTS_SIDEBAR_WIDTH_MIN;
  readonly SIDEBAR_WIDTH_DEFAULT = TL_CHARTS_SIDEBAR_WIDTH_DEFAULT;
  readonly textSizeHint = 'Scales every text of the charts, on screen and in every image; the pixel size stays.';
  readonly formatNote = 'Every chart is downloaded in this format. Copy always writes a PNG.';

  /** Sampled once: the density option the size section marks *(this display)*. */
  readonly displayDensity = displayDensity();
  readonly defaultChartSize: FigureSizeSettings = defaultFigureSize(this.displayDensity);
  readonly defaultWebpQuality = DEFAULT_WEBP_QUALITY;

  private readonly storedLayout = readStoredTelemetryChartsLayout();
  private readonly storedDownload = parseTelemetryChartsDownload(readStoredRecord(TL_CHARTS_DOWNLOAD_STORAGE_KEY));

  // --- Request and data settings ---

  request: TelemetryChartsOpenRequest | null = null;
  keys: AiTelemetryKeySource = 'system';
  roles: AiUsageRoleGroup[] = [...DEFAULT_ROLES];
  bucket: AiTelemetryBucket = this.storedLayout.bucket;
  splitBy: AiTelemetrySplitBy = this.storedLayout.splitBy;
  cumulative = this.storedLayout.cumulative;
  /** The user filter the dialog was opened with, until its chip is removed; never stored. */
  user: { readonly id: string; readonly name: string } | null = null;

  dto: AiTelemetrySeriesDto | null = null;
  loading = false;
  error: string | null = null;
  /** The bucket guard's refusal, shown under Bucket as well. */
  bucketError: string | null = null;

  // --- Layout ---

  sidebarCollapsed = this.storedLayout.collapsed;
  sidebarWidth = this.storedLayout.width;
  sidebarWidthMax = TL_CHARTS_SIDEBAR_WIDTH_MAX;
  sidebarTab: TelemetryChartsSidebarTab = this.storedLayout.tab;
  view: TelemetryChartsView = this.storedLayout.view;
  singleZoom: TelemetryChartsZoom = 'fitScreen';
  private singleKey: TelemetryFigureKey = this.storedLayout.figure;
  private openDataSections = new Set<TelemetryChartsDataSection>(this.storedLayout.dataSections);

  // --- Style and download ---

  figureStyle: FigureStyle = readStoredTelemetryChartsFigureStyle();
  private figureTheme: ResolvedFigureTheme = resolveFigureTheme(this.figureStyle.appearance);
  fontLoadStatus = '';
  private figureLogos: Partial<Record<FigureLogoVariant, CanvasImageSource>> = {};

  chartSize: FigureSizeSettings = readStoredSizeSettings(TL_CHARTS_SIZE_STORAGE_KEY, this.defaultChartSize, false);
  /** The file's pixel size: the last usable chart size's. */
  private target: CcTargetPixels = ccTargetPixels(this.chartSize) ?? ccTargetPixels(this.defaultChartSize)!;
  imageFormat: FigureExportFormat = this.storedDownload.format;
  webpQuality: WebpQuality = this.storedDownload.webpQuality;
  chartSizeOpen = this.storedDownload.chartSizeOpen;
  imageFormatOpen = this.storedDownload.imageFormatOpen;

  // --- Figures and exports ---

  figures: TelemetryFigure[] = [];
  private summaries = new Map<TelemetryFigureKey, string>();
  /** When the series arrived: the charts' footer time. */
  private loadedAt = new Date().toISOString();

  /** Set while a copy, a download or *Download all* runs; the dialog refuses to close meanwhile. */
  exporting = false;
  /** The outcome of the last copy or download. */
  exportStatus = '';

  // --- Measuring and composition ---

  private viewportBox: { width: number; height: number } | null = null;
  private scrollbarWidth = 0;
  /** An All charts tile's image width, in whole CSS px; 0 until measured. */
  private tileWidth = 0;
  private dpr = ccPreviewDpr(typeof window !== 'undefined' ? window.devicePixelRatio || 1 : 1);
  private nearKeys = new Set<TelemetryFigureKey>();
  private readonly nearAll = typeof IntersectionObserver === 'undefined';
  private composeVersion = 0;
  private screenImages = new Map<TelemetryFigureKey, TelemetryChartsScreenImage>();
  /** The bitmap last copied into each on-screen canvas. */
  private painted = new WeakMap<HTMLCanvasElement, HTMLCanvasElement>();
  private composeTimer: ReturnType<typeof setTimeout> | null = null;
  private composeFrame: number | null = null;
  private composing = false;
  private composeAgain = false;
  private styleTimer: ReturnType<typeof setTimeout> | null = null;

  // --- Fetching ---

  /** Bumped by every request; a response of an older one is dropped. */
  private fetchGeneration = 0;
  private fetchTimer: ReturnType<typeof setTimeout> | null = null;
  private fetchSubscription: Subscription | null = null;

  private isOpen = false;
  private destroyed = false;

  @ViewChild('dialog') private dialogRef?: ElementRef<HTMLDialogElement>;
  @ViewChild('title') private titleRef?: ElementRef<HTMLElement>;
  @ViewChild('workspace') private workspaceRef?: ElementRef<HTMLElement>;
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
  private tileChanges: Subscription | null = null;
  private measureFrame: number | null = null;
  private measureFollowUps = 0;

  // --- Lifecycle ---

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngAfterViewInit(): void {
    this.tileChanges = this.tileRefs?.changes.subscribe(() => {
      this.observeTiles();
      this.scheduleMeasure();
      refreshAnchorPositioning();
    }) ?? null;
  }

  ngAfterViewChecked(): void {
    this.paintImages();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.isOpen = false;
    this.cancelFetch();
    this.cancelScheduledCompose();
    if (this.styleTimer !== null) clearTimeout(this.styleTimer);
    if (this.measureFrame !== null) cancelAnimationFrame(this.measureFrame);
    this.styleTimer = null;
    this.measureFrame = null;
    this.viewportObserver?.disconnect();
    this.tileObserver?.disconnect();
    this.tileChanges?.unsubscribe();
    this.screenImages.clear();
    const dialog = this.dialogRef?.nativeElement;
    if (dialog?.open) dialog.close();
  }

  // --- Dialog ---

  /** Shows the dialog on the request's range, keys, roles and user, and fetches the series at once. */
  open(request: TelemetryChartsOpenRequest): void {
    this.request = request;
    this.keys = request.keys;
    const roles = TL_CHARTS_ROLE_OPTIONS.map(option => option.value).filter(role => request.roles.includes(role));
    this.roles = roles.length > 0 ? roles : [...DEFAULT_ROLES];
    this.user = request.userId ? { id: request.userId, name: request.userName || 'Unknown user' } : null;
    if (this.splitBy === 'user' && !this.userKeysIncluded) this.splitBy = 'none';
    this.dto = null;
    this.figures = [];
    this.summaries.clear();
    this.screenImages.clear();
    this.error = null;
    this.bucketError = null;
    this.exportStatus = '';
    this.isOpen = true;
    const dialog = this.dialogRef?.nativeElement;
    if (dialog && !dialog.open) dialog.showModal();
    this.fetchNow();
    this.cdr.detectChanges();
    this.titleRef?.nativeElement.focus();
    refreshAnchorPositioning();
    void this.prepareComposition();
    this.scheduleMeasure();
  }

  /** Refused while an export runs. */
  close(): void {
    if (this.exporting) return;
    this.isOpen = false;
    this.cancelFetch();
    this.cancelScheduledCompose();
    const dialog = this.dialogRef?.nativeElement;
    if (dialog?.open) dialog.close();
    this.cdr.markForCheck();
  }

  /** Escape: closes this dialog and nothing behind it; refused while an export runs. */
  onCancel(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    this.close();
  }

  /** The native close, however it came: stopped here, and the state follows when it was not `close()`. */
  onDialogClose(event: Event): void {
    event.stopPropagation();
    if (this.isOpen) {
      this.isOpen = false;
      this.cancelFetch();
      this.cancelScheduledCompose();
      this.cdr.markForCheck();
    }
  }

  /** `AI Telemetry Charts`, with the user's name while filtered. */
  get dialogTitle(): string {
    return this.user ? `AI Telemetry Charts — ${this.user.name}` : 'AI Telemetry Charts';
  }

  get rangeLabel(): string {
    return telemetryRangeLabel(this.request?.start, this.request?.end);
  }

  /** The keys include user keys, so the *User* split is offered. */
  get userKeysIncluded(): boolean {
    return this.keys !== 'system';
  }

  get splitOptions(): readonly { readonly value: AiTelemetrySplitBy; readonly label: string }[] {
    return this.userKeysIncluded ? TL_CHARTS_SPLIT_OPTIONS : SPLIT_OPTIONS_WITHOUT_USER;
  }

  get splitHint(): string {
    const base = 'The usage charts draw one line per value: at most eight, and an Other line. The activity charts are not split.';
    return this.userKeysIncluded ? base : `${base} User is offered with user keys.`;
  }

  // --- Fetching ---

  private get query(): AiTelemetrySeriesQuery {
    return {
      ...(this.request?.start ? { startDate: this.request.start } : {}),
      ...(this.request?.end ? { endDate: this.request.end } : {}),
      keys: this.keys,
      roles: [...this.roles],
      bucket: this.bucket,
      splitBy: this.splitBy,
      ...(this.user ? { userId: this.user.id } : {})
    };
  }

  /** Fetches once the Data settings have been quiet for the debounce; a newer change restarts it. */
  private scheduleFetch(): void {
    if (this.fetchTimer !== null) clearTimeout(this.fetchTimer);
    this.loading = true;
    this.fetchTimer = setTimeout(() => {
      this.fetchTimer = null;
      this.fetchNow();
    }, TL_CHARTS_FETCH_DEBOUNCE_MS);
  }

  private fetchNow(): void {
    if (this.fetchTimer !== null) clearTimeout(this.fetchTimer);
    this.fetchTimer = null;
    this.fetchSubscription?.unsubscribe();
    const generation = ++this.fetchGeneration;
    this.loading = true;
    this.error = null;
    this.bucketError = null;
    this.cdr.markForCheck();
    this.fetchSubscription = this.admin.getAiTelemetrySeries(this.query).subscribe({
      next: dto => {
        if (generation !== this.fetchGeneration || this.destroyed) return;
        this.loading = false;
        this.dto = dto;
        this.loadedAt = new Date().toISOString();
        this.rebuildFigures();
        this.cdr.markForCheck();
        this.scheduleMeasure();
      },
      error: (error: unknown) => {
        if (generation !== this.fetchGeneration || this.destroyed) return;
        const failure = seriesErrorText(error);
        this.loading = false;
        this.error = failure.message;
        this.bucketError = failure.bucket ? failure.message : null;
        this.cdr.markForCheck();
      }
    });
  }

  private cancelFetch(): void {
    if (this.fetchTimer !== null) clearTimeout(this.fetchTimer);
    this.fetchTimer = null;
    this.fetchSubscription?.unsubscribe();
    this.fetchSubscription = null;
    this.fetchGeneration++;
    this.loading = false;
  }

  /** The series has no request in any bucket. */
  get usageEmpty(): boolean {
    return this.dto !== null && telemetryUsageEmpty(this.dto);
  }

  // --- Data tab ---

  setKeys(value: string): void {
    if (!TL_CHARTS_KEY_OPTIONS.some(option => option.value === value) || value === this.keys) return;
    this.keys = value as AiTelemetryKeySource;
    if (this.splitBy === 'user' && !this.userKeysIncluded) this.splitBy = 'none';
    this.afterDataChange();
  }

  isRoleSelected(role: AiUsageRoleGroup): boolean {
    return this.roles.includes(role);
  }

  /** At least one role stays selected: unchecking the last one is undone. */
  setRole(role: AiUsageRoleGroup, on: boolean, input?: HTMLInputElement): void {
    if (on === this.roles.includes(role)) return;
    if (!on && this.roles.length === 1) {
      if (input) input.checked = true;
      return;
    }
    const next = new Set(this.roles);
    if (on) next.add(role); else next.delete(role);
    this.roles = TL_CHARTS_ROLE_OPTIONS.map(option => option.value).filter(value => next.has(value));
    this.afterDataChange();
  }

  setBucket(value: string): void {
    if (!TL_CHARTS_BUCKET_OPTIONS.some(option => option.value === value) || value === this.bucket) return;
    this.bucket = value as AiTelemetryBucket;
    this.afterDataChange();
  }

  setSplitBy(value: string): void {
    if (!this.splitOptions.some(option => option.value === value) || value === this.splitBy) return;
    this.splitBy = value as AiTelemetrySplitBy;
    this.afterDataChange();
  }

  /** Applies to the chart from the series already loaded; nothing is fetched. */
  setCumulative(on: boolean): void {
    if (on === this.cumulative) return;
    this.cumulative = on;
    this.persistLayout();
    this.rebuildFigures();
    this.cdr.markForCheck();
  }

  /** Back to every user; focus moves to the Data panel, since the chip leaves. */
  removeUserFilter(): void {
    if (!this.user) return;
    this.user = null;
    this.afterDataChange();
    this.cdr.detectChanges();
    document.getElementById('tl-charts-side-panel-data')?.focus();
  }

  private afterDataChange(): void {
    this.persistLayout();
    this.scheduleFetch();
    this.cdr.markForCheck();
  }

  isDataSectionOpen(name: TelemetryChartsDataSection): boolean {
    return this.openDataSections.has(name);
  }

  /** Follows the native `toggle`, which fires for a click, a key and a bound `open` alike. */
  onDataSectionToggle(name: TelemetryChartsDataSection, event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (open === this.openDataSections.has(name)) return;
    const next = new Set(this.openDataSections);
    if (open) next.add(name); else next.delete(name);
    this.openDataSections = next;
    this.persistLayout();
  }

  /** The one-line summary a closed Data section shows of its values. */
  dataReadout(name: TelemetryChartsDataSection): string {
    switch (name) {
      case 'scope':
        return `${telemetryKeysLabel(this.keys)} · ${this.roles.map(role => TL_CHARTS_ROLE_OPTIONS.find(option => option.value === role)?.label ?? role).join(', ')}`;
      case 'lines': {
        const bucket = TL_CHARTS_BUCKET_OPTIONS.find(option => option.value === this.bucket)?.label ?? this.bucket;
        const split = TL_CHARTS_SPLIT_OPTIONS.find(option => option.value === this.splitBy)?.label ?? this.splitBy;
        return `${bucket} · split by ${split.toLowerCase()}`;
      }
      case 'cost':
        return this.cumulative ? 'Cumulative' : 'Per bucket';
    }
  }

  // --- Sidebar ---

  measureSidebarWidthMax(): void {
    const workspace = this.workspaceRef?.nativeElement.clientWidth ?? 0;
    this.sidebarWidthMax = workspace > 0
      ? Math.max(TL_CHARTS_SIDEBAR_WIDTH_MIN, Math.min(TL_CHARTS_SIDEBAR_WIDTH_MAX, Math.floor(workspace / 2)))
      : TL_CHARTS_SIDEBAR_WIDTH_MAX;
    this.cdr.markForCheck();
  }

  onSidebarWidthChange(width: number): void {
    this.sidebarWidth = width;
    this.cdr.markForCheck();
  }

  onSidebarWidthCommit(width: number): void {
    this.sidebarWidth = width;
    this.persistLayout();
    this.cdr.markForCheck();
  }

  /** Focus stays on the toggle, which sits outside the sidebar and is always rendered. */
  toggleSidebar(): void {
    this.sidebarCollapsed = !this.sidebarCollapsed;
    this.persistLayout();
    this.cdr.detectChanges();
    refreshAnchorPositioning();
    this.scheduleMeasure();
  }

  selectSidebarTab(tab: TelemetryChartsSidebarTab): void {
    if (tab === this.sidebarTab) return;
    this.sidebarTab = tab;
    this.persistLayout();
    this.cdr.detectChanges();
    refreshAnchorPositioning();
  }

  /** Left/Right move and wrap, Home/End jump to the ends; focus follows selection. */
  onSidebarTabKeydown(event: KeyboardEvent, index: number): void {
    const next = rovingTabIndex(event, index, this.sidebarTabs.length);
    if (next === null) return;
    const tab = this.sidebarTabs[next].id;
    this.selectSidebarTab(tab);
    document.getElementById(`tl-charts-side-tab-${tab}`)?.focus();
  }

  // --- Theme and Charts tabs ---

  /**
   * Stores the style at once, so the panel's own controls follow it, and composes the charts again
   * once the change pauses. A newly chosen font, and a newly shown or chosen logo, starts loading.
   */
  onFigureStyleChange(style: FigureStyle): void {
    const previous = this.figureStyle.appearance;
    this.figureStyle = normalizeFigureStyle(style);
    writeStoredRecord(TL_CHARTS_FIGURE_STYLE_STORAGE_KEY, { appearance: this.figureStyle.appearance, timeline: this.figureStyle.timeline });
    const appearance = this.figureStyle.appearance;
    if (appearance.fontFamily !== previous.fontFamily) void this.loadFigureFont();
    if (appearance.logo && (appearance.logo !== previous.logo || appearance.logoVariant !== previous.logoVariant)) {
      void this.loadFigureLogo();
    }
    if (this.styleTimer !== null) clearTimeout(this.styleTimer);
    this.styleTimer = setTimeout(() => {
      this.styleTimer = null;
      if (this.destroyed) return;
      this.figureTheme = resolveFigureTheme(this.figureStyle.appearance);
      this.rebuildFigures();
      this.cdr.markForCheck();
    }, COMPOSE_DEBOUNCE_MS);
    this.cdr.markForCheck();
  }

  get isTransparentFigure(): boolean {
    return this.figureStyle.appearance.background === 'transparent';
  }

  /** The backdrop color for the canvases' scroller to pass down; null keeps the checkerboard. */
  get figureBackdropStyle(): string | null {
    const appearance = this.figureStyle.appearance;
    return appearance.background === 'transparent' && appearance.previewBackdrop === 'color'
      ? `--gh-fig-backdrop: ${appearance.previewBackdropColor}`
      : null;
  }

  private async loadFigureFont(): Promise<void> {
    const id = this.figureStyle.appearance.fontFamily;
    const loaded = await ensureFigureFont(id);
    const label = figureFont(id).label;
    const status = id === 'default' ? '' : loaded ? `${label} is loaded.` : `${label} could not be loaded, so the charts use the fallback font.`;
    if (id === this.figureStyle.appearance.fontFamily && status !== this.fontLoadStatus && !this.destroyed) {
      this.fontLoadStatus = status;
      this.cdr.markForCheck();
    }
  }

  private async loadFigureLogo(): Promise<void> {
    const appearance = this.figureStyle.appearance;
    if (!appearance.logo) return;
    const variant = appearance.logoVariant;
    const image = await ensureFigureLogo(variant);
    if (image) this.figureLogos[variant] = image;
  }

  /** The font and the logo every composition measures and draws with. */
  private async prepareComposition(): Promise<void> {
    await Promise.all([this.loadFigureFont(), this.loadFigureLogo()]);
  }

  private figureLogo(): FigureLogo | null {
    const appearance = this.figureStyle.appearance;
    const image = appearance.logo ? this.figureLogos[appearance.logoVariant] : undefined;
    return image ? { image, aspectRatio: figureLogoAspect(appearance.logoVariant), heightPx: appearance.logoHeightPx } : null;
  }

  private composeContext(): TelemetryComposeContext {
    return {
      style: { appearance: this.figureStyle.appearance, timeline: this.figureStyle.timeline },
      theme: this.figureTheme,
      logo: this.figureLogo(),
      rangeLabel: this.rangeLabel,
      keys: this.keys,
      cumulative: this.cumulative,
      userName: this.dto?.userId ? (this.dto.userName || this.user?.name || null) : null,
      loadedAt: this.loadedAt
    };
  }

  /** Builds the nine charts for composition, with their image summaries, and marks every image stale. */
  private rebuildFigures(): void {
    const dto = this.dto;
    const context = this.composeContext();
    this.figures = dto ? buildTelemetryFigures(dto, context) : [];
    this.summaries = new Map(this.figures.map(figure => [figure.key, telemetryFigureSummary(figure, telemetryFigureChrome(figure, context))]));
    this.invalidateImages(0);
  }

  // --- Download tab ---

  get sizeError(): string {
    return sizeErrors(this.chartSize, 'chart').any;
  }

  onChartSizeChange(settings: FigureSizeSettings): void {
    this.chartSize = { ...settings };
    writeStoredSizeSettings(TL_CHARTS_SIZE_STORAGE_KEY, this.chartSize);
    this.target = ccTargetPixels(this.chartSize) ?? this.target;
    this.cdr.markForCheck();
    this.scheduleMeasure();
    this.invalidateImages(COMPOSE_DEBOUNCE_MS);
  }

  resetChartSize(): void {
    if (!sameFigureSize(this.chartSize, this.defaultChartSize)) this.onChartSizeChange(this.defaultChartSize);
  }

  onChartSizeOpenChange(open: boolean): void {
    this.chartSizeOpen = open;
    this.persistDownload();
  }

  onImageFormatChange(format: FigureExportFormat): void {
    this.imageFormat = format;
    this.persistDownload();
    this.cdr.markForCheck();
  }

  onWebpQualityChange(quality: WebpQuality): void {
    this.webpQuality = quality;
    this.persistDownload();
    this.cdr.markForCheck();
  }

  onImageFormatOpenChange(open: boolean): void {
    this.imageFormatOpen = open;
    this.persistDownload();
  }

  resetImageFormat(): void {
    this.imageFormat = 'png';
    this.webpQuality = DEFAULT_WEBP_QUALITY;
    this.persistDownload();
    this.cdr.markForCheck();
  }

  // --- Views ---

  selectView(view: TelemetryChartsView): void {
    if (view === this.view) return;
    this.view = view;
    if (view === 'single') this.singleZoom = 'fitScreen';
    this.persistLayout();
    this.cdr.detectChanges();
    refreshAnchorPositioning();
    this.scheduleMeasure();
    this.invalidateImages(0);
  }

  onViewTabKeydown(event: KeyboardEvent, index: number): void {
    const next = rovingTabIndex(event, index, this.viewTabs.length);
    if (next === null) return;
    const view = this.viewTabs[next].id;
    this.selectView(view);
    document.getElementById(`tl-charts-view-tab-${view}`)?.focus();
  }

  /** The chart Single chart shows: the chosen one, else the first. */
  get singleFigure(): TelemetryFigure | null {
    return this.figures.find(figure => figure.key === this.singleKey) ?? this.figures[0] ?? null;
  }

  selectSingleFigure(key: string): void {
    if (!TELEMETRY_FIGURE_KEYS.includes(key as TelemetryFigureKey)) return;
    this.singleKey = key as TelemetryFigureKey;
    this.persistLayout();
    this.cdr.markForCheck();
    this.scheduleCompose(0);
  }

  /** Wrapping, so neither end is a dead control. */
  stepSingleFigure(delta: number): void {
    const figures = this.figures;
    if (figures.length === 0) return;
    const current = figures.findIndex(figure => figure.key === this.singleFigure?.key);
    const next = ((current < 0 ? 0 : current + delta) + figures.length) % figures.length;
    this.selectSingleFigure(figures[next].key);
  }

  /** A tile's *Open in Single view*: Single chart on that chart, focus on the chart select. */
  openInSingle(key: TelemetryFigureKey): void {
    this.selectSingleFigure(key);
    if (this.view === 'single') {
      this.cdr.detectChanges();
    } else {
      this.selectView('single');
    }
    document.getElementById('tl-charts-single-figure')?.focus();
  }

  setSingleZoom(zoom: TelemetryChartsZoom): void {
    this.singleZoom = zoom;
    this.cdr.markForCheck();
    this.scheduleMeasure();
    this.invalidateImages(COMPOSE_DEBOUNCE_MS);
  }

  /** In All charts, whether the tile is near enough to the view to be composed. */
  isNear(key: TelemetryFigureKey): boolean {
    return this.nearAll || this.nearKeys.has(key);
  }

  /** The file's pixel size as a CSS aspect ratio, for the tiles' reserved image boxes. */
  get aspectRatio(): string {
    return `${this.target.pixelWidth} / ${this.target.pixelHeight}`;
  }

  /** The Single chart's zoom: device pixels per file pixel at the chosen fit; NaN until measured. */
  private get singleZoomValue(): number {
    const box = this.viewportBox;
    if (!box) return Number.NaN;
    return this.singleZoom === 'fitWidth'
      ? ccFitWidthWithScrollbar(this.target, box.width, box.height, NO_CHROME, this.dpr, this.scrollbarWidth)
      : ccFitScreenZoom(this.target, box.width, box.height, NO_CHROME, this.dpr);
  }

  /** The Single chart's image box, in whole CSS px; null until measured. */
  get singleBox(): { cssWidth: number; cssHeight: number } | null {
    const zoom = this.singleZoomValue;
    return Number.isFinite(zoom) && zoom > 0 ? ccDisplaySize(this.target, zoom, this.dpr, true) : null;
  }

  /** What a figure shows: its last composition until a newer one replaces it; null while the first is pending. */
  imageFor(key: TelemetryFigureKey): TelemetryChartsScreenImage | null {
    return this.screenImages.get(key) ?? null;
  }

  /** The image's accessible name: what it is, then its badges, lines and notes. */
  imageLabel(figure: TelemetryFigure): string {
    const summary = this.summaries.get(figure.key) ?? '';
    return summary ? `${figure.title}, a line chart by date. ${summary}` : `${figure.title}, a line chart by date.`;
  }

  // --- Copy and Download ---

  /** Why nothing can be exported now, or empty. */
  get exportBlockReason(): string {
    if (this.exporting) return 'An export is running.';
    if (!this.dto) return this.loading ? 'The series is loading.' : 'No series is loaded.';
    return this.sizeError;
  }

  chartBlockReason(figure: TelemetryFigure | null): string {
    if (!figure) return 'No chart is shown.';
    return this.exportBlockReason || (figure.config ? '' : `${figure.title} has nothing to draw in this range.`);
  }

  get downloadAllBlockReason(): string {
    if (this.exportBlockReason) return this.exportBlockReason;
    return this.figures.some(figure => figure.config) ? '' : 'No chart has anything to draw in this range.';
  }

  /** `Full HD — 1920 × 1080 · 100% · PNG`. */
  get exportSummary(): string {
    const format = this.imageFormat === 'webp' ? `WebP q${this.webpQuality}` : 'PNG';
    return `${resolveSizeResolution(this.chartSize).label} · ${densityPercentLabel(resolveSizeDensity(this.chartSize))} · ${format}`;
  }

  copyTooltip(figure: TelemetryFigure | null): string {
    return this.chartBlockReason(figure) || 'Copy as a PNG image';
  }

  downloadTooltip(figure: TelemetryFigure | null): string {
    return this.chartBlockReason(figure) || `Download this chart — ${this.exportSummary}`;
  }

  get downloadAllTooltip(): string {
    return this.downloadAllBlockReason || `Every chart as one ZIP — ${this.exportSummary}`;
  }

  private setExporting(exporting: boolean): void {
    this.exporting = exporting;
    this.cdr.markForCheck();
  }

  private exportSnapshot(): ExportSnapshot {
    return { size: { ...this.chartSize }, dto: this.dto, context: this.composeContext(), webpQuality: this.webpQuality };
  }

  private async preparedExportSnapshot(): Promise<ExportSnapshot> {
    await this.prepareComposition();
    return this.exportSnapshot();
  }

  /** Composes one chart at the chart size, exactly as the screen shows it, and encodes it. */
  private async chartImage(key: TelemetryFigureKey, format: FigureExportFormat, snapshot: ExportSnapshot): Promise<ChartImage> {
    if (!snapshot.dto) return { result: null, refusal: null, empty: true };
    const figure = buildTelemetryFigure(snapshot.dto, key, snapshot.context);
    if (!figure.config) return { result: null, refusal: null, empty: true };
    const target = telemetryFigureLayout(figure, snapshot.context, snapshot.size);
    if (!target.layout) return { result: null, refusal: target.refusal ?? '', empty: false };
    const canvas = await composeTelemetryFigure(figure, snapshot.context, target.layout);
    if (!canvas) return { result: null, refusal: null, empty: false };
    return { result: await encodeFigureImage(canvas, format, snapshot.webpQuality), refusal: null, empty: false };
  }

  /** Always a PNG, the image type clipboards take. */
  async copyChart(figure: TelemetryFigure | null): Promise<void> {
    if (!figure || this.chartBlockReason(figure)) return;
    const title = figure.title;
    this.exportStatus = '';
    this.setExporting(true);
    try {
      const image = await this.chartImage(figure.key, 'png', await this.preparedExportSnapshot());
      if (!image.result) {
        this.exportStatus = image.refusal || `${title} could not be copied.`;
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

  async downloadChart(figure: TelemetryFigure | null): Promise<void> {
    if (!figure || this.chartBlockReason(figure)) return;
    const title = figure.title;
    this.exportStatus = '';
    this.setExporting(true);
    try {
      const image = await this.chartImage(figure.key, this.imageFormat, await this.preparedExportSnapshot());
      if (!image.result) {
        this.exportStatus = image.refusal || `${title} could not be downloaded.`;
        return;
      }
      saveFigureBlob(image.result.blob, telemetryChartFilename(figure.key, image.result.format, new Date()));
      this.exportStatus = `Downloaded ${title}.${image.result.fellBackToPng ? ' Written as PNG: this browser cannot encode WebP.' : ''}`;
    } catch {
      this.exportStatus = `${title} could not be downloaded.`;
    } finally {
      this.setExporting(false);
    }
  }

  /** Every chart with something to draw, in order, as one ZIP; a chart with nothing to draw is skipped and named. */
  async downloadAllCharts(): Promise<void> {
    if (this.downloadAllBlockReason) return;
    const figures = [...this.figures];
    const format = this.imageFormat;
    this.exportStatus = '';
    this.setExporting(true);
    const stamp = new Date();
    const entries: (FigureArchiveEntry & { title: string })[] = [];
    const skipped: string[] = [];
    const failed: string[] = [];
    let fellBack = false;
    try {
      const snapshot = await this.preparedExportSnapshot();
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
        entries.push({ name: telemetryChartFilename(figure.key, image.result.format, stamp), blob: image.result.blob, title: figure.title });
      }
      let outcome: string;
      if (entries.length === 1) {
        saveFigureBlob(entries[0].blob, entries[0].name);
        outcome = `Downloaded ${entries[0].title}.`;
      } else if (entries.length > 1) {
        const archive = await buildFigureArchive(entries.map(entry => ({ name: entry.name, blob: entry.blob })));
        saveFigureBlob(archive, telemetryArchiveFilename(stamp));
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

  // --- Observers and measuring ---

  private observeViewport(): void {
    this.viewportObserver?.disconnect();
    this.viewportObserver = null;
    const viewport = this.viewportEl;
    if (viewport && typeof ResizeObserver !== 'undefined') {
      // A size of zero (a closed dialog) keeps the last fit; the observer fires again once shown.
      this.viewportObserver = new ResizeObserver(() => this.zone.run(() => this.scheduleMeasure()));
      this.viewportObserver.observe(viewport);
    }
    this.observeTiles();
  }

  /** Only tiles within one viewport height of view are composed; the rest keep their reserved box. */
  private observeTiles(): void {
    this.tileObserver?.disconnect();
    this.tileObserver = null;
    const viewport = this.viewportEl;
    if (!viewport || this.view !== 'all' || typeof IntersectionObserver === 'undefined') return;
    const observer = new IntersectionObserver(
      entries => this.zone.run(() => this.onTilesIntersect(entries)),
      { root: viewport, rootMargin: '100% 0px' }
    );
    viewport.querySelectorAll<HTMLElement>('.tl-charts-tile').forEach(tile => observer.observe(tile));
    this.tileObserver = observer;
  }

  private onTilesIntersect(entries: IntersectionObserverEntry[]): void {
    const next = new Set(this.nearKeys);
    for (const entry of entries) {
      const key = (entry.target as HTMLElement).dataset['figure'] as TelemetryFigureKey | undefined;
      if (!key) continue;
      if (entry.isIntersecting) next.add(key); else next.delete(key);
    }
    if (next.size !== this.nearKeys.size || [...next].some(key => !this.nearKeys.has(key))) {
      this.nearKeys = next;
      this.cdr.markForCheck();
      this.scheduleCompose(0);
    }
  }

  /** Measures on the next frame, so the views have laid out; a measurement that changed something measures again. */
  private scheduleMeasure(followUps = 3): void {
    this.measureFollowUps = followUps;
    if (this.measureFrame !== null || this.destroyed) return;
    this.measureFrame = requestAnimationFrame(() => {
      this.measureFrame = null;
      if (this.destroyed || !this.measure()) return;
      this.cdr.markForCheck();
      if (this.measureFollowUps > 0) this.scheduleMeasure(this.measureFollowUps - 1);
    });
  }

  /**
   * The viewport's content box with no scrollbar, the scrollbar it shows, an All charts tile's image
   * width and the device ratio; false when nothing changed or nothing has a size. A change marks every
   * image stale.
   */
  private measure(): boolean {
    const viewport = this.viewportEl;
    if (!viewport) return false;
    const style = getComputedStyle(viewport);
    const px = (value: string) => Number.parseFloat(value) || 0;
    const rect = viewport.getBoundingClientRect();
    const borders = {
      left: px(style.borderLeftWidth), right: px(style.borderRightWidth),
      top: px(style.borderTopWidth), bottom: px(style.borderBottomWidth)
    };
    const { width, height } = previewStageContentBox(rect.width, rect.height, {
      left: borders.left + px(style.paddingLeft),
      right: borders.right + px(style.paddingRight),
      top: borders.top + px(style.paddingTop),
      bottom: borders.bottom + px(style.paddingBottom)
    });
    if (!(width > 0) || !(height > 0)) return false;
    const scrollbar = Math.max(0, viewport.offsetWidth - viewport.clientWidth - borders.left - borders.right);
    const tile = viewport.querySelector<HTMLElement>('.tl-charts-tile .tl-charts-image-box');
    const tileWidth = tile ? Math.floor(tile.getBoundingClientRect().width + 1e-6) : 0;
    const dpr = ccPreviewDpr(window.devicePixelRatio || 1);
    const before = this.viewportBox;
    const changed = !before
      || Math.abs(before.width - width) >= MEASURE_TOLERANCE
      || Math.abs(before.height - height) >= MEASURE_TOLERANCE
      || (scrollbar > 0 && scrollbar !== this.scrollbarWidth)
      || tileWidth !== this.tileWidth
      || dpr !== this.dpr;
    if (!changed) return false;
    this.viewportBox = { width, height };
    this.tileWidth = tileWidth;
    this.dpr = dpr;
    if (scrollbar > 0) this.scrollbarWidth = scrollbar;
    this.invalidateImages(before ? COMPOSE_DEBOUNCE_MS : 0);
    return true;
  }

  // --- Composition ---

  /** Marks every image stale and composes the visible ones once changes have been quiet for `delay` ms. */
  private invalidateImages(delay: number): void {
    this.composeVersion++;
    this.scheduleCompose(delay);
  }

  private scheduleCompose(delay: number): void {
    if (this.destroyed) return;
    this.cancelScheduledCompose();
    this.composeTimer = setTimeout(() => {
      this.composeTimer = null;
      this.composeFrame = requestAnimationFrame(() => {
        this.composeFrame = null;
        void this.composeVisible();
      });
    }, delay);
  }

  private cancelScheduledCompose(): void {
    if (this.composeTimer !== null) clearTimeout(this.composeTimer);
    if (this.composeFrame !== null) cancelAnimationFrame(this.composeFrame);
    this.composeTimer = null;
    this.composeFrame = null;
  }

  /** One pass at a time; a request during a pass runs another after it. */
  private async composeVisible(): Promise<void> {
    if (this.composing) {
      this.composeAgain = true;
      return;
    }
    this.composing = true;
    try {
      await this.composeStale();
    } finally {
      this.composing = false;
    }
    if (this.composeAgain && !this.destroyed) {
      this.composeAgain = false;
      this.scheduleCompose(0);
    }
  }

  /** The zoom the shown view rasterizes at: an All tile's width, or the Single chart's fit; NaN until measured. */
  private get screenZoom(): number {
    if (this.view === 'single') return this.singleZoomValue;
    return this.tileWidth > 0 ? this.tileWidth * this.dpr / this.target.pixelWidth : Number.NaN;
  }

  /**
   * Composes each visible chart whose image is older than the current version: the Single chart, or the
   * tiles near the All view. Every pass awaits the font and the logo first; a newer version abandons it.
   */
  private async composeStale(): Promise<void> {
    const version = this.composeVersion;
    if (!this.dto || !this.isOpen || this.destroyed) return;
    await this.prepareComposition();
    if (version !== this.composeVersion || this.destroyed) return;
    const single = this.singleFigure;
    const figures = this.view === 'all'
      ? this.figures.filter(figure => figure.config && this.isNear(figure.key))
      : single && single.config ? [single] : [];
    for (const figure of figures) {
      if (this.screenImages.get(figure.key)?.version === version) continue;
      const zoom = this.screenZoom;
      if (!(zoom > 0)) return;
      const image = await this.composeForScreen(figure, zoom);
      if (version !== this.composeVersion || this.destroyed) return;
      this.screenImages.set(figure.key, { ...image, version });
      this.cdr.markForCheck();
    }
  }

  /** The file's own layout, rasterized at `zoom`, composed for the screen; or why the file is refused. */
  private async composeForScreen(figure: TelemetryFigure, zoom: number): Promise<{ canvas: HTMLCanvasElement | null; refusal: string }> {
    const context = this.composeContext();
    const target = telemetryFigureLayout(figure, context, this.chartSize);
    if (!target.layout) return { canvas: null, refusal: target.refusal ?? '' };
    const layout = ccPreviewLayout(target.layout, zoom, this.dpr)?.layout ?? null;
    if (!layout) return { canvas: null, refusal: '' };
    let canvas: HTMLCanvasElement | null;
    try {
      canvas = await composeTelemetryFigure(figure, context, layout);
    } catch {
      canvas = null;
    }
    return { canvas, refusal: canvas ? '' : `${figure.title} could not be composed: its chart could not be built.` };
  }

  /**
   * Copies each composed bitmap into its on-screen canvas at its own pixel size; CSS scales it. The
   * canvases are found in the DOM, since the image box is a shared template.
   */
  private paintImages(): void {
    const dialog = this.dialogRef?.nativeElement;
    if (!dialog) return;
    dialog.querySelectorAll<HTMLCanvasElement>('canvas.tl-charts-canvas').forEach(target => {
      const key = target.dataset['figure'] as TelemetryFigureKey | undefined;
      const source = key ? this.screenImages.get(key)?.canvas ?? null : null;
      if (!source || this.painted.get(target) === source) return;
      target.width = source.width;
      target.height = source.height;
      target.getContext('2d')?.drawImage(source, 0, 0);
      this.painted.set(target, source);
    });
  }

  // --- Tooltips and storage ---

  tipId(action: string, key: string): string {
    return `tl-charts-tip-${action}-${key}`;
  }

  private persistLayout(): void {
    const layout: TelemetryChartsLayout = {
      collapsed: this.sidebarCollapsed,
      width: this.sidebarWidth,
      tab: this.sidebarTab,
      view: this.view,
      figure: this.singleKey,
      bucket: this.bucket,
      splitBy: this.splitBy,
      cumulative: this.cumulative,
      dataSections: DATA_SECTIONS.map(section => section.name).filter(name => this.openDataSections.has(name))
    };
    writeStoredRecord(TL_CHARTS_STORAGE_KEY, layout);
  }

  private persistDownload(): void {
    const download: TelemetryChartsDownload = {
      format: this.imageFormat,
      webpQuality: this.webpQuality,
      chartSizeOpen: this.chartSizeOpen,
      imageFormatOpen: this.imageFormatOpen
    };
    writeStoredRecord(TL_CHARTS_DOWNLOAD_STORAGE_KEY, download);
  }
}
