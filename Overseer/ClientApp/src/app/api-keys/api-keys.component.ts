import { Component, OnInit, OnDestroy, inject, ChangeDetectionStrategy, ViewChild, ElementRef } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import {
  SettingsService,
  ApiKeyStatus,
  ApiKeySaveResult,
  CONFIDENTIALITY_POSTURES,
  confidentialityPostureLabel
} from '../services/settings.service';
import { KeyVerificationDialogComponent } from '../shared/key-verification/key-verification-dialog.component';
import {
  ApiKeyVerification,
  readApiKeyRefusal,
  readServerMessage,
  verificationLabel,
  verificationTooltip
} from '../shared/key-verification/key-verification';
import { ensureOverlayPolyfills } from '../utils/polyfills.util';

/** An inline error under a provider's key input: the message, then the failure detail when there is one. */
export interface ApiKeyFieldError {
  message: string;
  detail: string | null;
}

/** The three states of the per-key confidential-trust decision, as the `<select>` carries them. */
export type ConfidentialTrustChoice = 'yes' | 'no' | 'undecided';

/** The postures a personal key may declare. The endpoint-dependent two are not among them. */
const USER_KEY_POSTURES = CONFIDENTIALITY_POSTURES.filter(p => p.userKeySelectable);

/** How long the posture "Saved" confirmation stays on screen, in milliseconds. */
const POSTURE_SAVED_MS = 3000;

@Component({
    selector: 'app-api-keys',
    imports: [FormsModule, RouterModule, KeyVerificationDialogComponent],
    templateUrl: './api-keys.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './api-keys.component.scss'
})
export class ApiKeysComponent implements OnInit, OnDestroy {
  settingsService = inject(SettingsService);
  @ViewChild('deleteConfirmDialog') deleteConfirmDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('advancedInfoDialog') advancedInfoDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild(KeyVerificationDialogComponent) verificationDialog?: KeyVerificationDialogComponent;

  providers = ['Anthropic', 'Google', 'OpenAI'];
  keyStatuses: Record<string, boolean> = {};
  keyParallelModes: Record<string, number> = {};
  newKeys: Record<string, string> = {};

  /** The last check of each saved key. A null status (a key saved before checks existed) has no label. */
  keyVerifications: Record<string, ApiKeyVerification | null> = {};
  keyErrors: Record<string, ApiKeyFieldError | null> = {};
  keyWarnings: Record<string, string | null> = {};
  /** The `role="status"` line of each provider card. */
  keyStatusLines: Record<string, string> = {};
  verifyingProvider: string | null = null;
  /** The provider the key verification dialog is about. */
  private pendingProvider: string | null = null;

  readonly verificationLabel = verificationLabel;
  readonly verificationTooltip = verificationTooltip;

  // Rebuilt only when a saved posture changes, so the <select> is not handed a fresh array on
  // every change-detection pass, which would re-render its options and drop the selection.
  private postureOptionLists: Record<string, typeof CONFIDENTIALITY_POSTURES> = {};

  // Editable posture state, plus the last saved values it is compared against.
  keyPostures: Record<string, string> = {};
  keyPostureNotes: Record<string, string> = {};
  private savedPostures: Record<string, string> = {};
  private savedPostureNotes: Record<string, string> = {};
  keyPostureDeclaredUtc: Record<string, string | null> = {};

  keyTrusts: Record<string, ConfidentialTrustChoice> = {};
  keyTrustDecidedUtc: Record<string, string | null> = {};

  loading = false;
  savingProvider = '';
  savingParallelProvider = '';
  savingPostureProvider = '';
  savingTrustProvider = '';
  postureErrors: Record<string, string> = {};
  trustErrors: Record<string, string> = {};
  deletingProvider: string | null = null;

  /** The provider whose posture save is currently being confirmed, or '' when none is. */
  postureSavedProvider = '';
  private postureSavedTimer?: ReturnType<typeof setTimeout>;

  ngOnInit() {
    ensureOverlayPolyfills();
    this.loadStatuses();
  }

