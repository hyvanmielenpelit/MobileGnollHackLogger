/** Edge zone size as a fraction of the scroller's visible height. */
const ZONE_FRACTION = 0.25;
/** Edge zone size limits, in CSS px. */
const ZONE_MIN_PX = 48;
const ZONE_MAX_PX = 160;
/** An edge zone never exceeds this fraction of the visible height, so a neutral middle always remains. */
const ZONE_MAX_FRACTION = 1 / 3;
/** Speed on entering the zone, in px/s. */
const SLOWEST_SPEED = 40;
/** Top speed in visible heights per second. */
const TOP_SPEED_HEIGHTS = 1.25;
/** Top speed limits, in px/s. */
const TOP_SPEED_MIN = 480;
const TOP_SPEED_MAX = 900;
/** Fraction of the zone's depth at which top speed is reached. */
const FULL_SPEED_DEPTH = 0.8;
/** Time from standstill to full speed, in ms. */
const RAMP_MS = 400;
/** Pointer travel from the drag's start, in CSS px, that arms the direction it moved in. */
const ARM_PX = 8;
/** The longest frame time counted, in ms, so a stall cannot cause a jump. */
const MAX_FRAME_MS = 50;

/** Scroll velocity in px/s for a pointer position; negative is up, 0 outside the edge zones. */
export function autoscrollVelocity(pointerY: number, top: number, bottom: number, reducedMotion: boolean): number {
  const height = bottom - top;
  if (height <= 0) {
    return 0;
  }
  const zone = Math.min(height * ZONE_MAX_FRACTION, Math.max(ZONE_MIN_PX, Math.min(ZONE_MAX_PX, height * ZONE_FRACTION)));
  const upDepth = top + zone - pointerY;
  const downDepth = pointerY - (bottom - zone);
  if (upDepth < 0 && downDepth < 0) {
    return 0;
  }
  const direction = upDepth >= 0 ? -1 : 1;
  const depth = upDepth >= 0 ? upDepth : downDepth;

  const topSpeed = Math.max(TOP_SPEED_MIN, Math.min(TOP_SPEED_MAX, height * TOP_SPEED_HEIGHTS)) * (reducedMotion ? 0.5 : 1);
  const progress = Math.min(1, depth / (FULL_SPEED_DEPTH * zone));
  return direction * (SLOWEST_SPEED + (topSpeed - SLOWEST_SPEED) * progress * progress);
}

/** The nearest ancestor that scrolls vertically, or the document's scroller. */
export function findScroller(element: HTMLElement): Element {
  for (let node = element.parentElement; node; node = node.parentElement) {
    const { overflowY } = getComputedStyle(node);
    if ((overflowY === 'auto' || overflowY === 'scroll' || overflowY === 'overlay') &&
        node.scrollHeight > node.clientHeight) {
      return node;
    }
  }
  return document.scrollingElement ?? document.documentElement;
}

/** The scroller's visible top and bottom in viewport coordinates, clipped to the viewport. */
export function visibleEdges(scroller: Element): { top: number; bottom: number } {
  const viewportBottom = window.innerHeight;
  if (scroller === document.scrollingElement || scroller === document.documentElement || scroller === document.body) {
    return { top: 0, bottom: viewportBottom };
  }
  const rect = scroller.getBoundingClientRect();
  const top = rect.top + scroller.clientTop;
  return { top: Math.max(0, top), bottom: Math.min(viewportBottom, top + scroller.clientHeight) };
}

export interface DragAutoscrollerOptions {
  /** Called after every frame that moved the scroller. */
  readonly onScroll?: () => void;
  /** Overrides the `prefers-reduced-motion` media query; tests set it. */
  readonly reducedMotion?: boolean;
}

/**
 * Scrolls one element while a drag holds the pointer near its top or bottom edge. Free of Angular:
 * callers construct and drive it outside the zone, so its frames never run change detection.
 */
export class DragAutoscroller {
  private readonly reducedMotion: boolean;
  private readonly onScroll: (() => void) | undefined;
  private pointerY: number;
  private armedUp = false;
  private armedDown = false;
  private stopped = false;

  private frame: number | null = null;
  private lastTimestamp: number | null = null;
  private rampStart = 0;
  private direction = 0;
  /** Whole pixels are scrolled; the fraction waits here for the next frame. */
  private carry = 0;

  constructor(private readonly scroller: Element, private readonly startY: number, options: DragAutoscrollerOptions = {}) {
    this.onScroll = options.onScroll;
    this.reducedMotion = options.reducedMotion ??
      (typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches);
    this.pointerY = startY;
    this.armedVelocity();
  }

  /** The latest pointer position; starts the frame loop when scrolling is due. */
  update(pointerY: number): void {
    if (this.stopped) {
      return;
    }
    this.pointerY = pointerY;
    if (this.armedVelocity() !== 0 && this.frame === null) {
      this.frame = requestAnimationFrame(this.onFrame);
    }
  }

  /** Stops scrolling until the next update(). */
  pause(): void {
    this.endLoop();
  }

  /** Ends the session; further calls do nothing. */
  stop(): void {
    this.stopped = true;
    this.endLoop();
  }

  /**
   * The velocity at the pointer, 0 until its direction is armed. A direction arms once the pointer
   * has moved that way from the drag's start or has been outside that direction's zone, so picking
   * up a row near an edge does not scroll by itself.
   */
  private armedVelocity(): number {
    const { top, bottom } = visibleEdges(this.scroller);
    const velocity = autoscrollVelocity(this.pointerY, top, bottom, this.reducedMotion);
    this.armedUp ||= velocity >= 0 || this.startY - this.pointerY >= ARM_PX;
    this.armedDown ||= velocity <= 0 || this.pointerY - this.startY >= ARM_PX;
    return (velocity < 0 && !this.armedUp) || (velocity > 0 && !this.armedDown) ? 0 : velocity;
  }

  private onFrame = (timestamp: number): void => {
    this.frame = null;
    const velocity = this.armedVelocity();
    if (velocity === 0) {
      this.endLoop();
      return;
    }

    const direction = Math.sign(velocity);
    if (direction !== this.direction) {
      this.direction = direction;
      this.rampStart = timestamp;
      this.carry = 0;
    }
    const elapsed = this.lastTimestamp === null ? 0 : Math.min(MAX_FRAME_MS, timestamp - this.lastTimestamp);
    this.lastTimestamp = timestamp;

    this.carry += velocity * Math.min(1, (timestamp - this.rampStart) / RAMP_MS) * elapsed / 1000;
    const step = Math.trunc(this.carry);
    if (step !== 0) {
      this.carry -= step;
      const before = this.scroller.scrollTop;
      // 'instant' keeps CSS scroll-behavior: smooth from fighting the loop.
      this.scroller.scrollBy({ top: step, behavior: 'instant' });
      if (this.scroller.scrollTop === before) {
        // At the end of the scroll range; the next update() tries again.
        this.endLoop();
        return;
      }
      this.onScroll?.();
    }
    this.frame = requestAnimationFrame(this.onFrame);
  };

  private endLoop(): void {
    if (this.frame !== null) {
      cancelAnimationFrame(this.frame);
      this.frame = null;
    }
    this.lastTimestamp = null;
    this.direction = 0;
    this.carry = 0;
  }
}
