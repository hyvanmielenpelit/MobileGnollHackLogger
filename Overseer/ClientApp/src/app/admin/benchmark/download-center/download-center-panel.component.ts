import {
  ChangeDetectionStrategy,
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
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, Subscription, firstValueFrom, forkJoin, of, timer } from 'rxjs';
import { catchError, map, switchMap } from 'rxjs/operators';

import {
  AdminBenchmarkService,
  BenchmarkPdfPaper,
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportDocumentOrigin,
  BenchmarkReportPeerNaming,
  BenchmarkRunReportJobDto,
  BenchmarkRunReportJobPhase,
  reportDisclosureParam,
  reportPeerNamingParam
} from '../../../services/admin-benchmark.service';
import { downloadTextFile, safeFileName } from '../../../utils/download.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { TableState, exactFilter } from '../../../shared/data-table/table-state';
import { SortHeaderComponent } from '../../../shared/data-table/sort-header.component';
import { TablePagerComponent } from '../../../shared/data-table/table-pager.component';
import { PdfViewerDialogComponent } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { exportTimestamp, saveFigureBlob } from '../model-comparison/figure-export';
import {
  REPORT_LIBRARY_ALL_TAKE,
  REPORT_PACK_AUDIENCES,
  ReportDocumentLibraryScope,
  audienceLabel,
  formatUtc
} from '../report-pack/report-document-format';
import {
  ReportChartPublishProgress,
  ReportChartPublishResult,
  ReportChartSelection,
  ReportChartTarget,
  normalizeChartSelection,
  readStoredChartSelection
} from '../report-pack/report-charts';
import { ReportChartPickerComponent } from '../report-pack/report-chart-picker.component';
import {
  DOWNLOAD_CENTER_DISCLOSURE_TITLE,
  REPORT_DISCLOSURE_FORMATS_NOTE,
  REPORT_DISCLOSURE_GUIDES,
  REPORT_DISCLOSURE_NOTE_SHARED,
  reportDisclosureInfo
} from '../report-disclosure-guide';
import { markdownToPrintableHtml } from './printable-html';
import {
  ArchiveEntry,
  MANIFEST_FILE_NAME,
  ManifestFailure,
  ManifestFile,
  buildManifest,
  buildTextArchive,
  paperLabel,
  uniqueFileNames
} from './text-archive';

/** The run a run-context Download Center packages files of. */
export interface DownloadCenterRunInfo {
  id: number;
  suiteName: string;
  modelLabel: string;
  startedAtUtc: string;
  completedAtUtc: string | null;
}

/** Opened from a run report: the run's files and every pack document whose subject includes the run. */
export interface DownloadCenterRunContext {
  kind: 'run';
  run: DownloadCenterRunInfo;
  /** The run diagnostics text, captured when the download is prepared. */
  diagnosticsText: () => string;
}

/** Opened on chosen documents: those documents and the reports of their subjects' runs. */
export interface DownloadCenterDocumentsContext {
  kind: 'documents';
  documentIds: number[];
  /** The dialog title in place of *Downloads*. */
  title?: string;
  /** The subtitle in place of the document count line. */
  subtitle?: string;
}

/**
 * Every Report Pack document of a comparison, or of every comparison, by one list request, and the
 * reports of their subjects' runs.
 */
export interface DownloadCenterLibraryContext {
  kind: 'library';
  scope: ReportDocumentLibraryScope;
  /** `all`: every row starts as the package chooses it; `none`: nothing starts chosen. */
  preselect: 'all' | 'none';
  /** The dialog title in place of *Report documents*. */
  title?: string;
  /** The subtitle in place of the scope line. */
  subtitle?: string;
}

export type DownloadCenterContext = DownloadCenterRunContext | DownloadCenterDocumentsContext | DownloadCenterLibraryContext;

/**
 * What the host lends the panel so it can chart the documents of the comparison open beside it: the
 * Model Comparison wizard's step 4. Without it the panel shows no Charts column and no chart action.
 */
export interface DownloadCenterChartActions {
  /** The hash of the chart settings on step 2 now; null while it is being computed. */
  readonly currentSettingsHash: string | null;
  /** Step 2's pricing basis, as the document list spells it: `AsRun` or `Current`. */
  readonly pricingBasis: string;
  /** The figure keys the open comparison can draw. */
  readonly available: readonly string[];
  /** Why the chart settings would print badly, or null. */
  readonly advisory: string | null;
  /** Chart storage is known not to be configured on the server. */
  readonly storageMissing: boolean;
  /** Whether the document was written for the comparison open on step 2. */
  comparisonKeyMatches(doc: BenchmarkReportDocumentListItemDto): boolean;
  /** Composes and uploads the chosen figures of every target, one document at a time. */
  publish(
    targets: readonly ReportChartTarget[],
    selection: ReportChartSelection,
    onProgress?: (progress: ReportChartPublishProgress) => void
  ): Promise<ReportChartPublishResult>;
}

export type DownloadPackageId = 'internal' | 'provider' | 'custom';
export type DownloadFormat = 'pdf' | 'docx' | 'md' | 'html' | 'txt';
export type DownloadRowKind = 'pack' | 'runReport' | 'toolCallLog' | 'diagnostics';

/** What a remembered choice is keyed by: a pack document's audience, or a run file's kind. */
export type DownloadRowCategory =
  | 'executiveSummary'
  | 'technicalReport'
  | 'internalBrief'
  | 'runReport'
  | 'toolCallLog'
  | 'diagnostics';

/** One available document: a row of the Documents table. */
export interface DownloadRow {
  key: string;
  kind: DownloadRowKind;
  category: DownloadRowCategory;
  label: string;
  /** What identifies the row beyond its label: its document type and writer, or its suite and model. */
  detail: string;
  /** An explanation shown behind the row's info button, or null. */
  note: string | null;
  runId: number | null;
  doc: BenchmarkReportDocumentListItemDto | null;
  /** A subject run was re-scored, re-run or deleted since the document was written. */
  runChanged: boolean;
  /** The disclosures a pack document renders at; empty for run files. */
  allowedDisclosures: BenchmarkReportDisclosure[];
  /** The formats this row can be downloaded in. */
  formats: readonly DownloadFormat[];
  /** Why the row is internal only whatever is chosen, or null for a shareable pack document. */
  internalReason: string | null;
  /** The model or group the row is about, for the Subject column and its filter. */
  subject: string;
  /** The suite, for the Subject column and the Suite filter; empty when unknown. */
  suite: string;
  /** `Executive Summary`, `Run report`…: the Document filter's value. */
  documentType: string;
  /** When the document was written or the run finished; null when unknown. */
  createdAtUtc: string | null;
}

export interface DownloadRowState {
  selected: boolean;
  disclosure: BenchmarkReportDisclosure;
  naming: BenchmarkReportPeerNaming;
  formats: DownloadFormat[];
}

export interface DownloadPackage {
  id: DownloadPackageId;
  name: string;
  /** The name in the summary line, the manifest and the ZIP file name. */
  fullName: string;
  /** One line under the name on the package card. */
  tagline: string;
  description: string;
}

/** A failure the panel lists after a download or a chart update. */
export interface DownloadFailure {
  label: string;
  reason: string;
}

/** A download in preparation: finished steps of `total` (every file, then the ZIP or the save) and the current one. */
export interface DownloadProgress {
  done: number;
  total: number;
  step: string;
}

/** The text a row's text formats are made from; `capturedAt` is set for the diagnostics. */
interface SourceText {
  text: string;
  fileName: string | null;
  capturedAt: Date | null;
}

/** A PDF or Word document the server rendered, with the name it gave it. */
interface SourceBinary {
  bytes: Uint8Array;
  fileName: string | null;
  capturedAt: Date | null;
}

/** A finished file: a PDF's or Word document's bytes, or the text of every other format. */
type ProducedFile = { name: string; mime: string; mtime: Date; manifest: ManifestFile }
  & ({ text: string } | { bytes: Uint8Array });

/** Where finished files go. A holder, so a spec can stand fakes in for the browser download path. */
export const downloadCenterIo = {
  saveText: (fileName: string, text: string, mimeType: string): void => downloadTextFile(fileName, text, mimeType),
  saveBytes: (fileName: string, bytes: Uint8Array, mimeType: string): void =>
    saveFigureBlob(new Blob([bytes as unknown as BlobPart], { type: mimeType }), fileName),
  saveBlob: (blob: Blob, fileName: string): void => saveFigureBlob(blob, fileName),
  now: (): Date => new Date()
};

export const DOWNLOAD_CENTER_STORAGE_KEY = 'overseer.benchmark.downloadCenter';

/**
 * The stored settings' version. Version 2 is migrated on reading, without the Internal package's
 * remembered formats; settings of any other version read as absent.
 */
export const STORED_SETTINGS_VERSION = 3;

/** One remembered row choice; every field is checked against the row before it is applied. */
interface StoredChoice {
  selected?: unknown;
  disclosure?: unknown;
  naming?: unknown;
  formats?: unknown;
}

interface StoredSettings {
  version: typeof STORED_SETTINGS_VERSION;
  package?: DownloadPackageId;
  paper?: BenchmarkPdfPaper;
  packages?:Partial<Record<DownloadPackageId, Partial<Record<DownloadRowCategory, StoredChoice>>>>;
}

export const DOWNLOAD_PACKAGES: readonly DownloadPackage[] = [
  {
    id: 'internal',
    name: 'Internal',
    fullName: 'Internal package',
    tagline: 'Every file, for the Overseer team',
    description: 'Every available file for the Overseer team, as PDF, Word and Markdown (PDF, Word and Text for the diagnostics): report documents at Full disclosure with peers named, the run report, the tool-call log and the diagnostics.'
  },
  {
    id: 'provider',
    name: 'External',
    fullName: 'External package',
    tagline: 'Shareable reports for a model’s provider',
    description: 'The Executive Summary and the Report for AI Researchers and Developers as PDF, to send to a model’s provider: Summary disclosure (Detailed optional), peers anonymized (named optional). Internal-only files cannot be chosen.'
  },
  {
    id: 'custom',
    name: 'Custom',
    fullName: 'Custom',
    tagline: 'Your own selection',
    description: 'Any selection, at any level a document allows.'
  }
];

export const INTERNAL_REASONS = {
  internalBrief: 'Internal only: contains rubric text',
  fullDisclosure: 'Internal only at Full: contains rubric text',
  runReport: 'Internal only: contains questions, rubrics and answers',
  toolCallLog: 'Internal only: contains every tool call’s arguments and results',
  diagnostics: 'Internal only: contains the run’s configuration and internal log'
} as const;

