import { Component, OnInit, inject, ChangeDetectionStrategy, ViewChild, ElementRef } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { SettingsService, ApiKeyStatus } from '../services/settings.service';

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
  newKeys: Record<string, string> = {};
  
  loading = false;
  savingProvider = '';
  deletingProvider: string | null = null;
  
  ngOnInit() {
    this.loadStatuses();
  }

  loadStatuses() {
    this.loading = true;
    this.settingsService.getApiKeys().subscribe({
      next: (statuses) => {
        this.keyStatuses = {};
        for (const status of statuses) {
          this.keyStatuses[status.provider] = status.hasKey;
        }
        this.loading = false;
      },
      error: () => this.loading = false
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
