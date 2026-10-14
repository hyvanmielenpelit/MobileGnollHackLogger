import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild,
  inject
} from '@angular/core';
import { map } from 'rxjs';

import {
  BenchmarkBatteryAnalysisDto,
  BenchmarkBatteryRunDto,
  BenchmarkRunDetailDto,
  BenchmarkRunReportDocumentsStatus
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { ModelPickerComponent, ModelPickerOption } from '../../../shared/model-picker/model-picker.component';
import { PdfViewerDialogComponent } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import {
  BenchmarkDownloadCenterComponent,
  DownloadCenterBatteryContext
} from '../download-center/benchmark-download-center.component';
import {
  BATTERY_COMPLETION_ORIGIN,
  ReportStatusSnapshot,
  completionDocumentsOf,
  reportDocumentsStatusOf
} from '../run-ai-reports/report-documents-list';
import {
  REPORT_DOCUMENTS_POLL_MS,
  REPORT_ESTIMATE_DEBOUNCE_MS,
  ReportDocumentsApi,
  ReportDocumentsTabBase
} from '../run-ai-reports/report-documents-tab.base';
import { BATTERY_REPORT_WRITER_ADVICE } from '../run-ai-reports/report-writer-advice';
import { ReportWriterCandidate } from '../run-ai-reports/report-writer-policy';
import {
  ReportJobSource,
  RunReportWritingContext,
  RunReportWritingDialogComponent
} from '../run-ai-reports/run-report-writing-dialog.component';
import { BenchmarkWorkspaceStore } from '../state/benchmark-workspace.store';
import { isFinishedBatteryRunStatus } from './battery.models';

/** The interval of the tab's status poll while a battery run's reports are queued or written. */
export const BATTERY_REPORT_DOCUMENTS_POLL_MS = REPORT_DOCUMENTS_POLL_MS;

/** The pause after the last change of writer or documents before the cost is estimated. */
export const BATTERY_REPORT_ESTIMATE_DEBOUNCE_MS = REPORT_ESTIMATE_DEBOUNCE_MS;

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
 * battery run report dialog, so their close and cancel events stop here. The behavior shared with
 * the run report's tab is in {@link ReportDocumentsTabBase}, and the template is the run report
 * tab's, with every id taking this instance's prefix, since the run report's tab can be in the same
 * document. The layout classes are the run report tab's (`run-ai-reports.component.scss`).
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
  templateUrl: '../run-ai-reports/run-ai-reports.component.html',
  styleUrls: ['../run-ai-reports/run-ai-reports.component.scss', './battery-ai-reports.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BatteryAiReportsComponent extends ReportDocumentsTabBase<BenchmarkBatteryRunDto> implements OnChanges {
  private readonly workspace = inject(BenchmarkWorkspaceStore, { optional: true });

  /** The battery run the report dialog shows. */
  @Input() batteryRun: BenchmarkBatteryRunDto | null = null;
  /** Its latest analysis; whether it is complete and current decides whether the documents can be written. */
  @Input() analysis: BenchmarkBatteryAnalysisDto | null = null;
  /** The report dialog is open; the tab polls only then. */
  @Input() override dialogOpen = true;
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
  @Output() override readonly downloadsRequested = new EventEmitter<HTMLElement>();
  /** The battery run's report status, writer and message changed. */
  @Output() override readonly reportStatusChange = new EventEmitter<BatteryReportStatusChange>();

  @ViewChild(BenchmarkDownloadCenterComponent) downloadCenter?: BenchmarkDownloadCenterComponent;

  /** The prefix of every element id; the progress dialog's ids take it too. */
  override readonly idPrefix = `bai${++nextInstanceId}`;

  /** The *Choosing a report writer* tip's entries. */
  override readonly writerAdvice = BATTERY_REPORT_WRITER_ADVICE;
  override readonly rootClass = 'bai-reports';
  override readonly writerPickerClass = 'bai-report-writer-model-selector';
  override readonly changedSinceText = 'A member run changed since this document was written';
  override readonly downloadsNoticeText = 'These reports can be downloaded from the Download Center as PDF, Word or Markdown, ' +
    'at Summary, Detailed or Full disclosure, one by one or together in a ZIP with the battery\'s analysis report.';

  protected override readonly subjectNoun = 'battery run';
  protected override readonly api: ReportDocumentsApi<BenchmarkBatteryRunDto> = {
    estimate: (batteryRunId, request) => this.benchmarkService.estimateBatteryReports(batteryRunId, request),
    write: (batteryRunId, request) => this.benchmarkService.writeBatteryReportDocuments(batteryRunId, request),
    delete: (batteryRunId, documentId) => this.benchmarkService.deleteBatteryReportDocument(batteryRunId, documentId),
    getJob: batteryRunId => this.benchmarkService.getBatteryReportJob(batteryRunId),
    getStored: batteryRunId => this.benchmarkService.getBatteryRun(batteryRunId),
    listDocuments: batteryRunId => {
      const subjectKey = `battery:${batteryRunId}`;
      return this.benchmarkService.listReportDocuments({ subject: subjectKey, origin: 'batteryCompletion' }).pipe(
        map(documents => completionDocumentsOf(documents, subjectKey, BATTERY_COMPLETION_ORIGIN)));
    }
  };

  ngOnChanges(changes: SimpleChanges): void {
    const runChange = changes['batteryRun'];
    if (runChange) {
      const previous = runChange.previousValue as BenchmarkBatteryRunDto | null | undefined;
      const run = this.batteryRun;
      if (!run) {
        this.reset();
      } else if (!previous || previous.id !== run.id) {
        this.reset();
        this.setSnapshot(this.snapshotOf(run));
        this.writerConfigId = this.defaultWriterId(run);
        this.loadDocuments();
      } else {
        this.setSnapshot(this.snapshotOf(run));
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

  // --- Inputs, with the workspace as their fallback ---

  override get options(): readonly ModelPickerOption<SystemAiConfigDto>[] {
    return this.pickerOptions ?? this.workspace?.benchmarkPickerOptions ?? [];
  }

  override get configs(): readonly SystemAiConfigDto[] {
    return this.writerConfigs ?? this.workspace?.stableBenchmarkCapableConfigs ?? [];
  }

  override get emptyHint(): string | null {
    return this.pickerEmptyHint ?? this.workspace?.benchmarkPickerEmptyHint ?? null;
  }

  // --- The battery run as the tab's subject ---

  protected override get subject(): BenchmarkBatteryRunDto | null {
    return this.batteryRun;
  }

  /** The battery run finished with a status its documents can be written for. */
  get batteryFinished(): boolean {
    return isFinishedBatteryRunStatus(this.batteryRun?.status);
  }

  /**
   * Why the battery run's documents cannot be written now, or '' when they can: it must have
   * finished, with a latest analysis that is complete and current. The server decides the same.
   */
  override get notWritableReason(): string {
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

  protected override get candidate(): ReportWriterCandidate | null {
    const run = this.batteryRun;
    return run
      ? { id: run.testedModelConfigurationId ?? null, provider: run.testedProvider ?? null, modelId: run.testedModelId ?? null }
      : null;
  }

  /** A missing document offers the write panel, with its reason while the battery run cannot be written. */
  protected override get offersWritePanel(): boolean {
    return true;
  }

  protected override get subjectRefusal(): string {
    return '';
  }

  protected override get estimateAllowed(): boolean {
    return true;
  }

  protected override get pollSuppressed(): boolean {
    return false;
  }

  protected override notWrittenYetText(writerName: string): string {
    return this.batteryFinished
      ? ''
      : `Not written yet: ${writerName} writes them once the battery run is finished and analyzed`;
  }

  protected override subjectName(batteryRunId: number): string {
    return `Battery run #${batteryRunId}`;
  }

  protected override fileStem(batteryRunId: number): string {
    return `battery-run-${batteryRunId}`;
  }

  /** What the subtitles name the battery run by: its battery and model. */
  get batteryLabel(): string {
    const run = this.batteryRun;
    if (!run) return '';
    return [run.batteryName, run.testedModelLabel].filter(part => !!part).join(' · ');
  }

  /** The battery run's own writer, when it is one of the picker's configurations. */
  protected override defaultWriterId(run: BenchmarkBatteryRunDto): number | null {
    const own = run.reportWriterModelConfigurationId ?? null;
    return own != null && this.configs.some(config => config.id === own) ? own : null;
  }

  protected override snapshotOf(run: BenchmarkBatteryRunDto): ReportStatusSnapshot {
    const writerId = run.reportWriterModelConfigurationId ?? null;
    return {
      status: reportDocumentsStatusOf(run.reportDocumentsStatus),
      message: run.reportDocumentsMessage ?? null,
      writerId,
      writerName: writerId != null ? (this.configs.find(config => config.id === writerId)?.displayName ?? null) : null
    };
  }

  /** The battery run's job, as the progress dialog follows it. */
  private jobSourceFor(run: BenchmarkBatteryRunDto): ReportJobSource {
    const batteryRunId = run.id;
    return {
      getJob: () => this.benchmarkService.getBatteryReportJob(batteryRunId),
      cancel: () => this.benchmarkService.cancelBatteryReportJob(batteryRunId),
      subjectLabel: this.subjectName(batteryRunId),
      getStoredStatus: () => this.benchmarkService.getBatteryRun(batteryRunId),
      fileStem: this.fileStem(batteryRunId)
    };
  }

  protected override progressRequest(run: BenchmarkBatteryRunDto, estimateUsd: number | null): RunReportWritingContext {
    return {
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
    };
  }

  /**
   * Open Download Center: handed to the host when it listens, else opened here on the battery run,
   * with focus returning to the button when it closes.
   */
  override requestDownloads(button: HTMLElement): void {
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
}
