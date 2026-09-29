import { ChangeDetectionStrategy, Component, ViewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPeerNaming
} from '../../../services/admin-benchmark.service';
import type { PdfViewerRequest } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { reportDocumentFileStem } from '../download-center/benchmark-download-center.component';
import { REPORT_DISCLOSURE_NOTE, REPORT_DISCLOSURE_NOTE_PEERS } from '../report-disclosure-guide';
import {
  REPORT_LIBRARY_ALL_TAKE,
  ReportDocumentLibraryComponent,
  ReportDocumentLibraryScope
} from './report-document-library.component';

const { ExecutiveSummary, TechnicalReport, InternalBrief } = BenchmarkReportAudience;
const { Summary, Detailed, Full } = BenchmarkReportDisclosure;

const DOCUMENTS_URL = '/api/admin/benchmark/report-documents';
const COMPARISON: ReportDocumentLibraryScope = { kind: 'comparison', entryKeys: ['run:1', 'run:2', 'group:4'] };

function doc(id: number, audience: BenchmarkReportAudience, overrides: Partial<BenchmarkReportDocumentListItemDto> = {}): BenchmarkReportDocumentListItemDto {
  const titles: Record<number, string> = {
    [ExecutiveSummary]: 'Executive Summary: Gemini Flash',
    [TechnicalReport]: 'Technical Report: Gemini Flash',
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
    origin: 1,
    comparisonKey: 'cmp-1',
    comparisonEntryCount: 3,
    peerCount: 2,
    pricingBasis: 'Current',
    peersChangedSinceGeneration: false,
    ...overrides
  };
}

