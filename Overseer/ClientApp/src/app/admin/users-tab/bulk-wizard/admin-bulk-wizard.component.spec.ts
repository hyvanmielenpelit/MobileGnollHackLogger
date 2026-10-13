import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { NEVER, of, throwError } from 'rxjs';

import {
  AdminBulkAction,
  AdminBulkItem,
  AdminBulkOperation,
  AdminBulkPreview,
  AdminBulkPreviewItem,
  AdminUsersService
} from '../../../services/admin-users.service';
import { AdminUsersLogFilter } from '../../admin-page.store';
import { configureAdminTestBed } from '../../admin.component.testing';
import { buildAdminUser, buildUsersPage } from '../admin-users.testing';
import { AdminBulkWizardComponent, BULK_BUSY_MESSAGE } from './admin-bulk-wizard.component';
import { BULK_POLL_MS } from './bulk-results-step.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

function item(id: string, name: string): AdminBulkPreviewItem {
  return {
    userId: id,
    userName: name,
    email: `${name}@example.com`,
    statuses: ['EmailUnconfirmed'],
    statusText: 'Email not confirmed',
    createdUtc: '2026-01-02T00:00:00Z',
    createdUtcIsEstimate: false,
    lastSignInUtc: null,
    isAdvanced: false
  };
}

/** A delete preview of three eligible accounts, valid for 15 minutes. */
function buildPreview(overrides: Partial<AdminBulkPreview> = {}): AdminBulkPreview {
  return {
    operationId: 7,
    action: 'Delete',
    expiresUtc: new Date(Date.now() + 15 * 60_000).toISOString(),
    dangerLevel: 'danger',
    requiredAcknowledgments: ['reviewed', 'irreversible'],
    matchedCount: 3,
    eligibleCount: 3,
    advancedCount: 0,
    refusedCount: 0,
    noOpCount: 0,
    truncatedCount: 0,
    maxUsers: 500,
    eligible: [item('u1', 'ann'), item('u2', 'bob'), item('u3', 'cid')],
    advanced: [],
    refused: [],
    dataTotals: [],
    requiredPhrase: 'DELETE 3 ACCOUNTS',
    ...overrides
  };
}

