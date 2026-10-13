import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AdminBulkAction } from '../../../services/admin-users.service';
import {
  BulkActionOptions,
  BulkActionStepComponent,
  DEFAULT_BULK_OPTIONS,
  bulkActionMissing,
  bulkLockoutUntil,
  bulkPreviewOptions,
  bulkRunLabel
} from './bulk-action-step.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

const NOW = new Date('2026-10-13T12:00:00Z');

describe('bulk action rules', () => {
  const options = (overrides: Partial<BulkActionOptions> = {}): BulkActionOptions => ({ ...DEFAULT_BULK_OPTIONS, ...overrides });

  it('labels the run button with the verb and the count', () => {
    expect(bulkRunLabel('Delete', 37)).toBe('Delete 37 Accounts');
    expect(bulkRunLabel('LockOut', 12)).toBe('Lock Out 12 Accounts');
    expect(bulkRunLabel('Unlock', 1)).toBe('Unlock 1 Account');
    expect(bulkRunLabel('SignOutEverywhere', 5)).toBe('Sign Out 5 Accounts');
  });

  it('names what step 2 still needs', () => {
    expect(bulkActionMissing(null, options(), NOW, 365)).toEqual(['an action']);
    expect(bulkActionMissing('Delete', options(), NOW, 365)).toEqual(['a reason']);
    expect(bulkActionMissing('Ban', options({ reason: '  ' }), NOW, 365)).toEqual(['a reason']);
    expect(bulkActionMissing('LockOut', options({ duration: 'date', dateValue: '2028-01-01' }), NOW, 365)).toEqual(['a valid date', 'a reason']);
    expect(bulkActionMissing('LockOut', options({ reason: 'Spam' }), NOW, 365)).toEqual([]);
    expect(bulkActionMissing('Unlock', options(), NOW, 365)).toEqual([]);
  });

  it('ends a lockout after the chosen span, or at the start of the chosen day', () => {
    expect(bulkLockoutUntil(options({ duration: '24h' }), NOW, 365)?.toISOString()).toBe('2026-10-14T12:00:00.000Z');
    expect(bulkLockoutUntil(options({ duration: 'date', dateValue: '2026-10-20' }), NOW, 365)?.toISOString()).toBe('2026-10-20T00:00:00.000Z');
    expect(bulkLockoutUntil(options({ duration: 'date', dateValue: '2026-10-13' }), NOW, 365)).toBeNull();
  });

  it('sends the options each action takes', () => {
    expect(bulkPreviewOptions('LockOut', options({ duration: '1h', reason: ' Spam ' }), NOW, 365))
      .toEqual({ until: '2026-10-13T13:00:00.000Z', reason: 'Spam' });
    expect(bulkPreviewOptions('Delete', options({ reason: 'Old' }), NOW, 365)).toEqual({ reason: 'Old' });
    expect(bulkPreviewOptions('Unlock', options({ reason: 'ignored' }), NOW, 365)).toEqual({});
  });
});

describe('BulkActionStepComponent', () => {
  let fixture: ComponentFixture<BulkActionStepComponent>;
  let component: BulkActionStepComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [BulkActionStepComponent] }).compileComponents();
    fixture = TestBed.createComponent(BulkActionStepComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    component.now = () => NOW;
    fixture.componentRef.setInput('matchCount', 428);
    fixture.detectChanges();
  });

  afterEach(() => fixture.destroy());

  function choose(action: AdminBulkAction | null): void {
    fixture.componentRef.setInput('action', action);
    fixture.detectChanges();
  }

  it('offers six actions, each with a danger tag of a glyph and a word', () => {
    expect(textOf(el.querySelector('.bw-action-legend'))).toBe('What happens to the 428 matching accounts');
    const items = Array.from(el.querySelectorAll('.bw-action-item'));
    expect(items.map(i => textOf(i.querySelector('.bw-action-title'))))
      .toEqual(['Delete accounts', 'Lock out', 'Unlock', 'Ban', 'Unban', 'Sign out everywhere']);
    const tags = items.map(i => i.querySelector('.bw-danger-tag')!);
    expect(tags.map(t => textOf(t))).toEqual(['Danger — permanent', 'Caution', 'Safe', 'Caution', 'Safe', 'Safe']);
    expect(tags.map(t => t.getAttribute('data-tone'))).toEqual(['danger', 'warning', 'ok', 'warning', 'ok', 'ok']);
    expect(tags.every(t => t.querySelector('svg')?.getAttribute('aria-hidden') === 'true')).toBe(true);
    expect(el.querySelector('#bw-action-Delete')?.getAttribute('aria-describedby')).toBe('bw-action-Delete-line');
  });

  it('emits the chosen action', () => {
    const chosen: AdminBulkAction[] = [];
    component.actionChange.subscribe(a => chosen.push(a));

    el.querySelector<HTMLInputElement>('#bw-action-Ban')!.click();

    expect(chosen).toEqual(['Ban']);
  });

  it('shows the options under the chosen card only', () => {
    choose('Unlock');
    expect(el.querySelector('.bw-action-fields')).toBeNull();

    choose('LockOut');
    const fields = el.querySelector('.bw-action-item.is-chosen .bw-action-fields')!;
    expect(Array.from(fields.querySelectorAll('.bw-durations input')).map(i => i.id))
      .toEqual(['bw-lockout-for-1h', 'bw-lockout-for-24h', 'bw-lockout-for-7d', 'bw-lockout-for-30d', 'bw-lockout-for-date']);
    expect(fields.querySelector('#bw-action-reason')).toBeTruthy();

    choose('Delete');
    expect(el.querySelector('.bw-durations')).toBeNull();
    expect(el.querySelector('.bw-action-item.is-chosen #bw-action-reason')).toBeTruthy();
  });

  it('emits the typed reason with the other options', () => {
    const emitted: BulkActionOptions[] = [];
    component.optionsChange.subscribe(o => emitted.push(o));
    choose('Ban');

    const reason = el.querySelector<HTMLTextAreaElement>('#bw-action-reason')!;
    reason.value = 'Abuse';
    reason.dispatchEvent(new Event('input', { bubbles: true }));

    expect(emitted).toEqual([{ ...DEFAULT_BULK_OPTIONS, reason: 'Abuse' }]);
  });

  it('says what it checks while the preview runs', () => {
    fixture.componentRef.setInput('busy', true);
    fixture.detectChanges();

    expect(textOf(el.querySelector('.bw-checking[role="status"]'))).toBe('Checking 428 accounts…');
  });

  it('offers Back to Users when a delete needs a narrowing criterion', () => {
    const back = vi.fn();
    component.backToUsers.subscribe(back);
    fixture.componentRef.setInput('error', 'Bulk deletion needs at least one of: Email not confirmed, Last sign-in, Registered more than 30 days ago.');
    fixture.componentRef.setInput('errorCode', 'narrowing');
    fixture.detectChanges();

    expect(textOf(el.querySelector('#bw-action-error'))).toContain('Bulk deletion needs at least one of');
    el.querySelector<HTMLButtonElement>('.bw-back-to-users')!.click();
    expect(back).toHaveBeenCalledTimes(1);

    fixture.componentRef.setInput('errorCode', 'reason');
    fixture.detectChanges();
    expect(el.querySelector('.bw-back-to-users')).toBeNull();
  });
});
