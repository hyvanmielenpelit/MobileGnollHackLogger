/**
 * The Results images' two color schemes: *Dark, as on screen*, from the tokens of `styles.scss` and the
 * key-figures images' badge colors, and *Light, for print*, the same roles on white with inks dark
 * enough for text contrast on paper. No Angular.
 *
 * The dark period colors are the values of `--cc-period-baseline` and `--cc-period-comparison`, the
 * score tiers those of `--score-high-*`, `--score-mid-*` and `--score-low-*`; change both together.
 */

import { BADGE_PALETTE } from '../../run-report-frame/key-figures-image';
import type { ImageBadgeTone } from '../../run-report-frame/key-figures-image';
import type { CcImageTone } from './results-image-blocks';
import type { CcResultsImageScheme } from './results-image-settings';

/** A colored element's ink, fill and border. */
export interface CcImageSwatch {
  readonly ink: string;
  readonly fill: string;
  readonly border: string;
}

export interface CcResultsImagePalette {
  readonly scheme: CcResultsImageScheme;
  readonly background: string;
  /** The image title and the outcome's eyebrow. */
  readonly title: string;
  /** Headings, card titles and strong text. */
  readonly heading: string;
  readonly body: string;
  readonly muted: string;
  readonly rule: string;
  readonly cardFill: string;
  readonly cardBorder: string;
  /** The endpoint id tags and the interval bar's line and dot. */
  readonly accent: string;
  readonly tones: Readonly<Record<CcImageTone, CcImageSwatch>>;
  readonly badges: Readonly<Record<ImageBadgeTone, CcImageSwatch>>;
  /** The reliability notice. */
  readonly warning: CcImageSwatch;
  /** The interval bar's margin band, its zero line, and the ring around the estimate dot. */
  readonly band: CcImageSwatch;
  readonly zero: string;
  readonly dotRing: string;
  /** The wash of a box inside a card, such as one attribution. */
  readonly inset: string;
}

const DARK_TONES: Readonly<Record<CcImageTone, CcImageSwatch>> = {
  high: { ink: '#81c784', fill: 'rgba(76, 175, 80, 0.15)', border: 'rgba(76, 175, 80, 0.45)' },
  mid: { ink: '#ffb74d', fill: 'rgba(255, 152, 0, 0.15)', border: 'rgba(255, 152, 0, 0.45)' },
  low: { ink: '#e57373', fill: 'rgba(244, 67, 54, 0.15)', border: 'rgba(244, 67, 54, 0.45)' },
  neutral: { ink: '#cccccc', fill: 'rgba(204, 204, 204, 0.04)', border: 'rgba(204, 204, 204, 0.4)' },
  accent: { ink: '#e0ba6d', fill: 'rgba(224, 186, 109, 0.06)', border: '#e0ba6d' },
  baseline: { ink: '#7fb2e5', fill: 'rgba(127, 178, 229, 0.08)', border: '#7fb2e5' },
  comparison: { ink: '#e0ba6d', fill: 'rgba(224, 186, 109, 0.08)', border: '#e0ba6d' }
};

const LIGHT_TONES: Readonly<Record<CcImageTone, CcImageSwatch>> = {
  high: { ink: '#1b5e20', fill: 'rgba(46, 125, 50, 0.1)', border: 'rgba(46, 125, 50, 0.55)' },
  mid: { ink: '#8a4b00', fill: 'rgba(230, 126, 0, 0.12)', border: 'rgba(200, 110, 0, 0.6)' },
  low: { ink: '#b3261e', fill: 'rgba(211, 47, 47, 0.1)', border: 'rgba(211, 47, 47, 0.55)' },
  neutral: { ink: '#3f3f46', fill: 'rgba(63, 63, 70, 0.04)', border: 'rgba(63, 63, 70, 0.45)' },
  accent: { ink: '#7a5a12', fill: 'rgba(176, 132, 32, 0.08)', border: '#b08420' },
  baseline: { ink: '#1f5f99', fill: 'rgba(31, 95, 153, 0.08)', border: '#3b7cc0' },
  comparison: { ink: '#7a5a12', fill: 'rgba(176, 132, 32, 0.1)', border: '#b08420' }
};

