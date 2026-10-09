import type { Mock } from "vitest";
import { DebugElement } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { BaseChartDirective } from 'ng2-charts';
import {
  ComparisonFigureCard,
  FIGURE_SIDEBAR_STORAGE_KEY,
  SIDEBAR_WIDTH_DEFAULT,
  SIDEBAR_WIDTH_MAX,
  SIDEBAR_WIDTH_MIN,
  ModelComparisonComponent
} from './model-comparison.component';
import { DEFAULT_FIGURE_STYLE } from './figure-style';
import { formatComputedAt } from './figure-chrome';
import type { FigureChrome, FigureFooter } from './figure-chrome';
import { toChartContext } from './model-comparison.models';
import { DEFAULT_TABLE_COLUMNS } from './table-export';
import { FIGURE_SIZE_STORAGE_KEY, defaultFigureSize } from './figure-size';
import { figureLogoIo } from './figure-logo';
import { fitHeightZoom } from './preview-view';
import { ToastComponent } from '../../../shared/toast/toast.component';
import {
  buildDto, render, showView, renderTable, refresh, comparableSet, textOf, setUpModelComparisonSpec, openSidebarTab,
  openStyleFamily, singleTabButton, openSingle, withStyleDebounce, zoomButton, captureSaves, withClipboard,
  stubZipWriter, settleAllTab
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
    expect(zip.names[0].every(name => /^model-comparison_.+\.(png|webp)$/.test(name))).toBe(true);
    expect(zip.names[0].every(name => name.includes(stamp))).toBe(true);
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
      timeline: DEFAULT_FIGURE_STYLE.timeline,
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

  describe('the GnollBench logo', () => {
    type LogoAccess = {
      exportChrome(card: ComparisonFigureCard): { logo?: { image: CanvasImageSource; aspectRatio: number; heightPx: number } | null };
      tableImageOptions(): { logo?: { image: CanvasImageSource; aspectRatio: number; heightPx: number } | null };
      prepareFigureComposition(): Promise<void>;
    };
    const access = (): LogoAccess => component as unknown as LogoAccess;

    let logoImage: HTMLCanvasElement;

    beforeEach(() => {
      logoImage = document.createElement('canvas');
      logoImage.width = 8;
      logoImage.height = 8;
      (figureLogoIo.loadImage as Mock).mockResolvedValue(logoImage);
    });

    it('draws the wide logo at 48 px into every chart and the table image by default', async () => {
      render(buildDto(comparableSet(3)), 2);
      await access().prepareFigureComposition();

      const chart = access().exportChrome(component.panelCards[0]).logo!;
      expect(chart.image).toBe(logoImage);
      expect(chart).toEqual(expect.objectContaining({ heightPx: 48, aspectRatio: 3248 / 850 }));
      expect(access().tableImageOptions().logo).toEqual(expect.objectContaining({ heightPx: 48, aspectRatio: 3248 / 850 }));
      expect(figureLogoIo.loadImage).toHaveBeenCalledWith('/img/gnollbench/gnollbench-wide-v3-h850.webp');
    });

    it('draws the square emblem at its own proportions and the chosen height', async () => {
      render(buildDto(comparableSet(3)), 2);
      withStyleDebounce(() => component.onFigureStyleChange({
        ...component.figureStyle,
        appearance: { ...component.figureStyle.appearance, logoVariant: 'square', logoHeightPx: 64 }
      }));
      await access().prepareFigureComposition();

      expect(access().exportChrome(component.panelCards[0]).logo)
        .toEqual(expect.objectContaining({ heightPx: 64, aspectRatio: 1 }));
      expect(access().tableImageOptions().logo).toEqual(expect.objectContaining({ heightPx: 64, aspectRatio: 1 }));
      expect(figureLogoIo.loadImage).toHaveBeenCalledWith('/img/gnollbench/gnollbench-logo-v3-843.webp');
    });

    it('draws no logo while it is hidden, and loads none', async () => {
      render(buildDto(comparableSet(3)), 2);
      withStyleDebounce(() => component.onFigureStyleChange({
        ...component.figureStyle,
        appearance: { ...component.figureStyle.appearance, logo: false }
      }));
      (figureLogoIo.loadImage as Mock).mockClear();
      await access().prepareFigureComposition();

      expect(access().exportChrome(component.panelCards[0]).logo).toBeNull();
      expect(access().tableImageOptions().logo).toBeNull();
      expect(figureLogoIo.loadImage).not.toHaveBeenCalled();
    });

    it('draws no logo where it failed to load', async () => {
      (figureLogoIo.loadImage as Mock).mockResolvedValue(null);
      render(buildDto(comparableSet(3)), 2);
      await access().prepareFigureComposition();

      expect(access().exportChrome(component.panelCards[0]).logo).toBeNull();
      expect(access().tableImageOptions().logo).toBeNull();
    });
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
    expect(component.exporting).toBe(false);
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
    vi.spyOn(component as unknown as {
      exportChrome(card: ComparisonFigureCard): unknown;
    }, 'exportChrome').mockReturnValue({
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
      expect(button.textContent!.trim(), name).toBe('');
      expect(button.classList).toContain('action-btn');
      const tipId = button.getAttribute('interestfor');
      expect(tipId, name).toBeTruthy();
      const tip = (fixture.nativeElement as HTMLElement).querySelector(`#${tipId}`);
      expect(tip?.getAttribute('popover'), name).toBe('hint');
      expect(button.getAttribute('style')).toContain(`anchor-name: --${tipId}`);
      expect(tip?.getAttribute('style')).toContain(`position-anchor: --${tipId}`);
    }

    // Every icon-only button in the workspace has a name and a tooltip, and none uses `title`.
    const step = fixture.debugElement.query(By.css('#mc-step-panel-2')).nativeElement as HTMLElement;
    for (const button of Array.from(step.querySelectorAll<HTMLButtonElement>('button.action-btn'))) {
      expect(button.getAttribute('aria-label'), button.outerHTML).toBeTruthy();
      expect(button.getAttribute('interestfor'), button.outerHTML).toBeTruthy();
      expect(button.getAttribute('type')).toBe('button');
    }
    expect(step.querySelectorAll('[title]').length).toBe(0);

    // Every control on the row is an icon button; the figure's Download is one too.
    expect(toolbar.querySelectorAll('.btn-gh').length).toBe(0);
    expect(toolbar.querySelector('.mc-preview-download')?.classList).toContain('action-btn');
  });

  it('keeps Previous, the figure picker and Next on one line down to the 44rem panel width', () => {
    render(buildDto(comparableSet(3)), 2);
    openSingle();

    const panel = fixture.debugElement.query(By.css('.mc-fig-panel')).nativeElement as HTMLElement;
    // 45rem of content: the container query measures the panel inside its padding.
    panel.style.boxSizing = 'content-box';
    panel.style.right = 'auto';
    panel.style.width = '45rem';
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const button = (name: string): HTMLElement => host.querySelector(`button[aria-label="${name}"]`) as HTMLElement;
    const middle = (element: Element): number => {
      const rect = element.getBoundingClientRect();
      return rect.top + rect.height / 2;
    };
    const previous = button('Previous figure');
    const next = button('Next figure');
    const figureGroup = host.querySelector('.mc-preview-figure-group') as HTMLElement;
    const zoom = host.querySelector('.mc-preview-zoom') as HTMLElement;

    expect(figureGroup).not.toBeNull();
    expect(getComputedStyle(figureGroup).flexWrap).toBe('nowrap');
    expect(getComputedStyle(figureGroup).flexShrink).toBe('0');
    expect(previous.offsetTop).toBe(next.offsetTop);
    expect(Math.abs(previous.getBoundingClientRect().top - next.getBoundingClientRect().top)).toBeLessThanOrEqual(1);
    // The zoom group shares their line: the only group on the row that shrinks.
    expect(Math.abs(middle(zoom) - middle(next))).toBeLessThanOrEqual(1);
    expect(getComputedStyle(zoom).flexShrink).toBe('1');
    // The read-out is as wide as its text, never reserving room for the longest one.
    const value = host.querySelector('.mc-preview-zoom-value') as HTMLElement;
    expect(getComputedStyle(value).whiteSpace).toBe('nowrap');
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
    expect(sidebar().hidden).toBe(false);
    expect(textOf('#mc-tip-sidebar')).toContain('Hide settings');

    toggle.click();
    fixture.detectChanges();

    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    // The name stays constant; the state is aria-expanded's.
    expect(toggle.getAttribute('aria-label')).toBe('Comparison settings');
    expect(sidebar().hidden).toBe(true);
    expect(getComputedStyle(sidebar()).display).toBe('none');
    expect(fixture.debugElement.query(By.css('.mc-fig-workspace.is-collapsed'))).not.toBeNull();
    expect(textOf('#mc-tip-sidebar')).toContain('Show settings');
    expect(storedSidebar()).toEqual({
      version: 1, collapsed: true, sidebarWidth: SIDEBAR_WIDTH_DEFAULT, tab: 'data', view: 'all',
      figureSizeOpen: true, tableImageSizeOpen: true, imageFormatOpen: true
    });

    // One collapsed state for every view: the table views have the same sidebar.
    showView('table');
    expect(sidebar().hidden).toBe(true);

    const second = secondInstance();
    expect(second.componentInstance.sidebarCollapsed).toBe(true);
    expect((second.nativeElement as HTMLElement).querySelector('#mc-fig-sidebar')?.hasAttribute('hidden')).toBe(true);
    second.destroy();
  });

  it('keeps the sidebar defaults when storage throws', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    const blocked = TestBed.createComponent(ModelComparisonComponent);
    expect(blocked.componentInstance.sidebarCollapsed).toBe(false);
    expect(blocked.componentInstance.sidebarTab).toBe('data');
    expect(blocked.componentInstance.tableColumns).toEqual(DEFAULT_TABLE_COLUMNS);
    expect(blocked.componentInstance.tableFormat).toBe('xlsx');
    expect(() => blocked.componentInstance.toggleSidebar()).not.toThrow();
    expect(() => blocked.componentInstance.selectSidebarTab('charts')).not.toThrow();
    expect(() => blocked.componentInstance.onTableFormatChange('md')).not.toThrow();
    expect(() => blocked.componentInstance.onTableColumnsChange(DEFAULT_TABLE_COLUMNS)).not.toThrow();
    expect(blocked.componentInstance.sidebarCollapsed).toBe(true);
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
    expect(second.componentInstance.sidebarCollapsed).toBe(false);
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
    expect(size.open, 'open by default').toBe(true);
    expect(size.querySelector('summary .gh-disclosure-summary-title')?.textContent?.trim()).toBe('Chart size');
    for (const id of ['mc-export-resolution', 'mc-export-density', 'mc-export-text-scale']) {
      expect(size.querySelector(`#${id}`), id).not.toBeNull();
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
    expect(downloadButtons.length, 'no download or copy buttons').toBe(0);
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
    expect(component.figureSizeOpen).toBe(false);
    expect(storedSidebar().figureSizeOpen).toBe(false);
    expect(storedSidebar().tab).toBe('download');

    const second = secondInstance();
    expect(second.componentInstance.figureSizeOpen).toBe(false);
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
      expect(Math.abs(button.top + button.height / 2 - centreOf(stepRow)), selector).toBeLessThanOrEqual(1);
    }
  });

  it('lines the sidebar tabs\' rule up with the view bar\'s', () => {
    // Wide enough that the container query keeps the sidebar beside the views.
    const hostElement = fixture.nativeElement as HTMLElement;
    hostElement.style.display = 'block';
    hostElement.style.width = '1200px';
    hostElement.style.height = '800px';
    render(buildDto(comparableSet(3)), 2);
    const element = (selector: string): HTMLElement =>
      fixture.debugElement.query(By.css(selector)).nativeElement as HTMLElement;

    const sidebarTabs = element('.mc-fig-sidebar-tabs');
    const bar = element('.mc-fig-bar');
    expect(Math.abs(sidebarTabs.getBoundingClientRect().bottom - bar.getBoundingClientRect().bottom)).toBeLessThanOrEqual(1);
    expect(getComputedStyle(sidebarTabs).paddingTop).toBe('6px');
    expect(getComputedStyle(element('.mc-fig-tabs')).paddingTop).toBe('6px');
  });

  it('rules the All charts toolbar off from the notes and tiles', () => {
    const hostElement = fixture.nativeElement as HTMLElement;
    hostElement.style.display = 'block';
    hostElement.style.width = '1200px';
    hostElement.style.height = '800px';
    render(buildDto(comparableSet(3)), 2);
    expect(component.figureTab).toBe('all');
    const toolbar = fixture.debugElement.query(By.css('#mc-fig-panel-all .mc-all-toolbar')).nativeElement as HTMLElement;
    expect(getComputedStyle(toolbar).borderBottomWidth).toBe('1px');
    expect(getComputedStyle(toolbar).borderBottomStyle).toBe('solid');
  });

  it('puts About and Recompute at the step row\'s end on step 2 only, and leaves the view bar the toggle and the tabs', () => {
    render(buildDto(comparableSet(3)), 2);
    for (const selector of ['#mc-about-trigger', '.mc-recompute']) {
      const button = fixture.debugElement.query(By.css(selector)).nativeElement as HTMLElement;
      expect(button.closest('.mc-wizard-tabbar .mc-wizard-meta'), selector).not.toBeNull();
      expect(button.closest('.mc-fig-bar'), selector).toBeNull();
      expect(button.closest('[role="tablist"]'), selector).toBeNull();
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
    expect(Math.abs(tableContentTop - barBottom() - 16), 'Interactive table').toBeLessThanOrEqual(1);

    showView('tablePreview');
    const previewTop = element('#mc-fig-panel-tablePreview .mc-preview-toolbar').getBoundingClientRect().top;
    expect(Math.abs(previewTop - barBottom() - 16), 'Table preview').toBeLessThanOrEqual(1);
  });

  it('renders no chart directive on step 2, and exports without reading a page canvas', async () => {
    render(buildDto(comparableSet(3)), 2);
    const directives = (): number => fixture.debugElement.queryAll(By.directive(BaseChartDirective)).length;
    expect(directives(), 'the All tab').toBe(0);
    expect(fixture.debugElement.queryAll(By.css('canvas[baseChart], canvas[basechart]')).length).toBe(0);

    openSingle(component.panelCards[0]);
    expect(directives(), 'the Single tab').toBe(0);
    // Nothing is kept rendered and hidden behind the Single tab any more.
    expect(fixture.debugElement.query(By.css('#mc-fig-panel-all'))).toBeNull();
    expect(fixture.debugElement.query(By.css('.mc-fig-panel[inert], .mc-fig-panel.is-inactive'))).toBeNull();

    captureSaves();
    const exportOne = vi.spyOn(component as unknown as {
      exportOneFigure(card: ComparisonFigureCard, ...rest: unknown[]): Promise<unknown>;
    }, 'exportOneFigure').mockResolvedValue({ result: null, refusal: null, pixels: '' });
    await component.downloadPreviewedFigure();

    expect(exportOne).toHaveBeenCalledTimes(1);
    const args = vi.mocked(exportOne).mock.lastCall!;
    expect((args[0] as ComparisonFigureCard).id).toBe(component.panelCards[0].id);
    expect(args.some(arg => arg instanceof HTMLCanvasElement)).toBe(false);
  });

  it('re-composes the Single stage on a highlight toggle while it is shown, and not on the All tab', () => {
    render(buildDto(comparableSet(3)), 2);
    const renderPreview = vi.spyOn(component as unknown as {
      renderPreview(): Promise<void>;
    }, 'renderPreview').mockResolvedValue();
    const box = (): DebugElement => fixture.debugElement.queryAll(By.css('.mc-models-table input[id^="mc-emph-"]'))[0];

    vi.useFakeTimers();
    try {
      box().triggerEventHandler('change', { target: box().nativeElement });
      fixture.detectChanges();
      vi.advanceTimersByTime(200);
      expect(renderPreview).not.toHaveBeenCalled();

      openSingle();
      vi.advanceTimersByTime(200);
      renderPreview.mockClear();

      box().triggerEventHandler('change', { target: box().nativeElement });
      fixture.detectChanges();
      expect(renderPreview).not.toHaveBeenCalled();
      vi.advanceTimersByTime(200);
      expect(renderPreview).toHaveBeenCalledTimes(1);
    }
    finally {
      vi.useRealTimers();
    }
  });

  it('lights a hover highlight on the All tab, none on the Single tab, and still clears one', async () => {
    render(buildDto(comparableSet(3)), 2);
    await settleAllTab();
    const schedule = vi.spyOn(component as unknown as {
      scheduleAllCompose(): void;
    }, 'scheduleAllCompose');
    component.setHighlight('run:1');
    expect(component.highlightedKey).toBe('run:1');
    // The tiles re-compose with the model lit.
    expect(schedule).toHaveBeenCalled();

    openSingle();
    expect(component.highlightedKey, 'entering the Single tab clears it').toBeNull();

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
    vi.spyOn(component, 'measureAllViewport').mockReturnValue({ width: 1200, height: 900, devicePixelRatio: 2 });
    component.onExportDensityChange(1);
    render(buildDto(comparableSet(3)), 2);
    await settleAllTab();
  }

  it('opens the All tab at Fit height: one figure’s full height in the visible height', async () => {
    await openFittedAll();

    expect(component.allActive).toBe(true);
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
    expect(press(tile, '+').defaultPrevented).toBe(true);
    expect(component.allView).toBeGreaterThan(fit);
    const zoomedIn = component.allZoomValue;
    press(allPanel(), '=');
    expect(component.allZoomValue).toBeGreaterThan(zoomedIn);
    press(allPanel(), '-');
    expect(component.allZoomValue).toBeCloseTo(zoomedIn, 9);
    press(allPanel(), '0');
    expect(component.allView).toBe('fitHeight');

    const withCtrl = press(allPanel(), '+', { ctrlKey: true });
    expect(withCtrl.defaultPrevented).toBe(false);
    expect(component.allView).toBe('fitHeight');
    const slider = fixture.debugElement.query(By.css('#mc-all-zoom')).nativeElement as HTMLInputElement;
    expect(press(slider, '-').defaultPrevented, 'the slider keeps its own keys').toBe(false);
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
      expect(tipId, name).toBeTruthy();
      expect((fixture.nativeElement as HTMLElement).querySelector(`#${tipId}`)?.getAttribute('popover')).toBe('hint');
      expect(button(name).hasAttribute('title')).toBe(false);
    }
  });

  it('keeps the reader’s zoom across a viewport resize, and re-fits while they have not zoomed', async () => {
    await openFittedAll();
    const measure = component.measureAllViewport as Mock;
    const refreshGeometry = (): void => (component as unknown as {
      refreshAllGeometry(): void;
    }).refreshAllGeometry();

    measure.mockReturnValue({ width: 1200, height: 600, devicePixelRatio: 2 });
    refreshGeometry();
    expect(component.allZoomValue).toBeCloseTo(fitHeightZoom(600, 1080, 2, 16), 9);

    component.setAllView(1);
    measure.mockReturnValue({ width: 1200, height: 1000, devicePixelRatio: 2 });
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
      expect(canvas.width, card.id).toBe(Math.round(1920 * Math.min(1, zoom)));
      expect(canvas.height, card.id).toBe(Math.round(1080 * Math.min(1, zoom)));
    }
    expect(component.allTileRefusals).toEqual({});
  });

  it('re-composes the tiles and persists a size change, giving every tile the new box', async () => {
    withoutIntersectionObserver();
    await openFittedAll();
    await composeAllTiles();
    const schedule = vi.spyOn(component as unknown as {
      scheduleAllCompose(): void;
    }, 'scheduleAllCompose');

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
    const notice = (index: number): string => `Notice ${index}: ` + 'the speed axis is degraded for this entry, so its bar is drawn from a partial sample. '.repeat(6);
    vi.spyOn(component as unknown as {
      exportChrome(card: ComparisonFigureCard): unknown;
    }, 'exportChrome').mockReturnValue({
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
    expect(tiles.slice(1).every(tile => tile.classList.contains('is-deferred'))).toBe(true);
    expect(tiles[1].style.getPropertyValue('contain-intrinsic-size')).toContain(`${Math.round(component.allTileCssHeight)}px`);
  });

  it('stops composing, and drops its observers and timers, on leaving the All tab, step 2 and on destroy', async () => {
    await openFittedAll();
    const internals = component as unknown as {
      allComposeTimer: unknown; allResizeObserver: unknown; allIntersectionObserver: unknown;
    };
    expect(internals.allComposeTimer).not.toBeNull();

    openSingle();
    expect(component.allActive).toBe(false);
    expect(internals.allComposeTimer).toBeNull();
    expect(internals.allResizeObserver).toBeNull();
    expect(internals.allIntersectionObserver).toBeNull();

    component.selectFigureTab('all');
    refresh();
    expect(component.allActive).toBe(true);
    component.goToStep(1);
    fixture.detectChanges();
    expect(component.allActive).toBe(false);
    expect(internals.allComposeTimer).toBeNull();

    component.goToStep(2);
    fixture.detectChanges();
    await settleAllTab();
    expect(component.allActive).toBe(true);
    fixture.destroy();
    expect(component.allActive).toBe(false);
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
    expect(component.styleFamily, 'follows the figure on the stage').toBe('bar');
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
      expect(panels.length, 'only the selected family\'s panel exists').toBe(1);
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
    expect(component.styleFamily, 'Left wraps to the last tab').toBe('scatter');
    expectSelected(2);
    expect(document.activeElement).toBe(tabs()[2]);

    press(2, 'ArrowRight');
    expect(component.styleFamily, 'Right wraps to the first tab').toBe('bar');
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
      expect(outsidePanel(text).length, text).toBe(0);
    }
    expect(fixture.debugElement.query(By.css('dialog.mc-preview-dialog'))).toBeNull();
    // Each export control exists once.
    expect(step.querySelectorAll('#mc-export-resolution').length).toBeLessThanOrEqual(1);
    expect(fixture.debugElement.queryAll(By.css('app-figure-style-panel')).length).toBeLessThanOrEqual(1);
  });

  it('returns to the Single tab after a step away, and watches the stage again', async () => {
    render(buildDto(comparableSet(3)), 2);
    openSingle();
    const observe = vi.spyOn(component as unknown as {
      observeStage(): void;
    }, 'observeStage');

    component.goToStep(1);
    fixture.detectChanges();
    expect(component.previewActive).toBe(false);
    expect(fixture.debugElement.query(By.css('.mc-preview-stage'))).toBeNull();

    component.goToStep(2);
    fixture.detectChanges();
    // The re-attach runs in a microtask, outside the check pass that found the stage.
    await Promise.resolve();
    fixture.detectChanges();

    expect(component.figureTab).toBe('single');
    expect(singleTabButton().getAttribute('aria-selected')).toBe('true');
    expect(component.previewActive).toBe(true);
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
