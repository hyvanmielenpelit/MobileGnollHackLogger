import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';

import { ensureOverlayPolyfills } from '../../../../utils/polyfills.util';
import { ExportFormatSectionComponent } from '../../model-comparison/export-format-section.component';
import { ExportSizeSectionComponent } from '../../model-comparison/export-size-section.component';
import { FigureExportFormat, WebpQuality, displayDensity as currentDisplayDensity } from '../../model-comparison/figure-export';
import { FIT_RESOLUTION_ID, FigureSizeSettings, sizeErrors } from '../../model-comparison/figure-size';
import { CcResultsImageItem } from './results-image-blocks';
import type { CcResultsImageMeasure } from './results-image-export';
import {
  CC_RESULTS_IMAGE_DETAILS,
  CC_RESULTS_IMAGE_SECTIONS,
  CC_RESULTS_IMAGE_SECTION_LABELS,
  CcResultsImageFileSections,
  CcResultsImageScheme,
  CcResultsImageSection,
  CcResultsImageSettings,
  ccResultsImageFormatLabel,
  defaultCcResultsImageSettings,
  readStoredCcResultsImageFileSections,
  withSectionExclusions,
  writeStoredCcResultsImageFileSections
} from './results-image-settings';

/** What the host measures for the footer summary: one section's next image at the given settings. */
export type CcResultsImageMeasurer = (settings: CcResultsImageSettings, section: CcResultsImageSection) => CcResultsImageMeasure | null;

/** Each section's choosable items, from the current result. */
export type CcResultsImageItems = Readonly<Record<CcResultsImageSection, readonly CcResultsImageItem[]>>;

/** The two color schemes, as the radio group offers them. */
export const CC_RESULTS_IMAGE_SCHEMES: readonly { readonly value: CcResultsImageScheme; readonly label: string }[] = [
  { value: 'dark', label: 'Dark, as on screen' },
  { value: 'light', label: 'Light, for print' }
];

/**
 * The Results step's *Image settings*: what each section's image shows, the image details and colors
 * common to every section, and the image file. The left column, *What to show*: a tab row of the six
 * sections, opening on the section the dialog was opened from, with each section's checklist, then the
 * image details and the colors. The right column, *Image file*: the format and the size.
 *
 * Presentational: the host passes the item lists and the remembered settings to `open()` and stores the
 * settings `settingsChange` carries. Every change applies at once; Done, the close button, Escape and
 * light dismiss only close. Exclusions of items the current result does not have are kept. The dialog's
 * own storage is only the open state of its two file sections.
 *
 * Nested in the analysis wizard's dialog, so its own close, cancel and click events stop here.
 */
