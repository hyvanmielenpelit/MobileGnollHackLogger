import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, of, throwError } from 'rxjs';

import { AdminDevtoolsService, CorpusIndexJob, CorpusReindexStarted, WikiStatus } from '../../../services/admin-devtools.service';
import { buildCorpusIndexInfo, buildCorpusJob, buildCorpusStatus, configureAdminTestBed } from '../../admin.component.testing';
import { ACTIVE_POLL_MS, IDLE_POLL_MS } from '../shared/dashboard-poller';
import { GnollHackWikiDashboardComponent } from './gnollhack-wiki-dashboard.component';

const nf = new Intl.NumberFormat();
const pf = new Intl.NumberFormat(undefined, { style: 'percent', maximumFractionDigits: 1 });
const LINGER = GnollHackWikiDashboardComponent.ENDED_JOB_LINGER_MS;
const BENCHMARK_MESSAGE = 'A GnollBench run is executing and reads the GnollHack wiki.';

/** An IntersectionObserver that never fires, so the tool usage card stays unloaded. */
class IdleIntersectionObserver {
  readonly root = null;
  readonly rootMargin = '';
  readonly thresholds: number[] = [];
  observe(): void { /* never fires */ }
  unobserve(): void { /* not used */ }
  takeRecords(): IntersectionObserverEntry[] { return []; }
  disconnect(): void { /* nothing to release */ }
}

/** A dialog's close event is queued as a task; wait for the event itself. */
function nextClose(dialog: HTMLDialogElement): Promise<void> {
  return new Promise<void>(resolve => dialog.addEventListener('close', () => resolve(), { once: true }));
}

/** A ready wiki status: indexed at the current HEAD, nothing running. */
function wikiStatus(overrides: Partial<WikiStatus> = {}): WikiStatus {
  return buildCorpusStatus('gnollhack-wiki', { index: buildCorpusIndexInfo(), ...overrides });
}

function runningJob(overrides: Partial<CorpusIndexJob> = {}): CorpusIndexJob {
  return buildCorpusJob({
    id: 'corpus-job-7', outcome: 'Running', finishedAtUtc: null, phase: 'ReadingFiles',
    filesRead: 40, filesTotal: 120, message: null, ...overrides
  });
}

function conflict(code: string, message: string): HttpErrorResponse {
  return new HttpErrorResponse({ status: 409, error: { code, message } });
}

