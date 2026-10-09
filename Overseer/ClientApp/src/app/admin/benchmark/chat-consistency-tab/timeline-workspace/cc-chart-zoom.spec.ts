import { FigureExportLayout } from '../../model-comparison/figure-export';
import { FigureSizeSettings, defaultFigureSize } from '../../model-comparison/figure-size';
import { PREVIEW_MAX_ZOOM, PREVIEW_MIN_ZOOM_FLOOR } from '../../model-comparison/preview-view';
import {
  CcTargetPixels,
  ccDisplaySize,
  ccFitHeightZoom,
  ccFitScreenZoom,
  ccFitWidthWithScrollbar,
  ccFitWidthZoom,
  ccPreviewDpr,
  ccPreviewLayout,
  ccResolveZoom,
  ccTargetPixels,
  ccZoomRange
} from './cc-chart-zoom';

/** A size at 100 % density and 100 % text, with overrides. */
function size(overrides: Partial<FigureSizeSettings> = {}): FigureSizeSettings {
  return { ...defaultFigureSize(1), ...overrides };
}

const NO_CHROME = { width: 0, height: 0 };

describe('cc-chart-zoom', () => {
  describe('ccTargetPixels', () => {
    it('is the resolution at its density: Full HD at 200 % is 3840 × 2160', () => {
      expect(ccTargetPixels(size())).toEqual({ pixelWidth: 1920, pixelHeight: 1080 });
      expect(ccTargetPixels(size({ densitySelection: 2 }))).toEqual({ pixelWidth: 3840, pixelHeight: 2160 });
      expect(ccTargetPixels(size({ resolutionId: 'a4p' }))).toEqual({ pixelWidth: 2480, pixelHeight: 3508 });
    });

    it('ignores the text size, which changes only the layout box', () => {
      expect(ccTargetPixels(size({ textScalePercent: 150 }))).toEqual({ pixelWidth: 1920, pixelHeight: 1080 });
    });

    it('is null for a refused size', () => {
      expect(ccTargetPixels(size({ resolutionId: 'custom', customWidthPx: 10, customHeightPx: 10 }))).toBeNull();
    });
  });

  describe('ccPreviewDpr', () => {
    it('keeps the display ratio between 1 and 4', () => {
      expect(ccPreviewDpr(2)).toBe(2);
      expect(ccPreviewDpr(0.5)).toBe(1);
      expect(ccPreviewDpr(6)).toBe(4);
      expect(ccPreviewDpr(Number.NaN)).toBe(1);
    });
  });

  describe('fits', () => {
    const fullHd: CcTargetPixels = { pixelWidth: 1920, pixelHeight: 1080 };
    const fullHd2x: CcTargetPixels = { pixelWidth: 3840, pixelHeight: 2160 };
    const a4: CcTargetPixels = { pixelWidth: 2480, pixelHeight: 3508 };
    const chrome = { width: 26, height: 120 };

    it('fits the width against the target pixels and the device ratio, less the HTML across', () => {
      expect(ccFitWidthZoom(fullHd, 986, chrome, 1)).toBeCloseTo(0.5, 9);
      // 100 % on a 2× display is 960 CSS px wide for a 1920 px file.
      expect(ccFitWidthZoom(fullHd, 986, chrome, 2)).toBeCloseTo(1, 9);
      expect(ccFitWidthZoom(fullHd2x, 1946, chrome, 2)).toBeCloseTo(1, 9);
    });

    it('fits the height less the HTML under the image', () => {
      expect(ccFitHeightZoom(fullHd, 660, chrome, 1)).toBeCloseTo(0.5, 9);
      expect(ccFitHeightZoom(a4, 120 + 3508 / 4, chrome, 2)).toBeCloseTo(0.5, 9);
      // An unmeasured or negative chrome counts as none.
      expect(ccFitHeightZoom(fullHd, 540, { width: Number.NaN, height: -50 }, 1)).toBeCloseTo(0.5, 9);
    });

    it('fits the screen at the smaller of the two', () => {
      expect(ccFitScreenZoom(fullHd, 986, 2000, chrome, 1)).toBeCloseTo(0.5, 9);
      expect(ccFitScreenZoom(a4, 3000, 120 + 877, chrome, 1)).toBeCloseTo(877 / 3508, 9);
    });

    it('caps a fit at 800 % and lets it fall below 10 %', () => {
      expect(ccFitWidthZoom({ pixelWidth: 100, pixelHeight: 100 }, 10_000, NO_CHROME, 1)).toBe(PREVIEW_MAX_ZOOM);
      expect(ccFitWidthZoom(fullHd, 96, NO_CHROME, 1)).toBeCloseTo(0.05, 9);
    });

    it('is not a number where there is no room to fit into', () => {
      expect(ccFitWidthZoom(fullHd, 20, chrome, 1)).toBeNaN();
      expect(ccFitHeightZoom(fullHd, 100, chrome, 1)).toBeNaN();
      expect(ccFitWidthZoom({ pixelWidth: 0, pixelHeight: 0 }, 960, NO_CHROME, 1)).toBeNaN();
    });

    it('recomputes Fit width against the narrower box once a vertical scrollbar will appear', () => {
      // Fitted to 986 px, the figure is 540 + 120 px tall: it fits 700 px, so no scrollbar comes.
      expect(ccFitWidthWithScrollbar(fullHd, 986, 700, chrome, 1, 15)).toBeCloseTo(0.5, 9);
      // In 600 px it does not: the scrollbar takes 15 px, and the fit is taken against 971 px.
      const narrower = ccFitWidthWithScrollbar(fullHd, 986, 600, chrome, 1, 15);
      expect(narrower).toBeCloseTo(945 / 1920, 9);
      expect(ccDisplaySize(fullHd, narrower, 1, true).cssWidth + chrome.width).toBeLessThanOrEqual(986 - 15);
      // An unknown scrollbar width keeps the plain fit.
      expect(ccFitWidthWithScrollbar(fullHd, 986, 600, chrome, 1, 0)).toBeCloseTo(0.5, 9);
    });
  });

  describe('ccDisplaySize', () => {
    const fullHd: CcTargetPixels = { pixelWidth: 1920, pixelHeight: 1080 };

    it('shows one file pixel on one device pixel at 100 %', () => {
      expect(ccDisplaySize(fullHd, 1, 2, false)).toEqual({ cssWidth: 960, cssHeight: 540 });
      expect(ccDisplaySize({ pixelWidth: 3840, pixelHeight: 2160 }, 1, 2, false)).toEqual({ cssWidth: 1920, cssHeight: 1080 });
    });

    it('floors a fit to whole CSS px and keeps an explicit zoom exact', () => {
      const zoom = 1000 / 1920;
      const fit = ccDisplaySize(fullHd, zoom, 1, true);
      expect(fit).toEqual({ cssWidth: 1000, cssHeight: 562 });
      const exact = ccDisplaySize(fullHd, zoom, 1, false);
      expect(exact.cssHeight).toBeCloseTo(562.5, 9);
    });

    it('never floors a whole box a pixel short through float noise', () => {
      const zoom = 0.1 * 3;
      expect(ccDisplaySize({ pixelWidth: 1000, pixelHeight: 1000 }, zoom, 1, true).cssWidth).toBe(300);
    });
  });

  describe('ccPreviewLayout', () => {
    const target: FigureExportLayout = {
      layoutWidth: 960, layoutHeight: 540, plotWidth: 920, plotHeight: 400,
      density: 2, pixelWidth: 1920, pixelHeight: 1080
    };

    it('keeps the composition and changes only the density and the pixels', () => {
      const fit = ccPreviewLayout(target, 0.5, 2)!;
      expect(fit.zoom).toBeCloseTo(0.5, 9);
      expect(fit.cssWidth).toBeCloseTo(480, 9);
      expect(fit.layout).toEqual({ ...target, density: 960 / 960, pixelWidth: 960, pixelHeight: 540 });
    });

    it('rasterizes at the file pixels from 100 % up', () => {
      const fit = ccPreviewLayout(target, 2, 1)!;
      expect(fit.layout.pixelWidth).toBe(1920);
      expect(fit.cssWidth).toBeCloseTo(3840, 9);
    });

    it('is null for an unusable zoom', () => {
      expect(ccPreviewLayout(target, 0, 1)).toBeNull();
      expect(ccPreviewLayout(target, Number.NaN, 1)).toBeNull();
    });
  });

  describe('range, stops and resolving', () => {
    it('runs from 10 % to 800 %, the floor following a lower fit', () => {
      expect(ccZoomRange(1)).toEqual({ min: PREVIEW_MIN_ZOOM_FLOOR, max: PREVIEW_MAX_ZOOM });
      expect(ccZoomRange(0.05)).toEqual({ min: 0.05, max: PREVIEW_MAX_ZOOM });
      expect(ccZoomRange(Number.NaN)).toEqual({ min: PREVIEW_MIN_ZOOM_FLOOR, max: PREVIEW_MAX_ZOOM });
    });

    const fits = { width: 1.5, height: 2, screen: 1.5 };

    it('resolves each fit view to its fit', () => {
      expect(ccResolveZoom('fitWidth', fits)).toBe(1.5);
      expect(ccResolveZoom('fitScreen', fits)).toBe(1.5);
    });

    it('clamps an explicit zoom into the range', () => {
      expect(ccResolveZoom(1, fits)).toBe(1);
      expect(ccResolveZoom(20, fits)).toBe(PREVIEW_MAX_ZOOM);
      expect(ccResolveZoom(0.01, fits)).toBe(PREVIEW_MIN_ZOOM_FLOOR);
      expect(ccResolveZoom(Number.NaN, fits)).toBe(PREVIEW_MIN_ZOOM_FLOOR);
    });

    it('lowers the floor to the lowest fit, so a fit below 10 % stays reachable', () => {
      const narrow = { width: 0.05, height: 0.5, screen: 0.05 };
      expect(ccResolveZoom('fitScreen', narrow)).toBeCloseTo(0.05, 9);
      expect(ccResolveZoom(0.01, narrow)).toBeCloseTo(0.05, 9);
    });

    it('takes the floor where no fit is usable', () => {
      const none = { width: Number.NaN, height: Number.NaN, screen: Number.NaN };
      expect(ccResolveZoom('fitWidth', none)).toBe(PREVIEW_MIN_ZOOM_FLOOR);
      expect(ccResolveZoom(2, none)).toBe(2);
    });
  });
});
