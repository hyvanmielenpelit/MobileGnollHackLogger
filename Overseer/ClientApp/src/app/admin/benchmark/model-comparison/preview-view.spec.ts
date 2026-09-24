import {
  PREVIEW_MAX_RASTER_PIXELS,
  PREVIEW_MAX_ZOOM,
  PREVIEW_SLIDER_STEPS,
  anchoredScrollDelta,
  canZoomPreviewIn,
  canZoomPreviewOut,
  clampPreviewZoom,
  fitHeightZoom,
  formatPreviewZoom,
  nextPreviewZoomStop,
  previewRasterZoom,
  previewZoomRange,
  previousPreviewZoomStop,
  resolvePreviewZoom,
  sliderToZoom,
  wheelZoomFactor,
  zoomToSlider
} from './preview-view';

describe('preview-view', () => {
  describe('resolvePreviewZoom', () => {
    it('shrinks a large export in the default view and fills the stage when fitted to the screen', () => {
      expect(resolvePreviewZoom('default', 0.4)).toBe(0.4);
      expect(resolvePreviewZoom('fitScreen', 0.4)).toBe(0.4);
    });

    it('never enlarges a small export in the default view, but does when fitted to the screen', () => {
      expect(resolvePreviewZoom('default', 2.5)).toBe(1);
      expect(resolvePreviewZoom('fitScreen', 2.5)).toBe(2.5);
    });

    it('clamps an explicit zoom to the range', () => {
      expect(resolvePreviewZoom(1.5, 0.5)).toBe(1.5);
      expect(resolvePreviewZoom(20, 0.5)).toBe(PREVIEW_MAX_ZOOM);
      expect(resolvePreviewZoom(0.01, 0.5)).toBe(0.1);
    });
  });

  describe('fitHeightZoom', () => {
    it('fits one figure’s full height into the visible height, less the padding', () => {
      // A 1080 px figure in a 900 px viewport at DPR 2 with 16 px padding.
      expect(fitHeightZoom(900, 1080, 2, 16)).toBeCloseTo((900 - 32) * 2 / 1080, 12);
      expect(fitHeightZoom(900, 1080, 2, 16)).toBeCloseTo(1.607, 3);
    });

    it('clamps to the largest zoom, and falls to the floor where no height is left', () => {
      expect(fitHeightZoom(4000, 100, 4, 0)).toBe(PREVIEW_MAX_ZOOM);
      expect(fitHeightZoom(20, 1080, 1, 16)).toBe(0.1);
      expect(fitHeightZoom(900, 0, 2, 16)).toBe(0.1);
    });

    it('keeps a fit below 10 % rather than raising it to the fixed floor', () => {
      const fit = fitHeightZoom(300, 8000, 1, 0);
      expect(fit).toBeCloseTo(300 / 8000, 12);
      expect(fit).toBeLessThan(0.1);
    });
  });

  describe('previewZoomRange', () => {
    it('floors at 10 %, or lower where the screen fit is lower', () => {
      expect(previewZoomRange(0.8)).toEqual({ min: 0.1, max: 8 });
      expect(previewZoomRange(0.04)).toEqual({ min: 0.04, max: 8 });
    });

    it('keeps both fitted views reachable for a very large export', () => {
      const range = previewZoomRange(0.04);
      expect(resolvePreviewZoom('fitScreen', 0.04)).toBe(0.04);
      expect(clampPreviewZoom(0.04, range)).toBe(0.04);
    });

    it('falls back to the plain floor for an unusable screen fit', () => {
      expect(previewZoomRange(Number.NaN)).toEqual({ min: 0.1, max: 8 });
      expect(previewZoomRange(0)).toEqual({ min: 0.1, max: 8 });
    });
  });

  describe('zoom stops', () => {
    const fit = 0.8333;
    const range = previewZoomRange(fit);

    it('steps through the fixed stops', () => {
      expect(nextPreviewZoomStop(1, fit, range)).toBe(1.5);
      expect(previousPreviewZoomStop(1, fit, range)).toBe(fit);
      expect(nextPreviewZoomStop(2, fit, range)).toBe(3);
      expect(previousPreviewZoomStop(0.5, fit, range)).toBeCloseTo(1 / 3, 12);
    });

    it('includes the screen fit as a stop', () => {
      expect(nextPreviewZoomStop(0.7, fit, range)).toBe(fit);
      expect(nextPreviewZoomStop(fit, fit, range)).toBe(1);
      expect(previousPreviewZoomStop(fit, fit, range)).toBeCloseTo(2 / 3, 12);
    });

    it('steps past a stop it is within the tolerance of', () => {
      expect(nextPreviewZoomStop(1 + 1e-9, fit, range)).toBe(1.5);
      expect(previousPreviewZoomStop(1 - 1e-9, fit, range)).toBe(fit);
      expect(nextPreviewZoomStop(2 / 3 + 1e-12, fit, range)).toBe(fit);
    });

    it('clamps at both ends', () => {
      expect(nextPreviewZoomStop(8, fit, range)).toBe(8);
      expect(previousPreviewZoomStop(0.1, fit, range)).toBe(0.1);
      expect(canZoomPreviewIn(8, range)).toBeFalse();
      expect(canZoomPreviewIn(6, range)).toBeTrue();
      expect(canZoomPreviewOut(0.1, range)).toBeFalse();
      expect(canZoomPreviewOut(0.125, range)).toBeTrue();
    });

    it('reaches a screen fit below the fixed floor', () => {
      const tiny = previewZoomRange(0.04);
      expect(previousPreviewZoomStop(0.1, 0.04, tiny)).toBe(0.04);
      expect(nextPreviewZoomStop(0.04, 0.04, tiny)).toBe(0.1);
    });
  });

  describe('slider mapping', () => {
    const range = previewZoomRange(0.5);

    it('maps the ends of the range to the ends of the slider', () => {
      expect(zoomToSlider(0.1, range)).toBe(0);
      expect(zoomToSlider(8, range)).toBe(PREVIEW_SLIDER_STEPS);
      expect(sliderToZoom(0, range)).toBeCloseTo(0.1, 12);
      expect(sliderToZoom(PREVIEW_SLIDER_STEPS, range)).toBeCloseTo(8, 12);
    });

    it('is logarithmic, so the geometric middle sits at the middle', () => {
      expect(zoomToSlider(Math.sqrt(0.1 * 8), range)).toBe(PREVIEW_SLIDER_STEPS / 2);
    });

    it('round-trips within one step', () => {
      for (const zoom of [0.1, 0.37, 1, 1.5, 2.4, 8]) {
        const back = sliderToZoom(zoomToSlider(zoom, range), range);
        const oneStep = Math.pow(8 / 0.1, 1 / PREVIEW_SLIDER_STEPS);
        expect(back / zoom).toBeLessThanOrEqual(oneStep);
        expect(zoom / back).toBeLessThanOrEqual(oneStep);
      }
    });

    it('clamps an out-of-range slider value', () => {
      expect(sliderToZoom(-5, range)).toBeCloseTo(0.1, 12);
      expect(sliderToZoom(5000, range)).toBeCloseTo(8, 12);
    });
  });

  describe('wheelZoomFactor', () => {
    it('zooms in on a wheel moved up and out on one moved down', () => {
      expect(wheelZoomFactor(-100, 0)).toBeGreaterThan(1);
      expect(wheelZoomFactor(100, 0)).toBeLessThan(1);
      expect(wheelZoomFactor(0, 0)).toBe(1);
    });

    it('normalises lines and pages to pixels', () => {
      expect(wheelZoomFactor(-1, 1)).toBeCloseTo(wheelZoomFactor(-16, 0), 12);
      expect(wheelZoomFactor(-0.1, 2)).toBeCloseTo(wheelZoomFactor(-80, 0), 12);
    });

    it('moves at most a factor of two per event', () => {
      expect(wheelZoomFactor(-10000, 0)).toBe(2);
      expect(wheelZoomFactor(10000, 0)).toBe(0.5);
      expect(wheelZoomFactor(3, 2)).toBe(0.5);
    });

    it('ignores a non-finite delta', () => {
      expect(wheelZoomFactor(Number.NaN, 0)).toBe(1);
    });
  });

  describe('anchoredScrollDelta', () => {
    it('keeps the point under the pointer on an overflowing canvas', () => {
      // A 1000 px canvas scrolled to start at client x = -200 doubles; the point at x = 300 was
      // half-way along it and is now at -200 + 1000, 500 px to the right of the pointer.
      expect(anchoredScrollDelta(-200, 1000, -200, 2000, 300)).toBe(500);
    });

    it('holds for a centred canvas that grows past the viewport', () => {
      // Centred at 100..500 in a 600 px viewport; grown to 800 px, it starts at the padding edge.
      const delta = anchoredScrollDelta(100, 400, 12, 800, 200);
      // The point 25 % along lands at 12 + 200 = 212, 12 px right of the pointer.
      expect(delta).toBe(12);
    });

    it('returns nothing for an empty canvas', () => {
      expect(anchoredScrollDelta(0, 0, 0, 800, 100)).toBe(0);
      expect(anchoredScrollDelta(0, 400, 0, 0, 100)).toBe(0);
    });
  });

  describe('previewRasterZoom', () => {
    it('rasterises the displayed pixels below 100 % and the target’s own above it', () => {
      expect(previewRasterZoom(0.5, 1920, 1080)).toEqual({ zoom: 0.5, capped: false });
      expect(previewRasterZoom(4, 1920, 1080)).toEqual({ zoom: 1, capped: false });
    });

    it('caps a raster past the budget', () => {
      const raster = previewRasterZoom(4, 8000, 8000);
      expect(raster.capped).toBeTrue();
      expect(8000 * 8000 * raster.zoom * raster.zoom).toBeCloseTo(PREVIEW_MAX_RASTER_PIXELS, 0);
    });
  });

  describe('formatPreviewZoom', () => {
    it('names whole percentages, with one decimal only for the small stops that need it', () => {
      expect(formatPreviewZoom(0.38)).toBe('38%');
      expect(formatPreviewZoom(0.8333)).toBe('83%');
      expect(formatPreviewZoom(0.125)).toBe('12.5%');
      expect(formatPreviewZoom(1 / 6)).toBe('16.7%');
      expect(formatPreviewZoom(0.1)).toBe('10%');
      expect(formatPreviewZoom(8)).toBe('800%');
      expect(formatPreviewZoom(0.04)).toBe('4%');
    });
  });
});
