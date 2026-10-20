import { ComponentFixture, TestBed } from '@angular/core/testing';

import {
  BenchmarkReportAudience,
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkReportPackValidationIssueDto
} from '../../../services/admin-benchmark.service';
import {
  REPORT_JOB_DOCUMENT_CARD_DEFAULTS,
  ReportJobDocumentCard,
  ReportJobDocumentsComponent,
  reportJobDocumentCost,
  reportJobDocumentTokens,
  reportJobDocumentTone,
  reuseReportJobDocumentCards
} from './report-job-documents.component';

const { ExecutiveSummary, RootCauseAnalysis } = BenchmarkReportAudience;

const DIGIT: BenchmarkReportPackValidationIssueDto = {
  rule: 3, ruleName: 'Digit in prose', severity: 'removesItem', location: 'sections.conclusions[0]', locationLabel: 'Conclusions, paragraph 1',
  message: 'The paragraph holds a number of its own.', dropped: true, text: 'The model scored 72.'
};

function card(overrides: Partial<ReportJobDocumentCard> = {}): ReportJobDocumentCard {
  return {
    ...REPORT_JOB_DOCUMENT_CARD_DEFAULTS,
    key: `comparison:4|${RootCauseAnalysis}`,
    audience: RootCauseAnalysis,
    title: 'Root Cause Analysis',
    phrase: 'the Root Cause Analysis',
    statusWord: 'Completed with warnings',
    chipClass: 'job-status-chip status-partial',
    tone: 'warning',
    duration: '5 min 04 s',
    modelCalls: '2',
    tokens: '390,985 in · 11,875 out',
    cost: '$0.54',
    tipStem: `rjd-test-${RootCauseAnalysis}`,
    ...overrides
  };
}

describe('report-job-documents helpers', () => {
  it('words the tokens and the cost, and gives a warning or a failure its edge', () => {
    expect(reportJobDocumentTokens(390985, 11875)).toBe('390,985 in · 11,875 out');
    expect(reportJobDocumentTokens(6000, undefined)).toBe('6,000 in · 0 out');
    expect(reportJobDocumentTokens(undefined, null)).toBe('—');
    expect(reportJobDocumentCost(0.54)).toBe('$0.54');
    expect(reportJobDocumentCost(null)).toBe('Unknown');
    expect(reportJobDocumentCost(undefined)).toBe('—');
    expect(['CompletedWithWarnings', 'Failed', 'Completed', 'Writing'].map(reportJobDocumentTone)).toEqual(['warning', 'failed', null, null]);
  });

  it('keeps the previous cards while nothing on them changes', () => {
    const first = [card({ meta: ['GPT-6 Luna (high)'], charts: { text: 'Charts: 8', retry: false } })];
    const same = [card({ meta: ['GPT-6 Luna (high)'], charts: { text: 'Charts: 8', retry: false } })];
    expect(reuseReportJobDocumentCards(first, same)).toBe(first);
    const later = [card({ meta: ['GPT-6 Luna (high)'], charts: { text: 'Charts: 8', retry: false }, duration: '5 min 05 s' })];
    expect(reuseReportJobDocumentCards(first, later)).toBe(later);
    expect(reuseReportJobDocumentCards(first, [])).toEqual([]);
  });
});

