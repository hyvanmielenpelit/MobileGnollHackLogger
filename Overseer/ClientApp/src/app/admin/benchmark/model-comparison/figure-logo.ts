/**
 * The GnollBench logo in the top right corner of every chart and the table image.
 *
 * Both composers take the logo from here: the asset per variant, its decoded image, the box it is
 * drawn in and the drawing itself. The logo is drawn in layout px through the context's density
 * transform from high-resolution assets, so it stays sharp at high pixel densities.
 */

import type { FigureLogoVariant } from './figure-style';

/** One logo file and its intrinsic size, in image px. */
export interface FigureLogoAsset {
  readonly url: string;
  readonly widthPx: number;
  readonly heightPx: number;
}

export const FIGURE_LOGO_ASSETS: Readonly<Record<FigureLogoVariant, FigureLogoAsset>> = {
  wide: { url: '/img/gnollbench/gnollbench-wide-v3-h850.webp', widthPx: 3248, heightPx: 850 },
  square: { url: '/img/gnollbench/gnollbench-logo-v3-843.webp', widthPx: 843, heightPx: 843 },
};

/** What a composer draws: the decoded image, width over height, and height in layout px. */
export interface FigureLogo {
  readonly image: CanvasImageSource;
  readonly aspectRatio: number;
  readonly heightPx: number;
}

/** The drawn box, and the width the header text keeps beside it. */
export interface FigureLogoBox {
  readonly width: number;
  readonly height: number;
}

/** Between the header text and the logo, in layout px. */
export const FIGURE_LOGO_GAP = 16;

/** The widest the logo is drawn, as a share of the content width. */
export const FIGURE_LOGO_MAX_WIDTH_SHARE = 0.4;

/** How long a load may take before the figure is drawn without the logo. */
export const FIGURE_LOGO_LOAD_TIMEOUT_MS = 5000;

/** The IO the loader performs, behind a holder a spec replaces. */
export const figureLogoIo = {
  loadImage: (url: string): Promise<CanvasImageSource | null> => loadImageElement(url),
};

const cache = new Map<FigureLogoVariant, Promise<CanvasImageSource | null>>();

/**
 * The decoded logo of one variant, or null where it failed to load or outlasted the timeout. Never
 * rejects. A loaded image is cached per variant; a null result is not, so the next composition
 * tries again.
 */
export function ensureFigureLogo(variant: FigureLogoVariant): Promise<CanvasImageSource | null> {
  const cached = cache.get(variant);
  if (cached) {
    return cached;
  }
  let loading: Promise<CanvasImageSource | null>;
  try {
    loading = Promise.resolve(figureLogoIo.loadImage(FIGURE_LOGO_ASSETS[variant].url));
  } catch {
    loading = Promise.resolve(null);
  }
  const result: Promise<CanvasImageSource | null> = loading
    .catch(() => null)
    .then((image) => {
      if (!image && cache.get(variant) === result) {
        cache.delete(variant);
      }
      return image ?? null;
    });
  cache.set(variant, result);
  return result;
}

/** Width over height of one variant's asset. */
export function figureLogoAspect(variant: FigureLogoVariant): number {
  const asset = FIGURE_LOGO_ASSETS[variant];
  return asset.widthPx / asset.heightPx;
}

/**
 * The box the logo is drawn in: its height at the variant's proportions, scaled down with its
 * height to at most {@link FIGURE_LOGO_MAX_WIDTH_SHARE} of `contentWidth`. Null without a logo or
 * for a non-positive height or aspect.
 */
export function figureLogoBox(logo: FigureLogo | null | undefined, contentWidth: number): FigureLogoBox | null {
  if (!logo || !(logo.heightPx > 0) || !(logo.aspectRatio > 0)) {
    return null;
  }
  const width = logo.heightPx * logo.aspectRatio;
  const maxWidth = Math.max(0, contentWidth * FIGURE_LOGO_MAX_WIDTH_SHARE);
  if (width <= maxWidth) {
    return { width, height: logo.heightPx };
  }
  return { width: maxWidth, height: maxWidth / logo.aspectRatio };
}

/** Draws the logo into `box` at (`x`, `y`), in the context's own units, with high-quality smoothing. */
export function drawFigureLogo(
  context: CanvasRenderingContext2D,
  logo: FigureLogo,
  box: FigureLogoBox,
  x: number,
  y: number
): void {
  context.save();
  context.imageSmoothingEnabled = true;
  context.imageSmoothingQuality = 'high';
  context.drawImage(logo.image, x, y, box.width, box.height);
  context.restore();
}

/** Forgets every cached image. For specs. */
export function resetFigureLogoCache(): void {
  cache.clear();
}

/** Same-origin, so the canvas it is drawn onto stays untainted and can still be encoded. */
function loadImageElement(url: string): Promise<CanvasImageSource | null> {
  const image = new Image();
  let timer: ReturnType<typeof setTimeout> | undefined;
  const timeout = new Promise<null>((resolve) => {
    timer = setTimeout(() => resolve(null), FIGURE_LOGO_LOAD_TIMEOUT_MS);
  });
  image.src = url;
  const decoded = image.decode().then(() => image as CanvasImageSource, () => null);
  return Promise.race([decoded, timeout]).then((result) => {
    clearTimeout(timer);
    return result;
  });
}
