import type { Mock } from 'vitest';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, convertToParamMap, provideRouter } from '@angular/router';
import { provideLocationMocks } from '@angular/common/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';
import {
  AdminBenchmarkService,
  BenchmarkExperimentalChangeCatalogDto,
  BenchmarkExperimentalChangeDto
} from '../../../services/admin-benchmark.service';
import {
  AdminGnollBenchFeedbackService,
  GnollBenchFeedbackCounts,
  GnollBenchFeedbackFacets,
  GnollBenchFeedbackFilters,
  GnollBenchFeedbackIds,
  GnollBenchFeedbackIssue,
  GnollBenchFeedbackIssuePage,
  GnollBenchFeedbackIssuesQuery,
  GnollBenchFeedbackOccurrence,
  GnollBenchFeedbackOccurrencePage
} from '../../../services/admin-gnollbench-feedback.service';
import { FilterFacetComponent } from '../../../shared/data-table/filter-facet.component';
import { FeedbackSelectionBarComponent } from '../shared/feedback-selection-bar.component';
import { FeedbackExportOptions, FeedbackRange, loadFeedbackView, loadGnollBenchSelection } from '../shared/feedback-view-storage';
import {
  FeedbackGnollBenchPanelComponent,
  GNOLLBENCH_FEEDBACK_VIEW_STORAGE_KEY,
  gnollBenchDateText,
  gnollBenchDateTimeText,
  gnollBenchQueryParams,
  gnollBenchStateFromQuery,
  gnollBenchVariantLabel
} from './feedback-gnollbench-panel.component';

const SELECTION_KEY = 'overseer.admin.feedback.gnollbench.selection';

const settle = (ms = 0): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

/** A 64-character lowercase hex fingerprint. */
const fp = (n: number): string => n.toString(16).padStart(2, '0').repeat(32);

function clearStorage(): void {
  try {
    localStorage.removeItem(GNOLLBENCH_FEEDBACK_VIEW_STORAGE_KEY);
    localStorage.removeItem(SELECTION_KEY);
  } catch {
    // Nothing stored.
  }
}

function issue(n: number, overrides: Partial<GnollBenchFeedbackIssue> = {}): GnollBenchFeedbackIssue {
  return {
    fingerprint: fp(n), category: 'missing_information', subject: n === 1 ? 'Dilithium crystal' : `Subject ${n}`,
    summary: 'The wiki has no page for it.',
    firstSeenUtc: '2026-09-02T08:00:00Z', lastSeenUtc: '2026-10-20T12:30:00Z',
    selfReportCount: 12, runCount: 3, modelCount: 2, models: ['Claude X', 'GPT X'], providers: ['Anthropic', 'OpenAI'],
    suites: ['Suite A'], status: 'new', triageNote: null, resolution: null, fixedInVersion: null,
    triagedByUserName: null, triagedUtc: null, resolvedUtc: null, alsoInOverseer: null,
    ...overrides
  };
}

function issueRange(from: number, count: number): GnollBenchFeedbackIssue[] {
  return Array.from({ length: count }, (_, i) => issue(from + i));
}

function occurrence(id: number, overrides: Partial<GnollBenchFeedbackOccurrence> = {}): GnollBenchFeedbackOccurrence {
  return {
    id, occurredUtc: '2026-10-20T12:30:00Z', runId: 12, seriesId: 4, suiteId: 8, suiteName: 'Suite A',
    questionId: 9, questionText: 'What does a dilithium crystal do?', modelId: 'claude-x', modelLabel: 'Claude X',
    provider: 'Anthropic', condition: 'closedBook', variant: 'asker-search-words-v1+race-role-tool-v1:0123456789ab',
    variantName: 'asker-search-words-v1+race-role-tool-v1', corpus: 'live', harness: '61', accepted: true, error: null,
    category: 'missing_information', subject: 'Dilithium crystal', summary: 'No page.', details: null,
    searchedFor: 'wiki_search "dilithium"', expectedLocation: 'wiki page Dilithium crystal', relatedTools: ['wiki_search'],
    noticedVia: null, suspectedCause: 'did_not_verify', suspectedCauseNote: 'I answered first.', truncated: false,
    ...overrides
  };
}

function page(rows: GnollBenchFeedbackIssue[], totalCount = rows.length): GnollBenchFeedbackIssuePage {
  return { totalCount, rows };
}

function counts(overrides: Partial<GnollBenchFeedbackCounts> = {}): GnollBenchFeedbackCounts {
  return { new: 0, acknowledged: 0, resolved: 0, regressed: 0, dismissed: 0, ...overrides };
}

function facets(overrides: Partial<GnollBenchFeedbackFacets> = {}): GnollBenchFeedbackFacets {
  return {
    categories: [
      { value: 'missing_information', label: 'missing_information', count: 2 },
      { value: 'tool_problem', label: 'tool_problem', count: 1 }
    ],
    models: [{ value: 'claude-x', label: 'Claude X', count: 2 }, { value: 'gpt-x', label: 'GPT X', count: 1 }],
    providers: [{ value: 'Anthropic', label: 'Anthropic', count: 2 }, { value: 'OpenAI', label: 'OpenAI', count: 1 }],
    conditions: [{ value: 'production', label: 'Production', count: 2 }, { value: 'closedBook', label: 'Closed book', count: 1 }],
    corpora: [{ value: 'live', label: 'live', count: 3 }],
    variants: [
      { value: 'none', label: 'none', count: 2 },
      { value: 'asker-search-words-v1+race-role-tool-v1', label: 'asker-search-words-v1+race-role-tool-v1', count: 1 }
    ],
    harnesses: [{ value: '61', label: '61', count: 3 }],
    runs: [{ id: 13, name: 'Run #13 · Suite A · Claude X', count: 1 }, { id: 12, name: 'Run #12 · Suite A · GPT X', count: 2 }],
    series: [], groups: [], batteries: [], batches: [],
    suites: [{ id: 8, name: 'Suite A', count: 3 }],
    questions: [{ id: 9, name: 'What does a dilithium crystal do?', count: 1 }, { id: 10, name: 'Where is it?', count: 2 }],
    ...overrides
  };
}

/** The catalog's titles of the two changes the fixtures use. */
const CHANGE_TITLES: ReadonlyMap<string, string> = new Map([
  ['asker-search-words-v1', 'Search with the player\'s race and role'],
  ['race-role-tool-v1', 'Race and role lookup tool']
]);

