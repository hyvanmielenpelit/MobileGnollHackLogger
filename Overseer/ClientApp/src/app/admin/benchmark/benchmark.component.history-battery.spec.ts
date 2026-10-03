import type { Mock, MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import {
  AdminBenchmarkService,
  BenchmarkBatteryMemberDto,
  BenchmarkBatteryRunDto,
  BenchmarkRunSummaryDto
} from '../../services/admin-benchmark.service';
import { BATTERY_RUN_HISTORY_LIMIT, RUN_HISTORY_MEMBERS_STORAGE_KEY } from './benchmark.models';
import {
  AdminBenchmarkSpecContext, benchmarkSpecHandles, buildBatteryRun, clearStoredState, createAdminBenchmarkFixture
} from './benchmark.component.testing';

// Run History's battery run cards: their place among the run cards, the member-run toggle, the Kind
// facet, the search, the actions and the delete confirmation.

/** Clears the stored member-run toggle as well as the state every Benchmark spec clears. */
function clearHistoryState(): void {
  clearStoredState();
  try {
    localStorage.removeItem(RUN_HISTORY_MEMBERS_STORAGE_KEY);
  } catch { /* private-browsing modes throw */ }
}

function buildHistoryRun(overrides: Partial<BenchmarkRunSummaryDto> = {}): BenchmarkRunSummaryDto {
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
  } as BenchmarkRunSummaryDto;
}

function member(runId: number, suiteIndex: number, overrides: Partial<BenchmarkBatteryMemberDto> = {}): BenchmarkBatteryMemberDto {
  return {
    memberId: runId, suiteIndex, round: 1, runId, runStatus: 'Completed', qualityIndex: 70, speedIndex: 60,
    origin: 'Launched', superseded: false, usable: true, unusableReason: null, guardFailure: null,
    addedAtUtc: '2026-09-02T00:00:00Z', runStartedAtUtc: null, runCompletedAtUtc: null,
    answeredQuestionCount: 5, totalQuestionCount: 5,
    ...overrides
  };
}

/** Battery run 9: finished, two suites of one round, started on 2 September, not analyzed. */
function finishedBattery(overrides: Partial<BenchmarkBatteryRunDto> = {}): BenchmarkBatteryRunDto {
  return buildBatteryRun({
    status: 'Completed', isDriving: false, resumable: false,
    startedAtUtc: '2026-09-02T00:00:00Z', completedAtUtc: '2026-09-02T01:00:00Z',
    suites: [
      { index: 0, suiteId: 1, suiteName: 'Default Suite' },
      { index: 1, suiteId: 2, suiteName: 'Second Suite' }
    ],
    completedSuiteCount: 2, completedMemberCount: 2, currentSuitePosition: null, currentSuiteName: null, currentRound: null,
    testedModelLabel: 'Model A', testedProvider: 'Anthropic', testedModelId: 'model-a', assessorLabel: 'Model B',
    members: [member(41, 0), member(42, 1)],
    ...overrides
  });
}

