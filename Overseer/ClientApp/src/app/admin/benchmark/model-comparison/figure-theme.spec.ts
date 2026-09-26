import {
  FIGURE_BACKGROUND,
  FIGURE_BODY_COLOR,
  FIGURE_FONT_STACK,
  FIGURE_MUTED_COLOR,
  FIGURE_RULE_COLOR,
  FIGURE_TITLE_COLOR
} from './figure-export';
import { OVERSEER_DEFAULT_FONT_STACK } from './figure-fonts';
import { DEFAULT_APPEARANCE_STYLE, FigureAppearanceStyle } from './figure-style';
import {
  TABLE_SHADING_ALPHA,
  appearanceWarnings,
  contrastRatio,
  mixHex,
  resolveFigureTheme,
  tableBandColor,
  themeBackground
} from './figure-theme';
import {
  ACCENT,
  CATEGORICAL_PALETTE_DARK,
  CHART_INK,
  CHART_SURFACE,
  DE_EMPHASIS_FILL,
  DE_EMPHASIS_STROKE,
  DOMINATED_REGION_FILL
} from './model-comparison-charts';

describe('figure-theme', () => {
  function appearance(patch: Partial<FigureAppearanceStyle>): FigureAppearanceStyle {
    return { ...DEFAULT_APPEARANCE_STYLE, ...patch };
  }

  it('resolves the default appearance to the literal palette the figures were always drawn in', () => {
    const theme = resolveFigureTheme();
    expect(theme.name).toBe('dark');
    expect(theme.background).toBe(FIGURE_BACKGROUND);
    expect(theme.surface).toBe(FIGURE_BACKGROUND);
    expect(theme.chrome.title).toBe(FIGURE_TITLE_COLOR);
    expect(theme.chrome.body).toBe(FIGURE_BODY_COLOR);
    expect(theme.chrome.muted).toBe(FIGURE_MUTED_COLOR);
    expect(theme.chrome.rule).toBe(FIGURE_RULE_COLOR);
    expect(theme.chrome.keyInk).toBe('#c3c2b7');
    expect(theme.chrome.dominatedKeyFill).toBe('rgba(255, 255, 255, 0.12)');
    expect(theme.chrome.direction).toEqual({
      border: 'rgba(224, 186, 109, 0.55)', fill: 'rgba(224, 186, 109, 0.1)', ink: FIGURE_TITLE_COLOR
    });
    expect(theme.chrome.badge).toEqual({
      neutral: { border: 'rgba(255, 255, 255, 0.25)', fill: 'rgba(255, 255, 255, 0.04)', text: FIGURE_TITLE_COLOR },
      pricing: { border: 'rgba(16, 185, 129, 0.3)', fill: 'rgba(16, 185, 129, 0.1)', text: '#6ee7b7' }
    });
    expect(theme.chrome.note).toEqual({
      warning: { rule: '#e0ba6d', text: '#e0ba6d' },
      info: { rule: '#6b6b66', text: FIGURE_MUTED_COLOR }
    });

    expect(theme.chart.surface).toBe(CHART_SURFACE);
    expect(theme.chart.inkPrimary).toBe(CHART_INK.primary);
    expect(theme.chart.inkSecondary).toBe(CHART_INK.secondary);
    expect(theme.chart.inkMuted).toBe(CHART_INK.muted);
    expect(theme.chart.gridline).toBe(CHART_INK.gridline);
    expect(theme.chart.baseline).toBe(CHART_INK.baseline);
    expect(theme.chart.accent).toBe(ACCENT);
    expect(theme.chart.deEmphasisFill).toBe(DE_EMPHASIS_FILL);
    expect(theme.chart.deEmphasisStroke).toBe(DE_EMPHASIS_STROKE);
    expect(theme.chart.dominatedRegionFill).toBe(DOMINATED_REGION_FILL);
    expect(theme.chart.categorical).toEqual([...CATEGORICAL_PALETTE_DARK]);

    expect(theme.fonts).toEqual({
      chromeStack: FIGURE_FONT_STACK, chartStack: null, headingWeight: 600, labelWeight: 400
    });
    expect(OVERSEER_DEFAULT_FONT_STACK).toBe(FIGURE_FONT_STACK);
    expect(theme.border).toBeNull();
    expect(theme.frameColor).toBe(CHART_INK.baseline);
  });

  it('resolves the light theme to values measured on white', () => {
    const theme = resolveFigureTheme(appearance({ theme: 'light' }));
    expect(theme.background).toBe('#ffffff');
    expect(theme.chart.surface).toBe('#ffffff');
    expect(theme.chrome.title).toBe('#0b0b0b');
    expect(theme.chrome.body).toBe('#52514e');
    expect(theme.chrome.muted).toBe('#6b6a66');
    expect(theme.chart.accent).toBe('#9a6b12');
    expect(theme.chart.categorical).toEqual(['#2a78d6', '#eb6834', '#18a070']);
    expect(theme.chrome.badge.pricing.text).toBe('#047857');
    expect(theme.chrome.note.warning).toEqual({ rule: '#8a5a00', text: '#8a5a00' });
    expect(theme.frameColor).toBe('#c3c2b7');

    for (const [ink, minimum] of [['#0b0b0b', 19], ['#52514e', 7.8], ['#6b6a66', 5.3], ['#9a6b12', 4.6], ['#047857', 5.4], ['#8a5a00', 5.8]] as const) {
      expect(contrastRatio(ink, '#ffffff')).withContext(ink).toBeGreaterThan(minimum);
    }
    expect(appearanceWarnings(appearance({ theme: 'light' }))).toEqual([]);
    expect(appearanceWarnings(DEFAULT_APPEARANCE_STYLE)).toEqual([]);
  });

  it('paints a custom background and uses it as the chart surface', () => {
    const theme = resolveFigureTheme(appearance({ theme: 'light', background: 'custom', backgroundColor: '#f4f1ea' }));
    expect(theme.background).toBe('#f4f1ea');
    expect(theme.surface).toBe('#f4f1ea');
    expect(theme.chart.surface).toBe('#f4f1ea');
  });

  it('paints nothing on a transparent background but keeps the theme base as the surface', () => {
    const dark = resolveFigureTheme(appearance({ background: 'transparent' }));
    expect(dark.background).toBeNull();
    expect(dark.surface).toBe('#181818');
    expect(dark.chart.surface).toBe(CHART_SURFACE);
    expect(themeBackground('light')).toBe('#ffffff');
    expect(resolveFigureTheme(appearance({ theme: 'light', background: 'transparent' })).surface).toBe('#ffffff');
  });

  it('applies heading and text colours to the roles they name, and leaves the theme-owned ones', () => {
    const theme = resolveFigureTheme(appearance({ headingColor: '#123456', textColor: '#abcdef' }));
    expect(theme.chrome.title).toBe('#123456');
    expect(theme.chrome.badge.neutral.text).toBe('#123456');
    expect(theme.chrome.direction.ink).toBe('#123456');
    expect(theme.chrome.body).toBe('#abcdef');
    expect(theme.chrome.keyInk).toBe('#abcdef');
    expect(theme.chart.inkPrimary).toBe('#abcdef');
    expect(theme.chart.inkSecondary).toBe('#abcdef');
    expect(theme.chrome.muted).toBe(mixHex('#abcdef', '#181818', 0.7));
    expect(theme.chart.inkMuted).toBe(theme.chrome.muted);
    expect(theme.chrome.note.info.text).toBe(theme.chrome.muted);
    expect(theme.chrome.note.warning).toEqual({ rule: '#e0ba6d', text: '#e0ba6d' });
    expect(theme.chrome.badge.pricing.text).toBe('#6ee7b7');
    expect(theme.chart.accent).toBe(ACCENT);
    expect(theme.chart.categorical).toEqual([...CATEGORICAL_PALETTE_DARK]);
  });

  it('resolves a border in the theme baseline unless a colour is chosen', () => {
    expect(resolveFigureTheme(appearance({ border: true, borderWidthPx: 2, borderRadiusPx: 12 })).border)
      .toEqual({ widthPx: 2, radiusPx: 12, color: CHART_INK.baseline });
    expect(resolveFigureTheme(appearance({ theme: 'light', border: true, borderColor: '#ff0000' })).border)
      .toEqual({ widthPx: 1, radiusPx: 0, color: '#ff0000' });
    expect(resolveFigureTheme(appearance({ border: false, borderWidthPx: 4 })).border).toBeNull();
  });

  it('gives a bundled font to both the chrome and the charts', () => {
    const fonts = resolveFigureTheme(appearance({ fontFamily: 'inter', headingWeight: 700, labelWeight: 500 })).fonts;
    expect(fonts.chromeStack.startsWith('"Inter", ')).toBeTrue();
    expect(fonts.chartStack).toBe(fonts.chromeStack);
    expect(fonts.headingWeight).toBe(700);
    expect(fonts.labelWeight).toBe(500);
  });

  it('mixes and measures colours', () => {
    expect(mixHex('#ffffff', '#000000', 0.5)).toBe('#808080');
    expect(mixHex('#ffffff', '#000000', 1)).toBe('#ffffff');
    expect(mixHex('red', '#000000', 0.5)).toBe('red');
    expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 5);
    expect(contrastRatio('#777777', '#777777')).toBeCloseTo(1, 5);
  });

  it('warns about faint headings, text and series in plain sentences', () => {
    const faint = appearanceWarnings(appearance({ background: 'custom', backgroundColor: '#ffffff' }));
    // Dark theme inks on a white ground: the gold heading and the light body text fail.
    expect(faint.some(w => w.startsWith('Headings in #e0ba6d'))).toBeTrue();
    expect(faint.some(w => w.startsWith('Text in #d4d4d8'))).toBeTrue();
    expect(faint.every(w => w.includes('the background #ffffff'))).toBeTrue();

    const onBlue = appearanceWarnings(appearance({ background: 'custom', backgroundColor: '#3987e5' }));
    expect(onBlue.some(w => w.startsWith('The series color #3987e5 has 1.0:1'))).toBeTrue();
  });

  it('judges a transparent image against the backdrop colour, or the theme base under the checkerboard', () => {
    const onColour = appearanceWarnings(appearance({ background: 'transparent', previewBackdrop: 'color', previewBackdropColor: '#ffffff' }));
    expect(onColour.length).toBeGreaterThan(0);
    expect(onColour.every(w => w.includes('preview backdrop color #ffffff'))).toBeTrue();

    const onChecker = appearanceWarnings(appearance({ background: 'transparent', textColor: '#202020' }));
    expect(onChecker.length).toBe(1);
    expect(onChecker[0]).toContain("dark theme's own background #181818");
    expect(onChecker[0]).toContain('checkerboard');
  });

  describe('tableBandColor', () => {
    it('draws no band at none', () => {
      expect(tableBandColor(resolveFigureTheme(), 'none')).toBeNull();
    });

    it('lays the dark theme text color over the ground at each level', () => {
      const theme = resolveFigureTheme();
      expect(tableBandColor(theme, 'light')).toBe('rgba(212, 212, 216, 0.05)');
      expect(tableBandColor(theme, 'medium')).toBe('rgba(212, 212, 216, 0.1)');
      expect(tableBandColor(theme, 'strong')).toBe('rgba(212, 212, 216, 0.16)');
    });

    it('builds the light theme band from its text color', () => {
      const theme = resolveFigureTheme(appearance({ theme: 'light' }));
      expect(tableBandColor(theme, 'medium')).toBe('rgba(82, 81, 78, 0.1)');
    });

    it('follows a custom text color', () => {
      const theme = resolveFigureTheme(appearance({ textColor: '#336699' }));
      expect(tableBandColor(theme, 'strong')).toBe('rgba(51, 102, 153, 0.16)');
    });

    it('grows stronger level by level', () => {
      expect(TABLE_SHADING_ALPHA.light).toBeLessThan(TABLE_SHADING_ALPHA.medium);
      expect(TABLE_SHADING_ALPHA.medium).toBeLessThan(TABLE_SHADING_ALPHA.strong);
    });
  });
});
