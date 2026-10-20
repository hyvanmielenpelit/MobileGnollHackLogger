import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiKeyVerification } from '../shared/key-verification/key-verification';
import {
  ModelAvailability,
  ModelCatalogMode,
  ModelResolutionRequest,
  ModelResolutionResult
} from '../shared/model-availability/model-availability';

/** Counts of one security event kind in a range; `count` sums the runs' repeat counts. */
export interface SecurityKindCount {
  /** A `SecurityEventKind` name, e.g. "LoginFailed". */
  kind: string;
  count: number;
  distinctUsers: number;
  distinctIps: number;
}

export interface SecuritySummary {
  kinds: SecurityKindCount[];
  pendingDeletionRequests: number;
  activeAccountHolds: number;
}

/** One security event, or a run of identical ones within ten minutes (`count`). Identifiers and codes only. */
export interface SecurityEvent {
  id: number;
  occurredUtc: string;
  lastOccurredUtc: string;
  kind: string;
  count: number;
  userId?: string | null;
  userName?: string | null;
  userIsOverseerBlocked: boolean;
  ipAddress?: string | null;
  systemAiApiConfigurationId?: number | null;
  provider?: string | null;
  modelId?: string | null;
  code?: string | null;
  detail?: string | null;
}

export interface SecurityEventsPage {
  totalCount: number;
  rows: SecurityEvent[];
}

export interface SecurityEventsQuery {
  startDate?: string;
  endDate?: string;
  /** `SecurityEventKind` names. */
  kinds?: string[];
  userId?: string;
  /** Matches user names. */
  search?: string;
  skip?: number;
  /** Clamped to 1–100 by the server. */
  take?: number;
}

export interface SafetyIdentifierLookup {
  userId: string;
  userName: string;
}

export type AccountDeletionRequestStatus = 'Pending' | 'Completed' | 'Declined';

/** A held account deletion waiting for, or decided by, an administrator. */
export interface AccountDeletionRequest {
  id: number;
  /** Null once completed. */
  userId?: string | null;
  /** Null once completed (shown as "Deleted account"). */
  userName?: string | null;
  requestedUtc: string;
  ageDays: number;
  /** Pending and at least AccountHold:ReviewHighlightDays old. */
  isOverdue: boolean;
  status: AccountDeletionRequestStatus;
  /** Why it was held, as stored when the request was made. */
  holdReasons: string;
  /** The reasons as evaluated now; null once decided or when the account is gone. */
  currentReasons?: string | null;
  manualHold: boolean;
  automaticRule: boolean;
  decidedByUserName?: string | null;
  decidedUtc?: string | null;
  decisionNote?: string | null;
}

export interface AccountDeletionRequestsPage {
  totalCount: number;
  pendingCount: number;
  overdueCount: number;
  /** False while DumpLogPath or ConversationsDataLocation is missing; Approve is refused then. */
  erasurePathsConfigured: boolean;
  erasurePathsMessage?: string | null;
  rows: AccountDeletionRequest[];
}

export interface AccountDeletionRequestsQuery {
  /** Omitted: every status. */
  status?: AccountDeletionRequestStatus;
  search?: string;
  skip?: number;
  take?: number;
}

export interface AccountDeletionDecisionResult {
  emailSent: boolean;
  emailError?: string | null;
  /** Approve only: bones files not found on disk. */
  bonesFilesMissing: number;
  /** Approve only: dumplog and attachment folders not found on disk. */
  foldersMissing: number;
}

export interface GroupDto {
  id: number;
  displayName: string;
}

/**
 * What the server's custom-endpoint policy permits. An empty host allowlist is fail-closed,
 * so `customEndpointsEnabled` false means every base URL would be refused on save.
 */
export interface EndpointPolicySummaryDto {
  customEndpointsEnabled: boolean;
  allowedHostPatterns: string[];
  allowedHeaderNames: string[];
  allowLoopback: boolean;
}

export interface ModelPricingDto {
  inputPerMillion: number;
  outputPerMillion: number;
  cachedInputPerMillion?: number | null;
  cacheWritePerMillion?: number | null;
  asOf?: string | null;
}

export interface SystemAiConfigDto {
  id: number;
  displayName: string;
  displayNameMode?: string | null;
  provider: string;
  modelId: string;
  /** The catalog release date, yyyy-MM-dd; null when the catalog does not describe the model. */
  releaseDate?: string | null;
  thinkingLevel: string | null;
  reasoningMode: string | null;
  reasoningSummary: string | null;
  serviceTier: string | null;
  maxInputTokens: number | null;
  maxOutputTokens: number | null;
  orderIndex: number;
  isEnabled: boolean;
  /** True when the configuration holds a key, its own or a copy of the provider's default key. */
  hasApiKey: boolean;
  /** The configuration uses its provider's default key, which is copied into it. */
  useDefaultApiKey?: boolean;
  isSystemWide: boolean;
  maxDailyChatRequests: number | null;
  maxMonthlyChatRequests: number | null;
  maxTotalChatRequests: number | null;
  dailyChatRequestsCount: number;
  monthlyChatRequestsCount: number;
  totalChatRequestsCount: number;
  maxDailyTitleRequests: number | null;
  maxMonthlyTitleRequests: number | null;
  maxTotalTitleRequests: number | null;
  dailyTitleRequestsCount: number;
  monthlyTitleRequestsCount: number;
  totalTitleRequestsCount: number;
  maxDailyChatTokens: number | null;
  maxMonthlyChatTokens: number | null;
  maxTotalChatTokens: number | null;
  dailyChatTokensCount: number;
  monthlyChatTokensCount: number;
  totalChatTokensCount: number;
  maxDailyTitleTokens: number | null;
  maxMonthlyTitleTokens: number | null;
  maxTotalTitleTokens: number | null;
  dailyTitleTokensCount: number;
  monthlyTitleTokensCount: number;
  totalTitleTokensCount: number;
  modelRole: number;
  parallelExecutionMode: number;
  apiKey?: string;
  note?: string | null;
  userAssignmentCount?: number;
  groupAssignmentCount?: number;
  pricingMode?: string | null;
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
   * What has actually been agreed with this configuration's provider account, and where inference
   * runs. Nothing here is inferred from the model name. `postureVerifiedUtc` is the field that
   * grants verified status: an operator checked the posture against the agreement and dated it.
   */
  confidentialityPosture?: string | null;
  confidentialityNote?: string | null;
  postureAgreementRef?: string | null;
  postureVerifiedUtc?: string | null;
  dataRegion?: string | null;

