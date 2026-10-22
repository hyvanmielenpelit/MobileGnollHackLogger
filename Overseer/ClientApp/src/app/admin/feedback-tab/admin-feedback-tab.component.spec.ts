import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { provideLocationMocks } from '@angular/common/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Subject, of, throwError } from 'rxjs';
import {
  AdminFeedbackCounts,
  AdminFeedbackIssue,
  AdminFeedbackIssuePage,
  AdminFeedbackIssuesQuery,
  AdminFeedbackOccurrence,
  AdminFeedbackOccurrencePage,
  AdminFeedbackService,
  AdminFeedbackSwitches
} from '../../services/admin-feedback.service';
import { FilterFacetComponent } from '../../shared/data-table/filter-facet.component';
import { AdminFeedbackRange, AdminPageStore } from '../admin-page.store';
import { adminTestRoutes } from '../admin.component.testing';
import {
  AdminFeedbackTabComponent,
  FEEDBACK_VIEW_STORAGE_KEY,
  feedbackDateText,
  feedbackDateTimeText,
  feedbackRangeDates
} from './admin-feedback-tab.component';

const settle = (ms = 0): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

function clearStorage(): void {
  try {
    localStorage.removeItem(FEEDBACK_VIEW_STORAGE_KEY);
  } catch {
    // Nothing stored.
  }
}

function issue(id: number, overrides: Partial<AdminFeedbackIssue> = {}): AdminFeedbackIssue {
  return {
    id, category: 'missing_information', subject: id === 1 ? 'Dilithium crystal' : `Subject ${id}`,
    summary: 'The wiki has no page for it.',
    firstSeenUtc: '2026-09-02T08:00:00Z', lastSeenUtc: '2026-10-20T12:30:00Z',
    occurrenceCount: 12, retainedCount: 12, distinctUserCount: 3,
    modelIds: ['claude-x', 'gpt-x'], regressionCount: 0, lastRegressionUtc: null,
    latestExpectedLocation: 'wiki page Dilithium crystal', latestRelatedTools: ['wiki_search', 'item_lookup'],
    latestSuspectedCause: 'corpus_or_tool_limit', latestAppVersion: '1.4.2+abc',
    status: 'new', triageNote: null, triagedByUserName: null, triagedUtc: null,
    ...overrides
  };
}

function issueRange(from: number, count: number): AdminFeedbackIssue[] {
  return Array.from({ length: count }, (_, i) => issue(from + i));
}

function occurrence(id: number, overrides: Partial<AdminFeedbackOccurrence> = {}): AdminFeedbackOccurrence {
  return {
    id, createdUtc: '2026-10-20T12:30:00Z', subject: 'Dilithium crystal', summary: 'No page.',
    details: null, searchedFor: 'wiki_search "dilithium"', expectedLocation: 'wiki page Dilithium crystal',
    relatedTools: ['wiki_search'], noticedVia: null, suspectedCause: 'did_not_verify', suspectedCauseNote: 'I answered first.',
    truncated: false, redacted: false, chatSessionId: 812, turnUserMessageId: 4410,
    provider: 'Anthropic', modelId: 'claude-x', appVersion: '1.4.2+abc', userName: 'alice',
    ...overrides
  };
}

function page(rows: AdminFeedbackIssue[], totalCount = rows.length): AdminFeedbackIssuePage {
  return { totalCount, rows };
}

function counts(overrides: Partial<AdminFeedbackCounts> = {}): AdminFeedbackCounts {
  return { new: 0, acknowledged: 0, resolved: 0, regressed: 0, dismissed: 0, ...overrides };
}

/** A custom range, which keeps its dates as given. */
const CUSTOM_RANGE: AdminFeedbackRange = { timeSpan: 'custom', start: '2026-09-21', end: '2026-10-21' };

