import type { Mock } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';
import { AdminComponent } from './admin.component';
import { AdminService, UsersResponse, GroupDto, SystemAiConfigDto, DefaultApiKeyStatus } from '../services/admin.service';
import { buildSystemConfig, spyAdminService } from './admin.component.testing';
import { AdminUsersTabComponent } from './users-tab/admin-users-tab.component';
import { AdminConfigsTabComponent } from './configs-tab/admin-configs-tab.component';

describe('AdminComponent', () => {
  let component: AdminComponent;
  let fixture: ComponentFixture<AdminComponent>;
  let adminService: AdminService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();

    adminService = TestBed.inject(AdminService);
    spyAdminService(adminService);

    fixture = TestBed.createComponent(AdminComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('loadData', () => {
    it('should populate users, groups, and configs on normal success', () => {
      const mockUsers: UsersResponse = {
        rows: [{ id: '1', userName: 'admin', email: 'admin@test.com', groups: [] }],
        totalCount: 1
      };
      const mockGroups: GroupDto[] = [{ id: 1, displayName: 'Admins' }];
      const mockConfigs: SystemAiConfigDto[] = [buildSystemConfig({
        id: 1,
        displayName: 'System GPT-4o',
        provider: 'openai',
        modelId: 'gpt-4o',
        isSystemWide: true
      })];

      (adminService.getUsers as Mock).mockReturnValue(of(mockUsers));
      (adminService.getGroups as Mock).mockReturnValue(of(mockGroups));
      (adminService.getSystemConfigs as Mock).mockReturnValue(of(mockConfigs));

      // ngOnInit runs loadData, and the Users tab it then shows loads the users.
      fixture.detectChanges();

      const usersTab: AdminUsersTabComponent = fixture.debugElement.query(By.directive(AdminUsersTabComponent)).componentInstance;
      expect(usersTab.users.length).toBe(1);
      expect(component.store.groups.length).toBe(1);
      expect(component.store.configs.length).toBe(1);
      expect(component.loading).toBe(false);
    });

    it('should catch TypeError: Failed to fetch on getUsers and reset loading to false', () => {
      (adminService.getUsers as Mock).mockReturnValue(throwError(() => new TypeError('Failed to fetch')));

      expect(() => {
        fixture.detectChanges();
      }).not.toThrow();

      expect(component.loading).toBe(false);
    });

    it('should catch TypeError: Failed to fetch on nested getGroups and reset loading to false', () => {
      (adminService.getUsers as Mock).mockReturnValue(of({ rows: [], totalCount: 0 }));
      (adminService.getGroups as Mock).mockReturnValue(throwError(() => new TypeError('Failed to fetch')));

      expect(() => {
        component.loadData();
      }).not.toThrow();

      expect(component.loading).toBe(false);
    });

    it('should catch TypeError: Failed to fetch on nested getSystemConfigs and reset loading to false', () => {
      (adminService.getUsers as Mock).mockReturnValue(of({ rows: [], totalCount: 0 }));
      (adminService.getGroups as Mock).mockReturnValue(of([]));
      (adminService.getSystemConfigs as Mock).mockReturnValue(throwError(() => new TypeError('Failed to fetch')));

      expect(() => {
        component.loadData();
      }).not.toThrow();

      expect(component.loading).toBe(false);
    });
  });

  // ---------------------------------------------------------------------------
  // Tab semantics and keyboard navigation for the admin dashboard's main tab
  // row.
  // ---------------------------------------------------------------------------
  describe('main tab row', () => {
    const mainTabList = () =>
      fixture.nativeElement.querySelector('[role="tablist"][aria-label="Admin sections"]');
    const mainTabs = () =>
      Array.from(
        fixture.nativeElement.querySelectorAll('[aria-label="Admin sections"] [role="tab"]')
      ) as HTMLButtonElement[];

    beforeEach(() => fixture.detectChanges());

    it('should expose the tab row as a labelled tablist with one tab per section', () => {
      expect(mainTabList()).toBeTruthy();
      expect(mainTabs().length).toBe(component.tabs.length);
    });

    it('should mark exactly one tab selected, matching activeTab', () => {
      const selected = mainTabs().filter(t => t.getAttribute('aria-selected') === 'true');
      expect(selected.length).toBe(1);
      expect(selected[0].id).toBe('admin-tab-' + component.activeTab);
    });

    it('should give exactly one tab tabindex="0" and the rest tabindex="-1"', () => {
      const all = mainTabs();
      expect(all.filter(t => t.getAttribute('tabindex') === '0').length).toBe(1);
      expect(all.filter(t => t.getAttribute('tabindex') === '-1').length).toBe(all.length - 1);
    });

    it('should give every tab an explicit type="button"', () => {
      expect(mainTabs().every(t => t.getAttribute('type') === 'button')).toBe(true);
    });

    it('should wrap forward from the last tab to the first with ArrowRight', () => {
      const last = component.tabs.length - 1;
      component.selectTab(component.tabs[last].id);
      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }), last);
      expect(component.activeTab).toBe(component.tabs[0].id);
    });

    it('should wrap backward from the first tab to the last with ArrowLeft', () => {
      component.selectTab(component.tabs[0].id);
      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowLeft' }), 0);
      expect(component.activeTab).toBe(component.tabs[component.tabs.length - 1].id);
    });

    it('should select the first and last tab with Home and End', () => {
      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'End' }), 0);
      expect(component.activeTab).toBe(component.tabs[component.tabs.length - 1].id);

      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'Home' }), 6);
      expect(component.activeTab).toBe(component.tabs[0].id);
    });

    it('should ignore keys that are not part of the tab keyboard model', () => {
      component.selectTab('groups');
      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'x' }), 1);
      expect(component.activeTab).toBe('groups');
    });

    it('should move focus to the newly selected tab after a keyboard change', () => {
      mainTabs()[0].focus();
      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }), 0);
      fixture.detectChanges();
      expect(document.activeElement)
        .toBe(fixture.nativeElement.querySelector('#admin-tab-groups'));
    });

    it('should render a tabpanel labelled by the selected tab', () => {
      const panel = fixture.nativeElement.querySelector('[role="tabpanel"]');
      expect(panel).toBeTruthy();
      expect(panel.id).toBe('admin-panel-' + component.activeTab);
      expect(panel.getAttribute('aria-labelledby')).toBe('admin-tab-' + component.activeTab);
      expect(panel.getAttribute('tabindex')).toBe('0');
    });

    it('should not rely on the removed admin-tab-active class', () => {
      expect(fixture.nativeElement.querySelectorAll('.admin-tab-active').length).toBe(0);
    });
  });

  describe('Configs tab hand-off', () => {
    it('selects the Benchmark tab and hands it the run the Configs tab asks to open', () => {
      fixture.detectChanges();
      component.selectTab('configs');
      fixture.detectChanges();

      const configsTab: AdminConfigsTabComponent = fixture.debugElement.query(By.directive(AdminConfigsTabComponent)).componentInstance;
      configsTab.openBenchmarkRun.emit(900);

      expect(component.activeTab).toBe('benchmark');
      expect(component.pendingRunId).toBe(900);
    });
  });

  describe('default API keys', () => {
    const keyStatus = (provider: string, overrides: Partial<DefaultApiKeyStatus> = {}): DefaultApiKeyStatus => ({
      provider, hasKey: false, keyHint: null, updatedAtUtc: null,
      verification: { status: null, checkedAtUtc: null, message: null },
      usedBy: [],
      ...overrides
    });

    const config = (id: number, displayName: string, overrides: Partial<SystemAiConfigDto> = {}): SystemAiConfigDto =>
      buildSystemConfig({ id, displayName, orderIndex: id, ...overrides });

    it('places API Keys after Groups and before System Configs', () => {
      const ids = component.tabs.map(t => t.id);
      expect(ids.indexOf('apikeys')).toBe(ids.indexOf('groups') + 1);
      expect(ids.indexOf('configs')).toBe(ids.indexOf('apikeys') + 1);
      expect(component.tabs.find(t => t.id === 'apikeys')!.label).toBe('API Keys');
    });

    it('renders the API Keys panel with the loaded keys and reloads them when the tab is chosen', () => {
      (adminService.getDefaultApiKeys as Mock).mockReturnValue(of([
        keyStatus('Anthropic', { hasKey: true, keyHint: 'ab12' }), keyStatus('Google'), keyStatus('OpenAI')
      ]));
      fixture.detectChanges();
      (adminService.getDefaultApiKeys as Mock).mockClear();

      (fixture.nativeElement.querySelector('#admin-tab-apikeys') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(adminService.getDefaultApiKeys).toHaveBeenCalledTimes(1);
      const panel = fixture.nativeElement.querySelector('#admin-panel-apikeys') as HTMLElement;
      expect(panel.getAttribute('role')).toBe('tabpanel');
      expect(panel.getAttribute('aria-labelledby')).toBe('admin-tab-apikeys');
      expect(panel.querySelector('app-admin-api-keys')).not.toBeNull();
      expect(panel.querySelector('.aak-card[data-provider="Anthropic"] .aak-key-badge')!.textContent!.trim())
        .toBe('Default key saved');
    });

    it('reloads the keys and the configurations after the tab changes a key', () => {
      fixture.detectChanges();
      (adminService.getDefaultApiKeys as Mock).mockClear();
      (adminService.getSystemConfigs as Mock).mockClear();
      (adminService.getSystemConfigs as Mock).mockReturnValue(of([config(2, 'Default Gone', { useDefaultApiKey: true, hasApiKey: false, isEnabled: false })]));

      component.store.onDefaultApiKeysChanged();

      expect(adminService.getDefaultApiKeys).toHaveBeenCalledTimes(1);
      expect(adminService.getSystemConfigs).toHaveBeenCalledTimes(1);
      expect(component.store.configs.length).toBe(1);
    });
  });

  describe('query-parameter navigation', () => {
    let navigateSpy: Mock;

    const removal = {
      queryParams: { tab: null, subtab: null, suiteId: null },
      queryParamsHandling: 'merge',
      replaceUrl: true
    };

    beforeEach(() => {
      navigateSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    });

    it('selects the Benchmark tab, hands it the sub-tab and suite, and removes the parameters', () => {
      component.applyNavigationParams(convertToParamMap({ tab: 'benchmark', subtab: 'suites', suiteId: '7' }));

      expect(component.activeTab).toBe('benchmark');
      expect(component.pendingBenchmarkNavigation).toEqual({ subTab: 'suites', suiteId: 7 });
      expect(navigateSpy).toHaveBeenCalledTimes(1);
      expect(navigateSpy).toHaveBeenCalledWith([], expect.objectContaining(removal));
    });

    it('passes a non-numeric suite id on as null', () => {
      component.applyNavigationParams(convertToParamMap({ tab: 'benchmark', subtab: 'suites', suiteId: 'abc' }));

      expect(component.pendingBenchmarkNavigation).toEqual({ subTab: 'suites', suiteId: null });
    });

    it('keeps the active tab for an unknown tab and still removes the parameters', () => {
      component.applyNavigationParams(convertToParamMap({ tab: 'bogus' }));

      expect(component.activeTab).toBe('users');
      expect(component.pendingBenchmarkNavigation).toBeNull();
      expect(navigateSpy).toHaveBeenCalledWith([], expect.objectContaining(removal));
    });

    it('does nothing without navigation parameters', () => {
      component.applyNavigationParams(convertToParamMap({}));

      expect(component.activeTab).toBe('users');
      expect(navigateSpy).not.toHaveBeenCalled();
    });

    it('selects another tab without a Benchmark request', () => {
      component.applyNavigationParams(convertToParamMap({ tab: 'database' }));

      expect(component.activeTab).toBe('database');
      expect(component.pendingBenchmarkNavigation).toBeNull();
    });

    it('reads the parameters from the route on init', () => {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        imports: [AdminComponent],
        providers: [
          provideRouter([]),
          provideHttpClient(),
          provideHttpClientTesting(),
          { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap({ tab: 'telemetry' })) } }
        ]
      });
      const service = TestBed.inject(AdminService);
      spyAdminService(service);
      // The Telemetry tab loads its summary and governor status when it is created.
      vi.spyOn(service, 'getAiTelemetrySummary').mockReturnValue(of({
        totalRequests: 0, totalChatRequests: 0, totalTitleRequests: 0, totalBenchmarkRequests: 0,
        totalInputTokens: 0, totalOutputTokens: 0, totalCacheReadTokens: 0, totalCacheCreationTokens: 0,
        cacheHitRatio: 0, avgDurationMs: 0, models: []
      }));
      vi.spyOn(service, 'getGovernorStatus').mockReturnValue(of({ maxConcurrentCalls: 4, maxRetryAfterSeconds: 90, activeKeys: [] }));
      vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

      const routed = TestBed.createComponent(AdminComponent);
      routed.detectChanges();

      expect(routed.componentInstance.activeTab).toBe('telemetry');
      routed.destroy();
    });
  });
});
