import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Subject } from 'rxjs';

export interface ModelPricingDto {
  inputPerMillion: number;
  outputPerMillion: number;
  cachedInputPerMillion?: number | null;
  cacheWritePerMillion?: number | null;
  asOf?: string | null;
}

/** How a confidential chat is stored. Ordered weakest to strictest by `CONFIDENTIAL_PERSISTENCE_RANKS`. */
export type ConfidentialPersistence = 'Encrypted' | 'Ephemeral' | 'Plaintext';

/** How strictly the funding key's retention posture must be established before a confidential turn runs. */
export type ConfidentialModelGate = 'UserDecides' | 'AskWhenUnclear' | 'VerifiedPostureOnly';

/** The administrator's floor. Read-only: the effective value of each setting is the stricter of this and the user's. */
export interface ConfidentialFloor {
  persistence: ConfidentialPersistence;
  retentionDays: number;
  disableToolEgress: boolean;
  disableTitleGeneration: boolean;
  disablePromptCache: boolean;
  immediatePurge: boolean;
  modelGate: ConfidentialModelGate;
}

/* Outbound DLP masking. Unlike Confidentiality Mode these are not per session and not opt-in
   per chat: a class switched on applies to every outbound turn, confidential or not. */

/** One switch per class of secret. */
export interface DlpSettings {
  dlpMaskApiKeys: boolean;
  dlpMaskPrivateKeys: boolean;
  dlpMaskTokens: boolean;
  dlpMaskCreditCards: boolean;
  dlpMaskSsns: boolean;
  dlpMaskEmails: boolean;
  dlpMaskPhoneNumbers: boolean;
}

/**
 * Which classes the administrator forces on. A class whose floor is true cannot be switched
 * off, so the control has to be shown as fixed rather than accepting a setting that silently
 * has no effect.
 */
export interface DlpFloor {
  apiKeys: boolean;
  privateKeys: boolean;
  tokens: boolean;
  creditCards: boolean;
  ssns: boolean;
  emails: boolean;
  phoneNumbers: boolean;
}

/** The seven user-owned confidentiality controls, as `PUT /api/settings` accepts them. */
export interface ConfidentialUserSettings {
  confidentialPersistence: ConfidentialPersistence;
  confidentialRetentionDays: number;
  confidentialDisableToolEgress: boolean;
  confidentialDisableTitleGeneration: boolean;
  confidentialDisablePromptCache: boolean;
  confidentialImmediatePurge: boolean;
  confidentialModelGate: ConfidentialModelGate;
}

export interface UserAiSettings {
  hasApiKey: boolean;
  hasModel?: boolean;
  maxAttachmentSize?: number;
  /* The file picker's accept list, served from the same allowlist the server enforces. Held
     here rather than duplicated in the template so the dialog cannot offer formats the server
     refuses. */
  attachmentAcceptExtensions?: string[];
  spoilerFreeMode: boolean;
  showSourceCodeReferences?: boolean;
  showParallelBadge?: boolean;
  parallelBadgeEnabled?: boolean;
  showContextWindowUsage?: boolean;
  showChatCost?: boolean;
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
  confidentialPersistence?: ConfidentialPersistence;
  confidentialRetentionDays?: number;
  confidentialDisableToolEgress?: boolean;
  confidentialDisableTitleGeneration?: boolean;
  confidentialDisablePromptCache?: boolean;
  confidentialImmediatePurge?: boolean;
  confidentialModelGate?: ConfidentialModelGate;
  confidentialFloor?: ConfidentialFloor | null;
  confidentialFirstUseNoticeAcknowledged?: boolean;

