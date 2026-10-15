import { Marked } from 'marked';
import DOMPurify from 'dompurify';

/**
 * Report Markdown to HTML, for the Download Center's HTML files and the Report Pack dialog's
 * preview, so what is previewed is what is downloaded.
 *
 * The converter owns a private `Marked` instance and a private DOMPurify instance. It never uses the
 * global `marked` or the default DOMPurify export: `chat/markdown.pipe.ts` configures both on module
 * load (the KaTeX math extension, an image renderer, sanitize hooks), and its LLM-output repair
 * heuristics belong to the chat. A report is rendered by the server from stored figures, so it gets
 * plain GitHub-flavored Markdown: `$4 … $20` stays text, never math.
 *
 * The output is deterministic: no clock, no random ids, no environment values.
 */

type Purifier = typeof DOMPurify;

interface PrivateConverter {
  readonly marked: Marked;
  readonly purify: Purifier;
}

let converter: PrivateConverter | null = null;

/** Created on first use: a DOMPurify instance needs the window, which exists by then. */
function privateConverter(): PrivateConverter {
  if (!converter) {
    converter = {
      marked: new Marked({ gfm: true, breaks: false }),
      purify: DOMPurify(window)
    };
  }
  return converter;
}

/**
 * The sanitized HTML body of a report: the `html` profile only (no SVG or MathML), no `style`
 * attributes or elements, and no images, so a saved file never fetches anything when it is opened.
 * A block quote that opens with a GitHub alert marker becomes a callout (`withCallouts`).
 */
export function markdownToSafeHtmlFragment(markdown: string): string {
  const { marked, purify } = privateConverter();
  const html = marked.parse(markdown ?? '', { async: false }) as string;
  return withCallouts(purify.sanitize(html, {
    USE_PROFILES: { html: true },
    FORBID_ATTR: ['style'],
    FORBID_TAGS: ['style', 'img']
  }));
}

/** A callout kind, as its modifier class names it. */
type CalloutKind = 'note' | 'important' | 'tip' | 'warning';

/** The label each callout kind prints when its Markdown gives none. */
const CALLOUT_LABELS: Readonly<Record<CalloutKind, string>> = {
  note: 'Question',
  important: 'Key finding',
  tip: 'Conclusion',
  warning: 'Caveat'
};

/** A callout marker alone on the first line of a quote's first paragraph; group 1 is the kind. */
const CALLOUT_MARKER = /^\s*\[!(NOTE|IMPORTANT|TIP|WARNING)\][ \t]*(?:\r?\n|$)/i;

/**
 * Turns every `<blockquote>` of sanitized HTML whose first paragraph opens with a callout marker on a
 * line of its own into `<aside class="report-callout report-callout--<kind>" aria-label="<label>">`,
 * without the marker. The label is the bold run that opens the callout, its class
 * `report-callout__label`; when there is none, the kind's label is put there in bold. HTML without a
 * marker is returned unchanged. Only fixed class names and text nodes are added, so the result stays
 * as safe as its input.
 */
function withCallouts(html: string): string {
  if (!html.includes('[!')) {
    return html;
  }
  const template = document.createElement('template');
  template.innerHTML = html;
  let changed = false;
  for (const quote of Array.from(template.content.querySelectorAll('blockquote'))) {
    changed = toCallout(quote) || changed;
  }
  return changed ? template.innerHTML : html;
}

function toCallout(quote: HTMLQuoteElement): boolean {
  const first = quote.firstElementChild;
  if (!first || first.tagName !== 'P') {
    return false;
  }
  first.normalize();
  const marker = first.firstChild;
  if (!marker || marker.nodeType !== Node.TEXT_NODE) {
    return false;
  }
  const match = CALLOUT_MARKER.exec(marker.textContent ?? '');
  if (!match) {
    return false;
  }
  const kind = match[1].toLowerCase() as CalloutKind;
  const doc = quote.ownerDocument;

  marker.textContent = (marker.textContent ?? '').slice(match[0].length).replace(/^\s+/, '');
  if (marker.textContent === '') {
    marker.remove();
  }
  if (first.childNodes.length === 0 || (first.children.length === 0 && (first.textContent ?? '').trim() === '')) {
    first.remove();
  }

  // The label is the bold run the lead paragraph opens with.
  let lead = quote.firstElementChild;
  const opening = lead?.tagName === 'P'
    ? Array.from(lead.childNodes).find(n => !(n.nodeType === Node.TEXT_NODE && (n.textContent ?? '').trim() === ''))
    : undefined;
  let label: Element;
  if (opening && opening.nodeType === Node.ELEMENT_NODE && ['STRONG', 'B'].includes((opening as Element).tagName)) {
    label = opening as Element;
  } else {
    if (!lead || lead.tagName !== 'P') {
      lead = doc.createElement('p');
      quote.insertBefore(lead, quote.firstChild);
    }
    const hasText = (lead.textContent ?? '').trim() !== '';
    label = doc.createElement('strong');
    label.textContent = `${CALLOUT_LABELS[kind]}.`;
    if (hasText) {
      lead.insertBefore(doc.createTextNode(' '), lead.firstChild);
    }
    lead.insertBefore(label, lead.firstChild);
  }
  label.classList.add('report-callout__label');
  const name = (label.textContent ?? '').replace(/[\s.:]+$/, '').trim() || CALLOUT_LABELS[kind];

  const aside = doc.createElement('aside');
  aside.className = `report-callout report-callout--${kind}`;
  aside.setAttribute('aria-label', name);
  while (quote.firstChild) {
    aside.appendChild(quote.firstChild);
  }
  quote.replaceWith(aside);
  return true;
}

