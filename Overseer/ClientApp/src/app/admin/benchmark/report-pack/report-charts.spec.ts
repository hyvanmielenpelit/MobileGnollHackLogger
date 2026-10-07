import { HttpErrorResponse } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkReportAudience,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportScope,
  ReportDocumentChartLayout,
  ReportDocumentChartUpload,
  ReportDocumentChartsSummaryDto
} from '../../../services/admin-benchmark.service';
import { FIGURE_EXPORT_MIN_CONTENT_WIDTH, layoutBoxFor, resolveFigureLayout } from '../model-comparison/figure-export';
import { P1_STACK_BREAKPOINT_PX } from '../model-comparison/model-comparison-charts';
import { DEFAULT_FIGURE_STYLE, FigureStyle } from '../model-comparison/figure-style';
import {
  BASE_LABEL_PX,
  CHART_STORAGE_NOT_CONFIGURED,
  CHAT_CONSISTENCY_CHART_FIGURE_KEYS,
  COMPARISON_SCOPE_CHART_PLACEMENTS,
  ComposedReportChart,
  DEFAULT_CHART_LAYOUT_SETTINGS,
  DEFAULT_CHART_SELECTION,
  DEFAULT_DOCUMENT_CHART_LAYOUT,
  DOCUMENT_CHART_COLUMN_PT,
  DOCUMENT_CHART_DPI,
  DOCUMENT_CHART_MIN_LAYOUT_WIDTH,
  DOCUMENT_MIN_CONTENT_WIDTH,
  DOCUMENT_MIN_PLOT_HEIGHT,
  DOCUMENT_TEXT_COLUMN_PT,
  REPORT_CHART_FIGURES,
  REPORT_CHART_STORAGE_KEY,
  ReportChartDocumentLayout,
  ReportChartFigureKey,
  ReportChartPublishProgress,
  ReportChartPublisher,
  ReportChartSelection,
  ReportChartSettingsInput,
  ReportChartTarget,
  ReportChartVariant,
  canonicalJson,
  chartSettingsHash,
  documentChartHashLayout,
  documentChartLayout,
  documentChartRefusal,
  documentChartSize,
  documentChartWidth,
  documentFigureLayout,
  documentLabelSizeRefusal,
  documentTextStyle,
  isDocumentChartFigureKey,
  isReportChartFigureKey,
  normalizeChartSelection,
  normalizeDocumentChartLayout,
  printFigureAppearance,
  readStoredChartSelection,
  readStoredChartSettings,
  reportChartCoveredKeys,
  reportChartPlacementLabel,
  reportChartTargetFor,
  reportDocumentChartLayout,
  storeChartSelection,
  storeChartSettings
} from './report-charts';

const { ExecutiveSummary, TechnicalReport, InternalBrief } = BenchmarkReportAudience;

const HASH = 'a'.repeat(64);

