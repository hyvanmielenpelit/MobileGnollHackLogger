import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import {
  AdminService,
  AiTelemetryKeySource,
  AiTelemetryUserConfigDto,
  AiTelemetryUserRowDto,
  AiTelemetryUsersPage,
  AiTelemetryUsersQuery
} from '../../../services/admin.service';
import { AdminUserActionResult, AdminUsersService } from '../../../services/admin-users.service';
import { AdminPageStore } from '../../admin-page.store';
import { configureAdminTestBed } from '../../admin.component.testing';
import { TELEMETRY_USERS_VIEW_STORAGE_KEY, TelemetryUserModelsRef, TelemetryUserRef, TelemetryUsersComponent } from './telemetry-users.component';

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
    requests: 10, systemRequests: 10, userKeyRequests: 0, systemEstimatedCost: 0.25, userKeyEstimatedCost: 0,
    chatRequests: 8, titleRequests: 2, benchmarkRequests: 0,
    promptTokens: 1000, outputTokens: 200, estimatedCost: 0.25, pricedRequests: 10,
    peakHourRequests: 6, peakHourStartUtc: '2026-10-12T20:00:00Z', requestsPerActiveHour: 2.5,
    activeDays: 3, lastRequestUtc: '2026-10-12T21:15:00Z', chatsCreated: 4, userMessages: 9,
    limitRefusals: 0, providerRefusals: 0, isOverseerBlocked: false, hasAccountHold: false, configs: [],
    ...overrides
  };
}

function config(overrides: Partial<AiTelemetryUserConfigDto> = {}): AiTelemetryUserConfigDto {
  return {
    keySource: 'system', configId: 7, configName: 'Claude Chat', requests: 10, estimatedCost: 0.25,
    assignmentId: 3, assignmentEnabled: true,
    dailyChatRequests: 12, maxDailyChatRequests: 50, monthlyChatRequests: 140, maxMonthlyChatRequests: 1000,
    dailyChatTokens: null, maxDailyChatTokens: null, limitSource: 'user',
    ...overrides
  };
}

/** A user's own model: no assignment and no limits. */
function ownKeyConfig(overrides: Partial<AiTelemetryUserConfigDto> = {}): AiTelemetryUserConfigDto {
  return {
    keySource: 'user', configId: 7, configName: 'My Gemini', requests: 3, estimatedCost: 0.12,
    assignmentId: null, assignmentEnabled: null,
    dailyChatRequests: null, maxDailyChatRequests: null, monthlyChatRequests: null, maxMonthlyChatRequests: null,
    dailyChatTokens: null, maxDailyChatTokens: null, limitSource: 'ownKey',
    ...overrides
  };
}

/** Users with indexes `from` to `from + count - 1`. */
function userRange(from: number, count: number): AiTelemetryUserRowDto[] {
  return Array.from({ length: count }, (_, i) => user(from + i));
}

