import type { Mock } from "vitest";
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';
import {
  AdminService,
  AiTelemetrySummaryDto,
  AiGovernorStatusDto,
  AiGovernorKeyStatusDto,
  AiTelemetryUserRowDto,
  AiTelemetryUsersPage,
  AiUsageBreakdownRowDto,
  ChatProviderAvailabilityDto,
  OverseerActivityDto
} from '../../services/admin.service';
import { AdminPageStore } from '../admin-page.store';
import { spyAdminService } from '../admin.component.testing';
import { AdminTelemetryTabComponent } from './admin-telemetry-tab.component';
import { TelemetryChartsDialogComponent } from './charts/telemetry-charts-dialog.component';
import { TELEMETRY_BREAKDOWN_STORAGE_KEY } from './telemetry-breakdown';
import { TELEMETRY_USERS_VIEW_STORAGE_KEY } from './users/telemetry-users.component';

/** Stands in for the charts dialog, so the tab's spec does not depend on chart rendering. */
@Component({ selector: 'app-telemetry-charts-dialog', template: '' })
class ChartsDialogStub {
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
  userId: 'u-1', userName: 'alice', requests: 0, chatRequests: 0, titleRequests: 0, benchmarkRequests: 0,
  promptTokens: 0, outputTokens: 0, estimatedCost: 0, pricedRequests: 0, peakHourRequests: 0, peakHourStartUtc: null,
  requestsPerActiveHour: 0, activeDays: 0, lastRequestUtc: null, chatsCreated: 0, userMessages: 0,
  limitRefusals: 0, providerRefusals: 0, isOverseerBlocked: false, hasAccountHold: false, configs: [],
  ...overrides
});

const usersPage = (users: AiTelemetryUserRowDto[] = []): AiTelemetryUsersPage => ({
  users, totalUsers: users.length, securityEventsAvailable: true
});

