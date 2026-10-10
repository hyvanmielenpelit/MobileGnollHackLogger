import { BenchmarkReportAudience } from '../../../services/admin-benchmark.service';
import { DEFAULT_DOCUMENT_CHART_LAYOUT } from '../report-pack/report-charts';
import {
  CC_DEFAULT_DOCUMENT_CHART_LAYOUT,
  CC_DEFAULT_REPORT_CHART_LAYOUT,
  CC_DEFAULT_REPORT_CHART_SELECTION,
  CC_REPORT_CHART_COLUMN_DISABLED_REASON,
  CC_REPORT_CHART_LAYOUT_FIELDS,
  CC_REPORT_CHART_PICKER_AUDIENCES,
  CC_REPORT_CHART_PICKER_FIGURES,
  CC_REPORT_CHART_STORAGE_KEY,
  CcDocumentChartLayout,
  ccReportChartAnchor,
  ccReportChartNotes,
  ccReportDocumentChartLayout,
  normalizeCcDocumentChartLayout,
  normalizeCcReportChartLayout,
  normalizeCcReportChartSelection,
  readStoredCcReportChartSettings,
  storeCcReportChartSettings
} from './chat-consistency-report-chart-settings';
import { ccEndpoint } from './chat-consistency-tab.testing';

const { ExecutiveSummary, TechnicalReport, InternalBrief, ProviderIssueReport } = BenchmarkReportAudience;
const ALL = ['cc1-quality', 'cc2-speed', 'cc3-work', 'cc4-timeline'];