  /**
   * Custom endpoint. Empty means the provider's official public endpoint. Validated
   * server-side: an unallowlisted host, a non-https scheme or a denied header name is a 400,
   * never a stored value that is silently ignored.
   */
  baseUrl?: string | null;
  customHeadersJson?: string | null;
  apiVersion?: string | null;

  /** Whether the model is still in the model catalog; absent from an older server. */
  modelAvailability?: ModelAvailability;
  modelCatalogMode?: ModelCatalogMode | null;
}

/** A system configuration named by a default key's status or deletion check. */
export interface DefaultApiKeyConfigRef {
  id: number;
  displayName: string;
  isEnabled: boolean;
}

/** One provider's default key. Never carries the key itself, only its last four characters. */
export interface DefaultApiKeyStatus {
  provider: string;
  hasKey: boolean;
  keyHint: string | null;
  updatedAtUtc: string | null;
  verification: ApiKeyVerification;
  /** The system configurations set to Default for this provider. */
  usedBy: DefaultApiKeyConfigRef[];
}

/** A saved default key, and how many configurations received the copy. */
export interface DefaultApiKeySaveResult {
  status: DefaultApiKeyStatus;
  updatedConfigCount: number;
  /** A rate limit or an exhausted quota: the key exists and is saved as verified. */
  warning: string | null;
}

export interface DefaultApiKeyVerifyResult {
  status: DefaultApiKeyStatus;
}

/** The configurations a delete of the default key would disable. */
export interface DefaultApiKeyDeletionCheck {
  count: number;
  configs: DefaultApiKeyConfigRef[];
}

export interface DefaultApiKeyDeleteResult {
  disabledCount: number;
}

/** Something using a system configuration right now, which a delete would interrupt. */
export interface SystemConfigBlockerDto {
  /**
   * "run", "series", "difficultyJob", "generationJob", "rubricCheckJob", "rubricGapAuthorJob",
   * "reportPackJob" or "runReportWriter" (a run whose AI-written reports are queued or being written).
   */
  kind: string;
  /** The run or series id, or the job id. */
  id: string;
  /** A benchmark run id when `kind` is "run" or "runReportWriter", so the UI can open it. */
  runId?: number | null;
  label: string;
  /** The roles the configuration plays, in words ("assessor", "claim verifier"). */
  roles: string[];
  startedAtUtc: string;
}

/** Whether a system configuration can be deleted now, and what a delete removes and keeps. */
export interface SystemConfigDeletionCheckDto {
  configId: number;
  displayName: string;
  canDelete: boolean;
  blockers: SystemConfigBlockerDto[];
  /** Benchmark runs naming the configuration. Informational: their history is kept. */
  benchmarkRunReferenceCount: number;
  /** Stopped series naming the configuration. Informational: resuming them will be refused. */
  stoppedSeriesCount: number;
  /** Stopped battery runs naming the configuration. Informational: resuming them will be refused. */
  stoppedBatteryRunCount: number;
  /** Removed with the configuration. */
  userAssignmentCount: number;
  /** Removed with the configuration. */
  groupAssignmentCount: number;
  /** Users' confidentiality decisions for this model. Removed with the configuration. */
  confidentialTrustCount: number;
}

export interface UserSystemAiConfigDto {
  id: number;
  systemAiApiConfigurationId: number;
  systemAiApiConfiguration?: SystemAiConfigDto;
  isEnabled: boolean;
  orderIndex: number;
  maxDailyChatRequests: number | null;
  maxMonthlyChatRequests: number | null;
  maxTotalChatRequests: number | null;
  dailyChatRequestsCount: number;
  monthlyChatRequestsCount: number;
  totalChatRequestsCount: number;
  maxDailyTitleRequests: number | null;
  maxMonthlyTitleRequests: number | null;
  maxTotalTitleRequests: number | null;
  dailyTitleRequestsCount: number;
  monthlyTitleRequestsCount: number;
  totalTitleRequestsCount: number;
  maxDailyChatTokens: number | null;
  maxMonthlyChatTokens: number | null;
  maxTotalChatTokens: number | null;
  dailyChatTokensCount: number;
  monthlyChatTokensCount: number;
  totalChatTokensCount: number;
  maxDailyTitleTokens: number | null;
  maxMonthlyTitleTokens: number | null;
  maxTotalTitleTokens: number | null;
  dailyTitleTokensCount: number;
  monthlyTitleTokensCount: number;
  totalTitleTokensCount: number;
  modelRole: number;
}