/** Why a chosen document gets no charts, word for word as the panel lists it. */
export const CHART_SKIP_REASONS = {
  noPeers: 'It compares the model with no other model, so it has no comparison charts.',
  otherComparison: 'It was written for a different comparison than the one open on step 2.'
} as const;

const MIME_TYPES: Record<DownloadFormat, string> = {
  pdf: 'application/pdf',
  docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  md: 'text/markdown;charset=utf-8',
  html: 'text/html;charset=utf-8',
  txt: 'text/plain;charset=utf-8'
};

const FORMAT_LABELS: Record<DownloadFormat, string> = { pdf: 'PDF', docx: 'Word', md: 'Markdown', html: 'HTML', txt: 'Text' };

/**
 * The formats a package chooses where a row offers them: External the PDF alone; Internal, and Custom
 * with nothing to keep, the PDF, the Word document and the text they are made from (Markdown, or Text
 * for the diagnostics).
 */
const PRESET_FORMATS: Record<'internal' | 'provider', readonly DownloadFormat[]> = {
  internal: ['pdf', 'docx', 'md', 'txt'],
  provider: ['pdf']
};

export const PDF_PAPERS: readonly { id: BenchmarkPdfPaper; label: string }[] = [
  { id: 'a4', label: paperLabel('a4') },
  { id: 'letter', label: paperLabel('letter') }
];

const ALL_DISCLOSURES = [BenchmarkReportDisclosure.Summary, BenchmarkReportDisclosure.Detailed, BenchmarkReportDisclosure.Full];

/** The interval of the run's report writing job poll while its reports are being written. */
export const DOWNLOAD_CENTER_REPORT_JOB_POLL_MS = 5000;

/** A report writing job phase inside the notice's parentheses. */
export function reportJobPhaseText(phase: BenchmarkRunReportJobPhase): string {
  switch (phase) {
    case 'Queued': return 'waiting for the report writer';
    case 'Preparing': return 'preparing the fact sheet';
    case 'Writing': return 'writing';
    default: return 'finishing';
  }
}

/** The document types in the order the Document filter lists them. */
const DOCUMENT_TYPE_ORDER: readonly string[] = [
  ...REPORT_PACK_AUDIENCES.map(option => option.label),
  'Run report',
  'Tool-call log',
  'Run diagnostics'
];

/** A pricing basis as a sentence names it. */
function pricingBasisText(basis: string): string {
  return basis === 'AsRun' ? 'prices at run time' : basis === 'Current' ? 'today’s prices' : basis;
}

type ReportJobPoll = { ok: true; view: BenchmarkRunReportJobDto | null } | { ok: false };

let nextInstanceId = 0;

/**
 * The Download Center's body and footer: packages report documents and run files for download, as
 * one file or as a ZIP with `MANIFEST.md`. Three packages set the choices (Internal, External,
 * Custom); a paged, sortable and filterable table lists every available document with its options,
 * a View and, for a Report Pack document, a Delete.
 *
 * Hosted by the Download Center dialog (`app-benchmark-download-center`), which calls `load()` on
 * every opening, and by the Model Comparison wizard's step 4, which binds `context` and lends
 * `chartActions`: a Charts column, **Update charts…** and per-row **Remove charts**.
 *
 * It makes no request but the document list or detail that fills the table, the render endpoints
 * (Markdown, PDF and Word), the run report and tool-call log endpoints with their PDFs and Word
 * documents, the diagnostics PDF and Word endpoints, which render the captured text and store
 * nothing, a document's delete and its charts' delete: nothing here can start generation. Chart
 * uploads go through the host's `chartActions.publish`.
 *
 * Its nested dialogs stop their own close and cancel events, so a dialog around the panel never
 * sees them. Every element id derives from `idPrefix`.
 */
