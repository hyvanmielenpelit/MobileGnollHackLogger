/**
 * Lays one Results section's blocks out as a list of drawing operations, in layout pixels: the header
 * (the wide logo, the title, the model line and the analysis line), the blocks, and the footer. Cards go
 * two to a row from {@link CC_RESULTS_IMAGE_TWO_COLUMN_WIDTH}. Colors are named by role and resolved
 * by the draw pass against the chosen palette.
 *
 * Pure: text is measured through a {@link TextWrapper} and a {@link TextMeasurer}, so the layout
 * unit-tests without a canvas. {@link composeResultsImageLayout} picks the layout width for the chosen
 * size: 1280 px in fit mode, where the height follows the content; for a box, the box's width at its
 * text size, scaled down to no less than {@link CC_RESULTS_IMAGE_MIN_SCALE} when the content is taller.
 */

import { bitmapRefusal } from '../../model-comparison/figure-export';
import { FIT_RESOLUTION_ID, FigureSizeSettings, resolveSizeDensity, resolveSizeResolution, sizeErrors } from '../../model-comparison/figure-size';
import { canvasLineHeight as lineHeight } from '../../run-report-frame/canvas-drawing';
import {
  FactLayout,
  FactLayoutSizes,
  ImageFactRow,
  TextMeasurer,
  TextWrapper,
  estimateTextWidth,
  layoutFactRows
} from '../../run-report-frame/key-figures-image';
import { CcIntervalGeometry } from '../chat-consistency-results';
import {
  CC_IMAGE_CARD_KINDS,
  CcImageAttributionGroup,
  CcImageEndpointCard,
  CcImageFact,
  CcImageIcon,
  CcImagePeriodCard,
  CcImageRunGroupCard,
  CcImageTone,
  CcResultsImageBlock
} from './results-image-blocks';

/** A text color, by role or by tone; the palette resolves it. */
export type CcImageInk = 'title' | 'heading' | 'body' | 'muted' | 'warning' | CcImageTone;

/** A box's fill and border, by surface or by tone; the palette resolves it. */
export type CcImageSurface = 'card' | 'inset' | 'warning' | 'band' | CcImageTone;

/** One drawing operation, in layout pixels from the image's top left corner. */
export type CcImageOp =
  | {
    readonly kind: 'text';
    readonly x: number;
    readonly y: number;
    readonly lines: readonly string[];
    readonly size: number;
    readonly weight: string;
    readonly ink: CcImageInk;
  }
  | {
    readonly kind: 'box';
    readonly x: number;
    readonly y: number;
    readonly width: number;
    readonly height: number;
    readonly radius: number;
    readonly surface: CcImageSurface;
    /** No border, only the fill. */
    readonly borderless?: boolean;
    readonly dashed?: boolean;
    /** A colored bar down the inline start. */
    readonly rail?: { readonly width: number; readonly ink: CcImageInk };
  }
  | {
    readonly kind: 'pill';
    readonly x: number;
    readonly y: number;
    readonly width: number;
    readonly height: number;
    readonly text: string;
    readonly size: number;
    readonly weight: string;
    readonly tone: CcImageTone;
    readonly icon?: CcImageIcon;
    readonly dashed?: boolean;
  }
  | { readonly kind: 'icon'; readonly x: number; readonly y: number; readonly size: number; readonly icon: CcImageIcon; readonly ink: CcImageInk }
  | { readonly kind: 'interval'; readonly x: number; readonly y: number; readonly width: number; readonly height: number; readonly geometry: CcIntervalGeometry }
  | { readonly kind: 'rule'; readonly x: number; readonly y: number; readonly width: number }
  | { readonly kind: 'factRows'; readonly x: number; readonly y: number; readonly layout: FactLayout }
  | { readonly kind: 'logo'; readonly x: number; readonly y: number; readonly height: number };

/** What the image carries around its blocks; an empty or null part is left out. */
export interface CcResultsImageLayoutInput {
  readonly blocks: readonly CcResultsImageBlock[];
  /** The title, with the wide logo above it when `logo`; null leaves the header out. */
  readonly header: { readonly title: string; readonly logo: boolean } | null;
  readonly modelRows: readonly ImageFactRow[];
  readonly analysisLine: string;
  readonly footer: string;
}

export interface CcResultsImageLayout {
  readonly width: number;
  /** At least the natural height; a box's extra height goes above the footer. */
  readonly height: number;
  readonly naturalHeight: number;
  /** Cards per row. */
  readonly columns: number;
  readonly ops: readonly CcImageOp[];
}

