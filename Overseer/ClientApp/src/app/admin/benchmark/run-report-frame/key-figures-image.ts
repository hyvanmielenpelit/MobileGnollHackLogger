/**
 * The run report's key figures as a PNG image: the whole strip, or one card.
 *
 * No Angular. The cells are read from the rendered `.score-card`s, so the image says what the dialog
 * says, and composed on a canvas in the dialog's dark theme. The run's settings above the figures are
 * the header's run facts (`run-facts.ts`), converted by {@link toImageFactRows}. The layout decisions
 * are pure functions over a {@link TextWrapper} and a {@link TextMeasurer}, so they unit-test without
 * a canvas.
 *
 * Both compositions are square first: the strip picks the column count and card width whose image is
 * closest to square without being taller than wide, and pads it to an exact square when it is within
 * {@link STRIP_SQUARE_TOLERANCE}; the card image is 640 × 640 unless its notes need more width.
 */

import {
  FIGURE_BACKGROUND,
  FIGURE_FONT_STACK,
  FIGURE_MUTED_COLOR,
  FIGURE_RULE_COLOR,
  FIGURE_TITLE_COLOR,
  copyImageToClipboard,
  encodeFigureImage,
  exportTimestamp,
  saveFigureBlob,
  wrapText
} from '../model-comparison/figure-export';
import type { ClipboardImageOutcome } from '../model-comparison/figure-export';
import { safeFileName } from '../../../utils/download.util';
import { RunFactRow, runFactBadges } from './run-facts';

/** The `badge-score-*` class on a card's value, or `na` for a muted value with none. */
export type KeyFigureTone = 'high' | 'mid' | 'low' | 'na';

/**
 * Every card's stable key, in display order, from its `data-figure` attribute. A key never changes
 * with a label variant (Speed Index / Median Model Time; Assessor / Reference Reader Agreement).
 */
export const KEY_FIGURE_KEYS = [
  'intelligence', 'raw-quality', 'unweighted-mean', 'speed', 'mean-time', 'panel', 'agreement',
  'holistic', 'answer-duration', 'wall-time', 'model-cost', 'estimated-cost'
] as const;

export type KeyFigureKey = typeof KEY_FIGURE_KEYS[number];

/** Whether a card, by its key, goes into the whole-strip image. */
export type KeyFigureFilter = (key: string) => boolean;

/** One key-figure card, as the dialog renders it. */
export interface KeyFigureCell {
  /** The card's `data-figure`, or its label's slug where it has none. */
  readonly key: string;
  readonly label: string;
  /** The value text, whitespace collapsed, with one `*` per advisory marker. */
  readonly value: string;
  readonly notes: readonly string[];
  /** The Intelligence Index card. */
  readonly main: boolean;
  /** The value is drawn at the headline size: a `.score-value`, or a `.score-subvalue.score-headline`. */
  readonly headline: boolean;
  readonly tone: KeyFigureTone | null;
  /** `* {title}` for each distinct advisory marker title. */
  readonly footnotes: readonly string[];
}

/** A badge's colors, by what it marks. */
export type ImageBadgeTone = 'thinking' | 'reasoning' | 'openai' | 'anthropic' | 'google' | 'provider'
  | 'config' | 'role' | 'success' | 'warning' | 'danger' | 'info' | 'running';

/** One piece of a fact row's value: text, or an atomic badge. */
export type ImageFactRun =
  | {
    readonly kind: 'text';
    readonly text: string;
    readonly strong?: boolean;
    readonly warning?: boolean;
    readonly muted?: boolean;
    /** Starts a new line. */
    readonly lineBreak?: boolean;
    /** The gap before this run on its line, instead of the default. */
    readonly gapBefore?: number;
  }
  | {
    readonly kind: 'badge';
    readonly text: string;
    readonly tone: ImageBadgeTone;
    /** Starts a new line. */
    readonly lineBreak?: boolean;
    readonly gapBefore?: number;
  };

/** One run setting above the figures: `MODEL  GPT-6.1 Sol [HIGH] [OpenAI]`. */
export interface ImageFactRow {
  readonly label: string;
  readonly runs: readonly ImageFactRun[];
  /** Drawn in the card image too. */
  readonly primary: boolean;
}

/** What the image says about the run besides its figures. */
export interface ImageContext {
  /** `Run #72 · Snapshot: Tommi2 2026-09-17`. */
  readonly title: string;
  /** The chosen run settings under the title; the card image shows the primary ones. */
  readonly facts: readonly ImageFactRow[];
  readonly runId: number;
  /** The footer's Overseer build; null reads as `unknown`. */
  readonly overseerVersion: string | null;
  /** For the file name. */
  readonly suiteName: string;
  /** For the file name. */
  readonly modelName: string;
}

/** A decoded logo and its natural size. */
export interface KeyFigureLogo {
  readonly image: CanvasImageSource;
  readonly width: number;
  readonly height: number;
}

/** The wide wordmark heads the strip image, the square emblem the card image. Either may be missing. */
export interface KeyFigureLogos {
  readonly wide: KeyFigureLogo | null;
  readonly emblem: KeyFigureLogo | null;
}

/** Greedy word wrap of `text` into lines at most `maxWidth` wide, in the figure font. */
export type TextWrapper = (text: string, maxWidth: number, sizePx: number, weight: string) => string[];

/** The width of `text` in the figure font. */
export type TextMeasurer = (text: string, sizePx: number, weight: string) => number;

/** A measurer for when there is no canvas: every character a little over half the font size wide. */
export const estimateTextWidth: TextMeasurer = (text, sizePx) => text.length * sizePx * 0.55;

export type KeyFiguresAction = 'copy' | 'download';

/** A clipboard outcome, a completed download, a strip with no figure selected, or a composition that failed. */
export type KeyFiguresOutcome = ClipboardImageOutcome | 'downloaded' | 'empty' | 'failed';

/** One encoded image and the name it is saved under. */
export interface KeyFiguresImage {
  readonly blob: Blob;
  readonly fileName: string;
}

export type KeyFiguresLoadedImage = HTMLImageElement | HTMLCanvasElement | ImageBitmap;

export const GNOLLBENCH_WIDE_LOGO_URL = '/img/gnollbench/gnollbench-wide-v3-h256.webp';
export const GNOLLBENCH_EMBLEM_URL = '/img/gnollbench/gnollbench-logo-v3-256.webp';

/** The IO the export performs, behind a holder a spec replaces. */
export const keyFiguresImageIo = {
  loadImage: (url: string): Promise<KeyFiguresLoadedImage> => loadImageElement(url),
  copy: (blob: Blob): Promise<ClipboardImageOutcome> => copyImageToClipboard(blob),
  save: (blob: Blob, fileName: string): void => saveFigureBlob(blob, fileName),
  now: (): Date => new Date()
};

/** Device pixels per logical pixel in both images. */
export const KEY_FIGURES_IMAGE_SCALE = 2;

/** Colors of the dialog's dark theme. */
const CARD_FILL = '#161616';
const CARD_BORDER = '#333';
const LABEL_COLOR = '#888';
const NOTE_COLOR = '#888';
const VALUE_COLOR = '#eee';
const TONE_COLORS: Record<KeyFigureTone, string> = {
  high: '#81c784',
  mid: '#ffb74d',
  low: '#e57373',
  na: '#888'
};
const WARNING_COLOR = '#fcd34d';

/** `color-mix(in srgb, a t, b)` for two `#rrggbb` colors. */
export function mixHex(a: string, b: string, t: number): string {
  const channel = (hex: string, index: number): number => Number.parseInt(hex.slice(1 + index * 2, 3 + index * 2), 16);
  return '#' + [0, 1, 2]
    .map(index => Math.round(channel(a, index) * t + channel(b, index) * (1 - t)).toString(16).padStart(2, '0'))
    .join('');
}

function providerTone(base: string, ink: string): { ink: string; fill: string; border: string } {
  return { ink, fill: mixHex(base, '#1b1b1b', 0.22), border: mixHex(base, '#383838', 0.6) };
}

/**
 * The badge colors of `styles.scss` (`.thinking-badge`, `.reasoning-badge`, `.provider-badge` and its
 * `--provider-*` tokens, `.config-badge`, `.model-option-tag`) and of the run status badges in
 * `benchmark.component.scss`, drawn over {@link FIGURE_BACKGROUND}; change both together.
 */
