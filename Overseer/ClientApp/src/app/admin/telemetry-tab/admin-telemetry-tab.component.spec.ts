import type { Mock } from "vitest";
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, Subject, throwError } from 'rxjs';
import {
  AdminService,
  AiTelemetrySummaryDto,
  AiGovernorStatusDto,
  AiGovernorKeyStatusDto,
  AiTelemetryGroupRowDto,
  AiTelemetryGroupsDto,
  AiTelemetryUserRowDto,
  AiTelemetryUsersPage,
  AiUsageBreakdownRowDto,
  ChatProviderAvailabilityDto,
  OverseerActivityDto
} from '../../services/admin.service';
import {
  AdminFeedbackCounts,
  AdminFeedbackIssuePage,
  AdminFeedbackService
} from '../../services/admin-feedback.service';
import { AdminPageStore, AdminTelemetrySubTab } from '../admin-page.store';
import { spyAdminService } from '../admin.component.testing';
import { AdminTelemetryTabComponent } from './admin-telemetry-tab.component';
import { TelemetryChartsDialogComponent } from './charts/telemetry-charts-dialog.component';
import { TelemetryModelUsersDialogComponent } from './model-users/telemetry-model-users-dialog.component';
import { TelemetryUserModelsDialogComponent } from './user-models/telemetry-user-models-dialog.component';
import { TELEMETRY_BREAKDOWN_STORAGE_KEY } from './telemetry-breakdown';
import { TELEMETRY_USERS_VIEW_STORAGE_KEY } from './users/telemetry-users.component';

/** Stands in for the charts dialog, so the tab's spec does not depend on chart rendering. */
@Component({ selector: 'app-telemetry-charts-dialog', template: '' })
class ChartsDialogStub {
  open = vi.fn();
}

/** Stands in for the Usage by user dialog, so every test of the tab does not compile and run it. */
@Component({ selector: 'app-telemetry-model-users-dialog', template: '' })
class ModelUsersDialogStub {
  open = vi.fn();
}

/** Stands in for the Usage by model dialog. */
@Component({ selector: 'app-telemetry-user-models-dialog', template: '' })
class UserModelsDialogStub {
  open = vi.fn();
}

const availability = (overrides: Partial<ChatProviderAvailabilityDto> = {}): ChatProviderAvailabilityDto => ({
  recorded: false, recordedSinceUtc: null, validAttempts: 0, providerErrors: 0, unattributedErrors: 0, ourSideFailures: 0,
  providerErrorRate: null, low: null, high: null, inclusiveRate: null,
  overloaded: 0, serverErrors: 0, timeouts: 0, streamFailures: 0, rateLimitedUnattributed: 0, transportUnattributed: 0,
  calls: 0, firstAttemptSuccessCalls: 0, failedCalls: 0, chatTurns: 0, chatTurnsEndedByProviderError: 0,
  titleValidAttempts: 0, titleProviderErrors: 0, titleRequests: 0, titleRequestsFailed: 0,
  ...overrides
});

const activity = (overrides: Partial<OverseerActivityDto> = {}): OverseerActivityDto => ({
  activeUsers: 0, newUsers: 0, chatsCreated: 0, confidentialChatsCreated: 0, gnollHackChatsCreated: 0,
  userMessages: 0, assistantMessages: 0, incognitoChatsCreated: 0, incognitoUserMessages: 0, countedSinceUtc: null,
  ...overrides
});

const summary = (overrides: Partial<AiTelemetrySummaryDto> = {}): AiTelemetrySummaryDto => ({
  keySource: 'system',
  totalRequests: 10, totalChatRequests: 6, totalTitleRequests: 2, totalBenchmarkRequests: 2,
  totalPromptTokens: 0, totalUncachedInputTokens: 0, totalOutputTokens: 0, totalCacheReadTokens: 0, totalCacheCreationTokens: 0,
  cacheHitRatio: 0, avgDurationMs: 0, totalEstimatedCost: 0, pricedRequests: 0, costRecordedSinceUtc: null,
  rows: [], chatAvailability: availability(), activity: activity(),
  ...overrides
});

/** A system-key Chat row of one request with every figure at zero. */
const row = (overrides: Partial<AiUsageBreakdownRowDto> = {}): AiUsageBreakdownRowDto => ({
  keySource: 'system', userId: null, userName: null,
  provider: 'Anthropic', modelId: 'claude-x', modelName: 'Claude X',
  roleContext: 1, roleGroup: 'chat', thinkingLevel: 'high',
  requests: 1, promptTokens: 0, uncachedInputTokens: 0, cacheReadTokens: 0, cacheCreationTokens: 0, outputTokens: 0,
  durationSumMs: 0, durationCount: 0, estimatedCost: 0, pricedRequests: 0,
  validAttempts: 0, providerErrors: 0, unattributedErrors: 0, ourSideFailures: 0,
  ...overrides
});

