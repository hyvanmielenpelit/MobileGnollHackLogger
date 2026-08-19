import { Pipe, PipeTransform } from '@angular/core';
import { marked } from 'marked';
import markedKatex from 'marked-katex-extension';
import DOMPurify from 'dompurify';

// Register KaTeX extension once at module load
marked.use(markedKatex({ throwOnError: false, nonStandard: true }));

@Pipe({
  name: 'markdown',
  standalone: true
})
export class MarkdownPipe implements PipeTransform {
  transform(value: string): string {
    if (!value) return '';
    // Split the string into code blocks/inline code and normal text
    // This ensures we don't accidentally modify code snippets
    const codeRegex = /(```[\s\S]*?```|`[^`]*`)/g;
    const parts = value.split(codeRegex);

    for (let i = 0; i < parts.length; i++) {
      // Even indices are normal text, odd indices are code blocks/inline code
      if (i % 2 === 0) {
        // Fix missing newlines before headings (e.g. LLM outputs "TEXT#### HEADING")
        // Only apply if the # is preceded by a non-newline character and followed by a space
        parts[i] = parts[i].replace(/([^\n])(#{1,6}\s+)/g, (match, p1, p2, offset, str) => {
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
        parts[i] = parts[i].replace(/([a-zA-Z0-9\)]):\s*(\d+\.\s+)/g, '$1:\n\n$2');
        
        // Fix squished sentences (e.g., LLM outputs "word.Next word" without a space)
        // Matches any non-whitespace (excluding markdown formatting characters *, _, `, opening brackets (, [, {, <, and uppercase letters A-Z), a punctuation mark (., !, ?), and a capital letter
        // This prevents splitting terms like "**.NET", "(.NET", or "ASP.NET" into "**.\n\nNET"
        parts[i] = parts[i].replace(/([^\s\*\_\`\(\[\{\<A-Z][\.\!\?])([A-Z])/g, '$1\n\n$2');

        // Fix missing newlines before code blocks (e.g. LLM outputs "text.```c")
        if (i + 1 < parts.length && parts[i].length > 0 && !parts[i].endsWith('\n') && parts[i + 1].startsWith('```')) {
          parts[i] += '\n\n';
        }

        // Fix missing newlines after code blocks (e.g. LLM outputs "```Text")
        if (i > 0 && parts[i].length > 0 && !parts[i].startsWith('\n') && parts[i - 1].startsWith('```')) {
          parts[i] = '\n\n' + parts[i];
        }
      }
    }
    
    let processed = parts.join('');

    const parsed = marked.parse(processed);
    // marked.parse can return a Promise if async options are used, but by default it returns a string
    const html = typeof parsed === 'string' ? parsed : '';
    
    return DOMPurify.sanitize(html, {
      USE_PROFILES: { mathMl: true, html: true },
      ADD_TAGS: ['annotation', 'semantics'],
      ADD_ATTR: ['encoding', 'class']
    });
  }
}
