import type { AiUsageBreakdownRowDto } from '../../services/admin.service';
import {
  TELEMETRY_BREAKDOWN_STORAGE_KEY,
  buildBreakdown,
  defaultBreakdownView,
  readBreakdownView,
  roleCounts,
  thinkingLevelLabel,
  writeBreakdownView
} from './telemetry-breakdown';

/** A system-key Chat row of one request with every figure at zero. */
function breakdownRow(overrides: Partial<AiUsageBreakdownRowDto> = {}): AiUsageBreakdownRowDto {
  return {
    keySource: 'system', userId: null, userName: null,
    provider: 'Anthropic', modelId: 'claude-x', modelName: 'Claude X',
    roleContext: 1, roleGroup: 'chat', thinkingLevel: 'high',
    requests: 1, promptTokens: 0, uncachedInputTokens: 0, cacheReadTokens: 0, cacheCreationTokens: 0, outputTokens: 0,
    durationSumMs: 0, durationCount: 0, estimatedCost: 0, pricedRequests: 0,
    validAttempts: 0, providerErrors: 0, unattributedErrors: 0, ourSideFailures: 0,
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

const ALL = { roles: ['chat', 'title', 'benchmark', 'other'] as const, separateThinkingLevels: false };

describe('telemetry breakdown', () => {
  describe('thinkingLevelLabel', () => {
    it('says Not recorded for null, None sent for an empty level, else the level', () => {
      expect(thinkingLevelLabel(null)).toBe('Not recorded');
      expect(thinkingLevelLabel(undefined)).toBe('Not recorded');
      expect(thinkingLevelLabel('')).toBe('None sent');
      expect(thinkingLevelLabel('high')).toBe('high');
    });
  });

  describe('buildBreakdown', () => {
    it('keeps only the selected role groups', () => {
      const rows = [
        breakdownRow({ roleGroup: 'chat', requests: 5 }),
        breakdownRow({ roleGroup: 'title', roleContext: 2, requests: 3 }),
        breakdownRow({ roleGroup: 'benchmark', roleContext: 4, requests: 2 })
      ];

      const { cards, total } = buildBreakdown(rows, { roles: ['chat', 'benchmark'], separateThinkingLevels: false });

      expect(cards.map(c => c.roleGroup)).toEqual(['chat', 'benchmark']);
      expect(total.requests).toBe(7);
      expect(buildBreakdown(rows, { roles: [], separateThinkingLevels: false }).cards).toEqual([]);
    });

    it('groups thinking levels together by default and splits them when separate', () => {
      const rows = [
        breakdownRow({ thinkingLevel: 'high', requests: 4 }),
        breakdownRow({ thinkingLevel: 'low', requests: 2 }),
        breakdownRow({ thinkingLevel: '', requests: 1 }),
        breakdownRow({ thinkingLevel: null, requests: 1 })
      ];

      const grouped = buildBreakdown(rows, ALL);
      expect(grouped.cards.length).toBe(1);
      expect(grouped.cards[0].requests).toBe(8);
      expect(grouped.cards[0].thinkingLevel).toBeUndefined();

      const separate = buildBreakdown(rows, { ...ALL, separateThinkingLevels: true });
      expect(separate.cards.map(c => thinkingLevelLabel(c.thinkingLevel))).toEqual(['high', 'low', 'Not recorded', 'None sent']);
      expect(separate.total.requests).toBe(8);
    });

    it('separates key sources and user-key rows by user, keeping the user name', () => {
      const rows = [
        breakdownRow({ requests: 3 }),
        breakdownRow({ keySource: 'user', userId: 'u-1', userName: 'alice', requests: 2 }),
        breakdownRow({ keySource: 'user', userId: 'u-1', userName: 'alice', requests: 2 }),
        breakdownRow({ keySource: 'user', userId: 'u-2', userName: 'bob', requests: 1 })
      ];

      const { cards } = buildBreakdown(rows, ALL);

      expect(cards.map(c => [c.keySource, c.userName, c.requests])).toEqual([
        ['user', 'alice', 4],
        ['system', null, 3],
        ['user', 'bob', 1]
      ]);
    });

    it('derives the cache hit and the mean duration from the sums, null without a denominator', () => {
      const rows = [
        breakdownRow({ promptTokens: 100, cacheReadTokens: 90, durationSumMs: 1000, durationCount: 1 }),
        breakdownRow({ promptTokens: 300, cacheReadTokens: 10, durationSumMs: 3000, durationCount: 3 })
      ];

      const card = buildBreakdown(rows, ALL).cards[0];
      expect(card.cacheHitRatio).toBeCloseTo(0.25, 10);
      expect(card.avgDurationMs).toBe(1000);

      const empty = buildBreakdown([breakdownRow()], ALL).cards[0];
      expect(empty.cacheHitRatio).toBeNull();
      expect(empty.avgDurationMs).toBeNull();
    });

    it('sums the cost and works out the priced share and the unpriced requests', () => {
      const rows = [
        breakdownRow({ requests: 3, estimatedCost: 0.5, pricedRequests: 3 }),
        breakdownRow({ requests: 1, estimatedCost: 0, pricedRequests: 0 })
      ];

      const card = buildBreakdown(rows, ALL).cards[0];
      expect(card.estimatedCost).toBe(0.5);
      expect(card.pricedShare).toBe(0.75);
      expect(card.unpricedRequests).toBe(1);
      expect(buildBreakdown([breakdownRow({ requests: 0 })], ALL).cards[0].pricedShare).toBeNull();
    });

    it('sorts by requests, then the title, then the thinking level, and keeps rows with no request', () => {
      const rows = [
        breakdownRow({ modelId: 'b', modelName: null, requests: 2 }),
        breakdownRow({ modelId: 'a', modelName: 'Zeta', requests: 2 }),
        breakdownRow({ modelId: 'c', modelName: 'Alpha', requests: 2 }),
        breakdownRow({ modelId: 'd', modelName: 'Delta', requests: 9 }),
        breakdownRow({ modelId: 'e', modelName: 'Echo', requests: 0 })
      ];

      expect(buildBreakdown(rows, ALL).cards.map(c => c.modelName ?? c.modelId)).toEqual(['Delta', 'Alpha', 'b', 'Zeta', 'Echo']);
    });

    it('totals every card shown', () => {
      const rows = [
        breakdownRow({ requests: 2, outputTokens: 10, providerErrors: 1, validAttempts: 4 }),
        breakdownRow({ modelId: 'other', requests: 3, outputTokens: 5, providerErrors: 0, validAttempts: 6 }),
        breakdownRow({ roleGroup: 'title', requests: 7, outputTokens: 100 })
      ];

      const { total } = buildBreakdown(rows, { roles: ['chat'], separateThinkingLevels: false });

      expect(total.rowCount).toBe(2);
      expect(total.requests).toBe(5);
      expect(total.outputTokens).toBe(15);
      expect(total.providerErrors).toBe(1);
      expect(total.validAttempts).toBe(10);
    });
  });

  describe('roleCounts', () => {
    it('counts requests per role group over every row', () => {
      const counts = roleCounts([
        breakdownRow({ requests: 2 }),
        breakdownRow({ roleGroup: 'title', requests: 3 }),
        breakdownRow({ roleGroup: 'other', requests: 1 })
      ]);
      expect(counts).toEqual({ chat: 2, title: 3, benchmark: 0, other: 1 });
    });
  });

  describe('view storage', () => {
    it('falls back to the three named roles, grouped levels and system keys', () => {
      expect(readBreakdownView(memoryStorage())).toEqual({ roles: ['chat', 'title', 'benchmark'], separateThinkingLevels: false, keys: 'system' });
      expect(readBreakdownView(null)).toEqual(defaultBreakdownView());
    });

    it('round-trips the view under its key with version 1', () => {
      const storage = memoryStorage();
      writeBreakdownView({ roles: ['title', 'other'], separateThinkingLevels: true, keys: 'both' }, storage);

      expect(JSON.parse(storage.getItem(TELEMETRY_BREAKDOWN_STORAGE_KEY)!)).toEqual({
        version: 1, roles: ['title', 'other'], separateThinkingLevels: true, keys: 'both'
      });
      expect(readBreakdownView(storage)).toEqual({ roles: ['title', 'other'], separateThinkingLevels: true, keys: 'both' });
    });

    it('keeps an empty role selection', () => {
      const storage = memoryStorage();
      writeBreakdownView({ roles: [], separateThinkingLevels: false, keys: 'system' }, storage);
      expect(readBreakdownView(storage).roles).toEqual([]);
    });

    it('ignores garbage, another version and unknown values', () => {
      const read = (raw: string) => readBreakdownView(memoryStorage({ [TELEMETRY_BREAKDOWN_STORAGE_KEY]: raw }));

      expect(read('not json')).toEqual(defaultBreakdownView());
      expect(read('null')).toEqual(defaultBreakdownView());
      expect(read(JSON.stringify({ version: 2, roles: ['chat'], separateThinkingLevels: true, keys: 'user' }))).toEqual(defaultBreakdownView());
      expect(read(JSON.stringify({ version: 1, roles: ['chat', 'bogus'], separateThinkingLevels: 'yes', keys: 'all' })))
        .toEqual({ roles: ['chat'], separateThinkingLevels: false, keys: 'system' });
      expect(read(JSON.stringify({ version: 1, roles: 'chat', keys: 'user' })))
        .toEqual({ roles: ['chat', 'title', 'benchmark'], separateThinkingLevels: false, keys: 'user' });
    });

    it('falls back when storage throws', () => {
      const throwing = memoryStorage();
      throwing.getItem = () => { throw new Error('denied'); };
      throwing.setItem = () => { throw new Error('denied'); };

      expect(readBreakdownView(throwing)).toEqual(defaultBreakdownView());
      expect(() => writeBreakdownView(defaultBreakdownView(), throwing)).not.toThrow();
    });
  });
});
