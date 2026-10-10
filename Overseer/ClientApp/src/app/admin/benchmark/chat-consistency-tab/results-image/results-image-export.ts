/**
 * Copies or downloads one Results section as an image: builds the section's blocks from the stored
 * result, lays them out at the chosen size, draws them in the chosen colors, encodes the canvas and
 * hands it to the clipboard or to a download. The only module of the composer with side effects; its IO
 * sits behind {@link ccResultsImageIo}, which a spec replaces.
 */

import { safeFileName } from '../../../../utils/download.util';
import {
  FigureExportFormat,
  copyImageToClipboard,
  encodeFigureImage,
  exportTimestamp,
  saveFigureBlob
} from '../../model-comparison/figure-export';
import type { ClipboardImageOutcome } from '../../model-comparison/figure-export';
import { CanvasLogo } from '../../run-report-frame/canvas-drawing';
import {
  GNOLLBENCH_WIDE_LOGO_URL,
  TextMeasurer,
  TextWrapper,
  canvasTextMeasurer,
  canvasTextWrapper,
  estimateTextWidth,
  keyFiguresFooterText,
  keyFiguresStatusMessage,
  loadKeyFigureFonts
} from '../../run-report-frame/key-figures-image';
import { CcAnalysisResult } from '../chat-consistency.models';
import {
  CcResultsImageContext,
  ccImageModelRows,
  ccResultsImageAnalysisLine,
  ccResultsImageBlocks,
  ccResultsImageHasContent,
  ccResultsImageSectionLabel,
  ccResultsImageTitle
} from './results-image-blocks';
import { paintResultsImage } from './results-image-draw';
import {
  CcResultsImageComposition,
  CcResultsImageLayoutInput,
  composeResultsImageLayout,
  estimateTextWrapper
} from './results-image-layout';
import { ccResultsImagePalette } from './results-image-palette';
import { CcResultsImageSection, CcResultsImageSettings } from './results-image-settings';

export type CcResultsImageAction = 'copy' | 'download';

/** A clipboard outcome, a download, a WebP the browser wrote as PNG, a section with nothing selected, or a failure. */
export type CcResultsImageOutcome = ClipboardImageOutcome | 'downloaded' | 'webp-fallback' | 'empty' | 'failed';

/** One section's image as the host asks for it. */
export interface CcResultsImageRequest {
  readonly section: CcResultsImageSection;
  readonly result: CcAnalysisResult;
  readonly context: CcResultsImageContext;
  /** The footer's Overseer build; null reads as `unknown`. */
  readonly overseerVersion: string | null;
  readonly settings: CcResultsImageSettings;
}

/** One encoded image and the name it is saved under. */
export interface CcResultsImage {
  readonly blob: Blob;
  readonly fileName: string;
  /** WebP was asked for and the browser wrote PNG; the name ends in `.png`. */
  readonly fellBackToPng: boolean;
}

/** Thrown before drawing when the chosen size cannot hold the section; the message is the refusal. */
export class CcResultsImageRefusal extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'CcResultsImageRefusal';
  }
}

export type CcResultsLoadedImage = HTMLImageElement | HTMLCanvasElement | ImageBitmap;

/** The IO the export performs, behind a holder a spec replaces. */
export const ccResultsImageIo = {
  loadImage: (url: string): Promise<CcResultsLoadedImage> => loadImageElement(url),
  copy: (blob: Blob): Promise<ClipboardImageOutcome> => copyImageToClipboard(blob),
  save: (blob: Blob, fileName: string): void => saveFigureBlob(blob, fileName),
  now: (): Date => new Date()
};

/** What the dialog shows of the next image: its pixel size, why it cannot be written, or nothing selected. */
export type CcResultsImageMeasure =
  | { readonly widthPx: number; readonly heightPx: number }
  | { readonly refusal: string }
  | { readonly empty: true };

/**
 * The section's blocks and the parts around them that the settings include: the header with the
 * wordmark when `logo`, the model line, the analysis line and the footer.
 */
export function ccResultsImageInput(request: CcResultsImageRequest, now: Date, logo: boolean): CcResultsImageLayoutInput {
  const { section, result, context, settings } = request;
  const excluded = new Set(settings.excluded[section] ?? []);
  const details = new Set(settings.detailsExcluded);
  return {
    blocks: ccResultsImageBlocks(section, result, context, key => !excluded.has(key)),
    header: details.has('header') ? null : { title: ccResultsImageTitle(section), logo },
    modelRows: details.has('model') ? [] : ccImageModelRows(result, false),
    analysisLine: details.has('analysis') ? '' : ccResultsImageAnalysisLine(result),
    footer: details.has('footer') ? '' : keyFiguresFooterText(request.overseerVersion, now)
  };
}

/** `chat-consistency-4_claude-5.5-haiku-xhigh_next-runs_20261010_094512.png`; `ext` is the encoded format. */
export function ccResultsImageFileName(
  result: CcAnalysisResult,
  section: CcResultsImageSection,
  now: Date,
  ext: FigureExportFormat = 'png'
): string {
  const id = result.analysisId !== null ? String(result.analysisId) : 'unsaved';
  return `chat-consistency-${id}_${safeFileName(result.subject.displayName)}_`
    + `${safeFileName(ccResultsImageSectionLabel(section))}_${exportTimestamp(now)}.${ext}`;
}

