import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';

import {
  AdminDevtoolsService,
  NetHackWikiConversionReport,
  NetHackWikiStatus
} from '../../../services/admin-devtools.service';
import {
  buildCorpusIndexInfo,
  buildCorpusJob,
  buildCorpusStatus,
  configureAdminTestBed
} from '../../admin.component.testing';
import { IDLE_POLL_MS, ACTIVE_POLL_MS } from '../shared/dashboard-poller';
import { formatBytes } from '../shared/devtools-format';
import { NetHackWikiDashboardComponent, skipReasonLabel } from './nethack-wiki-dashboard.component';

const nf = new Intl.NumberFormat();
const pf = new Intl.NumberFormat(undefined, { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 });
const LINGER = NetHackWikiDashboardComponent.ENDED_JOB_LINGER_MS;

/** A dialog's close event is queued as a task; wait for the event itself. */
function nextClose(dialog: HTMLDialogElement): Promise<void> {
  return new Promise<void>(resolve => dialog.addEventListener('close', () => resolve(), { once: true }));
}

function report(overrides: Partial<NetHackWikiConversionReport> = {}): NetHackWikiConversionReport {
  return {
    reportLastWriteUtc: '2026-10-04T18:48:00Z',
    generatedAt: '2026-10-04T20:47:34',
    dumpModified: '2026-10-04T18:31:07',
    dumpBytes: 197540777,
    durationSeconds: 44,
    pagesWritten: 2512,
    redirects: 8277,
    errors: 0,
    skipped: [],
    tablesConverted: 1500,
    droppedTemplates: 42,
    largestFileName: 'Source_nethack.md',
    largestFileBytes: 480000,
    filesOver500Kb: 0,
    ...overrides
  };
}

/** An indexed NetHack wiki at rest, with no report unless the overrides add one. */
function wiki(overrides: Partial<NetHackWikiStatus> = {}): NetHackWikiStatus {
  return buildCorpusStatus('nethack-wiki', {
    index: buildCorpusIndexInfo({ headSha: null, branch: null, maxFileSizeKB: 2048 }),
    ...overrides
  });
}

const running = () => buildCorpusJob({ outcome: 'Running', finishedAtUtc: null, phase: 'ReadingFiles', filesRead: 40, filesTotal: 120 });
const finished = () => buildCorpusJob({ finishedAtUtc: '2026-10-18T12:00:05Z' });