  /* The RESOLVED masking policy, not the raw preferences: a class the administrator forces on
     reads back as on, so a switch shows what actually happens rather than what the user last
     asked for. `dlpFloor` says which of them they cannot change. */
  dlpMaskApiKeys?: boolean;
  dlpMaskPrivateKeys?: boolean;
  dlpMaskTokens?: boolean;
  dlpMaskCreditCards?: boolean;
  dlpMaskSsns?: boolean;
  dlpMaskEmails?: boolean;
  dlpMaskPhoneNumbers?: boolean;
  dlpFloor?: DlpFloor | null;
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
  defaultThinkingLevel?: string;
  defaultPricing?: ModelPricingDto | null;
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
  pricingMode?: string;
  inputPricePerMillion?: number | null;
  outputPricePerMillion?: number | null;
  cachedInputPricePerMillion?: number | null;
  effectiveInputPricePerMillion?: number | null;
  effectiveOutputPricePerMillion?: number | null;
  effectiveCachedInputPricePerMillion?: number | null;
  pricingSource?: string | null;
  pricingAsOf?: string | null;
  /** Long-prompt rate card. Null for a flat-rate model and for every custom price override. */
  effectiveLongContextThresholdTokens?: number | null;
  effectiveLongContextInputPricePerMillion?: number | null;
  effectiveLongContextOutputPricePerMillion?: number | null;
  /** Multipliers keyed by the provider's served service tier. An unlisted tier costs 1.0. */
  effectiveServiceTierMultipliers?: { [tier: string]: number } | null;
  /**
   * An already-announced future price change. `pricingScheduleElapsed` means its date has passed, so the
   * effective rates above are already the scheduled ones — correct, but a sign the catalog entry should be
   * folded down and re-verified.
   */
  pricingScheduledChangeFrom?: string | null;
  pricingScheduledChangeInputPricePerMillion?: number | null;
  pricingScheduledChangeOutputPricePerMillion?: number | null;
  pricingScheduledChangeNote?: string | null;
  pricingScheduleElapsed?: boolean;
  /**
   * What has been agreed with the provider account behind this model's key, and whether an
   * operator dated that check. A non-null `postureVerifiedUtc` is the only thing that makes a
   * posture verified; without it the posture is a claim nobody has confirmed.
   */
  confidentialityPosture?: string | null;
  postureVerifiedUtc?: string | null;
  /** Where inference runs. Always null for a user's own model, which runs on their own key. */
  dataRegion?: string | null;
}

/** The provider-trust posture names, weakest to strongest. Persisted as the enum's string name. */
export type ConfidentialityPosture =
  'Unknown' | 'Standard' | 'NoTraining' | 'ZeroRetention' | 'PrivateCloud' | 'SelfHosted';

/** The selectable postures in ladder order, with the text the UI shows for each. */
export const CONFIDENTIALITY_POSTURES: ReadonlyArray<{ value: ConfidentialityPosture; label: string }> = [
  { value: 'Unknown', label: 'Not established' },
  { value: 'Standard', label: 'Standard provider terms' },
  { value: 'NoTraining', label: 'No training on content' },
  { value: 'ZeroRetention', label: 'Zero data retention' },
  { value: 'PrivateCloud', label: 'Private cloud deployment' },
  { value: 'SelfHosted', label: 'Self-hosted inference' }
];

/** Ladder position. A legacy null row is Unknown, the weakest rung. */
const POSTURE_RANKS: Record<string, number> = {
  Unknown: 0,
  Standard: 1,
  NoTraining: 2,
  ZeroRetention: 3,
  PrivateCloud: 4,
  SelfHosted: 5
};

/** Display text for a posture name. A null or unrecognised name reads as Unknown. */
export function confidentialityPostureLabel(posture: string | null | undefined): string {
  const match = CONFIDENTIALITY_POSTURES.find(p => p.value === posture);
  return match ? match.label : 'Not established';
}

export function confidentialityPostureRank(posture: string | null | undefined): number {
  return posture && POSTURE_RANKS[posture] !== undefined ? POSTURE_RANKS[posture] : 0;
}

/**
 * The three privacy levels the indicators share with the chat badge: `strong` is green,
 * `moderate` yellow, `weak` orange. Only an operator-verified posture of ZeroRetention or
 * stronger reaches `strong`; a self-declared or undated posture never does.
 */
export type PosturePrivacyLevel = 'strong' | 'moderate' | 'weak';

export function posturePrivacyLevel(
  posture: string | null | undefined,
  verifiedUtc: string | null | undefined
): PosturePrivacyLevel {
  const rank = confidentialityPostureRank(posture);
  if (rank <= POSTURE_RANKS['Standard']) {
    return 'weak';
  }
  return (verifiedUtc && rank >= POSTURE_RANKS['ZeroRetention']) ? 'strong' : 'moderate';
}

