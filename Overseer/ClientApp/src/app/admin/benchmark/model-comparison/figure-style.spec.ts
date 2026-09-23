import {
  BAR_RANGE_CONTROLS,
  DEFAULT_FIGURE_STYLE,
  HIDDEN_INTERVALS_NOTE,
  SCATTER_RANGE_CONTROLS,
  normalizeFigureStyle
} from './figure-style';

describe('figure-style', () => {
  it('defaults to the values the figures were drawn with before any control existed', () => {
    expect(DEFAULT_FIGURE_STYLE.bar).toEqual({
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
      gridlines: true
    });
    expect(DEFAULT_FIGURE_STYLE.scatter).toEqual({
      markRadiusPx: 6,
      intervals: true,
      hiddenIntervalsNote: true,
      frontierIntervalsNote: true,
      dominatedShading: true,
      frontierWidthPx: 2,
      labelTextSizePx: 11,
      axisTextSizePx: 11,
      legendPosition: 'bottom',
      gridlines: true
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
    expect(style.bar.valueLabelSizePx).toBe(28);
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
    expect(Object.keys(style)).toEqual(['bar', 'scatter']);
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
});
