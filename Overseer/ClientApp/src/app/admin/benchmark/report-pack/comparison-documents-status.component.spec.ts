import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import {
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPackPreviewDto,
  BenchmarkReportPackWrittenDocumentDto,
  BenchmarkReportScope
} from '../../../services/admin-benchmark.service';
import type { PdfViewerRequest } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import {
  ComparisonDocumentsStatusComponent,
  ComparisonDocumentsViewInput,
  chartsPhrase,
  comparisonDocumentsView,
  writerPhrase
} from './comparison-documents-status.component';

const { ExecutiveSummary, TechnicalReport, InternalBrief } = BenchmarkReportAudience;

const LABELS: Record<string, string> = {
  'run:1': 'GPT-6.1 Sol (medium)',
  'run:2': 'GPT-5.6 Luna (max)',
  'run:3': 'Claude 5 Opus (high)'
};

function written(audience: BenchmarkReportAudience, documentId: number, overrides: Partial<BenchmarkReportPackWrittenDocumentDto> = {}): BenchmarkReportPackWrittenDocumentDto {
  return {
    audience,
    documentId,
    createdAtUtc: '2026-10-05T14:30:00Z',
    writerDisplayName: 'Claude 5.5 Opus',
    writerProvider: 'Anthropic',
    writerModelId: 'claude-opus-5-5',
    writerThinkingLevel: 'medium',
    status: 'Completed',
    durationMs: 72000,
    costUsd: 0.21,
    ...overrides
  };
}

function listItem(id: number, overrides: Partial<BenchmarkReportDocumentListItemDto> = {}): BenchmarkReportDocumentListItemDto {
  return {
    id,
    packId: 'pack',
    audience: ExecutiveSummary,
    title: 'Comparison #12 — Sol vs Luna vs Opus: Executive Summary',
    subjectKey: 'comparison:12',
    subjectLabel: 'Comparison #12',
    subjectRunIds: [1, 2, 3],
    suiteId: 5,
    suiteName: 'Board Suite',
    writerDisplayName: 'Claude 5.5 Opus',
    writerProvider: 'Anthropic',
    writerModelId: 'claude-opus-5-5',
    writerThinkingLevel: 'medium',
    sameProviderAcknowledged: false,
    status: 'Completed',
    reportFormatVersion: 12,
    createdAtUtc: '2026-10-05T14:30:00Z',
    inputTokens: 1000,
    outputTokens: 500,
    durationMs: 72000,
    costUsd: 0.21,
    runChangedSinceGeneration: false,
    missingRunIds: [],
    allowedDisclosures: [BenchmarkReportDisclosure.Summary, BenchmarkReportDisclosure.Detailed, BenchmarkReportDisclosure.Full],
    scope: BenchmarkReportScope.Comparison,
    chartCount: 6,
    chartFigureKeys: ['p1a-quality', 's2-quality-cost', 'p1b-speed'],
    ...overrides
  };
}

function preview(overrides: Partial<BenchmarkReportPackPreviewDto> = {}): BenchmarkReportPackPreviewDto {
  return {
    subjectKey: 'comparison:12',
    subjectLabel: 'Comparison #12',
    subjectState: 'Comparable',
    suiteName: 'Board Suite',
    peers: [],
    estimates: [],
    estimatedTotalCostUsd: null,
    writerDisplayName: null,
    sameProviderWarning: null,
    refusal: null,
    scope: BenchmarkReportScope.Comparison,
    comparisonId: 12,
    comparisonEntryCount: 3,
    coversAllEntries: true,
    writtenDocuments: [written(ExecutiveSummary, 41)],
    otherModelSets: [{
      coveredSetKey: 'abc123',
      subjectKey: 'comparison:12/abc123',
      coversAllEntries: false,
      coveredModels: [
        { entryKey: 'run:1', label: 'GPT-6.1 Sol (medium)', provider: 'OpenAI', letter: 'A' },
        { entryKey: 'run:2', label: 'GPT-5.6 Luna (max)', provider: 'OpenAI', letter: 'B' }
      ],
      documents: [written(InternalBrief, 44, { status: 'CompletedWithWarnings' })]
    }],
    subjectDocuments: [
      { subjectKey: 'run:1', subjectLabel: 'GPT-6.1 Sol (medium)', documents: [written(TechnicalReport, 51)] },
      { subjectKey: 'run:2', subjectLabel: 'GPT-5.6 Luna (max)', documents: [] }
    ],
    ...overrides
  };
}