@Component({
  selector: 'app-download-center-panel',
  standalone: true,
  imports: [InfoTipComponent, SortHeaderComponent, TablePagerComponent, PdfViewerDialogComponent, ReportChartPickerComponent],
  templateUrl: './download-center-panel.component.html',
  styleUrls: ['./download-center-panel.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DownloadCenterPanelComponent implements OnInit, OnChanges, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** What to list; a new context loads afresh. The dialog wrapper calls `load()` instead. */
  @Input() context: DownloadCenterContext | null = null;
  /** Bumped by the host to list a library context's documents again, keeping the choices made. */
  @Input() reloadToken = 0;
  /** The prefix of every element id. */
  @Input() idPrefix = `dcp${++nextInstanceId}`;
  /** Chart actions for the documents of the comparison open beside the panel; none without them. */
  @Input() chartActions: DownloadCenterChartActions | null = null;
  /** Shows the footer's Cancel, for the dialog wrapper. */
  @Input() showCancel = false;

  /** The footer's Cancel. */
  @Output() readonly cancelRequested = new EventEmitter<void>();
  /** A document was deleted, or its charts were updated or removed. */
  @Output() readonly documentsChanged = new EventEmitter<void>();

  @ViewChild(PdfViewerDialogComponent) pdfViewer?: PdfViewerDialogComponent;
  @ViewChild('deleteDialog') deleteDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('chartsDialog') chartsDialog?: ElementRef<HTMLDialogElement>;

  readonly packages = DOWNLOAD_PACKAGES;
  readonly papers = PDF_PAPERS;
  readonly disclosureGuides = REPORT_DISCLOSURE_GUIDES;
  readonly disclosureTitle = DOWNLOAD_CENTER_DISCLOSURE_TITLE;
  readonly disclosureNotes = [REPORT_DISCLOSURE_NOTE_SHARED, REPORT_DISCLOSURE_FORMATS_NOTE];
  readonly formatUtc = formatUtc;

  packageId: DownloadPackageId = 'internal';
  /** The paper every PDF and Word document of a download is laid out on. */
  paper: BenchmarkPdfPaper = 'a4';
  /** Every row, in source order; the table renders `view`. */
  rows: DownloadRow[] = [];
  loadingDocuments = false;
  /** Notices above the table: runs that no longer exist, documents that could not be loaded. */
  notices: string[] = [];
  preparing = false;
  /** The download or chart update in preparation, shown over the body; null while none is. */
  progress: DownloadProgress | null = null;
  statusMessage = '';
  failures: DownloadFailure[] = [];

  /** The phase of the run's report writing job while it is not finished; null when there is none to wait for. */
  reportJobPhase: BenchmarkRunReportJobPhase | null = null;

  /**
   * Sort, filter and page state. The source list stays in load order: the download plan, the
   * manifest and the remembered choices read `rows`, never the view.
   */
  readonly table = new TableState<DownloadRow>('created', 'desc').registerAccessors(
    {
      created: row => utcDate(row.createdAtUtc)?.getTime() ?? null,
      document: row => row.label
    },
    {
      subject: row => row.subject,
      document: exactFilter(row => row.documentType),
      suite: exactFilter(row => row.suite),
      selected: exactFilter(row => (this.isIncluded(row) ? 'yes' : 'no'))
    }
  );

  // --- Delete ---
  deleteTarget: DownloadRow | null = null;
  deleting = false;
  deleteError: string | null = null;

  // --- Charts ---
  /** The rows the Update charts dialog applies to. */
  chartDialogRows: DownloadRow[] = [];
  /** The dialog's picker columns: the document types of `chartDialogRows`. */
  chartDialogAudiences: BenchmarkReportAudience[] = [];
  /** The figures the dialog will draw, per document type. */
  chartDraft: ReportChartSelection = {};
  /** The server's message when a chart update stopped because chart storage is not configured. */
  chartStorageMessage: string | null = null;
  /** Chosen documents a chart update left out, each with its reason. */
  chartSkips: DownloadFailure[] = [];
  /** Documents whose charts could not be updated or removed. */
  chartFailures: DownloadFailure[] = [];
  /** The row whose More popover is open, for its trigger's `aria-expanded`. */
  moreOpenKey: string | null = null;

  private readonly states = new Map<string, DownloadRowState>();
  /** The context the rows were loaded for. */
  private loaded: DownloadCenterContext | null = null;
  /** Bumped on every load and deactivation, so an abandoned request or download never lands. */
  private generation = 0;
  private reportJobSub: Subscription | null = null;
  private listSub: Subscription | null = null;
  private deleteSub: Subscription | null = null;
  /** Where focus returns when a nested dialog closes. */
  private returnFocus: HTMLElement | null = null;
  /** After a delete: the key of the row to focus, or null for the Documents heading. */
  private focusAfterDelete: string | null | undefined = undefined;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['context'] && this.context !== this.loaded) {
      if (this.context) {
        this.load(this.context);
      } else {
        this.deactivate();
        this.loaded = null;
        this.rows = [];
        this.states.clear();
      }
    } else if (changes['reloadToken'] && !changes['reloadToken'].firstChange) {
      this.refresh();
    }
  }

  ngOnDestroy(): void {
    this.generation++;
    this.stopReportJobPoll();
    this.listSub?.unsubscribe();
    this.deleteSub?.unsubscribe();
  }

  // -------------------------------------------------------------------------------------------
  // Loading
  // -------------------------------------------------------------------------------------------

  /** Lists a context's files afresh, at the last package and paper used. */
  load(context: DownloadCenterContext): void {
    this.generation++;
    this.loaded = context;
    this.rows = [];
    this.states.clear();
    this.notices = [];
    this.failures = [];
    this.statusMessage = '';
    this.preparing = false;
    this.progress = null;
    this.reportJobPhase = null;
    this.chartStorageMessage = null;
    this.chartSkips = [];
    this.chartFailures = [];
    this.stopReportJobPoll();
    this.listSub?.unsubscribe();
    this.table.clearFilters();
    this.table.page = 1;
    const stored = readStoredSettings();
    this.packageId = stored?.package ?? 'internal';
    this.paper = stored?.paper ?? 'a4';

    switch (context.kind) {
      case 'run':
        this.addRows(runFileRows(context.run));
        this.loadRunDocuments(context.run.id, this.generation);
        this.pollRunReportJob(context.run.id, this.generation, 0);
        break;
      case 'documents':
        this.loadChosenDocuments(context.documentIds, this.generation);
        break;
      case 'library':
        this.loadLibrary(context, this.generation, false);
        break;
    }
    this.cdr.markForCheck();
  }

  /** Lists a library context's documents again, keeping every choice made on a row still listed. */
  refresh(): void {
    const context = this.loaded;
    if (context?.kind === 'library') {
      this.loadLibrary(context, this.generation, true);
    }
  }

  /** Drops everything in flight: a list, the job poll, a download. The rows stay. */
  deactivate(): void {
    this.generation++;
    this.stopReportJobPoll();
    this.listSub?.unsubscribe();
    this.loadingDocuments = false;
    this.preparing = false;
    this.progress = null;
    this.cdr.markForCheck();
  }

  /** The notice above the table while the run's AI-written reports are being written, or null. */
  get reportJobNotice(): string | null {
    const phase = this.reportJobPhase;
    return phase === null
      ? null
      : `The AI-written reports of this run are being written (${reportJobPhaseText(phase)}). They appear here when they are done.`;
  }

  // -------------------------------------------------------------------------------------------
  // Packages and row choices
  // -------------------------------------------------------------------------------------------

  selectPackage(id: DownloadPackageId): void {
    if (this.preparing) {
      return;
    }
    this.packageId = id;
    const stored = readStoredSettings();
    for (const row of this.rows) {
      const preset = this.presetState(row, id);
      this.states.set(row.key, this.rememberedState(row, id, preset, stored));
    }
    this.failures = [];
    this.statusMessage = '';
    this.cdr.markForCheck();
  }

  selectPaper(paper: BenchmarkPdfPaper): void {
    if (this.preparing || !PDF_PAPERS.some(p => p.id === paper)) {
      return;
    }
    this.paper = paper;
    this.cdr.markForCheck();
  }

  get currentPackage(): DownloadPackage {
    return DOWNLOAD_PACKAGES.find(p => p.id === this.packageId) ?? DOWNLOAD_PACKAGES[0];
  }

  /** Internal locks every pack document at Full with peers named. */
  get optionsLocked(): boolean {
    return this.packageId === 'internal';
  }

  stateOf(row: DownloadRow): DownloadRowState {
    let state = this.states.get(row.key);
    if (!state) {
      state = this.presetState(row, this.packageId);
      this.states.set(row.key, state);
    }
    return state;
  }

  /** In the External package, internal-only rows are listed but cannot be chosen. */
  isSelectable(row: DownloadRow): boolean {
    return this.isSelectableIn(row, this.packageId);
  }

  isIncluded(row: DownloadRow): boolean {
    return this.isSelectable(row) && this.stateOf(row).selected;
  }

  /** Internal only whatever is chosen, or at the chosen Full disclosure. */
  isInternalOnly(row: DownloadRow): boolean {
    return row.internalReason !== null
      || (row.kind === 'pack' && this.stateOf(row).disclosure === BenchmarkReportDisclosure.Full);
  }

  internalReasonOf(row: DownloadRow): string | null {
    if (row.internalReason) {
      return row.internalReason;
    }
    return this.isInternalOnly(row) ? INTERNAL_REASONS.fullDisclosure : null;
  }

  disclosureOptions(row: DownloadRow): BenchmarkReportDisclosure[] {
    return this.disclosureOptionsFor(row, this.packageId);
  }

  namingOptions(): BenchmarkReportPeerNaming[] {
    return this.namingOptionsFor(this.packageId);
  }

  disclosureLabel(disclosure: BenchmarkReportDisclosure): string {
    return disclosureLabel(disclosure);
  }

  namingLabel(naming: BenchmarkReportPeerNaming): string {
    return naming === BenchmarkReportPeerNaming.Named ? 'Named' : 'Anonymized';
  }

  formatLabel(format: DownloadFormat): string {
    return FORMAT_LABELS[format];
  }

  hasFormat(row: DownloadRow, format: DownloadFormat): boolean {
    return this.stateOf(row).formats.includes(format);
  }

  rowId(row: DownloadRow): string {
    return `${this.idPrefix}-${row.key.replace(/[^A-Za-z0-9_-]/g, '-')}`;
  }

  /** The row's name in its controls' accessible names: its label, and when it was written. */
  rowName(row: DownloadRow): string {
    const created = row.kind === 'pack' ? formatUtc(row.createdAtUtc) : '';
    return created ? `${row.label}, ${created}` : row.label;
  }

  /** The row checkbox's description: why it cannot be chosen, and the row's note. */
  rowDescribedBy(row: DownloadRow): string | null {
    const rid = this.rowId(row);
    const ids: string[] = [];
    if (!this.isSelectable(row)) {
      ids.push(`${rid}-reason`);
    }
    if (row.note) {
      ids.push(`${rid}-note`);
    }
    return ids.length > 0 ? ids.join(' ') : null;
  }

  toggleRow(row: DownloadRow, event: Event): void {
    if (!this.isSelectable(row) || this.preparing) {
      return;
    }
    this.stateOf(row).selected = (event.target as HTMLInputElement).checked;
    this.cdr.markForCheck();
  }

  setDisclosure(row: DownloadRow, event: Event): void {
    const value = Number((event.target as HTMLSelectElement).value) as BenchmarkReportDisclosure;
    if (this.optionsLocked || !this.disclosureOptions(row).includes(value)) {
      return;
    }
    this.stateOf(row).disclosure = value;
    this.cdr.markForCheck();
  }

  setNaming(row: DownloadRow, event: Event): void {
    const value = Number((event.target as HTMLSelectElement).value) as BenchmarkReportPeerNaming;
    if (this.optionsLocked || !this.namingOptions().includes(value)) {
      return;
    }
    this.stateOf(row).naming = value;
    this.cdr.markForCheck();
  }

  toggleFormat(row: DownloadRow, format: DownloadFormat, event: Event): void {
    if (!row.formats.includes(format)) {
      return;
    }
    const state = this.stateOf(row);
    const checked = (event.target as HTMLInputElement).checked;
    const next = new Set(state.formats);
    if (checked) {
      next.add(format);
    } else {
      next.delete(format);
    }
    state.formats = row.formats.filter(f => next.has(f));
    this.cdr.markForCheck();
  }

  /** A shareable pack document chosen with its peers named. */
  get namedPeersWarning(): boolean {
    return this.packageId !== 'internal' && this.rows.some(row =>
      row.kind === 'pack' && this.isIncluded(row) && !this.isInternalOnly(row)
      && this.stateOf(row).naming === BenchmarkReportPeerNaming.Named);
  }

  // -------------------------------------------------------------------------------------------
  // The table: sorting, filtering, paging and the selection line
  // -------------------------------------------------------------------------------------------

  get view(): DownloadRow[] {
    return this.table.view(this.rows);
  }

  /** What the table says with no row at all, as against no row the filters let through. */
  get emptyText(): string {
    const context = this.loaded;
    if (context?.kind === 'library') {
      return context.scope.kind === 'comparison'
        ? 'No reports have been written for this comparison yet.'
        : 'No comparison reports yet.';
    }
    return 'Nothing is available to download.';
  }

  /** The Document filter's options: the document types present. */
  get documentTypeOptions(): string[] {
    const present = new Set(this.rows.map(row => row.documentType));
    return DOCUMENT_TYPE_ORDER.filter(type => present.has(type));
  }

  /** The Suite filter's options: the suites present. */
  get suiteOptions(): string[] {
    return [...new Set(this.rows.map(row => row.suite).filter(suite => !!suite))].sort((a, b) => a.localeCompare(b));
  }

  filterValue(column: string): string {
    return this.table.filters[column] ?? '';
  }

  setFilter(column: string, event: Event): void {
    this.table.setFilter(column, (event.target as HTMLInputElement | HTMLSelectElement).value);
    this.cdr.markForCheck();
  }

  clearFilters(): void {
    const selectedOnly = this.showSelectedOnly;
    this.table.clearFilters();
    if (selectedOnly) {
      this.table.setFilter('selected', 'yes');
    }
    this.cdr.markForCheck();
  }

  /** The column filters, *Show selected only* aside, which has its own control. */
  get hasColumnFilters(): boolean {
    return Object.entries(this.table.filters).some(([column, value]) => column !== 'selected' && (value ?? '').trim() !== '');
  }

  onTableChanged(): void {
    this.cdr.markForCheck();
  }

  /** Rows chosen for download, on any page. */
  get selectedCount(): number {
    return this.rows.filter(row => this.isIncluded(row)).length;
  }

  /** Chosen rows the current page does not show. */
  get offPageSelectedCount(): number {
    const onPage = new Set(this.view.map(row => row.key));
    return this.rows.filter(row => this.isIncluded(row) && !onPage.has(row.key)).length;
  }

  /** Rows the filters show, on any page, that can be chosen. */
  get shownSelectableRows(): DownloadRow[] {
    return this.table.viewAll(this.rows).filter(row => this.isSelectable(row));
  }

  get showSelectedOnly(): boolean {
    return this.table.filters['selected'] === 'yes';
  }

  toggleShowSelectedOnly(): void {
    this.table.setFilter('selected', this.showSelectedOnly ? '' : 'yes');
    this.cdr.markForCheck();
  }

  clearSelection(): void {
    if (this.preparing) {
      return;
    }
    for (const row of this.rows) {
      this.stateOf(row).selected = false;
    }
    if (this.showSelectedOnly) {
      this.table.setFilter('selected', '');
    }
    this.cdr.markForCheck();
  }

  /** Chooses every row the filters show, on every page. */
  selectShown(): void {
    if (this.preparing) {
      return;
    }
    for (const row of this.shownSelectableRows) {
      this.stateOf(row).selected = true;
    }
    this.cdr.markForCheck();
  }

  // -------------------------------------------------------------------------------------------
  // The download
  // -------------------------------------------------------------------------------------------

  /** Every file the current choices produce, in source order. */
  get plannedFiles(): { row: DownloadRow; format: DownloadFormat }[] {
    const files: { row: DownloadRow; format: DownloadFormat }[] = [];
    for (const row of this.rows) {
      if (!this.isIncluded(row)) {
        continue;
      }
      for (const format of row.formats) {
        if (this.stateOf(row).formats.includes(format)) {
          files.push({ row, format });
        }
      }
    }
    return files;
  }

  /** `5 files · 1 ZIP · Internal package`. */
  get summaryLine(): string {
    const count = this.plannedFiles.length;
    const name = this.currentPackage.fullName;
    if (count === 0) {
      return `No files chosen · ${name}`;
    }
    if (count === 1) {
      return `1 file · ${name}`;
    }
    return `${count} files · 1 ZIP · ${name}`;
  }

  get canDownload(): boolean {
    return !this.preparing && !this.loadingDocuments && this.plannedFiles.length > 0;
  }

  /**
   * Fetches and converts every chosen file, one at a time, then saves one file as itself or
   * several as a ZIP with `MANIFEST.md`. A file that fails is listed and the rest still download.
   */
  async download(): Promise<void> {
    if (!this.canDownload || !this.loaded) {
      return;
    }
    const generation = this.generation;
    const context = this.loaded;
    const packagedAt = downloadCenterIo.now();
    const plan = this.plannedFiles.map(file => ({ ...file, state: { ...this.stateOf(file.row), formats: [...this.stateOf(file.row).formats] } }));
    const packageName = this.currentPackage.fullName;
    const paper = this.paper;
    const texts = new Map<string, Promise<SourceText>>();

    const progress: DownloadProgress = { done: 0, total: plan.length + 1, step: '' };
    this.preparing = true;
    this.progress = progress;
    this.failures = [];
    this.persistSettings();

    const produced: ProducedFile[] = [];
    const failures: DownloadFailure[] = [];

    for (let i = 0; i < plan.length; i++) {
      const { row, format, state } = plan[i];
      this.showStep(progress, `Preparing ${i + 1} of ${plan.length} — ${row.label} (${FORMAT_LABELS[format]})`);
      try {
        const source = format === 'pdf' || format === 'docx'
          ? await this.binarySource(row, state, context, texts, paper, format)
          : await this.sourceText(row, state, context, texts);
        if (generation !== this.generation) {
          this.releaseProgress(progress);
          return;
        }
        produced.push(this.produceFile(row, state, format, source, context, packagedAt, paper));
      } catch (error) {
        if (generation !== this.generation) {
          this.releaseProgress(progress);
          return;
        }
        failures.push({ label: `${row.label} (${FORMAT_LABELS[format]})`, reason: failureReason(error, row) });
      }
      progress.done++;
    }

    if (produced.length > 0) {
      this.showStep(progress, plan.length === 1 ? 'Saving…' : 'Building the ZIP…');
    }

    try {
      if (plan.length === 1) {
        if (produced.length === 1) {
          const only = produced[0];
          if ('bytes' in only) {
            downloadCenterIo.saveBytes(only.name, only.bytes, only.mime);
          } else {
            downloadCenterIo.saveText(only.name, only.text, only.mime);
          }
        }
      } else if (produced.length > 0) {
        const names = uniqueFileNames([MANIFEST_FILE_NAME, ...produced.map(file => file.name)]).slice(1);
        const manifestFiles = produced.map((file, index) => ({ ...file.manifest, name: names[index] }));
        const manifest = await buildManifest({
          packageName,
          packagedAt,
          files: manifestFiles,
          failures: failures as ManifestFailure[]
        });
        const entries: ArchiveEntry[] = produced.map((file, index): ArchiveEntry => 'bytes' in file
          ? { name: names[index], bytes: file.bytes, mtime: file.mtime }
          : { name: names[index], text: file.text, mtime: file.mtime });
        entries.push({ name: MANIFEST_FILE_NAME, text: manifest, mtime: packagedAt });
        const archive = await buildTextArchive(entries);
        if (generation !== this.generation) {
          this.releaseProgress(progress);
          return;
        }
        downloadCenterIo.saveBlob(archive, this.zipFileName(context, packagedAt));
      }
    } catch (error) {
      if (generation !== this.generation) {
        this.releaseProgress(progress);
        return;
      }
      failures.push({ label: 'The ZIP', reason: error instanceof Error ? error.message : 'it could not be built' });
      produced.length = 0;
    }

    this.preparing = false;
    this.progress = null;
    this.failures = failures;
    this.statusMessage = completionMessage(plan.length, produced.length, failures.length);
    this.cdr.markForCheck();
  }

  /** Shows a step in the overlay and in the footer's status line. */
  private showStep(progress: DownloadProgress, step: string): void {
    progress.step = step;
    this.statusMessage = step;
    this.cdr.markForCheck();
  }

  /** Clears an abandoned download's progress, unless a newer one has already replaced it. */
  private releaseProgress(progress: DownloadProgress): void {
    if (this.progress === progress) {
      this.progress = null;
      this.cdr.markForCheck();
    }
  }

  // -------------------------------------------------------------------------------------------
  // View
  // -------------------------------------------------------------------------------------------

  /** A pack document or a run report opens in the PDF viewer; other run files have no View. */
  canView(row: DownloadRow): boolean {
    return (row.kind === 'pack' && row.doc !== null) || (row.kind === 'runReport' && row.runId !== null);
  }

  viewRow(row: DownloadRow, button: HTMLElement): void {
    if (row.kind === 'pack' && row.doc) {
      this.viewDocument(row.doc, button);
    } else if (row.kind === 'runReport' && row.runId !== null) {
      this.viewRunReport(row, button);
    }
  }

  /**
   * Opens a document in the PDF viewer at the fullest disclosure it allows, the others offered as
   * versions; with peers, the peer names are a second choice that opens at *Named*.
   */
  private viewDocument(doc: BenchmarkReportDocumentListItemDto, button: HTMLElement): void {
    this.returnFocus = button;
    const allowed = [...new Set(doc.allowedDisclosures ?? [])].sort((a, b) => a - b);
    const disclosures = allowed.length > 0 ? allowed : [BenchmarkReportDisclosure.Full];
    const highest = disclosures[disclosures.length - 1];
    const byKey = new Map<string, BenchmarkReportDisclosure>(
      disclosures.map(disclosure => [reportDisclosureParam(disclosure), disclosure] as const));
    const disclosureOf = (variant: string | null): BenchmarkReportDisclosure =>
      (variant !== null ? byKey.get(variant) : undefined) ?? highest;
    const namingOf = (secondary: string | undefined): BenchmarkReportPeerNaming =>
      secondary === reportPeerNamingParam(BenchmarkReportPeerNaming.Anonymized)
        ? BenchmarkReportPeerNaming.Anonymized
        : BenchmarkReportPeerNaming.Named;
    const hasPeers = (doc.peerCount ?? 0) > 0;
    const paper = rememberedPdfPaper();
    const label = audienceLabel(doc.audience);
    this.pdfViewer?.open({
      title: doc.title || label,
      subtitle: documentViewerSubtitle(doc),
      variants: disclosures.map(disclosure => ({ key: reportDisclosureParam(disclosure), label: disclosureLabel(disclosure) })),
      initialVariant: reportDisclosureParam(highest),
      variantsInfo: reportDisclosureInfo(doc.audience, { peerNaming: hasPeers }),
      ...(hasPeers ? {
        secondaryVariants: {
          label: 'Peer names',
          options: [BenchmarkReportPeerNaming.Named, BenchmarkReportPeerNaming.Anonymized].map(naming => ({
            key: reportPeerNamingParam(naming),
            label: naming === BenchmarkReportPeerNaming.Named ? 'Named' : 'Anonymized'
          })),
          initial: reportPeerNamingParam(BenchmarkReportPeerNaming.Named)
        }
      } : {}),
      load: (variant, secondary) =>
        this.benchmarkService.getReportDocumentPdf(doc.id, disclosureOf(variant), namingOf(secondary), paper),
      tabUrl: (variant, secondary) =>
        this.benchmarkService.reportDocumentPdfUrl(doc.id, disclosureOf(variant), namingOf(secondary), paper, true),
      fallbackFileName: `${reportDocumentFileStem(doc, label)}.pdf`
    });
  }

  /** Opens a run's report in the PDF viewer, on the remembered paper. */
  private viewRunReport(row: DownloadRow, button: HTMLElement): void {
    const runId = row.runId!;
    this.returnFocus = button;
    const paper = rememberedPdfPaper();
    this.pdfViewer?.open({
      title: row.label,
      subtitle: row.detail,
      load: () => this.benchmarkService.getRunReportPdf(runId, paper),
      fallbackFileName: `benchmark_run${runId}_report_INTERNAL.pdf`
    });
  }

  // -------------------------------------------------------------------------------------------
  // Delete
  // -------------------------------------------------------------------------------------------

  /** Only a Report Pack document can be deleted here; a run's own documents are the run report's. */
  canDelete(row: DownloadRow): boolean {
    return row.kind === 'pack' && row.doc !== null
      && (row.doc.origin ?? BenchmarkReportDocumentOrigin.ReportPack) === BenchmarkReportDocumentOrigin.ReportPack;
  }

  requestDelete(row: DownloadRow, button: HTMLElement): void {
    if (!this.canDelete(row) || this.preparing) {
      return;
    }
    this.deleteTarget = row;
    this.deleteError = null;
    this.deleting = false;
    this.returnFocus = button;
    this.focusAfterDelete = undefined;
    this.cdr.detectChanges();
    const dialog = this.deleteDialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
  }

  cancelDelete(): void {
    this.deleteDialog?.nativeElement?.close();
  }

  confirmDelete(): void {
    const row = this.deleteTarget;
    const doc = row?.doc;
    if (!row || !doc || this.deleting) {
      return;
    }
    this.deleting = true;
    this.deleteError = null;
    const generation = this.generation;
    this.deleteSub?.unsubscribe();
    this.deleteSub = this.benchmarkService.deleteReportDocument(doc.id).subscribe({
      next: () => {
        if (generation !== this.generation) {
          return;
        }
        const view = this.view;
        const index = view.findIndex(r => r.key === row.key);
        const next = index >= 0 ? (view[index + 1] ?? view[index - 1] ?? null) : null;
        this.deleting = false;
        this.rows = this.rows.filter(r => r.key !== row.key);
        this.states.delete(row.key);
        this.statusMessage = `Deleted ${this.rowName(row)}.`;
        this.returnFocus = null;
        this.focusAfterDelete = next?.key ?? null;
        this.deleteDialog?.nativeElement?.close();
        this.documentsChanged.emit();
        this.cdr.markForCheck();
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.generation) {
          return;
        }
        this.deleting = false;
        this.deleteError = serverMessage(error) ?? 'The document could not be deleted.';
        this.cdr.markForCheck();
      }
    });
    this.cdr.markForCheck();
  }

  onDeleteDialogEvent(event: Event): void {
    event.stopPropagation();
    if (event.type !== 'close' || this.deleteDialog?.nativeElement?.open) {
      return;
    }
    this.deleteTarget = null;
    this.deleteError = null;
    const focusKey = this.focusAfterDelete;
    this.focusAfterDelete = undefined;
    if (focusKey === undefined) {
      this.cdr.markForCheck();
      this.restoreFocus();
      return;
    }
    // The next row, else the previous, else the Documents heading: the deleted row's buttons are gone.
    this.cdr.detectChanges();
    const nextRow = focusKey === null ? null : this.rows.find(r => r.key === focusKey) ?? null;
    const find = (id: string): HTMLElement | null => this.host.nativeElement.querySelector<HTMLElement>(`[id="${id}"]`);
    const target = nextRow ? find(`${this.rowId(nextRow)}-view`) ?? find(`${this.rowId(nextRow)}-check`) : null;
    (target ?? find(`${this.idPrefix}-documents-title`))?.focus();
  }

  // -------------------------------------------------------------------------------------------
  // Charts
  // -------------------------------------------------------------------------------------------

  /** How many figures a document has charts of. */
  chartFigureCount(row: DownloadRow): number {
    const doc = row.doc;
    if (!doc) {
      return 0;
    }
    return doc.chartFigureKeys?.length ?? (doc.chartCount ?? 0);
  }

  /** Whether a charted document's charts were drawn with step 2's current settings. */
  chartsCurrent(row: DownloadRow): boolean {
    const hash = this.chartActions?.currentSettingsHash ?? null;
    return hash !== null && row.doc?.chartSettingsHash === hash;
  }

  /** `None`, `3 · current` or `3 · differs from step 2`. */
  chartStateText(row: DownloadRow): string {
    const count = this.chartFigureCount(row);
    if (count === 0) {
      return 'None';
    }
    return `${count} · ${this.chartsCurrent(row) ? 'current' : 'differs from step 2'}`;
  }

  /** Why a document cannot be charted from the comparison open beside the panel, or null. */
  chartSkipReason(row: DownloadRow): string | null {
    const actions = this.chartActions;
    const doc = row.doc;
    if (!actions || row.kind !== 'pack' || !doc) {
      return 'It is not a report document.';
    }
    if ((doc.peerCount ?? 0) <= 0) {
      return CHART_SKIP_REASONS.noPeers;
    }
    if (!actions.comparisonKeyMatches(doc)) {
      return CHART_SKIP_REASONS.otherComparison;
    }
    if (doc.pricingBasis && actions.pricingBasis && doc.pricingBasis !== actions.pricingBasis) {
      return `It was written on ${pricingBasisText(doc.pricingBasis)}, and step 2 shows ${pricingBasisText(actions.pricingBasis)}.`;
    }
    return null;
  }

  /** Chosen Report Pack documents, on any page: what the toolbar's Update charts… applies to. */
  get selectedPackRows(): DownloadRow[] {
    return this.rows.filter(row => row.kind === 'pack' && row.doc !== null && this.isIncluded(row));
  }

  /** Why Update charts… is unavailable, or null. */
  get updateChartsBlockedReason(): string | null {
    if (!this.chartActions) {
      return null;
    }
    if (this.chartActions.storageMissing) {
      return 'Chart storage is not configured on the server.';
    }
    if (this.preparing) {
      return 'Wait for the download or the chart update in progress.';
    }
    if (this.selectedPackRows.length === 0) {
      return 'Choose one or more report documents first.';
    }
    return null;
  }

  /** Why a row's Update charts is unavailable, or null. */
  rowUpdateBlockedReason(row: DownloadRow): string | null {
    if (this.chartActions?.storageMissing) {
      return 'Chart storage is not configured on the server.';
    }
    if (this.preparing) {
      return 'Wait for the download or the chart update in progress.';
    }
    return this.chartSkipReason(row);
  }

  /** Why a row's Remove charts is unavailable, or null. */
  rowRemoveBlockedReason(row: DownloadRow): string | null {
    if (this.preparing) {
      return 'Wait for the download or the chart update in progress.';
    }
    return this.chartFigureCount(row) === 0 ? 'It has no charts.' : null;
  }

  /** Opens the Update charts dialog on the chosen documents. */
  openChartsDialog(button: HTMLElement): void {
    if (this.updateChartsBlockedReason !== null) {
      return;
    }
    this.showChartsDialog(this.selectedPackRows, button);
  }

  /** Opens the Update charts dialog on one row, from its More popover. */
  openRowChartsDialog(row: DownloadRow): void {
    if (this.rowUpdateBlockedReason(row) !== null) {
      return;
    }
    const trigger = this.moreTrigger(row);
    this.hideMore(row);
    this.showChartsDialog([row], trigger);
  }

  /**
   * The dialog's picker shows the chosen documents' types and starts from the figures they already
   * have; a type none of them has charts for starts from the remembered selection.
   */
  private showChartsDialog(rows: DownloadRow[], returnFocus: HTMLElement | null): void {
    const audiences = REPORT_PACK_AUDIENCES.map(option => option.audience)
      .filter(audience => rows.some(row => row.doc?.audience === audience));
    const remembered = readStoredChartSelection();
    const draft: Partial<Record<BenchmarkReportAudience, readonly string[]>> = {};
    for (const audience of audiences) {
      const current = new Set(rows.filter(row => row.doc?.audience === audience)
        .flatMap(row => row.doc?.chartFigureKeys ?? []));
      draft[audience] = current.size > 0 ? [...current] : [...(remembered[audience] ?? [])];
    }
    this.chartDialogRows = rows;
    this.chartDialogAudiences = audiences;
    this.chartDraft = normalizeChartSelection(draft as ReportChartSelection);
    this.returnFocus = returnFocus;
    this.cdr.detectChanges();
    const dialog = this.chartsDialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
  }

  onChartDraftChange(selection: ReportChartSelection): void {
    this.chartDraft = selection;
    this.cdr.markForCheck();
  }

  cancelCharts(): void {
    this.chartsDialog?.nativeElement?.close();
  }

  onChartsDialogEvent(event: Event): void {
    event.stopPropagation();
    if (event.type !== 'close' || this.chartsDialog?.nativeElement?.open) {
      return;
    }
    this.cdr.markForCheck();
    this.restoreFocus();
  }

  /**
   * Update: closes the dialog and charts every chosen document the comparison can chart, one at a
   * time under the preparing overlay. The documents it leaves out are listed with their reasons; a
   * stop because chart storage is not configured closes the overlay and shows the server's message.
   */
  async applyCharts(): Promise<void> {
    const actions = this.chartActions;
    if (!actions || this.preparing) {
      return;
    }
    const rows = this.chartDialogRows;
    const selection = this.chartDraft;
    this.chartsDialog?.nativeElement?.close();

    const skips: DownloadFailure[] = [];
    const targets: ReportChartTarget[] = [];
    for (const row of rows) {
      const reason = this.chartSkipReason(row);
      if (reason) {
        skips.push({ label: this.rowName(row), reason });
        continue;
      }
      const doc = row.doc!;
      targets.push({
        documentId: doc.id,
        audience: doc.audience,
        subjectKey: doc.subjectKey,
        peerLetters: doc.peerLetters ?? {},
        label: row.label
      });
    }
    this.chartSkips = skips;
    this.chartFailures = [];
    this.chartStorageMessage = null;
    this.failures = [];
    if (targets.length === 0) {
      this.statusMessage = 'No chosen document can be charted from this comparison.';
      this.cdr.markForCheck();
      return;
    }

    const generation = this.generation;
    const progress: DownloadProgress = { done: 0, total: targets.length, step: 'Preparing the charts…' };
    this.preparing = true;
    this.progress = progress;
    this.statusMessage = progress.step;
    this.cdr.markForCheck();

    let result: ReportChartPublishResult | null = null;
    try {
      result = await actions.publish(targets, selection, update => {
        if (this.progress === progress) {
          progress.done = update.done;
          progress.total = Math.max(1, update.total);
          this.showStep(progress, update.step);
        }
      });
    } catch (error) {
      this.chartFailures = [{ label: 'The charts', reason: error instanceof Error && error.message ? error.message : 'they could not be updated' }];
    }
    if (generation !== this.generation) {
      this.releaseProgress(progress);
      return;
    }
    this.preparing = false;
    this.progress = null;

    if (result) {
      const labelOf = (id: number): string => {
        const row = rows.find(r => r.doc?.id === id);
        return row ? this.rowName(row) : `Report document #${id}`;
      };
      this.chartFailures = result.failed.map(failure => ({ label: labelOf(failure.documentId), reason: failure.message }));
      this.chartSkips = [
        ...skips,
        ...result.skipped.map(id => ({ label: labelOf(id), reason: 'No chart is chosen for its document type.' }))
      ];
      this.chartStorageMessage = result.storageNotConfigured;
      const published = result.published.length;
      this.statusMessage = result.storageNotConfigured
        ? 'The charts were not updated: chart storage is not configured.'
        : result.canceled
          ? `Stopped after charting ${published} ${published === 1 ? 'document' : 'documents'}.`
          : `Charts updated on ${published} ${published === 1 ? 'document' : 'documents'}.`;
      if (published > 0) {
        this.documentsChanged.emit();
      }
    } else {
      this.statusMessage = 'The charts could not be updated.';
    }
    this.refresh();
    this.cdr.markForCheck();
  }

  /** Removes every chart of one document, from its More popover. */
  removeCharts(row: DownloadRow): void {
    const doc = row.doc;
    if (!doc || this.rowRemoveBlockedReason(row) !== null) {
      return;
    }
    const trigger = this.moreTrigger(row);
    this.hideMore(row);
    trigger?.focus();
    const generation = this.generation;
    this.chartFailures = [];
    this.benchmarkService.deleteReportDocumentCharts(doc.id).subscribe({
      next: () => {
        if (generation !== this.generation) {
          return;
        }
        this.statusMessage = `Removed the charts of ${this.rowName(row)}.`;
        this.documentsChanged.emit();
        this.refresh();
        this.cdr.markForCheck();
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.generation) {
          return;
        }
        this.chartFailures = [{ label: this.rowName(row), reason: serverMessage(error) ?? 'the charts could not be removed' }];
        this.cdr.markForCheck();
      }
    });
  }

  // --- The row's More popover (§4f) ---

  moreId(row: DownloadRow): string {
    return `${this.rowId(row)}-more`;
  }

  onMoreToggle(row: DownloadRow, event: Event): void {
    const open = (event as ToggleEvent).newState === 'open';
    const popover = this.host.nativeElement.querySelector<HTMLElement>(`[id="${this.moreId(row)}"]`);
    if (open) {
      this.moreOpenKey = row.key;
      this.cdr.markForCheck();
      refreshAnchorPositioning();
      popover?.querySelector<HTMLElement>('.gh-action-popover-item:not([aria-disabled="true"])')?.focus();
      return;
    }
    if (this.moreOpenKey === row.key) {
      this.moreOpenKey = null;
      this.cdr.markForCheck();
    }
    const active = document.activeElement;
    if (!active || active === document.body || !!popover?.contains(active)) {
      this.moreTrigger(row)?.focus();
    }
  }

  /** Escape closes the popover only; a dialog around the panel stays open. */
  onMoreKeydown(row: DownloadRow, event: KeyboardEvent): void {
    if (event.key !== 'Escape') {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.hideMore(row);
    this.moreTrigger(row)?.focus();
  }

  private moreTrigger(row: DownloadRow): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(`[id="${this.moreId(row)}-trigger"]`);
  }

  private hideMore(row: DownloadRow): void {
    const popover = this.host.nativeElement.querySelector<HTMLElement>(`[id="${this.moreId(row)}"]`);
    try {
      popover?.hidePopover();
    } catch {
      // Already hidden.
    }
    if (this.moreOpenKey === row.key) {
      this.moreOpenKey = null;
    }
  }

  // -------------------------------------------------------------------------------------------
  // Nested dialogs
  // -------------------------------------------------------------------------------------------

  /** The nested dialogs' close and cancel events stop here, short of any dialog around the panel. */
  stopNestedEvent(event: Event): void {
    event.stopPropagation();
  }

  /** The PDF viewer closed: focus goes back to the button that opened it. */
  onNestedClosed(): void {
    this.restoreFocus();
  }

  private restoreFocus(): void {
    const target = this.returnFocus;
    this.returnFocus = null;
    if (target?.isConnected) {
      target.focus();
    }
  }

  // -------------------------------------------------------------------------------------------
  // Internals
  // -------------------------------------------------------------------------------------------

  private loadRunDocuments(runId: number, generation: number): void {
    this.loadingDocuments = true;
    this.listSub = this.benchmarkService.listReportDocuments({ runId }).subscribe({
      next: documents => {
        if (generation !== this.generation) {
          return;
        }
        this.addRows(sortDocuments(documents).map(packRow));
        this.loadingDocuments = false;
        this.cdr.markForCheck();
      },
      error: () => {
        if (generation !== this.generation) {
          return;
        }
        this.notices = [...this.notices, 'The report documents of this run could not be loaded; the run files are still listed.'];
        this.loadingDocuments = false;
        this.cdr.markForCheck();
      }
    });
  }

  /**
   * Asks for the run's report writing job after `delayMs`. While it is not finished the notice shows
   * its phase and the job is asked again every 5 s; once it finishes the run's documents are reloaded
   * and the notice goes. No job (204) or a failed first request shows nothing.
   */
  private pollRunReportJob(runId: number, generation: number, delayMs: number): void {
    this.stopReportJobPoll();
    const request$: Observable<ReportJobPoll> = this.benchmarkService.getRunReportJob(runId).pipe(
      map((view): ReportJobPoll => ({ ok: true, view })),
      catchError(() => of<ReportJobPoll>({ ok: false }))
    );
    this.reportJobSub = (delayMs > 0 ? timer(delayMs).pipe(switchMap(() => request$)) : request$)
      .subscribe(result => {
        if (generation !== this.generation) {
          return;
        }
        this.onRunReportJob(result, runId, generation);
        this.cdr.markForCheck();
      });
  }

  private onRunReportJob(result: ReportJobPoll, runId: number, generation: number): void {
    const waiting = this.reportJobPhase !== null;
    if (!result.ok) {
      // A failed tick while waiting is skipped; a failed first request shows nothing.
      if (waiting) {
        this.pollRunReportJob(runId, generation, DOWNLOAD_CENTER_REPORT_JOB_POLL_MS);
      }
      return;
    }
    const view = result.view;
    if (view === null || view.phase === 'Finished') {
      this.reportJobPhase = null;
      if (waiting) {
        this.loadRunDocuments(runId, generation);
      }
      return;
    }
    this.reportJobPhase = view.phase;
    this.pollRunReportJob(runId, generation, DOWNLOAD_CENTER_REPORT_JOB_POLL_MS);
  }

  private stopReportJobPoll(): void {
    this.reportJobSub?.unsubscribe();
    this.reportJobSub = null;
  }

  private loadChosenDocuments(ids: readonly number[], generation: number): void {
    const unique = Array.from(new Set(ids));
    if (unique.length === 0) {
      return;
    }
    this.loadingDocuments = true;
    this.listSub = forkJoin(unique.map(id => this.benchmarkService.getReportDocument(id).pipe(catchError(() => of(null)))))
      .subscribe(details => {
        if (generation !== this.generation) {
          return;
        }
        const documents: BenchmarkReportDocumentListItemDto[] = [];
        const notices: string[] = [];
        details.forEach((detail, index) => {
          if (detail) {
            documents.push(detail);
          } else {
            notices.push(`Report document #${unique[index]} is no longer available.`);
          }
        });
        const listed = documentRows(documents);
        this.notices = [...this.notices, ...notices, ...listed.notices];
        this.addRows(listed.rows);
        this.loadingDocuments = false;
        this.cdr.markForCheck();
      });
  }

  /** One list request for the scope's Report Pack documents, newest first, and their runs' reports. */
  private loadLibrary(context: DownloadCenterLibraryContext, generation: number, keepChoices: boolean): void {
    this.loadingDocuments = true;
    this.listSub?.unsubscribe();
    const scope = context.scope;
    const query = scope.kind === 'comparison'
      ? { comparison: scope.entryKeys, origin: 'reportPack' as const }
      : { origin: 'reportPack' as const, take: REPORT_LIBRARY_ALL_TAKE };
    this.listSub = this.benchmarkService.listReportDocuments(query).subscribe({
      next: documents => {
        if (generation !== this.generation) {
          return;
        }
        const listed = documentRows(sortDocuments(documents ?? []));
        if (keepChoices) {
          this.replaceRows(listed.rows, context.preselect);
          this.notices = listed.notices;
        } else {
          this.notices = [...this.notices, ...listed.notices];
          this.addRows(listed.rows);
          if (context.preselect === 'none') {
            for (const row of listed.rows) {
              this.stateOf(row).selected = false;
            }
          }
        }
        this.loadingDocuments = false;
        this.cdr.markForCheck();
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.generation) {
          return;
        }
        this.notices = [serverMessage(error) ?? 'The report documents could not be loaded.'];
        this.loadingDocuments = false;
        this.cdr.markForCheck();
      }
    });
    this.cdr.markForCheck();
  }

  private addRows(rows: DownloadRow[]): void {
    const stored = readStoredSettings();
    for (const row of rows) {
      if (this.rows.some(existing => existing.key === row.key)) {
        continue;
      }
      this.rows = [...this.rows, row];
      this.states.set(row.key, this.rememberedState(row, this.packageId, this.presetState(row, this.packageId), stored));
    }
  }

  /** The listed rows in place of the current ones; a row still listed keeps its choices. */
  private replaceRows(rows: DownloadRow[], preselect: 'all' | 'none'): void {
    const stored = readStoredSettings();
    const listed = new Set(rows.map(row => row.key));
    for (const key of [...this.states.keys()]) {
      if (!listed.has(key)) {
        this.states.delete(key);
      }
    }
    for (const row of rows) {
      if (!this.states.has(row.key)) {
        const state = this.rememberedState(row, this.packageId, this.presetState(row, this.packageId), stored);
        this.states.set(row.key, preselect === 'none' ? { ...state, selected: false } : state);
      }
    }
    this.rows = rows;
  }

  private isSelectableIn(row: DownloadRow, pkg: DownloadPackageId): boolean {
    if (pkg !== 'provider') {
      return true;
    }
    return row.kind === 'pack' && row.internalReason === null && this.disclosureOptionsFor(row, pkg).length > 0;
  }

  private disclosureOptionsFor(row: DownloadRow, pkg: DownloadPackageId): BenchmarkReportDisclosure[] {
    if (row.kind !== 'pack') {
      return [];
    }
    const allowed = ALL_DISCLOSURES.filter(d => row.allowedDisclosures.includes(d));
    if (pkg === 'internal') {
      return allowed.includes(BenchmarkReportDisclosure.Full) ? [BenchmarkReportDisclosure.Full] : allowed.slice(-1);
    }
    if (pkg === 'provider') {
      return allowed.filter(d => d !== BenchmarkReportDisclosure.Full);
    }
    return allowed;
  }

  private namingOptionsFor(pkg: DownloadPackageId): BenchmarkReportPeerNaming[] {
    return pkg === 'internal'
      ? [BenchmarkReportPeerNaming.Named]
      : [BenchmarkReportPeerNaming.Anonymized, BenchmarkReportPeerNaming.Named];
  }

  /** The package's own choice for a row, before anything remembered is applied. */
  private presetState(row: DownloadRow, pkg: DownloadPackageId): DownloadRowState {
    const options = this.disclosureOptionsFor(row, pkg);
    if (pkg === 'provider') {
      return {
        selected: this.isSelectableIn(row, pkg) && (row.category === 'executiveSummary' || row.category === 'technicalReport'),
        disclosure: options.includes(BenchmarkReportDisclosure.Summary) ? BenchmarkReportDisclosure.Summary : (options[0] ?? BenchmarkReportDisclosure.Summary),
        naming: BenchmarkReportPeerNaming.Anonymized,
        formats: presetFormats(row, 'provider')
      };
    }
    if (pkg === 'custom') {
      const current = this.states.get(row.key);
      if (current) {
        return {
          selected: current.selected,
          disclosure: options.includes(current.disclosure) ? current.disclosure : (options[options.length - 1] ?? current.disclosure),
          naming: current.naming,
          formats: [...current.formats]
        };
      }
    }
    return {
      selected: true,
      disclosure: options[options.length - 1] ?? BenchmarkReportDisclosure.Full,
      naming: BenchmarkReportPeerNaming.Named,
      formats: presetFormats(row, 'internal')
    };
  }

  /** The remembered choice of this package for the row's category, where it is still valid. */
  private rememberedState(
    row: DownloadRow,
    pkg: DownloadPackageId,
    preset: DownloadRowState,
    stored: StoredSettings | null
  ): DownloadRowState {
    const choice = stored?.packages?.[pkg]?.[row.category];
    if (!choice || typeof choice !== 'object') {
      return preset;
    }
    const disclosures = this.disclosureOptionsFor(row, pkg);
    const namings = this.namingOptionsFor(pkg);
    const formats = Array.isArray(choice.formats)
      ? row.formats.filter(format => (choice.formats as unknown[]).includes(format))
      : [];
    return {
      selected: this.isSelectableIn(row, pkg) && (typeof choice.selected === 'boolean' ? choice.selected : preset.selected),
      disclosure: disclosures.includes(choice.disclosure as BenchmarkReportDisclosure)
        ? choice.disclosure as BenchmarkReportDisclosure
        : preset.disclosure,
      naming: namings.includes(choice.naming as BenchmarkReportPeerNaming)
        ? choice.naming as BenchmarkReportPeerNaming
        : preset.naming,
      formats: formats.length > 0 ? formats : preset.formats
    };
  }

  /** Remembers the package and paper used and, per category, the choice of its first row. */
  private persistSettings(): void {
    const choices: Partial<Record<DownloadRowCategory, StoredChoice>> = {};
    for (const row of this.rows) {
      if (choices[row.category] || !this.isSelectable(row)) {
        continue;
      }
      const state = this.stateOf(row);
      choices[row.category] = {
        selected: state.selected,
        disclosure: state.disclosure,
        naming: state.naming,
        formats: [...state.formats]
      };
    }
    const stored = readStoredSettings();
    const settings: StoredSettings = {
      version: STORED_SETTINGS_VERSION,
      package: this.packageId,
      paper: this.paper,
      packages: { ...(stored?.packages ?? {}), [this.packageId]: { ...(stored?.packages?.[this.packageId] ?? {}), ...choices } }
    };
    try {
      localStorage.setItem(DOWNLOAD_CENTER_STORAGE_KEY, JSON.stringify(settings));
    } catch {
      // Storage full or unavailable: the choices are simply not remembered.
    }
  }

  /**
   * The text a row's files are made from, fetched once however many formats use it: the diagnostics
   * are captured once per download, and the same capture feeds their Text, their PDF and their Word
   * document.
   */
  private sourceText(
    row: DownloadRow,
    state: DownloadRowState,
    context: DownloadCenterContext,
    cache: Map<string, Promise<SourceText>>
  ): Promise<SourceText> {
    const key = row.kind === 'pack'
      ? `render:${row.doc!.id}:${state.disclosure}:${state.naming}`
      : `${row.kind}:${row.runId}`;
    let pending = cache.get(key);
    if (!pending) {
      pending = this.fetchText(row, state, context);
      cache.set(key, pending);
    }
    return pending;
  }

  private async fetchText(
    row: DownloadRow,
    state: DownloadRowState,
    context: DownloadCenterContext
  ): Promise<SourceText> {
    switch (row.kind) {
      case 'pack':
        return {
          text: await firstValueFrom(this.benchmarkService.renderReportDocument(row.doc!.id, state.disclosure, state.naming)),
          fileName: null,
          capturedAt: null
        };
      case 'runReport':
        return { ...await firstValueFrom(this.benchmarkService.getRunReportText(row.runId!)), capturedAt: null };
      case 'toolCallLog':
        return { ...await firstValueFrom(this.benchmarkService.getToolCallLogText(row.runId!)), capturedAt: null };
      case 'diagnostics':
        if (context.kind !== 'run') {
          throw new Error('Diagnostics exist only for a run.');
        }
        return { text: context.diagnosticsText(), fileName: null, capturedAt: downloadCenterIo.now() };
    }
  }

  /**
   * A row's PDF or Word document, rendered by the server; the diagnostics' from the same capture as
   * their Text.
   */
  private async binarySource(
    row: DownloadRow,
    state: DownloadRowState,
    context: DownloadCenterContext,
    cache: Map<string, Promise<SourceText>>,
    paper: BenchmarkPdfPaper,
    format: 'pdf' | 'docx'
  ): Promise<SourceBinary> {
    const service = this.benchmarkService;
    const word = format === 'docx';
    switch (row.kind) {
      case 'pack': {
        const { id } = row.doc!;
        const file = word
          ? service.getReportDocumentDocx(id, state.disclosure, state.naming, paper)
          : service.getReportDocumentPdf(id, state.disclosure, state.naming, paper);
        return { ...await firstValueFrom(file), capturedAt: null };
      }
      case 'runReport': {
        const file = word ? service.getRunReportDocx(row.runId!, paper) : service.getRunReportPdf(row.runId!, paper);
        return { ...await firstValueFrom(file), capturedAt: null };
      }
      case 'toolCallLog': {
        const file = word ? service.getToolCallLogDocx(row.runId!, paper) : service.getToolCallLogPdf(row.runId!, paper);
        return { ...await firstValueFrom(file), capturedAt: null };
      }
      case 'diagnostics': {
        const captured = await this.sourceText(row, state, context, cache);
        const capturedAt = captured.capturedAt ?? downloadCenterIo.now();
        const file = word
          ? service.renderDiagnosticsDocx(row.runId!, captured.text, isoSeconds(capturedAt), paper)
          : service.renderDiagnosticsPdf(row.runId!, captured.text, isoSeconds(capturedAt), paper);
        return { ...await firstValueFrom(file), capturedAt };
      }
    }
  }

  private produceFile(
    row: DownloadRow,
    state: DownloadRowState,
    format: DownloadFormat,
    source: SourceText | SourceBinary,
    context: DownloadCenterContext,
    packagedAt: Date,
    paper: BenchmarkPdfPaper
  ): ProducedFile {
    const internal = row.internalReason !== null
      || (row.kind === 'pack' && state.disclosure === BenchmarkReportDisclosure.Full);
    const bytes = 'bytes' in source ? source.bytes : null;
    let text = 'text' in source ? source.text : '';
    let name: string;
    let mtime: Date;
    let createdAtUtc: string | null;

    if (row.kind === 'pack') {
      const doc = row.doc!;
      name = `${reportDocumentFileStem(doc, row.label)}_${reportDisclosureParam(state.disclosure)}_${reportPeerNamingParam(state.naming)}`
        + `${internal ? '_INTERNAL' : ''}.${format}`;
      if (format === 'html') {
        text = markdownToPrintableHtml(text, doc.title || row.label);
      }
      mtime = utcDate(doc.createdAtUtc) ?? packagedAt;
      createdAtUtc = isoSeconds(mtime);
    } else if (row.kind === 'diagnostics') {
      const run = (context as DownloadCenterRunContext).run;
      name = (format === 'pdf' || format === 'docx') && source.fileName
        ? internalServerName(source.fileName, format)
        : `${safeFileName(run.suiteName)}_${safeFileName(run.modelLabel)}_run${run.id}_diagnostics_INTERNAL.${format}`;
      mtime = source.capturedAt ?? packagedAt;
      createdAtUtc = isoSeconds(mtime);
    } else {
      const fallback = row.kind === 'runReport' ? `benchmark_run${row.runId}_report.md` : `benchmark_run${row.runId}_tool_calls.md`;
      name = internalServerName(source.fileName ?? fallback, format);
      if (format === 'html') {
        text = markdownToPrintableHtml(text, `Benchmark run #${row.runId} report`);
      }
      mtime = runFileTime(row, context, source.fileName) ?? packagedAt;
      createdAtUtc = isoSeconds(mtime);
    }

    const common = {
      name,
      mime: MIME_TYPES[format],
      mtime,
      manifest: {
        name,
        content: bytes ?? text,
        format: FORMAT_LABELS[format],
        pdfPaper: format === 'pdf' ? paper : null,
        wordPaper: format === 'docx' ? paper : null,
        description: row.kind === 'pack' ? audienceLabel(row.doc!.audience) : RUN_FILE_DESCRIPTIONS[row.kind],
        documentId: row.doc?.id ?? null,
        audience: row.doc ? audienceLabel(row.doc.audience) : null,
        disclosure: row.kind === 'pack' ? disclosureLabel(state.disclosure) : null,
        naming: row.kind === 'pack' ? (state.naming === BenchmarkReportPeerNaming.Named ? 'Named' : 'Anonymized') : null,
        rendererVersion: row.doc?.reportFormatVersion ?? null,
        createdAtUtc,
        writer: row.doc?.writerDisplayName || null,
        internalOnly: internal
      } satisfies ManifestFile
    };
    return bytes ? { ...common, bytes } : { ...common, text };
  }

  /** `<model>_<package>_<yyyyMMdd_HHmmss>.zip`. */
  private zipFileName(context: DownloadCenterContext, packagedAt: Date): string {
    const model = context.kind === 'run'
      ? context.run.modelLabel
      : (this.rows.find(row => row.doc)?.doc?.subjectLabel ?? 'reports');
    return `${safeFileName(model)}_${safeFileName(this.currentPackage.fullName)}_${exportTimestamp(packagedAt)}.zip`;
  }
}