/** The layout width in fit mode. */
export const CC_RESULTS_IMAGE_FIT_WIDTH = 1280;

/** From this layout width, cards go two to a row. */
export const CC_RESULTS_IMAGE_TWO_COLUMN_WIDTH = 1100;

/** The smallest scale a box's content is drawn at before the size is refused. */
export const CC_RESULTS_IMAGE_MIN_SCALE = 0.7;

/** The scales a box tries, largest first. */
const BOX_SCALES: readonly number[] = [1, 0.95, 0.9, 0.85, 0.8, 0.75, CC_RESULTS_IMAGE_MIN_SCALE];

export const CC_RESULTS_IMAGE_PAD = 40;
export const CC_RESULTS_IMAGE_LOGO_HEIGHT = 36;
const LOGO_GAP = 12;
const TITLE_PX = 22;
const HEADER_LINE_GAP = 6;
const HEADER_RULE_GAP = 24;
const BLOCK_GAP = 14;
const BODY_PX = 15;
const SMALL_PX = 13;
const XS_PX = 12;
const HEADING_PX = 17;
const OUTCOME_PX = 24;
const CARD_PAD = 16;
const CARD_GAP = 14;
const CARD_RADIUS = 10;
const RAIL_WIDTH = 4;
export const CC_IMAGE_PILL_PAD_X = 9;
export const CC_IMAGE_PILL_PAD_Y = 3;
const PILL_GAP = 8;
export const CC_IMAGE_ICON_GAP = 6;
const FIGURE_MIN_WIDTH = 200;
const INTERVAL_HEIGHT = 24;
const FOOTER_RULE_GAP = 16;
const FACT_TERM_GUTTER = 16;
const FACT_TERM_SHARE = 0.32;
const HEADER_FACT_SIZES: FactLayoutSizes = { labelPx: XS_PX, textPx: 14 };
const BLOCK_FACT_SIZES: FactLayoutSizes = { labelPx: XS_PX, textPx: BODY_PX };

/** A greedy word wrap measured with {@link estimateTextWidth}, for layouts without a canvas. */
export const estimateTextWrapper: TextWrapper = (text, maxWidth, sizePx, weight) => {
  const words = (text ?? '').trim().split(/\s+/).filter(word => word !== '');
  const lines: string[] = [];
  let current = '';
  for (const word of words) {
    const candidate = current === '' ? word : `${current} ${word}`;
    if (current !== '' && estimateTextWidth(candidate, sizePx, weight) > maxWidth) {
      lines.push(current);
      current = word;
    } else {
      current = candidate;
    }
  }
  if (current !== '') lines.push(current);
  return lines;
};

/** A block laid out at the origin: its operations and its height. */
interface Placed {
  ops: CcImageOp[];
  height: number;
}

/** A card's content at the origin, and the box it is drawn in. */
interface PlacedCard extends Placed {
  surface: CcImageSurface;
  dashed?: boolean;
  rail?: { width: number; ink: CcImageInk };
}

/** `ops` moved by `dx`, `dy`. */
function shift(ops: readonly CcImageOp[], dx: number, dy: number): CcImageOp[] {
  return ops.map(op => ({ ...op, x: op.x + dx, y: op.y + dy }));
}

/** Lays text, wraps, and the shared pieces out. */
class Composer {
  constructor(private readonly wrap: TextWrapper, private readonly measure: TextMeasurer) {}

  /** Wrapped text at `x`, `y`; no operation and height 0 for empty text. */
  text(text: string, x: number, y: number, width: number, size: number, weight: string, ink: CcImageInk): Placed {
    const lines = this.wrap(text, Math.max(1, width), size, weight);
    if (lines.length === 0) return { ops: [], height: 0 };
    return { ops: [{ kind: 'text', x, y, lines, size, weight, ink }], height: lines.length * lineHeight(size) };
  }

  /** An icon before wrapped text, the icon centered on the first line. */
  iconText(icon: CcImageIcon, iconInk: CcImageInk, text: string, x: number, y: number, width: number, size: number, weight: string, ink: CcImageInk): Placed {
    const iconSize = Math.round(size * 1.05);
    const textX = x + iconSize + CC_IMAGE_ICON_GAP;
    const body = this.text(text, textX, y, width - iconSize - CC_IMAGE_ICON_GAP, size, weight, ink);
    const iconY = y + (lineHeight(size) - iconSize) / 2;
    return { ops: [{ kind: 'icon', x, y: iconY, size: iconSize, icon, ink: iconInk }, ...body.ops], height: Math.max(body.height, lineHeight(size)) };
  }

