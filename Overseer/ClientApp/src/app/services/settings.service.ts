import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Subject } from 'rxjs';

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
  showThoughtsAndTools?: number;
  enableWebSearch?: boolean;
  enableToolUse?: boolean;
  enableClientTools?: boolean;
  enableGameActions?: boolean;
  isProduction?: boolean;
  requestTimeout?: number;
  configuredProviders?: string[];
  performanceLimits?: any;
  titleGenerationModelId?: number | null;
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
  public showThoughtsAndToolsUpdated = new Subject<number>();

  getSettings() {
    return this.http.get<UserAiSettings>('/api/settings', {
      headers: {
        'Cache-Control': 'no-cache',
        'Pragma': 'no-cache',
        'Expires': '0'
      }
    });
  }

  saveSettings(spoilerFreeMode: boolean, enableWebSearch: boolean, enableToolUse: boolean, enableClientTools: boolean, enableGameActions: boolean, allowMultipleModels: boolean, showSourceCodeReferences: boolean, maxResultLength: number | null, maxCallsPerSession: number | null, maxToolIterations: number | null, showThoughtsAndTools: number, requestTimeout: number | null) {
    return this.http.put('/api/settings', {
      spoilerFreeMode,
      enableWebSearch,
      enableToolUse,
      enableClientTools,
      enableGameActions,
      allowMultipleModels,
      showSourceCodeReferences,
      maxResultLength,
      maxCallsPerSession,
      maxToolIterations,
      showThoughtsAndTools,
      requestTimeout
    });
  }

  saveTitleGenerationModel(modelId: number | null) {
    return this.http.put('/api/settings/titlemodel', { modelId });
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
