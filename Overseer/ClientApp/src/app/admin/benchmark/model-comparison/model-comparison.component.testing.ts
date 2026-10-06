import type { Mock } from 'vitest';
import { ChangeDetectorRef, Component, EventEmitter, Input, Output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideCharts } from 'ng2-charts';
import {
  ComparisonFigureCard,
  ComparisonWizardStep,
  DOWNLOAD_SETTINGS_STORAGE_KEY,
  FIGURE_SIDEBAR_STORAGE_KEY,
  FIGURE_STYLE_STORAGE_KEY,
  FigureSidebarTab,
  TABLE_COLUMNS_STORAGE_KEY,
  ModelComparisonComponent
} from './model-comparison.component';
import { directLabelPlugin } from './model-comparison-charts';
import type { DirectLabelBlock, DirectLabelPluginOptions } from './model-comparison-charts';
import { APP_CHART_REGISTRABLES } from '../../../chart-registrables';
import { BenchmarkModelComparisonDto, BenchmarkModelComparisonEntryDto } from './model-comparison.models';
import { zipWriterModule } from './figure-export';
import { FIGURE_SIZE_STORAGE_KEY, TABLE_IMAGE_SIZE_STORAGE_KEY } from './figure-size';
import { figureLogoIo, resetFigureLogoCache } from './figure-logo';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { AdminAlertService, SystemAlert } from '../../../services/admin-alert.service';
import { BenchmarkReportPackDocumentProgressDto } from '../../../services/admin-benchmark.service';
import { REPORT_PACK_STORAGE_KEY, ReportPackPanelComponent } from '../report-pack/report-pack-panel.component';
import { REPORT_CHART_STORAGE_KEY } from '../report-pack/report-charts';

// Spec helper for the ModelComparisonComponent spec files, which are split by area and share this
// setup and these helpers. Imported by specs only.

/**
 * Step 3's panel, in place of the real one: its own requests and polling are its own spec's concern,
 * and what the wizard owes it is inputs and handlers.
 */
@Component({
  selector: 'app-report-pack-panel',
  standalone: true,
  template: '<input class="rp-stub-field" aria-label="A form field of step 3">'
})
export class ReportPackPanelStubComponent {
  @Input() context: unknown = null;
  @Input() comparisonId: number | null = null;
  @Input() chartSelection: unknown = null;
  @Input() chartLayout: unknown = null;
  @Input() documentChartsComposer: unknown = null;
  @Input() chartsAvailable: readonly string[] = [];
  @Input() chartAdvisory: string | null = null;
  @Input() chartStorageMissing = false;
  @Input() chartStatus: Readonly<Record<number, unknown>> = {};
  @Input() idPrefix = 'rp';
  @Output() graderGuideRequested = new EventEmitter<void>();
  @Output() documentWritten = new EventEmitter<BenchmarkReportPackDocumentProgressDto>();
  @Output() jobFinished = new EventEmitter<unknown>();
  @Output() documentsRequested = new EventEmitter<void>();
  @Output() documentsChanged = new EventEmitter<void>();
  @Output() chartSelectionChange = new EventEmitter<unknown>();
  @Output() chartLayoutChange = new EventEmitter<unknown>();
  @Output() chartRetryRequested = new EventEmitter<BenchmarkReportPackDocumentProgressDto>();
  @Output() busyChange = new EventEmitter<boolean>();
}

/** The variables a ModelComparisonComponent spec file keeps its fixture in. */
export interface ModelComparisonSpecVariables {
  component: ModelComparisonComponent;
  fixture: ComponentFixture<ModelComparisonComponent>;
  http: HttpTestingController;
  /** The system alerts the wizard reads chart storage from. */
  alerts: BehaviorSubject<SystemAlert[]>;
}

/**
 * The running spec file's variables, read and written through its accessors: the setup below assigns
 * them and the helpers read them, so a spec that replaces its fixture is seen by both.
 */
let mc: ModelComparisonSpecVariables;

/**
 * A comparable entry with plausible measures on all three axes.
 *
 * Every field the server declares is populated, because the view's whole job is to distinguish a
 * measured entry from an unmeasured one and a fixture with holes in it would pass by accident.
 */
