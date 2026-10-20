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
import { Observable, Subject, Subscription, catchError, interval, map, of, switchMap } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkReportAudience,
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkReportPackJobLogEntryDto,
  BenchmarkRunDetailDto,
  BenchmarkRunReportDocumentsStatus,
  BenchmarkRunReportJobDto
} from '../../../services/admin-benchmark.service';
import { BenchmarkPollTickerHandle, BenchmarkPollTickerService } from '../../../services/benchmark-poll-ticker.service';
import { copyToClipboard } from '../../../utils/clipboard.util';
import { parseServerUtcDate } from '../../../utils/date.util';
import { downloadTextFile } from '../../../utils/download.util';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { ProviderBadgeComponent } from '../../../shared/provider-badge/provider-badge.component';
import { RunFactBadge, runFactBadges } from '../run-report-frame/run-facts';
import {
  audienceLabel,
  documentChipClass,
  documentProgressLabel,
  documentRowPhaseLine,
  formatCostUsd,
  formatElapsed
} from '../report-pack/report-document-format';
import {
  REPORT_JOB_DOCUMENT_CARD_DEFAULTS,
  ReportJobDocumentCard,
  ReportJobDocumentsComponent,
  reportJobDocumentCost,
  reportJobDocumentTokens,
  reportJobDocumentTone,
  reuseReportJobDocumentCards
} from '../report-pack/report-job-documents.component';
import { documentStatusQuestion, documentStatusSubject } from '../report-pack/report-validation-explanation';
import {
  ClientPollError,
  ClientPollState,
  RunIdentity,
  buildRunReportWritingDiagnostics,
  formatUtcSeconds,
  runReportStatusWord,
  runReportWriterRepliesFileName,
  runReportWritingDiagnosticsFileName
} from './run-report-writing-diagnostics';
import { reportDocumentsStatusOf } from './report-documents-list';

/** A subject's stored report status, read when the server knows no job for it. */
export interface ReportJobStoredStatus {
  reportDocumentsStatus?: BenchmarkRunReportDocumentsStatus | string | null;
  reportDocumentsMessage?: string | null;
}

/**
 * Where the dialog follows a report writing job: the job endpoint, the cancel endpoint and the
 * subject's name. A run's is built from `runId` when the context names none.
 */
export interface ReportJobSource {
  getJob(): Observable<BenchmarkRunReportJobDto | null>;
  cancel(): Observable<BenchmarkRunReportJobDto>;
  /** The subject in the title and the diagnostics: *Run #42*, *Battery run #7*. */
  subjectLabel: string;
  /**
   * The subject's stored report status, read when the job endpoint answers 204. Without it a 204
   * settles the dialog at once, on no stored status.
   */
  getStoredStatus?(): Observable<ReportJobStoredStatus | null>;
  /** The writer's raw replies of the job as Markdown; 404 once the server no longer holds the job. Without it none are offered. */
  getWriterReplies?(): Observable<string>;
  /** The diagnostics file name's stem in place of `run-<runId>`, such as `battery-run-7`. */
  fileStem?: string;
}

/** What the dialog is opened on. `run` feeds the diagnostics; without it they carry the run id alone. */
export interface RunReportWritingContext {
  /** The run, or for another subject the id its diagnostics carry. */
  runId: number;
  /** The subtitle: the suite and the candidate, as the opener names them. */
  runLabel: string;
  /** The estimate shown before writing started, or null when none was known. */
  estimateUsd: number | null;
  run?: RunIdentity;
  /** The job to follow; absent, the run's own. */
  source?: ReportJobSource;
}

/** One stage of the rail: Queued, Preparing, one per requested document, Done. */
export interface RunReportWritingStage {
  key: string;
  name: string;
  state: 'done' | 'current' | 'pending';
}

/** One document's progress card, with its status. */
export interface RunReportWritingDocumentRow extends ReportJobDocumentCard {
  readonly status: string;
}

/** The interval between two job requests while nothing fails. */
export const RUN_REPORT_WRITING_POLL_MS = 2000;

/** The waits after the 1st, 2nd, 3rd, 4th and every later consecutive failed request. */
export const RUN_REPORT_WRITING_BACKOFF_MS: readonly number[] = [2000, 4000, 8000, 16000, 30000];

/** The wait after `consecutiveFailures` failed requests in a row (1 or more). */
export function runReportWritingBackoffMs(consecutiveFailures: number): number {
  const index = Math.min(Math.max(consecutiveFailures, 1), RUN_REPORT_WRITING_BACKOFF_MS.length) - 1;
  return RUN_REPORT_WRITING_BACKOFF_MS[index];
}

/** The D7 note: finished jobs are kept in the server's memory only. */
export const RUN_REPORT_JOB_UNKNOWN_NOTE =
  'Live details of this job are not available on this server. Overseer may have restarted while it ran. The stored status is shown below.';

/**
 * How long after opening a 204 from the job endpoint is taken for a job still being registered, as
 * long as the run's stored status says its reports are pending or being written.
 */
export const RUN_REPORT_JOB_START_GRACE_MS = 30000;

/** The clipboard and file side effects, held in an object so a spec can observe them. */
export const runReportWritingIo = {
  copy: (text: string): Promise<boolean> => copyToClipboard(text),
  download: (fileName: string, text: string): void => downloadTextFile(fileName, text, 'text/plain;charset=utf-8'),
  downloadMarkdown: (fileName: string, text: string): void => downloadTextFile(fileName, text, 'text/markdown;charset=utf-8')
};

