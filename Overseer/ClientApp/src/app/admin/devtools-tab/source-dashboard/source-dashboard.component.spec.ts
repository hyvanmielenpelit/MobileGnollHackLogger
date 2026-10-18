import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import {
  AdminDevtoolsService,
  SourceDashboard,
  SourceIndexInfo,
  SourceIndexJob,
  SourceIndexStatus
} from '../../../services/admin-devtools.service';
import { buildSourceStatus, configureAdminTestBed } from '../../admin.component.testing';
import { SourceDashboardComponent } from './source-dashboard.component';

const IDLE = SourceDashboardComponent.IDLE_POLL_MS;
const ACTIVE = SourceDashboardComponent.ACTIVE_POLL_MS;
const nf = new Intl.NumberFormat();

function indexInfo(overrides: Partial<SourceIndexInfo> = {}): SourceIndexInfo {
  return {
    generation: 3,
    headSha: '0123456789abcdef',
    branch: 'main',
    indexedAtUtc: '2026-10-18T11:50:00Z',
    durationMs: 4200,
    fileCount: 2050,
    totalLines: 812345,
    constantCount: 9876,
    fingerprintShort: 'abc123',
    skippedBySize: [],
    skippedExcludedDirectories: 0,
    maxFileSizeKB: 512,
    ...overrides
  };
}

function job(overrides: Partial<SourceIndexJob> = {}): SourceIndexJob {
  return {
    id: 'job-1',
    trigger: 'Manual',
    kind: 'Index',
    startedAtUtc: '2026-10-18T11:59:50Z',
    finishedAtUtc: null,
    phase: 'ReadingFiles',
    filesRead: 1234,
    filesTotal: 2050,
    outcome: 'Running',
    message: null,
    outputTail: null,
    makedefsOutcome: 'UpToDate',
    ...overrides
  };
}

function dashboard(gnollHack: Partial<SourceIndexStatus> = {}): SourceDashboard {
  return {
    gnollHack: buildSourceStatus({ index: indexInfo(), currentHeadSha: '0123456789abcdef', ...gnollHack }),
    netHack: buildSourceStatus({ sourcePath: 'C:\\src\\NetHack' }),
    serverTimeUtc: '2026-10-18T12:00:00Z'
  };
}

const BENCHMARK_MESSAGE = 'A GnollBench run is executing and reads the GnollHack source.';

/** A dialog's close event is queued as a task; wait for the event itself. */
function nextClose(dialog: HTMLDialogElement): Promise<void> {
  return new Promise<void>(resolve => dialog.addEventListener('close', () => resolve(), { once: true }));
}

function conflict(code: string, message: string): HttpErrorResponse {
  return new HttpErrorResponse({ status: 409, error: { code, message } });
}

