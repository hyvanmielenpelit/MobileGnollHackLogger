import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'relativeTime',
  standalone: true,
  pure: false
})
export class RelativeTimePipe implements PipeTransform {
  transform(value: string | Date | null | undefined, mode: 'chat' | 'changelog' = 'chat'): string {
    if (!value) return '';
    
    let dateStr = value;
    if (typeof dateStr === 'string' && !dateStr.endsWith('Z') && !dateStr.includes('+') && !dateStr.match(/-\d{2}:\d{2}$/)) {
      // Don't append 'Z' for date-only strings (YYYY-MM-DD), which parse as UTC correctly
      if (dateStr.length > 10 || dateStr.includes('T') || dateStr.includes(' ')) {
        dateStr += 'Z';
      }
    }
    
    const date = new Date(dateStr);
    const now = new Date();
    const diffInSeconds = Math.floor((now.getTime() - date.getTime()) / 1000);
    
    if (mode === 'changelog') {
      const diffInDays = Math.floor(diffInSeconds / (24 * 3600));
      if (diffInDays === 0) return 'Today';
      if (diffInDays < 7) return `${diffInDays}d ago`;
      
      const diffInWeeks = Math.floor(diffInDays / 7);
      if (diffInDays < 30) return `${diffInWeeks}w ago`;
      
      const diffInMonths = Math.floor(diffInDays / 30);
      if (diffInDays < 365) return `${diffInMonths}mo ago`;
      
      const diffInYears = Math.floor(diffInDays / 365);
      return `${diffInYears}y ago`;
    }
    
    // For future dates (e.g., slightly off clocks) or very recent
    if (diffInSeconds < 60) {
      return 'just now';
    }
    
    const diffInMinutes = Math.floor(diffInSeconds / 60);
    if (diffInMinutes < 60) {
      return `${diffInMinutes} min ago`;
    }
    
    const diffInHours = Math.floor(diffInMinutes / 60);
    if (diffInHours < 24) {
      return `${diffInHours} hr ago`;
    }
    
    const diffInDays = Math.floor(diffInHours / 24);
    if (diffInDays < 7) {
      return `${diffInDays} day${diffInDays > 1 ? 's' : ''} ago`;
    }
    
    // For older messages, use absolute date
    return date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
  }
}
