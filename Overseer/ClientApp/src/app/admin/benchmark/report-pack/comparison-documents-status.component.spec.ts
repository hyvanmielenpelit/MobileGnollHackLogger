import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import {
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPackDocumentsDto,
  BenchmarkReportPackPreviewDto,
  BenchmarkReportPackWrittenDocumentDto,
  BenchmarkReportScope
} from '../../../services/admin-benchmark.service';
import type { PdfViewerRequest } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { BenchmarkShellBridge, LauncherSetupPrefill } from '../state/benchmark-shell-bridge.service';
import { recommendedRun } from './root-cause.testing';
import {
  ComparisonDocumentRow,
  ComparisonDocumentsStatusComponent,
  ComparisonDocumentsViewInput,
  chartsPhrase,
  comparisonDocumentsView,
  writerPhrase
} from './comparison-documents-status.component';

const { ExecutiveSummary, RootCauseAnalysis } = BenchmarkReportAudience;

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
      documents: [written(RootCauseAnalysis, 44, { status: 'CompletedWithWarnings' })]
    }],
    subjectDocuments: [
      { subjectKey: 'run:1', subjectLabel: 'GPT-6.1 Sol (medium)', documents: [written(RootCauseAnalysis, 51)] },
      { subjectKey: 'run:2', subjectLabel: 'GPT-5.6 Luna (max)', documents: [] }
    ],
    ...overrides
  };
}

/** The stored documents' answer with a preview's lists. */
function documentsOf(source: BenchmarkReportPackPreviewDto): BenchmarkReportPackDocumentsDto {
  return {
    scope: source.scope ?? BenchmarkReportScope.Comparison,
    comparisonId: source.comparisonId ?? null,
    coveredSetKey: source.coveredSetKey ?? null,
    writtenDocuments: source.writtenDocuments ?? [],
    otherModelSets: source.otherModelSets ?? [],
    subjectDocuments: source.subjectDocuments ?? []
  };
}