function page(users: AiTelemetryUserRowDto[], totalUsers = users.length, keySource: AiTelemetryKeySource = 'system'): AiTelemetryUsersPage {
  return { users, totalUsers, securityEventsAvailable: true, keySource };
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
      el.querySelectorAll<HTMLElement>('[popover]').forEach(popover => {
        try { popover.hidePopover(); } catch { /* already closed */ }
      });
      fixture.destroy();
    }
    clearStorage();
  });

  function create(range: { start?: string; end?: string } = { start: '2026-09-13', end: '2026-10-13' }, keys?: AiTelemetryKeySource): void {
    fixture = TestBed.createComponent(TelemetryUsersComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    if (range.start !== undefined) fixture.componentRef.setInput('startDate', range.start);
    if (range.end !== undefined) fixture.componentRef.setInput('endDate', range.end);
    if (keys !== undefined) fixture.componentRef.setInput('keys', keys);
    fixture.detectChanges();
  }

  const queries = (): AiTelemetryUsersQuery[] => usersSpy.mock.calls.map(call => call[0] as AiTelemetryUsersQuery);
  const lastQuery = (): AiTelemetryUsersQuery => queries()[queries().length - 1];
  const cards = (): HTMLElement[] => Array.from(el.querySelectorAll<HTMLElement>('article.tl-user-card'));
  const status = (): string => textOf(el.querySelector('#tl-users-status'));
  const q = <T extends HTMLElement = HTMLElement>(selector: string): T | null => el.querySelector<T>(selector);

  function typeInto(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  function pressEscape(target: HTMLElement): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  describe('list', () => {
    it('fetches the first page of the range and key source, highest cost first, under the scope and busy-hours lines', () => {
      create();

      expect(lastQuery()).toEqual(expect.objectContaining({
        startDate: '2026-09-13', endDate: '2026-10-13', keys: 'system', sort: 'cost', direction: 'desc', skip: 0, take: 20
      }));
      expect(textOf(q('#tl-users-sort-trigger'))).toBe('Sort: Est. cost, highest first');
      expect(textOf(q('.tl-users-busy-note'))).toBe(
        'Busy hours are normal during long play sessions. These figures help you set limits; they are not evidence of misuse.');
      expect(cards().length).toBe(1);
    });

    it('has no heading of its own; the scope line comes first and the status line sits under the filter bar', () => {
      create();

      expect(q('#tl-users-heading')).toBeNull();
      expect(q('h4')).toBeNull();
      const scope = q('p.tl-users-scope')!;
      expect(scope.parentElement).toBe(el);
      expect(el.firstElementChild).toBe(scope);
      const bar = q('.gh-filter-bar')!;
      const statusLine = q('p.gh-list-status.tl-users-status')!;
      expect(statusLine.getAttribute('role')).toBe('status');
      expect(bar.compareDocumentPosition(statusLine) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(bar.contains(statusLine)).toBe(false);
    });

    it('is a list named Users of articles labeled by their titles', () => {
      create();

      const list = q('ul.tl-user-cards')!;
      expect(list.getAttribute('role')).toBe('list');
      expect(list.getAttribute('aria-label')).toBe('Users');
      expect(list.hasAttribute('aria-labelledby')).toBe(false);
      const article = cards()[0];
      expect(el.querySelector('#' + article.getAttribute('aria-labelledby'))?.tagName).toBe('H5');
      expect(textOf(article.querySelector('h5'))).toBe('alice');
    });

    it('refetches page 1 for the search once typing pauses', async () => {
      create();
      usersSpy.mockClear();

      typeInto(q<HTMLInputElement>('#tl-users-search')!, 'ali');
      expect(usersSpy).not.toHaveBeenCalled();

      await settle(300);
      fixture.detectChanges();

      expect(usersSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery()).toEqual(expect.objectContaining({ search: 'ali', sort: 'cost', direction: 'desc', skip: 0, take: 20 }));
    });

    it('Escape with text clears the search at once and refetches; with none it is left alone', async () => {
      create();
      const search = q<HTMLInputElement>('#tl-users-search')!;
      typeInto(search, 'ali');
      await settle(300);
      fixture.detectChanges();
      usersSpy.mockClear();

      const event = pressEscape(search);

      expect(event.defaultPrevented).toBe(true);
      expect(search.value).toBe('');
      expect(usersSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery().search).toBeUndefined();

      usersSpy.mockClear();
      expect(pressEscape(search).defaultPrevented).toBe(false);
      expect(usersSpy).not.toHaveBeenCalled();
    });

    it('offers Clear filters while the field has text; it clears the search and focuses the field', async () => {
      create();
      expect(q('#tl-users-clear')).toBeNull();
      const search = q<HTMLInputElement>('#tl-users-search')!;
      typeInto(search, 'ali');
      await settle(300);
      fixture.detectChanges();
      usersSpy.mockClear();

      const clear = q<HTMLButtonElement>('#tl-users-clear')!;
      expect(textOf(clear)).toBe('Clear filters');
      clear.click();
      fixture.detectChanges();

      expect(search.value).toBe('');
      expect(lastQuery().search).toBeUndefined();
      expect(q('#tl-users-clear')).toBeNull();
      expect(document.activeElement).toBe(search);
    });

    it('appends the next page on Show more and focuses the first new card', () => {
      nextPage = page(userRange(1, 20), 25);
      create();

      expect(cards().length).toBe(20);
      expect(status()).toBe('Showing 20 of 25 users');
      const more = q<HTMLButtonElement>('.tl-users-show-more')!;
      expect(textOf(more)).toBe('Show 5 more');

      nextPage = page(userRange(21, 5), 25);
      more.click();
      fixture.detectChanges();

      expect(lastQuery()).toEqual(expect.objectContaining({ skip: 20, take: 20, sort: 'cost', direction: 'desc' }));
      expect(cards().length).toBe(25);
      expect(document.activeElement?.id).toBe('tl-users-20-title');
      expect(q('.tl-users-show-more')).toBeNull();
    });

    it('refetches page 1 when the range changes', () => {
      create();
      usersSpy.mockClear();

      fixture.componentRef.setInput('startDate', '2026-10-01');
      fixture.detectChanges();

      expect(usersSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery()).toEqual(expect.objectContaining({ startDate: '2026-10-01', skip: 0 }));
    });

    it('sends the Keys filter and refetches page 1 when it changes', () => {
      create(undefined, 'user');
      expect(lastQuery()).toEqual(expect.objectContaining({ keys: 'user' }));
      usersSpy.mockClear();

      fixture.componentRef.setInput('keys', 'both');
      fixture.detectChanges();

      expect(usersSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery()).toEqual(expect.objectContaining({ keys: 'both', skip: 0, take: 20 }));
    });

    it('says what the counts cover for each key source', () => {
      create();
      const scope = () => textOf(q('.tl-users-scope'));
      expect(scope()).toBe('System AI configurations. Chats, messages and refusals count every key.');

      fixture.componentRef.setInput('keys', 'user');
      fixture.detectChanges();
      expect(scope()).toBe("Users' own API keys. Limits apply to system AI configurations only; chats, messages and refusals count every key.");

      fixture.componentRef.setInput('keys', 'both');
      fixture.detectChanges();
      expect(scope()).toBe("System AI configurations and users' own keys. Chats, messages and refusals count every key.");
    });

    it('says so when the range has no usage', () => {
      nextPage = page([]);
      create();

      expect(textOf(q('.tl-users-empty'))).toBe('No system AI usage in this range.');
      expect(q('ul.tl-user-cards')).toBeNull();
    });
  });

  describe('Sort popover', () => {
    it('sends the field and the order, labels the trigger and stores the sort as version 2', () => {
      create();
      expect(Array.from(el.querySelectorAll('.tl-sort-fields .gh-facet-option-label')).map(textOf)).toEqual(
        ['Est. cost', 'Requests', 'Tokens', 'Busiest hour', 'Refusals', 'Last active', 'User name']);
      expect(q('#tl-users-sort-reset')?.getAttribute('aria-disabled')).toBe('true');
      usersSpy.mockClear();

      q<HTMLInputElement>('#tl-users-sort-field-requests')!.click();
      fixture.detectChanges();

      expect(usersSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery()).toEqual(expect.objectContaining({ sort: 'requests', direction: 'desc', skip: 0, take: 20 }));
      expect(textOf(q('#tl-users-sort-trigger'))).toBe('Sort: Requests, highest first');

      q<HTMLInputElement>('#tl-users-sort-direction-asc')!.click();
      fixture.detectChanges();

      expect(lastQuery()).toEqual(expect.objectContaining({ sort: 'requests', direction: 'asc', skip: 0 }));
      expect(textOf(q('#tl-users-sort-trigger'))).toBe('Sort: Requests, lowest first');
      expect(JSON.parse(localStorage.getItem(TELEMETRY_USERS_VIEW_STORAGE_KEY)!)).toEqual({
        version: 2, sort: { field: 'requests', direction: 'asc' }
      });
      expect(q('#tl-users-sort-reset')?.hasAttribute('aria-disabled')).toBe(false);

      usersSpy.mockClear();
      q<HTMLButtonElement>('#tl-users-sort-reset')!.click();
      fixture.detectChanges();

      expect(lastQuery()).toEqual(expect.objectContaining({ sort: 'cost', direction: 'desc', skip: 0 }));
      expect(textOf(q('#tl-users-sort-trigger'))).toBe('Sort: Est. cost, highest first');
    });

    it('offers A–Z and Z–A for User name, and Newest first and Oldest first for Last active', () => {
      create();
      const directions = () => Array.from(el.querySelectorAll('.tl-sort-segment')).map(textOf);
      expect(directions()).toEqual(['Highest first', 'Lowest first']);

      q<HTMLInputElement>('#tl-users-sort-field-name')!.click();
      fixture.detectChanges();
      expect(directions()).toEqual(['Z–A', 'A–Z']);
      expect(textOf(q('#tl-users-sort-trigger'))).toBe('Sort: User name, Z–A');

      q<HTMLInputElement>('#tl-users-sort-direction-asc')!.click();
      fixture.detectChanges();
      expect(lastQuery()).toEqual(expect.objectContaining({ sort: 'name', direction: 'asc' }));
      expect(textOf(q('#tl-users-sort-trigger'))).toBe('Sort: User name, A–Z');

      q<HTMLInputElement>('#tl-users-sort-field-lastActive')!.click();
      fixture.detectChanges();
      expect(directions()).toEqual(['Newest first', 'Oldest first']);
    });

    it('names its radios and its Order group', () => {
      create();
      const fields = Array.from(el.querySelectorAll<HTMLInputElement>('#tl-users-sort-panel input[name="tl-users-sort-field"]'));
      expect(fields.length).toBe(7);
      const group = q('#tl-users-sort-panel [role="radiogroup"]')!;
      expect(textOf(el.querySelector('#' + group.getAttribute('aria-labelledby')))).toBe('Order');
      expect(group.querySelectorAll('input[name="tl-users-sort-direction"]').length).toBe(2);
      expect(q('#tl-users-sort-trigger')?.getAttribute('popovertarget')).toBe('tl-users-sort-panel');
    });

    it('reads a version 1 Sort by record as that field, descending', () => {
      localStorage.setItem(TELEMETRY_USERS_VIEW_STORAGE_KEY, JSON.stringify({ version: 1, sort: 'lastActive' }));
      create();

      expect(lastQuery()).toEqual(expect.objectContaining({ sort: 'lastActive', direction: 'desc' }));
      expect(textOf(q('#tl-users-sort-trigger'))).toBe('Sort: Last active, newest first');
      expect(q<HTMLInputElement>('#tl-users-sort-field-lastActive')!.checked).toBe(true);
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
      expect(Array.from(rows[0].querySelectorAll('td')).map(td => td.getAttribute('data-label'))).toEqual(
        ['Requests', 'Est. cost', 'Today', 'This month', 'Limits from']);
    });

    it('marks an own-key line own key, with dashes for Today and This month', () => {
      nextPage = page([user(1, { configs: [ownKeyConfig()] })], 1, 'user');
      create(undefined, 'user');

      const row = cards()[0].querySelector('tbody tr')!;
      expect(textOf(row.querySelector('th .tl-config-own-key'))).toBe('own key');
      expect(textOf(row.querySelector('.tl-config-today'))).toBe('—');
      expect(textOf(row.querySelector('.tl-config-month'))).toBe('—');
      expect(textOf(row.querySelector('.tl-config-source'))).toBe('own key');
    });

    it('shows a configuration\'s thinking level, reasoning, provider and service tier badges in its row header', () => {
      nextPage = page([user(1, {
        configs: [
          config({ provider: 'OpenAI', thinkingLevel: 'high', reasoningMode: 'Summary', serviceTier: 'flex', assignmentEnabled: false }),
          config({ configId: 8, configName: 'Gone' })
        ]
      })]);
      create();

      const headers = Array.from(cards()[0].querySelectorAll('tbody th'));
      const badges = headers[0].querySelector('.tl-config-badges')!;
      expect(textOf(badges.querySelector('.thinking-badge'))).toBe('thinking level High');
      expect(textOf(badges.querySelector('.reasoning-badge'))).toBe('reasoning mode Summary');
      expect(textOf(badges.querySelector('app-provider-badge'))).toBe('OpenAI');
      expect(textOf(badges.querySelector('.config-badge'))).toBe('service tier Flex');
      expect(textOf(badges.querySelector('.tl-config-disabled'))).toBe('disabled');

      expect(headers[1].querySelector('.thinking-badge')).toBeNull();
      expect(headers[1].querySelector('app-provider-badge')).toBeNull();
      expect(headers[1].querySelector('.config-badge')).toBeNull();
      expect(textOf(headers[1])).toBe('Gone');
    });

    it('titles the card with the user name, focusable from script', () => {
      nextPage = page([user(1)]);
      create();

      const title = cards()[0].querySelector('h5.tl-user-title')!;
      expect(title.id).toBe('tl-users-0-title');
      expect(title.getAttribute('tabindex')).toBe('-1');
      expect(textOf(title)).toBe('alice');
    });

    it('renders both lines when a system configuration and an own model share a configId', () => {
      nextPage = page([user(1, { configs: [config({ configId: 7 }), ownKeyConfig({ configId: 7 })] })], 1, 'both');
      create(undefined, 'both');

      const rows = Array.from(cards()[0].querySelectorAll('tbody tr'));
      expect(rows.map(r => r.getAttribute('data-key-source'))).toEqual(['system', 'user']);
      expect(rows.map(r => textOf(r.querySelector('th')))).toEqual(['Claude Chat', 'My Gemini own key']);
    });

    it('puts the configurations in a focusable region labeled by its caption', () => {
      nextPage = page([user(1, { configs: [config()] })]);
      create();

      const region = cards()[0].querySelector<HTMLElement>('.tl-user-configs')!;
      expect(region.getAttribute('role')).toBe('region');
      expect(region.tabIndex).toBe(0);
      const caption = el.querySelector('#' + region.getAttribute('aria-labelledby'))!;
      expect(caption.tagName).toBe('CAPTION');
      expect(textOf(caption)).toBe('Configurations alice used');
      region.focus();
      expect(document.activeElement).toBe(region);
    });

    it('says Blocked, Deletion hold and the refusals as outlined badges, and the last active day under the title', () => {
      nextPage = page([user(1, { isOverseerBlocked: true, hasAccountHold: true, limitRefusals: 2, providerRefusals: 1 })]);
      create();

      const badges = cards()[0].querySelector('ul.gh-model-card-badges')!;
      expect(badges.getAttribute('role')).toBe('list');
      expect(badges.getAttribute('aria-label')).toBe('Account state');
      expect(Array.from(badges.querySelectorAll('li')).map(textOf)).toEqual(['Blocked', 'Deletion hold', '3 refusals']);
      expect(Array.from(badges.querySelectorAll('li > span')).every(span => span.classList.contains('gh-tag'))).toBe(true);
      expect(textOf(cards()[0].querySelector('.tl-user-meta'))).toBe('Last active 2026-10-12');
    });

    it('renders no badge row when there is nothing to say, and No request in this range without a last request', () => {
      nextPage = page([user(1, { lastRequestUtc: null })]);
      create();

      expect(cards()[0].querySelector('.gh-model-card-badges')).toBeNull();
      expect(textOf(cards()[0].querySelector('.tl-user-meta'))).toBe('No request in this range');
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

    it('shows the requests split, benchmark requests when there are any, and an unpriced qualifier', () => {
      nextPage = page([user(1, { requests: 10, pricedRequests: 7 }), user(2, { benchmarkRequests: 3 })], 2);
      create();

      const notes = (card: HTMLElement, metric: string) =>
        Array.from(card.querySelectorAll(`.${metric} .tl-metric-note`)).map(textOf);
      expect(notes(cards()[0], 'tl-metric-requests')).toEqual(['chat 8 · title 2']);
      expect(notes(cards()[0], 'tl-metric-cost')).toEqual(['3 unpriced']);
      expect(notes(cards()[1], 'tl-metric-requests')).toEqual(['chat 8 · title 2 · benchmark 3']);
    });

    it('splits requests and cost by key source under Both only', () => {
      const split = user(1, { requests: 10, systemRequests: 7, userKeyRequests: 3, userKeyEstimatedCost: 0.12 });
      nextPage = page([split], 1, 'both');
      create(undefined, 'both');

      const metric = (name: string) => Array.from(cards()[0].querySelectorAll(`.${name} .tl-metric-note`)).map(textOf);
      expect(metric('tl-metric-requests')).toEqual(['chat 8 · title 2', 'system 7 · own key 3']);
      expect(metric('tl-metric-cost')).toEqual(['own key $0.1200']);

      nextPage = page([split], 1, 'system');
      fixture.componentRef.setInput('keys', 'system');
      fixture.detectChanges();
      expect(metric('tl-metric-requests')).toEqual(['chat 8 · title 2']);
      expect(metric('tl-metric-cost')).toEqual([]);
    });

    it('lays the nine metrics out in 9, 5, 5, 3 and 2 columns as the list narrows, beside the head from 68 rem', () => {
      nextPage = page([user(1, { configs: [config()] })]);
      create();

      const measure = (width: string) => {
        el.style.width = width;
        fixture.componentRef.changeDetectorRef.markForCheck();
        fixture.detectChanges();
        void el.offsetWidth;
        const card = cards()[0];
        const head = card.querySelector('.tl-user-head')!.getBoundingClientRect();
        const metrics = card.querySelector<HTMLElement>('.tl-metrics')!;
        const configs = card.querySelector('.tl-user-configs')!.getBoundingClientRect();
        return {
          columns: getComputedStyle(metrics).gridTemplateColumns.split(' ').length,
          beside: metrics.getBoundingClientRect().left >= head.right,
          configsBelow: configs.top >= Math.max(head.bottom, metrics.getBoundingClientRect().bottom) - 1
        };
      };

      expect(['1800px', '1300px', '900px', '600px', '380px'].map(w => measure(w).columns)).toEqual([9, 5, 5, 3, 2]);
      expect(['1800px', '1200px', '1000px', '600px'].map(w => measure(w).beside)).toEqual([true, true, false, false]);
      expect(['1800px', '600px'].map(w => measure(w).configsBelow)).toEqual([true, true]);
    });

    it('stacks the configurations table under 36 rem of its own width, each value led by its column name', () => {
      nextPage = page([user(1, { configs: [config()] })]);
      create();
      const display = (width: string) => {
        el.style.width = width;
        void el.offsetWidth;
        const td = cards()[0].querySelector('tbody td')!;
        return { td: getComputedStyle(td).display, before: getComputedStyle(td, '::before').content };
      };

      expect(display('1000px').td).toBe('table-cell');
      const narrow = display('400px');
      expect(narrow.td).toBe('block');
      // The computed value is the label or the attr() expression, by engine; either way the label is generated.
      expect(narrow.before).toMatch(/Requests|attr\(data-label\)/);
    });
  });

  describe('actions', () => {
    it('sits in a group named for the user at the end of the title row: Usage by model, Usage charts, Manage limits, Block', () => {
      create();

      const row = cards()[0].querySelector('.tl-user-title-row')!;
      expect(row.firstElementChild?.tagName).toBe('H5');
      const group = row.querySelector('[role="group"]')!;
      expect(group.parentElement).toBe(row);
      expect(group.getAttribute('aria-label')).toBe('Actions for alice');
      const names = Array.from(group.querySelectorAll('button')).map(b => b.getAttribute('aria-label'));
      expect(names).toEqual(['Usage by model: alice', 'Usage charts of alice', 'Manage the limits of alice', 'Block alice from the AI chat']);
      for (const name of names) {
        const button = group.querySelector<HTMLButtonElement>(`button[aria-label="${name}"]`)!;
        expect(button.hasAttribute('title')).toBe(false);
        expect(el.querySelector('#' + button.getAttribute('interestfor'))?.getAttribute('popover')).toBe('hint');
      }
    });

    it('Usage by model opens a dialog, says so in its tooltip and emits the user with the button as opener', () => {
      create();
      const emitted: TelemetryUserModelsRef[] = [];
      component.usageByModel.subscribe(ref => emitted.push(ref));

      const button = q<HTMLButtonElement>('button[aria-label="Usage by model: alice"]')!;
      expect(button.getAttribute('aria-haspopup')).toBe('dialog');
      expect(textOf(el.querySelector('#' + button.getAttribute('interestfor')))).toBe('Usage by model');
      expect(button.querySelectorAll('svg path').length).toBe(2);
      button.click();

      expect(emitted.length).toBe(1);
      expect(emitted[0]).toEqual(expect.objectContaining({ userId: 'u-1', userName: 'alice' }));
      expect(emitted[0].opener).toBe(button);
    });

    it('emits the user from Usage charts', () => {
      create();
      const emitted: TelemetryUserRef[] = [];
      component.usageCharts.subscribe(ref => emitted.push(ref));

      q<HTMLButtonElement>('button[aria-label="Usage charts of alice"]')!.click();

      expect(emitted).toEqual([{ userId: 'u-1', userName: 'alice' }]);
    });

    it('Manage limits asks the Users tab to manage the user and selects it', () => {
      create();
      q<HTMLButtonElement>('button[aria-label="Manage the limits of alice"]')!.click();

      expect(store.pendingManageUser).toEqual({ userId: 'u-1', userName: 'alice' });
      expect(store.activeTab).toBe('users');
    });

    it('Block asks for a reason, blocks with it and refetches', () => {
      const blockSpy = vi.spyOn(usersService, 'blockFromAiChat').mockReturnValue(of({} as AdminUserActionResult));
      create();
      usersSpy.mockClear();

      q<HTMLButtonElement>('button[aria-label="Block alice from the AI chat"]')!.click();
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

      q<HTMLButtonElement>('button[aria-label="Unblock alice"]')!.click();
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
    it('shows only that user with its status line, and Clear drops the filter and focuses the search field', () => {
      nextPage = page(userRange(1, 3), 3);
      create();

      nextPage = page([user(5, { userName: 'eve' })]);
      component.filterToUser('u-5', 'eve');
      fixture.detectChanges();

      expect(lastQuery()).toEqual(expect.objectContaining({ userId: 'u-5', skip: 0 }));
      expect(lastQuery().search).toBeUndefined();
      expect(status()).toBe('Showing 1 user — eve');
      expect(cards().length).toBe(1);
      expect(q('#tl-users-search')).toBeNull();

      nextPage = page(userRange(1, 3), 3);
      q<HTMLButtonElement>('.tl-users-filter-clear')!.click();
      fixture.detectChanges();

      expect(lastQuery().userId).toBeUndefined();
      expect(status()).toBe('Showing 3 of 3 users');
      expect(q('.tl-users-filter')).toBeNull();
      expect(document.activeElement?.id).toBe('tl-users-search');
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

    it('takes a request pending in the page store when it starts, and the first fetch carries it', () => {
      store.pendingTelemetryUserId = 'u-5';
      store.pendingTelemetryUserName = 'eve';
      nextPage = page([user(5, { userName: 'eve' })]);

      create();

      expect(usersSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery()).toEqual(expect.objectContaining({ userId: 'u-5', skip: 0 }));
      expect(store.pendingTelemetryUserId).toBeNull();
      expect(store.pendingTelemetryUserName).toBeNull();
      expect(status()).toBe('Showing 1 user — eve');
    });

    it('names a pending user without a name by the id', () => {
      store.pendingTelemetryUserId = 'u-5';
      store.pendingTelemetryUserName = null;

      create();

      expect(textOf(q('#tl-users-filter strong'))).toBe('u-5');
    });

    it('filters to a user requested while alive and focuses the filter line', () => {
      nextPage = page(userRange(1, 3), 3);
      create();
      usersSpy.mockClear();

      nextPage = page([user(5, { userName: 'eve' })]);
      store.requestTelemetryUser('u-5', 'eve');
      fixture.detectChanges();

      expect(usersSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery()).toEqual(expect.objectContaining({ userId: 'u-5', skip: 0 }));
      expect(store.pendingTelemetryUserId).toBeNull();
      const filter = q('#tl-users-filter')!;
      expect(filter.getAttribute('tabindex')).toBe('-1');
      expect(document.activeElement).toBe(filter);
    });

    it('stops listening for requests once destroyed', () => {
      create();
      const destroyed = fixture;
      fixture = undefined as unknown as ComponentFixture<TelemetryUsersComponent>;
      destroyed.destroy();
      usersSpy.mockClear();

      store.requestTelemetryUser('u-5', 'eve');

      expect(usersSpy).not.toHaveBeenCalled();
      expect(store.pendingTelemetryUserId).toBe('u-5');
    });
  });
});