describe('AdminTelemetryTabComponent', () => {
  let component: AdminTelemetryTabComponent;
  let fixture: ComponentFixture<AdminTelemetryTabComponent>;
  let adminService: AdminService;
  let store: AdminPageStore;
  let usersSpy: Mock;

  beforeEach(async () => {
    localStorage.removeItem(TELEMETRY_BREAKDOWN_STORAGE_KEY);
    localStorage.removeItem(TELEMETRY_USERS_VIEW_STORAGE_KEY);

    await TestBed.configureTestingModule({
      imports: [AdminTelemetryTabComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), AdminPageStore]
    })
      .overrideComponent(AdminTelemetryTabComponent, {
        remove: { imports: [TelemetryChartsDialogComponent] },
        add: { imports: [ChartsDialogStub] }
      })
      .compileComponents();

    adminService = TestBed.inject(AdminService);
    spyAdminService(adminService);
    usersSpy = vi.spyOn(adminService, 'getAiTelemetryUsers').mockReturnValue(of(usersPage()));
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

    const openTab = () => {
      fixture.detectChanges();
    };

    const clearAllButton = (): HTMLButtonElement =>
      (Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[])
        .find(b => (b.textContent ?? '').includes('Clear All Cooldowns'))!;

    /** The breakdown's model cards, without the total. */
    const cards = (): HTMLElement[] =>
      Array.from(host().querySelectorAll<HTMLElement>('.tl-cards > li > article.tl-card:not(.tl-card-total)'));

    const status = () => host().querySelector('.tl-breakdown-status')?.textContent?.trim() ?? '';

    /** Each `dt`/`dd` pair of a summary card's term lists, whitespace collapsed. */
    const termValues = (cardSelector: string): string[][] =>
      Array.from(host().querySelectorAll(`${cardSelector} dl > div`)).map(pair =>
        ['dt', 'dd'].map(tag => (pair.querySelector(tag)?.textContent ?? '').replace(/\s+/g, ' ').trim()));

    const chartsDialog = (): ChartsDialogStub =>
      fixture.debugElement.query(By.directive(ChartsDialogStub)).componentInstance;

    it('renders Clear All Cooldowns as a small image button, aria-disabled only when nothing is rate-limited', () => {
      openTab();

      let button = clearAllButton();
      expect(button.matches('button.btn-gh.btn-gh-small[type="button"]')).toBe(true);
      expect(button.hasAttribute('title')).toBe(false);
      expect(button.getAttribute('aria-disabled')).toBe('true');

      component.governorStatus = governor([key('openai:user:u1', 30)]);
      fixture.detectChanges();

      button = clearAllButton();
      expect(button.hasAttribute('aria-disabled')).toBe(false);
    });

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

    it('renders the Other request line only when the named counts fall short of the total', () => {
      openTab();
      expect(fixture.nativeElement.querySelector('.telemetry-other-requests')).toBeNull();

      component.telemetrySummary = summary({ totalRequests: 13 });
      fixture.detectChanges();

      const other: HTMLElement | null = fixture.nativeElement.querySelector('.telemetry-other-requests');
      expect(other).not.toBeNull();
      expect(other!.textContent).toContain('3');
    });

    it('keeps the partitions section with an empty-state line when no partition exists', () => {
      openTab();

      const section: HTMLElement | null = fixture.nativeElement.querySelector('.telemetry-partitions');
      expect(section).not.toBeNull();
      expect(section!.textContent).toContain('No partitions have been used since the last restart.');
      expect(section!.querySelector('table')).toBeNull();
    });

    it('names each row reset button after its partition and pairs it with a hint tooltip', () => {
      governorSpy.mockReturnValue(of(governor([key('openai:system:7', 30)])));
      openTab();

      const button: HTMLButtonElement = fixture.nativeElement.querySelector('.telemetry-partitions button.action-btn');
      expect(button.getAttribute('type')).toBe('button');
      expect(button.getAttribute('aria-label')).toBe('Clear cooldown for openai:system:7');
      expect(button.hasAttribute('title')).toBe(false);
      const tip: HTMLElement | null = fixture.nativeElement.querySelector('#' + button.getAttribute('interestfor'));
      expect(tip?.getAttribute('popover')).toBe('hint');
    });

    // --- Keys filter ---

    it('asks for system configurations by default and reloads with keys=user when the Keys select changes', () => {
      openTab();
      expect(summarySpy).toHaveBeenLastCalledWith(component.telemetryStartDate, component.telemetryEndDate, 'system');

      const select = host().querySelector('#telemetryKeysSelect') as HTMLSelectElement;
      expect(host().querySelector('label[for="telemetryKeysSelect"]')?.textContent?.trim()).toBe('Keys');
      expect(Array.from(select.options).map(o => o.textContent?.trim())).toEqual(['System AI configurations', 'User keys', 'Both']);

      select.value = 'user';
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();

      expect(summarySpy).toHaveBeenLastCalledWith(component.telemetryStartDate, component.telemetryEndDate, 'user');
      expect(JSON.parse(localStorage.getItem(TELEMETRY_BREAKDOWN_STORAGE_KEY)!).keys).toBe('user');
    });

    // --- Breakdown ---

    it('hides the cards of an unchecked role and updates the status line', () => {
      summarySpy.mockReturnValue(of(summary({
        rows: [
          row({ requests: 5 }),
          row({ provider: 'OpenAI', modelId: 'gpt-x', modelName: 'GPT X', roleGroup: 'title', roleContext: 2, requests: 3 })
        ]
      })));
      openTab();

      expect(cards().length).toBe(2);
      expect(status()).toBe('Showing 2 entries · 8 requests');

      const title = host().querySelector('#tl-role-title') as HTMLInputElement;
      expect(title.closest('label')?.textContent).toContain('3');
      title.click();
      fixture.detectChanges();

      expect(cards().length).toBe(1);
      expect(cards()[0].querySelector('h5')?.textContent?.trim()).toBe('Claude X');
      expect(status()).toBe('One entry · 5 of 8 requests');
      expect(JSON.parse(localStorage.getItem(TELEMETRY_BREAKDOWN_STORAGE_KEY)!).roles).toEqual(['chat', 'benchmark']);
    });

    it('lists Other only while it has rows, and says when no role is selected', () => {
      openTab();
      expect(host().querySelector('#tl-role-other')).toBeNull();

      component.telemetrySummary = summary({ rows: [row({ roleGroup: 'other', roleContext: 9 })] });
      fixture.detectChanges();
      expect(host().querySelector('#tl-role-other')).not.toBeNull();
      expect(host().querySelector('.tl-empty')?.textContent).toContain('No usage for the selected roles in this range.');

      for (const group of ['chat', 'title', 'benchmark']) {
        (host().querySelector(`#tl-role-${group}`) as HTMLInputElement).click();
        fixture.detectChanges();
      }
      expect(host().querySelector('.tl-empty')?.textContent).toContain('No role is selected.');
    });

    it('says so when the range has no usage at all', () => {
      openTab();
      expect(host().querySelector('.tl-empty')?.textContent).toContain('No AI usage logged for the selected date range.');
      expect(host().querySelector('#tl-thinking-select')).toBeNull();
    });

    it('splits a model by thinking level with Show separately', () => {
      summarySpy.mockReturnValue(of(summary({
        rows: [row({ thinkingLevel: 'high', requests: 4 }), row({ thinkingLevel: '', requests: 2 })]
      })));
      openTab();
      expect(cards().length).toBe(1);
      expect(cards()[0].textContent).not.toContain('Thinking:');

      const select = host().querySelector('#tl-thinking-select') as HTMLSelectElement;
      expect(host().querySelector('label[for="tl-thinking-select"]')).not.toBeNull();
      select.value = 'separate';
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();

      expect(cards().map(c => c.querySelector('.tl-card-thinking')?.textContent?.trim())).toEqual(['Thinking: high', 'Thinking: None sent']);
    });

    it('titles a card with the model name and shows the id beside it, else titles it with the id', () => {
      summarySpy.mockReturnValue(of(summary({
        rows: [row({ requests: 2 }), row({ modelId: 'mystery-1', modelName: null })]
      })));
      openTab();

      const [named, unnamed] = cards();
      expect(named.querySelector('h5')?.textContent?.trim()).toBe('Claude X');
      expect(named.querySelector('.model-id-chip')?.textContent?.trim()).toBe('claude-x');
      expect(named.querySelector('app-provider-badge')).not.toBeNull();
      expect(unnamed.querySelector('h5')?.textContent?.trim()).toBe('mystery-1');
      expect(unnamed.querySelector('.model-id-chip')).toBeNull();
    });

    it('names the user of a user-key card and marks system cards under Both', () => {
      summarySpy.mockReturnValue(of(summary({
        keySource: 'both',
        rows: [row({ requests: 3 }), row({ keySource: 'user', userId: 'u-1', userName: 'alice', requests: 2 })]
      })));
      openTab();

      const select = host().querySelector('#telemetryKeysSelect') as HTMLSelectElement;
      select.value = 'both';
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();

      expect(cards().map(c => c.querySelector('.tl-card-key')?.textContent?.trim())).toEqual(['System', 'User key: alice']);
    });

    it('is a labeled list of articles, each labeled by its title, ending in the All shown total', () => {
      summarySpy.mockReturnValue(of(summary({ rows: [row({ requests: 2, outputTokens: 10 }), row({ modelId: 'b', modelName: 'B', outputTokens: 5 })] })));
      openTab();

      const list = host().querySelector('ul.tl-cards') as HTMLElement;
      expect(list.getAttribute('role')).toBe('list');
      expect(list.getAttribute('aria-labelledby')).toBe('tl-breakdown-heading');
      expect(host().querySelector('h4#tl-breakdown-heading')).not.toBeNull();

      for (const article of Array.from(list.querySelectorAll<HTMLElement>(':scope > li > article'))) {
        const title = host().querySelector('#' + article.getAttribute('aria-labelledby'));
        expect(title?.tagName).toBe('H5');
      }
      const total = list.querySelector(':scope > li:last-child > article.tl-card-total') as HTMLElement;
      expect(total.querySelector('h5')?.textContent?.trim()).toBe('All shown');
      expect(total.querySelector('.tl-metrics')?.textContent).toContain('15');
    });

    it('shows provider errors for Chat with the count and one-in, No attempts at none, and In GnollBench for Benchmark', () => {
      summarySpy.mockReturnValue(of(summary({
        rows: [
          row({ requests: 9, validAttempts: 200, providerErrors: 1, pricedRequests: 9, estimatedCost: 2 }),
          row({ modelId: 'b', modelName: 'B', roleGroup: 'title', roleContext: 2, requests: 5, pricedRequests: 0 }),
          row({ modelId: 'c', modelName: 'C', roleGroup: 'benchmark', roleContext: 4, requests: 3 })
        ]
      })));
      openTab();

      const errors = (card: HTMLElement) => card.querySelector('.tl-metric-errors')?.textContent?.replace(/\s+/g, ' ').trim();
      const [chat, title, benchmark] = cards();
      expect(errors(chat)).toContain('0.50%');
      expect(errors(chat)).toContain('1 of 200 attempts');
      expect(errors(chat)).toContain('about 1 in 200');
      expect(errors(title)).toContain('No attempts');
      expect(errors(benchmark)).toContain('In GnollBench');

      // A placeholder value, In GnollBench, No attempts and an unpriced cost are grayed; a rate and a priced cost are not.
      const value = (card: HTMLElement, metric: string) =>
        card.querySelector<HTMLElement>(`.tl-metric-${metric} dd:not(.tl-metric-note)`)!;
      expect(value(chat, 'errors').classList.contains('tl-metric-muted')).toBe(false);
      expect(value(chat, 'cost').classList.contains('tl-metric-muted')).toBe(false);
      expect(value(title, 'errors').textContent?.trim()).toBe('No attempts');
      expect(value(title, 'errors').classList.contains('tl-metric-muted')).toBe(true);
      expect(value(title, 'cost').textContent?.trim()).toBe('—');
      expect(value(title, 'cost').classList.contains('tl-metric-muted')).toBe(true);
      expect(value(benchmark, 'errors').textContent?.trim()).toBe('In GnollBench');
      expect(value(benchmark, 'errors').classList.contains('tl-metric-muted')).toBe(true);
    });

    it('shows a partial cost with its unpriced qualifier', () => {
      summarySpy.mockReturnValue(of(summary({ rows: [row({ requests: 4, pricedRequests: 3, estimatedCost: 1.5 })] })));
      openTab();

      const cost = cards()[0].querySelector('.tl-metric-cost')?.textContent ?? '';
      expect(cost).toContain('$1.50');
      expect(cost).toContain('1 unpriced');
    });

    it('lays the nine metrics out in 9, 5, 5 and 2 columns as the list narrows, lined up with the total', () => {
      summarySpy.mockReturnValue(of(summary({ rows: [row({ requests: 2 }), row({ modelId: 'b', modelName: 'A much longer model name' })] })));
      openTab();

      const metrics = () => cards()[0].querySelector<HTMLElement>('.tl-metrics')!;
      const totalMetrics = () => host().querySelector<HTMLElement>('.tl-card-total .tl-metrics')!;
      const columns = (width: string): number => {
        host().style.width = width;
        fixture.detectChanges();
        void host().offsetWidth;
        return getComputedStyle(metrics()).gridTemplateColumns.split(' ').length;
      };

      expect(columns('1700px')).toBe(9);
      expect(totalMetrics().getBoundingClientRect().left).toBe(metrics().getBoundingClientRect().left);
      expect(['1300px', '900px', '380px'].map(columns)).toEqual([5, 5, 2]);
    });

    // --- Summary cards ---

    it('shows Not recorded on the availability card until the ledger has a row, then the rate', () => {
      openTab();
      const rate = () => host().querySelector('.telemetry-availability-rate')?.textContent ?? '';
      expect(rate()).toContain('Not recorded');
      expect(host().querySelector('.telemetry-error-classes-card')).toBeNull();

      component.telemetrySummary = summary({
        chatAvailability: availability({
          recorded: true, validAttempts: 1480, providerErrors: 12, providerErrorRate: 12 / 1480, low: 0.0046, high: 0.014,
          titleValidAttempts: 140, titleProviderErrors: 2
        })
      });
      fixture.detectChanges();

      expect(rate()).toContain('0.81%');
      expect(host().querySelector('.telemetry-availability-count')?.textContent).toContain('12 of 1,480 attempts');
      expect(termValues('.telemetry-availability-card')).toContainEqual(['Title generation', '2 of 140 attempts']);
      expect(host().querySelector('.telemetry-error-classes-card')).not.toBeNull();
    });

    it('shows the error classes and the unattributed and our-side counts on their own card', () => {
      summarySpy.mockReturnValue(of(summary({
        chatAvailability: availability({
          recorded: true, validAttempts: 300, providerErrors: 6, providerErrorRate: 6 / 300,
          overloaded: 3, serverErrors: 2, timeouts: 1, streamFailures: 0,
          unattributedErrors: 4, rateLimitedUnattributed: 3, transportUnattributed: 1, inclusiveRate: 0.02, ourSideFailures: 5
        })
      })));
      openTab();

      const card = '.telemetry-error-classes-card';
      expect(host().querySelector(`${card} .telemetry-error-total`)?.textContent).toContain('6');
      expect(termValues(card)).toEqual([
        ['Overloaded', '3'],
        ['Server errors', '2'],
        ['Timeouts', '1'],
        ['Stream failures', '0'],
        ['Unattributed', '4 rate limited 3 · transport 1 · 2.0% with them'],
        ['Our side', '5 excluded from the rate']
      ]);
      const notes = Array.from(host().querySelectorAll(`${card} .telemetry-dd-note`)).map(n => n.textContent?.trim());
      expect(notes).toEqual(['rate limited 3 · transport 1 · 2.0% with them', 'excluded from the rate']);
    });

    it('shows the estimated cost with the unpriced requests and when cost recording began', () => {
      const today = component.formatLocalDate(new Date());
      summarySpy.mockReturnValue(of(summary({
        totalRequests: 10, pricedRequests: 7, totalEstimatedCost: 2.5, costRecordedSinceUtc: `${today}T08:00:00Z`,
        rows: [row({ requests: 7, estimatedCost: 2.5, pricedRequests: 7 })]
      })));
      openTab();

      const card = host().querySelector('.telemetry-cost-card')?.textContent ?? '';
      expect(card).toContain('$2.50');
      expect(card).toContain('Chat: $2.50');
      expect(card).toContain('3 of 10 requests unpriced');
      expect(card).toContain(`Cost recorded since ${today}`);
    });

    it("shows Overseer activity for all keys with its counts, the anonymous incognito line and the day counting began", () => {
      const today = component.formatLocalDate(new Date());
      summarySpy.mockReturnValue(of(summary({
        activity: activity({
          activeUsers: 1234, newUsers: 5, chatsCreated: 20, confidentialChatsCreated: 2, gnollHackChatsCreated: 3,
          userMessages: 10, assistantMessages: 9, incognitoChatsCreated: 4, incognitoUserMessages: 7, countedSinceUtc: today
        })
      })));
      openTab();

      const card = () => (host().querySelector('.telemetry-activity-card')?.textContent ?? '').replace(/\s+/g, ' ');
      expect(card()).toContain('all keys');
      expect(host().querySelector('.telemetry-active-users')?.textContent).toContain('1,234');
      expect(card()).toContain('New users: 5');
      expect(card()).toContain('Chats: 20');
      expect(card()).toContain('confidential 2 · GnollHack 3');
      expect(card()).toContain('Messages: 19');
      expect(card()).toContain('sent 10 · replies 9');
      expect(card()).toContain('Incognito chats 4 · messages 7');
      expect(card()).toContain('not attributed to users');
      expect(card()).toContain(`Counted since ${today}`);

      component.telemetrySummary = summary({ activity: activity({ countedSinceUtc: '2000-01-01' }) });
      fixture.detectChanges();
      expect(card()).not.toContain('Counted since');
    });

    // --- Charts ---

    it('opens the charts dialog with the range, the Keys filter and the selected roles', () => {
      openTab();

      const button = host().querySelector('.tab-header-row button.telemetry-charts-btn') as HTMLButtonElement;
      expect(button.matches('.btn-gh[type="button"]')).toBe(true);
      expect(button.textContent?.trim()).toBe('Charts');
      expect(button.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
      button.click();

      expect(chartsDialog().open).toHaveBeenCalledWith({
        start: component.telemetryStartDate,
        end: component.telemetryEndDate,
        keys: 'system',
        roles: ['chat', 'title', 'benchmark']
      });
    });

    // --- Users view ---

    it('places the users view under its heading and scope line, and opens a user\'s charts from it', () => {
      usersSpy.mockReturnValue(of(usersPage([userRow({ userId: 'u-3', userName: 'carol', requests: 4 })])));
      openTab();

      const section = host().querySelector('section.telemetry-section-card.tl-users') as HTMLElement;
      expect(section.querySelector('h4#tl-users-heading')?.textContent?.trim()).toBe('System AI usage by user');
      expect(section.querySelector('.tl-users-scope')?.textContent).toContain('Keys filter does not apply');
      expect(section.querySelector('app-telemetry-users')).not.toBeNull();
      expect(usersSpy).toHaveBeenCalledWith(expect.objectContaining({
        startDate: component.telemetryStartDate, endDate: component.telemetryEndDate, sort: 'cost', skip: 0
      }));

      (section.querySelector('button[aria-label="Usage charts of carol"]') as HTMLButtonElement).click();

      expect(chartsDialog().open).toHaveBeenCalledWith(expect.objectContaining({
        start: component.telemetryStartDate, end: component.telemetryEndDate, keys: 'system', userId: 'u-3', userName: 'carol'
      }));
    });

    it('filters the users view to a user a Usage link asked for, and clears the request', () => {
      usersSpy.mockReturnValue(of(usersPage([userRow({ userId: 'u-5', userName: 'eve' })])));
      store.pendingTelemetryUserId = 'u-5';
      store.pendingTelemetryUserName = 'eve';

      openTab();

      expect(store.pendingTelemetryUserId).toBeNull();
      expect(store.pendingTelemetryUserName).toBeNull();
      expect(usersSpy).toHaveBeenCalledWith(expect.objectContaining({ userId: 'u-5' }));
      expect(host().querySelector('.tl-users-status')?.textContent?.trim()).toBe('Showing 1 user — eve');
    });

    it('filters the users view when a Usage request arrives while the tab is open', () => {
      openTab();
      usersSpy.mockClear();

      store.requestTelemetryUser('u-6', 'frank');
      fixture.detectChanges();

      expect(store.pendingTelemetryUserId).toBeNull();
      expect(usersSpy).toHaveBeenCalledWith(expect.objectContaining({ userId: 'u-6' }));
    });
  });
});
