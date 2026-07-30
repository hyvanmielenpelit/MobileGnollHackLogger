import { Pipe, PipeTransform } from '@angular/core';
import { marked } from 'marked';
import DOMPurify from 'dompurify';

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
    
    const parsed = marked.parse(processed);
    // marked.parse can return a Promise if async options are used, but by default it returns a string
    const html = typeof parsed === 'string' ? parsed : '';
    return DOMPurify.sanitize(html);
  }
}
