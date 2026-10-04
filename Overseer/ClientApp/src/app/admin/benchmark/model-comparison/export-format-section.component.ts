/**
 * One image format, as a settings-section disclosure: PNG or WebP as a radio group, the WebP quality
 * while WebP is chosen, an optional note, and a reset button.
 *
 * Presentational: it holds only the reset button's status line. The format and the quality come from
 * the inputs, and every change goes out through `formatChange` or `qualityChange`; the host stores it
 * and hands it back down. Every id derives from `idPrefix`, so two sections can share a page.
 */

import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges
} from '@angular/core';

import {
  DEFAULT_WEBP_QUALITY,
  FigureExportFormat,
  WEBP_QUALITY_OPTIONS,
  WebpQuality
} from './figure-export';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';

/** The offered formats, in order, with their visible names. */
export const EXPORT_FORMAT_OPTIONS: readonly { readonly value: FigureExportFormat; readonly label: string }[] = [
  { value: 'png', label: 'PNG' },
  { value: 'webp', label: 'WebP' }
];

/** The closed section's read-out: `PNG`, or `WebP · quality 85`. */
export function exportFormatReadout(format: FigureExportFormat, quality: WebpQuality): string {
  return format === 'webp' ? `WebP · quality ${quality}` : 'PNG';
}

@Component({
  selector: 'app-export-format-section',
  standalone: true,
  templateUrl: './export-format-section.component.html',
  styleUrls: ['./export-format-section.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ExportFormatSectionComponent implements OnInit, OnChanges {
  @Input() title = 'Image format';
  @Input() idPrefix = '';
  @Input() format: FigureExportFormat = 'png';
  @Input() webpQuality: WebpQuality = DEFAULT_WEBP_QUALITY;
  @Input() defaultFormat: FigureExportFormat = 'png';
  @Input() defaultQuality: WebpQuality = DEFAULT_WEBP_QUALITY;
  @Input() open = false;
  /** A sentence under the controls, linked to the format group; none renders nothing. */
  @Input() note = '';

  @Output() readonly formatChange = new EventEmitter<FigureExportFormat>();
  @Output() readonly qualityChange = new EventEmitter<WebpQuality>();
  @Output() readonly openChange = new EventEmitter<boolean>();
  @Output() readonly reset = new EventEmitter<void>();

  readonly formatOptions = EXPORT_FORMAT_OPTIONS;
  readonly qualityOptions = WEBP_QUALITY_OPTIONS;

  /** What the reset button's status line announces; cleared by the next change that is not the reset's own. */
  resetStatus = '';
  private pendingResetAck = false;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    const changed = ['format', 'webpQuality'].some(key => changes[key] && !changes[key].firstChange);
    if (!changed) {
      return;
    }
    if (this.pendingResetAck) {
      this.pendingResetAck = false;
    } else {
      this.resetStatus = '';
    }
  }

  get readout(): string {
    return exportFormatReadout(this.format, this.webpQuality);
  }

  get isDefault(): boolean {
    return this.format === this.defaultFormat && this.webpQuality === this.defaultQuality;
  }

  get resetTip(): string {
    return this.isDefault ? 'Already at defaults' : 'Reset to defaults';
  }

  get noteId(): string | null {
    return this.note !== '' ? `${this.idPrefix}-note` : null;
  }

  onToggle(event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (open !== this.open) {
      this.openChange.emit(open);
    }
  }

  onFormatChange(value: FigureExportFormat): void {
    if (value === this.format) {
      return;
    }
    this.resetStatus = '';
    this.formatChange.emit(value);
  }

  onQualityChange(value: string): void {
    const quality = Number(value) as WebpQuality;
    if (!WEBP_QUALITY_OPTIONS.includes(quality) || quality === this.webpQuality) {
      return;
    }
    this.resetStatus = '';
    this.qualityChange.emit(quality);
  }

  onReset(): void {
    if (this.isDefault) {
      return;
    }
    this.resetStatus = `${this.title} reset to defaults.`;
    this.pendingResetAck = true;
    this.reset.emit();
  }
}
