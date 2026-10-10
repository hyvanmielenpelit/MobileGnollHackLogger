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
import { Observable, Subject, Subscription, catchError, debounceTime, interval, map, of, switchMap, timer } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkReportAudience,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPeerNaming,
  BenchmarkRunReportDocumentsStatus,
  BenchmarkRunReportJobDto,
  reportDisclosureParam,
  reportPeerNamingParam
} from '../../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../../services/admin.service';
import { AdminChatConsistencyService, ccErrorText } from '../../../../services/admin-chat-consistency.service';
import { InfoTipComponent } from '../../../../shared/info-tip/info-tip.component';
import { ModelPickerComponent, ModelPickerOption } from '../../../../shared/model-picker/model-picker.component';
import { PdfViewerDialogComponent } from '../../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { copyToClipboard } from '../../../../utils/clipboard.util';
import { parseServerUtcDate } from '../../../../utils/date.util';
import { downloadTextFile } from '../../../../utils/download.util';
import { ensureOverlayPolyfills } from '../../../../utils/polyfills.util';
import { rememberedPdfPaper, reportDocumentFileStem } from '../../download-center/download-center-panel.component';
import { reportDisclosureInfo } from '../../report-disclosure-guide';
import { ReportChartPickerComponent } from '../../report-pack/report-chart-picker.component';
import { ReportChartLayoutSettings, ReportChartSelection } from '../../report-pack/report-charts';
import { disclosureLabel, documentStatusLabel, formatCostUsd } from '../../report-pack/report-document-format';
import { RunReportFrameComponent } from '../../run-report-frame/run-report-frame.component';
import {
  isReportWriterSameProviderWarning,
  reportDocumentByline,
  reportDocumentDisclosures,
  reportDocumentMeta,
  reportDocumentsStatusOf,
  reportJobInProgress,
  reportJobStatusText
} from '../../run-ai-reports/report-documents-list';
import { CHAT_CONSISTENCY_REPORT_WRITER_ADVICE, REPORT_WRITER_ADVICE_LEAD } from '../../run-ai-reports/report-writer-advice';
import { reportWriterRefusal, reportWriterWarning, reportWriterWarningText } from '../../run-ai-reports/report-writer-policy';
import type { ClientPollError } from '../../run-ai-reports/run-report-writing-diagnostics';
import { formatUtcSeconds } from '../../run-ai-reports/run-report-writing-diagnostics';
import { CcFigureInput, analysisBands, analysisChartPoints } from '../chat-consistency-charts';
import { CcEventGroup } from '../chat-consistency-events';
import { formatUsd } from '../chat-consistency-format';
import {
  CC_REPORT_CHART_AUDIENCE_KEYS,
  CC_REPORT_CHART_COLUMN_DISABLED_REASON,
  CC_REPORT_CHART_LAYOUT_FIELDS,
  CC_REPORT_CHART_PICKER_AUDIENCES,
  CC_REPORT_CHART_PICKER_FIGURES,
  CcReportChartSettings,
  ccReportChartNotes,
  normalizeCcReportChartLayout,
  normalizeCcReportChartSelection,
  readStoredCcReportChartSettings,
  storeCcReportChartSettings
} from '../chat-consistency-report-chart-settings';
import { CcChartPublishResult, CcReportChartDocument, publishCcReportCharts } from '../chat-consistency-report-charts';
import { ccModelBaseName, ccOutOfDateReasons } from '../chat-consistency-results';
import {
  CC_REPORT_AUDIENCES,
  CcAnalysisFreshness,
  CcAnalysisResult,
  CcBatteryTimelinePoint,
  CcReportEstimate,
  CcTimelinePoint
} from '../chat-consistency.models';
import { CcBadgedModel, CcModelBadgesComponent } from '../model-badges/model-badges.component';
import {
  CcReportJobRow,
  CcReportJobStage,
  CcReportJobStats,
  ccReportAudienceLabel,
  ccReportDiagnosticsFileName,
  ccReportJobDiagnostics,
  ccReportJobRows,
  ccReportJobStages,
  ccReportJobStats,
  ccReportJobSummary
} from './cc-report-job-view';
import { CcFreshnessNoticeComponent } from './freshness-notice/freshness-notice.component';

/** The report job's poll interval, and the longest pause the back-off grows to. */
export const CC_REPORT_POLL_MS = 3000;
export const CC_REPORT_POLL_MAX_MS = 30_000;

/** The pause after the last change of writer or documents before the cost is estimated. */
export const CC_REPORT_ESTIMATE_DEBOUNCE_MS = 300;

/** What the Provider Issue Report checkbox says before an estimate has answered for it. */
export const CC_PROVIDER_REPORT_UNKNOWN =
  'Choose a report writer: its estimate says whether a Provider Issue Report can be written for this analysis.';

/** The per-viewer memory of the step's sidebar width. */
export const CC_REPORTS_STORAGE_KEY = 'overseer.benchmark.chatConsistency.reports';

/** The line under Documents when nothing is left to write. */
export const CC_ALL_WRITTEN_TEXT = 'Every document of this analysis is written.';

/** Why Write Reports is unavailable when every document is written; the button stays focusable. */
export const CC_ALL_WRITTEN_REASON = 'Every document of this analysis is written. Delete one to write it again.';

/** The notice above the documents of an analysis without a decisive verdict. */
export const CC_NO_DECISIVE_VERDICT_TEXT =
  'Every endpoint is inconclusive or not computable; the documents will say Not enough evidence yet.';

/** The documents' live line after *Write the Executive Summary only*. */
export const CC_SUMMARY_ONLY_LIVE_TEXT = 'Only the Executive Summary is checked.';

/** Why Delete is unavailable while a job runs or charts are attached. */
export const CC_DELETE_BUSY_REASON = 'Wait for the reports and their charts to finish.';

/** Why Update charts is unavailable: reports are being written, charts are being attached, or nothing is written. */
export const CC_UPDATE_CHARTS_WRITING_REASON = 'Reports are being written. Update the charts once they finish.';
export const CC_UPDATE_CHARTS_ATTACHING_REASON = 'Charts are being drawn and attached.';
export const CC_UPDATE_CHARTS_NONE_REASON = 'No document of this analysis is written yet.';

/** The clipboard and file side effects of the step, held in an object so a spec can observe them. */
export const ccReportIo = {
  copy: (text: string): Promise<boolean> => copyToClipboard(text),
  download: (fileName: string, text: string): void => downloadTextFile(fileName, text, 'text/plain;charset=utf-8')
};

type ChartState = 'idle' | 'attaching' | 'done' | 'failed';

