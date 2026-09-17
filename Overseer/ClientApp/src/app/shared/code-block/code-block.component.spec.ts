import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { COPIED_MS, CodeBlockComponent } from './code-block.component';

describe('CodeBlockComponent', () => {
  let fixture: ComponentFixture<CodeBlockComponent>;
  let component: CodeBlockComponent;
  let host: HTMLElement;

  const CODE = 'format: overseer-benchmark-questions\n    indented: kept\n';

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CodeBlockComponent] }).compileComponents();
    fixture = TestBed.createComponent(CodeBlockComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('code', CODE);
    fixture.componentRef.setInput('idPrefix', 'cb-test');
    fixture.componentRef.setInput('subject', 'the sample');
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  const buttons = (): HTMLButtonElement[] => Array.from(host.querySelectorAll<HTMLButtonElement>('button'));

  function withClipboard(reject: boolean): { writeText: jasmine.Spy; restore: () => void } {
    const writeText = jasmine.createSpy('writeText')
      .and.returnValue(reject ? Promise.reject(new Error('denied')) : Promise.resolve());
    const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true, writable: true });
    return {
      writeText,
      restore: () => {
        delete (navigator as { clipboard?: unknown }).clipboard;
        if (original) Object.defineProperty(navigator, 'clipboard', original);
      }
    };
  }

  it('shows the exact text, leading spaces intact, as text and not HTML', () => {
    fixture.componentRef.setInput('code', '<b>not bold</b>\n  two spaces');
    fixture.detectChanges();
    const code = host.querySelector('pre code')!;
    expect(code.textContent).toBe('<b>not bold</b>\n  two spaces');
    expect(code.querySelector('b')).toBeNull();
  });

  it('without a caption has one corner copy button and no header', () => {
    expect(host.querySelector('.code-block-header')).toBeNull();
    expect(buttons().length).toBe(1);
    expect(host.querySelector('.code-block-corner .code-block-copy')).not.toBeNull();
    expect(host.querySelector('pre')!.classList).toContain('has-corner-button');
  });

  it('names its buttons after the subject and anchors the tooltips on both ends', () => {
    fixture.componentRef.setInput('caption', 'sample.yaml');
    fixture.componentRef.setInput('downloadName', 'sample.yaml');
    fixture.detectChanges();

    expect(host.querySelector('.code-block-caption')!.textContent).toBe('sample.yaml');
    expect(buttons().map(b => b.getAttribute('aria-label')))
      .toEqual(['Download the sample', 'Copy the sample to the clipboard']);
    for (const b of buttons()) {
      expect(b.getAttribute('type')).toBe('button');
      expect(b.hasAttribute('title')).toBeFalse();
      const tipId = b.getAttribute('interestfor')!;
      expect(b.getAttribute('style')).toBe(`anchor-name: --${tipId}`);
      expect(host.querySelector(`#${tipId}`)!.getAttribute('style')).toBe(`position-anchor: --${tipId}`);
    }
  });

  it('offers no Download without a caption', () => {
    fixture.componentRef.setInput('downloadName', 'sample.yaml');
    fixture.detectChanges();
    expect(host.querySelector('.code-block-download')).toBeNull();
  });

  it('copies the text, swaps to the check glyph and announces it, then reverts', fakeAsync(() => {
    const clip = withClipboard(false);
    try {
      host.querySelector<HTMLButtonElement>('.code-block-copy')!.click();
      flushMicrotasks();
      fixture.detectChanges();

      expect(clip.writeText).toHaveBeenCalledWith(CODE);
      expect(host.querySelector('[role="status"]')!.textContent).toBe('Copied');
      expect(host.querySelector('.code-block-copy polyline')).not.toBeNull();

      tick(COPIED_MS);
      fixture.detectChanges();
      expect(host.querySelector('[role="status"]')!.textContent).toBe('');
      expect(host.querySelector('.code-block-copy polyline')).toBeNull();
    } finally {
      clip.restore();
    }
  }));

  it('says visibly when the clipboard refuses, pointing at Download when there is one', fakeAsync(() => {
    const clip = withClipboard(true);
    try {
      component.copy();
      flushMicrotasks();
      fixture.detectChanges();
      expect(host.querySelector('.code-block-error')!.textContent).toContain('select the text instead');

      fixture.componentRef.setInput('caption', 'sample.yaml');
      fixture.componentRef.setInput('downloadName', 'sample.yaml');
      fixture.detectChanges();
      expect(host.querySelector('.code-block-error')!.textContent).toContain('use Download instead');
    } finally {
      clip.restore();
    }
  }));
});
