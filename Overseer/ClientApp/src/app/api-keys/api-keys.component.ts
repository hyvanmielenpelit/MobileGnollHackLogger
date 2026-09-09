import { Component, OnInit, inject, ChangeDetectionStrategy, ViewChild, ElementRef } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import {
  SettingsService,
  ApiKeyStatus,
  CONFIDENTIALITY_POSTURES,
  confidentialityPostureLabel
} from '../services/settings.service';

/** The three states of the per-key confidential-trust decision, as the `<select>` carries them. */
export type ConfidentialTrustChoice = 'yes' | 'no' | 'undecided';

@Component({
    selector: 'app-api-keys',
    imports: [FormsModule, RouterModule],
    templateUrl: './api-keys.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './api-keys.component.scss'
})
export class ApiKeysComponent implements OnInit {
  settingsService = inject(SettingsService);
  @ViewChild('deleteConfirmDialog') deleteConfirmDialog?: ElementRef<HTMLDialogElement>;

  providers = ['Anthropic', 'Google', 'OpenAI'];
  keyStatuses: Record<string, boolean> = {};
  keyParallelModes: Record<string, number> = {};
  newKeys: Record<string, string> = {};

  readonly postures = CONFIDENTIALITY_POSTURES;

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

  ngOnInit() {
    this.loadStatuses();
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
        for (const status of statuses) {
          this.keyStatuses[status.provider] = status.hasKey;
          this.keyParallelModes[status.provider] = status.parallelExecutionMode ?? 2;
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
  }

  /** The posture as last saved, so the badge names a stored claim rather than a pending edit. */
  savedPostureLabel(provider: string): string {
    return confidentialityPostureLabel(this.savedPostures[provider] || 'Unknown');
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
        this.savingPostureProvider = '';
      },
      error: (err) => {
        this.postureErrors[provider] = err?.error?.message || 'Could not save the posture. Please try again.';
        this.savingPostureProvider = '';
      }
    });
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

  saveKey(provider: string) {
    const key = this.newKeys[provider];
    if (!key) return;

    this.savingProvider = provider;
    this.settingsService.saveApiKey(provider, key).subscribe({
      next: () => {
        this.keyStatuses[provider] = true;
        this.newKeys[provider] = '';
        this.savingProvider = '';
      },
      error: () => this.savingProvider = ''
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
        this.savingProvider = '';
      },
      error: () => this.savingProvider = ''
    });
  }
}