export interface GroupSystemAiConfigDto {
  id: number;
  systemAiApiConfigurationId: number;
  systemAiApiConfiguration?: SystemAiConfigDto;
  isEnabled: boolean;
  orderIndex: number;
  maxDailyChatRequests: number | null;
  maxMonthlyChatRequests: number | null;
  maxTotalChatRequests: number | null;
  dailyChatRequestsCount: number;
  monthlyChatRequestsCount: number;
  totalChatRequestsCount: number;
  maxDailyTitleRequests: number | null;
  maxMonthlyTitleRequests: number | null;
  maxTotalTitleRequests: number | null;
  dailyTitleRequestsCount: number;
  monthlyTitleRequestsCount: number;
  totalTitleRequestsCount: number;
  maxDailyChatTokens: number | null;
  maxMonthlyChatTokens: number | null;
  maxTotalChatTokens: number | null;
  dailyChatTokensCount: number;
  monthlyChatTokensCount: number;
  totalChatTokensCount: number;
  maxDailyTitleTokens: number | null;
  maxMonthlyTitleTokens: number | null;
  maxTotalTitleTokens: number | null;
  dailyTitleTokensCount: number;
  monthlyTitleTokensCount: number;
  totalTitleTokensCount: number;
  modelRole: number;
}

export interface AnalyticsUserRow {
  userId: string;
  userName: string;
  chatRequests: number;
  titleRequests: number;
  benchmarkRequests: number;
  inputTokens: number;
  outputTokens: number;
  cacheReadTokens: number;
  cacheCreationTokens: number;
  avgDurationMs: number;
}

export interface AnalyticsResponse {
  rows: AnalyticsUserRow[];
  totalCount: number;
}

/** The AI Telemetry key-source filter. */
export type AiTelemetryKeySource = 'system' | 'user' | 'both';

/** Usage-log role groups: Chat = RoleContext 1 and 3, Title Generation = 2, Benchmark = 4–8, Other = anything else. */
export type AiUsageRoleGroup = 'chat' | 'title' | 'benchmark' | 'other';

/**
 * One breakdown row: a key source, user, model, role context and thinking level. Token figures are normalized:
 * `promptTokens` is the whole prompt and `uncachedInputTokens` the part neither read from nor written to the cache.
 */
export interface AiUsageBreakdownRowDto {
  keySource: 'system' | 'user';
  /** User-key rows only. */
  userId?: string | null;
  /** The user's current name; user-key rows only. */
  userName?: string | null;
  provider: string;
  modelId: string;
  /** The catalog name, else the retired entry's, else null. */
  modelName?: string | null;
  roleContext: number;
  roleGroup: AiUsageRoleGroup;
  /** "" when none was sent, null when not recorded. */
  thinkingLevel?: string | null;
  requests: number;
  promptTokens: number;
  uncachedInputTokens: number;
  cacheReadTokens: number;
  cacheCreationTokens: number;
  outputTokens: number;
  durationSumMs: number;
  durationCount: number;
  estimatedCost: number;
  pricedRequests: number;
  validAttempts: number;
  providerErrors: number;
  unattributedErrors: number;
  ourSideFailures: number;
}

/** Provider availability of the Overseer chat and title generation, with GnollBench's classes and interval. */
export interface ChatProviderAvailabilityDto {
  /** The ledger has any row at all. */
  recorded: boolean;
  recordedSinceUtc?: string | null;
  validAttempts: number;
  providerErrors: number;
  unattributedErrors: number;
  ourSideFailures: number;
  /** Provider errors ÷ valid attempts; null at no valid attempt. */
  providerErrorRate?: number | null;
  /** The cluster-adjusted Wilson 95% interval. */
  low?: number | null;
  high?: number | null;
  /** (Provider + unattributed) ÷ valid attempts. */
  inclusiveRate?: number | null;
  overloaded: number;
  serverErrors: number;
  timeouts: number;
  streamFailures: number;
  rateLimitedUnattributed: number;
  transportUnattributed: number;
  calls: number;
  firstAttemptSuccessCalls: number;
  failedCalls: number;
  chatTurns: number;
  chatTurnsEndedByProviderError: number;
  titleValidAttempts: number;
  titleProviderErrors: number;
  titleRequests: number;
  titleRequestsFailed: number;
}

/** Users, chats and messages; independent of the key-source filter. */
export interface OverseerActivityDto {
  /** Distinct users with a chat or a visible message in the range. */
  activeUsers: number;
  /** Active users whose first activity day ever falls in the range. */
  newUsers: number;
  chatsCreated: number;
  confidentialChatsCreated: number;
  gnollHackChatsCreated: number;
  userMessages: number;
  assistantMessages: number;
  /** Counted only anonymously. */
  incognitoChatsCreated: number;
  incognitoUserMessages: number;
  /** The first counted day (the deployment day), "YYYY-MM-DD"; null before anything was counted. */
  countedSinceUtc?: string | null;
}

export interface AiTelemetrySummaryDto {
  keySource: AiTelemetryKeySource;
  totalRequests: number;
  totalChatRequests: number;
  totalTitleRequests: number;
  totalBenchmarkRequests: number;
  totalPromptTokens: number;
  totalUncachedInputTokens: number;
  totalOutputTokens: number;
  totalCacheReadTokens: number;
  totalCacheCreationTokens: number;
  /** Cache read ÷ prompt. */
  cacheHitRatio: number;
  avgDurationMs: number;
  /** Over priced rows only. */
  totalEstimatedCost: number;
  pricedRequests: number;
  /** The earliest priced row in the selected logs. */
  costRecordedSinceUtc?: string | null;
  rows: AiUsageBreakdownRowDto[];
  chatAvailability: ChatProviderAvailabilityDto;
  activity: OverseerActivityDto;
}

/** One system configuration a user used in the range, with that user's limits on it. */
export interface AiTelemetryUserConfigDto {
  configId: number;
  configName: string;
  requests: number;
  estimatedCost: number;
  /** The user's own assignment, if any. */
  assignmentId?: number | null;
  assignmentEnabled?: boolean | null;
  /** Effective (lazily reset) counters. */
  dailyChatRequests?: number | null;
  maxDailyChatRequests?: number | null;
  monthlyChatRequests?: number | null;
  maxMonthlyChatRequests?: number | null;
  dailyChatTokens?: number | null;
  maxDailyChatTokens?: number | null;
  limitSource: 'user' | 'group' | 'systemWide' | 'none';
}