const userRow = (overrides: Partial<AiTelemetryUserRowDto> = {}): AiTelemetryUserRowDto => ({
  userId: 'u-1', userName: 'alice', requests: 0, systemRequests: 0, userKeyRequests: 0, systemEstimatedCost: 0, userKeyEstimatedCost: 0,
  chatRequests: 0, titleRequests: 0, benchmarkRequests: 0,
  promptTokens: 0, outputTokens: 0, estimatedCost: 0, pricedRequests: 0, peakHourRequests: 0, peakHourStartUtc: null,
  requestsPerActiveHour: 0, activeDays: 0, lastRequestUtc: null, chatsCreated: 0, userMessages: 0,
  limitRefusals: 0, providerRefusals: 0, isOverseerBlocked: false, hasAccountHold: false, configs: [],
  ...overrides
});

const usersPage = (users: AiTelemetryUserRowDto[] = []): AiTelemetryUsersPage => ({
  users, totalUsers: users.length, securityEventsAvailable: true, keySource: 'system'
});

const groupRow = (overrides: Partial<AiTelemetryGroupRowDto> = {}): AiTelemetryGroupRowDto => ({
  groupId: 1, groupName: 'Group', memberCount: 1, activeMembers: 0, blockedMembers: 0,
  requests: 0, systemRequests: 0, userKeyRequests: 0, systemEstimatedCost: 0, userKeyEstimatedCost: 0,
  chatRequests: 0, titleRequests: 0, benchmarkRequests: 0, promptTokens: 0, outputTokens: 0,
  estimatedCost: 0, pricedRequests: 0, peakHourRequests: 0, peakHourStartUtc: null, requestsPerActiveHour: 0,
  activeDays: 0, lastRequestUtc: null, chatsCreated: 0, userMessages: 0, limitRefusals: 0, providerRefusals: 0, configs: [],
  ...overrides
});

const groupsDto = (groups: AiTelemetryGroupRowDto[] = []): AiTelemetryGroupsDto => ({
  keySource: 'system', groups, securityEventsAvailable: true
});

const feedbackCounts = (overrides: Partial<AdminFeedbackCounts> = {}): AdminFeedbackCounts => ({
  new: 0, acknowledged: 0, resolved: 0, regressed: 0, dismissed: 0,
  ...overrides
});

const emptyIssuePage = (): AdminFeedbackIssuePage => ({ totalCount: 0, rows: [] });

