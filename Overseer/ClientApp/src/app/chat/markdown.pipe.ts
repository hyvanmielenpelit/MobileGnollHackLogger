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
    // Fix missing newlines before headings (e.g. LLM outputs "TEXT#### HEADING")
    // Only apply if the # is preceded by a non-newline character and followed by a space
    let processed = value.replace(/([^\n])(#{1,6}\s+)/g, '$1\n\n$2');
    
    // Fix missing newlines before lists following a colon (e.g. "text):1. Item")
    processed = processed.replace(/([a-zA-Z0-9\)]):\s*(\d+\.\s+)/g, '$1:\n\n$2');

    const parsed = marked.parse(processed);
    // marked.parse can return a Promise if async options are used, but by default it returns a string
    const html = typeof parsed === 'string' ? parsed : '';
    
    return DOMPurify.sanitize(html, {
      USE_PROFILES: { mathMl: true, html: true },
      ADD_TAGS: ['annotation', 'semantics'],
      ADD_ATTR: ['encoding']
    });
  }
}
