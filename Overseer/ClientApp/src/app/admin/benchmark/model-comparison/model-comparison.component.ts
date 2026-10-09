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
import { firstValueFrom } from 'rxjs';
import type { Subscription } from 'rxjs';

import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { parseServerUtcDate } from '../../../utils/date.util';
import {
  FigureChrome,
  FigureFooter,
  FigureNote,
  figureSummary,
  formatComputedAt
} from './figure-chrome';
import {
  FIT_RESOLUTION_ID,
  FigureSizeSettings,
  defaultFigureSize,
  defaultTableImageSize,
  readStoredFigureSize,
  readStoredTableImageSize,
  resolveSizeDensity,
  resolveSizeResolution,
  sameFigureSize,
  sizeDimensionsLabel,
  sizeErrors,
  sizeReadout,
  writeStoredFigureSize,
  writeStoredTableImageSize
} from './figure-size';
import {
  DEFAULT_FIGURE_STYLE,
  FigureAppearanceStyle,
  FigureLogoVariant,
  FigureStyle,
  TableImageStyle,
  normalizeFigureStyle
} from './figure-style';
import { FigureStylePanelComponent, FigureStylePanelKind } from './figure-style-panel.component';
import { ResolvedFigureTheme, contrastRatio, resolveFigureTheme } from './figure-theme';
import { figureFont } from './figure-fonts';
import { ensureFigureFont } from './figure-font-loader';
import { FigureLogo, ensureFigureLogo, figureLogoAspect } from './figure-logo';
import { ExportSizeSectionComponent } from './export-size-section.component';
import { TableSettingsPanelComponent } from './table-settings-panel.component';
import { exactFilter, SortAccessor, TableState } from '../../../shared/data-table/table-state';
import { SortHeaderComponent } from '../../../shared/data-table/sort-header.component';
import { TablePagerComponent } from '../../../shared/data-table/table-pager.component';
import {
  ReorderableListComponent,
  ReorderableListItem
} from '../../../shared/reorderable-list/reorderable-list.component';
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
  ProfileFigure,
  ProfileNormalization,
  ReducedMotionWatcher,
  SortDirection,
  SpeedMeasure,
  buildComparisonFigures,
  buildNumberSamples,
  costValue,
  glyphFor,
  modelOrderKeys,
  normalizeProfile,
  speedValue,
  suiteCostSdUsd
} from './model-comparison-charts';
import { MEASURE_NAMES } from './measure-format';
import type { NumberSamples } from './measure-format';

import { MAX_COMPARISON_SOURCES } from './comparison-source-picker.component';
import {
  BenchmarkModelComparisonDto,
  BenchmarkModelComparisonEntryDto,
  BenchmarkModelComparisonPricingBasis,
  BenchmarkPanelDiagnosticsDto,
  BenchmarkPanelFamilyGapDto,
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
  FIGURE_EXPORT_MAX_TEXT_SCALE_PERCENT,
  FIGURE_EXPORT_MIN_TEXT_SCALE_PERCENT,
  FigureArchiveEntry,
  FigureExportFormat,
  FigureExportLayout,
  FigureExportRequest,
  FigureExportResolution,
  FigureExportResult,
  OffscreenPlotConfig,
  PreviewStage,
  WEBP_QUALITY_OPTIONS,
  WebpQuality,
  aspectRatioLabel,
  buildFigureArchive,
  composeFigureImage,
  copyImageToClipboard,
  densityPercentLabel,
  displayDensity,
  encodeFigureImage,
  figureArchiveFilename,
  figureExportFilename,
  previewLayoutFor,
  renderPlotOffscreen,
  renderTiledPlotOffscreen,
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
  previewStageContentBox,
  previewZoomRange,
  previousPreviewZoomStop,
  resolvePreviewZoom,
  sliderToZoom,
  wheelZoomFactor,
  zoomToSlider
} from './preview-view';
import { ProviderBadgeComponent } from '../../../shared/provider-badge/provider-badge.component';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { PaneResizerComponent } from '../../../shared/pane-resizer/pane-resizer.component';
import { ToastComponent, ToastNotice } from '../../../shared/toast/toast.component';
import { showReasoningBadge } from '../../../utils/model-badge-format.util';
import { AdminAlertService } from '../../../services/admin-alert.service';
import {
  AdminBenchmarkService,
  BenchmarkComparisonDto,
  BenchmarkReportAudience,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkReportPackJobDto,
  ReportDocumentChartLayout
} from '../../../services/admin-benchmark.service';
import { ReportPackContext, ReportPackPanelComponent } from '../report-pack/report-pack-panel.component';
import type { DocumentChartsComposer } from '../report-pack/report-chart-layout-preview';
import {
  ComposedReportChart,
  DOCUMENT_CHART_COLUMN_PT,
  DOCUMENT_MIN_CONTENT_WIDTH,
  DOCUMENT_MIN_PLOT_HEIGHT,
  DocumentChartLayout,
  REPORT_CHART_FIGURES,
  ReportChartDocumentLayout,
  ReportChartFigureKey,
  ReportChartLayoutSettings,
  ReportChartPublishProgress,
  ReportChartPublishResult,
  ReportChartPublisher,
  ReportChartRowStatus,
  ReportChartScope,
  ReportChartSelection,
  ReportChartSettingsInput,
  ReportChartTarget,
  ReportChartVariant,
  ReportChartWidth,
  canonicalJson,
  chartSettingsHash,
  documentChartHashLayout,
  documentChartLayout,
  documentChartLayoutFor,
  documentChartRefusal,
  documentChartWidth,
  documentFigureLayout,
  documentTextStyle,
  normalizeChartSelection,
  printFigureAppearance,
  readStoredChartSettings,
  reportChartCoveredKeys,
  reportChartTargetFor,
  reportDocumentChartLayout,
  storeChartSettings
} from '../report-pack/report-charts';
import { formatUtc } from '../report-pack/report-document-format';
import {
  DownloadCenterChartActions,
  DownloadCenterContext,
  DownloadCenterPanelComponent
} from '../download-center/download-center-panel.component';
import {
  anonymizeComparisonForAll,
  anonymizeComparisonForSubject,
  restrictComparisonToEntries
} from './report-chart-anonymize';
import { PairedTestsViewComponent } from './paired-tests-view.component';
import { ComparisonRenameDialogComponent } from './comparison-rename-dialog.component';
import {
  ComparisonTableCell,
  ComparisonTableModel,
  ComparisonTableProvenance,
  DEFAULT_TABLE_COLUMNS,
  TABLE_COLUMN_CONFIG_VERSION,
  TABLE_DISPLAY_COLUMNS,
  TableColumnConfig,
  TableDisplayColumn,
  TableExportResult,
  TableFileFormat,
  TableImageOptions,
  TableImageSize,
  TableModelFlavour,
  buildComparisonTableModel,
  comparisonStateLabel,
  comparisonTableCells,
  composeTableImage,
  encodeComparisonTable,
  familyGapPairText,
  familyRelationText,
  formatFamilyGapCellText,
  formatIndexText,
  formatMsText,
  formatOverturnGapText,
  formatPanelIndexText,
  formatRateText,
  formatUsdText,
  measureTableImage,
  migrateTableColumnConfig,
  normalizeTableColumnConfig,
  panelMemberText,
  panelPairsEmptyText,
  panelRoleText,
  populatedColumnKeys,
  resolveTableImageLayout,
  shownTableColumns,
  tableClipboardPayload,
  tableDisplayColumn,
  tableExportFilename
} from './table-export';

/** Which wizard step is on screen: sources, the charts and the table, the AI reports, the documents. */
export type ComparisonWizardStep = 1 | 2 | 3 | 4;

/** Step 4's note: where the documents not written by this wizard are kept. */
export const COMPARISON_DOCUMENTS_NOTE =
  'Run reports, and each run\'s or battery run\'s own AI reports, are in that report\'s Downloads.';

/** The bitmap a document figure is resolved at. */
function documentResolution(box: DocumentChartLayout): FigureExportResolution {
  return { id: 'document', label: 'Document', widthPx: box.pxWidth, heightPx: box.pxHeight, group: 'Document' };
}

/** The numbered comparison open in the wizard, as steps 3 and 4 take it. */
export interface ComparisonIdentityState {
  /** *Comparison #id*. */
  readonly id: number;
  /** The display name: the admin's rename, else the default name. */
  readonly name: string;
  /** The sorted `run:`, `group:` and `battery:` entry keys the server numbered. */
  readonly entryKeys: readonly string[];
}

/** What `comparisonIdentified` carries: the computed comparison and the numbered comparison it is. */
export interface ComparisonIdentifiedEvent {
  comparison: BenchmarkModelComparisonDto;
  identity: BenchmarkComparisonDto;
}

/**
 * Every wizard step with its title and a one-line summary of what it is for.
 *
 * Exported so the launcher lists the same steps under the same names as the wizard's own stepper.
 */
export const COMPARISON_WIZARD_STEPS: readonly {
  readonly step: ComparisonWizardStep; readonly title: string; readonly summary: string;
}[] = [
  { step: 1, title: 'Sources', summary: 'Choose the single runs or groups to compare, or the battery results, which are compared only with each other.' },
  { step: 2, title: 'Charts & table', summary: 'Choose the models to show, view the charts and the table, and export them.' },
  { step: 3, title: 'Reports', summary: 'Write AI reports that compare the models of this comparison.' },
  { step: 4, title: 'Documents', summary: 'View, chart, download and delete this comparison’s documents.' }
];

/** The system alert raised while the server has no folder for report charts. */
export const REPORT_CHARTS_ALERT_ID = 'report-charts-location-missing';

/** How long a comparison may run before the footer offers the ways out. */
const SLOW_COMPARISON_MS = 15_000;

/** Where the figure style is kept, per browser. Read and written in `try/catch`; never required. */
export const FIGURE_STYLE_STORAGE_KEY = 'overseer.modelComparison.figureStyle';

/**
 * Where the table's column configuration is kept, per browser, as
 * `{ version: TABLE_COLUMN_CONFIG_VERSION, order, shown }`. Read and written in `try/catch`, and
 * migrated and repaired on read; never required.
 */
export const TABLE_COLUMNS_STORAGE_KEY = 'overseer.modelComparison.tableColumns';

/**
 * Where the Download tab's formats are kept, per browser, as `{ version: 1, tableFormat,
 * imageFormat, webpQuality }`. Read and written in `try/catch`, validated field by field.
 */
export const DOWNLOAD_SETTINGS_STORAGE_KEY = 'overseer.modelComparison.download';

/**
 * Step 2's five views: every chart, one chart with zoom and pan, the sortable table, the table image
 * with zoom and pan, and the paired tests. The charts and the table image are composed by the export
 * pipeline.
 */
export type FigureViewTab = 'all' | 'single' | 'table' | 'tablePreview' | 'paired';

/** The two views that show charts. */
export function isChartView(tab: FigureViewTab): boolean {
  return tab === 'all' || tab === 'single';
}

/** The two views that show the table. */
export function isTableView(tab: FigureViewTab): boolean {
  return tab === 'table' || tab === 'tablePreview';
}

/** The Paired tests view, whose sidebar shows only Data. */
export function isPairedView(tab: FigureViewTab): boolean {
  return tab === 'paired';
}

/** The settings sidebar's tabs. Which of them are shown depends on the view group. */
export type FigureSidebarTab = 'data' | 'theme' | 'charts' | 'table' | 'download';

/** One sidebar tab as the tab row renders it. */
export interface FigureSidebarTabOption {
  readonly id: FigureSidebarTab;
  readonly label: string;
}

/** The chart views' tabs, in order. */
export const CHART_VIEW_SIDEBAR_TABS: readonly FigureSidebarTabOption[] = [
  { id: 'data', label: 'Data' },
  { id: 'theme', label: 'Theme' },
  { id: 'charts', label: 'Charts' },
  { id: 'download', label: 'Download' }
];

/** The table views' tabs, in order: the chart views' with Table in place of Charts. */
export const TABLE_VIEW_SIDEBAR_TABS: readonly FigureSidebarTabOption[] = [
  { id: 'data', label: 'Data' },
  { id: 'theme', label: 'Theme' },
  { id: 'table', label: 'Table' },
  { id: 'download', label: 'Download' }
];

/** The Paired tests view's one tab: the theme, chart, table and download settings do not apply to it. */
export const PAIRED_VIEW_SIDEBAR_TABS: readonly FigureSidebarTabOption[] = [
  { id: 'data', label: 'Data' }
];

/**
 * The tab to show with a view: the chosen one, except that Charts and Table swap for each other
 * across the two view groups. Data, Theme and Download are in both sets and never change. The Paired
 * tests view shows Data.
 */
export function sidebarTabForView(tab: FigureSidebarTab, view: FigureViewTab): FigureSidebarTab {
  if (isPairedView(view)) {
    return 'data';
  }
  if (isTableView(view)) {
    return tab === 'charts' ? 'table' : tab;
  }
  return tab === 'table' ? 'charts' : tab;
}

/**
 * Where the sidebar's collapsed state, width and tab, the view and the Download tab's section open
 * states are kept, per browser, as `{ version: 1, collapsed, sidebarWidth, tab, view, figureSizeOpen,
 * tableImageSizeOpen, imageFormatOpen }`. Read and written in `try/catch`; never required.
 */
export const FIGURE_SIDEBAR_STORAGE_KEY = 'overseer.modelComparison.figureSidebar';

/** The settings sidebar's width, in CSS px: 26 rem by default, adjustable from 18 rem to 40 rem. */
export const SIDEBAR_WIDTH_DEFAULT = 416;
export const SIDEBAR_WIDTH_MIN = 288;
export const SIDEBAR_WIDTH_MAX = 640;

const FIGURE_SIDEBAR_TABS: readonly FigureSidebarTab[] = ['data', 'theme', 'charts', 'table', 'download'];

const FIGURE_VIEW_TABS: readonly FigureViewTab[] = ['all', 'single', 'table', 'tablePreview', 'paired'];

/** Earlier stored tab names: Emphasis became Data, Style became Charts, Export became Download. */
const MIGRATED_SIDEBAR_TABS: Readonly<Record<string, FigureSidebarTab>> = {
  emphasis: 'data',
  style: 'charts',
  export: 'download'
};

/** The stored workspace layout, field by field. */
interface StoredFigureSidebar {
  readonly collapsed: boolean;
  readonly sidebarWidth: number;
  readonly tab: FigureSidebarTab;
  readonly view: FigureViewTab;
  readonly figureSizeOpen: boolean;
  readonly tableImageSizeOpen: boolean;
  readonly imageFormatOpen: boolean;
}

/** The stored sidebar state, field by field; the default wherever storage is absent or unreadable. */
function readStoredFigureSidebar(): StoredFigureSidebar {
  const fallback: StoredFigureSidebar = {
    collapsed: false, sidebarWidth: SIDEBAR_WIDTH_DEFAULT, tab: 'data', view: 'all',
    figureSizeOpen: true, tableImageSizeOpen: true, imageFormatOpen: true
  };
  try {
    const raw = localStorage.getItem(FIGURE_SIDEBAR_STORAGE_KEY);
    const stored: unknown = raw === null ? null : JSON.parse(raw);
    if (stored === null || typeof stored !== 'object') {
      return fallback;
    }
    const {
      collapsed, sidebarWidth, tab: storedTab, view: storedView, figureSizeOpen, tableImageSizeOpen, imageFormatOpen
    } = stored as {
      collapsed?: unknown; sidebarWidth?: unknown; tab?: unknown; view?: unknown;
      figureSizeOpen?: unknown; tableImageSizeOpen?: unknown; imageFormatOpen?: unknown;
    };
    const tab = typeof storedTab === 'string' ? MIGRATED_SIDEBAR_TABS[storedTab] ?? storedTab : storedTab;
    // 'charts' and 'preview' are the All and Single views' earlier stored names.
    const view = storedView === 'charts' ? 'all' : storedView === 'preview' ? 'single' : storedView;
    const flag = (value: unknown, otherwise: boolean): boolean => typeof value === 'boolean' ? value : otherwise;
    return {
      collapsed: flag(collapsed, fallback.collapsed),
      sidebarWidth: typeof sidebarWidth === 'number' && Number.isFinite(sidebarWidth)
        ? Math.min(SIDEBAR_WIDTH_MAX, Math.max(SIDEBAR_WIDTH_MIN, Math.round(sidebarWidth)))
        : fallback.sidebarWidth,
      tab: FIGURE_SIDEBAR_TABS.includes(tab as FigureSidebarTab) ? tab as FigureSidebarTab : fallback.tab,
      view: FIGURE_VIEW_TABS.includes(view as FigureViewTab) ? view as FigureViewTab : fallback.view,
      figureSizeOpen: flag(figureSizeOpen, fallback.figureSizeOpen),
      tableImageSizeOpen: flag(tableImageSizeOpen, fallback.tableImageSizeOpen),
      imageFormatOpen: flag(imageFormatOpen, fallback.imageFormatOpen)
    };
  } catch {
    return fallback;
  }
}

/** The stored column configuration, migrated and repaired; today's nine columns wherever storage is absent or unreadable. */
function readStoredTableColumns(): TableColumnConfig {
  try {
    const raw = localStorage.getItem(TABLE_COLUMNS_STORAGE_KEY);
    return raw === null ? DEFAULT_TABLE_COLUMNS : migrateTableColumnConfig(JSON.parse(raw));
  } catch {
    return DEFAULT_TABLE_COLUMNS;
  }
}

/** The Download tab's formats, as stored. */
interface StoredDownloadSettings {
  readonly tableFormat: TableFileFormat;
  readonly imageFormat: FigureExportFormat;
  readonly webpQuality: WebpQuality;
}

const TABLE_FILE_FORMATS: readonly TableFileFormat[] = ['xlsx', 'csv', 'tsv', 'md', 'json', 'html', 'image'];

/** The stored formats, field by field; Excel, PNG and 85 wherever a field is absent or unreadable. */
function readStoredDownloadSettings(): StoredDownloadSettings {
  const fallback: StoredDownloadSettings = { tableFormat: 'xlsx', imageFormat: 'png', webpQuality: DEFAULT_WEBP_QUALITY };
  try {
    const raw = localStorage.getItem(DOWNLOAD_SETTINGS_STORAGE_KEY);
    const stored: unknown = raw === null ? null : JSON.parse(raw);
    if (stored === null || typeof stored !== 'object' || Array.isArray(stored)) {
      return fallback;
    }
    const { tableFormat, imageFormat, webpQuality } =
      stored as { tableFormat?: unknown; imageFormat?: unknown; webpQuality?: unknown };
    return {
      tableFormat: TABLE_FILE_FORMATS.includes(tableFormat as TableFileFormat)
        ? tableFormat as TableFileFormat
        : fallback.tableFormat,
      imageFormat: imageFormat === 'png' || imageFormat === 'webp' ? imageFormat : fallback.imageFormat,
      webpQuality: WEBP_QUALITY_OPTIONS.includes(webpQuality as WebpQuality)
        ? webpQuality as WebpQuality
        : fallback.webpQuality
    };
  } catch {
    return fallback;
  }
}

/**
 * The chart families the Charts tab styles; the Theme tab's appearance and Chat Consistency's
 * timeline are not among them.
 */
type ChartStyleFamily = Exclude<FigureStylePanelKind, 'appearance' | 'timeline'>;

/** The filter each filterable display column carries, by display key. */
const TABLE_COLUMN_FILTERS: Readonly<Record<string, string>> = { model: 'label', stateCol: 'state' };

/** The TableState sort column of the model order, which no header carries. */
const MODEL_ORDER_SORT = 'modelOrder';

/** Every entry's cells, by part key, as `comparisonTableCells` builds them. */
type EntryCells = Readonly<Record<string, ComparisonTableCell>>;

/** A cell's raw value as a sort key: a flag sorts as 1 or 0, and an absent value last. */
function sortKeyOf(raw: string | number | boolean | null): string | number | null {
  return typeof raw === 'boolean' ? (raw ? 1 : 0) : raw;
}

/** The All tab's scroller padding, in CSS px; the global `.gh-fig-viewport` in `styles.scss` matches it. */
const ALL_VIEWPORT_PADDING = 16;

/** The All tab's gap between tiles, in CSS px; `.mc-all-grid` matches it. */
const ALL_TILE_GAP = 16;

/** How long the All tab waits for changes to stop before it re-composes its tiles. */
const ALL_COMPOSE_QUIET_MS = 120;

