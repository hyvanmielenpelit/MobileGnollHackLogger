/**
 * The admin-adjustable style of the comparison figures: one set for the three bar panels, one for
 * the three trade-off scatters and one for the profile. The chart builders read it, so the page,
 * the preview and every export draw from the same values.
 *
 * Pure TypeScript with no Chart.js, Angular or DOM dependency.
 */

import type { FigureBadgeKind } from './figure-chrome';

export type BarOrientationChoice = 'auto' | 'vertical' | 'horizontal';
export type ScatterLegendPosition = 'bottom' | 'right';

export interface BarFigureStyle {
  readonly orientation: BarOrientationChoice;
  /** Space between bars, as a percentage of each model's slot. */
  readonly gapPercent: number;
  /** Null means no limit. */
  readonly maxBarWidthPx: number | null;
  readonly cornerRadiusPx: number;
  readonly outlineWidthPx: number;
  /** Single-run bars drawn solid rather than as an outline; multi-run bars are always solid. */
  readonly filledBars: boolean;
  /** Uncertainty bars (whiskers). */
  readonly intervals: boolean;
  /** Caption note while the intervals are hidden. */
  readonly hiddenIntervalsNote: boolean;
  /** Speed panel note: mean time per question has no uncertainty bar. */
  readonly meanTimeNoIntervalNote: boolean;
  readonly valueLabels: boolean;
  readonly valueLabelSizePx: number;
  /** Ticks; axis titles are this + 1. */
  readonly axisTextSizePx: number;
  /** `n = 1` under a single-run model's name. */
  readonly singleRunMarker: boolean;
  readonly gridlines: boolean;
  readonly hiddenBadges: readonly FigureBadgeKind[];
}

export interface ScatterFigureStyle {
  readonly markRadiusPx: number;
  /** Uncertainty bars (whiskers) on both axes. */
  readonly intervals: boolean;
  /** Caption note while the intervals are hidden. */
  readonly hiddenIntervalsNote: boolean;
  /** Caption note: frontier differences within the intervals. */
  readonly frontierIntervalsNote: boolean;
  /** The region the frontier beats on both axes. */
  readonly dominatedShading: boolean;
  readonly frontierWidthPx: number;
  /** Direct-label name; value lines are this - 1. */
  readonly labelTextSizePx: number;
  /** Ticks and the Better marker; axis titles are this + 1. */
  readonly axisTextSizePx: number;
  readonly legendPosition: ScatterLegendPosition;
  readonly gridlines: boolean;
  readonly hiddenBadges: readonly FigureBadgeKind[];
}

export interface ProfileFigureStyle {
  readonly hiddenBadges: readonly FigureBadgeKind[];
}

/** Three families: the bar panels, the trade-off scatters and the profile. */
export interface FigureStyle {
  readonly bar: BarFigureStyle;
  readonly scatter: ScatterFigureStyle;
  readonly profile: ProfileFigureStyle;
}

/** Equal to the values the figures were drawn with before any control existed. */
export const DEFAULT_FIGURE_STYLE: FigureStyle = {
  bar: {
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
    singleRunMarker: true,
    gridlines: true,
    hiddenBadges: [],
  },
  scatter: {
    markRadiusPx: 6,
    intervals: true,
    hiddenIntervalsNote: true,
    frontierIntervalsNote: true,
    dominatedShading: true,
    frontierWidthPx: 2,
    labelTextSizePx: 11,
    axisTextSizePx: 11,
    legendPosition: 'bottom',
    gridlines: true,
    hiddenBadges: [],
  },
  profile: {
    hiddenBadges: [],
  },
};

/** The caption note a figure carries when its uncertainty bars are hidden and would have drawn. */
export const HIDDEN_INTERVALS_NOTE =
  'Uncertainty bars are hidden in this figure, so it does not show how precise each value is.';

export type NumericBarStyleKey = 'gapPercent' | 'maxBarWidthPx' | 'cornerRadiusPx' | 'outlineWidthPx'
  | 'valueLabelSizePx' | 'axisTextSizePx';