describe('AdminBenchmarkComponent: Run History battery runs', () => {
  let ctx: AdminBenchmarkSpecContext;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: MockedObject<AdminBenchmarkService>;
  let deleteBatteryRun: Mock;

  beforeEach(clearHistoryState);

  afterEach(() => {
    for (const dialog of Array.from(document.querySelectorAll('dialog[open]')) as HTMLDialogElement[]) {
      dialog.close();
    }
    vi.useRealTimers();
    clearHistoryState();
  });

  beforeEach(async () => {
    ctx = await createAdminBenchmarkFixture();
    ({ fixture, benchmarkServiceMock } = ctx);
    deleteBatteryRun = vi.fn().mockName('AdminBenchmarkService.deleteBatteryRun').mockReturnValue(of(undefined));
    (benchmarkServiceMock as unknown as { deleteBatteryRun: Mock }).deleteBatteryRun = deleteBatteryRun;
  });

  /** Enters Run History through its real tab, with the server returning these runs and battery runs. */
  function openHistoryWith(runs: BenchmarkRunSummaryDto[], batteries: BenchmarkBatteryRunDto[]): void {
    benchmarkServiceMock.getRuns.mockReturnValue(of(runs));
    benchmarkServiceMock.getBatteryRuns.mockReturnValue(of(batteries));
    (fixture.nativeElement.querySelector('#bm-tab-history') as HTMLButtonElement).click();
    fixture.detectChanges();
  }

  /** The cards shown, in order: `run:N` or `battery:N`. */
  function shownKeys(): string[] {
    return (Array.from(fixture.nativeElement.querySelectorAll('.rh-card-list article.rh-card')) as HTMLElement[])
      .map(card => card.hasAttribute('data-battery-run-id')
        ? `battery:${card.getAttribute('data-battery-run-id')}`
        : `run:${card.getAttribute('data-run-id')}`);
  }

  function batteryCard(id = 9): HTMLElement {
    return fixture.nativeElement.querySelector(`article.rh-card-battery[data-battery-run-id="${id}"]`) as HTMLElement;
  }

  function membersToggle(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('.rh-list-head .rh-show-members') as HTMLButtonElement;
  }

  function historyStatus(): string {
    return (fixture.nativeElement.querySelector('#rh-list-status')?.textContent || '').trim();
  }

  /** An element's text with its whitespace collapsed. */
  function text(element: Element | null): string {
    return (element?.textContent || '').replace(/\s+/g, ' ').trim();
  }

  it('should place a battery run card among the run cards by its start time', () => {
    openHistoryWith(
      [buildHistoryRun({ id: 3, startedAtUtc: '2026-09-03T00:00:00Z' }), buildHistoryRun({ id: 1, startedAtUtc: '2026-09-01T00:00:00Z' })],
      [finishedBattery()]
    );

    expect(shownKeys()).toEqual(['run:3', 'battery:9', 'run:1']);

    const card = batteryCard();
    expect(card.classList.contains('rh-card')).toBe(true);
    expect(card.getAttribute('aria-labelledby')).toBe('rh-battery-9-title');
    expect(card.querySelector('h5#rh-battery-9-title')?.getAttribute('tabindex')).toBe('-1');
    expect(text(card.querySelector('.rh-card-model'))).toBe('Model A');

    const kicker = card.querySelector('.rh-card-kicker') as HTMLElement;
    expect(text(kicker.querySelector('.rh-battery-run-badge'))).toBe('Battery run #9');
    expect(kicker.querySelector('.rh-battery-badge')).toBeNull();
    const status = kicker.querySelector('.status-badge') as HTMLElement;
    expect(status.classList.contains('badge-status-completed')).toBe(true);
    expect(text(status)).toBe('Completed');
    expect(text(kicker.querySelector('.rh-battery-suites'))).toBe('2 of 2 suites');
    expect(text(kicker.querySelector('.rh-battery-rounds'))).toBe('1 run per suite');
    expect(text(kicker.querySelector('.rh-battery-not-analyzed'))).toBe('Not analyzed');

    const actions = card.querySelector('.rh-card-actions[role="group"]') as HTMLElement;
    expect(actions.getAttribute('aria-label')).toBe('Actions for battery run 9');
    expect((Array.from(actions.querySelectorAll('button.action-btn')) as HTMLButtonElement[]).map(b => b.getAttribute('aria-label'))).toEqual([
      'View details for battery run 9',
      'Download Markdown report for battery run 9',
      'Delete battery run 9'
    ]);

    const pairs = Array.from(card.querySelectorAll('.rh-instrument-strip dl.rh-instrument > div')) as HTMLElement[];
    expect(pairs.map(pair => pair.querySelector('dt .visually-hidden')?.textContent?.trim())).toEqual([
      '(battery definition)', '(comparability class)'
    ]);
    expect(pairs.map(pair => pair.querySelector('dd')?.textContent?.trim())).toEqual(['def-abc', '-']);
    expect((card.querySelector('.rh-instrument-strip app-info-tip button.gh-info-btn') as HTMLElement).getAttribute('aria-label'))
      .toBe('About Instrument of battery run 9');
    expect(card.querySelectorAll('[title]').length).toBe(0);
  });

  it('should tier the Overall Index like a run score, and say why there is none while incomplete', () => {
    openHistoryWith([], [
      finishedBattery({ id: 9, latestAnalysisId: 4, latestAnalysisComplete: true, overallIndex: 82.4, overallIndexHalfWidth: 3.1 }),
      finishedBattery({
        id: 8, startedAtUtc: '2026-09-01T00:00:00Z', status: 'Stopped', resumable: true, completedSuiteCount: 1,
        latestAnalysisId: 3, latestAnalysisComplete: false, analysisStale: true, overallIndex: null
      })
    ]);

    const index = batteryCard(9).querySelector('.rh-metric[data-metric="intelligence"] .score-badge') as HTMLElement;
    expect(text(index)).toBe('82.4 ± 3.1');
    expect(index.classList.contains('badge-score-high')).toBe(true);
    expect(batteryCard(9).querySelector('.rh-battery-not-analyzed')).toBeNull();

    const incomplete = batteryCard(8).querySelector('.rh-metric[data-metric="intelligence"]') as HTMLElement;
    expect(incomplete.querySelector('.score-badge')).toBeNull();
    expect(text(incomplete.querySelector('.rh-metric-note'))).toBe('Incomplete (1 of 2 suites)');
    expect(text(batteryCard(8).querySelector('.rh-card-kicker .gh-tag-changed'))).toBe('Analysis stale');
    expect(batteryCard(8).querySelector('.rh-card-kicker .status-badge')?.classList.contains('badge-status-completedwitherrors')).toBe(true);
  });

  it('should hide battery member runs until the toggle shows them, keep it through Clear all and remember it', () => {
    openHistoryWith([
      buildHistoryRun({ id: 3, startedAtUtc: '2026-09-03T00:00:00Z' }),
      buildHistoryRun({
        id: 41, startedAtUtc: '2026-09-02T00:10:00Z', batteryRunId: 9, batterySuitePosition: 1, batterySuiteCount: 2
      } as Partial<BenchmarkRunSummaryDto>),
      buildHistoryRun({ id: 1, startedAtUtc: '2026-09-01T00:00:00Z' })
    ], [finishedBattery()]);

    expect(membersToggle().classList.contains('gh-filter-toggle')).toBe(true);
    expect(membersToggle().getAttribute('aria-pressed')).toBe('false');
    expect(shownKeys()).toEqual(['run:3', 'battery:9', 'run:1']);
    // A battery run counts as one run; a hidden member run is not counted.
    expect(historyStatus()).toBe('Showing 3 of 3 runs');

    membersToggle().click();

    expect(membersToggle().getAttribute('aria-pressed')).toBe('true');
    expect(shownKeys()).toEqual(['run:3', 'run:41', 'battery:9', 'run:1']);
    const memberCard = fixture.nativeElement.querySelector('article.rh-card[data-run-id="41"]') as HTMLElement;
    expect(text(memberCard.querySelector('.rh-card-kicker .rh-battery-badge'))).toBe('Battery #9 · suite 1/2');
    expect(localStorage.getItem(RUN_HISTORY_MEMBERS_STORAGE_KEY)).toBe('1');

    // Not a filter: Clear all leaves it on, and no chip stands for it.
    ctx.historyTab().onHistoryFacetChange('kind', ['Single run']);
    expect(shownKeys()).toEqual(['run:3', 'run:41', 'run:1']);
    (fixture.nativeElement.querySelector('.rh-clear-filters') as HTMLButtonElement).click();
    expect(membersToggle().getAttribute('aria-pressed')).toBe('true');
    expect(shownKeys()).toEqual(['run:3', 'run:41', 'battery:9', 'run:1']);

    const restored = TestBed.createComponent(AdminBenchmarkComponent);
    expect(benchmarkSpecHandles(restored).workspace.showBatteryMembers).toBe(true);
    restored.destroy();

    membersToggle().click();
    expect(membersToggle().getAttribute('aria-pressed')).toBe('false');
    expect(shownKeys()).toEqual(['run:3', 'battery:9', 'run:1']);
    expect(localStorage.getItem(RUN_HISTORY_MEMBERS_STORAGE_KEY)).toBe('0');
  });

  it('should filter by the Kind facet', () => {
    openHistoryWith(
      [buildHistoryRun({ id: 3, startedAtUtc: '2026-09-03T00:00:00Z' }), buildHistoryRun({ id: 1, startedAtUtc: '2026-09-01T00:00:00Z' })],
      [finishedBattery()]
    );

    const facet = ctx.historyTab().historyFacets.find(f => f.column === 'kind')!;
    expect(facet.facetId).toBe('rh-facet-kind');
    expect(facet.options.map(o => [o.value, o.count])).toEqual([['Single run', 2], ['Battery run', 1]]);
    expect(fixture.nativeElement.querySelector('.rh-facet-row #rh-facet-kind-trigger')).toBeTruthy();

    ctx.historyTab().onHistoryFacetChange('kind', ['Battery run']);

    expect(shownKeys()).toEqual(['battery:9']);
    expect((Array.from(fixture.nativeElement.querySelectorAll('.rh-filter-chips .gh-filter-chip')) as HTMLElement[])
      .map(chip => chip.getAttribute('aria-label'))).toEqual(['Remove filter Kind: Battery run']);
  });

  it('should find a battery run by its battery name and by the name of one of its suites', () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    openHistoryWith(
      [buildHistoryRun({ id: 3, startedAtUtc: '2026-09-03T00:00:00Z' }), buildHistoryRun({ id: 1, startedAtUtc: '2026-09-01T00:00:00Z' })],
      [finishedBattery()]
    );
    const search = fixture.nativeElement.querySelector('#rh-search') as HTMLInputElement;
    const searchFor = (value: string) => {
      search.value = value;
      search.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(ctx.workspace.historyList.debounceMs);
    };

    searchFor('core battery');
    expect(shownKeys()).toEqual(['battery:9']);

    searchFor('Second Suite');
    expect(shownKeys()).toEqual(['battery:9']);

    searchFor('battery #9');
    expect(shownKeys()).toEqual(['battery:9']);
  });

  it('should open the Battery Run Report from View details', () => {
    const open = vi.spyOn(ctx.bridge, 'openBatteryRunReport').mockImplementation(() => { });
    openHistoryWith([], [finishedBattery()]);

    (batteryCard().querySelector('button[aria-label="View details for battery run 9"]') as HTMLButtonElement).click();

    expect(open).toHaveBeenCalledWith(9);
  });

  it('should keep Download Markdown report aria-disabled, with its reason, until the battery run is analyzed', () => {
    const windowOpen = vi.spyOn(window, 'open').mockReturnValue(null);
    try {
      openHistoryWith([], [
        finishedBattery({ id: 9 }),
        finishedBattery({ id: 8, startedAtUtc: '2026-09-01T00:00:00Z', latestAnalysisId: 4, latestAnalysisComplete: true, overallIndex: 70 })
      ]);

      const notAnalyzed = batteryCard(9).querySelector('button[aria-label="Download Markdown report for battery run 9"]') as HTMLButtonElement;
      expect(notAnalyzed.getAttribute('aria-disabled')).toBe('true');
      expect(notAnalyzed.disabled).toBe(false);
      expect(notAnalyzed.getAttribute('interestfor')).toBe('tip-dl-battery-9');
      expect(text(batteryCard(9).querySelector('#tip-dl-battery-9'))).toBe('Not analyzed yet: compute the analysis in View details first');
      notAnalyzed.click();
      expect(windowOpen).not.toHaveBeenCalled();
      expect(benchmarkServiceMock.getBatteryReportUrl).not.toHaveBeenCalled();

      const analyzed = batteryCard(8).querySelector('button[aria-label="Download Markdown report for battery run 8"]') as HTMLButtonElement;
      expect(analyzed.hasAttribute('aria-disabled')).toBe(false);
      expect(text(batteryCard(8).querySelector('#tip-dl-battery-8'))).toBe('Download Markdown report');
      analyzed.click();
      expect(benchmarkServiceMock.getBatteryReportUrl).toHaveBeenCalledWith(8);
      expect(windowOpen).toHaveBeenCalledWith('/api/admin/benchmark/batteries/runs/9/report', '_blank');
    } finally {
      windowOpen.mockRestore();
    }
  });

  it('should offer Show progress only while the battery run is live or resumable, and keep Delete aria-disabled while it is live', () => {
    const openProgress = vi.spyOn(ctx.monitor, 'openBatteryDialog').mockImplementation(() => { });
    openHistoryWith([], [
      buildBatteryRun({ id: 9, status: 'Running', isDriving: true, startedAtUtc: '2026-09-03T00:00:00Z' }),
      finishedBattery({ id: 8, status: 'Stopped', resumable: true, startedAtUtc: '2026-09-02T00:00:00Z' }),
      finishedBattery({ id: 7, startedAtUtc: '2026-09-01T00:00:00Z' })
    ]);

    const progress = (id: number) => batteryCard(id).querySelector('.rh-battery-progress') as HTMLButtonElement | null;
    expect(progress(9)?.getAttribute('aria-label')).toBe('Show progress of battery run 9');
    expect(progress(8)).toBeTruthy();
    expect(progress(7)).toBeNull();
    progress(9)!.click();
    expect(openProgress).toHaveBeenCalledWith(9);

    const liveDelete = batteryCard(9).querySelector('button[aria-label="Delete battery run 9"]') as HTMLButtonElement;
    expect(liveDelete.classList.contains('action-btn-danger')).toBe(true);
    expect(liveDelete.getAttribute('aria-disabled')).toBe('true');
    expect(text(batteryCard(9).querySelector('#tip-del-battery-9'))).toBe('A live battery run cannot be deleted; stop it first');
    liveDelete.click();
    fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('#rhDeleteBatteryDialog') as HTMLDialogElement).open).toBe(false);

    expect(batteryCard(8).querySelector('button[aria-label="Delete battery run 8"]')?.hasAttribute('aria-disabled')).toBe(false);
  });

  describe('delete confirmation', () => {
    const run3 = () => buildHistoryRun({ id: 3, startedAtUtc: '2026-09-03T00:00:00Z' });
    const run1 = () => buildHistoryRun({ id: 1, startedAtUtc: '2026-09-01T00:00:00Z' });
    // Run 41's first attempt was superseded, so three member rows point at two runs.
    const battery = () => finishedBattery({ members: [member(41, 0, { memberId: 1, superseded: true }), member(41, 0), member(42, 1)] });

    function dialog(): HTMLDialogElement {
      return fixture.nativeElement.querySelector('#rhDeleteBatteryDialog') as HTMLDialogElement;
    }

    function membersCheckbox(): HTMLInputElement {
      return dialog().querySelector('#rhDeleteBatteryMembers') as HTMLInputElement;
    }

    function openDelete(): void {
      (batteryCard().querySelector('button[aria-label="Delete battery run 9"]') as HTMLButtonElement).click();
      fixture.detectChanges();
    }

    it('should keep the member runs unless asked, then reload Run History and focus the card in its place', () => {
      openHistoryWith([run3(), run1()], [battery()]);
      openDelete();

      expect(dialog().open).toBe(true);
      expect(dialog().classList.contains('gh-dialog')).toBe(true);
      expect(text(dialog().querySelector('#rhDeleteBatteryTitle'))).toBe('Delete battery run #9?');
      expect(dialog().getAttribute('aria-labelledby')).toBe('rhDeleteBatteryTitle');
      expect(text(dialog().querySelector('.rh-delete-battery-message'))).toContain('analyses and its AI documents');
      expect(membersCheckbox().checked).toBe(false);
      expect(membersCheckbox().closest('label')?.classList.contains('checkbox-label')).toBe(true);
      expect(text(membersCheckbox().closest('label'))).toBe('Also delete its 2 member runs');
      const confirm = dialog().querySelector('.rh-confirm-delete-battery') as HTMLButtonElement;
      expect(confirm.classList.contains('btn-gh')).toBe(true);
      expect(confirm.classList.contains('btn-gh-delete')).toBe(true);
      expect(confirm.querySelector('svg.btn-icon polyline[points="3 6 5 6 21 6"]')).toBeTruthy();

      benchmarkServiceMock.getRuns.mockClear();
      benchmarkServiceMock.getBatteryRuns.mockClear();
      benchmarkServiceMock.getRuns.mockReturnValue(of([run3(), run1()]));
      benchmarkServiceMock.getBatteryRuns.mockReturnValue(of([]));
      confirm.click();

      expect(deleteBatteryRun).toHaveBeenCalledWith(9, false);
      expect(benchmarkServiceMock.getRuns).toHaveBeenCalledTimes(1);
      expect(benchmarkServiceMock.getBatteryRuns).toHaveBeenCalledTimes(1);
      expect(dialog().open).toBe(false);
      expect(shownKeys()).toEqual(['run:3', 'run:1']);
      expect(document.activeElement?.id).toBe('rh-run-1-title');
    });

    it('should delete the member runs too when the box is checked', () => {
      const runDeleted = vi.spyOn(ctx.bridge, 'runDeleted');
      openHistoryWith([run3(), run1()], [battery()]);
      openDelete();

      membersCheckbox().click();
      expect(membersCheckbox().checked).toBe(true);
      benchmarkServiceMock.getBatteryRuns.mockReturnValue(of([]));
      (dialog().querySelector('.rh-confirm-delete-battery') as HTMLButtonElement).click();

      expect(deleteBatteryRun).toHaveBeenCalledWith(9, true);
      expect(runDeleted.mock.calls).toEqual([[41], [42]]);
      expect(dialog().open).toBe(false);
      expect(shownKeys()).toEqual(['run:3', 'run:1']);
    });

    it('should open unchecked again after a canceled confirmation', () => {
      openHistoryWith([run3(), run1()], [battery()]);
      openDelete();
      membersCheckbox().click();
      (dialog().querySelector('.btn-gh-cancel') as HTMLButtonElement).click();
      expect(dialog().open).toBe(false);
      expect(deleteBatteryRun).not.toHaveBeenCalled();

      openDelete();
      expect(membersCheckbox().checked).toBe(false);
    });
  });

  it('should keep an INSTRUMENT CHANGED badge on its own run with battery cards interleaved and member runs hidden', () => {
    openHistoryWith([
      buildHistoryRun({ id: 3, startedAtUtc: '2026-09-03T00:00:00Z', knowledgeBaseHeadSha: 'kb-b' }),
      // A member of battery run 9: hidden, yet still the next older run of the suite.
      buildHistoryRun({ id: 2, startedAtUtc: '2026-09-02T00:30:00Z', batteryRunId: 9 } as Partial<BenchmarkRunSummaryDto>),
      buildHistoryRun({ id: 1, startedAtUtc: '2026-09-01T00:00:00Z' })
    ], [finishedBattery({ startedAtUtc: '2026-09-02T00:00:00Z' }), finishedBattery({ id: 10, startedAtUtc: '2026-09-04T00:00:00Z' })]);

    const badgesOf = (key: string) => {
      const [kind, id] = key.split(':');
      const card = fixture.nativeElement.querySelector(kind === 'run'
        ? `article.rh-card[data-run-id="${id}"]`
        : `article.rh-card[data-battery-run-id="${id}"]`) as HTMLElement;
      return (Array.from(card.querySelectorAll('.instrument-changed')) as HTMLElement[])
        .map(badge => `${badge.textContent?.trim()} (${badge.getAttribute('aria-label')})`);
    };

    expect(shownKeys()).toEqual(['battery:10', 'run:3', 'battery:9', 'run:1']);
    expect(badgesOf('run:3')).toEqual(['INSTRUMENT CHANGED (Instrument changed since run 2)']);
    expect(badgesOf('battery:10')).toEqual([]);
    expect(badgesOf('battery:9')).toEqual([]);
    expect(badgesOf('run:1')).toEqual([]);

    membersToggle().click();
    expect(shownKeys()).toEqual(['battery:10', 'run:3', 'run:2', 'battery:9', 'run:1']);
    expect(badgesOf('run:3')).toEqual(['INSTRUMENT CHANGED (Instrument changed since run 2)']);
    expect(badgesOf('run:2')).toEqual([]);
  });

  it('should say when the newest battery runs are all that is loaded', () => {
    const batteries = Array.from({ length: BATTERY_RUN_HISTORY_LIMIT }, (_, i) => finishedBattery({
      id: BATTERY_RUN_HISTORY_LIMIT - i,
      startedAtUtc: new Date(Date.UTC(2026, 0, 1) + (BATTERY_RUN_HISTORY_LIMIT - i) * 60_000).toISOString()
    }));
    openHistoryWith([buildHistoryRun({ id: 2, startedAtUtc: '2027-01-01T00:00:00Z' }), buildHistoryRun({ id: 1, startedAtUtc: '2025-01-01T00:00:00Z' })], batteries);

    expect(benchmarkServiceMock.getBatteryRuns).toHaveBeenCalledWith(undefined, BATTERY_RUN_HISTORY_LIMIT);
    expect(historyStatus()).toBe(`Showing 10 of ${BATTERY_RUN_HISTORY_LIMIT + 2} runs · newest ${BATTERY_RUN_HISTORY_LIMIT} battery runs`);
  });

  it('should mention battery runs in the empty state, with the advice on starting one', () => {
    openHistoryWith([], []);

    const empty = fixture.nativeElement.querySelector('#bm-panel-history .rh-empty') as HTMLElement;
    expect(text(empty)).toBe('No benchmark runs or battery runs recorded yet. Start one from the Run Benchmark tab; for a battery run, set Run Target to Battery.');
  });
});