  pillWidth(text: string, size: number, weight: string, icon?: CcImageIcon): number {
    return Math.ceil(this.measure(text, size, weight)) + CC_IMAGE_PILL_PAD_X * 2 + (icon ? size + CC_IMAGE_ICON_GAP - 2 : 0);
  }

  pillHeight(size: number): number {
    return lineHeight(size) + CC_IMAGE_PILL_PAD_Y * 2;
  }

  /** Pills flowing left to right and wrapping, each `gap` apart. */
  pills(
    items: readonly { text: string; tone: CcImageTone; icon?: CcImageIcon; dashed?: boolean; weight?: string }[],
    x: number,
    y: number,
    width: number,
    size: number
  ): Placed {
    if (items.length === 0) return { ops: [], height: 0 };
    const height = this.pillHeight(size);
    const ops: CcImageOp[] = [];
    let cx = 0;
    let cy = 0;
    for (const item of items) {
      const weight = item.weight ?? '600';
      const itemWidth = Math.min(width, this.pillWidth(item.text, size, weight, item.icon));
      if (cx > 0 && cx + itemWidth > width) {
        cx = 0;
        cy += height + PILL_GAP / 2;
      }
      ops.push({
        kind: 'pill', x: x + cx, y: y + cy, width: itemWidth, height, text: item.text, size, weight,
        tone: item.tone, icon: item.icon, dashed: item.dashed
      });
      cx += itemWidth + PILL_GAP;
    }
    return { ops, height: cy + height };
  }

  /** A term column and a value column; the terms go above their values where the value column would be narrow. */
  facts(facts: readonly CcImageFact[], x: number, y: number, width: number, termPx: number, valuePx: number): Placed {
    if (facts.length === 0) return { ops: [], height: 0 };
    const termWidth = Math.max(...facts.map(fact => this.measure(fact.term, termPx, '600')));
    const stacked = termWidth > width * FACT_TERM_SHARE;
    const valueX = stacked ? 0 : Math.ceil(termWidth) + FACT_TERM_GUTTER;
    const ops: CcImageOp[] = [];
    let cy = 0;
    facts.forEach((fact, index) => {
      if (index > 0) cy += 4;
      // The term is centered on the value's first line.
      const termOffset = stacked ? 0 : (lineHeight(valuePx) - lineHeight(termPx)) / 2;
      const term = this.text(fact.term, x, y + cy + termOffset, stacked ? width : termWidth + 1, termPx, '600', 'muted');
      ops.push(...term.ops);
      const valueTop = stacked ? cy + term.height : cy;
      const value = this.text(fact.value, x + valueX, y + valueTop, width - valueX, valuePx, '400', 'body');
      ops.push(...value.ops);
      cy = stacked ? valueTop + value.height : cy + Math.max(term.height, value.height, lineHeight(valuePx));
    });
    return { ops, height: cy };
  }

  /** Items with a bullet each, the text hanging after it. */
  bullets(items: readonly string[], x: number, y: number, width: number, size: number, ink: CcImageInk): Placed {
    const ops: CcImageOp[] = [];
    let cy = 0;
    items.forEach((item, index) => {
      if (index > 0) cy += 4;
      ops.push({ kind: 'text', x, y: y + cy, lines: ['•'], size, weight: '700', ink: 'muted' });
      const body = this.text(item, x + size, y + cy, width - size, size, '400', ink);
      ops.push(...body.ops);
      cy += Math.max(body.height, lineHeight(size));
    });
    return { ops, height: cy };
  }
}

/** Stacks laid-out parts top to bottom with `gap` between each two non-empty ones. */
function stack(parts: readonly ((y: number) => Placed)[], gap: number): Placed {
  const ops: CcImageOp[] = [];
  let y = 0;
  let any = false;
  for (const part of parts) {
    const placed = part(any ? y + gap : y);
    if (placed.height <= 0 && placed.ops.length === 0) continue;
    if (any) y += gap;
    ops.push(...placed.ops);
    y += placed.height;
    any = true;
  }
  return { ops, height: y };
}

const TONE_OF_GRADE: Readonly<Record<string, CcImageTone>> = {
  established: 'high',
  indicated: 'mid'
};