/** One user's usage of system AI configurations. Busiest-hour figures are information for limits, never a misuse signal. */
export interface AiTelemetryUserRowDto {
  userId: string;
  userName: string;
  requests: number;
  chatRequests: number;
  titleRequests: number;
  benchmarkRequests: number;
  promptTokens: number;
  outputTokens: number;
  estimatedCost: number;
  pricedRequests: number;
  peakHourRequests: number;
  peakHourStartUtc?: string | null;
  requestsPerActiveHour: number;
  activeDays: number;
  lastRequestUtc?: string | null;
  chatsCreated: number;
  userMessages: number;
  limitRefusals: number;
  providerRefusals: number;
  isOverseerBlocked: boolean;
  /** An active deletion hold or a pending deletion request. */
  hasAccountHold: boolean;
  configs: AiTelemetryUserConfigDto[];
}

export interface AiTelemetryUsersPage {
  users: AiTelemetryUserRowDto[];
  totalUsers: number;
  securityEventsAvailable: boolean;
}

export type AiTelemetryUsersSort = 'cost' | 'requests' | 'tokens' | 'peakHour' | 'refusals' | 'lastActive';

export interface AiTelemetryUsersQuery {
  startDate?: string;
  endDate?: string;
  /** Matches user name or email. */
  search?: string;
  /** Restricts the page to one user. */
  userId?: string;
  sort?: AiTelemetryUsersSort;
  skip?: number;
  /** Clamped to 1–100 by the server. */
  take?: number;
}

export type AiTelemetryBucket = 'auto' | 'day' | 'week' | 'month';
export type AiTelemetrySplitBy = 'none' | 'role' | 'provider' | 'model' | 'keySource' | 'user';

export interface AiTelemetryPointDto {
  requests: number;
  promptTokens: number;
  cacheReadTokens: number;
  outputTokens: number;
  estimatedCost: number;
  pricedRequests: number;
  durationSumMs: number;
  durationCount: number;
  validAttempts: number;
  providerErrors: number;
}

export interface AiTelemetrySeriesLineDto {
  key: string;
  label: string;
  /** For provider colors (split by provider or model). */
  provider?: string | null;
  isOther: boolean;
  /** Aligned with bucketStartsUtc, zeros filled. */
  points: AiTelemetryPointDto[];
}

export interface AiTelemetryActivityPointDto {
  /** Distinct in the bucket, not a sum of days. */
  activeUsers: number;
  newUsers: number;
  chatsCreated: number;
  confidentialChatsCreated: number;
  gnollHackChatsCreated: number;
  incognitoChatsCreated: number;
  userMessages: number;
  assistantMessages: number;
  incognitoUserMessages: number;
}

export interface AiTelemetrySeriesDto {
  /** The resolved bucket. */
  bucket: 'day' | 'week' | 'month';
  splitBy: AiTelemetrySplitBy;
  bucketStartsUtc: string[];
  /** At most 8 lines plus one "Other" line. */
  series: AiTelemetrySeriesLineDto[];
  costRecordedSinceUtc?: string | null;
  attemptsRecordedSinceUtc?: string | null;
  userId?: string | null;
  userName?: string | null;
  /** Aligned with bucketStartsUtc. */
  activity?: AiTelemetryActivityPointDto[] | null;
  /** Buckets before it have no activity data ("YYYY-MM-DD"). */
  activityCountedSinceUtc?: string | null;
}

export interface AiTelemetrySeriesQuery {
  startDate?: string;
  endDate?: string;
  keys?: AiTelemetryKeySource;
  /** Role groups; empty: the three named groups. */
  roles?: AiUsageRoleGroup[];
  bucket?: AiTelemetryBucket;
  splitBy?: AiTelemetrySplitBy;
  userId?: string;
}

/** One user's usage of one model in one role context and thinking level; token figures normalized as the breakdown's. */
export interface AiTelemetryModelUserRowDto {
  userId: string;
  /** The user's current name; null when the account is gone. */
  userName?: string | null;
  roleContext: number;
  roleGroup: AiUsageRoleGroup;
  /** "" when none was sent, null when not recorded. */
  thinkingLevel?: string | null;
  requests: number;
  promptTokens: number;
  uncachedInputTokens: number;
  cacheReadTokens: number;
  cacheCreationTokens: number;
  outputTokens: number;
  durationSumMs: number;
  durationCount: number;
  estimatedCost: number;
  pricedRequests: number;
  lastRequestUtc?: string | null;
}

/** The users of one model in one key source and the requested role groups, over a date range. */
export interface AiTelemetryModelUsersDto {
  keySource: 'system' | 'user';
  provider: string;
  modelId: string;
  /** The catalog name, else the retired entry's, else null. */
  modelName?: string | null;
  /** The role groups included. */
  roles: AiUsageRoleGroup[];
  /** The earliest priced row of the chosen log in the range, of any model. */
  costRecordedSinceUtc?: string | null;
  rows: AiTelemetryModelUserRowDto[];
}

export interface AiTelemetryModelUsersQuery {
  startDate?: string;
  endDate?: string;
  keys: 'system' | 'user';
  provider: string;
  modelId: string;
  /** Role groups; empty: the three named groups. */
  roles: AiUsageRoleGroup[];
}

export interface AiGovernorKeyStatusDto {
  credentialKey: string;
  isRateLimited: boolean;
  remainingCooldownSeconds: number;
  inFlightCalls: number;
}

export interface AiGovernorStatusDto {
  maxConcurrentCalls: number;
  maxRetryAfterSeconds: number;
  activeKeys: AiGovernorKeyStatusDto[];
}

export interface TableStorageMetric {
  tableName: string;
  rowCount: number;
  totalSpaceMb: number;
  usedSpaceMb: number;
  /** Non-clustered index pages, in MB; included in usedSpaceMb. */
  indexSpaceMb: number;
}

