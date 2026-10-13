import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { of, throwError } from 'rxjs';
import {
  AccountDeletionDecisionResult,
  AccountDeletionRequest,
  AccountDeletionRequestsPage,
  AccountDeletionRequestsQuery,
  AdminService
} from '../../../services/admin.service';
import { FilterFacetComponent } from '../../../shared/data-table/filter-facet.component';
import { AdminPageStore } from '../../admin-page.store';
import { configureAdminTestBed } from '../../admin.component.testing';
import { DELETION_REQUESTS_VIEW_STORAGE_KEY, DeletionRequestsComponent } from './deletion-requests.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

function clearStorage(): void {
  try {
    localStorage.removeItem(DELETION_REQUESTS_VIEW_STORAGE_KEY);
  } catch {
    // Nothing stored.
  }
}

function deletionRequest(id: number, overrides: Partial<AccountDeletionRequest> = {}): AccountDeletionRequest {
  return {
    id,
    userId: `u-${id}`,
    userName: id === 1 ? 'alice' : `user${id}`,
    requestedUtc: '2026-09-16T08:00:00Z',
    ageDays: 27,
    isOverdue: false,
    status: 'Pending',
    holdReasons: 'Manual hold: open fraud case',
    currentReasons: null,
    manualHold: true,
    automaticRule: false,
    ...overrides
  };
}

function page(rows: AccountDeletionRequest[], overrides: Partial<AccountDeletionRequestsPage> = {}): AccountDeletionRequestsPage {
  return {
    totalCount: rows.length,
    pendingCount: rows.filter(r => r.status === 'Pending').length,
    overdueCount: rows.filter(r => r.isOverdue).length,
    erasurePathsConfigured: true,
    rows,
    ...overrides
  };
}

/** Requests with ids `from` to `from + count - 1`. */
function requestRange(from: number, count: number): AccountDeletionRequest[] {
  return Array.from({ length: count }, (_, i) => deletionRequest(from + i));
}

function decision(overrides: Partial<AccountDeletionDecisionResult> = {}): AccountDeletionDecisionResult {
  return { emailSent: true, emailError: null, bonesFilesMissing: 0, foldersMissing: 0, ...overrides };
}

