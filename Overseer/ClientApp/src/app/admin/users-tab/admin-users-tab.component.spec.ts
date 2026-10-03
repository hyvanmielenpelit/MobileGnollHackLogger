import type { Mock } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AdminUsersTabComponent } from './admin-users-tab.component';
import { AdminService, UsersResponse } from '../../services/admin.service';
import { configureAdminTestBed } from '../admin.component.testing';

describe('AdminUsersTabComponent', () => {
  let component: AdminUsersTabComponent;
  let fixture: ComponentFixture<AdminUsersTabComponent>;
  let adminService: AdminService;

  beforeEach(async () => {
    ({ adminService } = await configureAdminTestBed(AdminUsersTabComponent));

    fixture = TestBed.createComponent(AdminUsersTabComponent);
    component = fixture.componentInstance;
  });

  describe('loadUsers', () => {
    it('should populate users and totalCount on normal success', () => {
      const mockResponse: UsersResponse = {
        rows: [
          { id: '1', userName: 'admin', email: 'admin@test.com', groups: [] }
        ],
        totalCount: 1
      };
      (adminService.getUsers as Mock).mockReturnValue(of(mockResponse));

      component.loadUsers();

      expect(component.users.length).toBe(1);
      expect(component.totalCount).toBe(1);
      expect(component.usersLoading).toBe(false);
    });

    it('should catch TypeError: Failed to fetch on getUsers and reset usersLoading to false', () => {
      (adminService.getUsers as Mock).mockReturnValue(throwError(() => new TypeError('Failed to fetch')));

      expect(() => {
        component.loadUsers();
      }).not.toThrow();

      expect(component.usersLoading).toBe(false);
    });
  });
});
