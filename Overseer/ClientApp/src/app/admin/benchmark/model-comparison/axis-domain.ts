/**
 * Axis domains and tick labels for the comparison scatters and bar panels: which scale type an axis
 * takes, where it starts and ends, which ticks it carries and how each tick is written.
 *
 * Pure TypeScript with no Chart.js, Angular or DOM dependency, so every rule here is testable on
 * plain numbers.
 */

export type ScaleType = 'linear' | 'logarithmic';
export type AxisTickKind = 'time' | 'usd' | 'index';
export type TimeUnit = 's' | 'ms';

/** Hard limits a linear domain is clamped to. An absent side is unbounded. */
export interface AxisBounds {
  readonly min?: number;
  readonly max?: number;
}

export interface LinearDomain {
  readonly min: number;
  readonly max: number;
  readonly step: number;
  readonly ticks: readonly number[];
}

export interface LogDomain {
  readonly min: number;
  readonly max: number;
  readonly ticks: readonly number[];
}

export interface LinearDomainInput {
  /** Each entry's value minus its lower whisker, so no interval is clipped. */
  readonly lows: readonly number[];
  /** Each entry's value plus its upper whisker. */
  readonly highs: readonly number[];
  readonly bounds?: AxisBounds;
  /** The narrowest span the domain may take before padding, so near-ties do not fill the axis. */
  readonly minSpan: number;
  readonly targetTicks?: number;
  readonly padFraction?: number;
}

export interface LogDomainInput {
  readonly lows: readonly number[];
  readonly highs: readonly number[];
}

export interface TickFormat {
  readonly kind: AxisTickKind;
  /** The unit a time tick is written in. Values are always milliseconds. */
  readonly unit?: TimeUnit;
  /** The tick step, in the value's own units (milliseconds for time). Absent on a log axis. */
  readonly step?: number;
}

/** The most ticks an axis carries; past it the labels crowd. */
const MAX_TICKS = 7;
/** The fewest ticks an axis carries; below it the reader cannot interpolate. */
const MIN_TICKS = 3;

/** Relative tolerance for comparisons made on values built from floating-point arithmetic. */
const EPSILON = 1e-9;

const NICE_MANTISSAS = [1, 2, 2.5, 5, 10] as const;

/** `mantissa x 10^exponent`, divided rather than multiplied for negative exponents so 2.5e-3 stays exact. */
function scaled(mantissa: number, exponent: number): number {
  return exponent >= 0 ? mantissa * 10 ** exponent : mantissa / 10 ** -exponent;
}

function finite(values: readonly number[]): number[] {
  return values.filter((value) => Number.isFinite(value));
}

/** The smallest of {1, 2, 2.5, 5, 10} x 10^k that is at least `raw`. */
export function niceStep(raw: number): number {
  if (!Number.isFinite(raw) || raw <= 0) {
    return 1;
  }
  const exponent = Math.floor(Math.log10(raw));
  for (const mantissa of NICE_MANTISSAS) {
    const candidate = scaled(mantissa, exponent);
    if (candidate >= raw * (1 - EPSILON)) {
      return candidate;
    }
  }
  return scaled(1, exponent + 1);
}

/** Logarithmic only when every value is positive and they span at least a factor of ten. */
export function chooseScaleType(values: readonly number[]): ScaleType {
  const usable = finite(values);
  if (usable.length === 0 || usable.some((value) => value <= 0)) {
    return 'linear';
  }
  return Math.max(...usable) / Math.min(...usable) >= 10 ? 'logarithmic' : 'linear';
}

/** Decimals a multiple of `step` needs: 1 for 0.5, 1 for 2.5, 2 for 0.25, 0 for 25. */
function decimalsForStep(step: number): number {
  if (!Number.isFinite(step) || step <= 0) {
    return 0;
  }
  const exponent = Math.floor(Math.log10(step) + EPSILON);
  const mantissa = step / scaled(1, exponent);
  const extra = Math.abs(mantissa - 2.5) < 1e-6 ? 1 : 0;
  return Math.max(0, -exponent + extra);
}

/** Removes the binary tail repeated arithmetic leaves on a multiple of `step`. */
function cleanMultiple(value: number, step: number): number {
  return Number(value.toFixed(Math.min(20, decimalsForStep(step) + 2)));
}

/** Ticks every `step` from `min` to `max`, computed as `min + i * step` so no error accumulates. */
function stepTicks(min: number, max: number, step: number): number[] {
  const ticks: number[] = [];
  const tolerance = step * 1e-6;
  for (let i = 0; i <= 1000; i += 1) {
    const tick = cleanMultiple(min + i * step, step);
    if (tick > max + tolerance) {
      break;
    }
    if (tick >= min - tolerance) {
      ticks.push(Math.min(Math.max(tick, min), max));
    }
  }
  return ticks;
}

/**
 * A linear domain that fits every interval, padded, snapped outward to round values and clamped to
 * the measure's bounds. Never narrower than `minSpan`, so two near-equal models are not pushed to
 * opposite edges.
 */
