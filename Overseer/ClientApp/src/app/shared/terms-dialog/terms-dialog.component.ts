import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  ElementRef,
  EventEmitter,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';
import { MarkdownPipe } from '../../chat/markdown.pipe';
import { LoadingIndicatorComponent } from '../loading-indicator/loading-indicator.component';
import {
  OVERSEER_TERMS_KEY,
  TermsAcceptRequest,
  TermsDocument,
  TermsService,
  formatTermsVersion,
  isTermsVersionChanged,
  withoutLastUpdatedLine
} from '../../services/terms.service';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';

/** `view` reads the Terms; `gate` asks the user to accept them before continuing. */
export type TermsDialogMode = 'view' | 'gate';

/** Emitted by `accepted`. */
export interface TermsAcceptedEvent {
  /** True when the server started the greeting of the chat passed to `open`. */
  greetingStarted: boolean;
}

/** The title shown until the document arrives. */
export const DEFAULT_TERMS_TITLE = 'Gnoll Overseer Terms of Service';

/**
 * The Gnoll Overseer Terms of Service in a modal dialog. In `view` mode it light-dismisses and has a
 * close button. In `gate` mode it cannot be dismissed: the user checks the agreement and chooses
 * **Accept and continue**, or **Not now**. Its own close and cancel events stop here.
 */
