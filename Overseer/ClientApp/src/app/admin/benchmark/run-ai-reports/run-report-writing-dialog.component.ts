import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, Subscription, catchError, interval, map, of, switchMap } from 'rxjs';

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
import { formatThinkingLevel } from '../../../utils/model-badge-format.util';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { ProviderBadgeComponent } from '../../../shared/provider-badge/provider-badge.component';
import {
  audienceLabel,
  documentChipClass,
  formatCostUsd,
  formatElapsed,
  statusLabel
} from '../report-pack/report-document-format';
import {
  ClientPollError,
  ClientPollState,
  RunIdentity,
  buildRunReportWritingDiagnostics,
  formatUtcSeconds,
  runReportStatusWord,
  runReportWritingDiagnosticsFileName
} from './run-report-writing-diagnostics';

/** What the dialog is opened on. `run` feeds the diagnostics; without it they carry the run id alone. */
export interface RunReportWritingContext {
  runId: number;
  /** The subtitle: the suite and the candidate, as the opener names them. */
  runLabel: string;
  /** The estimate shown before writing started, or null when none was known. */
  estimateUsd: number | null;
  run?: RunIdentity;
}

/** One stage of the rail: Queued, Preparing, one per requested document, Done. */
export interface RunReportWritingStage {
  key: string;
  name: string;
  state: 'done' | 'current' | 'pending';
}

/** One row of the Documents table. */
export interface RunReportWritingDocumentRow {
  audience: BenchmarkReportAudience;
  name: string;
  status: string;
  statusWord: string;
  chipClass: string;
  duration: string;
  modelCalls: string;
  tokens: string;
  cost: string;
  documentId: number | null;
  errorMessage: string | null;
  viewable: boolean;
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
  download: (fileName: string, text: string): void => downloadTextFile(fileName, text, 'text/plain;charset=utf-8')
};

const WRITTEN_STATUSES = new Set(['Completed', 'CompletedWithWarnings']);
const ACTIVE_STATUSES = new Set(['Writing', 'Repairing']);
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
 * running figures, one row per document and the diagnostics. It polls the run's job every 2 s
 * through the worker ticker, backing off on failures, until the job finishes or the dialog closes.
 *
 * Closing never cancels the job: the close button, Escape and **Run in Background** only stop
 * following it. It opens from inside the run report dialog, so it stops its own close and cancel
 * events, and those of its confirmation dialog, from reaching that dialog.
 */
