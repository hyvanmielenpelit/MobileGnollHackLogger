import {
  ChangeDetectorRef,
  Directive,
  ElementRef,
  EventEmitter,
  OnDestroy,
  OnInit,
  ViewChild,
  inject
} from '@angular/core';
import { Observable, Subject, Subscription, catchError, debounceTime, map, of, switchMap, timer } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkReportAudience,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPeerNaming,
  BenchmarkRunReportDocumentsStatus,
  BenchmarkRunReportEstimateDto,
  BenchmarkRunReportEstimateRequest,
  BenchmarkRunReportJobDto,
  WriteRunReportDocumentsRequest,
  WriteRunReportDocumentsResponse,
  reportDisclosureParam
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import { ModelPickerOption } from '../../../shared/model-picker/model-picker.component';
import { PdfViewerDialogComponent } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { safeFileName } from '../../../utils/download.util';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { rememberedPdfPaper } from '../download-center/benchmark-download-center.component';
import { reportDisclosureInfo } from '../report-disclosure-guide';
import { audienceLabel, disclosureLabel } from '../report-pack/report-document-format';
import {
  ReportDocumentRow,
  ReportDocumentsStatusKind,
  ReportEstimateView,
  ReportStatusSnapshot,
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
import { ReportWriterAdviceEntry } from './report-writer-advice';
import {
  ReportWriterCandidate,
  isSameProvider,
  reportWriterRefusal,
  reportWriterWarning,
  reportWriterWarningText
} from './report-writer-policy';
import { RunReportWritingContext, RunReportWritingDialogComponent } from './run-report-writing-dialog.component';

/** The interval of the tab's status poll while the subject's reports are queued or written. */
export const REPORT_DOCUMENTS_POLL_MS = 5000;

/** The pause after the last change of writer or documents before the cost is estimated. */
export const REPORT_ESTIMATE_DEBOUNCE_MS = 300;

/** The endpoints of one kind of subject's AI-written reports, each taking the subject's id. */
export interface ReportDocumentsApi<TSubject> {
  estimate(subjectId: number, request: BenchmarkRunReportEstimateRequest): Observable<BenchmarkRunReportEstimateDto>;
  write(subjectId: number, request: WriteRunReportDocumentsRequest): Observable<WriteRunReportDocumentsResponse>;
  delete(subjectId: number, documentId: number): Observable<void>;
  getJob(subjectId: number): Observable<BenchmarkRunReportJobDto | null>;
  /** The subject as stored, read when the server knows no job for it. */
  getStored(subjectId: number): Observable<TSubject>;
  /** The subject's completion documents, the newest one per audience, in audience order. */
  listDocuments(subjectId: number): Observable<BenchmarkReportDocumentListItemDto[]>;
}

interface EstimateRequest {
  key: string;
  subjectId: number;
  writerId: number;
  audiences: BenchmarkReportAudience[];
}

interface EstimateResult {
  request: EstimateRequest;
  estimate: BenchmarkRunReportEstimateDto | null;
  failed: boolean;
}

/**
 * The AI Reports tab of a report dialog, for a run (`app-run-ai-reports`) or a battery run
 * (`app-battery-ai-reports`): the job state, one row per completion document (written or not) with
 * View and Delete, a pointer to the Download Center, and, while a document is missing, the documents
 * to write, the report writer, the cost estimate and Write Reports. Both components render
 * `run-ai-reports.component.html`; what differs between their subjects is in the abstract members.
 *
 * It owns its own polling, estimate and dialogs: the progress dialog, the PDF viewer and two
 * confirmations. The confirmations sit inside the host's report dialog, so their close and cancel
 * events stop here.
 */
@Directive()
export abstract class ReportDocumentsTabBase<TSubject extends { id: number }> implements OnInit, OnDestroy {
  protected readonly benchmarkService = inject(AdminBenchmarkService);
  protected readonly cdr = inject(ChangeDetectorRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

  /** The report dialog is open; the tab polls only then. */
  abstract dialogOpen: boolean;
  /** The button that asked for the Download Center, for the host to return focus to. */
  abstract readonly downloadsRequested: EventEmitter<HTMLElement>;
  /** The subject's report status, writer and message changed. */
  abstract readonly reportStatusChange: EventEmitter<ReportStatusSnapshot>;

  @ViewChild(RunReportWritingDialogComponent) writingDialog?: RunReportWritingDialogComponent;
  @ViewChild(PdfViewerDialogComponent) pdfViewer?: PdfViewerDialogComponent;
  @ViewChild('deleteDialog') deleteDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('sameProviderDialog') sameProviderDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('writeButton') writeButton?: ElementRef<HTMLButtonElement>;

  /** The prefix of the element ids; see the id getters for the ones a subclass words differently. */
  abstract readonly idPrefix: string;
  /** The *Choosing a report writer* tip's entries. */
  abstract readonly writerAdvice: readonly ReportWriterAdviceEntry[];
  /** Classes of the tab's root besides `rr-ai-reports`. */
  abstract readonly rootClass: string;
  /** The report writer picker's class. */
  abstract readonly writerPickerClass: string;
  /** The tag of a document whose subject changed after it was written. */
  abstract readonly changedSinceText: string;
  /** The Downloads notice's sentence. */
  abstract readonly downloadsNoticeText: string;

  /**
   * The subject's AI-written (completion) documents, one per audience, from the document list.
   * Whether they exist is read here and never from the subject's status, which a delete does not reset.
   */
  documents: BenchmarkReportDocumentListItemDto[] = [];
  /** The document list of the subject has answered. */
  documentsLoaded = false;
  documentsError: string | null = null;

  /** The writer Write Reports sends; see `defaultWriterId` for where it starts. */
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
  /** The subject's report writer as the server last named it. */
  subjectWriterId: number | null = null;
  subjectWriterName: string | null = null;

  estimate: BenchmarkRunReportEstimateDto | null = null;
  estimateLoading = false;
  estimateFailed = false;

  /** The document the delete confirmation is about. */
  deleteTarget: BenchmarkReportDocumentListItemDto | null = null;
  deleting = false;
  deleteError: string | null = null;

  /** The same-provider confirmation's sentence. */
  confirmWarningText = '';

  /** The tab's own Download Center is rendered, for a subclass that opens one when no host answers. */
  ownDownloadCenter = false;
  /** Where focus returns when the tab's own Download Center closes. */
  protected downloadsReturnFocus: HTMLElement | null = null;

  /** Missing documents unchecked in the write panel; every other missing one is checked. */
  private readonly deselected = new Set<BenchmarkReportAudience>();
  private readonly estimateRequests = new Subject<EstimateRequest | null>();
  private readonly estimateSub: Subscription;
  private requestedEstimateKey: string | null = null;
  /** The estimate total when the last write was requested, for Show Progress. */
  private lastEstimateUsd: number | null = null;

  private documentsSub: Subscription | null = null;
  private pollSub: Subscription | null = null;
  private pollSubjectId: number | null = null;
  private writeSub: Subscription | null = null;
  private deleteSub: Subscription | null = null;
  private finishSub: Subscription | null = null;

  constructor() {
    this.estimateSub = this.estimateRequests.pipe(
      debounceTime(REPORT_ESTIMATE_DEBOUNCE_MS),
      switchMap((request): Observable<EstimateResult | null> => request === null
        ? of(null)
        : this.api.estimate(request.subjectId, {
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

  ngOnDestroy(): void {
    this.reset();
    this.estimateSub.unsubscribe();
  }

  // --- What differs between subjects ---

  /** The run or battery run the tab shows. */
  protected abstract get subject(): TSubject | null;
  /** The report writer picker's options. */
  abstract get options(): readonly ModelPickerOption<SystemAiConfigDto>[];
  /** The configurations the options stand for. */
  abstract get configs(): readonly SystemAiConfigDto[];
  /** The picker's text when there are no options. */
  abstract get emptyHint(): string | null;
  /** The model under test, which a writer may not be and should not share a provider with. */
  protected abstract get candidate(): ReportWriterCandidate | null;
  /** The write panel is offered while a document is missing. */
  protected abstract get offersWritePanel(): boolean;
  /** Why the subject itself refuses Write Reports whatever the writer, or ''. */
  protected abstract get subjectRefusal(): string;
  /** Why the subject's documents cannot be written yet, shown in the write panel, or '' when they can. */
  abstract get notWritableReason(): string;
  /** A cost estimate may be requested now. */
  protected abstract get estimateAllowed(): boolean;
  /** The subject's report status is followed elsewhere, so the tab does not poll it. */
  protected abstract get pollSuppressed(): boolean;
  /** The subject's endpoints. */
  protected abstract readonly api: ReportDocumentsApi<TSubject>;
  /** The subject's noun in a console message: *run*, *battery run*. */
  protected abstract readonly subjectNoun: string;
  /** The subject by name and id: *Run #42*, *Battery run #7*. */
  protected abstract subjectName(subjectId: number): string;
  /** The stem of a file about the subject: `run-42`, `battery-run-7`. */
  protected abstract fileStem(subjectId: number): string;
  /** The writer the tab starts with on a newly shown subject. */
  protected abstract defaultWriterId(subject: TSubject): number | null;
  /** The subject's report status as stored with it. */
  protected abstract snapshotOf(subject: TSubject): ReportStatusSnapshot;
  /** What the progress dialog is opened on. */
  protected abstract progressRequest(subject: TSubject, estimateUsd: number | null): RunReportWritingContext;
  /** The status line before a writer named with the subject has written anything, or '' when it does not apply. */
  protected abstract notWrittenYetText(writerName: string): string;

  // --- Element ids ---

  get labelId(): string {
    return `${this.idPrefix}ReportWriterModelLabel`;
  }

  get hintId(): string {
    return `${this.idPrefix}ReportWriterHint`;
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

  get deleteTitleId(): string {
    return `${this.idPrefix}DeleteTitle`;
  }

  get sameProviderTitleId(): string {
    return `${this.idPrefix}SameProviderTitle`;
  }

  /** The start of a delete button's tooltip id, which ends in the audience. */
  get deleteTipIdPrefix(): string {
    return `${this.idPrefix}-tip-delete-`;
  }

  /** The progress dialog's id prefix. */
  get writingDialogIdPrefix(): string {
    return `${this.idPrefix}Rw`;
  }

  /** The id of the write panel's checkbox of an audience. */
  audienceId(audience: BenchmarkReportAudience): string {
    return `${this.idPrefix}Audience${audience}`;
  }

  // --- What is shown ---

  /** A job for the subject's documents is queued or writing. */
  get jobInProgress(): boolean {
    return reportJobInProgress(this.status);
  }

  /** The job state, or '' when the document rows already say everything. */
  get statusText(): string {
    const text = reportJobStatusText(this.status, this.statusMessage);
    if (text) {
      return text;
    }
    if (this.status === BenchmarkRunReportDocumentsStatus.NotRequested && this.subjectWriterId != null) {
      return this.notWrittenYetText(this.subjectWriterName || 'the report writer');
    }
    return '';
  }

  get statusKind(): ReportDocumentsStatusKind {
    return reportJobStatusKind(this.status);
  }

  documentFor(audience: BenchmarkReportAudience): BenchmarkReportDocumentListItemDto | undefined {
    return this.documents.find(doc => doc.audience === audience);
  }

  /** One row per completion audience, in the order the tab lists them, with its stored document if any. */
  get documentRows(): ReportDocumentRow[] {
    return reportDocumentRows(this.documents);
  }

  /** At least one document is written, so the Download Center has something of this tab's to offer. */
  get hasWrittenDocument(): boolean {
    return this.documents.length > 0;
  }

  /** The documents the subject lacks, in list order. */
  get missingAudiences(): BenchmarkReportAudience[] {
    return missingReportAudiences(this.documents);
  }

  /** The written documents, whose names the write panel lists as not writable. */
  get writtenLabels(): string[] {
    return writtenReportLabels(this.documents);
  }

  /** The subject lacks a document, so the tab offers the write panel, with its reason while it cannot write. */
  get documentsMissing(): boolean {
    return !!this.subject && this.documentsLoaded && this.documentsError === null && this.offersWritePanel &&
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
    return this.configs.find(config => config.id === this.writerConfigId);
  }

  /** Why Write Reports is refused for the chosen writer and this subject, or '' when it is not. */
  get writeRefusal(): string {
    if (!this.subject || this.notWritableReason) return '';
    const own = this.subjectRefusal;
    if (own) return own;
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
    const ids = [this.writeRefusal ? this.blockedId : '', this.writerWarning ? this.warningId : '']
      .filter(id => id !== '');
    return ids.length > 0 ? ids.join(' ') : null;
  }

  /** The Write button's description: why the subject cannot be written yet, then the estimate. */
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
    return reportDocumentWriter(doc, this.subjectWriterName);
  }

  /** When a document was written, in UTC. */
  documentDate(doc: BenchmarkReportDocumentListItemDto): string {
    return reportDateUtc(doc.createdAtUtc);
  }

  /** A written document's writer and date. */
  documentByline(doc: BenchmarkReportDocumentListItemDto): string {
    return reportDocumentByline(doc, this.subjectWriterName);
  }

  /** The writer and date, then the stored duration and cost where known. */
  documentMeta(doc: BenchmarkReportDocumentListItemDto): string {
    return reportDocumentMeta(doc, this.subjectWriterName);
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
    const subject = this.subject;
    const writerId = this.writerConfigId;
    if (!subject || writerId == null || this.writeSubmitting) return;
    const subjectId = subject.id;
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
    this.writeSub = this.api.write(subjectId, request).subscribe({
      next: response => {
        this.writeSubmitting = false;
        if (this.subject?.id !== subjectId) return;
        const writer = this.configs.find(config => config.id === writerId);
        this.setSnapshot({
          status: reportDocumentsStatusOf(response?.status ?? BenchmarkRunReportDocumentsStatus.Pending),
          message: null,
          writerId,
          writerName: writer?.displayName ?? this.subjectWriterName
        });
        this.lastEstimateUsd = estimateUsd;
        this.emitStatus();
        this.syncPoll();
        this.cdr.markForCheck();
        this.openProgress(estimateUsd);
      },
      error: err => {
        this.writeSubmitting = false;
        if (this.subject?.id !== subjectId) return;
        // The selection changed under the client's check: the server asks for the acknowledgment.
        if (err?.status === 409 && !acknowledgeSameProvider && isReportWriterSameProviderWarning(err.error)) {
          this.cdr.markForCheck();
          this.openSameProviderConfirm(reportWriterWarningText(err.error.assessorModelDisplayName, err.error.provider));
          return;
        }
        this.writeError = this.writeErrorOf(err, subjectId);
        // A 409 means the list or the status moved on elsewhere; show where they stand now.
        if (err?.status === 409) {
          this.loadDocuments();
        }
        this.cdr.markForCheck();
      }
    });
  }

  private writeErrorOf(err: any, subjectId: number): string {
    if (err?.status === 404) return `${this.subjectName(subjectId)} no longer exists.`;
    if (err?.status === 0) return 'The server could not be reached.';
    return reportServerErrorText(err) ?? `The reports could not be requested (HTTP ${err?.status ?? 'error'}).`;
  }

  /** Show Progress: the progress dialog of the job in hand. Closing it never cancels the job. */
  showProgress(): void {
    this.openProgress(this.lastEstimateUsd);
  }

  private openProgress(estimateUsd: number | null): void {
    const subject = this.subject;
    if (!subject) return;
    this.writingDialog?.open(this.progressRequest(subject, estimateUsd));
  }

  /** The progress dialog's job finished: the documents and the status are read again. */
  onWritingFinished(view: BenchmarkRunReportJobDto | null): void {
    const subject = this.subject;
    if (!subject) return;
    if (view && view.runId === subject.id) {
      this.afterFinish(reportStatusSnapshotOfJob(view));
      return;
    }
    const subjectId = subject.id;
    this.finishSub?.unsubscribe();
    this.finishSub = this.fetchStatus(subjectId).subscribe(snapshot => {
      if (this.subject?.id === subjectId) {
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
    const subject = this.subject;
    if (!subject) return;
    this.viewError = null;
    const { disclosures, highest, disclosureOf } = reportDocumentDisclosures(doc);
    const paper = rememberedPdfPaper();
    const label = audienceLabel(doc.audience);
    this.pdfViewer?.open({
      title: label,
      subtitle: `${this.subjectName(subject.id)} · ${this.documentByline(doc)}`,
      variants: disclosures.map(disclosure => ({ key: reportDisclosureParam(disclosure), label: disclosureLabel(disclosure) })),
      initialVariant: reportDisclosureParam(highest),
      load: variant => this.benchmarkService.getReportDocumentPdf(doc.id, disclosureOf(variant), BenchmarkReportPeerNaming.Named, paper),
      tabUrl: variant => this.benchmarkService.reportDocumentPdfUrl(
        doc.id, disclosureOf(variant), BenchmarkReportPeerNaming.Named, paper, true),
      fallbackFileName: `${this.fileStem(subject.id)}_${safeFileName(label)}.pdf`,
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

  /** Open Download Center: handed to the host. */
  requestDownloads(button: HTMLElement): void {
    this.downloadsRequested.emit(button);
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
    const subject = this.subject;
    const doc = this.deleteTarget;
    if (!subject || !doc || this.deleting) return;
    const subjectId = subject.id;
    const label = audienceLabel(doc.audience);
    this.deleting = true;
    this.deleteError = null;
    this.cdr.markForCheck();
    this.deleteSub?.unsubscribe();
    this.deleteSub = this.api.delete(subjectId, doc.id).subscribe({
      next: () => {
        this.deleting = false;
        if (this.subject?.id !== subjectId) return;
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
    this.host.nativeElement.querySelector<HTMLInputElement>(`#${this.audienceId(audience)}`)?.focus();
  }

  /** The nested confirmations' own close and cancel events stop here, short of the host's report dialog. */
  stopNestedEvent(event: Event): void {
    event.stopPropagation();
  }

  // --- Loading and polling ---

  protected setSnapshot(snapshot: ReportStatusSnapshot): void {
    this.status = snapshot.status;
    this.statusMessage = snapshot.message;
    this.subjectWriterId = snapshot.writerId;
    this.subjectWriterName = snapshot.writerName;
  }

  private emitStatus(): void {
    this.reportStatusChange.emit({
      status: this.status,
      message: this.statusMessage,
      writerId: this.subjectWriterId,
      writerName: this.subjectWriterName
    });
  }

  /** Lists the subject's documents; `after` runs once they are rendered. */
  protected loadDocuments(after?: () => void): void {
    const subject = this.subject;
    if (!subject) return;
    const subjectId = subject.id;
    this.documentsSub?.unsubscribe();
    this.documentsSub = this.api.listDocuments(subjectId).subscribe({
      next: documents => {
        if (this.subject?.id !== subjectId) return;
        this.documents = documents;
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
        if (this.subject?.id !== subjectId) return;
        console.warn(`Failed to list the ${this.subjectNoun}'s AI-written reports`, err);
        this.documentsLoaded = true;
        this.documentsError = 'The AI-written reports could not be listed.';
        this.queueEstimate();
        this.cdr.markForCheck();
      }
    });
  }

  /** The job's view of the status while this server process knows the job, else the subject's own. */
  private fetchStatus(subjectId: number): Observable<ReportStatusSnapshot | null> {
    return this.api.getJob(subjectId).pipe(
      switchMap(job => job
        ? of(reportStatusSnapshotOfJob(job))
        : this.api.getStored(subjectId).pipe(map(stored => this.snapshotOf(stored)))),
      catchError(() => of(null))
    );
  }

  /**
   * Polls the subject's report status while its reports are queued or written, and only while the
   * report dialog is open and no one else follows it. The documents are listed again only when the
   * status changes.
   */
  protected syncPoll(): void {
    const subject = this.subject;
    if (!subject || !this.dialogOpen || this.pollSuppressed || !this.jobInProgress) {
      this.stopPoll();
      return;
    }
    if (this.pollSub && this.pollSubjectId === subject.id) {
      return;
    }
    this.stopPoll();
    const subjectId = subject.id;
    this.pollSubjectId = subjectId;
    this.pollSub = timer(REPORT_DOCUMENTS_POLL_MS, REPORT_DOCUMENTS_POLL_MS).pipe(
      // A failed tick is skipped; the next one asks again.
      switchMap(() => this.fetchStatus(subjectId))
    ).subscribe(snapshot => {
      if (!snapshot || this.subject?.id !== subjectId) return;
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
    this.pollSubjectId = null;
  }

  /**
   * Estimates the cost of the checked documents with the chosen writer, once the choice rests and the
   * subject is writable. While an estimate is not allowed nothing is sent; the subclass queues the
   * estimate its choice needs once it is.
   */
  protected queueEstimate(): void {
    const subject = this.subject;
    const writerId = this.writerConfigId;
    const audiences = this.checkedAudiences;
    const request: EstimateRequest | null =
      subject && this.documentsMissing && !this.notWritableReason && writerId != null && audiences.length > 0
        ? { key: `${subject.id}|${writerId}|${audiences.join(',')}`, subjectId: subject.id, writerId, audiences }
        : null;
    const key = request?.key ?? null;
    if (key === this.requestedEstimateKey) {
      return;
    }
    if (request !== null && !this.estimateAllowed) {
      return;
    }
    this.requestedEstimateKey = key;
    this.estimate = null;
    this.estimateFailed = false;
    this.estimateLoading = request !== null;
    this.estimateRequests.next(request);
  }

  protected reset(): void {
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