export type NumericScatterStyleKey = 'markRadiusPx' | 'frontierWidthPx' | 'labelTextSizePx' | 'axisTextSizePx';

/** One range control: its label, bounds and unit, which the style panel's template loops over. */
export interface RangeControl<TKey extends string> {
  readonly key: TKey;
  readonly label: string;
  readonly hint?: string;
  readonly min: number;
  readonly max: number;
  readonly step: number;
  readonly unit: 'px' | '%';
}

export const BAR_RANGE_CONTROLS: readonly RangeControl<NumericBarStyleKey>[] = [
  { key: 'gapPercent', label: 'Space between bars', min: 0, max: 90, step: 1, unit: '%' },
  { key: 'maxBarWidthPx', label: 'Maximum bar width', min: 8, max: 480, step: 1, unit: 'px' },
  { key: 'cornerRadiusPx', label: 'Corner radius', min: 0, max: 16, step: 1, unit: 'px' },
  {
    key: 'outlineWidthPx',
    label: 'Outline width',
    hint: 'Unless the bars are filled, a single-run bar is drawn as an outline only, so the outline never goes below 1 px.',
    min: 1,
    max: 6,
    step: 1,
    unit: 'px',
  },
  { key: 'axisTextSizePx', label: 'Axis text size', hint: 'Axis titles are 1 px larger.', min: 8, max: 28, step: 1, unit: 'px' },
  { key: 'valueLabelSizePx', label: 'Value text size', min: 8, max: 28, step: 1, unit: 'px' },
];

export const SCATTER_RANGE_CONTROLS: readonly RangeControl<NumericScatterStyleKey>[] = [
  { key: 'markRadiusPx', label: 'Mark size', min: 3, max: 14, step: 1, unit: 'px' },
  { key: 'frontierWidthPx', label: 'Frontier line width', min: 1, max: 6, step: 1, unit: 'px' },
  { key: 'labelTextSizePx', label: 'Label text size', min: 8, max: 24, step: 1, unit: 'px' },
  { key: 'axisTextSizePx', label: 'Axis text size', hint: 'Also sizes the Better marker.', min: 8, max: 28, step: 1, unit: 'px' },
];

/** One badge's *Show* checkbox, which the style panel's template loops over. */
export interface BadgeControl {
  readonly kind: FigureBadgeKind;
  readonly label: string;
  readonly hint?: string;
}

export const BADGE_CONTROLS: readonly BadgeControl[] = [
  { kind: 'models', label: 'Number of models' },
  { kind: 'runs', label: 'Runs behind each model' },
  { kind: 'questions', label: 'Number of questions' },
  { kind: 'pricing', label: 'Pricing basis', hint: 'Only figures with a cost axis carry it.' },
];

/** The descriptor for one key, which every numeric field has. */
export function barRangeControl(key: NumericBarStyleKey): RangeControl<NumericBarStyleKey> {
  return BAR_RANGE_CONTROLS.find((control) => control.key === key)!;
}

export function scatterRangeControl(key: NumericScatterStyleKey): RangeControl<NumericScatterStyleKey> {
  return SCATTER_RANGE_CONTROLS.find((control) => control.key === key)!;
}

/** A number rounded to the control's step and clamped into its range, or the fallback. */
export function clampToControl(value: unknown, control: RangeControl<string>, fallback: number): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return fallback;
  }
  const stepped = Math.round((value - control.min) / control.step) * control.step + control.min;
  return Math.min(control.max, Math.max(control.min, stepped));
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function booleanOr(value: unknown, fallback: boolean): boolean {
  return typeof value === 'boolean' ? value : fallback;
}

function oneOf<T extends string>(value: unknown, allowed: readonly T[], fallback: T): T {
  return typeof value === 'string' && (allowed as readonly string[]).includes(value) ? (value as T) : fallback;
}

