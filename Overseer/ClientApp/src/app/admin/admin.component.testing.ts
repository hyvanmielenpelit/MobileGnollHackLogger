import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { AdminService, SystemAiConfigDto } from '../services/admin.service';
import { AdminPageStore } from './admin-page.store';

// Spec helper for AdminComponent and the Admin tab and dialog components. Imported by specs only.

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

/** The empty responses every Admin spec starts from: no users, groups, configurations or default keys. */
export function spyAdminService(adminService: AdminService): void {
  vi.spyOn(adminService, 'getUsers').mockReturnValue(of({ rows: [], totalCount: 0 }));
  vi.spyOn(adminService, 'getGroups').mockReturnValue(of([]));
  vi.spyOn(adminService, 'getSystemConfigs').mockReturnValue(of([]));
  vi.spyOn(adminService, 'getDefaultApiKeys').mockReturnValue(of([]));
}

/**
 * Configures the TestBed for one Admin tab or dialog component with a page store of its own, and
 * installs the default AdminService spies.
 */
export async function configureAdminTestBed(component: Type<unknown>): Promise<{ adminService: AdminService; store: AdminPageStore }> {
  await TestBed.configureTestingModule({
    imports: [component],
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      AdminPageStore
    ]
  }).compileComponents();

  const adminService = TestBed.inject(AdminService);
  spyAdminService(adminService);
  return { adminService, store: TestBed.inject(AdminPageStore) };
}
