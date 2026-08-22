import { Pipe, PipeTransform, inject } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { marked, MarkedExtension } from 'marked';
import katex, { KatexOptions } from 'katex';
import DOMPurify from 'dompurify';

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
          try {
            return katex.renderToString(token.text, { ...katexOptions, displayMode: true }) + '\n';
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
          try {
            return katex.renderToString(token.text, { ...katexOptions, displayMode: token.displayMode ?? false });
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

@Pipe({
  name: 'markdown',
  standalone: true
})
export class MarkdownPipe implements PipeTransform {
  constructor(private sanitizer: DomSanitizer) {}

  transform(value: string): SafeHtml | string {
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
            line = line.replace(/([^\s\*\_\`\(\[\{\<A-Z][\.\!\?])([A-Z])/g, '$1 $2');
          } else {
            // Normal non-table line

            // Fix missing newlines before headings (e.g. LLM outputs "TEXT#### HEADING")
            // Only apply if the # is preceded by a non-newline character and followed by a space
            line = line.replace(/([^\n])(#{1,6}\s+)/g, (match, p1, p2, offset, str) => {
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
            // This prevents splitting terms like "**.NET", "(.NET", or "ASP.NET" into "**.\n\nNET"
            line = line.replace(/([^\s\*\_\`\(\[\{\<A-Z][\.\!\?])([A-Z])/g, '$1\n\n$2');
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

    const parsed = marked.parse(processed);
    // marked.parse can return a Promise if async options are used, but by default it returns a string
    const html = typeof parsed === 'string' ? parsed : '';
    
    const purified = DOMPurify.sanitize(html, {
      USE_PROFILES: { mathMl: true, html: true },
      ADD_TAGS: ['annotation', 'semantics'],
      ADD_ATTR: ['encoding', 'class', 'style', 'aria-hidden', 'tabindex']
    });

    return this.sanitizer.bypassSecurityTrustHtml(purified);
  }
}
