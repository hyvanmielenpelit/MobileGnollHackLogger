/**
 * Loads a bundled figure font before anything is measured with it.
 *
 * A canvas does not wait for web fonts: text measured or drawn before the face arrives uses the
 * fallback, and a layout measured with one face and drawn with another disagrees with itself. Every
 * composition therefore awaits {@link ensureFigureFont} first.
 */

import type { FigureFontId } from './figure-style';
import { figureFont } from './figure-fonts';

/** The weights the Theme tab offers; each is requested so none is synthesised. */
export const FIGURE_FONT_LOAD_WEIGHTS = [400, 500, 600, 700] as const;

/** How long a load may take before the fallback stack is used instead. */
export const FIGURE_FONT_LOAD_TIMEOUT_MS = 5000;

/** The part of `FontFaceSet` the loader uses, so a spec can supply its own. */
export interface FigureFontSource {
  load(font: string): Promise<readonly unknown[]>;
}

export interface FigureFontLoadOptions {
  /** Absent reads `document.fonts`; null means the browser has none. */
  readonly fonts?: FigureFontSource | null;
  readonly timeoutMs?: number;
}

const cache = new Map<FigureFontId, Promise<boolean>>();

/**
 * Resolves true once every offered weight of the family is loaded, and false where the browser has
 * no font loading API, a weight has no face, or the load outlasts the timeout. Never rejects. The
 * result is cached per family; *Overseer default* resolves true at once.
 */
export function ensureFigureFont(id: FigureFontId, options: FigureFontLoadOptions = {}): Promise<boolean> {
  const family = figureFont(id).family;
  if (family === null) {
    return Promise.resolve(true);
  }
  const cached = cache.get(id);
  if (cached) {
    return cached;
  }
  const fonts = options.fonts === undefined ? documentFonts() : options.fonts;
  if (!fonts) {
    return Promise.resolve(false);
  }
  const timeoutMs = options.timeoutMs ?? FIGURE_FONT_LOAD_TIMEOUT_MS;
  const load = Promise.all(FIGURE_FONT_LOAD_WEIGHTS.map((weight) => fonts.load(`${weight} 16px "${family}"`)))
    .then((faces) => faces.every((list) => list.length > 0))
    .catch(() => false);
  let timer: ReturnType<typeof setTimeout> | undefined;
  const timeout = new Promise<boolean>((resolve) => {
    timer = setTimeout(() => resolve(false), timeoutMs);
  });
  const result = Promise.race([load, timeout]).then((loaded) => {
    clearTimeout(timer);
    return loaded;
  });
  cache.set(id, result);
  return result;
}

/** Forgets every cached result. For specs. */
export function resetFigureFontCache(): void {
  cache.clear();
}

function documentFonts(): FigureFontSource | null {
  const doc = globalThis.document as (Document & { fonts?: FigureFontSource }) | undefined;
  return doc?.fonts && typeof doc.fonts.load === 'function' ? doc.fonts : null;
}
