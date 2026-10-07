import {
  MINUS,
  NO_VALUE,
  addUtcDays,
  annotationKindText,
  axisText,
  ccNumber,
  endOfUtcDay,
  endpointEstimateText,
  endpointMdeText,
  formatFixed,
  formatFractionPercent,
  formatInteger,
  formatInterval,
  formatMs,
  formatPValue,
  formatSigned,
  formatSignedPercent,
  formatTokenRate,
  formatUsd,
  formatUtcDate,
  formatUtcDateTime,
  gradeText,
  isUtcDateInput,
  parseUtc,
  plural,
  regradeStatusText,
  runStatusText,
  startOfUtcDay,
  utcDateTimeInputToIso,
  verdictText,
  withinUtcDays
} from './chat-consistency-format';
import { ccEndpoint } from './chat-consistency-tab.testing';

describe('chat-consistency-format', () => {
  describe('numbers', () => {
    it('reads JSON numbers and named literals, and turns NaN and Infinity into null', () => {
      expect(ccNumber(1.5)).toBe(1.5);
      expect(ccNumber('2.25')).toBe(2.25);
      expect(ccNumber('NaN')).toBeNull();
      expect(ccNumber('Infinity')).toBeNull();
      expect(ccNumber('-Infinity')).toBeNull();
      expect(ccNumber(Number.NaN)).toBeNull();
      expect(ccNumber(null)).toBeNull();
      expect(ccNumber(undefined)).toBeNull();
      expect(ccNumber('')).toBeNull();
    });

    it('formats fixed and signed values with the typographic minus and no sign on zero', () => {
      expect(formatFixed(3.14159, 2)).toBe('3.14');
      expect(formatFixed(-0.04, 1)).toBe('0.0');
      expect(formatFixed(-2.5, 1)).toBe(`${MINUS}2.5`);
      expect(formatFixed('NaN')).toBe(NO_VALUE);
      expect(formatSigned(1.234)).toBe('+1.2');
      expect(formatSigned(-1.25, 2)).toBe(`${MINUS}1.25`);
      expect(formatSigned(0.01)).toBe('0.0');
    });

    it('groups integers by thousands', () => {
      expect(formatInteger(1234567.4)).toBe('1,234,567');
      expect(formatInteger(999)).toBe('999');
      expect(formatInteger(-4321)).toBe(`${MINUS}4,321`);
    });

    it('formats percentages, times, rates and dollars invariantly', () => {
      expect(formatFractionPercent(0.025)).toBe('2.5 %');
      expect(formatSignedPercent(4.21)).toBe('+4.2 %');
      expect(formatMs(850)).toBe('850 ms');
      expect(formatMs(2430)).toBe('2.4 s');
      expect(formatTokenRate(41.96)).toBe('42.0 tok/s');
      expect(formatUsd(1.234)).toBe('$1.23');
      expect(formatUsd(0.0123)).toBe('$0.012');
      expect(formatUsd(0.00421)).toBe('$0.0042');
      expect(formatUsd(null)).toBe(NO_VALUE);
    });

    it('writes p-values and intervals', () => {
      expect(formatPValue(0.0004)).toBe('< 0.001');
      expect(formatPValue(0.0321)).toBe('0.032');
      expect(formatInterval({ lower: -1.24, upper: 3.4 })).toBe(`[${MINUS}1.2, +3.4]`);
      expect(formatInterval({ lower: 'NaN', upper: 1 })).toBe(NO_VALUE);
      expect(formatInterval(null)).toBe(NO_VALUE);
    });
  });

  describe('endpoint estimates', () => {
    it('writes a difference endpoint in its unit with the 95 % interval', () => {
      const p1 = ccEndpoint('P1', { estimate: -4.2, ci95: { lower: -6.1, upper: -2.3 }, unit: 'index points' });
      expect(endpointEstimateText(p1)).toBe(`${MINUS}4.2 index points (95 % CI [${MINUS}6.1, ${MINUS}2.3])`);
      expect(endpointMdeText({ ...p1, minimumDetectableEffect: 2.04 })).toBe('±2.0 index points');
    });

    it('writes a log-ratio endpoint as a signed percentage', () => {
      const p2 = ccEndpoint('P2', { scale: 'logRatio', estimatePercent: 18.4, ci95Percent: { lower: 9.1, upper: 28.7 } });
      expect(endpointEstimateText(p2)).toBe('+18.4 % (95 % CI [+9.1, +28.7] %)');
      expect(endpointMdeText({ ...p2, minimumDetectableEffectPercent: 11.25 })).toBe('±11.3 %');
    });

    it('says Not computed for an endpoint that was not computed', () => {
      expect(endpointEstimateText(ccEndpoint('P3', { computed: false }))).toBe('Not computed');
    });
  });

  describe('dates', () => {
    it('reads a time without an offset as UTC', () => {
      expect(parseUtc('2026-10-03T14:05:00').toISOString()).toBe('2026-10-03T14:05:00.000Z');
      expect(parseUtc('2026-10-03T14:05:00Z').toISOString()).toBe('2026-10-03T14:05:00.000Z');
      expect(parseUtc('2026-10-03T16:05:00+02:00').toISOString()).toBe('2026-10-03T14:05:00.000Z');
      expect(parseUtc('2026-10-03').toISOString()).toBe('2026-10-03T00:00:00.000Z');
    });

    it('formats UTC dates and times', () => {
      expect(formatUtcDate('2026-10-03T23:30:00Z')).toBe('2026-10-03');
      expect(formatUtcDateTime('2026-10-03T04:05:00Z')).toBe('2026-10-03 04:05 UTC');
      expect(formatUtcDate(null)).toBe(NO_VALUE);
      expect(formatUtcDate('not a date')).toBe(NO_VALUE);
    });

    it('turns date inputs into the bounds of the UTC day', () => {
      expect(isUtcDateInput('2026-02-30')).toBe(false);
      expect(isUtcDateInput('2026-02-28')).toBe(true);
      expect(startOfUtcDay('2026-10-01')).toBe('2026-10-01T00:00:00.000Z');
      expect(endOfUtcDay('2026-10-01')).toBe('2026-10-01T23:59:59.999Z');
      expect(startOfUtcDay('')).toBeNull();
      expect(addUtcDays('2026-10-01', 14)).toBe('2026-10-15');
      expect(addUtcDays('2026-03-01', -1)).toBe('2026-02-28');
    });

    it('tests an instant against an inclusive day range', () => {
      expect(withinUtcDays('2026-10-01T23:59:00Z', '2026-09-20', '2026-10-01')).toBe(true);
      expect(withinUtcDays('2026-10-02T00:00:00Z', '2026-09-20', '2026-10-01')).toBe(false);
    });

    it('reads a datetime-local value as UTC', () => {
      expect(utcDateTimeInputToIso('2026-10-01T12:30')).toBe('2026-10-01T12:30:00.000Z');
      expect(utcDateTimeInputToIso('2026-10-01')).toBeNull();
    });
  });

  describe('labels', () => {
    it('labels verdicts, preferring the server wording', () => {
      expect(verdictText('changedDegraded')).toBe('Degraded');
      expect(verdictText('changedDegraded', 'more work')).toBe('More work');
      expect(verdictText(null)).toBe('Not computable');
    });

    it('labels grades, axes, run and re-grade states and annotation kinds', () => {
      expect(gradeText('notEstablished')).toBe('Not established');
      expect(axisText('speedTelemetry')).toBe('Speed (telemetry)');
      expect(runStatusText('completedWithErrors')).toBe('Completed with errors');
      expect(regradeStatusText('canceled')).toBe('Canceled');
      expect(annotationKindText('providerConfirmedCause')).toBe('Provider confirmed a cause');
      expect(plural(1, 'run')).toBe('1 run');
      expect(plural(1200, 'run')).toBe('1,200 runs');
    });
  });
});