/** Known kinds only, each once, in {@link BADGE_CONTROLS} order, so equal selections serialise alike. */
function normalizeBadgeKinds(value: unknown): FigureBadgeKind[] {
  if (!Array.isArray(value)) {
    return [];
  }
  return BADGE_CONTROLS.map((control) => control.kind).filter((kind) => value.includes(kind));
}

function normalizeBar(value: unknown): BarFigureStyle {
  const d = DEFAULT_FIGURE_STYLE.bar;
  const v = isRecord(value) ? value : {};
  const numeric = (key: NumericBarStyleKey): number =>
    clampToControl(v[key], barRangeControl(key), d[key] ?? barRangeControl(key).min);
  return {
    orientation: oneOf(v['orientation'], ['auto', 'vertical', 'horizontal'] as const, d.orientation),
    gapPercent: numeric('gapPercent'),
    maxBarWidthPx: v['maxBarWidthPx'] === null ? null : numeric('maxBarWidthPx'),
    cornerRadiusPx: numeric('cornerRadiusPx'),
    outlineWidthPx: numeric('outlineWidthPx'),
    filledBars: booleanOr(v['filledBars'], d.filledBars),
    intervals: booleanOr(v['intervals'], d.intervals),
    hiddenIntervalsNote: booleanOr(v['hiddenIntervalsNote'], d.hiddenIntervalsNote),
    meanTimeNoIntervalNote: booleanOr(v['meanTimeNoIntervalNote'], d.meanTimeNoIntervalNote),
    valueLabels: booleanOr(v['valueLabels'], d.valueLabels),
    valueLabelSizePx: numeric('valueLabelSizePx'),
    axisTextSizePx: numeric('axisTextSizePx'),
    singleRunMarker: booleanOr(v['singleRunMarker'], d.singleRunMarker),
    gridlines: booleanOr(v['gridlines'], d.gridlines),
    hiddenBadges: normalizeBadgeKinds(v['hiddenBadges']),
  };
}

function normalizeScatter(value: unknown): ScatterFigureStyle {
  const d = DEFAULT_FIGURE_STYLE.scatter;
  const v = isRecord(value) ? value : {};
  const numeric = (key: NumericScatterStyleKey): number =>
    clampToControl(v[key], scatterRangeControl(key), d[key]);
  return {
    markRadiusPx: numeric('markRadiusPx'),
    intervals: booleanOr(v['intervals'], d.intervals),
    hiddenIntervalsNote: booleanOr(v['hiddenIntervalsNote'], d.hiddenIntervalsNote),
    frontierIntervalsNote: booleanOr(v['frontierIntervalsNote'], d.frontierIntervalsNote),
    dominatedShading: booleanOr(v['dominatedShading'], d.dominatedShading),
    frontierWidthPx: numeric('frontierWidthPx'),
    labelTextSizePx: numeric('labelTextSizePx'),
    axisTextSizePx: numeric('axisTextSizePx'),
    legendPosition: oneOf(v['legendPosition'], ['bottom', 'right'] as const, d.legendPosition),
    gridlines: booleanOr(v['gridlines'], d.gridlines),
    hiddenBadges: normalizeBadgeKinds(v['hiddenBadges']),
  };
}

function normalizeProfile(value: unknown): ProfileFigureStyle {
  const v = isRecord(value) ? value : {};
  return { hiddenBadges: normalizeBadgeKinds(v['hiddenBadges']) };
}

/**
 * A usable style from anything, a parsed `localStorage` value included. Every valid field is kept,
 * numbers are rounded and clamped into their ranges, only real booleans are accepted for the
 * checkboxes, unknown keys are dropped and every other field falls back to its default. Never throws.
 */
export function normalizeFigureStyle(value: unknown): FigureStyle {
  const v = isRecord(value) ? value : {};
  return { bar: normalizeBar(v['bar']), scatter: normalizeScatter(v['scatter']), profile: normalizeProfile(v['profile']) };
}
