import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

export interface UserAiSettings {
  provider: string;
  model: string;
  thinkingLevel?: string;
  hasApiKey: boolean;
  hasModel?: boolean;
  allowMultipleModels?: boolean;
  maxAttachmentSize?: number;
  spoilerFreeMode: boolean;
  maxInputTokens?: number | null;
  maxOutputTokens?: number | null;
  enableWebSearch?: boolean;
  enableToolUse?: boolean;
  enableClientTools?: boolean;
  enableGameActions?: boolean;
  isProduction?: boolean;
}

export interface ApiModelDto {
  id: string;
  createdAt: number;
  description: string;
  supportedThinkingLevels: string[];
  contextWindowSize: number;
  maxInputTokens: number;
  maxOutputTokens: number;
  isRecommended?: boolean;
  recommendationRank?: number;
  recommendedThinkingLevel?: string;
}

export interface UserAiModel {
  id?: number;
  provider: string;
  modelId: string;
  displayName?: string;
  thinkingLevel?: string;
  orderIndex?: number;
}

export interface ApiKeyStatus {
  provider: string;
  hasKey: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class SettingsService {
  private http = inject(HttpClient);

  getSettings() {
    return this.http.get<UserAiSettings>('/api/settings');
  }

  saveSettings(provider: string, model: string, apiKey: string, thinkingLevel?: string, spoilerFreeMode: boolean = false, maxInputTokens: number | null = null, maxOutputTokens: number | null = null, enableWebSearch: boolean = true, enableToolUse: boolean = true, enableClientTools: boolean = true, enableGameActions: boolean = false, allowMultipleModels: boolean = false) {
    return this.http.put('/api/settings', { provider, model, apiKey, thinkingLevel, spoilerFreeMode, maxInputTokens, maxOutputTokens, enableWebSearch, enableToolUse, enableClientTools, enableGameActions, allowMultipleModels });
  }

  deleteApiKey() {
    return this.http.delete('/api/settings/apikey');
  }

  getApiKeys() {
    return this.http.get<ApiKeyStatus[]>('/api/settings/apikeys');
  }

  saveApiKey(provider: string, apiKey: string) {
    return this.http.put('/api/settings/apikeys', { provider, apiKey });
  }

  deleteApiKeyForProvider(provider: string) {
    return this.http.delete(`/api/settings/apikeys/${provider}`);
  }

  getUserModels() {
    return this.http.get<UserAiModel[]>('/api/settings/usermodels');
  }

  addUserModel(provider: string, modelId: string, displayName?: string, thinkingLevel?: string) {
    return this.http.post<{ id: number }>('/api/settings/usermodels', { provider, modelId, displayName, thinkingLevel });
  }

  updateUserModel(id: number, displayName?: string, thinkingLevel?: string) {
    return this.http.put(`/api/settings/usermodels/${id}`, { displayName, thinkingLevel });
  }

  deleteUserModel(id: number) {
    return this.http.delete(`/api/settings/usermodels/${id}`);
  }

  reorderUserModels(orderedIds: number[]) {
    return this.http.put('/api/settings/usermodels/reorder', { orderedIds });
  }

  getAvailableModels(provider: string, apiKey: string) {
    return this.http.post<ApiModelDto[]>('/api/settings/models', { provider, apiKey });
  }
}
