import type { Mock } from 'vitest';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { provideLocationMocks } from '@angular/common/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Subject, of, throwError } from 'rxjs';
import {
  AdminFeedbackCounts,
  AdminFeedbackFacets,
  AdminFeedbackIssue,
  AdminFeedbackIssueFilters,
  AdminFeedbackIssueIds,
  AdminFeedbackIssuePage,
  AdminFeedbackIssuesQuery,
  AdminFeedbackOccurrence,
  AdminFeedbackOccurrencePage,
  AdminFeedbackService,
  FeedbackModelIdentity
} from '../../../services/admin-feedback.service';
import { FilterFacetComponent } from '../../../shared/data-table/filter-facet.component';
import { AdminPageStore } from '../../admin-page.store';
import { adminTestRoutes } from '../../admin.component.testing';
import { FeedbackExportDialogComponent, FeedbackExportDone } from '../shared/feedback-export-dialog.component';
import { FeedbackSelectionBarComponent, FeedbackSelectionId } from '../shared/feedback-selection-bar.component';
import {
  FeedbackExportOptions,
  FeedbackRange,
  loadFeedbackView,
  loadOverseerSelection,
  saveFeedbackView,
  saveOverseerSelection
} from '../shared/feedback-view-storage';
import {
  FeedbackOverseerPanelComponent,
  feedbackDateText,
  feedbackDateTimeText,
  feedbackModelSourceText,
  feedbackTimeText,
  feedbackVersionShort
} from './feedback-overseer-panel.component';

const settle = (ms = 0): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
/** A model identity's name and badges, space-separated; the element's text when it holds none. */
const identityTextOf = (el: Element | null | undefined): string => {
  const parts = Array.from(el?.querySelectorAll('.model-identity-name, .thinking-badge, .reasoning-badge, .provider-badge') ?? []);
  return parts.length ? parts.map(p => textOf(p)).join(' ') : textOf(el);
};

const VIEW_KEY = 'overseer.admin.feedback.overseer.view';
const STORAGE_KEYS = [VIEW_KEY, 'overseer.admin.feedback.overseer.selection', 'overseer.admin.telemetry.feedback.view'];

function clearStorage(): void {
  for (const key of STORAGE_KEYS) {
    try { localStorage.removeItem(key); } catch { /* nothing stored */ }
  }
}

function identity(modelId: string, overrides: Partial<FeedbackModelIdentity> = {}): FeedbackModelIdentity {
  return { modelId, modelLabel: modelId, provider: null, thinkingLevel: null, reasoningMode: null, ...overrides };
}

function issue(id: number, overrides: Partial<AdminFeedbackIssue> = {}): AdminFeedbackIssue {
  return {
    id, category: 'missing_information', subject: id === 1 ? 'Dilithium crystal' : `Subject ${id}`,
    summary: 'The wiki has no page for it.',
    firstSeenUtc: '2026-09-02T08:00:00Z', lastSeenUtc: '2026-10-20T12:30:00Z',
    occurrenceCount: 12, retainedCount: 12, distinctUserCount: 3,
    modelIds: ['claude-x', 'gpt-x'],
    modelIdentities: [identity('claude-x', { provider: 'Anthropic' }), identity('gpt-x', { provider: 'OpenAI' })],
    moreModelIdentities: 0,
    providers: ['Anthropic', 'OpenAI'], modelSources: { system: 2, user: 1, unknown: 0 },
    regressionCount: 0, lastRegressionUtc: null,
    latestExpectedLocation: 'wiki page Dilithium crystal', latestRelatedTools: ['wiki_search', 'item_lookup'],
    latestSuspectedCause: 'corpus_or_tool_limit', latestAppVersion: '1.4.2+abc',
    status: 'new', triageNote: null, triagedByUserName: null, triagedUtc: null,
    resolution: null, fixedInVersion: null, alsoInGnollBench: null,
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
    modelSource: 'system', systemConfigurationId: 7, systemConfigurationName: 'Claude (fast)', groupNames: ['Testers'],
    ...overrides
  };
}

function page(rows: AdminFeedbackIssue[], totalCount = rows.length): AdminFeedbackIssuePage {
  return { totalCount, rows };
}

function counts(overrides: Partial<AdminFeedbackCounts> = {}): AdminFeedbackCounts {
  return { new: 0, acknowledged: 0, resolved: 0, regressed: 0, dismissed: 0, ...overrides };
}

function facets(overrides: Partial<AdminFeedbackFacets> = {}): AdminFeedbackFacets {
  return {
    categories: [{ value: 'missing_information', count: 4 }, { value: 'tool_problem', count: 2 }],
    models: [{ value: 'gpt-x', count: 3 }, { value: 'claude-x', count: 5 }],
    providers: [{ value: 'OpenAI', count: 3 }, { value: 'Anthropic', count: 5 }],
    sources: [{ value: 'user', count: 2 }, { value: 'system', count: 6 }],
    versions: [{ value: '1.4.2', count: 4 }],
    tools: [{ value: 'wiki_search', count: 3 }, { value: 'item_lookup', count: 1 }],
    users: [{ id: 'u-2', name: 'bob', count: 1 }, { id: 'u-1', name: 'alice', count: 4 }],
    groups: [{ id: 'none', name: 'No group', count: 1 }, { id: '3', name: 'Testers', count: 2 }],
    configs: [{ id: '7', name: 'Claude (fast)', count: 3 }],
    ...overrides
  };
}

/** A custom range, which keeps its dates as given. */
const CUSTOM_RANGE: FeedbackRange = { preset: 'custom', start: '2026-09-21', end: '2026-10-21' };

/** The selection bar, as the panel sees it: its inputs and outputs. */
@Component({ selector: 'app-feedback-selection-bar', template: '' })
class SelectionBarStub {
  @Input() source: unknown;
  @Input() selectedIds: readonly FeedbackSelectionId[] = [];
  @Input() matchingCount = 0;
  @Input() notShownCount = 0;
  @Input() showSelectedOnly = false;
  @Input() exportOptions: FeedbackExportOptions | undefined;
  @Output() readonly showSelectedOnlyChange = new EventEmitter<boolean>();
  @Output() readonly clear = new EventEmitter<void>();
  @Output() readonly selectAllMatching = new EventEmitter<void>();
  @Output() readonly exported = new EventEmitter<{ missing: FeedbackSelectionId[]; exportOptions: FeedbackExportOptions }>();
  @Output() readonly imported = new EventEmitter<void>();
}

/** The export dialog, as a panel sees it: its inputs, its output, and open and close, which record the inputs they saw. */
@Component({ selector: 'app-feedback-export-dialog', template: '' })
class ExportDialogStub {
  @Input() source: unknown;
  @Input() idPrefix: string | null | undefined;
  @Input() ids: readonly FeedbackSelectionId[] = [];
  @Input() issueSubject: string | null = null;
  @Input() occurrenceTotal: number | null = null;
  @Input() exportOptions: FeedbackExportOptions | undefined;
  @Output() readonly exported = new EventEmitter<FeedbackExportDone>();
  readonly opened: { ids: readonly FeedbackSelectionId[]; issueSubject: string | null; occurrenceTotal: number | null }[] = [];
  closed = 0;

  open(): void {
    this.opened.push({ ids: [...this.ids], issueSubject: this.issueSubject, occurrenceTotal: this.occurrenceTotal });
  }

  close(): void {
    this.closed++;
  }
}

