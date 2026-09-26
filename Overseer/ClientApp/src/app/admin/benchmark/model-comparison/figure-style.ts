/**
 * The admin-adjustable style of the comparison figures: one set for the three bar panels, one for
 * the three trade-off scatters and one for the profile. The chart builders read it, so the page,
 * the preview and every export draw from the same values.
 *
 * Pure TypeScript with no Chart.js, Angular or DOM dependency.
 */

import type { FigureBadgeKind } from './figure-chrome';
import { DEFAULT_MEASURE_DECIMALS, NUMBER_MEASURES, normalizeMeasureDecimals } from './measure-format';
import type { NumberFormatStyle, NumberMeasure } from './measure-format';

export type BarOrientationChoice = 'auto' | 'vertical' | 'horizontal';
export type ScatterLegendPosition = 'bottom' | 'right';
/** Whether a bar value-axis title puts its final parenthetical on a line of its own. */
export type AxisTitleBreak = 'auto' | 'always' | 'never';

export type FigureThemeName = 'dark' | 'light';
export type FigureBackgroundMode = 'theme' | 'transparent' | 'custom';
/** What the page draws behind a transparent figure; never part of an export. */
export type FigurePreviewBackdrop = 'checkerboard' | 'color';
export type FigureFontId = 'default' | 'inter' | 'roboto' | 'geist' | 'ibm-plex-sans' | 'source-sans-3' | 'open-sans';
export type FigureFontWeight = 400 | 500 | 600 | 700;
/** How strongly the table image shades every second body row. */
export type TableRowShading = 'none' | 'light' | 'medium' | 'strong';

export const FIGURE_THEME_NAMES: readonly FigureThemeName[] = ['dark', 'light'];
export const FIGURE_BACKGROUND_MODES: readonly FigureBackgroundMode[] = ['theme', 'transparent', 'custom'];
export const FIGURE_PREVIEW_BACKDROPS: readonly FigurePreviewBackdrop[] = ['checkerboard', 'color'];
export const FIGURE_FONT_IDS: readonly FigureFontId[] =
  ['default', 'inter', 'roboto', 'geist', 'ibm-plex-sans', 'source-sans-3', 'open-sans'];
export const FIGURE_FONT_WEIGHTS: readonly FigureFontWeight[] = [400, 500, 600, 700];
export const TABLE_ROW_SHADINGS: readonly TableRowShading[] = ['none', 'light', 'medium', 'strong'];

/** Shared by every chart and the table image. */
export interface FigureAppearanceStyle {
  readonly theme: FigureThemeName;
  readonly background: FigureBackgroundMode;
  /** `#rrggbb`, painted while `background` is `custom`. */
  readonly backgroundColor: string;
  readonly previewBackdrop: FigurePreviewBackdrop;
  /** `#rrggbb`, shown while the backdrop is `color`. */
  readonly previewBackdropColor: string;
  readonly fontFamily: FigureFontId;
  /** Chart titles; the table's title and header row. */
  readonly headingWeight: FigureFontWeight;
  /** Value labels, direct labels and legends; the table's cells. */
  readonly labelWeight: FigureFontWeight;
  /** Null follows the theme. */
  readonly headingColor: string | null;
  /** Null follows the theme. */
  readonly textColor: string | null;
  readonly border: boolean;
  readonly borderWidthPx: number;
  readonly borderRadiusPx: number;
  /** Null follows the theme. */
  readonly borderColor: string | null;
}

/** Table image only. */
export interface TableImageStyle {
  /** Alternate-row background; its color is derived from the theme's text color. */
  readonly rowShading: TableRowShading;
  /** A hairline under every row. */
  readonly rowRules: boolean;
}

/** The figure's caption text, on the page card and in the composed figure. */
export interface FigureChromeStyle {
  readonly titleSizePx: number;
  readonly badgeTextSizePx: number;
  readonly footerTextSizePx: number;
  /** The suite and computation-time line under the composed figure. */
  readonly footer: boolean;
}

export interface BarFigureStyle extends FigureChromeStyle {
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
  /** Tick and category labels. */
  readonly axisTextSizePx: number;
  readonly axisTitleSizePx: number;
  /** Automatic breaks the value-axis title only where the unbroken title is longer than its axis. */
  readonly axisTitleBreak: AxisTitleBreak;
  /** `n = 1` under a single-run model's name. */
  readonly singleRunMarker: boolean;
  /** The thinking level on a line of its own under the model name. */
  readonly thinkingLevelBreak: boolean;
  readonly gridlines: boolean;
  readonly axisTitleWeight: FigureFontWeight;
  /** A hairline box around the plot area. */
  readonly plotFrame: boolean;
  readonly hiddenBadges: readonly FigureBadgeKind[];
}