describe('SourceDashboardComponent', () => {
  let fixture: ComponentFixture<SourceDashboardComponent>;
  let el: HTMLElement;
  let devtools: AdminDevtoolsService;
  let statusSpy: Mock;
  let current: SourceDashboard;
  let hidden = false;

  beforeEach(async () => {
    ({ devtoolsService: devtools } = await configureAdminTestBed(SourceDashboardComponent));
    hidden = false;
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => hidden });
    current = dashboard();
    statusSpy = vi.spyOn(devtools, 'getSourceStatus').mockImplementation(() => of(current)) as unknown as Mock;

    vi.useFakeTimers();
    fixture = TestBed.createComponent(SourceDashboardComponent);
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => {
    fixture.destroy();
    vi.useRealTimers();
    delete (document as unknown as { hidden?: boolean }).hidden;
  });

  /** Runs ngOnInit and the first poll, then renders. */
  function start(): void {
    fixture.detectChanges();
    vi.advanceTimersByTime(0);
    fixture.detectChanges();
  }

  function setHidden(value: boolean): void {
    hidden = value;
    document.dispatchEvent(new Event('visibilitychange'));
  }

  const button = (id: string): HTMLButtonElement => el.querySelector(`#${id}`) as HTMLButtonElement;
  const dialog = (): HTMLDialogElement => el.querySelector('dialog') as HTMLDialogElement;
  const text = (id: string): string => el.querySelector(`#${id}`)?.textContent?.trim() ?? '';

  it('renders the idle state from a status fixture', () => {
    start();

    const banner = el.querySelector('.gh-dash-banner') as HTMLElement;
    expect(banner.getAttribute('role')).toBe('status');
    expect(banner.getAttribute('data-status')).toBe('ok');
    expect(banner.textContent).toContain('Ready');
    expect(el.querySelector('.sd-progress')).toBeNull();

    const tiles = Array.from(el.querySelectorAll('.sd-figures .gh-health-tile'));
    expect(tiles.length).toBe(7);
    expect(tiles[0].querySelector('dt')?.textContent?.trim()).toBe('Indexed files');
    expect(tiles[0].querySelector('dd')?.textContent?.trim()).toBe(nf.format(2050));
    expect(tiles[2].querySelector('dd')?.textContent?.trim()).toBe(nf.format(9876));

    expect(button('sd-reindex-btn').getAttribute('aria-disabled')).toBeNull();
    expect(button('sd-makedefs-btn').getAttribute('aria-disabled')).toBeNull();
    expect(el.querySelector('#sd-actions-reason')).toBeNull();
  });

  it('shows the stage rail, a determinate progress bar and disabled actions while a job runs', () => {
    current = dashboard({ currentJob: job() });
    start();

    const banner = el.querySelector('.gh-dash-banner') as HTMLElement;
    expect(banner.getAttribute('data-status')).toBe('running');
    expect(banner.textContent).toContain('Indexing');

    const stages = Array.from(el.querySelectorAll('.run-stage-rail .run-stage'));
    expect(stages.length).toBe(7);
    const stage = (key: string) => el.querySelector(`.run-stage[data-stage="${key}"]`) as HTMLElement;
    expect(stage('WaitingForRepository').classList).toContain('is-done');
    expect(stage('BuildingMakedefs').classList).toContain('is-skipped');
    expect(stage('BuildingMakedefs').querySelector('.run-stage-note')?.textContent).toContain('Inputs unchanged');
    expect(stage('ReadingFiles').classList).toContain('is-current');
    expect(stage('ReadingFiles').getAttribute('aria-current')).toBe('step');
    expect(stage('Publishing').className).not.toMatch(/is-/);

    const progress = el.querySelector('progress') as HTMLProgressElement;
    expect(progress.getAttribute('aria-label')).toBe('Source job progress');
    expect(progress.getAttribute('value')).toBe('1234');
    expect(progress.getAttribute('max')).toBe('2050');
    const progressText = el.querySelector('.sd-progress-text') as HTMLElement;
    expect(progressText.textContent?.trim()).toBe(`${nf.format(1234)} of ${nf.format(2050)} files`);
    expect(progressText.getAttribute('aria-hidden')).toBe('true');

    for (const id of ['sd-reindex-btn', 'sd-makedefs-btn']) {
      expect(button(id).getAttribute('aria-disabled')).toBe('true');
      expect(button(id).getAttribute('aria-describedby')).toContain('sd-actions-reason');
    }
    expect(el.querySelector('#sd-actions-reason')?.textContent).toContain('A source job is running.');

    const reindex = vi.spyOn(devtools, 'startReindex');
    button('sd-reindex-btn').click();
    expect(reindex).not.toHaveBeenCalled();
  });

  it('keeps the progress bar indeterminate outside the Reading files stage', () => {
    current = dashboard({ currentJob: job({ phase: 'ParsingGameData' }) });
    start();

    const progress = el.querySelector('progress') as HTMLProgressElement;
    expect(progress.hasAttribute('value')).toBe(false);
    expect(el.querySelector('.sd-progress-text')).toBeNull();
  });

  it('polls every 15 s while idle and every second while a job runs', () => {
    start();
    expect(statusSpy).toHaveBeenCalledTimes(1);

    vi.advanceTimersByTime(IDLE - 1);
    expect(statusSpy).toHaveBeenCalledTimes(1);
    current = dashboard({ currentJob: job() });
    vi.advanceTimersByTime(1);
    expect(statusSpy).toHaveBeenCalledTimes(2);

    vi.advanceTimersByTime(ACTIVE - 1);
    expect(statusSpy).toHaveBeenCalledTimes(2);
    vi.advanceTimersByTime(1);
    expect(statusSpy).toHaveBeenCalledTimes(3);

    current = dashboard({ recentJobs: [job({ outcome: 'Succeeded', finishedAtUtc: '2026-10-18T12:00:05Z', phase: 'Publishing' })] });
    vi.advanceTimersByTime(ACTIVE);
    expect(statusSpy).toHaveBeenCalledTimes(4);

    vi.advanceTimersByTime(IDLE - 1);
    expect(statusSpy).toHaveBeenCalledTimes(4);
    vi.advanceTimersByTime(1);
    expect(statusSpy).toHaveBeenCalledTimes(5);
  });

  it('keeps a finished job on the progress card for 10 s with its outcome', () => {
    current = dashboard({ currentJob: job() });
    start();
    current = dashboard({ recentJobs: [job({ outcome: 'Succeeded', finishedAtUtc: '2026-10-18T12:00:05Z', phase: 'Publishing' })] });
    vi.advanceTimersByTime(ACTIVE);
    fixture.detectChanges();

    const card = el.querySelector('.sd-progress') as HTMLElement;
    expect(card).not.toBeNull();
    expect(card.textContent).toContain('Succeeded');
    expect(card.querySelector('#sd-cancel-btn')).toBeNull();

    vi.advanceTimersByTime(SourceDashboardComponent.ENDED_JOB_LINGER_MS);
    fixture.detectChanges();
    expect(el.querySelector('.sd-progress')).toBeNull();
  });

  it('shows the reason of a 409 inline and polls for the job that holds the source', () => {
    start();
    vi.spyOn(devtools, 'startReindex').mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 409, error: { message: 'A makedefs build is already running.' }
    })));

    button('sd-reindex-btn').click();
    fixture.detectChanges();

    const message = el.querySelector('#sd-action-message') as HTMLElement;
    expect(message.classList).toContain('gh-field-warning');
    expect(message.textContent).toContain('A makedefs build is already running.');
    expect(message.closest('[role="status"]')).not.toBeNull();
    expect(button('sd-reindex-btn').getAttribute('aria-describedby')).toContain('sd-action-message');

    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('shows the reason of a 409 alreadyRunning inline and polls for the job that holds the source', () => {
    start();
    vi.spyOn(devtools, 'startReindex').mockReturnValue(throwError(() =>
      conflict('alreadyRunning', 'A source job is already running.')));

    button('sd-reindex-btn').click();
    fixture.detectChanges();

    const message = el.querySelector('#sd-action-message') as HTMLElement;
    expect(message.classList).toContain('gh-field-warning');
    expect(message.textContent).toContain('A source job is already running.');
    expect(dialog().open).toBe(false);

    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('starts a re-index without the acknowledgment and polls at once', () => {
    start();
    const reindex = vi.spyOn(devtools, 'startReindex');

    button('sd-reindex-btn').click();
    expect(reindex).toHaveBeenCalledTimes(1);
    expect(reindex).toHaveBeenCalledWith(false);
    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('starts a makedefs build and polls at once', () => {
    start();
    const build = vi.spyOn(devtools, 'startMakedefsBuild');

    button('sd-makedefs-btn').click();
    expect(build).toHaveBeenCalledTimes(1);
    expect(build).toHaveBeenCalledWith(false);
    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('asks for confirmation while a GnollBench run executes, and resends the re-index acknowledged', async () => {
    start();
    const reindex = vi.spyOn(devtools, 'startReindex');
    reindex.mockReturnValueOnce(throwError(() => conflict('benchmarkRunActive', BENCHMARK_MESSAGE)));

    button('sd-reindex-btn').click();
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(text('sd-reindex-confirm-message')).toBe(BENCHMARK_MESSAGE);
    expect(text('sd-reindex-confirm-ok')).toBe('Re-index anyway');
    expect(el.querySelector('#sd-action-message')).toBeNull();
    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(1);

    const closing = nextClose(dialog());
    button('sd-reindex-confirm-ok').click();
    await closing;

    expect(dialog().open).toBe(false);
    expect(reindex).toHaveBeenCalledTimes(2);
    expect(reindex).toHaveBeenNthCalledWith(1, false);
    expect(reindex).toHaveBeenNthCalledWith(2, true);
    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('asks the same for Build makedefs and resends the build', async () => {
    start();
    const reindex = vi.spyOn(devtools, 'startReindex');
    const build = vi.spyOn(devtools, 'startMakedefsBuild');
    build.mockReturnValueOnce(throwError(() => conflict('benchmarkRunActive', BENCHMARK_MESSAGE)));

    button('sd-makedefs-btn').click();
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(text('sd-reindex-confirm-ok')).toBe('Build anyway');
    expect(text('sd-reindex-confirm-effect')).toMatch(/^Build makedefs /);

    const closing = nextClose(dialog());
    button('sd-reindex-confirm-ok').click();
    await closing;

    expect(build).toHaveBeenCalledTimes(2);
    expect(build).toHaveBeenNthCalledWith(1, false);
    expect(build).toHaveBeenNthCalledWith(2, true);
    expect(reindex).not.toHaveBeenCalled();
  });

  it('does not resend when the warning is canceled', async () => {
    start();
    const reindex = vi.spyOn(devtools, 'startReindex');
    reindex.mockReturnValueOnce(throwError(() => conflict('benchmarkRunActive', BENCHMARK_MESSAGE)));

    button('sd-reindex-btn').click();
    fixture.detectChanges();
    expect(dialog().open).toBe(true);

    const closing = nextClose(dialog());
    button('sd-reindex-confirm-cancel').click();
    await closing;

    expect(reindex).toHaveBeenCalledTimes(1);
    expect(el.querySelector('#sd-action-message')).toBeNull();
  });

  it('closes an open confirmation dialog when destroyed', () => {
    start();
    vi.spyOn(devtools, 'startReindex').mockReturnValueOnce(
      throwError(() => conflict('benchmarkRunActive', BENCHMARK_MESSAGE)));
    button('sd-reindex-btn').click();
    fixture.detectChanges();
    const open = dialog();
    expect(open.open).toBe(true);

    fixture.destroy();
    expect(open.open).toBe(false);
  });

  it('backs off 5, 10 and 30 s after failed polls, and Retry polls at once', () => {
    statusSpy.mockImplementation(() => throwError(() => new HttpErrorResponse({ status: 500 })));
    start();
    expect(statusSpy).toHaveBeenCalledTimes(1);
    expect(el.querySelector('.gh-dash-load-error')?.textContent).toContain('could not be loaded');

    vi.advanceTimersByTime(4999);
    expect(statusSpy).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(1);
    expect(statusSpy).toHaveBeenCalledTimes(2);
    vi.advanceTimersByTime(9999);
    expect(statusSpy).toHaveBeenCalledTimes(2);
    vi.advanceTimersByTime(1);
    expect(statusSpy).toHaveBeenCalledTimes(3);
    vi.advanceTimersByTime(29999);
    expect(statusSpy).toHaveBeenCalledTimes(3);
    vi.advanceTimersByTime(1);
    expect(statusSpy).toHaveBeenCalledTimes(4);
    vi.advanceTimersByTime(30000);
    expect(statusSpy).toHaveBeenCalledTimes(5);

    statusSpy.mockImplementation(() => of(current));
    fixture.detectChanges();
    (el.querySelector('.sd-retry') as HTMLButtonElement).click();
    vi.advanceTimersByTime(0);
    fixture.detectChanges();
    expect(statusSpy).toHaveBeenCalledTimes(6);
    expect(el.querySelector('.gh-dash-load-error')).toBeNull();
    expect(el.querySelector('.gh-dash-banner')?.textContent).toContain('Ready');
  });

  it('pauses polling while the page is hidden and polls again when it is shown', () => {
    start();
    setHidden(true);
    vi.advanceTimersByTime(IDLE * 4);
    expect(statusSpy).toHaveBeenCalledTimes(1);

    setHidden(false);
    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('stops polling when destroyed', () => {
    start();
    fixture.destroy();
    vi.advanceTimersByTime(IDLE * 4);
    expect(statusSpy).toHaveBeenCalledTimes(1);
  });

  it('cancels the running job through the service', () => {
    current = dashboard({ currentJob: job() });
    start();
    const cancel = vi.spyOn(devtools, 'cancelSourceJob');

    button('sd-cancel-btn').click();
    expect(cancel).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('names the reason when SourceCodePath is not configured', () => {
    current = dashboard({ configured: false, pathExists: false, index: null, sourcePath: null });
    start();

    expect(el.querySelector('.gh-dash-banner')?.textContent).toContain('Not configured');
    expect(button('sd-reindex-btn').getAttribute('aria-disabled')).toBe('true');
    expect(el.querySelector('#sd-actions-reason')?.textContent).toContain('SourceCodePath is not configured.');
  });
});
