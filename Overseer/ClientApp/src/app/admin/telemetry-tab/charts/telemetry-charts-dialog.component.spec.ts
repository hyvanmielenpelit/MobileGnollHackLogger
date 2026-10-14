import { ChangeDetectorRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import {
  AdminService,
  AiTelemetryPointDto,
  AiTelemetrySeriesDto,
  AiTelemetrySeriesLineDto,
  AiTelemetrySeriesQuery
} from '../../../services/admin.service';
import { ZipWriterModule, zipWriterModule } from '../../benchmark/model-comparison/figure-export';
import { FIGURE_STYLE_PANEL_OPEN_KEY } from '../../benchmark/model-comparison/figure-style-panel.component';
import { TELEMETRY_FIGURE_KEYS } from './telemetry-charts';
import {
  TL_CHARTS_DOWNLOAD_STORAGE_KEY,
  TL_CHARTS_FETCH_DEBOUNCE_MS,
  TL_CHARTS_FIGURE_STYLE_PANEL_OPEN_KEY,
  TL_CHARTS_FIGURE_STYLE_STORAGE_KEY,
  TL_CHARTS_SIZE_STORAGE_KEY,
  TL_CHARTS_STORAGE_KEY,
  TelemetryChartsDialogComponent,
  TelemetryChartsOpenRequest
} from './telemetry-charts-dialog.component';

/** Every storage key the dialog reads or writes, cleared around each test. */
const STORAGE_KEYS = [
  TL_CHARTS_STORAGE_KEY, TL_CHARTS_SIZE_STORAGE_KEY, TL_CHARTS_FIGURE_STYLE_STORAGE_KEY,
  TL_CHARTS_FIGURE_STYLE_PANEL_OPEN_KEY, TL_CHARTS_DOWNLOAD_STORAGE_KEY, FIGURE_STYLE_PANEL_OPEN_KEY
];

function clearStorage(): void {
  for (const key of STORAGE_KEYS) {
    try {
      localStorage.removeItem(key);
    } catch {
      // Nothing stored.
    }
  }
}

/** A small chart size at 100 % density, so every composition stays quick. */
function storeSmallChartSize(): void {
  localStorage.setItem(TL_CHARTS_SIZE_STORAGE_KEY, JSON.stringify({
    version: 1, resolutionId: 'custom', customWidthPx: 640, customHeightPx: 400,
    densitySelection: 1, customDensityPercent: 100, textScalePercent: 100
  }));
}

function point(overrides: Partial<AiTelemetryPointDto> = {}): AiTelemetryPointDto {
  return {
    requests: 2, promptTokens: 400, cacheReadTokens: 100, outputTokens: 50, estimatedCost: 0.2, pricedRequests: 2,
    durationSumMs: 3000, durationCount: 2, validAttempts: 2, providerErrors: 0, ...overrides
  };
}

function line(key: string, label: string, overrides: Partial<AiTelemetrySeriesLineDto> = {}): AiTelemetrySeriesLineDto {
  return { key, label, provider: null, isOther: false, points: [point(), point({ requests: 3 }), point()], ...overrides };
}

/** Three days, one line, activity counted from the second day. */
function seriesDto(overrides: Partial<AiTelemetrySeriesDto> = {}): AiTelemetrySeriesDto {
  const activity = {
    activeUsers: 2, newUsers: 1, chatsCreated: 3, confidentialChatsCreated: 1, gnollHackChatsCreated: 1,
    incognitoChatsCreated: 1, userMessages: 9, assistantMessages: 9, incognitoUserMessages: 2
  };
  return {
    bucket: 'day',
    splitBy: 'none',
    bucketStartsUtc: ['2026-10-01T00:00:00Z', '2026-10-02T00:00:00Z', '2026-10-03T00:00:00Z'],
    series: [line('all', 'All')],
    costRecordedSinceUtc: '2026-09-01T00:00:00Z',
    attemptsRecordedSinceUtc: '2026-09-01T00:00:00Z',
    userId: null,
    userName: null,
    activity: [activity, activity, activity],
    activityCountedSinceUtc: '2026-10-02',
    ...overrides
  };
}

const REQUEST: TelemetryChartsOpenRequest = { start: '2026-10-01', end: '2026-10-03', keys: 'system', roles: ['chat', 'title'] };

describe('TelemetryChartsDialogComponent', () => {
  let fixture: ComponentFixture<TelemetryChartsDialogComponent>;
  let component: TelemetryChartsDialogComponent;
  let el: HTMLElement;
  let requests: { query: AiTelemetrySeriesQuery; subject: Subject<AiTelemetrySeriesDto> }[];

  beforeEach(async () => {
    clearStorage();
    storeSmallChartSize();
    requests = [];
    const admin = {
      getAiTelemetrySeries: (query: AiTelemetrySeriesQuery) => {
        const subject = new Subject<AiTelemetrySeriesDto>();
        requests.push({ query, subject });
        return subject.asObservable();
      }
    };
    await TestBed.configureTestingModule({
      imports: [TelemetryChartsDialogComponent],
      providers: [{ provide: AdminService, useValue: admin }]
    }).compileComponents();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    fixture?.destroy();
    clearStorage();
  });

  // --- Helpers ---

  function create(): void {
    fixture = TestBed.createComponent(TelemetryChartsDialogComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  }

  /** The component is OnPush: a change made outside its handlers needs a mark before a check. */
  function render(): void {
    (component as unknown as { cdr: ChangeDetectorRef }).cdr.markForCheck();
    fixture.detectChanges();
  }

  function open(request: TelemetryChartsOpenRequest = REQUEST): void {
    component.open(request);
    fixture.detectChanges();
  }

  function respond(index: number, dto: AiTelemetrySeriesDto = seriesDto()): void {
    requests[index].subject.next(dto);
    requests[index].subject.complete();
    fixture.detectChanges();
  }

  function q<T extends Element = HTMLElement>(selector: string): T | null {
    return el.querySelector<T>(selector);
  }

  function qa<T extends Element = HTMLElement>(selector: string): T[] {
    return Array.from(el.querySelectorAll<T>(selector));
  }

  function text(element: Element | null): string {
    return (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function press(target: Element, key: string): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
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

  function optionValues(selector: string): string[] {
    return qa<HTMLOptionElement>(`${selector} option`).map(option => option.value);
  }

  function stored(key: string): Record<string, unknown> {
    return JSON.parse(localStorage.getItem(key) ?? 'null') as Record<string, unknown>;
  }

  function dialog(): HTMLDialogElement {
    return q<HTMLDialogElement>('dialog.tl-charts-dialog')!;
  }

  async function untilExported(): Promise<void> {
    await vi.waitFor(() => {
      if (component.exporting) throw new Error('Still exporting.');
    }, { timeout: 15_000, interval: 20 });
    fixture.detectChanges();
  }

  /** Stands in for the canvas encoder `encodeFigureImage` reaches through `toBlob`. */
  function stubEncoder(): void {
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation((callback: BlobCallback, type?: string) => {
      callback(new Blob(['image'], { type: type ?? 'image/png' }));
    });
  }

  /** The save path of `saveFigureBlob`: the blob through its object URL, the name through the anchor. */
  function captureSaves(): string[] {
    const names: string[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation(() => 'blob:telemetry-test');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      names.push(this.download);
    });
    return names;
  }

  // --- Opening ---

  it('opens modal on the request\'s range, keys and roles, fetches at once and focuses the title', () => {
    create();
    expect(dialog().open).toBe(false);
    open({ ...REQUEST, keys: 'user', roles: ['chat', 'benchmark'] });

    expect(dialog().open).toBe(true);
    expect(dialog().getAttribute('aria-labelledby')).toBe('tl-charts-title');
    expect(document.activeElement?.id).toBe('tl-charts-title');
    expect(text(q('#tl-charts-title'))).toBe('AI Telemetry Charts');
    expect(text(q('.tl-charts-range'))).toBe('2026-10-01 to 2026-10-03');
    expect(requests.length).toBe(1);
    expect(requests[0].query).toEqual({
      startDate: '2026-10-01', endDate: '2026-10-03', keys: 'user', roles: ['chat', 'benchmark'], bucket: 'auto', splitBy: 'none'
    });
    expect(q<HTMLSelectElement>('#tl-charts-keys')!.value).toBe('user');
    expect(['chat', 'title', 'benchmark', 'other'].map(role => q<HTMLInputElement>(`#tl-charts-role-${role}`)!.checked))
      .toEqual([true, false, true, false]);
    expect(q('.tl-charts-loading')).not.toBeNull();
    respond(0);
    expect(q('.tl-charts-loading')).toBeNull();
  });

  it('shows the empty state while no line holds a request, and the bucket guard\'s refusal inline', () => {
    create();
    open();
    respond(0, seriesDto({ series: [] }));
    expect(text(q('.tl-charts-empty'))).toBe('No AI usage in this range for the selected keys and roles.');

    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    component.setBucket('week');
    vi.advanceTimersByTime(TL_CHARTS_FETCH_DEBOUNCE_MS);
    fixture.detectChanges();
    const message = 'The range has 1,200 buckets; at most 1,100 are drawn. Choose a larger bucket.';
    requests[requests.length - 1].subject.error({ status: 400, error: { message } });
    fixture.detectChanges();
    expect(text(q('.tl-charts-state[role="alert"]'))).toBe(message);
    expect(text(q('#tl-charts-bucket-error'))).toBe(message);
    expect(q('#tl-charts-bucket')!.getAttribute('aria-describedby')).toBe('tl-charts-bucket-error');
  });

  // --- Tabs ---

  it('gives the settings tabs and the view tabs the tab ARIA and keyboard model', () => {
    create();
    open();
    respond(0);
    const tabs = qa<HTMLButtonElement>('[role="tablist"][aria-label="Settings sections"] [role="tab"]');
    expect(tabs.map(tab => text(tab))).toEqual(['Data', 'Theme', 'Charts', 'Download']);
    expect(tabs.map(tab => tab.getAttribute('aria-selected'))).toEqual(['true', 'false', 'false', 'false']);
    expect(tabs.map(tab => tab.getAttribute('tabindex'))).toEqual(['0', '-1', '-1', '-1']);
    expect(tabs.map(tab => tab.getAttribute('aria-controls')))
      .toEqual(['tl-charts-side-panel-data', 'tl-charts-side-panel-theme', 'tl-charts-side-panel-charts', 'tl-charts-side-panel-download']);
    expect(q('#tl-charts-side-panel-data')!.getAttribute('aria-labelledby')).toBe('tl-charts-side-tab-data');
    expect(q('#tl-charts-side-panel-data')!.getAttribute('role')).toBe('tabpanel');

    const right = press(q('#tl-charts-side-tab-data')!, 'ArrowRight');
    expect(right.defaultPrevented).toBe(true);
    expect(document.activeElement?.id).toBe('tl-charts-side-tab-theme');
    expect(q('#tl-charts-side-panel-theme')).not.toBeNull();
    expect(q('#tl-charts-side-panel-data')).toBeNull();

    press(q('#tl-charts-side-tab-theme')!, 'End');
    expect(document.activeElement?.id).toBe('tl-charts-side-tab-download');
    expect(q('#tl-export-section')).not.toBeNull();
    expect(q('#tl-image-format-section')).not.toBeNull();
    press(q('#tl-charts-side-tab-download')!, 'ArrowRight');
    expect(document.activeElement?.id).toBe('tl-charts-side-tab-data');
    press(q('#tl-charts-side-tab-data')!, 'ArrowLeft');
    expect(document.activeElement?.id).toBe('tl-charts-side-tab-download');
    press(q('#tl-charts-side-tab-download')!, 'Home');
    expect(document.activeElement?.id).toBe('tl-charts-side-tab-data');
    expect(press(q('#tl-charts-side-tab-data')!, 'Enter').defaultPrevented).toBe(false);

    const views = qa<HTMLButtonElement>('[role="tablist"][aria-label="Chart views"] [role="tab"]');
    expect(views.map(tab => text(tab))).toEqual(['All charts', 'Single chart']);
    expect(views.map(tab => tab.getAttribute('aria-selected'))).toEqual(['true', 'false']);
    press(views[0], 'ArrowRight');
    expect(document.activeElement?.id).toBe('tl-charts-view-tab-single');
    expect(q('#tl-charts-view-panel-single')!.getAttribute('aria-labelledby')).toBe('tl-charts-view-tab-single');
    expect(q('#tl-charts-view-panel-all')).toBeNull();
  });

  it('leaves the Chat-Consistency-only style controls out of the Charts tab', () => {
    create();
    open();
    respond(0);
    q<HTMLButtonElement>('#tl-charts-side-tab-charts')!.click();
    fixture.detectChanges();
    expect(q('#tl-style-timeline-section-notes')).toBeNull();
    expect(q('#tl-style-timeline-markerTagSizePx')).toBeNull();
    expect(q('#tl-style-timeline-betterBadgePlacement-fit')).toBeNull();
    expect(qa<HTMLInputElement>('#tl-style-timeline-section-heading .fsp-show-grid input').map(input => input.id))
      .toEqual(['tl-style-timeline-badge-dates']);
    expect(q('#tl-style-timeline-legendTextSizePx')).not.toBeNull();
  });

  // --- Data ---

  it('refetches once after the debounce, and drops a stale response', () => {
    create();
    open();
    respond(0);
    const first = component.dto;
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });

    setSelect('#tl-charts-bucket', 'week');
    setSelect('#tl-charts-split', 'role');
    expect(requests.length).toBe(1);
    vi.advanceTimersByTime(TL_CHARTS_FETCH_DEBOUNCE_MS - 1);
    expect(requests.length).toBe(1);
    vi.advanceTimersByTime(1);
    expect(requests.length).toBe(2);
    expect(requests[1].query).toEqual(expect.objectContaining({ bucket: 'week', splitBy: 'role', keys: 'system', roles: ['chat', 'title'] }));

    setSelect('#tl-charts-split', 'provider');
    vi.advanceTimersByTime(TL_CHARTS_FETCH_DEBOUNCE_MS);
    expect(requests.length).toBe(3);

    // The second request answers last but one: its response is stale and changes nothing.
    respond(1, seriesDto({ bucket: 'week', splitBy: 'role' }));
    expect(component.dto).toBe(first);
    respond(2, seriesDto({ bucket: 'week', splitBy: 'provider' }));
    expect(component.dto?.splitBy).toBe('provider');
    expect(requests.length).toBe(3);
  });

  it('offers the User split only with user keys, and leaves it when the keys exclude them', () => {
    create();
    open({ ...REQUEST, keys: 'system' });
    respond(0);
    expect(optionValues('#tl-charts-split')).toEqual(['none', 'role', 'provider', 'model', 'keySource']);

    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    setSelect('#tl-charts-keys', 'user');
    expect(optionValues('#tl-charts-split')).toEqual(['none', 'role', 'provider', 'model', 'keySource', 'user']);
    setSelect('#tl-charts-split', 'user');
    setSelect('#tl-charts-keys', 'system');
    expect(component.splitBy).toBe('none');
    expect(optionValues('#tl-charts-split')).not.toContain('user');
    vi.advanceTimersByTime(TL_CHARTS_FETCH_DEBOUNCE_MS);
    expect(requests[requests.length - 1].query).toEqual(expect.objectContaining({ keys: 'system', splitBy: 'none' }));
  });

  it('keeps at least one role selected', () => {
    create();
    open({ ...REQUEST, roles: ['chat'] });
    respond(0);
    const chat = q<HTMLInputElement>('#tl-charts-role-chat')!;
    chat.click();
    fixture.detectChanges();
    expect(chat.checked).toBe(true);
    expect(component.roles).toEqual(['chat']);
  });

  it('opened for a user, shows the chip, sends the user, and refetches without it once the chip is removed', () => {
    create();
    open({ ...REQUEST, userId: 'u-17', userName: 'alice' });
    expect(text(q('#tl-charts-title'))).toBe('AI Telemetry Charts — alice');
    expect(requests[0].query.userId).toBe('u-17');
    respond(0, seriesDto({ userId: 'u-17', userName: 'alice' }));
    const chip = q<HTMLButtonElement>('.tl-charts-user-chip')!;
    // Angular strips the whitespace between the chip's two spans; the gap is styling.
    expect(text(chip.querySelector('.gh-filter-chip-facet')!)).toBe('User:');
    expect(text(chip.querySelector('.gh-filter-chip-value')!)).toBe('alice');
    expect(chip.getAttribute('aria-label')).toBe('Remove user filter: alice, and show every user');

    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    chip.click();
    fixture.detectChanges();
    expect(q('.tl-charts-user-chip')).toBeNull();
    expect(text(q('#tl-charts-title'))).toBe('AI Telemetry Charts');
    expect(document.activeElement?.id).toBe('tl-charts-side-panel-data');
    vi.advanceTimersByTime(TL_CHARTS_FETCH_DEBOUNCE_MS);
    expect(requests.length).toBe(2);
    expect(requests[1].query.userId).toBeUndefined();
    // The user filter is not stored.
    expect(JSON.stringify(stored(TL_CHARTS_STORAGE_KEY))).not.toContain('u-17');
  });

  // --- Views ---

  it('shows the nine charts as tiles in All charts', () => {
    create();
    open();
    respond(0);
    const tiles = qa('#tl-charts-view-panel-all .tl-charts-tile');
    expect(tiles.map(tile => tile.getAttribute('data-figure'))).toEqual([...TELEMETRY_FIGURE_KEYS]);
    const actions = qa<HTMLButtonElement>('.tl-charts-tile[data-figure="cost"] .gh-fig-tile-actions button');
    expect(actions.map(button => button.getAttribute('aria-label')))
      .toEqual(['Open Estimated cost (USD) in Single view', 'Copy Estimated cost (USD) to the clipboard', 'Download Estimated cost (USD)']);
    for (const button of actions) {
      const tip = button.getAttribute('interestfor');
      expect(tip, button.getAttribute('aria-label')!).not.toBeNull();
      expect(q(`#${tip}`)).not.toBeNull();
      expect(button.hasAttribute('title')).toBe(false);
    }
  });

  it('opens a tile in Single chart on that chart', () => {
    create();
    open();
    respond(0);
    q<HTMLButtonElement>('.tl-charts-tile[data-figure="tokens"] .tl-charts-open')!.click();
    fixture.detectChanges();
    expect(q('#tl-charts-view-panel-single')).not.toBeNull();
    expect(q<HTMLSelectElement>('#tl-charts-single-figure')!.value).toBe('tokens');
    expect(q('.tl-charts-single')!.getAttribute('data-figure')).toBe('tokens');
    expect(document.activeElement?.id).toBe('tl-charts-single-figure');
    expect(q('.tl-charts-fit-screen')!.getAttribute('aria-pressed')).toBe('true');
  });

  // --- Closing ---

  it('closes on Escape, but not while an export runs', () => {
    create();
    open();
    respond(0);
    component.exporting = true;
    render();
    expect(dialog().getAttribute('closedby')).toBe('none');
    const refused = new Event('cancel', { cancelable: true });
    dialog().dispatchEvent(refused);
    fixture.detectChanges();
    expect(refused.defaultPrevented).toBe(true);
    expect(dialog().open).toBe(true);

    component.exporting = false;
    render();
    expect(dialog().hasAttribute('closedby')).toBe(false);
    const cancel = new Event('cancel', { cancelable: true });
    dialog().dispatchEvent(cancel);
    fixture.detectChanges();
    expect(cancel.defaultPrevented).toBe(true);
    expect(dialog().open).toBe(false);
  });

  // --- Storage ---

  it('stores the layout, the data settings, the download settings and the style, and reads them back', () => {
    create();
    open();
    respond(0);
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    q<HTMLButtonElement>('#tl-charts-view-tab-single')!.click();
    fixture.detectChanges();
    setSelect('#tl-charts-bucket', 'month');
    q<HTMLInputElement>('#tl-charts-cumulative')!.click();
    fixture.detectChanges();
    q<HTMLButtonElement>('#tl-charts-side-tab-download')!.click();
    fixture.detectChanges();
    component.onImageFormatChange('webp');
    component.onWebpQualityChange(90);
    component.onFigureStyleChange({ ...component.figureStyle, timeline: { ...component.figureStyle.timeline, lineWidthPx: 3 } });
    q<HTMLButtonElement>('.tl-charts-sidebar-toggle')!.click();
    fixture.detectChanges();

    const layout = stored(TL_CHARTS_STORAGE_KEY);
    expect(layout).toEqual(expect.objectContaining({
      version: 1, collapsed: true, tab: 'download', view: 'single', bucket: 'month', splitBy: 'none', cumulative: true
    }));
    expect(stored(TL_CHARTS_DOWNLOAD_STORAGE_KEY)).toEqual(expect.objectContaining({ version: 1, format: 'webp', webpQuality: 90 }));
    expect(stored(TL_CHARTS_FIGURE_STYLE_STORAGE_KEY)['version']).toBe(1);

    fixture.destroy();
    vi.useRealTimers();
    create();
    expect(component.sidebarCollapsed).toBe(true);
    expect(component.sidebarTab).toBe('download');
    expect(component.view).toBe('single');
    expect(component.bucket).toBe('month');
    expect(component.cumulative).toBe(true);
    expect(component.imageFormat).toBe('webp');
    expect(component.webpQuality).toBe(90);
    expect(component.figureStyle.timeline.lineWidthPx).toBe(3);
    expect(component.chartSize.customWidthPx).toBe(640);
  });

  it('starts without the logo and with defaults where nothing usable is stored', () => {
    localStorage.setItem(TL_CHARTS_STORAGE_KEY, '{ not json');
    create();
    expect(component.figureStyle.appearance.logo).toBe(false);
    expect(component.sidebarTab).toBe('data');
    expect(component.view).toBe('all');
    expect(component.bucket).toBe('auto');
    expect(component.sidebarWidth).toBe(416);
  });

  // --- Download ---

  it('downloads one chart under its file name, and every chart as one ZIP', async () => {
    create();
    open();
    respond(0);
    stubEncoder();
    const names = captureSaves();

    q<HTMLButtonElement>('.tl-charts-tile[data-figure="requests"] .tl-charts-download')!.click();
    await untilExported();
    expect(names).toEqual([expect.stringMatching(/^ai-telemetry_requests_\d{8}_\d{6}\.png$/)]);
    expect(text(q('.tl-charts-status'))).toBe('Downloaded Requests.');

    const archives: string[][] = [];
    const writer: ZipWriterModule = {
      zipSync: (data: Record<string, unknown>) => {
        archives.push(Object.keys(data));
        return new Uint8Array([0x50, 0x4b, 0x05, 0x06]);
      }
    };
    vi.spyOn(zipWriterModule, 'load').mockResolvedValue(writer);
    q<HTMLButtonElement>('#tl-charts-view-panel-all .tl-charts-download-all')!.click();
    await untilExported();
    expect(names[1]).toMatch(/^ai-telemetry_charts_\d{8}_\d{6}\.zip$/);
    expect(archives[0].length).toBe(9);
    expect(archives[0].every(name => /^ai-telemetry_[A-Za-z]+_\d{8}_\d{6}\.png$/.test(name))).toBe(true);
    expect(text(q('.tl-charts-status'))).toBe('Downloaded 9 charts as a ZIP.');
  });
});
