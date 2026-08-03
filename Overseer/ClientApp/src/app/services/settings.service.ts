import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

export interface UserAiSettings {
  hasApiKey: boolean;
  hasModel?: boolean;
  allowMultipleModels?: boolean;
  maxAttachmentSize?: number;
  spoilerFreeMode: boolean;
  showSourceCodeReferences?: boolean;
  maxResultLength?: number | null;
  maxCallsPerSession?: number | null;
  maxToolIterations?: number | null;
  enableWebSearch?: boolean;
  enableToolUse?: boolean;
  enableClientTools?: boolean;
  enableGameActions?: boolean;
  isProduction?: boolean;
  configuredProviders?: string[];
  performanceLimits?: any;
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
  maxInputTokens?: number | null;
  maxOutputTokens?: number | null;
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

  saveSettings(spoilerFreeMode: boolean = false, enableWebSearch: boolean = true, enableToolUse: boolean = true, enableClientTools: boolean = true, enableGameActions: boolean = false, allowMultipleModels: boolean = false, showSourceCodeReferences: boolean = false, maxResultLength: number | null = null, maxCallsPerSession: number | null = null, maxToolIterations: number | null = null) {
    return this.http.put('/api/settings', { spoilerFreeMode, enableWebSearch, enableToolUse, enableClientTools, enableGameActions, allowMultipleModels, showSourceCodeReferences, maxResultLength, maxCallsPerSession, maxToolIterations });
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

  addUserModel(provider: string, modelId: string, displayName?: string, thinkingLevel?: string, maxInputTokens?: number | null, maxOutputTokens?: number | null) {
    return this.http.post<{ id: number }>('/api/settings/usermodels', { provider, modelId, displayName, thinkingLevel, maxInputTokens, maxOutputTokens });
  }

  updateUserModel(id: number, displayName?: string, thinkingLevel?: string, maxInputTokens?: number | null, maxOutputTokens?: number | null) {
    return this.http.put(`/api/settings/usermodels/${id}`, { displayName, thinkingLevel, maxInputTokens, maxOutputTokens });
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
