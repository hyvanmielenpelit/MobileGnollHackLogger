import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { Subject, of } from 'rxjs';

import {
  MULTI_RUN_DISMISSED_STORAGE_KEY,
  MULTI_RUN_GROUPS_VIEW_STORAGE_KEY,
  MULTI_RUN_VIEW_STORAGE_KEY,
  MultiRunComponent,
  suggestionKey
} from './multi-run.component';
import { GroupAnalysisDialogComponent } from './group-analysis-dialog/group-analysis-dialog.component';
import { GroupBuilderDialogComponent } from './group-builder-dialog/group-builder-dialog.component';
import {
  AdminBenchmarkService,
  BenchmarkRunGroupDto,
  BenchmarkRunGroupSuggestionDto,
  BenchmarkSuiteDto
} from '../../../services/admin-benchmark.service';

/**
 * The URLs the page reaches the server by. Tested against the real service, because a wrong path
 * would fail silently: the report opens as a 404 in a new tab, and a suggestion list never loads.
 */
describe('AdminBenchmarkService run group URLs', () => {
  it('should target /api/admin/benchmark/runs/groups/{id}/report', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });

    const service = TestBed.inject(AdminBenchmarkService);

    expect(service.getGroupReportUrl(42)).toBe('/api/admin/benchmark/runs/groups/42/report');
  });

  it('should ask for a suite\'s suggestions at /api/admin/benchmark/runs/groups/suggestions', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });

    const service = TestBed.inject(AdminBenchmarkService);
    const http = TestBed.inject(HttpTestingController);
    service.getRunGroupSuggestions(5).subscribe();

    const request = http.expectOne(r => r.url === '/api/admin/benchmark/runs/groups/suggestions');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('suiteId')).toBe('5');
    request.flush([]);
    http.verify();
  });
});