// ---------------------------------------------------------------------------------------------
// Rows
// ---------------------------------------------------------------------------------------------

const RUN_FILE_DESCRIPTIONS: Record<Exclude<DownloadRowKind, 'pack'>, string> = {
  runReport: 'Run report',
  toolCallLog: 'Tool-call log',
  diagnostics: 'Run diagnostics (captured when the download was prepared)'
};

export const ROW_NOTES = {
  toolCallLog: 'Can run to several megabytes; its PDF and Word files can be hundreds of pages.',
  diagnostics: 'Captured when the download is prepared, not stored.'
} as const;

/** The package's formats that the row offers; every format the row offers where none of them is. */
function presetFormats(row: DownloadRow, pkg: 'internal' | 'provider'): DownloadFormat[] {
  const formats = row.formats.filter(format => PRESET_FORMATS[pkg].includes(format));
  return formats.length > 0 ? formats : [...row.formats];
}

function runFileRows(run: DownloadCenterRunInfo): DownloadRow[] {
  const detail = `${run.suiteName} · ${run.modelLabel}`;
  const base = {
    runId: run.id, doc: null, runChanged: false, allowedDisclosures: [],
    subject: run.modelLabel, suite: run.suiteName, createdAtUtc: run.completedAtUtc ?? run.startedAtUtc
  };
  return [
    {
      ...base, key: `report:${run.id}`, kind: 'runReport', category: 'runReport', documentType: 'Run report',
      label: `Run report, run #${run.id}`, detail, note: null,
      formats: ['pdf', 'docx', 'md', 'html'], internalReason: INTERNAL_REASONS.runReport
    },
    {
      ...base, key: `log:${run.id}`, kind: 'toolCallLog', category: 'toolCallLog', documentType: 'Tool-call log',
      label: `Tool-call log, run #${run.id}`, detail, note: ROW_NOTES.toolCallLog,
      formats: ['pdf', 'docx', 'md'], internalReason: INTERNAL_REASONS.toolCallLog
    },
    {
      ...base, key: `diag:${run.id}`, kind: 'diagnostics', category: 'diagnostics', documentType: 'Run diagnostics',
      label: `Run diagnostics, run #${run.id}`, detail, note: ROW_NOTES.diagnostics,
      formats: ['pdf', 'docx', 'txt'], internalReason: INTERNAL_REASONS.diagnostics
    }
  ];
}

