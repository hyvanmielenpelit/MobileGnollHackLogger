import type { Mock } from 'vitest';
import { Component, Input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { Chart } from 'chart.js';

import { APP_CHART_REGISTRABLES } from '../../../../chart-registrables';
import { FigureExportLayout, ZipWriterModule, zipWriterModule } from '../../model-comparison/figure-export';
import { FigureSizeSettings, defaultFigureSize } from '../../model-comparison/figure-size';
import { DEFAULT_FIGURE_STYLE, FigureStyle } from '../../model-comparison/figure-style';
import { PREVIEW_SLIDER_STEPS } from '../../model-comparison/preview-view';
import { figureLogoIo, resetFigureLogoCache } from '../../model-comparison/figure-logo';
import { CC_FIGURE_KEYS, CcFigure, CcFigureInput, CcFigureKey } from '../chat-consistency-charts';
import { CcRunInclusion } from '../chat-consistency-scope';
import { CcBatteryRunRow, CcComparisonSetKind, CcModelAxis, CcTimeline, CcUnitKind } from '../chat-consistency.models';
import {
  CC_API,
  CC_BATTERY_SET_KEY,
  ccAxis,
  ccBatteryPoint,
  ccBatteryRunRows,
  ccEventTimeline,
  chatConsistencyTestProviders,
  textOf
} from '../chat-consistency-tab.testing';
import { ccDisplaySize, ccTargetPixels } from './cc-chart-zoom';
import { CcComposeContext, ccFigureChrome, ccFigureRequest } from './cc-figure-compose';
import {
  CC_CHART_SIZE_STORAGE_KEY,
  CC_FIGURE_STYLE_PANEL_OPEN_KEY,
  CC_FIGURE_STYLE_STORAGE_KEY,
  CC_TIMELINE_STORAGE_KEY,
  CcTimelineWorkspaceComponent,
  parseLegacyImageSettings,
  parseTimelineLayout
} from './timeline-workspace.component';

/** Every Chat Consistency storage key, cleared around each test. */
const STORAGE_KEYS = [
  CC_TIMELINE_STORAGE_KEY, CC_CHART_SIZE_STORAGE_KEY, CC_FIGURE_STYLE_STORAGE_KEY, CC_FIGURE_STYLE_PANEL_OPEN_KEY,
  'overseer.benchmark.chatConsistency.launcher'
];

/** The workspace's private composition members the specs read. */
interface WorkspaceInternals {
  dpr: number;
  composeContext(): CcComposeContext;
  screenLayout(figure: CcFigure, context: CcComposeContext): { layout: FigureExportLayout | null; refusal: string };
  fileComposition(key: CcFigureKey, snapshot: unknown): { figure: CcFigure | null; layout: FigureExportLayout | null };
  exportSnapshot(): { context: CcComposeContext };
  screenImages: Map<CcFigureKey, { canvas: HTMLCanvasElement | null; refusal: string; version: number }>;
}

const FIGURE_ORDER: CcFigureKey[] = CC_FIGURE_KEYS.map(entry => entry.key);

/** `openai/gpt-5|high` reduced to a file name part. */
const MODEL_PART = 'openai-gpt-5-high';

function clearStorage(): void {
  for (const key of STORAGE_KEYS) {
    try {
      localStorage.removeItem(key);
    } catch {
      // Nothing stored.
    }
  }
}

/** The wizard's Charts step: a sized `.gh-fig-host` the workspace fills. */
@Component({
  standalone: true,
  imports: [CcTimelineWorkspaceComponent],
  template: `
    <section class="gh-wizard-step gh-fig-host" style="display: flex; flex-direction: column; width: 780px; height: 560px;">
      <app-cc-timeline-workspace [axis]="axis" [timeline]="timeline" [loading]="loading" [error]="error"
                                 [notAnalyzed]="notAnalyzed" [rangeLabel]="rangeLabel"
                                 [unitKind]="unitKind" [setKey]="setKey" [batteryRows]="batteryRows"
                                 [notAnalyzedUnits]="notAnalyzedUnits" [setLabel]="setLabel" [setKind]="setKind"
                                 (exportingChange)="exportingEvents.push($event)"></app-cc-timeline-workspace>
    </section>`
})
class TimelineWorkspaceHostComponent {
  @Input() axis: CcModelAxis | null = null;
  @Input() timeline: CcTimeline | null = null;
  @Input() loading = false;
  @Input() error: string | null = null;
  @Input() notAnalyzed: ReadonlyMap<number, CcRunInclusion> | null = null;
  @Input() rangeLabel = '';
  @Input() unitKind: CcUnitKind = 'run';
  @Input() setKey: string | null = null;
  @Input() batteryRows: readonly CcBatteryRunRow[] = [];
  @Input() notAnalyzedUnits: ReadonlyMap<number, CcRunInclusion> | null = null;
  @Input() setLabel = '';
  @Input() setKind: CcComparisonSetKind | null = null;
  readonly exportingEvents: boolean[] = [];
}

interface EncodeCall {
  type: string | undefined;
  quality: unknown;
}

describe('CcTimelineWorkspaceComponent', () => {
  let fixture: ComponentFixture<TimelineWorkspaceHostComponent>;
  let host: TimelineWorkspaceHostComponent;
  let ws: CcTimelineWorkspaceComponent;
  let el: HTMLElement;
  let http: HttpTestingController;

  beforeEach(async () => {
    clearStorage();
    // A logo the canvas can draw, without loading the asset.
    resetFigureLogoCache();
    vi.spyOn(figureLogoIo, 'loadImage').mockResolvedValue(document.createElement('canvas'));
    await TestBed.configureTestingModule({
      imports: [TimelineWorkspaceHostComponent],
      providers: chatConsistencyTestProviders()
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    if (fixture) fixture.destroy();
    http.verify();
    clearStorage();
    resetFigureLogoCache();
    // `withClipboard` stands in for `navigator.clipboard` on the instance.
    delete (navigator as unknown as { clipboard?: unknown }).clipboard;
  });

  // --- Helpers ---

  async function create(timeline: CcTimeline | null = ccEventTimeline(), axis: CcModelAxis | null = ccAxis()): Promise<void> {
    fixture = TestBed.createComponent(TimelineWorkspaceHostComponent);
    host = fixture.componentInstance;
    fixture.componentRef.setInput('axis', axis);
    fixture.componentRef.setInput('timeline', timeline);
    fixture.detectChanges();
    ws = fixture.debugElement.query(By.directive(CcTimelineWorkspaceComponent)).componentInstance as CcTimelineWorkspaceComponent;
    el = fixture.nativeElement as HTMLElement;
    await settle();
  }

  /** Lets the measuring frames, the ResizeObserver and the IntersectionObserver run, and renders after each. */
  async function settle(rounds = 4): Promise<void> {
    for (let i = 0; i < rounds; i++) {
      fixture.detectChanges();
      await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
      await new Promise<void>(resolve => setTimeout(resolve));
    }
    fixture.detectChanges();
  }

  const macrotask = (): Promise<void> => new Promise<void>(resolve => setTimeout(resolve));

  function q<T extends Element = HTMLElement>(selector: string): T | null {
    return el.querySelector<T>(selector);
  }

  function qa<T extends Element = HTMLElement>(selector: string): T[] {
    return Array.from(el.querySelectorAll<T>(selector));
  }

  function clickOn(selector: string): void {
    const target = q<HTMLElement>(selector);
    expect(target, selector).not.toBeNull();
    target!.click();
    fixture.detectChanges();
  }

  function press(target: Element, key: string, init: KeyboardEventInit = {}): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  function setSelect(selector: string, value: string): void {
    const select = q<HTMLSelectElement>(selector)!;
    select.value = value;
    expect(select.value, `${selector} offers ${value}`).toBe(value);
    select.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
  }

  function storedLayout(): Record<string, unknown> {
    return JSON.parse(localStorage.getItem(CC_TIMELINE_STORAGE_KEY) ?? 'null') as Record<string, unknown>;
  }

  function storedSize(): Record<string, unknown> {
    return JSON.parse(localStorage.getItem(CC_CHART_SIZE_STORAGE_KEY) ?? 'null') as Record<string, unknown>;
  }

  function openSideTab(id: 'data' | 'events' | 'annotations' | 'theme' | 'charts' | 'download'): void {
    clickOn(`#cc-tl-side-tab-${id}`);
  }

  function internals(): WorkspaceInternals {
    return ws as unknown as WorkspaceInternals;
  }

  /** The image box a size and zoom give, as the workspace computes it. */
  function expectedBox(settings: FigureSizeSettings, zoom: number, fit: boolean): { width: number; height: number } {
    const box = ccDisplaySize(ccTargetPixels(settings)!, zoom, internals().dpr, fit);
    return { width: box.cssWidth, height: box.cssHeight };
  }

  /** Waits until a chart's screen image is composed at the current settings. */
  async function untilComposed(key: CcFigureKey): Promise<HTMLCanvasElement> {
    let canvas: HTMLCanvasElement | null = null;
    await vi.waitFor(() => {
      fixture.detectChanges();
      const image = internals().screenImages.get(key);
      const current = (ws as unknown as { composeVersion: number }).composeVersion;
      if (!image?.canvas || image.version !== current) throw new Error(`${key} is not composed yet.`);
      canvas = image.canvas;
    }, { timeout: 15_000, interval: 20 });
    fixture.detectChanges();
    return canvas!;
  }

  /** Gives the hosting step a desktop size, larger than the test page, and lets the views re-fit. */
  async function desktopHost(width = 1600, height = 1000): Promise<void> {
    const section = q<HTMLElement>('section.gh-fig-host')!;
    section.style.width = `${width}px`;
    section.style.height = `${height}px`;
    await settle(8);
  }

  /** The workspace's figure style with `appearance` changed, through the panel's change event. */
  function changeAppearance(change: Partial<FigureStyle['appearance']>): void {
    ws.onFigureStyleChange({ ...ws.figureStyle, appearance: { ...ws.figureStyle.appearance, ...change } });
    fixture.detectChanges();
  }

  function tileKeys(): string[] {
    return qa('#cc-tl-view-panel-all .cc-tl-tile').map(tile => tile.getAttribute('data-figure') ?? '');
  }

  function singleTileKey(): string | null {
    return q('#cc-tl-view-panel-single .cc-tl-tile')?.getAttribute('data-figure') ?? null;
  }

  /** The chart box of a tile, as the workspace sized it. */
  function boxSize(selector: string): { width: number; height: number } {
    const box = q<HTMLElement>(`${selector} .cc-chart-box`)!;
    return { width: Number.parseFloat(box.style.inlineSize), height: Number.parseFloat(box.style.blockSize) };
  }

  function zoomButton(label: string): HTMLButtonElement {
    return q<HTMLButtonElement>(`button[aria-label="${label}"]`)!;
  }

  function figure(key: CcFigureKey) {
    return ws.figures.find(entry => entry.key === key)!;
  }

  function seriesIds(key: CcFigureKey): string[] {
    return figure(key).config!.data.datasets.map(dataset => dataset.seriesId);
  }

  async function untilExported(): Promise<void> {
    await vi.waitFor(() => {
      if (ws.exporting) throw new Error('Still exporting.');
    }, { timeout: 15_000, interval: 20 });
    fixture.detectChanges();
  }

  /**
   * Stands in for the canvas encoder, which `encodeFigureImage` reaches through `toBlob`: records
   * each request and answers with the requested type, or a PNG for WebP where `writesWebp` is false.
   */
  function stubEncoder(writesWebp: boolean): EncodeCall[] {
    const calls: EncodeCall[] = [];
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation((callback: BlobCallback, type?: string, quality?: unknown) => {
      calls.push({ type, quality });
      const written = type === 'image/webp' && !writesWebp ? 'image/png' : type ?? 'image/png';
      callback(new Blob(['image'], { type: written }));
    });
    return calls;
  }

  /** The save path of `saveFigureBlob`: the blob through its object URL, the name through the anchor. */
  function captureSaves(): { blobs: Blob[]; names: string[] } {
    const saved: { blobs: Blob[]; names: string[] } = { blobs: [], names: [] };
    vi.spyOn(URL, 'createObjectURL').mockImplementation((source: Blob | MediaSource) => {
      saved.blobs.push(source as Blob);
      return 'blob:chat-consistency-test';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      saved.names.push(this.download);
    });
    return saved;
  }

  /** The zip encoder behind `buildFigureArchive`'s holder; records the entry names of each archive. */
  function stubZipWriter(): { names: string[][]; load: Mock } {
    const names: string[][] = [];
    const writer: ZipWriterModule = {
      zipSync: (data: Record<string, unknown>) => {
        names.push(Object.keys(data));
        // An empty archive's end-of-central-directory record.
        return new Uint8Array([0x50, 0x4b, 0x05, 0x06]);
      }
    };
    const load = vi.spyOn(zipWriterModule, 'load').mockResolvedValue(writer);
    return { names, load: load as unknown as Mock };
  }

  /** `navigator.clipboard` is a prototype getter, so it is stood in for on the instance. */
  function withClipboard(value: unknown): void {
    Object.defineProperty(navigator, 'clipboard', { value, configurable: true });
  }

  // --- Sidebar ---

  it('switches the settings tabs by click and by Left / Right with wrap and Home / End, and stores the tab', async () => {
    await create();
    const tabs = qa<HTMLButtonElement>('[role="tablist"][aria-label="Settings sections"] [role="tab"]');
    expect(tabs.map(tab => textOf(tab))).toEqual(['Data', 'Events', 'Annotations', 'Theme', 'Charts', 'Download']);
    expect(tabs.map(tab => tab.getAttribute('aria-selected'))).toEqual(['true', 'false', 'false', 'false', 'false', 'false']);
    expect(tabs.map(tab => tab.getAttribute('tabindex'))).toEqual(['0', '-1', '-1', '-1', '-1', '-1']);
    expect(q('#cc-tl-side-panel-data')!.getAttribute('aria-labelledby')).toBe('cc-tl-side-tab-data');
    expect(q('#cc-tl-sidebar')!.getAttribute('aria-label')).toBe('Chart settings');

    openSideTab('events');
    expect(q('#cc-tl-side-panel-events')).not.toBeNull();
    expect(q('#cc-tl-side-panel-data')).toBeNull();
    expect(storedLayout()['sidebarTab']).toBe('events');

    const right = press(q('#cc-tl-side-tab-events')!, 'ArrowRight');
    expect(right.defaultPrevented).toBe(true);
    expect(q('#cc-tl-side-tab-annotations')!.getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement?.id).toBe('cc-tl-side-tab-annotations');
    // The Annotations tab lists the model's annotations.
    const annotations = http.expectOne(r => r.url === `${CC_API}/annotations`);
    expect(annotations.request.params.get('provider')).toBe('OpenAI');
    expect(annotations.request.params.get('modelId')).toBe('gpt-5');
    annotations.flush([]);
    fixture.detectChanges();

    press(q('#cc-tl-side-tab-annotations')!, 'End');
    expect(document.activeElement?.id).toBe('cc-tl-side-tab-download');
    expect(q('#cc-tl-side-panel-download')).not.toBeNull();

    press(q('#cc-tl-side-tab-download')!, 'ArrowRight');
    expect(document.activeElement?.id).toBe('cc-tl-side-tab-data');
    expect(q('#cc-tl-side-panel-data')).not.toBeNull();

    press(q('#cc-tl-side-tab-data')!, 'ArrowLeft');
    expect(document.activeElement?.id).toBe('cc-tl-side-tab-download');

    press(q('#cc-tl-side-tab-download')!, 'Home');
    expect(document.activeElement?.id).toBe('cc-tl-side-tab-data');
    expect(storedLayout()['sidebarTab']).toBe('data');

    const other = press(q('#cc-tl-side-tab-data')!, 'Enter');
    expect(other.defaultPrevented).toBe(false);
  });

  it('keeps a visited Annotations panel mounted but hidden under the other tabs', async () => {
    await create();
    openSideTab('annotations');
    http.expectOne(r => r.url === `${CC_API}/annotations`).flush([]);
    fixture.detectChanges();
    openSideTab('data');

    const annotations = q<HTMLElement>('#cc-tl-side-panel-annotations');
    expect(annotations).not.toBeNull();
    expect(annotations!.hidden).toBe(true);
    expect(getComputedStyle(annotations!).display).toBe('none');
    expect(getComputedStyle(q('#cc-tl-side-panel-data')!).display).not.toBe('none');
  });

  it('collapses and reopens the sidebar from its toggle, and resizes it with the separator', async () => {
    await create();
    const toggle = q<HTMLButtonElement>('.cc-tl-sidebar-toggle')!;
    expect(toggle.getAttribute('aria-label')).toBe('Chart settings');
    expect(toggle.getAttribute('aria-controls')).toBe('cc-tl-sidebar');
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(textOf(q('#cc-tl-tip-sidebar'))).toBe('Hide settings');
    expect(q('app-pane-resizer')).not.toBeNull();

    toggle.click();
    fixture.detectChanges();
    expect(q('#cc-tl-sidebar')!.hidden).toBe(true);
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(q('.gh-fig-workspace')!.classList).toContain('is-collapsed');
    expect(q('app-pane-resizer')).toBeNull();
    expect(textOf(q('#cc-tl-tip-sidebar'))).toBe('Show settings');
    expect(storedLayout()['sidebarCollapsed']).toBe(true);

    toggle.click();
    fixture.detectChanges();
    expect(q('#cc-tl-sidebar')!.hidden).toBe(false);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(storedLayout()['sidebarCollapsed']).toBe(false);

    const resizer = q('app-pane-resizer')!;
    expect(resizer.getAttribute('aria-controls')).toBe('cc-tl-sidebar');
    expect(resizer.getAttribute('aria-valuenow')).toBe('416');
    press(resizer, 'ArrowRight');
    expect(q('.gh-fig-workspace')!.style.getPropertyValue('--gh-fig-sidebar-width')).toBe('432px');
    expect(resizer.getAttribute('aria-valuenow')).toBe('432');
    expect(storedLayout()['sidebarWidth']).toBe(432);
  });

  it('opens a collapsed sidebar on the event list from a chart\'s Show events', async () => {
    await create();
    clickOn('.cc-tl-sidebar-toggle');
    expect(q('#cc-tl-sidebar')!.hidden).toBe(true);

    clickOn('.cc-tl-tile[data-figure="quality"] .cc-figure-show-events');
    expect(q('#cc-tl-sidebar')!.hidden).toBe(false);
    expect(q('#cc-tl-side-tab-events')!.getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement?.id).toBe('cc-tl-events');
  });

  // --- Data ---

  it('shows and hides charts with the Charts checkboxes and with All / None', async () => {
    await create();
    expect(textOf(q('.cc-tl-readout'))).toBe('GPT-5 high · every date · change in step 1');
    const boxes = (): HTMLInputElement[] => qa<HTMLInputElement>('input[id^="cc-tl-show-"]');
    expect(boxes().map(box => box.id)).toEqual(FIGURE_ORDER.map(key => `cc-tl-show-${key}`));
    expect(boxes().every(box => box.checked)).toBe(true);
    expect(tileKeys()).toEqual(FIGURE_ORDER);
    expect(textOf(q('.cc-tl-all-figures'))).toBe('All charts');
    expect(textOf(q('.cc-tl-no-figures'))).toBe('None of the charts');

    clickOn('#cc-tl-show-cost');
    const withoutCost = FIGURE_ORDER.filter(key => key !== 'cost');
    expect(tileKeys()).toEqual(withoutCost);
    expect(ws.figures.map(entry => entry.key)).toEqual(withoutCost);
    expect(storedLayout()['figures']).toEqual(withoutCost);

    clickOn('.cc-tl-no-figures');
    expect(tileKeys()).toEqual([]);
    expect(boxes().some(box => box.checked)).toBe(false);
    expect(textOf(q('#cc-tl-view-panel-all .cc-tl-empty'))).toBe('No chart is shown. Choose charts under Data.');
    expect(qa('.cc-tl-hint').some(hint => textOf(hint) === 'No shown chart draws more than one series.')).toBe(true);
    expect(q('.cc-tl-download-all')!.getAttribute('aria-disabled')).toBe('true');
    expect(textOf(q('#cc-tl-tip-download-all'))).toBe('No chart is shown. Choose charts under Data.');
    expect(storedLayout()['figures']).toEqual([]);

    clickOn('.cc-tl-all-figures');
    expect(tileKeys()).toEqual(FIGURE_ORDER);
    expect(boxes().every(box => box.checked)).toBe(true);
  });

  it('leaves a hidden series out of its chart, and lists only the series of the shown charts', async () => {
    await create();
    // Without a common grader the quality chart draws one series, so it has no series checklist.
    expect(q('#cc-tl-series-quality-common')).toBeNull();
    // Output tokens and tool calls are one series each, so neither has a checklist.
    expect(q('#cc-tl-series-work-tokens')).toBeNull();
    expect(q('#cc-tl-series-tools-calls')).toBeNull();
    const timeouts = q<HTMLInputElement>('#cc-tl-series-reliability-timeoutRate')!;
    expect(timeouts.checked).toBe(true);
    expect(seriesIds('reliability')).toContain('reliability.timeoutRate');

    timeouts.click();
    fixture.detectChanges();
    expect(q<HTMLInputElement>('#cc-tl-series-reliability-timeoutRate')!.checked).toBe(false);
    expect(seriesIds('reliability')).not.toContain('reliability.timeoutRate');
    expect(seriesIds('reliability')).toContain('reliability.refusalRate');
    expect(storedLayout()['hiddenSeries']).toEqual(['reliability.timeoutRate']);

    clickOn('#cc-tl-show-reliability');
    expect(q('#cc-tl-series-reliability-timeoutRate')).toBeNull();
  });

  it('shows the full 0–100 Intelligence scale on request', async () => {
    await create();
    const bounds = (key: CcFigureKey): [number | undefined, number | undefined] => {
      const y = figure(key).config!.options.scales!['y'] as unknown as { min?: number; max?: number };
      return [y.min, y.max];
    };
    expect(textOf(q('#cc-tl-zero-baseline')!.closest('label'))).toBe('Show the full 0–100 Intelligence scale');
    expect(bounds('quality')).not.toEqual([0, 100]);
    expect(bounds('ttfat')[0]).toBe(0);
    const ttfat = bounds('ttfat');

    clickOn('#cc-tl-zero-baseline');
    expect(q<HTMLInputElement>('#cc-tl-zero-baseline')!.checked).toBe(true);
    expect(bounds('quality')).toEqual([0, 100]);
    expect(bounds('ttfat')).toEqual(ttfat);
    expect(storedLayout()['zeroBaseline']).toBe(true);
  });

  it('sets each chart\'s decimal places under Charts, stores them, and resets them with All automatic', async () => {
    await create();
    expect(q('select[id^="cc-tl-decimals-"]')).toBeNull();
    openSideTab('charts');
    expect(q('#cc-tl-side-panel-charts .cc-tl-decimals legend')).not.toBeNull();
    expect(textOf(q('#cc-tl-side-panel-charts .cc-tl-decimals legend'))).toBe('Number format');
    const keys = ['quality', 'ttfat', 'rate', 'work', 'tools', 'cost', 'reliability'];
    const selects = qa<HTMLSelectElement>('select[id^="cc-tl-decimals-"]');
    expect(selects.map(select => select.id)).toEqual(keys.map(key => `cc-tl-decimals-${key}`));
    // Each select is named by its label, the chart's title.
    expect(selects.map(select => textOf(select.labels?.[0] ?? null)))
      .toEqual(FIGURE_ORDER.filter(key => key !== 'timeline').map(key => CC_FIGURE_KEYS.find(entry => entry.key === key)!.title));
    expect(selects.map(select => textOf(select.options[0]))).toEqual([
      'Automatic (0)', 'Automatic (1, in seconds)', 'Automatic (1)', 'Automatic (0)', 'Automatic (1)', 'Automatic (2–4)', 'Automatic (1)'
    ]);
    expect(selects.map(select => select.value)).toEqual(keys.map(() => ''));
    const offers = (key: string) => Array.from(q<HTMLSelectElement>(`#cc-tl-decimals-${key}`)!.options).map(option => option.value);
    expect(offers('cost')).toEqual(['', '0', '1', '2', '3', '4']);
    expect(offers('quality')).toEqual(['', '0', '1', '2', '3']);

    const reset = q<HTMLButtonElement>('.cc-tl-decimals-reset')!;
    expect(textOf(reset)).toBe('All automatic decimal places');
    expect(reset.getAttribute('aria-disabled')).toBe('true');

    const before = figure('quality').table.rows[0][2];
    expect(before).toMatch(/^\d+$/);
    setSelect('#cc-tl-decimals-quality', '2');
    await settle();
    expect(figure('quality').table.rows[0][2]).toMatch(/^\d+\.\d{2}$/);
    expect(figure('quality').takeaway).toMatch(/\d+\.\d{2}/);
    expect(storedLayout()['decimals']).toEqual({ quality: 2 });
    expect(q<HTMLButtonElement>('.cc-tl-decimals-reset')!.getAttribute('aria-disabled')).toBeNull();

    setSelect('#cc-tl-decimals-cost', '4');
    await settle();
    expect(storedLayout()['decimals']).toEqual({ quality: 2, cost: 4 });
    setSelect('#cc-tl-decimals-quality', '');
    await settle();
    expect(figure('quality').table.rows[0][2]).toBe(before);
    expect(storedLayout()['decimals']).toEqual({ cost: 4 });

    clickOn('.cc-tl-decimals-reset');
    await settle();
    expect(storedLayout()['decimals']).toEqual({});
    expect(q<HTMLSelectElement>('#cc-tl-decimals-cost')!.value).toBe('');
    expect(q<HTMLButtonElement>('.cc-tl-decimals-reset')!.getAttribute('aria-disabled')).toBe('true');
    // A disabled reset refuses the click.
    const resetDecimals = vi.spyOn(ws, 'resetDecimals');
    clickOn('.cc-tl-decimals-reset');
    expect(resetDecimals).not.toHaveBeenCalled();
  });

  it('reads stored decimal places, keeping only each chart\'s choices', () => {
    expect(parseTimelineLayout({ version: 2, decimals: { work: 1, cost: 9, bogus: 2 } }).decimals).toEqual({ work: 1 });
    expect(parseTimelineLayout({ version: 2, decimals: { cost: 4, quality: '2' } }).decimals).toEqual({ cost: 4 });
    expect(parseTimelineLayout({ version: 2, decimals: [1, 2] }).decimals).toEqual({});
    expect(parseTimelineLayout({ version: 2 }).decimals).toEqual({});
  });

  it('marks the runs not in the analysis, behind a stored Mark runs not in the analysis switch', async () => {
    await create();
    const input = (): CcFigureInput => (ws as unknown as { figureInput(): CcFigureInput }).figureInput();
    expect(input().notAnalyzed).toBeUndefined();
    expect(figure('quality').takeaway).not.toContain('gray cross');

    fixture.componentRef.setInput('notAnalyzed', new Map<number, CcRunInclusion>([[201, 'beforeSpan'], [204, 'leftOut']]));
    fixture.componentRef.setInput('rangeLabel', 'Last 30 days');
    fixture.detectChanges();
    expect([...input().notAnalyzed!]).toEqual([[201, 'before the first run'], [204, 'left out in step 1']]);
    expect(figure('quality').takeaway).toContain('2 runs not in the analysis are drawn as gray crosses.');
    expect(textOf(q('.cc-tl-readout'))).toBe('GPT-5 high · Last 30 days · change in step 1');

    const box = q<HTMLInputElement>('#cc-tl-mark-not-analyzed')!;
    expect(box.checked).toBe(true);
    expect(textOf(box.closest('label'))).toContain('Mark runs not in the analysis');
    clickOn('#cc-tl-mark-not-analyzed');
    expect(input().notAnalyzed).toBeUndefined();
    expect(figure('quality').takeaway).not.toContain('gray cross');
    expect(storedLayout()['markNotAnalyzed']).toBe(false);

    clickOn('#cc-tl-mark-not-analyzed');
    expect(storedLayout()['markNotAnalyzed']).toBe(true);
  });

  it('reads a stored layout without the switch as on', () => {
    expect(parseTimelineLayout({ version: 1, zeroBaseline: true }).markNotAnalyzed).toBe(true);
    expect(parseTimelineLayout({ version: 1, markNotAnalyzed: 'no' }).markNotAnalyzed).toBe(true);
    expect(parseTimelineLayout({ version: 1, markNotAnalyzed: false }).markNotAnalyzed).toBe(false);
  });

  it('reads a version-1 layout that shows Work per answer as showing both of its charts, and drops work.tools', () => {
    const old = parseTimelineLayout({ version: 1, figures: ['quality', 'work'], hiddenSeries: ['work.tools', 'ttfat.proxy'] });
    expect(old.figures).toEqual(['quality', 'work', 'tools']);
    expect(old.hiddenSeries).toEqual(['ttfat.proxy']);
    expect(parseTimelineLayout({ version: 1, figures: ['quality'] }).figures).toEqual(['quality']);
    // A version-2 layout means what it says.
    expect(parseTimelineLayout({ version: 2, figures: ['work'] }).figures).toEqual(['work']);
  });

  it('reads a stored layout\'s legacy image theme and logo, each null where absent or of the wrong kind', () => {
    expect(parseLegacyImageSettings({ version: 2, imageTheme: 'print', logo: false })).toEqual({ imageTheme: 'print', logo: false });
    expect(parseLegacyImageSettings({ imageTheme: 'sepia', logo: 'no' })).toEqual({ imageTheme: null, logo: null });
    expect(parseLegacyImageSettings(null)).toEqual({ imageTheme: null, logo: null });
  });

  it('writes the layout as version 2, without the image theme and logo the figure style holds', async () => {
    await create();
    clickOn('#cc-tl-zero-baseline');
    expect(storedLayout()['version']).toBe(2);
    expect(storedLayout()['logo']).toBeUndefined();
    expect(storedLayout()['imageTheme']).toBeUndefined();
  });

  // --- Events ---

  it('filters the chart markers and the event list together, by marker kind and by change kind', async () => {
    await create();
    openSideTab('events');
    const listTags = (kind: string): (string | null)[] =>
      qa(`#cc-tl-events .cc-ev-item[data-kind="${kind}"]`).map(item => item.getAttribute('data-tag'));
    const markerTags = (kind: string): string[] =>
      figure('quality').markers.filter(marker => marker.kind === kind).map(marker => marker.tag).sort();

    expect(['event', 'annotation', 'served'].map(kind => q<HTMLInputElement>(`#cc-tl-marker-${kind}`)!.checked))
      .toEqual([true, true, true]);
    expect(listTags('event')).toEqual(['E1', 'E2', 'E3', 'E4']);
    expect(markerTags('event')).toEqual(['E1', 'E2', 'E3', 'E4']);
    expect([...listTags('annotation')].sort()).toEqual(['A1', 'A2']);
    expect([...listTags('served')].sort()).toEqual(['S1', 'S2']);
    expect(markerTags('served')).toEqual(['S1', 'S2']);

    clickOn('#cc-tl-marker-event');
    expect(listTags('event')).toEqual([]);
    expect(markerTags('event')).toEqual([]);
    expect(listTags('annotation').length).toBe(2);
    expect(markerTags('annotation')).toEqual(['A1', 'A2']);
    expect(textOf(q('#cc-tl-ev-summary'))).toContain('4 hidden by the filters');
    expect(storedLayout()['markerKinds']).toEqual(['annotation', 'served']);

    clickOn('#cc-tl-marker-event');
    expect(listTags('event')).toEqual(['E1', 'E2', 'E3', 'E4']);
    expect(markerTags('event')).toEqual(['E1', 'E2', 'E3', 'E4']);

    // E4 holds only the source code and corpus index changes.
    clickOn('#cc-tl-kind-SourceCodeHeadSha');
    clickOn('#cc-tl-kind-CorpusIndexFingerprintsJson');
    expect(listTags('event')).toEqual(['E1', 'E2', 'E3']);
    expect(markerTags('event')).toEqual(['E1', 'E2', 'E3']);

    // E1 keeps its system prompt change, so it stays.
    clickOn('#cc-tl-kind-KnowledgeBaseHeadSha');
    expect(listTags('event')).toEqual(['E1', 'E2', 'E3']);
    expect(markerTags('event')).toEqual(['E1', 'E2', 'E3']);
    expect(storedLayout()['hiddenEventKinds'])
      .toEqual(['SourceCodeHeadSha', 'CorpusIndexFingerprintsJson', 'KnowledgeBaseHeadSha']);
  });

  // --- Zoom ---

  it('opens All charts at Fit to screen, and zooms with the buttons, the slider and the keys', async () => {
    await create();
    const size = ws.chartSize;
    const panel = q('#cc-tl-view-panel-all')!;
    expect(panel.getAttribute('aria-keyshortcuts')).toBe('+ - 0');
    expect(ws.zoomLabel).toMatch(/^\d+(\.\d)?% · Fit to screen$/);
    expect(textOf(q('#cc-tl-view-panel-all .gh-zoom-value'))).toBe(ws.zoomLabel);
    expect(q('#cc-tl-all-zoom')!.getAttribute('aria-valuetext')).toMatch(/ percent, fitted to the screen$/);
    const fitScreen = ws.zoom;
    // A fit is whole CSS px.
    expect(boxSize('.cc-tl-tile[data-figure="quality"]')).toEqual(expectedBox(size, fitScreen, true));
    expect(Number.isInteger(boxSize('.cc-tl-tile[data-figure="quality"]').width)).toBe(true);
    // The toolbar offers Fit width and Fit to screen.
    expect(q('#cc-tl-view-panel-all .cc-tl-fit-width')).not.toBeNull();
    expect(q('#cc-tl-view-panel-all .cc-tl-fit-screen')!.getAttribute('aria-label')).toBe('Fit to screen');
    expect(q('#cc-tl-view-panel-all .cc-tl-fit-height')).toBeNull();
    expect(textOf(q('#cc-tl-tip-all-fit-screen'))).toBe('Fit one whole chart to the view (0)');

    zoomButton('Zoom all charts in').click();
    fixture.detectChanges();
    expect(ws.zoom).toBeGreaterThan(fitScreen);
    expect(ws.zoomLabel).toMatch(/^\d+(\.\d)?%$/);
    const zoomedBox = expectedBox(size, ws.zoom, false);
    expect(boxSize('.cc-tl-tile[data-figure="quality"]').width).toBeCloseTo(zoomedBox.width, 3);
    expect(boxSize('.cc-tl-tile[data-figure="quality"]').height).toBeCloseTo(zoomedBox.height, 3);
    const zoomedIn = ws.zoom;

    zoomButton('Zoom all charts out').click();
    fixture.detectChanges();
    expect(ws.zoom).toBeLessThan(zoomedIn);

    clickOn('.cc-tl-fit-width');
    expect(ws.zoomLabel).toMatch(/ · Fit width$/);
    // One whole chart fits the screen no wider than the width does.
    expect(ws.zoom).toBeGreaterThanOrEqual(fitScreen - 1e-6);

    clickOn('#cc-tl-view-panel-all .cc-tl-fit-screen');
    expect(ws.zoomLabel).toMatch(/ · Fit to screen$/);
    expect(ws.zoom).toBeCloseTo(fitScreen, 6);

    // The slider applies on the next frame.
    const slider = q<HTMLInputElement>('#cc-tl-all-zoom')!;
    slider.value = String(PREVIEW_SLIDER_STEPS);
    slider.dispatchEvent(new Event('input', { bubbles: true }));
    await settle(1);
    expect(ws.zoomLabel).toBe('800%');
    expect(zoomButton('Zoom all charts in').getAttribute('aria-disabled')).toBe('true');

    const minus = press(panel, '-');
    expect(minus.defaultPrevented).toBe(true);
    const lowered = ws.zoom;
    expect(lowered).toBeLessThan(8);
    press(panel, '+');
    expect(ws.zoom).toBeGreaterThan(lowered);
    press(panel, '-');
    expect(ws.zoom).toBeCloseTo(lowered, 6);
    press(panel, '=');
    expect(ws.zoom).toBeGreaterThan(lowered);

    press(panel, '0');
    expect(ws.zoomLabel).toMatch(/ · Fit to screen$/);
    expect(ws.zoom).toBeCloseTo(fitScreen, 6);

    // Ctrl stays the browser's zoom, a form field keeps its keys, and 1 belongs to Single chart.
    const withCtrl = press(panel, '+', { ctrlKey: true });
    expect(withCtrl.defaultPrevented).toBe(false);
    press(slider, '+');
    const one = press(panel, '1');
    expect(one.defaultPrevented).toBe(false);
    expect(ws.zoomLabel).toMatch(/ · Fit to screen$/);
  });

  it('composes only the tiles near the view, keeping the others\' boxes', async () => {
    await create();
    await untilComposed('quality');
    expect(q('.cc-tl-tile[data-figure="quality"] canvas.cc-chart-image')).not.toBeNull();
    const last = q('.cc-tl-tile[data-figure="timeline"]')!;
    expect(last.querySelector('canvas')).toBeNull();
    expect(last.querySelector('.cc-chart-box')).not.toBeNull();
    expect(internals().screenImages.has('timeline')).toBe(false);

    const viewport = q('#cc-tl-view-panel-all .gh-fig-viewport')!;
    viewport.scrollTop = viewport.scrollHeight;
    await settle();
    await untilComposed('timeline');
    expect(last.querySelector('canvas.cc-chart-image')).not.toBeNull();
  }, 30_000);

  it('composes its charts on a page where nothing has registered chart.js', async () => {
    Chart.unregister(...APP_CHART_REGISTRABLES);
    await create();
    await untilComposed('quality');
    expect(q('.cc-tl-tile[data-figure="quality"] canvas.cc-chart-image')).not.toBeNull();
    expect(q('.cc-tl-tile[data-figure="quality"] .cc-figure-refusal')).toBeNull();
  }, 30_000);

  it('reshapes the images on a chart size change, re-fitting a fit view and keeping a numeric zoom', async () => {
    await create();
    clickOn('#cc-tl-view-panel-all .cc-tl-fit-width');
    await settle();
    const before = boxSize('.cc-tl-tile[data-figure="quality"]');
    expect(before.height / before.width).toBeCloseTo(1080 / 1920, 2);

    openSideTab('download');
    await settle(1);
    setSelect('#cc-export-resolution', 'uw1080');
    await settle();
    expect(storedSize()['resolutionId']).toBe('uw1080');
    const after = boxSize('.cc-tl-tile[data-figure="quality"]');
    const ultrawide = ccTargetPixels({ ...ws.chartSize })!;
    expect(after.height / after.width).toBeCloseTo(ultrawide.pixelHeight / ultrawide.pixelWidth, 2);
    expect(ws.zoomLabel).toMatch(/ · Fit width$/);
    // Fit width fills the same width with the wider image.
    expect(Math.abs(after.width - before.width)).toBeLessThanOrEqual(1);
    expect(after.height).toBeLessThan(before.height);

    zoomButton('Zoom all charts in').click();
    fixture.detectChanges();
    const label = ws.zoomLabel;
    const zoom = ws.zoom;
    expect(label).toMatch(/^\d+(\.\d)?%$/);

    setSelect('#cc-export-resolution', 'square1080');
    await settle();
    expect(ws.zoomLabel).toBe(label);
    const square = expectedBox(ws.chartSize, zoom, false);
    expect(boxSize('.cc-tl-tile[data-figure="quality"]').width).toBeCloseTo(square.width, 3);
    expect(boxSize('.cc-tl-tile[data-figure="quality"]').height).toBeCloseTo(square.height, 3);
  });

  // --- Views ---

  it('Single chart: picks a chart, steps through them with wrap, and zooms with Fit to screen and Actual size', async () => {
    await create();
    clickOn('#cc-tl-view-tab-single');
    await settle();
    expect(q('#cc-tl-view-tab-single')!.getAttribute('aria-selected')).toBe('true');
    expect(q('#cc-tl-view-panel-all')).toBeNull();
    expect(storedLayout()['view']).toBe('single');
    const panel = q('#cc-tl-view-panel-single')!;
    expect(panel.getAttribute('aria-keyshortcuts')).toBe('+ - 0 1');

    const select = q<HTMLSelectElement>('#cc-tl-single-figure')!;
    expect(Array.from(select.options).map(option => option.value)).toEqual(FIGURE_ORDER);
    expect(select.value).toBe('quality');
    expect(singleTileKey()).toBe('quality');
    expect(ws.zoomLabel).toMatch(/ · Fit to screen$/);

    clickOn('button[aria-label="Next chart"]');
    expect(singleTileKey()).toBe('ttfat');
    expect(select.value).toBe('ttfat');
    clickOn('button[aria-label="Previous chart"]');
    expect(singleTileKey()).toBe('quality');
    clickOn('button[aria-label="Previous chart"]');
    expect(singleTileKey()).toBe('timeline');
    expect(select.value).toBe('timeline');

    select.value = 'cost';
    select.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    expect(singleTileKey()).toBe('cost');
    expect(storedLayout()['singleFigure']).toBe('cost');

    const one = press(panel, '1');
    expect(one.defaultPrevented).toBe(true);
    expect(ws.zoomLabel).toBe('100%');
    // One file pixel on one device pixel: the file's pixels over the display's ratio.
    const target = ccTargetPixels(ws.chartSize)!;
    expect(boxSize('#cc-tl-view-panel-single .cc-tl-tile'))
      .toEqual({ width: target.pixelWidth / internals().dpr, height: target.pixelHeight / internals().dpr });

    press(panel, '+');
    expect(ws.zoom).toBeGreaterThan(1);
    press(panel, '0');
    expect(ws.zoomLabel).toMatch(/ · Fit to screen$/);

    clickOn('.cc-tl-actual-size');
    expect(ws.zoomLabel).toBe('100%');
    clickOn('.cc-tl-fit-screen');
    expect(ws.zoomLabel).toMatch(/ · Fit to screen$/);
  });

  it('opens a tile in Single view on its chart, and re-fits each view when it is entered', async () => {
    await create();
    zoomButton('Zoom all charts in').click();
    fixture.detectChanges();
    expect(ws.zoomLabel).not.toMatch(/Fit/);

    const open = q<HTMLButtonElement>('.cc-tl-tile[data-figure="cost"] .cc-tl-open')!;
    expect(open.getAttribute('aria-label')).toBe('Open Cost per question in Single view');
    open.click();
    fixture.detectChanges();
    expect(ws.view).toBe('single');
    expect(q<HTMLSelectElement>('#cc-tl-single-figure')!.value).toBe('cost');
    expect(document.activeElement?.id).toBe('cc-tl-single-figure');
    expect(ws.zoomLabel).toMatch(/ · Fit to screen$/);

    press(q('#cc-tl-view-panel-single')!, '1');
    expect(ws.zoomLabel).toBe('100%');

    clickOn('#cc-tl-view-tab-all');
    await settle();
    expect(ws.zoomLabel).toMatch(/ · Fit to screen$/);

    clickOn('#cc-tl-view-tab-single');
    await settle();
    expect(ws.zoomLabel).toMatch(/ · Fit to screen$/);
  });

  it('switches the views with Left / Right with wrap and Home / End', async () => {
    await create();
    const all = q('#cc-tl-view-tab-all')!;
    const single = q('#cc-tl-view-tab-single')!;
    expect(all.getAttribute('aria-controls')).toBe('cc-tl-view-panel-all');
    expect(all.closest('[role="tablist"]')!.getAttribute('aria-label')).toBe('Chart views');

    press(all, 'ArrowRight');
    expect(single.getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(single);
    press(single, 'ArrowRight');
    expect(all.getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(all);
    press(all, 'End');
    expect(document.activeElement).toBe(single);
    press(single, 'Home');
    expect(document.activeElement).toBe(all);
    press(all, 'ArrowLeft');
    expect(single.getAttribute('aria-selected')).toBe('true');
  });

  // --- Download tab ---

  it('renders the chart size and image format sections under Download, and stores their changes', async () => {
    await create();
    openSideTab('download');
    // ngModel writes the select's value in a later task.
    await settle(1);

    const size = q('app-export-size-section')!;
    expect(size.hasAttribute('title')).toBe(false);
    expect(textOf(q('#cc-export-section summary .gh-disclosure-summary-title'))).toBe('Chart size');
    expect(q<HTMLDetailsElement>('#cc-export-section')!.open).toBe(false);
    expect(textOf(q('#cc-export-text-scale-hint'))).toBe('Scales every text of the charts, on screen and in every image; the pixel size stays.');
    expect(q<HTMLSelectElement>('#cc-export-resolution')!.value).toBe('fullhd');

    expect(q('app-export-format-section')).not.toBeNull();
    expect(textOf(q('#cc-image-format-note'))).toBe('Every chart is downloaded in this format. Copy always writes a PNG.');
    expect(q<HTMLInputElement>('#cc-image-format-format-png')!.checked).toBe(true);
    expect(q('#cc-image-format-quality')).toBeNull();
    // The theme and the logo live in the Theme tab.
    expect(q('input[name="cc-tl-image-theme"]')).toBeNull();
    expect(q('#cc-tl-show-logo')).toBeNull();
    expect(qa('.cc-tl-hint').some(hint => textOf(hint).startsWith(
      'The image is the chart as shown: its heading, badges, logo, notes and footer, without the takeaway or Show data.'))).toBe(true);
    expect(q('.cc-tl-size-error')).toBeNull();

    clickOn('#cc-image-format-format-webp');
    expect(storedLayout()['imageFormat']).toBe('webp');
    expect(q<HTMLSelectElement>('#cc-image-format-quality')!.value).toBe('85');
    setSelect('#cc-image-format-quality', '95');
    expect(storedLayout()['webpQuality']).toBe(95);

    setSelect('#cc-export-resolution', 'qhd');
    expect(storedSize()['resolutionId']).toBe('qhd');
    expect(ws.chartSize.resolutionId).toBe('qhd');

    q<HTMLDetailsElement>('#cc-export-section')!.open = true;
    await macrotask();
    expect(storedLayout()['chartSizeOpen']).toBe(true);
    q<HTMLDetailsElement>('#cc-image-format-section')!.open = true;
    await macrotask();
    expect(storedLayout()['imageFormatOpen']).toBe(true);

    clickOn('#cc-image-format-reset');
    expect(storedLayout()['imageFormat']).toBe('png');
    expect(storedLayout()['webpQuality']).toBe(85);
  });

  // --- Copy and Download ---

  it('downloads a chart as WebP at the chosen quality, under a .webp name', async () => {
    localStorage.setItem(CC_TIMELINE_STORAGE_KEY, JSON.stringify({ version: 1, imageFormat: 'webp', webpQuality: 90 }));
    await create();
    const calls = stubEncoder(true);
    const saved = captureSaves();

    const button = q<HTMLButtonElement>('.cc-tl-tile[data-figure="quality"] .cc-tl-download')!;
    expect(button.getAttribute('aria-label')).toBe('Download Intelligence per run');
    expect(button.getAttribute('aria-disabled')).toBeNull();
    expect(textOf(q('#cc-tl-tip-download-quality'))).toContain('WebP q90');
    button.click();
    await untilExported();

    expect(calls).toEqual([{ type: 'image/webp', quality: 0.9 }]);
    expect(saved.names).toHaveLength(1);
    expect(saved.names[0]).toMatch(new RegExp(`^chat-consistency_${MODEL_PART}_quality_\\d{8}_\\d{6}\\.webp$`));
    expect(saved.blobs[0].type).toBe('image/webp');
    expect(textOf(q('.cc-tl-status'))).toBe('Downloaded Intelligence per run.');
  }, 20_000);

  it('names a download .png and says so when the browser cannot encode WebP', async () => {
    localStorage.setItem(CC_TIMELINE_STORAGE_KEY, JSON.stringify({ version: 1, imageFormat: 'webp', webpQuality: 90 }));
    await create();
    const calls = stubEncoder(false);
    const saved = captureSaves();

    clickOn('.cc-tl-tile[data-figure="quality"] .cc-tl-download');
    await untilExported();

    expect(calls).toEqual([{ type: 'image/webp', quality: 0.9 }]);
    expect(saved.names).toHaveLength(1);
    expect(saved.names[0]).toMatch(new RegExp(`^chat-consistency_${MODEL_PART}_quality_\\d{8}_\\d{6}\\.png$`));
    expect(textOf(q('.cc-tl-status'))).toBe('Downloaded Intelligence per run. Written as PNG: this browser cannot encode WebP.');
  }, 20_000);

  it('copies a PNG whatever the download format, and reports each clipboard outcome', async () => {
    localStorage.setItem(CC_TIMELINE_STORAGE_KEY, JSON.stringify({ version: 1, imageFormat: 'webp', webpQuality: 90 }));
    await create();
    const calls = stubEncoder(true);
    const written: ClipboardItem[] = [];
    withClipboard({ write: (items: ClipboardItem[]) => { written.push(...items); return Promise.resolve(); } });

    const copy = q<HTMLButtonElement>('.cc-tl-tile[data-figure="quality"] .cc-tl-copy')!;
    expect(copy.getAttribute('aria-label')).toBe('Copy Intelligence per run to the clipboard');
    expect(textOf(q('#cc-tl-tip-copy-quality'))).toBe('Copy as a PNG image');
    copy.click();
    await untilExported();
    expect(calls).toEqual([{ type: 'image/png', quality: undefined }]);
    expect(written).toHaveLength(1);
    expect(written[0].types).toEqual(['image/png']);
    expect(textOf(q('.cc-tl-status'))).toBe('Copied Intelligence per run to the clipboard.');

    withClipboard({ write: () => Promise.reject(new Error('denied')) });
    copy.click();
    await untilExported();
    expect(textOf(q('.cc-tl-status'))).toBe('The clipboard write was refused.');

    withClipboard(undefined);
    copy.click();
    await untilExported();
    expect(textOf(q('.cc-tl-status'))).toBe('This browser cannot copy images to the clipboard — download the chart instead.');
    expect(calls.every(call => call.type === 'image/png')).toBe(true);
  }, 20_000);

  it('downloads all charts as one file when one chart is shown', async () => {
    await create();
    stubEncoder(true);
    const saved = captureSaves();
    const zip = stubZipWriter();
    clickOn('.cc-tl-no-figures');
    clickOn('#cc-tl-show-quality');
    await settle();
    expect(tileKeys()).toEqual(['quality']);
    expect(textOf(q('#cc-tl-tip-download-all'))).toMatch(/^The shown chart — /);

    clickOn('.cc-tl-download-all');
    await untilExported();

    expect(saved.names).toHaveLength(1);
    expect(saved.names[0]).toMatch(new RegExp(`^chat-consistency_${MODEL_PART}_quality_\\d{8}_\\d{6}\\.png$`));
    expect(zip.load).not.toHaveBeenCalled();
    expect(textOf(q('.cc-tl-status'))).toBe('Downloaded Intelligence per run.');
  }, 20_000);

  it('downloads all charts as one ZIP when several are shown, every file under the archive\'s timestamp', async () => {
    await create();
    stubEncoder(true);
    const saved = captureSaves();
    const zip = stubZipWriter();
    const downloadAll = q<HTMLButtonElement>('.cc-tl-download-all')!;
    expect(downloadAll.getAttribute('aria-label')).toBe('Download all charts');
    expect(textOf(q('#cc-tl-tip-download-all'))).toMatch(/^All shown charts as one ZIP — /);

    downloadAll.click();
    await untilExported();

    const drawable = ws.figures.filter(entry => entry.config !== null).map(entry => entry.key);
    expect(drawable.length).toBeGreaterThan(1);
    expect(saved.names).toEqual([expect.stringMatching(new RegExp(`^chat-consistency_${MODEL_PART}_charts_\\d{8}_\\d{6}\\.zip$`))]);
    expect(saved.blobs[0].type).toBe('application/zip');
    expect(zip.names).toHaveLength(1);
    expect(zip.names[0]).toEqual(drawable.map(key =>
      expect.stringMatching(new RegExp(`^chat-consistency_${MODEL_PART}_${key}_\\d{8}_\\d{6}\\.png$`))));
    const stamp = /_(\d{8}_\d{6})\.zip$/.exec(saved.names[0])![1];
    expect(zip.names[0].every(name => name.includes(stamp))).toBe(true);
    expect(textOf(q('.cc-tl-status'))).toContain(`Downloaded ${drawable.length} charts as a ZIP.`);
  }, 30_000);

  it('disables Copy, Download and Download all while the chart size is refused, naming the reason', async () => {
    await create();
    stubEncoder(true);
    const saved = captureSaves();
    const before = boxSize('.cc-tl-tile[data-figure="quality"]');

    openSideTab('download');
    await settle(1);
    setSelect('#cc-export-resolution', 'custom');
    await settle(1);
    const width = q<HTMLInputElement>('#cc-export-width')!;
    width.value = '100';
    width.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();

    const reason = ws.sizeError;
    expect(reason).toContain('320');
    const error = q('.cc-tl-size-error')!;
    expect(error.getAttribute('role')).toBe('alert');
    expect(textOf(error)).toBe(reason);
    expect(q('.cc-tl-tile[data-figure="quality"] .cc-tl-download')!.getAttribute('aria-disabled')).toBe('true');
    expect(q('.cc-tl-tile[data-figure="quality"] .cc-tl-copy')!.getAttribute('aria-disabled')).toBe('true');
    expect(q('.cc-tl-download-all')!.getAttribute('aria-disabled')).toBe('true');
    expect(textOf(q('#cc-tl-tip-download-quality'))).toBe(reason);
    expect(textOf(q('#cc-tl-tip-copy-quality'))).toBe(reason);
    expect(textOf(q('#cc-tl-tip-download-all'))).toBe(reason);

    clickOn('.cc-tl-tile[data-figure="quality"] .cc-tl-download');
    clickOn('.cc-tl-download-all');
    await settle(1);
    expect(saved.names).toEqual([]);
    expect(host.exportingEvents).toEqual([]);
    // Each chart shows the refusal in its image's place, in the words of the export.
    expect(textOf(q('.cc-tl-tile[data-figure="quality"] .cc-figure-refusal'))).toBe(reason);
    expect(q('.cc-tl-tile[data-figure="quality"] canvas')).toBeNull();
    const after = boxSize('.cc-tl-tile[data-figure="quality"]');
    expect(after.height / after.width).toBeCloseTo(before.height / before.width, 1);
  });

  it('emits exportingChange around an export and refuses the export buttons meanwhile', async () => {
    await create();
    stubEncoder(true);
    captureSaves();

    q<HTMLButtonElement>('.cc-tl-tile[data-figure="quality"] .cc-tl-download')!.click();
    fixture.detectChanges();
    expect(ws.exporting).toBe(true);
    expect(host.exportingEvents).toEqual([true]);
    expect(q('.cc-tl-download-all')!.getAttribute('aria-disabled')).toBe('true');
    expect(textOf(q('#cc-tl-tip-download-all'))).toBe('An export is running.');
    expect(q('.cc-tl-tile[data-figure="cost"] .cc-tl-copy')!.getAttribute('aria-disabled')).toBe('true');

    await untilExported();
    expect(host.exportingEvents).toEqual([true, false]);
    expect(q('.cc-tl-download-all')!.getAttribute('aria-disabled')).toBeNull();
  }, 20_000);

  // --- Storage ---

  it('reads the stored layout and chart size field by field', async () => {
    localStorage.setItem(CC_TIMELINE_STORAGE_KEY, JSON.stringify({
      version: 1,
      sidebarCollapsed: true,
      sidebarWidth: 500,
      sidebarTab: 'download',
      view: 'single',
      figures: ['cost', 'quality', 'unknown'],
      hiddenSeries: ['ttfat.proxy', 'work.tools', 'unknown.series'],
      zeroBaseline: true,
      imageTheme: 'print',
      webpQuality: 42,
      singleFigure: 'cost'
    }));
    localStorage.setItem(CC_CHART_SIZE_STORAGE_KEY, JSON.stringify({ version: 1, resolutionId: 'uw1080' }));
    await create();

    expect(q('#cc-tl-sidebar')!.hidden).toBe(true);
    expect(q('.gh-fig-workspace')!.style.getPropertyValue('--gh-fig-sidebar-width')).toBe('500px');
    expect(ws.sidebarTab).toBe('download');
    expect(ws.view).toBe('single');
    const select = q<HTMLSelectElement>('#cc-tl-single-figure')!;
    expect(Array.from(select.options).map(option => option.value)).toEqual(['quality', 'cost']);
    expect(select.value).toBe('cost');
    // The retired `work.tools` series is dropped with the unknown one.
    expect([...ws.hiddenSeries]).toEqual(['ttfat.proxy']);
    expect(ws.zeroBaseline).toBe(true);
    // The retired image theme seeds the figure style.
    expect(ws.figureStyle.appearance.theme).toBe('light');
    // A quality that is not offered falls back to the default.
    expect(ws.webpQuality).toBe(85);
    expect(ws.chartSize.resolutionId).toBe('uw1080');
  });

  it('works with a localStorage that throws on every read and write', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('Storage is blocked.', 'SecurityError');
    });
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('Storage is blocked.', 'SecurityError');
    });
    await create();

    expect(ws.sidebarTab).toBe('data');
    expect(ws.view).toBe('all');
    expect(tileKeys()).toEqual(FIGURE_ORDER);

    clickOn('.cc-tl-sidebar-toggle');
    expect(q('#cc-tl-sidebar')!.hidden).toBe(true);
    expect(setItem).toHaveBeenCalled();
    clickOn('.cc-tl-sidebar-toggle');
    openSideTab('download');
    await settle(1);
    setSelect('#cc-export-resolution', 'uw1080');
    expect(ws.chartSize.resolutionId).toBe('uw1080');
    // The figure style is the default, and a change still applies for the session.
    expect(ws.figureStyle).toEqual(DEFAULT_FIGURE_STYLE);
    changeAppearance({ theme: 'light' });
    expect(ws.figureStyle.appearance.theme).toBe('light');
  });

  // --- Battery runs, the header band and the logo ---

  /** The workspace in the battery set of the fixtures: battery runs #11 and #12 on the timeline. */
  async function createBattery(notAnalyzedUnits: ReadonlyMap<number, CcRunInclusion> | null = null): Promise<void> {
    const timeline = ccEventTimeline({
      batteryPoints: [
        ccBatteryPoint(11, '2026-10-08T06:00:00Z', { overallIndex: 79, memberRunIds: [301, 302] }),
        ccBatteryPoint(12, '2026-10-08T10:00:00Z', { overallIndex: 86, memberRunIds: [303, 304] }),
        ccBatteryPoint(21, '2026-10-08T12:00:00Z', { setKey: `battery:${'d'.repeat(64)}` })
      ]
    });
    fixture = TestBed.createComponent(TimelineWorkspaceHostComponent);
    host = fixture.componentInstance;
    fixture.componentRef.setInput('axis', ccAxis());
    fixture.componentRef.setInput('timeline', timeline);
    fixture.componentRef.setInput('unitKind', 'batteryRun');
    fixture.componentRef.setInput('setKey', CC_BATTERY_SET_KEY);
    fixture.componentRef.setInput('batteryRows', ccBatteryRunRows());
    fixture.componentRef.setInput('notAnalyzedUnits', notAnalyzedUnits);
    fixture.componentRef.setInput('setLabel', 'Two initial suites (revision 1)');
    fixture.componentRef.setInput('setKind', 'battery');
    fixture.detectChanges();
    ws = fixture.debugElement.query(By.directive(CcTimelineWorkspaceComponent)).componentInstance as CcTimelineWorkspaceComponent;
    el = fixture.nativeElement as HTMLElement;
    await settle();
  }

  function plottedIds(key: CcFigureKey): number[] {
    return (figure(key).config!.data.datasets[0].data as { runId: number }[]).map(point => point.runId);
  }

  /** The badges of a chart's composed heading. */
  function badges(key: CcFigureKey): string[] {
    return ccFigureChrome(figure(key), internals().composeContext()).badges.map(badge => badge.text);
  }

  it('plots one point per battery run of the compared set, and member runs on request', async () => {
    await createBattery(new Map<number, CcRunInclusion>([[11, 'leftOut']]));
    expect(plottedIds('quality')).toEqual([11, 12]);
    expect(figure('quality').config!.data.datasets[0].label).toBe('Overall Intelligence Index (battery)');
    expect(figure('quality').table.columns[0]).toBe('Battery run');
    expect(figure('quality').takeaway).toContain('1 battery run not in the analysis is drawn as a gray cross.');
    expect(badges('quality').slice(0, 2)).toEqual(['GPT-5 high', '2 battery runs']);
    expect(internals().composeContext().set).toEqual({ kind: 'battery', label: 'Two initial suites (revision 1)' });
    expect(textOf(q('#cc-tl-fig-quality-data-title'))).toBe('Intelligence per run: data per battery run');

    const radios = qa<HTMLInputElement>('input[name="cc-tl-plot-by"]');
    expect(radios.map(radio => [radio.id, radio.checked])).toEqual([['cc-tl-plot-by-battery', true], ['cc-tl-plot-by-members', false]]);
    clickOn('#cc-tl-plot-by-members');
    expect(plottedIds('quality')).toEqual([201, 202, 203, 204, 205, 206]);
    expect(figure('quality').table.columns[0]).toBe('Run');
    expect(badges('quality')[1]).toBe('6 member runs');
    // Component state, not stored.
    expect(storedLayout()['plotBy']).toBeUndefined();

    clickOn('#cc-tl-plot-by-battery');
    expect(plottedIds('quality')).toEqual([11, 12]);
  });

  it('labels the automatic Intelligence decimals by what the charts plot', async () => {
    await createBattery();
    openSideTab('charts');
    const auto = () => textOf(q<HTMLSelectElement>('#cc-tl-decimals-quality')!.options[0]);
    expect(auto()).toBe('Automatic (1)');
    openSideTab('data');
    clickOn('#cc-tl-plot-by-members');
    openSideTab('charts');
    expect(auto()).toBe('Automatic (0)');
  });

  it('offers no Plot by choice without a battery set, counts runs, and names every suite in the footer', async () => {
    await create();
    expect(q('input[name="cc-tl-plot-by"]')).toBeNull();
    expect(badges('quality').slice(0, 2)).toEqual(['GPT-5 high', '6 runs']);
    expect(internals().composeContext().set).toBeNull();
  });

  it('draws the GnollBench logo on the charts and in every image until the Theme tab turns it off', async () => {
    await create();
    await vi.waitFor(() => {
      if (!internals().composeContext().logo) throw new Error('No logo yet.');
    });
    expect(internals().composeContext().logo!.heightPx).toBe(DEFAULT_FIGURE_STYLE.appearance.logoHeightPx);

    changeAppearance({ logo: false });
    expect(internals().composeContext().logo).toBeNull();
    expect(internals().exportSnapshot().context.logo).toBeNull();
    expect(JSON.parse(localStorage.getItem(CC_FIGURE_STYLE_STORAGE_KEY)!).appearance.logo).toBe(false);
  });

  it('hides the HTML figure caption visually, since the chart draws the title, and puts the takeaway and actions under the chart', async () => {
    await create();
    const tile = q('.cc-tl-tile[data-figure="quality"]')!;
    expect(tile.querySelector('figcaption')!.classList).toContain('visually-hidden');
    expect(textOf(tile.querySelector('.cc-figure-title'))).toBe('Intelligence per run');
    const takeaway = tile.querySelector('.cc-figure-takeaway')!;
    expect(takeaway.classList).not.toContain('visually-hidden');
    expect(takeaway.previousElementSibling!.classList).toContain('cc-chart-box');
    // Copy, Download and Open sit in the figure's footer, after the marker line.
    const actions = tile.querySelector('app-cc-chart-figure .cc-figure-footer .cc-figure-actions .cc-tl-tile-actions')!;
    expect(actions.getAttribute('role')).toBe('group');
    expect(actions.getAttribute('aria-label')).toBe('Actions for Intelligence per run');
    expect(Array.from(actions.querySelectorAll('button')).map(button => button.classList[1])).toEqual(['cc-tl-copy', 'cc-tl-download', 'cc-tl-open']);
    expect(tile.querySelector('.gh-fig-tile-actions')).toBeNull();
    // The data cards' headings sit under the step's h4.
    expect(tile.querySelector('.cc-data-card-title')!.tagName).toBe('H5');
  });

  // --- Theme and Charts, and the screen as the download ---

  it('hosts Model Comparison\'s Theme and Charts panels under the cc-style prefix and its own open-sections key', async () => {
    await create();
    openSideTab('theme');
    const theme = q('#cc-tl-side-panel-theme app-figure-style-panel')!;
    expect(theme).not.toBeNull();
    expect(theme.querySelector('[id^="cc-style-appearance"]')).not.toBeNull();
    expect(theme.querySelector('[id^="mc-style"]')).toBeNull();

    openSideTab('charts');
    const charts = q('#cc-tl-side-panel-charts app-figure-style-panel')!;
    expect(charts).not.toBeNull();
    expect(charts.querySelector('[id^="mc-style"]')).toBeNull();
    // The decimal places follow the panel in the same tab.
    expect(charts.nextElementSibling!.classList).toContain('cc-tl-decimals');
    expect(q('#cc-tl-side-panel-data')).toBeNull();
  });

  it('seeds the figure style once from a stored layout\'s print theme and hidden logo', async () => {
    localStorage.setItem(CC_TIMELINE_STORAGE_KEY, JSON.stringify({ version: 2, imageTheme: 'print', logo: false }));
    await create();
    expect(ws.figureStyle.appearance.theme).toBe('light');
    expect(ws.figureStyle.appearance.logo).toBe(false);
    const stored = JSON.parse(localStorage.getItem(CC_FIGURE_STYLE_STORAGE_KEY)!);
    expect(stored.version).toBe(1);
    expect(stored.appearance.theme).toBe('light');
    expect(Object.keys(stored).sort()).toEqual(['appearance', 'timeline', 'version']);
    fixture.destroy();

    // From then on the figure style is authoritative.
    localStorage.setItem(CC_FIGURE_STYLE_STORAGE_KEY, JSON.stringify({ version: 1, appearance: { ...DEFAULT_FIGURE_STYLE.appearance } }));
    localStorage.setItem(CC_TIMELINE_STORAGE_KEY, JSON.stringify({ version: 2, imageTheme: 'print', logo: false }));
    await create();
    expect(ws.figureStyle.appearance.theme).toBe('dark');
    expect(ws.figureStyle.appearance.logo).toBe(true);
  });

  it('shows the text size on screen: the composition is laid out in a smaller box', async () => {
    await create();
    await desktopHost();
    const first = await untilComposed('quality');
    const layoutWidth = () => internals().screenLayout(figure('quality'), internals().composeContext()).layout!.layoutWidth;
    expect(layoutWidth()).toBeCloseTo(960, 6);

    ws.onChartSizeChange({ ...ws.chartSize, textScalePercent: 140 });
    await settle();
    expect(layoutWidth()).toBeCloseTo(960 / 1.4, 6);
    const second = await untilComposed('quality');
    expect(second).not.toBe(first);
    // The pixels stay, so the image box does too.
    expect(boxSize('.cc-tl-tile[data-figure="quality"]')).toEqual(expectedBox(ws.chartSize, ws.zoom, true));
  }, 30_000);

  it('shows the light theme on screen', async () => {
    await create();
    await desktopHost();
    // The ground in the image's top left corner, inside its padding.
    const ground = (canvas: HTMLCanvasElement) => canvas.getContext('2d')!.getImageData(1, 1, 1, 1).data[0];
    const dark = await untilComposed('quality');
    expect(ground(dark)).toBeLessThan(64);

    changeAppearance({ theme: 'light' });
    await settle();
    await vi.waitFor(() => {
      if (internals().composeContext().theme.name !== 'light') throw new Error('Not light yet.');
    });
    const light = await untilComposed('quality');
    expect(ground(light)).toBeGreaterThan(200);
    expect(JSON.parse(localStorage.getItem(CC_FIGURE_STYLE_STORAGE_KEY)!).appearance.theme).toBe('light');
  }, 30_000);

  it('composes the screen and the download from the same request, apart from the density and the pixels', async () => {
    await create();
    await desktopHost();
    clickOn('#cc-tl-view-tab-single');
    await settle();
    await untilComposed('quality');
    const context = internals().composeContext();
    const screenLayout = internals().screenLayout(figure('quality'), context).layout!;
    const file = internals().fileComposition('quality', internals().exportSnapshot());
    const screen = ccFigureRequest(figure('quality'), context, screenLayout)!;
    const download = ccFigureRequest(file.figure!, internals().exportSnapshot().context, file.layout!)!;

    const { layout: screenBox, ...screenRest } = screen.request;
    const { layout: fileBox, ...fileRest } = download.request;
    expect(screenRest).toEqual(fileRest);
    const shape = (layout: FigureExportLayout) => [layout.layoutWidth, layout.layoutHeight, layout.plotWidth, layout.plotHeight];
    expect(shape(screenBox!)).toEqual(shape(fileBox!));
    // The datasets: Chart.js adds an empty `labels` to a data object it has rendered.
    expect(JSON.stringify(screen.config.data.datasets)).toBe(JSON.stringify(download.config.data.datasets));
    expect(Object.keys(screen.config.options).sort()).toEqual(Object.keys(download.config.options).sort());
    expect([fileBox!.pixelWidth, fileBox!.pixelHeight]).toEqual([ccTargetPixels(ws.chartSize)!.pixelWidth, ccTargetPixels(ws.chartSize)!.pixelHeight]);
  }, 30_000);

  it('shows a bitmap that invites no hover, with the values in Show data', async () => {
    await create();
    await untilComposed('quality');
    const tile = q('.cc-tl-tile[data-figure="quality"]')!;
    expect(tile.querySelector('canvas[basechart]')).toBeNull();
    const image = tile.querySelector<HTMLCanvasElement>('canvas.cc-chart-image')!;
    expect(getComputedStyle(image).cursor).toBe('default');
    expect(figure('quality').config!.options.events).toEqual([]);
    expect(textOf(tile.querySelector('.cc-figure-data > summary'))).toBe('Show data · 6 runs');
    expect(tile.querySelectorAll('article.cc-data-card').length).toBe(6);
  }, 30_000);

  describe('no scrollbar at Fit to screen', () => {
    function expectNoScrollbar(label: string): void {
      const viewport = q<HTMLElement>('.cc-tl-viewport')!;
      expect(viewport.scrollHeight, `${label}: vertical`).toBeLessThanOrEqual(viewport.clientHeight);
      expect(viewport.scrollWidth, `${label}: horizontal`).toBeLessThanOrEqual(viewport.clientWidth);
    }

    it('fits one whole chart in Single chart at every aspect, after a sidebar resize, and with Show data or a long takeaway', async () => {
      await create();
      await desktopHost();
      clickOn('#cc-tl-view-tab-single');
      await settle(8);
      for (const id of ['fullhd', 'square1080', 'uw1080', 'a4p']) {
        ws.onChartSizeChange({ ...ws.chartSize, resolutionId: id });
        await settle(8);
        expect(ws.zoomLabel).toMatch(/ · Fit to screen$/);
        expectNoScrollbar(id);
      }

      ws.onChartSizeChange({ ...ws.chartSize, resolutionId: 'fullhd' });
      await settle(8);
      const resizer = q('app-pane-resizer')!;
      for (let i = 0; i < 4; i++) press(resizer, 'ArrowRight');
      await settle(8);
      expectNoScrollbar('wider sidebar');
      for (let i = 0; i < 8; i++) press(resizer, 'ArrowLeft');
      await settle(8);
      expectNoScrollbar('narrower sidebar');

      const data = q<HTMLDetailsElement>('#cc-tl-view-panel-single details.cc-figure-data')!;
      data.open = true;
      await settle(8);
      data.open = false;
      await settle(8);
      expectNoScrollbar('Show data closed again');

      fixture.componentRef.setInput('notAnalyzed', new Map<number, CcRunInclusion>([[201, 'beforeSpan'], [204, 'leftOut']]));
      fixture.detectChanges();
      await settle(8);
      expect(figure('quality').takeaway).toContain('not in the analysis');
      expectNoScrollbar('long takeaway');
    }, 60_000);

    it('fits the first tile, its image and HTML, into All charts\' scroller', async () => {
      await create();
      await desktopHost();
      expect(ws.zoomLabel).toMatch(/ · Fit to screen$/);
      const viewport = q<HTMLElement>('.cc-tl-viewport')!;
      const style = getComputedStyle(viewport);
      const contentHeight = viewport.clientHeight - Number.parseFloat(style.paddingTop) - Number.parseFloat(style.paddingBottom);
      const tile = q('.cc-tl-tile[data-figure="quality"]')!;
      expect(tile.getBoundingClientRect().height).toBeLessThanOrEqual(contentHeight + 1e-6);
      expect(viewport.scrollWidth).toBeLessThanOrEqual(viewport.clientWidth);
    }, 30_000);
  });

  it('wraps the settings tabs and keeps the event list\'s sticky headings under the measured row', async () => {
    await create();
    const row = q('[role="tablist"][aria-label="Settings sections"]')!;
    expect(row.classList).toContain('gh-tabs-wrap');
    expect(getComputedStyle(row).flexWrap).toBe('wrap');
    openSideTab('events');
    await settle();
    const measured = Math.ceil(row.getBoundingClientRect().height);
    expect(ws.tabBarHeight).toBe(measured);
    expect(q<HTMLElement>('#cc-tl-events')!.style.getPropertyValue('--cc-ev-sticky-top')).toBe(`${measured}px`);
  });
});