export function linearDomain(input: LinearDomainInput): LinearDomain {
  const targetTicks = input.targetTicks ?? 5;
  const padFraction = input.padFraction ?? 0.12;
  const bounds = input.bounds ?? {};
  const minSpan = Number.isFinite(input.minSpan) && input.minSpan > 0 ? input.minSpan : 0;

  const lows = finite(input.lows);
  const highs = finite(input.highs);
  let lo: number;
  let hi: number;
  if (lows.length === 0 && highs.length === 0) {
    lo = bounds.min ?? 0;
    hi = lo + (minSpan > 0 ? minSpan : 1);
  } else {
    const all = [...lows, ...highs];
    lo = Math.min(...all);
    hi = Math.max(...all);
  }

  let span = hi - lo;
  const floorSpan = minSpan > 0 ? minSpan : span === 0 ? Math.abs(lo) * 0.1 || 1 : 0;
  if (span < floorSpan) {
    const middle = (lo + hi) / 2;
    lo = middle - floorSpan / 2;
    hi = middle + floorSpan / 2;
    // Slides the widened window back inside the bounds, where it fits, rather than cutting it.
    if (bounds.min !== undefined && lo < bounds.min) {
      hi += bounds.min - lo;
      lo = bounds.min;
    }
    if (bounds.max !== undefined && hi > bounds.max) {
      lo -= hi - bounds.max;
      hi = bounds.max;
    }
    span = hi - lo;
  }

  lo -= padFraction * span;
  hi += padFraction * span;

  let step = niceStep((hi - lo) / targetTicks);
  let domain = snap(lo, hi, step, bounds);
  for (let guard = 0; domain.ticks.length > MAX_TICKS && guard < 10; guard += 1) {
    step = niceStep(step * 1.001);
    domain = snap(lo, hi, step, bounds);
  }
  return domain;
}

function snap(lo: number, hi: number, step: number, bounds: AxisBounds): LinearDomain {
  let min = cleanMultiple(Math.floor(lo / step + EPSILON) * step, step);
  let max = cleanMultiple(Math.ceil(hi / step - EPSILON) * step, step);
  if (bounds.min !== undefined) {
    min = Math.max(min, bounds.min);
  }
  if (bounds.max !== undefined) {
    max = Math.min(max, bounds.max);
  }
  if (!(max > min)) {
    max = min + step;
  }
  return { min, max, step, ticks: stepTicks(min, max, step) };
}

/** Log-scale tick mantissas, in the order they are tried. */
const LOG_MANTISSAS_DEFAULT = [1, 2, 5] as const;
const LOG_MANTISSAS_SPARSE = [[1, 3], [1]] as const;
const LOG_MANTISSAS_DENSE = [1, 1.5, 2, 3, 5, 7] as const;

function logTicks(min: number, max: number, mantissas: readonly number[]): number[] {
  const ticks: number[] = [];
  const first = Math.floor(Math.log10(min)) - 1;
  const last = Math.ceil(Math.log10(max)) + 1;
  for (let exponent = first; exponent <= last; exponent += 1) {
    for (const mantissa of mantissas) {
      const tick = scaled(mantissa, exponent);
      if (tick >= min * (1 - EPSILON) && tick <= max * (1 + EPSILON)) {
        ticks.push(tick);
      }
    }
  }
  return ticks;
}

/**
 * A logarithmic domain padded in log space, with ticks on the 1-2-5 sequence. A wide domain thins to
 * 1-3 and then to one tick per decade; a narrow one fills in with 1-1.5-2-3-5-7.
 */
export function logDomain(input: LogDomainInput): LogDomain {
  const positive = [...input.lows, ...input.highs].filter((value) => Number.isFinite(value) && value > 0);
  if (positive.length === 0) {
    return { min: 1, max: 10, ticks: [1, 2, 5, 10] };
  }
  const lowLog = Math.log10(Math.min(...positive));
  const highLog = Math.log10(Math.max(...positive));
  const pad = Math.max(0.05, 0.08 * (highLog - lowLog));
  const min = 10 ** (lowLog - pad);
  const max = 10 ** (highLog + pad);

  let ticks = logTicks(min, max, LOG_MANTISSAS_DEFAULT);
  if (ticks.length > MAX_TICKS) {
    for (const mantissas of LOG_MANTISSAS_SPARSE) {
      ticks = logTicks(min, max, mantissas);
      if (ticks.length <= MAX_TICKS) {
        break;
      }
    }
  } else if (ticks.length < MIN_TICKS) {
    ticks = logTicks(min, max, LOG_MANTISSAS_DENSE);
  }
  return { min, max, ticks };
}

/** Seconds once the domain reaches 1000 ms, milliseconds below. The title and the ticks share it. */
export function timeUnitFor(maxMs: number): TimeUnit {
  return maxMs >= 1000 ? 's' : 'ms';
}

/** Three significant digits: the decimals a log tick needs, taken from its own magnitude. */
function decimalsForMagnitude(value: number): number {
  if (value === 0 || !Number.isFinite(value)) {
    return 0;
  }
  return Math.max(0, -Math.floor(Math.log10(Math.abs(value)) + EPSILON) + 2);
}

/**
 * One tick label: `22 s`, `22.5 s`, `850 ms`, `$0.0025`, `75`. The decimals follow the step, or on a
 * log axis the tick's own magnitude, and trailing zeros are dropped.
 */
export function formatTick(value: number, format: TickFormat): string {
  if (!Number.isFinite(value)) {
    return '';
  }
  const inSeconds = format.kind === 'time' && format.unit === 's';
  const shown = inSeconds ? value / 1000 : value;
  const step = format.step !== undefined && format.step > 0
    ? (inSeconds ? format.step / 1000 : format.step)
    : undefined;
  const decimals = Math.min(20, step !== undefined ? decimalsForStep(step) : decimalsForMagnitude(shown));

  let text = shown.toFixed(decimals);
  if (text.includes('.')) {
    text = text.replace(/\.?0+$/, '');
  }
  if (/^-0(\.0*)?$/.test(text)) {
    text = '0';
  }

  switch (format.kind) {
    case 'time':
      return `${text} ${format.unit ?? 'ms'}`;
    case 'usd':
      return `$${text}`;
    case 'index':
      return text;
  }
}
