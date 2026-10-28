import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, of, throwError } from 'rxjs';
import { FeedbackFormatExamples, FeedbackTransferService } from '../../../services/feedback-transfer.service';
import { FeedbackFormatsTipComponent } from './feedback-formats-tip.component';

const EXAMPLES: FeedbackFormatExamples = {
  issues: {
    fileName: 'example-overseer-feedback-overseer-20261028-0915.yaml',
    content: 'format: overseer-feedback-issues\nversion: 1\n',
    mediaType: 'application/yaml;charset=utf-8'
  },
  resolutions: {
    fileName: 'example-agent-feedback-resolutions-overseer_v1.yaml',
    content: 'format: overseer-feedback-resolutions\nversion: 2\n',
    mediaType: 'application/yaml;charset=utf-8'
  }
};

describe('FeedbackFormatsTipComponent', () => {
  let fixture: ComponentFixture<FeedbackFormatsTipComponent>;
  let host: HTMLElement;
  let formatExamples: Mock;

  const q = <T extends Element = HTMLElement>(selector: string): T | null => host.querySelector<T>(selector);
  const all = (selector: string): HTMLElement[] => Array.from(host.querySelectorAll<HTMLElement>(selector));
  const text = (el: Element | null): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

  function create(source: 'overseer' | 'gnollbench' = 'overseer'): void {
    fixture = TestBed.createComponent(FeedbackFormatsTipComponent);
    host = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('source', source);
    fixture.detectChanges();
  }

  function openTip(): void {
    q<HTMLButtonElement>('button.gh-info-btn')!.click();
    fixture.detectChanges();
  }

  function closeTip(): void {
    q<HTMLButtonElement>('dialog .dialog-header .btn-icon-action')!.click();
    fixture.detectChanges();
  }

  /** The save path of `downloadTextFile`: the blob through its object URL, the name through the anchor. */
  function captureSaves(): { blobs: Blob[]; names: string[] } {
    const saved: { blobs: Blob[]; names: string[] } = { blobs: [], names: [] };
    vi.spyOn(URL, 'createObjectURL').mockImplementation((source: Blob | MediaSource) => {
      saved.blobs.push(source as Blob);
      return 'blob:feedback-formats-test';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      saved.names.push(this.download);
    });
    return saved;
  }

  beforeEach(async () => {
    formatExamples = vi.fn().mockName('FeedbackTransferService.formatExamples');
    await TestBed.configureTestingModule({
      imports: [FeedbackFormatsTipComponent],
      providers: [{ provide: FeedbackTransferService, useValue: { formatExamples } }]
    }).compileComponents();
  });

  afterEach(() => {
    for (const dialog of Array.from(document.querySelectorAll('dialog[open]')) as HTMLDialogElement[]) {
      dialog.close();
    }
  });

  it('renders a dialog tip named for the file formats and loads nothing before it opens', () => {
    create();
    const button = q<HTMLButtonElement>('button.gh-info-btn')!;
    expect(button.getAttribute('aria-label')).toBe('About Feedback file formats');
    expect(button.getAttribute('aria-haspopup')).toBe('dialog');
    expect(text(q('#overseer-feedback-formats-title'))).toBe('Feedback file formats');
    expect(formatExamples).not.toHaveBeenCalled();
  });

  it('has three sections, and lists the resolved-via ids with their labels', () => {
    create();
    expect(all('section h4.gh-section-title').map(h => text(h))).toEqual([
      'Issues file (export)', 'Resolutions file (import)', 'Closing one issue'
    ]);
    const pairs = all('.fft-resolved-via > div').map(div => [text(div.querySelector('dt')), text(div.querySelector('dd'))]);
    expect(pairs).toEqual([
      ['wiki', 'GnollHack wiki'],
      ['knowledge_base', 'Knowledge base'],
      ['overseer_release', 'Overseer release'],
      ['gnollhack_release', 'GnollHack release'],
      ['server_configuration', 'Server configuration'],
      ['other', 'Other']
    ]);
  });

  it('shows Loading examples while the request runs, then both example files', () => {
    const response = new Subject<FeedbackFormatExamples>();
    formatExamples.mockReturnValue(response.asObservable());
    create();
    openTip();

    expect(formatExamples).toHaveBeenCalledWith('overseer');
    expect(text(q('.fft-status'))).toBe('Loading examples…');
    expect(q('.fft-status')!.getAttribute('role')).toBe('status');
    expect(q('.fft-issues pre')).toBeNull();
    expect(q('.fft-download')).toBeNull();

    response.next(EXAMPLES);
    response.complete();
    fixture.detectChanges();

    expect(text(q('.fft-status'))).toBe('');
    expect(q('.fft-issues pre')!.textContent).toBe(EXAMPLES.issues.content);
    expect(q('.fft-resolutions pre')!.textContent).toBe(EXAMPLES.resolutions.content);
    expect(q('.fft-issues pre')!.getAttribute('tabindex')).toBe('0');
    expect(q('.fft-issues pre')!.getAttribute('aria-label')).toBe('Example issues file');
  });

  it('loads the examples once, however often the dialog opens', () => {
    formatExamples.mockReturnValue(of(EXAMPLES));
    create();
    openTip();
    closeTip();
    openTip();

    expect(formatExamples).toHaveBeenCalledTimes(1);
    expect(q('.fft-issues pre')).not.toBeNull();
  });

  it('shows the failure with a retry, and the retry loads the examples', () => {
    formatExamples.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500, error: { message: 'Server error.' } })));
    create();
    openTip();

    expect(q('.fft-error [role="alert"]')).not.toBeNull();
    expect(text(q('.fft-error [role="alert"]'))).toContain('The examples could not be loaded');
    expect(q('.fft-issues pre')).toBeNull();

    formatExamples.mockReturnValueOnce(of(EXAMPLES));
    q<HTMLButtonElement>('.fft-retry')!.click();
    fixture.detectChanges();

    expect(formatExamples).toHaveBeenCalledTimes(2);
    expect(q('.fft-error')).toBeNull();
    expect(q('.fft-issues pre')!.textContent).toBe(EXAMPLES.issues.content);
  });

  it('loads again on the next opening after a failure', () => {
    formatExamples.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })));
    create();
    openTip();
    closeTip();

    formatExamples.mockReturnValueOnce(of(EXAMPLES));
    openTip();
    expect(formatExamples).toHaveBeenCalledTimes(2);
    expect(q('.fft-resolutions pre')).not.toBeNull();
  });

  it('downloads each example under its own name with its own media type', async () => {
    const saved = captureSaves();
    formatExamples.mockReturnValue(of({
      ...EXAMPLES,
      resolutions: { ...EXAMPLES.resolutions, mediaType: 'text/plain;charset=utf-8' }
    }));
    create();
    openTip();

    const issues = q<HTMLButtonElement>('.fft-download-issues')!;
    const resolutions = q<HTMLButtonElement>('.fft-download-resolutions')!;
    expect(text(issues)).toBe('Download example issues file');
    expect(text(resolutions)).toBe('Download example resolutions file');

    issues.click();
    resolutions.click();

    expect(saved.names).toEqual([EXAMPLES.issues.fileName, EXAMPLES.resolutions.fileName]);
    expect(saved.blobs.map(b => b.type)).toEqual(['application/yaml;charset=utf-8', 'text/plain;charset=utf-8']);
    expect(await saved.blobs[0].text()).toBe(EXAMPLES.issues.content);
  });

  it('shows the resolution message example for the sub-tab, and asks for GnollBench examples', () => {
    formatExamples.mockReturnValue(of(EXAMPLES));
    create('gnollbench');
    expect(q('.fft-message pre')!.textContent!.split('\n')[0]).toMatch(/^GnollBench feedback 3f2a9c1b4d5e: /);
    expect(q('.fft-message pre')!.textContent).toContain('Resolved via: GnollHack wiki');
    openTip();
    expect(formatExamples).toHaveBeenCalledWith('gnollbench');
    expect(q('#gnollbench-feedback-formats')).not.toBeNull();

    const overseer = TestBed.createComponent(FeedbackFormatsTipComponent);
    overseer.componentRef.setInput('source', 'overseer');
    overseer.detectChanges();
    const message = (overseer.nativeElement as HTMLElement).querySelector('.fft-message pre')!.textContent!;
    expect(message.split('\n')[0]).toMatch(/^Feedback #42: /);
    overseer.destroy();
  });

  it('renders an example as plain text, never as HTML', () => {
    formatExamples.mockReturnValue(of({
      ...EXAMPLES,
      issues: { ...EXAMPLES.issues, content: '<img src=x onerror="alert(1)">' }
    }));
    create();
    openTip();
    expect(q('.fft-issues pre img')).toBeNull();
    expect(q('.fft-issues pre')!.textContent).toBe('<img src=x onerror="alert(1)">');
  });
});
