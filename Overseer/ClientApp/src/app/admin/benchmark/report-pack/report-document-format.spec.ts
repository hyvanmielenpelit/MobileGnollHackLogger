import { BenchmarkReportAudience, BenchmarkReportDisclosure, BenchmarkReportScope } from '../../../services/admin-benchmark.service';
import {
  REPORT_DOCUMENT_SCOPES,
  REPORT_PACK_AUDIENCES,
  audienceLabel,
  audienceShortLabel,
  disclosureLabel,
  documentChipClass,
  documentStatusLabel,
  formatCostUsd,
  formatElapsed,
  formatUtc,
  peerCountLabel,
  reportDocumentScope,
  reportKindSlug,
  reportScopeLabel,
  statusLabel
} from './report-document-format';

describe('report-document-format', () => {
  it('names each audience, and falls back for an unknown one', () => {
    expect(audienceLabel(BenchmarkReportAudience.ExecutiveSummary)).toBe('Executive Summary');
    expect(audienceLabel(BenchmarkReportAudience.TechnicalReport)).toBe('Report for AI Researchers and Developers');
    expect(audienceLabel(BenchmarkReportAudience.InternalBrief)).toBe('Internal Improvement Brief');
    expect(audienceLabel(99 as BenchmarkReportAudience)).toBe('Report document');
  });

  it('offers the three documents, the Internal Improvement Brief unchecked by default', () => {
    expect(REPORT_PACK_AUDIENCES.map(option => option.audience)).toEqual([
      BenchmarkReportAudience.ExecutiveSummary,
      BenchmarkReportAudience.TechnicalReport,
      BenchmarkReportAudience.InternalBrief
    ]);
    expect(REPORT_PACK_AUDIENCES.map(option => option.checkedByDefault)).toEqual([true, true, false]);
  });

  it('gives each document a short label that is a word of its full label', () => {
    expect(REPORT_PACK_AUDIENCES.map(option => option.shortLabel)).toEqual(['Executive', 'Researchers', 'Internal']);
    for (const option of REPORT_PACK_AUDIENCES) {
      expect(option.label).toContain(option.shortLabel);
    }
    expect(audienceShortLabel(BenchmarkReportAudience.TechnicalReport)).toBe('Researchers');
    expect(audienceShortLabel(99 as BenchmarkReportAudience)).toBe('Document');
  });

  it('spells each document kind once in a file name, as the server does', () => {
    expect(reportKindSlug(BenchmarkReportAudience.ExecutiveSummary)).toBe('executive-summary');
    expect(reportKindSlug(BenchmarkReportAudience.TechnicalReport)).toBe('researcher-report');
    expect(reportKindSlug(BenchmarkReportAudience.InternalBrief)).toBe('internal-brief');
    expect(reportKindSlug(99 as BenchmarkReportAudience)).toBe('99');
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
