import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { AdminService, SecurityEvent, SecurityEventsPage, SecuritySummary } from '../../services/admin.service';
import { AdminUserActionResult, AdminUsersService } from '../../services/admin-users.service';
import { FilterFacetComponent } from '../../shared/data-table/filter-facet.component';
import { AdminPageStore } from '../admin-page.store';
import { configureAdminTestBed } from '../admin.component.testing';
import { AdminSecurityTabComponent, SECURITY_EVENTS_VIEW_STORAGE_KEY } from './admin-security-tab.component';

const settle = (ms = 0): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

function clearStorage(): void {
  try {
    localStorage.removeItem(SECURITY_EVENTS_VIEW_STORAGE_KEY);
  } catch {
    // Nothing stored.
  }
}

function securityEvent(id: number, overrides: Partial<SecurityEvent> = {}): SecurityEvent {
  return {
    id,
    occurredUtc: '2026-10-13T09:15:00Z',
    lastOccurredUtc: '2026-10-13T09:15:00Z',
    kind: 'LoginFailed',
    count: 1,
    userId: 'u1',
    userName: 'Alice',
    userIsOverseerBlocked: false,
    ipAddress: '203.0.113.5',
    ...overrides
  };
}

function page(rows: SecurityEvent[], totalCount = rows.length): SecurityEventsPage {
  return { rows, totalCount };
}

/** Events with ids `from` down to `from - count + 1`, newest first. */
function eventRange(from: number, count: number): SecurityEvent[] {
  return Array.from({ length: count }, (_, i) => securityEvent(from - i));
}

function fullSummary(): SecuritySummary {
  const kind = (name: string, count: number, distinctUsers = 1, distinctIps = 1) => ({ kind: name, count, distinctUsers, distinctIps });
  return {
    kinds: [
      kind('LoginFailed', 5, 3, 4),
      kind('TwoFactorFailed', 2),
      kind('LoginLockedOut', 1, 1, 2),
      kind('QuotaRefused', 3),
      kind('RateLimitRejected', 4),
      kind('AccessDenied', 1),
      kind('ProviderRefusal', 1),
      kind('ProviderSafetyBlock', 2),
      kind('AttachmentRejected', 1),
      kind('ChatReported', 2, 2, 2),
      kind('AdminBlocked', 1),
      kind('AdminUnblocked', 1)
    ],
    pendingDeletionRequests: 3,
    activeAccountHolds: 2
  };
}

