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
import { conditionDetailFor } from './model-comparison.models';
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
      speedCalibration: 'speed-a',
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

  it('offers neither a Compare nor a Clear selection button of its own', () => {
    render({ selectedRunIds: [1] });

    // The table's first sortable header is also labelled Compare; only actions count here.
    const texts = fixture.debugElement.queryAll(By.css('button'))
      .map(el => el.nativeElement as HTMLElement)
      .filter(el => el.closest('th') === null)
      .map(el => (el.textContent ?? '').trim());

    expect(texts).not.toContain('Compare');
    expect(texts).not.toContain('Clear selection');
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

    const runsText = (fixture.debugElement.query(By.css('.text-muted'))
      .nativeElement as HTMLElement).textContent ?? '';
    expect(runsText).toContain('Choose another suite');
    expect(runsText).toContain('run a benchmark');

    component.selectSourceTab('groups');
    fixture.detectChanges();
    const groupsText = (fixture.debugElement.query(By.css('.text-muted'))
      .nativeElement as HTMLElement).textContent ?? '';
    expect(groupsText).toContain('Choose another suite');
    expect(groupsText).toContain('build a group');
  });

  // -------------------------------------------------------------------------------------------
  // The comparability index
  // -------------------------------------------------------------------------------------------

  it("renders the run's and the group's condition label from the index", () => {
    render({ runs: runs(3), groups: [buildGroup({ id: 1 })], comparabilityIndex: buildIndex() });

    expect(component.conditionLabel('run:1')).toBe('Condition A');
    expect(component.conditionLabel('run:3')).toBe('Condition B');
    expect(component.conditionLabel('group:1')).toBe('Condition A');

    const runBadges = fixture.debugElement.queryAll(By.css('.csp-table .csp-condition'))
      .map(el => (el.nativeElement as HTMLElement).textContent?.trim());
    expect(runBadges).toContain('Condition A');

    component.selectSourceTab('groups');
    fixture.detectChanges();
    const groupBadges = fixture.debugElement.queryAll(By.css('.csp-table .csp-condition'))
      .map(el => (el.nativeElement as HTMLElement).textContent?.trim());
    expect(groupBadges).toContain('Condition A');
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
    expect(conditionDetailFor(component.comparabilityIndex, 'run:1', [1])).toBeNull();
    expect(component.hasConditionDetail('run:1')).toBeFalse();
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
  // The condition detail dialog
  // -------------------------------------------------------------------------------------------

  describe('the condition detail dialog', () => {
    function conditionDialog(): HTMLDialogElement {
      return fixture.debugElement.query(By.css('dialog.csp-condition-dialog'))
        .nativeElement as HTMLDialogElement;
    }

    /** The row controls actually offered: only a source with something to say carries one. */
    function detailButtons(): HTMLButtonElement[] {
      return fixture.debugElement.queryAll(By.css('.csp-condition-detail'))
        .map(element => element.nativeElement as HTMLButtonElement);
    }

    /**
     * Intercepts the save path rather than the module that performs it: the object URL names the
     * blob that was written and the anchor names the file it was written under, which between them
     * are everything a download can be asserted on without a real file system.
     */
    function captureSaves(): { blobs: Blob[]; names: string[] } {
      const saved: { blobs: Blob[]; names: string[] } = { blobs: [], names: [] };
      spyOn(URL, 'createObjectURL').and.callFake((source: Blob | MediaSource) => {
        saved.blobs.push(source as Blob);
        return 'blob:comparison-source-picker-test';
      });
      spyOn(URL, 'revokeObjectURL').and.stub();
      spyOn(HTMLAnchorElement.prototype, 'click').and.callFake(function (this: HTMLAnchorElement) {
        saved.names.push(this.download);
      });
      return saved;
    }

    it('offers the control only where the source differs from the reference condition', () => {
      render({ runs: runs(3), groups: [buildGroup({ id: 1 })], comparabilityIndex: buildIndex() });

      // Runs 1 and 2 and the group are the reference condition itself; run 3 is not.
      expect(detailButtons().length).toBe(1);
      expect(detailButtons()[0].getAttribute('aria-label')).toBe('Comparability detail for run 3');
      expect(component.hasConditionDetail('run:1')).toBeFalse();
      expect(component.hasConditionDetail('run:3')).toBeTrue();
    });

    it('opens the dialog populated with that entry\'s rows', () => {
      render({ runs: runs(3), comparabilityIndex: buildIndex() });
      const showModal = spyOn(conditionDialog(), 'showModal');

      detailButtons()[0].click();
      fixture.detectChanges();

      expect(showModal).toHaveBeenCalled();
      expect(component.conditionDetail?.sourceLabel).toBe('Run 3');
      expect(component.conditionDetailTitle).toBe('Run 3 — Condition B');
      // Full-screen, like the wizard it is nested in — its backdrop is too thin to be an honest
      // click target, so it carries no closedby and no light-dismiss handler.
      expect(conditionDialog().classList.contains('gh-dialog-fullscreen')).toBeTrue();
      expect(conditionDialog().hasAttribute('closedby')).toBeFalse();

      const rows = fixture.debugElement
        .queryAll(By.css('dialog.csp-condition-dialog tbody tr'))
        .map(element => (element.nativeElement as HTMLElement).textContent ?? '');
      expect(rows.length).toBe(1);
      expect(rows[0]).toContain('serviceTier');
      // Both sides of the difference, attributed from the variant run ids.
      expect(rows[0]).toContain('priority');
      expect(rows[0]).toContain('standard');
    });

    it('copies the detail as Markdown with the values in full', async () => {
      const writeText = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
      Object.defineProperty(navigator, 'clipboard', {
        value: { writeText }, configurable: true, writable: true
      });
      try {
        render({ runs: runs(3), comparabilityIndex: buildIndex() });
        spyOn(conditionDialog(), 'showModal');
        detailButtons()[0].click();

        await component.copyConditionDetail();

        const written = writeText.calls.mostRecent().args[0] as string;
        expect(written).toContain('Run 3 — Condition B');
        expect(written).toContain('| serviceTier');
        expect(written).toContain('priority');
        expect(component.conditionCopyState).toContain('copied');
      } finally {
        delete (navigator as { clipboard?: unknown }).clipboard;
      }
    });

    it('downloads the detail as Markdown, named after the source', () => {
      const index = buildIndex({
        entries: [
          ...buildIndex().entries,
          buildIndexEntry({
            key: 'run:46',
            sourceId: 46,
            conditionOrdinal: 2,
            conditionLabel: 'Condition B',
            signature: 'sig-b',
            differencesFromLargest: [{
              name: 'serviceTier',
              kind: 'MustMatch',
              description: 'Service tier differs from the largest condition',
              variants: [{ value: 'priority', runIds: [46] }, { value: 'standard', runIds: [1, 2] }]
            }]
          })
        ]
      });
      render({ runs: [...runs(3), buildRun({ id: 46 })], comparabilityIndex: index });
      spyOn(conditionDialog(), 'showModal');
      // The run table sorts by id descending by default, so run 46 renders — and opens — first.
      detailButtons()[0].click();
      fixture.detectChanges();
      const saved = captureSaves();

      component.downloadConditionDetail();

      expect(saved.blobs.length).toBe(1);
      expect(saved.blobs[0].type).toContain('text/markdown');
      expect(saved.names[0]).toMatch(/^comparability_run-46_\d{8}_\d{6}\.md$/);
      expect(component.conditionCopyState).toContain('Saved as');
    });

    it('closes on Escape and returns focus to the control that opened it', async () => {
      render({ runs: runs(3), comparabilityIndex: buildIndex() });
      const dialog = conditionDialog();
      const trigger = detailButtons()[0];

      trigger.click();
      fixture.detectChanges();
      expect(dialog.open).toBeTrue();

      // A synthetic key event cannot drive a dialog's own close request, so Escape is exercised as
      // the two events it produces: a cancel the handler must not prevent, then the close itself.
      const cancel = new Event('cancel', { cancelable: true });
      dialog.dispatchEvent(cancel);
      expect(cancel.defaultPrevented)
        .withContext('a refused close request leaves the reader trapped').toBeFalse();

      dialog.close();
      // `close` is fired from a queued element task, so it has not run yet.
      await new Promise<void>(resolve => setTimeout(resolve, 0));

      expect(dialog.open).toBeFalse();
      expect(document.activeElement).toBe(trigger);
    });

    it('renders the keys a self-inconsistent source disagrees with itself on', () => {
      const index = buildIndex({
        entries: [
          ...buildIndex().entries,
          buildIndexEntry({
            key: 'group:2',
            sourceKind: 'Group',
            sourceId: 2,
            conditionOrdinal: 0,
            conditionLabel: 'Self-inconsistent',
            signature: '',
            selfInconsistent: true,
            selfInconsistentKeys: ['BenchmarkSuiteId', 'CandidateModelId']
          })
        ]
      });
      // No runs, so the group's own control is the only one on the groups tab.
      render({ runs: [], groups: [buildGroup({ id: 2 })], comparabilityIndex: index });
      component.selectSourceTab('groups');
      fixture.detectChanges();
      spyOn(conditionDialog(), 'showModal');

      detailButtons()[0].click();
      fixture.detectChanges();

      expect(component.conditionDetail?.selfInconsistent).toBeTrue();
      const chips = fixture.debugElement
        .queryAll(By.css('dialog.csp-condition-dialog .csp-key-chips li'))
        .map(element => (element.nativeElement as HTMLElement).textContent?.trim());
      expect(chips).toEqual(['BenchmarkSuiteId', 'CandidateModelId']);
      // A source in no condition differs from the reference on nothing named, so no table is drawn.
      expect(fixture.debugElement.query(By.css('dialog.csp-condition-dialog tbody'))).toBeNull();
    });

    it('keeps the detail dialog close event off the wizard', () => {
      const event = new Event('close');
      const stopPropagation = spyOn(event, 'stopPropagation');

      component.onConditionDialogClose(event);

      expect(stopPropagation).toHaveBeenCalled();
    });
  });

  // -------------------------------------------------------------------------------------------
  // About conditions
  // -------------------------------------------------------------------------------------------

  function legendDialog(): HTMLDialogElement {
    return fixture.debugElement.query(By.css('dialog.csp-legend-dialog'))
      .nativeElement as HTMLDialogElement;
  }

  /** Every legend spec opens the dialog first: nothing of its body renders while it is closed. */
  function openLegend(): void {
    component.openLegend();
    fixture.detectChanges();
  }

  function conditionItems(): HTMLDetailsElement[] {
    return fixture.debugElement.queryAll(By.css('details.csp-cond-item'))
      .map(element => element.nativeElement as HTMLDetailsElement);
  }

  /** The accordion bodies actually in the DOM; the rule cards' disclosures carry the class too. */
  function conditionBodies(): HTMLElement[] {
    return fixture.debugElement.queryAll(By.css('details.csp-cond-item > .gh-disclosure-body'))
      .map(element => element.nativeElement as HTMLElement);
  }

  function toggle(item: HTMLDetailsElement, newState: 'open' | 'closed'): void {
    item.dispatchEvent(new ToggleEvent('toggle', {
      newState,
      oldState: newState === 'open' ? 'closed' : 'open'
    }));
    fixture.detectChanges();
  }

  /** The reference condition plus `count` others, each a day older than the one before. */
  function indexWithOthers(count: number): BenchmarkComparabilityIndexDto {
    const conditions = [buildIndex().conditions[0]];
    for (let i = 0; i < count; i++) {
      const ordinal = i + 2;
      conditions.push({
        ordinal,
        label: `Condition ${String.fromCharCode(64 + ordinal)}`,
        sourceCount: 1,
        runCount: 1,
        signature: `sig-${ordinal}`,
        newestRunStartedAtUtc: `2026-08-${String(28 - i).padStart(2, '0')}T10:00:00Z`
      });
    }
    return buildIndex({ conditions });
  }

  it('opens About conditions as a modal dialog from its trigger', () => {
    render({ comparabilityIndex: buildIndex() });
    const showModal = spyOn(legendDialog(), 'showModal');

    (fixture.debugElement.query(By.css('#csp-legend-trigger'))
      .nativeElement as HTMLButtonElement).click();

    expect(showModal).toHaveBeenCalled();
    expect(component.legendOpen).toBeTrue();
    // Full-screen, so its backdrop is only a thin ring: no light dismiss.
    expect(legendDialog().classList.contains('gh-dialog-fullscreen')).toBeTrue();
    expect(legendDialog().hasAttribute('closedby')).toBeFalse();
    expect((fixture.debugElement.query(By.css('#csp-legend-title')).nativeElement as HTMLElement)
      .textContent?.trim()).toBe('About conditions');
  });

  it('renders nothing of its body while closed, and drops it again on close', async () => {
    render({ comparabilityIndex: buildIndex() });
    expect(fixture.debugElement.query(By.css('.csp-legend-inner'))).toBeNull();

    openLegend();
    expect(fixture.debugElement.query(By.css('.csp-legend-inner'))).toBeTruthy();

    legendDialog().close();
    // `close` is fired from a queued element task, so it has not run yet.
    await new Promise<void>(resolve => setTimeout(resolve, 0));
    fixture.detectChanges();

    expect(component.legendOpen).toBeFalse();
    expect(fixture.debugElement.query(By.css('.csp-legend-inner'))).toBeNull();
    expect(document.activeElement?.id).toBe('csp-legend-trigger');
  });

  it('renders every comparability key as its own chip, under Technical names', () => {
    const index = buildIndex();
    render({ comparabilityIndex: index });
    openLegend();

    const chips = fixture.debugElement.queryAll(By.css('.csp-rule-keys .csp-key-chips li'))
      .map(element => (element.nativeElement as HTMLElement).textContent?.trim() ?? '');

    expect(chips.length).toBe(
      index.mustMatchKeyNames.length + index.modelAxisKeyNames.length + index.degradingKeyNames.length);
    // A comma would mean a joined string was handed to the browser as one breakable-anywhere token.
    expect(chips.some(text => text.includes(','))).toBeFalse();
  });

  it('pins the charted condition first, tagged and open, and lists the others newest first', () => {
    const index = buildIndex({
      conditions: [
        ...buildIndex().conditions,
        {
          ordinal: 3, label: 'Condition C', sourceCount: 1, runCount: 2,
          signature: 'sig-c', newestRunStartedAtUtc: '2026-09-08T09:00:00Z'
        }
      ]
    });
    render({ comparabilityIndex: index });
    openLegend();

    const items = conditionItems();
    const summaries = items.map(item => item.querySelector('summary')?.textContent ?? '');
    // Condition C's newest run is newer than the charted one's; it still comes after it.
    expect(summaries.map(text => text.match(/Condition [A-Z]/)?.[0]))
      .toEqual(['Condition A', 'Condition C', 'Condition B']);
    expect(items[0].querySelector('.csp-cond-charted')?.textContent?.trim()).toBe('Charted');
    expect(items[1].querySelector('.csp-cond-charted')).toBeNull();
    expect(items[0].open).toBeTrue();
    expect(component.openConditionOrdinal).toBe(1);
    expect(summaries[2]).toContain('differs on 1 setting');
  });

  it('keeps one body in the DOM, and moves it with the exclusive accordion', () => {
    render({ comparabilityIndex: buildIndex() });
    openLegend();
    const [reference, other] = conditionItems();

    expect(conditionItems().every(item => item.getAttribute('name') === 'csp-conditions')).toBeTrue();
    expect(conditionBodies().length).toBe(1);
    expect(reference.contains(conditionBodies()[0])).toBeTrue();

    toggle(other, 'open');
    // The closing item's event arrives after the opening one's and must not clear the new state.
    toggle(reference, 'closed');

    expect(component.openConditionOrdinal).toBe(2);
    expect(conditionBodies().length).toBe(1);
    expect(other.contains(conditionBodies()[0])).toBeTrue();

    toggle(other, 'closed');
    expect(component.openConditionOrdinal).toBeNull();
    expect(conditionBodies().length).toBe(0);
  });

  it('lists five more conditions per Show more and moves focus to the first new one', () => {
    render({ comparabilityIndex: indexWithOthers(8) });
    openLegend();

    expect(conditionItems().length).toBe(6);
    const count = () => (fixture.debugElement.query(By.css('.csp-cond-count'))
      .nativeElement as HTMLElement).textContent?.trim();
    expect(count()).toBe('Showing 6 of 9 conditions');

    const more = fixture.debugElement.query(By.css('.csp-cond-pager button'))
      .nativeElement as HTMLButtonElement;
    expect(more.textContent?.trim()).toBe('Show 3 more');
    const firstHidden = component.legend.others[5].condition.ordinal;

    more.click();
    fixture.detectChanges();

    expect(conditionItems().length).toBe(9);
    expect(count()).toBe('Showing 9 of 9 conditions');
    expect(fixture.debugElement.query(By.css('.csp-cond-pager button'))).toBeNull();
    expect(document.activeElement?.id).toBe(`csp-cond-summary-${firstHidden}`);
  });

  it('offers no pager while the other conditions fit on one page', () => {
    render({ comparabilityIndex: indexWithOthers(5) });
    openLegend();

    expect(conditionItems().length).toBe(6);
    expect(fixture.debugElement.query(By.css('.csp-cond-pager'))).toBeNull();
  });

  it("reads another condition's differences charted value first, and opens its full detail", () => {
    render({ runs: runs(3), comparabilityIndex: buildIndex() });
    openLegend();
    const other = conditionItems()[1];
    toggle(other, 'open');

    const body = conditionBodies()[0];
    const text = body.textContent ?? '';
    expect(text).toContain('What is different from Condition A');
    const line = body.querySelector('.csp-diff-list > li')?.textContent ?? '';
    expect(line).toContain('serviceTier');
    expect(line.indexOf('standard')).toBeGreaterThan(-1);
    expect(line.indexOf('standard')).toBeLessThan(line.indexOf('priority'));
    expect(line).toContain('changed to');
    expect(body.querySelector('.csp-cond-members')?.textContent?.trim()).toBe('Run 3');

    const detailDialog = fixture.debugElement.query(By.css('dialog.csp-condition-dialog'))
      .nativeElement as HTMLDialogElement;
    const showModal = spyOn(detailDialog, 'showModal');
    const fullDetail = body.querySelector('.csp-cond-actions button') as HTMLButtonElement;
    expect(fullDetail.getAttribute('aria-label')).toBe('Full detail for Condition B');

    fullDetail.click();

    expect(showModal).toHaveBeenCalled();
    expect(component.conditionDetail?.sourceLabel).toBe('Run 3');
  });

  it('keeps technical details off on every open', () => {
    render({ comparabilityIndex: buildIndex() });
    openLegend();
    expect(component.showTechnicalDetails).toBeFalse();
    expect(fixture.debugElement.query(By.css('.csp-methods-key'))).toBeNull();

    component.toggleTechnicalDetails();
    expect(fixture.debugElement.query(By.css('.csp-methods-key'))).toBeTruthy();
    expect((fixture.debugElement.query(By.css('.csp-conditions-head .gh-filter-toggle'))
      .nativeElement as HTMLButtonElement).getAttribute('aria-pressed')).toBe('true');

    component.onLegendDialogClose(new Event('close'));
    openLegend();

    expect(component.showTechnicalDetails).toBeFalse();
    expect(fixture.debugElement.query(By.css('.csp-methods-key'))).toBeNull();
  });

  it('rebuilds its entry lookup when the index is replaced', () => {
    render({ comparabilityIndex: buildIndex() });
    expect(component.conditionLabel('run:3')).toBe('Condition B');

    const replaced = buildIndex({
      entries: buildIndex().entries.map(entry => entry.key === 'run:3'
        ? { ...entry, conditionLabel: 'Condition Z' }
        : entry)
    });
    fixture.componentRef.setInput('comparabilityIndex', replaced);
    fixture.detectChanges();

    expect(component.conditionLabel('run:3')).toBe('Condition Z');
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

    /** The legend open on the reference condition, which is where the methods block renders. */
    function renderLegend(index: BenchmarkComparabilityIndexDto = buildIndex()): void {
      render({ comparabilityIndex: index });
      openLegend();
    }

    it('names the condition, its size, its newest run and the rule that chose it', () => {
      const index = buildIndex();
      renderLegend(index);

      expect(textOf('.csp-legend-charted .alert-heading')).toBe('The charts use Condition A');

      const facts = textOf('.csp-methods-facts');
      expect(facts).toContain('Sources');
      expect(facts).toContain('3');
      expect(facts).toContain('Newest run');
      // The signature is citable on screen at twelve characters, whatever its full length.
      expect(facts).toContain('sig-a');

      // The sentence is the server's, so the text an operator reads cannot drift from the
      // tie-break the bucketing applies.
      expect(textOf('.csp-methods-rule')).toContain(index.referenceSelectionRule);
    });

    it('gives each reference-condition key its own row, grouped by kind', () => {
      const index = buildIndex();
      renderLegend(index);

      const rows = fixture.debugElement.queryAll(By.css('.csp-methods-grid dt'));
      expect(rows.length).toBe(index.largestConditionKeys.length);

      const titles = fixture.debugElement.queryAll(By.css('.csp-methods-kind h5'))
        .map(element => (element.nativeElement as HTMLElement).textContent?.trim());
      expect(titles).toEqual(['The exam', 'The apparatus']);

      // The human label by default; the machine name and the one-line description are technical
      // details, one toggle away.
      expect((rows[0].nativeElement as HTMLElement).textContent).toContain('Question suite');
      expect((rows[0].nativeElement as HTMLElement).textContent).not.toContain('BenchmarkSuiteId');

      component.toggleTechnicalDetails();
      const firstRow = (fixture.debugElement.queryAll(By.css('.csp-methods-grid dt'))[0]
        .nativeElement as HTMLElement).textContent ?? '';
      expect(firstRow).toContain('BenchmarkSuiteId');
      expect(textOf('.csp-methods-key-note')).toContain('A difference here');
    });

    it('shows an identifier as the name the server knows for it', () => {
      renderLegend();

      const values = fixture.debugElement.queryAll(By.css('.csp-legend-values dd code'))
        .map(element => (element.nativeElement as HTMLElement).textContent ?? '');
      expect(values.some(text => text.includes('NetHack Wiki Suite (#5)'))).toBeTrue();
    });

    it('shows a hash as twelve characters, with the full digest a technical detail', () => {
      renderLegend();

      expect(textOf('.csp-methods-grid .csp-methods-hash code')).toBe(FULL_DIGEST.slice(0, 12));
      expect(fixture.debugElement.query(By.css('details.csp-methods-disclosure'))).toBeNull();

      component.toggleTechnicalDetails();

      const disclosure = fixture.debugElement.query(By.css('details.csp-methods-disclosure'));
      expect(disclosure).withContext('the full digest must remain reachable by hand').toBeTruthy();
      expect(textOf('details.csp-methods-disclosure code')).toBe(FULL_DIGEST);
    });

    it('keeps the prompt-options value inside its own code scroller', () => {
      renderLegend();

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
      renderLegend();

      const chips = fixture.debugElement.queryAll(By.css('.csp-legend-values dd .csp-key-chips li'))
        .map(element => (element.nativeElement as HTMLElement).textContent?.trim());
      expect(chips).toContain('70:1');
      expect(chips).toContain('71:1');
    });

    it('renders an absent value as a dash rather than as the word "(none)"', () => {
      renderLegend();

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
      renderLegend();
      const footerButtons = fixture.debugElement
        .queryAll(By.css('dialog.csp-legend-dialog .dialog-actions button'))
        .map(element => (element.nativeElement as HTMLButtonElement).textContent?.trim());
      expect(footerButtons).toEqual(['Close', 'Copy methods statement']);

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

    it('copies a condition signature in full', async () => {
      const writeText = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
      installClipboard({ writeText });
      render({ comparabilityIndex: buildIndex() });

      await component.copySignature(buildIndex().conditions[0]);

      expect(writeText).toHaveBeenCalledWith('sig-a');
      expect(component.methodsCopyState).toContain('signature copied');
    });

    it('says what to do instead when the clipboard refuses the write', async () => {
      installClipboard(undefined);
      render({ comparabilityIndex: buildIndex() });

      await component.copyMethodsStatement();

      expect(component.methodsCopyState).toBe('Copy failed — select the text instead.');
    });
  });

  it('keeps the legend close event off the wizard', () => {
    const event = new Event('close');
    const stopPropagation = spyOn(event, 'stopPropagation');

    component.onLegendDialogClose(event);

    expect(stopPropagation).toHaveBeenCalled();
  });

  // -------------------------------------------------------------------------------------------
  // The source-kind tabs
  // -------------------------------------------------------------------------------------------

  describe('the source-kind tabs', () => {
    function tabButtons(): HTMLButtonElement[] {
      return fixture.debugElement.queryAll(By.css('.csp-source-tabs .gh-tab'))
        .map(element => element.nativeElement as HTMLButtonElement);
    }

    it('titles both kind tabs from the exported constants', () => {
      render();
      const [runsTab, groupsTab] = tabButtons();

      expect(runsTab.textContent).toContain(RUN_SECTION_TITLE);
      expect(groupsTab.textContent).toContain(GROUP_SECTION_TITLE);
    });

    it("renders only the active kind's table, and keeps each table's own page across a tab switch", () => {
      render({ runs: runs(15), groups: [buildGroup({ id: 1 })] });

      expect(fixture.debugElement.queryAll(By.css('table.csp-table')).length).toBe(1);
      expect(fixture.debugElement.query(By.css('#csp-src-panel-runs'))).toBeTruthy();
      expect(fixture.debugElement.query(By.css('#csp-src-panel-groups'))).toBeFalsy();

      component.runTable.setPage(2, component.runs);
      fixture.detectChanges();

      tabButtons()[1].click();
      fixture.detectChanges();

      expect(fixture.debugElement.queryAll(By.css('table.csp-table')).length).toBe(1);
      expect(fixture.debugElement.query(By.css('#csp-src-panel-groups'))).toBeTruthy();
      expect(fixture.debugElement.query(By.css('#csp-src-panel-runs'))).toBeFalsy();

      tabButtons()[0].click();
      fixture.detectChanges();

      // The TableState lives on the component, not the template, so it survives the panel
      // being removed from the DOM and rendered again.
      expect(component.runTable.page).toBe(2);
    });

    it('puts a pager above and below each kind table, and only one of them announces', () => {
      render({ runs: runs(15), groups: [buildGroup({ id: 1 })] });

      const expectPagerPair = (): void => {
        const pagers = fixture.debugElement.queryAll(By.css('app-table-pager'));
        expect(pagers.length).toBe(2);
        const statuses = pagers.map(pager =>
          pager.query(By.css('.gh-pager-status')).nativeElement as HTMLElement);
        expect(statuses.filter(status => status.getAttribute('role') === 'status').length).toBe(1);
        expect(statuses.filter(status => status.getAttribute('aria-hidden') === 'true').length).toBe(1);
      };

      expectPagerPair();

      tabButtons()[1].click();
      fixture.detectChanges();

      expectPagerPair();
    });

    it('moves between the kind tabs with the arrow keys, wrapping, and focus follows', () => {
      render();
      const runsTab = tabButtons()[0];
      runsTab.focus();

      component.onSourceTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }), 0);
      fixture.detectChanges();
      expect(component.activeSourceTab).toBe('groups');
      expect(document.activeElement).toBe(fixture.nativeElement.querySelector('#csp-src-tab-groups'));

      // Two tabs: a second ArrowRight wraps back to the first.
      component.onSourceTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }), 1);
      fixture.detectChanges();
      expect(component.activeSourceTab).toBe('runs');
      expect(document.activeElement).toBe(fixture.nativeElement.querySelector('#csp-src-tab-runs'));

      component.onSourceTabKeydown(new KeyboardEvent('keydown', { key: 'End' }), 0);
      fixture.detectChanges();
      expect(component.activeSourceTab).toBe('groups');

      component.onSourceTabKeydown(new KeyboardEvent('keydown', { key: 'Home' }), 1);
      fixture.detectChanges();
      expect(component.activeSourceTab).toBe('runs');
    });

    it('gives exactly one tab a roving tabindex of 0, and marks it aria-selected', () => {
      render();
      const buttons = tabButtons();

      expect(buttons.filter(b => b.getAttribute('tabindex') === '0').length).toBe(1);
      expect(buttons.filter(b => b.getAttribute('tabindex') === '-1').length).toBe(1);
      expect(buttons.find(b => b.id === 'csp-src-tab-runs')?.getAttribute('aria-selected')).toBe('true');
      expect(buttons.find(b => b.id === 'csp-src-tab-groups')?.getAttribute('aria-selected')).toBe('false');
    });

    it('shows the selected count on each tab, only where it has a selection', () => {
      render({ selectedRunIds: [1, 2], selectedGroupIds: [] });
      const buttons = tabButtons();

      const runsBadge = buttons.find(b => b.id === 'csp-src-tab-runs')
        ?.querySelector('.csp-tab-selected') as HTMLElement | null;
      expect(runsBadge?.textContent?.trim()).toBe('2 selected');
      expect(buttons.find(b => b.id === 'csp-src-tab-groups')
        ?.querySelector('.csp-tab-selected')).toBeNull();
    });

    it('returns focus to the kind tab when the row a detail was opened from is gone', () => {
      render({ runs: runs(3), comparabilityIndex: buildIndex() });
      const dialog = fixture.debugElement.query(By.css('dialog.csp-condition-dialog'))
        .nativeElement as HTMLDialogElement;
      spyOn(dialog, 'showModal');
      const trigger = document.createElement('button');
      // Detached from the document: the row it named has since been paged, filtered or
      // refreshed away.
      component.openConditionDetail('run:3', { currentTarget: trigger } as unknown as Event);
      fixture.detectChanges();

      component.onConditionDialogClose(new Event('close'));

      expect(document.activeElement?.id).toBe('csp-src-tab-runs');
    });
  });
});
