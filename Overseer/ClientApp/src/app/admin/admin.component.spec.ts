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
        modelRole: 1
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
});
