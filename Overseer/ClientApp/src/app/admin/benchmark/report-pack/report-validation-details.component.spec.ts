import { ComponentFixture, TestBed } from '@angular/core/testing';

import {
  BenchmarkReportPackCallDto,
  BenchmarkReportPackValidationIssueDto
} from '../../../services/admin-benchmark.service';
import { ReportValidationDetailsComponent } from './report-validation-details.component';
import {
  DocumentStatusInput,
  documentStatusQuestion,
  documentStatusSubject,
  explainDocumentStatus,
  explanationLines,
  groupIssuesByRule,
  issueCountSummary,
  severityWord,
  tokenSegments
} from './report-validation-explanation';

function issue(
  rule: number, ruleName: string, severity: string, locationLabel: string, message: string, dropped: boolean, text: string | null = null
): BenchmarkReportPackValidationIssueDto {
  return { rule, ruleName, severity, location: 'sections.conclusions[0]', locationLabel, message, dropped, text };
}

function call(kind: string): BenchmarkReportPackCallDto {
  return {
    kind, startedAtUtc: '2026-10-15T10:00:00Z', durationMs: 30000, inputTokens: 1000, cachedInputTokens: 0, outputTokens: 500,
    costUsd: 0.01, finishReason: 'stop', requestMessages: 2, requestCharacters: 4000
  };
}

const RANKING = (n: number) => issue(16, 'Ranking without the overlap caveat', 'warning', `Hypotheses: cost, paragraph ${n}`,
  'The sentence ranks two models whose intervals overlap.', false);
const DIGIT = issue(3, 'Digit in prose', 'removesItem', 'Conclusions, paragraph 1', 'The paragraph holds a number of its own.', true,
  'The model scored 72 on {{model.index}} questions.');

const FIRST_DRAFT = [RANKING(1), RANKING(2), RANKING(3), DIGIT];

const CLEAN: DocumentStatusInput = {
  status: 'Completed', errorMessage: null, failedReply: null, firstTurnIssues: [], finalIssues: [], firstDraftClean: true, calls: [call('Draft')]
};

const WARNINGS: DocumentStatusInput = {
  status: 'CompletedWithWarnings',
  errorMessage: null,
  failedReply: null,
  firstTurnIssues: FIRST_DRAFT,
  finalIssues: [DIGIT, { ...RANKING(2), text: 'Model A is faster than model B.' }],
  firstDraftClean: false,
  calls: [call('Draft'), call('Revision')]
};

const SHORTENED_SPEED = issue(7, 'Over the word or item limit', 'removesItem', 'Hypotheses: speed, paragraph 2',
  'Removed its last 2 sentences to bring "Hypotheses: speed" within 120 words.', true, 'It ran slower. It also cost more.');
const SHORTENED_CONCLUSIONS = issue(7, 'Over the word or item limit', 'removesItem', 'Conclusions, paragraph 3',
  'Removed its last sentence to bring "Conclusions" within 120 words.', true, 'The cost follows.');
const ADDED: BenchmarkReportPackValidationIssueDto = {
  ...issue(36, 'Conclusions leave an axis out', 'addedByOverseer', 'Conclusions',
    'The writer\'s paragraph on cost was removed; Overseer wrote the axis\'s status in its place.', false, 'Cost: {{rca.axis.cost.status}}'),
  addedByOverseer: true
};

const SHORTENED: DocumentStatusInput = {
  status: 'CompletedWithWarnings',
  firstTurnIssues: FIRST_DRAFT,
  finalIssues: [SHORTENED_SPEED, SHORTENED_CONCLUSIONS, ADDED],
  firstDraftClean: false,
  calls: [call('Draft'), call('Revision'), call('Shorten')]
};

const FAILED: DocumentStatusInput = {
  status: 'Failed',
  errorMessage: 'The headline is missing.',
  failedReply: `{"headline": ""${' '.repeat(600)}}`,
  firstTurnIssues: [issue(1, 'Missing or unexpected section', 'failsDocument', 'Headline', 'The headline is empty.', false)],
  finalIssues: [issue(1, 'Missing or unexpected section', 'failsDocument', 'Headline', 'The headline is empty.', false)],
  firstDraftClean: false,
  calls: [call('Draft'), call('Revision')]
};

