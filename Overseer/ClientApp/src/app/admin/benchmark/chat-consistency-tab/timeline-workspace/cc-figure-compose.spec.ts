import { Chart } from 'chart.js';

import { APP_CHART_REGISTRABLES } from '../../../../chart-registrables';
import { FigureSizeSettings, defaultFigureSize, sizeErrors } from '../../model-comparison/figure-size';
import { DEFAULT_FIGURE_STYLE, TimelineFigureStyle } from '../../model-comparison/figure-style';
import { resolveFigureTheme } from '../../model-comparison/figure-theme';
import { CC_FIGURE_KEYS, CcFigureInput, CcFigureKey } from '../chat-consistency-charts';
import { ccEventTimeline } from '../chat-consistency-tab.testing';
import { ccPreviewLayout } from './cc-chart-zoom';
import {
  CcComposeContext,
  buildComposedCcFigure,
  ccComposedChartOptions,
  ccFigureChrome,
  ccFigureFooter,
  ccFigureLayout,
  ccFigureRequest,
  ccFigureSummary,
  ccMarkerNoteText,
  ccNotAnalyzedNoteText,
  composeCcFigure
} from './cc-figure-compose';

/** A size at 100 % density and 100 % text, with overrides. */
function size(overrides: Partial<FigureSizeSettings> = {}): FigureSizeSettings {
  return { ...defaultFigureSize(1), ...overrides };
}

function context(overrides: Partial<CcComposeContext> = {}, timeline: Partial<TimelineFigureStyle> = {}): CcComposeContext {
  return {
    style: { appearance: DEFAULT_FIGURE_STYLE.appearance, timeline: { ...DEFAULT_FIGURE_STYLE.timeline, ...timeline } },
    theme: resolveFigureTheme(DEFAULT_FIGURE_STYLE.appearance),
    logo: null,
    modelLabel: 'GPT-5 high',
    unitNoun: 'run',
    datesLabel: 'All dates',
    set: { kind: 'battery', label: 'Two initial suites (revision 1)' },
    loadedAt: '2026-10-09T08:00:00Z',
    hiddenSeries: new Set<string>(),
    zeroBaseline: false,
    decimals: {},
    ...overrides
  };
}

function input(overrides: Partial<CcFigureInput> = {}): CcFigureInput {
  const timeline = ccEventTimeline();
  return { points: timeline.points, events: timeline.events, annotations: timeline.annotations, ...overrides };
}

const KEYS: CcFigureKey[] = CC_FIGURE_KEYS.map(entry => entry.key);

