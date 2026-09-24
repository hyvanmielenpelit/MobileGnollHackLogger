import { ChangeDetectorRef, Component, DebugElement, inject } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideCharts } from 'ng2-charts';

import {
  ComparisonFigureCard,
  ComparisonWizardStep,
  FIGURE_SIDEBAR_STORAGE_KEY,
  FIGURE_STYLE_STORAGE_KEY,
  ModelComparisonComponent
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
  BenchmarkModelComparisonDto,
  BenchmarkModelComparisonEntryDto,
  ComparisonSelectedSource,
  ComparisonSelectionNotice,
  ComparisonSelectionState,
  selectionNotices,
  toChartContext,
  toChartEntries
} from './model-comparison.models';
import { TableExportFormat, xlsxWriterModule } from './table-export';
import { zipWriterModule } from './figure-export';
import { previewZoomRange, zoomToSlider } from './preview-view';
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
        suiteItemCount: 18,
        revisedItemCount: 0,
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
          instead: 'Candidate $ / question in the step 3 table, read beside the Intelligence '
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

  beforeEach(async () => {
    // The figure style and the sidebar are remembered per browser, so one spec's must not reach the
    // next.
    localStorage.removeItem(FIGURE_STYLE_STORAGE_KEY);
    localStorage.removeItem(FIGURE_SIDEBAR_STORAGE_KEY);
    await TestBed.configureTestingModule({
      imports: [ModelComparisonComponent],
      providers: [provideCharts({ registerables: APP_CHART_REGISTRABLES })]
    }).compileComponents();

    fixture = TestBed.createComponent(ModelComparisonComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    localStorage.removeItem(FIGURE_STYLE_STORAGE_KEY);
    localStorage.removeItem(FIGURE_SIDEBAR_STORAGE_KEY);
  });

  /** Selects one tab of the step-4 settings sidebar by clicking it. */
  function openSidebarTab(tab: 'emphasis' | 'style' | 'download'): void {
    (fixture.debugElement.query(By.css(`#mc-side-tab-${tab}`)).nativeElement as HTMLButtonElement).click();
    fixture.detectChanges();
  }

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

  it('keeps both table pagers outside the horizontal scroll wrapper', () => {
    render(buildDto(comparableSet(4)), 3);

    expect(fixture.debugElement.queryAll(By.css('.gh-datatable-scroll app-table-pager')).length).toBe(0);
    expect(fixture.debugElement.queryAll(By.css('.mc-table-block > app-table-pager')).length).toBe(2);
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

  /** Opens the sidebar's Style tab on one family, through the real tab and radio. */
  function openStyleFamily(kind: 'bar' | 'profile' | 'scatter'): void {
    openSidebarTab('style');
    (fixture.debugElement.query(By.css(`#mc-style-family-${kind}`)).nativeElement as HTMLInputElement).click();
    fixture.detectChanges();
  }

  /** The nth trade-off checkbox in the Style tab: 0 names the marks, 1 draws their values. */
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
    render(buildDto(comparableSet(3)), 4);

    // The values toggle is on by default, so the plugin is already registered; what the names
    // toggle changes is the legend and whether a block carries a name.
    expect(scatterLegendDisplays()).toEqual([true, true, true]);
    expect(scatterPluginIds().every(ids => ids.includes(directLabelPlugin.id))).toBeTrue();
    expect(scatterBlocks()!.every(b => b.name === undefined)).toBeTrue();

    const toggle = scatterToggle(0);
    expect(toggle).withContext('the toggle sits in the Style tab, under Trade-offs').toBeTruthy();
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

  it('ends each scatter and bar card head\'s badge row with the Better badge', () => {
    render(buildDto(comparableSet(3)), 4);

    const scatterFigures = fixture.debugElement.queryAll(By.css('.mc-grid .mc-card'));
    expect(scatterFigures.length).toBe(3);
    const label = (index: number): string =>
      (scatterFigures[index].query(By.css('canvas')).nativeElement as HTMLCanvasElement).getAttribute('aria-label') ?? '';
    expect(label(0)).toContain('Better toward the top left');
    expect(label(1)).toContain('Better toward the top left');
    expect(label(2)).toContain('Better toward the bottom left');

    const panelFigures = fixture.debugElement.queryAll(By.css('.mc-panels .mc-card'));
    expect(panelFigures.length).toBe(3);
    for (const figure of [...scatterFigures, ...panelFigures]) {
      const items = figure.queryAll(By.css('.mc-card-head .mc-meta > li'));
      const pill = items[items.length - 1].nativeElement as HTMLElement;
      expect(pill.classList).toContain('mc-direction');
      expect(figure.queryAll(By.css('.mc-direction')).length).toBe(1);
      expect(pill.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
      const badgeTexts = figure.queryAll(By.css('.mc-badge')).map(badge => (badge.nativeElement as HTMLElement).textContent!);
      expect(badgeTexts.some(badgeText => badgeText.includes('Better'))).toBeFalse();
    }
    const spoken = (figure: DebugElement): string =>
      (figure.query(By.css('.mc-direction .visually-hidden')).nativeElement as HTMLElement).textContent!.trim();
    expect(spoken(scatterFigures[2])).toBe('Better toward the bottom left');
    // Intelligence is better higher: up on vertical bars, right on horizontal ones.
    expect(spoken(panelFigures[0])).toBe(
      component.effectiveOrientation === 'vertical' ? 'Better toward the top' : 'Better toward the right');
    const arrow = scatterFigures[0].query(By.css('.mc-direction-arrow')).nativeElement as SVGElement;
    expect(arrow.style.rotate).toBe('270deg');
  });

  it('shows no Better badge where the style hides it, or on the profile', () => {
    render(buildDto(comparableSet(3)), 4);
    expect(fixture.debugElement.queryAll(By.css('.mc-direction')).length).toBe(6);

    jasmine.clock().install();
    try {
      component.onFigureStyleChange({
        ...DEFAULT_FIGURE_STYLE,
        bar: { ...DEFAULT_FIGURE_STYLE.bar, hiddenBadges: ['direction'] },
        scatter: { ...DEFAULT_FIGURE_STYLE.scatter, hiddenBadges: ['direction'] }
      });
      jasmine.clock().tick(150);
      fixture.detectChanges();
      expect(fixture.debugElement.queryAll(By.css('.mc-direction')).length).toBe(0);
      for (const card of [...component.panelCards, ...component.scatterCards]) {
        expect(card.chrome.direction).withContext(card.id).toBeUndefined();
      }
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('gives the scatter heads the same action block as the panels, with no side column', () => {
    render(buildDto(comparableSet(3)), 4);

    const heads = fixture.debugElement.queryAll(By.css('.mc-grid .mc-card .mc-card-head'));
    expect(heads.length).toBe(3);
    for (const head of heads) {
      expect(head.queryAll(By.css('.mc-card-actions')).length).toBe(1);
      expect(head.queryAll(By.css('.mc-card-side')).length).toBe(0);
    }
    expect(fixture.debugElement.queryAll(By.css('.mc-card-side')).length).toBe(0);
  });

  it('draws no Better marker on any plot canvas', () => {
    render(buildDto(comparableSet(3)), 4);

    for (const card of component.exportableCards) {
      expect(card.plugins.map(plugin => plugin.id)).withContext(card.id).not.toContain('overseerDirectionMarker');
      // The export finds a card's live canvas by this id, which no rebuild changes.
      expect(fixture.debugElement.queryAll(By.css(`canvas[data-figure-id="${card.id}"]`)).length).withContext(card.id).toBe(1);
    }
    expect(component.profileCard!.chrome.direction).toBeUndefined();
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

  it('renders the thinking-level caveat and the measures that are not charted', () => {
    render(buildDto(comparableSet(3), {
      thinkingLevelsDiffer: true,
      speedAxisCaveat: 'Speed is not comparable across thinking levels.'
    }));

    expect(textOf('.alert-body')).toContain('Speed is not comparable across thinking levels.');
    expect(textOf('.mc-measures')).toContain('Cost per index point');
    expect(textOf('.mc-measures')).not.toContain('Speed Index');
  });

  it('leads each refused measure with its plain-language summary, the reason behind a disclosure', () => {
    render(buildDto(comparableSet(3)));

    // The summary is the visible line; the specialist reason is one click away rather than absent.
    expect(textOf('.mc-measure-summary'))
      .toContain('Dividing cost by a noisy score gives a number with no reliable error bars');
    expect(textOf('.mc-measures-lead')).toContain('would mislead as a chart');

    const detail = fixture.debugElement.query(By.css('.mc-measure-detail'))
      .nativeElement as HTMLDetailsElement;
    expect(detail.open).toBeFalse();
    expect(detail.querySelector('summary')?.textContent?.trim()).toBe('Why, in full');
    expect(detail.textContent).toContain('A ratio of two noisy estimators');
    expect(textOf('.mc-measure-instead')).toContain('Candidate $ / question in the step 3 table');
  });

  it('shows no refused-measure cards over a set the figures cannot draw', () => {
    render(buildDto(comparableSet(1)));

    expect(component.shape).toBe('single');
    expect(fixture.debugElement.query(By.css('.mc-measures'))).toBeNull();
  });

  it('names Speed Index saturation in the speed hint only where an entry is saturated', () => {
    const entries = comparableSet(2);
    entries[0] = { ...entries[0], table: { ...entries[0].table!, speedIndexSaturated: true } };
    render(buildDto(entries));

    expect(component.speedIndexSaturatedCount).toBe(1);
    expect(textOf('.mc-speed-hint')).toContain('saturated for 1 of 2');

    render(buildDto(comparableSet(2)));

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

  it('gives every figure card one control, Open in the preview, and the set one Download all', () => {
    render(buildDto(comparableSet(3)), 4);

    const cards = fixture.debugElement.queryAll(By.css('.mc-card'));
    expect(cards.length).toBe(7);
    for (const card of cards) {
      const actions = card.queryAll(By.css('.mc-card-actions button'));
      expect(actions.length).toBe(1);
      // An icon-only button has no text, so aria-label is its accessible name — and it has to name
      // the card, or seven buttons share one name in a screen reader's control list.
      expect((actions[0].nativeElement as HTMLElement).getAttribute('aria-label'))
        .toMatch(/^Open .+ in the preview$/);
    }
    // Copying and downloading one figure happen on the Preview tab only.
    expect(fixture.debugElement.queryAll(By.css('.mc-card .mc-download, .mc-card .mc-copy')).length).toBe(0);

    const step = fixture.debugElement.query(By.css('#mc-step-panel-4')).nativeElement as HTMLElement;
    const downloadAll = Array.from(step.querySelectorAll('button'))
      .filter(button => (button.textContent ?? '').includes('Download all'));
    expect(downloadAll.length).toBe(1);
    expect(downloadAll[0].closest('.mc-fig-bar')).not.toBeNull();
    // One image on the clipboard at a time, so there is deliberately no batch copy.
    expect(step.textContent).not.toContain('Copy all figures');
  });

  it('offers a WebP quality for the figures only while WebP is the chosen format', async () => {
    render(buildDto(comparableSet(3)), 4);
    openSidebarTab('download');
    expect(component.figureTab).withContext('no preview is needed to reach the export settings')
      .toBe('charts');

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

  it('renders no workspace, sidebar or figure bar where no figure is rendered', () => {
    const absent = (): void => {
      for (const selector of ['.mc-fig-workspace', '#mc-fig-sidebar', '.mc-fig-bar', '.mc-card-actions']) {
        expect(fixture.debugElement.query(By.css(selector))).withContext(selector).toBeNull();
      }
    };

    // Not merely hidden: the whole Figures step refuses to open, because there is nothing on it.
    render(buildDto(comparableSet(1)), 4);
    expect(component.shape).toBe('single');
    expect(component.isStepReachable(4)).toBeFalse();
    absent();

    render(buildDto([
      buildExcludedEntry('run:8', ['ScoringMethodVersion']),
      buildExcludedEntry('run:9', ['CandidatePromptOptions'])
    ]), 4);
    expect(component.shape).toBe('none');
    expect(component.isStepReachable(4)).toBeFalse();
    expect(fixture.debugElement.query(By.css('.mc-card'))).toBeNull();
    absent();

    render(null);
    expect(component.shape).toBe('empty');
    expect(component.isStepReachable(2)).toBeFalse();
    absent();
    expect(component.canExport).toBeFalse();
  });

  it('says nothing can be charted when a refetch leaves step 4 open over an unchartable set', () => {
    render(buildDto(comparableSet(3)), 4);
    expect(fixture.debugElement.query(By.css('.mc-fig-workspace'))).not.toBeNull();

    fixture.componentRef.setInput('comparison', buildDto(comparableSet(1)));
    fixture.detectChanges();

    expect(component.step).toBe(4);
    expect(fixture.debugElement.query(By.css('.mc-fig-workspace'))).toBeNull();
    expect(textOf('.mc-fig-empty')).toContain('Nothing in this set can be charted.');
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

  it('chooses emphasis in the sidebar\'s Emphasis tab, one checkbox per plotted entry', () => {
    render(buildDto([...comparableSet(3), buildExcludedEntry('run:9', ['ScoringMethodVersion'])]), 4);

    // The sidebar opens on Emphasis.
    expect(component.sidebarTab).toBe('emphasis');
    const fieldset = fixture.debugElement.query(By.css('#mc-side-panel-emphasis .mc-emphasis'))
      .nativeElement as HTMLElement;
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

  it('labels each trade-off checkbox in the Style tab, and offers neither in the Charts panel', () => {
    render(buildDto(comparableSet(3)), 4);

    const names = scatterToggle(0).nativeElement as HTMLInputElement;
    expect(names.closest('label')?.textContent).toContain('Label models inside the chart');
    const values = scatterToggle(1).nativeElement as HTMLInputElement;
    expect(values.closest('label')?.textContent).toContain('Show values in the chart');

    const charts = fixture.debugElement.query(By.css('#mc-fig-panel-charts')).nativeElement as HTMLElement;
    expect(charts.querySelectorAll('input[type="checkbox"]').length).toBe(0);
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
    expect(textOf('.mc-wizard-selection-hint')).toContain('Tick runs or groups above');
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

  it('labels the footer Next rather than Comparing while a later step refetches', () => {
    render(buildDto(comparableSet(3)), 2);
    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();

    // A pricing-basis refetch loads too, and Next on step 2 is not blocked by it.
    expect(component.comparing).toBeFalse();
    expect(component.nextLabel).toBe('Next');
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

    const next = nextButton();
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
    const next = nextButton();
    expect(close.disabled).toBeTrue();
    expect(next.disabled).toBeTrue();
    // The wizard's two are the only close controls: no preview dialog carries a third.
    expect(fixture.debugElement.queryAll(By.css('.btn-icon-action')).length).toBe(1);
    expect(fixture.debugElement.query(By.css('dialog.mc-preview-dialog'))).toBeNull();

    component.exporting = false;
    refresh();
    expect(close.disabled).toBeFalse();
    expect(next.disabled).toBeFalse();
  });

  // -------------------------------------------------------------------------------------------
  // Export resolution in the sidebar's Download tab, and the Preview tab that shows its effect
  // -------------------------------------------------------------------------------------------

  /** The Preview tab's button in the figure bar. */
  function previewTabButton(): HTMLButtonElement {
    return fixture.debugElement.query(By.css('#mc-fig-tab-preview')).nativeElement as HTMLButtonElement;
  }

  /**
   * Shows the Preview tab: on `card` through that card's own eye button, or on the first figure
   * through the tab itself.
   */
  function openPreview(card?: ComparisonFigureCard): void {
    if (card) {
      const eye = fixture.debugElement.queryAll(By.css('.mc-card .mc-preview-open'))
        .map(button => button.nativeElement as HTMLButtonElement)
        .find(button => button.getAttribute('aria-label') === `Open ${card.title} in the preview`);
      expect(eye).withContext(`the eye button of ${card.title}`).toBeTruthy();
      eye!.click();
    } else {
      previewTabButton().click();
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

  it('shows the two custom size inputs only for Custom, and names what will be written', () => {
    render(buildDto(comparableSet(3)), 4);
    openSidebarTab('download');
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
    openSidebarTab('download');
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
    openSidebarTab('download');

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
    openSidebarTab('download');

    expect(component.exportDensitySelection).toBe('custom');
    expect(component.customExportDensityPercent).toBe(220);
    expect(component.exportDensity).toBe(2.2);
    expect(fixture.debugElement.query(By.css('#mc-export-density-custom'))).toBeTruthy();
    expect(component.exportSizeError).toBe('');
  });

  it('offers every Windows display scaling step, and Custom below them', () => {
    withDisplayDensity(1);
    render(buildDto(comparableSet(3)), 4);
    openSidebarTab('download');

    const options = fixture.debugElement
      .queryAll(By.css('#mc-export-density option'))
      .map(option => ((option.nativeElement as HTMLOptionElement).textContent ?? '').trim());
    expect(options).toEqual([
      '100% (this display)', '125%', '150%', '175%', '200%', '250%', '300%', '350%', '400%',
      'Custom…'
    ]);
  });

  it('multiplies the written size by the density in the read-out and the Download all summary', () => {
    render(buildDto(comparableSet(3)), 4);
    openSidebarTab('download');
    component.onExportResolutionChange('fullhd');

    component.onExportDensityChange(2);
    refresh();
    const doubled = textOf('.mc-export-dimensions');
    expect(doubled).toContain('3840 × 2160 px');
    expect(doubled).toContain('1920 × 1080 at 200%');
    expect(doubled).toContain('4× density');
    expect(textOf('#mc-tip-download-all')).toContain('200%');

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
    openSidebarTab('download');
    component.onExportDensityChange(1.5);
    refresh();

    const readout = textOf('.mc-export-dimensions');
    expect(readout).toContain('on-screen size at 150%');
    expect(readout).not.toContain('Twice');
    expect(component.exportResolution.label).toBe('On-screen');
  });

  it('refuses a custom density outside its bounds, and will not export under one', () => {
    render(buildDto(comparableSet(3)), 4);
    openSidebarTab('download');
    component.onExportDensityChange('custom');
    component.onCustomDensityChange(900);
    refresh();

    expect(component.customDensityError).toContain(`${component.maxExportDensityPercent}`);
    expect(component.exportSizeError).toBe(component.customDensityError);
    expect(component.canExport).toBeFalse();
    expect(textOf('#mc-side-panel-download .mc-export-error')).toContain('800');

    component.onCustomDensityChange(150);
    refresh();
    expect(component.customDensityError).toBe('');
    expect(component.exportDensity).toBe(1.5);
    expect(component.canExport).toBeTrue();
  });

  it('refuses a bitmap the browser could not allocate, and marks every export control unavailable', () => {
    render(buildDto(comparableSet(3)), 4);
    openSidebarTab('download');
    openPreview();
    component.onExportResolutionChange('custom');
    component.onCustomWidthChange(8000);
    component.onCustomHeightChange(8000);
    component.onExportDensityChange(3);
    refresh();

    expect(component.exportSizeError).toContain('24000 × 24000');
    expect(component.exportSizeError).toContain('16384');
    expect(component.canExport).toBeFalse();
    // Copy, Download and Download all: aria-disabled, so each stays focusable and its reason reachable.
    const controls = [
      ...fixture.debugElement.queryAll(By.css('.mc-preview-export button')),
      fixture.debugElement.query(By.css('.mc-fig-download-all'))
    ].map(button => button.nativeElement as HTMLButtonElement);
    expect(controls.length).toBe(3);
    expect(controls.every(button => button.getAttribute('aria-disabled') === 'true')).toBeTrue();
    expect(controls.every(button => !button.disabled)).toBeTrue();
    expect(component.downloadAllTooltip).toBe(component.exportSizeError);

    component.onExportDensityChange(2);
    refresh();
    expect(component.exportSizeError).toBe('');
    expect(component.canExport).toBeTrue();
    expect(controls.every(button => button.getAttribute('aria-disabled') === null)).toBeTrue();
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
    openSidebarTab('download');
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

  it('opens the Preview tab on one card and steps through the set, wrapping at both ends', () => {
    render(buildDto(comparableSet(3)), 4);
    const cards = component.exportableCards;
    expect(cards.length).toBe(7);

    openPreview(cards[2]);
    expect(component.figureTab).toBe('preview');
    expect(component.previewCardId).toBe(cards[2].id);
    expect(component.previewActive).toBeTrue();
    expect(previewTabButton().getAttribute('aria-selected')).toBe('true');
    // A screen reader lands on "Figure, <title>".
    expect(document.activeElement?.id).toBe('mc-preview-figure');

    component.previewNext();
    expect(component.previewCardId).toBe(cards[3].id);

    component.selectPreviewCard(cards[0].id);
    component.previewPrevious();
    expect(component.previewCardId).toBe(cards[cards.length - 1].id);

    component.previewNext();
    expect(component.previewCardId).toBe(cards[0].id);

    // The tab itself shows the last figure previewed, or the first where none was.
    component.selectFigureTab('charts');
    refresh();
    openPreview();
    expect(component.previewCardId).toBe(cards[0].id);
  });

  /** Shows the Preview tab on a card and the sidebar's Style tab, both through the real tabs. */
  function openStyleTab(card?: ComparisonFigureCard): void {
    openPreview(card);
    openSidebarTab('style');
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
   * The text of every note drawn on the page under one card, found by its place in a group, as one
   * string: each note carries a visually hidden tone prefix before its own text.
   */
  function pageNotes(group: '.mc-panels' | '.mc-grid', index: number): string {
    const card = fixture.debugElement.queryAll(By.css(`${group} .mc-card`))[index];
    return card.queryAll(By.css('.mc-note'))
      .map(note => ((note.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ').trim())
      .join(' | ');
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

  it('drives the page from both trade-off toggles in the Style tab', () => {
    render(buildDto(comparableSet(3)), 4);
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

  it('fills single-run bars from the Style tab, persists it, and keeps no second copy of the control', () => {
    render(buildDto(comparableSet(3).map(entry => ({ ...entry, runCount: 1 }))), 4);

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

  it('re-composes the preview when a trade-off toggle is changed in the Style tab', () => {
    render(buildDto(comparableSet(3)), 4);

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
    selected: () => string
  ): void {
    const tabs = (): HTMLButtonElement[] =>
      fixture.debugElement.queryAll(By.css(`${listSelector} [role="tab"]`))
        .map(tab => tab.nativeElement as HTMLButtonElement);
    const ids = labels.map(label => label.toLowerCase());

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

  it('offers the sidebar\'s three sections as tabs with the full tab contract', () => {
    render(buildDto(comparableSet(3)), 4);

    expectTabContract('.mc-fig-sidebar-tabs', 'Figure settings sections', 'mc-side-tab-', 'mc-side-panel-',
      ['Emphasis', 'Style', 'Download'], () => component.sidebarTab);
    // Only the selected section's panel is rendered.
    expect(fixture.debugElement.query(By.css('#mc-side-panel-download'))).not.toBeNull();
    expect(fixture.debugElement.query(By.css('#mc-side-panel-emphasis'))).toBeNull();
    expect(fixture.debugElement.query(By.css('#mc-side-panel-style'))).toBeNull();
  });

  it('offers Charts and Preview as tabs with the full tab contract', () => {
    render(buildDto(comparableSet(3)), 4);

    expectTabContract('.mc-fig-tabs', 'Figure views', 'mc-fig-tab-', 'mc-fig-panel-',
      ['Charts', 'Preview'], () => component.figureTab);
    expect(component.previewActive).toBeTrue();
    expect(fixture.debugElement.query(By.css('#mc-fig-panel-preview'))).not.toBeNull();
    // Both tabs carry a glyph, or neither would.
    const glyphs = fixture.debugElement.queryAll(By.css('.mc-fig-tabs [role="tab"] svg.btn-icon'));
    expect(glyphs.length).toBe(2);
  });

  it('shows the bar set on a panel, the trade-off set on a scatter and the profile set on the profile', () => {
    render(buildDto(comparableSet(3)), 4);
    openStyleTab(component.panelCards[0]);

    const has = (selector: string): boolean => fixture.debugElement.query(By.css(selector)) !== null;
    expect(has('#mc-style-bar-heading')).toBeTrue();
    expect(has('#mc-style-scatter-heading')).toBeFalse();
    expect(textOf('#mc-side-panel-style')).toContain(
      'Every change here applies to the page, the preview and every download.');

    // Switching figure keeps the tab, and the set follows the figure's kind.
    component.selectPreviewCard(component.scatterCards[0].id);
    refresh();
    expect(component.sidebarTab).toBe('style');
    expect(component.styleFamily).toBe('scatter');
    expect(has('#mc-style-scatter-heading')).toBeTrue();
    expect(has('#mc-style-bar-heading')).toBeFalse();

    component.selectPreviewCard(component.profileCard!.id);
    refresh();
    expect(has('#mc-style-bar-heading')).toBeFalse();
    expect(has('#mc-style-scatter-heading')).toBeFalse();
    expect(has('#mc-style-profile-heading')).toBeTrue();
    expect(textOf('.fsp-note')).toContain('Chart text follows Text size on the Download tab.');
  });

  it('stores a style change at once, persists it, and rebuilds the figures after the debounce', () => {
    render(buildDto(comparableSet(3)), 4);

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
    render(buildDto(comparableSet(3)), 4);

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

  /** A part of one page card on the Charts tab, or null while it is not rendered. */
  function cardPart(cardId: string, selector: string): HTMLElement | null {
    return (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLElement>(`figure.mc-card[aria-labelledby="${cardId}-title"] ${selector}`);
  }

  function cardPartSize(cardId: string, selector: string): string {
    const part = cardPart(cardId, selector);
    expect(part).withContext(`${cardId} ${selector}`).not.toBeNull();
    return part ? getComputedStyle(part).fontSize : '';
  }

  /** Applies one family's style change the way the Style tab does, and re-renders without a tick. */
  function changeFamilyStyle<K extends 'bar' | 'scatter' | 'profile'>(
    family: K, change: Partial<FigureStyle[K]>
  ): void {
    component.onFigureStyleChange({
      ...component.figureStyle,
      [family]: { ...component.figureStyle[family], ...change }
    });
    refresh();
  }

  it('sizes the page card titles from each family\'s Heading size', () => {
    render(buildDto(comparableSet(3)), 4);

    jasmine.clock().install();
    try {
      changeFamilyStyle('bar', { titleSizePx: 30 });
      expect(component.panelCards.length).toBe(3);
      for (const card of component.panelCards) {
        expect(cardPartSize(card.id, '.mc-card-title')).withContext(card.id).toBe('30px');
      }
      expect(cardPartSize(component.scatterCards[0].id, '.mc-card-title')).toBe('18px');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('sizes the page badges, the Better badge and its arrow from Badge text size', () => {
    render(buildDto(comparableSet(3)), 4);

    jasmine.clock().install();
    try {
      changeFamilyStyle('scatter', { badgeTextSizePx: 20 });
      const id = component.scatterCards[0].id;
      expect(cardPartSize(id, '.mc-badge')).toBe('20px');
      expect(cardPartSize(id, '.mc-direction')).toBe('20px');
      const arrow = cardPart(id, '.mc-direction-arrow');
      expect(arrow).not.toBeNull();
      expect(getComputedStyle(arrow!).width).toBe('26px');
      expect(cardPartSize(component.panelCards[0].id, '.mc-badge')).toBe('11px');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('shows the figure footer on every page card, and drops it where Show footer is off', () => {
    render(buildDto(comparableSet(3)), 4);
    const footers = (cardId: string): HTMLElement[] => Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
        `figure.mc-card[aria-labelledby="${cardId}-title"] .mc-card-footer`));
    const cards = [...component.panelCards, component.profileCard!, ...component.scatterCards];
    for (const card of cards) {
      const found = footers(card.id);
      expect(found.length).withContext(card.id).toBe(1);
      expect(found[0].textContent).toContain('GnollHack Player Assistance Benchmark Suite');
      expect(found[0].textContent).toContain('Computed');
    }

    jasmine.clock().install();
    try {
      changeFamilyStyle('profile', { footer: false });
      expect(footers(component.profileCard!.id).length).toBe(0);
      for (const card of component.panelCards) {
        expect(footers(card.id).length).withContext(card.id).toBe(1);
      }
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('sizes the page footer from Footer text size', () => {
    render(buildDto(comparableSet(3)), 4);

    jasmine.clock().install();
    try {
      changeFamilyStyle('bar', { footerTextSizePx: 16 });
      expect(cardPartSize(component.panelCards[0].id, '.mc-card-footer')).toBe('16px');
      expect(cardPartSize(component.scatterCards[0].id, '.mc-card-footer')).toBe('12px');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('follows a style change on the page at once, before the charts rebuild', () => {
    render(buildDto(comparableSet(3)), 4);
    const id = component.profileCard!.id;
    expect(cardPartSize(id, '.mc-card-title')).toBe('18px');

    jasmine.clock().install();
    try {
      changeFamilyStyle('profile', { titleSizePx: 24 });
      expect(cardPartSize(id, '.mc-card-title')).toBe('24px');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('adds the hidden-intervals note to the Intelligence card when its bars are hidden, and drops it on request', () => {
    render(buildDto(comparableSet(3)), 4);
    openStyleTab(component.panelCards[0]);

    const noteToggle = styleControl('mc-style-bar-hiddenIntervalsNote');
    expect(noteToggle.disabled).toBeTrue();
    expect(pageNotes('.mc-panels', 0)).not.toContain(HIDDEN_INTERVALS_NOTE);

    withStyleDebounce(() => setChecked(styleControl('mc-style-bar-intervals'), false));
    expect(pageNotes('.mc-panels', 0)).toContain(HIDDEN_INTERVALS_NOTE);
    // The Speed panel on mean time draws no whisker anyway, so it gains nothing.
    expect(pageNotes('.mc-panels', 1)).not.toContain(HIDDEN_INTERVALS_NOTE);
    expect(component.panelCards[0].plugins).not.toContain(errorBarPlugin);

    expect(styleControl('mc-style-bar-hiddenIntervalsNote').disabled).toBeFalse();
    withStyleDebounce(() => setChecked(styleControl('mc-style-bar-hiddenIntervalsNote'), false));
    expect(pageNotes('.mc-panels', 0)).not.toContain(HIDDEN_INTERVALS_NOTE);
  });

  it('drops the frontier note from the scatter card and its export on request, keeping the set notes', () => {
    render(buildDto([...comparableSet(3), buildExcludedEntry('run:9', ['ScoringVersion'])]), 4);
    const setNotes = component.setFigureNotes.map(note => note.text);
    expect(setNotes.length).toBeGreaterThan(0);

    const s1 = (): ComparisonFigureCard => component.scatterCards[0];
    expect(s1().chrome.notes.map(note => note.text)).toContain(FRONTIER_UNCERTAINTY_NOTE);
    expect(exportNotesOf(s1())).toContain(FRONTIER_UNCERTAINTY_NOTE);

    openStyleTab(s1());
    withStyleDebounce(() => setChecked(styleControl('mc-style-scatter-frontierIntervalsNote'), false));

    expect(s1().chrome.notes.map(note => note.text)).not.toContain(FRONTIER_UNCERTAINTY_NOTE);
    expect(pageNotes('.mc-grid', 0)).not.toContain(FRONTIER_UNCERTAINTY_NOTE);
    expect(exportNotesOf(s1())).not.toContain(FRONTIER_UNCERTAINTY_NOTE);
    for (const text of setNotes) {
      expect(exportNotesOf(s1())).toContain(text);
    }
  });

  it('states the scored questions in the badge and says why the rest are left out, in the export too', () => {
    const entries = comparableSet(2).map(entry => ({
      ...entry,
      quality: { ...entry.quality!, itemCount: 16, revisedItemCount: 2 }
    }));
    render(buildDto(entries), 4);

    const revisedNote = "2 of the suite's 18 questions are left out: their rubrics were revised after these runs, " +
      'so the stored grades are for the old rubrics. Runs made from now on include them.';
    expect(component.setFigureNotes).toContain({ text: revisedNote, tone: 'info' });

    const s1 = component.scatterCards[0];
    expect(s1.chrome.badges.find(badge => badge.kind === 'questions')?.text).toBe('16 of 18 questions');
    expect(exportNotesOf(s1)).toContain(revisedNote);
  });

  it('warns only about the plotted entries\' unscored questions', () => {
    const entries = comparableSet(3).map((entry, index) => index === 2
      ? { ...entry, quality: { ...entry.quality!, itemCount: 17, unscoredItemCount: 1 } }
      : entry);
    render(buildDto(entries), 4);

    const warning = 'Model 3: 1 question has no scored answer (failed, skipped or ungraded) and is left out of its index.';
    expect(component.setFigureNotes).toContain({ text: warning, tone: 'warning' });

    component.toggleEntry('run:3');
    expect(component.setFigureNotes.map(note => note.text)).not.toContain(warning);
  });

  it('drops the mean-time note from the Speed card and its export on request, keeping the set notes', () => {
    render(buildDto([...comparableSet(3), buildExcludedEntry('run:9', ['ScoringVersion'])]), 4);
    expect(component.speedMeasure).toBe('meanModelTime');
    const setNotes = component.setFigureNotes.map(note => note.text);
    const speed = (): ComparisonFigureCard => component.panelCards[1];
    expect(pageNotes('.mc-panels', 1)).toContain(MEAN_TIME_NO_INTERVAL_NOTE);

    openStyleTab(speed());
    const toggle = styleControl('mc-style-bar-meanTimeNoIntervalNote');
    expect(toggle.disabled).toBeFalse();
    withStyleDebounce(() => setChecked(toggle, false));

    expect(pageNotes('.mc-panels', 1)).not.toContain(MEAN_TIME_NO_INTERVAL_NOTE);
    expect(exportNotesOf(speed())).not.toContain(MEAN_TIME_NO_INTERVAL_NOTE);
    for (const text of setNotes) {
      expect(exportNotesOf(speed())).toContain(text);
    }
  });

  it('lets a forced horizontal orientation size the panels in a wide container', () => {
    render(buildDto(comparableSet(3)), 4);
    component.applyContainerWidth(P1_STACK_BREAKPOINT_PX + 400);
    expect(component.orientation).toBe('vertical');
    expect(component.panelHeight()).toBe(340);

    openStyleTab(component.panelCards[0]);
    withStyleDebounce(() => setChecked(styleControl('mc-style-bar-orientation-horizontal'), true));

    expect(component.effectiveOrientation).toBe('horizontal');
    expect(component.orientation).toBe('vertical');
    expect(component.panelHeight()).toBe(260);
    expect((component.panelCards[0].options as { indexAxis?: string }).indexAxis).toBe('y');
  });

  it('applies a stored style and falls back to the default on unreadable storage', () => {
    localStorage.setItem(FIGURE_STYLE_STORAGE_KEY, JSON.stringify({ version: 1, bar: { gapPercent: 10 } }));
    render(buildDto(comparableSet(3)), 4);
    expect(component.figureStyle.bar.gapPercent).toBe(10);
    expect(component.figureStyle.scatter).toEqual(DEFAULT_FIGURE_STYLE.scatter);

    localStorage.setItem(FIGURE_STYLE_STORAGE_KEY, '{not json');
    const second = TestBed.createComponent(ModelComparisonComponent);
    expect(() => second.detectChanges()).not.toThrow();
    expect(second.componentInstance.figureStyle).toEqual(DEFAULT_FIGURE_STYLE);
    second.destroy();
  });

  it('feeds the Text size range into the export layout, and disables it for On-screen', async () => {
    render(buildDto(comparableSet(3)), 4);
    openSidebarTab('download');
    openPreview(component.panelCards[0]);

    const range = styleControl('mc-export-text-scale');
    expect(component.exportResolutionId).toBe('onscreen');
    expect(range.disabled).toBeTrue();

    component.onExportResolutionChange('square1080');
    refresh();
    expect(styleControl('mc-export-text-scale').disabled).toBeFalse();
    expect(component.exportDimensionsLabel).toContain('laid out at 960 × 960');

    setRange(styleControl('mc-export-text-scale'), 200);
    expect(component.exportTextScalePercent).toBe(200);
    expect(component.exportDimensionsLabel).toContain('laid out at 480 × 480');

    component.onExportResolutionChange('custom');
    component.lockCustomRatio(false);
    component.onCustomWidthChange(640);
    component.onCustomHeightChange(640);
    setRange(styleControl('mc-export-text-scale'), 250);
    await composePreview();
    expect(component.previewRefusal).toContain('caption column would be narrower than 360 px');
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
    openSidebarTab('download');
    openPreview(card);

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
    render(buildDto(comparableSet(3)), 4);
    spyOn(component, 'measureStage').and.returnValue({ width: 800, height: 600, devicePixelRatio: 2 });
    openPreview();
    component.onExportDensityChange(1);
    component.onExportResolutionChange('fullhd');
    await composePreview();
  }

  /** A custom 800 × 600 at 100 % density on the same stage: the screen fit is 2. */
  async function openCustomPreview(): Promise<void> {
    render(buildDto(comparableSet(3)), 4);
    spyOn(component, 'measureStage').and.returnValue({ width: 800, height: 600, devicePixelRatio: 2 });
    openPreview();
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

  it('returns to the Preview tab in the default view', async () => {
    await openFullHdPreview();
    component.setPreviewView(4);

    component.selectFigureTab('charts');
    refresh();
    openPreview();

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

  it('stops watching the stage on switching to Charts', () => {
    render(buildDto(comparableSet(3)), 4);
    const observers = installFakeResizeObserver();
    openPreview();
    const { watching, removed } = watchedStage(observers);
    expect(watching.length).toBe(1);

    (fixture.debugElement.query(By.css('#mc-fig-tab-charts')).nativeElement as HTMLButtonElement).click();
    refresh();

    expect(watching[0].disconnected).toBe(1);
    expect(component.previewActive).toBeFalse();
    expect(removed).toHaveBeenCalledWith('wheel', jasmine.any(Function), jasmine.anything());
    expect(fixture.debugElement.query(By.css('#mc-fig-panel-preview'))).toBeNull();
  });

  it('stops watching the stage on leaving step 4, and on destroy', async () => {
    render(buildDto(comparableSet(3)), 4);
    const observers = installFakeResizeObserver();
    openPreview();
    const first = watchedStage(observers);

    component.goToStep(3);
    fixture.detectChanges();
    expect(first.watching[0].disconnected).toBe(1);
    expect(first.removed).toHaveBeenCalledWith('pointerdown', jasmine.any(Function), undefined);
    expect(component.figureTab).withContext('kept, so returning shows the Preview tab again').toBe('preview');

    component.goToStep(4);
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
    render(buildDto(comparableSet(3)), 4);
    const observers = installFakeResizeObserver();
    openPreview();
    const { watching, viewport, removed } = watchedStage(observers);

    // A refetch down to one entry: step 4 stays open, and the workspace goes with the figures.
    fixture.componentRef.setInput('comparison', buildDto(comparableSet(1)));
    fixture.detectChanges();

    expect(viewport.isConnected).toBeFalse();
    expect(component.previewActive).toBeFalse();
    expect(watching[0].disconnected).toBe(1);
    expect(removed).toHaveBeenCalledWith('wheel', jasmine.any(Function), jasmine.anything());
    expect(removed).toHaveBeenCalledWith('pointerdown', jasmine.any(Function), undefined);
  });

  it('carries the export size and format in the Download all tooltip, and a size error instead of it', () => {
    render(buildDto(comparableSet(3)), 4);

    component.onExportDensityChange(1);
    component.onExportResolutionChange('fullhd');
    refresh();
    expect(component.downloadAllTooltip).toContain(component.exportSummary);
    const tooltip = (): string => textOf('#mc-tip-download-all');
    expect(tooltip()).toContain('All figures as one archive');
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

  it('offers an open-in-preview control on every figure card, naming its figure and never disabled', () => {
    render(buildDto(comparableSet(3)), 4);

    const previews = (): HTMLButtonElement[] => fixture.debugElement.queryAll(By.css('.mc-card .mc-preview-open'))
      .map(button => button.nativeElement as HTMLButtonElement);
    expect(previews().length).toBe(7);

    // Icon-only, so aria-label is the accessible name — and seven of them must not share one.
    const names = previews().map(button => button.getAttribute('aria-label') ?? '');
    expect(names.every(name => name.startsWith('Open ') && name.endsWith(' in the preview')))
      .toBeTrue();
    expect(new Set(names).size).toBe(7);
    expect(previews().every(button => button.querySelector('path')?.getAttribute('d')?.startsWith('M1 12s4-8')))
      .withContext('the eye glyph').toBeTrue();

    // The preview is where a size error is shown and fixed, so it stays reachable under one.
    component.onExportResolutionChange('custom');
    component.onCustomWidthChange(10);
    refresh();
    expect(component.canExport).toBeFalse();
    expect(previews().every(button => !button.disabled && button.getAttribute('aria-disabled') === null))
      .toBeTrue();
    expect(previews().every(button => button.textContent?.trim() === '')).toBeTrue();
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
    // The suite and the pricing basis are in the wizard header; the line under the intro carries
    // only the computation time.
    expect(textOf('.mc-table-computed')).toContain('Computed');
    expect(textOf('.mc-table-computed')).not.toContain('Current catalog');
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

  it('composes each figure at its own family\'s caption sizes, and empties the footer where it is hidden', () => {
    render(buildDto(comparableSet(3)), 4);
    const exportChrome = (card: ComparisonFigureCard) => (component as unknown as {
      exportChrome(card: ComparisonFigureCard): { footer: FigureFooter; textSizes?: { titlePx: number; badgePx: number; footerPx: number } };
    }).exportChrome(card);

    expect(exportChrome(component.panelCards[0]).textSizes).toEqual({ titlePx: 18, badgePx: 11, footerPx: 12 });

    component.figureStyle = {
      bar: { ...DEFAULT_FIGURE_STYLE.bar, titleSizePx: 30, badgeTextSizePx: 14, footerTextSizePx: 16 },
      scatter: { ...DEFAULT_FIGURE_STYLE.scatter, footer: false },
      profile: { ...DEFAULT_FIGURE_STYLE.profile, titleSizePx: 22 }
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

  it('lays the preview toolbar out as three labelled groups of tooltipped icon buttons', () => {
    render(buildDto(comparableSet(3)), 4);
    openPreview();

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
    const step = fixture.debugElement.query(By.css('#mc-step-panel-4')).nativeElement as HTMLElement;
    for (const button of Array.from(step.querySelectorAll<HTMLButtonElement>('button.action-btn'))) {
      expect(button.getAttribute('aria-label')).withContext(button.outerHTML).toBeTruthy();
      expect(button.getAttribute('interestfor')).withContext(button.outerHTML).toBeTruthy();
      expect(button.getAttribute('type')).toBe('button');
    }
    expect(step.querySelectorAll('[title]').length).toBe(0);

    // The one image button, and only one, is the figure's Download.
    const primary = Array.from(toolbar.querySelectorAll('.btn-gh'));
    expect(primary.length).toBe(1);
    expect(primary[0].textContent!.trim()).toBe('Download');
  });

  it('hands the notice to its one toast', async () => {
    render(buildDto(comparableSet(3)), 4);
    withClipboard({ write: () => Promise.resolve() });

    await component.copyFigure(component.panelCards[0]);
    refresh();

    expect(fixture.debugElement.queryAll(By.css('app-toast')).length).toBe(1);
    const toast = fixture.debugElement.query(By.directive(ToastComponent)).componentInstance as ToastComponent;
    expect(toast.notice).toBe(component.exportNotice);

    // The Preview tab is not a modal, so the same toast still carries it there.
    openPreview();
    expect(toast.notice).toBe(component.exportNotice);
  });

  // -------------------------------------------------------------------------------------------
  // The step-4 workspace: sidebar, figure tabs, and the live charts under the preview
  // -------------------------------------------------------------------------------------------

  function sidebarToggle(): HTMLButtonElement {
    return fixture.debugElement.query(By.css('.mc-fig-sidebar-toggle')).nativeElement as HTMLButtonElement;
  }

  function sidebar(): HTMLElement {
    return fixture.debugElement.query(By.css('#mc-fig-sidebar')).nativeElement as HTMLElement;
  }

  function storedSidebar(): { version: number; collapsed: boolean; tab: string } {
    return JSON.parse(localStorage.getItem(FIGURE_SIDEBAR_STORAGE_KEY)!);
  }

  /** A second instance over the same payload, built after storage was written. */
  function secondInstance(): ComponentFixture<ModelComparisonComponent> {
    const second = TestBed.createComponent(ModelComparisonComponent);
    second.componentRef.setInput('comparison', buildDto(comparableSet(3)));
    second.detectChanges();
    second.componentInstance.goToStep(4);
    second.detectChanges();
    return second;
  }

  it('collapses the sidebar from its disclosure, persists it, and restores it in a new instance', () => {
    render(buildDto(comparableSet(3)), 4);

    const toggle = sidebarToggle();
    expect(toggle.getAttribute('aria-label')).toBe('Figure settings');
    expect(toggle.getAttribute('aria-controls')).toBe('mc-fig-sidebar');
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(sidebar().hidden).toBeFalse();
    expect(textOf('#mc-tip-sidebar')).toContain('Hide figure settings');

    toggle.click();
    fixture.detectChanges();

    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    // The name stays constant; the state is aria-expanded's.
    expect(toggle.getAttribute('aria-label')).toBe('Figure settings');
    expect(sidebar().hidden).toBeTrue();
    expect(getComputedStyle(sidebar()).display).toBe('none');
    expect(fixture.debugElement.query(By.css('.mc-fig-workspace.is-collapsed'))).not.toBeNull();
    expect(textOf('#mc-tip-sidebar')).toContain('Show figure settings');
    expect(storedSidebar()).toEqual({ version: 1, collapsed: true, tab: 'emphasis' });

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
    expect(blocked.componentInstance.sidebarTab).toBe('emphasis');
    expect(() => blocked.componentInstance.toggleSidebar()).not.toThrow();
    expect(() => blocked.componentInstance.selectSidebarTab('style')).not.toThrow();
    expect(blocked.componentInstance.sidebarCollapsed).toBeTrue();
    expect(blocked.componentInstance.sidebarTab).toBe('style');
    blocked.destroy();
  });

  it('restores the sidebar tab, and falls back to Emphasis on an unknown or malformed one', () => {
    render(buildDto(comparableSet(3)), 4);
    openSidebarTab('download');
    expect(storedSidebar()).toEqual({ version: 1, collapsed: false, tab: 'download' });

    let second = secondInstance();
    expect(second.componentInstance.sidebarTab).toBe('download');
    expect((second.nativeElement as HTMLElement).querySelector('#mc-side-panel-download')).not.toBeNull();
    second.destroy();

    localStorage.setItem(FIGURE_SIDEBAR_STORAGE_KEY, JSON.stringify({ version: 1, collapsed: 'yes', tab: 'layout' }));
    second = secondInstance();
    expect(second.componentInstance.sidebarTab).toBe('emphasis');
    expect(second.componentInstance.sidebarCollapsed).toBeFalse();
    second.destroy();

    localStorage.setItem(FIGURE_SIDEBAR_STORAGE_KEY, '{not json');
    second = secondInstance();
    expect(second.componentInstance.sidebarTab).toBe('emphasis');
    second.destroy();
  });

  it('opens Download for a stored tab of its earlier name, export', () => {
    render(buildDto(comparableSet(3)), 4);
    localStorage.setItem(FIGURE_SIDEBAR_STORAGE_KEY, JSON.stringify({ version: 1, collapsed: false, tab: 'export' }));

    const second = secondInstance();
    expect(second.componentInstance.sidebarTab).toBe('download');
    expect((second.nativeElement as HTMLElement).querySelector('#mc-side-panel-download')).not.toBeNull();
    second.destroy();
  });

  it('says on the Download tab that its settings affect downloads only', () => {
    render(buildDto(comparableSet(3)), 4);
    openSidebarTab('download');

    expect(textOf('#mc-side-panel-download .mc-download-scope')).toContain('These settings affect downloads only.');
  });

  it('centres Download all and the sidebar toggle on the figure bar', () => {
    render(buildDto(comparableSet(3)), 4);
    expect(component.figureTab).toBe('charts');

    const box = (selector: string): DOMRect =>
      (fixture.debugElement.query(By.css(selector)).nativeElement as HTMLElement).getBoundingClientRect();
    const bar = box('.mc-fig-bar');
    // The bar's 1 px bottom border is not part of the height the buttons centre on.
    const barCentre = bar.top + (bar.height - 1) / 2;
    for (const selector of ['.mc-fig-download-all', '.mc-fig-sidebar-toggle']) {
      const button = box(selector);
      expect(Math.abs(button.top + button.height / 2 - barCentre)).withContext(selector).toBeLessThanOrEqual(1);
    }
  });

  it('keeps every chart canvas alive, inert and invisible under the Preview tab, and exports from it', async () => {
    render(buildDto(comparableSet(3)), 4);
    const canvases = (): HTMLCanvasElement[] => Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLCanvasElement>('canvas[data-figure-id]'));
    expect(canvases().length).toBe(7);

    openPreview(component.panelCards[0]);

    expect(canvases().length).withContext('the live canvases every export composes from').toBe(7);
    const charts = fixture.debugElement.query(By.css('#mc-fig-panel-charts')).nativeElement as HTMLElement;
    expect(charts.hasAttribute('inert')).toBeTrue();
    expect(charts.classList).toContain('is-inactive');
    expect(getComputedStyle(charts).display).not.toBe('none');
    expect(getComputedStyle(charts).visibility).toBe('hidden');

    captureSaves();
    const exportOne = spyOn(
      component as unknown as { exportOneFigure(card: ComparisonFigureCard, canvas: HTMLCanvasElement | null): Promise<unknown> },
      'exportOneFigure'
    ).and.returnValue(Promise.resolve({ result: null, refusal: null, liveFallback: false, pixels: '' }));
    await component.downloadPreviewedFigure();

    expect(exportOne).toHaveBeenCalledTimes(1);
    const [card, canvas] = exportOne.calls.mostRecent().args;
    expect(card.id).toBe(component.panelCards[0].id);
    expect(canvas).not.toBeNull();
    expect(canvas!.isConnected).toBeTrue();

    (fixture.debugElement.query(By.css('#mc-fig-tab-charts')).nativeElement as HTMLButtonElement).click();
    refresh();
    expect(charts.hasAttribute('inert')).toBeFalse();
    expect(charts.classList).not.toContain('is-inactive');
  });

  it('re-composes the preview on an emphasis toggle while it is shown, and not on the Charts tab', () => {
    render(buildDto(comparableSet(3)), 4);
    const renderPreview = spyOn(
      component as unknown as { renderPreview(): Promise<void> }, 'renderPreview'
    ).and.returnValue(Promise.resolve());
    const box = (): DebugElement => fixture.debugElement.queryAll(By.css('.mc-emphasis-list input[type="checkbox"]'))[0];

    jasmine.clock().install();
    try {
      box().triggerEventHandler('change', { target: box().nativeElement });
      fixture.detectChanges();
      jasmine.clock().tick(200);
      expect(renderPreview).not.toHaveBeenCalled();

      openPreview();
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

  it('lights no hover highlight on the Preview tab, and still clears one', () => {
    render(buildDto(comparableSet(3)), 4);
    component.setHighlight('run:1');
    expect(component.highlightedKey).toBe('run:1');

    openPreview();
    expect(component.highlightedKey).withContext('entering the preview clears it').toBeNull();

    component.setHighlight('run:2');
    expect(component.highlightedKey).toBeNull();

    component.selectFigureTab('charts');
    component.setHighlight('run:2');
    expect(component.highlightedKey).toBe('run:2');
    component.setHighlight(null);
    expect(component.highlightedKey).toBeNull();
  });

  it('keeps the Style tab on the previewed figure\'s family, and moves the preview to a chosen one', () => {
    render(buildDto(comparableSet(3)), 4);
    openSidebarTab('style');
    const families = (): string[] => fixture.debugElement.queryAll(By.css('.mc-style-family-option'))
      .map(option => ((option.nativeElement as HTMLElement).textContent ?? '').trim());
    expect(families()).toEqual(['Bar panels', 'Profile', 'Trade-offs']);
    expect((fixture.debugElement.query(By.css('.mc-style-family legend')).nativeElement as HTMLElement)
      .textContent!.trim()).toBe('Figures to style');

    // On the Charts tab, choosing a family moves nothing.
    openStyleFamily('scatter');
    expect(component.styleFamily).toBe('scatter');
    expect(component.previewCardId).toBeNull();
    expect(fixture.debugElement.query(By.css('#mc-style-scatter-heading'))).not.toBeNull();

    openPreview(component.panelCards[1]);
    expect(component.styleFamily).withContext('follows the figure on the stage').toBe('bar');
    component.selectPreviewCard(component.scatterCards[0].id);
    expect(component.styleFamily).toBe('scatter');

    component.selectStyleFamily('bar');
    expect(component.previewCardId).toBe(component.panelCards[0].id);
    component.selectStyleFamily('profile');
    expect(component.previewCardId).toBe(component.profileCard!.id);
  });

  it('offers no Profile family where the profile is suppressed', () => {
    render(buildDto(comparableSet(2)), 4);
    expect(component.profileCard).toBeNull();
    expect(component.styleFamilies.map(family => family.kind)).toEqual(['bar', 'scatter']);

    component.styleFamily = 'profile';
    expect(component.effectiveStyleFamily).toBe('bar');
  });

  it('leaves no duplicate of any control in step 4', () => {
    render(buildDto(comparableSet(3)), 4);
    openPreview();

    const step = fixture.debugElement.query(By.css('#mc-step-panel-4')).nativeElement as HTMLElement;
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

  it('returns to the Preview tab after a step away, and watches the stage again', async () => {
    render(buildDto(comparableSet(3)), 4);
    openPreview();
    const observe = spyOn(component as unknown as { observeStage(): void }, 'observeStage').and.callThrough();

    component.goToStep(3);
    fixture.detectChanges();
    expect(component.previewActive).toBeFalse();
    expect(fixture.debugElement.query(By.css('.mc-preview-stage'))).toBeNull();

    component.goToStep(4);
    fixture.detectChanges();
    // The re-attach runs in a microtask, outside the check pass that found the stage.
    await Promise.resolve();
    fixture.detectChanges();

    expect(component.figureTab).toBe('preview');
    expect(previewTabButton().getAttribute('aria-selected')).toBe('true');
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

  it('names the questions asked on the single-entry cost tile, and nothing when the payload lacks it', () => {
    const [entry] = comparableSet(1);
    render(buildDto([{ ...entry, cost: { ...entry.cost!, questionsAskedPerRun: 18 } }]));
    const costTile = () => component.singleEntryTiles.find(tile => tile.label === 'Candidate cost per question');

    expect(costTile()?.detail).toContain('over 18 questions asked per run');

    render(buildDto(comparableSet(1)));
    expect(costTile()?.detail).not.toContain('asked per run');
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
    expect(Number.isNaN(entry.totalRunCostUsd)).toBeTrue();
    expect(entry.candidateCostPerQuestionSdUsd).toBeNull();
    expect(entry.speedIndexSd).toBeNull();
    expect(entry.totalModelTimeSdMs).toBeNull();
  });

  it('reads the question counts and the pricing label off the payload header', () => {
    const context = toChartContext(null);
    expect(context.scoredItemsMin).toBe(0);
    expect(context.scoredItemsMax).toBe(0);
    expect(context.suiteItemCount).toBe(0);
    expect(context.pricingBasisLabel).toBe('Unknown pricing basis');
    expect(context.pricingBasis).toBe('');
    expect(context.pricedOn).toBe('');
    expect(context.questionsAskedPerRun).toBeNull();
  });
});