export const BADGE_PALETTE: Record<ImageBadgeTone, { readonly ink: string; readonly fill: string; readonly border: string }> = {
  thinking: { ink: '#7dd3fc', fill: 'rgba(125, 211, 252, 0.08)', border: 'rgba(125, 211, 252, 0.4)' },
  reasoning: { ink: '#c084fc', fill: 'rgba(192, 132, 252, 0.08)', border: 'rgba(192, 132, 252, 0.4)' },
  openai: providerTone('#10a37f', '#7fe8d2'),
  anthropic: providerTone('#d97757', '#f7b39b'),
  google: providerTone('#4285f4', '#a8c7fa'),
  provider: { ink: '#c8c8c8', fill: '#1b1b1b', border: '#4a4a4a' },
  config: { ink: '#aaa', fill: '#252525', border: '#383838' },
  role: { ink: '#e0ba6d', fill: 'transparent', border: 'rgba(212, 160, 23, 0.25)' },
  success: { ink: '#81c784', fill: 'rgba(76, 175, 80, 0.2)', border: '#4caf50' },
  warning: { ink: '#ffb74d', fill: 'rgba(255, 152, 0, 0.2)', border: '#ff9800' },
  danger: { ink: '#e57373', fill: 'rgba(244, 67, 54, 0.2)', border: '#f44336' },
  info: { ink: '#64b5f6', fill: 'rgba(33, 150, 243, 0.2)', border: '#2196f3' },
  running: { ink: '#e0ba6d', fill: 'rgba(212, 160, 23, 0.2)', border: '#e0ba6d' }
};

/** Tones whose text is drawn upper-case, as the CSS transforms it. */
const UPPER_CASE_TONES: ReadonlySet<ImageBadgeTone> = new Set<ImageBadgeTone>(
  ['thinking', 'reasoning', 'config', 'success', 'warning', 'danger', 'info', 'running']);

// -----------------------------------------------------------------------------------------------
// Reading the rendered cards
// -----------------------------------------------------------------------------------------------

const CARD_ACTIONS_SELECTOR = 'app-key-figure-card-actions';
const TONES: readonly KeyFigureTone[] = ['high', 'mid', 'low', 'na'];

export function collapseWhitespace(text: string | null | undefined): string {
  return (text ?? '').replace(/\s+/g, ' ').trim();
}

/** One card's figures, read from a copy with its card actions removed. */
export function readKeyFigureCell(card: HTMLElement): KeyFigureCell {
  const copy = card.cloneNode(true) as HTMLElement;
  copy.querySelectorAll(CARD_ACTIONS_SELECTOR).forEach(element => element.remove());

  const label = collapseWhitespace(copy.querySelector('.score-label')?.textContent);
  const valueElement = copy.querySelector<HTMLElement>('.score-value, .score-subvalue');

  let value = '';
  let tone: KeyFigureTone | null = null;
  const headline = !!valueElement
    && (valueElement.classList.contains('score-value') || valueElement.classList.contains('score-headline'));
  if (valueElement) {
    const markers = Array.from(valueElement.querySelectorAll<HTMLElement>('.degraded-tag'))
      .map(tag => collapseWhitespace(tag.textContent) || '*');
    const bare = valueElement.cloneNode(true) as HTMLElement;
    bare.querySelectorAll('.degraded-tag').forEach(tag => tag.remove());
    value = collapseWhitespace(bare.textContent) + markers.join('');
    tone = TONES.find(candidate => valueElement.classList.contains(`badge-score-${candidate}`))
      ?? (valueElement.classList.contains('text-muted') ? 'na' : null);
  }

  const notes = Array.from(copy.querySelectorAll('.score-note'))
    .map(note => collapseWhitespace(note.textContent))
    .filter(note => note !== '');

  const footnotes: string[] = [];
  for (const tag of Array.from(copy.querySelectorAll<HTMLElement>('.degraded-tag'))) {
    const title = collapseWhitespace(tag.getAttribute('title'));
    const footnote = title === '' ? '' : `* ${title}`;
    if (footnote !== '' && !footnotes.includes(footnote)) {
      footnotes.push(footnote);
    }
  }

  const key = collapseWhitespace(card.getAttribute('data-figure')) || keyFigureSlug(label);
  return { key, label, value, notes, main: card.classList.contains('main-score'), headline, tone, footnotes };
}

/** One cell per `.score-card` under `root`, in document order. */
export function readKeyFigureCells(root: ParentNode): KeyFigureCell[] {
  return Array.from(root.querySelectorAll<HTMLElement>('.score-card')).map(readKeyFigureCell);
}

/** The cells `include` accepts, in order; every cell without a filter. */
export function filterKeyFigureCells(cells: readonly KeyFigureCell[], include?: KeyFigureFilter): KeyFigureCell[] {
  return include ? cells.filter(cell => include(cell.key)) : [...cells];
}

// -----------------------------------------------------------------------------------------------
// The remembered selection
// -----------------------------------------------------------------------------------------------

export const KEY_FIGURES_STORAGE_KEY = 'overseer.benchmark.runReport.keyFigures';
const KEY_FIGURES_STORAGE_VERSION = 1;

/**
 * The keys left out of the Summary panel and the whole-strip image, from
 * `{ version: 1, excluded: [...] }` in localStorage; none when absent or unreadable. Storing the exclusions keeps a card added later in.
 */
export function readStoredKeyFigureExclusions(): string[] {
  try {
    const raw = localStorage.getItem(KEY_FIGURES_STORAGE_KEY);
    if (!raw) {
      return [];
    }
    const parsed = JSON.parse(raw) as { version?: unknown; excluded?: unknown } | null;
    if (!parsed || parsed.version !== KEY_FIGURES_STORAGE_VERSION || !Array.isArray(parsed.excluded)) {
      return [];
    }
    return normalizeKeyFigureExclusions(parsed.excluded.filter((key): key is string => typeof key === 'string'));
  } catch {
    return [];
  }
}

export function storeKeyFigureExclusions(excluded: readonly string[]): void {
  try {
    localStorage.setItem(KEY_FIGURES_STORAGE_KEY, JSON.stringify({
      version: KEY_FIGURES_STORAGE_VERSION,
      excluded: normalizeKeyFigureExclusions(excluded)
    }));
  } catch {
    // Storage unavailable: the selection is simply not remembered.
  }
}

/** Trimmed, non-empty and distinct, in their first order. */
function normalizeKeyFigureExclusions(keys: readonly string[]): string[] {
  const result: string[] = [];
  for (const key of keys.map(entry => entry.trim())) {
    if (key !== '' && !result.includes(key)) {
      result.push(key);
    }
  }
  return result;
}

export const IMAGE_DETAILS_STORAGE_KEY = 'overseer.benchmark.runReport.imageDetails';
const IMAGE_DETAILS_STORAGE_VERSION = 1;

/** The run-fact rows the images leave out while nothing is stored: the board, which is grading diagnostics. */
export const DEFAULT_IMAGE_DETAIL_EXCLUSIONS: readonly string[] = ['board'];

/**
 * The run-fact keys the images leave out, from `{ version: 1, excluded: [...] }` in localStorage; an
 * empty list is a choice of every row. {@link DEFAULT_IMAGE_DETAIL_EXCLUSIONS} when absent or unreadable.
 */
export function readStoredImageDetailExclusions(): string[] {
  try {
    const raw = localStorage.getItem(IMAGE_DETAILS_STORAGE_KEY);
    if (!raw) {
      return [...DEFAULT_IMAGE_DETAIL_EXCLUSIONS];
    }
    const parsed = JSON.parse(raw) as { version?: unknown; excluded?: unknown } | null;
    if (!parsed || parsed.version !== IMAGE_DETAILS_STORAGE_VERSION || !Array.isArray(parsed.excluded)) {
      return [...DEFAULT_IMAGE_DETAIL_EXCLUSIONS];
    }
    return normalizeKeyFigureExclusions(parsed.excluded.filter((key): key is string => typeof key === 'string'));
  } catch {
    return [...DEFAULT_IMAGE_DETAIL_EXCLUSIONS];
  }
}

export function storeImageDetailExclusions(excluded: readonly string[]): void {
  try {
    localStorage.setItem(IMAGE_DETAILS_STORAGE_KEY, JSON.stringify({
      version: IMAGE_DETAILS_STORAGE_VERSION,
      excluded: normalizeKeyFigureExclusions(excluded)
    }));
  } catch {
    // Storage unavailable: the selection is simply not remembered.
  }
}

// -----------------------------------------------------------------------------------------------
// Run facts
// -----------------------------------------------------------------------------------------------

