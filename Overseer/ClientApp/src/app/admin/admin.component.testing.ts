import { Component, Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Routes, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { AdminService, SystemAiConfigDto } from '../services/admin.service';
import { AdminUsersService } from '../services/admin-users.service';
import { AdminFeedbackCounts, AdminFeedbackService } from '../services/admin-feedback.service';
import {
  AdminDevtoolsService, CORPUS_KIND_BY_SLUG, ConfigHealthReport, CorpusIndexInfo, CorpusIndexJob,
  CorpusIndexStatus, CorpusKind, CorpusKindSlug, CorpusProbeResult, CorpusStatusBySlug, CorpusToolUsage,
  CorpusToolUsageDays, CorpusToolUsageStats, SourceDashboard, SourceIndexStatus
} from '../services/admin-devtools.service';
import { AdminPageStore } from './admin-page.store';
import { adminUrlMatcher } from './admin-url';
import { spyAdminUsersService } from './users-tab/admin-users.testing';

// Spec helper for AdminComponent and the Admin tab and dialog components. Imported by specs only.

@Component({ template: '' })
class AdminRouteStubComponent {}

/**
 * The admin route with `component` on it. Tab specs get a stub, so a store navigation to an admin
 * path resolves instead of failing to match.
 */
export function adminTestRoutes(component: Type<unknown> = AdminRouteStubComponent): Routes {
  return [{ matcher: adminUrlMatcher, component }];
}

/** A source status with nothing configured, indexed or running. */
export function buildSourceStatus(overrides: Partial<SourceIndexStatus> = {}): SourceIndexStatus {
  return {
    configured: true,
    sourcePath: 'C:\\src\\GnollHack',
    pathExists: true,
    isIndexingComplete: true,
    currentHeadSha: null,
    currentBranch: null,
    index: null,
    upToDate: true,
    nextCheckAtUtc: null,
    checkIntervalMinutes: 10,
    generatedHeaders: [],
    makedefs: null,
    currentJob: null,
    recentJobs: [],
    ...overrides
  };
}

/** A finished manual pass that succeeded. */
export function buildCorpusJob(overrides: Partial<CorpusIndexJob> = {}): CorpusIndexJob {
  return {
    id: 'corpus-job-1',
    trigger: 'Manual',
    startedAtUtc: '2026-10-18T11:59:50Z',
    finishedAtUtc: '2026-10-18T11:59:52Z',
    phase: 'Publishing',
    filesRead: 120,
    filesTotal: 120,
    outcome: 'Succeeded',
    message: '120 articles',
    ...overrides
  };
}

/** A published index with no excluded or unreadable file. */
export function buildCorpusIndexInfo(overrides: Partial<CorpusIndexInfo> = {}): CorpusIndexInfo {
  return {
    generation: 1,
    headSha: '0123456789abcdef0123',
    branch: 'main',
    indexedAtUtc: '2026-10-18T11:50:00Z',
    durationMs: 2400,
    fileCount: 120,
    totalBytes: 524288,
    fingerprintShort: 'abcdef012345',
    maxFileSizeKB: 100,
    skippedBySize: [],
    skippedBySizeCount: 0,
    skippedDotDirectories: 0,
    failedFiles: [],
    failedFileCount: 0,
    ...overrides
  };
}

const CORPUS_DEFAULTS: Record<CorpusKindSlug, { configKey: string; path: string; git: boolean }> = {
  'gnollhack-wiki': { configKey: 'WikiPath', path: 'C:\\wiki', git: true },
  'nethack-wiki': { configKey: 'NetHackWikiPath', path: 'C:\\hmp\\nethackwiki', git: false },
  'knowledge-base': { configKey: 'KbPath', path: 'C:\\hmp\\kb', git: true }
};

/**
 * A configured, idle corpus status with nothing indexed or running, in the subclass shape of
 * `kind`. The NetHack wiki refreshes at startup and on request only; the other two poll Git.
 */
export function buildCorpusStatus<K extends CorpusKindSlug>(
  kind: K, overrides: Partial<CorpusStatusBySlug[K]> = {}
): CorpusStatusBySlug[K] {
  const defaults = CORPUS_DEFAULTS[kind];
  const gitPoll = kind !== 'nethack-wiki';
  const common: CorpusIndexStatus = {
    kind: CORPUS_KIND_BY_SLUG[kind],
    configKey: defaults.configKey,
    configured: true,
    path: defaults.path,
    pathExists: true,
    isGitRepository: defaults.git,
    isIndexingComplete: true,
    currentHeadSha: null,
    currentBranch: null,
    index: null,
    upToDate: true,
    refreshMode: gitPoll ? 'GitPoll' : 'StartupAndManual',
    nextCheckAtUtc: null,
    checkIntervalMinutes: gitPoll ? 10 : null,
    currentJob: null,
    recentJobs: [],
    serverTimeUtc: '2026-10-18T12:00:00Z'
  };
  const extra: Record<CorpusKindSlug, object> = {
    'gnollhack-wiki': { categories: [], duplicateTitles: [], duplicateTitleCount: 0 },
    'nethack-wiki': { namespaces: [], conversionReport: null, conversionReportError: null, treeChangedSinceIndex: false },
    'knowledge-base': { articles: [], shadowedTopics: [], topicListChars: 0, contentPathExists: true }
  };
  return { ...common, ...extra[kind], ...overrides } as CorpusStatusBySlug[K];
}

const USAGE_TOOLS: ReadonlyArray<{ tool: string; corpus: CorpusKind }> = [
  { tool: 'wiki_search', corpus: 'GnollHackWiki' },
  { tool: 'wiki_view', corpus: 'GnollHackWiki' },
  { tool: 'monster_lookup', corpus: 'GnollHackWiki' },
  { tool: 'item_lookup', corpus: 'GnollHackWiki' },
  { tool: 'nethack_wiki_search', corpus: 'NetHackWiki' },
  { tool: 'nethack_wiki_view', corpus: 'NetHackWiki' },
  { tool: 'get_knowledge_article', corpus: 'KnowledgeBase' }
];

/** Usage stats with no call, `days` zeros long. */
function emptyUsageStats(days: number): CorpusToolUsageStats {
  return {
    calls: 0, completed: 0, errors: 0, other: 0, guardRefusals: 0,
    p50ExecutionMs: null, p95ExecutionMs: null, daily: new Array<number>(days).fill(0)
  };
}

/**
 * The chat's corpus tool usage with no call over 30 days (or `overrides.days`): every tool and every
 * corpus present, each daily series of zeros as long as the period.
 */
export function buildCorpusToolUsage(overrides: Partial<CorpusToolUsage> = {}): CorpusToolUsage {
  const days = overrides.days ?? 30;
  const corpora: CorpusKind[] = ['GnollHackWiki', 'NetHackWiki', 'KnowledgeBase'];
  return {
    days,
    sinceUtc: '2026-09-19T00:00:00Z',
    generatedAtUtc: '2026-10-18T12:00:00Z',
    includesGnollBenchCalls: false,
    includesEphemeralSessions: false,
    includesSubAgentCalls: true,
    guardRefusalsIncludeEncryptedSessions: false,
    tools: USAGE_TOOLS.map(t => ({ ...emptyUsageStats(days), tool: t.tool, corpus: t.corpus })),
    corpora: corpora.map(corpus => ({
      ...emptyUsageStats(days),
      corpus,
      tools: USAGE_TOOLS.filter(t => t.corpus === corpus).map(t => t.tool)
    })),
    ...overrides
  };
}

/** The chat tool each corpus's search probe runs. */
const PROBE_TOOLS: Record<CorpusKindSlug, string> = {
  'gnollhack-wiki': 'wiki_search',
  'nethack-wiki': 'nethack_wiki_search',
  'knowledge-base': 'get_knowledge_article'
};

/** A probe whose tool succeeded with an empty answer. */
export function buildCorpusProbeResult(overrides: Partial<CorpusProbeResult> = {}): CorpusProbeResult {
  return {
    toolName: 'wiki_search',
    success: true,
    content: '',
    error: null,
    lengthChars: 0,
    maxResultLength: 10000,
    elapsedMs: 12,
    ...overrides
  };
}

/**
 * Empty Services tab dashboards: the source status, each corpus idle with nothing indexed, no corpus
 * tool usage, empty probe and article answers, and an empty health report.
 */
export function spyAdminDevtoolsService(service: AdminDevtoolsService): void {
  const dashboard: SourceDashboard = {
    gnollHack: buildSourceStatus(),
    netHack: buildSourceStatus({ sourcePath: 'C:\\src\\NetHack' }),
    serverTimeUtc: '2026-10-18T12:00:00Z'
  };
  const report: ConfigHealthReport = {
    generatedAtUtc: '2026-10-18T12:00:00Z',
    environmentName: 'Development',
    userSecretsExpected: true,
    sources: [],
    summary: { ok: 0, warning: 0, error: 0, info: 0 },
    checks: []
  };
  vi.spyOn(service, 'getSourceStatus').mockReturnValue(of(dashboard));
  vi.spyOn(service, 'startReindex').mockReturnValue(of({ jobId: 'job-1' }));
  vi.spyOn(service, 'startMakedefsBuild').mockReturnValue(of({ jobId: 'job-1' }));
  vi.spyOn(service, 'cancelSourceJob').mockReturnValue(of({ canceled: true }));
  vi.spyOn(service, 'getConfigHealth').mockReturnValue(of(report));
  vi.spyOn(service, 'getCorpusStatus').mockImplementation(
    ((kind: CorpusKindSlug) => of(buildCorpusStatus(kind))) as unknown as AdminDevtoolsService['getCorpusStatus']);
  vi.spyOn(service, 'startCorpusReindex').mockReturnValue(of({ jobId: 'corpus-job-1' }));
  vi.spyOn(service, 'probeCorpus').mockImplementation(
    (kind: CorpusKindSlug) => of(buildCorpusProbeResult({ toolName: PROBE_TOOLS[kind] })));
  vi.spyOn(service, 'getKnowledgeArticle').mockImplementation(
    (topic: string) => of({ topic, title: null, source: '' }));
  vi.spyOn(service, 'getCorpusToolUsage').mockImplementation(
    (days: CorpusToolUsageDays) => of(buildCorpusToolUsage({ days })));
}

/** A system configuration with every limit unset and every counter at zero. */
export function buildSystemConfig(overrides: Partial<SystemAiConfigDto> = {}): SystemAiConfigDto {
  return {
    id: 1,
    displayName: 'Config',
    provider: 'Anthropic',
    modelId: 'claude-x',
    thinkingLevel: null,
    reasoningMode: null,
    reasoningSummary: null,
    serviceTier: null,
    maxInputTokens: null,
    maxOutputTokens: null,
    orderIndex: 0,
    isEnabled: true,
    hasApiKey: true,
    isSystemWide: false,
    maxDailyChatRequests: null,
    maxMonthlyChatRequests: null,
    maxTotalChatRequests: null,
    dailyChatRequestsCount: 0,
    monthlyChatRequestsCount: 0,
    totalChatRequestsCount: 0,
    maxDailyTitleRequests: null,
    maxMonthlyTitleRequests: null,
    maxTotalTitleRequests: null,
    dailyTitleRequestsCount: 0,
    monthlyTitleRequestsCount: 0,
    totalTitleRequestsCount: 0,
    maxDailyChatTokens: null,
    maxMonthlyChatTokens: null,
    maxTotalChatTokens: null,
    dailyChatTokensCount: 0,
    monthlyChatTokensCount: 0,
    totalChatTokensCount: 0,
    maxDailyTitleTokens: null,
    maxMonthlyTitleTokens: null,
    maxTotalTitleTokens: null,
    dailyTitleTokensCount: 0,
    monthlyTitleTokensCount: 0,
    totalTitleTokensCount: 0,
    modelRole: 1,
    parallelExecutionMode: 2,
    ...overrides
  };
}

/**
 * The empty responses every Admin spec starts from: no groups, configurations, default keys or deletion
 * requests. The users come from `AdminUsersService`, which `spyAdminUsersService` empties.
 */
export function spyAdminService(adminService: AdminService): void {
  vi.spyOn(adminService, 'getGroups').mockReturnValue(of([]));
  vi.spyOn(adminService, 'getSystemConfigs').mockReturnValue(of([]));
  vi.spyOn(adminService, 'getDefaultApiKeys').mockReturnValue(of([]));
  vi.spyOn(adminService, 'getAccountDeletionRequests').mockReturnValue(of({
    totalCount: 0, pendingCount: 0, overdueCount: 0, erasurePathsConfigured: true, rows: []
  }));
}

/** Feedback issue counts of zero in every status, or `overrides`, for the Admin tab row and the Feedback tab. */
export function spyAdminFeedbackCounts(service: AdminFeedbackService, overrides: Partial<AdminFeedbackCounts> = {}) {
  return vi.spyOn(service, 'getCounts').mockReturnValue(of({
    new: 0, acknowledged: 0, resolved: 0, regressed: 0, dismissed: 0, ...overrides
  }));
}

/**
 * Configures the TestBed for one Admin tab or dialog component with a page store of its own, and
 * installs the default AdminService, AdminUsersService, AdminDevtoolsService and feedback count spies.
 */
export async function configureAdminTestBed(component: Type<unknown>): Promise<{
  adminService: AdminService;
  store: AdminPageStore;
  usersService: AdminUsersService;
  devtoolsService: AdminDevtoolsService;
}> {
  await TestBed.configureTestingModule({
    imports: [component],
    providers: [
      provideRouter(adminTestRoutes()),
      provideHttpClient(),
      provideHttpClientTesting(),
      AdminPageStore
    ]
  }).compileComponents();

  const adminService = TestBed.inject(AdminService);
  spyAdminService(adminService);
  const usersService = TestBed.inject(AdminUsersService);
  spyAdminUsersService(usersService);
  const devtoolsService = TestBed.inject(AdminDevtoolsService);
  spyAdminDevtoolsService(devtoolsService);
  spyAdminFeedbackCounts(TestBed.inject(AdminFeedbackService));
  return { adminService, store: TestBed.inject(AdminPageStore), usersService, devtoolsService };
}