@Component({
  selector: 'app-run-report-writing-dialog',
  standalone: true,
  imports: [ProviderBadgeComponent],
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

  /** The final job view once the job finishes, or null when it settled without one (after a restart). Emitted once per opening. */
  @Output() readonly finished = new EventEmitter<BenchmarkRunReportJobDto | null>();
  /** A written document's id: the host opens it. */
  @Output() readonly viewRequested = new EventEmitter<number>();
  /** The Open Download Center button, for the host to return focus to. */
  @Output() readonly downloadsRequested = new EventEmitter<HTMLElement>();
  /** The dialog closed, however it was closed. */
  @Output() readonly closed = new EventEmitter<void>();

  readonly formatCostUsd = formatCostUsd;
  readonly formatThinkingLevel = formatThinkingLevel;

  context: RunReportWritingContext | null = null;

  /** The last job view, and when it arrived (client clock). */
  view: BenchmarkRunReportJobDto | null = null;
  private viewReceivedAtMs = 0;

  /** The job endpoint answered 204: the job is not known to this server process. */
  unknownJob = false;
  fallbackRun: BenchmarkRunDetailDto | null = null;
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
    this.finishedEmitted = false;
    this.openedAtMs = Date.now();

    this.elapsedSub = interval(1000).subscribe(() => this.cdr.markForCheck());
    this.startPolling(context.runId);

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

  // -------------------------------------------------------------------------------------------
  // Polling
  // -------------------------------------------------------------------------------------------

  private startPolling(runId: number): void {
    const generation = this.generation;
    this.pollSub = this.pollTrigger$.pipe(
      switchMap(() => {
        this.pollCount++;
        return this.benchmarkService.getRunReportJob(runId).pipe(
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
   * 204: the run's stored status is read with the ticker paused. Within the start grace a Pending
   * or Writing run is a job not registered yet, and polling resumes; otherwise the dialog settles
   * on the stored status.
   */
  private onJobUnknown(): void {
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

  /** The job is not known to the server: the dialog settles on the run's stored status, or on why it could not be read. */
  private enterUnknownJob(run: BenchmarkRunDetailDto | null, error: string | null): void {
    this.unknownJob = true;
    this.canceling = false;
    this.fallbackRun = run;
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
        return `${active.status === 'Repairing' ? 'Repairing' : 'Writing'} ${documentPhrase(active.audience)}`;
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

  /** The requested documents, in the order the server lists them. */
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

  get costLabel(): string {
    return this.isFinished ? 'Total cost' : 'Cost so far';
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

  get documentRows(): RunReportWritingDocumentRow[] {
    const view = this.view;
    if (!view) {
      return [];
    }
    const documents = view.job?.documents ?? [];
    const audiences = this.requestedAudiences;
    const rows: BenchmarkReportPackDocumentProgressDto[] = audiences.map(audience =>
      documents.find(doc => doc.audience === audience)
        ?? { audience, status: 'Pending', documentId: null, errorMessage: null, modelCalls: 0 });
    for (const doc of documents) {
      if (!audiences.includes(doc.audience)) {
        rows.push(doc);
      }
    }
    const finished = view.phase === 'Finished';
    return rows.map((doc): RunReportWritingDocumentRow => {
      const duration = doc.startedAtUtc
        ? this.spanToNow(doc.startedAtUtc, doc.completedAtUtc ?? (finished ? view.finishedAtUtc : null))
        : null;
      const hasTokens = doc.inputTokens !== undefined || doc.outputTokens !== undefined;
      return {
        audience: doc.audience,
        name: audienceLabel(doc.audience),
        status: doc.status,
        statusWord: statusLabel(doc.status),
        chipClass: documentChipClass(doc.status),
        duration: duration === null ? '—' : formatElapsed(duration),
        modelCalls: this.formatCount(doc.modelCalls),
        tokens: hasTokens
          ? `${this.formatCount(doc.inputTokens ?? 0)} in · ${this.formatCount(doc.outputTokens ?? 0)} out`
          : '—',
        cost: doc.costUsd === undefined ? '—' : formatCostUsd(doc.costUsd),
        documentId: doc.documentId,
        errorMessage: doc.status === 'Failed' ? (doc.errorMessage || 'No reason was recorded.') : null,
        viewable: finished && doc.documentId !== null && WRITTEN_STATUSES.has(doc.status)
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
    return runReportStatusWord(this.fallbackRun?.reportDocumentsStatus);
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
      return base;
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

  diagnosticsText(nowUtc: Date = new Date()): string {
    return buildRunReportWritingDiagnostics(this.view, this.runIdentity, this.clientState, nowUtc,
      this.context?.estimateUsd ?? null);
  }

  /** The job and client facts the Diagnostics disclosure lists. */
  get diagnosticFacts(): { term: string; value: string }[] {
    const view = this.view;
    const notRecorded = 'not recorded';
    const time = (value: string | Date | null | undefined): string => formatUtcSeconds(value) ?? notRecorded;
    const status = view ? view.status : this.fallbackRun?.reportDocumentsStatus;
    return [
      { term: 'Job id', value: view?.job?.id || notRecorded },
      { term: 'Pack id', value: view?.job?.packId || notRecorded },
      { term: 'Phase', value: view?.phase ?? notRecorded },
      { term: 'Report status', value: runReportStatusWord(status) },
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
    runReportWritingIo.download(runReportWritingDiagnosticsFileName(this.runId ?? 0, now), this.diagnosticsText(now));
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
    this.cancelSub = this.benchmarkService.cancelRunReportJob(runId).subscribe({
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

  viewDocument(row: RunReportWritingDocumentRow): void {
    if (row.viewable && row.documentId !== null) {
      this.viewRequested.emit(row.documentId);
    }
  }

  openDownloads(button: HTMLElement): void {
    this.downloadsRequested.emit(button);
  }
}