/** The storage modes in ladder order, weakest first, with the text and helper line the UI shows. */
export const CONFIDENTIAL_PERSISTENCE_OPTIONS: ReadonlyArray<{ value: ConfidentialPersistence; label: string; hint: string }> = [
  {
    value: 'Plaintext',
    label: 'Stored readable',
    hint: 'Kept in the database exactly as a normal chat is.'
  },
  {
    value: 'Encrypted',
    label: 'Encrypted at rest',
    hint: 'Message content is enveloped in the database, so the stored rows are not readable on their own.'
  },
  {
    value: 'Ephemeral',
    label: 'Never stored',
    hint: 'Nothing is written to the database. The conversation is gone once you leave it.'
  }
];

/** The model gates in ladder order, weakest first. */
export const CONFIDENTIAL_MODEL_GATE_OPTIONS: ReadonlyArray<{ value: ConfidentialModelGate; label: string; hint: string }> = [
  {
    value: 'UserDecides',
    label: 'I decide',
    hint: 'Any key you have may fund a confidential turn. You judge its retention terms yourself.'
  },
  {
    value: 'AskWhenUnclear',
    label: 'Ask when unclear',
    hint: 'Ask first whenever the funding key has no retention posture recorded.'
  },
  {
    value: 'VerifiedPostureOnly',
    label: 'Verified posture only',
    hint: 'Only run a confidential turn on a key whose retention posture an operator has verified.'
  }
];

/** Ladder position for a storage mode. An unrecognised name reads as the weakest rung. */
const CONFIDENTIAL_PERSISTENCE_RANKS: Record<string, number> = {
  Plaintext: 0,
  Encrypted: 1,
  Ephemeral: 2
};

/** Ladder position for a model gate. An unrecognised name reads as the weakest rung. */
const CONFIDENTIAL_MODEL_GATE_RANKS: Record<string, number> = {
  UserDecides: 0,
  AskWhenUnclear: 1,
  VerifiedPostureOnly: 2
};

export function confidentialPersistenceRank(persistence: string | null | undefined): number {
  return persistence && CONFIDENTIAL_PERSISTENCE_RANKS[persistence] !== undefined
    ? CONFIDENTIAL_PERSISTENCE_RANKS[persistence]
    : 0;
}

export function confidentialModelGateRank(gate: string | null | undefined): number {
  return gate && CONFIDENTIAL_MODEL_GATE_RANKS[gate] !== undefined
    ? CONFIDENTIAL_MODEL_GATE_RANKS[gate]
    : 0;
}

/** The stricter of two storage modes, which is the effective one. */
export function stricterConfidentialPersistence(
  a: ConfidentialPersistence,
  b: ConfidentialPersistence | null | undefined
): ConfidentialPersistence {
  if (!b) {
    return a;
  }
  return confidentialPersistenceRank(b) > confidentialPersistenceRank(a) ? b : a;
}

/** The stricter of two model gates, which is the effective one. */
export function stricterConfidentialModelGate(
  a: ConfidentialModelGate,
  b: ConfidentialModelGate | null | undefined
): ConfidentialModelGate {
  if (!b) {
    return a;
  }
  return confidentialModelGateRank(b) > confidentialModelGateRank(a) ? b : a;
}

/** Display text for a storage mode. */
export function confidentialPersistenceLabel(persistence: string | null | undefined): string {
  const match = CONFIDENTIAL_PERSISTENCE_OPTIONS.find(o => o.value === persistence);
  return match ? match.label : 'Encrypted at rest';
}

/** Display text for a model gate. */
export function confidentialModelGateLabel(gate: string | null | undefined): string {
  const match = CONFIDENTIAL_MODEL_GATE_OPTIONS.find(o => o.value === gate);
  return match ? match.label : 'I decide';
}

/** The retention window a confidential chat may be given, in days of inactivity. */
export const CONFIDENTIAL_RETENTION_MIN_DAYS = 1;
export const CONFIDENTIAL_RETENTION_MAX_DAYS = 365;
export const CONFIDENTIAL_RETENTION_DEFAULT_DAYS = 30;

