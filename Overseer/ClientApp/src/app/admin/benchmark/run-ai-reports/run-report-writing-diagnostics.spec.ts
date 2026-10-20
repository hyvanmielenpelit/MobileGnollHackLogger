import {
  BenchmarkReportAudience,
  BenchmarkReportPackCallDto,
  BenchmarkReportPackValidationIssueDto,
  BenchmarkRunReportDocumentsStatus,
  BenchmarkRunReportJobDto
} from '../../../services/admin-benchmark.service';
import { documentStatusQuestion, explainDocumentStatus, explanationLines } from '../report-pack/report-validation-explanation';
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
    audiences: [BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.RootCauseAnalysis],
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
          audience: BenchmarkReportAudience.RootCauseAnalysis,
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
  'Requested documents: Executive Summary; Root Cause Analysis',
  '',
  '== Writer ==',
  'Writer: Claude Opus writer',
  'Provider / model: Anthropic / claude-opus',
  'Thinking level: high',
  'Config id: 7',
  '',
  '== Versions ==',
  'Writer output limit: not recorded',
  'Report format version: not recorded',
  'Engine version: not recorded',
  'PDF layout version: not recorded',
  'Word layout version: not recorded',
  'Writer prompt SHA-256: not recorded',
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
  '  First draft clean: not checked',
  '  Started: 2026-09-29 12:00:15 UTC',
  '  Completed: 2026-09-29 12:01:00 UTC',
  '  Duration: 45 s',
  '  Model calls: 1',
  '  Input tokens: 9,000',
  '  Output tokens: 1,500',
  '  Cost: $0.12',
  '  Document id: 17',
  '  Error: not recorded',
  'Document 2: Root Cause Analysis',
  '  Status: CompletedWithWarnings',
  '  First draft clean: not checked',
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
  `== ${documentStatusQuestion('document 2 (Root Cause Analysis)', 'CompletedWithWarnings')} ==`,
  ...explanationLines(explainDocumentStatus(fullView().job.documents[1], 'job'), ''),
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

  it('carries the Report Pack diagnostics\' sections: versions, preparation, calls, word counts, validation, failed replies, why', () => {
    const issue = (rule: number, location: string, extra: Partial<BenchmarkReportPackValidationIssueDto> = {}): BenchmarkReportPackValidationIssueDto => ({
      rule, location, locationLabel: location, message: `Rule ${rule} message.`, dropped: false, ...extra
    });
    const call = (kind: string, replyChars: number | null): BenchmarkReportPackCallDto => ({
      kind, startedAtUtc: '2026-09-29T12:01:00Z', durationMs: 32000, inputTokens: 12000, cachedInputTokens: 1000,
      outputTokens: 30000, reasoningTokens: 900, costUsd: 0.2, finishReason: 'stop', requestMessages: 2,
      requestCharacters: 40000, replyChars
    });
    const view = fullView();
    Object.assign(view.job, {
      writerMaxOutputTokens: 32000, reportFormatVersion: 25, engineVersion: 9, pdfLayoutVersion: 4, wordLayoutVersion: 3,
      writerPromptSha256: 'abc123',
      preparationNotes: [issue(40, 'Fact sheet', { severity: 'warning', ruleName: 'Few controls' })]
    });
    Object.assign(view.job.documents[0], {
      status: 'Failed', errorMessage: 'The writer gave up.', failedReply: 'first line\nsecond line'
    });
    Object.assign(view.job.documents[1], {
      phase: 'Done',
      phaseDetail: 'Stored with warnings.',
      firstDraftClean: false,
      calls: [call('Draft', 22113), call('Revision', null)],
      firstTurnIssues: [
        issue(3, 'Conclusions, paragraph 1', { dropped: true, text: 'The model scored 72.', ruleName: 'Digit in prose', severity: 'removesItem' }),
        issue(16, 'Key finding')
      ],
      revisionIssues: [issue(16, 'Key finding'), issue(7, 'Hypotheses: cost')],
      finalIssues: [issue(7, 'Hypotheses: cost'), issue(31, 'Summary', { addedByOverseer: true, text: 'Overseer wrote this.' })],
      wordCounts: [{ location: 'sections.abstract', locationLabel: 'Abstract', cap: 120, draft: 140, revision: 118, shortened: null, final: 118, finalParagraphs: [60, 58] }]
    });

    const text = buildRunReportWritingDiagnostics(view, RUN, CLIENT, NOW, 0.5);
    const lines = text.split('\n');
    const headers = [
      '== Run ==', '== Report status ==', '== Job ==', '== Writer ==', '== Versions ==', '== Timing ==', '== Queue ==',
      '== Preparation notes ==', '== Documents ==', '== Word counts ==', '== Validation ==', '== Failed writer replies ==',
      `== ${documentStatusQuestion('document 1 (Executive Summary)', 'Failed')} ==`,
      `== ${documentStatusQuestion('document 2 (Root Cause Analysis)', 'CompletedWithWarnings')} ==`,
      '== Totals ==', '== Client polling ==', '== Job log =='
    ];
    const positions = headers.map(header => lines.indexOf(header));
    expect(positions.every(position => position >= 0)).toBe(true);
    expect([...positions].sort((a, b) => a - b)).toEqual(positions);
    expect(text).not.toContain('== Charts ==');

    expect(lines).toContain('Writer output limit: 32,000 tokens (reasoning included)');
    expect(lines).toContain('Report format version: 25');
    expect(lines).toContain('Engine version: 9');
    expect(lines).toContain('PDF layout version: 4');
    expect(lines).toContain('Word layout version: 3');
    expect(lines).toContain('Writer prompt SHA-256: abc123');
    expect(text).toMatch(/^rule 40 \(Few controls, .+\) at Fact sheet: Rule 40 message\.$/m);

    expect(text).toMatch(/^ {2}Phase: .+ \(Done\)$/m);
    expect(lines).toContain('  Phase detail: Stored with warnings.');
    expect(lines).toContain('  First draft clean: no');
    const calls = lines.filter(line => line.startsWith('    Call '));
    expect(calls.length).toBe(2);
    expect(calls[0]).toContain('12,000 in / 1,000 cached / 30,000 out tokens (900 reasoning), output 94% of the limit (near the limit)');
    expect(calls[0]).toContain('finish stop, 2 messages, 40,000 characters, reply 22,113 characters');
    expect(calls[1]).toContain(', no reply');
    expect(lines).toContain('  Writer replies: 1 (22,113 characters), downloadable separately as Markdown '
      + '(Log and diagnostics → Download the writer\'s replies) while Overseer still holds this job.');

    expect(lines).toContain('  Abstract — cap 120 — draft 140, revision 118, final 118 (raw words; a token counts as one) — paragraphs 60, 58');

    expect(lines).toContain('  First turn: 2 issues (rule 3: 1, rule 16: 1)');
    expect(text).toMatch(/^ {4}rule 3 \(Digit in prose, .+\) at Conclusions, paragraph 1: Rule 3 message\. \(dropped\)$/m);
    expect(lines).toContain('      Removed text: The model scored 72.');
    expect(lines).toContain('  Revision: 2 issues (rule 7: 1, rule 16: 1)');
    expect(lines).toContain('  Fixed by the revision: 1 (rule 3: 1)');
    expect(lines).toContain('  Still there: 1 (rule 16 at Key finding)');
    expect(lines).toContain('  New in the revision: 1 (rule 7 at Hypotheses: cost)');
    expect(lines).toContain('  Final: 2 issues (rule 7: 1, rule 31: 1)');
    expect(lines).toContain('  Added by Overseer: Summary — Overseer wrote this.');

    const reply = lines.indexOf('--- reply ---');
    expect(lines.slice(reply - 1, reply + 4)).toEqual(['Document 1: Executive Summary', '--- reply ---', 'first line', 'second line', '--- end of reply ---']);
    expect(text).not.toContain('\r');
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
