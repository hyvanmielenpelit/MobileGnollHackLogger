import type { Mock } from "vitest";
import { Location } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ParamMap, Params, Router, convertToParamMap, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';
import { NG_CHARTS_CONFIGURATION } from 'ng2-charts';
import { APP_CHART_REGISTRABLES } from '../chart-registrables';
import { AdminComponent } from './admin.component';
import { AdminPageStore } from './admin-page.store';
import { AdminService, GroupDto, SystemAiConfigDto } from '../services/admin.service';
import { AdminUsersService } from '../services/admin-users.service';
import { AdminDevtoolsService } from '../services/admin-devtools.service';
import { adminTestRoutes, buildSystemConfig, spyAdminDevtoolsService, spyAdminService } from './admin.component.testing';
import { AdminUsersTabComponent } from './users-tab/admin-users-tab.component';
import { AdminUserListComponent } from './users-tab/user-list/admin-user-list.component';
import { buildAdminUser, buildUsersPage, spyAdminUsersService } from './users-tab/admin-users.testing';
import { AdminConfigsTabComponent } from './configs-tab/admin-configs-tab.component';
import { AdminBenchmarkComponent } from './benchmark/benchmark.component';
import { ModelResolutionDialogComponent } from '../shared/model-resolution-dialog/model-resolution-dialog.component';

/** A route's path or query parameters. */
const pm = (params: Params = {}): ParamMap => convertToParamMap(params);

