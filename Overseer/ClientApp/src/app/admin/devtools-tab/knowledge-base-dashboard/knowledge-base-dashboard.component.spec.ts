import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, of, throwError } from 'rxjs';

import {
  AdminDevtoolsService,
  KnowledgeArticleInfo,
  KnowledgeArticleSource,
  KnowledgeBaseStatus
} from '../../../services/admin-devtools.service';
import { IDLE_POLL_MS, ACTIVE_POLL_MS } from '../shared/dashboard-poller';
import {
  buildCorpusIndexInfo,
  buildCorpusJob,
  buildCorpusStatus,
  configureAdminTestBed
} from '../../admin.component.testing';
import { KnowledgeBaseDashboardComponent } from './knowledge-base-dashboard.component';

const nf = new Intl.NumberFormat();
const oneDecimal = new Intl.NumberFormat(undefined, { minimumFractionDigits: 1, maximumFractionDigits: 1 });
const LINGER = KnowledgeBaseDashboardComponent.ENDED_JOB_LINGER_MS;

function article(topic: string, overrides: Partial<KnowledgeArticleInfo> = {}): KnowledgeArticleInfo {
  return {
    topic,
    title: topic.charAt(0).toUpperCase() + topic.slice(1),
    hasTitle: true,
    hasSummary: true,
    bytes: 1024,
    lastWriteUtc: '2026-10-17T08:00:00Z',
    ...overrides
  };
}

/** In the server's order, which is not the table's default (topic, ascending). */
const ARTICLES: KnowledgeArticleInfo[] = [
  article('prayer', { bytes: 4096, lastWriteUtc: '2026-10-15T08:00:00Z' }),
  article('altars', { bytes: 2048, lastWriteUtc: '2026-10-17T08:00:00Z' }),
  article('engraving', { title: null, hasTitle: false, hasSummary: false, bytes: 8192, lastWriteUtc: '2026-10-16T08:00:00Z' })
];

function kbStatus(overrides: Partial<KnowledgeBaseStatus> = {}): KnowledgeBaseStatus {
  return buildCorpusStatus('knowledge-base', {
    index: buildCorpusIndexInfo({ fileCount: ARTICLES.length, maxFileSizeKB: null }),
    currentHeadSha: '0123456789abcdef0123',
    articles: ARTICLES,
    topicListChars: 1234,
    ...overrides
  });
}

const SOURCE: KnowledgeArticleSource = {
  topic: 'altars',
  title: 'Altars',
  source: '---\ntitle: Altars\nsummary: Identifying the alignment of an altar.\n---\n\nDrop an item on an altar to learn its curse status.'
};

/** A dialog's close event is queued as a task; wait for the event itself. */
function nextClose(dialog: HTMLDialogElement): Promise<void> {
  return new Promise<void>(resolve => dialog.addEventListener('close', () => resolve(), { once: true }));
}

/** Lets awaited promises settle; the timers are fake, so no timeout can be awaited. */
async function flushMicrotasks(): Promise<void> {
  for (let i = 0; i < 50; i++) {
    await Promise.resolve();
  }
}

