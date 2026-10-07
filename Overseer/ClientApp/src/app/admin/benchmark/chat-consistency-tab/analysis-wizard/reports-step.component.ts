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
  BenchmarkRunReportDocumentsStatus,
  BenchmarkRunReportJobDto
} from '../../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../../services/admin.service';
import { AdminChatConsistencyService, ccErrorText } from '../../../../services/admin-chat-consistency.service';
import { ModelPickerComponent, ModelPickerOption } from '../../../../shared/model-picker/model-picker.component';
import { ensureOverlayPolyfills } from '../../../../utils/polyfills.util';
import {
  isReportWriterSameProviderWarning,
  reportDocumentsStatusOf,
  reportJobInProgress,
  reportJobStatusText
} from '../../run-ai-reports/report-documents-list';
import { reportWriterRefusal, reportWriterWarning, reportWriterWarningText } from '../../run-ai-reports/report-writer-policy';
import { CcFigureInput, analysisBands } from '../chat-consistency-charts';
import { CcEventGroup } from '../chat-consistency-events';
import { formatUsd } from '../chat-consistency-format';
import { publishCcReportCharts } from '../chat-consistency-report-charts';
import {
  CC_REPORT_AUDIENCES,
  CcAnalysisResult,
  CcOpenDocumentsRequest,
  CcReportEstimate,
  CcTimelinePoint
} from '../chat-consistency.models';

/** The report job's poll interval, and the longest pause the back-off grows to. */
export const CC_REPORT_POLL_MS = 3000;
export const CC_REPORT_POLL_MAX_MS = 30_000;

/** The pause after the last change of writer or documents before the cost is estimated. */
export const CC_REPORT_ESTIMATE_DEBOUNCE_MS = 300;

/** What the Provider Issue Report checkbox says before an estimate has answered for it. */
export const CC_PROVIDER_REPORT_UNKNOWN =
  'Choose a report writer: its estimate says whether a Provider Issue Report can be written for this analysis.';

type ChartState = 'idle' | 'attaching' | 'done' | 'failed';

interface EstimateRequest {
  key: string;
  analysisId: number;
  writerId: number;
  audiences: BenchmarkReportAudience[];
}

/**
 * Step 4 of the analysis: the documents to write, the report writer, its estimate, the same-provider
 * confirmation, the job's progress with Cancel, the charts attached to the written documents, and
 * *Open in Download Center*.
 */
