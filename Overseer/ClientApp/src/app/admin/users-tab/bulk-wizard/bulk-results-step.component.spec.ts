import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import { AdminBulkItem, AdminBulkOperation, AdminUsersService } from '../../../services/admin-users.service';
import { configureAdminTestBed } from '../../admin.component.testing';
import { BULK_POLL_MS, BulkResultsStepComponent } from './bulk-results-step.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

function buildItem(id: number, userName: string, overrides: Partial<AdminBulkItem> = {}): AdminBulkItem {
  return {
    id,
    userId: `u${id}`,
    userName,
    isAdvanced: false,
    excluded: false,
    outcome: 'Changed',
    message: null,
    beforeStatus: 'Email not confirmed',
    afterStatus: 'Deleted',
    processedUtc: '2026-10-13T15:40:00Z',
    ...overrides
  };
}

function buildOperation(overrides: Partial<AdminBulkOperation> = {}): AdminBulkOperation {
  return {
    id: 12,
    action: 'Delete',
    status: 'Running',
    actorUserName: 'tommi',
    dangerLevel: 'danger',
    reason: 'Spam',
    until: null,
    stopMessage: null,
    createdUtc: '2026-10-13T15:30:00Z',
    expiresUtc: '2026-10-13T15:45:00Z',
    startedUtc: '2026-10-13T15:35:00Z',
    completedUtc: null,
    matchedCount: 3,
    eligibleCount: 3,
    refusedCount: 0,
    changedCount: 0,
    skippedCount: 0,
    failedCount: 0,
    pendingCount: 3,
    excludedCount: 0,
    includedAdvanced: false,
    refused: [],
    noOpCount: 0,
    truncatedCount: 0,
    itemsTotalCount: 0,
    items: [],
    ...overrides
  };
}

/** The operation done: two deleted and one skipped, with every item. */
function completed(): AdminBulkOperation {
  const items = [
    buildItem(1, 'ann'),
    buildItem(2, 'bob'),
    buildItem(3, 'cid', { outcome: 'Skipped', message: 'Changed since the preview: signed in.', afterStatus: null, beforeStatus: null })
  ];
  return buildOperation({
    status: 'Completed', changedCount: 2, skippedCount: 1, pendingCount: 0,
    completedUtc: '2026-10-13T15:41:00Z', items, itemsTotalCount: items.length
  });
}

