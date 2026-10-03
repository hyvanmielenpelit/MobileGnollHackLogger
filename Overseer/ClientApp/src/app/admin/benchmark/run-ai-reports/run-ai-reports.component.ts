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
  BenchmarkReportAudience,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportDocumentOrigin,
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
import { rememberedPdfPaper } from '../download-center/benchmark-download-center.component';
import { reportDisclosureInfo } from '../report-disclosure-guide';
import { audienceLabel, disclosureLabel } from '../report-pack/report-document-format';
import {
  REPORT_DOCUMENT_AUDIENCES,
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
} from './report-documents-list';
import {
  ReportWriterCandidate,
  isSameProvider,
  reportWriterRefusal,
  reportWriterWarning,
  reportWriterWarningText
} from './report-writer-policy';
import { RUN_REPORT_WRITER_ADVICE } from './report-writer-advice';
import { RunReportWritingDialogComponent } from './run-report-writing-dialog.component';

export { reportDocumentsStatusOf } from './report-documents-list';

/** The interval of the tab's status poll while a finished run's reports are queued or written. */
export const RUN_REPORT_DOCUMENTS_POLL_MS = 5000;

/** The pause after the last change of writer or documents before the cost is estimated. */
export const RUN_REPORT_ESTIMATE_DEBOUNCE_MS = 300;

/** The two AI-written documents of a run, in the order the tab lists them. */
export const RUN_REPORT_AUDIENCES: readonly BenchmarkReportAudience[] = REPORT_DOCUMENT_AUDIENCES;

/** The run's report fields as the tab last read them, for the host to keep its copy of the run current. */
export interface RunReportStatusChange {
  status: BenchmarkRunReportDocumentsStatus;
  message: string | null;
  writerId: number | null;
  writerName: string | null;
}

/** One row of the document list: an audience and its stored document, if any. */
export type RunAiReportRow = ReportDocumentRow;

/** How the status line is drawn. */
export type RunAiReportStatusKind = ReportDocumentsStatusKind;

/**
 * The cost estimate block: waiting for the estimate, failed, no price card for the writer, or the
 * total with, for two documents, the cost of each.
 */
export type RunReportEstimateView = ReportEstimateView;

interface EstimateRequest {
  key: string;
  runId: number;
  writerId: number;
  audiences: BenchmarkReportAudience[];
}

interface EstimateResult {
  request: EstimateRequest;
  estimate: BenchmarkRunReportEstimateDto | null;
  failed: boolean;
}

/** The run's report status, from the job while the server knows it, else from the run. */
type StatusSnapshot = ReportStatusSnapshot;

/** A run's status by name, from its number or its name. */
function runStatusName(status: string | number | null | undefined): string {
  switch (status) {
    case 1: case 'Running': return 'Running';
    case 2: case 'Completed': return 'Completed';
    case 3: case 'CompletedWithErrors': return 'CompletedWithErrors';
    case 4: case 'Failed': return 'Failed';
    case 5: case 'Canceled': return 'Canceled';
    case 6: case 'CompletedWithLimits': return 'CompletedWithLimits';
    default: return String(status ?? '');
  }
}

function snapshotOfRun(run: BenchmarkRunDetailDto): StatusSnapshot {
  return {
    status: reportDocumentsStatusOf(run.reportDocumentsStatus),
    message: run.reportDocumentsMessage ?? null,
    writerId: run.reportWriterModelConfigurationId ?? null,
    writerName: run.reportWriterDisplayName ?? null
  };
}

/** The list's run-completion documents of this run, the newest one per audience, in audience order. */
function runCompletionDocumentsOf(
  documents: readonly BenchmarkReportDocumentListItemDto[] | null | undefined,
  runId: number
): BenchmarkReportDocumentListItemDto[] {
  return completionDocumentsOf(documents, `run:${runId}`, BenchmarkReportDocumentOrigin.RunCompletion);
}

/**
 * The run report's AI Reports tab: the job state, one row per run-completion document (written or
 * not) with View and Delete, a pointer to the Download Center, and, while a finished run lacks a
 * document, the documents to write, the report writer, the cost estimate and Write Reports.
 *
 * It owns its own polling, estimate and dialogs: the progress dialog, the PDF viewer and two
 * confirmations. The confirmations sit inside the run report dialog, so their close and cancel
 * events stop here.
 */