describe('explainDocumentStatus', () => {
  it('says a clean document passed every check, with nothing to do', () => {
    const ex = explainDocumentStatus(CLEAN);
    expect(ex.summary).toBe('This document is usable. It passed every check.');
    expect(ex.checks).toBe('First draft passed every check. Final: no issues.');
    expect(ex.removed).toEqual([]);
    expect(ex.kept).toEqual([]);
    expect(ex.actions).toEqual([]);
  });

  it('counts what was removed and kept, and words the first draft by rule, the most frequent first', () => {
    const ex = explainDocumentStatus(WARNINGS);
    expect(ex.summary).toBe('This document is usable. One paragraph was removed and one warning was kept.');
    expect(ex.checks).toBe('First draft: 4 issues (Ranking without the overlap caveat: 3; Digit in prose: 1). One revision. Final: 1 removal, 1 warning.');
    expect(ex.removed.map(item => [item.location, item.ruleName, item.text])).toEqual([
      ['Conclusions, paragraph 1', 'Digit in prose', 'The model scored 72 on {{model.index}} questions.']
    ]);
    expect(ex.kept.map(item => [item.location, item.ruleName])).toEqual([['Hypotheses: cost, paragraph 2', 'Ranking without the overlap caveat']]);
    expect(ex.actions.some(action => action.includes('Rewrite'))).toBe(true);
    expect(ex.actions.some(action => action.includes('diagnostics'))).toBe(true);
    // A stored document has no diagnostics to copy.
    expect(explainDocumentStatus(WARNINGS, 'stored').actions.some(action => action.includes('diagnostics'))).toBe(false);
  });

  it('says a failed document could not be stored, with the error and the first 500 characters of the reply', () => {
    const ex = explainDocumentStatus(FAILED);
    expect(ex.summary).toBe('This document could not be stored.');
    expect(ex.blocking.map(item => item.ruleName)).toEqual(['Missing or unexpected section']);
    expect(ex.kept).toEqual([]);
    expect(ex.error).toBe('The headline is missing.');
    expect(ex.failedReplyExcerpt!.length).toBe(501);
    expect(ex.failedReplyExcerpt!.endsWith('…')).toBe(true);
    expect(ex.checks).toContain('Final: 1 issue that stops the document.');
  });

  it('falls back to the rule number and the raw location from a server that sends no names', () => {
    const ex = explainDocumentStatus({
      status: 'CompletedWithWarnings', finalIssues: [{ rule: 12, location: 'sections.abstract', message: 'British spelling.', dropped: false }]
    }, 'stored');
    expect(ex.kept.map(item => [item.ruleName, item.location])).toEqual([['Rule 12', 'sections.abstract']]);
    expect(ex.summary).toBe('This document is usable. One warning was kept.');
    expect(ex.checks).toBe('Final: 1 warning.');
  });

  it('says what was shortened and what Overseer wrote in place of a removed paragraph, and names the shortening turn', () => {
    const ex = explainDocumentStatus(SHORTENED);
    expect(ex.summary).toBe('This document is usable. Two paragraphs were shortened, and Overseer wrote one paragraph in place of a removed one.');
    expect(ex.checks).toBe(
      'First draft: 4 issues (Ranking without the overlap caveat: 3; Digit in prose: 1). One revision and one shortening turn. '
      + 'Final: 3 sentences removed, 1 paragraph added by Overseer.');
    expect(ex.removed.map(item => item.shortened)).toEqual([true, true]);
    expect(ex.added.map(item => [item.location, item.text])).toEqual([['Conclusions', 'Cost: {{rca.axis.cost.status}}']]);
    expect(ex.kept).toEqual([]);
    expect(ex.blocking).toEqual([]);

    const lines = explanationLines(ex);
    const added = lines.indexOf('  What Overseer added:');
    expect(added).toBeGreaterThan(lines.indexOf('  What was removed:'));
    expect(lines[added + 2]).toBe('      Added text: Cost: {{rca.axis.cost.status}}');
  });

  it('counts the sentences a salvage removed as one shortened paragraph, not as removals', () => {
    const sentence = (text: string): BenchmarkReportPackValidationIssueDto => ({
      ...issue(33, 'Cause without a hypothesis', 'removesItem', 'Hypotheses: speed, paragraph 1',
        'Uses a causal word without a hypothesis. Only this sentence was removed, so the section kept its paragraph.', true, text),
      sentenceOnly: true
    });
    const ex = explainDocumentStatus({
      status: 'CompletedWithWarnings',
      firstTurnIssues: [issue(1, 'Missing or unexpected section', 'removesItem', 'Reply', 'The reply is not valid JSON.', false)],
      finalIssues: [sentence('Every grade is Moderate because each model ran once.'), sentence('The gap is due to length.')],
      firstDraftClean: false,
      calls: [call('Draft'), call('Revision'), call('Revision')]
    });

    expect(ex.summary).toBe('This document is usable. One paragraph was shortened.');
    expect(ex.checks).toContain('Final: 2 sentences removed.');
    expect(ex.checks).not.toContain('removal');
    expect(ex.removed.map(item => [item.shortened, item.text])).toEqual([
      [true, 'Every grade is Moderate because each model ran once.'],
      [true, 'The gap is due to length.']
    ]);
  });

  it('keeps a note of a removing rule whose text was kept as a warning, as the server marks it', () => {
    const kept = issue(7, 'Over the word or item limit', 'warningTextKept', 'Conclusions',
      'Kept over its cap at 131 words: every remaining sentence states an axis\'s status.', false);
    const ex = explainDocumentStatus({ status: 'CompletedWithWarnings', finalIssues: [kept] }, 'stored');
    expect(ex.kept.map(item => item.severity)).toEqual(['warningTextKept']);
    expect(ex.summary).toBe('This document is usable. One warning was kept.');
    expect(severityWord('warningTextKept')).toBe('warning, text kept');
    expect(severityWord('addedByOverseer')).toBe('added by Overseer');
    expect(severityWord('warning')).toBe('warning');
  });

  it('splits a text around its fact tokens', () => {
    expect(tokenSegments('Continues {{rca.check.1}} after its full stop.')).toEqual([
      { text: 'Continues ', token: false },
      { text: '{{rca.check.1}}', token: true },
      { text: ' after its full stop.', token: false }
    ]);
    expect(tokenSegments('{{a}}{{b}}')).toEqual([{ text: '{{a}}', token: true }, { text: '{{b}}', token: true }]);
    expect(tokenSegments('')).toEqual([]);
    expect(tokenSegments(null)).toEqual([]);
  });

  it('groups issues by rule and names the dialog and its button by the status', () => {
    expect(groupIssuesByRule(FIRST_DRAFT).map(group => [group.rule, group.count])).toEqual([[16, 3], [3, 1]]);
    expect(issueCountSummary([])).toBe('0 issues');
    expect(documentStatusQuestion('the Executive Summary', 'CompletedWithWarnings')).toBe('Why the Executive Summary was completed with warnings');
    expect(documentStatusQuestion('the Executive Summary', 'Failed')).toBe('Why the Executive Summary failed');
    expect(documentStatusSubject('the Executive Summary', 'CompletedWithWarnings')).toBe('warnings in the Executive Summary');
    expect(documentStatusSubject('the Executive Summary', 'Failed')).toBe('failure of the Executive Summary');
  });

  it('prints the same explanation as plain lines, each removed text indented under its item', () => {
    const lines = explanationLines(explainDocumentStatus(WARNINGS));
    expect(lines[0]).toBe('  This document is usable. One paragraph was removed and one warning was kept.');
    const removed = lines.indexOf('  What was removed:');
    expect(lines[removed + 1]).toBe('  - Conclusions, paragraph 1: Digit in prose. The paragraph holds a number of its own.');
    expect(lines[removed + 2]).toBe('      Removed text: The model scored 72 on {{model.index}} questions.');
    expect(lines).toContain('  What was kept with a warning:');
    expect(lines.some(line => line.startsWith('  How the draft was checked: First draft: 4 issues'))).toBe(true);
  });
});

