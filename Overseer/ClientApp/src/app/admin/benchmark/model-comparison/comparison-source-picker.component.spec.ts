import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import {
  ComparisonSourcePickerComponent,
  GROUP_SECTION_TITLE,
  ModelComparisonSelection,
  RUN_SECTION_TITLE
} from './comparison-source-picker.component';
import type {
  BenchmarkRunGroupDto,
  BenchmarkRunSummaryDto
} from '../../../services/admin-benchmark.service';
import type {
  BenchmarkComparabilityIndexDto,
  BenchmarkComparabilityIndexEntryDto,
  BenchmarkComparabilityKeyValueDto
} from './model-comparison.models';

/** A single token with no break opportunity in it, which any narrower container must scroll. */
const UNBREAKABLE_TOKEN = 'x'.repeat(360);

/**
 * A value with no break opportunity in it, standing in for the serialized `CandidatePromptOptions`
 * the real index carries. Long enough that any container narrower than it must wrap or scroll.
 */
const UNBREAKABLE_VALUE = `{"systemPrompt":"${UNBREAKABLE_TOKEN}","temperature":0.2}`;

/** A full-length digest, so a test can tell an abbreviation from the value it stands for. */
const FULL_DIGEST = 'bb19dc24e287'.repeat(5) + 'abcd';

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

  /** One described must-match key of the reference condition, as the index sends it. */
  function buildKey(
    overrides: Partial<BenchmarkComparabilityKeyValueDto> = {}
  ): BenchmarkComparabilityKeyValueDto {
    return {
      name: 'serviceTier',
      label: 'Candidate service tier',
      description: 'A difference here means the runs were served at different priorities.',
      kind: 'Instrument',
      valueKind: 'Text',
      value: 'standard',
      displayValue: null,
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
        {
          ordinal: 1, label: 'Condition A', sourceCount: 3, runCount: 3,
          signature: 'sig-a', newestRunStartedAtUtc: '2026-09-07T18:22:00Z'
        },
        {
          ordinal: 2, label: 'Condition B', sourceCount: 1, runCount: 1,
          signature: 'sig-b', newestRunStartedAtUtc: '2026-09-06T09:10:00Z'
        }
      ],
      largestConditionKeys: [
        buildKey({
          name: 'BenchmarkSuiteId',
          label: 'Question suite',
          kind: 'Fundamental',
          valueKind: 'Identifier',
          value: '5',
          displayValue: 'NetHack Wiki Suite (#5)'
        }),
        buildKey({
          name: 'SuiteItemRevisions',
          label: 'Suite item revisions',
          kind: 'Fundamental',
          valueKind: 'List',
          value: '70:1,71:1'
        }),
        buildKey({
          name: 'CandidatePromptOptions',
          label: 'Candidate prompt options',
          kind: 'Instrument',
          valueKind: 'Json',
          value: UNBREAKABLE_VALUE
        }),
        buildKey({
          name: 'CandidateSystemPromptSha256',
          label: 'Candidate system prompt',
          kind: 'Instrument',
          valueKind: 'Hash',
          value: FULL_DIGEST
        }),
        buildKey({
          name: 'SecondOpinionConfiguration',
          label: 'Second opinion configuration',
          kind: 'Instrument',
          valueKind: 'List',
          value: '(none)'
        })
      ],
      referenceSelectionRule:
        'The reference condition is the one with the most sources; ties go to the most runs, then '
        + 'to the source offered first.',
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
  // Compare moved to the wizard footer
  // -------------------------------------------------------------------------------------------

  it('offers no Compare button of its own, and still emits clear on Clear selection', () => {
    render({ selectedRunIds: [1] });

    expect(fixture.debugElement.queryAll(By.css('button')).map(el => (el.nativeElement as HTMLElement).textContent)
      .some(text => (text ?? '').trim() === 'Compare')).toBeFalse();

    let cleared = 0;
    component.clear.subscribe(() => cleared++);
    const clearButton = fixture.debugElement.query(By.css('.csp-actions .btn-gh-cancel'));
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

  // -------------------------------------------------------------------------------------------
  // The condition legend
  // -------------------------------------------------------------------------------------------

  it('opens the condition legend as a modal dialog', () => {
    render({ comparabilityIndex: buildIndex() });
    const dialog = fixture.debugElement.query(By.css('dialog.csp-legend-dialog'))
      .nativeElement as HTMLDialogElement;
    const showModal = spyOn(dialog, 'showModal');

    (fixture.debugElement.query(By.css('.csp-actions .btn-gh'))
      .nativeElement as HTMLButtonElement).click();

    expect(showModal).toHaveBeenCalled();
  });

  it('renders every comparability key as its own chip', () => {
    const index = buildIndex();
    render({ comparabilityIndex: index });

    // Direct children only: the methods block and the other-conditions list reuse the same class
    // for their own values, nested inside a `<dd>` and an `<li>` respectively.
    const chips = fixture.debugElement.queryAll(By.css('.csp-legend-group > .csp-key-chips li'))
      .map(element => (element.nativeElement as HTMLElement).textContent?.trim() ?? '');

    expect(chips.length).toBe(
      index.mustMatchKeyNames.length + index.modelAxisKeyNames.length + index.degradingKeyNames.length);
    // A comma would mean a joined string was handed to the browser as one breakable-anywhere token.
    expect(chips.some(text => text.includes(','))).toBeFalse();
  });

  // -------------------------------------------------------------------------------------------
  // The reference condition, as a methods statement
  // -------------------------------------------------------------------------------------------

  describe('the reference-condition methods block', () => {
    /**
     * `navigator.clipboard` is a read-only accessor, so a copy case installs its own descriptor
     * and the restore below puts the real one back for every later spec in this browser.
     */
    const originalClipboard = Object.getOwnPropertyDescriptor(Navigator.prototype, 'clipboard')
      ?? Object.getOwnPropertyDescriptor(navigator, 'clipboard');

    function installClipboard(value: unknown): void {
      Object.defineProperty(navigator, 'clipboard', { value, configurable: true, writable: true });
    }

    afterEach(() => {
      delete (navigator as { clipboard?: unknown }).clipboard;
      if (originalClipboard) {
        Object.defineProperty(navigator, 'clipboard', originalClipboard);
      }
    });

    function textOf(selector: string): string {
      const element = fixture.debugElement.query(By.css(selector));
      return ((element?.nativeElement as HTMLElement | undefined)?.textContent ?? '').trim();
    }

    it('names the condition, its size, its newest run and the rule that chose it', () => {
      const index = buildIndex();
      render({ comparabilityIndex: index });

      expect(textOf('#csp-methods-heading')).toContain('Condition A');

      const facts = textOf('.csp-methods-facts');
      expect(facts).toContain('Sources');
      expect(facts).toContain('3');
      expect(facts).toContain('Newest run');
      // The signature is citable on screen at twelve characters, whatever its full length.
      expect(facts).toContain('sig-a');

      // The sentence is the server's, so the text an operator reads cannot drift from the
      // tie-break the bucketing applies.
      expect(textOf('.csp-methods-rule')).toBe(index.referenceSelectionRule);
    });

    it('gives each reference-condition key its own row, grouped by kind', () => {
      const index = buildIndex();
      render({ comparabilityIndex: index });

      const rows = fixture.debugElement.queryAll(By.css('.csp-legend-values dt'));
      expect(rows.length).toBe(index.largestConditionKeys.length);

      const titles = fixture.debugElement.queryAll(By.css('.csp-methods-kind h5'))
        .map(element => (element.nativeElement as HTMLElement).textContent?.trim());
      expect(titles).toEqual(['The exam', 'The apparatus']);

      // Every row carries the human label, the machine name and the one-line description: the
      // internal key name alone is what made the old block unreadable.
      const firstRow = (rows[0].nativeElement as HTMLElement).textContent ?? '';
      expect(firstRow).toContain('Question suite');
      expect(firstRow).toContain('BenchmarkSuiteId');
      expect(textOf('.csp-methods-key-note')).toContain('A difference here');
    });

    it('shows an identifier as the name the server knows for it', () => {
      render({ comparabilityIndex: buildIndex() });

      const values = fixture.debugElement.queryAll(By.css('.csp-legend-values dd code'))
        .map(element => (element.nativeElement as HTMLElement).textContent ?? '');
      expect(values.some(text => text.includes('NetHack Wiki Suite (#5)'))).toBeTrue();
    });

    it('shows a hash as twelve characters with the full digest behind a disclosure', () => {
      render({ comparabilityIndex: buildIndex() });

      expect(textOf('.csp-methods-hash code')).toBe(FULL_DIGEST.slice(0, 12));

      const disclosure = fixture.debugElement.query(By.css('details.csp-methods-disclosure'));
      expect(disclosure).withContext('the full digest must remain reachable by hand').toBeTruthy();
      expect(textOf('details.csp-methods-disclosure code')).toBe(FULL_DIGEST);
    });

    it('keeps the prompt-options value inside its own code scroller', () => {
      render({ comparabilityIndex: buildIndex() });

      const values = fixture.debugElement.queryAll(By.css('.csp-legend-values dd code'))
        .map(element => (element.nativeElement as HTMLElement).textContent ?? '');
      expect(values.some(text => text.includes(UNBREAKABLE_TOKEN)))
        .withContext('the prompt-options value must render inside its own code box').toBeTrue();
      // Pretty-printed rather than the minified blob the wire carries.
      expect(values.some(text => text.includes('"temperature": 0.2'))).toBeTrue();
    });

    it('renders a JSON value that does not parse as the raw string', () => {
      expect(component.formatJson('{ not json')).toBe('{ not json');
    });

    it('splits a list value into one chip per element', () => {
      render({ comparabilityIndex: buildIndex() });

      const chips = fixture.debugElement.queryAll(By.css('.csp-legend-values dd .csp-key-chips li'))
        .map(element => (element.nativeElement as HTMLElement).textContent?.trim());
      expect(chips).toContain('70:1');
      expect(chips).toContain('71:1');
    });

    it('renders an absent value as a dash rather than as the word "(none)"', () => {
      render({ comparabilityIndex: buildIndex() });

      const none = fixture.debugElement.query(By.css('.csp-methods-none'));
      expect(none).toBeTruthy();
      expect((none.nativeElement as HTMLElement).textContent).toContain('—');
      expect((none.nativeElement as HTMLElement).querySelector('.visually-hidden')?.textContent)
        .toBe('no value');
      expect(component.isNoValue('(none)')).toBeTrue();
    });

    it('copies the methods statement with full values, never the abbreviations', async () => {
      const writeText = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
      installClipboard({ writeText });
      render({ comparabilityIndex: buildIndex() });

      await component.copyMethodsStatement();

      const written = writeText.calls.mostRecent().args[0] as string;
      expect(written).toContain('Reference condition: Condition A (3 sources, 3 runs');
      expect(written).toContain('Signature: sig-a');
      expect(written).toContain('The exam');
      expect(written).toContain('The apparatus');
      // The whole use of the block is being pasted somewhere, so it carries the values in full.
      expect(written).toContain(FULL_DIGEST);
      expect(written).toContain(UNBREAKABLE_TOKEN);
      expect(component.methodsCopyState).toContain('copied');
    });

    it('copies a hash row in full rather than the twelve characters it shows', async () => {
      const writeText = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
      installClipboard({ writeText });
      const index = buildIndex();
      render({ comparabilityIndex: index });

      const hashKey = index.largestConditionKeys.find(key => key.valueKind === 'Hash')!;
      await component.copyFullValue(hashKey);

      expect(writeText).toHaveBeenCalledWith(FULL_DIGEST);
    });

    it('says what to do instead when the clipboard refuses the write', async () => {
      installClipboard(undefined);
      render({ comparabilityIndex: buildIndex() });

      await component.copyMethodsStatement();

      expect(component.methodsCopyState).toBe('Copy failed — select the text instead.');
    });
  });

  // -------------------------------------------------------------------------------------------
  // The conditions the figures are not measured under
  // -------------------------------------------------------------------------------------------

  it('lists every non-reference condition with its size and the keys it differs on', () => {
    render({ comparabilityIndex: buildIndex() });

    expect(component.otherConditions.length).toBe(1);
    expect(component.otherConditions[0].condition.label).toBe('Condition B');

    const others = fixture.debugElement.queryAll(By.css('.csp-others > li'))
      .map(element => (element.nativeElement as HTMLElement).textContent ?? '');
    expect(others.length).toBe(1);
    expect(others[0]).toContain('Condition B');
    expect(others[0]).toContain('1 source');
    expect(others[0]).toContain('1 run');
    // The differing key is named by its own label where the reference condition describes it, so
    // the reader sees what switching would change rather than a bare internal name.
    expect(others[0]).toContain('serviceTier');
  });

  it('offers no other-conditions section when everything is in one condition', () => {
    const index = buildIndex({
      conditions: [{
        ordinal: 1, label: 'Condition A', sourceCount: 3, runCount: 3,
        signature: 'sig-a', newestRunStartedAtUtc: '2026-09-07T18:22:00Z'
      }]
    });
    render({ comparabilityIndex: index });

    expect(component.otherConditions).toEqual([]);
    expect(fixture.debugElement.query(By.css('#csp-others-heading'))).toBeNull();
  });

  it('closes the legend on a backdrop click where closedby is unsupported', () => {
    render({ comparabilityIndex: buildIndex() });
    const dialog = fixture.debugElement.query(By.css('dialog.csp-legend-dialog'))
      .nativeElement as HTMLDialogElement;
    const close = spyOn(dialog, 'close');
    // The dialog is closed, so its rect is empty and every coordinate is outside it.
    const outside = { target: dialog, currentTarget: dialog, clientX: -50, clientY: -50 } as unknown as MouseEvent;

    component.onLegendDialogClick(outside);

    if ('closedBy' in HTMLDialogElement.prototype) {
      // The browser's own light dismiss owns this; the handler must not close it a second time.
      expect(close).not.toHaveBeenCalled();
    } else {
      expect(close).toHaveBeenCalled();
      close.calls.reset();
      const rect = dialog.getBoundingClientRect();
      const inside = {
        target: dialog,
        currentTarget: dialog,
        clientX: rect.left + rect.width / 2,
        clientY: rect.top + rect.height / 2
      } as unknown as MouseEvent;

      component.onLegendDialogClick(inside);

      expect(close).not.toHaveBeenCalled();
    }
  });

  it('keeps the legend close event off the wizard', () => {
    const event = new Event('close');
    const stopPropagation = spyOn(event, 'stopPropagation');

    component.onLegendDialogClose(event);

    expect(stopPropagation).toHaveBeenCalled();
  });

  it('titles both source sections from the exported constants', () => {
    render();

    expect((fixture.debugElement.query(By.css('#csp-runs-heading'))
      .nativeElement as HTMLElement).textContent?.trim()).toBe(RUN_SECTION_TITLE);
    expect((fixture.debugElement.query(By.css('#csp-groups-heading'))
      .nativeElement as HTMLElement).textContent?.trim()).toBe(GROUP_SECTION_TITLE);
  });
});