describe('chat-consistency-report-chart-settings', () => {
  beforeEach(() => localStorage.removeItem(CC_REPORT_CHART_STORAGE_KEY));
  afterEach(() => localStorage.removeItem(CC_REPORT_CHART_STORAGE_KEY));

  describe('the picker', () => {
    it('lists the four figures, titled as the charts, in document order', () => {
      expect(CC_REPORT_CHART_PICKER_FIGURES.map(figure => figure.key)).toEqual(ALL);
      expect(CC_REPORT_CHART_PICKER_FIGURES.map(figure => figure.title))
        .toEqual(['Intelligence per run', 'Time to first answer text', 'Output tokens per answer', 'Runs and events']);
    });

    it('places the measures after the verdicts and the overview after the events, but in the Executive Summary', () => {
      expect([ExecutiveSummary, TechnicalReport, InternalBrief, ProviderIssueReport].map(audience => ccReportChartAnchor(audience, 'cc4-timeline')))
        .toEqual(['Results', 'Events', 'Events', 'Events']);
      expect(ccReportChartAnchor(TechnicalReport, 'cc1-quality')).toBe('Results');
      const overview = CC_REPORT_CHART_PICKER_FIGURES[3].placement as Record<number, string>;
      expect(overview[ExecutiveSummary]).toBe('Results');
      expect(overview[InternalBrief]).toBe('Events');
    });

    it('offers the four documents with short labels, label size and maximum height, and its own reason', () => {
      expect(CC_REPORT_CHART_PICKER_AUDIENCES.map(option => [option.audience, option.label, option.shortLabel])).toEqual([
        [ExecutiveSummary, 'Executive Summary', 'Executive'],
        [TechnicalReport, 'Report for AI Researchers and Developers', 'Researchers'],
        [InternalBrief, 'Internal Brief', 'Internal'],
        [ProviderIssueReport, 'Provider Issue Report', 'Provider']
      ]);
      expect(CC_REPORT_CHART_LAYOUT_FIELDS).toEqual(['labelPt', 'maxHeightPercent']);
      expect(CC_REPORT_CHART_COLUMN_DISABLED_REASON).toBe('Not chosen in New reports.');
    });

    it('notes each figure whose endpoint the analysis could not compute', () => {
      const notes = ccReportChartNotes([
        ccEndpoint('P1', { computed: false, notComputedReason: 'No common grader.' }),
        ccEndpoint('P2'),
        ccEndpoint('P4', { computed: false })
      ]);
      expect(notes).toEqual({
        'cc1-quality': 'P1 was not computable in this analysis; the chart\'s caption says it is not comparable.',
        'cc3-work': 'P4 was not computable in this analysis; the chart\'s caption says it is not comparable.'
      });
      expect(ccReportChartNotes(null)).toEqual({});
    });
  });

  describe('defaults', () => {
    it('chooses two charts for the summary, all four for researchers, and two each for the brief and the provider', () => {
      expect(CC_DEFAULT_REPORT_CHART_SELECTION).toEqual({
        [ExecutiveSummary]: ['cc1-quality', 'cc2-speed'],
        [TechnicalReport]: ALL,
        [InternalBrief]: ['cc1-quality', 'cc3-work'],
        [ProviderIssueReport]: ['cc2-speed', 'cc4-timeline']
      });
    });

    it('lays the measures out at half and the overview at two thirds, at 8 pt and half the page at most', () => {
      expect(CC_DEFAULT_DOCUMENT_CHART_LAYOUT).toEqual({
        ...DEFAULT_DOCUMENT_CHART_LAYOUT,
        widths: { 'cc1-quality': 'half', 'cc2-speed': 'half', 'cc3-work': 'half', 'cc4-timeline': 'twoThirds' },
        labelPt: 8,
        maxHeightPercent: 50
      });
      expect(Object.keys(CC_DEFAULT_REPORT_CHART_LAYOUT).map(Number)).toEqual([ExecutiveSummary, TechnicalReport, InternalBrief, ProviderIssueReport]);
    });
  });

  describe('normalizing', () => {
    it('keeps the four figures in document order, once each, for the four documents', () => {
      expect(normalizeCcReportChartSelection({
        [ExecutiveSummary]: ['cc4-timeline', 'p1a-quality', 'cc1-quality', 'cc4-timeline', 7],
        [ProviderIssueReport]: [],
        9: ['cc1-quality']
      })).toEqual({ [ExecutiveSummary]: ['cc1-quality', 'cc4-timeline'], [ProviderIssueReport]: [] });
      expect(normalizeCcReportChartSelection('garbage')).toEqual({});
    });

    it('checks the label size, height and widths, fills what is missing from the defaults and fixes the rest', () => {
      const layout = normalizeCcDocumentChartLayout({
        labelPt: 9, maxHeightPercent: 40, widths: { 'cc1-quality': 'half', 'cc4-timeline': 'twoThirds', 'p1a-quality': 'half' },
        theme: 'asInStep2', heading: 'titleAndBadges', logo: true, sideBySide: false, orientation: 'vertical'
      });
      // Half width does not fit 9 pt labels, so it is full column.
      expect(layout).toEqual({
        ...CC_DEFAULT_DOCUMENT_CHART_LAYOUT, labelPt: 9, maxHeightPercent: 40, widths: { 'cc4-timeline': 'twoThirds' }
      });
      expect(normalizeCcDocumentChartLayout({ maxHeightPercent: 33 })).toEqual(CC_DEFAULT_DOCUMENT_CHART_LAYOUT);
      expect(normalizeCcDocumentChartLayout({ widths: {} }).widths).toEqual({});
    });

    it('gives a missing document type the defaults', () => {
      const layout = normalizeCcReportChartLayout({ [InternalBrief]: { ...CC_DEFAULT_DOCUMENT_CHART_LAYOUT, maxHeightPercent: 60 } });
      expect(layout[InternalBrief]!.maxHeightPercent).toBe(60);
      expect(layout[ExecutiveSummary]).toEqual(CC_DEFAULT_DOCUMENT_CHART_LAYOUT);
      expect(layout[ProviderIssueReport]).toEqual(CC_DEFAULT_DOCUMENT_CHART_LAYOUT);
    });
  });

  describe('storage', () => {
    it('reads the defaults without a record', () => {
      expect(readStoredCcReportChartSettings()).toEqual({ selection: CC_DEFAULT_REPORT_CHART_SELECTION, layout: CC_DEFAULT_REPORT_CHART_LAYOUT });
    });

    it('stores version 1 under its own key and reads it back', () => {
      const selection = { ...CC_DEFAULT_REPORT_CHART_SELECTION, [ExecutiveSummary]: ['cc4-timeline'] as const };
      const layout = { ...CC_DEFAULT_REPORT_CHART_LAYOUT, [TechnicalReport]: { ...CC_DEFAULT_DOCUMENT_CHART_LAYOUT, labelPt: 7.5 } };
      storeCcReportChartSettings({ selection, layout });
      const raw = JSON.parse(localStorage.getItem(CC_REPORT_CHART_STORAGE_KEY)!) as { version: number };
      expect(raw.version).toBe(1);
      const read = readStoredCcReportChartSettings();
      expect(read.selection[ExecutiveSummary]).toEqual(['cc4-timeline']);
      expect(read.layout[TechnicalReport]!.labelPt).toBe(7.5);
      expect(localStorage.getItem('overseer.benchmark.reportCharts')).toBeNull();
    });

    it('falls back to the defaults for a corrupt record or another version', () => {
      localStorage.setItem(CC_REPORT_CHART_STORAGE_KEY, '{not json');
      expect(readStoredCcReportChartSettings().selection).toEqual(CC_DEFAULT_REPORT_CHART_SELECTION);
      localStorage.setItem(CC_REPORT_CHART_STORAGE_KEY, JSON.stringify({ version: 2, selection: {}, layout: {} }));
      expect(readStoredCcReportChartSettings().selection).toEqual(CC_DEFAULT_REPORT_CHART_SELECTION);
      localStorage.setItem(CC_REPORT_CHART_STORAGE_KEY, JSON.stringify({ version: 1 }));
      expect(readStoredCcReportChartSettings()).toEqual({ selection: CC_DEFAULT_REPORT_CHART_SELECTION, layout: CC_DEFAULT_REPORT_CHART_LAYOUT });
    });
  });

  describe('the server\'s layout', () => {
    it('pairs consecutive half-width figures at one anchor in a row, and places the rest alone', () => {
      expect(ccReportDocumentChartLayout(TechnicalReport, ALL, CC_DEFAULT_DOCUMENT_CHART_LAYOUT)).toEqual({
        version: 1,
        figures: [
          { key: 'cc1-quality', widthShare: 0.5, rowGroup: 1 },
          { key: 'cc2-speed', widthShare: 0.5, rowGroup: 1 },
          { key: 'cc3-work', widthShare: 0.5, rowGroup: null },
          { key: 'cc4-timeline', widthShare: 2 / 3, rowGroup: null }
        ],
        maxHeightShare: 0.5
      });
      // Document order, whatever order the figures come in: Intelligence and Output tokens share a row.
      expect(ccReportDocumentChartLayout(ExecutiveSummary, ['cc3-work', 'cc1-quality'], CC_DEFAULT_DOCUMENT_CHART_LAYOUT).figures).toEqual([
        { key: 'cc1-quality', widthShare: 0.5, rowGroup: 1 },
        { key: 'cc3-work', widthShare: 0.5, rowGroup: 1 }
      ]);
    });

    it('pairs the overview only where it shares the measures\' anchor', () => {
      const halves: CcDocumentChartLayout = {
        ...CC_DEFAULT_DOCUMENT_CHART_LAYOUT, widths: { 'cc3-work': 'half', 'cc4-timeline': 'half' }
      };
      expect(ccReportDocumentChartLayout(ExecutiveSummary, ['cc3-work', 'cc4-timeline'], halves).figures.map(figure => figure.rowGroup)).toEqual([1, 1]);
      expect(ccReportDocumentChartLayout(InternalBrief, ['cc3-work', 'cc4-timeline'], halves).figures.map(figure => figure.rowGroup)).toEqual([null, null]);
    });

    it('places a width its label size refuses as full column, at the chosen maximum height', () => {
      const layout: CcDocumentChartLayout = { ...CC_DEFAULT_DOCUMENT_CHART_LAYOUT, labelPt: 9, maxHeightPercent: 40 };
      expect(ccReportDocumentChartLayout(ExecutiveSummary, ['cc1-quality', 'cc2-speed'], layout)).toEqual({
        version: 1,
        figures: [{ key: 'cc1-quality', widthShare: 1, rowGroup: null }, { key: 'cc2-speed', widthShare: 1, rowGroup: null }],
        maxHeightShare: 0.4
      });
    });
  });
});
