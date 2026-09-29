import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkReportAudience,
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkReportPackJobDto,
  BenchmarkRunDetailDto,
  BenchmarkRunReportDocumentsStatus,
  BenchmarkRunReportJobDto
} from '../../../services/admin-benchmark.service';
import { BenchmarkPollTickerHandle, BenchmarkPollTickerService } from '../../../services/benchmark-poll-ticker.service';
import {
  RUN_REPORT_JOB_UNKNOWN_NOTE,
  RunReportWritingContext,
  RunReportWritingDialogComponent,
  describeBlockingJob,
  runReportWritingBackoffMs,
  runReportWritingIo
} from './run-report-writing-dialog.component';

const { ExecutiveSummary, TechnicalReport } = BenchmarkReportAudience;
const QUEUED_AT = '2026-09-29T12:00:00Z';

/**
 * Stands in for the worker ticker: a plain setInterval, which fakeAsync's tick() drives, and a record
 * of every interval the dialog asked for, so the backoff sequence can be read back.
 */
class FakePollTicker {
  readonly intervals: number[] = [];
  running = 0;

  start(intervalMs: number, onTick: () => void): BenchmarkPollTickerHandle {
    this.intervals.push(intervalMs);
    this.running++;
    const id = setInterval(onTick, intervalMs);
    let stopped = false;
    const stop = (() => {
      if (!stopped) {
        stopped = true;
        this.running--;
        clearInterval(id);
      }
    }) as unknown as BenchmarkPollTickerHandle;
    Object.defineProperty(stop, 'mode', { value: 'timer', enumerable: true });
    return stop;
  }
}

function doc(audience: BenchmarkReportAudience, status: string,
  extra: Partial<BenchmarkReportPackDocumentProgressDto> = {}): BenchmarkReportPackDocumentProgressDto {
  return { audience, status, documentId: null, errorMessage: null, modelCalls: 0, ...extra };
}

function packJob(overrides: Partial<BenchmarkReportPackJobDto> = {}): BenchmarkReportPackJobDto {
  return {
    id: 'job-7',
    packId: 'pack-7',
    subjectKey: 'run:42',
    subjectLabel: 'GPT-6 Sol',
    suiteId: 5,
    suiteName: 'Core Mechanics',
    writerConfigId: 7,
    writerDisplayName: 'Claude Opus writer',
    startedByUserId: 'user-abc',
    startedAtUtc: QUEUED_AT,
    completedAtUtc: null,
    status: 'Running',
    totalModelCalls: 1,
    inputTokens: 9000,
    outputTokens: 1200,
    costUsd: 0.05,
    documents: [doc(ExecutiveSummary, 'Pending'), doc(TechnicalReport, 'Pending')],
    log: [],
    ...overrides
  };
}

function jobView(overrides: Partial<BenchmarkRunReportJobDto> = {},
  job: Partial<BenchmarkReportPackJobDto> = {}): BenchmarkRunReportJobDto {
  return {
    runId: 42,
    status: BenchmarkRunReportDocumentsStatus.Writing,
    message: null,
    phase: 'Writing',
    queuedAtUtc: QUEUED_AT,
    slotAcquiredAtUtc: '2026-09-29T12:00:05Z',
    finishedAtUtc: null,
    cancelRequestedAtUtc: null,
    jobsAhead: null,
    blockingJobLabel: null,
    audiences: [ExecutiveSummary, TechnicalReport],
    writerConfigId: 7,
    writerDisplayName: 'Claude Opus writer',
    writerProvider: 'Anthropic',
    writerModelId: 'claude-opus',
    writerThinkingLevel: 'high',
    serverTimeUtc: '2026-09-29T12:00:10Z',
    job: packJob(job),
    ...overrides
  };
}

