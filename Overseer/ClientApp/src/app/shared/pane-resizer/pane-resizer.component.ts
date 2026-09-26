import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnDestroy,
  Output,
  inject
} from '@angular/core';

/** `value` restricted to `[min, max]`. */
function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}

/** Drag geometry, captured once at pointerdown. */
interface DragState {
  readonly pointerId: number;
  readonly startX: number;
  readonly startValue: number;
}

/**
 * A vertical splitter implementing the WAI-ARIA *window splitter* pattern on the host element
 * itself: `role="separator"`, arrow-key and Home/End resizing, and a pointer drag.
 *
 * Presentational only. It keeps no copy of the pane's width: every emitted value is clamped to
 * `[min, max]`, and the host is expected to feed the resulting `value` back in. `valueChange`
 * fires live (at most once per animation frame while dragging); `valueCommit` fires once the
 * gesture ends, so a host that only persists on commit does not thrash on every pixel.
 */
@Component({
  selector: 'app-pane-resizer',
  standalone: true,
  templateUrl: './pane-resizer.component.html',
  styleUrl: './pane-resizer.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    'role': 'separator',
    'aria-orientation': 'vertical',
    'tabindex': '0',
    '[class.is-dragging]': 'dragging',
    '[attr.aria-controls]': 'controls',
    '[attr.aria-label]': 'label',
    '[attr.aria-valuenow]': 'ariaValueNow',
    '[attr.aria-valuemin]': 'ariaValueMin',
    '[attr.aria-valuemax]': 'ariaValueMax',
    '[attr.aria-valuetext]': 'ariaValueText',
    '(keydown)': 'onKeydown($event)',
    '(pointerdown)': 'onPointerDown($event)',
    '(pointermove)': 'onPointerMove($event)',
    '(pointerup)': 'onPointerUp($event)',
    '(pointercancel)': 'onPointerCancel($event)',
    '(lostpointercapture)': 'onLostPointerCapture($event)',
    '(dblclick)': 'onDoubleClick()'
  }
})
export class PaneResizerComponent implements OnDestroy {
  private host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** The `id` of the pane this splitter resizes; carried on `aria-controls`. */
  @Input({ required: true }) controls = '';
  /** The separator's accessible name. */
  @Input({ required: true }) label = '';
  /** The pane's current width in px. The component keeps no copy: the host passes it back. */
  @Input({ required: true }) value!: number;
  @Input({ required: true }) min!: number;
  @Input({ required: true }) max!: number;
  /** The width a double-click resets to. */
  @Input({ required: true }) defaultValue!: number;
  /** Arrow key step, in px. */
  @Input() step = 16;
  /** Shift+Arrow step, in px. */
  @Input() largeStep = 64;

  /** Live while dragging: at most once per animation frame. */
  @Output() valueChange = new EventEmitter<number>();
  /** Once, on pointer release or after a key press. */
  @Output() valueCommit = new EventEmitter<number>();

  /** Reflected as the `is-dragging` host class. */
  dragging = false;

  private drag: DragState | null = null;
  private pendingValue: number | null = null;
  private frame: number | null = null;

  private get clampedValue(): number {
    return clamp(this.value, this.min, this.max);
  }

  get ariaValueNow(): number {
    return Math.round(this.clampedValue);
  }

  get ariaValueMin(): number {
    return Math.round(this.min);
  }

  get ariaValueMax(): number {
    return Math.round(this.max);
  }

  get ariaValueText(): string {
    return `${this.ariaValueNow} pixels`;
  }

  ngOnDestroy(): void {
    if (this.frame !== null) {
      cancelAnimationFrame(this.frame);
      this.frame = null;
    }
  }

  onKeydown(event: KeyboardEvent): void {
    let next: number;
    switch (event.key) {
      case 'ArrowRight':
        next = this.clampedValue + (event.shiftKey ? this.largeStep : this.step);
        break;
      case 'ArrowLeft':
        next = this.clampedValue - (event.shiftKey ? this.largeStep : this.step);
        break;
      case 'Home':
        next = this.min;
        break;
      case 'End':
        next = this.max;
        break;
      default:
        // Any other key passes through untouched.
        return;
    }
    event.preventDefault();
    this.emitBoth(next);
  }

  onPointerDown(event: PointerEvent): void {
    if (this.drag || !event.isPrimary || event.button !== 0) {
      return;
    }
    // Keeps the press from selecting text or starting a native drag.
    event.preventDefault();
    try {
      this.host.nativeElement.setPointerCapture(event.pointerId);
    } catch {
      // The pointer is no longer active; the pointerId guards below then see nothing more.
    }
    this.drag = { pointerId: event.pointerId, startX: event.clientX, startValue: this.clampedValue };
    this.dragging = true;
  }

  onPointerMove(event: PointerEvent): void {
    const drag = this.drag;
    if (!drag || event.pointerId !== drag.pointerId) {
      return;
    }
    this.pendingValue = clamp(drag.startValue + (event.clientX - drag.startX), this.min, this.max);
    this.scheduleFrame();
  }

  onPointerUp(event: PointerEvent): void {
    if (this.drag && event.pointerId === this.drag.pointerId) {
      this.endDrag();
    }
  }

  onPointerCancel(event: PointerEvent): void {
    if (this.drag && event.pointerId === this.drag.pointerId) {
      this.endDrag();
    }
  }

  onLostPointerCapture(event: PointerEvent): void {
    // pointerup already clears `drag`, so a lostpointercapture that follows one finds it null
    // and does nothing here — the guard below is what keeps this from committing a second time.
    if (this.drag && event.pointerId === this.drag.pointerId) {
      this.endDrag();
    }
  }

  onDoubleClick(): void {
    this.emitBoth(this.defaultValue);
  }

  private scheduleFrame(): void {
    if (this.frame !== null) {
      return;
    }
    this.frame = requestAnimationFrame(() => {
      this.frame = null;
      if (this.pendingValue !== null) {
        this.valueChange.emit(this.pendingValue);
      }
    });
  }

  private endDrag(): void {
    const drag = this.drag;
    if (!drag) {
      return;
    }
    this.drag = null;
    this.dragging = false;
    if (this.frame !== null) {
      cancelAnimationFrame(this.frame);
      this.frame = null;
    }
    const final = this.pendingValue ?? drag.startValue;
    this.pendingValue = null;
    this.emitBoth(final);
  }

  private emitBoth(value: number): void {
    const clamped = clamp(value, this.min, this.max);
    this.valueChange.emit(clamped);
    this.valueCommit.emit(clamped);
  }
}
