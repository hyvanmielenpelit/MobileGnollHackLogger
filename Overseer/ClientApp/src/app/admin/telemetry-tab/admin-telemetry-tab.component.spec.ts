import type { Mock } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { of, throwError } from 'rxjs';
import { AdminService, AiTelemetrySummaryDto, AiGovernorStatusDto, AiGovernorKeyStatusDto } from '../../services/admin.service';
import { AdminComponent } from '../admin.component';
import { AdminPageStore } from '../admin-page.store';
import { configureAdminTestBed } from '../admin.component.testing';
import { AdminTelemetryTabComponent } from './admin-telemetry-tab.component';

describe('AdminTelemetryTabComponent', () => {
  let component: AdminTelemetryTabComponent;
  let fixture: ComponentFixture<AdminTelemetryTabComponent>;
  let adminService: AdminService;
  let store: AdminPageStore;

  beforeEach(async () => {
    ({ adminService, store } = await configureAdminTestBed(AdminTelemetryTabComponent));

    fixture = TestBed.createComponent(AdminTelemetryTabComponent);
    component = fixture.componentInstance;
  });

  describe('AI telemetry tab', () => {
    const summary = (overrides: Partial<AiTelemetrySummaryDto> = {}): AiTelemetrySummaryDto => ({
      totalRequests: 10, totalChatRequests: 6, totalTitleRequests: 2, totalBenchmarkRequests: 2,
      totalInputTokens: 0, totalOutputTokens: 0, totalCacheReadTokens: 0, totalCacheCreationTokens: 0,
      cacheHitRatio: 0, avgDurationMs: 0, models: [], ...overrides
    });

    const governor = (keys: AiGovernorKeyStatusDto[] = []): AiGovernorStatusDto => ({
      maxConcurrentCalls: 4, maxRetryAfterSeconds: 90, activeKeys: keys
    });

    const key = (credentialKey: string, remainingSeconds: number): AiGovernorKeyStatusDto => ({
      credentialKey, isRateLimited: remainingSeconds > 0, remainingCooldownSeconds: remainingSeconds, inFlightCalls: 0
    });

    let governorSpy: Mock;
    let toastSpy: Mock;

    beforeEach(() => {
      vi.spyOn(adminService, 'getAiTelemetrySummary').mockReturnValue(of(summary()));
      governorSpy = vi.spyOn(adminService, 'getGovernorStatus').mockReturnValue(of(governor()));
      toastSpy = vi.spyOn(store, 'showToast').mockReturnValue(undefined);
    });

    const openTab = () => {
      fixture.detectChanges();
    };

    const clearAllButton = (): HTMLButtonElement =>
      (Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[])
        .find(b => (b.textContent ?? '').includes('Clear All Cooldowns'))!;

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
        // The Admin page provides a page store of its own, so this tab instance does not use `store`.
        const shell = TestBed.createComponent(AdminComponent);
        shell.detectChanges();
        shell.componentInstance.selectTab('telemetry');
        shell.detectChanges();
        const tab: AdminTelemetryTabComponent =
          shell.debugElement.query(By.directive(AdminTelemetryTabComponent)).componentInstance;

        shell.componentInstance.selectTab('groups');
        shell.detectChanges();

        vi.advanceTimersByTime(3000);

        expect(tab.governorStatus!.activeKeys[0].remainingCooldownSeconds).toBe(5);
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
  });
});