describe('AdminTelemetryTabComponent', () => {
  let component: AdminTelemetryTabComponent;
  let fixture: ComponentFixture<AdminTelemetryTabComponent>;
  let adminService: AdminService;
  let store: AdminPageStore;
  let usersSpy: Mock;
  let groupsSpy: Mock;
  let feedbackService: AdminFeedbackService;
  let countsSpy: Mock;
  let issuesSpy: Mock;

  beforeEach(async () => {
    localStorage.removeItem(TELEMETRY_BREAKDOWN_STORAGE_KEY);
    localStorage.removeItem(TELEMETRY_USERS_VIEW_STORAGE_KEY);

    await TestBed.configureTestingModule({
      imports: [AdminTelemetryTabComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), AdminPageStore]
    })
      .overrideComponent(AdminTelemetryTabComponent, {
        remove: { imports: [TelemetryChartsDialogComponent, TelemetryModelUsersDialogComponent, TelemetryUserModelsDialogComponent] },
        add: { imports: [ChartsDialogStub, ModelUsersDialogStub, UserModelsDialogStub] }
      })
      .compileComponents();

    adminService = TestBed.inject(AdminService);
    spyAdminService(adminService);
    usersSpy = vi.spyOn(adminService, 'getAiTelemetryUsers').mockReturnValue(of(usersPage()));
    groupsSpy = vi.spyOn(adminService, 'getAiTelemetryGroups').mockReturnValue(of(groupsDto()));
    feedbackService = TestBed.inject(AdminFeedbackService);
    countsSpy = vi.spyOn(feedbackService, 'getCounts').mockReturnValue(of(feedbackCounts()));
    issuesSpy = vi.spyOn(feedbackService, 'getIssues').mockReturnValue(of(emptyIssuePage()));
    vi.spyOn(feedbackService, 'getSwitch').mockReturnValue(of({ enabled: true, setAtUtc: null, setByUserName: null, seeded: true }));
    vi.spyOn(feedbackService, 'getModels').mockReturnValue(of([]));
    store = TestBed.inject(AdminPageStore);

    fixture = TestBed.createComponent(AdminTelemetryTabComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    localStorage.removeItem(TELEMETRY_BREAKDOWN_STORAGE_KEY);
    localStorage.removeItem(TELEMETRY_USERS_VIEW_STORAGE_KEY);
  });

  describe('AI telemetry tab', () => {
    const governor = (keys: AiGovernorKeyStatusDto[] = []): AiGovernorStatusDto => ({
      maxConcurrentCalls: 4, maxRetryAfterSeconds: 90, activeKeys: keys
    });

    const key = (credentialKey: string, remainingSeconds: number): AiGovernorKeyStatusDto => ({
      credentialKey, isRateLimited: remainingSeconds > 0, remainingCooldownSeconds: remainingSeconds, inFlightCalls: 0
    });

    let summarySpy: Mock;
    let governorSpy: Mock;
    let toastSpy: Mock;

    beforeEach(() => {
      summarySpy = vi.spyOn(adminService, 'getAiTelemetrySummary').mockReturnValue(of(summary()));
      governorSpy = vi.spyOn(adminService, 'getGovernorStatus').mockReturnValue(of(governor()));
      toastSpy = vi.spyOn(store, 'showToast').mockReturnValue(undefined);
    });

    const host = () => fixture.nativeElement as HTMLElement;

    /** Renders the tab on a sub-tab, Summary by default. */
    const openTab = (subTab: AdminTelemetrySubTab = 'summary') => {
      store.telemetrySubTab = subTab;
      fixture.detectChanges();
    };

    const clearAllButton = (): HTMLButtonElement =>
      (Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[])
        .find(b => (b.textContent ?? '').includes('Clear All Cooldowns'))!;

    const normalize = (text: string | null | undefined) => (text ?? '').replace(/\s+/g, ' ').trim();

    const chartsDialog = (): ChartsDialogStub =>
      fixture.debugElement.query(By.directive(ChartsDialogStub)).componentInstance;

    const modelUsersDialog = (): ModelUsersDialogStub =>
      fixture.debugElement.query(By.directive(ModelUsersDialogStub)).componentInstance;

    const userModelsDialog = (): UserModelsDialogStub =>
      fixture.debugElement.query(By.directive(UserModelsDialogStub)).componentInstance;

    // --- Governor ---

    it('makes no request from Clear All Cooldowns when nothing is rate-limited', () => {
      const resetSpy = vi.spyOn(adminService, 'resetGovernorCooldown').mockReturnValue(of(undefined));
      component.governorStatus = governor([key('openai:user:u1', 0)]);

      component.resetGovernorCooldown();

      expect(resetSpy).not.toHaveBeenCalled();
      expect(toastSpy).not.toHaveBeenCalled();
    });

    it('clears all cooldowns and says so when a partition is rate-limited', () => {
      const resetSpy = vi.spyOn(adminService, 'resetGovernorCooldown').mockReturnValue(of(undefined));
      component.governorStatus = governor([key('openai:user:u1', 30)]);

      component.resetGovernorCooldown();

      expect(resetSpy).toHaveBeenCalledTimes(1);

      expect(resetSpy).toHaveBeenCalledWith(undefined);
      expect(vi.mocked(toastSpy).mock.lastCall![0]).toBe('All rate-limit cooldowns cleared.');
    });

    it('clears cooldowns from the Summary panel\'s buttons: all of them, or one partition', () => {
      const resetSpy = vi.spyOn(adminService, 'resetGovernorCooldown').mockReturnValue(of(undefined));
      governorSpy.mockReturnValue(of(governor([key('openai:user:u1', 30)])));
      openTab();

      clearAllButton().click();
      expect(resetSpy).toHaveBeenLastCalledWith(undefined);

      (host().querySelector('#tl-subpanel-summary .telemetry-partitions button.action-btn') as HTMLButtonElement).click();
      expect(resetSpy).toHaveBeenLastCalledWith('openai:user:u1');
      expect(vi.mocked(toastSpy).mock.lastCall![0]).toBe('Cooldown cleared for openai:user:u1.');
    });

    it('raises exactly one toast for a successful refresh', () => {
      component.refreshTelemetryTab(true);

      expect(toastSpy).toHaveBeenCalledTimes(1);
      expect(vi.mocked(toastSpy).mock.lastCall![1]).toBe('info');
      expect(component.telemetryLoading).toBe(false);
      expect(component.governorLoading).toBe(false);
    });

    it('raises one error toast when the governor call fails, and still shows the summary', () => {
      vi.spyOn(console, 'error').mockReturnValue(undefined);
      governorSpy.mockReturnValue(throwError(() => ({ message: 'boom' })));

      component.refreshTelemetryTab(true);

      expect(toastSpy).toHaveBeenCalledTimes(1);
      expect(vi.mocked(toastSpy).mock.lastCall![0]).toContain('governor status: boom');
      expect(vi.mocked(toastSpy).mock.lastCall![1]).toBe('error');
      expect(component.telemetrySummary).not.toBeNull();
      expect(component.governorLoading).toBe(false);
    });

    it('counts a cooldown down locally and re-fetches once when it ends', () => {
      vi.useFakeTimers();
      try {
        governorSpy.mockReturnValueOnce(of(governor([key('openai:user:u1', 2)]))).mockReturnValueOnce(of(governor()));
        component.refreshTelemetryTab();
        expect(governorSpy).toHaveBeenCalledTimes(1);

        vi.advanceTimersByTime(1000);
        expect(component.governorStatus!.activeKeys[0].remainingCooldownSeconds).toBe(1);
        expect(governorSpy).toHaveBeenCalledTimes(1);

        vi.advanceTimersByTime(1000);
        expect(governorSpy).toHaveBeenCalledTimes(2);
        expect(component.rateLimitedKeyCount).toBe(0);

        vi.advanceTimersByTime(5000);
        expect(governorSpy).toHaveBeenCalledTimes(2);
      }
      finally {
        vi.useRealTimers();
      }
    });

    it('shows the local countdown in the Summary panel\'s partitions table', () => {
      vi.useFakeTimers();
      try {
        governorSpy.mockReturnValue(of(governor([key('openai:user:u1', 5)])));
        openTab();
        const cooldown = () => normalize(host().querySelector('#tl-subpanel-summary .telemetry-cooldown')?.textContent);
        expect(cooldown()).toBe('5.0s');

        vi.advanceTimersByTime(1000);
        fixture.detectChanges();

        expect(cooldown()).toBe('4.0s');
      }
      finally {
        vi.useRealTimers();
      }
    });

    it('stops the countdown when another tab is selected', () => {
      vi.useFakeTimers();
      try {
        governorSpy.mockReturnValue(of(governor([key('openai:user:u1', 5)])));
        openTab();
        vi.advanceTimersByTime(1000);
        expect(component.governorStatus!.activeKeys[0].remainingCooldownSeconds).toBe(4);

        // Selecting another Admin tab destroys this one.
        fixture.destroy();
        vi.advanceTimersByTime(3000);

        expect(component.governorStatus!.activeKeys[0].remainingCooldownSeconds).toBe(4);
      }
      finally {
        vi.useRealTimers();
      }
    });

    // --- Keys filter ---

    it('asks for system configurations by default and reloads with keys=user when the Keys select changes', () => {
      openTab();
      expect(summarySpy).toHaveBeenLastCalledWith(component.telemetryStartDate, component.telemetryEndDate, 'system');

      const select = host().querySelector('#telemetryKeysSelect') as HTMLSelectElement;
      expect(host().querySelector('label[for="telemetryKeysSelect"]')?.textContent?.trim()).toBe('Keys');
      expect(Array.from(select.options).map(o => o.textContent?.trim())).toEqual(['System AI configurations', 'User keys', 'All keys']);

      select.value = 'user';
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();

      expect(summarySpy).toHaveBeenLastCalledWith(component.telemetryStartDate, component.telemetryEndDate, 'user');
      expect(JSON.parse(localStorage.getItem(TELEMETRY_BREAKDOWN_STORAGE_KEY)!).keys).toBe('user');
    });

    it('stores the settings the Models panel changes in the one view record, beside the Keys filter', () => {
      summarySpy.mockReturnValue(of(summary({
        rows: [row({ requests: 5 }), row({ modelId: 'b', modelName: 'B', roleGroup: 'title', roleContext: 2, requests: 3 })]
      })));
      openTab('models');

      const select = host().querySelector('#telemetryKeysSelect') as HTMLSelectElement;
      select.value = 'both';
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();

      (host().querySelector('#tl-role-title') as HTMLInputElement).click();
      fixture.detectChanges();

      expect(component.view.roles).toEqual(['chat', 'benchmark']);
      expect(component.view.keys).toBe('both');
      const stored = JSON.parse(localStorage.getItem(TELEMETRY_BREAKDOWN_STORAGE_KEY)!);
      expect(stored.roles).toEqual(['chat', 'benchmark']);
      expect(stored.keys).toBe('both');
      expect(host().querySelectorAll('#tl-subpanel-models .tl-cards > li > article.tl-card:not(.tl-card-total)').length).toBe(1);
    });

    // --- Sticky head, the header's filters and actions, and the sub-tabs ---

    it('keeps the header, with Date range, Keys, Charts and Refresh, and the sub-tab row in one sticky head', () => {
      openTab();

      const head = host().querySelector('.tl-head') as HTMLElement;
      expect(head.classList.contains('gh-subtab-head')).toBe(true);
      const header = head.querySelector('.tab-header-row') as HTMLElement;
      for (const id of ['telemetryTimeSpanSelect', 'telemetryKeysSelect', 'tl-charts-btn', 'tl-refresh-btn']) {
        expect(header.querySelector('#' + id)).not.toBeNull();
      }
      expect(head.querySelector(':scope > [role="tablist"]')).not.toBeNull();
      const style = getComputedStyle(head);
      expect(style.position).toBe('sticky');
      expect(style.top).toBe('0px');
    });

    it('wraps the sub-tab row rather than scrolling it', () => {
      openTab();

      expect(host().querySelector('[role="tablist"]')?.classList.contains('gh-tabs-wrap')).toBe(true);
    });

    const subTabButtons = (): HTMLButtonElement[] =>
      Array.from(host().querySelectorAll<HTMLButtonElement>('.tl-subtabs [role="tab"]'));

    it('is a named tablist of Summary, Models, Users, Groups and Feedback, each tab paired with its panel', () => {
      openTab();

      const tablist = host().querySelector('.tl-subtabs') as HTMLElement;
      expect(tablist.getAttribute('role')).toBe('tablist');
      expect(tablist.getAttribute('aria-label')).toBe('AI Telemetry sections');
      expect(subTabButtons().map(b => b.textContent?.trim())).toEqual(['Summary', 'Models', 'Users', 'Groups', 'Feedback']);
      expect(subTabButtons().map(b => b.getAttribute('aria-selected'))).toEqual(['true', 'false', 'false', 'false', 'false']);
      expect(subTabButtons().map(b => b.getAttribute('tabindex'))).toEqual(['0', '-1', '-1', '-1', '-1']);
      for (const tab of subTabButtons()) {
        expect(tab.getAttribute('type')).toBe('button');
        const panel = host().querySelector('#' + tab.getAttribute('aria-controls')) as HTMLElement;
        expect(panel.getAttribute('role')).toBe('tabpanel');
        expect(panel.getAttribute('aria-labelledby')).toBe(tab.id);
        expect(panel.getAttribute('tabindex')).toBe('0');
      }
      expect((host().querySelector('#tl-subpanel-summary') as HTMLElement).hidden).toBe(false);
      expect((host().querySelector('#tl-subpanel-models') as HTMLElement).hidden).toBe(true);
    });

    it('moves the address with a history entry on click, and replaces it with the arrow keys and moves focus', () => {
      openTab();
      const goTo = vi.spyOn(store, 'goTo');

      subTabButtons()[0].click();
      expect(goTo).not.toHaveBeenCalled();

      subTabButtons()[1].click();
      fixture.detectChanges();
      expect(goTo).toHaveBeenLastCalledWith('telemetry', 'models', 'push');
      expect(store.telemetrySubTab).toBe('models');
      expect(subTabButtons()[1].getAttribute('aria-selected')).toBe('true');

      subTabButtons()[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true }));
      fixture.detectChanges();
      expect(goTo).toHaveBeenLastCalledWith('telemetry', 'users', 'replace');
      expect(document.activeElement).toBe(host().querySelector('#tl-subtab-users'));
      expect(subTabButtons().map(b => b.getAttribute('tabindex'))).toEqual(['-1', '-1', '0', '-1', '-1']);
    });

    it('puts Date range and Keys in the header, named by visually hidden labels', () => {
      openTab();

      const header = host().querySelector('.tab-header-row') as HTMLElement;
      const tablist = host().querySelector('[role="tablist"]') as HTMLElement;
      const group = header.querySelector('.tl-range') as HTMLElement;
      expect(group.getAttribute('role')).toBe('group');
      expect(group.getAttribute('aria-label')).toBe('Date range and keys');
      expect(tablist.contains(group)).toBe(false);
      expect(Array.from(tablist.children).every(c => c.getAttribute('role') === 'tab')).toBe(true);
      for (const [id, name] of [['telemetryTimeSpanSelect', 'Date range'], ['telemetryKeysSelect', 'Keys']]) {
        const label = group.querySelector(`label[for="${id}"]`) as HTMLElement;
        expect(label.textContent?.trim()).toBe(name);
        expect(label.classList.contains('visually-hidden')).toBe(true);
      }
      expect(host().querySelector('#tl-toolbar-toggle')).toBeNull();
    });

    it('names the custom range dates by visually hidden labels, with a decorative dash between them', () => {
      store.telemetryRange.timeSpan = 'custom';
      store.telemetryRange.start = '2026-09-01';
      store.telemetryRange.end = '2026-09-30';
      openTab();

      const group = host().querySelector('.tab-header-row .tl-range') as HTMLElement;
      for (const [id, name] of [['telemetryStartDateInput', 'Start date'], ['telemetryEndDateInput', 'End date']]) {
        expect(group.querySelector('#' + id)).not.toBeNull();
        const label = group.querySelector(`label[for="${id}"]`) as HTMLElement;
        expect(label.textContent?.trim()).toBe(name);
        expect(label.classList.contains('visually-hidden')).toBe(true);
      }
      expect(group.querySelector('.tl-range-dash')?.getAttribute('aria-hidden')).toBe('true');
    });

    it('renders a panel on first selection and keeps it, hidden, after leaving it', () => {
      usersSpy.mockReturnValue(of(usersPage([userRow()])));
      openTab();
      expect(host().querySelector('#tl-subpanel-users app-telemetry-users')).toBeNull();
      expect(host().querySelector('#tl-subpanel-models .tl-breakdown-status')).toBeNull();
      expect(usersSpy).not.toHaveBeenCalled();

      subTabButtons()[2].click();
      fixture.detectChanges();
      const view = host().querySelector('#tl-subpanel-users app-telemetry-users');
      expect(view).not.toBeNull();
      expect(usersSpy).toHaveBeenCalledTimes(1);

      subTabButtons()[0].click();
      fixture.detectChanges();
      const panel = host().querySelector('#tl-subpanel-users') as HTMLElement;
      expect(panel.hidden).toBe(true);
      expect(panel.querySelector('app-telemetry-users')).toBe(view);

      subTabButtons()[2].click();
      fixture.detectChanges();
      expect(usersSpy).toHaveBeenCalledTimes(1);
    });

    it('shows no heading that repeats a sub-tab name', () => {
      summarySpy.mockReturnValue(of(summary({ rows: [row()] })));
      openTab('models');
      subTabButtons()[2].click();
      fixture.detectChanges();

      const headings = Array.from(host().querySelectorAll('h1, h2, h3, h4, h5, h6')).map(h => normalize(h.textContent));
      expect(headings).not.toContain('Usage by Model and Role');
      expect(headings).not.toContain('System AI usage by user');
      expect(host().querySelector('#tl-breakdown-heading, #tl-users-heading')).toBeNull();
    });

    // --- Charts ---

    it('opens the charts dialog with the range, the Keys filter and the selected roles', () => {
      openTab();

      const button = host().querySelector('.tab-header-row #tl-charts-btn') as HTMLButtonElement;
      expect(button.matches('.action-btn[type="button"]')).toBe(true);
      expect(button.getAttribute('aria-label')).toBe('Telemetry charts');
      expect(button.getAttribute('aria-haspopup')).toBe('dialog');
      expect(button.textContent?.trim()).toBe('');
      expect(button.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
      const tip = host().querySelector('#' + button.getAttribute('interestfor'));
      expect(tip?.getAttribute('popover')).toBe('hint');
      expect(tip?.textContent?.trim()).toBe('Charts');
      button.click();

      expect(chartsDialog().open).toHaveBeenCalledWith({
        start: component.telemetryStartDate,
        end: component.telemetryEndDate,
        keys: 'system',
        roles: ['chat', 'title', 'benchmark']
      });
    });

    // --- Refresh ---

    it('makes Refresh an icon button that is aria-disabled while loading and refuses the click', () => {
      const pending = new Subject<AiTelemetrySummaryDto>();
      summarySpy.mockReturnValueOnce(pending.asObservable());
      openTab();
      const refresh = vi.spyOn(component, 'refreshTelemetryTab');

      const button = () => host().querySelector('.tab-header-row #tl-refresh-btn') as HTMLButtonElement;
      expect(button().matches('.action-btn[type="button"]')).toBe(true);
      expect(button().getAttribute('aria-label')).toBe('Refresh telemetry');
      expect(button().textContent?.trim()).toBe('');
      const tip = host().querySelector('#' + button().getAttribute('interestfor'));
      expect(tip?.getAttribute('popover')).toBe('hint');
      expect(tip?.textContent?.trim()).toBe('Refresh telemetry');

      expect(button().getAttribute('aria-disabled')).toBe('true');
      expect(button().disabled).toBe(false);
      expect(button().querySelector('.gh-spinner-small')).not.toBeNull();
      const calls = summarySpy.mock.calls.length;
      button().click();
      fixture.detectChanges();
      expect(summarySpy.mock.calls.length).toBe(calls);
      expect(refresh).not.toHaveBeenCalled();

      pending.next(summary());
      pending.complete();
      fixture.detectChanges();
      expect(button().hasAttribute('aria-disabled')).toBe(false);
      expect(button().querySelector('.gh-spinner-small')).toBeNull();
      expect(button().querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');

      button().click();
      expect(refresh).toHaveBeenCalledWith(true);
    });

    // --- Usage by user ---

    const usersButtons = (): HTMLButtonElement[] =>
      Array.from(host().querySelectorAll<HTMLButtonElement>('.tl-cards button.tl-card-users'));

    it('opens the Usage by user dialog with the card\'s scope and the tab\'s dates, the button as the opener', () => {
      summarySpy.mockReturnValue(of(summary({
        rows: [row({ provider: 'OpenAI', modelId: 'gpt-x', modelName: 'GPT X', roleGroup: 'title', roleContext: 2, thinkingLevel: 'low' })]
      })));
      openTab('models');

      const button = usersButtons()[0];
      button.click();

      expect(modelUsersDialog().open).toHaveBeenCalledWith({
        start: component.telemetryStartDate,
        end: component.telemetryEndDate,
        keySource: 'system',
        provider: 'OpenAI',
        modelId: 'gpt-x',
        modelTitle: 'GPT X',
        roleGroup: 'title',
        thinkingLevel: undefined
      }, button);

      const thinking = host().querySelector('#tl-thinking-select') as HTMLSelectElement;
      thinking.value = 'separate';
      thinking.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      usersButtons()[0].click();

      expect(modelUsersDialog().open).toHaveBeenLastCalledWith(expect.objectContaining({ thinkingLevel: 'low' }), usersButtons()[0]);
    });

    // --- Users view ---

    it('places the users view in the Users panel with the range and keys, and opens a user\'s charts from it', () => {
      usersSpy.mockReturnValue(of(usersPage([userRow({ userId: 'u-3', userName: 'carol', requests: 4 })])));
      openTab('users');

      const panel = host().querySelector('#tl-subpanel-users') as HTMLElement;
      expect(panel.querySelector('app-telemetry-users')).not.toBeNull();
      expect(usersSpy).toHaveBeenCalledWith(expect.objectContaining({
        startDate: component.telemetryStartDate, endDate: component.telemetryEndDate, keys: 'system', sort: 'cost', skip: 0
      }));

      (panel.querySelector('button[aria-label="Usage charts of carol"]') as HTMLButtonElement).click();

      expect(chartsDialog().open).toHaveBeenCalledWith(expect.objectContaining({
        start: component.telemetryStartDate, end: component.telemetryEndDate, keys: 'system', userId: 'u-3', userName: 'carol'
      }));
    });

    it('refetches the users view with the Keys filter when it changes', () => {
      openTab('users');
      usersSpy.mockClear();

      const select = host().querySelector('#telemetryKeysSelect') as HTMLSelectElement;
      select.value = 'both';
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();

      expect(usersSpy).toHaveBeenCalledWith(expect.objectContaining({ keys: 'both', skip: 0 }));
    });

    it('opens the Usage by model dialog with the tab\'s range and keys, the button as the opener', () => {
      usersSpy.mockReturnValue(of(usersPage([userRow({ userId: 'u-3', userName: 'carol', requests: 4 })])));
      openTab('users');

      const button = host().querySelector('#tl-subpanel-users button[aria-label="Usage by model: carol"]') as HTMLButtonElement;
      button.click();

      expect(userModelsDialog().open).toHaveBeenCalledWith({
        start: component.telemetryStartDate,
        end: component.telemetryEndDate,
        keys: 'system',
        subject: { kind: 'user', userId: 'u-3', userName: 'carol' }
      }, button);
    });

    // --- Groups view ---

    it('mounts the Groups panel on first selection, at /admin/telemetry/groups, with the range and keys', () => {
      openTab();
      const goTo = vi.spyOn(store, 'goTo');
      expect(host().querySelector('#tl-subpanel-groups app-telemetry-groups')).toBeNull();
      expect(groupsSpy).not.toHaveBeenCalled();

      subTabButtons()[3].click();
      fixture.detectChanges();

      expect(goTo).toHaveBeenLastCalledWith('telemetry', 'groups', 'push');
      expect(subTabButtons()[3].getAttribute('aria-selected')).toBe('true');
      expect((host().querySelector('#tl-subpanel-groups') as HTMLElement).hidden).toBe(false);
      expect(host().querySelector('#tl-subpanel-groups app-telemetry-groups')).not.toBeNull();
      expect(groupsSpy).toHaveBeenCalledTimes(1);
      expect(groupsSpy).toHaveBeenCalledWith(expect.objectContaining({ keys: 'system' }));
    });

    it('opens a group\'s charts and its Usage by model from the Groups panel, and No group\'s too', () => {
      groupsSpy.mockReturnValue(of(groupsDto([
        groupRow({ groupId: 7, groupName: 'Testers', requests: 4, estimatedCost: 2, pricedRequests: 4 }),
        groupRow({ groupId: null, groupName: 'No group', requests: 1 })
      ])));
      openTab('groups');

      const panel = host().querySelector('#tl-subpanel-groups') as HTMLElement;
      (panel.querySelector('button[aria-label="Usage charts of Testers"]') as HTMLButtonElement).click();
      expect(chartsDialog().open).toHaveBeenLastCalledWith({
        start: component.telemetryStartDate, end: component.telemetryEndDate, keys: 'system',
        roles: ['chat', 'title', 'benchmark'], groupId: 7, groupName: 'Testers'
      });

      (panel.querySelector('button[aria-label="Usage charts of No group"]') as HTMLButtonElement).click();
      expect(chartsDialog().open).toHaveBeenLastCalledWith(expect.objectContaining({ groupId: 'none', groupName: 'No group' }));

      const byModel = panel.querySelector('button[aria-label="Usage by model: Testers"]') as HTMLButtonElement;
      byModel.click();
      expect(userModelsDialog().open).toHaveBeenLastCalledWith({
        start: component.telemetryStartDate,
        end: component.telemetryEndDate,
        keys: 'system',
        subject: { kind: 'group', groupId: 7, groupName: 'Testers' }
      }, byModel);
    });

    it('reloads the Groups view on Refresh once it is mounted', () => {
      openTab('groups');
      groupsSpy.mockClear();

      component.refreshFromHeader();

      expect(groupsSpy).toHaveBeenCalledTimes(1);
    });

    it('selects and mounts the Users panel for a Usage request made on Summary, and its first request carries the user', () => {
      usersSpy.mockReturnValue(of(usersPage([userRow({ userId: 'u-5', userName: 'eve' })])));
      openTab();
      expect(usersSpy).not.toHaveBeenCalled();

      store.requestTelemetryUser('u-5', 'eve');
      fixture.detectChanges();

      expect(store.telemetrySubTab).toBe('users');
      expect(subTabButtons()[2].getAttribute('aria-selected')).toBe('true');
      expect(store.pendingTelemetryUserId).toBeNull();
      expect(store.pendingTelemetryUserName).toBeNull();
      expect(usersSpy).toHaveBeenCalledTimes(1);
      expect(usersSpy).toHaveBeenCalledWith(expect.objectContaining({ userId: 'u-5' }));
      expect(host().querySelector('.tl-users-status')?.textContent?.trim()).toBe('Showing 1 user — eve');
    });

    // --- Feedback view ---

    it('mounts the Feedback panel on first selection, at /admin/telemetry/feedback, with the range and without Keys', () => {
      openTab();
      const goTo = vi.spyOn(store, 'goTo');
      expect(host().querySelector('#tl-subpanel-feedback app-telemetry-feedback')).toBeNull();
      expect(issuesSpy).not.toHaveBeenCalled();

      subTabButtons()[4].click();
      fixture.detectChanges();

      expect(goTo).toHaveBeenLastCalledWith('telemetry', 'feedback', 'push');
      expect(subTabButtons()[4].getAttribute('aria-selected')).toBe('true');
      const panel = host().querySelector('#tl-subpanel-feedback') as HTMLElement;
      expect(panel.hidden).toBe(false);
      expect(panel.getAttribute('aria-labelledby')).toBe('tl-subtab-feedback');
      expect(panel.querySelector('app-telemetry-feedback')).not.toBeNull();
      expect((component.feedbackView as unknown as Record<string, unknown>)['keys']).toBeUndefined();
      expect(issuesSpy).toHaveBeenCalled();
      const query = issuesSpy.mock.calls[0][0];
      expect(query).toEqual(expect.objectContaining({
        startDate: component.telemetryStartDate, endDate: component.telemetryEndDate, statuses: ['new', 'regressed'], skip: 0
      }));
      expect(Object.keys(query)).not.toContain('keys');
    });

    it('refetches the Feedback view for a new date range, and not for a new Keys filter', () => {
      openTab('feedback');
      issuesSpy.mockClear();

      const keys = host().querySelector('#telemetryKeysSelect') as HTMLSelectElement;
      keys.value = 'both';
      keys.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      expect(issuesSpy).not.toHaveBeenCalled();

      const span = host().querySelector('#telemetryTimeSpanSelect') as HTMLSelectElement;
      span.value = '7d';
      span.dispatchEvent(new Event('change'));
      fixture.detectChanges();

      expect(issuesSpy).toHaveBeenCalledWith(expect.objectContaining({
        startDate: component.telemetryStartDate, endDate: component.telemetryEndDate, skip: 0
      }));
    });

    it('labels Feedback with its new and regressed counts, and the other sub-tabs as before', () => {
      openTab();
      const label = () => subTabButtons()[4].textContent?.trim();
      expect(countsSpy).toHaveBeenCalledTimes(1);
      expect(label()).toBe('Feedback');

      component.feedbackCounts = feedbackCounts({ new: 3, regressed: 1, resolved: 4 });
      fixture.detectChanges();
      expect(label()).toBe('Feedback (3 new, 1 regressed)');
      expect(component.subTabLabel('feedback')).toBe('Feedback (3 new, 1 regressed)');

      component.feedbackCounts = feedbackCounts({ new: 3 });
      expect(component.subTabLabel('feedback')).toBe('Feedback (3 new)');

      component.feedbackCounts = feedbackCounts({ regressed: 1 });
      expect(component.subTabLabel('feedback')).toBe('Feedback (1 regressed)');

      component.feedbackCounts = feedbackCounts({ acknowledged: 2, dismissed: 5 });
      expect(component.subTabLabel('feedback')).toBe('Feedback');

      component.feedbackCounts = null;
      expect(component.subTabLabel('feedback')).toBe('Feedback');

      expect(['summary', 'models', 'users', 'groups'].map(tab => component.subTabLabel(tab as AdminTelemetrySubTab)))
        .toEqual(['Summary', 'Models', 'Users', 'Groups']);
    });

    it('takes the counts on init and again when the Feedback view reports a change, and passes them down', () => {
      countsSpy.mockReturnValue(of(feedbackCounts({ new: 2 })));
      openTab('feedback');
      expect(subTabButtons()[4].textContent?.trim()).toBe('Feedback (2 new)');

      countsSpy.mockClear();
      countsSpy.mockReturnValue(of(feedbackCounts({ new: 1, regressed: 2 })));
      component.feedbackView!.countsChange.emit();
      fixture.detectChanges();

      expect(countsSpy).toHaveBeenCalledTimes(1);
      expect(subTabButtons()[4].textContent?.trim()).toBe('Feedback (1 new, 2 regressed)');
      expect(component.feedbackView!.counts()).toEqual(feedbackCounts({ new: 1, regressed: 2 }));
    });

    it('keeps the plain Feedback label when the counts fail', () => {
      countsSpy.mockReturnValue(throwError(() => new Error('down')));
      vi.spyOn(console, 'error').mockImplementation(() => undefined);
      openTab();

      expect(subTabButtons()[4].textContent?.trim()).toBe('Feedback');
    });

    it('reloads the Feedback view and the counts on Refresh once the view is mounted', () => {
      openTab('feedback');
      const reload = vi.spyOn(component.feedbackView!, 'reload');
      countsSpy.mockClear();

      component.refreshFromHeader();

      expect(reload).toHaveBeenCalledTimes(1);
      expect(countsSpy).toHaveBeenCalledTimes(1);
    });
  });
});
