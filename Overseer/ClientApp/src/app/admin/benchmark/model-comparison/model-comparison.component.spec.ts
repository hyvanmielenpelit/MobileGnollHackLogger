import { ChangeDetectorRef, Component, DebugElement, inject } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideCharts } from 'ng2-charts';

import {
  ComparisonFigureCard,
  ComparisonWizardStep,
  ModelComparisonComponent
} from './model-comparison.component';
import { MAX_PLOTTED_ENTRIES, P1_STACK_BREAKPOINT_PX, directLabelPlugin } from './model-comparison-charts';
import type { DirectLabelBlock, DirectLabelPluginOptions } from './model-comparison-charts';
import { formatComputedAt } from './figure-chrome';
import type { FigureChrome, FigureFooter } from './figure-chrome';
import { APP_CHART_REGISTRABLES } from '../../../chart-registrables';
import {
  BenchmarkComparabilityIndexDto,
  BenchmarkComparabilityIndexEntryDto,
  BenchmarkModelComparisonDto,
  BenchmarkModelComparisonEntryDto,
  ComparisonSelectedSource,
  ComparisonSelectionNotice,
  ComparisonSelectionState,
  selectionNotices,
  toChartContext,
  toChartEntries
} from './model-comparison.models';
import { GROUP_SECTION_TITLE, RUN_SECTION_TITLE } from './comparison-source-picker.component';
import { TableExportFormat, xlsxWriterModule } from './table-export';
import { zipWriterModule } from './figure-export';
import { ToastComponent } from '../../../shared/toast/toast.component';

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
        caveat: 'Timings were recorded under parallel question execution.',
        modelTimeMeanMs: 28000,
        totalModelTimePerRunMeanMs: 504000,
        totalModelTimeSdMs: 31500
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
      baselineSignature: '9c79137965e4d1f0aa3b',
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
          summary: 'Fast models tie at the top, so the chart would hide real latency gaps.',
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
   * almost every assertion below is about the three steps behind it. Step 2 is the default: it
   * carries the filters and the caveats. The comparison table is step 3 and the figures are step
   * 4. `goToStep` refuses an unreachable step, so a test that asks for step 4 over an unchartable
   * set finds an empty panel rather than a quietly passing assertion.
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
      providers: [provideCharts({ registerables: APP_CHART_REGISTRABLES })]
    }).compileComponents();

    fixture = TestBed.createComponent(ModelComparisonComponent);
    component = fixture.componentInstance;
  });

  // -------------------------------------------------------------------------------------------
  // The table view
  // -------------------------------------------------------------------------------------------

  it('renders the table view on its own step, not behind a toggle', () => {
    render(buildDto(comparableSet(4)), 3);

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

  it('carries the three timings in one labelled column, after Speed Index', () => {
    render(buildDto(comparableSet(1)), 3);

    // Model, R, State, Intelligence Index, Speed Index, Timings, Candidate $ / question, Notes.
    const headers = fixture.debugElement.queryAll(By.css('table.mc-table thead tr:first-child th'))
      .map(header => (header.nativeElement as HTMLElement).textContent?.trim() ?? '');
    expect(headers.length).toBe(8);
    expect(headers[3]).toContain('Intelligence Index');
    expect(headers[4]).toContain('Speed Index');
    expect(headers[5]).toContain('Timings');
    // No TTFT column of its own: the exported table still carries both percentiles.
    expect(headers.some(header => header.includes('TTFT'))).toBeFalse();

    // The body row's Model cell is a <th scope="row">, so the <td> list starts at R.
    const row = fixture.debugElement.query(By.css('table.mc-table tbody tr'));
    const cellsText = row.queryAll(By.css('td')).map(cell => (cell.nativeElement as HTMLElement).textContent ?? '');
    const timings = cellsText[4];
    expect(timings).toContain('28.0 s');
    expect(timings).toContain('504.0 s');
    // The SD is appended when the DTO carries one.
    expect(timings).toContain('31.5 s');
    expect(timings).toContain('2000 ms');
    // Each value is named, so three numbers in one cell are not three unlabelled numbers.
    expect(timings).toContain('Model time / question');
    expect(timings).toContain('Suite total');
    expect(timings).toContain('TTFT P50 / P90');
  });

  it('names the model with its provider and thinking badges, over the source it came from', () => {
    const entries = comparableSet(2);
    // A set that mixes thinking levels: the badge is only drawn for the entry that carries one.
    entries[1] = { ...entries[1], thinkingLevel: null };
    render(buildDto(entries), 3);

    const rows = fixture.debugElement.queryAll(By.css('table.mc-table tbody tr'));
    const first = rows[0].nativeElement as HTMLElement;

    // Plain text, not a control: emphasis is chosen on step 4, where its effect is visible.
    expect(first.querySelector('.mc-model-name')?.textContent?.trim()).toBe('Gemini 2.5 Flash');
    expect(first.querySelector('.provider-badge')?.textContent?.trim()).toBe('Google');
    expect(first.querySelector('.thinking-badge')?.textContent?.trim()).toBe('medium');
    expect(first.querySelector('.mc-source')?.textContent?.trim()).toBe('Run 1');

    expect((rows[1].nativeElement as HTMLElement).querySelector('.thinking-badge')).toBeNull();
    expect((rows[1].nativeElement as HTMLElement).querySelector('.mc-source')?.textContent?.trim())
      .toBe('Run 2');
  });

  it('keeps an excluded entry in the table even though no figure can draw it', () => {
    render(buildDto([...comparableSet(3), buildExcludedEntry('run:9', ['ScoringMethodVersion'])]), 3);

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

    // The refusal is explained on step 2 and the entries stay listed on step 3, which opens over
    // a set no figure can draw precisely so that they do.
    component.goToStep(3);
    fixture.detectChanges();
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
    render(buildDto(comparableSet(2)), 4);

    expect(component.shape).toBe('pair');
    expect(component.profileCard).toBeNull();
    expect(component.panelCards.length).toBe(3);
    expect(component.scatterCards.length).toBe(3);
    expect(fixture.debugElement.queryAll(By.css('canvas')).length).toBe(6);
  });

  /** The three scatters' legend `display`, which the names toggle is what changes. */
  function scatterLegendDisplays(): unknown[] {
    return component.scatterCards.map(card => (card.options?.plugins?.legend as { display?: unknown })?.display);
  }

  function scatterPluginIds(): string[][] {
    return component.scatterCards.map(card => card.plugins.map(plugin => plugin.id));
  }

  /** The blocks one scatter hands the plugin, or undefined where it does not register it. */
  function scatterBlocks(index = 0): DirectLabelBlock[] | undefined {
    const plugins = component.scatterCards[index].options?.plugins as
      Record<string, DirectLabelPluginOptions> | undefined;
    return plugins?.[directLabelPlugin.id]?.blocks as DirectLabelBlock[] | undefined;
  }

  /** The nth checkbox above the three scatters: 0 names the marks, 1 draws their values. */
  function scatterToggle(index: number): DebugElement {
    return fixture.debugElement.queryAll(By.css('.mc-scatter-options input[type="checkbox"]'))[index];
  }

  function tick(toggle: DebugElement, on: boolean): void {
    (toggle.nativeElement as HTMLInputElement).checked = on;
    toggle.triggerEventHandler('change', { target: toggle.nativeElement });
    fixture.detectChanges();
  }

  it('swaps the scatter legends for direct labels when the toggle is ticked, and back', () => {
    render(buildDto(comparableSet(3)), 4);

    // The values toggle is on by default, so the plugin is already registered; what the names
    // toggle changes is the legend and whether a block carries a name.
    expect(scatterLegendDisplays()).toEqual([true, true, true]);
    expect(scatterPluginIds().every(ids => ids.includes(directLabelPlugin.id))).toBeTrue();
    expect(scatterBlocks()!.every(b => b.name === undefined)).toBeTrue();

    const toggle = scatterToggle(0);
    expect(toggle).withContext('the toggle sits above the three scatters').toBeTruthy();
    tick(toggle, true);

    expect(component.scatterDirectLabels).toBeTrue();
    expect(scatterLegendDisplays()).toEqual([false, false, false]);
    expect(scatterPluginIds().every(ids => ids.includes(directLabelPlugin.id))).toBeTrue();
    expect(scatterBlocks()!.every(b => typeof b.name === 'string')).toBeTrue();

    tick(toggle, false);

    expect(scatterLegendDisplays()).toEqual([true, true, true]);
    expect(scatterBlocks()!.every(b => b.name === undefined)).toBeTrue();
  });

  it('draws the marks\' values by default and drops the plugin when they are turned off', () => {
    render(buildDto(comparableSet(3)), 4);

    expect(component.scatterInlineValues).toBeTrue();
    expect(scatterPluginIds().every(ids => ids.includes(directLabelPlugin.id))).toBeTrue();
    const blocks = scatterBlocks()!;
    expect(blocks.length).toBe(3);
    expect(blocks[0].values.length).toBe(2);
    expect(blocks[0].name).toBeUndefined();
    expect(blocks[0].hue).toBeTruthy();

    tick(scatterToggle(1), false);

    expect(component.scatterInlineValues).toBeFalse();
    // Neither toggle on: no plugin at all, and the legend still names the marks.
    expect(scatterPluginIds().every(ids => ids.includes(directLabelPlugin.id))).toBeFalse();
    expect(scatterLegendDisplays()).toEqual([true, true, true]);
  });

  it('renders all six figures from three entries upward', () => {
    render(buildDto(comparableSet(3)), 4);

    expect(component.shape).toBe('full');
    expect(fixture.debugElement.queryAll(By.css('canvas')).length).toBe(7);
    // The normalization caveat's exact wording belongs to the chart builder; this only checks the
    // profile figure carries some explanatory note rather than none.
    expect(component.profileCard?.chrome.notes.length).toBeGreaterThan(0);
  });

  it('renders a pricing badge on the cost scatter and withholds it from the quality-speed one', () => {
    render(buildDto(comparableSet(3)), 4);

    const scatterFigures = fixture.debugElement.queryAll(By.css('.mc-grid .mc-card'));
    expect(scatterFigures.length).toBe(3);
    // S1 is quality vs. speed, which carries no cost axis; S2 is quality vs. cost.
    expect(scatterFigures[0].queryAll(By.css('.mc-badge--pricing')).length).toBe(0);
    expect(scatterFigures[1].queryAll(By.css('.mc-badge--pricing')).length).toBeGreaterThan(0);
  });

  // -------------------------------------------------------------------------------------------
  // The filter row
  // -------------------------------------------------------------------------------------------

  it('holds exactly one filter row, and no filter inside any chart card', () => {
    render(buildDto(comparableSet(4)));
    expect(fixture.debugElement.queryAll(By.css('.mc-filters')).length).toBe(1);

    // The figures are two steps further on, and none of them may carry a control of its own: six
    // figures scoped by six controls would each describe a different slice of one set.
    component.goToStep(4);
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
    component.goToStep(3);
    fixture.detectChanges();
    expect(fixture.debugElement.queryAll(By.css('tbody tr')).length).toBe(4);
  });

  it('raises the saturation notice on the speed panel when the Speed Index measure is chosen', () => {
    const entries = comparableSet(3);
    entries[0] = { ...entries[0], table: { ...entries[0].table!, speedIndexSaturated: true } };
    render(buildDto(entries));
    // TTFT carries no notice in this scenario; the default (mean model time) always carries its
    // own "no per-answer dispersion" notice, which would otherwise pollute this assertion.
    component.onSpeedMeasureChange('ttftP50');
    fixture.detectChanges();
    expect(component.figures?.smallMultiples.speed.chrome.notes.length).toBe(0);

    component.onSpeedMeasureChange('speedIndex');
    fixture.detectChanges();

    expect(component.figures?.smallMultiples.speed.chrome.notes.map(note => note.text).join(' '))
      .toContain('saturated');
  });

  it('carries no interval for mean model time, the default measure, and says so', () => {
    render(buildDto(comparableSet(3)));

    expect(component.speedMeasure).toBe('meanModelTime');
    expect(component.figures?.smallMultiples.speed.chrome.notes.map(note => note.text).join(' '))
      .toContain('has no uncertainty bar');
  });

  it('offers no total-run cost measure, because the endpoint carries candidate spend only', () => {
    render(buildDto(comparableSet(3)));

    const option = fixture.debugElement.query(By.css('#mc-cost-measure option[value="totalRun"]'));
    expect(option).toBeTruthy();
    expect((option.nativeElement as HTMLOptionElement).disabled).toBeTrue();
  });

  it('splits the six filters into two named groups inside the one filter row', () => {
    render(buildDto(comparableSet(3)));

    expect(fixture.debugElement.queryAll(By.css('.mc-filters')).length).toBe(1);
    const legends = fixture.debugElement.queryAll(By.css('.mc-filters .gh-fieldset > legend'))
      .map(legend => (legend.nativeElement as HTMLElement).textContent?.trim());
    expect(legends).toEqual(['Scope and order', 'Measures']);

    // Every control keeps its id and its own <label>, so the split is layout and naming only.
    expect(fixture.debugElement.queryAll(By.css('.mc-fieldset-scope .mc-field')).length).toBe(4);
    expect(fixture.debugElement.queryAll(By.css('.mc-fieldset-measures .mc-field')).length).toBe(2);
    expect(fixture.debugElement.query(By.css('.mc-fieldset-measures #mc-speed-measure'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.mc-fieldset-scope #mc-pricing-basis'))).toBeTruthy();
  });

  // -------------------------------------------------------------------------------------------
  // The entry picker
  // -------------------------------------------------------------------------------------------

  it('renders each entry as a checkbox, and an excluded one as a disabled box that keeps its reason', () => {
    render(buildDto([...comparableSet(2), buildExcludedEntry('run:9', ['ScoringMethodVersion'])]));

    expect(fixture.debugElement.queryAll(By.css('.mc-entry-list .mc-entry')).length).toBe(3);
    // Toggle buttons dressed as tags are gone: this is a multi-select over a fixed set.
    expect(fixture.debugElement.queryAll(By.css('.mc-entry-list button')).length).toBe(0);

    const boxes = fixture.debugElement.queryAll(By.css('.mc-entry-list input[type="checkbox"]'))
      .map(box => box.nativeElement as HTMLInputElement);
    expect(boxes.length).toBe(3);
    expect(boxes.slice(0, 2).every(box => box.checked && !box.disabled)).toBeTrue();
    expect(boxes[2].disabled).toBeTrue();
    expect(boxes[2].checked).toBeFalse();

    // The accessible name contains the visible label, so the two never contradict each other.
    expect(boxes[0].getAttribute('aria-label')).toBe('Plot Model 1 in the figures');
    expect(textOf('.mc-entry-list')).toContain('R = 3');
    expect(textOf('.mc-entry-excluded')).toContain('excluded');
    expect(fixture.debugElement.query(By.css('.mc-entry-excluded [popover="hint"]'))).toBeTruthy();

    boxes[1].checked = false;
    fixture.debugElement.queryAll(By.css('.mc-entry-list input[type="checkbox"]'))[1]
      .triggerEventHandler('change', { target: boxes[1] });
    fixture.detectChanges();

    expect(component.includedKeys).toEqual(['run:1']);
    expect(component.figures?.selection.plotted.length).toBe(1);
  });

  it('lets more entries be ticked than the figures plot, and restores one that was unticked', () => {
    render(buildDto(comparableSet(MAX_PLOTTED_ENTRIES + 1)));

    const boxes = () => fixture.debugElement.queryAll(By.css('.mc-entry-list input[type="checkbox"]'));
    expect(boxes().length).toBe(MAX_PLOTTED_ENTRIES + 1);
    // A fresh payload ticks every selectable entry, which is already past the plot cap.
    expect(component.includedKeys.length).toBe(MAX_PLOTTED_ENTRIES + 1);

    const first = boxes()[0];
    (first.nativeElement as HTMLInputElement).checked = false;
    first.triggerEventHandler('change', { target: first.nativeElement });
    fixture.detectChanges();
    expect(component.includedKeys.length).toBe(MAX_PLOTTED_ENTRIES);
    expect(component.isIncluded('run:1')).toBeFalse();

    // The seeded state has to stay reachable, so re-ticking at the cap is honoured.
    const again = boxes()[0];
    (again.nativeElement as HTMLInputElement).checked = true;
    again.triggerEventHandler('change', { target: again.nativeElement });
    fixture.detectChanges();
    expect(component.includedKeys.length).toBe(MAX_PLOTTED_ENTRIES + 1);
    expect(component.isIncluded('run:1')).toBeTrue();
  });

  // -------------------------------------------------------------------------------------------
  // Uncertainty
  // -------------------------------------------------------------------------------------------

  it('marks a set in which every plotted entry rests on a single run', () => {
    const entries = comparableSet(3).map(entry => ({ ...entry, runCount: 1 }));
    render(buildDto(entries));

    expect(component.allSingleRun).toBeTrue();
    expect(textOf('.alert-heading')).toContain('n = 1');

    component.goToStep(3);
    fixture.detectChanges();
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

  it('leads each refused measure with its plain-language summary, the reason behind a disclosure', () => {
    render(buildDto(comparableSet(3)));

    // The summary is the visible line; the specialist reason is one click away rather than absent.
    expect(textOf('.mc-measure-summary'))
      .toContain('Fast models tie at the top, so the chart would hide real latency gaps.');
    expect(textOf('.mc-measures-lead')).toContain('The catch, in one line');

    const detail = fixture.debugElement.query(By.css('.mc-measure-detail'))
      .nativeElement as HTMLDetailsElement;
    expect(detail.open).toBeFalse();
    expect(detail.querySelector('summary')?.textContent?.trim()).toBe('Why, in full');
    expect(detail.textContent).toContain('It saturates');
    expect(textOf('.mc-measure-instead')).toContain('Time to first token, P50.');
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

  it('offers a download and a copy control on every figure card, and one download for the set', () => {
    render(buildDto(comparableSet(3)), 4);

    const cards = fixture.debugElement.queryAll(By.css('.mc-card')).length;
    const downloads = fixture.debugElement.queryAll(By.css('.mc-card .mc-download'));
    const copies = fixture.debugElement.queryAll(By.css('.mc-card .mc-copy'));
    expect(cards).toBe(7);
    expect(downloads.length).toBe(7);
    expect(copies.length).toBe(7);
    // An icon-only button has no text, so aria-label is its accessible name — and it has to name
    // the card, or seven buttons share one name in a screen reader's control list.
    expect((downloads[0].nativeElement as HTMLElement).getAttribute('aria-label'))
      .toContain('Download ');
    const copyNames = copies.map(copy =>
      (copy.nativeElement as HTMLElement).getAttribute('aria-label') ?? '');
    expect(copyNames.every(name => name.startsWith('Copy ') && name.endsWith('to the clipboard')))
      .toBeTrue();
    expect(new Set(copyNames).size).toBe(7);

    expect(fixture.debugElement.query(By.css('#mc-export-format'))).toBeTruthy();
    expect(textOf('.mc-export')).toContain('Download all figures');
    // One image on the clipboard at a time, so there is deliberately no batch copy.
    expect(textOf('.mc-export')).not.toContain('Copy all figures');
  });

  it('offers a WebP quality for the figures only while WebP is the chosen format', async () => {
    render(buildDto(comparableSet(3)), 4);

    const format = fixture.debugElement.query(By.css('#mc-export-format'))
      .nativeElement as HTMLSelectElement;
    // The quality is a control of its own now, so the format option no longer names one.
    expect(Array.from(format.options).find(option => option.value === 'webp')?.textContent?.trim())
      .toBe('WebP');
    expect(fixture.debugElement.query(By.css('#mc-export-webp-quality'))).toBeNull();

    component.onExportFormatChange('webp');
    refresh();
    // `ngModel` writes a freshly created select's initial value in a microtask, not in the pass
    // that renders it.
    await fixture.whenStable();

    const quality = fixture.debugElement.query(By.css('#mc-export-webp-quality'))
      .nativeElement as HTMLSelectElement;
    const labels = Array.from(quality.options).map(option => option.textContent?.trim());
    expect(labels).toEqual(['75', '80', '85', '90', '95', '100']);
    // 85 is the project-wide WebP quality, so the control opens on it rather than on lossless.
    expect(quality.selectedIndex).toBe(labels.indexOf('85'));
    expect(component.figureWebpQuality).toBe(85);
    // A placeholder is not a label, and this control carries no visible one.
    expect(fixture.debugElement.query(By.css('label[for="mc-export-webp-quality"]'))).toBeTruthy();
  });

  it('hides the export controls where no figure is rendered', () => {
    // Not merely hidden: the whole Figures step refuses to open, because there is nothing on it.
    render(buildDto(comparableSet(1)), 4);
    expect(component.shape).toBe('single');
    expect(component.isStepReachable(4)).toBeFalse();
    expect(fixture.debugElement.query(By.css('.mc-export'))).toBeNull();
    expect(fixture.debugElement.queryAll(By.css('.mc-download')).length).toBe(0);

    render(buildDto([
      buildExcludedEntry('run:8', ['ScoringMethodVersion']),
      buildExcludedEntry('run:9', ['CandidatePromptOptions'])
    ]), 4);
    expect(component.shape).toBe('none');
    expect(component.isStepReachable(4)).toBeFalse();
    expect(fixture.debugElement.query(By.css('.mc-card'))).toBeNull();

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
  // Emphasis
  // -------------------------------------------------------------------------------------------

  /** The Clear emphasis control, found by its label rather than by its position in the fieldset. */
  function clearEmphasisButton(): HTMLButtonElement {
    return fixture.debugElement.queryAll(By.css('.mc-emphasis-actions button'))
      .map(button => button.nativeElement as HTMLButtonElement)
      .find(button => (button.textContent ?? '').trim() === 'Clear emphasis')!;
  }

  it('chooses emphasis on the figures step, one checkbox per plotted entry', () => {
    render(buildDto([...comparableSet(3), buildExcludedEntry('run:9', ['ScoringMethodVersion'])]), 4);

    const fieldset = fixture.debugElement.query(By.css('.mc-emphasis')).nativeElement as HTMLElement;
    expect(fieldset.querySelector('legend')?.textContent?.trim()).toBe('Emphasise models');
    // The hint names the group once, rather than being repeated onto every box.
    expect(fieldset.getAttribute('aria-describedby')).toBe('mc-emphasis-hint');
    expect(fixture.debugElement.query(By.css('#mc-emphasis-hint'))).toBeTruthy();

    const boxes = fixture.debugElement.queryAll(By.css('.mc-emphasis-list input[type="checkbox"]'))
      .map(box => box.nativeElement as HTMLInputElement);
    // Only what the figures actually draw, so an excluded entry is never offered.
    expect(boxes.length).toBe(3);
    expect(textOf('.mc-emphasis-list')).not.toContain('Model run:9');
    // The state is in words as well as in the box, and never in the gold alone.
    expect(textOf('.mc-emphasis-status')).toContain('No emphasis');
    expect(clearEmphasisButton().disabled).toBeTrue();

    fixture.debugElement.queryAll(By.css('.mc-emphasis-list input[type="checkbox"]'))[0]
      .triggerEventHandler('change', { target: boxes[0] });
    fixture.detectChanges();

    expect(component.emphasisKeys.length).toBe(1);
    expect(textOf('.mc-emphasis-status')).toContain('1 of 3 emphasised');
    expect(clearEmphasisButton().disabled).toBeFalse();

    clearEmphasisButton().click();
    fixture.detectChanges();

    expect(component.emphasisKeys).toEqual([]);
    expect(textOf('.mc-emphasis-status')).toContain('No emphasis');
    expect(clearEmphasisButton().disabled).toBeTrue();
  });

  it('carries no emphasis control in the table cell, where its effect cannot be seen', () => {
    render(buildDto(comparableSet(2)), 3);

    const cell = fixture.debugElement.query(By.css('table.mc-table tbody tr .col-name'))
      .nativeElement as HTMLElement;
    expect(cell.querySelector('button')).toBeNull();
    expect(cell.querySelector('.mc-model-name')?.textContent?.trim()).toBe('Gemini 2.5 Flash');
    expect(fixture.debugElement.query(By.css('.mc-emphasis'))).toBeNull();
  });

  it('describes each scatter checkbox with the hint that sits under it', () => {
    render(buildDto(comparableSet(3)), 4);

    const names = scatterToggle(0).nativeElement as HTMLInputElement;
    expect(names.getAttribute('aria-describedby')).toBe('mc-direct-labels-hint');
    expect((fixture.debugElement.query(By.css('#mc-direct-labels-hint'))
      .nativeElement as HTMLElement).textContent).toContain('leader line');

    const values = scatterToggle(1).nativeElement as HTMLInputElement;
    expect(values.getAttribute('aria-describedby')).toBe('mc-inline-values-hint');
    expect((fixture.debugElement.query(By.css('#mc-inline-values-hint'))
      .nativeElement as HTMLElement).textContent).toContain('hover overlay');
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

    fixture.componentRef.setInput('selectedRunCount', component.maxSources + 1);
    fixture.detectChanges();
    expect(component.canGoNext).toBeFalse();
    expect(textOf('.mc-wizard-blocked')).toContain(`at most ${component.maxSources}`);
    expect(textOf('.mc-wizard-blocked')).toContain('slow query');

    fixture.componentRef.setInput('selectedRunCount', 2);
    fixture.detectChanges();
    expect(component.canGoNext).toBeTrue();
    expect(component.nextBlockedReason).toBe('');
    expect(fixture.debugElement.query(By.css('.mc-wizard-blocked'))).toBeNull();
  });

  // -------------------------------------------------------------------------------------------
  // The step-1 notice band
  // -------------------------------------------------------------------------------------------

  const crossCondition: ComparisonSelectionNotice = {
    id: 'cross-condition',
    severity: 'warning',
    heading: 'Part of this selection will be excluded',
    body: '2 of 3 selected sources fall outside Condition A and will be excluded.'
  };

  const indexFailure: ComparisonSelectionNotice = {
    id: 'index-error',
    severity: 'error',
    heading: 'Conditions could not be computed',
    body: 'The index could not be built.'
  };

  const stillComputing: ComparisonSelectionNotice = {
    id: 'index-loading',
    severity: 'info',
    heading: 'Conditions are still being computed',
    body: 'The Condition column stays muted until the index lands.'
  };

  /** `n` run sources, for tests that only care about the chip count and the band label. */
  function runSources(count: number): ComparisonSelectedSource[] {
    return Array.from({ length: count }, (_unused, index) => ({
      kind: 'run',
      id: index + 1,
      label: `Model ${index + 1}`,
      provider: 'Google',
      detail: `#${index + 1}`
    }));
  }

  /** `n` group sources, for the same reason. */
  function groupSources(count: number): ComparisonSelectedSource[] {
    return Array.from({ length: count }, (_unused, index) => ({
      kind: 'group',
      id: index + 1,
      label: `Group ${index + 1}`,
      provider: null,
      detail: '3 runs'
    }));
  }

  /** Step 1, with counts, an optional chip list and a notice set in force. */
  function band(counts: {
    runs?: number;
    groups?: number;
    sources?: readonly ComparisonSelectedSource[];
    notices?: readonly ComparisonSelectionNotice[];
  } = {}): void {
    render(null, 1);
    fixture.componentRef.setInput('selectedRunCount', counts.runs ?? 0);
    fixture.componentRef.setInput('selectedGroupCount', counts.groups ?? 0);
    if (counts.sources) {
      fixture.componentRef.setInput('selectedSources', counts.sources);
    }
    fixture.componentRef.setInput('selectionNotices', counts.notices ?? [crossCondition]);
    fixture.detectChanges();
  }

  function bandAlerts(): HTMLElement[] {
    return fixture.debugElement.queryAll(By.css('.mc-wizard-notice .alert'))
      .map(element => element.nativeElement as HTMLElement);
  }

  it('sums the two selection counts', () => {
    band({ runs: 3, groups: 4 });

    expect(component.selectedSourceCount).toBe(7);
  });

  it('names the runs table in the band label when only runs are selected', () => {
    band({ runs: 3, sources: runSources(3) });

    const label = textOf('.mc-wizard-notice-label');
    expect(label).toContain(RUN_SECTION_TITLE);
    expect(label).toContain('3 sources selected');
    expect(label).not.toContain(GROUP_SECTION_TITLE);
  });

  it('names the groups table when only groups are selected', () => {
    band({ groups: 2, sources: groupSources(2) });

    const label = textOf('.mc-wizard-notice-label');
    expect(label).toContain(GROUP_SECTION_TITLE);
    expect(label).toContain('2 sources selected');
    expect(label).not.toContain(RUN_SECTION_TITLE);
  });

  it('names both tables when the selection spans them', () => {
    band({ runs: 2, groups: 1, sources: [...runSources(2), ...groupSources(1)] });

    const label = textOf('.mc-wizard-notice-label');
    expect(label).toContain(`${RUN_SECTION_TITLE} and ${GROUP_SECTION_TITLE}`);
    expect(label).toContain('3 sources selected');
  });

  it('labels the band for assistive technology', () => {
    band({ runs: 2, sources: runSources(2) });

    const strip = fixture.debugElement.query(By.css('.mc-wizard-notice')).nativeElement as HTMLElement;
    expect(strip.getAttribute('role')).toBe('region');
    const labelId = strip.getAttribute('aria-labelledby')!;
    expect((fixture.debugElement.query(By.css('.mc-wizard-notice-label'))
      .nativeElement as HTMLElement).id).toBe(labelId);
  });

  it('renders one chip per selected source, the provider badge on a run chip and not on a group chip', () => {
    const sources: ComparisonSelectedSource[] = [
      { kind: 'run', id: 1, label: 'Gemini 2.5 Flash', provider: 'Google', detail: '#1' },
      { kind: 'group', id: 3, label: 'Nightly regression', provider: null, detail: '4 runs' }
    ];
    band({ runs: 1, groups: 1, sources, notices: [] });

    const chips = fixture.debugElement.queryAll(By.css('.mc-selection-chip'));
    expect(chips.length).toBe(2);
    expect(chips[0].nativeElement.textContent).toContain('#1');
    expect(chips[0].nativeElement.textContent).toContain('Gemini 2.5 Flash');
    expect(chips[0].query(By.css('app-provider-badge'))).toBeTruthy();
    expect(chips[0].nativeElement.classList).not.toContain('mc-selection-chip--group');

    expect(chips[1].nativeElement.textContent).toContain('4 runs');
    expect(chips[1].nativeElement.textContent).toContain('Nightly regression');
    expect(chips[1].query(By.css('app-provider-badge'))).toBeNull();
    expect(chips[1].nativeElement.classList).toContain('mc-selection-chip--group');
  });

  it('emits the chip through removeSource when its remove button is clicked', () => {
    const source: ComparisonSelectedSource =
      { kind: 'run', id: 1, label: 'Gemini 2.5 Flash', provider: 'Google', detail: '#1' };
    band({ runs: 1, sources: [source], notices: [] });

    const removed: ComparisonSelectedSource[] = [];
    component.removeSource.subscribe(s => removed.push(s));

    (fixture.debugElement.query(By.css('.mc-selection-remove')).nativeElement as HTMLButtonElement).click();

    expect(removed).toEqual([source]);
  });

  it('renders on step 1 with nothing selected, and says so in the summary and the hint', () => {
    band({ notices: [] });

    expect(fixture.debugElement.query(By.css('.mc-wizard-notice'))).toBeTruthy();
    expect(textOf('.mc-wizard-notice-label')).toContain('Nothing selected yet');
    expect(textOf('.mc-wizard-selection-hint')).toContain('Tick runs or analysis groups above');
    expect(fixture.debugElement.query(By.css('.mc-selection-chips'))).toBeNull();
  });

  it('keeps the notices below the chip row when the selection carries both', () => {
    const source: ComparisonSelectedSource =
      { kind: 'run', id: 1, label: 'Gemini 2.5 Flash', provider: 'Google', detail: '#1' };
    band({ runs: 1, sources: [source], notices: [crossCondition] });

    const head = fixture.debugElement.query(By.css('.mc-selection-head')).nativeElement as HTMLElement;
    const notices = fixture.debugElement.query(By.css('.mc-wizard-notices')).nativeElement as HTMLElement;
    expect(head.nextElementSibling).toBe(notices);
  });

  it('announces through one polite live region around the notices, apart from the summary status', () => {
    band({ runs: 3, notices: [indexFailure, crossCondition, stillComputing] });

    const strip = fixture.debugElement.query(By.css('.mc-wizard-notice')).nativeElement as HTMLElement;
    expect(strip.hasAttribute('aria-live')).toBeFalse();

    const list = fixture.debugElement.query(By.css('.mc-wizard-notices')).nativeElement as HTMLElement;
    expect(list.getAttribute('aria-live')).toBe('polite');
    // Only the notice that appeared is announced, rather than the whole list again.
    expect(list.getAttribute('aria-atomic')).toBe('false');

    // The summary line carries the band's one role="status"; a role nested inside the aria-live
    // region above would double-announce in several screen readers, so no notice carries one.
    const statuses = fixture.debugElement.queryAll(By.css('.mc-wizard-notice [role="status"]'));
    expect(statuses.length).toBe(1);
    expect((statuses[0].nativeElement as HTMLElement).id).toBe('mc-selection-label');
    expect(bandAlerts().map(alert => alert.getAttribute('role'))).toEqual([null, null, null]);
  });

  it('renders one alert per notice, each with its own variant, glyph and heading', () => {
    band({ runs: 3, notices: [indexFailure, crossCondition, stillComputing] });

    const alerts = bandAlerts();
    expect(alerts.length).toBe(3);
    expect(alerts[0].classList).toContain('alert-danger');
    expect(alerts[1].classList).toContain('alert-warning');
    expect(alerts[2].classList).toContain('alert-info');

    expect(alerts[0].querySelector('.alert-heading')?.textContent)
      .toContain('Conditions could not be computed');
    expect(alerts[1].querySelector('.alert-body')?.textContent).toContain('fall outside Condition A');

    // Severity is carried by shape as well as by hue: three distinct glyphs, none of them decorative
    // to a screen reader.
    const glyphs = alerts.map(alert => alert.querySelector('svg.alert-icon'));
    expect(glyphs.every(glyph => glyph?.getAttribute('aria-hidden') === 'true')).toBeTrue();
    expect(new Set(glyphs.map(glyph => glyph?.innerHTML)).size).toBe(3);
  });

  it('stacks errors above warnings above information, whatever order they arrive in', () => {
    band({ runs: 3, notices: [stillComputing, crossCondition, indexFailure] });

    expect(component.bandNotices.map(notice => notice.id))
      .toEqual(['index-error', 'cross-condition', 'index-loading']);
    expect(bandAlerts().map(alert => alert.className.includes('alert-danger')))
      .toEqual([true, false, false]);
  });

  it('bands the notices above the footer on step 1, under one label', () => {
    band({ runs: 3, notices: [indexFailure, crossCondition] });

    expect(fixture.debugElement.queryAll(By.css('.mc-wizard-notice-label')).length).toBe(1);
    const strip = fixture.debugElement.query(By.css('.mc-wizard-notice')).nativeElement as HTMLElement;
    expect(strip.nextElementSibling?.classList).toContain('mc-wizard-nav');
  });

  it('keeps the notice band and the navigation as separate regions', () => {
    band({ runs: 2 });

    const nav = fixture.debugElement.query(By.css('.mc-wizard-nav')).nativeElement as HTMLElement;
    expect(getComputedStyle(nav).borderTopWidth).not.toBe('0px');
  });

  it('derives the plot-cap notice from its own constant, as information', () => {
    band({ runs: component.maxPlottedEntries + 1, notices: [] });

    expect(component.plotCapNotice?.id).toBe('plot-cap');
    const alerts = bandAlerts();
    expect(alerts.length).toBe(1);
    // Blue, not amber: the plot cap changes how much is drawn, not what the figures mean.
    expect(alerts[0].classList).toContain('alert-info');
    expect(alerts[0].textContent).toContain(`plot at most ${component.maxPlottedEntries}`);
  });

  it('drops the plot-cap notice above the request cap, leaving the band with no notice list', () => {
    band({ runs: component.maxSources + 1, notices: [] });

    expect(component.plotCapNotice).toBeNull();
    expect(fixture.debugElement.query(By.css('.mc-wizard-notice'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.mc-wizard-notices'))).toBeNull();
  });

  it('sorts the wizard-derived plot-cap notice in with the host-derived ones', () => {
    band({ runs: component.maxPlottedEntries + 1 });

    expect(component.bandNotices.map(notice => notice.id)).toEqual(['cross-condition', 'plot-cap']);
  });

  it('drops the band from step 2 on', () => {
    band({ runs: component.maxPlottedEntries + 1 });
    render(buildDto(comparableSet(3)), 2);
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.mc-wizard-notice'))).toBeNull();
  });

  it('renders the band with no notice list when the selection is within both caps and has nothing to report', () => {
    band({ runs: 2, sources: runSources(2), notices: [] });

    expect(fixture.debugElement.query(By.css('.mc-wizard-notice'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.mc-wizard-notices'))).toBeNull();
  });

  it('says in the band that nothing is selected yet, and stops as soon as something is', () => {
    band({ runs: 0, notices: [] });

    expect(component.bandNotices.map(notice => notice.id)).toEqual(['nothing-selected']);
    const alerts = bandAlerts();
    expect(alerts.length).toBe(1);
    // A warning: Compare is blocked, but nothing is wrong with the view or the index.
    expect(alerts[0].classList).toContain('alert-warning');
    expect(alerts[0].textContent).toContain('Nothing is selected yet');
    expect(alerts[0].textContent).toContain('Tick at least one completed run or analysis group');
    // The band explains; the footer names the blocked control, and neither repeats the other.
    expect(textOf('.mc-wizard-blocked')).toContain('at least one run or analysis group');

    fixture.componentRef.setInput('selectedRunCount', 1);
    fixture.detectChanges();

    expect(component.nothingSelectedNotice).toBeNull();
    // The band itself stays on screen; only its notice list, now with nothing to report, is gone.
    expect(fixture.debugElement.query(By.css('.mc-wizard-notice'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.mc-wizard-notices'))).toBeNull();
  });

  it('drops the nothing-selected notice while a comparison is being computed', () => {
    band({ runs: 0, notices: [] });
    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();

    expect(component.nothingSelectedNotice).toBeNull();
    expect(fixture.debugElement.query(By.css('.mc-wizard-notice'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('.mc-wizard-notices'))).toBeNull();
  });

  it('shows a spinner and Comparing on the footer while step 1 waits for its comparison', () => {
    render(null, 1);
    fixture.componentRef.setInput('selectedRunCount', 2);
    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();

    expect(component.comparing).toBeTrue();
    expect(component.nextLabel).toBe('Comparing…');

    const next = fixture.debugElement.queryAll(By.css('.mc-wizard-nav .btn-gh'))[1]
      .nativeElement as HTMLButtonElement;
    expect(next.textContent).toContain('Comparing…');
    expect(next.querySelector('.gh-spinner-small')).toBeTruthy();
    expect(next.getAttribute('aria-busy')).toBe('true');
    // aria-disabled, never disabled: the reason has to stay reachable by keyboard.
    expect(next.getAttribute('aria-disabled')).toBe('true');
    expect(next.hasAttribute('disabled')).toBeFalse();

    // The status row above the tabs carries the same fact, with its own spinner.
    const status = fixture.debugElement.query(By.css('.mc-wizard-loading'))
      .nativeElement as HTMLElement;
    expect(status.getAttribute('role')).toBe('status');
    expect(status.querySelector('.gh-spinner-small')).toBeTruthy();
    expect(status.textContent).toContain('pricing every entry server-side');

    // The step-1 panel says its result is pending; the picker's own controls stay live.
    const panel = fixture.debugElement.query(By.css('#mc-step-panel-1')).nativeElement as HTMLElement;
    expect(panel.getAttribute('aria-busy')).toBe('true');

    fixture.componentRef.setInput('loading', false);
    fixture.detectChanges();
    expect(component.comparing).toBeFalse();
    expect(component.nextLabel).toBe('Compare');
    expect(next.hasAttribute('aria-busy')).toBeFalse();
  });

  it('labels the footer Next rather than Comparing while a later step refetches', () => {
    render(buildDto(comparableSet(3)), 2);
    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();

    // A pricing-basis refetch loads too, and Next on step 2 is not blocked by it.
    expect(component.comparing).toBeFalse();
    expect(component.nextLabel).toBe('Next');
  });

  it('emits compare from the footer rather than advancing, while no comparison exists', () => {
    render(null);
    fixture.componentRef.setInput('selectedRunCount', 2);
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

    component.goToStep(4);
    // A pricing-basis refetch replaces one payload with another; it must not move the reader.
    fixture.componentRef.setInput('comparison', buildDto(comparableSet(3)));
    fixture.detectChanges();
    expect(component.step).toBe(4);

    fixture.componentRef.setInput('comparison', null);
    fixture.detectChanges();
    expect(component.step).toBe(1);
  });

  it('opens the table step over a set no figure can draw, and refuses the figures step', () => {
    render(buildDto([
      buildExcludedEntry('run:8', ['ScoringMethodVersion']),
      buildExcludedEntry('run:9', ['CandidatePromptOptions'])
    ]), 3);

    // The table is the artefact that says what could not be compared, so its step opens here.
    expect(component.step).toBe(3);
    expect(component.isStepReachable(3)).toBeTrue();
    expect(component.canGoNext).toBeFalse();
    expect(component.isStepReachable(4)).toBeFalse();

    const next = fixture.debugElement.queryAll(By.css('.mc-wizard-nav .btn-gh'))[1]
      .nativeElement as HTMLElement;
    // aria-disabled, not disabled: a disabled button cannot be focused, and the reader would be
    // left guessing why Next does nothing.
    expect(next.getAttribute('aria-disabled')).toBe('true');
    expect(next.hasAttribute('disabled')).toBeFalse();
    expect(textOf('.mc-wizard-blocked')).toContain('Nothing in this set may be charted together');

    const figuresTab = fixture.debugElement
      .queryAll(By.css('.mc-wizard-steps .gh-tab'))[3].nativeElement as HTMLElement;
    expect(figuresTab.getAttribute('aria-disabled')).toBe('true');
    expect(figuresTab.hasAttribute('disabled')).toBeFalse();
  });

  it('reaches the table step whenever a comparison exists, and neither step without one', () => {
    render(null);

    expect(component.isStepReachable(3)).toBeFalse();
    expect(component.isStepReachable(4)).toBeFalse();

    render(buildDto(comparableSet(3)));
    expect(component.isStepReachable(3)).toBeTrue();
    expect(component.isStepReachable(4)).toBeTrue();
    // Step 2 no longer gates on anything but the payload: the table behind it always has rows.
    expect(component.canGoNext).toBeTrue();
    expect(component.nextBlockedReason).toBe('');
  });

  it('names the single-entry case separately, because it is a different fix', () => {
    render(buildDto(comparableSet(1)), 3);

    expect(component.shape).toBe('single');
    expect(component.step).toBe(3);
    expect(component.nextBlockedReason).toContain('Only one entry is plotted');
  });

  it('labels step 4 Next as Close and emits closeRequested from it', () => {
    render(buildDto(comparableSet(3)), 4);
    expect(component.step).toBe(4);
    expect(component.nextLabel).toBe('Close');

    const closed: number[] = [];
    component.closeRequested.subscribe(() => closed.push(1));
    component.nextStep();

    expect(closed.length).toBe(1);
  });

  it('draws the figures on step 4 and keeps the comparison table on step 3', () => {
    render(buildDto(comparableSet(3)), 4);
    expect(fixture.debugElement.queryAll(By.css('canvas')).length).toBe(7);
    expect(fixture.debugElement.query(By.css('table.mc-table'))).toBeNull();

    component.goToStep(3);
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('table.mc-table'))).toBeTruthy();
    expect(fixture.debugElement.queryAll(By.css('canvas')).length).toBe(0);

    // Step 2 keeps the filters and gives up the table.
    component.goToStep(2);
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('table.mc-table'))).toBeNull();
    expect(fixture.debugElement.queryAll(By.css('.mc-filters')).length).toBe(1);
  });

  it('drives the step tablist with a roving tabindex and the arrow keys', () => {
    render(buildDto(comparableSet(3)));

    const tabs = fixture.debugElement.queryAll(By.css('.mc-wizard-steps .gh-tab'))
      .map(tab => tab.nativeElement as HTMLElement);
    expect(tabs.length).toBe(4);
    expect(tabs[1].getAttribute('aria-selected')).toBe('true');
    expect(tabs[1].getAttribute('tabindex')).toBe('0');
    expect(tabs[0].getAttribute('tabindex')).toBe('-1');
    expect(tabs[2].getAttribute('tabindex')).toBe('-1');
    expect(tabs[3].getAttribute('tabindex')).toBe('-1');

    component.onStepKeydown(new KeyboardEvent('keydown', { key: 'ArrowLeft' }), 1);
    fixture.detectChanges();
    expect(component.step).toBe(1);

    component.onStepKeydown(new KeyboardEvent('keydown', { key: 'End' }), 0);
    fixture.detectChanges();
    expect(component.step).toBe(4);
  });

  it('hides the step 1 panel rather than rendering it beside the open step', () => {
    render(buildDto(comparableSet(4)), 2);

    // `display: flex` on .mc-step is an author declaration and outranks the user-agent [hidden]
    // rule, so the panel needs an explicit [hidden] declaration of its own; without it both
    // panels render side by side in .mc-wizard-body's row.
    const panel = fixture.debugElement.query(By.css('#mc-step-panel-1'))
      .nativeElement as HTMLElement;
    expect(getComputedStyle(panel).display).toBe('none');

    component.goToStep(1);
    fixture.detectChanges();

    expect(getComputedStyle(panel).display).not.toBe('none');
  });

  it('survives being measured at width zero, which is what a closed dialog reports', () => {
    render(buildDto(comparableSet(3)), 4);

    expect(() => component.applyContainerWidth(0)).not.toThrow();
    expect(component.orientation).toBe('vertical');
  });

  it('disables its own close controls while an export is running, and nothing else', () => {
    render(buildDto(comparableSet(3)), 4);
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
  // Export resolution, and the preview dialog that now carries its controls
  // -------------------------------------------------------------------------------------------

  function previewDialog(): HTMLDialogElement {
    return fixture.debugElement.query(By.css('dialog.mc-preview-dialog'))
      .nativeElement as HTMLDialogElement;
  }

  /**
   * Opens the preview with `showModal` stubbed.
   *
   * A fixture's element is never in the document, and `showModal` on a detached dialog throws; the
   * source picker's own dialog specs stand it in the same way. The controls themselves are in the
   * DOM either way — a closed dialog is hidden, not absent — so the open state is about the
   * component's own behaviour rather than about reaching them.
   */
  function openPreview(card?: ComparisonFigureCard): void {
    const dialog = previewDialog();
    if (!jasmine.isSpy(dialog.showModal)) {
      spyOn(dialog, 'showModal');
    }
    component.openFigurePreview(card);
    refresh();
  }

  /** The composition the debounce would run, without waiting 150 ms for the timer to fire it. */
  async function composePreview(): Promise<void> {
    await (component as unknown as { renderPreview(): Promise<void> }).renderPreview();
    refresh();
  }

  // An open preview leaves a debounced composition behind it, and a timer that outlived its test
  // would compose against the next one's fixture.
  afterEach(() => {
    component.onFigurePreviewClosed();
  });

  let restoreDevicePixelRatio: (() => void) | null = null;

  /**
   * Redefines the display's density and rebuilds the component against it.
   *
   * The density control opens on `window.devicePixelRatio`, read once in a field initialiser, so
   * the value has to be in place before the instance exists — and the outer `beforeEach` has
   * already built one against whatever display the test machine has. Every assertion below that
   * names a percentage either comes through here or sets the density explicitly, or it would pass
   * on one machine and fail on another.
   */
  function withDisplayDensity(ratio: number): void {
    const original = Object.getOwnPropertyDescriptor(window, 'devicePixelRatio');
    restoreDevicePixelRatio = () => {
      if (original) {
        Object.defineProperty(window, 'devicePixelRatio', original);
      } else {
        delete (window as unknown as Record<string, unknown>)['devicePixelRatio'];
      }
    };
    Object.defineProperty(window, 'devicePixelRatio', { configurable: true, value: ratio });
    fixture = TestBed.createComponent(ModelComparisonComponent);
    component = fixture.componentInstance;
  }

  afterEach(() => {
    restoreDevicePixelRatio?.();
    restoreDevicePixelRatio = null;
  });

  it('shows the two custom size inputs only for Custom, and names what will be written', () => {
    render(buildDto(comparableSet(3)), 4);
    openPreview();
    // The control opens on the test machine's own display, which the pixel counts below are not
    // about; every one of them is the composition at 100 %.
    component.onExportDensityChange(1);
    expect(fixture.debugElement.query(By.css('#mc-export-width'))).toBeNull();
    expect(textOf('.mc-export-dimensions')).toContain('on-screen size');

    component.onExportResolutionChange('fullhd');
    refresh();
    expect(textOf('.mc-export-dimensions')).toContain('1920 × 1080 px');
    expect(textOf('.mc-export-dimensions')).toContain('2× density');

    // A 21:9 size is laid out wider rather than shorter, at the same type size and density.
    component.onExportResolutionChange('uw1080');
    refresh();
    expect(textOf('.mc-export-dimensions')).toContain('1280 × 540');
    expect(textOf('.mc-export-dimensions')).toContain('2× density');

    component.onExportResolutionChange('custom');
    refresh();
    expect(fixture.debugElement.query(By.css('#mc-export-width'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('#mc-export-height'))).toBeTruthy();
  });

  it('refuses an out-of-range custom size in words, and will not export under one', () => {
    render(buildDto(comparableSet(3)), 4);
    openPreview();
    component.onExportDensityChange(1);
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

  it('opens the density on the reader’s own display, and says which option that is', () => {
    withDisplayDensity(2);
    render(buildDto(comparableSet(3)), 4);
    openPreview();

    expect(component.exportDensitySelection).toBe(2);
    expect(component.exportDensity).toBe(2);
    expect(component.isCustomDensity).toBeFalse();
    expect(textOf('#mc-export-density')).toContain('200% (this display)');
    // Only the reader's own step is marked, or the note would name nothing.
    expect(textOf('#mc-export-density')).not.toContain('100% (this display)');
  });

  it('holds a display density no step matches in the custom field, prefilled', () => {
    // 110 % browser zoom on a 200 % display.
    withDisplayDensity(2.2);
    render(buildDto(comparableSet(3)), 4);
    openPreview();

    expect(component.exportDensitySelection).toBe('custom');
    expect(component.customExportDensityPercent).toBe(220);
    expect(component.exportDensity).toBe(2.2);
    expect(fixture.debugElement.query(By.css('#mc-export-density-custom'))).toBeTruthy();
    expect(component.exportSizeError).toBe('');
  });

  it('offers every Windows display scaling step, and Custom below them', () => {
    withDisplayDensity(1);
    render(buildDto(comparableSet(3)), 4);
    openPreview();

    const options = fixture.debugElement
      .queryAll(By.css('#mc-export-density option'))
      .map(option => ((option.nativeElement as HTMLOptionElement).textContent ?? '').trim());
    expect(options).toEqual([
      '100% (this display)', '125%', '150%', '175%', '200%', '250%', '300%', '350%', '400%',
      'Custom…'
    ]);
  });

  it('multiplies the written size by the density in the read-out and the header summary', () => {
    render(buildDto(comparableSet(3)), 4);
    openPreview();
    component.onExportResolutionChange('fullhd');

    component.onExportDensityChange(2);
    refresh();
    const doubled = textOf('.mc-export-dimensions');
    expect(doubled).toContain('3840 × 2160 px');
    expect(doubled).toContain('1920 × 1080 at 200%');
    expect(doubled).toContain('4× density');
    expect(textOf('.mc-export-summary')).toContain('200%');

    // At 100 % the requested size and the written one are one number, printed once.
    component.onExportDensityChange(1);
    refresh();
    const plain = textOf('.mc-export-dimensions');
    expect(plain).toContain('1920 × 1080 px at 100%');
    expect(plain).toContain('2× density');
    expect(plain).not.toContain('(1920 × 1080 at');
  });

  it('names the density on the on-screen size, and no fixed factor', () => {
    render(buildDto(comparableSet(3)), 4);
    openPreview();
    component.onExportDensityChange(1.5);
    refresh();

    const readout = textOf('.mc-export-dimensions');
    expect(readout).toContain('on-screen size at 150%');
    expect(readout).not.toContain('Twice');
    expect(component.exportResolution.label).toBe('On-screen');
  });

  it('refuses a custom density outside its bounds, and will not export under one', () => {
    render(buildDto(comparableSet(3)), 4);
    openPreview();
    component.onExportDensityChange('custom');
    component.onCustomDensityChange(900);
    refresh();

    expect(component.customDensityError).toContain(`${component.maxExportDensityPercent}`);
    expect(component.exportSizeError).toBe(component.customDensityError);
    expect(component.canExport).toBeFalse();
    expect(textOf('.mc-preview-controls .mc-export-error')).toContain('800');

    component.onCustomDensityChange(150);
    refresh();
    expect(component.customDensityError).toBe('');
    expect(component.exportDensity).toBe(1.5);
    expect(component.canExport).toBeTrue();
  });

  it('refuses a bitmap the browser could not allocate, and disables the footer under it', () => {
    render(buildDto(comparableSet(3)), 4);
    openPreview();
    component.onExportResolutionChange('custom');
    component.onCustomWidthChange(8000);
    component.onCustomHeightChange(8000);
    component.onExportDensityChange(3);
    refresh();

    expect(component.exportSizeError).toContain('24000 × 24000');
    expect(component.exportSizeError).toContain('16384');
    expect(component.canExport).toBeFalse();
    const footer = fixture.debugElement
      .queryAll(By.css('.mc-preview-foot button'))
      .map(button => button.nativeElement as HTMLButtonElement);
    expect(footer.length).toBe(3);
    expect(footer.every(button => button.disabled)).toBeTrue();

    component.onExportDensityChange(2);
    refresh();
    expect(component.exportSizeError).toBe('');
    expect(component.canExport).toBeTrue();
  });

  it('copies the live figure at the chosen density, and restores the chart afterwards', async () => {
    render(buildDto(comparableSet(3)), 4);
    withClipboard({ write: () => Promise.resolve() });
    const card = component.panelCards[0];
    const canvas = (component as unknown as {
      canvasFor(card: ComparisonFigureCard): HTMLCanvasElement | null;
    }).canvasFor(card)!;
    const chart = (component as unknown as {
      chartFor(canvas: HTMLCanvasElement): { options: { devicePixelRatio?: number }; resize(): void } | null;
    }).chartFor(canvas)!;
    expect(chart).withContext('the live chart the copy path re-renders').toBeTruthy();

    const previous = chart.options.devicePixelRatio;
    const seen: (number | undefined)[] = [];
    spyOn(chart, 'resize').and.callFake(() => seen.push(chart.options.devicePixelRatio));

    component.onExportDensityChange(3);
    await component.copyFigure(card);

    // Up for the encode, back down in the `finally`: a copy must not strand the on-screen figure.
    expect(seen[0]).toBe(3);
    expect(chart.options.devicePixelRatio).toBe(previous);
  });

  it('derives the other custom side from the locked ratio', () => {
    render(buildDto(comparableSet(3)), 4);
    openPreview();
    component.onExportResolutionChange('custom');
    component.customExportWidth = 1920;
    component.customExportHeight = 1080;

    component.lockCustomRatio(true);
    component.onCustomWidthChange(1280);
    expect(component.customExportHeight).toBe(720);

    component.onCustomHeightChange(1080);
    expect(component.customExportWidth).toBe(1920);
    expect(component.exportAspectLabel).toBe('16:9');

    // Unlocked, a side is exactly what was typed into it.
    component.lockCustomRatio(false);
    component.onCustomWidthChange(1000);
    expect(component.customExportHeight).toBe(1080);
  });

  it('opens the preview on one card and steps through the set, wrapping at both ends', () => {
    render(buildDto(comparableSet(3)), 4);
    const cards = component.exportableCards;
    expect(cards.length).toBe(7);

    const showModal = spyOn(previewDialog(), 'showModal');
    openPreview(cards[2]);
    expect(component.previewCardId).toBe(cards[2].id);
    expect(component.previewOpen).toBeTrue();
    expect(showModal).toHaveBeenCalled();

    component.previewNext();
    expect(component.previewCardId).toBe(cards[3].id);

    component.selectPreviewCard(cards[0].id);
    component.previewPrevious();
    expect(component.previewCardId).toBe(cards[cards.length - 1].id);

    component.previewNext();
    expect(component.previewCardId).toBe(cards[0].id);

    // The header's own control opens on the first card rather than on nothing.
    component.onFigurePreviewClosed();
    openPreview();
    expect(component.previewCardId).toBe(cards[0].id);
  });

  /**
   * The two trade-off checkboxes inside the dialog's own controls, in template order.
   *
   * Classed rather than matched by type: the custom-size panel above them carries a checkbox of its
   * own, and a bare `input[type="checkbox"]` would pick it up whenever that size is selected.
   */
  function dialogScatterToggles(): DebugElement[] {
    return fixture.debugElement.queryAll(By.css('.mc-preview-controls .mc-preview-scatter-toggle'));
  }

  it('mirrors both trade-off toggles in the dialog and drives the page from them', () => {
    render(buildDto(comparableSet(3)), 4);
    openPreview(component.scatterCards[0]);

    expect(textOf('.mc-preview-group-title')).toContain('Trade-off charts');
    const toggles = dialogScatterToggles();
    expect(toggles.length).toBe(2);
    expect((toggles[0].nativeElement as HTMLInputElement).checked).toBe(component.scatterDirectLabels);
    expect((toggles[1].nativeElement as HTMLInputElement).checked).toBe(component.scatterInlineValues);

    tick(toggles[0], true);
    expect(component.scatterDirectLabels).toBeTrue();
    // The page's own checkbox above the scatters reads the same field, so both stay in step.
    expect((scatterToggle(0).nativeElement as HTMLInputElement).checked).toBeTrue();
    expect(scatterLegendDisplays()).toEqual([false, false, false]);

    tick(dialogScatterToggles()[1], false);
    expect(component.scatterInlineValues).toBeFalse();
    expect((scatterToggle(1).nativeElement as HTMLInputElement).checked).toBeFalse();
    expect(scatterBlocks()!.every(b => b.values.length === 0)).toBeTrue();
  });

  it('re-composes the preview when a trade-off toggle is changed in the dialog', () => {
    render(buildDto(comparableSet(3)), 4);

    // The clock is installed before the dialog is opened, so the composition the open itself
    // schedules is a fake timer this test drains rather than a real one outliving it.
    jasmine.clock().install();
    try {
      openPreview(component.scatterCards[0]);
      const renderPreview = spyOn(
        component as unknown as { renderPreview(): Promise<void> }, 'renderPreview'
      ).and.returnValue(Promise.resolve());
      jasmine.clock().tick(200);
      renderPreview.calls.reset();

      tick(dialogScatterToggles()[1], false);
      expect(renderPreview).not.toHaveBeenCalled();
      jasmine.clock().tick(200);
      expect(renderPreview).toHaveBeenCalled();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('hides the trade-off group on a card the two toggles cannot change', () => {
    render(buildDto(comparableSet(3)), 4);
    openPreview(component.panelCards[0]);

    expect(component.previewIsScatter).toBeFalse();
    expect(fixture.debugElement.query(By.css('.mc-preview-group-title'))).toBeNull();
    expect(dialogScatterToggles().length).toBe(0);
  });

  it('refuses a size the figure does not fit, naming it, and leaves the stage blank', async () => {
    render(buildDto(comparableSet(3)), 4);
    const card = component.panelCards[0];

    // Every offered size is laid out in at least 960 × 540 layout px, so a figure is refused for
    // the caveats it carries rather than for the box it was asked for: this one's do not fit.
    const notice = (index: number): string =>
      `Notice ${index}: ` +
      'the speed axis is degraded for this entry, so its bar is drawn from a partial sample. '
        .repeat(6);
    spyOn(component as unknown as { exportChrome(card: ComparisonFigureCard): unknown }, 'exportChrome')
      .and.returnValue({
        chrome: {
          ...card.chrome,
          notes: [1, 2, 3, 4, 5, 6].map(index => ({ text: notice(index), tone: 'warning' as const }))
        },
        footer: { suite: 'Suite A', computedAt: 'Current catalog, 3 Sep 2026' }
      });
    openPreview(card);

    component.onExportResolutionChange('custom');
    component.onCustomWidthChange(1280);
    component.onCustomHeightChange(720);
    await composePreview();

    expect(component.customResolutionError).toBe('');
    expect(component.previewRefusal).toContain(card.title);
    // The target size, which is what Download would write and what it would refuse.
    expect(component.previewRefusal).toContain('1280 × 720 px');
    expect(textOf('.mc-preview-controls .mc-export-error')).toContain('1280 × 720 px');
    expect(component.previewCanvas!.nativeElement.width).toBe(0);
    expect(component.previewBusy).toBeFalse();
  });

  /**
   * A `ResizeObserver` that records what it watched and whether it was disconnected.
   *
   * The stage observer is what carries a window resize, a split screen and a browser zoom into the
   * preview, and none of the three can be produced inside a fixture.
   */
  class RecordingResizeObserver {
    static readonly created: RecordingResizeObserver[] = [];
    readonly observed: Element[] = [];
    disconnected = 0;

    constructor(_callback: ResizeObserverCallback) {
      RecordingResizeObserver.created.push(this);
    }

    observe(target: Element): void {
      this.observed.push(target);
    }

    unobserve(): void {
      // Never used: the component disconnects rather than unobserving one element at a time.
    }

    disconnect(): void {
      this.disconnected++;
    }
  }

  let realResizeObserver: typeof ResizeObserver | undefined;

  function installFakeResizeObserver(): RecordingResizeObserver[] {
    realResizeObserver = window.ResizeObserver;
    RecordingResizeObserver.created.length = 0;
    (window as unknown as { ResizeObserver: unknown }).ResizeObserver = RecordingResizeObserver;
    return RecordingResizeObserver.created;
  }

  afterEach(() => {
    if (realResizeObserver) {
      (window as unknown as { ResizeObserver: unknown }).ResizeObserver = realResizeObserver;
      realResizeObserver = undefined;
    }
  });

  it('rasterises the export at the density the stage affords', async () => {
    render(buildDto(comparableSet(3)), 4);
    // A fixture's element is never laid out, so the stage's geometry is given rather than measured.
    spyOn(component, 'measureStage').and.returnValue({ width: 800, height: 600, devicePixelRatio: 2 });
    openPreview();

    // A density above the stage's own only raises the cap on the preview; the stage still decides.
    component.onExportDensityChange(2);
    component.onExportResolutionChange('fullhd');
    await composePreview();

    const stage = component.previewCanvas!.nativeElement;
    expect(stage.width).toBe(1600);
    expect(stage.height).toBe(900);
    expect(stage.style.width).toBe('800px');
    expect(parseFloat(stage.style.height)).toBeCloseTo(450, 6);
  });

  it('fits a portrait target to the stage’s height, in the target’s own ratio', async () => {
    render(buildDto(comparableSet(3)), 4);
    spyOn(component, 'measureStage').and.returnValue({ width: 800, height: 600, devicePixelRatio: 2 });
    openPreview();

    component.onExportDensityChange(1);
    component.onExportResolutionChange('a4p');
    await composePreview();

    const stage = component.previewCanvas!.nativeElement;
    expect(parseFloat(stage.style.height)).toBeCloseTo(600, 6);
    expect(stage.height).toBeGreaterThan(stage.width);
    expect(Math.abs(stage.width - stage.height * (2480 / 3508))).toBeLessThan(1);
  });

  it('stops watching the stage when the preview closes', () => {
    render(buildDto(comparableSet(3)), 4);
    const observers = installFakeResizeObserver();
    openPreview();

    const stage = fixture.debugElement.query(By.css('.mc-preview-stage'))
      .nativeElement as HTMLElement;
    const watching = observers.filter(observer => observer.observed.includes(stage));
    expect(watching.length).toBe(1);

    component.onFigurePreviewClosed();

    expect(watching[0].disconnected).toBe(1);
  });

  it('summarises the export size and format in the Figures header', () => {
    render(buildDto(comparableSet(3)), 4);

    component.onExportDensityChange(1);
    component.onExportResolutionChange('fullhd');
    refresh();
    expect(textOf('.mc-export-summary')).toContain('Full HD — 1920 × 1080');
    expect(textOf('.mc-export-summary')).toContain('100%');
    expect(textOf('.mc-export-summary')).toContain('PNG');

    component.onExportFormatChange('webp');
    refresh();
    expect(textOf('.mc-export-summary')).toContain('WebP q85');
    expect(component.exportAspectLabel).toBe('16:9');
  });

  it('offers a preview control on every figure card, each naming its own figure', () => {
    render(buildDto(comparableSet(3)), 4);

    const previews = fixture.debugElement.queryAll(By.css('.mc-card .mc-preview-open'))
      .map(button => button.nativeElement as HTMLButtonElement);
    expect(previews.length).toBe(7);

    // Icon-only, so aria-label is the accessible name — and seven of them must not share one.
    const names = previews.map(button => button.getAttribute('aria-label') ?? '');
    expect(names.every(name => name.startsWith('Preview ') && name.endsWith(' at export size')))
      .toBeTrue();
    expect(new Set(names).size).toBe(7);
    expect(previews.every(button => button.textContent?.trim() === '')).toBeTrue();
  });

  // -------------------------------------------------------------------------------------------
  // Table export, and the two clipboard paths
  // -------------------------------------------------------------------------------------------

  /**
   * Intercepts the save path rather than the module that performs it: the object URL names the
   * blob that was written and the anchor names the file it was written under, which between them
   * are everything a download can be asserted on without a real file system.
   */
  function captureSaves(): { blobs: Blob[]; names: string[] } {
    const saved: { blobs: Blob[]; names: string[] } = { blobs: [], names: [] };
    spyOn(URL, 'createObjectURL').and.callFake((source: Blob | MediaSource) => {
      saved.blobs.push(source as Blob);
      return 'blob:model-comparison-test';
    });
    spyOn(URL, 'revokeObjectURL').and.stub();
    spyOn(HTMLAnchorElement.prototype, 'click').and.callFake(function (this: HTMLAnchorElement) {
      saved.names.push(this.download);
    });
    return saved;
  }

  /** `navigator.clipboard` is a prototype getter, so it is stood in for on the instance. */
  function withClipboard(value: unknown): void {
    Object.defineProperty(navigator, 'clipboard', { value, configurable: true });
  }

  afterEach(() => {
    delete (navigator as unknown as { clipboard?: unknown }).clipboard;
  });

  /** A stand-in for the dynamically imported spreadsheet writer, which the specs never really run. */
  function stubXlsxWriter(): void {
    spyOn(xlsxWriterModule, 'load').and.returnValue(Promise.resolve({
      default: () => ({ toBlob: () => Promise.resolve(new Blob(['xlsx-bytes'])) })
    } as any));
  }

  /**
   * A stand-in for the dynamically imported zip writer, which the specs never really run.
   *
   * The recorder holds the entry names of every archive it was asked to pack, which is what a
   * batch export can be asserted on without decoding one.
   */
  function stubZipWriter(): { names: string[][]; load: jasmine.Spy } {
    const names: string[][] = [];
    const load = spyOn(zipWriterModule, 'load').and.returnValue(Promise.resolve({
      zipSync: (data: Record<string, unknown>) => {
        names.push(Object.keys(data));
        // An empty archive's end-of-central-directory record: a valid zip, and nothing in it.
        return new Uint8Array([0x50, 0x4b, 0x05, 0x06]);
      }
    } as any));
    return { names, load };
  }

  it('offers the eight table formats with Excel first, and a Markdown copy beside the download', () => {
    render(buildDto(comparableSet(4)), 3);

    const select = fixture.debugElement.query(By.css('#mc-table-export-format'))
      .nativeElement as HTMLSelectElement;
    expect(Array.from(select.options).map(option => option.value))
      .toEqual(['xlsx', 'csv', 'tsv', 'md', 'json', 'html', 'png', 'webp']);
    // A placeholder is not a label, and this control carries no visible one.
    expect(fixture.debugElement.query(By.css('label[for="mc-table-export-format"]'))).toBeTruthy();
    expect(textOf('.mc-table-export')).toContain('Download table');
    expect(textOf('.mc-table-export')).toContain('Copy as Markdown');
    expect(textOf('.mc-table-provenance')).toContain('Current catalog, as of 2026-09-07');
    expect(textOf('.mc-table-provenance')).not.toContain('condition');
    expect(component.tableProvenance.conditionSignature).toBe('9c79137965e4');
  });

  it('offers a WebP quality for the table only while WebP is the chosen format', async () => {
    render(buildDto(comparableSet(4)), 3);

    const format = fixture.debugElement.query(By.css('#mc-table-export-format'))
      .nativeElement as HTMLSelectElement;
    expect(Array.from(format.options).find(option => option.value === 'webp')?.textContent?.trim())
      .toBe('WebP');
    expect(fixture.debugElement.query(By.css('#mc-table-export-webp-quality'))).toBeNull();

    component.onTableExportFormatChange('webp');
    refresh();
    // `ngModel` writes a freshly created select's initial value in a microtask, not in the pass
    // that renders it.
    await fixture.whenStable();

    const quality = fixture.debugElement.query(By.css('#mc-table-export-webp-quality'))
      .nativeElement as HTMLSelectElement;
    const labels = Array.from(quality.options).map(option => option.textContent?.trim());
    expect(labels).toEqual(['75', '80', '85', '90', '95', '100']);
    // 85 is the project-wide WebP quality, so the control opens on it rather than on lossless.
    expect(quality.selectedIndex).toBe(labels.indexOf('85'));
    expect(component.tableWebpQuality).toBe(85);
    expect(fixture.debugElement.query(By.css('label[for="mc-table-export-webp-quality"]'))).toBeTruthy();
  });

  it('opens the column chooser on the populated columns, and writes only those', async () => {
    render(buildDto(comparableSet(3)), 3);
    const saved = captureSaves();

    component.openTableColumnDialog();
    refresh();

    expect(fixture.debugElement.queryAll(By.css('.mc-columns-grid .checkbox-label')).length).toBe(26);
    // A comparable, fully priced set fills neither of these, so they open unticked and say so.
    expect(component.tableColumnEmpty.has('scheduledChange')).toBeTrue();
    expect(component.tableColumnEmpty.has('differsOn')).toBeTrue();
    expect(component.isTableColumnSelected('scheduledChange')).toBeFalse();
    expect(component.isTableColumnSelected('intelligenceIndex')).toBeTrue();
    expect(component.tableColumnSelectedCount).toBe(22);
    expect(textOf('.mc-columns-count')).toContain('22 of 26 columns');
    expect(textOf('.mc-columns-grid')).toContain('empty');

    component.tableExportFormat = 'csv';
    await component.confirmTableDownload();

    const header = (await saved.blobs[0].text()).split('\r\n')[0];
    expect(header).toContain('Intelligence Index');
    expect(header).not.toContain('Scheduled price change');
    expect(component.exportStatus).toContain('22 of 26 columns');
  });

  it('writes every ticked column, empty ones included, and refuses an empty selection', async () => {
    render(buildDto(comparableSet(3)), 3);
    const saved = captureSaves();

    component.openTableColumnDialog();
    component.selectAllTableColumns();
    refresh();
    expect(component.tableColumnSelectedCount).toBe(26);

    component.tableExportFormat = 'csv';
    await component.confirmTableDownload();
    expect((await saved.blobs[0].text()).split('\r\n')[0]).toContain('Scheduled price change');

    component.openTableColumnDialog();
    for (const column of component.tableColumns) {
      if (component.isTableColumnSelected(column.key)) {
        component.toggleTableColumn(column.key);
      }
    }
    refresh();

    expect(component.tableColumnSelectedCount).toBe(0);
    const download = fixture.debugElement
      .queryAll(By.css('.mc-columns-dialog .dialog-actions .btn-gh'))
      .map(button => button.nativeElement as HTMLButtonElement)
      .find(button => (button.textContent ?? '').trim() === 'Download');
    expect(download!.disabled).toBeTrue();
  });

  it('keeps the nested chooser close event off the wizard dialog that contains it', () => {
    render(buildDto(comparableSet(3)), 3);

    // The host closes the whole wizard from its own dialog's close, and this one is a descendant:
    // both events have to stop where they are raised.
    for (const type of ['cancel', 'close']) {
      const event = new Event(type, { bubbles: true });
      const stopped = spyOn(event, 'stopPropagation').and.callThrough();
      component.onTableColumnsDialogClose(event);
      expect(stopped).withContext(type).toHaveBeenCalled();
    }
  });

  it('writes one file per table format, under the extension that format names', async () => {
    render(buildDto(comparableSet(4)), 3);
    const saved = captureSaves();
    stubXlsxWriter();

    for (const format of ['xlsx', 'csv', 'tsv', 'md', 'json', 'html'] as TableExportFormat[]) {
      component.tableExportFormat = format;
      await component.downloadTable();
    }

    expect(saved.blobs.length).toBe(6);
    expect(saved.names.map(name => name.split('.').pop()))
      .toEqual(['xlsx', 'csv', 'tsv', 'md', 'json', 'html']);
    expect(saved.names.every(name => /^model-comparison_table_\d{8}_\d{6}\./.test(name))).toBeTrue();
    expect(saved.blobs.every(blob => blob.size > 0)).toBeTrue();
    expect(component.exportStatus).toContain('4 entries');
    expect(component.exportStatus).toContain('current sort, filters applied, all pages');
    expect(component.exporting).toBeFalse();
  });

  it('writes the table as an image in both image formats', async () => {
    render(buildDto(comparableSet(2)), 3);
    const saved = captureSaves();

    component.tableExportFormat = 'png';
    await component.downloadTable();
    component.tableExportFormat = 'webp';
    await component.downloadTable();

    expect(saved.names[0]).toMatch(/\.png$/);
    // A browser with no WebP encoder answers with a PNG, and the file is then named .png.
    expect(saved.names[1]).toMatch(/\.(webp|png)$/);
    expect(saved.blobs.every(blob => blob.size > 0)).toBeTrue();
  });

  it('exports every filtered row across all pages, not the visible page', async () => {
    render(buildDto(comparableSet(12)), 3);
    const saved = captureSaves();
    expect(component.entryTable.view(component.entries).length).toBe(10);

    component.tableExportFormat = 'csv';
    await component.downloadTable();

    // Twelve records and a header, from a page showing ten.
    const text = await saved.blobs[0].text();
    expect(text.trimEnd().split('\r\n').length).toBe(13);
    expect(component.exportStatus).toContain('12 entries');
  });

  it('exports the rows the column filters leave, and says how many', async () => {
    render(buildDto(comparableSet(12)), 3);
    const saved = captureSaves();
    // Model 1, Model 10, Model 11 and Model 12.
    component.entryTable.setFilter('label', 'Model 1');

    component.tableExportFormat = 'csv';
    await component.downloadTable();

    expect((await saved.blobs[0].text()).trimEnd().split('\r\n').length).toBe(5);
    expect(component.exportStatus).toContain('4 entries');
  });

  it('copies the table as Markdown, whatever the format control says', async () => {
    render(buildDto(comparableSet(3)), 3);
    const writeText = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
    withClipboard({ writeText });

    await component.copyTableMarkdown();

    expect(writeText).toHaveBeenCalledTimes(1);
    expect(writeText.calls.mostRecent().args[0] as string).toContain('| Model |');
    expect(component.exportStatus).toBe('Copied 3 entries as Markdown.');
    expect(component.exporting).toBeFalse();
  });

  it('reports a refused clipboard write inline rather than throwing', async () => {
    render(buildDto(comparableSet(3)), 3);
    withClipboard({ writeText: () => Promise.reject(new Error('Document is not focused.')) });

    await expectAsync(component.copyTableMarkdown()).toBeResolved();

    expect(component.exportStatus).toBe('The clipboard write was refused.');
    expect(component.exporting).toBeFalse();
  });

  it('reports an absent clipboard API inline, and names the way out', async () => {
    render(buildDto(comparableSet(3)), 3);
    withClipboard(undefined);

    await component.copyTableMarkdown();

    expect(component.exportStatus).toContain('cannot copy text to the clipboard');
    expect(component.exportStatus).toContain('download the table instead');
    expect(component.exporting).toBeFalse();
  });

  it('copies one figure to the clipboard and names the card in the status', async () => {
    render(buildDto(comparableSet(3)), 4);
    const write = jasmine.createSpy('write').and.returnValue(Promise.resolve());
    withClipboard({ write });
    const card = component.panelCards[0];

    await component.copyFigure(card);

    expect(write).toHaveBeenCalledTimes(1);
    expect(component.exportStatus).toBe(`Copied ${card.title} to the clipboard.`);
    expect(component.exporting).toBeFalse();
  });

  it('reports a refused figure copy, and an absent clipboard API, as inline text', async () => {
    render(buildDto(comparableSet(3)), 4);
    withClipboard({ write: () => Promise.reject(new Error('Write permission denied.')) });

    await component.copyFigure(component.panelCards[0]);
    expect(component.exportStatus).toBe('The clipboard write was refused.');

    withClipboard(undefined);
    await component.copyFigure(component.panelCards[0]);
    expect(component.exportStatus).toContain('cannot copy images to the clipboard');
    expect(component.exportStatus).toContain('download the figure instead');
    expect(component.exporting).toBeFalse();
  });

  // -------------------------------------------------------------------------------------------
  // The figure archive, and the toast the outcome lands in
  // -------------------------------------------------------------------------------------------

  it('writes a batch as one archive, under one timestamp shared with every figure in it', async () => {
    render(buildDto(comparableSet(3)), 4);
    const saved = captureSaves();
    const zip = stubZipWriter();

    await component.downloadAllFigures();

    // One save for the whole batch: a second anchor click from one gesture is what browsers prompt
    // over, and a prompt mid-batch writes an unpredictable subset.
    expect(saved.names.length).toBe(1);
    expect(saved.names[0]).toMatch(/^model-comparison_figures_\d{8}_\d{6}\.zip$/);
    expect(saved.blobs[0].type).toBe('application/zip');

    const stamp = /_(\d{8}_\d{6})\.zip$/.exec(saved.names[0])![1];
    expect(zip.names.length).toBe(1);
    expect(zip.names[0].length).toBe(component.exportableCards.length);
    expect(zip.names[0].every(name => /^model-comparison_.+\.(png|webp)$/.test(name))).toBeTrue();
    expect(zip.names[0].every(name => name.includes(stamp))).toBeTrue();
  });

  it('composes a figure export from the suite and the computation time, and nothing else in the footer', () => {
    render(buildDto(comparableSet(3)), 4);
    const card = component.panelCards[0];

    const { chrome, footer } = (component as unknown as {
      exportChrome(card: ComparisonFigureCard): { chrome: FigureChrome; footer: FigureFooter };
    }).exportChrome(card);

    expect(footer).toEqual({
      suite: 'GnollHack Player Assistance Benchmark Suite',
      computedAt: formatComputedAt('2026-09-07T12:00:00Z')
    });
    expect(footer.computedAt).not.toBe('2026-09-07T12:00:00Z');
    expect(footer.computedAt).toContain('2026');
    const footerText = JSON.stringify(footer);
    expect(footerText).not.toContain('condition');
    expect(footerText).not.toContain('entries charted');
    const chromeText = chrome.notes.map(note => note.text).join(' ');
    expect(chromeText).not.toContain('condition');
    expect(chromeText).not.toContain('entries charted');
  });

  it('writes one image and no archive for a single figure from the preview', async () => {
    render(buildDto(comparableSet(3)), 4);
    const saved = captureSaves();
    const zip = stubZipWriter();
    openPreview(component.panelCards[0]);

    await component.downloadPreviewedFigure();

    expect(saved.names.length).toBe(1);
    expect(saved.names[0]).toMatch(/^model-comparison_.+\.(png|webp)$/);
    expect(zip.load).not.toHaveBeenCalled();
    expect(zip.names.length).toBe(0);
  });

  it('announces a written batch as a success naming the archive', async () => {
    render(buildDto(comparableSet(3)), 4);
    captureSaves();
    stubZipWriter();

    await component.downloadAllFigures();

    expect(component.exportNotice?.kind).toBe('success');
    expect(component.exportNotice?.message).toContain('saved to model-comparison_figures_');
    expect(component.exporting).toBeFalse();
  });

  it('announces a wholly refused batch as an error, and writes nothing', async () => {
    render(buildDto(comparableSet(3)), 4);
    const saved = captureSaves();
    const zip = stubZipWriter();

    // Caveats no offered box can hold, so every card is refused for what it carries rather than
    // for the size it was asked for.
    const notice = (index: number): string =>
      `Notice ${index}: ` +
      'the speed axis is degraded for this entry, so its bar is drawn from a partial sample. '
        .repeat(6);
    spyOn(component as unknown as { exportChrome(card: ComparisonFigureCard): unknown }, 'exportChrome')
      .and.returnValue({
        chrome: {
          title: 'Figure', badges: [], detail: '', key: [], highlight: '',
          notes: [1, 2, 3, 4, 5, 6].map(index => ({ text: notice(index), tone: 'warning' as const }))
        },
        footer: { suite: 'Suite A', computedAt: 'Current catalog, 3 Sep 2026' }
      });

    component.onExportResolutionChange('custom');
    component.onCustomWidthChange(1280);
    component.onCustomHeightChange(720);
    expect(component.customResolutionError).toBe('');

    await component.downloadAllFigures();

    expect(saved.names.length).toBe(0);
    expect(zip.names.length).toBe(0);
    expect(component.exportNotice?.kind).toBe('error');
    expect(component.exportNotice?.message).toContain('No figure was written at this size.');
  });

  it('lays the preview footer out as a row of its own, its controls spaced', () => {
    render(buildDto(comparableSet(3)), 4);
    openPreview();

    const foot = fixture.debugElement.query(By.css('.mc-preview-foot')).nativeElement as HTMLElement;
    const style = getComputedStyle(foot);
    expect(style.display).toBe('flex');
    expect(style.columnGap).not.toBe('0px');
    expect(style.columnGap).not.toBe('normal');
  });

  it('hands the notice to the toast inside whichever modal is innermost', async () => {
    render(buildDto(comparableSet(3)), 4);
    withClipboard({ write: () => Promise.resolve() });

    await component.copyFigure(component.panelCards[0]);
    refresh();

    // Two, one per modal: everything outside the innermost open dialog is inert.
    expect(fixture.debugElement.queryAll(By.css('app-toast')).length).toBe(2);
    const toasts = fixture.debugElement.queryAll(By.directive(ToastComponent))
      .map(element => element.componentInstance as ToastComponent);
    expect(toasts.length).toBe(2);
    expect(toasts[0].notice).toBe(component.exportNotice);
    expect(toasts[1].notice).toBeNull();

    openPreview();

    expect(toasts[0].notice).toBeNull();
    expect(toasts[1].notice).toBe(component.exportNotice);
  });

});

describe('selectionNotices', () => {
  function entry(
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

  function buildIndex(entries: BenchmarkComparabilityIndexEntryDto[]): BenchmarkComparabilityIndexDto {
    return {
      computedAtUtc: '2026-09-05T12:00:00Z',
      entries,
      conditions: [
        {
          ordinal: 1, label: 'Condition A', sourceCount: 2, runCount: 2,
          signature: 'sig-a', newestRunStartedAtUtc: '2026-09-05T10:00:00Z'
        },
        {
          ordinal: 2, label: 'Condition B', sourceCount: 1, runCount: 1,
          signature: 'sig-b', newestRunStartedAtUtc: '2026-09-04T10:00:00Z'
        }
      ],
      largestConditionKeys: [{
        name: 'serviceTier',
        label: 'Candidate service tier',
        description: 'The service tier the candidate ran under.',
        kind: 'Instrument',
        valueKind: 'Text',
        value: 'standard',
        displayValue: null
      }],
      referenceSelectionRule: 'The reference condition is the one with the most sources.',
      mustMatchKeyNames: ['serviceTier'],
      modelAxisKeyNames: ['modelId'],
      degradingKeyNames: ['QuestionParallelism', 'PricingSnapshot']
    };
  }

  /**
   * Two runs in the reference condition, one outside it, and one analysis group whose own members
   * disagree — which is the whole space of placements the index can report.
   */
  function defaultIndex(): BenchmarkComparabilityIndexDto {
    return buildIndex([
      entry({ key: 'run:1', sourceId: 1 }),
      entry({ key: 'run:2', sourceId: 2 }),
      entry({
        key: 'run:3',
        sourceId: 3,
        conditionOrdinal: 2,
        conditionLabel: 'Condition B',
        signature: 'sig-b'
      }),
      entry({
        key: 'group:11',
        sourceKind: 'Group',
        sourceId: 11,
        conditionOrdinal: 0,
        conditionLabel: 'Self-inconsistent',
        selfInconsistent: true,
        selfInconsistentKeys: ['ScoringMethodVersion']
      })
    ]);
  }

  function state(overrides: Partial<ComparisonSelectionState> = {}): ComparisonSelectionState {
    return {
      index: defaultIndex(),
      indexLoading: false,
      indexError: null,
      runIds: [],
      groupIds: [],
      pricingBasis: 'Current',
      ...overrides
    };
  }

  function ids(notices: readonly ComparisonSelectionNotice[]): string[] {
    return notices.map(notice => notice.id);
  }

  it('says nothing about a selection that sits wholly in the reference condition', () => {
    expect(selectionNotices(state({ runIds: [1, 2] }))).toEqual([]);
  });

  it('says nothing while nothing is selected and the index is in hand', () => {
    expect(selectionNotices(state())).toEqual([]);
  });

  it('reports a failed index as an error, carrying the server text', () => {
    const notices = selectionNotices(state({
      index: null,
      indexError: 'The index could not be built.'
    }));

    expect(ids(notices)).toEqual(['index-error']);
    expect(notices[0].severity).toBe('error');
    expect(notices[0].body).toContain('The index could not be built.');
    expect(notices[0].body).toContain('cannot be checked for comparability before Compare');
  });

  it('reports an index still in flight as information', () => {
    const notices = selectionNotices(state({ index: null, indexLoading: true }));

    expect(ids(notices)).toEqual(['index-loading']);
    expect(notices[0].severity).toBe('info');
  });

  it('refuses the whole selection when no condition contains any of it', () => {
    const notices = selectionNotices(state({ groupIds: [11] }));

    expect(ids(notices)).toEqual(['no-condition', 'self-inconsistent']);
    expect(notices[0].severity).toBe('error');
    expect(notices[0].body).toContain('would chart nothing');
  });

  it('stays silent about an empty condition set while the index has not landed', () => {
    // Otherwise every selection would be refused for the length of the round trip.
    expect(ids(selectionNotices(state({ index: null, indexLoading: true, runIds: [1] }))))
      .toEqual(['index-loading']);
  });

  it('names the excluded count and the reference condition once the selection crosses one', () => {
    const notices = selectionNotices(state({ runIds: [1, 3] }));

    expect(ids(notices)).toEqual(['cross-condition', 'single-point']);
    expect(notices[0].body).toBe('1 of 2 selected sources fall outside Condition A and will be '
      + 'excluded from the comparison — only one condition can be charted.');
  });

  it('warns about a self-inconsistent group inside an otherwise single-condition selection', () => {
    // The gap the cross-condition sentence cannot close: an unassigned group folds to no condition,
    // so the selection still spans exactly one and nothing used to be said about the exclusion.
    const notices = selectionNotices(state({ runIds: [1, 2], groupIds: [11] }));

    expect(ids(notices)).toEqual(['self-inconsistent']);
    expect(notices[0].severity).toBe('warning');
    expect(notices[0].body).toContain('Analysis group 11');
    expect(notices[0].body).toContain('ScoringMethodVersion');
  });

  it('warns that one point in the reference condition draws no figure', () => {
    const notices = selectionNotices(state({ runIds: [1] }));

    expect(ids(notices)).toEqual(['single-point']);
    expect(notices[0].body).toContain('A comparison needs two points');
  });

  it('warns when the sources to be charted differ on question parallelism', () => {
    const notices = selectionNotices(state({
      index: buildIndex([
        entry({ key: 'run:1', sourceId: 1, questionParallelism: '1' }),
        entry({ key: 'run:2', sourceId: 2, questionParallelism: '4' })
      ]),
      runIds: [1, 2]
    }));

    expect(ids(notices)).toEqual(['degrading-keys']);
    expect(notices[0].heading).toBe('The speed and cost axes will be flagged');
    expect(notices[0].body).toContain('QuestionParallelism');
    expect(notices[0].body).toContain('speed axis');
    expect(notices[0].body).toContain('cost axis');
  });

  it('warns that a differing speed calibration flags the speed axis alone', () => {
    const index = buildIndex([
      entry({ key: 'run:1', sourceId: 1, speedCalibration: 'speed-old' }),
      entry({ key: 'run:2', sourceId: 2, speedCalibration: 'speed-new' })
    ]);

    const notices = selectionNotices(state({ index, runIds: [1, 2] }));

    expect(ids(notices)).toEqual(['degrading-keys']);
    // The calibration cannot move a quality or a cost number, so the heading claims neither.
    expect(notices[0].heading).toBe('The speed axis will be flagged');
    expect(notices[0].body).toContain('SpeedCalibration');
    expect(notices[0].body).not.toContain('cost axis');
  });

  it('warns about a differing pricing snapshot only on the As-run basis', () => {
    const index = buildIndex([
      entry({ key: 'run:1', sourceId: 1, pricingSnapshot: '2026-08-01' }),
      entry({ key: 'run:2', sourceId: 2, pricingSnapshot: '2026-09-01' })
    ]);

    // Repriced to one basis, the figures are not charting the stored snapshot prices at all, so a
    // snapshot difference no longer describes the cost axis.
    expect(selectionNotices(state({ index, runIds: [1, 2], pricingBasis: 'Current' }))).toEqual([]);

    const notices = selectionNotices(state({ index, runIds: [1, 2], pricingBasis: 'AsRun' }));
    expect(ids(notices)).toEqual(['degrading-keys']);
    // The snapshot degrades cost only, so the heading claims nothing about the speed axis.
    expect(notices[0].heading).toBe('The cost axis will be flagged');
    expect(notices[0].body).toContain('PricingSnapshot');
    expect(notices[0].body).toContain('cost axis');
  });

  it('orders errors above warnings above information', () => {
    const notices = selectionNotices(state({
      indexError: 'The index is stale.',
      indexLoading: true,
      runIds: [1, 3]
    }));

    expect(ids(notices))
      .toEqual(['index-error', 'cross-condition', 'single-point', 'index-loading']);
    expect(notices.map(notice => notice.severity))
      .toEqual(['error', 'warning', 'warning', 'info']);
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
    <app-benchmark-model-comparison [comparison]="comparison" [selectedRunCount]="2">
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
      providers: [provideCharts({ registerables: APP_CHART_REGISTRABLES })]
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
      baselineSignature: '',
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
      baselineSignature: '',
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
    expect(Number.isNaN(entry.modelTimeMeanMs)).toBeTrue();
    expect(Number.isNaN(entry.totalModelTimeMs)).toBeTrue();
    expect(Number.isNaN(entry.candidateCostPerQuestionUsd)).toBeTrue();
    expect(Number.isNaN(entry.totalRunCostUsd)).toBeTrue();
    expect(entry.candidateCostPerQuestionSdUsd).toBeNull();
    expect(entry.speedIndexSd).toBeNull();
    expect(entry.totalModelTimeSdMs).toBeNull();
  });

  it('reads items per run and the pricing label off the payload header', () => {
    const context = toChartContext(null);
    expect(context.itemsPerRun).toBe(0);
    expect(context.pricingBasisLabel).toBe('Unknown pricing basis');
    expect(context.pricingBasis).toBe('');
    expect(context.pricedOn).toBe('');
  });
});
