import {
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportDocumentOrigin,
  BenchmarkRunReportDocumentsStatus,
  BenchmarkRunReportJobDto
} from '../../../services/admin-benchmark.service';
import {
  BATTERY_COMPLETION_ORIGIN,
  CHAT_CONSISTENCY_DOCUMENT_AUDIENCES,
  CHAT_CONSISTENCY_REPORT_ORIGIN,
  REPORT_DOCUMENT_AUDIENCES,
  completionDocumentsOf,
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
  it('lists the Executive Summary, then the Root Cause Analysis', () => {
    expect(REPORT_DOCUMENT_AUDIENCES).toEqual([ExecutiveSummary, RootCauseAnalysis]);
    expect(BATTERY_COMPLETION_ORIGIN).toBe(3);
  });

  it('says the write order of the checked documents', () => {
    expect(reportWriteOrderHint([ExecutiveSummary, RootCauseAnalysis], 'this run\'s'))
      .toBe('Written in this order: the Root Cause Analysis first, then the Executive Summary, which is written from it.');
    expect(reportWriteOrderHint([ExecutiveSummary], 'this battery run\'s'))
      .toBe('The Executive Summary is written from this battery run\'s stored Root Cause Analysis when that was written from '
        + 'the same data; otherwise it is written without one and says so.');
    expect(reportWriteOrderHint([RootCauseAnalysis], 'this run\'s')).toBe('');
    expect(reportWriteOrderHint([], 'this run\'s')).toBe('');
  });

  it('names the Root Cause Analysis a stored Executive Summary was written from, and says when a later one exists', () => {
    const summary = doc(71, ExecutiveSummary, { rootCauseSourceDocumentId: 72 });
    expect(reportRootCauseSourceLine(summary, [summary, doc(72, RootCauseAnalysis)])).toBe('Written from Root Cause Analysis #72');
    // The source deleted, with no later one listed.
    expect(reportRootCauseSourceLine(summary, [summary])).toBe('Written from Root Cause Analysis #72');

    const stale = doc(71, ExecutiveSummary, { rootCauseSourceDocumentId: 70 });
    expect(reportRootCauseSourceLine(stale, [stale, doc(72, RootCauseAnalysis)]))
      .toBe('Written from an earlier Root Cause Analysis (#70); rewrite it to include the newest');
    // Another subject's Root Cause Analysis is not this one's newer analysis.
    expect(reportRootCauseSourceLine(stale, [stale, doc(72, RootCauseAnalysis, { subjectKey: 'battery:8' })]))
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

  it('keeps one subject\'s completion documents of one origin, the newest per audience, in audience order', () => {
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
    expect(completionDocumentsOf(documents, 'battery:7', BATTERY_COMPLETION_ORIGIN).map(d => d.id)).toEqual([3, 2]);
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
      { audience: ExecutiveSummary, label: 'Executive Summary', doc: documents[0] },
      { audience: RootCauseAnalysis, label: 'Root Cause Analysis', doc: null }
    ]);
    expect(missingReportAudiences(documents)).toEqual([RootCauseAnalysis]);
    expect(writtenReportLabels(documents)).toEqual(['Executive Summary']);
    expect(missingReportAudiences([])).toEqual([ExecutiveSummary, RootCauseAnalysis]);
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
