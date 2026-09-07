import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideCharts } from 'ng2-charts';

import { ModelComparisonComponent } from './model-comparison.component';
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

  /** Renders one payload through the input, which is the only way the host feeds this component. */
  function render(dto: BenchmarkModelComparisonDto | null): void {
    fixture.componentRef.setInput('comparison', dto);
    fixture.detectChanges();
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
    render(buildDto(comparableSet(2)));

    expect(component.shape).toBe('pair');
    expect(component.profileCard).toBeNull();
    expect(component.panelCards.length).toBe(3);
    expect(component.scatterCards.length).toBe(3);
    expect(fixture.debugElement.queryAll(By.css('canvas')).length).toBe(6);
  });

  it('renders all six figures from three entries upward', () => {
    render(buildDto(comparableSet(3)));

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
    expect(fixture.debugElement.queryAll(By.css('.mc-card select, .mc-card input')).length).toBe(0);
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

  it('emits the two controls that change what the host fetches', () => {
    render(buildDto(comparableSet(3)));
    const suites: number[] = [];
    const bases: string[] = [];
    component.suiteIdChange.subscribe(value => suites.push(value ?? -1));
    component.pricingBasisChange.subscribe(value => bases.push(value));

    component.onSuiteChange(7);
    component.onPricingBasisChange('AsRun');

    expect(suites).toEqual([7]);
    expect(bases).toEqual(['AsRun']);
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