const STATUS_TONE: Readonly<Record<string, CcImageTone>> = {
  within: 'high',
  improved: 'high',
  changed: 'mid',
  inconclusive: 'neutral',
  notComputable: 'neutral'
};

const STATUS_ICON: Readonly<Record<string, CcImageIcon>> = {
  within: 'check',
  improved: 'check',
  changed: 'alert',
  inconclusive: 'minus',
  notComputable: 'minus'
};

function periodTone(period: string): CcImageTone {
  return period === 'baseline' ? 'baseline' : period === 'comparison' ? 'comparison' : 'neutral';
}

/** The blocks laid out one by one; consecutive cards in a grid. */
class BlockLayout {
  private readonly c: Composer;

  constructor(wrap: TextWrapper, private readonly measure: TextMeasurer) {
    this.c = new Composer(wrap, measure);
  }

  block(block: CcResultsImageBlock, width: number): Placed {
    const c = this.c;
    switch (block.kind) {
      case 'outcome':
        return this.outcome(block, width);
      case 'heading':
        return c.text(block.text, 0, 0, width, HEADING_PX, '700', 'heading');
      case 'paragraph':
        switch (block.tone) {
          case 'muted':
            return c.text(block.text, 0, 0, width, SMALL_PX, '400', 'muted');
          case 'strong':
            return c.text(block.text, 0, 0, width, BODY_PX, '600', 'heading');
          case 'high':
            return c.iconText('check', 'high', block.text, 0, 0, width, BODY_PX, '600', 'high');
          default:
            return c.text(block.text, 0, 0, width, BODY_PX, '400', 'body');
        }
      case 'facts':
        return c.facts(block.facts, 0, 0, width, SMALL_PX, BODY_PX);
      case 'badgeRows': {
        const layout = layoutFactRows(block.rows, width, this.measure, BLOCK_FACT_SIZES);
        return layout.height > 0 ? { ops: [{ kind: 'factRows', x: 0, y: 0, layout }], height: layout.height } : { ops: [], height: 0 };
      }
      case 'chips':
        return stack([
          y => c.text(block.label, 0, y, width, SMALL_PX, '600', 'muted'),
          y => c.pills(block.chips, 0, y, width, SMALL_PX)
        ], 6);
      case 'figures':
        return this.figures(block.figures, width);
      case 'notice':
        return this.notice(block.title, block.items, width);
      case 'bullets':
        return stack([
          y => c.text(block.title, 0, y, width, SMALL_PX, '600', 'heading'),
          y => c.bullets(block.items, 0, y, width, 14, 'body')
        ], 6);
      case 'uncomputed':
        return this.uncomputed(block.title, block.rows, width);
      default:
        // The card kinds are placed by `grid`.
        return { ops: [], height: 0 };
    }
  }

  card(block: CcResultsImageBlock, width: number): PlacedCard {
    switch (block.kind) {
      case 'endpoint':
        return this.endpoint(block.card, width);
      case 'period':
        return this.period(block.card, width);
      case 'attribution':
        return this.attribution(block.group, width);
      case 'runGroup':
        return this.runGroup(block.card, width);
      default:
        return { ...this.block(block, width), surface: 'card' };
    }
  }

  /** The cards in `columns` per row, each row as tall as its tallest card. */
  grid(cards: readonly CcResultsImageBlock[], width: number, columns: number): Placed {
    const cardWidth = (width - (columns - 1) * CARD_GAP) / columns;
    const inner = cardWidth - CARD_PAD * 2;
    const ops: CcImageOp[] = [];
    let y = 0;
    for (let start = 0; start < cards.length; start += columns) {
      const row = cards.slice(start, start + columns).map(block => this.card(block, inner));
      const height = Math.max(...row.map(card => card.height)) + CARD_PAD * 2;
      row.forEach((card, index) => {
        const x = index * (cardWidth + CARD_GAP);
        const railWidth = card.rail?.width ?? 0;
        ops.push({ kind: 'box', x, y, width: cardWidth, height, radius: CARD_RADIUS, surface: card.surface, dashed: card.dashed, rail: card.rail });
        ops.push(...shift(card.ops, x + CARD_PAD + railWidth, y + CARD_PAD));
      });
      y += height + CARD_GAP;
    }
    return { ops, height: Math.max(0, y - CARD_GAP) };
  }

