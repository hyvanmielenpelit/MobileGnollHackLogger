import { BenchmarkReportAudience } from '../../../services/admin-benchmark.service';

/*
 * The advice behind the *Choosing a report writer* info tips: the run report's and the battery run
 * report's AI Reports tabs and the Report Pack dialog. It follows docs/overseer/ai-benchmark-report-pack.md § 4, The Writer Model.
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
  }
};

/** What every document of a run shares; the AI Reports tab writes each one separately. */
export const REPORT_WRITER_ADVICE_RUN_SHARED: ReportWriterAdviceEntry = {
  term: 'Both',
  badge: null,
  text: 'Prefer a writer from another provider than the model under test; a writer from the same provider is allowed '
    + 'after a warning. Where the roster allows, prefer one that shares a family with neither panel member either. '
    + 'Avoid economy tiers (Flash, Flash-Lite) and the top tiers (Claude Fable, GPT Astra). Each document can be '
    + 'written by a different model: write one, then choose another writer for the other.'
};

/** What both documents of a battery run share; the battery run report's AI Reports tab writes each one separately. */
export const REPORT_WRITER_ADVICE_BATTERY_SHARED: ReportWriterAdviceEntry = {
  term: 'Both',
  badge: null,
  text: 'A battery’s documents describe a composite index over several suites, so their fact sheets are longer than '
    + 'a run’s and cost more to write. Prefer a writer from another provider than the model under test; a writer from '
    + 'the same provider is allowed after a warning. Avoid economy tiers (Flash, Flash-Lite) and the top tiers (Claude '
    + 'Fable, GPT Astra). Each document can be written by a different model: write one, then choose another writer for '
    + 'the other.'
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

/** The run report's AI Reports tab: its two documents, then what they share. */
export const RUN_REPORT_WRITER_ADVICE: readonly ReportWriterAdviceEntry[] = [
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.ExecutiveSummary],
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.TechnicalReport],
  REPORT_WRITER_ADVICE_RUN_SHARED
];

/** The battery run report's AI Reports tab: its two documents, then what they share. */
export const BATTERY_REPORT_WRITER_ADVICE: readonly ReportWriterAdviceEntry[] = [
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.ExecutiveSummary],
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.TechnicalReport],
  REPORT_WRITER_ADVICE_BATTERY_SHARED
];

/** The Report Pack dialog: its three documents, then what they share. */
export const REPORT_PACK_WRITER_ADVICE: readonly ReportWriterAdviceEntry[] = [
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.ExecutiveSummary],
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.TechnicalReport],
  REPORT_WRITER_ADVICE_BY_AUDIENCE[BenchmarkReportAudience.InternalBrief],
  REPORT_WRITER_ADVICE_PACK_SHARED
];
