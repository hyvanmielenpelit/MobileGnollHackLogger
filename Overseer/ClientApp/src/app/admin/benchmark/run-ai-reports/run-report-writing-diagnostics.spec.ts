import {
  BenchmarkReportAudience,
  BenchmarkRunReportDocumentsStatus,
  BenchmarkRunReportJobDto
} from '../../../services/admin-benchmark.service';
import {
  ClientPollState,
  NOT_RECORDED,
  RunIdentity,
  buildRunReportWritingDiagnostics,
  runReportStatusWord,
  runReportWritingDiagnosticsFileName
} from './run-report-writing-diagnostics';

const USER_ID = 'user-5e1f-secret';

function fullView(): BenchmarkRunReportJobDto {
  return {
    runId: 42,
    status: BenchmarkRunReportDocumentsStatus.CompletedWithWarnings,
    message: 'One document has warnings.',
    phase: 'Finished',
    queuedAtUtc: '2026-09-29T12:00:00Z',
    slotAcquiredAtUtc: '2026-09-29T12:00:12Z',
    finishedAtUtc: '2026-09-29T12:03:17Z',
    cancelRequestedAtUtc: '2026-09-29T12:03:00Z',
    jobsAhead: 1,
    blockingJobLabel: 'Report Pack: GPT-6 Sol',
    audiences: [BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.TechnicalReport],
    writerConfigId: 7,
    writerDisplayName: 'Claude Opus writer',
    writerProvider: 'Anthropic',
    writerModelId: 'claude-opus',
    writerThinkingLevel: 'high',
    serverTimeUtc: '2026-09-29T12:03:20Z',
    job: {
      id: 'job-7',
      packId: 'pack-7',
      subjectKey: 'run:42',
      subjectLabel: 'GPT-6 Sol',
      suiteId: 5,
      suiteName: 'Core Mechanics',
      writerConfigId: 7,
      writerDisplayName: 'Claude Opus writer',
      startedByUserId: USER_ID,
      startedAtUtc: '2026-09-29T12:00:12Z',
      completedAtUtc: '2026-09-29T12:03:17Z',
      status: 'CompletedWithErrors',
      totalModelCalls: 3,
      inputTokens: 21000,
      outputTokens: 4500,
      costUsd: 0.42,
      documents: [
        {
          audience: BenchmarkReportAudience.ExecutiveSummary,
          status: 'Completed',
          documentId: 17,
          errorMessage: null,
          modelCalls: 1,
          startedAtUtc: '2026-09-29T12:00:15Z',
          completedAtUtc: '2026-09-29T12:01:00Z',
          inputTokens: 9000,
          outputTokens: 1500,
          costUsd: 0.12
        },
        {
          audience: BenchmarkReportAudience.TechnicalReport,
          status: 'CompletedWithWarnings',
          documentId: 18,
          errorMessage: 'Repaired a missing section.',
          modelCalls: 2,
          startedAtUtc: '2026-09-29T12:01:00Z',
          completedAtUtc: '2026-09-29T12:03:17Z',
          inputTokens: 12000,
          outputTokens: 3000,
          costUsd: 0.3
        }
      ],
      log: [
        { timestampUtc: '2026-09-29T12:00:00Z', severity: 'Info', message: 'Queued.' },
        { timestampUtc: '2026-09-29T12:02:00Z', severity: 'Warning', message: 'The first draft missed a section; repairing.' }
      ]
    }
  };
}

const RUN: RunIdentity = {
  runId: 42,
  suiteName: 'Core Mechanics',
  candidateLabel: 'GPT-6 Sol',
  provider: 'OpenAI',
  modelId: 'gpt-6-sol'
};

const CLIENT: ClientPollState = {
  pollCount: 12,
  lastSuccessUtc: new Date('2026-09-29T12:03:18Z'),
  consecutiveFailures: 1,
  lastError: { httpStatus: 502, message: 'Bad Gateway' }
};

const NOW = new Date('2026-09-29T12:03:25Z');

