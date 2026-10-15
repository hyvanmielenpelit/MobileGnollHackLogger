import {
  BenchmarkReportAudience,
  BenchmarkReportPackCallDto,
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkReportPackJobDto,
  BenchmarkReportPackValidationIssueDto,
  BenchmarkReportScope
} from '../../../services/admin-benchmark.service';
import type { BenchmarkModelComparisonEntryDto } from '../model-comparison/model-comparison.models';
import { DEFAULT_CHART_SELECTION } from './report-charts';
import {
  ReportPackDiagnosticsInput,
  buildReportPackDiagnostics,
  reportPackChartStatusWord,
  reportPackDiagnosticsFileName
} from './report-pack-diagnostics';
import type { ReportPackContext } from './report-pack-panel.component';
import { explainDocumentStatus, explanationLines } from './report-validation-explanation';

const { ExecutiveSummary, RootCauseAnalysis } = BenchmarkReportAudience;

const CONTEXT: ReportPackContext = {
  runIds: [1, 2],
  groupIds: [4],
  pricingBasis: 'Current',
  entries: [
    { key: 'run:1', label: 'Gemini Flash' },
    { key: 'run:2', label: 'Claude Opus' },
    { key: 'group:4', label: 'GPT Sol group' }
  ] as BenchmarkModelComparisonEntryDto[],
  entryKeys: ['run:1', 'run:2', 'group:4'],
  suiteId: 5,
  suiteName: 'Board Suite'
};

function job(overrides: Partial<BenchmarkReportPackJobDto> = {}): BenchmarkReportPackJobDto {
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
    totalModelCalls: 3,
    inputTokens: 12000,
    outputTokens: 2500,
    costUsd: 0.08,
    documents: [
      {
        audience: ExecutiveSummary, status: 'Completed', documentId: 21, errorMessage: null, modelCalls: 1,
        startedAtUtc: '2026-09-28T10:00:05Z', completedAtUtc: '2026-09-28T10:00:50Z', inputTokens: 6000, outputTokens: 1500, costUsd: 0.05
      },
      {
        audience: RootCauseAnalysis, status: 'Writing', documentId: null, errorMessage: null, modelCalls: 2,
        startedAtUtc: '2026-09-28T10:00:50', completedAtUtc: null
      },
      { audience: RootCauseAnalysis, status: 'Failed', documentId: 23, errorMessage: 'The writer\nrefused.', modelCalls: 1 }
    ],
    log: [
      { timestampUtc: '2026-09-28T10:00:00Z', message: 'Started.', severity: 'info' },
      { timestampUtc: '2026-09-28T10:00:50Z', message: 'Repaired once.', severity: 'warning' }
    ],
    serverTimeUtc: '2026-09-28T10:01:10Z',
    ...overrides
  };
}

function input(overrides: Partial<ReportPackDiagnosticsInput> = {}): ReportPackDiagnosticsInput {
  return {
    context: CONTEXT,
    job: job(),
    subjectKey: 'run:1',
    chartSelection: DEFAULT_CHART_SELECTION,
    chartStatus: { 21: { state: 'done', count: 4, images: 4 }, 23: { state: 'failed', message: 'Disk full.' } },
    chartStorageMissing: false,
    chartAdvisory: null,
    client: {
      pollCount: 7,
      lastSuccessUtc: new Date('2026-09-28T10:01:10Z'),
      consecutiveFailures: 0,
      lastError: { httpStatus: 503, message: 'Service Unavailable' }
    },
    estimateUsd: { low: 0.12, high: null },
    nowUtc: new Date('2026-09-28T10:01:12Z'),
    ...overrides
  };
}