  ngOnDestroy() {
    if (this.postureSavedTimer) {
      clearTimeout(this.postureSavedTimer);
    }
  }

  loadStatuses() {
    this.loading = true;
    this.settingsService.getApiKeys().subscribe({
      next: (statuses) => {
        this.keyStatuses = {};
        this.keyParallelModes = {};
        this.keyPostures = {};
        this.keyPostureNotes = {};
        this.savedPostures = {};
        this.savedPostureNotes = {};
        this.keyPostureDeclaredUtc = {};
        this.keyTrusts = {};
        this.keyTrustDecidedUtc = {};
        this.keyVerifications = {};
        for (const status of statuses) {
          this.keyStatuses[status.provider] = status.hasKey;
          this.keyParallelModes[status.provider] = status.parallelExecutionMode ?? 2;
          this.keyVerifications[status.provider] = status.hasKey ? (status.verification ?? null) : null;
          this.applyPostureState(status);
        }
        this.loading = false;
      },
      error: () => this.loading = false
    });
  }

  private applyPostureState(status: ApiKeyStatus) {
    const posture = status.confidentialityPosture || 'Unknown';
    const note = status.confidentialityNote || '';
    this.keyPostures[status.provider] = posture;
    this.keyPostureNotes[status.provider] = note;
    this.savedPostures[status.provider] = posture;
    this.savedPostureNotes[status.provider] = note;
    this.keyPostureDeclaredUtc[status.provider] = status.postureDeclaredUtc ?? null;
    this.keyTrusts[status.provider] = status.userTrustsForConfidential === true
      ? 'yes'
      : status.userTrustsForConfidential === false ? 'no' : 'undecided';
    this.keyTrustDecidedUtc[status.provider] = status.confidentialTrustDecidedUtc ?? null;
    this.refreshPostureOptions(status.provider);
  }

  /**
   * The posture choices for one key: the four a personal key can describe, plus the saved value
   * when a legacy row holds one of the other two, so it still displays and can be changed away
   * from.
   */
  private refreshPostureOptions(provider: string) {
    const saved = this.savedPostures[provider] || 'Unknown';
    const legacy = CONFIDENTIALITY_POSTURES.find(p => p.value === saved && !p.userKeySelectable);
    this.postureOptionLists[provider] = legacy ? [...USER_KEY_POSTURES, legacy] : USER_KEY_POSTURES;
  }

  postureOptions(provider: string): typeof CONFIDENTIALITY_POSTURES {
    return this.postureOptionLists[provider] ?? USER_KEY_POSTURES;
  }

  /** The posture as last saved, so the badge names a stored claim rather than a pending edit. */
  savedPostureLabel(provider: string): string {
    return confidentialityPostureLabel(this.savedPostures[provider] || 'Unknown');
  }

  /** The badge text beside the posture select. An undeclared posture is an absence, not a claim. */
  savedPostureBadge(provider: string): string {
    const saved = this.savedPostures[provider] || 'Unknown';
    return saved === 'Unknown' ? 'Not recorded' : 'Self-declared: ' + confidentialityPostureLabel(saved);
  }

  /**
   * The parts of this card's advanced settings that are not at their default, for the collapsed
   * summary line. Empty when everything is default.
   */
  advancedSummary(provider: string): string {
    const parts: string[] = [];

    const mode = this.keyParallelModes[provider] ?? 2;
    if (mode === 1) {
      parts.push('On request');
    } else if (mode === 0) {
      parts.push('Sequential only');
    }

    const trust = this.keyTrusts[provider] ?? 'undecided';
    if (trust === 'no') {
      parts.push('Not for confidential chats');
    } else if (trust === 'yes') {
      parts.push('Allowed for confidential chats');
    }

    if ((this.savedPostures[provider] || 'Unknown') !== 'Unknown') {
      parts.push(this.savedPostureLabel(provider));
    }

    return parts.join(' · ');
  }

