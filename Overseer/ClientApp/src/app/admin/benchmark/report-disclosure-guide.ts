import { BenchmarkReportAudience } from '../../services/admin-benchmark.service';
import type { PdfViewerVariantsInfo, PdfViewerVariantsInfoItem } from '../../shared/pdf-viewer/pdf-viewer-dialog.component';

/*
 * What each report document prints at each disclosure level: the PDF viewer's Versions and the
 * Download Center's Disclosure. The levels offered, the banners and the markings are defined by
 * the server's BenchmarkReportPackRenderer (AllowedDisclosures and its stamp constants), which is
 * the source of truth for the quoted banner texts.
 */

/** What each disclosure level of one document type prints. */
export interface ReportDisclosureGuide {
  readonly audience: BenchmarkReportAudience;
  /** The document type, as the Download Center heads its section. */
  readonly heading: string;
  /** The PDF viewer's Versions dialog title. */
  readonly title: string;
  /** One item per level the document offers, in order, then what every level shares. */
  readonly items: readonly PdfViewerVariantsInfoItem[];
}

const SUMMARY_BANNER = 'Page 1 banner: “Confidential. Prepared for the model’s provider. Questions are described, not quoted.” '
  + 'Every page’s footer: CONFIDENTIAL — PROVIDER COPY.';
const FULL_MARKING = 'Every page’s footer: INTERNAL, over a diagonal INTERNAL watermark.';
const INTERNAL_FILE_NAME = 'The file name ends in _INTERNAL.';
const FULL_QUESTIONS_BANNER = 'Page 1 banner: “INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.”';

export const REPORT_DISCLOSURE_GUIDES: readonly ReportDisclosureGuide[] = [
  {
    audience: BenchmarkReportAudience.ExecutiveSummary,
    heading: 'Executive Summary',
    title: 'What Summary and Full contain',
    items: [
      {
        term: 'Summary',
        text: 'For the model’s company.',
        points: [
          SUMMARY_BANNER,
          'Removed content, present only when validation removed items from the writer’s text: each item’s location '
            + 'and rule number, without the reason.'
        ]
      },
      {
        term: 'Full',
        text: 'Internal only; never share it outside the Overseer team.',
        points: [
          'Page 1 banner: “INTERNAL — unpublished benchmark results. Do not share outside the Overseer team.” '
            + FULL_MARKING + ' ' + INTERNAL_FILE_NAME,
          'Removed content also gives the validator’s reason for each removed item.'
        ]
      },
      {
        term: 'Both levels',
        text: 'Everything else is identical.',
        points: [
          'The result in one sentence, key figures, what it did well, where it fell short, what it means for use as a '
            + 'game assistant, how reliable the result is, about this benchmark, and evaluation terms.',
          'Neither level quotes a question, rubric, answer or grader note, or lists the questions.'
        ]
      }
    ]
  },
  {
    audience: BenchmarkReportAudience.TechnicalReport,
    heading: 'Report for AI Researchers and Developers',
    title: 'What Summary, Detailed and Full contain',
    items: [
      {
        term: 'Summary',
        text: 'Safe to send to the model’s company.',
        points: [
          'Every section: abstract, key figures, setup and method, results against peers, speed and cost, strengths and '
            + 'weaknesses, recommendations, per-question results, tool use, grader reliability, threats to validity and '
            + 'the reproducibility appendix.',
          'Per-question results name each question by its topic, never its text, with its score and peer comparison, '
            + 'critical error, refuted answer sentences, tool calls and model time, plus the writer’s note on each '
            + 'question it flags.',
          'Strengths, weaknesses and recommendations carry only their support label.',
          SUMMARY_BANNER + ' Removed content, when present, lists location and rule number only.'
        ]
      },
      {
        term: 'Detailed',
        text: 'For the model’s company, not for publishing: it reveals the benchmark’s questions. Everything in Summary, plus:',
        points: [
          'An Evidence line under each strength, weakness and recommendation: the grading rows and facts it cites, the '
            + 'questions it cites with their scores, and any of them on which the claim verifier refuted an answer sentence.',
          'Questions and answers: every question word for word, and each run’s answer excerpt. The excerpt is the '
            + 'answer’s opening, cut at the length set when the document was written (600 characters by default); a '
            + 'shorter answer appears whole.',
          'Page 1 banner: “Confidential. Prepared for the model’s provider. Contains benchmark questions — do not publish.”',
          'Still no rubrics, individual graders’ scores or notes, or claim verifier rulings.'
        ]
      },
      {
        term: 'Full',
        text: 'Internal only; never share it outside the Overseer team. Everything in Detailed, plus:',
        points: [
          'Question details, in place of Questions and answers: each question’s rubric, each run’s complete answer, '
            + 'every grader’s score, comment and evidence, and every claim the verifier checked with its ruling and '
            + 'reason. A document written before complete answers were kept shows the excerpt, with a note saying so.',
          'Grader reliability: each grader’s own note on every finding row.',
          'Tool use: whether each call’s arguments and results are in the runs’ tool-call logs.',
          'Removed content: the validator’s reason for each removed item.',
          FULL_QUESTIONS_BANNER + ' ' + FULL_MARKING + ' ' + INTERNAL_FILE_NAME
        ]
      }
    ]
  },
  {
    audience: BenchmarkReportAudience.InternalBrief,
    heading: 'Internal Improvement Brief',
    title: 'What Full contains',
    items: [
      {
        term: 'Full',
        text: 'The only level; internal only, never share it outside the Overseer team.',
        points: [
          'Sections on the Overseer chat and its tools, the benchmarking system, the model’s result, and leads, with '
            + 'their recommendations.',
          'Per-question results with the same question details as the researcher report at Full: rubrics, complete '
            + 'answers, every grader’s verdict and every claim verifier ruling.',
          'The whole fact sheet as JSON, including each grader’s note on every finding row.',
          'No Evidence lines, tool use, grader reliability or evaluation terms sections.',
          FULL_QUESTIONS_BANNER + ' ' + FULL_MARKING
        ]
      }
    ]
  }
];

