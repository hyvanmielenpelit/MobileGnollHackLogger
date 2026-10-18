import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import {
  AdminService,
  AiTelemetryUserConfigDto,
  AiTelemetryUserRowDto,
  AiTelemetryUsersPage,
  AiTelemetryUsersQuery
} from '../../../services/admin.service';
import { AdminUserActionResult, AdminUsersService } from '../../../services/admin-users.service';
import { AdminPageStore } from '../../admin-page.store';
import { configureAdminTestBed } from '../../admin.component.testing';
import { TELEMETRY_USERS_SORTS, TELEMETRY_USERS_VIEW_STORAGE_KEY, TelemetryUserRef, TelemetryUsersComponent } from './telemetry-users.component';

const settle = (ms = 0): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

function clearStorage(): void {
  try {
    localStorage.removeItem(TELEMETRY_USERS_VIEW_STORAGE_KEY);
  } catch {
    // Nothing stored.
  }
}

function user(index: number, overrides: Partial<AiTelemetryUserRowDto> = {}): AiTelemetryUserRowDto {
  return {
    userId: `u-${index}`, userName: index === 1 ? 'alice' : `user${index}`,
    requests: 10, chatRequests: 8, titleRequests: 2, benchmarkRequests: 0,
    promptTokens: 1000, outputTokens: 200, estimatedCost: 0.25, pricedRequests: 10,
    peakHourRequests: 6, peakHourStartUtc: '2026-10-12T20:00:00Z', requestsPerActiveHour: 2.5,
    activeDays: 3, lastRequestUtc: '2026-10-12T21:15:00Z', chatsCreated: 4, userMessages: 9,
    limitRefusals: 0, providerRefusals: 0, isOverseerBlocked: false, hasAccountHold: false, configs: [],
    ...overrides
  };
}

function config(overrides: Partial<AiTelemetryUserConfigDto> = {}): AiTelemetryUserConfigDto {
  return {
    configId: 7, configName: 'Claude Chat', requests: 10, estimatedCost: 0.25,
    assignmentId: 3, assignmentEnabled: true,
    dailyChatRequests: 12, maxDailyChatRequests: 50, monthlyChatRequests: 140, maxMonthlyChatRequests: 1000,
    dailyChatTokens: null, maxDailyChatTokens: null, limitSource: 'user',
    ...overrides
  };
}

/** Users with indexes `from` to `from + count - 1`. */
function userRange(from: number, count: number): AiTelemetryUserRowDto[] {
  return Array.from({ length: count }, (_, i) => user(from + i));
}

function page(users: AiTelemetryUserRowDto[], totalUsers = users.length): AiTelemetryUsersPage {
  return { users, totalUsers, securityEventsAvailable: true };
}

