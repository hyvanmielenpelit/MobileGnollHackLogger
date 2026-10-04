import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
  inject
} from '@angular/core';

import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { ExportFormatSectionComponent } from '../model-comparison/export-format-section.component';
import { ExportSizeSectionComponent } from '../model-comparison/export-size-section.component';
import { FigureExportFormat, WebpQuality, displayDensity as currentDisplayDensity } from '../model-comparison/figure-export';
import {
  FIT_RESOLUTION_ID,
  FigureSizeSettings,
  resolveSizeDensity,
  resolveSizeResolution,
  sizeErrors
} from '../model-comparison/figure-size';
import {
  KeyFiguresExportSections,
  KeyFiguresExportSettings,
  defaultKeyFiguresExportSettings,
  keyFiguresFormatLabel,
  readStoredKeyFiguresExportSections,
  writeStoredKeyFiguresExportSections
} from './key-figures-export-settings';
import type { KeyFiguresImageMeasure } from './key-figures-image';

/** What the host measures for the footer summary: the next image's size, its refusal, or null for nothing to measure. */
export type KeyFiguresImageMeasurer = (settings: KeyFiguresExportSettings) => KeyFiguresImageMeasure | null;

/** One figure the run shows: its stable key, its label and its current value. */
export interface KeyFigureChoice {
  readonly key: string;
  readonly label: string;
  readonly value: string;
}

/** The run settings the images may carry: one choice per run-fact row, and the remembered exclusions. */
export interface ImageDetailChoices {
  readonly rows: readonly KeyFigureChoice[];
  readonly excluded: readonly string[];
}

/**
 * Which key figures the run report shows and exports, and the image file they are written as. The
 * left column, *What to show*: one checkbox per card the run shows, and, when the host passes them,
 * one per run setting the images carry (Image details). The right column, *Image file*: the image
 * format and the image size. Every change applies at once through `selectionChange`,
 * `detailSelectionChange` or `exportSettingsChange`; Done, the close button, Escape and light dismiss
 * only close.
 *
 * The host passes the remembered exclusions and download settings to `open()` and stores the ones the
 * outputs carry. Exclusions of figures or rows this run does not have are kept. The chooser's own
 * storage is only the open state of its two file sections.
 *
 * Nested in the run report dialog, so its own close, cancel and click events stop here.
 */