function input(overrides: Partial<ComparisonDocumentsViewInput> = {}): ComparisonDocumentsViewInput {
  return {
    mode: 'comparison',
    preview: preview(),
    error: null,
    chosenKeys: ['run:1', 'run:2', 'run:3'],
    offeredKeys: ['run:1', 'run:2', 'run:3'],
    labelOf: key => LABELS[key] ?? key,
    listItems: new Map([[41, listItem(41)]]),
    checks: new Map(),
    ...overrides
  };
}

describe('comparisonDocumentsView', () => {
  it('lists the chosen set\'s three documents, written ones unchecked and the rest checked, then the other sets', () => {
    const view = comparisonDocumentsView(input());

    expect(view.state).toBe('ready');
    expect(view.groups.length).toBe(1);
    const rows = view.groups[0].rows;
    expect(rows.map(row => [row.key, row.document?.documentId ?? null, row.checked, row.checkable])).toEqual([
      [`comparison|${ExecutiveSummary}`, 41, false, true],
      [`comparison|${TechnicalReport}`, null, true, true],
      [`comparison|${InternalBrief}`, null, true, true]
    ]);
    expect(rows[0].listItem?.id).toBe(41);
    expect(rows[0].entryKey).toBeNull();

    expect(view.otherSets.length).toBe(1);
    const other = view.otherSets[0];
    expect(other.label).toBe('GPT-6.1 Sol (medium), GPT-5.6 Luna (max) — 2 of 3 models');
    expect(other.entryKeys).toEqual(['run:1', 'run:2']);
    expect(other.chooseRefusal).toBeNull();
    expect(other.rows.map(row => [row.audience, row.checkable])).toEqual([[InternalBrief, false]]);
  });

  it('follows the admin\'s Write and Rewrite choices', () => {
    const view = comparisonDocumentsView(input({
      checks: new Map([[`comparison|${ExecutiveSummary}`, true], [`comparison|${InternalBrief}`, false]])
    }));
    expect(view.groups[0].rows.map(row => row.checked)).toEqual([true, true, false]);
  });

  it('refuses to choose another set whose model is Excluded now', () => {
    const view = comparisonDocumentsView(input({ offeredKeys: ['run:2', 'run:3'] }));
    expect(view.otherSets[0].chooseRefusal).toBe('A model of this set is Excluded from the comparison now.');
  });

  it('groups per-model documents by chosen model', () => {
    const view = comparisonDocumentsView(input({ mode: 'model', chosenKeys: ['run:1', 'run:2'] }));

    expect(view.otherSets).toEqual([]);
    expect(view.groups.map(group => group.heading)).toEqual(['GPT-6.1 Sol (medium)', 'GPT-5.6 Luna (max)']);
    expect(view.groups[0].rows.map(row => [row.key, row.document?.documentId ?? null, row.checked])).toEqual([
      [`run:1|${ExecutiveSummary}`, null, true],
      [`run:1|${TechnicalReport}`, 51, false],
      [`run:1|${InternalBrief}`, null, true]
    ]);
    expect(view.groups[1].rows.every(row => row.entryKey === 'run:2' && row.document === null)).toBe(true);
  });

  it('is loading until the preview answers, and says why when it failed', () => {
    expect(comparisonDocumentsView(input({ preview: null })).state).toBe('loading');
    const failed = comparisonDocumentsView(input({ preview: null, error: 'Down.' }));
    expect(failed.state).toBe('error');
    expect(failed.message).toBe('Down.');
  });

  it('names the writer with its provider and thinking level, and counts figures', () => {
    expect(writerPhrase(written(ExecutiveSummary, 1))).toBe('by Claude 5.5 Opus (Anthropic; medium)');
    expect(writerPhrase({ writerDisplayName: null, writerProvider: null, writerThinkingLevel: null })).toBe('by an unknown writer');
    expect(chartsPhrase(listItem(1))).toBe('Charts: 3');
    expect(chartsPhrase(listItem(1, { chartFigureKeys: undefined, chartCount: 0 }))).toBe('No charts');
    expect(chartsPhrase(null)).toBeNull();
  });
});

