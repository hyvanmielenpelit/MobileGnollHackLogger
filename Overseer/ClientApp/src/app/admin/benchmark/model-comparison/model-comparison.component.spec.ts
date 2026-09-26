import { ChangeDetectorRef, Component, DebugElement, inject } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { BaseChartDirective, provideCharts } from 'ng2-charts';

import {
  ComparisonFigureCard,
  ComparisonWizardStep,
  DOWNLOAD_SETTINGS_STORAGE_KEY,
  FIGURE_SIDEBAR_STORAGE_KEY,
  FIGURE_STYLE_STORAGE_KEY,
  FigureSidebarTab,
  SIDEBAR_WIDTH_DEFAULT,
  SIDEBAR_WIDTH_MAX,
  SIDEBAR_WIDTH_MIN,
  TABLE_COLUMNS_STORAGE_KEY,
  ModelComparisonComponent,
  sidebarTabForView
} from './model-comparison.component';
import {
  FRONTIER_UNCERTAINTY_NOTE,
  MAX_PLOTTED_ENTRIES,
  MEAN_TIME_NO_INTERVAL_NOTE,
  P1_STACK_BREAKPOINT_PX,
  directLabelPlugin,
  errorBarPlugin
} from './model-comparison-charts';
import { DEFAULT_FIGURE_STYLE, HIDDEN_INTERVALS_NOTE } from './figure-style';
import type { FigureStyle } from './figure-style';
import type { DirectLabelBlock, DirectLabelPluginOptions } from './model-comparison-charts';
import { formatComputedAt } from './figure-chrome';
import type { FigureChrome, FigureFooter } from './figure-chrome';
import { APP_CHART_REGISTRABLES } from '../../../chart-registrables';
import {
  BenchmarkComparabilityIndexDto,
  BenchmarkComparabilityIndexEntryDto,
  BenchmarkModelComparisonCostDto,
  BenchmarkModelComparisonDto,
  BenchmarkModelComparisonEntryDto,
  ComparisonSelectedSource,
  ComparisonSelectionNotice,
  ComparisonSelectionState,
  selectionNotices,
  toChartContext,
  toChartEntries
} from './model-comparison.models';
import { DEFAULT_TABLE_COLUMNS, TableColumnConfig, TableFileFormat, xlsxWriterModule } from './table-export';
import { FIGURE_EXPORT_MAX_DENSITY_PERCENT, FIGURE_EXPORT_MAX_DIMENSION, zipWriterModule } from './figure-export';
import { FIGURE_SIZE_STORAGE_KEY, TABLE_IMAGE_SIZE_STORAGE_KEY, defaultFigureSize } from './figure-size';
import { fitHeightZoom, previewZoomRange, zoomToSlider } from './preview-view';
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
        examItemCount: 18,
        unscoredItemCount: 0,
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
        totalRunCostPerRunUsd: 0.9876,
        totalRunCostSdUsd: 0.0432,
        totalRunCostUnavailableReason: null,
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
          measure: 'Cost per index point',
          reason: 'A ratio of two noisy estimators. It has no simple confidence interval and '
            + 'inverts its meaning as the index approaches zero, which is why the group cost '
            + 'statistics already guard it against a non-positive index.',
          summary: 'Dividing cost by a noisy score gives a number with no reliable error bars, '
            + 'and it swings wildly as the score nears zero.',
          instead: 'Candidate $ / question in the comparison table, read beside the Intelligence '
            + 'Index and its ± interval.'
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
   * almost every assertion below is about step 2, which is the default. Step 2 holds the charts
   * and the table, and opens on All charts in a fresh browser, or on the Interactive table where
   * nothing can be charted. `goToStep` refuses an unreachable step, so a test that asks for step 2
   * without a comparison finds no panel rather than a quietly passing assertion.
   */
  function render(dto: BenchmarkModelComparisonDto | null, step: ComparisonWizardStep = 2): void {
    fixture.componentRef.setInput('comparison', dto);
    fixture.detectChanges();
    component.goToStep(step);
    fixture.detectChanges();
  }

  /** Opens one of step 2's four views through its tab. */
  function showView(view: 'all' | 'single' | 'table' | 'tablePreview'): void {
    (fixture.debugElement.query(By.css(`#mc-fig-tab-${view}`)).nativeElement as HTMLButtonElement).click();
    fixture.detectChanges();
  }

  /** Step 2 on the Interactive table, where the comparison table lives. */
  function renderTable(dto: BenchmarkModelComparisonDto | null): void {
    render(dto, 2);
    if (component.effectiveFigureTab !== 'table') {
      showView('table');
    }
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

  /** The footer's Compare / Next / Close button. */
  function nextButton(): HTMLButtonElement {
    return fixture.debugElement.query(By.css('.mc-wizard-next')).nativeElement as HTMLButtonElement;
  }

  /** The footer's Cancel Comparison button, or null while none is rendered. */
  function cancelCompareButton(): HTMLButtonElement | null {
    return fixture.debugElement.queryAll(By.css('.mc-wizard-nav-actions .btn-gh-cancel'))
      .map(button => button.nativeElement as HTMLButtonElement)
      .find(button => (button.textContent ?? '').includes('Cancel Comparison')) ?? null;
  }

  /** Every key this wizard remembers per browser. */
  const STORED_KEYS = [
    FIGURE_STYLE_STORAGE_KEY, FIGURE_SIDEBAR_STORAGE_KEY, FIGURE_SIZE_STORAGE_KEY, TABLE_IMAGE_SIZE_STORAGE_KEY,
    TABLE_COLUMNS_STORAGE_KEY, DOWNLOAD_SETTINGS_STORAGE_KEY
  ];

  beforeEach(async () => {
    // The figure style, the sizes, the columns, the formats and the sidebar are remembered per
    // browser, so one spec's must not reach the next.
    STORED_KEYS.forEach(key => localStorage.removeItem(key));
    await TestBed.configureTestingModule({
      imports: [ModelComparisonComponent],
      providers: [provideCharts({ registerables: APP_CHART_REGISTRABLES })]
    }).compileComponents();

    fixture = TestBed.createComponent(ModelComparisonComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    STORED_KEYS.forEach(key => localStorage.removeItem(key));
  });

  // The All tab leaves a debounced composition behind it, and a timer that outlived its test would
  // compose against the next one's fixture.
  afterEach(() => {
    (component as unknown as { detachAll(): void }).detachAll();
  });

  /** Selects one tab of the step-3 settings sidebar by clicking it. */
  function openSidebarTab(tab: FigureSidebarTab): void {
    (fixture.debugElement.query(By.css(`#mc-side-tab-${tab}`)).nativeElement as HTMLButtonElement).click();
    fixture.detectChanges();
  }

  // -------------------------------------------------------------------------------------------
  // The Interactive table
  // -------------------------------------------------------------------------------------------

  it('renders the Interactive table as a view of step 2, with no control that hides it', () => {
    renderTable(buildDto(comparableSet(4)));

    const table = fixture.debugElement.query(By.css('#mc-fig-panel-table table.mc-table'));
    expect(table).withContext('the table is a tabpanel of its own').toBeTruthy();
    expect(fixture.debugElement.queryAll(By.css('app-table-pager')).length).toBe(2);
    expect(fixture.debugElement.queryAll(By.css('table.mc-table tbody tr')).length).toBe(4);

    // Nothing in the view may hide the table, so there is no control that could.
    const toggles = fixture.debugElement
      .queryAll(By.css('button'))
      .map(element => (element.nativeElement as HTMLElement).getAttribute('aria-label') ?? '');
    expect(toggles.some(label => /show table|hide table/i.test(label))).toBeFalse();
  });

  it('keeps both table pagers outside the horizontal scroll wrapper', () => {
    renderTable(buildDto(comparableSet(4)));

    expect(fixture.debugElement.queryAll(By.css('.gh-datatable-scroll app-table-pager')).length).toBe(0);
    expect(fixture.debugElement.queryAll(By.css('.mc-table-block > app-table-pager')).length).toBe(2);
  });

  it('carries the three timings in one labelled column, after Speed Index', () => {
    renderTable(buildDto(comparableSet(1)));

    // Model, R, State, Intelligence Index, Speed Index, Timings, Candidate $ / question, Total $ / run, Notes.
    const headers = fixture.debugElement.queryAll(By.css('table.mc-table thead tr:first-child th'))
      .map(header => (header.nativeElement as HTMLElement).textContent?.trim() ?? '');
    expect(headers.length).toBe(9);
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
    renderTable(buildDto(entries));

    // The rows follow the model order, so each is found by the source it names.
    const first = tableRowOf('Run 1');

    // Plain text, not a control: emphasis is chosen in the chart views' Data tab, where its effect is visible.
    expect(first.querySelector('.mc-model-name')?.textContent?.trim()).toBe('Gemini 2.5 Flash');
    expect(first.querySelector('.provider-badge')?.textContent?.trim()).toBe('Google');
    expect(first.querySelector('.thinking-badge')?.textContent?.trim()).toBe('medium');
    expect(first.querySelector('.mc-source')?.textContent?.trim()).toBe('Run 1');

    expect(tableRowOf('Run 2').querySelector('.thinking-badge')).toBeNull();
  });

  /** The Interactive table's body row whose source line reads `source`. */
  function tableRowOf(source: string): HTMLElement {
    const row = fixture.debugElement.queryAll(By.css('table.mc-table tbody tr'))
      .map(candidate => candidate.nativeElement as HTMLElement)
      .find(candidate => candidate.querySelector('.mc-source')?.textContent?.trim() === source);
    expect(row).withContext(`the row of ${source}`).toBeTruthy();
    return row!;
  }

  it('badges a non-baseline reasoning mode in the table and the Models list, and never standard', () => {
    const entries = comparableSet(2);
    entries[0] = { ...entries[0], reasoningMode: 'pro' };
    entries[1] = { ...entries[1], reasoningMode: 'standard' };
    renderTable(buildDto(entries));

    const pro = tableRowOf('Run 1');
    expect(pro.querySelector('.reasoning-badge')?.textContent?.trim()).toBe('pro');
    // Thinking level, reasoning mode, then the provider last.
    const badgeOrder = Array.from(pro.querySelector('.mc-model-row')!.children)
      .map(child => ['thinking-badge', 'reasoning-badge', 'provider-badge']
        .find(name => child.classList.contains(name)))
      .filter(name => name != null);
    expect(badgeOrder).toEqual(['thinking-badge', 'reasoning-badge', 'provider-badge']);
    expect(tableRowOf('Run 2').querySelector('.reasoning-badge')).toBeNull();

    openSidebarTab('data');
    // The Models table follows the model order, not an index, so the badges are counted rather than indexed.
    expect(fixture.debugElement.queryAll(By.css('.mc-models-table tbody tr')).length).toBe(2);
    const badges = fixture.debugElement.queryAll(By.css('.mc-models-table .reasoning-badge'))
      .map(badge => (badge.nativeElement as HTMLElement).textContent?.trim());
    expect(badges).toEqual(['pro']);
  });

  it('keeps an excluded entry in the table even though no figure can draw it', () => {
    renderTable(buildDto([...comparableSet(3), buildExcludedEntry('run:9', ['ScoringMethodVersion'])]));

    expect(fixture.debugElement.queryAll(By.css('.mc-table tbody tr')).length).toBe(4);
    expect(component.figures?.selection.plotted.length).toBe(3);
    expect(component.figures?.selection.excluded.length).toBe(1);
  });

  // -------------------------------------------------------------------------------------------
  // Exclusions
  // -------------------------------------------------------------------------------------------

  it('names the comparability keys an excluded entry differs on', () => {
    render(buildDto([...comparableSet(3), buildExcludedEntry('run:9', ['ScoringMethodVersion'])]));

    component.openAbout();
    fixture.detectChanges();

    const excluded = textOf('.mc-about-excluded');
    expect(excluded).toContain('ScoringMethodVersion');
    expect(excluded).toContain('v8');
    expect(excluded).toContain('v9');
  });

  it('draws no axes at all when nothing in the set may be charted together', () => {
    render(buildDto([
      buildExcludedEntry('run:8', ['ScoringMethodVersion']),
      buildExcludedEntry('run:9', ['CandidatePromptOptions'])
    ]));

    expect(component.shape).toBe('none');
    expect(fixture.debugElement.queryAll(By.css('canvas')).length).toBe(0);
    expect(textOf('#mc-fig-unavailable')).toContain(
      'Fewer than two models were measured the same way, so there is nothing to chart. The table lists every model and why.');
    for (const view of ['all', 'single']) {
      const tab = fixture.debugElement.query(By.css(`#mc-fig-tab-${view}`)).nativeElement as HTMLButtonElement;
      expect(tab.getAttribute('aria-disabled')).withContext(view).toBe('true');
    }

    // Step 2 opens on the Interactive table over a set no chart can draw, so the excluded entries
    // stay listed regardless.
    expect(component.effectiveFigureTab).toBe('table');
    expect(fixture.debugElement.queryAll(By.css('.mc-table tbody tr')).length).toBe(2);
    expect(fixture.debugElement.queryAll(By.css('canvas')).length).toBe(0);
  });

  // -------------------------------------------------------------------------------------------
  // Degenerate shapes
  // -------------------------------------------------------------------------------------------

  it('suppresses the profile plot at two entries and keeps P1 and the scatters', () => {
    render(buildDto(comparableSet(2)), 2);

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

  /** Opens the sidebar's Charts tab on one family, through the real tabs. */
  function openStyleFamily(kind: 'bar' | 'profile' | 'scatter'): void {
    openSidebarTab('charts');
    (fixture.debugElement.query(By.css(`#mc-style-family-tab-${kind}`)).nativeElement as HTMLButtonElement).click();
    fixture.detectChanges();
  }

  /** The nth trade-off checkbox in the Charts tab: 0 names the marks, 1 draws their values. */
  function scatterToggle(index: number): DebugElement {
    openStyleFamily('scatter');
    return fixture.debugElement.query(By.css(
      index === 0 ? '#mc-style-scatter-directLabels' : '#mc-style-scatter-inlineValues'));
  }

  function tick(toggle: DebugElement, on: boolean): void {
    (toggle.nativeElement as HTMLInputElement).checked = on;
    toggle.triggerEventHandler('change', { target: toggle.nativeElement });
    fixture.detectChanges();
  }

  it('swaps the scatter legends for direct labels when the toggle is ticked, and back', () => {
    render(buildDto(comparableSet(3)), 2);

    // The values toggle is on by default, so the plugin is already registered; what the names
    // toggle changes is the legend and whether a block carries a name.
    expect(scatterLegendDisplays()).toEqual([true, true, true]);
    expect(scatterPluginIds().every(ids => ids.includes(directLabelPlugin.id))).toBeTrue();
    expect(scatterBlocks()!.every(b => b.name === undefined)).toBeTrue();

    const toggle = scatterToggle(0);
    expect(toggle).withContext('the toggle sits in the Charts tab, under Trade-offs').toBeTruthy();
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
    render(buildDto(comparableSet(3)), 2);

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
    render(buildDto(comparableSet(3)), 2);

    expect(component.shape).toBe('full');
    expect(fixture.debugElement.queryAll(By.css('canvas')).length).toBe(7);
    // The normalization caveat's exact wording belongs to the chart builder; this only checks the
    // profile figure carries some explanatory note rather than none.
    expect(component.profileCard?.chrome.notes.length).toBeGreaterThan(0);
  });

  it('carries a pricing badge on the cost scatter and withholds it from the quality-speed one', () => {
    render(buildDto(comparableSet(3)), 2);

    const scatters = component.scatterCards;
    expect(scatters.length).toBe(3);
    // S1 is quality vs. speed, which carries no cost axis; S2 is quality vs. cost. The composer draws
    // the chrome's badges into the tile and the file alike.
    expect(scatters[0].chrome.badges.filter(badge => badge.tone === 'pricing').length).toBe(0);
    expect(scatters[1].chrome.badges.filter(badge => badge.tone === 'pricing').length).toBeGreaterThan(0);
  });

  it('names the Better direction in each scatter and bar tile\'s accessible name, and never as a badge', () => {
    render(buildDto(comparableSet(3)), 2);

    const label = (card: ComparisonFigureCard): string =>
      (fixture.debugElement.query(By.css(`canvas[data-figure-id="${card.id}"]`)).nativeElement as HTMLCanvasElement)
        .getAttribute('aria-label') ?? '';
    const scatters = component.scatterCards;
    expect(label(scatters[0])).toContain('Better toward the top left');
    expect(label(scatters[1])).toContain('Better toward the top left');
    expect(label(scatters[2])).toContain('Better toward the bottom left');
    // Intelligence is better higher: up on vertical bars, right on horizontal ones.
    expect(label(component.panelCards[0])).toContain(
      component.effectiveOrientation === 'vertical' ? 'Better toward the top' : 'Better toward the right');

    for (const card of [...scatters, ...component.panelCards]) {
      expect(card.chrome.direction).withContext(card.id).toBeDefined();
      expect(card.chrome.badges.some(badge => badge.text.includes('Better'))).withContext(card.id).toBeFalse();
    }
  });

  it('shows no Better badge where the style hides it, or on the profile', () => {
    render(buildDto(comparableSet(3)), 2);
    const directions = (): number => component.exportableCards.filter(card => card.chrome.direction).length;
    expect(directions()).toBe(6);

    jasmine.clock().install();
    try {
      component.onFigureStyleChange({
        ...DEFAULT_FIGURE_STYLE,
        bar: { ...DEFAULT_FIGURE_STYLE.bar, hiddenBadges: ['direction'] },
        scatter: { ...DEFAULT_FIGURE_STYLE.scatter, hiddenBadges: ['direction'] }
      });
      jasmine.clock().tick(150);
      fixture.detectChanges();
      expect(directions()).toBe(0);
      for (const card of [...component.panelCards, ...component.scatterCards]) {
        expect(card.chrome.direction).withContext(card.id).toBeUndefined();
        const label = (fixture.debugElement.query(By.css(`canvas[data-figure-id="${card.id}"]`))
          .nativeElement as HTMLCanvasElement).getAttribute('aria-label') ?? '';
        expect(label).withContext(card.id).not.toContain('Better toward');
      }
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('draws no Better marker on any plot canvas', () => {
    render(buildDto(comparableSet(3)), 2);

    for (const card of component.exportableCards) {
      expect(card.plugins.map(plugin => plugin.id)).withContext(card.id).not.toContain('overseerDirectionMarker');
      // The All tab paints a figure onto the tile canvas carrying this id, which no rebuild changes.
      expect(fixture.debugElement.queryAll(By.css(`canvas[data-figure-id="${card.id}"]`)).length).withContext(card.id).toBe(1);
    }
    expect(component.profileCard!.chrome.direction).toBeUndefined();
  });

  // -------------------------------------------------------------------------------------------
  // The filter row
  // -------------------------------------------------------------------------------------------

  it('carries no suite control: suite scope is a selection-stage control and belongs to the picker', () => {
    render(buildDto(comparableSet(4)));

    expect(fixture.debugElement.query(By.css('#mc-suite'))).toBeNull();
    // The suite the figures describe stays on screen in the wizard header, read off the payload itself.
    expect(textOf('.dialog-subtitle')).toContain('GnollHack Player Assistance Benchmark Suite');
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

  /** The comparable set with one entry's run total withheld for `reason`. */
  function setWithoutTotal(count: number, reason: string): BenchmarkModelComparisonEntryDto[] {
    const entries = comparableSet(count);
    entries[1] = {
      ...entries[1],
      cost: { ...entries[1].cost!, totalRunCostPerRunUsd: null, totalRunCostSdUsd: null, totalRunCostUnavailableReason: reason }
    };
    return entries;
  }

  const PRE_HARNESS_15 = 'Run 2 predates per-role cost tracking (harness 15), so its final synthesis is not counted.';

  it('offers the total-run cost measure when every charted entry carries a total, and charts it', () => {
    render(buildDto(comparableSet(3)), 2);

    const option = fixture.debugElement.query(By.css('#mc-side-panel-data #mc-cost-measure-totalRun'))
      .nativeElement as HTMLInputElement;
    expect(option.type).toBe('radio');
    expect(option.disabled).toBeFalse();
    expect(option.parentElement!.textContent).not.toContain('not available');
    expect(textOf('#mc-cost-measure-hint')).toContain('what one benchmark run of this model costs');

    chooseRadio('mc-cost-measure-totalRun');

    expect(component.costMeasure).toBe('totalRun');
    expect(option.checked).toBeTrue();
  });

  it('disables the total-run cost measure with the reason in the hint when one entry has no total', () => {
    render(buildDto(setWithoutTotal(3, PRE_HARNESS_15)), 2);

    const option = fixture.debugElement.query(By.css('#mc-side-panel-data #mc-cost-measure-totalRun'))
      .nativeElement as HTMLInputElement;
    expect(option.disabled).toBeTrue();
    expect(option.parentElement!.textContent).toContain('not available');
    expect(component.totalRunCostAvailable).toBeFalse();
    expect(textOf('#mc-cost-measure-hint')).toContain(`Model 2: ${PRE_HARNESS_15}`);
  });

  it('falls back to candidate cost when a new comparison cannot supply the run total', () => {
    render(buildDto(comparableSet(3)), 2);
    chooseRadio('mc-cost-measure-totalRun');
    expect(component.costMeasure).toBe('totalRun');

    fixture.componentRef.setInput('comparison', buildDto(setWithoutTotal(3, PRE_HARNESS_15)));
    fixture.detectChanges();

    expect(component.costMeasure).toBe('candidateSuite');
    expect((fixture.debugElement.query(By.css('#mc-cost-measure-candidateSuite')).nativeElement as HTMLInputElement).checked)
      .toBeTrue();
  });

  // -------------------------------------------------------------------------------------------
  // The Data tab: models, measures, prices, model order
  // -------------------------------------------------------------------------------------------

  /** Chooses one option of a Data tab radio group through the real radio. */
  function chooseRadio(id: string): void {
    const radio = fixture.debugElement.query(By.css(`#${id}`));
    expect(radio).withContext(id).toBeTruthy();
    (radio.nativeElement as HTMLInputElement).click();
    fixture.detectChanges();
  }

  it('opens the Data tab with Models, Measures, Prices and Model order beside the charts, and Models, Prices and Model order beside the table', () => {
    render(buildDto(comparableSet(3)), 2);

    expect(component.effectiveSidebarTab).toBe('data');
    const panel = fixture.debugElement.query(By.css('#mc-side-panel-data')).nativeElement as HTMLElement;
    expect(panel.getAttribute('aria-labelledby')).toBe('mc-side-tab-data');
    expect(Array.from(panel.querySelectorAll(':scope > fieldset > legend')).map(legend => legend.textContent?.trim()))
      .toEqual(['Models', 'Measures', 'Prices', 'Model order']);
    expect(Array.from(panel.querySelectorAll('.mc-models-table thead th')).map(th => th.textContent?.trim()))
      .toEqual(['Show', 'Model', 'Highlight']);
    expect(fixture.debugElement.query(By.css('#mc-pricing-basis'))).toBeTruthy();
    expect(panel.querySelectorAll('input[type="radio"][name="mc-speed-measure"]').length).toBe(4);
    expect(panel.querySelectorAll('input[type="radio"][name="mc-cost-measure"]').length).toBe(2);
    expect(panel.querySelectorAll('input[type="radio"][name="mc-sort-key"]').length).toBe(5);
    expect(panel.querySelectorAll('input[type="radio"][name="mc-sort-direction"]').length).toBe(2);
    expect((panel.querySelector('#mc-speed-measure-meanModelTime') as HTMLInputElement).checked).toBeTrue();
    expect((panel.querySelector('#mc-sort-key-intelligenceIndex') as HTMLInputElement).checked).toBeTrue();
    // Each radio group is a borderless fieldset with its own legend, and the hints are attached.
    expect(panel.querySelectorAll('fieldset.gh-choice').length).toBe(4);
    expect(panel.querySelector('#mc-speed-measure-hint')).toBeTruthy();

    showView('table');
    expect(Array.from(panel.querySelectorAll(':scope > fieldset > legend')).map(legend => legend.textContent?.trim()))
      .toEqual(['Models', 'Prices', 'Model order']);
    expect(Array.from(panel.querySelectorAll('.mc-models-table thead th')).map(th => th.textContent?.trim()))
      .toEqual(['Show', 'Model']);
    expect(panel.querySelector('[name="mc-speed-measure"]')).toBeNull();
    expect(fixture.debugElement.query(By.css('#mc-pricing-basis'))).toBeTruthy();
    expect(panel.textContent).toContain('Prices and the order of the table\'s rows.');
  });

  it('drives the speed and cost measures and the model order from the Data tab radios, and rebuilds', () => {
    render(buildDto(comparableSet(3)), 2);
    const descending = component.figures!.smallMultiples.order;

    chooseRadio('mc-speed-measure-ttftP50');
    expect(component.speedMeasure).toBe('ttftP50');
    expect(component.figures!.smallMultiples.speed.chrome.notes.length).toBe(0);

    chooseRadio('mc-sort-direction-asc');
    expect(component.sort.direction).toBe('asc');
    expect(component.figures!.smallMultiples.order).toEqual([...descending].reverse());

    chooseRadio('mc-sort-key-label');
    expect(component.sort.key).toBe('label');

    chooseRadio('mc-cost-measure-candidateSuite');
    expect(component.costMeasure).toBe('candidateSuite');
  });

  it('says in the speed hint when Speed Index is saturated, beside the Speed measure radios', () => {
    const entries = comparableSet(2);
    entries[0] = { ...entries[0], table: { ...entries[0].table!, speedIndexSaturated: true } };
    render(buildDto(entries), 2);

    expect(textOf('#mc-side-panel-data .mc-speed-hint')).toContain('saturated for 1 of 2');
    const group = fixture.debugElement.query(By.css('#mc-side-panel-data fieldset.gh-choice'))
      .nativeElement as HTMLElement;
    expect(group.getAttribute('aria-describedby')).toBe('mc-speed-measure-hint');
  });

  // -------------------------------------------------------------------------------------------
  // The Models table
  // -------------------------------------------------------------------------------------------

  /** One row's Show checkbox, found by its entry key: the rows follow the model order, not the payload. */
  function showBox(key: string): HTMLInputElement {
    return fixture.debugElement.query(By.css(`#mc-show-${component.domKey(key)}`)).nativeElement as HTMLInputElement;
  }

  /** Flips one row's Show checkbox the way the browser does, then lets its handler write it back. */
  function clickShow(key: string): void {
    showBox(key).click();
    fixture.detectChanges();
  }

  it('renders one Show checkbox per entry, and an excluded one as a disabled box that keeps its reason', () => {
    render(buildDto([...comparableSet(2), buildExcludedEntry('run:9', ['ScoringMethodVersion'])]));

    expect(fixture.debugElement.queryAll(By.css('.mc-models-table tbody tr')).length).toBe(3);
    expect(fixture.debugElement.queryAll(By.css('.mc-models-table input[type="checkbox"][id^="mc-show-"]')).length)
      .toBe(3);
    expect([showBox('run:1'), showBox('run:2')].every(box => box.checked && !box.disabled)).toBeTrue();
    expect(showBox('run:9').disabled).toBeTrue();
    expect(showBox('run:9').checked).toBeFalse();

    // The accessible name contains the visible label, so the two never contradict each other.
    const name = component.modelRows.find(row => row.key === 'run:1')!.name;
    expect(showBox('run:1').getAttribute('aria-label')).toBe(`Show ${name} in the charts`);
    expect(textOf('.mc-models-table')).toContain('Not comparable');
    const tip = fixture.debugElement.query(By.css(`#${component.tipId('excl', 'run:9')}`));
    expect(tip?.nativeElement.textContent).toContain('ScoringMethodVersion');

    clickShow('run:2');

    expect(component.includedKeys).toEqual(['run:1']);
    expect(showBox('run:2').checked).toBeFalse();
    expect(component.figures?.selection.plotted.length).toBe(1);
  });

  it('lets more Show boxes be ticked than the figures plot, and tags a row beyond the cap as Over the limit', () => {
    render(buildDto(comparableSet(MAX_PLOTTED_ENTRIES + 1)));

    expect(fixture.debugElement.queryAll(By.css('.mc-models-table input[type="checkbox"][id^="mc-show-"]')).length)
      .toBe(MAX_PLOTTED_ENTRIES + 1);
    // A fresh payload ticks every selectable entry, which is already past the plot cap.
    expect(component.includedKeys.length).toBe(MAX_PLOTTED_ENTRIES + 1);
    expect(fixture.debugElement.queryAll(By.css('.mc-models-tag')).length).toBe(1);
    expect(textOf('.mc-models-tag')).toContain('Over the limit');

    clickShow('run:1');
    expect(component.includedKeys.length).toBe(MAX_PLOTTED_ENTRIES);
    expect(component.isIncluded('run:1')).toBeFalse();
    expect(fixture.debugElement.queryAll(By.css('.mc-models-tag')).length).toBe(0);

    // The seeded state has to stay reachable, so re-ticking at the cap is honoured.
    clickShow('run:1');
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
    component.openAbout();
    fixture.detectChanges();
    expect(textOf('.alert-heading')).toContain('Each model has only one run');

    showView('table');
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
  // Set-level caveats, in the About dialog
  // -------------------------------------------------------------------------------------------

  it('shows the thinking-level caveat and the refused measures in the About dialog', () => {
    render(buildDto(comparableSet(3), { thinkingLevelsDiffer: true }));

    component.openAbout();
    fixture.detectChanges();

    expect(textOf('.alert-heading')).toContain('The models use different thinking levels');
    expect(fixture.debugElement.query(By.css('#mc-about-measures-heading'))).toBeTruthy();
    expect(textOf('.mc-about-measure')).toContain('Cost per index point');
  });

  it('leads each refused measure with its summary, names the alternative, and keeps the reason behind a disclosure', () => {
    render(buildDto(comparableSet(3)));

    component.openAbout();
    fixture.detectChanges();

    // The summary is the visible line; the full reason is one click away rather than absent.
    expect(textOf('.mc-about-measure'))
      .toContain('Dividing cost by a noisy score gives a number with no reliable error bars');
    expect(textOf('.mc-about-measure')).toContain(
      'Instead: Candidate $ / question in the comparison table, read beside the Intelligence Index and its ± interval.');

    const detail = fixture.debugElement.query(By.css('.mc-about-measure details.gh-disclosure'))
      .nativeElement as HTMLDetailsElement;
    expect(detail.open).toBeFalse();
    expect(detail.querySelector('summary')?.textContent?.trim()).toBe('Why');
    expect(detail.textContent).toContain('A ratio of two noisy estimators');
  });

  it('shows no "Not shown as charts" section when the server sends no refused measures', () => {
    render(buildDto(comparableSet(1), { excludedMeasures: [] }));

    component.openAbout();
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('#mc-about-measures-heading'))).toBeNull();
  });

  it('summarises how many models can be charted together, in singular and plural', () => {
    render(buildDto(comparableSet(3)));
    expect(component.aboutSummary).toBe('All 3 models were measured the same way and can be charted together.');

    render(buildDto([...comparableSet(3), buildExcludedEntry('run:9', ['ScoringMethodVersion'])]));
    expect(component.aboutSummary).toBe(
      '3 of 4 models can be charted together. 1 was measured differently and is in the table only.');

    render(buildDto([
      ...comparableSet(3),
      buildExcludedEntry('run:8', ['ScoringMethodVersion']),
      buildExcludedEntry('run:9', ['CandidatePromptOptions'])
    ]));
    expect(component.aboutSummary).toBe(
      '3 of 5 models can be charted together. 2 were measured differently and are in the table only.');
  });

  it('badges the About button with the note count, and opens the dialog as a modal', async () => {
    render(buildDto(comparableSet(3)));
    expect(fixture.debugElement.query(By.css('.mc-about-count'))).toBeNull();

    render(buildDto(comparableSet(3).map(entry => ({ ...entry, runCount: 1 })), { thinkingLevelsDiffer: true }));
    expect(textOf('.mc-about-count')).toContain('2');

    const dialog = fixture.debugElement.query(By.css('dialog.mc-about-dialog')).nativeElement as HTMLDialogElement;
    const showModal = spyOn(dialog, 'showModal').and.callThrough();
    // The body renders only while the dialog is open.
    expect(fixture.debugElement.query(By.css('.mc-about-summary'))).toBeNull();

    component.openAbout();
    fixture.detectChanges();

    expect(showModal).toHaveBeenCalled();
    expect(fixture.debugElement.query(By.css('.mc-about-summary'))).toBeTruthy();

    // The dialog's close event is queued as a task, not dispatched from close() itself.
    dialog.close();
    await new Promise(resolve => setTimeout(resolve));
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.mc-about-summary'))).toBeNull();
    const trigger = fixture.debugElement.query(By.css('#mc-about-trigger')).nativeElement as HTMLButtonElement;
    expect(document.activeElement).toBe(trigger);
  });

  it('stops the About dialog\'s close and cancel events from reaching the host wizard dialog', () => {
    render(buildDto(comparableSet(3)));
    component.openAbout();
    fixture.detectChanges();

    const dialog = fixture.debugElement.query(By.css('dialog.mc-about-dialog')).nativeElement as HTMLDialogElement;
    const host = fixture.nativeElement as HTMLElement;
    const heard: string[] = [];
    host.addEventListener('close', () => heard.push('close'));
    host.addEventListener('cancel', () => heard.push('cancel'));

    // A real close event does not bubble; dispatching with bubbles: true is what proves
    // onAboutDialogClose's stopPropagation actually runs rather than merely being unreachable.
    dialog.dispatchEvent(new Event('close', { bubbles: true }));
    dialog.dispatchEvent(new Event('cancel', { bubbles: true, cancelable: true }));

    expect(heard).toEqual([]);
  });

  it('emits refresh from Recompute, refuses it while loading, and marks aria-disabled', () => {
    render(buildDto(comparableSet(3)), 2);
    const button = fixture.debugElement.query(By.css('.mc-recompute')).nativeElement as HTMLButtonElement;
    const refreshed: number[] = [];
    component.refresh.subscribe(() => refreshed.push(1));

    button.click();
    expect(refreshed.length).toBe(1);
    expect(button.getAttribute('aria-disabled')).toBeNull();

    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();
    expect(button.getAttribute('aria-disabled')).toBe('true');

    button.click();
    expect(refreshed.length).toBe(1);
  });

  it('drops a highlight when its model is unticked, and refuses to highlight an unplotted row', () => {
    render(buildDto(comparableSet(3)), 2);

    component.toggleEmphasis('run:1');
    expect(component.emphasisKeys).toEqual(['run:1']);

    component.toggleEntry('run:1');
    fixture.detectChanges();
    expect(component.emphasisKeys).toEqual([]);
    expect(component.includedKeys).not.toContain('run:1');

    // The row is still in the Models table, unplotted, and its Highlight box is disabled.
    const box = fixture.debugElement.query(By.css(`#mc-emph-${component.domKey('run:1')}`))
      .nativeElement as HTMLInputElement;
    expect(box.disabled).toBeTrue();

    component.toggleEmphasis('run:1');
    expect(component.emphasisKeys).toEqual([]);
  });

  it('turns the chart views off when unticked down to one model, and back on when re-ticked', () => {
    render(buildDto(comparableSet(3)), 2);
    expect(component.showFigures).toBeTrue();

    component.toggleEntry('run:2');
    component.toggleEntry('run:3');
    fixture.detectChanges();

    expect(component.showFigures).toBeFalse();
    expect(textOf('#mc-fig-unavailable')).toContain('Charts need at least two models. Check more under Data → Models.');
    expect(component.effectiveFigureTab).toBe('table');
    expect(fixture.debugElement.query(By.css('.mc-models-table'))).toBeTruthy();

    component.toggleEntry('run:2');
    fixture.detectChanges();

    expect(component.showFigures).toBeTrue();
  });

  it('keeps includedKeys, emphasisKeys and the table page on a same-keys refetch, and reseeds them on a new key set', () => {
    render(buildDto(comparableSet(3)), 2);
    // A small page size, so a 3-row set still has more than one page to keep.
    component.entryTable.pageSize = 1;
    component.toggleEntry('run:2');
    component.toggleEmphasis('run:1');
    fixture.detectChanges();
    component.entryTable.page = 2;

    // A refetch under the same three keys: includedKeys, emphasisKeys and the table page survive.
    fixture.componentRef.setInput('comparison', buildDto(comparableSet(3)));
    fixture.detectChanges();

    expect(component.includedKeys).not.toContain('run:2');
    expect(component.emphasisKeys).toEqual(['run:1']);
    expect(component.entryTable.page).toBe(2);

    // A payload with a different key set is a new comparison: both reseed and the page resets.
    fixture.componentRef.setInput('comparison', buildDto(comparableSet(4)));
    fixture.detectChanges();

    expect([...component.includedKeys].sort()).toEqual(['run:1', 'run:2', 'run:3', 'run:4']);
    expect(component.emphasisKeys).toEqual([]);
    expect(component.entryTable.page).toBe(1);
  });

  it('never mentions strict comparability in the set-level notes', () => {
    render(buildDto(comparableSet(3)));

    component.toggleEntry('run:2');
    fixture.detectChanges();

    expect(component.setFigureNotes.some(note => /Strict comparability/.test(note.text))).toBeFalse();
    expect(component.setNotices.some(notice => /Strict comparability/.test(notice))).toBeFalse();
  });

  it('names Speed Index saturation in the speed hint only where an entry is saturated', () => {
    const entries = comparableSet(2);
    entries[0] = { ...entries[0], table: { ...entries[0].table!, speedIndexSaturated: true } };
    render(buildDto(entries), 2);

    expect(component.speedIndexSaturatedCount).toBe(1);
    expect(textOf('.mc-speed-hint')).toContain('saturated for 1 of 2');

    render(buildDto(comparableSet(2)), 2);

    expect(component.speedIndexSaturatedCount).toBe(0);
    expect(textOf('.mc-speed-hint')).not.toContain('saturated');
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

  it('gives every figure tile Copy, Download and Open, and the set one icon-only Download all charts', () => {
    render(buildDto(comparableSet(3)), 2);

    const tiles = fixture.debugElement.queryAll(By.css('.mc-all-tile'));
    expect(tiles.length).toBe(7);
    for (const tile of tiles) {
      const title = (tile.nativeElement as HTMLElement).getAttribute('aria-label')!;
      const actions = tile.queryAll(By.css('.mc-all-tile-actions button'))
        .map(button => button.nativeElement as HTMLButtonElement);
      expect(tile.queryAll(By.css('button')).length).toBe(3);
      expect(actions.map(button => button.classList.contains('mc-all-copy') ? 'copy'
        : button.classList.contains('mc-all-download') ? 'download'
          : button.classList.contains('mc-all-open') ? 'open' : '?')).toEqual(['copy', 'download', 'open']);
      // Icon-only buttons have no text, so aria-label is each one's accessible name — and it has to
      // name the figure, or seven buttons share one name in a screen reader's control list.
      expect(actions[0].getAttribute('aria-label')).toBe(`Copy ${title} to the clipboard`);
      expect(actions[1].getAttribute('aria-label')).toBe(`Download ${title}`);
      expect(actions[2].getAttribute('aria-label')).toBe(`Open ${title} in Single view`);
      // The tile itself is the keyboard stop and opens on Enter, so Open is not a second one; Copy
      // and Download are tab stops, the only keyboard route to them here.
      expect((tile.nativeElement as HTMLElement).getAttribute('tabindex')).toBe('0');
      expect(actions[0].hasAttribute('tabindex')).toBeFalse();
      expect(actions[1].hasAttribute('tabindex')).toBeFalse();
      expect(actions[2].getAttribute('tabindex')).toBe('-1');
    }

    const step = fixture.debugElement.query(By.css('#mc-step-panel-2')).nativeElement as HTMLElement;
    const downloadAll = Array.from(step.querySelectorAll<HTMLButtonElement>('button[aria-label="Download all charts"]'));
    expect(downloadAll.length).toBe(1);
    expect(downloadAll[0].closest('#mc-fig-panel-all .mc-all-toolbar')).not.toBeNull();
    expect(downloadAll[0].closest('.mc-fig-bar')).toBeNull();
    expect(downloadAll[0].closest('#mc-fig-sidebar')).toBeNull();
    expect(downloadAll[0].classList).toContain('action-btn');
    expect(downloadAll[0].textContent?.trim()).toBe('');
    expect(step.textContent).not.toContain('Download all charts');
    // One image on the clipboard at a time, so there is deliberately no batch copy.
    expect(step.textContent).not.toContain('Copy all figures');
  });

  it('copies and downloads one figure from its tile without opening it in Single', async () => {
    render(buildDto(comparableSet(3)), 2);
    const copy = spyOn(component, 'copyFigure').and.returnValue(Promise.resolve());
    const download = spyOn(component, 'downloadFigure').and.returnValue(Promise.resolve());
    const open = spyOn(component, 'openInSingle').and.callThrough();

    const tile = fixture.debugElement.queryAll(By.css('.mc-all-tile'))[1];
    const cardId = (tile.nativeElement as HTMLElement).getAttribute('data-figure-id');
    const copyButton = tile.query(By.css('.mc-all-copy')).nativeElement as HTMLButtonElement;
    const downloadButton = tile.query(By.css('.mc-all-download')).nativeElement as HTMLButtonElement;

    copyButton.click();
    downloadButton.click();
    expect(copy).toHaveBeenCalledTimes(1);
    expect(copy.calls.mostRecent().args[0].id).toBe(cardId!);
    expect(download).toHaveBeenCalledTimes(1);
    expect(download.calls.mostRecent().args[0].id).toBe(cardId!);

    // Enter on a button is the button's own; the tile opens only on an Enter aimed at itself.
    for (const button of [copyButton, downloadButton]) {
      button.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    }
    expect(open).not.toHaveBeenCalled();
    expect(component.figureTab).toBe('all');

    // Each has an interest tooltip, and Download's reads the export settings.
    for (const button of [copyButton, downloadButton]) {
      const tipId = button.getAttribute('interestfor')!;
      expect(button.getAttribute('style') ?? '').toMatch(new RegExp(`anchor-name:\\s*--${tipId}`));
      expect((fixture.nativeElement.querySelector(`#${tipId}`) as HTMLElement).getAttribute('popover')).toBe('hint');
    }
    expect(textOf(`#${downloadButton.getAttribute('interestfor')}`)).toBe(`Download this chart — ${component.exportSummary}`);
  });

  it('offers a WebP quality for the figures only while WebP is the chosen format', async () => {
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('download');
    expect(component.figureTab).withContext('no Single view is needed to reach the export settings')
      .toBe('all');

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
    expect(component.webpQuality).toBe(85);
    // A placeholder is not a label, and this control carries no visible one.
    expect(fixture.debugElement.query(By.css('label[for="mc-export-webp-quality"]'))).toBeTruthy();
  });

  it('opens step 2 on the Interactive table where nothing can be charted, and refuses the chart tabs', () => {
    const expectTableOnly = (): void => {
      expect(component.effectiveFigureTab).toBe('table');
      expect(fixture.debugElement.query(By.css('#mc-fig-panel-table table.mc-table'))).toBeTruthy();
      expect(fixture.debugElement.query(By.css('.mc-all-tile'))).toBeNull();
      expect(fixture.debugElement.queryAll(By.css('canvas')).length).toBe(0);
      const line = fixture.debugElement.query(By.css('#mc-fig-unavailable')).nativeElement as HTMLElement;
      expect(line.textContent).toContain(
        'Fewer than two models were measured the same way, so there is nothing to chart. The table lists every model and why.');
      for (const view of ['all', 'single']) {
        const tab = fixture.debugElement.query(By.css(`#mc-fig-tab-${view}`)).nativeElement as HTMLButtonElement;
        // aria-disabled, never disabled: the tab stays focusable and names the reason.
        expect(tab.getAttribute('aria-disabled')).withContext(view).toBe('true');
        expect(tab.hasAttribute('disabled')).withContext(view).toBeFalse();
        expect(tab.getAttribute('aria-describedby')).withContext(view).toBe('mc-fig-unavailable');
      }
      // The table views keep working.
      expect(fixture.debugElement.query(By.css('#mc-fig-tab-table')).nativeElement.getAttribute('aria-disabled')).toBeNull();
    };

    render(buildDto(comparableSet(1)), 2);
    expect(component.shape).toBe('single');
    expect(component.step).toBe(2);
    expectTableOnly();
    // A refused chart tab does nothing.
    showView('all');
    expect(component.effectiveFigureTab).toBe('table');

    render(buildDto([
      buildExcludedEntry('run:8', ['ScoringMethodVersion']),
      buildExcludedEntry('run:9', ['CandidatePromptOptions'])
    ]), 2);
    expect(component.shape).toBe('none');
    expectTableOnly();

    showView('tablePreview');
    expect(component.effectiveFigureTab).toBe('tablePreview');
    expect(fixture.debugElement.query(By.css('#mc-fig-panel-tablePreview canvas.mc-preview-canvas'))).toBeTruthy();
  });

  it('renders no workspace without a comparison: step 2 refuses to open', () => {
    render(null);
    expect(component.shape).toBe('empty');
    expect(component.isStepReachable(2)).toBeFalse();
    for (const selector of ['.mc-fig-workspace', '#mc-fig-sidebar', '.mc-fig-bar', '.mc-all-tile']) {
      expect(fixture.debugElement.query(By.css(selector))).withContext(selector).toBeNull();
    }
    expect(component.canExport).toBeFalse();
  });

  it('opens the table when a refetch leaves a chart view over an unchartable set, and returns to it after', () => {
    render(buildDto(comparableSet(3)), 2);
    expect(component.effectiveFigureTab).toBe('all');

    fixture.componentRef.setInput('comparison', buildDto(comparableSet(1)));
    fixture.detectChanges();

    expect(component.step).toBe(2);
    expect(component.figureTab).withContext('the chosen view is kept').toBe('all');
    expect(component.effectiveFigureTab).toBe('table');
    expect(fixture.debugElement.query(By.css('.mc-all-tile'))).toBeNull();
    expect(textOf('#mc-fig-unavailable')).toContain('Fewer than two models were measured the same way');

    fixture.componentRef.setInput('comparison', buildDto(comparableSet(3)));
    fixture.detectChanges();
    expect(component.effectiveFigureTab).toBe('all');
    expect(fixture.debugElement.query(By.css('#mc-fig-unavailable'))).toBeNull();
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
  // Highlight
  // -------------------------------------------------------------------------------------------

  /** The Clear highlights control, found by its label rather than by its position in the fieldset. */
  function clearHighlightsButton(): HTMLButtonElement {
    return fixture.debugElement.queryAll(By.css('.mc-models-actions button'))
      .map(button => button.nativeElement as HTMLButtonElement)
      .find(button => (button.textContent ?? '').trim() === 'Clear highlights')!;
  }

  it('chooses a highlight in the Models table, one checkbox per row, disabled on an unplotted one', () => {
    render(buildDto([...comparableSet(3), buildExcludedEntry('run:9', ['ScoringMethodVersion'])]), 2);

    expect(component.sidebarTab).toBe('data');
    const boxes = fixture.debugElement.queryAll(By.css('.mc-models-table input[id^="mc-emph-"]'))
      .map(box => box.nativeElement as HTMLInputElement);
    // One Highlight box per row, including the excluded one, but only the plotted rows' boxes work.
    expect(boxes.length).toBe(4);
    expect(boxes.filter(box => !box.disabled).length).toBe(3);
    const excludedBox = fixture.debugElement.query(By.css(`#mc-emph-${component.domKey('run:9')}`))
      .nativeElement as HTMLInputElement;
    expect(excludedBox.disabled).toBeTrue();
    // The state is in words as well as in the box, and never in the gold alone.
    expect(textOf('.mc-models-status')).toContain('3 of 3 shown');
    expect(textOf('.mc-models-status')).toContain('0 highlighted');
    expect(clearHighlightsButton().disabled).toBeTrue();

    fixture.debugElement.query(By.css(`#mc-emph-${component.domKey('run:1')}`))
      .triggerEventHandler('change', { target: {} });
    fixture.detectChanges();

    expect(component.emphasisKeys.length).toBe(1);
    expect(textOf('.mc-models-status')).toContain('1 highlighted');
    expect(clearHighlightsButton().disabled).toBeFalse();

    clearHighlightsButton().click();
    fixture.detectChanges();

    expect(component.emphasisKeys).toEqual([]);
    expect(textOf('.mc-models-status')).toContain('0 highlighted');
    expect(clearHighlightsButton().disabled).toBeTrue();
  });

  it('carries no highlight control in the table cell, where its effect cannot be seen', () => {
    renderTable(buildDto(comparableSet(2)));

    const cell = fixture.debugElement.query(By.css('table.mc-table tbody tr .col-name'))
      .nativeElement as HTMLElement;
    expect(cell.querySelector('button')).toBeNull();
    expect(cell.querySelector('.mc-model-name')?.textContent?.trim()).toBe('Gemini 2.5 Flash');
    expect(cell.querySelector('input[type="checkbox"]')).toBeNull();
  });

  it('labels each trade-off checkbox in the Charts tab, and offers neither in the All panel', () => {
    render(buildDto(comparableSet(3)), 2);

    const names = scatterToggle(0).nativeElement as HTMLInputElement;
    expect(names.closest('label')?.textContent).toContain('Label models inside the chart');
    const values = scatterToggle(1).nativeElement as HTMLInputElement;
    expect(values.closest('label')?.textContent).toContain('Show values in the chart');

    const all = fixture.debugElement.query(By.css('#mc-fig-panel-all')).nativeElement as HTMLElement;
    expect(all.querySelectorAll('input[type="checkbox"]').length).toBe(0);
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

  it('states the run count in the band label when only runs are selected', () => {
    band({ runs: 3, sources: runSources(3) });

    const label = textOf('.mc-wizard-notice-label');
    expect(label).toContain('3 runs selected');
  });

  it('states the group count in the band label when only groups are selected', () => {
    band({ groups: 2, sources: groupSources(2) });

    const label = textOf('.mc-wizard-notice-label');
    expect(label).toContain('2 groups selected');
  });

  it('states both counts when the selection spans runs and groups', () => {
    band({ runs: 2, groups: 1, sources: [...runSources(2), ...groupSources(1)] });

    const label = textOf('.mc-wizard-notice-label');
    expect(label).toContain('2 runs and 1 group selected');
  });

  it('pluralises the selection summary across all four forms', () => {
    fixture.componentRef.setInput('selectedRunCount', 0);
    fixture.componentRef.setInput('selectedGroupCount', 0);
    expect(component.selectionSummary).toBe('Nothing selected yet');

    fixture.componentRef.setInput('selectedRunCount', 1);
    expect(component.selectionSummary).toBe('1 run selected');

    fixture.componentRef.setInput('selectedRunCount', 2);
    expect(component.selectionSummary).toBe('2 runs selected');

    fixture.componentRef.setInput('selectedRunCount', 0);
    fixture.componentRef.setInput('selectedGroupCount', 1);
    expect(component.selectionSummary).toBe('1 group selected');

    fixture.componentRef.setInput('selectedGroupCount', 3);
    expect(component.selectionSummary).toBe('3 groups selected');

    fixture.componentRef.setInput('selectedRunCount', 1);
    expect(component.selectionSummary).toBe('1 run and 3 groups selected');

    fixture.componentRef.setInput('selectedGroupCount', 2);
    expect(component.selectionSummary).toBe('1 run and 2 groups selected');

    fixture.componentRef.setInput('selectedRunCount', 2);
    fixture.componentRef.setInput('selectedGroupCount', 1);
    expect(component.selectionSummary).toBe('2 runs and 1 group selected');
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
    expect(textOf('.mc-wizard-selection-hint')).toContain('Select runs or groups above');
    expect(fixture.debugElement.query(By.css('.mc-selection-chips'))).toBeNull();
  });

  it('offers Clear selection only when something is selected, and emits clearSelection', () => {
    band({ notices: [] });
    expect(fixture.debugElement.query(By.css('.mc-selection-clear'))).toBeNull();

    band({ runs: 2, sources: runSources(2), notices: [] });

    const cleared: void[] = [];
    component.clearSelection.subscribe(() => cleared.push(undefined));

    const button = fixture.debugElement.query(By.css('.mc-selection-clear'))
      .nativeElement as HTMLButtonElement;
    expect(button.textContent?.trim()).toBe('Clear selection');
    button.click();

    expect(cleared.length).toBe(1);
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
    expect(alerts[0].textContent).toContain('Select at least one completed run or analysis group');
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

    const next = nextButton();
    expect(next.textContent).toContain('Comparing…');
    expect(next.querySelector('.gh-spinner-small')).toBeTruthy();
    expect(next.getAttribute('aria-busy')).toBe('true');
    // aria-disabled, never disabled: the reason has to stay reachable by keyboard.
    expect(next.getAttribute('aria-disabled')).toBe('true');
    expect(next.hasAttribute('disabled')).toBeFalse();

    // The busy state is in the footer, where the click was; nothing is inserted above the tabs.
    expect(fixture.debugElement.query(By.css('.mc-wizard-loading'))).toBeNull();
    expect(textOf('#mc-next-blocked')).toContain('pricing every entry server-side');
    const root = fixture.nativeElement as HTMLElement;
    const header = root.querySelector('.mc-wizard-header')!;
    const tabs = root.querySelector('.mc-wizard-steps')!;
    const statusAboveTabs = Array.from(root.querySelectorAll('[role="status"]')).filter(status =>
      (header.compareDocumentPosition(status) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0 &&
      (tabs.compareDocumentPosition(status) & Node.DOCUMENT_POSITION_PRECEDING) !== 0);
    expect(statusAboveTabs).toEqual([]);

    // The step-1 panel says its result is pending; the picker's own controls stay live.
    const panel = fixture.debugElement.query(By.css('#mc-step-panel-1')).nativeElement as HTMLElement;
    expect(panel.getAttribute('aria-busy')).toBe('true');

    fixture.componentRef.setInput('loading', false);
    fixture.detectChanges();
    expect(component.comparing).toBeFalse();
    expect(component.nextLabel).toBe('Compare');
    expect(next.hasAttribute('aria-busy')).toBeFalse();
  });

  it('labels the footer Close rather than Comparing while step 2 refetches', () => {
    render(buildDto(comparableSet(3)), 2);
    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();

    // A Prices or Recompute refetch loads too, and Close on step 2 is not blocked by it.
    expect(component.comparing).toBeFalse();
    expect(component.nextLabel).toBe('Close');
    expect(nextButton().querySelector('.gh-spinner-small')).toBeNull();
  });

  describe('while a comparison is loading', () => {
    afterEach(() => {
      jasmine.clock().uninstall();
    });

    /** Step 1 with a valid selection, Compare pressed and the request in flight. */
    function comparingOnStep1(): void {
      render(null, 1);
      fixture.componentRef.setInput('selectedRunCount', 2);
      fixture.componentRef.setInput('loading', true);
      fixture.detectChanges();
    }

    it('offers Cancel Comparison only while step 1 is comparing, and emits cancelCompare once', () => {
      render(null, 1);
      fixture.componentRef.setInput('selectedRunCount', 2);
      fixture.detectChanges();
      expect(cancelCompareButton()).toBeNull();

      fixture.componentRef.setInput('loading', true);
      fixture.detectChanges();
      const cancel = cancelCompareButton();
      expect(cancel).toBeTruthy();
      // A dismissal: the cancel variant, and no icon.
      expect(cancel!.querySelector('svg')).toBeNull();

      const cancelled: number[] = [];
      component.cancelCompare.subscribe(() => cancelled.push(1));
      cancel!.click();
      expect(cancelled.length).toBe(1);
    });

    it('offers no Cancel Comparison on a step-2 refetch', () => {
      render(buildDto(comparableSet(3)), 2);
      fixture.componentRef.setInput('loading', true);
      fixture.detectChanges();

      expect(cancelCompareButton()).toBeNull();
    });

    it('moves focus to Next when Cancel Comparison is pressed and removed', () => {
      comparingOnStep1();
      // As the host does: it drops loading and checks the view before the emit returns.
      component.cancelCompare.subscribe(() => {
        fixture.componentRef.setInput('loading', false);
        fixture.detectChanges();
      });

      const cancel = cancelCompareButton()!;
      cancel.focus();
      cancel.click();
      fixture.detectChanges();

      expect(cancelCompareButton()).toBeNull();
      expect(document.activeElement).toBe(nextButton());
    });

    it('moves focus to Next when the result lands while Cancel Comparison has focus', () => {
      comparingOnStep1();
      cancelCompareButton()!.focus();
      expect(document.activeElement).toBe(cancelCompareButton());

      fixture.componentRef.setInput('loading', false);
      fixture.detectChanges();

      expect(cancelCompareButton()).toBeNull();
      expect(document.activeElement).toBe(nextButton());
    });

    it('says after 15 seconds that the comparison is slow, and how to leave it', () => {
      jasmine.clock().install();
      comparingOnStep1();

      jasmine.clock().tick(14_999);
      fixture.detectChanges();
      expect(component.slowLoading).toBeFalse();
      expect(textOf('#mc-next-blocked')).not.toContain('longer than usual');

      jasmine.clock().tick(1);
      fixture.detectChanges();
      expect(component.slowLoading).toBeTrue();
      expect(textOf('#mc-next-blocked')).toContain('longer than usual');
      expect(textOf('#mc-next-blocked')).toContain('close the wizard');

      fixture.componentRef.setInput('loading', false);
      fixture.detectChanges();
      expect(component.slowLoading).toBeFalse();
    });

    it('shows a spinner-bearing refetch line on step 2, where Next carries no spinner', () => {
      render(buildDto(comparableSet(3)), 2);
      fixture.componentRef.setInput('loading', true);
      fixture.detectChanges();

      const busy = fixture.debugElement.query(By.css('.mc-wizard-position .mc-wizard-busy'))
        .nativeElement as HTMLElement;
      expect(busy.querySelector('.gh-spinner-small')).toBeTruthy();
      expect(busy.textContent).toContain('Recomputing the comparison');
      expect(nextButton().querySelector('.gh-spinner-small')).toBeNull();

      fixture.componentRef.setInput('loading', false);
      fixture.detectChanges();
      expect(fixture.debugElement.query(By.css('.mc-wizard-busy'))).toBeNull();
    });

    it('keeps the header close button enabled and emitting while step 1 is comparing', () => {
      comparingOnStep1();

      const close = fixture.debugElement.query(By.css('[aria-label="Close cross-model comparison"]'))
        .nativeElement as HTMLButtonElement;
      expect(close.disabled).toBeFalse();

      const closed: number[] = [];
      component.closeRequested.subscribe(() => closed.push(1));
      close.click();
      expect(closed.length).toBe(1);
    });

    it('puts no blocking layer over the wizard, on step 1 or on a step-2 refetch', () => {
      const expectNoBlockingLayer = (where: string): void => {
        const root = fixture.nativeElement as HTMLElement;
        expect(root.querySelectorAll('[inert]').length).withContext(`${where}: inert`).toBe(0);
        expect(root.querySelectorAll('[class*="overlay"], [class*="scrim"], [class*="backdrop"]').length)
          .withContext(`${where}: overlay`).toBe(0);
      };

      comparingOnStep1();
      expectNoBlockingLayer('step 1');

      render(buildDto(comparableSet(3)), 2);
      fixture.componentRef.setInput('loading', true);
      fixture.detectChanges();
      expectNoBlockingLayer('step 2');
    });
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

    // A pricing-basis refetch replaces one payload with another; it must not move the reader.
    fixture.componentRef.setInput('comparison', buildDto(comparableSet(3)));
    fixture.detectChanges();
    expect(component.step).toBe(2);

    fixture.componentRef.setInput('comparison', null);
    fixture.detectChanges();
    expect(component.step).toBe(1);
  });

  it('has two steps: Sources, Charts & table', () => {
    render(buildDto(comparableSet(3)));

    expect(component.steps).toEqual([1, 2]);
    const tabs = fixture.debugElement.queryAll(By.css('.mc-wizard-steps .gh-tab'))
      .map(tab => (tab.nativeElement as HTMLElement).textContent?.trim());
    expect(tabs).toEqual(['1. Sources', '2. Charts & table']);
    expect(textOf('.mc-wizard-position')).toContain('Step 2 of 2 — Charts & table');
  });

  it('opens step 2 over a set no chart can draw, with Next unblocked', () => {
    render(buildDto([
      buildExcludedEntry('run:8', ['ScoringMethodVersion']),
      buildExcludedEntry('run:9', ['CandidatePromptOptions'])
    ]), 2);

    // The table is the artefact that says what could not be compared, so its step opens here.
    expect(component.step).toBe(2);
    expect(component.isStepReachable(2)).toBeTrue();
    expect(component.canGoNext).toBeTrue();
    expect(component.nextBlockedReason).toBe('');
    expect(nextButton().getAttribute('aria-disabled')).toBe('false');

    const chartsTab = fixture.debugElement
      .queryAll(By.css('.mc-wizard-steps .gh-tab'))[1].nativeElement as HTMLElement;
    expect(chartsTab.getAttribute('aria-disabled')).toBe('false');
  });

  it('reaches step 2 whenever a comparison exists, and not without one', () => {
    render(null);

    expect(component.isStepReachable(2)).toBeFalse();

    render(buildDto(comparableSet(3)));
    expect(component.isStepReachable(2)).toBeTrue();
    // Step 2 gates on nothing but the payload: the table behind it always has rows.
    expect(component.canGoNext).toBeTrue();
    expect(component.nextBlockedReason).toBe('');
  });

  it('labels step 2 Next as Close and emits closeRequested from it', () => {
    render(buildDto(comparableSet(3)), 2);
    expect(component.step).toBe(2);
    expect(component.nextLabel).toBe('Close');
    expect(textOf('.mc-wizard-position')).toContain('Step 2 of 2 — Charts & table');

    const closed: number[] = [];
    component.closeRequested.subscribe(() => closed.push(1));
    component.nextStep();

    expect(closed.length).toBe(1);
  });

  it('drives the step tablist with a roving tabindex and the arrow keys', () => {
    render(buildDto(comparableSet(3)));

    const tabs = fixture.debugElement.queryAll(By.css('.mc-wizard-steps .gh-tab'))
      .map(tab => tab.nativeElement as HTMLElement);
    expect(tabs.length).toBe(2);
    expect(tabs[1].getAttribute('aria-selected')).toBe('true');
    expect(tabs[1].getAttribute('tabindex')).toBe('0');
    expect(tabs[0].getAttribute('tabindex')).toBe('-1');

    component.onStepKeydown(new KeyboardEvent('keydown', { key: 'ArrowLeft' }), 1);
    fixture.detectChanges();
    expect(component.step).toBe(1);

    component.onStepKeydown(new KeyboardEvent('keydown', { key: 'End' }), 0);
    fixture.detectChanges();
    expect(component.step).toBe(2);
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
    render(buildDto(comparableSet(3)), 2);

    expect(() => component.applyContainerWidth(0)).not.toThrow();
    expect(component.orientation).toBe('vertical');
  });

  it('disables its own close controls while an export is running, and nothing else', () => {
    render(buildDto(comparableSet(3)), 2);
    component.exporting = true;
    refresh();

    const close = fixture.debugElement.query(By.css('.mc-wizard-header .btn-icon-action'))
      .nativeElement as HTMLButtonElement;
    const next = nextButton();
    expect(close.disabled).toBeTrue();
    expect(next.disabled).toBeTrue();
    // The wizard's two are the only close controls: no preview dialog carries a third. The About
    // dialog's own close stays live, since closing it leaves the export alone.
    const wizardCloses = fixture.debugElement.queryAll(By.css('.btn-icon-action'))
      .filter(button => !(button.nativeElement as HTMLElement).closest('.mc-about-dialog'));
    expect(wizardCloses.length).toBe(1);
    expect(fixture.debugElement.query(By.css('dialog.mc-preview-dialog'))).toBeNull();

    component.exporting = false;
    refresh();
    expect(close.disabled).toBeFalse();
    expect(next.disabled).toBeFalse();
  });

  // -------------------------------------------------------------------------------------------
  // The chart size in the sidebar's Download tab, and the Single tab that shows its effect
  // -------------------------------------------------------------------------------------------

  /** The Single tab's button in the figure bar. */
  function singleTabButton(): HTMLButtonElement {
    return fixture.debugElement.query(By.css('#mc-fig-tab-single')).nativeElement as HTMLButtonElement;
  }

  /**
   * Shows the Single tab: on `card` through the eye button on its All tile, or on the last figure
   * activated through the tab itself.
   */
  function openSingle(card?: ComparisonFigureCard): void {
    if (card) {
      const eye = fixture.debugElement.queryAll(By.css('.mc-all-tile .mc-all-open'))
        .map(button => button.nativeElement as HTMLButtonElement)
        .find(button => button.getAttribute('aria-label') === `Open ${card.title} in Single view`);
      expect(eye).withContext(`the eye button of ${card.title}`).toBeTruthy();
      eye!.click();
    } else {
      singleTabButton().click();
    }
    refresh();
  }

  /** The composition the debounce would run, without waiting 150 ms for the timer to fire it. */
  async function composePreview(): Promise<void> {
    await (component as unknown as { renderPreview(): Promise<void> }).renderPreview();
    refresh();
  }

  // A shown preview leaves a debounced composition behind it, and a timer that outlived its test
  // would compose against the next one's fixture.
  afterEach(() => {
    (component as unknown as { detachPreview(): void }).detachPreview();
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

  /** The Download tab's Chart size section: its read-out, its written size and its errors. */
  function chartSizeText(): string {
    return textOf('#mc-export-section');
  }

  it('shows the two custom size inputs only for Custom, and names what will be written', () => {
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('download');
    // The control opens on the test machine's own display, which the pixel counts below are not
    // about; every one of them is the composition at 100 %.
    component.onExportDensityChange(1);
    refresh();
    expect(fixture.debugElement.query(By.css('#mc-export-width'))).toBeNull();
    // Full HD is where the size opens.
    expect(chartSizeText()).toContain('1920 × 1080 px');
    expect(chartSizeText()).toContain('2× density');
    expect(component.exportAspectLabel).toBe('16:9');

    // A 21:9 size is laid out wider rather than shorter, at the same type size and density.
    component.onExportResolutionChange('uw1080');
    refresh();
    expect(chartSizeText()).toContain('1280 × 540');
    expect(chartSizeText()).toContain('2× density');

    component.onExportResolutionChange('custom');
    refresh();
    expect(fixture.debugElement.query(By.css('#mc-export-width'))).toBeTruthy();
    expect(fixture.debugElement.query(By.css('#mc-export-height'))).toBeTruthy();
  });

  it('refuses an out-of-range custom size in words, and will not export under one', () => {
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('download');
    component.onExportDensityChange(1);
    component.onExportResolutionChange('custom');
    component.onCustomHeightChange(10);
    refresh();

    expect(component.customResolutionError).toContain('height');
    expect(component.customResolutionError).toContain(`${FIGURE_EXPORT_MAX_DIMENSION}`);
    expect(component.canExport).toBeFalse();
    expect(chartSizeText()).toContain(component.customResolutionError);
    // The All tab has no tile to size under it, and says why instead.
    expect(fixture.debugElement.query(By.css('.mc-all-tile'))).toBeNull();
    expect(textOf('.mc-all-viewport .mc-export-error')).toContain('height');

    component.onCustomHeightChange(1080);
    refresh();
    expect(component.customResolutionError).toBe('');
    expect(component.canExport).toBeTrue();
  });

  it('opens the density on the reader’s own display, and says which option that is', () => {
    withDisplayDensity(2);
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('download');

    expect(component.figureSize.densitySelection).toBe(2);
    expect(component.exportDensity).toBe(2);
    expect(component.isCustomDensity).toBeFalse();
    expect(textOf('#mc-export-density')).toContain('200% (this display)');
    // Only the reader's own step is marked, or the note would name nothing.
    expect(textOf('#mc-export-density')).not.toContain('100% (this display)');
  });

  it('holds a display density no step matches in the custom field, prefilled', () => {
    // 110 % browser zoom on a 200 % display.
    withDisplayDensity(2.2);
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('download');

    expect(component.figureSize.densitySelection).toBe('custom');
    expect(component.figureSize.customDensityPercent).toBe(220);
    expect(component.exportDensity).toBe(2.2);
    expect(fixture.debugElement.query(By.css('#mc-export-density-custom'))).toBeTruthy();
    expect(component.exportSizeError).toBe('');
  });

  it('offers every Windows display scaling step, and Custom below them', () => {
    withDisplayDensity(1);
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('download');

    const options = fixture.debugElement
      .queryAll(By.css('#mc-export-density option'))
      .map(option => ((option.nativeElement as HTMLOptionElement).textContent ?? '').trim());
    expect(options).toEqual([
      '100% (this display)', '125%', '150%', '175%', '200%', '250%', '300%', '350%', '400%',
      'Custom…'
    ]);
  });

  it('multiplies the written size by the density in the read-out and the Download all charts summary', () => {
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('download');
    component.onExportResolutionChange('fullhd');

    component.onExportDensityChange(2);
    refresh();
    const doubled = chartSizeText();
    expect(doubled).toContain('3840 × 2160 px');
    expect(doubled).toContain('1920 × 1080 at 200%');
    expect(doubled).toContain('4× density');
    expect(textOf('#mc-tip-download-all')).toContain('200%');

    // At 100 % the requested size and the written one are one number, printed once.
    component.onExportDensityChange(1);
    refresh();
    const plain = component.exportDimensionsLabel;
    expect(plain).toContain('1920 × 1080 px at 100%');
    expect(plain).toContain('2× density');
    expect(plain).not.toContain('(1920 × 1080 at');
  });

  it('opens every figure at Full HD, the display’s own density and 100 % text, and offers no On-screen size', () => {
    withDisplayDensity(1.5);
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('download');

    expect(component.figureSize).toEqual(defaultFigureSize(1.5));
    expect(component.exportResolution.id).toBe('fullhd');
    expect(component.exportDensity).toBe(1.5);
    expect(component.exportTextScale).toBe(1);
    const readout = component.exportDimensionsLabel;
    expect(readout).toContain('2880 × 1620 px (1920 × 1080 at 150%)');
    expect(readout).not.toContain('on-screen');

    const sizes = Array.from((fixture.debugElement.query(By.css('#mc-export-resolution'))
      .nativeElement as HTMLSelectElement).options).map(option => option.value);
    expect(sizes).not.toContain('onscreen');
    expect(sizes).not.toContain('fit');
    expect(sizes).toContain('custom');
    // Every size composes at its own box, so the text size is never disabled.
    expect(styleControl('mc-export-text-scale').disabled).toBeFalse();
  });

  it('reads a stored On-screen size as Full HD, and keeps every other stored field', () => {
    localStorage.setItem(FIGURE_SIZE_STORAGE_KEY, JSON.stringify({
      version: 1, ...defaultFigureSize(1), resolutionId: 'onscreen', densitySelection: 3, textScalePercent: 150
    }));
    const stored = TestBed.createComponent(ModelComparisonComponent);
    expect(stored.componentInstance.figureSize.resolutionId).toBe('fullhd');
    expect(stored.componentInstance.exportDensity).toBe(3);
    expect(stored.componentInstance.figureSize.textScalePercent).toBe(150);
    stored.destroy();
  });

  it('refuses a custom density outside its bounds, and will not export under one', () => {
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('download');
    component.onExportDensityChange('custom');
    component.onCustomDensityChange(900);
    refresh();

    expect(component.customDensityError).toContain(`${FIGURE_EXPORT_MAX_DENSITY_PERCENT}`);
    expect(component.exportSizeError).toBe(component.customDensityError);
    expect(component.canExport).toBeFalse();
    expect(chartSizeText()).toContain('800');

    component.onCustomDensityChange(150);
    refresh();
    expect(component.customDensityError).toBe('');
    expect(component.exportDensity).toBe(1.5);
    expect(component.canExport).toBeTrue();
  });

  it('puts the Single chart\'s Copy and Download on the zoom line as icon buttons with tooltips', () => {
    render(buildDto(comparableSet(3)), 2);
    openSingle();

    const group = fixture.debugElement.query(By.css('.mc-preview-export')).nativeElement as HTMLElement;
    const [copy, download] = Array.from(group.querySelectorAll<HTMLButtonElement>('button'));
    expect(download.classList).toContain('mc-preview-download');
    expect(copy.classList).toContain('action-btn');
    expect(download.classList).toContain('action-btn');
    expect(download.classList).not.toContain('btn-gh');
    expect(download.textContent?.trim()).toBe('');
    expect(download.getAttribute('aria-label')).toBe(`Download ${component.previewCard!.title}`);
    expect(download.hasAttribute('title')).toBeFalse();
    const tipId = download.getAttribute('interestfor')!;
    expect(tipId).toBe('mc-tip-fig-download');
    expect(download.getAttribute('style') ?? '').toMatch(/anchor-name:\s*--mc-tip-fig-download/);
    const tip = fixture.nativeElement.querySelector('#mc-tip-fig-download') as HTMLElement;
    expect(tip.getAttribute('popover')).toBe('hint');
    expect(tip.textContent?.trim()).toBe(`Download this chart — ${component.exportSummary}`);

    const zoom = (fixture.debugElement.query(By.css('.mc-preview-zoom')).nativeElement as HTMLElement).getBoundingClientRect();
    const box = download.getBoundingClientRect();
    expect(Math.abs((box.top + box.height / 2) - (zoom.top + zoom.height / 2))).toBeLessThanOrEqual(1);
  });

  it('refuses a bitmap the browser could not allocate, and marks every export control unavailable', () => {
    render(buildDto(comparableSet(3)), 2);
    openSingle();
    component.onExportResolutionChange('custom');
    component.onCustomWidthChange(8000);
    component.onCustomHeightChange(8000);
    component.onExportDensityChange(3);
    refresh();

    expect(component.exportSizeError).toContain('24000 × 24000');
    expect(component.exportSizeError).toContain('16384');
    expect(component.canExport).toBeFalse();
    // Copy and Download: aria-disabled, so each stays focusable and its reason reachable.
    const controls = fixture.debugElement.queryAll(By.css('.mc-preview-export button'))
      .map(button => button.nativeElement as HTMLButtonElement);
    expect(controls.length).toBe(2);
    expect(controls.every(button => button.getAttribute('aria-disabled') === 'true')).toBeTrue();
    expect(controls.every(button => !button.disabled)).toBeTrue();
    expect(component.downloadAllTooltip).toBe(component.exportSizeError);
    expect(component.downloadFigureTooltip).toBe(component.exportSizeError);

    // The All view's Download all charts and each tile's Copy and Download refuse the same way.
    showView('all');
    const allControls = fixture.debugElement
      .queryAll(By.css('.mc-all-download-all, .mc-all-tile .mc-all-copy, .mc-all-tile .mc-all-download'))
      .map(button => button.nativeElement as HTMLButtonElement);
    // The size error replaces the tiles, so only Download all charts is left to refuse.
    expect(allControls.length).toBeGreaterThanOrEqual(1);
    expect(allControls.every(button => button.getAttribute('aria-disabled') === 'true')).toBeTrue();
    expect(allControls.every(button => !button.disabled)).toBeTrue();

    component.onExportDensityChange(2);
    refresh();
    expect(component.exportSizeError).toBe('');
    expect(component.canExport).toBeTrue();
    const enabled = fixture.debugElement
      .queryAll(By.css('.mc-all-download-all, .mc-all-tile .mc-all-copy, .mc-all-tile .mc-all-download'))
      .map(button => button.nativeElement as HTMLButtonElement);
    expect(enabled.length).toBe(1 + 7 * 2);
    expect(enabled.every(button => button.getAttribute('aria-disabled') === null)).toBeTrue();
  });

  it('copies the figure composed at the figure size, as a PNG, without a chart on the page', async () => {
    render(buildDto(comparableSet(3)), 2);
    const written: ClipboardItem[] = [];
    withClipboard({ write: (items: ClipboardItem[]) => { written.push(...items); return Promise.resolve(); } });
    const card = component.panelCards[0];
    expect(fixture.debugElement.queryAll(By.directive(BaseChartDirective)).length).toBe(0);

    component.onExportDensityChange(1);
    component.onExportResolutionChange('hd');
    await component.copyFigure(card);

    expect(component.exportStatus).toBe(`Copied ${card.title} to the clipboard.`);
    expect(written.length).toBe(1);
    expect(written[0].types).toEqual(['image/png']);
    const bitmap = await createImageBitmap(await written[0].getType('image/png'));
    expect([bitmap.width, bitmap.height]).toEqual([1280, 720]);
    bitmap.close();
  });

  it('opens the Single tab on one tile and steps through the set, wrapping at both ends', () => {
    render(buildDto(comparableSet(3)), 2);
    const cards = component.exportableCards;
    expect(cards.length).toBe(7);

    openSingle(cards[2]);
    expect(component.figureTab).toBe('single');
    expect(component.previewCardId).toBe(cards[2].id);
    expect(component.previewActive).toBeTrue();
    expect(singleTabButton().getAttribute('aria-selected')).toBe('true');
    // A screen reader lands on "Figure, <title>".
    expect(document.activeElement?.id).toBe('mc-preview-figure');

    component.previewNext();
    expect(component.previewCardId).toBe(cards[3].id);

    component.selectPreviewCard(cards[0].id);
    component.previewPrevious();
    expect(component.previewCardId).toBe(cards[cards.length - 1].id);

    component.previewNext();
    expect(component.previewCardId).toBe(cards[0].id);

    // The tab itself shows the last figure shown or activated, or the first where none was.
    component.selectFigureTab('all');
    refresh();
    openSingle();
    expect(component.previewCardId).toBe(cards[0].id);
  });

  /** Shows the Single tab on a card and the sidebar's Charts tab, both through the real tabs. */
  function openStyleTab(card?: ComparisonFigureCard): void {
    openSingle(card);
    openSidebarTab('charts');
  }

  function styleControl(id: string): HTMLInputElement {
    const element = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(`#${id}`);
    expect(element).withContext(id).not.toBeNull();
    return element!;
  }

  function setChecked(input: HTMLInputElement, on: boolean): void {
    input.checked = on;
    input.dispatchEvent(new Event('change'));
    refresh();
  }

  function setRange(input: HTMLInputElement, value: number): void {
    input.value = String(value);
    input.dispatchEvent(new Event('input'));
    refresh();
  }

  /**
   * The notes one figure draws — on its All tile and in its file alike, since both are one
   * composition — found by its place in a family, as one string.
   */
  function drawnNotes(family: 'panels' | 'scatters', index: number): string {
    const card = family === 'panels' ? component.panelCards[index] : component.scatterCards[index];
    return exportNotesOf(card).join(' | ');
  }

  function exportNotesOf(card: ComparisonFigureCard): string[] {
    const chrome = (component as unknown as { exportChrome(card: ComparisonFigureCard): { chrome: FigureChrome } })
      .exportChrome(card);
    return chrome.chrome.notes.map(note => note.text);
  }

  /** Runs `act` with a fake clock and lets the style debounce fire. */
  function withStyleDebounce(act: () => void): void {
    jasmine.clock().install();
    try {
      act();
      jasmine.clock().tick(200);
      refresh();
    } finally {
      jasmine.clock().uninstall();
    }
  }

  it('drives the page from both trade-off toggles in the Charts tab', () => {
    render(buildDto(comparableSet(3)), 2);
    openStyleTab(component.scatterCards[0]);

    const named = styleControl('mc-style-scatter-directLabels');
    const valued = styleControl('mc-style-scatter-inlineValues');
    expect(named.checked).toBe(component.scatterDirectLabels);
    expect(valued.checked).toBe(component.scatterInlineValues);

    setChecked(named, true);
    expect(component.scatterDirectLabels).toBeTrue();
    expect(styleControl('mc-style-scatter-directLabels').checked).toBeTrue();
    expect(scatterLegendDisplays()).toEqual([false, false, false]);

    setChecked(styleControl('mc-style-scatter-inlineValues'), false);
    expect(component.scatterInlineValues).toBeFalse();
    expect(styleControl('mc-style-scatter-inlineValues').checked).toBeFalse();
    expect(scatterBlocks()!.every(b => b.values.length === 0)).toBeTrue();
  });

  it('fills single-run bars from the Charts tab, persists it, and keeps no second copy of the control', () => {
    render(buildDto(comparableSet(3).map(entry => ({ ...entry, runCount: 1 }))), 2);

    const fills = (): unknown[] =>
      (component.panelCards[0].data.datasets[0] as unknown as Record<string, unknown[]>)['backgroundColor'];
    expect(fills().every(fill => fill === 'transparent')).toBeTrue();
    expect(fixture.debugElement.query(By.css('#mc-bar-filled'))).toBeNull();

    openStyleFamily('bar');
    const styleToggle = (): HTMLInputElement => styleControl('mc-style-bar-filledBars');
    expect(styleToggle().checked).toBeFalse();

    withStyleDebounce(() => setChecked(styleToggle(), true));
    expect(component.figureStyle.bar.filledBars).toBeTrue();
    expect(JSON.parse(localStorage.getItem(FIGURE_STYLE_STORAGE_KEY)!).bar.filledBars).toBeTrue();
    expect(fills().some(fill => fill === 'transparent')).toBeFalse();

    withStyleDebounce(() => setChecked(styleToggle(), false));
    expect(styleToggle().checked).toBeFalse();
    expect(fills().every(fill => fill === 'transparent')).toBeTrue();
  });

  it('re-composes the preview when a trade-off toggle is changed in the Charts tab', () => {
    render(buildDto(comparableSet(3)), 2);

    // The clock is installed before the dialog is opened, so the composition the open itself
    // schedules is a fake timer this test drains rather than a real one outliving it.
    jasmine.clock().install();
    try {
      openStyleTab(component.scatterCards[0]);
      const renderPreview = spyOn(
        component as unknown as { renderPreview(): Promise<void> }, 'renderPreview'
      ).and.returnValue(Promise.resolve());
      jasmine.clock().tick(200);
      renderPreview.calls.reset();

      setChecked(styleControl('mc-style-scatter-inlineValues'), false);
      expect(renderPreview).not.toHaveBeenCalled();
      jasmine.clock().tick(200);
      expect(renderPreview).toHaveBeenCalled();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  /**
   * Checks one tab row against the full tab contract: roles, names, selection, a roving tabindex,
   * Left/Right wrapping, Home/End, and focus following selection.
   */
  function expectTabContract(
    listSelector: string,
    listLabel: string,
    idPrefix: string,
    panelPrefix: string,
    labels: string[],
    selected: () => string,
    tabIds: string[] = labels.map(label => label.toLowerCase())
  ): void {
    const tabs = (): HTMLButtonElement[] =>
      fixture.debugElement.queryAll(By.css(`${listSelector} [role="tab"]`))
        .map(tab => tab.nativeElement as HTMLButtonElement);
    const ids = tabIds;

    const tablist = fixture.debugElement.query(By.css(listSelector)).nativeElement as HTMLElement;
    expect(tablist.getAttribute('role')).toBe('tablist');
    expect(tablist.getAttribute('aria-label')).toBe(listLabel);
    expect(tabs().map(tab => tab.textContent!.trim())).toEqual(labels);

    tabs().forEach((tab, index) => {
      expect(tab.id).toBe(`${idPrefix}${ids[index]}`);
      expect(tab.getAttribute('aria-controls')).toBe(`${panelPrefix}${ids[index]}`);
      expect(tab.getAttribute('aria-selected')).toBe(index === 0 ? 'true' : 'false');
      expect(tab.getAttribute('tabindex')).toBe(index === 0 ? '0' : '-1');
    });
    const firstPanel = fixture.debugElement.query(By.css(`#${panelPrefix}${ids[0]}`)).nativeElement as HTMLElement;
    expect(firstPanel.getAttribute('role')).toBe('tabpanel');
    expect(firstPanel.getAttribute('aria-labelledby')).toBe(`${idPrefix}${ids[0]}`);
    expect(firstPanel.getAttribute('tabindex')).toBe('0');

    const press = (index: number, key: string): void => {
      tabs()[index].dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
      refresh();
    };
    const last = labels.length - 1;

    press(0, 'ArrowRight');
    expect(selected()).toBe(ids[1]);
    expect(tabs()[1].getAttribute('aria-selected')).toBe('true');
    expect(tabs()[1].getAttribute('tabindex')).toBe('0');
    expect(tabs()[0].getAttribute('tabindex')).toBe('-1');
    expect(document.activeElement).toBe(tabs()[1]);

    press(1, 'Home');
    expect(selected()).toBe(ids[0]);
    press(0, 'ArrowLeft');
    expect(selected()).withContext('Left wraps to the last tab').toBe(ids[last]);
    press(last, 'ArrowRight');
    expect(selected()).withContext('Right wraps to the first tab').toBe(ids[0]);
    press(0, 'End');
    expect(selected()).toBe(ids[last]);
    expect(document.activeElement).toBe(tabs()[last]);
  }

  it('offers the chart views\' four sidebar sections as tabs with the full tab contract', () => {
    render(buildDto(comparableSet(3)), 2);

    expectTabContract('.mc-fig-sidebar-tabs', 'Settings sections', 'mc-side-tab-', 'mc-side-panel-',
      ['Data', 'Theme', 'Charts', 'Download'], () => component.sidebarTab);
    // Only the selected section's panel is rendered.
    expect(fixture.debugElement.query(By.css('#mc-side-panel-download'))).not.toBeNull();
    for (const tab of ['data', 'theme', 'charts', 'table']) {
      expect(fixture.debugElement.query(By.css(`#mc-side-panel-${tab}`))).withContext(tab).toBeNull();
    }
  });

  it('offers the four views as tabs with icons and the full tab contract', () => {
    render(buildDto(comparableSet(3)), 2);

    // Each visible label is the whole accessible name, so no aria-label repeats it.
    const tabs = fixture.debugElement.queryAll(By.css('.mc-fig-tabs [role="tab"]'))
      .map(tab => tab.nativeElement as HTMLElement);
    expect(tabs.map(tab => tab.getAttribute('aria-label'))).toEqual([null, null, null, null]);

    expectTabContract('.mc-fig-tabs', 'Comparison views', 'mc-fig-tab-', 'mc-fig-panel-',
      ['All charts', 'Single chart', 'Interactive table', 'Table preview'], () => component.figureTab,
      ['all', 'single', 'table', 'tablePreview']);
    expect(component.previewActive).toBeTrue();
    expect(fixture.debugElement.query(By.css('#mc-fig-panel-tablePreview'))).not.toBeNull();
    expect(fixture.debugElement.query(By.css('#mc-fig-panel-all'))).toBeNull();
    expect(fixture.debugElement.query(By.css('#mc-fig-panel-table'))).toBeNull();
    // Every tab carries a glyph, or none would.
    const glyphs = fixture.debugElement.queryAll(By.css('.mc-fig-tabs [role="tab"] svg.btn-icon'));
    expect(glyphs.length).toBe(4);
  });

  it('reads a stored Charts or Preview view as All or Single, and keeps a stored table view', () => {
    const storedView = (view: string): string => {
      localStorage.setItem(FIGURE_SIDEBAR_STORAGE_KEY, JSON.stringify({ version: 1, collapsed: false, tab: 'data', view }));
      const stored = TestBed.createComponent(ModelComparisonComponent);
      const figureTab = stored.componentInstance.figureTab;
      stored.destroy();
      return figureTab;
    };
    expect(storedView('preview')).toBe('single');
    expect(storedView('charts')).toBe('all');
    expect(storedView('gallery')).toBe('all');
    expect(storedView('table')).toBe('table');
    expect(storedView('tablePreview')).toBe('tablePreview');
  });

  it('shows the bar set on a panel, the trade-off set on a scatter and the profile set on the profile', () => {
    render(buildDto(comparableSet(3)), 2);
    openStyleTab(component.panelCards[0]);

    const has = (selector: string): boolean => fixture.debugElement.query(By.css(selector)) !== null;
    expect(has('#mc-style-bar-heading')).toBeTrue();
    expect(has('#mc-style-scatter-heading')).toBeFalse();
    expect(textOf('#mc-side-panel-charts')).not.toContain('Every change here applies');

    // Switching figure keeps the tab, and the set follows the figure's kind.
    component.selectPreviewCard(component.scatterCards[0].id);
    refresh();
    expect(component.sidebarTab).toBe('charts');
    expect(component.styleFamily).toBe('scatter');
    expect(has('#mc-style-scatter-heading')).toBeTrue();
    expect(has('#mc-style-bar-heading')).toBeFalse();

    component.selectPreviewCard(component.profileCard!.id);
    refresh();
    expect(has('#mc-style-bar-heading')).toBeFalse();
    expect(has('#mc-style-scatter-heading')).toBeFalse();
    expect(has('#mc-style-profile-heading')).toBeTrue();
  });

  it('stores a style change at once, persists it, and rebuilds the figures after the debounce', () => {
    render(buildDto(comparableSet(3)), 2);

    jasmine.clock().install();
    try {
      openStyleTab(component.panelCards[0]);
      const renderPreview = spyOn(
        component as unknown as { renderPreview(): Promise<void> }, 'renderPreview'
      ).and.returnValue(Promise.resolve());
      jasmine.clock().tick(200);
      renderPreview.calls.reset();

      const barPercentage = (): unknown =>
        (component.panelCards[0].data.datasets[0] as unknown as Record<string, unknown>)['barPercentage'];
      expect(barPercentage()).toBeCloseTo(0.72, 9);

      setRange(styleControl('mc-style-bar-gapPercent'), 10);
      expect(component.figureStyle.bar.gapPercent).toBe(10);
      const stored = JSON.parse(localStorage.getItem(FIGURE_STYLE_STORAGE_KEY)!);
      expect(stored.version).toBe(1);
      expect(stored.bar.gapPercent).toBe(10);
      // Not yet: a drag rebuilds once it pauses.
      expect(barPercentage()).toBeCloseTo(0.72, 9);

      jasmine.clock().tick(150);
      expect(barPercentage()).toBeCloseTo(0.9, 9);
      expect(renderPreview).not.toHaveBeenCalled();
      jasmine.clock().tick(150);
      expect(renderPreview).toHaveBeenCalled();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('drops a hidden badge from the bar cards after the debounce, and persists it', () => {
    render(buildDto(comparableSet(3)), 2);

    jasmine.clock().install();
    try {
      const kinds = (): (string | undefined)[] => component.panelCards[0].chrome.badges.map(badge => badge.kind);
      const before = kinds();
      expect(before).toContain('questions');

      component.onFigureStyleChange({
        ...component.figureStyle,
        bar: { ...component.figureStyle.bar, hiddenBadges: ['questions'] }
      });
      expect(kinds()).toEqual(before);
      const stored = JSON.parse(localStorage.getItem(FIGURE_STYLE_STORAGE_KEY)!);
      expect(stored.version).toBe(1);
      expect(stored.bar.hiddenBadges).toEqual(['questions']);

      jasmine.clock().tick(150);
      expect(kinds()).toEqual(before.filter(kind => kind !== 'questions'));
      expect(component.scatterCards[0].chrome.badges.some(badge => badge.kind === 'questions')).toBeTrue();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  /** Applies one family's style change the way the Charts tab does, and re-renders without a tick. */
  function changeFamilyStyle<K extends 'bar' | 'scatter' | 'profile'>(
    family: K, change: Partial<FigureStyle[K]>
  ): void {
    component.onFigureStyleChange({
      ...component.figureStyle,
      [family]: { ...component.figureStyle[family], ...change }
    });
    refresh();
  }

  /** One figure's caption as the composer draws it — on its All tile and in its file alike. */
  function drawnChrome(card: ComparisonFigureCard): {
    footer: FigureFooter; textSizes?: { titlePx: number; badgePx: number; footerPx: number };
  } {
    return (component as unknown as {
      exportChrome(card: ComparisonFigureCard): {
        footer: FigureFooter; textSizes?: { titlePx: number; badgePx: number; footerPx: number };
      };
    }).exportChrome(card);
  }

  it('draws the figure footer in every figure, and drops it where Show footer is off', () => {
    render(buildDto(comparableSet(3)), 2);
    const cards = [...component.panelCards, component.profileCard!, ...component.scatterCards];
    for (const card of cards) {
      expect(drawnChrome(card).footer.suite).withContext(card.id).toBe('GnollHack Player Assistance Benchmark Suite');
      expect(drawnChrome(card).footer.computedAt).withContext(card.id).not.toBe('');
    }

    jasmine.clock().install();
    try {
      changeFamilyStyle('profile', { footer: false });
      expect(drawnChrome(component.profileCard!).footer).toEqual({ suite: '', computedAt: '' });
      for (const card of component.panelCards) {
        expect(drawnChrome(card).footer.suite).withContext(card.id).not.toBe('');
      }
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('draws each family at its own caption sizes, and re-composes the All tiles once a style change pauses', async () => {
    render(buildDto(comparableSet(3)), 2);
    await settleAllTab();
    expect(component.allActive).toBeTrue();
    const schedule = spyOn(
      component as unknown as { scheduleAllCompose(): void }, 'scheduleAllCompose').and.callThrough();

    jasmine.clock().install();
    try {
      changeFamilyStyle('bar', { titleSizePx: 30, badgeTextSizePx: 14, footerTextSizePx: 16 });
      // Not yet: a drag re-composes once it pauses.
      expect(schedule).not.toHaveBeenCalled();
      jasmine.clock().tick(150);
      expect(schedule).toHaveBeenCalled();

      for (const card of component.panelCards) {
        expect(drawnChrome(card).textSizes).withContext(card.id).toEqual({ titlePx: 30, badgePx: 14, footerPx: 16 });
      }
      expect(drawnChrome(component.scatterCards[0]).textSizes).toEqual({ titlePx: 18, badgePx: 11, footerPx: 12 });
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('adds the hidden-intervals note to the Intelligence card when its bars are hidden, and drops it on request', () => {
    render(buildDto(comparableSet(3)), 2);
    openStyleTab(component.panelCards[0]);

    const noteToggle = styleControl('mc-style-bar-hiddenIntervalsNote');
    expect(noteToggle.disabled).toBeTrue();
    expect(drawnNotes('panels', 0)).not.toContain(HIDDEN_INTERVALS_NOTE);

    withStyleDebounce(() => setChecked(styleControl('mc-style-bar-intervals'), false));
    expect(drawnNotes('panels', 0)).toContain(HIDDEN_INTERVALS_NOTE);
    // The Speed panel on mean time draws no whisker anyway, so it gains nothing.
    expect(drawnNotes('panels', 1)).not.toContain(HIDDEN_INTERVALS_NOTE);
    expect(component.panelCards[0].plugins).not.toContain(errorBarPlugin);

    expect(styleControl('mc-style-bar-hiddenIntervalsNote').disabled).toBeFalse();
    withStyleDebounce(() => setChecked(styleControl('mc-style-bar-hiddenIntervalsNote'), false));
    expect(drawnNotes('panels', 0)).not.toContain(HIDDEN_INTERVALS_NOTE);
  });

  it('drops the frontier note from the scatter card and its export on request, keeping the set notes', () => {
    render(buildDto([...comparableSet(3), buildExcludedEntry('run:9', ['ScoringVersion'])]), 2);
    const setNotes = component.setFigureNotes.map(note => note.text);
    expect(setNotes.length).toBeGreaterThan(0);

    const s1 = (): ComparisonFigureCard => component.scatterCards[0];
    expect(s1().chrome.notes.map(note => note.text)).toContain(FRONTIER_UNCERTAINTY_NOTE);
    expect(exportNotesOf(s1())).toContain(FRONTIER_UNCERTAINTY_NOTE);

    openStyleTab(s1());
    withStyleDebounce(() => setChecked(styleControl('mc-style-scatter-frontierIntervalsNote'), false));

    expect(s1().chrome.notes.map(note => note.text)).not.toContain(FRONTIER_UNCERTAINTY_NOTE);
    expect(drawnNotes('scatters', 0)).not.toContain(FRONTIER_UNCERTAINTY_NOTE);
    expect(exportNotesOf(s1())).not.toContain(FRONTIER_UNCERTAINTY_NOTE);
    for (const text of setNotes) {
      expect(exportNotesOf(s1())).toContain(text);
    }
  });

  it('reads the badge off the exam the runs were asked, with no left-out note for a revised rubric', () => {
    // A fully scored 18-question exam, whatever the suite holds now.
    render(buildDto(comparableSet(2)), 2);

    expect(component.setFigureNotes.some(note => note.tone === 'info' && /left out/.test(note.text))).toBeFalse();
    expect(component.setFigureNotes.some(note => /revised/.test(note.text))).toBeFalse();

    const s1 = component.scatterCards[0];
    expect(s1.chrome.badges.find(badge => badge.kind === 'questions')?.text).toBe('18 questions');
    expect(exportNotesOf(s1).some(text => /revised/.test(text))).toBeFalse();
  });

  it('states the scored questions against the exam in the badge when some went unscored', () => {
    const entries = comparableSet(2).map(entry => ({
      ...entry,
      quality: { ...entry.quality!, itemCount: 16, unscoredItemCount: 2 }
    }));
    render(buildDto(entries), 2);

    const s1 = component.scatterCards[0];
    expect(s1.chrome.badges.find(badge => badge.kind === 'questions')?.text).toBe('16 of 18 questions');
  });

  it('warns only about the plotted entries\' unscored questions', () => {
    const entries = comparableSet(3).map((entry, index) => index === 2
      ? { ...entry, quality: { ...entry.quality!, itemCount: 17, unscoredItemCount: 1 } }
      : entry);
    render(buildDto(entries), 2);

    const warning = 'Gemini 2.5 Flash (medium): 1 question has no scored answer (failed, skipped or ungraded) and is left out of its index.';
    expect(component.setFigureNotes).toContain({ text: warning, tone: 'warning' });

    component.toggleEntry('run:3');
    expect(component.setFigureNotes.map(note => note.text)).not.toContain(warning);
  });

  it('drops the mean-time note from the Speed card and its export on request, keeping the set notes', () => {
    render(buildDto([...comparableSet(3), buildExcludedEntry('run:9', ['ScoringVersion'])]), 2);
    expect(component.speedMeasure).toBe('meanModelTime');
    const setNotes = component.setFigureNotes.map(note => note.text);
    const speed = (): ComparisonFigureCard => component.panelCards[1];
    expect(drawnNotes('panels', 1)).toContain(MEAN_TIME_NO_INTERVAL_NOTE);

    openStyleTab(speed());
    const toggle = styleControl('mc-style-bar-meanTimeNoIntervalNote');
    expect(toggle.disabled).toBeFalse();
    withStyleDebounce(() => setChecked(toggle, false));

    expect(drawnNotes('panels', 1)).not.toContain(MEAN_TIME_NO_INTERVAL_NOTE);
    expect(exportNotesOf(speed())).not.toContain(MEAN_TIME_NO_INTERVAL_NOTE);
    for (const text of setNotes) {
      expect(exportNotesOf(speed())).toContain(text);
    }
  });

  it('lets a forced horizontal orientation turn the panels in a wide container', () => {
    render(buildDto(comparableSet(3)), 2);
    component.applyContainerWidth(P1_STACK_BREAKPOINT_PX + 400);
    expect(component.orientation).toBe('vertical');
    expect((component.panelCards[0].options as { indexAxis?: string }).indexAxis).not.toBe('y');

    openStyleTab(component.panelCards[0]);
    withStyleDebounce(() => setChecked(styleControl('mc-style-bar-orientation-horizontal'), true));

    expect(component.effectiveOrientation).toBe('horizontal');
    expect(component.orientation).toBe('vertical');
    expect((component.panelCards[0].options as { indexAxis?: string }).indexAxis).toBe('y');
  });

  it('applies a stored style and falls back to the default on unreadable storage', () => {
    localStorage.setItem(FIGURE_STYLE_STORAGE_KEY, JSON.stringify({ version: 1, bar: { gapPercent: 10 } }));
    render(buildDto(comparableSet(3)), 2);
    expect(component.figureStyle.bar.gapPercent).toBe(10);
    expect(component.figureStyle.scatter).toEqual(DEFAULT_FIGURE_STYLE.scatter);

    localStorage.setItem(FIGURE_STYLE_STORAGE_KEY, '{not json');
    const second = TestBed.createComponent(ModelComparisonComponent);
    expect(() => second.detectChanges()).not.toThrow();
    expect(second.componentInstance.figureStyle).toEqual(DEFAULT_FIGURE_STYLE);
    second.destroy();
  });

  // --- Number format and the value-axis title break ---------------------------------------------

  it('holds empty number samples until figures exist, then fills each family\'s from the plotted set', () => {
    expect(component.figures).toBeNull();
    expect(component.numberSamples).toEqual({ bar: {}, scatter: {}, profile: {} });

    render(buildDto(comparableSet(3)), 2);
    const plotted = component.figures!.selection.plotted;
    expect(component.numberSamples.bar.intelligenceIndex).toEqual({ value: plotted[0].intelligenceIndex });
    expect(Object.keys(component.numberSamples.bar).sort()).toEqual(['intelligenceIndex', 'meanModelTime', 'suiteCost']);
    expect(Object.keys(component.numberSamples.scatter).sort()).toEqual(['costPerQuestion', 'intelligenceIndex', 'meanModelTime']);
    expect(component.numberSamples.scatter.costPerQuestion).toEqual({ value: plotted[0].candidateCostPerQuestionUsd });
    expect(Object.keys(component.numberSamples.profile).sort()).toEqual(['intelligenceIndex', 'meanModelTime', 'suiteCost']);
  });

  it('keeps the number samples through a family switch and replaces them on a rebuild', () => {
    render(buildDto(comparableSet(3)), 2);
    const before = component.numberSamples;
    component.selectStyleFamily('scatter');
    component.selectStyleFamily('profile');
    component.selectStyleFamily('bar');
    expect(component.numberSamples).toBe(before);

    component.onSpeedMeasureChange('ttftP50');
    expect(component.numberSamples).not.toBe(before);
    const plotted = component.figures!.selection.plotted;
    expect(component.numberSamples.bar.ttftP50).toEqual({ value: plotted[0].ttftP50Ms, unit: 's' });
    expect(component.numberSamples.bar.meanModelTime).toBeUndefined();

    const first = component.numberSamples.bar.intelligenceIndex!.value;
    component.onSortDirectionChange(component.sort.direction === 'desc' ? 'asc' : 'desc');
    expect(component.numberSamples.bar.intelligenceIndex!.value).not.toBe(first);
    expect(component.numberSamples.bar.intelligenceIndex!.value).toBe(component.figures!.selection.plotted[0].intelligenceIndex);
  });

  it('hands the shown family\'s samples and the selected measures to the style panel', () => {
    render(buildDto(comparableSet(3)), 2);
    openStyleTab(component.panelCards[0]);
    const panel = fixture.debugElement.query(By.css('app-figure-style-panel'));
    expect(panel).not.toBeNull();
    const instance = panel.componentInstance as { numberSamples: unknown; speedMeasure: unknown; costMeasure: unknown };
    expect(instance.numberSamples).toBe(component.numberSamples.bar);
    expect(instance.speedMeasure).toBe(component.speedMeasure);
    expect(instance.costMeasure).toBe(component.costMeasure);
    expect(styleControl('mc-style-bar-number-intelligenceIndex')).toBeTruthy();
  });

  it('refreshes the profile ranges and the figures after a number change, once the style debounce passes', () => {
    render(buildDto(comparableSet(3)), 2);
    const minLabel = (): string => component.profileAxes!.axes[0].minLabel;
    expect(minLabel()).toMatch(/^\d+$/);

    jasmine.clock().install();
    try {
      component.onFigureStyleChange({
        ...component.figureStyle,
        numbers: { ...component.figureStyle.numbers, intelligenceIndex: 2 }
      });
      expect(JSON.parse(localStorage.getItem(FIGURE_STYLE_STORAGE_KEY)!).numbers.intelligenceIndex).toBe(2);
      expect(minLabel()).toMatch(/^\d+$/);

      jasmine.clock().tick(150);
      refresh();
      expect(minLabel()).toMatch(/^\d+\.\d{2}$/);
      expect(textOf('.mc-axis-ends')).toContain(minLabel());
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('persists the number formats and the title break, and reads them back', () => {
    render(buildDto(comparableSet(3)), 2);
    component.onFigureStyleChange({
      ...component.figureStyle,
      bar: { ...component.figureStyle.bar, axisTitleBreak: 'always' },
      numbers: { ...component.figureStyle.numbers, suiteCost: 2, ttftP50: 5 }
    });
    const stored = JSON.parse(localStorage.getItem(FIGURE_STYLE_STORAGE_KEY)!);
    expect(stored.version).toBe(1);
    expect(stored.bar.axisTitleBreak).toBe('always');
    expect(stored.numbers).toEqual({ ...DEFAULT_FIGURE_STYLE.numbers, suiteCost: 2, ttftP50: 5 });

    const second = TestBed.createComponent(ModelComparisonComponent);
    second.detectChanges();
    expect(second.componentInstance.figureStyle.bar.axisTitleBreak).toBe('always');
    expect(second.componentInstance.figureStyle.numbers).toEqual({ ...DEFAULT_FIGURE_STYLE.numbers, suiteCost: 2, ttftP50: 5 });
    second.destroy();

    // A style stored before either field existed reads both at their defaults.
    localStorage.setItem(FIGURE_STYLE_STORAGE_KEY, JSON.stringify({ version: 1, bar: { gapPercent: 10 } }));
    const third = TestBed.createComponent(ModelComparisonComponent);
    third.detectChanges();
    expect(third.componentInstance.figureStyle.bar.axisTitleBreak).toBe('auto');
    expect(third.componentInstance.figureStyle.numbers).toEqual(DEFAULT_FIGURE_STYLE.numbers);
    third.destroy();
  });

  it('feeds the Text size range into every size’s layout, and persists it', async () => {
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('download');
    openSingle(component.panelCards[0]);

    // Full HD, where the size opens: the text size applies to it as to every other size.
    expect(component.figureSize.resolutionId).toBe('fullhd');
    expect(styleControl('mc-export-text-scale').disabled).toBeFalse();
    expect(component.exportDimensionsLabel).toContain('laid out at 960 × 540');

    component.onExportResolutionChange('square1080');
    refresh();
    expect(component.exportDimensionsLabel).toContain('laid out at 960 × 960');

    setRange(styleControl('mc-export-text-scale'), 200);
    expect(component.figureSize.textScalePercent).toBe(200);
    expect(component.exportDimensionsLabel).toContain('laid out at 480 × 480');
    expect(JSON.parse(localStorage.getItem(FIGURE_SIZE_STORAGE_KEY)!).textScalePercent).toBe(200);

    component.onExportResolutionChange('custom');
    component.onCustomWidthChange(640);
    component.onCustomHeightChange(640);
    setRange(styleControl('mc-export-text-scale'), 250);
    await composePreview();
    expect(component.previewRefusal).toContain('caption column would be narrower than 360 px');
  });

  it('refuses a size the figure does not fit, naming it, and leaves the stage blank', async () => {
    render(buildDto(comparableSet(3)), 2);
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
    openSidebarTab('download');
    openSingle(card);

    component.onExportResolutionChange('custom');
    component.onCustomWidthChange(1280);
    component.onCustomHeightChange(720);
    await composePreview();

    expect(component.customResolutionError).toBe('');
    expect(component.previewRefusal).toContain(card.title);
    // The target size, which is what Download would write and what it would refuse.
    expect(component.previewRefusal).toContain('1280 × 720 px');
    expect(textOf('#mc-side-panel-download .mc-export-error')).toContain('1280 × 720 px');
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
    render(buildDto(comparableSet(3)), 2);
    // A fixture's element is never laid out, so the stage's geometry is given rather than measured.
    spyOn(component, 'measureStage').and.returnValue({ width: 800, height: 600, devicePixelRatio: 2 });
    openSingle();

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
    render(buildDto(comparableSet(3)), 2);
    spyOn(component, 'measureStage').and.returnValue({ width: 800, height: 600, devicePixelRatio: 2 });
    openSingle();

    component.onExportDensityChange(1);
    component.onExportResolutionChange('a4p');
    await composePreview();

    const stage = component.previewCanvas!.nativeElement;
    expect(parseFloat(stage.style.height)).toBeCloseTo(600, 6);
    expect(stage.height).toBeGreaterThan(stage.width);
    expect(Math.abs(stage.width - stage.height * (2480 / 3508))).toBeLessThan(1);
  });

  // -------------------------------------------------------------------------------------------
  // Preview zoom and pan
  // -------------------------------------------------------------------------------------------

  /** One of the zoom group's controls, by its accessible name. */
  function zoomButton(name: string): HTMLButtonElement {
    return fixture.debugElement.query(By.css(`.mc-preview-zoom button[aria-label="${name}"]`))
      .nativeElement as HTMLButtonElement;
  }

  /** One of the three icon-only view buttons, by its accessible name. */
  function viewButton(label: 'Fit to screen' | 'Actual pixels, 100 percent' | 'Reset view'): HTMLButtonElement {
    return zoomButton(label);
  }

  function previewViewport(): HTMLElement {
    return fixture.debugElement.query(By.css('.mc-preview-viewport')).nativeElement as HTMLElement;
  }

  function clickAndRefresh(button: HTMLButtonElement): void {
    button.click();
    refresh();
  }

  function stageCanvas(): HTMLCanvasElement {
    return component.previewCanvas!.nativeElement;
  }

  /** Full HD at 100 % density on an 800 × 600 stage at DPR 2: the screen fit is 5/6. */
  async function openFullHdPreview(): Promise<void> {
    render(buildDto(comparableSet(3)), 2);
    spyOn(component, 'measureStage').and.returnValue({ width: 800, height: 600, devicePixelRatio: 2 });
    openSingle();
    component.onExportDensityChange(1);
    component.onExportResolutionChange('fullhd');
    await composePreview();
  }

  /** A custom 800 × 600 at 100 % density on the same stage: the screen fit is 2. */
  async function openCustomPreview(): Promise<void> {
    render(buildDto(comparableSet(3)), 2);
    spyOn(component, 'measureStage').and.returnValue({ width: 800, height: 600, devicePixelRatio: 2 });
    openSingle();
    component.onExportDensityChange(1);
    component.onExportResolutionChange('custom');
    component.onCustomWidthChange(800);
    component.onCustomHeightChange(600);
    await composePreview();
  }

  it('opens the preview in the default view, shrunk to fit', async () => {
    await openFullHdPreview();

    expect(component.previewView).toBe('default');
    expect(textOf('.mc-preview-zoom-value')).toBe('83%');
    const slider = fixture.debugElement.query(By.css('#mc-preview-zoom')).nativeElement as HTMLInputElement;
    expect(+slider.value).toBe(zoomToSlider(5 / 6, previewZoomRange(5 / 6)));
    expect(slider.getAttribute('aria-valuetext')).toBe('83 percent, default view');
    expect(parseFloat(stageCanvas().style.width)).toBeCloseTo(800, 6);
  });

  it('zooms in from the button, rasterising the export’s own pixels at 100 %', async () => {
    await openFullHdPreview();

    clickAndRefresh(zoomButton('Zoom the preview in'));

    expect(component.previewZoomValue).toBe(1);
    expect(textOf('.mc-preview-zoom-value')).toBe('100%');
    // The old bitmap is stretched at once; the composition that follows sharpens it.
    expect(parseFloat(stageCanvas().style.width)).toBeCloseTo(960, 6);
    await composePreview();
    expect(stageCanvas().width).toBe(1920);
    expect(stageCanvas().height).toBe(1080);
    expect(parseFloat(stageCanvas().style.width)).toBeCloseTo(960, 6);
    expect(stageCanvas().classList).not.toContain('is-pixelated');
  });

  it('enlarges past 100 % by CSS alone, square-edged from 200 %', async () => {
    await openFullHdPreview();
    clickAndRefresh(zoomButton('Zoom the preview in'));
    await composePreview();

    const schedule = spyOn(
      component as unknown as { schedulePreview(): void }, 'schedulePreview').and.callThrough();
    clickAndRefresh(zoomButton('Zoom the preview in'));
    expect(component.previewZoomValue).toBe(1.5);
    expect(stageCanvas().classList).not.toContain('is-pixelated');
    clickAndRefresh(zoomButton('Zoom the preview in'));

    expect(component.previewZoomValue).toBe(2);
    expect(parseFloat(stageCanvas().style.width)).toBeCloseTo(1920, 6);
    expect(stageCanvas().classList).toContain('is-pixelated');
    expect(stageCanvas().width).toBe(1920);
    expect(schedule).not.toHaveBeenCalled();
  });

  it('returns to the default view on Reset view', async () => {
    await openFullHdPreview();
    component.setPreviewView(3);
    refresh();

    clickAndRefresh(viewButton('Reset view'));

    expect(component.previewView).toBe('default');
    expect(parseFloat(stageCanvas().style.width)).toBeCloseTo(800, 6);
    expect(stageCanvas().classList).not.toContain('is-pixelated');
  });

  it('keeps an explicit zoom across a style change and resets it on a new pixel size', async () => {
    await openFullHdPreview();
    component.setPreviewView(1.5);

    component.onFigureStyleChange({ ...component.figureStyle });
    await composePreview();
    expect(component.previewView).toBe(1.5);

    component.onExportResolutionChange('a4p');
    await composePreview();
    expect(component.previewView).toBe('default');
    expect(parseFloat(stageCanvas().style.height)).toBeCloseTo(600, 6);
  });

  it('fills the stage on Fit to screen, past 100 % for a small export', async () => {
    await openCustomPreview();
    expect(component.previewZoomValue).toBe(1);
    expect(parseFloat(stageCanvas().style.width)).toBeCloseTo(400, 6);

    clickAndRefresh(viewButton('Fit to screen'));

    expect(parseFloat(stageCanvas().style.width)).toBeCloseTo(800, 6);
    expect(textOf('.mc-preview-zoom-value')).toBe('200% · Fit to screen');
    expect(stageCanvas().classList).toContain('is-pixelated');
    await composePreview();
    expect(stageCanvas().width).toBe(800);
    expect(stageCanvas().height).toBe(600);
  });

  it('keeps Fit to screen across a new size, refitting it, until Reset view', async () => {
    await openCustomPreview();
    clickAndRefresh(viewButton('Fit to screen'));

    component.onCustomWidthChange(640);
    component.onCustomHeightChange(480);
    await composePreview();

    expect(component.previewView).toBe('fitScreen');
    expect(component.previewZoomValue).toBeCloseTo(2.5, 9);
    expect(parseFloat(stageCanvas().style.width)).toBeCloseTo(800, 6);

    clickAndRefresh(viewButton('Reset view'));
    expect(component.previewZoomValue).toBe(1);
    expect(parseFloat(stageCanvas().style.width)).toBeCloseTo(320, 6);
  });

  it('shows the target’s own size over the display ratio at 100 %', async () => {
    await openCustomPreview();
    clickAndRefresh(viewButton('Fit to screen'));

    const actual = viewButton('Actual pixels, 100 percent');
    expect(actual.textContent!.trim()).toBe('');
    clickAndRefresh(actual);

    expect(component.previewView).toBe(1);
    expect(parseFloat(stageCanvas().style.width)).toBeCloseTo(400, 6);
  });

  it('answers + − 0 1 on the focused viewport, and leaves them to the browser with Ctrl', async () => {
    await openFullHdPreview();
    const press = (key: string, init: KeyboardEventInit = {}): KeyboardEvent => {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
      previewViewport().dispatchEvent(event);
      refresh();
      return event;
    };

    expect(press('+').defaultPrevented).toBeTrue();
    expect(component.previewZoomValue).toBe(1);
    press('=');
    expect(component.previewZoomValue).toBe(1.5);
    press('-');
    expect(component.previewZoomValue).toBe(1);
    press('0');
    expect(component.previewView).toBe('fitScreen');
    press('1');
    expect(component.previewView).toBe(1);

    const withCtrl = press('+', { ctrlKey: true });
    expect(withCtrl.defaultPrevented).toBeFalse();
    expect(component.previewZoomValue).toBe(1);
    const arrow = press('ArrowDown');
    expect(arrow.defaultPrevented).toBeFalse();
  });

  it('marks zoom in unavailable at 800 %, and refuses it there', async () => {
    await openFullHdPreview();
    component.setPreviewView(8);
    refresh();

    const zoomIn = zoomButton('Zoom the preview in');
    expect(zoomIn.getAttribute('aria-disabled')).toBe('true');
    expect(zoomIn.disabled).toBeFalse();
    expect(zoomButton('Zoom the preview out').getAttribute('aria-disabled')).toBeNull();
    clickAndRefresh(zoomIn);

    expect(component.previewZoomValue).toBe(8);
  });

  it('writes the same pixels whatever the preview’s zoom', async () => {
    await openFullHdPreview();
    const saved = captureSaves();
    const sizeOf = async (blob: Blob): Promise<[number, number]> => {
      const bitmap = await createImageBitmap(blob);
      const size: [number, number] = [bitmap.width, bitmap.height];
      bitmap.close();
      return size;
    };

    await component.downloadPreviewedFigure();
    component.setPreviewView(4);
    await component.downloadPreviewedFigure();
    component.setPreviewView('fitScreen');
    await component.downloadPreviewedFigure();

    expect(saved.blobs.length).toBe(3);
    const sizes = await Promise.all(saved.blobs.map(sizeOf));
    expect(sizes[0]).toEqual([1920, 1080]);
    expect(sizes[1]).toEqual(sizes[0]);
    expect(sizes[2]).toEqual(sizes[0]);
  });

  it('returns to the Single tab in the default view', async () => {
    await openFullHdPreview();
    component.setPreviewView(4);

    component.selectFigureTab('all');
    refresh();
    openSingle();

    expect(component.previewView).toBe('default');
  });

  /** The stage the fake observer watches, and the viewport the pan and wheel listeners are on. */
  function watchedStage(observers: RecordingResizeObserver[]): {
    watching: RecordingResizeObserver[]; viewport: HTMLElement; removed: jasmine.Spy;
  } {
    const stage = fixture.debugElement.query(By.css('.mc-preview-stage')).nativeElement as HTMLElement;
    const viewport = previewViewport();
    return {
      watching: observers.filter(observer => observer.observed.includes(stage)),
      viewport,
      removed: spyOn(viewport, 'removeEventListener').and.callThrough()
    };
  }

  it('stops watching the stage on switching to All, and watches the All viewport instead', () => {
    render(buildDto(comparableSet(3)), 2);
    const observers = installFakeResizeObserver();
    openSingle();
    const { watching, removed } = watchedStage(observers);
    expect(watching.length).toBe(1);

    (fixture.debugElement.query(By.css('#mc-fig-tab-all')).nativeElement as HTMLButtonElement).click();
    refresh();

    expect(watching[0].disconnected).toBe(1);
    expect(component.previewActive).toBeFalse();
    expect(removed).toHaveBeenCalledWith('wheel', jasmine.any(Function), jasmine.anything());
    expect(fixture.debugElement.query(By.css('#mc-fig-panel-single'))).toBeNull();

    const viewport = fixture.debugElement.query(By.css('.mc-all-viewport')).nativeElement as HTMLElement;
    const watchingAll = observers.filter(observer => observer.observed.includes(viewport));
    expect(watchingAll.length).toBe(1);
    expect(component.allActive).toBeTrue();

    // Back to Single: the All viewport's observer goes with it.
    singleTabButton().click();
    refresh();
    expect(watchingAll[0].disconnected).toBe(1);
    expect(component.allActive).toBeFalse();
  });

  it('stops watching the stage on leaving step 2, and on destroy', async () => {
    render(buildDto(comparableSet(3)), 2);
    const observers = installFakeResizeObserver();
    openSingle();
    const first = watchedStage(observers);

    component.goToStep(1);
    fixture.detectChanges();
    expect(first.watching[0].disconnected).toBe(1);
    expect(first.removed).toHaveBeenCalledWith('pointerdown', jasmine.any(Function), undefined);
    expect(component.figureTab).withContext('kept, so returning shows the Single tab again').toBe('single');

    component.goToStep(2);
    fixture.detectChanges();
    // The re-attach runs in a microtask, outside the check pass that found the stage.
    await Promise.resolve();
    fixture.detectChanges();
    const second = watchedStage(observers);
    expect(second.watching.length).toBe(1);

    fixture.destroy();
    expect(second.watching[0].disconnected).toBe(1);
  });

  it('removes the viewport listeners from their element after a refetch has taken it out of the DOM', () => {
    render(buildDto(comparableSet(3)), 2);
    const observers = installFakeResizeObserver();
    openSingle();
    const { watching, viewport, removed } = watchedStage(observers);

    // A refetch down to one entry: step 2 stays open, and the stage goes with the charts.
    fixture.componentRef.setInput('comparison', buildDto(comparableSet(1)));
    fixture.detectChanges();

    expect(viewport.isConnected).toBeFalse();
    expect(component.previewActive).toBeFalse();
    expect(watching[0].disconnected).toBe(1);
    expect(removed).toHaveBeenCalledWith('wheel', jasmine.any(Function), jasmine.anything());
    expect(removed).toHaveBeenCalledWith('pointerdown', jasmine.any(Function), undefined);
  });

  it('carries the export size and format in the Download all charts tooltip, and a size error instead of it', () => {
    render(buildDto(comparableSet(3)), 2);

    component.onExportDensityChange(1);
    component.onExportResolutionChange('fullhd');
    refresh();
    expect(component.downloadAllTooltip).toContain(component.exportSummary);
    const tooltip = (): string => textOf('#mc-tip-download-all');
    expect(tooltip()).toContain('All charts as one archive');
    expect(tooltip()).toContain('Full HD — 1920 × 1080');
    expect(tooltip()).toContain('100%');
    expect(tooltip()).toContain('PNG');

    component.onExportFormatChange('webp');
    refresh();
    expect(tooltip()).toContain('WebP q85');
    expect(component.exportAspectLabel).toBe('16:9');

    component.onExportResolutionChange('custom');
    component.onCustomWidthChange(10);
    refresh();
    expect(component.exportSizeError).not.toBe('');
    expect(tooltip()).toBe(component.exportSizeError);

    component.exporting = true;
    expect(component.downloadAllTooltip).toBe('An export is running.');
    component.exporting = false;
  });

  it('offers an Open in Single view control on every tile, naming its figure and never disabled', () => {
    render(buildDto(comparableSet(3)), 2);

    const opens = (): HTMLButtonElement[] => fixture.debugElement.queryAll(By.css('.mc-all-tile .mc-all-open'))
      .map(button => button.nativeElement as HTMLButtonElement);
    expect(opens().length).toBe(7);

    // Icon-only, so aria-label is the accessible name — and seven of them must not share one.
    const names = opens().map(button => button.getAttribute('aria-label') ?? '');
    expect(names.every(name => name.startsWith('Open ') && name.endsWith(' in Single view')))
      .toBeTrue();
    expect(new Set(names).size).toBe(7);
    expect(opens().every(button => button.querySelector('path')?.getAttribute('d')?.startsWith('M1 12s4-8')))
      .withContext('the eye glyph').toBeTrue();
    expect(opens().every(button => !button.disabled && button.getAttribute('aria-disabled') === null))
      .toBeTrue();
    expect(opens().every(button => button.textContent?.trim() === '')).toBeTrue();

    // The Single tab is where a size error is shown and fixed, so it stays reachable under one,
    // though the All tab has no tile to size.
    component.onExportResolutionChange('custom');
    component.onCustomWidthChange(10);
    refresh();
    expect(component.canExport).toBeFalse();
    expect(opens().length).toBe(0);
    const single = singleTabButton();
    expect(single.disabled).toBeFalse();
    expect(single.getAttribute('aria-disabled')).toBeNull();
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

  /** The Comparison table row's table actions, present in both table views. */
  function tableActions(): { copy: HTMLButtonElement; download: HTMLButtonElement } {
    return {
      copy: fixture.debugElement.query(By.css('.mc-table-export .mc-table-copy')).nativeElement as HTMLButtonElement,
      download: fixture.debugElement.query(By.css('.mc-table-export .mc-table-download')).nativeElement as HTMLButtonElement
    };
  }

  function chooseTableFormat(format: TableFileFormat): void {
    component.onTableFormatChange(format);
    refresh();
  }

  /** The keys of the rows every write takes: filters applied, current order, all pages. */
  function tableOrder(): string[] {
    return component.entryTable.viewAll(component.entries).map(entry => entry.key);
  }

  /** The sort button of one Interactive table header, found by its label. */
  function headerButton(label: string): HTMLButtonElement {
    const button = fixture.debugElement.queryAll(By.css('table.mc-table thead th .gh-th-sort'))
      .map(candidate => candidate.nativeElement as HTMLButtonElement)
      .find(candidate => candidate.textContent?.trim() === label);
    expect(button).withContext(`the ${label} header`).toBeTruthy();
    return button!;
  }

  /** The shown headers of the Interactive table, in order. */
  function tableHeaders(): string[] {
    return fixture.debugElement.queryAll(By.css('table.mc-table thead tr:first-child th'))
      .map(header => (header.nativeElement as HTMLElement).textContent?.trim() ?? '');
  }

  /** A column configuration from the defaults, with columns shown, hidden or moved. */
  function columnsWith(change: { show?: string[]; hide?: string[]; order?: string[] }): TableColumnConfig {
    const order = change.order ?? [...DEFAULT_TABLE_COLUMNS.order];
    const shown = [...DEFAULT_TABLE_COLUMNS.shown, ...(change.show ?? [])].filter(key => !(change.hide ?? []).includes(key));
    return { order, shown };
  }

  it('offers seven table formats in the table views\' Download tab, Excel first, each with one line on it', () => {
    renderTable(buildDto(comparableSet(4)));
    openSidebarTab('download');

    const select = fixture.debugElement.query(By.css('#mc-table-format')).nativeElement as HTMLSelectElement;
    expect(Array.from(select.options).map(option => option.value))
      .toEqual(['xlsx', 'csv', 'tsv', 'md', 'json', 'html', 'image']);
    expect(Array.from(select.options).map(option => option.textContent?.trim()).pop()).toBe('Image (PNG or WebP)');
    expect(fixture.debugElement.query(By.css('label[for="mc-table-format"]'))).toBeTruthy();
    expect(textOf('#mc-table-format-hint')).toContain('Numbers stay numbers; a second sheet holds the provenance.');
    // Settings and the scope line only: Copy table and Download table are on the Comparison table row.
    expect(fixture.debugElement.query(By.css('#mc-side-table-download'))).toBeNull();
    expect(fixture.debugElement.query(By.css('#mc-side-table-copy'))).toBeNull();
    expect(fixture.debugElement.query(By.css('#mc-side-panel-download .mc-table-scope'))).not.toBeNull();

    // The names follow the format select.
    select.value = 'csv';
    select.dispatchEvent(new Event('change'));
    refresh();
    expect(component.tableFormat).toBe('csv');
    expect(component.downloadTableName).toBe('Download the table as CSV');
    expect(tableActions().copy.getAttribute('aria-label')).toBe('Copy the table as CSV');

    // The suite and the pricing basis are in the wizard header; the meta line carries only the
    // computation time and the order.
    expect(textOf('.mc-table-computed')).toContain('Computed');
    expect(textOf('.mc-table-computed')).not.toContain('Current catalog');
    expect(component.tableProvenance.conditionSignature).toBe('9c79137965e4');
    // The inline export toolbar and the column dialog are gone.
    expect(fixture.debugElement.query(By.css('#mc-table-export-format'))).toBeNull();
    expect(fixture.debugElement.query(By.css('dialog:not(.mc-about-dialog)'))).toBeNull();
  });

  it('puts Copy table and Download table on the Comparison table row of both table views, each with an interest tooltip', () => {
    renderTable(buildDto(comparableSet(4)));

    const { copy, download } = tableActions();
    expect(copy.closest('.mc-table-toolbar')).not.toBeNull();
    expect(copy.classList).toContain('action-btn');
    expect(copy.getAttribute('aria-label')).toBe('Copy the table as cells for Excel');
    expect(download.classList).toContain('action-btn');
    expect(download.getAttribute('aria-label')).toBe(component.downloadTableName);
    expect(download.textContent?.trim()).toBe('');
    for (const button of [copy, download]) {
      expect(button.getAttribute('type')).toBe('button');
      expect(button.hasAttribute('title')).toBeFalse();
      const tipId = button.getAttribute('interestfor');
      expect(tipId).toBeTruthy();
      expect(button.getAttribute('style') ?? '').toMatch(new RegExp(`anchor-name:\\s*--${tipId}`));
      const tip = fixture.nativeElement.querySelector(`#${tipId}`) as HTMLElement;
      expect(tip.getAttribute('popover')).toBe('hint');
      expect(tip.getAttribute('style') ?? '').toMatch(new RegExp(`position-anchor:\\s*--${tipId}`));
    }
    expect(textOf('#mc-tip-table-copy')).toBe('Copy the table as cells for Excel');
    expect(textOf('#mc-tip-table-download')).toBe('Download the table as Excel (.xlsx)');
    expect(fixture.debugElement.query(By.css('.mc-all-download-all'))).toBeNull();
    expect(fixture.debugElement.query(By.css('.mc-fig-actions'))).toBeNull();

    showView('tablePreview');
    expect(tableActions().download.closest('#mc-fig-panel-tablePreview .mc-preview-toolbar')).not.toBeNull();
    expect(fixture.debugElement.query(By.css('.mc-fig-actions'))).toBeNull();

    showView('all');
    expect(fixture.debugElement.query(By.css('.mc-table-copy'))).toBeNull();
    expect(fixture.debugElement.query(By.css('.mc-fig-actions'))).toBeNull();
    expect(fixture.debugElement.query(By.css('.mc-all-toolbar .mc-all-download-all'))).not.toBeNull();

    showView('single');
    expect(fixture.debugElement.query(By.css('.mc-fig-actions'))).toBeNull();
  });

  it('opens the Interactive table on one compact header row and one meta line', () => {
    renderTable(buildDto(comparableSet(4)));
    const panel = fixture.debugElement.query(By.css('#mc-fig-panel-table')).nativeElement as HTMLElement;

    const toolbar = panel.querySelector('.mc-table-toolbar')!;
    expect(toolbar).not.toBeNull();
    expect(toolbar.querySelector('#mc-table-heading')?.textContent?.trim()).toBe('Comparison table');
    const tip = toolbar.querySelector('app-info-tip')!;
    expect(tip).not.toBeNull();
    expect(panel.querySelector('#mc-table-about-tip')?.textContent)
      .toContain('Every entry is listed here, charted or not. Point at a State badge to see why.');
    expect(panel.querySelector('table.mc-table')?.getAttribute('aria-describedby')).toBe('mc-table-about-tip');
    expect(toolbar.querySelector('.mc-table-export .mc-table-copy')).not.toBeNull();

    // The removed paragraphs: the lead note is in the tip, the download scope in the Download tab.
    const visibleText = Array.from(panel.querySelectorAll('p')).map(p => p.textContent ?? '').join(' ');
    expect(visibleText).not.toContain('Every entry is listed here');
    expect(panel.textContent).not.toContain('A download holds every row');
    expect(panel.querySelector('.mc-section-intro')).toBeNull();

    const meta = panel.querySelector('.mc-table-meta')!;
    expect(meta.querySelector('.mc-table-computed')?.textContent).toContain('Computed');
    expect(meta.querySelector('.mc-table-order-line')?.getAttribute('role')).toBe('status');
    expect(meta.querySelector('.mc-table-order-line')?.textContent).toContain('Rows follow the model order');

    headerButton('State').click();
    fixture.detectChanges();
    const useModelOrder = meta.querySelector<HTMLButtonElement>('.mc-use-model-order')!;
    expect(useModelOrder).not.toBeNull();
    expect(useModelOrder.classList).toContain('gh-filter-clear');
  });

  it('names Copy table after what it writes, in every format', () => {
    renderTable(buildDto(comparableSet(3)));
    const names: Record<TableFileFormat, string> = {
      xlsx: 'Copy the table as cells for Excel',
      csv: 'Copy the table as CSV',
      tsv: 'Copy the table as TSV',
      md: 'Copy the table as Markdown',
      json: 'Copy the table as JSON',
      html: 'Copy the table as a formatted table',
      image: 'Copy the table as an image'
    };
    for (const format of Object.keys(names) as TableFileFormat[]) {
      chooseTableFormat(format);
      expect(tableActions().copy.getAttribute('aria-label')).withContext(format).toBe(names[format]);
    }
    component.onExportFormatChange('webp');
    refresh();
    expect(tableActions().copy.getAttribute('aria-label')).toBe('Copy the table as an image (copied as PNG)');
  });

  it('marks both table actions aria-disabled, not disabled, with no entries, and both refuse', async () => {
    renderTable(buildDto([]));
    const saved = captureSaves();

    const { copy, download } = tableActions();
    for (const button of [copy, download]) {
      expect(button.getAttribute('aria-disabled')).toBe('true');
      expect(button.disabled).toBeFalse();
    }
    expect(component.downloadTableTooltip).toContain('Nothing to export');

    await component.downloadTable();
    await component.copyTable();
    expect(saved.names.length).toBe(0);
    expect(component.exportStatus).toBe('');
  });

  it('downloads in one click, through one write path, with no column dialog', async () => {
    renderTable(buildDto(comparableSet(3)));
    const download = spyOn(component, 'downloadTable').and.returnValue(Promise.resolve());
    const copy = spyOn(component, 'copyTable').and.returnValue(Promise.resolve());

    tableActions().download.click();
    tableActions().copy.click();

    expect(download).toHaveBeenCalledTimes(1);
    expect(copy).toHaveBeenCalledTimes(1);
    expect(fixture.debugElement.query(By.css('dialog:not(.mc-about-dialog)'))).toBeNull();
  });

  it('writes one file per table format, under the extension that format names', async () => {
    renderTable(buildDto(comparableSet(4)));
    const saved = captureSaves();
    stubXlsxWriter();

    for (const format of ['xlsx', 'csv', 'tsv', 'md', 'json', 'html'] as TableFileFormat[]) {
      chooseTableFormat(format);
      await component.downloadTable();
    }

    expect(saved.blobs.length).toBe(6);
    expect(saved.names.map(name => name.split('.').pop()))
      .toEqual(['xlsx', 'csv', 'tsv', 'md', 'json', 'html']);
    expect(saved.names.every(name => /^model-comparison_table_\d{8}_\d{6}\./.test(name))).toBeTrue();
    expect(saved.blobs.every(blob => blob.size > 0)).toBeTrue();
    expect(component.exportStatus).toContain('4 entries');
    expect(component.exportStatus)
      .toContain('current order (model order: Intelligence Index, descending), filters applied, all pages');
    expect(component.exporting).toBeFalse();
  });

  it('writes a data format split into parts, and a reading format combined as on screen', async () => {
    renderTable(buildDto(comparableSet(3)));
    const saved = captureSaves();

    chooseTableFormat('csv');
    await component.downloadTable();
    const header = (await saved.blobs[0].text()).split('\r\n')[0];
    // The Timings column is written as its four typed parts; a hidden column is not written.
    expect(header).toContain('Model time mean ms');
    expect(header).toContain('Suite total ms');
    expect(header).toContain('TTFT P90 ms');
    expect(header).not.toContain('Timings');
    expect(header).not.toContain('Model id');
    expect(component.exportStatus).toMatch(/9 columns, written as \d+\./);
    expect(component.tableScopeLine).toMatch(/^3 entries \(filters applied, current order, all pages\) · 9 columns, written as \d+$/);

    chooseTableFormat('md');
    await component.downloadTable();
    const markdown = await saved.blobs[1].text();
    expect(markdown).toContain('Timings');
    expect(markdown).not.toContain('Model time mean ms');
    expect(component.exportStatus).toContain('9 columns.');
    expect(component.tableScopeLine).toBe('3 entries (filters applied, current order, all pages) · 9 columns');
  });

  it('writes the table image in the shared image format at the table image size, naming its pixels', async () => {
    renderTable(buildDto(comparableSet(2)));
    const saved = captureSaves();

    chooseTableFormat('image');
    await component.downloadTable();
    component.onExportFormatChange('webp');
    await component.downloadTable();

    expect(saved.names[0]).toMatch(/\.png$/);
    // A browser with no WebP encoder answers with a PNG, and the file is then named .png.
    expect(saved.names[1]).toMatch(/\.(webp|png)$/);
    expect(saved.blobs.every(blob => blob.size > 0)).toBeTrue();
    expect(component.exportStatus).toMatch(/ at \d+ × \d+ px\./);
  });

  it('refuses a table image size the table does not fit, disabling both actions with the reason', async () => {
    renderTable(buildDto(comparableSet(3)));
    const saved = captureSaves();
    chooseTableFormat('image');
    component.onTableImageSizeChange({ ...component.tableImageSize, resolutionId: 'custom', customWidthPx: 400, customHeightPx: 320 });
    await component.measureTableImageNow();
    refresh();

    expect(component.tableImageRefusal).toMatch(/need at least \d+ px/);
    expect(component.canExportTable).toBeFalse();
    const { copy, download } = tableActions();
    expect(copy.getAttribute('aria-disabled')).toBe('true');
    expect(download.getAttribute('aria-disabled')).toBe('true');
    expect(textOf('#mc-tip-table-download')).toBe(component.tableImageRefusal);
    openSidebarTab('download');
    expect(textOf('#mc-side-panel-download .mc-export-error')).toContain(component.tableImageRefusal);

    await component.downloadTable();
    expect(saved.names.length).toBe(0);

    // Any other format writes whatever the image size says.
    chooseTableFormat('csv');
    expect(component.canExportTable).toBeTrue();
  });

  it('copies cells with an HTML fallback for Excel, a formatted table for HTML, and text for Markdown', async () => {
    renderTable(buildDto(comparableSet(3)));
    const written: ClipboardItem[] = [];
    withClipboard({ write: (items: ClipboardItem[]) => { written.push(...items); return Promise.resolve(); } });

    await component.copyTable();
    expect(written.length).toBe(1);
    expect([...written[0].types].sort()).toEqual(['text/html', 'text/plain']);
    expect(await (await written[0].getType('text/plain')).text()).toContain('\t');
    expect(component.exportStatus).toBe('Copied 3 entries as cells for Excel — current order (model order: ' +
      'Intelligence Index, descending), filters applied, all pages.');

    chooseTableFormat('html');
    await component.copyTable();
    expect([...written[1].types].sort()).toEqual(['text/html', 'text/plain']);
    expect(await (await written[1].getType('text/html')).text()).toContain('<table');

    chooseTableFormat('md');
    await component.copyTable();
    expect(written[2].types).toEqual(['text/plain']);
    expect(await (await written[2].getType('text/plain')).text()).toContain('| Model |');
    expect(component.exportStatus).toContain('Copied 3 entries as Markdown');

    // CSV and TSV paste without the byte-order mark their files carry.
    chooseTableFormat('csv');
    await component.copyTable();
    expect((await (await written[3].getType('text/plain')).text()).startsWith('﻿')).toBeFalse();
    expect(component.exporting).toBeFalse();
  });

  it('falls back to writeText for a text payload where only it exists', async () => {
    renderTable(buildDto(comparableSet(3)));
    const writeText = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
    withClipboard({ writeText });

    chooseTableFormat('md');
    await component.copyTable();

    expect(writeText).toHaveBeenCalledTimes(1);
    expect(writeText.calls.mostRecent().args[0] as string).toContain('| Model |');
    expect(component.exportStatus).toContain('Copied 3 entries as Markdown');
  });

  it('copies the table image as a PNG, WebP chosen or not', async () => {
    renderTable(buildDto(comparableSet(2)));
    const written: ClipboardItem[] = [];
    withClipboard({ write: (items: ClipboardItem[]) => { written.push(...items); return Promise.resolve(); } });
    chooseTableFormat('image');
    component.onExportFormatChange('webp');

    await component.copyTable();

    expect(written.length).toBe(1);
    expect(written[0].types).toEqual(['image/png']);
    expect(component.exportStatus).toMatch(/^Copied 2 entries as an image \(PNG, \d+ × \d+ px\)/);

    withClipboard(undefined);
    await component.copyTable();
    expect(component.exportStatus).toBe('This browser cannot copy images — download the table instead.');
  });

  it('reports a refused clipboard write and an absent clipboard API inline rather than throwing', async () => {
    renderTable(buildDto(comparableSet(3)));
    withClipboard({
      write: () => Promise.reject(new Error('Document is not focused.')),
      writeText: () => Promise.reject(new Error('Document is not focused.'))
    });

    await expectAsync(component.copyTable()).toBeResolved();
    expect(component.exportStatus).toBe('The clipboard write was refused.');

    withClipboard(undefined);
    await component.copyTable();
    expect(component.exportStatus).toContain('cannot copy text to the clipboard');
    expect(component.exportStatus).toContain('download the table instead');
    expect(component.exporting).toBeFalse();
  });

  it('exports every filtered row across all pages, not the visible page', async () => {
    renderTable(buildDto(comparableSet(12)));
    const saved = captureSaves();
    expect(component.entryTable.view(component.entries).length).toBe(10);

    chooseTableFormat('csv');
    await component.downloadTable();

    // Twelve records and a header, from a page showing ten.
    const text = await saved.blobs[0].text();
    expect(text.trimEnd().split('\r\n').length).toBe(13);
    expect(component.exportStatus).toContain('12 entries');
  });

  it('exports the rows the column filters leave, and says how many', async () => {
    renderTable(buildDto(comparableSet(12)));
    const saved = captureSaves();
    // Model 1, Model 10, Model 11 and Model 12.
    component.entryTable.setFilter('label', 'Model 1');
    component.onTableChanged();

    chooseTableFormat('csv');
    await component.downloadTable();

    expect((await saved.blobs[0].text()).trimEnd().split('\r\n').length).toBe(5);
    expect(component.exportStatus).toContain('4 entries');
  });

  it('remembers the table format, the image format and the WebP quality per browser, and repairs a bad record', () => {
    localStorage.setItem(DOWNLOAD_SETTINGS_STORAGE_KEY, JSON.stringify({
      version: 1, tableFormat: 'md', imageFormat: 'webp', webpQuality: 90
    }));
    let stored = TestBed.createComponent(ModelComparisonComponent);
    expect(stored.componentInstance.tableFormat).toBe('md');
    expect(stored.componentInstance.exportFormat).toBe('webp');
    expect(stored.componentInstance.webpQuality).toBe(90);
    stored.destroy();

    localStorage.setItem(DOWNLOAD_SETTINGS_STORAGE_KEY, JSON.stringify({
      version: 1, tableFormat: 'png', imageFormat: 'gif', webpQuality: 42
    }));
    stored = TestBed.createComponent(ModelComparisonComponent);
    expect(stored.componentInstance.tableFormat).toBe('xlsx');
    expect(stored.componentInstance.exportFormat).toBe('png');
    expect(stored.componentInstance.webpQuality).toBe(85);
    stored.destroy();

    localStorage.setItem(DOWNLOAD_SETTINGS_STORAGE_KEY, '{not json');
    stored = TestBed.createComponent(ModelComparisonComponent);
    expect(stored.componentInstance.tableFormat).toBe('xlsx');
    stored.destroy();

    component.onTableFormatChange('json');
    component.onWebpQualityChange(95);
    expect(JSON.parse(localStorage.getItem(DOWNLOAD_SETTINGS_STORAGE_KEY)!))
      .toEqual({ version: 1, tableFormat: 'json', imageFormat: 'png', webpQuality: 95 });
  });

  it('shows the table image size and the image format for Image, and always on the Table preview', async () => {
    renderTable(buildDto(comparableSet(3)));
    openSidebarTab('download');
    const shown = (): boolean[] => [
      fixture.debugElement.query(By.css('#mc-side-panel-download app-export-size-section')) !== null,
      fixture.debugElement.query(By.css('#mc-side-panel-download #mc-image-format-section')) !== null
    ];
    expect(shown()).toEqual([false, false]);

    chooseTableFormat('image');
    expect(shown()).toEqual([true, true]);
    const sizes = Array.from((fixture.debugElement.query(By.css('#mc-table-image-resolution'))
      .nativeElement as HTMLSelectElement).options).map(option => option.value);
    expect(sizes[0]).toBe('fit');

    chooseTableFormat('xlsx');
    showView('tablePreview');
    expect(component.effectiveSidebarTab).toBe('download');
    expect(shown()).toEqual([true, true]);
    // The image format here is the one the charts are written in.
    component.onExportFormatChange('webp');
    showView('all');
    refresh();
    await fixture.whenStable();
    expect((fixture.debugElement.query(By.css('#mc-export-format')).nativeElement as HTMLSelectElement).value).toBe('webp');
  });

  it('shows the Fit the table information in custom mode only, and updates it with the columns', async () => {
    renderTable(buildDto(comparableSet(3)));
    openSidebarTab('download');
    chooseTableFormat('image');

    // Fit the table is the default, where the size is the table's own and the written size says so.
    await component.measureTableImageNow();
    expect(component.tableFitInfo).toBe('');
    expect(component.tableImageWrittenLabel).toMatch(/^\d+ × \d+ px — the whole table at 200%$/);

    component.onTableImageSizeChange({ ...component.tableImageSize, resolutionId: 'custom' });
    await component.measureTableImageNow();
    refresh();
    const info = /^Fit the table: (\d+) × (\d+) px at this text size — the whole table with its (\d+) shown columns and 3 rows\.$/
      .exec(component.tableFitInfo);
    expect(info).withContext(component.tableFitInfo).not.toBeNull();
    expect(info![3]).toBe('9');
    expect(textOf('#mc-table-image-fit-info')).toBe(component.tableFitInfo);

    component.onTableColumnsChange(columnsWith({ hide: ['notes', 'timings'] }));
    await component.measureTableImageNow();
    const narrower = /^Fit the table: (\d+) × (\d+) px/.exec(component.tableFitInfo)!;
    expect(component.tableFitInfo).toContain('its 7 shown columns');
    expect(Number(narrower[1])).toBeLessThan(Number(info![1]));

    // A preset names its minimum in its refusal instead.
    component.onTableImageSizeChange({ ...component.tableImageSize, resolutionId: 'fullhd' });
    await component.measureTableImageNow();
    expect(component.tableFitInfo).toBe('');
  });

  it('keeps the chart size and the table image size apart, each stored under its own key', () => {
    render(buildDto(comparableSet(3)), 2);
    const chart = component.figureSize;

    component.onTableImageSizeChange({ ...component.tableImageSize, resolutionId: 'custom', customWidthPx: 1000, customHeightPx: 800 });
    expect(component.figureSize).toEqual(chart);
    expect(JSON.parse(localStorage.getItem(TABLE_IMAGE_SIZE_STORAGE_KEY)!).customWidthPx).toBe(1000);
    expect(localStorage.getItem(FIGURE_SIZE_STORAGE_KEY)).toBeNull();

    component.onExportResolutionChange('hd');
    expect(component.tableImageSize.customWidthPx).toBe(1000);
    expect(component.tableImageSize.resolutionId).toBe('custom');
  });

  // -------------------------------------------------------------------------------------------
  // The table's rows follow the model order
  // -------------------------------------------------------------------------------------------

  it('orders the table by the model order by default, with no header sorted and a line naming it', () => {
    renderTable(buildDto(comparableSet(3)));

    expect(component.entryTable.sortColumn).toBe('modelOrder');
    expect(tableOrder()).toEqual(component.figures!.selection.plotted.map(entry => entry.key));
    expect(tableOrder()).toEqual(['run:3', 'run:2', 'run:1']);
    const sorted = fixture.debugElement.queryAll(By.css('table.mc-table thead th[aria-sort]'));
    expect(sorted.length).toBe(0);
    expect(textOf('.mc-table-order-line')).toContain('Rows follow the model order: Intelligence Index, descending');
    expect(fixture.debugElement.query(By.css('.mc-use-model-order'))).toBeNull();
  });

  it('sorts by a column header, says so, and returns to the model order with Use model order', () => {
    const entries = comparableSet(3);
    entries[1] = { ...entries[1], state: 'Degraded', speedDegraded: true, speedDegradingKeys: ['ParallelMode'] };
    renderTable(buildDto(entries));

    headerButton('State').click();
    refresh();
    expect(component.entryTable.sortColumn).toBe('stateCol');
    // State is one click away, and puts the entries a reader has to check first.
    expect(tableOrder()[0]).toBe('run:2');
    expect(textOf('.mc-table-order-line')).toContain('Rows sorted by State');

    // The Data tab says so too, beside the table.
    expect(textOf('#mc-side-panel-data .mc-table-order--side')).toContain('The table is sorted by State.');

    (fixture.debugElement.query(By.css('.mc-use-model-order')).nativeElement as HTMLButtonElement).click();
    refresh();
    expect(component.entryTable.sortColumn).toBe('modelOrder');
    expect(component.entryTable.sortDirection).toBe('asc');
    expect(tableOrder()).toEqual(['run:3', 'run:2', 'run:1']);
    expect(fixture.debugElement.query(By.css('#mc-side-panel-data .mc-table-order--side'))).toBeNull();
  });

  it('returns the table to the model order on every model-order change, and keeps charts and table in one order', () => {
    renderTable(buildDto(comparableSet(3)));

    headerButton('Candidate $ / question').click();
    refresh();
    expect(component.entryTable.sortColumn).toBe('cost');

    // The table views' Data tab carries the model order too.
    chooseRadio('mc-sort-key-cost');
    expect(component.sort.key).toBe('cost');
    expect(component.entryTable.sortColumn).toBe('modelOrder');
    expect(tableOrder()).toEqual(component.figures!.selection.plotted.map(entry => entry.key));

    headerButton('R').click();
    refresh();
    chooseRadio('mc-sort-direction-asc');
    expect(component.entryTable.sortColumn).toBe('modelOrder');
    expect(tableOrder()).toEqual(component.figures!.selection.plotted.map(entry => entry.key));
    expect(textOf('.mc-table-order-line')).toContain('Rows follow the model order: Cost, ascending');
  });

  it('names the order in the export toast: the model order, custom, or the sorted column', async () => {
    renderTable(buildDto(comparableSet(3)));
    captureSaves();
    chooseTableFormat('csv');

    headerButton('Candidate $ / question').click();
    refresh();
    await component.downloadTable();
    expect(component.exportStatus).toContain('current order (sorted by Candidate $ / question)');

    component.onSortKeyChange('custom');
    await component.downloadTable();
    expect(component.exportStatus).toContain('current order (model order: custom)');
  });

  // -------------------------------------------------------------------------------------------
  // The custom model order
  // -------------------------------------------------------------------------------------------

  it('seeds Custom from the order in effect, disables Direction, and keeps it across a switch of key', () => {
    render(buildDto(comparableSet(3)), 2);
    chooseRadio('mc-sort-direction-asc');
    expect(component.figures!.smallMultiples.order).toEqual(['run:1', 'run:2', 'run:3']);

    chooseRadio('mc-sort-key-custom');
    expect(component.sort.key).toBe('custom');
    expect(component.customOrder).toEqual(['run:1', 'run:2', 'run:3']);
    expect(component.figures!.smallMultiples.order).toEqual(['run:1', 'run:2', 'run:3']);
    const direction = fixture.debugElement.queryAll(By.css('#mc-side-panel-data fieldset.gh-choice'))
      .map(group => group.nativeElement as HTMLFieldSetElement)
      .find(group => group.querySelector('legend')?.textContent?.trim() === 'Direction')!;
    expect(direction.disabled).toBeTrue();
    expect(textOf('#mc-sort-direction-hint')).toContain('A custom order has no direction.');
    const list = fixture.debugElement.query(By.css('#mc-side-panel-data app-reorderable-list'));
    expect(list).toBeTruthy();
    expect((list.componentInstance as { items: readonly { key: string }[] }).items.map(item => item.key))
      .toEqual(['run:1', 'run:2', 'run:3']);

    component.onCustomOrderChange(['run:3', 'run:1', 'run:2']);
    chooseRadio('mc-sort-key-label');
    expect(component.sort.key).toBe('label');
    expect(fixture.debugElement.query(By.css('#mc-side-panel-data app-reorderable-list'))).toBeNull();
    chooseRadio('mc-sort-key-custom');
    expect(component.customOrder).toEqual(['run:3', 'run:1', 'run:2']);
    expect(component.figures!.smallMultiples.order).toEqual(['run:3', 'run:1', 'run:2']);
  });

  it('reorders the charts and the table from one custom move, and returns a column sort to the model order', () => {
    render(buildDto(comparableSet(3)), 2);
    chooseRadio('mc-sort-key-custom');
    showView('table');
    headerButton('Model').click();
    refresh();
    expect(component.entryTable.sortColumn).toBe('model');
    const rebuild = spyOn(component as unknown as { rebuild(): void }, 'rebuild').and.callThrough();

    // The Custom list is in the table views' Data tab too, with a Move button per row.
    const move = fixture.debugElement.queryAll(By.css('#mc-side-panel-data app-reorderable-list button'))
      .map(button => button.nativeElement as HTMLButtonElement)
      .find(button => button.getAttribute('aria-label') === 'Move Model 3 down');
    expect(move).withContext('Move Model 3 down').toBeTruthy();
    move!.click();
    refresh();

    expect(rebuild).toHaveBeenCalledTimes(1);
    expect(component.customOrder).toEqual(['run:2', 'run:3', 'run:1']);
    expect(component.figures!.smallMultiples.order).toEqual(['run:2', 'run:3', 'run:1']);
    expect(component.entryTable.sortColumn).toBe('modelOrder');
    expect(tableOrder()).toEqual(['run:2', 'run:3', 'run:1']);
    expect(textOf('.mc-table-order-line')).toContain('Rows follow the model order: custom');
  });

  it('resets the custom order to Intelligence Index, descending, and refuses while it already is', () => {
    render(buildDto(comparableSet(3)), 2);
    chooseRadio('mc-sort-key-custom');
    const reset = (): HTMLButtonElement => fixture.debugElement.queryAll(By.css('#mc-side-panel-data .mc-order-actions button'))
      .map(button => button.nativeElement as HTMLButtonElement)
      .find(button => button.textContent?.trim() === 'Reset custom order')!;
    expect(reset().getAttribute('aria-disabled')).toBe('true');

    component.onCustomOrderChange(['run:1', 'run:3', 'run:2']);
    refresh();
    expect(reset().getAttribute('aria-disabled')).toBeNull();

    reset().click();
    refresh();
    expect(component.customOrder).toEqual(['run:3', 'run:2', 'run:1']);
    expect(reset().getAttribute('aria-disabled')).toBe('true');
    reset().click();
    expect(component.customOrder).toEqual(['run:3', 'run:2', 'run:1']);
  });

  it('tags entries the charts never draw as table only, and draws the divider where the charts stop', () => {
    render(buildDto([...comparableSet(MAX_PLOTTED_ENTRIES + 1), buildExcludedEntry('run:99', ['ScoringMethodVersion'])]), 2);
    component.onSortKeyChange('custom');

    const tags = (key: string): readonly string[] =>
      component.customOrderItems.find(item => item.key === key)?.tags ?? [];
    expect(component.customOrderItems.length).toBe(MAX_PLOTTED_ENTRIES + 2);
    expect(tags('run:99')).toEqual(['table only']);
    expect(component.customOrderDividerIndex).toBe(MAX_PLOTTED_ENTRIES);

    component.toggleEntry('run:5');
    expect(tags('run:5')).toEqual(['table only']);
    // Now every chartable entry fits under the cap, so there is no line to draw.
    expect(component.customOrderDividerIndex).toBeNull();
  });

  it('keeps the custom order across a refetch, dropping gone entries and appending new ones by Intelligence Index', () => {
    const entries = comparableSet(4);
    render(buildDto(entries.slice(0, 3)), 2);
    component.onSortKeyChange('custom');
    component.onCustomOrderChange(['run:1', 'run:3', 'run:2']);

    fixture.componentRef.setInput('comparison', buildDto([entries[0], entries[1], entries[3]]));
    fixture.detectChanges();

    expect(component.customOrder).toEqual(['run:1', 'run:2', 'run:4']);
    expect(component.sort.customOrder).toEqual(['run:1', 'run:2', 'run:4']);
    expect(component.figures!.smallMultiples.order).toEqual(['run:1', 'run:2', 'run:4']);
  });

  // -------------------------------------------------------------------------------------------
  // The table's columns
  // -------------------------------------------------------------------------------------------

  it('renders today\'s table with the default columns', () => {
    const entries = comparableSet(2);
    entries[0] = { ...entries[0], state: 'Degraded', speedDegraded: true, speedDegradingKeys: ['ParallelMode'] };
    renderTable(buildDto(entries));

    expect(tableHeaders()).toEqual([
      'Model', 'R', 'State', 'Intelligence Index', 'Speed Index', 'Timings', 'Candidate $ / question',
      'Total $ / run, with grading', 'Notes'
    ]);
    expect(fixture.debugElement.queryAll(By.css('table.mc-table thead th[app-sort-header]')).length).toBe(8);
    expect(textOf('table.mc-table caption')).toContain(
      'Model, R, State, Intelligence Index, Speed Index, Timings, Candidate $ / question, Total $ / run, with grading, Notes');

    const filters = fixture.debugElement.queryAll(By.css('.gh-filter-row td'))
      .map(cell => cell.nativeElement as HTMLElement);
    expect(filters.length).toBe(9);
    expect(filters[0].querySelector('#mc-f-label')).toBeTruthy();
    expect(filters[2].querySelector('#mc-f-state')).toBeTruthy();

    const row = tableRowOf('Run 1');
    expect(row.children.length).toBe(9);
    expect(row.children[0].tagName).toBe('TH');
    expect(row.children[1].classList).toContain('col-center');
    expect(row.querySelector('.mc-state')?.getAttribute('interestfor')).toBeTruthy();
    expect(row.querySelector('.mc-degraded-axes')?.textContent).toContain('Degraded: speed');
    expect(row.querySelector('.mc-interval')?.textContent).toContain('± 6.4');
    expect(row.querySelector('.mc-basis-btn')).toBeTruthy();
    expect(row.querySelectorAll('.mc-timings > div').length).toBe(3);
    expect(row.querySelector('.mc-notes')?.textContent).toContain('Speed degraded by: ParallelMode');
    expect(row.querySelector('.mc-source')?.textContent?.trim()).toBe('Run 1');
  });

  it('hides, shows and moves columns from the one configuration, and stores it', () => {
    renderTable(buildDto(comparableSet(2)));

    const order = [...DEFAULT_TABLE_COLUMNS.order];
    order.splice(order.indexOf('cost'), 1);
    order.splice(order.indexOf('intelligence'), 0, 'cost');
    component.onTableColumnsChange(columnsWith({ order, show: ['intervalHalfWidth'], hide: ['notes'] }));
    refresh();

    expect(tableHeaders()).toEqual([
      'Model', 'R', 'State', 'Candidate $ / question', 'Intelligence Index', 'Speed Index', 'Timings',
      'Total $ / run, with grading', '±'
    ]);
    expect(JSON.parse(localStorage.getItem(TABLE_COLUMNS_STORAGE_KEY)!)).toEqual({
      version: 2, order, shown: component.tableColumns.shown
    });
    // The ± is its own column now, so the Intelligence Index cell stops printing it.
    const row = tableRowOf('Run 1');
    const intelligenceCell = row.children[4];
    expect(intelligenceCell.querySelector('.mc-nowrap')?.textContent?.trim()).toMatch(/^\d+\.\d$/);
    expect(intelligenceCell.querySelector('.mc-interval')).toBeNull();
    expect(row.lastElementChild?.textContent?.trim()).toBe('6.4');
    expect(fixture.debugElement.query(By.css('.mc-notes'))).toBeNull();

    // The Table tab edits the same configuration.
    openSidebarTab('table');
    const panel = fixture.debugElement.query(By.css('#mc-side-panel-table app-table-settings-panel'));
    expect(panel).toBeTruthy();
    expect((panel.componentInstance as { columns: unknown }).columns).toBe(component.tableColumns);
  });

  it('clears the filter of a column it hides, with a status line, and returns a sort on it to the model order', () => {
    const entries = comparableSet(3);
    entries[0] = { ...entries[0], state: 'Degraded', speedDegraded: true, speedDegradingKeys: ['ParallelMode'] };
    renderTable(buildDto(entries));
    component.entryTable.setFilter('state', 'Degraded');
    component.onTableChanged();
    headerButton('State').click();
    refresh();
    expect(component.entryTable.filteredCount(component.entries)).toBe(1);

    component.onTableColumnsChange(columnsWith({ hide: ['stateCol'] }));
    refresh();

    expect(component.entryTable.filters['state'] ?? '').toBe('');
    expect(component.entryTable.filteredCount(component.entries)).toBe(3);
    expect(component.entryTable.sortColumn).toBe('modelOrder');
    expect(component.tableColumnsStatus).toBe('The State filter was cleared because its column is hidden.');
    expect(fixture.debugElement.query(By.css('#mc-f-state'))).toBeNull();
    openSidebarTab('table');
    expect(textOf('.mc-columns-status')).toContain('The State filter was cleared because its column is hidden.');
  });

  it('leaves a part out of its combined cell while that part is shown as a column of its own', () => {
    const entries = comparableSet(2);
    entries[0] = { ...entries[0], table: { ...entries[0].table!, speedIndexSaturated: true } };
    renderTable(buildDto(entries));
    const combined = tableRowOf('Run 1');
    expect(combined.querySelector('.thinking-badge')).toBeTruthy();
    expect(combined.querySelector('.provider-badge')).toBeTruthy();
    expect(combined.querySelector('.mc-saturated')).toBeTruthy();

    component.onTableColumnsChange(columnsWith({
      show: ['thinkingLevel', 'provider', 'source', 'explanation', 'speedIndexSaturated', 'intervalBasis']
    }));
    refresh();

    const row = fixture.debugElement.queryAll(By.css('table.mc-table tbody tr'))
      .map(candidate => candidate.nativeElement as HTMLElement)
      .find(candidate => candidate.textContent?.includes('run:1') || candidate.textContent?.includes('Run 1'))!;
    expect(row).toBeTruthy();
    expect(row.querySelector('.thinking-badge')).toBeNull();
    expect(row.querySelector('.provider-badge')).toBeNull();
    expect(row.querySelector('.mc-source')).toBeNull();
    expect(row.querySelector('.mc-state')?.getAttribute('interestfor')).toBeNull();
    expect(row.querySelector('.mc-saturated')).toBeNull();
    expect(row.querySelector('.mc-basis-btn')).toBeNull();
    // The reasoning badge has no column of its own, so it never leaves.
    expect(tableHeaders()).toContain('Thinking level');
    expect(tableHeaders()).toContain('Explanation');
  });

  it('restores the column configuration from storage and repairs it', () => {
    localStorage.setItem(TABLE_COLUMNS_STORAGE_KEY, JSON.stringify({
      version: 2, order: ['cost', 'bogus', 'model', 'cost'], shown: ['cost', 'bogus']
    }));
    let stored = TestBed.createComponent(ModelComparisonComponent);
    const columns = stored.componentInstance.tableColumns;
    expect(columns.order.slice(0, 2)).toEqual(['cost', 'model']);
    expect(columns.order).not.toContain('bogus');
    expect(columns.order.length).toBe(DEFAULT_TABLE_COLUMNS.order.length);
    // Model is always shown, whatever was stored.
    expect(columns.shown).toEqual(['cost', 'model']);
    expect(stored.componentInstance.shownColumns.map(column => column.key)).toEqual(['cost', 'model']);
    stored.destroy();

    localStorage.setItem(TABLE_COLUMNS_STORAGE_KEY, '{not json');
    stored = TestBed.createComponent(ModelComparisonComponent);
    expect(stored.componentInstance.tableColumns).toEqual(DEFAULT_TABLE_COLUMNS);
    stored.destroy();
  });

  it('shows the total-cost column on load to an admin whose version-1 layout predates it', () => {
    localStorage.setItem(TABLE_COLUMNS_STORAGE_KEY, JSON.stringify({
      version: 1,
      order: DEFAULT_TABLE_COLUMNS.order.filter(key => key !== 'totalCost'),
      shown: DEFAULT_TABLE_COLUMNS.shown.filter(key => key !== 'totalCost')
    }));
    fixture.destroy();
    fixture = TestBed.createComponent(ModelComparisonComponent);
    component = fixture.componentInstance;

    renderTable(buildDto(comparableSet(2)));

    const headers = tableHeaders();
    expect(headers[headers.indexOf('Candidate $ / question') + 1]).toBe('Total $ / run, with grading');
  });

  it('renders the run total with its SD in the total-cost cell', () => {
    renderTable(buildDto(comparableSet(2)));

    const cell = tableRowOf('Run 1').children[tableHeaders().indexOf('Total $ / run, with grading')] as HTMLElement;
    expect(cell.querySelector('.mc-nowrap')?.textContent).toContain('$0.9876');
    expect(cell.querySelector('.mc-interval')?.textContent).toContain('± $0.0432');
    expect(cell.querySelector('.mc-badge')).toBeNull();
  });

  it('marks the total-cost cell Unavailable, with the reason in its tooltip, when the total is null', () => {
    renderTable(buildDto(setWithoutTotal(2, PRE_HARNESS_15)));

    const cell = tableRowOf('Run 2').children[tableHeaders().indexOf('Total $ / run, with grading')] as HTMLElement;
    expect(cell.querySelector('.mc-nowrap')?.textContent?.trim()).toBe('—');
    expect(cell.querySelector('.mc-interval')).toBeNull();
    const badge = cell.querySelector('.mc-badge') as HTMLElement;
    expect(badge.textContent?.trim()).toBe('Unavailable');
    const tip = cell.querySelector(`#${badge.getAttribute('interestfor')}`) as HTMLElement;
    expect(tip.textContent).toContain('harness 15');
  });

  it('computes the Columns empty set from the rows passing the filters, not in the template', () => {
    const entries = comparableSet(2);
    entries[1] = { ...entries[1], cost: { ...entries[1].cost!, scheduledChangeEffectiveFrom: '2026-10-01' } };
    renderTable(buildDto(entries));
    expect(component.columnEmptyKeys.has('scheduledChange')).toBeFalse();
    expect(component.columnEmptyKeys.has('differsOn')).toBeTrue();

    // Model 1 alone has no scheduled change.
    component.entryTable.setFilter('label', 'Model 1');
    component.onTableChanged();
    expect(component.columnEmptyKeys.has('scheduledChange')).toBeTrue();

    openSidebarTab('table');
    const panel = fixture.debugElement.query(By.css('app-table-settings-panel'));
    expect((panel.componentInstance as { empty: unknown }).empty).toBe(component.columnEmptyKeys);
  });

  // -------------------------------------------------------------------------------------------
  // The sidebar's tab sets, and the Table preview
  // -------------------------------------------------------------------------------------------

  it('shows Table in place of Charts beside the table, keeps a shared tab across a switch, and swaps Charts and Table', () => {
    render(buildDto(comparableSet(3)), 2);
    const labels = (): string[] => fixture.debugElement.queryAll(By.css('.mc-fig-sidebar-tabs [role="tab"]'))
      .map(tab => (tab.nativeElement as HTMLElement).textContent?.trim() ?? '');
    expect(labels()).toEqual(['Data', 'Theme', 'Charts', 'Download']);

    openSidebarTab('charts');
    showView('table');
    expect(labels()).toEqual(['Data', 'Theme', 'Table', 'Download']);
    expect(component.effectiveSidebarTab).toBe('table');
    expect(fixture.debugElement.query(By.css('#mc-side-panel-table'))).toBeTruthy();

    // Within one group the tab never changes.
    showView('tablePreview');
    expect(component.effectiveSidebarTab).toBe('table');
    showView('all');
    expect(component.effectiveSidebarTab).toBe('charts');
    showView('single');
    expect(component.effectiveSidebarTab).toBe('charts');

    for (const shared of ['data', 'theme', 'download'] as FigureSidebarTab[]) {
      openSidebarTab(shared);
      showView('table');
      expect(component.effectiveSidebarTab).withContext(shared).toBe(shared);
      showView('all');
      expect(component.effectiveSidebarTab).withContext(shared).toBe(shared);
    }
  });

  it('runs the roving tabindex over the table views\' tabs', () => {
    renderTable(buildDto(comparableSet(3)));

    expectTabContract('.mc-fig-sidebar-tabs', 'Settings sections', 'mc-side-tab-', 'mc-side-panel-',
      ['Data', 'Theme', 'Table', 'Download'], () => component.effectiveSidebarTab);
  });

  it('migrates stored sidebar tabs: Emphasis to Data, Style to Charts, Export to Download', () => {
    const storedTab = (tab: string): string => {
      localStorage.setItem(FIGURE_SIDEBAR_STORAGE_KEY, JSON.stringify({ version: 1, collapsed: false, tab, view: 'all' }));
      const stored = TestBed.createComponent(ModelComparisonComponent);
      const result = stored.componentInstance.sidebarTab;
      stored.destroy();
      return result;
    };
    expect(storedTab('emphasis')).toBe('data');
    expect(storedTab('style')).toBe('charts');
    expect(storedTab('export')).toBe('download');
    expect(storedTab('download')).toBe('download');
    expect(storedTab('theme')).toBe('theme');
    expect(storedTab('gallery')).toBe('data');
    // A stored Table opens as Charts beside the charts, and Charts as Table beside the table.
    expect(sidebarTabForView('table', 'all')).toBe('charts');
    expect(sidebarTabForView('charts', 'tablePreview')).toBe('table');
    expect(sidebarTabForView('download', 'table')).toBe('download');

    localStorage.setItem(FIGURE_SIDEBAR_STORAGE_KEY, JSON.stringify({ version: 1, collapsed: false, tab: 'table', view: 'all' }));
    fixture = TestBed.createComponent(ModelComparisonComponent);
    component = fixture.componentInstance;
    render(buildDto(comparableSet(3)), 2);
    expect(component.effectiveSidebarTab).toBe('charts');
    expect(fixture.debugElement.query(By.css('#mc-side-panel-charts'))).toBeTruthy();
  });

  it('shows the table image on the Table preview stage, with its own view state and its pixel size', async () => {
    render(buildDto(comparableSet(3)), 2);
    openSingle(component.panelCards[0]);
    component.setPreviewView(2);
    expect(component.previewView).toBe(2);

    showView('tablePreview');
    expect(component.previewView).withContext('it opens at Fit to screen').toBe('fitScreen');
    expect(textOf('#mc-fig-panel-tablePreview .mc-preview-label')).toBe('Comparison table');
    expect(fixture.debugElement.query(By.css('#mc-fig-panel-tablePreview .mc-preview-export'))).toBeNull();
    // The row is label, zoom, then the table's own export group at its end.
    const row = fixture.debugElement.query(By.css('#mc-fig-panel-tablePreview .mc-preview-toolbar')).nativeElement as HTMLElement;
    expect(Array.from(row.children).filter(child => !child.hasAttribute('popover'))
      .map(child => child.classList.contains('mc-preview-label') ? 'label'
      : child.classList.contains('mc-preview-zoom') ? 'zoom'
        : child.classList.contains('mc-table-export') ? 'export' : child.tagName))
      .toEqual(['label', 'zoom', 'export']);
    expect(fixture.debugElement.query(By.css('#mc-preview-figure'))).toBeNull();
    // Karma lays the stage out at no size, so the fit is given one, as the Single chart specs do.
    spyOn(component, 'measureStage').and.returnValue({ width: 800, height: 600, devicePixelRatio: 2 });
    await composePreview();

    expect(component.tablePreviewPixels).toMatch(/^\d+ × \d+ px$/);
    expect(textOf('.mc-table-preview-size')).toBe(component.tablePreviewPixels);
    const canvas = fixture.debugElement.query(By.css('#mc-fig-panel-tablePreview canvas.mc-preview-canvas'))
      .nativeElement as HTMLCanvasElement;
    expect(canvas.getAttribute('role')).toBe('img');
    expect(canvas.getAttribute('aria-label')).toBe('Table preview: 3 entries, 9 columns');
    expect(canvas.width).toBeGreaterThan(0);
    expect(textOf('.mc-table-preview-notes')).toContain(
      'Previewing the image export. The chosen format, Excel (.xlsx), has no appearance of its own.');

    chooseTableFormat('image');
    expect(textOf('.mc-table-preview-notes')).not.toContain('Previewing the image export');

    // Back to Single: its own zoom, and the table's bitmap released.
    showView('single');
    expect(component.previewView).toBe(2);
    expect(component.tablePreviewPixels).toBe('');
  });

  it('shows a refused table image size under the Table preview stage instead of the image', async () => {
    render(buildDto(comparableSet(3)), 2);
    showView('tablePreview');
    component.onTableImageSizeChange({ ...component.tableImageSize, resolutionId: 'custom', customWidthPx: 400, customHeightPx: 320 });
    await composePreview();

    expect(component.previewRefusal).toMatch(/need at least \d+ px/);
    expect(textOf('.mc-table-preview-notes [role="alert"]')).toBe(component.previewRefusal);
    expect(component.tablePreviewPixels).toBe('');
  });

  // -------------------------------------------------------------------------------------------
  // The Theme tab
  // -------------------------------------------------------------------------------------------

  it('hosts the appearance panel in the Theme tab of both view groups', () => {
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('theme');
    const panel = (): { kind: string; fontLoadStatus: string; figureStyle: FigureStyle } =>
      fixture.debugElement.query(By.css('#mc-side-panel-theme app-figure-style-panel')).componentInstance;
    expect(panel().kind).toBe('appearance');
    expect(panel().figureStyle).toBe(component.figureStyle);

    showView('table');
    expect(panel().kind).toBe('appearance');
  });

  it('resolves the theme once and hands it to every chart\'s chrome, and paints a transparent background\'s backdrop on screen only', () => {
    render(buildDto(comparableSet(3)), 2);
    expect(component.figureTheme.name).toBe('dark');
    expect(fixture.debugElement.queryAll(By.css('.mc-all-canvas.is-transparent-figure')).length).toBe(0);

    withStyleDebounce(() => component.onFigureStyleChange({
      ...component.figureStyle,
      appearance: {
        ...component.figureStyle.appearance,
        theme: 'light', background: 'transparent', previewBackdrop: 'color', previewBackdropColor: '#123456'
      }
    }));

    expect(component.figureTheme.name).toBe('light');
    expect(component.figureTheme.background).toBeNull();
    const chrome = (component as unknown as { exportChrome(card: ComparisonFigureCard): { theme?: unknown } })
      .exportChrome(component.panelCards[0]);
    expect(chrome.theme).toBe(component.figureTheme);
    expect(fixture.debugElement.queryAll(By.css('.mc-all-canvas.is-transparent-figure')).length).toBe(7);
    const viewport = fixture.debugElement.query(By.css('.mc-all-viewport')).nativeElement as HTMLElement;
    expect(viewport.getAttribute('style') ?? '').toContain('--mc-figure-backdrop: #123456');
  });

  it('copies one figure to the clipboard and names the card in the status', async () => {
    render(buildDto(comparableSet(3)), 2);
    const write = jasmine.createSpy('write').and.returnValue(Promise.resolve());
    withClipboard({ write });
    const card = component.panelCards[0];

    await component.copyFigure(card);

    expect(write).toHaveBeenCalledTimes(1);
    expect(component.exportStatus).toBe(`Copied ${card.title} to the clipboard.`);
    expect(component.exporting).toBeFalse();
  });

  it('reports a refused figure copy, and an absent clipboard API, as inline text', async () => {
    render(buildDto(comparableSet(3)), 2);
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
    render(buildDto(comparableSet(3)), 2);
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
    render(buildDto(comparableSet(3)), 2);
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

  it('composes each figure at its own family\'s caption sizes, and empties the footer where it is hidden', () => {
    render(buildDto(comparableSet(3)), 2);
    const exportChrome = (card: ComparisonFigureCard) => (component as unknown as {
      exportChrome(card: ComparisonFigureCard): { footer: FigureFooter; textSizes?: { titlePx: number; badgePx: number; footerPx: number } };
    }).exportChrome(card);

    expect(exportChrome(component.panelCards[0]).textSizes).toEqual({ titlePx: 18, badgePx: 11, footerPx: 12 });

    component.figureStyle = {
      bar: { ...DEFAULT_FIGURE_STYLE.bar, titleSizePx: 30, badgeTextSizePx: 14, footerTextSizePx: 16 },
      scatter: { ...DEFAULT_FIGURE_STYLE.scatter, footer: false },
      profile: { ...DEFAULT_FIGURE_STYLE.profile, titleSizePx: 22 },
      numbers: DEFAULT_FIGURE_STYLE.numbers,
      appearance: DEFAULT_FIGURE_STYLE.appearance,
      table: DEFAULT_FIGURE_STYLE.table
    };

    const bar = exportChrome(component.panelCards[1]);
    expect(bar.textSizes).toEqual({ titlePx: 30, badgePx: 14, footerPx: 16 });
    expect(bar.footer.suite).toBe('GnollHack Player Assistance Benchmark Suite');

    const scatter = exportChrome(component.scatterCards[0]);
    expect(scatter.textSizes).toEqual({ titlePx: 18, badgePx: 11, footerPx: 12 });
    expect(scatter.footer).toEqual({ suite: '', computedAt: '' });

    expect(exportChrome(component.profileCard!).textSizes!.titlePx).toBe(22);
  });

  it('writes one image and no archive for a single figure from the preview', async () => {
    render(buildDto(comparableSet(3)), 2);
    const saved = captureSaves();
    const zip = stubZipWriter();
    openSingle(component.panelCards[0]);

    await component.downloadPreviewedFigure();

    expect(saved.names.length).toBe(1);
    expect(saved.names[0]).toMatch(/^model-comparison_.+\.(png|webp)$/);
    expect(zip.load).not.toHaveBeenCalled();
    expect(zip.names.length).toBe(0);
  });

  it('announces a written batch as a success naming the archive', async () => {
    render(buildDto(comparableSet(3)), 2);
    captureSaves();
    stubZipWriter();

    await component.downloadAllFigures();

    expect(component.exportNotice?.kind).toBe('success');
    expect(component.exportNotice?.message).toContain('saved to model-comparison_figures_');
    expect(component.exporting).toBeFalse();
  });

  it('announces a wholly refused batch as an error, and writes nothing', async () => {
    render(buildDto(comparableSet(3)), 2);
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

  it('lays the preview toolbar out as three labelled groups of tooltipped icon buttons', () => {
    render(buildDto(comparableSet(3)), 2);
    openSingle();

    const toolbar = fixture.debugElement.query(By.css('.mc-preview-toolbar')).nativeElement as HTMLElement;
    const style = getComputedStyle(toolbar);
    expect(style.display).toBe('flex');
    expect(style.columnGap).not.toBe('0px');
    expect(style.columnGap).not.toBe('normal');
    // Groups, not a toolbar: a toolbar's arrow-key model would fight the slider's.
    expect(toolbar.getAttribute('role')).toBeNull();
    const groups = Array.from(toolbar.querySelectorAll(':scope > [role="group"]'));
    expect(groups.map(group => group.getAttribute('aria-label')))
      .toEqual(['Figure', 'Preview zoom', 'Export this figure']);

    for (const name of ['Fit to screen', 'Actual pixels, 100 percent', 'Reset view']) {
      const button = zoomButton(name);
      expect(button.textContent!.trim()).withContext(name).toBe('');
      expect(button.classList).toContain('action-btn');
      const tipId = button.getAttribute('interestfor');
      expect(tipId).withContext(name).toBeTruthy();
      const tip = (fixture.nativeElement as HTMLElement).querySelector(`#${tipId}`);
      expect(tip?.getAttribute('popover')).withContext(name).toBe('hint');
      expect(button.getAttribute('style')).toContain(`anchor-name: --${tipId}`);
      expect(tip?.getAttribute('style')).toContain(`position-anchor: --${tipId}`);
    }

    // Every icon-only button in the workspace has a name and a tooltip, and none uses `title`.
    const step = fixture.debugElement.query(By.css('#mc-step-panel-2')).nativeElement as HTMLElement;
    for (const button of Array.from(step.querySelectorAll<HTMLButtonElement>('button.action-btn'))) {
      expect(button.getAttribute('aria-label')).withContext(button.outerHTML).toBeTruthy();
      expect(button.getAttribute('interestfor')).withContext(button.outerHTML).toBeTruthy();
      expect(button.getAttribute('type')).toBe('button');
    }
    expect(step.querySelectorAll('[title]').length).toBe(0);

    // Every control on the row is an icon button; the figure's Download is one too.
    expect(toolbar.querySelectorAll('.btn-gh').length).toBe(0);
    expect(toolbar.querySelector('.mc-preview-download')?.classList).toContain('action-btn');
  });

  it('hands the notice to its one toast', async () => {
    render(buildDto(comparableSet(3)), 2);
    withClipboard({ write: () => Promise.resolve() });

    await component.copyFigure(component.panelCards[0]);
    refresh();

    expect(fixture.debugElement.queryAll(By.css('app-toast')).length).toBe(1);
    const toast = fixture.debugElement.query(By.directive(ToastComponent)).componentInstance as ToastComponent;
    expect(toast.notice).toBe(component.exportNotice);

    // The Single tab is not a modal, so the same toast still carries it there.
    openSingle();
    expect(toast.notice).toBe(component.exportNotice);
  });

  // -------------------------------------------------------------------------------------------
  // The step-2 workspace: sidebar, view tabs, and the Download tab of the chart views
  // -------------------------------------------------------------------------------------------

  function sidebarToggle(): HTMLButtonElement {
    return fixture.debugElement.query(By.css('.mc-fig-sidebar-toggle')).nativeElement as HTMLButtonElement;
  }

  function sidebar(): HTMLElement {
    return fixture.debugElement.query(By.css('#mc-fig-sidebar')).nativeElement as HTMLElement;
  }

  function storedSidebar(): {
    version: number; collapsed: boolean; sidebarWidth: number; tab: string; view: string;
    figureSizeOpen: boolean; tableImageSizeOpen: boolean; imageFormatOpen: boolean;
  } {
    return JSON.parse(localStorage.getItem(FIGURE_SIDEBAR_STORAGE_KEY)!);
  }

  /** A second instance over the same payload, built after storage was written. */
  function secondInstance(): ComponentFixture<ModelComparisonComponent> {
    const second = TestBed.createComponent(ModelComparisonComponent);
    second.componentRef.setInput('comparison', buildDto(comparableSet(3)));
    second.detectChanges();
    second.componentInstance.goToStep(2);
    second.detectChanges();
    return second;
  }

  it('collapses the sidebar from its disclosure, persists it, and restores it in a new instance', () => {
    render(buildDto(comparableSet(3)), 2);

    const toggle = sidebarToggle();
    expect(toggle.getAttribute('aria-label')).toBe('Comparison settings');
    expect(toggle.getAttribute('aria-controls')).toBe('mc-fig-sidebar');
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(sidebar().hidden).toBeFalse();
    expect(textOf('#mc-tip-sidebar')).toContain('Hide settings');

    toggle.click();
    fixture.detectChanges();

    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    // The name stays constant; the state is aria-expanded's.
    expect(toggle.getAttribute('aria-label')).toBe('Comparison settings');
    expect(sidebar().hidden).toBeTrue();
    expect(getComputedStyle(sidebar()).display).toBe('none');
    expect(fixture.debugElement.query(By.css('.mc-fig-workspace.is-collapsed'))).not.toBeNull();
    expect(textOf('#mc-tip-sidebar')).toContain('Show settings');
    expect(storedSidebar()).toEqual({
      version: 1, collapsed: true, sidebarWidth: SIDEBAR_WIDTH_DEFAULT, tab: 'data', view: 'all',
      figureSizeOpen: true, tableImageSizeOpen: true, imageFormatOpen: true
    });

    // One collapsed state for every view: the table views have the same sidebar.
    showView('table');
    expect(sidebar().hidden).toBeTrue();

    const second = secondInstance();
    expect(second.componentInstance.sidebarCollapsed).toBeTrue();
    expect((second.nativeElement as HTMLElement).querySelector('#mc-fig-sidebar')?.hasAttribute('hidden')).toBeTrue();
    second.destroy();
  });

  it('keeps the sidebar defaults when storage throws', () => {
    spyOn(Storage.prototype, 'getItem').and.throwError('blocked');
    spyOn(Storage.prototype, 'setItem').and.throwError('blocked');

    const blocked = TestBed.createComponent(ModelComparisonComponent);
    expect(blocked.componentInstance.sidebarCollapsed).toBeFalse();
    expect(blocked.componentInstance.sidebarTab).toBe('data');
    expect(blocked.componentInstance.tableColumns).toEqual(DEFAULT_TABLE_COLUMNS);
    expect(blocked.componentInstance.tableFormat).toBe('xlsx');
    expect(() => blocked.componentInstance.toggleSidebar()).not.toThrow();
    expect(() => blocked.componentInstance.selectSidebarTab('charts')).not.toThrow();
    expect(() => blocked.componentInstance.onTableFormatChange('md')).not.toThrow();
    expect(() => blocked.componentInstance.onTableColumnsChange(DEFAULT_TABLE_COLUMNS)).not.toThrow();
    expect(blocked.componentInstance.sidebarCollapsed).toBeTrue();
    expect(blocked.componentInstance.sidebarTab).toBe('charts');
    blocked.destroy();
  });

  it('restores the sidebar tab and the view, and falls back to Data on an unknown or malformed one', () => {
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('download');
    showView('table');
    expect(storedSidebar()).toEqual({
      version: 1, collapsed: false, sidebarWidth: SIDEBAR_WIDTH_DEFAULT, tab: 'download', view: 'table',
      figureSizeOpen: true, tableImageSizeOpen: true, imageFormatOpen: true
    });

    let second = secondInstance();
    expect(second.componentInstance.sidebarTab).toBe('download');
    expect(second.componentInstance.effectiveFigureTab).toBe('table');
    expect((second.nativeElement as HTMLElement).querySelector('#mc-side-panel-download #mc-table-format')).not.toBeNull();
    second.destroy();

    localStorage.setItem(FIGURE_SIDEBAR_STORAGE_KEY, JSON.stringify({ version: 1, collapsed: 'yes', tab: 'layout' }));
    second = secondInstance();
    expect(second.componentInstance.sidebarTab).toBe('data');
    expect(second.componentInstance.sidebarCollapsed).toBeFalse();
    second.destroy();

    localStorage.setItem(FIGURE_SIDEBAR_STORAGE_KEY, '{not json');
    second = secondInstance();
    expect(second.componentInstance.sidebarTab).toBe('data');
    second.destroy();
  });

  it('opens Download for a stored tab of its earlier name, export', () => {
    render(buildDto(comparableSet(3)), 2);
    localStorage.setItem(FIGURE_SIDEBAR_STORAGE_KEY, JSON.stringify({ version: 1, collapsed: false, tab: 'export' }));

    const second = secondInstance();
    expect(second.componentInstance.sidebarTab).toBe('download');
    expect((second.nativeElement as HTMLElement).querySelector('#mc-side-panel-download')).not.toBeNull();
    second.destroy();
  });

  it('holds the chart size, the image format and a hint to the download controls in the chart views\' Download tab', () => {
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('download');

    const panel = fixture.debugElement.query(By.css('#mc-side-panel-download')).nativeElement as HTMLElement;
    const size = panel.querySelector<HTMLDetailsElement>('#mc-export-section')!;
    expect(size).not.toBeNull();
    expect(size.open).withContext('open by default').toBeTrue();
    expect(size.querySelector('summary .gh-disclosure-summary-title')?.textContent?.trim()).toBe('Chart size');
    for (const id of ['mc-export-resolution', 'mc-export-density', 'mc-export-text-scale']) {
      expect(size.querySelector(`#${id}`)).withContext(id).not.toBeNull();
    }
    expect(textOf('#mc-export-text-scale-hint'))
      .toContain('Scales the caption, notes and chart text together without changing the pixel size.');

    const format = panel.querySelector<HTMLDetailsElement>('#mc-image-format-section')!;
    expect(format.querySelector('summary .gh-disclosure-summary-title')?.textContent?.trim()).toBe('Image format');
    expect(format.querySelector('#mc-export-format')).not.toBeNull();
    expect(size.compareDocumentPosition(format) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    // Settings only: the downloads themselves are on the tiles, in Single chart and in All charts.
    expect(panel.querySelector('#mc-side-download-all')).toBeNull();
    const downloadButtons = Array.from(panel.querySelectorAll('button'))
      .filter(button => /download|copy/i.test(`${button.getAttribute('aria-label') ?? ''} ${button.textContent ?? ''}`));
    expect(downloadButtons.length).withContext('no download or copy buttons').toBe(0);
    expect(panel.querySelector('.mc-download-hint')?.textContent?.trim())
      .toBe('Download a chart from its tile or from Single chart, or all of them at once from All charts.');
    // The table's formats belong to the table views.
    expect(panel.querySelector('#mc-table-format')).toBeNull();

    // The Charts tab holds the family tabs first, and no size.
    openSidebarTab('charts');
    const charts = fixture.debugElement.query(By.css('#mc-side-panel-charts')).nativeElement as HTMLElement;
    expect(charts.firstElementChild?.classList).toContain('mc-style-family-tabs');
    expect(charts.querySelector('#mc-export-resolution')).toBeNull();
  });

  it('remembers whether Chart size is open, in the sidebar record', () => {
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('download');
    const section = fixture.debugElement.query(By.css('#mc-export-section')).nativeElement as HTMLDetailsElement;

    section.open = false;
    section.dispatchEvent(new Event('toggle'));
    refresh();
    expect(component.figureSizeOpen).toBeFalse();
    expect(storedSidebar().figureSizeOpen).toBeFalse();
    expect(storedSidebar().tab).toBe('download');

    const second = secondInstance();
    expect(second.componentInstance.figureSizeOpen).toBeFalse();
    second.destroy();
  });

  it('resets Chart size to Full HD at the display’s density and 100 % text', () => {
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('download');
    const reset = (): HTMLButtonElement =>
      fixture.debugElement.query(By.css('#mc-export-reset')).nativeElement as HTMLButtonElement;

    expect(reset().getAttribute('aria-label')).toBe('Reset Chart size to defaults');
    expect(reset().getAttribute('aria-disabled')).toBe('true');

    component.onExportResolutionChange('a4p');
    component.onExportTextScaleChange(150);
    refresh();
    expect(reset().getAttribute('aria-disabled')).toBeNull();

    reset().click();
    refresh();
    expect(component.figureSize).toEqual(defaultFigureSize(component.displayDensity));
    expect(JSON.parse(localStorage.getItem(FIGURE_SIZE_STORAGE_KEY)!).resolutionId).toBe('fullhd');
    expect(reset().getAttribute('aria-disabled')).toBe('true');
  });

  it('resets the image format to PNG and quality 85, and says so', () => {
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('download');
    const reset = (): HTMLButtonElement =>
      fixture.debugElement.query(By.css('#mc-image-format-reset')).nativeElement as HTMLButtonElement;
    expect(reset().closest('details')).toBeNull();
    expect(reset().getAttribute('aria-disabled')).toBe('true');

    component.onExportFormatChange('webp');
    component.onWebpQualityChange(95);
    refresh();
    expect(component.imageFormatReadout).toBe('WebP · quality 95');
    expect(reset().getAttribute('aria-disabled')).toBeNull();

    reset().click();
    refresh();
    expect(component.exportFormat).toBe('png');
    expect(component.webpQuality).toBe(85);
    expect(textOf('#mc-side-panel-download [role="status"]')).toContain('Image format reset to defaults.');
  });

  it('persists a size change, and a new instance opens on it', () => {
    render(buildDto(comparableSet(3)), 2);
    component.onExportResolutionChange('uw1440');
    component.onExportDensityChange(2);

    const stored = JSON.parse(localStorage.getItem(FIGURE_SIZE_STORAGE_KEY)!);
    expect(stored.version).toBe(1);
    expect(stored.resolutionId).toBe('uw1440');
    expect(stored.densitySelection).toBe(2);

    const second = secondInstance();
    expect(second.componentInstance.exportResolution.id).toBe('uw1440');
    expect(second.componentInstance.exportDensity).toBe(2);
    second.destroy();
  });

  it('centres the sidebar toggle on the view bar, and About and Recompute on the step row', () => {
    render(buildDto(comparableSet(3)), 2);
    expect(component.figureTab).toBe('all');

    const box = (selector: string): DOMRect =>
      (fixture.debugElement.query(By.css(selector)).nativeElement as HTMLElement).getBoundingClientRect();
    // Each row's 1 px bottom border is not part of the height the buttons centre on.
    const centreOf = (row: DOMRect): number => row.top + (row.height - 1) / 2;
    const bar = box('.mc-fig-bar');
    const toggle = box('.mc-fig-sidebar-toggle');
    expect(Math.abs(toggle.top + toggle.height / 2 - centreOf(bar))).toBeLessThanOrEqual(1);

    const stepRow = box('.mc-wizard-tabbar');
    for (const selector of ['#mc-about-trigger', '.mc-recompute']) {
      const button = box(selector);
      expect(Math.abs(button.top + button.height / 2 - centreOf(stepRow))).withContext(selector).toBeLessThanOrEqual(1);
    }
  });

  it('puts About and Recompute at the step row\'s end on step 2 only, and leaves the view bar the toggle and the tabs', () => {
    render(buildDto(comparableSet(3)), 2);
    for (const selector of ['#mc-about-trigger', '.mc-recompute']) {
      const button = fixture.debugElement.query(By.css(selector)).nativeElement as HTMLElement;
      expect(button.closest('.mc-wizard-tabbar .mc-wizard-meta')).withContext(selector).not.toBeNull();
      expect(button.closest('.mc-fig-bar')).withContext(selector).toBeNull();
      expect(button.closest('[role="tablist"]')).withContext(selector).toBeNull();
    }
    const bar = fixture.debugElement.query(By.css('.mc-fig-bar')).nativeElement as HTMLElement;
    expect(Array.from(bar.children).map(child => child.classList.contains('mc-fig-sidebar-toggle') ? 'toggle'
      : child.classList.contains('mc-fig-tabs') ? 'tabs' : child.getAttribute('popover') ? 'tooltip' : child.className))
      .toEqual(['toggle', 'tooltip', 'tabs']);

    component.goToStep(1);
    fixture.detectChanges();
    expect(component.comparison).not.toBeNull();
    expect(fixture.debugElement.query(By.css('#mc-about-trigger'))).toBeNull();
    expect(fixture.debugElement.query(By.css('.mc-recompute'))).toBeNull();
  });

  it('keeps the sidebar width in the sidebar record, clamped, and hands it to the workspace', () => {
    render(buildDto(comparableSet(3)), 2);
    const workspace = (): HTMLElement =>
      fixture.debugElement.query(By.css('.mc-fig-workspace')).nativeElement as HTMLElement;
    const resizer = (): HTMLElement | null =>
      fixture.debugElement.query(By.css('app-pane-resizer.mc-fig-resizer'))?.nativeElement ?? null;

    expect(component.sidebarWidth).toBe(SIDEBAR_WIDTH_DEFAULT);
    expect(workspace().style.getPropertyValue('--mc-sidebar-width')).toBe(`${SIDEBAR_WIDTH_DEFAULT}px`);
    expect(resizer()?.getAttribute('role')).toBe('separator');
    expect(resizer()?.getAttribute('aria-controls')).toBe('mc-fig-sidebar');
    expect(resizer()?.getAttribute('aria-valuenow')).toBe(`${SIDEBAR_WIDTH_DEFAULT}`);

    // A key press is a commit: the width is stored and the workspace takes it.
    resizer()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(component.sidebarWidth).toBe(SIDEBAR_WIDTH_DEFAULT + 16);
    expect(storedSidebar().sidebarWidth).toBe(SIDEBAR_WIDTH_DEFAULT + 16);
    expect(workspace().style.getPropertyValue('--mc-sidebar-width')).toBe(`${SIDEBAR_WIDTH_DEFAULT + 16}px`);

    const second = secondInstance();
    expect(second.componentInstance.sidebarWidth).toBe(SIDEBAR_WIDTH_DEFAULT + 16);
    second.destroy();

    // Stored values are clamped, and anything that is not a finite number is the default.
    const storedWidth = (width: unknown): number => {
      localStorage.setItem(FIGURE_SIDEBAR_STORAGE_KEY, JSON.stringify({ version: 1, collapsed: false, sidebarWidth: width }));
      const instance = secondInstance();
      const read = instance.componentInstance.sidebarWidth;
      instance.destroy();
      return read;
    };
    expect(storedWidth(10)).toBe(SIDEBAR_WIDTH_MIN);
    expect(storedWidth(5000)).toBe(SIDEBAR_WIDTH_MAX);
    expect(storedWidth('wide')).toBe(SIDEBAR_WIDTH_DEFAULT);
    expect(storedWidth(null)).toBe(SIDEBAR_WIDTH_DEFAULT);

    // No handle while the sidebar is collapsed.
    sidebarToggle().click();
    fixture.detectChanges();
    expect(resizer()).toBeNull();
  });

  it('puts the resizer in its own column between the sidebar and the main area', () => {
    // Wide enough that the container query keeps the sidebar beside the views.
    const hostElement = fixture.nativeElement as HTMLElement;
    hostElement.style.display = 'block';
    hostElement.style.width = '1200px';
    hostElement.style.height = '800px';
    render(buildDto(comparableSet(3)), 2);
    const rect = (selector: string): DOMRect =>
      (fixture.debugElement.query(By.css(selector)).nativeElement as HTMLElement).getBoundingClientRect();

    const sidebar = rect('.mc-fig-sidebar');
    const resizer = rect('app-pane-resizer.mc-fig-resizer');
    const main = rect('.mc-fig-main');
    expect(resizer.width).toBeCloseTo(12, 0);
    expect(Math.abs(resizer.left - sidebar.right)).toBeLessThanOrEqual(1);
    expect(Math.abs(main.left - resizer.right)).toBeLessThanOrEqual(1);
    expect(getComputedStyle(fixture.debugElement.query(By.css('.mc-fig-sidebar')).nativeElement).borderInlineEndWidth)
      .toBe('0px');
  });

  it('starts the Interactive table\'s header row 16px under the view bar, as the Table preview does', () => {
    const hostElement = fixture.nativeElement as HTMLElement;
    hostElement.style.display = 'block';
    hostElement.style.width = '1200px';
    hostElement.style.height = '800px';
    renderTable(buildDto(comparableSet(3)));
    const element = (selector: string): HTMLElement =>
      fixture.debugElement.query(By.css(selector)).nativeElement as HTMLElement;
    const barBottom = (): number => element('.mc-fig-bar').getBoundingClientRect().bottom;

    const toolbar = element('#mc-fig-panel-table .mc-table-toolbar');
    const tableContentTop = toolbar.getBoundingClientRect().top + parseFloat(getComputedStyle(toolbar).paddingTop);
    expect(getComputedStyle(element('#mc-fig-panel-table')).paddingTop).toBe('0px');
    expect(Math.abs(tableContentTop - barBottom() - 16)).withContext('Interactive table').toBeLessThanOrEqual(1);

    showView('tablePreview');
    const previewTop = element('#mc-fig-panel-tablePreview .mc-preview-toolbar').getBoundingClientRect().top;
    expect(Math.abs(previewTop - barBottom() - 16)).withContext('Table preview').toBeLessThanOrEqual(1);
  });

  it('renders no chart directive on step 2, and exports without reading a page canvas', async () => {
    render(buildDto(comparableSet(3)), 2);
    const directives = (): number => fixture.debugElement.queryAll(By.directive(BaseChartDirective)).length;
    expect(directives()).withContext('the All tab').toBe(0);
    expect(fixture.debugElement.queryAll(By.css('canvas[baseChart], canvas[basechart]')).length).toBe(0);

    openSingle(component.panelCards[0]);
    expect(directives()).withContext('the Single tab').toBe(0);
    // Nothing is kept rendered and hidden behind the Single tab any more.
    expect(fixture.debugElement.query(By.css('#mc-fig-panel-all'))).toBeNull();
    expect(fixture.debugElement.query(By.css('.mc-fig-panel[inert], .mc-fig-panel.is-inactive'))).toBeNull();

    captureSaves();
    const exportOne = spyOn(
      component as unknown as { exportOneFigure(card: ComparisonFigureCard, ...rest: unknown[]): Promise<unknown> },
      'exportOneFigure'
    ).and.returnValue(Promise.resolve({ result: null, refusal: null, pixels: '' }));
    await component.downloadPreviewedFigure();

    expect(exportOne).toHaveBeenCalledTimes(1);
    const args = exportOne.calls.mostRecent().args;
    expect((args[0] as ComparisonFigureCard).id).toBe(component.panelCards[0].id);
    expect(args.some(arg => arg instanceof HTMLCanvasElement)).toBeFalse();
  });

  it('re-composes the Single stage on a highlight toggle while it is shown, and not on the All tab', () => {
    render(buildDto(comparableSet(3)), 2);
    const renderPreview = spyOn(
      component as unknown as { renderPreview(): Promise<void> }, 'renderPreview'
    ).and.returnValue(Promise.resolve());
    const box = (): DebugElement => fixture.debugElement.queryAll(By.css('.mc-models-table input[id^="mc-emph-"]'))[0];

    jasmine.clock().install();
    try {
      box().triggerEventHandler('change', { target: box().nativeElement });
      fixture.detectChanges();
      jasmine.clock().tick(200);
      expect(renderPreview).not.toHaveBeenCalled();

      openSingle();
      jasmine.clock().tick(200);
      renderPreview.calls.reset();

      box().triggerEventHandler('change', { target: box().nativeElement });
      fixture.detectChanges();
      expect(renderPreview).not.toHaveBeenCalled();
      jasmine.clock().tick(200);
      expect(renderPreview).toHaveBeenCalledTimes(1);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('lights a hover highlight on the All tab, none on the Single tab, and still clears one', async () => {
    render(buildDto(comparableSet(3)), 2);
    await settleAllTab();
    const schedule = spyOn(
      component as unknown as { scheduleAllCompose(): void }, 'scheduleAllCompose').and.callThrough();
    component.setHighlight('run:1');
    expect(component.highlightedKey).toBe('run:1');
    // The tiles re-compose with the model lit.
    expect(schedule).toHaveBeenCalled();

    openSingle();
    expect(component.highlightedKey).withContext('entering the Single tab clears it').toBeNull();

    component.setHighlight('run:2');
    expect(component.highlightedKey).toBeNull();

    component.selectFigureTab('all');
    component.setHighlight('run:2');
    expect(component.highlightedKey).toBe('run:2');
    component.setHighlight(null);
    expect(component.highlightedKey).toBeNull();
  });

  // -------------------------------------------------------------------------------------------
  // The All tab: every figure, exactly as exported
  // -------------------------------------------------------------------------------------------

  let realIntersectionObserver: typeof IntersectionObserver | undefined;

  /** Every tile counts as near, as it does in a browser without IntersectionObserver. */
  function withoutIntersectionObserver(): void {
    realIntersectionObserver = window.IntersectionObserver;
    (window as unknown as { IntersectionObserver: unknown }).IntersectionObserver = undefined;
  }

  afterEach(() => {
    if (realIntersectionObserver) {
      (window as unknown as { IntersectionObserver: unknown }).IntersectionObserver = realIntersectionObserver;
      realIntersectionObserver = undefined;
    }
  });

  /** Lets the All tab attach, which runs in a microtask outside the check pass that found it. */
  async function settleAllTab(): Promise<void> {
    await Promise.resolve();
    fixture.detectChanges();
  }

  /** The composition the debounce would run, without waiting for its timer and frame. */
  async function composeAllTiles(): Promise<void> {
    await (component as unknown as { composeAllTiles(): Promise<void> }).composeAllTiles();
    refresh();
  }

  function allPanel(): HTMLElement {
    return fixture.debugElement.query(By.css('#mc-fig-panel-all')).nativeElement as HTMLElement;
  }

  function tileOf(card: ComparisonFigureCard): HTMLElement {
    return fixture.debugElement.query(By.css(`.mc-all-tile[data-figure-id="${card.id}"]`)).nativeElement as HTMLElement;
  }

  /** A 1200 × 900 viewport at DPR 2, with Full HD at 100 % density: Fit height is 868 × 2 / 1080. */
  async function openFittedAll(): Promise<void> {
    spyOn(component, 'measureAllViewport').and.returnValue({ width: 1200, height: 900, devicePixelRatio: 2 });
    component.onExportDensityChange(1);
    render(buildDto(comparableSet(3)), 2);
    await settleAllTab();
  }

  it('opens the All tab at Fit height: one figure’s full height in the visible height', async () => {
    await openFittedAll();

    expect(component.allActive).toBeTrue();
    expect(component.allView).toBe('fitHeight');
    const fit = fitHeightZoom(900, 1080, 2, 16);
    expect(component.allZoomValue).toBeCloseTo(fit, 9);
    // The tile is the export's pixels over the display ratio, times the zoom: 900 less the padding.
    // Inline style lengths are serialized to two decimals.
    expect(parseFloat(tileOf(component.panelCards[0]).style.height)).toBeCloseTo(868, 1);
    expect(parseFloat(tileOf(component.panelCards[0]).style.width)).toBeCloseTo(868 * 1920 / 1080, 1);
    expect(textOf('.mc-all-toolbar .mc-preview-zoom-value')).toContain('Fit height');
    const slider = fixture.debugElement.query(By.css('#mc-all-zoom')).nativeElement as HTMLInputElement;
    expect(slider.getAttribute('aria-valuetext')).toBe(`${Math.round(fit * 100)} percent, fitted to the height`);
    expect(fixture.debugElement.query(By.css('label[for="mc-all-zoom"]'))?.nativeElement.textContent.trim()).toBe('Zoom');
  });

  it('answers + − 0 anywhere in the All panel but a form field, and leaves them to the browser with Ctrl', async () => {
    await openFittedAll();
    const fit = component.allZoomValue;
    const press = (target: HTMLElement, key: string, init: KeyboardEventInit = {}): KeyboardEvent => {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
      target.dispatchEvent(event);
      refresh();
      return event;
    };

    const tile = tileOf(component.panelCards[0]);
    expect(press(tile, '+').defaultPrevented).toBeTrue();
    expect(component.allView).toBeGreaterThan(fit);
    const zoomedIn = component.allZoomValue;
    press(allPanel(), '=');
    expect(component.allZoomValue).toBeGreaterThan(zoomedIn);
    press(allPanel(), '-');
    expect(component.allZoomValue).toBeCloseTo(zoomedIn, 9);
    press(allPanel(), '0');
    expect(component.allView).toBe('fitHeight');

    const withCtrl = press(allPanel(), '+', { ctrlKey: true });
    expect(withCtrl.defaultPrevented).toBeFalse();
    expect(component.allView).toBe('fitHeight');
    const slider = fixture.debugElement.query(By.css('#mc-all-zoom')).nativeElement as HTMLInputElement;
    expect(press(slider, '-').defaultPrevented).withContext('the slider keeps its own keys').toBeFalse();
    expect(component.allView).toBe('fitHeight');
  });

  it('zooms every tile from the toolbar, and fits the height again', async () => {
    await openFittedAll();
    const button = (name: string): HTMLButtonElement =>
      fixture.debugElement.query(By.css(`.mc-all-toolbar button[aria-label="${name}"]`)).nativeElement as HTMLButtonElement;
    const height = (): number => parseFloat(tileOf(component.scatterCards[0]).style.height);
    const fitted = height();

    button('Zoom all figures in').click();
    refresh();
    expect(typeof component.allView).toBe('number');
    expect(height()).toBeGreaterThan(fitted);

    button('Zoom all figures out').click();
    button('Zoom all figures out').click();
    refresh();
    expect(height()).toBeLessThan(fitted);

    button('Fit height').click();
    refresh();
    expect(component.allView).toBe('fitHeight');
    expect(height()).toBeCloseTo(fitted, 6);

    for (const name of ['Zoom all figures out', 'Zoom all figures in', 'Fit height']) {
      const tipId = button(name).getAttribute('interestfor');
      expect(tipId).withContext(name).toBeTruthy();
      expect((fixture.nativeElement as HTMLElement).querySelector(`#${tipId}`)?.getAttribute('popover')).toBe('hint');
      expect(button(name).hasAttribute('title')).toBeFalse();
    }
  });

  it('keeps the reader’s zoom across a viewport resize, and re-fits while they have not zoomed', async () => {
    await openFittedAll();
    const measure = component.measureAllViewport as jasmine.Spy;
    const refreshGeometry = (): void =>
      (component as unknown as { refreshAllGeometry(): void }).refreshAllGeometry();

    measure.and.returnValue({ width: 1200, height: 600, devicePixelRatio: 2 });
    refreshGeometry();
    expect(component.allZoomValue).toBeCloseTo(fitHeightZoom(600, 1080, 2, 16), 9);

    component.setAllView(1);
    measure.and.returnValue({ width: 1200, height: 1000, devicePixelRatio: 2 });
    refreshGeometry();
    expect(component.allZoomValue).toBe(1);
  });

  it('opens a tile in Single on Enter or a click, and Single opens on the figure last activated', async () => {
    await openFittedAll();
    const card = component.scatterCards[1];

    const tile = tileOf(card);
    expect(tile.getAttribute('tabindex')).toBe('0');
    expect(tile.getAttribute('aria-label')).toBe(card.title);
    const canvas = tile.querySelector('canvas')!;
    expect(canvas.getAttribute('role')).toBe('img');
    expect(canvas.getAttribute('aria-label')).toBe(card.ariaLabel);

    tile.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    refresh();
    expect(component.figureTab).toBe('single');
    expect(component.previewCardId).toBe(card.id);
    expect(document.activeElement?.id).toBe('mc-preview-figure');

    component.selectFigureTab('all');
    refresh();
    tileOf(component.panelCards[2]).click();
    refresh();
    expect(component.figureTab).toBe('single');
    expect(component.previewCardId).toBe(component.panelCards[2].id);

    // Back on All, the Single tab itself reopens the figure last activated.
    component.selectFigureTab('all');
    refresh();
    openSingle();
    expect(component.previewCardId).toBe(component.panelCards[2].id);
  });

  it('paints every tile with the composition the download writes, at the tile’s raster', async () => {
    withoutIntersectionObserver();
    await openFittedAll();
    await composeAllTiles();

    const zoom = component.allZoomValue;
    for (const card of component.exportableCards) {
      const canvas = tileOf(card).querySelector('canvas')!;
      // The displayed size at the display's density, never more than the export itself.
      expect(canvas.width).withContext(card.id).toBe(Math.round(1920 * Math.min(1, zoom)));
      expect(canvas.height).withContext(card.id).toBe(Math.round(1080 * Math.min(1, zoom)));
    }
    expect(component.allTileRefusals).toEqual({});
  });

  it('re-composes the tiles and persists a size change, giving every tile the new box', async () => {
    withoutIntersectionObserver();
    await openFittedAll();
    await composeAllTiles();
    const schedule = spyOn(
      component as unknown as { scheduleAllCompose(): void }, 'scheduleAllCompose').and.callThrough();

    openSidebarTab('download');
    const select = fixture.debugElement.query(By.css('#mc-export-resolution')).nativeElement as HTMLSelectElement;
    select.value = 'square1080';
    select.dispatchEvent(new Event('change'));
    refresh();

    expect(component.figureSize.resolutionId).toBe('square1080');
    expect(JSON.parse(localStorage.getItem(FIGURE_SIZE_STORAGE_KEY)!).resolutionId).toBe('square1080');
    expect(schedule).toHaveBeenCalled();
    // Still at Fit height, now for a square: as tall as before, as wide as it is tall.
    const tile = tileOf(component.panelCards[0]);
    expect(parseFloat(tile.style.height)).toBeCloseTo(868, 6);
    expect(parseFloat(tile.style.width)).toBeCloseTo(868, 6);

    await composeAllTiles();
    const canvas = tile.querySelector('canvas')!;
    expect(canvas.width).toBe(canvas.height);
  });

  it('names a figure its caveats do not fit on its tile, in the download’s words', async () => {
    withoutIntersectionObserver();
    await openFittedAll();
    const card = component.panelCards[0];
    const notice = (index: number): string =>
      `Notice ${index}: ` + 'the speed axis is degraded for this entry, so its bar is drawn from a partial sample. '.repeat(6);
    spyOn(component as unknown as { exportChrome(card: ComparisonFigureCard): unknown }, 'exportChrome')
      .and.returnValue({
        chrome: { ...card.chrome, notes: [1, 2, 3, 4, 5, 6].map(index => ({ text: notice(index), tone: 'warning' as const })) },
        footer: { suite: 'Suite A', computedAt: '3 Sep 2026' }
      });
    component.onExportResolutionChange('hd');
    await composeAllTiles();

    expect(component.allTileRefusals[card.id]).toContain(card.title);
    expect(component.allTileRefusals[card.id]).toContain('1280 × 720 px');
    expect(tileOf(card).querySelector('.mc-all-refusal')?.textContent).toContain('1280 × 720 px');
    expect(tileOf(card).querySelector('canvas')!.width).toBe(0);
  });

  it('defers the rendering of tiles past the first row, holding their size', async () => {
    await openFittedAll();

    // A 1200 px viewport less 32 px of padding holds one Fit height tile of 1543 px per row.
    expect(component.allTilesPerRow).toBe(1);
    const tiles = fixture.debugElement.queryAll(By.css('.mc-all-tile')).map(tile => tile.nativeElement as HTMLElement);
    expect(tiles[0].classList).not.toContain('is-deferred');
    expect(tiles.slice(1).every(tile => tile.classList.contains('is-deferred'))).toBeTrue();
    expect(tiles[1].style.getPropertyValue('contain-intrinsic-size')).toContain(`${Math.round(component.allTileCssHeight)}px`);
  });

  it('stops composing, and drops its observers and timers, on leaving the All tab, step 2 and on destroy', async () => {
    await openFittedAll();
    const internals = component as unknown as {
      allComposeTimer: unknown; allResizeObserver: unknown; allIntersectionObserver: unknown;
    };
    expect(internals.allComposeTimer).not.toBeNull();

    openSingle();
    expect(component.allActive).toBeFalse();
    expect(internals.allComposeTimer).toBeNull();
    expect(internals.allResizeObserver).toBeNull();
    expect(internals.allIntersectionObserver).toBeNull();

    component.selectFigureTab('all');
    refresh();
    expect(component.allActive).toBeTrue();
    component.goToStep(1);
    fixture.detectChanges();
    expect(component.allActive).toBeFalse();
    expect(internals.allComposeTimer).toBeNull();

    component.goToStep(2);
    fixture.detectChanges();
    await settleAllTab();
    expect(component.allActive).toBeTrue();
    fixture.destroy();
    expect(component.allActive).toBeFalse();
    expect(internals.allResizeObserver).toBeNull();
  });

  it('keeps the Charts tab on the Single figure\'s family, and moves the Single stage to a chosen one', () => {
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('charts');
    const families = (): string[] => fixture.debugElement.queryAll(By.css('.mc-style-family-tabs [role="tab"]'))
      .map(option => ((option.nativeElement as HTMLElement).textContent ?? '').trim());
    expect(families()).toEqual(['Bar panels', 'Profile', 'Trade-offs']);
    expect((fixture.debugElement.query(By.css('.mc-style-family-tabs')).nativeElement as HTMLElement)
      .getAttribute('aria-label')).toBe('Charts to style');

    // On the All tab, choosing a family moves nothing.
    openStyleFamily('scatter');
    expect(component.styleFamily).toBe('scatter');
    expect(component.previewCardId).toBeNull();
    expect(fixture.debugElement.query(By.css('#mc-style-scatter-heading'))).not.toBeNull();

    openSingle(component.panelCards[1]);
    expect(component.styleFamily).withContext('follows the figure on the stage').toBe('bar');
    component.selectPreviewCard(component.scatterCards[0].id);
    expect(component.styleFamily).toBe('scatter');

    component.selectStyleFamily('bar');
    expect(component.previewCardId).toBe(component.panelCards[0].id);
    component.selectStyleFamily('profile');
    expect(component.previewCardId).toBe(component.profileCard!.id);
  });

  it('offers the style families as tabs with the full tab contract', () => {
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('charts');

    const kinds = ['bar', 'profile', 'scatter'];
    const tabs = (): HTMLButtonElement[] =>
      fixture.debugElement.queryAll(By.css('.mc-style-family-tabs [role="tab"]'))
        .map(tab => tab.nativeElement as HTMLButtonElement);
    const tablist = fixture.debugElement.query(By.css('.mc-style-family-tabs')).nativeElement as HTMLElement;
    expect(tablist.getAttribute('role')).toBe('tablist');
    expect(tablist.getAttribute('aria-label')).toBe('Charts to style');
    expect(component.effectiveStyleFamily).toBe('bar');

    const expectSelected = (selected: number): void => {
      tabs().forEach((tab, index) => {
        expect(tab.id).toBe(`mc-style-family-tab-${kinds[index]}`);
        expect(tab.getAttribute('aria-controls')).toBe(`mc-style-family-panel-${kinds[index]}`);
        expect(tab.getAttribute('aria-selected')).toBe(index === selected ? 'true' : 'false');
        expect(tab.getAttribute('tabindex')).toBe(index === selected ? '0' : '-1');
      });
      const panels = fixture.debugElement.queryAll(By.css('.mc-style-family-panel'));
      expect(panels.length).withContext('only the selected family\'s panel exists').toBe(1);
      const panel = panels[0].nativeElement as HTMLElement;
      expect(panel.getAttribute('role')).toBe('tabpanel');
      expect(panel.id).toBe(`mc-style-family-panel-${kinds[selected]}`);
      expect(panel.getAttribute('aria-labelledby')).toBe(`mc-style-family-tab-${kinds[selected]}`);
      expect(panel.getAttribute('tabindex')).toBe('0');
    };
    expectSelected(0);

    const press = (index: number, key: string): void => {
      tabs()[index].dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
      refresh();
    };

    press(0, 'ArrowRight');
    expect(component.styleFamily).toBe('profile');
    expectSelected(1);
    expect(document.activeElement).toBe(tabs()[1]);

    press(1, 'Home');
    expect(component.styleFamily).toBe('bar');
    expect(document.activeElement).toBe(tabs()[0]);

    press(0, 'ArrowLeft');
    expect(component.styleFamily).withContext('Left wraps to the last tab').toBe('scatter');
    expectSelected(2);
    expect(document.activeElement).toBe(tabs()[2]);

    press(2, 'ArrowRight');
    expect(component.styleFamily).withContext('Right wraps to the first tab').toBe('bar');
    expect(document.activeElement).toBe(tabs()[0]);

    press(0, 'End');
    expect(component.styleFamily).toBe('scatter');
    expect(document.activeElement).toBe(tabs()[2]);

    expect(fixture.debugElement.query(By.css('input[type="radio"][name="mc-style-family"]'))).toBeNull();
  });

  it('offers no Profile family where the profile is suppressed', () => {
    render(buildDto(comparableSet(2)), 2);
    expect(component.profileCard).toBeNull();
    expect(component.styleFamilies.map(family => family.kind)).toEqual(['bar', 'scatter']);

    component.styleFamily = 'profile';
    expect(component.effectiveStyleFamily).toBe('bar');
  });

  it('leaves no duplicate of any control in step 2', () => {
    render(buildDto(comparableSet(3)), 2);
    openSingle();

    const step = fixture.debugElement.query(By.css('#mc-step-panel-2')).nativeElement as HTMLElement;
    const outsidePanel = (text: string): Element[] => Array.from(step.querySelectorAll('label, button, h4'))
      .filter(element => !element.closest('app-figure-style-panel'))
      .filter(element => (element.textContent ?? '').includes(text));
    for (const text of ['Filled bars in the Intelligence', 'Label models inside', 'Show values in', 'Figure preview…']) {
      expect(outsidePanel(text).length).withContext(text).toBe(0);
    }
    expect(fixture.debugElement.query(By.css('dialog.mc-preview-dialog'))).toBeNull();
    // Each export control exists once.
    expect(step.querySelectorAll('#mc-export-resolution').length).toBeLessThanOrEqual(1);
    expect(fixture.debugElement.queryAll(By.css('app-figure-style-panel')).length).toBeLessThanOrEqual(1);
  });

  it('returns to the Single tab after a step away, and watches the stage again', async () => {
    render(buildDto(comparableSet(3)), 2);
    openSingle();
    const observe = spyOn(component as unknown as { observeStage(): void }, 'observeStage').and.callThrough();

    component.goToStep(1);
    fixture.detectChanges();
    expect(component.previewActive).toBeFalse();
    expect(fixture.debugElement.query(By.css('.mc-preview-stage'))).toBeNull();

    component.goToStep(2);
    fixture.detectChanges();
    // The re-attach runs in a microtask, outside the check pass that found the stage.
    await Promise.resolve();
    fixture.detectChanges();

    expect(component.figureTab).toBe('single');
    expect(singleTabButton().getAttribute('aria-selected')).toBe('true');
    expect(component.previewActive).toBeTrue();
    expect(observe).toHaveBeenCalledTimes(1);
  });

  // -------------------------------------------------------------------------------------------
  // Questions asked, the per-question cost's denominator
  // -------------------------------------------------------------------------------------------

  it('shares the asked count only when every charted entry asked the same number of questions', () => {
    const asked = (counts: number[]) => comparableSet(counts.length).map((entry, index) =>
      ({ ...entry, cost: { ...entry.cost!, questionsAskedPerRun: counts[index] } }));

    expect(toChartContext(buildDto(asked([18, 18]))).questionsAskedPerRun).toBe(18);
    expect(toChartContext(buildDto(asked([18, 17]))).questionsAskedPerRun).toBeNull();
    expect(toChartContext(buildDto(comparableSet(2))).questionsAskedPerRun).toBeNull();
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
    expect(Number.isNaN(entry.candidateCostPerRunUsd)).toBeTrue();
    // No cost object, so no run total: NaN, like every other absent measure.
    expect(Number.isNaN(entry.totalRunCostUsd)).toBeTrue();
    expect(entry.totalRunCostSdUsd).toBeNull();
    expect(entry.candidateCostPerQuestionSdUsd).toBeNull();
    expect(entry.speedIndexSd).toBeNull();
    expect(entry.totalModelTimeSdMs).toBeNull();
  });

  it('maps the run total including grading roles when the payload carries one, and NaN when it is null', () => {
    const cost: BenchmarkModelComparisonCostDto = {
      candidateCostPerQuestionUsd: 0.0123,
      candidateCostPerRunUsd: 0.2214,
      totalRunCostPerRunUsd: 0.9876,
      totalRunCostSdUsd: 0.0432,
      basis: 'Current',
      pricingResolved: true,
      degraded: false
    };
    const measured = (key: string, overrides: Partial<BenchmarkModelComparisonCostDto>): BenchmarkModelComparisonEntryDto =>
      ({ ...excluded, key, excluded: false, comparable: true, state: 'Comparable', excludingKeys: [], cost: { ...cost, ...overrides } });
    const [withTotal, withoutTotal] = toChartEntries({
      pricingBasis: 'Current',
      pricingBasisLabel: 'Current catalog',
      computedAtUtc: '2026-09-07T12:00:00Z',
      baselineSuiteId: 5,
      baselineSuiteName: 'Suite',
      baselineEntryKeys: [],
      baselineKeyValues: {},
      baselineSignature: '',
      modelAxisKeys: [],
      entries: [
        measured('run:1', {}),
        measured('run:2', {
          totalRunCostPerRunUsd: null,
          totalRunCostSdUsd: null,
          totalRunCostUnavailableReason: 'Run 2 has no resolvable pricing.'
        })
      ],
      comparableCount: 2,
      excludedCount: 0,
      thinkingLevelsDiffer: false,
      speedAxisCaveat: null,
      explanation: '',
      excludedMeasures: []
    });

    expect(withTotal.totalRunCostUsd).toBe(0.9876);
    expect(withTotal.totalRunCostSdUsd).toBe(0.0432);
    expect(Number.isNaN(withoutTotal.totalRunCostUsd)).toBeTrue();
    expect(withoutTotal.totalRunCostSdUsd).toBeNull();
  });

  it('reads the question counts and the pricing label off the payload header', () => {
    const context = toChartContext(null);
    expect(context.scoredItemsMin).toBe(0);
    expect(context.scoredItemsMax).toBe(0);
    expect(context.examItemCount).toBe(0);
    expect(context.pricingBasisLabel).toBe('Unknown pricing basis');
    expect(context.pricingBasis).toBe('');
    expect(context.pricedOn).toBe('');
    expect(context.questionsAskedPerRun).toBeNull();
  });
});
