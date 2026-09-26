/**
 * Resolves the admin's appearance choices into the colours, fonts and border every chart, the
 * composed chrome and the table image draw with.
 *
 * Resolved once and threaded through the chart builders, the figure composer and the table
 * composer, so the page, the previews and every export agree. The dark theme is the literal
 * palette the figures were always drawn in. Pure TypeScript with no Chart.js, Angular or DOM
 * dependency.
 */

import type { FigureBadgeTone, FigureNoteTone } from './figure-chrome';
import { figureFont } from './figure-fonts';
import type { FigureAppearanceStyle, FigureFontWeight, FigureThemeName, TableRowShading } from './figure-style';
import { DEFAULT_APPEARANCE_STYLE } from './figure-style';

export interface ResolvedChromeColors {
  readonly title: string;
  readonly body: string;
  readonly muted: string;
  readonly rule: string;
  readonly keyInk: string;
  readonly dominatedKeyFill: string;
  /** The Better badge. */
  readonly direction: { readonly border: string; readonly fill: string; readonly ink: string };
  readonly badge: Record<FigureBadgeTone, { readonly border: string; readonly fill: string; readonly text: string }>;
  readonly note: Record<FigureNoteTone, { readonly rule: string; readonly text: string }>;
}

export interface ResolvedChartColors {
  readonly surface: string;
  readonly inkPrimary: string;
  readonly inkSecondary: string;
  readonly inkMuted: string;
  readonly gridline: string;
  readonly baseline: string;
  readonly accent: string;
  readonly deEmphasisFill: string;
  readonly deEmphasisStroke: string;
  readonly dominatedRegionFill: string;
  readonly categorical: readonly [string, string, string];
}

export interface ResolvedFigureFonts {
  /** The composed chrome and the table image. */
  readonly chromeStack: string;
  /** Null keeps each chart site's own family (Lato ticks, the Chart.js default elsewhere). */
  readonly chartStack: string | null;
  readonly headingWeight: FigureFontWeight;
  readonly labelWeight: FigureFontWeight;
}

export interface ResolvedFigureBorder {
  readonly widthPx: number;
  readonly radiusPx: number;
  readonly color: string;
}

export interface ResolvedFigureTheme {
  readonly name: FigureThemeName;
  /** Null paints nothing, so the written image carries alpha. */
  readonly background: string | null;
  /** The ground text is measured against: the background, or the theme's base while transparent. */
  readonly surface: string;
  readonly chrome: ResolvedChromeColors;
  readonly chart: ResolvedChartColors;
  readonly fonts: ResolvedFigureFonts;
  /** Null draws no border. */
  readonly border: ResolvedFigureBorder | null;
  /** The per-family plot frame's stroke: the theme's axis baseline. */
  readonly frameColor: string;
}

interface ThemeBase {
  readonly background: string;
  readonly chrome: ResolvedChromeColors;
  readonly chart: ResolvedChartColors;
}

const DARK_TITLE = '#e0ba6d';
const DARK_MUTED = '#a1a1aa';

/** The palette the figures were always drawn in: `figure-export.ts`, the chart builders and the table composer. */
const DARK_THEME: ThemeBase = {
  background: '#181818',
  chrome: {
    title: DARK_TITLE,
    body: '#d4d4d8',
    muted: DARK_MUTED,
    rule: '#2a2a2a',
    keyInk: '#c3c2b7',
    dominatedKeyFill: 'rgba(255, 255, 255, 0.12)',
    direction: { border: 'rgba(224, 186, 109, 0.55)', fill: 'rgba(224, 186, 109, 0.1)', ink: DARK_TITLE },
    badge: {
      neutral: { border: 'rgba(255, 255, 255, 0.25)', fill: 'rgba(255, 255, 255, 0.04)', text: DARK_TITLE },
      pricing: { border: 'rgba(16, 185, 129, 0.3)', fill: 'rgba(16, 185, 129, 0.1)', text: '#6ee7b7' },
    },
    note: {
      warning: { rule: '#e0ba6d', text: '#e0ba6d' },
      info: { rule: '#6b6b66', text: DARK_MUTED },
    },
  },
  chart: {
    surface: '#0b0b0b',
    inkPrimary: '#ffffff',
    inkSecondary: '#c3c2b7',
    inkMuted: '#898781',
    gridline: '#2c2c2a',
    baseline: '#383835',
    accent: '#e0ba6d',
    deEmphasisFill: 'rgba(137, 135, 129, 0.45)',
    deEmphasisStroke: '#898781',
    dominatedRegionFill: 'rgba(255, 255, 255, 0.04)',
    categorical: ['#3987e5', '#d95926', '#199e70'],
  },
};