@Component({
  selector: 'app-terms-dialog',
  imports: [MarkdownPipe, LoadingIndicatorComponent],
  templateUrl: './terms-dialog.component.html',
  styleUrl: './terms-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TermsDialogComponent implements OnInit {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);
  private readonly terms = inject(TermsService);

  @ViewChild('termsDialog') dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('termsHeading') heading?: ElementRef<HTMLElement>;
  @ViewChild('termsText') region?: ElementRef<HTMLElement>;

  /** The user accepted the Terms in gate mode and the server recorded it; the dialog has closed. */
  @Output() accepted = new EventEmitter<TermsAcceptedEvent>();
  /** The user chose Not now in gate mode; the dialog has closed. */
  @Output() declined = new EventEmitter<void>();
  /** The dialog closed, for any reason. */
  @Output() closed = new EventEmitter<void>();

  mode: TermsDialogMode = 'view';
  doc: TermsDocument | null = null;
  loading = false;
  loadError = false;
  /** The agreement checkbox, gate mode only. */
  agreed = false;
  submitting = false;
  submitError: string | null = null;
  /** The accept request was refused because the Terms changed; the new text is shown. */
  termsChanged = false;

  private sessionId: number | null = null;
  private loadSubscription?: Subscription;
  /** Set by the component's own closes, so a gate close from anywhere else can be undone. */
  private closing = false;

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.destroyRef.onDestroy(() => this.loadSubscription?.unsubscribe());
  }

  get isOpen(): boolean {
    return !!this.dialog?.nativeElement.open;
  }

  get title(): string {
    return this.doc?.title || DEFAULT_TERMS_TITLE;
  }

  /** The text below the Last updated line, which is shown separately. */
  get body(): string {
    return withoutLastUpdatedLine(this.doc?.markdown);
  }

  get lastUpdated(): string | null {
    return this.doc?.versionLabel || formatTermsVersion(this.doc?.version);
  }

  /** True when the document can be accepted: it is loaded and has a version, even when only the wiki link could be shown. */
  get acceptable(): boolean {
    return !!this.doc?.version && !this.loading;
  }

  /** Opens the dialog in the given mode; `sessionId` names the chat whose greeting waits on acceptance. */
  open(mode: TermsDialogMode, sessionId?: number | null): void {
    this.mode = mode;
    this.sessionId = sessionId ?? null;
    this.agreed = false;
    this.submitting = false;
    this.submitError = null;
    this.termsChanged = false;
    this.cdr.detectChanges();
    const dialog = this.dialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    this.scrollTextToTop();
    this.heading?.nativeElement.focus();
    this.load(false);
  }

  /** Closes the dialog without accepting or declining. */
  close(): void {
    const dialog = this.dialog?.nativeElement;
    if (dialog?.open) {
      this.closing = true;
      dialog.close();
    }
  }

  retryLoad(): void {
    this.load(true);
  }

  onAgreeChange(event: Event): void {
    this.agreed = (event.target as HTMLInputElement).checked;
    this.submitError = null;
  }

  /** The Terms of Service link in the agreement: back to the top of the text, without navigating. */
  showTermsFromTop(event: Event): void {
    event.preventDefault();
    this.scrollTextToTop();
    this.region?.nativeElement.focus({ preventScroll: true });
  }

  decline(): void {
    this.close();
    this.declined.emit();
  }

  accept(): void {
    const doc = this.doc;
    if (!doc || !this.agreed || this.submitting || !this.acceptable) {
      return;
    }
    const body: TermsAcceptRequest = { version: doc.version, contentSha256: doc.contentSha256 };
    if (this.sessionId !== null) {
      body.sessionId = this.sessionId;
    }
    this.submitting = true;
    this.submitError = null;
    this.terms.accept(OVERSEER_TERMS_KEY, body)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.submitting = false;
          this.cdr.markForCheck();
          this.close();
          this.accepted.emit({ greetingStarted: !!result?.greetingStarted });
        },
        error: (error: unknown) => {
          this.submitting = false;
          if (isTermsVersionChanged(error)) {
            this.agreed = false;
            this.termsChanged = true;
            this.load(true, true);
          } else {
            this.submitError = acceptErrorText(error);
          }
          this.cdr.markForCheck();
        }
      });
  }

  /** Escape: refused in gate mode. The event stops here, short of any dialog around it. */
  onCancel(event: Event): void {
    event.stopPropagation();
    if (this.mode === 'gate') {
      event.preventDefault();
    }
  }

  /**
   * The dialog's close event stops here. A gate closed by anything but the component itself (a
   * repeated Escape in a browser without `closedby`) is shown again.
   */
  onClose(event: Event): void {
    event.stopPropagation();
    const dialog = this.dialog?.nativeElement;
    if (!dialog || event.target !== dialog) {
      return;
    }
    const intended = this.closing;
    this.closing = false;
    if (this.mode === 'gate' && !intended) {
      if (!dialog.open && dialog.isConnected) {
        dialog.showModal();
        this.heading?.nativeElement.focus();
      }
      return;
    }
    this.cdr.markForCheck();
    this.closed.emit();
  }

  /** Light dismiss in view mode where `closedby` is unsupported: a click outside the border box closes it. */
  onDialogClick(event: MouseEvent): void {
    if (this.mode !== 'view' || 'closedBy' in HTMLDialogElement.prototype) {
      return;
    }
    const dialog = this.dialog?.nativeElement;
    if (!dialog || event.target !== dialog) {
      return;
    }
    const rect = dialog.getBoundingClientRect();
    const inside = rect.top <= event.clientY && event.clientY <= rect.top + rect.height
      && rect.left <= event.clientX && event.clientX <= rect.left + rect.width;
    if (!inside) {
      this.close();
    }
  }

  private load(reload: boolean, focusText = false): void {
    this.loadSubscription?.unsubscribe();
    this.loading = true;
    this.loadError = false;
    this.cdr.markForCheck();
    this.loadSubscription = this.terms.getTerms(OVERSEER_TERMS_KEY, reload).subscribe({
      next: doc => {
        this.doc = doc;
        this.loading = false;
        this.cdr.detectChanges();
        if (reload) {
          this.scrollTextToTop();
        }
        if (focusText) {
          this.region?.nativeElement.focus({ preventScroll: true });
        }
      },
      error: () => {
        this.loading = false;
        this.loadError = true;
        this.cdr.markForCheck();
      }
    });
  }

  private scrollTextToTop(): void {
    const region = this.region?.nativeElement;
    if (region) {
      region.scrollTop = 0;
    }
  }
}

/** The message for a failed accept that is not a Terms change. */
function acceptErrorText(error: unknown): string {
  if (error instanceof HttpErrorResponse && error.status === 0) {
    return 'The server could not be reached. Please try again.';
  }
  if (error instanceof HttpErrorResponse && error.status === 401) {
    return 'You are no longer signed in. Sign in again to accept the Terms.';
  }
  return 'Your acceptance could not be saved. Please try again.';
}