function subjectRunReportRow(runId: number, subjectLabel: string, suiteName: string): DownloadRow {
  return {
    key: `report:${runId}`,
    kind: 'runReport',
    category: 'runReport',
    label: `Run report, run #${runId}`,
    detail: subjectLabel ? `A subject run of ${subjectLabel}` : 'A subject run',
    note: null,
    runId,
    doc: null,
    runChanged: false,
    allowedDisclosures: [],
    formats: ['pdf', 'docx', 'md', 'html'],
    internalReason: INTERNAL_REASONS.runReport,
    subject: subjectLabel,
    suite: suiteName,
    documentType: 'Run report',
    createdAtUtc: null
  };
}

/** The pack rows of the documents, then a run report row per subject run that still exists, and a notice per one that does not. */
function documentRows(documents: readonly BenchmarkReportDocumentListItemDto[]): { rows: DownloadRow[]; notices: string[] } {
  const missing = new Set(documents.flatMap(doc => doc.missingRunIds ?? []));
  const runIds = Array.from(new Set(documents.flatMap(doc => doc.subjectRunIds ?? []))).sort((a, b) => a - b);
  const notices = Array.from(missing).sort((a, b) => a - b)
    .map(runId => `Run #${runId} no longer exists, so its run report is not listed.`);
  const rows = documents.map(packRow);
  for (const runId of runIds) {
    if (!missing.has(runId)) {
      const owner = documents.find(doc => doc.subjectRunIds.includes(runId));
      rows.push(subjectRunReportRow(runId, owner?.subjectLabel ?? '', owner?.suiteName ?? ''));
    }
  }
  return { rows, notices };
}

