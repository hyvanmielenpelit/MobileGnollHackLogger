import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnInit,
  Output,
  ViewChild
} from '@angular/core';

import { ensureOverlayPolyfills } from '../../utils/polyfills.util';

/**
 * The icon-only filter button on a picker's trigger, which shows and hides the picker's view
 * toolbar: a funnel glyph, a gold dot while `active`, and an interest-triggered tooltip. Knows
 * nothing about what the picker lists. Presentational: the picker owns `expanded` and acts on
 * `pressed`. The look is global (`styles.scss`, `.picker-tools-toggle`).
 */
@Component({
  selector: 'app-picker-tools-toggle',
  standalone: true,
  templateUrl: './picker-tools-toggle.component.html',
  styleUrl: './picker-tools-toggle.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'class': 'picker-tools-toggle' }
})
export class PickerToolsToggleComponent implements OnInit {
  /** The button's id; the tooltip is `{buttonId}-tip` and the anchor name `--{buttonId}`. */
  @Input({ required: true }) buttonId!: string;
  /** The accessible name. */
  @Input() label = '';
  /** The hint text. */
  @Input() tooltip = '';
  @Input() expanded = false;
  /** The toolbar's id, while it is rendered. */
  @Input() controls: string | null = null;
  /** Shows the dot; the label says why. */
  @Input() active = false;
  /** The button stays focusable and emits nothing. */
  @Input() disabled = false;

  /** A click, or Enter or Space on the button; never while disabled. */
  @Output() pressed = new EventEmitter<void>();

  @ViewChild('button', { static: true }) private buttonRef!: ElementRef<HTMLButtonElement>;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  focus(): void {
    this.buttonRef.nativeElement.focus();
  }

  onClick(): void {
    if (this.disabled) return;
    this.pressed.emit();
  }
}
