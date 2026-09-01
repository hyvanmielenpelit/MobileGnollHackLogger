import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Subject } from 'rxjs';

export interface UserAiSettings {
  hasApiKey: boolean;
  hasModel?: boolean;
  maxAttachmentSize?: number;
  spoilerFreeMode: boolean;
  showSourceCodeReferences?: boolean;
  showParallelBadge?: boolean;
  parallelBadgeEnabled?: boolean;
  maxResultLength?: number | null;
  maxCallsPerSession?: number | null;
  maxToolIterations?: number | null;
  maxParallelToolCalls?: number | null;
  showThoughtsAndTools?: number;
  enableWebSearch?: boolean;
  enableToolUse?: boolean;
  enableSubAgents?: boolean;
  enableClientTools?: boolean;
  enableGameActions?: boolean;
  showDebugLog?: boolean;
  requestTimeout?: number;
  configuredProviders?: string[];
  performanceLimits?: any;
  titleGenerationModelId?: number | null;
  titleGenerationSystemModelId?: number | null;
  titleGenerationDisabled?: boolean;
}

export interface ApiModelDto {
  id: string;
  displayName: string;
  createdAt: number;
  description: string;
  supportedThinkingLevels: string[];
  supportedReasoningModes: string[];
  supportedReasoningSummaries: string[];
  supportedServiceTiers?: string[];
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
  displayNameMode?: string;
  thinkingLevel?: string;
  reasoningMode?: string;
  reasoningSummary?: string;
  serviceTier?: string;
  orderIndex?: number;
  maxInputTokens?: number | null;
  maxOutputTokens?: number | null;
  isSystem?: boolean;
  modelRole?: number;
  parallelExecutionMode?: number;
}

export interface ApiKeyStatus {
  provider: string;
  hasKey: boolean;
  parallelExecutionMode?: number;
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

  getSettingsResponse() {
    return this.http.get<UserAiSettings>('/api/settings', {
      observe: 'response',
      headers: {
        'Cache-Control': 'no-cache',
        'Pragma': 'no-cache',
        'Expires': '0'
      }
    });
  }

  saveSettings(spoilerFreeMode: boolean, enableWebSearch: boolean, enableToolUse: boolean, enableSubAgents: boolean, enableClientTools: boolean, enableGameActions: boolean, showSourceCodeReferences: boolean, maxResultLength: number | null, maxCallsPerSession: number | null, maxToolIterations: number | null, maxParallelToolCalls: number | null, showThoughtsAndTools: number, requestTimeout: number | null, showParallelBadge?: boolean) {
    return this.http.put('/api/settings', {
      spoilerFreeMode,
      enableWebSearch,
      enableToolUse,
      enableSubAgents,
      enableClientTools,
      enableGameActions,
      showSourceCodeReferences,
      maxResultLength,
      maxCallsPerSession,
      maxToolIterations,
      maxParallelToolCalls,
      showThoughtsAndTools,
      requestTimeout,
      showParallelBadge
    });
  }

  saveTitleGenerationModel(modelId: number | null, isSystem: boolean = false, disabled?: boolean) {
    return this.http.put('/api/settings/titlemodel', { modelId, isSystem, disabled });
  }

  getApiKeys() {
    return this.http.get<ApiKeyStatus[]>('/api/settings/apikeys', {
      headers: {
        'Cache-Control': 'no-cache',
        'Pragma': 'no-cache',
        'Expires': '0'
      }
    });
  }

  saveApiKey(provider: string, apiKey: string) {
    return this.http.put('/api/settings/apikeys', { provider, apiKey });
  }

  saveApiKeyParallelMode(provider: string, mode: number) {
    return this.http.put(`/api/settings/apikeys/${provider}/parallel`, { mode });
  }

  deleteApiKeyForProvider(provider: string) {
    return this.http.delete(`/api/settings/apikeys/${provider}`);
  }

  getUserModels() {
    return this.http.get<UserAiModel[]>('/api/settings/usermodels', {
      headers: {
        'Cache-Control': 'no-cache',
        'Pragma': 'no-cache',
        'Expires': '0'
      }
    });
  }

  addUserModel(provider: string, modelId: string, displayName?: string, displayNameMode?: string, thinkingLevel?: string, reasoningMode?: string, reasoningSummary?: string, serviceTier?: string, maxInputTokens?: number | null, maxOutputTokens?: number | null) {
    return this.http.post<{ id: number }>('/api/settings/usermodels', { provider, modelId, displayName, displayNameMode, thinkingLevel, reasoningMode, reasoningSummary, serviceTier, maxInputTokens, maxOutputTokens });
  }

  updateUserModel(id: number, displayName?: string, displayNameMode?: string, thinkingLevel?: string, reasoningMode?: string, reasoningSummary?: string, serviceTier?: string, maxInputTokens?: number | null, maxOutputTokens?: number | null) {
    return this.http.put(`/api/settings/usermodels/${id}`, { displayName, displayNameMode, thinkingLevel, reasoningMode, reasoningSummary, serviceTier, maxInputTokens, maxOutputTokens });
  }

  deleteUserModel(id: number) {
    return this.http.delete(`/api/settings/usermodels/${id}`);
  }

  reorderUserModels(orderedIds: number[]) {
    return this.http.put('/api/settings/usermodels/reorder', { orderedIds });
  }

  reorderSystemModels(orderedIds: number[]) {
    return this.http.put('/api/settings/systemmodels/reorder', { orderedIds });
  }

  resetSystemModelsOrder() {
    return this.http.put('/api/settings/systemmodels/reorder/reset', {});
  }

  getAvailableModels(provider: string, apiKey: string, systemConfigId?: number) {
    return this.http.post<ApiModelDto[]>('/api/settings/models', { provider, apiKey, systemConfigId });
  }
}
