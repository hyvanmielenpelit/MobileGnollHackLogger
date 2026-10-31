import { Directive, ElementRef, NgZone, OnDestroy, OnInit, inject } from '@angular/core';

import { DragAutoscroller, findScroller } from './drag-autoscroll';

/** Longer than the 550 ms the HTML specification allows between `dragover` events over the page. */
const DRAGOVER_WATCHDOG_MS = 750;

/**
 * Autoscrolls the nearest scroller during a native HTML5 drag that starts inside the host. It only
 * observes the drag events, so the list's own handlers behave as if it were not there.
 */
@Directive({
  selector: '[appDragAutoscroll]',
  standalone: true
})
export class DragAutoscrollDirective implements OnInit, OnDestroy {
  private host = inject<ElementRef<HTMLElement>>(ElementRef);
  private zone = inject(NgZone);

  private autoscroller: DragAutoscroller | null = null;
  private watchdog: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    this.zone.runOutsideAngular(() => {
      this.host.nativeElement.addEventListener('dragstart', this.onDragStart);
    });
  }

  ngOnDestroy(): void {
    this.endSession();
    this.host.nativeElement.removeEventListener('dragstart', this.onDragStart);
  }

  private onDragStart = (event: DragEvent): void => {
    this.endSession();
    if (event.defaultPrevented) {
      return;
    }
    this.autoscroller = new DragAutoscroller(findScroller(this.host.nativeElement), event.clientY);
    // On document, because the pointer may be outside the list. `drop` is here as well as `dragend`
    // because a `dragend` sent to a source row the list has re-rendered away does not reach document.
    document.addEventListener('dragover', this.onDragOver, true);
    document.addEventListener('dragend', this.onDragEnd, true);
    document.addEventListener('drop', this.onDragEnd, true);
  };

  private onDragOver = (event: DragEvent): void => {
    if (event.clientX === 0 && event.clientY === 0) {
      return;
    }
    this.autoscroller?.update(event.clientY);
    this.clearWatchdog();
    // No `dragover` arrives once the pointer has left the window.
    this.watchdog = setTimeout(() => {
      this.watchdog = null;
      this.autoscroller?.pause();
    }, DRAGOVER_WATCHDOG_MS);
  };

  private onDragEnd = (): void => {
    this.endSession();
  };

  private endSession(): void {
    this.autoscroller?.stop();
    this.autoscroller = null;
    this.clearWatchdog();
    document.removeEventListener('dragover', this.onDragOver, true);
    document.removeEventListener('dragend', this.onDragEnd, true);
    document.removeEventListener('drop', this.onDragEnd, true);
  }

  private clearWatchdog(): void {
    if (this.watchdog !== null) {
      clearTimeout(this.watchdog);
      this.watchdog = null;
    }
  }
}
