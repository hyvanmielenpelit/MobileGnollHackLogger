import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import {
  AdminService,
  AiTelemetryGroupConfigDto,
  AiTelemetryGroupRowDto,
  AiTelemetryGroupsDto,
  AiTelemetryGroupsQuery,
  AiTelemetryKeySource
} from '../../../services/admin.service';
import { AdminPageStore } from '../../admin-page.store';
import { configureAdminTestBed } from '../../admin.component.testing';
import {
  TELEMETRY_GROUPS_VIEW_STORAGE_KEY,
  TelemetryGroupModelsRef,
  TelemetryGroupRef,
  TelemetryGroupsComponent
} from './telemetry-groups.component';

const settle = (ms = 0): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

function clearStorage(): void {
  try {
    localStorage.removeItem(TELEMETRY_GROUPS_VIEW_STORAGE_KEY);
  } catch {
    // Nothing stored.
  }
}

function group(groupId: number | null, groupName: string, overrides: Partial<AiTelemetryGroupRowDto> = {}): AiTelemetryGroupRowDto {
  return {
    groupId, groupName, memberCount: 12, activeMembers: 9, blockedMembers: 0,
    requests: 10, systemRequests: 10, userKeyRequests: 0, systemEstimatedCost: 0.25, userKeyEstimatedCost: 0,
    chatRequests: 8, titleRequests: 2, benchmarkRequests: 0,
    promptTokens: 1000, outputTokens: 200, estimatedCost: 0.25, pricedRequests: 10,
    peakHourRequests: 6, peakHourStartUtc: '2026-10-12T20:00:00Z', requestsPerActiveHour: 2.5,
    activeDays: 3, lastRequestUtc: '2026-10-12T21:15:00Z', chatsCreated: 4, userMessages: 9,
    limitRefusals: 0, providerRefusals: 0, configs: [],
    ...overrides
  };
}

function config(overrides: Partial<AiTelemetryGroupConfigDto> = {}): AiTelemetryGroupConfigDto {
  return {
    keySource: 'system', configId: 7, configName: 'Claude Chat', requests: 10, estimatedCost: 0.25,
    assignmentId: 3, assignmentEnabled: true, grantedByGroup: true,
    dailyChatRequests: 12, maxDailyChatRequests: 50, monthlyChatRequests: 140, maxMonthlyChatRequests: 1000,
    dailyChatTokens: null, maxDailyChatTokens: null,
    ...overrides
  };
}

/** Groups `Group {from}` to `Group {from + count - 1}`, all alike, so they sort by name. */
function groupRange(from: number, count: number): AiTelemetryGroupRowDto[] {
  return Array.from({ length: count }, (_, i) => group(from + i, `Group ${from + i}`));
}

function dto(groups: AiTelemetryGroupRowDto[], keySource: AiTelemetryKeySource = 'system'): AiTelemetryGroupsDto {
  return { keySource, groups, securityEventsAvailable: true };
}

