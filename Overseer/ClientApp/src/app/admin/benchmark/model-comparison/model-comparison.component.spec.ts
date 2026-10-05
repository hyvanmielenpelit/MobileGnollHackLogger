import { DebugElement } from '@angular/core';
import { ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ComparisonFigureCard, ModelComparisonComponent } from './model-comparison.component';
import { MAX_PLOTTED_ENTRIES, P1_STACK_BREAKPOINT_PX, directLabelPlugin } from './model-comparison-charts';
import { DEFAULT_FIGURE_STYLE } from './figure-style';
import {
  buildExcludedEntry, buildDto, render, showView, renderTable, refresh, comparableSet, textOf,
  setUpModelComparisonSpec, openSidebarTab, tableRowOf, scatterLegendDisplays, scatterBlocks, openStyleFamily,
  setWithoutTotal, PRE_HARNESS_15, chooseRadio
} from './model-comparison.component.testing';

describe('ModelComparisonComponent', () => {
  let component: ModelComparisonComponent;
  let fixture: ComponentFixture<ModelComparisonComponent>;

  setUpModelComparisonSpec({
    get component() { return component; },
    set component(value) { component = value; },
    get fixture() { return fixture; },
    set fixture(value) { fixture = value; }
  }, { stubReportPackPanel: false });

  // -------------------------------------------------------------------------------------------
  // The Interactive table
  // -------------------------------------------------------------------------------------------

  it('renders the Interactive table as a view of step 2, with no control that hides it', () => {
    renderTable(buildDto(comparableSet(4)));

    const table = fixture.debugElement.query(By.css('#mc-fig-panel-table table.mc-table'));
    expect(table, 'the table is a tabpanel of its own').toBeTruthy();
    expect(fixture.debugElement.queryAll(By.css('app-table-pager')).length).toBe(2);
    expect(fixture.debugElement.queryAll(By.css('table.mc-table tbody tr')).length).toBe(4);

    // Nothing in the view may hide the table, so there is no control that could.
    const toggles = fixture.debugElement
      .queryAll(By.css('button'))
      .map(element => (element.nativeElement as HTMLElement).getAttribute('aria-label') ?? '');
    expect(toggles.some(label => /show table|hide table/i.test(label))).toBe(false);
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
    expect(headers.some(header => header.includes('TTFT'))).toBe(false);

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

  it("marks every row with its provider's color, as a circle", () => {
    const entries = comparableSet(3);
    entries[1] = { ...entries[1], provider: 'Anthropic' };
    entries[2] = { ...entries[2], provider: 'xAI' };
    renderTable(buildDto(entries));

    const glyphOf = (source: string): HTMLElement => tableRowOf(source).querySelector('.mc-glyph') as HTMLElement;
    expect(glyphOf('Run 1').classList).toContain('mc-glyph-provider-google');
    expect(glyphOf('Run 2').classList).toContain('mc-glyph-provider-anthropic');
    expect(glyphOf('Run 3').classList).toContain('mc-glyph-provider-other');
    expect(getComputedStyle(glyphOf('Run 1')).borderRadius).toBe('50%');
    expect(getComputedStyle(glyphOf('Run 1')).backgroundColor).toBe('rgb(57, 135, 229)');
    expect(glyphOf('Run 1').getAttribute('style')).toBeNull();
  });

  it("names the Pareto frontier and the faded models in a trade-off chart's accessible name", () => {
    const entries = comparableSet(3).map((entry, index) => ({ ...entry, modelDisplayName: `Model ${index + 1}` }));
    render(buildDto(entries), 2);

    // One shared mean time: the strongest model beats the other two on both axes.
    const label = component.scatterCards[0].ariaLabel;
    expect(label).toContain('Pareto frontier: Model 3 (medium). Faded: ');
    expect(label).toContain('Model 1 (medium)');
    expect(label).toContain('Model 2 (medium)');
    expect(label.endsWith('Values for every entry are in the comparison table below.')).toBe(true);
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
      expect(tab.getAttribute('aria-disabled'), view).toBe('true');
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

  function scatterPluginIds(): string[][] {
    return component.scatterCards.map(card => card.plugins.map(plugin => plugin.id));
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

  it('names the scatter marks directly by default, and swaps back to legends when the toggle is unticked', () => {
    render(buildDto(comparableSet(3)), 2);

    // Color means provider, so the names on the marks identify the models; no legend repeats them.
    expect(component.scatterDirectLabels).toBe(true);
    expect(scatterLegendDisplays()).toEqual([false, false, false]);
    expect(scatterPluginIds().every(ids => ids.includes(directLabelPlugin.id))).toBe(true);
    expect(scatterBlocks()!.every(b => typeof b.name === 'string')).toBe(true);

    const toggle = scatterToggle(0);
    expect(toggle, 'the toggle sits in the Charts tab, under Trade-offs').toBeTruthy();
    tick(toggle, false);

    expect(component.scatterDirectLabels).toBe(false);
    expect(scatterLegendDisplays()).toEqual([true, true, true]);
    // Neither toggle on: no plugin at all, and the legend names the marks.
    expect(scatterPluginIds().every(ids => ids.includes(directLabelPlugin.id))).toBe(false);

    tick(toggle, true);

    expect(scatterLegendDisplays()).toEqual([false, false, false]);
    expect(scatterBlocks()!.every(b => typeof b.name === 'string')).toBe(true);
  });

  it('labels the marks with names only by default, and adds their values when asked', () => {
    render(buildDto(comparableSet(3)), 2);

    expect(component.scatterInlineValues).toBe(false);
    const blocks = scatterBlocks()!;
    expect(blocks.length).toBe(3);
    expect(blocks.every(b => b.values.length === 0)).toBe(true);
    expect(typeof blocks[0].name).toBe('string');
    expect(blocks[0].hue).toBeTruthy();

    tick(scatterToggle(1), true);

    expect(component.scatterInlineValues).toBe(true);
    expect(scatterBlocks()![0].values.length).toBe(2);
    expect(scatterLegendDisplays()).toEqual([false, false, false]);
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
      expect(card.chrome.direction, card.id).toBeDefined();
      expect(card.chrome.badges.some(badge => badge.text.includes('Better')), card.id).toBe(false);
    }
  });

  it('shows no Better badge where the style hides it, or on the profile', () => {
    render(buildDto(comparableSet(3)), 2);
    const directions = (): number => component.exportableCards.filter(card => card.chrome.direction).length;
    expect(directions()).toBe(6);

    vi.useFakeTimers();
    try {
      component.onFigureStyleChange({
        ...DEFAULT_FIGURE_STYLE,
        bar: { ...DEFAULT_FIGURE_STYLE.bar, hiddenBadges: ['direction'] },
        scatter: { ...DEFAULT_FIGURE_STYLE.scatter, hiddenBadges: ['direction'] }
      });
      vi.advanceTimersByTime(150);
      fixture.detectChanges();
      expect(directions()).toBe(0);
      for (const card of [...component.panelCards, ...component.scatterCards]) {
        expect(card.chrome.direction, card.id).toBeUndefined();
        const label = (fixture.debugElement.query(By.css(`canvas[data-figure-id="${card.id}"]`))
          .nativeElement as HTMLCanvasElement).getAttribute('aria-label') ?? '';
        expect(label, card.id).not.toContain('Better toward');
      }
    }
    finally {
      vi.useRealTimers();
    }
  });

  it('draws no Better marker on any plot canvas', () => {
    render(buildDto(comparableSet(3)), 2);

    for (const card of component.exportableCards) {
      expect(card.plugins.map(plugin => plugin.id), card.id).not.toContain('overseerDirectionMarker');
      // The All tab paints a figure onto the tile canvas carrying this id, which no rebuild changes.
      expect(fixture.debugElement.queryAll(By.css(`canvas[data-figure-id="${card.id}"]`)).length, card.id).toBe(1);
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
    // One profile tile per model, each drawing its own line, the Ideal line and the other two.
    expect(figures.profile.tiles.length).toBe(3);
    expect(figures.profile.tiles.every(tile => tile.data.datasets.length === 4)).toBe(true);
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

  it('offers the total-run cost measure when every charted entry carries a total, and charts it', () => {
    render(buildDto(comparableSet(3)), 2);

    const option = fixture.debugElement.query(By.css('#mc-side-panel-data #mc-cost-measure-totalRun'))
      .nativeElement as HTMLInputElement;
    expect(option.type).toBe('radio');
    expect(option.disabled).toBe(false);
    expect(option.parentElement!.textContent).not.toContain('not available');
    expect(textOf('#mc-cost-measure-hint')).toContain('what one benchmark run of this model costs');

    chooseRadio('mc-cost-measure-totalRun');

    expect(component.costMeasure).toBe('totalRun');
    expect(option.checked).toBe(true);
  });

  it('disables the total-run cost measure with the reason in the hint when one entry has no total', () => {
    render(buildDto(setWithoutTotal(3, PRE_HARNESS_15)), 2);

    const option = fixture.debugElement.query(By.css('#mc-side-panel-data #mc-cost-measure-totalRun'))
      .nativeElement as HTMLInputElement;
    expect(option.disabled).toBe(true);
    expect(option.parentElement!.textContent).toContain('not available');
    expect(component.totalRunCostAvailable).toBe(false);
    expect(textOf('#mc-cost-measure-hint')).toContain(`Model 2: ${PRE_HARNESS_15}`);
  });

  it('falls back to candidate cost when a new comparison cannot supply the run total', () => {
    render(buildDto(comparableSet(3)), 2);
    chooseRadio('mc-cost-measure-totalRun');
    expect(component.costMeasure).toBe('totalRun');

    fixture.componentRef.setInput('comparison', buildDto(setWithoutTotal(3, PRE_HARNESS_15)));
    fixture.detectChanges();

    expect(component.costMeasure).toBe('candidateSuite');
    expect((fixture.debugElement.query(By.css('#mc-cost-measure-candidateSuite')).nativeElement as HTMLInputElement).checked).toBe(true);
  });

  // -------------------------------------------------------------------------------------------
  // The Data tab: models, measures, prices, model order
  // -------------------------------------------------------------------------------------------

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
    expect((panel.querySelector('#mc-speed-measure-meanModelTime') as HTMLInputElement).checked).toBe(true);
    expect((panel.querySelector('#mc-sort-key-intelligenceIndex') as HTMLInputElement).checked).toBe(true);
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
    expect([showBox('run:1'), showBox('run:2')].every(box => box.checked && !box.disabled)).toBe(true);
    expect(showBox('run:9').disabled).toBe(true);
    expect(showBox('run:9').checked).toBe(false);

    // The accessible name contains the visible label, so the two never contradict each other.
    const name = component.modelRows.find(row => row.key === 'run:1')!.name;
    expect(showBox('run:1').getAttribute('aria-label')).toBe(`Show ${name} in the charts`);
    expect(textOf('.mc-models-table')).toContain('Not comparable');
    const tip = fixture.debugElement.query(By.css(`#${component.tipId('excl', 'run:9')}`));
    expect(tip?.nativeElement.textContent).toContain('ScoringMethodVersion');

    clickShow('run:2');

    expect(component.includedKeys).toEqual(['run:1']);
    expect(showBox('run:2').checked).toBe(false);
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
    expect(component.isIncluded('run:1')).toBe(false);
    expect(fixture.debugElement.queryAll(By.css('.mc-models-tag')).length).toBe(0);

    // The seeded state has to stay reachable, so re-ticking at the cap is honoured.
    clickShow('run:1');
    expect(component.includedKeys.length).toBe(MAX_PLOTTED_ENTRIES + 1);
    expect(component.isIncluded('run:1')).toBe(true);
  });

  // -------------------------------------------------------------------------------------------
  // Uncertainty
  // -------------------------------------------------------------------------------------------

  it('marks a set in which every plotted entry rests on a single run', () => {
    const entries = comparableSet(3).map(entry => ({ ...entry, runCount: 1 }));
    render(buildDto(entries));

    expect(component.allSingleRun).toBe(true);
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
    expect(detail.open).toBe(false);
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
    const showModal = vi.spyOn(dialog, 'showModal');
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
    expect(box.disabled).toBe(true);

    component.toggleEmphasis('run:1');
    expect(component.emphasisKeys).toEqual([]);
  });

  it('turns the chart views off when unticked down to one model, and back on when re-ticked', () => {
    render(buildDto(comparableSet(3)), 2);
    expect(component.showFigures).toBe(true);

    component.toggleEntry('run:2');
    component.toggleEntry('run:3');
    fixture.detectChanges();

    expect(component.showFigures).toBe(false);
    expect(textOf('#mc-fig-unavailable')).toContain('Charts need at least two models. Check more under Data → Models.');
    expect(component.effectiveFigureTab).toBe('table');
    expect(fixture.debugElement.query(By.css('.mc-models-table'))).toBeTruthy();

    component.toggleEntry('run:2');
    fixture.detectChanges();

    expect(component.showFigures).toBe(true);
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

    expect(component.setFigureNotes.some(note => /Strict comparability/.test(note.text))).toBe(false);
    expect(component.setNotices.some(notice => /Strict comparability/.test(notice))).toBe(false);
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
      expect(actions[0].hasAttribute('tabindex')).toBe(false);
      expect(actions[1].hasAttribute('tabindex')).toBe(false);
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
    const copy = vi.spyOn(component, 'copyFigure').mockResolvedValue();
    const download = vi.spyOn(component, 'downloadFigure').mockResolvedValue();
    const open = vi.spyOn(component, 'openInSingle');

    const tile = fixture.debugElement.queryAll(By.css('.mc-all-tile'))[1];
    const cardId = (tile.nativeElement as HTMLElement).getAttribute('data-figure-id');
    const copyButton = tile.query(By.css('.mc-all-copy')).nativeElement as HTMLButtonElement;
    const downloadButton = tile.query(By.css('.mc-all-download')).nativeElement as HTMLButtonElement;

    copyButton.click();
    downloadButton.click();
    expect(copy).toHaveBeenCalledTimes(1);
    expect(vi.mocked(copy).mock.lastCall![0].id).toBe(cardId!);
    expect(download).toHaveBeenCalledTimes(1);
    expect(vi.mocked(download).mock.lastCall![0].id).toBe(cardId!);

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
    expect(component.figureTab, 'no Single view is needed to reach the export settings').toBe('all');

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
    await Promise.resolve();
    fixture.detectChanges();

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
        expect(tab.getAttribute('aria-disabled'), view).toBe('true');
        expect(tab.hasAttribute('disabled'), view).toBe(false);
        expect(tab.getAttribute('aria-describedby'), view).toBe('mc-fig-unavailable');
      }
      // The table views keep working.
      expect(fixture.debugElement.query(By.css('#mc-fig-tab-table')).nativeElement.getAttribute('aria-disabled')).toBeNull();
    };

    render(buildDto(comparableSet(1)), 2);
    expect(component.shape).toBe('single');
    expect(component.step).toBe(2);
    expectTableOnly();
    // A single model has no peer to be compared with, so no comparison report can be written.
    expect(component.isStepReachable(3)).toBe(false);
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
    expect(component.isStepReachable(2)).toBe(false);
    for (const selector of ['.mc-fig-workspace', '#mc-fig-sidebar', '.mc-fig-bar', '.mc-all-tile']) {
      expect(fixture.debugElement.query(By.css(selector)), selector).toBeNull();
    }
    expect(component.canExport).toBe(false);
  });

  it('opens the table when a refetch leaves a chart view over an unchartable set, and returns to it after', () => {
    render(buildDto(comparableSet(3)), 2);
    expect(component.effectiveFigureTab).toBe('all');

    fixture.componentRef.setInput('comparison', buildDto(comparableSet(1)));
    fixture.detectChanges();

    expect(component.step).toBe(2);
    expect(component.figureTab, 'the chosen view is kept').toBe('all');
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
    expect(excludedBox.disabled).toBe(true);
    // The state is in words as well as in the box, and never in the gold alone.
    expect(textOf('.mc-models-status')).toContain('3 of 3 shown');
    expect(textOf('.mc-models-status')).toContain('0 highlighted');
    expect(clearHighlightsButton().disabled).toBe(true);

    fixture.debugElement.query(By.css(`#mc-emph-${component.domKey('run:1')}`))
      .triggerEventHandler('change', { target: {} });
    fixture.detectChanges();

    expect(component.emphasisKeys.length).toBe(1);
    expect(textOf('.mc-models-status')).toContain('1 highlighted');
    expect(clearHighlightsButton().disabled).toBe(false);

    clearHighlightsButton().click();
    fixture.detectChanges();

    expect(component.emphasisKeys).toEqual([]);
    expect(textOf('.mc-models-status')).toContain('0 highlighted');
    expect(clearHighlightsButton().disabled).toBe(true);
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
    expect(component.canGoNext).toBe(false);
    expect(textOf('.mc-wizard-blocked')).toContain('Select at least one run, analysis group or battery result.');

    fixture.componentRef.setInput('selectedRunCount', component.maxSources + 1);
    fixture.detectChanges();
    expect(component.canGoNext).toBe(false);
    expect(textOf('.mc-wizard-blocked')).toContain(`at most ${component.maxSources}`);
    expect(textOf('.mc-wizard-blocked')).toContain('slow query');

    fixture.componentRef.setInput('selectedRunCount', 2);
    fixture.detectChanges();
    expect(component.canGoNext).toBe(true);
    expect(component.nextBlockedReason).toBe('');
    expect(fixture.debugElement.query(By.css('.mc-wizard-blocked'))).toBeNull();
  });
});
