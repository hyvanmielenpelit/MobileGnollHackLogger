import { BenchmarkReportAudience } from '../../services/admin-benchmark.service';
import type { PdfViewerVariantsInfo, PdfViewerVariantsInfoItem } from '../../shared/pdf-viewer/pdf-viewer-dialog.component';

/** A report document's disclosure levels explained: the PDF viewer's Versions and the Download Center's Disclosure. */
export interface ReportDisclosureGuide {
  readonly title: string;
  /** Summary, Detailed and Full, then what every level has in common. */
  readonly items: readonly PdfViewerVariantsInfoItem[];
  /** Why the Executive Summary comes at Summary and Full only. */
  readonly executiveSummaryNote: string;
}

export const REPORT_DISCLOSURE_GUIDE: ReportDisclosureGuide = {
  title: 'What Summary, Detailed and Full mean',
  items: [
    {
      term: 'Summary',
      text: 'Safe to send to the model’s company. The results, findings and the topic of each question. '
        + 'It never shows the exact questions, the rubrics, the model’s answers or the graders’ notes.'
    },
    {
      term: 'Detailed',
      text: 'For the model’s company, not for publishing. Everything in Summary, plus every question exactly as asked, '
        + 'an excerpt of the model’s answer and the evidence line under each finding. Still no rubrics or grader notes. '
        + 'Publishing it would reveal the benchmark’s questions.'
    },
    {
      term: 'Full',
      text: 'Internal only. Everything in Detailed, plus each question’s rubric, every grader’s score, notes and evidence, '
        + 'the claim checker’s rulings, and the model’s complete answers. Documents written before complete answers were '
        + 'kept show the stored excerpt instead. '
        + 'Marked INTERNAL and watermarked; never share it outside the Overseer team.'
    },
    {
      term: 'Every level',
      text: 'The stored document is the same; the level only decides what is printed.'
    }
  ],
  executiveSummaryNote: 'The Executive Summary is written for decision-makers and never quotes a question, so it comes '
    + 'as Summary (for the model’s company) or Full (internal only). The two differ only in the confidentiality '
    + 'marking and the internal validation notes.'
};

/** The PDF viewer's explanation of a report document's versions; the Executive Summary note only for that audience. */
export function reportDisclosureInfo(audience: BenchmarkReportAudience): PdfViewerVariantsInfo {
  const info: PdfViewerVariantsInfo = {
    title: REPORT_DISCLOSURE_GUIDE.title,
    items: REPORT_DISCLOSURE_GUIDE.items.map(item => ({ ...item }))
  };
  if (audience === BenchmarkReportAudience.ExecutiveSummary) {
    info.note = REPORT_DISCLOSURE_GUIDE.executiveSummaryNote;
  }
  return info;
}
