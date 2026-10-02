import type { Mock } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { BaseChartDirective } from 'ng2-charts';
import {
  ComparisonFigureCard,
  FIGURE_SIDEBAR_STORAGE_KEY,
  FIGURE_STYLE_STORAGE_KEY,
  ModelComparisonComponent
} from './model-comparison.component';
import {
  FRONTIER_UNCERTAINTY_NOTE,
  MEAN_TIME_NO_INTERVAL_NOTE,
  P1_STACK_BREAKPOINT_PX,
  errorBarPlugin
} from './model-comparison-charts';
import { DEFAULT_FIGURE_STYLE, HIDDEN_INTERVALS_NOTE } from './figure-style';
import type { FigureStyle } from './figure-style';
import type { FigureChrome, FigureFooter } from './figure-chrome';
import { FIGURE_EXPORT_MAX_DENSITY_PERCENT, FIGURE_EXPORT_MAX_DIMENSION } from './figure-export';
import { FIGURE_SIZE_STORAGE_KEY, defaultFigureSize } from './figure-size';
import { previewZoomRange, zoomToSlider } from './preview-view';
import {
  buildExcludedEntry, buildDto, render, showView, refresh, comparableSet, textOf, setUpModelComparisonSpec,
  openSidebarTab, scatterLegendDisplays, scatterBlocks, openStyleFamily, singleTabButton, openSingle, composePreview,
  withStyleDebounce, expectTabContract, zoomButton, captureSaves, withClipboard, settleAllTab
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
  // The chart size in the sidebar's Download tab, and the Single tab that shows its effect
  // -------------------------------------------------------------------------------------------

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
    expect(component.canExport).toBe(false);
    expect(chartSizeText()).toContain(component.customResolutionError);
    // The All tab has no tile to size under it, and says why instead.
    expect(fixture.debugElement.query(By.css('.mc-all-tile'))).toBeNull();
    expect(textOf('.mc-all-viewport .mc-export-error')).toContain('height');

    component.onCustomHeightChange(1080);
    refresh();
    expect(component.customResolutionError).toBe('');
    expect(component.canExport).toBe(true);
  });

  it('opens the density on the reader’s own display, and says which option that is', () => {
    withDisplayDensity(2);
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('download');

    expect(component.figureSize.densitySelection).toBe(2);
    expect(component.exportDensity).toBe(2);
    expect(component.isCustomDensity).toBe(false);
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
    expect(styleControl('mc-export-text-scale').disabled).toBe(false);
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
    expect(component.canExport).toBe(false);
    expect(chartSizeText()).toContain('800');

    component.onCustomDensityChange(150);
    refresh();
    expect(component.customDensityError).toBe('');
    expect(component.exportDensity).toBe(1.5);
    expect(component.canExport).toBe(true);
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
    expect(download.hasAttribute('title')).toBe(false);
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
    expect(component.canExport).toBe(false);
    // Copy and Download: aria-disabled, so each stays focusable and its reason reachable.
    const controls = fixture.debugElement.queryAll(By.css('.mc-preview-export button'))
      .map(button => button.nativeElement as HTMLButtonElement);
    expect(controls.length).toBe(2);
    expect(controls.every(button => button.getAttribute('aria-disabled') === 'true')).toBe(true);
    expect(controls.every(button => !button.disabled)).toBe(true);
    expect(component.downloadAllTooltip).toBe(component.exportSizeError);
    expect(component.downloadFigureTooltip).toBe(component.exportSizeError);

    // The All view's Download all charts and each tile's Copy and Download refuse the same way.
    showView('all');
    const allControls = fixture.debugElement
      .queryAll(By.css('.mc-all-download-all, .mc-all-tile .mc-all-copy, .mc-all-tile .mc-all-download'))
      .map(button => button.nativeElement as HTMLButtonElement);
    // The size error replaces the tiles, so only Download all charts is left to refuse.
    expect(allControls.length).toBeGreaterThanOrEqual(1);
    expect(allControls.every(button => button.getAttribute('aria-disabled') === 'true')).toBe(true);
    expect(allControls.every(button => !button.disabled)).toBe(true);

    component.onExportDensityChange(2);
    refresh();
    expect(component.exportSizeError).toBe('');
    expect(component.canExport).toBe(true);
    const enabled = fixture.debugElement
      .queryAll(By.css('.mc-all-download-all, .mc-all-tile .mc-all-copy, .mc-all-tile .mc-all-download'))
      .map(button => button.nativeElement as HTMLButtonElement);
    expect(enabled.length).toBe(1 + 7 * 2);
    expect(enabled.every(button => button.getAttribute('aria-disabled') === null)).toBe(true);
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
    expect(component.previewActive).toBe(true);
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
    expect(element, id).not.toBeNull();
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

  it('drives the page from both trade-off toggles in the Charts tab', () => {
    render(buildDto(comparableSet(3)), 2);
    openStyleTab(component.scatterCards[0]);

    const named = styleControl('mc-style-scatter-directLabels');
    const valued = styleControl('mc-style-scatter-inlineValues');
    expect(named.checked).toBe(component.scatterDirectLabels);
    expect(valued.checked).toBe(component.scatterInlineValues);

    setChecked(named, false);
    expect(component.scatterDirectLabels).toBe(false);
    expect(styleControl('mc-style-scatter-directLabels').checked).toBe(false);
    expect(scatterLegendDisplays()).toEqual([true, true, true]);

    setChecked(styleControl('mc-style-scatter-inlineValues'), true);
    expect(component.scatterInlineValues).toBe(true);
    expect(styleControl('mc-style-scatter-inlineValues').checked).toBe(true);
    expect(scatterBlocks()!.every(b => b.values.length === 2)).toBe(true);
  });

  it('fills single-run bars from the Charts tab, persists it, and keeps no second copy of the control', () => {
    render(buildDto(comparableSet(3).map(entry => ({ ...entry, runCount: 1 }))), 2);

    const fills = (): unknown[] => (component.panelCards[0].data.datasets[0] as unknown as Record<string, unknown[]>)['backgroundColor'];
    expect(fills().every(fill => fill === 'transparent')).toBe(true);
    expect(fixture.debugElement.query(By.css('#mc-bar-filled'))).toBeNull();

    openStyleFamily('bar');
    const styleToggle = (): HTMLInputElement => styleControl('mc-style-bar-filledBars');
    expect(styleToggle().checked).toBe(false);

    withStyleDebounce(() => setChecked(styleToggle(), true));
    expect(component.figureStyle.bar.filledBars).toBe(true);
    expect(JSON.parse(localStorage.getItem(FIGURE_STYLE_STORAGE_KEY)!).bar.filledBars).toBe(true);
    expect(fills().some(fill => fill === 'transparent')).toBe(false);

    withStyleDebounce(() => setChecked(styleToggle(), false));
    expect(styleToggle().checked).toBe(false);
    expect(fills().every(fill => fill === 'transparent')).toBe(true);
  });

  it('re-composes the preview when a trade-off toggle is changed in the Charts tab', () => {
    render(buildDto(comparableSet(3)), 2);

    // The clock is installed before the dialog is opened, so the composition the open itself
    // schedules is a fake timer this test drains rather than a real one outliving it.
    vi.useFakeTimers();
    try {
      openStyleTab(component.scatterCards[0]);
      const renderPreview = vi.spyOn(component as unknown as {
        renderPreview(): Promise<void>;
      }, 'renderPreview').mockResolvedValue();
      vi.advanceTimersByTime(200);
      renderPreview.mockClear();

      setChecked(styleControl('mc-style-scatter-inlineValues'), true);
      expect(renderPreview).not.toHaveBeenCalled();
      vi.advanceTimersByTime(200);
      expect(renderPreview).toHaveBeenCalled();
    }
    finally {
      vi.useRealTimers();
    }
  });

  it('offers the chart views\' four sidebar sections as tabs with the full tab contract', () => {
    render(buildDto(comparableSet(3)), 2);

    expectTabContract('.mc-fig-sidebar-tabs', 'Settings sections', 'mc-side-tab-', 'mc-side-panel-',
      ['Data', 'Theme', 'Charts', 'Download'], () => component.sidebarTab);
    // Only the selected section's panel is rendered.
    expect(fixture.debugElement.query(By.css('#mc-side-panel-download'))).not.toBeNull();
    for (const tab of ['data', 'theme', 'charts', 'table']) {
      expect(fixture.debugElement.query(By.css(`#mc-side-panel-${tab}`)), tab).toBeNull();
    }
  });

  it('offers the four views as tabs with icons and the full tab contract', () => {
    render(buildDto(comparableSet(3)), 2);

    // Each visible label is the whole accessible name, so no aria-label repeats it.
    const tabs = fixture.debugElement.queryAll(By.css('.mc-fig-tabs [role="tab"]'))
      .map(tab => tab.nativeElement as HTMLElement);
    expect(tabs.map(tab => tab.getAttribute('aria-label'))).toEqual([null, null, null, null]);

    expectTabContract('.mc-fig-tabs', 'Comparison views', 'mc-fig-tab-', 'mc-fig-panel-', ['All charts', 'Single chart', 'Interactive table', 'Table preview'], () => component.figureTab, ['all', 'single', 'table', 'tablePreview']);
    expect(component.previewActive).toBe(true);
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
    expect(has('#mc-style-bar-heading')).toBe(true);
    expect(has('#mc-style-scatter-heading')).toBe(false);
    expect(textOf('#mc-side-panel-charts')).not.toContain('Every change here applies');

    // Switching figure keeps the tab, and the set follows the figure's kind.
    component.selectPreviewCard(component.scatterCards[0].id);
    refresh();
    expect(component.sidebarTab).toBe('charts');
    expect(component.styleFamily).toBe('scatter');
    expect(has('#mc-style-scatter-heading')).toBe(true);
    expect(has('#mc-style-bar-heading')).toBe(false);

    component.selectPreviewCard(component.profileCard!.id);
    refresh();
    expect(has('#mc-style-bar-heading')).toBe(false);
    expect(has('#mc-style-scatter-heading')).toBe(false);
    expect(has('#mc-style-profile-heading')).toBe(true);
  });

  it('stores a style change at once, persists it, and rebuilds the figures after the debounce', () => {
    render(buildDto(comparableSet(3)), 2);

    vi.useFakeTimers();
    try {
      openStyleTab(component.panelCards[0]);
      const renderPreview = vi.spyOn(component as unknown as {
        renderPreview(): Promise<void>;
      }, 'renderPreview').mockResolvedValue();
      vi.advanceTimersByTime(200);
      renderPreview.mockClear();

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

      vi.advanceTimersByTime(150);
      expect(barPercentage()).toBeCloseTo(0.9, 9);
      expect(renderPreview).not.toHaveBeenCalled();
      vi.advanceTimersByTime(150);
      expect(renderPreview).toHaveBeenCalled();
    }
    finally {
      vi.useRealTimers();
    }
  });

  it('drops a hidden badge from the bar cards after the debounce, and persists it', () => {
    render(buildDto(comparableSet(3)), 2);

    vi.useFakeTimers();
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

      vi.advanceTimersByTime(150);
      expect(kinds()).toEqual(before.filter(kind => kind !== 'questions'));
      expect(component.scatterCards[0].chrome.badges.some(badge => badge.kind === 'questions')).toBe(true);
    }
    finally {
      vi.useRealTimers();
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
      expect(drawnChrome(card).footer.suite, card.id).toBe('GnollHack Player Assistance Benchmark Suite');
      expect(drawnChrome(card).footer.computedAt, card.id).not.toBe('');
    }

    vi.useFakeTimers();
    try {
      changeFamilyStyle('profile', { footer: false });
      expect(drawnChrome(component.profileCard!).footer).toEqual({ suite: '', computedAt: '' });
      for (const card of component.panelCards) {
        expect(drawnChrome(card).footer.suite, card.id).not.toBe('');
      }
    }
    finally {
      vi.useRealTimers();
    }
  });

  it('draws each family at its own caption sizes, and re-composes the All tiles once a style change pauses', async () => {
    render(buildDto(comparableSet(3)), 2);
    await settleAllTab();
    expect(component.allActive).toBe(true);
    const schedule = vi.spyOn(component as unknown as {
      scheduleAllCompose(): void;
    }, 'scheduleAllCompose');

    vi.useFakeTimers();
    try {
      changeFamilyStyle('bar', { titleSizePx: 30, badgeTextSizePx: 14, footerTextSizePx: 16 });
      // Not yet: a drag re-composes once it pauses.
      expect(schedule).not.toHaveBeenCalled();
      vi.advanceTimersByTime(150);
      expect(schedule).toHaveBeenCalled();

      for (const card of component.panelCards) {
        expect(drawnChrome(card).textSizes, card.id).toEqual({ titlePx: 30, badgePx: 14, footerPx: 16 });
      }
      expect(drawnChrome(component.scatterCards[0]).textSizes).toEqual({ titlePx: 18, badgePx: 11, footerPx: 12 });
    }
    finally {
      vi.useRealTimers();
    }
  });

  it('adds the hidden-intervals note to the Intelligence card when its bars are hidden, and drops it on request', () => {
    render(buildDto(comparableSet(3)), 2);
    openStyleTab(component.panelCards[0]);

    const noteToggle = styleControl('mc-style-bar-hiddenIntervalsNote');
    expect(noteToggle.disabled).toBe(true);
    expect(drawnNotes('panels', 0)).not.toContain(HIDDEN_INTERVALS_NOTE);

    withStyleDebounce(() => setChecked(styleControl('mc-style-bar-intervals'), false));
    expect(drawnNotes('panels', 0)).toContain(HIDDEN_INTERVALS_NOTE);
    // The Speed panel on mean time draws no whisker anyway, so it gains nothing.
    expect(drawnNotes('panels', 1)).not.toContain(HIDDEN_INTERVALS_NOTE);
    expect(component.panelCards[0].plugins).not.toContain(errorBarPlugin);

    expect(styleControl('mc-style-bar-hiddenIntervalsNote').disabled).toBe(false);
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

  it('drops the frontier line and its key item from the scatter cards on request, keeping the highlight', () => {
    render(buildDto(comparableSet(3)), 2);
    const hasLine = (card: ComparisonFigureCard): boolean =>
      card.data.datasets.some(dataset => dataset.label === 'Pareto frontier');
    const index = component.scatterCards.findIndex(hasLine);
    expect(index, 'a scatter with a drawn frontier').toBeGreaterThanOrEqual(0);
    const card = (): ComparisonFigureCard => component.scatterCards[index];
    const highlight = card().chrome.highlight;
    expect(card().chrome.key.map(item => item.glyph)).toContain('frontier');

    openStyleTab(card());
    withStyleDebounce(() => setChecked(styleControl('mc-style-scatter-frontierLine'), false));

    expect(component.scatterCards.some(hasLine)).toBe(false);
    expect(card().chrome.key.map(item => item.glyph)).not.toContain('frontier');
    expect(card().chrome.highlight).toBe(highlight);
  });

  it('reads the badge off the exam the runs were asked, with no left-out note for a revised rubric', () => {
    // A fully scored 18-question exam, whatever the suite holds now.
    render(buildDto(comparableSet(2)), 2);

    expect(component.setFigureNotes.some(note => note.tone === 'info' && /left out/.test(note.text))).toBe(false);
    expect(component.setFigureNotes.some(note => /revised/.test(note.text))).toBe(false);

    const s1 = component.scatterCards[0];
    expect(s1.chrome.badges.find(badge => badge.kind === 'questions')?.text).toBe('18 questions');
    expect(exportNotesOf(s1).some(text => /revised/.test(text))).toBe(false);
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
    expect(component.setFigureNotes).toContainEqual({ text: warning, tone: 'warning' });

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
    expect(toggle.disabled).toBe(false);
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

  it('refreshes the profile descriptions and the figures after a number change, once the style debounce passes', () => {
    render(buildDto(comparableSet(3)), 2);
    const minLabel = (): string => component.profileAxes!.axes[0].minLabel;
    expect(minLabel()).toMatch(/^\d+$/);

    vi.useFakeTimers();
    try {
      component.onFigureStyleChange({
        ...component.figureStyle,
        numbers: { ...component.figureStyle.numbers, intelligenceIndex: 2 }
      });
      expect(JSON.parse(localStorage.getItem(FIGURE_STYLE_STORAGE_KEY)!).numbers.intelligenceIndex).toBe(2);
      expect(minLabel()).toMatch(/^\d+$/);

      vi.advanceTimersByTime(150);
      refresh();
      expect(minLabel()).toMatch(/^\d+\.\d{2}$/);
      // One entry per model: its values, in the new decimals, and its weakest axis.
      expect(textOf('.mc-axis-ends')).toContain(`intelligence ${minLabel()}`);
      expect(fixture.debugElement.queryAll(By.css('.mc-axis-ends li')).length).toBe(3);
      expect(textOf('.mc-axis-ends')).toContain('weakest axis:');
    }
    finally {
      vi.useRealTimers();
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
    expect(styleControl('mc-export-text-scale').disabled).toBe(false);
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
    vi.spyOn(component as unknown as {
      exportChrome(card: ComparisonFigureCard): unknown;
    }, 'exportChrome').mockReturnValue({
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
    expect(component.previewBusy).toBe(false);
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
    vi.spyOn(component, 'measureStage').mockReturnValue({ width: 800, height: 600, devicePixelRatio: 2 });
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
    vi.spyOn(component, 'measureStage').mockReturnValue({ width: 800, height: 600, devicePixelRatio: 2 });
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
  // Fit to screen against the real stylesheet: measureStage is not stubbed here, so the stage's
  // border and the viewport's padding are what the fit has to leave room for.
  // -------------------------------------------------------------------------------------------

  /**
   * Gives the stage a laid-out size of its own, fractional as a flex layout often is, and composes
   * at Fit to screen. The fixture's own layout would otherwise leave the stage at no size.
   */
  async function composeLaidOutFit(): Promise<HTMLElement> {
    const stage = fixture.debugElement.query(By.css('.mc-preview-stage')).nativeElement as HTMLElement;
    stage.style.flex = 'none';
    stage.style.width = '802.6px';
    stage.style.height = '402.4px';
    component.fitPreviewToScreen();
    await composePreview();
    return previewViewport();
  }

  function expectNoOverflow(viewport: HTMLElement): void {
    expect(viewport.scrollHeight, 'vertical overflow').toBeLessThanOrEqual(viewport.clientHeight);
    expect(viewport.scrollWidth, 'horizontal overflow').toBeLessThanOrEqual(viewport.clientWidth);
    expect(viewport.classList.contains('is-pannable'), 'pannable at Fit to screen').toBe(false);
  }

  it('fits a tall chart to the stage’s height at Fit to screen without a scrollbar', async () => {
    render(buildDto(comparableSet(3)), 2);
    openSingle();
    component.onExportDensityChange(1);
    component.onExportResolutionChange('a4p');

    const viewport = await composeLaidOutFit();

    expect(stageCanvas().width).toBeGreaterThan(0);
    expectNoOverflow(viewport);
    // Filled, not merely shrunk: the canvas takes the viewport's height less its padding, to a pixel.
    const padding = parseFloat(getComputedStyle(viewport).paddingTop) + parseFloat(getComputedStyle(viewport).paddingBottom);
    expect(Math.abs(stageCanvas().getBoundingClientRect().height - (viewport.clientHeight - padding))).toBeLessThanOrEqual(1);
  });

  it('fits a wide chart to the stage’s width at Fit to screen without a scrollbar', async () => {
    render(buildDto(comparableSet(3)), 2);
    openSingle();
    component.onExportDensityChange(1);
    component.onExportResolutionChange('custom');
    component.onCustomWidthChange(3000);
    component.onCustomHeightChange(1000);

    const viewport = await composeLaidOutFit();

    expect(stageCanvas().width).toBeGreaterThan(0);
    expectNoOverflow(viewport);
  });

  it('fits the table image at Fit to screen without a scrollbar', async () => {
    render(buildDto(comparableSet(3)), 2);
    showView('tablePreview');

    const viewport = await composeLaidOutFit();

    expect(stageCanvas().width).toBeGreaterThan(0);
    expectNoOverflow(viewport);
  });

  // -------------------------------------------------------------------------------------------
  // Preview zoom and pan
  // -------------------------------------------------------------------------------------------

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
    vi.spyOn(component, 'measureStage').mockReturnValue({ width: 800, height: 600, devicePixelRatio: 2 });
    openSingle();
    component.onExportDensityChange(1);
    component.onExportResolutionChange('fullhd');
    await composePreview();
  }

  /** A custom 800 × 600 at 100 % density on the same stage: the screen fit is 2. */
  async function openCustomPreview(): Promise<void> {
    render(buildDto(comparableSet(3)), 2);
    vi.spyOn(component, 'measureStage').mockReturnValue({ width: 800, height: 600, devicePixelRatio: 2 });
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

    const schedule = vi.spyOn(component as unknown as {
      schedulePreview(): void;
    }, 'schedulePreview');
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

    expect(press('+').defaultPrevented).toBe(true);
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
    expect(withCtrl.defaultPrevented).toBe(false);
    expect(component.previewZoomValue).toBe(1);
    const arrow = press('ArrowDown');
    expect(arrow.defaultPrevented).toBe(false);
  });

  it('marks zoom in unavailable at 800 %, and refuses it there', async () => {
    await openFullHdPreview();
    component.setPreviewView(8);
    refresh();

    const zoomIn = zoomButton('Zoom the preview in');
    expect(zoomIn.getAttribute('aria-disabled')).toBe('true');
    expect(zoomIn.disabled).toBe(false);
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
    watching: RecordingResizeObserver[];
    viewport: HTMLElement;
    removed: Mock;
  } {
    const stage = fixture.debugElement.query(By.css('.mc-preview-stage')).nativeElement as HTMLElement;
    const viewport = previewViewport();
    return {
      watching: observers.filter(observer => observer.observed.includes(stage)),
      viewport,
      removed: vi.spyOn(viewport, 'removeEventListener')
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
    expect(component.previewActive).toBe(false);
    expect(removed).toHaveBeenCalledWith('wheel', expect.any(Function), expect.anything());
    expect(fixture.debugElement.query(By.css('#mc-fig-panel-single'))).toBeNull();

    const viewport = fixture.debugElement.query(By.css('.mc-all-viewport')).nativeElement as HTMLElement;
    const watchingAll = observers.filter(observer => observer.observed.includes(viewport));
    expect(watchingAll.length).toBe(1);
    expect(component.allActive).toBe(true);

    // Back to Single: the All viewport's observer goes with it.
    singleTabButton().click();
    refresh();
    expect(watchingAll[0].disconnected).toBe(1);
    expect(component.allActive).toBe(false);
  });

  it('stops watching the stage on leaving step 2, and on destroy', async () => {
    render(buildDto(comparableSet(3)), 2);
    const observers = installFakeResizeObserver();
    openSingle();
    const first = watchedStage(observers);

    component.goToStep(1);
    fixture.detectChanges();
    expect(first.watching[0].disconnected).toBe(1);
    expect(first.removed).toHaveBeenCalledWith('pointerdown', expect.any(Function), undefined);
    expect(component.figureTab, 'kept, so returning shows the Single tab again').toBe('single');

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

    expect(viewport.isConnected).toBe(false);
    expect(component.previewActive).toBe(false);
    expect(watching[0].disconnected).toBe(1);
    expect(removed).toHaveBeenCalledWith('wheel', expect.any(Function), expect.anything());
    expect(removed).toHaveBeenCalledWith('pointerdown', expect.any(Function), undefined);
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
    expect(names.every(name => name.startsWith('Open ') && name.endsWith(' in Single view'))).toBe(true);
    expect(new Set(names).size).toBe(7);
    expect(opens().every(button => button.querySelector('path')?.getAttribute('d')?.startsWith('M1 12s4-8')), 'the eye glyph').toBe(true);
    expect(opens().every(button => !button.disabled && button.getAttribute('aria-disabled') === null)).toBe(true);
    expect(opens().every(button => button.textContent?.trim() === '')).toBe(true);

    // The Single tab is where a size error is shown and fixed, so it stays reachable under one,
    // though the All tab has no tile to size.
    component.onExportResolutionChange('custom');
    component.onCustomWidthChange(10);
    refresh();
    expect(component.canExport).toBe(false);
    expect(opens().length).toBe(0);
    const single = singleTabButton();
    expect(single.disabled).toBe(false);
    expect(single.getAttribute('aria-disabled')).toBeNull();
  });
});