/** The levels a document renders at when the server sends none: its audience's own set. */
function fallbackDisclosures(audience: BenchmarkReportAudience): BenchmarkReportDisclosure[] {
  switch (audience) {
    case BenchmarkReportAudience.InternalBrief: return [BenchmarkReportDisclosure.Full];
    case BenchmarkReportAudience.ExecutiveSummary: return [BenchmarkReportDisclosure.Summary, BenchmarkReportDisclosure.Full];
    default: return [...ALL_DISCLOSURES];
  }
}

function packRow(doc: BenchmarkReportDocumentListItemDto): DownloadRow {
  const allowed = doc.allowedDisclosures && doc.allowedDisclosures.length > 0
    ? doc.allowedDisclosures
    : fallbackDisclosures(doc.audience);
  const shareable = doc.audience !== BenchmarkReportAudience.InternalBrief
    && allowed.some(d => d !== BenchmarkReportDisclosure.Full);
  const type = audienceLabel(doc.audience);
  const parts = [type, doc.writerDisplayName ? `written by ${doc.writerDisplayName}` : ''].filter(part => part);
  return {
    key: `doc:${doc.id}`,
    kind: 'pack',
    category: doc.audience === BenchmarkReportAudience.ExecutiveSummary ? 'executiveSummary'
      : doc.audience === BenchmarkReportAudience.TechnicalReport ? 'technicalReport' : 'internalBrief',
    label: doc.title || `${type}: ${doc.subjectLabel}`,
    detail: parts.join(' · '),
    note: null,
    runId: null,
    doc,
    runChanged: !!doc.runChangedSinceGeneration,
    allowedDisclosures: allowed,
    formats: ['pdf', 'docx', 'md', 'html'],
    internalReason: shareable ? null : INTERNAL_REASONS.internalBrief,
    subject: doc.subjectLabel,
    suite: doc.suiteName ?? '',
    documentType: type,
    createdAtUtc: doc.createdAtUtc ?? null
  };
}