const LIGHT_TITLE = '#0b0b0b';
const LIGHT_MUTED = '#6b6a66';

/** Measured on white, PowerPoint's default slide: every text role passes WCAG AA there. */
const LIGHT_THEME: ThemeBase = {
  background: '#ffffff',
  chrome: {
    title: LIGHT_TITLE,
    body: '#52514e',
    muted: LIGHT_MUTED,
    rule: '#e1e0d9',
    keyInk: '#52514e',
    dominatedKeyFill: 'rgba(11, 11, 11, 0.10)',
    direction: { border: 'rgba(154, 107, 18, 0.55)', fill: 'rgba(154, 107, 18, 0.08)', ink: LIGHT_TITLE },
    badge: {
      neutral: { border: 'rgba(11, 11, 11, 0.25)', fill: 'rgba(11, 11, 11, 0.03)', text: LIGHT_TITLE },
      pricing: { border: 'rgba(4, 120, 87, 0.35)', fill: 'rgba(4, 120, 87, 0.08)', text: '#047857' },
    },
    note: {
      warning: { rule: '#8a5a00', text: '#8a5a00' },
      info: { rule: '#c3c2b7', text: LIGHT_MUTED },
    },
  },
  chart: {
    surface: '#ffffff',
    inkPrimary: LIGHT_TITLE,
    inkSecondary: '#52514e',
    inkMuted: LIGHT_MUTED,
    gridline: '#e1e0d9',
    baseline: '#c3c2b7',
    accent: '#9a6b12',
    deEmphasisFill: 'rgba(107, 106, 102, 0.35)',
    deEmphasisStroke: '#898781',
    dominatedRegionFill: 'rgba(11, 11, 11, 0.04)',
    categorical: ['#2a78d6', '#eb6834', '#18a070'],
  },
};

function themeBase(name: FigureThemeName): ThemeBase {
  return name === 'light' ? LIGHT_THEME : DARK_THEME;
}

/** The theme's own ground, which a transparent image is judged against when no backdrop colour is set. */
export function themeBackground(name: FigureThemeName): string {
  return themeBase(name).background;
}

/**
 * Colours, fonts and border for one appearance. Warning notes, pricing badges, the accent and the
 * series hues stay theme-owned; the heading and text colours override only the roles they name.
 */
export function resolveFigureTheme(appearance: FigureAppearanceStyle = DEFAULT_APPEARANCE_STYLE): ResolvedFigureTheme {
  const base = themeBase(appearance.theme);
  const background = appearance.background === 'transparent'
    ? null
    : appearance.background === 'custom' ? appearance.backgroundColor : base.background;
  const surface = background ?? base.background;
  const chartSurface = appearance.background === 'custom' ? appearance.backgroundColor : base.chart.surface;

  const title = appearance.headingColor ?? base.chrome.title;
  const text = appearance.textColor;
  const muted = text === null ? base.chrome.muted : mixHex(text, surface, 0.7);
  const chartMuted = text === null ? base.chart.inkMuted : muted;

  const chrome: ResolvedChromeColors = {
    ...base.chrome,
    title,
    body: text ?? base.chrome.body,
    muted,
    keyInk: text ?? base.chrome.keyInk,
    direction: { ...base.chrome.direction, ink: title },
    badge: { ...base.chrome.badge, neutral: { ...base.chrome.badge.neutral, text: title } },
    note: { ...base.chrome.note, info: { ...base.chrome.note.info, text: muted } },
  };
  const chart: ResolvedChartColors = {
    ...base.chart,
    surface: chartSurface,
    inkPrimary: text ?? base.chart.inkPrimary,
    inkSecondary: text ?? base.chart.inkSecondary,
    inkMuted: chartMuted,
  };
  const font = figureFont(appearance.fontFamily);
  return {
    name: appearance.theme,
    background,
    surface,
    chrome,
    chart,
    fonts: {
      chromeStack: font.stack,
      chartStack: font.family === null ? null : font.stack,
      headingWeight: appearance.headingWeight,
      labelWeight: appearance.labelWeight,
    },
    border: appearance.border
      ? {
        widthPx: appearance.borderWidthPx,
        radiusPx: appearance.borderRadiusPx,
        color: appearance.borderColor ?? base.chart.baseline,
      }
      : null,
    frameColor: base.chart.baseline,
  };
}

