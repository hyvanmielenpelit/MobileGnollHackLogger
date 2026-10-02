import { Marked, marked } from 'marked';
import DOMPurify from 'dompurify';

// Loaded for its side effects: it configures the global `marked` (KaTeX, image renderer) and the
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
    // The global instance renders inline math once the pipe is loaded; the converter must not.
    expect(marked.parse('Inline $x+y$ math.', { async: false }) as string).toContain('katex');

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
});