describe('NetHackWikiDashboardComponent', () => {
  let fixture: ComponentFixture<NetHackWikiDashboardComponent>;
  let el: HTMLElement;
  let devtools: AdminDevtoolsService;
  let statusSpy: Mock;
  let current: NetHackWikiStatus;
  let hidden = false;

  const q = <T extends Element = HTMLElement>(selector: string) => el.querySelector<T>(selector);
  const text = (node: Element | null) => (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const reindexButton = () => q<HTMLButtonElement>('#nw-reindex-btn')!;
  const dialog = () => q<HTMLDialogElement>('app-reindex-confirm-dialog dialog')!;

  beforeEach(async () => {
    ({ devtoolsService: devtools } = await configureAdminTestBed(NetHackWikiDashboardComponent));
    hidden = false;
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => hidden });
    current = wiki();
    statusSpy = vi.spyOn(devtools, 'getCorpusStatus').mockImplementation(
      (() => of(current)) as unknown as AdminDevtoolsService['getCorpusStatus']) as unknown as Mock;

    vi.useFakeTimers();
    fixture = TestBed.createComponent(NetHackWikiDashboardComponent);
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => {
    if (dialog()?.open) {
      dialog().close();
    }
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

  function refusal(status: number, error: object): Observable<never> {
    return throwError(() => new HttpErrorResponse({ status, error }));
  }

  // ---------------------------------------------------------------- panel and banner

  it('polls the NetHack wiki status and renders the panel with its label, tip and Namespaces tile', () => {
    current = wiki({ namespaces: [{ namespace: 'article', articles: 118 }, { namespace: 'Help', articles: 2 }] });
    start();

    expect(statusSpy).toHaveBeenCalledWith('nethack-wiki');
    expect(q('.gh-dash-root > app-corpus-index-panel')).not.toBeNull();
    expect(text(reindexButton())).toBe('Re-index wiki');
    expect(reindexButton().getAttribute('aria-describedby')).toBe('nw-reindex-tip');
    expect(text(q('#nw-reindex-tip'))).toContain('Indexes the wiki folder once, now. This corpus never refreshes on its own.');

    const tiles = Array.from(el.querySelectorAll('.cip-figures > .gh-health-tile'));
    const last = tiles[tiles.length - 1];
    expect(last.classList).toContain('nw-namespaces-tile');
    expect(text(last.querySelector('dt'))).toBe('Namespaces');
    expect(text(last.querySelector('dd'))).toBe('2');
  });

  it('shows Ready when the index matches the generated tree', () => {
    start();
    const banner = q('.gh-dash-banner')!;
    expect(banner.getAttribute('data-status')).toBe('ok');
    expect(text(banner.querySelector('app-health-status'))).toBe('Ready');
    expect(text(banner.querySelector('.gh-dash-banner-sentence'))).toBe('The index matches the generated tree.');
  });

  it('says the tree was regenerated after the index was built', () => {
    current = wiki({ treeChangedSinceIndex: true });
    start();
    const banner = q('.gh-dash-banner')!;
    expect(banner.getAttribute('data-status')).toBe('warning');
    expect(text(banner.querySelector('app-health-status'))).toBe('Tree regenerated');
    expect(text(banner.querySelector('.gh-dash-banner-sentence')))
      .toBe('The wiki was regenerated after this index was built. Re-index to load it.');
  });

  it('shows Indexing with the progress card and a blocked action while a pass runs, polling every second', () => {
    current = wiki({ currentJob: running() });
    start();
    const banner = q('.gh-dash-banner')!;
    expect(banner.getAttribute('data-status')).toBe('running');
    expect(text(banner.querySelector('app-health-status'))).toBe('Indexing');
    expect(text(q('#nw-progress-title'))).toBe('NetHack wiki pass in progress');
    expect(reindexButton().getAttribute('aria-disabled')).toBe('true');

    const reindex = vi.spyOn(devtools, 'startCorpusReindex');
    reindexButton().click();
    expect(reindex).not.toHaveBeenCalled();

    vi.advanceTimersByTime(ACTIVE_POLL_MS);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('shows a failed poll with its retry delay, and Retry polls at once', () => {
    statusSpy.mockImplementation(() => throwError(() => new HttpErrorResponse({ status: 500 })));
    start();
    expect(text(q('#nw-load-error'))).toBe('The NetHack wiki status could not be loaded (HTTP 500). Trying again in 5 s.');

    statusSpy.mockImplementation(() => of(current));
    q<HTMLButtonElement>('.gh-dash-load-error button')!.click();
    vi.advanceTimersByTime(0);
    fixture.detectChanges();
    expect(statusSpy).toHaveBeenCalledTimes(2);
    expect(q('#nw-load-error')).toBeNull();
    expect(text(q('.gh-dash-banner app-health-status'))).toBe('Ready');
  });

  // ---------------------------------------------------------------- re-index

  it('starts a pass and polls at once', () => {
    start();
    const reindex = vi.spyOn(devtools, 'startCorpusReindex');
    reindexButton().click();
    expect(reindex).toHaveBeenCalledWith('nethack-wiki', false);
    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('asks before re-indexing during a GnollBench run, and resends acknowledged on Re-index anyway', async () => {
    start();
    const reindex = vi.spyOn(devtools, 'startCorpusReindex')
      .mockReturnValueOnce(refusal(409, { code: 'benchmarkRunActive', message: 'A GnollBench run is executing and reads the NetHack wiki.' }))
      .mockReturnValueOnce(of({ jobId: 'corpus-job-2' }));

    reindexButton().click();
    fixture.detectChanges();
    expect(dialog().open).toBe(true);
    expect(text(q('#nw-reindex-confirm-message'))).toBe('A GnollBench run is executing and reads the NetHack wiki.');
    expect(q('#nw-action-message')).toBeNull();
    expect(reindex).toHaveBeenCalledTimes(1);

    const closing = nextClose(dialog());
    q<HTMLButtonElement>('#nw-reindex-confirm-ok')!.click();
    await closing;
    expect(dialog().open).toBe(false);
    expect(reindex.mock.calls).toEqual([['nethack-wiki', false], ['nethack-wiki', true]]);

    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('does nothing when the GnollBench warning is canceled', async () => {
    start();
    const reindex = vi.spyOn(devtools, 'startCorpusReindex')
      .mockReturnValue(refusal(409, { code: 'benchmarkRunActive', message: 'A GnollBench run is executing.' }));

    reindexButton().click();
    fixture.detectChanges();
    const closing = nextClose(dialog());
    q<HTMLButtonElement>('#nw-reindex-confirm-cancel')!.click();
    await closing;

    expect(reindex).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(1);
  });

  it('shows alreadyRunning as a warning and polls for the running pass', () => {
    start();
    vi.spyOn(devtools, 'startCorpusReindex')
      .mockReturnValue(refusal(409, { code: 'alreadyRunning', message: 'A pass over the NetHack wiki is already running.' }));

    reindexButton().click();
    fixture.detectChanges();
    const message = q('#nw-action-message')!;
    expect(message.classList).toContain('gh-field-warning');
    expect(text(message)).toBe('A pass over the NetHack wiki is already running.');
    expect(dialog().open).toBe(false);

    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('shows a 400 as an error without polling', () => {
    start();
    vi.spyOn(devtools, 'startCorpusReindex')
      .mockReturnValue(refusal(400, { message: 'The NetHackWikiPath folder does not exist.' }));

    reindexButton().click();
    fixture.detectChanges();
    const message = q('#nw-action-message')!;
    expect(message.classList).toContain('gh-field-error');
    expect(text(message)).toBe('The NetHackWikiPath folder does not exist.');

    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(1);
  });

  it('keeps a finished pass on the progress card for 10 s with its outcome', () => {
    current = wiki({ currentJob: running() });
    start();
    current = wiki({ recentJobs: [finished()] });
    vi.advanceTimersByTime(ACTIVE_POLL_MS);
    fixture.detectChanges();

    expect(text(q('#nw-progress-title'))).toBe('NetHack wiki pass finished');
    expect(text(q('.cip-progress .gh-dash-card-head app-health-status'))).toBe('Succeeded');

    vi.advanceTimersByTime(LINGER - 1);
    fixture.detectChanges();
    expect(q('.cip-progress')).not.toBeNull();
    vi.advanceTimersByTime(1);
    fixture.detectChanges();
    expect(q('.cip-progress')).toBeNull();
  });

  it('lingers on a pass it started even when the pass ended between polls', () => {
    start();
    vi.spyOn(devtools, 'startCorpusReindex').mockReturnValue(of({ jobId: 'corpus-job-1' }));
    reindexButton().click();
    current = wiki({ recentJobs: [finished()] });
    vi.advanceTimersByTime(0);
    fixture.detectChanges();

    expect(text(q('#nw-progress-title'))).toBe('NetHack wiki pass finished');
    vi.advanceTimersByTime(LINGER);
    fixture.detectChanges();
    expect(q('.cip-progress')).toBeNull();
  });

  // ---------------------------------------------------------------- last regeneration

  it('shows the last conversion report as facts, in converter time', () => {
    current = wiki({ conversionReport: report() });
    start();

    const card = q('section[aria-labelledby="nw-regeneration-title"]')!;
    expect(card.classList).toContain('gh-dash-card');
    expect(text(q('h4#nw-regeneration-title'))).toBe('Last regeneration');
    const groups = Array.from(card.querySelectorAll('.nw-report-facts > div'));
    expect(groups.map(g => text(g.querySelector('dt')))).toEqual([
      'Generated', 'Dump', 'Duration', 'Pages written', 'Redirects', 'Errors', 'Tables converted',
      'Dropped templates', 'Largest file', 'Files over 500 KB'
    ]);
    const value = (label: string) => text(groups.find(g => text(g.querySelector('dt')) === label)!.querySelector('dd'));
    expect(value('Generated')).toBe('2026-10-04T20:47:34 converter time');
    expect(card.querySelector('time')!.getAttribute('datetime')).toBe('2026-10-04T20:47:34');
    expect(value('Dump')).toBe(`2026-10-04T18:31:07 converter time ${formatBytes(197540777)}`);
    expect(value('Duration')).toBe('44.0 s');
    expect(value('Pages written')).toBe(nf.format(2512));
    expect(value('Redirects')).toBe(nf.format(8277));
    expect(value('Errors')).toBe('0');
    expect(card.querySelector('.nw-report-errors app-health-status')).toBeNull();
    expect(value('Tables converted')).toBe(nf.format(1500));
    expect(value('Dropped templates')).toBe('42');
    expect(value('Largest file')).toBe(`Source_nethack.md ${formatBytes(480000)}`);
    expect(value('Files over 500 KB')).toBe('0');
  });

  it('marks conversion errors with an error status', () => {
    current = wiki({ conversionReport: report({ errors: 3, largestFileName: null, largestFileBytes: null }) });
    start();
    const errors = q('.nw-report-errors')!;
    expect(errors.querySelector('.gh-health-status')!.getAttribute('data-status')).toBe('error');
    expect(text(errors)).toBe('3 Conversion errors');
  });

  it('lists the skipped reasons in words, with a reason\'s breakdown nested under it', () => {
    current = wiki({
      conversionReport: report({
        skipped: [
          { reason: 'namespace', count: 24938 },
          {
            reason: 'formerly_converted_namespaces', count: 3148,
            breakdown: [{ reason: 'source', count: 2294 }, { reason: 'category', count: 854 }]
          },
          { reason: 'non_english', count: 529 },
          { reason: 'some_new_reason', count: 2 }
        ]
      })
    });
    start();

    const wrap = q('.nw-regeneration .gh-dash-table-wrap')!;
    expect(wrap.getAttribute('role')).toBe('region');
    expect(wrap.getAttribute('aria-labelledby')).toBe('nw-skipped-title');
    expect(text(q('#nw-skipped-title'))).toBe('Skipped by the converter');
    expect(Array.from(el.querySelectorAll('.nw-skipped thead th')).map(th => text(th))).toEqual(['Reason', 'Pages']);

    const rows = Array.from(el.querySelectorAll('.nw-skipped tbody > tr'));
    expect(rows.map(r => text(r.querySelector('.nw-skip-reason')))).toEqual([
      'Namespace', 'Formerly converted namespaces', 'Non-English', 'Some new reason'
    ]);
    expect(text(rows[0].querySelector('td[data-label="Pages"]'))).toBe(nf.format(24938));
    expect(el.querySelectorAll('.nw-skipped td:not([data-label])').length).toBe(0);

    const parts = Array.from(rows[1].querySelectorAll('.nw-breakdown > li'));
    expect(parts.map(p => text(p.querySelector('.nw-breakdown-name')))).toEqual(['Source', 'Category']);
    expect(parts.map(p => text(p.querySelector('.nw-breakdown-count')))).toEqual([nf.format(2294), nf.format(854)]);
    expect(rows[0].querySelector('.nw-breakdown')).toBeNull();
  });

  it('says when the wiki folder has no conversion report', () => {
    start();
    expect(text(q('.nw-report-missing'))).toBe('No conversion report in the wiki folder.');
    expect(q('.nw-report-facts')).toBeNull();
  });

  it('shows why the conversion report could not be read', () => {
    current = wiki({ conversionReportError: 'JsonException: Unexpected end of data.' });
    start();
    const error = q('.nw-report-error')!;
    expect(error.classList).toContain('gh-field-error');
    expect(text(error)).toBe('The conversion report could not be read: JsonException: Unexpected end of data.');
    expect(q('.nw-report-missing')).toBeNull();
  });

  it('names skip reasons in words', () => {
    expect(skipReasonLabel('variant_only')).toBe('Variant only');
    expect(skipReasonLabel('non_game')).toBe('Non-game');
    expect(skipReasonLabel('user_talk')).toBe('User talk');
    expect(skipReasonLabel('forum')).toBe('Forum');
  });

  // ---------------------------------------------------------------- namespaces

  it('lists the namespaces most first with their share of the articles', () => {
    current = wiki({ namespaces: [{ namespace: 'Help', articles: 5 }, { namespace: '', articles: 120 }, { namespace: 'nethackwiki', articles: 75 }] });
    start();

    expect(text(q('h4#nw-namespaces-title'))).toBe('Namespaces');
    expect(q('.nw-namespaces .gh-dash-table-wrap')!.getAttribute('aria-labelledby')).toBe('nw-namespaces-title');
    const rows = Array.from(el.querySelectorAll('.nw-namespace-table tbody tr'));
    expect(rows.map(r => text(r.querySelector('td[data-label="Namespace"]')))).toEqual(['article', 'nethackwiki', 'Help']);
    expect(rows.map(r => text(r.querySelector('td[data-label="Articles"]')))).toEqual(['120', '75', '5']);
    expect(text(rows[0].querySelector('.nw-share-text'))).toBe(pf.format(0.6));
    const bar = rows[0].querySelector<HTMLElement>('.gh-dash-bar')!;
    expect(bar.getAttribute('aria-hidden')).toBe('true');
    expect(bar.style.getPropertyValue('--gh-dash-share')).toBe('60.0%');
    expect(el.querySelectorAll('.nw-namespace-table td:not([data-label])').length).toBe(0);
  });

  it('says when no index has been published', () => {
    current = buildCorpusStatus('nethack-wiki');
    start();
    expect(text(q('.nw-namespaces .gh-dash-muted'))).toBe('No index has been published yet.');
    const tiles = Array.from(el.querySelectorAll('.cip-figures > .gh-health-tile'));
    expect(text(tiles[tiles.length - 1].querySelector('dd'))).toBe('—');
  });

  // ---------------------------------------------------------------- how to regenerate, usage, probe

  it('explains how to regenerate the wiki in a disclosure without buttons', () => {
    start();
    const card = q('section[aria-labelledby="nw-howto-title"]')!;
    expect(text(card.querySelector('h4.gh-section-title'))).toBe('How to regenerate');
    const details = card.querySelector<HTMLDetailsElement>('details.gh-disclosure')!;
    expect(details.open).toBe(false);
    expect(text(details.querySelector('summary'))).toBe('Steps to regenerate the wiki');
    const steps = Array.from(details.querySelectorAll('ol > li'));
    expect(steps.length).toBe(4);
    expect(text(steps[0])).toContain('new dated folder under C:\\Backup\\NetHack Wiki');
    const paths = Array.from(details.querySelectorAll('.gh-dash-mono')).map(c => text(c));
    expect(paths).toContain('C:\\Backup\\NetHack Wiki');
    expect(paths).toContain('Overseer/Scripts/NetHackWiki/README.md');
    expect(paths).toContain('C:\\hmp\\nethackwiki');
    expect(text(steps[3])).toBe('Click Re-index wiki on this page.');
    expect(card.querySelector('button')).toBeNull();
  });

  it('shows the NetHack wiki tool usage', () => {
    start();
    expect(q('app-corpus-tool-usage.gh-dash-wide')).not.toBeNull();
    expect(q('#nw-usage-title')).not.toBeNull();
  });

  it('runs nethack_wiki_search in the probe, with no filter select', () => {
    start();
    expect(q('app-corpus-search-probe.gh-dash-wide')).not.toBeNull();
    expect(q('app-corpus-search-probe select')).toBeNull();
    expect(q('#nw-probe-filter')).toBeNull();
    expect(text(q('label[for="nw-probe-query"]'))).toBe('Search terms');

    const probe = vi.spyOn(devtools, 'probeCorpus');
    const input = q<HTMLInputElement>('#nw-probe-query')!;
    input.value = 'Excalibur';
    input.dispatchEvent(new Event('input'));
    q<HTMLButtonElement>('#nw-probe-run')!.click();
    expect(probe).toHaveBeenCalledWith('nethack-wiki', 'Excalibur', null);
  });

  it('heads every card with an h4 section title that labels it', () => {
    current = wiki({ conversionReport: report() });
    start();
    const sections = Array.from(el.querySelectorAll('.gh-dash-grid section.gh-dash-card'));
    expect(sections.length).toBeGreaterThanOrEqual(5);
    for (const section of sections) {
      const id = section.getAttribute('aria-labelledby')!;
      expect(section.querySelector(`h4.gh-section-title#${id}`)).not.toBeNull();
    }
  });

  // ---------------------------------------------------------------- polling and teardown

  it('pauses polling while the page is hidden and polls again when it is shown', () => {
    start();
    setHidden(true);
    vi.advanceTimersByTime(IDLE_POLL_MS * 4);
    expect(statusSpy).toHaveBeenCalledTimes(1);

    setHidden(false);
    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('stops polling, drops a start request in flight and clears the linger when destroyed', () => {
    current = wiki({ currentJob: running() });
    start();
    current = wiki({ recentJobs: [finished()] });
    vi.advanceTimersByTime(ACTIVE_POLL_MS);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    expect(component.endedJob).not.toBeNull();

    let unsubscribed = 0;
    vi.spyOn(devtools, 'startCorpusReindex').mockReturnValue(new Observable<never>(() => () => unsubscribed++));
    reindexButton().click();
    const calls = statusSpy.mock.calls.length;

    fixture.destroy();
    expect(unsubscribed).toBe(1);
    vi.advanceTimersByTime(IDLE_POLL_MS * 4);
    expect(statusSpy).toHaveBeenCalledTimes(calls);
    expect(component.endedJob).not.toBeNull();
  });
});
