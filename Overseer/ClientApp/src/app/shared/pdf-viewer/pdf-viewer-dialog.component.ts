import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  NgZone,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  ViewEncapsulation,
  inject
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, Subscription } from 'rxjs';
import type { PDFDocumentLoadingTask, PDFDocumentProxy } from 'pdfjs-dist';
import type { EventBus, PDFLinkService, PDFViewer } from 'pdfjs-dist/web/pdf_viewer.mjs';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';
import { safeFileName } from '../../utils/download.util';
import { InfoTipComponent } from '../info-tip/info-tip.component';
import { PDFJS_LOADER, PdfJsModules } from './pdfjs-loader';

/** One selectable version of a document, such as a disclosure level. */
export interface PdfViewerVariant {
  key: string;
  label: string;
}

/** One term of a variants explanation and what it means. */
export interface PdfViewerVariantsInfoItem {
  term: string;
  text: string;
}

/** What the variants mean, behind an info button after the variant tab row. */
export interface PdfViewerVariantsInfo {
  /** The explanation dialog's title. */
  title: string;
  /** Shown as a definition list. */
  items: PdfViewerVariantsInfoItem[];
  /** A paragraph after the list. */
  note?: string;
}

/** A loaded PDF: its bytes and the file name the server gave it, if any. */
export interface PdfViewerFile {
  bytes: Uint8Array;
  fileName: string | null;
}

/** What the viewer shows and where it gets it. Nothing in it is specific to one kind of document. */
export interface PdfViewerRequest {
  /** The document's name: the dialog title, and the subject of every control's accessible name. */
  title: string;
  subtitle?: string;
  /** Versions offered as a segmented tab row; absent or empty for a single document. */
  variants?: PdfViewerVariant[];
  /** The variant shown first; the first variant when absent or unknown. */
  initialVariant?: string;
  /** What the variants mean; its info button is shown only with variants. */
  variantsInfo?: PdfViewerVariantsInfo;
  /** Fetches the PDF; `variant` is null when there are no variants. An error shows the server's message. */
  load(variant: string | null): Observable<PdfViewerFile>;
  /** A real same-origin URL for the same PDF, opened by "Open in new tab"; that control is absent without it. */
  tabUrl?(variant: string | null): string;
  /** The download name when the server sends none; reduced by `safeFileName`. */
  fallbackFileName: string;
}

export type PdfViewerState = 'idle' | 'loading' | 'ready' | 'error';

interface ZoomOption {
  value: string;
  label: string;
}

/** pdf.js named scales, in the order the zoom select lists them. */
const ZOOM_PRESETS: readonly ZoomOption[] = [
  { value: 'auto', label: 'Automatic' },
  { value: 'page-fit', label: 'Page fit' },
  { value: 'page-width', label: 'Page width' }
];

const ZOOM_OPTIONS: readonly ZoomOption[] = [
  ...ZOOM_PRESETS,
  ...[0.5, 0.75, 1, 1.25, 1.5, 2].map(scale => ({ value: String(scale), label: `${Math.round(scale * 100)} %` }))
];

/** The zoom select's value while the scale matches none of its options. */
const CUSTOM_ZOOM = 'custom';

/** pdf.js `TextLayerMode.ENABLE` (the viewer module does not export the enum): selectable, copyable text. */
const TEXT_LAYER_ENABLE = 1;

/**
 * Options for every `getDocument` call. If the CSP turns out to block the embedded fonts, set
 * `disableFontFace: true` here: pdf.js then draws glyphs as paths instead of loading font faces.
 */
const PDF_DOCUMENT_OPTIONS = {
  disableFontFace: false
} as const;

const GENERIC_ERROR = 'The PDF could not be loaded.';

let nextInstanceId = 0;

interface PageChangingEvent {
  pageNumber: number;
}

interface ScaleChangingEvent {
  scale: number;
  presetValue?: string;
}

/**
 * The message to show for a failed load: the server's own (`{ error }`, `{ message }`, `{ detail }`
 * or `{ title }`, or a short plain-text body, as `decodeBinaryErrorBody` delivers it), else a generic one.
 */
export function pdfLoadErrorMessage(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return GENERIC_ERROR;
  }
  const body: unknown = error.error;
  if (typeof body === 'string') {
    const text = body.trim();
    return text !== '' && text.length <= 500 && !text.startsWith('<') ? text : GENERIC_ERROR;
  }
  if (body && typeof body === 'object') {
    const record = body as Record<string, unknown>;
    for (const key of ['error', 'message', 'detail', 'title']) {
      const value = record[key];
      if (typeof value === 'string' && value.trim() !== '') {
        return value.trim();
      }
    }
  }
  return GENERIC_ERROR;
}