function buildOperation(overrides: Partial<AdminBulkOperation> = {}): AdminBulkOperation {
  return {
    id: 7,
    action: 'Delete',
    status: 'Queued',
    actorUserName: 'tommi',
    dangerLevel: 'danger',
    reason: 'Spam accounts',
    until: null,
    stopMessage: null,
    createdUtc: '2026-10-13T15:30:00Z',
    expiresUtc: '2026-10-13T15:45:00Z',
    startedUtc: null,
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

function completedOperation(id: number): AdminBulkOperation {
  const items: AdminBulkItem[] = [{
    id: 1, userId: 'u1', userName: 'ann', isAdvanced: false, excluded: false, outcome: 'Changed', message: null,
    beforeStatus: 'Email not confirmed', afterStatus: 'Deleted', processedUtc: '2026-10-13T15:40:00Z'
  }];
  return buildOperation({ id, status: 'Completed', changedCount: 1, pendingCount: 0, items, itemsTotalCount: 1 });
}

const httpError = (status: number, message: string, code: string | null) =>
  throwError(() => new HttpErrorResponse({ status, error: { message, code } }));

/** Resolves on the dialog's next native close event. */
const nextClose = (dialog: HTMLDialogElement): Promise<void> =>
  new Promise(resolve => dialog.addEventListener('close', () => resolve(), { once: true }));

describe('AdminBulkWizardComponent', () => {
  let fixture: ComponentFixture<AdminBulkWizardComponent>;
  let component: AdminBulkWizardComponent;
  let el: HTMLElement;
  let usersService: AdminUsersService;

  beforeEach(async () => {
    ({ usersService } = await configureAdminTestBed(AdminBulkWizardComponent));
    vi.spyOn(usersService, 'list').mockReturnValue(of(buildUsersPage([
      buildAdminUser({ id: 'u1', userName: 'ann', emailConfirmed: false, statuses: ['EmailUnconfirmed'] })
    ], 3)));
    vi.spyOn(usersService, 'bulkCancel').mockReturnValue(of(buildOperation({ status: 'Canceled' })));
    fixture = TestBed.createComponent(AdminBulkWizardComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  afterEach(() => {
    const dialog = el.querySelector('dialog');
    if (dialog?.open) dialog.close();
    fixture.destroy();
    vi.useRealTimers();
  });

  const dialog = () => el.querySelector<HTMLDialogElement>('dialog.bw-dialog')!;
  const query = <T extends Element = HTMLElement>(selector: string) => el.querySelector<T>(selector);
  const click = (selector: string): void => {
    query<HTMLElement>(selector)!.click();
    fixture.detectChanges();
  };
  const type = (selector: string, value: string): void => {
    const field = query<HTMLInputElement | HTMLTextAreaElement>(selector)!;
    field.value = value;
    field.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  };

  /** From step 1, through step 2 with `action` (and a reason where it takes one), to the review. */
  function reachReview(preview: AdminBulkPreview, action: AdminBulkAction = 'Delete') {
    component.open({ emailConfirmed: 'no', registeredOlderThanDays: 30 });
    fixture.detectChanges();
    click('.bw-next');
    click(`#bw-action-${action}`);
    if (query('#bw-action-reason')) type('#bw-action-reason', 'Spam accounts');
    const previewSpy = vi.spyOn(usersService, 'bulkPreview').mockReturnValue(of(preview));
    click('.bw-next');
    return previewSpy;
  }

  it('opens as a modal without light dismiss, on step 1 with the criteria it was given', () => {
    component.open({ statuses: ['Banned'] });
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(dialog().matches(':modal')).toBe(true);
    expect(dialog().hasAttribute('closedby')).toBe(false);
    expect(dialog().classList.contains('gh-dialog-fullscreen')).toBe(true);
    expect(document.activeElement?.id).toBe('bw-title');
    expect(Array.from(el.querySelectorAll('.bw-step-item')).map(s => textOf(s)))
      .toEqual(['1. Users', '2. Action', '3. Review', '4. Results']);
    expect(query('.bw-step-item[aria-current="step"]')?.getAttribute('data-step')).toBe('1');
    expect(query<HTMLInputElement>('#bw-preset-banned')!.checked).toBe(true);
    expect(usersService.list).toHaveBeenCalledWith({ statuses: ['Banned'] }, 'oldest', 0, 10);
    expect(textOf(query('#bw-match-status'))).toBe('3 users match');
  });

  it('keeps Next unavailable, with the reason, while nothing matches', () => {
    vi.spyOn(usersService, 'list').mockReturnValue(of(buildUsersPage([], 0)));
    component.open({ statuses: ['Banned'] });
    fixture.detectChanges();

    const next = query<HTMLButtonElement>('.bw-next')!;
    expect(next.getAttribute('aria-disabled')).toBe('true');
    expect(next.getAttribute('aria-describedby')).toBe('bw-footer-note');
    expect(textOf(query('#bw-footer-note'))).toBe('No account matches these criteria; change them to continue.');
    click('.bw-next');
    expect(component.step).toBe(1);
    expect(query('.bw-back')).toBeNull();
  });

  it('applies a preset as the criteria', () => {
    component.open({});
    fixture.detectChanges();

    click('#bw-preset-unconfirmed-old');

    expect(component.criteria).toEqual({ emailConfirmed: 'no', registeredOlderThanDays: 30, hasGameData: 'no', hasOverseerData: 'no' });
    expect(usersService.list).toHaveBeenLastCalledWith(component.criteria, 'oldest', 0, 10);
  });

  it('moves to the action step with focus on its heading, and asks for an action', () => {
    component.open({});
    fixture.detectChanges();

    click('.bw-next');

    expect(component.step).toBe(2);
    expect(document.activeElement?.id).toBe('bw-step-heading');
    expect(textOf(query('#bw-step-heading'))).toBe('2. Action');
    expect(query('.bw-step-item[data-step="1"]')?.classList.contains('is-done')).toBe(true);
    expect(query('.bw-next')?.getAttribute('aria-disabled')).toBe('true');
    expect(textOf(query('#bw-footer-note'))).toBe('Still needed: an action.');

    click('.bw-back');
    expect(component.step).toBe(1);
  });

  it('shows a refused delete preview inline with Back to Users', () => {
    component.open({});
    fixture.detectChanges();
    click('.bw-next');
    click('#bw-action-Delete');
    type('#bw-action-reason', 'Spam accounts');
    vi.spyOn(usersService, 'bulkPreview').mockReturnValue(httpError(400,
      'Bulk deletion needs at least one of: Email not confirmed, Last sign-in, Registered more than 30 days ago.', 'narrowing'));

    click('.bw-next');

    expect(component.step).toBe(2);
    expect(textOf(query('#bw-action-error'))).toContain('Bulk deletion needs at least one of');
    click('.bw-back-to-users');
    expect(component.step).toBe(1);
    expect(document.activeElement?.id).toBe('bw-step-heading');
  });

  it('reviews a delete: the phrase follows the live count, and the run sends exactly the choices', () => {
    const previewSpy = reachReview(buildPreview());

    expect(previewSpy).toHaveBeenCalledWith({
      action: 'Delete',
      options: { reason: 'Spam accounts' },
      criteria: { emailConfirmed: 'no', registeredOlderThanDays: 30 }
    });
    expect(component.step).toBe(3);
    expect(document.activeElement?.id).toBe('bw-step-heading');

    const run = () => query<HTMLButtonElement>('.bw-run')!;
    expect(textOf(run())).toBe('Delete 3 Accounts');
    expect(run().classList.contains('btn-gh-delete')).toBe(true);
    expect(run().getAttribute('aria-disabled')).toBe('true');
    expect(textOf(query('#bw-footer-note'))).toBe('Still needed: every acknowledgment and DELETE 3 ACCOUNTS typed exactly.');

    click('#bw-ack-reviewed');
    click('#bw-ack-irreversible');
    type('#bw-phrase', 'DELETE 3 ACCOUNTS');
    expect(run().getAttribute('aria-disabled')).toBeNull();
    expect(query('#bw-footer-note')).toBeNull();

    click('#bw-include-u2');
    expect(textOf(run())).toBe('Delete 2 Accounts');
    expect(run().getAttribute('aria-disabled')).toBe('true');
    expect(query('#bw-phrase')?.getAttribute('aria-invalid')).toBe('true');
    expect(textOf(query('#bw-footer-note'))).toBe('Still needed: DELETE 2 ACCOUNTS typed exactly.');
    const execute = vi.spyOn(usersService, 'bulkExecute').mockReturnValue(of(buildOperation()));
    click('.bw-run');
    expect(execute).not.toHaveBeenCalled();

    type('#bw-phrase', 'DELETE 2 ACCOUNTS');
    vi.spyOn(usersService, 'bulkGet').mockReturnValue(NEVER);
    click('.bw-run');

    expect(execute).toHaveBeenCalledWith(7, {
      excludedUserIds: ['u2'],
      includeAdvanced: false,
      acknowledgments: ['reviewed', 'irreversible'],
      confirmationPhrase: 'DELETE 2 ACCOUNTS'
    });
    expect(component.step).toBe(4);
    expect(document.activeElement?.id).toBe('bw-step-heading');
    expect(query('.bw-back')).toBeNull();
    expect(textOf(query('.bw-done'))).toBe('Done');
    expect(textOf(query('#bw-footer-note'))).toBe('Closing does not stop it. The operation keeps running; see Admin log.');
    expect(textOf(query('#bw-close-tip'))).toBe('Close. The operation keeps running; see Admin log.');
  });

  it('asks a caution action for the review acknowledgment only', () => {
    reachReview(buildPreview({
      action: 'Ban', dangerLevel: 'caution', requiredAcknowledgments: ['reviewed'], dataTotals: null, requiredPhrase: null
    }), 'Ban');

    expect(Array.from(el.querySelectorAll('.bw-acks input')).map(i => i.id)).toEqual(['bw-ack-reviewed']);
    expect(query('#bw-phrase')).toBeNull();
    expect(textOf(query('.bw-run'))).toBe('Ban 3 Accounts');
    click('#bw-ack-reviewed');
    expect(query('.bw-run')?.getAttribute('aria-disabled')).toBeNull();
  });

  it('runs a safe action without acknowledgments, in the gold button', () => {
    reachReview(buildPreview({
      action: 'Unlock', dangerLevel: 'safe', requiredAcknowledgments: [], dataTotals: null, requiredPhrase: null
    }), 'Unlock');

    expect(query('.bw-acks')).toBeNull();
    const run = query<HTMLButtonElement>('.bw-run')!;
    expect(textOf(run)).toBe('Unlock 3 Accounts');
    expect(run.classList.contains('btn-gh-delete')).toBe(false);
    expect(run.getAttribute('aria-disabled')).toBeNull();
  });

  it('refreshes an expired review on the same step and clears what was confirmed', () => {
    const previewSpy = reachReview(buildPreview());
    click('#bw-ack-reviewed');
    click('#bw-ack-irreversible');
    type('#bw-phrase', 'DELETE 3 ACCOUNTS');
    previewSpy.mockClear();
    previewSpy.mockReturnValue(of(buildPreview({ operationId: 8, eligible: [item('u1', 'ann'), item('u3', 'cid'), item('u4', 'dee')] })));
    vi.spyOn(usersService, 'bulkExecute').mockReturnValue(httpError(409, 'The preview has expired. Preview again.', 'expired'));

    click('.bw-run');

    expect(previewSpy).toHaveBeenCalledTimes(1);
    expect(component.step).toBe(3);
    expect(component.preview?.operationId).toBe(8);
    expect(textOf(query('.bw-refreshed'))).toBe('The review expired and was refreshed. 2 accounts changed; check the list again.');
    expect(query<HTMLInputElement>('#bw-ack-reviewed')!.checked).toBe(false);
    expect(query<HTMLInputElement>('#bw-ack-irreversible')!.checked).toBe(false);
    expect(query<HTMLInputElement>('#bw-phrase')!.value).toBe('');
    expect(component.choices.includeAdvanced).toBe(false);
    expect(query('.bw-run')?.getAttribute('aria-disabled')).toBe('true');
    expect(document.activeElement?.id).toBe('bw-step-heading');
  });

  it('says another operation is running on a 409 busy, and stays on the review', () => {
    const previewSpy = reachReview(buildPreview({
      action: 'Unlock', dangerLevel: 'safe', requiredAcknowledgments: [], dataTotals: null, requiredPhrase: null
    }), 'Unlock');
    previewSpy.mockClear();
    vi.spyOn(usersService, 'bulkExecute').mockReturnValue(httpError(409, 'Another bulk operation is running. Wait for it to finish.', 'busy'));

    click('.bw-run');

    expect(component.step).toBe(3);
    expect(textOf(query('.bw-execute-error'))).toBe(BULK_BUSY_MESSAGE);
    expect(previewSpy).not.toHaveBeenCalled();
  });

  it('closes before the run without changing anything, and discards the preview', async () => {
    reachReview(buildPreview());
    const execute = vi.spyOn(usersService, 'bulkExecute');
    const closed = vi.fn();
    component.closed.subscribe(closed);

    const done = nextClose(dialog());
    click('.bw-close');
    await done;
    fixture.detectChanges();

    expect(dialog().open).toBe(false);
    expect(execute).not.toHaveBeenCalled();
    expect(usersService.bulkCancel).toHaveBeenCalledWith(7);
    expect(closed).toHaveBeenCalledTimes(1);
    expect(query('.bw-step')).toBeNull();
    expect(component.criteria).toEqual({});
  });

  it('discards the preview when going back from the review', () => {
    reachReview(buildPreview());

    click('.bw-back');

    expect(component.step).toBe(2);
    expect(component.preview).toBeNull();
    expect(usersService.bulkCancel).toHaveBeenCalledWith(7);
  });

  it('reports the end of a run it started, once', () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const finished: number[] = [];
    component.operationFinished.subscribe(id => finished.push(id));
    reachReview(buildPreview({
      action: 'Unlock', dangerLevel: 'safe', requiredAcknowledgments: [], dataTotals: null, requiredPhrase: null
    }), 'Unlock');
    vi.spyOn(usersService, 'bulkExecute').mockReturnValue(of(buildOperation({ action: 'Unlock' })));
    vi.spyOn(usersService, 'bulkGet').mockReturnValue(of(completedOperation(7)));

    click('.bw-run');
    expect(finished).toEqual([]);
    vi.advanceTimersByTime(BULK_POLL_MS);
    fixture.detectChanges();

    expect(finished).toEqual([7]);
    expect(query('#bw-footer-note')).toBeNull();
    vi.advanceTimersByTime(10 * BULK_POLL_MS);
    expect(finished).toEqual([7]);
  });

  it('opens on the results of an operation, read-only, and Show in admin log closes it', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    vi.spyOn(usersService, 'bulkGet').mockReturnValue(of(completedOperation(12)));
    const finished = vi.fn();
    component.operationFinished.subscribe(finished);
    const shown: AdminUsersLogFilter[] = [];
    component.showInLog.subscribe(filter => shown.push(filter));

    component.openResults(12);
    vi.advanceTimersByTime(0);
    fixture.detectChanges();

    expect(component.isOpen).toBe(true);
    expect(component.operationId).toBe(12);
    expect(query('.bw-step-item[aria-current="step"]')?.getAttribute('data-step')).toBe('4');
    expect(el.querySelectorAll('.bw-step-item.is-done').length).toBe(0);
    expect(query('.bw-back')).toBeNull();
    expect(query('.bw-next')).toBeNull();
    expect(textOf(query('#bw-res-status'))).toBe('Deleted 1 · Skipped 0 · Failed 0');

    const done = nextClose(dialog());
    click('.bw-show-in-log');
    expect(shown).toEqual([{ bulkOperationId: 12 }]);
    await done;
    expect(dialog().open).toBe(false);
    expect(finished).not.toHaveBeenCalled();
  });

  it('stops polling when it closes during a run', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const get = vi.spyOn(usersService, 'bulkGet').mockReturnValue(of(buildOperation({ id: 12, status: 'Running', changedCount: 1, pendingCount: 2 })));

    component.openResults(12);
    vi.advanceTimersByTime(0);
    vi.advanceTimersByTime(BULK_POLL_MS);
    expect(get).toHaveBeenCalledTimes(2);
    fixture.detectChanges();
    expect(textOf(query('#bw-close-tip'))).toBe('Close. The operation keeps running; see Admin log.');

    const done = nextClose(dialog());
    click('.bw-close');
    await done;
    vi.advanceTimersByTime(10 * BULK_POLL_MS);

    expect(get).toHaveBeenCalledTimes(2);
    expect(query('app-bulk-results-step')).toBeNull();
  });
});
