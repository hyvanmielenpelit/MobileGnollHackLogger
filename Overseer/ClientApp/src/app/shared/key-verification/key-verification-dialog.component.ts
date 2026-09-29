import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';
import { ApiKeyRefusal, describeCheckResponse, formatElapsed } from './key-verification';

/**
 * *Could not verify the key*: shown when a provider did not answer a key check. **Save Anyway**
 * emits `saveAnyway`; the host resends with `saveUnverified: true` and then either calls
 * `close()` or `open()` again with the new outcome. Its own close and cancel events stop here.
 */
@Component({
  selector: 'app-key-verification-dialog',
  templateUrl: './key-verification-dialog.component.html',
  styleUrl: './key-verification-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class KeyVerificationDialogComponent implements OnInit {
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('kvDialog') dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('kvHeading') heading?: ElementRef<HTMLElement>;

  /** Save Anyway was chosen for the refusal on screen. */
  @Output() saveAnyway = new EventEmitter<void>();
  /** The dialog closed, by Cancel, Escape, the close button or the host. */
  @Output() closed = new EventEmitter<void>();

  provider = '';
  refusal: ApiKeyRefusal | null = null;
  /** True from Save Anyway until the host closes the dialog or shows a new outcome. */
  saving = false;

  readonly describeResponse = describeCheckResponse;
  readonly formatElapsed = formatElapsed;

  get isOpen(): boolean {
    return !!this.dialog?.nativeElement.open;
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  /** Shows a refusal, opening the dialog if it is closed. */
  open(provider: string, refusal: ApiKeyRefusal): void {
    this.provider = provider;
    this.refusal = refusal;
    this.saving = false;
    this.cdr.detectChanges();
    const dialog = this.dialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    this.heading?.nativeElement.focus();
  }

  close(): void {
    const dialog = this.dialog?.nativeElement;
    if (dialog?.open) {
      dialog.close();
    }
  }

  onSaveAnyway(): void {
    if (this.saving) {
      return;
    }
    this.saving = true;
    this.cdr.markForCheck();
    this.saveAnyway.emit();
  }

  /** The dialog's own close and cancel events stop here, short of any dialog or page around it. */
  onDialogEvent(event: Event): void {
    event.stopPropagation();
    if (event.type === 'close') {
      this.saving = false;
      this.cdr.markForCheck();
      this.closed.emit();
    }
  }
}