describe('FeedbackOverseerPanelComponent', () => {
  let fixture: ComponentFixture<FeedbackOverseerPanelComponent>;
  let component: FeedbackOverseerPanelComponent;
  let el: HTMLElement;
  let service: AdminFeedbackService;
  let store: AdminPageStore;
  let router: Router;
  let issuesSpy: Mock;
  let countsSpy: Mock;
  let facetsSpy: Mock;
  let idsSpy: Mock;
  let occurrencesSpy: Mock;
  /** What the next list request returns; a count request (take 1) gets `rangePage`. */
  let nextPage: AdminFeedbackIssuePage;
  let rangePage: AdminFeedbackIssuePage;

  beforeEach(async () => {
    clearStorage();
    await TestBed.configureTestingModule({
      imports: [FeedbackOverseerPanelComponent],
      providers: [provideRouter(adminTestRoutes()), provideLocationMocks(), provideHttpClient(), provideHttpClientTesting(), AdminPageStore]
    }).overrideComponent(FeedbackOverseerPanelComponent, {
      remove: { imports: [FeedbackSelectionBarComponent, FeedbackExportDialogComponent] },
      add: { imports: [SelectionBarStub, ExportDialogStub] }
    }).compileComponents();

    service = TestBed.inject(AdminFeedbackService);
    store = TestBed.inject(AdminPageStore);
    router = TestBed.inject(Router);
    nextPage = page([issue(1)]);
    rangePage = page([], 0);
    issuesSpy = vi.spyOn(service, 'getIssues').mockImplementation(
      (query?: AdminFeedbackIssuesQuery) => of(query?.take === 1 ? rangePage : nextPage));
    countsSpy = vi.spyOn(service, 'getCounts').mockReturnValue(of(counts()));
    facetsSpy = vi.spyOn(service, 'getFacets').mockReturnValue(of(facets()));
    // By default every selected id still exists and matches the filters.
    idsSpy = vi.spyOn(service, 'getIssueIds').mockImplementation((filters?: AdminFeedbackIssueFilters) => {
      const ids = filters?.ids ? [...filters.ids] : [1];
      return of<AdminFeedbackIssueIds>({ ids, totalCount: ids.length, truncated: false });
    });
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

  async function create(url = '/admin/feedback', range: FeedbackRange = CUSTOM_RANGE,
                        countsValue: AdminFeedbackCounts | null = null): Promise<void> {
    await router.navigateByUrl(url);
    store.feedbackCounts = countsValue;
    fixture = TestBed.createComponent(FeedbackOverseerPanelComponent);
    fixture.componentRef.setInput('range', { ...range });
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  }

  /** The list requests, without the count requests. */
  const listQueries = (): AdminFeedbackIssuesQuery[] =>
    issuesSpy.mock.calls.map(call => call[0] as AdminFeedbackIssuesQuery).filter(query => query.take !== 1);
  const lastQuery = (): AdminFeedbackIssuesQuery => listQueries()[listQueries().length - 1];
  /** The empty-state count requests, without the Has resolution counts. */
  const rangeProbes = (): AdminFeedbackIssuesQuery[] =>
    issuesSpy.mock.calls.map(call => call[0] as AdminFeedbackIssuesQuery).filter(query => query.take === 1 && !query.resolution);
  const q = <T extends HTMLElement = HTMLElement>(selector: string): T | null => el.querySelector<T>(selector);
  const cards = (): HTMLElement[] => Array.from(el.querySelectorAll<HTMLElement>('article.tl-fb-card'));
  const status = (): string => textOf(q('#tl-fb-status'));
  const facet = (id: string): FilterFacetComponent | undefined => fixture.debugElement
    .queryAll(By.directive(FilterFacetComponent))
    .map(d => d.componentInstance as FilterFacetComponent)
    .find(f => f.facetId === id);
  const facetIds = (): string[] => fixture.debugElement
    .queryAll(By.directive(FilterFacetComponent))
    .map(d => (d.componentInstance as FilterFacetComponent).facetId);
  const bar = (): SelectionBarStub => fixture.debugElement.query(By.directive(SelectionBarStub)).componentInstance as SelectionBarStub;
  const queryParams = () => router.parseUrl(router.url).queryParams;
  const chipLabels = (): (string | null)[] =>
    Array.from(el.querySelectorAll('.tl-fb-chips .gh-filter-chip')).map(c => c.getAttribute('aria-label'));
  const storedView = () => JSON.parse(localStorage.getItem(VIEW_KEY)!);

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

  describe('dates and labels', () => {
    it('reads a local day as 21 Oct 2026 and adds the local time as 14:02', () => {
      const local = new Date(2026, 9, 21, 14, 2).toISOString();
      expect(feedbackDateText(local)).toBe('21 Oct 2026');
      expect(feedbackDateTimeText(local)).toBe('21 Oct 2026 14:02');
      expect(feedbackTimeText(local)).toBe('14:02');
      expect(feedbackTimeText('not a date')).toBe('');
      expect(feedbackDateText(null)).toBe('');
    });

    it('names an occurrence\'s model source: the configuration, the user\'s own model, or Unknown', () => {
      expect(feedbackModelSourceText({ modelSource: 'system', systemConfigurationId: 7, systemConfigurationName: 'Claude (fast)' }))
        .toBe('System configuration Claude (fast)');
      expect(feedbackModelSourceText({ modelSource: 'system', systemConfigurationId: 7, systemConfigurationName: null }))
        .toBe('System configuration #7');
      expect(feedbackModelSourceText({ modelSource: 'user', systemConfigurationId: null, systemConfigurationName: null }))
        .toBe('User\'s own model');
      expect(feedbackModelSourceText({ modelSource: null, systemConfigurationId: null, systemConfigurationName: null })).toBe('Unknown');
    });

    it('cuts long build metadata of a version to its first 10 characters, and leaves short ones whole', () => {
      expect(feedbackVersionShort('1.4.1+d4452568e4f0a1b2c3d4e5f6a7b8c9d0dca4d1384')).toBe('1.4.1+d4452568e4');
      expect(feedbackVersionShort('1.4.1+d4452568e4f0')).toBe('1.4.1+d4452568e4');
      expect(feedbackVersionShort('1.4.1+d4452568e4f')).toBe('1.4.1+d4452568e4f');
      expect(feedbackVersionShort('1.4.2+abc')).toBe('1.4.2+abc');
      expect(feedbackVersionShort('1.4.2')).toBe('1.4.2');
      expect(feedbackVersionShort(null)).toBe('');
    });
  });

  describe('date range', () => {
    it('fetches without dates for All time', async () => {
      await create(undefined, { preset: 'all', start: '', end: '' });

      expect(lastQuery().startDate).toBeUndefined();
      expect(lastQuery().endDate).toBeUndefined();
      expect(facetsSpy).toHaveBeenCalledWith(undefined, undefined);
    });

    it('refetches the facets and the first page for a new range, and remembers it', async () => {
      await create(undefined, { preset: 'all', start: '', end: '' });
      issuesSpy.mockClear();
      facetsSpy.mockClear();

      fixture.componentRef.setInput('range', { preset: '30d', start: '2026-09-22', end: '2026-10-22' });
      fixture.detectChanges();

      expect(lastQuery()).toEqual(expect.objectContaining({ startDate: '2026-09-22', endDate: '2026-10-22', skip: 0 }));
      expect(facetsSpy).toHaveBeenCalledWith('2026-09-22', '2026-10-22');
      expect(storedView().range).toEqual({ preset: '30d', start: '2026-09-22', end: '2026-10-22' });
    });

    it('refetches the facets and the first page when the shell\'s Refresh bumps the token', async () => {
      await create();
      issuesSpy.mockClear();
      facetsSpy.mockClear();

      fixture.componentRef.setInput('refreshToken', 1);
      fixture.detectChanges();

      expect(facetsSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery()).toEqual(expect.objectContaining({ skip: 0 }));
    });

    it('tells the shell while a page loads', async () => {
      const pending = new Subject<AdminFeedbackIssuePage>();
      issuesSpy.mockImplementation((query?: AdminFeedbackIssuesQuery) => query?.take === 1 ? of(rangePage) : pending.asObservable());
      const states: boolean[] = [];
      await router.navigateByUrl('/admin/feedback');
      fixture = TestBed.createComponent(FeedbackOverseerPanelComponent);
      fixture.componentRef.setInput('range', { ...CUSTOM_RANGE });
      component = fixture.componentInstance;
      el = fixture.nativeElement as HTMLElement;
      component.loadingChange.subscribe(value => states.push(value));
      fixture.detectChanges();

      expect(states).toEqual([true]);
      pending.next(page([issue(1)]));
      expect(states).toEqual([true, false]);
    });
  });

  describe('issue cards', () => {
    it('fetches New and Regressed issues of the range, last seen first, 25 at a time', async () => {
      await create();

      expect(lastQuery()).toEqual(expect.objectContaining({
        startDate: '2026-09-21', endDate: '2026-10-21', statuses: ['new', 'regressed'], sort: 'lastSeen', skip: 0, take: 25
      }));
      expect(lastQuery().categories).toBeUndefined();
      expect(lastQuery().modelIds).toBeUndefined();
      expect(lastQuery().search).toBeUndefined();
      expect(lastQuery().ids).toBeUndefined();
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
      const models = card.querySelector('.tl-fb-kicker ul.tl-fb-model-list')!;
      expect(models.getAttribute('role')).toBe('list');
      expect(models.getAttribute('aria-label')).toBe('Models');
      expect(Array.from(models.querySelectorAll('li')).map(li => identityTextOf(li))).toEqual(['claude-x Anthropic', 'gpt-x OpenAI']);
      expect(textOf(card.querySelector('.tl-fb-summary'))).toBe('The wiki has no page for it.');

      const metrics = Array.from(card.querySelectorAll('.tl-fb-metrics > .tl-fb-metric'));
      expect(metrics.map(m => textOf(m.querySelector('dt')))).toEqual(['Occurrences', 'Users', 'First seen', 'Last seen']);
      expect(Array.from(card.querySelectorAll('.tl-fb-metric-occurrences dd')).map(d => textOf(d))).toEqual(['12', '9 retained']);
      expect(textOf(card.querySelector('.tl-fb-metric-users dd'))).toBe('3');
      expect(Array.from(card.querySelectorAll('.tl-fb-metric-first dd')).map(d => textOf(d))).toEqual(
        [feedbackDateText('2026-09-02T08:00:00Z'), feedbackTimeText('2026-09-02T08:00:00Z')]);
      expect(Array.from(card.querySelectorAll('.tl-fb-metric-last dd')).map(d => textOf(d))).toEqual(
        [feedbackDateText('2026-10-20T12:30:00Z'), feedbackTimeText('2026-10-20T12:30:00Z')]);
      expect(card.querySelector('.tl-fb-metric-last time')!.getAttribute('datetime')).toBe('2026-10-20T12:30:00Z');
      expect(card.querySelector('.tl-fb-metric-last .tl-fb-metric-note')).not.toBeNull();

      const facts = Array.from(card.querySelectorAll('.tl-fb-facts > div')).map(f => [textOf(f.querySelector('dt')), textOf(f.querySelector('dd'))]);
      expect(facts).toEqual([
        ['Latest expected location', 'wiki page Dilithium crystal'],
        ['Latest related tools', 'GnollHack wiki search, GnollHack wiki item lookup'],
        ["Model's suspected cause (hypothesis)", 'Corpus or tool limit'],
        ['Latest version', '1.4.2+abc'],
        ['Model sources', 'System configuration 2 · User\'s own model 1']
      ]);
      expect(card.querySelector('.tl-fb-fact-providers')).toBeNull();
      expect(card.querySelector('.tl-fb-facts .tl-fb-muted')).toBeNull();
      expect(card.querySelector('.tl-fb-crosslink')).toBeNull();
    });

    it('heads the latest facts as Latest report, in a section it labels, and leaves out Triage while nothing is triaged', async () => {
      await create();
      const card = cards()[0];

      const sections = Array.from(card.querySelectorAll<HTMLElement>('.tl-fb-fact-groups > section'));
      expect(sections.length).toBe(1);
      const heading = card.querySelector('#' + sections[0].getAttribute('aria-labelledby'))!;
      expect(heading.tagName).toBe('H5');
      expect(heading.classList.contains('gh-section-title')).toBe(true);
      expect(textOf(heading)).toBe('Latest report');
      expect(sections[0].querySelector('dl.tl-fb-facts')).not.toBeNull();
    });

    it('groups Resolved via, the fixed-in version, the note and the resolution under Triage once one is set', async () => {
      nextPage = page([issue(1, { triageNote: 'Seen.' }), issue(2, { fixedInVersion: '1.4.3' }), issue(3)], 3);
      await create();
      const [first, second, third] = cards();

      const triage = first.querySelector<HTMLElement>('.tl-fb-fact-group-triage')!;
      expect(textOf(triage.querySelector('#' + triage.getAttribute('aria-labelledby')))).toBe('Triage');
      expect(Array.from(triage.querySelectorAll('dt')).map(d => textOf(d))).toEqual(['Note']);
      expect(first.querySelector('.tl-fb-fact-group-latest .tl-fb-fact-note')).toBeNull();
      expect(textOf(second.querySelector('.tl-fb-fact-group-triage .tl-fb-fact-fixed dd'))).toBe('1.4.3');
      expect(third.querySelector('.tl-fb-fact-group-triage')).toBeNull();
    });

    it('leaves out the retained qualifier while every occurrence remains, and says Not recorded, muted, for missing facts', async () => {
      nextPage = page([issue(1, {
        latestExpectedLocation: null, latestRelatedTools: [], latestSuspectedCause: null, latestAppVersion: null,
        providers: [], modelSources: { system: 0, user: 0, unknown: 0 }
      })]);
      await create();
      const card = cards()[0];

      expect(card.querySelector('.tl-fb-metric-occurrences .tl-fb-metric-note')).toBeNull();
      const values = Array.from(card.querySelectorAll('.tl-fb-facts dd'));
      expect(values.map(d => textOf(d))).toEqual(['Not recorded', 'Not recorded', 'Not recorded', 'Not recorded', 'Not recorded']);
      expect(values.every(d => d.classList.contains('tl-fb-muted'))).toBe(true);
      expect(getComputedStyle(values[0]).fontStyle).toBe('italic');
      expect(values[3].classList.contains('tl-fb-mono')).toBe(false);
    });

    it('names each model with its thinking level, reasoning mode and provider badges, then the rest as +N more', async () => {
      nextPage = page([issue(1, {
        modelIdentities: [
          identity('gpt-x', { modelLabel: 'GPT X', provider: 'openai', thinkingLevel: 'high', reasoningMode: 'pro' }),
          identity('gpt-x', { modelLabel: 'GPT X', provider: 'openai', thinkingLevel: 'low' }),
          identity('custom-x', { modelLabel: '', provider: null })
        ],
        moreModelIdentities: 3
      })]);
      await create();
      const items = Array.from(cards()[0].querySelectorAll<HTMLElement>('.tl-fb-model-list > li'));

      expect(items.length).toBe(4);
      const first = items[0].querySelector('app-model-identity')!;
      expect(textOf(first.querySelector('.model-identity-name'))).toBe('GPT X');
      expect(textOf(first.querySelector('.thinking-badge'))).toBe('thinking level high');
      expect(textOf(first.querySelector('.reasoning-badge'))).toBe('reasoning mode pro');
      expect(textOf(first.querySelector('app-provider-badge'))).toBe('OpenAI');
      expect(items[1].querySelector('.reasoning-badge')).toBeNull();
      expect(textOf(items[2].querySelector('.model-identity-name'))).toBe('custom-x');
      expect(items[2].querySelector('app-provider-badge')).toBeNull();
      expect(textOf(items[3])).toBe('+3 more');
      expect(items[3].classList.contains('tl-fb-model-more')).toBe(true);
    });

    it('leaves the models out of the kicker when the issue has none', async () => {
      nextPage = page([issue(1, { modelIdentities: [], moreModelIdentities: 0 })]);
      await create();
      const card = cards()[0];

      expect(card.querySelector('.tl-fb-model-list')).toBeNull();
      expect(card.querySelector('.tl-fb-kicker .tl-fb-sep')).toBeNull();
    });

    it('edges the card with its status, the badge carrying the word', async () => {
      nextPage = page([issue(1, { status: 'acknowledged' }), issue(2, { status: 'dismissed' })], 2);
      await create();
      const [first, second] = cards();

      expect(first.getAttribute('data-status')).toBe('acknowledged');
      expect(getComputedStyle(first).boxShadow).toContain('inset');
      expect(second.getAttribute('data-status')).toBe('dismissed');
      expect(getComputedStyle(second).backgroundImage).toContain('repeating-linear-gradient');
    });

    it('shows the fixed-in version and the resolution as plain text with its line breaks', async () => {
      nextPage = page([issue(1, { fixedInVersion: '1.4.3', resolution: 'Added the page.\n<b>Verified</b> in the chat.' })]);
      await create();
      const card = cards()[0];

      expect(textOf(card.querySelector('.tl-fb-fact-fixed dd'))).toBe('1.4.3');
      const resolution = card.querySelector<HTMLElement>('.tl-fb-fact-resolution dd')!;
      expect(resolution.textContent).toBe('Added the page.\n<b>Verified</b> in the chat.');
      expect(resolution.querySelector('b')).toBeNull();
      expect(getComputedStyle(resolution).whiteSpace).toBe('pre-wrap');
    });

    it('shows the triage note as a fact only when there is one, between the fixed-in version and the resolution', async () => {
      nextPage = page([
        issue(1, { triageNote: 'Wiki page coming.\nCheck the guide.', fixedInVersion: '1.4.3', resolution: 'Added the page.' }),
        issue(2)
      ], 2);
      await create();
      const [first, second] = cards();

      expect(Array.from(first.querySelectorAll('.tl-fb-facts dt')).slice(-3).map(d => textOf(d))).toEqual(['Fixed in version', 'Note', 'Resolution']);
      const note = first.querySelector<HTMLElement>('.tl-fb-fact-note dd')!;
      expect(note.textContent).toBe('Wiki page coming.\nCheck the guide.');
      expect(getComputedStyle(note).whiteSpace).toBe('pre-wrap');
      expect(second.querySelector('.tl-fb-fact-note')).toBeNull();
    });

    it('shows a long build hash of the latest version cut short, with the whole version in its title', async () => {
      const version = '1.4.1+d4452568e4f0a1b2c3d4e5f6a7b8c9d0dca4d1384';
      nextPage = page([issue(1, { latestAppVersion: version }), issue(2)], 2);
      await create();
      const [first, second] = cards();

      const value = first.querySelector('.tl-fb-fact-version dd')!;
      expect(textOf(value)).toBe('1.4.1+d4452568e4');
      expect(value.getAttribute('title')).toBe(version);
      expect(textOf(second.querySelector('.tl-fb-fact-version dd'))).toBe('1.4.2+abc');
      expect(second.querySelector('.tl-fb-fact-version dd')!.getAttribute('title')).toBeNull();
    });

    it('links an issue also reported in GnollBench to that sub-tab, searched for its subject in its status', async () => {
      nextPage = page([issue(1, { alsoInGnollBench: { selfReportCount: 4, status: 'resolved' } })]);
      await create('/admin/feedback?category=missing_information&sessionId=s1');
      const line = cards()[0].querySelector('.tl-fb-crosslink')!;

      expect(textOf(line.querySelector('span'))).toBe('Also reported in GnollBench: 4 self-reports, Resolved.');
      const link = line.querySelector<HTMLAnchorElement>('a')!;
      expect(textOf(link)).toBe('Open in GnollBench feedback: Dilithium crystal');
      const target = router.parseUrl(link.getAttribute('href')!);
      expect(target.root.children['primary'].segments.map(s => s.path)).toEqual(['admin', 'feedback', 'gnollbench']);
      expect(target.queryParams).toEqual({ sessionId: 's1', q: 'Dilithium crystal', status: 'resolved' });
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
      expect(textOf(card.querySelector('details.tl-fb-occurrences > summary'))).toBe('Occurrences 12');
      expect(textOf(card.querySelector('details.tl-fb-occurrences > summary .tl-fb-count'))).toBe('12');
      expect(occurrencesSpy).not.toHaveBeenCalled();

      const details = openOccurrences(card);

      expect(occurrencesSpy).toHaveBeenCalledWith(1, 0, 10, {}, false);
      expect(details.querySelectorAll('li.tl-fb-occ').length).toBe(10);
      expect(details.querySelector('.tl-fb-occ-scope')).toBeNull();
      const more = details.querySelector<HTMLButtonElement>('.tl-fb-occ-more')!;
      expect(textOf(more)).toBe('Show 2 more');

      occurrencesSpy.mockReturnValue(of<AdminFeedbackOccurrencePage>({ totalCount: 12, rows: [occurrence(90), occurrence(89)] }));
      more.click();
      fixture.detectChanges();

      expect(occurrencesSpy).toHaveBeenLastCalledWith(1, 10, 10, {}, false);
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
          occurrence(4, { details: 'Short <b>note</b>.', modelSource: 'user', systemConfigurationId: null, systemConfigurationName: null, groupNames: [] })
        ]
      }));
      await create();
      const details = openOccurrences(cards()[0]);
      const [first, second] = Array.from(details.querySelectorAll<HTMLElement>('li.tl-fb-occ'));

      expect(textOf(first.querySelector('h5'))).toBe(feedbackDateTimeText('2026-10-20T12:30:00Z'));
      const factsOf = (item: HTMLElement) => Object.fromEntries(Array.from(item.querySelectorAll('.tl-fb-occ-facts > div'))
        .map(f => [textOf(f.querySelector('dt')), identityTextOf(f.querySelector('dd'))]));
      expect(factsOf(first)).toEqual(expect.objectContaining({
        'User': 'Account erased',
        'Groups': 'Testers',
        'Model': 'claude-x Anthropic',
        'Model source': 'System configuration Claude (fast)',
        'Version': '1.4.2+abc',
        'Conversation': 'Session 812 · message 4410',
        'Searched for': 'wiki_search "dilithium"',
        'Expected location': 'wiki page Dilithium crystal',
        'Noticed via': 'The user corrected it'
      }));
      expect(factsOf(second)).toEqual(expect.objectContaining({ 'Groups': 'No group', 'Model source': 'User\'s own model' }));
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

    it('tags an issue and an occurrence whose texts hold hidden characters', async () => {
      const rlo = String.fromCodePoint(0x202e);
      nextPage = page([issue(1, { summary: 'Reversed ' + rlo + 'txet' }), issue(2)]);
      occurrencesSpy.mockReturnValue(of<AdminFeedbackOccurrencePage>({
        totalCount: 1,
        rows: [occurrence(5, { truncated: true, redacted: true, details: 'Zero' + String.fromCodePoint(0x200b) + 'width' })]
      }));
      await create();
      const [first, second] = cards();

      const tag = first.querySelector<HTMLElement>('.tl-fb-kicker .tl-fb-mark-hidden')!;
      expect(textOf(tag)).toBe('Hidden characters');
      expect(tag.getAttribute('title')).toBe('Invisible characters: U+202E');
      expect(textOf(tag.nextElementSibling)).toBe(': U+202E');
      expect(second.querySelector('.tl-fb-mark-hidden')).toBeNull();

      const details = openOccurrences(first);
      const item = details.querySelector<HTMLElement>('li.tl-fb-occ')!;
      expect(Array.from(item.querySelectorAll('.tl-fb-occ-marks .gh-tag')).map(t => textOf(t))).toEqual(['Truncated', 'Redacted', 'Hidden characters']);
    });

    it('renders hostile markup in a subject and a summary as text', async () => {
      const subject = '<img src=x onerror="window.__fbXss=1">';
      const summary = '<script>window.__fbXss=1</script>';
      nextPage = page([issue(1, { subject, summary })]);
      await create();
      await settle(20);
      const [card] = cards();

      expect(card.querySelector('.tl-fb-title label')!.textContent).toBe(subject);
      expect(card.querySelector('.tl-fb-summary')!.textContent).toBe(summary);
      expect(card.querySelector('img')).toBeNull();
      expect(card.querySelector('script')).toBeNull();
      expect((window as unknown as Record<string, unknown>)['__fbXss']).toBeUndefined();
    });

    it('narrows the occurrences by the occurrence filters, and Show all occurrences ignores them', async () => {
      occurrencesSpy.mockReturnValue(of<AdminFeedbackOccurrencePage>({ totalCount: 1, rows: [occurrence(5)] }));
      await create('/admin/feedback?model=claude-x&source=system');
      const details = openOccurrences(cards()[0]);

      expect(occurrencesSpy).toHaveBeenLastCalledWith(1, 0, 10,
        expect.objectContaining({ modelIds: ['claude-x'], sources: ['system'] }), false);
      expect(textOf(details.querySelector('.tl-fb-occ-scope-text'))).toBe('Only the occurrences that match the filters are shown.');
      const toggle = details.querySelector<HTMLButtonElement>('.tl-fb-occ-all')!;
      expect(toggle.getAttribute('aria-pressed')).toBe('false');

      toggle.click();
      fixture.detectChanges();

      expect(occurrencesSpy).toHaveBeenLastCalledWith(1, 0, 10, {}, true);
      expect(toggle.getAttribute('aria-pressed')).toBe('true');
      expect(textOf(details.querySelector('.tl-fb-occ-scope-text'))).toBe('All stored occurrences are shown.');
    });
  });

  describe('filters', () => {
    it('offers every status with the counts over all dates, New and Regressed selected', async () => {
      await create(undefined, undefined, counts({ new: 3, regressed: 1, resolved: 7 }));

      const statusFacet = facet('tl-fb-facet-status')!;
      expect(statusFacet.options.map(o => `${o.label} ${o.count}`)).toEqual(
        ['New 3', 'Regressed 1', 'Acknowledged 0', 'Resolved 7', 'Dismissed 0']);
      expect([...statusFacet.selected]).toEqual(['new', 'regressed']);
      expect(chipLabels()).toEqual(['Remove filter Status: New', 'Remove filter Status: Regressed']);
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

    it('lists every facet the range has options for, in order, with the range\'s counts', async () => {
      rangePage = page([], 3);
      await create();

      expect(facetIds()).toEqual(['tl-fb-facet-status', 'tl-fb-facet-category', 'tl-fb-facet-model', 'tl-fb-facet-provider',
        'tl-fb-facet-user', 'tl-fb-facet-group', 'tl-fb-facet-source', 'tl-fb-facet-tool', 'tl-fb-facet-resolution']);
      const options = (id: string) => facet(id)!.options.map(o => `${o.label} ${o.count}`);
      expect(options('tl-fb-facet-category')).toEqual(['Missing information 4', 'Tool problem 2', 'Own mistake 0']);
      expect(facet('tl-fb-facet-category')!.mode).toBe('multiple');
      expect(options('tl-fb-facet-model')).toEqual(['claude-x 5', 'gpt-x 3']);
      expect(options('tl-fb-facet-provider')).toEqual(['Anthropic 5', 'OpenAI 3']);
      expect(options('tl-fb-facet-user')).toEqual(['alice 4', 'bob 1']);
      expect(options('tl-fb-facet-group')).toEqual(['Testers 2', 'No group 1']);
      expect(options('tl-fb-facet-source')).toEqual(['System configuration 6', 'User\'s own model 2']);
      expect(options('tl-fb-facet-tool')).toEqual(['GnollHack wiki item lookup 1', 'GnollHack wiki search 3']);
      expect(options('tl-fb-facet-resolution')).toEqual(['With a resolution 3', 'Without a resolution 3']);
      expect(facet('tl-fb-facet-resolution')!.mode).toBe('single');
      expect(issuesSpy).toHaveBeenCalledWith({ startDate: '2026-09-21', endDate: '2026-10-21', resolution: 'with', take: 1 });
      expect(issuesSpy).toHaveBeenCalledWith({ startDate: '2026-09-21', endDate: '2026-10-21', resolution: 'without', take: 1 });
    });

    it('refetches page 1 for each facet\'s choice and keeps it in the address', async () => {
      await create();
      expect(facet('tl-fb-facet-model')!.options.map(o => o.value)).toEqual(['claude-x', 'gpt-x']);

      choose('tl-fb-facet-status', ['resolved']);
      expect(lastQuery()).toEqual(expect.objectContaining({ statuses: ['resolved'], skip: 0 }));

      choose('tl-fb-facet-category', ['tool_problem', 'own_mistake']);
      expect(lastQuery()).toEqual(expect.objectContaining({ statuses: ['resolved'], categories: ['tool_problem', 'own_mistake'], skip: 0 }));

      choose('tl-fb-facet-model', ['gpt-x']);
      choose('tl-fb-facet-provider', ['OpenAI']);
      choose('tl-fb-facet-user', ['u-1']);
      choose('tl-fb-facet-group', ['none']);
      choose('tl-fb-facet-source', ['user']);
      choose('tl-fb-facet-tool', ['wiki_search']);
      choose('tl-fb-facet-resolution', ['without']);
      expect(lastQuery()).toEqual(expect.objectContaining({
        categories: ['tool_problem', 'own_mistake'], modelIds: ['gpt-x'], providers: ['OpenAI'], users: ['u-1'], groups: ['none'],
        sources: ['user'], tools: ['wiki_search'], resolution: 'without', skip: 0
      }));

      await settle();
      expect(queryParams()).toEqual(expect.objectContaining({
        status: 'resolved', category: 'tool_problem,own_mistake', model: 'gpt-x', provider: 'OpenAI', user: 'u-1', group: 'none',
        source: 'user', tool: 'wiki_search', resolution: 'without'
      }));
      expect(router.url.startsWith('/admin/feedback?')).toBe(true);
      expect(chipLabels()).toEqual([
        'Remove filter Status: Resolved', 'Remove filter Category: Tool problem', 'Remove filter Category: Own mistake',
        'Remove filter Model: gpt-x', 'Remove filter Provider: OpenAI', 'Remove filter User: alice', 'Remove filter Group: No group',
        'Remove filter Model source: User\'s own model', 'Remove filter Tool: GnollHack wiki search', 'Remove filter Has resolution: Without a resolution'
      ]);
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

    it('takes its filters from the address it opens on, and keeps a selected value the range does not list', async () => {
      await create('/admin/feedback?status=resolved,dismissed,bogus&category=own_mistake&model=gpt-x&config=9&version=1.4.0&q=crystal');

      expect(listQueries().length).toBe(1);
      expect(lastQuery()).toEqual(expect.objectContaining({
        statuses: ['resolved', 'dismissed'], categories: ['own_mistake'], modelIds: ['gpt-x'], configs: ['9'], versions: ['1.4.0'],
        search: 'crystal'
      }));
      expect(q<HTMLInputElement>('#tl-fb-search')!.value).toBe('crystal');
      expect(chipLabels()).toEqual([
        'Remove filter Status: Resolved', 'Remove filter Status: Dismissed', 'Remove filter Category: Own mistake',
        'Remove filter Model: gpt-x', 'Remove filter Configuration: 9', 'Remove filter Version: 1.4.0', 'Remove filter Search: “crystal”'
      ]);
      expect(facet('tl-fb-facet-config')!.options.map(o => `${o.label} ${o.count}`)).toEqual(['Claude (fast) 3', '9 0']);
    });

    it('opens a GnollBench sub-tab link on the issues it names, with no status filter, shown as removable chips', async () => {
      await create('/admin/feedback?ids=42,x,0,7&status=all');

      expect(lastQuery()).toEqual(expect.objectContaining({ ids: [42, 7] }));
      expect(lastQuery().statuses).toBeUndefined();
      expect(facetIds()).not.toContain('tl-fb-facet-ids');
      expect(chipLabels()).toEqual(['Remove filter Issue: #42', 'Remove filter Issue: #7']);

      q<HTMLButtonElement>('.tl-fb-chips .gh-filter-chip')!.click();
      fixture.detectChanges();
      expect(lastQuery()).toEqual(expect.objectContaining({ ids: [7] }));
      await settle();
      expect(queryParams()).toEqual(expect.objectContaining({ ids: '7', status: 'all' }));
    });

    it('applies an address change made by Back or Forward, and reads status=all as no status filter', async () => {
      await create();

      await router.navigateByUrl('/admin/feedback?status=all&category=tool_problem');
      fixture.detectChanges();

      expect(lastQuery().statuses).toBeUndefined();
      expect(lastQuery().categories).toEqual(['tool_problem']);
    });

    it('ignores an address of another sub-tab or tab', async () => {
      await create();
      issuesSpy.mockClear();

      await router.navigateByUrl('/admin/feedback/gnollbench?status=resolved&q=wand');
      fixture.detectChanges();

      expect(listQueries().length).toBe(0);
    });

    it('waits for its own address before it fetches anything', async () => {
      await create('/admin/users?sessionId=s1');
      expect(listQueries().length).toBe(0);
      expect(facetsSpy).not.toHaveBeenCalled();

      await router.navigateByUrl('/admin/feedback?sessionId=s1');
      fixture.detectChanges();

      expect(listQueries().length).toBe(1);
      expect(router.url).toBe('/admin/feedback?sessionId=s1');
    });

    it('refetches page 1 for the search once typing pauses, keeps it in the address, and Escape clears it at once', async () => {
      await create();
      issuesSpy.mockClear();
      const search = q<HTMLInputElement>('#tl-fb-search')!;
      expect(textOf(q('label[for="tl-fb-search"]'))).toBe('Search issues by subject, summary, details, searched for or expected location');

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

    it('sorts by seven orders, refetches and remembers the choice in the view, not the address', async () => {
      await create();
      const sort = q<HTMLSelectElement>('#tl-fb-sort')!;
      expect(Array.from(sort.options).map(o => o.textContent?.trim())).toEqual(
        ['Last seen', 'First seen', 'Most occurrences', 'Most users', 'Most regressions', 'Recently triaged', 'Subject (A–Z)']);
      expect(Array.from(sort.options).map(o => o.value)).toEqual(
        ['lastSeen', 'firstSeen', 'count', 'users', 'regressions', 'triaged', 'subject']);
      expect(textOf(el.querySelector('label[for="tl-fb-sort"]'))).toBe('Sort by');

      selectValue(sort, 'regressions');

      expect(lastQuery()).toEqual(expect.objectContaining({ sort: 'regressions', skip: 0 }));
      expect(storedView()).toEqual(expect.objectContaining({ version: 1, sort: 'regressions' }));
      expect(queryParams()['sort']).toBeUndefined();
    });

    it('opens on the sort it kept before the sub-tabs, and leaves that key in place', async () => {
      localStorage.setItem('overseer.admin.telemetry.feedback.view', JSON.stringify({ version: 1, sort: 'firstSeen' }));
      await create();

      expect(lastQuery().sort).toBe('firstSeen');
      expect(q<HTMLSelectElement>('#tl-fb-sort')!.value).toBe('firstSeen');
      expect(localStorage.getItem('overseer.admin.telemetry.feedback.view')).not.toBeNull();
    });

    it('opens on the remembered view when the address names no filter, and writes it into the address', async () => {
      saveFeedbackView('overseer', {
        version: 1, sort: 'count', filters: { status: ['resolved'], category: ['tool_problem'], user: ['u-1'] }, search: 'wand',
        range: CUSTOM_RANGE
      });
      await create('/admin/feedback?sessionId=s1');

      expect(listQueries().length).toBe(1);
      expect(lastQuery()).toEqual(expect.objectContaining({
        statuses: ['resolved'], categories: ['tool_problem'], users: ['u-1'], search: 'wand', sort: 'count'
      }));
      expect(q<HTMLInputElement>('#tl-fb-search')!.value).toBe('wand');
      await settle();
      expect(queryParams()).toEqual({ sessionId: 's1', status: 'resolved', category: 'tool_problem', user: 'u-1', q: 'wand' });
      expect(listQueries().length).toBe(1);
    });

    it('remembers no status filter as none, and an empty view as the default', async () => {
      saveFeedbackView('overseer', { version: 1, sort: 'lastSeen', filters: { status: [] }, search: '', range: CUSTOM_RANGE });
      await create();
      expect(lastQuery().statuses).toBeUndefined();
      await settle();
      expect(queryParams()['status']).toBe('all');
    });

    it('lets an address that names a filter win over the remembered view, and remembers it', async () => {
      saveFeedbackView('overseer', {
        version: 1, sort: 'count', filters: { status: ['resolved'], category: ['tool_problem'] }, search: '', range: CUSTOM_RANGE
      });
      await create('/admin/feedback?q=crystal');

      expect(lastQuery()).toEqual(expect.objectContaining({ statuses: ['new', 'regressed'], search: 'crystal', sort: 'count' }));
      expect(lastQuery().categories).toBeUndefined();
      expect(router.url).toBe('/admin/feedback?q=crystal');
      expect(storedView()).toEqual(expect.objectContaining({
        sort: 'count', search: 'crystal', range: CUSTOM_RANGE,
        filters: expect.objectContaining({ status: ['new', 'regressed'], category: [] })
      }));
    });

    it('remembers every filter change in the view', async () => {
      await create();

      choose('tl-fb-facet-tool', ['wiki_search']);

      expect(loadFeedbackView('overseer')).toEqual(expect.objectContaining({
        version: 1, sort: 'lastSeen', search: '', range: CUSTOM_RANGE,
        filters: expect.objectContaining({ status: ['new', 'regressed'], tool: ['wiki_search'], model: [] })
      }));
    });

    it('removes a chip\'s filter, refetches and focuses the chip in its place', async () => {
      await create();

      const first = q<HTMLButtonElement>('.tl-fb-chips .gh-filter-chip')!;
      first.click();
      fixture.detectChanges();

      expect(lastQuery().statuses).toEqual(['regressed']);
      expect(document.activeElement?.getAttribute('aria-label')).toBe('Remove filter Status: Regressed');
    });

    describe('Resolved via', () => {
      const RESOLVED_VIA_IDS = /knowledge_base|server_configuration|\bnone\b/;
      const chipTexts = (): string[] => Array.from(el.querySelectorAll('.tl-fb-chips .gh-filter-chip'))
        .map(c => `${textOf(c.querySelector('.gh-filter-chip-facet'))} ${textOf(c.querySelector('.gh-filter-chip-value'))}`);

      beforeEach(() => {
        facetsSpy.mockReturnValue(of(facets({
          resolvedVia: [
            { value: 'wiki', label: '', count: 2 },
            { value: 'server_configuration', label: '', count: 1 },
            { value: 'none', label: '', count: 5 }
          ]
        })));
      });

      it('lists the facet right after Status, its options in labels with counts and None recorded last', async () => {
        await create();

        expect(facetIds().slice(0, 3)).toEqual(['tl-fb-facet-status', 'tl-fb-facet-resolvedVia', 'tl-fb-facet-category']);
        const resolvedVia = facet('tl-fb-facet-resolvedVia')!;
        expect(resolvedVia.label).toBe('Resolved via');
        expect(resolvedVia.mode).toBe('multiple');
        expect(resolvedVia.options.map(o => o.value)).toEqual(['wiki', 'server_configuration', 'none']);
        expect(resolvedVia.options.map(o => `${o.label} ${o.count}`)).toEqual(
          ['GnollHack wiki 2', 'Server configuration 1', 'None recorded 5']);
        expect(resolvedVia.options.map(o => o.label).join(' ')).not.toMatch(RESOLVED_VIA_IDS);
      });

      it('is not listed with one option and no selection', async () => {
        facetsSpy.mockReturnValue(of(facets({ resolvedVia: [{ value: 'none', label: '', count: 5 }] })));
        await create();

        expect(facetIds()).not.toContain('tl-fb-facet-resolvedVia');
      });

      it('refetches page 1 with the chosen ids, shows them as chips in labels, and keeps them in the address and the view', async () => {
        await create();

        choose('tl-fb-facet-resolvedVia', ['server_configuration', 'none']);

        expect(lastQuery()).toEqual(expect.objectContaining({
          resolvedVia: ['server_configuration', 'none'], statuses: ['new', 'regressed'], skip: 0
        }));
        expect(chipLabels()).toEqual([
          'Remove filter Status: New', 'Remove filter Status: Regressed',
          'Remove filter Resolved via: Server configuration', 'Remove filter Resolved via: None recorded'
        ]);
        expect(chipTexts()).toContainEqual('Resolved via: Server configuration');
        expect(chipTexts()).toContainEqual('Resolved via: None recorded');
        expect(textOf(q('.tl-fb-chips'))).not.toMatch(RESOLVED_VIA_IDS);
        await settle();
        expect(queryParams()['resolvedVia']).toBe('server_configuration,none');
        expect(queryParams()['status']).toBeUndefined();
        expect(loadFeedbackView('overseer')).toEqual(expect.objectContaining({
          filters: expect.objectContaining({ resolvedVia: ['server_configuration', 'none'] })
        }));
      });

      it('takes it from the address it opens on, dropping an unknown value, and Clear filters removes it', async () => {
        await create('/admin/feedback?resolvedVia=none,bogus,wiki');

        expect(listQueries().length).toBe(1);
        expect(lastQuery()).toEqual(expect.objectContaining({ resolvedVia: ['wiki', 'none'] }));
        expect([...facet('tl-fb-facet-resolvedVia')!.selected]).toEqual(['wiki', 'none']);
        expect(chipLabels()).toEqual([
          'Remove filter Status: New', 'Remove filter Status: Regressed',
          'Remove filter Resolved via: GnollHack wiki', 'Remove filter Resolved via: None recorded'
        ]);

        q<HTMLButtonElement>('#tl-fb-clear')!.click();
        fixture.detectChanges();

        expect(lastQuery().resolvedVia).toBeUndefined();
        await settle();
        expect(queryParams()['resolvedVia']).toBeUndefined();
        expect(storedView().filters.resolvedVia).toEqual([]);
      });

      it('keeps a selected value the range does not list, in its label, so it can still be cleared', async () => {
        await create('/admin/feedback?resolvedVia=knowledge_base');

        expect(facet('tl-fb-facet-resolvedVia')!.options.map(o => `${o.label} ${o.count}`)).toEqual(
          ['GnollHack wiki 2', 'Server configuration 1', 'None recorded 5', 'Knowledge base 0']);
        expect(chipLabels()).toContainEqual('Remove filter Resolved via: Knowledge base');
      });

      it('takes it from the remembered view when the address names no filter, and writes it into the address', async () => {
        saveFeedbackView('overseer', {
          version: 1, sort: 'lastSeen', filters: { resolvedVia: ['knowledge_base'] }, search: '', range: CUSTOM_RANGE
        });
        await create('/admin/feedback?sessionId=s1');

        expect(lastQuery()).toEqual(expect.objectContaining({ resolvedVia: ['knowledge_base'], statuses: ['new', 'regressed'] }));
        await settle();
        expect(queryParams()).toEqual({ sessionId: 's1', resolvedVia: 'knowledge_base' });
      });

      it('removes one value with its chip', async () => {
        await create('/admin/feedback?resolvedVia=wiki,none');

        Array.from(el.querySelectorAll<HTMLButtonElement>('.tl-fb-chips .gh-filter-chip'))
          .find(c => c.getAttribute('aria-label') === 'Remove filter Resolved via: GnollHack wiki')!.click();
        fixture.detectChanges();

        expect(lastQuery().resolvedVia).toEqual(['none']);
        await settle();
        expect(queryParams()['resolvedVia']).toBe('none');
      });

      it('never narrows the occurrences, so it offers no toggle to show them all', async () => {
        occurrencesSpy.mockReturnValue(of<AdminFeedbackOccurrencePage>({ totalCount: 1, rows: [occurrence(5)] }));
        await create('/admin/feedback?resolvedVia=wiki');
        const details = openOccurrences(cards()[0]);

        expect(occurrencesSpy).toHaveBeenLastCalledWith(1, 0, 10, {}, false);
        expect(details.querySelector('.tl-fb-occ-all')).toBeNull();
      });
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
      expect(rangeProbes().length).toBe(0);
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

  describe('selection', () => {
    const checkbox = (card: HTMLElement) => card.querySelector<HTMLInputElement>('input.tl-fb-select-input')!;

    it('gives each card a checkbox, top left, named for the issue, whose label is the title', async () => {
      await create();
      const card = cards()[0];
      const box = checkbox(card);

      expect(box.type).toBe('checkbox');
      expect(box.id).toBe('tl-fb-1-select');
      expect(box.getAttribute('aria-label')).toBe('Select issue: Dilithium crystal');
      expect(card.querySelector('h4 label')!.getAttribute('for')).toBe('tl-fb-1-select');
      expect(box.checked).toBe(false);
    });

    it('selects by id, remembers the selection and hands it to the selection bar', async () => {
      nextPage = page([issue(1), issue(2)], 40);
      await create();
      expect(bar().source).toBe('overseer');
      expect(bar().matchingCount).toBe(40);
      expect(bar().selectedIds).toEqual([]);

      checkbox(cards()[1]).click();
      fixture.detectChanges();

      expect(bar().selectedIds).toEqual([2]);
      expect(loadOverseerSelection()).toEqual([2]);
      expect(checkbox(cards()[1]).checked).toBe(true);

      (cards()[0].querySelector('h4 label') as HTMLLabelElement).click();
      fixture.detectChanges();
      expect(bar().selectedIds).toEqual([2, 1]);

      checkbox(cards()[1]).click();
      fixture.detectChanges();
      expect(loadOverseerSelection()).toEqual([1]);
    });

    it('keeps the selection across a reload and drops ids the server no longer has', async () => {
      saveOverseerSelection([1, 7]);
      idsSpy.mockImplementation((filters?: AdminFeedbackIssueFilters) => {
        const ids = (filters?.ids ?? []).filter(id => id !== 7);
        return of<AdminFeedbackIssueIds>({ ids, totalCount: ids.length, truncated: false });
      });
      await create();

      expect(idsSpy).toHaveBeenCalledWith({ ids: [1, 7] });
      expect(loadOverseerSelection()).toEqual([1]);
      expect(bar().selectedIds).toEqual([1]);
      expect(checkbox(cards()[0]).checked).toBe(true);
    });

    it('counts the selected issues the filters leave out', async () => {
      saveOverseerSelection([1, 9]);
      idsSpy.mockImplementation((filters?: AdminFeedbackIssueFilters) => {
        const ids = filters?.statuses ? [1] : [...(filters?.ids ?? [])];
        return of<AdminFeedbackIssueIds>({ ids, totalCount: ids.length, truncated: false });
      });
      await create();

      expect(idsSpy).toHaveBeenCalledWith(expect.objectContaining({ ids: [1, 9], statuses: ['new', 'regressed'] }));
      expect(bar().notShownCount).toBe(1);
    });

    it('shows the selected issues alone with Show selected only, sending their ids without the filters', async () => {
      saveOverseerSelection([1, 4]);
      await create('/admin/feedback?category=tool_problem');

      bar().showSelectedOnlyChange.emit(true);
      fixture.detectChanges();

      expect(lastQuery()).toEqual({ ids: [1, 4], sort: 'lastSeen', skip: 0, take: 25 });
      expect(bar().showSelectedOnly).toBe(true);
      expect(bar().notShownCount).toBe(0);

      bar().showSelectedOnlyChange.emit(false);
      fixture.detectChanges();
      expect(lastQuery()).toEqual(expect.objectContaining({ categories: ['tool_problem'] }));
      expect(lastQuery().ids).toBeUndefined();
    });

    it('says when Show selected only leaves nothing', async () => {
      nextPage = page([], 0);
      await create();

      bar().showSelectedOnlyChange.emit(true);
      fixture.detectChanges();

      expect(textOf(q('.tl-fb-empty-selection .tl-fb-empty-title'))).toBe('No selected issue');
    });

    it('selects every matching issue, up to 500, and says when the cap cut the list', async () => {
      idsSpy.mockImplementation((filters?: AdminFeedbackIssueFilters) => filters?.ids
        ? of<AdminFeedbackIssueIds>({ ids: [...filters.ids], totalCount: filters.ids.length, truncated: false })
        : of<AdminFeedbackIssueIds>({ ids: Array.from({ length: 1000 }, (_, i) => i + 1), totalCount: 1200, truncated: true }));
      await create();

      bar().selectAllMatching.emit();
      fixture.detectChanges();

      expect(idsSpy).toHaveBeenCalledWith(expect.objectContaining({ statuses: ['new', 'regressed'] }), 'lastSeen');
      expect(bar().selectedIds.length).toBe(500);
      expect(loadOverseerSelection().length).toBe(500);
      expect(status()).toContain('Selected the first 500 of 1,200.');
    });

    it('says it selected them all when no cap cut the list', async () => {
      idsSpy.mockImplementation(() => of<AdminFeedbackIssueIds>({ ids: [1, 2, 3], totalCount: 3, truncated: false }));
      await create();

      bar().selectAllMatching.emit();
      fixture.detectChanges();

      expect(bar().selectedIds).toEqual([1, 2, 3]);
      expect(status()).toContain('Selected all 3 matching issues.');
    });

    it('refuses a 501st issue and says so', async () => {
      saveOverseerSelection(Array.from({ length: 500 }, (_, i) => i + 100));
      await create();

      const box = checkbox(cards()[0]);
      box.click();
      fixture.detectChanges();

      expect(box.checked).toBe(false);
      expect(loadOverseerSelection().length).toBe(500);
      expect(status()).toContain('The selection holds at most 500 issues.');
    });

    it('clears the selection, and Show selected only with it', async () => {
      saveOverseerSelection([1]);
      await create();
      bar().showSelectedOnlyChange.emit(true);
      fixture.detectChanges();

      bar().clear.emit();
      fixture.detectChanges();

      expect(bar().selectedIds).toEqual([]);
      expect(loadOverseerSelection()).toEqual([]);
      expect(bar().showSelectedOnly).toBe(false);
      expect(lastQuery().ids).toBeUndefined();
    });

    it('drops the issues an export could not find, and remembers its options', async () => {
      saveOverseerSelection([1, 5]);
      await create();

      bar().exported.emit({ missing: [5], exportOptions: { occurrencesPerIssue: 25, includeQuestionText: true } });
      fixture.detectChanges();

      expect(loadOverseerSelection()).toEqual([1]);
      expect(loadFeedbackView('overseer')!.exportOptions).toEqual({ occurrencesPerIssue: 25, includeQuestionText: true });
      expect(bar().exportOptions).toEqual({ occurrencesPerIssue: 25, includeQuestionText: true });
    });

    it('refetches the list, the facets and the counts after an import', async () => {
      await create();
      issuesSpy.mockClear();
      facetsSpy.mockClear();
      const loadCounts = vi.spyOn(store, 'loadFeedbackCounts');

      bar().imported.emit();
      fixture.detectChanges();

      expect(lastQuery()).toEqual(expect.objectContaining({ skip: 0 }));
      expect(facetsSpy).toHaveBeenCalledTimes(1);
      expect(loadCounts).toHaveBeenCalledTimes(1);
    });
  });

  describe('triage', () => {
    const triageDialog = (): HTMLDialogElement => q<HTMLDialogElement>('dialog.ftd-dialog')!;
    const field = <T extends HTMLElement>(name: string): T => triageDialog().querySelector<T>(`#tl-fb-1-triage-${name}`)!;

    /** Clicks the card's Edit triage, focused first as a real click leaves it. */
    function editTriage(card: HTMLElement): HTMLDialogElement {
      const button = card.querySelector<HTMLButtonElement>('.tl-fb-edit')!;
      button.focus();
      button.click();
      fixture.detectChanges();
      return triageDialog();
    }

    function saveTriage(): void {
      triageDialog().querySelector<HTMLButtonElement>('.ftd-save')!.click();
      fixture.detectChanges();
    }

    it('ends each card in a footer: the triaged line, then Edit triage and Delete in a group named for the issue; no form in the card', async () => {
      await create();
      const card = cards()[0];

      expect(textOf(card.querySelector('.tl-fb-footer .tl-fb-triaged'))).toBe('Not triaged yet');
      const group = card.querySelector('.tl-fb-footer [role="group"]')!;
      expect(group.getAttribute('aria-label')).toBe('Actions for Dilithium crystal');
      const edit = group.querySelector<HTMLButtonElement>('.tl-fb-edit')!;
      expect(edit.classList.contains('btn-ghost')).toBe(true);
      expect(textOf(edit)).toBe('Edit triage: Dilithium crystal');
      expect(edit.getAttribute('aria-haspopup')).toBe('dialog');
      expect(group.querySelector('.tl-fb-delete')).not.toBeNull();
      expect(card.querySelector('select, textarea, input[type="text"]')).toBeNull();
      expect(triageDialog().open).toBe(false);
    });

    it('opens the triage dialog on the issue\'s values, with the recurrence rule in the Status (i)', async () => {
      nextPage = page([issue(1, { status: 'acknowledged', triageNote: 'Seen', resolution: 'Added the page.', fixedInVersion: '1.4.3' })]);
      await create();
      const dialog = editTriage(cards()[0]);

      expect(dialog.open).toBe(true);
      expect(textOf(dialog.querySelector('.ftd-issue-subject'))).toBe('Dilithium crystal');
      expect(textOf(dialog.querySelector('.ftd-issue-details'))).toBe('Missing information · , Status: Acknowledged · , 12 occurrences');
      const select = field<HTMLSelectElement>('status');
      expect(Array.from(select.options).map(o => o.value)).toEqual(['new', 'acknowledged', 'resolved', 'dismissed']);
      expect(Array.from(select.options).map(o => textOf(o))).toEqual(['New', 'Acknowledged', 'Resolved', 'Dismissed']);
      expect(select.value).toBe('acknowledged');
      expect(textOf(dialog.querySelector('#' + select.getAttribute('aria-describedby')))).toBe(
        'A resolved issue that recurs becomes Regressed; a dismissed one stays dismissed.');
      expect(field<HTMLTextAreaElement>('note').value).toBe('Seen');
      expect(field<HTMLTextAreaElement>('resolution').value).toBe('Added the page.');
      expect(field<HTMLTextAreaElement>('resolution').getAttribute('maxlength')).toBe('4000');
      expect(field<HTMLInputElement>('fixed').value).toBe('1.4.3');
      expect(field<HTMLInputElement>('fixed').getAttribute('maxlength')).toBe('64');
      expect(document.activeElement).toBe(select);
    });

    it('never offers Regressed: a regressed issue asks for a new status before it saves', async () => {
      nextPage = page([issue(1, { status: 'regressed', regressionCount: 1 })]);
      const updateSpy = vi.spyOn(service, 'updateStatus');
      await create();
      editTriage(cards()[0]);
      const select = field<HTMLSelectElement>('status');

      expect(Array.from(select.options).map(o => o.value)).toEqual(['', 'new', 'acknowledged', 'resolved', 'dismissed']);
      expect(select.options[0].disabled).toBe(true);
      expect(Array.from(select.options).some(o => o.value === 'regressed')).toBe(false);

      saveTriage();

      expect(updateSpy).not.toHaveBeenCalled();
      expect(textOf(triageDialog().querySelector('.ftd-status-error'))).toBe('Choose a status.');
      expect(triageDialog().open).toBe(true);
    });

    it('saves the status and the note, takes the server\'s copy, announces it, refetches the counts and gives focus back', async () => {
      const updated = issue(1, { status: 'acknowledged', triageNote: 'Wiki page coming', triagedByUserName: 'tommi', triagedUtc: '2026-10-21T12:05:00Z' });
      const updateSpy = vi.spyOn(service, 'updateStatus').mockReturnValue(of(updated));
      await create();
      const loadCounts = vi.spyOn(store, 'loadFeedbackCounts');
      const card = cards()[0];

      editTriage(card);
      selectValue(field<HTMLSelectElement>('status'), 'acknowledged');
      typeInto(field<HTMLTextAreaElement>('note'), '  Wiki page coming ');
      saveTriage();

      expect(updateSpy).toHaveBeenCalledWith(1, 'acknowledged', { note: 'Wiki page coming' });
      expect(triageDialog().open).toBe(false);
      expect(document.activeElement).toBe(card.querySelector('.tl-fb-edit'));
      const result = card.querySelector('#tl-fb-1-result')!;
      expect(result.getAttribute('role')).toBe('status');
      expect(textOf(result)).toBe('Saved. The issue is Acknowledged.');
      expect(textOf(card.querySelector('.tl-fb-status-badge [aria-hidden="true"]'))).toBe('Acknowledged');
      expect(textOf(card.querySelector('.tl-fb-triaged'))).toBe(`Triaged ${feedbackDateTimeText('2026-10-21T12:05:00Z')} by tommi`);
      expect(textOf(card.querySelector('.tl-fb-fact-note dd'))).toBe('Wiki page coming');
      expect(loadCounts).toHaveBeenCalledTimes(1);
      expect(card.querySelector('.tl-fb-triage-error')).toBeNull();
    });

    it('keeps the fields left as they were and clears the ones emptied', async () => {
      nextPage = page([issue(1, { status: 'resolved', triageNote: 'Seen', resolution: 'Added the page.', fixedInVersion: '1.4.3' })]);
      const updateSpy = vi.spyOn(service, 'updateStatus').mockImplementation(
        (_id: number, _status: string, changes?: { resolution?: string | null; fixedInVersion?: string | null }) => of(issue(1, {
          status: 'resolved', triageNote: 'Seen',
          resolution: changes?.resolution ?? 'Added the page.',
          fixedInVersion: changes?.fixedInVersion !== undefined ? changes.fixedInVersion || null : '1.4.3'
        })));
      await create();

      editTriage(cards()[0]);
      saveTriage();
      expect(updateSpy).toHaveBeenLastCalledWith(1, 'resolved', {});

      editTriage(cards()[0]);
      typeInto(field<HTMLInputElement>('fixed'), '');
      typeInto(field<HTMLTextAreaElement>('resolution'), ' Rewrote the guide. ');
      saveTriage();
      expect(updateSpy).toHaveBeenLastCalledWith(1, 'resolved', { resolution: 'Rewrote the guide.', fixedInVersion: '' });
      expect(textOf(cards()[0].querySelector('.tl-fb-fact-resolution dd'))).toBe('Rewrote the guide.');
      expect(cards()[0].querySelector('.tl-fb-fact-fixed')).toBeNull();
    });

    it('sends an emptied stored field as empty, so the server clears it', async () => {
      nextPage = page([issue(1, { status: 'resolved', resolution: 'Added the page.', fixedInVersion: '1.4.3' })]);
      const updateSpy = vi.spyOn(service, 'updateStatus').mockReturnValue(of(issue(1, { status: 'resolved' })));
      await create();

      editTriage(cards()[0]);
      typeInto(field<HTMLInputElement>('fixed'), '');
      typeInto(field<HTMLTextAreaElement>('resolution'), '   ');
      saveTriage();

      expect(updateSpy).toHaveBeenCalledWith(1, 'resolved', { resolution: '', fixedInVersion: '' });
    });

    it('refuses a fixed-in version the server would refuse, before sending anything', async () => {
      const updateSpy = vi.spyOn(service, 'updateStatus');
      await create();

      editTriage(cards()[0]);
      typeInto(field<HTMLInputElement>('fixed'), '1.4 beta');
      saveTriage();

      expect(updateSpy).not.toHaveBeenCalled();
      expect(textOf(triageDialog().querySelector('.ftd-fixed-error'))).toBe(
        'Fixed in version takes letters, digits, periods, plus and minus signs only, at most 64 characters.');
    });

    it('keeps a failed save in the dialog, leaves the card as it was and refetches no counts', async () => {
      vi.spyOn(service, 'updateStatus').mockReturnValue(throwError(() => ({ status: 400, error: { message: 'Unknown status.' } })));
      await create();
      const loadCounts = vi.spyOn(store, 'loadFeedbackCounts');
      const card = cards()[0];

      editTriage(card);
      selectValue(field<HTMLSelectElement>('status'), 'dismissed');
      saveTriage();

      expect(triageDialog().open).toBe(true);
      expect(textOf(triageDialog().querySelector('.ftd-error'))).toBe('The issue could not be saved: Unknown status.');
      expect(field<HTMLSelectElement>('status').value).toBe('dismissed');
      expect(textOf(card.querySelector('.tl-fb-status-badge [aria-hidden="true"]'))).toBe('New');
      expect(textOf(card.querySelector('#tl-fb-1-result'))).toBe('');
      expect(card.querySelector('.tl-fb-triage-error')).toBeNull();
      expect(loadCounts).not.toHaveBeenCalled();
      expect(component.savingId()).toBeNull();
    });

    it('refuses Delete while a save runs', async () => {
      const response = new Subject<AdminFeedbackIssue>();
      vi.spyOn(service, 'updateStatus').mockReturnValue(response.asObservable());
      const deleteSpy = vi.spyOn(service, 'deleteIssue');
      await create();
      const card = cards()[0];

      editTriage(card);
      saveTriage();
      expect(card.querySelector('.tl-fb-edit')!.getAttribute('aria-busy')).toBe('true');
      component.confirmDelete(component.rows()[0]);
      fixture.detectChanges();

      expect((el.querySelector('app-admin-confirm-dialog dialog') as HTMLDialogElement).open).toBe(false);
      expect(deleteSpy).not.toHaveBeenCalled();
      response.next(issue(1));
      response.complete();
      fixture.detectChanges();
      expect(component.savingId()).toBeNull();
    });

    it('deletes an issue after a confirmation that names it and its occurrences, then refetches the counts', async () => {
      nextPage = page([issue(1, { retainedCount: 9 }), issue(2)], 2);
      saveOverseerSelection([1, 2]);
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
      expect(loadOverseerSelection()).toEqual([2]);
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

    it('shows the filters\' empty state when the last card is deleted', async () => {
      rangePage = page([], 4);
      const response = new Subject<void>();
      vi.spyOn(service, 'deleteIssue').mockReturnValue(response.asObservable());
      await create();
      expect(rangeProbes().length).toBe(0);

      const button = cards()[0].querySelector<HTMLButtonElement>('.tl-fb-delete')!;
      button.focus();
      button.click();
      fixture.detectChanges();
      const dialog = el.querySelector('app-admin-confirm-dialog dialog') as HTMLDialogElement;
      (Array.from(dialog.querySelectorAll('button')).find(b => b.textContent?.trim() === 'Delete Issue') as HTMLButtonElement).click();
      fixture.detectChanges();
      response.next();
      response.complete();
      fixture.detectChanges();

      expect(cards().length).toBe(0);
      expect(rangeProbes().length).toBe(1);
      expect(q('.tl-fb-empty-filters')).not.toBeNull();
      expect(document.activeElement?.id).toBe('tl-fb-issues-title');
    });

    describe('when the saved issue leaves the filters', () => {
      const LEFT = '“Dilithium crystal” is now Resolved and no longer matches the filters.';
      /** The membership answer, held so that it arrives after the dialog has closed, as a real request does. */
      let answer: Subject<AdminFeedbackIssueIds>;

      beforeEach(() => {
        answer = new Subject<AdminFeedbackIssueIds>();
        vi.spyOn(service, 'updateStatus').mockReturnValue(of(issue(1, { status: 'resolved' })));
      });

      /** Saves issue 1 as Resolved with the membership answer held, and checks the dialog gave focus back first. */
      function resolveFirst(): void {
        idsSpy.mockReturnValueOnce(answer.asObservable());
        const card = cards()[0];
        editTriage(card);
        selectValue(field<HTMLSelectElement>('status'), 'resolved');
        saveTriage();
        expect(triageDialog().open).toBe(false);
        expect(document.activeElement).toBe(card.querySelector('.tl-fb-edit'));
      }

      function answerWith(ids: number[]): void {
        answer.next({ ids, totalCount: ids.length, truncated: false });
        answer.complete();
        fixture.detectChanges();
      }

      it('drops the card once the server says it no longer matches, announces it and focuses the next card', async () => {
        nextPage = page([issue(1), issue(2)]);
        await create();

        resolveFirst();
        expect(idsSpy).toHaveBeenLastCalledWith(expect.objectContaining({
          startDate: '2026-09-21', endDate: '2026-10-21', statuses: ['new', 'regressed'], ids: [1]
        }));
        answerWith([]);

        expect(cards().map(c => c.getAttribute('data-issue-id'))).toEqual(['2']);
        expect(status()).toBe(`${LEFT} One issue`);
        expect(document.activeElement?.id).toBe('tl-fb-2-title');
      });

      it('counts the dropped card out of a longer list', async () => {
        nextPage = page([issue(1), issue(2)], 3);
        await create();

        resolveFirst();
        answerWith([]);

        expect(status()).toBe(`${LEFT} Showing 1 of 2 issues`);
      });

      it('keeps the card when the membership check fails', async () => {
        nextPage = page([issue(1), issue(2)]);
        await create();

        resolveFirst();
        answer.error({ status: 500, error: { message: 'Down.' } });
        fixture.detectChanges();

        expect(cards().length).toBe(2);
        expect(textOf(cards()[0].querySelector('#tl-fb-1-result'))).toBe('Saved. The issue is Resolved.');
        expect(cards()[0].querySelector('.tl-fb-triage-error')).toBeNull();
        expect(status()).not.toContain('no longer matches');
      });

      it('keeps the card under Show selected only, asking about the issue alone', async () => {
        saveOverseerSelection([1]);
        await create();
        bar().showSelectedOnlyChange.emit(true);
        fixture.detectChanges();

        editTriage(cards()[0]);
        selectValue(field<HTMLSelectElement>('status'), 'resolved');
        saveTriage();

        expect(idsSpy).toHaveBeenLastCalledWith({ ids: [1] });
        expect(cards().length).toBe(1);
        expect(textOf(cards()[0].querySelector('#tl-fb-1-result'))).toBe('Saved. The issue is Resolved.');
      });

      it('keeps a selected issue selected and counts it as not shown, with no further request', async () => {
        nextPage = page([issue(1), issue(2)]);
        saveOverseerSelection([1]);
        await create();
        expect(bar().notShownCount).toBe(0);
        idsSpy.mockClear();

        resolveFirst();
        answerWith([]);

        expect(cards().length).toBe(1);
        expect(loadOverseerSelection()).toEqual([1]);
        expect(bar().selectedIds).toEqual([1]);
        expect(bar().notShownCount).toBe(1);
        expect(idsSpy).toHaveBeenCalledTimes(1);
      });

      it('shows the filters\' empty state when the last card leaves', async () => {
        rangePage = page([], 4);
        await create();
        expect(rangeProbes().length).toBe(0);

        resolveFirst();
        answerWith([]);

        expect(cards().length).toBe(0);
        expect(rangeProbes().length).toBe(1);
        expect(q('.tl-fb-empty-filters')).not.toBeNull();
        expect(document.activeElement?.id).toBe('tl-fb-issues-title');
      });

      it('ignores an answer that arrives after the list was refetched', async () => {
        nextPage = page([issue(1), issue(2)]);
        await create();

        resolveFirst();
        fixture.componentRef.setInput('refreshToken', 1);
        fixture.detectChanges();
        answerWith([]);

        expect(cards().map(c => c.getAttribute('data-issue-id'))).toEqual(['1', '2']);
        expect(status()).not.toContain('no longer matches');
      });

      it('refetches the facets after a save', async () => {
        await create();
        facetsSpy.mockClear();

        resolveFirst();

        expect(facetsSpy).toHaveBeenCalledTimes(1);
      });
    });
  });

  describe('readable labels', () => {
    /** The ids a card or an occurrence must not show while each has a label. */
    const IDS = /wiki_search|item_lookup|claude-x|gpt-x|\banthropic\b|\bopenai\b/;

    it('shows tools, providers and models by their labels on the card and its occurrences, and an unlabeled tool in words', async () => {
      nextPage = page([issue(1, {
        modelIds: ['claude-x', 'gpt-x', '+2'], modelLabels: ['Claude X', 'GPT X', '+2'],
        modelIdentities: [
          identity('claude-x', { modelLabel: 'Claude X', provider: 'anthropic' }),
          identity('gpt-x', { modelLabel: 'GPT X', provider: 'openai' })
        ],
        moreModelIdentities: 2,
        providers: ['anthropic', 'openai'], latestRelatedTools: ['wiki_search', 'some_new_tool']
      })]);
      occurrencesSpy.mockReturnValue(of<AdminFeedbackOccurrencePage>({
        totalCount: 1,
        rows: [occurrence(5, {
          provider: 'anthropic', modelId: 'claude-x', modelLabel: 'Claude X', relatedTools: ['item_lookup', 'some_new_tool'],
          searchedFor: 'the dilithium page', thinkingLevel: 'medium'
        })]
      }));
      await create();
      const card = cards()[0];
      const details = openOccurrences(card);

      expect(Array.from(card.querySelectorAll('.tl-fb-model-list > li')).map(li => identityTextOf(li))).toEqual(
        ['Claude X Anthropic', 'GPT X OpenAI', '+2 more']);
      const tools = card.querySelector('.tl-fb-fact-tools dd')!;
      expect(textOf(tools)).toBe('GnollHack wiki search, Some new tool');
      expect(tools.classList.contains('tl-fb-mono')).toBe(false);
      expect(card.querySelector('.tl-fb-fact-providers')).toBeNull();
      expect(Array.from(card.querySelectorAll('.tl-fb-facts dt')).map(d => textOf(d))).not.toContain('Providers');

      const item = details.querySelector<HTMLElement>('li.tl-fb-occ')!;
      const facts = Object.fromEntries(Array.from(item.querySelectorAll('.tl-fb-occ-facts > div'))
        .map(f => [textOf(f.querySelector('dt')), f.querySelector('dd')!]));
      expect(facts['Provider and model']).toBeUndefined();
      const model = facts['Model'].querySelector('app-model-identity')!;
      expect(textOf(model.querySelector('.model-identity-name'))).toBe('Claude X');
      expect(textOf(model.querySelector('.thinking-badge'))).toBe('thinking level medium');
      expect(textOf(model.querySelector('app-provider-badge'))).toBe('Anthropic');
      expect(textOf(facts['Related tools'])).toBe('GnollHack wiki item lookup, Some new tool');
      expect(facts['Related tools'].classList.contains('tl-fb-mono')).toBe(false);

      expect(textOf(card)).not.toMatch(IDS);
    });

    it('shows a model id when the server sent no label for it', async () => {
      nextPage = page([issue(1, {
        modelIds: ['claude-x', 'gpt-x'], modelLabels: ['Claude X', ''],
        modelIdentities: [identity('claude-x', { modelLabel: 'Claude X' }), identity('gpt-x', { modelLabel: '' })]
      })]);
      occurrencesSpy.mockReturnValue(of<AdminFeedbackOccurrencePage>({ totalCount: 1, rows: [occurrence(5, { modelLabel: null })] }));
      await create();
      const card = cards()[0];
      const details = openOccurrences(card);

      expect(Array.from(card.querySelectorAll('.tl-fb-model-list .model-identity-name')).map(n => textOf(n))).toEqual(['Claude X', 'gpt-x']);
      expect(textOf(details.querySelector('.tl-fb-occ-model .model-identity-name'))).toBe('claude-x');
    });

    it('labels the Model, Provider and Tool facets and their chips, and filters by the ids', async () => {
      facetsSpy.mockReturnValue(of(facets({
        models: [{ value: 'gpt-x', count: 3, label: 'GPT X' }, { value: 'claude-x', count: 5, label: 'Claude X' }],
        providers: [{ value: 'openai', count: 3, label: '' }, { value: 'anthropic', count: 5, label: '' }],
        tools: [{ value: 'wiki_search', count: 3, label: '' }, { value: 'some_new_tool', count: 1, label: '' }]
      })));
      await create();
      const options = (id: string) => facet(id)!.options.map(o => `${o.label} ${o.count}`);

      expect(options('tl-fb-facet-model')).toEqual(['Claude X 5', 'GPT X 3']);
      expect(options('tl-fb-facet-provider')).toEqual(['Anthropic 5', 'OpenAI 3']);
      expect(options('tl-fb-facet-tool')).toEqual(['GnollHack wiki search 3', 'Some new tool 1']);

      choose('tl-fb-facet-model', ['gpt-x']);
      choose('tl-fb-facet-provider', ['openai']);
      choose('tl-fb-facet-tool', ['wiki_search']);

      expect(lastQuery()).toEqual(expect.objectContaining({ modelIds: ['gpt-x'], providers: ['openai'], tools: ['wiki_search'] }));
      expect(chipLabels()).toEqual(expect.arrayContaining([
        'Remove filter Model: GPT X', 'Remove filter Provider: OpenAI', 'Remove filter Tool: GnollHack wiki search'
      ]));
      expect(textOf(q('.tl-fb-chips'))).not.toMatch(IDS);
      await settle();
      expect(queryParams()).toEqual(expect.objectContaining({ model: 'gpt-x', provider: 'openai', tool: 'wiki_search' }));
    });

    it('shows where the fix went in labels beside the fixed-in version, and nothing while it says nothing', async () => {
      nextPage = page([
        issue(1, { status: 'resolved', resolvedVia: ['overseer_release', 'wiki'], fixedInVersion: '1.4.3' }),
        issue(2, { resolvedVia: [] }),
        issue(3)
      ], 3);
      await create();
      const [first, second, third] = cards();

      expect(textOf(first.querySelector('.tl-fb-fact-resolved dt'))).toBe('Resolved via');
      expect(textOf(first.querySelector('.tl-fb-fact-resolved dd'))).toBe('GnollHack wiki, Overseer release');
      expect(first.querySelector('.tl-fb-fact-resolved')!.nextElementSibling!.classList.contains('tl-fb-fact-fixed')).toBe(true);
      expect(textOf(first.querySelector('.tl-fb-fact-group-triage'))).not.toMatch(/overseer_release/);
      expect(second.querySelector('.tl-fb-fact-resolved')).toBeNull();
      expect(third.querySelector('.tl-fb-fact-resolved')).toBeNull();
    });
  });

  describe('Resolved via in the triage dialog', () => {
    const triageDialog = (): HTMLDialogElement => q<HTMLDialogElement>('dialog.ftd-dialog')!;
    const box = (id: string): HTMLInputElement => triageDialog().querySelector<HTMLInputElement>(`#tl-fb-1-triage-resolved-${id}`)!;

    function openTriage(): void {
      cards()[0].querySelector<HTMLButtonElement>('.tl-fb-edit')!.click();
      fixture.detectChanges();
    }

    function toggle(input: HTMLInputElement): void {
      input.click();
      fixture.detectChanges();
    }

    function save(): void {
      triageDialog().querySelector<HTMLButtonElement>('.ftd-save')!.click();
      fixture.detectChanges();
    }

    it('opens on the stored choice and sends it only when it changed', async () => {
      nextPage = page([issue(1, { status: 'resolved', resolvedVia: ['wiki'] })]);
      const updateSpy = vi.spyOn(service, 'updateStatus').mockImplementation(
        (_id: number, _status: string, changes?: { resolvedVia?: readonly string[] | null }) =>
          of(issue(1, { status: 'resolved', resolvedVia: changes?.resolvedVia ? [...changes.resolvedVia] : ['wiki'] })));
      await create();

      openTriage();
      expect(box('wiki').checked).toBe(true);
      save();
      expect(updateSpy).toHaveBeenLastCalledWith(1, 'resolved', {});

      openTriage();
      toggle(box('overseer_release'));
      save();
      expect(updateSpy).toHaveBeenLastCalledWith(1, 'resolved', { resolvedVia: ['wiki', 'overseer_release'] });
      expect(textOf(cards()[0].querySelector('.tl-fb-fact-resolved dd'))).toBe('GnollHack wiki, Overseer release');

      openTriage();
      toggle(box('wiki'));
      toggle(box('overseer_release'));
      save();
      expect(updateSpy).toHaveBeenLastCalledWith(1, 'resolved', { resolvedVia: [] });
      expect(cards()[0].querySelector('.tl-fb-fact-resolved')).toBeNull();
    });
  });

  describe('export one issue', () => {
    const button = (card: HTMLElement = cards()[0]): HTMLButtonElement => card.querySelector<HTMLButtonElement>('.tl-fb-export')!;
    const dialog = (): ExportDialogStub => fixture.debugElement.query(By.directive(ExportDialogStub)).componentInstance as ExportDialogStub;

    function clickExport(card?: HTMLElement): void {
      button(card).click();
      fixture.detectChanges();
    }

    function done(overrides: Partial<FeedbackExportDone> = {}): FeedbackExportDone {
      return {
        missing: [], exportOptions: { occurrencesPerIssue: 'all', includeQuestionText: true, format: 'markdown' },
        fileName: 'overseer-feedback-overseer-issue-1-20261028-0915.md', issueCount: 1, ...overrides
      };
    }

    it('leads the card\'s actions as an icon button named for the issue, with an Export issue… tooltip, that opens a dialog', async () => {
      await create();
      const group = cards()[0].querySelector('.tl-fb-actions')!;
      const exportButton = button();

      expect(group.querySelector('button')).toBe(exportButton);
      expect(exportButton.classList.contains('action-btn')).toBe(true);
      expect(exportButton.getAttribute('aria-label')).toBe('Export issue: Dilithium crystal');
      expect(exportButton.getAttribute('aria-haspopup')).toBe('dialog');
      expect(textOf(exportButton)).toBe('');
      expect(exportButton.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
      const tip = el.querySelector<HTMLElement>('#' + exportButton.getAttribute('interestfor'))!;
      expect(tip.getAttribute('popover')).toBe('hint');
      expect(tip.classList.contains('gh-tooltip')).toBe(true);
      expect(textOf(tip)).toBe('Export issue…');
      expect(exportButton.getAttribute('style')).toContain('anchor-name: --tl-fb-1-export-tip');
      expect(tip.getAttribute('style')).toContain('position-anchor: --tl-fb-1-export-tip');
      expect(exportButton.hasAttribute('title')).toBe(false);
      expect(group.querySelector('.tl-fb-download')).toBeNull();
    });

    it('opens the export dialog on that one issue, its subject and its stored occurrences, with the remembered options', async () => {
      saveFeedbackView('overseer', {
        version: 1, sort: 'lastSeen', filters: {}, search: '', range: CUSTOM_RANGE,
        exportOptions: { occurrencesPerIssue: 25, includeQuestionText: false, format: 'markdown' }
      });
      nextPage = page([issue(1, { retainedCount: 9 }), issue(2)], 2);
      await create();

      expect(dialog().source).toBe('overseer');
      expect(dialog().idPrefix).toBe('overseer-card');
      expect(dialog().opened).toEqual([]);

      clickExport(cards()[1]);
      expect(dialog().opened).toEqual([{ ids: [2], issueSubject: 'Subject 2', occurrenceTotal: 12 }]);

      clickExport(cards()[0]);
      expect(dialog().opened[1]).toEqual({ ids: [1], issueSubject: 'Dilithium crystal', occurrenceTotal: 9 });
      expect(dialog().exportOptions).toEqual({ occurrencesPerIssue: 25, includeQuestionText: false, format: 'markdown' });
    });

    it('names the saved file on the card\'s result line and remembers the options for both exports', async () => {
      await create();
      clickExport();

      dialog().exported.emit(done());
      fixture.detectChanges();

      const line = cards()[0].querySelector('#tl-fb-1-result')!;
      expect(line.getAttribute('role')).toBe('status');
      expect(textOf(line)).toBe('Downloaded overseer-feedback-overseer-issue-1-20261028-0915.md.');
      expect(loadFeedbackView('overseer')!.exportOptions).toEqual(done().exportOptions);
      expect(bar().exportOptions).toEqual(done().exportOptions);
      expect(dialog().exportOptions).toEqual(done().exportOptions);
      expect(dialog().closed).toBe(0);
    });

    it('says an issue the server no longer has is gone, closes the dialog, fetches the list again and drops it from the selection', async () => {
      saveOverseerSelection([1, 2]);
      await create();
      clickExport();
      const before = listQueries().length;
      idsSpy.mockImplementation((filters?: AdminFeedbackIssueFilters) => {
        const ids = (filters?.ids ?? [2]).filter(id => id !== 1);
        return of<AdminFeedbackIssueIds>({ ids, totalCount: ids.length, truncated: false });
      });

      dialog().exported.emit(done({ missing: [1], fileName: null, issueCount: 0 }));
      fixture.detectChanges();

      expect(dialog().closed).toBe(1);
      expect(listQueries().length).toBe(before + 1);
      expect(textOf(cards()[0].querySelector('#tl-fb-1-result'))).toBe('This issue no longer exists.');
      expect(status()).toContain('The issue “Dilithium crystal” no longer exists.');
      expect(loadOverseerSelection()).toEqual([2]);
    });

    it('clears the card\'s last result when the dialog opens again', async () => {
      await create();
      clickExport();
      dialog().exported.emit(done());
      fixture.detectChanges();

      clickExport();

      expect(textOf(cards()[0].querySelector('#tl-fb-1-result'))).toBe('');
    });
  });

  describe('reload', () => {
    it('refetches the facets and the first page', async () => {
      await create();
      issuesSpy.mockClear();
      facetsSpy.mockClear();

      component.reload();
      fixture.detectChanges();

      expect(facetsSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery()).toEqual(expect.objectContaining({ skip: 0 }));
    });
  });
});
