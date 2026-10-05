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
  reportServerErrorText,
  reportStatusSnapshotOfJob,
  writtenReportLabels
} from './report-documents-list';

const { ExecutiveSummary, TechnicalReport, InternalBrief } = BenchmarkReportAudience;
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
  it('lists the Executive Summary, the Report for AI Researchers and Developers, then the Internal Improvement Brief', () => {
    expect(REPORT_DOCUMENT_AUDIENCES).toEqual([ExecutiveSummary, TechnicalReport, InternalBrief]);
    expect(BATTERY_COMPLETION_ORIGIN).toBe(3);
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
      doc(1, TechnicalReport, { createdAtUtc: '2026-10-01T00:00:00Z' }),
      doc(2, TechnicalReport, { createdAtUtc: '2026-10-02T00:00:00Z' }),
      doc(3, ExecutiveSummary),
      doc(9, InternalBrief),
      // A Report Pack document about the same battery run, and a completion document of another one.
      doc(4, ExecutiveSummary, { origin: BenchmarkReportDocumentOrigin.ReportPack, createdAtUtc: '2026-10-04T00:00:00Z' }),
      doc(5, ExecutiveSummary, { subjectKey: 'battery:8', createdAtUtc: '2026-10-04T00:00:00Z' })
    ];
    expect(completionDocumentsOf(documents, 'battery:7', BATTERY_COMPLETION_ORIGIN).map(d => d.id)).toEqual([3, 2, 9]);
    expect(completionDocumentsOf(null, 'battery:7', BATTERY_COMPLETION_ORIGIN)).toEqual([]);
    expect(completionDocumentsOf([doc(6, ExecutiveSummary, { subjectKey: 'run:55', origin: BenchmarkReportDocumentOrigin.RunCompletion })],
      'run:55', BenchmarkReportDocumentOrigin.RunCompletion).map(d => d.id)).toEqual([6]);
  });

  it('builds one row per audience, written or not, with the missing and written ones', () => {
    const documents = [doc(3, ExecutiveSummary)];
    expect(reportDocumentRows(documents)).toEqual([
      { audience: ExecutiveSummary, label: 'Executive Summary', doc: documents[0] },
      { audience: TechnicalReport, label: 'Report for AI Researchers and Developers', doc: null },
      { audience: InternalBrief, label: 'Internal Improvement Brief', doc: null }
    ]);
    expect(missingReportAudiences(documents)).toEqual([TechnicalReport, InternalBrief]);
    expect(writtenReportLabels(documents)).toEqual(['Executive Summary']);
    expect(missingReportAudiences([])).toEqual([ExecutiveSummary, TechnicalReport, InternalBrief]);
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
        { audience: TechnicalReport, promptChars: 1, estimatedInputTokens: 1, estimatedOutputTokens: 1, estimatedCostUsd: 0.14 }
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
      parts: [{ name: 'Executive Summary', cost: '$0.04' }, { name: 'Report for AI Researchers and Developers', cost: '$0.14' }]
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
