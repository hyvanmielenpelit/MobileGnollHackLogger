import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  ViewChild,
  inject
} from '@angular/core';

/**
 * The warning a Services dashboard shows when the server answers a Re-index, Reload or Build makedefs
 * with 409 `benchmarkRunActive`: a GnollBench run is executing and reads the corpus or the source
 * index. A modal
 * `<dialog class="gh-dialog">` with *Cancel* (focused first) and the confirm button
 * (*Re-index anyway* by default). Escape and the platform's close request cancel.
 *
 * ```html
 * <app-reindex-confirm-dialog #confirm idPrefix="gw" (confirmed)="reindex(true)" />
 * <!-- on the 409: confirm.open(serverMessage) -->
 * ```
 *
 * `open(message)` shows it with the server's message; `confirmed` fires when the confirm button
 * is clicked, after which the host resends with `acknowledgeActiveBenchmark=true`. Ids start with
 * `idPrefix`: `{p}-reindex-confirm-title`, `{p}-reindex-confirm-message`, `{p}-reindex-confirm-cancel`,
 * `{p}-reindex-confirm-ok`.
 */
@Component({
  selector: 'app-reindex-confirm-dialog',
  standalone: true,
  templateUrl: './reindex-confirm-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReindexConfirmDialogComponent {
  private readonly cdr = inject(ChangeDetectorRef);

  /** The prefix of every id. */
  @Input({ required: true }) idPrefix = '';
  /** The confirm button's label, matching the action: *Re-index anyway*, *Reload anyway*, *Build anyway*. */
  @Input() confirmLabel = 'Re-index anyway';
  /** The action's name at the start of the effect sentence: *Re-index*, *Reload*, *Build makedefs*. */
  @Input() actionName = 'Re-index';

  /** The confirm button was clicked; the dialog has closed. */
  @Output() confirmed = new EventEmitter<void>();

  @ViewChild('confirmDialog') dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('cancelButton') cancelButton?: ElementRef<HTMLButtonElement>;

  /** The server's message, such as "A GnollBench run is executing and reads the GnollHack wiki." */
  message = '';

  get isOpen(): boolean {
    return !!this.dialog?.nativeElement.open;
  }

  /** Shows the dialog modally with `message`, focusing Cancel. */
  open(message: string): void {
    this.message = message;
    this.cdr.markForCheck();
    this.cdr.detectChanges();
    const dialog = this.dialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    this.cancelButton?.nativeElement.focus();
  }

  close(): void {
    const dialog = this.dialog?.nativeElement;
    if (dialog?.open) {
      dialog.close();
    }
  }

  confirm(): void {
    this.close();
    this.confirmed.emit();
  }
}
