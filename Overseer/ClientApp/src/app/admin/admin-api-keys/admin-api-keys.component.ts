import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
  inject
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Subscription } from 'rxjs';
import {
  AdminService,
  DefaultApiKeyDeletionCheck,
  DefaultApiKeySaveResult,
  DefaultApiKeyStatus
} from '../../services/admin.service';
import { KeyVerificationDialogComponent } from '../../shared/key-verification/key-verification-dialog.component';
import {
  readApiKeyRefusal,
  readServerMessage,
  verificationLabel,
  verificationTooltip
} from '../../shared/key-verification/key-verification';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';

/** The providers that can have a default key, in display order. */
export const DEFAULT_KEY_PROVIDERS: readonly string[] = ['Anthropic', 'Google', 'OpenAI'];

/** An inline error under a card's key input: the message, then the failure detail when there is one. */
export interface DefaultKeyError {
  message: string;
  detail: string | null;
}

/**
 * Admin → API Keys: the per-provider default keys that system configurations set to Default use.
 * The host owns the list (`statuses`) and reloads it on `changed`; an answer this tab receives is
 * shown at once and replaced by the next list the host passes.
 */
@Component({
  selector: 'app-admin-api-keys',
  imports: [FormsModule, KeyVerificationDialogComponent],
  templateUrl: './admin-api-keys.component.html',
  styleUrl: './admin-api-keys.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AdminApiKeysComponent implements OnInit, OnChanges, OnDestroy {
  private readonly adminService = inject(AdminService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

  @Input() statuses: DefaultApiKeyStatus[] = [];
  /** True while the host's list has not arrived yet. */
  @Input() loading = false;

  /** After every save, verification and delete, so the host reloads the keys and the configurations. */
  @Output() changed = new EventEmitter<void>();

  @ViewChild(KeyVerificationDialogComponent) verificationDialog?: KeyVerificationDialogComponent;
  @ViewChild('deleteDefaultKeyDialog') deleteDialog?: ElementRef<HTMLDialogElement>;

  readonly providers = DEFAULT_KEY_PROVIDERS;
  readonly verificationLabel = verificationLabel;
  readonly verificationTooltip = verificationTooltip;

  /** Answers received since the host's last list, by provider. */
  private received: Record<string, DefaultApiKeyStatus> = {};

  keyInputs: Record<string, string> = {};
  /** Providers whose saved key is being replaced, so the input shows. */
  replacing: Record<string, boolean> = {};
  errors: Record<string, DefaultKeyError | null> = {};
  warnings: Record<string, string | null> = {};
  /** The `role="status"` line of each card. */
  statusLines: Record<string, string> = {};

  savingProvider: string | null = null;
  verifyingProvider: string | null = null;
  /** The provider the key verification dialog is about. */
  private pendingProvider: string | null = null;

  // Delete dialog
  deleteProvider: string | null = null;
  deleteCheck: DefaultApiKeyDeletionCheck | null = null;
  deleteCheckLoading = false;
  deleteError: string | null = null;
  deleting = false;

  private subs = new Subscription();

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['statuses']) {
      this.received = {};
    }
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
  }

  statusFor(provider: string): DefaultApiKeyStatus {
    return this.received[provider]
      ?? this.statuses.find(s => s.provider.toLowerCase() === provider.toLowerCase())
      ?? {
        provider,
        hasKey: false,
        keyHint: null,
        updatedAtUtc: null,
        verification: { status: null, checkedAtUtc: null, message: null },
        usedBy: []
      };
  }

  /** The input shows for a provider without a key, and while a saved key is being replaced. */
  showsInput(provider: string): boolean {
    return !this.statusFor(provider).hasKey || !!this.replacing[provider];
  }

  usedByText(status: DefaultApiKeyStatus): string {
    const n = status.usedBy.length;
    return n === 1 ? 'Used by 1 system configuration' : `Used by ${n} system configurations`;
  }

  inputId(provider: string): string {
    return `aak-key-${provider}`;
  }

  errorId(provider: string): string {
    return `aak-error-${provider}`;
  }

  onKeyInput(provider: string): void {
    if (this.errors[provider]) {
      this.errors[provider] = null;
    }
  }

  startReplace(provider: string): void {
    this.replacing[provider] = true;
    this.errors[provider] = null;
    this.warnings[provider] = null;
    this.statusLines[provider] = '';
    this.cdr.detectChanges();
    this.focusInput(provider);
  }

  cancelReplace(provider: string): void {
    this.replacing[provider] = false;
    this.keyInputs[provider] = '';
    this.errors[provider] = null;
    this.cdr.markForCheck();
  }

  // --- Saving ---

  save(provider: string, saveUnverified = false): void {
    if (this.savingProvider) {
      return;
    }
    const key = (this.keyInputs[provider] ?? '').trim();
    if (!key) {
      this.errors[provider] = { message: `Enter the ${provider} key first.`, detail: null };
      this.cdr.markForCheck();
      this.focusInput(provider);
      return;
    }

    this.savingProvider = provider;
    this.errors[provider] = null;
    this.warnings[provider] = null;
    this.statusLines[provider] = `Checking the key with ${provider}…`;
    this.cdr.markForCheck();

    this.subs.add(this.adminService.saveDefaultApiKey(provider, key, saveUnverified).subscribe({
      next: result => this.onSaved(provider, result),
      error: (err: HttpErrorResponse) => this.onSaveFailed(provider, err)
    }));
  }

  private onSaved(provider: string, result: DefaultApiKeySaveResult): void {
    this.savingProvider = null;
    this.received[provider] = result.status;
    this.keyInputs[provider] = '';
    this.replacing[provider] = false;
    this.warnings[provider] = result.warning?.trim() || null;

    const notVerified = result.status.verification?.status === 'NotVerified';
    const n = result.updatedConfigCount;
    const received = n === 0
      ? 'No system configuration uses it yet.'
      : n === 1 ? '1 system configuration received it.' : `${n} system configurations received it.`;
    this.statusLines[provider] = `The ${provider} key was saved${notVerified ? ' as Not verified' : ''}. ${received}`;

    this.pendingProvider = null;
    this.verificationDialog?.close();
    this.cdr.markForCheck();
    this.changed.emit();
  }

  private onSaveFailed(provider: string, err: HttpErrorResponse): void {
    this.savingProvider = null;
    this.statusLines[provider] = '';
    const refusal = readApiKeyRefusal(err);

    if (refusal?.verdict === 'unverifiable') {
      this.pendingProvider = provider;
      this.cdr.markForCheck();
      this.verificationDialog?.open(provider, refusal);
      return;
    }

    this.pendingProvider = null;
    this.verificationDialog?.close();
    this.errors[provider] = refusal
      ? { message: refusal.message || `${provider} rejected the key.`, detail: refusal.detail?.text?.trim() || null }
      : { message: readServerMessage(err) ?? `The ${provider} key could not be saved.`, detail: null };
    this.cdr.detectChanges();
    this.focusInput(provider);
  }

  /** Save Anyway in the key verification dialog: the same key again, with `saveUnverified`. */
  onSaveAnyway(): void {
    if (this.pendingProvider) {
      this.save(this.pendingProvider, true);
    }
  }

  onVerificationDialogClosed(): void {
    const provider = this.pendingProvider;
    this.pendingProvider = null;
    if (provider && this.savingProvider !== provider) {
      this.focusInput(provider);
    }
  }

  // --- Verify Again ---

  verifyAgain(provider: string): void {
    if (this.verifyingProvider) {
      return;
    }
    this.verifyingProvider = provider;
    this.errors[provider] = null;
    this.statusLines[provider] = `Checking the key with ${provider}…`;
    this.cdr.markForCheck();

    this.subs.add(this.adminService.verifyDefaultApiKey(provider).subscribe({
      next: result => {
        this.verifyingProvider = null;
        this.received[provider] = result.status;
        this.statusLines[provider] = result.status.verification?.status === 'Verified'
          ? `The ${provider} key is verified.`
          : `The ${provider} key is still not verified.`;
        this.cdr.markForCheck();
        this.changed.emit();
      },
      error: (err: HttpErrorResponse) => {
        this.verifyingProvider = null;
        this.statusLines[provider] = '';
        this.errors[provider] = {
          message: readServerMessage(err) ?? `The ${provider} key could not be checked.`,
          detail: null
        };
        this.cdr.markForCheck();
      }
    }));
  }

  // --- Deleting ---

  requestDelete(provider: string): void {
    this.deleteProvider = provider;
    this.deleteCheck = null;
    this.deleteError = null;
    this.deleteCheckLoading = true;
    this.deleting = false;
    this.cdr.detectChanges();
    const dialog = this.deleteDialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }

    this.subs.add(this.adminService.getDefaultApiKeyDeletionCheck(provider).subscribe({
      next: check => {
        if (this.deleteProvider !== provider) return;
        this.deleteCheck = check;
        this.deleteCheckLoading = false;
        this.cdr.markForCheck();
      },
      error: (err: HttpErrorResponse) => {
        if (this.deleteProvider !== provider) return;
        this.deleteCheckLoading = false;
        this.deleteError = readServerMessage(err)
          ?? 'Could not find out which configurations use this key. Deleting it still disables them.';
        this.cdr.markForCheck();
      }
    }));
  }

  cancelDelete(): void {
    this.deleteDialog?.nativeElement.close();
  }

  confirmDelete(): void {
    const provider = this.deleteProvider;
    if (!provider || this.deleting) {
      return;
    }
    this.deleting = true;
    this.deleteError = null;
    this.cdr.markForCheck();

    this.subs.add(this.adminService.deleteDefaultApiKey(provider).subscribe({
      next: result => {
        this.deleting = false;
        this.received[provider] = {
          ...this.statusFor(provider),
          hasKey: false,
          keyHint: null,
          updatedAtUtc: null,
          verification: { status: null, checkedAtUtc: null, message: null },
          usedBy: []
        };
        this.replacing[provider] = false;
        this.warnings[provider] = null;
        this.errors[provider] = null;
        const n = result.disabledCount;
        this.statusLines[provider] = n === 0
          ? `The ${provider} default key was deleted.`
          : `The ${provider} default key was deleted. ${n === 1 ? '1 system configuration was' : n + ' system configurations were'} disabled.`;
        this.deleteDialog?.nativeElement.close();
        this.cdr.markForCheck();
        this.changed.emit();
      },
      error: (err: HttpErrorResponse) => {
        this.deleting = false;
        this.deleteError = readServerMessage(err) ?? `The ${provider} default key could not be deleted.`;
        this.cdr.markForCheck();
      }
    }));
  }

  onDeleteDialogClosed(event: Event): void {
    event.stopPropagation();
    this.deleteProvider = null;
    this.deleteCheck = null;
    this.deleteError = null;
    this.cdr.markForCheck();
  }

  /** The delete dialog's cancel event stops here, as its close event does. */
  stopDialogEvent(event: Event): void {
    event.stopPropagation();
  }

  private focusInput(provider: string): void {
    const input = this.host.nativeElement.querySelector<HTMLInputElement>('#' + this.inputId(provider));
    input?.focus();
  }
}