describe('AdminComponent', () => {
  let component: AdminComponent;
  let fixture: ComponentFixture<AdminComponent>;
  let adminService: AdminService;
  let usersService: AdminUsersService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminComponent],
      providers: [
        provideRouter(adminTestRoutes(AdminComponent)),
        provideLocationMocks(),
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();

    adminService = TestBed.inject(AdminService);
    spyAdminService(adminService);
    usersService = TestBed.inject(AdminUsersService);
    spyAdminUsersService(usersService);
    spyAdminDevtoolsService(TestBed.inject(AdminDevtoolsService));

    fixture = TestBed.createComponent(AdminComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    document.querySelectorAll('dialog').forEach(d => { if (d.open) d.close(); });
  });

  it('provides the application\'s chart registrables to the admin page', () => {
    expect(fixture.debugElement.injector.get(NG_CHARTS_CONFIGURATION).registerables).toEqual(APP_CHART_REGISTRABLES);
  });

  describe('loadData', () => {
    it('should populate users, groups, and configs on normal success', () => {
      const mockUsers = buildUsersPage([buildAdminUser({ id: '1', userName: 'admin', email: 'admin@test.com' })]);
      const mockGroups: GroupDto[] = [{ id: 1, displayName: 'Admins' }];
      const mockConfigs: SystemAiConfigDto[] = [buildSystemConfig({
        id: 1,
        displayName: 'System GPT-4o',
        provider: 'openai',
        modelId: 'gpt-4o',
        isSystemWide: true
      })];

      (usersService.list as Mock).mockReturnValue(of(mockUsers));
      (adminService.getGroups as Mock).mockReturnValue(of(mockGroups));
      (adminService.getSystemConfigs as Mock).mockReturnValue(of(mockConfigs));

      // ngOnInit runs loadData, and the Users tab it then shows loads the users.
      fixture.detectChanges();

      expect(fixture.debugElement.query(By.directive(AdminUsersTabComponent))).toBeTruthy();
      const list: AdminUserListComponent = fixture.debugElement.query(By.directive(AdminUserListComponent)).componentInstance;
      expect(list.rows.length).toBe(1);
      expect(component.store.groups.length).toBe(1);
      expect(component.store.configs.length).toBe(1);
      expect(component.loading).toBe(false);
    });

    it('should catch TypeError: Failed to fetch on nested getGroups and reset loading to false', () => {
      (adminService.getGroups as Mock).mockReturnValue(throwError(() => new TypeError('Failed to fetch')));

      expect(() => {
        component.loadData();
      }).not.toThrow();

      expect(component.loading).toBe(false);
    });

    it('should catch TypeError: Failed to fetch on nested getSystemConfigs and reset loading to false', () => {
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

    it('should list the seven sections in order', () => {
      expect(mainTabs().map(t => t.id)).toEqual([
        'admin-tab-users', 'admin-tab-configs', 'admin-tab-database', 'admin-tab-telemetry',
        'admin-tab-security', 'admin-tab-benchmark', 'admin-tab-devtools'
      ]);
      expect(mainTabs().map(t => t.textContent?.trim())).toEqual([
        'Users', 'System Configs', 'Database', 'AI Telemetry', 'Security', 'GnollBench', 'Services'
      ]);
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

    it('should move focus to the newly selected tab after a keyboard change', () => {
      mainTabs()[0].focus();
      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }), 0);
      fixture.detectChanges();
      expect(document.activeElement)
        .toBe(fixture.nativeElement.querySelector('#admin-tab-configs'));
      expect(component.activeTab).toBe('configs');
    });

    it('should render a tabpanel labelled by the selected tab', () => {
      const panel = fixture.nativeElement.querySelector('[role="tabpanel"]');
      expect(panel).toBeTruthy();
      expect(panel.id).toBe('admin-panel-' + component.activeTab);
      expect(panel.getAttribute('aria-labelledby')).toBe('admin-tab-' + component.activeTab);
      expect(panel.getAttribute('tabindex')).toBe('0');
    });
  });

  describe('Security tab', () => {
    beforeEach(() => {
      vi.spyOn(adminService, 'getSecuritySummary').mockReturnValue(of({ kinds: [], pendingDeletionRequests: 0, activeAccountHolds: 0 }));
      vi.spyOn(adminService, 'getSecurityEvents').mockReturnValue(of({ totalCount: 0, rows: [] }));
    });

    it('places Security right after AI Telemetry', () => {
      const ids = component.tabs.map(t => t.id);
      expect(ids.indexOf('security')).toBe(ids.indexOf('telemetry') + 1);
      expect(component.tabs.find(t => t.id === 'security')!.label).toBe('Security');
      expect(component.tabs.length).toBe(7);
    });

    it('renders the Security tab panel when the tab is chosen', () => {
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('#admin-tab-security') as HTMLButtonElement).click();
      fixture.detectChanges();

      const panel = fixture.nativeElement.querySelector('#admin-panel-security') as HTMLElement;
      expect(panel.getAttribute('role')).toBe('tabpanel');
      expect(panel.getAttribute('aria-labelledby')).toBe('admin-tab-security');
      expect(panel.querySelector('app-admin-security-tab')).not.toBeNull();
    });
  });

  describe('tab requests from the tabs', () => {
    beforeEach(() => fixture.detectChanges());

    it('selects the tab a request names', () => {
      component.store.requestTab('database');

      expect(component.activeTab).toBe('database');
    });

    it('ignores a request naming no tab', () => {
      component.selectTab('configs');
      component.store.requestTab('bogus');

      expect(component.activeTab).toBe('configs');
    });

    it('opens the Users tab on the sub-tab a request remembers', () => {
      component.store.usersSubTab = 'deletions';
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate');

      component.store.requestTab('users');

      expect(component.activeTab).toBe('users');
      expect(navigate).toHaveBeenCalledWith(['/admin', 'users', 'deletions'], { queryParamsHandling: 'preserve', replaceUrl: true });
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
    const config = (id: number, displayName: string, overrides: Partial<SystemAiConfigDto> = {}): SystemAiConfigDto =>
      buildSystemConfig({ id, displayName, orderIndex: id, ...overrides });

    it('has no API Keys tab: the keys are a dialog of System Configs', () => {
      expect(component.tabs.map(t => t.id as string)).not.toContain('apikeys');
      expect(component.tabs.map(t => t.id as string)).not.toContain('groups');
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

  // ---------------------------------------------------------------------------
  // The address: what applyAddress does with one path and query, without the
  // router's own navigation.
  // ---------------------------------------------------------------------------
  describe('address handling', () => {
    let navigateSpy: Mock;

    const removal = {
      queryParams: { suiteId: null, resolveConfig: null },
      queryParamsHandling: 'merge',
      replaceUrl: true
    };

    beforeEach(() => {
      navigateSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true) as unknown as Mock;
    });

    it('selects the tab and sub-tab a canonical path names, and navigates nowhere', () => {
      component.applyAddress(pm({ tab: 'users', subtab: 'groups' }), pm());

      expect(component.activeTab).toBe('users');
      expect(component.store.usersSubTab).toBe('groups');
      expect(navigateSpy).not.toHaveBeenCalled();
    });

    it('selects Services on the sub-tab the path names', () => {
      component.applyAddress(pm({ tab: 'services', subtab: 'config-health' }), pm());

      expect(component.activeTab).toBe('devtools');
      expect(component.store.devtoolsSubTab).toBe('config-health');
      expect(navigateSpy).not.toHaveBeenCalled();
    });

    it('replaces the retired Services slug with its current one', () => {
      component.applyAddress(pm({ tab: 'devtools', subtab: 'config-health' }), pm());

      expect(navigateSpy).toHaveBeenCalledWith(['/admin', 'services', 'config-health'], { queryParamsHandling: 'preserve', replaceUrl: true });
    });

    it('opens the API Keys dialog only on its own path', () => {
      component.applyAddress(pm({ tab: 'configs', subtab: 'api-keys' }), pm());
      expect(component.activeTab).toBe('configs');
      expect(component.store.apiKeysDialogOpen).toBe(true);

      component.applyAddress(pm({ tab: 'configs' }), pm());
      expect(component.store.apiKeysDialogOpen).toBe(false);
    });

    for (const [params, target] of [
      [{}, ['/admin', 'users']],
      [{ tab: 'bogus' }, ['/admin', 'users']],
      [{ tab: 'users', subtab: 'users' }, ['/admin', 'users']],
      [{ tab: 'users', subtab: 'bogus' }, ['/admin', 'users']],
      [{ tab: 'services', subtab: 'source' }, ['/admin', 'services']],
      [{ tab: 'devtools', subtab: 'sentry' }, ['/admin', 'services', 'developer-tools']],
      [{ tab: 'database', subtab: 'extra' }, ['/admin', 'database']]
    ] as [Params, string[]][]) {
      it(`replaces the non-canonical path ${JSON.stringify(params)} with ${target.join('/')}, keeping the query`, () => {
        component.applyAddress(pm(params), pm({ sessionId: 's1' }));

        expect(navigateSpy).toHaveBeenCalledTimes(1);
        expect(navigateSpy).toHaveBeenCalledWith(target, { queryParamsHandling: 'preserve', replaceUrl: true });
      });
    }

    for (const [query, target] of [
      [{ tab: 'groups' }, ['/admin', 'users', 'groups']],
      [{ tab: 'apikeys' }, ['/admin', 'configs', 'api-keys']],
      [{ tab: 'users', subtab: 'deletions' }, ['/admin', 'users', 'deletions']],
      [{ tab: 'users', subtab: 'log' }, ['/admin', 'users', 'log']],
      [{ tab: 'benchmark', subtab: 'suites', suiteId: '7' }, ['/admin', 'gnollbench', 'suites']],
      [{ tab: 'configs', resolveConfig: '7' }, ['/admin', 'configs']],
      [{ tab: 'database' }, ['/admin', 'database']],
      [{ tab: 'bogus' }, ['/admin', 'users']]
    ] as [Params, string[]][]) {
      it(`replaces the old link ?${new URLSearchParams(query)} with ${target.join('/')}, keeping the other parameters`, () => {
        component.applyAddress(pm(), pm(query));

        expect(navigateSpy).toHaveBeenCalledTimes(1);
        expect(navigateSpy).toHaveBeenCalledWith(target, {
          queryParams: { tab: null, subtab: null },
          queryParamsHandling: 'merge',
          replaceUrl: true
        });
      });
    }

    it('issues one navigation for an old link on a non-canonical path', () => {
      component.applyAddress(pm({ tab: 'bogus', subtab: 'x' }), pm({ tab: 'groups' }));

      expect(navigateSpy).toHaveBeenCalledTimes(1);
      expect(navigateSpy.mock.lastCall![0]).toEqual(['/admin', 'users', 'groups']);
    });

    it('hands GnollBench the sub-tab and suite of a Manage Suites link, and removes the suite id', () => {
      component.applyAddress(pm({ tab: 'gnollbench', subtab: 'suites' }), pm({ suiteId: '7' }));

      expect(component.activeTab).toBe('benchmark');
      expect(component.store.benchmarkSubTab).toBe('suites');
      expect(component.pendingBenchmarkNavigation).toEqual({ subTab: 'suites', suiteId: 7 });
      expect(navigateSpy).toHaveBeenCalledTimes(1);
      expect(navigateSpy).toHaveBeenCalledWith([], expect.objectContaining(removal));
    });

    it('passes a non-numeric suite id on as null', () => {
      component.applyAddress(pm({ tab: 'gnollbench', subtab: 'suites' }), pm({ suiteId: 'abc' }));

      expect(component.pendingBenchmarkNavigation).toEqual({ subTab: 'suites', suiteId: null });
    });

    it('ignores a suite id on another GnollBench sub-tab and still removes it', () => {
      component.applyAddress(pm({ tab: 'gnollbench', subtab: 'history' }), pm({ suiteId: '7' }));

      expect(component.pendingBenchmarkNavigation).toEqual({ subTab: 'history', suiteId: null });
      expect(navigateSpy).toHaveBeenCalledWith([], expect.objectContaining(removal));
    });

    it('hands GnollBench only a sub-tab it does not show, and again after its panel was removed', () => {
      component.applyAddress(pm({ tab: 'gnollbench', subtab: 'suites' }), pm());
      expect(component.pendingBenchmarkNavigation).toEqual({ subTab: 'suites', suiteId: null });
      component.pendingBenchmarkNavigation = null;

      component.applyAddress(pm({ tab: 'gnollbench', subtab: 'suites' }), pm({ sessionId: 's1' }));
      expect(component.pendingBenchmarkNavigation).toBeNull();

      component.benchmarkPanel = {} as AdminBenchmarkComponent;
      component.applyAddress(pm({ tab: 'users' }), pm());
      component.benchmarkPanel = undefined;
      component.applyAddress(pm({ tab: 'gnollbench', subtab: 'suites' }), pm());

      expect(component.pendingBenchmarkNavigation).toEqual({ subTab: 'suites', suiteId: null });
    });

    it('hands the System Configs tab the configuration to resolve and removes the parameter', () => {
      component.applyAddress(pm({ tab: 'configs' }), pm({ resolveConfig: '7' }));

      expect(component.activeTab).toBe('configs');
      expect(component.store.pendingResolveConfigId).toBe(7);
      expect(navigateSpy).toHaveBeenCalledTimes(1);
      expect(navigateSpy).toHaveBeenCalledWith([], expect.objectContaining(removal));
    });

    it('goes to System Configs for a resolveConfig link on another tab, keeping the parameter', () => {
      component.applyAddress(pm({ tab: 'users' }), pm({ resolveConfig: '12' }));

      expect(component.store.pendingResolveConfigId).toBeNull();
      expect(navigateSpy).toHaveBeenCalledTimes(1);
      expect(navigateSpy).toHaveBeenCalledWith(['/admin', 'configs'], { queryParamsHandling: 'preserve', replaceUrl: true });
    });

    for (const raw of ['0', '-3', 'abc', '2.5', '']) {
      it(`ignores resolveConfig="${raw}" and still removes it`, () => {
        component.applyAddress(pm({ tab: 'users' }), pm({ resolveConfig: raw }));

        expect(component.activeTab).toBe('users');
        expect(component.store.pendingResolveConfigId).toBeNull();
        expect(navigateSpy).toHaveBeenCalledTimes(1);
        expect(navigateSpy).toHaveBeenCalledWith([], expect.objectContaining(removal));
      });
    }

    it('follows a GnollBench sub-tab change: a new entry when the user chose it, else a replaced one', () => {
      component.onBenchmarkSubTabChange({ tab: 'history', byUser: true });
      expect(navigateSpy).toHaveBeenLastCalledWith(['/admin', 'gnollbench', 'history'], { queryParamsHandling: 'preserve', replaceUrl: false });

      component.onBenchmarkSubTabChange({ tab: 'suites', byUser: false });
      expect(navigateSpy).toHaveBeenLastCalledWith(['/admin', 'gnollbench', 'suites'], { queryParamsHandling: 'preserve', replaceUrl: true });
      expect(component.shownBenchmarkSubTab).toBe('suites');
      expect(component.store.benchmarkSubTab).toBe('suites');

      // The address the change produced hands GnollBench nothing back.
      component.applyAddress(pm({ tab: 'gnollbench', subtab: 'suites' }), pm());
      expect(component.pendingBenchmarkNavigation).toBeNull();
    });

    it('does not navigate when GnollBench carries out the sub-tab the address handed it', () => {
      component.applyAddress(pm({ tab: 'gnollbench', subtab: 'suites' }), pm({ suiteId: '7' }));
      navigateSpy.mockClear();

      component.onBenchmarkSubTabChange({ tab: 'suites', byUser: false });

      expect(navigateSpy).not.toHaveBeenCalled();
      expect(component.shownBenchmarkSubTab).toBe('suites');
    });
  });

  // ---------------------------------------------------------------------------
  // Through the router, with the real admin route.
  // ---------------------------------------------------------------------------
  describe('paths', () => {
    let harness: RouterTestingHarness;
    let router: Router;

    const host = () => harness.routeNativeElement as HTMLElement;
    const settle = async () => {
      await harness.fixture.whenStable();
      harness.detectChanges();
    };

    beforeEach(async () => {
      harness = await RouterTestingHarness.create();
      router = TestBed.inject(Router);
    });

    it('shows Users → Groups for /admin/users/groups', async () => {
      const page = await harness.navigateByUrl('/admin/users/groups', AdminComponent);
      await settle();

      expect(page.activeTab).toBe('users');
      expect(page.store.usersSubTab).toBe('groups');
      expect(host().querySelector('#users-subtab-groups')?.getAttribute('aria-selected')).toBe('true');
      expect(host().querySelector('#users-subpanel-groups app-admin-groups-tab')).toBeTruthy();
      expect(router.url).toBe('/admin/users/groups');
    });

    for (const url of ['/admin', '/admin/bogus', '/admin/users/users']) {
      it(`replaces ${url} with /admin/users`, async () => {
        await harness.navigateByUrl(url, AdminComponent);
        await settle();

        expect(router.url).toBe('/admin/users');
      });
    }

    it('keeps the page, its store and its data across tab changes', async () => {
      const loadInitial = vi.spyOn(AdminPageStore.prototype, 'loadInitial');
      const first = await harness.navigateByUrl('/admin/users', AdminComponent);
      const store = first.store;

      const second = await harness.navigateByUrl('/admin/database', AdminComponent);
      await settle();

      expect(second).toBe(first);
      expect(second.store).toBe(store);
      expect(second.activeTab).toBe('database');
      expect(loadInitial).toHaveBeenCalledTimes(1);
    });

    it('adds a history entry for a click and replaces it for an arrow key, keeping an unrelated query parameter', async () => {
      await harness.navigateByUrl('/admin/users?sessionId=s1', AdminComponent);
      await settle();
      const navigate = vi.spyOn(router, 'navigate');

      (host().querySelector('#admin-tab-database') as HTMLButtonElement).click();
      await settle();

      expect(navigate).toHaveBeenLastCalledWith(['/admin', 'database'], { queryParamsHandling: 'preserve', replaceUrl: false });
      expect(router.url).toBe('/admin/database?sessionId=s1');
      expect(host().querySelector('#admin-panel-database')).toBeTruthy();

      const database = host().querySelector('#admin-tab-database') as HTMLButtonElement;
      database.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true, cancelable: true }));
      await settle();

      expect(navigate).toHaveBeenLastCalledWith(['/admin', 'configs'], { queryParamsHandling: 'preserve', replaceUrl: true });
      expect(router.url).toBe('/admin/configs?sessionId=s1');
      expect(document.activeElement).toBe(host().querySelector('#admin-tab-configs'));
    });

    it('follows Back to the tab and sub-tab it names', async () => {
      // No bootstrap runs the initial navigation, which is what subscribes the router to Back and Forward.
      router.setUpLocationChangeListener();
      const page = await harness.navigateByUrl('/admin/users/log', AdminComponent);
      await harness.navigateByUrl('/admin/database', AdminComponent);
      await settle();
      expect(page.activeTab).toBe('database');

      TestBed.inject(Location).back();
      await vi.waitFor(() => expect(router.url).toBe('/admin/users/log'));
      await settle();

      expect(page.activeTab).toBe('users');
      expect(page.store.usersSubTab).toBe('log');
      expect(host().querySelector('#users-subpanel-log')).toBeTruthy();
    });

    it('replaces an old ?tab=groups link with /admin/users/groups', async () => {
      await harness.navigateByUrl('/admin?tab=groups&sessionId=s1', AdminComponent);
      await settle();

      expect(router.url).toBe('/admin/users/groups?sessionId=s1');
      expect(host().querySelector('#users-subpanel-groups')).toBeTruthy();
    });

    it('replaces an old ?tab=users&subtab=deletions link with /admin/users/deletions', async () => {
      const page = await harness.navigateByUrl('/admin?tab=users&subtab=deletions', AdminComponent);
      await settle();

      expect(router.url).toBe('/admin/users/deletions');
      expect(page.store.usersSubTab).toBe('deletions');
    });

    it('opens the API Keys dialog of System Configs for an old ?tab=apikeys link', async () => {
      const page = await harness.navigateByUrl('/admin?tab=apikeys', AdminComponent);
      await settle();

      expect(router.url).toBe('/admin/configs/api-keys');
      expect(page.activeTab).toBe('configs');
      const dialog = host().querySelector('dialog.api-keys-dialog') as HTMLDialogElement;
      expect(dialog.open).toBe(true);
      expect(dialog.querySelector('app-admin-api-keys')).toBeTruthy();
    });

    it('opens the resolution dialog for an old resolveConfig link once the configurations have loaded, and removes it', async () => {
      (adminService.getSystemConfigs as Mock).mockReturnValue(of([buildSystemConfig({
        id: 7, displayName: 'Old Claude', modelId: 'claude-old',
        modelAvailability: { status: 'retired', needsAttention: true, retiredOn: '2026-09-30' }
      })]));
      const open = vi.spyOn(ModelResolutionDialogComponent.prototype, 'open').mockReturnValue(undefined);

      const page = await harness.navigateByUrl('/admin?tab=configs&resolveConfig=7', AdminComponent);
      await settle();

      expect(router.url).toBe('/admin/configs');
      expect(page.activeTab).toBe('configs');
      expect(open).toHaveBeenCalledTimes(1);
      expect(open.mock.lastCall![0]).toEqual(expect.objectContaining({ id: 7, displayName: 'Old Claude', modelId: 'claude-old' }));
      expect(page.store.pendingResolveConfigId).toBeNull();
    });
  });
});