function finishedView(status = BenchmarkRunReportDocumentsStatus.Completed, message: string | null = null): BenchmarkRunReportJobDto {
  return jobView(
    { phase: 'Finished', status, message, finishedAtUtc: '2026-09-29T12:03:05Z', serverTimeUtc: '2026-09-29T12:03:06Z' },
    {
      status: 'Completed',
      completedAtUtc: '2026-09-29T12:03:05Z',
      totalModelCalls: 2,
      inputTokens: 21000,
      outputTokens: 4500,
      costUsd: 0.42,
      documents: [
        doc(ExecutiveSummary, 'Completed', {
          documentId: 17, modelCalls: 1, startedAtUtc: '2026-09-29T12:00:06Z', completedAtUtc: '2026-09-29T12:01:00Z',
          inputTokens: 9000, outputTokens: 1500, costUsd: 0.12
        }),
        doc(TechnicalReport, 'Completed', {
          documentId: 18, modelCalls: 1, startedAtUtc: '2026-09-29T12:01:00Z', completedAtUtc: '2026-09-29T12:03:05Z',
          inputTokens: 12000, outputTokens: 3000, costUsd: 0.3
        })
      ],
      log: [
        { timestampUtc: '2026-09-29T12:00:00Z', severity: 'Info', message: 'Queued.' },
        { timestampUtc: '2026-09-29T12:02:00Z', severity: 'Warning', message: 'Repairing a section.' }
      ]
    });
}

const CONTEXT: RunReportWritingContext = {
  runId: 42,
  runLabel: 'Core Mechanics · GPT-6 Sol',
  estimateUsd: 0.5,
  run: { runId: 42, suiteName: 'Core Mechanics', candidateLabel: 'GPT-6 Sol', provider: 'OpenAI', modelId: 'gpt-6-sol' }
};