/** The status line for one export of a section. */
export function ccResultsImageStatusMessage(outcome: CcResultsImageOutcome, section: CcResultsImageSection): string {
  const label = ccResultsImageSectionLabel(section);
  if (outcome === 'empty') {
    return `Nothing in the ${label} section is selected; use Image settings.`;
  }
  return keyFiguresStatusMessage(outcome, `${label} section`);
}

function measuringTools(): { wrap: TextWrapper; measure: TextMeasurer } {
  const context = typeof document !== 'undefined' ? document.createElement('canvas').getContext('2d') : null;
  return context
    ? { wrap: canvasTextWrapper(context), measure: canvasTextMeasurer(context) }
    : { wrap: estimateTextWrapper, measure: estimateTextWidth };
}

/** Whether the section, as the settings include it, has anything to draw. */
export function ccResultsImageHasSelection(request: CcResultsImageRequest): boolean {
  const excluded = new Set(request.settings.excluded[request.section] ?? []);
  return ccResultsImageHasContent(ccResultsImageBlocks(request.section, request.result, request.context, key => !excluded.has(key)));
}

/** The section laid out at the settings' size, measured with the fonts as they are now. */
export function composeResultsImage(request: CcResultsImageRequest, now: Date, logo: boolean): CcResultsImageComposition {
  const { wrap, measure } = measuringTools();
  return composeResultsImageLayout(ccResultsImageInput(request, now, logo), request.settings.size,
    ccResultsImageSectionLabel(request.section), wrap, measure);
}

/**
 * The size the next image would have, without drawing it: exact for a box, and measured with the fonts
 * as they are now in fit mode, assuming the wordmark loads.
 */
export function measureResultsImage(request: CcResultsImageRequest): CcResultsImageMeasure {
  if (!ccResultsImageHasSelection(request)) {
    return { empty: true };
  }
  const composition = composeResultsImage(request, ccResultsImageIo.now(), true);
  return 'refusal' in composition
    ? { refusal: composition.refusal }
    : { widthPx: composition.frame.pixelWidth, heightPx: composition.frame.pixelHeight };
}

/**
 * The section drawn at the settings' size and colors and encoded in `format` (the settings' own unless
 * given). Throws {@link CcResultsImageRefusal} for a size that cannot hold it.
 */
export async function renderResultsImage(
  request: CcResultsImageRequest,
  format: FigureExportFormat = request.settings.format
): Promise<CcResultsImage> {
  await loadKeyFigureFonts();
  const now = ccResultsImageIo.now();
  const headerShown = !request.settings.detailsExcluded.includes('header');
  const logo = headerShown ? await loadLogo(GNOLLBENCH_WIDE_LOGO_URL) : null;
  const composition = composeResultsImage(request, now, logo !== null);
  if ('refusal' in composition) {
    throw new CcResultsImageRefusal(composition.refusal);
  }
  const canvas = paintResultsImage(composition.layout, composition.frame, ccResultsImagePalette(request.settings.scheme), logo);
  const encoded = await encodeFigureImage(canvas, format, request.settings.webpQuality);
  return {
    blob: encoded.blob,
    fileName: ccResultsImageFileName(request.result, request.section, now, encoded.format),
    fellBackToPng: encoded.fellBackToPng
  };
}

/**
 * Composes the section, then copies or saves it, and returns the status line to announce. A download is
 * encoded in the settings' format; a copy is always PNG, which is all the clipboard takes. A section with
 * nothing selected is not composed, and a size that cannot hold it is refused with its reason as the
 * status line. Never throws.
 */
export async function exportResultsImage(action: CcResultsImageAction, request: CcResultsImageRequest): Promise<string> {
  if (!ccResultsImageHasSelection(request)) {
    return ccResultsImageStatusMessage('empty', request.section);
  }
  const format: FigureExportFormat = action === 'copy' ? 'png' : request.settings.format;
  let outcome: CcResultsImageOutcome;
  try {
    const image = await renderResultsImage(request, format);
    if (action === 'download') {
      ccResultsImageIo.save(image.blob, image.fileName);
      outcome = image.fellBackToPng ? 'webp-fallback' : 'downloaded';
    } else {
      outcome = await ccResultsImageIo.copy(image.blob);
    }
  } catch (error) {
    if (error instanceof CcResultsImageRefusal) {
      return error.message;
    }
    outcome = 'failed';
  }
  return ccResultsImageStatusMessage(outcome, request.section);
}

function loadImageElement(url: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.decoding = 'async';
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error(`Could not load ${url}`));
    image.src = url;
  });
}

/** The decoded wordmark, or null when it does not load; the image is then drawn without it. */
async function loadLogo(url: string): Promise<CanvasLogo | null> {
  try {
    const image = await ccResultsImageIo.loadImage(url);
    const width = image instanceof HTMLImageElement ? image.naturalWidth || image.width : image.width;
    const height = image instanceof HTMLImageElement ? image.naturalHeight || image.height : image.height;
    return width > 0 && height > 0 ? { image, width, height } : null;
  } catch {
    return null;
  }
}