describe('AdminFeedbackTabComponent', () => {
  let fixture: ComponentFixture<AdminFeedbackTabComponent>;
  let component: AdminFeedbackTabComponent;
  let el: HTMLElement;
  let service: AdminFeedbackService;
  let store: AdminPageStore;
  let router: Router;
  let issuesSpy: Mock;
  let switchSpy: Mock;
  let countsSpy: Mock;
  let occurrencesSpy: Mock;
  /** What the next issues request returns; the empty-state count request (take 1) gets `rangePage`. */
  let nextPage: AdminFeedbackIssuePage;
  let rangePage: AdminFeedbackIssuePage;
  let switchState: AdminFeedbackSwitches;

  beforeEach(async () => {
    clearStorage();
    await TestBed.configureTestingModule({
      imports: [AdminFeedbackTabComponent],
      providers: [provideRouter(adminTestRoutes()), provideLocationMocks(), provideHttpClient(), provideHttpClientTesting(), AdminPageStore]
    }).compileComponents();

    service = TestBed.inject(AdminFeedbackService);
    store = TestBed.inject(AdminPageStore);
    router = TestBed.inject(Router);
    nextPage = page([issue(1)]);
    rangePage = page([], 0);
    switchState = {
      chat: { enabled: true, setAtUtc: '2026-10-01T09:00:00Z', setByUserName: 'tommi', seeded: false }
    };
    issuesSpy = vi.spyOn(service, 'getIssues').mockImplementation(
      (query?: AdminFeedbackIssuesQuery) => of(query?.take === 1 ? rangePage : nextPage));
    switchSpy = vi.spyOn(service, 'getSwitches').mockImplementation(() => of(switchState));
    countsSpy = vi.spyOn(service, 'getCounts').mockReturnValue(of(counts()));
    vi.spyOn(service, 'getModels').mockReturnValue(of(['gpt-x', 'claude-x']));
    occurrencesSpy = vi.spyOn(service, 'getOccurrences').mockReturnValue(of({ totalCount: 0, rows: [] }));
  });

  afterEach(() => {
    if (fixture) {
      el.querySelectorAll('dialog').forEach(dialog => {
        if (dialog.open) dialog.close();
      });
      el.querySelectorAll<HTMLElement>('[popover]').forEach(popover => {
        try { popover.hidePopover(); } catch { /* already closed */ }
      });
      fixture.destroy();
    }
    clearStorage();
  });

  async function create(url = '/admin/feedback', range: AdminFeedbackRange = CUSTOM_RANGE,
                        countsValue: AdminFeedbackCounts | null = null): Promise<void> {
    await router.navigateByUrl(url);
    store.feedbackRange = { ...range };
    store.feedbackCounts = countsValue;
    fixture = TestBed.createComponent(AdminFeedbackTabComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  }

  /** The list requests, without the empty-state count request. */
  const listQueries = (): AdminFeedbackIssuesQuery[] =>
    issuesSpy.mock.calls.map(call => call[0] as AdminFeedbackIssuesQuery).filter(query => query.take !== 1);
  const lastQuery = (): AdminFeedbackIssuesQuery => listQueries()[listQueries().length - 1];
  const q = <T extends HTMLElement = HTMLElement>(selector: string): T | null => el.querySelector<T>(selector);
  const cards = (): HTMLElement[] => Array.from(el.querySelectorAll<HTMLElement>('article.tl-fb-card'));
  const status = (): string => textOf(q('#tl-fb-status'));
  const facet = (id: string): FilterFacetComponent | undefined => fixture.debugElement
    .queryAll(By.directive(FilterFacetComponent))
    .map(d => d.componentInstance as FilterFacetComponent)
    .find(f => f.facetId === id);
  const queryParams = () => router.parseUrl(router.url).queryParams;

  function choose(facetId: string, values: string[]): void {
    facet(facetId)!.selectedChange.emit(values);
    fixture.detectChanges();
  }

  function typeInto(input: HTMLInputElement | HTMLTextAreaElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  function selectValue(select: HTMLSelectElement, value: string): void {
    select.value = value;
    select.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
  }

  function openOccurrences(card: HTMLElement): HTMLDetailsElement {
    const details = card.querySelector('details.tl-fb-occurrences') as HTMLDetailsElement;
    details.open = true;
    details.dispatchEvent(new Event('toggle'));
    fixture.detectChanges();
    return details;
  }

  describe('dates', () => {
    it('reads a local day as 21 Oct 2026 and adds the local time as 14:02', () => {
      const local = new Date(2026, 9, 21, 14, 2).toISOString();
      expect(feedbackDateText(local)).toBe('21 Oct 2026');
      expect(feedbackDateTimeText(local)).toBe('21 Oct 2026 14:02');
      expect(feedbackDateText(null)).toBe('');
    });

    it('turns each preset into local days ending today, All time into no bound', () => {
      const today = new Date(2026, 9, 22, 15, 0);
      expect(feedbackRangeDates('all', today)).toEqual({ start: '', end: '' });
      expect(feedbackRangeDates('7d', today)).toEqual({ start: '2026-10-15', end: '2026-10-22' });
      expect(feedbackRangeDates('30d', today)).toEqual({ start: '2026-09-22', end: '2026-10-22' });
      expect(feedbackRangeDates('90d', today)).toEqual({ start: '2026-07-24', end: '2026-10-22' });
      expect(feedbackRangeDates('1y', today)).toEqual({ start: '2025-10-22', end: '2026-10-22' });
      expect(feedbackRangeDates('custom', today)).toEqual({ start: '2026-09-22', end: '2026-10-22' });
    });
  });

  describe('header', () => {
    const rangeSelect = () => q<HTMLSelectElement>('#fb-range-select')!;

    it('is a sticky header titled Feedback, with the date range, its (i) and a Refresh icon button', async () => {
      await create();

      const head = q('.tab-header-row')!;
      expect(textOf(head.querySelector('h3'))).toBe('Feedback');
      expect(getComputedStyle(head).position).toBe('sticky');
      expect(textOf(q('label[for="fb-range-select"]'))).toBe('Date range');
      expect(rangeSelect().getAttribute('aria-describedby')).toBe('fb-range-tip');
      expect(textOf(q('#fb-range-tip'))).toBe(
        'Shows the issues reported at least once in this range. The counts on the tab are over all dates.');
      const refresh = q<HTMLButtonElement>('#fb-refresh-btn')!;
      expect(refresh.getAttribute('aria-label')).toBe('Refresh feedback');
      expect(refresh.getAttribute('interestfor')).toBe('fb-refresh-tip');
      expect(textOf(q('#fb-refresh-tip'))).toBe('Refresh');
      expect(el.querySelector('.gh-dash-note')).toBeNull();
    });

    it('offers six ranges with All time selected by default, and fetches without dates', async () => {
      await create(undefined, { timeSpan: 'all', start: '', end: '' });

      expect(Array.from(rangeSelect().options).map(o => o.textContent?.trim())).toEqual(
        ['All time', 'Last 7 days', 'Last 30 days', 'Last 90 days', 'Last year', 'Custom date range']);
      expect(rangeSelect().value).toBe('all');
      expect(lastQuery().startDate).toBeUndefined();
      expect(lastQuery().endDate).toBeUndefined();
      expect(q('#fb-range-start')).toBeNull();
    });

    it('starts on All time in a new page store', () => {
      expect(TestBed.inject(AdminPageStore).feedbackRange).toEqual({ timeSpan: 'all', start: '', end: '' });
    });

    it('refetches the first page with both dates for a preset, and keeps the choice in the page store', async () => {
      await create(undefined, { timeSpan: 'all', start: '', end: '' });
      issuesSpy.mockClear();

      selectValue(rangeSelect(), '30d');

      const expected = feedbackRangeDates('30d', new Date());
      expect(lastQuery()).toEqual(expect.objectContaining({ startDate: expected.start, endDate: expected.end, skip: 0 }));
      expect(store.feedbackRange).toEqual({ timeSpan: '30d', ...expected });
    });

    it('shows two labeled date inputs for Custom, and moves an end before the start to the start', async () => {
      await create(undefined, { timeSpan: 'all', start: '', end: '' });

      selectValue(rangeSelect(), 'custom');
      const start = q<HTMLInputElement>('#fb-range-start')!;
      const end = q<HTMLInputElement>('#fb-range-end')!;
      expect(textOf(q('label[for="fb-range-start"]'))).toBe('Start date');
      expect(textOf(q('label[for="fb-range-end"]'))).toBe('End date');
      expect(start.value).toBe(feedbackRangeDates('custom', new Date()).start);

      issuesSpy.mockClear();
      start.value = '2026-10-01';
      start.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      expect(lastQuery()).toEqual(expect.objectContaining({ startDate: '2026-10-01', skip: 0 }));

      end.value = '2026-09-01';
      end.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      expect(store.feedbackRange.end).toBe('2026-10-01');
      expect(lastQuery()).toEqual(expect.objectContaining({ startDate: '2026-10-01', endDate: '2026-10-01' }));
    });

    it('refreshes the switches, the models, the first page and the counts', async () => {
      await create();
      switchSpy.mockClear();
      issuesSpy.mockClear();
      countsSpy.mockClear();
      const loadCounts = vi.spyOn(store, 'loadFeedbackCounts');

      q<HTMLButtonElement>('#fb-refresh-btn')!.click();
      fixture.detectChanges();

      expect(switchSpy).toHaveBeenCalledTimes(1);
      expect(service.getModels).toHaveBeenCalledTimes(2);
      expect(lastQuery()).toEqual(expect.objectContaining({ skip: 0 }));
      expect(loadCounts).toHaveBeenCalledTimes(1);
      expect(countsSpy).toHaveBeenCalledTimes(1);
    });

    it('marks Refresh aria-disabled and shows a spinner while the first page loads', async () => {
      const pending = new Subject<AdminFeedbackIssuePage>();
      issuesSpy.mockReturnValue(pending.asObservable());
      await create();

      const refresh = q<HTMLButtonElement>('#fb-refresh-btn')!;
      expect(refresh.getAttribute('aria-disabled')).toBe('true');
      expect(refresh.querySelector('.gh-spinner-small')).not.toBeNull();
      switchSpy.mockClear();
      refresh.click();
      expect(switchSpy).not.toHaveBeenCalled();

      pending.next(page([issue(1)]));
      fixture.detectChanges();
      expect(refresh.getAttribute('aria-disabled')).toBeNull();
    });

    it('sets the sticky clearance from the header\'s height', async () => {
      const callbacks: ResizeObserverCallback[] = [];
      const original = globalThis.ResizeObserver;
      globalThis.ResizeObserver = class {
        constructor(callback: ResizeObserverCallback) { callbacks.push(callback); }
        observe(): void { /* driven by hand */ }
        unobserve(): void { /* driven by hand */ }
        disconnect(): void { /* driven by hand */ }
      } as unknown as typeof ResizeObserver;
      try {
        await create();
        const head = q('.tab-header-row')!;
        vi.spyOn(head, 'getBoundingClientRect').mockReturnValue({ height: 63.2 } as DOMRect);

        callbacks.forEach(callback => callback([], {} as ResizeObserver));

        expect(el.style.getPropertyValue('--tl-sticky-clearance')).toBe('72px');
      } finally {
        globalThis.ResizeObserver = original;
      }
    });
  });

  describe('switch card', () => {
    const toggleOf = (target: string) => q<HTMLInputElement>(`#tl-fb-switch-${target}-toggle`)!;
    const metaOf = (target: string) => textOf(q(`#tl-fb-switch-${target}-state`));
    const wordOf = (target: string) => q(`#tl-fb-switch-${target}-toggle`)!.closest('.tl-fb-switch-row')!.querySelector('.tl-fb-switch-word');
    const statusOf = (target: string) => textOf(q(`#tl-fb-switch-${target}-status`));
    const rowOf = (target: string) => toggleOf(target).closest('.tl-fb-switch-row') as HTMLElement;
    const at = '2026-10-21T12:02:00Z';

    function serve(chat: Partial<AdminFeedbackSwitches['chat']>): void {
      switchState = { chat: { ...switchState.chat, ...chat } };
      component.loadSwitches();
      fixture.detectChanges();
    }

    it('is a section named by Feedback tool, with one large switch named by the title and the surface it governs', async () => {
      await create();

      const section = q('section.tl-fb-switch-card')!;
      expect(section.getAttribute('aria-labelledby')).toBe('tl-fb-switch-title');
      const title = q('h3#tl-fb-switch-title')!;
      expect(textOf(title)).toBe('Feedback tool');
      expect(title.classList.contains('gh-section-title')).toBe(true);
      expect(section.querySelectorAll('.tl-fb-switch-row').length).toBe(1);
      expect(section.querySelectorAll('input[type="checkbox"]').length).toBe(1);

      const toggle = toggleOf('chat');
      expect(toggle.type).toBe('checkbox');
      expect(toggle.getAttribute('role')).toBe('switch');
      expect(toggle.closest('.gh-switch')?.classList.contains('gh-switch-lg')).toBe(true);
      expect(toggle.getAttribute('aria-labelledby')).toBe('tl-fb-switch-title tl-fb-switch-chat-label');
      const name = toggle.getAttribute('aria-labelledby')!.split(' ').map(id => textOf(el.querySelector('#' + id))).join(' ');
      expect(name).toBe('Feedback tool Overseer chat');
      expect(textOf(q('#tl-fb-switch-chat-label'))).toBe('Overseer chat');
      expect(textOf(q('#tl-fb-switch-chat-desc'))).toBe(
        'Lets the chat assistant report missing information, tool problems and its own mistakes to administrators.');
      expect(toggle.getAttribute('aria-describedby')).toBe('tl-fb-switch-chat-desc tl-fb-switch-chat-state');
      expect(toggle.checked).toBe(true);
      expect(toggle.disabled).toBe(false);
      expect(el.textContent).not.toContain('Enabled in Overseer chat');
      expect(el.textContent).not.toContain('Enabled in GnollBench');
    });

    it('says in an (i) popup that the switch is for the Overseer chat only and where GnollBench sets it', async () => {
      await create();

      const head = q('.tl-fb-switch-head')!;
      const button = head.querySelector<HTMLButtonElement>('app-info-tip button')!;
      expect(button.getAttribute('aria-label')).toBe('About the feedback tool');
      expect(button.getAttribute('popovertarget')).toBe('tl-fb-switch-scope-tip-popup');
      const tip = textOf(q('#tl-fb-switch-scope-tip'));
      expect(tip).toContain('Overseer chat only');
      expect(tip).toContain('GnollBench → Run Benchmark → Feedback Tool');
      expect(q('#tl-fb-switch-gnollbench-pointer')).toBeNull();
    });

    it('keeps the status line announced but visually hidden', async () => {
      await create();

      const line = q('#tl-fb-switch-chat-status')!;
      expect(line.getAttribute('role')).toBe('status');
      expect(line.classList.contains('visually-hidden')).toBe(true);
    });

    it('states how the switch was set in its meta line, and its state in a word', async () => {
      await create();
      expect(metaOf('chat')).toBe(`On · Changed ${feedbackDateTimeText('2026-10-01T09:00:00Z')} by tommi`);
      expect(textOf(wordOf('chat'))).toBe('On');
      expect(wordOf('chat')!.getAttribute('data-state')).toBe('on');
      expect(wordOf('chat')!.getAttribute('aria-hidden')).toBe('true');

      serve({ enabled: false, setAtUtc: at, setByUserName: 'tommi', seeded: false });
      expect(metaOf('chat')).toBe(`Off · Changed ${feedbackDateTimeText(at)} by tommi`);
      expect(textOf(wordOf('chat'))).toBe('Off');
      expect(wordOf('chat')!.getAttribute('data-state')).toBe('off');
      expect(toggleOf('chat').checked).toBe(false);

      serve({ enabled: true, setAtUtc: at, setByUserName: null, seeded: false });
      expect(metaOf('chat')).toBe(`On · Changed ${feedbackDateTimeText(at)}`);

      serve({ enabled: false, setAtUtc: null, setByUserName: null, seeded: true });
      expect(metaOf('chat')).toBe('Off · Configuration default');

      serve({ enabled: true, setAtUtc: null, setByUserName: null, seeded: true });
      expect(metaOf('chat')).toBe('On · Configuration default');
    });

    it('says Loading… until the state arrives, and shows no state word', async () => {
      const pending = new Subject<AdminFeedbackSwitches>();
      switchSpy.mockReturnValue(pending.asObservable());
      await create();

      expect(metaOf('chat')).toBe('Loading…');
      expect(wordOf('chat')).toBeNull();
      expect(toggleOf('chat').disabled).toBe(true);
    });

    it('commits an Overseer chat click at once, disables the switch while it runs, announces it and refetches', async () => {
      await create();
      const pending = new Subject<AdminFeedbackSwitches>();
      const setSpy = vi.spyOn(service, 'setSwitch').mockReturnValue(pending.asObservable());
      switchSpy.mockClear();
      const toggle = toggleOf('chat');

      toggle.click();
      fixture.detectChanges();

      expect(setSpy).toHaveBeenCalledTimes(1);
      expect(setSpy).toHaveBeenCalledWith('chat', false);
      expect(toggle.disabled).toBe(true);
      expect(toggle.getAttribute('aria-busy')).toBe('true');

      switchState = { chat: { enabled: false, setAtUtc: at, setByUserName: 'tommi', seeded: false } };
      pending.next(switchState);
      pending.complete();
      fixture.detectChanges();

      expect(toggle.disabled).toBe(false);
      expect(toggle.checked).toBe(false);
      expect(toggle.getAttribute('aria-busy')).toBeNull();
      expect(statusOf('chat')).toBe('Overseer chat: the feedback tool is off.');
      expect(switchSpy).toHaveBeenCalledTimes(1);
      expect(metaOf('chat')).toBe(`Off · Changed ${feedbackDateTimeText(at)} by tommi`);
      expect(textOf(wordOf('chat'))).toBe('Off');
      expect(q('.tl-fb-switch-error')).toBeNull();
    });

    it('reverts only the Overseer chat switch and shows the error under it when the request fails', async () => {
      await create();
      vi.spyOn(service, 'setSwitch').mockReturnValue(throwError(() => ({ status: 500, error: { message: 'Database unavailable' } })));
      const toggle = toggleOf('chat');

      toggle.click();
      fixture.detectChanges();

      expect(toggle.checked).toBe(true);
      expect(toggle.disabled).toBe(false);
      const errors = el.querySelectorAll('.tl-fb-switch-error.gh-field-error');
      expect(errors.length).toBe(1);
      expect(errors[0].id).toBe('tl-fb-switch-chat-error');
      expect(rowOf('chat').contains(errors[0])).toBe(true);
      expect(textOf(errors[0])).toBe('The feedback tool could not be turned off for Overseer chat: Database unavailable');
      expect(toggle.getAttribute('aria-describedby')).toBe('tl-fb-switch-chat-desc tl-fb-switch-chat-state tl-fb-switch-chat-error');
      expect(statusOf('chat')).toBe('');
    });

    it('shows a failed load once, under the title, and leaves the switch disabled', async () => {
      switchSpy.mockReturnValue(throwError(() => ({ status: 500, error: { message: 'Database unavailable' } })));
      await create();

      const errors = el.querySelectorAll('.tl-fb-switch-load-error');
      expect(errors.length).toBe(1);
      expect(textOf(errors[0])).toBe('The state of the feedback tool could not be loaded: Database unavailable');
      expect(errors[0].closest('.tl-fb-switch-row')).toBeNull();
      expect(el.querySelectorAll('.tl-fb-switch-error').length).toBe(0);
      expect(el.querySelectorAll('.tl-fb-switch-row').length).toBe(1);
      expect(toggleOf('chat').disabled).toBe(true);
      expect(metaOf('chat')).toBe('');
    });
  });

  describe('issue cards', () => {
    it('fetches New and Regressed issues of the range, last seen first, 25 at a time', async () => {
      await create();

      expect(lastQuery()).toEqual(expect.objectContaining({
        startDate: '2026-09-21', endDate: '2026-10-21', statuses: ['new', 'regressed'], sort: 'lastSeen', skip: 0, take: 25
      }));
      expect(lastQuery().category).toBeUndefined();
      expect(lastQuery().modelId).toBeUndefined();
      expect(lastQuery().search).toBeUndefined();
    });

    it('is a list named by the section heading, which carries the count beside it, of articles labeled by their h4 titles', async () => {
      await create();

      const list = q('ul.tl-fb-cards')!;
      expect(list.getAttribute('role')).toBe('list');
      expect(list.getAttribute('aria-labelledby')).toBe('tl-fb-issues-title');
      expect(textOf(q('#tl-fb-issues-title'))).toBe('Reported issues');
      expect(q('#tl-fb-issues-title')!.classList.contains('gh-section-title')).toBe(true);
      expect(q('.tl-fb-issues-head #tl-fb-status')).not.toBeNull();
      expect(q('#tl-fb-status')!.getAttribute('role')).toBe('status');
      const article = cards()[0];
      const title = el.querySelector('#' + article.getAttribute('aria-labelledby'))!;
      expect(title.tagName).toBe('H4');
      expect(title.id).toBe('tl-fb-1-title');
      expect(textOf(title)).toBe('Dilithium crystal');
    });

    it('renders the kicker, the summary, the four metrics with the retained count and every fact', async () => {
      nextPage = page([issue(1, { occurrenceCount: 12, retainedCount: 9 })]);
      await create();
      const card = cards()[0];

      expect(textOf(card.querySelector('.tl-fb-category'))).toBe('Missing information');
      expect(textOf(card.querySelector('.tl-fb-status-badge [aria-hidden="true"]'))).toBe('New');
      expect(textOf(card.querySelector('.tl-fb-models'))).toBe('Models: claude-x, gpt-x');
      expect(textOf(card.querySelector('.tl-fb-summary'))).toBe('The wiki has no page for it.');

      const metrics = Array.from(card.querySelectorAll('.tl-fb-metrics > .tl-fb-metric'));
      expect(metrics.map(m => textOf(m.querySelector('dt')))).toEqual(['Occurrences', 'Users', 'First seen', 'Last seen']);
      expect(Array.from(card.querySelectorAll('.tl-fb-metric-occurrences dd')).map(d => textOf(d))).toEqual(['12', '9 retained']);
      expect(textOf(card.querySelector('.tl-fb-metric-users dd'))).toBe('3');
      expect(textOf(card.querySelector('.tl-fb-metric-first dd'))).toBe(feedbackDateText('2026-09-02T08:00:00Z'));
      expect(textOf(card.querySelector('.tl-fb-metric-last dd'))).toBe(feedbackDateTimeText('2026-10-20T12:30:00Z'));

      const facts = Array.from(card.querySelectorAll('.tl-fb-facts > div')).map(f => [textOf(f.querySelector('dt')), textOf(f.querySelector('dd'))]);
      expect(facts).toEqual([
        ['Latest expected location', 'wiki page Dilithium crystal'],
        ['Latest related tools', 'wiki_search, item_lookup'],
        ["Model's suspected cause (hypothesis)", 'Corpus or tool limit'],
        ['Latest version', '1.4.2+abc']
      ]);
    });

    it('leaves out the retained qualifier while every occurrence remains, and says Not recorded for missing facts', async () => {
      nextPage = page([issue(1, {
        latestExpectedLocation: null, latestRelatedTools: [], latestSuspectedCause: null, latestAppVersion: null
      })]);
      await create();
      const card = cards()[0];

      expect(card.querySelector('.tl-fb-metric-occurrences .tl-fb-metric-note')).toBeNull();
      expect(Array.from(card.querySelectorAll('.tl-fb-facts dd')).map(d => textOf(d))).toEqual(
        ['Not recorded', 'Not recorded', 'Not recorded', 'Not recorded']);
    });

    it('badges Regressed in words with the warning tone, and counts regressions above one', async () => {
      nextPage = page([
        issue(1, { status: 'regressed', regressionCount: 1 }),
        issue(2, { status: 'regressed', regressionCount: 2 })
      ]);
      await create();
      const [first, second] = cards();

      const badge = first.querySelector('.tl-fb-status-badge')!;
      expect(badge.classList.contains('status-badge')).toBe(true);
      expect(badge.classList.contains('badge-warning')).toBe(true);
      expect(textOf(badge.querySelector('[aria-hidden="true"]'))).toBe('Regressed');
      expect(textOf(second.querySelector('.tl-fb-status-badge [aria-hidden="true"]'))).toBe('Regressed 2×');
      expect(textOf(second.querySelector('.tl-fb-status-badge .visually-hidden'))).toBe('Regressed 2 times');
    });

    it('renders model-written text as plain text', async () => {
      nextPage = page([issue(1, { subject: '<i>Elbereth</i>', summary: 'It said <b>bold</b> things.' })]);
      await create();
      const card = cards()[0];

      expect(textOf(card.querySelector('.tl-fb-summary'))).toBe('It said <b>bold</b> things.');
      expect(textOf(card.querySelector('h4'))).toBe('<i>Elbereth</i>');
      expect(card.querySelector('.tl-fb-summary b')).toBeNull();
      expect(card.querySelector('h4 i')).toBeNull();
    });

    it('appends the next 25 on Show more and focuses the first new card', async () => {
      nextPage = page(issueRange(1, 25), 30);
      await create();

      expect(status()).toBe('Showing 25 of 30 issues');
      const more = q<HTMLButtonElement>('.tl-fb-show-more')!;
      expect(textOf(more)).toBe('Show 5 more');

      nextPage = page(issueRange(26, 5), 30);
      more.click();
      fixture.detectChanges();

      expect(lastQuery()).toEqual(expect.objectContaining({ skip: 25, take: 25 }));
      expect(cards().length).toBe(30);
      expect(document.activeElement?.id).toBe('tl-fb-26-title');
      expect(q('.tl-fb-show-more')).toBeNull();
    });
  });

  describe('occurrences', () => {
    it('loads the first page on first open, newest first, then 10 more on Show more, and not again on reopening', async () => {
      nextPage = page([issue(1, { retainedCount: 12 })]);
      occurrencesSpy.mockReturnValue(of<AdminFeedbackOccurrencePage>({
        totalCount: 12, rows: Array.from({ length: 10 }, (_, i) => occurrence(100 - i))
      }));
      await create();
      const card = cards()[0];
      expect(textOf(card.querySelector('details.tl-fb-occurrences > summary'))).toBe('Occurrences (12)');
      expect(occurrencesSpy).not.toHaveBeenCalled();

      const details = openOccurrences(card);

      expect(occurrencesSpy).toHaveBeenCalledWith(1, 0, 10);
      expect(details.querySelectorAll('li.tl-fb-occ').length).toBe(10);
      const more = details.querySelector<HTMLButtonElement>('.tl-fb-occ-more')!;
      expect(textOf(more)).toBe('Show 2 more');

      occurrencesSpy.mockReturnValue(of<AdminFeedbackOccurrencePage>({ totalCount: 12, rows: [occurrence(90), occurrence(89)] }));
      more.click();
      fixture.detectChanges();

      expect(occurrencesSpy).toHaveBeenLastCalledWith(1, 10, 10);
      expect(details.querySelectorAll('li.tl-fb-occ').length).toBe(12);
      expect(details.querySelector('.tl-fb-occ-more')).toBeNull();
      expect(document.activeElement?.id).toBe('tl-fb-occ-90-time');

      occurrencesSpy.mockClear();
      details.open = false;
      details.dispatchEvent(new Event('toggle'));
      openOccurrences(card);
      expect(occurrencesSpy).not.toHaveBeenCalled();
    });

    it('shows each occurrence\'s facts as text, its marks, and collapses long details behind an expand control', async () => {
      const long = Array.from({ length: 9 }, (_, i) => `Line ${i + 1}`).join('\n');
      occurrencesSpy.mockReturnValue(of<AdminFeedbackOccurrencePage>({
        totalCount: 2,
        rows: [
          occurrence(5, { details: long, truncated: true, redacted: true, noticedVia: 'user_correction', userName: null }),
          occurrence(4, { details: 'Short <b>note</b>.' })
        ]
      }));
      await create();
      const details = openOccurrences(cards()[0]);
      const [first, second] = Array.from(details.querySelectorAll<HTMLElement>('li.tl-fb-occ'));

      expect(textOf(first.querySelector('h5'))).toBe(feedbackDateTimeText('2026-10-20T12:30:00Z'));
      const facts = Object.fromEntries(Array.from(first.querySelectorAll('.tl-fb-occ-facts > div'))
        .map(f => [textOf(f.querySelector('dt')), textOf(f.querySelector('dd'))]));
      expect(facts).toEqual(expect.objectContaining({
        'User': 'Account erased',
        'Provider and model': 'Anthropic · claude-x',
        'Version': '1.4.2+abc',
        'Conversation': 'Session 812 · message 4410',
        'Searched for': 'wiki_search "dilithium"',
        'Expected location': 'wiki page Dilithium crystal',
        'Noticed via': 'The user corrected it'
      }));
      expect(textOf(first.querySelector('.tl-fb-occ-cause'))).toBe('Did not verify');
      expect(textOf(first.querySelector('.tl-fb-occ-cause-note'))).toBe('I answered first.');
      expect(Array.from(first.querySelectorAll('.tl-fb-occ-marks .gh-tag')).map(t => textOf(t))).toEqual(['Truncated', 'Redacted']);
      expect(second.querySelector('.tl-fb-occ-marks')).toBeNull();

      const text = first.querySelector<HTMLElement>('.tl-fb-occ-details')!;
      expect(text.textContent).toBe(long);
      expect(text.classList.contains('is-collapsed')).toBe(true);
      const expand = first.querySelector<HTMLButtonElement>('.tl-fb-occ-expand')!;
      expect(expand.getAttribute('aria-expanded')).toBe('false');
      expect(expand.getAttribute('aria-controls')).toBe(text.id);
      expand.click();
      fixture.detectChanges();
      expect(text.classList.contains('is-collapsed')).toBe(false);
      expect(expand.getAttribute('aria-expanded')).toBe('true');

      expect(second.querySelector('.tl-fb-occ-expand')).toBeNull();
      expect(textOf(second.querySelector('.tl-fb-occ-details'))).toBe('Short <b>note</b>.');
      expect(second.querySelector('.tl-fb-occ-details b')).toBeNull();
    });
  });

  describe('filters', () => {
    it('offers every status with the counts over all dates, New and Regressed selected', async () => {
      await create(undefined, undefined, counts({ new: 3, regressed: 1, resolved: 7 }));

      const status = facet('tl-fb-facet-status')!;
      expect(status.options.map(o => `${o.label} ${o.count}`)).toEqual(
        ['New 3', 'Regressed 1', 'Acknowledged 0', 'Resolved 7', 'Dismissed 0']);
      expect([...status.selected]).toEqual(['new', 'regressed']);
      expect(Array.from(el.querySelectorAll('.tl-fb-chips .gh-filter-chip')).map(c => c.getAttribute('aria-label'))).toEqual(
        ['Remove filter Status: New', 'Remove filter Status: Regressed']);
    });

    it('takes new counts from the page store as they arrive', async () => {
      await create();
      expect(facet('tl-fb-facet-status')!.options.map(o => o.count)).toEqual([0, 0, 0, 0, 0]);

      countsSpy.mockReturnValue(of(counts({ new: 2, dismissed: 4 })));
      store.loadFeedbackCounts();
      fixture.detectChanges();

      expect(facet('tl-fb-facet-status')!.options.map(o => `${o.label} ${o.count}`)).toEqual(
        ['New 2', 'Regressed 0', 'Acknowledged 0', 'Resolved 0', 'Dismissed 4']);
    });

    it('refetches page 1 for a Status, Category or Model choice and keeps each in the address', async () => {
      await create();
      expect(facet('tl-fb-facet-model')!.options.map(o => o.value)).toEqual(['claude-x', 'gpt-x']);

      choose('tl-fb-facet-status', ['resolved']);
      expect(lastQuery()).toEqual(expect.objectContaining({ statuses: ['resolved'], skip: 0 }));

      choose('tl-fb-facet-category', ['tool_problem']);
      expect(lastQuery()).toEqual(expect.objectContaining({ statuses: ['resolved'], category: 'tool_problem', skip: 0 }));

      choose('tl-fb-facet-model', ['gpt-x']);
      expect(lastQuery()).toEqual(expect.objectContaining({ category: 'tool_problem', modelId: 'gpt-x', skip: 0 }));

      await settle();
      expect(queryParams()).toEqual(expect.objectContaining({ status: 'resolved', category: 'tool_problem', model: 'gpt-x' }));
      expect(router.url.startsWith('/admin/feedback?')).toBe(true);
    });

    it('leaves the default statuses out of the address and writes all for none', async () => {
      await create();

      choose('tl-fb-facet-category', ['own_mistake']);
      await settle();
      expect(queryParams()['status']).toBeUndefined();
      expect(queryParams()['category']).toBe('own_mistake');

      choose('tl-fb-facet-status', []);
      await settle();
      expect(lastQuery().statuses).toBeUndefined();
      expect(queryParams()['status']).toBe('all');
    });

    it('takes its filters from the address it opens on', async () => {
      await create('/admin/feedback?status=resolved,dismissed,bogus&category=own_mistake&model=gpt-x&q=crystal');

      expect(listQueries().length).toBe(1);
      expect(lastQuery()).toEqual(expect.objectContaining({
        statuses: ['resolved', 'dismissed'], category: 'own_mistake', modelId: 'gpt-x', search: 'crystal'
      }));
      expect(q<HTMLInputElement>('#tl-fb-search')!.value).toBe('crystal');
      expect(Array.from(el.querySelectorAll('.tl-fb-chips .gh-filter-chip')).map(c => c.getAttribute('aria-label'))).toEqual([
        'Remove filter Status: Resolved', 'Remove filter Status: Dismissed', 'Remove filter Category: Own mistake',
        'Remove filter Model: gpt-x', 'Remove filter Search: “crystal”'
      ]);
    });

    it('applies an address change made by Back or Forward, and reads status=all as no status filter', async () => {
      await create();

      await router.navigateByUrl('/admin/feedback?status=all&category=tool_problem');
      fixture.detectChanges();

      expect(lastQuery().statuses).toBeUndefined();
      expect(lastQuery().category).toBe('tool_problem');
    });

    it('refetches page 1 for the search once typing pauses, keeps it in the address, and Escape clears it at once', async () => {
      await create();
      issuesSpy.mockClear();
      const search = q<HTMLInputElement>('#tl-fb-search')!;

      typeInto(search, 'crystal');
      expect(listQueries().length).toBe(0);
      await settle(300);
      fixture.detectChanges();

      expect(lastQuery()).toEqual(expect.objectContaining({ search: 'crystal', skip: 0 }));
      await settle();
      expect(queryParams()['q']).toBe('crystal');

      const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      search.dispatchEvent(escape);
      fixture.detectChanges();

      expect(escape.defaultPrevented).toBe(true);
      expect(search.value).toBe('');
      expect(lastQuery().search).toBeUndefined();
      await settle();
      expect(queryParams()['q']).toBeUndefined();
    });

    it('sorts by Last seen, First seen or Most occurrences, refetches and remembers the choice', async () => {
      await create();
      const sort = q<HTMLSelectElement>('#tl-fb-sort')!;
      expect(Array.from(sort.options).map(o => o.textContent?.trim())).toEqual(['Last seen', 'First seen', 'Most occurrences']);
      expect(textOf(el.querySelector('label[for="tl-fb-sort"]'))).toBe('Sort by');

      selectValue(sort, 'count');

      expect(lastQuery()).toEqual(expect.objectContaining({ sort: 'count', skip: 0 }));
      expect(JSON.parse(localStorage.getItem(FEEDBACK_VIEW_STORAGE_KEY)!)).toEqual({ version: 1, sort: 'count' });
      expect(queryParams()['sort']).toBeUndefined();
    });

    it('keeps the Sort by under the key it had before the Feedback tab', () => {
      expect(FEEDBACK_VIEW_STORAGE_KEY).toBe('overseer.admin.telemetry.feedback.view');
    });

    it('opens on the remembered sort', async () => {
      localStorage.setItem(FEEDBACK_VIEW_STORAGE_KEY, JSON.stringify({ version: 1, sort: 'firstSeen' }));
      await create();

      expect(lastQuery().sort).toBe('firstSeen');
      expect(q<HTMLSelectElement>('#tl-fb-sort')!.value).toBe('firstSeen');
    });

    it('removes a chip\'s filter, refetches and focuses the chip in its place', async () => {
      await create();

      const first = q<HTMLButtonElement>('.tl-fb-chips .gh-filter-chip')!;
      first.click();
      fixture.detectChanges();

      expect(lastQuery().statuses).toEqual(['regressed']);
      expect(document.activeElement?.getAttribute('aria-label')).toBe('Remove filter Status: Regressed');
    });
  });

  describe('empty states', () => {
    const glyph = () => q('.tl-fb-empty > svg');

    it('says the range has no issues when nothing filters the list', async () => {
      nextPage = page([], 0);
      await create('/admin/feedback?status=all');

      const empty = q('.tl-fb-empty.tl-fb-empty-range')!;
      expect(textOf(empty.querySelector('.tl-fb-empty-title'))).toBe('No feedback issues');
      expect(textOf(empty.querySelector('.tl-fb-empty-text'))).toBe('The assistant has not reported an issue in this date range.');
      expect(glyph()!.getAttribute('aria-hidden')).toBe('true');
      expect(glyph()!.getAttribute('width')).toBe('32');
      expect(q('.tl-fb-empty-clear')).toBeNull();
      expect(issuesSpy.mock.calls.some(call => (call[0] as AdminFeedbackIssuesQuery).take === 1)).toBe(false);
    });

    it('says the range has no issues when the filters leave none and the range holds none either', async () => {
      nextPage = page([], 0);
      rangePage = page([], 0);
      await create();

      expect(q('.tl-fb-empty-range')).not.toBeNull();
      expect(issuesSpy).toHaveBeenCalledWith({ startDate: '2026-09-21', endDate: '2026-10-21', take: 1 });
    });

    it('says the filters match nothing when the range holds issues, and Clear filters clears them all and focuses the search', async () => {
      nextPage = page([], 0);
      rangePage = page([], 4);
      await create();

      const empty = q('.tl-fb-empty.tl-fb-empty-filters')!;
      expect(textOf(empty.querySelector('.tl-fb-empty-title'))).toBe('No issues match these filters');
      expect(glyph()!.getAttribute('aria-hidden')).toBe('true');
      nextPage = page([issue(1, { status: 'resolved' })]);
      (empty.querySelector('button.tl-fb-empty-clear') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(lastQuery().statuses).toBeUndefined();
      expect(cards().length).toBe(1);
      expect(document.activeElement?.id).toBe('tl-fb-search');
      await settle();
      expect(queryParams()['status']).toBe('all');
    });

    it('shows the load error in place of the list', async () => {
      issuesSpy.mockImplementation(() => throwError(() => ({ status: 400, error: { message: 'Unknown sort.' } })));
      await create();

      expect(textOf(q('.tl-fb-error'))).toBe('The feedback issues could not be loaded: Unknown sort.');
      expect(q('ul.tl-fb-cards')).toBeNull();
    });
  });

  describe('triage', () => {
    it('offers New, Acknowledged, Resolved and Dismissed under a visible label, with the recurrence rule in its (i)', async () => {
      await create();
      const card = cards()[0];
      const select = card.querySelector<HTMLSelectElement>('#tl-fb-1-status')!;

      expect(textOf(card.querySelector('label[for="tl-fb-1-status"]'))).toBe('Status');
      expect(Array.from(select.options).map(o => o.value)).toEqual(['new', 'acknowledged', 'resolved', 'dismissed']);
      expect(select.value).toBe('new');
      const help = card.querySelector('#' + select.getAttribute('aria-describedby'))!;
      expect(textOf(help)).toBe('A resolved issue that recurs becomes Regressed; a dismissed one stays dismissed.');
      expect(help.closest('app-info-tip')).not.toBeNull();
      expect(card.querySelector('.tl-fb-label-row app-info-tip button')!.getAttribute('aria-label')).toBe('About Status');
      expect(card.querySelector('.form-hint')).toBeNull();
      expect(textOf(card.querySelector('label[for="tl-fb-1-note"]'))).toBe('Note');
      expect(card.querySelector('#tl-fb-1-note')!.classList.contains('gh-textarea-autosize')).toBe(true);
    });

    it('never offers Regressed: a regressed issue asks for a new status before it saves', async () => {
      nextPage = page([issue(1, { status: 'regressed', regressionCount: 1 })]);
      const updateSpy = vi.spyOn(service, 'updateStatus');
      await create();
      const card = cards()[0];
      const select = card.querySelector<HTMLSelectElement>('#tl-fb-1-status')!;

      expect(Array.from(select.options).map(o => o.value)).toEqual(['', 'new', 'acknowledged', 'resolved', 'dismissed']);
      expect(select.options[0].disabled).toBe(true);
      expect(Array.from(select.options).some(o => o.value === 'regressed')).toBe(false);

      card.querySelector<HTMLButtonElement>('.tl-fb-save')!.click();
      fixture.detectChanges();

      expect(updateSpy).not.toHaveBeenCalled();
      expect(textOf(card.querySelector('.tl-fb-triage-error'))).toBe('Choose a status first.');
    });

    it('saves the status and note, takes the server\'s copy, announces it and refetches the counts', async () => {
      const updated = issue(1, { status: 'acknowledged', triageNote: 'Wiki page coming', triagedByUserName: 'tommi', triagedUtc: '2026-10-21T12:05:00Z' });
      const updateSpy = vi.spyOn(service, 'updateStatus').mockReturnValue(of(updated));
      await create();
      const loadCounts = vi.spyOn(store, 'loadFeedbackCounts');
      const card = cards()[0];

      selectValue(card.querySelector<HTMLSelectElement>('#tl-fb-1-status')!, 'acknowledged');
      typeInto(card.querySelector<HTMLTextAreaElement>('#tl-fb-1-note')!, '  Wiki page coming ');
      card.querySelector<HTMLButtonElement>('.tl-fb-save')!.click();
      fixture.detectChanges();

      expect(updateSpy).toHaveBeenCalledWith(1, 'acknowledged', 'Wiki page coming');
      const result = card.querySelector('#tl-fb-1-result')!;
      expect(result.getAttribute('role')).toBe('status');
      expect(textOf(result)).toBe('Saved. The issue is Acknowledged.');
      expect(textOf(card.querySelector('.tl-fb-status-badge [aria-hidden="true"]'))).toBe('Acknowledged');
      expect(textOf(card.querySelector('.tl-fb-triaged'))).toBe(`Triaged ${feedbackDateTimeText('2026-10-21T12:05:00Z')} by tommi`);
      expect(loadCounts).toHaveBeenCalledTimes(1);
      expect(card.querySelector('.tl-fb-triage-error')).toBeNull();
    });

    it('shows a failed save under the form and refetches no counts', async () => {
      vi.spyOn(service, 'updateStatus').mockReturnValue(throwError(() => ({ status: 400, error: { message: 'Unknown status.' } })));
      await create();
      const loadCounts = vi.spyOn(store, 'loadFeedbackCounts');
      const card = cards()[0];

      card.querySelector<HTMLButtonElement>('.tl-fb-save')!.click();
      fixture.detectChanges();

      const error = card.querySelector('.tl-fb-triage-error.gh-field-error')!;
      expect(textOf(error)).toBe('The issue could not be saved: Unknown status.');
      expect(card.querySelector('.tl-fb-save')!.getAttribute('aria-describedby')).toBe(error.id);
      expect(textOf(card.querySelector('#tl-fb-1-result'))).toBe('');
      expect(loadCounts).not.toHaveBeenCalled();
    });

    it('deletes an issue after a confirmation that names it and its occurrences, then refetches the counts', async () => {
      nextPage = page([issue(1, { retainedCount: 9 }), issue(2)], 2);
      // The response arrives after the dialog has closed and given focus back, as a real request does.
      const response = new Subject<void>();
      const deleteSpy = vi.spyOn(service, 'deleteIssue').mockReturnValue(response.asObservable());
      await create();
      const loadCounts = vi.spyOn(store, 'loadFeedbackCounts');

      const button = cards()[0].querySelector<HTMLButtonElement>('.tl-fb-delete')!;
      expect(button.getAttribute('aria-label')).toBe('Delete issue: Dilithium crystal');
      expect(button.textContent?.trim()).toBe('');
      button.focus();
      button.click();
      fixture.detectChanges();

      const dialog = el.querySelector('app-admin-confirm-dialog dialog') as HTMLDialogElement;
      expect(dialog.open).toBe(true);
      expect(textOf(dialog)).toContain('Delete the issue “Dilithium crystal” and its 9 stored occurrences? This cannot be undone.');
      expect(deleteSpy).not.toHaveBeenCalled();

      const confirm = Array.from(dialog.querySelectorAll('button')).find(b => b.textContent?.trim() === 'Delete Issue') as HTMLButtonElement;
      confirm.click();
      fixture.detectChanges();
      response.next();
      response.complete();
      fixture.detectChanges();

      expect(deleteSpy).toHaveBeenCalledWith(1);
      expect(cards().map(c => c.getAttribute('data-issue-id'))).toEqual(['2']);
      expect(status()).toBe('Deleted the issue “Dilithium crystal”. One issue');
      expect(loadCounts).toHaveBeenCalledTimes(1);
      expect(document.activeElement?.id).toBe('tl-fb-2-title');
    });

    it('makes no request when the delete is canceled', async () => {
      const deleteSpy = vi.spyOn(service, 'deleteIssue');
      await create();

      cards()[0].querySelector<HTMLButtonElement>('.tl-fb-delete')!.click();
      fixture.detectChanges();
      const dialog = el.querySelector('app-admin-confirm-dialog dialog') as HTMLDialogElement;
      (Array.from(dialog.querySelectorAll('button')).find(b => b.textContent?.trim() === 'Cancel') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(dialog.open).toBe(false);
      expect(deleteSpy).not.toHaveBeenCalled();
      expect(cards().length).toBe(1);
    });
  });

  describe('reload', () => {
    it('refetches the switches, the models and the first page', async () => {
      await create();
      switchSpy.mockClear();
      issuesSpy.mockClear();

      component.reload();
      fixture.detectChanges();

      expect(switchSpy).toHaveBeenCalledTimes(1);
      expect(service.getModels).toHaveBeenCalledTimes(2);
      expect(lastQuery()).toEqual(expect.objectContaining({ skip: 0 }));
    });
  });
});
