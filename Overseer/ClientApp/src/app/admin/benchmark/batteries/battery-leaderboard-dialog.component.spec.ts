import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkBatteryLeaderboardClassDto,
  BenchmarkBatteryLeaderboardDto,
  BenchmarkBatteryLeaderboardRowDto
} from '../../../services/admin-benchmark.service';
import { MAX_COMPARISON_SOURCES } from '../model-comparison/comparison-source-picker.component';
import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import {
  BatteryLeaderboardDialogComponent,
  BatteryLeaderboardOpenRequest,
  comparisonSourceIds,
  intervalStripGeometry,
  intervalStripScale,
  intervalsOverlap
} from './battery-leaderboard-dialog.component';

const HASH = 'ee1a4cfe0123456789abcdef0123456789abcdef0123456789abcdef01234567';
const CLASS_A = 'a1b2c3d4aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
const CLASS_B = 'b2c3d4e5bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb';
const CLASS_C = 'c3d4e5f6cccccccccccccccccccccccccccccccccccccccccccccccccccccccc';

function hourIso(hour: number): string {
  return new Date(Date.UTC(2026, 8, 1) + hour * 3_600_000).toISOString();
}

function row(id: number, overrides: Partial<BenchmarkBatteryLeaderboardRowDto> = {}): BenchmarkBatteryLeaderboardRowDto {
  return {
    batteryRunId: id,
    batteryId: 3,
    batteryName: 'Core Battery',
    definitionRevision: 2,
    analysisId: id * 10,
    computedAtUtc: hourIso(id),
    testedModelConfigurationId: id,
    testedModelLabel: `Model ${id}`,
    status: 'Completed',
    runsPerSuite: 3,
    suiteCount: 4,
    completedSuiteCount: 4,
    complete: true,
    comparabilityClassSha256: CLASS_A,
    harnessVersion: '31',
    scoringMethodVersion: 4,
    overallIndex: 70,
    overallIndexHalfWidth: 5,
    overallIndexLower: 65,
    overallIndexUpper: 75,
    overallSpeedIndex: 55.25,
    totalCost: 3,
    passCost: 1.5,
    testedProvider: 'Anthropic',
    testedModelId: `model-${id}`,
    testedThinkingLevel: null,
    testedReasoningMode: null,
    testedServiceTier: null,
    ...overrides
  } as BenchmarkBatteryLeaderboardRowDto;
}

function cls(sha: string, rows: BenchmarkBatteryLeaderboardRowDto[], overrides: Partial<BenchmarkBatteryLeaderboardClassDto> = {}): BenchmarkBatteryLeaderboardClassDto {
  return {
    comparabilityClassSha256: sha,
    label: `Harness 31, class ${sha.slice(0, 8)}`,
    harnessVersion: '31',
    scoringMethodVersion: 4,
    distinguishingKeys: [],
    rows,
    ...overrides
  };
}

function board(classes: BenchmarkBatteryLeaderboardClassDto[], incomplete: BenchmarkBatteryLeaderboardRowDto[] = []): BenchmarkBatteryLeaderboardDto {
  return { definitionSha256: HASH, batteryId: 3, batteryName: 'Core Battery', classes, incomplete };
}

/** Three ranked rows of one class: 1 and 2 overlap, 3 stands clear below. */
function threeRows(): BenchmarkBatteryLeaderboardRowDto[] {
  return [
    row(1, { testedModelLabel: 'Zeta', overallIndex: 80, overallIndexHalfWidth: 4, overallIndexLower: 76, overallIndexUpper: 84, overallSpeedIndex: 40, passCost: 2 }),
    row(2, { testedModelLabel: 'Alpha', overallIndex: 72.4, overallIndexHalfWidth: 5.1, overallIndexLower: 67.3, overallIndexUpper: 77.5, overallSpeedIndex: 60, passCost: 0.5,
             testedProvider: 'OpenAI', testedThinkingLevel: 'high' }),
    row(3, { testedModelLabel: 'Mid', overallIndex: 50, overallIndexHalfWidth: 3, overallIndexLower: 47, overallIndexUpper: 53, overallSpeedIndex: 50, passCost: 1 })
  ];
}