@Component({
  standalone: true,
  imports: [ReportDocumentLibraryComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="shell" [style.width.px]="width">
      <app-report-document-library [scope]="scope" [heading]="heading" [idPrefix]="idPrefix"
                                   [showComparisonColumn]="showComparisonColumn" [reloadToken]="reloadToken"
                                   (documentsChange)="changes.push($event)"></app-report-document-library>
    </div>
  `
})
class LibraryHostComponent {
  @ViewChild(ReportDocumentLibraryComponent, { static: true }) library!: ReportDocumentLibraryComponent;
  width = 1100;
  scope: ReportDocumentLibraryScope = COMPARISON;
  heading = '';
  idPrefix = 'rp';
  showComparisonColumn = false;
  reloadToken = 0;
  readonly changes: (readonly BenchmarkReportDocumentListItemDto[])[] = [];
}

describe('ReportDocumentLibraryComponent', () => {
  let fixture: ComponentFixture<LibraryHostComponent>;
  let hostComponent: LibraryHostComponent;
  let http: HttpTestingController;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LibraryHostComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    fixture = TestBed.createComponent(LibraryHostComponent);
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
  });

  const q = <T extends HTMLElement = HTMLElement>(selector: string): T | null => el.querySelector<T>(selector);
  const text = (selector: string): string => (q(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const rowIds = (): string[] => Array.from(el.querySelectorAll('.rdl-row')).map(row => row.getAttribute('data-document-id')!);
  const library = (): ReportDocumentLibraryComponent => hostComponent.library;

  function expectList(): TestRequest {
    return http.expectOne(r => r.method === 'GET' && r.url === DOCUMENTS_URL);
  }

  /** Renders the host with the given settings and answers the library's first list. */
  function render(documents: BenchmarkReportDocumentListItemDto[], settings: Partial<LibraryHostComponent> = {}): TestRequest {
    Object.assign(hostComponent, settings);
    fixture.detectChanges();
    const request = expectList();
    request.flush(documents);
    fixture.detectChanges();
    return request;
  }

  function setFilter(id: string, value: string): void {
    const control = q<HTMLInputElement | HTMLSelectElement>(`#${id}`)!;
    control.value = value;
    control.dispatchEvent(new Event(control instanceof HTMLSelectElement ? 'change' : 'input'));
    fixture.detectChanges();
  }

  // -------------------------------------------------------------------------------------------
  // Loading
  // -------------------------------------------------------------------------------------------

  it('lists a comparison\'s Report Pack documents by its entry keys', () => {
    const request = render([doc(11, ExecutiveSummary)]);

    expect(request.request.params.get('comparison')).toBe('run:1,run:2,group:4');
    expect(request.request.params.get('origin')).toBe('reportPack');
    expect(request.request.params.has('take')).toBeFalse();
    expect(request.request.params.has('suiteId')).toBeFalse();
    expect(rowIds()).toEqual(['11']);
    expect(hostComponent.changes.length).toBe(1);
    expect(hostComponent.changes[0].map(d => d.id)).toEqual([11]);
  });

  it('lists every Report Pack document, up to the endpoint\'s maximum, in the all scope', () => {
    const request = render([doc(11, ExecutiveSummary)], { scope: { kind: 'all' }, showComparisonColumn: true, idPrefix: 'mcl' });

    expect(request.request.params.get('origin')).toBe('reportPack');
    expect(request.request.params.get('take')).toBe(String(REPORT_LIBRARY_ALL_TAKE));
    expect(REPORT_LIBRARY_ALL_TAKE).toBe(500);
    expect(request.request.params.has('comparison')).toBeFalse();
  });

  it('lists again when the reload token changes, and when the scope\'s entries change, but not for an equal scope', () => {
    render([doc(11, ExecutiveSummary), doc(12, TechnicalReport)]);
    q<HTMLInputElement>('#rp-doc-11-select')!.click();
    fixture.detectChanges();

    hostComponent.scope = { kind: 'comparison', entryKeys: ['run:1', 'run:2', 'group:4'] };
    fixture.detectChanges();
    http.expectNone(r => r.url === DOCUMENTS_URL);

    hostComponent.reloadToken++;
    fixture.detectChanges();
    expectList().flush([doc(11, ExecutiveSummary), doc(12, TechnicalReport)]);
    fixture.detectChanges();
    expect(library().selectedIds.has(11)).toBeTrue();

    hostComponent.scope = { kind: 'comparison', entryKeys: ['run:1', 'run:3'] };
    fixture.detectChanges();
    const request = expectList();
    expect(request.request.params.get('comparison')).toBe('run:1,run:3');
    request.flush([doc(13, ExecutiveSummary)]);
    fixture.detectChanges();
    expect(rowIds()).toEqual(['13']);
    expect(library().selectedIds.size).toBe(0);
  });

  it('shows its heading only when it has one', () => {
    render([doc(11, ExecutiveSummary)]);
    expect(q('.rdl-heading')).toBeNull();

    fixture.destroy();
    fixture = TestBed.createComponent(LibraryHostComponent);
    hostComponent = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    render([doc(11, ExecutiveSummary)], { scope: { kind: 'all' }, heading: 'Comparison reports', idPrefix: 'mcl' });
    const heading = q('h4.gh-section-title.rdl-heading')!;
    expect(heading.textContent!.trim()).toBe('Comparison reports');
    expect(heading.id).toBe('mcl-heading');
    expect(text('caption')).toBe('Comparison reports');
  });

  // -------------------------------------------------------------------------------------------
  // Columns
  // -------------------------------------------------------------------------------------------

  it('shows the comparison scope\'s columns, without Compared with', () => {
    render([doc(11, ExecutiveSummary)]);

    const headers = Array.from(el.querySelectorAll('table.gh-datatable thead tr:first-child th')).map(th => (th.textContent ?? '').replace(/\s+/g, ' ').trim());
    expect(headers).toEqual(['Select', 'Created (UTC)', 'Subject', 'Document', 'Writer', 'Status', 'Cost', 'Actions']);
    expect(q('.rdl-col-compared')).toBeNull();
    expect(q('#rp-f-suite')).toBeNull();
  });

  it('shows Compared with in the all scope: the peer count and the suite, with a suite filter of the suites present', () => {
    render([
      doc(11, ExecutiveSummary, { peerCount: 4 }),
      doc(12, TechnicalReport, { peerCount: 1, suiteName: 'Wiki Suite' }),
      doc(13, InternalBrief, { peerCount: 0 })
    ], { scope: { kind: 'all' }, showComparisonColumn: true, idPrefix: 'mcl' });

    const headers = Array.from(el.querySelectorAll('table.gh-datatable thead tr:first-child th')).map(th => (th.textContent ?? '').replace(/\s+/g, ' ').trim());
    expect(headers).toEqual(['Select', 'Created (UTC)', 'Subject', 'Document', 'Compared with', 'Writer', 'Status', 'Cost', 'Actions']);
    expect(text('.rdl-row[data-document-id="11"] .rdl-peers')).toBe('4 other models');
    expect(text('.rdl-row[data-document-id="11"] .rdl-suite')).toBe('Board Suite');
    expect(text('.rdl-row[data-document-id="12"] .rdl-peers')).toBe('1 other model');
    expect(text('.rdl-row[data-document-id="13"] .rdl-peers')).toBe('No other models');

    const suites = Array.from(q<HTMLSelectElement>('#mcl-f-suite')!.options).map(option => option.textContent!.trim());
    expect(suites).toEqual(['All suites', 'Board Suite', 'Wiki Suite']);
    setFilter('mcl-f-suite', 'Wiki Suite');
    expect(rowIds()).toEqual(['12']);
  });

  it('labels the status as the AI Reports tab does, and flags a changed run and a changed comparison in words', () => {
    render([
      doc(11, ExecutiveSummary, { runChangedSinceGeneration: true }),
      doc(12, TechnicalReport, { status: 'CompletedWithWarnings', peersChangedSinceGeneration: true })
    ]);

    expect(text('.rdl-row[data-document-id="11"] .rdl-status-tag')).toBe('Written');
    expect(text('.rdl-row[data-document-id="12"] .rdl-status-tag')).toBe('Written with warnings');
    expect(q('.rdl-row[data-document-id="12"] .rdl-status-tag')!.classList).toContain('is-warning');

    const run = q('.rdl-row[data-document-id="11"] .rdl-tag-run-changed')!;
    expect(run.classList).toContain('gh-tag');
    expect(run.textContent!.replace(/\s+/g, ' ').trim()).toBe('Run changed since this document was written');
    expect(q('.rdl-row[data-document-id="11"] .rdl-tag-comparison-changed')).toBeNull();
    const comparison = q('.rdl-row[data-document-id="12"] .rdl-tag-comparison-changed')!;
    expect(comparison.classList).toContain('gh-tag');
    expect(comparison.textContent).toContain('Comparison changed');
  });

  it('sorts by the sortable columns, newest first by default', () => {
    render([
      doc(11, TechnicalReport, { costUsd: 0.2, writerDisplayName: 'B writer' }),
      doc(13, ExecutiveSummary, { costUsd: 0.01, writerDisplayName: 'A writer' }),
      doc(12, InternalBrief, { costUsd: 0.1, writerDisplayName: 'C writer' })
    ]);

    const sortable = Array.from(el.querySelectorAll<HTMLElement>('thead th.gh-th-sortable'))
      .map(th => th.querySelector('.gh-th-label')!.textContent!.trim());
    expect(sortable).toEqual(['Created (UTC)', 'Subject', 'Document', 'Writer', 'Status', 'Cost']);
    expect(rowIds()).toEqual(['13', '12', '11']);
    expect(q('thead th[aria-sort]')!.textContent).toContain('Created (UTC)');

    const sortBy = (label: string): void => {
      Array.from(el.querySelectorAll<HTMLButtonElement>('thead .gh-th-sort'))
        .find(button => button.textContent!.includes(label))!.click();
      fixture.detectChanges();
    };
    sortBy('Cost');
    expect(rowIds()).toEqual(['11', '12', '13']);
    sortBy('Writer');
    expect(rowIds()).toEqual(['12', '11', '13']);
  });

  it('moves Writer and Cost into the Document cell below 44rem of its own width', () => {
    render([doc(11, ExecutiveSummary)]);
    const writerCell = q('.rdl-row .rdl-col-writer')!;
    const meta = q('.rdl-row .rdl-doc-meta')!;
    expect(getComputedStyle(writerCell).display).not.toBe('none');
    expect(getComputedStyle(meta).display).toBe('none');
    expect(getComputedStyle(q('app-report-document-library')!).containerType).toBe('inline-size');

    hostComponent.width = 600;
    fixture.detectChanges();
    expect(getComputedStyle(writerCell).display).toBe('none');
    expect(getComputedStyle(q('thead .rdl-col-cost')!).display).toBe('none');
    expect(getComputedStyle(meta).display).toBe('block');
    expect(meta.textContent!.trim()).toBe('Claude Opus writer · $0.05');
  });

  it('puts one pager above and one below the scrolling table, the second silent', () => {
    render([doc(11, ExecutiveSummary)]);

    const pagers = Array.from(el.querySelectorAll('app-table-pager'));
    const scroll = q('.gh-datatable-scroll')!;
    expect(pagers.length).toBe(2);
    expect(pagers.every(pager => !scroll.contains(pager))).toBeTrue();
    expect(pagers[0].compareDocumentPosition(scroll) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(scroll.compareDocumentPosition(pagers[1]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(pagers[1].querySelector('[role="status"]')).toBeNull();
    expect(scroll.querySelector('table.gh-datatable')).not.toBeNull();
  });

  // -------------------------------------------------------------------------------------------
  // Ids
  // -------------------------------------------------------------------------------------------

  it('derives every id, tooltip and anchor from idPrefix', () => {
    render([doc(5, ExecutiveSummary)], { scope: { kind: 'all' }, showComparisonColumn: true, idPrefix: 'mcl' });

    for (const action of ['view', 'download', 'delete']) {
      const button = q(`#mcl-doc-5-${action}`)!;
      expect(button).withContext(action).not.toBeNull();
      expect(button.getAttribute('interestfor')).toBe(`mcl-tip-${action}-5`);
      expect(button.getAttribute('style')).toBe(`anchor-name: --mcl-tip-${action}-5`);
      expect(button.hasAttribute('title')).toBeFalse();
      const tip = q(`#mcl-tip-${action}-5`)!;
      expect(tip.getAttribute('popover')).toBe('hint');
      expect(tip.getAttribute('style')).toBe(`position-anchor: --mcl-tip-${action}-5`);
    }
    expect(q('#mcl-doc-5-select')).not.toBeNull();
    expect(q('#mcl-download')).not.toBeNull();
    expect(q('#mcl-f-subject')).not.toBeNull();
    expect(q('#mcl-f-document')).not.toBeNull();
    expect(q('dialog.rdl-delete-dialog')!.getAttribute('aria-labelledby')).toBe('mcl-delete-title');
    expect(el.querySelectorAll('[id^="rp-"]').length).toBe(0);
  });

  it('names each row action for its document', () => {
    render([doc(11, ExecutiveSummary)]);

    expect(q('#rp-doc-11-view')!.getAttribute('aria-label')).toBe('View Executive Summary: Gemini Flash, 2026-09-21 16:00 UTC');
    expect(q('#rp-doc-11-download')!.getAttribute('aria-label')).toBe('Download Executive Summary: Gemini Flash, 2026-09-21 16:00 UTC');
    expect(q('#rp-doc-11-delete')!.getAttribute('aria-label')).toBe('Delete Executive Summary: Gemini Flash, 2026-09-21 16:00 UTC');
    expect(q('#rp-doc-11-delete')!.classList).toContain('action-btn-danger');
    expect(q('#rp-doc-11-select')!.getAttribute('aria-label')).toBe('Select Executive Summary: Gemini Flash, 2026-09-21 16:00 UTC');
  });

  // -------------------------------------------------------------------------------------------
  // Empty states
  // -------------------------------------------------------------------------------------------

  it('says when a comparison has no reports, and when there are no comparison reports at all', () => {
    render([]);
    expect(text('.rdl-empty')).toBe('No reports have been written for this comparison yet.');
    expect(q('table.gh-datatable')).toBeNull();

    fixture.destroy();
    fixture = TestBed.createComponent(LibraryHostComponent);
    hostComponent = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    render([], { scope: { kind: 'all' }, showComparisonColumn: true, idPrefix: 'mcl' });
    expect(text('.rdl-empty')).toBe('No comparison reports yet.');
  });

  it('says when the filters hide every document, and Clear filters shows them again', () => {
    render([doc(11, ExecutiveSummary), doc(12, TechnicalReport)]);

    setFilter('rp-f-subject', 'nothing like this');
    expect(rowIds()).toEqual([]);
    expect(text('.rdl-no-matches')).toContain('No documents match these filters.');
    expect(q('.rdl-empty')).toBeNull();

    Array.from(el.querySelectorAll<HTMLButtonElement>('.rdl-no-matches .gh-filter-clear'))[0].click();
    fixture.detectChanges();
    expect(rowIds().sort()).toEqual(['11', '12']);
    expect(q('.rdl-no-matches')).toBeNull();
  });

  it('filters by document type with the types present only', () => {
    render([doc(11, ExecutiveSummary), doc(12, TechnicalReport), doc(14, ExecutiveSummary)]);

    const options = Array.from(q<HTMLSelectElement>('#rp-f-document')!.options).map(option => option.textContent!.trim());
    expect(options).toEqual(['All documents', 'Executive Summary', 'Report for AI Researchers and Developers']);
    setFilter('rp-f-document', 'Executive Summary');
    expect(rowIds()).toEqual(['14', '11']);
  });

  // -------------------------------------------------------------------------------------------
  // Selection and Download
  // -------------------------------------------------------------------------------------------

  it('opens the Download Center on every document shown when nothing is selected, and says so', () => {
    render([doc(11, ExecutiveSummary), doc(12, TechnicalReport), doc(14, ExecutiveSummary)]);
    const open = spyOn(library().downloadCenter!, 'open');
    const toolbar = q<HTMLButtonElement>('#rp-download')!;

    expect(toolbar.classList).toContain('btn-ghost');
    expect(toolbar.textContent!.trim()).toBe('Download…');
    expect(toolbar.getAttribute('aria-label')).toBe('Download all 3 documents shown');
    expect(toolbar.hasAttribute('aria-disabled')).toBeFalse();

    setFilter('rp-f-document', 'Executive Summary');
    expect(toolbar.getAttribute('aria-label')).toBe('Download all 2 documents shown');
    toolbar.click();
    expect(open).toHaveBeenCalledOnceWith({
      kind: 'documents',
      documentIds: [14, 11],
      title: 'Comparison reports',
      subtitle: '2 documents of the comparison of 3 models'
    });

    // Nothing shown: Download… is aria-disabled and does nothing.
    setFilter('rp-f-subject', 'nothing like this');
    expect(toolbar.getAttribute('aria-disabled')).toBe('true');
    toolbar.click();
    expect(open).toHaveBeenCalledTimes(1);
  });

  it('opens the Download Center on the selected documents, off-page ones included, and counts them', () => {
    render([doc(11, ExecutiveSummary), doc(12, TechnicalReport)], { scope: { kind: 'all' }, showComparisonColumn: true });
    const open = spyOn(library().downloadCenter!, 'open');

    q<HTMLInputElement>('#rp-doc-11-select')!.click();
    q<HTMLInputElement>('#rp-doc-12-select')!.click();
    fixture.detectChanges();
    expect(text('.rdl-selection-count')).toBe('2 selected');
    expect(q('#rp-download')!.getAttribute('aria-label')).toBe('Download 2 selected documents');

    // A filter that hides one: the line says so, and the selection still downloads whole.
    setFilter('rp-f-document', 'Executive Summary');
    expect(text('.rdl-selection-count')).toBe('2 selected — 1 not on this page');
    q<HTMLButtonElement>('#rp-download')!.click();
    expect(open).toHaveBeenCalledOnceWith({
      kind: 'documents',
      documentIds: [11, 12],
      title: 'Comparison reports',
      subtitle: '2 comparison report documents'
    });

    const showSelected = q<HTMLButtonElement>('.rdl-show-selected')!;
    expect(showSelected.getAttribute('aria-pressed')).toBe('false');
    q<HTMLButtonElement>('.rdl-clear-selection')!.click();
    fixture.detectChanges();
    expect(q('.rdl-selection-count')).toBeNull();
    // No header select-all.
    expect(q('thead input[type="checkbox"]')).toBeNull();
  });

  it('shows only the selected documents on request', () => {
    render([doc(11, ExecutiveSummary), doc(12, TechnicalReport)]);

    q<HTMLInputElement>('#rp-doc-12-select')!.click();
    fixture.detectChanges();
    q<HTMLButtonElement>('.rdl-show-selected')!.click();
    fixture.detectChanges();
    expect(rowIds()).toEqual(['12']);
    expect(q('.rdl-show-selected')!.getAttribute('aria-pressed')).toBe('true');
  });

  it('opens the Download Center on one row\'s document, and returns focus to its button when it closes', () => {
    render([doc(11, ExecutiveSummary), doc(12, TechnicalReport)]);
    const open = spyOn(library().downloadCenter!, 'open');

    const button = q<HTMLButtonElement>('#rp-doc-12-download')!;
    button.click();
    expect(open).toHaveBeenCalledOnceWith(jasmine.objectContaining({ kind: 'documents', documentIds: [12] }));

    library().downloadCenter!.closed.emit();
    expect(document.activeElement).toBe(button);
  });

  // -------------------------------------------------------------------------------------------
  // View
  // -------------------------------------------------------------------------------------------

  it('views a document with peers at its highest disclosure, peers named, with the peer-names row', () => {
    const service = TestBed.inject(AdminBenchmarkService);
    const pdf = spyOn(service, 'getReportDocumentPdf').and.returnValue(of({ bytes: new Uint8Array([1]), fileName: null }));
    render([doc(11, TechnicalReport, { allowedDisclosures: [Detailed, Summary], peerCount: 2 })]);
    const open = spyOn(library().pdfViewer!, 'open');

    const button = q<HTMLButtonElement>('#rp-doc-11-view')!;
    button.click();

    const request = open.calls.mostRecent().args[0] as PdfViewerRequest;
    expect(request.title).toBe('Technical Report: Gemini Flash');
    expect(request.subtitle).toBe('Gemini Flash · compared with 2 models · by Claude Opus writer on 2026-09-21 16:00 UTC');
    expect(request.variants).toEqual([{ key: 'summary', label: 'Summary' }, { key: 'detailed', label: 'Detailed' }]);
    expect(request.initialVariant).toBe('detailed');
    expect(request.variantsInfo!.note).toBe(REPORT_DISCLOSURE_NOTE_PEERS);
    expect(request.variantsInfo!.note).toContain('Switch Peer names to see the copy a provider would receive.');
    expect(request.secondaryVariants).toEqual({
      label: 'Peer names',
      options: [{ key: 'named', label: 'Named' }, { key: 'anonymized', label: 'Anonymized' }],
      initial: 'named'
    });
    const shown = doc(11, TechnicalReport, { allowedDisclosures: [Detailed, Summary], peerCount: 2 });
    expect(request.fallbackFileName).toBe(`${reportDocumentFileStem(shown, 'Report for AI Researchers and Developers')}.pdf`);
    expect(request.fallbackFileName).toMatch(/^run-1_.*_Researcher_Report\.pdf$/);

    request.load('summary', 'anonymized').subscribe();
    expect(pdf).toHaveBeenCalledWith(11, Summary, BenchmarkReportPeerNaming.Anonymized, jasmine.any(String));
    request.load('detailed', 'named').subscribe();
    expect(pdf).toHaveBeenCalledWith(11, Detailed, BenchmarkReportPeerNaming.Named, jasmine.any(String));
    const url = request.tabUrl!('detailed', 'anonymized');
    expect(url).toContain('/api/admin/benchmark/report-documents/11/render/pdf?');
    expect(url).toContain('disclosure=detailed');
    expect(url).toContain('peers=anonymized');
    expect(url).toContain('inline=true');

    // Focus returns to View when the viewer closes.
    library().pdfViewer!.closed.emit();
    expect(document.activeElement).toBe(button);
  });

  it('views a document without peers with no peer-names row, named, and the plain note', () => {
    const service = TestBed.inject(AdminBenchmarkService);
    const pdf = spyOn(service, 'getReportDocumentPdf').and.returnValue(of({ bytes: new Uint8Array([1]), fileName: null }));
    render([doc(13, InternalBrief, { peerCount: 0 })]);
    const open = spyOn(library().pdfViewer!, 'open');

    q<HTMLButtonElement>('#rp-doc-13-view')!.click();

    const request = open.calls.mostRecent().args[0] as PdfViewerRequest;
    expect(request.secondaryVariants).toBeUndefined();
    expect(request.variants!.map(v => v.key)).toEqual(['full']);
    expect(request.initialVariant).toBe('full');
    expect(request.variantsInfo!.note).toBe(REPORT_DISCLOSURE_NOTE);
    expect(request.subtitle).toBe('Gemini Flash · by Claude Opus writer on 2026-09-23 16:00 UTC');
    request.load('full').subscribe();
    expect(pdf).toHaveBeenCalledWith(13, Full, BenchmarkReportPeerNaming.Named, jasmine.any(String));
  });

  // -------------------------------------------------------------------------------------------
  // Delete
  // -------------------------------------------------------------------------------------------

  it('deletes a document only after the confirmation, and says so', async () => {
    render([doc(11, ExecutiveSummary), doc(12, TechnicalReport)]);
    const confirm = q<HTMLDialogElement>('dialog.rdl-delete-dialog')!;

    q<HTMLButtonElement>('#rp-doc-11-delete')!.click();
    fixture.detectChanges();
    expect(confirm.open).toBeTrue();
    expect(text('.rdl-delete-text')).toContain('Executive Summary: Gemini Flash');

    const canceled = new Promise<void>(resolve => confirm.addEventListener('close', () => resolve(), { once: true }));
    q<HTMLButtonElement>('.rdl-delete-cancel')!.click();
    await canceled;
    fixture.detectChanges();
    expect(confirm.open).toBeFalse();
    expect(document.activeElement).toBe(q('#rp-doc-11-delete'));
    http.expectNone(r => r.method === 'DELETE');

    q<HTMLButtonElement>('#rp-doc-11-delete')!.click();
    fixture.detectChanges();
    const closed = new Promise<void>(resolve => confirm.addEventListener('close', () => resolve(), { once: true }));
    q<HTMLButtonElement>('.rdl-delete-confirm')!.click();
    const request = http.expectOne(r => r.method === 'DELETE');
    expect(request.request.url).toBe(`${DOCUMENTS_URL}/11`);
    request.flush(null);
    await closed;
    fixture.detectChanges();

    expect(confirm.open).toBeFalse();
    expect(rowIds()).toEqual(['12']);
    expect(q('.rdl-status')!.getAttribute('role')).toBe('status');
    expect(text('.rdl-status')).toBe('Deleted Executive Summary: Gemini Flash, 2026-09-21 16:00 UTC.');
    expect(document.activeElement).toBe(q('#rp-download'));
    expect(hostComponent.changes[hostComponent.changes.length - 1].map(d => d.id)).toEqual([12]);
  });

  it('shows the server\'s reason when a delete fails, and keeps the document', () => {
    render([doc(11, ExecutiveSummary)]);

    q<HTMLButtonElement>('#rp-doc-11-delete')!.click();
    fixture.detectChanges();
    q<HTMLButtonElement>('.rdl-delete-confirm')!.click();
    http.expectOne(r => r.method === 'DELETE').flush({ error: 'The document is being rendered.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(q<HTMLDialogElement>('dialog.rdl-delete-dialog')!.open).toBeTrue();
    expect(text('.rdl-delete-dialog .gh-field-error')).toBe('The document is being rendered.');
    expect(rowIds()).toEqual(['11']);
  });

  // -------------------------------------------------------------------------------------------
  // Nesting
  // -------------------------------------------------------------------------------------------

  it('stops the close and cancel events of its nested dialogs, and works outside any dialog', () => {
    render([doc(11, ExecutiveSummary)]);
    expect(el.closest('dialog')).toBeNull();

    const heard: string[] = [];
    el.addEventListener('close', () => heard.push('close'));
    el.addEventListener('cancel', () => heard.push('cancel'));
    for (const selector of ['dialog.rdl-delete-dialog', 'dialog.benchmark-download-center-dialog', 'dialog.pdfv']) {
      const dialog = q<HTMLDialogElement>(selector)!;
      expect(dialog).withContext(selector).not.toBeNull();
      dialog.dispatchEvent(new Event('cancel', { bubbles: true, cancelable: true }));
      dialog.dispatchEvent(new Event('close', { bubbles: true }));
    }
    expect(heard).toEqual([]);
  });
});