@Component({
  selector: 'app-cc-reports-step',
  standalone: true,
  imports: [ModelPickerComponent],
  templateUrl: './reports-step.component.html',
  styleUrls: ['./reports-step.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcReportsStepComponent implements OnInit, OnChanges, OnDestroy {
  private readonly service = inject(AdminChatConsistencyService);
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);

  @Input({ required: true }) result!: CcAnalysisResult;
  /** The report writer options, as the launcher lists them. */
  @Input() writerOptions: readonly ModelPickerOption<SystemAiConfigDto>[] = [];
  @Input() writerConfigs: readonly SystemAiConfigDto[] = [];
  @Input() pickerEmptyHint: string | null = null;
  /** The subject's timeline points, for the attached charts. */
  @Input() points: readonly CcTimelinePoint[] = [];
  /** The timeline's composite events, whose E numbers the attached charts reuse. */
  @Input() eventNumbering: readonly CcEventGroup[] = [];

  @Output() readonly openDocuments = new EventEmitter<CcOpenDocumentsRequest>();

  @ViewChild('sameProviderDialog') sameProviderDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('writeButton') writeButton?: ElementRef<HTMLButtonElement>;

  readonly audiences = CC_REPORT_AUDIENCES;
  readonly providerAudience = BenchmarkReportAudience.ProviderIssueReport;

  writerId: number | null = null;
  estimate: CcReportEstimate | null = null;
  estimating = false;
  estimateFailed = false;
  writeSubmitting = false;
  writeError: string | null = null;
  confirmText = '';

  status: BenchmarkRunReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.NotRequested;
  statusMessage: string | null = null;
  job: BenchmarkRunReportJobDto | null = null;
  canceling = false;
  cancelError: string | null = null;

  chartState: ChartState = 'idle';
  chartMessage = '';
  /** Documents of this analysis exist, so the Download Center has something to open. */
  documentCount = 0;

  private readonly checked = new Set<BenchmarkReportAudience>([
    BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.TechnicalReport, BenchmarkReportAudience.InternalBrief
  ]);
  private readonly estimateRequests = new Subject<EstimateRequest | null>();
  private readonly estimateSub: Subscription;
  private requestedKey: string | null = null;
  private writeSub: Subscription | null = null;
  private pollSub: Subscription | null = null;
  private cancelSub: Subscription | null = null;
  private documentsSub: Subscription | null = null;
  private pollDelay = CC_REPORT_POLL_MS;
  private chartsGeneration = 0;

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
      this.reset();
      this.resumeJob();
      this.countDocuments();
    }
  }

  ngOnDestroy(): void {
    this.reset();
    this.estimateSub.unsubscribe();
  }

  get analysisId(): number | null {
    return this.result?.analysisId ?? null;
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
    if (this.isDisabled(audience)) return;
    if ((event.target as HTMLInputElement).checked) this.checked.add(audience); else this.checked.delete(audience);
    this.queueEstimate();
    this.cdr.markForCheck();
  }

  get checkedAudiences(): BenchmarkReportAudience[] {
    return CC_REPORT_AUDIENCES.map(entry => entry.audience).filter(audience => this.checked.has(audience));
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

  get jobInProgress(): boolean {
    return reportJobInProgress(this.status);
  }

  get writeDisabled(): boolean {
    return this.writerId === null || this.checkedAudiences.length === 0 || !!this.writeRefusal ||
      this.jobInProgress || this.writeSubmitting;
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

  get statusText(): string {
    return reportJobStatusText(this.status, this.statusMessage);
  }

  // --- Writing ---

  requestWrite(): void {
    if (this.writeDisabled) return;
    const warning = this.writerWarning;
    if (warning) {
      this.openConfirm(warning);
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

  private openConfirm(text: string): void {
    this.confirmText = text;
    this.cdr.detectChanges();
    const dialog = this.sameProviderDialog?.nativeElement;
    if (dialog && !dialog.open) dialog.showModal();
  }

  private sendWrite(acknowledgeSameProvider: boolean): void {
    const analysisId = this.analysisId;
    const writerId = this.writerId;
    if (analysisId === null || writerId === null || this.writeSubmitting) return;
    this.writeSubmitting = true;
    this.writeError = null;
    this.chartState = 'idle';
    this.chartMessage = '';
    this.cdr.markForCheck();
    this.writeSub?.unsubscribe();
    this.writeSub = this.service.writeReports(analysisId, {
      writerModelConfigurationId: writerId,
      audiences: this.checkedAudiences,
      ...(acknowledgeSameProvider ? { acknowledgeSameProvider: true } : {})
    }).subscribe({
      next: response => {
        this.writeSubmitting = false;
        this.status = reportDocumentsStatusOf(response?.status ?? BenchmarkRunReportDocumentsStatus.Pending);
        this.statusMessage = null;
        this.pollDelay = CC_REPORT_POLL_MS;
        this.schedulePoll(analysisId);
        this.cdr.markForCheck();
      },
      error: err => {
        this.writeSubmitting = false;
        if (err?.status === 409 && !acknowledgeSameProvider && isReportWriterSameProviderWarning(err.error)) {
          this.cdr.markForCheck();
          this.openConfirm(reportWriterWarningText(err.error.assessorModelDisplayName, err.error.provider));
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
        this.cdr.markForCheck();
      },
      error: err => {
        this.canceling = false;
        this.cancelError = ccErrorText(err, 'The writing could not be canceled.');
        this.cdr.markForCheck();
      }
    });
  }

  requestOpenDocuments(): void {
    const analysisId = this.analysisId;
    if (analysisId !== null && this.documentCount > 0) this.openDocuments.emit({ analysisId });
  }

  stopNested(event: Event): void {
    event.stopPropagation();
  }

  // --- Job, documents and charts ---

  private applyJob(job: BenchmarkRunReportJobDto): void {
    this.job = job;
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
        this.cdr.markForCheck();
      },
      error: () => { /* No job information: the step starts idle. */ }
    });
  }

  private schedulePoll(analysisId: number): void {
    this.pollSub?.unsubscribe();
    this.pollSub = timer(this.pollDelay).pipe(
      switchMap(() => this.service.getReportJob(analysisId))
    ).subscribe({
      next: job => {
        this.pollDelay = CC_REPORT_POLL_MS;
        if (job) this.applyJob(job);
        if (this.jobInProgress) {
          this.schedulePoll(analysisId);
        } else {
          this.onFinished(analysisId);
        }
        this.cdr.markForCheck();
      },
      error: () => {
        this.pollDelay = Math.min(this.pollDelay * 2, CC_REPORT_POLL_MAX_MS);
        this.schedulePoll(analysisId);
      }
    });
  }

  /** The job ended: the documents are counted again, and those without charts get them. */
  private onFinished(analysisId: number): void {
    this.countDocuments(documentIds => {
      if (documentIds.length > 0) void this.attachCharts(analysisId, documentIds);
    });
  }

  private countDocuments(after?: (uncharted: number[]) => void): void {
    const analysisId = this.analysisId;
    if (analysisId === null) return;
    this.documentsSub?.unsubscribe();
    this.documentsSub = this.benchmarkService.listReportDocuments({ subject: `chat-consistency:${analysisId}` }).subscribe({
      next: documents => {
        if (this.analysisId !== analysisId) return;
        this.documentCount = documents.length;
        this.cdr.markForCheck();
        after?.(documents.filter(doc => (doc.chartCount ?? 0) === 0).map(doc => doc.id));
      },
      error: () => {
        this.documentCount = 0;
        this.cdr.markForCheck();
      }
    });
  }

  /**
   * What the attached charts draw: the analyzed runs, the analysis's events and annotations, its
   * period bands, every timeline point for the events' harness lookup, and the timeline's E numbers.
   */
  chartInput(): CcFigureInput {
    const ids = new Set([...this.result.baseline.runIds, ...this.result.comparison.runIds]);
    return {
      points: this.points.filter(point => ids.has(point.runId)),
      events: this.result.events,
      annotations: this.result.annotations,
      bands: analysisBands(this.result.baseline, this.result.comparison),
      harnessPoints: this.points,
      eventNumbering: this.eventNumbering
    };
  }

  private async attachCharts(analysisId: number, documentIds: number[]): Promise<void> {
    const generation = ++this.chartsGeneration;
    this.chartState = 'attaching';
    this.chartMessage = `Attaching charts to ${documentIds.length} ${documentIds.length === 1 ? 'document' : 'documents'}…`;
    this.cdr.markForCheck();
    try {
      const outcome = await publishCcReportCharts(this.benchmarkService, documentIds, this.chartInput());
      if (generation !== this.chartsGeneration || this.analysisId !== analysisId) return;
      if (outcome.failed.length === 0) {
        this.chartState = 'done';
        this.chartMessage = `Charts attached to ${outcome.published.length} ${outcome.published.length === 1 ? 'document' : 'documents'}.`;
      } else {
        this.chartState = 'failed';
        this.chartMessage = `Charts could not be attached to ${outcome.failed.length} of ${documentIds.length} documents: ${outcome.failed[0].message}`;
      }
    } catch (error) {
      if (generation !== this.chartsGeneration) return;
      this.chartState = 'failed';
      this.chartMessage = `The charts could not be drawn: ${error instanceof Error ? error.message : 'unknown error'}`;
    }
    this.cdr.markForCheck();
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
    for (const sub of [this.writeSub, this.pollSub, this.cancelSub, this.documentsSub]) {
      sub?.unsubscribe();
    }
    this.writeSub = this.pollSub = this.cancelSub = this.documentsSub = null;
    this.chartsGeneration++;
    this.status = BenchmarkRunReportDocumentsStatus.NotRequested;
    this.statusMessage = null;
    this.job = null;
    this.estimate = null;
    this.estimating = false;
    this.estimateFailed = false;
    this.writeError = null;
    this.cancelError = null;
    this.chartState = 'idle';
    this.chartMessage = '';
    this.documentCount = 0;
    this.requestedKey = null;
    this.estimateRequests.next(null);
    this.queueEstimate();
  }
}
