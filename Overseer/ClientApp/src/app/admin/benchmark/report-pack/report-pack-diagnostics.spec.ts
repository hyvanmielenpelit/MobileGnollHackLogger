import {
  BenchmarkReportAudience,
  BenchmarkReportPackJobDto
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

const { ExecutiveSummary, TechnicalReport, InternalBrief } = BenchmarkReportAudience;

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
        audience: TechnicalReport, status: 'Writing', documentId: null, errorMessage: null, modelCalls: 2,
        startedAtUtc: '2026-09-28T10:00:50', completedAtUtc: null
      },
      { audience: InternalBrief, status: 'Failed', documentId: 23, errorMessage: 'The writer\nrefused.', modelCalls: 1 }
    ],
    log: [
      { timestampUtc: '2026-09-28T10:00:00Z', message: 'Started.', severity: 'Info' },
      { timestampUtc: '2026-09-28T10:00:50Z', message: 'Repaired once.', severity: 'Warning' }
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
    estimateUsd: 0.12,
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
      '== Comparison ==', '== Job ==', '== Charts ==', '== Documents ==', '== Totals ==', '== Client polling ==', '== Job log =='
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
    expect(lines).toContain('Document 2: Report for AI Researchers and Developers');
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
    expect(lines).toContain('[2026-09-28 10:00:50 UTC] [Warning] Repaired once.');
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