export interface KeyVersionUsage {
  version: string;
  sessionCount: number;
  /** False means every session wrapped under this version is unreadable. */
  inRing: boolean;
  isActive: boolean;
}

/** The effective ChatRetentionSettings, echoed by the server. */
export interface RetentionPolicy {
  maxActiveSessionsPerUser: number;
  maxPinnedSessionsPerUser: number;
  inactivityTtlDays: number;
  softDeleteGracePeriodDays: number;
  pruneToolCallResultsDays: number;
  pruneBenchmarkToolCallResultsDays: number;
  auditLogRetentionDays: number;
  pruneDismissedAiErrorLogDays: number;
  pruneSecurityEventDays: number;
  pruneChatProviderAttemptDays: number;
  maintenanceHistoryRetentionDays: number;
  maintenanceRunHourUtc: number;
  enableStorageWarningEmails: boolean;
  reportEmailAddress?: string;
  emailSenderConfigured: boolean;
}

export interface DatabaseStorageMetrics {
  allocatedDataSizeMb: number;
  usedDataSizeMb: number;
  freeSpaceWithinLimitMb: number;
  /** Ceiling the meter is drawn against, in MB. Zero means no limit applies. */
  maxLimitMb: number;
  usedPercentage: number;
  tableMetrics: TableStorageMetric[];
  /** True when the database engine itself enforces maxLimitMb. */
  hasEngineSizeLimit: boolean;
  limitSource: 'Detected' | 'Configured' | 'Fallback';
  /** Display name of the instance, e.g. "SQL Server 2022 Express". */
  serverProductLabel: string;
  serverEditionName?: string;
  serverProductVersion?: string;
  activeSessionCount: number;
  softDeletedSessionCount: number;
  inactiveSessionCount: number;
  pinnedSessionCount: number;
  diskAttachmentsSizeBytes: number;
  diskAttachmentsSizeMb: number;
  diskAttachmentsFolderCount: number;
  diskAttachmentsFileCount: number;
  /** Benchmark:ReportPack:ChartsDataLocation is set to an absolute folder. */
  reportChartsConfigured?: boolean;
  /** Report chart folders: one per document, plus the staging folder when present. */
  reportChartFolderCount?: number;
  reportChartFileCount?: number;
  reportChartSizeBytes?: number;
  reportChartSizeMb?: number;
  estimatedReclaimableMb: number;
  lastMaintenanceRunUtc?: string;
  statusLevel: 'Normal' | 'Warning' | 'Critical';

  logAllocatedMb: number;
  logUsedMb: number;

  /** tableMetrics holds the largest tables; these summarise the rest. */
  otherTablesTotalSpaceMb: number;
  otherTablesCount: number;
  allTablesTotalSpaceMb: number;

  confidentialSessionCount: number;
  ownTtlSessionCount: number;
  immediatePurgeSessionCount: number;
  ephemeralSessionCount: number;
  activeContentKeyVersion?: string;
  contentKeyVersions: KeyVersionUsage[];
  sessionsWithUnknownKeyVersionCount: number;

  auditLogRowCount: number;
  auditLogOldestUtc?: string;
  auditLogPrunableCount: number;

  aiErrorLogUndismissedCount: number;
  aiErrorLogDismissedCount: number;
  aiErrorLogPrunableCount: number;

  securityEventRowCount: number;
  securityEventOldestUtc?: string;
  /** Excludes events of users with an active account hold or a pending deletion request. */
  securityEventPrunableCount: number;

  chatProviderAttemptRowCount: number;
  chatProviderAttemptOldestUtc?: string;
  chatProviderAttemptPrunableCount: number;

  expiredTrashSessionCount: number;
  prunableToolCallCount: number;
  prunableBenchmarkToolCallCount: number;
  nextScheduledMaintenanceUtc?: string;
  serviceStartedUtc: string;

  appliedMigrationCount: number;
  lastAppliedMigration?: string;
  pendingMigrations: string[];
  /** False when migration state could not be read. */
  schemaStatusAvailable: boolean;

  policy: RetentionPolicy;
}

export interface MaintenanceRequest {
  dryRun?: boolean;
  inactivityDays?: number;
  toolCallPruneDays?: number;
  benchmarkToolCallPruneDays?: number;
  /** Granular prune only; the full pass reads the configured window. */
  auditLogRetentionDays?: number;
  /** Granular prune only; the full pass reads the configured window. */
  aiErrorLogPruneDays?: number;
  /** Granular prune only; the full pass reads the configured window. */
  securityEventPruneDays?: number;
  /** Granular prune only; the full pass reads the configured window. */
  chatProviderAttemptPruneDays?: number;
}

export interface MaintenanceResult {
  success: boolean;
  isDryRun: boolean;
  softDeletedCount: number;
  purgedSessionCount: number;
  purgedMessageCount: number;
  purgedToolCallCount: number;
  prunedToolResultCount: number;
  prunedBenchmarkToolResultCount: number;
  deletedDiskFolderCount: number;
  deletedDiskFileCount: number;
  reclaimedDiskBytes: number;
  sweptOrphanFolderCount: number;
  prunedAuditLogCount: number;
  prunedAiErrorLogCount: number;
  prunedSecurityEventCount: number;
  prunedChatProviderAttemptCount: number;
  elapsedMilliseconds: number;
  /** "Scheduled", "Startup", "Manual", or "Manual:<Action>". */
  trigger: string;
  errorMessage?: string;
  logs: string[];
}

