import type { AiTelemetryGroupRowDto } from '../../../services/admin.service';
import {
  DEFAULT_TELEMETRY_GROUPS_SORT,
  NO_GROUP_TEXT,
  TELEMETRY_GROUPS_SORT_FIELDS,
  TELEMETRY_GROUPS_VIEW_STORAGE_KEY,
  TelemetryGroupsSort,
  defaultGroupsView,
  groupDisplayName,
  groupKeyOf,
  groupsSortLabel,
  isDefaultGroupsSort,
  readGroupsView,
  searchGroups,
  sortGroups,
  writeGroupsView
} from './telemetry-groups-view';

/** An in-memory Storage. */
function memoryStorage(initial: Record<string, string> = {}): Storage {
  const data = new Map(Object.entries(initial));
  return {
    get length() { return data.size; },
    clear: () => data.clear(),
    getItem: (key: string) => data.get(key) ?? null,
    key: (index: number) => Array.from(data.keys())[index] ?? null,
    removeItem: (key: string) => { data.delete(key); },
    setItem: (key: string, value: string) => { data.set(key, value); }
  };
}

function group(groupId: number | null, groupName: string, overrides: Partial<AiTelemetryGroupRowDto> = {}): AiTelemetryGroupRowDto {
  return {
    groupId, groupName, memberCount: 5, activeMembers: 2, blockedMembers: 0,
    requests: 10, systemRequests: 10, userKeyRequests: 0, systemEstimatedCost: 0.25, userKeyEstimatedCost: 0,
    chatRequests: 8, titleRequests: 2, benchmarkRequests: 0,
    promptTokens: 1000, outputTokens: 200, estimatedCost: 0.25, pricedRequests: 10,
    peakHourRequests: 6, peakHourStartUtc: '2026-10-12T20:00:00Z', requestsPerActiveHour: 2.5,
    activeDays: 3, lastRequestUtc: '2026-10-12T21:15:00Z', chatsCreated: 4, userMessages: 9,
    limitRefusals: 0, providerRefusals: 0, configs: [],
    ...overrides
  };
}

const names = (rows: AiTelemetryGroupRowDto[]): string[] => rows.map(groupDisplayName);