/** Opacity of the text color laid over the ground as the alternate-row band, per level. */
export const TABLE_SHADING_ALPHA: Readonly<Record<Exclude<TableRowShading, 'none'>, number>> = {
  light: 0.05,
  medium: 0.1,
  strong: 0.16,
};

/**
 * The alternate-row band: the table's text color at the level's opacity, so it follows the theme,
 * a custom text color and a custom or transparent background alike. Null draws no band.
 */
export function tableBandColor(theme: ResolvedFigureTheme, shading: TableRowShading): string | null {
  if (shading === 'none') {
    return null;
  }
  const fallback: [number, number, number] = theme.name === 'light' ? [11, 11, 11] : [255, 255, 255];
  const [r, g, b] = parseHex(theme.chrome.body) ?? fallback;
  return `rgba(${r}, ${g}, ${b}, ${TABLE_SHADING_ALPHA[shading]})`;
}

/** `#rrggbb` → `[r, g, b]`, or null for anything else. */
function parseHex(hex: string): [number, number, number] | null {
  const match = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex);
  return match ? [parseInt(match[1], 16), parseInt(match[2], 16), parseInt(match[3], 16)] : null;
}

function toHex(channels: readonly number[]): string {
  return `#${channels.map((c) => Math.round(Math.min(255, Math.max(0, c))).toString(16).padStart(2, '0')).join('')}`;
}

/** `weight` of `a` and the rest of `b`, channel by channel in sRGB. `a` when either is not `#rrggbb`. */
export function mixHex(a: string, b: string, weight: number): string {
  const ca = parseHex(a);
  const cb = parseHex(b);
  if (!ca || !cb) {
    return a;
  }
  return toHex(ca.map((channel, i) => channel * weight + cb[i] * (1 - weight)));
}

function relativeLuminance(hex: string): number {
  const channels = parseHex(hex) ?? [0, 0, 0];
  const [r, g, b] = channels.map((c) => {
    const s = c / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/** The WCAG 2 contrast ratio of two `#rrggbb` colours, from 1 to 21. */
export function contrastRatio(a: string, b: string): number {
  const la = relativeLuminance(a);
  const lb = relativeLuminance(b);
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

function ratioText(ratio: number): string {
  return `${ratio.toFixed(1)}:1`;
}

/**
 * Plain-text warnings about colours too faint to read: a heading below 3:1, text below 4.5:1, and a
 * series hue below 3:1 where the ground is not the one the theme's palette was validated on. A
 * transparent image is judged against the preview backdrop colour, or the theme's own ground while
 * the backdrop is the checkerboard.
 */
export function appearanceWarnings(appearance: FigureAppearanceStyle): string[] {
  const theme = resolveFigureTheme(appearance);
  const base = themeBase(appearance.theme);
  let ground: string;
  let where: string;
  let ownGround = false;
  if (appearance.background === 'custom') {
    ground = appearance.backgroundColor;
    where = `the background ${ground}`;
  } else if (appearance.background === 'transparent' && appearance.previewBackdrop === 'color') {
    ground = appearance.previewBackdropColor;
    where = `the preview backdrop color ${ground}, which stands in for the slide behind a transparent image`;
  } else if (appearance.background === 'transparent') {
    ground = base.background;
    ownGround = true;
    where = `the ${appearance.theme} theme's own background ${ground}, which a transparent image is judged ` +
      'against while the preview backdrop is the checkerboard';
  } else {
    ground = base.background;
    ownGround = true;
    where = `the ${appearance.theme} theme's background ${ground}`;
  }

  const warnings: string[] = [];
  const heading = contrastRatio(theme.chrome.title, ground);
  if (heading < 3) {
    warnings.push(`Headings in ${theme.chrome.title} have ${ratioText(heading)} contrast on ${where}; ` +
      'headings need at least 3:1.');
  }
  const body = contrastRatio(theme.chrome.body, ground);
  if (body < 4.5) {
    warnings.push(`Text in ${theme.chrome.body} has ${ratioText(body)} contrast on ${where}; ` +
      'text needs at least 4.5:1.');
  }
  if (!ownGround) {
    const faint = theme.chart.categorical
      .map((hue) => ({ hue, ratio: contrastRatio(hue, ground) }))
      .filter((series) => series.ratio < 3);
    for (const series of faint) {
      warnings.push(`The series color ${series.hue} has ${ratioText(series.ratio)} contrast on ${where}; ` +
        'marks need at least 3:1.');
    }
  }
  return warnings;
}