describe('ReportValidationDetailsComponent', () => {
  let fixture: ComponentFixture<ReportValidationDetailsComponent>;
  let host: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ReportValidationDetailsComponent] }).compileComponents();
    fixture = TestBed.createComponent(ReportValidationDetailsComponent);
    host = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => fixture.destroy());

  const text = (selector: string): string => (host.querySelector(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const headings = (): string[] => Array.from(host.querySelectorAll('h4')).map(h => h.textContent!.trim());

  function show(document: DocumentStatusInput, extra: Record<string, unknown> = {}): void {
    fixture.componentRef.setInput('document', document);
    for (const [key, value] of Object.entries(extra)) {
      fixture.componentRef.setInput(key, value);
    }
    fixture.detectChanges();
  }

  it('shows a completed document with a clean first draft', () => {
    show(CLEAN);
    expect(text('.report-validation-summary')).toBe('This document is usable. It passed every check.');
    expect(headings()).toEqual(['How the draft was checked']);
    expect(text('.report-validation-checks p')).toBe('First draft passed every check. Final: no issues.');
    expect(host.querySelector('details')).toBeNull();
  });

  it('shows what was removed, with the removed text in a disclosure, and what was kept with a warning', () => {
    show(WARNINGS);
    expect(text('.report-validation-summary')).toBe('This document is usable. One paragraph was removed and one warning was kept.');
    expect(headings()).toEqual(['What was removed', 'What was kept with a warning', 'How the draft was checked', 'What you can do']);
    const removed = host.querySelector('.report-validation-removed .report-validation-item')!;
    expect(removed.querySelector('.report-validation-location')!.textContent).toBe('Conclusions, paragraph 1');
    expect(removed.querySelector('.report-validation-rule')!.textContent).toBe('Digit in prose');
    expect(removed.querySelector('.report-validation-meaning')!.textContent).toBe('The paragraph holds a number of its own.');
    const disclosure = removed.querySelector<HTMLDetailsElement>('details.report-validation-text')!;
    expect(disclosure.open).toBe(false);
    expect(disclosure.querySelector('summary')!.textContent!.replace(/\s+/g, ' ').trim()).toBe('Removed text at Conclusions, paragraph 1');
    expect(disclosure.querySelector('.report-validation-quote')!.textContent).toBe('The model scored 72 on {{model.index}} questions.');
    expect(text('.report-validation-kept .report-validation-rule')).toBe('Ranking without the overlap caveat');
    expect(text('.report-validation-kept summary')).toContain('Flagged text');
    expect(text('.report-validation-checks p')).toContain('First draft: 4 issues (Ranking without the overlap caveat: 3; Digit in prose: 1). One revision.');
  });

  it('lists what Overseer added before what was kept, marks a shortened paragraph, and shows fact names as code', () => {
    const keptOverCap = issue(7, 'Over the word or item limit', 'warningTextKept', 'Conclusions',
      'Kept over its cap at 131 words: every remaining sentence states an axis\'s status.', false);
    show({ ...SHORTENED, finalIssues: [...SHORTENED.finalIssues!, keptOverCap] });

    expect(headings()).toEqual(['What was removed', 'What Overseer added', 'What was kept with a warning', 'How the draft was checked', 'What you can do']);
    expect(text('.report-validation-summary')).toBe(
      'This document is usable. Two paragraphs were shortened and one warning was kept, and Overseer wrote one paragraph in place of a removed one.');
    expect(Array.from(host.querySelectorAll('.report-validation-removed .report-validation-severity')).map(el => el.textContent!.trim()))
      .toEqual(['shortened', 'shortened']);
    expect(text('.report-validation-added .report-validation-severity')).toBe('added by Overseer');
    expect(text('.report-validation-added summary')).toBe('Added text at Conclusions');
    expect(text('.report-validation-kept .report-validation-severity')).toBe('warning, text kept');

    // A token is a code element, never markup parsed from the text; the quote reads as before.
    const quote = host.querySelector('.report-validation-added .report-validation-quote')!;
    expect(quote.textContent).toBe('Cost: {{rca.axis.cost.status}}');
    expect(Array.from(quote.querySelectorAll('code.report-validation-token')).map(code => code.textContent)).toEqual(['{{rca.axis.cost.status}}']);
  });

  it('shows a failed document with its error and the start of the writer\'s reply', () => {
    show(FAILED);
    expect(text('.report-validation-summary')).toBe('This document could not be stored.');
    expect(headings()).toEqual(['What stopped the document', 'How the draft was checked', 'What you can do']);
    expect(text('.report-validation-error')).toBe('The error: The headline is missing.');
    expect(host.querySelector('.report-validation-reply')!.textContent!.length).toBe(501);
    expect(text('.report-validation-actions')).toContain('check Write on its row');
  });

  it('lists the first draft\'s issues by rule in the first-draft view, led by the phase detail', () => {
    show({ ...WARNINGS, status: 'Repairing', finalIssues: null }, { view: 'firstDraft', phaseDetail: 'The automatic check found 4 issues in the first draft.' });
    expect(text('.report-validation-summary')).toBe('The automatic check found 4 issues in the first draft.');
    const groups = Array.from(host.querySelectorAll('.report-validation-group'));
    expect(groups.map(group => group.getAttribute('data-rule'))).toEqual(['16', '3']);
    expect(groups[0].querySelector('.report-validation-count')!.textContent!.trim()).toBe('3 issues');
    expect(groups[0].querySelectorAll('li').length).toBe(3);
    expect(headings()).toEqual([]);
  });
});