describe('AdminSecurityTabComponent', () => {
  let fixture: ComponentFixture<AdminSecurityTabComponent>;
  let component: AdminSecurityTabComponent;
  let el: HTMLElement;
  let adminService: AdminService;
  let usersService: AdminUsersService;
  let store: AdminPageStore;
  let eventsSpy: Mock;

  beforeEach(async () => {
    clearStorage();
    ({ adminService, usersService, store } = await configureAdminTestBed(AdminSecurityTabComponent));
    vi.spyOn(adminService, 'getSecuritySummary').mockReturnValue(of(fullSummary()));
    eventsSpy = vi.spyOn(adminService, 'getSecurityEvents').mockReturnValue(of(page([securityEvent(1)])));
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
    fixture = TestBed.createComponent(AdminSecurityTabComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  }

  const lastQuery = () => eventsSpy.mock.lastCall![0];
  const card = (id: number): HTMLElement => el.querySelector<HTMLElement>(`.sec-card[data-event-id="${id}"]`)!;
  const stat = (id: string): HTMLElement => el.querySelector<HTMLElement>(`.sec-stat[data-card="${id}"]`)!;
  /** A card's facts as label → value. */
  const factsOf = (cardEl: HTMLElement): Record<string, string> => Object.fromEntries(
    Array.from(cardEl.querySelectorAll('.sec-facts > div')).map(row => [textOf(row.querySelector('dt')), textOf(row.querySelector('dd'))])
  );

  function typeInto(input: HTMLInputElement | HTMLTextAreaElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }

  describe('summary', () => {
    it('loads the summary for the shared date range and renders a card per group', () => {
      create();

      expect(adminService.getSecuritySummary).toHaveBeenCalledWith(store.telemetryRange.start, store.telemetryRange.end);
      expect(store.telemetryRange.start).toMatch(/^\d{4}-\d{2}-\d{2}$/);

      expect(textOf(stat('sign-in').querySelector('.sec-stat-title'))).toBe('Sign-in failures');
      expect(textOf(stat('sign-in').querySelector('.sec-stat-value'))).toBe('7');
      expect(textOf(stat('sign-in').querySelector('.sec-stat-lines'))).toContain('Two-factor failed: 2');
      expect(textOf(stat('lockouts').querySelector('.sec-stat-value'))).toBe('1');
      expect(textOf(stat('lockouts').querySelector('.sec-stat-lines'))).toBe('1 user · 2 IP addresses');
      expect(textOf(stat('limits').querySelector('.sec-stat-value'))).toBe('7');
      expect(textOf(stat('limits').querySelector('.sec-stat-lines'))).toContain('Access denied: 1');
      expect(textOf(stat('provider').querySelector('.sec-stat-title'))).toBe('Provider refusals and policy hits');
      expect(textOf(stat('provider').querySelector('.sec-stat-value'))).toBe('3');
      expect(textOf(stat('attachments').querySelector('.sec-stat-value'))).toBe('1');
      expect(textOf(stat('reports').querySelector('.sec-stat-value'))).toBe('2');
      expect(textOf(stat('blocks').querySelector('.sec-stat-value'))).toBe('1');
      expect(textOf(stat('blocks').querySelector('.sec-stat-lines'))).toBe('Unblocks: 1');
      expect(textOf(stat('holds').querySelector('.sec-stat-value'))).toBe('2 active');
      expect(textOf(stat('holds').querySelector('.sec-stat-lines'))).toBe('3 pending deletion requests');
    });

    it('reads zero for a group without events', () => {
      (adminService.getSecuritySummary as Mock).mockReturnValue(of({ kinds: [], pendingDeletionRequests: 0, activeAccountHolds: 0 }));
      create();

      expect(textOf(stat('sign-in').querySelector('.sec-stat-value'))).toBe('0');
      expect(stat('lockouts').querySelector('.sec-stat-lines')).toBeNull();
    });

    it('opens the Users tab on its deletion requests from Review deletion requests', () => {
      create();
      const requested: string[] = [];
      store.tabRequested$.subscribe(tab => requested.push(tab));

      el.querySelector<HTMLButtonElement>('.sec-review-deletions')!.click();

      expect(store.usersSubTab).toBe('deletions');
      expect(requested).toEqual(['users']);
    });
  });

  describe('events list', () => {
    it('shows each event as a card with its kind, time, run, user and facts', () => {
      eventsSpy.mockReturnValue(of(page([
        securityEvent(3, {
          kind: 'ProviderSafetyBlock', count: 12, lastOccurredUtc: '2026-10-13T09:19:00Z',
          provider: 'Anthropic', modelId: 'claude-x', code: 'refusal', detail: 'Stopped by the provider', ipAddress: null
        }),
        securityEvent(2, { userId: 'u9', userName: null }),
        securityEvent(1, { userId: null, userName: null })
      ], 3)));
      create();

      expect(el.querySelector('ul.sec-cards')!.getAttribute('role')).toBe('list');
      expect(textOf(el.querySelector('#sec-status'))).toBe('Showing 3 of 3 events');
      expect(lastQuery()).toEqual(expect.objectContaining({ skip: 0, take: 20 }));

      const first = card(3);
      expect(first.getAttribute('aria-labelledby')).toBe('sec-3-title');
      const kicker = textOf(first.querySelector('.sec-kicker'));
      expect(kicker).toContain('Provider safety block');
      expect(kicker).toContain('2026-10-13 09:15 UTC');
      expect(kicker).toContain('×12');
      expect(kicker).toContain('12 times');
      expect(kicker).toContain('in 4 min');
      expect(textOf(first.querySelector('h5.sec-title'))).toBe('Alice');
      expect(factsOf(first)).toEqual({ Model: 'Anthropic · claude-x', Code: 'refusal', Detail: 'Stopped by the provider' });
      expect(first.querySelector('.sec-actions')!.getAttribute('role')).toBe('group');
      expect(first.querySelector('.sec-actions')!.getAttribute('aria-label')).toBe('Actions for Alice');

      const block = first.querySelector<HTMLButtonElement>('.sec-block')!;
      expect(block.getAttribute('aria-label')).toBe('Block Alice');
      expect(block.getAttribute('interestfor')).toBe('sec-3-block-tip');
      expect(block.hasAttribute('title')).toBe(false);
      expect(textOf(el.querySelector('#sec-3-block-tip'))).toBe('Block from Overseer AI');

      expect(textOf(card(2).querySelector('h5.sec-title'))).toBe('Unknown user');
      expect(factsOf(card(2))).toEqual({ IP: '203.0.113.5' });
      expect(textOf(card(1).querySelector('h5.sec-title'))).toBe('No user');
      expect(card(1).querySelector('.sec-actions')).toBeNull();
    });

    it('offers Unblock for a blocked user and says so in words', () => {
      eventsSpy.mockReturnValue(of(page([securityEvent(1, { userIsOverseerBlocked: true })])));
      create();

      expect(textOf(card(1).querySelector('.sec-blocked-tag'))).toBe('Blocked');
      expect(card(1).querySelector('.sec-block')).toBeNull();
      expect(card(1).querySelector('.sec-unblock')!.getAttribute('aria-label')).toBe('Unblock Alice');
    });

    it('names the admin account actions in words, on the cards and in the Kind facet', () => {
      const kind = (name: string, count: number) => ({ kind: name, count, distinctUsers: 1, distinctIps: 0 });
      (adminService.getSecuritySummary as Mock).mockReturnValue(of({
        kinds: [
          kind('AdminLockedOut', 2), kind('AdminUnlocked', 1), kind('AdminBanned', 1), kind('AdminUnbanned', 1),
          kind('AdminSignedOut', 1), kind('AdminEmailConfirmed', 1), kind('AdminTwoFactorReset', 1)
        ],
        pendingDeletionRequests: 0,
        activeAccountHolds: 0
      }));
      eventsSpy.mockReturnValue(of(page([
        securityEvent(2, { kind: 'AdminLockedOut', ipAddress: null }),
        securityEvent(1, { kind: 'AdminTwoFactorReset', ipAddress: null })
      ])));
      create();

      expect(textOf(card(2).querySelector('.sec-kind'))).toBe('Locked out by admin');
      expect(textOf(card(1).querySelector('.sec-kind'))).toBe('Two-factor reset by admin');

      const facet = fixture.debugElement.query(By.directive(FilterFacetComponent)).componentInstance as FilterFacetComponent;
      expect(facet.options.map(o => o.label)).toEqual([
        'Locked out by admin',
        'Unlocked by admin',
        'Banned by admin',
        'Unbanned by admin',
        'Signed out by admin',
        'Email confirmed by admin',
        'Two-factor reset by admin'
      ]);
      expect(facet.options.find(o => o.value === 'AdminLockedOut')?.count).toBe(2);
    });

    it('shows the empty state when the range holds no events', () => {
      eventsSpy.mockReturnValue(of(page([])));
      create();

      expect(textOf(el.querySelector('.sec-empty'))).toBe('No security events in this range.');
      expect(el.querySelector('.sec-cards')).toBeNull();
      expect(textOf(el.querySelector('#sec-status'))).toBe('');
    });

    it('refetches page 1 for the Kind facet and shows its chip', () => {
      create();
      eventsSpy.mockClear();

      const facet = fixture.debugElement.query(By.directive(FilterFacetComponent)).componentInstance as FilterFacetComponent;
      expect(facet.facetId).toBe('sec-facet-kind');
      expect(facet.options.map(o => o.label)).toContain('Sign-in failed');
      facet.selectedChange.emit(['LoginFailed']);
      fixture.detectChanges();

      expect(eventsSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery()).toEqual(expect.objectContaining({ kinds: ['LoginFailed'], skip: 0, take: 20 }));
      const chip = el.querySelector<HTMLButtonElement>('.sec-chips .gh-filter-chip')!;
      expect(chip.getAttribute('aria-label')).toBe('Remove filter Kind: Sign-in failed');

      chip.click();
      fixture.detectChanges();
      expect(lastQuery().kinds).toBeUndefined();
      expect(document.activeElement?.id).toBe('sec-search');
    });

    it('refetches page 1 for the search once typing pauses', async () => {
      create();
      eventsSpy.mockClear();

      typeInto(el.querySelector<HTMLInputElement>('#sec-search')!, 'ali');
      expect(eventsSpy).not.toHaveBeenCalled();

      await settle(300);
      fixture.detectChanges();

      expect(eventsSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery()).toEqual(expect.objectContaining({ search: 'ali', skip: 0, take: 20 }));
    });

    it('appends the next page on Show more and focuses the first new card', () => {
      eventsSpy.mockReturnValue(of(page(eventRange(25, 20), 25)));
      create();

      expect(el.querySelectorAll('.sec-card').length).toBe(20);
      expect(textOf(el.querySelector('#sec-status'))).toBe('Showing 20 of 25 events');
      const more = el.querySelector<HTMLButtonElement>('.sec-show-more')!;
      expect(textOf(more)).toBe('Show 5 more');

      eventsSpy.mockReturnValue(of(page(eventRange(5, 5), 25)));
      more.click();
      fixture.detectChanges();

      expect(lastQuery()).toEqual(expect.objectContaining({ skip: 20, take: 20 }));
      expect(el.querySelectorAll('.sec-card').length).toBe(25);
      expect(document.activeElement?.id).toBe('sec-5-title');
      expect(textOf(el.querySelector('#sec-status'))).toBe('Showing 25 of 25 events');
      expect(el.querySelector('.sec-show-more')).toBeNull();
    });
  });

  describe('actions', () => {
    it('blocks the user with a reason and refetches the list', () => {
      const block = vi.spyOn(usersService, 'blockFromAiChat').mockReturnValue(of({} as AdminUserActionResult));
      create();
      eventsSpy.mockClear();

      card(1).querySelector<HTMLButtonElement>('.sec-block')!.click();
      fixture.detectChanges();
      const dialog = el.querySelector<HTMLDialogElement>('dialog.sec-block-dialog')!;
      expect(dialog.open).toBe(true);
      expect(textOf(dialog.querySelector('#sec-block-title'))).toBe('Block Alice?');

      el.querySelector<HTMLButtonElement>('.sec-block-confirm')!.click();
      fixture.detectChanges();
      expect(block).not.toHaveBeenCalled();
      expect(textOf(el.querySelector('.sec-block-error'))).toBe('Enter a reason of 1 to 256 characters.');

      typeInto(el.querySelector<HTMLTextAreaElement>('#sec-block-reason')!, '  Abusive prompts  ');
      el.querySelector<HTMLButtonElement>('.sec-block-confirm')!.click();
      fixture.detectChanges();

      expect(block).toHaveBeenCalledWith('u1', 'Abusive prompts');
      expect(dialog.open).toBe(false);
      expect(eventsSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery().skip).toBe(0);
    });

    it('keeps the dialog open with the server message when the block is refused', () => {
      vi.spyOn(usersService, 'blockFromAiChat').mockReturnValue(throwError(() => new HttpErrorResponse({
        status: 400, error: { message: 'You cannot block yourself.' }
      })));
      create();

      card(1).querySelector<HTMLButtonElement>('.sec-block')!.click();
      typeInto(el.querySelector<HTMLTextAreaElement>('#sec-block-reason')!, 'Test');
      el.querySelector<HTMLButtonElement>('.sec-block-confirm')!.click();
      fixture.detectChanges();

      expect(el.querySelector<HTMLDialogElement>('dialog.sec-block-dialog')!.open).toBe(true);
      expect(textOf(el.querySelector('.sec-block-error'))).toBe('You cannot block yourself.');
    });

    it('unblocks a blocked user and refetches the list', () => {
      eventsSpy.mockReturnValue(of(page([securityEvent(1, { userIsOverseerBlocked: true })])));
      const unblock = vi.spyOn(usersService, 'unblockFromAiChat').mockReturnValue(of({} as AdminUserActionResult));
      create();
      eventsSpy.mockClear();

      card(1).querySelector<HTMLButtonElement>('.sec-unblock')!.click();

      expect(unblock).toHaveBeenCalledWith('u1');
      expect(eventsSpy).toHaveBeenCalledTimes(1);
    });

    it('shows the user\'s AI usage on the AI Telemetry tab from Usage', () => {
      create();
      const requested: string[] = [];
      store.tabRequested$.subscribe(tab => requested.push(tab));
      const usage = vi.spyOn(store, 'requestTelemetryUser');

      const button = card(1).querySelector<HTMLButtonElement>('.sec-usage')!;
      expect(button.getAttribute('aria-label')).toBe('Show the AI usage of Alice');
      button.click();

      expect(usage).toHaveBeenCalledWith('u1', 'Alice');
      expect(store.pendingTelemetryUserId).toBe('u1');
      expect(requested).toEqual(['telemetry']);
    });
  });

  describe('safety identifier lookup', () => {
    function lookUp(identifier: string): void {
      typeInto(el.querySelector<HTMLInputElement>('#sec-safety-id')!, identifier);
      fixture.detectChanges();
      el.querySelector<HTMLFormElement>('.sec-lookup-form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      fixture.detectChanges();
    }

    // The form registers its ngModel control asynchronously, so each test waits for it before typing.
    it('shows the user behind the identifier, with a Usage action', async () => {
      const lookup = vi.spyOn(adminService, 'lookupSafetyIdentifier').mockReturnValue(of({ userId: 'u4', userName: 'Bob' }));
      const usage = vi.spyOn(store, 'requestTelemetryUser');
      create();
      await fixture.whenStable();

      expect(el.querySelector('label[for="sec-safety-id"]')).not.toBeNull();
      lookUp(' sid-123 ');

      expect(lookup).toHaveBeenCalledWith('sid-123');
      expect(textOf(el.querySelector('#sec-lookup-status'))).toBe('Found Bob.');
      el.querySelector<HTMLButtonElement>('.sec-lookup-usage')!.click();
      expect(usage).toHaveBeenCalledWith('u4', 'Bob');
    });

    it('says no user has the identifier on a 404', async () => {
      vi.spyOn(adminService, 'lookupSafetyIdentifier').mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
      create();
      await fixture.whenStable();

      lookUp('unknown');

      expect(textOf(el.querySelector('#sec-lookup-status'))).toBe('No user has this identifier.');
      expect(el.querySelector('#sec-lookup-status')!.getAttribute('role')).toBe('status');
      expect(el.querySelector('.sec-lookup-usage')).toBeNull();
    });
  });

  describe('pending security-user request', () => {
    it('narrows the list to the requested user on init, and Clear drops it', () => {
      store.pendingSecurityUserId = 'u7';
      store.pendingSecurityUserName = 'Mallory';
      create();

      expect(eventsSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery()).toEqual(expect.objectContaining({ userId: 'u7', skip: 0 }));
      expect(store.pendingSecurityUserId).toBeNull();
      expect(store.pendingSecurityUserName).toBeNull();
      expect(textOf(el.querySelector('.sec-user-filter'))).toContain('Showing events of Mallory');

      el.querySelector<HTMLButtonElement>('.sec-user-filter-clear')!.click();
      fixture.detectChanges();

      expect(lastQuery().userId).toBeUndefined();
      expect(el.querySelector('.sec-user-filter')).toBeNull();
    });

    it('narrows the list when a request arrives while the tab is open', () => {
      create();
      eventsSpy.mockClear();

      store.requestSecurityUser('u8', 'Eve');
      fixture.detectChanges();

      expect(eventsSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery()).toEqual(expect.objectContaining({ userId: 'u8', skip: 0 }));
      expect(store.pendingSecurityUserId).toBeNull();
      expect(textOf(el.querySelector('.sec-user-filter'))).toContain('Showing events of Eve');
    });
  });
});
