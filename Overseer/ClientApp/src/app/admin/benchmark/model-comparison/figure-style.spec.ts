import {
  BADGE_CONTROLS,
  BAR_RANGE_CONTROLS,
  CHROME_RANGE_CONTROLS,
  DEFAULT_FIGURE_STYLE,
  HIDDEN_INTERVALS_NOTE,
  MAX_TEXT_SIZE_PX,
  SCATTER_RANGE_CONTROLS,
  badgeControlsFor,
  normalizeFigureStyle
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
      hiddenBadges: []
    });
    expect(DEFAULT_FIGURE_STYLE.scatter).toEqual({
      ...chromeDefaults,
      markRadiusPx: 6,
      intervals: true,
      hiddenIntervalsNote: true,
      frontierIntervalsNote: true,
      dominatedShading: true,
      frontierWidthPx: 2,
      labelTextSizePx: 11,
      axisTextSizePx: 11,
      axisTitleSizePx: 12,
      legendPosition: 'bottom',
      thinkingLevelBreak: false,
      gridlines: true,
      hiddenBadges: []
    });
    expect(DEFAULT_FIGURE_STYLE.profile).toEqual({ ...chromeDefaults, hiddenBadges: [] });
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
      expect(value).withContext(control.key).toBeGreaterThanOrEqual(control.min);
      expect(value).withContext(control.key).toBeLessThanOrEqual(control.max);
    }
    for (const control of SCATTER_RANGE_CONTROLS) {
      const value = DEFAULT_FIGURE_STYLE.scatter[control.key];
      expect(value).withContext(control.key).toBeGreaterThanOrEqual(control.min);
      expect(value).withContext(control.key).toBeLessThanOrEqual(control.max);
    }
    for (const control of CHROME_RANGE_CONTROLS) {
      for (const family of ['bar', 'scatter', 'profile'] as const) {
        const value = DEFAULT_FIGURE_STYLE[family][control.key];
        expect(value).withContext(`${family} ${control.key}`).toBeGreaterThanOrEqual(control.min);
        expect(value).withContext(`${family} ${control.key}`).toBeLessThanOrEqual(control.max);
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
      expect(control.max).withContext(control.key).toBe(MAX_TEXT_SIZE_PX);
      expect(control.min).withContext(control.key).toBe(8);
    }
    expect(MAX_TEXT_SIZE_PX).toBe(48);

    const sixty = { titleSizePx: 60, badgeTextSizePx: 60, footerTextSizePx: 60, axisTextSizePx: 60, axisTitleSizePx: 60 };
    const style = normalizeFigureStyle({
      bar: { ...sixty, valueLabelSizePx: 60 },
      scatter: { ...sixty, labelTextSizePx: 60 },
      profile: { titleSizePx: 60, badgeTextSizePx: 60, footerTextSizePx: 60 }
    });
    for (const key of ['titleSizePx', 'badgeTextSizePx', 'footerTextSizePx', 'axisTextSizePx', 'axisTitleSizePx', 'valueLabelSizePx'] as const) {
      expect(style.bar[key]).withContext(`bar ${key}`).toBe(48);
    }
    for (const key of ['titleSizePx', 'badgeTextSizePx', 'footerTextSizePx', 'axisTextSizePx', 'axisTitleSizePx', 'labelTextSizePx'] as const) {
      expect(style.scatter[key]).withContext(`scatter ${key}`).toBe(48);
    }
    for (const key of ['titleSizePx', 'badgeTextSizePx', 'footerTextSizePx'] as const) {
      expect(style.profile[key]).withContext(`profile ${key}`).toBe(48);
    }
  });

  it('keeps the caption sizes and the footer per family', () => {
    const style = normalizeFigureStyle({
      bar: { titleSizePx: 24, footer: false },
      scatter: { badgeTextSizePx: 14 },
      profile: { footerTextSizePx: 9, footer: 'no' }
    });
    expect(style.bar.titleSizePx).toBe(24);
    expect(style.bar.footer).toBeFalse();
    expect(style.scatter.titleSizePx).toBe(18);
    expect(style.scatter.badgeTextSizePx).toBe(14);
    expect(style.scatter.footer).toBeTrue();
    expect(style.profile.footerTextSizePx).toBe(9);
    expect(style.profile.footer).toBeTrue();
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
    expect(badgeControlsFor('scatter')[0].kind).toBe('direction');
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
      expect(() => normalizeFigureStyle(value)).withContext(JSON.stringify(value) ?? 'undefined').not.toThrow();
      expect(normalizeFigureStyle(value)).withContext(JSON.stringify(value) ?? 'undefined').toEqual(DEFAULT_FIGURE_STYLE);
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
      scatter: { legendPosition: 'top', markRadiusPx: 9, dominatedShading: false }
    });
    expect(style.bar).toEqual({ ...DEFAULT_FIGURE_STYLE.bar, gapPercent: 10, gridlines: false });
    expect(style.scatter).toEqual({ ...DEFAULT_FIGURE_STYLE.scatter, markRadiusPx: 9, dominatedShading: false });
    expect(Object.keys(style)).toEqual(['bar', 'scatter', 'profile', 'numbers']);
    expect('colour' in style.bar).toBeFalse();
  });

  it('keeps No limit on the bar width', () => {
    expect(normalizeFigureStyle({ bar: { maxBarWidthPx: null } }).bar.maxBarWidthPx).toBeNull();
    expect(normalizeFigureStyle({ bar: {} }).bar.maxBarWidthPx).toBe(24);
  });

  it('reads a stored style missing any checkbox field as on', () => {
    const everyCheckboxOff = {
      bar: { intervals: false, hiddenIntervalsNote: false, meanTimeNoIntervalNote: false },
      scatter: { intervals: false, hiddenIntervalsNote: false, frontierIntervalsNote: false, dominatedShading: false }
    };
    expect(normalizeFigureStyle(everyCheckboxOff).bar.intervals).toBeFalse();

    const fields: readonly [('bar' | 'scatter'), string][] = [
      ['bar', 'intervals'],
      ['bar', 'hiddenIntervalsNote'],
      ['bar', 'meanTimeNoIntervalNote'],
      ['scatter', 'intervals'],
      ['scatter', 'hiddenIntervalsNote'],
      ['scatter', 'frontierIntervalsNote'],
      ['scatter', 'dominatedShading']
    ];
    for (const [family, key] of fields) {
      const stored = JSON.parse(JSON.stringify(everyCheckboxOff)) as Record<string, Record<string, unknown>>;
      delete stored[family][key];
      const style = normalizeFigureStyle(stored) as unknown as Record<string, Record<string, unknown>>;
      expect(style[family][key]).withContext(`${family}.${key}`).toBeTrue();
    }

    // A style stored before the mean-time switch existed.
    const older = normalizeFigureStyle({ version: 1, bar: { intervals: false, hiddenIntervalsNote: false } });
    expect(older.bar.meanTimeNoIntervalNote).toBeTrue();
    expect(older.bar.intervals).toBeFalse();
  });

  it('reads a stored style without filledBars as outlined, and keeps it when set', () => {
    expect(normalizeFigureStyle({ version: 1, bar: {} }).bar.filledBars).toBeFalse();
    expect(normalizeFigureStyle({ version: 1, bar: { filledBars: true } }).bar.filledBars).toBeTrue();
  });

  it('accepts only real booleans for a checkbox', () => {
    const style = normalizeFigureStyle({
      bar: { intervals: 'false', valueLabels: 0, filledBars: 'true' },
      scatter: { dominatedShading: 'false', gridlines: null }
    });
    expect(style.bar.intervals).toBeTrue();
    expect(style.bar.valueLabels).toBeTrue();
    expect(style.bar.filledBars).toBeFalse();
    expect(style.scatter.dominatedShading).toBeTrue();
    expect(style.scatter.gridlines).toBeTrue();
  });

  it('keeps the n = 1 marker when set and reads anything but a boolean as shown', () => {
    expect(normalizeFigureStyle({ bar: { singleRunMarker: false } }).bar.singleRunMarker).toBeFalse();
    for (const value of ['false', 0, null, undefined]) {
      expect(normalizeFigureStyle({ bar: { singleRunMarker: value } }).bar.singleRunMarker)
        .withContext(String(value)).toBeTrue();
    }
  });

  it('keeps the thinking level break per family when set and reads anything but a boolean as off', () => {
    const style = normalizeFigureStyle({ bar: { thinkingLevelBreak: true }, scatter: { thinkingLevelBreak: true } });
    expect(style.bar.thinkingLevelBreak).toBeTrue();
    expect(style.scatter.thinkingLevelBreak).toBeTrue();
    expect(normalizeFigureStyle({ bar: { thinkingLevelBreak: true } }).scatter.thinkingLevelBreak).toBeFalse();
    for (const value of ['true', 1, null, undefined]) {
      const repaired = normalizeFigureStyle({ bar: { thinkingLevelBreak: value }, scatter: { thinkingLevelBreak: value } });
      expect(repaired.bar.thinkingLevelBreak).withContext(String(value)).toBeFalse();
      expect(repaired.scatter.thinkingLevelBreak).withContext(String(value)).toBeFalse();
    }
    const stored = normalizeFigureStyle({ bar: { gapPercent: 10 }, scatter: { markRadiusPx: 9 } });
    expect(stored.bar.thinkingLevelBreak).toBeFalse();
    expect(stored.scatter.thinkingLevelBreak).toBeFalse();
  });

  it('keeps known badge kinds once each, in control order, and drops the rest', () => {
    expect(BADGE_CONTROLS.map(control => control.kind)).toEqual(['direction', 'models', 'runs', 'questions', 'pricing']);
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
      expect(repaired.bar.hiddenBadges).withContext(JSON.stringify(value)).toEqual([]);
      expect(repaired.scatter.hiddenBadges).withContext(JSON.stringify(value)).toEqual([]);
      expect(repaired.profile.hiddenBadges).withContext(JSON.stringify(value)).toEqual([]);
    }
  });

  it('falls back to the default profile when it is missing or malformed', () => {
    for (const value of [undefined, null, 'profile', [], 42]) {
      expect(normalizeFigureStyle({ profile: value }).profile).withContext(String(value)).toEqual(DEFAULT_FIGURE_STYLE.profile);
    }
    expect(normalizeFigureStyle({ profile: { hiddenBadges: ['runs'], extra: 1 } }).profile)
      .toEqual({ ...DEFAULT_FIGURE_STYLE.profile, hiddenBadges: ['runs'] });
  });

  it('reads a version-1 style stored before the badge and marker fields existed with them at their defaults', () => {
    const style = normalizeFigureStyle({ version: 1, bar: { gapPercent: 10 } });
    expect(style.bar).toEqual({ ...DEFAULT_FIGURE_STYLE.bar, gapPercent: 10 });
    expect(style.bar.singleRunMarker).toBeTrue();
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
      expect(normalizeFigureStyle({ bar: { axisTitleBreak: value } }).bar.axisTitleBreak)
        .withContext(JSON.stringify(value)).toBe('auto');
    }
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
    expect('colour' in style.numbers).toBeFalse();

    for (const value of [null, [], [1, 2], 'numbers', 7, true]) {
      expect(normalizeFigureStyle({ numbers: value }).numbers).withContext(JSON.stringify(value)).toEqual(DEFAULT_FIGURE_STYLE.numbers);
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
});