/** One figure's chrome, as the export composer and the layout resolver both take it. */
type FigureExportChrome = Omit<FigureExportRequest, 'canvas' | 'format' | 'layout'>;

/** A degenerate shape the entry set can take, each of which is rendered differently. */
export type ComparisonShape = 'empty' | 'none' | 'single' | 'pair' | 'full';

/** One row of the Data tab's Models table. */
export interface ModelRow {
  readonly key: string;
  /** The name the model is drawn under. */
  readonly name: string;
  readonly thinkingLevel: string | null;
  readonly reasoningMode: string | null;
  /** Excluded by the server, so it has no numbers to draw and its Show box is disabled. */
  readonly excluded: boolean;
  /** Ticked under Show; never true for an excluded entry. */
  readonly shown: boolean;
  /** Drawn in the figures: shown and within the plot cap. */
  readonly plotted: boolean;
  /** Shown but past the plot cap, so the figures leave it out. */
  readonly overCap: boolean;
  /** The server's explanation, for an excluded row's info tip. */
  readonly explanation: string;
}

/** One caveat in the About dialog, counted on its button's badge. */
export interface AboutNote {
  readonly id: string;
  readonly tone: 'warning' | 'info';
  readonly heading: string;
  readonly body: string;
}

/** The entry keys of a payload, or null for no payload. */
function keySet(comparison: BenchmarkModelComparisonDto | null | undefined): Set<string> | null {
  return comparison ? new Set(comparison.entries.map(entry => entry.key)) : null;
}

/** Whether a payload has exactly the given entry keys, in any order. */
function sameKeys(keys: ReadonlySet<string>, comparison: BenchmarkModelComparisonDto | null): boolean {
  const entries = comparison?.entries ?? [];
  return entries.length === keys.size && entries.every(entry => keys.has(entry.key));
}

