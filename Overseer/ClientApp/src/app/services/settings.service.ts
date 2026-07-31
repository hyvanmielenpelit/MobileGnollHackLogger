import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

export interface UserAiSettings {
  provider: string;
  model: string;
  thinkingLevel?: string;
  hasApiKey: boolean;
  maxAttachmentSize?: number;
  spoilerFreeMode: boolean;
  maxInputTokens?: number | null;
  maxOutputTokens?: number | null;
}

export interface ApiModelDto {
  id: string;
  createdAt: number;
  description: string;
  supportedThinkingLevels: string[];
  contextWindowSize: number;
  maxInputTokens: number;
  maxOutputTokens: number;
}

@Injectable({
  providedIn: 'root'
})
export class SettingsService {
  private http = inject(HttpClient);

  getSettings() {
    return this.http.get<UserAiSettings>('/api/settings');
  }

  saveSettings(provider: string, model: string, apiKey: string, thinkingLevel?: string, spoilerFreeMode: boolean = false, maxInputTokens: number | null = null, maxOutputTokens: number | null = null) {
    return this.http.put('/api/settings', { provider, model, apiKey, thinkingLevel, spoilerFreeMode, maxInputTokens, maxOutputTokens });
  }

  deleteApiKey() {
    return this.http.delete('/api/settings/apikey');
  }

  getAvailableModels(provider: string, apiKey: string) {
    return this.http.post<ApiModelDto[]>('/api/settings/models', { provider, apiKey });
  }
}