describe('GnollHackWikiDashboardComponent', () => {
  let fixture: ComponentFixture<GnollHackWikiDashboardComponent>;
  let el: HTMLElement;
  let devtools: AdminDevtoolsService;
  let statusSpy: Mock;
  let current: WikiStatus;
  let hidden = false;

  const q = <T extends Element = HTMLElement>(selector: string) => el.querySelector<T>(selector);
  const qa = <T extends Element = HTMLElement>(selector: string) => Array.from(el.querySelectorAll<T>(selector));
  const text = (node: Element | null) => (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const reindexButton = () => q<HTMLButtonElement>('#gw-reindex-btn')!;
  const dialog = () => q<HTMLDialogElement>('dialog')!;

  beforeEach(async () => {
    ({ devtoolsService: devtools } = await configureAdminTestBed(GnollHackWikiDashboardComponent));
    hidden = false;
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => hidden });
    vi.stubGlobal('IntersectionObserver', IdleIntersectionObserver);
    current = wikiStatus();
    statusSpy = vi.spyOn(devtools, 'getCorpusStatus').mockImplementation(
      (() => of(current)) as unknown as AdminDevtoolsService['getCorpusStatus']) as unknown as Mock;

    vi.useFakeTimers();
    fixture = TestBed.createComponent(GnollHackWikiDashboardComponent);
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => {
    fixture.destroy();
    vi.useRealTimers();
    vi.unstubAllGlobals();
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

  // ---------------------------------------------------------------- banner and figures

  it('polls the GnollHack wiki status and shows the ready banner through the panel', () => {
    start();

    expect(statusSpy).toHaveBeenCalledWith('gnollhack-wiki');
    const banner = q('.gh-dash-banner')!;
    expect(banner.getAttribute('role')).toBe('status');
    expect(banner.getAttribute('data-status')).toBe('ok');
    expect(text(banner.querySelector('app-health-status'))).toBe('Ready');
    expect(text(banner.querySelector('.gh-dash-banner-sentence'))).toBe('The index matches the current HEAD.');

    expect(text(reindexButton())).toBe('Re-index wiki');
    expect(reindexButton().hasAttribute('aria-disabled')).toBe(false);
    expect(q('#gw-reindex-tip')).not.toBeNull();
    expect(q('.cip-progress')).toBeNull();
  });

  it('projects the Categories and Duplicate titles counts into the key figures', () => {
    current = wikiStatus({
      categories: [
        { category: 'Monsters', articles: 60, bytes: 4096 },
        { category: 'Items', articles: 30, bytes: 2048 },
        { category: '(root)', articles: 10, bytes: 512 }
      ],
      duplicateTitles: [{ title: 'Orc', paths: ['Monsters/Orc.md', 'Races/Orc.md'] }],
      duplicateTitleCount: 1234
    });
    start();

    const categories = q('.cip-figures .gw-tile-categories')!;
    expect(text(categories.querySelector('dt'))).toBe('Categories');
    expect(text(categories.querySelector('dd'))).toBe('3');
    const duplicates = q('.cip-figures .gw-tile-duplicates')!;
    expect(text(duplicates.querySelector('dt'))).toBe('Duplicate titles');
    expect(text(duplicates.querySelector('dd'))).toBe(nf.format(1234));
  });

  it('shows dashes in the extra figures until an index is published', () => {
    current = wikiStatus({ index: null });
    start();

    expect(text(q('.gw-tile-categories dd'))).toBe('—');
    expect(text(q('.gw-tile-duplicates dd'))).toBe('—');
  });

  it('shows the Indexing banner, the progress card and an unavailable Re-index while a pass runs', () => {
    current = wikiStatus({ currentJob: runningJob() });
    start();

    const banner = q('.gh-dash-banner')!;
    expect(banner.getAttribute('data-status')).toBe('running');
    expect(text(banner.querySelector('app-health-status'))).toBe('Indexing');
    expect(text(banner.querySelector('.gh-dash-banner-sentence')))
      .toBe(`Stage: Reading files (${nf.format(40)} of ${nf.format(120)}).`);
    expect(q('.cip-progress')).not.toBeNull();

    expect(reindexButton().getAttribute('aria-disabled')).toBe('true');
    const reindexSpy = vi.spyOn(devtools, 'startCorpusReindex');
    reindexButton().click();
    expect(reindexSpy).not.toHaveBeenCalled();
  });

  it('shows a failed last pass with its message', () => {
    current = wikiStatus({
      index: null,
      recentJobs: [buildCorpusJob({ trigger: 'Startup', outcome: 'Failed', message: 'The WikiPath folder does not exist.' })]
    });
    start();

    const banner = q('.gh-dash-banner')!;
    expect(banner.getAttribute('data-status')).toBe('error');
    expect(text(banner.querySelector('app-health-status'))).toBe('Last pass failed');
    expect(text(banner.querySelector('.gh-dash-banner-sentence'))).toBe('The WikiPath folder does not exist.');
  });

  it('shows a failed poll with Retry, which polls at once', () => {
    statusSpy.mockImplementation(() => throwError(() => new HttpErrorResponse({ status: 500 })));
    start();

    expect(text(q('#gw-load-error'))).toBe('The GnollHack wiki status could not be loaded (HTTP 500). Trying again in 5 s.');

    statusSpy.mockImplementation(() => of(current));
    q<HTMLButtonElement>('.gh-dash-load-error button')!.click();
    vi.advanceTimersByTime(0);
    fixture.detectChanges();
    expect(statusSpy).toHaveBeenCalledTimes(2);
    expect(q('#gw-load-error')).toBeNull();
    expect(text(q('.gh-dash-banner app-health-status'))).toBe('Ready');
  });

  // ---------------------------------------------------------------- polling

  it('polls every 15 s while idle and every second while a pass runs', () => {
    start();
    expect(statusSpy).toHaveBeenCalledTimes(1);

    current = wikiStatus({ currentJob: runningJob() });
    vi.advanceTimersByTime(IDLE_POLL_MS - 1);
    expect(statusSpy).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(1);
    expect(statusSpy).toHaveBeenCalledTimes(2);

    vi.advanceTimersByTime(ACTIVE_POLL_MS - 1);
    expect(statusSpy).toHaveBeenCalledTimes(2);
    vi.advanceTimersByTime(1);
    expect(statusSpy).toHaveBeenCalledTimes(3);
  });

  it('pauses polling while the page is hidden and polls again when it is shown', () => {
    start();
    setHidden(true);
    vi.advanceTimersByTime(IDLE_POLL_MS * 4);
    expect(statusSpy).toHaveBeenCalledTimes(1);

    setHidden(false);
    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  // ---------------------------------------------------------------- re-index

  it('starts a re-index without the acknowledgment and polls at once', () => {
    start();
    const reindexSpy = vi.spyOn(devtools, 'startCorpusReindex');

    reindexButton().click();
    expect(reindexSpy).toHaveBeenCalledTimes(1);
    expect(reindexSpy).toHaveBeenCalledWith('gnollhack-wiki', false);
    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('asks for confirmation while a GnollBench run executes, and resends acknowledged on Re-index anyway', async () => {
    start();
    const reindexSpy = vi.spyOn(devtools, 'startCorpusReindex');
    reindexSpy.mockReturnValueOnce(throwError(() => conflict('benchmarkRunActive', BENCHMARK_MESSAGE)));

    reindexButton().click();
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(text(q('#gw-reindex-confirm-message'))).toBe(BENCHMARK_MESSAGE);
    expect(text(q('#gw-reindex-confirm-ok'))).toBe('Re-index anyway');
    expect(q('#gw-action-message')).toBeNull();
    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(1);

    const closing = nextClose(dialog());
    q<HTMLButtonElement>('#gw-reindex-confirm-ok')!.click();
    await closing;

    expect(dialog().open).toBe(false);
    expect(reindexSpy).toHaveBeenCalledTimes(2);
    expect(reindexSpy).toHaveBeenNthCalledWith(1, 'gnollhack-wiki', false);
    expect(reindexSpy).toHaveBeenNthCalledWith(2, 'gnollhack-wiki', true);
    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('does not resend when the confirmation is canceled', async () => {
    start();
    const reindexSpy = vi.spyOn(devtools, 'startCorpusReindex');
    reindexSpy.mockReturnValueOnce(throwError(() => conflict('benchmarkRunActive', BENCHMARK_MESSAGE)));

    reindexButton().click();
    fixture.detectChanges();
    expect(dialog().open).toBe(true);

    const closing = nextClose(dialog());
    q<HTMLButtonElement>('#gw-reindex-confirm-cancel')!.click();
    await closing;

    expect(reindexSpy).toHaveBeenCalledTimes(1);
    expect(q('#gw-action-message')).toBeNull();
  });

  it('shows a warning and polls at once when a pass is already running', () => {
    start();
    vi.spyOn(devtools, 'startCorpusReindex').mockReturnValue(
      throwError(() => conflict('alreadyRunning', 'A GnollHack wiki pass is already running.')));

    reindexButton().click();
    fixture.detectChanges();

    const message = q('#gw-action-message')!;
    expect(message.classList).toContain('gh-field-warning');
    expect(text(message)).toBe('A GnollHack wiki pass is already running.');
    expect(message.closest('[role="status"]')).not.toBeNull();
    expect(reindexButton().getAttribute('aria-describedby')).toContain('gw-action-message');
    expect(dialog().open).toBe(false);

    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('shows an error and does not poll when the server refuses with 400', () => {
    start();
    vi.spyOn(devtools, 'startCorpusReindex').mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400, error: { message: 'WikiPath is not configured.' }
    })));

    reindexButton().click();
    fixture.detectChanges();

    const message = q('#gw-action-message')!;
    expect(message.classList).toContain('gh-field-error');
    expect(text(message)).toBe('WikiPath is not configured.');

    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(1);
  });

  it('keeps a finished pass on the progress card for 10 s with its outcome', () => {
    current = wikiStatus({ currentJob: runningJob() });
    start();
    current = wikiStatus({ recentJobs: [buildCorpusJob({ id: 'corpus-job-7' })] });
    vi.advanceTimersByTime(ACTIVE_POLL_MS);
    fixture.detectChanges();

    const card = q('.cip-progress')!;
    expect(card).not.toBeNull();
    expect(card.textContent).toContain('Succeeded');

    vi.advanceTimersByTime(LINGER - 1);
    fixture.detectChanges();
    expect(q('.cip-progress')).not.toBeNull();
    vi.advanceTimersByTime(1);
    fixture.detectChanges();
    expect(q('.cip-progress')).toBeNull();
  });

  // ---------------------------------------------------------------- categories and duplicate titles

  it('lists the categories by articles with labeled cells and share bars', () => {
    current = wikiStatus({
      categories: [
        { category: 'Items', articles: 30, bytes: 2048 },
        { category: '(root)', articles: 10, bytes: 512 },
        { category: 'Monsters', articles: 60, bytes: 4096 }
      ]
    });
    start();

    const wrap = q('.gw-categories-card .gh-dash-table-wrap')!;
    expect(wrap.getAttribute('role')).toBe('region');
    expect(wrap.getAttribute('aria-labelledby')).toBe('gw-categories-title');
    expect(wrap.getAttribute('tabindex')).toBe('0');
    expect(text(q('#gw-categories-title'))).toBe('Categories');
    expect(qa('.gw-categories thead th').map(th => text(th))).toEqual(['Category', 'Articles', 'Size', 'Share']);

    const rows = qa('.gw-categories tbody tr');
    expect(rows.map(r => text(r.querySelector('th[scope="row"]')))).toEqual(['Monsters', 'Items', '(root)']);
    expect(text(rows[0].querySelector('td[data-label="Articles"]'))).toBe(nf.format(60));
    expect(text(rows[0].querySelector('td[data-label="Share"]'))).toBe(pf.format(0.6));
    const bar = rows[0].querySelector<HTMLElement>('.gh-dash-bar')!;
    expect(bar.getAttribute('aria-hidden')).toBe('true');
    expect(bar.style.getPropertyValue('--gh-dash-share')).toBe('60%');

    const cells = qa('.gw-categories td');
    expect(cells.length).toBe(9);
    expect(cells.every(td => !!td.getAttribute('data-label'))).toBe(true);

    expect(text(q('.gw-categories-card .gh-dash-note'))).toContain('wiki_search');
  });

  it('offers Any and the category names, without (root), as the probe filter', () => {
    current = wikiStatus({
      categories: [
        { category: 'Monsters', articles: 60, bytes: 4096 },
        { category: 'Items', articles: 30, bytes: 2048 },
        { category: '(root)', articles: 10, bytes: 512 }
      ]
    });
    start();

    expect(text(q('label[for="gw-probe-filter"]'))).toBe('Category');
    expect(qa('#gw-probe-filter option').map(o => text(o))).toEqual(['Any', 'Items', 'Monsters']);
    expect(q('#gw-probe-query')).not.toBeNull();
  });

  it('lists each duplicate title with its paths and says how many more there are', () => {
    current = wikiStatus({
      duplicateTitles: [
        { title: 'Long sword', paths: ['Items/Long sword.md', 'Weapons/Long sword.md'] },
        { title: 'Orc', paths: ['Monsters/Orc.md', 'Races/Orc.md', 'Roles/Orc.md'] }
      ],
      duplicateTitleCount: 5
    });
    start();

    expect(text(q('#gw-duplicates-title'))).toBe('Duplicate titles');
    expect(text(q('.gw-duplicates-card .gh-dash-note'))).toContain('wiki_view');
    expect(text(q('.gw-duplicates-card .gh-dash-note'))).toContain('disambiguation list');

    const groups = qa<HTMLDetailsElement>('details.gh-disclosure.gw-duplicate');
    expect(groups.length).toBe(2);
    expect(text(groups[0].querySelector('summary'))).toBe('Long sword (2 articles)');
    expect(Array.from(groups[1].querySelectorAll('li code')).map(c => text(c)))
      .toEqual(['Monsters/Orc.md', 'Races/Orc.md', 'Roles/Orc.md']);
    expect(text(q('.gw-more'))).toBe('and 3 more titles');
  });

  it('shows the empty states of both cards', () => {
    start();
    expect(text(q('.gw-categories-card .gh-dash-muted'))).toBe('The index holds no articles.');
    expect(text(q('.gw-duplicates-card .gh-dash-muted'))).toBe('No two articles share a title.');
    expect(q('.gw-more')).toBeNull();

    current = wikiStatus({ index: null });
    vi.advanceTimersByTime(IDLE_POLL_MS);
    fixture.detectChanges();
    expect(text(q('.gw-categories-card .gh-dash-muted'))).toBe('No index has been published yet.');
    expect(text(q('.gw-duplicates-card .gh-dash-muted'))).toBe('No index has been published yet.');
  });

  // ---------------------------------------------------------------- teardown

  it('stops polling when destroyed', () => {
    start();
    fixture.destroy();
    vi.advanceTimersByTime(IDLE_POLL_MS * 4);
    expect(statusSpy).toHaveBeenCalledTimes(1);
  });

  it('cancels a start request in flight when destroyed', () => {
    start();
    const pending = new Subject<CorpusReindexStarted>();
    vi.spyOn(devtools, 'startCorpusReindex').mockReturnValue(pending);

    reindexButton().click();
    expect(pending.observed).toBe(true);
    fixture.destroy();
    expect(pending.observed).toBe(false);
  });

  it('clears the ended-pass linger when destroyed', () => {
    current = wikiStatus({ currentJob: runningJob() });
    start();
    current = wikiStatus({ recentJobs: [buildCorpusJob({ id: 'corpus-job-7' })] });
    vi.advanceTimersByTime(ACTIVE_POLL_MS);
    const component = fixture.componentInstance;
    expect(component.endedJob?.id).toBe('corpus-job-7');

    fixture.destroy();
    vi.advanceTimersByTime(LINGER);
    expect(component.endedJob?.id).toBe('corpus-job-7');
  });

  it('closes an open confirmation dialog when destroyed', () => {
    start();
    vi.spyOn(devtools, 'startCorpusReindex').mockReturnValueOnce(
      throwError(() => conflict('benchmarkRunActive', BENCHMARK_MESSAGE)));
    reindexButton().click();
    fixture.detectChanges();
    const open = dialog();
    expect(open.open).toBe(true);

    fixture.destroy();
    expect(open.open).toBe(false);
  });
});
