import { BenchmarkReportAudience, BenchmarkRunReportDocumentsStatus } from '../../../../services/admin-benchmark.service';
import { ccReportJob } from '../chat-consistency-tab.testing';
import {
  CcReportDiagnosticsContext,
  ccReportAudienceLabel,
  ccReportDiagnosticsFileName,
  ccReportJobDiagnostics,
  ccReportJobRows,
  ccReportJobStages,
  ccReportJobStats,
  ccReportJobSummary
} from './cc-report-job-view';

/** The server's clock of {@link ccReportJob}'s last response. */
const NOW = new Date('2026-10-02T10:01:30Z');

/** {@link ccReportJob}, finished at 10:01:17 with the given document statuses. */
function finishedJob(jobStatus: string, documentStatuses: string[]) {
  const running = ccReportJob();
  return ccReportJob(
    { phase: 'Finished', status: BenchmarkRunReportDocumentsStatus.Completed, finishedAtUtc: '2026-10-02T10:01:17Z' },
    {
      status: jobStatus,
      completedAtUtc: '2026-10-02T10:01:17Z',
      documents: running.job.documents.map((doc, i) => ({ ...doc, status: documentStatuses[i] ?? doc.status }))
    }
  );
}

function context(overrides: Partial<CcReportDiagnosticsContext> = {}): CcReportDiagnosticsContext {
  return {
    analysisId: 7,
    analysisName: 'September check',
    subject: { displayName: 'GPT-5 high', provider: 'OpenAI', modelId: 'gpt-5', thinkingLevel: 'high' },
    chartState: 'idle',
    chartMessage: '',
    client: { pollCount: 4, lastSuccessUtc: NOW, consecutiveFailures: 0, lastError: null },
    estimateUsd: 0.12,
    ...overrides
  };
}