/** A self-contained, printable HTML page holding the report, titled `title`. */
export function markdownToPrintableHtml(markdown: string, title: string): string {
  const body = markdownToSafeHtmlFragment(markdown);
  return [
    '<!DOCTYPE html>',
    '<html lang="en">',
    '<head>',
    '<meta charset="utf-8">',
    '<meta name="viewport" content="width=device-width, initial-scale=1">',
    `<title>${escapeHtml(title)}</title>`,
    '<style>',
    PRINT_CSS,
    '</style>',
    '</head>',
    '<body>',
    '<main>',
    body.trim(),
    '</main>',
    '</body>',
    '</html>',
    ''
  ].join('\n');
}

/** Escapes text for an HTML text node or a quoted attribute. */
export function escapeHtml(text: string): string {
  return (text ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

/** A4, readable type, bordered tables whose header repeats on every printed page, and callouts that keep their colors in print. */
const PRINT_CSS = [
  '@page { size: A4; margin: 18mm 16mm; }',
  ':root { color-scheme: light; }',
  'body { margin: 0; background: #fff; color: #1b1b1b; font: 11pt/1.55 Georgia, "Times New Roman", serif; }',
  'main { max-width: 50rem; margin: 0 auto; padding: 2rem 1.5rem 3rem; }',
  'h1, h2, h3, h4, h5, h6 { font-family: "Segoe UI", "Helvetica Neue", Arial, sans-serif; line-height: 1.25; margin: 1.4em 0 0.5em; break-after: avoid; }',
  'h1 { font-size: 1.7em; margin-top: 0; }',
  'h2 { font-size: 1.35em; border-bottom: 1px solid #ccc; padding-bottom: 0.2em; }',
  'h3 { font-size: 1.15em; }',
  'p, ul, ol, blockquote, table, pre { margin: 0 0 0.9em; }',
  'li + li { margin-top: 0.2em; }',
  'a { color: #1a4f8b; overflow-wrap: anywhere; }',
  'table { border-collapse: collapse; width: 100%; font-size: 0.9em; }',
  'th, td { border: 1px solid #9a9a9a; padding: 4px 8px; text-align: left; vertical-align: top; }',
  'th { background: #eeeeee; font-family: "Segoe UI", "Helvetica Neue", Arial, sans-serif; }',
  'thead { display: table-header-group; }',
  'tr { break-inside: avoid; }',
  'code { font-family: Consolas, "Courier New", monospace; font-size: 0.9em; }',
  'pre { white-space: pre-wrap; overflow-wrap: anywhere; background: #f5f5f5; border: 1px solid #ddd; border-radius: 4px; padding: 8px 10px; }',
  'pre code { font-size: 0.85em; }',
  'blockquote { margin-left: 0; padding: 0.2em 0 0.2em 1em; border-left: 3px solid #999; color: #3d3d3d; }',
  // Callouts: the document palette's rule and tint per kind, kept when printed.
  '.report-callout { margin: 0 0 0.9em; padding: 0.5em 0.9em 0.5em 0.85em; border-left: 3px solid #2f6fb5; background: #eaf2fb; color: #1b1b1b; break-inside: avoid; -webkit-print-color-adjust: exact; print-color-adjust: exact; }',
  '.report-callout > :last-child { margin-bottom: 0; }',
  '.report-callout__label { font-weight: 700; }',
  '.report-callout--note { border-left-color: #2f6fb5; background: #eaf2fb; }',
  '.report-callout--important { border-left-color: #7048b8; background: #f2edfa; }',
  '.report-callout--tip { border-left-color: #2b7a4b; background: #e8f4ec; }',
  '.report-callout--warning { border-left-color: #9a6200; background: #fff4d6; }',
  'hr { border: 0; border-top: 1px solid #ccc; margin: 1.5em 0; }',
  '@media print { main { max-width: none; padding: 0; } a { color: inherit; } }'
].join('\n');
