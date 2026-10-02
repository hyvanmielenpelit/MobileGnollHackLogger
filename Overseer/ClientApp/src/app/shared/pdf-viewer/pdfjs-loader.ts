import { InjectionToken } from '@angular/core';

/** The pdf.js display API (`pdfjs-dist`). A type only: the module itself is loaded on demand. */
export type PdfJsLib = typeof import('pdfjs-dist');

/** The pdf.js viewer components (`pdfjs-dist/web/pdf_viewer.mjs`): `PDFViewer`, `EventBus`, `PDFLinkService`. */
export type PdfJsViewerLib = typeof import('pdfjs-dist/web/pdf_viewer.mjs');

export interface PdfJsModules {
  readonly pdfjs: PdfJsLib;
  readonly viewer: PdfJsViewerLib;
}

/**
 * The pdf.js worker, a same-origin static file: `angular.json` copies it from
 * `node_modules/pdfjs-dist/build` to `/pdfjs/` at build time, so `worker-src` falls back to
 * `script-src 'self'`.
 */
export const PDFJS_WORKER_SRC = '/pdfjs/pdf.worker.min.mjs';

let pending: Promise<PdfJsModules> | null = null;

/**
 * Loads pdf.js and its viewer components once per page, as lazy chunks that never reach the initial
 * bundle. The viewer module reads `globalThis.pdfjsLib`, which the display module sets, so the two
 * are imported in that order. A failed load is forgotten, so the next call retries.
 */
export function loadPdfJs(): Promise<PdfJsModules> {
  if (pending) {
    return pending;
  }
  const attempt = (async (): Promise<PdfJsModules> => {
    const pdfjs = await import('pdfjs-dist');
    pdfjs.GlobalWorkerOptions.workerSrc = PDFJS_WORKER_SRC;
    const viewer = await import('pdfjs-dist/web/pdf_viewer.mjs');
    return { pdfjs, viewer };
  })();
  pending = attempt;
  attempt.catch(() => {
    if (pending === attempt) {
      pending = null;
    }
  });
  return attempt;
}

/**
 * How the PDF viewer obtains pdf.js. Specs provide a factory returning fakes, so the test runner never loads
 * pdf.js or its worker:
 *
 * ```ts
 * providers: [{ provide: PDFJS_LOADER, useValue: () => Promise.resolve(fakeModules) }]
 * ```
 */
export const PDFJS_LOADER = new InjectionToken<() => Promise<PdfJsModules>>('PDFJS_LOADER', {
  providedIn: 'root',
  factory: () => loadPdfJs
});