/** The status line while the writer's replies are fetched. */
export const RUN_REPORT_REPLIES_PREPARING_TEXT = 'Preparing the writer\'s replies…';

/** The server no longer holds the job's replies (404). */
export const RUN_REPORT_REPLIES_GONE_TEXT =
  'Overseer no longer holds this job\'s replies (they are kept 6 hours, and until the next writing job of this run).';

/** Any other failure to fetch the replies, followed by its HTTP status. */
export const RUN_REPORT_REPLIES_FAILED_TEXT = 'The writer\'s replies could not be downloaded';

/** Under the stage rail while both documents are written. */
export const RUN_REPORT_ORDER_NOTE = 'The Root Cause Analysis is written first; the Executive Summary is then written from it.';

/** Appended to the status line while the Root Cause Analysis is written and the Executive Summary follows. */
export const RUN_REPORT_ORDER_STATUS_SUFFIX = ' — the Executive Summary follows, written from it';

const WRITTEN_STATUSES = new Set(['Completed', 'CompletedWithWarnings']);
const ACTIVE_STATUSES = new Set(['Writing', 'Repairing']);
/** The document statuses whose card offers the *why* explanation beside its chip. */
const EXPLAINED_STATUSES = new Set(['CompletedWithWarnings', 'Failed']);
const numberFormat = new Intl.NumberFormat('en-US');

/** A document name inside a sentence: *the Executive Summary*. */
function documentPhrase(audience: BenchmarkReportAudience): string {
  return `the ${audienceLabel(audience)}`;
}

/** `Report Pack: GPT-6 Sol` → *a Report Pack for GPT-6 Sol*; `Run #12: Gemini` → *the reports of Run #12 (Gemini)*. */
export function describeBlockingJob(label: string): string {
  const pack = /^Report Pack:\s*(.*)$/i.exec(label);
  if (pack) {
    return pack[1] ? `a Report Pack for ${pack[1]}` : 'a Report Pack';
  }
  const run = /^Run #(\d+):\s*(.*)$/.exec(label);
  if (run) {
    return run[2] ? `the reports of Run #${run[1]} (${run[2]})` : `the reports of Run #${run[1]}`;
  }
  return label;
}

/** The `{ error }` or plain-string message of a refused request, or null. */
function serverMessage(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse)) {
    return null;
  }
  const body = error.error;
  if (typeof body === 'string' && body.trim()) {
    return body.trim();
  }
  if (body && typeof body === 'object') {
    const message = (body as { error?: unknown }).error ?? (body as { message?: unknown }).message;
    if (typeof message === 'string' && message.trim()) {
      return message.trim();
    }
  }
  return null;
}

function reportsWord(n: number): string {
  return n === 1 ? 'report' : 'reports';
}

/** The run's stored report status is Pending or Writing, given as its number or its name. */
function storedStatusInProgress(status: unknown): boolean {
  return status === BenchmarkRunReportDocumentsStatus.Pending || status === BenchmarkRunReportDocumentsStatus.Writing ||
    status === 'Pending' || status === 'Writing';
}

type PollResult = { ok: true; view: BenchmarkRunReportJobDto | null } | { ok: false; error: unknown };

/**
 * The progress of a run's AI report writing job: a phase line, a stage rail, an activity bar, the
 * running figures, one progress card per document and the diagnostics. It polls the run's job, or the job of
 * the context's `source` (a battery run's), every 2 s through the worker ticker, backing off on
 * failures, until the job finishes or the dialog closes.
 *
 * Closing never cancels the job: the close button, Escape and **Run in Background** only stop
 * following it. It opens from inside the run report dialog, so it stops its own close and cancel
 * events, and those of its confirmation dialog, from reaching that dialog.
 */