const REQUEST: BatteryLeaderboardOpenRequest = {
  definitionSha256: HASH,
  name: 'Core Battery',
  revision: 2,
  schemeLabel: 'Questions and difficulty',
  suiteCount: 4
};

describe('BatteryLeaderboardDialogComponent', () => {
  let fixture: ComponentFixture<BatteryLeaderboardDialogComponent>;
  let component: BatteryLeaderboardDialogComponent;
  let service: MockedObject<AdminBenchmarkService>;
  let bridge: MockedObject<BenchmarkShellBridge>;

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function q<T extends Element = HTMLElement>(selector: string): T | null {
    return el().querySelector<T>(selector);
  }

  function qa<T extends Element = HTMLElement>(selector: string): T[] {
    return Array.from(el().querySelectorAll<T>(selector));
  }

  function text(selector: string): string {
    return (q(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function dialog(): HTMLDialogElement {
    return q<HTMLDialogElement>('dialog.battery-leaderboard-dialog')!;
  }

  function open(data: BenchmarkBatteryLeaderboardDto, request: BatteryLeaderboardOpenRequest = REQUEST): void {
    service.getBatteryLeaderboard.mockReturnValue(of(data));
    component.open(request);
    fixture.detectChanges();
  }

  function bodyRows(): HTMLTableRowElement[] {
    return qa<HTMLTableRowElement>('.bl-table tbody tr');
  }

  function rankedIds(): number[] {
    return bodyRows().map(r => Number(r.getAttribute('data-battery-run')));
  }

  function ranks(): string[] {
    return bodyRows().map(r => (r.querySelector('.bl-rank')?.textContent ?? '')
      .replace('interval overlaps the result above', '').replace('≈', '').trim());
  }

  function sortButton(label: string): HTMLButtonElement {
    const th = qa<HTMLTableCellElement>('.bl-table th.gh-th-sortable').find(h => h.textContent?.includes(label));
    return th!.querySelector('button')!;
  }

  function tabs(): HTMLButtonElement[] {
    return qa<HTMLButtonElement>('[role="tab"]');
  }

  function key(target: HTMLElement, name: string): void {
    target.dispatchEvent(new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }

  beforeEach(async () => {
    service = {
      getBatteryLeaderboard: vi.fn().mockName('AdminBenchmarkService.getBatteryLeaderboard')
    } as unknown as MockedObject<AdminBenchmarkService>;
    bridge = {
      openBatteryRunReport: vi.fn().mockName('BenchmarkShellBridge.openBatteryRunReport'),
      openComparisonWizard: vi.fn().mockName('BenchmarkShellBridge.openComparisonWizard')
    } as unknown as MockedObject<BenchmarkShellBridge>;

    await TestBed.configureTestingModule({
      imports: [BatteryLeaderboardDialogComponent],
      providers: [
        { provide: AdminBenchmarkService, useValue: service },
        { provide: BenchmarkShellBridge, useValue: bridge }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(BatteryLeaderboardDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    component.close();
  });

  // -------------------------------------------------------------------------------------------
  // Open, header and close
  // -------------------------------------------------------------------------------------------

  it('loads nothing until opened', () => {
    expect(service.getBatteryLeaderboard).not.toHaveBeenCalled();
    expect(dialog().open).toBe(false);
  });

  it('opens modal, loads the definition and focuses the title', () => {
    open(board([cls(CLASS_A, threeRows())]));

    expect(dialog().open).toBe(true);
    expect(dialog().getAttribute('aria-labelledby')).toBe('blTitle');
    expect(dialog().classList).toContain('gh-dialog-fullscreen');
    expect(service.getBatteryLeaderboard).toHaveBeenCalledWith(HASH);
    expect(text('#blTitle')).toBe('Leaderboard: Core Battery');
    expect(q('#blTitle')!.getAttribute('tabindex')).toBe('-1');
    expect(document.activeElement).toBe(q('#blTitle'));
    expect(q<HTMLImageElement>('.bl-emblem')!.getAttribute('alt')).toBe('');
  });

  it('lists the revision, scheme, suites, uniform runs per suite and the short definition hash', () => {
    open(board([cls(CLASS_A, threeRows())]));

    const badges = qa('.bl-badges > li').map(li => (li.textContent ?? '').replace(/\s+/g, ' ').trim());
    expect(badges[0]).toBe('Revision 2');
    expect(badges[1]).toBe('weighting scheme Questions and difficulty');
    expect(badges[2]).toBe('4 suites');
    expect(badges[3]).toBe('3 runs per suite');
    expect(text('.bl-definition')).toBe('definition ee1a4cfe');
    expect(q('#bl-definition-tip')?.textContent).toContain(HASH);
  });

  it('leaves out runs per suite when the battery runs differ', () => {
    const rows = threeRows();
    rows[2] = { ...rows[2], runsPerSuite: 1 };
    open(board([cls(CLASS_A, rows)]));

    expect(qa('.bl-badges > li').some(li => li.textContent?.includes('per suite'))).toBe(false);
  });

  it('closes on the header Close and emits closed', () => {
    open(board([cls(CLASS_A, threeRows())]));
    const closed = vi.fn().mockName('closed');
    component.closed.subscribe(closed);

    const close = q<HTMLButtonElement>('.bl-close')!;
    expect(close.classList).toContain('btn-icon-action');
    expect(close.getAttribute('aria-label')).toBe('Close leaderboard');
    expect(close.getAttribute('interestfor')).toBe('bl-close-tip');
    close.click();
    fixture.detectChanges();

    expect(dialog().open).toBe(false);
    expect(closed).toHaveBeenCalledTimes(1);
  });

  it('closes on Escape without letting its cancel or close events reach the host', () => {
    open(board([cls(CLASS_A, threeRows())]));
    const closed = vi.fn().mockName('closed');
    component.closed.subscribe(closed);
    const heard: string[] = [];
    el().addEventListener('cancel', () => heard.push('cancel'));
    el().addEventListener('close', () => heard.push('close'));

    const cancel = new Event('cancel', { bubbles: true, cancelable: true });
    dialog().dispatchEvent(cancel);
    fixture.detectChanges();

    expect(cancel.defaultPrevented).toBe(true);
    expect(dialog().open).toBe(false);
    expect(closed).toHaveBeenCalledTimes(1);

    component.open(REQUEST);
    fixture.detectChanges();
    dialog().dispatchEvent(new Event('close', { bubbles: true }));
    fixture.detectChanges();

    expect(heard).toEqual([]);
    expect(closed).toHaveBeenCalledTimes(2);
  });

  // -------------------------------------------------------------------------------------------
  // Load states
  // -------------------------------------------------------------------------------------------

  it('shows a status line while loading', () => {
    service.getBatteryLeaderboard.mockReturnValue(new Subject<BenchmarkBatteryLeaderboardDto>());
    component.open(REQUEST);
    fixture.detectChanges();

    const loading = q('.bl-loading')!;
    expect(loading.getAttribute('role')).toBe('status');
    expect(loading.textContent).toContain('Loading leaderboard');
    expect(q('.bl-table')).toBeNull();
  });

  it('shows a failure with Retry, and Retry loads again and focuses the title', () => {
    service.getBatteryLeaderboard.mockReturnValue(throwError(() => ({ error: 'The server is down.' })));
    component.open(REQUEST);
    fixture.detectChanges();

    const alert = q('.bl-error')!;
    expect(alert.classList).toContain('alert-danger');
    expect(alert.getAttribute('role')).toBe('alert');
    expect(alert.textContent).toContain('The server is down.');

    service.getBatteryLeaderboard.mockReturnValue(of(board([cls(CLASS_A, threeRows())])));
    q<HTMLButtonElement>('.bl-retry')!.click();
    fixture.detectChanges();

    expect(service.getBatteryLeaderboard).toHaveBeenCalledTimes(2);
    expect(q('.bl-error')).toBeNull();
    expect(bodyRows().length).toBe(3);
    expect(document.activeElement).toBe(q('#blTitle'));
  });

  it('says how to get a result when nothing has an analysis', () => {
    open(board([]));

    expect(qa('.bl-empty p').map(p => p.textContent!.trim())).toEqual([
      'No battery run of this definition has an analysis yet.',
      'Start one from Run Benchmark with Run Target set to Battery.'
    ]);
    expect(q('.bl-table')).toBeNull();
    expect(q('.bl-open-comparison')!.getAttribute('aria-disabled')).toBe('true');
  });

  it('lists incomplete battery runs in a closed disclosure', () => {
    open(board([cls(CLASS_A, threeRows())], [row(9, { complete: false, completedSuiteCount: 2, status: 'Stopped' })]));

    const details = q<HTMLDetailsElement>('details.bl-incomplete')!;
    expect(details.classList).toContain('gh-disclosure');
    expect(details.open).toBe(false);
    expect(text('details.bl-incomplete summary')).toBe('Incomplete battery runs (1) — not ranked');
    expect(text('.bl-incomplete-table tbody tr')).toContain('#9 · rev. 2');
  });

  // -------------------------------------------------------------------------------------------
  // Classes
  // -------------------------------------------------------------------------------------------

  it('shows one class without a tab row', () => {
    open(board([cls(CLASS_A, threeRows())]));

    expect(q('[role="tablist"]')).toBeNull();
    expect(q('#bl-panel')!.getAttribute('role')).toBeNull();
    expect(text('.bl-class-meta')).toBe('Harness 31 · scoring method 4 · class a1b2c3d4');
    expect(q('.bl-differs')).toBeNull();
    expect(text('.bl-note')).toBe(
      'Each table ranks results of one comparability class. Overlapping intervals are not a ranking.');
    expect(text('.bl-table caption')).toContain('Class A');
  });

  it('orders several classes as tabs by row count, then newest, with the full tab contract', () => {
    const small = cls(CLASS_A, [row(1)], { distinguishingKeys: ['HarnessVersion'] });
    const bigOld = cls(CLASS_B, [row(2), row(3)], { distinguishingKeys: ['HarnessVersion', 'ScoringMethodVersion'] });
    const bigNew = cls(CLASS_C, [row(4), row(5)], { distinguishingKeys: ['ScoringMethodVersion'] });
    open(board([small, bigOld, bigNew]));

    const tablist = q('[role="tablist"]')!;
    expect(tablist.getAttribute('aria-label')).toBe('Comparability classes');
    expect(tablist.classList).toContain('gh-tabs-secondary');
    expect(tabs().map(t => t.querySelector('.bl-tab-name')!.textContent)).toEqual(['Class A', 'Class B', 'Class C']);
    expect(tabs().map(t => t.querySelector('.bl-tab-count')!.textContent)).toEqual(['2', '2', '1']);
    // Class A is the newer of the two two-row classes.
    expect(text('.bl-class-hash')).toBe('c3d4e5f6');
    expect(tabs().map(t => t.getAttribute('aria-selected'))).toEqual(['true', 'false', 'false']);
    expect(tabs().map(t => t.getAttribute('tabindex'))).toEqual(['0', '-1', '-1']);
    expect(tabs().every(t => t.getAttribute('aria-controls') === 'bl-panel')).toBe(true);

    const panel = q('#bl-panel')!;
    expect(panel.getAttribute('role')).toBe('tabpanel');
    expect(panel.getAttribute('aria-labelledby')).toBe('bl-tab-0');
    expect(panel.getAttribute('tabindex')).toBe('0');
    expect(qa('.bl-differs-tag').map(t => t.textContent)).toEqual(['ScoringMethodVersion']);
    expect(text('.bl-differs-label')).toBe('Differs from other classes on');
  });

  it('moves between class tabs with the arrow keys, Home and End, focus following', () => {
    open(board([cls(CLASS_A, [row(1), row(2)]), cls(CLASS_B, [row(3)]), cls(CLASS_C, [row(4)])]));

    key(tabs()[0], 'ArrowRight');
    expect(tabs()[1].getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(tabs()[1]);
    expect(q('#bl-panel')!.getAttribute('aria-labelledby')).toBe('bl-tab-1');

    key(tabs()[1], 'End');
    expect(tabs()[2].getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(tabs()[2]);

    key(tabs()[2], 'ArrowRight');
    expect(tabs()[0].getAttribute('aria-selected')).toBe('true');

    key(tabs()[0], 'ArrowLeft');
    expect(tabs()[2].getAttribute('aria-selected')).toBe('true');

    key(tabs()[2], 'Home');
    expect(tabs()[0].getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(tabs()[0]);

    // Tab order: the two-row class, then the newer one-row class (run 4), then run 3's.
    tabs()[1].click();
    fixture.detectChanges();
    expect(tabs()[1].getAttribute('aria-selected')).toBe('true');
    expect(rankedIds()).toEqual([4]);
  });

  // -------------------------------------------------------------------------------------------
  // The ranked table
  // -------------------------------------------------------------------------------------------

  it('renders the ranked columns, badges and a sticky data table', () => {
    open(board([cls(CLASS_A, threeRows())]));

    expect(q('.gh-datatable-scroll .bl-table')!.classList).toContain('gh-datatable');
    const headers = qa('.bl-table thead th').map(th => (th.textContent ?? '').replace(/\s+/g, ' ').trim());
    expect(headers).toEqual(['Rank', 'Model', 'Overall Index', '95 % interval', 'Speed', 'Pass cost', 'R', 'Analyzed', 'Actions']);
    expect(rankedIds()).toEqual([1, 2, 3]);
    expect(ranks()).toEqual(['1', '2', '3']);

    const second = bodyRows()[1];
    expect(second.querySelector('.bl-model-name')!.textContent).toBe('Alpha');
    expect(second.querySelector('.provider-badge')!.textContent!.trim()).toBe('OpenAI');
    const thinking = second.querySelector('.thinking-badge')!;
    expect(thinking.querySelector('.visually-hidden')!.textContent).toBe('thinking level ');
    expect(thinking.hasAttribute('title')).toBe(false);
    expect(second.querySelector('.bl-index')!.textContent!.trim()).toBe('72.4 ± 5.1');
    expect(second.querySelector('.bl-interval-text')!.textContent!.trim()).toBe('[67.3, 77.5]');
    expect(second.querySelector('.bl-speed')!.textContent!.trim()).toBe('60.0');
    expect(second.querySelector('.bl-cost')!.textContent!.trim()).toBe('$0.5000');
    expect(second.querySelector('.bl-r')!.textContent!.trim()).toBe('3');
    expect(second.querySelector('time')!.getAttribute('datetime')).toBe(hourIso(2));
  });

  it('marks a rank whose interval overlaps the result above', () => {
    open(board([cls(CLASS_A, threeRows())]));

    const overlaps = bodyRows().map(r => r.querySelector('.bl-overlap'));
    expect(overlaps[0]).toBeNull();
    expect(overlaps[1]!.textContent).toContain('≈');
    expect(overlaps[1]!.querySelector('.visually-hidden')!.textContent).toBe('interval overlaps the result above');
    expect(overlaps[2]).toBeNull();
  });

  it('sorts by a column while Rank stays the index rank', () => {
    open(board([cls(CLASS_A, threeRows())]));
    expect(sortButton('Overall Index').closest('th')!.getAttribute('aria-sort')).toBe('descending');

    sortButton('Model').click();
    fixture.detectChanges();
    expect(sortButton('Model').closest('th')!.getAttribute('aria-sort')).toBe('descending');
    expect(rankedIds()).toEqual([1, 3, 2]);
    expect(ranks()).toEqual(['1', '3', '2']);

    sortButton('Model').click();
    fixture.detectChanges();
    expect(rankedIds()).toEqual([2, 3, 1]);
    expect(ranks()).toEqual(['2', '3', '1']);

    sortButton('Pass cost').click();
    fixture.detectChanges();
    expect(rankedIds()).toEqual([1, 3, 2]);
    expect(ranks()).toEqual(['1', '3', '2']);
    expect(q('.gh-pager')).toBeNull();
  });

  it('draws every strip of a class on one shared scale, the top row in gold', () => {
    open(board([cls(CLASS_A, threeRows())]));

    const strips = bodyRows().map(r => r.querySelector<HTMLElement>('.bl-strip')!);
    expect(strips.every(s => s.getAttribute('aria-hidden') === 'true')).toBe(true);
    expect(strips[0].classList).toContain('is-top');
    expect(strips[1].classList).not.toContain('is-top');

    // Scale: 47 – 84, padded by 5 % of 37 on each side.
    const scale = intervalStripScale(threeRows())!;
    expect(scale.min).toBeCloseTo(47 - 1.85, 6);
    expect(scale.max).toBeCloseTo(84 + 1.85, 6);
    const lowest = intervalStripGeometry(threeRows()[2], scale)!;
    const highest = intervalStripGeometry(threeRows()[0], scale)!;
    expect(lowest.startPct).toBeCloseTo((1.85 / 40.7) * 100, 6);
    expect(highest.endPct).toBeCloseTo(((84 - 45.15) / 40.7) * 100, 6);

    const bar = strips[2].querySelector<HTMLElement>('.bl-strip-bar')!;
    expect(parseFloat(bar.style.insetInlineStart)).toBeCloseTo(lowest.startPct, 3);
  });

  it('ends a truncated interval in a chevron', () => {
    const rows = [
      row(1, { overallIndex: 97, overallIndexHalfWidth: 6, overallIndexLower: 91, overallIndexUpper: 100 }),
      row(2, { overallIndex: 3, overallIndexHalfWidth: 5, overallIndexLower: 0, overallIndexUpper: 8 })
    ];
    open(board([cls(CLASS_A, rows)]));

    expect(bodyRows()[0].querySelector('.bl-strip-chevron-high')).not.toBeNull();
    expect(bodyRows()[0].querySelector('.bl-strip-chevron-low')).toBeNull();
    expect(bodyRows()[1].querySelector('.bl-strip-chevron-low')).not.toBeNull();
    expect(bodyRows()[1].querySelector('.bl-strip-chevron-high')).toBeNull();
  });

  it('decides overlap only from two complete intervals', () => {
    expect(intervalsOverlap(row(1, { overallIndexLower: 60, overallIndexUpper: 70 }),
      row(2, { overallIndexLower: 70, overallIndexUpper: 80 }))).toBe(true);
    expect(intervalsOverlap(row(1, { overallIndexLower: 60, overallIndexUpper: 69.9 }),
      row(2, { overallIndexLower: 70, overallIndexUpper: 80 }))).toBe(false);
    expect(intervalsOverlap(row(1, { overallIndexLower: null, overallIndexUpper: null }),
      row(2))).toBe(false);
  });

  // -------------------------------------------------------------------------------------------
  // Actions
  // -------------------------------------------------------------------------------------------

  it('asks the shell for the battery run report and can return focus to the eye button', () => {
    open(board([cls(CLASS_A, threeRows())]));

    const eye = q<HTMLButtonElement>('#bl-view-2')!;
    expect(eye.getAttribute('aria-label')).toBe('View the report of battery run #2');
    expect(eye.getAttribute('interestfor')).toBe('bl-view-tip-2');
    eye.click();
    expect(bridge.openBatteryRunReport).toHaveBeenCalledWith(2);
    expect(dialog().open).toBe(true);

    q<HTMLElement>('#blTitle')!.focus();
    component.restoreFocusAfterReport();
    expect(document.activeElement).toBe(eye);
  });

  it('keeps Open in Model Comparison aria-disabled with fewer than two ranked rows', () => {
    open(board([cls(CLASS_A, [row(1)])]));

    const button = q<HTMLButtonElement>('.bl-open-comparison')!;
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(button.getAttribute('interestfor')).toBe('bl-open-comparison-tip');
    expect(q('#bl-open-comparison-tip')!.textContent).toContain('at least two ranked results');
    button.click();
    fixture.detectChanges();

    expect(bridge.openComparisonWizard).not.toHaveBeenCalled();
    expect(dialog().open).toBe(true);
  });

  it('opens Model Comparison on the shown class in rank order and closes', () => {
    open(board([cls(CLASS_A, threeRows()), cls(CLASS_B, [row(7)])]));
    const closed = vi.fn().mockName('closed');
    component.closed.subscribe(closed);

    const button = q<HTMLButtonElement>('.bl-open-comparison')!;
    expect(button.getAttribute('aria-disabled')).toBeNull();
    expect(q('.bl-actions')!.getAttribute('aria-label')).toBe('Leaderboard actions');
    button.click();
    fixture.detectChanges();

    expect(bridge.openComparisonWizard).toHaveBeenCalledWith({ batteryRunIds: [1, 2, 3] });
    expect(dialog().open).toBe(false);
    expect(closed).toHaveBeenCalledTimes(1);
  });

  it('caps Model Comparison at its source limit, dropping the newest first with a note', () => {
    const count = MAX_COMPARISON_SOURCES + 2;
    // Ranked newest first, so the cap must drop the two top-ranked rows, not the two lowest.
    const rows = Array.from({ length: count }, (_, i) => {
      const id = count - i;
      return row(id, { overallIndex: 90 - i, overallIndexLower: 89 - i, overallIndexUpper: 91 - i });
    });
    open(board([cls(CLASS_A, rows)]));

    expect(text('#bl-comparison-cap-note')).toBe(
      `Model Comparison takes at most ${MAX_COMPARISON_SOURCES} results: Open in Model Comparison leaves out the 2 newest.`);
    expect(q('.bl-open-comparison')!.getAttribute('aria-describedby')).toBe('bl-comparison-cap-note');

    q<HTMLButtonElement>('.bl-open-comparison')!.click();
    fixture.detectChanges();

    const expected = Array.from({ length: MAX_COMPARISON_SOURCES }, (_, i) => MAX_COMPARISON_SOURCES - i);
    expect(bridge.openComparisonWizard).toHaveBeenCalledWith({ batteryRunIds: expected });
  });

  it('keeps every source that fits', () => {
    expect(comparisonSourceIds(threeRows(), 2)).toEqual([1, 2]);
    expect(comparisonSourceIds(threeRows())).toEqual([1, 2, 3]);
  });

  it('refreshes the leaderboard and keeps the shown class', () => {
    open(board([cls(CLASS_A, [row(1), row(2)]), cls(CLASS_B, [row(3)])]));
    tabs()[1].click();
    fixture.detectChanges();

    service.getBatteryLeaderboard.mockReturnValue(of(board([cls(CLASS_A, [row(1), row(2)]), cls(CLASS_B, [row(3), row(4), row(5)])])));
    const refresh = q<HTMLButtonElement>('.bl-refresh')!;
    expect(refresh.getAttribute('aria-label')).toBe('Refresh leaderboard');
    refresh.click();
    fixture.detectChanges();

    expect(service.getBatteryLeaderboard).toHaveBeenCalledTimes(2);
    // CLASS_B now has the most rows, so it leads the tab row and is still the one shown.
    expect(tabs()[0].getAttribute('aria-selected')).toBe('true');
    expect(text('.bl-class-hash')).toBe('b2c3d4e5');
    expect(rankedIds()).toEqual([3, 4, 5]);
  });
});