describe('report-pack-diagnostics', () => {
  it('lists the comparison, the job, each document with its charts, the totals, the polling and the log, with LF endings', () => {
    const text = buildReportPackDiagnostics(input());

    expect(text).not.toContain('\r');
    expect(text.endsWith('\n')).toBe(true);
    const lines = text.split('\n');
    expect(lines[0]).toBe('Overseer Report Pack diagnostics');
    expect(lines).toContain('Captured: 2026-09-28 10:01:12 UTC');
    const sections = lines.filter(line => line.startsWith('== '));
    expect(sections).toEqual([
      '== Comparison ==', '== Job ==', '== Charts ==', '== Documents ==', '== Why document 3 (Root Cause Analysis) failed ==',
      '== Totals ==', '== Client polling ==', '== Job log =='
    ]);

    expect(lines).toContain('Suite: Board Suite');
    expect(lines).toContain('Entries: 3 (run:1, run:2, group:4)');
    expect(lines).toContain('Runs: 1, 2');
    expect(lines).toContain('Groups: 4');
    expect(lines).toContain('Pricing basis: Current');
    expect(lines).toContain('Subject: Gemini Flash (run:1)');

    expect(lines).toContain('Job id: job-1');
    expect(lines).toContain('Writer config id: 7');
    expect(lines).toContain('Server time at the last response: 2026-09-28 10:01:10 UTC');
    expect(lines).toContain('Elapsed: 1 min 10 s so far');
    expect(lines).toContain('Chart storage: configured');

    expect(lines).toContain('Document 1: Executive Summary');
    expect(lines).toContain('  Duration: 45 s');
    expect(lines).toContain('  Cost: $0.05');
    expect(lines).toContain('  Charts selected: Intelligence; Intelligence against cost');
    expect(lines).toContain('  Charts: 4 attached');
    expect(lines).toContain('Document 2: Root Cause Analysis');
    // An offset-less server time reads as UTC; an unfinished document runs to the server's clock.
    expect(lines).toContain('  Duration: 20 s so far');
    expect(lines).toContain('  Charts: not recorded');
    expect(lines).toContain('  Error: The writer refused.');
    expect(lines).toContain('  Charts: failed: Disk full.');

    expect(lines).toContain('Model calls: 3');
    expect(lines).toContain('Input tokens: 12,000');
    expect(lines).toContain('Estimate: $0.12');
    expect(lines).toContain('Polls: 7');
    expect(lines).toContain('Last error: HTTP 503: Service Unavailable');
    expect(lines).toContain('[2026-09-28 10:00:50 UTC] [warning] Repaired once.');
  });

  it('adds the job\'s context, writer and versions, the preparation notes, each call, the rule names and the removed text', () => {
    const ranking: BenchmarkReportPackValidationIssueDto = {
      rule: 16, ruleName: 'Ranking without the overlap caveat', severity: 'warning', location: 'sections.hypothesesCost[1]',
      locationLabel: 'Hypotheses: cost, paragraph 2', message: 'The sentence ranks two overlapping models.', dropped: false, text: null
    };
    const digit: BenchmarkReportPackValidationIssueDto = {
      rule: 3, ruleName: 'Digit in prose', severity: 'removesItem', location: 'sections.conclusions[0]',
      locationLabel: 'Conclusions, paragraph 1', message: 'The paragraph holds a number of its own.', dropped: true,
      text: 'The model scored 72\non {{model.index}}.'
    };
    const call = (kind: string, startedAtUtc: string, costUsd: number | null): BenchmarkReportPackCallDto => ({
      kind, startedAtUtc, durationMs: 32000, inputTokens: 6000, cachedInputTokens: 1000, outputTokens: 1500, costUsd,
      finishReason: kind === 'Draft' ? 'stop' : null, requestMessages: kind === 'Draft' ? 2 : 4, requestCharacters: 20000
    });
    const warned: BenchmarkReportPackDocumentProgressDto = {
      audience: ExecutiveSummary, status: 'CompletedWithWarnings', documentId: 21, errorMessage: null, modelCalls: 2,
      phase: 'Done', phaseDetail: null, firstDraftClean: false, firstTurnIssues: [ranking, digit], finalIssues: [digit, ranking],
      calls: [call('Draft', '2026-09-28T10:00:05Z', 0.05), call('Revision', '2026-09-28T10:00:40Z', null)]
    };
    const battery = job({
      scope: BenchmarkReportScope.Comparison,
      comparisonId: 12,
      suiteId: null,
      referenceModel: 'GPT-6 Sol (medium)',
      entries: ['battery:13', 'battery:14'],
      writerProvider: 'Anthropic',
      writerModelId: 'claude-opus-5-5',
      writerThinkingLevel: 'medium',
      reportFormatVersion: 14,
      engineVersion: 3,
      pdfLayoutVersion: 5,
      wordLayoutVersion: 2,
      writerPromptSha256: 'ab12cd34',
      preparationNotes: [{ ...ranking, rule: 90, ruleName: 'Question detail left out', location: 'facts', locationLabel: 'Fact sheet', message: 'Six questions were summarized.' }],
      status: 'Completed',
      documents: [warned]
    });
    const text = buildReportPackDiagnostics(input({
      context: { ...CONTEXT, runIds: [], groupIds: [], batteryRunIds: [13, 14], suiteId: null, entryKeys: ['battery:13', 'battery:14'] },
      job: battery
    }));
    const lines = text.split('\n');

    expect(lines).toContain('Scope: Comparison');
    expect(lines).toContain('Comparison id: 12');
    expect(lines).toContain('Reference model: GPT-6 Sol (medium)');
    expect(lines).toContain('Suite id: not applicable (battery comparison)');
    expect(lines).toContain('Entries: 2 (battery:13, battery:14)');
    expect(lines).toContain('Battery runs: 13, 14');
    expect(lines).toContain('Writer provider: Anthropic');
    expect(lines).toContain('Writer model id: claude-opus-5-5');
    expect(lines).toContain('Writer thinking level: medium');
    expect(lines).toContain('Report format version: 14');
    expect(lines).toContain('Engine version: 3');
    expect(lines).toContain('PDF layout version: 5');
    expect(lines).toContain('Word layout version: 2');
    expect(lines).toContain('Writer prompt SHA-256: ab12cd34');

    expect(lines.filter(line => line.startsWith('== '))).toEqual([
      '== Comparison ==', '== Job ==', '== Charts ==', '== Preparation notes ==', '== Documents ==', '== Validation ==',
      '== Why document 1 (Executive Summary) was completed with warnings ==', '== Totals ==', '== Client polling ==', '== Job log =='
    ]);
    expect(lines).toContain('rule 90 (Question detail left out, warning) at Fact sheet: Six questions were summarized.');

    expect(lines).toContain('  Phase: Done (Done)');
    expect(lines).toContain('  First draft clean: no');
    expect(lines).toContain('    Call 1: Draft, started 2026-09-28 10:00:05 UTC, 32 s, 6,000 in / 1,000 cached / 1,500 out tokens, $0.05, finish stop, 2 messages, 20,000 characters');
    expect(lines).toContain('    Call 2: Revision, started 2026-09-28 10:00:40 UTC, 32 s, 6,000 in / 1,000 cached / 1,500 out tokens, not recorded, finish not recorded, 4 messages, 20,000 characters');

    const final = lines.indexOf('  Final: 2 issues (rule 3: 1, rule 16: 1)');
    expect(lines.slice(final + 1, final + 4)).toEqual([
      '    rule 3 (Digit in prose, removes the item) at Conclusions, paragraph 1: The paragraph holds a number of its own. (dropped)',
      '      Removed text: The model scored 72 on {{model.index}}.',
      '    rule 16 (Ranking without the overlap caveat, warning) at Hypotheses: cost, paragraph 2: The sentence ranks two overlapping models.'
    ]);

    // The same explanation as the dialog.
    const why = lines.indexOf('== Why document 1 (Executive Summary) was completed with warnings ==');
    const expected = explanationLines(explainDocumentStatus(warned, 'job'), '');
    expect(lines.slice(why + 1, why + 1 + expected.length)).toEqual(expected);
    expect(expected[0]).toBe('This document is usable. One paragraph was removed and one warning was kept.');
  });

  it('reads the suite id as not recorded for a comparison that is not of battery results, and the preparation notes as none', () => {
    const lines = buildReportPackDiagnostics(input({ job: job({ suiteId: null, preparationNotes: [] }), context: { ...CONTEXT, suiteId: null } })).split('\n');
    expect(lines).toContain('Suite id: not recorded');
    expect(lines).toContain('Preparation notes: none');
    expect(lines).toContain('Engine version: not recorded');
    expect(lines).toContain('  First draft clean: not checked');
    // A server that sends no phases writes no phase line.
    expect(lines.some(line => line.startsWith('  Phase:'))).toBe(false);
  });

  it('prints the estimate as a range from the first call to one repair turn per document, when both ends are known and differ', () => {
    const lines = (estimateUsd: ReportPackDiagnosticsInput['estimateUsd']) => buildReportPackDiagnostics(input({ estimateUsd })).split('\n');
    expect(lines({ low: 1.04, high: 2.1 })).toContain('Estimate: $1.04 to $2.10 (first call only, to one repair turn per document)');
    expect(lines({ low: 1.04, high: null })).toContain('Estimate: $1.04');
    expect(lines({ low: 1.04, high: 1.0401 })).toContain('Estimate: $1.04');
  });

  it('adds the validation issues and the failed writer replies after the documents, only when a document carries them', () => {
    const issue = (rule: number, location: string, message: string, dropped = false) => ({ rule, location, message, dropped });
    const finished = job({
      status: 'CompletedWithErrors',
      documents: [
        {
          audience: ExecutiveSummary, status: 'Completed', documentId: 21, errorMessage: null, modelCalls: 2,
          firstTurnIssues: [issue(16, 'sections.abstract', 'A token-shaped name.'), issue(2, 'headline', 'Too long.'),
            issue(16, 'weaknesses[1]', 'A token-shaped name.', true)],
          finalIssues: [issue(16, 'weaknesses[1]', 'A token-shaped name.', true)],
          failedReply: null
        },
        {
          audience: RootCauseAnalysis, status: 'Failed', documentId: null, errorMessage: 'Still invalid.', modelCalls: 3,
          firstTurnIssues: [], finalIssues: [issue(7, 'headline', 'Missing.')],
          failedReply: '{"headline":\r\n""}'
        }
      ]
    });
    const lines = buildReportPackDiagnostics(input({ job: finished })).split('\n');

    expect(lines.filter(line => line.startsWith('== '))).toEqual([
      '== Comparison ==', '== Job ==', '== Charts ==', '== Documents ==', '== Validation ==', '== Failed writer replies ==',
      '== Why document 2 (Root Cause Analysis) failed ==', '== Totals ==', '== Client polling ==', '== Job log =='
    ]);
    const validation = lines.slice(lines.indexOf('== Validation =='), lines.indexOf('== Failed writer replies =='));
    expect(validation).toEqual([
      '== Validation ==',
      'Document 1: Executive Summary',
      '  First turn: 3 issues (rule 2: 1, rule 16: 2)',
      '    rule 16 at sections.abstract: A token-shaped name.',
      '    rule 2 at headline: Too long.',
      '    rule 16 at weaknesses[1]: A token-shaped name. (dropped)',
      '  Final: 1 issue (rule 16: 1)',
      '    rule 16 at weaknesses[1]: A token-shaped name. (dropped)',
      'Document 2: Root Cause Analysis',
      '  First turn: 0 issues',
      '  Final: 1 issue (rule 7: 1)',
      '    rule 7 at headline: Missing.',
      ''
    ]);
    const replies = lines.slice(lines.indexOf('== Failed writer replies =='), lines.indexOf('== Why document 2 (Root Cause Analysis) failed =='));
    expect(replies).toEqual([
      '== Failed writer replies ==', 'Document 2: Root Cause Analysis', '--- reply ---', '{"headline":', '""}', '--- end of reply ---', ''
    ]);

    // A running job's rows carry neither, so neither section is written.
    const running = buildReportPackDiagnostics(input());
    expect(running).not.toContain('== Validation ==');
    expect(running).not.toContain('== Failed writer replies ==');
  });

  it('never includes the user who started the job', () => {
    const text = buildReportPackDiagnostics(input());
    expect(text).not.toContain('user-secret-id');
    expect(text.toLowerCase()).not.toContain('started by');
  });

  it('reads "not recorded" for every absent fact', () => {
    const text = buildReportPackDiagnostics(input({
      context: null,
      job: null,
      subjectKey: null,
      estimateUsd: null,
      client: { pollCount: 0, lastSuccessUtc: null, consecutiveFailures: 0, lastError: null }
    }));
    const lines = text.split('\n');
    expect(lines).toContain('Suite: not recorded');
    expect(lines).toContain('Entries: not recorded');
    expect(lines).toContain('Subject: not recorded (not recorded)');
    expect(lines).toContain('Job id: not recorded');
    expect(lines).toContain('Elapsed: not recorded');
    expect(lines).toContain('Documents: not recorded');
    expect(lines).toContain('Cost: not recorded');
    expect(lines).toContain('Last success: not recorded');
    expect(lines).toContain('Last error: not recorded');
    expect(lines).toContain('Log: not recorded');
    expect(text).not.toContain('Estimate:');
  });

  it('words each document\'s chart state, none while chart storage is missing or no chart is selected', () => {
    const doc = job().documents[0];
    const base = { chartSelection: DEFAULT_CHART_SELECTION, chartStatus: {}, chartStorageMissing: false };
    expect(reportPackChartStatusWord(doc, { ...base, chartStatus: { 21: { state: 'attaching' } } })).toBe('attaching');
    expect(reportPackChartStatusWord(doc, { ...base, chartStatus: { 21: { state: 'done', count: 3, images: 3 } } })).toBe('3 attached');
    expect(reportPackChartStatusWord(doc, { ...base, chartStatus: { 21: { state: 'done', count: 3, images: 6 } } }))
      .toBe('3 attached (6 images, named and anonymized)');
    expect(reportPackChartStatusWord(doc, { ...base, chartStatus: { 21: { state: 'skipped', reason: 'no peers' } } })).toBe('none (no peers)');
    expect(reportPackChartStatusWord(doc, { ...base, chartStorageMissing: true })).toBe('none (chart storage is not configured)');
    expect(reportPackChartStatusWord(doc, { ...base, chartSelection: { [ExecutiveSummary]: [] } }))
      .toBe('none (no chart selected for this document type)');
    expect(buildReportPackDiagnostics(input({ chartStorageMissing: true }))).toContain('Chart storage: not configured');
  });

  it('names the file for the subject and the capture time in UTC', () => {
    const now = new Date('2026-09-29T12:00:05Z');
    expect(reportPackDiagnosticsFileName('Gemini Flash', now)).toBe('report-pack_gemini-flash_diagnostics_20260929-120005.txt');
    expect(reportPackDiagnosticsFileName('GPT/Sol: "group"', now)).toBe('report-pack_gptsol-group_diagnostics_20260929-120005.txt');
    expect(reportPackDiagnosticsFileName(null, now)).toBe('report-pack_no-subject_diagnostics_20260929-120005.txt');
  });
});