export interface ScatterFigureStyle extends FigureChromeStyle {
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
  /** Tick labels. */
  readonly axisTextSizePx: number;
  readonly axisTitleSizePx: number;
  readonly legendPosition: ScatterLegendPosition;
  /** The thinking level on a line of its own under the model name. */
  readonly thinkingLevelBreak: boolean;
  readonly gridlines: boolean;
  readonly axisTitleWeight: FigureFontWeight;
  /** A hairline box around the plot area. */
  readonly plotFrame: boolean;
  readonly hiddenBadges: readonly FigureBadgeKind[];
}

export interface ProfileFigureStyle extends FigureChromeStyle {
  readonly hiddenBadges: readonly FigureBadgeKind[];
}

/** Three families: the bar panels, the trade-off scatters and the profile, and the number formats they share. */
export interface FigureStyle {
  readonly bar: BarFigureStyle;
  readonly scatter: ScatterFigureStyle;
  readonly profile: ProfileFigureStyle;
  /** Decimal places per measure, in every family that shows the measure. */
  readonly numbers: NumberFormatStyle;
  /** Theme, background, fonts and border of every chart and the table image. */
  readonly appearance: FigureAppearanceStyle;
  readonly table: TableImageStyle;
}

/** Dark, on the theme background, in the Overseer font, without a border: the drawing before any theme existed. */
export const DEFAULT_APPEARANCE_STYLE: FigureAppearanceStyle = {
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
};

export const DEFAULT_TABLE_IMAGE_STYLE: TableImageStyle = {
  rowShading: 'medium',
  rowRules: false,
};

/** The composer's caption sizes and a shown footer. */
const DEFAULT_CHROME_STYLE: FigureChromeStyle = {
  titleSizePx: 18,
  badgeTextSizePx: 11,
  footerTextSizePx: 12,
  footer: true,
};

/**
 * The style a browser without a stored one starts from. The sizes, spacing and toggles equal the
 * drawing before any control existed; the value text follows {@link DEFAULT_MEASURE_DECIMALS}.
 */
export const DEFAULT_FIGURE_STYLE: FigureStyle = {
  bar: {
    ...DEFAULT_CHROME_STYLE,
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
  },
  scatter: {
    ...DEFAULT_CHROME_STYLE,
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
    axisTitleWeight: 400,
    plotFrame: false,
    hiddenBadges: [],
  },
  profile: {
    ...DEFAULT_CHROME_STYLE,
    hiddenBadges: [],
  },
  numbers: DEFAULT_MEASURE_DECIMALS,
  appearance: DEFAULT_APPEARANCE_STYLE,
  table: DEFAULT_TABLE_IMAGE_STYLE,
};

/** The caption note a figure carries when its uncertainty bars are hidden and would have drawn. */
export const HIDDEN_INTERVALS_NOTE =
  'Uncertainty bars are hidden in this figure, so it does not show how precise each value is.';

/** The largest size any text-size control offers. */
export const MAX_TEXT_SIZE_PX = 48;
const MIN_TEXT_SIZE_PX = 8;

export type NumericBarStyleKey = 'gapPercent' | 'maxBarWidthPx' | 'cornerRadiusPx' | 'outlineWidthPx'
  | 'valueLabelSizePx' | 'axisTextSizePx' | 'axisTitleSizePx';
export type NumericScatterStyleKey = 'markRadiusPx' | 'frontierWidthPx' | 'labelTextSizePx' | 'axisTextSizePx'
  | 'axisTitleSizePx';
export type NumericChromeStyleKey = 'titleSizePx' | 'badgeTextSizePx' | 'footerTextSizePx';

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

function textSizeControl<TKey extends string>(key: TKey, label: string, hint?: string): RangeControl<TKey> {
  return { key, label, ...(hint ? { hint } : {}), min: MIN_TEXT_SIZE_PX, max: MAX_TEXT_SIZE_PX, step: 1, unit: 'px' };
}

