import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';
import { AdminComponent } from './admin.component';
import { AdminService, UsersResponse, GroupDto, SystemAiConfigDto } from '../services/admin.service';

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
    spyOn(adminService, 'getUsers').and.returnValue(of({ rows: [], totalCount: 0 }));
    spyOn(adminService, 'getGroups').and.returnValue(of([]));
    spyOn(adminService, 'getSystemConfigs').and.returnValue(of([]));

    fixture = TestBed.createComponent(AdminComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('loadUsers', () => {
    it('should populate users and totalCount on normal success', () => {
      const mockResponse: UsersResponse = {
        rows: [
          { id: '1', userName: 'admin', email: 'admin@test.com', groups: [] }
        ],
        totalCount: 1
      };
      (adminService.getUsers as jasmine.Spy).and.returnValue(of(mockResponse));

      component.loadUsers();

      expect(component.users.length).toBe(1);
      expect(component.totalCount).toBe(1);
      expect(component.usersLoading).toBeFalse();
    });

    it('should catch TypeError: Failed to fetch on getUsers and reset usersLoading to false', () => {
      (adminService.getUsers as jasmine.Spy).and.returnValue(
        throwError(() => new TypeError('Failed to fetch'))
      );

      expect(() => {
        component.loadUsers();
      }).not.toThrow();

      expect(component.usersLoading).toBeFalse();
    });
  });

  describe('loadData', () => {
    it('should populate users, groups, and configs on normal success', () => {
      const mockUsers: UsersResponse = {
        rows: [{ id: '1', userName: 'admin', email: 'admin@test.com', groups: [] }],
        totalCount: 1
      };
      const mockGroups: GroupDto[] = [{ id: 1, displayName: 'Admins' }];
      const mockConfigs: SystemAiConfigDto[] = [{
        id: 1,
        displayName: 'System GPT-4o',
        provider: 'openai',
        modelId: 'gpt-4o',
        thinkingLevel: null,
        reasoningMode: null,
        reasoningSummary: null,
        serviceTier: null,
        maxInputTokens: null,
        maxOutputTokens: null,
        orderIndex: 0,
        isEnabled: true,
        hasApiKey: true,
        isSystemWide: true,
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
        parallelExecutionMode: 2
      }];

      (adminService.getUsers as jasmine.Spy).and.returnValue(of(mockUsers));
      (adminService.getGroups as jasmine.Spy).and.returnValue(of(mockGroups));
      (adminService.getSystemConfigs as jasmine.Spy).and.returnValue(of(mockConfigs));

      component.loadData();

      expect(component.users.length).toBe(1);
      expect(component.groups.length).toBe(1);
      expect(component.configs.length).toBe(1);
      expect(component.loading).toBeFalse();
    });

    it('should catch TypeError: Failed to fetch on getUsers and reset loading to false', () => {
      (adminService.getUsers as jasmine.Spy).and.returnValue(
        throwError(() => new TypeError('Failed to fetch'))
      );

      expect(() => {
        component.loadData();
      }).not.toThrow();

      expect(component.loading).toBeFalse();
    });

    it('should catch TypeError: Failed to fetch on nested getGroups and reset loading to false', () => {
      (adminService.getUsers as jasmine.Spy).and.returnValue(of({ rows: [], totalCount: 0 }));
      (adminService.getGroups as jasmine.Spy).and.returnValue(
        throwError(() => new TypeError('Failed to fetch'))
      );

      expect(() => {
        component.loadData();
      }).not.toThrow();

      expect(component.loading).toBeFalse();
    });

    it('should catch TypeError: Failed to fetch on nested getSystemConfigs and reset loading to false', () => {
      (adminService.getUsers as jasmine.Spy).and.returnValue(of({ rows: [], totalCount: 0 }));
      (adminService.getGroups as jasmine.Spy).and.returnValue(of([]));
      (adminService.getSystemConfigs as jasmine.Spy).and.returnValue(
        throwError(() => new TypeError('Failed to fetch'))
      );

      expect(() => {
        component.loadData();
      }).not.toThrow();

      expect(component.loading).toBeFalse();
    });
  });

  // ---------------------------------------------------------------------------
  // Tab semantics and keyboard navigation for the admin dashboard's main tab
  // row, and for the nested tab row inside the rate limits dialog.
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
      expect(mainTabs().every(t => t.getAttribute('type') === 'button')).toBeTrue();
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

  describe('rate limits dialog tab row', () => {
    it('should wrap around and move selection with the arrow keys', () => {
      component.activeRateLimitTab = 'chat';

      component.onRateLimitTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowLeft' }), 0);
      expect(component.activeRateLimitTab).toBe('title');

      component.onRateLimitTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }), 1);
      expect(component.activeRateLimitTab).toBe('chat');
    });

    it('should select the ends with Home and End', () => {
      component.onRateLimitTabKeydown(new KeyboardEvent('keydown', { key: 'End' }), 0);
      expect(component.activeRateLimitTab).toBe('title');

      component.onRateLimitTabKeydown(new KeyboardEvent('keydown', { key: 'Home' }), 1);
      expect(component.activeRateLimitTab).toBe('chat');
    });

    it('should ignore unrelated keys', () => {
      component.activeRateLimitTab = 'title';
      component.onRateLimitTabKeydown(new KeyboardEvent('keydown', { key: 'Enter' }), 1);
      expect(component.activeRateLimitTab).toBe('title');
    });
  });
});