describe('ReportJobDocumentsComponent', () => {
  let fixture: ComponentFixture<ReportJobDocumentsComponent>;
  let host: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ReportJobDocumentsComponent] }).compileComponents();
    fixture = TestBed.createComponent(ReportJobDocumentsComponent);
    host = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('idPrefix', 'rjd-test');
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
  const text = (selector: string): string => (q(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();

  function show(rows: ReportJobDocumentCard[]): void {
    fixture.componentRef.setInput('rows', rows);
    fixture.detectChanges();
  }

  it('lists one labeled article per document, titled by a paragraph, with its status in words', () => {
    show([
      card({ key: `comparison:4|${ExecutiveSummary}`, audience: ExecutiveSummary, title: 'Executive Summary', statusWord: 'Completed', chipClass: 'job-status-chip status-completed', tone: null }),
      card({ subject: 'comparison:4' })
    ]);

    const list = q('ul.rjd-list')!;
    expect(list.getAttribute('role')).toBe('list');
    expect(list.getAttribute('aria-label')).toBe('Progress of each document');
    const articles = Array.from(host.querySelectorAll<HTMLElement>('ul.rjd-list > li > article.rjd-card'));
    expect(articles.length).toBe(2);
    const article = articles[1];
    const title = q(`#${article.getAttribute('aria-labelledby')}`)!;
    expect(title.tagName).toBe('P');
    expect(title.textContent!.trim()).toBe('Root Cause Analysis');
    expect(host.querySelectorAll('h1, h2, h3, h4, h5, h6').length).toBe(0);
    expect(article.getAttribute('data-audience')).toBe(String(RootCauseAnalysis));
    expect(article.getAttribute('data-subject')).toBe('comparison:4');
    expect(articles[0].hasAttribute('data-subject')).toBe(false);
    // The edge repeats the chip's words; the chip carries the state.
    expect(article.getAttribute('data-tone')).toBe('warning');
    expect(text(`#${article.id} .rjd-status`)).toBe('Status: Completed with warnings');
    expect(articles[0].hasAttribute('data-tone')).toBe(false);
  });

  it('shows the metrics as a description list, and the duration as it changes', () => {
    show([card()]);
    const terms = Array.from(host.querySelectorAll('.rjd-metrics dt')).map(dt => dt.textContent!.trim());
    expect(terms).toEqual(['Duration', 'Model calls', 'Tokens', 'Cost']);
    expect([text('.rjd-duration'), text('.rjd-calls'), text('.rjd-tokens'), text('.rjd-cost')])
      .toEqual(['5 min 04 s', '2', '390,985 in · 11,875 out', '$0.54']);

    show([card({ duration: '5 min 05 s' })]);
    expect(text('.rjd-duration')).toBe('5 min 05 s');
  });

  it('separates the meta line\'s parts with a hidden dot and a visually hidden comma', () => {
    show([card({ meta: ['GPT-6 Luna (high)', 'Comparison #4'] })]);
    const meta = q('.rjd-meta')!;
    expect(Array.from(meta.querySelectorAll('.rjd-meta-part')).map(part => part.textContent!.trim())).toEqual(['GPT-6 Luna (high)', 'Comparison #4']);
    expect(meta.querySelector('.rjd-sep')!.getAttribute('aria-hidden')).toBe('true');
    expect(meta.querySelector('.rjd-sep')!.nextElementSibling!.classList).toContain('visually-hidden');

    show([card()]);
    expect(q('.rjd-meta')).toBeNull();
  });

  it('shows the whole phase line, wrapping, and the error', () => {
    const phase = 'Revised once and shortened — 12 issues in the first draft';
    show([card({ phaseLine: phase, errorMessage: 'The headline is missing.' })]);
    const line = q('.rjd-phase')!;
    expect(line.textContent!.trim()).toBe(phase);
    const style = getComputedStyle(line);
    expect(style.whiteSpace).not.toBe('nowrap');
    expect(style.textOverflow).not.toBe('ellipsis');
    expect(q('.rjd-error')!.classList).toContain('gh-field-error');
    expect(text('.rjd-error')).toBe('The headline is missing.');
  });

  it('wraps the status group under the title rather than overflow, and lets the chip wrap its own words', () => {
    show([card({ why: { subject: 'warnings in the Root Cause Analysis', title: 'Why the Root Cause Analysis was completed with warnings' }, doc: {
      status: 'CompletedWithWarnings', finalIssues: [DIGIT], firstTurnIssues: [DIGIT], firstDraftClean: false
    } })]);
    expect(getComputedStyle(q('.rjd-head')!).flexWrap).toBe('wrap');
    expect(getComputedStyle(q('.rjd-status')!).flexWrap).toBe('wrap');
    expect(getComputedStyle(q('.rjd-status .job-status-chip')!).whiteSpace).toBe('normal');
    expect(getComputedStyle(q('.rjd-list')!).containerType).toBe('inline-size');
  });

  it('explains a warning in a dialog beside the chip', () => {
    show([card({
      why: { subject: 'warnings in the Root Cause Analysis', title: 'Why the Root Cause Analysis was completed with warnings' },
      doc: { status: 'CompletedWithWarnings', finalIssues: [DIGIT], firstTurnIssues: [DIGIT], firstDraftClean: false }
    })]);
    const why = q<HTMLButtonElement>('.rjd-status .rjd-why button.gh-info-btn')!;
    expect(why.getAttribute('aria-label')).toBe('About warnings in the Root Cause Analysis');
    why.click();
    fixture.detectChanges();
    const dialog = q<HTMLDialogElement>('.rjd-why dialog')!;
    expect(dialog.open).toBe(true);
    expect(dialog.querySelector('h3')!.textContent!.trim()).toBe('Why the Root Cause Analysis was completed with warnings');
    expect(dialog.querySelector('.report-validation-summary')!.textContent!.trim()).toBe('This document is usable. One paragraph was removed.');
  });

  it('puts the first draft\'s issues beside the chip while the document is written, beside the phase line once it is done', () => {
    const doc = { status: 'Repairing', firstTurnIssues: [DIGIT], firstDraftClean: false };
    show([card({ doc, draftIssueCount: 1, draftTipBesideChip: true, phaseLine: 'Checking the revision — 1 issue found by the automatic check', phaseDetail: 'One issue.' })]);
    expect(q('.rjd-status .job-status-chip')!.nextElementSibling!.classList).toContain('rjd-draft');
    expect(q('.rjd-phase-row .rjd-draft')).toBeNull();
    expect(q<HTMLButtonElement>('.rjd-draft button.gh-info-btn')!.getAttribute('aria-label'))
      .toBe('About the first draft check of the Root Cause Analysis');

    show([card({ doc: { ...doc, status: 'Completed' }, draftIssueCount: 1, draftTipBesideChip: false, phaseLine: 'Revised once — 1 issue in the first draft' })]);
    expect(q('.rjd-status .rjd-draft')).toBeNull();
    expect(q('.rjd-phase-row .rjd-draft')).not.toBeNull();

    // Without the progress to explain, no tip at all.
    show([card({ doc: null, draftIssueCount: 1, draftTipBesideChip: true })]);
    expect(q('.rjd-draft')).toBeNull();
  });

  describe('an Executive Summary\'s Root Cause Analysis', () => {
    function summary(
      status: string,
      extra: Partial<BenchmarkReportPackDocumentProgressDto> = {},
      overrides: Partial<ReportJobDocumentCard> = {}
    ): ReportJobDocumentCard {
      return card({
        key: `comparison:4|${ExecutiveSummary}`, audience: ExecutiveSummary, title: 'Executive Summary', phrase: 'the Executive Summary',
        statusWord: status, tone: reportJobDocumentTone(status), tipStem: `rjd-test-${ExecutiveSummary}`,
        doc: { status, ...extra }, ...overrides
      });
    }

    function rootCause(status: string, overrides: Partial<ReportJobDocumentCard> = {}): ReportJobDocumentCard {
      return card({ statusWord: status, tone: reportJobDocumentTone(status), doc: { status }, ...overrides });
    }

    const source = (): string => text(`.rjd-card[data-audience="${ExecutiveSummary}"] .rjd-source`);

    it('keeps the cards in the order the job lists them', () => {
      show([rootCause('Writing'), summary('Pending')]);
      expect(Array.from(host.querySelectorAll('.rjd-card')).map(article => article.getAttribute('data-audience')))
        .toEqual([String(RootCauseAnalysis), String(ExecutiveSummary)]);
    });

    it('waits for an unfinished Root Cause Analysis of the same subject', () => {
      show([rootCause('Writing'), summary('Pending')]);
      expect(source()).toBe('Waits for the Root Cause Analysis');
      expect(q(`.rjd-card[data-audience="${RootCauseAnalysis}"] .rjd-source`)).toBeNull();

      // A placeholder card, which carries no progress yet, waits too.
      show([rootCause('Pending', { doc: null }), summary('Pending', {}, { doc: null })]);
      expect(source()).toBe('Waits for the Root Cause Analysis');

      // Nothing to wait for: no Root Cause Analysis in the job, or another subject's.
      show([summary('Pending')]);
      expect(q('.rjd-source')).toBeNull();
      show([rootCause('Writing', { subject: 'run:2' }), summary('Pending', {}, { subject: 'run:1' })]);
      expect(q('.rjd-source')).toBeNull();
    });

    it('says it was written from the Root Cause Analysis above, before the phase line', () => {
      show([rootCause('Completed'), summary('Writing', { rootCauseSource: 'thisJob', rootCauseSourceDocumentId: null }, {
        phaseLine: 'Revised once — 2 issues in the first draft'
      })]);
      expect(source()).toBe('Written from the Root Cause Analysis above');
      const line = q(`.rjd-card[data-audience="${ExecutiveSummary}"] .rjd-source`)!;
      expect(line.classList).not.toContain('gh-field-warning');
      const phase = q(`.rjd-card[data-audience="${ExecutiveSummary}"] .rjd-phase-row`)!;
      expect(line.compareDocumentPosition(phase) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('names a stored Root Cause Analysis by its id', () => {
      show([summary('Completed', { rootCauseSource: 'stored', rootCauseSourceDocumentId: 72 })]);
      expect(source()).toBe('Written from Root Cause Analysis #72');
    });

    it('says why it was written without one, in the warning style', () => {
      show([rootCause('Failed'), summary('CompletedWithWarnings', {
        rootCauseSource: 'none', rootCauseSourceNote: 'the Root Cause Analysis of this job failed'
      })]);
      expect(source()).toBe('Written without a Root Cause Analysis: the Root Cause Analysis of this job failed');
      const line = q(`.rjd-card[data-audience="${ExecutiveSummary}"] .rjd-source`)!;
      expect(line.classList).toContain('gh-field-warning');
      expect(line.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    });

    it('says nothing once started when the server names no source', () => {
      show([rootCause('Completed'), summary('Writing')]);
      expect(q('.rjd-source')).toBeNull();
    });
  });

  it('shows the charts line and its retry only when the document has charts, and emits retry and View', () => {
    const retried: ReportJobDocumentCard[] = [];
    const viewed: ReportJobDocumentCard[] = [];
    fixture.componentInstance.retryCharts.subscribe(row => retried.push(row));
    fixture.componentInstance.view.subscribe(row => viewed.push(row));

    show([card()]);
    expect(q('.rjd-foot')).toBeNull();

    show([card({ charts: { text: 'Charts: 8', retry: false } })]);
    expect(text('.rjd-charts')).toBe('Charts: 8');
    expect(q('.rjd-chart-retry')).toBeNull();

    show([card({ phrase: 'the Root Cause Analysis of GPT Sol', charts: { text: 'Charts failed — retry', retry: true }, viewable: true, documentId: 65 })]);
    const retry = q<HTMLButtonElement>('.rjd-chart-retry')!;
    expect(retry.textContent!.replace(/\s+/g, ' ').trim()).toBe('Charts failed — retry for the Root Cause Analysis of GPT Sol');
    retry.click();
    expect(retried.map(row => row.key)).toEqual([`comparison:4|${RootCauseAnalysis}`]);

    const view = q<HTMLButtonElement>('.rjd-foot .rjd-view')!;
    expect(view.classList).toContain('btn-gh');
    expect(view.classList).toContain('btn-gh-small');
    expect(view.getAttribute('aria-label')).toBe('View the Root Cause Analysis of GPT Sol');
    view.click();
    expect(viewed.map(row => row.documentId)).toEqual([65]);
  });
});