describe('ComparisonDocumentsStatusComponent', () => {
  let fixture: ComponentFixture<ComparisonDocumentsStatusComponent>;
  let component: ComparisonDocumentsStatusComponent;
  let http: HttpTestingController;
  let host: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ComparisonDocumentsStatusComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    fixture = TestBed.createComponent(ComparisonDocumentsStatusComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => {
    host.querySelectorAll('dialog').forEach(dialog => {
      if (dialog.open) {
        dialog.close();
      }
    });
    fixture.destroy();
  });

  const q = <T extends HTMLElement = HTMLElement>(selector: string): T | null => host.querySelector<T>(selector);
  const text = (element: Element | null): string => (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const row = (key: string): HTMLElement => q(`.cds-row[data-row="${key}"]`)!;

  function show(viewInput: Partial<ComparisonDocumentsViewInput> = {}): void {
    fixture.componentRef.setInput('view', comparisonDocumentsView(input(viewInput)));
    fixture.detectChanges();
  }

  it('shows each row\'s status, writer, date, duration, cost, charts and change flag', () => {
    show({
      listItems: new Map([[41, listItem(41, { runChangedSinceGeneration: true })]])
    });

    expect(text(q('.cds-heading'))).toBe('Documents of this comparison');
    const executive = row(`comparison|${ExecutiveSummary}`);
    expect(text(executive.querySelector('.cds-doc-name'))).toBe('Executive Summary');
    expect(text(executive.querySelector('.cds-status'))).toBe('Written');
    expect(text(executive.querySelector('.cds-changed'))).toBe('Comparison changed since written');
    expect(text(executive.querySelector('.cds-writer'))).toBe('by Claude 5.5 Opus (Anthropic; medium)');
    const time = executive.querySelector('time')!;
    expect(time.getAttribute('datetime')).toBe('2026-10-05T14:30:00Z');
    expect(text(time)).toBe('2026-10-05 14:30 UTC');
    expect(Array.from(executive.querySelectorAll('.cds-meta-part')).map(text)).toEqual(['1 min 12 s', '$0.21', 'Charts: 3']);

    const technical = row(`comparison|${TechnicalReport}`);
    expect(text(technical.querySelector('.cds-status'))).toBe('Not written');
    expect(technical.querySelector('.cds-meta')).toBeNull();
    expect(technical.querySelector('.cds-view-btn')).toBeNull();
  });

  it('reads Write for a document not written and Rewrite for a written one, and reports each change', () => {
    show();
    const changes: { key: string; checked: boolean }[] = [];
    component.checkChange.subscribe(change => changes.push(change));

    const rewrite = row(`comparison|${ExecutiveSummary}`).querySelector<HTMLInputElement>('input[type="checkbox"]')!;
    expect(rewrite.checked).toBe(false);
    expect(text(rewrite.closest('label'))).toBe('Rewrite — replaces the current document: the Executive Summary of GPT-6.1 Sol (medium), GPT-5.6 Luna (max), Claude 5 Opus (high)');
    const write = row(`comparison|${TechnicalReport}`).querySelector<HTMLInputElement>('input[type="checkbox"]')!;
    expect(write.checked).toBe(true);
    expect(text(write.closest('label'))).toContain('Write: the Report for AI Researchers and Developers');

    rewrite.click();
    write.click();
    expect(changes).toEqual([
      { key: `comparison|${ExecutiveSummary}`, checked: true },
      { key: `comparison|${TechnicalReport}`, checked: false }
    ]);

    fixture.componentRef.setInput('busy', true);
    fixture.detectChanges();
    expect(rewrite.disabled).toBe(true);
    const deleteButton = row(`comparison|${ExecutiveSummary}`).querySelector<HTMLButtonElement>('.cds-delete-btn')!;
    expect(deleteButton.getAttribute('aria-disabled')).toBe('true');
    deleteButton.click();
    fixture.detectChanges();
    expect(q<HTMLDialogElement>('dialog.cds-delete-dialog')!.open).toBe(false);
  });

  it('names its icon-only View and Delete with the document, with tooltips and no title', () => {
    show();
    const executive = row(`comparison|${ExecutiveSummary}`);
    const view = executive.querySelector<HTMLButtonElement>('.cds-view-btn')!;
    const del = executive.querySelector<HTMLButtonElement>('.cds-delete-btn')!;
    expect(view.classList).toContain('action-btn');
    expect(del.classList).toContain('action-btn-danger');
    expect(view.getAttribute('aria-label')).toBe('View the Executive Summary of GPT-6.1 Sol (medium), GPT-5.6 Luna (max), Claude 5 Opus (high)');
    expect(del.getAttribute('aria-label')).toContain('Delete the Executive Summary');
    for (const button of [view, del]) {
      expect(button.hasAttribute('title')).toBe(false);
      const tip = q(`#${button.getAttribute('interestfor')}`)!;
      expect(tip.getAttribute('popover')).toBe('hint');
    }
  });

  it('opens a document in the PDF viewer at its fullest disclosure, with the model names as a second choice', () => {
    show();
    const open = vi.spyOn(component.pdfViewer!, 'open').mockReturnValue(undefined);

    row(`comparison|${ExecutiveSummary}`).querySelector<HTMLButtonElement>('.cds-view-btn')!.click();
    const request = vi.mocked(open).mock.lastCall![0] as PdfViewerRequest;
    expect(request.title).toBe('Comparison #12 — Sol vs Luna vs Opus: Executive Summary');
    expect(request.subtitle).toBe('by Claude 5.5 Opus (Anthropic; medium) on 2026-10-05 14:30 UTC');
    expect(request.variants!.map(variant => variant.key)).toEqual(['summary', 'detailed', 'full']);
    expect(request.initialVariant).toBe('full');
    expect(request.secondaryVariants?.label).toBe('Model names');

    request.load('detailed', 'anonymized').subscribe();
    const pdf = http.expectOne(r => r.url === '/api/admin/benchmark/report-documents/41/render/pdf');
    expect(pdf.request.params.get('disclosure')).toBe('detailed');
    expect(pdf.request.params.get('peers')).toBe('anonymized');
  });

  it('deletes a document after a confirmation and says so, focus going to the list\'s heading', async () => {
    show();
    const deleted: number[] = [];
    component.documentDeleted.subscribe(id => deleted.push(id));

    // Cancel: nothing is deleted.
    row(`comparison|${ExecutiveSummary}`).querySelector<HTMLButtonElement>('.cds-delete-btn')!.click();
    fixture.detectChanges();
    const dialog = q<HTMLDialogElement>('dialog.cds-delete-dialog')!;
    expect(dialog.open).toBe(true);
    expect(text(dialog.querySelector('.cds-delete-text'))).toContain('Delete Comparison #12 — Sol vs Luna vs Opus: Executive Summary');
    q<HTMLButtonElement>('.cds-delete-cancel')!.click();
    expect(dialog.open).toBe(false);
    http.expectNone(r => r.method === 'DELETE');

    row(`comparison|${ExecutiveSummary}`).querySelector<HTMLButtonElement>('.cds-delete-btn')!.click();
    fixture.detectChanges();
    q<HTMLButtonElement>('.cds-delete-confirm')!.click();
    const request = http.expectOne('/api/admin/benchmark/report-documents/41');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(deleted).toEqual([41]);
    expect(dialog.open).toBe(false);
    expect(text(q('.cds-live'))).toBe('Deleted the Executive Summary of GPT-6.1 Sol (medium), GPT-5.6 Luna (max), Claude 5 Opus (high).');
    // The close event is queued after close().
    for (let i = 0; i < 50 && document.activeElement !== q('.cds-heading'); i++) {
      await new Promise(resolve => setTimeout(resolve, 10));
    }
    expect(document.activeElement).toBe(q('.cds-heading'));
  });

  it('shows the server\'s refusal of a delete in the confirmation', () => {
    show();
    row(`comparison|${ExecutiveSummary}`).querySelector<HTMLButtonElement>('.cds-delete-btn')!.click();
    fixture.detectChanges();
    q<HTMLButtonElement>('.cds-delete-confirm')!.click();
    http.expectOne('/api/admin/benchmark/report-documents/41').flush({ error: 'A job is writing.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(q<HTMLDialogElement>('dialog.cds-delete-dialog')!.open).toBe(true);
    expect(text(q('.cds-delete-error'))).toBe('A job is writing.');
  });

  it('keeps other model sets in a closed disclosure, each with Choose these models', () => {
    show();
    const chosen: (readonly string[])[] = [];
    component.chooseModels.subscribe(keys => chosen.push(keys));

    const details = q<HTMLDetailsElement>('details.cds-other-sets')!;
    expect(details.open).toBe(false);
    expect(text(details.querySelector('summary'))).toBe('Other model sets (1)');
    expect(text(details.querySelector('.cds-set-title'))).toBe('GPT-6.1 Sol (medium), GPT-5.6 Luna (max) — 2 of 3 models');
    const setRow = details.querySelector('.cds-row')!;
    expect(text(setRow.querySelector('.cds-status'))).toBe('Written with warnings');
    expect(setRow.querySelector('input[type="checkbox"]')).toBeNull();
    expect(setRow.querySelector('.cds-view-btn')).not.toBeNull();
    expect(setRow.querySelector('.cds-delete-btn')).not.toBeNull();

    details.querySelector<HTMLButtonElement>('.cds-choose-set')!.click();
    expect(chosen).toEqual([['run:1', 'run:2']]);

    // A set that cannot be chosen says why, and its link does nothing.
    show({ offeredKeys: ['run:2', 'run:3'] });
    const link = q<HTMLButtonElement>('.cds-choose-set')!;
    expect(link.getAttribute('aria-disabled')).toBe('true');
    expect(text(q(`#${link.getAttribute('aria-describedby')}`))).toBe('A model of this set is Excluded from the comparison now.');
    link.click();
    expect(chosen.length).toBe(1);
  });

  it('groups per-model rows under their model', () => {
    show({ mode: 'model', chosenKeys: ['run:1', 'run:2'] });

    const titles = Array.from(host.querySelectorAll('.cds-group-title')).map(text);
    expect(titles).toEqual(['GPT-6.1 Sol (medium)', 'GPT-5.6 Luna (max)']);
    const lists = Array.from(host.querySelectorAll<HTMLElement>('ul.cds-list'));
    expect(lists.length).toBe(2);
    expect(lists[0].getAttribute('aria-labelledby')).toBe(host.querySelectorAll('.cds-group-title')[0].id);
    expect(lists[0].querySelectorAll('.cds-row').length).toBe(3);
    expect(q('details.cds-other-sets')).toBeNull();
  });

  it('says it is looking the documents up, and why it could not', () => {
    show({ preview: null });
    expect(text(q('.cds-loading'))).toBe('Looking up the documents already written…');
    expect(q('.cds')!.getAttribute('aria-busy')).toBe('true');

    show({ preview: null, error: 'The comparison could not be computed.' });
    expect(text(q('.cds-error'))).toBe('The comparison could not be computed.');
  });

  it('stops the close and cancel events of its nested dialogs', () => {
    show();
    const heard: string[] = [];
    host.addEventListener('close', () => heard.push('close'));
    host.addEventListener('cancel', () => heard.push('cancel'));
    for (const dialog of Array.from(host.querySelectorAll('dialog'))) {
      dialog.dispatchEvent(new Event('cancel', { bubbles: true, cancelable: true }));
      dialog.dispatchEvent(new Event('close', { bubbles: true }));
    }
    expect(heard).toEqual([]);
  });
});