describe('BulkResultsStepComponent', () => {
  let fixture: ComponentFixture<BulkResultsStepComponent> | null;
  let component: BulkResultsStepComponent;
  let el: HTMLElement;
  let usersService: AdminUsersService;

  beforeEach(async () => {
    ({ usersService } = await configureAdminTestBed(BulkResultsStepComponent));
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    fixture = null;
  });

  afterEach(() => {
    fixture?.destroy();
    vi.useRealTimers();
  });

  function create(initial: AdminBulkOperation | null): void {
    fixture = TestBed.createComponent(BulkResultsStepComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('operationId', 12);
    fixture.componentRef.setInput('initial', initial);
    fixture.detectChanges();
  }

  const click = (selector: string): void => {
    el.querySelector<HTMLButtonElement>(selector)!.click();
    fixture!.detectChanges();
  };

  it('polls every 1.5 s while the operation runs and stops once it has finished', () => {
    const get = vi.spyOn(usersService, 'bulkGet')
      .mockReturnValueOnce(of(buildOperation({ changedCount: 1, pendingCount: 2 })))
      .mockReturnValue(of(completed()));
    const seen: AdminBulkOperation[] = [];
    create(buildOperation({ status: 'Queued' }));
    component.operationChange.subscribe(op => seen.push(op));

    expect(textOf(el.querySelector('#bw-res-status'))).toBe('Deleting 0 of 3…');
    expect(el.querySelector('#bw-res-status')?.getAttribute('role')).toBe('status');
    vi.advanceTimersByTime(BULK_POLL_MS - 1);
    expect(get).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(get).toHaveBeenCalledTimes(1);
    expect(get).toHaveBeenLastCalledWith(12, { take: 1 });

    vi.advanceTimersByTime(BULK_POLL_MS);
    // The finishing poll, then the read of every item.
    expect(get).toHaveBeenCalledTimes(3);
    expect(get).toHaveBeenLastCalledWith(12, { skip: 0, take: 200 });
    expect(seen.map(op => op.status)).toEqual(['Running', 'Completed']);

    vi.advanceTimersByTime(10 * BULK_POLL_MS);
    expect(get).toHaveBeenCalledTimes(3);

    fixture!.detectChanges();
    expect(textOf(el.querySelector('#bw-res-status'))).toBe('Deleted 2 · Skipped 1 · Failed 0');
    expect(el.querySelector('.bw-stop')).toBeNull();
  });

  it('changes the status line at most every 2 s while running', () => {
    vi.spyOn(usersService, 'bulkGet').mockReturnValueOnce(of(buildOperation({ changedCount: 1, pendingCount: 2 })))
      .mockReturnValue(of(buildOperation({ changedCount: 2, pendingCount: 1 })));
    create(buildOperation());

    vi.advanceTimersByTime(BULK_POLL_MS);
    fixture!.detectChanges();
    expect(textOf(el.querySelector('#bw-res-status'))).toBe('Deleting 0 of 3…');

    vi.advanceTimersByTime(2000 - BULK_POLL_MS);
    fixture!.detectChanges();
    expect(textOf(el.querySelector('#bw-res-status'))).toBe('Deleting 1 of 3…');
  });

  it('backs off after a failed poll and says so', () => {
    const get = vi.spyOn(usersService, 'bulkGet').mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 500, error: { message: 'Server unavailable', code: null }
    })));
    create(buildOperation());

    vi.advanceTimersByTime(BULK_POLL_MS);
    expect(get).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(2 * BULK_POLL_MS - 1);
    expect(get).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(1);
    expect(get).toHaveBeenCalledTimes(2);
    vi.advanceTimersByTime(4 * BULK_POLL_MS - 1);
    expect(get).toHaveBeenCalledTimes(2);
    vi.advanceTimersByTime(1);
    expect(get).toHaveBeenCalledTimes(3);

    fixture!.detectChanges();
    expect(textOf(el.querySelector('.bw-res-error'))).toContain('Server unavailable');
  });

  it('stops polling when destroyed', () => {
    const get = vi.spyOn(usersService, 'bulkGet').mockReturnValue(of(buildOperation({ changedCount: 1, pendingCount: 2 })));
    create(buildOperation());

    vi.advanceTimersByTime(BULK_POLL_MS);
    expect(get).toHaveBeenCalledTimes(1);
    fixture!.destroy();
    fixture = null;
    vi.advanceTimersByTime(10 * BULK_POLL_MS);
    expect(get).toHaveBeenCalledTimes(1);
  });

  it('reads the operation at once when opened without one', () => {
    const get = vi.spyOn(usersService, 'bulkGet').mockReturnValue(of(completed()));
    create(null);

    expect(textOf(el.querySelector('.bw-res-loading'))).toBe('Loading bulk operation #12…');
    vi.advanceTimersByTime(0);
    expect(get).toHaveBeenCalledWith(12, { take: 1 });
    fixture!.detectChanges();
    expect(textOf(el.querySelector('#bw-res-status'))).toBe('Deleted 2 · Skipped 1 · Failed 0');
  });

  it('stops after the current account, once', () => {
    vi.spyOn(usersService, 'bulkGet').mockReturnValue(of(buildOperation({ status: 'CancelRequested', changedCount: 1, pendingCount: 2 })));
    const cancel = vi.spyOn(usersService, 'bulkCancel').mockReturnValue(of(buildOperation({ status: 'CancelRequested', changedCount: 1, pendingCount: 2 })));
    create(buildOperation());

    const stop = el.querySelector<HTMLButtonElement>('.bw-stop')!;
    expect(stop.classList.contains('btn-ghost')).toBe(true);
    expect(textOf(stop)).toBe('Stop after the current account');
    click('.bw-stop');

    expect(cancel).toHaveBeenCalledWith(12);
    expect(el.querySelector('.bw-stop')?.getAttribute('aria-disabled')).toBe('true');
    expect(textOf(el.querySelector('#bw-stop-reason'))).toBe('Stopping after the current account…');
    click('.bw-stop');
    expect(cancel).toHaveBeenCalledTimes(1);
  });

  it('lists every account of a finished operation, with its outcome and its before and after', () => {
    const get = vi.spyOn(usersService, 'bulkGet').mockReturnValue(of(completed()));
    create(completed());
    fixture!.detectChanges();

    expect(get).toHaveBeenCalledTimes(1);
    expect(get).toHaveBeenCalledWith(12, { skip: 0, take: 200 });
    const cards = Array.from(el.querySelectorAll('.bw-res-card'));
    expect(cards.map(c => textOf(c.querySelector('.bw-res-card-title')))).toEqual(['ann', 'bob', 'cid']);
    expect(cards.map(c => textOf(c.querySelector('.bw-outcome')))).toEqual(['Changed', 'Changed', 'Skipped']);
    expect(textOf(cards[0].querySelector('.gh-change-note'))).toBe('Email not confirmed → Deleted');
    expect(textOf(cards[2].querySelector('.bw-res-message'))).toBe('Changed since the preview: signed in.');

    vi.advanceTimersByTime(10 * BULK_POLL_MS);
    expect(get).toHaveBeenCalledTimes(1);
  });

  it('downloads the report and asks for the admin log', () => {
    vi.spyOn(usersService, 'bulkGet').mockReturnValue(of(completed()));
    const blob = new Blob(['user,outcome\n'], { type: 'text/csv' });
    const report = vi.spyOn(usersService, 'bulkReport').mockReturnValue(of(blob));
    create(completed());
    const save = vi.fn();
    component.saveFile = save;
    const shown = vi.fn();
    component.showInLog.subscribe(shown);

    expect(textOf(el.querySelector('.bw-download'))).toBe('Download report (CSV)');
    click('.bw-download');
    expect(report).toHaveBeenCalledWith(12);
    expect(save).toHaveBeenCalledWith(blob, 'bulk-operation-12.csv');

    click('.bw-show-in-log');
    expect(shown).toHaveBeenCalledTimes(1);
  });

  it('says how an interrupted run ended', () => {
    vi.spyOn(usersService, 'bulkGet').mockReturnValue(of(buildOperation({ status: 'Interrupted', changedCount: 1, pendingCount: 2 })));
    create(buildOperation({ status: 'Interrupted', changedCount: 1, pendingCount: 2 }));

    expect(textOf(el.querySelector('.bw-res-note'))).toBe(
      'Interrupted when Overseer restarted. The accounts already processed stay as they are; the rest were not changed.');
  });
});
