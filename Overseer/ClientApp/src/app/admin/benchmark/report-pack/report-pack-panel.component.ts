import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { EMPTY, Subject, Subscription, catchError, debounceTime, interval, map, of, switchMap } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkReportAudience,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPackAudienceEstimateDto,
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkReportPackJobDto,
  BenchmarkReportPackPreviewDto,
  BenchmarkReportPackPricingBasis,
  BenchmarkReportPackRequest,
  BenchmarkReportScope,
  SameProviderWarningDto
} from '../../../services/admin-benchmark.service';
import { AdminService, SystemAiConfigDto } from '../../../services/admin.service';
import { ModelPickerComponent, ModelPickerKey, ModelPickerOption, toModelPickerOptions } from '../../../shared/model-picker/model-picker.component';
import { ModelMultiPickerComponent } from '../../../shared/model-picker/model-multi-picker.component';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { PdfViewerDialogComponent } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { rememberedPdfPaper } from '../download-center/download-center-panel.component';
import { RunReportFrameComponent } from '../run-report-frame/run-report-frame.component';
import { REPORT_PACK_WRITER_ADVICE, REPORT_WRITER_ADVICE_LEAD } from '../run-ai-reports/report-writer-advice';
import type { ClientPollError } from '../run-ai-reports/run-report-writing-diagnostics';
import { formatUtcSeconds } from '../run-ai-reports/run-report-writing-diagnostics';
import type {
  BenchmarkModelComparisonEntryDto,
  BenchmarkModelComparisonPricingBasis
} from '../model-comparison/model-comparison.models';
import {
  ComparisonDocumentRow,
  ComparisonDocumentsStatusComponent,
  ComparisonDocumentsView,
  ReportDocumentScopeMode,
  comparisonDocumentsView
} from './comparison-documents-status.component';
import {
  ComparisonModelOption,
  MAX_COMPARISON_DOCUMENT_ENTRIES,
  MIN_COMPARISON_DOCUMENT_ENTRIES,
  comparisonModelMention,
  comparisonModelOptions,
  defaultCoveredKeys,
  defaultSubjectKey
} from './comparison-model-options';
import { ReportChartPickerComponent } from './report-chart-picker.component';
import { DocumentChartsComposer, ReportChartLayoutPreviewFailure, layoutPreviewViewerRequest } from './report-chart-layout-preview';
import {
  REPORT_CHART_FIGURES,
  ReportChartLayoutSettings,
  ReportChartRowStatus,
  ReportChartScope,
  ReportChartSelection
} from './report-charts';
import {
  REPORT_PACK_AUDIENCES,
  audienceLabel,
  documentChipClass,
  formatCostUsd,
  formatElapsed,
  formatUtc,
  statusLabel
} from './report-document-format';
import { buildReportPackDiagnostics, reportPackDiagnosticsFileName, reportPackIo } from './report-pack-diagnostics';

/** What the Reports step works on: the comparison request, its entries and its suite. */
export interface ReportPackContext {
  readonly runIds: readonly number[];
  readonly groupIds: readonly number[];
  /** The battery results of a comparison of battery results; never beside runs or groups. Absent reads as none. */
  readonly batteryRunIds?: readonly number[];
  readonly pricingBasis: BenchmarkModelComparisonPricingBasis;
  /** Every entry of the comparison; the non-Excluded ones are the models a document can cover. */
  readonly entries: readonly BenchmarkModelComparisonEntryDto[];
  /**
   * Every entry's key (`run:<id>`, `group:<id>`, `battery:<id>`), Excluded ones included: the set the
   * server's comparison key is computed from, the same sources the request carries.
   */
  readonly entryKeys: readonly string[];
  readonly suiteId: number | null;
  readonly suiteName: string | null;
}

/** One stage of the job's rail: Queued, Preparing, one per document, Done. */
export interface ReportPackJobStage {
  readonly key: string;
  readonly name: string;
  readonly state: 'done' | 'current' | 'pending';
}

/** The estimate panel: waiting, failed, no price for the writer, or the total with its parts. */
export interface ReportPackEstimateView {
  readonly state: 'loading' | 'failed' | 'noPrice' | 'ready';
  readonly total: string | null;
  /** One entry per document, only when there are two or more. */
  readonly parts: readonly { name: string; cost: string }[];
}

/** The charts cell of a document's progress row: its words, which are the retry button's after a failure. */
export interface ReportPackChartCell {
  readonly text: string;
  readonly retry: boolean;
}

/** One row of the document progress list. */
export interface ReportPackDocumentRow {
  /** `<subject key>|<audience>`. */
  readonly key: string;
  readonly doc: BenchmarkReportPackDocumentProgressDto;
  readonly audience: BenchmarkReportAudience;
  readonly name: string;
  /** The model the document is about; the comparison's label for a comparison-scope document. */
  readonly subjectLabel: string;
  readonly statusWord: string;
  readonly chipClass: string;
  readonly duration: string;
  readonly modelCalls: string;
  readonly errorMessage: string | null;
  readonly charts: ReportPackChartCell;
}

/** The two document scopes, as the segmented tab row offers them; `note` is a second, smaller line. */
export const REPORT_PACK_SCOPE_OPTIONS: readonly {
  readonly value: ReportDocumentScopeMode; readonly label: string; readonly note?: string;
}[] = [
  { value: 'comparison', label: 'Whole comparison', note: 'Recommended' },
  { value: 'model', label: 'One model at a time' }
];

/** The info tip of *One model at a time*. */
export const REPORT_PACK_MODEL_SCOPE_TIP =
  'Per-model documents restate the comparison from one model\'s side. Use them only when one model\'s document must be shared on its own.';

/** The wait after the last change of models, documents or writer before the preview is requested. */
export const REPORT_PACK_PREVIEW_DEBOUNCE_MS = 300;

/** The interval between two progress requests while a job runs. */
export const REPORT_PACK_POLL_MS = 2000;

/** The longest wait between progress requests after repeated failures. */
export const REPORT_PACK_POLL_MAX_BACKOFF_MS = 30000;

/** The per-viewer memory of the last writer used, the document scope and the sidebar's width. */
export const REPORT_PACK_STORAGE_KEY = 'overseer.benchmark.reportPack';

/** The estimate warns from this share of the writer's context window; the server refuses from 0.9. */
export const REPORT_PACK_CONTEXT_WARNING_SHARE = 0.7;

/** The share of the writer's context window the server refuses from. */
export const REPORT_PACK_CONTEXT_REFUSAL_SHARE = 0.9;

/** The warning shown when the server has no chart folder. */
export const REPORT_PACK_CHART_STORAGE_MISSING_TEXT =
  'Chart storage is not configured; documents will be written without charts.';

/** The paragraph under the step heading: where these reports are kept, and where a run's own reports are written. */
export const REPORT_PACK_LEAD_TEXT =
  'These reports are kept with the comparison: step 4 lists them, and so does Comparison reports on the Model '
  + 'Comparison tab. A run\'s or battery run\'s own reports are written in the AI Reports tab of its report.';

/** The server's refusal of a subject with no peer, word for word (`BenchmarkReportPackPreparation.PeerlessReportRefusal`). */
export const REPORT_PACK_PEERLESS_REFUSAL =
  'A comparison report compares one model with at least one other. To write a run\'s or a battery run\'s own '
  + 'reports, use the AI Reports tab of its report.';

/** Why Generate is unavailable when every listed document is written and none is chosen to be rewritten. */
export const REPORT_PACK_ALL_WRITTEN_REASON =
  'Every document listed is already written. Check Rewrite on one to replace it, or delete it.';

/** Why Generate is unavailable when the checked documents are not the same for every model checked. */
export const REPORT_PACK_UNEVEN_MODELS_REASON =
  'One job writes the same documents for every model it covers. Check the same documents for each model, or write them in separate jobs.';

/** The job statuses after which nothing changes. */
const FINISHED_JOB_STATUSES = new Set(['Completed', 'CompletedWithErrors', 'Canceled', 'Failed']);

/** A document's statuses once it is written or given up. */
const FINISHED_DOCUMENT_STATUSES = new Set(['Completed', 'CompletedWithWarnings', 'Failed', 'Canceled']);

/** A document's statuses once it is written. */
const WRITTEN_DOCUMENT_STATUSES = new Set(['Completed', 'CompletedWithWarnings']);

/** A document's statuses while the writer works on it. */
const ACTIVE_DOCUMENT_STATUSES = new Set(['Writing', 'Repairing']);

const numberFormat = new Intl.NumberFormat('en-US');

function plural(count: number, one: string, many: string): string {
  return `${count} ${count === 1 ? one : many}`;
}

function isJobDto(body: unknown): body is BenchmarkReportPackJobDto {
  return !!body && typeof body === 'object'
    && typeof (body as BenchmarkReportPackJobDto).id === 'string'
    && Array.isArray((body as BenchmarkReportPackJobDto).documents);
}

