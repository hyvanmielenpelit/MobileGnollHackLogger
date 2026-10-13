import { ComponentFixture, TestBed } from '@angular/core/testing';

import { buildAdminUser } from '../admin-users.testing';
import { BulkUserPreset, BulkUsersStepComponent, bulkPresetCriteria, bulkPresetOf } from './bulk-users-step.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

describe('bulk user presets', () => {
  it('maps each preset to its criteria and back', () => {
    expect(bulkPresetCriteria('unconfirmed-old'))
      .toEqual({ emailConfirmed: 'no', registeredOlderThanDays: 30, hasGameData: 'no', hasOverseerData: 'no' });
    expect(bulkPresetCriteria('locked-out')).toEqual({ statuses: ['LockedOut'] });
    expect(bulkPresetCriteria('custom')).toEqual({});

    expect(bulkPresetOf({ hasOverseerData: 'no', emailConfirmed: 'no', hasGameData: 'no', registeredOlderThanDays: 30 })).toBe('unconfirmed-old');
    expect(bulkPresetOf({ statuses: ['Banned'] })).toBe('banned');
    expect(bulkPresetOf({ statuses: ['Banned'], search: 'bob' })).toBe('custom');
    expect(bulkPresetOf({})).toBe('custom');
  });

  it('hands out a copy, never the preset itself', () => {
    const criteria = bulkPresetCriteria('banned');
    criteria.statuses!.push('LockedOut');
    expect(bulkPresetCriteria('banned')).toEqual({ statuses: ['Banned'] });
  });
});

describe('BulkUsersStepComponent', () => {
  let fixture: ComponentFixture<BulkUsersStepComponent>;
  let component: BulkUsersStepComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [BulkUsersStepComponent] }).compileComponents();
    fixture = TestBed.createComponent(BulkUsersStepComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => fixture.destroy());

  it('offers the presets, with the current one chosen', () => {
    fixture.componentRef.setInput('preset', 'locked-out');
    fixture.detectChanges();

    const radios = Array.from(el.querySelectorAll<HTMLInputElement>('.bw-presets input[type="radio"]'));
    expect(radios.map(r => textOf(r.closest('label'))))
      .toEqual(['Unconfirmed accounts older than 30 days', 'Locked out', 'Banned', 'Custom']);
    expect(radios.filter(r => r.checked).map(r => r.id)).toEqual(['bw-preset-locked-out']);

    const chosen: BulkUserPreset[] = [];
    component.presetChange.subscribe(p => chosen.push(p));
    el.querySelector<HTMLInputElement>('#bw-preset-banned')!.click();
    expect(chosen).toEqual(['banned']);
  });

  it('renders the shared criteria bar without Sort by', () => {
    fixture.detectChanges();

    expect(el.querySelector('app-user-criteria-bar #bw-criteria-search')).toBeTruthy();
    expect(el.querySelector('#bw-criteria-sort')).toBeNull();
  });

  it('counts the matches in its live region and lists the first of them', () => {
    fixture.componentRef.setInput('matchCount', 428);
    fixture.componentRef.setInput('matches', [
      buildAdminUser({ id: 'u1', userName: 'ann', email: 'ann@example.com' }),
      buildAdminUser({ id: 'u2', userName: 'bob', email: 'bob@example.com', statuses: ['EmailUnconfirmed'] })
    ]);
    fixture.detectChanges();

    const status = el.querySelector('#bw-match-status')!;
    expect(status.getAttribute('role')).toBe('status');
    expect(textOf(status)).toBe('428 users match');
    const rows = Array.from(el.querySelectorAll('.bw-match-row'));
    expect(rows.map(r => textOf(r.querySelector('.bw-match-name')))).toEqual(['ann', 'bob']);
    expect(textOf(rows[1].querySelector('.bw-match-email'))).toBe('bob@example.com');
    expect(rows[1].querySelector('app-user-status-badges [data-status="EmailUnconfirmed"]')).toBeTruthy();
    expect(textOf(el.querySelector('.bw-match-note'))).toBe('The first 2, oldest registration first. The exact list is fixed in step 3.');
  });

  it('says when nothing matches, or one account does', () => {
    fixture.componentRef.setInput('matchCount', 0);
    fixture.detectChanges();
    expect(textOf(el.querySelector('#bw-match-status'))).toBe('No users match these criteria.');
    expect(el.querySelector('.bw-match-list')).toBeNull();

    fixture.componentRef.setInput('matchCount', 1);
    fixture.componentRef.setInput('matches', [buildAdminUser()]);
    fixture.detectChanges();
    expect(textOf(el.querySelector('#bw-match-status'))).toBe('1 user matches');
    expect(textOf(el.querySelector('.bw-match-note'))).toBe('The exact list is fixed in step 3.');
  });
});
