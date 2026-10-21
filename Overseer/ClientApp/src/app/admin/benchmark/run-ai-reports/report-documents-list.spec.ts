import {
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportDocumentOrigin,
  BenchmarkRootCauseReadinessDto,
  BenchmarkRunReportDocumentsStatus,
  BenchmarkRunReportJobDto
} from '../../../services/admin-benchmark.service';
import {
  BATTERY_COMPLETION_ORIGIN,
  CHAT_CONSISTENCY_DOCUMENT_AUDIENCES,
  CHAT_CONSISTENCY_REPORT_ORIGIN,
  ExecutiveSummaryAvailability,
  ExecutiveSummaryAvailabilityInput,
  REPORT_DOCUMENT_AUDIENCES,
  ReportRootCauseReadiness,
  completionDocumentsOf,
  executiveSummaryAvailability,
  executiveSummaryFixLabel,
  isReportWriterSameProviderWarning,
  missingReportAudiences,
  reportDateUtc,
  reportDocumentByline,
  reportDocumentDisclosures,
  reportDocumentMeta,
  reportDocumentRows,
  reportDocumentTag,
  reportDocumentsStatusOf,
  reportEstimateView,
  reportJobInProgress,
  reportJobStatusKind,
  reportJobStatusText,
  reportRootCauseSourceLine,
  reportServerErrorText,
  reportStatusSnapshotOfJob,
  reportWriteOrderHint,
  rootCauseRewriteOffered,
  writtenReportLabels
} from './report-documents-list';

const { ExecutiveSummary, RootCauseAnalysis, ProviderIssueReport } = BenchmarkReportAudience;
const { NotRequested, Pending, Writing, Completed, Failed, Skipped, Canceled } = BenchmarkRunReportDocumentsStatus;

function doc(id: number, audience: BenchmarkReportAudience, overrides: Partial<BenchmarkReportDocumentListItemDto> = {}): BenchmarkReportDocumentListItemDto {
  return {
    id, packId: 'p', audience, title: `Document ${id}`, subjectKey: 'battery:7', subjectLabel: 'GPT Sol',
    subjectRunIds: [101, 102], suiteId: null, suiteName: '', writerDisplayName: 'Claude Opus writer',
    writerProvider: 'Anthropic', writerModelId: 'claude-opus', writerThinkingLevel: null,
    sameProviderAcknowledged: false, status: 'Completed', reportFormatVersion: 2,
    createdAtUtc: '2026-10-03T09:30:00Z', inputTokens: 0, outputTokens: 0, durationMs: 0, costUsd: null,
    runChangedSinceGeneration: false, missingRunIds: [], allowedDisclosures: [1, 2, 3],
    origin: BATTERY_COMPLETION_ORIGIN as BenchmarkReportDocumentOrigin,
    ...overrides
  };
}

