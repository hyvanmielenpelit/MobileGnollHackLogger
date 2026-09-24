import {
  DEFAULT_MEASURE_DECIMALS,
  MAX_MEASURE_DECIMALS,
  MEASURE_EXAMPLES,
  MEASURE_NAMES,
  MEASURE_SHORT_NAMES,
  NUMBER_MEASURES,
  costNumberMeasure,
  formatMeasure,
  formatMeasureSample,
  normalizeMeasureDecimals,
  speedNumberMeasure
} from './measure-format';
import type { NumberMeasure } from './measure-format';

describe('measure-format', () => {
  it('lists eight measures with their defaults, names and examples', () => {
    expect(NUMBER_MEASURES).toEqual([
      'intelligenceIndex', 'speedIndex', 'meanModelTime', 'totalModelTime', 'ttftP50',
      'suiteCost', 'totalRunCost', 'costPerQuestion'
    ]);
    expect(DEFAULT_MEASURE_DECIMALS).toEqual({
      intelligenceIndex: 0,
      speedIndex: 0,
      meanModelTime: 1,
      totalModelTime: 1,
      ttftP50: 2,
      suiteCost: 4,
      totalRunCost: 4,
      costPerQuestion: 4
    });
    expect(MAX_MEASURE_DECIMALS).toBe(6);
    for (const measure of NUMBER_MEASURES) {
      expect(MEASURE_NAMES[measure]).withContext(measure).toBeTruthy();
      expect(MEASURE_SHORT_NAMES[measure]).withContext(measure).toBeTruthy();
      expect(Number.isFinite(MEASURE_EXAMPLES[measure])).withContext(measure).toBeTrue();
    }
  });

  it('previews each fixed example at its default decimals', () => {
    const expected: Record<NumberMeasure, string> = {
      intelligenceIndex: '71',
      speedIndex: '64',
      meanModelTime: '22.5 s',
      totalModelTime: '504.0 s',
      ttftP50: '1.25 s',
      suiteCost: '$0.0761',
      totalRunCost: '$0.3120',
      costPerQuestion: '$0.0042'
    };
    for (const measure of NUMBER_MEASURES) {
      expect(formatMeasureSample(measure, DEFAULT_MEASURE_DECIMALS[measure])).withContext(measure).toBe(expected[measure]);
    }
    expect(formatMeasureSample('meanModelTime', 2, { value: 850, unit: 'ms' })).toBe('850 ms');
    expect(formatMeasureSample('meanModelTime', 2, { value: 870, unit: 's' })).toBe('0.87 s');
  });

  it('maps every speed and cost measure', () => {
    expect(speedNumberMeasure('meanModelTime')).toBe('meanModelTime');
    expect(speedNumberMeasure('totalModelTime')).toBe('totalModelTime');
    expect(speedNumberMeasure('ttftP50')).toBe('ttftP50');
    expect(speedNumberMeasure('speedIndex')).toBe('speedIndex');
    expect(costNumberMeasure('candidateSuite')).toBe('suiteCost');
    expect(costNumberMeasure('totalRun')).toBe('totalRunCost');
  });

  it('rounds and clamps decimal settings, and falls back for anything that is not a finite number', () => {
    expect(normalizeMeasureDecimals(0, 4)).toBe(0);
    expect(normalizeMeasureDecimals(6, 4)).toBe(6);
    expect(normalizeMeasureDecimals(2.4, 4)).toBe(2);
    expect(normalizeMeasureDecimals(2.5, 4)).toBe(3);
    expect(normalizeMeasureDecimals(9, 4)).toBe(6);
    expect(normalizeMeasureDecimals(-2, 4)).toBe(0);
    expect(normalizeMeasureDecimals(-0.4, 4)).toBe(0);
    for (const value of ['2', null, undefined, Number.NaN, Number.POSITIVE_INFINITY, Number.NEGATIVE_INFINITY, {}, [2], true]) {
      expect(normalizeMeasureDecimals(value, 4)).withContext(String(value)).toBe(4);
    }
  });

  it('writes fixed-point text with trailing zeros, from 0 to 6 decimals', () => {
    expect(formatMeasure(71.4, 'intelligenceIndex', 0)).toBe('71');
    expect(formatMeasure(71.4, 'intelligenceIndex', 6)).toBe('71.400000');
    expect(formatMeasure(0.0761, 'suiteCost', 0)).toBe('$0');
    expect(formatMeasure(0.0761, 'suiteCost', 2)).toBe('$0.08');
    expect(formatMeasure(0.0761, 'suiteCost', 6)).toBe('$0.076100');
    expect(formatMeasure(22470, 'meanModelTime', 0, 's')).toBe('22 s');
    expect(formatMeasure(22470, 'meanModelTime', 1, 's')).toBe('22.5 s');
    expect(formatMeasure(22470, 'meanModelTime', 6, 's')).toBe('22.470000 s');
    expect(formatMeasure(1234567, 'totalModelTime', 1, 's')).toBe('1234.6 s');
  });

  it('uses the measure default for an invalid or fractional decimal setting', () => {
    expect(formatMeasure(0.0761, 'suiteCost', Number.NaN)).toBe('$0.0761');
    expect(formatMeasure(71.44, 'intelligenceIndex', 1.6)).toBe('71.44');
    expect(formatMeasure(71.44, 'intelligenceIndex', 12)).toBe('71.440000');
    expect(formatMeasure(71.44, 'intelligenceIndex', -1)).toBe('71');
  });

  it('never writes a minus sign on a value that rounds to zero, in any unit', () => {
    expect(formatMeasure(-0.004, 'intelligenceIndex', 2)).toBe('0.00');
    expect(formatMeasure(-0.4, 'speedIndex', 0)).toBe('0');
    expect(formatMeasure(-0, 'speedIndex', 1)).toBe('0.0');
    expect(formatMeasure(-0.00001, 'costPerQuestion', 4)).toBe('$0.0000');
    expect(formatMeasure(-0.3, 'ttftP50', 2, 'ms')).toBe('0 ms');
    expect(formatMeasure(-40, 'meanModelTime', 1, 's')).toBe('0.0 s');
    // A negative value that does not round to zero keeps its sign.
    expect(formatMeasure(-1.5, 'intelligenceIndex', 1)).toBe('-1.5');
    expect(formatMeasure(-1500, 'meanModelTime', 1, 's')).toBe('-1.5 s');
  });

  it('writes a non-finite value as an empty string', () => {
    for (const value of [Number.NaN, Number.POSITIVE_INFINITY, Number.NEGATIVE_INFINITY]) {
      for (const measure of NUMBER_MEASURES) {
        expect(formatMeasure(value, measure, 2, 's')).withContext(`${measure} ${value}`).toBe('');
      }
    }
  });

  it('chooses seconds from 1000 ms without a unit, and follows a given unit either way', () => {
    expect(formatMeasure(999, 'meanModelTime', 1)).toBe('999 ms');
    expect(formatMeasure(1000, 'meanModelTime', 1)).toBe('1.0 s');
    expect(formatMeasure(1250, 'ttftP50', 2)).toBe('1.25 s');
    expect(formatMeasure(870, 'meanModelTime', 1, 's')).toBe('0.9 s');
    expect(formatMeasure(1250, 'ttftP50', 2, 'ms')).toBe('1250 ms');
  });

  it('shows at least whole milliseconds: settings 0-3 read alike on a millisecond display', () => {
    for (const decimals of [0, 1, 2, 3]) {
      expect(formatMeasure(850.3, 'meanModelTime', decimals, 'ms')).withContext(String(decimals)).toBe('850 ms');
    }
    expect(formatMeasure(850.3, 'meanModelTime', 4, 'ms')).toBe('850.3 ms');
    expect(formatMeasure(850.3, 'meanModelTime', 6, 'ms')).toBe('850.300 ms');
    expect(formatMeasure(850, 'meanModelTime', 1, 'ms')).toBe('850 ms');
  });
});
