import { BenchmarkReportAudience } from '../../../services/admin-benchmark.service';

/*
 * The advice behind the *Choosing a report writer* info tips: the run report's and the battery run
 * report's AI Reports tabs, the Report Pack dialog and the Chat Consistency wizard's Reports step. It follows docs/overseer/ai-benchmark-report-pack.md § 4, The Writer Model.
 */

/** One entry of the advice: a document, or what every document shares. */
export interface ReportWriterAdviceEntry {
  readonly term: string;
  /** A short marker after the term, such as its length. */
  readonly badge: string | null;
  readonly text: string;
}

/** The Report Pack dialog's opening paragraph: what the writer does. */
export const REPORT_WRITER_ADVICE_LEAD = 'The report writer writes the documents’ words from the figures the server '
  + 'computed; it never writes a figure. One call per document, and one repair turn when its output fails validation.';

export const REPORT_WRITER_ADVICE_BY_AUDIENCE: Readonly<Record<BenchmarkReportAudience, ReportWriterAdviceEntry>> = {
  [BenchmarkReportAudience.ExecutiveSummary]: {
    term: 'Executive Summary',
    badge: 'Short',
    text: 'For decision-makers. A short, plain-language document (about 2,000 output tokens) where clear, careful '
      + 'wording matters more than depth. Use a strong writing model — Claude Opus or GPT Sol — at medium effort. It is '
      + 'cheap even with a strong model.'
  },
  [BenchmarkReportAudience.TechnicalReport]: {
    term: 'Report for AI Researchers and Developers',
    badge: 'Long',
    text: 'For specialists. A long, number-dense document (about 7,000 output tokens) that must keep every figure exact '
      + 'and follow a strict schema. Use the strongest scoring-tier reasoning model you trust with numbers — Claude Opus '
      + 'or GPT Sol — at medium effort, high if its documents often need repair. It costs roughly three to four times '
      + 'the summary.'
  },
  [BenchmarkReportAudience.InternalBrief]: {
    term: 'Internal Improvement Brief',
    badge: 'Internal',
    text: 'For the Overseer team. It weighs the whole fact sheet, rubrics and grader notes included, and says what to '
      + 'improve in the chat, the benchmark and the model. Use the strongest scoring-tier model — Claude Opus or GPT '
      + 'Sol — at medium effort.'
  },
  [BenchmarkReportAudience.ProviderIssueReport]: {
    term: 'Provider Issue Report',
    badge: 'Provider',
    text: 'For the model’s provider, from a chat consistency analysis: the issue, its timeline and measurements, what '
      + 'was ruled out, and the request to the provider. Use a strong writing model that keeps every figure exact — '
      + 'Claude Opus or GPT Sol — at medium effort.'
  }
};

/** What every document of a run shares; the AI Reports tab writes each one separately. */
export const REPORT_WRITER_ADVICE_RUN_SHARED: ReportWriterAdviceEntry = {
  term: 'Every document',
  badge: null,
  text: 'Prefer a writer from another provider than the model under test; a writer from the same provider is allowed '
    + 'after a warning. Where the roster allows, prefer one that shares a family with neither panel member either. '
    + 'Avoid economy tiers (Flash, Flash-Lite) and the top tiers (Claude Fable, GPT Astra). Each document can be '
    + 'written by a different model: write one, then choose another writer for the next.'
};

/** What every document of a battery run shares; the battery run report's AI Reports tab writes each one separately. */
export const REPORT_WRITER_ADVICE_BATTERY_SHARED: ReportWriterAdviceEntry = {
  term: 'Every document',
  badge: null,
  text: 'A battery’s documents describe a composite index over several suites, so their fact sheets are longer than '
    + 'a run’s and cost more to write. Prefer a writer from another provider than the model under test; a writer from '
    + 'the same provider is allowed after a warning. Avoid economy tiers (Flash, Flash-Lite) and the top tiers (Claude '
    + 'Fable, GPT Astra). Each document can be written by a different model: write one, then choose another writer for '
    + 'the next.'
};

/** What every document of a report pack shares; one writer writes every document the pack generates. */
export const REPORT_WRITER_ADVICE_PACK_SHARED: ReportWriterAdviceEntry = {
  term: 'Every document',
  badge: null,
  text: 'Prefer a writer from another provider than the subject’s; a writer from the same provider is allowed after '
    + 'a confirmation. Where the roster allows, prefer one that shares a family with neither panel member either. '
    + 'Avoid economy tiers (Flash, Flash-Lite) and the top tiers (Claude Fable, GPT Astra). One writer writes every '
    + 'document a pack generates: to use another writer for one document, generate it on its own.'
};

/** What every document of a chat consistency analysis shares; each one is written once. */
export const REPORT_WRITER_ADVICE_CHAT_CONSISTENCY_SHARED: ReportWriterAdviceEntry = {
  term: 'Every document',
  badge: null,
  text: 'Prefer a writer from another provider than the analyzed model’s; a writer from the same provider is allowed '
    + 'after a confirmation. Avoid economy tiers (Flash, Flash-Lite) and the top tiers (Claude Fable, GPT Astra). Each '
    + 'document of an analysis is written once: to write it again, delete it first.'
};

/** The run report's AI Reports tab: its three documents, then what they share. */
export const RUN_REPORT_WRITER_ADVICE: readonly ReportWriterAdviceEntry[] = [
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.ExecutiveSummary],
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.TechnicalReport],
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.InternalBrief],
  REPORT_WRITER_ADVICE_RUN_SHARED
];

/** The battery run report's AI Reports tab: its three documents, then what they share. */
export const BATTERY_REPORT_WRITER_ADVICE: readonly ReportWriterAdviceEntry[] = [
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.ExecutiveSummary],
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.TechnicalReport],
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.InternalBrief],
  REPORT_WRITER_ADVICE_BATTERY_SHARED
];

/** The Report Pack dialog: its three documents, then what they share. */
export const REPORT_PACK_WRITER_ADVICE: readonly ReportWriterAdviceEntry[] = [
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.ExecutiveSummary],
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.TechnicalReport],
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.InternalBrief],
  REPORT_WRITER_ADVICE_PACK_SHARED
];

/** The Chat Consistency wizard's Reports step: its four documents, then what they share. */
export const CHAT_CONSISTENCY_REPORT_WRITER_ADVICE: readonly ReportWriterAdviceEntry[] = [
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.ExecutiveSummary],
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.TechnicalReport],
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.InternalBrief],
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.ProviderIssueReport],
  REPORT_WRITER_ADVICE_CHAT_CONSISTENCY_SHARED
];
