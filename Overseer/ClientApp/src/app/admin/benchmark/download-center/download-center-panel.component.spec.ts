import type { Mock } from "vitest";
import { ChangeDetectionStrategy, Component, ViewChild } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportDocumentOrigin,
  BenchmarkReportPeerNaming,
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
  DOWNLOAD_CENTER_REPORT_JOB_POLL_MS,
  DOWNLOAD_CENTER_SEARCH_DEBOUNCE_MS,
  DOWNLOAD_CENTER_STORAGE_KEY,
  DOWNLOAD_CENTER_VIEW_STORAGE_KEY,
  DOWNLOAD_SORTS,
  DownloadCenterChartActions,
  DownloadCenterContext,
  DownloadCenterPanelComponent,
  downloadCenterIo,
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
                                 [chartActions]="actions" (documentsChanged)="changes = changes + 1"></app-download-center-panel>
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

  function library(preselect: 'all' | 'none' = 'all', scope: 'comparison' | 'all' = 'comparison'): DownloadCenterContext {
    return {
      kind: 'library',
      scope: scope === 'comparison' ? { kind: 'comparison', entryKeys: ENTRY_KEYS } : { kind: 'all' },
      preselect
    };
  }

  function expectList(): TestRequest {
    return http.expectOne(r => r.method === 'GET' && r.url === DOCUMENTS_URL);
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
    it('lists a comparison\'s documents by one list call, with the reports of their runs', () => {
      const request = render([doc(11, ExecutiveSummary), doc(12, TechnicalReport, { subjectKey: 'run:2', subjectRunIds: [2] })]);

      expect(request.request.params.get('comparison')).toBe('run:1,run:2,group:4');
      expect(request.request.params.get('origin')).toBe('reportPack');
      expect(request.request.params.has('take')).toBe(false);
      http.expectNone(r => /report-documents\/\d+$/.test(r.url));
      expect(panel().rows.map(r => r.key)).toEqual(['doc:12', 'doc:11', 'report:1', 'report:2']);
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
      expect(panel().selectedCount).toBe(3);
      expect(rowEl('doc:11').querySelector<HTMLInputElement>('input[type="checkbox"]')!.checked).toBe(true);
    });

    it('lists again on the reload token, keeping the choices made on the rows still listed', () => {
      render([doc(11, ExecutiveSummary), doc(12, TechnicalReport)], library('none'));
      check('doc:11');

      hostComponent.reloadToken++;
      fixture.detectChanges();
      expectList().flush([doc(11, ExecutiveSummary), doc(13, InternalBrief)]);
      fixture.detectChanges();

      expect(panel().rows.map(r => r.key)).toEqual(['doc:13', 'doc:11', 'report:1']);
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
    /** Three report documents over two subjects and two suites, with their two runs' reports. */
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
      expect(items.length).toBe(3);
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
      expect(rowKeys()).toEqual(['doc:13', 'doc:12', 'doc:11', 'report:1']);

      setFilter('mc-dc-sort', 'title');
      expect(rowKeys()).toEqual(['doc:13', 'doc:11', 'doc:12', 'report:1']);

      setFilter('mc-dc-sort', 'changed-first');
      expect(rowKeys()).toEqual(['doc:12', 'doc:13', 'doc:11', 'report:1']);
      expect(JSON.parse(localStorage.getItem(DOWNLOAD_CENTER_VIEW_STORAGE_KEY)!)).toEqual({ version: 1, sort: 'changed-first' });

      const second = TestBed.createComponent(PanelHostComponent);
      second.componentInstance.context = library();
      second.detectChanges();
      expectList().flush([doc(11, TechnicalReport), doc(13, ExecutiveSummary), doc(12, InternalBrief, { runChangedSinceGeneration: true })]);
      second.detectChanges();
      const secondEl = second.nativeElement as HTMLElement;
      expect(secondEl.querySelector<HTMLSelectElement>('[id="mc-dc-sort"]')!.value).toBe('changed-first');
      expect(Array.from(secondEl.querySelectorAll('article.dc-card')).map(card => card.getAttribute('data-row-key')))
        .toEqual(['doc:12', 'doc:13', 'doc:11', 'report:1']);
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
      expect(rowKeys().length).toBe(5);
      await pauseTyping();
      expect(rowKeys()).toEqual(['doc:13', 'doc:12', 'report:2']);

      typeInto(input, 'wiki suite');
      await pauseTyping();
      expect(rowKeys()).toEqual(['doc:13', 'doc:12', 'report:2']);

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
      expect(rowKeys().length).toBe(5);

      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      expect(heard).toEqual(['Escape']);
    });

    it('filters by facets: values of one facet OR together, facets AND, and each count reflects the other filters', () => {
      render(twoSuites());

      expect(facetLabels()).toEqual(['Document', 'Subject', 'Suite', 'Written by', 'Created']);
      // Every row is unchanged, so Changes has one value and is not offered.
      expect(byId('mc-dc-facet-changes-trigger')).toBeNull();
      expect(facetOptions('document').map(option => option.label))
        .toEqual(['Executive Summary, 2 documents', 'Report for AI Researchers and Developers, 1 document', 'Run report, 2 documents']);
      expect(facetOptions('writer').map(option => option.label))
        .toEqual(['Claude Opus writer, 2 documents', 'Gemini writer, 1 document', 'No writer — run files, 2 documents']);

      const facetsBefore = panel().facets;
      fixture.detectChanges();
      expect(panel().facets).toBe(facetsBefore);

      pickFacet('document', 'Executive Summary');
      pickFacet('document', 'Run report');
      expect(rowKeys()).toEqual(['doc:13', 'doc:11', 'report:1', 'report:2']);
      expect(text('#mc-dc-facet-document-trigger')).toBe('Document 2 selected');
      expect(panel().facets).not.toBe(facetsBefore);

      pickFacet('suite', 'Wiki Suite');
      expect(rowKeys()).toEqual(['doc:13', 'report:2']);
      expect(facetOptions('document').map(option => option.label))
        .toEqual(['Executive Summary, 1 document', 'Report for AI Researchers and Developers, 1 document', 'Run report, 1 document']);
      expect(facetOptions('suite').map(option => option.label)).toEqual(['Board Suite, 2 documents', 'Wiki Suite, 2 documents']);
      expect(text('#mc-dc-list-status')).toBe('Showing 2 of 2 documents · filtered from 5');
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
      expect(rowKeys()).toEqual(['doc:13', 'doc:12', 'doc:11', 'report:1']);
    });

    it('shows the active filters as removable chips, and Clear all keeps Show selected only', async () => {
      useSearchClock();
      render(twoSuites());
      pickFacet('document', 'Executive Summary');
      pickFacet('document', 'Run report');
      pickFacet('suite', 'Wiki Suite');
      typeInto(byId<HTMLInputElement>('mc-dc-search')!, 'harbor');
      await pauseTyping();

      const chipList = q('ul.dc-filter-chips')!;
      expect(chipList.getAttribute('role')).toBe('list');
      expect(chipList.getAttribute('aria-label')).toBe('Active filters');
      expect(chipNames()).toEqual([
        'Remove filter Document: Executive Summary',
        'Remove filter Document: Run report',
        'Remove filter Suite: Wiki Suite',
        'Remove filter Search: “harbor”'
      ]);

      chips()[0].click();
      fixture.detectChanges();
      expect(chipNames()[0]).toBe('Remove filter Document: Run report');
      expect(document.activeElement).toBe(chips()[0]);
      expect(rowKeys()).toEqual(['report:2']);

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
      expect(rowKeys().length).toBe(5);

      typeInto(byId<HTMLInputElement>('mc-dc-search')!, 'nothing like this');
      await pauseTyping();
      expect(rowKeys()).toEqual([]);
      expect(text('.dc-no-matches')).toContain('No documents match these filters.');
      expect(q('.dc-no-matches button')!.textContent!.trim()).toBe('Clear all filters');
    });

    it('shows ten cards, then more on request, focusing the first new card', () => {
      const many = Array.from({ length: 12 }, (_, i) => doc(100 + i, ExecutiveSummary, { createdAtUtc: `2026-09-${10 + i}T16:00:00Z` }));
      render(many);

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

      const more = Array.from({ length: 24 }, (_, i) => doc(200 + i, ExecutiveSummary, { createdAtUtc: `2026-09-10T${String(i).padStart(2, '0')}:00:00Z` }));
      render(more);
      expect(rowKeys().length).toBe(10);
      const showAll = q<HTMLButtonElement>('.dc-show-all')!;
      expect(showAll.textContent!.trim()).toBe('Show all 25');
      showAll.click();
      fixture.detectChanges();
      expect(rowKeys().length).toBe(25);
      expect(document.activeElement).toBe(byId(`mc-dc-${rowKeys()[10].replace(':', '-')}-title`));
    });

    it('counts the selection the list does not show, selects what matches, and has no select-all checkbox', () => {
      const many = Array.from({ length: 12 }, (_, i) => doc(100 + i, ExecutiveSummary, { createdAtUtc: `2026-09-${10 + i}T16:00:00Z` }));
      render(many, library('none'));
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
      const many = Array.from({ length: 12 }, (_, i) => doc(100 + i, ExecutiveSummary, { createdAtUtc: `2026-09-${10 + i}T16:00:00Z` }));
      render(many);
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
      expect(request.fallbackFileName).toBe('run-1_vs-2-models_gemini-flash_Researcher_Report.pdf');
      request.load('summary', 'anonymized').subscribe();
      expect(pdf).toHaveBeenCalledWith(11, Summary, BenchmarkReportPeerNaming.Anonymized, expect.any(String));

      panel().pdfViewer!.closed.emit();
      expect(document.activeElement).toBe(button);
    });

    it('views a run report row as the run report PDF', () => {
      const service = TestBed.inject(AdminBenchmarkService);
      const pdf = vi.spyOn(service, 'getRunReportPdf').mockReturnValue(of({ bytes: new Uint8Array([1]), fileName: null }));
      render([doc(11, ExecutiveSummary)]);
      const open = vi.spyOn(panel().pdfViewer!, 'open').mockReturnValue(undefined);

      const rid = panel().rowId(panel().rows.find(r => r.key === 'report:1')!);
      byId<HTMLButtonElement>(`${rid}-view`)!.click();

      const request = vi.mocked(open).mock.lastCall![0] as PdfViewerRequest;
      expect(request.title).toBe('Run report, run #1');
      expect(request.variants).toBeUndefined();
      request.load(null).subscribe();
      expect(pdf).toHaveBeenCalledWith(1, expect.any(String));
      // A run report is never deleted here.
      expect(byId(`${rid}-delete`)).toBeNull();
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

      expect(rowKeys()).toEqual(['doc:13', 'doc:11', 'report:1']);
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

  it('names a document compared with peers vs-<N>-models_, after run-<id>_ or first for a group subject', () => {
    expect(reportDocumentFileStem(doc(1, ExecutiveSummary, { peerCount: 4 }), 'x'))
      .toBe('run-1_vs-4-models_executive-summary-gemini-flash');
    expect(reportDocumentFileStem(doc(1, ExecutiveSummary, { peerCount: 4, subjectKey: 'group:3' }), 'x'))
      .toBe('vs-4-models_executive-summary-gemini-flash');
    expect(reportDocumentFileStem(doc(1, ExecutiveSummary, { peerCount: 0 }), 'x'))
      .toBe('run-1_executive-summary-gemini-flash');
    expect(reportDocumentFileStem(doc(2, TechnicalReport, { peerCount: 1 }), 'x'))
      .toBe('run-1_vs-1-models_gemini-flash_Researcher_Report');
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
      expect(rowEl('report:1').querySelector('.dc-option-charts')).toBeNull();
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
    const BATTERY: DownloadCenterContext = { kind: 'battery', batteryRunId: 7, label: 'Core Battery · Gemini Flash' };

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

    /** Renders the battery context and answers its document list and its job request. */
    function openBattery(documents: BenchmarkReportDocumentListItemDto[], view: BenchmarkRunReportJobDto | null = null): TestRequest {
      hostComponent.context = BATTERY;
      fixture.detectChanges();
      const list = expectList();
      list.flush(documents);
      flushJob(view);
      fixture.detectChanges();
      return list;
    }

    it('lists the analysis report and every document about the battery run, by subject, and no member run\'s files', () => {
      const list = openBattery([
        batteryDoc(21, ExecutiveSummary),
        batteryDoc(22, TechnicalReport),
        batteryDoc(23, InternalBrief, { origin: BenchmarkReportDocumentOrigin.ReportPack, peerCount: 2 })
      ]);

      expect(list.request.params.get('subject')).toBe('battery:7');
      expect(list.request.params.has('runId')).toBe(false);
      expect(list.request.params.has('origin')).toBe(false);
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

      // A battery-completion document is not deleted here; a Report Pack document about the battery run is.
      expect(rowEl('doc:21').querySelector('.dc-view-btn')).not.toBeNull();
      expect(rowEl('doc:21').querySelector('.dc-delete-btn')).toBeNull();
      expect(rowEl('doc:23').querySelector('.dc-delete-btn')).not.toBeNull();
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
      expectList().flush({ error: 'Boom' }, { status: 500, statusText: 'Server Error' });
      flushJob(null);
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
      const reload = expectList();
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
  });
});
