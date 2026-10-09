import {
  BADGE_CONTROLS,
  BAR_RANGE_CONTROLS,
  CHROME_RANGE_CONTROLS,
  DEFAULT_FIGURE_STYLE,
  DEFAULT_TIMELINE_STYLE,
  HIDDEN_INTERVALS_NOTE,
  MAX_TEXT_SIZE_PX,
  SCATTER_RANGE_CONTROLS,
  TIMELINE_RANGE_CONTROLS,
  TimelineFigureStyle,
  badgeControlsFor,
  normalizeFigureStyle,
  timelineRangeControl
} from './figure-style';

describe('figure-style', () => {
  const chromeDefaults = { titleSizePx: 18, badgeTextSizePx: 11, footerTextSizePx: 12, footer: true };

  it('defaults to the values the figures were drawn with before any control existed, plus the new controls', () => {
    expect(DEFAULT_FIGURE_STYLE.bar).toEqual({
      ...chromeDefaults,
      orientation: 'auto',
      gapPercent: 28,
      maxBarWidthPx: 24,
      cornerRadiusPx: 4,
      outlineWidthPx: 2,
      filledBars: false,
      intervals: true,
      hiddenIntervalsNote: true,
      meanTimeNoIntervalNote: true,
      valueLabels: true,
      valueLabelSizePx: 11,
      axisTextSizePx: 11,
      axisTitleSizePx: 12,
      axisTitleBreak: 'auto',
      singleRunMarker: true,
      thinkingLevelBreak: false,
      gridlines: true,
      axisTitleWeight: 400,
      plotFrame: false,
      hiddenBadges: [],
      betterBadgePlacement: 'fit'
    });
    expect(DEFAULT_FIGURE_STYLE.scatter).toEqual({
      ...chromeDefaults,
      markRadiusPx: 6,
      intervals: true,
      hiddenIntervalsNote: true,
      frontierIntervalsNote: true,
      frontierLine: true,
      frontierWidthPx: 2,
      labelTextSizePx: 11,
      axisTextSizePx: 11,
      axisTitleSizePx: 12,
      legendPosition: 'bottom',
      thinkingLevelBreak: false,
      gridlines: true,
      axisTitleWeight: 400,
      plotFrame: false,
      hiddenBadges: [],
      betterBadgePlacement: 'fit'
    });
    expect(DEFAULT_FIGURE_STYLE.profile).toEqual({ ...chromeDefaults, hiddenBadges: [] });
    expect('betterBadgePlacement' in DEFAULT_FIGURE_STYLE.profile).toBe(false);
    expect(DEFAULT_FIGURE_STYLE.appearance).toEqual({
      theme: 'dark',
      background: 'theme',
      backgroundColor: '#ffffff',
      previewBackdrop: 'checkerboard',
      previewBackdropColor: '#ffffff',
      fontFamily: 'default',
      headingWeight: 600,
      labelWeight: 400,
      headingColor: null,
      textColor: null,
      border: false,
      borderWidthPx: 1,
      borderRadiusPx: 0,
      borderColor: null,
      logo: true,
      logoVariant: 'wide',
      logoHeightPx: 48
    });
    expect(DEFAULT_FIGURE_STYLE.table).toEqual({ rowShading: 'medium', rowRules: false });
    expect(DEFAULT_FIGURE_STYLE.numbers).toEqual({
      intelligenceIndex: 0,
      speedIndex: 0,
      meanModelTime: 1,
      totalModelTime: 1,
      ttftP50: 2,
      suiteCost: 4,
      totalRunCost: 4,
      costPerQuestion: 4
    });
    // 0.8 × 0.9, the two percentages the bars were drawn with.
    expect(1 - DEFAULT_FIGURE_STYLE.bar.gapPercent / 100).toBeCloseTo(0.72, 9);
    expect(HIDDEN_INTERVALS_NOTE).toBe(
      'Uncertainty bars are hidden in this figure, so it does not show how precise each value is.');
  });

  it('keeps every default inside its control range', () => {
    for (const control of BAR_RANGE_CONTROLS) {
      const value = DEFAULT_FIGURE_STYLE.bar[control.key] as number;
      expect(value, control.key).toBeGreaterThanOrEqual(control.min);
      expect(value, control.key).toBeLessThanOrEqual(control.max);
    }
    for (const control of SCATTER_RANGE_CONTROLS) {
      const value = DEFAULT_FIGURE_STYLE.scatter[control.key];
      expect(value, control.key).toBeGreaterThanOrEqual(control.min);
      expect(value, control.key).toBeLessThanOrEqual(control.max);
    }
    for (const control of TIMELINE_RANGE_CONTROLS) {
      const value = DEFAULT_FIGURE_STYLE.timeline[control.key];
      expect(value, control.key).toBeGreaterThanOrEqual(control.min);
      expect(value, control.key).toBeLessThanOrEqual(control.max);
    }
    for (const control of CHROME_RANGE_CONTROLS) {
      for (const family of ['bar', 'scatter', 'profile', 'timeline'] as const) {
        const value = DEFAULT_FIGURE_STYLE[family][control.key];
        expect(value, `${family} ${control.key}`).toBeGreaterThanOrEqual(control.min);
        expect(value, `${family} ${control.key}`).toBeLessThanOrEqual(control.max);
      }
    }
  });

  it('caps every text size at 48 px', () => {
    const textKeys = ['valueLabelSizePx', 'axisTextSizePx', 'axisTitleSizePx', 'labelTextSizePx',
      'titleSizePx', 'badgeTextSizePx', 'footerTextSizePx'];
    const controls = [...BAR_RANGE_CONTROLS, ...SCATTER_RANGE_CONTROLS, ...CHROME_RANGE_CONTROLS]
      .filter(control => textKeys.includes(control.key));
    expect(controls.length).toBe(9);
    for (const control of controls) {
      expect(control.max, control.key).toBe(MAX_TEXT_SIZE_PX);
      expect(control.min, control.key).toBe(8);
    }
    expect(MAX_TEXT_SIZE_PX).toBe(48);

    const sixty = { titleSizePx: 60, badgeTextSizePx: 60, footerTextSizePx: 60, axisTextSizePx: 60, axisTitleSizePx: 60 };
    const style = normalizeFigureStyle({
      bar: { ...sixty, valueLabelSizePx: 60 },
      scatter: { ...sixty, labelTextSizePx: 60 },
      profile: { titleSizePx: 60, badgeTextSizePx: 60, footerTextSizePx: 60 }
    });
    for (const key of ['titleSizePx', 'badgeTextSizePx', 'footerTextSizePx', 'axisTextSizePx', 'axisTitleSizePx', 'valueLabelSizePx'] as const) {
      expect(style.bar[key], `bar ${key}`).toBe(48);
    }
    for (const key of ['titleSizePx', 'badgeTextSizePx', 'footerTextSizePx', 'axisTextSizePx', 'axisTitleSizePx', 'labelTextSizePx'] as const) {
      expect(style.scatter[key], `scatter ${key}`).toBe(48);
    }
    for (const key of ['titleSizePx', 'badgeTextSizePx', 'footerTextSizePx'] as const) {
      expect(style.profile[key], `profile ${key}`).toBe(48);
    }
  });

  it('keeps the caption sizes and the footer per family', () => {
    const style = normalizeFigureStyle({
      bar: { titleSizePx: 24, footer: false },
      scatter: { badgeTextSizePx: 14 },
      profile: { footerTextSizePx: 9, footer: 'no' }
    });
    expect(style.bar.titleSizePx).toBe(24);
    expect(style.bar.footer).toBe(false);
    expect(style.scatter.titleSizePx).toBe(18);
    expect(style.scatter.badgeTextSizePx).toBe(14);
    expect(style.scatter.footer).toBe(true);
    expect(style.profile.footerTextSizePx).toBe(9);
    expect(style.profile.footer).toBe(true);
  });

  it('reads a style stored without axis title sizes as the axis value size + 1, clamped', () => {
    const style = normalizeFigureStyle({ version: 1, bar: { axisTextSizePx: 16 }, scatter: { axisTextSizePx: 48 } });
    expect(style.bar.axisTitleSizePx).toBe(17);
    expect(style.scatter.axisTitleSizePx).toBe(48);
    expect(normalizeFigureStyle({ version: 1, bar: {} }).bar.axisTitleSizePx).toBe(12);

    const stored = normalizeFigureStyle({ bar: { axisTextSizePx: 16, axisTitleSizePx: 10 } });
    expect(stored.bar.axisTitleSizePx).toBe(10);
  });

  it('offers the Better badge for bars and trade-offs, first, but not for the profile', () => {
    expect(badgeControlsFor('bar').map(control => control.kind)).toEqual(['direction', 'models', 'runs', 'questions', 'pricing']);
    expect(badgeControlsFor('scatter').map(control => control.kind)).toEqual(['direction', 'models', 'runs', 'questions', 'pricing']);
    expect(badgeControlsFor('bar')[0].hint).toContain('value axis');
    expect(badgeControlsFor('scatter')[0].hint).toBe('An arrow toward the better corner of the chart.');
    expect(badgeControlsFor('profile').map(control => control.kind)).toEqual(['models', 'runs', 'questions', 'pricing']);

    const style = normalizeFigureStyle({
      bar: { hiddenBadges: ['runs', 'direction'] },
      profile: { hiddenBadges: ['direction'] }
    });
    expect(style.bar.hiddenBadges).toEqual(['direction', 'runs']);
    expect(style.profile.hiddenBadges).toEqual(['direction']);
  });

  it('accepts anything without throwing and falls back to the default', () => {
    for (const value of [null, undefined, 'style', 42, [], [1, 2], true, {}]) {
      expect(() => normalizeFigureStyle(value), JSON.stringify(value) ?? 'undefined').not.toThrow();
      expect(normalizeFigureStyle(value), JSON.stringify(value) ?? 'undefined').toEqual(DEFAULT_FIGURE_STYLE);
    }
    expect(normalizeFigureStyle({ bar: 'x', scatter: [] })).toEqual(DEFAULT_FIGURE_STYLE);
  });

  it('clamps and rounds numbers into their ranges', () => {
    const style = normalizeFigureStyle({
      bar: { gapPercent: 140, maxBarWidthPx: 2, cornerRadiusPx: 3.6, outlineWidthPx: 0, valueLabelSizePx: 99, axisTextSizePx: Number.NaN },
      scatter: { markRadiusPx: -4, frontierWidthPx: 7, labelTextSizePx: 12.4, axisTextSizePx: '16' }
    });
    expect(style.bar.gapPercent).toBe(90);
    expect(style.bar.maxBarWidthPx).toBe(8);
    expect(style.bar.cornerRadiusPx).toBe(4);
    expect(style.bar.outlineWidthPx).toBe(1);
    expect(style.bar.valueLabelSizePx).toBe(48);
    expect(style.bar.axisTextSizePx).toBe(11);
    expect(style.scatter.markRadiusPx).toBe(3);
    expect(style.scatter.frontierWidthPx).toBe(6);
    expect(style.scatter.labelTextSizePx).toBe(12);
    // A numeric string is not a number.
    expect(style.scatter.axisTextSizePx).toBe(11);
  });

  it('keeps valid fields, repairs the rest one by one and drops unknown keys', () => {
    const style = normalizeFigureStyle({
      version: 1,
      extra: true,
      bar: { gapPercent: 10, orientation: 'sideways', gridlines: false, colour: 'red' },
      scatter: { legendPosition: 'top', markRadiusPx: 9, frontierIntervalsNote: false }
    });
    expect(style.bar).toEqual({ ...DEFAULT_FIGURE_STYLE.bar, gapPercent: 10, gridlines: false });
    expect(style.scatter).toEqual({ ...DEFAULT_FIGURE_STYLE.scatter, markRadiusPx: 9, frontierIntervalsNote: false });
    expect(Object.keys(style)).toEqual(['bar', 'scatter', 'profile', 'timeline', 'numbers', 'appearance', 'table']);
    expect('colour' in style.bar).toBe(false);
  });

  it('keeps No limit on the bar width', () => {
    expect(normalizeFigureStyle({ bar: { maxBarWidthPx: null } }).bar.maxBarWidthPx).toBeNull();
    expect(normalizeFigureStyle({ bar: {} }).bar.maxBarWidthPx).toBe(24);
  });

  it('reads a stored style missing any checkbox field as on', () => {
    const everyCheckboxOff = {
      bar: { intervals: false, hiddenIntervalsNote: false, meanTimeNoIntervalNote: false },
      scatter: { intervals: false, hiddenIntervalsNote: false, frontierIntervalsNote: false, frontierLine: false }
    };
    expect(normalizeFigureStyle(everyCheckboxOff).bar.intervals).toBe(false);

    const fields: readonly [('bar' | 'scatter'), string][] = [
      ['bar', 'intervals'],
      ['bar', 'hiddenIntervalsNote'],
      ['bar', 'meanTimeNoIntervalNote'],
      ['scatter', 'intervals'],
      ['scatter', 'hiddenIntervalsNote'],
      ['scatter', 'frontierIntervalsNote'],
      ['scatter', 'frontierLine']
    ];
    for (const [family, key] of fields) {
      const stored = JSON.parse(JSON.stringify(everyCheckboxOff)) as Record<string, Record<string, unknown>>;
      delete stored[family][key];
      const style = normalizeFigureStyle(stored) as unknown as Record<string, Record<string, unknown>>;
      expect(style[family][key], `${family}.${key}`).toBe(true);
    }

    // A style stored before the mean-time switch existed.
    const older = normalizeFigureStyle({ version: 1, bar: { intervals: false, hiddenIntervalsNote: false } });
    expect(older.bar.meanTimeNoIntervalNote).toBe(true);
    expect(older.bar.intervals).toBe(false);
  });

  it('reads a stored style without filledBars as outlined, and keeps it when set', () => {
    expect(normalizeFigureStyle({ version: 1, bar: {} }).bar.filledBars).toBe(false);
    expect(normalizeFigureStyle({ version: 1, bar: { filledBars: true } }).bar.filledBars).toBe(true);
  });

  it('accepts only real booleans for a checkbox', () => {
    const style = normalizeFigureStyle({
      bar: { intervals: 'false', valueLabels: 0, filledBars: 'true' },
      scatter: { frontierIntervalsNote: 'false', frontierLine: 'false', gridlines: null }
    });
    expect(style.bar.intervals).toBe(true);
    expect(style.bar.valueLabels).toBe(true);
    expect(style.bar.filledBars).toBe(false);
    expect(style.scatter.frontierIntervalsNote).toBe(true);
    expect(style.scatter.frontierLine).toBe(true);
    expect(style.scatter.gridlines).toBe(true);
  });

  it('drops a stored dominatedShading quietly, and loads the rest of the style', () => {
    const style = normalizeFigureStyle({
      version: 1,
      scatter: { dominatedShading: false, markRadiusPx: 8, frontierWidthPx: 3 }
    });
    expect('dominatedShading' in style.scatter).toBe(false);
    expect(style.scatter).toEqual({ ...DEFAULT_FIGURE_STYLE.scatter, markRadiusPx: 8, frontierWidthPx: 3 });
  });

  it('keeps the n = 1 marker when set and reads anything but a boolean as shown', () => {
    expect(normalizeFigureStyle({ bar: { singleRunMarker: false } }).bar.singleRunMarker).toBe(false);
    for (const value of ['false', 0, null, undefined]) {
      expect(normalizeFigureStyle({ bar: { singleRunMarker: value } }).bar.singleRunMarker, String(value)).toBe(true);
    }
  });

  it('keeps the thinking level break per family when set and reads anything but a boolean as off', () => {
    const style = normalizeFigureStyle({ bar: { thinkingLevelBreak: true }, scatter: { thinkingLevelBreak: true } });
    expect(style.bar.thinkingLevelBreak).toBe(true);
    expect(style.scatter.thinkingLevelBreak).toBe(true);
    expect(normalizeFigureStyle({ bar: { thinkingLevelBreak: true } }).scatter.thinkingLevelBreak).toBe(false);
    for (const value of ['true', 1, null, undefined]) {
      const repaired = normalizeFigureStyle({ bar: { thinkingLevelBreak: value }, scatter: { thinkingLevelBreak: value } });
      expect(repaired.bar.thinkingLevelBreak, String(value)).toBe(false);
      expect(repaired.scatter.thinkingLevelBreak, String(value)).toBe(false);
    }
    const stored = normalizeFigureStyle({ bar: { gapPercent: 10 }, scatter: { markRadiusPx: 9 } });
    expect(stored.bar.thinkingLevelBreak).toBe(false);
    expect(stored.scatter.thinkingLevelBreak).toBe(false);
  });

  it('keeps known badge kinds once each, in control order, and drops the rest', () => {
    expect(BADGE_CONTROLS.map(control => control.kind)).toEqual(['direction', 'models', 'runs', 'questions', 'pricing', 'model', 'dates']);
    const style = normalizeFigureStyle({
      bar: { hiddenBadges: ['pricing', 'models'] },
      scatter: { hiddenBadges: ['runs', 'colour', 'runs', 7, null, 'questions'] },
      profile: { hiddenBadges: ['pricing'] }
    });
    expect(style.bar.hiddenBadges).toEqual(['models', 'pricing']);
    expect(style.scatter.hiddenBadges).toEqual(['runs', 'questions']);
    expect(style.profile.hiddenBadges).toEqual(['pricing']);

    for (const value of ['runs', 3, {}, null, true]) {
      const repaired = normalizeFigureStyle({ bar: { hiddenBadges: value }, scatter: { hiddenBadges: value }, profile: { hiddenBadges: value } });
      expect(repaired.bar.hiddenBadges, JSON.stringify(value)).toEqual([]);
      expect(repaired.scatter.hiddenBadges, JSON.stringify(value)).toEqual([]);
      expect(repaired.profile.hiddenBadges, JSON.stringify(value)).toEqual([]);
    }
  });

  it('falls back to the default profile when it is missing or malformed', () => {
    for (const value of [undefined, null, 'profile', [], 42]) {
      expect(normalizeFigureStyle({ profile: value }).profile, String(value)).toEqual(DEFAULT_FIGURE_STYLE.profile);
    }
    expect(normalizeFigureStyle({ profile: { hiddenBadges: ['runs'], extra: 1 } }).profile)
      .toEqual({ ...DEFAULT_FIGURE_STYLE.profile, hiddenBadges: ['runs'] });
  });

  it('reads a version-1 style stored before the badge and marker fields existed with them at their defaults', () => {
    const style = normalizeFigureStyle({ version: 1, bar: { gapPercent: 10 } });
    expect(style.bar).toEqual({ ...DEFAULT_FIGURE_STYLE.bar, gapPercent: 10 });
    expect(style.bar.singleRunMarker).toBe(true);
    expect(style.bar.hiddenBadges).toEqual([]);
    expect(style.scatter.hiddenBadges).toEqual([]);
    expect(style.profile).toEqual(DEFAULT_FIGURE_STYLE.profile);
  });

  it('reads a style stored before the title break and number formats existed with both at their defaults', () => {
    const style = normalizeFigureStyle({ version: 1, bar: { gapPercent: 10 }, scatter: { markRadiusPx: 9 } });
    expect(style.bar.axisTitleBreak).toBe('auto');
    expect(style.numbers).toEqual(DEFAULT_FIGURE_STYLE.numbers);
    expect(style.bar.gapPercent).toBe(10);
    expect(style.scatter.markRadiusPx).toBe(9);
  });

  it('keeps a valid title break and repairs any other value', () => {
    for (const value of ['auto', 'always', 'never'] as const) {
      expect(normalizeFigureStyle({ bar: { axisTitleBreak: value } }).bar.axisTitleBreak).toBe(value);
    }
    for (const value of ['sometimes', 1, null, true, ['always']]) {
      expect(normalizeFigureStyle({ bar: { axisTitleBreak: value } }).bar.axisTitleBreak, JSON.stringify(value)).toBe('auto');
    }
  });

  it('keeps an always Better badge placement per family and reads anything else as fit', () => {
    const kept = normalizeFigureStyle({ bar: { betterBadgePlacement: 'always' }, scatter: { betterBadgePlacement: 'always' } });
    expect(kept.bar.betterBadgePlacement).toBe('always');
    expect(kept.scatter.betterBadgePlacement).toBe('always');
    expect('betterBadgePlacement' in kept.profile).toBe(false);
    for (const value of ['sometimes', 1, null, true, ['always'], undefined]) {
      const repaired = normalizeFigureStyle({ bar: { betterBadgePlacement: value }, scatter: { betterBadgePlacement: value } });
      expect(repaired.bar.betterBadgePlacement, JSON.stringify(value) ?? 'undefined').toBe('fit');
      expect(repaired.scatter.betterBadgePlacement, JSON.stringify(value) ?? 'undefined').toBe('fit');
    }
  });

  it('reads a style stored before the Better badge placement existed as fit, and the rest unchanged', () => {
    const storedBar: Record<string, unknown> = { ...DEFAULT_FIGURE_STYLE.bar, gapPercent: 10, hiddenBadges: ['runs'] };
    const storedScatter: Record<string, unknown> = { ...DEFAULT_FIGURE_STYLE.scatter, markRadiusPx: 9 };
    delete storedBar['betterBadgePlacement'];
    delete storedScatter['betterBadgePlacement'];
    const style = normalizeFigureStyle({ version: 1, bar: storedBar, scatter: storedScatter });
    expect(style.bar).toEqual({ ...storedBar, betterBadgePlacement: 'fit' } as unknown as typeof style.bar);
    expect(style.scatter).toEqual({ ...storedScatter, betterBadgePlacement: 'fit' } as unknown as typeof style.scatter);
  });

  it('normalizes the number formats field by field and drops unknown keys', () => {
    const style = normalizeFigureStyle({
      numbers: {
        intelligenceIndex: 2,
        speedIndex: 9,
        meanModelTime: -3,
        totalModelTime: 2.6,
        ttftP50: '3',
        suiteCost: null,
        totalRunCost: Number.NaN,
        costPerQuestion: Number.POSITIVE_INFINITY,
        colour: 3
      }
    });
    expect(style.numbers).toEqual({
      intelligenceIndex: 2,
      speedIndex: 6,
      meanModelTime: 0,
      totalModelTime: 3,
      ttftP50: 2,
      suiteCost: 4,
      totalRunCost: 4,
      costPerQuestion: 4
    });
    expect('colour' in style.numbers).toBe(false);

    for (const value of [null, [], [1, 2], 'numbers', 7, true]) {
      expect(normalizeFigureStyle({ numbers: value }).numbers, JSON.stringify(value)).toEqual(DEFAULT_FIGURE_STYLE.numbers);
    }
  });

  it('never mutates its input or the defaults, and returns a fresh number record', () => {
    const input = { numbers: { intelligenceIndex: 3 }, bar: { axisTitleBreak: 'never' } };
    const snapshot = JSON.parse(JSON.stringify(input));
    const defaults = JSON.parse(JSON.stringify(DEFAULT_FIGURE_STYLE));
    const style = normalizeFigureStyle(input);
    expect(input).toEqual(snapshot);
    expect(JSON.parse(JSON.stringify(DEFAULT_FIGURE_STYLE))).toEqual(defaults);
    expect(style.numbers).not.toBe(DEFAULT_FIGURE_STYLE.numbers);
    expect(normalizeFigureStyle({}).numbers).not.toBe(DEFAULT_FIGURE_STYLE.numbers);
    expect(style.numbers.intelligenceIndex).toBe(3);
  });

  it('reads a style stored before the theme existed with the appearance and table at their defaults', () => {
    const style = normalizeFigureStyle({ bar: { gapPercent: 10 }, scatter: {}, profile: {}, numbers: {} });
    expect(style.appearance).toEqual(DEFAULT_FIGURE_STYLE.appearance);
    expect(style.table).toEqual(DEFAULT_FIGURE_STYLE.table);
    expect(style.bar.axisTitleWeight).toBe(400);
    expect(style.bar.plotFrame).toBe(false);
    expect(style.scatter.axisTitleWeight).toBe(400);
    expect(style.scatter.plotFrame).toBe(false);
  });

  it('keeps valid appearance fields and repairs the rest one by one', () => {
    const style = normalizeFigureStyle({
      appearance: {
        theme: 'light',
        background: 'custom',
        backgroundColor: '#ABCDEF',
        previewBackdrop: 'color',
        previewBackdropColor: 'red',
        fontFamily: 'inter',
        headingWeight: 700,
        labelWeight: 450,
        headingColor: '#112233',
        textColor: null,
        border: true,
        borderWidthPx: 20,
        borderRadiusPx: 12.4,
        borderColor: '#12345',
        extra: 1
      },
      table: { rowBands: false, rowRules: 'yes' }
    });
    expect(style.appearance).toEqual({
      ...DEFAULT_FIGURE_STYLE.appearance,
      theme: 'light',
      background: 'custom',
      backgroundColor: '#abcdef',
      previewBackdrop: 'color',
      fontFamily: 'inter',
      headingWeight: 700,
      headingColor: '#112233',
      border: true,
      borderWidthPx: 8,
      borderRadiusPx: 12
    });
    expect('extra' in style.appearance).toBe(false);
    expect(style.table).toEqual({ rowShading: 'none', rowRules: false });

    const invalid = normalizeFigureStyle({
      appearance: { theme: 'sepia', background: 'none', fontFamily: 'comic', headingWeight: '700', borderRadiusPx: -3 }
    });
    expect(invalid.appearance).toEqual({ ...DEFAULT_FIGURE_STYLE.appearance, borderRadiusPx: 0 });
    for (const value of [null, [], 'dark', 3]) {
      expect(normalizeFigureStyle({ appearance: value }).appearance, String(value)).toEqual(DEFAULT_FIGURE_STYLE.appearance);
    }
  });

  it('keeps valid logo fields and repairs the rest', () => {
    const kept = normalizeFigureStyle({ appearance: { logo: false, logoVariant: 'square', logoHeightPx: 72 } });
    expect(kept.appearance).toEqual({ ...DEFAULT_FIGURE_STYLE.appearance, logo: false, logoVariant: 'square', logoHeightPx: 72 });

    const repaired = normalizeFigureStyle({ appearance: { logo: 'yes', logoVariant: 'huge', logoHeightPx: 200 } });
    expect(repaired.appearance.logo).toBe(true);
    expect(repaired.appearance.logoVariant).toBe('wide');
    expect(repaired.appearance.logoHeightPx).toBe(96);
    expect(normalizeFigureStyle({ appearance: { logoHeightPx: 12.6 } }).appearance.logoHeightPx).toBe(16);
    expect(normalizeFigureStyle({ appearance: { logoHeightPx: 40.4 } }).appearance.logoHeightPx).toBe(40);
    expect(normalizeFigureStyle({ appearance: { logoHeightPx: '48' } }).appearance.logoHeightPx).toBe(48);
  });

  it('reads a stored appearance without the logo fields at their defaults', () => {
    const style = normalizeFigureStyle({ appearance: { theme: 'light', border: true } });
    expect(style.appearance.logo).toBe(true);
    expect(style.appearance.logoVariant).toBe('wide');
    expect(style.appearance.logoHeightPx).toBe(48);
  });

  it('migrates the stored row bands and keeps a valid shading', () => {
    const cases: { table: Record<string, unknown>; shading: string }[] = [
      { table: { rowBands: true }, shading: 'medium' },
      { table: { rowBands: false }, shading: 'none' },
      { table: { rowShading: 'strong' }, shading: 'strong' },
      { table: { rowShading: 'strong', rowBands: false }, shading: 'strong' },
      { table: { rowShading: 'loud' }, shading: 'medium' },
      { table: { rowBands: 'yes' }, shading: 'medium' }
    ];
    for (const { table, shading } of cases) {
      const style = normalizeFigureStyle({ table });
      expect(style.table.rowShading, JSON.stringify(table)).toBe(shading);
      expect('rowBands' in style.table, JSON.stringify(table)).toBe(false);
    }
  });

  it('accepts only the four font weights for the axis titles', () => {
    const style = normalizeFigureStyle({ bar: { axisTitleWeight: 600, plotFrame: true }, scatter: { axisTitleWeight: 300 } });
    expect(style.bar.axisTitleWeight).toBe(600);
    expect(style.bar.plotFrame).toBe(true);
    expect(style.scatter.axisTitleWeight).toBe(400);
  });

  describe('the timeline family', () => {
    /** A valid stored timeline whose every field differs from its default. */
    const changed: TimelineFigureStyle = {
      titleSizePx: 20,
      badgeTextSizePx: 13,
      footerTextSizePx: 10,
      footer: false,
      axisTextSizePx: 14,
      axisTitleSizePx: 15,
      axisTitleWeight: 700,
      gridlines: false,
      plotFrame: true,
      valueLabels: false,
      valueLabelSizePx: 13,
      legendTextSizePx: 14,
      markerTagSizePx: 16,
      lineWidthPx: 3,
      pointRadiusPx: 6,
      areaWash: false,
      markerNote: false,
      notAnalyzedNote: false,
      hiddenBadges: ['direction', 'dates'],
      betterBadgePlacement: 'always'
    };

    /** A value each field rejects. */
    const invalid: Record<keyof TimelineFigureStyle, unknown> = {
      titleSizePx: '20',
      badgeTextSizePx: null,
      footerTextSizePx: Number.NaN,
      footer: 'no',
      axisTextSizePx: '14',
      axisTitleSizePx: true,
      axisTitleWeight: 450,
      gridlines: 0,
      plotFrame: 'true',
      valueLabels: null,
      valueLabelSizePx: [13],
      legendTextSizePx: {},
      markerTagSizePx: Number.POSITIVE_INFINITY,
      lineWidthPx: '3',
      pointRadiusPx: null,
      areaWash: 1,
      markerNote: 'false',
      notAnalyzedNote: [],
      hiddenBadges: 'dates',
      betterBadgePlacement: 'sometimes'
    };

    it('defaults to the shared caption defaults plus Model Comparison\'s sizes, every element shown', () => {
      expect(DEFAULT_TIMELINE_STYLE).toEqual({
        ...chromeDefaults,
        axisTextSizePx: 11,
        axisTitleSizePx: 12,
        axisTitleWeight: 400,
        gridlines: true,
        plotFrame: false,
        valueLabels: true,
        valueLabelSizePx: 11,
        legendTextSizePx: 12,
        markerTagSizePx: 10,
        lineWidthPx: 2,
        pointRadiusPx: 4,
        areaWash: true,
        markerNote: true,
        notAnalyzedNote: true,
        hiddenBadges: [],
        betterBadgePlacement: 'fit'
      });
      expect(DEFAULT_FIGURE_STYLE.timeline).toBe(DEFAULT_TIMELINE_STYLE);
    });

    it('offers the text sizes from 8 to 48 px and its own ranges for the lines, points and marker tags', () => {
      for (const key of ['valueLabelSizePx', 'axisTextSizePx', 'axisTitleSizePx', 'legendTextSizePx'] as const) {
        expect([timelineRangeControl(key).min, timelineRangeControl(key).max], key).toEqual([8, MAX_TEXT_SIZE_PX]);
      }
      expect([timelineRangeControl('lineWidthPx').min, timelineRangeControl('lineWidthPx').max]).toEqual([1, 6]);
      expect([timelineRangeControl('pointRadiusPx').min, timelineRangeControl('pointRadiusPx').max]).toEqual([2, 10]);
      expect([timelineRangeControl('markerTagSizePx').min, timelineRangeControl('markerTagSizePx').max]).toEqual([8, 24]);
      expect(TIMELINE_RANGE_CONTROLS.length).toBe(7);
    });

    it('keeps a valid stored timeline whole', () => {
      expect(normalizeFigureStyle({ timeline: changed }).timeline).toEqual(changed);
    });

    it('repairs each field on its own, invalid or missing, and keeps the rest', () => {
      for (const key of Object.keys(invalid) as (keyof TimelineFigureStyle)[]) {
        const repaired = normalizeFigureStyle({ timeline: { ...changed, [key]: invalid[key] } }).timeline;
        expect(repaired, `invalid ${key}`).toEqual({ ...changed, [key]: DEFAULT_TIMELINE_STYLE[key] });

        const stored: Record<string, unknown> = { ...changed };
        delete stored[key];
        expect(normalizeFigureStyle({ timeline: stored }).timeline, `missing ${key}`)
          .toEqual({ ...changed, [key]: DEFAULT_TIMELINE_STYLE[key] });
      }
      expect(Object.keys(invalid).sort()).toEqual(Object.keys(DEFAULT_TIMELINE_STYLE).sort());
    });

    it('clamps and rounds its numbers into their ranges', () => {
      const style = normalizeFigureStyle({
        timeline: { lineWidthPx: 9, pointRadiusPx: 1, markerTagSizePx: 30, legendTextSizePx: 60, valueLabelSizePx: 3.4, axisTextSizePx: 12.6 }
      });
      expect(style.timeline.lineWidthPx).toBe(6);
      expect(style.timeline.pointRadiusPx).toBe(2);
      expect(style.timeline.markerTagSizePx).toBe(24);
      expect(style.timeline.legendTextSizePx).toBe(48);
      expect(style.timeline.valueLabelSizePx).toBe(8);
      expect(style.timeline.axisTextSizePx).toBe(13);
    });

    it('falls back to the defaults when the timeline is missing or malformed, and drops unknown keys', () => {
      for (const value of [undefined, null, 'timeline', [], 42]) {
        expect(normalizeFigureStyle({ timeline: value }).timeline, String(value)).toEqual(DEFAULT_TIMELINE_STYLE);
      }
      expect('extra' in normalizeFigureStyle({ timeline: { extra: 1 } }).timeline).toBe(false);
    });

    it('offers the Better badge, Model, Number of runs and Dates, in that order', () => {
      const controls = badgeControlsFor('timeline');
      expect(controls.map(control => control.kind)).toEqual(['direction', 'model', 'runs', 'dates']);
      expect(controls.map(control => control.label)).toEqual(['Better badge', 'Model', 'Number of runs', 'Dates']);
      expect(controls[0].hint).toContain('value axis');

      const style = normalizeFigureStyle({ timeline: { hiddenBadges: ['dates', 'colour', 'direction', 'dates'] } });
      expect(style.timeline.hiddenBadges).toEqual(['direction', 'dates']);
    });

    it('leaves the other families as they are', () => {
      const style = normalizeFigureStyle({ timeline: changed });
      expect(style.bar).toEqual(DEFAULT_FIGURE_STYLE.bar);
      expect(style.scatter).toEqual(DEFAULT_FIGURE_STYLE.scatter);
      expect(style.profile).toEqual(DEFAULT_FIGURE_STYLE.profile);
      expect(style.appearance).toEqual(DEFAULT_FIGURE_STYLE.appearance);
      expect(style.numbers).toEqual(DEFAULT_FIGURE_STYLE.numbers);
      expect(style.table).toEqual(DEFAULT_FIGURE_STYLE.table);
    });
  });
});