  private outcome(block: Extract<CcResultsImageBlock, { kind: 'outcome' }>, width: number): Placed {
    const c = this.c;
    const inner = width - CARD_PAD * 2 - RAIL_WIDTH;
    const content = stack([
      y => c.text(block.eyebrow.toUpperCase(), 0, y, inner, XS_PX, '700', 'muted'),
      y => c.iconText(block.icon, block.tone, block.title, 0, y, inner, OUTCOME_PX, '700', 'heading'),
      y => c.text(block.detail, 0, y, inner, BODY_PX, '400', 'body')
    ], 6);
    const height = content.height + CARD_PAD * 2;
    return {
      ops: [
        { kind: 'box', x: 0, y: 0, width, height, radius: CARD_RADIUS, surface: 'card', rail: { width: RAIL_WIDTH, ink: block.tone } },
        ...shift(content.ops, CARD_PAD + RAIL_WIDTH, CARD_PAD)
      ],
      height
    };
  }

  private figures(figures: Extract<CcResultsImageBlock, { kind: 'figures' }>['figures'], width: number): Placed {
    const c = this.c;
    const columns = Math.max(1, Math.min(figures.length, Math.floor((width + CARD_GAP) / (FIGURE_MIN_WIDTH + CARD_GAP))));
    const cardWidth = (width - (columns - 1) * CARD_GAP) / columns;
    const inner = cardWidth - CARD_PAD * 2;
    const ops: CcImageOp[] = [];
    let y = 0;
    for (let start = 0; start < figures.length; start += columns) {
      const row = figures.slice(start, start + columns).map(figure => stack([
        top => c.text(figure.label.toUpperCase(), 0, top, inner, XS_PX, '600', 'muted'),
        top => c.text(figure.value, 0, top, inner, OUTCOME_PX, '800', 'heading'),
        top => c.text(figure.sub ?? '', 0, top, inner, SMALL_PX, '600', 'body'),
        top => c.text(figure.note ?? '', 0, top, inner, XS_PX, '400', 'muted')
      ], 4));
      const height = Math.max(...row.map(card => card.height)) + CARD_PAD * 2;
      row.forEach((card, index) => {
        const x = index * (cardWidth + CARD_GAP);
        ops.push({ kind: 'box', x, y, width: cardWidth, height, radius: 8, surface: 'card' });
        ops.push(...shift(card.ops, x + CARD_PAD, y + CARD_PAD));
      });
      y += height + CARD_GAP;
    }
    return { ops, height: Math.max(0, y - CARD_GAP) };
  }

  private notice(title: string, items: readonly string[], width: number): Placed {
    const c = this.c;
    const inner = width - CARD_PAD * 2;
    const content = stack([
      y => c.iconText('alert', 'warning', title, 0, y, inner, BODY_PX, '700', 'warning'),
      y => c.bullets(items, 0, y, inner, SMALL_PX, 'body')
    ], 6);
    const height = content.height + CARD_PAD * 2;
    return {
      ops: [{ kind: 'box', x: 0, y: 0, width, height, radius: 8, surface: 'warning' }, ...shift(content.ops, CARD_PAD, CARD_PAD)],
      height
    };
  }

  private uncomputed(title: string, rows: readonly CcImageFact[], width: number): Placed {
    const c = this.c;
    const inner = width - CARD_PAD * 2;
    const parts: ((y: number) => Placed)[] = [y => c.iconText('minus', 'muted', title, 0, y, inner, BODY_PX, '700', 'heading')];
    rows.forEach((row, index) => {
      parts.push(y => {
        const rule: CcImageOp[] = index > 0 ? [{ kind: 'rule', x: 0, y, width: inner }] : [];
        const top = y + (index > 0 ? 8 : 0);
        const body = stack([
          t => c.text(row.term, 0, top + t, inner, BODY_PX, '600', 'heading'),
          t => c.text(row.value, 0, top + t, inner, SMALL_PX, '400', 'muted')
        ], 2);
        return { ops: [...rule, ...body.ops], height: body.height + (index > 0 ? 8 : 0) };
      });
    });
    const content = stack(parts, 8);
    const height = content.height + CARD_PAD * 2;
    return {
      ops: [{ kind: 'box', x: 0, y: 0, width, height, radius: 8, surface: 'neutral', dashed: true }, ...shift(content.ops, CARD_PAD, CARD_PAD)],
      height
    };
  }