describe('MultiRunComponent', () => {
  let component: MultiRunComponent;
  let fixture: ComponentFixture<MultiRunComponent>;
  let serviceMock: MockedObject<AdminBenchmarkService>;

  const SUITES = [
    { id: 5, name: 'GnollHack Player Assistance Benchmark Suite' },
    { id: 6, name: 'Two initial suites' }
  ] as BenchmarkSuiteDto[];

  function buildGroup(overrides: Partial<BenchmarkRunGroupDto> = {}): BenchmarkRunGroupDto {
    return {
      id: 7,
      name: 'GnollHack Player Assistance Benchmark Suite · GPT-5.6 Luna · 2026-09-06 · R=3',
      benchmarkSuiteId: 5,
      suiteName: 'GnollHack Player Assistance Benchmark Suite',
      testedModelDisplayName: 'GPT-5.6 Luna',
      testedModelProvider: 'OpenAI',
      testedModelId: 'gpt-5.6-luna',
      testedModelThinkingLevel: 'high',
      testedModelReasoningMode: null,
      tier: 'Replicate',
      tierLabel: 'Tier A — Replicate',
      comparabilityKeyHash: 'a1b2c3d4e5f6',
      comparabilityKeyStale: false,
      crossCondition: false,
      notes: null,
      createdFromSeriesId: 3,
      createdAtUtc: '2026-09-06T10:00:00Z',
      modifiedAtUtc: '2026-09-06T10:00:00Z',
      runCount: 3,
      members: [],
      memberRunIds: [41, 42, 43],
      latestAnalysisId: 11,
      latestAnalysisAtUtc: '2026-09-06T11:00:00Z',
      analysisStale: false,
      headlineIndex: 71.34,
      headlineHalfWidth: 1.24,
      headlineSpeedIndex: 55,
      headlineCostPerRun: 0.1234,
      nameIsGenerated: true,
      ...overrides
    } as BenchmarkRunGroupDto;
  }

  function buildSuggestion(overrides: Partial<BenchmarkRunGroupSuggestionDto> = {}): BenchmarkRunGroupSuggestionDto {
    return {
      keyHash: 'k1',
      runIds: [51, 52, 53],
      runCount: 3,
      tier: 'Replicate',
      testedModel: { displayName: 'Claude Luna', id: 'claude-luna', provider: 'Anthropic', thinkingLevel: 'max', reasoningMode: null },
      firstStartedAtUtc: '2026-10-01T08:00:00Z',
      lastStartedAtUtc: '2026-10-01T09:30:00Z',
      seriesIds: [9],
      meanQualityIndex: 68.25,
      ...overrides
    };
  }

  function clearStorage(): void {
    try {
      localStorage.removeItem(MULTI_RUN_VIEW_STORAGE_KEY);
      localStorage.removeItem(MULTI_RUN_DISMISSED_STORAGE_KEY);
      localStorage.removeItem(MULTI_RUN_GROUPS_VIEW_STORAGE_KEY);
    } catch {
      // Storage unavailable: nothing to clear.
    }
  }

  function create(inputs: { suites?: BenchmarkSuiteDto[]; defaultSuiteId?: number | null } = {}): void {
    fixture = TestBed.createComponent(MultiRunComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('suites', inputs.suites ?? SUITES);
    fixture.componentRef.setInput('defaultSuiteId', inputs.defaultSuiteId === undefined ? 5 : inputs.defaultSuiteId);
    fixture.detectChanges();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function q<T extends HTMLElement = HTMLElement>(selector: string): T {
    const found = el().querySelector<T>(selector);
    expect(found, selector).not.toBeNull();
    return found!;
  }

  function text(selector: string): string {
    const element = el().querySelector(selector) as HTMLElement | null;
    return (element?.textContent || '').replace(/\s+/g, ' ').trim();
  }

  function click(selector: string): void {
    q(selector).click();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    clearStorage();
    serviceMock = {
      getRunGroups: vi.fn().mockName("AdminBenchmarkService.getRunGroups"),
      getRunGroup: vi.fn().mockName("AdminBenchmarkService.getRunGroup"),
      getRunGroupSuggestions: vi.fn().mockName("AdminBenchmarkService.getRunGroupSuggestions"),
      createRunGroup: vi.fn().mockName("AdminBenchmarkService.createRunGroup"),
      deleteRunGroup: vi.fn().mockName("AdminBenchmarkService.deleteRunGroup"),
      getGroupReportUrl: vi.fn().mockName("AdminBenchmarkService.getGroupReportUrl"),
      // The hosted dialogs' reads, in case they load anything while the page renders.
      getRuns: vi.fn().mockName("AdminBenchmarkService.getRuns"),
      previewRunGroupTier: vi.fn().mockName("AdminBenchmarkService.previewRunGroupTier"),
      getRunGroupAnalysis: vi.fn().mockName("AdminBenchmarkService.getRunGroupAnalysis"),
      analyseRunGroup: vi.fn().mockName("AdminBenchmarkService.analyseRunGroup")
    } as unknown as MockedObject<AdminBenchmarkService>;

    serviceMock.getRunGroups.mockReturnValue(of([buildGroup()]));
    serviceMock.getRunGroup.mockReturnValue(of(buildGroup()));
    serviceMock.getRunGroupSuggestions.mockReturnValue(of([]));
    serviceMock.deleteRunGroup.mockReturnValue(of(void 0));
    serviceMock.getGroupReportUrl.mockImplementation((id: number) => `/api/admin/benchmark/runs/groups/${id}/report`);
    serviceMock.getRuns.mockReturnValue(of([]));
    serviceMock.previewRunGroupTier.mockReturnValue(of({ accepted: true, comparability: null, group: null }));
    serviceMock.getRunGroupAnalysis.mockReturnValue(of(null));

    vi.spyOn(GroupAnalysisDialogComponent.prototype, 'open').mockImplementation(() => undefined);
    vi.spyOn(GroupAnalysisDialogComponent.prototype, 'close').mockImplementation(() => undefined);
    vi.spyOn(GroupBuilderDialogComponent.prototype, 'open').mockImplementation(() => undefined);
    vi.spyOn(GroupBuilderDialogComponent.prototype, 'close').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [MultiRunComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: serviceMock }]
    }).compileComponents();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    clearStorage();
  });

  // --- The header ---

  describe('the header', () => {
    it('names the page and holds the suite select, About, New group and Refresh', () => {
      create();

      const header = q('.tab-header-row.mr-header');
      expect(text('.mr-header h3')).toBe('Multi-Run Analysis');

      const group = q('.mr-suite');
      expect(group.getAttribute('role')).toBe('group');
      expect(group.getAttribute('aria-label')).toBe('Suite');
      const label = q('label[for="mr-suite-select"]');
      expect(label.classList.contains('visually-hidden')).toBe(true);
      const select = q<HTMLSelectElement>('#mr-suite-select');
      expect(select.classList.contains('gh-input')).toBe(true);
      expect(Array.from(select.options).map(o => o.textContent?.trim())).toEqual(SUITES.map(s => s.name));

      expect(header.querySelector('app-info-tip button')?.getAttribute('aria-label')).toBe('About multi-run analysis');

      const newGroup = q('.mr-new-group');
      expect(newGroup.classList.contains('btn-ghost')).toBe(true);
      expect(newGroup.getAttribute('aria-haspopup')).toBe('dialog');
      expect(newGroup.textContent?.trim()).toBe('New group');
      expect(newGroup.querySelector('svg.btn-icon')?.getAttribute('aria-hidden')).toBe('true');

      const refresh = q('#mr-refresh');
      expect(refresh.classList.contains('action-btn')).toBe(true);
      expect(refresh.getAttribute('aria-label')).toBe('Refresh multi-run analysis');
      expect(refresh.getAttribute('interestfor')).toBe('mr-refresh-tip');
      expect(text('#mr-refresh-tip')).toBe('Refresh');
      expect(refresh.hasAttribute('aria-disabled')).toBe(false);
      expect(header.querySelector('[title]')).toBeNull();
    });

    it('marks Refresh aria-disabled with a spinner while a load runs, and refuses a second load', () => {
      const pending = new Subject<BenchmarkRunGroupDto[]>();
      serviceMock.getRunGroups.mockReturnValue(pending);
      create();

      const refresh = q('#mr-refresh');
      expect(refresh.getAttribute('aria-disabled')).toBe('true');
      expect(refresh.querySelector('.gh-spinner-small')).not.toBeNull();

      refresh.click();
      fixture.detectChanges();
      expect(serviceMock.getRunGroups).toHaveBeenCalledTimes(1);

      pending.next([buildGroup()]);
      pending.complete();
      fixture.detectChanges();
      expect(refresh.hasAttribute('aria-disabled')).toBe(false);
      expect(refresh.querySelector('.gh-spinner-small')).toBeNull();

      serviceMock.getRunGroups.mockReturnValue(of([buildGroup()]));
      refresh.click();
      fixture.detectChanges();
      expect(serviceMock.getRunGroups).toHaveBeenCalledTimes(2);
      expect(serviceMock.getRunGroupSuggestions).toHaveBeenCalledTimes(2);
    });

    it('opens the builder from New group', () => {
      create();
      click('.mr-new-group');
      expect(GroupBuilderDialogComponent.prototype.open).toHaveBeenCalledTimes(1);
    });
  });

  // --- The suite ---

  describe('the suite', () => {
    it('falls back to the launcher\'s suite when this browser remembers none', () => {
      create({ defaultSuiteId: 5 });
      expect(component.selectedSuiteId).toBe(5);
      expect(serviceMock.getRunGroupSuggestions).toHaveBeenCalledWith(5);
      expect(q<HTMLSelectElement>('#mr-suite-select').value).toBe('5');
    });

    it('uses the remembered suite over the launcher\'s', () => {
      localStorage.setItem(MULTI_RUN_VIEW_STORAGE_KEY, JSON.stringify({ version: 1, suiteId: 6 }));
      create({ defaultSuiteId: 5 });
      expect(component.selectedSuiteId).toBe(6);
      expect(serviceMock.getRunGroupSuggestions).toHaveBeenCalledWith(6);
      expect(q<HTMLSelectElement>('#mr-suite-select').value).toBe('6');
    });

    it('skips a remembered suite that no longer exists, then an unknown launcher suite, for the first suite', () => {
      localStorage.setItem(MULTI_RUN_VIEW_STORAGE_KEY, JSON.stringify({ version: 1, suiteId: 99 }));
      create({ defaultSuiteId: 77, suites: [SUITES[1], SUITES[0]] });
      expect(component.selectedSuiteId).toBe(6);
    });

    it('remembers a chosen suite and loads its groups and suggestions', () => {
      create();
      const select = q<HTMLSelectElement>('#mr-suite-select');
      select.value = '6';
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();

      expect(component.selectedSuiteId).toBe(6);
      expect(JSON.parse(localStorage.getItem(MULTI_RUN_VIEW_STORAGE_KEY)!)).toEqual({ version: 1, suiteId: 6 });
      expect(serviceMock.getRunGroupSuggestions).toHaveBeenLastCalledWith(6);
      // Group 7 belongs to suite 5.
      expect(el().querySelector('article.mr-group')).toBeNull();
    });
  });

  // --- Suggested groups ---

  describe('suggested groups', () => {
    it('shows no section while there are no suggestions', () => {
      create();
      expect(el().querySelector('.mr-suggestions')).toBeNull();
    });

    it('renders a suggestion with its model, runs and meta line', () => {
      serviceMock.getRunGroupSuggestions.mockReturnValue(of([buildSuggestion()]));
      create();

      expect(text('#mr-suggestions-title')).toBe('Suggested groups');
      expect(q('#mr-suggestions-title').classList.contains('gh-section-title')).toBe(true);
      expect(q('.mr-suggestions .mr-count-pill').getAttribute('aria-hidden')).toBe('true');
      expect(q('.mr-suggestion-list').getAttribute('role')).toBe('list');

      const card = q('article.mr-suggestion');
      expect(card.getAttribute('aria-labelledby')).toBe('mr-sugg-51-52-53-title');
      expect(card.querySelector('app-model-identity .model-identity-name')?.textContent?.trim()).toBe('Claude Luna');

      const chips = Array.from(card.querySelectorAll<HTMLButtonElement>('.mr-run-chip'));
      expect(chips.map(c => c.textContent?.trim())).toEqual(['#51', '#52', '#53']);
      expect(chips[0].getAttribute('aria-label')).toBe('Open run report #51');
      expect(chips[0].getAttribute('interestfor')).toBe('mr-sugg-51-52-53-run-51-tip');

      const meta = (card.querySelector('.mr-card-meta')?.textContent || '').replace(/\s+/g, ' ');
      expect(meta).toContain('Tier A — Replicate');
      expect(meta).toContain('R = 3');
      expect(meta).toContain('Series 9');
      expect(meta).toContain('Mean index 68.3');
      expect(card.querySelectorAll('.mr-card-meta time').length).toBe(2);

      expect(card.querySelector('.mr-card-actions')?.getAttribute('aria-label'))
        .toBe('Actions for the suggested group of runs #51, #52, #53');
      expect(text('.mr-create-group')).toBe('Create group');
      expect(q('.mr-dismiss-suggestion').getAttribute('aria-label')).toBe('Dismiss suggestion of runs #51, #52, #53');
    });

    it('leaves the series out when the runs came from several', () => {
      serviceMock.getRunGroupSuggestions.mockReturnValue(of([buildSuggestion({ seriesIds: [9, 10] })]));
      create();
      expect(text('article.mr-suggestion .mr-card-meta')).not.toContain('Series');
    });

    it('emits openRun from a run chip', () => {
      serviceMock.getRunGroupSuggestions.mockReturnValue(of([buildSuggestion()]));
      create();
      const emitted: number[] = [];
      component.openRun.subscribe(id => emitted.push(id));

      (el().querySelectorAll<HTMLButtonElement>('article.mr-suggestion .mr-run-chip')[1]).click();
      expect(emitted).toEqual([52]);
    });

    it('creates the group without a name, focuses its new card and announces the analysis', () => {
      serviceMock.getRunGroupSuggestions.mockReturnValue(of([buildSuggestion()]));
      create();

      const created = buildGroup({ id: 12, memberRunIds: [51, 52, 53], createdAtUtc: '2026-10-02T10:00:00Z', latestAnalysisId: 30 });
      serviceMock.createRunGroup.mockReturnValue(of({ accepted: true, group: created }));
      serviceMock.getRunGroups.mockReturnValue(of([buildGroup(), created]));
      serviceMock.getRunGroupSuggestions.mockReturnValue(of([]));

      click('.mr-create-group');

      expect(serviceMock.createRunGroup).toHaveBeenCalledTimes(1);
      const request = serviceMock.createRunGroup.mock.calls[0][0];
      expect(request).toEqual({ runIds: [51, 52, 53] });
      expect('name' in request).toBe(false);

      expect(el().querySelector('.mr-suggestions')).toBeNull();
      expect(document.activeElement?.id).toBe('mr-group-12-title');
      expect(text('#mr-groups-status')).toContain('Created group #12 and analyzed it.');
    });

    it('announces a created group the server did not analyze', () => {
      serviceMock.getRunGroupSuggestions.mockReturnValue(of([buildSuggestion()]));
      create();
      const created = buildGroup({ id: 12, latestAnalysisId: null });
      serviceMock.createRunGroup.mockReturnValue(of({ accepted: true, group: created }));
      serviceMock.getRunGroups.mockReturnValue(of([created]));

      click('.mr-create-group');
      expect(text('#mr-groups-status')).toContain('Created group #12.');
      expect(text('#mr-groups-status')).not.toContain('analyzed');
    });

    it('shows a refusal on the suggestion and keeps it', () => {
      serviceMock.getRunGroupSuggestions.mockReturnValue(of([buildSuggestion()]));
      create();
      serviceMock.createRunGroup.mockReturnValue(of({ accepted: false, error: 'Group #4 already holds these runs.', duplicateOfGroupId: 4 }));

      click('.mr-create-group');
      expect(text('article.mr-suggestion .mr-card-error')).toBe('Group #4 already holds these runs.');
      expect(el().querySelector('article.mr-suggestion')).not.toBeNull();
    });

    it('persists a dismissal, and shows the cluster again once it gains a run', () => {
      serviceMock.getRunGroupSuggestions.mockReturnValue(of([buildSuggestion()]));
      create();

      click('.mr-dismiss-suggestion');
      expect(el().querySelector('article.mr-suggestion')).toBeNull();
      expect(JSON.parse(localStorage.getItem(MULTI_RUN_DISMISSED_STORAGE_KEY)!))
        .toEqual({ version: 1, keys: [suggestionKey(buildSuggestion())] });
      expect(document.activeElement?.id).toBe('mr-groups-title');

      fixture.destroy();
      create();
      expect(el().querySelector('article.mr-suggestion')).toBeNull();

      fixture.destroy();
      serviceMock.getRunGroupSuggestions.mockReturnValue(of([buildSuggestion({ runIds: [51, 52, 53, 54], runCount: 4 })]));
      create();
      expect(el().querySelector('article.mr-suggestion')).not.toBeNull();
    });
  });

  // --- Group cards ---

  describe('group cards', () => {
    it('renders the kicker, the title, the runs row and the metrics', () => {
      create();

      const card = q('article.mr-group');
      expect(card.getAttribute('aria-labelledby')).toBe('mr-group-7-title');
      expect(q('#mr-groups-title').classList.contains('gh-section-title')).toBe(true);

      const kicker = (card.querySelector('.mr-group-kicker')?.textContent || '').replace(/\s+/g, ' ');
      expect(kicker).toContain('#7');
      expect(kicker).toContain('Tier A — Replicate');
      expect(kicker).toContain('Current');
      expect(kicker).toContain('Series 3');
      expect(kicker).not.toContain('Definition changed');

      const title = q('#mr-group-7-title');
      expect(title.tagName).toBe('H5');
      expect(title.getAttribute('tabindex')).toBe('-1');
      expect(title.querySelector('app-model-identity .model-identity-name')?.textContent?.trim()).toBe('GPT-5.6 Luna');
      expect(card.querySelector('.mr-card-meta time')).not.toBeNull();

      const chips = Array.from(card.querySelectorAll('.mr-run-chip')).map(c => c.textContent?.trim());
      expect(chips).toEqual(['#41', '#42', '#43']);

      // The value and its qualifier are separate lines of the dd.
      const metric = (name: string) => Array.from(card.querySelector(`[data-metric="${name}"] dd`)?.children ?? [])
        .map(line => (line.textContent || '').replace(/\s+/g, ' ').trim())
        .join(' ');
      expect(metric('intelligence')).toBe('71.3 ± 1.2');
      expect(metric('speed')).toBe('55.0');
      expect(metric('cost')).toBe('$0.1234');
      expect(metric('runs')).toBe('3');
      expect(Array.from(card.querySelectorAll('.mr-metrics dt')).map(d => d.textContent?.trim()))
        .toEqual(['Intelligence', 'Speed', 'Cost / run', 'Runs']);

      expect(card.querySelector('.mr-card-actions')?.getAttribute('aria-label')).toBe('Actions for group #7');
      expect(q('#mr-group-7-open').getAttribute('aria-label')).toBe('Open analysis of group #7');
      expect(card.querySelector('.mr-download')?.getAttribute('aria-label')).toBe('Download the Markdown report of group #7');
      expect(q('#mr-group-7-delete').getAttribute('aria-label')).toBe('Delete group #7');
      expect(q('.mr-groups-section').querySelector('[title]')).toBeNull();
    });

    it('names a stale analysis with a tooltip, and marks a changed definition', () => {
      serviceMock.getRunGroups.mockReturnValue(of([buildGroup({ analysisStale: true, comparabilityKeyStale: true })]));
      create();

      const stale = q('.mr-state-stale');
      expect(stale.tagName).toBe('BUTTON');
      expect(stale.textContent?.trim()).toBe('Stale');
      expect(text('#' + stale.getAttribute('interestfor'))).toContain('a member was re-run');
      expect(text('.mr-definition-changed')).toBe('Definition changed');
    });

    it('says Not analyzed, and keeps the download focusable but unavailable', () => {
      serviceMock.getRunGroups.mockReturnValue(of([buildGroup({
        latestAnalysisId: null, latestAnalysisAtUtc: null, headlineIndex: null, headlineHalfWidth: null,
        headlineSpeedIndex: null, headlineCostPerRun: null
      })]));
      create();
      const openSpy = vi.spyOn(window, 'open').mockReturnValue(null);

      expect(text('.mr-group-kicker .mr-state')).toBe('Not analyzed');
      expect(text('[data-metric="intelligence"] dd')).toBe('Not analyzed');
      const download = q('.mr-download');
      expect(download.getAttribute('aria-disabled')).toBe('true');
      download.click();
      expect(openSpy).not.toHaveBeenCalled();
    });

    it('asks for a re-analysis when an analysis exists without headline figures', () => {
      serviceMock.getRunGroups.mockReturnValue(of([buildGroup({ headlineIndex: null, headlineHalfWidth: null })]));
      create();
      expect(text('[data-metric="intelligence"] dd')).toContain('Re-analyze to show figures');
    });

    it('downloads the report through window.open once an analysis exists', () => {
      create();
      const openSpy = vi.spyOn(window, 'open').mockReturnValue(null);
      click('.mr-download');
      expect(openSpy).toHaveBeenCalledWith('/api/admin/benchmark/runs/groups/7/report', '_blank');
    });

    it('shows a typed name as a caption only when the server did not generate it', () => {
      serviceMock.getRunGroups.mockReturnValue(of([
        buildGroup({ id: 7, name: 'Luna baseline after the prompt fix', nameIsGenerated: false }),
        buildGroup({ id: 8, name: 'Something the server chose', nameIsGenerated: true })
      ]));
      create();

      expect(text('[data-group-id="7"] .mr-custom-name')).toBe('“Luna baseline after the prompt fix”');
      expect(el().querySelector('[data-group-id="8"] .mr-custom-name')).toBeNull();
    });

    it('emits the originating series from Series N', () => {
      create();
      const emitted: number[] = [];
      component.openSeries.subscribe(id => emitted.push(id));
      click('.mr-badge-series');
      expect(emitted).toEqual([3]);
    });

    it('emits openRun from a group\'s run chip', () => {
      create();
      const emitted: number[] = [];
      component.openRun.subscribe(id => emitted.push(id));
      (el().querySelectorAll<HTMLButtonElement>('article.mr-group .mr-run-chip')[2]).click();
      expect(emitted).toEqual([43]);
    });

    it('opens the analysis dialog from the eye button', () => {
      create();
      click('#mr-group-7-open');
      expect(GroupAnalysisDialogComponent.prototype.open).toHaveBeenCalledWith(expect.objectContaining({ id: 7 }));
    });
  });

  // --- Delete ---

  describe('deleting a group', () => {
    it('confirms inline, and Keep returns focus to the trash button', () => {
      create();
      click('#mr-group-7-delete');

      expect(text('.mr-confirm span')).toBe('Delete group #7? Its runs are kept.');
      expect(q('.mr-confirm-delete').classList.contains('btn-ghost-danger')).toBe(true);
      expect(document.activeElement?.id).toBe('mr-group-7-keep');
      expect(serviceMock.deleteRunGroup).not.toHaveBeenCalled();

      click('#mr-group-7-keep');
      expect(el().querySelector('.mr-confirm')).toBeNull();
      expect(document.activeElement?.id).toBe('mr-group-7-delete');
    });

    it('deletes on Delete and focuses the next card, else the heading', () => {
      serviceMock.getRunGroups.mockReturnValue(of([
        buildGroup({ id: 8, createdAtUtc: '2026-09-07T10:00:00Z' }),
        buildGroup({ id: 7 })
      ]));
      create();

      click('#mr-group-8-delete');
      click('#mr-group-8-confirm-delete');
      expect(serviceMock.deleteRunGroup).toHaveBeenCalledWith(8);
      expect(el().querySelector('[data-group-id="8"]')).toBeNull();
      expect(document.activeElement?.id).toBe('mr-group-7-title');
      expect(text('#mr-groups-status')).toContain('Deleted group #8.');

      click('#mr-group-7-delete');
      click('#mr-group-7-confirm-delete');
      expect(document.activeElement?.id).toBe('mr-groups-title');
    });
  });

  // --- Empty states and the filter bar ---

  describe('empty states and the filter bar', () => {
    it('shows one dashed card with New group when the suite has no groups', () => {
      serviceMock.getRunGroups.mockReturnValue(of([]));
      create();

      expect(text('.mr-empty-groups .mr-empty-title')).toBe('No analysis groups for this suite yet');
      expect(text('.mr-empty-groups .mr-empty-text')).toBe('A completed series or battery creates and analyzes one automatically.');
      click('.mr-empty-new-group');
      expect(GroupBuilderDialogComponent.prototype.open).toHaveBeenCalledTimes(1);
    });

    it('shows the filter bar only above three groups', () => {
      serviceMock.getRunGroups.mockReturnValue(of([1, 2, 3].map(id => buildGroup({ id }))));
      create();
      expect(el().querySelector('.mr-filter-bar')).toBeNull();

      fixture.destroy();
      serviceMock.getRunGroups.mockReturnValue(of([1, 2, 3, 4].map(id => buildGroup({ id }))));
      create();
      expect(el().querySelector('.mr-filter-bar')).not.toBeNull();
      expect(q('#mr-groups-search').getAttribute('type')).toBe('search');
      expect(Array.from(q<HTMLSelectElement>('#mr-groups-sort').options).map(o => o.textContent?.trim()))
        .toEqual(['Newest first', 'Oldest first', 'Most runs', 'Highest index', 'Tier (A first)', 'Model (A–Z)']);
    });

    it('says no groups match, and Clear all filters brings them back', () => {
      serviceMock.getRunGroups.mockReturnValue(of([1, 2, 3, 4].map(id => buildGroup({ id }))));
      create();

      component.onFacetChange('tier', ['Tier C — Cross-condition']);
      fixture.detectChanges();
      expect(text('.mr-empty-filters .mr-empty-title')).toBe('No groups match these filters.');
      expect(el().querySelector('article.mr-group')).toBeNull();

      click('.mr-empty-clear');
      expect(el().querySelectorAll('article.mr-group').length).toBe(4);
    });
  });

  // --- Dialog hand-offs ---

  describe('openGroupById', () => {
    it('opens a listed group without fetching it', () => {
      create();
      component.openGroupById(7);
      expect(serviceMock.getRunGroup).not.toHaveBeenCalled();
      expect(GroupAnalysisDialogComponent.prototype.open).toHaveBeenCalledWith(expect.objectContaining({ id: 7 }));
    });

    it('fetches and opens a group absent from the list', () => {
      create();
      serviceMock.getRunGroup.mockReturnValue(of(buildGroup({ id: 99, benchmarkSuiteId: 6 })));
      component.openGroupById(99);
      expect(serviceMock.getRunGroup).toHaveBeenCalledWith(99);
      expect(GroupAnalysisDialogComponent.prototype.open).toHaveBeenCalledWith(expect.objectContaining({ id: 99 }));
    });
  });

  describe('the builder', () => {
    it('reloads, focuses and announces a group it created', () => {
      create();
      const created = buildGroup({ id: 20, createdAtUtc: '2026-10-03T10:00:00Z', latestAnalysisId: 40 });
      serviceMock.getRunGroups.mockReturnValue(of([buildGroup(), created]));

      const builder = fixture.debugElement.query(By.directive(GroupBuilderDialogComponent)).componentInstance as GroupBuilderDialogComponent;
      builder.created.emit(created);
      fixture.detectChanges();

      expect(document.activeElement?.id).toBe('mr-group-20-title');
      expect(text('#mr-groups-status')).toContain('Created group #20 and analyzed it.');
    });

    it('closes the builder and opens the analysis of a duplicate it names', () => {
      create();
      const builder = fixture.debugElement.query(By.directive(GroupBuilderDialogComponent)).componentInstance as GroupBuilderDialogComponent;
      builder.openGroup.emit(7);

      expect(GroupBuilderDialogComponent.prototype.close).toHaveBeenCalled();
      expect(GroupAnalysisDialogComponent.prototype.open).toHaveBeenCalledWith(expect.objectContaining({ id: 7 }));
    });
  });
});
