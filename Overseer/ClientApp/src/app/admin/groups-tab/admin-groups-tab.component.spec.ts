import type { Mock } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminGroupsTabComponent } from './admin-groups-tab.component';
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
