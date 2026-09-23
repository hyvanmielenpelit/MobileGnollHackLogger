/**
 * The structured chrome every comparison figure carries besides its plot: title, badges, a detail
 * line, a key, a highlight and notes. The card template and the export composer both render it from
 * this one model, so the page and a downloaded image never say different things.
 *
 * Pure TypeScript with no Chart.js, Angular or DOM dependency.
 */

export type FigureBadgeTone = 'neutral' | 'pricing';

/** Which badge a figure's badge is, so the style can hide it by kind. */
export type FigureBadgeKind = 'models' | 'runs' | 'questions' | 'pricing';

export interface FigureBadge {
  readonly text: string;
  readonly tone: FigureBadgeTone;
  readonly ariaLabel?: string;
  /** Absent on a badge no style control can hide. */
  readonly kind?: FigureBadgeKind;
}

/** The badges whose kind is not hidden, in their original order. */
export function visibleBadges(badges: readonly FigureBadge[], hidden: readonly FigureBadgeKind[]): FigureBadge[] {
  return badges.filter((badge) => badge.kind === undefined || !hidden.includes(badge.kind));
}

/** Which corner of a trade-off plot is better, and the word the marker shows. */
export interface FigureDirection {
  readonly x: 'left' | 'right';
  readonly y: 'top' | 'bottom';
  readonly label: string;
}

export type FigureNoteTone = 'info' | 'warning';
export interface FigureNote { readonly text: string; readonly tone: FigureNoteTone; }

export type FigureKeyGlyph = 'hollow' | 'solid' | 'frontier' | 'dominated' | 'interval';
export interface FigureKeyItem { readonly glyph: FigureKeyGlyph; readonly text: string; }

/** Everything a figure shows besides its plot, identical on screen and in an export. */
export interface FigureChrome {
  readonly title: string;
  readonly badges: readonly FigureBadge[];
  /**
   * The better corner of a two-axis plot. The plot draws the marker itself; the chrome keeps it for
   * the text summary.
   */
  readonly direction?: FigureDirection;
  /** One short sentence under the badges, or ''. Used only for the pricing basis on cost figures. */
  readonly detail: string;
  readonly key: readonly FigureKeyItem[];
  /** e.g. 'Best trade-offs: GPT-5.6 Luna', or ''. */
  readonly highlight: string;
  readonly notes: readonly FigureNote[];
}

/** The export's last line: suite on the left, computation time on the right. */
export interface FigureFooter { readonly suite: string; readonly computedAt: string; }

/** Clockwise rotation, in degrees, that turns the marker's up-right arrow toward the better corner. */
export function figureDirectionRotation(direction: FigureDirection): number {
  if (direction.y === 'top') {
    return direction.x === 'right' ? 0 : 270;
  }
  return direction.x === 'right' ? 90 : 180;
}

/** The marker spelled out, e.g. `Better toward the top left`. */
export function figureDirectionText(direction: FigureDirection): string {
  return `${direction.label} toward the ${direction.y} ${direction.x}`;
}

/**
 * The badges' texts and the direction joined with `, `, plus the detail: the figure's one-line
 * summary for assistive technology.
 */
export function figureSummary(chrome: FigureChrome): string {
  const badges = [
    ...chrome.badges.map((badge) => badge.ariaLabel ?? badge.text),
    ...(chrome.direction ? [figureDirectionText(chrome.direction)] : []),
  ].join(', ');
  if (chrome.detail === '') {
    return badges;
  }
  return badges === '' ? chrome.detail : `${badges}. ${chrome.detail}`;
}

function parseDate(iso: string): Date | null {
  if (!iso) {
    return null;
  }
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? null : date;
}

/** A computation timestamp in the reader's locale, e.g. `23 Sep 2026, 11:06`. */
export function formatComputedAt(iso: string, locale?: string): string {
  const date = parseDate(iso);
  if (date === null) {
    return 'unknown time';
  }
  return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}

/**
 * The pricing-basis badge for a figure with a cost axis. The catalog date is formatted in UTC because
 * the server takes "today" as the UTC date (`BenchmarkModelComparisonService`).
 */
export function pricingBadge(basis: string, pricedOn: string, locale?: string): FigureBadge {
  if (basis === 'Current') {
    const date = parseDate(pricedOn);
    const text = date === null
      ? 'Catalog prices'
      : `Catalog prices · ${new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeZone: 'UTC' }).format(date)}`;
    return { text, tone: 'pricing', kind: 'pricing' };
  }
  if (basis === 'AsRun') {
    return { text: 'Prices as run', tone: 'pricing', kind: 'pricing' };
  }
  return { text: 'Pricing basis unknown', tone: 'pricing', kind: 'pricing' };
}

/** One sentence saying what the pricing basis means for the cost values, or '' for an unknown basis. */
export function pricingNote(basis: string): string {
  if (basis === 'Current') {
    return "Today's catalog prices applied to every run: comparable across dates, not the amount actually spent.";
  }
  if (basis === 'AsRun') {
    return "Each run's own stored prices: the amount actually spent, not comparable across price-change dates.";
  }
  return '';
}

/** How many runs stand behind each plotted entry: `1 run each`, `3 runs each`, or `1–3 runs each`. */
export function runsBadge(plotted: readonly { readonly runCount: number }[]): FigureBadge {
  if (plotted.length === 0) {
    return { text: 'No runs', tone: 'neutral', kind: 'runs' };
  }
  const counts = plotted.map((entry) => entry.runCount);
  const min = Math.min(...counts);
  const max = Math.max(...counts);
  const text = min === max
    ? `${min} ${min === 1 ? 'run' : 'runs'} each`
    : `${min}–${max} runs each`;
  return { text, tone: 'neutral', kind: 'runs' };
}
