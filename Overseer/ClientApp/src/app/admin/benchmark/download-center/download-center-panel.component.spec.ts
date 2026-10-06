import type { Mock } from "vitest";
import { ChangeDetectionStrategy, Component, ViewChild } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkBatteryMemberDto,
  BenchmarkBatteryRunDto,
  BenchmarkReportAudience,
  BenchmarkReportCoveredModelDto,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportDocumentOrigin,
  BenchmarkReportPeerNaming,
  BenchmarkReportScope,
  BenchmarkRunDetailDto,
  BenchmarkRunReportDocumentsStatus,
  BenchmarkRunReportJobDto,
  BenchmarkRunReportJobPhase
} from '../../../services/admin-benchmark.service';
import type { PdfViewerRequest } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { REPORT_LIBRARY_ALL_TAKE } from '../report-pack/report-document-format';
import { REPORT_CHART_STORAGE_KEY, ReportChartPublishResult, ReportChartSelection } from '../report-pack/report-charts';
import { ReportChartPickerComponent } from '../report-pack/report-chart-picker.component';
import {
  CHART_SKIP_REASONS,
  COMPARISON_NAME_SLUG_MAX,
  DOWNLOAD_CENTER_REPORT_JOB_POLL_MS,
  DOWNLOAD_CENTER_SEARCH_DEBOUNCE_MS,
  DOWNLOAD_CENTER_STORAGE_KEY,
  DOWNLOAD_CENTER_VIEW_STORAGE_KEY,
  DOWNLOAD_SORTS,
  DownloadCenterBatteryContext,
  DownloadCenterChartActions,
  DownloadChoice,
  DownloadCenterContext,
  DownloadCenterLibraryContext,
  DownloadCenterPanelComponent,
  DownloadCenterPreselect,
  INCLUDE_MEMBER_RUNS_TIP,
  MEMBER_RUNS_FAILED_NOTICE,
  ROW_NOTES,
  comparisonNameSlug,
  downloadCenterIo,
  downloadZipStem,
  manifestComparisons,
  reportDocumentFileStem
} from './download-center-panel.component';

const { ExecutiveSummary, TechnicalReport, InternalBrief } = BenchmarkReportAudience;
const { Summary, Detailed, Full } = BenchmarkReportDisclosure;

const DOCUMENTS_URL = '/api/admin/benchmark/report-documents';
const ENTRY_KEYS = ['run:1', 'run:2', 'group:4'];
const HASH = 'a'.repeat(64);
const OTHER_HASH = 'b'.repeat(64);

function doc(id: number, audience: BenchmarkReportAudience, overrides: Partial<BenchmarkReportDocumentListItemDto> = {}): BenchmarkReportDocumentListItemDto {
  const titles: Record<number, string> = {
    [ExecutiveSummary]: 'Executive Summary: Gemini Flash',
    [TechnicalReport]: 'Gemini Flash — Report for AI Researchers and Developers',
    [InternalBrief]: 'Internal Improvement Brief: Gemini Flash'
  };
  return {
    id,
    packId: 'pack-1',
    audience,
    title: titles[audience],
    subjectKey: 'run:1',
    subjectLabel: 'Gemini Flash',
    subjectRunIds: [1],
    suiteId: 5,
    suiteName: 'Board Suite',
    writerDisplayName: 'Claude Opus writer',
    writerProvider: 'Anthropic',
    writerModelId: 'claude-opus',
    writerThinkingLevel: 'medium',
    sameProviderAcknowledged: false,
    status: 'Completed',
    reportFormatVersion: 1,
    createdAtUtc: `2026-09-2${id % 10}T16:00:00Z`,
    inputTokens: 6000,
    outputTokens: 1500,
    durationMs: 30000,
    costUsd: 0.05,
    runChangedSinceGeneration: false,
    missingRunIds: [],
    allowedDisclosures: audience === InternalBrief ? [Full] : [Summary, Detailed, Full],
    origin: BenchmarkReportDocumentOrigin.ReportPack,
    comparisonKey: 'cmp-1',
    comparisonEntryCount: 3,
    peerCount: 2,
    pricingBasis: 'Current',
    peersChangedSinceGeneration: false,
    chartCount: 0,
    chartFigureKeys: [],
    chartSettingsHash: null,
    peerLetters: { 'run:2': 'A', 'group:4': 'B' },
    ...overrides
  };
}

/** Models of Comparison #12, as a comparison-scope document lists them, with their letters. */
const LUNA: BenchmarkReportCoveredModelDto = { entryKey: 'run:1', label: 'GPT-5.6 Luna', provider: 'OpenAI', letter: 'A' };
const GROK: BenchmarkReportCoveredModelDto = { entryKey: 'run:2', label: 'Grok 5', provider: 'xAI', letter: 'B' };
const MISTRAL: BenchmarkReportCoveredModelDto = { entryKey: 'group:4', label: 'Mistral Large 4', provider: 'Mistral', letter: 'C' };
const QWEN: BenchmarkReportCoveredModelDto = { entryKey: 'run:5', label: 'Qwen 4', provider: 'Alibaba', letter: 'D' };
/** A covered-set key whose first 6 hex are `3f9a0c`, as the server's file-name fixtures have it. */
const SET_KEY = `3f9a0c${'0'.repeat(58)}`;

/** A document of Comparison #12, *Five-model comparison* (five entries): one model's, unless the overrides say otherwise. */
function numberedDoc(id: number, audience: BenchmarkReportAudience, overrides: Partial<BenchmarkReportDocumentListItemDto> = {}): BenchmarkReportDocumentListItemDto {
  return doc(id, audience, {
    comparisonId: 12,
    comparisonName: 'Five-model comparison',
    comparisonEntryCount: 5,
    scope: BenchmarkReportScope.Model,
    coversAllEntries: false,
    coveredSetKey: null,
    coveredModels: [{ entryKey: 'run:1', label: 'Gemini Flash', provider: 'Google', letter: null }],
    ...overrides
  });
}

/** A comparison-scope document of Comparison #12 covering `models`: the whole comparison, or a subset of it. */
function coveringDoc(
  id: number,
  audience: BenchmarkReportAudience,
  wholeComparison: boolean,
  models: BenchmarkReportCoveredModelDto[],
  overrides: Partial<BenchmarkReportDocumentListItemDto> = {}
): BenchmarkReportDocumentListItemDto {
  return numberedDoc(id, audience, {
    scope: BenchmarkReportScope.Comparison,
    coversAllEntries: wholeComparison,
    subjectKey: wholeComparison ? 'comparison:12' : 'comparison:12/9999999999999999',
    subjectLabel: 'Five-model comparison',
    coveredSetKey: SET_KEY,
    coveredModels: models,
    peerCount: 0,
    peerLetters: Object.fromEntries(models.map(model => [model.entryKey, model.letter ?? ''])),
    ...overrides
  });
}

function publishResult(overrides: Partial<ReportChartPublishResult> = {}): ReportChartPublishResult {
  return { published: [], failed: [], skipped: [], canceled: false, storageNotConfigured: null, ...overrides };
}

type FakeActions = DownloadCenterChartActions & {
  publish: Mock;
};

function chartActions(overrides: Partial<DownloadCenterChartActions> = {}): FakeActions {
  return {
    currentSettingsHash: HASH,
    pricingBasis: 'Current',
    available: ['p1a-quality', 'p1b-speed', 'p1c-cost', 'p2-profile', 's1-quality-speed', 's2-quality-cost', 's3-speed-cost'],
    advisory: null,
    storageMissing: false,
    comparisonKeyMatches: (d: BenchmarkReportDocumentListItemDto) => d.comparisonKey === 'cmp-1',
    publish: vi.fn().mockName('publish').mockResolvedValue(publishResult()),
    ...overrides
  } as FakeActions;
}