  private endpoint(card: CcImageEndpointCard, width: number): PlacedCard {
    const c = this.c;
    const idWidth = c.pillWidth(card.id, SMALL_PX, '700');
    const parts: ((y: number) => Placed)[] = [
      y => {
        const id = c.pills([{ text: card.id, tone: 'accent', weight: '700' }], 0, y, width, SMALL_PX);
        const nameX = idWidth + PILL_GAP;
        const name = c.text(card.name, nameX, y + (id.height - lineHeight(BODY_PX)) / 2, width - nameX, BODY_PX, '700', 'heading');
        return { ops: [...id.ops, ...name.ops], height: Math.max(id.height, name.height + (id.height - lineHeight(BODY_PX)) / 2) };
      },
      y => c.text(card.margin, 0, y, width, SMALL_PX, '400', 'muted'),
      y => {
        const pills: { text: string; tone: CcImageTone; icon?: CcImageIcon; dashed?: boolean }[] = [{
          text: card.statusText,
          tone: STATUS_TONE[card.status] ?? 'neutral',
          icon: STATUS_ICON[card.status],
          dashed: card.status === 'notComputable'
        }];
        if (card.grade) {
          pills.push({ text: card.grade, tone: TONE_OF_GRADE[card.grade.toLowerCase()] ?? 'neutral' });
        }
        return c.pills(pills, 0, y, width, SMALL_PX);
      },
      y => c.text(card.estimate, 0, y, width, 22, '700', 'heading'),
      y => c.text(card.interval ?? '', 0, y, width, SMALL_PX, '400', 'muted')
    ];
    const geometry = card.geometry;
    if (geometry) {
      parts.push(y => {
        const ops: CcImageOp[] = [{ kind: 'interval', x: 0, y, width, height: INTERVAL_HEIGHT, geometry }];
        let height = INTERVAL_HEIGHT;
        if (card.ends.start || card.ends.end) {
          const top = y + INTERVAL_HEIGHT + 4;
          if (card.ends.start) ops.push({ kind: 'text', x: 0, y: top, lines: [card.ends.start], size: XS_PX, weight: '400', ink: 'muted' });
          if (card.ends.end) {
            const endWidth = this.measure(card.ends.end, XS_PX, '400');
            ops.push({ kind: 'text', x: width - endWidth, y: top, lines: [card.ends.end], size: XS_PX, weight: '400', ink: 'muted' });
          }
          height += 4 + lineHeight(XS_PX);
        }
        return { ops, height };
      });
    }
    parts.push(y => c.facts(card.facts, 0, y, width, XS_PX, 14));
    for (const note of card.notes) {
      parts.push(y => note.shortfall
        ? c.iconText('alert', 'mid', note.text, 0, y, width, SMALL_PX, '400', 'body')
        : c.iconText('info', 'muted', note.text, 0, y, width, SMALL_PX, '400', 'body'));
    }
    if (card.more.length > 0) {
      parts.push(y => stack([
        t => c.text(`More about ${card.id}`, 0, y + t, width, XS_PX, '700', 'muted'),
        t => c.bullets(card.more, 0, y + t, width, SMALL_PX, 'body')
      ], 4));
    }
    const content = stack(parts, 8);
    return { ...content, surface: 'card', dashed: card.status === 'notComputable' };
  }

  private period(card: CcImagePeriodCard, width: number): PlacedCard {
    const c = this.c;
    const inner = width - RAIL_WIDTH;
    const content = stack([
      y => c.text(card.title, 0, y, inner, BODY_PX, '700', card.period),
      y => c.text(card.dates, 0, y, inner, 14, '600', 'heading'),
      y => c.text(card.range, 0, y, inner, SMALL_PX, '400', 'muted'),
      y => c.facts(card.facts, 0, y, inner, XS_PX, 14)
    ], 6);
    return { ...content, surface: 'card', rail: { width: RAIL_WIDTH, ink: card.period } };
  }

