import { ChangeDetectionStrategy, Component, ElementRef, EventEmitter, HostListener, Input, OnChanges, OnDestroy, Output, SimpleChanges } from '@angular/core';

export type ToastKind = 'success' | 'error' | 'info';

/** One message to show. A new `id` re-shows the toast even when the text repeats. */
export interface ToastNotice {
  id: number;
  kind: ToastKind;
  message: string;
  /** Milliseconds before a success or info notice closes itself. Ignored for errors. */
  durationMs?: number;
}

const DEFAULT_DURATION_MS = 6000;

/**
 * A single popover toast, always present in the DOM so its live region exists
 * before its text changes -- some screen readers do not announce a region that
 * appears already carrying its first text.
 *
 * `popover="manual"` so it has no light dismiss and can coexist with the modal
 * dialogs it is rendered inside.
 */
@Component({
  selector: 'app-toast',
  standalone: true,
  templateUrl: './toast.component.html',
  styleUrl: './toast.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    'class': 'toast',
    'popover': 'manual',
    '[class.toast--success]': "notice?.kind === 'success'",
    '[class.toast--error]': "notice?.kind === 'error'",
    '[class.toast--info]': "notice?.kind === 'info'",
    '[attr.role]': "notice?.kind === 'error' ? 'alert' : 'status'",
    '[attr.aria-live]': "notice?.kind === 'error' ? 'assertive' : 'polite'",
    'aria-atomic': 'true'
  }
})
export class ToastComponent implements OnChanges, OnDestroy {
  @Input() notice: ToastNotice | null = null;
  @Output() dismissed = new EventEmitter<void>();

  private timeout: ReturnType<typeof setTimeout> | null = null;
  private remainingMs = DEFAULT_DURATION_MS;

  constructor(private readonly host: ElementRef<HTMLElement>) {}

  ngOnChanges(changes: SimpleChanges): void {
    const noticeChange = changes['notice'];
    if (!noticeChange) {
      return;
    }

    const previous = noticeChange.previousValue as ToastNotice | null;
    const current = noticeChange.currentValue as ToastNotice | null;

    if (current && (!previous || previous.id !== current.id)) {
      this.show(current);
    } else if (!current) {
      this.stopTimer();
    }
  }

  ngOnDestroy(): void {
    this.stopTimer();
  }

  @HostListener('mouseenter')
  @HostListener('focusin')
  onPause(): void {
    this.stopTimer();
  }

  @HostListener('mouseleave')
  @HostListener('focusout')
  onResume(): void {
    if (this.notice && this.notice.kind !== 'error') {
      this.startTimer(this.remainingMs);
    }
  }

  /** Hides the toast: clears the timer, closes the popover, and emits `dismissed`. */
  hide(): void {
    this.stopTimer();
    const el = this.host.nativeElement as any;
    if (el && 'hidePopover' in el) {
      try {
        if (el.matches(':popover-open')) {
          el.hidePopover();
        }
      } catch {
        try { el.hidePopover(); } catch { /* no-op */ }
      }
    }
    this.dismissed.emit();
  }

  private show(notice: ToastNotice): void {
    const el = this.host.nativeElement as any;
    if (el && 'showPopover' in el) {
      try {
        if (!el.matches(':popover-open')) {
          el.showPopover();
        }
      } catch {
        try { el.showPopover(); } catch { /* no-op */ }
      }
    }

    this.remainingMs = notice.durationMs ?? DEFAULT_DURATION_MS;
    // Errors and refusals never auto-dismiss (WCAG 2.2.1 Timing Adjustable): the
    // message tells the user something they must act on.
    if (notice.kind !== 'error') {
      this.startTimer(this.remainingMs);
    } else {
      this.stopTimer();
    }
  }

  private startTimer(durationMs: number): void {
    this.stopTimer();
    this.timeout = setTimeout(() => this.hide(), durationMs);
  }

  private stopTimer(): void {
    if (this.timeout !== null) {
      clearTimeout(this.timeout);
      this.timeout = null;
    }
  }
}
