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
  BenchmarkReportPackDocumentsDto,
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
import { ModelIdentityComponent } from '../../../shared/model-identity/model-identity.component';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { LoadingIndicatorComponent } from '../../../shared/loading-indicator/loading-indicator.component';
import { PdfViewerDialogComponent } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { rovingTabIndex } from '../../../utils/roving-tab.util';
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
  ComparisonModelPickerModel,
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
  ReportCostRange,
  ReportPackAudienceOption,
  audienceLabel,
  documentChipClass,
  documentPhaseLine,
  documentProgressLabel,
  documentRowPhaseLine,
  formatCostRangeUsd,
  formatCostUsd,
  formatElapsed,
  formatUtc,
  isCostRange,
  reportAudiencesFor,
  statusLabel
} from './report-document-format';
import {
  ReportJobDocumentCard,
  ReportJobDocumentsComponent,
  reportJobDocumentCost,
  reportJobDocumentTokens,
  reportJobDocumentTone,
  reuseReportJobDocumentCards
} from './report-job-documents.component';
import {
  buildReportPackDiagnostics,
  reportPackDiagnosticsFileName,
  reportPackIo,
  reportPackWriterRepliesFileName
} from './report-pack-diagnostics';
import { documentStatusQuestion, documentStatusSubject } from './report-validation-explanation';

/** What the Write step works on: the comparison request, its entries and its suite. */
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
  /**
   * The GnollBench baseline model's entry when a charted entry is it; absent or null otherwise. The
   * default Reference model, and kept among the covered models when there are more than the cap.
   */
  readonly baselineModelEntryKey?: string | null;
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

/** One document type in the Preview layout popover; `reason` holds the item back. */
export interface LayoutPreviewItem {
  readonly audience: BenchmarkReportAudience;
  readonly label: string;
  /** `5 charts`, `1 chart`: the chosen figures the comparison can draw. */
  readonly detail: string;
  readonly reason: string | null;
}

