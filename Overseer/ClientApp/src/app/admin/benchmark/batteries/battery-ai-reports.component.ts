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
import { Observable, Subject, Subscription, catchError, debounceTime, map, of, switchMap, timer } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkBatteryAnalysisDto,
  BenchmarkBatteryRunDto,
  BenchmarkReportAudience,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPeerNaming,
  BenchmarkRunDetailDto,
  BenchmarkRunReportDocumentsStatus,
  BenchmarkRunReportEstimateDto,
  BenchmarkRunReportJobDto,
  WriteRunReportDocumentsRequest,
  reportDisclosureParam
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { ModelPickerComponent, ModelPickerOption } from '../../../shared/model-picker/model-picker.component';
import { PdfViewerDialogComponent } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { safeFileName } from '../../../utils/download.util';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import {
  BenchmarkDownloadCenterComponent,
  DownloadCenterBatteryContext,
  rememberedPdfPaper
} from '../download-center/benchmark-download-center.component';
import { reportDisclosureInfo } from '../report-disclosure-guide';
import { audienceLabel, disclosureLabel } from '../report-pack/report-document-format';
import {
  BATTERY_COMPLETION_ORIGIN,
  ReportDocumentRow,
  ReportDocumentsStatusKind,
  ReportEstimateView,
  ReportStatusSnapshot,
  completionDocumentsOf,
  isReportWriterSameProviderWarning,
  missingReportAudiences,
  reportDateUtc,
  reportDocumentByline,
  reportDocumentDisclosures,
  reportDocumentMeta,
  reportDocumentRows,
  reportDocumentTag,
  reportDocumentWriter,
  reportDocumentsStatusOf,
  reportEstimateView,
  reportJobInProgress,
  reportJobStatusKind,
  reportJobStatusText,
  reportServerErrorText,
  reportStatusSnapshotOfJob,
  writtenReportLabels
} from '../run-ai-reports/report-documents-list';
import { BATTERY_REPORT_WRITER_ADVICE } from '../run-ai-reports/report-writer-advice';
import {
  ReportWriterCandidate,
  isSameProvider,
  reportWriterRefusal,
  reportWriterWarning,
  reportWriterWarningText
} from '../run-ai-reports/report-writer-policy';
import { ReportJobSource, RunReportWritingDialogComponent } from '../run-ai-reports/run-report-writing-dialog.component';
import { BenchmarkWorkspaceStore } from '../state/benchmark-workspace.store';
import { isFinishedBatteryRunStatus } from './battery.models';

/** The interval of the tab's status poll while a battery run's reports are queued or written. */
export const BATTERY_REPORT_DOCUMENTS_POLL_MS = 5000;

/** The pause after the last change of writer or documents before the cost is estimated. */
export const BATTERY_REPORT_ESTIMATE_DEBOUNCE_MS = 300;

/** Why a battery run's documents cannot be written yet, word for word as the write panel says it. */
export const BATTERY_REPORT_NOT_WRITABLE = {
  notFinished: 'The battery run has not finished. Its reports can be written once it has finished and its analysis is complete.',
  noAnalysis: 'The battery run has no analysis yet. Compute the analysis first.',
  incomplete: 'The analysis is incomplete: every suite needs at least one usable member run.',
  stale: 'The analysis is out of date: the member runs changed since it was computed. Recompute it first.'
} as const;

/** The battery run's report fields as the tab last read them. */
export interface BatteryReportStatusChange {
  status: BenchmarkRunReportDocumentsStatus;
  message: string | null;
  writerId: number | null;
  writerName: string | null;
}

interface EstimateRequest {
  key: string;
  batteryRunId: number;
  writerId: number;
  audiences: BenchmarkReportAudience[];
}

interface EstimateResult {
  request: EstimateRequest;
  estimate: BenchmarkRunReportEstimateDto | null;
  failed: boolean;
}

let nextInstanceId = 0;

/**
 * The battery run report's AI Reports tab, the counterpart of the run report's `app-run-ai-reports`:
 * the job state, one row per battery-completion document (written or not) with View and Delete, a
 * pointer to the Download Center, and, while a document is missing, the documents to write, the
 * report writer, the cost estimate and Write Reports. A battery run's documents can be written only
 * once it has finished with a complete, current analysis; until then the write panel says why.
 *
 * It owns its own polling, estimate and dialogs: the progress dialog (following the battery run's
 * job through a {@link ReportJobSource}), the PDF viewer, two confirmations and, when no host
 * listens for `downloadsRequested`, a Download Center of its own. The confirmations sit inside the
 * battery run report dialog, so their close and cancel events stop here.
 */