/** Newest first, then by audience, so one pack's documents stay together. */
function sortDocuments(documents: readonly BenchmarkReportDocumentListItemDto[]): BenchmarkReportDocumentListItemDto[] {
  return [...documents].sort((a, b) =>
    (b.createdAtUtc ?? '').localeCompare(a.createdAtUtc ?? '') || a.audience - b.audience || a.id - b.id);
}

/** `<subject> · compared with N models · by <writer> on <date>`. */
function documentViewerSubtitle(doc: BenchmarkReportDocumentListItemDto): string {
  const parts = [doc.subjectLabel];
  const peers = doc.peerCount ?? 0;
  if (peers > 0) {
    parts.push(`compared with ${peers} ${peers === 1 ? 'model' : 'models'}`);
  }
  parts.push(`by ${doc.writerDisplayName || 'the report writer'} on ${formatUtc(doc.createdAtUtc) || 'an unknown date'}`);
  return parts.join(' · ');
}

// ---------------------------------------------------------------------------------------------
// Names, times and messages
// ---------------------------------------------------------------------------------------------

/** The audience suffixes a Report for AI Researchers and Developers title can end in, current and earlier. */
const RESEARCHER_REPORT_TITLE_SUFFIX = /\s+[—–-]\s+(?:Report for AI Researchers and Developers|Technical Report)\s*$/;