describe('TelemetryUsersComponent', () => {
  let fixture: ComponentFixture<TelemetryUsersComponent>;
  let component: TelemetryUsersComponent;
  let el: HTMLElement;
  let adminService: AdminService;
  let usersService: AdminUsersService;
  let store: AdminPageStore;
  let usersSpy: Mock;
  /** What the next users request returns. */
  let nextPage: AiTelemetryUsersPage;

  beforeEach(async () => {
    clearStorage();
    ({ adminService, usersService, store } = await configureAdminTestBed(TelemetryUsersComponent));
    nextPage = page([user(1)]);
    usersSpy = vi.spyOn(adminService, 'getAiTelemetryUsers').mockImplementation(() => of(nextPage));
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

  function create(range: { start?: string; end?: string } = { start: '2026-09-13', end: '2026-10-13' }): void {
    fixture = TestBed.createComponent(TelemetryUsersComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    if (range.start !== undefined) fixture.componentRef.setInput('startDate', range.start);
    if (range.end !== undefined) fixture.componentRef.setInput('endDate', range.end);
    fixture.detectChanges();
  }

  const queries = (): AiTelemetryUsersQuery[] => usersSpy.mock.calls.map(call => call[0] as AiTelemetryUsersQuery);
  const lastQuery = (): AiTelemetryUsersQuery => queries()[queries().length - 1];
  const cards = (): HTMLElement[] => Array.from(el.querySelectorAll<HTMLElement>('article.tl-user-card'));
  const status = (): string => textOf(el.querySelector('#tl-users-status'));

  function typeInto(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  describe('list', () => {
    it('fetches the first page of the range sorted by Highest cost, under the always-shown explanatory line', () => {
      create();

      expect(lastQuery()).toEqual(expect.objectContaining({ startDate: '2026-09-13', endDate: '2026-10-13', sort: 'cost', skip: 0, take: 20 }));
      const select = el.querySelector<HTMLSelectElement>('#tl-users-sort')!;
      expect(textOf(el.querySelector('label[for="tl-users-sort"]'))).toBe('Sort by');
      expect(select.value).toBe('cost');
      expect(textOf(select.selectedOptions[0])).toBe('Highest cost');
      expect(textOf(el.querySelector('.tl-users-busy-note'))).toBe(
        'Busy hours are normal during long play sessions. These figures help you set limits; they are not evidence of misuse.');
      expect(cards().length).toBe(1);
    });

    it('is a list of articles labeled by their titles', () => {
      create();

      const list = el.querySelector('ul.tl-user-cards')!;
      expect(list.getAttribute('role')).toBe('list');
      expect(list.getAttribute('aria-labelledby')).toBe('tl-users-heading');
      const article = cards()[0];
      expect(el.querySelector('#' + article.getAttribute('aria-labelledby'))?.tagName).toBe('H5');
      expect(textOf(article.querySelector('h5'))).toBe('alice');
    });

    it('refetches page 1 for the search once typing pauses', async () => {
      create();
      usersSpy.mockClear();

      typeInto(el.querySelector<HTMLInputElement>('#tl-users-search')!, 'ali');
      expect(usersSpy).not.toHaveBeenCalled();

      await settle(300);
      fixture.detectChanges();

      expect(usersSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery()).toEqual(expect.objectContaining({ search: 'ali', sort: 'cost', skip: 0, take: 20 }));
    });

    it('sends each sort key and refetches page 1', () => {
      create();
      const select = el.querySelector<HTMLSelectElement>('#tl-users-sort')!;
      expect(Array.from(select.options).map(o => textOf(o))).toEqual(
        ['Highest cost', 'Most requests', 'Most tokens', 'Busiest hour', 'Most refusals', 'Most recently active']);

      for (const sort of TELEMETRY_USERS_SORTS) {
        usersSpy.mockClear();
        select.value = sort.id;
        select.dispatchEvent(new Event('change'));
        fixture.detectChanges();

        if (usersSpy.mock.calls.length > 0) {
          expect(lastQuery()).toEqual(expect.objectContaining({ sort: sort.id, skip: 0 }));
        } else {
          // Choosing the order already in use changes nothing.
          expect(sort.id).toBe('cost');
        }
      }
      expect(TELEMETRY_USERS_SORTS.map(s => s.id)).toEqual(['cost', 'requests', 'tokens', 'peakHour', 'refusals', 'lastActive']);
    });

    it('appends the next page on Show more and focuses the first new card', () => {
      nextPage = page(userRange(1, 20), 25);
      create();

      expect(cards().length).toBe(20);
      expect(status()).toBe('Showing 20 of 25 users');
      const more = el.querySelector<HTMLButtonElement>('.tl-users-show-more')!;
      expect(textOf(more)).toBe('Show 5 more');

      nextPage = page(userRange(21, 5), 25);
      more.click();
      fixture.detectChanges();

      expect(lastQuery()).toEqual(expect.objectContaining({ skip: 20, take: 20, sort: 'cost' }));
      expect(cards().length).toBe(25);
      expect(document.activeElement?.id).toBe('tl-users-20-title');
      expect(el.querySelector('.tl-users-show-more')).toBeNull();
    });

    it('refetches page 1 when the range changes', () => {
      create();
      usersSpy.mockClear();

      fixture.componentRef.setInput('startDate', '2026-10-01');
      fixture.detectChanges();

      expect(lastQuery()).toEqual(expect.objectContaining({ startDate: '2026-10-01', skip: 0 }));
    });

    it('says so when the range has no system AI usage', () => {
      nextPage = page([]);
      create();

      expect(textOf(el.querySelector('.tl-users-empty'))).toBe('No system AI usage in this range.');
      expect(el.querySelector('ul.tl-user-cards')).toBeNull();
    });
  });

  describe('a card', () => {
    it('shows each configuration with its limits as 12 / 50 and no limit, the limit source and a disabled assignment', () => {
      nextPage = page([user(1, {
        configs: [
          config(),
          config({ configId: 8, configName: 'Titles', dailyChatRequests: 3, maxDailyChatRequests: null, monthlyChatRequests: 5, maxMonthlyChatRequests: 0, limitSource: 'group', assignmentEnabled: false })
        ]
      })]);
      create();

      const rows = Array.from(cards()[0].querySelectorAll('tbody tr'));
      expect(textOf(rows[0].querySelector('.tl-config-today'))).toBe('12 / 50');
      expect(textOf(rows[0].querySelector('.tl-config-month'))).toBe('140 / 1,000');
      expect(textOf(rows[0].querySelector('.tl-config-source'))).toBe('user');
      expect(rows[0].querySelector('.tl-config-disabled')).toBeNull();
      expect(textOf(rows[1].querySelector('.tl-config-today'))).toContain('no limit');
      expect(textOf(rows[1].querySelector('.tl-config-month'))).toContain('no limit');
      expect(textOf(rows[1].querySelector('.tl-config-source'))).toBe('group');
      expect(textOf(rows[1].querySelector('.tl-config-disabled'))).toBe('disabled');
    });

    it('says Blocked and Deletion hold as words, and the refusals and last active day', () => {
      nextPage = page([user(1, { isOverseerBlocked: true, hasAccountHold: true, limitRefusals: 2, providerRefusals: 1 })]);
      create();

      const kicker = textOf(cards()[0].querySelector('.tl-user-kicker'));
      expect(textOf(cards()[0].querySelector('.tl-user-blocked'))).toBe('Blocked');
      expect(textOf(cards()[0].querySelector('.tl-user-hold'))).toBe('Deletion hold');
      expect(kicker).toContain('3 refusals');
      expect(kicker).toContain('Last active 2026-10-12');
    });

    it('shows a very high busiest hour as plain text, with no badge, color class or warning glyph', () => {
      nextPage = page([user(1, { peakHourRequests: 5000, requestsPerActiveHour: 4.2 })]);
      create();

      const peak = cards()[0].querySelector('.tl-metric-peak')!;
      // Angular strips the whitespace between the term and its descriptions, so the parts are read one by one.
      expect(Array.from(peak.children).map(c => textOf(c)).join(' '))
        .toBe('Busiest hour 5,000 requests typical 4.2 per active hour');
      expect(peak.querySelector('svg, .gh-tag, [class*="badge"], [class*="warning"], [class*="danger"]')).toBeNull();
      expect(Array.from(peak.querySelectorAll('*')).every(node => !/badge|warn|danger|alert/.test(node.className.toString()))).toBe(true);
    });

    it('shows the requests split and an unpriced qualifier', () => {
      nextPage = page([user(1, { requests: 10, pricedRequests: 7 })]);
      create();

      const metrics = textOf(cards()[0].querySelector('.tl-user-metrics'));
      expect(metrics).toContain('chat 8 · title 2');
      expect(metrics).toContain('3 unpriced');
    });

    it('lays the nine metrics out in 9, 5, 5 and 2 columns as the list narrows', () => {
      create();

      const columns = (width: string): number => {
        el.style.width = width;
        fixture.detectChanges();
        void el.offsetWidth;
        return getComputedStyle(cards()[0].querySelector('.tl-user-metrics')!).gridTemplateColumns.split(' ').length;
      };

      expect(['1800px', '1300px', '900px', '380px'].map(columns)).toEqual([9, 5, 5, 2]);
    });
  });

  describe('actions', () => {
    it('names the user in the actions group and its buttons', () => {
      create();

      const group = cards()[0].querySelector('[role="group"]')!;
      expect(group.getAttribute('aria-label')).toBe('Actions for alice');
      for (const name of ['Usage charts of alice', 'Manage the limits of alice', 'Block alice from the AI chat']) {
        const button = group.querySelector<HTMLButtonElement>(`button[aria-label="${name}"]`)!;
        expect(button.hasAttribute('title')).toBe(false);
        expect(el.querySelector('#' + button.getAttribute('interestfor'))?.getAttribute('popover')).toBe('hint');
      }
    });

    it('emits the user from Usage charts', () => {
      create();
      const emitted: TelemetryUserRef[] = [];
      component.usageCharts.subscribe(ref => emitted.push(ref));

      el.querySelector<HTMLButtonElement>('button[aria-label="Usage charts of alice"]')!.click();

      expect(emitted).toEqual([{ userId: 'u-1', userName: 'alice' }]);
    });

    it('Manage limits asks the Users tab to manage the user and selects it', () => {
      create();
      el.querySelector<HTMLButtonElement>('button[aria-label="Manage the limits of alice"]')!.click();

      expect(store.pendingManageUser).toEqual({ userId: 'u-1', userName: 'alice' });
      expect(store.activeTab).toBe('users');
    });

    it('Block asks for a reason, blocks with it and refetches', () => {
      const blockSpy = vi.spyOn(usersService, 'blockFromAiChat').mockReturnValue(of({} as AdminUserActionResult));
      create();
      usersSpy.mockClear();

      el.querySelector<HTMLButtonElement>('button[aria-label="Block alice from the AI chat"]')!.click();
      fixture.detectChanges();

      const dialog = el.querySelector('app-admin-reason-dialog dialog') as HTMLDialogElement;
      expect(dialog.open).toBe(true);
      expect(dialog.querySelector('textarea')?.getAttribute('maxlength')).toBe('256');
      const field = dialog.querySelector('textarea') as HTMLTextAreaElement;
      field.value = 'Too many requests';
      field.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      (dialog.querySelector('.reason-confirm') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(blockSpy).toHaveBeenCalledWith('u-1', 'Too many requests');
      expect(usersSpy).toHaveBeenCalledWith(expect.objectContaining({ skip: 0 }));
    });

    it('Unblock asks for confirmation, unblocks and refetches', () => {
      const unblockSpy = vi.spyOn(usersService, 'unblockFromAiChat').mockReturnValue(of({} as AdminUserActionResult));
      nextPage = page([user(1, { isOverseerBlocked: true })]);
      create();
      usersSpy.mockClear();

      el.querySelector<HTMLButtonElement>('button[aria-label="Unblock alice"]')!.click();
      fixture.detectChanges();

      const dialog = el.querySelector('app-admin-confirm-dialog dialog') as HTMLDialogElement;
      expect(dialog.open).toBe(true);
      const confirm = Array.from(dialog.querySelectorAll('button')).find(b => b.textContent?.trim() === 'Unblock') as HTMLButtonElement;
      confirm.click();
      fixture.detectChanges();

      expect(unblockSpy).toHaveBeenCalledWith('u-1');
      expect(usersSpy).toHaveBeenCalled();
    });
  });

  describe('one user from a Usage link', () => {
    it('shows only that user with its status line, and Clear drops the filter', () => {
      nextPage = page(userRange(1, 3), 3);
      create();

      nextPage = page([user(5, { userName: 'eve' })]);
      component.filterToUser('u-5', 'eve');
      fixture.detectChanges();

      expect(lastQuery()).toEqual(expect.objectContaining({ userId: 'u-5', skip: 0 }));
      expect(lastQuery().search).toBeUndefined();
      expect(status()).toBe('Showing 1 user — eve');
      expect(cards().length).toBe(1);
      expect(el.querySelector('#tl-users-search')).toBeNull();

      nextPage = page(userRange(1, 3), 3);
      el.querySelector<HTMLButtonElement>('.tl-users-filter-clear')!.click();
      fixture.detectChanges();

      expect(lastQuery().userId).toBeUndefined();
      expect(status()).toBe('Showing 3 of 3 users');
      expect(el.querySelector('.tl-users-filter')).toBeNull();
    });

    it('applies a filter set before the list exists to the first fetch', () => {
      fixture = TestBed.createComponent(TelemetryUsersComponent);
      component = fixture.componentInstance;
      el = fixture.nativeElement as HTMLElement;
      component.filterToUser('u-5', 'eve');
      expect(usersSpy).not.toHaveBeenCalled();

      fixture.detectChanges();

      expect(usersSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery()).toEqual(expect.objectContaining({ userId: 'u-5' }));
    });
  });
});