const EXPECTED_FULL = [
  'Overseer AI report writing diagnostics',
  'Captured: 2026-09-29 12:03:25 UTC',
  '',
  '== Run ==',
  'Run: #42',
  'Suite: Core Mechanics',
  'Candidate: GPT-6 Sol',
  'Provider / model: OpenAI / gpt-6-sol',
  '',
  '== Report status ==',
  'Status: Completed with warnings',
  'Message: One document has warnings.',
  'Source: the job known to this server process',
  '',
  '== Job ==',
  'Job id: job-7',
  'Pack id: pack-7',
  'Phase: Finished',
  'Job status: CompletedWithErrors',
  'Requested documents: Executive Summary; Report for AI Researchers and Developers',
  '',
  '== Writer ==',
  'Writer: Claude Opus writer',
  'Provider / model: Anthropic / claude-opus',
  'Thinking level: high',
  'Config id: 7',
  '',
  '== Timing ==',
  'Queued: 2026-09-29 12:00:00 UTC',
  'Slot acquired: 2026-09-29 12:00:12 UTC',
  'Finished: 2026-09-29 12:03:17 UTC',
  'Cancel requested: 2026-09-29 12:03:00 UTC',
  'Server time at the last response: 2026-09-29 12:03:20 UTC',
  'Queue wait: 12 s',
  'Writing time: 3 min 05 s',
  'Total: 3 min 17 s',
  '',
  '== Queue ==',
  'Jobs ahead: 1',
  'Blocking job: Report Pack: GPT-6 Sol',
  '',
  '== Documents ==',
  'Document 1: Executive Summary',
  '  Status: Completed',
  '  Started: 2026-09-29 12:00:15 UTC',
  '  Completed: 2026-09-29 12:01:00 UTC',
  '  Duration: 45 s',
  '  Model calls: 1',
  '  Input tokens: 9,000',
  '  Output tokens: 1,500',
  '  Cost: $0.12',
  '  Document id: 17',
  '  Error: not recorded',
  'Document 2: Report for AI Researchers and Developers',
  '  Status: CompletedWithWarnings',
  '  Started: 2026-09-29 12:01:00 UTC',
  '  Completed: 2026-09-29 12:03:17 UTC',
  '  Duration: 2 min 17 s',
  '  Model calls: 2',
  '  Input tokens: 12,000',
  '  Output tokens: 3,000',
  '  Cost: $0.30',
  '  Document id: 18',
  '  Error: Repaired a missing section.',
  '',
  '== Totals ==',
  'Model calls: 3',
  'Input tokens: 21,000',
  'Output tokens: 4,500',
  'Cost: $0.42',
  'Estimate: $0.50',
  '',
  '== Client polling ==',
  'Polls: 12',
  'Last success: 2026-09-29 12:03:18 UTC',
  'Consecutive failures: 1',
  'Last error: HTTP 502: Bad Gateway',
  '',
  '== Job log ==',
  '[2026-09-29 12:00:00 UTC] [Info] Queued.',
  '[2026-09-29 12:02:00 UTC] [Warning] The first draft missed a section; repairing.',
  ''
].join('\n');