/** A single run's subject key, `run:<digits>`, as the server's `BenchmarkPdfFileNames` matches it. */
const RUN_SUBJECT_KEY = /^run:([0-9]+)$/;

/**
 * A report document's file-name stem. A Report for AI Researchers and Developers is named by its
 * title without the audience suffix, then `_Researcher_Report`; every other document by its title.
 * A document about one run (subject `run:<digits>`) is prefixed `run-<digits>_`, and a document
 * compared with peers `vs-<peer count>-models_` after that (first, for a group subject), as the
 * server's `BenchmarkPdfFileNames.ForReportDocument` names its PDF and Word files.
 */
export function reportDocumentFileStem(doc: BenchmarkReportDocumentListItemDto, fallbackTitle: string): string {
  const title = doc.title || fallbackTitle;
  const run = RUN_SUBJECT_KEY.exec(doc.subjectKey ?? '');
  const peers = doc.peerCount ?? 0;
  const prefix = (run ? `run-${run[1]}_` : '') + (peers > 0 ? `vs-${peers}-models_` : '');
  if (doc.audience !== BenchmarkReportAudience.TechnicalReport) {
    return `${prefix}${safeFileName(title)}`;
  }
  return `${prefix}${safeFileName(title.replace(RESEARCHER_REPORT_TITLE_SUFFIX, ''))}_Researcher_Report`;
}

function disclosureLabel(disclosure: BenchmarkReportDisclosure): string {
  switch (disclosure) {
    case BenchmarkReportDisclosure.Summary: return 'Summary';
    case BenchmarkReportDisclosure.Detailed: return 'Detailed';
    default: return 'Full';
  }
}

/**
 * The server's file name with `_INTERNAL` before the extension, unless it already ends so, and the
 * chosen format's extension. Its case is kept; only characters a file system refuses, and path
 * separators, are replaced.
 */
export function internalServerName(serverName: string, format: DownloadFormat): string {
  const clean = serverName.replace(/[\\/:*?"<>|\u0000-\u001f]/g, '_');
  const dot = clean.lastIndexOf('.');
  const base = dot > 0 ? clean.slice(0, dot) : clean;
  return `${base.endsWith('_INTERNAL') ? base : `${base}_INTERNAL`}.${format}`;
}

/** An ISO date from the server, read as UTC when it carries no offset. */
function utcDate(value: string | null | undefined): Date | null {
  if (!value) {
    return null;
  }
  const hasZone = /(Z|[+-]\d{2}:?\d{2})$/i.test(value);
  const date = new Date(hasZone ? value : `${value}Z`);
  return Number.isNaN(date.getTime()) ? null : date;
}

function isoSeconds(date: Date): string {
  return date.toISOString().replace(/\.\d{3}Z$/, 'Z');
}

/**
 * A run file's time: the run's completion, else its start, in run context. Elsewhere the run itself
 * is not fetched, so the start time is read from the report's server file name
 * (`…_yyyyMMdd_HHmmss.md`, or `…_yyyyMMdd_HHmmss_INTERNAL.pdf`, UTC), and null makes the caller use
 * the packaging time.
 */
function runFileTime(row: DownloadRow, context: DownloadCenterContext, serverName: string | null): Date | null {
  if (context.kind === 'run' && context.run.id === row.runId) {
    return utcDate(context.run.completedAtUtc) ?? utcDate(context.run.startedAtUtc);
  }
  const match = /_(\d{4})(\d{2})(\d{2})_(\d{2})(\d{2})(\d{2})(?:_INTERNAL)?\.[A-Za-z0-9]+$/.exec(serverName ?? '');
  if (!match) {
    return null;
  }
  const [, y, mo, d, h, mi, s] = match.map(Number);
  return new Date(Date.UTC(y, mo - 1, d, h, mi, s));
}

function failureReason(error: unknown, row: DownloadRow): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 404) {
      return row.kind === 'pack' ? 'the document no longer exists' : 'the run no longer exists';
    }
    if (error.status === 0) {
      return 'the server could not be reached';
    }
    const body = typeof error.error === 'string' ? safeJson(error.error) : error.error;
    const message = body && typeof body === 'object' && typeof (body as { error?: unknown }).error === 'string'
      ? (body as { error: string }).error
      : null;
    return message ?? `the server answered ${error.status}`;
  }
  return error instanceof Error && error.message ? error.message : 'it could not be prepared';
}

/** The `{ error }` or plain-string message of a refused request, or null. */
function serverMessage(error: HttpErrorResponse): string | null {
  const body = error?.error;
  if (typeof body === 'string' && body.trim()) {
    return body.trim();
  }
  if (body && typeof body === 'object') {
    const message = (body as { error?: unknown; message?: unknown }).error ?? (body as { message?: unknown }).message;
    if (typeof message === 'string' && message.trim()) {
      return message.trim();
    }
  }
  return null;
}

function safeJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function completionMessage(planned: number, produced: number, failed: number): string {
  if (produced === 0) {
    return planned === 1 ? 'The file could not be prepared; nothing was downloaded.' : 'No file could be prepared; nothing was downloaded.';
  }
  if (planned === 1) {
    return 'Downloaded 1 file.';
  }
  return failed === 0
    ? `Downloaded ${produced} files as one ZIP.`
    : `Downloaded ${produced} of ${planned} files as one ZIP; ${failed} failed.`;
}

/** The paper the Download Center last used, for a PDF opened elsewhere; A4 when none is remembered. */
export function rememberedPdfPaper(): BenchmarkPdfPaper {
  return readStoredSettings()?.paper ?? 'a4';
}

/**
 * The stored settings, or null when absent, unreadable, corrupt or of another version. Version 2
 * reads as version 3 without the formats remembered for the Internal package, so its rows take the
 * Internal preset.
 */
function readStoredSettings(): StoredSettings | null {
  try {
    const raw = localStorage.getItem(DOWNLOAD_CENTER_STORAGE_KEY);
    if (!raw) {
      return null;
    }
    const parsed = JSON.parse(raw) as { version?: unknown; package?: unknown; paper?: unknown; packages?: unknown } | null;
    if (!parsed || typeof parsed !== 'object' || (parsed.version !== STORED_SETTINGS_VERSION && parsed.version !== 2)) {
      return null;
    }
    const pkg = DOWNLOAD_PACKAGES.find(p => p.id === parsed.package)?.id;
    const paper = PDF_PAPERS.find(p => p.id === parsed.paper)?.id;
    let packages = parsed.packages && typeof parsed.packages === 'object'
      ? parsed.packages as NonNullable<StoredSettings['packages']>
      : undefined;
    if (parsed.version === 2 && packages?.internal && typeof packages.internal === 'object') {
      const internal: Partial<Record<DownloadRowCategory, StoredChoice>> = {};
      for (const [category, choice] of Object.entries(packages.internal)) {
        if (choice && typeof choice === 'object') {
          const kept: StoredChoice = { ...choice };
          delete kept.formats;
          internal[category as DownloadRowCategory] = kept;
        }
      }
      packages = { ...packages, internal };
    }
    return { version: STORED_SETTINGS_VERSION, package: pkg, paper, packages };
  } catch {
    return null;
  }
}
