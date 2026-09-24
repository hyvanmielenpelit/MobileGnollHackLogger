/**
 * Decimal places per measure for the comparison figures' value text: bar labels, scatter value
 * plates, tooltips and the profile's real-value ranges. Axis ticks keep their own step-driven
 * precision (`formatTick` in `axis-domain.ts`).
 *
 * Pure TypeScript with no Chart.js, Angular or DOM dependency; the chart module is imported for its
 * types only.
 */

import { timeUnitFor } from './axis-domain';
import type { TimeUnit } from './axis-domain';
import type { CostMeasure, SpeedMeasure } from './model-comparison-charts';

export type NumberMeasure =
  | 'intelligenceIndex'
  | 'speedIndex'
  | 'meanModelTime'
  | 'totalModelTime'
  | 'ttftP50'
  | 'suiteCost'
  | 'totalRunCost'
  | 'costPerQuestion';

/** Decimal places per measure, each an integer 0 to {@link MAX_MEASURE_DECIMALS}. Time counts in seconds. */
export type NumberFormatStyle = Readonly<Record<NumberMeasure, number>>;

/** One measured value a select's options are previewed on, in stored units (ms, USD, index points). */
export interface NumberSample {
  readonly value: number;
  /** The unit the figure family writes this time in; absent for indices and costs. */
  readonly unit?: TimeUnit;
}

/** The samples of one figure family; a measure absent here previews on its fixed example. */
export type NumberSamples = Readonly<Partial<Record<NumberMeasure, NumberSample>>>;

export const MAX_MEASURE_DECIMALS = 6;

/** Every measure, in the order the controls list them. */
export const NUMBER_MEASURES: readonly NumberMeasure[] = [
  'intelligenceIndex',
  'speedIndex',
  'meanModelTime',
  'totalModelTime',
  'ttftP50',
  'suiteCost',
  'totalRunCost',
  'costPerQuestion',
];

export const DEFAULT_MEASURE_DECIMALS: NumberFormatStyle = {
  intelligenceIndex: 0,
  speedIndex: 0,
  meanModelTime: 1,
  totalModelTime: 1,
  ttftP50: 2,
  suiteCost: 4,
  totalRunCost: 4,
  costPerQuestion: 4,
};

export const MEASURE_NAMES: Readonly<Record<NumberMeasure, string>> = {
  intelligenceIndex: 'Intelligence Index',
  speedIndex: 'Speed Index',
  meanModelTime: 'Mean time per question',
  totalModelTime: 'Total time for the suite',
  ttftP50: 'Time to first token, median',
  suiteCost: 'Candidate cost of one suite run',
  totalRunCost: 'Total run cost',
  costPerQuestion: 'Cost per question',
};

/** The read-out's names. The two suite costs are never shown together, so both read *Cost*. */
export const MEASURE_SHORT_NAMES: Readonly<Record<NumberMeasure, string>> = {
  intelligenceIndex: 'Intelligence',
  speedIndex: 'Speed Index',
  meanModelTime: 'Mean time',
  totalModelTime: 'Suite time',
  ttftP50: 'TTFT',
  suiteCost: 'Cost',
  totalRunCost: 'Cost',
  costPerQuestion: 'Cost / question',
};

/** The value an option previews on when the plotted set has none, in stored units. */
export const MEASURE_EXAMPLES: Readonly<Record<NumberMeasure, number>> = {
  intelligenceIndex: 71,
  speedIndex: 64,
  meanModelTime: 22500,
  totalModelTime: 504000,
  ttftP50: 1250,
  suiteCost: 0.0761,
  totalRunCost: 0.312,
  costPerQuestion: 0.0042,
};

type MeasureKind = 'index' | 'time' | 'usd';

const MEASURE_KINDS: Readonly<Record<NumberMeasure, MeasureKind>> = {
  intelligenceIndex: 'index',
  speedIndex: 'index',
  meanModelTime: 'time',
  totalModelTime: 'time',
  ttftP50: 'time',
  suiteCost: 'usd',
  totalRunCost: 'usd',
  costPerQuestion: 'usd',
};

/** A finite number rounded and clamped to 0-6, or the fallback for anything else. */
export function normalizeMeasureDecimals(value: unknown, fallback: number): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return fallback;
  }
  return Math.min(MAX_MEASURE_DECIMALS, Math.max(0, Math.round(value)));
}

export function speedNumberMeasure(measure: SpeedMeasure): NumberMeasure {
  switch (measure) {
    case 'meanModelTime':
      return 'meanModelTime';
    case 'totalModelTime':
      return 'totalModelTime';
    case 'ttftP50':
      return 'ttftP50';
    case 'speedIndex':
      return 'speedIndex';
  }
}

export function costNumberMeasure(measure: CostMeasure): NumberMeasure {
  switch (measure) {
    case 'candidateSuite':
      return 'suiteCost';
    case 'totalRun':
      return 'totalRunCost';
  }
}

/** Fixed-point text with its trailing zeros, where a value that rounds to zero never keeps a minus sign. */
function fixed(value: number, decimals: number): string {
  const text = value.toFixed(decimals);
  return /^-0(\.0*)?$/.test(text) ? text.slice(1) : text;
}

/**
 * One value as the figures write it: `71`, `22.5 s`, `850 ms`, `$0.0761`. Time decimals count in
 * seconds, so a millisecond display shows `decimals - 3` of them and never fewer than whole
 * milliseconds. A figure passes its time axis's unit; without one the value's own magnitude decides.
 * A non-finite value is written as `''`.
 */
export function formatMeasure(value: number, measure: NumberMeasure, decimals: number, unit?: TimeUnit): string {
  if (!Number.isFinite(value)) {
    return '';
  }
  const places = normalizeMeasureDecimals(decimals, DEFAULT_MEASURE_DECIMALS[measure]);
  switch (MEASURE_KINDS[measure]) {
    case 'index':
      return fixed(value, places);
    case 'usd':
      return `$${fixed(value, places)}`;
    case 'time':
      return (unit ?? timeUnitFor(value)) === 's'
        ? `${fixed(value / 1000, places)} s`
        : `${fixed(value, Math.max(0, places - 3))} ms`;
  }
}

/** What an option previews: the family's sample at `decimals`, or the measure's fixed example. */
export function formatMeasureSample(measure: NumberMeasure, decimals: number, sample?: NumberSample): string {
  return sample
    ? formatMeasure(sample.value, measure, decimals, sample.unit)
    : formatMeasure(MEASURE_EXAMPLES[measure], measure, decimals);
}
