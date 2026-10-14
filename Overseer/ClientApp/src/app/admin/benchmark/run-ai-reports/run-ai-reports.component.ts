import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges
} from '@angular/core';
import { map } from 'rxjs';

import {
  BenchmarkReportAudience,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportDocumentOrigin,
  BenchmarkRunDetailDto,
  BenchmarkRunReportDocumentsStatus
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { ModelPickerComponent, ModelPickerOption } from '../../../shared/model-picker/model-picker.component';
import { PdfViewerDialogComponent } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { BenchmarkDownloadCenterComponent } from '../download-center/benchmark-download-center.component';
import {
  REPORT_DOCUMENT_AUDIENCES,
  ReportDocumentRow,
  ReportDocumentsStatusKind,
  ReportEstimateView,
  ReportStatusSnapshot,
  completionDocumentsOf,
  reportDocumentsStatusOf
} from './report-documents-list';
import {
  REPORT_DOCUMENTS_POLL_MS,
  REPORT_ESTIMATE_DEBOUNCE_MS,
  ReportDocumentsApi,
  ReportDocumentsTabBase
} from './report-documents-tab.base';
import { ReportWriterCandidate, reportWriterRefusal, reportWriterWarning } from './report-writer-policy';
import { RUN_REPORT_WRITER_ADVICE } from './report-writer-advice';
import { RunReportWritingContext, RunReportWritingDialogComponent } from './run-report-writing-dialog.component';

export { reportDocumentsStatusOf } from './report-documents-list';

/** The interval of the tab's status poll while a finished run's reports are queued or written. */
export const RUN_REPORT_DOCUMENTS_POLL_MS = REPORT_DOCUMENTS_POLL_MS;

/** The pause after the last change of writer or documents before the cost is estimated. */
export const RUN_REPORT_ESTIMATE_DEBOUNCE_MS = REPORT_ESTIMATE_DEBOUNCE_MS;

/** The three AI-written documents of a run, in the order the tab lists them. */
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
 * total with, for two or more documents, the cost of each.
 */
export type RunReportEstimateView = ReportEstimateView;

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

function snapshotOfRun(run: BenchmarkRunDetailDto): ReportStatusSnapshot {
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
 * events stop here. The behavior shared with the battery run report's tab is in
 * {@link ReportDocumentsTabBase}, and its template is the one both render.
 */
@Component({
  selector: 'app-run-ai-reports',
  standalone: true,
  imports: [
    ModelPickerComponent,
    InfoTipComponent,
    RunReportWritingDialogComponent,
    PdfViewerDialogComponent,
    BenchmarkDownloadCenterComponent
  ],
  templateUrl: './run-ai-reports.component.html',
  styleUrls: ['./run-ai-reports.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RunAiReportsComponent extends ReportDocumentsTabBase<BenchmarkRunDetailDto> implements OnChanges {
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
  @Input() override dialogOpen = false;
  /** The tab is the one shown; the cost estimate is requested only then. */
  @Input() active = false;

  /** The button that asked for the Download Center, for the host to return focus to. */
  @Output() override readonly downloadsRequested = new EventEmitter<HTMLElement>();
  /** The run's report status, writer and message changed. */
  @Output() override readonly reportStatusChange = new EventEmitter<RunReportStatusChange>();

  /** The prefix of the element ids; one tab of a run report is open at a time. */
  override readonly idPrefix = 'rr';
  /** The *Choosing a report writer* tip's entries. */
  override readonly writerAdvice = RUN_REPORT_WRITER_ADVICE;
  override readonly rootClass = '';
  override readonly writerPickerClass = 'rr-report-writer-model-selector';
  override readonly changedSinceText = 'Run changed since this document was written';
  override readonly downloadsNoticeText = 'These reports, and the rest of this run, can be downloaded from the Download Center ' +
    'as PDF, Word or Markdown, at Summary, Detailed or Full disclosure, one by one or together in a ZIP.';

  protected override readonly subjectNoun = 'run';
  protected override readonly api: ReportDocumentsApi<BenchmarkRunDetailDto> = {
    estimate: (runId, request) => this.benchmarkService.estimateRunReports(runId, request),
    write: (runId, request) => this.benchmarkService.writeRunReportDocuments(runId, request),
    delete: (runId, documentId) => this.benchmarkService.deleteRunReportDocument(runId, documentId),
    getJob: runId => this.benchmarkService.getRunReportJob(runId),
    getStored: runId => this.benchmarkService.getRun(runId),
    listDocuments: runId => this.benchmarkService.listReportDocuments({ runId }).pipe(
      map(documents => runCompletionDocumentsOf(documents, runId)))
  };

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

  // --- The run as the tab's subject ---

  protected override get subject(): BenchmarkRunDetailDto | null {
    return this.run;
  }

  override get options(): readonly ModelPickerOption<SystemAiConfigDto>[] {
    return this.pickerOptions;
  }

  override get configs(): readonly SystemAiConfigDto[] {
    return this.writerConfigs;
  }

  override get emptyHint(): string | null {
    return this.pickerEmptyHint;
  }

  /** The run finished with a status its documents can be written for. */
  get runFinished(): boolean {
    const status = runStatusName(this.run?.status);
    return status === 'Completed' || status === 'CompletedWithErrors' || status === 'CompletedWithLimits';
  }

  protected override get candidate(): ReportWriterCandidate | null {
    const run = this.run;
    return run ? { provider: run.testedModelProviderUsed, modelId: run.testedModelIdUsed } : null;
  }

  /** A finished run lacking a document offers Write Reports. */
  protected override get offersWritePanel(): boolean {
    return this.runFinished;
  }

  protected override get subjectRefusal(): string {
    return this.run?.assessmentJson ? '' : 'The run has no final synthesis to write about. Re-run the final synthesis first.';
  }

  /** A finished run's documents can always be written; the write panel needs no reason. */
  override get notWritableReason(): string {
    return '';
  }

  /** While the tab is not shown nothing is sent; selecting the tab queues the estimate its choice needs then. */
  protected override get estimateAllowed(): boolean {
    return this.active;
  }

  /** A running run is followed by the report dialog's own detail poll. */
  protected override get pollSuppressed(): boolean {
    return runStatusName(this.run?.status) === 'Running';
  }

  protected override notWrittenYetText(writerName: string): string {
    return runStatusName(this.run?.status) === 'Running'
      ? `Not written yet: ${writerName} writes them once the run is scored`
      : '';
  }

  protected override subjectName(runId: number): string {
    return `Run #${runId}`;
  }

  protected override fileStem(runId: number): string {
    return `run-${runId}`;
  }

  // --- Ids spelled outside the prefix pattern ---

  override get deleteTitleId(): string {
    return 'rrAiDeleteTitle';
  }

  override get sameProviderTitleId(): string {
    return 'rrAiSameProviderTitle';
  }

  override get deleteTipIdPrefix(): string {
    return 'rr-ai-tip-delete-';
  }

  /** The progress dialog's own default prefix. */
  override get writingDialogIdPrefix(): string {
    return 'rw';
  }

  protected override defaultWriterId(run: BenchmarkRunDetailDto): number | null {
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

  protected override snapshotOf(run: BenchmarkRunDetailDto): ReportStatusSnapshot {
    return snapshotOfRun(run);
  }

  protected override progressRequest(run: BenchmarkRunDetailDto, estimateUsd: number | null): RunReportWritingContext {
    return {
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
    };
  }
}
