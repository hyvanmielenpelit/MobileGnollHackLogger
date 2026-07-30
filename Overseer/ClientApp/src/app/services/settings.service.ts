import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

export interface UserAiSettings {
  provider: string;
  model: string;
  thinkingLevel?: string;
  hasApiKey: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class SettingsService {
  private http = inject(HttpClient);

  getSettings() {
    return this.http.get<UserAiSettings>('/api/settings');
  }

  saveSettings(provider: string, model: string, apiKey: string, thinkingLevel?: string) {
    return this.http.put('/api/settings', { provider, model, apiKey, thinkingLevel });
  }

  getAvailableModels(provider: string, apiKey: string) {
    return this.http.post<string[]>('/api/settings/models', { provider, apiKey });
  }
}