@Component({
  selector: 'app-key-figures-chooser',
  standalone: true,
  imports: [ExportFormatSectionComponent, ExportSizeSectionComponent],
  templateUrl: './key-figures-chooser.component.html',
  styleUrls: ['./key-figures-chooser.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class KeyFiguresChooserComponent implements OnInit, OnChanges {
  private readonly cdr = inject(ChangeDetectorRef);

  /**
   * The start of every element id: `{idPrefix}Title`, `{idPrefix}-close-tip`, `{idPrefix}Caption`,
   * `{idPrefix}DetailsCaption`, `{idPrefix}-{key}`, `{idPrefix}-detail-{key}`, `{idPrefix}ShowTitle`,
   * `{idPrefix}FileTitle`, `{idPrefix}Summary`, and the two file sections' `{idPrefix}-fmt-…` and
   * `{idPrefix}-size-…`. A second chooser on the page passes its own, so the ids stay unique.
   */
  @Input() idPrefix = 'kfch';

  /** The download settings shown in the Image file column; `open()` may pass newer ones. */
  @Input()
  set exportSettings(value: KeyFiguresExportSettings | null | undefined) {
    if (value) {
      this.settings = value;
    }
  }
  get exportSettings(): KeyFiguresExportSettings {
    return this.settings;
  }

  /** Measures the next whole-strip image for the footer summary; null shows the format alone in fit mode. */
  @Input() measureImage: KeyFiguresImageMeasurer | null = null;

  /** Emitted on every change of the format, the WebP quality or the size, with the whole settings to remember. */
  @Output() readonly exportSettingsChange = new EventEmitter<KeyFiguresExportSettings>();

  /** The hint under the figures. */
  @Input() figuresHint = 'Shown in the Summary and in the copied or downloaded image. Remembered for every run report; a figure a run does not have is left out.';

  /** The hint under the image details. */
  @Input() detailsHint = "The run's settings above the figures in the copied or downloaded image. The dialog header always lists them all. Remembered for every run report.";

  /** Emitted on every change of the selection, with the exclusions to remember. */
  @Output() readonly selectionChange = new EventEmitter<string[]>();

  /** Emitted on every change of the image details, with the exclusions to remember. */
  @Output() readonly detailSelectionChange = new EventEmitter<string[]>();

  @ViewChild('chooserDialog') chooserDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('chooserTitle') chooserTitle?: ElementRef<HTMLElement>;

  /** The figures the run shows, in display order. */
  figures: KeyFigureChoice[] = [];

  /** The keys of the checked figures. */
  private checked = new Set<string>();

  /** The remembered exclusions `open()` received. */
  private storedExcluded: string[] = [];

  /** The run settings the images may carry, in display order; none hides the group. */
  detailRows: KeyFigureChoice[] = [];

  /** The keys of the checked run settings. */
  private checkedDetails = new Set<string>();

  /** The remembered image-detail exclusions `open()` received. */
  private storedDetailExcluded: string[] = [];

  /** Where focus returns on close. */
  private opener: HTMLElement | null = null;

  /** The format, quality and size the Image file column shows. */
  settings: KeyFiguresExportSettings = defaultKeyFiguresExportSettings();

  /** What the size section resets to: Fit the figures at 200 %. */
  readonly sizeDefaults: FigureSizeSettings = defaultKeyFiguresExportSettings().size;

  /** The density of the display the dialog opened on, marked in the density options. */
  readonly displayDensity = currentDisplayDensity();

  /** Which file sections are open, read on every open and stored on every toggle. */
  sections: KeyFiguresExportSections = { format: true, size: true };

  /** The footer's one-line summary of the file the next download writes. */
  summary = '';

  /** A fit-mode bitmap the browser cannot write, under the size section; a box size shows its own. */
  sizeRefusal = '';

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['exportSettings'] || changes['measureImage']) {
      this.refreshSummary();
    }
  }

  /**
   * Shows the dialog with every figure checked that `excluded` does not name, every image detail
   * checked that `details.excluded` does not name, and the download `settings` when given, and
   * focuses its title.
   */
  open(
    figures: readonly KeyFigureChoice[],
    excluded: readonly string[],
    opener?: HTMLElement | null,
    details?: ImageDetailChoices,
    settings?: KeyFiguresExportSettings
  ): void {
    const dialog = this.chooserDialog?.nativeElement;
    if (!dialog) {
      return;
    }
    if (settings) {
      this.settings = settings;
    }
    this.sections = readStoredKeyFiguresExportSections();
    this.figures = [...figures];
    this.storedExcluded = [...excluded];
    this.checked = new Set(this.figures.map(figure => figure.key).filter(key => !excluded.includes(key)));
    this.detailRows = [...(details?.rows ?? [])];
    this.storedDetailExcluded = [...(details?.excluded ?? [])];
    this.checkedDetails = new Set(this.detailRows.map(row => row.key).filter(key => !this.storedDetailExcluded.includes(key)));
    this.opener = opener ?? (document.activeElement instanceof HTMLElement ? document.activeElement : null);
    this.refreshSummary();
    this.cdr.detectChanges();
    if (!dialog.open) {
      dialog.showModal();
    }
    this.chooserTitle?.nativeElement.focus();
  }

  isChecked(key: string): boolean {
    return this.checked.has(key);
  }

  onToggle(key: string, event: Event): void {
    if ((event.target as HTMLInputElement).checked) {
      this.checked.add(key);
    } else {
      this.checked.delete(key);
    }
    this.emitSelection();
  }

  selectAll(): void {
    this.checked = new Set(this.figures.map(figure => figure.key));
    this.emitSelection();
  }

  selectNone(): void {
    this.checked = new Set();
    this.emitSelection();
  }

  get selectedCount(): number {
    return this.figures.filter(figure => this.checked.has(figure.key)).length;
  }

  /** `9 of 12 selected`. */
  get countText(): string {
    return `${this.selectedCount} of ${this.figures.length} selected`;
  }

  /**
   * The exclusions to remember: the unchecked figures of this run, and the stored ones of figures
   * this run does not show.
   */
  get excluded(): string[] {
    const shown = new Set(this.figures.map(figure => figure.key));
    return [
      ...this.storedExcluded.filter(key => !shown.has(key)),
      ...this.figures.map(figure => figure.key).filter(key => !this.checked.has(key))
    ];
  }

  isDetailChecked(key: string): boolean {
    return this.checkedDetails.has(key);
  }

  toggleDetail(key: string, event: Event): void {
    if ((event.target as HTMLInputElement).checked) {
      this.checkedDetails.add(key);
    } else {
      this.checkedDetails.delete(key);
    }
    this.emitDetailSelection();
  }

  selectAllDetails(): void {
    this.checkedDetails = new Set(this.detailRows.map(row => row.key));
    this.emitDetailSelection();
  }

  selectNoDetails(): void {
    this.checkedDetails = new Set();
    this.emitDetailSelection();
  }

  /** `5 of 6 selected`. */
  get detailCountText(): string {
    const selected = this.detailRows.filter(row => this.checkedDetails.has(row.key)).length;
    return `${selected} of ${this.detailRows.length} selected`;
  }

  /**
   * The image-detail exclusions to remember: the unchecked rows of this run, and the stored ones of
   * rows this run does not have.
   */
  get detailsExcluded(): string[] {
    const shown = new Set(this.detailRows.map(row => row.key));
    return [
      ...this.storedDetailExcluded.filter(key => !shown.has(key)),
      ...this.detailRows.map(row => row.key).filter(key => !this.checkedDetails.has(key))
    ];
  }

  /** Done and the close button. */
  done(): void {
    this.chooserDialog?.nativeElement.close();
  }

  private emitSelection(): void {
    this.selectionChange.emit(this.excluded);
    this.refreshSummary();
    this.cdr.markForCheck();
  }

  private emitDetailSelection(): void {
    this.detailSelectionChange.emit(this.detailsExcluded);
    this.refreshSummary();
    this.cdr.markForCheck();
  }

  onFormatChange(format: FigureExportFormat): void {
    this.applySettings({ ...this.settings, format });
  }

  onQualityChange(webpQuality: WebpQuality): void {
    this.applySettings({ ...this.settings, webpQuality });
  }

  /** PNG, and 85 for a later WebP. */
  onFormatReset(): void {
    const defaults = defaultKeyFiguresExportSettings();
    this.applySettings({ ...this.settings, format: defaults.format, webpQuality: defaults.webpQuality });
  }

  onSizeChange(size: FigureSizeSettings): void {
    this.applySettings({ ...this.settings, size });
  }

  onSizeReset(): void {
    this.applySettings({ ...this.settings, size: this.sizeDefaults });
  }

  onSectionOpenChange(section: keyof KeyFiguresExportSections, open: boolean): void {
    if (this.sections[section] === open) {
      return;
    }
    this.sections = { ...this.sections, [section]: open };
    writeStoredKeyFiguresExportSections(this.sections);
  }

  /** `PNG` or `WebP`. */
  get formatLabel(): string {
    return keyFiguresFormatLabel(this.settings.format);
  }

  private applySettings(next: KeyFiguresExportSettings): void {
    this.settings = next;
    this.exportSettingsChange.emit(next);
    this.refreshSummary();
    this.cdr.markForCheck();
  }

  /**
   * The footer summary and the fit-mode refusal, from the host's measurer: *Downloads a WebP,
   * 3840 × 2160 px.* for a box, *Downloads a PNG, about 2152 × 2152 px.* in fit mode, whose fonts may
   * still be loading, or the refusal.
   */
  private refreshSummary(): void {
    const fit = this.settings.size.resolutionId === FIT_RESOLUTION_ID;
    const measured = this.measureImage ? this.measureImage(this.settings) : fit ? null : this.boxMeasure();
    const file = `Downloads a ${this.formatLabel}`;
    if (measured && 'refusal' in measured) {
      this.summary = measured.refusal;
      this.sizeRefusal = fit ? measured.refusal : '';
      return;
    }
    this.sizeRefusal = '';
    this.summary = measured
      ? `${file}, ${fit ? 'about ' : ''}${measured.widthPx} × ${measured.heightPx} px.`
      : `${file}.`;
  }

  /** A box size's bitmap, which needs no figures to measure; its problems are the size section's to show. */
  private boxMeasure(): KeyFiguresImageMeasure | null {
    const size = this.settings.size;
    if (sizeErrors(size, 'image').any) {
      return null;
    }
    const box = resolveSizeResolution(size);
    const density = resolveSizeDensity(size);
    return { widthPx: Math.round(box.widthPx * density), heightPx: Math.round(box.heightPx * density) };
  }

  /** The dialog's close and cancel events stop here, short of the run report dialog. */
  onDialogEvent(event: Event): void {
    event.stopPropagation();
    if (event.type !== 'close') {
      return;
    }
    const opener = this.opener;
    this.opener = null;
    if (opener?.isConnected) {
      opener.focus();
    }
  }

  /**
   * A click in the dialog stops here. Light dismiss where `closedby` is unsupported: a backdrop
   * click reports the dialog itself as the target, so a hit outside its border box closes it.
   */
  onDialogClick(event: MouseEvent): void {
    event.stopPropagation();
    if ('closedBy' in HTMLDialogElement.prototype) {
      return;
    }
    const dialog = this.chooserDialog?.nativeElement;
    if (!dialog || event.target !== dialog) {
      return;
    }
    const rect = dialog.getBoundingClientRect();
    const inside = rect.top <= event.clientY && event.clientY <= rect.top + rect.height
      && rect.left <= event.clientX && event.clientX <= rect.left + rect.width;
    if (!inside) {
      dialog.close();
    }
  }
}
