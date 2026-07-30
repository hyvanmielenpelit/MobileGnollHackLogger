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
    const parsed = marked.parse(value);
    // marked.parse can return a Promise if async options are used, but by default it returns a string
    const html = typeof parsed === 'string' ? parsed : '';
    return DOMPurify.sanitize(html);
  }
}
