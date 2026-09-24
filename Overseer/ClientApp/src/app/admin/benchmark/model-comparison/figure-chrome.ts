/**
 * The structured chrome every comparison figure carries besides its plot: title, badges, a detail
 * line, a key, a highlight and notes. The card template and the export composer both render it from
 * this one model, so the page and a downloaded image never say different things.
 *
 * Pure TypeScript with no Chart.js, Angular or DOM dependency.
 */

export type FigureBadgeTone = 'neutral' | 'pricing';

/** Which badge a figure's badge is, so the style can hide it by kind. */
export type FigureBadgeKind = 'direction' | 'models' | 'runs' | 'questions' | 'pricing';

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

/**
 * Where a figure is better, and the word the marker shows. Diagonal on a trade-off plot; one axis
 * on a bar chart. At least one of `x` and `y` is present.
 */
export interface FigureDirection {
  readonly x?: 'left' | 'right';
  readonly y?: 'top' | 'bottom';
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
   * The better direction, drawn as the Better badge at the end of the badge row; absent when hidden
   * or when the figure has none.
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

/** Clockwise rotation, in degrees, that turns the marker's up-right arrow toward the better side. */
export function figureDirectionRotation(direction: FigureDirection): number {
  const { x, y } = direction;
  if (y === 'top') {
    return x === 'right' ? 0 : x === 'left' ? 270 : 315;
  }
  if (y === 'bottom') {
    return x === 'right' ? 90 : x === 'left' ? 180 : 135;
  }
  return x === 'left' ? 225 : 45;
}

/** The marker spelled out, e.g. `Better toward the top left` or `Better toward the top`. */
export function figureDirectionText(direction: FigureDirection): string {
  const parts = [direction.y, direction.x].filter((part) => part !== undefined);
  return `${direction.label} toward the ${parts.join(' ')}`;
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

/** Scored questions per plotted entry against the questions the runs were asked: `18 questions`, `16 of 18 questions`, `15–16 of 18 questions`. */
export function questionsBadge(min: number, max: number, asked: number): FigureBadge {
  const scored = min === max ? `${min}` : `${min}–${max}`;
  if (asked <= 0) {
    return { text: `${scored} ${min === max && min === 1 ? 'question' : 'questions'}`, tone: 'neutral', kind: 'questions' };
  }
  if (min === asked && max === asked) {
    return { text: `${asked} ${asked === 1 ? 'question' : 'questions'}`, tone: 'neutral', kind: 'questions' };
  }
  const noun = asked === 1 ? 'question' : 'questions';
  return {
    text: `${scored} of ${asked} ${noun}`,
    tone: 'neutral',
    ariaLabel: `${scored} of ${asked} asked ${noun} scored`,
    kind: 'questions',
  };
}
