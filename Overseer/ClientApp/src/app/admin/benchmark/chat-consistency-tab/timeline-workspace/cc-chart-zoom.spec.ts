import { FigureSizeSettings, defaultFigureSize } from '../../model-comparison/figure-size';
import {
  CC_MAX_CANVAS_PIXELS,
  CC_ZOOM_FLOOR,
  CC_ZOOM_MAX,
  CcChartBox,
  ccCanvasRatio,
  ccChartBox,
  ccFitHeightZoom,
  ccFitScreenZoom,
  ccFitWidthZoom,
  ccResolveZoom,
  ccZoomRange
} from './cc-chart-zoom';

/** A size at 100 % density and 100 % text, with overrides. */
function size(overrides: Partial<FigureSizeSettings> = {}): FigureSizeSettings {
  return { ...defaultFigureSize(1), ...overrides };
}

/** A4 portrait at 300 dpi, laid out 960 px wide: 3508 × 960 / 2480. */
const A4_PORTRAIT_HEIGHT = (3508 * 960) / 2480;

describe('cc-chart-zoom', () => {
  describe('ccChartBox', () => {
    it('is the download layout box: Full HD at 100 % text is 960 × 540', () => {
      expect(ccChartBox(size())).toEqual({ width: 960, height: 540 });
    });

    it('ignores the pixel density', () => {
      expect(ccChartBox(size({ densitySelection: 2 }))).toEqual({ width: 960, height: 540 });
      expect(ccChartBox(size({ densitySelection: 'custom', customDensityPercent: 175 }))).toEqual({ width: 960, height: 540 });
    });

    it('grows a 21:9 box wider at the same height', () => {
      const box = ccChartBox(size({ resolutionId: 'uw1080' }));
      expect(box.width).toBeCloseTo(1280, 6);
      expect(box.height).toBeCloseTo(540, 6);
    });

    it('grows an A4 portrait box taller at the same width', () => {
      const box = ccChartBox(size({ resolutionId: 'a4p' }));
      expect(box.width).toBeCloseTo(960, 6);
      expect(box.height).toBeCloseTo(1357.94, 1);
      expect(box.height).toBeCloseTo(A4_PORTRAIT_HEIGHT, 6);
    });

    it('shrinks the box for a larger text size', () => {
      const box = ccChartBox(size({ textScalePercent: 150 }));
      expect(box.width).toBeCloseTo(640, 6);
      expect(box.height).toBeCloseTo(360, 6);
      const small = ccChartBox(size({ textScalePercent: 50 }));
      expect(small.width).toBeCloseTo(1920, 6);
      expect(small.height).toBeCloseTo(1080, 6);
    });

    it('lays a custom size out at its own aspect ratio', () => {
      const box = ccChartBox(size({ resolutionId: 'custom', customWidthPx: 1200, customHeightPx: 1200 }));
      expect(box.width).toBeCloseTo(960, 6);
      expect(box.height).toBeCloseTo(960, 6);
    });
  });

  describe('ccZoomRange', () => {
    it('runs from 25 % to 400 %, the floor following a lower fit', () => {
      expect(CC_ZOOM_FLOOR).toBe(0.25);
      expect(CC_ZOOM_MAX).toBe(4);
      expect(ccZoomRange(1)).toEqual({ min: 0.25, max: 4 });
      expect(ccZoomRange(0.1)).toEqual({ min: 0.1, max: 4 });
      expect(ccZoomRange(Number.NaN)).toEqual({ min: 0.25, max: 4 });
      expect(ccZoomRange(0)).toEqual({ min: 0.25, max: 4 });
    });
  });

  describe('fits', () => {
    const fullHd: CcChartBox = { width: 960, height: 540 };
    const ultrawide: CcChartBox = { width: 1280, height: 540 };
    const a4: CcChartBox = { width: 960, height: A4_PORTRAIT_HEIGHT };
    const largeText: CcChartBox = { width: 640, height: 360 };

    it('fits the width', () => {
      expect(ccFitWidthZoom(fullHd, 1440)).toBeCloseTo(1.5, 9);
      expect(ccFitWidthZoom(ultrawide, 960)).toBeCloseTo(0.75, 9);
      expect(ccFitWidthZoom(a4, 960)).toBeCloseTo(1, 9);
      expect(ccFitWidthZoom(largeText, 1280)).toBeCloseTo(2, 9);
    });

    it('fits the height less the chrome', () => {
      expect(ccFitHeightZoom(fullHd, 1000, 190)).toBeCloseTo(1.5, 9);
      expect(ccFitHeightZoom(ultrawide, 1000, 190)).toBeCloseTo(1.5, 9);
      expect(ccFitHeightZoom(a4, 1000, 190)).toBeCloseTo(810 / A4_PORTRAIT_HEIGHT, 9);
      expect(ccFitHeightZoom(largeText, 1000, 280)).toBeCloseTo(2, 9);
      // An unmeasured or negative chrome counts as none.
      expect(ccFitHeightZoom(fullHd, 1080, Number.NaN)).toBeCloseTo(2, 9);
      expect(ccFitHeightZoom(fullHd, 1080, -50)).toBeCloseTo(2, 9);
    });

    it('fits the screen at the smaller of the two', () => {
      expect(ccFitScreenZoom(fullHd, 1440, 1000, 190)).toBeCloseTo(1.5, 9);
      expect(ccFitScreenZoom(ultrawide, 1280, 1000, 190)).toBeCloseTo(1, 9);
      expect(ccFitScreenZoom(a4, 1440, 1000, 190)).toBeCloseTo(810 / A4_PORTRAIT_HEIGHT, 9);
      expect(ccFitScreenZoom(largeText, 1920, 1000, 280)).toBeCloseTo(2, 9);
    });

    it('caps a fit at 400 % and lets it fall below 25 %', () => {
      expect(ccFitWidthZoom(fullHd, 9600)).toBe(CC_ZOOM_MAX);
      expect(ccFitHeightZoom(fullHd, 9000, 0)).toBe(CC_ZOOM_MAX);
      expect(ccFitWidthZoom(fullHd, 96)).toBeCloseTo(0.1, 9);
      expect(ccFitScreenZoom(a4, 1440, 300, 190)).toBeCloseTo(110 / A4_PORTRAIT_HEIGHT, 9);
    });

    it('falls back to the floor where there is no room to fit into', () => {
      expect(ccFitWidthZoom(fullHd, 0)).toBe(CC_ZOOM_FLOOR);
      expect(ccFitHeightZoom(fullHd, 100, 190)).toBe(CC_ZOOM_FLOOR);
      expect(ccFitWidthZoom(fullHd, Number.NaN)).toBe(CC_ZOOM_FLOOR);
      expect(ccFitWidthZoom({ width: 0, height: 0 }, 960)).toBe(CC_ZOOM_FLOOR);
    });
  });

  describe('ccResolveZoom', () => {
    const fits = { width: 1.5, height: 2, screen: 1.5 };

    it('resolves each fit view to its fit', () => {
      expect(ccResolveZoom('fitWidth', fits)).toBe(1.5);
      expect(ccResolveZoom('fitHeight', fits)).toBe(2);
      expect(ccResolveZoom('fitScreen', fits)).toBe(1.5);
    });

    it('clamps an explicit zoom into the range', () => {
      expect(ccResolveZoom(1, fits)).toBe(1);
      expect(ccResolveZoom(0.5, fits)).toBe(0.5);
      expect(ccResolveZoom(10, fits)).toBe(CC_ZOOM_MAX);
      expect(ccResolveZoom(0.05, fits)).toBe(CC_ZOOM_FLOOR);
      expect(ccResolveZoom(Number.NaN, fits)).toBe(CC_ZOOM_FLOOR);
    });

    it('lowers the floor to the lowest fit, so a fit below 25 % stays reachable', () => {
      const narrow = { width: 0.1, height: 0.5, screen: 0.1 };
      expect(ccResolveZoom('fitWidth', narrow)).toBeCloseTo(0.1, 9);
      expect(ccResolveZoom('fitScreen', narrow)).toBeCloseTo(0.1, 9);
      expect(ccResolveZoom(0.05, narrow)).toBeCloseTo(0.1, 9);
    });

    it('keeps the floor where no fit is usable', () => {
      const none = { width: Number.NaN, height: 0, screen: Number.NaN };
      expect(ccResolveZoom('fitWidth', none)).toBe(CC_ZOOM_FLOOR);
      expect(ccResolveZoom(2, none)).toBe(2);
    });
  });

  describe('ccCanvasRatio', () => {
    it('draws at the device ratio while the canvas stays under the pixel cap', () => {
      expect(CC_MAX_CANVAS_PIXELS).toBe(8_000_000);
      expect(ccCanvasRatio(960, 540, 2)).toBe(2);
      expect(ccCanvasRatio(10, 10, 1.25)).toBe(1.25);
    });

    it('lowers the ratio so a large canvas holds at most the cap in device pixels', () => {
      const ratio = ccCanvasRatio(3840, 2160, 2);
      expect(ratio).toBeLessThan(2);
      expect(3840 * ratio * 2160 * ratio).toBeCloseTo(CC_MAX_CANVAS_PIXELS, 0);
      // Never above the device's own ratio, however small the canvas.
      expect(ccCanvasRatio(1920, 1080, 1)).toBe(1);
    });

    it('reads an unusable device ratio as 1 and an empty canvas as uncapped', () => {
      expect(ccCanvasRatio(100, 100, 0)).toBe(1);
      expect(ccCanvasRatio(100, 100, Number.NaN)).toBe(1);
      expect(ccCanvasRatio(0, 100, 2)).toBe(2);
      expect(ccCanvasRatio(Number.NaN, 100, 2)).toBe(2);
    });
  });
});
