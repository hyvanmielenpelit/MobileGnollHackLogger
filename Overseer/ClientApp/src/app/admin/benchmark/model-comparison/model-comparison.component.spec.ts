import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideCharts } from 'ng2-charts';

import { ComparisonWizardStep, ModelComparisonComponent } from './model-comparison.component';
import { MAX_PLOTTED_ENTRIES, MODEL_COMPARISON_REGISTRABLES, P1_STACK_BREAKPOINT_PX } from './model-comparison-charts';
import {
  BenchmarkModelComparisonDto,
  BenchmarkModelComparisonEntryDto,
  toChartContext,
  toChartEntries
} from './model-comparison.models';

describe('ModelComparisonComponent', () => {
  let component: ModelComparisonComponent;
  let fixture: ComponentFixture<ModelComparisonComponent>;

  /**
   * A comparable entry with plausible measures on all three axes.
   *
   * Every field the server declares is populated, because the view's whole job is to distinguish a
   * measured entry from an unmeasured one and a fixture with holes in it would pass by accident.
   */
  function buildEntry(overrides: Partial<BenchmarkModelComparisonEntryDto> = {}): BenchmarkModelComparisonEntryDto {
    const key = overrides.key ?? 'run:1';
    return {
      key,
      sourceKind: 'Run',
      sourceId: 1,
      sourceName: null,
      runIds: [1],
      runCount: 3,
      suiteId: 5,
      suiteName: 'GnollHack Player Assistance Benchmark Suite',
      provider: 'Google',
      modelId: 'gemini-2.5-flash',
      modelDisplayName: 'Gemini 2.5 Flash',
      thinkingLevel: 'medium',
      reasoningMode: null,
      reasoningSummary: null,
      serviceTier: null,
      maxOutputTokens: 8192,
      parallelExecutionMode: 'Enabled',
      label: `Model ${key}`,
      firstRunStartedAtUtc: '2026-09-01T10:00:00Z',
      lastRunStartedAtUtc: '2026-09-02T10:00:00Z',
      state: 'Comparable',
      comparable: true,
      excluded: false,
      speedDegraded: false,
      costDegraded: false,
      excludingKeys: [],
      speedDegradingKeys: [],
      costDegradingKeys: [],
      differences: [],
      explanation: 'Comparable with the baseline on every must-match key.',
      quality: {
        pointEstimate: 68,
        itemCount: 18,
        intervalHalfWidth: 6.4,
        intervalLower: 61.6,
        intervalUpper: 74.4,
        intervalTruncated: false,
        itemSamplingHalfWidth: 5.9,
        reproducibilityHalfWidth: 2.5,
        reproducibilityStandardDeviation: 1.4,
        reproducibilityAvailable: true,
        intervalBasis: 'item sampling and reproducibility'
      },
      speed: {
        ttftP50Ms: 4200,
        ttftP90Ms: 9100,
        ttftAnswerCount: 54,
        modelTimeP50Ms: 31000,
        modelTimeP90Ms: 72000,
        pooledAnswerCount: 54,
        degraded: false,
        degradedReason: null,
        caveat: 'Timings were recorded under parallel question execution.'
      },
      cost: {
        candidateCostPerQuestionUsd: 0.0123,
        candidateCostPerRunUsd: 0.2214,
        candidateTotalCostUsd: 0.6642,
        basis: 'Current',
        pricingAsOf: '2026-09-01',
        pricingResolved: true,
        degraded: false,
        degradedReason: null,
        scheduledChangeEffectiveFrom: null,
        scheduledChangeNote: null
      },
      table: {
        meanSpeedIndex: 88,
        speedIndexSaturated: false,
        speedIndexCeilingAnswerCount: 3,
        speedIndexScoredAnswerCount: 54,
        costPerIndexPointUsd: 0.0032,
        meanStoredQualityIndex: 67.4,
        unstableItemCount: 0
      },
      ...overrides
    };
  }

  /**
   * An excluded entry, shaped the way the service returns one: all four measure objects null, so a
   * chart cannot render it by ignoring a flag.
   */
  function buildExcludedEntry(key: string, keys: string[]): BenchmarkModelComparisonEntryDto {
    return buildEntry({
      key,
      label: `Model ${key}`,
      state: 'Excluded',
      comparable: false,
      excluded: true,
      excludingKeys: keys,
      explanation: `Not comparable: ${keys.join(', ')} differs from the baseline.`,
      quality: null,
      speed: null,
      cost: null,
      table: null,
      differences: [
        {
          name: keys[0],
          kind: 'Instrument',
          description: 'The scoring method version the answers were graded under.',
          variants: [
            { value: 'v8', runIds: [1, 2] },
            { value: 'v9', runIds: [9] }
          ]
        }
      ]
    });
  }

  function buildDto(
    entries: BenchmarkModelComparisonEntryDto[],
    overrides: Partial<BenchmarkModelComparisonDto> = {}
  ): BenchmarkModelComparisonDto {
    const excluded = entries.filter(entry => entry.excluded).length;
    return {
      pricingBasis: 'Current',
      pricingBasisLabel: 'Current catalog, as of 2026-09-07',
      computedAtUtc: '2026-09-07T12:00:00Z',
      baselineSuiteId: 5,
      baselineSuiteName: 'GnollHack Player Assistance Benchmark Suite',
      baselineEntryKeys: entries.filter(entry => !entry.excluded).map(entry => entry.key),
      baselineKeyValues: { BenchmarkSuiteId: '5', ScoringMethodVersion: 'v8' },
      modelAxisKeys: ['Provider', 'ModelId', 'ThinkingLevel'],
      entries,
      comparableCount: entries.length - excluded,
      excludedCount: excluded,
      thinkingLevelsDiffer: false,
      speedAxisCaveat: null,
      explanation: `${entries.length - excluded} of ${entries.length} entries may be charted.`,
      excludedMeasures: [
        {
          measure: 'Speed Index',
          reason: 'It saturates: several models sit at the ceiling while their real latency differs.',
          instead: 'Time to first token, P50.'
        }
      ],
      ...overrides
    };
  }

  /**
   * Renders one payload through the input, which is the only way the host feeds this component,
   * and opens one wizard step.
   *
   * The step is explicit because the wizard opens on step 1 — the projected source picker — and
   * almost every assertion below is about the two steps behind it. Step 2 is the default: it
   * carries the filters, the caveats and the comparison table. `goToStep` refuses an unreachable
   * step, so a test that asks for step 3 over an unchartable set finds an empty panel rather than
   * a quietly passing assertion.
   */
  function render(dto: BenchmarkModelComparisonDto | null, step: ComparisonWizardStep = 2): void {
    fixture.componentRef.setInput('comparison', dto);
    fixture.detectChanges();
    component.goToStep(step);
    fixture.detectChanges();
  }

  /**
   * Re-renders after a field was set directly.
   *
   * Every real interaction reaches this component through a template event or an input, both of
   * which mark its view; `ApplicationRef.tick` refreshes only what is marked, so a spec writing a
   * field straight onto the instance has to check that view itself.
   */
  function refresh(): void {
    (component as unknown as { cdr: ChangeDetectorRef }).cdr.detectChanges();
  }

  function comparableSet(count: number): BenchmarkModelComparisonEntryDto[] {
    return Array.from({ length: count }, (_unused, index) =>
      buildEntry({
        key: `run:${index + 1}`,
        sourceId: index + 1,
        runIds: [index + 1],
        label: `Model ${index + 1}`,
        quality: { ...buildEntry().quality!, pointEstimate: 50 + index * 3 },
        speed: { ...buildEntry().speed!, ttftP50Ms: 2000 + index * 900, ttftP90Ms: 5000 + index * 1200 },
        cost: { ...buildEntry().cost!, candidateCostPerQuestionUsd: 0.004 + index * 0.003 }
      })
    );
  }

  function textOf(selector: string): string {
    return fixture.debugElement.queryAll(By.css(selector))
      .map(element => (element.nativeElement as HTMLElement).textContent ?? '')
      .join(' ');
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ModelComparisonComponent],
      providers: [provideCharts({ registerables: MODEL_COMPARISON_REGISTRABLES })]
    }).compileComponents();

    fixture = TestBed.createComponent(ModelComparisonComponent);
    component = fixture.componentInstance;
  });

  // -------------------------------------------------------------------------------------------
  // The table view
  // -------------------------------------------------------------------------------------------

  it('renders the table view alongside the figures, not behind a toggle', () => {
    render(buildDto(comparableSet(4)));

    const table = fixture.debugElement.query(By.css('table.mc-table'));
    expect(table).withContext('the table view must always be in the DOM').toBeTruthy();
    expect(fixture.debugElement.queryAll(By.css('app-table-pager')).length).toBe(2);
    expect(fixture.debugElement.queryAll(By.css('tbody tr')).length).toBe(4);

    // Nothing in the view may hide the table, so there is no control that could.
    const toggles = fixture.debugElement
      .queryAll(By.css('button'))
      .map(element => (element.nativeElement as HTMLElement).getAttribute('aria-label') ?? '');
    expect(toggles.some(label => /show table|hide table|table view/i.test(label))).toBeFalse();
  });

  it('keeps an excluded entry in the table even though no figure can draw it', () => {
    render(buildDto([...comparableSet(3), buildExcludedEntry('run:9', ['ScoringMethodVersion'])]));

    expect(fixture.debugElement.queryAll(By.css('tbody tr')).length).toBe(4);
    expect(component.figures?.selection.plotted.length).toBe(3);
    expect(component.figures?.selection.excluded.length).toBe(1);
  });

  // -------------------------------------------------------------------------------------------
  // Exclusions
  // -------------------------------------------------------------------------------------------

  it('names the comparability keys an excluded entry differs on', () => {
    render(buildDto([...comparableSet(3), buildExcludedEntry('run:9', ['ScoringMethodVersion'])]));

    const excluded = textOf('.mc-excluded');
    expect(excluded).toContain('ScoringMethodVersion');
    expect(excluded).toContain('v8');
    expect(excluded).toContain('v9');
    expect(textOf('.mc-notices')).toContain('not comparable');
  });

  it('draws no axes at all when nothing in the set may be charted together', () => {
    render(buildDto([
      buildExcludedEntry('run:8', ['ScoringMethodVersion']),
      buildExcludedEntry('run:9', ['CandidatePromptOptions'])
    ]));

    expect(component.shape).toBe('none');
    expect(fixture.debugElement.queryAll(By.css('canvas')).length).toBe(0);
    expect(textOf('.alert-heading')).toContain('Nothing in this set may be charted together');
    // The refusal is explained and the entries stay listed.
    expect(fixture.debugElement.queryAll(By.css('tbody tr')).length).toBe(2);
  });

  // -------------------------------------------------------------------------------------------
  // Degenerate shapes
  // -------------------------------------------------------------------------------------------

  it('renders a KPI row rather than a one-bar chart for a single entry', () => {
    render(buildDto(comparableSet(1)));

    expect(component.shape).toBe('single');
    expect(fixture.debugElement.queryAll(By.css('canvas')).length).toBe(0);
    expect(fixture.debugElement.queryAll(By.css('.mc-kpi')).length).toBe(3);
    expect(textOf('.mc-kpi-label')).toContain('Intelligence Index');
    expect(textOf('.mc-kpi-label')).toContain('Time to first token, P50');
  });

  it('suppresses the profile plot at two entries and keeps P1 and the scatters', () => {
    render(buildDto(comparableSet(2)), 3);

    expect(component.shape).toBe('pair');
    expect(component.profileCard).toBeNull();
    expect(component.panelCards.length).toBe(3);
    expect(component.scatterCards.length).toBe(3);
    expect(fixture.debugElement.queryAll(By.css('canvas')).length).toBe(6);
  });

  it('renders all six figures from three entries upward', () => {
    render(buildDto(comparableSet(3)), 3);

    expect(component.shape).toBe('full');
    expect(fixture.debugElement.queryAll(By.css('canvas')).length).toBe(7);
    expect(component.profileCard?.caption).toContain('read shape and crossings, not values');
  });

  // -------------------------------------------------------------------------------------------
  // The filter row
  // -------------------------------------------------------------------------------------------

  it('holds exactly one filter row, and no filter inside any chart card', () => {
    render(buildDto(comparableSet(4)));
    expect(fixture.debugElement.queryAll(By.css('.mc-filters')).length).toBe(1);

    // The figures are a step further on, and none of them may carry a control of its own: six
    // figures scoped by six controls would each describe a different slice of one set.
    component.goToStep(3);
    fixture.detectChanges();
    expect(fixture.debugElement.queryAll(By.css('.mc-filters')).length).toBe(0);
    expect(fixture.debugElement.queryAll(By.css('.mc-card select, .mc-card input')).length).toBe(0);
  });

  it('carries no suite control: suite scope is a selection-stage control and belongs to the picker', () => {
    render(buildDto(comparableSet(4)));

    expect(fixture.debugElement.query(By.css('#mc-suite'))).toBeNull();
    // The suite the figures describe stays on screen, read off the payload itself.
    expect(textOf('.mc-meta')).toContain('GnollHack Player Assistance Benchmark Suite');
  });

  it('scopes every one of the six figures with one entry selection', () => {
    render(buildDto(comparableSet(4)));
    expect(component.figures?.selection.plotted.length).toBe(4);

    component.toggleEntry('run:2');
    fixture.detectChanges();

    const figures = component.figures!;
    const modelDatasets = (spec: { config: { data: { datasets: { label?: string }[] } } }): number =>
      spec.config.data.datasets.filter(dataset => dataset.label !== 'Pareto frontier').length;

    expect(figures.selection.plotted.length).toBe(3);
    expect(figures.smallMultiples.order.length).toBe(3);
    expect(figures.smallMultiples.quality.config.data.labels?.length).toBe(3);
    expect(figures.smallMultiples.speed.config.data.labels?.length).toBe(3);
    expect(figures.smallMultiples.cost.config.data.labels?.length).toBe(3);
    expect(modelDatasets(figures.qualitySpeed)).toBe(3);
    expect(modelDatasets(figures.qualityCost)).toBe(3);
    expect(modelDatasets(figures.speedCost)).toBe(3);
    expect(figures.profile.config.data.datasets.length).toBe(3);
    expect(textOf('.mc-notices')).toContain('Model 2');
  });

  it('reorders all three P1 panels together from the one order control', () => {
    render(buildDto(comparableSet(3)));
    const descending = component.figures!.smallMultiples.order;

    component.onSortDirectionChange('asc');
    fixture.detectChanges();

    const panels = component.figures!.smallMultiples;
    expect(panels.order).toEqual([...descending].reverse());
    expect(panels.quality.config.data.labels).toEqual(panels.speed.config.data.labels);
    expect(panels.quality.config.data.labels).toEqual(panels.cost.config.data.labels);
  });

  it('keeps a model glyph stable when another model is filtered out', () => {
    render(buildDto(comparableSet(4)));
    const before = component.glyph('run:4');

    component.toggleEntry('run:1');
    fixture.detectChanges();

    expect(component.glyph('run:4')).toEqual(before);
  });

  it('holds degraded entries out of the figures under strict comparability', () => {
    const entries = comparableSet(4);
    entries[3] = { ...entries[3], state: 'Degraded', costDegraded: true, costDegradingKeys: ['PricingSnapshot'] };
    render(buildDto(entries));
    expect(component.figures?.selection.plotted.length).toBe(4);

    component.onStrictnessChange('comparableOnly');
    fixture.detectChanges();

    expect(component.figures?.selection.plotted.length).toBe(3);
    expect(textOf('.mc-notices')).toContain('Strict comparability is on');
    // Withheld, not hidden: the table still carries it.
    expect(fixture.debugElement.queryAll(By.css('tbody tr')).length).toBe(4);
  });

  it('raises the saturation notice on the speed panel when the Speed Index measure is chosen', () => {
    const entries = comparableSet(3);
    entries[0] = { ...entries[0], table: { ...entries[0].table!, speedIndexSaturated: true } };
    render(buildDto(entries));
    expect(component.figures?.smallMultiples.speed.notices.length).toBe(0);

    component.onSpeedMeasureChange('speedIndex');
    fixture.detectChanges();

    expect(component.figures?.smallMultiples.speed.notices.join(' ')).toContain('saturated');
  });

  it('offers no total-run cost measure, because the endpoint carries candidate spend only', () => {
    render(buildDto(comparableSet(3)));

    const option = fixture.debugElement.query(By.css('#mc-cost-measure option[value="totalRun"]'));
    expect(option).toBeTruthy();
    expect((option.nativeElement as HTMLOptionElement).disabled).toBeTrue();
  });

  // -------------------------------------------------------------------------------------------
  // Uncertainty
  // -------------------------------------------------------------------------------------------

  it('marks a set in which every plotted entry rests on a single run', () => {
    const entries = comparableSet(3).map(entry => ({ ...entry, runCount: 1 }));
    render(buildDto(entries));

    expect(component.allSingleRun).toBeTrue();
    expect(textOf('.alert-heading')).toContain('n = 1');
    expect(fixture.debugElement.queryAll(By.css('tbody .mc-n1')).length).toBe(3);
  });

  it('says that no cost interval is available rather than drawing bars without one', () => {
    render(buildDto(comparableSet(3)));

    expect(component.setNotices.join(' ')).toContain('Cost bars carry no interval');
  });

  // -------------------------------------------------------------------------------------------
  // The eight-entry cap
  // -------------------------------------------------------------------------------------------

  it('notices the models the eight-entry cap pushed out and keeps them in the table', () => {
    const entries = comparableSet(MAX_PLOTTED_ENTRIES + 1);
    render(buildDto(entries));

    expect(component.figures?.selection.plotted.length).toBe(MAX_PLOTTED_ENTRIES);
    expect(component.figures?.selection.overflow.length).toBe(1);
    expect(textOf('.mc-notices')).toContain(`Charts plot at most ${MAX_PLOTTED_ENTRIES} models`);
    expect(component.entryTable.filteredCount(component.entries)).toBe(MAX_PLOTTED_ENTRIES + 1);
  });

  // -------------------------------------------------------------------------------------------
  // Set-level caveats
  // -------------------------------------------------------------------------------------------

  it('renders the thinking-level caveat and the measures left off every axis', () => {
    render(buildDto(comparableSet(3), {
      thinkingLevelsDiffer: true,
      speedAxisCaveat: 'Speed is not comparable across thinking levels.'
    }));

    expect(textOf('.alert-body')).toContain('Speed is not comparable across thinking levels.');
    expect(textOf('.mc-measures')).toContain('Speed Index');
    expect(textOf('.mc-measures')).toContain('Time to first token, P50.');
  });

  // -------------------------------------------------------------------------------------------
  // Layout
  // -------------------------------------------------------------------------------------------

  it('turns P1 bars horizontal at the same width the panels stack at', () => {
    render(buildDto(comparableSet(3)));

    component.applyContainerWidth(P1_STACK_BREAKPOINT_PX - 1);
    fixture.detectChanges();
    expect(component.orientation).toBe('horizontal');

    component.applyContainerWidth(P1_STACK_BREAKPOINT_PX);
    fixture.detectChanges();
    expect(component.orientation).toBe('vertical');
  });

  // -------------------------------------------------------------------------------------------
  // Query controls
  // -------------------------------------------------------------------------------------------

  it('emits the one control that changes what the host fetches', () => {
    render(buildDto(comparableSet(3)));
    const bases: string[] = [];
    component.pricingBasisChange.subscribe(value => bases.push(value));

    component.onPricingBasisChange('AsRun');

    expect(bases).toEqual(['AsRun']);
  });

  // -------------------------------------------------------------------------------------------
  // Figure export
  // -------------------------------------------------------------------------------------------

  it('offers a download control on every figure card and one for the whole set', () => {
    render(buildDto(comparableSet(3)), 3);

    const cards = fixture.debugElement.queryAll(By.css('.mc-card')).length;
    const downloads = fixture.debugElement.queryAll(By.css('.mc-card .mc-download'));
    expect(cards).toBe(7);
    expect(downloads.length).toBe(7);
    // An icon-only button has no text, so aria-label is its accessible name.
    expect((downloads[0].nativeElement as HTMLElement).getAttribute('aria-label'))
      .toContain('Download ');

    expect(fixture.debugElement.query(By.css('#mc-export-format'))).toBeTruthy();
    expect(textOf('.mc-export')).toContain('Download all figures');
  });

  it('hides the export controls where no figure is rendered', () => {
    // Not merely hidden: the whole Figures step refuses to open, because there is nothing on it.
    render(buildDto(comparableSet(1)), 3);
    expect(component.shape).toBe('single');
    expect(component.isStepReachable(3)).toBeFalse();
    expect(fixture.debugElement.query(By.css('.mc-export'))).toBeNull();
    expect(fixture.debugElement.queryAll(By.css('.mc-download')).length).toBe(0);

    render(buildDto([
      buildExcludedEntry('run:8', ['ScoringMethodVersion']),
      buildExcludedEntry('run:9', ['CandidatePromptOptions'])
    ]), 3);
    expect(component.shape).toBe('none');
    expect(component.isStepReachable(3)).toBeFalse();
    expect(fixture.debugElement.query(By.css('.mc-export'))).toBeNull();

    render(null);
    expect(component.shape).toBe('empty');
    expect(component.isStepReachable(2)).toBeFalse();
    expect(fixture.debugElement.query(By.css('.mc-export'))).toBeNull();
    expect(component.canExport).toBeFalse();
  });

  it('lists every rendered card as exportable, in the order they are drawn', () => {
    render(buildDto(comparableSet(3)));

    const ids = component.exportableCards.map(card => card.id);
    expect(ids.length).toBe(7);
    expect(ids).toEqual([
      ...component.panelCards.map(card => card.id),
      component.profileCard!.id,
      ...component.scatterCards.map(card => card.id)
    ]);
  });
  // -------------------------------------------------------------------------------------------
  // The wizard
  // -------------------------------------------------------------------------------------------

  it('gates Next on step 1 on the source selection, and names the reason as visible text', () => {
    render(null);
    expect(component.step).toBe(1);
    expect(component.nextLabel).toBe('Compare');
    expect(component.canGoNext).toBeFalse();
    expect(textOf('.mc-wizard-blocked')).toContain('at least one run or analysis group');

    fixture.componentRef.setInput('selectedSourceCount', component.maxSources + 1);
    fixture.detectChanges();
    expect(component.canGoNext).toBeFalse();
    expect(textOf('.mc-wizard-blocked')).toContain(`at most ${component.maxSources}`);

    fixture.componentRef.setInput('selectedSourceCount', 2);
    fixture.detectChanges();
    expect(component.canGoNext).toBeTrue();
    expect(component.nextBlockedReason).toBe('');
    expect(fixture.debugElement.query(By.css('.mc-wizard-blocked'))).toBeNull();
  });

  it('emits compare from the footer rather than advancing, while no comparison exists', () => {
    render(null);
    fixture.componentRef.setInput('selectedSourceCount', 2);
    fixture.detectChanges();

    const asked: number[] = [];
    component.compare.subscribe(() => asked.push(1));
    component.nextStep();

    // The step advances when the payload lands, not on the click: advancing now would show an
    // empty step 2 for the length of the round trip.
    expect(asked.length).toBe(1);
    expect(component.step).toBe(1);
  });

  it('advances on the first comparison, stays put on a refetch, and drops back when it is lost', () => {
    fixture.componentRef.setInput('comparison', null);
    fixture.detectChanges();
    expect(component.step).toBe(1);

    fixture.componentRef.setInput('comparison', buildDto(comparableSet(3)));
    fixture.detectChanges();
    expect(component.step).toBe(2);

    component.goToStep(3);
    // A pricing-basis refetch replaces one payload with another; it must not move the reader.
    fixture.componentRef.setInput('comparison', buildDto(comparableSet(3)));
    fixture.detectChanges();
    expect(component.step).toBe(3);

    fixture.componentRef.setInput('comparison', null);
    fixture.detectChanges();
    expect(component.step).toBe(1);
  });

  it('marks Next on step 2 aria-disabled where nothing may be charted, never disabled', () => {
    render(buildDto([
      buildExcludedEntry('run:8', ['ScoringMethodVersion']),
      buildExcludedEntry('run:9', ['CandidatePromptOptions'])
    ]));

    expect(component.step).toBe(2);
    expect(component.canGoNext).toBeFalse();
    expect(component.isStepReachable(3)).toBeFalse();

    const next = fixture.debugElement.queryAll(By.css('.mc-wizard-nav .btn-gh'))[1]
      .nativeElement as HTMLElement;
    // aria-disabled, not disabled: a disabled button cannot be focused, and the reader would be
    // left guessing why Next does nothing.
    expect(next.getAttribute('aria-disabled')).toBe('true');
    expect(next.hasAttribute('disabled')).toBeFalse();
    expect(textOf('.mc-wizard-blocked')).toContain('Nothing in this set may be charted together');

    const figuresTab = fixture.debugElement
      .queryAll(By.css('.mc-wizard-steps .gh-tab'))[2].nativeElement as HTMLElement;
    expect(figuresTab.getAttribute('aria-disabled')).toBe('true');
    expect(figuresTab.hasAttribute('disabled')).toBeFalse();
  });

  it('names the single-entry case separately, because it is a different fix', () => {
    render(buildDto(comparableSet(1)));

    expect(component.shape).toBe('single');
    expect(component.nextBlockedReason).toContain('Only one entry is plotted');
  });

  it('labels step 3 Next as Close and emits closeRequested from it', () => {
    render(buildDto(comparableSet(3)), 3);
    expect(component.step).toBe(3);
    expect(component.nextLabel).toBe('Close');

    const closed: number[] = [];
    component.closeRequested.subscribe(() => closed.push(1));
    component.nextStep();

    expect(closed.length).toBe(1);
  });

  it('draws the figures on step 3 and keeps the comparison table on step 2, always reachable', () => {
    render(buildDto(comparableSet(3)), 3);
    expect(fixture.debugElement.queryAll(By.css('canvas')).length).toBe(7);
    expect(fixture.debugElement.query(By.css('table.mc-table'))).toBeNull();

    component.goToStep(2);
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('table.mc-table'))).toBeTruthy();
    expect(fixture.debugElement.queryAll(By.css('canvas')).length).toBe(0);
  });

  it('drives the step tablist with a roving tabindex and the arrow keys', () => {
    render(buildDto(comparableSet(3)));

    const tabs = fixture.debugElement.queryAll(By.css('.mc-wizard-steps .gh-tab'))
      .map(tab => tab.nativeElement as HTMLElement);
    expect(tabs.length).toBe(3);
    expect(tabs[1].getAttribute('aria-selected')).toBe('true');
    expect(tabs[1].getAttribute('tabindex')).toBe('0');
    expect(tabs[0].getAttribute('tabindex')).toBe('-1');
    expect(tabs[2].getAttribute('tabindex')).toBe('-1');

    component.onStepKeydown(new KeyboardEvent('keydown', { key: 'ArrowLeft' }), 1);
    fixture.detectChanges();
    expect(component.step).toBe(1);

    component.onStepKeydown(new KeyboardEvent('keydown', { key: 'End' }), 0);
    fixture.detectChanges();
    expect(component.step).toBe(3);
  });

  it('survives being measured at width zero, which is what a closed dialog reports', () => {
    render(buildDto(comparableSet(3)), 3);

    expect(() => component.applyContainerWidth(0)).not.toThrow();
    expect(component.orientation).toBe('vertical');
  });

  it('disables its own close controls while an export is running, and nothing else', () => {
    render(buildDto(comparableSet(3)), 3);
    component.exporting = true;
    refresh();

    const close = fixture.debugElement.query(By.css('.mc-wizard-header .btn-icon-action'))
      .nativeElement as HTMLButtonElement;
    const next = fixture.debugElement.queryAll(By.css('.mc-wizard-nav .btn-gh'))[1]
      .nativeElement as HTMLButtonElement;
    expect(close.disabled).toBeTrue();
    expect(next.disabled).toBeTrue();

    component.exporting = false;
    refresh();
    expect(close.disabled).toBeFalse();
    expect(next.disabled).toBeFalse();
  });

  // -------------------------------------------------------------------------------------------
  // Export resolution
  // -------------------------------------------------------------------------------------------

  it('shows the two custom size inputs only for Custom, and names what will be written', () => {
    render(buildDto(comparableSet(3)), 3);
    expect(fixture.debugElement.query(By.css('#mc-export-width'))).toBeNull();
    expect(textOf('.mc-export-dimensions')).toContain('on-screen size');

    component.onExportResolutionChange('fullhd');
    refresh();
    expect(textOf('.mc-export-dimensions')).toContain('1920 × 1080 px');
    expect(textOf('.mc-export-dimensions')).toContain('2× density');

    component.onExportResolutionChange('custom');
    refresh();
    expect(fixture.debugElement.query(By.css('#mc-export-width'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('#mc-export-height'))).toBeTruthy();
  });

  it('refuses an out-of-range custom size in words, and will not export under one', () => {
    render(buildDto(comparableSet(3)), 3);
    component.onExportResolutionChange('custom');
    component.customExportHeight = 10;
    refresh();

    expect(component.customResolutionError).toContain('height');
    expect(component.customResolutionError).toContain(`${component.maxExportDimension}`);
    expect(component.canExport).toBeFalse();
    expect(textOf('.mc-export-error')).toContain('height');

    component.customExportHeight = 1080;
    refresh();
    expect(component.customResolutionError).toBe('');
    expect(component.canExport).toBeTrue();
  });

});