describe('RunReportWritingDialogComponent', () => {
  let fixture: ComponentFixture<RunReportWritingDialogComponent>;
  let component: RunReportWritingDialogComponent;
  let service: jasmine.SpyObj<AdminBenchmarkService>;
  let ticker: FakePollTicker;
  let host: HTMLElement;

  beforeEach(async () => {
    service = jasmine.createSpyObj<AdminBenchmarkService>('AdminBenchmarkService',
      ['getRunReportJob', 'cancelRunReportJob', 'getRun']);
    ticker = new FakePollTicker();

    await TestBed.configureTestingModule({
      imports: [RunReportWritingDialogComponent],
      providers: [
        { provide: AdminBenchmarkService, useValue: service },
        { provide: BenchmarkPollTickerService, useValue: ticker }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(RunReportWritingDialogComponent);
    component = fixture.componentInstance;
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  afterEach(() => {
    component.close();
    fixture.destroy();
  });

  function open(context: RunReportWritingContext = CONTEXT): void {
    component.open(context);
    fixture.detectChanges();
  }

  function el<T extends HTMLElement = HTMLElement>(selector: string): T | null {
    return host.querySelector<T>(selector);
  }

  function text(selector: string): string {
    return el(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  }

  function dialog(): HTMLDialogElement {
    return el<HTMLDialogElement>('dialog.run-writing-dialog')!;
  }

  function confirmDialog(): HTMLDialogElement {
    return el<HTMLDialogElement>('dialog.rw-confirm-dialog')!;
  }

  function stop(): void {
    component.close();
    fixture.detectChanges();
    discardPeriodicTasks();
  }

  it('opens modal, sized as a benchmark dialog, and focuses its title', fakeAsync(() => {
    service.getRunReportJob.and.returnValue(of(jobView()));
    open();
    expect(dialog().open).toBeTrue();
    expect(dialog().getAttribute('closedby')).toBe('closerequest');
    expect(dialog().getAttribute('aria-labelledby')).toBe('rwTitle');
    expect(document.activeElement).toBe(el('#rwTitle'));
    expect(text('#rwTitle')).toBe('Writing AI Reports · Run #42');
    expect(text('.dialog-subtitle')).toBe('Core Mechanics · GPT-6 Sol');
    expect(el('.rw-close')?.getAttribute('aria-label')).toBe('Close the report writing progress. The writing continues.');
    expect(el('.rw-close')?.hasAttribute('title')).toBeFalse();
    stop();
  }));

  it('announces phase changes in the live region and keeps the ticking elapsed time outside it', fakeAsync(() => {
    const finished: (BenchmarkRunReportJobDto | null)[] = [];
    component.finished.subscribe(value => finished.push(value));
    service.getRunReportJob.and.returnValues(
      of(jobView({ phase: 'Queued', slotAcquiredAtUtc: null, jobsAhead: 1, blockingJobLabel: 'Report Pack: GPT-6 Sol' })),
      of(jobView({ phase: 'Preparing' })),
      of(jobView({}, { documents: [doc(ExecutiveSummary, 'Writing'), doc(TechnicalReport, 'Pending')] })),
      of(jobView({}, { documents: [doc(ExecutiveSummary, 'Repairing'), doc(TechnicalReport, 'Pending')] })),
      of(finishedView())
    );
    open();

    const status = el('[role="status"].rw-status')!;
    expect(status.getAttribute('aria-live')).toBe('polite');
    expect(status.textContent?.trim()).toBe('Queued behind a Report Pack for GPT-6 Sol (1 job ahead)');

    const elapsed = el('.rw-elapsed')!;
    expect(status.contains(elapsed)).toBeFalse();
    expect(elapsed.closest('[role="status"], [aria-live]')).toBeNull();
    expect(elapsed.textContent?.trim()).toBe('10 s');
    tick(1000);
    fixture.detectChanges();
    expect(el('.rw-elapsed')?.textContent?.trim()).toBe('11 s');
    expect(status.textContent?.trim()).toBe('Queued behind a Report Pack for GPT-6 Sol (1 job ahead)');

    tick(1000);
    fixture.detectChanges();
    expect(text('.rw-status')).toBe('Preparing the fact sheet');

    tick(2000);
    fixture.detectChanges();
    expect(text('.rw-status')).toBe('Writing the Executive Summary');
    const current = el('.run-stage.is-current')!;
    expect(current.getAttribute('data-stage')).toBe(`doc-${ExecutiveSummary}`);
    expect(current.getAttribute('aria-current')).toBe('step');
    expect(current.querySelector('.visually-hidden')?.textContent).toContain('current');
    expect(host.querySelectorAll('.run-stage.is-done').length).toBe(2);
    expect(Array.from(host.querySelectorAll('.run-stage-name')).map(n => n.textContent?.trim()))
      .toEqual(['Queued', 'Preparing', 'Executive Summary', 'Report for AI Researchers and Developers', 'Done']);
    expect(el('progress.job-progress')?.getAttribute('aria-label')).toBe('Writing progress');
    expect(el('progress.job-progress')?.hasAttribute('value')).toBeFalse();

    tick(2000);
    fixture.detectChanges();
    expect(text('.rw-status')).toBe('Repairing the Executive Summary');

    tick(2000);
    fixture.detectChanges();
    expect(text('.rw-status')).toBe('All reports written');
    expect(el('progress')).toBeNull();
    expect(host.querySelectorAll('.run-stage.is-done').length).toBe(5);
    expect(finished.length).toBe(1);

    const calls = service.getRunReportJob.calls.count();
    tick(10000);
    expect(service.getRunReportJob.calls.count()).toBe(calls);
    expect(ticker.running).toBe(0);
    expect(finished.length).toBe(1);
    stop();
  }));

  it('says how many jobs are ahead when no blocking job is named', fakeAsync(() => {
    service.getRunReportJob.and.returnValue(of(jobView({ phase: 'Queued', slotAcquiredAtUtc: null, jobsAhead: 2 })));
    open();
    expect(text('.rw-status')).toBe('Queued (2 jobs ahead)');
    stop();
  }));

  it('describes blocking jobs in words', () => {
    expect(describeBlockingJob('Report Pack: GPT-6 Sol')).toBe('a Report Pack for GPT-6 Sol');
    expect(describeBlockingJob('Run #12: Gemini Flash')).toBe('the reports of Run #12 (Gemini Flash)');
    expect(describeBlockingJob('Something else')).toBe('Something else');
  });

  it('shows the writer, the counts, and an unknown cost as Unknown', fakeAsync(() => {
    service.getRunReportJob.and.returnValue(of(jobView({}, { costUsd: null, inputTokens: 12345 })));
    open();
    expect(text('.rw-cost-label')).toBe('Cost so far');
    expect(text('.rw-cost')).toBe('Unknown');
    expect(text('.rw-input-tokens')).toBe('12,345');
    expect(text('.rw-estimate')).toBe('$0.50');
    expect(text('.rw-stat-writer .model-name')).toBe('Claude Opus writer');
    expect(el('.rw-stat-writer app-provider-badge')).not.toBeNull();
    expect(text('.rw-stat-writer .thinking-badge')).toContain('High');
    stop();
  }));

  it('asks before canceling, then cancels and shows Canceling… until the job settles', fakeAsync(() => {
    service.getRunReportJob.and.returnValue(of(jobView({}, { documents: [doc(ExecutiveSummary, 'Writing'), doc(TechnicalReport, 'Pending')] })));
    service.cancelRunReportJob.and.returnValue(of(jobView({ cancelRequestedAtUtc: '2026-09-29T12:00:09Z' })));
    open();

    el<HTMLButtonElement>('.rw-cancel')!.click();
    fixture.detectChanges();
    expect(confirmDialog().open).toBeTrue();
    expect(service.cancelRunReportJob).not.toHaveBeenCalled();
    expect(text('.rw-confirm-dialog .dialog-body'))
      .toBe('Cancel the writing? The document being written is discarded; documents already written are kept. Tokens already used are still charged.');

    el<HTMLButtonElement>('.rw-confirm-cancel')!.click();
    fixture.detectChanges();
    expect(confirmDialog().open).toBeFalse();
    expect(service.cancelRunReportJob).toHaveBeenCalledOnceWith(42);
    const button = el<HTMLButtonElement>('.rw-cancel')!;
    expect(button.textContent?.trim()).toBe('Canceling…');
    expect(button.getAttribute('aria-disabled')).toBe('true');

    button.click();
    fixture.detectChanges();
    expect(confirmDialog().open).toBeFalse();
    expect(service.cancelRunReportJob).toHaveBeenCalledTimes(1);

    service.getRunReportJob.and.returnValue(of(jobView({
      phase: 'Finished',
      status: BenchmarkRunReportDocumentsStatus.Canceled,
      message: 'Canceled. The Executive Summary was written and is kept.',
      finishedAtUtc: '2026-09-29T12:01:10Z',
      cancelRequestedAtUtc: '2026-09-29T12:00:09Z'
    }, {
      status: 'Canceled',
      costUsd: 0.12,
      documents: [doc(ExecutiveSummary, 'Completed', { documentId: 17 }), doc(TechnicalReport, 'Canceled')]
    })));
    tick(2000);
    fixture.detectChanges();
    expect(text('.rw-status')).toBe('Canceled. The Executive Summary was written and is kept.');
    expect(el('.rw-cancel')).toBeNull();
    expect(text('.rw-summary')).toBe('Canceled after 1 min 10 s: 1 of 2 reports written, for $0.12.');
    stop();
  }));

  it('keeps writing when the confirmation is declined', fakeAsync(() => {
    service.getRunReportJob.and.returnValue(of(jobView()));
    open();
    el<HTMLButtonElement>('.rw-cancel')!.click();
    fixture.detectChanges();
    el<HTMLButtonElement>('.rw-keep-writing')!.click();
    fixture.detectChanges();
    expect(confirmDialog().open).toBeFalse();
    expect(service.cancelRunReportJob).not.toHaveBeenCalled();
    expect(text('.rw-cancel')).toBe('Cancel Writing');
    stop();
  }));

  it('shows a refused cancellation inline', fakeAsync(() => {
    service.getRunReportJob.and.returnValue(of(jobView()));
    service.cancelRunReportJob.and.returnValue(throwError(() => new HttpErrorResponse({
      status: 409, error: { error: 'No report writing is in progress for this run.' }
    })));
    open();
    el<HTMLButtonElement>('.rw-cancel')!.click();
    el<HTMLButtonElement>('.rw-confirm-cancel')!.click();
    fixture.detectChanges();
    expect(text('.rw-cancel-error')).toBe('No report writing is in progress for this run.');
    expect(el('.rw-cancel-error svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(text('.rw-cancel')).toBe('Cancel Writing');
    stop();
  }));

  it('runs in the background without canceling, and stops polling', fakeAsync(() => {
    service.getRunReportJob.and.returnValue(of(jobView()));
    open();
    expect(text('.rw-background')).toBe('Run in Background');
    el<HTMLButtonElement>('.rw-background')!.click();
    fixture.detectChanges();
    expect(dialog().open).toBeFalse();
    expect(service.cancelRunReportJob).not.toHaveBeenCalled();
    const calls = service.getRunReportJob.calls.count();
    tick(10000);
    expect(service.getRunReportJob.calls.count()).toBe(calls);
    expect(ticker.running).toBe(0);
    discardPeriodicTasks();
  }));

  it('stops its own close and cancel events from reaching the dialog around it', fakeAsync(() => {
    service.getRunReportJob.and.returnValue(of(jobView()));
    open();
    const reached: string[] = [];
    host.addEventListener('cancel', () => reached.push('cancel'));
    host.addEventListener('close', () => reached.push('close'));
    dialog().dispatchEvent(new Event('cancel', { bubbles: true }));
    confirmDialog().dispatchEvent(new Event('close', { bubbles: true }));
    expect(reached).toEqual([]);
    stop();
  }));

  it('sums up a finished job and offers View, Open Download Center and Done', fakeAsync(() => {
    const finished: (BenchmarkRunReportJobDto | null)[] = [];
    const viewed: number[] = [];
    const downloads: HTMLElement[] = [];
    component.finished.subscribe(value => finished.push(value));
    component.viewRequested.subscribe(id => viewed.push(id));
    component.downloadsRequested.subscribe(button => downloads.push(button));
    const view = finishedView();
    service.getRunReportJob.and.returnValue(of(view));
    open();

    expect(finished).toEqual([view]);
    expect(text('.rw-summary')).toBe('2 reports written in 3 min 05 s for $0.42.');
    expect(text('.rw-cost-label')).toBe('Total cost');
    expect(text('.rw-elapsed')).toBe('3 min 05 s');
    expect(el('.rw-cancel')).toBeNull();
    expect(el('.rw-background')).toBeNull();
    expect(text('.rw-done')).toBe('Done');
    expect(el('.rw-done')?.className).toContain('btn-gh');

    const rows = host.querySelectorAll('.rw-document-row');
    expect(rows.length).toBe(2);
    expect(rows[0].querySelector('.job-status-chip')?.textContent?.trim()).toBe('Completed');
    expect(rows[0].textContent).toContain('54 s');

    const viewButtons = host.querySelectorAll<HTMLButtonElement>('.rw-view');
    expect(viewButtons.length).toBe(2);
    expect(viewButtons[0].getAttribute('aria-label')).toBe('View the Executive Summary');
    expect(viewButtons[1].getAttribute('aria-label')).toBe('View the Report for AI Researchers and Developers');
    viewButtons[0].click();
    expect(viewed).toEqual([17]);

    const downloadsButton = el<HTMLButtonElement>('.rw-downloads')!;
    expect(downloadsButton.className).toContain('btn-gh-cancel');
    downloadsButton.click();
    expect(downloads.length).toBe(1);
    expect(downloads[0]).toBe(downloadsButton);

    el<HTMLButtonElement>('.rw-done')!.click();
    fixture.detectChanges();
    expect(dialog().open).toBeFalse();
    discardPeriodicTasks();
  }));

  it('shows a failed document’s error inline', fakeAsync(() => {
    service.getRunReportJob.and.returnValue(of(jobView(
      { phase: 'Finished', status: BenchmarkRunReportDocumentsStatus.Failed, message: 'The writer failed.', finishedAtUtc: '2026-09-29T12:01:00Z' },
      { documents: [doc(ExecutiveSummary, 'Failed', { errorMessage: 'The model refused.' }), doc(TechnicalReport, 'Canceled')] })));
    open();
    expect(text('.rw-status')).toBe('Failed: The writer failed.');
    expect(text('.rw-document-row .rw-document-error')).toBe('The model refused.');
    expect(el('.rw-view')).toBeNull();
    stop();
  }));

  it('backs off 2, 4, 8, 16 and 30 s after failed polls, and recovers', fakeAsync(() => {
    const failure = (): ReturnType<AdminBenchmarkService['getRunReportJob']> =>
      throwError(() => new HttpErrorResponse({ status: 502, statusText: 'Bad Gateway' }));
    service.getRunReportJob.and.callFake(failure);
    open();
    expect(service.getRunReportJob).toHaveBeenCalledTimes(1);
    expect(text('.rw-poll-trouble')).toBe('Lost contact with Overseer. Retrying in 2 s…');

    tick(2000);
    expect(service.getRunReportJob).toHaveBeenCalledTimes(2);
    tick(3999);
    expect(service.getRunReportJob).toHaveBeenCalledTimes(2);
    tick(1);
    expect(service.getRunReportJob).toHaveBeenCalledTimes(3);
    fixture.detectChanges();
    expect(text('.rw-poll-trouble')).toBe('Lost contact with Overseer. Retrying in 8 s…');
    tick(8000);
    tick(16000);
    tick(30000);
    expect(service.getRunReportJob).toHaveBeenCalledTimes(6);
    expect(ticker.intervals).toEqual([2000, 2000, 4000, 8000, 16000, 30000, 30000]);
    expect(runReportWritingBackoffMs(9)).toBe(30000);
    expect(component.consecutiveFailures).toBe(6);
    expect(component.lastError).toEqual({ httpStatus: 502, message: 'Bad Gateway' });

    service.getRunReportJob.and.returnValue(of(jobView()));
    tick(30000);
    fixture.detectChanges();
    expect(el('.rw-poll-trouble')).toBeNull();
    expect(component.consecutiveFailures).toBe(0);
    expect(ticker.intervals[ticker.intervals.length - 1]).toBe(2000);
    expect(ticker.running).toBe(1);
    stop();
  }));

  it('keeps the diagnostics closed by default and copies them', fakeAsync(() => {
    const copy = spyOn(runReportWritingIo, 'copy').and.returnValue(Promise.resolve(true));
    service.getRunReportJob.and.returnValue(of(jobView()));
    open();
    const details = el<HTMLDetailsElement>('details.rw-diagnostics')!;
    expect(details.open).toBeFalse();
    expect(text('details.rw-diagnostics summary')).toBe('Diagnostics');
    const button = el<HTMLButtonElement>('.rw-copy-diagnostics')!;
    expect(button.getAttribute('aria-label')).toBe('Copy the report writing diagnostics for run 42');
    expect(button.hasAttribute('title')).toBeFalse();

    button.click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(copy).toHaveBeenCalledTimes(1);
    const copied = copy.calls.mostRecent().args[0];
    expect(copied).toContain('Overseer AI report writing diagnostics');
    expect(copied).toContain('Run: #42');
    expect(copied).not.toContain('user-abc');
    expect(el('.rw-copy-status')?.getAttribute('role')).toBe('status');
    expect(text('.rw-copy-status')).toBe('Copied');
    expect(el('.rw-copy-error')).toBeNull();
    stop();
  }));

  it('shows an inline error when copying fails', fakeAsync(() => {
    spyOn(runReportWritingIo, 'copy').and.returnValue(Promise.resolve(false));
    service.getRunReportJob.and.returnValue(of(jobView()));
    open();
    el<HTMLButtonElement>('.rw-copy-diagnostics')!.click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(text('.rw-copy-error')).toContain('could not be copied');
    expect(el('.rw-copy-error')?.classList).toContain('gh-field-error');
    expect(text('.rw-copy-status')).toBe('');
    stop();
  }));

  it('reports a rejected clipboard write as a failed copy', async () => {
    if (!navigator.clipboard) {
      expect(await runReportWritingIo.copy('x')).toBeFalse();
      return;
    }
    spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.reject(new Error('denied')));
    expect(await runReportWritingIo.copy('x')).toBeFalse();
  });

  it('downloads the diagnostics under a UTC-stamped name with LF line endings', fakeAsync(() => {
    const download = spyOn(runReportWritingIo, 'download');
    service.getRunReportJob.and.returnValue(of(jobView()));
    open();
    el<HTMLButtonElement>('.rw-download-diagnostics')!.click();
    expect(download).toHaveBeenCalledTimes(1);
    const [fileName, content] = download.calls.mostRecent().args;
    expect(fileName).toMatch(/^run-42_ai-report-writing-diagnostics_\d{8}-\d{6}\.txt$/);
    expect(content).toContain('Overseer AI report writing diagnostics');
    expect(content).not.toContain('\r');
    stop();
  }));

  it('falls back to the stored run status when the job is unknown on the first poll', fakeAsync(() => {
    const finished: (BenchmarkRunReportJobDto | null)[] = [];
    component.finished.subscribe(value => finished.push(value));
    service.getRunReportJob.and.returnValue(of(null));
    service.getRun.and.returnValue(of({
      id: 42,
      suiteName: 'Core Mechanics',
      reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Completed,
      reportDocumentsMessage: null
    } as unknown as BenchmarkRunDetailDto));
    open();

    expect(text('.rw-status')).toBe(RUN_REPORT_JOB_UNKNOWN_NOTE);
    expect(RUN_REPORT_JOB_UNKNOWN_NOTE).toBe('Details of this job are no longer available (Overseer restarted).');
    expect(service.getRun).toHaveBeenCalledOnceWith(42);
    expect(text('.rw-fallback-status')).toBe('Completed');
    expect(finished).toEqual([null]);
    expect(el('.run-stage-rail')).toBeNull();
    expect(el('progress')).toBeNull();
    expect(el('.rw-done')).not.toBeNull();

    tick(10000);
    expect(service.getRunReportJob).toHaveBeenCalledTimes(1);
    expect(service.getRun).toHaveBeenCalledTimes(1);
    stop();
  }));

  it('falls back when a job that was seen is no longer known', fakeAsync(() => {
    const finished: (BenchmarkRunReportJobDto | null)[] = [];
    component.finished.subscribe(value => finished.push(value));
    service.getRunReportJob.and.returnValues(of(jobView()), of(null));
    service.getRun.and.returnValue(of({
      id: 42,
      reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Writing,
      reportDocumentsMessage: null
    } as unknown as BenchmarkRunDetailDto));
    open();
    expect(text('.rw-status')).toBe('Writing the reports');
    tick(2000);
    fixture.detectChanges();
    expect(text('.rw-status')).toBe(RUN_REPORT_JOB_UNKNOWN_NOTE);
    expect(text('.rw-fallback-status')).toBe('Writing');
    expect(finished).toEqual([null]);
    stop();
  }));
});