/** The keys of a payload's entries that the server measured, which is every entry it did not exclude. */
function measuredKeys(comparison: BenchmarkModelComparisonDto | null): string[] {
  return (comparison?.entries ?? []).filter(entry => !entry.excluded).map(entry => entry.key);
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
  /**
   * A small-multiples figure's tiles, rendered each on its own and stitched `columns` wide into the
   * plot. Present on the profile only; `type`, `data` and `options` are then the first tile's.
   */
  readonly tiles?: { readonly configs: readonly OffscreenPlotConfig[]; readonly columns: number };
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
 * 1. **The table is a view of the charts step, always available, and the one that opens over a
 *    set no chart can draw.** A chart is far more persuasive than a table, and a reader will trust
 *    six figures without checking twenty-three comparability keys. The canvases are `role="img"`
 *    summaries; the table is the artefact that carries the numbers, the states and the differing
 *    keys.
 * 2. **An excluded entry stays visible and stays explained.** The service refuses to return measures
 *    for one, so it can never reach a figure; dropping it from the view as well would make an
 *    unchartable model invisible, which is exactly how a reader concludes a set is comparable when
 *    it is not. Excluded entries are tabulated with the keys they differ on named.
 * 3. **A degraded axis says which axis and why.** Speed and cost degrade independently of quality,
 *    so a set can be trustworthy on one axis and not on another, and the notices are per figure.
 *
 * Every control that narrows the **figures** sits in one place, step 2's Data tab, and scopes every
 * one of them. A filter inside a chart card would leave the six figures describing different slices
 * of the same set. Controls that decide which sources are in the request at all are a different
 * stage of the same task and belong to the source picker, next to the tables they scope — which is
 * why suite scope lives there and pricing basis, which changes only the cost arithmetic over an
 * unchanged set, lives here.
 */
@Component({
  selector: 'app-benchmark-model-comparison',
  standalone: true,
  imports: [
    CommonModule, FormsModule, SortHeaderComponent, TablePagerComponent, ProviderBadgeComponent, ToastComponent,
    FigureStylePanelComponent, ExportSizeSectionComponent, TableSettingsPanelComponent, ReorderableListComponent,
    InfoTipComponent, PaneResizerComponent, ReportPackPanelComponent, DownloadCenterPanelComponent,
    PairedTestsViewComponent, ComparisonRenameDialogComponent
  ],
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

  /** How many battery results the host currently has selected; never beside runs or groups. */
  @Input() selectedBatteryCount = 0;

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

  /** The selection band's label: a programmatic focus target once Clear selection or the last chip goes. */
  @ViewChild('selectionLabel') private selectionLabel?: ElementRef<HTMLElement>;

  @ViewChild('clearSelectionButton') private clearSelectionButton?: ElementRef<HTMLButtonElement>;

  /** The chips' remove buttons, in chip order. */
  @ViewChildren('chipRemoveButton') private chipRemoveButtons?: QueryList<ElementRef<HTMLButtonElement>>;

  /** The selection counts together, which is what both caps and Next are judged on. */
  get selectedSourceCount(): number {
    return this.selectedRunCount + this.selectedGroupCount + this.selectedBatteryCount;
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

  @ViewChild('aboutDialog') private aboutDialogRef?: ElementRef<HTMLDialogElement>;

  /** The About dialog is open; its body renders only while it is. */
  aboutOpen = false;

  /** Step 3's *How the graders work* link; the host opens the guide at *Choosing grader models*. */
  @Output() graderGuideRequested = new EventEmitter<void>();

  /**
   * The computed comparison with the numbered comparison it is: once `identify` answers, after a
   * rename, and when a recompute of the same entries brings new figures while the number is held.
   */
  @Output() readonly comparisonIdentified = new EventEmitter<ComparisonIdentifiedEvent>();

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

  /**
   * P1's model order, shared by all three panels, by every other figure's series order and by the
   * table's rows until a column header is clicked.
   *
   * One control, not three: panels that sorted independently would stop a row meaning one model,
   * which is the whole reason the three are drawn as small multiples rather than separately.
   */
  sort: ModelSort = DEFAULT_MODEL_SORT;

  /**
   * The Custom order's entry keys, kept for the wizard's lifetime and never stored in the browser.
   * Null until Custom is first chosen, which seeds it from the order in effect; kept while another
   * key is chosen, so returning to Custom restores it.
   */
  customOrder: string[] | null = null;

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
   * Candidate cost for the whole suite by default: what the model would cost as the chat assistant.
   * The run total including grading roles is offered when every charted entry carries one, and is
   * shown disabled with its reason otherwise.
   */
  costMeasure: CostMeasure = 'candidateSuite';

  /**
   * Names every scatter mark on the canvas instead of in the legend below it.
   *
   * On by default: color means provider, so the name beside a mark is what identifies the model.
   * The plugin behind it places the names without collisions.
   */
  scatterDirectLabels = true;

  /**
   * Draws each scatter mark's two measured values beside it. Off by default: the values are in the
   * tooltip and the table, and a label is the model's name.
   */
  scatterInlineValues = false;

  /** Bar and trade-off styling, the theme and the table image's layout, applied to the page and to every export alike. */
  figureStyle: FigureStyle = DEFAULT_FIGURE_STYLE;

  /**
   * The theme resolved from `figureStyle.appearance`, once per rebuild or appearance change, and
   * read by every chart, every figure's chrome and the table image.
   */
  figureTheme: ResolvedFigureTheme = resolveFigureTheme(DEFAULT_FIGURE_STYLE.appearance);

  /** The appearance `figureTheme` was resolved from. */
  private themedAppearance: FigureAppearanceStyle = DEFAULT_FIGURE_STYLE.appearance;

  /** Whether the chosen font family loaded, for the Theme tab's status line. Empty for Overseer default. */
  fontLoadStatus = '';

  /** The decoded GnollBench logo per variant, once loaded. */
  private figureLogos: Partial<Record<FigureLogoVariant, CanvasImageSource>> = {};

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

  /** The profile's shared scales and one description per model, the profile tile's `aria-describedby`. */
  profileAxes: ProfileNormalization | null = null;

  /** What each family's Number format options preview on; replaced only by a rebuild. */
  numberSamples: Readonly<Record<ChartStyleFamily, NumberSamples>> = { bar: {}, scatter: {}, profile: {} };

  /** The hard ceiling the chart core enforces; the picker names it so the cap is never a surprise. */
  readonly maxPlottedEntries = MAX_PLOTTED_ENTRIES;

  private chartEntries: readonly ModelComparisonEntry[] = [];
  private readonly reducedMotion = new ReducedMotionWatcher();
  private unsubscribeReducedMotion: (() => void) | null = null;
  private resizeObserver: ResizeObserver | null = null;

  /** The element `resizeObserver` watches; a new one, after the chart views are rendered again, is re-observed. */
  private observedChartsHost: HTMLElement | null = null;

  // --- The model order, as the table and the Custom list read it ---

  /**
   * Every entry's position in the model order, excluded and deselected ones included. Rebuilt only
   * when the order, the measures or the entries change; the table's `modelOrder` sort reads it.
   */
  private modelOrderRank = new Map<string, number>();

  /** Every entry key in the model order: the Custom list's rows. */
  private modelOrderKeyList: readonly string[] = [];

  /** What `modelOrderRank` was computed from, so a rebuild that changes none of it keeps it. */
  private modelOrderInputs: {
    readonly sort: ModelSort;
    readonly speedMeasure: SpeedMeasure;
    readonly costMeasure: CostMeasure;
    readonly entries: readonly ModelComparisonEntry[];
  } | null = null;

  /** The Custom list's rows, rebuilt with the figures while Custom is chosen. */
  customOrderItems: readonly ReorderableListItem[] = [];

  /** Where the Custom list draws the plot cap's line, or null while every chartable entry is charted. */
  customOrderDividerIndex: number | null = null;

  /** Whether the Custom list already equals Intelligence Index, descending, where its reset has nothing to do. */
  customOrderIsDefault = true;

  // --- The table's cells and columns ---

  /** Every entry's cells, by entry key, built once per comparison and read by the template, the sorts and every export. */
  private tableCellsByKey = new Map<string, EntryCells>();

  /** Every entry, by key, for the Custom list's rows. */
  private entriesByKey = new Map<string, BenchmarkModelComparisonEntryDto>();

  /** The entry array the two maps above were built from. */
  private cellsSource: readonly BenchmarkModelComparisonEntryDto[] | null = null;

  /**
   * Which display columns are shown, and the order of all of them: one configuration for the
   * Interactive table, the Table preview and every download and copy. Remembered per browser.
   */
  tableColumns: TableColumnConfig = readStoredTableColumns();

  /** The shown display columns, in order; rebuilt only when the configuration changes. */
  shownColumns: readonly TableDisplayColumn[] = shownTableColumns(this.tableColumns);

  /** The shown display keys, which the combined renderers read to leave out a part shown on its own. */
  shownColumnKeys: ReadonlySet<string> = new Set(this.shownColumns.map(column => column.key));

  /** The table's generated caption: its shown columns, named. */
  tableCaption = this.captionFor(this.shownColumns);

  /** Display columns with no value on any row passing the filters, for the Table tab's `empty` tags. */
  columnEmptyKeys: ReadonlySet<string> = new Set<string>();

  /** The filters `columnEmptyKeys` was computed under, so a sort or a page change keeps it. */
  private columnEmptySignature: string | null = null;

  /** Why a filter was just cleared: its column was hidden. Cleared by the next column change. */
  tableColumnsStatus = '';

  /**
   * Sort, filter and page state for the Interactive table.
   *
   * The rows open in the model order — the order the charts use — through the `modelOrder` sort,
   * which no header carries; clicking a header sorts by that column instead, and *Use model order*
   * returns to it. State is one header click away, and sorts on a rank rather than the label so
   * the entries a reader has to check — excluded, then degraded — come first instead of
   * alphabetically.
   *
   * Every sortable display column sorts by the raw value of its primary part, read from
   * `tableCellsByKey`. The combined Timings column sorts by the mean model time per question: it is
   * the figure the scoring profile targets, and the suite total is that mean times a constant item
   * count; the other timings sort as columns of their own once shown.
   */
  readonly entryTable = new TableState<BenchmarkModelComparisonEntryDto>(MODEL_ORDER_SORT, 'asc').registerAccessors(
    {
      [MODEL_ORDER_SORT]: e => this.modelOrderRank.get(e.key) ?? null,
      ...this.displaySortAccessors()
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
    // A stored bundled font starts loading now, so the first composition rarely waits for it.
    if (this.figureStyle.appearance.fontFamily !== 'default') {
      void this.loadFigureFont();
    }
    this.unsubscribeReducedMotion = this.reducedMotion.subscribe(() => this.rebuild());
    this.alertSub = this.alertService.alerts$.subscribe(alerts => {
      const missing = alerts.some(alert => alert.id === REPORT_CHARTS_ALERT_ID);
      if (missing !== this.systemChartStorageMissing) {
        this.systemChartStorageMissing = missing;
        this.refreshDocumentChartActions();
        this.cdr.markForCheck();
      }
    });
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

    // A refetch of the same entries (pricing basis, Recompute) keeps what the admin chose; a new
    // set of entries is a new comparison and starts with every measured entry shown.
    const previousKeys = keySet(change.previousValue as BenchmarkModelComparisonDto | null | undefined);
    const sameEntries = previousKeys !== null && sameKeys(previousKeys, this.comparison);
    if (sameEntries) {
      const measured = new Set(measuredKeys(this.comparison));
      this.includedKeys = this.includedKeys.filter(key => measured.has(key));
      this.emphasisKeys = this.emphasisKeys.filter(key => this.includedKeys.includes(key));
    } else {
      this.includedKeys = measuredKeys(this.comparison);
      this.emphasisKeys = [];
      this.entryTable.page = 1;
    }
    this.highlightedKey = null;
    this.reconcileCustomOrder();
    this.rebuild();
    this.refreshColumnEmpty(true);
    this.scheduleTableMeasure();
    if (!this.identifyComparison()) {
      // The number held is this entry set's, so the new figures go out with it.
      this.emitComparisonIdentified();
    }

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
   * The views' box lives inside step 2, so it does not exist on the pass that runs
   * `ngAfterViewInit`, and it is a new element each time step 2 is rendered again. The observer is
   * attached whenever the element it watches is not the one on the page.
   *
   * The stage and the All tiles can leave the DOM or come back without a tab click — a refetch
   * takes the charts away or returns them, and leaving step 2 removes them. Leaving tears down only
   * unbound state, since a bound field changed inside this hook faults the check; returning
   * re-attaches outside the check pass.
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
    } else if (this.stageViewShown && this.previewStage && !this.previewActive && !this.previewAttachQueued) {
      this.previewAttachQueued = true;
      queueMicrotask(() => {
        this.previewAttachQueued = false;
        if (this.stageViewShown && this.previewStage && !this.previewActive) {
          this.attachPreview();
          this.cdr.markForCheck();
        }
      });
    }
    if (this.allActive && !this.allViewport) {
      this.teardownAll();
    } else if (this.effectiveFigureTab === 'all' && this.allViewport && !this.allActive && !this.allAttachQueued) {
      this.allAttachQueued = true;
      queueMicrotask(() => {
        this.allAttachQueued = false;
        if (this.effectiveFigureTab === 'all' && this.allViewport && !this.allActive) {
          this.attachAll();
          this.cdr.markForCheck();
        }
      });
    }
  }

  /** A re-attach is waiting for its microtask, so later checks in the same turn do not queue another. */
  private previewAttachQueued = false;

  ngOnDestroy(): void {
    this.alertSub?.unsubscribe();
    this.identitySub?.unsubscribe();
    this.chartPublisher.cancel();
    this.clearSlowLoadingTimer();
    this.unsubscribeReducedMotion?.();
    this.reducedMotion.dispose();
    this.resizeObserver?.disconnect();
    this.resizeObserver = null;
    this.observedChartsHost = null;
    this.disconnectStageObserver();
    this.cancelScheduledPreview();
    this.cancelScheduledStyle();
    this.cancelScheduledTableMeasure();
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

  /**
   * Steps 3 and 4 have been opened. Their panels are created on the first visit and kept, hidden
   * while another step is shown, so a form, a running job's card, a table's page and its selection
   * survive every step change.
   */
  visited3 = false;
  visited4 = false;

  readonly stepTitles = Object.fromEntries(
    COMPARISON_WIZARD_STEPS.map(entry => [entry.step, entry.title])
  ) as Record<ComparisonWizardStep, string>;

  /**
   * Step 2 needs a computed comparison and nothing else.
   *
   * It opens over a set no chart can draw, on the table views, which is the point of them: an
   * incomparable set still has measures, states and differing keys to read, and the step that
   * carries them must not close behind the same gate as the pictures. Only its chart views refuse.
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
    return step !== 3 || this.hasComparisonPeers;
  }

  /**
   * Step 3 needs a subject and at least one peer: two entries the server measured, because a
   * comparison report compares one model with at least one other.
   */
  get hasComparisonPeers(): boolean {
    return this.comparableEntryCount >= 2;
  }

  private get comparableEntryCount(): number {
    return (this.comparison?.entries ?? []).filter(entry => !entry.excluded).length;
  }

  /** Why a step cannot be opened, for its tab's description; empty where it can. */
  stepBlockedReason(step: ComparisonWizardStep): string {
    if (this.isStepReachable(step)) {
      return '';
    }
    if (this.comparison === null) {
      return 'Compare the selected sources first.';
    }
    if (this.comparableEntryCount === 1) {
      return 'A comparison report compares one model with at least one other. Add another model on step 1, '
        + 'or write a run\'s or battery run\'s own reports in the AI Reports tab of its report.';
    }
    return 'Every model in this comparison was measured differently, so none of them can be the subject of a report.';
  }

  get canGoPrevious(): boolean {
    return this.step > 1;
  }

  get canGoNext(): boolean {
    if (this.step === 1) {
      return !this.loading && this.selectedSourceCount > 0 && this.selectedSourceCount <= this.maxSources;
    }
    return true;
  }

  /**
   * A comparison is being computed for the selection on step 1.
   *
   * Step-scoped rather than the bare `loading` flag: a refetch from step 2's Prices select or its
   * Recompute button loads too, and the footer button there is Next, which the request does not
   * block.
   */
  get comparing(): boolean {
    return this.loading && this.step === 1;
  }

  /** Compare while the current selection has no computed comparison; Next once it does; Close on step 4. */
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
        return 'Select at least one run, analysis group or battery result.';
      }
      return `${this.selectedSourceCount} sources selected — at most ${this.maxSources} may be ` +
        'compared in one request. A comparison over every stored run is a slow query and an ' +
        'unreadable figure.';
    }
    return '';
  }

  /**
   * The busy line of a refetch from step 2, where Next is not blocked and carries no spinner.
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
   * array — it does not mention the request cap; `nextBlockedReason` already does. Read after
   * *Your selection —*, so it starts in lower case where it starts with a word.
   */
  get selectionSummary(): string {
    const runs = this.selectedRunCount;
    const groups = this.selectedGroupCount;
    const batteries = this.selectedBatteryCount;
    if (runs === 0 && groups === 0 && batteries === 0) {
      return 'nothing selected yet';
    }
    const parts = [
      runs > 0 ? `${runs} ${runs === 1 ? 'run' : 'runs'}` : '',
      groups > 0 ? `${groups} ${groups === 1 ? 'group' : 'groups'}` : '',
      batteries > 0 ? `${batteries} ${batteries === 1 ? 'battery result' : 'battery results'}` : ''
    ].filter(part => part !== '');
    return `${parts.join(' and ')} selected`;
  }

  /** One chip's tooltip anchor id, for its remove button's `interestfor` / `position-anchor` pair. */
  sourceChipId(source: ComparisonSelectedSource): string {
    return `mc-sel-${source.kind}-${source.id}`;
  }

  /**
   * Clear selection. The button goes away with the selection, so focus moves to the band's label,
   * which stays on step 1 and announces the empty selection.
   */
  onClearSelection(): void {
    this.clearSelection.emit();
    this.selectionLabel?.nativeElement.focus();
  }

  /**
   * One chip's remove button. Focus moves to the next chip's remove button, else to Clear
   * selection while other chips remain, else to the band's label. The target is chosen before the
   * emit, since a host that re-renders synchronously has removed the clicked chip by the time it
   * returns; chips are tracked by source, so the other chips' buttons survive the re-render.
   */
  onRemoveSource(source: ComparisonSelectedSource, index: number): void {
    const removeButtons = this.chipRemoveButtons?.toArray() ?? [];
    const target = index + 1 < removeButtons.length
      ? removeButtons[index + 1].nativeElement
      : this.selectedSources.length > 1 ? this.clearSelectionButton?.nativeElement : undefined;
    this.removeSource.emit(source);
    const label = this.selectionLabel?.nativeElement;
    (target?.isConnected ? target : label)?.focus();
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
      body: 'Select at least one completed run, analysis group or battery result in the tables above. '
        + 'Compare stays unavailable until you do.'
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
    if (this.step === 2 && step !== 2) {
      // While the views still exist. `figureTab` is kept, so returning re-attaches the one shown.
      this.detachPreview();
      this.detachAll();
    }
    this.step = step;
    if (step === 3) {
      this.visited3 = true;
    } else if (step === 4) {
      this.visited4 = true;
    }
    this.scheduleTableMeasure();
    // Marked, like every other mutator here: several callers are outside a template event —
    // ngOnChanges, the keyboard handler, the host reopening the dialog.
    this.cdr.markForCheck();
  }

  /** The nearest earlier step that can be opened. */
  previousStep(): void {
    if (!this.canGoPrevious) {
      return;
    }
    const previous = [...this.steps].reverse().find(step => step < this.step && this.isStepReachable(step));
    if (previous !== undefined) {
      this.goToStep(previous);
    }
  }

  /** Compare on step 1 without a comparison, then the next step that can be opened, and Close on step 4. */
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
    const next = this.steps.find(step => step > this.step && this.isStepReachable(step));
    if (next !== undefined) {
      this.goToStep(next);
    }
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
   * refetch under an unchanged selection — step 2's Prices select or its Recompute button —
   * replaces one non-null payload with another and leaves the step alone, so a reader who went
   * back to step 1 while it computed is not yanked forward. Losing the payload drops back to step 1, where the
   * sources are: step 2 has nothing to render without one. A payload with no model a report can be
   * about moves a reader on step 3 to step 2.
   */
  private applyComparisonToStep(previous: BenchmarkModelComparisonDto | null | undefined): void {
    let next: ComparisonWizardStep = this.comparison === null ? 1 : !previous ? 2 : this.step;
    if (!this.isStepReachable(next)) {
      next = 2;
    }
    if (this.step === 2 && next !== 2) {
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

  /** Recompute, in the step tab row on step 2. Refused while a request is already in flight. */
  onRefresh(): void {
    if (this.loading) {
      return;
    }
    this.refresh.emit();
  }

  // ---------------------------------------------------------------------------------------------
  // Client-side filter controls — these re-render every figure against the same slice
  //
  // All of them are in step 2's Data tab, beside the charts they change: the Models table, the
  // measures and the model order, which also orders the table.
  // ---------------------------------------------------------------------------------------------

  /**
   * Adds or removes one entry from the plotted set. Taking an entry out also drops its highlight,
   * which would draw nothing and return unasked on the next tick.
   *
   * Ticking more entries than the figures plot is allowed: the chart core takes the first
   * {@link MAX_PLOTTED_ENTRIES} and names the rest in an overflow notice, which is also the state
   * a fresh payload seeds, so a refusal here would leave that state unreachable once left.
   */
  toggleEntry(key: string): void {
    if (this.includedKeys.includes(key)) {
      this.includedKeys = this.includedKeys.filter(k => k !== key);
      this.emphasisKeys = this.emphasisKeys.filter(k => k !== key);
    } else {
      this.includedKeys = [...this.includedKeys, key];
    }
    this.rebuild();
  }

  isIncluded(key: string): boolean {
    return this.includedKeys.includes(key);
  }

  /**
   * Custom is seeded, the first time it is chosen, from the order in effect at that moment; another
   * key keeps the custom order for a later return. Every model-order change also returns the table
   * to the model order, because the admin has just said how the models should be ordered.
   */
  onSortKeyChange(value: ModelSortKey): void {
    if (value === 'custom') {
      if (this.customOrder === null) {
        this.customOrder = modelOrderKeys(this.chartEntries, this.sort, this.speedMeasure, this.costMeasure, this.context);
      }
      this.sort = { key: 'custom', direction: this.sort.direction, customOrder: this.customOrder };
    } else {
      this.sort = { key: value, direction: this.sort.direction };
    }
    this.resetTableToModelOrder();
    this.rebuild();
  }

  onSortDirectionChange(value: SortDirection): void {
    this.sort = { ...this.sort, direction: value };
    this.resetTableToModelOrder();
    this.rebuild();
  }

  /** One committed move of the Custom list — a drop or a Move button — and one rebuild for it. */
  onCustomOrderChange(keys: readonly string[]): void {
    this.customOrder = [...keys];
    this.sort = { key: 'custom', direction: this.sort.direction, customOrder: this.customOrder };
    this.resetTableToModelOrder();
    this.rebuild();
  }

  /** Re-seeds the Custom list from Intelligence Index, descending. Refused while it already is that. */
  resetCustomOrder(): void {
    if (this.customOrderIsDefault) {
      return;
    }
    this.onCustomOrderChange(this.defaultModelOrderKeys(this.chartEntries));
  }

  /** Every entry key under Intelligence Index, descending: what Custom resets to and appends new entries by. */
  private defaultModelOrderKeys(entries: readonly ModelComparisonEntry[]): string[] {
    return modelOrderKeys(entries, DEFAULT_MODEL_SORT, this.speedMeasure, this.costMeasure, toChartContext(this.comparison));
  }

  /**
   * On a new payload, the Custom order drops the keys no longer present and appends the new ones in
   * Intelligence Index, descending order.
   */
  private reconcileCustomOrder(): void {
    if (this.customOrder === null) {
      return;
    }
    const entries = toChartEntries(this.comparison);
    const present = new Set(entries.map(entry => entry.key));
    const kept = this.customOrder.filter(key => present.has(key));
    const keptSet = new Set(kept);
    this.customOrder = [...kept, ...this.defaultModelOrderKeys(entries).filter(key => !keptSet.has(key))];
    if (this.sort.key === 'custom') {
      this.sort = { ...this.sort, customOrder: this.customOrder };
    }
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
   * Both scatter toggles live in the sidebar's Charts tab and, through `rebuild`, re-compose
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
   * six figures and the table image once the change pauses: a range drag or a colour picker fires on
   * every step. A newly chosen font, and a newly shown or chosen logo, starts loading at once.
   */
  onFigureStyleChange(style: FigureStyle): void {
    const previous = this.figureStyle.appearance;
    this.figureStyle = normalizeFigureStyle(style);
    this.writeStoredFigureStyle(this.figureStyle);
    const appearance = this.figureStyle.appearance;
    if (appearance.fontFamily !== previous.fontFamily) {
      void this.loadFigureFont();
    }
    if (appearance.logo && (appearance.logo !== previous.logo || appearance.logoVariant !== previous.logoVariant)) {
      void this.loadFigureLogo();
    }
    this.cancelScheduledStyle();
    this.styleTimer = setTimeout(() => {
      this.styleTimer = null;
      this.rebuild();
      this.scheduleTableMeasure();
    }, this.previewDebounceMs);
    this.cdr.markForCheck();
  }

  /** The Table tab's Row style section, kept in the figure style. */
  onTableStyleChange(table: TableImageStyle): void {
    this.onFigureStyleChange({ ...this.figureStyle, table });
  }

  /**
   * Loads the chosen font family before anything is measured with it, and says in the Theme tab
   * whether it loaded. Every composition awaits this first: a canvas does not wait for web fonts,
   * and a layout measured in the fallback and drawn in the face would disagree with itself.
   */
  private async loadFigureFont(): Promise<void> {
    const id = this.figureStyle.appearance.fontFamily;
    const loaded = await ensureFigureFont(id);
    const label = figureFont(id).label;
    const status = id === 'default'
      ? ''
      : loaded
        ? `${label} is loaded.`
        : `${label} could not be loaded, so the charts and the table use the fallback font.`;
    // A later choice may have replaced this one while it loaded.
    if (id === this.figureStyle.appearance.fontFamily && status !== this.fontLoadStatus) {
      this.fontLoadStatus = status;
      this.cdr.markForCheck();
    }
  }

  /** Loads the chosen logo variant while the logo is shown. A logo that fails to load draws nothing. */
  private async loadFigureLogo(): Promise<void> {
    const appearance = this.figureStyle.appearance;
    if (!appearance.logo) {
      return;
    }
    const variant = appearance.logoVariant;
    const image = await ensureFigureLogo(variant);
    if (image) {
      this.figureLogos[variant] = image;
    }
  }

  /** The font and the logo every composition and measurement draws with, loaded together. */
  private async prepareFigureComposition(): Promise<void> {
    await Promise.all([this.loadFigureFont(), this.loadFigureLogo()]);
  }

  /** The logo the composers draw, or null while it is hidden or not loaded. */
  private figureLogo(): FigureLogo | null {
    const appearance = this.figureStyle.appearance;
    const image = appearance.logo ? this.figureLogos[appearance.logoVariant] : undefined;
    return image
      ? { image, aspectRatio: figureLogoAspect(appearance.logoVariant), heightPx: appearance.logoHeightPx }
      : null;
  }

  /** Resolves the theme once per appearance, so every composition reads one cached object. */
  private refreshFigureTheme(): void {
    const appearance = this.figureStyle.appearance;
    if (appearance !== this.themedAppearance) {
      this.themedAppearance = appearance;
      this.figureTheme = resolveFigureTheme(appearance);
    }
  }

  /** A transparent background, which the page shows over the preview backdrop and never exports. */
  get isTransparentFigure(): boolean {
    return this.figureStyle.appearance.background === 'transparent';
  }

  /**
   * The backdrop colour as a custom property, set on the canvases' scroller so the canvases
   * inherit it: the Single stage's canvas has its size written into its own style attribute, which
   * a bound one would overwrite. Null keeps the stylesheet's checkerboard.
   */
  get figureBackdropStyle(): string | null {
    const appearance = this.figureStyle.appearance;
    return appearance.background === 'transparent' && appearance.previewBackdrop === 'color'
      ? `--gh-fig-backdrop: ${appearance.previewBackdropColor}`
      : null;
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
   * Hover on a Models row lights the same model in all three panels and in
   * the profile; on the All tab the tiles re-compose once the pointer settles.
   *
   * Refused on the Single tab, where a transient hover would be composed into the figure about to
   * be exported and recompose on every pass of the pointer. Clearing is always accepted.
   */
  setHighlight(key: string | null): void {
    if (this.highlightedKey === key || (key !== null && this.effectiveFigureTab === 'single')) {
      return;
    }
    this.highlightedKey = key;
    this.rebuild();
  }

  /** Pins a plotted model into the accent, or releases it. A model the figures do not draw cannot be pinned. */
  toggleEmphasis(key: string): void {
    if (this.emphasisKeys.includes(key)) {
      this.emphasisKeys = this.emphasisKeys.filter(k => k !== key);
    } else if (this.plotted.some(entry => entry.key === key)) {
      this.emphasisKeys = [...this.emphasisKeys, key];
    } else {
      return;
    }
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
   * Every entry as the Data tab's Models table names it, in the model order so the list matches the
   * charts and the table; any entry the order does not rank follows in payload order.
   *
   * A field rebuilt at the end of `rebuild()` rather than a getter, because the template reads it per
   * row on every change detection. The display name, the thinking level and the reasoning mode are
   * the same facts the comparison table's model cell shows.
   */
  modelRows: readonly ModelRow[] = [];

  /** Rows ticked under Show. */
  get shownModelCount(): number {
    return this.modelRows.filter(row => row.shown).length;
  }

  /** Rows that can be ticked under Show: every entry the server did not exclude. */
  get chartableModelCount(): number {
    return this.modelRows.filter(row => !row.excluded).length;
  }

  private buildModelRows(): ModelRow[] {
    const plotted = new Set(this.plotted.map(entry => entry.key));
    const overflow = new Set((this.figures?.selection.overflow ?? []).map(entry => entry.key));
    const byKey = new Map(this.entries.map(entry => [entry.key, entry] as const));
    const ranked = this.modelOrderKeyList.filter(key => byKey.has(key));
    const rankedSet = new Set(ranked);
    const keys = [...ranked, ...this.entries.map(entry => entry.key).filter(key => !rankedSet.has(key))];
    return keys.map(key => {
      const entry = byKey.get(key)!;
      const shown = !entry.excluded && this.includedKeys.includes(key);
      return {
        key,
        name: entry.modelDisplayName || entry.label,
        thinkingLevel: entry.thinkingLevel ?? null,
        reasoningMode: entry.reasoningMode ?? null,
        excluded: entry.excluded,
        shown,
        plotted: plotted.has(key),
        overCap: shown && overflow.has(key),
        explanation: entry.explanation ?? ''
      };
    });
  }

  readonly showReasoningBadge = showReasoningBadge;

  /** `Run 48` or `Analysis group 3` — the run line under a model name in the table. */
  sourceLabel(entry: { sourceKind: string; sourceId: number }): string {
    return sourceLabel(entry);
  }

  /**
   * After a sort, a filter or a page change. The Columns `empty` set follows the filters; the Table
   * preview and the *Fit the table* information follow the filters and the order. A page change
   * changes neither, so neither is redone for it.
   */
  onTableChanged(): void {
    this.refreshColumnEmpty();
    const signature = this.tableViewSignature();
    if (signature !== this.lastTableViewSignature) {
      this.lastTableViewSignature = signature;
      this.scheduleTableOutputs();
    }
    this.cdr.detectChanges();
  }

  /** The sort and the filters `onTableChanged` last saw. */
  private lastTableViewSignature = '';

  private tableViewSignature(): string {
    return JSON.stringify([this.entryTable.sortColumn, this.entryTable.sortDirection, this.entryTable.filters]);
  }

  /** Recomposes the Table preview, if shown, and re-measures the table image. */
  private scheduleTableOutputs(): void {
    if (this.figureTab === 'tablePreview') {
      this.schedulePreview();
    }
    this.scheduleTableMeasure();
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

  /** Entries a chart can draw: not excluded, and carrying a quality figure. */
  private get chartableEntries(): BenchmarkModelComparisonEntryDto[] {
    return this.entries.filter(entry => !entry.excluded && entry.quality != null);
  }

  /**
   * The run total including grading roles can be charted: at least two chartable entries, and every
   * one of them carries it. All or nothing, because an axis missing some bars would rank models on a
   * figure that is absent for some of them.
   */
  get totalRunCostAvailable(): boolean {
    const chartable = this.chartableEntries;
    return chartable.length >= 2
      && chartable.every(entry => Number.isFinite(entry.cost?.totalRunCostPerRunUsd ?? Number.NaN));
  }

  /** Why the run total cannot be charted, naming the first chartable entry that lacks it; null when it can. */
  get totalRunCostUnavailableReason(): string | null {
    const missing = this.chartableEntries.find(entry => entry.cost?.totalRunCostUnavailableReason);
    return missing ? `${missing.label}: ${missing.cost!.totalRunCostUnavailableReason}` : null;
  }

  /** Measured entries whose Speed Index sits at the ceiling. */
  get speedIndexSaturatedCount(): number {
    return this.entries.filter(entry => !entry.excluded && entry.table?.speedIndexSaturated).length;
  }

  /** Entries the operator has taken out of the figures, which stay in the table regardless. */
  get deselectedEntries(): BenchmarkModelComparisonEntryDto[] {
    return this.entries.filter(entry => !entry.excluded && !this.includedKeys.includes(entry.key));
  }

  /** Why the chart views are off, for the line that replaces them; read only while they are. */
  get chartsUnavailableReason(): string {
    if (this.entries.length === 0) {
      return 'There are no models in this comparison.';
    }
    if (this.chartableEntries.length < 2) {
      return 'Fewer than two models were measured the same way, so there is nothing to chart. '
        + 'The table lists every model and why.';
    }
    return 'Charts need at least two models. Check more under Data → Models.';
  }

  // ---------------------------------------------------------------------------------------------
  // The About dialog
  //
  // A nested <dialog>, a DOM descendant of the host's wizard dialog, whose (close) closes the
  // wizard: its own close and cancel events are stopped here so they never reach it.
  // ---------------------------------------------------------------------------------------------

  openAbout(): void {
    const dialog = this.aboutDialogRef?.nativeElement;
    if (!dialog || dialog.open) {
      return;
    }
    this.aboutOpen = true;
    this.cdr.detectChanges();
    dialog.showModal();
  }

  onAboutDialogClose(event: Event): void {
    event.stopPropagation();
    if (event.type !== 'close') {
      return;
    }
    this.aboutOpen = false;
    this.cdr.markForCheck();
    document.getElementById('mc-about-trigger')?.focus();
  }

  // ---------------------------------------------------------------------------------------------
  // Steps 3 and 4: the AI reports and the documents
  //
  // Step 3 hosts the Report Pack panel and step 4 the Download Center panel, each created on its
  // first visit and kept. The wizard owns the chart selection, charts every document step 3 writes
  // while the wizard is open, and lends step 4 the chart actions: both publish through one
  // publisher, one document at a time, composing each figure from step 2's live settings.
  // ---------------------------------------------------------------------------------------------

  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly alertService = inject(AdminAlertService);
  private readonly hostElement = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly chartPublisher = new ReportChartPublisher(this.benchmarkService);
  private alertSub: Subscription | null = null;

  private readonly storedChartSettings = readStoredChartSettings();

  /** The figures each document type gets, remembered per browser. */
  chartSelection: ReportChartSelection = this.storedChartSettings.selection;

  /** How each document type's charts are drawn and placed, remembered per browser with the selection. */
  chartLayout: ReportChartLayoutSettings = this.storedChartSettings.layout;

  /** Step 3's chart progress per written document, by document id. */
  chartStatus: Readonly<Record<number, ReportChartRowStatus>> = {};

  /** Bumped to make step 4 list its documents again: a job finished, or a document was charted. */
  documentsReloadToken = 0;

  readonly documentsNote = COMPARISON_DOCUMENTS_NOTE;

  /** The chart actions step 4 is lent; replaced whenever one of their values changes. */
  documentChartActions: DownloadCenterChartActions | null = null;

  /** The hash of step 2's chart settings; null until it is first computed. */
  currentChartSettingsHash: string | null = null;

  private systemChartStorageMissing = false;
  private publishChartStorageMissing = false;
  /** The numbered comparison `documentChartActions` was lent for. */
  private chartActionsComparisonId: number | null = null;
  private chartSettingsSignature = '';
  private chartSettingsHashPending: Promise<string> | null = null;
  private chartsAvailableKeys: readonly string[] = [];
  private publishQueue: Promise<unknown> = Promise.resolve();
  private pendingPublishes = 0;
  private reportPackContextCache: { comparison: BenchmarkModelComparisonDto; basis: string; context: ReportPackContext } | null = null;
  private documentsContextCache: {
    keys: string;
    ids: string;
    comparisonId: number | null;
    name: string | null;
    context: DownloadCenterContext;
  } | null = null;
  /** The documents of the last step 3 job that finished, with the entry set they were written for. */
  private lastJobDocuments: { keys: string; ids: readonly number[] } | null = null;

  // --- The numbered comparison ---

  /**
   * The numbered comparison the computed one is (*Comparison #12*), as `identify` answered after
   * Compare or as a rename left it; null before the answer, and when identify failed, which leaves
   * the header without a number and blocks nothing.
   */
  comparisonIdentity: BenchmarkComparisonDto | null = null;

  /**
   * The comparison steps 3 and 4 are fed: its number, its display name and its entry keys. One object
   * per identity, so a child sees a new value only when there is one.
   */
  currentComparison: ComparisonIdentityState | null = null;

  @ViewChild(ComparisonRenameDialogComponent) private renameDialog?: ComparisonRenameDialogComponent;

  /** The entry set `identify` was last asked about, so a recompute of the same set asks nothing. */
  private identitySignature: string | null = null;
  private identitySub: Subscription | null = null;

  /** The ids of the runs, groups and battery runs the comparison's entries come from, each once. */
  private comparisonSourceIds(comparison: BenchmarkModelComparisonDto): { runIds: number[]; groupIds: number[]; batteryRunIds: number[] } {
    const idsOf = (kind: string): number[] => {
      const ids: number[] = [];
      for (const entry of comparison.entries) {
        if (entry.sourceKind.toLowerCase() === kind && !ids.includes(entry.sourceId)) {
          ids.push(entry.sourceId);
        }
      }
      return ids;
    };
    return { runIds: idsOf('run'), groupIds: idsOf('group'), batteryRunIds: idsOf('battery') };
  }

  /** The entry set of a comparison, independent of entry order. */
  private static entrySignature(comparison: BenchmarkModelComparisonDto): string {
    return comparison.entries.map(entry => entry.key).sort().join(',');
  }

  /**
   * Numbers the computed comparison: after Compare, and whenever the entry set changes; a recompute
   * of the same entries keeps the identity it has. A failure, or an answer for an older set, is
   * dropped. True when it asked, false when there was nothing to ask about.
   */
  private identifyComparison(): boolean {
    const comparison = this.comparison;
    if (!comparison || comparison.entries.length === 0) {
      return false;
    }
    const signature = ModelComparisonComponent.entrySignature(comparison);
    if (signature === this.identitySignature) {
      return false;
    }
    this.identitySignature = signature;
    this.identitySub?.unsubscribe();
    this.identitySub = null;
    this.setComparisonIdentity(null);
    try {
      this.identitySub = this.benchmarkService.identifyComparison(this.comparisonSourceIds(comparison)).subscribe({
        next: identity => {
          if (this.identitySignature === signature) {
            this.setComparisonIdentity(identity);
          }
        },
        error: () => {
          // The header omits the number; step 3 shows the server's refusal at Generate.
        }
      });
    } catch {
      // As a failed request: the wizard carries on without the number.
    }
    return true;
  }

  private setComparisonIdentity(identity: BenchmarkComparisonDto | null): void {
    this.comparisonIdentity = identity;
    this.currentComparison = identity
      ? { id: identity.id, name: identity.name, entryKeys: identity.entryKeys }
      : null;
    this.refreshDocumentChartActions();
    this.cdr.markForCheck();
    if (identity) {
      this.emitComparisonIdentified();
    }
  }

  /**
   * Emits `comparisonIdentified` while a comparison and the number of its own entry set are both
   * held. A failure in the host's handler never reaches the wizard.
   */
  private emitComparisonIdentified(): void {
    const comparison = this.comparison;
    const identity = this.comparisonIdentity;
    if (!comparison || !identity || comparison.entries.length === 0
      || ModelComparisonComponent.entrySignature(comparison) !== this.identitySignature) {
      return;
    }
    try {
      this.comparisonIdentified.emit({ comparison, identity });
    } catch {
      // The record is a convenience of the launcher; the wizard carries on without it.
    }
  }

  /** The rename dialog is rendered: from Rename comparison until it has closed. */
  renameOpen = false;

  /** The header's Rename comparison: renders the dialog, then opens it. */
  openRenameComparison(trigger: HTMLElement | null = null): void {
    const identity = this.comparisonIdentity;
    if (!identity) {
      return;
    }
    this.renameOpen = true;
    this.cdr.detectChanges();
    this.renameDialog?.open(identity, trigger);
  }

  /** The rename dialog closed and returned focus: it leaves the DOM. */
  onRenameClosed(): void {
    this.renameOpen = false;
    this.cdr.markForCheck();
  }

  /** The rename dialog saved: the header and steps 3 and 4 take the new name. */
  onComparisonRenamed(identity: BenchmarkComparisonDto): void {
    if (identity.id === this.comparisonIdentity?.id) {
      this.setComparisonIdentity(identity);
    }
  }

  /**
   * The computed comparison as the report-pack request carries it: the entries' own sources, so every
   * subject key is one the server's recomputation produces, and the basis the comparison was computed on.
   * One object per comparison and basis, so step 3's panel sees a new context only when there is one.
   */
  get reportPackContext(): ReportPackContext | null {
    const comparison = this.comparison;
    if (!comparison) {
      return null;
    }
    const basis = this.chartPricingBasis as ReportPackContext['pricingBasis'];
    const cached = this.reportPackContextCache;
    if (cached && cached.comparison === comparison && cached.basis === basis) {
      return cached.context;
    }
    const { runIds, groupIds, batteryRunIds } = this.comparisonSourceIds(comparison);
    const context: ReportPackContext = {
      runIds,
      groupIds,
      ...(batteryRunIds.length > 0 ? { batteryRunIds } : {}),
      pricingBasis: basis,
      entries: comparison.entries,
      entryKeys: comparison.entries.map(entry => entry.key),
      suiteId: comparison.baselineSuiteId ?? null,
      // A comparison of battery results spans several suites; its battery stands in for the suite.
      suiteName: comparison.baselineSuiteName ?? comparison.baselineBatteryName ?? null
    };
    this.reportPackContextCache = { comparison, basis, context };
    return context;
  }

  /**
   * Step 4's library: this comparison's documents, by its number once it has one (and by its entry
   * keys, for documents written before comparisons were numbered), headed with its number and name.
   * The documents of the last step 3 job that finished for this entry set start chosen; without one,
   * every row does. One object per entry set, number, name and chosen ids.
   */
  get documentsContext(): DownloadCenterContext | null {
    const comparison = this.comparison;
    if (!comparison) {
      return null;
    }
    const entryKeys = comparison.entries.map(entry => entry.key);
    const keys = entryKeys.join(',');
    const lastIds = this.lastJobDocuments?.keys === keys ? this.lastJobDocuments.ids : [];
    const ids = lastIds.join(',');
    const comparisonId = this.currentComparison?.id ?? null;
    const name = this.currentComparison?.name ?? null;
    const cached = this.documentsContextCache;
    if (cached?.keys === keys && cached.ids === ids && cached.comparisonId === comparisonId && cached.name === name) {
      return cached.context;
    }
    const context: DownloadCenterContext = {
      kind: 'library',
      scope: { kind: 'comparison', comparisonId, name, entryKeys },
      preselect: lastIds.length > 0 ? { ids: lastIds } : 'all',
      title: 'Documents of this comparison',
      subtitle: COMPARISON_DOCUMENTS_NOTE
    };
    this.documentsContextCache = { keys, ids, comparisonId, name, context };
    return context;
  }

  /** The pricing basis step 2 shows: the computed comparison's, else the requested one. */
  get chartPricingBasis(): string {
    const basis = this.comparison?.pricingBasis;
    return basis === 'AsRun' || basis === 'Current' ? basis : this.pricingBasis;
  }

  /** The figure keys the comparison can draw: two plotted models for each figure, three for the profile. */
  get chartsAvailable(): readonly string[] {
    return this.chartsAvailableKeys;
  }

  /** Chart storage is not configured: the system alert says so, or a publish stopped for it. */
  get chartStorageMissing(): boolean {
    return this.systemChartStorageMissing || this.publishChartStorageMissing;
  }

  /** Charts are being composed or uploaded, by step 3 or by step 4. */
  get chartsPublishing(): boolean {
    return this.pendingPublishes > 0 || this.chartPublisher.running;
  }

  /** The wizard's own close controls refuse while an export or a chart upload runs. */
  get closeBlocked(): boolean {
    return this.exporting || this.chartsPublishing;
  }

  /**
   * Why charts drawn with step 2's theme would print badly, or null; only while a document type's
   * charts take their theme *As in step 2*. Documents are printed on white paper: a dark theme prints
   * as a dark block, and light text on a transparent background all but disappears. Advice only; the
   * charts are drawn as chosen.
   */
  get chartAdvisory(): string | null {
    const followsStep2 = [
      BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.TechnicalReport, BenchmarkReportAudience.InternalBrief
    ].some(audience => documentChartLayoutFor(this.chartLayout, audience).theme === 'asInStep2');
    if (!followsStep2) {
      return null;
    }
    const appearance = this.figureStyle.appearance;
    if (appearance.theme === 'dark') {
      return 'The charts use the dark theme, which prints as a dark block on white paper. Choose the light '
        + 'theme under Theme on step 2, or Light, for print under the charts\' Layout, for documents that are printed.';
    }
    if (appearance.background === 'transparent'
        && contrastRatio(appearance.textColor ?? resolveFigureTheme(appearance).chrome.body, '#ffffff') < 3) {
      return 'The charts have a transparent background with light text, which can barely be read on white '
        + 'paper. Choose a background or a darker text color under Theme on step 2, or Light, for print '
        + 'under the charts\' Layout.';
    }
    return null;
  }

  onReportsGraderGuideRequested(): void {
    this.graderGuideRequested.emit();
  }

  onChartSelectionChange(selection: ReportChartSelection): void {
    this.chartSelection = selection;
    storeChartSettings({ selection, layout: this.chartLayout });
    this.cdr.markForCheck();
  }

  /** The charts' layout changed on step 3: remembered with the selection, and the settings hash follows. */
  onChartLayoutChange(layout: ReportChartLayoutSettings): void {
    this.chartLayout = layout;
    storeChartSettings({ selection: this.chartSelection, layout });
    this.refreshChartSettingsHash();
    this.refreshDocumentChartActions();
    this.cdr.markForCheck();
  }

  /**
   * A document of step 3's job was written: its charts are drawn and attached now, unless its type
   * has no chart chosen. While the wizard is closed nothing is drawn; step 4's Update charts adds them.
   */
  onReportDocumentWritten(progress: BenchmarkReportPackDocumentProgressDto): void {
    if (progress.documentId === null || progress.documentId === undefined) {
      return;
    }
    if ((this.chartSelection[progress.audience] ?? []).length === 0 || !this.wizardOpen) {
      return;
    }
    void this.chartWrittenDocument(progress.documentId);
  }

  /** Step 3's *Charts failed — retry*. */
  onChartRetryRequested(progress: BenchmarkReportPackDocumentProgressDto): void {
    if (progress.documentId !== null && progress.documentId !== undefined) {
      void this.chartWrittenDocument(progress.documentId);
    }
  }

  /** Step 3's job finished: step 4 lists afresh, with that job's documents chosen. */
  onReportJobFinished(job?: Partial<BenchmarkReportPackJobDto> | null): void {
    const ids = (job?.documents ?? [])
      .map(doc => doc.documentId)
      .filter((id): id is number => id !== null && id !== undefined);
    if (ids.length > 0 && this.comparison) {
      this.lastJobDocuments = { keys: this.comparison.entries.map(entry => entry.key).join(','), ids };
    }
    this.documentsReloadToken++;
    this.cdr.markForCheck();
  }

  /** Step 3 deleted a document: step 4 lists afresh. */
  onReportDocumentsChanged(): void {
    this.documentsReloadToken++;
    this.cdr.markForCheck();
  }

  /** Step 3's way to the documents: step 4, listed afresh. */
  onReportDocumentsRequested(): void {
    this.documentsReloadToken++;
    this.goToStep(4);
    document.getElementById('mc-step-tab-4')?.focus();
  }

  /**
   * Composes and uploads the chosen figures of every target through the one publisher, after any
   * publish already running, each document type's charts drawn and placed by its layout. Figures the
   * comparison cannot draw are left out of the selection.
   */
  publishCharts(
    targets: readonly ReportChartTarget[],
    selection: ReportChartSelection,
    onProgress?: (progress: ReportChartPublishProgress) => void
  ): Promise<ReportChartPublishResult> {
    const available = new Set(this.chartsAvailableKeys);
    const usable: Partial<Record<string, readonly ReportChartFigureKey[]>> = {};
    for (const [audience, keys] of Object.entries(selection)) {
      usable[audience] = ((keys ?? []) as readonly ReportChartFigureKey[]).filter(key => available.has(key));
    }
    const layout = this.chartLayout;
    this.pendingPublishes++;
    this.cdr.markForCheck();
    const run = async (): Promise<ReportChartPublishResult> => {
      const hash = await this.chartSettingsHashNow();
      return this.chartPublisher.publish(
        targets, usable as ReportChartSelection, (key, variant, audience) => this.composeReportChart(key, variant, audience),
        hash, onProgress, layout);
    };
    const result = this.publishQueue.then(run, run);
    this.publishQueue = result.catch(() => undefined);
    return result
      .then(outcome => {
        if (outcome.storageNotConfigured) {
          this.publishChartStorageMissing = true;
        }
        return outcome;
      })
      .finally(() => {
        this.pendingPublishes--;
        this.refreshDocumentChartActions();
        this.cdr.markForCheck();
      });
  }

  /**
   * Whether a document was written for the comparison open on step 2. Where both the document and the
   * open comparison are numbered, by the number. Otherwise, for a document written before comparisons
   * were numbered: its subject and every lettered peer are entries of the open comparison, and it
   * counted as many entries. The server keys a comparison by its entry set; the client never computes
   * that key, so it compares the entry set it can see.
   */
  documentMatchesComparison(doc: BenchmarkReportDocumentListItemDto): boolean {
    const id = this.comparisonIdentity?.id;
    if (id !== undefined && doc.comparisonId !== null && doc.comparisonId !== undefined) {
      return doc.comparisonId === id;
    }
    const entries = this.comparison?.entries ?? [];
    if (!doc.comparisonKey || entries.length === 0) {
      return false;
    }
    const keys = new Set(entries.map(entry => entry.key));
    if (!keys.has(doc.subjectKey)) {
      return false;
    }
    if (doc.comparisonEntryCount !== undefined && doc.comparisonEntryCount !== keys.size) {
      return false;
    }
    return Object.keys(doc.peerLetters ?? {}).every(key => keys.has(key));
  }

  /** Reads a written document for its peer letters, then charts it, and reports each step to step 3. */
  private async chartWrittenDocument(documentId: number): Promise<void> {
    this.setChartStatus(documentId, { state: 'attaching' });
    let doc: BenchmarkReportDocumentListItemDto;
    try {
      doc = await firstValueFrom(this.benchmarkService.getReportDocument(documentId));
    } catch {
      this.setChartStatus(documentId, { state: 'failed', message: 'The document could not be read to chart it.' });
      return;
    }
    const target = reportChartTargetFor(doc);
    const covered = reportChartCoveredKeys(target);
    if (covered ? covered.length < 2 : (doc.peerCount ?? 0) <= 0) {
      this.setChartStatus(documentId, {
        state: 'skipped',
        reason: covered ? 'It covers fewer than two models.' : 'It compares the model with no other model.'
      });
      return;
    }
    if (doc.pricingBasis && doc.pricingBasis !== this.chartPricingBasis) {
      this.setChartStatus(documentId, { state: 'skipped', reason: 'Step 2 now shows other prices than it was written on.' });
      return;
    }
    if (!this.documentMatchesComparison(doc)) {
      this.setChartStatus(documentId, { state: 'skipped', reason: 'Step 2 now shows another comparison.' });
      return;
    }

    let result: ReportChartPublishResult;
    try {
      result = await this.publishCharts([target], this.chartSelection);
    } catch (error) {
      this.setChartStatus(documentId, {
        state: 'failed',
        message: error instanceof Error && error.message ? error.message : 'The charts could not be drawn.'
      });
      return;
    }

    const published = result.published.find(item => item.documentId === documentId);
    const failed = result.failed.find(item => item.documentId === documentId);
    if (result.storageNotConfigured) {
      this.setChartStatus(documentId, { state: 'failed', message: result.storageNotConfigured });
    } else if (published) {
      this.setChartStatus(documentId, { state: 'done', count: published.figureCount, images: published.chartCount });
    } else if (failed) {
      this.setChartStatus(documentId, { state: 'failed', message: failed.message });
    } else if (result.skipped.includes(documentId)) {
      this.setChartStatus(documentId, { state: 'skipped', reason: 'No chart is chosen for its document type.' });
    } else {
      this.setChartStatus(documentId, { state: 'skipped', reason: 'Charting was canceled.' });
    }
    this.documentsReloadToken++;
    this.cdr.markForCheck();
  }

  private setChartStatus(documentId: number, status: ReportChartRowStatus): void {
    this.chartStatus = { ...this.chartStatus, [documentId]: status };
    this.cdr.markForCheck();
  }

  /** The wizard's dialog is open; true where no dialog holds it. */
  private get wizardOpen(): boolean {
    const dialog = this.hostElement.nativeElement.closest('dialog');
    return dialog ? dialog.open : true;
  }

  // --- The document charts ---

  /** What shapes the document charts, as the settings hash reads it. */
  chartSettingsInput(): ReportChartSettingsInput {
    const { bar, scatter, profile, numbers, appearance } = this.figureStyle;
    return {
      figureStyle: {
        bar, scatter, profile, numbers, appearance,
        scatterDirectLabels: this.scatterDirectLabels,
        scatterInlineValues: this.scatterInlineValues
      },
      layout: documentChartHashLayout(this.chartLayout),
      show: [...this.includedKeys].sort(),
      highlight: [...this.emphasisKeys].sort(),
      order: this.sort,
      measures: { speed: this.speedMeasure, cost: this.costMeasure },
      pricingBasis: this.chartPricingBasis,
      computedAtUtc: this.comparison?.computedAtUtc ?? null
    };
  }

  /** Step 2's settings hash, computing it now if the settings changed since it was last computed. */
  chartSettingsHashNow(): Promise<string> {
    this.refreshChartSettingsHash();
    return this.chartSettingsHashPending ?? chartSettingsHash(this.chartSettingsInput());
  }

  /** Recomputes the settings hash when what shapes the charts changed; a hover or a view change leaves it. */
  private refreshChartSettingsHash(): void {
    if (!this.comparison) {
      return;
    }
    const input = this.chartSettingsInput();
    const signature = canonicalJson(input);
    if (signature === this.chartSettingsSignature) {
      return;
    }
    this.chartSettingsSignature = signature;
    const pending = chartSettingsHash(input);
    this.chartSettingsHashPending = pending;
    pending.then(hash => {
      if (this.chartSettingsHashPending !== pending) {
        return;
      }
      this.currentChartSettingsHash = hash;
      this.refreshDocumentChartActions();
      this.cdr.markForCheck();
    }, () => {
      // No digest without a secure context: the charts cannot be called current, and a publish says why.
    });
  }

  private refreshChartsAvailable(): void {
    const plotted = this.plotted.length;
    const keys = REPORT_CHART_FIGURES.filter(figure => plotted >= Math.max(2, figure.minModels)).map(figure => figure.key);
    if (keys.join(',') !== this.chartsAvailableKeys.join(',')) {
      this.chartsAvailableKeys = keys;
    }
  }

  /** Lends step 4 a new chart actions object whenever one of their values changed, so its view follows. */
  private refreshDocumentChartActions(): void {
    const values = {
      currentSettingsHash: this.currentChartSettingsHash,
      pricingBasis: this.chartPricingBasis,
      available: this.chartsAvailableKeys,
      advisory: this.chartAdvisory,
      storageMissing: this.chartStorageMissing
    };
    const current = this.documentChartActions;
    const comparisonId = this.comparisonIdentity?.id ?? null;
    if (current && current.currentSettingsHash === values.currentSettingsHash && current.pricingBasis === values.pricingBasis
        && current.available === values.available && current.advisory === values.advisory
        && current.storageMissing === values.storageMissing && this.chartActionsComparisonId === comparisonId) {
      return;
    }
    // A new object when the numbered comparison changes too, since comparisonKeyMatches then answers differently.
    this.chartActionsComparisonId = comparisonId;
    this.documentChartActions = {
      ...values,
      comparisonKeyMatches: doc => this.documentMatchesComparison(doc),
      publish: (targets, selection, onProgress) => this.publishCharts(targets, selection, onProgress)
    };
  }

  /** One document type's chart layout; the defaults without a document type. */
  private documentLayoutFor(audience?: BenchmarkReportAudience | null): ReportChartDocumentLayout {
    return documentChartLayoutFor(this.chartLayout, audience);
  }

  /**
   * The bar orientation of a document type's charts: the layout's own choice, or under *As in step 2*
   * step 2's, whose *Automatic* is resolved at the document chart's own layout width (a full-column
   * chart at 8 pt is about 663 layout px wide, below the 720 px breakpoint, so horizontal), never at
   * the visible chart host's.
   */
  documentChartOrientation(key: ReportChartFigureKey = 'p1a-quality', audience?: BenchmarkReportAudience | null): BarOrientation {
    const layout = this.documentLayoutFor(audience);
    if (layout.orientation !== 'asInStep2') {
      return layout.orientation;
    }
    const choice = this.figureStyle.bar.orientation;
    if (choice !== 'auto') {
      return choice;
    }
    const box = documentFigureLayout(key, documentChartWidth(layout, key), layout.labelPt);
    return box.layoutWidth > 0 && box.layoutWidth < P1_STACK_BREAKPOINT_PX ? 'horizontal' : 'vertical';
  }

  /**
   * One figure as a document prints it, composed off-screen as a PNG for the document type's layout
   *: its width of the text column at 300 dpi, its bar labels at the layout's size in points, its
   * orientation, heading, logo and theme, with step 2's styles, Show, Highlight, order and measures.
   * Without a document type, the default layout.
   *
   * The named variant draws the comparison as step 2 does. The anonymized variant draws a copy in
   * which the document's lettered peers are *Model A*… in the neutral gray, the others are left out,
   * only the subject is highlighted, and set-level notes that name a model are dropped. A variant with
   * `coveredKeys` (a comparison-scope document) plots those entries only: named, step 2's chart with
   * Show limited to them and Highlight kept where it falls among them; anonymized, every one of them
   * lettered. Throws when the figure cannot be drawn for this comparison or does not fit.
   */
  async composeReportChart(
    key: ReportChartFigureKey,
    variant: ReportChartVariant,
    audience?: BenchmarkReportAudience | null
  ): Promise<ComposedReportChart> {
    const comparison = this.comparison;
    if (!comparison) {
      throw new Error('No comparison is open.');
    }
    const title = REPORT_CHART_FIGURES.find(figure => figure.key === key)?.title ?? key;
    const documentLayout = this.documentLayoutFor(audience);
    const width = documentChartWidth(documentLayout, key);
    const widthRefusal = documentChartRefusal(width, documentLayout.labelPt);
    if (widthRefusal) {
      throw new Error(`${title} cannot be drawn: ${widthRefusal}`);
    }
    const look = this.documentLook(documentLayout);
    const built = this.buildDocumentFigures(comparison, variant, this.documentChartOrientation(key, audience), look);
    const card = this.documentCard(built.figures, key);
    if (!card) {
      throw new Error(`${title} cannot be drawn: this comparison plots too few models for it.`);
    }
    await this.prepareFigureComposition();
    const logo = documentLayout.logo ? await this.documentFigureLogo() : null;
    const chrome = this.documentChrome(card, built.setNotes, documentLayout, look.theme, logo);
    const box = this.documentFigureBox(key, width, documentLayout.labelPt, chrome);
    const { layout, refusal } = resolveFigureLayout(chrome, documentResolution(box), 1, box.textScale);
    if (!layout) {
      throw new Error(refusal ?? `${card.title} does not fit a document chart.`);
    }
    const canvas = await this.composeFigure(card, chrome, layout, 'png');
    if (!canvas) {
      throw new Error(`${card.title} could not be composed: its chart could not be built.`);
    }
    const encoded = await encodeFigureImage(canvas, 'png');
    const computed = formatUtc(comparison.computedAtUtc) || comparison.computedAtUtc;
    return {
      png: encoded.blob,
      widthPx: layout.pixelWidth,
      heightPx: layout.pixelHeight,
      title: card.title,
      caption: [card.chrome.detail, this.documentChartMeasureNote(key), `Drawn from the comparison computed ${computed}.`]
        .filter(part => part).join(' '),
      altText: this.documentChartAltText(key, card.title, built.figures)
    };
  }

  /**
   * A document chart caption's note on the measure it plots, where the document's own tables give
   * another: the speed figures' time measure (none for the Speed Index), and the cost panel's cost
   * per pass. Empty for every other figure.
   */
  private documentChartMeasureNote(key: ReportChartFigureKey): string {
    if (key === 'p1b-speed' || key === 's1-quality-speed' || key === 's3-speed-cost') {
      switch (this.speedMeasure) {
        case 'meanModelTime':
          return 'Times are the mean model time per question; the document\'s tables give the median.';
        case 'totalModelTime':
        case 'ttftP50': {
          const name = MEASURE_NAMES[this.speedMeasure];
          return `Times are the ${name.charAt(0).toLocaleLowerCase('en-US')}${name.slice(1)}; the document's tables give the median.`;
        }
        default:
          return '';
      }
    }
    if (key === 'p1c-cost') {
      return 'Costs are per battery pass (per suite run for a suite); the document\'s tables give the cost per question.';
    }
    return '';
  }

  /**
   * The chosen figures of a document type, composed as its charts are, with the layout the server
   * would place them by: what a layout preview shows. A figure that cannot be drawn is listed with
   * its reason and left out of the layout.
   */
  async composeDocumentCharts(
    audience: BenchmarkReportAudience,
    variant: ReportChartVariant = { kind: 'named' },
    scope: ReportChartScope = 'model'
  ): Promise<{
    charts: { key: ReportChartFigureKey; chart: ComposedReportChart }[];
    failed: { key: ReportChartFigureKey; message: string }[];
    layout: ReportDocumentChartLayout;
  }> {
    const available = new Set(this.chartsAvailableKeys);
    const keys = (normalizeChartSelection(this.chartSelection)[audience] ?? []).filter(key => available.has(key));
    const charts: { key: ReportChartFigureKey; chart: ComposedReportChart }[] = [];
    const failed: { key: ReportChartFigureKey; message: string }[] = [];
    for (const key of keys) {
      try {
        charts.push({ key, chart: await this.composeReportChart(key, variant, audience) });
      } catch (error) {
        failed.push({ key, message: error instanceof Error && error.message ? error.message : 'The chart could not be drawn.' });
      }
    }
    return {
      charts,
      failed,
      layout: reportDocumentChartLayout(audience, charts.map(item => item.key), this.documentLayoutFor(audience), scope)
    };
  }

  /** `composeDocumentCharts`, lent to step 3 for its Preview layout. */
  readonly documentChartsComposer: DocumentChartsComposer =
    (audience, variant, scope) => this.composeDocumentCharts(audience, variant, scope);

  /**
   * A document figure's box: its width's aspect, made taller where the chrome would leave the plot
   * less than {@link DOCUMENT_MIN_PLOT_HEIGHT}. The chrome is measured at the content width alone, so a
   * probe composed three widths tall gives its height; a probe the composer refuses (for its width)
   * leaves the aspect's box for the real call to refuse with its reason.
   */
  private documentFigureBox(key: ReportChartFigureKey, width: ReportChartWidth, labelPt: number, chrome: FigureExportChrome): DocumentChartLayout {
    const box = documentFigureLayout(key, width, labelPt);
    const probe = documentChartLayout(box.widthPt, box.widthPt * 3, labelPt);
    const probed = resolveFigureLayout(chrome, documentResolution(probe), 1, probe.textScale).layout;
    if (!probed) {
      return box;
    }
    const chromeHeight = probed.layoutHeight - probed.plotHeight;
    return documentFigureLayout(key, width, labelPt, DOCUMENT_CHART_COLUMN_PT, chromeHeight + DOCUMENT_MIN_PLOT_HEIGHT);
  }

  /**
   * The style and theme a document type's charts are drawn in: step 2's, or *Light, for print*, with
   * the bar and scatter text normalized so the layout's label size holds for both (`documentTextStyle`).
   */
  private documentLook(layout: ReportChartDocumentLayout): { style: FigureStyle; theme: ResolvedFigureTheme } {
    const style = documentTextStyle(this.figureStyle);
    if (layout.theme === 'asInStep2') {
      return { style, theme: this.figureTheme };
    }
    const appearance = printFigureAppearance(this.figureStyle.appearance);
    return { style: { ...style, appearance }, theme: resolveFigureTheme(appearance) };
  }

  /** Step 2's logo variant at its height, loaded now if step 2 does not show it. Null where it fails to load. */
  private async documentFigureLogo(): Promise<FigureLogo | null> {
    const appearance = this.figureStyle.appearance;
    const variant = appearance.logoVariant;
    const image = this.figureLogos[variant] ?? await ensureFigureLogo(variant);
    if (!image) {
      return null;
    }
    this.figureLogos[variant] = image;
    return { image, aspectRatio: figureLogoAspect(variant), heightPx: appearance.logoHeightPx };
  }

  /**
   * A document chart's chrome: step 2's for the card, in the document's theme and with its logo
   * choice, and the heading the layout asks for. *None* leaves the title, the badges, the Better badge
   * and the detail line to the document's caption; *Title* keeps the title alone. Only *Title and
   * badges* keeps the footer: the document itself names the suite and the computation time. It is
   * laid out against the document charts' narrower content column, {@link DOCUMENT_MIN_CONTENT_WIDTH}.
   */
  private documentChrome(
    card: ComparisonFigureCard,
    setNotes: readonly FigureNote[],
    layout: ReportChartDocumentLayout,
    theme: ResolvedFigureTheme,
    logo: FigureLogo | null
  ): FigureExportChrome {
    const base = { ...this.exportChrome(card, setNotes), minContentWidth: DOCUMENT_MIN_CONTENT_WIDTH };
    if (layout.heading === 'titleAndBadges') {
      return { ...base, theme, logo };
    }
    const chrome: FigureChrome = {
      title: layout.heading === 'title' ? base.chrome.title : '',
      badges: [],
      detail: '',
      key: base.chrome.key,
      highlight: base.chrome.highlight,
      notes: base.chrome.notes
    };
    return { ...base, chrome, footer: { suite: '', computedAt: '' }, theme, logo };
  }

  /** The figure set a document chart is drawn from, and the set-level notes its chrome carries. */
  private buildDocumentFigures(
    comparison: BenchmarkModelComparisonDto,
    variant: ReportChartVariant,
    orientation: BarOrientation,
    look: { style: FigureStyle; theme: ResolvedFigureTheme } = { style: this.figureStyle, theme: this.figureTheme }
  ): { figures: ComparisonFigureSet; setNotes: readonly FigureNote[] } {
    const options = {
      sort: this.sort,
      speedMeasure: this.speedMeasure,
      costMeasure: this.costMeasure,
      orientation,
      directLabels: this.scatterDirectLabels,
      inlineValues: this.scatterInlineValues,
      reducedMotion: this.reducedMotion.matches,
      highlightedKey: null,
      style: look.style,
      theme: look.theme
    };
    if (variant.coveredKeys) {
      return this.buildCoveredDocumentFigures(comparison, variant, variant.coveredKeys, options);
    }
    if (variant.kind === 'named') {
      const input = this.chartEntries.filter(entry => entry.excluded || this.includedKeys.includes(entry.key));
      const figures = buildComparisonFigures(input, {
        ...options, context: this.context, selectedKeys: this.emphasisKeys, glyphSource: this.chartEntries
      });
      return { figures, setNotes: this.setFigureNotes };
    }

    const anonymized = anonymizeComparisonForSubject(comparison, variant.subjectKey, variant.letters);
    const entries = toChartEntries(anonymized);
    const input = entries.filter(entry => entry.excluded || this.includedKeys.includes(entry.key));
    const figures = buildComparisonFigures(input, {
      ...options,
      context: toChartContext(anonymized),
      selectedKeys: this.includedKeys.includes(variant.subjectKey) ? [variant.subjectKey] : [],
      glyphSource: entries
    });
    const names = [...comparison.entries, ...anonymized.entries]
      .flatMap(entry => [entry.label, entry.modelDisplayName])
      .filter(name => !!name && name.trim().length >= 2)
      .map(name => name.toLowerCase());
    const plottedKeys = new Set(figures.selection.plotted.map(entry => entry.key));
    const setNotes: FigureNote[] = [
      ...figures.selection.notices.map(text => ({ text, tone: 'warning' as const })),
      ...questionCoverageNotes(anonymized.entries.filter(entry => plottedKeys.has(entry.key)))
    ].filter(note => !names.some(name => note.text.toLowerCase().includes(name)));
    return { figures, setNotes };
  }

  /**
   * A comparison-scope document's figure set: its covered entries only, those step 2 shows, with
   * step 2's Highlight where it falls among them. The named copy keeps the names and step 2's glyphs;
   * the anonymized copy letters every covered entry. Set-level notes that name an entry the document
   * leaves out, or under the anonymized copy any entry, are dropped.
   */
  private buildCoveredDocumentFigures(
    comparison: BenchmarkModelComparisonDto,
    variant: ReportChartVariant,
    coveredKeys: readonly string[],
    options: Omit<Parameters<typeof buildComparisonFigures>[1], 'context' | 'selectedKeys' | 'glyphSource'>
  ): { figures: ComparisonFigureSet; setNotes: readonly FigureNote[] } {
    const covered = new Set(coveredKeys);
    const subset = variant.kind === 'anonymized'
      ? anonymizeComparisonForAll(comparison, Object.fromEntries(
        Object.entries(variant.letters).filter(([key]) => covered.has(key))))
      : restrictComparisonToEntries(comparison, coveredKeys);
    const entries = variant.kind === 'anonymized'
      ? toChartEntries(subset)
      : this.chartEntries.filter(entry => covered.has(entry.key));
    const input = entries.filter(entry => !entry.excluded && this.includedKeys.includes(entry.key));
    const figures = buildComparisonFigures(input, {
      ...options,
      context: toChartContext(subset),
      selectedKeys: this.emphasisKeys.filter(key => covered.has(key)),
      glyphSource: variant.kind === 'anonymized' ? entries : this.chartEntries
    });
    const named = variant.kind === 'anonymized'
      ? [...comparison.entries, ...subset.entries]
      : comparison.entries.filter(entry => !covered.has(entry.key));
    const names = named
      .flatMap(entry => [entry.label, entry.modelDisplayName])
      .filter(name => !!name && name.trim().length >= 2)
      .map(name => name.toLowerCase());
    const plottedKeys = new Set(figures.selection.plotted.map(entry => entry.key));
    const setNotes: FigureNote[] = [
      ...figures.selection.notices.map(text => ({ text, tone: 'warning' as const })),
      ...questionCoverageNotes(subset.entries.filter(entry => plottedKeys.has(entry.key)))
    ].filter(note => !names.some(name => note.text.toLowerCase().includes(name)));
    return { figures, setNotes };
  }

  /** One figure of a set as a card, or null where the set plots too few models for it. */
  private documentCard(figures: ComparisonFigureSet, key: ReportChartFigureKey): ComparisonFigureCard | null {
    const plotted = figures.selection.plotted.length;
    if (plotted < 2 || (key === 'p2-profile' && plotted < 3)) {
      return null;
    }
    switch (key) {
      case 'p1a-quality': return this.toCard(figures.smallMultiples.quality);
      case 'p1b-speed': return this.toCard(figures.smallMultiples.speed);
      case 'p1c-cost': return this.toCard(figures.smallMultiples.cost);
      case 'p2-profile': return this.toProfileCard(figures.profile);
      case 's1-quality-speed': return this.toCard(figures.qualitySpeed);
      case 's2-quality-cost': return this.toCard(figures.qualityCost);
      case 's3-speed-cost': return this.toCard(figures.speedCost);
    }
  }

  /** The title, then one clause per plotted model: its value and interval on the figure's measures. */
  private documentChartAltText(key: ReportChartFigureKey, title: string, figures: ComparisonFigureSet): string {
    const clauses = figures.selection.plotted.map(entry => `${entry.label}: ${this.documentChartValues(key, entry)}`);
    return clauses.length > 0 ? `${title}. ${clauses.join('; ')}.` : `${title}.`;
  }

  private documentChartValues(key: ReportChartFigureKey, entry: ModelComparisonEntry): string {
    const quality = `Intelligence Index ${formatIndexText(entry.intelligenceIndex)} ± ${formatIndexText(entry.intelligenceIndexCi95HalfWidth)}`;
    const speed = this.documentSpeedClause(entry);
    const perQuestion = `cost per question ${formatUsdText(entry.candidateCostPerQuestionUsd)}`
      + (entry.candidateCostPerQuestionSdUsd !== null ? ` ± ${formatUsdText(entry.candidateCostPerQuestionSdUsd)}` : '');
    const suite = this.costMeasure === 'candidateSuite';
    const sd = suite ? suiteCostSdUsd(entry) : entry.totalRunCostSdUsd;
    const cost = `${suite ? 'candidate cost for the suite' : 'run cost with grading'} `
      + `${formatUsdText(costValue(entry, this.costMeasure, this.context))}${sd !== null ? ` ± ${formatUsdText(sd)}` : ''}`;
    switch (key) {
      case 'p1a-quality': return quality;
      case 'p1b-speed': return speed;
      case 'p1c-cost': return cost;
      case 'p2-profile': return [quality, speed, cost].join(', ');
      case 's1-quality-speed': return [quality, speed].join(', ');
      case 's2-quality-cost': return [quality, perQuestion].join(', ');
      case 's3-speed-cost': return [speed, perQuestion].join(', ');
    }
  }

  private documentSpeedClause(entry: ModelComparisonEntry): string {
    const value = speedValue(entry, this.speedMeasure);
    switch (this.speedMeasure) {
      case 'speedIndex':
        return `Speed Index ${formatIndexText(value)}`;
      case 'ttftP50':
        return `median time to first token ${formatMsText(value)} (P90 ${formatMsText(entry.ttftP90Ms)})`;
      case 'totalModelTime':
        return `model time for the suite ${formatMsText(value)}`
          + (entry.totalModelTimeSdMs !== null ? ` ± ${formatMsText(entry.totalModelTimeSdMs)}` : '');
      default:
        return `mean model time per question ${formatMsText(value)}`;
    }
  }

  /** One line on how much of the set can be charted together. */
  get aboutSummary(): string {
    const total = this.entries.length;
    const excluded = this.excludedEntries.length;
    if (excluded === 0) {
      return `All ${total} models were measured the same way and can be charted together.`;
    }
    const one = excluded === 1;
    return `${total - excluded} of ${total} models can be charted together. ${excluded} `
      + `${one ? 'was' : 'were'} measured differently and ${one ? 'is' : 'are'} in the table only.`;
  }

  /** The caveats that qualify the figures, each counted on the About button's badge. */
  get aboutNotes(): AboutNote[] {
    const notes: AboutNote[] = [];
    if (this.comparison?.thinkingLevelsDiffer) {
      notes.push({
        id: 'thinking-levels',
        tone: 'warning',
        heading: 'The models use different thinking levels',
        body: 'Thinking level has a large effect on speed. The charts compare each model as it is '
          + 'configured, not at one shared thinking level.'
      });
    }
    if (this.allSingleRun) {
      notes.push({
        id: 'single-run',
        tone: 'info',
        heading: 'Each model has only one run',
        body: 'Nothing here shows how much a repeat run would differ. Treat small gaps between models '
          + 'as uncertain, and read the ± ranges before the bar tops.'
      });
    }
    return notes;
  }

  get aboutNoteCount(): number {
    return this.aboutNotes.length;
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

  /** P2 — the model profiles, one tile per model, suppressed below three entries. */
  get profileCard(): ComparisonFigureCard | null {
    return this.figures && this.showProfile ? this.toProfileCard(this.figures.profile) : null;
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
    summary?: readonly string[];
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

  /** The profile's card: every tile with the shared plugins, stitched `columns` wide when composed. */
  private toProfileCard(profile: ProfileFigure): ComparisonFigureCard {
    const configs: OffscreenPlotConfig[] = profile.tiles.map(tile => ({
      type: tile.type as ChartType,
      data: tile.data as unknown as ChartConfiguration['data'],
      options: tile.options as unknown as ChartConfiguration['options'],
      plugins: profile.plugins
    }));
    const first = configs[0];
    return {
      id: profile.id,
      title: profile.title,
      chrome: profile.chrome,
      ariaLabel: this.chartAriaLabel(profile),
      type: 'line',
      data: first ? first.data : { datasets: [] },
      options: first?.options,
      plugins: profile.plugins,
      tiles: { configs, columns: profile.columns }
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

  private readonly storedDownload = readStoredDownloadSettings();

  /** The image format every chart and the table image are written in; remembered per browser. */
  exportFormat: FigureExportFormat = this.storedDownload.imageFormat;

  /** The qualities a WebP export may be written at, offered whenever WebP is the chosen format. */
  readonly webpQualityOptions = WEBP_QUALITY_OPTIONS;

  /**
   * The WebP quality of every chart and of the table image. One setting, because the image format it
   * qualifies is one shared setting too; remembered per browser.
   */
  webpQuality: WebpQuality = this.storedDownload.webpQuality;

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

  // --- Chart size and table image size ---
  //
  // Two `FigureSizeSettings`, each hosted by an `app-export-size-section` in the Download tab, which
  // owns the controls, the custom-ratio lock and the reset's status line.
  //
  // The chart size shapes every chart: the All tab, the Single tab and every download compose at
  // it. Every chart size composes at one layout width and scales, so a 4K chart and a Full HD chart
  // differ in pixels and not in relative type size; the density then multiplies the bitmap of
  // whichever size was chosen, leaving the composition alone.
  //
  // The table image size is its own, because a table and a chart are very different shapes: *Fit
  // the table*, or a box in plain pixels, where the table either fits or is refused with the minimum
  // it needs (`resolveTableImageLayout`).

  /**
   * The display's density when the component initialised, which the matching option is labelled
   * with and which the control opens on.
   *
   * Sampled once and then left alone: a reader who chose 300 % and dragged the window to another
   * display keeps 300 %, and the labelled option is how they find their way back.
   */
  readonly displayDensity = displayDensity();

  /**
   * Size, density and text size of every chart, as the Download tab's Chart size section sets
   * them. Read from storage when the component is created and written on every change; replaced,
   * never mutated.
   */
  figureSize: FigureSizeSettings = readStoredFigureSize(this.displayDensity);

  /** Full HD, the display's own density and 100 % text: what the Chart size section resets to. */
  readonly defaultChartSize: FigureSizeSettings = defaultFigureSize(this.displayDensity);

  /** The table image's size, stored apart from the charts'. */
  tableImageSize: FigureSizeSettings = readStoredTableImageSize();

  /** *Fit the table* at 200 % and 100 % text: the table image as it was always written. */
  readonly defaultTableImageSize: FigureSizeSettings = defaultTableImageSize();

  /** The one-line meaning of 100 % text in each size section. */
  readonly chartTextSizeHint = 'Scales the caption, notes and chart text together without changing the pixel size.';
  readonly tableTextSizeHint =
    '100 % draws the table text at the size Fit the table uses; the table is laid out in the pixels above, before density.';

  get isCustomResolution(): boolean {
    return this.figureSize.resolutionId === 'custom';
  }

  /** The chosen preset, or the custom pair clamped into the supported range. */
  get exportResolution(): FigureExportResolution {
    return resolveSizeResolution(this.figureSize);
  }

  get isCustomDensity(): boolean {
    return this.figureSize.densitySelection === 'custom';
  }

  /** The chosen factor: a listed preset, or the custom percentage clamped into its bounds. */
  get exportDensity(): number {
    return resolveSizeDensity(this.figureSize);
  }

  /** The percentage the read-outs name the current density by. */
  get exportDensityLabel(): string {
    return densityPercentLabel(this.exportDensity);
  }

  /** The current size's shape, e.g. `16:9`. */
  get exportAspectLabel(): string {
    const resolution = this.exportResolution;
    return aspectRatioLabel(resolution.widthPx, resolution.heightPx);
  }

  /**
   * The size and format in one line, for the chart Download tooltips.
   *
   * The toolbars carry the settings as a read-out rather than as controls: the size and the
   * format live in the sidebar's Download tab.
   */
  get exportSummary(): string {
    const format = this.exportFormat === 'webp'
      ? `WebP q${this.webpQuality}`
      : 'PNG';
    return `${this.exportResolution.label} · ${this.exportDensityLabel} · ${format}`;
  }

  /** What *Download all charts* would do now, or why it will not. */
  get downloadAllTooltip(): string {
    if (this.exporting) {
      return 'An export is running.';
    }
    if (this.exportSizeError !== '') {
      return this.exportSizeError;
    }
    return `All charts as one archive — ${this.exportSummary}`;
  }

  /** What a one-chart Download would do now, or why it will not. */
  get downloadFigureTooltip(): string {
    if (this.exporting) {
      return 'An export is running.';
    }
    if (this.exportSizeError !== '') {
      return this.exportSizeError;
    }
    return `Download this chart — ${this.exportSummary}`;
  }

  /** An out-of-range custom size, named. Empty while the current setting is usable. */
  get customResolutionError(): string {
    return sizeErrors(this.figureSize).customResolution;
  }

  /** An out-of-range custom density, named. Empty while the current setting is usable. */
  get customDensityError(): string {
    return sizeErrors(this.figureSize).customDensity;
  }

  /**
   * A written bitmap no browser can allocate, named. Empty while either input is out of range,
   * which is the more specific complaint and is what the reader has to fix first.
   */
  get exportDensityError(): string {
    return sizeErrors(this.figureSize).bitmap;
  }

  /** The one message the size, the custom sides and the density all describe themselves by. */
  get exportSizeError(): string {
    return sizeErrors(this.figureSize).any;
  }

  /** What the current setting will actually write, in the reader's own units. */
  get exportDimensionsLabel(): string {
    return sizeDimensionsLabel(this.figureSize);
  }

  /** The closed Chart size section's one-line read-out. */
  get figureSizeReadout(): string {
    return sizeReadout(this.figureSize);
  }

  /** Whether the Chart size section holds its defaults, which is when its reset has nothing to do. */
  get figureSizeIsDefault(): boolean {
    return sameFigureSize(this.figureSize, this.defaultChartSize);
  }

  /** Restores Full HD, the display's own density and 100 % text. */
  resetFigureSize(): void {
    if (this.figureSizeIsDefault) {
      return;
    }
    this.setFigureSize(this.defaultChartSize);
  }

  /** The Chart size section's whole new settings. */
  onFigureSizeChange(settings: FigureSizeSettings): void {
    this.setFigureSize(settings);
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

  onCustomWidthChange(width: number): void {
    this.setFigureSize({ customWidthPx: width });
  }

  onCustomHeightChange(height: number): void {
    this.setFigureSize({ customHeightPx: height });
  }

  /** The text size as the factor the layout resolver takes. */
  get exportTextScale(): number {
    return this.figureSize.textScalePercent / 100;
  }

  /** Composition text size as a percentage: larger composes in a smaller box, at the same pixel size. */
  onExportTextScaleChange(percent: number): void {
    const value = Number.isFinite(percent) ? Math.round(percent) : 100;
    this.setFigureSize({
      textScalePercent: Math.min(FIGURE_EXPORT_MAX_TEXT_SCALE_PERCENT, Math.max(FIGURE_EXPORT_MIN_TEXT_SCALE_PERCENT, value))
    });
  }

  /**
   * Replaces the chart size, stores it and re-composes the chart view shown. The All tab's tiles
   * take their new box at once; their bitmaps follow once the change pauses. The table image is not
   * touched: it has a size of its own.
   */
  private setFigureSize(patch: Partial<FigureSizeSettings>): void {
    this.figureSize = { ...this.figureSize, ...patch };
    writeStoredFigureSize(this.figureSize);
    if (this.figureTab === 'single') {
      this.schedulePreview();
    }
    if (this.allActive) {
      this.refreshAllGeometry();
      this.markAllStale();
      this.scheduleAllCompose();
    }
    this.cdr.markForCheck();
  }

  /** Whether the Table image size section holds its defaults. */
  get tableImageSizeIsDefault(): boolean {
    return sameFigureSize(this.tableImageSize, this.defaultTableImageSize);
  }

  /** The Table image size section's whole new settings. */
  onTableImageSizeChange(settings: FigureSizeSettings): void {
    this.setTableImageSize(settings);
  }

  /** Restores *Fit the table* at 200 % and 100 % text. */
  resetTableImageSize(): void {
    if (!this.tableImageSizeIsDefault) {
      this.setTableImageSize(this.defaultTableImageSize);
    }
  }

  /** Replaces the table image size, stores it, and re-composes only what depends on it: the Table preview. */
  private setTableImageSize(settings: FigureSizeSettings): void {
    this.tableImageSize = { ...settings };
    writeStoredTableImageSize(this.tableImageSize);
    this.scheduleTableOutputs();
    this.cdr.markForCheck();
  }

  // --- Image format ---

  onExportFormatChange(value: FigureExportFormat): void {
    this.exportFormat = value;
    this.imageFormatResetStatus = '';
    this.writeStoredDownload();
    this.cdr.markForCheck();
  }

  onWebpQualityChange(value: WebpQuality): void {
    this.webpQuality = value;
    this.imageFormatResetStatus = '';
    this.writeStoredDownload();
    this.cdr.markForCheck();
  }

  /** The closed Image format section's one-line read-out. */
  get imageFormatReadout(): string {
    return this.exportFormat === 'webp' ? `WebP · quality ${this.webpQuality}` : 'PNG';
  }

  get imageFormatIsDefault(): boolean {
    return this.exportFormat === 'png' && this.webpQuality === DEFAULT_WEBP_QUALITY;
  }

  /** What the Image format section's status line announces after a reset; cleared by the next change. */
  imageFormatResetStatus = '';

  /** Restores PNG, and 85 for a later WebP. */
  resetImageFormat(): void {
    if (this.imageFormatIsDefault) {
      return;
    }
    this.exportFormat = 'png';
    this.webpQuality = DEFAULT_WEBP_QUALITY;
    this.writeStoredDownload();
    this.imageFormatResetStatus = 'Image format reset to defaults.';
    this.cdr.markForCheck();
  }

  private writeStoredDownload(): void {
    try {
      localStorage.setItem(DOWNLOAD_SETTINGS_STORAGE_KEY, JSON.stringify({
        version: 1,
        tableFormat: this.tableFormat,
        imageFormat: this.exportFormat,
        webpQuality: this.webpQuality
      }));
    } catch {
      // Private mode or blocked storage: the formats still apply for this session.
    }
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
    // The chrome is measured before it is drawn, so the face and the logo have to be there first.
    await this.prepareFigureComposition();
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
      result: await encodeFigureImage(composed, format, this.webpQuality),
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
    await this.prepareFigureComposition();
    const plot = card.tiles
      ? await renderTiledPlotOffscreen(card.tiles.configs, card.tiles.columns, layout)
      : await renderPlotOffscreen(
        { type: card.type, data: card.data, options: card.options, plugins: card.plugins },
        layout
      );
    return plot ? composeFigureImage({ ...chrome, canvas: plot, format, layout }) : null;
  }

  /**
   * One card's chrome: everything the exported image carries besides the plot itself, at its
   * family's caption sizes and Better badge placement, with the footer emptied while the family
   * hides it, in the theme. The set-level notes are the comparison's unless a document chart
   * brings its own.
   */
  private exportChrome(card: ComparisonFigureCard, setNotes: readonly FigureNote[] = this.setFigureNotes): FigureExportChrome {
    const style = this.figureStyle[this.familyOf(card)];
    return {
      chrome: { ...card.chrome, notes: [...card.chrome.notes, ...setNotes] },
      footer: style.footer ? this.figureFooter : { suite: '', computedAt: '' },
      textSizes: {
        titlePx: style.titleSizePx,
        badgePx: style.badgeTextSizePx,
        footerPx: style.footerTextSizePx
      },
      theme: this.figureTheme,
      logo: this.figureLogo(),
      betterBadgePlacement: 'betterBadgePlacement' in style ? style.betterBadgePlacement : undefined
    };
  }

  /** Which style family a card draws from: the bar panels, the trade-off scatters or the profile. */
  private familyOf(card: ComparisonFigureCard | null | undefined): ChartStyleFamily {
    const type = card?.type;
    return type === 'bar' ? 'bar' : type === 'scatter' ? 'scatter' : 'profile';
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
    const battery = !dto?.baselineSuiteName && !!dto?.baselineBatteryName;
    return {
      ...(battery ? { suiteLabel: 'Battery' } : {}),
      suite: dto?.baselineSuiteName || dto?.baselineBatteryName || 'Suite not set',
      pricingBasis: dto?.pricingBasisLabel || dto?.pricingBasis || 'Unknown pricing basis',
      conditionSignature: (dto?.baselineSignature ?? '').trim().slice(0, 12),
      computedAt: dto?.computedAtUtc ? parseServerUtcDate(dto.computedAtUtc).toLocaleString() : 'unknown time',
      plottedOfTotal: `${this.plotted.length} of ${this.entries.length} entries charted`,
      notices: this.setNotices
    };
  }

  /** The computation time, the one part of the provenance the wizard header does not show. */
  get tableComputedAtLine(): string {
    return `Computed ${this.tableProvenance.computedAt}`;
  }

  /**
   * The figure footer: the suite (the battery, for a battery comparison) and the computation time, in
   * the composer's own two-sided layout, under each figure whose family has the footer on.
   */
  get figureFooter(): FigureFooter {
    const dto = this.comparison;
    const battery = !dto?.baselineSuiteName && !!dto?.baselineBatteryName;
    return {
      ...(battery ? { label: 'Battery' } : {}),
      suite: dto?.baselineSuiteName || dto?.baselineBatteryName || 'Suite not set',
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
  // The step-2 workspace
  //
  // A collapsible settings sidebar beside five views: All charts, Single chart, the Interactive
  // table, the Table preview and the Paired tests. The chart views and the Table preview show images
  // composed by the export pipeline, so the page and a download are the same image. The sidebar's
  // tabs follow the view group — Data · Theme · Charts · Download beside the charts, Data · Theme ·
  // Table · Download beside the table, Data alone beside the paired tests — so a tab never holds a
  // setting that does not apply to the view it is shown with. Every control exists once.
  // ---------------------------------------------------------------------------------------------

  private readonly storedSidebar = readStoredFigureSidebar();

  /** All charts by default; kept across steps and remembered per browser. */
  figureTab: FigureViewTab = this.storedSidebar.view;

  /** The five views. Each label is also the tab's accessible name. */
  readonly figureTabs: readonly { readonly id: FigureViewTab; readonly label: string }[] = [
    { id: 'all', label: 'All charts' },
    { id: 'single', label: 'Single chart' },
    { id: 'table', label: 'Interactive table' },
    { id: 'tablePreview', label: 'Table preview' },
    { id: 'paired', label: 'Paired tests' }
  ];

  readonly isChartView = isChartView;
  readonly isTableView = isTableView;
  readonly isPairedView = isPairedView;

  /**
   * The view on screen: the chosen one, or the Interactive table while a chart view is chosen and
   * nothing can be charted, or the Paired tests view is chosen and fewer than two entries are
   * comparable. The chosen one is kept, so a refetch that makes it available again returns to it.
   */
  get effectiveFigureTab(): FigureViewTab {
    return this.isFigureTabUnavailable(this.figureTab) ? 'table' : this.figureTab;
  }

  /** Why the Paired tests tab refuses, as the tab's visually hidden suffix. */
  readonly pairedUnavailableReason = 'unavailable: comparing needs two comparable models';

  /**
   * Whether the Paired tests view has been shown since step 2 was rendered. It stays mounted, and
   * hidden, after the first visit, so its mode, reference and result survive a trip to another view.
   */
  private pairedVisited = false;

  get pairedViewMounted(): boolean {
    if (this.effectiveFigureTab === 'paired') {
      this.pairedVisited = true;
    }
    return this.pairedVisited;
  }

  /** The Single chart or the Table preview, the two views that share the zoom-and-pan stage. */
  get stageViewShown(): boolean {
    const tab = this.effectiveFigureTab;
    return tab === 'single' || tab === 'tablePreview';
  }

  /**
   * A chart view's tab refuses while nothing can be charted, and the Paired tests tab while fewer
   * than two entries are comparable; each says why.
   */
  isFigureTabUnavailable(tab: FigureViewTab): boolean {
    if (isPairedView(tab)) {
      return this.measuredEntryCount < 2;
    }
    return isChartView(tab) && !this.showFigures;
  }

  sidebarCollapsed = this.storedSidebar.collapsed;

  readonly SIDEBAR_WIDTH_MIN = SIDEBAR_WIDTH_MIN;
  readonly SIDEBAR_WIDTH_DEFAULT = SIDEBAR_WIDTH_DEFAULT;

  /** The sidebar's width in CSS px, set by its resizer; the grid clamps it to half the workspace. */
  sidebarWidth = this.storedSidebar.sidebarWidth;

  @ViewChild('figWorkspace') figWorkspace?: ElementRef<HTMLElement>;

  /**
   * The widest the resizer goes: 40 rem, or half the workspace when that is narrower. Measured when
   * the handle is grabbed or focused, never inside a change-detection pass that renders from it.
   */
  sidebarWidthMax = SIDEBAR_WIDTH_MAX;

  measureSidebarWidthMax(): void {
    const workspace = this.figWorkspace?.nativeElement.clientWidth ?? 0;
    this.sidebarWidthMax = workspace > 0
      ? Math.max(SIDEBAR_WIDTH_MIN, Math.min(SIDEBAR_WIDTH_MAX, Math.floor(workspace / 2)))
      : SIDEBAR_WIDTH_MAX;
    this.cdr.markForCheck();
  }

  /** Live while dragging; the All grid and the Single stage refit through their own observers. */
  onSidebarWidthChange(width: number): void {
    this.sidebarWidth = width;
    this.cdr.markForCheck();
  }

  onSidebarWidthCommit(width: number): void {
    this.sidebarWidth = width;
    this.writeStoredSidebar();
    this.scheduleTableMeasure();
    this.cdr.markForCheck();
  }

  /** The chosen sidebar tab. `effectiveSidebarTab` is what the current view group shows of it. */
  sidebarTab: FigureSidebarTab = this.storedSidebar.tab;

  /** The tabs the current view group shows, in order. */
  get visibleSidebarTabs(): readonly FigureSidebarTabOption[] {
    const view = this.effectiveFigureTab;
    if (isPairedView(view)) {
      return PAIRED_VIEW_SIDEBAR_TABS;
    }
    return isTableView(view) ? TABLE_VIEW_SIDEBAR_TABS : CHART_VIEW_SIDEBAR_TABS;
  }

  /** The tab shown: Charts and Table swap for each other with the view group; the rest are in both sets. */
  get effectiveSidebarTab(): FigureSidebarTab {
    return sidebarTabForView(this.sidebarTab, this.effectiveFigureTab);
  }

  /** Whether the Download tab's Chart size section is open. Open by default. */
  figureSizeOpen = this.storedSidebar.figureSizeOpen;

  /** Whether the Download tab's Table image size section is open. Open by default. */
  tableImageSizeOpen = this.storedSidebar.tableImageSizeOpen;

  /** Whether the Download tab's Image format section is open. Open by default. */
  imageFormatOpen = this.storedSidebar.imageFormatOpen;

  onFigureSizeOpenChange(open: boolean): void {
    if (open !== this.figureSizeOpen) {
      this.figureSizeOpen = open;
      this.writeStoredSidebar();
    }
  }

  onTableImageSizeOpenChange(open: boolean): void {
    if (open !== this.tableImageSizeOpen) {
      this.tableImageSizeOpen = open;
      this.writeStoredSidebar();
      this.scheduleTableMeasure();
    }
  }

  /** Follows the native `toggle`, which fires for a click, a key and a bound `open` alike. */
  onImageFormatToggle(event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (open !== this.imageFormatOpen) {
      this.imageFormatOpen = open;
      this.writeStoredSidebar();
    }
  }

  /** Focus stays on the toggle, which sits outside the sidebar and is always rendered. */
  toggleSidebar(): void {
    this.sidebarCollapsed = !this.sidebarCollapsed;
    this.writeStoredSidebar();
    this.scheduleTableMeasure();
    this.cdr.markForCheck();
  }

  selectSidebarTab(tab: FigureSidebarTab): void {
    this.sidebarTab = tab;
    this.writeStoredSidebar();
    this.scheduleTableMeasure();
    this.cdr.markForCheck();
  }

  /** Left/Right move and wrap, Home/End jump to the ends, over the visible tabs; focus follows selection. */
  onSidebarTabKeydown(event: KeyboardEvent, index: number): void {
    const tabs = this.visibleSidebarTabs;
    const next = this.rovingTabIndex(event, index, tabs.length);
    if (next === null) {
      return;
    }
    const tab = tabs[next].id;
    this.selectSidebarTab(tab);
    this.cdr.detectChanges();
    document.getElementById(`mc-side-tab-${tab}`)?.focus();
  }

  /**
   * The Single chart's view, kept while the Table preview borrows the stage, so returning to Single
   * shows it as it was left. Leaving for any other view forgets it.
   */
  private singleViewMemo: { view: PreviewViewRequest; targetKey: string | null } | null = null;

  /**
   * Switches the view. Each view attaches once its panel exists, and detaches while its elements
   * still do: leaving a chart view for a table view drops every chart bitmap, and leaving the Table
   * preview drops its bitmap. The chart views refuse while nothing can be charted. Single opens on
   * the chart last activated on All, or the first; the Table preview opens at Fit to screen.
   *
   * The sidebar keeps a tab both view groups show; Charts and Table swap for each other. The Paired
   * tests view shows only Data and leaves the chosen tab for the next view.
   */
  selectFigureTab(tab: FigureViewTab): void {
    if (this.isFigureTabUnavailable(tab)) {
      return;
    }
    const current = this.effectiveFigureTab;
    if (tab === current) {
      if (tab !== this.figureTab) {
        this.figureTab = tab;
        this.writeStoredSidebar();
      }
      return;
    }

    if (current === 'all') {
      this.detachAll();
    }
    const memo = current === 'single' && tab === 'tablePreview'
      ? { view: this.previewView, targetKey: this.previewTargetKey }
      : null;
    if (current === 'single' || current === 'tablePreview') {
      this.detachPreview();
    }
    if (tab !== 'tablePreview' && tab !== 'single') {
      this.singleViewMemo = null;
    } else if (memo) {
      this.singleViewMemo = memo;
    }

    if (tab === 'single') {
      if (this.previewCard === null) {
        this.previewCardId = this.exportableCards[0]?.id ?? null;
      }
      const card = this.previewCard;
      if (card) {
        this.styleFamily = this.familyOf(card);
      }
      this.setHighlight(null);
    }

    if (!isPairedView(tab)) {
      this.sidebarTab = sidebarTabForView(this.sidebarTab, tab);
    }
    this.figureTab = tab;
    this.writeStoredSidebar();
    this.cdr.detectChanges();

    if (tab === 'all') {
      this.attachAll();
    } else if (tab === 'single') {
      const restored = this.singleViewMemo;
      this.singleViewMemo = null;
      this.attachPreview(restored?.view ?? 'default');
      if (restored) {
        this.previewTargetKey = restored.targetKey;
      }
    } else if (tab === 'tablePreview') {
      this.attachPreview('fitScreen');
    }
    this.scheduleTableMeasure();
  }

  /** Left/Right move and wrap, Home/End jump to the ends; focus follows selection, onto a refusing tab too. */
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
        sidebarWidth: this.sidebarWidth,
        tab: this.sidebarTab,
        view: this.figureTab,
        figureSizeOpen: this.figureSizeOpen,
        tableImageSizeOpen: this.tableImageSizeOpen,
        imageFormatOpen: this.imageFormatOpen
      }));
    } catch {
      // Private mode or blocked storage: the layout still applies for this session.
    }
  }

  // --- The Data tab's radio groups ---

  readonly speedMeasureOptions: readonly { readonly value: SpeedMeasure; readonly label: string }[] = [
    { value: 'meanModelTime', label: 'Model time per question, mean' },
    { value: 'totalModelTime', label: 'Candidate model time for the whole suite' },
    { value: 'ttftP50', label: 'Time to first token, P50' },
    { value: 'speedIndex', label: 'Speed Index (0-100)' }
  ];

  readonly sortKeyOptions: readonly { readonly value: ModelSortKey; readonly label: string }[] = [
    { value: 'intelligenceIndex', label: 'Intelligence Index' },
    { value: 'speed', label: 'Speed' },
    { value: 'cost', label: 'Cost' },
    { value: 'label', label: 'Name' },
    { value: 'custom', label: 'Custom' }
  ];

  readonly sortDirectionOptions: readonly { readonly value: SortDirection; readonly label: string }[] = [
    { value: 'desc', label: 'Descending' },
    { value: 'asc', label: 'Ascending' }
  ];

  /** The model order in words: `Intelligence Index, descending`, or `custom`. */
  get modelOrderDescription(): string {
    if (this.sort.key === 'custom') {
      return 'custom';
    }
    const key = this.sortKeyOptions.find(option => option.value === this.sort.key)?.label ?? this.sort.key;
    return `${key}, ${this.sort.direction === 'desc' ? 'descending' : 'ascending'}`;
  }

  /** The Custom list row's display name, glyph and badges, read off the payload. */
  customOrderEntry(key: string): BenchmarkModelComparisonEntryDto | null {
    return this.entriesByKey.get(key) ?? null;
  }

  // --- Style family ---

  /** Which family the Charts tab edits. Follows the figure on the Single tab. */
  styleFamily: ChartStyleFamily = 'bar';

  /** The families with a rendered card, in card order. */
  get styleFamilies(): { kind: ChartStyleFamily; label: string }[] {
    const families: { kind: ChartStyleFamily; label: string }[] = [{ kind: 'bar', label: 'Bar panels' }];
    if (this.profileCard) {
      families.push({ kind: 'profile', label: 'Profile' });
    }
    if (this.scatterCards.length > 0) {
      families.push({ kind: 'scatter', label: 'Trade-offs' });
    }
    return families;
  }

  /** The family the Charts tab renders: `bar` where the chosen one has no card in this set. */
  get effectiveStyleFamily(): ChartStyleFamily {
    return this.styleFamilies.some(family => family.kind === this.styleFamily) ? this.styleFamily : 'bar';
  }

  /** On the Single tab, the stage moves to the first figure of the chosen family. */
  selectStyleFamily(kind: ChartStyleFamily): void {
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
  // The Single chart and Table preview stage
  //
  // One image at a time, composed by the same pipeline the download uses and drawn onto a canvas
  // with zoom and pan: the size, the aspect ratio and the text size are chosen against the image
  // they produce rather than against a file already on disk. The stage shows one of two sources —
  // the chart picked on the Single tab, or the table image on the Table preview — with one set of
  // view handlers; each view opens with its own view state. An image the chosen box cannot hold is
  // refused here, in the same words the download would refuse it in. The `preview` names below are
  // the stage's.
  // ---------------------------------------------------------------------------------------------

  /** The stage the composed image is drawn onto. Present while the Single chart or the Table preview is shown. */
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

  /**
   * The stage is a `role="img"`, so it carries the card's own summary, or the table's size, rather
   * than a bare noun.
   */
  get previewAriaLabel(): string {
    if (this.figureTab === 'tablePreview') {
      const rows = this.entryTable.filteredCount(this.entries);
      return `Table preview: ${rows} ${rows === 1 ? 'entry' : 'entries'}, ` +
        `${this.shownColumns.length} ${this.shownColumns.length === 1 ? 'column' : 'columns'}`;
    }
    const card = this.previewCard;
    return card ? `Preview of ${card.ariaLabel}` : 'Figure preview';
  }

  /** The Table preview's written pixel size, `2960 × 1240 px`, from the last composition; empty while refused. */
  tablePreviewPixels = '';

  /**
   * Starts composing onto a stage that has just been rendered — after a tab change, or when step 2
   * opens on a remembered Single chart or Table preview, where no figure has been chosen yet. The
   * Single chart opens in the default view; the Table preview at Fit to screen.
   *
   * The toolbar's tooltip anchors render behind the panel's @if, which the polyfill's first scan
   * never saw.
   */
  private attachPreview(view: PreviewViewRequest = this.figureTab === 'tablePreview' ? 'fitScreen' : 'default'): void {
    if (this.previewCard === null) {
      this.previewCardId = this.exportableCards[0]?.id ?? null;
    }
    this.previewView = view;
    this.previewRefusal = '';
    this.previewActive = true;
    this.observeStage();
    refreshAnchorPositioning();
    this.schedulePreview();
  }

  /**
   * Stops composing and drops the composition. The stage is blanked rather than left holding the
   * last image: returning on another card would show the previous one until the first composition
   * landed, and the Table preview's bitmap is released with it.
   */
  private detachPreview(): void {
    this.previewActive = false;
    this.cancelScheduledPreview();
    this.disconnectStageObserver();
    this.previewSeq++;
    this.previewBusy = false;
    this.tablePreviewPixels = '';
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

  /** The Charts tab follows, so it always edits the figure on the stage. */
  selectPreviewCard(id: string): void {
    this.previewCardId = id;
    const card = this.previewCard;
    if (card) {
      this.styleFamily = this.familyOf(card);
    }
    this.schedulePreview();
    this.cdr.markForCheck();
  }

  /** Delegates, so the preview and *Download all charts* cannot drift apart in what they write. */
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
    if (this.figureTab === 'tablePreview') {
      await this.renderTablePreview();
      return;
    }
    const card = this.previewCard;
    if (!card || !this.previewCanvas) {
      return;
    }

    const sequence = ++this.previewSeq;
    this.previewBusy = true;
    this.previewRefusal = '';
    this.cdr.markForCheck();

    try {
      await this.prepareFigureComposition();
      if (sequence !== this.previewSeq) {
        return;
      }
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
   * The Table preview: the table image the download would write, at the table image size, in the
   * reading flavour, over the rows passing the filters in the current order.
   *
   * The target layout decides the pixel count and any refusal, as for a chart; the stage then gets
   * the same image drawn at the display-resolution raster its box affords, under
   * `PREVIEW_MAX_RASTER_PIXELS`, so a long table at 4K never allocates a 4K bitmap for a preview.
   */
  private async renderTablePreview(): Promise<void> {
    if (!this.previewCanvas) {
      return;
    }
    const sequence = ++this.previewSeq;
    this.previewBusy = true;
    this.previewRefusal = '';
    this.cdr.markForCheck();

    try {
      await this.prepareFigureComposition();
      if (sequence !== this.previewSeq) {
        return;
      }
      const model = this.tableModel('reading');
      const options = this.tableImageOptions();
      const sizeError = sizeErrors(this.tableImageSize, 'image').any;
      const target = sizeError === ''
        ? resolveTableImageLayout(model, options)
        : { layout: null, refusal: sizeError };
      if (!target.layout) {
        this.previewRefusal = target.refusal ?? '';
        this.tablePreviewPixels = '';
        this.blankPreview();
        return;
      }
      const table = target.layout;
      this.tablePreviewPixels = `${table.pixelWidth} × ${table.pixelHeight} px`;

      const targetKey = `${table.pixelWidth}x${table.pixelHeight}`;
      if (targetKey !== this.previewTargetKey) {
        this.previewTargetKey = targetKey;
        if (typeof this.previewView === 'number') {
          this.previewView = 'fitScreen';
        }
      }

      // The stage fitter reads only the box and the bitmap; a table has no plot box.
      const asFigure: FigureExportLayout = {
        layoutWidth: table.layoutWidth,
        layoutHeight: table.layoutHeight,
        plotWidth: 0,
        plotHeight: 0,
        density: table.scale,
        pixelWidth: table.pixelWidth,
        pixelHeight: table.pixelHeight
      };
      const stage = this.measureStage();
      const fit = stage ? previewLayoutFor(asFigure, stage, this.previewView) : null;
      if (!fit) {
        this.blankPreview();
        return;
      }

      const composed = composeTableImage(model, { ...options, previewRaster: fit.layout.pixelWidth / table.layoutWidth });
      if (sequence !== this.previewSeq) {
        return;
      }
      const centre = this.captureViewCentre();
      this.previewScreenFitZoom = fit.screenFitZoom;
      this.previewTargetPixels = { width: table.pixelWidth, height: table.pixelHeight };
      this.previewDpr = Math.min(4, Math.max(1, stage!.devicePixelRatio));
      this.previewPaintedRasterZoom = fit.rasterZoom;
      this.previewRasterCapped = fit.rasterCapped;
      this.paintPreview(composed);
      this.applyPreviewViewSize();
      this.restoreViewCentre(centre);
      const wanted = previewRasterZoom(this.previewZoomValue, table.pixelWidth, table.pixelHeight).zoom;
      if (Math.abs(wanted - fit.rasterZoom) > fit.rasterZoom * 1e-9) {
        this.schedulePreview();
      }
    } catch {
      this.previewRefusal = 'The table image could not be composed at that size.';
      this.tablePreviewPixels = '';
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
   * scrollbar appearing on zoom cannot change the fit and feed back into the observer. The viewport
   * fills the stage's padding box, so the stage's borders and the viewport's padding are both
   * subtracted from the stage's border box: that, in whole CSS px, is what the canvas has to fit
   * inside. `window.devicePixelRatio` is read here on every composition, so a window dragged to
   * another display re-rasterises instead of softening.
   */
  measureStage(): PreviewStage | null {
    const element = this.previewStage?.nativeElement;
    if (!element) {
      return null;
    }
    const box = element.getBoundingClientRect();
    const border = getComputedStyle(element);
    const padding = getComputedStyle(this.previewViewport?.nativeElement ?? element);
    const px = (value: string) => {
      const parsed = parseFloat(value || '0');
      return Number.isFinite(parsed) ? parsed : 0;
    };
    const content = previewStageContentBox(box.width, box.height, {
      left: px(border.borderLeftWidth) + px(padding.paddingLeft),
      right: px(border.borderRightWidth) + px(padding.paddingRight),
      top: px(border.borderTopWidth) + px(padding.paddingTop),
      bottom: px(border.borderBottomWidth) + px(padding.paddingBottom)
    });
    return { ...content, devicePixelRatio: window.devicePixelRatio || 1 };
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
    await this.prepareFigureComposition();
    if (generation !== this.allGeneration || !this.allActive) {
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
  // The table: its cells, its columns, its order, and its export and copy
  //
  // One column configuration decides the Interactive table's columns, the Table preview's and every
  // download's and copy's. Every write covers every entry passing the current filters, in the
  // current order, across all pages — never the visible page, which is why this reads `viewAll`
  // rather than `view`. The toast says so in as many words: a file holding ten of forty rows, with
  // nothing on it to say which ten, is worse than no file.
  // ---------------------------------------------------------------------------------------------

  // --- Cells and sorts ---

  /** Each sortable display column's sort: the raw value of its primary part, and State by its rank. */
  private displaySortAccessors(): Record<string, SortAccessor<BenchmarkModelComparisonEntryDto>> {
    const accessors: Record<string, SortAccessor<BenchmarkModelComparisonEntryDto>> = {};
    for (const column of TABLE_DISPLAY_COLUMNS) {
      const part = column.sortPart;
      if (part === null) {
        continue;
      }
      accessors[column.key] = column.key === 'stateCol'
        ? e => this.stateOrder(e)
        : e => sortKeyOf(this.tableCell(e, part)?.raw ?? null);
    }
    return accessors;
  }

  /** One entry's cell for one part, from the cells built once per comparison. */
  tableCell(entry: BenchmarkModelComparisonEntryDto, part: string): ComparisonTableCell | undefined {
    return this.tableCellsByKey.get(entry.key)?.[part];
  }

  /** One part's text as the table prints it, for the single-value columns. */
  cellText(entry: BenchmarkModelComparisonEntryDto, part: string): string {
    return this.tableCell(entry, part)?.text ?? '—';
  }

  /**
   * Whether a part is shown as a column of its own. A combined cell leaves such a part out, so no
   * value is printed twice; with the default columns nothing is left out.
   */
  isColumnShown(key: string): boolean {
    return this.shownColumnKeys.has(key);
  }

  /** Builds every entry's cells once per payload; a rebuild over the same payload keeps them. */
  private refreshTableCells(): void {
    if (this.entries === this.cellsSource) {
      return;
    }
    this.cellsSource = this.entries;
    this.tableCellsByKey = new Map(this.entries.map(entry => [entry.key, comparisonTableCells(entry)] as const));
    this.entriesByKey = new Map(this.entries.map(entry => [entry.key, entry] as const));
  }

  /**
   * The Table tab's `empty` set: display columns with no value on any row passing the filters.
   * Recomputed only when the filters or the entries change, never from the template.
   */
  private refreshColumnEmpty(force = false): void {
    const signature = JSON.stringify(this.entryTable.filters);
    if (!force && signature === this.columnEmptySignature) {
      return;
    }
    this.columnEmptySignature = signature;
    const rows = this.entryTable.viewAll(this.entries)
      .map(entry => this.tableCellsByKey.get(entry.key) ?? comparisonTableCells(entry));
    const populated = new Set(populatedColumnKeys(rows));
    this.columnEmptyKeys = new Set(TABLE_DISPLAY_COLUMNS.map(column => column.key).filter(key => !populated.has(key)));
  }

  // --- The order of the rows ---

  /** The column header the table is sorted by, or null while it follows the model order. */
  get tableSortedBy(): TableDisplayColumn | null {
    const column = this.entryTable.sortColumn;
    return column === MODEL_ORDER_SORT ? null : tableDisplayColumn(column) ?? null;
  }

  /** *Use model order*, above the table and in the Data tab. */
  useModelOrder(): void {
    this.resetTableToModelOrder();
    this.onTableChanged();
  }

  /** Returns the rows to the model order, on page 1. Every model-order change does this too. */
  private resetTableToModelOrder(): void {
    this.entryTable.sortColumn = MODEL_ORDER_SORT;
    this.entryTable.sortDirection = 'asc';
    this.entryTable.page = 1;
  }

  /** The order a write follows, as the toast names it. */
  get tableOrderPhrase(): string {
    const sorted = this.tableSortedBy;
    return sorted
      ? `current order (sorted by ${sorted.header})`
      : `current order (model order: ${this.modelOrderDescription})`;
  }

  // --- Columns ---

  /**
   * The Table tab's Columns section: stores the configuration, clears the filter of a column just
   * hidden, returns a sort on a column just hidden to the model order, and re-composes the Table
   * preview and the *Fit the table* information.
   */
  onTableColumnsChange(config: TableColumnConfig): void {
    const next = normalizeTableColumnConfig(config);
    const nowShown = new Set(next.shown);
    const hidden = this.tableColumns.shown.filter(key => !nowShown.has(key));
    this.setTableColumns(next);

    const cleared: string[] = [];
    for (const key of hidden) {
      const filter = TABLE_COLUMN_FILTERS[key];
      if (filter !== undefined && (this.entryTable.filters[filter] ?? '') !== '') {
        this.entryTable.setFilter(filter, '');
        cleared.push(tableDisplayColumn(key)?.header ?? key);
      }
      if (this.entryTable.sortColumn === key) {
        this.resetTableToModelOrder();
      }
    }
    this.tableColumnsStatus = cleared.length === 0
      ? ''
      : cleared.length === 1
        ? `The ${cleared[0]} filter was cleared because its column is hidden.`
        : `The ${cleared.join(' and ')} filters were cleared because their columns are hidden.`;

    this.refreshColumnEmpty();
    this.lastTableViewSignature = this.tableViewSignature();
    this.scheduleTableOutputs();
    this.cdr.markForCheck();
  }

  /** Replaces the configuration, stores it, and rebuilds the memoised column list and caption. */
  private setTableColumns(config: TableColumnConfig): void {
    this.tableColumns = config;
    this.shownColumns = shownTableColumns(config);
    this.shownColumnKeys = new Set(this.shownColumns.map(column => column.key));
    this.tableCaption = this.captionFor(this.shownColumns);
    try {
      localStorage.setItem(TABLE_COLUMNS_STORAGE_KEY, JSON.stringify({ version: TABLE_COLUMN_CONFIG_VERSION, order: config.order, shown: config.shown }));
    } catch {
      // Private mode or blocked storage: the columns still apply for this session.
    }
  }

  /** The table's `<caption>`: the shown columns, named in order. */
  private captionFor(columns: readonly TableDisplayColumn[]): string {
    return `Cross-model comparison entries, one row per entry: ${columns.map(column => column.header).join(', ')}.`;
  }

  // --- Formats ---

  /** The Download tab's Table format; remembered per browser. */
  tableFormat: TableFileFormat = this.storedDownload.tableFormat;

  /** Seven formats, so a select; each with the one line the Download tab says about it. */
  readonly tableFormatOptions: readonly { readonly value: TableFileFormat; readonly label: string; readonly description: string }[] = [
    { value: 'xlsx', label: 'Excel (.xlsx)', description: 'Numbers stay numbers; a second sheet holds the provenance.' },
    { value: 'csv', label: 'CSV', description: 'One typed column per value, comma-separated, for any spreadsheet or script.' },
    { value: 'tsv', label: 'TSV', description: 'One typed column per value, tab-separated, for pasting into a spreadsheet.' },
    { value: 'md', label: 'Markdown', description: 'The table as on screen, as a Markdown table for a document or a chat message.' },
    { value: 'json', label: 'JSON', description: 'One typed value per field, with the provenance, for scripts and notebooks.' },
    { value: 'html', label: 'HTML', description: 'The table as on screen, as a web page carrying its provenance.' },
    { value: 'image', label: 'Image (PNG or WebP)', description: 'A picture of the table in the Theme tab\'s look, at the Table image size below.' }
  ];

  onTableFormatChange(value: TableFileFormat): void {
    this.tableFormat = value;
    this.writeStoredDownload();
    this.scheduleTableMeasure();
    this.cdr.markForCheck();
  }

  /** The chosen format's one line. */
  get tableFormatDescription(): string {
    return this.tableFormatOptions.find(option => option.value === this.tableFormat)?.description ?? '';
  }

  /**
   * Reading formats (Markdown, HTML, the image) keep a combined column combined, as on screen; data
   * formats (Excel, CSV, TSV, JSON) write its parts as typed columns.
   */
  tableFlavour(format: TableFileFormat = this.tableFormat): TableModelFlavour {
    return format === 'md' || format === 'html' || format === 'image' ? 'reading' : 'data';
  }

  /** What the chosen format is called in the download's name: the image is named by the image format. */
  get tableFormatName(): string {
    if (this.tableFormat === 'image') {
      return this.exportFormat === 'webp' ? 'WebP' : 'PNG';
    }
    return this.tableFormatOptions.find(option => option.value === this.tableFormat)?.label ?? this.tableFormat;
  }

  /** Download table's accessible name. */
  get downloadTableName(): string {
    return `Download the table as ${this.tableFormatName}`;
  }

  /** Copy table's accessible name, which says what it writes: the clipboard takes what the format can paste as. */
  get copyTableName(): string {
    switch (this.tableFormat) {
      case 'image':
        return this.exportFormat === 'webp'
          ? 'Copy the table as an image (copied as PNG)'
          : 'Copy the table as an image';
      case 'xlsx':
        return 'Copy the table as cells for Excel';
      case 'html':
        return 'Copy the table as a formatted table';
      case 'md':
        return 'Copy the table as Markdown';
      default:
        return `Copy the table as ${this.tableFormat.toUpperCase()}`;
    }
  }

  /** The Download tab shows the table image's size and format for the Image format, and whenever the Table preview is shown. */
  get showTableImageSettings(): boolean {
    return this.tableFormat === 'image' || this.effectiveFigureTab === 'tablePreview';
  }

  /** What the chosen format will write, as the Download tab's status line names it. */
  get tableScopeLine(): string {
    const rows = this.entryTable.filteredCount(this.entries);
    const columns = this.shownColumns.length;
    const scope = `${rows} ${rows === 1 ? 'entry' : 'entries'} (filters applied, current order, all pages) · ` +
      `${columns} ${columns === 1 ? 'column' : 'columns'}`;
    if (this.tableFlavour() === 'reading') {
      return scope;
    }
    const written = new Set(this.shownColumns.flatMap(column => column.parts)).size;
    return written === columns ? scope : `${scope}, written as ${written}`;
  }

  // --- The table image's size, refusal and *Fit the table* information ---

  /** The table image's written size, `2960 × 1240 px`, from the last measurement; empty while refused. */
  tableImageWritten = '';

  /** Why the table image cannot be written at its size, from the last measurement. Empty while it fits. */
  tableImageRefusal = '';

  /**
   * The part of `tableImageRefusal` the layout gives — too few pixels for the columns or the rows —
   * rather than an out-of-range field, which the size section names beside the field itself.
   */
  private tableImageLayoutRefusal = '';

  /** The Download tab's closing alert: the table image's layout refusal, while Image is the format. */
  get tableImageLayoutAlert(): string {
    return this.tableFormat === 'image' ? this.tableImageLayoutRefusal : '';
  }

  /** *Fit the table* in custom mode: the size that holds the whole table at this text size. */
  tableFitInfo = '';

  private tableMeasureTimer: ReturnType<typeof setTimeout> | null = null;

  /** Bumped by every measurement, so a slow one does not overwrite a newer one. */
  private tableMeasureSeq = 0;

  /** How long the measurement waits for typing, ticking or dragging to pause. */
  private readonly tableMeasureQuietMs = 120;

  /** Whether the Download tab's Table image size section is on screen and open. */
  private get tableImageSectionShown(): boolean {
    return !this.sidebarCollapsed
      && this.effectiveSidebarTab === 'download'
      && this.showTableImageSettings
      && this.tableImageSizeOpen;
  }

  /** The Table image size section's written-size line in fit mode, where the size depends on the table. */
  get tableImageWrittenLabel(): string {
    return this.tableImageSize.resolutionId === FIT_RESOLUTION_ID && this.tableImageWritten !== ''
      ? `${this.tableImageWritten} — the whole table at ${densityPercentLabel(resolveSizeDensity(this.tableImageSize))}`
      : '';
  }

  /**
   * Re-measures the table image 120 ms after the last change that affects it: the columns, the rows
   * passing the filters, the size, the font, the weights and the row style. Only while a table view
   * is shown, where the buttons, the Table preview and the size section read the result.
   */
  private scheduleTableMeasure(): void {
    this.cancelScheduledTableMeasure();
    if (this.step !== 2 || !isTableView(this.effectiveFigureTab)) {
      return;
    }
    this.tableMeasureTimer = setTimeout(() => {
      this.tableMeasureTimer = null;
      void this.measureTableImageNow();
    }, this.tableMeasureQuietMs);
  }

  private cancelScheduledTableMeasure(): void {
    if (this.tableMeasureTimer !== null) {
      clearTimeout(this.tableMeasureTimer);
      this.tableMeasureTimer = null;
    }
  }

  /**
   * The measurement itself, memoised in three fields: the refusal and the written size whenever the
   * image is what the table views write or show, and the *Fit the table* information only while the
   * size section is shown in custom mode. Public so a spec can run it without the timer.
   */
  async measureTableImageNow(): Promise<void> {
    const sequence = ++this.tableMeasureSeq;
    if (!this.showTableImageSettings || this.entries.length === 0) {
      this.tableImageWritten = '';
      this.tableImageRefusal = '';
      this.tableImageLayoutRefusal = '';
      this.tableFitInfo = '';
      this.cdr.markForCheck();
      return;
    }
    await this.prepareFigureComposition();
    if (sequence !== this.tableMeasureSeq) {
      return;
    }
    const model = this.tableModel('reading');
    const sizeError = sizeErrors(this.tableImageSize, 'image').any;
    const target = sizeError === '' ? resolveTableImageLayout(model, this.tableImageOptions()) : null;
    this.tableImageLayoutRefusal = target?.refusal ?? '';
    this.tableImageRefusal = sizeError || this.tableImageLayoutRefusal;
    this.tableImageWritten = target?.layout ? `${target.layout.pixelWidth} × ${target.layout.pixelHeight} px` : '';

    if (this.tableImageSize.resolutionId === 'custom' && this.tableImageSectionShown) {
      const fit = measureTableImage(model, {
        theme: this.figureTheme,
        tableStyle: this.figureStyle.table,
        textScale: this.tableImageSize.textScalePercent / 100,
        logo: this.figureLogo()
      });
      const rows = model.rows.length;
      const columns = model.columns.length;
      this.tableFitInfo = `Fit the table: ${fit.widthPx} × ${fit.heightPx} px at this text size — the whole ` +
        `table with its ${columns} shown ${columns === 1 ? 'column' : 'columns'} and ${rows} ` +
        `${rows === 1 ? 'row' : 'rows'}.`;
    } else {
      this.tableFitInfo = '';
    }
    this.cdr.markForCheck();
  }

  /** The table image refuses the chosen size, which disables Download table and Copy table while Image is chosen. */
  get tableWriteRefusal(): string {
    return this.tableFormat === 'image' ? this.tableImageRefusal : '';
  }

  /** Nothing to write from an empty comparison, never two writes at once, and nothing at a refused size. */
  get canExportTable(): boolean {
    return this.entries.length > 0 && !this.exporting && this.tableWriteRefusal === '';
  }

  /** Download table's tooltip: its name, or why it will not. */
  get downloadTableTooltip(): string {
    return this.tableActionTooltip(this.downloadTableName);
  }

  /** Copy table's tooltip: its name, or why it will not. */
  get copyTableTooltip(): string {
    return this.tableActionTooltip(this.copyTableName);
  }

  private tableActionTooltip(name: string): string {
    if (this.exporting) {
      return 'An export is running.';
    }
    if (this.entries.length === 0) {
      return 'Nothing to export: the comparison has no entries.';
    }
    return this.tableWriteRefusal || name;
  }

  /** The rows every write takes: filters applied, current order, all pages. */
  private tableRows(): BenchmarkModelComparisonEntryDto[] {
    return this.entryTable.viewAll(this.entries);
  }

  /** The table in one flavour, over the given rows, in the column configuration and with the cells built once. */
  private tableModel(
    flavour: TableModelFlavour,
    rows: readonly BenchmarkModelComparisonEntryDto[] = this.tableRows()
  ): ComparisonTableModel {
    const model = buildComparisonTableModel(rows, this.tableProvenance, this.tableColumns, flavour, this.tableCellsByKey);
    const diagnostics = this.panelDiagnostics;
    return diagnostics ? { ...model, panelDiagnostics: diagnostics } : model;
  }

  /** The table image's theme, row style, logo and size: *Fit the table* at a density, or a box in plain pixels. */
  private tableImageOptions(): TableImageOptions {
    const settings = this.tableImageSize;
    const density = resolveSizeDensity(settings);
    let size: TableImageSize;
    if (settings.resolutionId === FIT_RESOLUTION_ID) {
      size = { mode: 'fit', density };
    } else {
      const resolution = resolveSizeResolution(settings);
      size = {
        mode: 'box',
        widthPx: resolution.widthPx,
        heightPx: resolution.heightPx,
        density,
        textScale: settings.textScalePercent / 100
      };
    }
    return { theme: this.figureTheme, tableStyle: this.figureStyle.table, size, logo: this.figureLogo() };
  }

  // --- Download and copy ---

  /**
   * The one write path: the rows passing the filters, in the current order, across all pages, in
   * the chosen format's flavour and the column configuration. The image is written in the shared
   * image format at the table image size, or refused in the words the Table preview uses.
   */
  async downloadTable(): Promise<void> {
    if (!this.canExportTable) {
      return;
    }
    const rows = this.tableRows();
    const format = this.tableFormat;
    const flavour = this.tableFlavour(format);
    this.exporting = true;
    this.exportNotice = null;
    this.cdr.markForCheck();

    try {
      const model = this.tableModel(flavour, rows);
      let pixels = '';
      let encoded: TableExportResult;
      if (format === 'image') {
        await this.prepareFigureComposition();
        const options = this.tableImageOptions();
        const sizeError = sizeErrors(this.tableImageSize, 'image').any;
        const target = sizeError === '' ? resolveTableImageLayout(model, options) : { layout: null, refusal: sizeError };
        if (!target.layout) {
          this.announce(target.refusal ?? 'The table image could not be written at this size.', 'error');
          return;
        }
        pixels = ` at ${target.layout.pixelWidth} × ${target.layout.pixelHeight} px`;
        encoded = await encodeComparisonTable(model, this.exportFormat, { webpQuality: this.webpQuality, ...options });
      } else {
        encoded = await encodeComparisonTable(model, format);
      }
      const filename = tableExportFilename(encoded.format);
      saveFigureBlob(encoded.blob, filename);

      const noun = rows.length === 1 ? 'entry' : 'entries';
      const shown = this.shownColumns.length;
      const columns = flavour === 'reading'
        ? `${shown} ${shown === 1 ? 'column' : 'columns'}`
        : `${shown} ${shown === 1 ? 'column' : 'columns'}, written as ${model.columns.length}`;
      const fallback = encoded.fellBackToPng
        ? ' This browser cannot encode WebP, so the file was written as PNG.'
        : '';
      this.announce(
        `Table saved as ${filename} — ${rows.length} ${noun}, ${this.tableOrderPhrase}, filters applied, ` +
        `all pages, ${columns}${pixels}.${fallback}`,
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
   * Copies the same rows in what the chosen format pastes as: a PNG for the image (WebP included,
   * because every engine that implements `ClipboardItem` rejects `image/webp` in one), cells for
   * Excel, a formatted table for HTML, and the text of Markdown, CSV, TSV and JSON.
   *
   * A text payload falls back to `writeText` where `ClipboardItem` is missing; an image cannot. Every
   * refusal and every missing API lands as inline text — a bare console error tells the reader
   * nothing.
   */
  async copyTable(): Promise<void> {
    if (!this.canExportTable) {
      return;
    }
    const rows = this.tableRows();
    const format = this.tableFormat;
    this.exporting = true;
    this.exportNotice = null;
    this.cdr.markForCheck();

    const noun = rows.length === 1 ? 'entry' : 'entries';
    const scope = `${this.tableOrderPhrase}, filters applied, all pages`;
    try {
      const model = this.tableModel(this.tableFlavour(format), rows);
      if (format === 'image') {
        await this.prepareFigureComposition();
        const options = this.tableImageOptions();
        const sizeError = sizeErrors(this.tableImageSize, 'image').any;
        const target = sizeError === '' ? resolveTableImageLayout(model, options) : { layout: null, refusal: sizeError };
        if (!target.layout) {
          this.announce(target.refusal ?? 'The table image could not be copied at this size.', 'error');
          return;
        }
        const encoded = await encodeComparisonTable(model, 'png', options);
        const outcome = await copyImageToClipboard(encoded.blob);
        if (outcome === 'copied') {
          this.announce(
            `Copied ${rows.length} ${noun} as an image (PNG, ${target.layout.pixelWidth} × ` +
            `${target.layout.pixelHeight} px) — ${scope}.`,
            'success'
          );
        } else if (outcome === 'unsupported') {
          this.announce('This browser cannot copy images — download the table instead.', 'error');
        } else {
          this.announce('The clipboard write was refused.', 'error');
        }
        return;
      }

      const payload = tableClipboardPayload(model, format);
      const clipboard = navigator.clipboard as Clipboard | undefined;
      const canWriteItems = !!clipboard && typeof clipboard.write === 'function' && typeof ClipboardItem !== 'undefined';
      if (canWriteItems) {
        const parts: Record<string, Blob> = { 'text/plain': new Blob([payload.text], { type: 'text/plain' }) };
        if (payload.html !== undefined) {
          parts['text/html'] = new Blob([payload.html], { type: 'text/html' });
        }
        await clipboard!.write([new ClipboardItem(parts)]);
      } else if (clipboard && typeof clipboard.writeText === 'function') {
        await clipboard.writeText(payload.text);
      } else {
        this.announce('This browser cannot copy text to the clipboard — download the table instead.', 'error');
        return;
      }
      const what = format === 'xlsx'
        ? 'cells for Excel'
        : format === 'html'
          ? 'a formatted table'
          : format === 'md' ? 'Markdown' : format.toUpperCase();
      this.announce(`Copied ${rows.length} ${noun} as ${what} — ${scope}.`, 'success');
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
   * Set-level notices: the eight-entry cap, the exclusions, whatever the entry selection is
   * withholding, and the two intervals this payload cannot supply.
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
   * exclusions are warnings, deselection is informational, and the plotted
   * entries' question coverage closes the list (see {@link questionCoverageNotes}). Appended to every
   * card's own notes on export and on preview, and the first of {@link setNotices}.
   */
  get setFigureNotes(): FigureNote[] {
    const notes: FigureNote[] = (this.figures?.selection.notices ?? [])
      .map(text => ({ text, tone: 'warning' as const }));


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
  chartAriaLabel(spec: { chrome: FigureChrome; summary?: readonly string[] } | null | undefined): string {
    if (!spec) {
      return '';
    }
    const chrome = spec.chrome;
    const summary = (spec.summary ?? []).map(sentence => ` ${sentence}.`).join('');
    return `${chrome.title}: ${figureSummary(chrome)}.${summary} Values for every entry are in the comparison table below.`;
  }

  /** An entry key made safe for a DOM id or an anchor name: keys carry a `run:12` style colon. */
  domKey(key: string): string {
    return key.replace(/[^A-Za-z0-9_-]/g, '-');
  }

  /** A tooltip's DOM id and anchor name, derived from an entry key. */
  tipId(prefix: string, key: string): string {
    return `mc-tip-${prefix}-${this.domKey(key)}`;
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
  // Assessor panel diagnostics
  //
  // Rendered from the payload as the server computed them. The cell formats are the Markdown
  // block's, from `table-export`, for the same reason as the table's.
  // ---------------------------------------------------------------------------------------------

  /** The comparison's judge-family diagnostics, or null when no run in it is a panel run. */
  get panelDiagnostics(): BenchmarkPanelDiagnosticsDto | null {
    return this.comparison?.panelDiagnostics ?? null;
  }

  readonly panelMemberText = panelMemberText;
  readonly panelRoleText = panelRoleText;
  readonly formatPanelIndex = formatPanelIndexText;
  readonly formatFamilyGapCell = formatFamilyGapCellText;
  readonly familyGapPair = familyGapPairText;
  readonly formatRate = formatRateText;
  readonly formatOverturnGap = formatOverturnGapText;
  readonly familyRelation = familyRelationText;
  readonly panelPairsEmpty = panelPairsEmptyText;

  /** The family gap rows that carry either labeled contrast: the members' own provider pair. */
  get panelContrastGaps(): BenchmarkPanelFamilyGapDto[] {
    return (this.panelDiagnostics?.familyGaps ?? [])
      .filter(gap => !!gap.interactionContrastLabel || !!gap.asymmetryEstimateLabel);
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

  /** The payload `chartEntries` and `context` were derived from. */
  private chartSource: BenchmarkModelComparisonDto | null | undefined = undefined;

  /**
   * Rebuilds every figure from one entry set, one order, one glyph assignment and one theme, and
   * with them the model order the table and the Custom list read.
   *
   * The glyph source is the whole payload rather than the current slice, so deselecting a model
   * never reshapes the survivors; the hue is the provider's and never moves.
   */
  private rebuild(): void {
    this.entries = this.comparison?.entries ?? [];
    if (this.comparison !== this.chartSource) {
      this.chartSource = this.comparison;
      this.chartEntries = toChartEntries(this.comparison);
      this.context = toChartContext(this.comparison);
      // A total chosen on an earlier comparison would draw an empty cost panel on this one.
      if (this.costMeasure === 'totalRun' && !this.totalRunCostAvailable) {
        this.costMeasure = 'candidateSuite';
      }
    }
    this.refreshTableCells();
    this.refreshFigureTheme();

    // Excluded entries pass through so the selection counts and names them; the chart core drops
    // them before any figure is built.
    const input = this.chartEntries.filter(entry => entry.excluded || this.includedKeys.includes(entry.key));

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
      style: this.figureStyle,
      theme: this.figureTheme
    });

    this.refreshModelOrder();
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

    this.modelRows = this.buildModelRows();
    this.refreshChartsAvailable();
    this.refreshChartSettingsHash();
    this.refreshDocumentChartActions();

    // Every caller of this method changes what the template renders, and several of them are
    // outside change detection: a filter control, the reduced-motion listener, the resize
    // observer. Marking here is what makes the figures, the notices and the table agree.
    this.cdr.markForCheck();
    // No-ops unless the stage or the All tiles are composing, which follow every rebuild.
    this.schedulePreview();
    if (this.allActive) {
      this.markAllStale();
      this.scheduleAllCompose();
    }
  }

  /**
   * The model order over every entry, and the Custom list built from it. The rank is recomputed only
   * when the order, the measures or the entries changed; the list's tags and divider follow the
   * figures, so they are rebuilt with them while Custom is chosen.
   */
  private refreshModelOrder(): void {
    const inputs = this.modelOrderInputs;
    if (!inputs || inputs.sort !== this.sort || inputs.speedMeasure !== this.speedMeasure
        || inputs.costMeasure !== this.costMeasure || inputs.entries !== this.chartEntries) {
      this.modelOrderInputs = {
        sort: this.sort, speedMeasure: this.speedMeasure, costMeasure: this.costMeasure, entries: this.chartEntries
      };
      this.modelOrderKeyList = modelOrderKeys(this.chartEntries, this.sort, this.speedMeasure, this.costMeasure, this.context);
      this.modelOrderRank = new Map(this.modelOrderKeyList.map((key, index) => [key, index] as const));
      const reset = this.defaultModelOrderKeys(this.chartEntries);
      this.customOrderIsDefault = this.sort.key !== 'custom'
        || (reset.length === this.modelOrderKeyList.length && reset.every((key, index) => key === this.modelOrderKeyList[index]));
    }

    if (this.sort.key !== 'custom') {
      this.customOrderItems = [];
      this.customOrderDividerIndex = null;
      return;
    }
    const selection = this.figures?.selection;
    const plotted = new Set((selection?.plotted ?? []).map(entry => entry.key));
    const overflow = new Set((selection?.overflow ?? []).map(entry => entry.key));
    this.customOrderItems = this.modelOrderKeyList.map(key => ({
      key,
      label: this.entriesByKey.get(key)?.label ?? key,
      tags: plotted.has(key) || overflow.has(key) ? [] : ['table only']
    }));
    // The line sits under the last charted entry, and only where the cap left chartable entries out.
    let lastPlotted = -1;
    this.modelOrderKeyList.forEach((key, index) => {
      if (plotted.has(key)) {
        lastPlotted = index;
      }
    });
    this.customOrderDividerIndex = overflow.size > 0 && lastPlotted >= 0 ? lastPlotted + 1 : null;
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
