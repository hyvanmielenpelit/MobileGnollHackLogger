/**
 * One export size, as a settings-section disclosure: the size and aspect ratio, an optional custom
 * width and height with a ratio lock, pixel density, text size, the written-size read-out and a
 * reset button.
 *
 * Presentational: it holds only the custom-ratio lock and the reset button's status line, both of
 * which are this control's own transient UI state rather than anything the host needs to persist.
 * Every other value comes from `settings`, and every change goes out as a whole new settings object
 * through `settingsChange` — the host stores it, validates nothing twice, and hands the same object
 * back down. Three hosts use it: the charts' export size (`allowFit` off), the table's image size
 * (`allowFit` on, offering *Fit the table* ahead of the presets) and the key-figures image size
 * (`allowFit` on as *Fit the figures*, with no text size control).
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
import { FormsModule } from '@angular/forms';

import {
  FIGURE_EXPORT_DENSITY_PRESETS,
  FIGURE_EXPORT_MAX_DENSITY_PERCENT,
  FIGURE_EXPORT_MAX_DIMENSION,
  FIGURE_EXPORT_MAX_TEXT_SCALE_PERCENT,
  FIGURE_EXPORT_MIN_DENSITY_PERCENT,
  FIGURE_EXPORT_MIN_DIMENSION,
  FIGURE_EXPORT_MIN_TEXT_SCALE_PERCENT,
  FIGURE_EXPORT_PRESET_GROUPS,
  FigureExportResolution,
  aspectRatioLabel,
  densityPercentLabel
} from './figure-export';
import {
  FIT_RESOLUTION_ID,
  FigureSizeSettings,
  SizeErrors,
  clampExportDimension,
  resolveSizeResolution,
  sameFigureSize,
  sizeDimensionsLabel,
  sizeErrors,
  sizeReadout,
  sizeWrittenLabel
} from './figure-size';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';

@Component({
  selector: 'app-export-size-section',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './export-size-section.component.html',
  styleUrls: ['./export-size-section.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ExportSizeSectionComponent implements OnInit, OnChanges {
  @Input() title = '';
  @Input() idPrefix = '';
  @Input() settings!: FigureSizeSettings;
  @Input() defaults!: FigureSizeSettings;
  @Input() allowFit = false;
  @Input() displayDensity = 1;
  @Input() open = false;
  @Input() refusal = '';
  /** Shown only in custom mode, under the width and height fields. */
  @Input() fitInfo = '';
  /** The one-line meaning of 100 % text size. Absent, the row still renders with no hint under it. */
  @Input() textSizeHint = '';
  @Input() errorNoun = 'figure';
  @Input() layoutRule: 'figure' | 'plain' = 'figure';
  /** Overrides the written-size line when non-empty; the table uses it in fit mode. */
  @Input() writtenLabel = '';
  /** The fit option's text, also in the read-out. */
  @Input() fitLabel = 'Fit the table';
  /** The hint under the controls in fit mode. */
  @Input() fitHint = 'The image is as large as the table; density sets its sharpness.';
  /** Off, there is no text size control and the read-out leaves the text size out. */
  @Input() showTextSize = true;
  /** Shown under the controls when there is no text size control and a box size is chosen. */
  @Input() boxHint = '';

  @Output() readonly settingsChange = new EventEmitter<FigureSizeSettings>();
  @Output() readonly openChange = new EventEmitter<boolean>();
  @Output() readonly reset = new EventEmitter<void>();

  readonly fitResolutionId = FIT_RESOLUTION_ID;
  readonly presetGroups = FIGURE_EXPORT_PRESET_GROUPS;
  readonly minDimension = FIGURE_EXPORT_MIN_DIMENSION;
  readonly maxDimension = FIGURE_EXPORT_MAX_DIMENSION;
  readonly densityPresets = FIGURE_EXPORT_DENSITY_PRESETS;
  readonly minDensityPercent = FIGURE_EXPORT_MIN_DENSITY_PERCENT;
  readonly maxDensityPercent = FIGURE_EXPORT_MAX_DENSITY_PERCENT;
  readonly minTextScalePercent = FIGURE_EXPORT_MIN_TEXT_SCALE_PERCENT;
  readonly maxTextScalePercent = FIGURE_EXPORT_MAX_TEXT_SCALE_PERCENT;

  /** On, one custom side follows the other so the shape survives a change of size. */
  customRatioLocked = false;

  /** The ratio the lock captured, which is what the unedited side is derived from. */
  private customRatio = 16 / 9;

  /** What the reset button's status line announces; cleared on the next settings change that is not
   * the one the reset itself caused. */
  resetStatus = '';
  private pendingResetAck = false;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['settings'] || changes['settings'].firstChange) {
      return;
    }
    if (this.pendingResetAck) {
      this.pendingResetAck = false;
    } else {
      this.resetStatus = '';
    }
  }

  get isFit(): boolean {
    return this.allowFit && this.settings.resolutionId === FIT_RESOLUTION_ID;
  }

  get isCustomResolution(): boolean {
    return this.settings.resolutionId === 'custom';
  }

  get isCustomDensity(): boolean {
    return this.settings.densitySelection === 'custom';
  }

  get resolution(): FigureExportResolution {
    return resolveSizeResolution(this.settings);
  }

  /** The current custom size's shape, e.g. `16:9`, for the ratio-lock checkbox label. */
  get aspectLabel(): string {
    const resolution = this.resolution;
    return aspectRatioLabel(resolution.widthPx, resolution.heightPx);
  }

  get errors(): SizeErrors {
    return sizeErrors(this.settings, this.errorNoun);
  }

  /** Without a text size control the layout box says nothing the reader can change, so only the written size shows. */
  get dimensionsLabel(): string {
    if (this.writtenLabel !== '') {
      return this.writtenLabel;
    }
    return this.showTextSize ? sizeDimensionsLabel(this.settings, this.layoutRule) : sizeWrittenLabel(this.settings);
  }

  get readout(): string {
    return sizeReadout(this.settings, this.fitLabel, this.showTextSize);
  }

  get isDefault(): boolean {
    return sameFigureSize(this.settings, this.defaults);
  }

  get resetTip(): string {
    return this.isDefault ? 'Already at defaults' : 'Reset to defaults';
  }

  /** Also read directly by the density controls' own `aria-describedby`, and by the size select's through {@link resolutionDescribedBy}. */
  get errorId(): string | null {
    return this.errors.any ? `${this.idPrefix}-resolution-error` : null;
  }

  private get fitInfoId(): string | null {
    return this.allowFit && this.isCustomResolution && this.fitInfo !== '' ? `${this.idPrefix}-fit-info` : null;
  }

  /** `width` and `height` share one described-by set: the error, when there is one, and the fit
   * information, when it is showing — so a screen-reader user hears both with either field. */
  get dimensionDescribedBy(): string | null {
    const ids = [this.errorId, this.fitInfoId].filter((id): id is string => id !== null);
    return ids.length > 0 ? ids.join(' ') : null;
  }

  /** The fit or box hint under the controls, when one shows; null otherwise. */
  get sizeHintId(): string | null {
    if (this.isFit) {
      return this.fitHint !== '' ? `${this.idPrefix}-fit-hint` : null;
    }
    return !this.showTextSize && this.boxHint !== '' ? `${this.idPrefix}-box-hint` : null;
  }

  /** The size select's described-by set: the error, when there is one, and the size hint. */
  get resolutionDescribedBy(): string | null {
    const ids = [this.errorId, this.sizeHintId].filter((id): id is string => id !== null);
    return ids.length > 0 ? ids.join(' ') : null;
  }

  get textScaleDescribedBy(): string | null {
    return this.textSizeHint !== '' ? `${this.idPrefix}-text-scale-hint` : null;
  }

  /** One option's text, with the one that matches the reader's own display marked as such. */
  densityOptionLabel(preset: number): string {
    return preset === this.displayDensity
      ? `${densityPercentLabel(preset)} (this display)`
      : densityPercentLabel(preset);
  }

  onToggle(event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (open === this.open) {
      return;
    }
    this.openChange.emit(open);
  }

  onResolutionChange(value: string): void {
    this.emitSettings({ resolutionId: value });
  }

  onCustomWidthChange(width: number): void {
    this.emitSettings(this.customRatioLocked
      ? { customWidthPx: width, customHeightPx: clampExportDimension(Math.round(width / this.customRatio)) }
      : { customWidthPx: width });
  }

  onCustomHeightChange(height: number): void {
    this.emitSettings(this.customRatioLocked
      ? { customHeightPx: height, customWidthPx: clampExportDimension(Math.round(height * this.customRatio)) }
      : { customHeightPx: height });
  }

  /**
   * Captures the current shape when the lock goes on, and releases it when it goes off.
   *
   * Captured rather than held from the preset the reader came from: the two fields are what is on
   * screen, and a lock that snapped them to some earlier ratio would change the size it was asked
   * to preserve.
   */
  lockCustomRatio(locked: boolean): void {
    this.customRatioLocked = locked;
    if (locked) {
      const width = clampExportDimension(this.settings.customWidthPx);
      const height = clampExportDimension(this.settings.customHeightPx);
      this.customRatio = height > 0 ? width / height : 1;
    }
  }

  onDensityChange(value: number | 'custom'): void {
    this.emitSettings({ densitySelection: value });
  }

  onCustomDensityChange(percent: number): void {
    this.emitSettings({ customDensityPercent: percent });
  }

  /** Composition text size as a percentage: larger composes in a smaller box, at the same pixel size. */
  onTextScaleChange(percent: number): void {
    const value = Number.isFinite(percent) ? Math.round(percent) : 100;
    this.emitSettings({
      textScalePercent: Math.min(this.maxTextScalePercent, Math.max(this.minTextScalePercent, value))
    });
  }

  onReset(): void {
    if (this.isDefault) {
      return;
    }
    this.resetStatus = `${this.title} reset to defaults.`;
    this.pendingResetAck = true;
    this.reset.emit();
  }

  private emitSettings(patch: Partial<FigureSizeSettings>): void {
    this.resetStatus = '';
    this.settingsChange.emit({ ...this.settings, ...patch });
  }
}