@Component({
  selector: 'app-run-ai-reports',
  standalone: true,
  imports: [ModelPickerComponent, InfoTipComponent, RunReportWritingDialogComponent, PdfViewerDialogComponent],
  templateUrl: './run-ai-reports.component.html',
  styleUrls: ['./run-ai-reports.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RunAiReportsComponent implements OnInit, OnChanges, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

  /** The run the report dialog shows. */
  @Input() run: BenchmarkRunDetailDto | null = null;
  /** The report writer picker's options. */
  @Input() pickerOptions: readonly ModelPickerOption<SystemAiConfigDto>[] = [];
  /** The picker's text when there are no options. */
  @Input() pickerEmptyHint: string | null = null;
  /** The configurations the options stand for, with a stable identity. */
  @Input() writerConfigs: readonly SystemAiConfigDto[] = [];
  /** The launcher's Report Writer, the fallback for the writer the tab starts with. */
  @Input() launcherWriterConfigId: number | null = null;
  /** The report dialog is open; the tab polls only then. */
  @Input() dialogOpen = false;
  /** The tab is the one shown; the cost estimate is requested only then. */
  @Input() active = false;

  /** The button that asked for the Download Center, for the host to return focus to. */
  @Output() readonly downloadsRequested = new EventEmitter<HTMLElement>();
  /** The run's report status, writer and message changed. */
  @Output() readonly reportStatusChange = new EventEmitter<RunReportStatusChange>();

  @ViewChild(RunReportWritingDialogComponent) writingDialog?: RunReportWritingDialogComponent;
  @ViewChild(PdfViewerDialogComponent) pdfViewer?: PdfViewerDialogComponent;
  @ViewChild('deleteDialog') deleteDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('sameProviderDialog') sameProviderDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('writeButton') writeButton?: ElementRef<HTMLButtonElement>;

  /** The *Choosing a report writer* tip's entries. */
  readonly writerAdvice = RUN_REPORT_WRITER_ADVICE;

  /**
   * The run's AI-written (run-completion) documents, one per audience, from the document list.
   * Whether they exist is read here and never from the run's status, which a delete does not reset.
   */
  documents: BenchmarkReportDocumentListItemDto[] = [];
  /** The document list of the run has answered. */
  documentsLoaded = false;
  documentsError: string | null = null;

  /** The writer Write Reports sends; see {@link defaultWriterId} for where it starts. */
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
  /** The run's report writer as the server last named it. */
  runWriterId: number | null = null;
  runWriterName: string | null = null;

  estimate: BenchmarkRunReportEstimateDto | null = null;
  estimateLoading = false;
  estimateFailed = false;

  /** The document the delete confirmation is about. */
  deleteTarget: BenchmarkReportDocumentListItemDto | null = null;
  deleting = false;
  deleteError: string | null = null;

  /** The same-provider confirmation's sentence. */
  confirmWarningText = '';

  /** Missing documents unchecked in the write panel; every other missing one is checked. */
  private readonly deselected = new Set<BenchmarkReportAudience>();
  private readonly estimateRequests = new Subject<EstimateRequest | null>();
  private readonly estimateSub: Subscription;
  private requestedEstimateKey: string | null = null;
  /** The estimate total when the last write was requested, for Show Progress. */
  private lastEstimateUsd: number | null = null;

  private documentsSub: Subscription | null = null;
  private pollSub: Subscription | null = null;
  private pollRunId: number | null = null;
  private writeSub: Subscription | null = null;
  private deleteSub: Subscription | null = null;
  private finishSub: Subscription | null = null;

  constructor() {
    this.estimateSub = this.estimateRequests.pipe(
      debounceTime(RUN_REPORT_ESTIMATE_DEBOUNCE_MS),
      switchMap((request): Observable<EstimateResult | null> => request === null
        ? of(null)
        : this.benchmarkService.estimateRunReports(request.runId, {
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
    const runChange = changes['run'];
    if (runChange) {
      const previous = runChange.previousValue as BenchmarkRunDetailDto | null | undefined;
      const run = this.run;
      if (!run) {
        this.reset();
      } else if (!previous || previous.id !== run.id) {
        this.reset();
        this.setSnapshot(snapshotOfRun(run));
        this.writerConfigId = this.defaultWriterId(run);
        this.loadDocuments();
      } else {
        this.setSnapshot(snapshotOfRun(run));
        // A run that has just finished may have had its reports written as it was scored.
        if (runStatusName(previous.status) === 'Running' && runStatusName(run.status) !== 'Running') {
          this.loadDocuments();
        }
      }
    }
    if (runChange || changes['dialogOpen']) {
      this.syncPoll();
    }
    if (changes['active'] && this.active) {
      this.queueEstimate();
    }
  }

  ngOnDestroy(): void {
    this.reset();
    this.estimateSub.unsubscribe();
  }

  // --- What is shown ---

  /** A job for the run's documents is queued or writing. */
  get jobInProgress(): boolean {
    return reportJobInProgress(this.status);
  }

  /** The run finished with a status its documents can be written for. */
  get runFinished(): boolean {
    const status = runStatusName(this.run?.status);
    return status === 'Completed' || status === 'CompletedWithErrors' || status === 'CompletedWithLimits';
  }

  /** The job state, or '' when the document rows already say everything. */
  get statusText(): string {
    const text = reportJobStatusText(this.status, this.statusMessage);
    if (text) {
      return text;
    }
    if (this.status === BenchmarkRunReportDocumentsStatus.NotRequested && this.runWriterId != null &&
      runStatusName(this.run?.status) === 'Running') {
      return `Not written yet: ${this.runWriterName || 'the report writer'} writes them once the run is scored`;
    }
    return '';
  }

  get statusKind(): RunAiReportStatusKind {
    return reportJobStatusKind(this.status);
  }

  documentFor(audience: BenchmarkReportAudience): BenchmarkReportDocumentListItemDto | undefined {
    return this.documents.find(doc => doc.audience === audience);
  }

  /** One row per run-completion audience, in the order the tab lists them, with its stored document if any. */
  get documentRows(): RunAiReportRow[] {
    return reportDocumentRows(this.documents);
  }

  /** At least one document is written, so the Download Center has something of this tab's to offer. */
  get hasWrittenDocument(): boolean {
    return this.documents.length > 0;
  }

  /** The documents the run lacks, in list order. */
  get missingAudiences(): BenchmarkReportAudience[] {
    return missingReportAudiences(this.documents);
  }

  /** The written documents, whose names the write panel lists as not writable. */
  get writtenLabels(): string[] {
    return writtenReportLabels(this.documents);
  }

  /** A finished run has fewer than both documents, so the tab offers Write Reports. */
  get documentsMissing(): boolean {
    return !!this.run && this.documentsLoaded && this.documentsError === null && this.runFinished &&
      this.missingAudiences.length > 0;
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
    return this.writerConfigs.find(config => config.id === this.writerConfigId);
  }

  private get candidate(): ReportWriterCandidate | null {
    const run = this.run;
    return run ? { provider: run.testedModelProviderUsed, modelId: run.testedModelIdUsed } : null;
  }

  /** Why Write Reports is refused for the chosen writer and this run, or '' when it is not. */
  get writeRefusal(): string {
    const run = this.run;
    if (!run) return '';
    if (!run.assessmentJson) {
      return 'The run has no final synthesis to write about. Re-run the final synthesis first.';
    }
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

  /**
   * The picker's description: the refusal or the warning while there is one. The report writer
   * info is a dialog, too long to be read out as a description.
   */
  get writerDescribedBy(): string | null {
    const ids = [this.writeRefusal ? 'rrReportWriterBlocked' : '', this.writerWarning ? 'rrReportWriterWarning' : '']
      .filter(id => id !== '');
    return ids.length > 0 ? ids.join(' ') : null;
  }

  get writeDisabled(): boolean {
    return this.writerConfigId == null || this.checkedAudiences.length === 0 || !!this.writeRefusal ||
      this.jobInProgress || this.writeSubmitting;
  }

  /** The cost estimate block under the write row, or null when there is nothing to say. */
  get estimateView(): RunReportEstimateView | null {
    return reportEstimateView(this.estimateLoading, this.estimateFailed, this.estimate);
  }

  documentStatusLabel(doc: BenchmarkReportDocumentListItemDto): string {
    return reportDocumentTag(doc);
  }

  documentWriter(doc: BenchmarkReportDocumentListItemDto): string {
    return reportDocumentWriter(doc, this.runWriterName);
  }

  /** When a document was written, in UTC. */
  documentDate(doc: BenchmarkReportDocumentListItemDto): string {
    return reportDateUtc(doc.createdAtUtc);
  }

  /** A written document's writer and date. */
  documentByline(doc: BenchmarkReportDocumentListItemDto): string {
    return reportDocumentByline(doc, this.runWriterName);
  }

  /** The writer and date, then the stored duration and cost where known. */
  documentMeta(doc: BenchmarkReportDocumentListItemDto): string {
    return reportDocumentMeta(doc, this.runWriterName);
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
    const run = this.run;
    const writerId = this.writerConfigId;
    if (!run || writerId == null || this.writeSubmitting) return;
    const runId = run.id;
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
    this.writeSub = this.benchmarkService.writeRunReportDocuments(runId, request).subscribe({
      next: response => {
        this.writeSubmitting = false;
        if (this.run?.id !== runId) return;
        const writer = this.writerConfigs.find(config => config.id === writerId);
        this.setSnapshot({
          status: reportDocumentsStatusOf(response?.status ?? BenchmarkRunReportDocumentsStatus.Pending),
          message: null,
          writerId,
          writerName: writer?.displayName ?? this.runWriterName
        });
        this.lastEstimateUsd = estimateUsd;
        this.emitStatus();
        this.syncPoll();
        this.cdr.markForCheck();
        this.openProgress(estimateUsd);
      },
      error: err => {
        this.writeSubmitting = false;
        if (this.run?.id !== runId) return;
        // The selection changed under the client's check: the server asks for the acknowledgment.
        if (err?.status === 409 && !acknowledgeSameProvider && isReportWriterSameProviderWarning(err.error)) {
          this.cdr.markForCheck();
          this.openSameProviderConfirm(reportWriterWarningText(err.error.assessorModelDisplayName, err.error.provider));
          return;
        }
        this.writeError = RunAiReportsComponent.writeErrorOf(err, runId);
        // A 409 means the list or the status moved on elsewhere; show where they stand now.
        if (err?.status === 409) {
          this.loadDocuments();
        }
        this.cdr.markForCheck();
      }
    });
  }

  private static writeErrorOf(err: any, runId: number): string {
    if (err?.status === 404) return `Run #${runId} no longer exists.`;
    if (err?.status === 0) return 'The server could not be reached.';
    return reportServerErrorText(err) ?? `The reports could not be requested (HTTP ${err?.status ?? 'error'}).`;
  }

  /** Show Progress: the progress dialog of the job in hand. Closing it never cancels the job. */
  showProgress(): void {
    this.openProgress(this.lastEstimateUsd);
  }

  private openProgress(estimateUsd: number | null): void {
    const run = this.run;
    if (!run) return;
    this.writingDialog?.open({
      runId: run.id,
      runLabel: `${run.suiteName} · ${run.testedModelDisplayNameUsed}`,
      estimateUsd,
      run: {
        runId: run.id,
        suiteName: run.suiteName,
        candidateLabel: run.testedModelDisplayNameUsed,
        provider: run.testedModelProviderUsed,
        modelId: run.testedModelIdUsed,
        reportDocumentsStatus: this.status,
        reportDocumentsMessage: this.statusMessage
      }
    });
  }

  /** The progress dialog's job finished: the documents and the status are read again. */
  onWritingFinished(view: BenchmarkRunReportJobDto | null): void {
    const run = this.run;
    if (!run) return;
    if (view && view.runId === run.id) {
      this.afterFinish(reportStatusSnapshotOfJob(view));
      return;
    }
    const runId = run.id;
    this.finishSub?.unsubscribe();
    this.finishSub = this.fetchStatus(runId).subscribe(snapshot => {
      if (this.run?.id === runId) {
        this.afterFinish(snapshot);
      }
    });
  }

  private afterFinish(snapshot: StatusSnapshot | null): void {
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
    const run = this.run;
    if (!run) return;
    this.viewError = null;
    const { disclosures, highest, disclosureOf } = reportDocumentDisclosures(doc);
    const paper = rememberedPdfPaper();
    const label = audienceLabel(doc.audience);
    this.pdfViewer?.open({
      title: label,
      subtitle: `Run #${run.id} · ${this.documentByline(doc)}`,
      variants: disclosures.map(disclosure => ({ key: reportDisclosureParam(disclosure), label: disclosureLabel(disclosure) })),
      initialVariant: reportDisclosureParam(highest),
      load: variant => this.benchmarkService.getReportDocumentPdf(doc.id, disclosureOf(variant), BenchmarkReportPeerNaming.Named, paper),
      tabUrl: variant => this.benchmarkService.reportDocumentPdfUrl(
        doc.id, disclosureOf(variant), BenchmarkReportPeerNaming.Named, paper, true),
      fallbackFileName: `run-${run.id}_${safeFileName(label)}.pdf`,
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

  requestDownloads(button: HTMLElement): void {
    this.downloadsRequested.emit(button);
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
    const run = this.run;
    const doc = this.deleteTarget;
    if (!run || !doc || this.deleting) return;
    const runId = run.id;
    const label = audienceLabel(doc.audience);
    this.deleting = true;
    this.deleteError = null;
    this.cdr.markForCheck();
    this.deleteSub?.unsubscribe();
    this.deleteSub = this.benchmarkService.deleteRunReportDocument(runId, doc.id).subscribe({
      next: () => {
        this.deleting = false;
        if (this.run?.id !== runId) return;
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

  /** Moves focus to the write panel's checkbox of an audience. */
  private focusAudience(audience: BenchmarkReportAudience): void {
    this.host.nativeElement.querySelector<HTMLInputElement>(`#rrAudience${audience}`)?.focus();
  }

  /** The nested confirmations' own close and cancel events stop here, short of the run report dialog. */
  stopNestedEvent(event: Event): void {
    event.stopPropagation();
  }

  // --- Loading and polling ---

  private defaultWriterId(run: BenchmarkRunDetailDto): number | null {
    const own = run.reportWriterModelConfigurationId ?? null;
    if (own != null && this.writerConfigs.some(config => config.id === own)) {
      return own;
    }
    const launcher = this.writerConfigs.find(config => config.id === this.launcherWriterConfigId);
    const candidate = { provider: run.testedModelProviderUsed, modelId: run.testedModelIdUsed };
    // The launcher's writer starts the picker only when it needs neither a refusal nor a warning.
    return launcher && !reportWriterRefusal(launcher, candidate) && !reportWriterWarning(launcher, candidate)
      ? launcher.id
      : null;
  }

  private setSnapshot(snapshot: StatusSnapshot): void {
    this.status = snapshot.status;
    this.statusMessage = snapshot.message;
    this.runWriterId = snapshot.writerId;
    this.runWriterName = snapshot.writerName;
  }

  private emitStatus(): void {
    this.reportStatusChange.emit({
      status: this.status,
      message: this.statusMessage,
      writerId: this.runWriterId,
      writerName: this.runWriterName
    });
  }

  /** Lists the run's documents; `after` runs once they are rendered. */
  private loadDocuments(after?: () => void): void {
    const run = this.run;
    if (!run) return;
    const runId = run.id;
    this.documentsSub?.unsubscribe();
    this.documentsSub = this.benchmarkService.listReportDocuments({ runId }).subscribe({
      next: documents => {
        if (this.run?.id !== runId) return;
        this.documents = runCompletionDocumentsOf(documents, runId);
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
        if (this.run?.id !== runId) return;
        console.warn('Failed to list the run\'s AI-written reports', err);
        this.documentsLoaded = true;
        this.documentsError = 'The AI-written reports could not be listed.';
        this.queueEstimate();
        this.cdr.markForCheck();
      }
    });
  }

  /** The job's view of the status while this server process knows the job, else the run's own. */
  private fetchStatus(runId: number): Observable<StatusSnapshot | null> {
    return this.benchmarkService.getRunReportJob(runId).pipe(
      switchMap(job => job ? of(reportStatusSnapshotOfJob(job)) :this.benchmarkService.getRun(runId).pipe(map(snapshotOfRun))),
      catchError(() => of(null))
    );
  }

  /**
   * Polls a finished run's report status while its reports are queued or written, and only while the
   * report dialog is open. A running run is followed by the report dialog's own detail poll. The
   * documents are listed again only when the status changes.
   */
  private syncPoll(): void {
    const run = this.run;
    if (!run || !this.dialogOpen || runStatusName(run.status) === 'Running' || !this.jobInProgress) {
      this.stopPoll();
      return;
    }
    if (this.pollSub && this.pollRunId === run.id) {
      return;
    }
    this.stopPoll();
    const runId = run.id;
    this.pollRunId = runId;
    this.pollSub = timer(RUN_REPORT_DOCUMENTS_POLL_MS, RUN_REPORT_DOCUMENTS_POLL_MS).pipe(
      // A failed tick is skipped; the next one asks again.
      switchMap(() => this.fetchStatus(runId))
    ).subscribe(snapshot => {
      if (!snapshot || this.run?.id !== runId) return;
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
    this.pollRunId = null;
  }

  /**
   * Estimates the cost of the checked documents with the chosen writer, once the choice rests. While the
   * tab is not shown nothing is sent; selecting the tab queues the estimate its choice needs then.
   */
  private queueEstimate(): void {
    const run = this.run;
    const writerId = this.writerConfigId;
    const audiences = this.checkedAudiences;
    const request: EstimateRequest | null = run && this.documentsMissing && writerId != null && audiences.length > 0
      ? { key: `${run.id}|${writerId}|${audiences.join(',')}`, runId: run.id, writerId, audiences }
      : null;
    const key = request?.key ?? null;
    if (key === this.requestedEstimateKey) {
      return;
    }
    if (request !== null && !this.active) {
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