describe('report-documents-list', () => {
  it('lists the Root Cause Analysis, then the Executive Summary written from it', () => {
    expect(REPORT_DOCUMENT_AUDIENCES).toEqual([RootCauseAnalysis, ExecutiveSummary]);
    expect(BATTERY_COMPLETION_ORIGIN).toBe(3);
  });

  it('says the write order only while both documents are checked', () => {
    expect(reportWriteOrderHint([RootCauseAnalysis, ExecutiveSummary]))
      .toBe('Written in this order: the Root Cause Analysis first, then the Executive Summary, which is written from it.');
    // The Executive Summary alone says what it is written from on its own reason line.
    expect(reportWriteOrderHint([ExecutiveSummary])).toBe('');
    expect(reportWriteOrderHint([RootCauseAnalysis])).toBe('');
    expect(reportWriteOrderHint([])).toBe('');
  });

  it('names the Root Cause Analysis a stored Executive Summary was written from, and says when a later one exists', () => {
    const summary = doc(71, ExecutiveSummary, { rootCauseSourceDocumentId: 72 });
    expect(reportRootCauseSourceLine(summary, [summary, doc(72, RootCauseAnalysis)])).toBe('Written from Root Cause Analysis #72');
    // The source deleted, with no later one listed.
    expect(reportRootCauseSourceLine(summary, [summary])).toBe('Written from Root Cause Analysis #72, which no longer exists');
    // Only an earlier one listed: the source is gone too.
    expect(reportRootCauseSourceLine(summary, [summary, doc(69, RootCauseAnalysis)]))
      .toBe('Written from Root Cause Analysis #72, which no longer exists');

    const stale = doc(71, ExecutiveSummary, { rootCauseSourceDocumentId: 70 });
    expect(reportRootCauseSourceLine(stale, [stale, doc(72, RootCauseAnalysis)]))
      .toBe('Written from an earlier Root Cause Analysis (#70); rewrite it to include the newest');
    // Another subject's Root Cause Analysis is not this one's newer analysis.
    expect(reportRootCauseSourceLine(stale, [stale, doc(70, RootCauseAnalysis), doc(72, RootCauseAnalysis, { subjectKey: 'battery:8' })]))
      .toBe('Written from Root Cause Analysis #70');

    expect(reportRootCauseSourceLine(doc(71, ExecutiveSummary, { rootCauseSourceDocumentId: null }), [doc(72, RootCauseAnalysis)])).toBe('');
    expect(reportRootCauseSourceLine(doc(71, ExecutiveSummary), [doc(72, RootCauseAnalysis)])).toBe('');
    expect(reportRootCauseSourceLine(doc(72, RootCauseAnalysis, { rootCauseSourceDocumentId: 70 }), [])).toBe('');
  });

  it('reads a status from its number or its name, and anything else as NotRequested', () => {
    expect(reportDocumentsStatusOf(Writing)).toBe(Writing);
    expect(reportDocumentsStatusOf('Failed')).toBe(Failed);
    expect(reportDocumentsStatusOf('Nonsense')).toBe(NotRequested);
    expect(reportDocumentsStatusOf(undefined)).toBe(NotRequested);
    expect(reportJobInProgress(Pending)).toBe(true);
    expect(reportJobInProgress(Writing)).toBe(true);
    expect(reportJobInProgress(Completed)).toBe(false);
  });

  it('keeps one subject\'s completion documents of one origin, the newest per audience, in list order', () => {
    const documents = [
      doc(1, RootCauseAnalysis, { createdAtUtc: '2026-10-01T00:00:00Z' }),
      doc(2, RootCauseAnalysis, { createdAtUtc: '2026-10-02T00:00:00Z' }),
      doc(3, ExecutiveSummary),
      // A document of a retired type is never listed.
      doc(9, 3 as BenchmarkReportAudience),
      // A Report Pack document about the same battery run, and a completion document of another one.
      doc(4, ExecutiveSummary, { origin: BenchmarkReportDocumentOrigin.ReportPack, createdAtUtc: '2026-10-04T00:00:00Z' }),
      doc(5, ExecutiveSummary, { subjectKey: 'battery:8', createdAtUtc: '2026-10-04T00:00:00Z' })
    ];
    expect(completionDocumentsOf(documents, 'battery:7', BATTERY_COMPLETION_ORIGIN).map(d => d.id)).toEqual([2, 3]);
    expect(completionDocumentsOf(null, 'battery:7', BATTERY_COMPLETION_ORIGIN)).toEqual([]);
    expect(completionDocumentsOf([doc(6, ExecutiveSummary, { subjectKey: 'run:55', origin: BenchmarkReportDocumentOrigin.RunCompletion })],
      'run:55', BenchmarkReportDocumentOrigin.RunCompletion).map(d => d.id)).toEqual([6]);
  });

  it('keeps chat consistency documents out of a run\'s documents, and lists an analysis\'s two with the Provider Issue Report', () => {
    const cc = (id: number, audience: BenchmarkReportAudience, overrides: Partial<BenchmarkReportDocumentListItemDto> = {}) =>
      doc(id, audience, { subjectKey: 'chat-consistency:12', origin: CHAT_CONSISTENCY_REPORT_ORIGIN, ...overrides });
    const documents = [
      cc(21, ProviderIssueReport),
      cc(22, ExecutiveSummary),
      cc(23, ExecutiveSummary, { createdAtUtc: '2026-10-01T09:30:00Z' }),
      cc(24, ExecutiveSummary, { subjectKey: 'chat-consistency:13' }),
      // A chat consistency analysis never lists a Root Cause Analysis or a retired type.
      cc(28, RootCauseAnalysis),
      cc(29, 2 as BenchmarkReportAudience),
      cc(25, RootCauseAnalysis, { subjectKey: 'run:55' }),
      doc(26, RootCauseAnalysis, { subjectKey: 'run:55', origin: BenchmarkReportDocumentOrigin.RunCompletion })
    ];

    expect(CHAT_CONSISTENCY_REPORT_ORIGIN).toBe(BenchmarkReportDocumentOrigin.ChatConsistencyReport);
    expect(CHAT_CONSISTENCY_DOCUMENT_AUDIENCES).toEqual([ExecutiveSummary, ProviderIssueReport]);
    expect(completionDocumentsOf(documents, 'chat-consistency:12', CHAT_CONSISTENCY_REPORT_ORIGIN).map(d => d.id)).toEqual([22, 21]);
    // A run's documents never take one of another origin, even about the same subject key.
    expect(completionDocumentsOf(documents, 'run:55', BenchmarkReportDocumentOrigin.RunCompletion).map(d => d.id)).toEqual([26]);
    // A run's two audiences never include the Provider Issue Report.
    expect(completionDocumentsOf([doc(27, ProviderIssueReport)], 'battery:7', BATTERY_COMPLETION_ORIGIN)).toEqual([]);
  });

  it('builds one row per audience, written or not, with the missing and written ones', () => {
    const documents = [doc(3, ExecutiveSummary)];
    expect(reportDocumentRows(documents)).toEqual([
      { audience: RootCauseAnalysis, label: 'Root Cause Analysis', doc: null },
      { audience: ExecutiveSummary, label: 'Executive Summary', doc: documents[0] }
    ]);
    expect(missingReportAudiences(documents)).toEqual([RootCauseAnalysis]);
    expect(writtenReportLabels(documents)).toEqual(['Executive Summary']);
    expect(missingReportAudiences([])).toEqual([RootCauseAnalysis, ExecutiveSummary]);
  });

  describe('executiveSummaryAvailability', () => {
    const answered = (state: BenchmarkRootCauseReadinessDto['state'], overrides: Partial<BenchmarkRootCauseReadinessDto> = {}): ReportRootCauseReadiness => ({
      kind: 'answered',
      readiness: {
        state,
        rootCauseDocumentId: state === 'missing' ? null : 77,
        message: state === 'usable' ? null : 'The server\'s sentence.',
        code: state === 'usable' ? null : state === 'missing' ? 'rootCauseMissing' : 'rootCauseOutdated',
        ...overrides
      }
    });
    const availabilityOf = (input: Partial<ExecutiveSummaryAvailabilityInput>): ExecutiveSummaryAvailability => executiveSummaryAvailability({
      rootCauseChecked: false, storedRootCauseId: 77, readiness: null, subjectNoun: 'run', ...input
    });

    it('is available, written from the analysis above, while the Root Cause Analysis is checked, whatever is stored', () => {
      const expected = { available: true, reason: 'Written from the Root Cause Analysis above, after it.', fix: null, retry: false };
      expect(availabilityOf({ rootCauseChecked: true, storedRootCauseId: null })).toEqual(expected);
      expect(availabilityOf({ rootCauseChecked: true, readiness: answered('outdated') })).toEqual(expected);
      expect(availabilityOf({ rootCauseChecked: true, readiness: { kind: 'loading' } })).toEqual(expected);
    });

    it('is available from a stored analysis written from the subject\'s current data, named by its id', () => {
      expect(availabilityOf({ readiness: answered('usable') })).toEqual({
        available: true,
        reason: 'Written from Root Cause Analysis #77, which was written from this run\'s current data.',
        fix: null,
        retry: false
      });
      expect(availabilityOf({ readiness: answered('usable'), subjectNoun: 'battery run' }).reason)
        .toBe('Written from Root Cause Analysis #77, which was written from this battery run\'s current data.');
    });

    it('is blocked without a stored analysis, with the fix that checks the Root Cause Analysis', () => {
      expect(availabilityOf({ storedRootCauseId: null })).toEqual({
        available: false,
        reason: 'Needs the Root Cause Analysis: check Root Cause Analysis to write both.',
        fix: 'checkRootCause',
        retry: false
      });
    });

    it('is blocked by an outdated stored analysis, with the fix that checks its rewrite', () => {
      expect(availabilityOf({ readiness: answered('outdated') })).toEqual({
        available: false,
        reason: 'Root Cause Analysis #77 was written from different data than this run has now. '
          + 'Check Root Cause Analysis — rewrite to write both.',
        fix: 'checkRootCauseRewrite',
        retry: false
      });
      expect(availabilityOf({ readiness: answered('outdated'), subjectNoun: 'battery run' }).reason)
        .toBe('Root Cause Analysis #77 was written from different data than this battery run has now. '
          + 'Check Root Cause Analysis — rewrite to write both.');
      expect(rootCauseRewriteOffered(answered('outdated'))).toBe(true);
    });

    it('is blocked while the stored analysis is checked, and offers Try again when the check failed', () => {
      expect(availabilityOf({ readiness: null })).toEqual(
        { available: false, reason: 'Checking the stored Root Cause Analysis…', fix: null, retry: false });
      expect(availabilityOf({ readiness: { kind: 'loading' } })).toEqual(
        { available: false, reason: 'Checking the stored Root Cause Analysis…', fix: null, retry: false });
      expect(availabilityOf({ readiness: { kind: 'failed' } })).toEqual(
        { available: false, reason: 'The stored Root Cause Analysis could not be checked.', fix: null, retry: true });
    });

    it('shows the server\'s sentence, with no fix, for a subject it could not prepare or an analysis it no longer finds', () => {
      const unprepared = answered('outdated', { code: null, message: 'The run has no final synthesis.' });
      expect(availabilityOf({ readiness: unprepared })).toEqual(
        { available: false, reason: 'The run has no final synthesis.', fix: null, retry: false });
      expect(rootCauseRewriteOffered(unprepared)).toBe(false);
      expect(availabilityOf({ readiness: answered('missing', { message: 'None is stored.' }) })).toEqual(
        { available: false, reason: 'None is stored.', fix: null, retry: false });
      expect(rootCauseRewriteOffered(answered('usable'))).toBe(false);
      expect(rootCauseRewriteOffered(null)).toBe(false);
      expect(rootCauseRewriteOffered({ kind: 'failed' })).toBe(false);
    });

    it('labels each fix by the checkbox it checks', () => {
      expect(executiveSummaryFixLabel('checkRootCause')).toBe('Check the Root Cause Analysis');
      expect(executiveSummaryFixLabel('checkRootCauseRewrite')).toBe('Check Root Cause Analysis — rewrite');
    });
  });

  it('words the job state, and draws it by kind', () => {
    expect(reportJobStatusText(Pending, null)).toBe('Waiting for the report writer');
    expect(reportJobStatusText(Writing, null)).toBe('Writing…');
    expect(reportJobStatusText(Failed, ' Timed out. ')).toBe('Failed: Timed out.');
    expect(reportJobStatusText(Skipped, null)).toBe('Skipped');
    expect(reportJobStatusText(Canceled, 'Canceled before the writing began.')).toBe('Canceled before the writing began.');
    expect(reportJobStatusText(Canceled, 'Overseer stopped')).toBe('Canceled: Overseer stopped');
    expect(reportJobStatusText(Completed, 'ignored')).toBe('');
    expect(reportJobStatusKind(Failed)).toBe('failed');
    expect(reportJobStatusKind(Skipped)).toBe('skipped');
    expect(reportJobStatusKind(Pending)).toBe('progress');
    expect(reportJobStatusKind(Completed)).toBe('plain');
  });

  it('tags, dates and describes a written document', () => {
    expect(reportDocumentTag(doc(1, ExecutiveSummary))).toBe('Written');
    expect(reportDocumentTag(doc(1, ExecutiveSummary, { status: 'CompletedWithWarnings' }))).toBe('Written with warnings');
    expect(reportDateUtc('2026-10-03T09:30:00Z')).toBe('2026-10-03 09:30 UTC');
    expect(reportDateUtc(null)).toBe('an unknown date');
    expect(reportDocumentByline(doc(1, ExecutiveSummary, { writerDisplayName: '' }), 'Fallback writer'))
      .toBe('by Fallback writer on 2026-10-03 09:30 UTC');
    expect(reportDocumentByline(doc(1, ExecutiveSummary, { writerDisplayName: '' }), null))
      .toBe('by the report writer on 2026-10-03 09:30 UTC');
    expect(reportDocumentMeta(doc(1, ExecutiveSummary, { durationMs: 72000, costUsd: 0.08, sameProviderAcknowledged: true }), null))
      .toBe('by Claude Opus writer on 2026-10-03 09:30 UTC · 1 min 12 s · $0.08 · same provider, acknowledged');
  });

  it('opens a document at its fullest allowed disclosure and maps the viewer\'s variants back', () => {
    const { disclosures, highest, disclosureOf } = reportDocumentDisclosures(doc(1, ExecutiveSummary, { allowedDisclosures: [2, 1] }));
    expect(disclosures).toEqual([BenchmarkReportDisclosure.Summary, BenchmarkReportDisclosure.Detailed]);
    expect(highest).toBe(BenchmarkReportDisclosure.Detailed);
    expect(disclosureOf('summary')).toBe(BenchmarkReportDisclosure.Summary);
    expect(disclosureOf(null)).toBe(BenchmarkReportDisclosure.Detailed);
    expect(reportDocumentDisclosures(doc(1, ExecutiveSummary, { allowedDisclosures: [] })).highest).toBe(BenchmarkReportDisclosure.Full);
  });

  it('shows the estimate as loading, failed, without a price card, or ready with a breakdown for two or more documents', () => {
    const estimate = {
      estimates: [
        { audience: ExecutiveSummary, promptChars: 1, estimatedInputTokens: 1, estimatedOutputTokens: 1, estimatedCostUsd: 0.04 },
        { audience: RootCauseAnalysis, promptChars: 1, estimatedInputTokens: 1, estimatedOutputTokens: 1, estimatedCostUsd: 0.14 }
      ],
      estimatedTotalCostUsd: 0.18,
      refusal: null,
      sameProviderWarning: null
    };
    expect(reportEstimateView(true, false, null)?.state).toBe('loading');
    expect(reportEstimateView(false, true, null)?.state).toBe('failed');
    expect(reportEstimateView(false, false, null)).toBeNull();
    expect(reportEstimateView(false, false, { ...estimate, refusal: 'No.' })).toBeNull();
    expect(reportEstimateView(false, false, { ...estimate, estimatedTotalCostUsd: null })?.state).toBe('noPrice');
    expect(reportEstimateView(false, false, estimate)).toEqual({
      state: 'ready',
      total: '$0.18',
      parts: [{ name: 'Executive Summary', cost: '$0.04' }, { name: 'Root Cause Analysis', cost: '$0.14' }]
    });
  });

  it('reads the server\'s message and recognizes a same-provider warning', () => {
    expect(reportServerErrorText({ error: ' Refused. ' })).toBe('Refused.');
    expect(reportServerErrorText({ error: { error: 'Busy.' } })).toBe('Busy.');
    expect(reportServerErrorText({ error: {} })).toBeNull();
    expect(isReportWriterSameProviderWarning({ provider: 'OpenAI', sameProvider: true })).toBe(true);
    expect(isReportWriterSameProviderWarning({ provider: 'OpenAI', role: 'reportWriter' })).toBe(true);
    expect(isReportWriterSameProviderWarning({ error: 'No.' })).toBe(false);
    expect(isReportWriterSameProviderWarning(null)).toBe(false);
  });

  it('takes the status and the writer from a job view', () => {
    const job = { status: Writing, message: null, writerConfigId: 7, writerDisplayName: 'Claude Opus writer' } as unknown as BenchmarkRunReportJobDto;
    expect(reportStatusSnapshotOfJob(job)).toEqual({ status: Writing, message: null, writerId: 7, writerName: 'Claude Opus writer' });
  });
});