/** The image tone of a run status badge, from its `badge-status-*` class or bare status key. */
export function statusImageTone(statusBadgeClass: string): ImageBadgeTone {
  switch (statusBadgeClass.toLowerCase().replace(/^badge-status-/, '')) {
    case 'completed':
    case 'scored':
      return 'success';
    case 'completedwitherrors':
      return 'warning';
    case 'failed':
      return 'danger';
    case 'completedwithlimits':
      return 'info';
    case 'running':
      return 'running';
    default:
      return 'config';
  }
}

function providerImageTone(provider: string | undefined): ImageBadgeTone {
  const key = (provider ?? '').trim().toLowerCase();
  return key === 'openai' || key === 'anthropic' || key === 'google' ? key : 'provider';
}

/**
 * The header's run facts as the images draw them. Every row given is drawn; the caller passes the
 * chosen ones. The run status follows the start time as a badge; Model and Assessor(s) are primary;
 * each model after the first starts a new line.
 */
export function toImageFactRows(rows: readonly RunFactRow[], status: { text: string; tone: ImageBadgeTone }): ImageFactRow[] {
  return rows.map(row => {
    const item = row.item;
    const runs: ImageFactRun[] = [];
    switch (item.kind) {
      case 'models':
        item.models.forEach((model, index) => {
          const lineBreak = index > 0 ? true : undefined;
          if (model.role) {
            runs.push({ kind: 'badge', text: model.role, tone: 'role', lineBreak });
          }
          runs.push({ kind: 'text', text: model.name, strong: true, lineBreak: model.role ? undefined : lineBreak });
          for (const badge of runFactBadges(model)) {
            const tone: ImageBadgeTone = badge.kind === 'thinking' ? 'thinking'
              : badge.kind === 'reasoning' ? 'reasoning'
                : badge.kind === 'provider' ? providerImageTone(badge.provider)
                  : 'config';
            runs.push({ kind: 'badge', text: badge.text, tone });
          }
        });
        break;
      case 'prompt':
        runs.push({ kind: 'text', text: item.name });
        for (const tag of item.tags) {
          runs.push({ kind: 'badge', text: tag, tone: 'config' });
        }
        break;
      case 'text':
        runs.push({ kind: 'text', text: item.text });
        break;
      case 'time':
        runs.push({ kind: 'text', text: item.text });
        runs.push({ kind: 'badge', text: status.text, tone: status.tone });
        break;
      case 'board':
        if (item.figures.length > 0) {
          runs.push({ kind: 'text', text: item.figures.map(f => `${f.role} ${f.delivered}/${f.total}`).join(' · ') });
        }
        runs.push({ kind: 'text', text: `· ${item.note}`, muted: true });
        for (const gap of item.gaps) {
          runs.push({ kind: 'text', text: `Graded without the board — ${gap}`, warning: true, strong: true, lineBreak: true });
        }
        break;
    }
    return { label: row.label, runs, primary: row.key === 'model' || row.key === 'assessor' };
  });
}

/** The sizes a fact block is laid out at. */
export interface FactLayoutSizes {
  /** The label's size; drawn upper-case at weight 600. */
  readonly labelPx: number;
  /** The value text's size; badge text is 2 px smaller. */
  readonly textPx: number;
}

/** One placed piece of a value: a text run (or a part of one), or a badge. `x` is from the value column. */
export interface FactLayoutItem {
  readonly run: ImageFactRun;
  /** What is drawn: the words on this line, or the badge's display text. */
  readonly text: string;
  readonly x: number;
  readonly width: number;
}

export interface FactLayoutRow {
  /** Upper-cased. */
  readonly label: string;
  /** From the block's top. */
  readonly y: number;
  readonly lines: readonly (readonly FactLayoutItem[])[];
  readonly lineHeights: readonly number[];
  readonly height: number;
}

/** A block of fact rows laid out to a width, in logical pixels. */
export interface FactLayout {
  /** The widest label, upper-cased, at the label size. */
  readonly labelWidth: number;
  /** Labels above their values, for a width that leaves the value column under 60 %. */
  readonly stacked: boolean;
  /** Where the values start. */
  readonly valueX: number;
  readonly sizes: FactLayoutSizes;
  readonly rows: readonly FactLayoutRow[];
  readonly height: number;
}

const FACT_LABEL_GUTTER = 12;
const FACT_STACK_RATIO = 0.6;
const FACT_ROW_GAP = 4;
const FACT_BADGE_GAP = 6;
const FACT_BADGE_PAD_X = 6;
const FACT_BADGE_PAD_Y = 2;
const FACT_BADGE_BORDER = 1;
const FACT_BADGE_RADIUS = 4;
const FACT_BADGE_WEIGHT = '700';
const FACT_LABEL_WEIGHT = '600';
const ELLIPSIS = '…';

function factBadgePx(sizes: FactLayoutSizes): number {
  return sizes.textPx - 2;
}

/** A badge's box height: its text, padding and border. */
export function factBadgeHeight(sizes: FactLayoutSizes): number {
  return Math.round(factBadgePx(sizes) * 1.2) + FACT_BADGE_PAD_Y * 2 + FACT_BADGE_BORDER * 2;
}

function factTextWeight(run: ImageFactRun): string {
  return run.kind === 'text' && run.strong ? '600' : '400';
}

function badgeDisplayText(run: ImageFactRun & { kind: 'badge' }): string {
  return UPPER_CASE_TONES.has(run.tone) ? run.text.toUpperCase() : run.text;
}

/** `text` cut with an ellipsis to at most `maxWidth`. */
function fitText(text: string, maxWidth: number, measure: TextMeasurer, sizePx: number, weight: string): string {
  if (measure(text, sizePx, weight) <= maxWidth) {
    return text;
  }
  let cut = text;
  while (cut.length > 0 && measure(cut + ELLIPSIS, sizePx, weight) > maxWidth) {
    cut = cut.slice(0, -1);
  }
  return cut.trimEnd() + ELLIPSIS;
}

/**
 * Lays out fact rows in two columns, label and value, at `width`: values flow left to right, text
 * breaking at spaces and a badge never splitting; a word or badge wider than the value column is cut
 * with an ellipsis on a line of its own. Where the value column would be under 60 % of `width`, labels
 * go above their values. No rows is a block of height 0.
 */
export function layoutFactRows(
  rows: readonly ImageFactRow[],
  width: number,
  measure: TextMeasurer,
  sizes: FactLayoutSizes
): FactLayout {
  if (rows.length === 0) {
    return { labelWidth: 0, stacked: false, valueX: 0, sizes, rows: [], height: 0 };
  }
  const labelWidth = Math.max(...rows.map(row => measure(row.label.toUpperCase(), sizes.labelPx, FACT_LABEL_WEIGHT)));
  const stacked = width - labelWidth - FACT_LABEL_GUTTER < width * FACT_STACK_RATIO;
  const valueX = stacked ? 0 : labelWidth + FACT_LABEL_GUTTER;
  const valueWidth = Math.max(1, width - valueX);
  const badgePx = factBadgePx(sizes);
  const badgeHeight = factBadgeHeight(sizes);
  const textLineHeight = Math.max(lineHeight(sizes.textPx), lineHeight(sizes.labelPx));
  const spaceWidth = measure(' ', sizes.textPx, '400');
  const badgeChrome = FACT_BADGE_PAD_X * 2 + FACT_BADGE_BORDER * 2;

  interface Token { run: ImageFactRun; text: string; width: number; gap: number; joins: boolean; lineBreak: boolean }

  const laidOut: FactLayoutRow[] = [];
  let y = 0;
  rows.forEach((row, rowIndex) => {
    const tokens: Token[] = [];
    for (const run of row.runs) {
      const previous = tokens[tokens.length - 1];
      const leadGap = run.gapBefore
        ?? (!previous ? 0 : run.kind === 'badge' || previous.run.kind === 'badge' ? FACT_BADGE_GAP : spaceWidth);
      if (run.kind === 'badge') {
        const text = badgeDisplayText(run);
        tokens.push({ run, text, width: measure(text, badgePx, FACT_BADGE_WEIGHT) + badgeChrome, gap: leadGap, joins: false, lineBreak: !!run.lineBreak });
        continue;
      }
      const weight = factTextWeight(run);
      run.text.split(/\s+/).filter(word => word !== '').forEach((word, index) => {
        tokens.push({
          run,
          text: word,
          width: measure(word, sizes.textPx, weight),
          gap: index === 0 ? leadGap : spaceWidth,
          joins: index > 0,
          lineBreak: index === 0 && !!run.lineBreak
        });
      });
    }

    const lines: { run: ImageFactRun; text: string; x: number; width: number }[][] = [[]];
    let x = 0;
    let forceBreak = false;
    const newLine = (): void => {
      lines.push([]);
      x = 0;
      forceBreak = false;
    };
    for (const token of tokens) {
      let line = lines[lines.length - 1];
      if (line.length > 0 && (forceBreak || token.lineBreak || x + token.gap + token.width > valueWidth)) {
        newLine();
        line = lines[lines.length - 1];
      }
      if (token.width > valueWidth) {
        const text = token.run.kind === 'badge'
          ? fitText(token.text, valueWidth - badgeChrome, measure, badgePx, FACT_BADGE_WEIGHT)
          : fitText(token.text, valueWidth, measure, sizes.textPx, factTextWeight(token.run));
        const fitted = token.run.kind === 'badge'
          ? measure(text, badgePx, FACT_BADGE_WEIGHT) + badgeChrome
          : measure(text, sizes.textPx, factTextWeight(token.run));
        line.push({ run: token.run, text, x: 0, width: Math.min(valueWidth, fitted) });
        x = valueWidth;
        forceBreak = true;
        continue;
      }
      const gap = line.length > 0 ? token.gap : 0;
      const last = line[line.length - 1];
      if (token.joins && last && last.run === token.run) {
        last.text = `${last.text} ${token.text}`;
        last.width = x + gap + token.width - last.x;
      } else {
        line.push({ run: token.run, text: token.text, x: x + gap, width: token.width });
      }
      x += gap + token.width;
    }

    const lineHeights = lines.map(line => (line.some(item => item.run.kind === 'badge')
      ? Math.max(textLineHeight, badgeHeight)
      : textLineHeight));
    const height = (stacked ? lineHeight(sizes.labelPx) : 0) + lineHeights.reduce((sum, h) => sum + h, 0);
    laidOut.push({ label: row.label.toUpperCase(), y, lines, lineHeights, height });
    y += height + (rowIndex < rows.length - 1 ? FACT_ROW_GAP : 0);
  });

  return { labelWidth, stacked, valueX, sizes, rows: laidOut, height: y };
}

