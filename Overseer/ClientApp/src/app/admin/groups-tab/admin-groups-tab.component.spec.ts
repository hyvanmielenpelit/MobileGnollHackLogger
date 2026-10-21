import type { Mock } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { AdminGroupsTabComponent } from './admin-groups-tab.component';
import { AdminService } from '../../services/admin.service';
import { AdminPageStore } from '../admin-page.store';
import { configureAdminTestBed } from '../admin.component.testing';

describe('AdminGroupsTabComponent group name keys', () => {
  let component: AdminGroupsTabComponent;
  let fixture: ComponentFixture<AdminGroupsTabComponent>;
  let saveSpy: Mock;

  beforeEach(async () => {
    await configureAdminTestBed(AdminGroupsTabComponent);

    fixture = TestBed.createComponent(AdminGroupsTabComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    component.openCreateGroup();
    saveSpy = vi.spyOn(component, 'saveCreateGroup').mockImplementation(() => {});
  });

  afterEach(() => {
    (fixture.nativeElement as HTMLElement).querySelectorAll('dialog').forEach(d => { if (d.open) d.close(); });
  });

  /** Dispatches a key event of `type` on the group name input. */
  function press(type: 'keydown' | 'keyup', init: KeyboardEventInit): void {
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('#newGroupName');
    expect(input).not.toBeNull();
    input!.dispatchEvent(new KeyboardEvent(type, { bubbles: true, cancelable: true, ...init }));
  }

  it('creates the group on Enter', () => {
    press('keydown', { key: 'Enter' });

    expect(saveSpy).toHaveBeenCalledTimes(1);
  });

  it('does not create while an IME composition is active', () => {
    press('keydown', { key: 'Enter', isComposing: true });

    expect(saveSpy).not.toHaveBeenCalled();
  });

  it("does not create on Safari's keyCode 229 confirming Enter", () => {
    press('keydown', { key: 'Enter', keyCode: 229 });

    expect(saveSpy).not.toHaveBeenCalled();
  });

  it('does not create on the keyup of Enter', () => {
    press('keyup', { key: 'Enter' });

    expect(saveSpy).not.toHaveBeenCalled();
  });
});

describe('AdminGroupsTabComponent manage request', () => {
  let fixture: ComponentFixture<AdminGroupsTabComponent>;
  let adminService: AdminService;
  let store: AdminPageStore;
  let configsSpy: Mock;

  beforeEach(async () => {
    ({ adminService, store } = await configureAdminTestBed(AdminGroupsTabComponent));
    configsSpy = vi.spyOn(adminService, 'getGroupSystemConfigs').mockReturnValue(of([]));
    fixture = TestBed.createComponent(AdminGroupsTabComponent);
  });

  afterEach(() => {
    (fixture.nativeElement as HTMLElement).querySelectorAll('dialog').forEach(d => { if (d.open) d.close(); });
    fixture.destroy();
  });

  const dialog = () => (fixture.nativeElement as HTMLElement).querySelector<HTMLDialogElement>('app-config-access-dialog dialog')!;

  it('opens the AI configuration access dialog for a group requested before it was created, and clears the request', () => {
    store.pendingManageGroup = { groupId: 5, groupName: 'Testers' };

    fixture.detectChanges();

    expect(store.pendingManageGroup).toBeNull();
    expect(configsSpy).toHaveBeenCalledWith(5);
    expect(dialog().open).toBe(true);
    expect(dialog().querySelector('h3')?.textContent).toContain('AI configuration access for group Testers');
  });

  it('opens nothing without a request', () => {
    fixture.detectChanges();

    expect(configsSpy).not.toHaveBeenCalled();
    expect(dialog().open).toBe(false);
  });

  it('opens the dialog for a request made while it is shown, and selects Users → Groups', () => {
    fixture.detectChanges();

    store.requestManageGroup(7, 'Moderators');
    fixture.detectChanges();

    expect(store.pendingManageGroup).toBeNull();
    expect(store.activeTab).toBe('users');
    expect(store.usersSubTab).toBe('groups');
    expect(configsSpy).toHaveBeenCalledWith(7);
    expect(dialog().open).toBe(true);
    expect(dialog().querySelector('h3')?.textContent).toContain('AI configuration access for group Moderators');
  });

  it('stops listening for requests once destroyed', () => {
    fixture.detectChanges();
    fixture.destroy();

    store.requestManageGroup(7, 'Moderators');

    expect(configsSpy).not.toHaveBeenCalled();
    expect(store.pendingManageGroup).toEqual({ groupId: 7, groupName: 'Moderators' });
  });
});
