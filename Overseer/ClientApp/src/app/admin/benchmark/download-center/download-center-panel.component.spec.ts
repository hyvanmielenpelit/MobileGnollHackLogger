import { ChangeDetectionStrategy, Component, ViewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
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
  BenchmarkReportPeerNaming
} from '../../../services/admin-benchmark.service';
import type { PdfViewerRequest } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { REPORT_LIBRARY_ALL_TAKE } from '../report-pack/report-document-format';
import { REPORT_CHART_STORAGE_KEY, ReportChartPublishResult, ReportChartSelection } from '../report-pack/report-charts';
import { ReportChartPickerComponent } from '../report-pack/report-chart-picker.component';
import {
  CHART_SKIP_REASONS,
  DOWNLOAD_CENTER_STORAGE_KEY,
  DownloadCenterChartActions,
  DownloadCenterContext,
  DownloadCenterPanelComponent,
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

type FakeActions = DownloadCenterChartActions & { publish: jasmine.Spy };

function chartActions(overrides: Partial<DownloadCenterChartActions> = {}): FakeActions {
  return {
    currentSettingsHash: HASH,
    pricingBasis: 'Current',
    available: ['p1a-quality', 'p1b-speed', 'p1c-cost', 'p2-profile', 's1-quality-speed', 's2-quality-cost', 's3-speed-cost'],
    advisory: null,
    storageMissing: false,
    comparisonKeyMatches: (d: BenchmarkReportDocumentListItemDto) => d.comparisonKey === 'cmp-1',
    publish: jasmine.createSpy('publish').and.resolveTo(publishResult()),
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
    fixture.destroy();
    localStorage.removeItem(DOWNLOAD_CENTER_STORAGE_KEY);
    localStorage.removeItem(REPORT_CHART_STORAGE_KEY);
  });

  const panel = (): DownloadCenterPanelComponent => hostComponent.panel;
  const q = <T extends HTMLElement = HTMLElement>(selector: string): T | null => el.querySelector<T>(selector);
  const text = (selector: string): string => (q(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const rowKeys = (): string[] => Array.from(el.querySelectorAll('tr.dc-row')).map(row => row.getAttribute('data-row-key')!);
  const rowEl = (key: string): HTMLElement => q(`tr.dc-row[data-row-key="${key}"]`)!;
  const byId = <T extends HTMLElement = HTMLElement>(id: string): T | null => el.querySelector<T>(`[id="${id}"]`);

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
      expect(request.request.params.has('take')).toBeFalse();
      http.expectNone(r => /report-documents\/\d+$/.test(r.url));
      expect(panel().rows.map(r => r.key)).toEqual(['doc:12', 'doc:11', 'report:1', 'report:2']);
    });

    it('lists every comparison document, up to the endpoint\'s maximum, in the all scope', () => {
      const request = render([doc(11, ExecutiveSummary)], library('none', 'all'));

      expect(request.request.params.get('take')).toBe(String(REPORT_LIBRARY_ALL_TAKE));
      expect(request.request.params.has('comparison')).toBeFalse();
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
      expect(rowEl('doc:11').querySelector<HTMLInputElement>('input[type="checkbox"]')!.checked).toBeTrue();
    });

    it('lists again on the reload token, keeping the choices made on the rows still listed', () => {
      render([doc(11, ExecutiveSummary), doc(12, TechnicalReport)], library('none'));
      check('doc:11');

      hostComponent.reloadToken++;
      fixture.detectChanges();
      expectList().flush([doc(11, ExecutiveSummary), doc(13, InternalBrief)]);
      fixture.detectChanges();

      expect(panel().rows.map(r => r.key)).toEqual(['doc:13', 'doc:11', 'report:1']);
      expect(panel().isIncluded(panel().rows.find(r => r.key === 'doc:11')!)).toBeTrue();
      expect(panel().isIncluded(panel().rows.find(r => r.key === 'doc:13')!)).toBeFalse();
    });

    it('says when a comparison has no reports', () => {
      render([]);

      expect(text('.dc-empty')).toBe('No reports have been written for this comparison yet.');
      expect(q('table.dc-table')).toBeNull();
    });
  });

  // -------------------------------------------------------------------------------------------
  // The table
  // -------------------------------------------------------------------------------------------

  describe('the table', () => {
    it('sorts by Created, newest first, and by Document', () => {
      render([doc(11, TechnicalReport), doc(13, ExecutiveSummary), doc(12, InternalBrief)]);

      const sortable = Array.from(el.querySelectorAll<HTMLElement>('thead th.gh-th-sortable'))
        .map(th => th.querySelector('.gh-th-label')!.textContent!.trim());
      expect(sortable).toEqual(['Created (UTC)', 'Document']);
      expect(rowKeys()).toEqual(['doc:13', 'doc:12', 'doc:11', 'report:1']);
      expect(q('thead th[aria-sort]')!.textContent).toContain('Created (UTC)');

      Array.from(el.querySelectorAll<HTMLButtonElement>('thead .gh-th-sort'))
        .find(button => button.textContent!.includes('Document'))!.click();
      fixture.detectChanges();
      // Descending by name: Run report, Internal…, Gemini Flash —…, Executive….
      expect(rowKeys()).toEqual(['report:1', 'doc:12', 'doc:11', 'doc:13']);
    });

    it('filters by subject, document and suite, each with a visually hidden label, and clears them', () => {
      render([
        doc(11, ExecutiveSummary),
        doc(12, TechnicalReport, { subjectLabel: 'Claude Harbor', subjectKey: 'run:2', subjectRunIds: [2], suiteName: 'Wiki Suite' })
      ]);

      for (const name of ['subject', 'document', 'suite']) {
        const label = q(`label[for="mc-dc-f-${name}"]`)!;
        expect(label).withContext(name).not.toBeNull();
        expect(label.classList).toContain('visually-hidden');
      }
      const documents = Array.from(byId<HTMLSelectElement>('mc-dc-f-document')!.options).map(o => o.textContent!.trim());
      expect(documents).toEqual(['All documents', 'Executive Summary', 'Report for AI Researchers and Developers', 'Run report']);

      setFilter('mc-dc-f-suite', 'Wiki Suite');
      expect(rowKeys()).toEqual(['doc:12', 'report:2']);
      setFilter('mc-dc-f-document', 'Run report');
      expect(rowKeys()).toEqual(['report:2']);

      q<HTMLButtonElement>('.dc-clear-filters')!.click();
      fixture.detectChanges();
      expect(rowKeys().length).toBe(4);

      setFilter('mc-dc-f-subject', 'nothing like this');
      expect(rowKeys()).toEqual([]);
      expect(text('.dc-no-matches')).toContain('No documents match these filters.');
    });

    it('pages at 10 rows with a pager above and below the scrolling table, the second silent', () => {
      const many = Array.from({ length: 12 }, (_, i) => doc(100 + i, ExecutiveSummary, { createdAtUtc: `2026-09-${10 + i}T16:00:00Z` }));
      render(many);

      expect(rowKeys().length).toBe(10);
      const pagers = Array.from(el.querySelectorAll('app-table-pager'));
      const scroll = q('.gh-datatable-scroll')!;
      expect(pagers.length).toBe(2);
      expect(pagers.every(pager => !scroll.contains(pager))).toBeTrue();
      expect(pagers[1].querySelector('[role="status"]')).toBeNull();
    });

    it('says what is selected off the page, shows only the selection, selects what is shown, and has no select-all', () => {
      const many = Array.from({ length: 12 }, (_, i) => doc(100 + i, ExecutiveSummary, { createdAtUtc: `2026-09-${10 + i}T16:00:00Z` }));
      render(many, library('none'));
      expect(q('thead input[type="checkbox"]')).toBeNull();

      const shown = q<HTMLButtonElement>('.dc-select-shown')!;
      expect(shown.textContent!.trim()).toBe('Select the 13 shown');
      setFilter('mc-dc-f-document', 'Executive Summary');
      expect(q('.dc-select-shown')!.textContent!.trim()).toBe('Select the 12 shown');
      q<HTMLButtonElement>('.dc-select-shown')!.click();
      fixture.detectChanges();
      expect(text('.dc-selection-count')).toBe('12 selected — 2 not on this page');

      q<HTMLButtonElement>('.dc-show-selected')!.click();
      fixture.detectChanges();
      expect(q('.dc-show-selected')!.getAttribute('aria-pressed')).toBe('true');

      q<HTMLButtonElement>('.dc-clear-selection')!.click();
      fixture.detectChanges();
      expect(panel().selectedCount).toBe(0);
      expect(panel().showSelectedOnly).toBeFalse();
    });

    it('turns rows into cards below 64rem of its own width, and stacks the package column below 48rem', () => {
      render([doc(11, ExecutiveSummary)]);
      expect(getComputedStyle(rowEl('doc:11')).display).toBe('table-row');
      expect(getComputedStyle(q('.dc-layout')!).gridTemplateColumns.split(' ').length).toBe(2);

      hostComponent.width = 700;
      fixture.detectChanges();
      expect(getComputedStyle(rowEl('doc:11')).display).toBe('grid');
      expect(getComputedStyle(q('.dc-layout')!).gridTemplateColumns.split(' ').length).toBe(1);
    });
  });

  // -------------------------------------------------------------------------------------------
  // View and Delete
  // -------------------------------------------------------------------------------------------

  describe('View and Delete', () => {
    it('views a pack document at its highest disclosure, peers named, with the Peer names row', () => {
      const service = TestBed.inject(AdminBenchmarkService);
      const pdf = spyOn(service, 'getReportDocumentPdf').and.returnValue(of({ bytes: new Uint8Array([1]), fileName: null }));
      render([doc(11, TechnicalReport, { allowedDisclosures: [Detailed, Summary] })]);
      const open = spyOn(panel().pdfViewer!, 'open');

      const rid = panel().rowId(panel().rows[0]);
      const button = byId<HTMLButtonElement>(`${rid}-view`)!;
      expect(button.getAttribute('aria-label')).toBe('View Gemini Flash — Report for AI Researchers and Developers, 2026-09-21 16:00 UTC');
      expect(button.getAttribute('interestfor')).toBe(`${rid}-view-tip`);
      expect(button.hasAttribute('title')).toBeFalse();
      button.click();

      const request = open.calls.mostRecent().args[0] as PdfViewerRequest;
      expect(request.variants).toEqual([{ key: 'summary', label: 'Summary' }, { key: 'detailed', label: 'Detailed' }]);
      expect(request.initialVariant).toBe('detailed');
      expect(request.secondaryVariants?.label).toBe('Peer names');
      expect(request.secondaryVariants?.initial).toBe('named');
      expect(request.fallbackFileName).toBe('run-1_vs-2-models_gemini-flash_Researcher_Report.pdf');
      request.load('summary', 'anonymized').subscribe();
      expect(pdf).toHaveBeenCalledWith(11, Summary, BenchmarkReportPeerNaming.Anonymized, jasmine.any(String));

      panel().pdfViewer!.closed.emit();
      expect(document.activeElement).toBe(button);
    });

    it('views a run report row as the run report PDF', () => {
      const service = TestBed.inject(AdminBenchmarkService);
      const pdf = spyOn(service, 'getRunReportPdf').and.returnValue(of({ bytes: new Uint8Array([1]), fileName: null }));
      render([doc(11, ExecutiveSummary)]);
      const open = spyOn(panel().pdfViewer!, 'open');

      const rid = panel().rowId(panel().rows.find(r => r.key === 'report:1')!);
      byId<HTMLButtonElement>(`${rid}-view`)!.click();

      const request = open.calls.mostRecent().args[0] as PdfViewerRequest;
      expect(request.title).toBe('Run report, run #1');
      expect(request.variants).toBeUndefined();
      request.load(null).subscribe();
      expect(pdf).toHaveBeenCalledWith(1, jasmine.any(String));
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
      expect(confirm.open).toBeTrue();
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

      expect(q<HTMLDialogElement>('dialog.dc-delete-dialog')!.open).toBeTrue();
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
        expect(dialog).withContext(selector).not.toBeNull();
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
    it('shows no chart column or action without chart actions', () => {
      render([doc(11, ExecutiveSummary)]);

      expect(q('.dc-col-charts')).toBeNull();
      expect(q('.dc-update-charts')).toBeNull();
      expect(q('.dc-more-btn')).toBeNull();
      expect(q('dialog.dc-charts-dialog')).toBeNull();
    });

    it('says None, current or differs from step 2 in the Charts column', () => {
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
    });

    it('keeps Update charts… aria-disabled with its reason until documents are chosen', () => {
      render([doc(11, ExecutiveSummary)], library('none'), chartActions());
      const button = q<HTMLButtonElement>('.dc-update-charts')!;

      expect(button.classList).toContain('btn-ghost');
      expect(button.getAttribute('aria-disabled')).toBe('true');
      expect(byId(button.getAttribute('aria-describedby')!)!.textContent!.trim()).toBe('Choose one or more report documents first.');
      button.click();
      expect(q<HTMLDialogElement>('dialog.dc-charts-dialog')!.open).toBeFalse();

      check('doc:11');
      expect(button.hasAttribute('aria-disabled')).toBeFalse();
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

      expect(q<HTMLDialogElement>('dialog.dc-charts-dialog')!.open).toBeTrue();
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

      expect(dialog.open).toBeFalse();
      expect(actions.publish).not.toHaveBeenCalled();
    });

    it('publishes the chosen documents with the draft selection, skipping a document on other prices with its reason', async () => {
      const actions = chartActions();
      let finish!: (result: ReportChartPublishResult) => void;
      actions.publish.and.callFake((_targets: unknown, _selection: unknown, onProgress?: (p: { done: number; total: number; step: string; documentId: number | null }) => void) => {
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
      const [targets, selection] = actions.publish.calls.mostRecent().args;
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
      actions.publish.and.resolveTo(publishResult({ failed: [{ documentId: 11, message: 'the server answered 500' }] }));
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
      actions.publish.and.resolveTo(publishResult({ storageNotConfigured: 'Chart storage is not configured: set Benchmark:ReportChartsPath.' }));
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
});