// -----------------------------------------------------------------------------------------------
// Text, names and footer
// -----------------------------------------------------------------------------------------------

/** `run72-intelligence-index`: a lowercase dashed identifier, safe in an id and an anchor name. */
export function keyFigureSlug(text: string): string {
  const slug = (text ?? '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
  return slug === '' ? 'figure' : slug;
}

/** `yyyy-MM-dd HH:mm` in UTC. */
export function formatUtcMinute(now: Date): string {
  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${now.getUTCFullYear()}-${pad(now.getUTCMonth() + 1)}-${pad(now.getUTCDate())} ` +
    `${pad(now.getUTCHours())}:${pad(now.getUTCMinutes())}`;
}

/** `GnollBench · Overseer {version} · exported {yyyy-MM-dd HH:mm} UTC`. */
export function keyFiguresFooterText(overseerVersion: string | null, now: Date): string {
  const version = collapseWhitespace(overseerVersion) || 'unknown';
  return `GnollBench · Overseer ${version} · exported ${formatUtcMinute(now)} UTC`;
}

/** `gnollbench_run72_<suite>_<model>_key-figures_<yyyyMMdd_HHmmss>.png`. */
export function keyFiguresFileName(context: ImageContext, now: Date): string {
  return `${fileNameStem(context)}_key-figures_${exportTimestamp(now)}.png`;
}

/** `gnollbench_run72_<suite>_<model>_<card-label>_<yyyyMMdd_HHmmss>.png`. */
export function keyFigureCardFileName(context: ImageContext, cardLabel: string, now: Date): string {
  return `${fileNameStem(context)}_${safeFileName(cardLabel)}_${exportTimestamp(now)}.png`;
}

function fileNameStem(context: ImageContext): string {
  return `gnollbench_run${context.runId}_${safeFileName(context.suiteName)}_${safeFileName(context.modelName)}`;
}

/** The status line for one export; `subject` is `Key figures` or the card's label. */
export function keyFiguresStatusMessage(outcome: KeyFiguresOutcome, subject: string): string {
  switch (outcome) {
    case 'copied':
      return `${subject} copied as an image.`;
    case 'unsupported':
      return 'This browser cannot copy images here; use Download instead.';
    case 'denied':
      return 'Could not copy the image.';
    case 'downloaded':
      return 'Image downloaded.';
    case 'empty':
      return 'None of this run\'s key figures is selected; use Choose figures.';
    default:
      return 'Could not create the image.';
  }
}

// -----------------------------------------------------------------------------------------------
// Strip layout
// -----------------------------------------------------------------------------------------------

const STRIP_PAD = 24;
const STRIP_GAP = 10;
const STRIP_HEADER_GAP = 16;
export const STRIP_LOGO_HEIGHT = 36;
const STRIP_LOGO_GAP = 12;
const STRIP_TITLE_PX = 18;
const STRIP_LINE_PX = 13;
const STRIP_LINE_GAP = 4;
const STRIP_CARD_PAD = 12;
const STRIP_CARD_GAP = 6;
const STRIP_LABEL_PX = 12;
const STRIP_MAIN_VALUE_PX = 22;
const STRIP_VALUE_PX = 16;
const STRIP_NOTE_PX = 12;
const STRIP_FOOTNOTE_PX = 12;
const STRIP_FOOTNOTE_GAP = 12;
const STRIP_FOOTER_PX = 12;
const STRIP_RULE_GAP = 12;
export const STRIP_MIN_CARD_WIDTH = 220;
export const STRIP_MAX_CARD_WIDTH = 340;
export const STRIP_CARD_WIDTH_STEP = 10;
/** A layout at most this much wider than tall is padded to an exact square. */
export const STRIP_SQUARE_TOLERANCE = 1.1;
/** The widest card a single row widens to when no layout in the range is landscape. */
const STRIP_FALLBACK_MAX_CARD_WIDTH = 1200;
const STRIP_FACT_SIZES: FactLayoutSizes = { labelPx: STRIP_LABEL_PX, textPx: STRIP_LINE_PX };

/** The texts the strip image carries besides its cards. */
export interface StripText {
  readonly title: string;
  readonly facts: readonly ImageFactRow[];
  readonly footnotes: readonly string[];
  readonly footer: string;
}

/** One card wrapped to a width. */
export interface MeasuredKeyFigureCard {
  readonly labelLines: readonly string[];
  readonly valueLines: readonly string[];
  readonly valuePx: number;
  readonly valueWeight: string;
  readonly noteLines: readonly (readonly string[])[];
  readonly notePx: number;
  readonly height: number;
}

/** One card's box, relative to the grid's top left corner. */
export interface StripPlacement {
  readonly index: number;
  readonly x: number;
  readonly y: number;
  readonly width: number;
  /** The row's height: the tallest card in it. */
  readonly height: number;
  readonly card: MeasuredKeyFigureCard;
}

/** The chosen strip composition, in logical pixels. */
export interface StripLayout {
  readonly columns: number;
  readonly cardWidth: number;
  /** The main card spans two columns. */
  readonly mainSpans: boolean;
  readonly width: number;
  readonly height: number;
  /** The height before square padding. */
  readonly naturalHeight: number;
  readonly square: boolean;
  readonly logoHeight: number;
  readonly titleLines: readonly string[];
  readonly facts: FactLayout;
  readonly footnoteLines: readonly (readonly string[])[];
  readonly footerLines: readonly string[];
  readonly gridTop: number;
  readonly gridWidth: number;
  readonly gridHeight: number;
  /** Where the footnotes start, or the footer's rule when there are none. */
  readonly afterGridTop: number;
  readonly placements: readonly StripPlacement[];
}

function lineHeight(size: number): number {
  return Math.round(size * 1.4);
}

function blockHeight(lines: readonly string[], size: number): number {
  return lines.length * lineHeight(size);
}

/** Sums blocks with `gap` between each two non-empty ones. */
function stackHeight(blocks: readonly number[], gap: number): number {
  const present = blocks.filter(height => height > 0);
  return present.reduce((sum, height) => sum + height, 0) + Math.max(0, present.length - 1) * gap;
}

/** One card of the strip wrapped to `width`, with its padding. */
export function measureStripCard(cell: KeyFigureCell, width: number, wrap: TextWrapper): MeasuredKeyFigureCard {
  const inner = Math.max(1, width - STRIP_CARD_PAD * 2);
  const valuePx = cell.headline ? STRIP_MAIN_VALUE_PX : STRIP_VALUE_PX;
  const valueWeight = cell.headline ? '800' : '700';
  const labelLines = wrap(cell.label.toUpperCase(), inner, STRIP_LABEL_PX, '600');
  const valueLines = wrap(cell.value, inner, valuePx, valueWeight);
  const noteLines = cell.notes.map(note => wrap(note, inner, STRIP_NOTE_PX, '400'));
  const height = STRIP_CARD_PAD * 2 + stackHeight([
    blockHeight(labelLines, STRIP_LABEL_PX),
    blockHeight(valueLines, valuePx),
    ...noteLines.map(lines => blockHeight(lines, STRIP_NOTE_PX))
  ], STRIP_CARD_GAP);
  return { labelLines, valueLines, valuePx, valueWeight, noteLines, notePx: STRIP_NOTE_PX, height };
}

interface StripCandidate {
  readonly columns: number;
  readonly cardWidth: number;
  readonly mainSpans: boolean;
  readonly width: number;
  readonly naturalHeight: number;
  readonly titleLines: readonly string[];
  readonly facts: FactLayout;
  readonly footnoteLines: readonly (readonly string[])[];
  readonly footerLines: readonly string[];
  readonly headerHeight: number;
  readonly gridWidth: number;
  readonly gridHeight: number;
  readonly placements: readonly StripPlacement[];
}

/**
 * The strip's composition: for every column count from 1 to the number of cells and every card width
 * from {@link STRIP_MIN_CARD_WIDTH} to {@link STRIP_MAX_CARD_WIDTH}, the main card spanning two
 * columns or not where there are at least three, the layout whose width is at least its height and
 * whose ratio is closest to 1 (ties: more columns, then wider cards, then the spanning main card).
 * Within {@link STRIP_SQUARE_TOLERANCE} the height is padded to the width, the extra space split
 * above and below the grid. Where no candidate is landscape, a single row widens its cards until it
 * is, and past {@link STRIP_FALLBACK_MAX_CARD_WIDTH} the width is padded to the height, so the result
 * is never portrait. `cells` may be any selection of the cards, with or without the main card. The
 * run facts under the title are laid out at the grid's width by {@link layoutFactRows}.
 */
export function chooseStripLayout(
  cells: readonly KeyFigureCell[],
  text: StripText,
  wrap: TextWrapper,
  logoHeight: number = STRIP_LOGO_HEIGHT,
  measure: TextMeasurer = estimateTextWidth
): StripLayout {
  const factCache = new Map<number, FactLayout>();
  const factsAt = (gridWidth: number): FactLayout => {
    let facts = factCache.get(gridWidth);
    if (!facts) {
      facts = layoutFactRows(text.facts, gridWidth, measure, STRIP_FACT_SIZES);
      factCache.set(gridWidth, facts);
    }
    return facts;
  };
  const cache = new Map<string, MeasuredKeyFigureCard>();
  const measureCard = (index: number, width: number): MeasuredKeyFigureCard => {
    const key = `${index}:${width}`;
    let measured = cache.get(key);
    if (!measured) {
      measured = measureStripCard(cells[index], width, wrap);
      cache.set(key, measured);
    }
    return measured;
  };
  const mainIndex = cells.findIndex(cell => cell.main);

  const candidate = (columns: number, cardWidth: number, mainSpans: boolean): StripCandidate => {
    const gridWidth = columns * cardWidth + (columns - 1) * STRIP_GAP;
    const rows: { index: number; column: number; span: number }[][] = [];
    let row: { index: number; column: number; span: number }[] = [];
    let column = 0;
    cells.forEach((_, index) => {
      const span = mainSpans && index === mainIndex ? Math.min(2, columns) : 1;
      if (column + span > columns) {
        rows.push(row);
        row = [];
        column = 0;
      }
      row.push({ index, column, span });
      column += span;
    });
    if (row.length > 0) {
      rows.push(row);
    }

    const placements: StripPlacement[] = [];
    let y = 0;
    rows.forEach((slots, rowIndex) => {
      const cards = slots.map(slot => {
        const width = slot.span * cardWidth + (slot.span - 1) * STRIP_GAP;
        return { slot, width, card: measureCard(slot.index, width) };
      });
      const height = Math.max(...cards.map(entry => entry.card.height));
      for (const entry of cards) {
        placements.push({
          index: entry.slot.index,
          x: entry.slot.column * (cardWidth + STRIP_GAP),
          y,
          width: entry.width,
          height,
          card: entry.card
        });
      }
      y += height + (rowIndex < rows.length - 1 ? STRIP_GAP : 0);
    });
    const gridHeight = y;

    const titleLines = wrap(text.title, gridWidth, STRIP_TITLE_PX, '700');
    const facts = factsAt(gridWidth);
    const headerHeight = (logoHeight > 0 ? logoHeight + STRIP_LOGO_GAP : 0)
      + blockHeight(titleLines, STRIP_TITLE_PX)
      + (facts.height > 0 ? STRIP_LINE_GAP + facts.height : 0);
    const footnoteLines = text.footnotes.map(footnote => wrap(footnote, gridWidth, STRIP_FOOTNOTE_PX, '400'));
    const footnotesHeight = footnoteLines.length > 0
      ? STRIP_FOOTNOTE_GAP + stackHeight(footnoteLines.map(lines => blockHeight(lines, STRIP_FOOTNOTE_PX)), 2)
      : 0;
    const footerLines = wrap(text.footer, gridWidth, STRIP_FOOTER_PX, '400');
    const footerHeight = footerLines.length > 0 ? STRIP_RULE_GAP + blockHeight(footerLines, STRIP_FOOTER_PX) : 0;

    return {
      columns,
      cardWidth,
      mainSpans,
      width: gridWidth + STRIP_PAD * 2,
      naturalHeight: STRIP_PAD * 2 + headerHeight + (cells.length > 0 ? STRIP_HEADER_GAP + gridHeight : 0)
        + footnotesHeight + footerHeight,
      titleLines,
      facts,
      footnoteLines,
      footerLines,
      headerHeight,
      gridWidth,
      gridHeight,
      placements
    };
  };

  const count = Math.max(1, cells.length);
  const candidates: StripCandidate[] = [];
  for (let columns = 1; columns <= count; columns++) {
    for (let width = STRIP_MIN_CARD_WIDTH; width <= STRIP_MAX_CARD_WIDTH; width += STRIP_CARD_WIDTH_STEP) {
      candidates.push(candidate(columns, width, false));
      if (columns >= 3 && mainIndex >= 0) {
        candidates.push(candidate(columns, width, true));
      }
    }
  }

  const landscape = candidates.filter(entry => entry.width >= entry.naturalHeight);
  let chosen: StripCandidate;
  if (landscape.length > 0) {
    chosen = landscape.reduce((best, entry) => (betterStripCandidate(entry, best) ? entry : best));
  } else {
    let width = STRIP_MAX_CARD_WIDTH;
    chosen = candidate(count, width, false);
    while (chosen.width < chosen.naturalHeight && width < STRIP_FALLBACK_MAX_CARD_WIDTH) {
      width += STRIP_CARD_WIDTH_STEP;
      chosen = candidate(count, width, false);
    }
  }

  const ratio = chosen.width / chosen.naturalHeight;
  const square = ratio <= STRIP_SQUARE_TOLERANCE;
  const imageWidth = Math.max(chosen.width, chosen.naturalHeight);
  const height = square ? imageWidth : chosen.naturalHeight;
  const extra = height - chosen.naturalHeight;
  const above = Math.floor(extra / 2);
  const gridTop = STRIP_PAD + chosen.headerHeight + STRIP_HEADER_GAP + above;

  return {
    columns: chosen.columns,
    cardWidth: chosen.cardWidth,
    mainSpans: chosen.mainSpans,
    width: imageWidth,
    height,
    naturalHeight: chosen.naturalHeight,
    square: imageWidth === height,
    logoHeight,
    titleLines: chosen.titleLines,
    facts: chosen.facts,
    footnoteLines: chosen.footnoteLines,
    footerLines: chosen.footerLines,
    gridTop,
    gridWidth: chosen.gridWidth,
    gridHeight: chosen.gridHeight,
    afterGridTop: gridTop + chosen.gridHeight + (extra - above),
    placements: chosen.placements
  };
}

function betterStripCandidate(entry: StripCandidate, best: StripCandidate): boolean {
  const epsilon = 1e-9;
  const entryRatio = entry.width / entry.naturalHeight;
  const bestRatio = best.width / best.naturalHeight;
  if (Math.abs(entryRatio - bestRatio) > epsilon) {
    return entryRatio < bestRatio;
  }
  if (entry.columns !== best.columns) {
    return entry.columns > best.columns;
  }
  if (entry.cardWidth !== best.cardWidth) {
    return entry.cardWidth > best.cardWidth;
  }
  return entry.mainSpans && !best.mainSpans;
}

// -----------------------------------------------------------------------------------------------
// Card image size
// -----------------------------------------------------------------------------------------------

export const CARD_IMAGE_SIZE = 640;
/** 4 : 3 of {@link CARD_IMAGE_SIZE}, rounded to a whole pixel. */
export const CARD_IMAGE_MAX_WIDTH = Math.round(CARD_IMAGE_SIZE * 4 / 3);
export const CARD_IMAGE_WIDTH_STEP = 40;
export const CARD_IMAGE_NOTE_PX = 18;
export const CARD_IMAGE_SMALL_NOTE_PX = 15;
const CARD_IMAGE_PAD = 32;
const CARD_IMAGE_EMBLEM = 40;
const CARD_IMAGE_EMBLEM_GAP = 14;
const CARD_IMAGE_TITLE_PX = 18;
const CARD_IMAGE_LINE_PX = 14;
const CARD_IMAGE_HEADER_GAP = 24;
const CARD_IMAGE_CARD_PAD = 28;
const CARD_IMAGE_LABEL_PX = 20;
const CARD_IMAGE_MAIN_VALUE_PX = 52;
const CARD_IMAGE_VALUE_PX = 40;
const CARD_IMAGE_BLOCK_GAP = 12;
const CARD_IMAGE_NOTE_GAP = 8;
const CARD_IMAGE_RADIUS = 12;
const CARD_IMAGE_FOOTNOTE_PX = 14;
const CARD_IMAGE_FOOTNOTE_GAP = 16;
const CARD_IMAGE_FOOTER_PX = 14;
const CARD_IMAGE_RULE_GAP = 16;
const CARD_IMAGE_FACT_SIZES: FactLayoutSizes = { labelPx: 12, textPx: CARD_IMAGE_LINE_PX };

/** The texts the card image carries besides its card. */
export interface CardImageText {
  readonly title: string;
  /** Only the primary rows are drawn. */
  readonly facts: readonly ImageFactRow[];
  readonly footnotes: readonly string[];
  readonly footer: string;
}

/** The card image's size and every wrapped block, in logical pixels. */
export interface CardImageLayout {
  readonly width: number;
  readonly height: number;
  readonly notePx: number;
  /** False when the card overflows even at 4 : 3 with the smaller notes. */
  readonly fits: boolean;
  readonly headerTextX: number;
  readonly titleLines: readonly string[];
  readonly facts: FactLayout;
  readonly headerHeight: number;
  readonly cardWidth: number;
  readonly card: MeasuredKeyFigureCard;
  readonly footnoteLines: readonly (readonly string[])[];
  readonly footerLines: readonly string[];
  /** The height of the footnotes and the footer together, anchored to the bottom padding. */
  readonly bottomHeight: number;
}

/** One card wrapped for the card image. */
function measureImageCard(cell: KeyFigureCell, width: number, notePx: number, wrap: TextWrapper): MeasuredKeyFigureCard {
  const inner = Math.max(1, width - CARD_IMAGE_CARD_PAD * 2);
  const valuePx = cell.headline ? CARD_IMAGE_MAIN_VALUE_PX : CARD_IMAGE_VALUE_PX;
  const valueWeight = cell.headline ? '800' : '700';
  const labelLines = wrap(cell.label.toUpperCase(), inner, CARD_IMAGE_LABEL_PX, '600');
  const valueLines = wrap(cell.value, inner, valuePx, valueWeight);
  const noteLines = cell.notes.map(note => wrap(note, inner, notePx, '400'));
  const notesHeight = stackHeight(noteLines.map(lines => blockHeight(lines, notePx)), CARD_IMAGE_NOTE_GAP);
  const height = CARD_IMAGE_CARD_PAD * 2 + stackHeight([
    blockHeight(labelLines, CARD_IMAGE_LABEL_PX),
    blockHeight(valueLines, valuePx),
    notesHeight
  ], CARD_IMAGE_BLOCK_GAP);
  return { labelLines, valueLines, valuePx, valueWeight, noteLines, notePx, height };
}

function cardImageLayoutAt(
  cell: KeyFigureCell,
  text: CardImageText,
  wrap: TextWrapper,
  width: number,
  notePx: number,
  hasEmblem: boolean,
  measure: TextMeasurer
): CardImageLayout {
  const height = CARD_IMAGE_SIZE;
  const contentWidth = width - CARD_IMAGE_PAD * 2;
  const headerTextX = CARD_IMAGE_PAD + (hasEmblem ? CARD_IMAGE_EMBLEM + CARD_IMAGE_EMBLEM_GAP : 0);
  const headerTextWidth = width - CARD_IMAGE_PAD - headerTextX;
  const titleLines = wrap(text.title, headerTextWidth, CARD_IMAGE_TITLE_PX, '700');
  const facts = layoutFactRows(text.facts.filter(row => row.primary), headerTextWidth, measure, CARD_IMAGE_FACT_SIZES);
  const headerHeight = Math.max(
    hasEmblem ? CARD_IMAGE_EMBLEM : 0,
    stackHeight([blockHeight(titleLines, CARD_IMAGE_TITLE_PX), facts.height], 4)
  );
  const card = measureImageCard(cell, contentWidth, notePx, wrap);
  const footnoteLines = text.footnotes.map(footnote => wrap(footnote, contentWidth, CARD_IMAGE_FOOTNOTE_PX, '400'));
  const footerLines = wrap(text.footer, contentWidth, CARD_IMAGE_FOOTER_PX, '400');
  const bottomHeight =
    (footnoteLines.length > 0
      ? CARD_IMAGE_FOOTNOTE_GAP + stackHeight(footnoteLines.map(lines => blockHeight(lines, CARD_IMAGE_FOOTNOTE_PX)), 4)
      : 0)
    + (footerLines.length > 0 ? CARD_IMAGE_RULE_GAP + blockHeight(footerLines, CARD_IMAGE_FOOTER_PX) : 0);
  const needed = CARD_IMAGE_PAD * 2 + headerHeight + CARD_IMAGE_HEADER_GAP + card.height + CARD_IMAGE_HEADER_GAP + bottomHeight;
  return {
    width,
    height,
    notePx,
    fits: needed <= height,
    headerTextX,
    titleLines,
    facts,
    headerHeight,
    cardWidth: contentWidth,
    card,
    footnoteLines,
    footerLines,
    bottomHeight
  };
}

/**
 * The card image's size: {@link CARD_IMAGE_SIZE} square when the card fits; otherwise widened in
 * {@link CARD_IMAGE_WIDTH_STEP} steps, the text column widening with it, up to
 * {@link CARD_IMAGE_MAX_WIDTH} (4 : 3); a card that still does not fit is laid out at 4 : 3 with its
 * notes at {@link CARD_IMAGE_SMALL_NOTE_PX}. The height never changes, so the image is never portrait.
 * The header carries the primary run facts, laid out beside the emblem.
 */
export function chooseCardImageLayout(
  cell: KeyFigureCell,
  text: CardImageText,
  wrap: TextWrapper,
  hasEmblem = true,
  measure: TextMeasurer = estimateTextWidth
): CardImageLayout {
  const widths: number[] = [];
  for (let width = CARD_IMAGE_SIZE; width < CARD_IMAGE_MAX_WIDTH; width += CARD_IMAGE_WIDTH_STEP) {
    widths.push(width);
  }
  widths.push(CARD_IMAGE_MAX_WIDTH);
  for (const width of widths) {
    const layout = cardImageLayoutAt(cell, text, wrap, width, CARD_IMAGE_NOTE_PX, hasEmblem, measure);
    if (layout.fits) {
      return layout;
    }
  }
  return cardImageLayoutAt(cell, text, wrap, CARD_IMAGE_MAX_WIDTH, CARD_IMAGE_SMALL_NOTE_PX, hasEmblem, measure);
}

// -----------------------------------------------------------------------------------------------
// Drawing
// -----------------------------------------------------------------------------------------------

function fontOf(size: number, weight: string): string {
  return `${weight} ${size}px ${FIGURE_FONT_STACK}`;
}

/** A {@link TextWrapper} measuring in `context`, through the figure composer's own `wrapText`. */
export function canvasTextWrapper(context: CanvasRenderingContext2D): TextWrapper {
  return (text, maxWidth, sizePx, weight) => wrapText(context, text, maxWidth, sizePx, weight, FIGURE_FONT_STACK);
}

/** A {@link TextMeasurer} measuring in `context`. */
export function canvasTextMeasurer(context: CanvasRenderingContext2D): TextMeasurer {
  return (text, sizePx, weight) => {
    context.font = fontOf(sizePx, weight);
    return context.measureText(text).width;
  };
}

/** Waits for the figure font in every weight the images use; a font that cannot load is skipped. */
export async function loadKeyFigureFonts(): Promise<void> {
  const fonts = typeof document !== 'undefined' ? document.fonts : undefined;
  if (!fonts || typeof fonts.load !== 'function') {
    return;
  }
  await Promise.all(['400', '600', '700', '800'].map(weight =>
    fonts.load(fontOf(16, weight)).catch(() => [])));
}

function drawLines(
  context: CanvasRenderingContext2D,
  lines: readonly string[],
  x: number,
  y: number,
  size: number,
  weight: string,
  color: string
): number {
  context.font = fontOf(size, weight);
  context.fillStyle = color;
  const height = lineHeight(size);
  for (const line of lines) {
    // Centered in its line box, as the dialog's line-height centers it.
    context.fillText(line, x, y + (height - size) / 2);
    y += height;
  }
  return y;
}

function pathRoundedRect(context: CanvasRenderingContext2D, x: number, y: number, width: number, height: number, radius: number): void {
  const r = Math.max(0, Math.min(radius, width / 2, height / 2));
  context.beginPath();
  context.moveTo(x + r, y);
  context.lineTo(x + width - r, y);
  context.arcTo(x + width, y, x + width, y + r, r);
  context.lineTo(x + width, y + height - r);
  context.arcTo(x + width, y + height, x + width - r, y + height, r);
  context.lineTo(x + r, y + height);
  context.arcTo(x, y + height, x, y + height - r, r);
  context.lineTo(x, y + r);
  context.arcTo(x, y, x + r, y, r);
  context.closePath();
}

function drawCardBox(context: CanvasRenderingContext2D, x: number, y: number, width: number, height: number, radius: number): void {
  pathRoundedRect(context, x + 0.5, y + 0.5, width - 1, height - 1, radius);
  context.fillStyle = CARD_FILL;
  context.fill();
  context.strokeStyle = CARD_BORDER;
  context.lineWidth = 1;
  context.stroke();
}

/** Draws a card's label, value and notes from `y`, left-aligned at `x` or centered on it. */
function drawCardText(
  context: CanvasRenderingContext2D,
  cell: KeyFigureCell,
  card: MeasuredKeyFigureCard,
  x: number,
  y: number,
  labelPx: number,
  blockGap: number,
  noteGap: number
): void {
  const blocks: (() => number)[] = [];
  if (card.labelLines.length > 0) {
    blocks.push(() => drawLines(context, card.labelLines, x, y, labelPx, '600', LABEL_COLOR));
  }
  if (card.valueLines.length > 0) {
    const color = cell.tone ? TONE_COLORS[cell.tone] : VALUE_COLOR;
    blocks.push(() => drawLines(context, card.valueLines, x, y, card.valuePx, card.valueWeight, color));
  }
  const notes = card.noteLines.filter(lines => lines.length > 0);
  if (notes.length > 0) {
    blocks.push(() => {
      notes.forEach((lines, index) => {
        if (index > 0) {
          y += noteGap;
        }
        y = drawLines(context, lines, x, y, card.notePx, '400', NOTE_COLOR);
      });
      return y;
    });
  }
  blocks.forEach((draw, index) => {
    if (index > 0) {
      y += blockGap;
    }
    y = draw();
  });
}

/** Draws a block of fact rows laid out by {@link layoutFactRows} with its top left corner at `x`, `y`. */
function drawFactRows(context: CanvasRenderingContext2D, layout: FactLayout, x: number, y: number): void {
  const { labelPx, textPx } = layout.sizes;
  const badgePx = factBadgePx(layout.sizes);
  const badgeHeight = factBadgeHeight(layout.sizes);
  const labelLineHeight = lineHeight(labelPx);
  for (const row of layout.rows) {
    const rowTop = y + row.y;
    const firstLine = layout.stacked ? labelLineHeight : (row.lineHeights[0] ?? labelLineHeight);
    context.font = fontOf(labelPx, FACT_LABEL_WEIGHT);
    context.fillStyle = LABEL_COLOR;
    context.fillText(row.label, x, rowTop + (firstLine - labelPx) / 2);

    let lineTop = rowTop + (layout.stacked ? labelLineHeight : 0);
    row.lines.forEach((line, index) => {
      const height = row.lineHeights[index];
      for (const item of line) {
        const left = x + layout.valueX + item.x;
        if (item.run.kind === 'badge') {
          const palette = BADGE_PALETTE[item.run.tone];
          const top = lineTop + (height - badgeHeight) / 2;
          pathRoundedRect(context, left + 0.5, top + 0.5, item.width - 1, badgeHeight - 1, FACT_BADGE_RADIUS);
          context.fillStyle = palette.fill;
          context.fill();
          context.strokeStyle = palette.border;
          context.lineWidth = 1;
          context.stroke();
          context.font = fontOf(badgePx, FACT_BADGE_WEIGHT);
          context.fillStyle = palette.ink;
          context.fillText(item.text, left + FACT_BADGE_PAD_X + FACT_BADGE_BORDER, top + (badgeHeight - badgePx) / 2);
        } else {
          const run = item.run;
          context.font = fontOf(textPx, factTextWeight(run));
          context.fillStyle = run.warning ? WARNING_COLOR : run.muted ? NOTE_COLOR : run.strong ? VALUE_COLOR : FIGURE_MUTED_COLOR;
          context.fillText(item.text, left, lineTop + (height - textPx) / 2);
        }
      }
      lineTop += height;
    });
  }
}

function drawLogo(context: CanvasRenderingContext2D, logo: KeyFigureLogo, x: number, y: number, height: number): void {
  const width = logo.width * height / logo.height;
  context.drawImage(logo.image, x, y, width, height);
}

function drawFooter(context: CanvasRenderingContext2D, lines: readonly string[], x: number, y: number, width: number, ruleGap: number, size: number): void {
  if (lines.length === 0) {
    return;
  }
  context.strokeStyle = FIGURE_RULE_COLOR;
  context.lineWidth = 1;
  context.beginPath();
  const ruleY = Math.round(y + ruleGap / 2) + 0.5;
  context.moveTo(x, ruleY);
  context.lineTo(x + width, ruleY);
  context.stroke();
  drawLines(context, lines, x, y + ruleGap, size, '400', FIGURE_MUTED_COLOR);
}

function newCanvas(width: number, height: number): { canvas: HTMLCanvasElement; context: CanvasRenderingContext2D | null } {
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(width * KEY_FIGURES_IMAGE_SCALE);
  canvas.height = Math.round(height * KEY_FIGURES_IMAGE_SCALE);
  const context = canvas.getContext('2d');
  if (context) {
    context.scale(KEY_FIGURES_IMAGE_SCALE, KEY_FIGURES_IMAGE_SCALE);
    context.textBaseline = 'top';
    context.fillStyle = FIGURE_BACKGROUND;
    context.fillRect(0, 0, width, height);
  }
  return { canvas, context };
}

function measuringContext(): CanvasRenderingContext2D | null {
  return document.createElement('canvas').getContext('2d');
}

/** Distinct footnotes of every cell, in order. */
export function stripFootnotes(cells: readonly KeyFigureCell[]): string[] {
  const footnotes: string[] = [];
  for (const footnote of cells.flatMap(cell => cell.footnotes)) {
    if (!footnotes.includes(footnote)) {
      footnotes.push(footnote);
    }
  }
  return footnotes;
}

/** The whole key-figures strip on one canvas, laid out by {@link chooseStripLayout}. */
export async function composeStripImage(
  cells: readonly KeyFigureCell[],
  context: ImageContext,
  logos: KeyFigureLogos,
  now: Date
): Promise<HTMLCanvasElement> {
  await loadKeyFigureFonts();
  const measure = measuringContext();
  const wrap: TextWrapper = measure ? canvasTextWrapper(measure) : text => (text.trim() ? [text] : []);
  const layout = chooseStripLayout(cells, {
    title: context.title,
    facts: context.facts,
    footnotes: stripFootnotes(cells),
    footer: keyFiguresFooterText(context.overseerVersion, now)
  }, wrap, logos.wide ? STRIP_LOGO_HEIGHT : 0, measure ? canvasTextMeasurer(measure) : estimateTextWidth);

  const { canvas, context: draw } = newCanvas(layout.width, layout.height);
  if (!draw) {
    return canvas;
  }

  const left = (layout.width - layout.gridWidth) / 2;
  let y = STRIP_PAD;
  if (logos.wide && layout.logoHeight > 0) {
    drawLogo(draw, logos.wide, left, y, layout.logoHeight);
    y += layout.logoHeight + STRIP_LOGO_GAP;
  }
  y = drawLines(draw, layout.titleLines, left, y, STRIP_TITLE_PX, '700', FIGURE_TITLE_COLOR);
  if (layout.facts.height > 0) {
    drawFactRows(draw, layout.facts, left, y + STRIP_LINE_GAP);
  }

  for (const placement of layout.placements) {
    const x = left + placement.x;
    const top = layout.gridTop + placement.y;
    drawCardBox(draw, x, top, placement.width, placement.height, 6);
    drawCardText(draw, cells[placement.index], placement.card, x + STRIP_CARD_PAD, top + STRIP_CARD_PAD,
      STRIP_LABEL_PX, STRIP_CARD_GAP, STRIP_CARD_GAP);
  }

  y = layout.afterGridTop;
  if (layout.footnoteLines.length > 0) {
    y += STRIP_FOOTNOTE_GAP;
    layout.footnoteLines.forEach((lines, index) => {
      y = drawLines(draw, lines, left, y + (index > 0 ? 2 : 0), STRIP_FOOTNOTE_PX, '400', NOTE_COLOR);
    });
  }
  drawFooter(draw, layout.footerLines, left, y, layout.gridWidth, STRIP_RULE_GAP, STRIP_FOOTER_PX);
  return canvas;
}

/** One card, enlarged and centered, laid out by {@link chooseCardImageLayout}. */
export async function composeCardImage(
  cell: KeyFigureCell,
  context: ImageContext,
  logos: KeyFigureLogos,
  now: Date
): Promise<HTMLCanvasElement> {
  await loadKeyFigureFonts();
  const measure = measuringContext();
  const wrap: TextWrapper = measure ? canvasTextWrapper(measure) : text => (text.trim() ? [text] : []);
  const layout = chooseCardImageLayout(cell, {
    title: context.title,
    facts: context.facts,
    footnotes: cell.footnotes,
    footer: keyFiguresFooterText(context.overseerVersion, now)
  }, wrap, logos.emblem !== null, measure ? canvasTextMeasurer(measure) : estimateTextWidth);

  const { canvas, context: draw } = newCanvas(layout.width, layout.height);
  if (!draw) {
    return canvas;
  }

  const top = CARD_IMAGE_PAD;
  if (logos.emblem) {
    drawLogo(draw, logos.emblem, CARD_IMAGE_PAD, top, CARD_IMAGE_EMBLEM);
  }
  const textHeight = stackHeight([blockHeight(layout.titleLines, CARD_IMAGE_TITLE_PX), layout.facts.height], 4);
  let y = top + Math.max(0, (layout.headerHeight - textHeight) / 2);
  y = drawLines(draw, layout.titleLines, layout.headerTextX, y, CARD_IMAGE_TITLE_PX, '700', FIGURE_TITLE_COLOR);
  if (layout.facts.height > 0) {
    drawFactRows(draw, layout.facts, layout.headerTextX, y + 4);
  }

  // The card is centered in the space between the header and the footnotes and footer.
  const regionTop = top + layout.headerHeight + CARD_IMAGE_HEADER_GAP;
  const bottomTop = layout.height - CARD_IMAGE_PAD - layout.bottomHeight;
  const regionHeight = bottomTop - CARD_IMAGE_HEADER_GAP - regionTop;
  const cardTop = regionTop + Math.max(0, (regionHeight - layout.card.height) / 2);
  drawCardBox(draw, CARD_IMAGE_PAD, cardTop, layout.cardWidth, layout.card.height, CARD_IMAGE_RADIUS);
  draw.textAlign = 'center';
  drawCardText(draw, cell, layout.card, CARD_IMAGE_PAD + layout.cardWidth / 2, cardTop + CARD_IMAGE_CARD_PAD,
    CARD_IMAGE_LABEL_PX, CARD_IMAGE_BLOCK_GAP, CARD_IMAGE_NOTE_GAP);
  draw.textAlign = 'left';

  y = bottomTop;
  if (layout.footnoteLines.length > 0) {
    y += CARD_IMAGE_FOOTNOTE_GAP;
    layout.footnoteLines.forEach((lines, index) => {
      y = drawLines(draw, lines, CARD_IMAGE_PAD, y + (index > 0 ? 4 : 0), CARD_IMAGE_FOOTNOTE_PX, '400', NOTE_COLOR);
    });
  }
  drawFooter(draw, layout.footerLines, CARD_IMAGE_PAD, y, layout.cardWidth, CARD_IMAGE_RULE_GAP, CARD_IMAGE_FOOTER_PX);
  return canvas;
}

// -----------------------------------------------------------------------------------------------
// Export
// -----------------------------------------------------------------------------------------------

function loadImageElement(url: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.decoding = 'async';
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error(`Could not load ${url}`));
    image.src = url;
  });
}

async function loadLogo(url: string): Promise<KeyFigureLogo | null> {
  try {
    const image = await keyFiguresImageIo.loadImage(url);
    const width = image instanceof HTMLImageElement ? image.naturalWidth || image.width : image.width;
    const height = image instanceof HTMLImageElement ? image.naturalHeight || image.height : image.height;
    return width > 0 && height > 0 ? { image, width, height } : null;
  } catch {
    return null;
  }
}

/** Both logos; one that fails to load is null, and the image is drawn without it. */
export async function loadKeyFigureLogos(): Promise<KeyFigureLogos> {
  const [wide, emblem] = await Promise.all([loadLogo(GNOLLBENCH_WIDE_LOGO_URL), loadLogo(GNOLLBENCH_EMBLEM_URL)]);
  return { wide, emblem };
}

/** The strip under `root`, limited to the cells `include` accepts, encoded as PNG. */
export async function renderKeyFiguresStripImage(
  root: ParentNode,
  context: ImageContext,
  include?: KeyFigureFilter
): Promise<KeyFiguresImage> {
  const cells = filterKeyFigureCells(readKeyFigureCells(root), include);
  const now = keyFiguresImageIo.now();
  const canvas = await composeStripImage(cells, context, await loadKeyFigureLogos(), now);
  const { blob } = await encodeFigureImage(canvas, 'png');
  return { blob, fileName: keyFiguresFileName(context, now) };
}

/** One card, encoded as PNG. */
export async function renderKeyFigureCardImage(card: HTMLElement, context: ImageContext): Promise<KeyFiguresImage> {
  const cell = readKeyFigureCell(card);
  const now = keyFiguresImageIo.now();
  const canvas = await composeCardImage(cell, context, await loadKeyFigureLogos(), now);
  const { blob } = await encodeFigureImage(canvas, 'png');
  return { blob, fileName: keyFigureCardFileName(context, cell.label, now) };
}

/**
 * Composes the strip (`card` null, read under `root` and limited to the cells `include` accepts) or
 * one card, then copies or saves it, and returns the status line to announce. A strip with no cell
 * left is not composed. Never throws.
 */
export async function exportKeyFiguresImage(
  action: KeyFiguresAction,
  root: ParentNode,
  card: HTMLElement | null,
  context: ImageContext,
  include?: KeyFigureFilter
): Promise<string> {
  const subject = card ? (readKeyFigureCell(card).label || 'Key figure') : 'Key figures';
  if (!card && filterKeyFigureCells(readKeyFigureCells(root), include).length === 0) {
    return keyFiguresStatusMessage('empty', subject);
  }
  let outcome: KeyFiguresOutcome;
  try {
    const image = card
      ? await renderKeyFigureCardImage(card, context)
      : await renderKeyFiguresStripImage(root, context, include);
    if (action === 'download') {
      keyFiguresImageIo.save(image.blob, image.fileName);
      outcome = 'downloaded';
    } else {
      outcome = await keyFiguresImageIo.copy(image.blob);
    }
  } catch {
    outcome = 'failed';
  }
  return keyFiguresStatusMessage(outcome, subject);
}