/** The experimental changes catalog the panel reads its titles from. */
function changesCatalog(): BenchmarkExperimentalChangeCatalogDto {
  return {
    changes: [...CHANGE_TITLES].map(([id, title]): BenchmarkExperimentalChangeDto => ({
      id, title, summary: `${title}.`, expectedEffect: null, source: 'builtIn' as const, kinds: [], hashPrefix: '0123456789ab',
      isValid: true, invalidReason: null, retired: false, locked: false, createdAtUtc: null, updatedAtUtc: null, copiedFrom: null,
      experimentalTool: null, experiment: null, descriptionEdits: [], instructions: null,
      appliesUnder: { production: 'full' as const, sourceOnly: 'full' as const, closedBook: 'none' as const },
      notApplyingUnderSourceOnly: [], mentions: [], conflictsWith: [], runCount: 0,
      canEdit: false, canEditText: false, canDelete: false, canCopy: true
    })),
    recentSelections: [], productionTools: [],
    limits: { titleMax: 60, summaryMax: 220, textMax: 4000, descriptionEditsMax: 6 }, maxStampLength: 160
  };
}

/** A custom range, which keeps its dates as given. */
const CUSTOM_RANGE: FeedbackRange = { preset: 'custom', start: '2026-09-21', end: '2026-10-21' };

/** Stands in for the selection bar, which has its own spec; only its contract is exercised here. */
@Component({ selector: 'app-feedback-selection-bar', template: '' })
class SelectionBarStubComponent {
  @Input() source = '';
  @Input() selectedIds: readonly (string | number)[] = [];
  @Input() matchingCount = 0;
  @Input() notShownCount = 0;
  @Input() showSelectedOnly = false;
  @Input() exportOptions: FeedbackExportOptions | undefined;
  @Output() showSelectedOnlyChange = new EventEmitter<boolean>();
  @Output() clear = new EventEmitter<void>();
  @Output() selectAllMatching = new EventEmitter<void>();
  @Output() exported = new EventEmitter<{ missing: (string | number)[]; exportOptions: FeedbackExportOptions }>();
  @Output() imported = new EventEmitter<void>();
}