describe('report-charts', () => {
  describe('figures and placements', () => {
    it('lists the seven figures in placement order, the profile needing three models', () => {
      expect(REPORT_CHART_FIGURES.map(figure => figure.key)).toEqual([
        'p1a-quality', 'p1b-speed', 'p1c-cost', 'p2-profile', 's1-quality-speed', 's2-quality-cost', 's3-speed-cost'
      ]);
      expect(REPORT_CHART_FIGURES.map(figure => figure.title)).toEqual([
        'Intelligence', 'Speed', 'Cost', 'Model profiles',
        'Intelligence against speed', 'Intelligence against cost', 'Speed against cost'
      ]);
      expect(REPORT_CHART_FIGURES.map(figure => figure.minModels)).toEqual([2, 2, 2, 3, 2, 2, 2]);
      expect(isReportChartFigureKey('p2-profile')).toBe(true);
      expect(isReportChartFigureKey('p9-nothing')).toBe(false);
    });

    it('knows the chat consistency figures as document figures, apart from the Report Pack\'s', () => {
      expect(CHAT_CONSISTENCY_CHART_FIGURE_KEYS).toEqual(['cc1-quality', 'cc2-speed', 'cc3-work', 'cc4-timeline']);
      for (const key of CHAT_CONSISTENCY_CHART_FIGURE_KEYS) {
        expect(isDocumentChartFigureKey(key), key).toBe(true);
        expect(isReportChartFigureKey(key), key).toBe(false);
      }
      expect(isDocumentChartFigureKey('p1a-quality')).toBe(true);
      expect(isDocumentChartFigureKey('cc9-nothing')).toBe(false);
    });

    it('places each figure in the section the server draws it in', () => {
      const table = REPORT_CHART_FIGURES.map(figure => [
        reportChartPlacementLabel(ExecutiveSummary, figure.key),
        reportChartPlacementLabel(TechnicalReport, figure.key),
        reportChartPlacementLabel(InternalBrief, figure.key)
      ]);
      expect(table).toEqual([
        ['How it compares', 'Results against peers → Quality', '§3 Key figures'],
        ['How it compares', 'Results against peers → Speed', '§3 Key figures'],
        ['How it compares', 'Results against peers → Cost', '§3 Key figures'],
        ['How it compares', 'Results against peers', '§3 Key figures'],
        ['How it compares', 'Speed and cost', '§3 Key figures'],
        ['How it compares', 'Speed and cost', '§3 Key figures'],
        ['How it compares', 'Speed and cost', '§3 Key figures']
      ]);
    });

    it('places a comparison-scope figure by the comparison-scope table, falling back to the model-scope section', () => {
      const comparisonTable = COMPARISON_SCOPE_CHART_PLACEMENTS as unknown as Record<number, Record<string, string> | undefined>;
      for (const audience of [ExecutiveSummary, TechnicalReport, InternalBrief]) {
        for (const figure of REPORT_CHART_FIGURES) {
          const expected = comparisonTable[audience]?.[figure.key] ?? reportChartPlacementLabel(audience, figure.key);
          expect(reportChartPlacementLabel(audience, figure.key, 'comparison'), `${audience} ${figure.key}`).toBe(expected);
        }
      }
      expect(reportChartPlacementLabel(TechnicalReport, 'p1b-speed', 'model')).toBe('Results against peers → Speed');
    });

    it('places comparison-scope figures at the server\'s comparison-scope anchors', () => {
      const table = REPORT_CHART_FIGURES.map(figure => [
        reportChartPlacementLabel(ExecutiveSummary, figure.key, 'comparison'),
        reportChartPlacementLabel(TechnicalReport, figure.key, 'comparison'),
        reportChartPlacementLabel(InternalBrief, figure.key, 'comparison')
      ]);
      expect(table).toEqual([
        ['How they compare', 'Results', 'Models compared'],
        ['How they compare', 'Speed and cost frontier', 'Models compared'],
        ['How they compare', 'Speed and cost frontier', 'Models compared'],
        ['How they compare', 'Dimension profiles', 'Models compared'],
        ['How they compare', 'Speed and cost frontier', 'Models compared'],
        ['How they compare', 'Speed and cost frontier', 'Models compared'],
        ['How they compare', 'Speed and cost frontier', 'Models compared']
      ]);
    });

    it('defaults to two charts in the Executive Summary, all seven in the researchers\' report and the three bars in the brief', () => {
      expect(DEFAULT_CHART_SELECTION[ExecutiveSummary]).toEqual(['p1a-quality', 's2-quality-cost']);
      expect(DEFAULT_CHART_SELECTION[TechnicalReport]).toEqual(REPORT_CHART_FIGURES.map(figure => figure.key));
      expect(DEFAULT_CHART_SELECTION[InternalBrief]).toEqual(['p1a-quality', 'p1b-speed', 'p1c-cost']);
    });

    it('draws full column at 300 dpi with 8 pt labels by default, taller as the width narrows', () => {
      expect(DOCUMENT_TEXT_COLUMN_PT).toEqual({ a4: 481.9, letter: 498.6 });
      expect(DOCUMENT_CHART_COLUMN_PT).toBe(481.9);
      expect(DOCUMENT_CHART_DPI).toBe(300);
      expect(BASE_LABEL_PX).toBe(11);
      // 481.9 pt × 300 / 72 = 2007.9 px; 16:10 of 481.9 pt is 301.2 pt, 1254.9 px; 4:3 is 361.4 pt, 1505.9 px.
      expect(documentChartSize('p1a-quality')).toEqual({ widthPx: 2008, heightPx: 1255 });
      expect(documentChartSize('s3-speed-cost')).toEqual({ widthPx: 2008, heightPx: 1255 });
      expect(documentChartSize('p2-profile')).toEqual({ widthPx: 2008, heightPx: 1506 });
      // Two thirds, 321.3 pt, at 4:3; half, 241.0 pt, square; the profile one step taller.
      expect(documentChartSize('p1a-quality', 'twoThirds', 8.5)).toEqual({ widthPx: 1339, heightPx: 1004 });
      expect(documentChartSize('p1a-quality', 'half')).toEqual({ widthPx: 1004, heightPx: 1004 });
      expect(documentChartSize('p2-profile', 'twoThirds')).toEqual({ widthPx: 1339, heightPx: 1339 });
      expect(documentChartSize('p2-profile', 'half')).toEqual({ widthPx: 1004, heightPx: 1255 });
    });

    it('grows a figure to the least layout height asked for, and never shrinks it below its aspect', () => {
      const aspect = documentFigureLayout('p1a-quality', 'half', 8);
      expect(documentFigureLayout('p1a-quality', 'half', 8, DOCUMENT_CHART_COLUMN_PT, 100)).toEqual(aspect);

      const taller = documentFigureLayout('p1a-quality', 'half', 8, DOCUMENT_CHART_COLUMN_PT, 500);
      expect(taller.pxWidth).toBe(aspect.pxWidth);
      expect(taller.layoutWidth).toBeCloseTo(aspect.layoutWidth, 9);
      expect(taller.layoutHeight).toBeCloseTo(500, 0);
      expect(DOCUMENT_MIN_PLOT_HEIGHT).toBe(200);
    });
  });

  describe('document charts sized in points', () => {
    it('lays a full column at 8 pt out about 663 px wide, below the 720 px breakpoint', () => {
      const layout = documentChartLayout(481.9, 481.9 * 10 / 16, 8);

      expect(layout.pxWidth).toBe(2008);
      expect(layout.pxHeight).toBe(1255);
      expect(layout.layoutWidth).toBeCloseTo(662.61, 2);
      expect(layout.layoutHeight).toBeCloseTo(662.61 * 1255 / 2008, 1);
      expect(layout.layoutWidth).toBeLessThan(P1_STACK_BREAKPOINT_PX);
      // textScale = (2008 / 662.6) / min(2008 / 960, 1255 / 540)
      expect(layout.textScale).toBeCloseTo((2008 / 662.6125) / (2008 / 960), 6);
    });

    it('gives the text scale that composes the bitmap in exactly the layout width, so a bar label prints at the label size', () => {
      for (const labelPt of [7, 7.5, 8, 8.5, 9, 10]) {
        const layout = documentFigureLayout('p1a-quality', 'full', labelPt);
        const box = layoutBoxFor(layout.pxWidth, layout.pxHeight, layout.textScale);
        expect(box.layoutWidth, `${labelPt} pt`).toBeCloseTo(481.9 * BASE_LABEL_PX / labelPt, 6);
        // An 11 px label in a layout this wide prints at labelPt across the column.
        expect(BASE_LABEL_PX * layout.widthPt / box.layoutWidth, `${labelPt} pt`).toBeCloseTo(labelPt, 6);
      }
      expect(documentFigureLayout('p1a-quality', 'full', 8, DOCUMENT_TEXT_COLUMN_PT.letter).layoutWidth).toBeCloseTo(685.575, 3);
    });

    it('refuses a chart narrower than its content column and padding, naming the label size that would fit', () => {
      expect(DOCUMENT_MIN_CONTENT_WIDTH).toBe(260);
      expect(DOCUMENT_CHART_MIN_LAYOUT_WIDTH).toBe(300);
      for (const labelPt of [7, 7.5, 8, 8.5, 9, 10]) {
        expect(documentChartRefusal('full', labelPt), `full at ${labelPt} pt`).toBeNull();
        expect(documentChartRefusal('twoThirds', labelPt), `two thirds at ${labelPt} pt`).toBeNull();
      }
      expect(documentChartRefusal('half', 8)).toBeNull();
      expect(documentChartRefusal('half', 8.5)).toBeNull();
      expect(documentChartRefusal('half', 9)).toBe(
        'A half-width chart with 9 pt labels would be 294 layout px wide; a chart needs at least 300. '
        + 'Choose 8.5 pt labels or smaller, or a wider chart.');
      expect(documentChartRefusal('half', 10)).toContain('265 layout px wide');
    });

    it('refuses exactly where resolveFigureLayout does with the document charts\' content column', () => {
      const chrome = { title: '', badges: [], detail: '', key: [], highlight: '', notes: [] };
      const request = { chrome, footer: { suite: '', computedAt: '' }, minContentWidth: DOCUMENT_MIN_CONTENT_WIDTH };
      const resolve = (width: 'twoThirds' | 'half', labelPt: number, minContentWidth?: number) => {
        const layout = documentFigureLayout('p1a-quality', width, labelPt);
        return resolveFigureLayout({ ...request, minContentWidth }, {
          id: 'document', label: 'Document', widthPx: layout.pxWidth, heightPx: layout.pxHeight, group: 'Document'
        }, 1, layout.textScale);
      };
      expect(resolve('half', 8.5, DOCUMENT_MIN_CONTENT_WIDTH).layout).not.toBeNull();
      expect(resolve('half', 9, DOCUMENT_MIN_CONTENT_WIDTH).refusal).toContain('narrower than 260');
      expect(resolve('twoThirds', 10, DOCUMENT_MIN_CONTENT_WIDTH).layout).not.toBeNull();
      // Without the document column, the composer keeps step 2's 360 px.
      expect(FIGURE_EXPORT_MIN_CONTENT_WIDTH).toBe(360);
      expect(resolve('half', 8.5).refusal).toContain('narrower than 360');
    });

    it('offers Half at 8 pt and refuses it at 9 pt', () => {
      const layout: ReportChartDocumentLayout = { ...DEFAULT_DOCUMENT_CHART_LAYOUT, widths: { 'p1a-quality': 'half' } };
      expect(documentChartWidth(layout, 'p1a-quality')).toBe('half');
      expect(documentChartWidth({ ...layout, labelPt: 9 }, 'p1a-quality')).toBe('full');
      expect(normalizeDocumentChartLayout(layout).widths).toEqual({ 'p1a-quality': 'half' });
      expect(normalizeDocumentChartLayout({ ...layout, labelPt: 9 }).widths).toEqual({});
    });

    it('refuses a label size too large for the narrowest width the selected figures have', () => {
      const layout: ReportChartDocumentLayout = { ...DEFAULT_DOCUMENT_CHART_LAYOUT, widths: { 's2-quality-cost': 'half' } };
      expect(documentLabelSizeRefusal(layout, ['p1a-quality', 's2-quality-cost'], 9)).toBe(documentChartRefusal('half', 9));
      expect(documentLabelSizeRefusal(layout, ['p1a-quality', 's2-quality-cost'], 8.5)).toBeNull();
      // The half-width figure is not selected: only full-column ones decide.
      expect(documentLabelSizeRefusal(layout, ['p1a-quality'], 10)).toBeNull();
    });

    it('composes and places a refused width as full column', () => {
      const layout: ReportChartDocumentLayout = {
        ...DEFAULT_DOCUMENT_CHART_LAYOUT, labelPt: 9, widths: { 'p1a-quality': 'half', 'p1b-speed': 'twoThirds' }
      };
      expect(documentChartWidth(layout, 'p1a-quality')).toBe('full');
      expect(documentChartWidth(layout, 'p1b-speed')).toBe('twoThirds');
      expect(documentChartWidth({ ...layout, labelPt: 8 }, 'p1a-quality')).toBe('half');
      expect(documentChartWidth(layout, 'p1c-cost')).toBe('full');
    });

    it('draws Light, for print with the light theme on its own background and the theme\'s colors, and keeps the rest', () => {
      const appearance = {
        ...DEFAULT_FIGURE_STYLE.appearance,
        theme: 'dark' as const, background: 'custom' as const, backgroundColor: '#123456',
        headingColor: '#ff0000', textColor: '#00ff00', borderColor: '#0000ff', border: true, borderWidthPx: 3, logo: true
      };
      expect(printFigureAppearance(appearance)).toEqual({
        ...appearance, theme: 'light', background: 'theme', headingColor: null, textColor: null, borderColor: null
      });
    });
  });

  describe('the layout the server places charts by', () => {
    const layout = (overrides: Partial<ReportChartDocumentLayout> = {}): ReportChartDocumentLayout =>
      ({ ...DEFAULT_DOCUMENT_CHART_LAYOUT, ...overrides });

    it('is every figure full column on its own row, at 60 %, by default', () => {
      expect(reportDocumentChartLayout(TechnicalReport, ['p1b-speed', 'p1a-quality'], layout())).toEqual({
        version: 1,
        figures: [
          { key: 'p1a-quality', widthShare: 1, rowGroup: null },
          { key: 'p1b-speed', widthShare: 1, rowGroup: null }
        ],
        maxHeightShare: 0.6
      } satisfies ReportDocumentChartLayout);
    });

    it('carries two-thirds widths and the maximum height', () => {
      const result = reportDocumentChartLayout(ExecutiveSummary, ['p1a-quality', 's2-quality-cost'],
        layout({ widths: { 's2-quality-cost': 'twoThirds' }, maxHeightPercent: 40 }));
      expect(result.figures).toEqual([
        { key: 'p1a-quality', widthShare: 1, rowGroup: null },
        { key: 's2-quality-cost', widthShare: 2 / 3, rowGroup: null }
      ]);
      expect(result.maxHeightShare).toBe(0.4);
    });

    it('places a half-width figure full column where its width is refused', () => {
      const halves = layout({ labelPt: 9, widths: { 'p1a-quality': 'half', 'p1b-speed': 'half' } });
      expect(reportDocumentChartLayout(InternalBrief, ['p1a-quality', 'p1b-speed'], halves).figures)
        .toEqual([{ key: 'p1a-quality', widthShare: 1, rowGroup: null }, { key: 'p1b-speed', widthShare: 1, rowGroup: null }]);
    });

    it('pairs two half-width figures at the same placement into one row group', () => {
      const halves = layout({ widths: { 'p1a-quality': 'half', 'p1b-speed': 'half' } });
      expect(reportDocumentChartLayout(InternalBrief, ['p1a-quality', 'p1b-speed', 'p1c-cost'], halves).figures).toEqual([
        { key: 'p1a-quality', widthShare: 0.5, rowGroup: 1 },
        { key: 'p1b-speed', widthShare: 0.5, rowGroup: 1 },
        { key: 'p1c-cost', widthShare: 1, rowGroup: null }
      ]);
    });

    it('pairs consecutive half-width figures of one section only, and only while side by side is on', () => {
      const halves = layout({ widths: { 'p1a-quality': 'half', 'p1b-speed': 'half', 'p1c-cost': 'half', 's1-quality-speed': 'half' } });
      const keys: ReportChartFigureKey[] = ['p1a-quality', 'p1b-speed', 'p1c-cost', 's1-quality-speed', 's2-quality-cost'];

      // The Executive Summary puts every figure in one section: the four halves form two rows.
      expect(reportDocumentChartLayout(ExecutiveSummary, keys, halves).figures).toEqual([
        { key: 'p1a-quality', widthShare: 0.5, rowGroup: 1 },
        { key: 'p1b-speed', widthShare: 0.5, rowGroup: 1 },
        { key: 'p1c-cost', widthShare: 0.5, rowGroup: 2 },
        { key: 's1-quality-speed', widthShare: 0.5, rowGroup: 2 },
        { key: 's2-quality-cost', widthShare: 1, rowGroup: null }
      ]);
      // The researchers' report puts Quality, Speed and Cost in sections of their own: no pair forms.
      expect(reportDocumentChartLayout(TechnicalReport, keys, halves).figures.map(figure => figure.rowGroup))
        .toEqual([null, null, null, null, null]);
      // Side by side off: every half alone.
      expect(reportDocumentChartLayout(ExecutiveSummary, keys, { ...halves, sideBySide: false }).figures
        .map(figure => [figure.widthShare, figure.rowGroup])).toEqual([[0.5, null], [0.5, null], [0.5, null], [0.5, null], [1, null]]);
    });
  });

  describe('normalizing a document layout', () => {
    it('defaults every unknown or missing field and drops full-column and refused widths', () => {
      expect(normalizeDocumentChartLayout(null)).toEqual(DEFAULT_DOCUMENT_CHART_LAYOUT);
      expect(normalizeDocumentChartLayout({
        orientation: 'diagonal', labelPt: 11, maxHeightPercent: 75, heading: 'loud', theme: 'neon', logo: 'yes', sideBySide: 0,
        widths: { 'p1a-quality': 'full', 'p1b-speed': 'twoThirds', 'p1c-cost': 'half', 'p9-nothing': 'twoThirds', 's1-quality-speed': 'quarter' }
      })).toEqual({ ...DEFAULT_DOCUMENT_CHART_LAYOUT, widths: { 'p1b-speed': 'twoThirds', 'p1c-cost': 'half' } });
      // At 9 pt half width is refused, and dropped.
      expect(normalizeDocumentChartLayout({
        orientation: 'horizontal', labelPt: 9, maxHeightPercent: 40, heading: 'titleAndBadges', theme: 'asInStep2', logo: true,
        sideBySide: false, widths: { 'p1b-speed': 'twoThirds', 'p1c-cost': 'half' }
      })).toEqual({
        orientation: 'horizontal', labelPt: 9, maxHeightPercent: 40, heading: 'titleAndBadges', theme: 'asInStep2', logo: true,
        sideBySide: false, widths: { 'p1b-speed': 'twoThirds' }
      });
    });

    it('defaults to As in step 2, full column, side by side, 8 pt, 60 %, no heading, no logo, Light, for print', () => {
      expect(DEFAULT_DOCUMENT_CHART_LAYOUT).toEqual({
        orientation: 'asInStep2', widths: {}, sideBySide: true, labelPt: 8, maxHeightPercent: 60,
        heading: 'none', logo: false, theme: 'lightPrint'
      });
      expect(DEFAULT_CHART_LAYOUT_SETTINGS).toEqual({
        [ExecutiveSummary]: DEFAULT_DOCUMENT_CHART_LAYOUT,
        [TechnicalReport]: DEFAULT_DOCUMENT_CHART_LAYOUT,
        [InternalBrief]: DEFAULT_DOCUMENT_CHART_LAYOUT
      });
    });
  });

  describe('selection', () => {
    afterEach(() => localStorage.removeItem(REPORT_CHART_STORAGE_KEY));

    it('normalizes: dedupes, orders by placement, drops unknown keys and audiences, keeps an explicit none', () => {
      const messy = {
        [ExecutiveSummary]: ['s2-quality-cost', 'p1a-quality', 's2-quality-cost', 'bogus'],
        [InternalBrief]: [],
        9: ['p1a-quality']
      } as unknown as ReportChartSelection;
      expect(normalizeChartSelection(messy)).toEqual({
        [ExecutiveSummary]: ['p1a-quality', 's2-quality-cost'],
        [InternalBrief]: []
      });
    });

    it('reads the defaults when nothing is stored, the record is unreadable or of another version', () => {
      expect(readStoredChartSelection()).toEqual(DEFAULT_CHART_SELECTION);
      expect(readStoredChartSettings()).toEqual({ selection: DEFAULT_CHART_SELECTION, layout: DEFAULT_CHART_LAYOUT_SETTINGS });
      localStorage.setItem(REPORT_CHART_STORAGE_KEY, '{not json');
      expect(readStoredChartSelection()).toEqual(DEFAULT_CHART_SELECTION);
      localStorage.setItem(REPORT_CHART_STORAGE_KEY, JSON.stringify({ version: 3, selection: { [ExecutiveSummary]: [] } }));
      expect(readStoredChartSelection()).toEqual(DEFAULT_CHART_SELECTION);
    });

    it('stores a version-2 record and reads it back normalized', () => {
      storeChartSelection({ [ExecutiveSummary]: ['p1c-cost', 'p1a-quality'], [TechnicalReport]: [] });
      expect(JSON.parse(localStorage.getItem(REPORT_CHART_STORAGE_KEY)!)).toEqual({
        version: 2,
        selection: { [ExecutiveSummary]: ['p1a-quality', 'p1c-cost'], [TechnicalReport]: [] },
        layout: DEFAULT_CHART_LAYOUT_SETTINGS
      });
      expect(readStoredChartSelection()).toEqual({ [ExecutiveSummary]: ['p1a-quality', 'p1c-cost'], [TechnicalReport]: [] });
    });

    it('migrates a version-1 record with its figure selection intact and the default layout', () => {
      localStorage.setItem(REPORT_CHART_STORAGE_KEY, JSON.stringify({
        version: 1,
        selection: { [ExecutiveSummary]: ['s3-speed-cost', 'p1b-speed'], [InternalBrief]: [] }
      }));

      expect(readStoredChartSettings()).toEqual({
        selection: { [ExecutiveSummary]: ['p1b-speed', 's3-speed-cost'], [InternalBrief]: [] },
        layout: DEFAULT_CHART_LAYOUT_SETTINGS
      });
      // The next store writes version 2.
      storeChartSettings(readStoredChartSettings());
      expect(JSON.parse(localStorage.getItem(REPORT_CHART_STORAGE_KEY)!).version).toBe(2);
    });

    it('stores the layout per document type, normalized, and keeps it when only the selection is stored', () => {
      const brief: ReportChartDocumentLayout = {
        ...DEFAULT_DOCUMENT_CHART_LAYOUT, orientation: 'vertical', labelPt: 7.5, theme: 'asInStep2', logo: true,
        widths: { 'p1b-speed': 'twoThirds' }
      };
      storeChartSettings({
        selection: DEFAULT_CHART_SELECTION,
        layout: { [InternalBrief]: { ...brief, heading: 'loud' } as unknown as ReportChartDocumentLayout }
      });

      const read = readStoredChartSettings();
      expect(read.layout[InternalBrief]).toEqual({ ...brief, heading: 'none' });
      expect(read.layout[ExecutiveSummary]).toEqual(DEFAULT_DOCUMENT_CHART_LAYOUT);

      storeChartSelection({ [InternalBrief]: ['p1a-quality'] });
      expect(readStoredChartSettings()).toEqual({ selection: { [InternalBrief]: ['p1a-quality'] }, layout: read.layout });
    });

    it('survives storage that throws', () => {
      vi.spyOn(localStorage, 'getItem').mockImplementation(() => {
        throw new Error('denied');
      });
      vi.spyOn(localStorage, 'setItem').mockImplementation(() => {
        throw new Error('denied');
      });
      expect(() => storeChartSelection(DEFAULT_CHART_SELECTION)).not.toThrow();
      expect(readStoredChartSelection()).toEqual(DEFAULT_CHART_SELECTION);
    });
  });

  describe('the settings hash', () => {
    const settings = (overrides: Partial<ReportChartSettingsInput> = {}): ReportChartSettingsInput => ({
      figureStyle: { theme: 'dark', font: 'Lato' },
      layout: documentChartHashLayout(DEFAULT_CHART_LAYOUT_SETTINGS),
      show: ['run:1', 'run:2'],
      highlight: [],
      order: { mode: 'score' },
      measures: { speed: 'median', cost: 'perQuestion' },
      pricingBasis: 'Current',
      computedAtUtc: '2026-09-30T10:00:00Z',
      ...overrides
    });

    it('writes canonical JSON: keys sorted at every depth, arrays in order, undefined left out', () => {
      expect(canonicalJson({ b: 1, a: { d: [3, { z: true, y: null }], c: 'x' }, u: undefined }))
        .toBe('{"a":{"c":"x","d":[3,{"y":null,"z":true}]},"b":1}');
      // Integer-like keys sort as text too, unlike an object's own key order.
      expect(canonicalJson({ 10: 'ten', 2: 'two', a: 'a' })).toBe('{"10":"ten","2":"two","a":"a"}');
      expect(canonicalJson([undefined, Number.NaN])).toBe('[null,null]');
    });

    it('hashes to 64 lowercase hex characters, pinned, whatever the key order', async () => {
      const hash = await chartSettingsHash(settings());
      expect(hash).toBe('a96a276f21ec376190ea617df68188050f980844ef78d378278b853efe41f46c');
      const reordered = await chartSettingsHash(settings({ figureStyle: { font: 'Lato', theme: 'dark' } }));
      expect(reordered).toBe(hash);
    });

    it('changes with anything that shapes the images', async () => {
      const base = await chartSettingsHash(settings());
      expect(await chartSettingsHash(settings({ computedAtUtc: '2026-09-30T10:00:01Z' }))).not.toBe(base);
      expect(await chartSettingsHash(settings({ pricingBasis: 'AsRun' }))).not.toBe(base);
      expect(await chartSettingsHash(settings({ show: ['run:2', 'run:1'] }))).not.toBe(base);
      expect(await chartSettingsHash(settings({ highlight: ['run:1'] }))).not.toBe(base);
    });

    it('hashes the composition constants and every document type\'s layout', async () => {
      expect(documentChartHashLayout(DEFAULT_CHART_LAYOUT_SETTINGS)).toEqual({
        columnPt: 481.9, dpi: 300, baseLabelPx: 11, textNormalization: 1, format: 'png', documents: DEFAULT_CHART_LAYOUT_SETTINGS
      });
      const base = await chartSettingsHash(settings());
      const changed = (patch: Partial<ReportChartDocumentLayout>): ReportChartSettingsInput => settings({
        layout: documentChartHashLayout({
          ...DEFAULT_CHART_LAYOUT_SETTINGS, [InternalBrief]: { ...DEFAULT_DOCUMENT_CHART_LAYOUT, ...patch }
        })
      });
      for (const patch of [
        { labelPt: 9 }, { orientation: 'vertical' as const }, { heading: 'title' as const }, { logo: true },
        { theme: 'asInStep2' as const }, { maxHeightPercent: 50 }, { sideBySide: false }, { widths: { 'p1a-quality': 'twoThirds' as const } }
      ]) {
        expect(await chartSettingsHash(changed(patch)), JSON.stringify(patch)).not.toBe(base);
      }
      // A missing document type hashes as its defaults.
      expect(await chartSettingsHash(settings({ layout: documentChartHashLayout({}) }))).toBe(base);
    });

    it('moves with the document text normalization, so charts drawn before it read as differing from step 2', async () => {
      const { textNormalization, ...before } = documentChartHashLayout(DEFAULT_CHART_LAYOUT_SETTINGS) as Record<string, unknown>;
      expect(textNormalization).toBe(1);
      expect(await chartSettingsHash(settings({ layout: before }))).not.toBe(await chartSettingsHash(settings()));
    });
  });

  describe('documentTextStyle', () => {
    const styled = (bar: Partial<FigureStyle['bar']>, scatter: Partial<FigureStyle['scatter']> = {}): FigureStyle => ({
      ...DEFAULT_FIGURE_STYLE,
      bar: { ...DEFAULT_FIGURE_STYLE.bar, ...bar },
      scatter: { ...DEFAULT_FIGURE_STYLE.scatter, ...scatter }
    });

    it('prints the bar axis text at the base label size, the value labels and axis titles in proportion', () => {
      const bar = documentTextStyle(styled({ axisTextSizePx: 15, valueLabelSizePx: 20, axisTitleSizePx: 16 })).bar;
      expect([bar.axisTextSizePx, bar.valueLabelSizePx, bar.axisTitleSizePx]).toEqual([11, 14.7, 11.7]);
    });

    it('prints the scatter axis text at the base label size too', () => {
      const scatter = documentTextStyle(styled({}, { axisTextSizePx: 10 })).scatter;
      expect(scatter.axisTextSizePx).toBe(11);
      expect(scatter.labelTextSizePx).toBe(Math.round(DEFAULT_FIGURE_STYLE.scatter.labelTextSizePx * 1.1 * 10) / 10);
    });

    it('leaves the default style, the profile family and the chrome unchanged', () => {
      expect(documentTextStyle(DEFAULT_FIGURE_STYLE)).toEqual(DEFAULT_FIGURE_STYLE);
      const changed = documentTextStyle(styled({ axisTextSizePx: 15 }));
      expect(changed.profile).toBe(DEFAULT_FIGURE_STYLE.profile);
      expect(changed.appearance).toBe(DEFAULT_FIGURE_STYLE.appearance);
    });
  });

  describe('ReportChartPublisher', () => {
    let puts: { id: number; charts: ReportDocumentChartUpload[]; layout: ReportDocumentChartLayout | null | undefined }[];
    let composedAudiences: (BenchmarkReportAudience | undefined)[];
    let variants: ReportChartVariant[];
    let putResponse: (id: number, charts: ReportDocumentChartUpload[]) => Observable<ReportDocumentChartsSummaryDto>;
    let service: AdminBenchmarkService;
    let composed: string[];
    let rejectKeys: Set<string>;
    let onCompose: ((key: ReportChartFigureKey, variant: ReportChartVariant) => void) | null;

    const target = (documentId: number, audience: BenchmarkReportAudience, letters: Record<string, string> = { 'run:2': 'A' }): ReportChartTarget => ({
      documentId,
      audience,
      subjectKey: 'run:1',
      peerLetters: letters,
      label: `Document ${documentId}`
    });

    const compose = async (key: ReportChartFigureKey, variant: ReportChartVariant, audience?: BenchmarkReportAudience): Promise<ComposedReportChart> => {
      composed.push(`${key}/${variant.kind}`);
      composedAudiences.push(audience);
      variants.push(variant);
      onCompose?.(key, variant);
      if (rejectKeys.has(key)) {
        throw new Error(`cannot draw ${key}`);
      }
      return {
        png: new Blob([new Uint8Array([1, 2, 3])], { type: 'image/png' }),
        widthPx: 1800,
        heightPx: 1125,
        title: `Title ${key}`,
        caption: `Caption ${key}`,
        altText: `Alt ${key}`
      };
    };

    beforeEach(() => {
      puts = [];
      composed = [];
      composedAudiences = [];
      variants = [];
      rejectKeys = new Set();
      onCompose = null;
      putResponse = (id, charts) => of({ documentId: id, chartCount: charts.length, figureKeys: [], settingsHash: HASH });
      service = {
        putReportDocumentCharts: (id: number, charts: ReportDocumentChartUpload[], layout?: ReportDocumentChartLayout | null) => {
          puts.push({ id, charts, layout });
          return putResponse(id, charts);
        }
      } as unknown as AdminBenchmarkService;
    });

    it('composes each figure for its document type and sends the layout of that type for the figures drawn', async () => {
      rejectKeys = new Set(['p1b-speed']);
      const publisher = new ReportChartPublisher(service);
      const layout = {
        ...DEFAULT_CHART_LAYOUT_SETTINGS,
        [TechnicalReport]: { ...DEFAULT_DOCUMENT_CHART_LAYOUT, widths: { 'p1c-cost': 'twoThirds' as const }, maxHeightPercent: 50 }
      };
      await publisher.publish([target(11, ExecutiveSummary, {}), target(12, TechnicalReport, {})],
        { [ExecutiveSummary]: ['p1a-quality'], [TechnicalReport]: ['p1a-quality', 'p1b-speed', 'p1c-cost'] },
        compose, HASH, undefined, layout);

      expect(composedAudiences).toEqual([ExecutiveSummary, TechnicalReport, TechnicalReport, TechnicalReport]);
      expect(puts[0].layout).toEqual({ version: 1, figures: [{ key: 'p1a-quality', widthShare: 1, rowGroup: null }], maxHeightShare: 0.6 });
      // Speed failed to draw, so the layout names the two figures uploaded.
      expect(puts[1].layout).toEqual({
        version: 1,
        figures: [{ key: 'p1a-quality', widthShare: 1, rowGroup: null }, { key: 'p1c-cost', widthShare: 2 / 3, rowGroup: null }],
        maxHeightShare: 0.5
      });
    });

    it('sends no layout without one, so the server places the charts by its default', async () => {
      const publisher = new ReportChartPublisher(service);
      await publisher.publish([target(11, ExecutiveSummary)], { [ExecutiveSummary]: ['p1a-quality'] }, compose, HASH);

      expect(puts.length).toBe(1);
      expect(puts[0].layout).toBeNull();
    });

    it('draws a comparison-scope document over its covered entries, lettering every one of them', async () => {
      const publisher = new ReportChartPublisher(service);
      const letters = { 'run:1': 'A', 'run:2': 'B', 'run:3': 'C' };
      await publisher.publish([{
        documentId: 21, audience: ExecutiveSummary, subjectKey: 'comparison:12', peerLetters: letters, label: 'Comparison #12',
        coveredKeys: ['run:1', 'run:2', 'run:3']
      }], { [ExecutiveSummary]: ['p1a-quality'] }, compose, HASH, undefined, DEFAULT_CHART_LAYOUT_SETTINGS);

      expect(variants).toEqual([
        { kind: 'named', coveredKeys: ['run:1', 'run:2', 'run:3'] },
        { kind: 'anonymized', subjectKey: 'comparison:12', letters, coveredKeys: ['run:1', 'run:2', 'run:3'] }
      ]);
    });

    it('keeps a per-model document\'s variants as they were', async () => {
      const publisher = new ReportChartPublisher(service);
      await publisher.publish([target(11, ExecutiveSummary)], { [ExecutiveSummary]: ['p1a-quality'] }, compose, HASH);

      expect(variants).toEqual([
        { kind: 'named' },
        { kind: 'anonymized', subjectKey: 'run:1', letters: { 'run:2': 'A' } }
      ]);
    });

    it('charts one document at a time, in order: each selected figure named, then anonymized, then one PUT', async () => {
      const publisher = new ReportChartPublisher(service);
      const progress: ReportChartPublishProgress[] = [];
      const selection: ReportChartSelection = {
        [ExecutiveSummary]: ['s2-quality-cost', 'p1a-quality'],
        [InternalBrief]: ['p1b-speed']
      };

      const running = publisher.publish([target(11, ExecutiveSummary), target(12, InternalBrief, {})], selection, compose, HASH, p => progress.push(p));
      expect(publisher.running).toBe(true);
      const result = await running;

      expect(publisher.running).toBe(false);
      expect(composed).toEqual([
        'p1a-quality/named', 'p1a-quality/anonymized', 's2-quality-cost/named', 's2-quality-cost/anonymized',
        'p1b-speed/named'
      ]);
      expect(puts.map(put => put.id)).toEqual([11, 12]);
      expect(puts[0].charts.map(chart => `${chart.figureKey}/${chart.naming}`))
        .toEqual(['p1a-quality/named', 'p1a-quality/anonymized', 's2-quality-cost/named', 's2-quality-cost/anonymized']);
      expect(puts[0].charts[0]).toEqual({
        figureKey: 'p1a-quality',
        naming: 'named',
        title: 'Title p1a-quality',
        caption: 'Caption p1a-quality',
        altText: 'Alt p1a-quality',
        settingsHash: HASH,
        pngBase64: 'AQID'
      });
      expect(result).toEqual({
        published: [{ documentId: 11, chartCount: 4, figureCount: 2 }, { documentId: 12, chartCount: 1, figureCount: 1 }],
        failed: [],
        skipped: [],
        canceled: false,
        storageNotConfigured: null
      });

      // Four images and one upload, then one image and one upload.
      const last = progress[progress.length - 1];
      expect(last.total).toBe(7);
      expect(last.done).toBe(7);
      expect(last.documentId).toBeNull();
      expect(progress.some(p => p.documentId === 11 && p.step.startsWith('Drawing Intelligence (anonymized)'))).toBe(true);
      expect(progress.every((p, i) => i === 0 || p.done >= progress[i - 1].done)).toBe(true);
    });

    it('skips a document whose selection is empty, uploading nothing for it', async () => {
      const publisher = new ReportChartPublisher(service);
      const result = await publisher.publish([target(11, ExecutiveSummary), target(12, TechnicalReport)],
        { [ExecutiveSummary]: [], [TechnicalReport]: ['p1c-cost'] }, compose, HASH);

      expect(result.skipped).toEqual([11]);
      expect(puts.map(put => put.id)).toEqual([12]);
    });

    it('stops after the document in flight when canceled', async () => {
      const publisher = new ReportChartPublisher(service);
      onCompose = () => publisher.cancel();
      const result = await publisher.publish([target(11, ExecutiveSummary), target(12, ExecutiveSummary)],
        { [ExecutiveSummary]: ['p1a-quality'] }, compose, HASH);

      expect(puts.map(put => put.id)).toEqual([11]);
      expect(result.canceled).toBe(true);
      expect(result.published).toEqual([{ documentId: 11, chartCount: 2, figureCount: 1 }]);
      expect(publisher.running).toBe(false);
    });

    it('leaves out a figure the composer rejects, and fails a document none of whose figures could be drawn', async () => {
      rejectKeys = new Set(['p1b-speed']);
      const publisher = new ReportChartPublisher(service);
      const progress: ReportChartPublishProgress[] = [];
      const result = await publisher.publish([target(11, ExecutiveSummary, {}), target(12, InternalBrief, {})],
        { [ExecutiveSummary]: ['p1a-quality', 'p1b-speed'], [InternalBrief]: ['p1b-speed'] }, compose, HASH,
        p => progress.push(p));

      expect(puts.map(put => put.id)).toEqual([11]);
      expect(puts[0].charts.map(chart => chart.figureKey)).toEqual(['p1a-quality']);
      expect(progress.some(p => p.step.includes('Could not draw Speed (named)') && p.step.includes('cannot draw p1b-speed'))).toBe(true);
      expect(result.published).toEqual([{ documentId: 11, chartCount: 1, figureCount: 1 }]);
      expect(result.failed.length).toBe(1);
      expect(result.failed[0].documentId).toBe(12);
      expect(result.failed[0].message).toContain('No chart could be drawn');
    });

    it('records a failed upload and carries on with the next document', async () => {
      putResponse = (id, charts) => id === 11
        ? throwError(() => new HttpErrorResponse({ status: 500, statusText: 'Server Error', error: { error: 'Disk full.' } }))
        : of({ documentId: id, chartCount: charts.length, figureKeys: [], settingsHash: HASH });
      const publisher = new ReportChartPublisher(service);
      const result = await publisher.publish([target(11, ExecutiveSummary), target(12, ExecutiveSummary)],
        { [ExecutiveSummary]: ['p1a-quality'] }, compose, HASH);

      expect(puts.map(put => put.id)).toEqual([11, 12]);
      expect(result.failed).toEqual([{ documentId: 11, message: 'Disk full.' }]);
      expect(result.published).toEqual([{ documentId: 12, chartCount: 2, figureCount: 1 }]);
      expect(result.storageNotConfigured).toBeNull();
    });

    it('counts figures from the distinct uploaded keys when the summary lists none', async () => {
      const publisher = new ReportChartPublisher(service);
      const result = await publisher.publish([target(11, ExecutiveSummary)],
        { [ExecutiveSummary]: ['p1a-quality', 'p1b-speed', 'p1c-cost'] }, compose, HASH);

      expect(result.published).toEqual([{ documentId: 11, chartCount: 6, figureCount: 3 }]);
    });

    it('counts figures from the summary\'s figure keys when it lists them', async () => {
      putResponse = (id, charts) => of({ documentId: id, chartCount: charts.length, figureKeys: ['p1a-quality', 'p1b-speed'], settingsHash: HASH });
      const publisher = new ReportChartPublisher(service);
      const result = await publisher.publish([target(11, ExecutiveSummary)],
        { [ExecutiveSummary]: ['p1a-quality', 'p1b-speed', 'p1c-cost'] }, compose, HASH);

      expect(result.published).toEqual([{ documentId: 11, chartCount: 6, figureCount: 2 }]);
    });

    it('stops the whole publish on a 400 saying chart storage is not configured, and reports it once', async () => {
      const message = `${CHART_STORAGE_NOT_CONFIGURED}. Set ReportCharts:Location.`;
      putResponse = () => throwError(() => new HttpErrorResponse({ status: 400, statusText: 'Bad Request', error: { error: message } }));
      const publisher = new ReportChartPublisher(service);
      const progress: ReportChartPublishProgress[] = [];
      const result = await publisher.publish([target(11, ExecutiveSummary), target(12, ExecutiveSummary), target(13, TechnicalReport)],
        { [ExecutiveSummary]: ['p1a-quality'], [TechnicalReport]: ['p1a-quality'] }, compose, HASH, p => progress.push(p));

      expect(puts.map(put => put.id)).toEqual([11]);
      expect(result.storageNotConfigured).toBe(message);
      expect(result.failed).toEqual([]);
      expect(result.published).toEqual([]);
      expect(result.canceled).toBe(false);
      expect(progress.filter(p => p.step === message).length).toBe(1);
    });

    it('treats any other 400 as a failure of that document alone', async () => {
      putResponse = (id, charts) => id === 11
        ? throwError(() => new HttpErrorResponse({ status: 400, statusText: 'Bad Request', error: { error: 'A stand-alone document takes no charts.' } }))
        : of({ documentId: id, chartCount: charts.length, figureKeys: [], settingsHash: HASH });
      const publisher = new ReportChartPublisher(service);
      const result = await publisher.publish([target(11, ExecutiveSummary), target(12, ExecutiveSummary)],
        { [ExecutiveSummary]: ['p1a-quality'] }, compose, HASH);

      expect(result.failed).toEqual([{ documentId: 11, message: 'A stand-alone document takes no charts.' }]);
      expect(result.published.map(p => p.documentId)).toEqual([12]);
      expect(result.storageNotConfigured).toBeNull();
    });

    it('refuses a second publish while one runs', async () => {
      const publisher = new ReportChartPublisher(service);
      const first = publisher.publish([target(11, ExecutiveSummary)], { [ExecutiveSummary]: ['p1a-quality'] }, compose, HASH);
      await expect(publisher.publish([target(12, ExecutiveSummary)], { [ExecutiveSummary]: ['p1a-quality'] }, compose, HASH)).rejects.toThrowError(/already running/);
      await first;
    });
  });

  describe('chart targets', () => {
    const doc = (overrides: Partial<BenchmarkReportDocumentListItemDto>): BenchmarkReportDocumentListItemDto => ({
      id: 7, audience: TechnicalReport, title: 'Report 7', subjectKey: 'run:1', peerLetters: { 'run:2': 'A' },
      ...overrides
    } as BenchmarkReportDocumentListItemDto);

    it('makes a per-model document a target without covered entries', () => {
      const target = reportChartTargetFor(doc({}));
      expect(target).toEqual({ documentId: 7, audience: TechnicalReport, subjectKey: 'run:1', peerLetters: { 'run:2': 'A' }, label: 'Report 7' });
      expect(reportChartCoveredKeys(target)).toBeNull();
      expect(reportChartTargetFor(doc({}), 'Row label').label).toBe('Row label');
    });

    it('gives a comparison-scope document its covered entries, from the covered models or else its letters', () => {
      const covered = reportChartTargetFor(doc({
        scope: BenchmarkReportScope.Comparison, subjectKey: 'comparison:12',
        peerLetters: { 'run:1': 'A', 'run:2': 'B' },
        coveredModels: [{ entryKey: 'run:1', label: 'One', provider: 'Google' }, { entryKey: 'run:2', label: 'Two', provider: null }]
      }));
      expect(covered.coveredKeys).toEqual(['run:1', 'run:2']);
      expect(reportChartCoveredKeys(covered)).toEqual(['run:1', 'run:2']);

      const lettered = reportChartTargetFor(doc({ subjectKey: 'comparison:12/0123456789abcdef', peerLetters: { 'group:4': 'A', 'run:9': 'B' } }));
      expect(lettered.coveredKeys).toEqual(['group:4', 'run:9']);
    });

    it('reads a comparison-scope target built without covered entries from its letters', () => {
      const target: ReportChartTarget = {
        documentId: 1, audience: ExecutiveSummary, subjectKey: 'comparison:3', peerLetters: { 'run:5': 'A', 'run:6': 'B' }, label: 'x'
      };
      expect(reportChartCoveredKeys(target)).toEqual(['run:5', 'run:6']);
    });
  });
});