@Component({
  standalone: true,
  imports: [DownloadCenterPanelComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="shell" [style.width.px]="width">
      <app-download-center-panel [context]="context" [reloadToken]="reloadToken" [idPrefix]="idPrefix"
                                 [chartActions]="actions" (documentsChanged)="changes = changes + 1"
                                 (openBatteryDownloads)="batteryDownloads.push($event)"
                                 (openComparisonDocuments)="comparisonDocuments.push($event)"></app-download-center-panel>
    </div>
  `
})
class PanelHostComponent {
  @ViewChild(DownloadCenterPanelComponent, { static: true }) panel!: DownloadCenterPanelComponent;
  width = 1400;
  context: DownloadCenterContext | null = null;
  reloadToken = 0;
  idPrefix = 'mc-dc';
  actions: DownloadCenterChartActions | null = null;
  changes = 0;
  /** Every battery run id the panel's Open battery run downloads emitted. */
  batteryDownloads: number[] = [];
  /** Every context the panel's Open comparison documents emitted. */
  comparisonDocuments: DownloadCenterLibraryContext[] = [];
}

describe('DownloadCenterPanelComponent', () => {
  let fixture: ComponentFixture<PanelHostComponent>;
  let hostComponent: PanelHostComponent;
  let http: HttpTestingController;
  let el: HTMLElement;

  beforeEach(async () => {
    localStorage.removeItem(DOWNLOAD_CENTER_STORAGE_KEY);
    localStorage.removeItem(DOWNLOAD_CENTER_VIEW_STORAGE_KEY);
    localStorage.removeItem(REPORT_CHART_STORAGE_KEY);
    await TestBed.configureTestingModule({
      imports: [PanelHostComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    fixture = TestBed.createComponent(PanelHostComponent);
    hostComponent = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => {
    el.querySelectorAll('dialog').forEach(dialog => {
      if (dialog.open) {
        dialog.close();
      }
    });
    el.querySelectorAll<HTMLElement>('[popover]').forEach(popover => {
      if (popover.matches(':popover-open')) {
        popover.hidePopover();
      }
    });
    fixture.destroy();
    vi.useRealTimers();
    localStorage.removeItem(DOWNLOAD_CENTER_STORAGE_KEY);
    localStorage.removeItem(DOWNLOAD_CENTER_VIEW_STORAGE_KEY);
    localStorage.removeItem(REPORT_CHART_STORAGE_KEY);
  });

  const panel = (): DownloadCenterPanelComponent => hostComponent.panel;
  const q = <T extends HTMLElement = HTMLElement>(selector: string): T | null => el.querySelector<T>(selector);
  const flat = (node: Element | null): string => (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const text = (selector: string): string => flat(q(selector));
  const rowKeys = (): string[] => Array.from(el.querySelectorAll('article.dc-card')).map(card => card.getAttribute('data-row-key')!);
  const rowEl = (key: string): HTMLElement => q(`article.dc-card[data-row-key="${key}"]`)!;
  const byId = <T extends HTMLElement = HTMLElement>(id: string): T | null => el.querySelector<T>(`[id="${id}"]`);
  const facetLabels = (): string[] => Array.from(el.querySelectorAll('.dc-facet-row .gh-facet-label')).map(flat);
  const chips = (): HTMLButtonElement[] => Array.from(el.querySelectorAll<HTMLButtonElement>('.dc-filter-chips .gh-filter-chip'));
  const chipNames = (): string[] => chips().map(chip => chip.getAttribute('aria-label')!);

  /** A facet's options as rendered in its popover, which need not be open to be read or clicked. */
  function facetOptions(column: string): { label: string; input: HTMLInputElement }[] {
    return Array.from(byId(`mc-dc-facet-${column}-popover`)!.querySelectorAll('.gh-facet-option'))
      .map(option => ({ label: flat(option), input: option.querySelector('input')! }));
  }

  /** Clicks the facet option whose label reads `label`. */
  function pickFacet(column: string, label: string): void {
    const option = Array.from(byId(`mc-dc-facet-${column}-popover`)!.querySelectorAll('.gh-facet-option'))
      .find(node => flat(node.querySelector('.gh-facet-option-label')) === label)!;
    option.querySelector('input')!.click();
    fixture.detectChanges();
  }

  function typeInto(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  /**
   * Fakes the clock the search debounce runs on, until `afterEach`. Only `setTimeout` and
   * `clearTimeout`: everything else keeps the real clock.
   */
  function useSearchClock(): void {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  }

  /** Runs out the search debounce on the clock `useSearchClock` fakes, then renders. */
  function pauseTyping(): Promise<void> {
    vi.advanceTimersByTime(DOWNLOAD_CENTER_SEARCH_DEBOUNCE_MS);
    fixture.detectChanges();
    return Promise.resolve();
  }

  function library(preselect: DownloadCenterPreselect = 'all', scope: 'comparison' | 'all' = 'comparison'): DownloadCenterContext {
    return {
      kind: 'library',
      scope: scope === 'comparison' ? { kind: 'comparison', entryKeys: ENTRY_KEYS } : { kind: 'all' },
      preselect
    };
  }

  /** The one pending document list request, of the given origin when one is named. */
  function expectList(origin?: string): TestRequest {
    return http.expectOne(r => r.method === 'GET' && r.url === DOCUMENTS_URL
      && (origin === undefined || r.params.get('origin') === origin));
  }

  /** Renders the host with the context and answers its one list request. */
  function render(documents: BenchmarkReportDocumentListItemDto[], context = library(), actions: DownloadCenterChartActions | null = null): TestRequest {
    hostComponent.context = context;
    hostComponent.actions = actions;
    fixture.detectChanges();
    const request = expectList();
    request.flush(documents);
    fixture.detectChanges();
    return request;
  }

  function setFilter(id: string, value: string): void {
    const control = byId<HTMLInputElement | HTMLSelectElement>(id)!;
    control.value = value;
    control.dispatchEvent(new Event(control instanceof HTMLSelectElement ? 'change' : 'input'));
    fixture.detectChanges();
  }

  function check(key: string): void {
    rowEl(key).querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    fixture.detectChanges();
  }

  function settle(): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, 0));
  }

  // -------------------------------------------------------------------------------------------
  // The library context
  // -------------------------------------------------------------------------------------------

  describe('library context', () => {
    it('lists a comparison\'s documents by one list call, and no run reports', () => {
      const request = render([
        doc(11, ExecutiveSummary, { missingRunIds: [3], subjectRunIds: [1, 3] }),
        doc(12, TechnicalReport, { subjectKey: 'run:2', subjectRunIds: [2] })
      ]);

      expect(request.request.params.get('comparison')).toBe('run:1,run:2,group:4');
      expect(request.request.params.get('origin')).toBe('reportPack');
      expect(request.request.params.has('take')).toBe(false);
      http.expectNone(r => /report-documents\/\d+$/.test(r.url));
      expect(panel().rows.map(r => r.key)).toEqual(['doc:12', 'doc:11']);
      expect(panel().rows.every(r => r.kind === 'pack')).toBe(true);
      // A missing subject run makes no notice: no run report is listed.
      expect(el.querySelectorAll('.dc-notice').length).toBe(0);
    });

    it('lists the comparison documents about one subject, by subject and origin', () => {
      const request = render([doc(11, ExecutiveSummary), doc(12, TechnicalReport)], {
        kind: 'library',
        scope: { kind: 'subject', subjectKey: 'run:1', label: 'run #1 · Board Suite · Gemini Flash' },
        preselect: 'none'
      });

      expect(request.request.params.get('subject')).toBe('run:1');
      expect(request.request.params.get('origin')).toBe('reportPack');
      expect(request.request.params.has('comparison')).toBe(false);
      expect(request.request.params.has('take')).toBe(false);
      expect(panel().rows.map(r => r.key)).toEqual(['doc:12', 'doc:11']);
      expect(panel().selectedCount).toBe(0);
    });

    it('says when no comparison document is about the subject', () => {
      render([], {
        kind: 'library',
        scope: { kind: 'subject', subjectKey: 'battery:7', label: 'battery run #7' },
        preselect: 'none'
      });

      expect(text('.dc-empty')).toBe('No comparison documents have been written about it.');
    });

    it('preselects only the listed documents, as the package chooses them, and keeps that on a reload', () => {
      render([doc(11, ExecutiveSummary), doc(12, TechnicalReport), doc(13, InternalBrief)], library({ ids: [12, 13] }));

      const included = (): string[] => panel().rows.filter(r => panel().isIncluded(r)).map(r => r.key);
      expect(included().sort()).toEqual(['doc:12', 'doc:13']);
      expect(rowEl('doc:11').querySelector<HTMLInputElement>('input[type="checkbox"]')!.checked).toBe(false);
      expect(rowEl('doc:12').querySelector<HTMLInputElement>('input[type="checkbox"]')!.checked).toBe(true);

      hostComponent.reloadToken++;
      fixture.detectChanges();
      expectList().flush([doc(11, ExecutiveSummary), doc(12, TechnicalReport), doc(13, InternalBrief), doc(14, ExecutiveSummary)]);
      fixture.detectChanges();
      // The rows still listed keep their choices; a document new on the reload starts chosen only when listed.
      expect(included().sort()).toEqual(['doc:12', 'doc:13']);
    });

    it('preselects a listed document only where the package chooses it', () => {
      localStorage.setItem(DOWNLOAD_CENTER_STORAGE_KEY, JSON.stringify({ version: 3, package: 'provider' }));
      render([doc(11, ExecutiveSummary), doc(13, InternalBrief)], library({ ids: [11, 13] }));

      // External never chooses the Internal Improvement Brief, listed or not.
      expect(panel().isIncluded(panel().rows.find(r => r.key === 'doc:11')!)).toBe(true);
      expect(panel().isIncluded(panel().rows.find(r => r.key === 'doc:13')!)).toBe(false);
    });

    it('lists every comparison document, up to the endpoint\'s maximum, in the all scope', () => {
      const request = render([doc(11, ExecutiveSummary)], library('none', 'all'));

      expect(request.request.params.get('take')).toBe(String(REPORT_LIBRARY_ALL_TAKE));
      expect(request.request.params.has('comparison')).toBe(false);
    });

    it('preselects nothing from the launcher and every row from the wizard', () => {
      render([doc(11, ExecutiveSummary), doc(12, TechnicalReport)], library('none'));
      expect(panel().selectedCount).toBe(0);
      expect(panel().summaryLine).toBe('No files chosen · Internal package');
      expect(text('.dc-selection-count')).toBe('Nothing selected');

      hostComponent.context = library('all');
      fixture.detectChanges();
      expectList().flush([doc(11, ExecutiveSummary), doc(12, TechnicalReport)]);
      fixture.detectChanges();
      expect(panel().selectedCount).toBe(2);
      expect(rowEl('doc:11').querySelector<HTMLInputElement>('input[type="checkbox"]')!.checked).toBe(true);
    });

    it('lists again on the reload token, keeping the choices made on the rows still listed', () => {
      render([doc(11, ExecutiveSummary), doc(12, TechnicalReport)], library('none'));
      check('doc:11');

      hostComponent.reloadToken++;
      fixture.detectChanges();
      expectList().flush([doc(11, ExecutiveSummary), doc(13, InternalBrief)]);
      fixture.detectChanges();

      expect(panel().rows.map(r => r.key)).toEqual(['doc:13', 'doc:11']);
      expect(panel().isIncluded(panel().rows.find(r => r.key === 'doc:11')!)).toBe(true);
      expect(panel().isIncluded(panel().rows.find(r => r.key === 'doc:13')!)).toBe(false);
    });

    it('says when a comparison has no reports', () => {
      render([]);

      expect(text('.dc-empty')).toBe('No reports have been written for this comparison yet.');
      expect(q('.dc-card-list')).toBeNull();
      expect(q('.dc-filter-bar')).toBeNull();
    });
  });

  // -------------------------------------------------------------------------------------------
  // The list
  // -------------------------------------------------------------------------------------------

  describe('the list', () => {
    /** Three report documents over two subjects, two suites and two writers. */
    function twoSuites(): BenchmarkReportDocumentListItemDto[] {
      const harbor = { subjectLabel: 'Claude Harbor', subjectKey: 'run:2', subjectRunIds: [2], suiteName: 'Wiki Suite' };
      return [
        doc(11, ExecutiveSummary),
        doc(12, TechnicalReport, { ...harbor, writerDisplayName: 'Gemini writer' }),
        doc(13, ExecutiveSummary, harbor)
      ];
    }

    it('renders one card per row in a list, each titled by the label of its checkbox', () => {
      render([doc(11, ExecutiveSummary), doc(12, TechnicalReport)]);

      const list = q('ul.dc-card-list')!;
      expect(list.getAttribute('role')).toBe('list');
      expect(list.getAttribute('aria-labelledby')).toBe('mc-dc-documents-title');
      const items = Array.from(list.children);
      expect(items.length).toBe(2);
      expect(items.every(item => item.tagName === 'LI' && item.firstElementChild!.matches('article.dc-card'))).toBe(true);

      const card = rowEl('doc:11');
      const title = card.querySelector<HTMLElement>('h5.dc-card-title')!;
      expect(title.id).toBe('mc-dc-doc-11-title');
      expect(card.getAttribute('aria-labelledby')).toBe(title.id);
      expect(card.querySelector('.dc-card-type')!.textContent!.trim()).toBe('Executive Summary');
      expect(text('article[data-row-key="doc:11"] .dc-card-meta')).toBe('2026-09-21 16:00 UTC·, Gemini Flash·, Board Suite·, by Claude Opus writer');

      const check = card.querySelector<HTMLInputElement>('.dc-card-check')!;
      expect(check.getAttribute('aria-label')).toBe('Include Executive Summary: Gemini Flash');
      const label = title.querySelector<HTMLLabelElement>('label.dc-doc-label')!;
      expect(label.htmlFor).toBe(check.id);
      expect(check.checked).toBe(true);
      label.click();
      fixture.detectChanges();
      expect(check.checked).toBe(false);
      expect(panel().isIncluded(panel().rows.find(r => r.key === 'doc:11')!)).toBe(false);

      const actions = card.querySelector('.dc-card-actions')!;
      expect(actions.getAttribute('role')).toBe('group');
      expect(actions.getAttribute('aria-label')).toBe('Actions for Executive Summary: Gemini Flash, 2026-09-21 16:00 UTC');
      expect(actions.querySelector('.dc-view-btn')).not.toBeNull();

      const options = card.querySelector('.dc-card-options')!;
      expect(options.getAttribute('role')).toBe('group');
      expect(Array.from(options.querySelectorAll('.dc-option-label')).map(node => node.textContent!.trim()))
        .toEqual(['Disclosure', 'Peer names', 'Formats']);
      expect(options.querySelector('fieldset.dc-formats > legend')!.textContent!.trim()).toBe('Formats');
    });

    it('sorts with Sort by, remembering the order for the next panel', () => {
      render([doc(11, TechnicalReport), doc(13, ExecutiveSummary), doc(12, InternalBrief, { runChangedSinceGeneration: true })]);

      const select = byId<HTMLSelectElement>('mc-dc-sort')!;
      const label = q(`label[for="mc-dc-sort"]`)!;
      expect(label.textContent!.trim()).toBe('Sort by');
      expect(label.classList).not.toContain('visually-hidden');
      expect(Array.from(select.options).map(option => option.textContent!.trim())).toEqual(DOWNLOAD_SORTS.map(sort => sort.label));
      expect(select.value).toBe('created-desc');
      expect(rowKeys()).toEqual(['doc:13', 'doc:12', 'doc:11']);

      setFilter('mc-dc-sort', 'title');
      expect(rowKeys()).toEqual(['doc:13', 'doc:11', 'doc:12']);

      setFilter('mc-dc-sort', 'changed-first');
      expect(rowKeys()).toEqual(['doc:12', 'doc:13', 'doc:11']);
      expect(JSON.parse(localStorage.getItem(DOWNLOAD_CENTER_VIEW_STORAGE_KEY)!)).toEqual({ version: 1, sort: 'changed-first' });

      const second = TestBed.createComponent(PanelHostComponent);
      second.componentInstance.context = library();
      second.detectChanges();
      expectList().flush([doc(11, TechnicalReport), doc(13, ExecutiveSummary), doc(12, InternalBrief, { runChangedSinceGeneration: true })]);
      second.detectChanges();
      const secondEl = second.nativeElement as HTMLElement;
      expect(secondEl.querySelector<HTMLSelectElement>('[id="mc-dc-sort"]')!.value).toBe('changed-first');
      expect(Array.from(secondEl.querySelectorAll('article.dc-card')).map(card => card.getAttribute('data-row-key')))
        .toEqual(['doc:12', 'doc:13', 'doc:11']);
      second.destroy();

      localStorage.setItem(DOWNLOAD_CENTER_VIEW_STORAGE_KEY, JSON.stringify({ version: 1, sort: 'no-such-order' }));
      const third = TestBed.createComponent(PanelHostComponent);
      third.componentInstance.context = library();
      third.detectChanges();
      expectList().flush([doc(11, TechnicalReport)]);
      third.detectChanges();
      expect((third.nativeElement as HTMLElement).querySelector<HTMLSelectElement>('[id="mc-dc-sort"]')!.value).toBe('created-desc');
      third.destroy();
    });

    it('searches title, subject, suite and writer once typing pauses, and clears on Escape without closing a dialog', async () => {
      useSearchClock();
      render(twoSuites());
      const label = q(`label[for="mc-dc-search"]`)!;
      expect(label.classList).toContain('visually-hidden');
      expect(label.textContent!.trim()).toBe('Search documents');
      const input = byId<HTMLInputElement>('mc-dc-search')!;
      expect(input.type).toBe('search');

      typeInto(input, 'harbor');
      expect(rowKeys().length).toBe(3);
      await pauseTyping();
      expect(rowKeys()).toEqual(['doc:13', 'doc:12']);

      typeInto(input, 'wiki suite');
      await pauseTyping();
      expect(rowKeys()).toEqual(['doc:13', 'doc:12']);

      typeInto(input, 'gemini writer');
      await pauseTyping();
      expect(rowKeys()).toEqual(['doc:12']);

      typeInto(input, 'executive');
      await pauseTyping();
      expect(rowKeys()).toEqual(['doc:13', 'doc:11']);

      const heard: string[] = [];
      el.addEventListener('keydown', event => heard.push(event.key));
      const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      input.dispatchEvent(escape);
      fixture.detectChanges();
      expect(escape.defaultPrevented).toBe(true);
      expect(heard).toEqual([]);
      expect(input.value).toBe('');
      expect(rowKeys().length).toBe(3);

      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      expect(heard).toEqual(['Escape']);
    });

    it('filters by facets: values of one facet OR together, facets AND, and each count reflects the other filters', () => {
      render(twoSuites());

      expect(facetLabels()).toEqual(['Document', 'Model', 'Suite', 'Written by', 'Created']);
      // Every row is unchanged, so Changes has one value and is not offered.
      expect(byId('mc-dc-facet-changes-trigger')).toBeNull();
      expect(facetOptions('document').map(option => option.label))
        .toEqual(['Executive Summary, 2 documents', 'Report for AI Researchers and Developers, 1 document']);
      expect(facetOptions('writer').map(option => option.label))
        .toEqual(['Claude Opus writer, 2 documents', 'Gemini writer, 1 document']);

      const facetsBefore = panel().facets;
      fixture.detectChanges();
      expect(panel().facets).toBe(facetsBefore);

      pickFacet('model', 'Claude Harbor');
      pickFacet('model', 'Gemini Flash');
      expect(rowKeys()).toEqual(['doc:13', 'doc:12', 'doc:11']);
      expect(text('#mc-dc-facet-model-trigger')).toBe('Model 2 selected');
      expect(panel().facets).not.toBe(facetsBefore);

      pickFacet('document', 'Executive Summary');
      expect(rowKeys()).toEqual(['doc:13', 'doc:11']);
      expect(facetOptions('suite').map(option => option.label)).toEqual(['Board Suite, 1 document', 'Wiki Suite, 1 document']);

      pickFacet('suite', 'Wiki Suite');
      expect(rowKeys()).toEqual(['doc:13']);
      expect(facetOptions('document').map(option => option.label))
        .toEqual(['Executive Summary, 1 document', 'Report for AI Researchers and Developers, 1 document']);
      expect(facetOptions('suite').map(option => option.label)).toEqual(['Board Suite, 1 document', 'Wiki Suite, 1 document']);
      expect(text('#mc-dc-list-status')).toBe('One document · filtered from 3');
    });

    it('filters by creation time in single mode, against the Download Center clock', () => {
      vi.spyOn(downloadCenterIo, 'now').mockReturnValue(new Date('2026-09-24T00:00:00Z'));
      render([doc(11, ExecutiveSummary, { createdAtUtc: '2026-09-01T16:00:00Z' }), doc(12, TechnicalReport), doc(13, InternalBrief)]);

      const created = facetOptions('created');
      expect(created.map(option => option.input.type)).toEqual(['radio', 'radio', 'radio', 'radio']);
      expect(created.map(option => option.label))
        .toEqual(['Any time', 'Last 24 hours, 1 document', 'Last 7 days, 2 documents', 'Last 30 days, 3 documents']);

      pickFacet('created', 'Last 7 days');
      expect(rowKeys()).toEqual(['doc:13', 'doc:12']);

      pickFacet('created', 'Any time');
      expect(rowKeys()).toEqual(['doc:13', 'doc:12', 'doc:11']);
    });

    it('shows the active filters as removable chips, and Clear all keeps Show selected only', async () => {
      useSearchClock();
      render(twoSuites());
      pickFacet('document', 'Executive Summary');
      pickFacet('document', 'Report for AI Researchers and Developers');
      pickFacet('suite', 'Wiki Suite');
      typeInto(byId<HTMLInputElement>('mc-dc-search')!, 'harbor');
      await pauseTyping();

      const chipList = q('ul.dc-filter-chips')!;
      expect(chipList.getAttribute('role')).toBe('list');
      expect(chipList.getAttribute('aria-label')).toBe('Active filters');
      expect(chipNames()).toEqual([
        'Remove filter Document: Executive Summary',
        'Remove filter Document: Report for AI Researchers and Developers',
        'Remove filter Suite: Wiki Suite',
        'Remove filter Search: “harbor”'
      ]);

      chips()[0].click();
      fixture.detectChanges();
      expect(chipNames()[0]).toBe('Remove filter Document: Report for AI Researchers and Developers');
      expect(document.activeElement).toBe(chips()[0]);
      expect(rowKeys()).toEqual(['doc:12']);

      q<HTMLButtonElement>('.dc-show-selected')!.click();
      fixture.detectChanges();
      const clearAll = q<HTMLButtonElement>('.dc-clear-filters')!;
      expect(clearAll.textContent!.trim()).toBe('Clear all');
      clearAll.click();
      fixture.detectChanges();

      expect(q('ul.dc-filter-chips')).toBeNull();
      expect(panel().showSelectedOnly).toBe(true);
      expect(byId<HTMLInputElement>('mc-dc-search')!.value).toBe('');
      expect(document.activeElement).toBe(byId('mc-dc-search'));
      expect(rowKeys().length).toBe(3);

      typeInto(byId<HTMLInputElement>('mc-dc-search')!, 'nothing like this');
      await pauseTyping();
      expect(rowKeys()).toEqual([]);
      expect(text('.dc-no-matches')).toContain('No documents match these filters.');
      expect(q('.dc-no-matches button')!.textContent!.trim()).toBe('Clear all filters');
    });

    /** Twelve Executive Summaries and one older Report for AI Researchers and Developers. */
    function thirteen(): BenchmarkReportDocumentListItemDto[] {
      return [
        ...Array.from({ length: 12 }, (_, i) => doc(100 + i, ExecutiveSummary, { createdAtUtc: `2026-09-${10 + i}T16:00:00Z` })),
        doc(300, TechnicalReport, { createdAtUtc: '2026-09-01T16:00:00Z' })
      ];
    }

    it('shows ten cards, then more on request, focusing the first new card', () => {
      render(thirteen());

      expect(rowKeys().length).toBe(10);
      const status = byId('mc-dc-list-status')!;
      expect(status.getAttribute('role')).toBe('status');
      expect(status.textContent!.trim()).toBe('Showing 10 of 13 documents');
      expect(q('.dc-show-more')!.textContent!.trim()).toBe('Show 3 more');
      expect(q('.dc-show-all')).toBeNull();

      q<HTMLButtonElement>('.dc-show-more')!.click();
      fixture.detectChanges();
      expect(rowKeys().length).toBe(13);
      expect(document.activeElement).toBe(byId('mc-dc-doc-101-title'));
      expect(text('#mc-dc-list-status')).toBe('Showing 13 of 13 documents');
      expect(q('.dc-show-more')).toBeNull();

      pickFacet('document', 'Executive Summary');
      expect(rowKeys().length).toBe(10);
      expect(text('#mc-dc-list-status')).toBe('Showing 10 of 12 documents · filtered from 13');

      const more = [
        ...Array.from({ length: 24 }, (_, i) => doc(200 + i, ExecutiveSummary, { createdAtUtc: `2026-09-10T${String(i).padStart(2, '0')}:00:00Z` })),
        doc(301, TechnicalReport, { createdAtUtc: '2026-09-01T16:00:00Z' })
      ];
      // A context that lists other documents loads afresh; an equal one keeps the loaded list.
      render(more, library('none'));
      expect(rowKeys().length).toBe(10);
      const showAll = q<HTMLButtonElement>('.dc-show-all')!;
      expect(showAll.textContent!.trim()).toBe('Show all 25');
      showAll.click();
      fixture.detectChanges();
      expect(rowKeys().length).toBe(25);
      expect(document.activeElement).toBe(byId(`mc-dc-${rowKeys()[10].replace(':', '-')}-title`));
    });

    it('counts the selection the list does not show, selects what matches, and has no select-all checkbox', () => {
      render(thirteen(), library('none'));
      const stray = Array.from(el.querySelectorAll('.dc-documents input[type="checkbox"]'))
        .filter(input => !input.closest('.dc-card') && !input.closest('app-filter-facet'));
      expect(stray).toEqual([]);

      expect(q('.dc-select-shown')!.textContent!.trim()).toBe('Select all 13');
      pickFacet('document', 'Executive Summary');
      expect(q('.dc-select-shown')!.textContent!.trim()).toBe('Select all 12 matching');
      q<HTMLButtonElement>('.dc-select-shown')!.click();
      fixture.detectChanges();
      expect(text('.dc-selection-count')).toBe('12 selected — 2 not shown');

      q<HTMLButtonElement>('.dc-show-selected')!.click();
      fixture.detectChanges();
      expect(q('.dc-show-selected')!.getAttribute('aria-pressed')).toBe('true');

      q<HTMLButtonElement>('.dc-clear-selection')!.click();
      fixture.detectChanges();
      expect(panel().selectedCount).toBe(0);
      expect(panel().showSelectedOnly).toBe(false);
    });

    it('keeps the filter bar flush with the top of the scroller from 36rem, and lets it scroll away below', async () => {
      render(thirteen());
      const shell = q('.shell')!;
      shell.style.display = 'flex';
      shell.style.flexDirection = 'column';
      shell.style.height = '500px';
      fixture.detectChanges();

      const body = q('.dc-body')!;
      const bar = q('.dc-filter-bar')!;
      expect(getComputedStyle(bar).position).toBe('sticky');
      body.scrollTop = 800;
      await new Promise(resolve => requestAnimationFrame(() => resolve(null)));
      expect(body.scrollTop).toBeGreaterThan(0);
      expect(Math.round(bar.getBoundingClientRect().top - body.getBoundingClientRect().top)).toBe(0);

      hostComponent.width = 400;
      fixture.detectChanges();
      expect(getComputedStyle(bar).position).toBe('static');
    });

    it('lays the cards out by the list\'s own width, and stacks the package column below 48rem', () => {
      render([doc(11, ExecutiveSummary)]);
      const areaRows = (): string[][] => (getComputedStyle(rowEl('doc:11')).gridTemplateAreas.match(/"[^"]*"/g) ?? [])
        .map(row => row.replace(/"/g, '').trim().split(/\s+/));
      expect(areaRows()[0].length).toBe(3);
      expect(getComputedStyle(q('.dc-layout')!).gridTemplateColumns.split(' ').length).toBe(2);

      hostComponent.width = 400;
      fixture.detectChanges();
      expect(areaRows().map(row => row.length)).toEqual([2, 2, 2]);
      expect(getComputedStyle(rowEl('doc:11').querySelector('.dc-card-options')!).flexDirection).toBe('column');
      expect(getComputedStyle(q('.dc-layout')!).gridTemplateColumns.split(' ').length).toBe(1);
    });
  });

  // -------------------------------------------------------------------------------------------
  // View and Delete
  // -------------------------------------------------------------------------------------------

  describe('View and Delete', () => {
    it('views a pack document at its highest disclosure, peers named, with the Peer names row', () => {
      const service = TestBed.inject(AdminBenchmarkService);
      const pdf = vi.spyOn(service, 'getReportDocumentPdf').mockReturnValue(of({ bytes: new Uint8Array([1]), fileName: null }));
      render([doc(11, TechnicalReport, { allowedDisclosures: [Detailed, Summary] })]);
      const open = vi.spyOn(panel().pdfViewer!, 'open').mockReturnValue(undefined);

      const rid = panel().rowId(panel().rows[0]);
      const button = byId<HTMLButtonElement>(`${rid}-view`)!;
      expect(button.getAttribute('aria-label')).toBe('View Gemini Flash — Report for AI Researchers and Developers, 2026-09-21 16:00 UTC');
      expect(button.getAttribute('interestfor')).toBe(`${rid}-view-tip`);
      expect(button.hasAttribute('title')).toBe(false);
      button.click();

      const request = vi.mocked(open).mock.lastCall![0] as PdfViewerRequest;
      expect(request.variants).toEqual([{ key: 'summary', label: 'Summary' }, { key: 'detailed', label: 'Detailed' }]);
      expect(request.initialVariant).toBe('detailed');
      expect(request.secondaryVariants?.label).toBe('Peer names');
      expect(request.secondaryVariants?.initial).toBe('named');
      expect(request.fallbackFileName).toBe('run-1_vs-run-2-group-4_gemini-flash_Researcher_Report.pdf');
      request.load('summary', 'anonymized').subscribe();
      expect(pdf).toHaveBeenCalledWith(11, Summary, BenchmarkReportPeerNaming.Anonymized, expect.any(String));

      panel().pdfViewer!.closed.emit();
      expect(document.activeElement).toBe(button);
    });

    it('offers Delete only on Report Pack documents', () => {
      render([doc(11, ExecutiveSummary), doc(12, TechnicalReport, { origin: BenchmarkReportDocumentOrigin.RunCompletion })]);

      expect(byId('mc-dc-doc-11-delete')).not.toBeNull();
      expect(byId('mc-dc-doc-11-delete')!.classList).toContain('action-btn-danger');
      expect(byId('mc-dc-doc-12-delete')).toBeNull();
      expect(byId('mc-dc-doc-12-view')).not.toBeNull();
    });

    it('deletes after the confirmation, moves focus to the next row and says so', async () => {
      render([doc(11, ExecutiveSummary), doc(12, TechnicalReport), doc(13, InternalBrief)]);
      const confirm = q<HTMLDialogElement>('dialog.dc-delete-dialog')!;

      byId<HTMLButtonElement>('mc-dc-doc-12-delete')!.click();
      fixture.detectChanges();
      expect(confirm.open).toBe(true);
      expect(text('.dc-delete-text')).toContain('Gemini Flash — Report for AI Researchers and Developers');

      const canceled = new Promise<void>(resolve => confirm.addEventListener('close', () => resolve(), { once: true }));
      q<HTMLButtonElement>('.dc-delete-cancel')!.click();
      await canceled;
      fixture.detectChanges();
      expect(document.activeElement).toBe(byId('mc-dc-doc-12-delete'));
      http.expectNone(r => r.method === 'DELETE');

      byId<HTMLButtonElement>('mc-dc-doc-12-delete')!.click();
      fixture.detectChanges();
      const closed = new Promise<void>(resolve => confirm.addEventListener('close', () => resolve(), { once: true }));
      q<HTMLButtonElement>('.dc-delete-confirm')!.click();
      const request = http.expectOne(r => r.method === 'DELETE');
      expect(request.request.url).toBe(`${DOCUMENTS_URL}/12`);
      request.flush(null);
      await closed;
      fixture.detectChanges();

      expect(rowKeys()).toEqual(['doc:13', 'doc:11']);
      // Newest first: the row after the deleted one is document 11.
      expect(document.activeElement).toBe(byId('mc-dc-doc-11-view'));
      expect(text('.dc-status')).toBe('Deleted Gemini Flash — Report for AI Researchers and Developers, 2026-09-22 16:00 UTC.');
      expect(hostComponent.changes).toBe(1);
    });

    it('shows the server\'s reason when a delete fails, and keeps the document', () => {
      render([doc(11, ExecutiveSummary)]);

      byId<HTMLButtonElement>('mc-dc-doc-11-delete')!.click();
      fixture.detectChanges();
      q<HTMLButtonElement>('.dc-delete-confirm')!.click();
      http.expectOne(r => r.method === 'DELETE').flush({ error: 'The document is being rendered.' }, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      expect(q<HTMLDialogElement>('dialog.dc-delete-dialog')!.open).toBe(true);
      expect(text('.dc-delete-dialog .gh-field-error')).toBe('The document is being rendered.');
      expect(rowKeys()).toContain('doc:11');
    });

    it('stops the close and cancel events of its nested dialogs', () => {
      render([doc(11, ExecutiveSummary)], library(), chartActions());
      const heard: string[] = [];
      el.addEventListener('close', () => heard.push('close'));
      el.addEventListener('cancel', () => heard.push('cancel'));

      for (const selector of ['dialog.dc-delete-dialog', 'dialog.dc-charts-dialog', 'dialog.pdfv']) {
        const dialog = q<HTMLDialogElement>(selector)!;
        expect(dialog, selector).not.toBeNull();
        dialog.dispatchEvent(new Event('cancel', { bubbles: true, cancelable: true }));
        dialog.dispatchEvent(new Event('close', { bubbles: true }));
      }
      expect(heard).toEqual([]);
    });
  });

  // -------------------------------------------------------------------------------------------
  // File names
  // -------------------------------------------------------------------------------------------

  describe('file names', () => {
    const stem = (overrides: Partial<BenchmarkReportDocumentListItemDto>, audience = ExecutiveSummary): string =>
      reportDocumentFileStem(doc(1, audience, overrides), 'x');

    it('names one to three peers by their entry keys in letter order, after run-<id>_ or first for a group subject', () => {
      expect(stem({ peerCount: 2, peerLetters: { 'group:4': 'B', 'run:2': 'A' } }))
        .toBe('run-1_vs-run-2-group-4_executive-summary-gemini-flash');
      expect(stem({ subjectKey: 'battery:9', peerCount: 1, peerLetters: { 'battery:10': 'A' } }))
        .toBe('battery-run-9_vs-battery-run-10_executive-summary-gemini-flash');
      expect(stem({ subjectKey: 'run:92', peerCount: 2, peerLetters: { 'run:95': 'B', 'run:94': 'A' } }))
        .toBe('run-92_vs-run-94-run-95_executive-summary-gemini-flash');
      expect(stem({ subjectKey: 'group:3', peerCount: 3, peerLetters: { 'run:5': 'C', 'battery:6': 'A', 'group:7': 'B' } }))
        .toBe('vs-battery-run-6-group-7-run-5_executive-summary-gemini-flash');
      expect(stem({ peerCount: 1, peerLetters: { 'run:2': 'A' } }, TechnicalReport))
        .toBe('run-1_vs-run-2_gemini-flash_Researcher_Report');
    });

    it('names more than three peers, or peers it cannot spell, by their count and the comparison key', () => {
      const four = { 'run:2': 'A', 'run:3': 'B', 'run:4': 'C', 'run:5': 'D' };
      expect(stem({ peerCount: 4, peerLetters: four, comparisonKey: '1a2b3c4d5e6f7a8b' }))
        .toBe('run-1_vs-4-models-1a2b3c4d_executive-summary-gemini-flash');
      // A peer key the name cannot spell.
      expect(stem({ peerCount: 2, peerLetters: { 'run:2': 'A', 'set:9': 'B' }, comparisonKey: 'ffeeddccbbaa' }))
        .toBe('run-1_vs-2-models-ffeeddcc_executive-summary-gemini-flash');
      // A peer the document list sends no letter for.
      expect(stem({ peerCount: 2, peerLetters: { 'run:2': 'A' }, comparisonKey: 'ffeeddccbbaa' }))
        .toBe('run-1_vs-2-models-ffeeddcc_executive-summary-gemini-flash');
    });

    it('names a legacy document stored without a comparison key vs-<N>-models_, and one without peers with no comparison part', () => {
      expect(stem({ peerCount: 4, peerLetters: {}, comparisonKey: null }))
        .toBe('run-1_vs-4-models_executive-summary-gemini-flash');
      expect(stem({ subjectKey: 'group:3', peerCount: 4, peerLetters: undefined, comparisonKey: undefined }))
        .toBe('vs-4-models_executive-summary-gemini-flash');
      expect(stem({ peerCount: 0, peerLetters: {} }))
        .toBe('run-1_executive-summary-gemini-flash');
    });
  });

  // -------------------------------------------------------------------------------------------
  // Numbered comparisons
  // -------------------------------------------------------------------------------------------

  describe('numbered comparisons', () => {
    const { Named, Anonymized } = BenchmarkReportPeerNaming;

    function comparisonContext(name: string | null = 'Five-model comparison'): DownloadCenterContext {
      return { kind: 'library', scope: { kind: 'comparison', comparisonId: 12, name, entryKeys: ENTRY_KEYS }, preselect: 'all' };
    }

    /** The two list requests of a numbered comparison: by its number, and by its entry keys. */
    function expectComparisonLists(): { byNumber: TestRequest; byEntries: TestRequest } {
      const byNumber = http.expectOne(r => r.url === DOCUMENTS_URL && r.params.get('comparisonId') === '12');
      const byEntries = http.expectOne(r => r.url === DOCUMENTS_URL && r.params.has('comparison'));
      return { byNumber, byEntries };
    }

    /** Four documents: Comparison #12 whole and a subset of it, one model's of Comparison #14, and one without a number. */
    function fourDocuments(): BenchmarkReportDocumentListItemDto[] {
      return [
        coveringDoc(31, ExecutiveSummary, true, [LUNA, GROK, MISTRAL]),
        coveringDoc(32, TechnicalReport, false, [LUNA, GROK]),
        numberedDoc(33, ExecutiveSummary, {
          comparisonId: 14,
          comparisonName: 'Second comparison',
          subjectLabel: 'GPT-5.6 Luna',
          coveredModels: [{ entryKey: 'run:1', label: 'GPT-5.6 Luna', provider: 'OpenAI', letter: null }]
        }),
        doc(34, InternalBrief)
      ];
    }

    it('lists a numbered comparison by its number, and the documents written before numbering by its entry keys', () => {
      hostComponent.context = comparisonContext();
      fixture.detectChanges();
      const { byNumber, byEntries } = expectComparisonLists();
      expect(byNumber.request.params.get('origin')).toBe('reportPack');
      expect(byNumber.request.params.has('comparison')).toBe(false);
      expect(byEntries.request.params.get('comparison')).toBe('run:1,run:2,group:4');
      expect(byEntries.request.params.get('origin')).toBe('reportPack');
      expect(byEntries.request.params.has('comparisonId')).toBe(false);
      byNumber.flush([numberedDoc(11, ExecutiveSummary), numberedDoc(12, TechnicalReport)]);
      // By its entry keys: a numbered document again, one without a number, and one of another comparison.
      byEntries.flush([numberedDoc(11, ExecutiveSummary), doc(13, InternalBrief), numberedDoc(14, ExecutiveSummary, { comparisonId: 14 })]);
      fixture.detectChanges();

      expect(panel().rows.map(r => r.key)).toEqual(['doc:13', 'doc:12', 'doc:11']);
    });

    it('lists the numbered documents alone when the list by entry keys fails', () => {
      hostComponent.context = comparisonContext();
      fixture.detectChanges();
      const { byNumber, byEntries } = expectComparisonLists();
      byEntries.flush({ error: 'No.' }, { status: 500, statusText: 'Server Error' });
      byNumber.flush([numberedDoc(11, ExecutiveSummary)]);
      fixture.detectChanges();

      expect(panel().rows.map(r => r.key)).toEqual(['doc:11']);
      expect(el.querySelectorAll('.dc-notice').length).toBe(0);
    });

    it('heads the list with the comparison\'s number and name, and keeps its rows and choices when only the name changes', () => {
      hostComponent.context = comparisonContext();
      fixture.detectChanges();
      const { byNumber, byEntries } = expectComparisonLists();
      byNumber.flush([numberedDoc(11, ExecutiveSummary), numberedDoc(12, TechnicalReport)]);
      byEntries.flush([]);
      fixture.detectChanges();

      const heading = byId('mc-dc-documents-title')!;
      expect(heading.tagName).toBe('H4');
      expect(flat(heading)).toBe('Documents of Comparison #12 — Five-model comparison');
      check('doc:11');
      const included = (key: string): boolean => panel().isIncluded(panel().rows.find(r => r.key === key)!);
      expect(included('doc:11')).toBe(false);

      hostComponent.context = comparisonContext('Flagships, October');
      fixture.detectChanges();
      http.expectNone(r => r.url === DOCUMENTS_URL);
      expect(flat(byId('mc-dc-documents-title'))).toBe('Documents of Comparison #12 — Flagships, October');
      expect(included('doc:11')).toBe(false);
      expect(included('doc:12')).toBe(true);

      hostComponent.context = comparisonContext(null);
      fixture.detectChanges();
      http.expectNone(r => r.url === DOCUMENTS_URL);
      expect(flat(byId('mc-dc-documents-title'))).toBe('Documents of Comparison #12');
    });

    it('reads Documents for a comparison not yet numbered, and for every other list', () => {
      render([doc(11, ExecutiveSummary)]);
      expect(flat(byId('mc-dc-documents-title'))).toBe('Documents');

      hostComponent.context = library('none', 'all');
      fixture.detectChanges();
      expectList().flush([doc(11, ExecutiveSummary)]);
      fixture.detectChanges();
      expect(flat(byId('mc-dc-documents-title'))).toBe('Documents');
    });

    it('filters by Scope, by Comparison, and by Model, where a comparison document counts under each model it covers', () => {
      render(fourDocuments(), library('none', 'all'));

      expect(facetLabels()).toEqual(['Document', 'Scope', 'Comparison', 'Model', 'Created']);
      expect(facetOptions('scope').map(option => option.label))
        .toEqual(['Whole comparison, 1 document', 'Model subset, 1 document', 'One model, 2 documents']);
      expect(facetOptions('comparison').map(option => option.label))
        .toEqual(['#14 — Second comparison, 1 document', '#12 — Five-model comparison, 2 documents']);
      expect(facetOptions('model').map(option => option.label)).toEqual([
        'Gemini Flash, 1 document',
        'GPT-5.6 Luna, 3 documents',
        'Grok 5, 2 documents',
        'Mistral Large 4, 1 document'
      ]);

      pickFacet('model', 'Grok 5');
      expect(rowKeys()).toEqual(['doc:32', 'doc:31']);
      expect(text('#mc-dc-facet-model-trigger')).toBe('Model 1 selected');
      pickFacet('scope', 'Model subset');
      expect(rowKeys()).toEqual(['doc:32']);
      expect(chipNames()).toContain('Remove filter Scope: Model subset');

      panel().clearFilters();
      fixture.detectChanges();
      pickFacet('comparison', '#14 — Second comparison');
      expect(rowKeys()).toEqual(['doc:33']);
      expect(chipNames()).toContain('Remove filter Comparison: #14 — Second comparison');
    });

    it('lists the Comparison facet only with documents of two comparisons, and never while one comparison\'s are listed', () => {
      render([numberedDoc(31, ExecutiveSummary), numberedDoc(32, TechnicalReport)], library('none', 'all'));
      expect(facetLabels()).not.toContain('Comparison');

      // A comparison's list, even holding documents of another number, offers no Comparison facet.
      hostComponent.context = library();
      fixture.detectChanges();
      expectList().flush([numberedDoc(31, ExecutiveSummary), numberedDoc(32, ExecutiveSummary, { comparisonId: 14 }), doc(33, TechnicalReport)]);
      fixture.detectChanges();
      expect(facetLabels()).not.toContain('Comparison');
      expect(byId('mc-dc-facet-comparison-trigger')).toBeNull();
    });

    it('opens each card\'s meta line with the comparison, and a subset\'s with how many of its models it covers', () => {
      render(fourDocuments(), library('none', 'all'));

      const meta = (key: string): string => text(`article[data-row-key="${key}"] .dc-card-meta`);
      expect(meta('doc:31')).toBe('Comparison #12·, Five-model comparison·, 2026-09-21 16:00 UTC·, Board Suite·, by Claude Opus writer');
      expect(meta('doc:32'))
        .toBe('Comparison #12·, Five-model comparison·, 2 of 5 models·, 2026-09-22 16:00 UTC·, Board Suite·, by Claude Opus writer');
      expect(meta('doc:33'))
        .toBe('Comparison #14·, Second comparison·, 2026-09-23 16:00 UTC·, GPT-5.6 Luna·, Board Suite·, by Claude Opus writer');
      expect(meta('doc:34')).toBe('2026-09-24 16:00 UTC·, Gemini Flash·, Board Suite·, by Claude Opus writer');
      expect(flat(rowEl('doc:32').querySelector('.dc-card-covered'))).toBe('2 of 5 models');
      expect(flat(rowEl('doc:31').querySelector('.dc-card-comparison'))).toBe('Comparison #12');
    });

    it('searches the comparison\'s number and name and the models a document covers', async () => {
      useSearchClock();
      render(fourDocuments(), library('none', 'all'));
      const input = byId<HTMLInputElement>('mc-dc-search')!;
      expect(input.placeholder).toBe('Search title, comparison, model, suite or writer');

      typeInto(input, '#14');
      await pauseTyping();
      expect(rowKeys()).toEqual(['doc:33']);

      typeInto(input, 'mistral');
      await pauseTyping();
      expect(rowKeys()).toEqual(['doc:31']);

      typeInto(input, 'second comparison');
      await pauseTyping();
      expect(rowKeys()).toEqual(['doc:33']);
    });

    // --- File names (D6), the server's BenchmarkReportCoverAndFileNameTests one for one ---

    it('names one model\'s document after the comparison number and the model, in both namings', () => {
      const perModel = (audience: BenchmarkReportAudience, subjectLabel = 'GPT-5.6 Luna'): BenchmarkReportDocumentListItemDto =>
        numberedDoc(1, audience, { subjectLabel, coveredModels: [{ entryKey: 'run:1', label: subjectLabel, provider: 'OpenAI', letter: null }] });

      expect(`${reportDocumentFileStem(perModel(ExecutiveSummary), 'x', Named)}_summary_named.pdf`)
        .toBe('comparison-12_gpt-5.6-luna_executive-summary_summary_named.pdf');
      expect(`${reportDocumentFileStem(perModel(TechnicalReport), 'x', Named)}_full_named_INTERNAL.pdf`)
        .toBe('comparison-12_gpt-5.6-luna_researcher-report_full_named_INTERNAL.pdf');
      expect(`${reportDocumentFileStem(perModel(TechnicalReport), 'x', Anonymized)}_detailed_anonymized.pdf`)
        .toBe('comparison-12_gpt-5.6-luna_researcher-report_detailed_anonymized.pdf');
      expect(`${reportDocumentFileStem(perModel(InternalBrief), 'x', Anonymized)}_full_anonymized_INTERNAL.docx`)
        .toBe('comparison-12_gpt-5.6-luna_internal-brief_full_anonymized_INTERNAL.docx');

      // Named unless told otherwise; the comparison's number alone is needed.
      expect(reportDocumentFileStem(perModel(ExecutiveSummary, 'GPT-5.6 Luna (max)'), 'x'))
        .toBe('comparison-12_gpt-5.6-luna-max_executive-summary');
      expect(reportDocumentFileStem(numberedDoc(1, ExecutiveSummary, { subjectLabel: 'GPT-5.6 Luna (max)', comparisonName: null }), 'x'))
        .toBe('comparison-12_gpt-5.6-luna-max_executive-summary');
    });

    it('names a document of the whole comparison after its name, and an anonymized copy after its number alone', () => {
      const whole = coveringDoc(1, ExecutiveSummary, true, [LUNA, GROK, MISTRAL, QWEN], {
        comparisonName: 'GPT-5.6 Luna (max) vs GPT-6.1 Sol (medium)'
      });

      expect(`${reportDocumentFileStem(whole, 'x', Named)}_summary_named.pdf`)
        .toBe('comparison-12_gpt-5.6-luna-max-vs-gpt-6.1-sol-medium_executive-summary_summary_named.pdf');
      expect(`${reportDocumentFileStem(whole, 'x', Anonymized)}_summary_anonymized.pdf`)
        .toBe('comparison-12_executive-summary_summary_anonymized.pdf');
      // A name the list does not send leaves it out in both namings.
      expect(reportDocumentFileStem({ ...whole, comparisonName: null }, 'x', Named)).toBe('comparison-12_executive-summary');
    });

    it('names a subset of up to three models after them, and a larger or anonymized one by its count', () => {
      const two = coveringDoc(1, ExecutiveSummary, false, [LUNA, GROK]);
      expect(`${reportDocumentFileStem(two, 'x', Named)}_summary_named.pdf`)
        .toBe('comparison-12_subset-gpt-5.6-luna-vs-grok-5_executive-summary_summary_named.pdf');
      expect(`${reportDocumentFileStem(two, 'x', Anonymized)}_summary_anonymized.pdf`)
        .toBe('comparison-12_subset-2-of-5-models-3f9a0c_executive-summary_summary_anonymized.pdf');

      const four = coveringDoc(1, InternalBrief, false, [LUNA, GROK, MISTRAL, QWEN]);
      expect(`${reportDocumentFileStem(four, 'x', Named)}_full_named_INTERNAL.pdf`)
        .toBe('comparison-12_subset-4-of-5-models-3f9a0c_internal-brief_full_named_INTERNAL.pdf');

      // A covered model the list knows only by its entry key is counted, as the server counts it.
      const unlabeled = coveringDoc(1, ExecutiveSummary, false, [LUNA, { entryKey: 'run:9', label: 'run:9', provider: null, letter: 'B' }]);
      expect(reportDocumentFileStem(unlabeled, 'x', Named)).toBe('comparison-12_subset-2-of-5-models-3f9a0c_executive-summary');

      // The count of models not Excluded when the document was written wins over the request's entry count.
      const afterExclusion = { ...four, comparisonModelCount: 4 };
      expect(reportDocumentFileStem(afterExclusion, 'x', Named)).toBe('comparison-12_subset-4-of-4-models-3f9a0c_internal-brief');
    });

    it('cuts a long covered slug at a hyphen within forty characters, without a dangling -vs', () => {
      const three = coveringDoc(1, ExecutiveSummary, false, [
        { entryKey: 'run:12', label: 'Claude 5.5 Opus Extended Thinking', provider: 'Anthropic', letter: 'A' },
        { entryKey: 'run:14', label: 'Gemini 3.8 Pro Deep Think', provider: 'Google', letter: 'B' },
        { entryKey: 'run:13', label: 'GPT-6.1 Sol Medium Reasoning', provider: 'OpenAI', letter: 'C' }
      ]);

      expect(`${reportDocumentFileStem(three, 'x', Named)}_summary_named.pdf`)
        .toBe('comparison-12_subset-claude-5.5-opus-extended-thinking_executive-summary_summary_named.pdf');
    });

    it('slugs a comparison name as safeFileName, cut at the last hyphen within forty characters', () => {
      const cases: [string, string][] = [
        ['GPT-5.6 Luna (max) vs GPT-6.1 Sol (medium)', 'gpt-5.6-luna-max-vs-gpt-6.1-sol-medium'],
        ['10 models · Core knowledge battery revision three', '10-models-core-knowledge-battery'],
        ['a'.repeat(50), 'a'.repeat(40)]
      ];
      for (const [name, slug] of cases) {
        expect(comparisonNameSlug(name), name).toBe(slug);
        expect(comparisonNameSlug(name).length).toBeLessThanOrEqual(COMPARISON_NAME_SLUG_MAX);
      }
    });

    it('keeps the earlier names for a document without a number, and for a run\'s or battery run\'s own documents', () => {
      const legacy = doc(1, ExecutiveSummary, { peerCount: 2, peerLetters: { 'run:2': 'A', 'group:4': 'B' } });
      expect(reportDocumentFileStem(legacy, 'x')).toBe('run-1_vs-run-2-group-4_executive-summary-gemini-flash');

      const run = doc(1, ExecutiveSummary, {
        origin: BenchmarkReportDocumentOrigin.RunCompletion, comparisonId: 12, comparisonName: 'Five-model comparison', peerCount: 0, peerLetters: {}
      });
      expect(reportDocumentFileStem(run, 'x', Named)).toBe('run-1_executive-summary-gemini-flash');

      const battery = doc(1, ExecutiveSummary, {
        origin: BenchmarkReportDocumentOrigin.BatteryCompletion, subjectKey: 'battery:9', comparisonId: 12, peerCount: 0, peerLetters: {}
      });
      expect(reportDocumentFileStem(battery, 'x', Anonymized)).toBe('battery-run-9_executive-summary-gemini-flash');
    });

    // --- The ZIP and the manifest, from the chosen rows ---

    it('names a ZIP from the chosen rows: one comparison, several, and documents without a number', () => {
      const lunaVsSol = 'GPT-5.6 Luna (max) vs GPT-6.1 Sol (medium)';
      render([
        numberedDoc(31, ExecutiveSummary, { subjectLabel: 'GPT-5.6 Luna', comparisonName: lunaVsSol }),
        numberedDoc(32, TechnicalReport, { subjectLabel: 'GPT-6.1 Sol', comparisonName: lunaVsSol }),
        numberedDoc(33, ExecutiveSummary, { comparisonId: 14, comparisonName: 'Second comparison' }),
        doc(34, ExecutiveSummary, { subjectLabel: 'Claude Harbor' }),
        doc(35, TechnicalReport, { subjectLabel: 'Claude Harbor' }),
        doc(36, ExecutiveSummary)
      ], library('none', 'all'));
      const context = hostComponent.context!;
      const chosen = (picks: [number, BenchmarkReportPeerNaming][]): DownloadChoice[] =>
        picks.map(([id, naming]) => ({ row: panel().rows.find(r => r.key === `doc:${id}`)!, state: { naming } }));

      expect(downloadZipStem(context, chosen([[31, Named], [32, Named]]))).toBe('comparison-12_gpt-5.6-luna-max-vs-gpt-6.1-sol-medium');
      // The newest listed row (doc:36, Gemini Flash) never names a package it is not in.
      expect(downloadZipStem(context, chosen([[32, Named]]))).toBe('comparison-12_gpt-5.6-luna-max-vs-gpt-6.1-sol-medium');
      expect(downloadZipStem(context, chosen([[34, Named], [35, Named]]))).toBe('claude-harbor');
      // Any anonymized copy leaves the comparison's name out.
      expect(downloadZipStem(context, chosen([[31, Named], [32, Anonymized]]))).toBe('comparison-12');
      expect(downloadZipStem(context, chosen([[31, Named], [33, Named]]))).toBe('comparison-reports');
      expect(downloadZipStem(context, chosen([[31, Named], [34, Named]]))).toBe('comparison-reports');
      expect(downloadZipStem(context, chosen([[34, Named], [36, Named]]))).toBe('comparison-reports');

      // A run or battery context keeps its model or label, whatever is chosen.
      const run: DownloadCenterContext = {
        kind: 'run',
        run: { id: 42, suiteName: 'Board Suite', modelLabel: 'GPT Model X', startedAtUtc: '2026-09-21T16:00:00Z', completedAtUtc: null },
        diagnosticsText: () => ''
      };
      expect(downloadZipStem(run, [])).toBe('gpt-model-x');
      expect(downloadZipStem({ kind: 'battery', batteryRunId: 7, label: 'Core Battery · GPT Model X' }, chosen([[31, Named]])))
        .toBe('core-battery-gpt-model-x');
      expect(downloadZipStem({ kind: 'battery', batteryRunId: 7, label: '' }, [])).toBe('battery-run-7');
    });

    it('lists the manifest\'s comparisons by number, each named only while every chosen document of it is named', () => {
      render([
        numberedDoc(31, ExecutiveSummary),
        numberedDoc(32, TechnicalReport),
        numberedDoc(33, ExecutiveSummary, { comparisonId: 14, comparisonName: 'Second comparison' }),
        doc(34, ExecutiveSummary)
      ], library('none', 'all'));
      const choice = (id: number, naming: BenchmarkReportPeerNaming): DownloadChoice =>
        ({ row: panel().rows.find(r => r.key === `doc:${id}`)!, state: { naming } });

      expect(manifestComparisons([choice(33, Named), choice(31, Named), choice(32, Named), choice(34, Named)]))
        .toEqual(['Comparison #12 — Five-model comparison', 'Comparison #14 — Second comparison']);
      expect(manifestComparisons([choice(31, Named), choice(32, Anonymized), choice(33, Named)]))
        .toEqual(['Comparison #12', 'Comparison #14 — Second comparison']);
      expect(manifestComparisons([choice(34, Named)])).toEqual([]);
    });
  });

  // -------------------------------------------------------------------------------------------
  // Charts
  // -------------------------------------------------------------------------------------------

  describe('charts', () => {
    it('shows no chart option, facet or action without chart actions', () => {
      render([doc(11, ExecutiveSummary), doc(12, TechnicalReport, { chartCount: 2, chartFigureKeys: ['p1a-quality'], chartSettingsHash: HASH })]);

      expect(q('.dc-option-charts')).toBeNull();
      expect(byId('mc-dc-facet-charts-trigger')).toBeNull();
      expect(Array.from(el.querySelectorAll('.dc-help-section > h4')).map(flat)).toEqual(['Sharing', 'Disclosure', 'Peer names', 'Formats']);
      expect(q('.dc-update-charts')).toBeNull();
      expect(q('.dc-more-btn')).toBeNull();
      expect(q('dialog.dc-charts-dialog')).toBeNull();
    });

    it('says None, current or differs from step 2 in the Charts option, and filters by it', () => {
      render([
        doc(11, ExecutiveSummary),
        doc(12, TechnicalReport, { chartCount: 6, chartFigureKeys: ['p1a-quality', 'p1b-speed', 'p1c-cost'], chartSettingsHash: HASH }),
        doc(13, InternalBrief, { chartCount: 6, chartFigureKeys: ['p1a-quality', 'p1b-speed', 'p1c-cost'], chartSettingsHash: OTHER_HASH })
      ], library(), chartActions());

      const state = (key: string): HTMLElement => rowEl(key).querySelector<HTMLElement>('.dc-charts-state')!;
      expect(state('doc:11').textContent!.trim()).toBe('None');
      expect(state('doc:12').textContent!.trim()).toBe('3 · current');
      expect(state('doc:12').classList).not.toContain('gh-tag-changed');
      expect(state('doc:13').textContent!.trim()).toBe('3 · differs from step 2');
      expect(state('doc:13').classList).toContain('gh-tag-changed');
      expect(flat(state('doc:11').closest('.dc-option-charts')!.querySelector('.dc-option-label'))).toBe('Charts');
      const help = Array.from(el.querySelectorAll('.dc-help-section'));
      expect(help.map(section => flat(section.querySelector('h4')))).toEqual(['Sharing', 'Disclosure', 'Peer names', 'Formats', 'Charts']);
      expect(Array.from(help[4].querySelectorAll('dt .gh-info-term')).map(flat)).toEqual(['None', 'current', 'differs from step 2']);

      expect(facetOptions('charts').map(option => option.label))
        .toEqual(['None, 1 document', 'Current, 1 document', 'Differs from step 2, 1 document']);
      pickFacet('charts', 'Differs from step 2');
      expect(rowKeys()).toEqual(['doc:13']);
    });

    it('keeps Update charts… aria-disabled with its reason until documents are chosen', () => {
      render([doc(11, ExecutiveSummary)], library('none'), chartActions());
      const button = q<HTMLButtonElement>('.dc-update-charts')!;

      expect(button.closest('.dc-selection')).not.toBeNull();
      expect(button.classList).toContain('btn-ghost');
      expect(button.getAttribute('aria-disabled')).toBe('true');
      expect(byId(button.getAttribute('aria-describedby')!)!.textContent!.trim()).toBe('Choose one or more report documents first.');
      button.click();
      expect(q<HTMLDialogElement>('dialog.dc-charts-dialog')!.open).toBe(false);

      check('doc:11');
      expect(button.hasAttribute('aria-disabled')).toBe(false);
    });

    it('opens Update charts on the chosen documents\' types, prefilled from their figures, with the advisory', () => {
      render([
        doc(11, ExecutiveSummary, { chartFigureKeys: ['s2-quality-cost'], chartCount: 2 }),
        doc(12, InternalBrief)
      ], library('none'), chartActions({ advisory: 'The dark theme prints poorly.' }));
      check('doc:11');
      check('doc:12');

      q<HTMLButtonElement>('.dc-update-charts')!.click();
      fixture.detectChanges();

      expect(q<HTMLDialogElement>('dialog.dc-charts-dialog')!.open).toBe(true);
      expect(text('.dc-charts-advisory')).toContain('The dark theme prints poorly.');
      const picker = fixture.debugElement.query(By.directive(ReportChartPickerComponent)).componentInstance as ReportChartPickerComponent;
      expect(picker.audiences).toEqual([ExecutiveSummary, InternalBrief]);
      expect(picker.enabledAudiences).toEqual([ExecutiveSummary, InternalBrief]);
      expect(panel().chartDraft[ExecutiveSummary]).toEqual(['s2-quality-cost']);
      // No figure yet: the remembered selection, here the defaults.
      expect(panel().chartDraft[InternalBrief]).toEqual(['p1a-quality', 'p1b-speed', 'p1c-cost']);
    });

    it('cancels Update charts without publishing', async () => {
      const actions = chartActions();
      render([doc(11, ExecutiveSummary)], library(), actions);
      q<HTMLButtonElement>('.dc-update-charts')!.click();
      fixture.detectChanges();
      const dialog = q<HTMLDialogElement>('dialog.dc-charts-dialog')!;

      const closed = new Promise<void>(resolve => dialog.addEventListener('close', () => resolve(), { once: true }));
      q<HTMLButtonElement>('.dc-charts-cancel')!.click();
      await closed;

      expect(dialog.open).toBe(false);
      expect(actions.publish).not.toHaveBeenCalled();
    });

    it('publishes the chosen documents with the draft selection, skipping a document on other prices with its reason', async () => {
      const actions = chartActions();
      let finish!: (result: ReportChartPublishResult) => void;
      actions.publish.mockImplementation((_targets: unknown, _selection: unknown, onProgress?: (p: {
        done: number;
        total: number;
        step: string;
        documentId: number | null;
      }) => void) => {
        onProgress?.({ done: 0, total: 1, step: 'Drawing Intelligence for Executive Summary: Gemini Flash', documentId: 11 });
        return new Promise<ReportChartPublishResult>(resolve => finish = resolve);
      });
      render([
        doc(11, ExecutiveSummary),
        doc(12, TechnicalReport, { pricingBasis: 'AsRun' }),
        doc(13, InternalBrief, { comparisonKey: 'cmp-other' })
      ], library(), actions);
      q<HTMLButtonElement>('.dc-update-charts')!.click();
      fixture.detectChanges();
      const draft: ReportChartSelection = { [ExecutiveSummary]: ['p1a-quality'], [TechnicalReport]: ['p1b-speed'], [InternalBrief]: ['p1c-cost'] };
      panel().onChartDraftChange(draft);

      const applying = panel().applyCharts();
      await settle();
      fixture.detectChanges();

      expect(actions.publish).toHaveBeenCalledTimes(1);
      const [targets, selection] = vi.mocked(actions.publish).mock.lastCall!;
      expect(targets).toEqual([{
        documentId: 11, audience: ExecutiveSummary, subjectKey: 'run:1',
        peerLetters: { 'run:2': 'A', 'group:4': 'B' }, label: 'Executive Summary: Gemini Flash'
      }]);
      expect(selection).toBe(draft);
      // The progress uses the preparing overlay.
      expect(q('.dc-preparing')).not.toBeNull();
      expect(text('.dc-preparing-step')).toBe('Drawing Intelligence for Executive Summary: Gemini Flash');

      finish(publishResult({ published: [{ documentId: 11, chartCount: 2 }] }));
      await applying;
      fixture.detectChanges();
      expectList().flush([doc(11, ExecutiveSummary, { chartFigureKeys: ['p1a-quality'], chartCount: 2, chartSettingsHash: HASH })]);
      fixture.detectChanges();

      expect(q('.dc-preparing')).toBeNull();
      const skips = text('.dc-chart-skips');
      expect(skips).toContain('It was written on prices at run time, and step 2 shows today’s prices.');
      expect(skips).toContain(CHART_SKIP_REASONS.otherComparison);
      expect(text('.dc-status')).toBe('Charts updated on 1 document.');
      expect(rowEl('doc:11').querySelector('.dc-charts-state')!.textContent!.trim()).toBe('1 · current');
      expect(hostComponent.changes).toBe(1);
    });

    it('lists a document whose charts failed', async () => {
      const actions = chartActions();
      actions.publish.mockResolvedValue(publishResult({ failed: [{ documentId: 11, message: 'the server answered 500' }] }));
      render([doc(11, ExecutiveSummary)], library(), actions);
      q<HTMLButtonElement>('.dc-update-charts')!.click();
      fixture.detectChanges();

      await panel().applyCharts();
      fixture.detectChanges();
      expectList().flush([doc(11, ExecutiveSummary)]);
      fixture.detectChanges();

      expect(text('.dc-chart-failures')).toContain('Executive Summary: Gemini Flash, 2026-09-21 16:00 UTC: the server answered 500');
    });

    it('closes the overlay and shows the server\'s message when chart storage is not configured', async () => {
      const actions = chartActions();
      actions.publish.mockResolvedValue(publishResult({ storageNotConfigured: 'Chart storage is not configured: set Benchmark:ReportChartsPath.' }));
      render([doc(11, ExecutiveSummary)], library(), actions);
      q<HTMLButtonElement>('.dc-update-charts')!.click();
      fixture.detectChanges();

      await panel().applyCharts();
      fixture.detectChanges();
      expectList().flush([doc(11, ExecutiveSummary)]);
      fixture.detectChanges();

      expect(q('.dc-preparing')).toBeNull();
      const warning = q('.dc-chart-storage')!;
      expect(warning.classList).toContain('alert-warning');
      expect(warning.textContent).toContain('Chart storage is not configured: set Benchmark:ReportChartsPath.');
    });

    it('refuses Update charts while chart storage is known to be missing', () => {
      render([doc(11, ExecutiveSummary)], library(), chartActions({ storageMissing: true }));

      expect(q('.dc-update-charts')!.getAttribute('aria-disabled')).toBe('true');
      expect(text('.dc-toolbar-reason')).toBe('Chart storage is not configured on the server.');
    });

    it('removes a document\'s charts from its More popover', () => {
      render([
        doc(11, ExecutiveSummary, { chartFigureKeys: ['p1a-quality'], chartCount: 2, chartSettingsHash: HASH }),
        doc(12, TechnicalReport)
      ], library(), chartActions());

      const trigger = byId<HTMLButtonElement>('mc-dc-doc-11-more-trigger')!;
      expect(trigger.getAttribute('aria-label')).toBe('More actions for Executive Summary: Gemini Flash, 2026-09-21 16:00 UTC');
      expect(trigger.getAttribute('popovertarget')).toBe('mc-dc-doc-11-more');
      const popover = byId('mc-dc-doc-11-more')!;
      expect(popover.classList).toContain('gh-action-popover');
      expect(popover.getAttribute('role')).toBe('group');
      const items = Array.from(popover.querySelectorAll<HTMLButtonElement>('.gh-action-popover-item'));
      expect(items.map(item => item.querySelector('span')!.textContent!.trim())).toEqual(['Update charts', 'Remove charts']);
      // Document 12 has no charts to remove, and says so.
      const remove12 = byId('mc-dc-doc-12-more')!.querySelector<HTMLButtonElement>('.dc-more-remove')!;
      expect(remove12.getAttribute('aria-disabled')).toBe('true');
      expect(remove12.textContent).toContain('It has no charts.');

      items[1].click();
      const request = http.expectOne(r => r.method === 'DELETE');
      expect(request.request.url).toBe(`${DOCUMENTS_URL}/11/charts`);
      request.flush(null);
      fixture.detectChanges();
      expectList().flush([doc(11, ExecutiveSummary), doc(12, TechnicalReport)]);
      fixture.detectChanges();

      expect(text('.dc-status')).toBe('Removed the charts of Executive Summary: Gemini Flash, 2026-09-21 16:00 UTC.');
      expect(rowEl('doc:11').querySelector('.dc-charts-state')!.textContent!.trim()).toBe('None');
      expect(hostComponent.changes).toBe(1);
    });
  });

  // -------------------------------------------------------------------------------------------
  // The battery context
  // -------------------------------------------------------------------------------------------

  describe('battery context', () => {
    const JOB_URL = '/api/admin/benchmark/batteries/runs/7/report-documents/job';
    const REPORT_URL = '/api/admin/benchmark/batteries/runs/7/report';
    const BATTERY: DownloadCenterBatteryContext = { kind: 'battery', batteryRunId: 7, label: 'Core Battery · Gemini Flash' };

    /** A document about battery run 7: a battery-completion one unless `origin` says otherwise. */
    function batteryDoc(id: number, audience: BenchmarkReportAudience, overrides: Partial<BenchmarkReportDocumentListItemDto> = {}): BenchmarkReportDocumentListItemDto {
      return doc(id, audience, {
        subjectKey: 'battery:7',
        subjectLabel: 'Gemini Flash',
        subjectRunIds: [101, 102],
        suiteId: null,
        suiteName: '',
        // A battery-completion document; the client enum lists the first two origins only.
        origin: 3 as number as BenchmarkReportDocumentOrigin,
        comparisonKey: null,
        comparisonEntryCount: 1,
        peerCount: 0,
        peerLetters: {},
        ...overrides
      });
    }

    function job(phase: BenchmarkRunReportJobPhase, status = BenchmarkRunReportDocumentsStatus.Writing): BenchmarkRunReportJobDto {
      return {
        runId: 7, status, message: null, phase,
        queuedAtUtc: '2026-10-03T08:00:00Z', slotAcquiredAtUtc: null, finishedAtUtc: null, cancelRequestedAtUtc: null,
        jobsAhead: null, blockingJobLabel: null, audiences: [ExecutiveSummary, TechnicalReport],
        writerConfigId: 7, writerDisplayName: 'Claude Opus writer', writerProvider: 'Anthropic', writerModelId: 'claude-opus',
        writerThinkingLevel: null, job: null as unknown as BenchmarkRunReportJobDto['job'], serverTimeUtc: '2026-10-03T08:00:10Z'
      };
    }

    function flushJob(view: BenchmarkRunReportJobDto | null): void {
      const request = http.expectOne(JOB_URL);
      expect(request.request.method).toBe('GET');
      if (view) {
        request.flush(view);
      } else {
        request.flush(null, { status: 204, statusText: 'No Content' });
      }
    }

    const BATTERY_RUN_URL = '/api/admin/benchmark/batteries/runs/7';

    /** A current, usable member in suite 0, round 1, unless the overrides say otherwise. */
    function member(runId: number, overrides: Partial<BenchmarkBatteryMemberDto> = {}): BenchmarkBatteryMemberDto {
      return {
        memberId: runId, suiteIndex: 0, round: 1, runId, runStatus: 'Completed', origin: 'Launched',
        superseded: false, usable: true, addedAtUtc: '2026-10-01T07:59:00Z',
        runStartedAtUtc: '2026-10-01T08:00:00Z', runCompletedAtUtc: '2026-10-01T08:30:00Z',
        answeredQuestionCount: 16, totalQuestionCount: 16,
        ...overrides
      };
    }

    /** Battery run 7 of Core Battery, over Board Suite (0) and Wiki Suite (1), with the given members. */
    function batteryRun(members: BenchmarkBatteryMemberDto[]): BenchmarkBatteryRunDto {
      return {
        id: 7,
        batteryName: 'Core Battery',
        testedModelLabel: 'Gemini Flash',
        suites: [{ index: 0, suiteId: 5, suiteName: 'Board Suite' }, { index: 1, suiteId: 6, suiteName: 'Wiki Suite' }],
        members
      } as unknown as BenchmarkBatteryRunDto;
    }

    /** The battery run's member list request. */
    function expectMembers(): TestRequest {
      return http.expectOne(r => r.method === 'GET' && r.url === BATTERY_RUN_URL);
    }

    /**
     * Renders the battery context and answers its own document list, its comparison count, its job
     * request and its member list.
     */
    function openBattery(
      documents: BenchmarkReportDocumentListItemDto[],
      view: BenchmarkRunReportJobDto | null = null,
      options: { members?: BenchmarkBatteryMemberDto[]; comparison?: BenchmarkReportDocumentListItemDto[]; context?: DownloadCenterContext } = {}
    ): TestRequest {
      hostComponent.context = options.context ?? BATTERY;
      fixture.detectChanges();
      const list = expectList('batteryCompletion');
      list.flush(documents);
      expectList('reportPack').flush(options.comparison ?? []);
      flushJob(view);
      expectMembers().flush(batteryRun(options.members ?? []));
      fixture.detectChanges();
      return list;
    }

    it('lists the analysis report and the battery run\'s own documents, by subject and origin', () => {
      const list = openBattery([batteryDoc(21, ExecutiveSummary), batteryDoc(22, TechnicalReport), batteryDoc(23, InternalBrief)]);

      expect(list.request.params.get('subject')).toBe('battery:7');
      expect(list.request.params.get('origin')).toBe('batteryCompletion');
      expect(list.request.params.has('runId')).toBe(false);
      expect([...rowKeys()].sort()).toEqual(['battery-report:7', 'doc:21', 'doc:22', 'doc:23']);
      expect(panel().rows.some(row => row.runId !== null)).toBe(false);

      const report = rowEl('battery-report:7');
      expect(flat(report.querySelector('.dc-card-type'))).toBe('Battery analysis report');
      expect(flat(report.querySelector('.dc-card-title'))).toBe('Battery analysis report, battery run #7');
      expect(flat(report.querySelector('.dc-doc-detail'))).toBe('Core Battery · Gemini Flash');
      expect(report.querySelector('.gh-tag-internal')).not.toBeNull();
      expect(report.querySelector('.dc-view-btn')).toBeNull();
      expect(report.querySelector('.dc-delete-btn')).toBeNull();
      expect(panel().rows.find(row => row.key === 'battery-report:7')!.formats).toEqual(['md']);

      // A battery-completion document is viewed here, never deleted.
      expect(rowEl('doc:21').querySelector('.dc-view-btn')).not.toBeNull();
      expect(rowEl('doc:21').querySelector('.dc-delete-btn')).toBeNull();
    });

    it('points to the comparison documents about the battery run, and opens them in a subject library', () => {
      openBattery([batteryDoc(21, ExecutiveSummary)], null, {
        comparison: [
          batteryDoc(31, ExecutiveSummary, { origin: BenchmarkReportDocumentOrigin.ReportPack, peerCount: 1 }),
          batteryDoc(32, TechnicalReport, { origin: BenchmarkReportDocumentOrigin.ReportPack, peerCount: 1 })
        ]
      });

      expect(text('.dc-comparison-pointer-text')).toBe(
        '2 comparison documents compare this battery run with other models. They are kept with their comparisons.');
      expect(rowKeys()).not.toContain('doc:31');
      const button = q<HTMLButtonElement>('.dc-comparison-pointer .dc-open-comparison-documents')!;
      expect(button.getAttribute('type')).toBe('button');
      expect(button.classList.contains('btn-ghost')).toBe(true);
      expect(flat(button)).toBe('Open comparison documents');
      expect(hostComponent.comparisonDocuments).toEqual([]);

      button.click();
      fixture.detectChanges();

      expect(hostComponent.comparisonDocuments).toEqual([{
        kind: 'library',
        scope: { kind: 'subject', subjectKey: 'battery:7', label: 'battery run #7 · Core Battery · Gemini Flash' },
        preselect: 'none'
      }]);
      // The pointer sits above the documents.
      expect(q('.dc-comparison-pointer')!.compareDocumentPosition(rowEl('doc:21')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('says one comparison document in the singular, and shows no pointer for none or a failed count', () => {
      openBattery([], null, { comparison: [batteryDoc(31, ExecutiveSummary, { origin: BenchmarkReportDocumentOrigin.ReportPack })] });
      expect(text('.dc-comparison-pointer-text')).toBe(
        '1 comparison document compares this battery run with other models. It is kept with its comparison.');

      openBattery([], null, { comparison: [], context: { ...BATTERY } });
      expect(q('.dc-comparison-pointer')).toBeNull();

      hostComponent.context = { ...BATTERY };
      fixture.detectChanges();
      expectList('batteryCompletion').flush([]);
      expectList('reportPack').flush({ error: 'Boom' }, { status: 500, statusText: 'Server Error' });
      flushJob(null);
      expectMembers().flush(batteryRun([]));
      fixture.detectChanges();
      expect(q('.dc-comparison-pointer')).toBeNull();
      expect(el.querySelectorAll('.dc-notice').length).toBe(0);
    });

    it('names a battery run\'s documents after it', () => {
      const unprefixed = reportDocumentFileStem(batteryDoc(21, ExecutiveSummary, { subjectKey: 'group:4' }), '');
      expect(reportDocumentFileStem(batteryDoc(21, ExecutiveSummary), '')).toBe(`battery-run-7_${unprefixed}`);
      const researcher = reportDocumentFileStem(batteryDoc(22, TechnicalReport), '');
      expect(researcher.startsWith('battery-run-7_')).toBe(true);
      expect(researcher.endsWith('_Researcher_Report')).toBe(true);
      expect(reportDocumentFileStem(batteryDoc(23, ExecutiveSummary, { peerCount: 2 }), '')).toBe(`battery-run-7_vs-2-models_${unprefixed}`);
    });

    it('downloads the analysis report as Markdown under the server\'s name, marked internal', async () => {
      const saveText = vi.spyOn(downloadCenterIo, 'saveText').mockReturnValue(undefined);
      openBattery([batteryDoc(21, ExecutiveSummary)]);
      check('doc:21');
      expect(panel().isIncluded(panel().rows.find(row => row.key === 'battery-report:7')!)).toBe(true);

      const done = panel().download();
      const request = http.expectOne(r => r.method === 'GET' && r.url === REPORT_URL);
      expect(request.request.responseType).toBe('text');
      request.flush('# Battery run #7\n', {
        headers: { 'Content-Disposition': 'attachment; filename="Core_Battery_battery-run-7_20261002_090000.md"' }
      });
      await done;
      fixture.detectChanges();

      expect(saveText).toHaveBeenCalledTimes(1);
      const [fileName, content, mime] = vi.mocked(saveText).mock.lastCall!;
      expect(fileName).toBe('Core_Battery_battery-run-7_20261002_090000_INTERNAL.md');
      expect(content).toBe('# Battery run #7\n');
      expect(mime).toBe('text/markdown;charset=utf-8');
      expect(text('.dc-status')).toBe('Downloaded 1 file.');
    });

    it('lists the analysis report alone, with a notice, when the documents cannot be loaded', () => {
      hostComponent.context = BATTERY;
      fixture.detectChanges();
      expectList('batteryCompletion').flush({ error: 'Boom' }, { status: 500, statusText: 'Server Error' });
      expectList('reportPack').flush([]);
      flushJob(null);
      expectMembers().flush(batteryRun([]));
      fixture.detectChanges();

      expect(rowKeys()).toEqual(['battery-report:7']);
      expect(text('.dc-notice')).toBe('The report documents of this battery run could not be loaded; the analysis report is still listed.');
    });

    it('shows nothing about writing when the battery run has no job', () => {
      openBattery([batteryDoc(21, ExecutiveSummary)]);

      expect(panel().reportJobPhase).toBeNull();
      expect(q('.dc-report-job-notice')).toBeNull();
      http.expectNone(JOB_URL);
    });

    it('shows a notice while the reports are being written and polls the job every 5 s', fakeAsync(() => {
      openBattery([], job('Queued', BenchmarkRunReportDocumentsStatus.Pending));

      expect(text('.dc-report-job-notice')).toBe(
        'The AI-written reports of this battery run are being written (waiting for the report writer). They appear here when they are done.');
      expect(q('.dc-report-job-status')!.getAttribute('role')).toBe('status');

      tick(DOWNLOAD_CENTER_REPORT_JOB_POLL_MS - 1);
      http.expectNone(JOB_URL);
      tick(1);
      flushJob(job('Writing'));
      fixture.detectChanges();
      expect(text('.dc-report-job-notice')).toContain('(writing)');

      // Finished: the battery run's documents are listed again, and the notice goes.
      tick(DOWNLOAD_CENTER_REPORT_JOB_POLL_MS);
      flushJob(job('Finished', BenchmarkRunReportDocumentsStatus.Completed));
      const reload = expectList('batteryCompletion');
      expect(reload.request.params.get('subject')).toBe('battery:7');
      reload.flush([batteryDoc(21, ExecutiveSummary), batteryDoc(22, TechnicalReport)]);
      fixture.detectChanges();

      expect(q('.dc-report-job-notice')).toBeNull();
      expect([...rowKeys()].sort()).toEqual(['battery-report:7', 'doc:21', 'doc:22']);
      tick(DOWNLOAD_CENTER_REPORT_JOB_POLL_MS * 2);
      http.expectNone(JOB_URL);
    }));

    it('stops polling the job when the panel is deactivated', fakeAsync(() => {
      openBattery([], job('Writing'));
      expect(q('.dc-report-job-notice')).not.toBeNull();

      panel().deactivate();
      tick(DOWNLOAD_CENTER_REPORT_JOB_POLL_MS * 2);
      http.expectNone(JOB_URL);
    }));

    describe('Include member runs', () => {
      const includeBox = (): HTMLInputElement => byId<HTMLInputElement>('mc-dc-include-members')!;

      function toggleMembers(): void {
        includeBox().click();
        fixture.detectChanges();
      }

      /** Members out of order, one unusable, one superseded and one deleted. */
      function members(): BenchmarkBatteryMemberDto[] {
        return [
          member(102, { suiteIndex: 1, usable: false, unusableReason: 'failed' }),
          member(103, { superseded: true }),
          member(101),
          member(104, { suiteIndex: 1, round: 2, runStatus: 'Deleted' }),
          member(105, { round: 2 })
        ];
      }

      it('is checked whenever a battery run opens, with its click-mode explanation, and lists every current member\'s report and log', () => {
        openBattery([batteryDoc(21, ExecutiveSummary)], null, { members: members() });

        const box = includeBox();
        expect(box.checked).toBe(true);
        expect(box.closest('label')!.textContent!.trim()).toBe('Include member runs');
        expect(box.getAttribute('aria-describedby')).toBe('mc-dc-include-members-tip');
        expect(box.closest('.dc-selection')).not.toBeNull();
        expect(flat(byId('mc-dc-include-members-tip'))).toBe(INCLUDE_MEMBER_RUNS_TIP);
        expect(q('.dc-include-members .gh-info-btn')!.getAttribute('aria-label')).toBe('About Include member runs');

        // By suite, then round; no superseded or deleted member; no diagnostics without the host's callback.
        const memberKeys = panel().rows.filter(row => row.runId !== null).map(row => row.key);
        expect(memberKeys).toEqual(['report:101', 'log:101', 'report:105', 'log:105', 'report:102', 'log:102']);

        const report = panel().rows.find(row => row.key === 'report:101')!;
        expect(report.label).toBe('Run report, run #101');
        expect(report.detail).toBe('Board Suite · round 1 · run #101');
        expect(report.suite).toBe('Board Suite');
        expect(report.subject).toBe('Gemini Flash');
        expect(report.note).toBeNull();
        expect(panel().rows.find(row => row.key === 'log:105')!.detail).toBe('Board Suite · round 2 · run #105');

        // An unusable member is listed, and says it is not used in the battery's statistics.
        const unusable = panel().rows.find(row => row.key === 'report:102')!;
        expect(unusable.detail).toBe('Wiki Suite · round 1 · run #102');
        expect(unusable.note).toBe(ROW_NOTES.unusableMember);
        expect(panel().rows.find(row => row.key === 'log:102')!.note).toBe(`${ROW_NOTES.unusableMember} ${ROW_NOTES.toolCallLog}`);

        // Internal chooses every member file; External cannot choose them.
        expect(memberKeys.every(key => panel().isIncluded(panel().rows.find(row => row.key === key)!))).toBe(true);
        panel().selectPackage('provider');
        fixture.detectChanges();
        expect(memberKeys.some(key => panel().isSelectable(panel().rows.find(row => row.key === key)!))).toBe(false);
      });

      it('is checked again on the next opening after it was unchecked', () => {
        openBattery([], null, { members: [member(101)] });
        toggleMembers();
        expect(includeBox().checked).toBe(false);

        openBattery([], null, { members: [member(101)], context: { ...BATTERY } });
        expect(includeBox().checked).toBe(true);
        expect(rowKeys()).toContain('report:101');
      });

      it('removes the member rows when unchecked, and adds them again, preset again, when checked', () => {
        openBattery([batteryDoc(21, ExecutiveSummary)], null, { members: [member(101), member(102, { suiteIndex: 1 })] });
        check('report:101');
        expect(panel().isIncluded(panel().rows.find(row => row.key === 'report:101')!)).toBe(false);

        toggleMembers();
        expect(panel().rows.map(row => row.key).sort()).toEqual(['battery-report:7', 'doc:21']);
        expect(rowKeys().sort()).toEqual(['battery-report:7', 'doc:21']);

        toggleMembers();
        http.expectNone(BATTERY_RUN_URL);
        expect(panel().rows.filter(row => row.runId !== null).map(row => row.key))
          .toEqual(['report:101', 'log:101', 'report:102', 'log:102']);
        expect(panel().isIncluded(panel().rows.find(row => row.key === 'report:101')!)).toBe(true);
      });

      it('narrows a large battery with the Suite and Document facets', () => {
        openBattery([], null, { members: [member(101), member(102, { suiteIndex: 1 }), member(103, { suiteIndex: 1, round: 2 })] });

        pickFacet('suite', 'Wiki Suite');
        pickFacet('document', 'Tool-call log');
        expect([...rowKeys()].sort()).toEqual(['log:102', 'log:103']);
      });

      it('says when the member runs cannot be listed, and lists them when checked again', () => {
        hostComponent.context = BATTERY;
        fixture.detectChanges();
        expectList('batteryCompletion').flush([batteryDoc(21, ExecutiveSummary)]);
        expectList('reportPack').flush([]);
        flushJob(null);
        expectMembers().flush({ error: 'Boom' }, { status: 500, statusText: 'Server Error' });
        fixture.detectChanges();

        expect(text('.dc-member-notice')).toBe(MEMBER_RUNS_FAILED_NOTICE);
        expect([...rowKeys()].sort()).toEqual(['battery-report:7', 'doc:21']);

        toggleMembers();
        expect(q('.dc-member-notice')).toBeNull();
        http.expectNone(BATTERY_RUN_URL);

        toggleMembers();
        expect(text('.dc-loading-members')).toBe('Listing the member runs…');
        expect(panel().canDownload).toBe(false);
        expectMembers().flush(batteryRun([member(101)]));
        fixture.detectChanges();
        expect(q('.dc-loading-members')).toBeNull();
        expect(rowKeys()).toContain('report:101');
        expect(panel().canDownload).toBe(true);
      });

      it('lists member diagnostics with the host\'s callback, and captures them from the member run\'s detail at preparation', async () => {
        const saveText = vi.spyOn(downloadCenterIo, 'saveText').mockReturnValue(undefined);
        vi.spyOn(downloadCenterIo, 'now').mockReturnValue(new Date('2026-10-06T12:00:00Z'));
        const capture = vi.fn((run: BenchmarkRunDetailDto) => `=== DIAGNOSTICS of run ${run.id} ===\n`);
        openBattery([], null, {
          members: [member(101), member(102, { suiteIndex: 1, usable: false })],
          context: { ...BATTERY, memberDiagnosticsText: capture }
        });

        expect(panel().rows.filter(row => row.kind === 'diagnostics').map(row => row.key)).toEqual(['diag:101', 'diag:102']);
        const diagnostics = panel().rows.find(row => row.key === 'diag:102')!;
        expect(diagnostics.label).toBe('Run diagnostics, run #102');
        expect(diagnostics.note).toBe(`${ROW_NOTES.unusableMember} ${ROW_NOTES.diagnostics}`);
        expect(capture).not.toHaveBeenCalled();

        panel().selectPackage('custom');
        for (const row of panel().rows) {
          const state = panel().stateOf(row);
          state.selected = row.key === 'diag:101';
          state.formats = row.key === 'diag:101' ? ['txt'] : state.formats;
        }
        fixture.detectChanges();

        const done = panel().download();
        await settle();
        http.expectOne(r => r.method === 'GET' && r.url === '/api/admin/benchmark/runs/101').flush({ id: 101 } as BenchmarkRunDetailDto);
        await done;
        fixture.detectChanges();

        expect(capture).toHaveBeenCalledTimes(1);
        expect(capture.mock.calls[0][0].id).toBe(101);
        const [fileName, content] = vi.mocked(saveText).mock.lastCall!;
        expect(fileName).toBe('board-suite_gemini-flash_run101_diagnostics_INTERNAL.txt');
        expect(content).toBe('=== DIAGNOSTICS of run 101 ===\n');
        http.expectNone(r => r.url === '/api/admin/benchmark/runs/102');
      });
    });
  });

  // -------------------------------------------------------------------------------------------
  // The run context
  // -------------------------------------------------------------------------------------------

  describe('run context', () => {
    const JOB_URL = '/api/admin/benchmark/runs/42/report-documents/job';

    function runContext(batteryRunId: number | null = null): DownloadCenterContext {
      return {
        kind: 'run',
        run: {
          id: 42,
          suiteName: 'Board Suite',
          modelLabel: 'Gemini Flash',
          startedAtUtc: '2026-10-02T09:00:00Z',
          completedAtUtc: '2026-10-02T09:40:00Z',
          batteryRunId
        },
        diagnosticsText: () => ''
      };
    }

    /** A comparison document about run 42. */
    function comparisonDoc(id: number): BenchmarkReportDocumentListItemDto {
      return doc(id, ExecutiveSummary, { subjectKey: 'run:42', subjectRunIds: [42] });
    }

    /**
     * Renders a run context and answers its own document list, its comparison count (none unless
     * given) and its job request (no job).
     */
    function openRun(context: DownloadCenterContext, comparison: BenchmarkReportDocumentListItemDto[] = []): TestRequest {
      hostComponent.context = context;
      fixture.detectChanges();
      const list = expectList('runCompletion');
      list.flush([doc(31, ExecutiveSummary, {
        subjectKey: 'run:42',
        subjectRunIds: [42],
        origin: BenchmarkReportDocumentOrigin.RunCompletion,
        comparisonKey: null,
        peerCount: 0,
        peerLetters: {}
      })]);
      const count = expectList('reportPack');
      expect(count.request.params.get('subject')).toBe('run:42');
      count.flush(comparison);
      const job = http.expectOne(JOB_URL);
      job.flush(null, { status: 204, statusText: 'No Content' });
      fixture.detectChanges();
      return list;
    }

    const pointer = (): HTMLElement | null => q('.dc-battery-pointer');
    const comparisonPointer = (): HTMLElement | null => q('.dc-comparison-pointer');

    it('lists the run\'s files and only its own run-completion documents', () => {
      const list = openRun(runContext());

      expect(list.request.params.get('subject')).toBe('run:42');
      expect(list.request.params.get('origin')).toBe('runCompletion');
      expect(list.request.params.has('runId')).toBe(false);
      expect([...rowKeys()].sort()).toEqual(['diag:42', 'doc:31', 'log:42', 'report:42']);
      // A run context offers no member runs.
      expect(byId('mc-dc-include-members')).toBeNull();
      http.expectNone(r => r.url.includes('/batteries/'));
    });

    it('views the run report row as the run report PDF', () => {
      const service = TestBed.inject(AdminBenchmarkService);
      const pdf = vi.spyOn(service, 'getRunReportPdf').mockReturnValue(of({ bytes: new Uint8Array([1]), fileName: null }));
      openRun(runContext());
      const open = vi.spyOn(panel().pdfViewer!, 'open').mockReturnValue(undefined);

      const rid = panel().rowId(panel().rows.find(r => r.key === 'report:42')!);
      byId<HTMLButtonElement>(`${rid}-view`)!.click();

      const request = vi.mocked(open).mock.lastCall![0] as PdfViewerRequest;
      expect(request.title).toBe('Run report, run #42');
      expect(request.variants).toBeUndefined();
      request.load(null).subscribe();
      expect(pdf).toHaveBeenCalledWith(42, expect.any(String));
      // A run report is never deleted here.
      expect(byId(`${rid}-delete`)).toBeNull();
    });

    it('points to the comparison documents about the run instead of listing them, and opens them in a subject library', () => {
      openRun(runContext(), [comparisonDoc(51), comparisonDoc(52), comparisonDoc(53)]);

      expect(rowKeys()).not.toContain('doc:51');
      expect(text('.dc-comparison-pointer-text')).toBe(
        '3 comparison documents compare this run with other models. They are kept with their comparisons.');
      const button = q<HTMLButtonElement>('.dc-comparison-pointer .dc-open-comparison-documents')!;
      expect(button.getAttribute('type')).toBe('button');
      expect(button.classList.contains('btn-ghost')).toBe(true);
      expect(flat(button)).toBe('Open comparison documents');

      button.click();
      fixture.detectChanges();

      expect(hostComponent.comparisonDocuments).toEqual([{
        kind: 'library',
        scope: { kind: 'subject', subjectKey: 'run:42', label: 'run #42 · Board Suite · Gemini Flash' },
        preselect: 'none'
      }]);
      expect(comparisonPointer()!.compareDocumentPosition(rowEl('doc:31')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('shows no comparison pointer without comparison documents, or in a library context', () => {
      openRun(runContext());
      expect(comparisonPointer()).toBeNull();

      render([doc(11, ExecutiveSummary)]);
      expect(comparisonPointer()).toBeNull();
    });

    it('shows both pointers for a battery member with comparison documents', () => {
      openRun(runContext(7), [comparisonDoc(51)]);

      expect(pointer()).not.toBeNull();
      expect(text('.dc-comparison-pointer-text')).toBe(
        '1 comparison document compares this run with other models. It is kept with its comparison.');
    });

    it('shows no battery pointer for a run outside a battery', () => {
      openRun(runContext());

      expect(pointer()).toBeNull();
      expect(panel().memberOfBatteryRunId).toBeNull();
    });

    it('points a battery member to the battery run\'s downloads, emitting the battery run id', () => {
      openRun(runContext(7));

      expect(text('.dc-battery-pointer-text')).toBe(
        "This run is a member of battery run #7. Its AI-written documents are in the battery run's downloads.");
      const button = q<HTMLButtonElement>('.dc-battery-pointer .dc-open-battery-downloads')!;
      expect(button.getAttribute('type')).toBe('button');
      expect(button.classList.contains('btn-ghost')).toBe(true);
      expect(flat(button)).toBe('Open battery run downloads');
      expect(hostComponent.batteryDownloads).toEqual([]);

      button.click();
      fixture.detectChanges();

      expect(hostComponent.batteryDownloads).toEqual([7]);
      // The pointer sits above the documents.
      expect(pointer()!.compareDocumentPosition(rowEl('doc:31')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('shows no battery pointer in a library context', () => {
      render([doc(11, ExecutiveSummary)]);

      expect(pointer()).toBeNull();
    });
  });

  // -------------------------------------------------------------------------------------------
  // The footer's Cancel
  // -------------------------------------------------------------------------------------------

  describe('the footer Cancel', () => {
    const cancelButton = (): HTMLButtonElement | null => q<HTMLButtonElement>('.dc-footer .dc-cancel');
    const downloadButton = (): HTMLButtonElement => q<HTMLButtonElement>('.dc-footer .dc-download')!;

    function spySaves(): Mock[] {
      return [
        vi.spyOn(downloadCenterIo, 'saveText').mockReturnValue(undefined),
        vi.spyOn(downloadCenterIo, 'saveBytes').mockReturnValue(undefined),
        vi.spyOn(downloadCenterIo, 'saveBlob').mockReturnValue(undefined)
      ] as unknown as Mock[];
    }

    it('is absent while no download is prepared', () => {
      render([doc(1, ExecutiveSummary), doc(2, TechnicalReport)]);

      expect(cancelButton()).toBeNull();
      expect(downloadButton()).not.toBeNull();
    });

    it('appears while a download is prepared; it stops the download, keeps the panel, saves nothing and says so', async () => {
      const saves = spySaves();
      render([doc(1, ExecutiveSummary), doc(2, TechnicalReport)]);
      const keys = rowKeys();
      expect(panel().plannedFiles.length).toBeGreaterThan(1);

      const done = panel().download();
      const pending = http.match(() => true);
      expect(pending.length).toBe(1);
      fixture.detectChanges();

      const cancel = cancelButton()!;
      expect(cancel).not.toBeNull();
      expect(cancel.classList.contains('btn-gh-cancel')).toBe(true);
      expect(flat(cancel)).toBe('Cancel');
      expect(cancel.querySelector('svg')).toBeNull();
      expect(cancel.nextElementSibling).toBe(downloadButton());

      cancel.click();
      fixture.detectChanges();

      expect(pending[0].cancelled).toBe(true);
      expect(panel().preparing).toBe(false);
      expect(panel().progress).toBeNull();
      expect(q('.dc-preparing')).toBeNull();
      expect(cancelButton()).toBeNull();
      expect(q('.dc-status')!.getAttribute('role')).toBe('status');
      expect(text('.dc-status')).toBe('Download canceled. Nothing was saved.');
      expect(document.activeElement).toBe(downloadButton());
      expect(rowKeys()).toEqual(keys);

      await done;
      await settle();
      fixture.detectChanges();

      http.expectNone(() => true);
      for (const save of saves) {
        expect(save).not.toHaveBeenCalled();
      }
      expect(text('.dc-status')).toBe('Download canceled. Nothing was saved.');
      expect(panel().canDownload).toBe(true);
    });

    it('aborts a download in preparation when the host takes the context away, without announcing a cancel', async () => {
      const saves = spySaves();
      render([doc(1, ExecutiveSummary), doc(2, TechnicalReport)]);

      const done = panel().download();
      const pending = http.match(() => true);
      expect(pending.length).toBe(1);

      hostComponent.context = null;
      fixture.detectChanges();

      expect(pending[0].cancelled).toBe(true);
      expect(panel().preparing).toBe(false);
      await done;
      await settle();

      http.expectNone(() => true);
      for (const save of saves) {
        expect(save).not.toHaveBeenCalled();
      }
      expect(panel().statusMessage).not.toBe('Download canceled. Nothing was saved.');
    });

    it('aborts a download in preparation when the panel is destroyed', async () => {
      const saves = spySaves();
      render([doc(1, ExecutiveSummary), doc(2, TechnicalReport)]);

      const done = panel().download();
      const pending = http.match(() => true);
      expect(pending.length).toBe(1);

      fixture.destroy();

      expect(pending[0].cancelled).toBe(true);
      await done;
      await settle();
      for (const save of saves) {
        expect(save).not.toHaveBeenCalled();
      }
    });
  });
});