@Component({
  selector: 'app-cc-results-image-dialog',
  standalone: true,
  imports: [ExportFormatSectionComponent, ExportSizeSectionComponent],
  templateUrl: './results-image-dialog.component.html',
  styleUrls: ['./results-image-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcResultsImageDialogComponent implements OnInit {
  private readonly cdr = inject(ChangeDetectorRef);

  /** Measures the selected section's next image for the footer summary; null shows the format alone in fit mode. */
  @Input() measureImage: CcResultsImageMeasurer | null = null;

  /** Emitted on every change, with the whole settings to remember. */
  @Output() readonly settingsChange = new EventEmitter<CcResultsImageSettings>();

  @ViewChild('imageDialog') imageDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('imageTitle') imageTitle?: ElementRef<HTMLElement>;

  readonly sections = CC_RESULTS_IMAGE_SECTIONS.map(id => ({ id, label: CC_RESULTS_IMAGE_SECTION_LABELS[id] }));
  readonly details = CC_RESULTS_IMAGE_DETAILS;
  readonly schemes = CC_RESULTS_IMAGE_SCHEMES;

  /** The section whose checklist shows. */
  section: CcResultsImageSection = 'summary';

  /** Each section's items, from `open()`. */
  items: CcResultsImageItems = emptyItems();

  /** The settings every control shows. */
  settings: CcResultsImageSettings = defaultCcResultsImageSettings();

  /** What the size section resets to: Fit the content at 200 %. */
  readonly sizeDefaults: FigureSizeSettings = defaultCcResultsImageSettings().size;

  /** The density of the display the dialog opened on, marked in the density options. */
  readonly displayDensity = currentDisplayDensity();

  /** Which file sections are open, read on every open and stored on every toggle. */
  fileSections: CcResultsImageFileSections = { format: true, size: true };

  /** The footer's one-line summary of the file the next download of the selected section writes. */
  summary = '';

  /** A refusal of the selected section at this size that the size section does not show itself. */
  sizeRefusal = '';

  /** Where focus returns on close. */
  private opener: HTMLElement | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  /** Shows the dialog on `section`, with `items` and `settings`, and focuses its title. */
  open(section: CcResultsImageSection, items: CcResultsImageItems, settings: CcResultsImageSettings, opener?: HTMLElement | null): void {
    const dialog = this.imageDialog?.nativeElement;
    if (!dialog) {
      return;
    }
    this.section = section;
    this.items = items;
    this.settings = settings;
    this.fileSections = readStoredCcResultsImageFileSections();
    this.opener = opener ?? (document.activeElement instanceof HTMLElement ? document.activeElement : null);
    this.refreshSummary();
    this.cdr.detectChanges();
    if (!dialog.open) {
      dialog.showModal();
    }
    this.imageTitle?.nativeElement.focus();
  }

  /** Closes the dialog; the host calls it when the analysis shows another result. */
  close(): void {
    const dialog = this.imageDialog?.nativeElement;
    if (dialog?.open) {
      dialog.close();
    }
  }

  get isOpen(): boolean {
    return !!this.imageDialog?.nativeElement.open;
  }

  // --- Sections ---

  selectSection(section: CcResultsImageSection): void {
    if (section === this.section) {
      return;
    }
    this.section = section;
    this.refreshSummary();
    this.cdr.markForCheck();
  }

  /** Left/Right move and wrap, Home/End jump to the ends; focus follows selection. */
  onSectionKeydown(event: KeyboardEvent, index: number): void {
    const count = this.sections.length;
    const targets: Record<string, number> = { ArrowRight: index + 1, ArrowLeft: index - 1, Home: 0, End: count - 1 };
    const requested = targets[event.key];
    if (requested === undefined) {
      return;
    }
    event.preventDefault();
    const next = this.sections[(requested + count) % count].id;
    this.selectSection(next);
    this.cdr.detectChanges();
    document.getElementById(`cc-rim-tab-${next}`)?.focus();
  }

  itemsOf(section: CcResultsImageSection): readonly CcResultsImageItem[] {
    return this.items[section] ?? [];
  }

  isIncluded(section: CcResultsImageSection, key: string): boolean {
    return !(this.settings.excluded[section] ?? []).includes(key);
  }

  onToggle(section: CcResultsImageSection, key: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const excluded = (this.settings.excluded[section] ?? []).filter(entry => entry !== key);
    this.apply(withSectionExclusions(this.settings, section, checked ? excluded : [...excluded, key]));
  }

  /** Every item of the section in; exclusions of items this result does not have are kept. */
  selectAll(section: CcResultsImageSection): void {
    const shown = new Set(this.itemsOf(section).map(item => item.key));
    this.apply(withSectionExclusions(this.settings, section, (this.settings.excluded[section] ?? []).filter(key => !shown.has(key))));
  }

  selectNone(section: CcResultsImageSection): void {
    const excluded = [...(this.settings.excluded[section] ?? [])];
    for (const item of this.itemsOf(section)) {
      if (!excluded.includes(item.key)) {
        excluded.push(item.key);
      }
    }
    this.apply(withSectionExclusions(this.settings, section, excluded));
  }

  /** `7 of 10 selected`. */
  countText(section: CcResultsImageSection): string {
    const items = this.itemsOf(section);
    return `${items.filter(item => this.isIncluded(section, item.key)).length} of ${items.length} selected`;
  }

  // --- Image details and colors ---

  isDetailIncluded(key: string): boolean {
    return !this.settings.detailsExcluded.includes(key);
  }

  onDetailToggle(key: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const excluded = this.settings.detailsExcluded.filter(entry => entry !== key);
    this.apply({ ...this.settings, detailsExcluded: checked ? excluded : [...excluded, key] });
  }

  selectAllDetails(): void {
    const shown = new Set(this.details.map(detail => detail.key));
    this.apply({ ...this.settings, detailsExcluded: this.settings.detailsExcluded.filter(key => !shown.has(key)) });
  }

  selectNoDetails(): void {
    const excluded = [...this.settings.detailsExcluded];
    for (const detail of this.details) {
      if (!excluded.includes(detail.key)) {
        excluded.push(detail.key);
      }
    }
    this.apply({ ...this.settings, detailsExcluded: excluded });
  }

  /** `3 of 4 selected`. */
  get detailCountText(): string {
    return `${this.details.filter(detail => this.isDetailIncluded(detail.key)).length} of ${this.details.length} selected`;
  }

  onSchemeChange(scheme: CcResultsImageScheme): void {
    if (scheme !== this.settings.scheme) {
      this.apply({ ...this.settings, scheme });
    }
  }

  // --- Image file ---

  onFormatChange(format: FigureExportFormat): void {
    this.apply({ ...this.settings, format });
  }

  onQualityChange(webpQuality: WebpQuality): void {
    this.apply({ ...this.settings, webpQuality });
  }

  /** PNG, and 85 for a later WebP. */
  onFormatReset(): void {
    const defaults = defaultCcResultsImageSettings();
    this.apply({ ...this.settings, format: defaults.format, webpQuality: defaults.webpQuality });
  }

  onSizeChange(size: FigureSizeSettings): void {
    this.apply({ ...this.settings, size });
  }

  onSizeReset(): void {
    this.apply({ ...this.settings, size: this.sizeDefaults });
  }

  onFileSectionOpenChange(section: keyof CcResultsImageFileSections, open: boolean): void {
    if (this.fileSections[section] === open) {
      return;
    }
    this.fileSections = { ...this.fileSections, [section]: open };
    writeStoredCcResultsImageFileSections(this.fileSections);
  }

  /** `PNG` or `WebP`. */
  get formatLabel(): string {
    return ccResultsImageFormatLabel(this.settings.format);
  }

  /** Done and the close button. */
  done(): void {
    this.close();
  }

  private apply(next: CcResultsImageSettings): void {
    this.settings = next;
    this.settingsChange.emit(next);
    this.refreshSummary();
    this.cdr.markForCheck();
  }

  /**
   * The footer summary and the size refusal, from the host's measurer, for the selected section:
   * *Downloads a WebP, 3840 × 2160 px.* for a box, *Downloads a PNG, about 2560 × 1890 px.* in fit mode,
   * whose fonts may still be loading, or the refusal. A refusal the size section shows itself, a size
   * outside its limits, is not repeated under it.
   */
  private refreshSummary(): void {
    const fit = this.settings.size.resolutionId === FIT_RESOLUTION_ID;
    const measured = this.measureImage ? this.measureImage(this.settings, this.section) : null;
    const file = `Downloads a ${this.formatLabel}`;
    this.sizeRefusal = '';
    if (measured && 'refusal' in measured) {
      this.summary = measured.refusal;
      this.sizeRefusal = sizeErrors(this.settings.size, 'image').any ? '' : measured.refusal;
      return;
    }
    if (measured && 'empty' in measured) {
      this.summary = `Nothing in the ${CC_RESULTS_IMAGE_SECTION_LABELS[this.section]} section is selected.`;
      return;
    }
    this.summary = measured
      ? `${file}, ${fit ? 'about ' : ''}${measured.widthPx} × ${measured.heightPx} px.`
      : `${file}.`;
  }

  /** The dialog's close and cancel events stop here, short of the wizard's dialog. */
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
   * A click in the dialog stops here. Light dismiss where `closedby` is unsupported: a backdrop click
   * reports the dialog itself as the target, so a hit outside its border box closes it.
   */
  onDialogClick(event: MouseEvent): void {
    event.stopPropagation();
    if ('closedBy' in HTMLDialogElement.prototype) {
      return;
    }
    const dialog = this.imageDialog?.nativeElement;
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

function emptyItems(): CcResultsImageItems {
  return { summary: [], verdicts: [], periods: [], attribution: [], nextRuns: [], details: [] };
}