const LIGHT_BADGES: Readonly<Record<ImageBadgeTone, CcImageSwatch>> = {
  thinking: { ink: '#075985', fill: 'rgba(14, 116, 144, 0.08)', border: 'rgba(14, 116, 144, 0.45)' },
  reasoning: { ink: '#6b21a8', fill: 'rgba(126, 34, 206, 0.07)', border: 'rgba(126, 34, 206, 0.4)' },
  openai: { ink: '#0b6e55', fill: 'rgba(16, 163, 127, 0.1)', border: 'rgba(16, 163, 127, 0.5)' },
  anthropic: { ink: '#9a3f22', fill: 'rgba(217, 119, 87, 0.12)', border: 'rgba(217, 119, 87, 0.55)' },
  google: { ink: '#1a56c4', fill: 'rgba(66, 133, 244, 0.1)', border: 'rgba(66, 133, 244, 0.5)' },
  provider: { ink: '#3f3f46', fill: '#f4f4f5', border: '#a1a1aa' },
  config: { ink: '#52525b', fill: '#f4f4f5', border: '#d4d4d8' },
  role: { ink: '#7a5a12', fill: 'transparent', border: 'rgba(176, 132, 32, 0.5)' },
  success: LIGHT_TONES.high,
  warning: LIGHT_TONES.mid,
  danger: LIGHT_TONES.low,
  info: { ink: '#1f5f99', fill: 'rgba(33, 150, 243, 0.1)', border: 'rgba(33, 150, 243, 0.5)' },
  running: LIGHT_TONES.accent
};

/** On screen: the dialog surfaces, `--font-color`, `--nav-color` and `--primary-color`. */
export const CC_RESULTS_IMAGE_DARK: CcResultsImagePalette = {
  scheme: 'dark',
  background: '#181818',
  title: '#e0ba6d',
  heading: '#f4f4f5',
  body: '#d4d4d8',
  muted: '#a1a1aa',
  rule: '#2f2f2f',
  cardFill: '#1f1f1f',
  cardBorder: '#363636',
  accent: '#e0ba6d',
  tones: DARK_TONES,
  badges: BADGE_PALETTE,
  warning: { ink: '#fcd34d', fill: 'rgba(255, 152, 0, 0.1)', border: 'rgba(255, 152, 0, 0.45)' },
  band: { ink: '#cccccc', fill: 'rgba(204, 204, 204, 0.12)', border: 'rgba(204, 204, 204, 0.45)' },
  zero: 'rgba(204, 204, 204, 0.7)',
  dotRing: '#1f1f1f',
  inset: 'rgba(255, 255, 255, 0.03)'
};

/** For print: white, near-black text, and the tones darkened to read on paper. */
export const CC_RESULTS_IMAGE_LIGHT: CcResultsImagePalette = {
  scheme: 'light',
  background: '#ffffff',
  title: '#7a5a12',
  heading: '#18181b',
  body: '#27272a',
  muted: '#52525b',
  rule: '#d4d4d8',
  cardFill: '#fafafa',
  cardBorder: '#d4d4d8',
  accent: '#9a7419',
  tones: LIGHT_TONES,
  badges: LIGHT_BADGES,
  warning: { ink: '#8a4b00', fill: 'rgba(255, 152, 0, 0.1)', border: 'rgba(200, 110, 0, 0.55)' },
  band: { ink: '#3f3f46', fill: 'rgba(63, 63, 70, 0.08)', border: 'rgba(63, 63, 70, 0.4)' },
  zero: 'rgba(63, 63, 70, 0.7)',
  dotRing: '#fafafa',
  inset: 'rgba(0, 0, 0, 0.03)'
};

export function ccResultsImagePalette(scheme: CcResultsImageScheme): CcResultsImagePalette {
  return scheme === 'light' ? CC_RESULTS_IMAGE_LIGHT : CC_RESULTS_IMAGE_DARK;
}