@Component({
  selector: 'app-run-report-writing-dialog',
  standalone: true,
  imports: [ProviderBadgeComponent, ReportJobDocumentsComponent],
  templateUrl: './run-report-writing-dialog.component.html',
  styleUrls: ['./run-report-writing-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RunReportWritingDialogComponent implements OnInit, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly pollTicker = inject(BenchmarkPollTickerService);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('rwDialog') dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('rwHeading') heading?: ElementRef<HTMLElement>;
  @ViewChild('rwConfirmDialog') confirmDialog?: ElementRef<HTMLDialogElement>;

  /** The prefix of every element id, so two hosts' dialogs can share a document. */
  @Input() idPrefix = 'rw';

  /** The final job view once the job finishes, or null when it settled without one (after a restart). Emitted once per opening. */
  @Output() readonly finished = new EventEmitter<BenchmarkRunReportJobDto | null>();
  /** A written document's id: the host opens it. */
  @Output() readonly viewRequested = new EventEmitter<number>();
  /** The Open Download Center button, for the host to return focus to. */
  @Output() readonly downloadsRequested = new EventEmitter<HTMLElement>();
  /** The dialog closed, however it was closed. */
  @Output() readonly closed = new EventEmitter<void>();

  readonly formatCostUsd = formatCostUsd;
  readonly orderNote = RUN_REPORT_ORDER_NOTE;

  context: RunReportWritingContext | null = null;

  /** The last job view, and when it arrived (client clock). */
  view: BenchmarkRunReportJobDto | null = null;
  private viewReceivedAtMs = 0;

  /** The job endpoint answered 204: the job is not known to this server process. */
  unknownJob = false;
  fallbackRun: BenchmarkRunDetailDto | null = null;
  /** The stored status the 204 fallback read: the run's, or the custom source's. */
  fallbackStatus: ReportJobStoredStatus | null = null;
  fallbackError: string | null = null;

  pollCount = 0;
  lastSuccessUtc: Date | null = null;
  consecutiveFailures = 0;
  lastError: ClientPollError | null = null;
  /** The wait before the next request after a failure, or null while polling is healthy. */
  retryDelayMs: number | null = null;

  /** Cancellation was requested and the job has not settled yet. */
  canceling = false;
  cancelError: string | null = null;

  copyStatus = '';
  copyError: string | null = null;
  /** The writer's replies are being fetched. */
  repliesLoading = false;

  /** The progress cards last shown. */
  private documentCards: readonly RunReportWritingDocumentRow[] = [];
  private finishedEmitted = false;
  /** When the dialog was last opened (client clock), for the start grace of a 204. */
  private openedAtMs = 0;
  /** Bumped on every open and teardown, so a response for an earlier opening never lands. */
  private generation = 0;
  private readonly pollTrigger$ = new Subject<void>();
  private tickerHandle: BenchmarkPollTickerHandle | null = null;
  private pollSub: Subscription | null = null;
  private elapsedSub: Subscription | null = null;
  private cancelSub: Subscription | null = null;
  private fallbackSub: Subscription | null = null;
  private repliesSub: Subscription | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.teardown();
  }

  // -------------------------------------------------------------------------------------------
  // Opening and closing
  // -------------------------------------------------------------------------------------------

  /** Shows the dialog for a run and starts following its report writing job. */
  open(context: RunReportWritingContext): void {
    this.teardown();
    this.context = context;
    this.view = null;
    this.viewReceivedAtMs = 0;
    this.unknownJob = false;
    this.fallbackRun = null;
    this.fallbackStatus = null;
    this.fallbackError = null;
    this.pollCount = 0;
    this.lastSuccessUtc = null;
    this.consecutiveFailures = 0;
    this.lastError = null;
    this.retryDelayMs = null;
    this.canceling = false;
    this.cancelError = null;
    this.copyStatus = '';
    this.copyError = null;
    this.repliesLoading = false;
    this.finishedEmitted = false;
    this.openedAtMs = Date.now();

    this.elapsedSub = interval(1000).subscribe(() => this.cdr.markForCheck());
    this.startPolling(this.jobSource);

    const dialog = this.dialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    this.cdr.detectChanges();
    this.heading?.nativeElement.focus();
  }

  /** Closes the dialog. The job carries on. */
  close(): void {
    this.teardown();
    this.confirmDialog?.nativeElement?.close();
    this.dialog?.nativeElement?.close();
    this.cdr.markForCheck();
  }

  /** The dialog's own close and cancel events; neither may reach the run report dialog around it. */
  onDialogEvent(event: Event): void {
    event.stopPropagation();
    if (event.type !== 'close' || this.dialog?.nativeElement?.open) {
      return;
    }
    this.teardown();
    this.closed.emit();
  }

  /** The confirmation dialog's close and cancel events stop at it. */
  stopNestedEvent(event: Event): void {
    event.stopPropagation();
  }

  get runId(): number | null {
    return this.context?.runId ?? null;
  }

  /** The subject the title and the diagnostics name: the custom source's, else *Run #N*. */
  get subjectLabel(): string {
    return this.context?.source?.subjectLabel ?? `Run #${this.runId ?? ''}`;
  }

  /** The subject inside the diagnostics buttons' names: *run 42*, *battery run #7*. */
  get diagnosticsSubject(): string {
    const label = this.context?.source?.subjectLabel;
    return label ? label.charAt(0).toLowerCase() + label.slice(1) : `run ${this.runId ?? ''}`;
  }

  /** The context's source, or the run's own, built from its id. */
  private get jobSource(): ReportJobSource {
    const context = this.context;
    if (context?.source) {
      return context.source;
    }
    const runId = context?.runId ?? 0;
    return {
      getJob: () => this.benchmarkService.getRunReportJob(runId),
      cancel: () => this.benchmarkService.cancelRunReportJob(runId),
      getWriterReplies: () => this.benchmarkService.getRunReportWriterReplies(runId),
      subjectLabel: `Run #${runId}`
    };
  }

  // -------------------------------------------------------------------------------------------
  // Polling
  // -------------------------------------------------------------------------------------------

  private startPolling(source: ReportJobSource): void {
    const generation = this.generation;
    this.pollSub = this.pollTrigger$.pipe(
      switchMap(() => {
        this.pollCount++;
        return source.getJob().pipe(
          map((view): PollResult => ({ ok: true, view })),
          catchError((error: unknown) => of<PollResult>({ ok: false, error }))
        );
      })
    ).subscribe(result => {
      if (generation !== this.generation) {
        return;
      }
      if (result.ok) {
        this.onPollSuccess(result.view);
      } else {
        this.onPollError(result.error);
      }
      this.cdr.markForCheck();
    });
    this.startTicker(RUN_REPORT_WRITING_POLL_MS);
    this.pollTrigger$.next();
  }

  private startTicker(intervalMs: number): void {
    this.stopTicker();
    this.tickerHandle = this.pollTicker.start(intervalMs, () => this.pollTrigger$.next());
  }

  private stopTicker(): void {
    if (this.tickerHandle) {
      this.tickerHandle();
      this.tickerHandle = null;
    }
  }

  private stopPolling(): void {
    this.stopTicker();
    this.pollSub?.unsubscribe();
    this.pollSub = null;
  }

  private onPollSuccess(view: BenchmarkRunReportJobDto | null): void {
    this.lastSuccessUtc = new Date();
    const recovering = this.consecutiveFailures > 0;
    this.consecutiveFailures = 0;
    this.retryDelayMs = null;
    if (view === null) {
      this.onJobUnknown();
      return;
    }
    this.applyView(view);
    if (recovering && !this.isFinished) {
      this.startTicker(RUN_REPORT_WRITING_POLL_MS);
    }
  }

  /**
   * 204: the subject's stored status is read with the ticker paused. Within the start grace a
   * Pending or Writing subject is a job not registered yet, and polling resumes; otherwise the
   * dialog settles on the stored status.
   */
  private onJobUnknown(): void {
    const source = this.context?.source;
    if (source) {
      this.onSourceJobUnknown(source);
      return;
    }
    const runId = this.runId;
    if (runId === null) {
      this.enterUnknownJob(null, null);
      return;
    }
    this.stopTicker();
    this.fallbackSub?.unsubscribe();
    const generation = this.generation;
    this.fallbackSub = this.benchmarkService.getRun(runId).subscribe({
      next: run => {
        if (generation !== this.generation) {
          return;
        }
        if (storedStatusInProgress(run?.reportDocumentsStatus) &&
          Date.now() - this.openedAtMs < RUN_REPORT_JOB_START_GRACE_MS) {
          this.startTicker(RUN_REPORT_WRITING_POLL_MS);
          return;
        }
        this.enterUnknownJob(run, null);
      },
      error: () => {
        if (generation !== this.generation) {
          return;
        }
        this.enterUnknownJob(null, 'The run’s report status could not be read.');
      }
    });
  }

  /** A custom source's 204: as the run's, through its stored status where it can read one. */
  private onSourceJobUnknown(source: ReportJobSource): void {
    if (!source.getStoredStatus) {
      this.enterUnknownJob(null, null);
      return;
    }
    this.stopTicker();
    this.fallbackSub?.unsubscribe();
    const generation = this.generation;
    this.fallbackSub = source.getStoredStatus().subscribe({
      next: stored => {
        if (generation !== this.generation) {
          return;
        }
        if (storedStatusInProgress(stored?.reportDocumentsStatus) &&
          Date.now() - this.openedAtMs < RUN_REPORT_JOB_START_GRACE_MS) {
          this.startTicker(RUN_REPORT_WRITING_POLL_MS);
          return;
        }
        this.enterUnknownJob(null, null, stored ?? null);
      },
      error: () => {
        if (generation !== this.generation) {
          return;
        }
        this.enterUnknownJob(null, `The report status of ${this.diagnosticsSubject} could not be read.`);
      }
    });
  }

  private onPollError(error: unknown): void {
    this.consecutiveFailures++;
    this.lastError = {
      httpStatus: error instanceof HttpErrorResponse && error.status > 0 ? error.status : null,
      message: serverMessage(error)
        ?? (error instanceof HttpErrorResponse ? (error.statusText || error.message) : String(error ?? 'Unknown error'))
    };
    this.retryDelayMs = runReportWritingBackoffMs(this.consecutiveFailures);
    this.startTicker(this.retryDelayMs);
  }

  private applyView(view: BenchmarkRunReportJobDto): void {
    this.view = view;
    this.viewReceivedAtMs = Date.now();
    if (view.phase === 'Finished') {
      this.canceling = false;
      this.settle(view);
    } else if (view.cancelRequestedAtUtc) {
      this.canceling = true;
    }
  }

  /** The job is not known to the server: the dialog settles on the stored status, or on why it could not be read. */
  private enterUnknownJob(
    run: BenchmarkRunDetailDto | null,
    error: string | null,
    stored: ReportJobStoredStatus | null = run
  ): void {
    this.unknownJob = true;
    this.canceling = false;
    this.fallbackRun = run;
    this.fallbackStatus = stored;
    this.fallbackError = error;
    this.settle(null);
    this.cdr.markForCheck();
  }

  /** The job is over: polling and the elapsed tick stop, and `finished` fires once. */
  private settle(view: BenchmarkRunReportJobDto | null): void {
    this.stopPolling();
    this.elapsedSub?.unsubscribe();
    this.elapsedSub = null;
    if (!this.finishedEmitted) {
      this.finishedEmitted = true;
      this.finished.emit(view);
    }
  }

  private teardown(): void {
    this.generation++;
    this.stopPolling();
    this.elapsedSub?.unsubscribe();
    this.elapsedSub = null;
    this.cancelSub?.unsubscribe();
    this.cancelSub = null;
    this.fallbackSub?.unsubscribe();
    this.fallbackSub = null;
    this.repliesSub?.unsubscribe();
    this.repliesSub = null;
    this.repliesLoading = false;
  }

  // -------------------------------------------------------------------------------------------
  // What is shown
  // -------------------------------------------------------------------------------------------

  /** The job is over, or no longer known to the server. */
  get isFinished(): boolean {
    return this.unknownJob || this.view?.phase === 'Finished';
  }

  /** The live line: it changes only when the phase or the document in hand changes. */
  get statusLine(): string {
    if (this.unknownJob) {
      return RUN_REPORT_JOB_UNKNOWN_NOTE;
    }
    const view = this.view;
    if (!view) {
      return 'Checking the report writing job…';
    }
    switch (view.phase) {
      case 'Queued': {
        const ahead = view.jobsAhead;
        const aheadText = ahead === null || ahead === undefined ? null : `${ahead} ${ahead === 1 ? 'job' : 'jobs'} ahead`;
        if (view.blockingJobLabel) {
          return `Queued behind ${describeBlockingJob(view.blockingJobLabel)}${aheadText ? ` (${aheadText})` : ''}`;
        }
        return aheadText ? `Queued (${aheadText})` : 'Queued';
      }
      case 'Preparing':
        return 'Preparing the fact sheet';
      case 'Writing': {
        const active = (view.job?.documents ?? []).find(doc => ACTIVE_STATUSES.has(doc.status));
        if (!active) {
          return 'Writing the reports';
        }
        const line = `${active.status === 'Repairing' ? 'Revising' : 'Writing'} the draft of ${documentPhrase(active.audience)}`;
        return active.audience === BenchmarkReportAudience.RootCauseAnalysis && this.writesBoth
          ? `${line}${RUN_REPORT_ORDER_STATUS_SUFFIX}`
          : line;
      }
      default:
        return this.finishedLine(view);
    }
  }

  private finishedLine(view: BenchmarkRunReportJobDto): string {
    switch (view.status) {
      case BenchmarkRunReportDocumentsStatus.Completed:
        return 'All reports written';
      case BenchmarkRunReportDocumentsStatus.CompletedWithWarnings:
        return 'Written with warnings';
      case BenchmarkRunReportDocumentsStatus.Canceled:
        return view.message?.trim() || 'Canceled';
      case BenchmarkRunReportDocumentsStatus.Failed:
        return `Failed: ${view.message?.trim() || 'no reason was recorded'}`;
      case BenchmarkRunReportDocumentsStatus.Skipped:
        return `Skipped: ${view.message?.trim() || 'no reason was recorded'}`;
      default:
        return `Finished: ${runReportStatusWord(view.status)}`;
    }
  }

  /** The job writes both the Root Cause Analysis and the Executive Summary, the second from the first. */
  get writesBoth(): boolean {
    const audiences = this.requestedAudiences;
    return audiences.includes(BenchmarkReportAudience.RootCauseAnalysis)
      && audiences.includes(BenchmarkReportAudience.ExecutiveSummary);
  }

  /** The write order under the rail, while both documents are being written. */
  get showOrderNote(): boolean {
    return this.writesBoth && !this.isFinished;
  }

  /** The requested documents, in the order the server lists them, which is the order it writes them. */
  private get requestedAudiences(): BenchmarkReportAudience[] {
    const view = this.view;
    if (!view) {
      return [];
    }
    if (view.audiences?.length) {
      return view.audiences;
    }
    return (view.job?.documents ?? []).map(doc => doc.audience);
  }

  get stages(): RunReportWritingStage[] {
    const view = this.view;
    const audiences = this.requestedAudiences;
    const stages: Omit<RunReportWritingStage, 'state'>[] = [
      { key: 'queued', name: 'Queued' },
      { key: 'preparing', name: 'Preparing' },
      ...audiences.map(audience => ({ key: `doc-${audience}`, name: audienceLabel(audience) })),
      { key: 'done', name: 'Done' }
    ];
    let current = 0;
    if (view?.phase === 'Finished') {
      current = stages.length;
    } else if (view?.phase === 'Preparing') {
      current = 1;
    } else if (view?.phase === 'Writing') {
      const documents = view.job?.documents ?? [];
      const statusOf = (audience: BenchmarkReportAudience): string =>
        documents.find(doc => doc.audience === audience)?.status ?? 'Pending';
      let index = audiences.findIndex(audience => ACTIVE_STATUSES.has(statusOf(audience)));
      if (index < 0) {
        index = audiences.findIndex(audience => statusOf(audience) === 'Pending');
      }
      if (index < 0) {
        index = Math.max(audiences.length - 1, 0);
      }
      current = 2 + index;
    }
    return stages.map((stage, i): RunReportWritingStage => ({
      ...stage,
      state: i < current ? 'done' : i === current ? 'current' : 'pending'
    }));
  }

  /** The server's clock now: its last reading plus the client time since that response arrived (D3). */
  private serverNowMs(): number | null {
    const view = this.view;
    if (!view) {
      return null;
    }
    const server = parseServerUtcDate(view.serverTimeUtc).getTime();
    if (Number.isNaN(server)) {
      return null;
    }
    return server + Math.max(0, Date.now() - this.viewReceivedAtMs);
  }

  private spanToNow(startUtc: string | null | undefined, endUtc: string | null | undefined): number | null {
    if (!startUtc) {
      return null;
    }
    const start = parseServerUtcDate(startUtc).getTime();
    const end = endUtc ? parseServerUtcDate(endUtc).getTime() : this.serverNowMs();
    if (Number.isNaN(start) || end === null || Number.isNaN(end)) {
      return null;
    }
    return Math.max(0, end - start);
  }

  /** From queued to now, or to the finish once the job is over. */
  get elapsedLabel(): string {
    const view = this.view;
    const ms = view ? this.spanToNow(view.queuedAtUtc, view.finishedAtUtc) : null;
    return ms === null ? '—' : formatElapsed(ms);
  }

  /** The writer's name, or '' before the server has named it (the view right after a write is requested). */
  get writerName(): string {
    const view = this.view;
    return (view?.writerDisplayName || view?.writerModelId || '').trim();
  }

  /** The writer's badges in the documented order: thinking level, reasoning mode, provider. */
  get writerBadges(): RunFactBadge[] {
    const view = this.view;
    if (!view) {
      return [];
    }
    return runFactBadges({
      name: this.writerName,
      provider: view.writerProvider || null,
      thinkingLevel: view.writerThinkingLevel,
      reasoningMode: null,
      serviceTier: null,
      customEndpoint: false
    });
  }

  get costLabel(): string {
    return this.isFinished ? 'Cost' : 'Cost so far';
  }

  get costValue(): string {
    return formatCostUsd(this.view?.job?.costUsd ?? null);
  }

  get estimateValue(): string | null {
    const estimate = this.context?.estimateUsd;
    return estimate === null || estimate === undefined ? null : formatCostUsd(estimate);
  }

  formatCount(value: number | null | undefined): string {
    return value === null || value === undefined ? '—' : numberFormat.format(value);
  }

  /** `42,000 in · 9,000 out`, or `—` before the first view. */
  get tokensValue(): string {
    const job = this.view?.job;
    return job ? reportJobDocumentTokens(job.inputTokens, job.outputTokens) : '—';
  }

  /** One progress card per requested document, then any other the job writes; kept while nothing on them changes. */
  get documentRows(): readonly RunReportWritingDocumentRow[] {
    this.documentCards = reuseReportJobDocumentCards(this.documentCards, this.buildDocumentCards());
    return this.documentCards;
  }

  private buildDocumentCards(): RunReportWritingDocumentRow[] {
    const view = this.view;
    if (!view) {
      return [];
    }
    const documents = view.job?.documents ?? [];
    const audiences = this.requestedAudiences;
    // A requested document the job has not listed yet is a placeholder, which no info tip explains.
    const rows: { doc: BenchmarkReportPackDocumentProgressDto; listed: boolean }[] = audiences.map(audience => {
      const listed = documents.find(doc => doc.audience === audience);
      return listed
        ? { doc: listed, listed: true }
        : { doc: { audience, status: 'Pending', documentId: null, errorMessage: null, modelCalls: 0 }, listed: false };
    });
    for (const doc of documents) {
      if (!audiences.includes(doc.audience)) {
        rows.push({ doc, listed: true });
      }
    }
    const finished = view.phase === 'Finished';
    return rows.map(({ doc, listed }): RunReportWritingDocumentRow => {
      const duration = doc.startedAtUtc
        ? this.spanToNow(doc.startedAtUtc, doc.completedAtUtc ?? (finished ? view.finishedAtUtc : null))
        : null;
      const title = audienceLabel(doc.audience);
      const phrase = documentPhrase(doc.audience);
      return {
        ...REPORT_JOB_DOCUMENT_CARD_DEFAULTS,
        key: `doc-${doc.audience}`,
        audience: doc.audience,
        title,
        phrase,
        status: doc.status,
        statusWord: documentProgressLabel(doc.status),
        chipClass: documentChipClass(doc.status),
        tone: reportJobDocumentTone(doc.status),
        phaseLine: documentRowPhaseLine(doc),
        errorMessage: doc.errorMessage || (doc.status === 'Failed' ? 'No reason was recorded.' : null),
        duration: duration === null ? '—' : formatElapsed(duration),
        modelCalls: this.formatCount(doc.modelCalls),
        tokens: reportJobDocumentTokens(doc.inputTokens, doc.outputTokens),
        cost: reportJobDocumentCost(doc.costUsd),
        documentId: doc.documentId,
        viewable: finished && doc.documentId !== null && WRITTEN_STATUSES.has(doc.status),
        doc: listed ? doc : null,
        tipStem: `${this.idPrefix}-card-${doc.audience}`,
        why: listed && EXPLAINED_STATUSES.has(doc.status)
          ? { subject: documentStatusSubject(phrase, doc.status), title: documentStatusQuestion(phrase, doc.status) }
          : null,
        draftIssueCount: listed ? doc.firstTurnIssues?.length ?? 0 : 0,
        draftTipBesideChip: ACTIVE_STATUSES.has(doc.status),
        phaseDetail: listed ? doc.phaseDetail ?? null : null
      };
    });
  }

  /** The footer's sentence once the job is over. */
  get finishedSummary(): string {
    if (this.unknownJob) {
      return '';
    }
    const view = this.view;
    if (!view || view.phase !== 'Finished') {
      return '';
    }
    const documents = view.job?.documents ?? [];
    const written = documents.filter(doc => WRITTEN_STATUSES.has(doc.status)).length;
    const total = Math.max(this.requestedAudiences.length, documents.length);
    const elapsed = this.elapsedLabel;
    const cost = view.job?.costUsd;
    const costPhrase = cost === null || cost === undefined ? 'at an unknown cost' : `for ${formatCostUsd(cost)}`;
    switch (view.status) {
      case BenchmarkRunReportDocumentsStatus.Completed:
        return `${written} ${reportsWord(written)} written in ${elapsed} ${costPhrase}.`;
      case BenchmarkRunReportDocumentsStatus.CompletedWithWarnings:
        return `${written} ${reportsWord(written)} written with warnings in ${elapsed} ${costPhrase}.`;
      case BenchmarkRunReportDocumentsStatus.Canceled:
        return `Canceled after ${elapsed}: ${written} of ${total} ${reportsWord(total)} written, ${costPhrase}.`;
      case BenchmarkRunReportDocumentsStatus.Failed:
        return `Failed after ${elapsed}: ${written} of ${total} ${reportsWord(total)} written, ${costPhrase}.`;
      case BenchmarkRunReportDocumentsStatus.Skipped:
        return 'Skipped: no report was written.';
      default:
        return `${written} of ${total} ${reportsWord(total)} written in ${elapsed} ${costPhrase}.`;
    }
  }

  /** The stored status the 204 fallback read, as a word. */
  get fallbackStatusWord(): string {
    return runReportStatusWord(this.fallbackStatusValue);
  }

  /** The stored status the 204 fallback read, a status name read as its number. */
  private get fallbackStatusValue(): BenchmarkRunReportDocumentsStatus | null | undefined {
    const raw = this.fallbackStatus?.reportDocumentsStatus;
    return typeof raw === 'string' ? reportDocumentsStatusOf(raw) : raw;
  }

  get pollTroubleLine(): string | null {
    if (this.consecutiveFailures === 0 || this.retryDelayMs === null || this.isFinished) {
      return null;
    }
    return `Lost contact with Overseer. Retrying in ${Math.round(this.retryDelayMs / 1000)} s…`;
  }

  get logEntries(): BenchmarkReportPackJobLogEntryDto[] {
    return this.view?.job?.log ?? [];
  }

  logTime(entry: BenchmarkReportPackJobLogEntryDto): string {
    return formatUtcSeconds(entry.timestampUtc) ?? entry.timestampUtc;
  }

  logClass(entry: BenchmarkReportPackJobLogEntryDto): string {
    const severity = (entry.severity ?? '').toLowerCase();
    if (severity === 'error') {
      return 'rw-log-error';
    }
    return severity === 'warning' ? 'rw-log-warning' : '';
  }

  // -------------------------------------------------------------------------------------------
  // Diagnostics
  // -------------------------------------------------------------------------------------------

  private get runIdentity(): RunIdentity {
    const run = this.fallbackRun;
    const base: RunIdentity = this.context?.run ?? { runId: this.runId ?? 0 };
    if (!run) {
      const stored = this.fallbackStatus;
      return stored
        ? {
          ...base,
          reportDocumentsStatus: this.fallbackStatusValue ?? base.reportDocumentsStatus,
          reportDocumentsMessage: stored.reportDocumentsMessage ?? base.reportDocumentsMessage
        }
        : base;
    }
    return {
      ...base,
      suiteName: base.suiteName ?? run.suiteName,
      candidateLabel: base.candidateLabel ?? run.testedModelDisplayNameUsed,
      provider: base.provider ?? run.testedModelProviderUsed,
      modelId: base.modelId ?? run.testedModelIdUsed,
      reportDocumentsStatus: run.reportDocumentsStatus ?? base.reportDocumentsStatus,
      reportDocumentsMessage: run.reportDocumentsMessage ?? base.reportDocumentsMessage
    };
  }

  private get clientState(): ClientPollState {
    return {
      pollCount: this.pollCount,
      lastSuccessUtc: this.lastSuccessUtc,
      consecutiveFailures: this.consecutiveFailures,
      lastError: this.lastError
    };
  }

  /** The diagnostics; a custom source's subject replaces the run line. */
  diagnosticsText(nowUtc: Date = new Date()): string {
    const text = buildRunReportWritingDiagnostics(this.view, this.runIdentity, this.clientState, nowUtc,
      this.context?.estimateUsd ?? null);
    const source = this.context?.source;
    if (!source) {
      return text;
    }
    return text
      .replace(/^== Run ==$/m, '== Subject ==')
      .replace(/^Run: #\d*$/m, () => `Subject: ${source.subjectLabel}`);
  }

  /** The diagnostics file's name: `run-<id>_…`, or the custom source's stem in place of `run-<id>`. */
  private diagnosticsFileName(nowUtc: Date): string {
    const runId = this.runId ?? 0;
    const name = runReportWritingDiagnosticsFileName(runId, nowUtc);
    const stem = this.context?.source?.fileStem;
    return stem ? `${stem}${name.slice(`run-${runId}`.length)}` : name;
  }

  /** The job and client facts the Diagnostics disclosure lists. */
  get diagnosticFacts(): { term: string; value: string }[] {
    const view = this.view;
    const notRecorded = 'not recorded';
    const time = (value: string | Date | null | undefined): string => formatUtcSeconds(value) ?? notRecorded;
    const status = view ? view.status : this.fallbackStatusValue;
    const job = view?.job;
    const versionOf = (value: number | null | undefined): string =>
      value === null || value === undefined ? notRecorded : String(value);
    return [
      { term: 'Job id', value: view?.job?.id || notRecorded },
      { term: 'Pack id', value: view?.job?.packId || notRecorded },
      { term: 'Phase', value: view?.phase ?? notRecorded },
      { term: 'Report status', value: runReportStatusWord(status) },
      { term: 'Report format', value: versionOf(job?.reportFormatVersion) },
      {
        term: 'Engine version',
        value: job?.engineVersion === null ? 'none (no Root Cause Analysis)' : versionOf(job?.engineVersion)
      },
      {
        term: 'Writer output limit',
        value: typeof job?.writerMaxOutputTokens === 'number'
          ? `${numberFormat.format(job.writerMaxOutputTokens)} tokens (reasoning included)`
          : notRecorded
      },
      { term: 'Queued', value: time(view?.queuedAtUtc) },
      { term: 'Slot acquired', value: time(view?.slotAcquiredAtUtc) },
      { term: 'Finished', value: time(view?.finishedAtUtc) },
      { term: 'Cancel requested', value: time(view?.cancelRequestedAtUtc) },
      { term: 'Jobs ahead', value: view?.jobsAhead === null || view?.jobsAhead === undefined ? notRecorded : String(view.jobsAhead) },
      { term: 'Blocking job', value: view?.blockingJobLabel || notRecorded },
      { term: 'Server time', value: time(view?.serverTimeUtc) },
      { term: 'Polls', value: numberFormat.format(this.pollCount) },
      { term: 'Last success', value: time(this.lastSuccessUtc) },
      { term: 'Consecutive failures', value: numberFormat.format(this.consecutiveFailures) },
      {
        term: 'Last error',
        value: this.lastError ? `HTTP ${this.lastError.httpStatus ?? 'none (network)'}: ${this.lastError.message}` : notRecorded
      }
    ];
  }

  async copyDiagnostics(): Promise<void> {
    const generation = this.generation;
    this.copyStatus = '';
    this.copyError = null;
    this.cdr.markForCheck();
    const ok = await runReportWritingIo.copy(this.diagnosticsText());
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
    runReportWritingIo.download(this.diagnosticsFileName(now), this.diagnosticsText(now));
  }

  /** The source fetches replies and the finished job has a writer call with one: *Download the writer's replies* is offered. */
  get showWriterReplies(): boolean {
    return !!this.jobSource.getWriterReplies && this.isFinished
      && (this.view?.job?.documents ?? []).some(doc => (doc.calls ?? []).some(call => (call.replyChars ?? 0) > 0));
  }

  /** The writer-replies file's name: `run-<id>_…`, or the custom source's stem in place of `run-<id>`. */
  private writerRepliesFileName(nowUtc: Date): string {
    const runId = this.runId ?? 0;
    const name = runReportWriterRepliesFileName(runId, nowUtc);
    const stem = this.context?.source?.fileStem;
    return stem ? `${stem}${name.slice(`run-${runId}`.length)}` : name;
  }

  /**
   * Fetches the writer's raw replies of the job as Markdown and saves them; the status line says what
   * happens, and the error line why when the server no longer holds them.
   */
  downloadWriterReplies(): void {
    const source = this.jobSource;
    if (!source.getWriterReplies || this.repliesLoading) {
      return;
    }
    const generation = this.generation;
    this.repliesLoading = true;
    this.copyError = null;
    this.copyStatus = RUN_REPORT_REPLIES_PREPARING_TEXT;
    this.cdr.markForCheck();
    this.repliesSub?.unsubscribe();
    this.repliesSub = source.getWriterReplies().subscribe({
      next: markdown => {
        if (generation !== this.generation) {
          return;
        }
        this.repliesLoading = false;
        this.copyStatus = '';
        runReportWritingIo.downloadMarkdown(this.writerRepliesFileName(new Date()), markdown);
        this.cdr.markForCheck();
      },
      error: (error: unknown) => {
        if (generation !== this.generation) {
          return;
        }
        this.repliesLoading = false;
        this.copyStatus = '';
        const status = error instanceof HttpErrorResponse ? error.status : 0;
        this.copyError = status === 404
          ? RUN_REPORT_REPLIES_GONE_TEXT
          : `${RUN_REPORT_REPLIES_FAILED_TEXT} (${status > 0 ? `HTTP ${status}` : 'no answer from the server'}).`;
        this.cdr.markForCheck();
      }
    });
  }

  // -------------------------------------------------------------------------------------------
  // Actions
  // -------------------------------------------------------------------------------------------

  /** Cancel Writing in the footer: asks first. Inert while a cancellation is in progress. */
  requestCancel(): void {
    if (this.canceling || this.isFinished) {
      return;
    }
    this.cancelError = null;
    const dialog = this.confirmDialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    this.cdr.markForCheck();
  }

  keepWriting(): void {
    this.confirmDialog?.nativeElement?.close();
  }

  confirmCancel(): void {
    const runId = this.runId;
    this.confirmDialog?.nativeElement?.close();
    if (runId === null || this.canceling || this.isFinished) {
      return;
    }
    this.canceling = true;
    this.cancelError = null;
    const generation = this.generation;
    this.cancelSub = this.jobSource.cancel().subscribe({
      next: view => {
        if (generation !== this.generation) {
          return;
        }
        if (view) {
          this.applyView(view);
          if (view.phase !== 'Finished') {
            this.canceling = true;
          }
        }
        this.cdr.markForCheck();
      },
      error: (error: unknown) => {
        if (generation !== this.generation) {
          return;
        }
        this.canceling = false;
        this.cancelError = serverMessage(error) ?? 'The writing could not be canceled.';
        this.pollTrigger$.next();
        this.cdr.markForCheck();
      }
    });
    this.cdr.markForCheck();
  }

  /** View on a stored document's card: the host opens it. */
  viewDocument(row: ReportJobDocumentCard): void {
    if (row.viewable && row.documentId !== null) {
      this.viewRequested.emit(row.documentId);
    }
  }

  openDownloads(button: HTMLElement): void {
    this.downloadsRequested.emit(button);
  }
}
