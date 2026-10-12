import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import {
  BATTERY_SECTION_TITLE,
  ComparisonSourcePickerComponent,
  GROUP_SECTION_TITLE,
  ModelComparisonSelection,
  RUN_SECTION_TITLE,
  SOURCE_BATTERIES_VIEW_STORAGE_KEY,
  SOURCE_GROUPS_VIEW_STORAGE_KEY,
  SOURCE_MIX_NOTE,
  SOURCE_RUNS_VIEW_STORAGE_KEY
} from './comparison-source-picker.component';
import type {
  BenchmarkBatteryRunDto,
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

/**
 * Resolves after the dialog's next `close` event has been dispatched. The browser fires `close`
 * from its own queued task, which a fixed timer can overtake; the component's template listener
 * was registered first, so it has already run when this one resolves.
 */
function nextClose(dialog: HTMLDialogElement): Promise<void> {
  return new Promise<void>(resolve => dialog.addEventListener('close', () => resolve(), { once: true }));
}

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
      testedModelThinkingLevelUsed: null,
      testedModelReasoningModeUsed: null,
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
      testedModelDisplayName: null,
      testedModelProvider: null,
      testedModelId: null,
      testedModelThinkingLevel: null,
      testedModelReasoningMode: null,
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

  /** A finished battery run with a complete, current analysis: one a comparison may include. */
  function buildBattery(overrides: Partial<BenchmarkBatteryRunDto> = {}): BenchmarkBatteryRunDto {
    const id = overrides.id ?? 4;
    return {
      id,
      batteryId: 2,
      batteryName: 'Core Battery',
      definitionRevision: 3,
      definitionSha256: 'd'.repeat(64),
      weightingScheme: 'Equal',
      suites: [],
      suiteCount: 3,
      runsPerSuite: 2,
      requestedMemberCount: 6,
      completedMemberCount: 6,
      failedMemberCount: 0,
      completedSuiteCount: 3,
      status: 'Completed',
      allowCapWait: false,
      resumable: false,
      isDriving: false,
      startedAtUtc: '2026-10-01T10:00:00Z',
      completedAtUtc: '2026-10-01T14:00:00Z',
      testedModelLabel: `Battery model ${id}`,
      testedProvider: 'Anthropic',
      slots: [],
      members: [],
      latestAnalysisId: 30 + id,
      latestAnalysisAtUtc: '2026-10-01T15:00:00Z',
      latestAnalysisComplete: true,
      comparabilityClassSha256: 'c1a55'.repeat(13),
      overallIndex: 71.25,
      analysisStale: false,
      analysisHasExcludedMembers: false,
      ...overrides
    } as BenchmarkBatteryRunDto;
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
          label: 'Second reader configuration',
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
    batteryRuns?: BenchmarkBatteryRunDto[];
    selectedRunIds?: number[];
    selectedGroupIds?: number[];
    selectedBatteryRunIds?: number[];
    comparabilityIndex?: BenchmarkComparabilityIndexDto | null;
    indexLoading?: boolean;
  } = {}): void {
    fixture.componentRef.setInput('runs', inputs.runs ?? runs(3));
    fixture.componentRef.setInput('groups', inputs.groups ?? [buildGroup()]);
    fixture.componentRef.setInput('batteryRuns', inputs.batteryRuns ?? [buildBattery()]);
    fixture.componentRef.setInput('selectedRunIds', inputs.selectedRunIds ?? []);
    fixture.componentRef.setInput('selectedGroupIds', inputs.selectedGroupIds ?? []);
    fixture.componentRef.setInput('selectedBatteryRunIds', inputs.selectedBatteryRunIds ?? []);
    fixture.componentRef.setInput('comparabilityIndex', inputs.comparabilityIndex ?? null);
    fixture.componentRef.setInput('indexLoading', inputs.indexLoading ?? false);
    fixture.detectChanges();
  }

  /** Every list's stored Sort by choice, cleared so one spec's order never leaks into the next. */
  function clearStoredViews(): void {
    for (const key of [SOURCE_RUNS_VIEW_STORAGE_KEY, SOURCE_GROUPS_VIEW_STORAGE_KEY, SOURCE_BATTERIES_VIEW_STORAGE_KEY]) {
      localStorage.removeItem(key);
    }
  }

  beforeEach(async () => {
    clearStoredViews();
    await TestBed.configureTestingModule({
      imports: [ComparisonSourcePickerComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(ComparisonSourcePickerComponent);
    component = fixture.componentInstance;
  });

  // -------------------------------------------------------------------------------------------
  // Selection
  // -------------------------------------------------------------------------------------------

  it('counts the whole selection, not the batch, and says on the status line what is not shown', () => {
    // Twenty-five runs, newest first in batches of twenty: run 1 is beyond the first batch.
    render({ runs: runs(25), selectedRunIds: [1, 25] });

    expect(component.selectedCount).toBe(2);
    expect(cards().length).toBe(20);
    expect(component.hiddenSelectedCount('runs')).toBe(1);
    const status = fixture.nativeElement.querySelector('#csp-runs-status') as HTMLElement;
    expect(status.textContent?.trim()).toBe('Showing 20 of 25 runs · 2 selected, 1 not shown');
  });

  it('keeps a selected id selected through a facet and a batch change', () => {
    render({ runs: runs(25), selectedRunIds: [25] });

    component.onFacetChange('runs', 'model', ['Model 1']);
    fixture.detectChanges();
    expect(component.isRunSelected(25)).toBe(true);
    expect(component.hiddenSelectedCount('runs')).toBe(1);

    component.clearFilters('runs');
    component.showMore('runs');
    fixture.detectChanges();
    expect(component.selectedCount).toBe(1);
    expect(component.hiddenSelectedCount('runs')).toBe(0);
  });

  it('offers a run that is not completed aria-disabled, described by a visible reason line', () => {
    render({
      runs: [buildRun({ id: 1 }), buildRun({ id: 2, status: 'Failed' })]
    });
    const emitted: ModelComparisonSelection[] = [];
    component.selectionChange.subscribe(value => emitted.push(value));

    // Both cards are present: an operator who ran a benchmark should see why it cannot be charted.
    expect(cards().length).toBe(2);

    const failed = fixture.nativeElement.querySelector('#csp-run-2') as HTMLInputElement;
    // aria-disabled, not disabled: it stays focusable and keeps its name.
    expect(failed.disabled).toBe(false);
    expect(failed.getAttribute('aria-disabled')).toBe('true');
    expect(failed.getAttribute('aria-label')).toBe('Compare run 2');
    const reason = fixture.nativeElement.querySelector('#csp-run-2-reason') as HTMLElement;
    expect(failed.getAttribute('aria-describedby')).toBe('csp-run-2-reason');
    expect(reason.textContent).toContain('only a completed run can be compared');
    expect(reason.closest('.csp-card')?.classList).toContain('is-unselectable');

    failed.click();
    expect(failed.checked).toBe(false);
    expect(emitted).toEqual([]);

    const completed = fixture.nativeElement.querySelector('#csp-run-1') as HTMLInputElement;
    expect(completed.getAttribute('aria-disabled')).toBeNull();
    expect(completed.getAttribute('aria-describedby')).toBeNull();
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

    const texts = fixture.debugElement.queryAll(By.css('button'))
      .map(el => ((el.nativeElement as HTMLElement).textContent ?? '').trim());

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
    expect(select, 'the suite scope select belongs to the picker').toBeTruthy();

    component.onSuiteChange(6);
    component.onSuiteChange(null);

    expect(emitted).toEqual([6, null]);
  });

  it('renders both empty states naming the fix rather than a bare "nothing here"', () => {
    render({ runs: [], groups: [] });

    const runsText = (fixture.debugElement.query(By.css('.csp-empty'))
      .nativeElement as HTMLElement).textContent ?? '';
    expect(runsText).toContain('Choose another suite');
    expect(runsText).toContain('run a benchmark');
    expect(fixture.debugElement.query(By.css('.csp-filter-bar'))).toBeNull();

    component.selectSourceTab('groups');
    fixture.detectChanges();
    const groupsText = (fixture.debugElement.query(By.css('.csp-empty'))
      .nativeElement as HTMLElement).textContent ?? '';
    expect(groupsText).toContain('Choose another suite');
    expect(groupsText).toContain('build a group');
  });

  it('says when the filters hide everything, and clears them all from there', () => {
    render({ runs: runs(3), selectedRunIds: [1] });
    component.toggleShowSelectedOnly('runs');
    component.onFacetChange('runs', 'model', ['Model 2']);
    fixture.detectChanges();

    const note = fixture.nativeElement.querySelector('.csp-no-matches') as HTMLElement;
    expect(note.textContent).toContain('No runs match these filters.');
    expect(fixture.nativeElement.querySelector('.csp-cards')).toBeNull();

    (note.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();

    // The view settings go too, since they may be what hides every card.
    expect(component.isShowSelectedOnly('runs')).toBe(false);
    expect(cards().length).toBe(3);
    expect(document.activeElement?.id).toBe('csp-runs-search');
  });

  // -------------------------------------------------------------------------------------------
  // Model identity
  // -------------------------------------------------------------------------------------------

  /** The active list's cards, in display order. */
  function cards(): HTMLElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('article.csp-card')) as HTMLElement[];
  }

  /** A group the series orchestrator named, carrying its tested model. */
  function buildModelGroup(overrides: Partial<BenchmarkRunGroupDto> = {}): BenchmarkRunGroupDto {
    return buildGroup({
      name: 'GnollHack Player Assistance Benchmark Suite · GPT-5.6 Luna · 2026-09-07 · R=3',
      testedModelDisplayName: 'GPT-5.6 Luna',
      testedModelProvider: 'OpenAI',
      testedModelId: 'gpt-5.6-luna',
      testedModelThinkingLevel: 'max',
      testedModelReasoningMode: 'pro',
      ...overrides
    });
  }

  it('badges each run with its thinking level and a non-baseline reasoning mode', () => {
    render({
      runs: [
        buildRun({ id: 1, testedModelThinkingLevelUsed: 'max', testedModelReasoningModeUsed: 'pro' }),
        buildRun({ id: 2, testedModelThinkingLevelUsed: 'high', testedModelReasoningModeUsed: 'default' })
      ]
    });

    // Newest first: run 2, then run 1.
    const [second, first] = cards();
    const title = first.querySelector('.csp-card-title') as HTMLElement;
    expect(title.id).toBe('csp-run-1-title');
    expect(title.querySelector('label')?.getAttribute('for')).toBe('csp-run-1');
    expect(title.querySelector('app-model-identity .model-identity-name')?.textContent?.trim()).toBe('Model 1');
    expect(title.querySelector('app-model-identity .provider-badge')?.textContent?.trim()).toBe('Google');
    expect(first.querySelector('.thinking-badge')?.textContent).toContain('max');
    expect(first.querySelector('.reasoning-badge')?.textContent).toContain('pro');
    expect(second.querySelector('.thinking-badge')?.textContent).toContain('high');
    expect(second.querySelector('.reasoning-badge')).toBeNull();
    expect(first.querySelector('.csp-card-meta')?.textContent).toContain('#1');
    expect(first.querySelector('.csp-card-meta')?.textContent).toContain('Completed');
  });

  it('selects a card from a click on its title, never from the card itself', () => {
    render({ runs: runs(2) });
    const emitted: ModelComparisonSelection[] = [];
    component.selectionChange.subscribe(value => emitted.push(value));

    (fixture.nativeElement.querySelector('#csp-run-1-title label') as HTMLLabelElement).click();
    expect(emitted).toEqual([{ runIds: [1], groupIds: [], batteryRunIds: [] }]);

    (fixture.nativeElement.querySelector('[data-source-id="2"] .csp-card-meta') as HTMLElement).click();
    expect(emitted.length).toBe(1);
    expect(fixture.nativeElement.querySelector('label.csp-card')).toBeNull();
  });

  it('finds runs by reasoning mode through the search', () => {
    render({
      runs: [
        buildRun({ id: 1, testedModelReasoningModeUsed: 'pro' }),
        buildRun({ id: 2, testedModelReasoningModeUsed: 'standard' })
      ]
    });

    component.runTable.setFilter('search', 'pro');
    fixture.detectChanges();

    expect(component.runList.view(component.runs).map(r => r.id)).toEqual([1]);
  });

  it('leads a group card with its model and badges, and hides a generated name', () => {
    render({ groups: [buildModelGroup({ id: 12 })] });
    component.selectSourceTab('groups');
    fixture.detectChanges();

    const card = cards()[0];
    expect(card.querySelector('.csp-card-id')?.textContent?.trim()).toBe('#12');
    const title = card.querySelector('.csp-card-title') as HTMLElement;
    expect(title.querySelector('.model-identity-name')?.textContent?.trim()).toBe('GPT-5.6 Luna');
    expect(title.querySelector('.provider-badge')?.textContent?.trim()).toBe('OpenAI');
    expect(title.querySelector('.thinking-badge')?.textContent).toContain('max');
    expect(title.querySelector('.reasoning-badge')?.textContent).toContain('pro');
    expect(card.querySelector('.csp-group-caption')).toBeNull();
    expect(card.querySelector('.csp-tier')?.textContent?.trim()).toBe('Tier A — Replicate');
    expect(card.textContent).toContain('GnollHack Player Assistance Benchmark Suite');
  });

  it('shows a renamed group\'s name as a caption on its meta line', () => {
    render({ groups: [buildModelGroup({ name: 'Luna baseline after the prompt fix' })] });
    component.selectSourceTab('groups');
    fixture.detectChanges();

    const card = cards()[0];
    expect(card.querySelector('.csp-card-title .model-identity-name')?.textContent?.trim()).toBe('GPT-5.6 Luna');
    expect(card.querySelector('.csp-card-meta .csp-group-caption')?.textContent?.trim())
      .toBe('Luna baseline after the prompt fix');
  });

  it('shows an empty group\'s name as its title', () => {
    render({ groups: [buildGroup({ name: 'Suite · R=0', runCount: 0 })] });
    component.selectSourceTab('groups');
    fixture.detectChanges();

    const card = cards()[0];
    expect(card.querySelector('app-model-identity')).toBeNull();
    expect(card.querySelector('.csp-card-title')?.textContent?.trim()).toBe('Suite · R=0');
    expect(card.querySelector('.csp-group-caption')).toBeNull();
  });

  it('matches the group search against the model name and the group name', () => {
    const groups = [
      buildModelGroup({ id: 1 }),
      buildModelGroup({
        id: 2,
        name: 'Claude comparison set',
        testedModelDisplayName: 'Claude Opus',
        testedModelProvider: 'Anthropic',
        testedModelThinkingLevel: 'high',
        testedModelReasoningMode: null
      })
    ];
    render({ groups });

    component.groupTable.setFilter('search', 'luna');
    expect(component.groupList.view(component.groups).map(g => g.id)).toEqual([1]);

    component.groupTable.setFilter('search', 'comparison set');
    expect(component.groupList.view(component.groups).map(g => g.id)).toEqual([2]);
  });

  it('names a group checkbox by its id and model', () => {
    render({ groups: [buildModelGroup({ id: 12 })] });
    component.selectSourceTab('groups');
    fixture.detectChanges();

    const box = fixture.debugElement.query(By.css('#csp-group-12')).nativeElement as HTMLInputElement;
    expect(box.getAttribute('aria-label')).toBe('Compare analysis group 12, GPT-5.6 Luna');
  });

  it('labels each kind\'s card metrics with visible terms', () => {
    render({ runs: runs(1), groups: [buildModelGroup()], batteryRuns: [buildBattery()] });
    const labels = (): string[] => Array.from(cards()[0].querySelectorAll('.csp-card-metrics dt'))
      .map(term => (term.textContent ?? '').trim());

    expect(labels()).toEqual(['Intelligence Index', 'Condition']);

    component.selectSourceTab('groups');
    fixture.detectChanges();
    expect(labels()).toEqual(['Runs', 'Condition']);

    component.selectSourceTab('batteries');
    fixture.detectChanges();
    expect(labels()).toEqual(['Overall Index', 'Class', 'Runs per suite']);
  });

  it('ends the meta line of a run, group or battery card with the provider-error badge only when it has provider errors or re-runs', () => {
    const trouble = {
      providerAvailabilityRecorded: true, providerRecordedRunCount: 3, providerRunCount: 3,
      providerValidAttempts: 1480, providerErrorCount: 12, providerUnattributedErrorCount: 3,
      providerErrorRate: 12 / 1480, rerunsNeeded: 2, providerRerunsNeeded: 1
    };
    const allWell = { ...trouble, providerErrorCount: 0, providerUnattributedErrorCount: 0, providerErrorRate: 0, rerunsNeeded: 0, providerRerunsNeeded: 0 };
    render({
      runs: [buildRun({ id: 2, ...trouble }), buildRun({ id: 1, ...allWell })],
      groups: [buildGroup({ id: 2, ...trouble }), buildGroup({ id: 1 })],
      batteryRuns: [buildBattery({ id: 5, ...trouble }), buildBattery({ id: 4, ...allWell })]
    });
    const card = (id: number) => cards().find(c => c.getAttribute('data-source-id') === String(id)) as HTMLElement;
    const expectBadgeOn = (troubled: number, quiet: number) => {
      const meta = card(troubled).querySelector('.csp-card-meta') as HTMLElement;
      const badge = meta.lastElementChild as HTMLElement;
      expect(badge.tagName.toLowerCase()).toBe('app-provider-error-badge');
      expect(badge.previousElementSibling?.classList.contains('csp-sep')).toBe(true);
      const quietMeta = card(quiet).querySelector('.csp-card-meta') as HTMLElement;
      expect(quietMeta.querySelector('app-provider-error-badge')).toBeNull();
      expect(quietMeta.lastElementChild?.classList.contains('csp-sep')).toBe(false);
    };

    expectBadgeOn(2, 1);

    component.selectSourceTab('groups');
    fixture.detectChanges();
    expectBadgeOn(2, 1);

    component.selectSourceTab('batteries');
    fixture.detectChanges();
    expectBadgeOn(5, 4);
  });

  // -------------------------------------------------------------------------------------------
  // The comparability index
  // -------------------------------------------------------------------------------------------

  it("renders the run's and the group's condition label from the index", () => {
    render({ runs: runs(3), groups: [buildGroup({ id: 1 })], comparabilityIndex: buildIndex() });

    expect(component.conditionLabel('run:1')).toBe('Condition A');
    expect(component.conditionLabel('run:3')).toBe('Condition B');
    expect(component.conditionLabel('group:1')).toBe('Condition A');

    const runBadges = fixture.debugElement.queryAll(By.css('.csp-card [data-metric="condition"] .csp-condition'))
      .map(el => (el.nativeElement as HTMLElement).textContent?.trim());
    expect(runBadges).toContain('Condition A');

    component.selectSourceTab('groups');
    fixture.detectChanges();
    const groupBadges = fixture.debugElement.queryAll(By.css('.csp-card [data-metric="condition"] .csp-condition'))
      .map(el => (el.nativeElement as HTMLElement).textContent?.trim());
    expect(groupBadges).toContain('Condition A');
  });

  it('narrows both lists through the Condition facet, listing the conditions in ordinal order', () => {
    render({ runs: runs(3), groups: [buildGroup({ id: 1 })], comparabilityIndex: buildIndex() });

    const facet = component.facetsOf('runs').find(candidate => candidate.column === 'condition')!;
    expect(facet.options.map(option => option.value)).toEqual(['Condition A', 'Condition B']);
    expect(facet.facetId).toBe('csp-runs-facet-condition');

    component.onFacetChange('runs', 'condition', ['Condition B']);
    component.onFacetChange('groups', 'condition', ['Condition B']);
    fixture.detectChanges();

    expect(component.runList.view(component.runs).map(r => r.id)).toEqual([3]);
    expect(component.groupList.view(component.groups).length).toBe(0);
  });

  it('renders a muted dash and blocks nothing while the index is null', () => {
    render({ runs: [buildRun({ id: 1 }), buildRun({ id: 2, status: 'Failed' })], comparabilityIndex: null });

    expect(component.conditionLabel('run:1')).toBe('—');
    expect(component.conditionOrdinal('run:1')).toBeNull();
    expect(conditionDetailFor(component.comparabilityIndex, 'run:1', [1])).toBeNull();
    expect(component.hasConditionDetail('run:1')).toBe(false);
    // The failed run is still unselectable for its own reason, not because of the index.
    expect(component.isRunSelectable(component.runs[0])).toBe(true);
    expect(component.isRunSelectable(component.runs[1])).toBe(false);
  });

  it('shows a muted dash while the index is loading, even if a stale index is present', () => {
    render({ comparabilityIndex: buildIndex(), indexLoading: true });

    expect(component.conditionLabel('run:1')).toBe('—');
  });

  it('narrows the runs to the largest condition with Show comparable only, which Clear all keeps', () => {
    render({ runs: runs(3), comparabilityIndex: buildIndex() });

    expect(component.showCompatibleRunsOnly).toBe(false);
    component.toggleShowCompatibleRunsOnly();
    fixture.detectChanges();

    expect(component.showCompatibleRunsOnly).toBe(true);
    const toggle = fixture.nativeElement.querySelector('.csp-comparable-only') as HTMLButtonElement;
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    // Newest first, so the surviving cards come back 2 then 1.
    expect(component.runList.view(component.runs).map(r => r.id)).toEqual([2, 1]);

    // Its own column, so the Condition facet can narrow at the same time.
    component.onFacetChange('runs', 'condition', ['Condition A']);
    fixture.detectChanges();
    expect(component.runList.view(component.runs).map(r => r.id)).toEqual([2, 1]);
    expect(component.showCompatibleRunsOnly).toBe(true);
    // A view setting, not a filter: it has no chip.
    expect(component.chipsOf('runs').map(chip => chip.column)).toEqual(['condition']);

    component.clearFilters('runs');
    fixture.detectChanges();
    expect(component.showCompatibleRunsOnly).toBe(true);
    expect(component.chipsOf('runs')).toEqual([]);
  });

  // -------------------------------------------------------------------------------------------
  // The card lists
  // -------------------------------------------------------------------------------------------

  describe('the card lists', () => {
    it('clears the search on Escape without letting the key reach an enclosing dialog', () => {
      render({ runs: runs(3) });
      const reached: string[] = [];
      const listener = (event: KeyboardEvent): void => {
        reached.push(event.key);
      };
      document.addEventListener('keydown', listener);
      try {
        const input = fixture.nativeElement.querySelector('#csp-runs-search') as HTMLInputElement;
        input.value = 'Model 2';
        input.dispatchEvent(new Event('input'));
        expect(component.runList.searchText).toBe('Model 2');

        const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
        input.dispatchEvent(escape);
        fixture.detectChanges();

        expect(input.value).toBe('');
        expect(component.runList.searchText).toBe('');
        expect(escape.defaultPrevented).toBe(true);
        expect(reached).toEqual([]);

        // In an empty field Escape is left alone, for the dialog to close on.
        const again = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
        input.dispatchEvent(again);
        expect(again.defaultPrevented).toBe(false);
        expect(reached).toEqual(['Escape']);
      } finally {
        document.removeEventListener('keydown', listener);
      }
    });

    it('moves focus to the next chip, else the previous, else the search, as chips are removed', () => {
      render({ runs: [buildRun({ id: 1 }), buildRun({ id: 2, status: 'Failed' }), buildRun({ id: 3 })] });
      component.onFacetChange('runs', 'model', ['Model 1']);
      component.onFacetChange('runs', 'status', ['Completed']);
      fixture.detectChanges();

      const chipButtons = (): HTMLButtonElement[] =>
        Array.from(fixture.nativeElement.querySelectorAll('#csp-runs-chips .gh-filter-chip'));
      expect(chipButtons().map(chip => chip.getAttribute('aria-label')))
        .toEqual(['Remove filter Model: Model 1', 'Remove filter Status: Completed']);

      chipButtons()[0].click();
      expect(chipButtons().length).toBe(1);
      expect(document.activeElement).toBe(chipButtons()[0]);

      chipButtons()[0].click();
      expect(fixture.nativeElement.querySelector('#csp-runs-chips')).toBeNull();
      expect(document.activeElement?.id).toBe('csp-runs-search');
    });

    it('recounts the facets when the selection changes', () => {
      render({ runs: runs(3), selectedRunIds: [1] });
      component.toggleShowSelectedOnly('runs');
      fixture.detectChanges();
      const counts = (): Record<string, number> => Object.fromEntries(
        component.facetsOf('runs').find(facet => facet.column === 'model')!.options
          .map(option => [option.value, option.count]));

      expect(counts()).toEqual({ 'Model 1': 1, 'Model 2': 0, 'Model 3': 0 });

      fixture.componentRef.setInput('selectedRunIds', [1, 2]);
      fixture.detectChanges();

      expect(counts()).toEqual({ 'Model 1': 1, 'Model 2': 1, 'Model 3': 0 });
    });

    it('remembers each list\'s Sort by choice, and falls back to Newest first when storage fails', () => {
      render({ runs: runs(3) });
      const sort = fixture.nativeElement.querySelector('#csp-runs-sort') as HTMLSelectElement;
      expect(Array.from(sort.options).map(option => option.textContent?.trim()))
        .toEqual(['Newest first', 'Oldest first', 'Intelligence Index (high–low)', 'Model (A–Z)', 'Condition']);
      sort.value = 'index';
      sort.dispatchEvent(new Event('change'));

      expect(JSON.parse(localStorage.getItem(SOURCE_RUNS_VIEW_STORAGE_KEY)!)).toEqual({ version: 1, sort: 'index' });

      const restored = TestBed.createComponent(ComparisonSourcePickerComponent).componentInstance;
      expect(restored.runList.sortId).toBe('index');
      expect(restored.groupList.sortId).toBe('newest');

      const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
        throw new Error('Storage is disabled');
      });
      try {
        expect(TestBed.createComponent(ComparisonSourcePickerComponent).componentInstance.runList.sortId)
          .toBe('newest');
      } finally {
        getItem.mockRestore();
      }
    });

    it('makes the card list the scroller of a picker given a height', () => {
      // As the wizard's step panel gives it: a fixed box, not a flex item growing into the test root.
      fixture.nativeElement.style.flex = 'none';
      fixture.nativeElement.style.height = '480px';
      render({ runs: runs(40) });
      component.showAll('runs');
      fixture.detectChanges();

      const scroller = fixture.nativeElement.querySelector('.csp-cards-scroll') as HTMLElement;
      expect(getComputedStyle(scroller).overflowY).toBe('auto');
      expect(scroller.clientHeight).toBeGreaterThan(0);
      expect(scroller.clientHeight).toBeLessThan(scroller.scrollHeight);
      // The tab row and the filter bar stay in place above it.
      const host = fixture.nativeElement as HTMLElement;
      expect(host.getBoundingClientRect().height).toBeLessThanOrEqual(481);
    });
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
    function captureSaves(): {
      blobs: Blob[];
      names: string[];
    } {
      const saved: {
        blobs: Blob[];
        names: string[];
      } = { blobs: [], names: [] };
      vi.spyOn(URL, 'createObjectURL').mockImplementation((source: Blob | MediaSource) => {
        saved.blobs.push(source as Blob);
        return 'blob:comparison-source-picker-test';
      });
      vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {
      });
      vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
        saved.names.push(this.download);
      });
      return saved;
    }

    it('offers the control only where the source differs from the reference condition', () => {
      render({ runs: runs(3), groups: [buildGroup({ id: 1 })], comparabilityIndex: buildIndex() });

      // Runs 1 and 2 and the group are the reference condition itself; run 3 is not.
      expect(detailButtons().length).toBe(1);
      expect(detailButtons()[0].getAttribute('aria-label')).toBe('Comparability detail for run 3');
      expect(component.hasConditionDetail('run:1')).toBe(false);
      expect(component.hasConditionDetail('run:3')).toBe(true);
    });

    it('opens the dialog populated with that entry\'s rows', () => {
      render({ runs: runs(3), comparabilityIndex: buildIndex() });
      const showModal = vi.spyOn(conditionDialog(), 'showModal').mockReturnValue(undefined);

      detailButtons()[0].click();
      fixture.detectChanges();

      expect(showModal).toHaveBeenCalled();
      expect(component.conditionDetail?.sourceLabel).toBe('Run 3');
      expect(component.conditionDetailTitle).toBe('Run 3 — Condition B');
      // Full-screen, like the wizard it is nested in — its backdrop is too thin to be an honest
      // click target, so it carries no closedby and no light-dismiss handler.
      expect(conditionDialog().classList.contains('gh-dialog-fullscreen')).toBe(true);
      expect(conditionDialog().hasAttribute('closedby')).toBe(false);

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
      const writeText = vi.fn().mockName('writeText').mockResolvedValue(undefined);
      Object.defineProperty(navigator, 'clipboard', {
        value: { writeText }, configurable: true, writable: true
      });
      try {
        render({ runs: runs(3), comparabilityIndex: buildIndex() });
        vi.spyOn(conditionDialog(), 'showModal').mockReturnValue(undefined);
        detailButtons()[0].click();

        await component.copyConditionDetail();

        const written = vi.mocked(writeText).mock.lastCall![0] as string;
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
      vi.spyOn(conditionDialog(), 'showModal').mockReturnValue(undefined);
      // The run list shows the newest first by default, so run 46 renders — and opens — first.
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
      expect(dialog.open).toBe(true);

      // A synthetic key event cannot drive a dialog's own close request, so Escape is exercised as
      // the two events it produces: a cancel the handler must not prevent, then the close itself.
      const cancel = new Event('cancel', { cancelable: true });
      dialog.dispatchEvent(cancel);
      expect(cancel.defaultPrevented, 'a refused close request leaves the reader trapped').toBe(false);

      const closed = nextClose(dialog);
      dialog.close();
      await closed;

      expect(dialog.open).toBe(false);
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
      vi.spyOn(conditionDialog(), 'showModal').mockReturnValue(undefined);

      detailButtons()[0].click();
      fixture.detectChanges();

      expect(component.conditionDetail?.selfInconsistent).toBe(true);
      const chips = fixture.debugElement
        .queryAll(By.css('dialog.csp-condition-dialog .csp-key-chips li'))
        .map(element => (element.nativeElement as HTMLElement).textContent?.trim());
      expect(chips).toEqual(['BenchmarkSuiteId', 'CandidateModelId']);
      // A source in no condition differs from the reference on nothing named, so no table is drawn.
      expect(fixture.debugElement.query(By.css('dialog.csp-condition-dialog tbody'))).toBeNull();
    });

    it('keeps the detail dialog close event off the wizard', () => {
      const event = new Event('close');
      const stopPropagation = vi.spyOn(event, 'stopPropagation').mockReturnValue(undefined);

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
    const showModal = vi.spyOn(legendDialog(), 'showModal').mockReturnValue(undefined);

    (fixture.debugElement.query(By.css('#csp-legend-trigger'))
      .nativeElement as HTMLButtonElement).click();

    expect(showModal).toHaveBeenCalled();
    expect(component.legendOpen).toBe(true);
    // Full-screen, so its backdrop is only a thin ring: no light dismiss.
    expect(legendDialog().classList.contains('gh-dialog-fullscreen')).toBe(true);
    expect(legendDialog().hasAttribute('closedby')).toBe(false);
    expect((fixture.debugElement.query(By.css('#csp-legend-title')).nativeElement as HTMLElement)
      .textContent?.trim()).toBe('About conditions');
  });

  it('renders nothing of its body while closed, and drops it again on close', async () => {
    render({ comparabilityIndex: buildIndex() });
    expect(fixture.debugElement.query(By.css('.csp-legend-inner'))).toBeNull();

    openLegend();
    expect(fixture.debugElement.query(By.css('.csp-legend-inner'))).toBeTruthy();

    const closed = nextClose(legendDialog());
    legendDialog().close();
    await closed;
    fixture.detectChanges();

    expect(component.legendOpen).toBe(false);
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
    expect(chips.some(text => text.includes(','))).toBe(false);
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
    expect(items[0].open).toBe(true);
    expect(component.openConditionOrdinal).toBe(1);
    expect(summaries[2]).toContain('differs on 1 setting');
  });

  it('keeps one body in the DOM, and moves it with the exclusive accordion', () => {
    render({ comparabilityIndex: buildIndex() });
    openLegend();
    const [reference, other] = conditionItems();

    expect(conditionItems().every(item => item.getAttribute('name') === 'csp-conditions')).toBe(true);
    expect(conditionBodies().length).toBe(1);
    expect(reference.contains(conditionBodies()[0])).toBe(true);

    toggle(other, 'open');
    // The closing item's event arrives after the opening one's and must not clear the new state.
    toggle(reference, 'closed');

    expect(component.openConditionOrdinal).toBe(2);
    expect(conditionBodies().length).toBe(1);
    expect(other.contains(conditionBodies()[0])).toBe(true);

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
    const showModal = vi.spyOn(detailDialog, 'showModal').mockReturnValue(undefined);
    const fullDetail = body.querySelector('.csp-cond-actions button') as HTMLButtonElement;
    expect(fullDetail.getAttribute('aria-label')).toBe('Full detail for Condition B');

    fullDetail.click();

    expect(showModal).toHaveBeenCalled();
    expect(component.conditionDetail?.sourceLabel).toBe('Run 3');
  });

  it('keeps technical details off on every open', () => {
    render({ comparabilityIndex: buildIndex() });
    openLegend();
    expect(component.showTechnicalDetails).toBe(false);
    expect(fixture.debugElement.query(By.css('.csp-methods-key'))).toBeNull();

    component.toggleTechnicalDetails();
    expect(fixture.debugElement.query(By.css('.csp-methods-key'))).toBeTruthy();
    expect((fixture.debugElement.query(By.css('.csp-conditions-head .gh-filter-toggle'))
      .nativeElement as HTMLButtonElement).getAttribute('aria-pressed')).toBe('true');

    component.onLegendDialogClose(new Event('close'));
    openLegend();

    expect(component.showTechnicalDetails).toBe(false);
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
      expect(values.some(text => text.includes('NetHack Wiki Suite (#5)'))).toBe(true);
    });

    it('shows a hash as twelve characters, with the full digest a technical detail', () => {
      renderLegend();

      expect(textOf('.csp-methods-grid .csp-methods-hash code')).toBe(FULL_DIGEST.slice(0, 12));
      expect(fixture.debugElement.query(By.css('details.csp-methods-disclosure'))).toBeNull();

      component.toggleTechnicalDetails();

      const disclosure = fixture.debugElement.query(By.css('details.csp-methods-disclosure'));
      expect(disclosure, 'the full digest must remain reachable by hand').toBeTruthy();
      expect(textOf('details.csp-methods-disclosure code')).toBe(FULL_DIGEST);
    });

    it('keeps the prompt-options value inside its own code scroller', () => {
      renderLegend();

      const values = fixture.debugElement.queryAll(By.css('.csp-legend-values dd code'))
        .map(element => (element.nativeElement as HTMLElement).textContent ?? '');
      expect(values.some(text => text.includes(UNBREAKABLE_TOKEN)), 'the prompt-options value must render inside its own code box').toBe(true);
      // Pretty-printed rather than the minified blob the wire carries.
      expect(values.some(text => text.includes('"temperature": 0.2'))).toBe(true);
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
      expect(component.isNoValue('(none)')).toBe(true);
    });

    it('copies the methods statement with full values, never the abbreviations', async () => {
      const writeText = vi.fn().mockName('writeText').mockResolvedValue(undefined);
      installClipboard({ writeText });
      renderLegend();
      const footerButtons = fixture.debugElement
        .queryAll(By.css('dialog.csp-legend-dialog .dialog-actions button'))
        .map(element => (element.nativeElement as HTMLButtonElement).textContent?.trim());
      expect(footerButtons).toEqual(['Close', 'Copy methods statement']);

      await component.copyMethodsStatement();

      const written = vi.mocked(writeText).mock.lastCall![0] as string;
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
      const writeText = vi.fn().mockName('writeText').mockResolvedValue(undefined);
      installClipboard({ writeText });
      const index = buildIndex();
      render({ comparabilityIndex: index });

      const hashKey = index.largestConditionKeys.find(key => key.valueKind === 'Hash')!;
      await component.copyFullValue(hashKey);

      expect(writeText).toHaveBeenCalledWith(FULL_DIGEST);
    });

    it('copies a condition signature in full', async () => {
      const writeText = vi.fn().mockName('writeText').mockResolvedValue(undefined);
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
    const stopPropagation = vi.spyOn(event, 'stopPropagation').mockReturnValue(undefined);

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

    it('titles the three kind tabs from the exported constants', () => {
      render();
      const buttons = tabButtons();
      expect(buttons.length).toBe(3);
      const [runsTab, groupsTab, batteriesTab] = buttons;

      expect(runsTab.textContent).toContain(RUN_SECTION_TITLE);
      expect(groupsTab.textContent).toContain(GROUP_SECTION_TITLE);
      expect(batteriesTab.textContent).toContain(BATTERY_SECTION_TITLE);
      expect(batteriesTab.id).toBe('csp-src-tab-batteries');
      expect(batteriesTab.getAttribute('aria-controls')).toBe('csp-src-panel-batteries');
    });

    it("renders only the active kind's list, and keeps each list's sort and batch across a tab switch", () => {
      render({ runs: runs(45), groups: [buildGroup({ id: 1 })] });

      expect(fixture.debugElement.queryAll(By.css('ul.csp-cards')).length).toBe(1);
      expect(fixture.debugElement.query(By.css('#csp-src-panel-runs'))).toBeTruthy();
      expect(fixture.debugElement.query(By.css('#csp-src-panel-groups'))).toBeFalsy();

      const sort = fixture.nativeElement.querySelector('#csp-runs-sort') as HTMLSelectElement;
      sort.value = 'oldest';
      sort.dispatchEvent(new Event('change'));
      component.showMore('runs');
      fixture.detectChanges();
      expect(cards().length).toBe(40);

      tabButtons()[1].click();
      fixture.detectChanges();

      expect(fixture.debugElement.queryAll(By.css('ul.csp-cards')).length).toBe(1);
      expect(fixture.debugElement.query(By.css('#csp-src-panel-groups'))).toBeTruthy();
      expect(fixture.debugElement.query(By.css('#csp-src-panel-runs'))).toBeFalsy();

      tabButtons()[0].click();
      fixture.detectChanges();

      // The list states live on the component, not the template, so they survive the panel being
      // removed from the DOM and rendered again.
      expect(component.runList.sortId).toBe('oldest');
      expect((fixture.nativeElement.querySelector('#csp-runs-sort') as HTMLSelectElement).value).toBe('oldest');
      expect(cards().length).toBe(40);
      expect(cards()[0].getAttribute('data-source-id')).toBe('1');
    });

    it('gives each list one polite status line, and focuses the first new card after Show more', () => {
      render({ runs: runs(45), groups: [buildGroup({ id: 1 })] });

      const statuses = (): HTMLElement[] => Array.from(fixture.nativeElement.querySelectorAll('.csp-panel [role="status"]'));
      expect(statuses().length).toBe(1);
      expect(statuses()[0].id).toBe('csp-runs-status');
      expect(statuses()[0].textContent?.trim()).toBe('Showing 20 of 45 runs');
      expect(fixture.nativeElement.querySelector('app-table-pager')).toBeNull();

      const more = fixture.nativeElement.querySelector('.csp-show-more') as HTMLButtonElement;
      expect(more.textContent?.trim()).toBe('Show 20 more');
      expect((fixture.nativeElement.querySelector('.csp-show-all') as HTMLButtonElement).textContent?.trim())
        .toBe('Show all 45');
      more.click();
      fixture.detectChanges();

      // Newest first: runs 45 to 26 were shown, so run 25 is the first new card.
      expect(document.activeElement?.id).toBe('csp-run-25-title');
      expect(statuses()[0].textContent?.trim()).toBe('Showing 40 of 45 runs');

      (fixture.nativeElement.querySelector('.csp-show-more') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(document.activeElement?.id).toBe('csp-run-5-title');
      expect(fixture.nativeElement.querySelector('.csp-load-more')).toBeNull();

      tabButtons()[1].click();
      fixture.detectChanges();
      expect(statuses().map(status => status.id)).toEqual(['csp-groups-status']);
    });

    it('moves between the kind tabs with the arrow keys, wrapping, and focus follows', () => {
      render();
      const runsTab = tabButtons()[0];
      runsTab.focus();

      component.onSourceTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }), 0);
      fixture.detectChanges();
      expect(component.activeSourceTab).toBe('groups');
      expect(document.activeElement).toBe(fixture.nativeElement.querySelector('#csp-src-tab-groups'));

      component.onSourceTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }), 1);
      fixture.detectChanges();
      expect(component.activeSourceTab).toBe('batteries');
      expect(document.activeElement).toBe(fixture.nativeElement.querySelector('#csp-src-tab-batteries'));

      // Three tabs: ArrowRight on the last wraps back to the first.
      component.onSourceTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }), 2);
      fixture.detectChanges();
      expect(component.activeSourceTab).toBe('runs');
      expect(document.activeElement).toBe(fixture.nativeElement.querySelector('#csp-src-tab-runs'));

      component.onSourceTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowLeft' }), 0);
      fixture.detectChanges();
      expect(component.activeSourceTab).toBe('batteries');

      component.onSourceTabKeydown(new KeyboardEvent('keydown', { key: 'End' }), 0);
      fixture.detectChanges();
      expect(component.activeSourceTab).toBe('batteries');

      component.onSourceTabKeydown(new KeyboardEvent('keydown', { key: 'Home' }), 2);
      fixture.detectChanges();
      expect(component.activeSourceTab).toBe('runs');
    });

    it('gives exactly one tab a roving tabindex of 0, and marks it aria-selected', () => {
      render();
      const buttons = tabButtons();

      expect(buttons.filter(b => b.getAttribute('tabindex') === '0').length).toBe(1);
      expect(buttons.filter(b => b.getAttribute('tabindex') === '-1').length).toBe(2);
      expect(buttons.find(b => b.id === 'csp-src-tab-runs')?.getAttribute('aria-selected')).toBe('true');
      expect(buttons.find(b => b.id === 'csp-src-tab-groups')?.getAttribute('aria-selected')).toBe('false');
      expect(buttons.find(b => b.id === 'csp-src-tab-batteries')?.getAttribute('aria-selected')).toBe('false');
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
      vi.spyOn(dialog, 'showModal').mockReturnValue(undefined);
      const trigger = document.createElement('button');
      // Detached from the document: the row it named has since been paged, filtered or
      // refreshed away.
      component.openConditionDetail('run:3', { currentTarget: trigger } as unknown as Event);
      fixture.detectChanges();

      component.onConditionDialogClose(new Event('close'));

      expect(document.activeElement?.id).toBe('csp-src-tab-runs');
    });
  });

  // -------------------------------------------------------------------------------------------
  // Battery results
  // -------------------------------------------------------------------------------------------

  describe('battery results', () => {
    function showBatteries(): void {
      component.selectSourceTab('batteries');
      fixture.detectChanges();
    }

    const box = (selector: string): HTMLInputElement =>
      fixture.nativeElement.querySelector(selector) as HTMLInputElement;

    it('lists battery results as their own cards, newest first, with no Condition metric', () => {
      render({
        batteryRuns: [
          buildBattery({ id: 4 }),
          buildBattery({ id: 5, analysisStale: true }),
          buildBattery({ id: 6, latestAnalysisComplete: false, comparabilityClassSha256: null })
        ]
      });
      showBatteries();

      const panel = fixture.nativeElement.querySelector('#csp-src-panel-batteries') as HTMLElement;
      expect(panel.getAttribute('aria-labelledby')).toBe('csp-src-tab-batteries');
      const list = panel.querySelector('ul.csp-cards') as HTMLElement;
      expect(list.getAttribute('role')).toBe('list');
      const heading = panel.querySelector('#' + list.getAttribute('aria-labelledby')) as HTMLElement;
      expect(heading.tagName).toBe('H4');
      expect(heading.textContent?.trim()).toBe(BATTERY_SECTION_TITLE);
      expect(panel.querySelector('[data-metric="condition"]')).toBeNull();

      // Newest first by id; the class is a short hash, or a tag while the analysis cannot be used.
      const rows = cards();
      expect(rows.map(row => row.querySelector('.csp-card-id')?.textContent?.trim())).toEqual(['#6', '#5', '#4']);
      expect(rows[0].querySelector('[data-metric="class"]')?.textContent).toContain('Incomplete');
      expect(rows[1].querySelector('.gh-tag-changed')?.textContent?.trim()).toBe('Stale');
      expect(rows[2].querySelector('.csp-class-hash')?.textContent?.trim()).toBe('c1a55'.repeat(13).slice(0, 12));
      expect(rows[2].textContent).toContain('Core Battery');
      expect(rows[2].textContent).toContain('rev 3');
      expect(rows[2].querySelector('[data-metric="index"] dd')?.textContent?.trim()).toBe('71.3');
      expect(rows[2].querySelector('[data-metric="runs-per-suite"] dd')?.textContent?.trim()).toBe('2');
      expect(box('#csp-battery-4').getAttribute('aria-label')).toBe('Include battery run 4 in the comparison');
    });

    it('names a battery result with no model as such', () => {
      render({ batteryRuns: [buildBattery({ id: 4, testedModelLabel: null, testedModelId: null })] });
      showBatteries();

      expect(cards()[0].querySelector('.csp-card-title')?.textContent?.trim()).toBe('Model not recorded');
    });

    it('offers a battery result the server would exclude aria-disabled, described by its reason', () => {
      render({
        batteryRuns: [
          buildBattery({ id: 4 }),
          buildBattery({ id: 5, status: 'Running' }),
          buildBattery({ id: 6, latestAnalysisId: null, latestAnalysisAtUtc: null }),
          buildBattery({ id: 7, latestAnalysisComplete: false }),
          buildBattery({ id: 8, analysisStale: true })
        ]
      });
      showBatteries();
      const reason = (id: number): string =>
        (fixture.nativeElement.querySelector(`#csp-battery-${id}-reason`) as HTMLElement).textContent ?? '';

      expect(box('#csp-battery-4').getAttribute('aria-disabled')).toBeNull();
      expect(fixture.nativeElement.querySelector('#csp-battery-4-reason')).toBeNull();
      for (const id of [5, 6, 7, 8]) {
        expect(box(`#csp-battery-${id}`).disabled).toBe(false);
        expect(box(`#csp-battery-${id}`).getAttribute('aria-disabled')).toBe('true');
        expect(box(`#csp-battery-${id}`).getAttribute('aria-describedby')).toBe(`csp-battery-${id}-reason`);
        expect(box(`#csp-battery-${id}`).getAttribute('aria-label')).toBe(`Include battery run ${id} in the comparison`);
      }
      expect(reason(5)).toContain('only a finished battery run can be compared');
      expect(reason(6)).toContain('has no analysis');
      expect(reason(7)).toContain('incomplete analysis');
      expect(reason(8)).toContain('stale analysis');

      let emitted = 0;
      component.selectionChange.subscribe(() => emitted++);
      box('#csp-battery-5').click();
      expect(box('#csp-battery-5').checked).toBe(false);
      component.toggleBattery(component.batteryRuns[1]);
      expect(emitted).toBe(0);
    });

    it('emits battery results with the rest of the selection, and counts them on the tab', () => {
      render({ batteryRuns: [buildBattery({ id: 4 }), buildBattery({ id: 9 })], selectedBatteryRunIds: [4] });
      const emitted: ModelComparisonSelection[] = [];
      component.selectionChange.subscribe(value => emitted.push(value));

      box('#csp-battery-9').click();

      expect(emitted).toEqual([{ runIds: [], groupIds: [], batteryRunIds: [4, 9] }]);
      const badge = fixture.nativeElement.querySelector('#csp-src-tab-batteries .csp-tab-selected') as HTMLElement;
      expect(badge.textContent?.trim()).toBe('1 selected');
    });

    it('narrows by battery on the exact name through the Battery facet', () => {
      render({
        batteryRuns: [
          buildBattery({ id: 4, batteryName: 'Core' }),
          buildBattery({ id: 5, batteryName: 'Core Plus' })
        ]
      });
      showBatteries();

      const facet = component.facetsOf('batteries').find(candidate => candidate.column === 'battery')!;
      expect(facet.options.map(option => option.label)).toEqual(['Core', 'Core Plus']);
      component.onFacetChange('batteries', 'battery', ['Core']);
      fixture.detectChanges();

      expect(component.batteryList.view(component.batteryRuns).map(battery => battery.id)).toEqual([4]);
    });

    it('keeps battery results apart from runs and groups, saying why in a visible line', () => {
      render({ batteryRuns: [buildBattery({ id: 4 })], selectedRunIds: [1] });
      showBatteries();
      const emitted: ModelComparisonSelection[] = [];
      component.selectionChange.subscribe(value => emitted.push(value));

      const note = fixture.nativeElement.querySelector('#csp-mix-note-batteries') as HTMLElement;
      expect(note.textContent?.trim()).toBe(SOURCE_MIX_NOTE);
      const battery = box('#csp-battery-4');
      // aria-disabled, not disabled: it stays focusable, and its description is the note.
      expect(battery.disabled).toBe(false);
      expect(battery.getAttribute('aria-disabled')).toBe('true');
      expect(battery.getAttribute('aria-describedby')).toBe('csp-mix-note-batteries');

      battery.click();
      expect(battery.checked).toBe(false);
      expect(emitted).toEqual([]);
    });

    it('keeps runs and groups apart from battery results the other way round', () => {
      render({ batteryRuns: [buildBattery({ id: 4 })], selectedBatteryRunIds: [4] });
      component.selectSourceTab('runs');
      fixture.detectChanges();
      const emitted: ModelComparisonSelection[] = [];
      component.selectionChange.subscribe(value => emitted.push(value));

      expect((fixture.nativeElement.querySelector('#csp-mix-note-runs') as HTMLElement).textContent?.trim())
        .toBe(SOURCE_MIX_NOTE);
      const run = box('#csp-run-1');
      expect(run.getAttribute('aria-disabled')).toBe('true');
      expect(run.getAttribute('aria-describedby')).toBe('csp-mix-note-runs');
      run.click();
      expect(run.checked).toBe(false);

      component.selectSourceTab('groups');
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('#csp-mix-note-groups')).toBeTruthy();
      const group = box('#csp-group-1');
      expect(group.getAttribute('aria-disabled')).toBe('true');
      group.click();

      expect(emitted).toEqual([]);
    });

    it('shows no mixing line while nothing blocks the kind', () => {
      render({ batteryRuns: [buildBattery({ id: 4 })] });
      expect(fixture.nativeElement.querySelector('.csp-mix-note')).toBeNull();
      expect(box('#csp-run-1').getAttribute('aria-disabled')).toBeNull();
      showBatteries();
      expect(fixture.nativeElement.querySelector('.csp-mix-note')).toBeNull();
      expect(box('#csp-battery-4').getAttribute('aria-disabled')).toBeNull();
    });

    it('opens on the Battery results tab when battery results arrive as the selection', () => {
      render({ batteryRuns: [buildBattery({ id: 4 })] });
      expect(component.activeSourceTab).toBe('runs');

      fixture.componentRef.setInput('selectedBatteryRunIds', [4]);
      fixture.detectChanges();

      expect(component.activeSourceTab).toBe('batteries');
      expect(fixture.nativeElement.querySelector('#csp-src-panel-batteries')).toBeTruthy();
    });

    it('sorts Recently analyzed by the instant, reading a server timestamp without a zone as UTC', () => {
      render({
        batteryRuns: [
          // 15:00 UTC: no zone, as System.Text.Json writes an unspecified DateTime.
          buildBattery({ id: 4, latestAnalysisAtUtc: '2026-10-01T15:00:00' }),
          buildBattery({ id: 5, latestAnalysisAtUtc: '2026-10-01T14:30:00Z' }),
          // 15:45 UTC.
          buildBattery({ id: 6, latestAnalysisAtUtc: '2026-10-01T17:45:00+02:00' })
        ]
      });

      expect(component.batteryList.setSort('analyzed')).toBe(true);
      expect(component.batteryList.view(component.batteryRuns).map(battery => battery.id)).toEqual([6, 4, 5]);
      component.batteryTable.setSort('analyzedAtUtc', 'asc');
      expect(component.batteryList.view(component.batteryRuns).map(battery => battery.id)).toEqual([5, 4, 6]);

      // Shown in the browser's own time zone, from the UTC instant.
      expect(component.formatDate('2026-10-01T15:00:00')).toBe(new Date(Date.UTC(2026, 9, 1, 15, 0, 0)).toLocaleString());
      expect(component.formatDate('2026-10-01T15:00:00Z')).toBe(new Date(Date.UTC(2026, 9, 1, 15, 0, 0)).toLocaleString());
    });

    it('offers the suite scope on the run and group tabs only, with no caveat line', () => {
      render();
      const select = (): HTMLSelectElement | null => fixture.nativeElement.querySelector('#csp-suite');

      expect(select()).toBeTruthy();
      expect(select()!.hasAttribute('aria-describedby')).toBe(false);
      expect(fixture.nativeElement.querySelector('#csp-suite-hint')).toBeNull();

      component.selectSourceTab('groups');
      fixture.detectChanges();
      expect(select()).toBeTruthy();

      showBatteries();
      expect(select()).toBeNull();
      expect(fixture.nativeElement.querySelector('#csp-legend-trigger')).toBeTruthy();
    });
  });
});
