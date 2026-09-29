import { Component, ElementRef, ViewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, Subject, of, throwError } from 'rxjs';
import {
  PdfViewerDialogComponent,
  PdfViewerFile,
  PdfViewerRequest,
  PdfViewerVariantsInfo,
  pdfLoadErrorMessage
} from './pdf-viewer-dialog.component';
import { PDFJS_LOADER, PdfJsModules } from './pdfjs-loader';

// ---------------------------------------------------------------------------------------------
// pdf.js fakes: the loader token is overridden, so neither pdf.js nor its worker is ever loaded.
// ---------------------------------------------------------------------------------------------

type Listener = (event: unknown) => void;

class FakeEventBus {
  private readonly listeners = new Map<string, Set<Listener>>();

  on(name: string, listener: Listener): void {
    if (!this.listeners.has(name)) {
      this.listeners.set(name, new Set());
    }
    this.listeners.get(name)!.add(listener);
  }

  off(name: string, listener: Listener): void {
    this.listeners.get(name)?.delete(listener);
  }

  dispatch(name: string, data: unknown): void {
    this.listeners.get(name)?.forEach(listener => listener(data));
  }
}

class FakeLinkService {
  readonly documents: unknown[] = [];
  viewer: unknown = null;

  setDocument(doc: unknown): void {
    this.documents.push(doc);
  }

  setViewer(viewer: unknown): void {
    this.viewer = viewer;
  }
}

class FakePdfViewer {
  static instances: FakePdfViewer[] = [];

  readonly eventBus: FakeEventBus;
  readonly options: Record<string, unknown>;
  readonly setDocumentCalls: unknown[] = [];
  readonly scaleValues: string[] = [];
  currentPageNumber = 1;
  increaseCount = 0;
  decreaseCount = 0;

  constructor(options: Record<string, unknown>) {
    this.options = options;
    this.eventBus = options['eventBus'] as FakeEventBus;
    FakePdfViewer.instances.push(this);
  }

  set currentScaleValue(value: string) {
    this.scaleValues.push(value);
  }

  get currentScaleValue(): string {
    return this.scaleValues[this.scaleValues.length - 1] ?? 'auto';
  }

  setDocument(doc: unknown): void {
    this.setDocumentCalls.push(doc);
  }

  previousPage(): void {
    this.currentPageNumber--;
  }

  nextPage(): void {
    this.currentPageNumber++;
  }

  increaseScale(): void {
    this.increaseCount++;
  }

  decreaseScale(): void {
    this.decreaseCount++;
  }
}

interface FakeDocument {
  numPages: number;
  /** Called when the loading task that produced this document is destroyed. */
  destroy: jasmine.Spy;
}

function fakeDocument(numPages = 3): FakeDocument {
  return { numPages, destroy: jasmine.createSpy('destroy').and.returnValue(Promise.resolve()) };
}

interface Deferred<T> {
  promise: Promise<T>;
  resolve(value: T): void;
}

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(r => (resolve = r));
  return { promise, resolve };
}

/** Each getDocument call takes the next queued document promise, else a resolved fresh document. */
class FakePdfJs {
  readonly queue: Promise<FakeDocument>[] = [];
  readonly documents: FakeDocument[] = [];
  readonly getDocument = jasmine.createSpy('getDocument').and.callFake(() => {
    const promise = this.queue.shift() ?? Promise.resolve(this.track(fakeDocument()));
    // pdf.js 6 destroys a document through its loading task.
    const destroy = jasmine.createSpy('taskDestroy').and.callFake(() => {
      void promise.then(doc => doc.destroy());
      return Promise.resolve();
    });
    return { promise, destroy };
  });
  readonly AnnotationMode = { DISABLE: 0, ENABLE: 1, ENABLE_FORMS: 2, ENABLE_STORAGE: 3 };
  readonly GlobalWorkerOptions = { workerSrc: '' };

  track(doc: FakeDocument): FakeDocument {
    this.documents.push(doc);
    return doc;
  }
}

// ---------------------------------------------------------------------------------------------
// Host: the viewer inside a parent dialog, as it sits inside the run report dialog.
// ---------------------------------------------------------------------------------------------

@Component({
  standalone: true,
  imports: [PdfViewerDialogComponent],
  template: `
    <dialog #parent class="gh-dialog parent-dialog" (cancel)="parentCancels = parentCancels + 1" (close)="parentCloses = parentCloses + 1">
      <app-pdf-viewer-dialog #viewer (closed)="closedCount = closedCount + 1" />
    </dialog>
  `
})
class TestHostComponent {
  @ViewChild('parent', { static: true }) parent!: ElementRef<HTMLDialogElement>;
  @ViewChild('viewer', { static: true }) viewer!: PdfViewerDialogComponent;
  parentCancels = 0;
  parentCloses = 0;
  closedCount = 0;
}