export function buildEntry(overrides: Partial<BenchmarkModelComparisonEntryDto> = {}): BenchmarkModelComparisonEntryDto {
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
export function buildExcludedEntry(key: string, keys: string[]): BenchmarkModelComparisonEntryDto {
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

export function buildDto(
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
export function render(dto: BenchmarkModelComparisonDto | null, step: ComparisonWizardStep = 2): void {
  mc.fixture.componentRef.setInput('comparison', dto);
  mc.fixture.detectChanges();
  mc.component.goToStep(step);
  mc.fixture.detectChanges();
}

/** Opens one of step 2's four views through its tab. */
export function showView(view: 'all' | 'single' | 'table' | 'tablePreview'): void {
  (mc.fixture.debugElement.query(By.css(`#mc-fig-tab-${view}`)).nativeElement as HTMLButtonElement).click();
  mc.fixture.detectChanges();
}

/** Step 2 on the Interactive table, where the comparison table lives. */
export function renderTable(dto: BenchmarkModelComparisonDto | null): void {
  render(dto, 2);
  if (mc.component.effectiveFigureTab !== 'table') {
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
export function refresh(): void {
  (mc.component as unknown as { cdr: ChangeDetectorRef }).cdr.detectChanges();
}

export function comparableSet(count: number): BenchmarkModelComparisonEntryDto[] {
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

export function textOf(selector: string): string {
  return mc.fixture.debugElement.queryAll(By.css(selector))
    .map(element => (element.nativeElement as HTMLElement).textContent ?? '')
    .join(' ');
}

/** The footer's Compare / Next / Close button. */
export function nextButton(): HTMLButtonElement {
  return mc.fixture.debugElement.query(By.css('.mc-wizard-next')).nativeElement as HTMLButtonElement;
}

/** The footer's Cancel Comparison button, or null while none is rendered. */
export function cancelCompareButton(): HTMLButtonElement | null {
  return mc.fixture.debugElement.queryAll(By.css('.mc-wizard-nav-actions .btn-gh-cancel'))
    .map(button => button.nativeElement as HTMLButtonElement)
    .find(button => (button.textContent ?? '').includes('Cancel Comparison')) ?? null;
}

/** Every key this wizard remembers per browser. */
const STORED_KEYS = [
  FIGURE_STYLE_STORAGE_KEY, FIGURE_SIDEBAR_STORAGE_KEY, FIGURE_SIZE_STORAGE_KEY, TABLE_IMAGE_SIZE_STORAGE_KEY,
  TABLE_COLUMNS_STORAGE_KEY, DOWNLOAD_SETTINGS_STORAGE_KEY, REPORT_CHART_STORAGE_KEY, REPORT_PACK_STORAGE_KEY
];

/**
 * Configures the TestBed and creates the fixture, with `ReportPackPanelStubComponent` in place of step 3's
 * panel when asked. An overridden component is recompiled before every test, so only the spec file that
 * opens step 3 asks for the stub.
 */
async function createFixture(stubReportPackPanel: boolean): Promise<void> {
  mc.alerts = new BehaviorSubject<SystemAlert[]>([]);
  const testBed = TestBed.configureTestingModule({
    imports: [ModelComparisonComponent],
    // Step 4 lists its documents and step 3 charts them over HTTP.
    providers: [
      provideCharts({ registerables: APP_CHART_REGISTRABLES }), provideHttpClient(), provideHttpClientTesting(),
      { provide: AdminAlertService, useValue: { alerts$: mc.alerts.asObservable() } }
    ]
  });
  if (stubReportPackPanel) {
    testBed.overrideComponent(ModelComparisonComponent, {
      remove: { imports: [ReportPackPanelComponent] },
      add: { imports: [ReportPackPanelStubComponent] }
    });
  }
  await testBed.compileComponents();

  mc.fixture = TestBed.createComponent(ModelComparisonComponent);
  mc.component = mc.fixture.componentInstance;
  mc.http = TestBed.inject(HttpTestingController);
}

/**
 * Registers the hooks every ModelComparisonComponent spec file shares. Called inside the file's outer
 * describe, with accessors for the variables it keeps its fixture in.
 */
export function setUpModelComparisonSpec(
  variables: Partial<ModelComparisonSpecVariables>,
  options: { stubReportPackPanel: boolean }
): void {
  beforeEach(async () => {
    mc = variables as ModelComparisonSpecVariables;
    // The figure style, the sizes, the columns, the formats and the sidebar are remembered per
    // browser, so one spec's must not reach the next.
    STORED_KEYS.forEach(key => localStorage.removeItem(key));
    // No logo loads unless a spec supplies one, so every layout is measured without it.
    resetFigureLogoCache();
    vi.spyOn(figureLogoIo, 'loadImage').mockResolvedValue(null);
    await createFixture(options.stubReportPackPanel);
  });

  afterEach(() => {
    STORED_KEYS.forEach(key => localStorage.removeItem(key));
  });

  // The All tab leaves a debounced composition behind it, and a timer that outlived its test would
  // compose against the next one's fixture.
  afterEach(() => {
    (mc.component as unknown as { detachAll(): void }).detachAll();
  });

  // A shown preview leaves a debounced composition behind it, and a timer that outlived its test
  // would compose against the next one's fixture.
  afterEach(() => {
    (mc.component as unknown as { detachPreview(): void }).detachPreview();
  });

  // `withClipboard` stands in for `navigator.clipboard` on the instance.
  afterEach(() => {
    delete (navigator as unknown as { clipboard?: unknown }).clipboard;
  });
}

/** Selects one tab of the step-3 settings sidebar by clicking it. */
export function openSidebarTab(tab: FigureSidebarTab): void {
  (mc.fixture.debugElement.query(By.css(`#mc-side-tab-${tab}`)).nativeElement as HTMLButtonElement).click();
  mc.fixture.detectChanges();
}

/** The Interactive table's body row whose source line reads `source`. */
export function tableRowOf(source: string): HTMLElement {
  const row = mc.fixture.debugElement.queryAll(By.css('table.mc-table tbody tr'))
    .map(candidate => candidate.nativeElement as HTMLElement)
    .find(candidate => candidate.querySelector('.mc-source')?.textContent?.trim() === source);
  expect(row, `the row of ${source}`).toBeTruthy();
  return row!;
}

/** The three scatters' legend `display`, which the names toggle is what changes. */
export function scatterLegendDisplays(): unknown[] {
  return mc.component.scatterCards.map(card => (card.options?.plugins?.legend as { display?: unknown })?.display);
}

/** The blocks one scatter hands the plugin, or undefined where it does not register it. */
export function scatterBlocks(index = 0): DirectLabelBlock[] | undefined {
  const plugins = mc.component.scatterCards[index].options?.plugins as
    Record<string, DirectLabelPluginOptions> | undefined;
  return plugins?.[directLabelPlugin.id]?.blocks as DirectLabelBlock[] | undefined;
}

/** Opens the sidebar's Charts tab on one family, through the real tabs. */
export function openStyleFamily(kind: 'bar' | 'profile' | 'scatter'): void {
  openSidebarTab('charts');
  (mc.fixture.debugElement.query(By.css(`#mc-style-family-tab-${kind}`)).nativeElement as HTMLButtonElement).click();
  mc.fixture.detectChanges();
}

/** The comparable set with one entry's run total withheld for `reason`. */
export function setWithoutTotal(count: number, reason: string): BenchmarkModelComparisonEntryDto[] {
  const entries = comparableSet(count);
  entries[1] = {
    ...entries[1],
    cost: { ...entries[1].cost!, totalRunCostPerRunUsd: null, totalRunCostSdUsd: null, totalRunCostUnavailableReason: reason }
  };
  return entries;
}

export const PRE_HARNESS_15 = 'Run 2 predates per-role cost tracking (harness 15), so its final synthesis is not counted.';

/** Chooses one option of a Data tab radio group through the real radio. */
export function chooseRadio(id: string): void {
  const radio = mc.fixture.debugElement.query(By.css(`#${id}`));
  expect(radio, id).toBeTruthy();
  (radio.nativeElement as HTMLInputElement).click();
  mc.fixture.detectChanges();
}

/** The Single tab's button in the figure bar. */
export function singleTabButton(): HTMLButtonElement {
  return mc.fixture.debugElement.query(By.css('#mc-fig-tab-single')).nativeElement as HTMLButtonElement;
}

/**
 * Shows the Single tab: on `card` through the eye button on its All tile, or on the last figure
 * activated through the tab itself.
 */
export function openSingle(card?: ComparisonFigureCard): void {
  if (card) {
    const eye = mc.fixture.debugElement.queryAll(By.css('.mc-all-tile .mc-all-open'))
      .map(button => button.nativeElement as HTMLButtonElement)
      .find(button => button.getAttribute('aria-label') === `Open ${card.title} in Single view`);
    expect(eye, `the eye button of ${card.title}`).toBeTruthy();
    eye!.click();
  } else {
    singleTabButton().click();
  }
  refresh();
}

/** The composition the debounce would run, without waiting 150 ms for the timer to fire it. */
export async function composePreview(): Promise<void> {
  await (mc.component as unknown as { renderPreview(): Promise<void> }).renderPreview();
  refresh();
}

/** Runs `act` with a fake clock and lets the style debounce fire. */
export function withStyleDebounce(act: () => void): void {
  vi.useFakeTimers();
  try {
    act();
    vi.advanceTimersByTime(200);
    refresh();
  }
  finally {
    vi.useRealTimers();
  }
}

/**
 * Checks one tab row against the full tab contract: roles, names, selection, a roving tabindex,
 * Left/Right wrapping, Home/End, and focus following selection.
 */
export function expectTabContract(
  listSelector: string,
  listLabel: string,
  idPrefix: string,
  panelPrefix: string,
  labels: string[],
  selected: () => string,
  tabIds: string[] = labels.map(label => label.toLowerCase())
): void {
  const tabs = (): HTMLButtonElement[] =>
    mc.fixture.debugElement.queryAll(By.css(`${listSelector} [role="tab"]`))
      .map(tab => tab.nativeElement as HTMLButtonElement);
  const ids = tabIds;

  const tablist = mc.fixture.debugElement.query(By.css(listSelector)).nativeElement as HTMLElement;
  expect(tablist.getAttribute('role')).toBe('tablist');
  expect(tablist.getAttribute('aria-label')).toBe(listLabel);
  expect(tabs().map(tab => tab.textContent!.trim())).toEqual(labels);

  tabs().forEach((tab, index) => {
    expect(tab.id).toBe(`${idPrefix}${ids[index]}`);
    expect(tab.getAttribute('aria-controls')).toBe(`${panelPrefix}${ids[index]}`);
    expect(tab.getAttribute('aria-selected')).toBe(index === 0 ? 'true' : 'false');
    expect(tab.getAttribute('tabindex')).toBe(index === 0 ? '0' : '-1');
  });
  const firstPanel = mc.fixture.debugElement.query(By.css(`#${panelPrefix}${ids[0]}`)).nativeElement as HTMLElement;
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
  expect(selected(), 'Left wraps to the last tab').toBe(ids[last]);
  press(last, 'ArrowRight');
  expect(selected(), 'Right wraps to the first tab').toBe(ids[0]);
  press(0, 'End');
  expect(selected()).toBe(ids[last]);
  expect(document.activeElement).toBe(tabs()[last]);
}

/** One of the zoom group's controls, by its accessible name. */
export function zoomButton(name: string): HTMLButtonElement {
  return mc.fixture.debugElement.query(By.css(`.mc-preview-zoom button[aria-label="${name}"]`))
    .nativeElement as HTMLButtonElement;
}

/**
 * Intercepts the save path rather than the module that performs it: the object URL names the
 * blob that was written and the anchor names the file it was written under, which between them
 * are everything a download can be asserted on without a real file system.
 */
export function captureSaves(): {
  blobs: Blob[];
  names: string[];
} {
  const saved: {
    blobs: Blob[];
    names: string[];
  } = { blobs: [], names: [] };
  vi.spyOn(URL, 'createObjectURL').mockImplementation((source: Blob | MediaSource) => {
    saved.blobs.push(source as Blob);
    return 'blob:model-comparison-test';
  });
  vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {
  });
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    saved.names.push(this.download);
  });
  return saved;
}

/** `navigator.clipboard` is a prototype getter, so it is stood in for on the instance. */
export function withClipboard(value: unknown): void {
  Object.defineProperty(navigator, 'clipboard', { value, configurable: true });
}

/**
 * A stand-in for the dynamically imported zip writer, which the specs never really run.
 *
 * The recorder holds the entry names of every archive it was asked to pack, which is what a
 * batch export can be asserted on without decoding one.
 */
export function stubZipWriter(): {
  names: string[][];
  load: Mock;
} {
  const names: string[][] = [];
  const load = vi.spyOn(zipWriterModule, 'load').mockResolvedValue({
    zipSync: (data: Record<string, unknown>) => {
      names.push(Object.keys(data));
      // An empty archive's end-of-central-directory record: a valid zip, and nothing in it.
      return new Uint8Array([0x50, 0x4b, 0x05, 0x06]);
    }
  } as any);
  return { names, load };
}

/** Lets the All tab attach, which runs in a microtask outside the check pass that found it. */
export async function settleAllTab(): Promise<void> {
  await Promise.resolve();
  mc.fixture.detectChanges();
}