/** The charts line of a document's progress card: its words, which are the retry button's after a failure. */
export interface ReportPackChartCell {
  readonly text: string;
  readonly retry: boolean;
  /** The charts are being drawn and attached now. */
  readonly pending: boolean;
  /** The document has charts, or is getting them: the card shows the line. */
  readonly shown: boolean;
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

/** The Reference model option that sends no reference: the server judges against the highest Intelligence Index. */
export const REPORT_PACK_HIGHEST_INDEX_REFERENCE = 'Highest Intelligence Index';

/**
 * The note in the job card while charts can still be pending: this browser draws the charts while the
 * wizard is open, and a document that finishes while it is closed gets none.
 */
export const REPORT_PACK_KEEP_OPEN_TEXT =
  'Keep this wizard open until every document shows its charts: this browser draws them as each document '
  + 'finishes. If you close it, add them later on step 4 with Update charts…';

/** The info tip beside New report pack: where these reports are kept, and where a run's own reports are written. */
export const REPORT_PACK_LEAD_TEXT =
  'These reports are kept with the comparison: step 4 lists them, and so does Comparison reports on the Model '
  + 'Comparison tab. A run\'s or battery run\'s own reports are written in the AI Reports tab of its report.';

/** The server's refusal of a subject with no peer, word for word (`BenchmarkReportPackPreparation.PeerlessReportRefusal`). */
export const REPORT_PACK_PEERLESS_REFUSAL =
  'A comparison report compares one model with at least one other. To write a run\'s or a battery run\'s own '
  + 'reports, use the AI Reports tab of its report.';

/**
 * The server's refusal of a document whose prompt is too large for the writer, word for word
 * (`AdminBenchmarkReportPacksController.ContextWindowRefusal`).
 */
export function reportPackContextWindowRefusal(
  estimate: BenchmarkReportPackAudienceEstimateDto,
  writerDisplayName: string | null | undefined
): string {
  const share = Math.round((estimate.contextWindowShare ?? 0) * 100);
  return `The ${audienceLabel(estimate.audience)} prompt is about `
    + `${Math.round(estimate.estimatedInputTokens).toLocaleString('en-US')} tokens, ${share} % of the context window `
    + `of ${writerDisplayName ?? 'the writer'}; above ${Math.round(REPORT_PACK_CONTEXT_REFUSAL_SHARE * 100)} % it is `
    + 'not written. Cover fewer models, or choose a writer with a larger context window.';
}

/** Why Generate is unavailable when every listed document is written and none is chosen to be rewritten. */
export const REPORT_PACK_ALL_WRITTEN_REASON =
  'Every document listed is already written. Check Rewrite on one to replace it, or delete it.';

/** Why Generate is unavailable when the checked documents are not the same for every model checked. */
export const REPORT_PACK_UNEVEN_MODELS_REASON =
  'One job writes the same documents for every model it covers. Check the same documents for each model, or write them in separate jobs.';

/** Why Generate is unavailable while a job is starting or running; the main area shows the job. */
export const REPORT_PACK_JOB_RUNNING_REASON = 'A report pack is being written. Wait for it to finish, or cancel it.';

/** Why Generate is unavailable while the estimate is computed; the estimate panel shows *Estimating…*. */
export const REPORT_PACK_ESTIMATING_REASON = 'Checking the writer and estimating the cost…';

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

/** The statuses whose chip has an explanation beside it. */
const EXPLAINED_DOCUMENT_STATUSES = new Set(['CompletedWithWarnings', 'Failed']);

/**
 * The status line while the writer's replies are fetched, after a 404 and, followed by the status code,
 * after any other failure.
 */
export const REPORT_PACK_REPLIES_PREPARING_TEXT = 'Preparing the writer\'s replies…';
export const REPORT_PACK_REPLIES_GONE_TEXT =
  'The writer\'s replies are no longer held: Overseer keeps only the latest report job, and a newer job or a restart replaced this one.';
export const REPORT_PACK_REPLIES_FAILED_TEXT = 'The writer\'s replies could not be downloaded';

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
export interface StoredReportPackSettings {
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

/** The sources and pricing basis of a comparison's Report Pack requests; battery ids only for a comparison of battery results. */
function reportPackRequestBase(
  context: ReportPackContext
): Pick<BenchmarkReportPackRequest, 'runIds' | 'groupIds' | 'batteryRunIds' | 'pricingBasis'> {
  const batteryRunIds = context.batteryRunIds ?? [];
  return {
    runIds: [...context.runIds],
    groupIds: [...context.groupIds],
    ...(batteryRunIds.length > 0 ? { batteryRunIds: [...batteryRunIds] } : {}),
    pricingBasis: context.pricingBasis === 'AsRun'
      ? BenchmarkReportPackPricingBasis.AsRun
      : BenchmarkReportPackPricingBasis.Current
  };
}

/** What a Report Pack request carries after its models: the documents, the writer, the acknowledgment and a start's replacements. */
type ReportPackRequestTail =
  Pick<BenchmarkReportPackRequest, 'audiences' | 'writerModelConfigurationId' | 'acknowledgeSameProvider' | 'replaceDocumentIds'>;

/** A comparison-scope request over `coveredKeys`. */
function comparisonReportPackRequest(
  context: ReportPackContext,
  coveredKeys: readonly string[],
  referenceEntryKey: string | null,
  tail: ReportPackRequestTail
): BenchmarkReportPackRequest {
  return {
    ...reportPackRequestBase(context), subjectKey: '', scope: BenchmarkReportScope.Comparison, coveredEntryKeys: [...coveredKeys], ...tail,
    // Sent only when named, so a request without a reference is unchanged by it.
    ...(referenceEntryKey !== null ? { referenceEntryKey } : {})
  };
}

/**
 * The panel's first comparison-scope preview request for `context`, with no writer: the default covered
 * models and Reference model and every comparison document type, as the panel asks on opening. Null when
 * the stored document scope is One model at a time or no model is offered. The server's preview cache
 * leaves the writer out of its key, so this request prepares what the panel's first preview reads.
 */
export function initialComparisonPreviewRequest(
  context: ReportPackContext,
  stored: StoredReportPackSettings = readStoredSettings()
): BenchmarkReportPackRequest | null {
  if ((stored.documentScope ?? 'comparison') !== 'comparison') {
    return null;
  }
  const baseline = context.baselineModelEntryKey ?? null;
  const covered = defaultCoveredKeys(comparisonModelOptions(context.entries), baseline);
  if (covered.length === 0) {
    return null;
  }
  return comparisonReportPackRequest(context, covered, baseline !== null && covered.includes(baseline) ? baseline : null, {
    audiences: reportAudiencesFor('comparison').map(option => option.audience),
    writerModelConfigurationId: 0,
    acknowledgeSameProvider: false
  });
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

/** One request for the stored documents, with what it was asked for. */
interface DocumentsTicket {
  readonly generation: number;
  readonly request: BenchmarkReportPackRequest;
  /** The scope and models: what the document list answers. */
  readonly listSignature: string;
}

interface DocumentsAnswer {
  readonly ticket: DocumentsTicket;
  /** Null when the request failed. */
  readonly documents: BenchmarkReportPackDocumentsDto | null;
}

/** A written comparison-scope document and the reference model it was written against. */
export interface ReportPackWrittenReference {
  /** The document's row key. */
  readonly key: string;
  /** The document type's name. */
  readonly name: string;
  /** The reference model's label, or *Highest Intelligence Index*. */
  readonly reference: string;
  /** It was written against another reference than the one chosen now. */
  readonly differs: boolean;
}

/** A covered model's letter in the anonymized copies. */
export interface ReportPackLetterPair {
  readonly letter: string;
  /** The preview's label for the model. */
  readonly label: string;
  /** The Models picker's model of the same entry, for its badges; null when no option matches. */
  readonly model: ComparisonModelPickerModel | null;
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
 * written or not; and a main area that follows the job, one card per document, and its log,
 * diagnostics and the writer's replies. The written documents are also step 4's.
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
    ComparisonDocumentsStatusComponent, PdfViewerDialogComponent, ReportJobDocumentsComponent, ModelIdentityComponent,
    LoadingIndicatorComponent
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
  @ViewChild('previewTrigger') previewTrigger?: ElementRef<HTMLButtonElement>;
  @ViewChild('previewMenu') previewMenu?: ElementRef<HTMLElement>;
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

  /** The prefix of every id in the panel. */
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

  /** The documents the chosen scope writes, in audience order. */
  get audiences(): readonly ReportPackAudienceOption[] {
    return reportAudiencesFor(this.scopeMode === 'comparison' ? 'comparison' : 'model');
  }

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
  readonly highestIndexReference = REPORT_PACK_HIGHEST_INDEX_REFERENCE;
  readonly keepOpenText = REPORT_PACK_KEEP_OPEN_TEXT;
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
  /**
   * The admin's Reference model under Whole comparison: an entry key, `''` for the highest
   * Intelligence Index, or null for the default (the baseline model when it is covered).
   */
  private referenceChoice: string | null = null;
  private writtenReferencesSource: unknown[] | null = null;
  private writtenReferencesCache: readonly ReportPackWrittenReference[] = [];
  private referencePickerOptionsSource: unknown[] | null = null;
  private referencePickerOptionsCache: ComparisonModelOption[] = [];
  /** The admin's own Write and Rewrite choices, by row key; a row without one takes its default. */
  private readonly checks = new Map<string, boolean>();
  /** The comparison's stored documents, by id, for each row's flags and chart count. */
  private listItems = new Map<number, BenchmarkReportDocumentListItemDto>();
  /** The list built from the stored documents, else the last preview, answered for the current scope and models. */
  documentsView: ComparisonDocumentsView = comparisonDocumentsView({
    mode: 'comparison', preview: null, documents: null, error: null, chosenKeys: [], offeredKeys: [], labelOf: key => key,
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
  private letterPairsPreview: BenchmarkReportPackPreviewDto | null | undefined = undefined;
  private letterPairsScope: ReportDocumentScopeMode | null = null;
  private letterPairsOptions: ComparisonModelOption[] | null = null;
  private letterPairsCache: readonly ReportPackLetterPair[] | null = null;

  /** The last preview answered for the current scope and models; its estimate is current while nothing is pending. */
  preview: BenchmarkReportPackPreviewDto | null = null;
  /** The scope and models `preview` answers. */
  private previewListSignature: string | null = null;
  /** The last stored documents answered, listed with nothing prepared; the rows come from it while it is current. */
  private listDocuments: BenchmarkReportPackDocumentsDto | null = null;
  /** The scope and models `listDocuments` answers. */
  private listDocumentsSignature: string | null = null;
  /** A change is waiting out the debounce or the writer list, or its preview is in flight. */
  previewPending = false;
  /** A preview waits for the writer list, so it is sent with the remembered writer. */
  private previewAwaitingWriters = false;
  previewError: string | null = null;
  /** The same-provider warning a 409 carried, shown until the models or writer change. */
  private serverWarning: string | null = null;
  /** The same-provider confirmation's sentence. */
  confirmWarningText = '';

  /** Which figures the last layout preview could not draw. */
  layoutPreviewNote = '';
  private layoutPreviewReturnFocus: HTMLElement | null = null;
  /** Whether the Preview layout popover is open, for its trigger's aria-expanded and chevron. */
  previewMenuOpen = false;
  private previewItemsSelection: ReportChartSelection | null = null;
  private previewItemsAvailable: readonly string[] | null = null;
  private previewItemsAudiences: BenchmarkReportAudience[] | null = null;
  private previewItemsCache: readonly LayoutPreviewItem[] = [];

  starting = false;
  startError: string | null = null;

  /** The job this panel follows, and its last known state. */
  activeJobId: string | null = null;
  job: BenchmarkReportPackJobDto | null = null;
  /** When `job` was last read (client clock), to run the server's clock on between readings. */
  jobReadAt = 0;
  /** The estimate when the job was started here, for its stat strip: first calls alone to one repair turn per document. */
  jobEstimateUsd: ReportCostRange | null = null;
  pollError: string | null = null;
  canceling = false;

  // --- Client polling, for the diagnostics ---
  pollCount = 0;
  lastSuccessUtc: Date | null = null;
  lastPollError: ClientPollError | null = null;

  copyStatus = '';
  copyError: string | null = null;
  /** The writer's replies are being fetched: their button is aria-disabled. */
  repliesLoading = false;
  /** The cards last shown, kept while nothing on them changes. */
  private documentCards: readonly ReportJobDocumentCard[] = [];

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
  private latestDocumentsTicket: DocumentsTicket | null = null;
  private readonly documentsQueue = new Subject<DocumentsTicket | null>();
  /** Not debounced; a newer ticket drops the request in flight, and an answer for an older one is ignored. */
  private readonly documentsPipeline: Subscription = this.documentsQueue.pipe(
    switchMap(ticket => ticket
      ? this.benchmarkService.listReportPackDocuments(ticket.request).pipe(
        map((documents): DocumentsAnswer => ({ ticket, documents })),
        catchError(() => of<DocumentsAnswer>({ ticket, documents: null })))
      : EMPTY)
  ).subscribe(answer => this.onDocumentsAnswer(answer));
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private pollFailures = 0;
  private tickSub: Subscription | null = null;
  private writersSub: Subscription | null = null;
  private listSub: Subscription | null = null;
  /** The comparison whose document list is in flight; null when none is. */
  private listRequestComparisonId: number | null = null;
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
    this.documentsPipeline.unsubscribe();
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
        const baselineChanged = (context.baselineModelEntryKey ?? null) !== (previous.baselineModelEntryKey ?? null);
        if ((modelsChanged || baselineChanged || context.pricingBasis !== previous.pricingBasis) && !this.jobInProgress) {
          this.scheduleDocuments();
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
    const baseline = this.baselineKey;
    const hadAll = before.length > 0
      && defaultCoveredKeys(this.modelOptions, baseline).every(key => this.coveredKeys.includes(key));
    this.modelOptions = comparisonModelOptions(context.entries);
    const offered = this.modelOptions.map(option => String(option.key));
    if (offered.join(',') === before.join(',')) {
      return false;
    }
    this.coveredKeys = hadAll
      ? defaultCoveredKeys(this.modelOptions, baseline)
      : offered.filter(key => this.coveredKeys.includes(key));
    if (this.coveredKeys.length < MIN_COMPARISON_DOCUMENT_ENTRIES) {
      this.coveredKeys = defaultCoveredKeys(this.modelOptions, baseline);
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
    this.coveredKeys = defaultCoveredKeys(this.modelOptions, this.baselineKey);
    const best = defaultSubjectKey(this.modelOptions);
    this.subjectKeys = best ? [best] : [];
    this.referenceChoice = null;
    this.checks.clear();
    this.listItems = new Map();
    this.writerId = null;
    this.preview = null;
    this.previewListSignature = null;
    this.listDocuments = null;
    this.listDocumentsSignature = null;
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
      this.scheduleDocuments();
      this.schedulePreview();
      this.refreshListItems(true);
      this.loadActiveJob(this.generation);
    } else {
      this.refreshDocumentsView();
    }
    this.cdr.markForCheck();
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
    this.scheduleDocuments();
    this.schedulePreview();
  }

  /** Arrow keys wrap, Home and End jump; focus follows the selection. */
  onScopeKeydown(event: KeyboardEvent, index: number): void {
    const target = rovingTabIndex(event, index, this.scopeOptions.length);
    if (target === null) return;
    const next = this.scopeOptions[target];
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
    this.scheduleDocuments();
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

  /** What Generate writes, in one line under the picker, and how many entries are Excluded and not offered. */
  get modelsLine(): string {
    const excluded = this.excludedNote;
    return excluded ? `${this.writesLine} ${excluded}` : this.writesLine;
  }

  private get writesLine(): string {
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

  /** `1 entry of this comparison is Excluded and is not offered.`, or null when none is. */
  private get excludedNote(): string | null {
    const excluded = (this.currentContext?.entries.length ?? 0) - this.modelOptions.length;
    if (excluded <= 0) {
      return null;
    }
    return excluded === 1
      ? '1 entry of this comparison is Excluded and is not offered.'
      : `${excluded} entries of this comparison are Excluded and are not offered.`;
  }

  /** Why a whole-comparison choice starts with fewer than every model, or null. */
  get modelsCapNote(): string | null {
    if (this.scopeMode !== 'comparison' || this.modelOptions.length <= MAX_COMPARISON_DOCUMENT_ENTRIES) {
      return null;
    }
    const baseline = this.baselineKey;
    if (baseline !== null && this.modelOptions.some(option => option.key === baseline)) {
      return `A document covers at most ${MAX_COMPARISON_DOCUMENT_ENTRIES} models, so the baseline model and the `
        + `${MAX_COMPARISON_DOCUMENT_ENTRIES - 1} others with the highest Intelligence Index of the `
        + `${this.modelOptions.length} are chosen first.`;
    }
    return `A document covers at most ${MAX_COMPARISON_DOCUMENT_ENTRIES} models, so the ${MAX_COMPARISON_DOCUMENT_ENTRIES} `
      + `with the highest Intelligence Index of the ${this.modelOptions.length} are chosen first.`;
  }

  // -------------------------------------------------------------------------------------------
  // The reference model
  // -------------------------------------------------------------------------------------------

  /** The baseline model's charted entry, from the comparison; null when none is. */
  get baselineKey(): string | null {
    return this.currentContext?.baselineModelEntryKey ?? null;
  }

  /**
   * The Reference model a comparison-scope request names: the admin's choice while it is covered, else
   * the baseline model while it is covered, else null (the highest Intelligence Index). Null under One
   * model at a time.
   */
  get referenceEntryKey(): string | null {
    if (this.scopeMode !== 'comparison') {
      return null;
    }
    const choice = this.referenceChoice;
    if (choice === '') {
      return null;
    }
    if (choice !== null && this.coveredKeys.includes(choice)) {
      return choice;
    }
    const baseline = this.baselineKey;
    return baseline !== null && this.coveredKeys.includes(baseline) ? baseline : null;
  }

  /**
   * The Reference model picker's options after *Highest Intelligence Index*: each covered model, in the
   * covered order, the baseline model tagged *Baseline*.
   */
  get referencePickerOptions(): ComparisonModelOption[] {
    const source: unknown[] = [this.coveredKeys, this.modelOptions, this.currentContext];
    const cached = this.referencePickerOptionsSource;
    if (cached && cached.every((item, i) => item === source[i])) {
      return this.referencePickerOptionsCache;
    }
    this.referencePickerOptionsSource = source;
    const baseline = this.baselineKey;
    this.referencePickerOptionsCache = this.coveredKeys.flatMap(key => {
      const option = this.modelOptions.find(candidate => candidate.key === key);
      if (!option) {
        return [];
      }
      return [key === baseline ? { ...option, tag: 'Baseline' } : option];
    });
    return this.referencePickerOptionsCache;
  }

  /** How the written documents and the mismatch note name a reference. */
  referenceLabel(key: string | null): string {
    if (key === null) {
      return REPORT_PACK_HIGHEST_INDEX_REFERENCE;
    }
    const known = this.modelOptions.some(option => option.key === key);
    const entry = this.currentContext?.entries.find(candidate => candidate.key === key);
    const label = known
      ? comparisonModelMention(key, this.modelOptions)
      : entry ? (entry.label || entry.modelDisplayName) : key;
    return key === this.baselineKey ? `${label} (baseline model)` : label;
  }

  onReferencePicked(key: ModelPickerKey | null): void {
    this.referenceChoice = key === null ? '' : String(key);
    this.serverWarning = null;
    this.startError = null;
    this.schedulePreview();
  }

  /** The written comparison-scope documents of the chosen models, each with its reference model. */
  get writtenReferences(): readonly ReportPackWrittenReference[] {
    const rows = this.scopeMode === 'comparison'
      ? this.documentsView.groups.flatMap(group => group.rows).filter(row => row.document !== null)
      : [];
    const current = this.referenceEntryKey;
    const source: unknown[] = [
      rows.length, ...rows.map(row => row.document), ...rows.map(row => row.listItem), current, this.modelOptions,
      this.currentContext
    ];
    const cached = this.writtenReferencesSource;
    if (cached && cached.length === source.length && cached.every((item, i) => item === source[i])) {
      return this.writtenReferencesCache;
    }
    this.writtenReferencesSource = source;
    this.writtenReferencesCache = rows.map(row => {
      const reference = row.document?.referenceEntryKey ?? row.listItem?.referenceEntryKey ?? null;
      return { key: row.key, name: row.name, reference: this.referenceLabel(reference), differs: reference !== current };
    });
    return this.writtenReferencesCache;
  }

  /** The amber note when a written document's reference is not the one chosen now, or null. */
  get referenceMismatchNote(): string | null {
    const differing = this.writtenReferences.filter(item => item.differs);
    if (differing.length === 0) {
      return null;
    }
    const one = differing.length === 1;
    const names = differing.map(item => `${item.name} (${item.reference})`).join(', ');
    return `${one ? 'A written document was' : 'Written documents were'} written against another reference model `
      + `than the one chosen: ${names}. Check Rewrite to write ${one ? 'it' : 'them'} against `
      + `${this.referenceLabel(this.referenceEntryKey)}.`;
  }

  /** Each covered model's letter, label and picker model, once the preview has lettered them; null under One model at a time. */
  get coveredLetterPairs(): readonly ReportPackLetterPair[] | null {
    const preview = this.listPreview;
    if (preview === this.letterPairsPreview && this.scopeMode === this.letterPairsScope
      && this.modelOptions === this.letterPairsOptions) {
      return this.letterPairsCache;
    }
    this.letterPairsPreview = preview;
    this.letterPairsScope = this.scopeMode;
    this.letterPairsOptions = this.modelOptions;
    const lettered = this.scopeMode === 'comparison'
      ? (preview?.coveredModels ?? []).filter(model => !!model.letter)
      : [];
    this.letterPairsCache = lettered.length === 0
      ? null
      : lettered.map(model => ({
        letter: model.letter as string,
        label: model.label,
        model: this.modelOptions.find(option => option.key === model.entryKey)?.model ?? null
      }));
    return this.letterPairsCache;
  }

  /** The preview answered for the current scope and models, or null while it is awaited. */
  private get listPreview(): BenchmarkReportPackPreviewDto | null {
    return this.previewListSignature === this.listSignature ? this.preview : null;
  }

  /** The stored documents answered for the current scope and models, or null while they are awaited. */
  private get listedDocuments(): BenchmarkReportPackDocumentsDto | null {
    return this.listDocumentsSignature === this.listSignature ? this.listDocuments : null;
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

  /** The preview estimates every document, so a check recomputes the total, the warning and the refusal here. */
  onDocumentCheck(change: { key: string; checked: boolean }): void {
    this.checks.set(change.key, change.checked);
    this.startError = null;
    this.refreshDocumentsView();
    this.cdr.markForCheck();
  }

  onDocumentDeleted(documentId: number): void {
    this.listItems.delete(documentId);
    for (const row of this.allRows()) {
      if (row.document?.documentId === documentId) {
        this.checks.delete(row.key);
      }
    }
    this.documentsChanged.emit();
    this.scheduleDocuments();
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
      documents: this.listedDocuments,
      error: this.listPreview ? null : this.previewError ?? unlisted,
      chosenKeys: this.chosenKeys,
      offeredKeys: this.modelOptions.map(option => String(option.key)),
      labelOf: key => this.modelLabel(key),
      listItems: this.listItems,
      checks: this.checks
    });
    const audiences = this.documentsView.state === 'ready'
      ? new Set(this.checkedRows.map(row => row.audience))
      : new Set(this.audiences.map(option => option.audience));
    const next = this.audiences.map(option => option.audience).filter(audience => audiences.has(audience));
    if (next.join(',') !== this.checkedAudiences.join(',')) {
      this.checkedAudiences = next;
    }
  }

  /**
   * The written documents' list entries, for their flags and chart counts. Forced, they are fetched as soon
   * as the comparison has a number, without waiting for the preview; otherwise when a listed document is
   * missing and no list of the comparison is in flight. The entries merge into the rows in either order.
   */
  private refreshListItems(force = false): void {
    const comparisonId = this.currentComparisonId ?? this.preview?.comparisonId ?? this.listDocuments?.comparisonId ?? null;
    if (comparisonId === null || this.destroyed || !this.currentContext) {
      return;
    }
    if (!force) {
      if (this.listRequestComparisonId === comparisonId) {
        return;
      }
      const ids = this.allRows().map(row => row.document?.documentId).filter((id): id is number => typeof id === 'number');
      if (ids.every(id => this.listItems.has(id))) {
        return;
      }
    }
    const generation = this.generation;
    this.listSub?.unsubscribe();
    this.listRequestComparisonId = comparisonId;
    this.listSub = this.benchmarkService.listReportDocuments({ comparisonId }).subscribe({
      next: items => {
        if (generation !== this.generation) {
          return;
        }
        this.listRequestComparisonId = null;
        this.listItems = new Map((items ?? []).map(item => [item.id, item]));
        this.refreshDocumentsView();
        this.cdr.markForCheck();
      },
      error: () => {
        // The rows stay without their flags and chart counts.
        if (generation === this.generation) {
          this.listRequestComparisonId = null;
        }
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
    return this.chartAudience ?? this.checkedAudiences[0] ?? this.audiences[0].audience;
  }

  /** Why Preview layout cannot run for any document type, or null. */
  get layoutPreviewGlobalReason(): string | null {
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
    return null;
  }

  /** Why one document type's layout cannot be previewed, or null. */
  layoutPreviewAudienceReason(audience: BenchmarkReportAudience): string | null {
    return this.previewFigureCount(audience) === 0 ? `No chart is chosen for the ${audienceLabel(audience)}.` : null;
  }

  private previewFigureCount(audience: BenchmarkReportAudience): number {
    return (this.chartSelection[audience] ?? []).filter(key => this.chartsAvailable.includes(key)).length;
  }

  /** The Preview layout popover's items: every document type, in tab order, checked under Documents or not. */
  get layoutPreviewItems(): readonly LayoutPreviewItem[] {
    if (this.previewItemsSelection !== this.chartSelection || this.previewItemsAvailable !== this.chartsAvailable
      || this.previewItemsAudiences !== this.checkedAudiences) {
      this.previewItemsSelection = this.chartSelection;
      this.previewItemsAvailable = this.chartsAvailable;
      this.previewItemsAudiences = this.checkedAudiences;
      this.previewItemsCache = this.audiences.map(option => {
        const count = this.previewFigureCount(option.audience);
        return {
          audience: option.audience,
          label: audienceLabel(option.audience),
          detail: plural(count, 'chart', 'charts'),
          reason: this.layoutPreviewAudienceReason(option.audience)
        };
      });
    }
    return this.previewItemsCache;
  }

  /** The popover's toggle event: aria-expanded, and focus into the popover or back to the trigger. */
  onPreviewMenuToggle(event: Event): void {
    const open = (event as ToggleEvent).newState === 'open';
    this.previewMenuOpen = open;
    const popover = this.previewMenu?.nativeElement;
    if (open) {
      refreshAnchorPositioning();
      const current = popover?.querySelector<HTMLElement>(
        `.gh-action-popover-item[data-audience="${this.chartPreviewAudience}"]:not([aria-disabled="true"])`);
      (current ?? popover?.querySelector<HTMLElement>('.gh-action-popover-item:not([aria-disabled="true"])'))?.focus();
    } else {
      const active = document.activeElement;
      if (!active || active === document.body || !!popover?.contains(active)) {
        this.previewTrigger?.nativeElement.focus();
      }
    }
    this.cdr.markForCheck();
  }

  /** Escape closes the popover only; the wizard's dialog stays open. */
  onPreviewMenuKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape') {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.hidePreviewMenu();
    this.previewTrigger?.nativeElement.focus();
  }

  private hidePreviewMenu(): void {
    try {
      this.previewMenu?.nativeElement.hidePopover();
    } catch {
      // Already hidden.
    }
    this.previewMenuOpen = false;
    this.cdr.markForCheck();
  }

  /** Preview layout of one document type: the viewer opens at once, and composes and fetches the PDF as it loads. */
  previewLayout(audience: BenchmarkReportAudience): void {
    const compose = this.documentChartsComposer;
    const request = this.buildRequest({ preview: true, layout: true });
    if (this.layoutPreviewGlobalReason !== null || this.layoutPreviewAudienceReason(audience) !== null || !compose || !request) {
      return;
    }
    this.hidePreviewMenu();
    this.layoutPreviewNote = '';
    this.layoutPreviewReturnFocus = this.previewTrigger?.nativeElement ?? null;
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

  /**
   * Why the documents cannot be written as chosen: the current preview's refusal, else the context-window
   * refusal of the first checked document too large for the writer; null while the preview is pending.
   */
  get refusal(): string | null {
    const preview = this.answeredPreview;
    if (!preview) {
      return null;
    }
    if (preview.refusal) {
      return preview.refusal;
    }
    const over = this.checkedEstimates.find(item => item.estimate.exceedsContextWindow === true);
    return over
      ? reportPackContextWindowRefusal(over.estimate, preview.writerDisplayName ?? this.selectedWriter?.displayName)
      : null;
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
   * The request body, or null while it cannot be built. A preview asks for every chosen model and every
   * document type of the scope, with or without a writer, so the checks never change it; a start asks for
   * what is checked, and names the written documents it replaces. A layout preview asks for
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
    const checked = this.checkedRows;
    const audiences = options.preview
      ? this.audiences.map(option => option.audience)
      : this.audiences.map(option => option.audience).filter(audience => checked.some(row => row.audience === audience));
    const replaceDocumentIds = checked
      .map(row => row.document?.documentId)
      .filter((id): id is number => typeof id === 'number');
    const tail: ReportPackRequestTail = {
      audiences,
      writerModelConfigurationId: this.writerId ?? 0,
      acknowledgeSameProvider: options.acknowledgeSameProvider ?? false,
      ...(options.preview ? {} : { replaceDocumentIds })
    };
    if (this.scopeMode === 'comparison') {
      return comparisonReportPackRequest(context, chosen, this.referenceEntryKey, tail);
    }
    const subjects = options.layout
      ? [chosen[0]]
      : options.preview
        ? [...chosen]
        : chosen.filter(key => checked.some(row => row.entryKey === key));
    if (subjects.length === 0) {
      return null;
    }
    return { ...reportPackRequestBase(context), subjectKey: subjects[0], scope: BenchmarkReportScope.Model, subjectKeys: subjects, ...tail };
  }

  // -------------------------------------------------------------------------------------------
  // The preview: the documents and the estimate
  // -------------------------------------------------------------------------------------------

  /**
   * Asks for the preview after the debounce; while the writer list is loading, after it arrives
   * instead. Changing the scope or the models lists the documents afresh; changing the writer or the
   * reference keeps the list and estimates again.
   */
  schedulePreview(): void {
    const request = this.buildRequest({ preview: true });
    if (this.previewListSignature !== this.listSignature) {
      this.preview = null;
      this.previewListSignature = null;
    }
    this.previewError = null;
    this.previewAwaitingWriters = false;
    if (request && this.writersLoading && !this.destroyed) {
      this.latestTicket = null;
      this.previewQueue.next(null);
      this.previewAwaitingWriters = true;
      this.previewPending = true;
    } else if (!request || this.destroyed) {
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
    // A request that changed since this one was sent (models, scope, reference or writer) is asked again.
    const now = this.buildRequest({ preview: true });
    if (now && JSON.stringify(now) !== ticket.signature) {
      this.schedulePreview();
      return;
    }
    this.refreshListItems();
    this.cdr.markForCheck();
  }

  /**
   * Asks at once for the stored documents of the current scope and models, which the server lists
   * without preparing anything, so the rows need not wait for the preview. Asked again when the
   * documents or the models can have changed; a writer or reference change keeps the answer.
   */
  private scheduleDocuments(): void {
    const request = this.buildRequest({ preview: true });
    if (!request || this.destroyed) {
      this.latestDocumentsTicket = null;
      this.documentsQueue.next(null);
      return;
    }
    const ticket: DocumentsTicket = { generation: this.generation, request, listSignature: this.listSignature };
    this.latestDocumentsTicket = ticket;
    this.documentsQueue.next(ticket);
  }

  /** A failed request leaves the rows to the preview. */
  private onDocumentsAnswer(answer: DocumentsAnswer): void {
    const ticket = answer.ticket;
    if (this.destroyed || ticket.generation !== this.generation || ticket !== this.latestDocumentsTicket || !answer.documents) {
      return;
    }
    this.listDocuments = answer.documents;
    this.listDocumentsSignature = ticket.listSignature;
    this.refreshDocumentsView();
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
    if (!preview || this.refusal) {
      return null;
    }
    const range = this.estimateTotalUsd;
    if (range === null) {
      return { state: 'noPrice', total: null, parts: [] };
    }
    const items = this.checkedEstimates;
    const several = this.scopeMode === 'model' && new Set(items.map(item => item.row.entryKey)).size > 1;
    const parts = items.length > 1
      ? items.map(item => ({
        name: several ? `${item.row.name} — ${item.row.subjectLabel}` : item.row.name,
        cost: formatCostRangeUsd(item.estimate.estimatedCostUsd, item.estimate.estimatedCostWithRepairUsd)
      }))
      : [];
    return { state: 'ready', total: formatCostRangeUsd(range.low, range.high), parts };
  }

  /**
   * The estimate's total in dollars, from the first calls alone to one repair turn per document, or
   * null without a first-call price; `high` is null when any document has no repair-inclusive figure.
   */
  private get estimateTotalUsd(): ReportCostRange | null {
    const items = this.checkedEstimates;
    if (items.length === 0 || items.some(item => item.estimate.estimatedCostUsd === null || item.estimate.estimatedCostUsd === undefined)) {
      return null;
    }
    const low = items.reduce((sum, item) => sum + (item.estimate.estimatedCostUsd ?? 0), 0);
    const high = items.some(item => item.estimate.estimatedCostWithRepairUsd === null || item.estimate.estimatedCostWithRepairUsd === undefined)
      ? null
      : items.reduce((sum, item) => sum + (item.estimate.estimatedCostWithRepairUsd ?? 0), 0);
    return { low, high };
  }

  /** What the estimate is for, under the total. */
  get estimateSubjectLine(): string {
    const preview = this.answeredPreview;
    if (!preview) {
      return '';
    }
    const range = this.estimateTotalUsd;
    const repair = range !== null && isCostRange(range.low, range.high)
      ? 'From the first call alone to one repair turn per document.'
      : 'A repair turn can roughly double a document’s cost.';
    if (this.scopeMode === 'comparison') {
      return `For ${this.coveredKeys.length} models, compared as equals. ${repair}`;
    }
    const subjects = new Set(this.checkedRows.map(row => row.entryKey));
    if (subjects.size <= 1) {
      return `For ${preview.subjectLabel} against ${plural(preview.peers.length, 'peer', 'peers')}. ${repair}`;
    }
    return `For ${subjects.size} models, each against the others. ${repair}`;
  }

  /** The amber warning when a checked document's prompt fills 70% or more of the writer's context window. */
  get contextWindowWarning(): string | null {
    const items = this.checkedEstimates
      .filter(item => (item.estimate.contextWindowShare ?? 0) >= REPORT_PACK_CONTEXT_WARNING_SHARE);
    if (items.length === 0 || this.refusal) {
      return null;
    }
    const fullest = items.reduce((a, b) => ((b.estimate.contextWindowShare ?? 0) > (a.estimate.contextWindowShare ?? 0) ? b : a));
    const percent = Math.round((fullest.estimate.contextWindowShare ?? 0) * 100);
    return `The ${fullest.row.name}'s prompt would fill about ${percent}% of the writer's context window; from `
      + `${Math.round(REPORT_PACK_CONTEXT_REFUSAL_SHARE * 100)}% it is refused. Fewer models, or a writer with a larger `
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
      return REPORT_PACK_JOB_RUNNING_REASON;
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
        ? REPORT_PACK_ESTIMATING_REASON
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

  /**
   * The blocked reason is visually hidden, and still Generate's description, while its fact is already
   * in view: the main area shows the job and Cancel Job, or the estimate panel shows *Estimating…*.
   */
  protected get generateBlockedReasonShownElsewhere(): boolean {
    const reason = this.generateBlockedReason;
    return reason === REPORT_PACK_JOB_RUNNING_REASON || reason === REPORT_PACK_ESTIMATING_REASON;
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
    // Focus is on the progress area before Generate disables; a stacked layout scrolls it into view.
    this.focusProgressArea();
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
        this.focusGenerateIfEnabled();
      }
    });
    this.cdr.markForCheck();
  }

  /** The frame's main column: a focusable region labeled "Report pack progress". */
  private focusProgressArea(): void {
    document.getElementById(`${this.idPrefix}-progress`)?.closest<HTMLElement>('.rrf-main')?.focus();
  }

  /**
   * After a refused start: back to Generate when it can be used again; otherwise focus stays on the
   * progress area, and the error's `role="alert"` announces it.
   */
  private focusGenerateIfEnabled(): void {
    // Renders now, so the button's disabled state is the one the refusal leaves.
    this.cdr.detectChanges();
    const button = this.generateButton?.nativeElement;
    if (button && !button.disabled) {
      button.focus();
    }
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

  /**
   * From Generate until the job's first reading: the start request, then the wait for the first
   * progress. The main area shows the starting panel in place of the job card meanwhile.
   */
  get isStarting(): boolean {
    return this.starting || (this.activeJobId !== null && this.job === null && !this.pollError);
  }

  /**
   * The keep-open note, in the job card while charts can still be pending: the job runs, or a written
   * document's charts are being drawn. Never without chart storage, which draws none.
   */
  get showKeepOpenNote(): boolean {
    return !this.chartStorageMissing && (this.jobInProgress || this.jobDocuments.some(doc => this.chartCell(doc).pending));
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

  /**
   * The live line: it changes as the job, or the document being written, moves from phase to phase,
   * never with the clock.
   */
  get jobStatusLine(): string {
    const job = this.job;
    if (!job) {
      return this.starting || this.activeJobId ? 'Starting the report pack…' : '';
    }
    const total = job.documents.length;
    const finished = job.documents.filter(d => FINISHED_DOCUMENT_STATUSES.has(d.status)).length;
    if (job.status === 'Running') {
      const line = `Writing ${total} ${total === 1 ? 'document' : 'documents'} for ${this.jobSubjectText}: ${finished} of ${total} finished.`;
      const current = job.documents.find(d => !FINISHED_DOCUMENT_STATUSES.has(d.status) && !!d.phase && d.phase !== 'Done');
      const phase = current ? documentPhaseLine(current) : null;
      if (!current || !phase) {
        return line;
      }
      const name = this.jobSubjectCount > 1
        ? `${audienceLabel(current.audience)} of ${current.subjectLabel ?? job.subjectLabel}`
        : audienceLabel(current.audience);
      return `${line} ${name}: ${phase}.`;
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

  /** A document's card key: `<subject key>|<audience>`. */
  private documentKey(doc: BenchmarkReportPackDocumentProgressDto): string {
    return `${doc.subjectKey ?? this.job?.subjectKey ?? ''}|${doc.audience}`;
  }

  /**
   * One card per document: its chip (with the explanation of a warning or a failure), its model in a
   * per-model job, its phase line, its duration on the server's clock, its model calls, tokens and cost,
   * and its charts.
   */
  get documentRows(): readonly ReportJobDocumentCard[] {
    this.documentCards = reuseReportJobDocumentCards(this.documentCards, this.buildDocumentCards());
    return this.documentCards;
  }

  private buildDocumentCards(): ReportJobDocumentCard[] {
    const job = this.job;
    if (!job) {
      return [];
    }
    const finished = FINISHED_JOB_STATUSES.has(job.status);
    const perModel = this.jobHasModelColumn;
    return job.documents.map((doc): ReportJobDocumentCard => {
      const duration = doc.startedAtUtc
        ? this.spanToNow(doc.startedAtUtc, doc.completedAtUtc ?? (finished ? job.completedAtUtc : null))
        : null;
      const key = this.documentKey(doc);
      const name = audienceLabel(doc.audience);
      const subjectLabel = doc.subjectLabel ?? job.subjectLabel;
      const phrase = perModel ? `the ${name} of ${subjectLabel}` : `the ${name}`;
      const charts = this.chartCell(doc);
      return {
        key,
        audience: doc.audience,
        subject: doc.subjectKey ?? null,
        title: name,
        meta: perModel ? [subjectLabel] : [],
        phrase,
        statusWord: documentProgressLabel(doc.status),
        chipClass: documentChipClass(doc.status),
        tone: reportJobDocumentTone(doc.status),
        phaseLine: documentRowPhaseLine(doc),
        errorMessage: doc.errorMessage || null,
        duration: duration === null ? '—' : formatElapsed(duration),
        modelCalls: numberFormat.format(doc.modelCalls),
        tokens: reportJobDocumentTokens(doc.inputTokens, doc.outputTokens),
        cost: reportJobDocumentCost(doc.costUsd),
        charts: charts.shown ? { text: charts.text, retry: charts.retry } : null,
        viewable: false,
        documentId: doc.documentId,
        doc,
        tipStem: `${this.idPrefix}-doc-${key.replace(/[^A-Za-z0-9_-]/g, '-')}`,
        why: EXPLAINED_DOCUMENT_STATUSES.has(doc.status)
          ? { subject: documentStatusSubject(phrase, doc.status), title: documentStatusQuestion(phrase, doc.status) }
          : null,
        draftIssueCount: doc.firstTurnIssues?.length ?? 0,
        draftTipBesideChip: ACTIVE_DOCUMENT_STATUSES.has(doc.status),
        phaseDetail: doc.phaseDetail ?? null
      };
    });
  }

  private chartCell(doc: BenchmarkReportPackDocumentProgressDto): ReportPackChartCell {
    if (this.chartStorageMissing || (this.chartSelection[doc.audience] ?? []).length === 0) {
      return { text: 'Charts: none', retry: false, pending: false, shown: false };
    }
    const status = doc.documentId === null ? undefined : this.chartStatus[doc.documentId];
    if (!status) {
      return { text: '—', retry: false, pending: false, shown: false };
    }
    switch (status.state) {
      case 'attaching':
        return { text: 'Charts: attaching…', retry: false, pending: true, shown: true };
      case 'done':
        return { text: `Charts: ${numberFormat.format(status.count)}`, retry: false, pending: false, shown: true };
      case 'failed':
        return { text: 'Charts failed — retry', retry: true, pending: false, shown: true };
      default:
        return { text: 'Charts: none', retry: false, pending: false, shown: false };
    }
  }

  /** *Charts failed — retry* on a document's card: the host draws its charts again. */
  retryCharts(card: ReportJobDocumentCard): void {
    const doc = this.job?.documents.find(candidate => this.documentKey(candidate) === card.key);
    if (doc) {
      this.chartRetryRequested.emit(doc);
    }
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
    return this.jobEstimateUsd === null ? null : `about ${formatCostRangeUsd(this.jobEstimateUsd.low, this.jobEstimateUsd.high)}`;
  }

  /** The finished job in one line: `Written: 2 documents (1 with warnings), 1 min 12 s, $0.21`. */
  get jobSummary(): string {
    const job = this.job;
    if (!job) {
      return '';
    }
    const total = job.documents.length;
    const written = job.documents.filter(d => WRITTEN_DOCUMENT_STATUSES.has(d.status)).length;
    const warned = job.documents.filter(d => d.status === 'CompletedWithWarnings').length;
    const warnings = warned > 0 ? ` (${warned} with warnings)` : '';
    const tail = [this.jobElapsed, formatCostUsd(job.costUsd)].filter(part => part !== '').join(', ');
    let head: string;
    switch (job.status) {
      case 'Completed':
        head = `Written: ${plural(written, 'document', 'documents')}${warnings}`;
        break;
      case 'CompletedWithErrors':
        head = `Written with errors: ${written} of ${plural(total, 'document', 'documents')}${warnings}`;
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
    this.focusProgressArea();
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
        this.scheduleDocuments();
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

  /** Loads the writers; a preview asked for meanwhile is sent when they arrive, or when they fail. */
  private loadWriters(): void {
    this.writersLoading = true;
    this.writersError = null;
    if (this.previewPending && this.latestTicket !== null) {
      this.schedulePreview();
    }
    this.writersSub?.unsubscribe();
    this.writersSub = this.adminService.getSystemConfigs().subscribe({
      next: configs => {
        this.writersLoading = false;
        this.writersLoaded = true;
        this.writers = (configs ?? []).filter(c => (c.modelRole & 4) === 4 && c.hasApiKey && c.isEnabled);
        this.applyRememberedWriter();
        if (this.previewAwaitingWriters) {
          this.schedulePreview();
        }
        this.cdr.markForCheck();
      },
      error: () => {
        this.writersLoading = false;
        this.writersError = 'The report writers could not be loaded.';
        if (this.previewAwaitingWriters) {
          this.schedulePreview();
        }
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

  /** The finished job has a writer call with a reply: *Download the writer's replies* is offered. */
  get showWriterReplies(): boolean {
    return this.isJobFinished
      && (this.job?.documents ?? []).some(doc => (doc.calls ?? []).some(call => (call.replyChars ?? 0) > 0));
  }

  /**
   * Fetches the writer's raw replies of the job as Markdown and saves them; the status line says what
   * happens, and why when the server no longer holds the job.
   */
  downloadWriterReplies(): void {
    const job = this.job;
    if (!job || this.repliesLoading) {
      return;
    }
    const generation = this.generation;
    this.repliesLoading = true;
    this.copyError = null;
    this.copyStatus = REPORT_PACK_REPLIES_PREPARING_TEXT;
    this.cdr.markForCheck();
    this.subscriptions['replies']?.unsubscribe();
    this.subscriptions['replies'] = this.benchmarkService.getReportPackWriterReplies(job.id).subscribe({
      next: markdown => {
        if (generation !== this.generation) {
          return;
        }
        this.repliesLoading = false;
        this.copyStatus = '';
        reportPackIo.downloadMarkdown(reportPackWriterRepliesFileName(job.subjectLabel, new Date()), markdown);
        this.cdr.markForCheck();
      },
      error: (error: unknown) => {
        if (generation !== this.generation) {
          return;
        }
        this.repliesLoading = false;
        const status = error instanceof HttpErrorResponse ? error.status : 0;
        this.copyStatus = status === 404
          ? REPORT_PACK_REPLIES_GONE_TEXT
          : `${REPORT_PACK_REPLIES_FAILED_TEXT} (${status > 0 ? `HTTP ${status}` : 'no answer from the server'}).`;
        this.cdr.markForCheck();
      }
    });
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
    this.latestDocumentsTicket = null;
    this.documentsQueue.next(null);
    this.clearPollTimer();
    this.listSub?.unsubscribe();
    this.listSub = null;
    this.listRequestComparisonId = null;
    for (const key of Object.keys(this.subscriptions)) {
      this.subscriptions[key]?.unsubscribe();
      this.subscriptions[key] = undefined;
    }
  }
}