export const BAR_RANGE_CONTROLS: readonly RangeControl<NumericBarStyleKey>[] = [
  { key: 'gapPercent', label: 'Space between bars', hint: "Share of each model's slot left empty.", min: 0, max: 90, step: 1, unit: '%' },
  {
    key: 'maxBarWidthPx',
    label: 'Maximum bar width',
    hint: 'Past this width the space between bars grows instead.',
    min: 8,
    max: 480,
    step: 1,
    unit: 'px',
  },
  { key: 'cornerRadiusPx', label: 'Corner radius', min: 0, max: 16, step: 1, unit: 'px' },
  { key: 'outlineWidthPx', label: 'Outline width', hint: 'Outlined bars need at least 1 px.', min: 1, max: 6, step: 1, unit: 'px' },
  textSizeControl('valueLabelSizePx', 'Value labels'),
  textSizeControl('axisTextSizePx', 'Axis values'),
  textSizeControl('axisTitleSizePx', 'Axis titles'),
];

export const SCATTER_RANGE_CONTROLS: readonly RangeControl<NumericScatterStyleKey>[] = [
  { key: 'markRadiusPx', label: 'Mark size', min: 3, max: 14, step: 1, unit: 'px' },
  { key: 'frontierWidthPx', label: 'Frontier line width', min: 1, max: 6, step: 1, unit: 'px' },
  textSizeControl('labelTextSizePx', 'Model labels'),
  textSizeControl('axisTextSizePx', 'Axis values'),
  textSizeControl('axisTitleSizePx', 'Axis titles'),
];

export const CHROME_RANGE_CONTROLS: readonly RangeControl<NumericChromeStyleKey>[] = [
  textSizeControl('titleSizePx', 'Heading size'),
  textSizeControl('badgeTextSizePx', 'Badge text size'),
  textSizeControl('footerTextSizePx', 'Footer text size'),
];

export type NumericAppearanceStyleKey = 'borderWidthPx' | 'borderRadiusPx';

export const APPEARANCE_RANGE_CONTROLS: readonly RangeControl<NumericAppearanceStyleKey>[] = [
  { key: 'borderWidthPx', label: 'Border width', min: 1, max: 8, step: 1, unit: 'px' },
  {
    key: 'borderRadiusPx',
    label: 'Corner radius',
    hint: 'Also rounds the background.',
    min: 0,
    max: 32,
    step: 1,
    unit: 'px',
  },
];

export function appearanceRangeControl(key: NumericAppearanceStyleKey): RangeControl<NumericAppearanceStyleKey> {
  return APPEARANCE_RANGE_CONTROLS.find((control) => control.key === key)!;
}

/** One badge's *Show* checkbox, which the style panel's template loops over. */
export interface BadgeControl {
  readonly kind: FigureBadgeKind;
  readonly label: string;
  readonly hint?: string;
}

/** Every badge kind, the Better badge first: it ends the badge row on the figure and heads the list. */
export const BADGE_CONTROLS: readonly BadgeControl[] = [
  { kind: 'direction', label: 'Better badge' },
  { kind: 'models', label: 'Number of models' },
  { kind: 'runs', label: 'Runs behind each model' },
  { kind: 'questions', label: 'Number of questions' },
  { kind: 'pricing', label: 'Pricing basis', hint: 'Only on figures with a cost axis.' },
];

const DIRECTION_BADGE_HINTS = {
  bar: 'An arrow toward the better end of the value axis. While shown, the axis title leaves out "higher is better".',
  scatter: 'An arrow toward the better corner of the chart.',
} as const;

/** The badge checkboxes one family offers; the profile has no better direction, so no Better badge. */
export function badgeControlsFor(family: 'bar' | 'scatter' | 'profile'): readonly BadgeControl[] {
  if (family === 'profile') {
    return BADGE_CONTROLS.filter((control) => control.kind !== 'direction');
  }
  return BADGE_CONTROLS.map((control) =>
    control.kind === 'direction' ? { ...control, hint: DIRECTION_BADGE_HINTS[family] } : control);
}

/** The descriptor for one key, which every numeric field has. */
export function barRangeControl(key: NumericBarStyleKey): RangeControl<NumericBarStyleKey> {
  return BAR_RANGE_CONTROLS.find((control) => control.key === key)!;
}

export function scatterRangeControl(key: NumericScatterStyleKey): RangeControl<NumericScatterStyleKey> {
  return SCATTER_RANGE_CONTROLS.find((control) => control.key === key)!;
}

