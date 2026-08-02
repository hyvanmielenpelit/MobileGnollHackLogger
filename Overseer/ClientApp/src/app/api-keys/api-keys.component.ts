import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService, ApiKeyStatus } from '../services/settings.service';

@Component({
  selector: 'app-api-keys',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './api-keys.component.html',
  styleUrl: './api-keys.component.scss'
})
export class ApiKeysComponent implements OnInit {
  settingsService = inject(SettingsService);

  providers = ['OpenAI', 'Anthropic', 'Google'];
  keyStatuses: Record<string, boolean> = {};
  newKeys: Record<string, string> = {};
  
  loading = false;
  savingProvider = '';
  
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

  deleteKey(provider: string) {
    if (confirm(`Are you sure you want to delete your saved API key for ${provider}?`)) {
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
}