function input(overrides: Partial<ComparisonDocumentsViewInput> = {}): ComparisonDocumentsViewInput {
  return {
    mode: 'comparison',
    preview: preview(),
    documents: null,
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
  it('lists the chosen set\'s two documents, the Root Cause Analysis first, written ones unchecked and the rest checked, then the other sets', () => {
    const view = comparisonDocumentsView(input());

    expect(view.state).toBe('ready');
    expect(view.groups.length).toBe(1);
    const rows = view.groups[0].rows;
    expect(rows.map(row => [row.key, row.document?.documentId ?? null, row.checked, row.checkable])).toEqual([
      [`comparison|${RootCauseAnalysis}`, null, true, true],
      [`comparison|${ExecutiveSummary}`, 41, false, true]
    ]);
    expect(rows.map(row => row.name)).toEqual(['Root Cause Analysis', 'Executive Summary']);
    expect(rows[1].listItem?.id).toBe(41);
    expect(rows[1].entryKey).toBeNull();
    expect(rows.every(row => row.blockedReason === null && row.blockedFix === null)).toBe(true);

    expect(view.otherSets.length).toBe(1);
    const other = view.otherSets[0];
    expect(other.label).toBe('GPT-6.1 Sol (medium), GPT-5.6 Luna (max) — 2 of 3 models');
    expect(other.entryKeys).toEqual(['run:1', 'run:2']);
    expect(other.chooseRefusal).toBeNull();
    expect(other.rows.map(row => [row.audience, row.checkable])).toEqual([[RootCauseAnalysis, false]]);
  });

  it('never lists a document of a retired type', () => {
    const view = comparisonDocumentsView(input({
      preview: preview({ writtenDocuments: [written(2 as BenchmarkReportAudience, 42), written(3 as BenchmarkReportAudience, 43)] })
    }));
    expect(view.groups[0].rows.map(row => row.audience)).toEqual([RootCauseAnalysis, ExecutiveSummary]);
    expect(view.groups[0].rows.every(row => row.document === null)).toBe(true);
  });

  it('follows the admin\'s Write and Rewrite choices', () => {
    const view = comparisonDocumentsView(input({
      checks: new Map([[`comparison|${ExecutiveSummary}`, true], [`comparison|${RootCauseAnalysis}`, true]])
    }));
    expect(view.groups[0].rows.map(row => row.checked)).toEqual([true, true]);

    const none = comparisonDocumentsView(input({
      checks: new Map([[`comparison|${ExecutiveSummary}`, false], [`comparison|${RootCauseAnalysis}`, false]])
    }));
    expect(none.groups[0].rows.map(row => row.checked)).toEqual([false, false]);
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
      [`run:1|${RootCauseAnalysis}`, 51, false],
      [`run:1|${ExecutiveSummary}`, null, true]
    ]);
    expect(view.groups[1].rows.every(row => row.entryKey === 'run:2' && row.document === null)).toBe(true);
  });

  it('is loading until the preview answers, and says why when it failed', () => {
    expect(comparisonDocumentsView(input({ preview: null })).state).toBe('loading');
    const failed = comparisonDocumentsView(input({ preview: null, error: 'Down.' }));
    expect(failed.state).toBe('error');
    expect(failed.message).toBe('Down.');
  });

  it('builds ready rows from the stored documents while the preview is awaited, and shows no preview error then', () => {
    const listed = documentsOf(preview());
    const view = comparisonDocumentsView(input({
      preview: null, documents: listed, error: 'Down.', offeredKeys: ['run:1', 'run:2', 'run:3', 'run:4']
    }));

    expect(view.state).toBe('ready');
    expect(view.message).toBeNull();
    expect(view.groups[0].rows.map(row => [row.key, row.document?.documentId ?? null, row.checked])).toEqual([
      [`comparison|${RootCauseAnalysis}`, null, true],
      [`comparison|${ExecutiveSummary}`, 41, false]
    ]);
    expect(view.groups[0].rows[1].listItem?.id).toBe(41);
    // Without the preview, a set's size is out of the models offered.
    expect(view.otherSets.map(set => set.label)).toEqual(['GPT-6.1 Sol (medium), GPT-5.6 Luna (max) — 2 of 4 models']);

    const perModel = comparisonDocumentsView(input({ mode: 'model', chosenKeys: ['run:1', 'run:2'], preview: null, documents: listed }));
    expect(perModel.state).toBe('ready');
    expect(perModel.groups.map(group => group.rows.map(row => row.document?.documentId ?? null))).toEqual([[51, null], [null, null]]);
  });

  it('takes the stored documents\' lists over the preview\'s', () => {
    const view = comparisonDocumentsView(input({
      documents: documentsOf(preview({ writtenDocuments: [written(RootCauseAnalysis, 45)], otherModelSets: [] }))
    }));

    expect(view.groups[0].rows.map(row => row.document?.documentId ?? null)).toEqual([45, null]);
    expect(view.otherSets).toEqual([]);
  });

  describe('the Executive Summary and its Root Cause Analysis', () => {
    const summaryOf = (view: ReturnType<typeof comparisonDocumentsView>, group = 0): ComparisonDocumentRow =>
      view.groups[group].rows.find(row => row.audience === ExecutiveSummary)!;
    const unchecked = (key: string): Map<string, boolean> => new Map([[key, false]]);

    it('blocks the summary while its Root Cause Analysis is neither checked nor written, by scope, keeping the admin\'s choice', () => {
      const comparison = comparisonDocumentsView(input({
        preview: preview({ writtenDocuments: [] }),
        checks: new Map([[`comparison|${RootCauseAnalysis}`, false], [`comparison|${ExecutiveSummary}`, true]])
      }));
      expect(summaryOf(comparison)).toEqual(expect.objectContaining({
        checked: false,
        blockedReason: 'Needs this comparison\'s Root Cause Analysis: check Write on the Root Cause Analysis above.',
        blockedFix: 'checkRootCause'
      }));
      // The Root Cause Analysis row itself is never blocked.
      expect(comparison.groups[0].rows[0].blockedReason).toBeNull();

      const perModel = comparisonDocumentsView(input({
        mode: 'model', chosenKeys: ['run:2'], checks: unchecked(`run:2|${RootCauseAnalysis}`)
      }));
      expect(summaryOf(perModel)).toEqual(expect.objectContaining({
        checked: false,
        blockedReason: 'Needs this model\'s Root Cause Analysis: check Write on the Root Cause Analysis above.',
        blockedFix: 'checkRootCause'
      }));

      // Checked again, the summary takes back the admin's own choice.
      const again = comparisonDocumentsView(input({
        preview: preview({ writtenDocuments: [] }),
        checks: new Map([[`comparison|${RootCauseAnalysis}`, true], [`comparison|${ExecutiveSummary}`, true]])
      }));
      expect(summaryOf(again)).toEqual(expect.objectContaining({ checked: true, blockedReason: null, blockedFix: null }));
    });

    it('blocks the summary on a written Root Cause Analysis written from different data until Rewrite is checked on it', () => {
      const outdated = preview({
        writtenDocuments: [written(RootCauseAnalysis, 42, { summaryReadiness: 'outdated', summaryReadinessMessage: 'Different data.' })]
      });
      const view = comparisonDocumentsView(input({ preview: outdated }));
      expect(summaryOf(view)).toEqual(expect.objectContaining({
        checked: false,
        blockedReason: 'Root Cause Analysis #42 was written from different data. Check Rewrite on it to write both.',
        blockedFix: 'checkRootCauseRewrite'
      }));

      const rewrite = comparisonDocumentsView(input({ preview: outdated, checks: new Map([[`comparison|${RootCauseAnalysis}`, true]]) }));
      expect(summaryOf(rewrite)).toEqual(expect.objectContaining({ checked: true, blockedReason: null }));

      // Per model, the readiness comes from the model's own documents.
      const perModel = comparisonDocumentsView(input({
        mode: 'model',
        chosenKeys: ['run:1'],
        preview: preview({
          subjectDocuments: [{ subjectKey: 'run:1', subjectLabel: 'GPT-6.1 Sol (medium)', documents: [written(RootCauseAnalysis, 51, { summaryReadiness: 'outdated' })] }]
        })
      }));
      expect(summaryOf(perModel).blockedReason)
        .toBe('Root Cause Analysis #51 was written from different data. Check Rewrite on it to write both.');
    });

    it('leaves the summary free on a written Root Cause Analysis the preview finds usable or says nothing about', () => {
      const usable = comparisonDocumentsView(input({
        preview: preview({ writtenDocuments: [written(RootCauseAnalysis, 42, { summaryReadiness: 'usable' })] })
      }));
      expect(summaryOf(usable)).toEqual(expect.objectContaining({ checked: true, blockedReason: null, blockedFix: null }));

      const silent = comparisonDocumentsView(input({ preview: preview({ writtenDocuments: [written(RootCauseAnalysis, 42)] }) }));
      expect(summaryOf(silent).blockedReason).toBeNull();
    });

    it('says the stored Root Cause Analysis is being checked while the preview is awaited, and when it failed', () => {
      const listed = documentsOf(preview({ writtenDocuments: [written(RootCauseAnalysis, 42)] }));
      const pending = comparisonDocumentsView(input({ preview: null, documents: listed }));
      expect(summaryOf(pending)).toEqual(expect.objectContaining({
        checked: false, blockedReason: 'Checking the stored Root Cause Analysis…', blockedFix: null
      }));

      const failed = comparisonDocumentsView(input({ preview: null, documents: listed, error: 'Down.' }));
      expect(summaryOf(failed)).toEqual(expect.objectContaining({
        checked: false, blockedReason: 'The stored Root Cause Analysis could not be checked.', blockedFix: null
      }));
    });
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
      providers: [BenchmarkShellBridge, provideHttpClient(), provideHttpClientTesting()]
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

    const technical = row(`comparison|${RootCauseAnalysis}`);
    expect(text(technical.querySelector('.cds-status'))).toBe('Not written');
    expect(technical.querySelector('.cds-meta')).toBeNull();
    expect(technical.querySelector('.cds-view-btn')).toBeNull();
  });

  it('says the write order of the checked documents, by scope', () => {
    const hints = (): string[] => Array.from(host.querySelectorAll('.cds-order-hint')).map(text);
    const both = 'Written in this order: the Root Cause Analysis first, then the Executive Summary, which is written from it.';

    show({ checks: new Map([[`comparison|${ExecutiveSummary}`, true]]) });
    expect(hints()).toEqual([both]);
    expect(q('.cds-order-hint')!.classList).toContain('form-hint');

    // The Executive Summary without its Root Cause Analysis is blocked, and its row says why: no line.
    show({ checks: new Map([[`comparison|${ExecutiveSummary}`, true], [`comparison|${RootCauseAnalysis}`, false]]) });
    expect(hints()).toEqual([]);

    // The Root Cause Analysis alone: no line.
    show();
    expect(hints()).toEqual([]);

    // Per model: run:1 has its Root Cause Analysis, so only its Executive Summary is checked; run:2 writes both.
    show({ mode: 'model', chosenKeys: ['run:1', 'run:2'] });
    expect(hints()).toEqual([both]);
  });

  it('lists the Root Cause Analysis first in every group', () => {
    show();
    // The chosen set's list comes first; the other sets follow in their disclosure.
    const chosen = host.querySelector('ul.cds-list')!;
    expect(Array.from(chosen.querySelectorAll('.cds-row')).map(item => item.getAttribute('data-audience')))
      .toEqual([String(RootCauseAnalysis), String(ExecutiveSummary)]);

    show({ mode: 'model', chosenKeys: ['run:1', 'run:2'] });
    for (const list of Array.from(host.querySelectorAll('ul.cds-list'))) {
      expect(Array.from(list.querySelectorAll('.cds-row')).map(item => item.getAttribute('data-audience')))
        .toEqual([String(RootCauseAnalysis), String(ExecutiveSummary)]);
    }
  });

  describe('a blocked Executive Summary', () => {
    const blockedInput = (): Partial<ComparisonDocumentsViewInput> => ({
      preview: preview({ writtenDocuments: [] }),
      checks: new Map([[`comparison|${RootCauseAnalysis}`, false]])
    });
    const summaryCheck = (): HTMLInputElement =>
      row(`comparison|${ExecutiveSummary}`).querySelector<HTMLInputElement>('input[type="checkbox"]')!;

    it('is aria-disabled, unchecked and described by its visible reason line, its label muted', () => {
      show(blockedInput());
      const check = summaryCheck();
      expect(check.checked).toBe(false);
      expect(check.disabled).toBe(false);
      expect(check.getAttribute('aria-disabled')).toBe('true');
      const reason = q(`#${check.getAttribute('aria-describedby')}`)!;
      expect(reason.classList).toContain('cds-blocked-reason');
      expect(text(reason)).toBe('Needs this comparison\'s Root Cause Analysis: check Write on the Root Cause Analysis above.');
      expect(check.closest('label')!.classList).toContain('is-blocked');
      expect(q('.cds-blocked-error')).toBeNull();

      // The Root Cause Analysis row is never blocked.
      const rca = row(`comparison|${RootCauseAnalysis}`).querySelector<HTMLInputElement>('input[type="checkbox"]')!;
      expect(rca.hasAttribute('aria-disabled')).toBe(false);
      expect(rca.hasAttribute('aria-describedby')).toBe(false);
    });

    it('refuses a click, shows the inline error with its fix, and the fix checks both and focuses the summary', () => {
      show(blockedInput());
      const changes: { key: string; checked: boolean }[] = [];
      component.checkChange.subscribe(change => changes.push(change));

      summaryCheck().click();
      fixture.detectChanges();
      expect(summaryCheck().checked).toBe(false);
      expect(changes).toEqual([]);
      const error = q('.cds-blocked-error')!;
      expect(error.classList).toContain('gh-field-error');
      expect(error.getAttribute('role')).toBe('alert');
      expect(error.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
      expect(text(error)).toContain('The Executive Summary is written from the Root Cause Analysis, so it cannot be written alone here.');
      expect(summaryCheck().getAttribute('aria-describedby')).toBe(`${error.id.replace(/-error$/, '-reason')} ${error.id}`);

      const fix = error.querySelector<HTMLButtonElement>('button.cds-blocked-fix')!;
      expect(fix.getAttribute('type')).toBe('button');
      expect(fix.classList).toContain('btn-link');
      expect(text(fix)).toContain('Check the Root Cause Analysis');
      fix.click();
      expect(changes).toEqual([
        { key: `comparison|${RootCauseAnalysis}`, checked: true },
        { key: `comparison|${ExecutiveSummary}`, checked: true }
      ]);
      expect(document.activeElement).toBe(summaryCheck());

      // The host's next view, with both checked, has no error and no reason.
      show({ preview: preview({ writtenDocuments: [] }), checks: new Map([[`comparison|${RootCauseAnalysis}`, true]]) });
      expect(q('.cds-blocked-error')).toBeNull();
      expect(q('.cds-blocked-reason')).toBeNull();
      expect(summaryCheck().hasAttribute('aria-disabled')).toBe(false);
      expect(summaryCheck().checked).toBe(true);
    });

    it('offers Rewrite on an outdated Root Cause Analysis as the fix, and sends nothing for a Space press on the summary', () => {
      show({ preview: preview({ writtenDocuments: [written(RootCauseAnalysis, 42, { summaryReadiness: 'outdated' })] }) });
      const changes: { key: string; checked: boolean }[] = [];
      component.checkChange.subscribe(change => changes.push(change));

      expect(text(q('.cds-blocked-reason')))
        .toBe('Root Cause Analysis #42 was written from different data. Check Rewrite on it to write both.');
      // A Space press arrives as a click.
      summaryCheck().dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
      fixture.detectChanges();
      expect(changes).toEqual([]);
      const fix = q<HTMLButtonElement>('.cds-blocked-fix')!;
      expect(text(fix)).toContain('Check Rewrite on the Root Cause Analysis');
      fix.click();
      expect(changes).toEqual([
        { key: `comparison|${RootCauseAnalysis}`, checked: true },
        { key: `comparison|${ExecutiveSummary}`, checked: true }
      ]);
    });
  });

  it('reads Write for a document not written and Rewrite for a written one, and reports each change', () => {
    show();
    const changes: { key: string; checked: boolean }[] = [];
    component.checkChange.subscribe(change => changes.push(change));

    const rewrite = row(`comparison|${ExecutiveSummary}`).querySelector<HTMLInputElement>('input[type="checkbox"]')!;
    expect(rewrite.checked).toBe(false);
    expect(text(rewrite.closest('label'))).toBe('Rewrite — replaces the current document: the Executive Summary of GPT-6.1 Sol (medium), GPT-5.6 Luna (max), Claude 5 Opus (high)');
    const write = row(`comparison|${RootCauseAnalysis}`).querySelector<HTMLInputElement>('input[type="checkbox"]')!;
    expect(write.checked).toBe(true);
    expect(text(write.closest('label'))).toContain('Write: the Root Cause Analysis');

    rewrite.click();
    write.click();
    expect(changes).toEqual([
      { key: `comparison|${ExecutiveSummary}`, checked: true },
      { key: `comparison|${RootCauseAnalysis}`, checked: false }
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
    expect(lists[0].querySelectorAll('.cds-row').length).toBe(2);
    expect(q('details.cds-other-sets')).toBeNull();
  });

  it('says it is looking the documents up, and why it could not', () => {
    show({ preview: null });
    expect(text(q('.cds-loading'))).toBe('Looking up the documents already written…');
    expect(q('.cds-loading')!.tagName.toLowerCase()).toBe('app-loading-indicator');
    expect(q('.cds-loading svg.dc-ring')).not.toBeNull();
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

  const withRootCause = (): Partial<ComparisonDocumentsViewInput> => ({
    preview: preview({ writtenDocuments: [written(ExecutiveSummary, 41), written(RootCauseAnalysis, 42)] })
  });

  it('adds an icon-only Redraw charts to a written Root Cause Analysis only, and redraws through the service', () => {
    show(withRootCause());

    expect(row(`comparison|${ExecutiveSummary}`).querySelector('.cds-redraw-btn')).toBeNull();
    const redraw = row(`comparison|${RootCauseAnalysis}`).querySelector<HTMLButtonElement>('.cds-redraw-btn')!;
    expect(redraw.classList).toContain('action-btn');
    expect(redraw.getAttribute('aria-label')).toContain('Redraw the charts of the Root Cause Analysis');
    expect(redraw.hasAttribute('title')).toBe(false);
    expect(q(`#${redraw.getAttribute('interestfor')}`)!.getAttribute('popover')).toBe('hint');
    redraw.click();

    const request = http.expectOne('/api/admin/benchmark/report-documents/42/charts/redraw');
    expect(request.request.method).toBe('POST');
    request.flush({ documentId: 42, chartCount: 8, figureKeys: [], settingsHash: null });
    fixture.detectChanges();

    expect(text(q('.cds-live'))).toContain('Redrew the charts of the Root Cause Analysis');
    expect(text(q('.cds-live'))).toContain('(8 images)');
  });

  it('shows a refused redraw', () => {
    show(withRootCause());
    row(`comparison|${RootCauseAnalysis}`).querySelector<HTMLButtonElement>('.cds-redraw-btn')!.click();
    http.expectOne('/api/admin/benchmark/report-documents/42/charts/redraw')
      .flush({ error: 'The document has no stored facts.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(text(q('.cds-redraw-error'))).toBe('The document has no stored facts.');
  });

  it('explains a document written with warnings from an info button, reading its issues when first opened', () => {
    show({ preview: preview({ writtenDocuments: [written(ExecutiveSummary, 41, { status: 'CompletedWithWarnings' })] }) });
    const executive = row(`comparison|${ExecutiveSummary}`);
    expect(row(`comparison|${RootCauseAnalysis}`).querySelector('.cds-why')).toBeNull();

    const button = executive.querySelector<HTMLButtonElement>('.cds-why button.gh-info-btn')!;
    const name = 'warnings in the Executive Summary of GPT-6.1 Sol (medium), GPT-5.6 Luna (max), Claude 5 Opus (high)';
    expect(button.getAttribute('aria-label')).toBe(`About ${name}`);
    expect(button.getAttribute('aria-haspopup')).toBe('dialog');
    expect(button.hasAttribute('title')).toBe(false);
    // Nothing is read until the dialog is opened.
    http.expectNone('/api/admin/benchmark/report-documents/41');

    button.click();
    fixture.detectChanges();
    const dialog = executive.querySelector<HTMLDialogElement>('.cds-why dialog')!;
    expect(dialog.open).toBe(true);
    expect(text(dialog.querySelector('h3'))).toBe(
      'Why the Executive Summary of GPT-6.1 Sol (medium), GPT-5.6 Luna (max), Claude 5 Opus (high) was completed with warnings');
    expect(document.activeElement).toBe(dialog.querySelector('h3'));
    expect(text(dialog.querySelector('.cds-why-loading'))).toBe('Reading the check results…');

    http.expectOne('/api/admin/benchmark/report-documents/41').flush({
      validationNotes: [{ rule: 16, location: 'sections.abstract', message: 'Ranks overlapping models.', dropped: false }],
      validationIssues: [{
        rule: 16, location: 'sections.abstract', message: 'Ranks overlapping models.', dropped: false,
        ruleName: 'Ranking without the overlap caveat', severity: 'warning', locationLabel: 'Abstract, paragraph 1', text: 'A beats B.'
      }]
    });
    fixture.detectChanges();
    expect(text(dialog.querySelector('.report-validation-summary'))).toBe('This document is usable. One warning was kept.');
    expect(text(dialog.querySelector('.report-validation-kept .report-validation-rule'))).toBe('Ranking without the overlap caveat');
    expect(text(dialog.querySelector('.report-validation-kept .report-validation-location'))).toBe('Abstract, paragraph 1');
    // A stored document offers no diagnostics to copy.
    expect(text(dialog.querySelector('.report-validation-actions'))).not.toContain('diagnostics');
    dialog.close();

    // Opened again, it is not read again.
    button.click();
    http.expectNone('/api/admin/benchmark/report-documents/41');
  });

  it('falls back to the notes without rule names from a server that sends no validation issues', () => {
    show({ preview: preview({ writtenDocuments: [written(ExecutiveSummary, 41, { status: 'CompletedWithWarnings' })] }) });
    const executive = row(`comparison|${ExecutiveSummary}`);
    executive.querySelector<HTMLButtonElement>('.cds-why button.gh-info-btn')!.click();
    http.expectOne('/api/admin/benchmark/report-documents/41').flush({
      validationNotes: [{ rule: 12, location: 'sections.abstract', message: 'British spelling.', dropped: false }]
    });
    fixture.detectChanges();
    const dialog = executive.querySelector<HTMLDialogElement>('.cds-why dialog')!;
    expect(text(dialog.querySelector('.report-validation-kept .report-validation-rule'))).toBe('Rule 12');
    expect(text(dialog.querySelector('.report-validation-kept .report-validation-location'))).toBe('sections.abstract');
    dialog.close();
  });

  it('hosts the next runs under a written Root Cause Analysis and sets one up through the bridge', () => {
    const prefills: LauncherSetupPrefill[] = [];
    TestBed.inject(BenchmarkShellBridge).prefillSetup$.subscribe(prefill => prefills.push(prefill));
    show(withRootCause());

    expect(row(`comparison|${ExecutiveSummary}`).querySelector('app-root-cause-runs')).toBeNull();
    const rca = row(`comparison|${RootCauseAnalysis}`);
    const details = rca.querySelector<HTMLDetailsElement>('app-root-cause-runs details')!;
    expect(details.open).toBe(false);
    details.open = true;
    details.dispatchEvent(new Event('toggle'));
    http.expectOne('/api/admin/benchmark/report-documents/42').flush({ recommendedRuns: [recommendedRun()], checks: [] });
    fixture.detectChanges();
    rca.querySelector<HTMLButtonElement>('.rcr-set-up')!.click();

    expect(prefills.map(prefill => prefill.sourceLabel)).toEqual(['Set up from Root Cause Analysis #42, run 1']);
  });
});
