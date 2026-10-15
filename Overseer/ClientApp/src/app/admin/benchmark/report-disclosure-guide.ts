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
    audience: BenchmarkReportAudience.RootCauseAnalysis,
    heading: 'Root Cause Analysis',
    title: 'What Full contains',
    items: [
      {
        term: 'Full',
        text: 'The only level; internal only, never share it outside the Overseer team.',
        points: [
          'Scope and design; the evidence on intelligence, speed and cost; the known facts; the hypotheses, each with '
            + 'its evidence for and against and a confidence grade; the conclusions; the next runs and checks; and the '
            + 'limitations.',
          'Four charts drawn by Overseer: where the points were lost, where the time goes, where the cost goes, and how '
            + 'sure each hypothesis is.',
          'Question details for every question it cites: the rubric, the answer excerpts and the grading.',
          'Peers by name, or by letter in the anonymized copy where the document has peers. The Markdown file adds the '
            + 'anonymized fact sheet as JSON.',
          FULL_QUESTIONS_BANNER + ' ' + FULL_MARKING + ' ' + INTERNAL_FILE_NAME
        ]
      }
    ]
  },
  {
    audience: BenchmarkReportAudience.ProviderIssueReport,
    heading: 'Provider Issue Report',
    title: 'What Summary, Detailed and Full contain',
    items: [
      {
        term: 'Summary',
        text: 'For the model’s provider.',
        points: [
          'Page 1 banner: “Confidential. Prepared for the model’s provider.”'
        ]
      },
      {
        term: 'Detailed',
        text: 'For the model’s provider; the same text as Summary, under the same banner.'
      },
      {
        term: 'Full',
        text: 'Internal only; never share it outside the Overseer team.',
        points: [
          'Page 1 banner: “INTERNAL — unpublished chat consistency results. Do not share outside the Overseer team.” '
            + FULL_MARKING + ' ' + INTERNAL_FILE_NAME
        ]
      },
      {
        term: 'Every level',
        text: 'The text is identical; only the banner and the markings change.',
        points: [
          'The overall verdict, the verdict table with a note line per endpoint and the secondary measures, the Overseer '
            + 'updates, the control models per period, the attribution, how to read it, every limitation the analysis '
            + 'recorded and the reproducibility list.',
          'The control models are always named by letter, never by name.'
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

/** The guide for one document type; the Root Cause Analysis' for an unknown audience, so it reads as internal only. */
export function reportDisclosureGuide(audience: BenchmarkReportAudience): ReportDisclosureGuide {
  return REPORT_DISCLOSURE_GUIDES.find(guide => guide.audience === audience)
    ?? REPORT_DISCLOSURE_GUIDES.find(guide => guide.audience === BenchmarkReportAudience.RootCauseAnalysis)!;
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
