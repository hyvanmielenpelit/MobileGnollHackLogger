import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface UserDto {
  id: string;
  userName: string;
  email: string;
  groups: GroupDto[];
}

export interface UsersResponse {
  rows: UserDto[];
  totalCount: number;
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
  thinkingLevel: string | null;
  reasoningMode: string | null;
  reasoningSummary: string | null;
  serviceTier: string | null;
  maxInputTokens: number | null;
  maxOutputTokens: number | null;
  orderIndex: number;
  isEnabled: boolean;
  hasApiKey: boolean;
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

export interface AiModelUsageBreakdownDto {
  provider: string;
  modelId: string;
  requests: number;
  inputTokens: number;
  outputTokens: number;
  cacheReadTokens: number;
  cacheCreationTokens: number;
  cacheHitRatio: number;
  avgDurationMs: number;
}

export interface AiTelemetrySummaryDto {
  totalRequests: number;
  totalChatRequests: number;
  totalTitleRequests: number;
  totalBenchmarkRequests: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalCacheReadTokens: number;
  totalCacheCreationTokens: number;
  cacheHitRatio: number;
  avgDurationMs: number;
  models: AiModelUsageBreakdownDto[];
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

  // Users
  getUsers(page: number = 1, pageSize: number = 10, usernameFilter: string = '', sortColumn: string = 'UserName', sortOrder: string = 'asc'): Observable<UsersResponse> {
    const params: any = { page, pageSize, sortColumn, sortOrder };
    if (usernameFilter) {
      params.usernameFilter = usernameFilter;
    }
    return this.http.get<UsersResponse>('/api/admin/users', { params });
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

  resetSystemConfig(id: number, counterName?: string): Observable<void> {
    return this.http.post<void>(`/api/admin/systemconfigs/${id}/reset`, { counterName });
  }

  reorderSystemConfigs(ids: number[]): Observable<void> {
    return this.http.put<void>('/api/admin/systemconfigs/reorder', { orderedIds: ids });
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

  sweepOrphans(request?: MaintenanceRequest): Observable<MaintenanceResult> {
    return this.http.post<MaintenanceResult>('/api/admin/maintenance/sweep-orphans', request || {});
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

  getAiTelemetrySummary(startDate?: string, endDate?: string): Observable<AiTelemetrySummaryDto> {
    let params: any = {};
    if (startDate) params.startDate = startDate;
    if (endDate) params.endDate = endDate;
    return this.http.get<AiTelemetrySummaryDto>('/api/Admin/ai-telemetry/summary', { params });
  }
}
