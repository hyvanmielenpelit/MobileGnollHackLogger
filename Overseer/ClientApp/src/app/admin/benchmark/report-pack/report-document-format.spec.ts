import { BenchmarkReportAudience, BenchmarkReportDisclosure, BenchmarkReportScope } from '../../../services/admin-benchmark.service';
import {
  CHAT_CONSISTENCY_REPORT_AUDIENCE_OPTIONS,
  COMPARISON_REPORT_AUDIENCE_OPTIONS,
  PROVIDER_ISSUE_REPORT_OPTION,
  REPORT_DOCUMENT_AUDIENCE_OPTIONS,
  REPORT_DOCUMENT_SCOPES,
  ReportAudienceScope,
  audienceLabel,
  audienceShortLabel,
  chatConsistencyAnalysisIdOf,
  chatConsistencySubjectKey,
  disclosureLabel,
  documentChipClass,
  documentStatusLabel,
  formatCostUsd,
  formatElapsed,
  formatUtc,
  peerCountLabel,
  reportAudiencesFor,
  reportDocumentScope,
  reportPackAudienceScope,
  reportKindSlug,
  reportScopeLabel,
  statusLabel
} from './report-document-format';

describe('report-document-format', () => {
  it('names each audience, and falls back for an unknown or retired one', () => {
    expect(audienceLabel(BenchmarkReportAudience.ExecutiveSummary)).toBe('Executive Summary');
    expect(audienceLabel(BenchmarkReportAudience.RootCauseAnalysis)).toBe('Root Cause Analysis');
    expect(audienceLabel(2 as BenchmarkReportAudience)).toBe('Report document');
    expect(audienceLabel(3 as BenchmarkReportAudience)).toBe('Report document');
    expect(audienceLabel(99 as BenchmarkReportAudience)).toBe('Report document');
  });

  it('offers each scope its own two documents', () => {
    const audiences = (scope: ReportAudienceScope) => reportAudiencesFor(scope).map(option => option.audience);
    const both = [BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.RootCauseAnalysis];
    expect(audiences('comparison')).toEqual(both);
    expect(audiences('model')).toEqual(both);
    expect(audiences('run')).toEqual(both);
    expect(audiences('battery')).toEqual(both);
    expect(audiences('chatConsistency')).toEqual([
      BenchmarkReportAudience.ExecutiveSummary,
      BenchmarkReportAudience.ProviderIssueReport
    ]);
    expect(COMPARISON_REPORT_AUDIENCE_OPTIONS.map(option => option.checkedByDefault)).toEqual([true, true]);
    expect(reportPackAudienceScope(BenchmarkReportScope.Comparison)).toBe('comparison');
    expect(reportPackAudienceScope(BenchmarkReportScope.Model)).toBe('model');
    expect(reportPackAudienceScope(undefined)).toBe('model');
  });

  it('gives each document a short label that is a word of its full label', () => {
    expect(REPORT_DOCUMENT_AUDIENCE_OPTIONS.map(option => option.shortLabel)).toEqual(['Executive', 'Root Cause', 'Provider']);
    for (const option of REPORT_DOCUMENT_AUDIENCE_OPTIONS) {
      expect(option.label).toContain(option.shortLabel);
    }
    expect(audienceShortLabel(BenchmarkReportAudience.RootCauseAnalysis)).toBe('Root Cause');
    expect(audienceShortLabel(99 as BenchmarkReportAudience)).toBe('Document');
  });

  it('spells each document kind once in a file name, as the server does', () => {
    expect(reportKindSlug(BenchmarkReportAudience.ExecutiveSummary)).toBe('executive-summary');
    expect(reportKindSlug(BenchmarkReportAudience.RootCauseAnalysis)).toBe('root-cause-analysis');
    expect(reportKindSlug(99 as BenchmarkReportAudience)).toBe('99');
  });

  it('names the Provider Issue Report and spells its kind, offering it only in chat consistency', () => {
    expect(audienceLabel(BenchmarkReportAudience.ProviderIssueReport)).toBe('Provider Issue Report');
    expect(audienceShortLabel(BenchmarkReportAudience.ProviderIssueReport)).toBe('Provider');
    expect(PROVIDER_ISSUE_REPORT_OPTION.label).toContain(PROVIDER_ISSUE_REPORT_OPTION.shortLabel);
    expect(reportKindSlug(BenchmarkReportAudience.ProviderIssueReport)).toBe('provider-issue-report');
    for (const scope of ['comparison', 'model', 'run', 'battery'] as const) {
      expect(reportAudiencesFor(scope).some(option => option.audience === BenchmarkReportAudience.ProviderIssueReport)).toBe(false);
    }
    expect(CHAT_CONSISTENCY_REPORT_AUDIENCE_OPTIONS.some(option => option.audience === BenchmarkReportAudience.RootCauseAnalysis)).toBe(false);
    expect(REPORT_DOCUMENT_AUDIENCE_OPTIONS.map(option => option.audience)).toEqual([
      BenchmarkReportAudience.ExecutiveSummary,
      BenchmarkReportAudience.RootCauseAnalysis,
      BenchmarkReportAudience.ProviderIssueReport
    ]);
  });

  it('builds and reads a chat consistency subject key', () => {
    expect(chatConsistencySubjectKey(12)).toBe('chat-consistency:12');
    expect(chatConsistencyAnalysisIdOf('chat-consistency:12')).toBe(12);
    expect(chatConsistencyAnalysisIdOf('chat-consistency:0')).toBeNull();
    expect(chatConsistencyAnalysisIdOf('chat-consistency')).toBeNull();
    expect(chatConsistencyAnalysisIdOf('run:12')).toBeNull();
    expect(chatConsistencyAnalysisIdOf(null)).toBeNull();
  });

  it('tells a document of the whole comparison, a model subset and one model apart, and labels each', () => {
    expect(reportDocumentScope({ scope: BenchmarkReportScope.Comparison, coversAllEntries: true })).toBe('comparison');
    expect(reportDocumentScope({ scope: BenchmarkReportScope.Comparison, coversAllEntries: false })).toBe('subset');
    expect(reportDocumentScope({ scope: BenchmarkReportScope.Model, coversAllEntries: false })).toBe('model');
    // A document listed before scopes existed is one model's.
    expect(reportDocumentScope({})).toBe('model');

    expect(REPORT_DOCUMENT_SCOPES.map(scope => scope.label)).toEqual(['Whole comparison', 'Model subset', 'One model']);
    expect(reportScopeLabel('subset')).toBe('Model subset');
    expect(reportScopeLabel('other')).toBe('other');
  });

  it('names each disclosure level', () => {
    expect(disclosureLabel(BenchmarkReportDisclosure.Summary)).toBe('Summary');
    expect(disclosureLabel(BenchmarkReportDisclosure.Detailed)).toBe('Detailed');
    expect(disclosureLabel(BenchmarkReportDisclosure.Full)).toBe('Full');
  });

  it('words a status, and a stored document status as the AI Reports tab does', () => {
    expect(statusLabel('CompletedWithWarnings')).toBe('Completed with warnings');
    expect(statusLabel(null)).toBe('');
    expect(documentStatusLabel('Completed')).toBe('Written');
    expect(documentStatusLabel('CompletedWithWarnings')).toBe('Written with warnings');
    expect(documentStatusLabel('Failed')).toBe('Failed');
  });

  it('gives each document status its chip class', () => {
    expect(documentChipClass('Writing')).toBe('job-status-chip status-generating');
    expect(documentChipClass('Completed')).toBe('job-status-chip status-completed');
    expect(documentChipClass('CompletedWithWarnings')).toBe('job-status-chip status-partial');
    expect(documentChipClass('Queued')).toBe('job-status-chip status-pending');
  });

  it('formats a cost', () => {
    expect(formatCostUsd(null)).toBe('Unknown');
    expect(formatCostUsd(Number.NaN)).toBe('Unknown');
    expect(formatCostUsd(0)).toBe('$0.00');
    expect(formatCostUsd(0.0042)).toBe('$0.0042');
    expect(formatCostUsd(1.234)).toBe('$1.23');
  });

  it('formats a UTC timestamp to the minute', () => {
    expect(formatUtc('2026-09-21T16:00:42Z')).toBe('2026-09-21 16:00 UTC');
    expect(formatUtc(null)).toBe('');
    expect(formatUtc('not a date')).toBe('not a date');
  });

  it('reads a server timestamp without a zone as UTC, whatever the browser\'s time zone', () => {
    expect(formatUtc('2026-10-05T16:51:44')).toBe('2026-10-05 16:51 UTC');
    expect(formatUtc('2026-10-05T16:51:44Z')).toBe('2026-10-05 16:51 UTC');
    expect(formatUtc('2026-10-05T18:51:44+02:00')).toBe('2026-10-05 16:51 UTC');
  });

  it('formats an elapsed time', () => {
    expect(formatElapsed(-5)).toBe('0 s');
    expect(formatElapsed(42_000)).toBe('42 s');
    expect(formatElapsed(185_000)).toBe('3 min 05 s');
    expect(formatElapsed(3_720_000)).toBe('1 h 02 min');
  });

  it('counts the other models', () => {
    expect(peerCountLabel(0)).toBe('No other models');
    expect(peerCountLabel(1)).toBe('1 other model');
    expect(peerCountLabel(4)).toBe('4 other models');
  });
});
