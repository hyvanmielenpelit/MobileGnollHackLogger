import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { AdminBenchmarkComponent, RUN_HISTORY_VIEW_STORAGE_KEY } from './benchmark.component';
import { AdminBenchmarkService } from '../../services/admin-benchmark.service';
import { runDurationMs } from './benchmark-run-format';
import {
  AdminBenchmarkSpecContext, benchmarkSpecHandles, clearStoredState, createAdminBenchmarkFixture
} from './benchmark.component.testing';

describe('AdminBenchmarkComponent', () => {
  let ctx: AdminBenchmarkSpecContext;
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: MockedObject<AdminBenchmarkService>;

  beforeEach(clearStoredState);

  afterEach(clearStoredState);

  beforeEach(async () => {
    ctx = await createAdminBenchmarkFixture();
    ({ component, fixture, benchmarkServiceMock } = ctx);
  });

  describe('Run History card list (data-table)', () => {
    function buildHistoryRun(overrides: Record<string, unknown> = {}): any {
      return {
        id: 1,
        benchmarkSuiteId: 1,
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Model A',
        testedModelProviderUsed: 'Anthropic',
        testedModelIdUsed: 'model-a',
        assessorModelDisplayNameUsed: 'Model B',
        status: 'Completed',
        startedAtUtc: '2026-09-01T00:00:00Z',
        totalAnswerDurationMs: 1000,
        totalDurationMs: 1000,
        speedMeasurementDegraded: false,
        answeredQuestionCount: 5,
        totalQuestionCount: 5,
        candidateSystemPromptSha256: 'sha-a',
        toolGuidesSha256: 'guide-a',
        knowledgeBaseHeadSha: 'kb-a',
        wikiHeadSha: 'wiki-a',
        sourceCodeHeadSha: 'src-a',
        ...overrides
      };
    }

    it('should default to sorting by ID, descending', () => {
      expect(ctx.workspace.historyTable.sortColumn).toBe('id');
      expect(ctx.workspace.historyTable.sortDirection).toBe('desc');
    });

    /** Renders the Run History tab; its ngOnInit loads the history, which a test replaces afterwards. */
    function showHistoryTab(): void {
      component.selectSubTab('history');
      fixture.detectChanges();
    }

    it('should keep instrumentChangeOf verdicts unchanged when the view is sorted', () => {
      showHistoryTab();
      ctx.workspace.historyRuns = [
        buildHistoryRun({ id: 3, startedAtUtc: '2026-09-03T00:00:00Z', candidateSystemPromptSha256: 'sha-b' }),
        buildHistoryRun({ id: 2, startedAtUtc: '2026-09-02T00:00:00Z', status: 'Running' }),
        buildHistoryRun({ id: 1, startedAtUtc: '2026-09-01T00:00:00Z' })
      ];

      const before = ctx.workspace.instrumentChangeOf(ctx.workspace.historyRuns[0]);
      expect(before).toBeTruthy();
      expect(before?.comparedToRunId).toBe(1);

      // historyView is a new sorted array; historyRuns itself — which instrumentChangeOf and
      // completedRunsOfSelectedSuite both read by position — must not move under it.
      expect(ctx.historyTab().historyView.map(r => r.id)).toEqual([3, 2, 1]);
      ctx.workspace.historyTable.toggleSort('startedAtUtc');
      ctx.workspace.historyTable.toggleSort('startedAtUtc');
      expect(ctx.historyTab().historyView.map(r => r.id)).toEqual([1, 2, 3]);

      const after = ctx.workspace.instrumentChangeOf(ctx.workspace.historyRuns[0]);
      expect(after).toEqual(before);
    });

    it('should derive the Status filter options from the statuses present in the history', () => {
      showHistoryTab();
      ctx.workspace.historyRuns = [
        buildHistoryRun({ id: 1, status: 'Completed' }),
        buildHistoryRun({ id: 2, status: 'CompletedWithLimits' }),
        buildHistoryRun({ id: 3, status: 'Completed' })
      ];

      expect(ctx.historyTab().historyStatusOptions).toEqual(['Completed', 'Completed with limits']);
    });

    it('should batch and filter the view without touching historyRuns', () => {
      const runs = Array.from({ length: 25 }, (_, i) => buildHistoryRun({ id: 25 - i, suiteName: `Suite ${(i % 2) + 1}` }));
      showHistoryTab();
      ctx.workspace.historyRuns = runs;

      expect(ctx.historyTab().historyView.length).toBe(10);
      ctx.historyTab().showMoreHistory();
      expect(ctx.historyTab().historyView.length).toBe(20);

      // A facet change returns the list to one batch.
      ctx.historyTab().onHistoryFacetChange('suite', ['Suite 2']);
      expect(ctx.historyTab().historyView.length).toBe(10);
      expect(ctx.historyTab().historyView.every(r => r.suiteName === 'Suite 2')).toBe(true);
      expect(ctx.workspace.historyList.matching(ctx.workspace.historyRuns).length).toBe(12);

      expect(ctx.workspace.historyRuns).toBe(runs);
      expect(ctx.workspace.historyRuns.length).toBe(25);
      expect(ctx.workspace.historyRuns.map(r => r.id)).toEqual(Array.from({ length: 25 }, (_, i) => 25 - i));
    });

    /** An element's visible text: its text without the visually hidden parts. */
    function visibleText(element: Element): string {
      const clone = element.cloneNode(true) as Element;
      clone.querySelectorAll('.visually-hidden').forEach(hidden => hidden.remove());
      return (clone.textContent || '').replace(/\s+/g, ' ').trim();
    }

    it('should list a labeled pair per fingerprint, dashing a hash that was not recorded, and the full hashes in its info tip', () => {
      component.activeSubTab = 'history';
      // Renders the tab, whose ngOnInit loads the history; the run below replaces it.
      ctx.refresh();
      ctx.workspace.historyRuns = [buildHistoryRun({ id: 1, wikiHeadSha: null })];
      ctx.refresh();

      const strip = fixture.nativeElement.querySelector('.rh-card .rh-instrument-strip') as HTMLElement;
      const pairs = Array.from(strip.querySelectorAll('dl.rh-instrument > div')) as HTMLElement[];

      // Five pairs whatever the run recorded: the label carries the meaning, so the strip stays
      // legible in grayscale, and a missing hash is visible as a dash rather than absent.
      expect(pairs.length).toBe(5);
      expect(pairs.map(pair => visibleText(pair.querySelector('dt')!))).toEqual(['PROMPT', 'GUIDES', 'KB', 'WIKI', 'SRC']);
      expect(pairs.map(pair => pair.querySelector('dt .visually-hidden')?.textContent?.trim())).toEqual([
        '(candidate system prompt)', '(tool guides)', '(knowledge base)', '(wiki)', '(source code)'
      ]);
      const values = pairs.map(pair => pair.querySelector('dd') as HTMLElement);
      expect(values.map(dd => dd.textContent?.trim())).toEqual(['sha-a', 'guide-a', 'kb-a', '-', 'src-a']);

      const cssClasses = ['fp-prompt', 'fp-guides', 'fp-kb', 'fp-wiki', 'fp-source'];
      cssClasses.forEach((cssClass, i) => expect(values[i].classList.contains(cssClass)).toBe(true));
      expect(getComputedStyle(values[0]).fontFamily).toContain('monospace');

      expect(strip.querySelectorAll('[title]').length).toBe(0);

      const button = strip.querySelector('app-info-tip button.gh-info-btn') as HTMLButtonElement;
      expect(button.getAttribute('aria-label')).toBe('About Instrument of run 1');
      const tip = strip.querySelector('#rh-instr-1') as HTMLElement;
      const terms = Array.from(tip.querySelectorAll('dt')).map(dt => dt.textContent?.trim());
      const full = Array.from(tip.querySelectorAll('dd')).map(dd => dd.textContent?.trim());
      expect(terms).toEqual([
        'Candidate system prompt SHA-256',
        'Tool guides SHA-256',
        'Knowledge base Git HEAD SHA',
        'GnollHack wiki Git HEAD SHA',
        'GnollHack source Git HEAD SHA'
      ]);
      expect(full).toEqual(['sha-a', 'guide-a', 'kb-a', 'not recorded', 'src-a']);
    });

    /** Enters Run History through its real tab, with the server returning these runs. */
    function openHistoryWith(runs: any[]): void {
      benchmarkServiceMock.getRuns.mockReturnValue(of(runs));
      (fixture.nativeElement.querySelector('#bm-tab-history') as HTMLButtonElement).click();
      fixture.detectChanges();
    }

    /** The runs whose cards are shown, by id, in order. */
    function shownIds(): number[] {
      return (Array.from(fixture.nativeElement.querySelectorAll('.rh-card-list article.rh-card')) as HTMLElement[])
        .map(card => Number(card.getAttribute('data-run-id')));
    }

    /** The active-filter chips. */
    function chipButtons(): HTMLButtonElement[] {
      return Array.from(fixture.nativeElement.querySelectorAll('.rh-filter-chips .gh-filter-chip')) as HTMLButtonElement[];
    }

    function historyStatus(): string {
      return (fixture.nativeElement.querySelector('#rh-list-status')?.textContent || '').trim();
    }

    /**
     * Fakes the clock the search debounce runs on. Only `setTimeout` and `clearTimeout`: the
     * component's polling intervals keep running on the real clock.
     */
    function useSearchClock(): void {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    }

    afterEach(() => {
      vi.useRealTimers();
    });

    /** Resolves once a debounced search has applied, on the clock `useSearchClock` fakes. */
    function afterSearchDebounce(): Promise<void> {
      vi.advanceTimersByTime(ctx.workspace.historyList.debounceMs);
      return Promise.resolve();
    }

    it('should show the degraded count as a glyph and words, the start time, both cost lines and the four actions', () => {
      openHistoryWith([buildHistoryRun({
        id: 42,
        startedAtUtc: '2026-09-28T16:12:00',
        estimatedCandidateCost: 2.5211,
        estimatedCost: 6.0068,
        degradedAnswerCount: 2
      })]);

      const card = fixture.nativeElement.querySelector('.rh-card-list > li > article.rh-card') as HTMLElement;
      expect(card).toBeTruthy();
      expect(card.getAttribute('aria-labelledby')).toBe('rh-run-42-title');
      expect(card.querySelector('h5#rh-run-42-title')?.getAttribute('tabindex')).toBe('-1');

      const degraded = card.querySelector('.rh-card-kicker .badge-degraded-count') as HTMLElement;
      expect(degraded.textContent?.replace(/\s+/g, ' ').trim()).toBe('2 degraded answers');
      expect(visibleText(degraded)).toBe('2');
      const glyph = degraded.querySelector('svg') as SVGElement;
      expect(glyph.getAttribute('aria-hidden')).toBe('true');
      expect(glyph.getAttribute('width')).toBe('12');
      expect(degraded.hasAttribute('title')).toBe(false);
      expect(card.textContent).not.toContain('⚠');

      const time = card.querySelector('.rh-card-meta time') as HTMLTimeElement;
      expect(time.getAttribute('datetime')).toBe('2026-09-28T16:12:00');
      expect(time.textContent?.trim()).toBe('2026-09-28 16:12 UTC');

      const cost = card.querySelector('.rh-metric[data-metric="cost"]') as HTMLElement;
      expect(Array.from(cost.querySelectorAll('dd > span')).map(span => span.textContent?.trim()))
        .toEqual(['$2.5211', 'catalog $6.0068']);

      const actions = card.querySelector('.rh-card-actions[role="group"]') as HTMLElement;
      expect(actions.getAttribute('aria-label')).toBe('Actions for run 42');
      const buttons = Array.from(actions.querySelectorAll('button.action-btn')) as HTMLButtonElement[];
      expect(buttons.map(b => b.getAttribute('aria-label'))).toEqual([
        'View details for run 42',
        'Download Markdown report for run 42',
        'Download tool-call log for run 42',
        'Delete run 42'
      ]);
      // The file-with-arrow glyph: a file whose arrow points down into it.
      expect(buttons[1].querySelector('path')?.getAttribute('d')).toMatch(/^M14 2H6/);
      expect(buttons[1].querySelector('polyline[points="9 15 12 18 15 15"]')).toBeTruthy();
      expect(buttons[2].querySelector('polyline[points="9 15 12 18 15 15"]')).toBeNull();
      expect(buttons[3].classList.contains('action-btn-danger')).toBe(true);
      expect(buttons.map(b => b.getAttribute('interestfor'))).toEqual([
        'tip-view-run-42', 'tip-dl-run-42', 'tip-tcl-run-42', 'tip-del-run-42'
      ]);
      expect(card.querySelectorAll('[title]').length).toBe(0);
    });

    it('should fit ten run cards in 1360 px without scrolling sideways, their metrics lined up', async () => {
      const hash = (seed: string, length: number) => seed.repeat(Math.ceil(length / seed.length)).substring(0, length);
      const instrument = {
        candidateSystemPromptSha256: hash('3f9d06fa', 64),
        toolGuidesSha256: hash('b66a59b2', 64),
        knowledgeBaseHeadSha: hash('7424b03c', 40),
        wikiHeadSha: hash('080485a1', 40),
        sourceCodeHeadSha: hash('429db58e', 40),
        candidatePromptOptionsJson: '{"verboseMode":false,"enableToolUse":true}'
      };
      const suites = [
        { benchmarkSuiteId: 7, suiteName: 'Snapshot: Tommi2 2026-09-17 (long variant for wrapping)' },
        { benchmarkSuiteId: 5, suiteName: 'Snapshot: Tommi2 2026-09-17' },
        { benchmarkSuiteId: 3, suiteName: 'GnollHack Mechanics Core' }
      ];
      const models = ['GPT-5.6 Luna', 'Claude 5 Opus', 'Gemini 3.8 Flash'];
      const runs = Array.from({ length: 10 }, (_, i) => buildHistoryRun({
        ...instrument,
        ...suites[i % suites.length],
        id: 110 - i,
        testedModelDisplayNameUsed: models[i % models.length],
        assessorModelDisplayNameUsed: 'Claude 5 Opus',
        qualityIndex: 60 + i * 3,
        speedIndex: 90 - i * 4,
        totalAnswerDurationMs: 765466 + i * 61000,
        totalDurationMs: 1419000 + i * 61000,
        estimatedCandidateCost: 2.5211,
        estimatedCost: 6.0068,
        startedAtUtc: `2026-09-${String(28 - i).padStart(2, '0')}T16:12:00`
      }));
      // The newest run of the long-named suite moved its knowledge base since run 107, the next
      // older run of that suite, so it carries the INSTRUMENT CHANGED badge.
      runs[0].knowledgeBaseHeadSha = hash('c0ffee42', 40);
      runs[1].degradedAnswerCount = 3;

      fixture.nativeElement.style.width = '1360px';
      openHistoryWith(runs);
      await document.fonts.ready;
      fixture.detectChanges();

      const cards = Array.from(fixture.nativeElement.querySelectorAll('.rh-card-list > li > article.rh-card')) as HTMLElement[];
      expect(cards.length).toBe(10);
      expect(fixture.nativeElement.querySelectorAll('.rh-card .rh-instrument dd').length).toBe(50);
      expect(Array.from(fixture.nativeElement.querySelectorAll('.rh-card .instrument-changed'))
        .map((badge: any) => badge.textContent.trim())).toEqual(['INSTRUMENT CHANGED']);

      const list = fixture.nativeElement.querySelector('.rh-card-list') as HTMLElement;
      expect(list.clientWidth).toBeGreaterThan(0);
      expect(list.scrollWidth).toBeLessThanOrEqual(list.clientWidth);
      for (const card of cards) {
        expect(card.scrollWidth, card.getAttribute('data-run-id')!).toBeLessThanOrEqual(card.clientWidth);
      }

      // Each metric column starts at the same x-position on every card, so the list scans like a table.
      for (let column = 0; column < 4; column++) {
        const lefts = cards.map(card => Math.round((card.querySelectorAll('.rh-metrics > .rh-metric')[column] as HTMLElement).getBoundingClientRect().left));
        expect(new Set(lefts).size, `metric column ${column}`).toBe(1);
      }

      // The model under test is the card's headline.
      const model = getComputedStyle(cards[0].querySelector('.rh-card-model') as HTMLElement);
      const meta = getComputedStyle(cards[0].querySelector('.rh-card-meta') as HTMLElement);
      expect(parseFloat(model.fontSize)).toBeGreaterThan(parseFloat(meta.fontSize));
      expect(parseFloat(model.fontWeight)).toBeGreaterThanOrEqual(700);
    });

    it('should filter by the Suite facet without asking the server again', () => {
      openHistoryWith([
        buildHistoryRun({ id: 3, suiteName: 'Alpha' }),
        buildHistoryRun({ id: 2, suiteName: 'Beta' }),
        buildHistoryRun({ id: 1, suiteName: 'Alpha' })
      ]);
      benchmarkServiceMock.getRuns.mockClear();

      const facet = ctx.historyTab().historyFacets.find(f => f.column === 'suite')!;
      expect(facet.facetId).toBe('rh-facet-suite');
      expect(facet.options.map(o => [o.value, o.count])).toEqual([['Alpha', 2], ['Beta', 1]]);
      expect(fixture.nativeElement.querySelector('.rh-facet-row #rh-facet-suite-trigger')).toBeTruthy();
      // The server-side suite select is gone.
      expect(fixture.nativeElement.querySelector('#historySuiteFilter')).toBeNull();

      ctx.historyTab().onHistoryFacetChange('suite', ['Alpha']);

      expect(shownIds()).toEqual([3, 1]);
      expect(chipButtons().map(chip => chip.getAttribute('aria-label'))).toEqual(['Remove filter Suite: Alpha']);
      expect(benchmarkServiceMock.getRuns).not.toHaveBeenCalled();
    });

    it('should find a run by its #id and by a hash prefix', async () => {
      useSearchClock();
      openHistoryWith([
        buildHistoryRun({ id: 42, knowledgeBaseHeadSha: '1b512e27aa55' }),
        buildHistoryRun({ id: 7 })
      ]);
      const search = fixture.nativeElement.querySelector('#rh-search') as HTMLInputElement;
      expect(fixture.nativeElement.querySelector('label[for="rh-search"]')?.textContent?.trim()).toBe('Search runs');

      search.value = '#42';
      search.dispatchEvent(new Event('input'));
      await afterSearchDebounce();
      expect(shownIds()).toEqual([42]);

      search.value = '1B512E';
      search.dispatchEvent(new Event('input'));
      await afterSearchDebounce();
      expect(shownIds()).toEqual([42]);
      expect(chipButtons().map(chip => chip.getAttribute('aria-label'))).toEqual(['Remove filter Search: “1B512E”']);

      search.value = '#7';
      search.dispatchEvent(new Event('input'));
      await afterSearchDebounce();
      expect(shownIds()).toEqual([7]);
    });

    it('should start the search text to the right of its glyph', async () => {
      openHistoryWith([buildHistoryRun({ id: 1 })]);
      await document.fonts.ready;
      fixture.detectChanges();

      const search = fixture.nativeElement.querySelector('#rh-search') as HTMLInputElement;
      const glyph = fixture.nativeElement.querySelector('.rh-search > svg') as SVGElement;
      expect(getComputedStyle(search).paddingInlineStart).toBe('34px');
      const field = search.getBoundingClientRect();
      expect(field.left + 34).toBeGreaterThanOrEqual(glyph.getBoundingClientRect().right);
      expect(field.height).toBeLessThanOrEqual(33);
    });

    it('should clear the search on Escape without closing anything, and let Escape through when it is empty', async () => {
      useSearchClock();
      openHistoryWith([buildHistoryRun({ id: 2 }), buildHistoryRun({ id: 1 })]);
      const panel = fixture.nativeElement.querySelector('#bm-panel-history') as HTMLElement;
      const reached: KeyboardEvent[] = [];
      panel.addEventListener('keydown', event => reached.push(event));
      const search = fixture.nativeElement.querySelector('#rh-search') as HTMLInputElement;

      search.value = 'model';
      search.dispatchEvent(new Event('input'));
      const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      search.dispatchEvent(escape);

      expect(escape.defaultPrevented).toBe(true);
      expect(reached).toEqual([]);
      expect(search.value).toBe('');
      expect(ctx.workspace.historyList.searchText).toBe('');
      // The pending search was dropped with the text.
      await afterSearchDebounce();
      expect(ctx.workspace.historyTable.hasActiveFilters).toBe(false);
      expect(shownIds()).toEqual([2, 1]);

      const again = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      search.dispatchEvent(again);
      expect(again.defaultPrevented).toBe(false);
      expect(reached.length).toBe(1);
      expect(reached[0]).toBe(again);
    });

    it('should remember the Sort by choice and restore it', () => {
      openHistoryWith([buildHistoryRun({ id: 2, qualityIndex: 50 }), buildHistoryRun({ id: 1, qualityIndex: 90 })]);
      const select = fixture.nativeElement.querySelector('#rh-sort') as HTMLSelectElement;
      expect(fixture.nativeElement.querySelector('label[for="rh-sort"]')?.textContent?.trim()).toBe('Sort by');
      expect(Array.from(select.options).map(option => option.textContent?.trim())).toEqual([
        'Newest first',
        'Oldest first',
        'Intelligence Index, highest first',
        'Speed Index, highest first',
        'Cost, lowest first',
        'Cost, highest first',
        'Duration, shortest first',
        'Tested model (A–Z)',
        'Suite (A–Z)'
      ]);
      expect(select.value).toBe('newest');
      expect(shownIds()).toEqual([2, 1]);

      select.value = 'intelligence-desc';
      select.dispatchEvent(new Event('change'));

      expect(shownIds()).toEqual([1, 2]);
      expect(JSON.parse(localStorage.getItem(RUN_HISTORY_VIEW_STORAGE_KEY)!)).toEqual({ version: 1, sort: 'intelligence-desc' });

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      const restoredWorkspace = benchmarkSpecHandles(restored).workspace;
      expect(restoredWorkspace.historyList.sortId).toBe('intelligence-desc');
      expect(restoredWorkspace.historyTable.sortColumn).toBe('qualityIndex');
      expect(restoredWorkspace.historyTable.sortDirection).toBe('desc');
      restored.destroy();
    });

    it('should move focus to the next chip after a chip is removed, then to the search field', () => {
      openHistoryWith([
        buildHistoryRun({ id: 3, suiteName: 'Alpha' }),
        buildHistoryRun({ id: 2, suiteName: 'Beta' }),
        buildHistoryRun({ id: 1, suiteName: 'Gamma' })
      ]);
      ctx.historyTab().onHistoryFacetChange('suite', ['Alpha', 'Beta']);
      expect(chipButtons().map(chip => chip.getAttribute('aria-label')))
        .toEqual(['Remove filter Suite: Alpha', 'Remove filter Suite: Beta']);

      chipButtons()[0].click();
      expect(chipButtons().map(chip => chip.getAttribute('aria-label'))).toEqual(['Remove filter Suite: Beta']);
      expect(document.activeElement).toBe(chipButtons()[0]);
      expect(shownIds()).toEqual([2]);

      chipButtons()[0].click();
      expect(chipButtons()).toEqual([]);
      expect(document.activeElement).toBe(fixture.nativeElement.querySelector('#rh-search'));
      expect(shownIds()).toEqual([3, 2, 1]);
    });

    it('should focus the first new card title after Show 10 more', () => {
      openHistoryWith(Array.from({ length: 25 }, (_, i) => buildHistoryRun({ id: 25 - i })));
      expect(shownIds().length).toBe(10);

      const more = fixture.nativeElement.querySelector('.rh-load-more .rh-show-more') as HTMLButtonElement;
      expect(more.classList.contains('btn-ghost')).toBe(true);
      expect(more.textContent?.trim()).toBe('Show 10 more');
      const all = fixture.nativeElement.querySelector('.rh-load-more .rh-show-all') as HTMLButtonElement;
      expect(all.classList.contains('gh-filter-clear')).toBe(true);
      expect(all.textContent?.trim()).toBe('Show all 25');

      more.click();

      expect(shownIds().length).toBe(20);
      expect(document.activeElement?.id).toBe('rh-run-15-title');
      expect(historyStatus()).toBe('Showing 20 of 25 runs');
      expect((fixture.nativeElement.querySelector('.rh-show-more') as HTMLElement).textContent?.trim()).toBe('Show 5 more');
      // One batch or less remains, so Show all is not offered.
      expect(fixture.nativeElement.querySelector('.rh-show-all')).toBeNull();
    });

    it('should move focus to the next card title after a delete, else the previous one, else the heading', () => {
      const runs = [3, 2, 1].map(id => buildHistoryRun({ id }));
      openHistoryWith(runs);
      benchmarkServiceMock.deleteRun.mockReturnValue(of(undefined as any));

      // The middle card: its successor takes its place.
      benchmarkServiceMock.getRuns.mockReturnValue(of([runs[0], runs[2]]));
      (fixture.nativeElement.querySelector('button[aria-label="Delete run 2"]') as HTMLButtonElement).click();
      component.executeConfirmAction();
      expect(benchmarkServiceMock.deleteRun).toHaveBeenCalledWith(2);
      expect(shownIds()).toEqual([3, 1]);
      expect(document.activeElement?.id).toBe('rh-run-1-title');

      // The last card: the one before it.
      benchmarkServiceMock.getRuns.mockReturnValue(of([runs[0]]));
      (fixture.nativeElement.querySelector('button[aria-label="Delete run 1"]') as HTMLButtonElement).click();
      component.executeConfirmAction();
      expect(document.activeElement?.id).toBe('rh-run-3-title');

      // The only card: the list's heading.
      benchmarkServiceMock.getRuns.mockReturnValue(of([]));
      (fixture.nativeElement.querySelector('button[aria-label="Delete run 3"]') as HTMLButtonElement).click();
      component.executeConfirmAction();
      expect(document.activeElement?.id).toBe('rh-list-title');
    });

    it('should count the runs in the status line, and say when the newest 200 are all that is loaded', () => {
      openHistoryWith(Array.from({ length: 12 }, (_, i) => buildHistoryRun({ id: 12 - i })));
      const status = fixture.nativeElement.querySelector('#rh-list-status') as HTMLElement;
      expect(status.getAttribute('role')).toBe('status');
      expect(historyStatus()).toBe('Showing 10 of 12 runs');

      benchmarkServiceMock.getRuns.mockClear();
      benchmarkServiceMock.getRuns.mockReturnValue(of(Array.from({ length: 200 }, (_, i) => buildHistoryRun({ id: 200 - i }))));
      const refresh = fixture.nativeElement.querySelector('.rh-list-head .rh-refresh') as HTMLButtonElement;
      expect(refresh.classList.contains('btn-ghost')).toBe(true);
      refresh.click();

      expect(benchmarkServiceMock.getRuns).toHaveBeenCalledTimes(1);

      expect(benchmarkServiceMock.getRuns).toHaveBeenCalledWith(undefined, 200);
      expect(historyStatus()).toBe('Showing 10 of 200 runs · Only the newest 200 runs are loaded');
    });

    describe('list head', () => {
      afterEach(() => {
        for (const dialog of Array.from(document.querySelectorAll('dialog[open]')) as HTMLDialogElement[]) {
          dialog.close();
        }
      });

      function head(): HTMLElement {
        return fixture.nativeElement.querySelector('.rh-list-head') as HTMLElement;
      }

      it('should center the Runs heading, its info button, the status line and Refresh on one line', () => {
        openHistoryWith([buildHistoryRun({ id: 1 })]);

        expect(getComputedStyle(head()).alignItems).toBe('center');
        // One line is the 32 px Refresh and the 6 px padding; a wrapped head is 72 px or more.
        expect(head().clientHeight).toBeLessThan(50);

        const centerOf = (selector: string): number => {
          const rect = (head().querySelector(selector) as HTMLElement).getBoundingClientRect();
          return rect.top + rect.height / 2;
        };
        const button = centerOf('app-info-tip .gh-info-btn');
        expect(Math.abs(centerOf('#rh-list-title') - button)).toBeLessThanOrEqual(1);
        expect(Math.abs(centerOf('#rh-list-status') - button)).toBeLessThanOrEqual(1);
        expect(Math.abs(centerOf('.rh-refresh') - button)).toBeLessThanOrEqual(1);
      });

      it('should open the Runs help in a modal dialog, not a popup', () => {
        openHistoryWith([buildHistoryRun({ id: 1 })]);

        const button = head().querySelector('app-info-tip button.gh-info-btn') as HTMLButtonElement;
        expect(button.getAttribute('aria-label')).toBe('About Run history');
        expect(button.getAttribute('aria-haspopup')).toBe('dialog');
        expect(button.hasAttribute('popovertarget')).toBe(false);
        expect(head().querySelector('.gh-info-popup')).toBeNull();

        const dialog = head().querySelector('app-info-tip dialog.gh-info-dialog') as HTMLDialogElement;
        expect(dialog.open).toBe(false);

        button.focus();
        button.click();
        expect(dialog.open).toBe(true);
        expect(dialog.matches(':modal')).toBe(true);
        const title = dialog.querySelector('h3') as HTMLElement;
        expect(title.id).toBe('rh-list-tip-title');
        expect(title.textContent?.trim()).toBe('About the run history');
        expect(document.activeElement).toBe(title);

        const body = dialog.querySelector('#rh-list-tip') as HTMLElement;
        expect(body.classList).toContain('gh-info-dialog-body');
        expect(Array.from(body.querySelectorAll('dt')).map(dt => dt.textContent?.trim())).toEqual([
          'Intelligence', 'Speed', 'Cost', 'Instrument', 'Instrument changed, Options changed'
        ]);

        const close = dialog.querySelector('.dialog-header .btn-icon-action') as HTMLButtonElement;
        expect(close.getAttribute('aria-label')).toBe('Close About the run history');
        close.click();
        expect(dialog.open).toBe(false);
        expect(document.activeElement).toBe(button);
      });
    });

    it('should tell no runs recorded apart from no runs matching the filters', () => {
      openHistoryWith([]);
      const panel = () => fixture.nativeElement.querySelector('#bm-panel-history') as HTMLElement;
      expect(panel().textContent).toContain('No benchmark runs recorded yet.');
      expect(panel().querySelector('.rh-no-matches')).toBeNull();
      expect(panel().querySelector('.rh-filter-bar')).toBeNull();

      benchmarkServiceMock.getRuns.mockReturnValue(of([buildHistoryRun({ id: 2 }), buildHistoryRun({ id: 1 })]));
      (panel().querySelector('.rh-refresh') as HTMLButtonElement).click();
      ctx.historyTab().onHistoryFacetChange('status', ['Failed']);

      const noMatches = panel().querySelector('.rh-no-matches') as HTMLElement;
      expect(noMatches.textContent).toContain('No runs match these filters.');
      expect(panel().textContent).not.toContain('No benchmark runs recorded yet.');
      expect(panel().querySelector('.rh-card-list')).toBeNull();

      const clear = Array.from(noMatches.querySelectorAll('button')).find(b => b.textContent?.trim() === 'Clear all filters') as HTMLButtonElement;
      clear.click();
      expect(shownIds()).toEqual([2, 1]);
      expect(document.activeElement).toBe(panel().querySelector('#rh-search'));
    });

    it('should count the Changes facet by instrumentChangeOf', () => {
      openHistoryWith([
        // A knowledge-base move since run 3, whose options cannot be compared with none.
        buildHistoryRun({ id: 4, knowledgeBaseHeadSha: 'kb-b' }),
        // Detailed rather than concise since run 2.
        buildHistoryRun({ id: 3, candidatePromptOptionsJson: '{"verboseMode":true}' }),
        buildHistoryRun({ id: 2, candidatePromptOptionsJson: '{"verboseMode":false}' }),
        buildHistoryRun({ id: 1 })
      ]);

      const counts = new Map<string, number>();
      for (const run of ctx.workspace.historyRuns) {
        const change = ctx.workspace.instrumentChangeOf(run);
        const value = change ? (change.kind === 'options' ? 'Options changed' : 'Instrument changed') : 'No change';
        counts.set(value, (counts.get(value) ?? 0) + 1);
      }

      const facet = ctx.historyTab().historyFacets.find(f => f.column === 'changes')!;
      expect(facet.options.map(o => [o.value, o.count])).toEqual([
        ['Instrument changed', counts.get('Instrument changed') ?? 0],
        ['Options changed', counts.get('Options changed') ?? 0],
        ['No change', counts.get('No change') ?? 0]
      ]);
      expect(facet.options.map(o => o.count)).toEqual([1, 1, 2]);

      ctx.historyTab().onHistoryFacetChange('changes', ['Instrument changed']);
      expect(shownIds()).toEqual([4]);
    });
  });

  // ---------------------------------------------------------------------------
  // Aborted runs, the answer shortfall badge, and the instrument-vs-options distinction
  //
  // A run that stopped early has no answer-duration total to be measured by, and a run that
  // answered fewer questions than its suite holds must say so beside its status. The instrument
  // badge is a separate claim: a changed run option moves the candidate hash on its own, and
  // reporting that as instrument drift blames the measuring stick for a change to what is measured.
  // ---------------------------------------------------------------------------
  describe('aborted runs, answer shortfall and the instrument badge', () => {
    function buildRun(overrides: Record<string, unknown> = {}): any {
      return {
        id: 1,
        benchmarkSuiteId: 1,
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Gemini 3.1 Pro',
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-3.1-pro',
        assessorModelDisplayNameUsed: 'Claude Opus',
        status: 'Completed',
        startedAtUtc: '2026-09-08T13:35:00Z',
        completedAtUtc: '2026-09-08T13:58:39Z',
        totalAnswerDurationMs: 765466,
        totalDurationMs: 1419000,
        speedMeasurementDegraded: false,
        answeredQuestionCount: 18,
        totalQuestionCount: 18,
        unansweredQuestionCount: 0,
        candidateSystemPromptSha256: 'sha-a',
        toolGuidesSha256: 'guide-a',
        knowledgeBaseHeadSha: 'kb-a',
        wikiHeadSha: 'wiki-a',
        sourceCodeHeadSha: 'src-a',
        candidatePromptOptionsJson: '{"verboseMode":false,"enableToolUse":true}',
        ...overrides
      };
    }

    it('should measure an aborted run by the wall clock, not by the time its answers took', () => {
      const run = buildRun({ status: 'Canceled', totalAnswerDurationMs: 2000000, totalDurationMs: 1419000 });

      expect(runDurationMs(run)).toBe(1419000);
      expect(component.isAbortedRun(run)).toBe(true);
    });

    it('should not treat a Canceled run whose answers cover its suite as aborted', () => {
      // A cancelled retry of a finished run: every answer row is still there, so the server
      // reports isAborted false and the run is measured by its answers like any complete run.
      const run = buildRun({ status: 'Canceled', isAborted: false });

      expect(component.isAbortedRun(run)).toBe(false);
      expect(runDurationMs(run)).toBe(765466);
    });

    it('should trust the server flag over the status', () => {
      const run = buildRun({ status: 'CompletedWithErrors', isAborted: true });

      expect(component.isAbortedRun(run)).toBe(true);
    });

    it('should measure a completed run by the time its answers took', () => {
      const run = buildRun();

      expect(runDurationMs(run)).toBe(765466);
      expect(component.isAbortedRun(run)).toBe(false);
    });

    it('should derive a duration from the timestamps when neither total was recorded', () => {
      const run = buildRun({ status: 'Canceled', totalAnswerDurationMs: 0, totalDurationMs: 0 });

      // 13:35:00 to 13:58:39 is run 24's own wall clock: 23m 39s.
      expect(runDurationMs(run)).toBe(1419000);
    });

    // The run detail's two duration cards. The Answer Duration card has no wall-clock fallback:
    // the two figures measure different things, so substituting one for the other would put a
    // wall-clock number under a label that says answer time.
    it('should label the two run detail durations as the separate figures they are', () => {
      const run = buildRun({ status: 'Canceled' });

      expect(component.runAnswerDurationLabel(run)).toBe('12m 45s');
      expect(component.runWallClockLabel(run)).toBe('23m 39s');
    });

    it('should dash the Answer Duration card rather than borrow the wall clock', () => {
      const run = buildRun({ status: 'Canceled', totalAnswerDurationMs: 0 });

      expect(component.runAnswerDurationLabel(run)).toBe('—');
      expect(component.runWallClockLabel(run)).toBe('23m 39s');
    });

    it('should derive the wall clock card from the timestamps when the run recorded none', () => {
      // What an interrupted run looks like: cleanup leaves TotalDurationMs at zero, because the
      // outage between the crash and the restart is not run time.
      const run = buildRun({ status: 'Failed', totalDurationMs: 0 });

      expect(component.runWallClockLabel(run)).toBe('23m 39s');
    });

    it('should dash the wall clock card for a run with no completion timestamp', () => {
      const run = buildRun({ status: 'Failed', totalDurationMs: 0, completedAtUtc: null });

      expect(component.runWallClockLabel(run)).toBe('—');
    });

    it('should leave both card notes unchanged for a run that was never re-run', () => {
      const run = buildRun({ status: 'Canceled' });

      expect(component.runAnswerDurationNote(run)).toBe('sum over answers, tools included');
      expect(component.runWallClockNote(run)).toBe('start to finish, grading included');
    });

    it("should name the re-run's own span on the wall time note and flag re-executed answers on the answer duration note", () => {
      const run = buildRun({
        status: 'Canceled',
        rerunStartedAtUtc: '2026-09-09T10:00:00Z',
        rerunCompletedAtUtc: '2026-09-09T10:20:04Z'
      });

      expect(component.runAnswerDurationNote(run)).toBe('sum over answers, tools included · includes re-executed answers');
      expect(component.runWallClockNote(run)).toBe('start to finish, grading included · plus re-run 20m 4s');
    });

    it('should report the shortfall of a run that finished with errors', () => {
      const run = buildRun({ status: 'CompletedWithErrors', answeredQuestionCount: 16, totalQuestionCount: 18 });

      expect(component.answerShortfallOf(run)).toEqual({ answered: 16, total: 18 });
    });

    it('should report the shortfall of a cancelled run', () => {
      const run = buildRun({ status: 'Canceled', answeredQuestionCount: 3, totalQuestionCount: 18 });

      expect(component.answerShortfallOf(run)).toEqual({ answered: 3, total: 18 });
    });

    it('should report no shortfall while running, at a full answer set, or with no suite total', () => {
      expect(component.answerShortfallOf(buildRun({ status: 'Running', answeredQuestionCount: 3, totalQuestionCount: 18 }))).toBeNull();
      expect(component.answerShortfallOf(buildRun({ answeredQuestionCount: 18, totalQuestionCount: 18 }))).toBeNull();
      expect(component.answerShortfallOf(buildRun({ answeredQuestionCount: 0, totalQuestionCount: 0 }))).toBeNull();
    });

    it('should badge a changed run option as an option change, not as instrument drift', () => {
      // Runs 24 and 25: verboseMode flipped, so the candidate prompt hash moved with it.
      ctx.workspace.historyRuns = [
        buildRun({
          id: 25,
          candidateSystemPromptSha256: 'bb19dc24',
          candidatePromptOptionsJson: '{"verboseMode":true,"enableToolUse":true}'
        }),
        buildRun({
          id: 24,
          candidateSystemPromptSha256: 'e9b3e9a7',
          candidatePromptOptionsJson: '{"verboseMode":false,"enableToolUse":true}'
        })
      ];

      const change = ctx.workspace.instrumentChangeOf(ctx.workspace.historyRuns[0]);

      expect(change?.kind).toBe('options');
      expect(change?.comparedToRunId).toBe(24);
      expect(change?.description).toContain('verboseMode');
    });

    it('should badge a moved hash as instrument drift when the options match', () => {
      ctx.workspace.historyRuns = [
        buildRun({ id: 25, knowledgeBaseHeadSha: 'kb-b' }),
        buildRun({ id: 24 })
      ];

      const change = ctx.workspace.instrumentChangeOf(ctx.workspace.historyRuns[0]);

      expect(change?.kind).toBe('instrument');
      expect(change?.comparedToRunId).toBe(24);
      expect(change?.description).toContain('knowledge base');
    });

    it('should fall back to the hash comparison when the options cannot be parsed', () => {
      ctx.workspace.historyRuns = [
        buildRun({ id: 25, knowledgeBaseHeadSha: 'kb-b', candidatePromptOptionsJson: 'not json' }),
        buildRun({ id: 24 })
      ];

      expect(ctx.workspace.instrumentChangeOf(ctx.workspace.historyRuns[0])?.kind).toBe('instrument');
    });

    it('should badge a moved GnollHack wiki HEAD as instrument drift', () => {
      ctx.workspace.historyRuns = [
        buildRun({ id: 25, wikiHeadSha: 'wiki-b' }),
        buildRun({ id: 24 })
      ];

      const change = ctx.workspace.instrumentChangeOf(ctx.workspace.historyRuns[0]);

      expect(change?.kind).toBe('instrument');
      expect(change?.description).toContain('GnollHack wiki');
    });

    it('should badge a moved GnollHack source HEAD as instrument drift', () => {
      ctx.workspace.historyRuns = [
        buildRun({ id: 25, sourceCodeHeadSha: 'src-b' }),
        buildRun({ id: 24 })
      ];

      const change = ctx.workspace.instrumentChangeOf(ctx.workspace.historyRuns[0]);

      expect(change?.kind).toBe('instrument');
      expect(change?.description).toContain('GnollHack source');
    });

    it('should report no drift when a corpus HEAD is recorded on only one of the two runs', () => {
      // "Not recorded" on either side is not "unchanged", so neither direction may be badged.
      ctx.workspace.historyRuns = [
        buildRun({ id: 25, wikiHeadSha: null, sourceCodeHeadSha: 'src-a' }),
        buildRun({ id: 24, wikiHeadSha: 'wiki-a', sourceCodeHeadSha: null })
      ];

      expect(ctx.workspace.instrumentChangeOf(ctx.workspace.historyRuns[0])).toBeNull();

      ctx.workspace.historyRuns = [
        buildRun({ id: 27, wikiHeadSha: 'wiki-b', sourceCodeHeadSha: 'src-b' }),
        buildRun({ id: 26, wikiHeadSha: null, sourceCodeHeadSha: null })
      ];

      expect(ctx.workspace.instrumentChangeOf(ctx.workspace.historyRuns[0])).toBeNull();
    });

    it('should badge nothing when both the options and all five hashes match', () => {
      ctx.workspace.historyRuns = [buildRun({ id: 25 }), buildRun({ id: 24 })];

      expect(ctx.workspace.instrumentChangeOf(ctx.workspace.historyRuns[0])).toBeNull();
    });

    it('should sort the Duration column by the figure each cell shows', () => {
      // The Run History tab is rendered first; its ngOnInit loads the history this test replaces.
      component.selectSubTab('history');
      fixture.detectChanges();
      ctx.workspace.historyRuns = [
        // The cancelled run's answers took the longest, but only 100s of wall clock elapsed.
        buildRun({ id: 3, status: 'Canceled', totalAnswerDurationMs: 900000, totalDurationMs: 100000 }),
        buildRun({ id: 2, totalAnswerDurationMs: 500000, totalDurationMs: 700000 }),
        buildRun({ id: 1, totalAnswerDurationMs: 300000, totalDurationMs: 300000 })
      ];

      ctx.workspace.historyTable.toggleSort('durationMs');

      expect(ctx.historyTab().historyView.map(r => r.id)).toEqual([2, 1, 3]);
    });
  });
});
