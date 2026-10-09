import { ccChartArchiveFilename, ccChartFilename } from './cc-chart-export';

/** 2026-10-07 14:05:09 local time, which `exportTimestamp` writes as `20261007_140509`. */
const NOW = new Date(2026, 9, 7, 14, 5, 9);

describe('cc-chart-export', () => {
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
});