/**
 * The projected step-1 content, which is the source picker in the running application.
 *
 * Its own state — two TableState instances holding a sort column, a page and a set of filters —
 * is exactly what would be lost if step 1 were an @if rather than [hidden], so this asserts the
 * element survives a round trip through another step rather than being re-created.
 */
@Component({
  standalone: true,
  imports: [ModelComparisonComponent],
  template: `
    <app-benchmark-model-comparison [comparison]="comparison" [selectedSourceCount]="2">
      <input id="projected-picker-state" type="text">
    </app-benchmark-model-comparison>`
})
class ProjectionHostComponent {
  /** As the real host does: it checks its own view after every mutation it makes. */
  readonly cdr = inject(ChangeDetectorRef);

  comparison: BenchmarkModelComparisonDto | null = null;
}

describe('ModelComparisonComponent projected step 1', () => {
  it('keeps the projected content alive across a step away and back', async () => {
    await TestBed.configureTestingModule({
      imports: [ProjectionHostComponent],
      providers: [provideCharts({ registerables: MODEL_COMPARISON_REGISTRABLES })]
    }).compileComponents();

    const fixture = TestBed.createComponent(ProjectionHostComponent);
    fixture.detectChanges();

    const wizard = fixture.debugElement
      .query(By.directive(ModelComparisonComponent)).componentInstance as ModelComparisonComponent;
    const before = fixture.debugElement.query(By.css('#projected-picker-state'))
      .nativeElement as HTMLInputElement;
    before.value = 'sorted by condition, page 3';

    fixture.componentInstance.comparison = {
      pricingBasis: 'Current',
      pricingBasisLabel: 'Current catalog',
      computedAtUtc: '2026-09-07T12:00:00Z',
      baselineSuiteId: 5,
      baselineSuiteName: 'Suite',
      baselineEntryKeys: [],
      baselineKeyValues: {},
      modelAxisKeys: [],
      entries: [],
      comparableCount: 0,
      excludedCount: 0,
      thinkingLevelsDiffer: false,
      speedAxisCaveat: null,
      explanation: '',
      excludedMeasures: []
    };
    fixture.componentInstance.cdr.detectChanges();
    expect(wizard.step).toBe(2);

    wizard.goToStep(1);
    fixture.componentInstance.cdr.detectChanges();

    const after = fixture.debugElement.query(By.css('#projected-picker-state'))
      .nativeElement as HTMLInputElement;
    expect(after).toBe(before);
    expect(after.value).toBe('sorted by condition, page 3');
  });
});

