import { Component, Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Routes, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { AdminService, SystemAiConfigDto } from '../services/admin.service';
import { AdminUsersService } from '../services/admin-users.service';
import {
  AdminDevtoolsService, ConfigHealthReport, SourceDashboard, SourceIndexStatus
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

/** An empty Developer Tools dashboard and health report. */
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

/**
 * Configures the TestBed for one Admin tab or dialog component with a page store of its own, and
 * installs the default AdminService, AdminUsersService and AdminDevtoolsService spies.
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
  return { adminService, store: TestBed.inject(AdminPageStore), usersService, devtoolsService };
}