describe('TelemetryGroupsComponent', () => {
  let fixture: ComponentFixture<TelemetryGroupsComponent>;
  let component: TelemetryGroupsComponent;
  let el: HTMLElement;
  let adminService: AdminService;
  let store: AdminPageStore;
  let groupsSpy: Mock;
  /** What the next groups request returns. */
  let next: AiTelemetryGroupsDto;

  beforeEach(async () => {
    clearStorage();
    ({ adminService, store } = await configureAdminTestBed(TelemetryGroupsComponent));
    next = dto([group(1, 'Testers')]);
    groupsSpy = vi.spyOn(adminService, 'getAiTelemetryGroups').mockImplementation(() => of(next));
  });

  afterEach(() => {
    if (fixture) {
      el.querySelectorAll<HTMLElement>('[popover]').forEach(popover => {
        try { popover.hidePopover(); } catch { /* already closed */ }
      });
      fixture.destroy();
    }
    clearStorage();
  });

  function create(range: { start?: string; end?: string } = { start: '2026-09-13', end: '2026-10-13' }, keys?: AiTelemetryKeySource): void {
    fixture = TestBed.createComponent(TelemetryGroupsComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    if (range.start !== undefined) fixture.componentRef.setInput('startDate', range.start);
    if (range.end !== undefined) fixture.componentRef.setInput('endDate', range.end);
    if (keys !== undefined) fixture.componentRef.setInput('keys', keys);
    fixture.detectChanges();
  }

  const lastQuery = (): AiTelemetryGroupsQuery => groupsSpy.mock.calls[groupsSpy.mock.calls.length - 1][0] as AiTelemetryGroupsQuery;
  const cards = (): HTMLElement[] => Array.from(el.querySelectorAll<HTMLElement>('article.tl-group-card'));
  const titles = (): string[] => cards().map(card => textOf(card.querySelector('h5')));
  const status = (): string => textOf(el.querySelector('#tl-groups-status'));
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
    it('fetches every group of the range and key source once, and lists them as articles labeled by their titles', () => {
      create();

      expect(groupsSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery()).toEqual({ startDate: '2026-09-13', endDate: '2026-10-13', keys: 'system' });
      const list = q('ul.tl-group-cards')!;
      expect(list.getAttribute('role')).toBe('list');
      expect(list.getAttribute('aria-label')).toBe('Groups');
      const article = cards()[0];
      expect(el.querySelector('#' + article.getAttribute('aria-labelledby'))?.tagName).toBe('H5');
      expect(titles()).toEqual(['Testers']);
      expect(q('#tl-groups-0-title')?.getAttribute('tabindex')).toBe('-1');
    });

    it('sends no bound for an empty range', () => {
      create({ start: '', end: '' });

      expect(lastQuery()).toEqual({ startDate: undefined, endDate: undefined, keys: 'system' });
    });

    it('says what the counts cover for each key source and how a group counts', () => {
      create();
      const scope = () => textOf(q('.tl-groups-scope'));
      const groupNote = "A group counts its current members' usage over the whole range; a member of two groups counts in both.";
      expect(el.firstElementChild).toBe(q('p.tl-groups-scope'));
      expect(scope()).toBe(`System AI configurations. Chats, messages and refusals count every key. ${groupNote}`);

      fixture.componentRef.setInput('keys', 'user');
      fixture.detectChanges();
      expect(scope()).toBe(`Users' own API keys. Limits apply to system AI configurations only; chats, messages and refusals count every key. ${groupNote}`);

      fixture.componentRef.setInput('keys', 'both');
      fixture.detectChanges();
      expect(scope()).toBe(`System AI configurations and users' own keys. Chats, messages and refusals count every key. ${groupNote}`);
    });

    it('refetches when the range or the Keys filter changes, and on reload', () => {
      create();
      groupsSpy.mockClear();

      fixture.componentRef.setInput('startDate', '2026-10-01');
      fixture.detectChanges();
      expect(groupsSpy).toHaveBeenCalledTimes(1);
      expect(lastQuery()).toEqual(expect.objectContaining({ startDate: '2026-10-01' }));

      fixture.componentRef.setInput('keys', 'both');
      fixture.detectChanges();
      expect(groupsSpy).toHaveBeenCalledTimes(2);
      expect(lastQuery()).toEqual(expect.objectContaining({ keys: 'both' }));

      next = dto([group(1, 'Testers'), group(2, 'Moderators')]);
      component.reload();
      fixture.detectChanges();
      expect(groupsSpy).toHaveBeenCalledTimes(3);
      expect(titles()).toEqual(['Moderators', 'Testers']);
    });

    it('puts the status line under the filter bar as a polite live region', () => {
      next = dto(groupRange(1, 34));
      create();

      const bar = q('.gh-filter-bar')!;
      const statusLine = q('p.gh-list-status.tl-groups-status')!;
      expect(statusLine.getAttribute('role')).toBe('status');
      expect(bar.compareDocumentPosition(statusLine) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(bar.contains(statusLine)).toBe(false);
      expect(status()).toBe('Showing 20 of 34 groups');
    });

    it('says One group for a single group', () => {
      create();

      expect(status()).toBe('One group');
    });

    it('lists groups with no usage with zeros', () => {
      next = dto([group(1, 'Testers'), group(2, 'Idle', {
        requests: 0, systemRequests: 0, chatRequests: 0, titleRequests: 0, promptTokens: 0, outputTokens: 0,
        estimatedCost: 0, pricedRequests: 0, peakHourRequests: 0, requestsPerActiveHour: 0, activeDays: 0,
        activeMembers: 0, lastRequestUtc: null, chatsCreated: 0, userMessages: 0
      })]);
      create();

      expect(titles()).toEqual(['Testers', 'Idle']);
      const idle = cards()[1];
      expect(textOf(idle.querySelector('.tl-metric-requests dd'))).toBe('0');
      expect(textOf(idle.querySelector('.tl-group-meta'))).toBe('12 members · No request in this range');
    });

    it('says so when there is no group to show', () => {
      next = dto([]);
      create();

      expect(textOf(q('.tl-groups-empty'))).toBe('No group used AI in this range.');
      expect(q('ul.tl-group-cards')).toBeNull();
      expect(status()).toBe('');
    });

    it('shows a failure as a field error', () => {
      groupsSpy.mockImplementation(() => throwError(() => ({ error: { message: 'Boom.' } })));
      create();

      const error = q('.gh-field-error.tl-groups-error')!;
      expect(textOf(error)).toBe('The groups could not be loaded: Boom.');
      expect(error.getAttribute('role')).toBe('alert');
      expect(q('ul.tl-group-cards')).toBeNull();
      expect(status()).toBe('');
    });
  });

  describe('search', () => {
    beforeEach(() => {
      next = dto([group(1, 'Beta testers'), group(2, 'Moderators'), group(null, '')]);
    });

    it('filters the cards by group name in the browser once typing pauses, without refetching', async () => {
      create();
      groupsSpy.mockClear();

      typeInto(q<HTMLInputElement>('#tl-groups-search')!, 'mod');
      expect(cards().length).toBe(3);

      await settle(300);
      fixture.detectChanges();

      expect(groupsSpy).not.toHaveBeenCalled();
      expect(titles()).toEqual(['Moderators']);
      expect(status()).toBe('One group');
      expect(textOf(q('label[for="tl-groups-search"]'))).toBe('Search groups by name');
    });

    it('says so when no group matches', async () => {
      create();

      typeInto(q<HTMLInputElement>('#tl-groups-search')!, 'nobody');
      await settle(300);
      fixture.detectChanges();

      expect(textOf(q('.tl-groups-empty'))).toBe('No groups match this search.');
    });

    it('Escape with text clears the search at once; with none it is left alone', async () => {
      create();
      const search = q<HTMLInputElement>('#tl-groups-search')!;
      typeInto(search, 'mod');
      await settle(300);
      fixture.detectChanges();
      expect(cards().length).toBe(1);

      const event = pressEscape(search);

      expect(event.defaultPrevented).toBe(true);
      expect(search.value).toBe('');
      expect(cards().length).toBe(3);
      expect(pressEscape(search).defaultPrevented).toBe(false);
    });

    it('offers Clear filters while the field has text; it clears the search and focuses the field', async () => {
      create();
      expect(q('#tl-groups-clear')).toBeNull();
      const search = q<HTMLInputElement>('#tl-groups-search')!;
      typeInto(search, 'mod');
      await settle(300);
      fixture.detectChanges();

      const clear = q<HTMLButtonElement>('#tl-groups-clear')!;
      expect(textOf(clear)).toBe('Clear filters');
      clear.click();
      fixture.detectChanges();

      expect(search.value).toBe('');
      expect(cards().length).toBe(3);
      expect(q('#tl-groups-clear')).toBeNull();
      expect(document.activeElement).toBe(search);
    });

    it('is not stored', async () => {
      create();
      typeInto(q<HTMLInputElement>('#tl-groups-search')!, 'mod');
      await settle(300);

      expect(localStorage.getItem(TELEMETRY_GROUPS_VIEW_STORAGE_KEY)).toBeNull();
    });
  });

  describe('sort', () => {
    beforeEach(() => {
      next = dto([
        group(1, 'Cheap', { estimatedCost: 0.1, requests: 30, memberCount: 2 }),
        group(2, 'Dear', { estimatedCost: 2, requests: 5, memberCount: 40 }),
        group(null, '', { estimatedCost: 0.5, requests: 10, memberCount: 7 })
      ]);
    });

    it('orders by the highest cost first by default, in the shared Sort popover', () => {
      create();

      expect(titles()).toEqual(['Dear', 'No group', 'Cheap']);
      expect(q('app-telemetry-sort-popover')).not.toBeNull();
      expect(q('#tl-groups-sort-trigger')).not.toBeNull();
      expect(q('#tl-groups-sort-panel')).not.toBeNull();
      expect(Array.from(el.querySelectorAll('#tl-groups-sort-panel [id^="tl-groups-sort-field-"]')).map(node => node.id)).toEqual([
        'tl-groups-sort-field-cost', 'tl-groups-sort-field-requests', 'tl-groups-sort-field-tokens', 'tl-groups-sort-field-peakHour',
        'tl-groups-sort-field-refusals', 'tl-groups-sort-field-members', 'tl-groups-sort-field-lastActive', 'tl-groups-sort-field-name'
      ]);
    });

    it('re-sorts in the browser without refetching and stores the sort as version 1', () => {
      create();
      groupsSpy.mockClear();

      q<HTMLInputElement>('#tl-groups-sort-field-members')!.click();
      fixture.detectChanges();

      expect(groupsSpy).not.toHaveBeenCalled();
      expect(titles()).toEqual(['Dear', 'No group', 'Cheap']);
      expect(JSON.parse(localStorage.getItem(TELEMETRY_GROUPS_VIEW_STORAGE_KEY)!)).toEqual({
        version: 1, sort: { field: 'members', direction: 'desc' }
      });

      q<HTMLInputElement>('#tl-groups-sort-field-requests')!.click();
      fixture.detectChanges();
      expect(titles()).toEqual(['Cheap', 'No group', 'Dear']);
      expect(textOf(q('#tl-groups-sort-trigger'))).toContain('Requests, highest first');

      q<HTMLInputElement>('#tl-groups-sort-direction-asc')!.click();
      fixture.detectChanges();
      expect(titles()).toEqual(['Dear', 'No group', 'Cheap']);
    });

    it('reads the stored sort', () => {
      localStorage.setItem(TELEMETRY_GROUPS_VIEW_STORAGE_KEY, JSON.stringify({ version: 1, sort: { field: 'name', direction: 'asc' } }));
      create();

      expect(titles()).toEqual(['Cheap', 'Dear', 'No group']);
    });
  });

  describe('batches', () => {
    it('shows 20 cards, then Show N more; it adds the rest and focuses the first new card', () => {
      next = dto(groupRange(1, 25));
      create();

      expect(cards().length).toBe(20);
      expect(status()).toBe('Showing 20 of 25 groups');
      const more = q<HTMLButtonElement>('.tl-groups-show-more')!;
      expect(more.classList).toContain('btn-ghost');
      expect(textOf(more)).toBe('Show 5 more');
      expect(q('.tl-groups-show-all')).toBeNull();

      more.click();
      fixture.detectChanges();

      expect(cards().length).toBe(25);
      expect(document.activeElement?.id).toBe('tl-groups-20-title');
      expect(textOf(document.activeElement)).toBe('Group 21');
      expect(q('.tl-groups-show-more')).toBeNull();
      expect(status()).toBe('Showing 25 of 25 groups');
    });

    it('offers Show all M while more than one batch remains', () => {
      next = dto(groupRange(1, 45));
      create();

      expect(textOf(q('.tl-groups-show-more'))).toBe('Show 20 more');
      const all = q<HTMLButtonElement>('.tl-groups-show-all')!;
      expect(textOf(all)).toBe('Show all 45');

      all.click();
      fixture.detectChanges();

      expect(cards().length).toBe(45);
      expect(document.activeElement?.id).toBe('tl-groups-20-title');
      expect(q('.gh-load-more')).toBeNull();
    });

    it('returns to one batch when the search changes', async () => {
      next = dto(groupRange(1, 45));
      create();
      q<HTMLButtonElement>('.tl-groups-show-more')!.click();
      fixture.detectChanges();
      expect(cards().length).toBe(40);

      typeInto(q<HTMLInputElement>('#tl-groups-search')!, 'group');
      await settle(300);
      fixture.detectChanges();

      expect(cards().length).toBe(20);
      expect(status()).toBe('Showing 20 of 45 groups');
    });
  });

  describe('a card', () => {
    it('says the members, the active members and the last active day', () => {
      create();

      expect(textOf(cards()[0].querySelector('.tl-group-meta'))).toBe('12 members · 9 active in this range · Last active 2026-10-12');
      expect(cards()[0].querySelector('time')?.getAttribute('datetime')).toBe('2026-10-12T21:15:00Z');
    });

    it('says the blocked members and the refusals as outlined badges in a list named Group state', () => {
      next = dto([group(1, 'Testers', { blockedMembers: 2, limitRefusals: 2, providerRefusals: 1 })]);
      create();

      const badges = cards()[0].querySelector('ul.gh-model-card-badges')!;
      expect(badges.getAttribute('role')).toBe('list');
      expect(badges.getAttribute('aria-label')).toBe('Group state');
      expect(Array.from(badges.querySelectorAll('li')).map(textOf)).toEqual(['2 blocked members', '3 refusals']);
      expect(Array.from(badges.querySelectorAll('li > span')).every(span => span.classList.contains('gh-tag'))).toBe(true);
    });

    it('renders no badge row when there is nothing to say', () => {
      create();

      expect(cards()[0].querySelector('.gh-model-card-badges')).toBeNull();
    });

    it('shows the nine metrics of a user card', () => {
      next = dto([group(1, 'Testers', { requests: 10, pricedRequests: 7, benchmarkRequests: 3 })]);
      create();

      expect(Array.from(cards()[0].querySelectorAll('.tl-metric dt')).map(textOf)).toEqual([
        'Requests', 'Tokens', 'Est. cost', 'Busiest hour', 'Active days', 'Chats', 'Messages', 'Limit refusals', 'Provider refusals'
      ]);
      const notes = (metric: string) => Array.from(cards()[0].querySelectorAll(`.${metric} .tl-metric-note`)).map(textOf);
      expect(notes('tl-metric-requests')).toEqual(['chat 8 · title 2 · benchmark 3']);
      expect(notes('tl-metric-cost')).toEqual(['3 unpriced']);
      expect(textOf(cards()[0].querySelector('.tl-metric-peak dd'))).toBe('6 requests');
    });

    it('splits requests and cost by key source under Both only', () => {
      next = dto([group(1, 'Testers', { systemRequests: 7, userKeyRequests: 3, userKeyEstimatedCost: 0.12 })], 'both');
      create(undefined, 'both');

      const notes = (metric: string) => Array.from(cards()[0].querySelectorAll(`.${metric} .tl-metric-note`)).map(textOf);
      expect(notes('tl-metric-requests')).toEqual(['chat 8 · title 2', 'system 7 · own key 3']);
      expect(notes('tl-metric-cost')).toEqual(['own key $0.1200']);
    });

    it('shows dashes for the refusals when security events are unavailable', () => {
      next = { ...dto([group(1, 'Testers', { limitRefusals: 2 })]), securityEventsAvailable: false };
      create();

      const values = Array.from(cards()[0].querySelectorAll('.tl-metric')).slice(7).map(metric => textOf(metric.querySelector('dd')));
      expect(values).toEqual(['—', '—']);
      expect(cards()[0].querySelector('.gh-model-card-badges')).toBeNull();
    });

    it('shows the configurations with the group\'s pooled counters against its limits', () => {
      next = dto([group(1, 'Testers', {
        configs: [
          config(),
          config({ configId: 8, configName: 'Titles', dailyChatRequests: 3, maxDailyChatRequests: null, monthlyChatRequests: 5, maxMonthlyChatRequests: 0, assignmentEnabled: false }),
          config({ configId: 9, configName: 'Gemini', grantedByGroup: false, assignmentId: null, assignmentEnabled: null,
            dailyChatRequests: null, maxDailyChatRequests: null, monthlyChatRequests: null, maxMonthlyChatRequests: null }),
          config({ keySource: 'user', configId: null, configName: 'Own keys', grantedByGroup: false, assignmentId: null, assignmentEnabled: null,
            dailyChatRequests: null, maxDailyChatRequests: null, monthlyChatRequests: null, maxMonthlyChatRequests: null })
        ]
      })], 'both');
      create(undefined, 'both');

      const card = cards()[0];
      expect(Array.from(card.querySelectorAll('thead th')).map(textOf)).toEqual(['Configuration', 'Requests', 'Est. cost', 'Today', 'This month']);
      const rows = Array.from(card.querySelectorAll('tbody tr'));
      expect(rows.map(r => textOf(r.querySelector('th')))).toEqual([
        'Claude Chat', 'Titles disabled', 'Gemini not granted by this group', 'Own keys own key'
      ]);
      expect(rows.map(r => textOf(r.querySelector('.tl-config-today')))).toEqual(['12 / 50', '3 (no limit)', '—', '—']);
      expect(rows.map(r => textOf(r.querySelector('.tl-config-month')))).toEqual(['140 / 1,000', '5 (no limit)', '—', '—']);
      expect(Array.from(rows[0].querySelectorAll('td')).map(td => td.getAttribute('data-label'))).toEqual(
        ['Requests', 'Est. cost', 'Today', 'This month']);
      const region = card.querySelector<HTMLElement>('.tl-group-configs')!;
      expect(region.getAttribute('role')).toBe('region');
      expect(region.tabIndex).toBe(0);
      expect(textOf(el.querySelector('#' + region.getAttribute('aria-labelledby')))).toBe('Configurations of Testers');
    });

    it('shows a system line\'s configuration badges, and none on the Own keys line', () => {
      next = dto([group(1, 'Testers', {
        configs: [
          config({ provider: 'Google', thinkingLevel: 'high', reasoningMode: 'Summary', serviceTier: 'priority' }),
          config({ configId: 9, configName: 'Gemini', grantedByGroup: false, assignmentId: null, assignmentEnabled: null, thinkingLevel: 'low' }),
          config({ keySource: 'user', configId: null, configName: 'Own keys', grantedByGroup: false, assignmentId: null, assignmentEnabled: null })
        ]
      })], 'both');
      create(undefined, 'both');

      const headers = Array.from(cards()[0].querySelectorAll('tbody th'));
      const badges = headers[0].querySelector('.tl-config-badges')!;
      expect(textOf(badges.querySelector('.thinking-badge'))).toBe('thinking level High');
      expect(textOf(badges.querySelector('.reasoning-badge'))).toBe('reasoning mode Summary');
      expect(textOf(badges.querySelector('app-provider-badge'))).toBe('Google');
      expect(textOf(badges.querySelector('.config-badge'))).toBe('service tier Priority');

      expect(textOf(headers[1].querySelector('.thinking-badge'))).toBe('thinking level Low');
      expect(textOf(headers[1].querySelector('.tl-config-badges .tl-config-not-granted'))).toBe('not granted by this group');

      expect(headers[2].querySelector('.thinking-badge')).toBeNull();
      expect(headers[2].querySelector('app-provider-badge')).toBeNull();
      expect(headers[2].querySelector('.config-badge')).toBeNull();
      expect(textOf(headers[2])).toBe('Own keys own key');
    });

    it('titles the card with the group name, focusable from script', () => {
      create();

      const title = cards()[0].querySelector('h5.tl-group-title')!;
      expect(title.id).toBe('tl-groups-0-title');
      expect(title.getAttribute('tabindex')).toBe('-1');
      expect(textOf(title)).toBe('Testers');
    });

    it('stacks the configurations table under 36 rem of its own width', () => {
      next = dto([group(1, 'Testers', { configs: [config()] })]);
      create();
      const display = (width: string) => {
        el.style.width = width;
        void el.offsetWidth;
        return getComputedStyle(cards()[0].querySelector('tbody td')!).display;
      };

      expect(display('1000px')).toBe('table-cell');
      expect(display('400px')).toBe('block');
    });

    it('titles the users in no group No group, tags them and calls them users', () => {
      next = dto([group(null, '', {
        memberCount: 3, blockedMembers: 1,
        configs: [config({ grantedByGroup: false, dailyChatRequests: null, maxDailyChatRequests: null })]
      })]);
      create();

      const card = cards()[0];
      expect(textOf(card.querySelector('h5'))).toBe('No group');
      expect(card.getAttribute('data-group-id')).toBe('none');
      expect(Array.from(card.querySelectorAll('.gh-model-card-badges li')).map(textOf)).toEqual(['Users in no group', '1 blocked user']);
      expect(textOf(card.querySelector('.tl-group-meta'))).toBe('3 users · 9 active in this range · Last active 2026-10-12');
      expect(card.querySelector('.tl-config-not-granted')).toBeNull();
      expect(textOf(card.querySelector('.tl-config-today'))).toBe('—');
    });
  });

  describe('actions', () => {
    it('sit in a group named for the group: Usage by model, Usage charts, Manage limits, each with a tooltip', () => {
      create();

      const row = cards()[0].querySelector('.tl-group-title-row')!;
      expect(row.firstElementChild?.tagName).toBe('H5');
      const actions = row.querySelector('[role="group"]')!;
      expect(actions.getAttribute('aria-label')).toBe('Actions for Testers');
      const names = Array.from(actions.querySelectorAll('button')).map(b => b.getAttribute('aria-label'));
      expect(names).toEqual(['Usage by model: Testers', 'Usage charts of Testers', 'Manage the limits of Testers']);
      const tips = Array.from(actions.querySelectorAll('button')).map(button => {
        expect(button.hasAttribute('title')).toBe(false);
        const tip = el.querySelector('#' + button.getAttribute('interestfor'));
        expect(tip?.getAttribute('popover')).toBe('hint');
        return textOf(tip);
      });
      expect(tips).toEqual(['Usage by model', 'Usage charts', 'Manage limits']);
    });

    it('Usage by model opens a dialog and emits the group with the button as opener', () => {
      create();
      const emitted: TelemetryGroupModelsRef[] = [];
      component.usageByModel.subscribe(ref => emitted.push(ref));

      const button = q<HTMLButtonElement>('button[aria-label="Usage by model: Testers"]')!;
      expect(button.getAttribute('aria-haspopup')).toBe('dialog');
      button.click();

      expect(emitted.length).toBe(1);
      expect(emitted[0]).toEqual(expect.objectContaining({ groupId: 1, groupName: 'Testers' }));
      expect(emitted[0].opener).toBe(button);
    });

    it('emits the group from Usage charts', () => {
      create();
      const emitted: TelemetryGroupRef[] = [];
      component.usageCharts.subscribe(ref => emitted.push(ref));

      q<HTMLButtonElement>('button[aria-label="Usage charts of Testers"]')!.click();

      expect(emitted).toEqual([{ groupId: 1, groupName: 'Testers' }]);
    });

    it('Manage limits asks the Groups sub-tab of the Users tab to manage the group and selects it', () => {
      create();
      q<HTMLButtonElement>('button[aria-label="Manage the limits of Testers"]')!.click();

      expect(store.pendingManageGroup).toEqual({ groupId: 1, groupName: 'Testers' });
      expect(store.activeTab).toBe('users');
      expect(store.usersSubTab).toBe('groups');
    });

    it('has no Manage limits for the users in no group, and emits none for them', () => {
      next = dto([group(null, '')]);
      create();
      const models: TelemetryGroupModelsRef[] = [];
      const charts: TelemetryGroupRef[] = [];
      component.usageByModel.subscribe(ref => models.push(ref));
      component.usageCharts.subscribe(ref => charts.push(ref));

      const actions = cards()[0].querySelector('[role="group"]')!;
      expect(Array.from(actions.querySelectorAll('button')).map(b => b.getAttribute('aria-label'))).toEqual(
        ['Usage by model: No group', 'Usage charts of No group']);
      q<HTMLButtonElement>('button[aria-label="Usage by model: No group"]')!.click();
      q<HTMLButtonElement>('button[aria-label="Usage charts of No group"]')!.click();

      expect(models.map(ref => [ref.groupId, ref.groupName])).toEqual([['none', 'No group']]);
      expect(charts).toEqual([{ groupId: 'none', groupName: 'No group' }]);
    });
  });
});
