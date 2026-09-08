import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import {
  ComparisonSourcePickerComponent,
  MAX_COMPARISON_SOURCES,
  ModelComparisonSelection
} from './comparison-source-picker.component';
import type {
  BenchmarkRunGroupDto,
  BenchmarkRunSummaryDto
} from '../../../services/admin-benchmark.service';
import type {
  BenchmarkComparabilityIndexDto,
  BenchmarkComparabilityIndexEntryDto
} from './model-comparison.models';

describe('ComparisonSourcePickerComponent', () => {
  let component: ComparisonSourcePickerComponent;
  let fixture: ComponentFixture<ComparisonSourcePickerComponent>;

  function buildRun(overrides: Partial<BenchmarkRunSummaryDto> = {}): BenchmarkRunSummaryDto {
    const id = overrides.id ?? 1;
    return {
      id,
      benchmarkSuiteId: 5,
      suiteName: 'GnollHack Player Assistance Benchmark Suite',
      testedModelConfigurationId: 1,
      testedModelDisplayNameUsed: `Model ${id}`,
      testedModelProviderUsed: 'Google',
      testedModelIdUsed: 'gemini-2.5-flash',
      assessorModelConfigurationId: 2,
      assessorModelDisplayNameUsed: 'Claude Opus',
      startedByUserName: 'admin',
      status: 'Completed',
      startedAtUtc: `2026-09-0${(id % 9) + 1}T10:00:00Z`,
      completedAtUtc: `2026-09-0${(id % 9) + 1}T11:00:00Z`,
      finalScore: 65,
      computedScore: 65,
      qualityIndex: 60 + id,
      qualityIndexStandardError: 2.1,
      rawQualityIndex: 68,
      speedIndex: 80,
      totalAnswerDurationMs: 300000,
      speedMeasurementDegraded: false,
      answeredQuestionCount: 18,
      totalQuestionCount: 18,
      harnessVersion: '1.0.29',
      totalDurationMs: 320000,
      estimatedCost: 0.22,
      ...overrides
    } as BenchmarkRunSummaryDto;
  }

  function buildGroup(overrides: Partial<BenchmarkRunGroupDto> = {}): BenchmarkRunGroupDto {
    const id = overrides.id ?? 1;
    return {
      id,
      name: `Group ${id}`,
      benchmarkSuiteId: 5,
      suiteName: 'GnollHack Player Assistance Benchmark Suite',
      tier: 'Replicate',
      tierLabel: 'Tier A — Replicate',
      comparabilityKeyHash: 'abc123',
      crossCondition: false,
      notes: null,
      createdFromSeriesId: null,
      createdAtUtc: '2026-09-05T10:00:00Z',
      modifiedAtUtc: '2026-09-05T10:00:00Z',
      runCount: 3,
      members: [],
      latestAnalysisId: 7,
      latestAnalysisAtUtc: '2026-09-05T12:00:00Z',
      analysisStale: false,
      ...overrides
    } as BenchmarkRunGroupDto;
  }

  function runs(count: number): BenchmarkRunSummaryDto[] {
    return Array.from({ length: count }, (_unused, index) => buildRun({ id: index + 1 }));
  }

  function buildIndexEntry(
    overrides: Partial<BenchmarkComparabilityIndexEntryDto> = {}
  ): BenchmarkComparabilityIndexEntryDto {
    return {
      key: 'run:1',
      sourceKind: 'Run',
      sourceId: 1,
      conditionOrdinal: 1,
      conditionLabel: 'Condition A',
      signature: 'sig-a',
      selfInconsistent: false,
      selfInconsistentKeys: [],
      differencesFromLargest: [],
      questionParallelism: '1',
      pricingSnapshot: '2026-09-01',
      ...overrides
    };
  }

  /**
   * Two runs and one group in Condition A (the largest), one run in Condition B. Run 3 differs on
   * `serviceTier` from Condition A's `standard`.
   */
  function buildIndex(overrides: Partial<BenchmarkComparabilityIndexDto> = {}): BenchmarkComparabilityIndexDto {
    return {
      computedAtUtc: '2026-09-05T12:00:00Z',
      entries: [
        buildIndexEntry({ key: 'run:1', sourceId: 1 }),
        buildIndexEntry({ key: 'run:2', sourceId: 2 }),
        buildIndexEntry({ key: 'group:1', sourceKind: 'Group', sourceId: 1 }),
        buildIndexEntry({
          key: 'run:3',
          sourceId: 3,
          conditionOrdinal: 2,
          conditionLabel: 'Condition B',
          signature: 'sig-b',
          differencesFromLargest: [{
            name: 'serviceTier',
            kind: 'MustMatch',
            description: 'Service tier differs from the largest condition',
            variants: [{ value: 'priority', runIds: [3] }, { value: 'standard', runIds: [1, 2] }]
          }]
        })
      ],
      conditions: [
        { ordinal: 1, label: 'Condition A', sourceCount: 3, runCount: 3 },
        { ordinal: 2, label: 'Condition B', sourceCount: 1, runCount: 1 }
      ],
      largestConditionKeyValues: { serviceTier: 'standard' },
      mustMatchKeyNames: ['serviceTier'],
      modelAxisKeyNames: ['modelId'],
      degradingKeyNames: ['questionParallelism'],
      ...overrides
    };
  }

  function render(inputs: {
    runs?: BenchmarkRunSummaryDto[];
    groups?: BenchmarkRunGroupDto[];
    selectedRunIds?: number[];
    selectedGroupIds?: number[];
    comparabilityIndex?: BenchmarkComparabilityIndexDto | null;
    indexLoading?: boolean;
  } = {}): void {
    fixture.componentRef.setInput('runs', inputs.runs ?? runs(3));
    fixture.componentRef.setInput('groups', inputs.groups ?? [buildGroup()]);
    fixture.componentRef.setInput('selectedRunIds', inputs.selectedRunIds ?? []);
    fixture.componentRef.setInput('selectedGroupIds', inputs.selectedGroupIds ?? []);
    fixture.componentRef.setInput('comparabilityIndex', inputs.comparabilityIndex ?? null);
    fixture.componentRef.setInput('indexLoading', inputs.indexLoading ?? false);
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ComparisonSourcePickerComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(ComparisonSourcePickerComponent);
    component = fixture.componentInstance;
  });

  // -------------------------------------------------------------------------------------------
  // Selection
  // -------------------------------------------------------------------------------------------

  it('counts the whole selection, not the page, and says what is off-screen', () => {
    // Fifteen runs at the default page size of ten, with one selection on each page.
    render({ runs: runs(15), selectedRunIds: [1, 15] });

    expect(component.selectedCount).toBe(2);
    expect(fixture.debugElement.queryAll(By.css('tbody tr')).length).toBeLessThan(15);
    expect(component.offPageRunCount).toBeGreaterThan(0);
    const summary = (fixture.debugElement.query(By.css('.csp-selection-count'))
      .nativeElement as HTMLElement).textContent ?? '';
    expect(summary).toContain('2 runs selected');
    expect(summary).toContain('not on this page');
  });

  it('keeps a selected id selected when the table is filtered or paged', () => {
    render({ runs: runs(15), selectedRunIds: [15] });

    component.runTable.setFilter('testedModel', 'Model 1');
    fixture.detectChanges();
    expect(component.isRunSelected(15)).toBeTrue();

    component.runTable.setPage(2, component.runs);
    fixture.detectChanges();
    expect(component.selectedCount).toBe(1);
  });

  it('offers a run that is not completed disabled, with the reason as its accessible name', () => {
    render({
      runs: [buildRun({ id: 1 }), buildRun({ id: 2, status: 'Failed' })]
    });

    const boxes = fixture.debugElement.queryAll(By.css('tbody input[id^="csp-run-"]'));
    // Both rows are present: an operator who ran a benchmark should see why it cannot be charted.
    expect(boxes.length).toBe(2);

    const failed = boxes
      .map(element => element.nativeElement as HTMLInputElement)
      .find(element => element.disabled)!;
    expect(failed).withContext('the failed run must be disabled, not hidden').toBeTruthy();
    expect(failed.getAttribute('aria-label')).toContain('only a completed run can be compared');
  });

  it('refuses to select an unselectable run even if its handler is called directly', () => {
    const failed = buildRun({ id: 2, status: 'Failed' });
    render({ runs: [buildRun({ id: 1 }), failed] });
    let emitted = 0;
    component.selectionChange.subscribe(() => emitted++);

    component.toggleRun(failed);

    expect(emitted).toBe(0);
  });

  it('emits the whole selection on every toggle, so the host stays the owner', () => {
    render({ selectedRunIds: [1] });
    const emitted: ModelComparisonSelection[] = [];
    component.selectionChange.subscribe(value => emitted.push(value));

    component.toggleRun(component.runs[1]);
    component.toggleGroup(component.groups[0]);

    expect(emitted[0].runIds).toEqual([1, 2]);
    expect(emitted[1].groupIds).toEqual([1]);
  });

  // -------------------------------------------------------------------------------------------
  // The two caps
  // -------------------------------------------------------------------------------------------

  it('warns above the plot cap while leaving canCompare true for the wizard footer to read', () => {
    const nine = Array.from({ length: 9 }, (_unused, index) => index + 1);
    render({ runs: runs(9), selectedRunIds: nine });

    expect(component.overPlotCap).toBeTrue();
    expect(component.overSourceCap).toBeFalse();
    expect(component.canCompare).toBeTrue();
    expect((fixture.debugElement.query(By.css('.csp-cap-note'))
      .nativeElement as HTMLElement).textContent).toContain('plot at most 8');
  });

  it('flags canCompare false above the source cap and names the reason in its own alert', () => {
    const tooMany = Array.from({ length: MAX_COMPARISON_SOURCES + 1 }, (_unused, i) => i + 1);
    render({ runs: runs(MAX_COMPARISON_SOURCES + 1), selectedRunIds: tooMany });

    expect(component.overSourceCap).toBeTrue();
    expect(component.canCompare).toBeFalse();
    expect((fixture.debugElement.query(By.css('.alert-warning'))
      .nativeElement as HTMLElement).textContent).toContain(`at most ${MAX_COMPARISON_SOURCES}`);
  });

  // -------------------------------------------------------------------------------------------
  // Compare moved to the wizard footer
  // -------------------------------------------------------------------------------------------

  it('offers no Compare button of its own, and still emits clear on Clear selection', () => {
    render({ selectedRunIds: [1] });

    expect(fixture.debugElement.queryAll(By.css('button')).map(el => (el.nativeElement as HTMLElement).textContent)
      .some(text => (text ?? '').trim() === 'Compare')).toBeFalse();

    let cleared = 0;
    component.clear.subscribe(() => cleared++);
    const clearButton = fixture.debugElement.query(By.css('.csp-actions button'));
    (clearButton.nativeElement as HTMLButtonElement).click();

    expect(cleared).toBe(1);
  });

  // -------------------------------------------------------------------------------------------
  // Suite scope
  // -------------------------------------------------------------------------------------------

  it('owns the suite scope control and emits it, above both tables', () => {
    fixture.componentRef.setInput('suites', [{ id: 5, name: 'Suite A' }, { id: 6, name: 'Suite B' }]);
    render();
    const emitted: (number | null)[] = [];
    component.suiteIdChange.subscribe(value => emitted.push(value));

    const select = fixture.debugElement.query(By.css('#csp-suite'));
    expect(select).withContext('the suite scope select belongs to the picker').toBeTruthy();

    component.onSuiteChange(6);
    component.onSuiteChange(null);

    expect(emitted).toEqual([6, null]);
  });

  it('renders both empty states naming the fix rather than a bare "nothing here"', () => {
    render({ runs: [], groups: [] });

    const text = fixture.debugElement.queryAll(By.css('.text-muted'))
      .map(element => (element.nativeElement as HTMLElement).textContent ?? '').join(' ');
    expect(text).toContain('pick another suite scope');
    expect(text).toContain('run a benchmark');
    expect(text).toContain('build a group');
  });

  // -------------------------------------------------------------------------------------------
  // The comparability index
  // -------------------------------------------------------------------------------------------

  it("renders the run's and the group's condition label from the index", () => {
    render({ runs: runs(3), groups: [buildGroup({ id: 1 })], comparabilityIndex: buildIndex() });

    expect(component.conditionLabel('run:1')).toBe('Condition A');
    expect(component.conditionLabel('run:3')).toBe('Condition B');
    expect(component.conditionLabel('group:1')).toBe('Condition A');

    const runBadges = fixture.debugElement.queryAll(By.css('.csp-table')).map(table =>
      table.queryAll(By.css('.csp-condition')).map(el => (el.nativeElement as HTMLElement).textContent?.trim())
    );
    expect(runBadges[0]).toContain('Condition A');
    expect(runBadges[1]).toContain('Condition A');
  });

  it('narrows both tables through the condition filter', () => {
    render({ runs: runs(3), groups: [buildGroup({ id: 1 })], comparabilityIndex: buildIndex() });

    component.runTable.setFilter('condition', 'Condition B');
    component.groupTable.setFilter('condition', 'Condition B');
    fixture.detectChanges();

    expect(component.runTable.view(component.runs).map(r => r.id)).toEqual([3]);
    expect(component.groupTable.view(component.groups).length).toBe(0);
  });

  it('raises a role="note" alert once the selection spans two conditions', () => {
    render({
      runs: runs(3),
      selectedRunIds: [1, 3],
      comparabilityIndex: buildIndex()
    });

    expect(component.selectionSpansConditions).toBeTrue();
    const notice = fixture.debugElement.query(By.css('[role="note"]'));
    expect(notice).withContext('the incompatible-selection notice must be rendered').toBeTruthy();
    expect((notice.nativeElement as HTMLElement).textContent).toContain(component.incompatibleSelectionNotice);
    expect(component.incompatibleSelectionNotice).toContain('Condition A');
  });

  it('renders a muted dash and blocks nothing while the index is null', () => {
    render({ runs: [buildRun({ id: 1 }), buildRun({ id: 2, status: 'Failed' })], comparabilityIndex: null });

    expect(component.conditionLabel('run:1')).toBe('—');
    expect(component.conditionOrdinal('run:1')).toBeNull();
    expect(component.conditionTooltip('run:1')).toBe('');
    // The failed run is still unselectable for its own reason, not because of the index.
    expect(component.isRunSelectable(component.runs[0])).toBeTrue();
    expect(component.isRunSelectable(component.runs[1])).toBeFalse();
  });

  it('shows a muted dash while the index is loading, even if a stale index is present', () => {
    render({ comparabilityIndex: buildIndex(), indexLoading: true });

    expect(component.conditionLabel('run:1')).toBe('—');
  });

  it('filters the run table to the largest condition when showCompatibleRunsOnly is toggled on', () => {
    render({ runs: runs(3), comparabilityIndex: buildIndex() });

    expect(component.showCompatibleRunsOnly).toBeFalse();
    component.toggleShowCompatibleRunsOnly();
    fixture.detectChanges();

    expect(component.showCompatibleRunsOnly).toBeTrue();
    // The run table sorts by id descending by default, so the surviving rows come back 2 then 1.
    expect(component.runTable.view(component.runs).map(r => r.id)).toEqual([2, 1]);
  });
});