export interface ApiKeyStatus {
  provider: string;
  hasKey: boolean;
  parallelExecutionMode?: number;
  /**
   * What the user says they have agreed with this provider account. Self-declared: Overseer
   * has no way to check it, so it never earns the verified treatment a system config can.
   */
  confidentialityPosture?: string | null;
  confidentialityNote?: string | null;
  postureDeclaredUtc?: string | null;
  /** `true` accepts the key for confidential chats, `false` refuses it, `null` is undecided. */
  userTrustsForConfidential?: boolean | null;
  confidentialTrustDecidedUtc?: string | null;
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

  /**
   * `confidential` carries the seven Confidentiality Mode fields in one trailing object, so the
   * positional list ahead of it keeps the shape every existing caller and spec relies on. A value
   * weaker than the administrator's floor is not an error; the server clamps it.
   */
  saveSettings(spoilerFreeMode: boolean, enableWebSearch: boolean, enableToolUse: boolean, enableSubAgents: boolean, enableClientTools: boolean, enableGameActions: boolean, showSourceCodeReferences: boolean, maxResultLength: number | null, maxCallsPerSession: number | null, maxToolIterations: number | null, maxParallelToolCalls: number | null, showThoughtsAndTools: number, requestTimeout: number | null, showParallelBadge?: boolean, showContextWindowUsage?: boolean, showChatCost?: boolean, confidential?: ConfidentialUserSettings) {
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
      showParallelBadge,
      showContextWindowUsage,
      showChatCost,
      ...(confidential ?? {})
    });
  }

  /** Marks the Confidentiality Mode first-use notice as seen, so it is not shown again. */
  /* Its own endpoint rather than more fields on PUT /api/settings, because these are the one
     group of settings that changes what leaves the server. A partial payload is fine: an
     omitted class is left alone. */
  saveDlpSettings(payload: Partial<DlpSettings>) {
    return this.http.post('/api/settings/dlp', payload);
  }

  acknowledgeConfidentialNotice() {
    return this.http.post('/api/settings/confidential-notice-acknowledged', {});
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

  /** `posture` is a `ConfidentialityPosture` name, or null to return the key to Unknown. */
  saveApiKeyPosture(provider: string, posture: string | null, note: string | null) {
    return this.http.put(`/api/settings/apikeys/${provider}/posture`, { posture, note });
  }

  /** `trusted` is true to accept the key for confidential chats, false to refuse it, null for undecided. */
  saveApiKeyConfidentialTrust(provider: string, trusted: boolean | null) {
    return this.http.put(`/api/settings/apikeys/${provider}/confidential-trust`, { trusted });
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

  addUserModel(provider: string, modelId: string, displayName?: string, displayNameMode?: string, thinkingLevel?: string, reasoningMode?: string, reasoningSummary?: string, serviceTier?: string, maxInputTokens?: number | null, maxOutputTokens?: number | null, pricingMode?: string, inputPricePerMillion?: number | null, outputPricePerMillion?: number | null, cachedInputPricePerMillion?: number | null) {
    return this.http.post<{ id: number }>('/api/settings/usermodels', { provider, modelId, displayName, displayNameMode, thinkingLevel, reasoningMode, reasoningSummary, serviceTier, maxInputTokens, maxOutputTokens, pricingMode, inputPricePerMillion, outputPricePerMillion, cachedInputPricePerMillion });
  }

  updateUserModel(id: number, displayName?: string, displayNameMode?: string, thinkingLevel?: string, reasoningMode?: string, reasoningSummary?: string, serviceTier?: string, maxInputTokens?: number | null, maxOutputTokens?: number | null, modelId?: string, provider?: string, pricingMode?: string, inputPricePerMillion?: number | null, outputPricePerMillion?: number | null, cachedInputPricePerMillion?: number | null) {
    return this.http.put(`/api/settings/usermodels/${id}`, { displayName, displayNameMode, thinkingLevel, reasoningMode, reasoningSummary, serviceTier, maxInputTokens, maxOutputTokens, modelId, provider, pricingMode, inputPricePerMillion, outputPricePerMillion, cachedInputPricePerMillion });
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