describe('FeedbackGnollBenchPanelComponent', () => {
  describe('labels and the address', () => {
    it('names the experimental changes by title when the catalog has them, by id otherwise, and drops a stamp\'s hash', () => {
      expect(gnollBenchVariantLabel('none')).toBe('None');
      expect(gnollBenchVariantLabel(null)).toBe('None');
      expect(gnollBenchVariantLabel('race-role-tool-v1')).toBe('race-role-tool-v1');
      expect(gnollBenchVariantLabel('race-role-tool-v1:0123456789ab')).toBe('race-role-tool-v1');
      expect(gnollBenchVariantLabel('asker-search-words-v1+race-role-tool-v1:0123456789ab'))
        .toBe('asker-search-words-v1, race-role-tool-v1');
      expect(gnollBenchVariantLabel('asker-search-words-v1+race-role-tool-v1', CHANGE_TITLES))
        .toBe('Search with the player\'s race and role, Race and role lookup tool');
      // A decoded query string has a space for the `+`; an id the catalog does not list keeps its id.
      expect(gnollBenchVariantLabel('asker-search-words-v1 gone-v1', CHANGE_TITLES)).toBe('Search with the player\'s race and role, gone-v1');
    });

    it('reads a local day as 21 Oct 2026 and adds the local time as 14:02', () => {
      const local = new Date(2026, 9, 21, 14, 2).toISOString();
      expect(gnollBenchDateText(local)).toBe('21 Oct 2026');
      expect(gnollBenchDateTimeText(local)).toBe('21 Oct 2026 14:02');
      expect(gnollBenchDateText(null)).toBe('');
    });

    it('reads an address, dropping unknown values and non-numeric ids, and writes it back without the defaults', () => {
      const state = gnollBenchStateFromQuery(convertToParamMap({
        status: 'resolved,bogus', category: 'tool_problem,nope', run: '12,x,13', condition: 'closedBook,moon',
        variant: 'none,asker-search-words-v1+race-role-tool-v1', accepted: 'refused', q: ' crystal '
      }));
      expect(state.facets.status).toEqual(['resolved']);
      expect(state.facets.category).toEqual(['tool_problem']);
      expect(state.facets.run).toEqual(['12', '13']);
      expect(state.facets.condition).toEqual(['closedBook']);
      expect(state.facets.variant).toEqual(['none', 'asker-search-words-v1+race-role-tool-v1']);
      expect(state.accepted).toBe('refused');
      expect(state.search).toBe('crystal');

      const params = gnollBenchQueryParams(state);
      expect(params).toEqual(expect.objectContaining({
        status: 'resolved', category: 'tool_problem', run: '12,13', condition: 'closedBook',
        variant: 'none,asker-search-words-v1+race-role-tool-v1', accepted: 'refused', q: 'crystal', model: null
      }));

      const defaults = gnollBenchStateFromQuery(convertToParamMap({ accepted: 'bogus' }));
      expect(defaults.facets.status).toEqual(['new', 'regressed']);
      expect(defaults.accepted).toBe('accepted');
      expect(Object.values(gnollBenchQueryParams(defaults)).every(v => v === null)).toBe(true);
      expect(gnollBenchStateFromQuery(convertToParamMap({ status: 'all' })).facets.status).toEqual([]);
      expect(gnollBenchQueryParams(gnollBenchStateFromQuery(convertToParamMap({ status: 'all' })))['status']).toBe('all');
    });
  });

  describe('the panel', () => {
    let fixture: ComponentFixture<FeedbackGnollBenchPanelComponent>;
    let component: FeedbackGnollBenchPanelComponent;
    let el: HTMLElement;
    let service: AdminGnollBenchFeedbackService;
    let router: Router;
    let issuesSpy: Mock;
    let idsSpy: Mock;
    let facetsSpy: Mock;
    let countsSpy: Mock;
    let occurrencesSpy: Mock;
    /** What the next issues request returns; the empty-state count request (take 1) gets `rangePage`. */
    let nextPage: GnollBenchFeedbackIssuePage;
    let rangePage: GnollBenchFeedbackIssuePage;
    /** The fingerprints the server still has; `issues/ids` answers with those its filters name. */
    let existing: Set<string>;
    let idsAnswer: ((filters: GnollBenchFeedbackFilters) => GnollBenchFeedbackIds) | null;

    beforeEach(async () => {
      clearStorage();
      await TestBed.configureTestingModule({
        imports: [FeedbackGnollBenchPanelComponent],
        providers: [
          provideRouter([{ path: 'admin/feedback/gnollbench', children: [] }, { path: 'admin/feedback', children: [] }]),
          provideLocationMocks(), provideHttpClient(), provideHttpClientTesting()
        ]
      })
        .overrideComponent(FeedbackGnollBenchPanelComponent, {
          remove: { imports: [FeedbackSelectionBarComponent] },
          add: { imports: [SelectionBarStubComponent] }
        })
        .compileComponents();

      service = TestBed.inject(AdminGnollBenchFeedbackService);
      router = TestBed.inject(Router);
      nextPage = page([issue(1)]);
      rangePage = page([], 0);
      existing = new Set([fp(1), fp(2)]);
      idsAnswer = null;
      issuesSpy = vi.spyOn(service, 'getIssues').mockImplementation(
        (query?: GnollBenchFeedbackIssuesQuery) => of(query?.take === 1 ? rangePage : nextPage));
      idsSpy = vi.spyOn(service, 'getIssueIds').mockImplementation((filters: GnollBenchFeedbackFilters = {}) => {
        if (idsAnswer) return of(idsAnswer(filters));
        const named = filters.fingerprints ? filters.fingerprints.filter(f => existing.has(f)) : [...existing];
        return of({ fingerprints: named, totalCount: named.length, truncated: false });
      });
      facetsSpy = vi.spyOn(service, 'getFacets').mockReturnValue(of(facets()));
      countsSpy = vi.spyOn(service, 'getCounts').mockReturnValue(of(counts({ new: 3, regressed: 1, resolved: 7 })));
      occurrencesSpy = vi.spyOn(service, 'getOccurrences').mockReturnValue(of({ totalCount: 0, rows: [] }));
      vi.spyOn(TestBed.inject(AdminBenchmarkService), 'getExperimentalChanges').mockReturnValue(of(changesCatalog()));
    });

    afterEach(() => {
      if (fixture) {
        el.querySelectorAll<HTMLElement>('[popover]').forEach(popover => {
          try { popover.hidePopover(); } catch { /* already closed */ }
        });
        fixture.destroy();
      }
      clearStorage();
    });

    async function create(url = '/admin/feedback/gnollbench', range: FeedbackRange = CUSTOM_RANGE): Promise<void> {
      await router.navigateByUrl(url);
      fixture = TestBed.createComponent(FeedbackGnollBenchPanelComponent);
      component = fixture.componentInstance;
      el = fixture.nativeElement as HTMLElement;
      fixture.componentRef.setInput('range', range);
      fixture.detectChanges();
    }

    /** The list requests, without the empty-state count request. */
    const listQueries = (): GnollBenchFeedbackIssuesQuery[] =>
      issuesSpy.mock.calls.map(call => call[0] as GnollBenchFeedbackIssuesQuery).filter(query => query.take !== 1);
    const lastQuery = (): GnollBenchFeedbackIssuesQuery => listQueries()[listQueries().length - 1];
    const q = <T extends HTMLElement = HTMLElement>(selector: string): T | null => el.querySelector<T>(selector);
    const cards = (): HTMLElement[] => Array.from(el.querySelectorAll<HTMLElement>('article.tl-gbfb-card'));
    const status = (): string => textOf(q('#tl-gbfb-status'));
    const facet = (key: string): FilterFacetComponent | undefined => fixture.debugElement
      .queryAll(By.directive(FilterFacetComponent))
      .map(d => d.componentInstance as FilterFacetComponent)
      .find(f => f.facetId === `tl-gbfb-facet-${key}`);
    const bar = (): SelectionBarStubComponent =>
      fixture.debugElement.query(By.directive(SelectionBarStubComponent)).componentInstance as SelectionBarStubComponent;
    const queryParams = () => router.parseUrl(router.url).queryParams;
    const chipLabels = (): (string | null)[] =>
      Array.from(el.querySelectorAll('.tl-gbfb-chips .gh-filter-chip')).map(c => c.getAttribute('aria-label'));
    const cid = (n: number): string => `tl-gbfb-${fp(n)}`;

    function choose(key: string, values: string[]): void {
      facet(key)!.selectedChange.emit(values);
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
      const details = card.querySelector('details.tl-gbfb-occurrences') as HTMLDetailsElement;
      details.open = true;
      details.dispatchEvent(new Event('toggle'));
      fixture.detectChanges();
      return details;
    }

    describe('issue cards', () => {
      it('fetches New and Regressed accepted issues of the range, last seen first, 25 at a time, with the facets and counts', async () => {
        await create();

        expect(listQueries().length).toBe(1);
        expect(lastQuery()).toEqual(expect.objectContaining({
          startDate: '2026-09-21', endDate: '2026-10-21', statuses: ['new', 'regressed'], accepted: 'accepted',
          sort: 'lastSeen', skip: 0, take: 25
        }));
        expect(lastQuery().runs).toBeUndefined();
        expect(lastQuery().fingerprints).toBeUndefined();
        expect(lastQuery().search).toBeUndefined();
        expect(facetsSpy).toHaveBeenCalledWith({ startDate: '2026-09-21', endDate: '2026-10-21', accepted: 'accepted' });
        expect(countsSpy).toHaveBeenCalledTimes(1);
      });

      it('is a list named by the section heading of articles labeled by h4 titles that label their checkboxes', async () => {
        await create();

        const list = q('ul.tl-gbfb-cards')!;
        expect(list.getAttribute('role')).toBe('list');
        expect(list.getAttribute('aria-labelledby')).toBe('tl-gbfb-issues-title');
        expect(textOf(q('#tl-gbfb-issues-title'))).toBe('Reported issues');
        expect(q('#tl-gbfb-status')!.getAttribute('role')).toBe('status');
        const article = cards()[0];
        const title = el.querySelector('#' + article.getAttribute('aria-labelledby'))!;
        expect(title.tagName).toBe('H4');
        expect(title.id).toBe(`${cid(1)}-title`);
        expect(textOf(title)).toBe('Dilithium crystal');
        const box = article.querySelector<HTMLInputElement>('input.tl-gbfb-check')!;
        expect(box.getAttribute('aria-label')).toBe('Select issue: Dilithium crystal');
        expect(title.querySelector('label')!.getAttribute('for')).toBe(box.id);
      });

      it('renders the kicker, the summary, the four metrics and the facts', async () => {
        await create();
        const card = cards()[0];

        expect(textOf(card.querySelector('.tl-gbfb-category'))).toBe('Missing information');
        expect(textOf(card.querySelector('.tl-gbfb-status-badge'))).toBe('New');
        expect(textOf(card.querySelector('.tl-gbfb-models'))).toBe('Models: Claude X, GPT X');
        expect(textOf(card.querySelector('.tl-gbfb-summary'))).toBe('The wiki has no page for it.');
        const metrics = Array.from(card.querySelectorAll('.tl-gbfb-metrics > .tl-gbfb-metric'));
        expect(metrics.map(m => textOf(m.querySelector('dt')))).toEqual(['Self-reports', 'Models', 'First seen', 'Last seen']);
        expect(Array.from(card.querySelectorAll('.tl-gbfb-metric-reports dd')).map(d => textOf(d))).toEqual(['12', 'in 3 runs']);
        expect(textOf(card.querySelector('.tl-gbfb-metric-models dd'))).toBe('2');
        expect(textOf(card.querySelector('.tl-gbfb-metric-first dd'))).toBe(gnollBenchDateText('2026-09-02T08:00:00Z'));
        expect(textOf(card.querySelector('.tl-gbfb-metric-last dd'))).toBe(gnollBenchDateTimeText('2026-10-20T12:30:00Z'));
        const facts = Array.from(card.querySelectorAll('.tl-gbfb-facts > div')).map(f => [textOf(f.querySelector('dt')), textOf(f.querySelector('dd'))]);
        expect(facts).toEqual([
          ['Providers', 'Anthropic, OpenAI'],
          ['Suites', 'Suite A'],
          ['Fingerprint', fp(1).slice(0, 12)]
        ]);
      });

      it('links an issue also reported in the Overseer chat to that issue in the Overseer sub-tab', async () => {
        nextPage = page([issue(1, { alsoInOverseer: { issueId: 42, status: 'resolved' } })]);
        await create();

        const fact = cards()[0].querySelector('.tl-gbfb-fact-overseer')!;
        expect(textOf(fact.querySelector('dt'))).toBe('Also reported in the Overseer chat');
        const link = fact.querySelector<HTMLAnchorElement>('a.tl-gbfb-overseer-link')!;
        expect(textOf(link)).toBe('Overseer issue #42');
        expect(link.getAttribute('href')).toBe('/admin/feedback?ids=42&status=all');
        expect(textOf(fact.querySelector('.tl-gbfb-overseer-status'))).toBe('Resolved');
      });

      it('badges Regressed and Resolved with the shared tones, in words', async () => {
        nextPage = page([issue(1, { status: 'regressed' }), issue(2, { status: 'resolved' })]);
        await create();
        const [first, second] = cards();

        const badge = first.querySelector('.tl-gbfb-status-badge')!;
        expect(badge.classList.contains('badge-warning')).toBe(true);
        expect(textOf(badge)).toBe('Regressed');
        expect(second.querySelector('.tl-gbfb-status-badge')!.classList.contains('badge-success')).toBe(true);
      });

      it('renders model-written text as plain text', async () => {
        nextPage = page([issue(1, { subject: '<i>Elbereth</i>', summary: 'It said <b>bold</b> things.' })]);
        await create();
        const card = cards()[0];

        expect(textOf(card.querySelector('.tl-gbfb-summary'))).toBe('It said <b>bold</b> things.');
        expect(textOf(card.querySelector('h4'))).toBe('<i>Elbereth</i>');
        expect(card.querySelector('.tl-gbfb-summary b')).toBeNull();
        expect(card.querySelector('h4 i')).toBeNull();
      });

      it('appends the next 25 on Show more and focuses the first new card', async () => {
        nextPage = page(issueRange(1, 25), 30);
        await create();

        expect(status()).toBe('Showing 25 of 30 issues');
        const more = q<HTMLButtonElement>('.tl-gbfb-show-more')!;
        expect(textOf(more)).toBe('Show 5 more');

        nextPage = page(issueRange(26, 5), 30);
        more.click();
        fixture.detectChanges();

        expect(lastQuery()).toEqual(expect.objectContaining({ skip: 25, take: 25 }));
        expect(cards().length).toBe(30);
        expect(document.activeElement?.id).toBe(`${cid(26)}-title`);
        expect(q('.tl-gbfb-show-more')).toBeNull();
      });
    });

    describe('filters', () => {
      it('offers every status with its count, New and Regressed selected, and lists a facet only with two options or a selection', async () => {
        await create();

        const statusFacet = facet('status')!;
        expect(statusFacet.options.map(o => `${o.label} ${o.count}`)).toEqual(
          ['New 3', 'Regressed 1', 'Acknowledged 0', 'Resolved 7', 'Dismissed 0']);
        expect([...statusFacet.selected]).toEqual(['new', 'regressed']);
        const listed = fixture.debugElement.queryAll(By.directive(FilterFacetComponent))
          .map(d => (d.componentInstance as FilterFacetComponent).label);
        expect(listed).toEqual(['Status', 'Category', 'Model', 'Provider', 'Run', 'Question', 'Information condition', 'Experimental Changes']);
        expect(facet('category')!.options.map(o => o.label)).toEqual(['Missing information', 'Tool problem']);
        expect(facet('model')!.options.map(o => o.label)).toEqual(['Claude X', 'GPT X']);
        expect(facet('run')!.options.map(o => o.value)).toEqual(['13', '12']);
        expect(facet('condition')!.options.map(o => o.label)).toEqual(['Production', 'Closed book']);
        expect(facet('variant')!.options.map(o => o.label))
          .toEqual(['None', 'Search with the player\'s race and role, Race and role lookup tool']);
        expect(chipLabels()).toEqual(['Remove filter Status: New', 'Remove filter Status: Regressed']);
      });

      it('refetches page 1 for a facet choice, keeps it in the address and remembers it', async () => {
        await create();

        choose('run', ['12']);
        expect(lastQuery()).toEqual(expect.objectContaining({ runs: ['12'], statuses: ['new', 'regressed'], skip: 0 }));

        choose('variant', ['asker-search-words-v1+race-role-tool-v1']);
        choose('condition', ['closedBook']);
        expect(lastQuery()).toEqual(expect.objectContaining({
          runs: ['12'], variants: ['asker-search-words-v1+race-role-tool-v1'], conditions: ['closedBook']
        }));

        await settle();
        expect(queryParams()).toEqual(expect.objectContaining({
          run: '12', variant: 'asker-search-words-v1+race-role-tool-v1', condition: 'closedBook'
        }));
        expect(queryParams()['status']).toBeUndefined();
        expect(router.url.startsWith('/admin/feedback/gnollbench?')).toBe(true);
        const record = loadFeedbackView('gnollbench')!;
        expect(record.filters['run']).toEqual(['12']);
        expect(record.filters['condition']).toEqual(['closedBook']);
        expect(record.filters['status']).toEqual(['new', 'regressed']);
        expect(record.range).toEqual(CUSTOM_RANGE);
      });

      it('takes its filters from the address it opens on, and remembers them', async () => {
        await create('/admin/feedback/gnollbench?status=resolved,dismissed&model=gpt-x&run=12&question=10&accepted=all&q=crystal');

        expect(listQueries().length).toBe(1);
        expect(lastQuery()).toEqual(expect.objectContaining({
          statuses: ['resolved', 'dismissed'], models: ['gpt-x'], runs: ['12'], questions: ['10'], accepted: 'all', search: 'crystal'
        }));
        expect(facetsSpy).toHaveBeenCalledWith(expect.objectContaining({ accepted: 'all' }));
        expect(q<HTMLInputElement>('#tl-gbfb-search')!.value).toBe('crystal');
        expect(q<HTMLSelectElement>('#tl-gbfb-accepted')!.value).toBe('all');
        expect(chipLabels()).toEqual([
          'Remove filter Status: Resolved', 'Remove filter Status: Dismissed', 'Remove filter Model: GPT X',
          'Remove filter Run: Run #12 · Suite A · GPT X', 'Remove filter Question: Where is it?',
          'Remove filter Self-reports: Accepted and refused', 'Remove filter Search: “crystal”'
        ]);
        const record = loadFeedbackView('gnollbench')!;
        expect(record.filters['model']).toEqual(['gpt-x']);
        expect(record.filters['accepted']).toEqual(['all']);
        expect(record.search).toBe('crystal');
      });

      it('applies the remembered view when the address names no filter, and writes it into the address', async () => {
        localStorage.setItem(GNOLLBENCH_FEEDBACK_VIEW_STORAGE_KEY, JSON.stringify({
          version: 1, sort: 'runs', filters: { status: ['resolved'], run: ['12'], accepted: ['refused'] }, search: 'abc',
          range: CUSTOM_RANGE
        }));
        await create();

        expect(listQueries().length).toBe(1);
        expect(lastQuery()).toEqual(expect.objectContaining({
          statuses: ['resolved'], runs: ['12'], accepted: 'refused', search: 'abc', sort: 'runs'
        }));
        expect(q<HTMLSelectElement>('#tl-gbfb-sort')!.value).toBe('runs');
        await settle();
        expect(queryParams()).toEqual(expect.objectContaining({ status: 'resolved', run: '12', accepted: 'refused', q: 'abc' }));
        expect(queryParams()['sort']).toBeUndefined();
      });

      it('applies an address change made by Back or Forward, and reads status=all as no status filter', async () => {
        await create();

        await router.navigateByUrl('/admin/feedback/gnollbench?status=all&harness=61');
        fixture.detectChanges();

        expect(lastQuery().statuses).toBeUndefined();
        expect(lastQuery().harnesses).toEqual(['61']);
      });

      it('sorts by six orders, refetches and remembers the choice in the view record, not in the address', async () => {
        await create();
        const sort = q<HTMLSelectElement>('#tl-gbfb-sort')!;
        expect(Array.from(sort.options).map(o => o.textContent?.trim())).toEqual(
          ['Last seen', 'First seen', 'Most self-reports', 'Most runs', 'Most models', 'Subject (A–Z)']);
        expect(textOf(el.querySelector('label[for="tl-gbfb-sort"]'))).toBe('Sort by');

        choose('run', ['12']);
        selectValue(sort, 'models');

        expect(lastQuery()).toEqual(expect.objectContaining({ sort: 'models', skip: 0 }));
        const record = loadFeedbackView('gnollbench')!;
        expect(record.sort).toBe('models');
        expect(record.filters['run']).toEqual(['12']);
        await settle();
        expect(queryParams()['sort']).toBeUndefined();
      });

      it('widens the base set to every call or the refused ones, refetching the facets too, with a chip', async () => {
        await create();
        const accepted = q<HTMLSelectElement>('#tl-gbfb-accepted')!;
        expect(textOf(el.querySelector('label[for="tl-gbfb-accepted"]'))).toBe('Self-reports');
        expect(Array.from(accepted.options).map(o => o.textContent?.trim())).toEqual(
          ['Accepted only', 'Accepted and refused', 'Refused only']);
        expect(accepted.getAttribute('aria-describedby')).toBe('tl-gbfb-accepted-tip');
        facetsSpy.mockClear();

        selectValue(accepted, 'refused');

        expect(lastQuery()).toEqual(expect.objectContaining({ accepted: 'refused', skip: 0 }));
        expect(facetsSpy).toHaveBeenCalledWith(expect.objectContaining({ accepted: 'refused' }));
        expect(chipLabels()).toContain('Remove filter Self-reports: Refused only');
        await settle();
        expect(queryParams()['accepted']).toBe('refused');
      });

      it('refetches page 1 for the search once typing pauses, keeps it in the address, and Escape clears it at once', async () => {
        await create();
        issuesSpy.mockClear();
        const search = q<HTMLInputElement>('#tl-gbfb-search')!;
        expect(search.getAttribute('maxlength')).toBe('200');

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

      it('removes a chip\'s filter, refetches and focuses the chip in its place', async () => {
        await create();

        q<HTMLButtonElement>('.tl-gbfb-chips .gh-filter-chip')!.click();
        fixture.detectChanges();

        expect(lastQuery().statuses).toEqual(['regressed']);
        expect(document.activeElement?.getAttribute('aria-label')).toBe('Remove filter Status: Regressed');
      });

      it('refetches the facets and the first page for a new range, and remembers it', async () => {
        await create();
        issuesSpy.mockClear();
        facetsSpy.mockClear();
        const range: FeedbackRange = { preset: '7d', start: '2026-10-16', end: '2026-10-23' };

        fixture.componentRef.setInput('range', range);
        fixture.detectChanges();

        expect(lastQuery()).toEqual(expect.objectContaining({ startDate: '2026-10-16', endDate: '2026-10-23', skip: 0 }));
        expect(facetsSpy).toHaveBeenCalledWith(expect.objectContaining({ startDate: '2026-10-16', endDate: '2026-10-23' }));
        expect(loadFeedbackView('gnollbench')!.range).toEqual(range);
      });

      it('refetches the facets, the counts and the first page when the shell\'s Refresh token changes', async () => {
        await create();
        issuesSpy.mockClear();
        facetsSpy.mockClear();
        countsSpy.mockClear();

        fixture.componentRef.setInput('refreshToken', 1);
        fixture.detectChanges();

        expect(listQueries().length).toBe(1);
        expect(facetsSpy).toHaveBeenCalledTimes(1);
        expect(countsSpy).toHaveBeenCalledTimes(1);
      });
    });

    describe('empty states', () => {
      it('says the range has no issues when the filters leave none and the range holds none either', async () => {
        nextPage = page([], 0);
        rangePage = page([], 0);
        await create();

        const empty = q('.tl-gbfb-empty.tl-gbfb-empty-range')!;
        expect(textOf(empty.querySelector('.tl-gbfb-empty-title'))).toBe('No GnollBench feedback issues');
        expect(q('.tl-gbfb-empty > svg')!.getAttribute('aria-hidden')).toBe('true');
        expect(issuesSpy).toHaveBeenCalledWith({ startDate: '2026-09-21', endDate: '2026-10-21', take: 1 });
      });

      it('says the filters match nothing when the range holds issues, and Clear filters clears them all and focuses the search', async () => {
        nextPage = page([], 0);
        rangePage = page([], 4);
        await create('/admin/feedback/gnollbench?run=12&accepted=refused');

        const empty = q('.tl-gbfb-empty.tl-gbfb-empty-filters')!;
        expect(textOf(empty.querySelector('.tl-gbfb-empty-title'))).toBe('No issues match these filters');
        nextPage = page([issue(1, { status: 'resolved' })]);
        (empty.querySelector('button.tl-gbfb-empty-clear') as HTMLButtonElement).click();
        fixture.detectChanges();

        expect(lastQuery().statuses).toBeUndefined();
        expect(lastQuery().runs).toBeUndefined();
        expect(lastQuery().accepted).toBe('accepted');
        expect(cards().length).toBe(1);
        expect(document.activeElement?.id).toBe('tl-gbfb-search');
        await settle();
        expect(queryParams()['status']).toBe('all');
        expect(queryParams()['run']).toBeUndefined();
      });

      it('shows the load error in place of the list', async () => {
        issuesSpy.mockImplementation(() => throwError(() => ({ status: 400, error: { message: 'Unknown sort.' } })));
        await create();

        expect(textOf(q('.tl-gbfb-error'))).toBe('The GnollBench feedback issues could not be loaded: Unknown sort.');
        expect(q('ul.tl-gbfb-cards')).toBeNull();
      });
    });

    describe('self-reports', () => {
      it('loads the first page on first open with the filters, then 10 more on Show more, and not again on reopening', async () => {
        nextPage = page([issue(1, { selfReportCount: 12 })]);
        occurrencesSpy.mockReturnValue(of<GnollBenchFeedbackOccurrencePage>({
          totalCount: 12, rows: Array.from({ length: 10 }, (_, i) => occurrence(100 - i))
        }));
        await create('/admin/feedback/gnollbench?run=12');
        const card = cards()[0];
        expect(textOf(card.querySelector('details.tl-gbfb-occurrences > summary'))).toBe('Self-reports (12)');
        expect(occurrencesSpy).not.toHaveBeenCalled();

        const details = openOccurrences(card);

        expect(occurrencesSpy).toHaveBeenCalledWith(fp(1), expect.objectContaining({ all: false, skip: 0, take: 10 }));
        expect((occurrencesSpy.mock.calls[0][1] as { filters: GnollBenchFeedbackFilters }).filters.runs).toEqual(['12']);
        expect(details.querySelectorAll('li.tl-gbfb-occ').length).toBe(10);
        const more = details.querySelector<HTMLButtonElement>('.tl-gbfb-occ-more')!;
        expect(textOf(more)).toBe('Show 2 more');

        occurrencesSpy.mockReturnValue(of<GnollBenchFeedbackOccurrencePage>({ totalCount: 12, rows: [occurrence(90), occurrence(89)] }));
        more.click();
        fixture.detectChanges();

        expect(occurrencesSpy).toHaveBeenLastCalledWith(fp(1), expect.objectContaining({ skip: 10, take: 10, all: false }));
        expect(details.querySelectorAll('li.tl-gbfb-occ').length).toBe(12);
        expect(document.activeElement?.id).toBe('tl-gbfb-occ-90-time');

        occurrencesSpy.mockClear();
        details.open = false;
        details.dispatchEvent(new Event('toggle'));
        openOccurrences(card);
        expect(occurrencesSpy).not.toHaveBeenCalled();
      });

      it('includes the self-reports outside the filters on request', async () => {
        occurrencesSpy.mockReturnValue(of<GnollBenchFeedbackOccurrencePage>({ totalCount: 1, rows: [occurrence(5)] }));
        await create('/admin/feedback/gnollbench?run=12');
        const details = openOccurrences(cards()[0]);

        const toggle = details.querySelector<HTMLButtonElement>('.tl-gbfb-occ-all')!;
        expect(textOf(toggle)).toBe('Include self-reports outside the filters');
        expect(toggle.getAttribute('aria-pressed')).toBe('false');
        toggle.click();
        fixture.detectChanges();

        expect(occurrencesSpy).toHaveBeenLastCalledWith(fp(1), expect.objectContaining({ all: true, skip: 0 }));
        expect(details.querySelector('.tl-gbfb-occ-all')!.getAttribute('aria-pressed')).toBe('true');
      });

      it('offers no such toggle while no filter narrows the self-reports', async () => {
        occurrencesSpy.mockReturnValue(of<GnollBenchFeedbackOccurrencePage>({ totalCount: 1, rows: [occurrence(5)] }));
        await create();
        const details = openOccurrences(cards()[0]);

        expect(details.querySelector('.tl-gbfb-occ-all')).toBeNull();
      });

      it('shows each self-report\'s run, suite, model, condition, Experimental Changes, corpus, harness and text as plain text', async () => {
        const long = Array.from({ length: 9 }, (_, i) => `Line ${i + 1}`).join('\n');
        occurrencesSpy.mockReturnValue(of<GnollBenchFeedbackOccurrencePage>({
          totalCount: 2,
          rows: [
            occurrence(5, { details: long, truncated: true, noticedVia: 'tool_result' }),
            occurrence(4, { accepted: false, error: 'The per-turn cap was reached.', details: 'Short <b>note</b>.', variant: null, variantName: null })
          ]
        }));
        await create();
        const details = openOccurrences(cards()[0]);
        const [first, second] = Array.from(details.querySelectorAll<HTMLElement>('li.tl-gbfb-occ'));

        expect(textOf(first.querySelector('h5'))).toBe(gnollBenchDateTimeText('2026-10-20T12:30:00Z'));
        const facts = Object.fromEntries(Array.from(first.querySelectorAll('.tl-gbfb-occ-facts > div'))
          .map(f => [textOf(f.querySelector('dt')), textOf(f.querySelector('dd'))]));
        expect(facts).toEqual(expect.objectContaining({
          'Run': 'Run #12 report, opens in a new tab · series #4',
          'Suite': 'Suite A',
          'Model and provider': 'Claude X · Anthropic',
          'Information condition': 'Closed book',
          'Experimental Changes': 'Search with the player\'s race and role, Race and role lookup tool asker-search-words-v1+race-role-tool-v1:0123456789ab',
          'Corpus': 'Live',
          'Harness': '61',
          'Question #9': 'What does a dilithium crystal do?',
          'Searched for': 'wiki_search "dilithium"',
          'Noticed via': 'A tool result contradicted it'
        }));
        const run = first.querySelector<HTMLAnchorElement>('.tl-gbfb-occ-run a')!;
        expect(run.getAttribute('href')).toBe('/api/admin/benchmark/runs/12/report');
        expect(run.getAttribute('target')).toBe('_blank');
        expect(run.getAttribute('rel')).toBe('noopener noreferrer');
        expect(Array.from(first.querySelectorAll('.tl-gbfb-occ-marks .gh-tag')).map(t => textOf(t))).toEqual(['Accepted', 'Truncated']);
        expect(textOf(first.querySelector('.tl-gbfb-occ-cause'))).toBe('Did not verify');

        const text = first.querySelector<HTMLElement>('.tl-gbfb-occ-details')!;
        expect(text.textContent).toBe(long);
        expect(text.classList.contains('is-collapsed')).toBe(true);
        const expand = first.querySelector<HTMLButtonElement>('.tl-gbfb-occ-details-block .tl-gbfb-occ-expand')!;
        expect(expand.getAttribute('aria-expanded')).toBe('false');
        expect(expand.getAttribute('aria-controls')).toBe(text.id);
        expand.click();
        fixture.detectChanges();
        expect(text.classList.contains('is-collapsed')).toBe(false);

        expect(Array.from(second.querySelectorAll('.tl-gbfb-occ-marks .gh-tag')).map(t => textOf(t))).toEqual(['Refused']);
        expect(textOf(second.querySelector('.tl-gbfb-occ-refusal dd'))).toBe('The per-turn cap was reached.');
        expect(textOf(second.querySelector('.tl-gbfb-occ-variant dd'))).toBe('None');
        expect(textOf(second.querySelector('.tl-gbfb-occ-details'))).toBe('Short <b>note</b>.');
        expect(second.querySelector('.tl-gbfb-occ-details b')).toBeNull();
      });

      it('tags an issue and a self-report whose texts hold hidden characters', async () => {
        nextPage = page([issue(1, { subject: 'Tag' + String.fromCodePoint(0xe0041) + 'ged' }), issue(2)]);
        occurrencesSpy.mockReturnValue(of<GnollBenchFeedbackOccurrencePage>({
          totalCount: 1,
          rows: [occurrence(5, { truncated: true, summary: 'Bidi ' + String.fromCodePoint(0x2066) + 'isolate' })]
        }));
        await create();
        const [first, second] = cards();

        const tag = first.querySelector<HTMLElement>('.tl-gbfb-kicker .tl-gbfb-mark-hidden')!;
        expect(textOf(tag)).toBe('Hidden characters');
        expect(tag.getAttribute('title')).toBe('Invisible characters: U+E0041');
        expect(second.querySelector('.tl-gbfb-mark-hidden')).toBeNull();

        const details = openOccurrences(first);
        const item = details.querySelector<HTMLElement>('li.tl-gbfb-occ')!;
        expect(Array.from(item.querySelectorAll('.tl-gbfb-occ-marks .gh-tag')).map(t => textOf(t))).toEqual(['Accepted', 'Truncated', 'Hidden characters']);
      });

      it('renders hostile markup in a subject and a summary as text', async () => {
        const subject = '<img src=x onerror="window.__fbXss=1">';
        const summary = '<script>window.__fbXss=1</script>';
        nextPage = page([issue(1, { subject, summary })]);
        await create();
        const [card] = cards();

        expect(card.querySelector('.tl-gbfb-title label')!.textContent).toBe(subject);
        expect(card.querySelector('.tl-gbfb-summary')!.textContent).toBe(summary);
        expect(card.querySelector('img')).toBeNull();
        expect(card.querySelector('script')).toBeNull();
        expect((window as unknown as Record<string, unknown>)['__fbXss']).toBeUndefined();
      });
    });

    describe('triage', () => {
      it('offers New, Acknowledged, Resolved and Dismissed, never Regressed, with the recurrence rule in its (i), and the three texts', async () => {
        nextPage = page([issue(1, { status: 'regressed' })]);
        const updateSpy = vi.spyOn(service, 'updateStatus');
        await create();
        const card = cards()[0];
        const select = card.querySelector<HTMLSelectElement>(`#${cid(1)}-status`)!;

        expect(textOf(card.querySelector(`label[for="${cid(1)}-status"]`))).toBe('Status');
        expect(Array.from(select.options).map(o => o.value)).toEqual(['', 'new', 'acknowledged', 'resolved', 'dismissed']);
        expect(select.options[0].disabled).toBe(true);
        const help = card.querySelector('#' + select.getAttribute('aria-describedby'))!;
        expect(textOf(help)).toBe('A resolved issue that a run reports again becomes Regressed; a dismissed one stays dismissed.');
        expect(textOf(card.querySelector(`label[for="${cid(1)}-note"]`))).toBe('Note');
        expect(textOf(card.querySelector(`label[for="${cid(1)}-resolution"]`))).toBe('Resolution');
        expect(textOf(card.querySelector(`label[for="${cid(1)}-version"]`))).toBe('Fixed in version');
        expect(card.querySelector(`#${cid(1)}-note`)!.getAttribute('maxlength')).toBe('1000');
        expect(card.querySelector(`#${cid(1)}-resolution`)!.getAttribute('maxlength')).toBe('4000');
        expect(card.querySelector(`#${cid(1)}-version`)!.getAttribute('maxlength')).toBe('64');

        card.querySelector<HTMLButtonElement>('.tl-gbfb-save')!.click();
        fixture.detectChanges();

        expect(updateSpy).not.toHaveBeenCalled();
        expect(textOf(card.querySelector('.tl-gbfb-triage-error'))).toBe('Choose a status first.');
      });

      it('sends the status and only the texts that changed, an emptied one as empty, then takes the server\'s copy and refetches the counts', async () => {
        nextPage = page([issue(1, { status: 'acknowledged', triageNote: 'Old note', resolution: 'Old fix', fixedInVersion: '1.4.0' })]);
        const updated = issue(1, {
          status: 'resolved', triageNote: 'New note', resolution: null, fixedInVersion: '1.4.0',
          triagedByUserName: 'tommi', triagedUtc: '2026-10-21T12:05:00Z', resolvedUtc: '2026-10-21T12:05:00Z'
        });
        const updateSpy = vi.spyOn(service, 'updateStatus').mockReturnValue(of(updated));
        await create();
        countsSpy.mockClear();
        const card = cards()[0];

        expect(card.querySelector<HTMLSelectElement>(`#${cid(1)}-status`)!.value).toBe('acknowledged');
        expect(card.querySelector<HTMLTextAreaElement>(`#${cid(1)}-resolution`)!.value).toBe('Old fix');
        selectValue(card.querySelector<HTMLSelectElement>(`#${cid(1)}-status`)!, 'resolved');
        typeInto(card.querySelector<HTMLTextAreaElement>(`#${cid(1)}-note`)!, '  New note ');
        typeInto(card.querySelector<HTMLTextAreaElement>(`#${cid(1)}-resolution`)!, '  ');
        card.querySelector<HTMLButtonElement>('.tl-gbfb-save')!.click();
        fixture.detectChanges();

        expect(updateSpy).toHaveBeenCalledWith(fp(1), { status: 'resolved', note: 'New note', resolution: '' });
        const result = card.querySelector(`#${cid(1)}-result`)!;
        expect(result.getAttribute('role')).toBe('status');
        expect(textOf(result)).toBe('Saved. The issue is Resolved.');
        expect(textOf(card.querySelector('.tl-gbfb-status-badge'))).toBe('Resolved');
        expect(textOf(card.querySelector('.tl-gbfb-triaged'))).toBe(
          `Triaged ${gnollBenchDateTimeText('2026-10-21T12:05:00Z')} by tommi · resolved ${gnollBenchDateTimeText('2026-10-21T12:05:00Z')}`);
        expect(countsSpy).toHaveBeenCalledTimes(1);
      });

      it('refuses a malformed fixed-in version before any request', async () => {
        const updateSpy = vi.spyOn(service, 'updateStatus');
        await create();
        const card = cards()[0];

        typeInto(card.querySelector<HTMLInputElement>(`#${cid(1)}-version`)!, '1.4 beta');
        card.querySelector<HTMLButtonElement>('.tl-gbfb-save')!.click();
        fixture.detectChanges();

        expect(updateSpy).not.toHaveBeenCalled();
        expect(textOf(card.querySelector('.tl-gbfb-triage-error'))).toBe(
          'Fixed in version takes letters, digits, dots, plus and minus signs only, up to 64 characters.');
      });

      it('shows a failed save under the form', async () => {
        vi.spyOn(service, 'updateStatus').mockReturnValue(throwError(() => ({ status: 400, error: { message: 'regressed is derived.' } })));
        await create();
        const card = cards()[0];

        card.querySelector<HTMLButtonElement>('.tl-gbfb-save')!.click();
        fixture.detectChanges();

        const error = card.querySelector('.tl-gbfb-triage-error.gh-field-error')!;
        expect(textOf(error)).toBe('The issue could not be saved: regressed is derived.');
        expect(card.querySelector('.tl-gbfb-save')!.getAttribute('aria-describedby')).toBe(error.id);
      });
    });

    describe('selection', () => {
      it('keeps a checked card by fingerprint, remembers it and hands it to the selection bar', async () => {
        nextPage = page([issue(1), issue(2)]);
        await create();
        expect(bar().source).toBe('gnollbench');
        expect(bar().selectedIds).toEqual([]);
        expect(bar().matchingCount).toBe(2);

        const box = cards()[0].querySelector<HTMLInputElement>('input.tl-gbfb-check')!;
        box.click();
        fixture.detectChanges();

        expect(loadGnollBenchSelection()).toEqual([fp(1)]);
        expect(bar().selectedIds).toEqual([fp(1)]);
        expect(bar().notShownCount).toBe(0);
        expect(box.checked).toBe(true);
      });

      it('counts the remembered selected issues the filters do not list', async () => {
        localStorage.setItem(SELECTION_KEY, JSON.stringify({ version: 1, fingerprints: [fp(1), fp(9)] }));
        existing = new Set([fp(1), fp(2), fp(9)]);
        idsAnswer = filters => {
          const named = (filters.fingerprints ?? []).filter(f => f === fp(1));
          return { fingerprints: named, totalCount: named.length, truncated: false };
        };
        await create();

        expect(idsSpy).toHaveBeenCalledWith(expect.objectContaining({ fingerprints: [fp(1), fp(9)], statuses: ['new', 'regressed'] }));
        expect(bar().selectedIds).toEqual([fp(1), fp(9)]);
        expect(bar().notShownCount).toBe(1);
        expect(cards()[0].querySelector<HTMLInputElement>('input.tl-gbfb-check')!.checked).toBe(true);
      });

      it('shows the selected issues only by fingerprint, and prunes those the server no longer has', async () => {
        localStorage.setItem(SELECTION_KEY, JSON.stringify({ version: 1, fingerprints: [fp(1), fp(9)] }));
        await create();

        bar().showSelectedOnlyChange.emit(true);
        fixture.detectChanges();

        expect(lastQuery()).toEqual(expect.objectContaining({ fingerprints: [fp(1), fp(9)], skip: 0 }));
        expect(idsSpy).toHaveBeenCalledWith({ accepted: 'all', fingerprints: [fp(1), fp(9)] });
        expect(loadGnollBenchSelection()).toEqual([fp(1)]);
        expect(bar().showSelectedOnly).toBe(true);
        expect(status()).toContain('One selected issue no longer exists and left the selection.');

        bar().showSelectedOnlyChange.emit(false);
        fixture.detectChanges();
        expect(lastQuery().fingerprints).toBeUndefined();
      });

      it('selects every matching issue up to 500 and says when the cap cut the list', async () => {
        await create('/admin/feedback/gnollbench?run=12');
        const many = Array.from({ length: 1000 }, (_, i) => `${i}`.padStart(64, 'a'));
        idsAnswer = () => ({ fingerprints: many, totalCount: 1200, truncated: true });

        bar().selectAllMatching.emit();
        fixture.detectChanges();

        expect(idsSpy).toHaveBeenLastCalledWith(expect.objectContaining({ runs: ['12'] }));
        expect((idsSpy.mock.calls[idsSpy.mock.calls.length - 1][0] as GnollBenchFeedbackFilters).fingerprints).toBeUndefined();
        expect(loadGnollBenchSelection().length).toBe(500);
        expect(bar().selectedIds.length).toBe(500);
        expect(status()).toContain('Selected the first 500 of 1,200.');
      });

      it('clears the selection and its memory', async () => {
        localStorage.setItem(SELECTION_KEY, JSON.stringify({ version: 1, fingerprints: [fp(1)] }));
        await create();

        bar().clear.emit();
        fixture.detectChanges();

        expect(loadGnollBenchSelection()).toEqual([]);
        expect(bar().selectedIds).toEqual([]);
        expect(cards()[0].querySelector<HTMLInputElement>('input.tl-gbfb-check')!.checked).toBe(false);
      });

      it('drops exported issues that no longer exist and remembers the export options in the view', async () => {
        localStorage.setItem(SELECTION_KEY, JSON.stringify({ version: 1, fingerprints: [fp(1), fp(9)] }));
        await create();
        const options: FeedbackExportOptions = { occurrencesPerIssue: 25, includeQuestionText: false };

        bar().exported.emit({ missing: [fp(9)], exportOptions: options });
        fixture.detectChanges();

        expect(loadGnollBenchSelection()).toEqual([fp(1)]);
        expect(loadFeedbackView('gnollbench')!.exportOptions).toEqual(options);
        expect(bar().exportOptions).toEqual(options);
      });

      it('fetches the list, the facets and the counts again after an import', async () => {
        await create();
        issuesSpy.mockClear();
        facetsSpy.mockClear();
        countsSpy.mockClear();

        bar().imported.emit();
        fixture.detectChanges();

        expect(listQueries().length).toBe(1);
        expect(facetsSpy).toHaveBeenCalledTimes(1);
        expect(countsSpy).toHaveBeenCalledTimes(1);
      });
    });
  });
});