describe('DeletionRequestsComponent', () => {
  let fixture: ComponentFixture<DeletionRequestsComponent>;
  let component: DeletionRequestsComponent;
  let el: HTMLElement;
  let adminService: AdminService;
  let store: AdminPageStore;
  let requestsSpy: Mock;
  /** What a list request (take 20) returns; a count request (take 1) gets an empty page. */
  let listPage: AccountDeletionRequestsPage;

  beforeEach(async () => {
    clearStorage();
    ({ adminService, store } = await configureAdminTestBed(DeletionRequestsComponent));
    listPage = page([deletionRequest(1)]);
    requestsSpy = vi.spyOn(adminService, 'getAccountDeletionRequests').mockImplementation(
      (query: AccountDeletionRequestsQuery) => of(query.take === 1 ? page([], { totalCount: 3 }) : listPage));
    vi.spyOn(store, 'showToast').mockReturnValue(undefined);
  });

  afterEach(() => {
    if (fixture) {
      el.querySelectorAll('dialog').forEach(dialog => {
        if (dialog.open) dialog.close();
      });
      fixture.destroy();
    }
    clearStorage();
  });

  function create(): void {
    fixture = TestBed.createComponent(DeletionRequestsComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  }

  /** The list requests, without the facet's count requests. */
  const listQueries = (): AccountDeletionRequestsQuery[] =>
    requestsSpy.mock.calls.map(call => call[0] as AccountDeletionRequestsQuery).filter(query => query.take === 20);
  const lastListQuery = (): AccountDeletionRequestsQuery => listQueries()[listQueries().length - 1];
  const card = (id: number): HTMLElement => el.querySelector<HTMLElement>(`.del-card[data-request-id="${id}"]`)!;

  function typeInto(input: HTMLInputElement | HTMLTextAreaElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  describe('list', () => {
    it('defaults the Status facet to Pending and reports the pending count', () => {
      fixture = TestBed.createComponent(DeletionRequestsComponent);
      component = fixture.componentInstance;
      el = fixture.nativeElement as HTMLElement;
      const counts: number[] = [];
      component.pendingCountChange.subscribe(count => counts.push(count));
      fixture.detectChanges();

      expect(requestsSpy.mock.calls[0][0]).toEqual({ status: 'Pending', skip: 0, take: 20 });
      expect(counts).toEqual([1]);

      const facet = fixture.debugElement.query(By.directive(FilterFacetComponent)).componentInstance as FilterFacetComponent;
      expect(facet.facetId).toBe('del-facet-status');
      expect(facet.mode).toBe('single');
      expect(facet.selected).toEqual(['Pending']);
      expect(facet.options.map(o => o.value)).toEqual(['Pending', 'Completed', 'Declined']);
      expect(el.querySelector('.del-chips .gh-filter-chip')!.getAttribute('aria-label')).toBe('Remove filter Status: Pending');
    });

    it('refetches page 1 for every status when Any status is chosen', () => {
      create();

      const facet = fixture.debugElement.query(By.directive(FilterFacetComponent)).componentInstance as FilterFacetComponent;
      facet.selectedChange.emit([]);
      fixture.detectChanges();

      expect(lastListQuery().status).toBeUndefined();
      expect(lastListQuery()).toEqual(expect.objectContaining({ skip: 0, take: 20 }));
    });

    it('shows the status and age in the kicker, and Overdue as a word', () => {
      listPage = page([deletionRequest(1, { isOverdue: true, automaticRule: true }), deletionRequest(2, { ageDays: 1, manualHold: false })]);
      create();

      expect(textOf(card(1).querySelector('.del-status-word'))).toBe('Pending');
      expect(textOf(card(1).querySelector('.del-age'))).toBe('27 days');
      expect(textOf(card(1).querySelector('.del-overdue-tag'))).toBe('Overdue');
      expect(textOf(card(1).querySelector('.del-hold-tag'))).toBe('Manual hold');
      expect(textOf(card(1).querySelector('.del-rule-tag'))).toBe('Automatic rule');
      expect(textOf(card(2).querySelector('.del-age'))).toBe('1 day');
      expect(card(2).querySelector('.del-overdue-tag')).toBeNull();
      expect(card(2).querySelector('.del-hold-tag')).toBeNull();
    });

    it('shows why it was held, and the reasons now when they differ', () => {
      listPage = page([
        deletionRequest(1, { currentReasons: 'Automatic rule: recent provider safety blocks' }),
        deletionRequest(2, { currentReasons: 'Manual hold: open fraud case' })
      ]);
      create();

      expect(textOf(card(1).querySelector('.del-why-title'))).toBe('Why it was held');
      expect(textOf(card(1).querySelector('.del-reasons'))).toBe('Manual hold: open fraud case');
      expect(textOf(card(1).querySelector('.del-now'))).toBe('Now: Automatic rule: recent provider safety blocks');
      expect(card(2).querySelector('.del-now')).toBeNull();
    });

    it('shows a decided request without actions, and a deleted account by that name', () => {
      listPage = page([
        deletionRequest(1, {
          status: 'Completed', userId: null, userName: null,
          decidedByUserName: 'admin', decidedUtc: '2026-10-01T12:00:00Z'
        }),
        deletionRequest(2, {
          status: 'Declined', decidedByUserName: 'admin', decidedUtc: '2026-10-02T12:00:00Z', decisionNote: 'Duplicate request'
        })
      ]);
      create();

      expect(textOf(card(1).querySelector('.del-card-title'))).toBe('Deleted account');
      expect(textOf(card(1).querySelector('.del-decision-text'))).toBe('Completed by admin on 2026-10-01');
      expect(card(1).querySelector('.del-actions')).toBeNull();

      expect(textOf(card(2).querySelector('.del-decision-text'))).toBe('Declined by admin on 2026-10-02');
      expect(textOf(card(2).querySelector('.del-note'))).toBe('Note: Duplicate request');
      expect(card(2).querySelector('.del-approve')).toBeNull();
      expect(card(2).querySelector('.del-decline')).toBeNull();
      expect(card(2).querySelector('.del-security')).toBeTruthy();
    });

    it('names the status in the empty state', () => {
      listPage = page([]);
      create();

      expect(textOf(el.querySelector('.del-empty'))).toBe('No pending deletion requests.');
      expect(el.querySelector('.del-cards')).toBeNull();
    });

    it('appends the next page on Show more and focuses the first new card', () => {
      listPage = page(requestRange(1, 20), { totalCount: 25 });
      create();

      expect(el.querySelectorAll('.del-card').length).toBe(20);
      expect(textOf(el.querySelector('#del-status'))).toBe('Showing 20 of 25 requests');
      const more = el.querySelector<HTMLButtonElement>('.del-show-more')!;
      expect(textOf(more)).toBe('Show 5 more');

      listPage = page(requestRange(21, 5), { totalCount: 25 });
      more.click();
      fixture.detectChanges();

      expect(lastListQuery()).toEqual(expect.objectContaining({ status: 'Pending', skip: 20, take: 20 }));
      expect(el.querySelectorAll('.del-card').length).toBe(25);
      expect(document.activeElement?.id).toBe('del-21-title');
      expect(el.querySelector('.del-show-more')).toBeNull();
    });
  });

  describe('actions', () => {
    it('names the user in the actions group and its buttons', () => {
      create();

      expect(card(1).querySelector('.del-actions')!.getAttribute('role')).toBe('group');
      expect(card(1).querySelector('.del-actions')!.getAttribute('aria-label')).toBe('Actions for alice');
      expect(card(1).querySelector('.del-approve')!.getAttribute('aria-label')).toBe('Approve and delete the account alice');
      expect(card(1).querySelector('.del-decline')!.getAttribute('aria-label')).toBe('Decline the deletion request of alice');
      expect(card(1).querySelector('.del-security')!.getAttribute('aria-label')).toBe('Show the security events of alice');
    });

    it('Approve needs the user name typed exactly, then deletes, reports the outcome and refetches', () => {
      const approve = vi.spyOn(adminService, 'approveAccountDeletion').mockReturnValue(
        of(decision({ emailSent: false, emailError: 'SMTP is down', bonesFilesMissing: 2 })));
      create();
      const listCallsBefore = listQueries().length;

      card(1).querySelector<HTMLButtonElement>('.del-approve')!.click();
      fixture.detectChanges();
      const dialog = el.querySelector<HTMLDialogElement>('dialog.del-approve-dialog')!;
      expect(dialog.open).toBe(true);
      expect(textOf(dialog.querySelector('#del-approve-title'))).toBe('Approve and delete alice?');

      const confirm = dialog.querySelector<HTMLButtonElement>('.del-approve-confirm')!;
      expect(confirm.getAttribute('aria-disabled')).toBe('true');
      confirm.click();
      expect(approve).not.toHaveBeenCalled();

      const input = dialog.querySelector<HTMLInputElement>('#del-approve-name')!;
      typeInto(input, 'Alice');
      expect(confirm.getAttribute('aria-disabled')).toBe('true');
      confirm.click();
      expect(approve).not.toHaveBeenCalled();

      typeInto(input, 'alice');
      expect(confirm.getAttribute('aria-disabled')).toBeNull();
      confirm.click();
      fixture.detectChanges();

      expect(approve).toHaveBeenCalledWith(1, 'alice');
      expect(dialog.open).toBe(false);
      const [message, type] = (store.showToast as Mock).mock.lastCall!;
      expect(message).toContain('2 bones files were not found on disk.');
      expect(message).toContain('The decision email was not sent: SMTP is down');
      expect(type).toBe('info');
      expect(listQueries().length).toBe(listCallsBefore + 1);
    });

    it('keeps the Approve dialog open with the server message when the approval is refused', () => {
      vi.spyOn(adminService, 'approveAccountDeletion').mockReturnValue(
        throwError(() => ({ status: 400, error: { message: 'The user name does not match.' } })));
      create();

      card(1).querySelector<HTMLButtonElement>('.del-approve')!.click();
      fixture.detectChanges();
      const dialog = el.querySelector<HTMLDialogElement>('dialog.del-approve-dialog')!;
      typeInto(dialog.querySelector<HTMLInputElement>('#del-approve-name')!, 'alice');
      dialog.querySelector<HTMLButtonElement>('.del-approve-confirm')!.click();
      fixture.detectChanges();

      expect(dialog.open).toBe(true);
      expect(textOf(dialog.querySelector('#del-approve-error'))).toBe('The user name does not match.');
    });

    it('makes Approve aria-disabled with the reason while the erasure paths are missing', () => {
      const approve = vi.spyOn(adminService, 'approveAccountDeletion');
      listPage = page([deletionRequest(1)], { erasurePathsConfigured: false, erasurePathsMessage: 'DumpLogPath is not configured.' });
      create();

      const button = card(1).querySelector<HTMLButtonElement>('.del-approve')!;
      expect(button.getAttribute('aria-disabled')).toBe('true');
      expect(button.getAttribute('interestfor')).toBe('del-1-approve-tip');
      expect(textOf(el.querySelector('#del-1-approve-tip'))).toBe('DumpLogPath is not configured.');

      button.click();
      fixture.detectChanges();
      expect(el.querySelector<HTMLDialogElement>('dialog.del-approve-dialog')!.open).toBe(false);
      expect(approve).not.toHaveBeenCalled();
    });

    it('falls back to a fixed reason when the server sends no erasure message', () => {
      listPage = page([deletionRequest(1)], { erasurePathsConfigured: false, erasurePathsMessage: null });
      create();

      expect(textOf(el.querySelector('#del-1-approve-tip'))).toBe('The account erasure paths are not configured.');
    });

    it('Decline needs a note, then declines with it and refetches', () => {
      const decline = vi.spyOn(adminService, 'declineAccountDeletion').mockReturnValue(of(decision()));
      create();
      const listCallsBefore = listQueries().length;

      card(1).querySelector<HTMLButtonElement>('.del-decline')!.click();
      fixture.detectChanges();
      const dialog = el.querySelector<HTMLDialogElement>('app-admin-reason-dialog dialog')!;
      expect(dialog.open).toBe(true);
      expect(textOf(dialog.querySelector('h3'))).toBe('Decline the deletion request');
      expect(textOf(dialog.querySelector('label'))).toBe('Note');
      expect(dialog.querySelector('textarea')!.getAttribute('maxlength')).toBe('512');

      const confirm = dialog.querySelector<HTMLButtonElement>('.reason-confirm')!;
      expect(confirm.getAttribute('aria-disabled')).toBe('true');
      confirm.click();
      expect(decline).not.toHaveBeenCalled();

      typeInto(dialog.querySelector('textarea')!, ' Duplicate request ');
      confirm.click();
      fixture.detectChanges();

      expect(decline).toHaveBeenCalledWith(1, 'Duplicate request');
      expect(store.showToast).toHaveBeenCalledWith(expect.stringContaining('The user was emailed the decision.'), 'success', 'Request Declined');
      expect(listQueries().length).toBe(listCallsBefore + 1);
    });

    it('shows the server message in an error toast when the decline fails', () => {
      vi.spyOn(adminService, 'declineAccountDeletion').mockReturnValue(
        throwError(() => ({ status: 409, error: { message: 'The request was already decided.' } })));
      create();

      component.openDecline(listPage.rows[0]);
      fixture.detectChanges();
      const dialog = el.querySelector<HTMLDialogElement>('app-admin-reason-dialog dialog')!;
      typeInto(dialog.querySelector('textarea')!, 'Note');
      dialog.querySelector<HTMLButtonElement>('.reason-confirm')!.click();

      expect(store.showToast).toHaveBeenCalledWith('The request was already decided.', 'error', 'Error');
    });

    it('Security events asks the Security tab for the user', () => {
      const requestSecurityUser = vi.spyOn(store, 'requestSecurityUser').mockReturnValue(undefined);
      create();

      card(1).querySelector<HTMLButtonElement>('.del-security')!.click();

      expect(requestSecurityUser).toHaveBeenCalledWith('u-1', 'alice');
    });
  });
});