function isSameProviderWarning(body: unknown): body is SameProviderWarningDto {
  return !!body && typeof body === 'object' && typeof (body as SameProviderWarningDto).message === 'string';
}

/** The `{ error }` or plain-string message of a refused request, or null. */
function serverMessage(error: HttpErrorResponse): string | null {
  const body = error.error;
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

/** The panel's stored record: the last writer used, the document scope and the sidebar's width. */
interface StoredReportPackSettings {
  writerConfigId?: number;
  sidebarWidth?: number;
  documentScope?: ReportDocumentScopeMode;
}

function readStoredSettings(): StoredReportPackSettings {
  try {
    const raw = localStorage.getItem(REPORT_PACK_STORAGE_KEY);
    if (!raw) {
      return {};
    }
    const parsed = JSON.parse(raw) as { writerConfigId?: unknown; sidebarWidth?: unknown; documentScope?: unknown } | null;
    const settings: StoredReportPackSettings = {};
    if (typeof parsed?.writerConfigId === 'number') {
      settings.writerConfigId = parsed.writerConfigId;
    }
    if (typeof parsed?.sidebarWidth === 'number' && Number.isFinite(parsed.sidebarWidth)) {
      settings.sidebarWidth = parsed.sidebarWidth;
    }
    if (parsed?.documentScope === 'comparison' || parsed?.documentScope === 'model') {
      settings.documentScope = parsed.documentScope;
    }
    return settings;
  } catch {
    return {};
  }
}

function writeStoredSettings(patch: StoredReportPackSettings): void {
  try {
    localStorage.setItem(REPORT_PACK_STORAGE_KEY, JSON.stringify({ ...readStoredSettings(), ...patch }));
  } catch {
    // Storage unavailable: the choice is simply not remembered.
  }
}

/** One preview request in the debounced queue, with what it was asked for. */
interface PreviewTicket {
  readonly id: number;
  readonly generation: number;
  readonly request: BenchmarkReportPackRequest;
  /** The request as JSON: what the estimate answers. */
  readonly signature: string;
  /** The scope and models: what the document list answers. */
  readonly listSignature: string;
}

interface PreviewAnswer {
  readonly ticket: PreviewTicket;
  readonly preview: BenchmarkReportPackPreviewDto | null;
  readonly error: HttpErrorResponse | null;
}

/** A checked row and the estimate the preview gave for it. */
interface CheckedEstimate {
  readonly row: ComparisonDocumentRow;
  readonly estimate: BenchmarkReportPackAudienceEstimateDto;
}

/**
 * Step 3 of the Model Comparison wizard, *Reports*: a sidebar that starts a report pack, either
 * comparison-wide documents over the chosen models (a subset when not every model is chosen) or the
 * per-model documents of each chosen model, with every document the choice can have listed as
 * written or not; and a main area that follows the job, one row per document, and its log and
 * diagnostics. The written documents are also step 4's.
 *
 * The wizard creates it once and hides it while another step shows, so the job's polling and the
 * one-second clock go on while it is hidden; both stop in `ngOnDestroy`. The form resets only when
 * the comparison's entry keys change, and then the running job, if any, is found again.
 */
@Component({
  selector: 'app-report-pack-panel',
  standalone: true,
  imports: [
    RunReportFrameComponent, ModelPickerComponent, ModelMultiPickerComponent, InfoTipComponent, ReportChartPickerComponent,
    ComparisonDocumentsStatusComponent, PdfViewerDialogComponent
  ],
  templateUrl: './report-pack-panel.component.html',
  styleUrls: ['./report-pack-panel.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReportPackPanelComponent implements OnInit, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly adminService = inject(AdminService);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('sameProviderDialog') sameProviderDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('generateButton') generateButton?: ElementRef<HTMLButtonElement>;
  /** The layout preview's viewer. */
  @ViewChild(PdfViewerDialogComponent) layoutViewer?: PdfViewerDialogComponent;

  /** The comparison; the form resets only when its entry keys change. */
  @Input()
  set context(value: ReportPackContext | null) {
    this.applyContext(value ?? null);
  }
  get context(): ReportPackContext | null {
    return this.currentContext;
  }

  /** The numbered comparison (*Comparison #12*); null until the wizard has it. */
  @Input()
  set comparisonId(value: number | null) {
    const id = value ?? null;
    if (id === this.currentComparisonId) {
      return;
    }
    this.currentComparisonId = id;
    this.refreshListItems(true);
  }
  get comparisonId(): number | null {
    return this.currentComparisonId;
  }

  /** The charts each document type carries; the wizard stores every change. */
  @Input() chartSelection: ReportChartSelection = {};

  /** How each document type's charts are drawn and placed; null shows no layout controls. */
  @Input() chartLayout: ReportChartLayoutSettings | null = null;

  /** The wizard's composition of a document type's charts, for Preview layout; null offers none. */
  @Input() documentChartsComposer: DocumentChartsComposer | null = null;

  /** The figure keys the comparison can draw. */
  @Input() chartsAvailable: readonly string[] = [];

  /** Why the charts may not match what the documents say (D10), or null. */
  @Input() chartAdvisory: string | null = null;

  /** The server has no chart folder: documents are written without charts. */
  @Input() chartStorageMissing = false;

  /** Each written document's chart state, keyed by document id. A finished chart set lists the documents again. */
  @Input()
  set chartStatus(value: Readonly<Record<number, ReportChartRowStatus>>) {
    const previous = this.currentChartStatus;
    this.currentChartStatus = value ?? {};
    const charted = Object.entries(this.currentChartStatus)
      .some(([id, status]) => status.state === 'done' && previous[Number(id)]?.state !== 'done');
    if (charted) {
      this.refreshListItems(true);
    }
  }
  get chartStatus(): Readonly<Record<number, ReportChartRowStatus>> {
    return this.currentChartStatus;
  }

  /** The prefix of every id in the panel, so the wizard can label its step region by `${idPrefix}-heading`. */
  @Input() idPrefix = 'rp';

  /** The writer field's *How the graders work* link: the host opens the guide at *Choosing grader models*. */
  @Output() readonly graderGuideRequested = new EventEmitter<void>();

  /** A document reached Completed or Completed with warnings; once per document id. */
  @Output() readonly documentWritten = new EventEmitter<BenchmarkReportPackDocumentProgressDto>();

  /** The job followed here finished; once per job. */
  @Output() readonly jobFinished = new EventEmitter<BenchmarkReportPackJobDto>();

  /** *See the documents* after a job: the host shows step 4. */
  @Output() readonly documentsRequested = new EventEmitter<void>();

  /** A document was deleted from the list: the host lists its documents again. */
  @Output() readonly documentsChanged = new EventEmitter<void>();

  /** The chart picker's change, normalized. */
  @Output() readonly chartSelectionChange = new EventEmitter<ReportChartSelection>();

  /** The chart picker's layout change, normalized. */
  @Output() readonly chartLayoutChange = new EventEmitter<ReportChartLayoutSettings>();

  /** *Charts failed — retry* on a document's row. */
  @Output() readonly chartRetryRequested = new EventEmitter<BenchmarkReportPackDocumentProgressDto>();

  /** Whether a job is running, emitted on every change. */
  @Output() readonly busyChange = new EventEmitter<boolean>();

  readonly audiences = REPORT_PACK_AUDIENCES;
  readonly scopeOptions = REPORT_PACK_SCOPE_OPTIONS;
  readonly modelScopeTip = REPORT_PACK_MODEL_SCOPE_TIP;
  readonly audienceLabel = audienceLabel;
  readonly statusLabel = statusLabel;
  readonly formatCostUsd = formatCostUsd;
  readonly formatUtc = formatUtc;
  readonly writerAdvice = REPORT_PACK_WRITER_ADVICE;
  readonly writerAdviceLead = REPORT_WRITER_ADVICE_LEAD;
  readonly chartStorageMissingText = REPORT_PACK_CHART_STORAGE_MISSING_TEXT;
  readonly leadText = REPORT_PACK_LEAD_TEXT;
  readonly writerEmptyHint =
    'No system AI configs with the Benchmark role are enabled. Enable the Benchmark role in System Configs.';

  private currentContext: ReportPackContext | null = null;
  private currentComparisonId: number | null = null;
  private currentChartStatus: Readonly<Record<number, ReportChartRowStatus>> = {};
  /** The entry keys the form was last reset for. */
  private contextKey: string | null = null;

  // --- New report pack ---
  /** Whole comparison (comparison-scope documents) or one model at a time (per-model documents). */
  scopeMode: ReportDocumentScopeMode = readStoredSettings().documentScope ?? 'comparison';
  /** The Models picker's options: every entry that is not Excluded. */
  modelOptions: ComparisonModelOption[] = [];
  /** The models chosen under Whole comparison, in the options' order. */
  private coveredKeys: string[] = [];
  /** The models chosen under One model at a time, in the options' order. */
  private subjectKeys: string[] = [];
  /** The admin's own Write and Rewrite choices, by row key; a row without one takes its default. */
  private readonly checks = new Map<string, boolean>();
  /** The comparison's stored documents, by id, for each row's flags and chart count. */
  private listItems = new Map<number, BenchmarkReportDocumentListItemDto>();
  /** The list built from the last preview answered for the current scope and models. */
  documentsView: ComparisonDocumentsView = comparisonDocumentsView({
    mode: 'comparison', preview: null, error: null, chosenKeys: [], offeredKeys: [], labelOf: key => key,
    listItems: new Map(), checks: new Map()
  });
  /** The document types a checked row has, for the chart picker. */
  checkedAudiences: BenchmarkReportAudience[] = [];
  /** The document type the chart picker shows; null for its own default. */
  private chartAudience: BenchmarkReportAudience | null = null;

  writers: SystemAiConfigDto[] = [];
  writersLoading = false;
  writersError: string | null = null;
  writerId: number | null = null;
  private writersLoaded = false;
  private writerOptionsSource: SystemAiConfigDto[] | null = null;
  private writerOptionsCache: ModelPickerOption<SystemAiConfigDto>[] = [];

  /** The last preview answered for the current scope and models; its estimate is current while nothing is pending. */
  preview: BenchmarkReportPackPreviewDto | null = null;
  /** The scope and models `preview` answers. */
  private previewListSignature: string | null = null;
  /** A change is waiting out the debounce, or its preview is in flight. */
  previewPending = false;
  previewError: string | null = null;
  /** The same-provider warning a 409 carried, shown until the models or writer change. */
  private serverWarning: string | null = null;
  /** The same-provider confirmation's sentence. */
  confirmWarningText = '';

  /** Which figures the last layout preview could not draw. */
  layoutPreviewNote = '';
  private layoutPreviewReturnFocus: HTMLElement | null = null;

  starting = false;
  startError: string | null = null;

  /** The job this panel follows, and its last known state. */
  activeJobId: string | null = null;
  job: BenchmarkReportPackJobDto | null = null;
  /** When `job` was last read (client clock), to run the server's clock on between readings. */
  jobReadAt = 0;
  /** The estimate when the job was started here, for its stat strip. */
  jobEstimateUsd: number | null = null;
  pollError: string | null = null;
  canceling = false;

  // --- Client polling, for the diagnostics ---
  pollCount = 0;
  lastSuccessUtc: Date | null = null;
  lastPollError: ClientPollError | null = null;

  copyStatus = '';
  copyError: string | null = null;

  /** The sidebar's width in CSS px, as last stored; null for the frame's default. */
  sidebarWidth: number | null = readStoredSettings().sidebarWidth ?? null;

  /** Bumped on every context reset and on destroy, so a response for an earlier comparison never lands. */
  private generation = 0;
  private previewTicketId = 0;
  private latestTicket: PreviewTicket | null = null;
  private readonly previewQueue = new Subject<PreviewTicket | null>();
  /** Debounced; a newer ticket drops the request in flight, and an answer for an older one is ignored. */
  private readonly previewPipeline: Subscription = this.previewQueue.pipe(
    debounceTime(REPORT_PACK_PREVIEW_DEBOUNCE_MS),
    switchMap(ticket => ticket
      ? this.benchmarkService.previewReportPack(ticket.request).pipe(
        map((preview): PreviewAnswer => ({ ticket, preview, error: null })),
        catchError((error: HttpErrorResponse) => of<PreviewAnswer>({ ticket, preview: null, error })))
      : EMPTY)
  ).subscribe(answer => this.onPreviewAnswer(answer));
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private pollFailures = 0;
  private tickSub: Subscription | null = null;
  private writersSub: Subscription | null = null;
  private listSub: Subscription | null = null;
  private destroyed = false;
  private lastBusy = false;
  /** The documents already announced by `documentWritten`, and the jobs by `jobFinished`. */
  private readonly writtenDocumentIds = new Set<number>();
  private readonly finishedJobIds = new Set<string>();
  private readonly subscriptions: Record<string, Subscription | undefined> = {};

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.loadWriters();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.generation++;
    this.stopTimers();
    this.stopTick();
    this.previewPipeline.unsubscribe();
    this.listSub?.unsubscribe();
    this.listSub = null;
    this.writersSub?.unsubscribe();
    this.writersSub = null;
  }

  // -------------------------------------------------------------------------------------------
  // The comparison
  // -------------------------------------------------------------------------------------------

  /**
   * A new comparison resets the form (the default models, the remembered writer) and looks for a
   * running job; the same entry keys only update the request's facts, keeping the form and the job.
   */
  private applyContext(context: ReportPackContext | null): void {
    const key = context ? context.entryKeys.join(',') : null;
    const previous = this.currentContext;
    this.currentContext = context;
    if (this.destroyed) {
      return;
    }
    if (key === this.contextKey) {
      if (context && previous) {
        const modelsChanged = this.refreshOptions(context);
        if ((modelsChanged || context.pricingBasis !== previous.pricingBasis) && !this.jobInProgress) {
          this.schedulePreview();
        }
      }
      this.cdr.markForCheck();
      return;
    }
    this.contextKey = key;
    this.resetForm(context);
  }

  /**
   * The options again, for the same entries: a model no longer offered leaves the choice, and a
   * whole-comparison choice that held every model holds every model again. True when the choice changed.
   */
  private refreshOptions(context: ReportPackContext): boolean {
    const before = this.modelOptions.map(option => String(option.key));
    const hadAll = before.length > 0 && defaultCoveredKeys(this.modelOptions).every(key => this.coveredKeys.includes(key));
    this.modelOptions = comparisonModelOptions(context.entries);
    const offered = this.modelOptions.map(option => String(option.key));
    if (offered.join(',') === before.join(',')) {
      return false;
    }
    this.coveredKeys = hadAll ? defaultCoveredKeys(this.modelOptions) : offered.filter(key => this.coveredKeys.includes(key));
    if (this.coveredKeys.length < MIN_COMPARISON_DOCUMENT_ENTRIES) {
      this.coveredKeys = defaultCoveredKeys(this.modelOptions);
    }
    this.subjectKeys = offered.filter(key => this.subjectKeys.includes(key));
    if (this.subjectKeys.length === 0) {
      const best = defaultSubjectKey(this.modelOptions);
      this.subjectKeys = best ? [best] : [];
    }
    this.checks.clear();
    return true;
  }

  private resetForm(context: ReportPackContext | null): void {
    this.generation++;
    this.stopTimers();
    this.stopTick();
    this.modelOptions = context ? comparisonModelOptions(context.entries) : [];
    this.coveredKeys = defaultCoveredKeys(this.modelOptions);
    const best = defaultSubjectKey(this.modelOptions);
    this.subjectKeys = best ? [best] : [];
    this.checks.clear();
    this.listItems = new Map();
    this.writerId = null;
    this.preview = null;
    this.previewListSignature = null;
    this.previewPending = false;
    this.previewError = null;
    this.serverWarning = null;
    this.confirmWarningText = '';
    this.layoutPreviewNote = '';
    this.starting = false;
    this.startError = null;
    this.activeJobId = null;
    this.job = null;
    this.jobEstimateUsd = null;
    this.pollError = null;
    this.pollFailures = 0;
    this.pollCount = 0;
    this.lastSuccessUtc = null;
    this.lastPollError = null;
    this.canceling = false;
    this.copyStatus = '';
    this.copyError = null;
    this.sidebarWidth = readStoredSettings().sidebarWidth ?? null;
    // Reached from an input setter, inside the host's change detection: the event waits for it to end.
    this.syncRunning(true);

    if (context) {
      this.applyRememberedWriter();
      this.schedulePreview();
      this.loadActiveJob(this.generation);
    } else {
      this.refreshDocumentsView();
    }
    this.cdr.markForCheck();
  }

  /** `<suite> · N models`, and how many of them are Excluded. */
  get subtitle(): string {
    const suite = this.currentContext?.suiteName || 'Suite not set';
    const models = this.currentContext?.entries.length ?? 0;
    const excluded = models - this.modelOptions.length;
    const parts = [suite, plural(models, 'model', 'models')];
    if (excluded > 0) {
      parts.push(`${excluded} Excluded`);
    }
    return parts.join(' · ');
  }

  get headingId(): string {
    return `${this.idPrefix}-heading`;
  }

  requestGraderGuide(): void {
    this.graderGuideRequested.emit();
  }

  requestDocuments(): void {
    this.documentsRequested.emit();
  }

  onSidebarWidthChange(width: number): void {
    this.sidebarWidth = width;
    writeStoredSettings({ sidebarWidth: width });
    this.cdr.markForCheck();
  }

  /** A nested dialog's close or cancel event, stopped so it never reaches the wizard's dialog. */
  stopNestedEvent(event: Event): void {
    event.stopPropagation();
  }

  // -------------------------------------------------------------------------------------------
  // The scope and the models
  // -------------------------------------------------------------------------------------------

  /** The models chosen in the current scope. */
  get chosenKeys(): readonly string[] {
    return this.scopeMode === 'comparison' ? this.coveredKeys : this.subjectKeys;
  }

  get pickerMin(): number {
    return this.scopeMode === 'comparison' ? MIN_COMPARISON_DOCUMENT_ENTRIES : 1;
  }

  get pickerMax(): number | null {
    return this.scopeMode === 'comparison' ? MAX_COMPARISON_DOCUMENT_ENTRIES : null;
  }

  selectScope(mode: ReportDocumentScopeMode): void {
    if (mode === this.scopeMode) {
      return;
    }
    this.scopeMode = mode;
    writeStoredSettings({ documentScope: mode });
    this.checks.clear();
    this.serverWarning = null;
    this.startError = null;
    this.layoutPreviewNote = '';
    this.schedulePreview();
  }

  /** Arrow keys wrap, Home and End jump; focus follows the selection. */
  onScopeKeydown(event: KeyboardEvent, index: number): void {
    const count = this.scopeOptions.length;
    const targets: Record<string, number> = { ArrowRight: index + 1, ArrowLeft: index - 1, Home: 0, End: count - 1 };
    const requested = targets[event.key];
    if (requested === undefined) {
      return;
    }
    event.preventDefault();
    const next = this.scopeOptions[(requested + count) % count];
    this.selectScope(next.value);
    document.getElementById(`${this.idPrefix}-scope-${next.value}`)?.focus();
  }

  /** The picker's choice, in the options' order. */
  onModelsChange(keys: readonly ModelPickerKey[]): void {
    const chosen = new Set(keys.map(key => String(key)));
    const ordered = this.modelOptions.map(option => String(option.key)).filter(key => chosen.has(key));
    if (ordered.join(',') === this.chosenKeys.join(',')) {
      return;
    }
    if (this.scopeMode === 'comparison') {
      this.coveredKeys = ordered;
      this.checks.clear();
    } else {
      this.subjectKeys = ordered;
    }
    this.serverWarning = null;
    this.startError = null;
    this.schedulePreview();
  }

  /** **Choose these models** on another model set: the picker takes that set. */
  chooseModels(keys: readonly string[]): void {
    if (this.scopeMode !== 'comparison' || this.jobInProgress) {
      return;
    }
    this.onModelsChange(keys);
    document.getElementById(`${this.idPrefix}-models-label`)?.scrollIntoView?.({ block: 'nearest' });
  }

  /** A model's label: `GPT-5.6 Luna (max)`. */
  modelLabel(key: string): string {
    return this.modelOptions.find(option => option.key === key)?.model.label ?? key;
  }

  /** What Generate writes, in one line under the picker. */
  get modelsLine(): string {
    const chosen = this.chosenKeys;
    const offered = this.modelOptions.map(option => String(option.key));
    if (this.scopeMode === 'comparison') {
      if (chosen.length === offered.length) {
        return 'Writes the comparison-wide documents.';
      }
      const left = offered.filter(key => !chosen.includes(key)).map(key => comparisonModelMention(key, this.modelOptions));
      return `Writes documents for ${chosen.length} of ${plural(offered.length, 'model', 'models')} — leaves out ${left.join(', ')}.`;
    }
    if (chosen.length === 1) {
      return `Writes the per-model documents of ${comparisonModelMention(chosen[0], this.modelOptions)}, compared with the other models.`;
    }
    return `Writes the per-model documents of ${chosen.length} models, one set each, each compared with the other models.`;
  }

  /** Why a whole-comparison choice starts with fewer than every model, or null. */
  get modelsCapNote(): string | null {
    if (this.scopeMode !== 'comparison' || this.modelOptions.length <= MAX_COMPARISON_DOCUMENT_ENTRIES) {
      return null;
    }
    return `A document covers at most ${MAX_COMPARISON_DOCUMENT_ENTRIES} models, so the ${MAX_COMPARISON_DOCUMENT_ENTRIES} `
      + `with the highest Intelligence Index of the ${this.modelOptions.length} are chosen first.`;
  }

  /** `A = GPT-6.1 Sol (medium), B = …`, once the preview has lettered the covered models. */
  get coveredLetters(): string | null {
    if (this.scopeMode !== 'comparison') {
      return null;
    }
    const preview = this.listPreview;
    const lettered = (preview?.coveredModels ?? []).filter(model => !!model.letter);
    if (lettered.length === 0) {
      return null;
    }
    return lettered.map(model => `${model.letter} = ${model.label}`).join(', ');
  }

  /** The preview answered for the current scope and models, or null while it is awaited. */
  private get listPreview(): BenchmarkReportPackPreviewDto | null {
    return this.previewListSignature === this.listSignature ? this.preview : null;
  }

  /** The preview whose estimate, refusal and warning are current: none while a change is pending. */
  private get answeredPreview(): BenchmarkReportPackPreviewDto | null {
    return this.previewPending || this.previewError ? null : this.listPreview;
  }

  private get listSignature(): string {
    return `${this.scopeMode}|${this.chosenKeys.join(',')}`;
  }

  // -------------------------------------------------------------------------------------------
  // The documents of this comparison
  // -------------------------------------------------------------------------------------------

  onDocumentCheck(change: { key: string; checked: boolean }): void {
    this.checks.set(change.key, change.checked);
    this.startError = null;
    this.refreshDocumentsView();
    this.schedulePreview();
  }

  onDocumentDeleted(documentId: number): void {
    this.listItems.delete(documentId);
    for (const row of this.allRows()) {
      if (row.document?.documentId === documentId) {
        this.checks.delete(row.key);
      }
    }
    this.documentsChanged.emit();
    this.schedulePreview();
  }

  /** Every row of the list, the other model sets' included. */
  private allRows(): ComparisonDocumentRow[] {
    return [
      ...this.documentsView.groups.flatMap(group => group.rows),
      ...this.documentsView.otherSets.flatMap(set => set.rows)
    ];
  }

  /** The rows the next job writes. */
  get checkedRows(): ComparisonDocumentRow[] {
    return this.documentsView.groups.flatMap(group => group.rows).filter(row => row.checkable && row.checked);
  }

  private refreshDocumentsView(): void {
    const unlisted = this.modelOptions.length === 0
      ? 'Every entry of this comparison is Excluded, so it has no documents to list.'
      : this.chosenKeys.length === 0 ? 'Choose a model to list its documents.' : null;
    this.documentsView = comparisonDocumentsView({
      mode: this.scopeMode,
      preview: this.listPreview,
      error: this.listPreview ? null : this.previewError ?? unlisted,
      chosenKeys: this.chosenKeys,
      offeredKeys: this.modelOptions.map(option => String(option.key)),
      labelOf: key => this.modelLabel(key),
      listItems: this.listItems,
      checks: this.checks
    });
    const audiences = this.documentsView.state === 'ready'
      ? new Set(this.checkedRows.map(row => row.audience))
      : new Set(REPORT_PACK_AUDIENCES.map(option => option.audience));
    const next = REPORT_PACK_AUDIENCES.map(option => option.audience).filter(audience => audiences.has(audience));
    if (next.join(',') !== this.checkedAudiences.join(',')) {
      this.checkedAudiences = next;
    }
  }

  /** The written documents' list entries, for their flags and chart counts: fetched when one is missing, or when forced. */
  private refreshListItems(force = false): void {
    const comparisonId = this.currentComparisonId ?? this.preview?.comparisonId ?? null;
    if (comparisonId === null || this.destroyed) {
      return;
    }
    const ids = this.allRows().map(row => row.document?.documentId).filter((id): id is number => typeof id === 'number');
    if (ids.length === 0 || (!force && ids.every(id => this.listItems.has(id)))) {
      return;
    }
    const generation = this.generation;
    this.listSub?.unsubscribe();
    this.listSub = this.benchmarkService.listReportDocuments({ comparisonId }).subscribe({
      next: items => {
        if (generation !== this.generation) {
          return;
        }
        this.listItems = new Map((items ?? []).map(item => [item.id, item]));
        this.refreshDocumentsView();
        this.cdr.markForCheck();
      },
      error: () => {
        // The rows stay without their flags and chart counts.
      }
    });
  }

  // -------------------------------------------------------------------------------------------
  // Charts
  // -------------------------------------------------------------------------------------------

  onChartSelectionChange(selection: ReportChartSelection): void {
    this.chartSelectionChange.emit(selection);
  }

  onChartLayoutChange(layout: ReportChartLayoutSettings): void {
    this.chartLayoutChange.emit(layout);
  }

  onChartAudienceChange(audience: BenchmarkReportAudience): void {
    this.chartAudience = audience;
    this.layoutPreviewNote = '';
  }

  /** Whose chart placements the picker shows. */
  get chartScope(): ReportChartScope {
    return this.scopeMode === 'comparison' ? 'comparison' : 'model';
  }

  /** The document type the chart picker shows, as it chooses it: the one chosen, else the first being written. */
  get chartPreviewAudience(): BenchmarkReportAudience {
    return this.chartAudience ?? this.checkedAudiences[0] ?? REPORT_PACK_AUDIENCES[0].audience;
  }

  /** Why Preview layout cannot run, or null. */
  get layoutPreviewBlockedReason(): string | null {
    if (!this.documentChartsComposer) {
      return 'The charts cannot be drawn here.';
    }
    if (this.chartStorageMissing) {
      return 'Chart storage is not configured, so there are no charts to preview.';
    }
    if ((this.currentComparisonId ?? this.preview?.comparisonId ?? null) === null) {
      return 'The comparison has no number yet, so its layout cannot be previewed.';
    }
    if (this.scopeMode === 'comparison' && this.coveredKeys.length < MIN_COMPARISON_DOCUMENT_ENTRIES) {
      return 'Choose at least two models.';
    }
    if (this.scopeMode === 'model' && this.subjectKeys.length === 0) {
      return 'Choose a model.';
    }
    const audience = this.chartPreviewAudience;
    const figures = (this.chartSelection[audience] ?? []).filter(key => this.chartsAvailable.includes(key));
    if (figures.length === 0) {
      return `No chart is chosen for the ${audienceLabel(audience)}.`;
    }
    return null;
  }

  /** Preview layout: the viewer opens at once, and composes and fetches the PDF as it loads. */
  previewLayout(button: HTMLElement): void {
    const compose = this.documentChartsComposer;
    const request = this.buildRequest({ preview: true, layout: true });
    if (this.layoutPreviewBlockedReason !== null || !compose || !request) {
      return;
    }
    const audience = this.chartPreviewAudience;
    this.layoutPreviewNote = '';
    this.layoutPreviewReturnFocus = button;
    const generation = this.generation;
    this.layoutViewer?.open(layoutPreviewViewerRequest(this.benchmarkService, compose, {
      request,
      audience,
      paper: rememberedPdfPaper(),
      scope: this.chartScope,
      coveredKeys: this.scopeMode === 'comparison' ? [...this.coveredKeys] : null
    }, failed => this.onLayoutPreviewComposed(failed, generation)));
    this.cdr.markForCheck();
  }

  private onLayoutPreviewComposed(failed: readonly ReportChartLayoutPreviewFailure[], generation: number): void {
    if (generation !== this.generation) {
      return;
    }
    this.layoutPreviewNote = failed.length === 0
      ? ''
      : 'Not drawn: ' + failed
        .map(item => `${REPORT_CHART_FIGURES.find(figure => figure.key === item.key)?.title ?? item.key} (${item.message})`)
        .join('; ');
    this.cdr.markForCheck();
  }

  /** The layout preview closed: focus returns to Preview layout. */
  onLayoutViewerClosed(): void {
    const target = this.layoutPreviewReturnFocus;
    this.layoutPreviewReturnFocus = null;
    if (target?.isConnected) {
      target.focus();
    }
  }

  // -------------------------------------------------------------------------------------------
  // The writer
  // -------------------------------------------------------------------------------------------

  get writerOptions(): ModelPickerOption<SystemAiConfigDto>[] {
    if (this.writerOptionsSource !== this.writers) {
      this.writerOptionsSource = this.writers;
      this.writerOptionsCache = toModelPickerOptions(this.writers);
    }
    return this.writerOptionsCache;
  }

  selectWriter(key: ModelPickerKey | null): void {
    const id = typeof key === 'number' ? key : null;
    if (id === this.writerId) {
      return;
    }
    this.writerId = id;
    this.serverWarning = null;
    this.startError = null;
    this.schedulePreview();
  }

  get selectedWriter(): SystemAiConfigDto | null {
    return this.writers.find(writer => writer.id === this.writerId) ?? null;
  }

  /** Why the documents cannot be written as chosen, as the current preview says; null while it is pending. */
  get refusal(): string | null {
    return this.answeredPreview?.refusal ?? null;
  }

  /** The preview's same-provider warning, or the one a 409 carried. Generate then asks first. */
  get sameProviderWarning(): string | null {
    if (this.refusal) {
      return null;
    }
    return this.answeredPreview?.sameProviderWarning ?? this.serverWarning;
  }

  /** The picker's description: the refusal or the warning while there is one. */
  get writerDescribedBy(): string | null {
    const ids = [
      this.refusal ? `${this.idPrefix}-writer-refusal` : '',
      this.sameProviderWarning ? `${this.idPrefix}-same-provider-text` : ''
    ].filter(id => id !== '');
    return ids.length > 0 ? ids.join(' ') : null;
  }

  /**
   * The request body, or null while it cannot be built. A preview asks for every chosen model and the
   * document types checked (all of them before the list is known), with or without a writer; a start
   * asks for what is checked, and names the written documents it replaces. A layout preview asks for
   * the first chosen model alone in per-model scope. `batteryRunIds` is sent only for a comparison of
   * battery results, so a run comparison's request is unchanged by it.
   */
  buildRequest(options: { preview?: boolean; layout?: boolean; acknowledgeSameProvider?: boolean } = {}): BenchmarkReportPackRequest | null {
    const context = this.currentContext;
    const chosen = this.chosenKeys;
    if (!context || chosen.length === 0) {
      return null;
    }
    if (!options.preview && this.writerId === null) {
      return null;
    }
    const ready = this.documentsView.state === 'ready';
    const checked = this.checkedRows;
    const audiences = ready || !options.preview
      ? REPORT_PACK_AUDIENCES.map(option => option.audience).filter(audience => checked.some(row => row.audience === audience))
      : REPORT_PACK_AUDIENCES.map(option => option.audience);
    const batteryRunIds = context.batteryRunIds ?? [];
    const base = {
      runIds: [...context.runIds],
      groupIds: [...context.groupIds],
      ...(batteryRunIds.length > 0 ? { batteryRunIds: [...batteryRunIds] } : {}),
      pricingBasis: context.pricingBasis === 'AsRun'
        ? BenchmarkReportPackPricingBasis.AsRun
        : BenchmarkReportPackPricingBasis.Current
    };
    const replaceDocumentIds = checked
      .map(row => row.document?.documentId)
      .filter((id): id is number => typeof id === 'number');
    const tail = {
      audiences,
      writerModelConfigurationId: this.writerId ?? 0,
      acknowledgeSameProvider: options.acknowledgeSameProvider ?? false,
      ...(options.preview ? {} : { replaceDocumentIds })
    };
    if (this.scopeMode === 'comparison') {
      return { ...base, subjectKey: '', scope: BenchmarkReportScope.Comparison, coveredEntryKeys: [...chosen], ...tail };
    }
    const subjects = options.layout
      ? [chosen[0]]
      : options.preview
        ? [...chosen]
        : chosen.filter(key => checked.some(row => row.entryKey === key));
    if (subjects.length === 0) {
      return null;
    }
    return { ...base, subjectKey: subjects[0], scope: BenchmarkReportScope.Model, subjectKeys: subjects, ...tail };
  }

  // -------------------------------------------------------------------------------------------
  // The preview: the documents and the estimate
  // -------------------------------------------------------------------------------------------

  /**
   * Asks for the preview after the debounce. Changing the scope or the models lists the documents
   * afresh; changing the checks or the writer keeps the list and estimates again.
   */
  schedulePreview(): void {
    const request = this.buildRequest({ preview: true });
    if (this.previewListSignature !== this.listSignature) {
      this.preview = null;
      this.previewListSignature = null;
    }
    this.previewError = null;
    if (!request || this.destroyed) {
      this.latestTicket = null;
      this.previewQueue.next(null);
      this.previewPending = false;
    } else {
      const ticket: PreviewTicket = {
        id: ++this.previewTicketId,
        generation: this.generation,
        request,
        signature: JSON.stringify(request),
        listSignature: this.listSignature
      };
      this.latestTicket = ticket;
      this.previewPending = true;
      this.previewQueue.next(ticket);
    }
    this.refreshDocumentsView();
    this.cdr.markForCheck();
  }

  retryPreview(): void {
    this.schedulePreview();
  }

  private onPreviewAnswer(answer: PreviewAnswer): void {
    const ticket = answer.ticket;
    if (this.destroyed || ticket.generation !== this.generation || ticket.id !== this.latestTicket?.id) {
      return;
    }
    this.previewPending = false;
    if (answer.error || !answer.preview) {
      this.previewError = (answer.error ? serverMessage(answer.error) : null)
        ?? 'The documents and the estimate could not be looked up.';
      this.refreshDocumentsView();
      this.cdr.markForCheck();
      return;
    }
    this.preview = answer.preview;
    this.previewListSignature = ticket.listSignature;
    this.previewError = null;
    this.refreshDocumentsView();
    // The checks follow the documents just listed: a request for other documents is asked again.
    const now = this.buildRequest({ preview: true });
    if (now && JSON.stringify(now) !== ticket.signature) {
      this.schedulePreview();
      return;
    }
    this.refreshListItems();
    this.cdr.markForCheck();
  }

  /** The checked rows with their estimates, while the preview is current. */
  private get checkedEstimates(): CheckedEstimate[] {
    const preview = this.answeredPreview;
    if (!preview) {
      return [];
    }
    const result: CheckedEstimate[] = [];
    for (const row of this.checkedRows) {
      const estimate = preview.estimates.find(candidate => candidate.audience === row.audience
        && (row.entryKey === null || (candidate.subjectKey ?? preview.subjectKey) === row.entryKey));
      if (estimate) {
        result.push({ row, estimate });
      }
    }
    return result;
  }

  /** The *Estimated cost* panel, or null while there is nothing to say. */
  get estimateView(): ReportPackEstimateView | null {
    if (this.writerId === null || (this.checkedRows.length === 0 && this.documentsView.state === 'ready')) {
      return null;
    }
    if (this.previewPending) {
      return { state: 'loading', total: null, parts: [] };
    }
    if (this.previewError) {
      return { state: 'failed', total: null, parts: [] };
    }
    const preview = this.answeredPreview;
    if (!preview || preview.refusal) {
      return null;
    }
    const items = this.checkedEstimates;
    if (items.length === 0 || items.some(item => item.estimate.estimatedCostUsd === null || item.estimate.estimatedCostUsd === undefined)) {
      return { state: 'noPrice', total: null, parts: [] };
    }
    const total = items.reduce((sum, item) => sum + (item.estimate.estimatedCostUsd ?? 0), 0);
    const several = this.scopeMode === 'model' && new Set(items.map(item => item.row.entryKey)).size > 1;
    const parts = items.length > 1
      ? items.map(item => ({
        name: several ? `${item.row.name} — ${item.row.subjectLabel}` : item.row.name,
        cost: formatCostUsd(item.estimate.estimatedCostUsd)
      }))
      : [];
    return { state: 'ready', total: formatCostUsd(total), parts };
  }

  /** The estimate's total in dollars, or null without one. */
  private get estimateTotalUsd(): number | null {
    const items = this.checkedEstimates;
    if (items.length === 0 || items.some(item => item.estimate.estimatedCostUsd === null || item.estimate.estimatedCostUsd === undefined)) {
      return null;
    }
    return items.reduce((sum, item) => sum + (item.estimate.estimatedCostUsd ?? 0), 0);
  }

  /** What the estimate is for, under the total. */
  get estimateSubjectLine(): string {
    const preview = this.answeredPreview;
    if (!preview) {
      return '';
    }
    const repair = 'A repair turn can roughly double a document’s cost.';
    if (this.scopeMode === 'comparison') {
      return `For ${this.coveredKeys.length} models, compared as equals. ${repair}`;
    }
    const subjects = new Set(this.checkedRows.map(row => row.entryKey));
    if (subjects.size <= 1) {
      return `For ${preview.subjectLabel} against ${plural(preview.peers.length, 'peer', 'peers')}. ${repair}`;
    }
    return `For ${subjects.size} models, each against the others. ${repair}`;
  }

  /** The amber warning when a checked document's prompt fills 70 % or more of the writer's context window. */
  get contextWindowWarning(): string | null {
    const items = this.checkedEstimates
      .filter(item => (item.estimate.contextWindowShare ?? 0) >= REPORT_PACK_CONTEXT_WARNING_SHARE);
    if (items.length === 0 || this.refusal) {
      return null;
    }
    const fullest = items.reduce((a, b) => ((b.estimate.contextWindowShare ?? 0) > (a.estimate.contextWindowShare ?? 0) ? b : a));
    const percent = Math.round((fullest.estimate.contextWindowShare ?? 0) * 100);
    return `The ${fullest.row.name}'s prompt would fill about ${percent} % of the writer's context window; from `
      + `${Math.round(REPORT_PACK_CONTEXT_REFUSAL_SHARE * 100)} % it is refused. Fewer models, or a writer with a larger `
      + 'context window, leaves the writer more room.';
  }

  // -------------------------------------------------------------------------------------------
  // Generate
  // -------------------------------------------------------------------------------------------

  /** Every listed document is written and none is checked to be rewritten. */
  private get allWrittenNoneChecked(): boolean {
    const rows = this.documentsView.groups.flatMap(group => group.rows);
    return this.documentsView.state === 'ready' && rows.length > 0
      && rows.every(row => row.document !== null) && this.checkedRows.length === 0;
  }

  /** In per-model scope, the checked documents are the same for every model checked. */
  private get checksEven(): boolean {
    if (this.scopeMode !== 'model') {
      return true;
    }
    const rows = this.checkedRows;
    const subjects = new Set(rows.map(row => row.entryKey));
    const audiences = new Set(rows.map(row => row.audience));
    return rows.length === subjects.size * audiences.size;
  }

  /** Why Generate is unavailable, or null when it can start. */
  get generateBlockedReason(): string | null {
    if (this.jobInProgress) {
      return 'A report pack is being written. Wait for it to finish, or cancel it.';
    }
    if (this.modelOptions.length === 0) {
      return 'Every entry of this comparison is Excluded, so no document can be written.';
    }
    if (this.scopeMode === 'comparison') {
      if (this.modelOptions.length < MIN_COMPARISON_DOCUMENT_ENTRIES) {
        return 'A comparison-wide document covers at least two models, and this comparison has one that is not '
          + 'Excluded. Write its documents under One model at a time.';
      }
      if (this.coveredKeys.length < MIN_COMPARISON_DOCUMENT_ENTRIES) {
        return 'Choose at least two models.';
      }
      if (this.coveredKeys.length > MAX_COMPARISON_DOCUMENT_ENTRIES) {
        return `Choose at most ${MAX_COMPARISON_DOCUMENT_ENTRIES} models.`;
      }
    } else if (this.subjectKeys.length === 0) {
      return 'Choose at least one model.';
    }
    if (this.documentsView.state === 'ready') {
      if (this.allWrittenNoneChecked) {
        return REPORT_PACK_ALL_WRITTEN_REASON;
      }
      if (this.checkedRows.length === 0) {
        return 'Choose at least one document.';
      }
      if (!this.checksEven) {
        return REPORT_PACK_UNEVEN_MODELS_REASON;
      }
    }
    if (this.writerId === null) {
      return 'Choose a report writer.';
    }
    if (this.previewPending) {
      return this.documentsView.state === 'ready'
        ? 'Checking the writer and estimating the cost…'
        : 'Looking up the documents already written…';
    }
    if (this.previewError) {
      return 'The estimate could not be computed. Retry it before generating.';
    }
    if (this.refusal === REPORT_PACK_PEERLESS_REFUSAL) {
      return 'A chosen model has no other model to be compared with in this comparison.';
    }
    if (this.refusal) {
      return 'The documents cannot be written as chosen; the reason is shown under the report writer.';
    }
    return null;
  }

  /**
   * Generate stays focusable, `aria-disabled`, when every listed document is already written, so a
   * keyboard reader lands on it and hears why; every other block disables it.
   */
  get generateAriaDisabled(): boolean {
    return this.generateBlockedReason === REPORT_PACK_ALL_WRITTEN_REASON;
  }

  /** The Generate button's description: the estimate, and the reason it is blocked while it is. */
  get generateDescribedBy(): string {
    const estimate = `${this.idPrefix}-estimate`;
    return this.generateBlockedReason !== null ? `${estimate} ${this.idPrefix}-generate-blocked` : estimate;
  }

  /** Generate: a same-provider writer asks first, on every generate; any other writer starts at once. */
  generate(): void {
    if (this.buildRequest() === null || this.generateBlockedReason !== null) {
      return;
    }
    const warning = this.sameProviderWarning;
    if (warning) {
      this.openSameProviderConfirm(warning);
      return;
    }
    this.startPack(false);
  }

  confirmSameProvider(): void {
    this.sameProviderDialog?.nativeElement.close();
    this.startPack(true);
  }

  cancelSameProvider(): void {
    this.sameProviderDialog?.nativeElement.close();
    this.generateButton?.nativeElement.focus();
  }

  private openSameProviderConfirm(text: string): void {
    this.confirmWarningText = text;
    this.cdr.detectChanges();
    const dialog = this.sameProviderDialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
  }

  private startPack(acknowledgeSameProvider: boolean): void {
    const request = this.buildRequest({ acknowledgeSameProvider });
    if (!request || this.starting || this.jobInProgress) {
      return;
    }
    this.starting = true;
    this.startError = null;
    this.pollError = null;
    this.syncRunning();
    const estimateUsd = this.estimateTotalUsd;
    writeStoredSettings({ writerConfigId: request.writerModelConfigurationId });
    const generation = this.generation;
    this.subscriptions['start'] = this.benchmarkService.startReportPack(request).subscribe({
      next: response => {
        if (generation !== this.generation) {
          return;
        }
        this.starting = false;
        this.activeJobId = response.jobId;
        this.job = null;
        this.jobEstimateUsd = estimateUsd;
        this.pollFailures = 0;
        // What was checked is being written now: the rows take their defaults once it is.
        this.checks.clear();
        this.refreshDocumentsView();
        this.syncRunning();
        this.pollJob();
        this.cdr.markForCheck();
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.generation) {
          return;
        }
        this.starting = false;
        const body = error.error;
        // The server saw a same-provider writer the preview did not: ask, as for the preview's.
        if (error.status === 409 && !acknowledgeSameProvider && !isJobDto(body) && isSameProviderWarning(body)) {
          this.serverWarning = body.message;
          this.syncRunning();
          this.cdr.markForCheck();
          this.openSameProviderConfirm(body.message);
          return;
        }
        this.startError = this.startErrorMessage(error);
        this.syncRunning();
        this.cdr.markForCheck();
      }
    });
    this.cdr.markForCheck();
  }

  private startErrorMessage(error: HttpErrorResponse): string {
    const body = error.error;
    if (error.status === 409 && isJobDto(body)) {
      this.adoptJob(body);
      return `Another report pack is being written, for ${body.subjectLabel}, started ${formatUtc(body.startedAtUtc)}. `
        + 'One job runs at a time; its progress is shown here.';
    }
    if (error.status === 409 && isSameProviderWarning(body)) {
      this.serverWarning = body.message;
      return body.message;
    }
    if (error.status === 429) {
      return serverMessage(error) ?? 'The benchmark spend cap does not allow this report pack now.';
    }
    return serverMessage(error) ?? 'The report pack could not be started.';
  }

  // -------------------------------------------------------------------------------------------
  // The job
  // -------------------------------------------------------------------------------------------

  /** Starting, or following a job that has not finished. */
  get jobInProgress(): boolean {
    if (this.starting) {
      return true;
    }
    if (this.activeJobId === null) {
      return false;
    }
    return this.job === null || !FINISHED_JOB_STATUSES.has(this.job.status);
  }

  /** A job is running: the host keeps the wizard from closing on it unawares. */
  get jobRunning(): boolean {
    return this.jobInProgress;
  }

  /** The job has finished; its card shows a one-line summary. */
  get isJobFinished(): boolean {
    return this.job !== null && FINISHED_JOB_STATUSES.has(this.job.status);
  }

  get jobDocuments(): BenchmarkReportPackDocumentProgressDto[] {
    return this.job?.documents ?? [];
  }

  /** A per-model job: its progress rows name their model. */
  get jobHasModelColumn(): boolean {
    return this.job !== null && this.job.scope !== BenchmarkReportScope.Comparison;
  }

  /** The distinct subjects of the job's documents. */
  private get jobSubjectCount(): number {
    const job = this.job;
    if (!job) {
      return 0;
    }
    return new Set(job.documents.map(doc => doc.subjectKey ?? job.subjectKey)).size;
  }

  /**
   * The server's clock now: its last reading plus the client time since that response arrived;
   * the client's clock when the server sent none.
   */
  private serverNowMs(): number {
    const serverTime = this.job?.serverTimeUtc;
    if (serverTime) {
      const server = parseServerUtcDate(serverTime).getTime();
      if (!Number.isNaN(server)) {
        return server + Math.max(0, Date.now() - this.jobReadAt);
      }
    }
    return Date.now();
  }

  /** Milliseconds from `startUtc` to `endUtc`, or to the server's clock now while `endUtc` is absent. */
  private spanToNow(startUtc: string | null | undefined, endUtc: string | null | undefined): number | null {
    if (!startUtc) {
      return null;
    }
    const start = parseServerUtcDate(startUtc).getTime();
    const end = endUtc ? parseServerUtcDate(endUtc).getTime() : this.serverNowMs();
    if (Number.isNaN(start) || Number.isNaN(end)) {
      return null;
    }
    return Math.max(0, end - start);
  }

  get jobElapsed(): string {
    const job = this.job;
    if (!job) {
      return '';
    }
    const ms = this.spanToNow(job.startedAtUtc, job.completedAtUtc);
    return ms === null ? '' : formatElapsed(ms);
  }

  /** Whom the job writes about: its subject, or `3 models` for a per-model job over several. */
  private get jobSubjectText(): string {
    const job = this.job;
    if (!job) {
      return '';
    }
    const count = this.jobSubjectCount;
    return count > 1 ? `${count} models` : job.subjectLabel;
  }

  /** The live line: it changes as the job moves from phase to phase, never with the clock. */
  get jobStatusLine(): string {
    const job = this.job;
    if (!job) {
      return this.activeJobId ? 'Starting the report pack…' : '';
    }
    const total = job.documents.length;
    const finished = job.documents.filter(d => FINISHED_DOCUMENT_STATUSES.has(d.status)).length;
    if (job.status === 'Running') {
      return `Writing ${total} ${total === 1 ? 'document' : 'documents'} for ${this.jobSubjectText}: ${finished} of ${total} finished.`;
    }
    return `Report pack for ${this.jobSubjectText}: ${statusLabel(job.status)}.`;
  }

  /** Queued, Preparing, one stage per document, Done; the current one is where the job stands. */
  get jobStages(): ReportPackJobStage[] {
    const job = this.job;
    const documents = job?.documents ?? [];
    const several = this.jobSubjectCount > 1;
    const stages = [
      { key: 'queued', name: 'Queued' },
      { key: 'preparing', name: 'Preparing' },
      ...documents.map(doc => ({
        key: `doc-${doc.subjectKey ?? job?.subjectKey ?? ''}-${doc.audience}`,
        name: several ? `${audienceLabel(doc.audience)} — ${doc.subjectLabel ?? job?.subjectLabel ?? ''}` : audienceLabel(doc.audience)
      })),
      { key: 'done', name: 'Done' }
    ];
    let current = 0;
    if (job && FINISHED_JOB_STATUSES.has(job.status)) {
      current = stages.length;
    } else if (job) {
      const started = job.totalModelCalls > 0 || documents.some(doc => doc.status !== 'Pending');
      if (!started) {
        current = 1;
      } else {
        let index = documents.findIndex(doc => ACTIVE_DOCUMENT_STATUSES.has(doc.status));
        if (index < 0) {
          index = documents.findIndex(doc => doc.status === 'Pending');
        }
        if (index < 0) {
          index = Math.max(documents.length - 1, 0);
        }
        current = 2 + index;
      }
    }
    return stages.map((stage, i): ReportPackJobStage => ({
      ...stage,
      state: i < current ? 'done' : i === current ? 'current' : 'pending'
    }));
  }

  /** One row per document: its model, its chip, its duration on the server's clock, its model calls and its charts. */
  get documentRows(): ReportPackDocumentRow[] {
    const job = this.job;
    if (!job) {
      return [];
    }
    const finished = FINISHED_JOB_STATUSES.has(job.status);
    return job.documents.map((doc): ReportPackDocumentRow => {
      const duration = doc.startedAtUtc
        ? this.spanToNow(doc.startedAtUtc, doc.completedAtUtc ?? (finished ? job.completedAtUtc : null))
        : null;
      return {
        key: `${doc.subjectKey ?? job.subjectKey}|${doc.audience}`,
        doc,
        audience: doc.audience,
        name: audienceLabel(doc.audience),
        subjectLabel: doc.subjectLabel ?? job.subjectLabel,
        statusWord: statusLabel(doc.status),
        chipClass: documentChipClass(doc.status),
        duration: duration === null ? '—' : formatElapsed(duration),
        modelCalls: numberFormat.format(doc.modelCalls),
        errorMessage: doc.errorMessage || null,
        charts: this.chartCell(doc)
      };
    });
  }

  private chartCell(doc: BenchmarkReportPackDocumentProgressDto): ReportPackChartCell {
    if (this.chartStorageMissing || (this.chartSelection[doc.audience] ?? []).length === 0) {
      return { text: 'Charts: none', retry: false };
    }
    const status = doc.documentId === null ? undefined : this.chartStatus[doc.documentId];
    if (!status) {
      return { text: '—', retry: false };
    }
    switch (status.state) {
      case 'attaching':
        return { text: 'Charts: attaching…', retry: false };
      case 'done':
        return { text: `Charts: ${numberFormat.format(status.count)}`, retry: false };
      case 'failed':
        return { text: 'Charts failed — retry', retry: true };
      default:
        return { text: 'Charts: none', retry: false };
    }
  }

  retryCharts(row: ReportPackDocumentRow): void {
    this.chartRetryRequested.emit(row.doc);
  }

  get jobTokens(): string {
    const job = this.job;
    if (!job) {
      return '—';
    }
    return `${job.inputTokens.toLocaleString('en-US')} in · ${job.outputTokens.toLocaleString('en-US')} out`;
  }

  get jobCostLabel(): string {
    return this.isJobFinished ? 'Cost' : 'Cost so far';
  }

  get jobEstimate(): string | null {
    return this.jobEstimateUsd === null ? null : `about ${formatCostUsd(this.jobEstimateUsd)}`;
  }

  /** The finished job in one line: `Written: 2 documents, 1 min 12 s, $0.21`. */
  get jobSummary(): string {
    const job = this.job;
    if (!job) {
      return '';
    }
    const total = job.documents.length;
    const written = job.documents.filter(d => WRITTEN_DOCUMENT_STATUSES.has(d.status)).length;
    const tail = [this.jobElapsed, formatCostUsd(job.costUsd)].filter(part => part !== '').join(', ');
    let head: string;
    switch (job.status) {
      case 'Completed':
        head = `Written: ${plural(written, 'document', 'documents')}`;
        break;
      case 'CompletedWithErrors':
        head = `Written with errors: ${written} of ${plural(total, 'document', 'documents')}`;
        break;
      case 'Canceled':
        head = `Canceled: ${written} of ${plural(total, 'document', 'documents')} written`;
        break;
      default:
        head = `Failed: ${written} of ${plural(total, 'document', 'documents')} written`;
    }
    return tail ? `${head}, ${tail}` : head;
  }

  /** Dismiss: the finished job's card goes away. */
  dismissJob(): void {
    if (this.jobInProgress) {
      return;
    }
    this.activeJobId = null;
    this.job = null;
    this.jobEstimateUsd = null;
    this.pollError = null;
    this.syncRunning();
    this.cdr.markForCheck();
    document.getElementById(`${this.idPrefix}-progress-heading`)?.focus();
  }

  cancelJob(): void {
    const id = this.activeJobId;
    if (!id || !this.jobInProgress || this.canceling) {
      return;
    }
    this.canceling = true;
    const generation = this.generation;
    this.subscriptions['cancel'] = this.benchmarkService.cancelReportPackJob(id).subscribe({
      next: () => {
        if (generation !== this.generation) {
          return;
        }
        this.canceling = false;
        this.pollJob();
        this.cdr.markForCheck();
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.generation) {
          return;
        }
        this.canceling = false;
        this.pollError = serverMessage(error) ?? 'The job could not be canceled.';
        this.cdr.markForCheck();
      }
    });
    this.cdr.markForCheck();
  }

  private loadActiveJob(generation: number): void {
    this.subscriptions['active'] = this.benchmarkService.getActiveReportPackJob().subscribe({
      next: job => {
        if (generation !== this.generation || !job || this.activeJobId !== null) {
          return;
        }
        this.adoptJob(job);
        this.cdr.markForCheck();
      },
      error: () => {
        // No running job can be shown; starting one reports the conflict if there is one.
      }
    });
  }

  private adoptJob(job: BenchmarkReportPackJobDto): void {
    this.activeJobId = job.id;
    this.pollFailures = 0;
    this.applyJob(job);
  }

  private applyJob(job: BenchmarkReportPackJobDto): void {
    this.job = job;
    this.jobReadAt = Date.now();
    this.pollError = null;
    for (const doc of job.documents) {
      if (doc.documentId !== null && WRITTEN_DOCUMENT_STATUSES.has(doc.status) && !this.writtenDocumentIds.has(doc.documentId)) {
        this.writtenDocumentIds.add(doc.documentId);
        this.documentWritten.emit(doc);
      }
    }
    if (FINISHED_JOB_STATUSES.has(job.status)) {
      this.clearPollTimer();
      if (!this.finishedJobIds.has(job.id)) {
        this.finishedJobIds.add(job.id);
        this.jobFinished.emit(job);
        // The documents it wrote are listed now.
        this.schedulePreview();
      }
    } else {
      this.schedulePoll(REPORT_PACK_POLL_MS);
    }
    this.syncRunning();
  }

  private schedulePoll(delay: number): void {
    this.clearPollTimer();
    this.pollTimer = setTimeout(() => {
      this.pollTimer = null;
      this.pollJob();
    }, delay);
  }

  /** One progress request; the next is scheduled only when it answers, so requests never stack. */
  private pollJob(): void {
    const id = this.activeJobId;
    if (!id) {
      return;
    }
    this.clearPollTimer();
    this.subscriptions['poll']?.unsubscribe();
    const generation = this.generation;
    this.pollCount++;
    this.subscriptions['poll'] = this.benchmarkService.getReportPackJob(id).subscribe({
      next: job => {
        if (generation !== this.generation) {
          return;
        }
        this.pollFailures = 0;
        this.lastSuccessUtc = new Date();
        this.applyJob(job);
        this.cdr.markForCheck();
      },
      error: (error: unknown) => {
        if (generation !== this.generation) {
          return;
        }
        this.pollFailures++;
        const http = error instanceof HttpErrorResponse ? error : null;
        this.lastPollError = {
          httpStatus: http && http.status > 0 ? http.status : null,
          message: (http ? serverMessage(http) ?? (http.statusText || http.message) : null) || 'Unknown error'
        };
        this.pollError = 'The job’s progress could not be read. Retrying.';
        this.schedulePoll(Math.min(REPORT_PACK_POLL_MS * 2 ** this.pollFailures, REPORT_PACK_POLL_MAX_BACKOFF_MS));
        this.cdr.markForCheck();
      }
    });
  }

  /** Keeps `busyChange` and the one-second clock in step with whether a job is running. */
  private syncRunning(deferEvent = false): void {
    const running = this.jobInProgress;
    if (running && this.activeJobId !== null) {
      if (!this.tickSub && !this.destroyed) {
        this.tickSub = interval(1000).subscribe(() => this.cdr.markForCheck());
      }
    } else {
      this.stopTick();
    }
    if (running !== this.lastBusy) {
      this.lastBusy = running;
      if (deferEvent) {
        void Promise.resolve().then(() => this.busyChange.emit(running));
      } else {
        this.busyChange.emit(running);
      }
    }
  }

  private stopTick(): void {
    this.tickSub?.unsubscribe();
    this.tickSub = null;
  }

  // -------------------------------------------------------------------------------------------
  // Writers
  // -------------------------------------------------------------------------------------------

  private loadWriters(): void {
    this.writersLoading = true;
    this.writersError = null;
    this.writersSub?.unsubscribe();
    this.writersSub = this.adminService.getSystemConfigs().subscribe({
      next: configs => {
        this.writersLoading = false;
        this.writersLoaded = true;
        this.writers = (configs ?? []).filter(c => (c.modelRole & 4) === 4 && c.hasApiKey && c.isEnabled);
        this.applyRememberedWriter();
        this.cdr.markForCheck();
      },
      error: () => {
        this.writersLoading = false;
        this.writersError = 'The report writers could not be loaded.';
        this.cdr.markForCheck();
      }
    });
  }

  /** The last writer used, while it still qualifies and no writer is chosen. */
  private applyRememberedWriter(): void {
    if (!this.writersLoaded || this.writerId !== null || this.currentContext === null) {
      return;
    }
    const remembered = readStoredSettings().writerConfigId ?? null;
    if (remembered !== null && this.writers.some(w => w.id === remembered)) {
      this.selectWriter(remembered);
    }
  }

  // -------------------------------------------------------------------------------------------
  // Diagnostics
  // -------------------------------------------------------------------------------------------

  diagnosticsText(nowUtc: Date = new Date()): string {
    return buildReportPackDiagnostics({
      context: this.currentContext,
      job: this.job,
      subjectKey: this.chosenKeys[0] ?? null,
      chartSelection: this.chartSelection,
      chartStatus: this.chartStatus,
      chartStorageMissing: this.chartStorageMissing,
      chartAdvisory: this.chartAdvisory,
      client: {
        pollCount: this.pollCount,
        lastSuccessUtc: this.lastSuccessUtc,
        consecutiveFailures: this.pollFailures,
        lastError: this.lastPollError
      },
      estimateUsd: this.jobEstimateUsd,
      nowUtc
    });
  }

  async copyDiagnostics(): Promise<void> {
    const generation = this.generation;
    this.copyStatus = '';
    this.copyError = null;
    this.cdr.markForCheck();
    const ok = await reportPackIo.copy(this.diagnosticsText());
    if (generation !== this.generation) {
      return;
    }
    if (ok) {
      this.copyStatus = 'Copied';
    } else {
      this.copyError = 'The diagnostics could not be copied to the clipboard. Download them instead.';
    }
    this.cdr.markForCheck();
  }

  downloadDiagnostics(): void {
    const now = new Date();
    const first = this.chosenKeys[0];
    const subject = this.job?.subjectLabel ?? (first ? this.modelLabel(first) : null);
    reportPackIo.download(reportPackDiagnosticsFileName(subject, now), this.diagnosticsText(now));
  }

  logTime(timestampUtc: string): string {
    return formatUtcSeconds(timestampUtc) ?? timestampUtc;
  }

  // -------------------------------------------------------------------------------------------
  // Housekeeping
  // -------------------------------------------------------------------------------------------

  private clearPollTimer(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }

  private stopTimers(): void {
    this.latestTicket = null;
    this.previewQueue.next(null);
    this.clearPollTimer();
    this.listSub?.unsubscribe();
    this.listSub = null;
    for (const key of Object.keys(this.subscriptions)) {
      this.subscriptions[key]?.unsubscribe();
      this.subscriptions[key] = undefined;
    }
  }
}