/** One recorded maintenance run: a full pass or a granular action, dry runs included. */
export interface MaintenanceRunLog {
  id: number;
  startedUtc: string;
  completedUtc?: string;
  trigger: string;
  isDryRun: boolean;
  success: boolean;
  elapsedMilliseconds: number;
  softDeletedCount: number;
  purgedSessionCount: number;
  purgedMessageCount: number;
  purgedToolCallCount: number;
  prunedToolResultCount: number;
  prunedBenchmarkToolResultCount: number;
  prunedAuditLogCount: number;
  prunedAiErrorLogCount: number;
  prunedSecurityEventCount: number;
  prunedChatProviderAttemptCount: number;
  deletedDiskFolderCount: number;
  deletedDiskFileCount: number;
  sweptOrphanFolderCount: number;
  reclaimedDiskBytes: number;
  errorMessage?: string | null;
  /** True when the run has log text or an error message to fetch with `getMaintenanceRunLog`. */
  hasLog: boolean;
  /**
   * Not sent by the history list. Undefined until `getMaintenanceRunLog` has filled it in;
   * null afterwards when the run has no log text.
   */
  logText?: string | null;
}

/** One page of maintenance runs, newest first, with the total across all pages. */
export interface MaintenanceHistoryPage {
  totalCount: number;
  rows: MaintenanceRunLog[];
}

/** The text of one maintenance run. */
export interface MaintenanceRunLogText {
  errorMessage?: string | null;
  logText?: string | null;
}

@Injectable({
  providedIn: 'root'
})
export class AdminService {
  private http = inject(HttpClient);

  // Analytics
  getConfigAnalytics(configId: number, params: {
    startDate?: string; endDate?: string;
    mode?: string; usernameFilter?: string;
    page?: number; pageSize?: number;
  }): Observable<AnalyticsResponse> {
    const httpParams: any = {};
    if (params.startDate) httpParams.startDate = params.startDate;
    if (params.endDate) httpParams.endDate = params.endDate;
    if (params.mode) httpParams.mode = params.mode;
    if (params.usernameFilter) httpParams.usernameFilter = params.usernameFilter;
    if (params.page) httpParams.page = params.page;
    if (params.pageSize) httpParams.pageSize = params.pageSize;
    return this.http.get<AnalyticsResponse>(
      `/api/admin/systemconfigs/${configId}/analytics`,
      { params: httpParams }
    );
  }

  // Security
  getSecuritySummary(startDate?: string, endDate?: string): Observable<SecuritySummary> {
    const params: any = {};
    if (startDate) params.startDate = startDate;
    if (endDate) params.endDate = endDate;
    return this.http.get<SecuritySummary>('/api/admin/security/summary', { params });
  }

  /** Newest first. */
  getSecurityEvents(query: SecurityEventsQuery): Observable<SecurityEventsPage> {
    const params: any = {};
    if (query.startDate) params.startDate = query.startDate;
    if (query.endDate) params.endDate = query.endDate;
    if (query.kinds?.length) params.kinds = query.kinds.join(',');
    if (query.userId) params.userId = query.userId;
    if (query.search) params.search = query.search;
    if (query.skip) params.skip = query.skip;
    if (query.take) params.take = query.take;
    return this.http.get<SecurityEventsPage>('/api/admin/security/events', { params });
  }

  /** 404 when no user has the identifier. */
  lookupSafetyIdentifier(identifier: string): Observable<SafetyIdentifierLookup> {
    return this.http.get<SafetyIdentifierLookup>(`/api/admin/security/safety-identifier/${encodeURIComponent(identifier)}`);
  }

  // Account deletion requests
  getAccountDeletionRequests(query: AccountDeletionRequestsQuery): Observable<AccountDeletionRequestsPage> {
    const params: any = {};
    if (query.status) params.status = query.status;
    if (query.search) params.search = query.search;
    if (query.skip) params.skip = query.skip;
    if (query.take) params.take = query.take;
    return this.http.get<AccountDeletionRequestsPage>('/api/admin/account-deletion-requests', { params });
  }

  /**
   * Erases the account. `confirmUserName` must equal the request's user name (400 otherwise); 409 while
   * the erasure paths are not configured.
   */
  approveAccountDeletion(id: number, confirmUserName: string): Observable<AccountDeletionDecisionResult> {
    return this.http.post<AccountDeletionDecisionResult>(`/api/admin/account-deletion-requests/${id}/approve`, { confirmUserName });
  }

  /** Keeps the account; `note` (1–512 characters) is for administrators and is not emailed. */
  declineAccountDeletion(id: number, note: string): Observable<AccountDeletionDecisionResult> {
    return this.http.post<AccountDeletionDecisionResult>(`/api/admin/account-deletion-requests/${id}/decline`, { note });
  }

  // Groups
  getGroups(): Observable<GroupDto[]> {
    return this.http.get<GroupDto[]>('/api/admin/groups');
  }
  
  createGroup(displayName: string): Observable<GroupDto> {
    return this.http.post<GroupDto>('/api/admin/groups', { displayName });
  }

