import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';

import {
  BenchmarkReportAudience,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPackAudienceEstimateDto,
  BenchmarkReportPackCallDto,
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkReportPackJobDto,
  BenchmarkReportPackPreviewDto,
  BenchmarkReportPackValidationIssueDto,
  BenchmarkReportPackWrittenDocumentDto,
  BenchmarkReportScope
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import { ModelMultiPickerComponent } from '../../../shared/model-picker/model-multi-picker.component';
import type { PdfViewerRequest } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import type { BenchmarkModelComparisonEntryDto } from '../model-comparison/model-comparison.models';
import { RunReportFrameComponent } from '../run-report-frame/run-report-frame.component';
import { ComparisonDocumentsStatusComponent } from './comparison-documents-status.component';
import type { ComposedDocumentCharts, DocumentChartsComposer } from './report-chart-layout-preview';
import { ReportChartPickerComponent } from './report-chart-picker.component';
import {
  DEFAULT_CHART_LAYOUT_SETTINGS,
  DEFAULT_CHART_SELECTION,
  REPORT_CHART_FIGURES,
  REPORT_CHART_NOT_DRAWN,
  ReportChartLayoutSettings,
  ReportChartRowStatus,
  ReportChartSelection
} from './report-charts';
import { reportPackIo } from './report-pack-diagnostics';
import {
  REPORT_PACK_ALL_WRITTEN_REASON,
  REPORT_PACK_JOB_RUNNING_REASON,
  REPORT_PACK_KEEP_OPEN_TEXT,
  REPORT_PACK_LEAD_TEXT,
  REPORT_PACK_MODEL_SCOPE_TIP,
  REPORT_PACK_PEERLESS_REFUSAL,
  REPORT_PACK_POLL_MS,
  REPORT_PACK_PREVIEW_DEBOUNCE_MS,
  REPORT_PACK_STORAGE_KEY,
  REPORT_PACK_UNEVEN_MODELS_REASON,
  ReportPackContext,
  ReportPackPanelComponent
} from './report-pack-panel.component';

const { ExecutiveSummary, RootCauseAnalysis } = BenchmarkReportAudience;
const ALL_AUDIENCES = [ExecutiveSummary, RootCauseAnalysis];

const SYSTEM_CONFIGS_URL = '/api/admin/systemconfigs';
const DOCUMENTS_URL = '/api/admin/benchmark/report-documents';
const ACTIVE_JOB_URL = '/api/admin/benchmark/report-packs/jobs/active';
const PREVIEW_URL = '/api/admin/benchmark/report-packs/preview';
const START_URL = '/api/admin/benchmark/report-packs';
const LAYOUT_PREVIEW_URL = '/api/admin/benchmark/report-packs/layout-preview';
const jobUrl = (id: string): string => `/api/admin/benchmark/report-packs/jobs/${id}`;

function entry(key: string, label: string, index: number | null, overrides: Partial<BenchmarkModelComparisonEntryDto> = {}): BenchmarkModelComparisonEntryDto {
  const [kind, id] = key.split(':');
  return {
    key,
    sourceKind: kind === 'group' ? 'Group' : kind === 'battery' ? 'Battery' : 'Run',
    sourceId: Number(id),
    label,
    provider: 'Google',
    modelId: label.toLowerCase().replace(/\s+/g, '-'),
    modelDisplayName: label,
    thinkingLevel: 'medium',
    reasoningMode: null,
    state: 'Comparable',
    comparable: true,
    excluded: false,
    speedDegraded: false,
    costDegraded: false,
    quality: (index === null ? null : { pointEstimate: index }) as BenchmarkModelComparisonEntryDto['quality'],
    ...overrides
  } as BenchmarkModelComparisonEntryDto;
}

const ENTRIES: BenchmarkModelComparisonEntryDto[] = [
  entry('run:1', 'Gemini Flash', 70),
  entry('run:2', 'Claude Opus', 60, { provider: 'Anthropic', state: 'Degraded', comparable: false, speedDegraded: true }),
  entry('run:3', 'Old GPT', 40, { provider: 'OpenAI', state: 'Excluded', comparable: false, excluded: true }),
  entry('group:4', 'GPT Sol group', 65, { provider: 'OpenAI' })
];

const OFFERED = ['run:1', 'run:2', 'group:4'];

/** The models line's last sentence for CONTEXT, whose Old GPT is Excluded. */
const EXCLUDED_ONE = '1 entry of this comparison is Excluded and is not offered.';

const CONTEXT: ReportPackContext = {
  runIds: [1, 2, 3],
  groupIds: [4],
  pricingBasis: 'Current',
  entries: ENTRIES,
  entryKeys: ['run:1', 'run:2', 'run:3', 'group:4'],
  suiteId: 5,
  suiteName: 'Board Suite'
};

function config(id: number, displayName: string, provider: string, overrides: Partial<SystemAiConfigDto> = {}): SystemAiConfigDto {
  return {
    id,
    displayName,
    provider,
    modelId: `model-${id}`,
    thinkingLevel: 'medium',
    reasoningMode: null,
    modelRole: 4,
    isEnabled: true,
    hasApiKey: true,
    ...overrides
  } as SystemAiConfigDto;
}

const CONFIGS: SystemAiConfigDto[] = [
  config(7, 'Claude Opus writer', 'Anthropic'),
  config(8, 'GPT Sol writer', 'OpenAI', { modelRole: 5 }),
  config(9, 'Chat only', 'OpenAI', { modelRole: 1 }),
  config(10, 'Disabled writer', 'OpenAI', { isEnabled: false })
];

function estimate(audience: BenchmarkReportAudience, cost: number | null, subjectKey = 'comparison:12', share = 0.1): BenchmarkReportPackAudienceEstimateDto {
  return { audience, promptChars: 20000, estimatedInputTokens: 6000, estimatedOutputTokens: 1500, estimatedCostUsd: cost, subjectKey, contextWindowShare: share };
}

const COMPARISON_ESTIMATES = [estimate(ExecutiveSummary, 0.05), estimate(RootCauseAnalysis, 0.16)];

function writtenDoc(audience: BenchmarkReportAudience, documentId: number, subjectKey = 'comparison:12'): BenchmarkReportPackWrittenDocumentDto {
  return {
    audience,
    documentId,
    createdAtUtc: '2026-10-05T14:30:00Z',
    writerDisplayName: 'Claude Opus writer',
    writerProvider: 'Anthropic',
    writerModelId: 'model-7',
    writerThinkingLevel: 'medium',
    subjectKey,
    status: 'Completed',
    durationMs: 65000,
    costUsd: 0.04
  };
}

/** A comparison-scope preview over every model. */
function comparisonPreview(overrides: Partial<BenchmarkReportPackPreviewDto> = {}): BenchmarkReportPackPreviewDto {
  return {
    subjectKey: 'comparison:12',
    subjectLabel: 'Comparison #12',
    subjectState: 'Comparable',
    suiteName: 'Board Suite',
    peers: [],
    estimates: COMPARISON_ESTIMATES,
    estimatedTotalCostUsd: 0.21,
    writerDisplayName: 'Claude Opus writer',
    sameProviderWarning: null,
    refusal: null,
    scope: BenchmarkReportScope.Comparison,
    comparisonId: 12,
    comparisonName: 'Gemini Flash vs Claude Opus vs GPT Sol group',
    comparisonEntryCount: 3,
    coversAllEntries: true,
    coveredSetKey: 'set-all',
    coveredModels: [
      { entryKey: 'run:1', label: 'Gemini Flash', provider: 'Google', letter: 'A' },
      { entryKey: 'group:4', label: 'GPT Sol group', provider: 'OpenAI', letter: 'B' },
      { entryKey: 'run:2', label: 'Claude Opus', provider: 'Anthropic', letter: 'C' }
    ],
    writtenDocuments: [],
    otherModelSets: [],
    subjectDocuments: [],
    writerContextWindowTokens: 200000,
    ...overrides
  };
}

/** A per-model preview for the given subjects, Gemini Flash first. */
function modelPreview(subjects: string[] = ['run:1'], overrides: Partial<BenchmarkReportPackPreviewDto> = {}): BenchmarkReportPackPreviewDto {
  return {
    ...comparisonPreview(),
    subjectKey: subjects[0],
    subjectLabel: 'Gemini Flash',
    peers: [{ letter: 'A', entryKey: 'run:2', label: 'Claude Opus', provider: 'Anthropic', state: 'Degraded' }],
    scope: BenchmarkReportScope.Model,
    coversAllEntries: false,
    coveredSetKey: null,
    coveredModels: subjects.map(key => ({ entryKey: key, label: key, provider: null })),
    estimates: subjects.flatMap(key => [estimate(ExecutiveSummary, 0.05, key), estimate(RootCauseAnalysis, 0.16, key)]),
    writtenDocuments: [],
    subjectDocuments: subjects.map(key => ({ subjectKey: key, subjectLabel: key, documents: [] })),
    ...overrides
  };
}

function jobDto(overrides: Partial<BenchmarkReportPackJobDto> = {}): BenchmarkReportPackJobDto {
  return {
    id: 'job-1',
    packId: 'pack-1',
    subjectKey: 'run:1',
    subjectLabel: 'Gemini Flash',
    suiteId: 5,
    suiteName: 'Board Suite',
    writerConfigId: 7,
    writerDisplayName: 'Claude Opus writer',
    startedByUserId: 'user-secret-id',
    startedAtUtc: '2026-09-28T10:00:00Z',
    completedAtUtc: null,
    status: 'Running',
    totalModelCalls: 1,
    inputTokens: 6000,
    outputTokens: 0,
    costUsd: null,
    documents: [
      { audience: ExecutiveSummary, status: 'Writing', documentId: null, errorMessage: null, modelCalls: 1 },
      { audience: RootCauseAnalysis, status: 'Pending', documentId: null, errorMessage: null, modelCalls: 0 }
    ],
    log: [{ timestampUtc: '2026-09-28T10:00:00Z', message: 'Started.', severity: 'info' }],
    ...overrides
  };
}

function written(audience: BenchmarkReportAudience, documentId: number, status = 'Completed'): BenchmarkReportPackDocumentProgressDto {
  return { audience, status, documentId, errorMessage: null, modelCalls: 1 };
}

describe('ReportPackPanelComponent', () => {
  let fixture: ComponentFixture<ReportPackPanelComponent>;
  let component: ReportPackPanelComponent;
  let http: HttpTestingController;
  let host: HTMLElement;

  beforeEach(async () => {
    localStorage.removeItem(REPORT_PACK_STORAGE_KEY);
    await TestBed.configureTestingModule({
      imports: [ReportPackPanelComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    fixture = TestBed.createComponent(ReportPackPanelComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('chartSelection', DEFAULT_CHART_SELECTION);
    fixture.componentRef.setInput('chartsAvailable', REPORT_CHART_FIGURES.map(figure => figure.key));
  });

  afterEach(() => {
    // A modal left open would make the rest of the page inert for the next spec.
    host.querySelectorAll('dialog').forEach(dialog => {
      if (dialog.open) {
        dialog.close();
      }
    });
    fixture.destroy();
    localStorage.removeItem(REPORT_PACK_STORAGE_KEY);
  });

  const q = <T extends HTMLElement = HTMLElement>(selector: string): T | null => host.querySelector<T>(selector);
  const text = (selector: string): string => (q(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const generateButton = (): HTMLButtonElement => q<HTMLButtonElement>('.rp-generate')!;
  const confirmDialog = (): HTMLDialogElement => q<HTMLDialogElement>('dialog.rp-same-provider-dialog')!;
  const cellText = (audience: BenchmarkReportAudience, selector: string): string =>
    text(`.rp-job-row[data-audience="${audience}"] ${selector}`);
  const picker = (): ModelMultiPickerComponent =>
    fixture.debugElement.query(By.directive(ModelMultiPickerComponent)).componentInstance as ModelMultiPickerComponent;
  const status = (): ComparisonDocumentsStatusComponent =>
    fixture.debugElement.query(By.directive(ComparisonDocumentsStatusComponent)).componentInstance as ComparisonDocumentsStatusComponent;
  const docRow = (key: string): HTMLElement => q(`.cds-row[data-row="${key}"]`)!;
  const docCheck = (key: string): HTMLInputElement => docRow(key).querySelector<HTMLInputElement>('input[type="checkbox"]')!;

  /** Waits, on the real clock, until `condition` holds; at most about a second. */
  async function until(condition: () => boolean): Promise<void> {
    for (let i = 0; i < 100 && !condition(); i++) {
      await new Promise(resolve => setTimeout(resolve, 10));
    }
  }

  interface OpenOptions {
    configs?: SystemAiConfigDto[];
    activeJob?: BenchmarkReportPackJobDto | null;
    context?: ReportPackContext;
  }

  /** Gives the panel its comparison and answers its two opening requests: the writers and the active job. */
  function openPanel(options: OpenOptions = {}): void {
    fixture.componentRef.setInput('context', options.context ?? CONTEXT);
    fixture.detectChanges();
    http.expectOne(SYSTEM_CONFIGS_URL).flush(options.configs ?? CONFIGS);
    answerActiveJob(options.activeJob ?? null);
  }

  function answerActiveJob(job: BenchmarkReportPackJobDto | null): void {
    const active = http.expectOne(ACTIVE_JOB_URL);
    if (job) {
      active.flush(job);
    } else {
      active.flush(null, { status: 204, statusText: 'No Content' });
    }
    fixture.detectChanges();
  }

  /** Waits out the debounce and answers the preview it requests. */
  function answerPreview(preview: BenchmarkReportPackPreviewDto = comparisonPreview()): TestRequest {
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS);
    const request = http.expectOne(PREVIEW_URL);
    request.flush(preview);
    fixture.detectChanges();
    return request;
  }

  /** Chooses a writer and answers the preview the debounce then requests. */
  function chooseWriter(id: number, preview: BenchmarkReportPackPreviewDto = comparisonPreview()): TestRequest {
    component.selectWriter(id);
    fixture.detectChanges();
    return answerPreview(preview);
  }

  /** The picker's choice, as its selection event reports it. */
  function chooseModels(keys: string[]): void {
    picker().selectionChange.emit({ keys, models: [] });
    fixture.detectChanges();
  }

  function selectScope(scope: 'comparison' | 'model'): void {
    q<HTMLButtonElement>(`#rp-scope-${scope}`)!.click();
    fixture.detectChanges();
  }

  /** Generates with writer 7 and answers the start and the first reading of the job. */
  function startJob(job: BenchmarkReportPackJobDto = jobDto()): void {
    chooseWriter(7);
    generateButton().click();
    http.expectOne(START_URL).flush({ jobId: job.id });
    http.expectOne(jobUrl(job.id)).flush(job);
    fixture.detectChanges();
  }

  // -------------------------------------------------------------------------------------------
  // Layout
  // -------------------------------------------------------------------------------------------

  it('lays out the form in the sidebar and the progress in the main area, with no heading above them', fakeAsync(() => {
    openPanel();
    answerPreview();

    const frame = q('app-run-report-frame')!;
    expect(frame.classList).toContain('rrf-layout-sidebar');
    const sidebar = q('aside.rrf-sidebar')!;
    const main = q('section.rrf-main')!;
    expect(sidebar.getAttribute('aria-label')).toBe('New report pack');
    expect(main.getAttribute('aria-label')).toBe('Report pack progress');

    // Scope, Models, the documents, Charts, Report writer, the estimate, Generate: in that order.
    const order = ['.rp-scope-tabs', '.rp-models-picker', 'app-comparison-documents-status', '.rp-charts-choice',
      '.rp-writer-selector', '#rp-estimate', '.rp-generate']
      .map(selector => sidebar.querySelector(selector));
    expect(order.every(element => element !== null)).toBe(true);
    for (let i = 1; i < order.length; i++) {
      expect(order[i - 1]!.compareDocumentPosition(order[i]!) & Node.DOCUMENT_POSITION_FOLLOWING, `${i}`).toBeTruthy();
    }

    // The frame renders no header: no step heading, subtitle or lead paragraph above the columns.
    expect(q('.rrf-header')).toBeNull();
    expect(q('.rp-identity, .rp-heading, .rp-subtitle, .rp-lead')).toBeNull();
    expect(text('#rp-new-heading')).toBe('New report pack');
    // The main column has no visible heading of its own; its region label names it.
    expect(main.querySelector('#rp-progress-heading, .rp-progress-heading')).toBeNull();
    expect(main.querySelector('.rp-main > h5, .rp-main > h4')).toBeNull();

    // Where the reports are kept is the info tip after the sidebar's heading.
    const headingRow = q('.rp-new-heading-row')!;
    expect(headingRow.firstElementChild).toBe(q('h5#rp-new-heading'));
    const leadButton = headingRow.querySelector<HTMLButtonElement>('app-info-tip button.gh-info-btn')!;
    expect(leadButton.getAttribute('aria-label')).toBe('About Where these reports are kept');
    expect(leadButton.getAttribute('interestfor')).toBe('rp-lead-tip');
    expect(text('#rp-lead-tip')).toBe(REPORT_PACK_LEAD_TEXT);
    expect(REPORT_PACK_LEAD_TEXT).toBe(
      'These reports are kept with the comparison: step 4 lists them, and so does Comparison reports on the Model '
      + 'Comparison tab. A run\'s or battery run\'s own reports are written in the AI Reports tab of its report.');

    // No subject select and no Documents checkboxes: the list of this comparison's documents replaces them.
    expect(q('#rp-subject')).toBeNull();
    expect(q('.rp-documents-choice')).toBeNull();
    expect(q('dialog.rp-dialog')).toBeNull();
    // No written document, so nothing to list them by.
    http.expectNone(r => r.url === DOCUMENTS_URL);
    fixture.destroy();
  }));

  it('derives its ids from idPrefix', () => {
    fixture.componentRef.setInput('idPrefix', 'mcr');
    openPanel();

    expect(q('h5#mcr-new-heading')).not.toBeNull();
    expect(q('#mcr-lead-tip')).not.toBeNull();
    expect(q('#mcr-scope-comparison')).not.toBeNull();
    expect(q('#mcr-models-label')).not.toBeNull();
    expect(q('#mcr-estimate')).not.toBeNull();
    expect(q('#mcr-docs-heading')).not.toBeNull();
    expect(q('#rp-scope-comparison')).toBeNull();
    expect(generateButton().getAttribute('aria-describedby')).toBe('mcr-estimate mcr-generate-blocked');
  });

  it('restores the stored sidebar width and stores a new one beside the writer', fakeAsync(() => {
    localStorage.setItem(REPORT_PACK_STORAGE_KEY, JSON.stringify({ writerConfigId: 8, sidebarWidth: 448 }));
    openPanel();
    answerPreview();

    const frame = fixture.debugElement.query(By.directive(RunReportFrameComponent)).componentInstance as RunReportFrameComponent;
    expect(frame.sidebarWidth).toBe(448);

    frame.sidebarWidthChange.emit(416);
    expect(JSON.parse(localStorage.getItem(REPORT_PACK_STORAGE_KEY)!)).toEqual({ writerConfigId: 8, sidebarWidth: 416 });
    fixture.destroy();
  }));

  it('falls back to the frame\'s default width when the stored record is unreadable', () => {
    localStorage.setItem(REPORT_PACK_STORAGE_KEY, '{not json');
    openPanel();

    const frame = fixture.debugElement.query(By.directive(RunReportFrameComponent)).componentInstance as RunReportFrameComponent;
    expect(frame.sidebarWidth).toBeNull();
    expect(frame.sidebarWidthPx).toBe(384);
  });

  // -------------------------------------------------------------------------------------------
  // The document scope
  // -------------------------------------------------------------------------------------------

  it('offers the two scopes as segmented tabs, Whole comparison first and chosen, with the tip on per-model documents', fakeAsync(() => {
    openPanel();

    const tablist = q('.rp-scope-tabs')!;
    expect(tablist.getAttribute('role')).toBe('tablist');
    expect(tablist.classList).toContain('gh-tabs-segmented');
    expect(tablist.getAttribute('aria-label')).toBe('Document scope');
    const tabs = Array.from(tablist.querySelectorAll<HTMLButtonElement>('[role="tab"]'));
    expect(tabs.map(tab => tab.textContent!.trim())).toEqual(['Whole comparison, Recommended', 'One model at a time']);
    expect(tabs.map(tab => tab.getAttribute('aria-selected'))).toEqual(['true', 'false']);
    expect(tabs.map(tab => tab.getAttribute('tabindex'))).toEqual(['0', '-1']);
    const panel = q('#rp-scope-panel')!;
    expect(panel.getAttribute('role')).toBe('tabpanel');
    expect(panel.getAttribute('aria-labelledby')).toBe('rp-scope-comparison');
    expect(tabs.every(tab => tab.getAttribute('aria-controls') === 'rp-scope-panel')).toBe(true);
    expect(text('#rp-scope-tip')).toContain(REPORT_PACK_MODEL_SCOPE_TIP);

    const first = answerPreview();
    expect(first.request.body.scope).toBe(BenchmarkReportScope.Comparison);

    // ArrowRight moves to One model at a time, focus following, and the choice is remembered.
    tabs[0].focus();
    tabs[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();
    expect(tabs[1].getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(tabs[1]);
    expect(panel.getAttribute('aria-labelledby')).toBe('rp-scope-model');
    expect(JSON.parse(localStorage.getItem(REPORT_PACK_STORAGE_KEY)!)).toEqual({ documentScope: 'model' });

    const second = answerPreview(modelPreview());
    expect(second.request.body.scope).toBe(BenchmarkReportScope.Model);
    expect(second.request.body.subjectKeys).toEqual(['run:1']);
    expect(second.request.body.coveredEntryKeys).toBeUndefined();
    fixture.destroy();
  }));

  it('wraps the scope tabs\' labels in a narrow sidebar instead of clipping them', () => {
    openPanel();
    // Narrower than the real minimum (a 320px track less the stable scrollbar gutter).
    q('.rp-new')!.style.inlineSize = '288px';

    const tabs = Array.from(host.querySelectorAll<HTMLButtonElement>('.rp-scope-tab'));
    expect(tabs.length).toBe(2);
    for (const tab of tabs) {
      expect(tab.scrollWidth, tab.id).toBeLessThanOrEqual(tab.clientWidth);
      const box = tab.getBoundingClientRect();
      const label = tab.querySelector<HTMLElement>('.rp-scope-tab-label')!.getBoundingClientRect();
      expect(label.left, tab.id).toBeGreaterThanOrEqual(box.left);
      expect(label.right, tab.id).toBeLessThanOrEqual(box.right);
      expect(label.top, tab.id).toBeGreaterThanOrEqual(box.top);
      expect(label.bottom, tab.id).toBeLessThanOrEqual(box.bottom);
    }
    const label = tabs[0].querySelector<HTMLElement>('.rp-scope-tab-label')!.getBoundingClientRect();
    const note = tabs[0].querySelector<HTMLElement>('.rp-scope-tab-note')!.getBoundingClientRect();
    expect(note.top).toBeGreaterThanOrEqual(label.bottom - 0.5);
  });

  it('opens on the remembered scope', () => {
    localStorage.setItem(REPORT_PACK_STORAGE_KEY, JSON.stringify({ documentScope: 'model' }));
    fixture = TestBed.createComponent(ReportPackPanelComponent);
    component = fixture.componentInstance;
    host = fixture.nativeElement as HTMLElement;
    openPanel();

    expect(component.scopeMode).toBe('model');
    expect(q('#rp-scope-model')!.getAttribute('aria-selected')).toBe('true');
  });

  // -------------------------------------------------------------------------------------------
  // The models
  // -------------------------------------------------------------------------------------------

  it('offers every entry that is not Excluded under Models, all chosen, and says it writes the comparison-wide documents', fakeAsync(() => {
    openPanel();

    const instance = picker();
    expect(instance.options.map(option => option.key)).toEqual(OFFERED);
    expect(instance.options.map(option => option.model.displayName)).toEqual(['Gemini Flash', 'Claude Opus', 'GPT Sol group']);
    expect(instance.selectedKeys).toEqual(OFFERED);
    expect(instance.min).toBe(2);
    expect(instance.max).toBe(12);
    expect(instance.chips).toBe('none');
    expect(instance.showPrice).toBe(false);
    expect(instance.showParallel).toBe(false);
    expect(instance.labelledBy).toBe('rp-models-label');
    expect(text('#rp-models-label')).toBe('Models');
    expect(text('.rp-models-picker .selector-trigger')).toBe('All 3 models');
    // The Excluded entry is not offered, and the line says so.
    expect(text('#rp-models-line')).toBe(`Writes the comparison-wide documents. ${EXCLUDED_ONE}`);
    expect(instance.describedBy).toBe('rp-models-line');
    expect(q('.rp-models-cap')).toBeNull();

    // The covered letters once the preview answers.
    expect(q('.rp-letters')).toBeNull();
    const request = answerPreview();
    expect(request.request.body.coveredEntryKeys).toEqual(OFFERED);
    expect(q('.rp-letters')!.getAttribute('role')).toBe('group');
    expect(q('.rp-letters')!.getAttribute('aria-labelledby')).toBe('rp-letters-title');
    expect(text('#rp-letters-title')).toBe('Letters in the anonymized copies');
    const texts = (selector: string): string[] =>
      Array.from(host.querySelectorAll(selector)).map(element => (element.textContent ?? '').replace(/\s+/g, ' ').trim());
    expect(texts('.rp-letters-list dt')).toEqual(['Letter A', 'Letter B', 'Letter C']);
    expect(texts('.rp-letters-list dd .model-identity-name')).toEqual(['Gemini Flash', 'GPT Sol group', 'Claude Opus']);
    // The thinking level is a badge, one per lettered model, never a parenthesis after the name.
    expect(texts('.rp-letters-list dd .thinking-badge')).toEqual(
      ['thinking level medium', 'thinking level medium', 'thinking level medium']);
    expect(texts('.rp-letters-list dd').some(line => line.includes('('))).toBe(false);
    expect(q('.rp-letters-list dd app-provider-badge')).toBeNull();
    fixture.destroy();
  }));

  it('writes a model subset when not every model is chosen, naming only what it leaves out', fakeAsync(() => {
    openPanel();
    answerPreview();

    // A real click on an option of the open list.
    q<HTMLButtonElement>('.rp-models-picker .selector-trigger')!.click();
    fixture.detectChanges();
    const claude = Array.from(host.querySelectorAll<HTMLElement>('.rp-models-picker [role="option"]'))
      .find(option => (option.textContent ?? '').includes('Claude Opus'))!;
    claude.click();
    fixture.detectChanges();

    expect(component.chosenKeys).toEqual(['run:1', 'group:4']);
    expect(text('.rp-models-picker .selector-trigger')).toBe('2 of 3 models');
    expect(text('#rp-models-line')).toBe(`Writes documents for 2 of 3 models — leaves out Claude Opus (medium). ${EXCLUDED_ONE}`);
    // The list waits for the subset's own documents.
    expect(text('.cds-loading')).toBe('Looking up the documents already written…');

    const request = answerPreview(comparisonPreview({ coversAllEntries: false, coveredSetKey: 'set-2', coveredModels: [] }));
    expect(request.request.body.coveredEntryKeys).toEqual(['run:1', 'group:4']);
    expect(host.querySelectorAll('.cds-row').length).toBe(2);
    fixture.destroy();
  }));

  it('names the source after a model that appears twice', fakeAsync(() => {
    openPanel({
      context: {
        ...CONTEXT,
        runIds: [],
        groupIds: [],
        batteryRunIds: [9, 10, 11],
        entries: [
          entry('battery:9', 'GPT-5.6 Luna', 60, { thinkingLevel: 'max' }),
          entry('battery:10', 'GPT-5.6 Luna', 62, { thinkingLevel: 'max' }),
          entry('battery:11', 'Claude Opus', 58)
        ],
        entryKeys: ['battery:9', 'battery:10', 'battery:11']
      }
    });

    expect(picker().options.map(option => option.detail)).toEqual(['Battery run #9', 'Battery run #10', undefined]);
    chooseModels(['battery:9', 'battery:11']);
    expect(text('#rp-models-line')).toBe('Writes documents for 2 of 3 models — leaves out GPT-5.6 Luna (max) from Battery run #10.');
    fixture.destroy();
  }));

  it('starts with the 12 highest-Index models of more than 12, and says why', () => {
    const entries = Array.from({ length: 14 }, (_, i) => entry(`run:${i + 1}`, `Model ${i + 1}`, 50 + i));
    openPanel({
      context: { ...CONTEXT, runIds: entries.map((_, i) => i + 1), groupIds: [], entries, entryKeys: entries.map(e => e.key) }
    });

    expect(component.chosenKeys.length).toBe(12);
    expect(component.chosenKeys).not.toContain('run:1');
    expect(component.chosenKeys).not.toContain('run:2');
    expect(text('.rp-models-cap')).toBe(
      'A document covers at most 12 models, so the 12 with the highest Intelligence Index of the 14 are chosen first.');
    expect(text('#rp-models-line')).toBe('Writes documents for 12 of 14 models — leaves out Model 1 (medium), Model 2 (medium).');
  });

  it('starts One model at a time on the highest-Index model, and keeps each scope\'s choice until the comparison changes', fakeAsync(() => {
    openPanel();
    selectScope('model');

    expect(component.chosenKeys).toEqual(['run:1']);
    expect(picker().min).toBe(1);
    expect(picker().max).toBeNull();
    expect(text('#rp-models-line')).toBe(
      `Writes the per-model documents of Gemini Flash (medium), compared with the other models. ${EXCLUDED_ONE}`);
    expect(q('.rp-letters')).toBeNull();

    chooseModels(['run:2', 'group:4']);
    expect(text('#rp-models-line')).toBe(
      `Writes the per-model documents of 2 models, one set each, each compared with the other models. ${EXCLUDED_ONE}`);
    selectScope('comparison');
    chooseModels(['run:1', 'run:2']);
    selectScope('model');
    expect(component.chosenKeys).toEqual(['run:2', 'group:4']);
    selectScope('comparison');
    expect(component.chosenKeys).toEqual(['run:1', 'run:2']);

    // Another comparison resets both.
    fixture.componentRef.setInput('context', { ...CONTEXT, entryKeys: ['run:1', 'run:2', 'run:3', 'group:4', 'run:5'] });
    fixture.detectChanges();
    answerActiveJob(null);
    expect(component.chosenKeys).toEqual(OFFERED);
    selectScope('model');
    expect(component.chosenKeys).toEqual(['run:1']);
    fixture.destroy();
  }));

  it('says when every entry is Excluded, and that nothing can be written', () => {
    openPanel({ context: { ...CONTEXT, entries: [ENTRIES[2]], entryKeys: ['run:3'], runIds: [3], groupIds: [] } });

    expect(q('app-model-multi-picker')).toBeNull();
    expect(text('.rp-no-models')).toBe('Every entry of this comparison is Excluded, so no document can be written.');
    expect(text('#rp-generate-blocked')).toBe('Every entry of this comparison is Excluded, so no document can be written.');
    expect(generateButton().disabled).toBe(true);
  });

  // -------------------------------------------------------------------------------------------
  // Documents of this comparison
  // -------------------------------------------------------------------------------------------

  it('lists the chosen set\'s documents, a written one unchecked, and estimates again without it', fakeAsync(() => {
    openPanel();
    const first = chooseWriter(7, comparisonPreview({ writtenDocuments: [writtenDoc(ExecutiveSummary, 41)] }));
    expect(first.request.body.audiences).toEqual(ALL_AUDIENCES);

    const executive = docRow(`comparison|${ExecutiveSummary}`);
    expect(executive.querySelector('.cds-status')!.textContent!.trim()).toBe('Written');
    expect(docCheck(`comparison|${ExecutiveSummary}`).checked).toBe(false);
    expect(docCheck(`comparison|${RootCauseAnalysis}`).checked).toBe(true);
    expect(text('#rp-estimate')).toBe('Estimating…');

    const second = answerPreview(comparisonPreview({
      estimates: COMPARISON_ESTIMATES.slice(1),
      writtenDocuments: [writtenDoc(ExecutiveSummary, 41)]
    }));
    expect(second.request.body.audiences).toEqual([RootCauseAnalysis]);
    expect(text('#rp-estimate .gh-estimate-total')).toBe('about $0.16');
    expect(generateButton().disabled).toBe(false);

    // The written document's flags and charts come from the comparison's list.
    const list = http.expectOne(r => r.url === DOCUMENTS_URL);
    expect(list.request.params.get('comparisonId')).toBe('12');
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS);
    http.expectNone(PREVIEW_URL);
    fixture.destroy();
  }));

  it('rewrites a written document when Rewrite is checked, naming it to be replaced', fakeAsync(() => {
    openPanel();
    chooseWriter(7, comparisonPreview({ writtenDocuments: [writtenDoc(ExecutiveSummary, 41)] }));
    answerPreview(comparisonPreview({ estimates: COMPARISON_ESTIMATES.slice(1), writtenDocuments: [writtenDoc(ExecutiveSummary, 41)] }));

    docCheck(`comparison|${ExecutiveSummary}`).click();
    fixture.detectChanges();
    const again = answerPreview(comparisonPreview({ writtenDocuments: [writtenDoc(ExecutiveSummary, 41)] }));
    expect(again.request.body.audiences).toEqual(ALL_AUDIENCES);
    expect(again.request.body.replaceDocumentIds).toBeUndefined();

    generateButton().click();
    const start = http.expectOne(START_URL);
    expect(start.request.body).toEqual({
      runIds: [1, 2, 3],
      groupIds: [4],
      pricingBasis: 1,
      subjectKey: '',
      scope: BenchmarkReportScope.Comparison,
      coveredEntryKeys: OFFERED,
      audiences: ALL_AUDIENCES,
      writerModelConfigurationId: 7,
      acknowledgeSameProvider: false,
      replaceDocumentIds: [41]
    });
    fixture.destroy();
  }));

  it('keeps Generate focusable and aria-disabled, with the reason, when every document is written and none is checked', fakeAsync(() => {
    openPanel();
    const all = [writtenDoc(ExecutiveSummary, 41), writtenDoc(RootCauseAnalysis, 42)];
    chooseWriter(7, comparisonPreview({ writtenDocuments: all }));
    answerPreview(comparisonPreview({ estimates: [], writtenDocuments: all }));

    const button = generateButton();
    expect(button.disabled).toBe(false);
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(button.getAttribute('aria-describedby')).toBe('rp-estimate rp-generate-blocked');
    expect(text('#rp-generate-blocked')).toBe(REPORT_PACK_ALL_WRITTEN_REASON);
    button.click();
    http.expectNone(START_URL);
    fixture.destroy();
  }));

  it('lists each chosen model\'s documents One model at a time, and writes the checked ones', fakeAsync(() => {
    openPanel();
    selectScope('model');
    chooseModels(['run:1', 'group:4']);
    const preview = modelPreview(['run:1', 'group:4'], {
      subjectDocuments: [
        { subjectKey: 'run:1', subjectLabel: 'Gemini Flash', documents: [writtenDoc(RootCauseAnalysis, 51, 'run:1')] },
        { subjectKey: 'group:4', subjectLabel: 'GPT Sol group', documents: [writtenDoc(RootCauseAnalysis, 52, 'group:4')] }
      ]
    });
    const first = chooseWriter(7, preview);
    expect(first.request.body.scope).toBe(BenchmarkReportScope.Model);
    expect(first.request.body.subjectKeys).toEqual(['run:1', 'group:4']);
    const second = answerPreview(preview);
    expect(second.request.body.audiences).toEqual([ExecutiveSummary]);

    expect(Array.from(host.querySelectorAll('.cds-group-title')).map(title => title.textContent!.trim()))
      .toEqual(['Gemini Flash (medium)', 'GPT Sol group (medium)']);
    expect(docCheck(`group:4|${RootCauseAnalysis}`).checked).toBe(false);
    // Two models of one document each: two parts.
    expect(text('#rp-estimate .gh-estimate-total')).toBe('about $0.10');
    expect(host.querySelectorAll('#rp-estimate .gh-estimate-parts > div').length).toBe(2);
    expect(text('#rp-estimate .gh-estimate-note')).toContain('For 2 models, each against the others.');

    generateButton().click();
    const start = http.expectOne(START_URL);
    expect(start.request.body).toEqual({
      runIds: [1, 2, 3],
      groupIds: [4],
      pricingBasis: 1,
      subjectKey: 'run:1',
      scope: BenchmarkReportScope.Model,
      subjectKeys: ['run:1', 'group:4'],
      audiences: [ExecutiveSummary],
      writerModelConfigurationId: 7,
      acknowledgeSameProvider: false,
      replaceDocumentIds: []
    });
    fixture.destroy();
  }));

  it('writes only the models with a checked document, and refuses different documents for different models', fakeAsync(() => {
    openPanel();
    selectScope('model');
    chooseModels(['run:1', 'group:4']);
    chooseWriter(7, modelPreview(['run:1', 'group:4']));

    // Every document of GPT Sol group unchecked: the job is about Gemini Flash alone.
    for (const audience of ALL_AUDIENCES) {
      docCheck(`group:4|${audience}`).click();
      fixture.detectChanges();
    }
    answerPreview(modelPreview(['run:1', 'group:4']));
    expect(generateButton().disabled).toBe(false);

    // One of them checked again: not the same documents as Gemini Flash's.
    docCheck(`group:4|${ExecutiveSummary}`).click();
    fixture.detectChanges();
    expect(text('#rp-generate-blocked')).toBe(REPORT_PACK_UNEVEN_MODELS_REASON);
    expect(generateButton().disabled).toBe(true);
    answerPreview(modelPreview(['run:1', 'group:4']));
    expect(text('#rp-generate-blocked')).toBe(REPORT_PACK_UNEVEN_MODELS_REASON);

    docCheck(`group:4|${ExecutiveSummary}`).click();
    fixture.detectChanges();
    answerPreview(modelPreview(['run:1', 'group:4']));
    generateButton().click();
    expect(http.expectOne(START_URL).request.body.subjectKeys).toEqual(['run:1']);
    fixture.destroy();
  }));

  it('shows the comparison\'s flags and chart counts for written documents, from the list by comparison number', fakeAsync(() => {
    fixture.componentRef.setInput('comparisonId', 12);
    openPanel();
    answerPreview(comparisonPreview({ writtenDocuments: [writtenDoc(ExecutiveSummary, 41)] }));
    answerPreview(comparisonPreview({ writtenDocuments: [writtenDoc(ExecutiveSummary, 41)] }));

    const list = http.expectOne(r => r.url === DOCUMENTS_URL);
    expect(list.request.params.get('comparisonId')).toBe('12');
    list.flush([{
      id: 41,
      title: 'Comparison #12 — Gemini Flash vs Claude Opus vs GPT Sol group: Executive Summary',
      audience: ExecutiveSummary,
      runChangedSinceGeneration: true,
      chartFigureKeys: ['p1a-quality', 's2-quality-cost'],
      allowedDisclosures: []
    } as unknown as BenchmarkReportDocumentListItemDto]);
    fixture.detectChanges();

    const row = docRow(`comparison|${ExecutiveSummary}`);
    expect(row.querySelector('.cds-changed')!.textContent!.trim()).toBe('Comparison changed since written');
    expect(Array.from(row.querySelectorAll('.cds-meta-part')).map(part => part.textContent!.trim()))
      .toEqual(['1 min 05 s', '$0.04', 'Charts: 2']);
    expect(row.querySelector('.cds-writer')!.textContent!.trim()).toBe('by Claude Opus writer (Anthropic; medium)');

    // A finished chart set lists the documents again, for its count.
    fixture.componentRef.setInput('chartStatus', { 41: { state: 'done', count: 3, images: 6 } });
    fixture.detectChanges();
    http.expectOne(r => r.url === DOCUMENTS_URL);
    fixture.destroy();
  }));

  it('lists the documents again after a delete, and tells the host', fakeAsync(() => {
    openPanel();
    answerPreview(comparisonPreview({ writtenDocuments: [writtenDoc(ExecutiveSummary, 41)] }));
    answerPreview(comparisonPreview({ writtenDocuments: [writtenDoc(ExecutiveSummary, 41)] }));
    let changes = 0;
    component.documentsChanged.subscribe(() => changes++);

    status().documentDeleted.emit(41);
    expect(changes).toBe(1);
    answerPreview(comparisonPreview());
    expect(docCheck(`comparison|${ExecutiveSummary}`).checked).toBe(true);
    fixture.destroy();
  }));

  it('lists other model sets and chooses one with Choose these models', fakeAsync(() => {
    openPanel();
    answerPreview(comparisonPreview({
      otherModelSets: [{
        coveredSetKey: 'set-2',
        subjectKey: 'comparison:12/set-2',
        coversAllEntries: false,
        coveredModels: [
          { entryKey: 'run:1', label: 'Gemini Flash', provider: 'Google', letter: 'A' },
          { entryKey: 'run:2', label: 'Claude Opus', provider: 'Anthropic', letter: 'B' }
        ],
        documents: [writtenDoc(ExecutiveSummary, 61, 'comparison:12/set-2')]
      }]
    }));

    expect(text('details.cds-other-sets > summary')).toBe('Other model sets (1)');
    q<HTMLButtonElement>('.cds-choose-set')!.click();
    fixture.detectChanges();
    expect(component.chosenKeys).toEqual(['run:1', 'run:2']);
    expect(text('#rp-models-line')).toBe(`Writes documents for 2 of 3 models — leaves out GPT Sol group (medium). ${EXCLUDED_ONE}`);
    const request = answerPreview(comparisonPreview({ coversAllEntries: false }));
    expect(request.request.body.coveredEntryKeys).toEqual(['run:1', 'run:2']);
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // Writers and refusals
  // -------------------------------------------------------------------------------------------

  it('offers only enabled Benchmark-role configurations with a key as writers, and requires one', fakeAsync(() => {
    openPanel({ configs: [...CONFIGS, config(11, 'Keyless writer', 'OpenAI', { hasApiKey: false })] });

    expect(component.writers.map(writer => writer.id)).toEqual([7, 8]);
    expect(component.writerId).toBeNull();
    expect(generateButton().disabled).toBe(true);
    expect(text('#rp-generate-blocked')).toBe('Choose a report writer.');
    // A reason shown nowhere else stays visible.
    expect(q('#rp-generate-blocked')!.classList).not.toContain('visually-hidden');

    // The documents are listed without a writer; there is no estimate yet.
    const request = answerPreview();
    expect(request.request.body.writerModelConfigurationId).toBe(0);
    expect(q('#rp-estimate')!.classList).toContain('is-empty');
    expect(host.querySelectorAll('.cds-row').length).toBe(2);
    fixture.destroy();
  }));

  it('restores the remembered writer while it still qualifies', fakeAsync(() => {
    localStorage.setItem(REPORT_PACK_STORAGE_KEY, JSON.stringify({ writerConfigId: 8 }));
    openPanel();

    expect(component.writerId).toBe(8);
    expect(answerPreview().request.body.writerModelConfigurationId).toBe(8);
    fixture.destroy();
  }));

  it('shows a refusal before Generate, and keeps Generate disabled while it stands', fakeAsync(() => {
    openPanel();
    const refusal = 'The writer is one of the covered models. Choose a writer of another model.';
    chooseWriter(7, comparisonPreview({ refusal, estimates: [], estimatedTotalCostUsd: null }));

    const line = q('.rp-refusal')!;
    expect(line.classList).toContain('gh-field-error');
    expect(line.getAttribute('role')).toBe('alert');
    expect(line.textContent).toContain(refusal);
    expect(line.compareDocumentPosition(generateButton()) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(q('.rp-writer-selector .selector-trigger')!.getAttribute('aria-describedby')).toContain('rp-writer-refusal');
    expect(generateButton().disabled).toBe(true);
    expect(text('#rp-generate-blocked')).toBe('The documents cannot be written as chosen; the reason is shown under the report writer.');

    generateButton().click();
    http.expectNone(START_URL);
    fixture.destroy();
  }));

  it('shows the peerless refusal with its own reason under Generate', fakeAsync(() => {
    openPanel();
    selectScope('model');
    chooseWriter(7, modelPreview(['run:1'], { refusal: REPORT_PACK_PEERLESS_REFUSAL, peers: [], estimates: [], estimatedTotalCostUsd: null }));

    expect(text('.rp-refusal')).toBe(REPORT_PACK_PEERLESS_REFUSAL);
    expect(generateButton().disabled).toBe(true);
    expect(text('#rp-generate-blocked')).toBe('A chosen model has no other model to be compared with in this comparison.');
    fixture.destroy();
  }));

  it('shows the server\'s refusal of a document already written as a start error', fakeAsync(() => {
    openPanel();
    chooseWriter(7);
    generateButton().click();
    const message = 'The Executive Summary of these models is already written for this comparison. Delete it, or rewrite it to replace it.';
    http.expectOne(START_URL).flush({ error: message }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(confirmDialog().open).toBe(false);
    const alert = q('.rp-start-error')!;
    expect(alert.getAttribute('role')).toBe('alert');
    expect(alert.textContent!.trim()).toBe(message);
    expect(component.activeJobId).toBeNull();
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // The comparison changes
  // -------------------------------------------------------------------------------------------

  it('keeps the form when the comparison is recomputed with the same entry keys', fakeAsync(() => {
    openPanel();
    chooseWriter(7);
    chooseModels(['run:1', 'group:4']);
    answerPreview();

    fixture.componentRef.setInput('context', { ...CONTEXT, pricingBasis: 'AsRun' });
    fixture.detectChanges();

    http.expectNone(ACTIVE_JOB_URL);
    expect(component.chosenKeys).toEqual(['run:1', 'group:4']);
    expect(component.writerId).toBe(7);
    // The pricing basis changed, so the preview is asked again, with it.
    const again = answerPreview();
    expect(again.request.body.pricingBasis).toBe(0);
    expect(again.request.body.coveredEntryKeys).toEqual(['run:1', 'group:4']);
    fixture.destroy();
  }));

  it('resets the form, and looks for a running job again, when the entry keys change', fakeAsync(() => {
    openPanel();
    chooseWriter(7);
    chooseModels(['run:1', 'group:4']);
    answerPreview();

    fixture.componentRef.setInput('context', {
      ...CONTEXT,
      runIds: [2],
      entries: ENTRIES.slice(1, 2).concat(ENTRIES.slice(3)),
      entryKeys: ['run:2', 'group:4']
    });
    fixture.detectChanges();
    answerActiveJob(jobDto({ id: 'job-7', subjectKey: 'run:2', subjectLabel: 'Claude Opus' }));

    expect(component.chosenKeys).toEqual(['run:2', 'group:4']);
    expect(component.writerId).toBeNull();
    expect(component.activeJobId).toBe('job-7');
    expect(host.querySelectorAll('.rp-job-row').length).toBe(2);
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // The estimate
  // -------------------------------------------------------------------------------------------

  it('requests the preview with the comparison request, the scope, the models, the documents and the writer', fakeAsync(() => {
    openPanel();
    const request = chooseWriter(7);

    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      runIds: [1, 2, 3],
      groupIds: [4],
      pricingBasis: 1,
      subjectKey: '',
      scope: BenchmarkReportScope.Comparison,
      coveredEntryKeys: OFFERED,
      audiences: ALL_AUDIENCES,
      writerModelConfigurationId: 7,
      acknowledgeSameProvider: false
    });

    const panel = q('#rp-estimate')!;
    expect(panel.classList).toContain('gh-estimate-panel');
    expect(panel.getAttribute('role')).toBe('status');
    expect(text('#rp-estimate .gh-estimate-label')).toBe('Estimated cost');
    expect(text('#rp-estimate .gh-estimate-total')).toBe('about $0.21');
    const parts = Array.from(panel.querySelectorAll('.gh-estimate-parts > div'))
      .map(part => [part.querySelector('dt')!.textContent!.trim(), part.querySelector('dd')!.textContent!.trim()]);
    expect(parts).toEqual([
      ['Executive Summary', '$0.05'],
      ['Root Cause Analysis', '$0.16']
    ]);
    expect(text('#rp-estimate .gh-estimate-note')).toContain('For 3 models, compared as equals.');
    expect(text('#rp-estimate .gh-estimate-note')).toContain('A repair turn can roughly double a document’s cost.');
    expect(generateButton().getAttribute('aria-describedby')).toBe('rp-estimate');
    expect(generateButton().disabled).toBe(false);
    fixture.destroy();
  }));

  it('shows the estimate as a range from the first call to one repair turn per document, when both are known and differ', fakeAsync(() => {
    openPanel();
    chooseWriter(7, comparisonPreview({
      estimates: [
        { ...estimate(ExecutiveSummary, 0.34), estimatedCostWithRepairUsd: 0.69 },
        { ...estimate(RootCauseAnalysis, 0.7), estimatedCostWithRepairUsd: 1.41 }
      ]
    }));

    expect(text('#rp-estimate .gh-estimate-total')).toBe('about $1.04 to $2.10');
    const parts = Array.from(host.querySelectorAll('#rp-estimate .gh-estimate-parts > div'))
      .map(part => part.querySelector('dd')!.textContent!.trim());
    expect(parts).toEqual(['$0.34 to $0.69', '$0.70 to $1.41']);
    expect(text('#rp-estimate .gh-estimate-note')).toContain('From the first call alone to one repair turn per document.');

    // One document without a repair-inclusive figure: the first-call total alone.
    chooseWriter(8, comparisonPreview({
      estimates: [{ ...estimate(ExecutiveSummary, 0.34), estimatedCostWithRepairUsd: 0.69 }, estimate(RootCauseAnalysis, 0.7)]
    }));
    expect(text('#rp-estimate .gh-estimate-total')).toBe('about $1.04');
    expect(text('#rp-estimate .gh-estimate-note')).toContain('A repair turn can roughly double a document’s cost.');
    fixture.destroy();
  }));

  it('covers battery results and sends their ids for a comparison of battery results', fakeAsync(() => {
    const batteryEntries = [
      entry('battery:4', 'Gemini Flash', 70, { sourceKind: 'Battery', sourceId: 4 }),
      entry('battery:9', 'Claude Opus', 60, { sourceKind: 'Battery', sourceId: 9, provider: 'Anthropic' })
    ];
    openPanel({
      context: {
        runIds: [],
        groupIds: [],
        batteryRunIds: [4, 9],
        pricingBasis: 'Current',
        entries: batteryEntries,
        entryKeys: ['battery:4', 'battery:9'],
        suiteId: null,
        suiteName: 'Core Battery'
      }
    });

    expect(picker().options.map(option => option.key)).toEqual(['battery:4', 'battery:9']);
    const request = chooseWriter(7);
    expect(request.request.body.batteryRunIds).toEqual([4, 9]);
    expect(request.request.body.runIds).toEqual([]);
    expect(request.request.body.coveredEntryKeys).toEqual(['battery:4', 'battery:9']);
    fixture.destroy();
  }));

  it('keeps the estimate panel in place while empty, and busy while estimating', fakeAsync(() => {
    openPanel();
    answerPreview();
    const panel = q('#rp-estimate')!;
    expect(panel.getAttribute('role')).toBe('status');
    expect(panel.classList).toContain('is-empty');

    component.selectWriter(7);
    fixture.detectChanges();
    expect(panel.getAttribute('aria-busy')).toBe('true');
    expect(panel.classList).toContain('is-muted');
    expect(text('#rp-estimate')).toBe('Estimating…');
    // The spinner shows at once, without the delayed reveal.
    const indicator = q('#rp-estimate app-loading-indicator')!;
    expect(indicator.querySelector('svg.dc-ring')).not.toBeNull();
    expect(indicator.classList).not.toContain('li-reveal');
    expect(generateButton().getAttribute('aria-describedby')).toBe('rp-estimate rp-generate-blocked');
    // Estimating… already says it under the estimate: the reason stays Generate's description, out of sight.
    expect(text('#rp-generate-blocked')).toBe('Checking the writer and estimating the cost…');
    expect(q('#rp-generate-blocked')!.classList).toContain('visually-hidden');

    answerPreview();
    expect(panel.hasAttribute('aria-busy')).toBe(false);
    expect(generateButton().disabled).toBe(false);
    fixture.destroy();
  }));

  it('debounces the preview, and drops the answer for an older choice', fakeAsync(() => {
    openPanel();
    chooseWriter(7);

    // Two quick changes make one request.
    docCheck(`comparison|${RootCauseAnalysis}`).click();
    fixture.detectChanges();
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS / 2);
    chooseModels(['run:1', 'run:2']);
    expect(generateButton().disabled).toBe(true);
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS - 1);
    http.expectNone(PREVIEW_URL);
    tick(1);
    const older = http.expectOne(PREVIEW_URL);
    expect(older.request.body.coveredEntryKeys).toEqual(['run:1', 'run:2']);

    // A newer choice drops the request in flight; its answer never lands.
    component.selectWriter(8);
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS);
    const newer = http.expectOne(PREVIEW_URL);
    expect(older.cancelled).toBe(true);
    expect(newer.request.body.writerModelConfigurationId).toBe(8);
    newer.flush(comparisonPreview({ coversAllEntries: false }));
    fixture.detectChanges();
    expect(host.querySelectorAll('.cds-row').length).toBe(2);
    fixture.destroy();
  }));

  it('warns from 70% of the writer\'s context window', fakeAsync(() => {
    openPanel();
    chooseWriter(7, comparisonPreview({
      estimates: [estimate(ExecutiveSummary, 0.05), estimate(RootCauseAnalysis, 0.16, 'comparison:12', 0.78)]
    }));

    const warning = q('.rp-context-warning')!;
    expect(warning.classList).toContain('alert-warning');
    expect(warning.textContent!.replace(/\s+/g, ' ').trim()).toBe(
      'The Root Cause Analysis\'s prompt would fill about 78% of the writer\'s context window; '
      + 'from 90% it is refused. Fewer models, or a writer with a larger context window, leaves the writer more room.');
    expect(generateButton().disabled).toBe(false);

    // An unchecked document's prompt does not count.
    docCheck(`comparison|${RootCauseAnalysis}`).click();
    fixture.detectChanges();
    answerPreview(comparisonPreview({ estimates: [estimate(ExecutiveSummary, 0.05)] }));
    expect(q('.rp-context-warning')).toBeNull();
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // The same-provider writer (D3)
  // -------------------------------------------------------------------------------------------

  it('warns about a same-provider writer, and confirms on every Generate before sending the acknowledgment', fakeAsync(() => {
    openPanel();
    const warning = 'The writer shares Anthropic with Claude Opus; its documents may favor its own family.';
    chooseWriter(7, comparisonPreview({ sameProviderWarning: warning }));

    const alert = q('.rp-same-provider')!;
    expect(alert.classList).toContain('alert-warning');
    expect(alert.textContent).toContain(warning);
    expect(text('.rp-same-provider .alert-heading')).toBe('Writer from a chosen model\'s provider');
    expect(generateButton().disabled).toBe(false);

    // Cancel: nothing is sent, and focus returns to Generate.
    generateButton().click();
    fixture.detectChanges();
    expect(confirmDialog().open).toBe(true);
    expect(text('#rp-same-provider-title')).toBe('Same-Provider Report Writer');
    expect(text('.rp-same-provider-confirm-text')).toBe(warning);
    http.expectNone(START_URL);
    q<HTMLButtonElement>('.rp-same-provider-cancel')!.click();
    fixture.detectChanges();
    expect(confirmDialog().open).toBe(false);
    expect(document.activeElement).toBe(generateButton());
    http.expectNone(START_URL);

    // Asked again, never remembered; Write Anyway sends the acknowledgment.
    generateButton().click();
    fixture.detectChanges();
    expect(confirmDialog().open).toBe(true);
    const confirm = q<HTMLButtonElement>('.rp-same-provider-confirm')!;
    expect(confirm.textContent!.trim()).toBe('Write Anyway');
    expect(confirm.querySelector('svg.btn-icon')).not.toBeNull();
    confirm.click();
    fixture.detectChanges();
    expect(confirmDialog().open).toBe(false);

    const start = http.expectOne(START_URL);
    expect(start.request.method).toBe('POST');
    expect(start.request.body.acknowledgeSameProvider).toBe(true);
    expect(start.request.body.writerModelConfigurationId).toBe(7);
    expect(JSON.parse(localStorage.getItem(REPORT_PACK_STORAGE_KEY)!)).toEqual({ writerConfigId: 7 });

    start.flush({ jobId: 'job-1' });
    http.expectOne(jobUrl('job-1')).flush(jobDto());
    fixture.detectChanges();
    expect(host.querySelectorAll('.rp-job-row').length).toBe(2);
    fixture.destroy();
  }));

  it('opens the same confirmation when the server answers a start with a same-provider 409', fakeAsync(() => {
    openPanel();
    chooseWriter(7);
    generateButton().click();
    const message = 'The writer and Claude Opus are both from Anthropic. Acknowledge the warning to continue.';
    const first = http.expectOne(START_URL);
    expect(first.request.body.acknowledgeSameProvider).toBe(false);
    first.flush({ sameProvider: true, provider: 'Anthropic', testedModelDisplayName: 'Claude Opus', assessorModelDisplayName: 'Claude Opus writer', message }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(confirmDialog().open).toBe(true);
    expect(text('.rp-same-provider-confirm-text')).toBe(message);
    expect(text('.rp-same-provider')).toContain(message);
    expect(q('.rp-start-error')).toBeNull();

    q<HTMLButtonElement>('.rp-same-provider-confirm')!.click();
    fixture.detectChanges();
    const second = http.expectOne(START_URL);
    expect(second.request.body.acknowledgeSameProvider).toBe(true);
    second.flush({ jobId: 'job-1' });
    http.expectOne(jobUrl('job-1')).flush(jobDto());
    fixture.detectChanges();
    fixture.destroy();
  }));

  it('stops the close and cancel events of its nested dialogs', () => {
    openPanel();

    // The host element stands in for the wizard's dialog around the panel.
    const heard: string[] = [];
    host.addEventListener('close', () => heard.push('close'));
    host.addEventListener('cancel', () => heard.push('cancel'));

    const dialogs = Array.from(host.querySelectorAll('dialog'));
    expect(dialogs.length).toBeGreaterThanOrEqual(3);
    for (const dialog of dialogs) {
      // A real close event does not bubble; a bubbling one proves the handlers stop it.
      dialog.dispatchEvent(new Event('cancel', { bubbles: true, cancelable: true }));
      dialog.dispatchEvent(new Event('close', { bubbles: true }));
    }
    expect(heard).toEqual([]);
  });

  // -------------------------------------------------------------------------------------------
  // 409 and 429
  // -------------------------------------------------------------------------------------------

  it('shows a 409 running job in an alert and follows that job', fakeAsync(() => {
    openPanel();
    const finished: BenchmarkReportPackJobDto[] = [];
    component.jobFinished.subscribe(job => finished.push(job));
    chooseWriter(7);
    generateButton().click();
    const running = jobDto({ id: 'job-9', subjectLabel: 'GPT Sol group' });
    http.expectOne(START_URL).flush(running, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(q('.rp-start-error')!.getAttribute('role')).toBe('alert');
    expect(text('.rp-start-error')).toContain('Another report pack is being written, for GPT Sol group');
    expect(text('.rp-start-error')).toContain('its progress is shown here.');
    expect(host.querySelectorAll('.rp-job-row').length).toBe(2);
    expect(generateButton().disabled).toBe(true);
    // Generate stays disabled for the other job, so focus stays on the progress area.
    expect(document.activeElement).toBe(q('section.rrf-main'));

    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-9')).flush(jobDto({ id: 'job-9', status: 'Completed', completedAtUtc: '2026-09-28T10:01:00Z' }));
    fixture.detectChanges();
    expect(finished.map(job => job.id)).toEqual(['job-9']);
    fixture.destroy();
  }));

  it('shows a 429 spend-cap refusal with the server\'s message', fakeAsync(() => {
    openPanel();
    chooseWriter(7);
    generateButton().click();
    const message = 'The benchmark spend cap for today has been reached.';
    http.expectOne(START_URL).flush(message, { status: 429, statusText: 'Too Many Requests' });
    fixture.detectChanges();

    const alert = q('.rp-start-error')!;
    expect(alert.getAttribute('role')).toBe('alert');
    expect(alert.textContent!.trim()).toBe(message);
    expect(generateButton().disabled).toBe(false);
    // Generate can be used again, so focus returns to it from the progress area.
    expect(document.activeElement).toBe(generateButton());
    // The starting panel is gone; no job was started.
    expect(q('.rp-starting')).toBeNull();
    expect(text('.rp-idle-note')).toContain('step 4, Documents');
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // Progress
  // -------------------------------------------------------------------------------------------

  it('answers Generate at once: the starting panel, the live line and focus on the progress area', fakeAsync(() => {
    openPanel();
    chooseWriter(7);
    expect(q('.rp-starting')).toBeNull();

    // A real click; the start request has not answered yet.
    generateButton().click();
    fixture.detectChanges();
    const start = http.expectOne(START_URL);

    const panel = q('section.rrf-main .rp-starting')!;
    expect(panel).not.toBeNull();
    const indicator = panel.querySelector('app-loading-indicator')!;
    expect(indicator.classList).toContain('li-block');
    expect(indicator.classList).not.toContain('li-reveal');
    expect(text('.rp-starting app-loading-indicator')).toBe('Starting the report pack…');
    expect(text('.rp-starting-detail')).toBe(
      'Checking the comparison, the report writer and the cost, then queuing the documents. This can take several seconds.');
    expect(q('.rp-idle-note')).toBeNull();
    expect(q('.rp-job')).toBeNull();
    expect(text('.rp-job-status')).toBe('Starting the report pack…');
    expect(document.activeElement).toBe(q('section.rrf-main'));
    expect(generateButton().disabled).toBe(true);
    expect(generateButton().getAttribute('aria-busy')).toBe('true');
    expect(text('#rp-generate-blocked')).toBe(REPORT_PACK_JOB_RUNNING_REASON);
    expect(q('#rp-generate-blocked')!.classList).toContain('visually-hidden');

    start.flush({ jobId: 'job-1' });
    fixture.detectChanges();
    expect(q('.rp-starting')).not.toBeNull();
    http.expectOne(jobUrl('job-1')).flush(jobDto());
    fixture.detectChanges();
    expect(q('.rp-starting')).toBeNull();
    expect(q('.rp-job')).not.toBeNull();
    fixture.destroy();
  }));

  it('shows no job card until a job exists, with the live line in place', () => {
    openPanel();

    expect(q('.rp-job')).toBeNull();
    const line = q('.rp-job-status')!;
    expect(line.getAttribute('role')).toBe('status');
    expect(line.getAttribute('aria-live')).toBe('polite');
    expect(line.classList).toContain('visually-hidden');
    expect(line.textContent!.trim()).toBe('');
    expect(text('.rp-idle-note')).toContain('step 4, Documents');
    expect(component.jobRunning).toBe(false);
  });

  it('polls a started job with a stage rail, a stat strip and a row per document, then summarizes it above the strip', fakeAsync(() => {
    openPanel();
    const busy: boolean[] = [];
    const finished: BenchmarkReportPackJobDto[] = [];
    let documentsRequests = 0;
    component.busyChange.subscribe(value => busy.push(value));
    component.jobFinished.subscribe(job => finished.push(job));
    component.documentsRequested.subscribe(() => documentsRequests++);
    chooseWriter(7);
    generateButton().click();
    http.expectOne(START_URL).flush({ jobId: 'job-1' });
    fixture.detectChanges();
    // Before the first reading, the starting panel stands in for the job card, whose values are not known yet.
    expect(q('.rp-starting')).not.toBeNull();
    expect(q('.rp-job')).toBeNull();
    expect(text('.rp-job-status')).toBe('Starting the report pack…');
    expect(component.jobRunning).toBe(true);

    http.expectOne(jobUrl('job-1')).flush(jobDto());
    fixture.detectChanges();
    expect(q('.rp-starting')).toBeNull();

    expect(cellText(ExecutiveSummary, '.rp-doc-name')).toBe('Executive Summary');
    expect(cellText(ExecutiveSummary, '.job-status-chip')).toBe('Writing draft');
    expect(cellText(ExecutiveSummary, '.rp-doc-calls')).toBe('Model calls: 1');
    expect(cellText(RootCauseAnalysis, '.job-status-chip')).toBe('Pending');
    expect(q('.rp-job-status')!.getAttribute('role')).toBe('status');
    expect(q('.rp-job-status')!.classList).toContain('visually-hidden');
    expect(text('.rp-job-status')).toBe('Writing 2 documents for Gemini Flash: 0 of 2 finished.');
    expect(q('.rp-job-log')).not.toBeNull();
    expect(generateButton().disabled).toBe(true);
    // A running job's rows cannot be checked or deleted.
    expect(docCheck(`comparison|${ExecutiveSummary}`).disabled).toBe(true);

    const stages = Array.from(host.querySelectorAll('.rp-job-rail .run-stage'));
    expect(q('.rp-job-rail')!.classList).toContain('run-stage-rail');
    expect(stages.map(stage => stage.querySelector('.run-stage-name')!.textContent!.trim()))
      .toEqual(['Queued', 'Preparing', 'Executive Summary', 'Root Cause Analysis', 'Done']);
    expect(stages.map(stage => stage.classList.contains('is-done'))).toEqual([true, true, false, false, false]);
    expect(stages[2].classList).toContain('is-current');
    expect(stages[2].getAttribute('aria-current')).toBe('step');

    expect(q('.rp-job-stats')!.classList).toContain('run-stat-strip');
    expect(text('.rp-job-writer')).toBe('Claude Opus writer');
    expect(text('.rp-job-calls')).toBe('1');
    expect(text('.rp-job-tokens')).toBe('6,000 in · 0 out');
    expect(text('.rp-job-cost-label')).toBe('Cost so far');
    expect(text('.rp-job-estimate')).toBe('about $0.21');

    tick(REPORT_PACK_POLL_MS - 1);
    http.expectNone(jobUrl('job-1'));
    tick(1);
    http.expectOne(jobUrl('job-1')).flush(jobDto({
      totalModelCalls: 3,
      documents: [
        { audience: ExecutiveSummary, status: 'Completed', documentId: 21, errorMessage: null, modelCalls: 1 },
        { audience: RootCauseAnalysis, status: 'Repairing', documentId: null, errorMessage: null, modelCalls: 2 }
      ]
    }));
    fixture.detectChanges();
    expect(cellText(ExecutiveSummary, '.job-status-chip')).toBe('Completed');
    // The revision is a routine step, not a fault.
    expect(cellText(RootCauseAnalysis, '.job-status-chip')).toBe('Revising');
    expect(cellText(RootCauseAnalysis, '.rp-doc-calls')).toBe('Model calls: 2');
    expect(text('.rp-job-status')).toBe('Writing 2 documents for Gemini Flash: 1 of 2 finished.');
    expect(text('.rp-job-rail .run-stage.is-current .run-stage-name')).toBe('Root Cause Analysis');

    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-1')).flush(jobDto({
      status: 'Completed',
      completedAtUtc: '2026-09-28T10:02:05Z',
      costUsd: 0.13,
      documents: [
        { audience: ExecutiveSummary, status: 'Completed', documentId: 21, errorMessage: null, modelCalls: 1 },
        { audience: RootCauseAnalysis, status: 'CompletedWithWarnings', documentId: 22, errorMessage: null, modelCalls: 2 }
      ]
    }));
    fixture.detectChanges();

    expect(text('.rp-job-status')).toBe('Report pack for Gemini Flash: Completed.');
    expect(text('.rp-job-summary')).toBe('Written: 2 documents (1 with warnings), 2 min 05 s, $0.13');
    expect(q('.rp-job-rail')).toBeNull();
    // The strip stays, with Cost and the estimate, under the summary line and Dismiss.
    expect(q('.rp-job-stats')).not.toBeNull();
    expect(text('.rp-job-cost-label')).toBe('Cost');
    expect(text('.rp-job-cost')).toBe('$0.13');
    expect(text('.rp-job-estimate')).toBe('about $0.21');
    expect(q('.rp-job-summary-row')!.compareDocumentPosition(q('.rp-job-stats')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(q('.rp-cancel-job')).toBeNull();
    expect(finished.map(job => job.id)).toEqual(['job-1']);
    expect(busy).toEqual([true, false]);
    expect(component.jobRunning).toBe(false);
    // The documents it wrote are listed again.
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS);
    http.expectOne(PREVIEW_URL);
    tick(REPORT_PACK_POLL_MS * 3);
    http.expectNone(jobUrl('job-1'));

    // See the documents: the host shows step 4.
    const see = q<HTMLButtonElement>('.rp-see-documents')!;
    expect(see.textContent!.trim()).toBe('See the documents');
    see.click();
    expect(documentsRequests).toBe(1);

    // Dismiss removes the card; the live line empties with it.
    q<HTMLButtonElement>('.rp-dismiss-job')!.click();
    fixture.detectChanges();
    expect(q('.rp-job')).toBeNull();
    expect(text('.rp-job-status')).toBe('');
    expect(document.activeElement).toBe(q('section.rrf-main'));
    fixture.destroy();
  }));

  it('names each document\'s model in a per-model job of several models', fakeAsync(() => {
    openPanel({
      activeJob: jobDto({
        id: 'job-5',
        scope: BenchmarkReportScope.Model,
        documents: [
          { audience: ExecutiveSummary, status: 'Writing', documentId: null, errorMessage: null, modelCalls: 1, subjectKey: 'run:1', subjectLabel: 'Gemini Flash' },
          { audience: ExecutiveSummary, status: 'Pending', documentId: null, errorMessage: null, modelCalls: 0, subjectKey: 'group:4', subjectLabel: 'GPT Sol group' }
        ]
      })
    });

    expect(text('.rp-job-status')).toBe('Writing 2 documents for 2 models: 0 of 2 finished.');
    const rows = Array.from(host.querySelectorAll('.rp-job-row'));
    expect(rows.map(row => row.getAttribute('data-subject'))).toEqual(['run:1', 'group:4']);
    expect(rows.map(row => row.querySelector('.rp-doc-model')!.textContent!.replace(/\s+/g, ' ').trim()))
      .toEqual(['Model: Gemini Flash', 'Model: GPT Sol group']);
    expect(Array.from(host.querySelectorAll('.rp-job-rail .run-stage-name')).map(name => name.textContent!.trim()))
      .toEqual(['Queued', 'Preparing', 'Executive Summary — Gemini Flash', 'Executive Summary — GPT Sol group', 'Done']);
    fixture.destroy();
  }));

  it('cancels the running job, then reads its final state at once', fakeAsync(() => {
    openPanel();
    startJob();

    q<HTMLButtonElement>('.rp-cancel-job')!.click();
    fixture.detectChanges();
    const cancel = http.expectOne(`${jobUrl('job-1')}/cancel`);
    expect(cancel.request.method).toBe('POST');
    cancel.flush({ cancelled: true });
    http.expectOne(jobUrl('job-1')).flush(jobDto({ status: 'Canceled', completedAtUtc: '2026-09-28T10:00:30Z' }));
    fixture.detectChanges();

    expect(text('.rp-job-status')).toBe('Report pack for Gemini Flash: Canceled.');
    expect(text('.rp-job-summary')).toBe('Canceled: 0 of 2 documents written, 30 s, Unknown');
    expect(q('.rp-cancel-job')).toBeNull();
    // The rows are listed again before Generate can start another job.
    answerPreview();
    expect(generateButton().disabled).toBe(false);
    fixture.destroy();
  }));

  it('picks up a running job on creation and backs off when its progress cannot be read', fakeAsync(() => {
    openPanel({ activeJob: jobDto({ id: 'job-5' }) });

    expect(host.querySelectorAll('.rp-job-row').length).toBe(2);
    expect(text('#rp-generate-blocked')).toBe('A report pack is being written. Wait for it to finish, or cancel it.');
    // The main area shows the job and Cancel Job: the reason is out of sight, and still Generate's description.
    expect(q('#rp-generate-blocked')!.classList).toContain('visually-hidden');
    expect(generateButton().getAttribute('aria-describedby')).toBe('rp-estimate rp-generate-blocked');

    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-5')).flush('down', { status: 503, statusText: 'Service Unavailable' });
    fixture.detectChanges();
    expect(text('.rp-job')).toContain('The job’s progress could not be read. Retrying.');

    tick(REPORT_PACK_POLL_MS);
    http.expectNone(jobUrl('job-5'));
    tick(REPORT_PACK_POLL_MS * 2);
    http.expectOne(jobUrl('job-5')).flush(jobDto({ id: 'job-5', status: 'Failed', completedAtUtc: '2026-09-28T10:00:10Z' }));
    fixture.detectChanges();
    expect(text('.rp-job-summary')).toContain('Failed: 0 of 2 documents written');
    fixture.destroy();
  }));

  it('announces each written document once, those already written when it reattaches included', fakeAsync(() => {
    const announced: BenchmarkReportPackDocumentProgressDto[] = [];
    component.documentWritten.subscribe(doc => announced.push(doc));
    openPanel({
      activeJob: jobDto({
        id: 'job-5',
        documents: [written(ExecutiveSummary, 31), { audience: RootCauseAnalysis, status: 'Writing', documentId: null, errorMessage: null, modelCalls: 1 }]
      })
    });
    expect(announced.map(doc => doc.documentId)).toEqual([31]);

    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-5')).flush(jobDto({
      id: 'job-5',
      documents: [written(ExecutiveSummary, 31), { audience: RootCauseAnalysis, status: 'Failed', documentId: null, errorMessage: 'Refused.', modelCalls: 2 }]
    }));
    expect(announced.map(doc => doc.documentId)).toEqual([31]);

    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-5')).flush(jobDto({
      id: 'job-5',
      status: 'CompletedWithErrors',
      completedAtUtc: '2026-09-28T10:03:00Z',
      documents: [written(ExecutiveSummary, 31), written(RootCauseAnalysis, 32, 'CompletedWithWarnings')]
    }));
    expect(announced.map(doc => doc.documentId)).toEqual([31, 32]);
    expect(announced[1].status).toBe('CompletedWithWarnings');

    // A comparison change that finds the same job again announces nothing twice.
    fixture.componentRef.setInput('context', { ...CONTEXT, entryKeys: ['run:1', 'run:2'] });
    fixture.detectChanges();
    answerActiveJob(jobDto({ id: 'job-5', status: 'CompletedWithErrors', documents: [written(ExecutiveSummary, 31), written(RootCauseAnalysis, 32)] }));
    expect(announced.map(doc => doc.documentId)).toEqual([31, 32]);
    fixture.destroy();
  }));

  it('keeps polling and ticking while hidden, as when another step of the wizard shows', fakeAsync(() => {
    openPanel({ activeJob: jobDto({ id: 'job-5' }) });
    host.hidden = true;

    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-5')).flush(jobDto({ id: 'job-5' }));
    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-5')).flush(jobDto({ id: 'job-5' }));
    expect(component['tickSub']).not.toBeNull();
    fixture.destroy();
  }));

  it('stops polling and its clock when destroyed', fakeAsync(() => {
    openPanel({ activeJob: jobDto({ id: 'job-5' }) });
    expect(component['tickSub']).not.toBeNull();

    fixture.destroy();
    expect(component['tickSub']).toBeNull();
    tick(REPORT_PACK_POLL_MS * 10);
    http.expectNone(jobUrl('job-5'));
    http.expectNone(PREVIEW_URL);
  }));

  // -------------------------------------------------------------------------------------------
  // The document progress list (v1 Task 1)
  // -------------------------------------------------------------------------------------------

  it('lists each document with its model, a chip, a live duration and centered model calls, under an aria-hidden header', fakeAsync(() => {
    openPanel({
      activeJob: jobDto({
        id: 'job-5',
        serverTimeUtc: '2026-09-28T10:01:00Z',
        documents: [
          { audience: ExecutiveSummary, status: 'Writing', documentId: null, errorMessage: null, modelCalls: 1, startedAtUtc: '2026-09-28T10:00:30Z' },
          { audience: RootCauseAnalysis, status: 'Failed', documentId: null, errorMessage: 'The writer refused.', modelCalls: 2 }
        ]
      })
    });

    const list = q('ol.rp-doc-progress')!;
    expect(list.getAttribute('aria-label')).toBe('Progress of each document');
    const head = q('.rp-doc-progress-head')!;
    expect(head.getAttribute('aria-hidden')).toBe('true');
    // A per-model job names its model first.
    expect(Array.from(head.children).map(cell => cell.textContent!.trim()))
      .toEqual(['Model', 'Document', 'Status', 'Duration', 'Model calls', 'Charts']);
    expect(q('.rp-doc-progress-grid')!.classList).toContain('has-model');
    expect(list.querySelectorAll('li.rp-job-row').length).toBe(2);
    expect(cellText(ExecutiveSummary, '.rp-doc-model')).toBe('Model: Gemini Flash');

    const chip = q(`.rp-job-row[data-audience="${ExecutiveSummary}"] .job-status-chip`)!;
    expect(chip.classList).toContain('status-generating');
    expect(q(`.rp-job-row[data-audience="${RootCauseAnalysis}"] .job-status-chip`)!.classList).toContain('status-failed');
    expect(cellText(RootCauseAnalysis, '.rp-job-row-error')).toBe('The writer refused.');
    expect(cellText(ExecutiveSummary, '.rp-doc-status')).toBe('Status: Writing draft');
    expect(q(`.rp-job-row[data-audience="${ExecutiveSummary}"] .rp-doc-calls`)!.classList).toContain('rp-num');

    // On the server's clock: 30 s at its last reading, one more second later.
    expect(cellText(ExecutiveSummary, '.rp-doc-duration')).toBe('Duration: 30 s');
    expect(cellText(RootCauseAnalysis, '.rp-doc-duration')).toBe('Duration: —');
    tick(1000);
    fixture.detectChanges();
    expect(cellText(ExecutiveSummary, '.rp-doc-duration')).toBe('Duration: 31 s');
    fixture.destroy();
  }));

  it('names no model in a comparison-scope job\'s progress', fakeAsync(() => {
    openPanel({ activeJob: jobDto({ id: 'job-5', scope: BenchmarkReportScope.Comparison, subjectKey: 'comparison:12', subjectLabel: 'Comparison #12' }) });

    expect(Array.from(q('.rp-doc-progress-head')!.children).map(cell => cell.textContent!.trim()))
      .toEqual(['Document', 'Status', 'Duration', 'Model calls', 'Charts']);
    expect(q('.rp-doc-progress-grid')!.classList).not.toContain('has-model');
    expect(q('.rp-doc-model')).toBeNull();
    expect(text('.rp-job-status')).toBe('Writing 2 documents for Comparison #12: 0 of 2 finished.');
    fixture.destroy();
  }));

  it('shows each written document\'s chart state, with a retry after a failure', fakeAsync(() => {
    const retries: BenchmarkReportPackDocumentProgressDto[] = [];
    component.chartRetryRequested.subscribe(doc => retries.push(doc));
    const statuses: Record<number, ReportChartRowStatus> = {
      31: { state: 'attaching' },
      32: { state: 'done', count: 3, images: 6 },
      33: { state: 'failed', message: 'Disk full.' }
    };
    fixture.componentRef.setInput('chartStatus', statuses);
    openPanel({
      activeJob: jobDto({
        id: 'job-5',
        documents: [
          written(ExecutiveSummary, 31),
          { ...written(ExecutiveSummary, 32), subjectKey: 'group:4', subjectLabel: 'GPT Sol group' },
          written(RootCauseAnalysis, 33)
        ]
      })
    });
    const groupCharts = (): string => q('.rp-job-row[data-subject="group:4"] .rp-doc-charts')!.textContent!.replace(/\s+/g, ' ').trim();

    expect(cellText(ExecutiveSummary, '.rp-doc-charts')).toBe('Charts: attaching…');
    expect(groupCharts()).toBe('Charts: 3');
    const retry = q<HTMLButtonElement>(`.rp-job-row[data-audience="${RootCauseAnalysis}"] .rp-chart-retry`)!;
    expect(retry.textContent!.replace(/\s+/g, ' ').trim()).toBe('Charts failed — retry for the Root Cause Analysis of Gemini Flash');
    retry.click();
    expect(retries.map(doc => doc.documentId)).toEqual([33]);

    // No chart selected for a document type: none.
    fixture.componentRef.setInput('chartSelection', { ...DEFAULT_CHART_SELECTION, [ExecutiveSummary]: [] } as ReportChartSelection);
    fixture.detectChanges();
    expect(groupCharts()).toBe('Charts: none');

    // No chart storage: none everywhere.
    fixture.componentRef.setInput('chartStorageMissing', true);
    fixture.detectChanges();
    expect(cellText(ExecutiveSummary, '.rp-doc-charts')).toBe('Charts: none');
    expect(q('.rp-chart-retry')).toBeNull();
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // Diagnostics
  // -------------------------------------------------------------------------------------------

  it('copies the diagnostics from an icon-only button with a tooltip, and says so', fakeAsync(() => {
    const copy = vi.spyOn(reportPackIo, 'copy').mockResolvedValue(true);
    openPanel({ activeJob: jobDto({ id: 'job-5' }) });

    expect(text('.rp-job-log > summary')).toBe('Log and diagnostics');
    const button = q<HTMLButtonElement>('.rp-copy-diagnostics')!;
    expect(button.classList).toContain('action-btn');
    expect(button.getAttribute('aria-label')).toBe('Copy the report pack diagnostics for Gemini Flash');
    expect(button.getAttribute('interestfor')).toBe('rp-copy-diagnostics-tip');
    expect(button.hasAttribute('title')).toBe(false);
    expect(text('#rp-copy-diagnostics-tip')).toBe('Copy diagnostics');
    expect(q('#rp-copy-diagnostics-tip')!.getAttribute('popover')).toBe('hint');

    button.click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(copy).toHaveBeenCalledTimes(1);
    const copied = vi.mocked(copy).mock.lastCall![0];
    expect(copied).toContain('Overseer Report Pack diagnostics');
    expect(copied).toContain('Job id: job-5');
    expect(copied).not.toContain('user-secret-id');
    expect(copied).not.toContain('\r');
    const line = q('.rp-copy-status')!;
    expect(line.getAttribute('role')).toBe('status');
    expect(line.textContent!.trim()).toBe('Copied');
    expect(q('.rp-copy-error')).toBeNull();
    fixture.destroy();
  }));

  it('shows an inline error when the clipboard refuses the diagnostics', fakeAsync(() => {
    vi.spyOn(reportPackIo, 'copy').mockResolvedValue(false);
    openPanel({ activeJob: jobDto({ id: 'job-5' }) });

    q<HTMLButtonElement>('.rp-copy-diagnostics')!.click();
    flushMicrotasks();
    fixture.detectChanges();
    const error = q('.rp-copy-error')!;
    expect(error.classList).toContain('gh-field-error');
    expect(error.textContent).toContain('could not be copied');
    expect(text('.rp-copy-status')).toBe('');
    fixture.destroy();
  }));

  it('downloads the diagnostics as a text file named for the subject', fakeAsync(() => {
    const download = vi.spyOn(reportPackIo, 'download').mockReturnValue(undefined);
    openPanel({ activeJob: jobDto({ id: 'job-5' }) });

    const button = q<HTMLButtonElement>('.rp-download-diagnostics')!;
    expect(button.getAttribute('aria-label')).toBe('Download the report pack diagnostics for Gemini Flash');
    expect(text('#rp-download-diagnostics-tip')).toBe('Download diagnostics');
    button.click();

    expect(download).toHaveBeenCalledTimes(1);
    const [fileName, body] = vi.mocked(download).mock.lastCall!;
    expect(fileName).toMatch(/^report-pack_gemini-flash_diagnostics_\d{8}-\d{6}\.txt$/);
    expect(body).toContain('== Documents ==');
    expect(body).toContain('Polls: ');
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // The clock (v1 Task 3)
  // -------------------------------------------------------------------------------------------

  it('measures elapsed time on the server\'s clock, ticks each second while running, and keeps the live line still', fakeAsync(() => {
    openPanel({ activeJob: jobDto({ id: 'job-5', serverTimeUtc: '2026-09-28T10:01:00Z' }) });

    expect(text('.rp-job-elapsed')).toBe('1 min 00 s');
    const line = text('.rp-job-status');
    tick(1000);
    fixture.detectChanges();
    expect(text('.rp-job-elapsed')).toBe('1 min 01 s');
    expect(text('.rp-job-status')).toBe(line);

    // A new reading resets the clock to the server's.
    tick(REPORT_PACK_POLL_MS - 1000);
    http.expectOne(jobUrl('job-5')).flush(jobDto({ id: 'job-5', serverTimeUtc: '2026-09-28T10:01:30Z' }));
    fixture.detectChanges();
    expect(text('.rp-job-elapsed')).toBe('1 min 30 s');

    // Finished: the clock stops.
    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-5')).flush(jobDto({
      id: 'job-5', status: 'Completed', completedAtUtc: '2026-09-28T10:02:00Z', serverTimeUtc: '2026-09-28T10:02:01Z'
    }));
    fixture.detectChanges();
    expect(component['tickSub']).toBeNull();
    expect(text('.rp-job-elapsed')).toBe('2 min 00 s');
    fixture.destroy();
  }));

  it('falls back to the client\'s clock when the server sends no time of its own', fakeAsync(() => {
    const started = new Date(Date.now() - 42000).toISOString();
    openPanel({ activeJob: jobDto({ id: 'job-5', startedAtUtc: started }) });

    expect(text('.rp-job-elapsed')).toBe('42 s');
    tick(1000);
    fixture.detectChanges();
    expect(text('.rp-job-elapsed')).toBe('43 s');
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // Phases and validation outcomes
  // -------------------------------------------------------------------------------------------

  function issue(
    rule: number, ruleName: string, severity: string, locationLabel: string, message: string, dropped: boolean, issueText: string | null = null
  ): BenchmarkReportPackValidationIssueDto {
    return { rule, ruleName, severity, location: 'sections.conclusions[0]', locationLabel, message, dropped, text: issueText };
  }

  const RANKING = issue(16, 'Ranking without the overlap caveat', 'warning', 'Hypotheses: cost, paragraph 2',
    'The sentence ranks two models whose intervals overlap.', false);
  const DIGIT = issue(3, 'Digit in prose', 'removesItem', 'Conclusions, paragraph 1', 'The paragraph holds a number of its own.', true,
    'The model scored 72.');

  function call(kind: string): BenchmarkReportPackCallDto {
    return {
      kind, startedAtUtc: '2026-09-28T10:00:05Z', durationMs: 30000, inputTokens: 6000, cachedInputTokens: 0, outputTokens: 1500,
      costUsd: 0.05, finishReason: 'stop', requestMessages: kind === 'Draft' ? 2 : 4, requestCharacters: 20000
    };
  }

  it('shows each document\'s phase under its chip, and announces a phase change only when the phase changes', fakeAsync(() => {
    const pending: BenchmarkReportPackDocumentProgressDto = {
      audience: RootCauseAnalysis, status: 'Pending', documentId: null, errorMessage: null, modelCalls: 0, phase: ''
    };
    const writing: BenchmarkReportPackDocumentProgressDto = {
      audience: ExecutiveSummary, status: 'Writing', documentId: null, errorMessage: null, modelCalls: 1, phase: 'Writing'
    };
    openPanel({ activeJob: jobDto({ id: 'job-5', serverTimeUtc: '2026-09-28T10:01:00Z', documents: [writing, pending] }) });

    // Writing: the chip says it, so the row has no phase line; the live line still names the phase.
    expect(cellText(ExecutiveSummary, '.job-status-chip')).toBe('Writing draft');
    expect(q(`.rp-job-row[data-audience="${ExecutiveSummary}"] .rp-doc-phase`)).toBeNull();
    expect(q(`.rp-job-row[data-audience="${ExecutiveSummary}"] .rp-doc-phase-row`)).toBeNull();
    expect(q(`.rp-job-row[data-audience="${RootCauseAnalysis}"] .rp-doc-phase`)).toBeNull();
    const writingLine = 'Writing 2 documents for Gemini Flash: 0 of 2 finished. Executive Summary: Writing draft.';
    expect(text('.rp-job-status')).toBe(writingLine);
    expect(q('.rp-job-status')!.getAttribute('aria-live')).toBe('polite');

    // Neither the clock nor a reading in the same phase changes the live line.
    tick(1000);
    fixture.detectChanges();
    expect(text('.rp-job-status')).toBe(writingLine);
    tick(REPORT_PACK_POLL_MS - 1000);
    http.expectOne(jobUrl('job-5')).flush(jobDto({ id: 'job-5', serverTimeUtc: '2026-09-28T10:01:02Z', documents: [{ ...writing }, { ...pending }] }));
    fixture.detectChanges();
    expect(text('.rp-job-status')).toBe(writingLine);

    // Revising: the chip reads Revising and the row has no phase line; the live line counts the first draft's issues.
    const revising: BenchmarkReportPackDocumentProgressDto = {
      ...writing, status: 'Repairing', modelCalls: 2, phase: 'Revising', firstDraftClean: false, firstTurnIssues: [RANKING, DIGIT],
      phaseDetail: 'The automatic check found 2 issues in the first draft: 1 ranking without the overlap caveat, 1 digit in prose.'
    };
    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-5')).flush(jobDto({ id: 'job-5', documents: [revising, { ...pending }] }));
    fixture.detectChanges();
    expect(cellText(ExecutiveSummary, '.job-status-chip')).toBe('Revising');
    expect(q(`.rp-job-row[data-audience="${ExecutiveSummary}"] .rp-doc-phase`)).toBeNull();
    expect(text('.rp-job-status')).toBe(
      'Writing 2 documents for Gemini Flash: 0 of 2 finished. Executive Summary: Revising the draft — 2 issues found by the automatic check.');

    // The first draft's info button sits right after the chip while the document is written.
    const chipRow = (): HTMLElement => q(`.rp-job-row[data-audience="${ExecutiveSummary}"] .rp-doc-chip-row`)!;
    expect(chipRow().querySelector('.job-status-chip')!.nextElementSibling!.classList).toContain('rp-doc-draft');
    expect(host.querySelectorAll(`.rp-job-row[data-audience="${ExecutiveSummary}"] .rp-doc-draft`).length).toBe(1);

    // It lists the first draft's issues by rule in a dialog.
    const draftButton = q<HTMLButtonElement>(`.rp-job-row[data-audience="${ExecutiveSummary}"] .rp-doc-chip-row .rp-doc-draft button.gh-info-btn`)!;
    expect(draftButton.getAttribute('aria-label')).toBe('About the first draft check of the Executive Summary of Gemini Flash');
    expect(draftButton.hasAttribute('title')).toBe(false);
    draftButton.click();
    fixture.detectChanges();
    const draftDialog = q<HTMLDialogElement>(`.rp-job-row[data-audience="${ExecutiveSummary}"] .rp-doc-draft dialog`)!;
    expect(draftDialog.open).toBe(true);
    expect(draftDialog.querySelector('.report-validation-summary')!.textContent!.trim())
      .toBe('The automatic check found 2 issues in the first draft: 1 ranking without the overlap caveat, 1 digit in prose.');
    expect(draftDialog.querySelectorAll('.report-validation-group').length).toBe(2);
    draftDialog.close();

    // Checking the revision: a phase line the chip does not say, and the info button stays beside the chip.
    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-5')).flush(jobDto({
      id: 'job-5', documents: [{ ...revising, modelCalls: 3, phase: 'CheckingRevision' }, { ...pending }]
    }));
    fixture.detectChanges();
    expect(cellText(ExecutiveSummary, '.job-status-chip')).toBe('Revising');
    expect(cellText(ExecutiveSummary, '.rp-doc-phase')).toBe('Checking the revision — 2 issues found by the automatic check');
    expect(text('.rp-job-status')).toBe(
      'Writing 2 documents for Gemini Flash: 0 of 2 finished. Executive Summary: Checking the revision — 2 issues found by the automatic check.');
    expect(chipRow().querySelector('.job-status-chip')!.nextElementSibling!.classList).toContain('rp-doc-draft');
    expect(q(`.rp-job-row[data-audience="${ExecutiveSummary}"] .rp-doc-phase-row .rp-doc-draft`)).toBeNull();
    // The phase line itself is not live: the one live line announces the change.
    expect(q('.rp-doc-phase')!.closest('[role="status"], [aria-live]')).toBeNull();

    // Finished: the phase line says how each draft was checked, its info button beside it.
    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-5')).flush(jobDto({
      id: 'job-5',
      status: 'Completed',
      completedAtUtc: '2026-09-28T10:03:00Z',
      documents: [
        {
          ...written(ExecutiveSummary, 31, 'CompletedWithWarnings'), modelCalls: 2, phase: 'Done', firstDraftClean: false,
          firstTurnIssues: [RANKING, DIGIT], finalIssues: [DIGIT, RANKING], calls: [call('Draft'), call('Revision')]
        },
        { ...written(RootCauseAnalysis, 32), phase: 'Done', firstDraftClean: true, firstTurnIssues: [], finalIssues: [], calls: [call('Draft')] }
      ]
    }));
    fixture.detectChanges();
    expect(cellText(ExecutiveSummary, '.rp-doc-phase')).toBe('Revised once — 2 issues in the first draft');
    expect(q(`.rp-job-row[data-audience="${ExecutiveSummary}"] .rp-doc-phase-row .rp-doc-draft`)).not.toBeNull();
    expect(q(`.rp-job-row[data-audience="${ExecutiveSummary}"] .rp-doc-chip-row .rp-doc-draft`)).toBeNull();
    expect(cellText(RootCauseAnalysis, '.rp-doc-phase')).toBe('First draft passed every check');
    expect(q(`.rp-job-row[data-audience="${RootCauseAnalysis}"] .rp-doc-draft`)).toBeNull();
    expect(text('.rp-job-status')).toBe('Report pack for Gemini Flash: Completed.');
    fixture.destroy();
  }));

  it('explains a document completed with warnings, and a failed one, in a dialog whose heading takes focus', fakeAsync(() => {
    openPanel({
      activeJob: jobDto({
        id: 'job-5',
        status: 'CompletedWithErrors',
        completedAtUtc: '2026-09-28T10:03:00Z',
        documents: [
          {
            ...written(ExecutiveSummary, 31, 'CompletedWithWarnings'), modelCalls: 2, firstDraftClean: false,
            firstTurnIssues: [RANKING, DIGIT], finalIssues: [DIGIT, RANKING], calls: [call('Draft'), call('Revision')]
          },
          {
            audience: RootCauseAnalysis, status: 'Failed', documentId: null, errorMessage: 'The headline is missing.', modelCalls: 2,
            firstDraftClean: false, firstTurnIssues: [], finalIssues: [], failedReply: '{"headline": ""}'
          }
        ]
      })
    });

    expect(text('.rp-job-summary')).toContain('Written with errors: 1 of 2 documents (1 with warnings)');
    const row = (audience: BenchmarkReportAudience): HTMLElement => q(`.rp-job-row[data-audience="${audience}"]`)!;
    const why = row(ExecutiveSummary).querySelector<HTMLButtonElement>('.rp-doc-why button.gh-info-btn')!;
    expect(why.getAttribute('aria-label')).toBe('About warnings in the Executive Summary of Gemini Flash');
    expect(why.getAttribute('aria-haspopup')).toBe('dialog');
    expect(why.hasAttribute('title')).toBe(false);

    why.click();
    fixture.detectChanges();
    const dialog = row(ExecutiveSummary).querySelector<HTMLDialogElement>('.rp-doc-why dialog')!;
    expect(dialog.open).toBe(true);
    const heading = dialog.querySelector('h3')!;
    expect(heading.textContent!.trim()).toBe('Why the Executive Summary of Gemini Flash was completed with warnings');
    expect(dialog.getAttribute('aria-labelledby')).toBe(heading.id);
    expect(document.activeElement).toBe(heading);
    expect(dialog.querySelector('.report-validation-summary')!.textContent!.trim())
      .toBe('This document is usable. One paragraph was removed and one warning was kept.');
    expect(dialog.querySelector('.report-validation-removed .report-validation-quote')!.textContent).toBe('The model scored 72.');
    dialog.close();

    const failed = row(RootCauseAnalysis).querySelector<HTMLButtonElement>('.rp-doc-why button.gh-info-btn')!;
    expect(failed.getAttribute('aria-label')).toBe('About failure of the Root Cause Analysis of Gemini Flash');
    failed.click();
    fixture.detectChanges();
    const failedDialog = row(RootCauseAnalysis).querySelector<HTMLDialogElement>('.rp-doc-why dialog')!;
    expect(failedDialog.querySelector('h3')!.textContent!.trim()).toBe('Why the Root Cause Analysis of Gemini Flash failed');
    expect(failedDialog.querySelector('.report-validation-summary')!.textContent!.trim()).toBe('This document could not be stored.');
    expect(failedDialog.querySelector('.report-validation-reply')!.textContent).toBe('{"headline": ""}');
    failedDialog.close();
    fixture.destroy();
  }));

  it('styles only a warning entry of the log as a warning, whatever the case the server sends', fakeAsync(() => {
    openPanel({
      activeJob: jobDto({
        id: 'job-5',
        log: [
          { timestampUtc: '2026-09-28T10:00:00Z', message: 'Started.', severity: 'info' },
          { timestampUtc: '2026-09-28T10:00:40Z', message: 'Revising the Executive Summary.', severity: 'warning' },
          { timestampUtc: '2026-09-28T10:00:50Z', message: 'Executive Summary written.', severity: 'Info' }
        ]
      })
    });

    const entries = Array.from(host.querySelectorAll('.rp-log-list li'));
    expect(entries.map(entry => entry.classList.contains('rp-log-warning'))).toEqual([false, true, false]);
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // Charts in PDF and Word
  // -------------------------------------------------------------------------------------------

  it('holds the chart picker between the documents and Report writer, its segments following the documents checked', fakeAsync(() => {
    openPanel();
    answerPreview(comparisonPreview({ writtenDocuments: [writtenDoc(RootCauseAnalysis, 43)] }));
    answerPreview(comparisonPreview({ writtenDocuments: [writtenDoc(RootCauseAnalysis, 43)] }));
    const selections: ReportChartSelection[] = [];
    component.chartSelectionChange.subscribe(selection => selections.push(selection));

    const fieldset = q('fieldset.rp-charts-choice')!;
    expect(fieldset.querySelector('legend')!.textContent!.trim()).toBe('Charts in PDF and Word');
    const chartPicker = fixture.debugElement.query(By.directive(ReportChartPickerComponent)).componentInstance as ReportChartPickerComponent;
    expect(chartPicker.enabledAudiences).toEqual([ExecutiveSummary]);
    expect(chartPicker.audiences).toEqual(ALL_AUDIENCES);
    expect(chartPicker.available).toEqual(REPORT_CHART_FIGURES.map(figure => figure.key));
    expect(chartPicker.idPrefix).toBe('rp-charts');
    expect(chartPicker.scope).toBe('comparison');
    q<HTMLButtonElement>(`#rp-charts-tab-${RootCauseAnalysis}`)!.click();
    fixture.detectChanges();
    expect(q(`#rp-charts-${RootCauseAnalysis}-p1b-speed`)!.getAttribute('aria-disabled')).toBe('true');
    expect(text(`#rp-charts-${RootCauseAnalysis}-p1b-speed-placement`)).toBe('Evidence: speed');
    // The comparison-scope Root Cause Analysis places no Intelligence bars.
    expect(text(`#rp-charts-${RootCauseAnalysis}-p1a-quality-placement`)).toBe(REPORT_CHART_NOT_DRAWN);
    expect(text('.rcp-server-note')).toContain('four charts drawn by Overseer');

    // Rewrite the Root Cause Analysis: its charts can be chosen.
    docCheck(`comparison|${RootCauseAnalysis}`).click();
    fixture.detectChanges();
    expect(chartPicker.enabledAudiences).toEqual(ALL_AUDIENCES);
    expect(q(`#rp-charts-${RootCauseAnalysis}-p1b-speed`)!.hasAttribute('aria-disabled')).toBe(false);
    expect(q(`#rp-charts-${RootCauseAnalysis}-p1a-quality`)!.getAttribute('aria-disabled')).toBe('true');

    q<HTMLButtonElement>(`#rp-charts-tab-${ExecutiveSummary}`)!.click();
    fixture.detectChanges();
    q<HTMLInputElement>(`#rp-charts-${ExecutiveSummary}-p1b-speed`)!.click();
    expect(selections.length).toBe(1);
    expect(selections[0][ExecutiveSummary]).toEqual(['p1a-quality', 'p1b-speed', 's2-quality-cost']);

    // One model at a time shows the per-model sections.
    selectScope('model');
    expect(chartPicker.scope).toBe('model');

    expect(q('.rp-chart-advisory')).toBeNull();
    expect(q('.rp-chart-storage-missing')).toBeNull();
    fixture.destroy();
  }));

  it('shows the chart advisory and the missing chart storage as visible warnings', () => {
    fixture.componentRef.setInput('chartAdvisory', 'The charts use today\'s prices; the documents were written at run-time prices.');
    fixture.componentRef.setInput('chartStorageMissing', true);
    openPanel();

    const advisory = q('.rp-chart-advisory')!;
    expect(advisory.classList).toContain('alert-warning');
    expect(advisory.textContent).toContain('The charts use today\'s prices');
    const missing = q('.rp-chart-storage-missing')!;
    expect(missing.classList).toContain('alert-warning');
    expect(missing.textContent!.trim()).toBe('Chart storage is not configured; documents will be written without charts.');
    expect(q('fieldset.rp-charts-choice')!.contains(missing)).toBe(true);
  });

  it('lists a figure the comparison cannot draw with the reason', () => {
    fixture.componentRef.setInput('chartsAvailable', REPORT_CHART_FIGURES.map(figure => figure.key).filter(key => key !== 'p2-profile'));
    openPanel();
    q<HTMLButtonElement>(`#rp-charts-tab-${ExecutiveSummary}`)!.click();
    fixture.detectChanges();

    expect(q(`#rp-charts-${ExecutiveSummary}-p2-profile`)!.getAttribute('aria-disabled')).toBe('true');
    expect(text('#rp-charts-row-p2-profile-reason')).toBe('needs three or more models');
  });

  describe('with a layout', () => {
    const COMPOSED: ComposedDocumentCharts = {
      charts: [{
        key: 'p1a-quality',
        chart: { png: new Blob(['png'], { type: 'image/png' }), widthPx: 2008, heightPx: 1255, title: 'Intelligence', caption: 'Drawn.', altText: 'Bars.' }
      }],
      failed: [{ key: 's2-quality-cost', message: 'it does not fit' }],
      layout: { version: 1, figures: [{ key: 'p1a-quality', widthShare: 1, rowGroup: null }], maxHeightShare: 0.6 }
    };

    beforeEach(() => {
      fixture.componentRef.setInput('chartLayout', DEFAULT_CHART_LAYOUT_SETTINGS);
    });

    const previewTrigger = (): HTMLButtonElement => q<HTMLButtonElement>('.rp-preview-layout')!;
    const previewMenu = (): HTMLElement => q('#rp-preview-menu')!;
    const previewItem = (audience: BenchmarkReportAudience): HTMLButtonElement =>
      q<HTMLButtonElement>(`#rp-preview-menu .gh-action-popover-item[data-audience="${audience}"]`)!;
    const menuOpen = (): boolean => previewMenu().matches(':popover-open');
    const openPreviewMenu = async (): Promise<void> => {
      previewTrigger().click();
      await until(() => menuOpen());
      // The toggle event is queued after the popover opens.
      await new Promise(resolve => setTimeout(resolve));
      fixture.detectChanges();
    };
    const usablePanel = (compose: DocumentChartsComposer = vi.fn() as unknown as DocumentChartsComposer): void => {
      fixture.componentRef.setInput('documentChartsComposer', compose);
      fixture.componentRef.setInput('comparisonId', 12);
      openPanel();
    };

    it('passes the layout to the picker and its changes to the host', fakeAsync(() => {
      openPanel();
      const layouts: ReportChartLayoutSettings[] = [];
      component.chartLayoutChange.subscribe(layout => layouts.push(layout));
      const chartPicker = fixture.debugElement.query(By.directive(ReportChartPickerComponent)).componentInstance as ReportChartPickerComponent;
      expect(chartPicker.layout).not.toBeNull();

      chartPicker.layoutChange.emit(DEFAULT_CHART_LAYOUT_SETTINGS);
      expect(layouts).toEqual([DEFAULT_CHART_LAYOUT_SETTINGS]);
      fixture.destroy();
    }));

    it('offers Preview layout beside Generate, aria-disabled with its reason while no document can be previewed', fakeAsync(() => {
      openPanel();
      answerPreview(comparisonPreview({ comparisonId: null }));

      const button = previewTrigger();
      expect(button.tagName).toBe('BUTTON');
      expect(button.classList).toContain('btn-gh');
      expect(button.closest('.rp-actions-row')).not.toBeNull();
      expect(button.closest('app-report-chart-picker')).toBeNull();
      expect(button.closest('.rp-actions-row')!.querySelector('.rp-generate')).not.toBeNull();
      // Generate stays last in the row.
      expect(button.compareDocumentPosition(generateButton()) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(button.querySelectorAll('svg.btn-icon').length).toBe(2);
      expect(button.textContent!.replace(/\s+/g, ' ').trim()).toBe('Preview layout');
      expect(button.getAttribute('aria-disabled')).toBe('true');
      expect(button.hasAttribute('popovertarget')).toBe(false);
      expect(button.hasAttribute('aria-expanded')).toBe(false);
      expect(text(`#${button.getAttribute('aria-describedby')}`)).toBe('The charts cannot be drawn here.');

      fixture.componentRef.setInput('documentChartsComposer', vi.fn());
      fixture.detectChanges();
      expect(text('#rp-preview-layout-reason')).toBe('The comparison has no number yet, so its layout cannot be previewed.');

      fixture.componentRef.setInput('chartStorageMissing', true);
      fixture.detectChanges();
      expect(text('#rp-preview-layout-reason')).toBe('Chart storage is not configured, so there are no charts to preview.');

      fixture.componentRef.setInput('chartStorageMissing', false);
      fixture.componentRef.setInput('comparisonId', 12);
      fixture.detectChanges();
      expect(button.hasAttribute('aria-disabled')).toBe(false);
      expect(button.getAttribute('popovertarget')).toBe('rp-preview-menu');
      expect(button.getAttribute('aria-expanded')).toBe('false');
      expect(q('#rp-preview-layout-reason')).toBeNull();
      fixture.destroy();
    }));

    it('lists every document type with its chart count, and holds back one without a chosen chart', async () => {
      fixture.componentRef.setInput('chartSelection', { ...DEFAULT_CHART_SELECTION, [RootCauseAnalysis]: [] } as ReportChartSelection);
      const open = vi.fn();
      usablePanel();
      vi.spyOn(component.layoutViewer!, 'open').mockImplementation(open);

      const menu = previewMenu();
      expect(menu.getAttribute('popover')).toBe('auto');
      expect(menu.getAttribute('role')).toBe('group');
      expect(menu.getAttribute('aria-label')).toBe('Preview the chart layout of a document');
      const labels = Array.from(menu.querySelectorAll('.gh-action-popover-item > span:first-child'))
        .map(span => (span.textContent ?? '').replace(/\s+/g, ' ').trim());
      expect(labels).toEqual([
        'Executive Summary · 2 charts',
        `${component.audienceLabel(RootCauseAnalysis)} · 0 charts`
      ]);

      const held = previewItem(RootCauseAnalysis);
      expect(held.getAttribute('aria-disabled')).toBe('true');
      expect(held.querySelector('.gh-action-popover-item-reason')!.textContent!.trim())
        .toBe(`No chart is chosen for the ${component.audienceLabel(RootCauseAnalysis)}.`);
      expect(previewItem(ExecutiveSummary).hasAttribute('aria-disabled')).toBe(false);

      await openPreviewMenu();
      held.click();
      expect(open).not.toHaveBeenCalled();
      expect(menuOpen()).toBe(true);
    });

    it('opens its popover with focus on the current document type, and Escape closes only the popover', async () => {
      usablePanel();
      const outside = vi.fn();
      host.parentElement!.addEventListener('keydown', outside);

      await openPreviewMenu();
      expect(previewTrigger().getAttribute('aria-expanded')).toBe('true');
      expect(document.activeElement).toBe(previewItem(component.chartPreviewAudience));

      const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      previewItem(component.chartPreviewAudience).dispatchEvent(escape);
      await new Promise(resolve => setTimeout(resolve));
      fixture.detectChanges();

      expect(escape.defaultPrevented).toBe(true);
      expect(outside).not.toHaveBeenCalled();
      expect(menuOpen()).toBe(false);
      expect(previewTrigger().getAttribute('aria-expanded')).toBe('false');
      expect(document.activeElement).toBe(previewTrigger());
      host.parentElement!.removeEventListener('keydown', outside);
    });

    it('composes the covered models\' figures through the host and opens the layout preview PDF of the type chosen', async () => {
      const compose = vi.fn().mockResolvedValue(COMPOSED);
      usablePanel(compose as unknown as DocumentChartsComposer);
      chooseModels(['run:1', 'group:4']);
      const open = vi.spyOn(component.layoutViewer!, 'open').mockReturnValue(undefined);

      await openPreviewMenu();
      previewItem(RootCauseAnalysis).click();
      fixture.detectChanges();
      expect(open).toHaveBeenCalledTimes(1);
      expect(menuOpen()).toBe(false);
      const request = vi.mocked(open).mock.lastCall![0] as PdfViewerRequest;
      expect(request.title).toBe('Layout preview — Root Cause Analysis');

      // Composed and sent as the viewer loads it.
      let bytes: Uint8Array | null = null;
      request.load(null).subscribe(file => (bytes = file.bytes));
      let post: TestRequest | null = null;
      await until(() => {
        post = http.match(LAYOUT_PREVIEW_URL)[0] ?? null;
        return post !== null;
      });
      expect(compose).toHaveBeenCalledWith(RootCauseAnalysis, { kind: 'named', coveredKeys: ['run:1', 'group:4'] }, 'comparison');
      const sent = post! as TestRequest;
      const body = JSON.parse((sent.request.body as FormData).get('request') as string);
      expect(body.scope).toBe(BenchmarkReportScope.Comparison);
      expect(body.coveredEntryKeys).toEqual(['run:1', 'group:4']);
      expect(body.audience).toBe(RootCauseAnalysis);
      expect(body.audiences).toEqual([RootCauseAnalysis]);
      expect(body.naming).toBe('named');
      expect(body.layout).toEqual(COMPOSED.layout);
      expect((sent.request.body as FormData).getAll('files').length).toBe(1);
      sent.flush(new Blob([new Uint8Array([37, 80, 68, 70])], { type: 'application/pdf' }));
      await until(() => bytes !== null);
      fixture.detectChanges();
      expect(Array.from(bytes!)).toEqual([37, 80, 68, 70]);
      expect(text('.rp-layout-preview-note')).toBe('Not drawn: Intelligence against cost (it does not fit)');

      // The viewer closed: focus returns to Preview layout.
      component.onLayoutViewerClosed();
      expect(document.activeElement).toBe(previewTrigger());
    });
  });

  // -------------------------------------------------------------------------------------------
  // The reference model
  // -------------------------------------------------------------------------------------------

  describe('the reference model', () => {
    /** The comparison with GPT Sol group as its charted baseline model. */
    const BASELINE_CONTEXT: ReportPackContext = { ...CONTEXT, baselineModelEntryKey: 'group:4' };

    /** The Reference model picker's trigger. */
    const referenceTrigger = (): HTMLButtonElement => q<HTMLButtonElement>('#rp-reference')!;
    /** The name the trigger shows: the chosen model's, or *Highest Intelligence Index*. */
    const referenceShown = (): HTMLElement => referenceTrigger().querySelector<HTMLElement>('.model-name')!;
    const referenceOptions = (): HTMLElement[] =>
      Array.from(host.querySelectorAll<HTMLElement>('.rp-reference-picker [role="option"]'));
    const optionName = (option: HTMLElement): string => option.querySelector('.model-name')!.textContent!.trim();

    function openReference(): void {
      referenceTrigger().click();
      fixture.detectChanges();
    }

    function closeReference(): void {
      q('.rp-reference-picker [role="listbox"]')!.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      fixture.detectChanges();
    }

    /** Opens the picker and clicks the option of that visible name. */
    function chooseReference(name: string): void {
      openReference();
      const option = referenceOptions().find(candidate => optionName(candidate) === name);
      expect(option).toBeDefined();
      option!.click();
      fixture.detectChanges();
    }

    it('defaults to the baseline model when it is covered, and sends it', fakeAsync(() => {
      openPanel({ context: BASELINE_CONTEXT });
      const request = chooseWriter(7);

      expect(text('#rp-reference-label')).toBe('Reference model');
      expect(referenceTrigger().getAttribute('aria-labelledby')).toBe('rp-reference-label rp-reference');
      expect(referenceTrigger().getAttribute('aria-describedby')).toBe('rp-reference-hint');
      // The trigger shows the baseline model with its role and its thinking level as a badge.
      expect(referenceShown().textContent!.trim()).toBe('GPT Sol group');
      expect(referenceTrigger().querySelector('.model-option-tag')!.textContent!.trim()).toBe('Baseline');
      expect(referenceTrigger().querySelector('.thinking-badge')!.textContent!.toLowerCase()).toContain('medium');

      openReference();
      const options = referenceOptions();
      expect(options.map(optionName)).toEqual(['Highest Intelligence Index', 'Gemini Flash', 'Claude Opus', 'GPT Sol group']);
      expect(options.map(option => option.getAttribute('aria-selected'))).toEqual(['false', 'false', 'false', 'true']);
      expect(options.map(option => option.querySelector('.model-option-tag')?.textContent?.trim() ?? null))
        .toEqual([null, null, null, 'Baseline']);
      expect(options.slice(1).every(option => option.querySelector('.thinking-badge') !== null)).toBe(true);
      // Highest Intelligence Index is a setting of its own, drawn at full strength.
      expect(options[0].querySelector('.model-name')!.classList).not.toContain('text-muted');
      closeReference();
      expect(request.request.body.referenceEntryKey).toBe('group:4');

      generateButton().click();
      const start = http.expectOne(START_URL);
      expect(start.request.body.referenceEntryKey).toBe('group:4');
      fixture.destroy();
    }));

    it('sends a reference only when one is named', fakeAsync(() => {
      openPanel();
      const first = chooseWriter(7);
      expect(referenceShown().textContent!.trim()).toBe('Highest Intelligence Index');
      expect(referenceShown().classList).not.toContain('text-muted');
      expect('referenceEntryKey' in first.request.body).toBe(false);

      chooseReference('Claude Opus');
      expect(referenceShown().textContent!.trim()).toBe('Claude Opus');
      expect(referenceTrigger().querySelector('.thinking-badge')).not.toBeNull();
      expect(referenceTrigger().querySelector('.model-option-tag')).toBeNull();
      const named = answerPreview();
      expect(named.request.body.referenceEntryKey).toBe('run:2');

      chooseReference('Highest Intelligence Index');
      expect(referenceShown().textContent!.trim()).toBe('Highest Intelligence Index');
      expect(referenceShown().classList).not.toContain('text-muted');
      const unnamed = answerPreview();
      expect('referenceEntryKey' in unnamed.request.body).toBe(false);
      fixture.destroy();
    }));

    it('falls back to the highest Intelligence Index when the baseline model is not covered', fakeAsync(() => {
      openPanel({ context: BASELINE_CONTEXT });
      chooseWriter(7);
      chooseModels(['run:1', 'run:2']);

      expect(referenceShown().textContent!.trim()).toBe('Highest Intelligence Index');
      const request = answerPreview();
      expect('referenceEntryKey' in request.request.body).toBe(false);
      fixture.destroy();
    }));

    it('is not offered for per-model documents, which send no reference', fakeAsync(() => {
      openPanel({ context: BASELINE_CONTEXT });
      selectScope('model');

      expect(q('#rp-reference')).toBeNull();
      const request = answerPreview(modelPreview());
      expect('referenceEntryKey' in request.request.body).toBe(false);
      fixture.destroy();
    }));

    it('shows each written document\'s reference, with an amber note when it is not the one chosen', fakeAsync(() => {
      openPanel({ context: BASELINE_CONTEXT });
      const written = [
        { ...writtenDoc(ExecutiveSummary, 41), referenceEntryKey: 'group:4' },
        { ...writtenDoc(RootCauseAnalysis, 42), referenceEntryKey: null }
      ];
      chooseWriter(7, comparisonPreview({ writtenDocuments: written }));
      answerPreview(comparisonPreview({ estimates: COMPARISON_ESTIMATES.slice(2), writtenDocuments: written }));

      expect(text('#rp-written-references-title')).toBe('Reference of the written documents');
      const rows = Array.from(host.querySelectorAll('.rp-written-reference'));
      expect(rows.map(row => row.querySelector('dt')!.textContent!.trim()))
        .toEqual(['Executive Summary', 'Root Cause Analysis']);
      expect(text(`.rp-written-reference[data-row="comparison|${ExecutiveSummary}"] dd`)).toBe('GPT Sol group (medium) (baseline model)');
      expect(text(`.rp-written-reference[data-row="comparison|${RootCauseAnalysis}"] dd`))
        .toBe('Highest Intelligence Index · not the one chosen');
      const note = q('.rp-reference-mismatch')!;
      expect(note.classList).toContain('alert-warning');
      expect(note.getAttribute('role')).toBe('note');
      expect(text('.rp-reference-mismatch')).toBe(
        'A written document was written against another reference model than the one chosen: Root Cause Analysis '
        + '(Highest Intelligence Index). Check Rewrite to write it against GPT Sol group (medium) (baseline model).');

      // The same reference as the newer document's: the note names the other one.
      chooseReference('Highest Intelligence Index');
      expect(text('.rp-reference-mismatch')).toContain('Executive Summary (GPT Sol group (medium) (baseline model))');
      fixture.destroy();
    }));

    it('keeps the baseline model among more than 12 models, and says so', () => {
      const entries = Array.from({ length: 14 }, (_, i) => entry(`run:${i + 1}`, `Model ${i + 1}`, 50 + i));
      openPanel({
        context: {
          ...CONTEXT, runIds: entries.map((_, i) => i + 1), groupIds: [], entries, entryKeys: entries.map(e => e.key),
          baselineModelEntryKey: 'run:1'
        }
      });

      expect(component.chosenKeys.length).toBe(12);
      expect(component.chosenKeys).toContain('run:1');
      expect(component.chosenKeys).not.toContain('run:2');
      expect(component.chosenKeys).not.toContain('run:3');
      expect(component.referenceEntryKey).toBe('run:1');
      expect(referenceShown().textContent!.trim()).toBe('Model 1');
      expect(text('.rp-models-cap')).toBe('A document covers at most 12 models, so the baseline model and the 11 others '
        + 'with the highest Intelligence Index of the 14 are chosen first.');
    });
  });

  it('says in the job card to keep the wizard open while charts can be pending, never under Generate', fakeAsync(() => {
    openPanel();
    // No job: no note, in the sidebar or anywhere else.
    expect(q('.rp-keep-open')).toBeNull();
    expect(REPORT_PACK_KEEP_OPEN_TEXT).toBe('Keep this wizard open until every document shows its charts: this browser '
      + 'draws them as each document finishes. If you close it, add them later on step 4 with Update charts…');

    // While the job runs, under the card's title.
    startJob();
    const note = q('section.rp-job .rp-keep-open')!;
    expect(text('section.rp-job .rp-keep-open')).toBe(REPORT_PACK_KEEP_OPEN_TEXT);
    expect(q('.rrf-sidebar .rp-keep-open')).toBeNull();
    expect(q('.rp-job-title')!.compareDocumentPosition(note) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    // Finished, while the last document's charts are still being attached.
    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-1')).flush(jobDto({
      status: 'Completed',
      completedAtUtc: '2026-09-28T10:02:05Z',
      documents: [written(ExecutiveSummary, 21), written(RootCauseAnalysis, 22)]
    }));
    fixture.componentRef.setInput('chartStatus', { 21: { state: 'done', count: 2, images: 4 }, 22: { state: 'attaching' } });
    fixture.detectChanges();
    expect(q('.rp-keep-open')).not.toBeNull();

    // Every document shows its charts: nothing is pending any more.
    fixture.componentRef.setInput('chartStatus', { 21: { state: 'done', count: 2, images: 4 }, 22: { state: 'done', count: 3, images: 6 } });
    fixture.detectChanges();
    expect(q('.rp-keep-open')).toBeNull();
    // The documents are listed again: the finished job, then the charted ones.
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS);
    http.match(PREVIEW_URL);
    http.match(r => r.url === DOCUMENTS_URL);
    fixture.destroy();
  }));

  it('never says to keep the wizard open without chart storage', fakeAsync(() => {
    fixture.componentRef.setInput('chartStorageMissing', true);
    openPanel();
    startJob();
    expect(q('section.rp-job')).not.toBeNull();
    expect(q('.rp-keep-open')).toBeNull();
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // The grader guide
  // -------------------------------------------------------------------------------------------

  it('asks for the grader guide from the writer field\'s link, and explains the writer per document in a dialog', () => {
    openPanel();
    const requests: number[] = [];
    component.graderGuideRequested.subscribe(() => requests.push(1));

    const link = q<HTMLButtonElement>('.rp-guide-link')!;
    expect(link.textContent!.trim()).toBe('More: How the graders work');
    link.click();
    expect(requests.length).toBe(1);

    const infoButton = q<HTMLButtonElement>('.gh-field-row app-info-tip button.gh-info-btn')!;
    expect(infoButton.getAttribute('aria-label')).toBe('About Report writer');
    expect(infoButton.getAttribute('aria-haspopup')).toBe('dialog');
    const tip = q('#rp-writer-tip')!;
    const terms = Array.from(tip.querySelectorAll('dl > div .gh-info-term')).map(term => term.textContent!.trim());
    expect(terms).toEqual(['Executive Summary', 'Root Cause Analysis', 'Every document']);
    const brief = Array.from(tip.querySelectorAll('dl > div'))[1].textContent!.replace(/\s+/g, ' ');
    expect(brief).toContain('strongest scoring-tier reasoning model');
    expect(brief).toContain('medium effort');
    expect(q('#rp-writer-tip-title')!.textContent!.trim()).toBe('Choosing a report writer');
  });
});