  private attribution(group: CcImageAttributionGroup, width: number): PlacedCard {
    const c = this.c;
    const parts: ((y: number) => Placed)[] = [y => c.text(group.title, 0, y, width, BODY_PX, '700', 'heading')];
    for (const item of group.cards) {
      parts.push(y => {
        const inner = width - 12 - 3;
        const body = stack([
          t => {
            const label = c.text(item.label, 0, t, inner, 14, '600', 'heading');
            const grade = c.pills([{ text: item.gradeText, tone: TONE_OF_GRADE[item.grade] ?? 'neutral', dashed: !TONE_OF_GRADE[item.grade] }], 0, t + label.height + 4, inner, XS_PX);
            return { ops: [...label.ops, ...grade.ops], height: label.height + 4 + grade.height };
          },
          t => c.pills(item.endpoints.map(id => ({ text: id, tone: 'accent' as const })), 0, t, inner, XS_PX),
          t => c.text(item.evidence, 0, t, inner, SMALL_PX, '400', 'body'),
          t => c.pills(item.eventRefs.map(ref => ({ text: ref, tone: 'comparison' as const, weight: '400' })), 0, t, inner, XS_PX)
        ], 6);
        const height = body.height + 16;
        return {
          ops: [
            { kind: 'box', x: 0, y, width, height, radius: 4, surface: 'inset', borderless: true, rail: { width: 3, ink: 'muted' } },
            ...shift(body.ops, 12 + 1.5, y + 8)
          ],
          height
        };
      });
    }
    return { ...stack(parts, 8), surface: 'card' };
  }

  private runGroup(card: CcImageRunGroupCard, width: number): PlacedCard {
    const c = this.c;
    const tone = periodTone(card.period);
    const railed = tone !== 'neutral';
    const inner = railed ? width - RAIL_WIDTH : width;
    const content = stack([
      y => c.pills([
        { text: card.periodText, tone },
        ...card.endpointIds.map(id => ({ text: id, tone: 'accent' as const }))
      ], 0, y, inner, XS_PX),
      y => c.text(card.title, 0, y, inner, BODY_PX, '700', 'heading'),
      y => c.facts(card.facts, 0, y, inner, XS_PX, 14),
      ...card.suggestions.map(text => (y: number) => c.text(text, 0, y, inner, 14, '400', 'body')),
      ...card.reasons.map(text => (y: number) => c.text(text, 0, y, inner, SMALL_PX, '400', 'muted')),
      y => c.text(card.repeat, 0, y, inner, SMALL_PX, '600', 'body'),
      y => c.text(card.hint, 0, y, inner, SMALL_PX, '400', 'muted')
    ], 6);
    return { ...content, surface: 'card', ...(railed ? { rail: { width: RAIL_WIDTH, ink: tone } } : {}) };
  }
}

/**
 * The image laid out at `width`: the header, the blocks with `BLOCK_GAP` between them, and the footer.
 * A `minHeight` above the natural height adds its space above the footer.
 */
export function layoutResultsImage(
  input: CcResultsImageLayoutInput,
  width: number,
  wrap: TextWrapper = estimateTextWrapper,
  measure: TextMeasurer = estimateTextWidth,
  minHeight = 0
): CcResultsImageLayout {
  const composer = new Composer(wrap, measure);
  const blocks = new BlockLayout(wrap, measure);
  const content = Math.max(1, width - CC_RESULTS_IMAGE_PAD * 2);
  const columns = width >= CC_RESULTS_IMAGE_TWO_COLUMN_WIDTH ? 2 : 1;
  const ops: CcImageOp[] = [];
  let y = CC_RESULTS_IMAGE_PAD;

  // Header.
  const header = stack([
    top => input.header?.logo
      ? { ops: [{ kind: 'logo', x: 0, y: top, height: CC_RESULTS_IMAGE_LOGO_HEIGHT }], height: CC_RESULTS_IMAGE_LOGO_HEIGHT + LOGO_GAP - HEADER_LINE_GAP }
      : { ops: [], height: 0 },
    top => input.header ? composer.text(input.header.title, 0, top, content, TITLE_PX, '700', 'title') : { ops: [], height: 0 },
    top => {
      const layout = layoutFactRows(input.modelRows, content, measure, HEADER_FACT_SIZES);
      return layout.height > 0 ? { ops: [{ kind: 'factRows', x: 0, y: top, layout }], height: layout.height } : { ops: [], height: 0 };
    },
    top => composer.text(input.analysisLine, 0, top, content, SMALL_PX, '400', 'muted')
  ], HEADER_LINE_GAP);
  if (header.height > 0) {
    ops.push(...shift(header.ops, CC_RESULTS_IMAGE_PAD, y));
    y += header.height + HEADER_RULE_GAP / 2;
    ops.push({ kind: 'rule', x: CC_RESULTS_IMAGE_PAD, y, width: content });
    y += HEADER_RULE_GAP / 2;
  }

  // Blocks; consecutive cards share a grid.
  let first = true;
  for (let index = 0; index < input.blocks.length;) {
    const block = input.blocks[index];
    let placed: Placed;
    if (CC_IMAGE_CARD_KINDS.has(block.kind)) {
      let end = index;
      while (end < input.blocks.length && input.blocks[end].kind === block.kind) end++;
      placed = blocks.grid(input.blocks.slice(index, end), content, columns);
      index = end;
    } else {
      placed = blocks.block(block, content);
      index++;
    }
    if (placed.height <= 0) continue;
    if (!first) y += BLOCK_GAP;
    ops.push(...shift(placed.ops, CC_RESULTS_IMAGE_PAD, y));
    y += placed.height;
    first = false;
  }

  // Footer, pinned to the bottom of a taller box.
  const footer = composer.text(input.footer, 0, 0, content, XS_PX, '400', 'muted');
  const footerHeight = footer.height > 0 ? FOOTER_RULE_GAP + footer.height : 0;
  const naturalHeight = y + footerHeight + CC_RESULTS_IMAGE_PAD;
  const height = Math.max(naturalHeight, minHeight);
  if (footer.height > 0) {
    const footerTop = height - CC_RESULTS_IMAGE_PAD - footerHeight;
    ops.push({ kind: 'rule', x: CC_RESULTS_IMAGE_PAD, y: footerTop + FOOTER_RULE_GAP / 2, width: content });
    ops.push(...shift(footer.ops, CC_RESULTS_IMAGE_PAD, footerTop + FOOTER_RULE_GAP));
  }
  return { width, height, naturalHeight, columns, ops };
}