describe('telemetry groups view', () => {
  describe('identity', () => {
    it('titles the users in no group No group and keys them as none', () => {
      const none = group(null, '');
      expect(groupDisplayName(none)).toBe(NO_GROUP_TEXT);
      expect(groupDisplayName(none)).toBe('No group');
      expect(groupKeyOf(none)).toBe('none');
      expect(groupDisplayName(group(4, 'Testers'))).toBe('Testers');
      expect(groupKeyOf(group(4, 'Testers'))).toBe(4);
    });
  });

  describe('search', () => {
    const rows = [group(1, 'Beta testers'), group(2, 'Moderators'), group(null, '')];

    it('matches every term in the group name, ignoring case', () => {
      expect(names(searchGroups(rows, 'TEST beta'))).toEqual(['Beta testers']);
      expect(names(searchGroups(rows, 'mod'))).toEqual(['Moderators']);
      expect(names(searchGroups(rows, 'no group'))).toEqual(['No group']);
      expect(searchGroups(rows, 'nobody')).toEqual([]);
    });

    it('keeps every row, in order, for an empty search', () => {
      expect(names(searchGroups(rows, '  '))).toEqual(['Beta testers', 'Moderators', 'No group']);
    });
  });

  describe('sort labels', () => {
    it('lists the fields in the popover order with their kinds', () => {
      expect(TELEMETRY_GROUPS_SORT_FIELDS.map(f => [f.field, f.label, f.kind])).toEqual([
        ['cost', 'Est. cost', 'number'],
        ['requests', 'Requests', 'number'],
        ['tokens', 'Tokens', 'number'],
        ['peakHour', 'Busiest hour', 'number'],
        ['refusals', 'Refusals', 'number'],
        ['members', 'Members', 'number'],
        ['lastActive', 'Last active', 'date'],
        ['name', 'Group name', 'text']
      ]);
    });

    it('labels a sort with its field and direction', () => {
      expect(groupsSortLabel(DEFAULT_TELEMETRY_GROUPS_SORT)).toBe('Est. cost, highest first');
      expect(groupsSortLabel({ field: 'members', direction: 'asc' })).toBe('Members, lowest first');
      expect(groupsSortLabel({ field: 'name', direction: 'asc' })).toBe('Group name, A–Z');
    });

    it('defaults to the highest cost first', () => {
      expect(DEFAULT_TELEMETRY_GROUPS_SORT).toEqual({ field: 'cost', direction: 'desc' });
      expect(isDefaultGroupsSort({ field: 'cost', direction: 'desc' })).toBe(true);
      expect(isDefaultGroupsSort({ field: 'cost', direction: 'asc' })).toBe(false);
      expect(isDefaultGroupsSort({ field: 'members', direction: 'desc' })).toBe(false);
    });
  });

  describe('sorting', () => {
    const sorted = (rows: AiTelemetryGroupRowDto[], sort: TelemetryGroupsSort) => names(sortGroups(rows, sort));

    it('sorts by a figure in either direction without changing the input', () => {
      const rows = [
        group(1, 'A', { requests: 5 }),
        group(2, 'B', { requests: 20 }),
        group(3, 'C', { requests: 10 })
      ];
      expect(sorted(rows, { field: 'requests', direction: 'desc' })).toEqual(['B', 'C', 'A']);
      expect(sorted(rows, { field: 'requests', direction: 'asc' })).toEqual(['A', 'C', 'B']);
      expect(names(rows)).toEqual(['A', 'B', 'C']);
    });

    it('sorts tokens, refusals and members by their sums and counts', () => {
      const rows = [
        group(1, 'A', { promptTokens: 10, outputTokens: 5, limitRefusals: 1, providerRefusals: 0, memberCount: 9 }),
        group(2, 'B', { promptTokens: 4, outputTokens: 20, limitRefusals: 1, providerRefusals: 2, memberCount: 1 })
      ];
      expect(sorted(rows, { field: 'tokens', direction: 'desc' })).toEqual(['B', 'A']);
      expect(sorted(rows, { field: 'refusals', direction: 'desc' })).toEqual(['B', 'A']);
      expect(sorted(rows, { field: 'members', direction: 'desc' })).toEqual(['A', 'B']);
    });

    it('puts an unpriced cost and a missing last request last in both directions', () => {
      const rows = [
        group(1, 'Unpriced', { requests: 4, pricedRequests: 0, estimatedCost: 0 }),
        group(2, 'Cheap', { estimatedCost: 0.1 }),
        group(3, 'Idle', { requests: 0, pricedRequests: 0, estimatedCost: 0, lastRequestUtc: null }),
        group(4, 'Dear', { estimatedCost: 2, lastRequestUtc: '2026-10-13T08:00:00Z' })
      ];
      expect(sorted(rows, { field: 'cost', direction: 'desc' })).toEqual(['Dear', 'Cheap', 'Idle', 'Unpriced']);
      expect(sorted(rows, { field: 'cost', direction: 'asc' })).toEqual(['Idle', 'Cheap', 'Dear', 'Unpriced']);
      expect(sorted(rows, { field: 'lastActive', direction: 'desc' })[0]).toBe('Dear');
      expect(sorted(rows, { field: 'lastActive', direction: 'desc' }).at(-1)).toBe('Idle');
      expect(sorted(rows, { field: 'lastActive', direction: 'asc' }).at(-1)).toBe('Idle');
    });

    it('breaks ties by name, then id, No group last', () => {
      const rows = [
        group(null, '', { requests: 10 }),
        group(7, 'Same', { requests: 10 }),
        group(3, 'Same', { requests: 10 }),
        group(5, 'Alpha', { requests: 10 })
      ];
      const ids = sortGroups(rows, { field: 'requests', direction: 'desc' }).map(r => r.groupId);
      expect(ids).toEqual([5, null, 3, 7]);
      const zero = [group(null, 'No group', { requests: 0 }), group(2, 'No group', { requests: 0 })];
      expect(sortGroups(zero, { field: 'requests', direction: 'asc' }).map(r => r.groupId)).toEqual([2, null]);
    });

    it('sorts by name A–Z and Z–A', () => {
      const rows = [group(1, 'beta'), group(2, 'Alpha'), group(null, ''), group(3, 'group 10'), group(4, 'group 9')];
      expect(sorted(rows, { field: 'name', direction: 'asc' })).toEqual(['Alpha', 'beta', 'group 9', 'group 10', 'No group']);
      expect(sorted(rows, { field: 'name', direction: 'desc' })).toEqual(['No group', 'group 10', 'group 9', 'beta', 'Alpha']);
    });
  });

  describe('view storage', () => {
    it('falls back to the highest cost first', () => {
      expect(readGroupsView(memoryStorage())).toEqual({ sort: { field: 'cost', direction: 'desc' } });
      expect(readGroupsView(null)).toEqual(defaultGroupsView());
    });

    it('round-trips the sort under its key with version 1', () => {
      const storage = memoryStorage();
      writeGroupsView({ sort: { field: 'members', direction: 'asc' } }, storage);

      expect(TELEMETRY_GROUPS_VIEW_STORAGE_KEY).toBe('overseer.admin.telemetry.groups.view');
      expect(JSON.parse(storage.getItem(TELEMETRY_GROUPS_VIEW_STORAGE_KEY)!)).toEqual({
        version: 1, sort: { field: 'members', direction: 'asc' }
      });
      expect(readGroupsView(storage)).toEqual({ sort: { field: 'members', direction: 'asc' } });
    });

    it('ignores garbage, another version and unknown values', () => {
      const read = (raw: string) => readGroupsView(memoryStorage({ [TELEMETRY_GROUPS_VIEW_STORAGE_KEY]: raw }));

      expect(read('not json')).toEqual(defaultGroupsView());
      expect(read('null')).toEqual(defaultGroupsView());
      expect(read('"cost"')).toEqual(defaultGroupsView());
      expect(read(JSON.stringify({ version: 2, sort: { field: 'name', direction: 'asc' } }))).toEqual(defaultGroupsView());
      expect(read(JSON.stringify({ version: 1, sort: 'name' }))).toEqual(defaultGroupsView());
      expect(read(JSON.stringify({ version: 1, sort: { field: 'name', direction: 'up' } }))).toEqual(defaultGroupsView());
      expect(read(JSON.stringify({ version: 1, sort: { field: 'height', direction: 'asc' } }))).toEqual(defaultGroupsView());
    });

    it('falls back when storage throws', () => {
      const throwing = memoryStorage();
      throwing.getItem = () => { throw new Error('denied'); };
      throwing.setItem = () => { throw new Error('denied'); };

      expect(readGroupsView(throwing)).toEqual(defaultGroupsView());
      expect(() => writeGroupsView({ sort: { field: 'name', direction: 'asc' } }, throwing)).not.toThrow();
    });
  });
});
