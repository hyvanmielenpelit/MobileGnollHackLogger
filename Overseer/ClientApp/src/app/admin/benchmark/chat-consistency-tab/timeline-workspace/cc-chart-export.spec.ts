import { FIGURE_EXPORT_MAX_BITMAP_DIMENSION, bitmapRefusal } from '../../model-comparison/figure-export';
import { FigureSizeSettings, defaultFigureSize } from '../../model-comparison/figure-size';
import { CC_PRINT_THEME, CC_SCREEN_THEME } from '../chat-consistency-charts';
import { ccChartArchiveFilename, ccChartFilename, ccExportLayout, ccExportTheme } from './cc-chart-export';

/** A size at 100 % density and 100 % text, with overrides. */
function size(overrides: Partial<FigureSizeSettings> = {}): FigureSizeSettings {
  return { ...defaultFigureSize(1), ...overrides };
}

/** 2026-10-07 14:05:09 local time, which `exportTimestamp` writes as `20261007_140509`. */
const NOW = new Date(2026, 9, 7, 14, 5, 9);

describe('cc-chart-export', () => {
  describe('ccExportLayout', () => {
    it('lays a preset out plot-only at 100 % density', () => {
      expect(ccExportLayout(size())).toEqual({
        layout: {
          layoutWidth: 960, layoutHeight: 540, plotWidth: 960, plotHeight: 540, density: 2, pixelWidth: 1920, pixelHeight: 1080
        },
        refusal: null
      });
    });

    it('multiplies the pixels and the density at 200 %, keeping the layout box', () => {
      expect(ccExportLayout(size({ densitySelection: 2 }))).toEqual({
        layout: {
          layoutWidth: 960, layoutHeight: 540, plotWidth: 960, plotHeight: 540, density: 4, pixelWidth: 3840, pixelHeight: 2160
        },
        refusal: null
      });
    });

    it('lays a custom size out at its own aspect ratio', () => {
      const { layout, refusal } = ccExportLayout(size({ resolutionId: 'custom', customWidthPx: 1200, customHeightPx: 1200 }));
      expect(refusal).toBeNull();
      expect(layout!.layoutWidth).toBeCloseTo(960, 6);
      expect(layout!.layoutHeight).toBeCloseTo(960, 6);
      expect(layout!.plotWidth).toBe(layout!.layoutWidth);
      expect(layout!.plotHeight).toBe(layout!.layoutHeight);
      expect(layout!.density).toBeCloseTo(1.25, 9);
      expect([layout!.pixelWidth, layout!.pixelHeight]).toEqual([1200, 1200]);
    });

    it('shrinks the layout box for a 150 % text size and leaves the pixels alone', () => {
      const plain = ccExportLayout(size()).layout!;
      const large = ccExportLayout(size({ textScalePercent: 150 })).layout!;
      expect(large.layoutWidth).toBeCloseTo(640, 6);
      expect(large.layoutHeight).toBeCloseTo(360, 6);
      expect(large.plotWidth).toBe(large.layoutWidth);
      expect(large.plotHeight).toBe(large.layoutHeight);
      expect(large.density).toBeCloseTo(3, 9);
      expect([large.pixelWidth, large.pixelHeight]).toEqual([plain.pixelWidth, plain.pixelHeight]);
    });

    it('lays out A4 portrait 960 px wide', () => {
      const layout = ccExportLayout(size({ resolutionId: 'a4p' })).layout!;
      expect(layout.layoutWidth).toBeCloseTo(960, 6);
      expect(layout.layoutHeight).toBeCloseTo(1357.94, 1);
      expect([layout.pixelWidth, layout.pixelHeight]).toEqual([2480, 3508]);
    });

    it('refuses a custom side out of range', () => {
      expect(ccExportLayout(size({ resolutionId: 'custom', customWidthPx: 100 }))).toEqual({
        layout: null,
        refusal: 'The chart width must be between 320 and 8000 px.'
      });
      expect(ccExportLayout(size({ resolutionId: 'custom', customWidthPx: 100, customHeightPx: 9000 })).refusal)
        .toBe('The chart width and height must be between 320 and 8000 px.');
    });

    it('refuses a custom density out of range', () => {
      expect(ccExportLayout(size({ densitySelection: 'custom', customDensityPercent: 900 }))).toEqual({
        layout: null,
        refusal: 'The pixel density must be between 50 and 800 %.'
      });
    });

    it('refuses a bitmap side over 16384 px, and accepts one of exactly 16384 px', () => {
      const oversized = ccExportLayout(size({ resolutionId: 'uhd', densitySelection: 'custom', customDensityPercent: 500 }));
      expect(oversized.layout).toBeNull();
      expect(oversized.refusal).toBe(bitmapRefusal(3840, 2160, 5));
      expect(oversized.refusal).toContain(`${FIGURE_EXPORT_MAX_BITMAP_DIMENSION} px`);

      const custom = (widthPx: number) => size({ resolutionId: 'custom', customWidthPx: widthPx, customHeightPx: 2304, densitySelection: 4 });
      const atCap = ccExportLayout(custom(4096));
      expect(atCap.refusal).toBeNull();
      expect(atCap.layout!.pixelWidth).toBe(FIGURE_EXPORT_MAX_BITMAP_DIMENSION);
      const overCap = ccExportLayout(custom(4097));
      expect(overCap.layout).toBeNull();
      expect(overCap.refusal).toBe(bitmapRefusal(4097, 2304, 4));
    });
  });

  describe('file names', () => {
    it('names a chart by model, figure and time, with the extension of the format written', () => {
      expect(ccChartFilename('openai/gpt-5|high', 'quality', 'webp', NOW))
        .toBe('chat-consistency_openai-gpt-5-high_quality_20261007_140509.webp');
      expect(ccChartFilename('openai/gpt-5|high', 'quality', 'png', NOW))
        .toBe('chat-consistency_openai-gpt-5-high_quality_20261007_140509.png');
    });

    it('reduces every part to letters, digits, underscores and hyphens', () => {
      expect(ccChartFilename('anthropic/claude opus 4.5|max', 'ttfat', 'png', NOW))
        .toBe('chat-consistency_anthropic-claude-opus-4-5-max_ttfat_20261007_140509.png');
      expect(ccChartFilename('gpt_5-mini/ä', 'reliability', 'png', NOW))
        .toBe('chat-consistency_gpt_5-mini--_reliability_20261007_140509.png');
      expect(ccChartFilename('', 'cost', 'webp', NOW)).toBe('chat-consistency_model_cost_20261007_140509.webp');
    });

    it('stamps the current time when none is given', () => {
      expect(ccChartFilename('m', 'work', 'png')).toMatch(/^chat-consistency_m_work_\d{8}_\d{6}\.png$/);
      expect(ccChartArchiveFilename('m')).toMatch(/^chat-consistency_m_charts_\d{8}_\d{6}\.zip$/);
    });

    it('names the archive by model and time', () => {
      expect(ccChartArchiveFilename('openai/gpt-5|high', NOW)).toBe('chat-consistency_openai-gpt-5-high_charts_20261007_140509.zip');
      expect(ccChartArchiveFilename('', NOW)).toBe('chat-consistency_model_charts_20261007_140509.zip');
    });
  });

  describe('ccExportTheme', () => {
    it('draws the screen theme on an opaque dark ground', () => {
      expect(ccExportTheme('screen')).toEqual({ ...CC_SCREEN_THEME, background: '#101010' });
      // The on-screen theme itself stays transparent.
      expect(CC_SCREEN_THEME.background).toBeNull();
    });

    it('draws the print theme as it is', () => {
      expect(ccExportTheme('print')).toBe(CC_PRINT_THEME);
      expect(ccExportTheme('print').background).toBe('#ffffff');
    });
  });
});