describe('model-comparison adapter', () => {
  const excluded: BenchmarkModelComparisonEntryDto = {
    key: 'run:9',
    sourceKind: 'Run',
    sourceId: 9,
    sourceName: null,
    runIds: [9],
    runCount: 1,
    suiteId: 5,
    suiteName: 'Suite',
    provider: 'Google',
    modelId: 'gemini-2.5-flash-lite',
    modelDisplayName: 'Gemini 2.5 Flash Lite',
    thinkingLevel: null,
    reasoningMode: null,
    reasoningSummary: null,
    serviceTier: null,
    maxOutputTokens: null,
    parallelExecutionMode: 'Enabled',
    label: 'Gemini 2.5 Flash Lite',
    firstRunStartedAtUtc: '2026-09-01T10:00:00Z',
    lastRunStartedAtUtc: '2026-09-01T10:00:00Z',
    state: 'Excluded',
    comparable: false,
    excluded: true,
    speedDegraded: false,
    costDegraded: false,
    excludingKeys: ['ScoringMethodVersion'],
    speedDegradingKeys: [],
    costDegradingKeys: [],
    differences: [],
    explanation: 'Graded under a different scoring method version.',
    quality: null,
    speed: null,
    cost: null,
    table: null
  };

  it('reports an unmeasured axis as absent rather than as zero', () => {
    const [entry] = toChartEntries({
      pricingBasis: 'Current',
      pricingBasisLabel: 'Current catalog',
      computedAtUtc: '2026-09-07T12:00:00Z',
      baselineSuiteId: 5,
      baselineSuiteName: 'Suite',
      baselineEntryKeys: [],
      baselineKeyValues: {},
      modelAxisKeys: [],
      entries: [excluded],
      comparableCount: 0,
      excludedCount: 1,
      thinkingLevelsDiffer: false,
      speedAxisCaveat: null,
      explanation: '',
      excludedMeasures: []
    });

    expect(entry.excluded).toBeTrue();
    expect(entry.excludedReasonKeys).toEqual(['ScoringMethodVersion']);
    // NaN, never 0: a zero cost would plot as a bar on the baseline and read as "free".
    expect(Number.isNaN(entry.intelligenceIndex)).toBeTrue();
    expect(Number.isNaN(entry.ttftP50Ms)).toBeTrue();
    expect(Number.isNaN(entry.candidateCostPerQuestionUsd)).toBeTrue();
    expect(Number.isNaN(entry.totalRunCostUsd)).toBeTrue();
    expect(entry.candidateCostPerQuestionSdUsd).toBeNull();
    expect(entry.speedIndexSd).toBeNull();
  });

  it('reads items per run and the pricing label off the payload header', () => {
    const context = toChartContext(null);
    expect(context.itemsPerRun).toBe(0);
    expect(context.pricingBasisLabel).toBe('Unknown pricing basis');
  });
});
