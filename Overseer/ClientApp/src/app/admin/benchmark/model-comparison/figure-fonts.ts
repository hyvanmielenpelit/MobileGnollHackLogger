/**
 * The font families a comparison chart and the table image can be drawn in.
 *
 * The six bundled families are self-hosted by `src/figure-fonts.scss`; *Overseer default* keeps the
 * stacks the figures were always drawn with. Pure TypeScript with no Angular or DOM dependency.
 */

import type { FigureFontId } from './figure-style';

/** The composed chrome's and the table image's stack under *Overseer default*. */
export const OVERSEER_DEFAULT_FONT_STACK = '"Segoe UI", "Helvetica Neue", Arial, sans-serif';

/** What a bundled family falls back to while its face loads, or where it never does. */
const BUNDLED_FALLBACK = '"Segoe UI", "Helvetica Neue", Arial, sans-serif';

export interface FigureFont {
  readonly id: FigureFontId;
  readonly label: string;
  /** The `@font-face` family name; null for *Overseer default*, which loads nothing. */
  readonly family: string | null;
  /** The chrome and table stack. */
  readonly stack: string;
}

function bundled(id: FigureFontId, family: string): FigureFont {
  return { id, label: family, family, stack: `"${family}", ${BUNDLED_FALLBACK}` };
}

/** In the order the font select offers them. */
export const FIGURE_FONTS: readonly FigureFont[] = [
  { id: 'default', label: 'Overseer default', family: null, stack: OVERSEER_DEFAULT_FONT_STACK },
  bundled('inter', 'Inter'),
  bundled('roboto', 'Roboto'),
  bundled('geist', 'Geist'),
  bundled('ibm-plex-sans', 'IBM Plex Sans'),
  bundled('source-sans-3', 'Source Sans 3'),
  bundled('open-sans', 'Open Sans'),
];

/** The catalogue entry for an id; *Overseer default* for anything unknown. */
export function figureFont(id: FigureFontId): FigureFont {
  return FIGURE_FONTS.find((font) => font.id === id) ?? FIGURE_FONTS[0];
}