export function chromeRangeControl(key: NumericChromeStyleKey): RangeControl<NumericChromeStyleKey> {
  return CHROME_RANGE_CONTROLS.find((control) => control.key === key)!;
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

const HEX_COLOR = /^#[0-9a-f]{6}$/i;

/** A `#rrggbb` colour, lower-cased, or the fallback. */
export function normalizeHexColor(value: unknown, fallback: string): string {
  return typeof value === 'string' && HEX_COLOR.test(value) ? value.toLowerCase() : fallback;
}

/** A `#rrggbb` colour, lower-cased, or null; anything else takes the fallback. */
function nullableHexColor(value: unknown, fallback: string | null): string | null {
  return value === null ? null : typeof value === 'string' && HEX_COLOR.test(value) ? value.toLowerCase() : fallback;
}

function fontWeightOr(value: unknown, fallback: FigureFontWeight): FigureFontWeight {
  return typeof value === 'number' && (FIGURE_FONT_WEIGHTS as readonly number[]).includes(value)
    ? (value as FigureFontWeight)
    : fallback;
}

/** Known kinds only, each once, in {@link BADGE_CONTROLS} order, so equal selections serialise alike. */
function normalizeBadgeKinds(value: unknown): FigureBadgeKind[] {
  if (!Array.isArray(value)) {
    return [];
  }
  return BADGE_CONTROLS.map((control) => control.kind).filter((kind) => value.includes(kind));
}

function normalizeChrome(v: Record<string, unknown>): FigureChromeStyle {
  const d = DEFAULT_CHROME_STYLE;
  const numeric = (key: NumericChromeStyleKey): number => clampToControl(v[key], chromeRangeControl(key), d[key]);
  return {
    titleSizePx: numeric('titleSizePx'),
    badgeTextSizePx: numeric('badgeTextSizePx'),
    footerTextSizePx: numeric('footerTextSizePx'),
    footer: booleanOr(v['footer'], d.footer),
  };
}

/** A stored axis title size, or, for a style stored before it existed, the axis value size + 1. */
function axisTitleSize(v: Record<string, unknown>, axisTextSizePx: number, control: RangeControl<string>): number {
  return clampToControl(v['axisTitleSizePx'], control, clampToControl(axisTextSizePx + 1, control, axisTextSizePx));
}

function normalizeBar(value: unknown): BarFigureStyle {
  const d = DEFAULT_FIGURE_STYLE.bar;
  const v = isRecord(value) ? value : {};
  const numeric = (key: NumericBarStyleKey): number =>
    clampToControl(v[key], barRangeControl(key), d[key] ?? barRangeControl(key).min);
  const axisTextSizePx = numeric('axisTextSizePx');
  return {
    ...normalizeChrome(v),
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
    axisTextSizePx,
    axisTitleSizePx: axisTitleSize(v, axisTextSizePx, barRangeControl('axisTitleSizePx')),
    axisTitleBreak: oneOf(v['axisTitleBreak'], ['auto', 'always', 'never'] as const, d.axisTitleBreak),
    singleRunMarker: booleanOr(v['singleRunMarker'], d.singleRunMarker),
    thinkingLevelBreak: booleanOr(v['thinkingLevelBreak'], d.thinkingLevelBreak),
    gridlines: booleanOr(v['gridlines'], d.gridlines),
    axisTitleWeight: fontWeightOr(v['axisTitleWeight'], d.axisTitleWeight),
    plotFrame: booleanOr(v['plotFrame'], d.plotFrame),
    hiddenBadges: normalizeBadgeKinds(v['hiddenBadges']),
  };
}

function normalizeScatter(value: unknown): ScatterFigureStyle {
  const d = DEFAULT_FIGURE_STYLE.scatter;
  const v = isRecord(value) ? value : {};
  const numeric = (key: NumericScatterStyleKey): number =>
    clampToControl(v[key], scatterRangeControl(key), d[key]);
  const axisTextSizePx = numeric('axisTextSizePx');
  return {
    ...normalizeChrome(v),
    markRadiusPx: numeric('markRadiusPx'),
    intervals: booleanOr(v['intervals'], d.intervals),
    hiddenIntervalsNote: booleanOr(v['hiddenIntervalsNote'], d.hiddenIntervalsNote),
    frontierIntervalsNote: booleanOr(v['frontierIntervalsNote'], d.frontierIntervalsNote),
    dominatedShading: booleanOr(v['dominatedShading'], d.dominatedShading),
    frontierWidthPx: numeric('frontierWidthPx'),
    labelTextSizePx: numeric('labelTextSizePx'),
    axisTextSizePx,
    axisTitleSizePx: axisTitleSize(v, axisTextSizePx, scatterRangeControl('axisTitleSizePx')),
    legendPosition: oneOf(v['legendPosition'], ['bottom', 'right'] as const, d.legendPosition),
    thinkingLevelBreak: booleanOr(v['thinkingLevelBreak'], d.thinkingLevelBreak),
    gridlines: booleanOr(v['gridlines'], d.gridlines),
    axisTitleWeight: fontWeightOr(v['axisTitleWeight'], d.axisTitleWeight),
    plotFrame: booleanOr(v['plotFrame'], d.plotFrame),
    hiddenBadges: normalizeBadgeKinds(v['hiddenBadges']),
  };
}

export function normalizeAppearance(value: unknown): FigureAppearanceStyle {
  const d = DEFAULT_APPEARANCE_STYLE;
  const v = isRecord(value) ? value : {};
  const numeric = (key: NumericAppearanceStyleKey): number =>
    clampToControl(v[key], appearanceRangeControl(key), d[key]);
  return {
    theme: oneOf(v['theme'], FIGURE_THEME_NAMES, d.theme),
    background: oneOf(v['background'], FIGURE_BACKGROUND_MODES, d.background),
    backgroundColor: normalizeHexColor(v['backgroundColor'], d.backgroundColor),
    previewBackdrop: oneOf(v['previewBackdrop'], FIGURE_PREVIEW_BACKDROPS, d.previewBackdrop),
    previewBackdropColor: normalizeHexColor(v['previewBackdropColor'], d.previewBackdropColor),
    fontFamily: oneOf(v['fontFamily'], FIGURE_FONT_IDS, d.fontFamily),
    headingWeight: fontWeightOr(v['headingWeight'], d.headingWeight),
    labelWeight: fontWeightOr(v['labelWeight'], d.labelWeight),
    headingColor: nullableHexColor(v['headingColor'], d.headingColor),
    textColor: nullableHexColor(v['textColor'], d.textColor),
    border: booleanOr(v['border'], d.border),
    borderWidthPx: numeric('borderWidthPx'),
    borderRadiusPx: numeric('borderRadiusPx'),
    borderColor: nullableHexColor(v['borderColor'], d.borderColor),
  };
}

/** A stored `rowShading`, else the level for a stored `rowBands` checkbox (off: none, on: medium). */
function normalizeTableImageStyle(value: unknown): TableImageStyle {
  const d = DEFAULT_TABLE_IMAGE_STYLE;
  const v = isRecord(value) ? value : {};
  const legacyBands = v['rowBands'];
  const shadingFallback: TableRowShading =
    typeof legacyBands === 'boolean' ? (legacyBands ? 'medium' : 'none') : d.rowShading;
  return {
    rowShading: oneOf(v['rowShading'], TABLE_ROW_SHADINGS, shadingFallback),
    rowRules: booleanOr(v['rowRules'], d.rowRules),
  };
}

function normalizeProfile(value: unknown): ProfileFigureStyle {
  const v = isRecord(value) ? value : {};
  return { ...normalizeChrome(v), hiddenBadges: normalizeBadgeKinds(v['hiddenBadges']) };
}

/** Every known measure, each on its own: a missing or invalid one takes its default. */
function normalizeNumbers(value: unknown): NumberFormatStyle {
  const v = isRecord(value) ? value : {};
  const numbers = {} as Record<NumberMeasure, number>;
  for (const measure of NUMBER_MEASURES) {
    numbers[measure] = normalizeMeasureDecimals(v[measure], DEFAULT_MEASURE_DECIMALS[measure]);
  }
  return numbers;
}

/**
 * A usable style from anything, a parsed `localStorage` value included. Every valid field is kept,
 * numbers are rounded and clamped into their ranges, only real booleans are accepted for the
 * checkboxes, unknown keys are dropped and every other field falls back to its default. Never throws.
 */
export function normalizeFigureStyle(value: unknown): FigureStyle {
  const v = isRecord(value) ? value : {};
  return {
    bar: normalizeBar(v['bar']),
    scatter: normalizeScatter(v['scatter']),
    profile: normalizeProfile(v['profile']),
    numbers: normalizeNumbers(v['numbers']),
    appearance: normalizeAppearance(v['appearance']),
    table: normalizeTableImageStyle(v['table']),
  };
}
