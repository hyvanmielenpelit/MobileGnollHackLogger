import type { AiTelemetryModelUserRowDto } from '../../../services/admin.service';
import {
  DEFAULT_MODEL_USER_SORT,
  MODEL_USER_SORT_FIELDS,
  ModelUserCard,
  ModelUserSortField,
  TELEMETRY_MODEL_USERS_STORAGE_KEY,
  buildModelUserCards,
  defaultModelUsersView,
  filterByThinkingLevel,
  modelUserDirectionLabel,
  modelUserRanks,
  modelUserShares,
  modelUserSortLabel,
  modelUsersStatus,
  readModelUsersView,
  searchModelUsers,
  shareFieldOf,
  sortModelUserCards,
  sumModelUserCards,
  thinkingLevelFilterValue,
  thinkingLevelOptionOf,
  thinkingLevelOptions,
  writeModelUsersView
} from './telemetry-model-users';

/** A Chat row of one request with every figure at zero. */
function userRow(overrides: Partial<AiTelemetryModelUserRowDto> = {}): AiTelemetryModelUserRowDto {
  return {
    userId: 'u1', userName: 'alice', roleContext: 1, roleGroup: 'chat', thinkingLevel: 'high',
    requests: 1, promptTokens: 0, uncachedInputTokens: 0, cacheReadTokens: 0, cacheCreationTokens: 0, outputTokens: 0,
    durationSumMs: 0, durationCount: 0, estimatedCost: 0, pricedRequests: 1, lastRequestUtc: '2026-09-10T12:00:00Z',
    ...overrides
  };
}

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

const grouped = (...rows: AiTelemetryModelUserRowDto[]) => buildModelUserCards(rows, { separateThinkingLevels: false });
const names = (cards: readonly ModelUserCard[]) => cards.map(c => c.userName ?? '(none)');

