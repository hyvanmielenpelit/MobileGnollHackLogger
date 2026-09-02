import { Pipe, PipeTransform, ChangeDetectorRef, OnDestroy } from '@angular/core';
import { Subscription } from 'rxjs';
import { RelativeTimeTickerService } from '../services/relative-time-ticker.service';
import { parseServerUtcDate } from '../utils/date.util';

@Pipe({
  name: 'relativeTime',
  standalone: true,
  pure: false
})
export class RelativeTimePipe implements PipeTransform, OnDestroy {
  private rtf = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
  private tickerSub: Subscription | null = null;
  private lastFormattedTime = '';
  private lastValue: string | Date | null | undefined = undefined;

  constructor(
    private cdr: ChangeDetectorRef,
    private ticker: RelativeTimeTickerService
  ) {}

  transform(value: string | Date | null | undefined): string {
    if (!value) return '';
    
    // Store the value so the ticker can re-evaluate it
    this.lastValue = value;
    
    // Subscribe to the ticker on first use
    if (!this.tickerSub) {
      this.tickerSub = this.ticker.tick$.subscribe(() => {
        if (this.lastValue) {
          const newFormattedTime = this.calculateRelativeTime(this.lastValue);
          if (newFormattedTime !== this.lastFormattedTime) {
            this.lastFormattedTime = newFormattedTime;
            this.cdr.markForCheck();
          }
        }
      });
    }

    const currentFormattedTime = this.calculateRelativeTime(value);
    this.lastFormattedTime = currentFormattedTime;
    return currentFormattedTime;
  }

  private calculateRelativeTime(value: string | Date): string {
    const date = parseServerUtcDate(value);
    const now = new Date();
    // Use Math.max(0, ...) to clamp future dates (from clock skew) to 0
    const diffInSeconds = Math.max(0, Math.floor((now.getTime() - date.getTime()) / 1000));
    
    if (diffInSeconds < 60) {
      return 'just now';
    }
    
    const diffInMinutes = Math.floor(diffInSeconds / 60);
    if (diffInMinutes < 60) {
      return this.rtf.format(-diffInMinutes, 'minute');
    }
    
    const diffInHours = Math.floor(diffInMinutes / 60);
    if (diffInHours < 24) {
      return this.rtf.format(-diffInHours, 'hour');
    }
    
    const diffInDays = Math.floor(diffInHours / 24);
    if (diffInDays < 7) {
      return this.rtf.format(-diffInDays, 'day');
    }
    
    const diffInWeeks = Math.floor(diffInDays / 7);
    if (diffInDays < 30) {
      return this.rtf.format(-diffInWeeks, 'week');
    }

    const diffInMonths = Math.floor(diffInDays / 30);
    if (diffInDays < 365) {
      return this.rtf.format(-diffInMonths, 'month');
    }

    const diffInYears = Math.floor(diffInDays / 365);
    return this.rtf.format(-diffInYears, 'year');
  }

  ngOnDestroy(): void {
    if (this.tickerSub) {
      this.tickerSub.unsubscribe();
    }
  }
}