  deleteGroup(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/groups/${id}`);
  }

  // User Groups
  addUserToGroup(userId: string, groupId: number): Observable<void> {
    return this.http.post<void>(`/api/admin/users/${userId}/groups`, { groupId });
  }

  removeUserFromGroup(userId: string, groupId: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/users/${userId}/groups/${groupId}`);
  }

  // System AI Configs
  getSystemConfigs(): Observable<SystemAiConfigDto[]> {
    return this.http.get<SystemAiConfigDto[]>('/api/admin/systemconfigs');
  }

  createSystemConfig(config: any): Observable<SystemAiConfigDto> {
    return this.http.post<SystemAiConfigDto>('/api/admin/systemconfigs', config);
  }

  updateSystemConfig(id: number, config: any): Observable<SystemAiConfigDto> {
    return this.http.put<SystemAiConfigDto>(`/api/admin/systemconfigs/${id}`, config);
  }

  deleteSystemConfig(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/systemconfigs/${id}`);
  }

  getSystemConfigDeletionCheck(id: number): Observable<SystemConfigDeletionCheckDto> {
    return this.http.get<SystemConfigDeletionCheckDto>(`/api/admin/systemconfigs/${id}/deletion-check`);
  }

  /**
   * Switches the configuration to a catalog model or keeps it as a custom one; `dryRun` only reports
   * the changes and the blockers. A real switch blocked by live use fails with 409, whose
   * `HttpErrorResponse.error` is `{ message, blockers }` (`ModelResolutionRefusal`).
   */
  resolveSystemConfig(id: number, request: ModelResolutionRequest): Observable<ModelResolutionResult<SystemAiConfigDto>> {
    return this.http.post<ModelResolutionResult<SystemAiConfigDto>>(`/api/admin/systemconfigs/${id}/model-resolution`, request);
  }

  resetSystemConfig(id: number, counterName?: string): Observable<void> {
    return this.http.post<void>(`/api/admin/systemconfigs/${id}/reset`, { counterName });
  }

  reorderSystemConfigs(ids: number[]): Observable<void> {
    return this.http.put<void>('/api/admin/systemconfigs/reorder', { orderedIds: ids });
  }

  // Default API keys, one per provider, in the order Anthropic, Google, OpenAI
  getDefaultApiKeys(): Observable<DefaultApiKeyStatus[]> {
    return this.http.get<DefaultApiKeyStatus[]>('/api/admin/default-api-keys');
  }

  /**
   * Checks the key with its provider, then saves it. A rejected key is a 400 refusal; an
   * unanswered check is a 409 refusal unless `saveUnverified` is set, which saves it as Not verified
   * when the repeated check is still unanswered.
   */
  saveDefaultApiKey(provider: string, apiKey: string, saveUnverified = false): Observable<DefaultApiKeySaveResult> {
    return this.http.put<DefaultApiKeySaveResult>(
      `/api/admin/default-api-keys/${encodeURIComponent(provider)}`, { apiKey, saveUnverified });
  }

  /** Re-checks the stored key and updates only its verification. 404 when there is no key. */
  verifyDefaultApiKey(provider: string): Observable<DefaultApiKeyVerifyResult> {
    return this.http.post<DefaultApiKeyVerifyResult>(
      `/api/admin/default-api-keys/${encodeURIComponent(provider)}/verify`, {});
  }

  getDefaultApiKeyDeletionCheck(provider: string): Observable<DefaultApiKeyDeletionCheck> {
    return this.http.get<DefaultApiKeyDeletionCheck>(
      `/api/admin/default-api-keys/${encodeURIComponent(provider)}/deletion-check`);
  }

  /** Deletes the key and disables the configurations that used it. 404 when there is no key. */
  deleteDefaultApiKey(provider: string): Observable<DefaultApiKeyDeleteResult> {
    return this.http.delete<DefaultApiKeyDeleteResult>(
      `/api/admin/default-api-keys/${encodeURIComponent(provider)}`);
  }

  // User System AI Configs
  getUserSystemConfigs(userId: string): Observable<UserSystemAiConfigDto[]> {
    return this.http.get<UserSystemAiConfigDto[]>(`/api/admin/users/${userId}/systemconfigs`);
  }

  createUserSystemConfig(userId: string, config: any): Observable<UserSystemAiConfigDto> {
    return this.http.post<UserSystemAiConfigDto>(`/api/admin/users/${userId}/systemconfigs`, config);
  }

  deleteUserSystemConfig(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/user-systemconfigs/${id}`);
  }

  updateUserSystemConfig(id: number, config: any): Observable<void> {
    return this.http.put<void>(`/api/admin/user-systemconfigs/${id}`, config);
  }

  resetUserSystemConfig(id: number, counterName?: string): Observable<void> {
    return this.http.post<void>(`/api/admin/user-systemconfigs/${id}/reset`, { counterName });
  }

  reorderUserSystemConfigs(userId: string, ids: number[]): Observable<void> {
    return this.http.put<void>(`/api/admin/users/${userId}/systemconfigs/reorder`, { orderedIds: ids });
  }

  // Group System AI Configs
  getGroupSystemConfigs(groupId: number): Observable<GroupSystemAiConfigDto[]> {
    return this.http.get<GroupSystemAiConfigDto[]>(`/api/admin/groups/${groupId}/systemconfigs`);
  }

  createGroupSystemConfig(groupId: number, config: any): Observable<GroupSystemAiConfigDto> {
    return this.http.post<GroupSystemAiConfigDto>(`/api/admin/groups/${groupId}/systemconfigs`, config);
  }

  deleteGroupSystemConfig(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/group-systemconfigs/${id}`);
  }

  updateGroupSystemConfig(id: number, config: any): Observable<void> {
    return this.http.put<void>(`/api/admin/group-systemconfigs/${id}`, config);
  }

  resetGroupSystemConfig(id: number, counterName?: string): Observable<void> {
    return this.http.post<void>(`/api/admin/group-systemconfigs/${id}/reset`, { counterName });
  }

  reorderGroupSystemConfigs(groupId: number, ids: number[]): Observable<void> {
    return this.http.put<void>(`/api/admin/groups/${groupId}/systemconfigs/reorder`, { orderedIds: ids });
  }

  triggerBackendSentryError(): Observable<any> {
    return this.http.post('/api/admin/test-sentry', {});
  }

  // Database Storage & Maintenance
  getStorageMetrics(): Observable<DatabaseStorageMetrics> {
    return this.http.get<DatabaseStorageMetrics>('/api/admin/storage-metrics');
  }

  runMaintenanceNow(request?: MaintenanceRequest): Observable<MaintenanceResult> {
    return this.http.post<MaintenanceResult>('/api/admin/maintenance/run-now', request || {});
  }

  purgeTrashNow(request?: MaintenanceRequest): Observable<MaintenanceResult> {
    return this.http.post<MaintenanceResult>('/api/admin/maintenance/purge-trash-now', request || {});
  }

  purgeInactive(request?: MaintenanceRequest): Observable<MaintenanceResult> {
    return this.http.post<MaintenanceResult>('/api/admin/maintenance/purge-inactive', request || {});
  }

  pruneToolResults(request?: MaintenanceRequest): Observable<MaintenanceResult> {
    return this.http.post<MaintenanceResult>('/api/admin/maintenance/prune-tool-results', request || {});
  }

  pruneBenchmarkToolResults(request?: MaintenanceRequest): Observable<MaintenanceResult> {
    return this.http.post<MaintenanceResult>('/api/admin/maintenance/prune-benchmark-tool-results', request || {});
  }

  pruneAuditLog(request?: MaintenanceRequest): Observable<MaintenanceResult> {
    return this.http.post<MaintenanceResult>('/api/admin/maintenance/prune-audit-log', request || {});
  }

  pruneAiErrorLog(request?: MaintenanceRequest): Observable<MaintenanceResult> {
    return this.http.post<MaintenanceResult>('/api/admin/maintenance/prune-ai-error-log', request || {});
  }

  pruneSecurityEvents(request?: MaintenanceRequest): Observable<MaintenanceResult> {
    return this.http.post<MaintenanceResult>('/api/admin/maintenance/prune-security-events', request || {});
  }

  pruneChatProviderAttempts(request?: MaintenanceRequest): Observable<MaintenanceResult> {
    return this.http.post<MaintenanceResult>('/api/admin/maintenance/prune-chat-provider-attempts', request || {});
  }

  sweepOrphans(request?: MaintenanceRequest): Observable<MaintenanceResult> {
    return this.http.post<MaintenanceResult>('/api/admin/maintenance/sweep-orphans', request || {});
  }

  /** Deletes every report chart image; the documents themselves stay. */
  clearReportCharts(request?: MaintenanceRequest): Observable<MaintenanceResult> {
    return this.http.post<MaintenanceResult>('/api/admin/maintenance/clear-report-charts', request || {});
  }

  /** Newest first; the server clamps page to at least 1 and pageSize to 1..1000. */
  getMaintenanceHistory(page: number, pageSize: number): Observable<MaintenanceHistoryPage> {
    return this.http.get<MaintenanceHistoryPage>('/api/admin/maintenance/history', { params: { page, pageSize } });
  }

  getMaintenanceRunLog(id: number): Observable<MaintenanceRunLogText> {
    return this.http.get<MaintenanceRunLogText>(`/api/admin/maintenance/history/${id}/log`);
  }

  sendReportEmail(): Observable<{ success: boolean; message: string }> {
    return this.http.post<{ success: boolean; message: string }>('/api/admin/maintenance/send-report-email', {});
  }

  // Telemetry & Rate-Limit Governor
  getGovernorStatus(): Observable<AiGovernorStatusDto> {
    return this.http.get<AiGovernorStatusDto>('/api/Admin/governor/status');
  }

  resetGovernorCooldown(credentialKey?: string): Observable<void> {
    return this.http.post<void>('/api/Admin/governor/reset-cooldown', { credentialKey });
  }

  getEndpointPolicy(): Observable<EndpointPolicySummaryDto> {
    return this.http.get<EndpointPolicySummaryDto>('/api/admin/endpoint-policy');
  }

  getAiTelemetrySummary(startDate?: string, endDate?: string, keys?: AiTelemetryKeySource): Observable<AiTelemetrySummaryDto> {
    let params: any = {};
    if (startDate) params.startDate = startDate;
    if (endDate) params.endDate = endDate;
    if (keys) params.keys = keys;
    return this.http.get<AiTelemetrySummaryDto>('/api/Admin/ai-telemetry/summary', { params });
  }

  /** 400 with `{ message }` when the range has more than 1,100 buckets. */
  getAiTelemetrySeries(query: AiTelemetrySeriesQuery): Observable<AiTelemetrySeriesDto> {
    const params: any = {};
    if (query.startDate) params.startDate = query.startDate;
    if (query.endDate) params.endDate = query.endDate;
    if (query.keys) params.keys = query.keys;
    if (query.roles?.length) params.roles = query.roles.join(',');
    if (query.bucket) params.bucket = query.bucket;
    if (query.splitBy) params.splitBy = query.splitBy;
    if (query.userId) params.userId = query.userId;
    return this.http.get<AiTelemetrySeriesDto>('/api/Admin/ai-telemetry/series', { params });
  }

  /** Per-user usage of system AI configurations, sorted and paged on the server. */
  getAiTelemetryUsers(query: AiTelemetryUsersQuery): Observable<AiTelemetryUsersPage> {
    const params: any = {};
    if (query.startDate) params.startDate = query.startDate;
    if (query.endDate) params.endDate = query.endDate;
    if (query.search) params.search = query.search;
    if (query.userId) params.userId = query.userId;
    if (query.sort) params.sort = query.sort;
    if (query.skip) params.skip = query.skip;
    if (query.take) params.take = query.take;
    return this.http.get<AiTelemetryUsersPage>('/api/Admin/ai-telemetry/users', { params });
  }

  /** Usage of one model by user, in one key source and the given role groups; 400 without a provider or model id. */
  getAiTelemetryModelUsers(query: AiTelemetryModelUsersQuery): Observable<AiTelemetryModelUsersDto> {
    const params: any = {};
    if (query.startDate) params.startDate = query.startDate;
    if (query.endDate) params.endDate = query.endDate;
    params.keys = query.keys;
    params.provider = query.provider;
    params.modelId = query.modelId;
    if (query.roles.length) params.roles = query.roles.join(',');
    return this.http.get<AiTelemetryModelUsersDto>('/api/Admin/ai-telemetry/model-users', { params });
  }
}
