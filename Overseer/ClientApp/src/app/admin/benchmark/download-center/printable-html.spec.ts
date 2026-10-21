import { Marked, marked } from 'marked';
import DOMPurify from 'dompurify';

// Loaded for its side effects: it configures the global `marked` (math extension, image renderer) and the
// default DOMPurify instance (sanitize hooks) on import, which the converter must not see.
import { MARKDOWN_IMAGE_DEFANG_HOOKS_INSTALLED } from '../../../chat/markdown.pipe';
import { escapeHtml, markdownToPrintableHtml, markdownToSafeHtmlFragment } from './printable-html';

describe('printable-html', () => {
  const report = [
    '# Executive Summary: Model X',
    '',
    'The run cost $4 per question and $20 in total.',
    '',
    '| Model | Quality |',
    '|---|---|',
    '| Model X | 80 ± 3 |',
    '| Model A | 74 ± 4 |',
    '',
    '```',
    'a very long line of code that must wrap when printed rather than run off the page',
    '```',
    ''
  ].join('\n');

  it('gives identical output for identical input', () => {
    expect(markdownToPrintableHtml(report, 'Report')).toBe(markdownToPrintableHtml(report, 'Report'));
    expect(markdownToSafeHtmlFragment(report)).toBe(markdownToSafeHtmlFragment(report));
  });

  it('is unaffected by the chat pipe configuring the global marked and DOMPurify', () => {
    expect(MARKDOWN_IMAGE_DEFANG_HOOKS_INSTALLED).toBe(true);
    // The global instance carries the pipe's math extension, which renders the TeX placeholder
    // outside a pipe transform; the converter must not.
    expect(marked.parse('Inline $x+y$ math.', { async: false }) as string).toContain('math-pending');

    const pristine = DOMPurify(window).sanitize(
      new Marked({ gfm: true, breaks: false }).parse(report, { async: false }) as string,
      { USE_PROFILES: { html: true }, FORBID_ATTR: ['style'], FORBID_TAGS: ['style', 'img'] });
    expect(markdownToSafeHtmlFragment(report)).toBe(pristine);

    const math = markdownToSafeHtmlFragment('Inline $x+y$ math.');
    expect(math).not.toContain('katex');
    expect(math).toContain('$x+y$');
  });

  it('keeps a dollar cost line as text, not math', () => {
    const html = markdownToSafeHtmlFragment('The run cost $4 per question and $20 in total.');

    expect(html.trim()).toBe('<p>The run cost $4 per question and $20 in total.</p>');
    expect(html).not.toContain('katex');
  });

  it('strips scripts, event handlers, style attributes and style elements', () => {
    const html = markdownToSafeHtmlFragment([
      '<script>alert(1)</script>',
      '',
      '<p onclick="steal()" style="color: red">Hello</p>',
      '',
      '<style>body { display: none; }</style>',
      '',
      '<img src="https://example.invalid/pixel.png" onerror="alert(2)">',
      '',
      '<svg><circle r="4"></circle></svg>',
      ''
    ].join('\n'));

    expect(html).not.toContain('<script');
    expect(html).not.toContain('alert(1)');
    expect(html).not.toContain('onclick');
    expect(html).not.toContain('onerror');
    expect(html).not.toContain('style');
    expect(html).not.toContain('display: none');
    expect(html).not.toContain('<img');
    expect(html).not.toContain('<svg');
    expect(html).toContain('Hello');
  });

  it('renders tables', () => {
    const html = markdownToSafeHtmlFragment(report);

    expect(html).toContain('<table>');
    expect(html).toContain('<thead>');
    expect(html).toContain('<th>Quality</th>');
    expect(html).toContain('<td>80 ± 3</td>');
  });

  it('builds a self-contained printable page with an escaped title', () => {
    const page = markdownToPrintableHtml(report, 'Model <X> & "friends"');

    expect(page.startsWith('<!DOCTYPE html>\n<html lang="en">')).toBe(true);
    expect(page).toContain('<meta charset="utf-8">');
    expect(page).toContain('<title>Model &lt;X&gt; &amp; &quot;friends&quot;</title>');
    expect(page).toContain('@page { size: A4;');
    expect(page).toContain('tr { break-inside: avoid; }');
    expect(page).toContain('thead { display: table-header-group; }');
    expect(page).toContain('white-space: pre-wrap');
    expect(page).toContain('<h1>Executive Summary: Model X</h1>');
    expect(page).not.toMatch(/<(script|link|img)\b/);
    expect(page).not.toMatch(/src=|href="http/);
  });

  it('carries no timestamp, random id or environment value', () => {
    const page = markdownToPrintableHtml(report, 'Report');

    expect(page).not.toMatch(/\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/);
    expect(page).not.toMatch(/\sid="/);
    expect(page).not.toContain(window.location.host);
    expect(page).not.toContain(navigator.userAgent);
  });

  it('escapes every HTML-significant character', () => {
    expect(escapeHtml(`<a href="x">'&'</a>`)).toBe('&lt;a href=&quot;x&quot;&gt;&#39;&amp;&#39;&lt;/a&gt;');
  });

  describe('callouts', () => {
    /** The fragment parsed into a detached body, to query. */
    function parse(html: string): HTMLElement {
      return new DOMParser().parseFromString(`<!DOCTYPE html><body>${html}</body>`, 'text/html').body;
    }

    const kinds: ReadonlyArray<readonly [string, string, string]> = [
      ['NOTE', 'note', 'Question'],
      ['IMPORTANT', 'important', 'Key finding'],
      ['TIP', 'tip', 'Conclusion'],
      ['WARNING', 'warning', 'Caveat']
    ];

    for (const [marker, modifier, label] of kinds) {
      it(`turns a [!${marker}] quote into a labeled ${modifier} callout without the marker`, () => {
        const html = markdownToSafeHtmlFragment(`> [!${marker}]\n> **${label}.** The headline text.\n>\n> A second paragraph.\n`);
        const body = parse(html);
        const aside = body.querySelector('aside');

        expect(html).not.toContain(`[!${marker}]`);
        expect(body.querySelector('blockquote')).toBeNull();
        expect(aside).not.toBeNull();
        expect(aside!.className).toBe(`report-callout report-callout--${modifier}`);
        expect(aside!.getAttribute('aria-label')).toBe(label);
        const paragraphs = aside!.querySelectorAll('p');
        expect(paragraphs.length).toBe(2);
        expect(paragraphs[0].firstElementChild?.tagName).toBe('STRONG');
        expect(paragraphs[0].firstElementChild?.classList.contains('report-callout__label')).toBe(true);
        expect(paragraphs[0].textContent).toBe(`${label}. The headline text.`);
        expect(paragraphs[1].textContent).toBe('A second paragraph.');
      });
    }

    it('names a callout after its own label', () => {
      const aside = parse(markdownToSafeHtmlFragment('> [!NOTE]\n> **Not tested by code.** The claim rests on the wiki.\n')).querySelector('aside');

      expect(aside!.getAttribute('aria-label')).toBe('Not tested by code');
      expect(aside!.querySelector('.report-callout__label')!.textContent).toBe('Not tested by code.');
    });

    it('prints the kind\'s label when the callout has none of its own', () => {
      const aside = parse(markdownToSafeHtmlFragment('> [!WARNING]\n> The numbers are small.\n')).querySelector('aside');

      expect(aside!.className).toBe('report-callout report-callout--warning');
      expect(aside!.getAttribute('aria-label')).toBe('Caveat');
      expect(aside!.querySelector('strong.report-callout__label')!.textContent).toBe('Caveat.');
      expect(aside!.querySelector('p')!.textContent).toBe('Caveat. The numbers are small.');
    });

    it('takes the label from the next paragraph when the marker stands alone', () => {
      const html = markdownToSafeHtmlFragment('> [!TIP]\n>\n> **Conclusion.** Ship it.\n');
      const aside = parse(html).querySelector('aside');

      expect(html).not.toContain('[!TIP]');
      expect(aside!.querySelectorAll('p').length).toBe(1);
      expect(aside!.getAttribute('aria-label')).toBe('Conclusion');
      expect(aside!.querySelector('p')!.textContent).toBe('Conclusion. Ship it.');
    });

    it('leaves an ordinary quote, an inline marker and an unknown kind as quotes', () => {
      const markdown = '> A quoted paragraph.\n\n> [!NOTE] inline text\n\n> [!CAUTION]\n> Not a callout kind.\n';
      const html = markdownToSafeHtmlFragment(markdown);
      const pristine = DOMPurify(window).sanitize(
        new Marked({ gfm: true, breaks: false }).parse(markdown, { async: false }) as string,
        { USE_PROFILES: { html: true }, FORBID_ATTR: ['style'], FORBID_TAGS: ['style', 'img'] });

      expect(html).toBe(pristine);
      expect(parse(html).querySelectorAll('blockquote').length).toBe(3);
      expect(html).not.toContain('<aside');
    });

    it('styles callouts for print in the printable page', () => {
      const page = markdownToPrintableHtml('> [!IMPORTANT]\n> **Key finding.** Text.\n', 'Report');

      expect(page).toContain('<aside class="report-callout report-callout--important" aria-label="Key finding">');
      expect(page).toContain('print-color-adjust: exact');
      expect(page).toContain('-webkit-print-color-adjust: exact');
      for (const modifier of ['note', 'important', 'tip', 'warning']) {
        expect(page).toContain(`.report-callout--${modifier} { border-left-color: #`);
      }
      expect(page).not.toContain('[!IMPORTANT]');
    });
  });
});