/**
 * A full-screen PDF viewer. pdf.js draws the pages into canvases inside this dialog, so no PDF is
 * embedded (the CSP forbids it), and the Download button saves the loaded bytes under the server's
 * file name. Generic: the caller supplies the title, the variants and the loader.
 *
 * `ViewEncapsulation.None` because pdf.js builds the page DOM outside Angular's templates; every own
 * rule is scoped under `.pdfv`.
 */
@Component({
  selector: 'app-pdf-viewer-dialog',
  standalone: true,
  imports: [InfoTipComponent],
  templateUrl: './pdf-viewer-dialog.component.html',
  styleUrl: './pdf-viewer-dialog.component.scss',
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PdfViewerDialogComponent implements OnInit, OnDestroy {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly zone = inject(NgZone);
  private readonly loadPdfJs = inject(PDFJS_LOADER);

  /** Emits after the dialog has closed and released its document. */
  @Output() readonly closed = new EventEmitter<void>();

  @ViewChild('dialog', { static: true }) private dialogRef!: ElementRef<HTMLDialogElement>;
  @ViewChild('heading', { static: true }) private headingRef!: ElementRef<HTMLHeadingElement>;
  @ViewChild('pages', { static: true }) private pagesRef!: ElementRef<HTMLDivElement>;
  @ViewChild('pagesViewer', { static: true }) private pagesViewerRef!: ElementRef<HTMLDivElement>;

  readonly idPrefix = `pdfv${++nextInstanceId}`;
  readonly zoomOptions = ZOOM_OPTIONS;
  readonly customZoom = CUSTOM_ZOOM;

  request: PdfViewerRequest | null = null;
  variant: string | null = null;
  tabHref: string | null = null;
  state: PdfViewerState = 'idle';
  errorMessage = '';
  pageNumber = 1;
  pagesCount = 0;
  zoomValue = 'page-width';
  customZoomLabel = '';

  private generation = 0;
  private loadSubscription: Subscription | null = null;
  private bytes: Uint8Array | null = null;
  private fileName: string | null = null;
  private loadingTask: PDFDocumentLoadingTask | null = null;
  private pdfDocument: PDFDocumentProxy | null = null;
  private viewer: PDFViewer | null = null;
  private eventBus: EventBus | null = null;
  private linkService: PDFLinkService | null = null;

  get title(): string {
    return this.request?.title ?? '';
  }

  get variants(): PdfViewerVariant[] {
    return this.request?.variants ?? [];
  }

  get variantsInfo(): PdfViewerVariantsInfo | null {
    return this.variants.length > 0 ? this.request?.variantsInfo ?? null : null;
  }

  get isReady(): boolean {
    return this.state === 'ready';
  }

  get canDownload(): boolean {
    return this.bytes !== null;
  }

  get canGoPrevious(): boolean {
    return this.isReady && this.pageNumber > 1;
  }

  get canGoNext(): boolean {
    return this.isReady && this.pageNumber < this.pagesCount;
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.teardown();
    if (this.eventBus) {
      this.eventBus.off('pagesinit', this.onPagesInit);
      this.eventBus.off('pagechanging', this.onPageChanging);
      this.eventBus.off('scalechanging', this.onScaleChanging);
    }
    this.viewer = null;
    this.eventBus = null;
    this.linkService = null;
  }

  /** Shows the dialog and loads the request's document; a dialog already open switches to the new request. */
  open(request: PdfViewerRequest): void {
    this.teardown();
    this.request = request;
    const variants = request.variants ?? [];
    this.variant = variants.length === 0
      ? null
      : (variants.some(v => v.key === request.initialVariant) ? request.initialVariant! : variants[0].key);
    this.updateTabHref();
    this.state = 'loading';
    this.cdr.detectChanges();

    const dialog = this.dialogRef.nativeElement;
    if (!dialog.open) {
      dialog.showModal();
    }
    // showModal() focuses the first focusable control, whose hint tooltip would then open by itself.
    this.headingRef.nativeElement.focus();
    this.startLoad();
  }

  close(): void {
    const dialog = this.dialogRef.nativeElement;
    if (dialog.open) {
      dialog.close();
    }
  }

  /** Stops the dialog's own cancel and close events, so a parent dialog behind it never reacts to them. */
  onDialogCancel(event: Event): void {
    event.stopPropagation();
  }

  onDialogClose(event: Event): void {
    event.stopPropagation();
    this.teardown();
    this.cdr.markForCheck();
    this.closed.emit();
  }

  selectVariant(key: string): void {
    if (key === this.variant) {
      return;
    }
    this.variant = key;
    this.updateTabHref();
    this.startLoad();
  }

  onVariantKeydown(event: KeyboardEvent, index: number): void {
    const variants = this.variants;
    const targets: Record<string, number> = {
      ArrowRight: index + 1,
      ArrowLeft: index - 1,
      Home: 0,
      End: variants.length - 1
    };
    const requested = targets[event.key];
    if (requested === undefined || variants.length === 0) {
      return;
    }
    event.preventDefault();
    const next = variants[(requested + variants.length) % variants.length];
    this.selectVariant(next.key);
    document.getElementById(this.variantTabId(next.key))?.focus();
  }

  variantTabId(key: string): string {
    return `${this.idPrefix}-variant-${key.replace(/[^A-Za-z0-9_-]/g, '-')}`;
  }

  retry(): void {
    this.startLoad();
    this.headingRef.nativeElement.focus();
  }

  previousPage(): void {
    if (this.canGoPrevious) {
      this.viewer?.previousPage();
    }
  }

  nextPage(): void {
    if (this.canGoNext) {
      this.viewer?.nextPage();
    }
  }

  /** Goes to the typed page; anything outside 1..N puts the current page back into the field. */
  commitPageInput(input: HTMLInputElement): void {
    const requested = Number(input.value);
    if (this.viewer && this.isReady && Number.isInteger(requested) && requested >= 1 && requested <= this.pagesCount) {
      this.viewer.currentPageNumber = requested;
      return;
    }
    input.value = String(this.pageNumber);
  }

  onZoomSelect(value: string): void {
    if (this.viewer && this.isReady && value !== CUSTOM_ZOOM) {
      this.viewer.currentScaleValue = value;
    }
  }

  zoomIn(): void {
    if (this.viewer && this.isReady) {
      this.viewer.increaseScale();
    }
  }

  zoomOut(): void {
    if (this.viewer && this.isReady) {
      this.viewer.decreaseScale();
    }
  }

  /**
   * `+` and `-` zoom while the pages have focus, and so do `Ctrl`+`=` and `Ctrl`+`-`, whose browser
   * zoom is prevented. Arrows, PageUp, PageDown, Home and End scroll the focused container natively.
   */
  onPagesKeydown(event: KeyboardEvent): void {
    if (event.altKey) {
      return;
    }
    const withCtrl = event.ctrlKey || event.metaKey;
    if (event.key === '+' || (withCtrl && event.key === '=')) {
      event.preventDefault();
      this.zoomIn();
    } else if (event.key === '-') {
      event.preventDefault();
      this.zoomOut();
    }
  }

  /** Saves the loaded bytes under the server's file name, else the sanitized fallback. */
  download(): void {
    const request = this.request;
    if (!this.bytes || !request) {
      return;
    }
    const serverName = this.fileName?.trim();
    const name = serverName ? serverName : withPdfExtension(safeFileName(request.fallbackFileName));
    const url = URL.createObjectURL(new Blob([this.bytes as unknown as BlobPart], { type: 'application/pdf' }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = name;
    anchor.style.display = 'none';
    document.body.appendChild(anchor);
    try {
      anchor.click();
    } finally {
      anchor.remove();
      setTimeout(() => URL.revokeObjectURL(url), 0);
    }
  }

  private updateTabHref(): void {
    const request = this.request;
    this.tabHref = request?.tabUrl ? request.tabUrl(this.variant) : null;
  }

  /** Fetches and renders the current variant; the previous document is destroyed first. */
  private startLoad(): void {
    const request = this.request;
    if (!request) {
      return;
    }
    const generation = ++this.generation;
    this.loadSubscription?.unsubscribe();
    this.loadSubscription = null;
    this.releaseDocument();
    this.state = 'loading';
    this.errorMessage = '';
    this.cdr.markForCheck();

    this.loadSubscription = request.load(this.variant).subscribe({
      next: file => {
        if (generation === this.generation) {
          this.zone.runOutsideAngular(() => void this.render(file, generation));
        }
      },
      error: (error: unknown) => {
        if (generation === this.generation) {
          this.fail(pdfLoadErrorMessage(error));
        }
      }
    });
  }

  private async render(file: PdfViewerFile, generation: number): Promise<void> {
    this.inAngular(() => {
      this.bytes = file.bytes;
      this.fileName = file.fileName;
    });
    try {
      const modules = await this.loadPdfJs();
      if (generation !== this.generation) {
        return;
      }
      const viewer = this.ensureViewer(modules);
      // pdf.js transfers the buffer it is given to its worker, so it gets a copy: the bytes stay downloadable.
      const task = modules.pdfjs.getDocument({ ...PDF_DOCUMENT_OPTIONS, data: file.bytes.slice() });
      this.loadingTask = task;
      const pdfDocument = await task.promise;
      if (generation !== this.generation) {
        void task.destroy().catch(() => undefined);
        return;
      }
      this.pdfDocument = pdfDocument;
      this.inAngular(() => {
        this.pagesCount = pdfDocument.numPages;
        this.pageNumber = 1;
      });
      this.linkService?.setDocument(pdfDocument, null);
      viewer.setDocument(pdfDocument);
    } catch {
      if (generation === this.generation) {
        this.fail(GENERIC_ERROR);
      }
    }
  }

  private ensureViewer(modules: PdfJsModules): PDFViewer {
    if (this.viewer) {
      return this.viewer;
    }
    const lib = modules.viewer;
    const eventBus = new lib.EventBus();
    const linkService = new lib.PDFLinkService({ eventBus });
    const viewer = new lib.PDFViewer({
      container: this.pagesRef.nativeElement,
      viewer: this.pagesViewerRef.nativeElement,
      eventBus,
      linkService,
      textLayerMode: TEXT_LAYER_ENABLE,
      annotationMode: modules.pdfjs.AnnotationMode.DISABLE,
      // Native ::selection over the text layer, rather than selection drawn into the canvas.
      enableSelectionRendering: false
    });
    linkService.setViewer(viewer);
    eventBus.on('pagesinit', this.onPagesInit);
    eventBus.on('pagechanging', this.onPageChanging);
    eventBus.on('scalechanging', this.onScaleChanging);
    this.viewer = viewer;
    this.eventBus = eventBus;
    this.linkService = linkService;
    return viewer;
  }

  private readonly onPagesInit = (): void => {
    if (!this.viewer || !this.pdfDocument) {
      return;
    }
    // Fits the page to a phone's width as well as a desktop's.
    this.viewer.currentScaleValue = 'page-width';
    this.inAngular(() => {
      this.state = 'ready';
    });
  };

  private readonly onPageChanging = (event: PageChangingEvent): void => {
    this.inAngular(() => {
      this.pageNumber = event.pageNumber;
    });
  };

  private readonly onScaleChanging = (event: ScaleChangingEvent): void => {
    this.inAngular(() => {
      const preset = ZOOM_PRESETS.find(option => option.value === event.presetValue);
      const numeric = ZOOM_OPTIONS.find(option => !ZOOM_PRESETS.includes(option) && Math.abs(Number(option.value) - event.scale) < 0.001);
      const match = preset ?? numeric;
      this.zoomValue = match ? match.value : CUSTOM_ZOOM;
      this.customZoomLabel = match ? '' : `${Math.round(event.scale * 100)} %`;
    });
  };

  private fail(message: string): void {
    this.inAngular(() => {
      this.releaseDocument(false);
      this.state = 'error';
      this.errorMessage = message;
    });
  }

  /** Invalidates any load in flight and releases everything the open document holds. */
  private teardown(): void {
    this.generation++;
    this.loadSubscription?.unsubscribe();
    this.loadSubscription = null;
    this.releaseDocument();
    this.state = 'idle';
    this.errorMessage = '';
  }

  /**
   * Clears the viewer and destroys the loading task, which owns the document (pdf.js 6 has no
   * PDFDocumentProxy.destroy); optionally drops the bytes.
   */
  private releaseDocument(dropBytes = true): void {
    const pdfDocument = this.pdfDocument;
    const task = this.loadingTask;
    this.pdfDocument = null;
    this.loadingTask = null;
    if (pdfDocument) {
      this.viewer?.setDocument(null as unknown as PDFDocumentProxy);
      this.linkService?.setDocument(null, null);
    }
    if (task) {
      void task.destroy().catch(() => undefined);
    }
    if (dropBytes) {
      this.bytes = null;
      this.fileName = null;
    }
    this.pagesCount = 0;
    this.pageNumber = 1;
    this.zoomValue = 'page-width';
    this.customZoomLabel = '';
  }

  /** Runs pdf.js callbacks back inside Angular and marks the view for checking. */
  private inAngular(update: () => void): void {
    this.zone.run(() => {
      update();
      this.cdr.markForCheck();
    });
  }
}

function withPdfExtension(name: string): string {
  return name.toLowerCase().endsWith('.pdf') ? name : `${name}.pdf`;
}