/** Where a layout lands in the written bitmap: its pixel size and the scale from layout to device pixels. */
export interface CcResultsImageFrame {
  readonly pixelWidth: number;
  readonly pixelHeight: number;
  readonly scale: number;
}

/** The chosen layout and its frame, or why the size cannot be written. */
export type CcResultsImageComposition =
  | { readonly layout: CcResultsImageLayout; readonly frame: CcResultsImageFrame; readonly contentScale: number }
  | { readonly refusal: string };

/** `The Verdicts section does not fit 1920 × 1080 px. Choose Fit the content, a taller size, or include less.` */
export function ccResultsImageFitRefusal(sectionLabel: string, widthPx: number, heightPx: number): string {
  return `The ${sectionLabel} section does not fit ${widthPx} × ${heightPx} px. Choose Fit the content, a taller size, or include less.`;
}

/**
 * The layout for `size`, or its refusal. Fit: {@link CC_RESULTS_IMAGE_FIT_WIDTH} wide, as tall as the
 * content, refused past the bitmap limit. A box W × H at text size T: laid out W / T wide and H / T tall;
 * content taller than that is laid out wider by 1 / s, for the largest s of 1, 0.95 … 0.7 at which it
 * fits, and refused below {@link CC_RESULTS_IMAGE_MIN_SCALE}. The bitmap is W × H times the density.
 */
export function composeResultsImageLayout(
  input: CcResultsImageLayoutInput,
  size: FigureSizeSettings,
  sectionLabel: string,
  wrap: TextWrapper = estimateTextWrapper,
  measure: TextMeasurer = estimateTextWidth
): CcResultsImageComposition {
  const errors = sizeErrors(size, 'image');
  if (errors.any) {
    return { refusal: errors.any };
  }
  const density = resolveSizeDensity(size);
  if (size.resolutionId === FIT_RESOLUTION_ID) {
    const layout = layoutResultsImage(input, CC_RESULTS_IMAGE_FIT_WIDTH, wrap, measure);
    const refusal = bitmapRefusal(layout.width, layout.height, density);
    if (refusal) {
      return { refusal };
    }
    return {
      layout,
      frame: { pixelWidth: Math.round(layout.width * density), pixelHeight: Math.round(layout.height * density), scale: density },
      contentScale: 1
    };
  }
  const box = resolveSizeResolution(size);
  const textScale = size.textScalePercent / 100;
  const boxWidth = box.widthPx / textScale;
  const boxHeight = box.heightPx / textScale;
  for (const scale of BOX_SCALES) {
    const width = boxWidth / scale;
    const height = boxHeight / scale;
    const layout = layoutResultsImage(input, width, wrap, measure, height);
    if (layout.naturalHeight <= height + 0.5) {
      return {
        layout,
        frame: {
          pixelWidth: Math.round(box.widthPx * density),
          pixelHeight: Math.round(box.heightPx * density),
          scale: box.widthPx * density / width
        },
        contentScale: scale
      };
    }
  }
  return { refusal: ccResultsImageFitRefusal(sectionLabel, box.widthPx, box.heightPx) };
}