  isPostureDirty(provider: string): boolean {
    return (this.keyPostures[provider] || 'Unknown') !== (this.savedPostures[provider] || 'Unknown')
      || (this.keyPostureNotes[provider] || '') !== (this.savedPostureNotes[provider] || '');
  }

  savePosture(provider: string) {
    const posture = this.keyPostures[provider] || 'Unknown';
    const note = (this.keyPostureNotes[provider] || '').trim();
    this.savingPostureProvider = provider;
    this.postureErrors[provider] = '';

    // Unknown is the absence of a declaration, so it is sent as a clear rather than as a name.
    this.settingsService.saveApiKeyPosture(provider, posture === 'Unknown' ? null : posture, note || null).subscribe({
      next: () => {
        this.savedPostures[provider] = posture;
        this.savedPostureNotes[provider] = note;
        this.keyPostureNotes[provider] = note;
        // The server clears the declaration stamp along with the posture itself.
        this.keyPostureDeclaredUtc[provider] = posture === 'Unknown' ? null : new Date().toISOString();
        this.refreshPostureOptions(provider);
        this.flagPostureSaved(provider);
        this.savingPostureProvider = '';
      },
      error: (err) => {
        this.postureErrors[provider] = err?.error?.message || 'Could not save the posture. Please try again.';
        this.savingPostureProvider = '';
      }
    });
  }

  /** Shows the transient "Saved" confirmation for one provider, replacing any pending one. */
  private flagPostureSaved(provider: string) {
    if (this.postureSavedTimer) {
      clearTimeout(this.postureSavedTimer);
    }
    this.postureSavedProvider = provider;
    this.postureSavedTimer = setTimeout(() => {
      this.postureSavedProvider = '';
      this.postureSavedTimer = undefined;
    }, POSTURE_SAVED_MS);
  }

  openAdvancedInfo() {
    this.advancedInfoDialog?.nativeElement.showModal();
  }

  closeAdvancedInfo() {
    this.advancedInfoDialog?.nativeElement.close();
  }

  onTrustChange(provider: string, choice: ConfidentialTrustChoice) {
    const previous = this.keyTrusts[provider] ?? 'undecided';
    this.keyTrusts[provider] = choice;
    this.savingTrustProvider = provider;
    this.trustErrors[provider] = '';

    const trusted = choice === 'yes' ? true : choice === 'no' ? false : null;
    this.settingsService.saveApiKeyConfidentialTrust(provider, trusted).subscribe({
      next: () => {
        this.keyTrustDecidedUtc[provider] = trusted === null ? null : new Date().toISOString();
        this.savingTrustProvider = '';
      },
      error: (err) => {
        this.keyTrusts[provider] = previous;
        this.trustErrors[provider] = err?.error?.message || 'Could not save the decision. Please try again.';
        this.savingTrustProvider = '';
      }
    });
  }

  /** Local calendar date for a stored UTC timestamp, or '' when nothing has been recorded. */
  formatStamp(iso: string | null | undefined): string {
    if (!iso) return '';
    const parsed = new Date(iso);
    return isNaN(parsed.getTime()) ? '' : parsed.toLocaleDateString();
  }

  onParallelModeChange(provider: string, mode: number) {
    const numMode = Number(mode);
    this.keyParallelModes[provider] = numMode;
    this.savingParallelProvider = provider;
    this.settingsService.saveApiKeyParallelMode(provider, numMode).subscribe({
      next: () => {
        this.savingParallelProvider = '';
      },
      error: () => {
        this.savingParallelProvider = '';
      }
    });
  }

  keyInputId(provider: string): string {
    return 'key-' + provider;
  }

  keyErrorId(provider: string): string {
    return 'key-error-' + provider;
  }

  onKeyInput(provider: string) {
    if (this.keyErrors[provider]) {
      this.keyErrors[provider] = null;
    }
  }