describe('cc-report-job-view', () => {
  it('names a document as the Reports step does', () => {
    expect(ccReportAudienceLabel(BenchmarkReportAudience.InternalBrief)).toBe('Internal Brief');
    expect(ccReportAudienceLabel(BenchmarkReportAudience.ProviderIssueReport)).toBe('Provider Issue Report');
  });

  describe('ccReportJobStages', () => {
    const states = (job = ccReportJob()) => ccReportJobStages(job).map(stage => `${stage.name}:${stage.state}`);

    it('rails Queued, Preparing, one stage per document and Done, current at the document being written', () => {
      expect(states()).toEqual([
        'Queued:done',
        'Preparing:done',
        'Executive Summary:done',
        'Report for AI Researchers and Developers:current',
        'Internal Brief:pending',
        'Done:pending'
      ]);
    });

    it('stands at Queued, then Preparing, by the job\'s phase', () => {
      expect(states(ccReportJob({ phase: 'Queued' }))[0]).toBe('Queued:current');
      expect(states(ccReportJob({ phase: 'Preparing' })).slice(0, 3)).toEqual(['Queued:done', 'Preparing:current', 'Executive Summary:pending']);
    });

    it('stands at the next pending document between two, and at Done once finished', () => {
      const between = ccReportJob({}, {
        documents: ccReportJob().job.documents.map((doc, i) => (i === 1 ? { ...doc, status: 'Completed' } : doc))
      });
      expect(states(between)[4]).toBe('Internal Brief:current');
      const done = states(finishedJob('Completed', ['Completed', 'Completed', 'Completed']));
      expect(done[done.length - 1]).toBe('Done:current');
      expect(done.slice(0, -1).every(state => state.endsWith(':done'))).toBe(true);
    });
  });

  describe('ccReportJobStats', () => {
    it('measures from the writer\'s slot to now while running, with the tokens and the cost so far', () => {
      expect(ccReportJobStats(ccReportJob(), NOW)).toEqual({
        elapsed: '1 min 25 s',
        modelCalls: '3',
        tokens: '42,000 in · 9,000 out',
        totalTokens: 51_000,
        cost: '$0.21',
        costLabel: 'Cost so far'
      });
    });

    it('measures from the queueing while no slot is held, and to the end once finished', () => {
      expect(ccReportJobStats(ccReportJob({ phase: 'Queued', slotAcquiredAtUtc: null }), NOW).elapsed).toBe('1 min 30 s');
      const finished = ccReportJobStats(finishedJob('Completed', []), NOW);
      expect(finished.elapsed).toBe('1 min 12 s');
      expect(finished.costLabel).toBe('Cost');
    });

    it('reads an unknown cost as Unknown', () => {
      expect(ccReportJobStats(ccReportJob({}, { costUsd: null }), NOW).cost).toBe('Unknown');
    });
  });

  describe('ccReportJobRows', () => {
    it('gives each document its chip, its duration to now while written, its model calls and its id', () => {
      const rows = ccReportJobRows(ccReportJob(), NOW);
      expect(rows.map(row => [row.name, row.statusWord, row.chipClass, row.duration, row.modelCalls, row.documentId])).toEqual([
        ['Executive Summary', 'Completed', 'job-status-chip status-completed', '1 min 00 s', '2', 501],
        ['Report for AI Researchers and Developers', 'Writing', 'job-status-chip status-generating', '25 s', '1', null],
        ['Internal Brief', 'Pending', 'job-status-chip status-pending', '—', '0', null]
      ]);
    });

    it('ends an unfinished document\'s duration at the job\'s end, and carries its error', () => {
      const job = finishedJob('CompletedWithErrors', ['Completed', 'Failed', 'Canceled']);
      job.job.documents[1].errorMessage = 'The writer failed validation twice.';
      const rows = ccReportJobRows(job, NOW);
      expect(rows[1].duration).toBe('12 s');
      expect(rows[1].errorMessage).toBe('The writer failed validation twice.');
      expect(rows[0].errorMessage).toBeNull();
    });
  });

  describe('ccReportJobSummary', () => {
    it('sums up a finished job in one line', () => {
      expect(ccReportJobSummary(finishedJob('Completed', ['Completed', 'Completed', 'Completed']), NOW))
        .toBe('3 documents written · $0.21 · 1 min 12 s');
      expect(ccReportJobSummary(finishedJob('CompletedWithErrors', ['Completed', 'Failed', 'Completed']), NOW))
        .toBe('2 of 3 documents written, with errors · $0.21 · 1 min 12 s');
      expect(ccReportJobSummary(finishedJob('Canceled', ['Completed', 'Canceled', 'Canceled']), NOW))
        .toBe('Canceled: 1 of 3 documents written · $0.21 · 1 min 12 s');
      expect(ccReportJobSummary(finishedJob('Failed', ['Failed', 'Pending', 'Pending']), NOW))
        .toBe('Failed: 0 of 3 documents written · $0.21 · 1 min 12 s');
    });

    it('leaves out an unknown cost, and words a job without documents by its status', () => {
      const unpriced = finishedJob('Completed', ['Completed', 'Completed', 'Completed']);
      unpriced.job.costUsd = null;
      expect(ccReportJobSummary(unpriced, NOW)).toBe('3 documents written · 1 min 12 s');
      const empty = ccReportJob(
        { phase: 'Finished', status: BenchmarkRunReportDocumentsStatus.Failed, message: 'The writer is disabled.', finishedAtUtc: '2026-10-02T10:00:05Z' },
        { status: 'Failed', documents: [] }
      );
      expect(ccReportJobSummary(empty, NOW)).toBe('Failed: The writer is disabled. · $0.21 · 0 s');
    });
  });

  describe('diagnostics', () => {
    it('names the file chat-consistency-reports_<analysis>_diagnostics_<UTC stamp>.txt', () => {
      expect(ccReportDiagnosticsFileName(7, NOW)).toBe('chat-consistency-reports_7_diagnostics_20261002-100130.txt');
      expect(ccReportDiagnosticsFileName(null, NOW)).toBe('chat-consistency-reports_unsaved_diagnostics_20261002-100130.txt');
    });

    it('writes the analysis, the job, every document with its charts, the polling and the log, one fact per LF line', () => {
      const text = ccReportJobDiagnostics(ccReportJob({}, { startedByUserId: 'user-secret-17' }), NOW, context({ chartState: 'done' }));
      const lines = text.split('\n');

      expect(text).not.toContain('\r');
      expect(text.endsWith('\n')).toBe(true);
      expect(text).not.toContain('user-secret-17');
      expect(lines[0]).toBe('Overseer chat consistency report writing diagnostics');
      expect(lines).toContain('Captured: 2026-10-02 10:01:30 UTC');
      expect(lines).toContain('Analysis: #7');
      expect(lines).toContain('Name: September check');
      expect(lines).toContain('Provider / model: OpenAI / gpt-5');
      expect(lines).toContain('Status: Writing');
      expect(lines).toContain('Phase: Writing');
      expect(lines).toContain('Requested documents: Executive Summary; Report for AI Researchers and Developers; Internal Brief');
      expect(lines).toContain('Writer: Claude writer');
      expect(lines).toContain('Provider / model: Anthropic / claude-30');
      expect(lines).toContain('Queue wait: 5 s');
      expect(lines).toContain('Writing time: 1 min 25 s so far');
      expect(lines).toContain('Document 2: Report for AI Researchers and Developers');
      expect(lines).toContain('  Duration: 25 s so far');
      expect(lines.filter(line => line.startsWith('  Charts: '))).toEqual(['  Charts: attached', '  Charts: none (not written)', '  Charts: none (not written)']);
      expect(lines).toContain('Estimate: $0.12');
      expect(lines).toContain('Polls: 4');
      expect(lines).toContain('[2026-10-02 10:00:05 UTC] [Info] Writing Executive Summary.');
    });

    it('reads every absent fact as not recorded when no job is known', () => {
      const text = ccReportJobDiagnostics(null, NOW, context({ estimateUsd: null }));
      expect(text).toContain('Job id: not recorded');
      expect(text).toContain('Documents: not recorded');
      expect(text).toContain('Log: not recorded');
      expect(text).not.toContain('Estimate:');
    });
  });
});