const TITLE = 'Executive Summary';

function pdfBytes(): Uint8Array {
  return new Uint8Array([0x25, 0x50, 0x44, 0x46, 0x2d, 0x31, 0x2e, 0x37]);
}

describe('PdfViewerDialogComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let host: TestHostComponent;
  let el: HTMLElement;
  let pdfjs: FakePdfJs;

  beforeEach(async () => {
    FakePdfViewer.instances = [];
    pdfjs = new FakePdfJs();
    const modules = {
      pdfjs,
      viewer: { EventBus: FakeEventBus, PDFLinkService: FakeLinkService, PDFViewer: FakePdfViewer }
    } as unknown as PdfJsModules;

    await TestBed.configureTestingModule({
      imports: [TestHostComponent],
      providers: [{ provide: PDFJS_LOADER, useValue: () => Promise.resolve(modules) }]
    }).compileComponents();

    fixture = TestBed.createComponent(TestHostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
    host.parent.nativeElement.showModal();
  });

  afterEach(() => {
    host.viewer.close();
    if (host.parent.nativeElement.open) {
      host.parent.nativeElement.close();
    }
  });

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await new Promise(resolve => setTimeout(resolve, 0));
    }
    fixture.detectChanges();
  }

  function request(overrides: Partial<PdfViewerRequest> = {}): PdfViewerRequest {
    return {
      title: TITLE,
      subtitle: 'Run #12',
      load: () => of({ bytes: pdfBytes(), fileName: 'GnollBench_Executive_Summary_Run12.pdf' }),
      fallbackFileName: 'Executive Summary',
      ...overrides
    };
  }

  const dialog = () => el.querySelector<HTMLDialogElement>('dialog.pdfv')!;
  const heading = () => dialog().querySelector<HTMLHeadingElement>('h3')!;
  const viewerInstance = () => FakePdfViewer.instances[0];
  const buttonByLabel = (label: string) =>
    dialog().querySelector<HTMLElement>(`[aria-label="${label}"]`)!;

  /** Opens, lets the fakes resolve, and fires pdf.js's pagesinit. */
  async function openReady(req: PdfViewerRequest = request()): Promise<void> {
    host.viewer.open(req);
    await settle();
    viewerInstance().eventBus.dispatch('pagesinit', {});
    fixture.detectChanges();
  }

  it('opens as a modal dialog and focuses its title', async () => {
    host.viewer.open(request());
    fixture.detectChanges();

    expect(dialog().open).toBeTrue();
    expect(heading().textContent?.trim()).toBe(TITLE);
    expect(document.activeElement).toBe(heading());
    expect(dialog().getAttribute('aria-labelledby')).toBe(heading().id);
    await settle();
  });

  it('shows the spinner and status line while loading, then the pages at page width', async () => {
    host.viewer.open(request());
    fixture.detectChanges();

    expect(dialog().querySelector('svg.dc-ring')).not.toBeNull();
    expect(dialog().querySelector('[role="status"]')?.textContent?.trim()).toBe('Rendering the PDF…');

    await settle();
    const viewer = viewerInstance();
    expect(viewer.options['textLayerMode']).toBe(1);
    expect(viewer.options['annotationMode']).toBe(0);
    expect(viewer.setDocumentCalls.length).toBe(1);

    viewer.eventBus.dispatch('pagesinit', {});
    viewer.eventBus.dispatch('pagechanging', { pageNumber: 2 });
    fixture.detectChanges();

    expect(viewer.currentScaleValue).toBe('page-width');
    expect(dialog().querySelector('svg.dc-ring')).toBeNull();
    expect(dialog().querySelector('[role="status"]')?.textContent?.trim()).toBe('');
    expect(dialog().querySelector<HTMLInputElement>('.pdfv-page-input')!.value).toBe('2');
    expect(dialog().querySelector('.pdfv-page-count')?.textContent?.trim()).toBe('of 3');
  });

  it('hands pdf.js a copy of the bytes, since pdf.js transfers the buffer to its worker', async () => {
    const bytes = pdfBytes();
    await openReady(request({ load: () => of({ bytes, fileName: null }) }));

    const passed = pdfjs.getDocument.calls.mostRecent().args[0].data as Uint8Array;
    expect(passed).not.toBe(bytes);
    expect(Array.from(passed)).toEqual(Array.from(bytes));
  });

  describe('Download', () => {
    let downloadNames: string[];
    let revoked: string[];

    beforeEach(() => {
      downloadNames = [];
      revoked = [];
      spyOn(URL, 'createObjectURL').and.returnValue('blob:pdf-viewer-test');
      spyOn(URL, 'revokeObjectURL').and.callFake((url: string) => revoked.push(url));
      spyOn(HTMLAnchorElement.prototype, 'click').and.callFake(function (this: HTMLAnchorElement) {
        downloadNames.push(this.download);
      });
    });

    it('saves under the server file name', async () => {
      await openReady();
      buttonByLabel(`Download the ${TITLE} as PDF`).click();
      await settle();

      expect(downloadNames).toEqual(['GnollBench_Executive_Summary_Run12.pdf']);
      expect(revoked).toEqual(['blob:pdf-viewer-test']);
    });

    it('falls back to the sanitized fallback name when the server sends none', async () => {
      await openReady(request({ load: () => of({ bytes: pdfBytes(), fileName: null }) }));
      buttonByLabel(`Download the ${TITLE} as PDF`).click();

      expect(downloadNames).toEqual(['executive-summary.pdf']);
    });

    it('does nothing before the bytes have arrived', () => {
      host.viewer.open(request({ load: () => new Subject<PdfViewerFile>() }));
      fixture.detectChanges();
      const button = buttonByLabel(`Download the ${TITLE} as PDF`);

      expect(button.getAttribute('aria-disabled')).toBe('true');
      button.click();
      expect(downloadNames).toEqual([]);
    });
  });

  it('reloads on a variant change and discards a load that finishes after a newer one started', async () => {
    const requested: (string | null)[] = [];
    const slowA = deferred<FakeDocument>();
    const docA = fakeDocument(5);
    const docB = pdfjs.track(fakeDocument(7));
    pdfjs.queue.push(slowA.promise, Promise.resolve(docB));

    host.viewer.open(request({
      variants: [{ key: 'summary', label: 'Summary' }, { key: 'full', label: 'Full' }],
      initialVariant: 'summary',
      load: (variant: string | null) => {
        requested.push(variant);
        return of({ bytes: pdfBytes(), fileName: `${variant}.pdf` });
      }
    }));
    await settle();

    const fullTab = dialog().querySelector<HTMLButtonElement>('[role="tab"]:nth-child(2)')!;
    expect(fullTab.textContent?.trim()).toBe('Full');
    fullTab.click();
    await settle();

    // The Summary document resolves only now, after Full has already been handed to the viewer.
    slowA.resolve(docA);
    await settle();

    const viewer = viewerInstance();
    expect(requested).toEqual(['summary', 'full']);
    expect(viewer.setDocumentCalls).toEqual([docB]);
    expect(docA.destroy).toHaveBeenCalled();
    expect(docB.destroy).not.toHaveBeenCalled();
    expect(fullTab.getAttribute('aria-selected')).toBe('true');
  });

  it('destroys the previous document when the variant changes', async () => {
    await openReady(request({
      variants: [{ key: 'summary', label: 'Summary' }, { key: 'full', label: 'Full' }]
    }));
    const first = pdfjs.documents[0];

    dialog().querySelector<HTMLButtonElement>('[role="tab"]:nth-child(2)')!.click();
    await settle();

    expect(first.destroy).toHaveBeenCalled();
    expect(viewerInstance().setDocumentCalls).toEqual([first, null, pdfjs.documents[1]]);
  });

  it('shows the server message on failure, and Try Again reloads', async () => {
    let attempts = 0;
    host.viewer.open(request({
      load: (): Observable<PdfViewerFile> => {
        attempts++;
        return attempts === 1
          ? throwError(() => new HttpErrorResponse({ status: 500, error: { error: 'The report could not be rendered.' } }))
          : of({ bytes: pdfBytes(), fileName: null });
      }
    }));
    await settle();

    const alert = dialog().querySelector('.alert.alert-danger[role="alert"]');
    expect(alert?.textContent).toContain('The report could not be rendered.');
    expect(dialog().querySelector<HTMLElement>('.pdfv-pages')!.hidden).toBeTrue();

    const retry = Array.from(dialog().querySelectorAll<HTMLButtonElement>('button.btn-gh'))
      .find(b => b.textContent?.trim() === 'Try Again')!;
    retry.click();
    await settle();
    viewerInstance().eventBus.dispatch('pagesinit', {});
    fixture.detectChanges();

    expect(attempts).toBe(2);
    expect(dialog().querySelector('.alert.alert-danger')).toBeNull();
    expect(viewerInstance().setDocumentCalls.length).toBe(1);
  });

  it('falls back to a generic message when the error carries none', () => {
    expect(pdfLoadErrorMessage(new Error('boom'))).toBe('The PDF could not be loaded.');
    expect(pdfLoadErrorMessage(new HttpErrorResponse({ status: 500, error: null }))).toBe('The PDF could not be loaded.');
    expect(pdfLoadErrorMessage(new HttpErrorResponse({ status: 500, error: '<html>oops</html>' }))).toBe('The PDF could not be loaded.');
    expect(pdfLoadErrorMessage(new HttpErrorResponse({ status: 404, error: 'Run not found.' }))).toBe('Run not found.');
  });

  it('tears down on close: destroys the document, clears the viewer and emits closed', async () => {
    await openReady();
    const doc = pdfjs.documents[0];
    const viewer = viewerInstance();

    // A dialog's close event is queued as a task, not fired synchronously, and under a loaded
    // suite it can arrive after any fixed number of ticks: wait for the event itself.
    const closeEvent = new Promise<void>(resolve => dialog().addEventListener('close', () => resolve(), { once: true }));
    host.viewer.close();
    await closeEvent;
    await settle();

    expect(dialog().open).toBeFalse();
    expect(doc.destroy).toHaveBeenCalled();
    expect(viewer.setDocumentCalls[viewer.setDocumentCalls.length - 1]).toBeNull();
    expect(host.closedCount).toBe(1);
  });

  it('keeps its cancel and close events from reaching a parent dialog', async () => {
    await openReady();

    // Sanity: a bubbling event from elsewhere inside the parent does reach it.
    el.querySelector('app-pdf-viewer-dialog')!.dispatchEvent(new Event('cancel', { bubbles: true, cancelable: true }));
    expect(host.parentCancels).toBe(1);

    dialog().dispatchEvent(new Event('cancel', { bubbles: true, cancelable: true }));
    dialog().dispatchEvent(new Event('close', { bubbles: true }));
    host.viewer.close();
    await settle();

    expect(host.parentCancels).toBe(1);
    expect(host.parentCloses).toBe(0);
    expect(host.parent.nativeElement.open).toBeTrue();
  });

  it('zooms with + and - on the focused pages, and prevents the browser zoom for Ctrl+=', async () => {
    await openReady();
    const pages = dialog().querySelector<HTMLElement>('.pdfv-pages')!;

    pages.dispatchEvent(new KeyboardEvent('keydown', { key: '+', bubbles: true, cancelable: true }));
    const ctrlPlus = new KeyboardEvent('keydown', { key: '=', ctrlKey: true, bubbles: true, cancelable: true });
    pages.dispatchEvent(ctrlPlus);
    pages.dispatchEvent(new KeyboardEvent('keydown', { key: '-', bubbles: true, cancelable: true }));

    expect(viewerInstance().increaseCount).toBe(2);
    expect(viewerInstance().decreaseCount).toBe(1);
    expect(ctrlPlus.defaultPrevented).toBeTrue();
    expect(pages.getAttribute('aria-label')).toBe(`${TITLE}, PDF pages`);
    expect(pages.tabIndex).toBe(0);
  });

  it('gives every icon control a distinct accessible name with a hint tooltip', async () => {
    await openReady(request({ tabUrl: variant => `/api/admin/benchmark/runs/12/executive-summary.pdf?v=${variant}` }));

    const controls = Array.from(dialog().querySelectorAll<HTMLElement>('.action-btn, .btn-icon-action'));
    const names = controls.map(c => c.getAttribute('aria-label') ?? '');

    expect(controls.length).toBe(7);
    expect(names.every(name => name.includes(TITLE))).toBeTrue();
    expect(new Set(names).size).toBe(names.length);
    for (const control of controls) {
      expect(control.hasAttribute('title')).toBeFalse();
      const tip = document.getElementById(control.getAttribute('interestfor') ?? '');
      expect(tip?.getAttribute('popover')).toBe('hint');
    }

    const tabLink = dialog().querySelector<HTMLAnchorElement>('a.action-btn')!;
    expect(tabLink.getAttribute('href')).toBe('/api/admin/benchmark/runs/12/executive-summary.pdf?v=null');
    expect(tabLink.target).toBe('_blank');
    expect(tabLink.rel).toBe('noopener');
  });

  it('leaves out Open in new tab when the request has no tab URL', async () => {
    await openReady();

    expect(dialog().querySelector('a.action-btn')).toBeNull();
  });

  describe('Versions explanation', () => {
    const VARIANTS = [{ key: 'summary', label: 'Summary' }, { key: 'full', label: 'Full' }];
    const INFO: PdfViewerVariantsInfo = {
      title: 'What the versions mean',
      items: [
        { term: 'Summary', text: 'The short version.' },
        { term: 'Full', text: 'Everything, for the team.', points: ['Marked internal.', 'Never shared.'] }
      ],
      note: 'Both come from one stored document.'
    };

    const versionsButton = () => dialog().querySelector<HTMLButtonElement>('button[aria-label="About Versions"]');

    it('has no Versions button without an explanation', async () => {
      host.viewer.open(request({ variants: VARIANTS }));
      await settle();

      expect(dialog().querySelector('[role="tablist"]')).not.toBeNull();
      expect(versionsButton()).toBeNull();
      expect(dialog().querySelector('app-info-tip')).toBeNull();
    });

    it('has no Versions button without variants, even with an explanation', async () => {
      host.viewer.open(request({ variantsInfo: INFO }));
      await settle();

      expect(dialog().querySelector('[role="tablist"]')).toBeNull();
      expect(versionsButton()).toBeNull();
    });

    it('puts the Versions button right after the variant tabs, and opens the explanation in a dialog', async () => {
      host.viewer.open(request({ variants: VARIANTS, variantsInfo: INFO }));
      await settle();

      const tablist = dialog().querySelector<HTMLElement>('[role="tablist"]')!;
      const tip = tablist.nextElementSibling!;
      expect(tip.tagName.toLowerCase()).toBe('app-info-tip');
      const button = versionsButton()!;
      expect(tip.contains(button)).toBeTrue();
      expect(button.hasAttribute('title')).toBeFalse();

      button.click();
      fixture.detectChanges();

      const infoDialog = tip.querySelector<HTMLDialogElement>('dialog')!;
      expect(infoDialog.open).toBeTrue();
      expect(infoDialog.textContent).toContain(INFO.title);
      const ddText = (dd: Element) => {
        const copy = dd.cloneNode(true) as Element;
        copy.querySelector('ul')?.remove();
        return copy.textContent!.trim();
      };
      const pairs = Array.from(infoDialog.querySelectorAll('dl > div')).map(group => [
        group.querySelector('dt')!.textContent!.trim(),
        ddText(group.querySelector('dd')!)
      ]);
      expect(pairs).toEqual([['Summary', 'The short version.'], ['Full', 'Everything, for the team.']]);
      expect(infoDialog.querySelector('p.pdfv-variants-info-note')!.textContent!.trim()).toBe(INFO.note!);

      // In dialog mode nothing is described by the tip.
      const tipId = `${host.viewer.idPrefix}-variants-info`;
      const describedByTip = Array.from(el.querySelectorAll('[aria-describedby]'))
        .filter(element => element.getAttribute('aria-describedby')!.split(' ').includes(tipId));
      expect(describedByTip).toEqual([]);

      // Closing the explanation leaves the viewer open.
      const closeEvent = new Promise<void>(resolve => infoDialog.addEventListener('close', () => resolve(), { once: true }));
      infoDialog.close();
      await closeEvent;
      await settle();

      expect(infoDialog.open).toBeFalse();
      expect(dialog().open).toBeTrue();
      expect(host.closedCount).toBe(0);
    });

    it('leaves out the note paragraph when the explanation has none', async () => {
      host.viewer.open(request({ variants: VARIANTS, variantsInfo: { title: INFO.title, items: INFO.items } }));
      await settle();

      versionsButton()!.click();
      fixture.detectChanges();

      const infoDialog = dialog().querySelector<HTMLDialogElement>('app-info-tip dialog')!;
      expect(infoDialog.querySelectorAll('dl > div').length).toBe(2);
      expect(infoDialog.querySelector('p.pdfv-variants-info-note')).toBeNull();
      infoDialog.close();
    });

    it('lists an item\'s points under its text, and renders no list for an item without points', async () => {
      host.viewer.open(request({ variants: VARIANTS, variantsInfo: INFO }));
      await settle();

      versionsButton()!.click();
      fixture.detectChanges();

      const infoDialog = dialog().querySelector<HTMLDialogElement>('app-info-tip dialog')!;
      const groups = Array.from(infoDialog.querySelectorAll('dl > div'));
      expect(groups[0].querySelector('dd ul')).toBeNull();
      const points = Array.from(groups[1].querySelectorAll('dd > ul > li')).map(li => li.textContent!.trim());
      expect(points).toEqual(['Marked internal.', 'Never shared.']);
      infoDialog.close();
    });
  });
});