/** What drew the charts last: a finished job, or Update charts. */
type ChartOrigin = 'job' | 'update';

interface EstimateRequest {
  key: string;
  analysisId: number;
  writerId: number;
  audiences: BenchmarkReportAudience[];
}

/** The written document the delete confirmation asks about. */
interface DeleteTarget {
  readonly audience: BenchmarkReportAudience;
  readonly label: string;
  readonly doc: BenchmarkReportDocumentListItemDto;
}

function readStoredSidebarWidth(): number | null {
  try {
    const raw = localStorage.getItem(CC_REPORTS_STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as { version?: unknown; sidebarWidth?: unknown } | null;
    if (parsed?.version !== 1) return null;
    return typeof parsed.sidebarWidth === 'number' && Number.isFinite(parsed.sidebarWidth) ? parsed.sidebarWidth : null;
  } catch {
    return null;
  }
}

function writeStoredSidebarWidth(width: number): void {
  try {
    localStorage.setItem(CC_REPORTS_STORAGE_KEY, JSON.stringify({ version: 1, sidebarWidth: width }));
  } catch {
    // Storage unavailable: the width is simply not remembered.
  }
}

/** The 409 body of a write refused because the analysis is out of date: `{ error, outOfDate: true }`. */
function isOutOfDateRefusal(body: unknown): boolean {
  return !!body && typeof body === 'object' && (body as { outOfDate?: unknown }).outOfDate === true;
}

/** A stored document as the chart publisher takes it. */
function chartDocumentOf(doc: BenchmarkReportDocumentListItemDto): CcReportChartDocument {
  return { id: doc.id, audience: doc.audience, chartCount: doc.chartCount ?? 0 };
}

/**
 * Step 5 of the Chat Consistency wizard, *Reports*, in Model Comparison step 3's layout: a resizable
 * sidebar with the analysis's four documents (a written one with View and Delete, an unwritten one
 * with its Write checkbox) and Update charts, the charts each document type carries
 * (`app-report-chart-picker`, remembered per browser), the report writer, its estimate and Write
 * Reports; and a main area that follows the job (the stage rail, the stat strip, one row per document,
 * the log and diagnostics, Cancel), then summarizes it. The header carries the out-of-date notice of
 * the analysis. The same-provider and out-of-date confirmations, the delete confirmation and the PDF
 * viewer are nested in the wizard's dialog and stop their own close and cancel events.
 *
 * When a job finishes, the documents without charts get the charts chosen for their type; Update
 * charts draws them again for every written document. Both set `chartState`, which the wizard's close
 * guard reads. Polling and the one-second clock go on while the step is hidden and stop in
 * `ngOnDestroy`; a new analysis resets the step and finds its running job again.
 */
@Component({
  selector: 'app-cc-reports-step',
  standalone: true,
  imports: [
    RunReportFrameComponent, ModelPickerComponent, InfoTipComponent, PdfViewerDialogComponent, CcModelBadgesComponent,
    ReportChartPickerComponent, CcFreshnessNoticeComponent
  ],
  templateUrl: './reports-step.component.html',
  styleUrls: ['./reports-step.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcReportsStepComponent implements OnInit, OnChanges, OnDestroy {
  private readonly service = inject(AdminChatConsistencyService);
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  @Input({ required: true }) result!: CcAnalysisResult;
  /** The report writer options, as the launcher lists them. */
  @Input() writerOptions: readonly ModelPickerOption<SystemAiConfigDto>[] = [];
  @Input() writerConfigs: readonly SystemAiConfigDto[] = [];
  @Input() pickerEmptyHint: string | null = null;
  /** The subject's timeline points, for the attached charts. */
  @Input() points: readonly CcTimelinePoint[] = [];
  /** The timeline's battery points, which a battery analysis's attached charts draw. */
  @Input() batteryPoints: readonly CcBatteryTimelinePoint[] = [];
  /** The timeline's composite events, whose E numbers the attached charts reuse. */
  @Input() eventNumbering: readonly CcEventGroup[] = [];

  /** *See the documents* after a job: the host shows step 6. */
  @Output() readonly documentsRequested = new EventEmitter<void>();
  /** The analysis's documents changed: a job finished, charts were attached, or a document was deleted. */
  @Output() readonly documentsChanged = new EventEmitter<void>();
  /** Whether a job runs or charts are attached changed, for the wizard's close guard and footer. */
  @Output() readonly stateChange = new EventEmitter<void>();
  /** *Analyze again* on the out-of-date notice: the host shows Analyze with this analysis's settings. */
  @Output() readonly analyzeAgain = new EventEmitter<void>();

  @ViewChild('sameProviderDialog') sameProviderDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('outOfDateDialog') outOfDateDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('deleteDialog') deleteDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('writeButton') writeButton?: ElementRef<HTMLButtonElement>;
  @ViewChild(PdfViewerDialogComponent) pdfViewer?: PdfViewerDialogComponent;

  readonly audiences = CC_REPORT_AUDIENCES;
  readonly providerAudience = BenchmarkReportAudience.ProviderIssueReport;
  readonly writerAdvice = CHAT_CONSISTENCY_REPORT_WRITER_ADVICE;
  readonly writerAdviceLead = REPORT_WRITER_ADVICE_LEAD;
  readonly allWrittenText = CC_ALL_WRITTEN_TEXT;
  readonly noDecisiveVerdictText = CC_NO_DECISIVE_VERDICT_TEXT;
  readonly deleteBusyReason = CC_DELETE_BUSY_REASON;

  writerId: number | null = null;
  estimate: CcReportEstimate | null = null;
  estimating = false;
  estimateFailed = false;
  writeSubmitting = false;
  writeError: string | null = null;
  confirmText = '';

  /** The shown analysis's freshness; null until its notice has read it. */
  freshness: CcAnalysisFreshness | null = null;
  /** The reason sentences the out-of-date confirmation shows. */
  outOfDateReasons: string[] = [];
  /** What the operator confirmed for the write in progress; reset on every Write Reports. */
  private acknowledgedSameProvider = false;
  private acknowledgedOutOfDate = false;

  status: BenchmarkRunReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.NotRequested;
  statusMessage: string | null = null;
  job: BenchmarkRunReportJobDto | null = null;
  /** When `job` was last read (client clock), to run the server's clock on between readings. */
  private jobReadAt = 0;
  /** The estimate when the job was started here, for its stat strip. */
  jobEstimateUsd: number | null = null;
  /** The finished job's card is hidden until the next write. */
  dismissed = false;
  canceling = false;
  cancelError: string | null = null;

  chartState: ChartState = 'idle';
  chartMessage = '';
  chartOrigin: ChartOrigin = 'job';

  // --- Charts in PDF and Word ---
  readonly chartFigures = CC_REPORT_CHART_PICKER_FIGURES;
  readonly chartAudienceOptions = CC_REPORT_CHART_PICKER_AUDIENCES;
  readonly chartLayoutFields = CC_REPORT_CHART_LAYOUT_FIELDS;
  readonly chartColumnReason = CC_REPORT_CHART_COLUMN_DISABLED_REASON;
  /** The figures and layout per document type, as last stored in this browser, else the analysis's defaults. */
  chartSettings: CcReportChartSettings = readStoredCcReportChartSettings();
  private chartEnabledKey = '';
  private chartEnabledCache: readonly BenchmarkReportAudience[] = [];
  private chartNotesResult: CcAnalysisResult | null = null;
  private chartNotesCache: Readonly<Record<string, string>> = {};

  /** The analysis's stored documents, as the list last answered. */
  documents: BenchmarkReportDocumentListItemDto[] = [];
  /** The newest stored document of each type. */
  private written = new Map<BenchmarkReportAudience, BenchmarkReportDocumentListItemDto>();

  /** The sidebar's width in CSS px, as last stored; null for the frame's default. */
  sidebarWidth: number | null = readStoredSidebarWidth();

  // --- Delete ---
  deleteTarget: DeleteTarget | null = null;
  deleting = false;
  deleteError: string | null = null;
  /** The last delete or *Write the Executive Summary only*, for the documents' live line. */
  documentsLiveText = '';

  // --- Client polling and diagnostics ---
  pollCount = 0;
  lastSuccessUtc: Date | null = null;
  lastPollError: ClientPollError | null = null;
  copyStatus = '';
  copyError: string | null = null;

  private readonly checked = new Set<BenchmarkReportAudience>();
  private readonly estimateRequests = new Subject<EstimateRequest | null>();
  private readonly estimateSub: Subscription;
  private requestedKey: string | null = null;
  private writeSub: Subscription | null = null;
  private pollSub: Subscription | null = null;
  private cancelSub: Subscription | null = null;
  private documentsSub: Subscription | null = null;
  private deleteSub: Subscription | null = null;
  private tickSub: Subscription | null = null;
  private pollDelay = CC_REPORT_POLL_MS;
  private pollFailures = 0;
  private chartsGeneration = 0;
  private lastStateKey = '';
  private returnFocus: HTMLElement | null = null;
  private focusAfterDelete = false;
  private writerBadgesKey = '';
  private writerBadgesCache: CcBadgedModel | null = null;

  constructor() {
    this.estimateSub = this.estimateRequests.pipe(
      debounceTime(CC_REPORT_ESTIMATE_DEBOUNCE_MS),
      switchMap((request): Observable<{ request: EstimateRequest; estimate: CcReportEstimate | null } | null> => request === null
        ? of(null)
        : this.service.estimateReports(request.analysisId, {
          writerModelConfigurationId: request.writerId,
          audiences: request.audiences
        }).pipe(
          map(estimate => ({ request, estimate })),
          catchError(() => of({ request, estimate: null }))
        ))
    ).subscribe(outcome => {
      if (!outcome || outcome.request.key !== this.requestedKey) return;
      this.estimating = false;
      this.estimate = outcome.estimate;
      this.estimateFailed = outcome.estimate === null;
      // A Provider Issue Report the estimate says cannot be written is unchecked.
      if (this.estimate && !this.estimate.providerIssueReportAvailable && this.checked.delete(BenchmarkReportAudience.ProviderIssueReport)) {
        this.queueEstimate();
      }
      this.cdr.markForCheck();
    });
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    const change = changes['result'];
    if (change && (change.firstChange || change.previousValue?.analysisId !== this.result.analysisId)) {
      // Without stored choices, the defaults leave out what this analysis could not compute.
      this.chartSettings = readStoredCcReportChartSettings(this.result.endpoints);
      this.reset();
      this.resumeJob();
      this.loadDocuments();
      // Reached inside the host's change detection: the event waits for it to end.
      this.syncState(true);
    }
  }

  ngOnDestroy(): void {
    this.reset();
    this.estimateSub.unsubscribe();
  }

  get analysisId(): number | null {
    return this.result?.analysisId ?? null;
  }

  /** `Analysis #7 · Chat consistency: GPT-5`. */
  get subtitle(): string {
    const subject = this.result?.subject;
    const model = subject ? ccModelBaseName(subject.displayName, subject.thinkingLevel) : '';
    const analysis = this.analysisId === null ? 'Unsaved analysis' : `Analysis #${this.analysisId}`;
    return model ? `${analysis} · Chat consistency: ${model}` : analysis;
  }

  onSidebarWidthChange(width: number): void {
    this.sidebarWidth = width;
    writeStoredSidebarWidth(width);
    this.cdr.markForCheck();
  }

  // --- Documents ---

  /** The newest stored document of this type, or null while it is not written. */
  writtenDoc(audience: BenchmarkReportAudience): BenchmarkReportDocumentListItemDto | null {
    return this.written.get(audience) ?? null;
  }

  /** `the Executive Summary`, for the names of a row's controls. */
  rowName(audience: BenchmarkReportAudience): string {
    return `the ${ccReportAudienceLabel(audience)}`;
  }

  /** The writer and date, then the stored duration and cost. */
  docMeta(doc: BenchmarkReportDocumentListItemDto): string {
    return reportDocumentMeta(doc, null);
  }

  /** `by Claude writer on 2026-10-02 10:01 UTC`. */
  docByline(doc: BenchmarkReportDocumentListItemDto): string {
    return reportDocumentByline(doc, null);
  }

  /** `Written`, `Written with warnings`. */
  docStatusText(doc: BenchmarkReportDocumentListItemDto): string {
    return documentStatusLabel(doc.status || 'Completed');
  }

  /**
   * Nothing is left to write: the three documents every analysis has are written, and the Provider
   * Issue Report is written or, by the estimate, cannot be.
   */
  get allWritten(): boolean {
    const standard = CC_REPORT_AUDIENCES
      .map(entry => entry.audience)
      .filter(audience => audience !== BenchmarkReportAudience.ProviderIssueReport);
    if (!standard.every(audience => this.written.has(audience))) return false;
    if (this.written.has(BenchmarkReportAudience.ProviderIssueReport)) return true;
    return this.estimate !== null && !this.estimate.providerIssueReportAvailable;
  }

  /** A written document is never deleted while a job writes or its charts are attached. */
  get deleteBusy(): boolean {
    return this.chartState === 'attaching' || this.jobInProgress;
  }

  /**
   * Every primary endpoint of the analysis is inconclusive or not computable, so every document will
   * say *Not enough evidence yet*; false for an analysis without endpoints.
   */
  get noDecisiveVerdict(): boolean {
    const endpoints = this.result?.endpoints ?? [];
    return endpoints.length > 0
      && endpoints.every(endpoint => !endpoint.computed || endpoint.verdict === null || endpoint.verdict === 'inconclusive');
  }

  /** *Write the Executive Summary only* is offered while the summary is unwritten and something is left to write. */
  get summaryOnlyAvailable(): boolean {
    return !this.allWritten && !this.written.has(BenchmarkReportAudience.ExecutiveSummary);
  }

  /** Checks the Executive Summary alone; the estimate follows. */
  writeSummaryOnly(): void {
    if (!this.summaryOnlyAvailable) return;
    this.checked.clear();
    this.checked.add(BenchmarkReportAudience.ExecutiveSummary);
    this.documentsLiveText = CC_SUMMARY_ONLY_LIVE_TEXT;
    this.queueEstimate();
    this.cdr.markForCheck();
  }

  // --- Audiences ---

  isChecked(audience: BenchmarkReportAudience): boolean {
    return this.checked.has(audience);
  }

  /** The Provider Issue Report can be checked only once an estimate says it is available. */
  get providerAvailable(): boolean {
    return this.estimate?.providerIssueReportAvailable === true;
  }

  /** Why the Provider Issue Report cannot be written, shown as text under its checkbox; '' when it can. */
  get providerReason(): string {
    if (this.providerAvailable) return '';
    if (!this.estimate) return CC_PROVIDER_REPORT_UNKNOWN;
    return this.estimate.providerIssueReportReason?.trim() || 'Not available for this analysis.';
  }

  isDisabled(audience: BenchmarkReportAudience): boolean {
    return audience === BenchmarkReportAudience.ProviderIssueReport && !this.providerAvailable;
  }

  toggleAudience(audience: BenchmarkReportAudience, event: Event): void {
    if (this.isDisabled(audience) || this.written.has(audience)) return;
    if ((event.target as HTMLInputElement).checked) this.checked.add(audience); else this.checked.delete(audience);
    this.queueEstimate();
    this.cdr.markForCheck();
  }

  get checkedAudiences(): BenchmarkReportAudience[] {
    return CC_REPORT_AUDIENCES.map(entry => entry.audience).filter(audience => this.checked.has(audience));
  }

  // --- Charts in PDF and Word ---

  /**
   * The document types whose charts can be chosen: those checked to be written, and those written,
   * whose charts Update charts draws. Memoized, so the picker sees a new list only when it changes.
   */
  get chartEnabledAudiences(): readonly BenchmarkReportAudience[] {
    const enabled = CC_REPORT_CHART_AUDIENCE_KEYS.filter(audience => this.checked.has(audience) || this.written.has(audience));
    const key = enabled.join(',');
    if (key !== this.chartEnabledKey) {
      this.chartEnabledKey = key;
      this.chartEnabledCache = enabled;
    }
    return this.chartEnabledCache;
  }

  /** The picker's note on each figure whose endpoint the analysis could not compute; memoized per analysis result. */
  get chartNotes(): Readonly<Record<string, string>> {
    if (this.result !== this.chartNotesResult) {
      this.chartNotesResult = this.result ?? null;
      this.chartNotesCache = ccReportChartNotes(this.result?.endpoints);
    }
    return this.chartNotesCache;
  }

  onChartSelectionChange(selection: ReportChartSelection<string>): void {
    this.chartSettings = { ...this.chartSettings, selection: normalizeCcReportChartSelection(selection) };
    storeCcReportChartSettings(this.chartSettings);
    this.cdr.markForCheck();
  }

  onChartLayoutChange(layout: ReportChartLayoutSettings<string>): void {
    this.chartSettings = { ...this.chartSettings, layout: normalizeCcReportChartLayout(layout) };
    storeCcReportChartSettings(this.chartSettings);
    this.cdr.markForCheck();
  }

  /** Why Update charts is unavailable, or null. */
  get updateChartsBlockedReason(): string | null {
    if (this.jobInProgress) return CC_UPDATE_CHARTS_WRITING_REASON;
    if (this.chartState === 'attaching') return CC_UPDATE_CHARTS_ATTACHING_REASON;
    if (this.documents.length === 0) return CC_UPDATE_CHARTS_NONE_REASON;
    return null;
  }

  /** Update charts' outcome, in the sidebar's live line; the job card shows a finished job's. */
  get updateChartsStatus(): string {
    return this.chartOrigin === 'update' ? this.chartMessage : '';
  }

  /** Update charts: every written document of the analysis gets the charts now chosen for its type. */
  updateCharts(): void {
    const analysisId = this.analysisId;
    if (analysisId === null || this.updateChartsBlockedReason !== null) return;
    void this.attachCharts(analysisId, this.documents.map(chartDocumentOf), 'update');
  }

  // --- Writer ---

  private get writer(): SystemAiConfigDto | undefined {
    return this.writerConfigs.find(config => config.id === this.writerId);
  }

  private get candidate(): { provider: string; modelId: string } {
    return { provider: this.result.subject.provider, modelId: this.result.subject.modelId };
  }

  selectWriter(config: SystemAiConfigDto | null): void {
    this.writerId = config?.id ?? null;
    this.writeError = null;
    // The previous writer's refusal and warning do not carry over to this one.
    this.estimate = null;
    this.queueEstimate();
    this.cdr.markForCheck();
  }

  /** Why writing is refused for this writer, or ''. */
  get writeRefusal(): string {
    if (this.analysisId === null) return 'The analysis is not saved.';
    const refusal = reportWriterRefusal(this.writer, this.candidate);
    if (refusal) return refusal;
    return this.writerId !== null ? (this.estimate?.refusal?.trim() || '') : '';
  }

  /** The same-provider warning, or ''. Writing stays possible after the confirmation. */
  get writerWarning(): string {
    if (this.writerId === null || this.writeRefusal) return '';
    const warning = reportWriterWarning(this.writer, this.candidate);
    if (warning) return warning;
    const server = this.estimate?.sameProviderWarning;
    return server ? reportWriterWarningText(server.assessorModelDisplayName, server.provider) : '';
  }

  /** The writer field's description: its refusal or its warning. */
  get writerDescribedBy(): string | null {
    return this.writeRefusal ? 'cc-rep-writer-refusal' : (this.writerWarning ? 'cc-rep-writer-warning' : null);
  }

  get jobInProgress(): boolean {
    return reportJobInProgress(this.status);
  }

  /** Why Write Reports is unavailable, or null when it can start. */
  get writeBlockedReason(): string | null {
    if (this.jobInProgress) return 'Reports are being written. Wait for them to finish, or cancel the writing.';
    if (this.allWritten) return CC_ALL_WRITTEN_REASON;
    if (this.analysisId === null) return 'The analysis is not saved.';
    if (this.checkedAudiences.length === 0) return 'Choose at least one document.';
    if (this.writerId === null) return 'Choose a report writer.';
    if (this.writeRefusal) return 'The documents cannot be written with this writer; the reason is shown under the report writer.';
    return null;
  }

  /**
   * Write Reports stays focusable, `aria-disabled`, when every document is written, so a keyboard
   * reader lands on it and hears why; every other block disables it.
   */
  get writeAriaDisabled(): boolean {
    return this.writeBlockedReason === CC_ALL_WRITTEN_REASON;
  }

  get writeDisabled(): boolean {
    return this.writeSubmitting || (this.writeBlockedReason !== null && !this.writeAriaDisabled);
  }

  /** The button's description: the estimate, and the reason it is blocked while it is. */
  get writeDescribedBy(): string {
    return this.writeBlockedReason !== null ? 'cc-rep-estimate cc-rep-write-blocked' : 'cc-rep-estimate';
  }

  get writeLabel(): string {
    if (this.writeSubmitting) return 'Requesting…';
    return this.checkedAudiences.length === 1 ? 'Write Report' : 'Write Reports';
  }

  get estimateText(): string {
    if (this.writerId === null || this.checkedAudiences.length === 0) return '';
    if (this.estimating) return 'Estimating…';
    if (this.estimateFailed) return 'The cost could not be estimated.';
    const total = this.estimate?.estimatedTotalCostUsd;
    if (total === null || total === undefined) return this.estimate ? 'No price card for this model; the cost cannot be estimated.' : '';
    return `Estimated cost: about ${formatUsd(total)}`;
  }

  // --- Writing ---

  /** The freshness of the shown analysis, as its notice read it. */
  onFreshnessChange(freshness: CcAnalysisFreshness): void {
    if (freshness.analysisId !== this.analysisId) return;
    this.freshness = freshness;
  }

  /**
   * Write Reports: asks about a same-provider writer, then about an out-of-date analysis, and sends
   * the request carrying each acknowledgment given. Every press asks again.
   */
  requestWrite(): void {
    if (this.writeDisabled || this.writeBlockedReason !== null) return;
    this.acknowledgedSameProvider = false;
    this.acknowledgedOutOfDate = false;
    this.continueWrite();
  }

  /** The next confirmation still to ask, or the request once none is left. */
  private continueWrite(): void {
    const warning = this.writerWarning;
    if (warning && !this.acknowledgedSameProvider) {
      this.openConfirm(warning);
      return;
    }
    if (this.freshness?.outOfDate && !this.acknowledgedOutOfDate) {
      this.openOutOfDate(ccOutOfDateReasons(this.freshness));
      return;
    }
    this.sendWrite(this.acknowledgedSameProvider, this.acknowledgedOutOfDate);
  }

  confirmSameProvider(): void {
    this.sameProviderDialog?.nativeElement.close();
    this.acknowledgedSameProvider = true;
    this.continueWrite();
  }

  cancelSameProvider(): void {
    this.sameProviderDialog?.nativeElement.close();
    this.writeButton?.nativeElement.focus();
  }

  /** *Write anyway* on the out-of-date confirmation. */
  confirmOutOfDate(): void {
    this.outOfDateDialog?.nativeElement.close();
    this.acknowledgedOutOfDate = true;
    this.continueWrite();
  }

  cancelOutOfDate(): void {
    this.outOfDateDialog?.nativeElement.close();
    this.writeButton?.nativeElement.focus();
  }

  private openConfirm(text: string): void {
    this.confirmText = text;
    this.cdr.detectChanges();
    const dialog = this.sameProviderDialog?.nativeElement;
    if (dialog && !dialog.open) dialog.showModal();
  }

  private openOutOfDate(reasons: string[]): void {
    this.outOfDateReasons = reasons;
    this.cdr.detectChanges();
    const dialog = this.outOfDateDialog?.nativeElement;
    if (dialog && !dialog.open) dialog.showModal();
  }

  private sendWrite(acknowledgeSameProvider: boolean, acknowledgeOutOfDate: boolean): void {
    const analysisId = this.analysisId;
    const writerId = this.writerId;
    if (analysisId === null || writerId === null || this.writeSubmitting) return;
    this.writeSubmitting = true;
    this.writeError = null;
    this.chartState = 'idle';
    this.chartMessage = '';
    const estimateUsd = this.estimate?.estimatedTotalCostUsd ?? null;
    this.cdr.markForCheck();
    this.writeSub?.unsubscribe();
    this.writeSub = this.service.writeReports(analysisId, {
      writerModelConfigurationId: writerId,
      audiences: this.checkedAudiences,
      ...(acknowledgeSameProvider ? { acknowledgeSameProvider: true } : {}),
      ...(acknowledgeOutOfDate ? { acknowledgeOutOfDate: true } : {})
    }).subscribe({
      next: response => {
        this.writeSubmitting = false;
        this.status = reportDocumentsStatusOf(response?.status ?? BenchmarkRunReportDocumentsStatus.Pending);
        this.statusMessage = null;
        this.job = null;
        this.jobEstimateUsd = estimateUsd;
        this.dismissed = false;
        this.cancelError = null;
        this.pollDelay = CC_REPORT_POLL_MS;
        this.pollFailures = 0;
        this.schedulePoll(analysisId);
        this.syncState();
        this.cdr.markForCheck();
      },
      error: err => {
        this.writeSubmitting = false;
        if (err?.status === 409 && !acknowledgeSameProvider && isReportWriterSameProviderWarning(err.error)) {
          this.cdr.markForCheck();
          this.openConfirm(reportWriterWarningText(err.error.assessorModelDisplayName, err.error.provider));
          return;
        }
        if (err?.status === 409 && !acknowledgeOutOfDate && isOutOfDateRefusal(err.error)) {
          // The freshness was not loaded yet, or changed since: the reasons it gives, else the server's text.
          const reasons = ccOutOfDateReasons(this.freshness);
          this.cdr.markForCheck();
          this.openOutOfDate(reasons.length > 0 ? reasons : [ccErrorText(err, 'The analysis is out of date.')]);
          return;
        }
        this.writeError = ccErrorText(err, 'The reports could not be requested.');
        this.cdr.markForCheck();
      }
    });
  }

  cancelJob(): void {
    const analysisId = this.analysisId;
    if (analysisId === null || !this.jobInProgress || this.canceling) return;
    this.canceling = true;
    this.cancelError = null;
    this.cancelSub?.unsubscribe();
    this.cancelSub = this.service.cancelReportJob(analysisId).subscribe({
      next: job => {
        this.canceling = false;
        if (job) this.applyJob(job);
        this.syncState();
        this.cdr.markForCheck();
      },
      error: err => {
        this.canceling = false;
        this.cancelError = ccErrorText(err, 'The writing could not be canceled.');
        this.cdr.markForCheck();
      }
    });
  }

  /** *See the documents*: the host shows step 6. */
  requestDocuments(): void {
    this.documentsRequested.emit();
  }

  /** Dismiss: the finished job's card goes away until the next write. */
  dismissJob(): void {
    if (this.jobInProgress) return;
    this.dismissed = true;
    this.cdr.markForCheck();
    this.host.nativeElement.querySelector<HTMLElement>('#cc-rep-new-heading')?.focus();
  }

  /** A nested dialog's close or cancel event, stopped so it never reaches the wizard's dialog. */
  stopNested(event: Event): void {
    event.stopPropagation();
  }

  // --- The job's view ---

  /** A job card is shown: one is running, or the last one has not been dismissed. */
  get showJob(): boolean {
    return this.jobInProgress || (this.job !== null && !this.dismissed);
  }

  get jobFinished(): boolean {
    return this.job !== null && !this.jobInProgress;
  }

  /** The live line: the job's state, how many jobs are ahead while it is queued, and its writer while it runs. */
  get jobStatusLine(): string {
    const parts = [reportJobStatusText(this.status, this.statusMessage)].filter(part => part !== '');
    const job = this.job;
    if (job && this.jobInProgress) {
      if (job.phase === 'Queued' && job.jobsAhead) parts.push(`${job.jobsAhead} ahead in the queue`);
      if (job.writerDisplayName) parts.push(job.writerDisplayName);
    }
    return parts.join(' · ');
  }

  /** The server's clock now: its last reading plus the client time since; the client's clock without one. */
  private serverNow(): Date {
    const serverTime = this.job?.serverTimeUtc;
    if (serverTime) {
      const server = parseServerUtcDate(serverTime).getTime();
      if (!Number.isNaN(server)) return new Date(server + Math.max(0, Date.now() - this.jobReadAt));
    }
    return new Date();
  }

  get jobStages(): CcReportJobStage[] {
    return this.job ? ccReportJobStages(this.job) : [];
  }

  get jobStats(): CcReportJobStats | null {
    return this.job ? ccReportJobStats(this.job, this.serverNow()) : null;
  }

  get jobRows(): CcReportJobRow[] {
    return this.job ? ccReportJobRows(this.job, this.serverNow()) : [];
  }

  get jobSummary(): string {
    return this.job ? ccReportJobSummary(this.job, this.serverNow()) : '';
  }

  get jobEstimate(): string | null {
    return this.jobEstimateUsd === null ? null : `about ${formatCostUsd(this.jobEstimateUsd)}`;
  }

  /** The writer's thinking and provider badges, memoized on the job's writer. */
  get writerBadges(): CcBadgedModel | null {
    const job = this.job;
    if (!job) return null;
    const key = `${job.writerProvider}|${job.writerThinkingLevel ?? ''}`;
    if (key !== this.writerBadgesKey || !this.writerBadgesCache) {
      this.writerBadgesKey = key;
      this.writerBadgesCache = { provider: job.writerProvider, thinkingLevel: job.writerThinkingLevel, serviceTier: null };
    }
    return this.writerBadgesCache;
  }

  /** A row's charts cell: the step attaches charts for the whole job, so every written row reads the same. */
  chartsText(row: CcReportJobRow): string {
    if (row.documentId === null) return '—';
    switch (this.chartState) {
      case 'attaching': return 'Charts: attaching…';
      case 'done': return 'Charts: attached';
      case 'failed': return 'Charts: failed';
      default: return '—';
    }
  }

  logTime(timestampUtc: string): string {
    return formatUtcSeconds(timestampUtc) ?? timestampUtc;
  }

  // --- Diagnostics ---

  diagnosticsText(nowUtc: Date = new Date()): string {
    const subject = this.result?.subject ?? null;
    return ccReportJobDiagnostics(this.job, nowUtc, {
      analysisId: this.analysisId,
      analysisName: this.result?.name ?? null,
      subject: subject
        ? { displayName: subject.displayName, provider: subject.provider, modelId: subject.modelId, thinkingLevel: subject.thinkingLevel }
        : null,
      chartState: this.chartState,
      chartMessage: this.chartMessage,
      client: {
        pollCount: this.pollCount,
        lastSuccessUtc: this.lastSuccessUtc,
        consecutiveFailures: this.pollFailures,
        lastError: this.lastPollError
      },
      estimateUsd: this.jobEstimateUsd
    });
  }

  async copyDiagnostics(): Promise<void> {
    const analysisId = this.analysisId;
    this.copyStatus = '';
    this.copyError = null;
    this.cdr.markForCheck();
    const ok = await ccReportIo.copy(this.diagnosticsText());
    if (analysisId !== this.analysisId) return;
    if (ok) {
      this.copyStatus = 'Copied';
    } else {
      this.copyError = 'The diagnostics could not be copied to the clipboard. Download them instead.';
    }
    this.cdr.markForCheck();
  }

  downloadDiagnostics(): void {
    const now = new Date();
    ccReportIo.download(ccReportDiagnosticsFileName(this.analysisId, now), this.diagnosticsText(now));
  }

  // --- View and Delete ---

  /**
   * Opens a written document in the PDF viewer at the fullest disclosure it allows, the others offered
   * as versions; with peers, the peer names are a second choice that opens at *Named*.
   */
  viewDocument(audience: BenchmarkReportAudience, button: HTMLElement): void {
    const doc = this.written.get(audience);
    if (!doc) return;
    this.returnFocus = button;
    const { disclosures, highest, disclosureOf } = reportDocumentDisclosures(doc);
    const namingOf = (secondary: string | undefined): BenchmarkReportPeerNaming =>
      secondary === reportPeerNamingParam(BenchmarkReportPeerNaming.Anonymized)
        ? BenchmarkReportPeerNaming.Anonymized
        : BenchmarkReportPeerNaming.Named;
    const hasPeers = (doc.peerCount ?? 0) > 0;
    const paper = rememberedPdfPaper();
    const label = ccReportAudienceLabel(audience);
    this.pdfViewer?.open({
      title: doc.title || label,
      subtitle: reportDocumentByline(doc, null),
      variants: disclosures.map(disclosure => ({ key: reportDisclosureParam(disclosure), label: disclosureLabel(disclosure) })),
      initialVariant: reportDisclosureParam(highest),
      variantsInfo: reportDisclosureInfo(audience, { peerNaming: hasPeers }),
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

  /** The viewer closed: focus returns to the View button that opened it. */
  onViewerClosed(): void {
    const target = this.returnFocus;
    this.returnFocus = null;
    if (target?.isConnected) target.focus();
  }

  requestDelete(audience: BenchmarkReportAudience, button: HTMLElement): void {
    const doc = this.written.get(audience);
    if (!doc || this.deleteBusy) return;
    this.deleteTarget = { audience, label: ccReportAudienceLabel(audience), doc };
    this.deleteError = null;
    this.deleting = false;
    this.returnFocus = button;
    this.focusAfterDelete = false;
    this.cdr.detectChanges();
    const dialog = this.deleteDialog?.nativeElement;
    if (dialog && !dialog.open) dialog.showModal();
  }

  cancelDelete(): void {
    this.deleteDialog?.nativeElement.close();
  }

  confirmDelete(): void {
    const target = this.deleteTarget;
    if (!target || this.deleting || this.deleteBusy) return;
    this.deleting = true;
    this.deleteError = null;
    this.deleteSub?.unsubscribe();
    this.deleteSub = this.benchmarkService.deleteReportDocument(target.doc.id).subscribe({
      next: () => {
        this.deleting = false;
        this.documentsLiveText = `Deleted ${this.rowName(target.audience)}.`;
        this.focusAfterDelete = true;
        this.deleteDialog?.nativeElement.close();
        this.loadDocuments();
        this.documentsChanged.emit();
        this.cdr.markForCheck();
      },
      error: (error: HttpErrorResponse) => {
        this.deleting = false;
        this.deleteError = ccErrorText(error, 'The document could not be deleted.');
        this.cdr.markForCheck();
      }
    });
    this.cdr.markForCheck();
  }

  /** After a delete, focus goes to the sidebar's heading, the row's buttons being gone; otherwise back to Delete. */
  onDeleteDialogEvent(event: Event): void {
    event.stopPropagation();
    if (event.type !== 'close' || this.deleteDialog?.nativeElement.open) return;
    this.deleteTarget = null;
    this.deleteError = null;
    const target = this.focusAfterDelete
      ? this.host.nativeElement.querySelector<HTMLElement>('#cc-rep-new-heading')
      : this.returnFocus;
    this.focusAfterDelete = false;
    this.returnFocus = null;
    this.cdr.markForCheck();
    if (target?.isConnected) target.focus();
  }

  /** Lists the analysis's documents again, as after a delete in step 6. */
  reloadDocuments(): void {
    this.loadDocuments();
  }

  // --- Job, documents and charts ---

  private applyJob(job: BenchmarkRunReportJobDto): void {
    this.job = job;
    this.jobReadAt = Date.now();
    this.status = reportDocumentsStatusOf(job.status);
    this.statusMessage = job.message ?? null;
  }

  /** A job already running for this analysis is shown and followed. */
  private resumeJob(): void {
    const analysisId = this.analysisId;
    if (analysisId === null) return;
    this.pollSub = this.service.getReportJob(analysisId).subscribe({
      next: job => {
        if (job) {
          this.applyJob(job);
          if (this.jobInProgress) this.schedulePoll(analysisId);
        }
        this.syncState();
        this.cdr.markForCheck();
      },
      error: () => { /* No job information: the step starts idle. */ }
    });
  }

  private schedulePoll(analysisId: number): void {
    this.pollSub?.unsubscribe();
    this.pollSub = timer(this.pollDelay).pipe(
      switchMap(() => {
        this.pollCount++;
        return this.service.getReportJob(analysisId);
      })
    ).subscribe({
      next: job => {
        this.pollDelay = CC_REPORT_POLL_MS;
        this.pollFailures = 0;
        this.lastSuccessUtc = new Date();
        if (job) this.applyJob(job);
        if (this.jobInProgress) {
          this.schedulePoll(analysisId);
        } else {
          this.onFinished(analysisId);
        }
        this.syncState();
        this.cdr.markForCheck();
      },
      error: (error: unknown) => {
        this.pollFailures++;
        const http = error instanceof HttpErrorResponse ? error : null;
        this.lastPollError = {
          httpStatus: http && http.status > 0 ? http.status : null,
          message: (http ? http.statusText || http.message : null) || 'Unknown error'
        };
        this.pollDelay = Math.min(this.pollDelay * 2, CC_REPORT_POLL_MAX_MS);
        this.schedulePoll(analysisId);
      }
    });
  }

  /** The job ended: the documents are listed again, and those without charts get them. */
  private onFinished(analysisId: number): void {
    this.loadDocuments(uncharted => {
      this.documentsChanged.emit();
      if (uncharted.length > 0) void this.attachCharts(analysisId, uncharted.map(chartDocumentOf), 'job');
    });
  }

  /** Lists the analysis's documents; the Write checkboxes are recomputed from what is written. */
  private loadDocuments(after?: (uncharted: BenchmarkReportDocumentListItemDto[]) => void): void {
    const analysisId = this.analysisId;
    if (analysisId === null) return;
    this.documentsSub?.unsubscribe();
    this.documentsSub = this.benchmarkService.listReportDocuments({ subject: `chat-consistency:${analysisId}` }).subscribe({
      next: documents => {
        if (this.analysisId !== analysisId) return;
        this.applyDocuments(documents ?? []);
        this.cdr.markForCheck();
        after?.(this.documents.filter(doc => (doc.chartCount ?? 0) === 0));
      },
      error: () => {
        this.applyDocuments([]);
        this.cdr.markForCheck();
      }
    });
  }

  private applyDocuments(documents: BenchmarkReportDocumentListItemDto[]): void {
    this.documents = documents;
    const newestFirst = [...documents]
      .sort((a, b) => (b.createdAtUtc ?? '').localeCompare(a.createdAtUtc ?? '') || b.id - a.id);
    this.written = new Map();
    for (const doc of newestFirst) {
      if (!this.written.has(doc.audience)) this.written.set(doc.audience, doc);
    }
    // Every unwritten document is checked, the Provider Issue Report only by hand once it is available.
    this.checked.clear();
    for (const entry of CC_REPORT_AUDIENCES) {
      if (entry.audience !== BenchmarkReportAudience.ProviderIssueReport && !this.written.has(entry.audience)) {
        this.checked.add(entry.audience);
      }
    }
    this.queueEstimate();
  }

  /**
   * What the attached charts draw: the analyzed units (battery runs in a battery analysis, runs
   * otherwise), the analysis's events and annotations, its period bands, every timeline point for the
   * events' harness lookup, the timeline's E numbers, and the endpoints, whose not computable ones
   * the captions name.
   */
  chartInput(): CcFigureInput {
    const { points, unitKind } = analysisChartPoints(this.result, this.points, this.batteryPoints);
    return {
      points,
      unitKind,
      events: this.result.events,
      annotations: this.result.annotations,
      bands: analysisBands(this.result.baseline, this.result.comparison),
      harnessPoints: this.points,
      eventNumbering: this.eventNumbering,
      endpoints: this.result.endpoints
    };
  }

  /** Draws and attaches the charts chosen for each document's type: after a job, or for Update charts. */
  private async attachCharts(analysisId: number, documents: CcReportChartDocument[], origin: ChartOrigin): Promise<void> {
    const generation = ++this.chartsGeneration;
    const count = documents.length;
    const noun = (n: number) => (n === 1 ? 'document' : 'documents');
    this.chartState = 'attaching';
    this.chartOrigin = origin;
    this.chartMessage = origin === 'update'
      ? `Updating the charts of ${count} ${noun(count)}…`
      : `Attaching charts to ${count} ${noun(count)}…`;
    this.syncState();
    this.cdr.markForCheck();
    try {
      const outcome = await publishCcReportCharts(this.benchmarkService, documents, this.chartInput(), this.chartSettings);
      if (generation !== this.chartsGeneration || this.analysisId !== analysisId) return;
      this.applyChartCounts(outcome);
      if (outcome.failed.length === 0) {
        const published = outcome.published.length;
        const without = outcome.withoutCharts.length;
        this.chartState = 'done';
        this.chartMessage = (origin === 'update'
          ? `Charts updated on ${published} ${noun(published)}.`
          : `Charts attached to ${published} ${noun(published)}.`)
          + (without > 0 ? ` ${without} ${without === 1 ? 'document has' : 'documents have'} no chart chosen.` : '');
      } else {
        this.chartState = 'failed';
        this.chartMessage = `Charts could not be attached to ${outcome.failed.length} of ${count} ${noun(count)}: ${outcome.failed[0].message}`;
      }
      this.documentsChanged.emit();
    } catch (error) {
      if (generation !== this.chartsGeneration) return;
      this.chartState = 'failed';
      this.chartMessage = `The charts could not be drawn: ${error instanceof Error ? error.message : 'unknown error'}`;
    }
    this.syncState();
    this.cdr.markForCheck();
  }

  /**
   * The listed documents with the chart counts a publish left them, so a later Update charts knows
   * which have charts to remove; the Write checkboxes are left as they are.
   */
  private applyChartCounts(outcome: CcChartPublishResult): void {
    const counts = new Map<number, number>(outcome.published.map(entry => [entry.documentId, entry.chartCount]));
    for (const id of outcome.withoutCharts) counts.set(id, 0);
    if (counts.size === 0) return;
    const patch = (doc: BenchmarkReportDocumentListItemDto) => {
      const chartCount = counts.get(doc.id);
      return chartCount === undefined ? doc : { ...doc, chartCount };
    };
    this.documents = this.documents.map(patch);
    this.written = new Map([...this.written].map(([audience, doc]) => [audience, patch(doc)]));
  }

  /**
   * Keeps the one-second clock running while a job runs, and emits `stateChange` when whether a job
   * runs or charts are attached changes.
   */
  private syncState(deferEvent = false): void {
    const running = this.jobInProgress;
    if (running && this.job) {
      this.tickSub ??= interval(1000).subscribe(() => this.cdr.markForCheck());
    } else {
      this.stopTick();
    }
    const key = `${running}|${this.chartState}`;
    if (key === this.lastStateKey) return;
    this.lastStateKey = key;
    if (deferEvent) {
      void Promise.resolve().then(() => this.stateChange.emit());
    } else {
      this.stateChange.emit();
    }
  }

  private stopTick(): void {
    this.tickSub?.unsubscribe();
    this.tickSub = null;
  }

  private queueEstimate(): void {
    const analysisId = this.analysisId;
    const writerId = this.writerId;
    const audiences = this.checkedAudiences;
    // With nothing checked, the estimate still answers whether the Provider Issue Report is available.
    const asked = audiences.length > 0 ? audiences : [BenchmarkReportAudience.ExecutiveSummary];
    const request: EstimateRequest | null = analysisId !== null && writerId !== null
      ? { key: `${analysisId}|${writerId}|${asked.join(',')}`, analysisId, writerId, audiences: asked }
      : null;
    const key = request?.key ?? null;
    if (key === this.requestedKey) return;
    this.requestedKey = key;
    this.estimating = request !== null;
    this.estimateFailed = false;
    this.estimateRequests.next(request);
  }

  private reset(): void {
    for (const sub of [this.writeSub, this.pollSub, this.cancelSub, this.documentsSub, this.deleteSub]) {
      sub?.unsubscribe();
    }
    this.writeSub = this.pollSub = this.cancelSub = this.documentsSub = this.deleteSub = null;
    this.stopTick();
    this.chartsGeneration++;
    this.status = BenchmarkRunReportDocumentsStatus.NotRequested;
    this.statusMessage = null;
    this.job = null;
    this.jobEstimateUsd = null;
    this.dismissed = false;
    this.estimate = null;
    this.estimating = false;
    this.estimateFailed = false;
    this.writeSubmitting = false;
    this.writeError = null;
    this.canceling = false;
    this.cancelError = null;
    this.chartState = 'idle';
    this.chartMessage = '';
    this.chartOrigin = 'job';
    this.documents = [];
    this.written = new Map();
    this.deleteTarget = null;
    this.deleting = false;
    this.deleteError = null;
    this.documentsLiveText = '';
    this.pollCount = 0;
    this.pollFailures = 0;
    this.pollDelay = CC_REPORT_POLL_MS;
    this.lastSuccessUtc = null;
    this.lastPollError = null;
    this.copyStatus = '';
    this.copyError = null;
    this.freshness = null;
    this.outOfDateReasons = [];
    this.acknowledgedSameProvider = false;
    this.acknowledgedOutOfDate = false;
    this.checked.clear();
    for (const entry of CC_REPORT_AUDIENCES) {
      if (entry.audience !== BenchmarkReportAudience.ProviderIssueReport) this.checked.add(entry.audience);
    }
    this.requestedKey = null;
    this.estimateRequests.next(null);
    this.queueEstimate();
  }
}