describe('buildRunReportWritingDiagnostics', () => {
  it('pins a fully populated example', () => {
    expect(buildRunReportWritingDiagnostics(fullView(), RUN, CLIENT, NOW, 0.5)).toBe(EXPECTED_FULL);
  });

  it('keeps the sections in order', () => {
    const text = buildRunReportWritingDiagnostics(fullView(), RUN, CLIENT, NOW, 0.5);
    const sections = ['Overseer AI report writing diagnostics', '== Run ==', '== Report status ==', '== Job ==',
      '== Writer ==', '== Timing ==', '== Queue ==', '== Documents ==', '== Totals ==', '== Client polling ==',
      '== Job log =='];
    const positions = sections.map(section => text.indexOf(section));
    expect(positions.every(position => position >= 0)).toBeTrue();
    expect([...positions].sort((a, b) => a - b)).toEqual(positions);
  });

  it('uses LF line endings only', () => {
    const view = fullView();
    view.job.log[0].message = 'Line one\r\nline two';
    const text = buildRunReportWritingDiagnostics(view, RUN, CLIENT, NOW);
    expect(text).not.toContain('\r');
    expect(text).toContain('[2026-09-29 12:00:00 UTC] [Info] Line one line two');
  });

  it('never includes the user who started the job', () => {
    const text = buildRunReportWritingDiagnostics(fullView(), RUN, CLIENT, NOW, 0.5);
    expect(text).not.toContain(USER_ID);
    expect(text).not.toContain('startedByUserId');
    expect(text.toLowerCase()).not.toContain('user id');
  });

  it('reads every absent fact as "not recorded"', () => {
    const client: ClientPollState = { pollCount: 1, lastSuccessUtc: null, consecutiveFailures: 0, lastError: null };
    const text = buildRunReportWritingDiagnostics(null, { runId: 5 }, client, NOW, null);
    const lines = text.split('\n');
    for (const expected of [
      'Run: #5',
      `Suite: ${NOT_RECORDED}`,
      `Candidate: ${NOT_RECORDED}`,
      `Provider / model: ${NOT_RECORDED}`,
      `Status: ${NOT_RECORDED}`,
      `Message: ${NOT_RECORDED}`,
      `Job id: ${NOT_RECORDED}`,
      `Pack id: ${NOT_RECORDED}`,
      `Phase: ${NOT_RECORDED}`,
      `Requested documents: ${NOT_RECORDED}`,
      `Writer: ${NOT_RECORDED}`,
      `Thinking level: ${NOT_RECORDED}`,
      `Queued: ${NOT_RECORDED}`,
      `Finished: ${NOT_RECORDED}`,
      `Queue wait: ${NOT_RECORDED}`,
      `Writing time: ${NOT_RECORDED}`,
      `Total: ${NOT_RECORDED}`,
      `Jobs ahead: ${NOT_RECORDED}`,
      `Blocking job: ${NOT_RECORDED}`,
      `Documents: ${NOT_RECORDED}`,
      `Cost: ${NOT_RECORDED}`,
      `Last success: ${NOT_RECORDED}`,
      `Last error: ${NOT_RECORDED}`,
      `Log: ${NOT_RECORDED}`
    ]) {
      expect(lines).toContain(expected);
    }
    expect(text).not.toContain('Estimate:');
    expect(text).not.toContain('null');
    expect(text).not.toContain('undefined');
  });

  it('reads the null fields of a running job as "not recorded" and open spans as "so far"', () => {
    const view = fullView();
    view.phase = 'Writing';
    view.status = BenchmarkRunReportDocumentsStatus.Writing;
    view.message = null;
    view.finishedAtUtc = null;
    view.cancelRequestedAtUtc = null;
    view.jobsAhead = null;
    view.blockingJobLabel = null;
    view.writerThinkingLevel = null;
    view.job.costUsd = null;
    view.job.documents[1].completedAtUtc = null;
    view.job.documents[1].errorMessage = null;
    const text = buildRunReportWritingDiagnostics(view, RUN, CLIENT, NOW);
    const lines = text.split('\n');
    expect(lines).toContain(`Message: ${NOT_RECORDED}`);
    expect(lines).toContain(`Finished: ${NOT_RECORDED}`);
    expect(lines).toContain(`Cancel requested: ${NOT_RECORDED}`);
    expect(lines).toContain(`Thinking level: ${NOT_RECORDED}`);
    expect(lines).toContain(`Jobs ahead: ${NOT_RECORDED}`);
    expect(lines).toContain(`Blocking job: ${NOT_RECORDED}`);
    expect(lines).toContain('Writing time: 3 min 08 s so far');
    expect(lines).toContain('Total: 3 min 20 s so far');
    expect(lines).toContain('  Duration: 2 min 20 s so far');
    expect(lines).toContain(`Cost: ${NOT_RECORDED}`);
    expect(text).not.toContain('null');
  });

  it('takes the report status from the stored run when no job is known', () => {
    const run: RunIdentity = {
      ...RUN,
      reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Failed,
      reportDocumentsMessage: 'The writer refused.'
    };
    const text = buildRunReportWritingDiagnostics(null, run, CLIENT, NOW);
    expect(text).toContain('Status: Failed\nMessage: The writer refused.\n');
    expect(text).toContain('Source: the stored run (no job known to this server process)');
  });

  it('describes a network failure without an HTTP status', () => {
    const client: ClientPollState = { ...CLIENT, lastError: { httpStatus: null, message: 'Offline' } };
    expect(buildRunReportWritingDiagnostics(null, RUN, client, NOW)).toContain('Last error: HTTP none (network): Offline');
  });
});

describe('runReportStatusWord', () => {
  it('names every status as a word', () => {
    expect(runReportStatusWord(BenchmarkRunReportDocumentsStatus.NotRequested)).toBe('Not requested');
    expect(runReportStatusWord(BenchmarkRunReportDocumentsStatus.CompletedWithWarnings)).toBe('Completed with warnings');
    expect(runReportStatusWord(BenchmarkRunReportDocumentsStatus.Canceled)).toBe('Canceled');
    expect(runReportStatusWord(null)).toBe(NOT_RECORDED);
  });
});

describe('runReportWritingDiagnosticsFileName', () => {
  it('stamps the file name in UTC', () => {
    expect(runReportWritingDiagnosticsFileName(42, new Date('2026-09-29T07:04:05Z')))
      .toBe('run-42_ai-report-writing-diagnostics_20260929-070405.txt');
  });
});