describe('cc-figure-compose', () => {
  describe('ccFigureChrome', () => {
    it('heads every chart with its title and the model, run count and dates badges', () => {
      for (const key of KEYS) {
        const figure = buildComposedCcFigure(key, input(), context());
        const chrome = ccFigureChrome(figure, context());
        expect(chrome.title).toBe(figure.title);
        expect(chrome.badges.map(badge => [badge.kind, badge.text, badge.tone]))
          .toEqual([['model', 'GPT-5 high', 'neutral'], ['runs', expect.stringMatching(/^\d+ runs?$/), 'neutral'], ['dates', 'All dates', 'neutral']]);
        expect([chrome.detail, chrome.highlight, chrome.key]).toEqual(['', '', []]);
      }
      const quality = ccFigureChrome(buildComposedCcFigure('quality', input(), context()), context());
      expect(quality.badges[1].text).toBe('6 runs');
      const battery = ccFigureChrome(buildComposedCcFigure('quality', input(), context()), context({ unitNoun: 'battery run' }));
      expect(battery.badges[1].text).toBe('6 battery runs');
    });

    it('gives a Better badge where the chart has a better direction, and none to the work charts and the overview', () => {
      const direction = (key: CcFigureKey) => ccFigureChrome(buildComposedCcFigure(key, input(), context()), context()).direction;
      expect(direction('quality')).toEqual({ y: 'top', label: 'Better' });
      expect(direction('ttfat')).toEqual({ y: 'bottom', label: 'Better' });
      expect(direction('rate')).toEqual({ y: 'top', label: 'Better' });
      expect(direction('cost')).toEqual({ y: 'bottom', label: 'Better' });
      expect(direction('reliability')).toEqual({ y: 'bottom', label: 'Better' });
      expect(direction('work')).toBeUndefined();
      expect(direction('tools')).toBeUndefined();
      expect(direction('timeline')).toBeUndefined();
    });

    it('leaves out the hidden badge kinds and the Better badge', () => {
      const hidden = context({}, { hiddenBadges: ['dates', 'direction'] });
      const chrome = ccFigureChrome(buildComposedCcFigure('quality', input(), hidden), hidden);
      expect(chrome.badges.map(badge => badge.kind)).toEqual(['model', 'runs']);
      expect(chrome.direction).toBeUndefined();
    });

    it('explains the markers and the gray crosses as image notes while their switches are on', () => {
      const marked = input({ notAnalyzed: new Map([[201, 'before the first run'], [204, 'left out in step 1']]) });
      const figure = buildComposedCcFigure('quality', marked, context());
      expect(ccMarkerNoteText(figure)).toBe('Markers: E1–E4 Overseer changes · A1–A2 annotations · S1–S2 served-model changes');
      expect(ccNotAnalyzedNoteText(figure)).toBe('2 runs not in the analysis are drawn as gray crosses.');
      // The takeaway keeps its own copy.
      expect(figure.takeaway).toContain(ccNotAnalyzedNoteText(figure));
      expect(ccFigureChrome(figure, context()).notes).toEqual([
        { text: 'Markers: E1–E4 Overseer changes · A1–A2 annotations · S1–S2 served-model changes', tone: 'info' },
        { text: '2 runs not in the analysis are drawn as gray crosses.', tone: 'info' }
      ]);
      const off = context({}, { markerNote: false, notAnalyzedNote: false });
      expect(ccFigureChrome(figure, off).notes).toEqual([]);
      // Without markers or marked points there is nothing to explain.
      const plain = buildComposedCcFigure('quality', input({ events: [], annotations: [] }), context({}, {}));
      expect(ccMarkerNoteText(buildComposedCcFigure('quality', { points: [ccEventTimeline().points[5]] }, context()))).toBe('');
      expect(ccNotAnalyzedNoteText(plain)).toBe('');
    });

    it('reads the badges, the direction and the notes as one summary', () => {
      const figure = buildComposedCcFigure('quality', input(), context());
      const summary = ccFigureSummary(ccFigureChrome(figure, context()));
      expect(summary).toBe('GPT-5 high, 6 runs, All dates, Better toward the top. '
        + 'Markers: E1–E4 Overseer changes · A1–A2 annotations · S1–S2 served-model changes');
    });
  });

  describe('ccFigureFooter', () => {
    it('names the compared set by its kind, and the load time', () => {
      const battery = ccFigureFooter(context());
      expect(battery.label).toBe('Battery');
      expect(battery.suite).toBe('Two initial suites (revision 1)');
      expect(battery.computedAt).not.toBe('unknown time');
      expect(ccFigureFooter(context({ set: { kind: 'suite', label: 'Board Suite' } }))).toEqual(
        { label: 'Suite', suite: 'Board Suite', computedAt: battery.computedAt });
      expect(ccFigureFooter(context({ set: null }))).toEqual({ label: 'Suites', suite: 'All suites', computedAt: battery.computedAt });
    });

    it('is empty while the footer is off', () => {
      expect(ccFigureFooter(context({}, { footer: false }))).toEqual({ suite: '', computedAt: '' });
    });
  });

  describe('ccComposedChartOptions', () => {
    it('draws no header band, logo, animation or tooltip, in the mapped theme and the timeline style', () => {
      const options = ccComposedChartOptions(context({}, { lineWidthPx: 3 }));
      expect(options.header).toEqual({ title: null, subject: null });
      expect(options.logo).toBeNull();
      expect(options.reducedMotion).toBe(true);
      expect(options.theme!.tooltip).toBeNull();
      expect(options.theme!.background).toBeNull();
      expect(options.style!.lineWidthPx).toBe(3);
      expect(options.style!.labelWeight).toBe(DEFAULT_FIGURE_STYLE.appearance.labelWeight);
    });
  });

  describe('ccFigureLayout', () => {
    const figure = () => buildComposedCcFigure('quality', input(), context());

    it('lays the chart out with its heading and footer, at the file\'s exact pixels', () => {
      const { layout, refusal } = ccFigureLayout(figure(), context(), size());
      expect(refusal).toBeNull();
      expect([layout!.layoutWidth, layout!.layoutHeight, layout!.pixelWidth, layout!.pixelHeight]).toEqual([960, 540, 1920, 1080]);
      expect(layout!.plotHeight).toBeLessThan(540);
    });

    it('keeps the pixel size and shrinks the layout box at 140 % text', () => {
      const plain = ccFigureLayout(figure(), context(), size()).layout!;
      const large = ccFigureLayout(figure(), context(), size({ textScalePercent: 140 })).layout!;
      expect([large.pixelWidth, large.pixelHeight]).toEqual([plain.pixelWidth, plain.pixelHeight]);
      expect(large.layoutWidth).toBeLessThan(plain.layoutWidth);
      expect(large.layoutWidth).toBeCloseTo(960 / 1.4, 6);
    });

    it('refuses a size error in its own words, and a size too short for the heading and footer', () => {
      const tiny = size({ resolutionId: 'custom', customWidthPx: 100 });
      expect(ccFigureLayout(figure(), context(), tiny)).toEqual({ layout: null, refusal: sizeErrors(tiny, 'chart').any });
      const short = ccFigureLayout(figure(), context(),
        size({ resolutionId: 'custom', customWidthPx: 3200, customHeightPx: 320, textScalePercent: 250 }));
      expect(short.layout).toBeNull();
      expect(short.refusal).toContain('does not fit 3200 × 320 px');
    });
  });

  describe('the screen and the download', () => {
    beforeEach(() => Chart.register(...APP_CHART_REGISTRABLES));

    it('compose the same request apart from the density and the pixels', () => {
      const ctx = context();
      const figure = buildComposedCcFigure('quality', input(), ctx);
      const target = ccFigureLayout(figure, ctx, size({ densitySelection: 2 })).layout!;
      const preview = ccPreviewLayout(target, 0.4, 2)!.layout;
      const file = ccFigureRequest(figure, ctx, target)!;
      const screen = ccFigureRequest(figure, ctx, preview)!;

      const { layout: fileLayout, ...fileRest } = file.request;
      const { layout: screenLayout, ...screenRest } = screen.request;
      expect(screenRest).toEqual(fileRest);
      expect(screen.config).toBe(file.config);
      const shape = (layout: typeof target) =>
        [layout.layoutWidth, layout.layoutHeight, layout.plotWidth, layout.plotHeight];
      expect(shape(screenLayout!)).toEqual(shape(fileLayout!));
      expect([fileLayout!.pixelWidth, fileLayout!.pixelHeight]).toEqual([3840, 2160]);
      expect([screenLayout!.pixelWidth, screenLayout!.pixelHeight]).toEqual([1536, 864]);

      // A figure built again from the same data and context draws the same chart.
      const again = buildComposedCcFigure('quality', input(), ctx);
      expect(ccFigureRequest(again, ctx, target)!.request).toEqual(file.request);
      expect(JSON.stringify(again.config!.data)).toBe(JSON.stringify(figure.config!.data));
    });

    it('composes a bitmap of the layout\'s pixels, and nothing for a chart with nothing to draw', async () => {
      const ctx = context();
      const figure = buildComposedCcFigure('quality', input(), ctx);
      const target = ccFigureLayout(figure, ctx, size()).layout!;
      const canvas = await composeCcFigure(figure, ctx, target);
      expect([canvas!.width, canvas!.height]).toEqual([1920, 1080]);
      const preview = ccPreviewLayout(target, 0.5, 1)!.layout;
      const small = await composeCcFigure(figure, ctx, preview);
      expect([small!.width, small!.height]).toEqual([960, 540]);

      const empty = buildComposedCcFigure('quality', { points: [] }, ctx);
      expect(empty.config).toBeNull();
      expect(await composeCcFigure(empty, ctx, target)).toBeNull();
      expect(ccFigureRequest(empty, ctx, target)).toBeNull();
    });
  });
});