  /** Checks the key with its provider, then saves it. The input keeps the key until a save succeeds. */
  saveKey(provider: string, saveUnverified = false) {
    const key = (this.newKeys[provider] || '').trim();
    if (!key || this.savingProvider) return;

    this.savingProvider = provider;
    this.keyErrors[provider] = null;
    this.keyWarnings[provider] = null;
    this.keyStatusLines[provider] = `Checking the key with ${provider}…`;
    this.settingsService.saveApiKey(provider, key, saveUnverified).subscribe({
      next: (result) => this.onKeySaved(provider, result),
      error: (err: HttpErrorResponse) => this.onKeySaveFailed(provider, err)
    });
  }

  private onKeySaved(provider: string, result: ApiKeySaveResult | null) {
    this.savingProvider = '';
    this.keyStatuses[provider] = true;
    this.newKeys[provider] = '';
    this.keyVerifications[provider] = result?.verification ?? null;
    this.keyWarnings[provider] = result?.warning?.trim() || null;
    this.keyStatusLines[provider] = result?.verification?.status === 'NotVerified'
      ? `The ${provider} key was saved as Not verified.`
      : `The ${provider} key was saved.`;
    this.pendingProvider = null;
    this.verificationDialog?.close();
  }

  private onKeySaveFailed(provider: string, err: HttpErrorResponse) {
    this.savingProvider = '';
    this.keyStatusLines[provider] = '';
    const refusal = readApiKeyRefusal(err);

    if (refusal?.verdict === 'unverifiable') {
      this.pendingProvider = provider;
      this.verificationDialog?.open(provider, refusal);
      return;
    }

    this.pendingProvider = null;
    this.verificationDialog?.close();
    this.keyErrors[provider] = refusal
      ? { message: refusal.message || `${provider} rejected the key.`, detail: refusal.detail?.text?.trim() || null }
      : { message: readServerMessage(err) ?? `The ${provider} key could not be saved.`, detail: null };
  }

  /** Save Anyway in the key verification dialog: the same key again, with `saveUnverified`. */
  onSaveAnyway() {
    if (this.pendingProvider) {
      this.saveKey(this.pendingProvider, true);
    }
  }

  onVerificationDialogClosed() {
    const provider = this.pendingProvider;
    this.pendingProvider = null;
    if (provider && this.savingProvider !== provider) {
      document.getElementById(this.keyInputId(provider))?.focus();
    }
  }

  /** Re-checks the stored key; the label updates in place and the outcome is announced. */
  verifyKey(provider: string) {
    if (this.verifyingProvider) return;
    this.verifyingProvider = provider;
    this.keyErrors[provider] = null;
    this.keyStatusLines[provider] = `Checking the key with ${provider}…`;
    this.settingsService.verifyApiKey(provider).subscribe({
      next: (result) => {
        this.verifyingProvider = null;
        this.keyVerifications[provider] = result.verification;
        this.keyStatusLines[provider] = result.verification?.status === 'Verified'
          ? `The ${provider} key is verified.`
          : `The ${provider} key is still not verified.`;
      },
      error: (err: HttpErrorResponse) => {
        this.verifyingProvider = null;
        this.keyStatusLines[provider] = '';
        this.keyErrors[provider] = {
          message: readServerMessage(err) ?? `The ${provider} key could not be checked.`,
          detail: null
        };
      }
    });
  }

  requestDeleteKey(provider: string) {
    this.deletingProvider = provider;
    this.deleteConfirmDialog?.nativeElement.showModal();
  }

  closeDeleteConfirmDialog() {
    this.deleteConfirmDialog?.nativeElement.close();
    this.deletingProvider = null;
  }

  confirmDeleteKey() {
    if (!this.deletingProvider) return;
    const provider = this.deletingProvider;
    this.closeDeleteConfirmDialog();
    this.savingProvider = provider;
    this.settingsService.deleteApiKeyForProvider(provider).subscribe({
      next: () => {
        this.keyStatuses[provider] = false;
        this.keyVerifications[provider] = null;
        this.keyWarnings[provider] = null;
        this.keyStatusLines[provider] = '';
        this.savingProvider = '';
      },
      error: () => this.savingProvider = ''
    });
  }
}