describe('KnowledgeBaseDashboardComponent', () => {
  let fixture: ComponentFixture<KnowledgeBaseDashboardComponent>;
  let el: HTMLElement;
  let devtools: AdminDevtoolsService;
  let statusSpy: Mock;
  let current: KnowledgeBaseStatus;
  let hidden = false;

  beforeEach(async () => {
    ({ devtoolsService: devtools } = await configureAdminTestBed(KnowledgeBaseDashboardComponent));
    hidden = false;
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => hidden });
    current = kbStatus();
    statusSpy = vi.spyOn(devtools, 'getCorpusStatus').mockImplementation(
      (() => of(current)) as unknown as AdminDevtoolsService['getCorpusStatus']) as unknown as Mock;

    vi.useFakeTimers();
    fixture = TestBed.createComponent(KnowledgeBaseDashboardComponent);
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => {
    for (const dialog of Array.from(el.querySelectorAll('dialog'))) {
      if (dialog.open) {
        dialog.close();
      }
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

  const q = <T extends Element = HTMLElement>(selector: string) => el.querySelector<T>(selector);
  const text = (node: Element | null) => (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const button = (id: string) => q<HTMLButtonElement>(`#${id}`)!;
  const rowTopics = () => Array.from(el.querySelectorAll('.kb-table tbody th[scope="row"]')).map(th => text(th));
  const sortButton = (label: string) => Array.from(el.querySelectorAll<HTMLButtonElement>('.kb-table thead .gh-th-sort'))
    .find(b => text(b) === label)!;
  const previewButton = (topic: string) => q<HTMLButtonElement>(`.kb-table button[aria-label="Preview article ${topic}"]`)!;
  const previewDialog = () => q<HTMLDialogElement>('#kb-preview-dialog')!;
  const confirmDialog = () => q<HTMLDialogElement>('app-reindex-confirm-dialog dialog')!;

  function typeFilter(id: string, value: string): void {
    const input = q<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function chooseFilter(id: string, value: string): void {
    const select = q<HTMLSelectElement>(`#${id}`)!;
    select.value = value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function conflict(status: number, error: object): HttpErrorResponse {
    return new HttpErrorResponse({ status, error });
  }

  // ---------------------------------------------------------------- banner and figures

  it('renders the panel with the Ready banner, the Reload action and the knowledge-base tiles', () => {
    start();

    const banner = q('.gh-dash-banner')!;
    expect(banner.getAttribute('role')).toBe('status');
    expect(banner.getAttribute('data-status')).toBe('ok');
    expect(text(banner)).toContain('Ready');
    expect(text(banner)).toContain(`All ${nf.format(3)} articles are loaded.`);

    const reload = button('kb-reindex-btn');
    expect(text(reload)).toBe('Reload articles');
    expect(reload.getAttribute('aria-disabled')).toBeNull();
    expect(text(q('#kb-reindex-tip'))).toBe(KnowledgeBaseDashboardComponent.RELOAD_TIP);

    expect(text(q('.cip-figures .kb-tile-prompt dt'))).toBe('In the system prompt');
    expect(text(q('.cip-figures .kb-tile-prompt dd'))).toBe(
      `${nf.format(1234)} characters ≈ ${nf.format(309)} tokens, estimated`);
    expect(text(q('.cip-figures .kb-tile-missing-summary dd'))).toBe('1');
    expect(text(q('.cip-figures .kb-tile-shadowed dd'))).toBe('0');
  });

  it('names the reason when KbPath is not configured', () => {
    current = kbStatus({ configured: false, path: null, pathExists: false, index: null, articles: [] });
    start();

    expect(text(q('.gh-dash-banner'))).toContain('Not configured');
    expect(text(q('.gh-dash-banner'))).toContain('KbPath is not set');
    expect(button('kb-reindex-btn').getAttribute('aria-disabled')).toBe('true');
    expect(text(q('#kb-actions-reason'))).toBe('KbPath is not configured.');
  });

  it('shows a failed last pass in the banner', () => {
    current = kbStatus({ recentJobs: [buildCorpusJob({ outcome: 'Failed', message: 'The KbPath folder does not exist.' })] });
    start();

    const banner = q('.gh-dash-banner')!;
    expect(banner.getAttribute('data-status')).toBe('error');
    expect(text(banner)).toContain('Last pass failed');
    expect(text(banner)).toContain('The KbPath folder does not exist.');
  });

  it('shows a failed poll with the time of the next attempt', () => {
    statusSpy.mockImplementation(() => throwError(() => new HttpErrorResponse({ status: 500 })));
    start();

    expect(text(q('#kb-load-error'))).toBe('The knowledge base status could not be loaded (HTTP 500). Trying again in 5 s.');
  });

  // ---------------------------------------------------------------- reload

  it('starts a reload and polls at once', () => {
    start();
    const reindex = vi.spyOn(devtools, 'startCorpusReindex');

    button('kb-reindex-btn').click();
    expect(reindex).toHaveBeenCalledWith('knowledge-base', false);
    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('asks first while a GnollBench run executes, and resends acknowledged on Reload anyway', async () => {
    start();
    const message = 'A GnollBench run is executing and reads the knowledge base.';
    const reindex = vi.spyOn(devtools, 'startCorpusReindex')
      .mockReturnValueOnce(throwError(() => conflict(409, { code: 'benchmarkRunActive', message })))
      .mockReturnValueOnce(of({ jobId: 'corpus-job-2' }));

    button('kb-reindex-btn').click();
    fixture.detectChanges();

    expect(confirmDialog().open).toBe(true);
    expect(text(q('#kb-reindex-confirm-message'))).toBe(message);
    expect(text(q('#kb-reindex-confirm-ok'))).toBe('Reload anyway');
    expect(q('#kb-action-message')).toBeNull();

    const closing = nextClose(confirmDialog());
    button('kb-reindex-confirm-ok').click();
    await closing;

    expect(reindex).toHaveBeenCalledTimes(2);
    expect(reindex).toHaveBeenNthCalledWith(1, 'knowledge-base', false);
    expect(reindex).toHaveBeenNthCalledWith(2, 'knowledge-base', true);
    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('does not resend when the GnollBench warning is canceled', async () => {
    start();
    const reindex = vi.spyOn(devtools, 'startCorpusReindex').mockReturnValueOnce(
      throwError(() => conflict(409, { code: 'benchmarkRunActive', message: 'A GnollBench run is executing.' })));

    button('kb-reindex-btn').click();
    fixture.detectChanges();
    const closing = nextClose(confirmDialog());
    button('kb-reindex-confirm-cancel').click();
    await closing;

    expect(reindex).toHaveBeenCalledTimes(1);
    expect(confirmDialog().open).toBe(false);
  });

  it('shows a running pass as a warning and polls for it', () => {
    start();
    vi.spyOn(devtools, 'startCorpusReindex').mockReturnValue(
      throwError(() => conflict(409, { code: 'alreadyRunning', message: 'A knowledge base pass is already running.' })));

    button('kb-reindex-btn').click();
    fixture.detectChanges();

    const message = q('#kb-action-message')!;
    expect(message.classList).toContain('gh-field-warning');
    expect(text(message)).toBe('A knowledge base pass is already running.');
    expect(message.closest('[role="status"]')).not.toBeNull();
    expect(button('kb-reindex-btn').getAttribute('aria-describedby')).toContain('kb-action-message');
    expect(confirmDialog().open).toBe(false);

    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(2);
  });

  it('shows a 400 as an error and does not poll', () => {
    start();
    vi.spyOn(devtools, 'startCorpusReindex').mockReturnValue(
      throwError(() => conflict(400, { message: 'The KbPath folder does not exist.' })));

    button('kb-reindex-btn').click();
    fixture.detectChanges();

    const message = q('#kb-action-message')!;
    expect(message.classList).toContain('gh-field-error');
    expect(text(message)).toBe('The KbPath folder does not exist.');
    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(1);
  });

  it('keeps a finished pass on the progress card for 10 s with its outcome', () => {
    current = kbStatus({ currentJob: buildCorpusJob({ outcome: 'Running', finishedAtUtc: null, phase: 'ReadingFiles', message: null }) });
    start();
    expect(q('.gh-dash-banner')!.getAttribute('data-status')).toBe('running');
    expect(button('kb-reindex-btn').getAttribute('aria-disabled')).toBe('true');

    current = kbStatus({ recentJobs: [buildCorpusJob()] });
    vi.advanceTimersByTime(ACTIVE_POLL_MS);
    fixture.detectChanges();

    const card = q('.cip-progress')!;
    expect(card).not.toBeNull();
    expect(text(card)).toContain('Succeeded');

    vi.advanceTimersByTime(LINGER - 1);
    fixture.detectChanges();
    expect(q('.cip-progress')).not.toBeNull();
    vi.advanceTimersByTime(1);
    fixture.detectChanges();
    expect(q('.cip-progress')).toBeNull();
  });

  // ---------------------------------------------------------------- articles table

  it('lists the articles sorted by topic, with a row header and a labeled cell per column', () => {
    start();

    const section = q('section.kb-articles')!;
    expect(section.getAttribute('aria-labelledby')).toBe('kb-articles-title');
    expect(q('#kb-articles-title')!.matches('h4.gh-section-title')).toBe(true);
    const wrap = q('#kb-articles-wrap')!;
    expect(wrap.getAttribute('role')).toBe('region');
    expect(wrap.getAttribute('tabindex')).toBe('0');

    expect(rowTopics()).toEqual(['altars', 'engraving', 'prayer']);
    expect(el.querySelectorAll('.kb-table tbody td:not([data-label])').length).toBe(0);
    const labels = Array.from(el.querySelectorAll('.kb-table tbody tr:first-child td')).map(td => td.getAttribute('data-label'));
    expect(labels).toEqual(['Title', 'Summary', 'Size', 'Modified', 'Preview']);

    const engraving = el.querySelectorAll('.kb-table tbody tr')[1];
    expect(text(engraving.querySelector('td[data-label="Title"]'))).toBe('Missing');
    expect(engraving.querySelector('td[data-label="Summary"] .gh-health-status')!.getAttribute('data-status')).toBe('warning');
    expect(text(el.querySelector('.kb-table tbody tr td[data-label="Size"]'))).toBe(`${oneDecimal.format(2)} KB`);

    expect(sortButton('Topic').closest('th')!.getAttribute('aria-sort')).toBe('ascending');
    expect(el.querySelectorAll('app-table-pager').length).toBe(1);
    const status = q('.gh-pager-status')!;
    expect(status.getAttribute('role')).toBe('status');
    expect(text(status)).toBe('Showing 1–3 of 3 articles');
  });

  it('sorts by a column header, largest first, then flips', () => {
    start();

    sortButton('Size').click();
    fixture.detectChanges();
    expect(rowTopics()).toEqual(['engraving', 'prayer', 'altars']);
    expect(sortButton('Size').closest('th')!.getAttribute('aria-sort')).toBe('descending');
    expect(sortButton('Topic').closest('th')!.hasAttribute('aria-sort')).toBe(false);

    sortButton('Size').click();
    fixture.detectChanges();
    expect(rowTopics()).toEqual(['altars', 'prayer', 'engraving']);

    sortButton('Modified').click();
    fixture.detectChanges();
    expect(rowTopics()).toEqual(['altars', 'engraving', 'prayer']);
  });

  it('filters by topic and title text, and Clear filters shows every article again', () => {
    start();
    expect(q('#kb-articles-clear')).toBeNull();
    expect(text(q('label[for="kb-filter-topic"]'))).toBe('Filter by topic');
    expect(text(q('label[for="kb-filter-title"]'))).toBe('Filter by title');

    typeFilter('kb-filter-topic', 'PR');
    expect(rowTopics()).toEqual(['prayer']);
    expect(text(q('.gh-pager-status'))).toBe('Showing 1–1 of 1 articles, filtered from 3');

    typeFilter('kb-filter-topic', '');
    typeFilter('kb-filter-title', 'alt');
    expect(rowTopics()).toEqual(['altars']);

    button('kb-articles-clear').click();
    fixture.detectChanges();
    expect(rowTopics()).toEqual(['altars', 'engraving', 'prayer']);
    expect(q<HTMLInputElement>('#kb-filter-title')!.value).toBe('');
    expect(q('#kb-articles-clear')).toBeNull();
  });

  it('filters by whether the summary and the title are present', () => {
    start();
    const options = (id: string) => Array.from(q<HTMLSelectElement>(`#${id}`)!.options).map(o => o.value);
    expect(options('kb-filter-has-summary')).toEqual(['', 'present', 'missing']);
    expect(options('kb-filter-has-title')).toEqual(['', 'present', 'missing']);

    chooseFilter('kb-filter-has-summary', 'missing');
    expect(rowTopics()).toEqual(['engraving']);
    chooseFilter('kb-filter-has-summary', 'present');
    expect(rowTopics()).toEqual(['altars', 'prayer']);

    chooseFilter('kb-filter-has-summary', '');
    chooseFilter('kb-filter-has-title', 'missing');
    expect(rowTopics()).toEqual(['engraving']);
    chooseFilter('kb-filter-has-title', 'present');
    expect(rowTopics()).toEqual(['altars', 'prayer']);
  });

  it('says when the filters hide every article, and offers Clear filters', () => {
    start();
    typeFilter('kb-filter-topic', 'unicorn');

    expect(rowTopics()).toEqual([]);
    expect(text(q('#kb-articles-no-matches span'))).toBe('No article matches these filters.');
    expect(text(q('#kb-articles-no-matches-clear'))).toBe('Clear filters');
    expect(text(q('.gh-pager-status'))).toBe('No articles match these filters');
    expect(q('#kb-articles-empty')).toBeNull();

    button('kb-articles-no-matches-clear').click();
    fixture.detectChanges();
    expect(rowTopics()).toEqual(['altars', 'engraving', 'prayer']);
    expect(q('#kb-articles-no-matches')).toBeNull();
    expect(q<HTMLInputElement>('#kb-filter-topic')!.value).toBe('');
  });

  it('says when no article is loaded, without a table', () => {
    current = kbStatus({ articles: [], index: buildCorpusIndexInfo({ fileCount: 0, maxFileSizeKB: null }) });
    start();

    expect(text(q('#kb-articles-empty'))).toBe('No article is loaded.');
    expect(q('.kb-table')).toBeNull();
    expect(q('app-table-pager')).toBeNull();
    expect(q('#kb-articles-no-matches')).toBeNull();
  });

  it('names the missing Content folder when no article is loaded', () => {
    current = kbStatus({ articles: [], contentPathExists: false, index: buildCorpusIndexInfo({ fileCount: 0, maxFileSizeKB: null }) });
    start();

    expect(text(q('#kb-articles-empty'))).toBe('No article is loaded: the KbPath folder has no Content folder.');
  });

  // ---------------------------------------------------------------- preview

  it('gives each row an icon-only Preview button named after its article', () => {
    start();
    const preview = previewButton('altars');
    expect(preview.type).toBe('button');
    expect(preview.classList).toContain('action-btn');
    expect(preview.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
    expect(text(preview)).toBe('');
    const tipId = preview.getAttribute('interestfor')!;
    expect(text(q(`#${tipId}`))).toBe('Preview article');
    expect(q(`#${tipId}`)!.getAttribute('popover')).toBe('hint');
    expect(preview.hasAttribute('title')).toBe(false);
  });

  it('opens the preview on the row button and shows the article source as plain text', () => {
    start();
    const articleSpy = vi.spyOn(devtools, 'getKnowledgeArticle').mockReturnValue(
      of({ ...SOURCE, source: SOURCE.source + '\n<b>not markup</b>' }));

    previewButton('altars').click();
    fixture.detectChanges();

    expect(articleSpy).toHaveBeenCalledWith('altars');
    const dialog = previewDialog();
    expect(dialog.open).toBe(true);
    expect(dialog.matches(':modal')).toBe(true);
    expect(dialog.classList).toContain('gh-dialog');
    expect(dialog.getAttribute('aria-labelledby')).toBe('kb-preview-title');
    expect(dialog.getAttribute('closedby')).toBe('any');
    expect(text(q('#kb-preview-title'))).toBe('altars');
    expect(text(q('#kb-preview-subtitle'))).toBe('Altars');
    expect(document.activeElement).toBe(q('#kb-preview-title'));

    const pre = q<HTMLPreElement>('#kb-preview-source')!;
    expect(pre.classList).toContain('gh-dash-pre');
    expect(pre.textContent).toBe(SOURCE.source + '\n<b>not markup</b>');
    expect(pre.querySelector('b')).toBeNull();

    const close = button('kb-preview-close');
    expect(close.classList).toContain('btn-icon-action');
    expect(close.getAttribute('aria-label')).toBe('Close article preview');
  });

  it('shows a loading line until the article arrives', () => {
    start();
    const pending = new Subject<KnowledgeArticleSource>();
    vi.spyOn(devtools, 'getKnowledgeArticle').mockReturnValue(pending.asObservable());

    previewButton('altars').click();
    fixture.detectChanges();
    expect(text(q('#kb-preview-status'))).toBe('Loading the article…');
    expect(q('#kb-preview-status')!.getAttribute('role')).toBe('status');
    expect(q('#kb-preview-source')).toBeNull();

    pending.next(SOURCE);
    fixture.detectChanges();
    expect(text(q('#kb-preview-status'))).toBe('');
    expect(q('#kb-preview-source')!.textContent).toBe(SOURCE.source);
  });

  it('shows an error line when the article is gone', () => {
    start();
    vi.spyOn(devtools, 'getKnowledgeArticle').mockReturnValue(
      throwError(() => conflict(404, { message: 'No such topic.' })));

    previewButton('altars').click();
    fixture.detectChanges();

    const error = q('#kb-preview-error')!;
    expect(error.classList).toContain('gh-field-error');
    expect(text(error)).toBe('No article has the topic altars now. The articles may have been reloaded since the list was loaded.');
    expect(q('#kb-preview-source')).toBeNull();
  });

  it('copies the full article source', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true, writable: true });
    try {
      start();
      vi.spyOn(devtools, 'getKnowledgeArticle').mockReturnValue(of(SOURCE));
      previewButton('altars').click();
      fixture.detectChanges();

      const copy = button('kb-preview-copy-btn');
      expect(copy.classList).toContain('action-btn');
      expect(copy.getAttribute('aria-label')).toBe('Copy article source');
      expect(copy.getAttribute('interestfor')).toBe('kb-preview-copy-tip');
      copy.click();
      await flushMicrotasks();
      fixture.detectChanges();

      expect(writeText).toHaveBeenCalledWith(SOURCE.source);
      expect(text(q('.kb-preview-copy-status'))).toBe('Article source copied.');
    } finally {
      delete (navigator as { clipboard?: unknown }).clipboard;
      if (original) {
        Object.defineProperty(navigator, 'clipboard', original);
      }
    }
  });

  it('returns the focus to the row button when the preview closes by its Close button', async () => {
    start();
    vi.spyOn(devtools, 'getKnowledgeArticle').mockReturnValue(of(SOURCE));
    const trigger = previewButton('altars');
    trigger.click();
    fixture.detectChanges();

    const closing = nextClose(previewDialog());
    button('kb-preview-close').click();
    await closing;

    expect(previewDialog().open).toBe(false);
    expect(document.activeElement).toBe(trigger);
  });

  it('stops loading and returns the focus when the platform closes the preview, as Escape does', async () => {
    start();
    const pending = new Subject<KnowledgeArticleSource>();
    vi.spyOn(devtools, 'getKnowledgeArticle').mockReturnValue(pending.asObservable());
    const trigger = previewButton('prayer');
    trigger.click();
    fixture.detectChanges();
    expect(pending.observed).toBe(true);

    const closing = nextClose(previewDialog());
    previewDialog().close();
    await closing;

    expect(pending.observed).toBe(false);
    expect(document.activeElement).toBe(trigger);
  });

  // ---------------------------------------------------------------- health checks

  it('lists each problem with a word and a glyph', () => {
    current = kbStatus({
      shadowedTopics: [{ topic: 'Prayer', path: 'Content/Prayer.md', shadowedByPath: 'Content/prayer.md' }],
      index: buildCorpusIndexInfo({
        fileCount: 3, maxFileSizeKB: null,
        failedFiles: [{ path: 'Content/broken.md', error: 'IOException: The file is locked.' }],
        failedFileCount: 3
      })
    });
    start();

    const section = q('section.kb-health')!;
    expect(section.getAttribute('aria-labelledby')).toBe('kb-health-title');
    expect(text(q('#kb-health-title'))).toBe('Health checks');

    const items = Array.from(el.querySelectorAll<HTMLElement>('#kb-health-list > li'));
    expect(items.map(li => li.dataset['check'])).toEqual(['missing-title', 'missing-summary', 'shadowed', 'unreadable']);
    const word = (li: HTMLElement) => text(li.querySelector('app-health-status'));
    const tone = (li: HTMLElement) => li.querySelector('.gh-health-status')!.getAttribute('data-status');
    expect(items.map(word)).toEqual(['Missing title', 'Missing summary', 'Shadowed topic', 'Unreadable']);
    expect(items.map(tone)).toEqual(['warning', 'warning', 'warning', 'error']);
    expect(items.every(li => !!li.querySelector('.gh-health-status svg[aria-hidden="true"]'))).toBe(true);

    expect(text(items[0].querySelector('.kb-health-topics'))).toBe('engraving');
    expect(text(items[2])).toContain('Prayer');
    expect(text(items[2])).toContain('Kept: Content/prayer.md');
    expect(text(items[2])).toContain('Shadowed: Content/Prayer.md');
    expect(text(items[3])).toContain('Content/broken.md');
    expect(text(items[3])).toContain('IOException: The file is locked.');
    expect(text(items[3].querySelector('.kb-health-more'))).toBe('and 2 more');

    expect(text(q('.cip-figures .kb-tile-shadowed dd'))).toBe('1');
  });

  it('says No problems found. when every check passes', () => {
    current = kbStatus({ articles: [article('altars'), article('prayer')] });
    start();

    expect(q('#kb-health-list')).toBeNull();
    const none = q('.kb-health-none')!;
    expect(text(none)).toBe('No problems found.');
    expect(none.querySelector('.gh-health-status')!.getAttribute('data-status')).toBe('ok');
  });

  // ---------------------------------------------------------------- tool usage and probe

  it('runs the probe with the Topic label and the topics as suggestions, beside the tool usage card', () => {
    start();

    const usage = q('app-corpus-tool-usage')!;
    expect(usage.classList).toContain('gh-dash-wide');
    const probe = q('app-corpus-search-probe')!;
    expect(probe.classList).toContain('gh-dash-wide');
    expect(text(q('label[for="kb-probe-query"]'))).toBe('Topic');
    const suggestions = Array.from(el.querySelectorAll<HTMLOptionElement>('#kb-probe-topics option')).map(o => o.value);
    expect(suggestions).toEqual(['prayer', 'altars', 'engraving']);
  });

  // ---------------------------------------------------------------- polling and teardown

  it('polls every 15 s while idle and pauses while the page is hidden', () => {
    start();
    expect(statusSpy).toHaveBeenCalledWith('knowledge-base');
    vi.advanceTimersByTime(IDLE_POLL_MS);
    expect(statusSpy).toHaveBeenCalledTimes(2);

    setHidden(true);
    vi.advanceTimersByTime(IDLE_POLL_MS * 4);
    expect(statusSpy).toHaveBeenCalledTimes(2);

    setHidden(false);
    vi.advanceTimersByTime(0);
    expect(statusSpy).toHaveBeenCalledTimes(3);
  });

  it('stops polling, the linger and the preview request when destroyed', () => {
    current = kbStatus({ currentJob: buildCorpusJob({ outcome: 'Running', finishedAtUtc: null }) });
    start();
    current = kbStatus({ recentJobs: [buildCorpusJob()] });
    vi.advanceTimersByTime(ACTIVE_POLL_MS);
    fixture.detectChanges();

    const pending = new Subject<KnowledgeArticleSource>();
    vi.spyOn(devtools, 'getKnowledgeArticle').mockReturnValue(pending.asObservable());
    previewButton('altars').click();
    fixture.detectChanges();
    const calls = statusSpy.mock.calls.length;

    fixture.destroy();
    expect(pending.observed).toBe(false);
    vi.advanceTimersByTime(IDLE_POLL_MS * 4);
    expect(statusSpy).toHaveBeenCalledTimes(calls);
  });
});