describe('telemetry-model-users', () => {
  describe('buildModelUserCards', () => {
    const rows = [
      userRow({ thinkingLevel: 'high', requests: 2, estimatedCost: 0.2, pricedRequests: 2, promptTokens: 100, outputTokens: 10,
        lastRequestUtc: '2026-09-10T10:00:00Z' }),
      userRow({ thinkingLevel: 'Low', requests: 1, estimatedCost: 0.1, promptTokens: 50, outputTokens: 5,
        lastRequestUtc: '2026-09-11T08:00:00Z' }),
      userRow({ roleContext: 3, thinkingLevel: 'low', requests: 3, estimatedCost: 0.3, pricedRequests: 3 }),
      userRow({ userId: 'u2', userName: 'bob', thinkingLevel: null }),
      userRow({ userId: 'u2', userName: 'bob', thinkingLevel: '' })
    ];

    it('groups by user, summing every level and keeping the latest request', () => {
      const cards = grouped(...rows);
      expect(cards.length).toBe(2);
      const alice = cards.find(c => c.userId === 'u1')!;
      expect(alice.requests).toBe(6);
      expect(alice.estimatedCost).toBeCloseTo(0.6);
      expect(alice.rowCount).toBe(3);
      expect(alice.thinkingLevel).toBeUndefined();
      expect(alice.thinkingLevels).toEqual(['low', 'high']);
      expect(alice.lastRequestUtc).toBe('2026-09-11T08:00:00Z');
      expect(alice.totalTokens).toBe(165);
      expect(alice.validAttempts).toBe(0);
      expect(cards.find(c => c.userId === 'u2')!.thinkingLevels).toEqual([]);
    });

    it('splits a user into one card per level, case-insensitively, keeping none sent apart from not recorded', () => {
      const cards = buildModelUserCards(rows, { separateThinkingLevels: true });
      const alice = cards.filter(c => c.userId === 'u1');
      expect(alice.map(c => [c.thinkingLevel, c.requests])).toEqual([['high', 2], ['low', 4]]);
      const bob = cards.filter(c => c.userId === 'u2');
      expect(bob.map(c => c.thinkingLevel)).toEqual([null, '']);
      expect(new Set(cards.map(c => c.key)).size).toBe(cards.length);
    });

    it('takes a later row\'s name when the first has none', () => {
      const cards = grouped(userRow({ userName: null }), userRow({ userName: ' alice ' }));
      expect(cards[0].userName).toBe('alice');
    });
  });

  describe('thinking-level filter', () => {
    const rows = [
      userRow({ thinkingLevel: 'high' }), userRow({ thinkingLevel: 'Low' }), userRow({ thinkingLevel: '' }),
      userRow({ thinkingLevel: null }), userRow({ userId: 'u2', thinkingLevel: 'low', requests: 5 })
    ];

    it('offers all levels, the recorded levels in the catalog order, then none sent and not recorded', () => {
      expect(thinkingLevelOptions(rows)).toEqual([
        { value: '*', label: 'All levels' },
        { value: '=low', label: 'Low' },
        { value: '=high', label: 'High' },
        { value: 'none', label: 'None sent' },
        { value: 'unrecorded', label: 'Not recorded' }
      ]);
      expect(thinkingLevelOptions([userRow()])).toEqual([{ value: '*', label: 'All levels' }, { value: '=high', label: 'High' }]);
    });

    it('maps a card level to its option and an option to its level', () => {
      expect(thinkingLevelOptionOf(undefined)).toBe('*');
      expect(thinkingLevelOptionOf(null)).toBe('unrecorded');
      expect(thinkingLevelOptionOf('')).toBe('none');
      expect(thinkingLevelOptionOf(' High ')).toBe('=high');
      expect(thinkingLevelFilterValue('*')).toBe('*');
      expect(thinkingLevelFilterValue('none')).toBe('');
      expect(thinkingLevelFilterValue('unrecorded')).toBeNull();
      expect(thinkingLevelFilterValue('=high')).toBe('high');
      expect(thinkingLevelFilterValue('bogus')).toBe('*');
    });

    it('filters the rows before grouping, so a grouped card sums that level only', () => {
      const low = filterByThinkingLevel(rows, '=low');
      expect(low.length).toBe(2);
      const cards = grouped(...low);
      expect(cards.find(c => c.userId === 'u1')!.requests).toBe(1);
      expect(filterByThinkingLevel(rows, 'none').length).toBe(1);
      expect(filterByThinkingLevel(rows, 'unrecorded').length).toBe(1);
      expect(filterByThinkingLevel(rows, '*').length).toBe(5);
    });
  });

  describe('search', () => {
    const cards = grouped(
      userRow({ userId: 'a', userName: 'Alice Smith' }), userRow({ userId: 'b', userName: 'bob' }),
      userRow({ userId: 'c', userName: null }));

    it('matches every term against the user name, keeping the order', () => {
      expect(names(searchModelUsers(cards, 'smith ALI'))).toEqual(['Alice Smith']);
      expect(names(searchModelUsers(cards, ''))).toEqual(['Alice Smith', 'bob', '(none)']);
      expect(searchModelUsers(cards, 'unknown')).toEqual([]);
    });
  });

  describe('sort', () => {
    const cards = grouped(
      userRow({ userId: 'a', userName: 'carol', requests: 5, estimatedCost: 0.5, pricedRequests: 5,
        uncachedInputTokens: 30, cacheReadTokens: 10, cacheCreationTokens: 1, outputTokens: 7, promptTokens: 40,
        durationSumMs: 500, durationCount: 5, lastRequestUtc: '2026-09-10T00:00:00Z' }),
      userRow({ userId: 'b', userName: 'alice', requests: 1, estimatedCost: 0.9, pricedRequests: 1,
        uncachedInputTokens: 10, cacheReadTokens: 30, cacheCreationTokens: 3, outputTokens: 2, promptTokens: 50,
        durationSumMs: 300, durationCount: 1, lastRequestUtc: '2026-09-12T00:00:00Z' }),
      userRow({ userId: 'c', userName: 'bob', requests: 3, estimatedCost: 0, pricedRequests: 0,
        uncachedInputTokens: 20, cacheReadTokens: 0, cacheCreationTokens: 2, outputTokens: 1, promptTokens: 0,
        durationSumMs: 0, durationCount: 0, lastRequestUtc: '2026-09-11T00:00:00Z' }));

    const order = (field: ModelUserSortField, direction: 'asc' | 'desc' = 'desc') =>
      names(sortModelUserCards(cards, { field, direction }));

    it('sorts by each figure both ways, a missing figure last in both', () => {
      expect(order('cost')).toEqual(['alice', 'carol', 'bob']);
      expect(order('cost', 'asc')).toEqual(['carol', 'alice', 'bob']);
      expect(order('requests')).toEqual(['carol', 'bob', 'alice']);
      expect(order('requests', 'asc')).toEqual(['alice', 'bob', 'carol']);
      expect(order('uncached')).toEqual(['carol', 'bob', 'alice']);
      expect(order('cacheRead')).toEqual(['alice', 'carol', 'bob']);
      expect(order('cacheCreated', 'asc')).toEqual(['carol', 'bob', 'alice']);
      expect(order('output')).toEqual(['carol', 'alice', 'bob']);
      expect(order('totalTokens')).toEqual(['alice', 'carol', 'bob']);
      expect(order('totalTokens', 'asc')).toEqual(['bob', 'carol', 'alice']);
      expect(order('cacheHit')).toEqual(['alice', 'carol', 'bob']);
      expect(order('cacheHit', 'asc')).toEqual(['carol', 'alice', 'bob']);
      expect(order('duration')).toEqual(['alice', 'carol', 'bob']);
      expect(order('duration', 'asc')).toEqual(['carol', 'alice', 'bob']);
      expect(order('lastRequest')).toEqual(['alice', 'bob', 'carol']);
      expect(order('lastRequest', 'asc')).toEqual(['carol', 'bob', 'alice']);
    });

    it('sorts by user name both ways, a missing name last in both', () => {
      const withUnknown = [...cards, ...grouped(userRow({ userId: 'z', userName: null }))];
      expect(names(sortModelUserCards(withUnknown, { field: 'name', direction: 'asc' }))).toEqual(['alice', 'bob', 'carol', '(none)']);
      expect(names(sortModelUserCards(withUnknown, { field: 'name', direction: 'desc' }))).toEqual(['carol', 'bob', 'alice', '(none)']);
    });

    it('breaks ties by user name, a missing name last, then by thinking level, then by key', () => {
      const tied = buildModelUserCards([
        userRow({ userId: 'x', userName: 'zed', thinkingLevel: 'high' }),
        userRow({ userId: 'y', userName: null, thinkingLevel: 'high' }),
        userRow({ userId: 'x', userName: 'zed', thinkingLevel: 'low' }),
        userRow({ userId: 'w', userName: 'amy', thinkingLevel: 'high' })
      ], { separateThinkingLevels: true });
      const sorted = sortModelUserCards(tied, DEFAULT_MODEL_USER_SORT);
      expect(sorted.map(c => [c.userName, c.thinkingLevel])).toEqual([['amy', 'high'], ['zed', 'low'], ['zed', 'high'], [null, 'high']]);
    });

    it('labels the directions by kind and the sort as the trigger shows it', () => {
      expect(modelUserDirectionLabel('number', 'desc')).toBe('Highest first');
      expect(modelUserDirectionLabel('number', 'asc')).toBe('Lowest first');
      expect(modelUserDirectionLabel('text', 'asc')).toBe('A–Z');
      expect(modelUserDirectionLabel('text', 'desc')).toBe('Z–A');
      expect(modelUserDirectionLabel('date', 'desc')).toBe('Newest first');
      expect(modelUserDirectionLabel('date', 'asc')).toBe('Oldest first');
      expect(modelUserSortLabel(DEFAULT_MODEL_USER_SORT)).toBe('Est. cost, highest first');
      expect(modelUserSortLabel({ field: 'name', direction: 'asc' })).toBe('User name, A–Z');
      expect(modelUserSortLabel({ field: 'lastRequest', direction: 'asc' })).toBe('Last request, oldest first');
      expect(MODEL_USER_SORT_FIELDS.map(f => f.field)).toEqual([
        'cost', 'requests', 'uncached', 'cacheRead', 'cacheCreated', 'output', 'totalTokens', 'cacheHit', 'duration', 'lastRequest', 'name'
      ]);
    });

    it('ranks the sorted scope, whatever the search later shows', () => {
      const sorted = sortModelUserCards(cards, DEFAULT_MODEL_USER_SORT);
      const ranks = modelUserRanks(sorted);
      const shown = searchModelUsers(sorted, 'carol');
      expect(shown.map(c => ranks.get(c.key))).toEqual([2]);
    });
  });

  describe('shares', () => {
    const cards = grouped(
      userRow({ userId: 'a', requests: 3, estimatedCost: 0.3, pricedRequests: 3, uncachedInputTokens: 30, cacheReadTokens: 0,
        cacheCreationTokens: 0, outputTokens: 10, promptTokens: 30 }),
      userRow({ userId: 'b', requests: 1, estimatedCost: 0, pricedRequests: 0, uncachedInputTokens: 10, cacheReadTokens: 0,
        cacheCreationTokens: 0, outputTokens: 30, promptTokens: 10 }));
    const total = sumModelUserCards(cards);

    it('takes each additive figure as a share of the scope total', () => {
      const a = modelUserShares(cards[0], total);
      expect(a.requests).toBeCloseTo(0.75);
      expect(a.cost).toBeCloseTo(1);
      expect(a.uncached).toBeCloseTo(0.75);
      expect(a.output).toBeCloseTo(0.25);
      expect(a.totalTokens).toBeCloseTo(0.5);
    });

    it('is null where the total is 0, and for the cost of a wholly unpriced card', () => {
      const b = modelUserShares(cards[1], total);
      expect(b.cost).toBeNull();
      expect(b.cacheRead).toBeNull();
      expect(b.cacheCreated).toBeNull();
      const unpricedTotal = sumModelUserCards([cards[1]]);
      expect(modelUserShares(cards[0], unpricedTotal).cost).toBeNull();
    });

    it('shows the sort field when it is additive, else requests', () => {
      expect(shareFieldOf({ field: 'cost', direction: 'desc' })).toBe('cost');
      expect(shareFieldOf({ field: 'totalTokens', direction: 'asc' })).toBe('totalTokens');
      expect(shareFieldOf({ field: 'cacheHit', direction: 'desc' })).toBe('requests');
      expect(shareFieldOf({ field: 'name', direction: 'asc' })).toBe('requests');
      expect(shareFieldOf({ field: 'lastRequest', direction: 'desc' })).toBe('requests');
    });
  });

  describe('modelUsersStatus', () => {
    it('counts the batch, the scope and its requests', () => {
      expect(modelUsersStatus({ scope: 312, matching: 312, shown: 50, search: '', requests: 12345 }))
        .toBe('Showing 50 of 312 users · 12,345 requests');
      expect(modelUsersStatus({ scope: 3, matching: 3, shown: 3, search: '', requests: 7 })).toBe('Showing 3 users · 7 requests');
      expect(modelUsersStatus({ scope: 1, matching: 1, shown: 1, search: '', requests: 1 })).toBe('One user · 1 request');
    });

    it('reads the search against the scope, and says when nothing matches', () => {
      expect(modelUsersStatus({ scope: 312, matching: 3, shown: 3, search: 'al', requests: 9 })).toBe('Showing 3 of 312 users');
      expect(modelUsersStatus({ scope: 312, matching: 80, shown: 50, search: 'a', requests: 9 })).toBe('Showing 50 of 80 matching users');
      expect(modelUsersStatus({ scope: 312, matching: 0, shown: 0, search: ' bob ', requests: 9 })).toBe('No users match "bob".');
    });

    it('is empty with no user in the scope', () => {
      expect(modelUsersStatus({ scope: 0, matching: 0, shown: 0, search: '', requests: 0 })).toBe('');
    });
  });

  describe('view storage', () => {
    it('reads the default when nothing is stored', () => {
      expect(readModelUsersView(memoryStorage())).toEqual({ separateThinkingLevels: false, sort: { field: 'cost', direction: 'desc' } });
    });

    it('round-trips a view', () => {
      const storage = memoryStorage();
      const view = { separateThinkingLevels: true, sort: { field: 'name' as const, direction: 'asc' as const } };
      writeModelUsersView(view, storage);
      expect(JSON.parse(storage.getItem(TELEMETRY_MODEL_USERS_STORAGE_KEY)!)).toEqual({ version: 1, ...view });
      expect(readModelUsersView(storage)).toEqual(view);
    });

    it('ignores another version and unreadable records', () => {
      const read = (raw: string) => readModelUsersView(memoryStorage({ [TELEMETRY_MODEL_USERS_STORAGE_KEY]: raw }));
      expect(read(JSON.stringify({ version: 2, separateThinkingLevels: true }))).toEqual(defaultModelUsersView());
      expect(read('{not json')).toEqual(defaultModelUsersView());
      expect(read('null')).toEqual(defaultModelUsersView());
    });

    it('falls back field by field', () => {
      const read = (fields: Record<string, unknown>) => readModelUsersView(memoryStorage({
        [TELEMETRY_MODEL_USERS_STORAGE_KEY]: JSON.stringify({ version: 1, ...fields })
      }));
      expect(read({ separateThinkingLevels: 'yes', sort: { field: 'requests', direction: 'asc' } }))
        .toEqual({ separateThinkingLevels: false, sort: { field: 'requests', direction: 'asc' } });
      expect(read({ separateThinkingLevels: true, sort: { field: 'providerErrors', direction: 'asc' } }))
        .toEqual({ separateThinkingLevels: true, sort: DEFAULT_MODEL_USER_SORT });
    });

    it('falls back when storage throws', () => {
      const throwing = memoryStorage();
      throwing.getItem = () => { throw new Error('denied'); };
      throwing.setItem = () => { throw new Error('denied'); };
      expect(readModelUsersView(throwing)).toEqual(defaultModelUsersView());
      expect(() => writeModelUsersView(defaultModelUsersView(), throwing)).not.toThrow();
    });
  });
});
