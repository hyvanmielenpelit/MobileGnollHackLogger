import type { MockedObject } from 'vitest';
import { ChangeDetectorRef } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';
import { MarkdownPipe } from './markdown.pipe';
import { KatexLoaderService } from '../services/katex-loader.service';

// The spec run shares one module registry across files, so the unloaded state comes from a fresh service.
describe('MarkdownPipe before KaTeX has loaded', () => {
  it('shows TeX placeholders, loads KaTeX once and re-renders when it arrives', async () => {
    const sanitizer = {
      bypassSecurityTrustHtml: vi.fn((html: string) => html as any)
    } as unknown as MockedObject<DomSanitizer>;
    const loader = new KatexLoaderService();
    const cdr = { markForCheck: vi.fn() };
    const pipe = new MarkdownPipe(sanitizer, loader, cdr as unknown as ChangeDetectorRef);

    const first = pipe.transform('Area is $\\pi r^2$.') as string;
    expect(first).toContain('math-pending');
    expect(first).toContain('$\\pi r^2$');
    expect(first).not.toContain('katex');
    expect(cdr.markForCheck).not.toHaveBeenCalled();

    const second = pipe.transform('And $E = mc^2$ too.') as string;
    expect(second).toContain('math-pending');
    expect(second).toContain('$E = mc^2$');
    expect(second).not.toContain('katex');

    await loader.load();
    await Promise.resolve();
    expect(cdr.markForCheck).toHaveBeenCalledTimes(1);

    const upgraded = pipe.transform('And $E = mc^2$ too.') as string;
    expect(upgraded).toContain('katex');
    expect(upgraded).not.toContain('math-pending');
  });
});