@Component({
  selector: 'app-battery-ai-reports',
  standalone: true,
  imports: [
    ModelPickerComponent,
    InfoTipComponent,
    RunReportWritingDialogComponent,
    PdfViewerDialogComponent,
    BenchmarkDownloadCenterComponent
  ],
  templateUrl: './battery-ai-reports.component.html',
  styleUrls: ['../run-ai-reports/run-ai-reports.component.scss', './battery-ai-reports.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BatteryAiReportsComponent implements OnInit, OnChanges, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly workspace = inject(BenchmarkWorkspaceStore, { optional: true });
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

  /** The battery run the report dialog shows. */
  @Input() batteryRun: BenchmarkBatteryRunDto | null = null;
  /** Its latest analysis; whether it is complete and current decides whether the documents can be written. */
  @Input() analysis: BenchmarkBatteryAnalysisDto | null = null;
  /** The report dialog is open; the tab polls only then. */
  @Input() dialogOpen = true;
  /** The report writer picker's options; the workspace's benchmark-capable configurations when unset. */
  @Input() pickerOptions: readonly ModelPickerOption<SystemAiConfigDto>[] | null = null;
  /** The configurations the options stand for; the workspace's when unset. */
  @Input() writerConfigs: readonly SystemAiConfigDto[] | null = null;
  /** The picker's text when there are no options; the workspace's when unset. */
  @Input() pickerEmptyHint: string | null = null;
  /**
   * A member run's diagnostics text, for the tab's own Download Center to list each member run's
   * diagnostics; without it the member runs' diagnostics are not listed.
   */
  @Input() memberDiagnosticsText?: (run: BenchmarkRunDetailDto) => string;

  /** The button that asked for the Download Center; with no listener the tab opens its own. */
  @Output() readonly downloadsRequested = new EventEmitter<HTMLElement>();
  /** The battery run's report status, writer and message changed. */
  @Output() readonly reportStatusChange = new EventEmitter<BatteryReportStatusChange>();

  @ViewChild(RunReportWritingDialogComponent) writingDialog?: RunReportWritingDialogComponent;
  @ViewChild(PdfViewerDialogComponent) pdfViewer?: PdfViewerDialogComponent;
  @ViewChild(BenchmarkDownloadCenterComponent) downloadCenter?: BenchmarkDownloadCenterComponent;
  @ViewChild('deleteDialog') deleteDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('sameProviderDialog') sameProviderDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('writeButton') writeButton?: ElementRef<HTMLButtonElement>;

  /** The prefix of every element id; the progress dialog's ids take it too. */
  readonly idPrefix = `bai${++nextInstanceId}`;

  /** The *Choosing a report writer* tip's entries. */
  readonly writerAdvice = BATTERY_REPORT_WRITER_ADVICE;

  /**
   * The battery run's AI-written (battery-completion) documents, one per audience, from the
   * document list. Whether they exist is read here and never from the battery run's status.
   */
  documents: BenchmarkReportDocumentListItemDto[] = [];
  /** The document list of the battery run has answered. */
  documentsLoaded = false;
  documentsError: string | null = null;

  /** The writer Write Reports sends; starts as the battery run's own writer. */
  writerConfigId: number | null = null;
  /** Why the picker was cleared, after a delete. */
  writerNote = '';
  writeSubmitting = false;
  writeError: string | null = null;
  viewError: string | null = null;
  /** A one-off confirmation read out in the status line. */
  announcement = '';

  status: BenchmarkRunReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.NotRequested;
  statusMessage: string | null = null;
  /** The battery run's report writer as the server last named it. */
  batteryWriterId: number | null = null;
  batteryWriterName: string | null = null;

  estimate: BenchmarkRunReportEstimateDto | null = null;
  estimateLoading = false;
  estimateFailed = false;

  /** The document the delete confirmation is about. */
  deleteTarget: BenchmarkReportDocumentListItemDto | null = null;
  deleting = false;
  deleteError: string | null = null;

  /** The same-provider confirmation's sentence. */
  confirmWarningText = '';

  /** The tab's own Download Center is rendered, after the first request no host answered. */
  ownDownloadCenter = false;

  /** Missing documents unchecked in the write panel; every other missing one is checked. */
  private readonly deselected = new Set<BenchmarkReportAudience>();
  private readonly estimateRequests = new Subject<EstimateRequest | null>();
  private readonly estimateSub: Subscription;
  private requestedEstimateKey: string | null = null;
  /** The estimate total when the last write was requested, for Show Progress. */
  private lastEstimateUsd: number | null = null;
  /** Where focus returns when the tab's own Download Center closes. */
  private downloadsReturnFocus: HTMLElement | null = null;

  private documentsSub: Subscription | null = null;
  private pollSub: Subscription | null = null;
  private pollBatteryRunId: number | null = null;
  private writeSub: Subscription | null = null;
  private deleteSub: Subscription | null = null;
  private finishSub: Subscription | null = null;

  constructor() {
    this.estimateSub = this.estimateRequests.pipe(
      debounceTime(BATTERY_REPORT_ESTIMATE_DEBOUNCE_MS),
      switchMap((request): Observable<EstimateResult | null> => request === null
        ? of(null)
        : this.benchmarkService.estimateBatteryReports(request.batteryRunId, {
          writerModelConfigurationId: request.writerId,
          audiences: request.audiences
        }).pipe(
          map((estimate): EstimateResult => ({ request, estimate, failed: false })),
          catchError(() => of<EstimateResult>({ request, estimate: null, failed: true }))
        ))
    ).subscribe(result => {
      if (!result || result.request.key !== this.requestedEstimateKey) {
        return;
      }
      this.estimateLoading = false;
      this.estimate = result.estimate;
      this.estimateFailed = result.failed;
      this.cdr.markForCheck();
    });
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    const runChange = changes['batteryRun'];
    if (runChange) {
      const previous = runChange.previousValue as BenchmarkBatteryRunDto | null | undefined;
      const run = this.batteryRun;
      if (!run) {
        this.reset();
      } else if (!previous || previous.id !== run.id) {
        this.reset();
        this.setSnapshot(this.snapshotOfBatteryRun(run));
        this.writerConfigId = this.defaultWriterId(run);
        this.loadDocuments();
      } else {
        this.setSnapshot(this.snapshotOfBatteryRun(run));
        // A battery run that has just finished may have had its reports written after its analysis.
        if (!isFinishedBatteryRunStatus(previous.status) && isFinishedBatteryRunStatus(run.status)) {
          this.loadDocuments();
        }
      }
    }
    if (runChange || changes['analysis']) {
      this.queueEstimate();
    }
    if (runChange || changes['dialogOpen']) {
      this.syncPoll();
    }
  }

  ngOnDestroy(): void {
    this.reset();
    this.estimateSub.unsubscribe();
  }

  // --- Inputs, with the workspace as their fallback ---

  get options(): readonly ModelPickerOption<SystemAiConfigDto>[] {
    return this.pickerOptions ?? this.workspace?.benchmarkPickerOptions ?? [];
  }

  get configs(): readonly SystemAiConfigDto[] {
    return this.writerConfigs ?? this.workspace?.stableBenchmarkCapableConfigs ?? [];
  }

  get emptyHint(): string | null {
    return this.pickerEmptyHint ?? this.workspace?.benchmarkPickerEmptyHint ?? null;
  }

  // --- What is shown ---

  /** A job for the battery run's documents is queued or writing. */
  get jobInProgress(): boolean {
    return reportJobInProgress(this.status);
  }

  /** The battery run finished with a status its documents can be written for. */
  get batteryFinished(): boolean {
    return isFinishedBatteryRunStatus(this.batteryRun?.status);
  }

  /**
   * Why the battery run's documents cannot be written now, or '' when they can: it must have
   * finished, with a latest analysis that is complete and current. The server decides the same.
   */
  get notWritableReason(): string {
    const run = this.batteryRun;
    if (!run) return '';
    if (!this.batteryFinished) return BATTERY_REPORT_NOT_WRITABLE.notFinished;
    const analysis = this.analysis;
    const hasAnalysis = analysis !== null || run.latestAnalysisId != null;
    if (!hasAnalysis) return BATTERY_REPORT_NOT_WRITABLE.noAnalysis;
    const complete = analysis ? analysis.complete : run.latestAnalysisComplete === true;
    if (!complete) return BATTERY_REPORT_NOT_WRITABLE.incomplete;
    if ((analysis?.stale ?? false) || run.analysisStale) return BATTERY_REPORT_NOT_WRITABLE.stale;
    return '';
  }

  /** The job state, or '' when the document rows already say everything. */
  get statusText(): string {
    const text = reportJobStatusText(this.status, this.statusMessage);
    if (text) {
      return text;
    }
    if (this.status === BenchmarkRunReportDocumentsStatus.NotRequested && this.batteryWriterId != null &&
      !this.batteryFinished) {
      return `Not written yet: ${this.batteryWriterName || 'the report writer'} writes them once the battery run is finished and analyzed`;
    }
    return '';
  }

  get statusKind(): ReportDocumentsStatusKind {
    return reportJobStatusKind(this.status);
  }

  documentFor(audience: BenchmarkReportAudience): BenchmarkReportDocumentListItemDto | undefined {
    return this.documents.find(doc => doc.audience === audience);
  }

  /** One row per battery-completion audience, in list order, with its stored document if any. */
  get documentRows(): ReportDocumentRow[] {
    return reportDocumentRows(this.documents);
  }

  /** At least one document is written, so the Download Center has something of this tab's to offer. */
  get hasWrittenDocument(): boolean {
    return this.documents.length > 0;
  }

  /** The documents the battery run lacks, in list order. */
  get missingAudiences(): BenchmarkReportAudience[] {
    return missingReportAudiences(this.documents);
  }

  /** The written documents, whose names the write panel lists as not writable. */
  get writtenLabels(): string[] {
    return writtenReportLabels(this.documents);
  }

  /** The battery run lacks a document, so the tab offers the write panel, with its reason while it cannot write. */
  get documentsMissing(): boolean {
    return !!this.batteryRun && this.documentsLoaded && this.documentsError === null && this.missingAudiences.length > 0;
  }

  audienceLabel(audience: BenchmarkReportAudience): string {
    return audienceLabel(audience);
  }

  isChecked(audience: BenchmarkReportAudience): boolean {
    return !this.deselected.has(audience);
  }

  /** The missing documents Write Reports sends. */
  get checkedAudiences(): BenchmarkReportAudience[] {
    return this.missingAudiences.filter(audience => !this.deselected.has(audience));
  }

  get writeLabel(): string {
    if (this.writeSubmitting) return 'Requesting…';
    return this.checkedAudiences.length === 1 ? 'Write Report' : 'Write Reports';
  }

  private get selectedWriter(): SystemAiConfigDto | undefined {
    return this.configs.find(config => config.id === this.writerConfigId);
  }

  private get candidate(): ReportWriterCandidate | null {
    const run = this.batteryRun;
    return run
      ? { id: run.testedModelConfigurationId ?? null, provider: run.testedProvider ?? null, modelId: run.testedModelId ?? null }
      : null;
  }

  /** Why Write Reports is refused for the chosen writer and this battery run's candidate, or '' when it is not. */
  get writeRefusal(): string {
    if (!this.batteryRun || this.notWritableReason) return '';
    const refusal = reportWriterRefusal(this.selectedWriter, this.candidate);
    if (refusal) return refusal;
    return this.writerConfigId != null ? (this.estimate?.refusal?.trim() || '') : '';
  }

  /** The same-provider warning the chosen writer carries, or ''. Writing stays possible after a confirmation. */
  get writerWarning(): string {
    if (this.writerConfigId == null || this.writeRefusal) return '';
    const warning = reportWriterWarning(this.selectedWriter, this.candidate);
    if (warning) return warning;
    const server = this.estimate?.sameProviderWarning;
    return server ? reportWriterWarningText(server.assessorModelDisplayName, server.provider) : '';
  }

  get blockedId(): string {
    return `${this.idPrefix}ReportWriterBlocked`;
  }

  get warningId(): string {
    return `${this.idPrefix}ReportWriterWarning`;
  }

  get notWritableId(): string {
    return `${this.idPrefix}NotWritable`;
  }

  get estimateId(): string {
    return `${this.idPrefix}WriteEstimate`;
  }

  /**
   * The picker's description: the refusal or the warning while there is one. The report writer
   * info is a dialog, too long to be read out as a description.
   */
  get writerDescribedBy(): string | null {
    const ids = [this.writeRefusal ? this.blockedId : '', this.writerWarning ? this.warningId : '']
      .filter(id => id !== '');
    return ids.length > 0 ? ids.join(' ') : null;
  }

  /** The Write button's description: why the battery run cannot be written yet, then the estimate. */
  get writeDescribedBy(): string {
    return this.notWritableReason ? `${this.notWritableId} ${this.estimateId}` : this.estimateId;
  }

  get writeDisabled(): boolean {
    return !!this.notWritableReason || this.writerConfigId == null || this.checkedAudiences.length === 0 ||
      !!this.writeRefusal || this.jobInProgress || this.writeSubmitting;
  }

  /** The cost estimate block under the write row, or null when there is nothing to say. */
  get estimateView(): ReportEstimateView | null {
    return reportEstimateView(this.estimateLoading, this.estimateFailed, this.estimate);
  }

  documentStatusLabel(doc: BenchmarkReportDocumentListItemDto): string {
    return reportDocumentTag(doc);
  }

  documentWriter(doc: BenchmarkReportDocumentListItemDto): string {
    return reportDocumentWriter(doc, this.batteryWriterName);
  }

  /** When a document was written, in UTC. */
  documentDate(doc: BenchmarkReportDocumentListItemDto): string {
    return reportDateUtc(doc.createdAtUtc);
  }

  /** A written document's writer and date. */
  documentByline(doc: BenchmarkReportDocumentListItemDto): string {
    return reportDocumentByline(doc, this.batteryWriterName);
  }

  /** The writer and date, then the stored duration and cost where known. */
  documentMeta(doc: BenchmarkReportDocumentListItemDto): string {
    return reportDocumentMeta(doc, this.batteryWriterName);
  }

  /** What the subtitles name the battery run by: its battery and model. */
  get batteryLabel(): string {
    const run = this.batteryRun;
    if (!run) return '';
    return [run.batteryName, run.testedModelLabel].filter(part => !!part).join(' · ');
  }

  // --- Choices ---

  selectWriter(config: SystemAiConfigDto | null): void {
    this.writerConfigId = config?.id ?? null;
    this.writeError = null;
    if (this.writerConfigId != null) {
      this.writerNote = '';
    }
    this.queueEstimate();
    this.cdr.markForCheck();
  }

  toggleAudience(audience: BenchmarkReportAudience, event: Event): void {
    if ((event.target as HTMLInputElement).checked) {
      this.deselected.delete(audience);
    } else {
      this.deselected.add(audience);
    }
    this.queueEstimate();
    this.cdr.markForCheck();
  }

  // --- Writing ---

  /** Write Reports: a same-provider writer asks first, every other one writes at once. */
  requestWrite(): void {
    if (this.writeDisabled) return;
    const warning = this.writerWarning;
    if (warning) {
      this.openSameProviderConfirm(warning);
      return;
    }
    this.sendWrite(false);
  }

  confirmSameProvider(): void {
    this.sameProviderDialog?.nativeElement.close();
    this.sendWrite(true);
  }

  cancelSameProvider(): void {
    this.sameProviderDialog?.nativeElement.close();
    this.writeButton?.nativeElement.focus();
  }

  private openSameProviderConfirm(text: string): void {
    this.confirmWarningText = text;
    this.cdr.detectChanges();
    const dialog = this.sameProviderDialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
  }

  private sendWrite(acknowledgeSameProvider: boolean): void {
    const run = this.batteryRun;
    const writerId = this.writerConfigId;
    if (!run || writerId == null || this.writeSubmitting) return;
    const batteryRunId = run.id;
    const request: WriteRunReportDocumentsRequest = {
      writerModelConfigurationId: writerId,
      audiences: this.checkedAudiences,
      ...(acknowledgeSameProvider ? { acknowledgeSameProvider: true } : {})
    };
    const estimateUsd = this.estimate?.estimatedTotalCostUsd ?? null;
    this.writeSubmitting = true;
    this.writeError = null;
    this.announcement = '';
    this.cdr.markForCheck();
    this.writeSub?.unsubscribe();
    this.writeSub = this.benchmarkService.writeBatteryReportDocuments(batteryRunId, request).subscribe({
      next: response => {
        this.writeSubmitting = false;
        if (this.batteryRun?.id !== batteryRunId) return;
        const writer = this.configs.find(config => config.id === writerId);
        this.setSnapshot({
          status: reportDocumentsStatusOf(response?.status ?? BenchmarkRunReportDocumentsStatus.Pending),
          message: null,
          writerId,
          writerName: writer?.displayName ?? this.batteryWriterName
        });
        this.lastEstimateUsd = estimateUsd;
        this.emitStatus();
        this.syncPoll();
        this.cdr.markForCheck();
        this.openProgress(estimateUsd);
      },
      error: err => {
        this.writeSubmitting = false;
        if (this.batteryRun?.id !== batteryRunId) return;
        // The selection changed under the client's check: the server asks for the acknowledgment.
        if (err?.status === 409 && !acknowledgeSameProvider && isReportWriterSameProviderWarning(err.error)) {
          this.cdr.markForCheck();
          this.openSameProviderConfirm(reportWriterWarningText(err.error.assessorModelDisplayName, err.error.provider));
          return;
        }
        this.writeError = BatteryAiReportsComponent.writeErrorOf(err, batteryRunId);
        // A 409 means the list or the status moved on elsewhere; show where they stand now.
        if (err?.status === 409) {
          this.loadDocuments();
        }
        this.cdr.markForCheck();
      }
    });
  }

  private static writeErrorOf(err: any, batteryRunId: number): string {
    if (err?.status === 404) return `Battery run #${batteryRunId} no longer exists.`;
    if (err?.status === 0) return 'The server could not be reached.';
    return reportServerErrorText(err) ?? `The reports could not be requested (HTTP ${err?.status ?? 'error'}).`;
  }

  /** Show Progress: the progress dialog of the job in hand. Closing it never cancels the job. */
  showProgress(): void {
    this.openProgress(this.lastEstimateUsd);
  }

  /** The battery run's job, as the progress dialog follows it. */
  private jobSourceFor(run: BenchmarkBatteryRunDto): ReportJobSource {
    const batteryRunId = run.id;
    return {
      getJob: () => this.benchmarkService.getBatteryReportJob(batteryRunId),
      cancel: () => this.benchmarkService.cancelBatteryReportJob(batteryRunId),
      subjectLabel: `Battery run #${batteryRunId}`,
      getStoredStatus: () => this.benchmarkService.getBatteryRun(batteryRunId),
      fileStem: `battery-run-${batteryRunId}`
    };
  }

  private openProgress(estimateUsd: number | null): void {
    const run = this.batteryRun;
    if (!run) return;
    this.writingDialog?.open({
      runId: run.id,
      runLabel: this.batteryLabel,
      estimateUsd,
      run: {
        runId: run.id,
        suiteName: run.batteryName,
        candidateLabel: run.testedModelLabel ?? null,
        provider: run.testedProvider ?? null,
        modelId: run.testedModelId ?? null,
        reportDocumentsStatus: this.status,
        reportDocumentsMessage: this.statusMessage
      },
      source: this.jobSourceFor(run)
    });
  }

  /** The progress dialog's job finished: the documents and the status are read again. */
  onWritingFinished(view: BenchmarkRunReportJobDto | null): void {
    const run = this.batteryRun;
    if (!run) return;
    if (view && view.runId === run.id) {
      this.afterFinish(reportStatusSnapshotOfJob(view));
      return;
    }
    const batteryRunId = run.id;
    this.finishSub?.unsubscribe();
    this.finishSub = this.fetchStatus(batteryRunId).subscribe(snapshot => {
      if (this.batteryRun?.id === batteryRunId) {
        this.afterFinish(snapshot);
      }
    });
  }

  private afterFinish(snapshot: ReportStatusSnapshot | null): void {
    if (snapshot) {
      this.setSnapshot(snapshot);
    }
    this.loadDocuments();
    this.emitStatus();
    this.syncPoll();
    this.cdr.markForCheck();
  }

  // --- Viewing ---

  /**
   * Opens a stored document in the PDF viewer with peers named, at the fullest disclosure it allows
   * and with the others offered as versions, which the viewer's info dialog explains.
   */
  viewDocument(doc: BenchmarkReportDocumentListItemDto): void {
    const run = this.batteryRun;
    if (!run) return;
    this.viewError = null;
    const { disclosures, highest, disclosureOf } = reportDocumentDisclosures(doc);
    const paper = rememberedPdfPaper();
    const label = audienceLabel(doc.audience);
    this.pdfViewer?.open({
      title: label,
      subtitle: `Battery run #${run.id} · ${this.documentByline(doc)}`,
      variants: disclosures.map(disclosure => ({ key: reportDisclosureParam(disclosure), label: disclosureLabel(disclosure) })),
      initialVariant: reportDisclosureParam(highest),
      load: variant => this.benchmarkService.getReportDocumentPdf(doc.id, disclosureOf(variant), BenchmarkReportPeerNaming.Named, paper),
      tabUrl: variant => this.benchmarkService.reportDocumentPdfUrl(
        doc.id, disclosureOf(variant), BenchmarkReportPeerNaming.Named, paper, true),
      fallbackFileName: `battery-run-${run.id}_${safeFileName(label)}.pdf`,
      variantsInfo: reportDisclosureInfo(doc.audience)
    });
  }

  /** The progress dialog's View: the document by id, from the list as it now stands. */
  viewDocumentById(documentId: number): void {
    const doc = this.documents.find(d => d.id === documentId);
    if (doc) {
      this.viewDocument(doc);
      return;
    }
    this.loadDocuments(() => {
      const loaded = this.documents.find(d => d.id === documentId);
      if (loaded) {
        this.viewDocument(loaded);
      } else {
        this.viewError = 'The document could not be opened: it no longer exists.';
        this.cdr.markForCheck();
      }
    });
  }

  /**
   * Open Download Center: handed to the host when it listens, else opened here on the battery run,
   * with focus returning to the button when it closes.
   */
  requestDownloads(button: HTMLElement): void {
    if (this.downloadsRequested.observed) {
      this.downloadsRequested.emit(button);
      return;
    }
    const run = this.batteryRun;
    if (!run) return;
    this.downloadsReturnFocus = button;
    this.ownDownloadCenter = true;
    this.cdr.detectChanges();
    const context: DownloadCenterBatteryContext = {
      kind: 'battery',
      batteryRunId: run.id,
      label: this.batteryLabel,
      ...(this.memberDiagnosticsText ? { memberDiagnosticsText: this.memberDiagnosticsText } : {})
    };
    this.downloadCenter?.open(context);
  }

  /** The tab's own Download Center closed: focus goes back to the button that opened it. */
  onDownloadCenterClosed(): void {
    const target = this.downloadsReturnFocus;
    this.downloadsReturnFocus = null;
    if (target?.isConnected) {
      target.focus();
    }
  }

  // --- Deleting ---

  requestDelete(doc: BenchmarkReportDocumentListItemDto): void {
    if (this.jobInProgress || this.deleting) return;
    this.deleteTarget = doc;
    this.deleteError = null;
    this.announcement = '';
    this.cdr.detectChanges();
    const dialog = this.deleteDialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
  }

  cancelDelete(): void {
    this.deleteDialog?.nativeElement.close();
  }

  confirmDelete(): void {
    const run = this.batteryRun;
    const doc = this.deleteTarget;
    if (!run || !doc || this.deleting) return;
    const batteryRunId = run.id;
    const label = audienceLabel(doc.audience);
    this.deleting = true;
    this.deleteError = null;
    this.cdr.markForCheck();
    this.deleteSub?.unsubscribe();
    this.deleteSub = this.benchmarkService.deleteBatteryReportDocument(batteryRunId, doc.id).subscribe({
      next: () => {
        this.deleting = false;
        if (this.batteryRun?.id !== batteryRunId) return;
        this.deleteDialog?.nativeElement.close();
        this.deleteTarget = null;
        this.announcement = `The ${label} was deleted.`;
        this.deselected.delete(doc.audience);
        this.documents = this.documents.filter(d => d.id !== doc.id);
        const writer = this.selectedWriter;
        if (writer && isSameProvider(writer.provider, doc.writerProvider) &&
          (writer.modelId ?? '').trim().toLowerCase() === (doc.writerModelId ?? '').trim().toLowerCase()) {
          this.writerConfigId = null;
          this.writerNote = `Choose a report writer. The deleted document was written by ${doc.writerDisplayName || 'that model'}.`;
        }
        this.queueEstimate();
        this.cdr.markForCheck();
        this.loadDocuments(() => this.focusAudience(doc.audience));
      },
      error: err => {
        this.deleting = false;
        const reason = err?.status === 404 ? 'it no longer exists'
          : err?.status === 0 ? 'the server could not be reached'
            : (reportServerErrorText(err) ?? `HTTP ${err?.status ?? 'error'}`);
        this.deleteError = `The ${label} could not be deleted: ${reason.replace(/\.$/, '')}.`;
        this.cdr.markForCheck();
      }
    });
  }

  /** The id of the write panel's checkbox of an audience. */
  audienceId(audience: BenchmarkReportAudience): string {
    return `${this.idPrefix}Audience${audience}`;
  }

  /** Moves focus to the write panel's checkbox of an audience. */
  private focusAudience(audience: BenchmarkReportAudience): void {
    this.host.nativeElement.querySelector<HTMLInputElement>(`#${this.audienceId(audience)}`)?.focus();
  }

  /** The nested confirmations' own close and cancel events stop here, short of the battery run report dialog. */
  stopNestedEvent(event: Event): void {
    event.stopPropagation();
  }

  // --- Loading and polling ---

  /** The battery run's own writer, when it is one of the picker's configurations. */
  private defaultWriterId(run: BenchmarkBatteryRunDto): number | null {
    const own = run.reportWriterModelConfigurationId ?? null;
    return own != null && this.configs.some(config => config.id === own) ? own : null;
  }

  private snapshotOfBatteryRun(run: BenchmarkBatteryRunDto): ReportStatusSnapshot {
    const writerId = run.reportWriterModelConfigurationId ?? null;
    return {
      status: reportDocumentsStatusOf(run.reportDocumentsStatus),
      message: run.reportDocumentsMessage ?? null,
      writerId,
      writerName: writerId != null ? (this.configs.find(config => config.id === writerId)?.displayName ?? null) : null
    };
  }

  private setSnapshot(snapshot: ReportStatusSnapshot): void {
    this.status = snapshot.status;
    this.statusMessage = snapshot.message;
    this.batteryWriterId = snapshot.writerId;
    this.batteryWriterName = snapshot.writerName;
  }

  private emitStatus(): void {
    this.reportStatusChange.emit({
      status: this.status,
      message: this.statusMessage,
      writerId: this.batteryWriterId,
      writerName: this.batteryWriterName
    });
  }

  /** Lists the battery run's documents; `after` runs once they are rendered. */
  private loadDocuments(after?: () => void): void {
    const run = this.batteryRun;
    if (!run) return;
    const batteryRunId = run.id;
    const subjectKey = `battery:${batteryRunId}`;
    this.documentsSub?.unsubscribe();
    this.documentsSub = this.benchmarkService.listReportDocuments({ subject: subjectKey, origin: 'batteryCompletion' }).subscribe({
      next: documents => {
        if (this.batteryRun?.id !== batteryRunId) return;
        this.documents = completionDocumentsOf(documents, subjectKey, BATTERY_COMPLETION_ORIGIN);
        this.documentsLoaded = true;
        this.documentsError = null;
        this.queueEstimate();
        this.cdr.markForCheck();
        if (after) {
          this.cdr.detectChanges();
          after();
        }
      },
      error: err => {
        if (this.batteryRun?.id !== batteryRunId) return;
        console.warn('Failed to list the battery run\'s AI-written reports', err);
        this.documentsLoaded = true;
        this.documentsError = 'The AI-written reports could not be listed.';
        this.queueEstimate();
        this.cdr.markForCheck();
      }
    });
  }

  /** The job's view of the status while this server process knows the job, else the battery run's own. */
  private fetchStatus(batteryRunId: number): Observable<ReportStatusSnapshot | null> {
    return this.benchmarkService.getBatteryReportJob(batteryRunId).pipe(
      switchMap(job => job
        ? of(reportStatusSnapshotOfJob(job))
        : this.benchmarkService.getBatteryRun(batteryRunId).pipe(map(run => this.snapshotOfBatteryRun(run)))),
      catchError(() => of(null))
    );
  }

  /**
   * Polls the battery run's report status while its reports are queued or written, and only while
   * the report dialog is open. The documents are listed again only when the status changes.
   */
  private syncPoll(): void {
    const run = this.batteryRun;
    if (!run || !this.dialogOpen || !this.jobInProgress) {
      this.stopPoll();
      return;
    }
    if (this.pollSub && this.pollBatteryRunId === run.id) {
      return;
    }
    this.stopPoll();
    const batteryRunId = run.id;
    this.pollBatteryRunId = batteryRunId;
    this.pollSub = timer(BATTERY_REPORT_DOCUMENTS_POLL_MS, BATTERY_REPORT_DOCUMENTS_POLL_MS).pipe(
      // A failed tick is skipped; the next one asks again.
      switchMap(() => this.fetchStatus(batteryRunId))
    ).subscribe(snapshot => {
      if (!snapshot || this.batteryRun?.id !== batteryRunId) return;
      const changed = snapshot.status !== this.status || snapshot.message !== this.statusMessage;
      this.setSnapshot(snapshot);
      if (changed) {
        this.loadDocuments();
        this.emitStatus();
      }
      if (!this.jobInProgress) {
        this.stopPoll();
      }
      this.cdr.markForCheck();
    });
  }

  private stopPoll(): void {
    this.pollSub?.unsubscribe();
    this.pollSub = null;
    this.pollBatteryRunId = null;
  }

  /** Estimates the cost of the checked documents with the chosen writer, once the choice rests and the battery run is writable. */
  private queueEstimate(): void {
    const run = this.batteryRun;
    const writerId = this.writerConfigId;
    const audiences = this.checkedAudiences;
    const request: EstimateRequest | null =
      run && this.documentsMissing && !this.notWritableReason && writerId != null && audiences.length > 0
        ? { key: `${run.id}|${writerId}|${audiences.join(',')}`, batteryRunId: run.id, writerId, audiences }
        : null;
    const key = request?.key ?? null;
    if (key === this.requestedEstimateKey) {
      return;
    }
    this.requestedEstimateKey = key;
    this.estimate = null;
    this.estimateFailed = false;
    this.estimateLoading = request !== null;
    this.estimateRequests.next(request);
  }

  private reset(): void {
    this.stopPoll();
    for (const sub of [this.documentsSub, this.writeSub, this.deleteSub, this.finishSub]) {
      sub?.unsubscribe();
    }
    this.documentsSub = this.writeSub = this.deleteSub = this.finishSub = null;
    this.documents = [];
    this.documentsLoaded = false;
    this.documentsError = null;
    this.writerConfigId = null;
    this.writerNote = '';
    this.writeSubmitting = false;
    this.writeError = null;
    this.viewError = null;
    this.announcement = '';
    this.deselected.clear();
    this.deleteTarget = null;
    this.deleting = false;
    this.deleteError = null;
    this.lastEstimateUsd = null;
    this.setSnapshot({ status: BenchmarkRunReportDocumentsStatus.NotRequested, message: null, writerId: null, writerName: null });
    this.requestedEstimateKey = null;
    this.estimate = null;
    this.estimateFailed = false;
    this.estimateLoading = false;
    this.estimateRequests.next(null);
  }
}
