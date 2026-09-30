import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Output,
  ViewChild,
  inject
} from '@angular/core';

import { KeyFiguresAction } from './key-figures-image';

/** One figure the run shows: its stable key, its label and its current value. */
export interface KeyFigureChoice {
  readonly key: string;
  readonly label: string;
  readonly value: string;
}

/** Export the chosen figures: the exclusions to remember, and what to do with the image. */
export interface KeyFiguresChooserExport {
  readonly action: KeyFiguresAction;
  readonly excluded: readonly string[];
}

/**
 * Which key figures go into the whole-strip image: one checkbox per card the run shows, and Copy
 * Image / Download PNG, which commit the choice and export it. Cancel discards the draft.
 *
 * Never touches storage: the host passes the remembered exclusions to `open()` and stores the ones
 * `exportRequested` carries. Exclusions of figures this run does not show are kept.
 *
 * Nested in the run report dialog, so its own close, cancel and click events stop here.
 */
@Component({
  selector: 'app-key-figures-chooser',
  standalone: true,
  templateUrl: './key-figures-chooser.component.html',
  styleUrls: ['./key-figures-chooser.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class KeyFiguresChooserComponent {
  private readonly cdr = inject(ChangeDetectorRef);

  /** Emitted, inside the click, by Copy Image and Download PNG; the dialog then closes. */
  @Output() readonly exportRequested = new EventEmitter<KeyFiguresChooserExport>();

  @ViewChild('chooserDialog') chooserDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('chooserTitle') chooserTitle?: ElementRef<HTMLElement>;

  /** The figures the run shows, in display order. */
  figures: KeyFigureChoice[] = [];

  /** The draft: the keys of the checked figures. */
  private checked = new Set<string>();

  /** The remembered exclusions `open()` received. */
  private storedExcluded: string[] = [];

  /** Where focus returns on close. */
  private opener: HTMLElement | null = null;

  /** Shows the dialog with every figure checked that `excluded` does not name, and focuses its title. */
  open(figures: readonly KeyFigureChoice[], excluded: readonly string[], opener?: HTMLElement | null): void {
    const dialog = this.chooserDialog?.nativeElement;
    if (!dialog) {
      return;
    }
    this.figures = [...figures];
    this.storedExcluded = [...excluded];
    this.checked = new Set(this.figures.map(figure => figure.key).filter(key => !excluded.includes(key)));
    this.opener = opener ?? (document.activeElement instanceof HTMLElement ? document.activeElement : null);
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
    this.cdr.markForCheck();
  }

  selectAll(): void {
    this.checked = new Set(this.figures.map(figure => figure.key));
    this.cdr.markForCheck();
  }

  selectNone(): void {
    this.checked = new Set();
    this.cdr.markForCheck();
  }

  get selectedCount(): number {
    return this.figures.filter(figure => this.checked.has(figure.key)).length;
  }

  get nothingSelected(): boolean {
    return this.selectedCount === 0;
  }

  /** `9 of 12 selected`. */
  get countText(): string {
    return `${this.selectedCount} of ${this.figures.length} selected`;
  }

  /**
   * The exclusions to remember: the unchecked figures of this run, and the stored ones of figures
   * this run does not show.
   */
  get draftExcluded(): string[] {
    const shown = new Set(this.figures.map(figure => figure.key));
    return [
      ...this.storedExcluded.filter(key => !shown.has(key)),
      ...this.figures.map(figure => figure.key).filter(key => !this.checked.has(key))
    ];
  }

  /** Copy Image and Download PNG: emits within the click, so a clipboard write keeps its user activation. */
  commit(action: KeyFiguresAction): void {
    if (this.nothingSelected) {
      return;
    }
    this.exportRequested.emit({ action, excluded: this.draftExcluded });
    this.close();
  }

  cancel(): void {
    this.close();
  }

  private close(): void {
    this.chooserDialog?.nativeElement.close();
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