/** The PDF viewer's closing paragraph. */
export const REPORT_DISCLOSURE_NOTE = 'Every level prints the same stored document; the level only decides what is printed, '
  + 'and the title block’s Disclosure line names it. This viewer always names the peers.';

/**
 * The PDF viewer's closing paragraph for a document with peers, which the viewer offers named and
 * anonymized.
 */
export const REPORT_DISCLOSURE_NOTE_PEERS = 'Every level prints the same stored document; the level only decides what is '
  + 'printed, and the title block’s Disclosure line names it. Switch Peer names to see the copy a provider would receive.';

/** The same paragraph without the viewer-only sentence, for the Download Center. */
export const REPORT_DISCLOSURE_NOTE_SHARED = 'Every level prints the same stored document; the level only decides what is '
  + 'printed, and the title block’s Disclosure line names it.';

/** How the Markdown and HTML downloads differ from PDF and Word; Download Center only. */
export const REPORT_DISCLOSURE_FORMATS_NOTE = 'Banners, footers and the watermark describe PDF and Word files. Markdown and '
  + 'HTML files carry the banner text near the top and the level in their closing line, with no page footer or watermark; '
  + 'at Full their file names end in _INTERNAL too.';

export const DOWNLOAD_CENTER_DISCLOSURE_TITLE = 'What each disclosure level contains';

/** The guide for one document type; the researcher report's for an unknown audience. */
export function reportDisclosureGuide(audience: BenchmarkReportAudience): ReportDisclosureGuide {
  return REPORT_DISCLOSURE_GUIDES.find(guide => guide.audience === audience)
    ?? REPORT_DISCLOSURE_GUIDES.find(guide => guide.audience === BenchmarkReportAudience.TechnicalReport)!;
}

/**
 * The PDF viewer's explanation of one document type's versions. With `peerNaming`, the viewer also
 * offers the peers named and anonymized, and the note says so.
 */
export function reportDisclosureInfo(
  audience: BenchmarkReportAudience,
  options: { readonly peerNaming?: boolean } = {}
): PdfViewerVariantsInfo {
  const guide = reportDisclosureGuide(audience);
  return {
    title: guide.title,
    items: guide.items.map(item => item.points ? { ...item, points: [...item.points] } : { ...item }),
    note: options.peerNaming ? REPORT_DISCLOSURE_NOTE_PEERS : REPORT_DISCLOSURE_NOTE
  };
}
