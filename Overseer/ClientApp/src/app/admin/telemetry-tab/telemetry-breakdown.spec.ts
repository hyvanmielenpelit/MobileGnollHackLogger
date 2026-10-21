import type { AiUsageBreakdownRowDto } from '../../services/admin.service';
import {
  BreakdownCard,
  BreakdownSortField,
  DEFAULT_BREAKDOWN_SORT,
  TELEMETRY_BREAKDOWN_STORAGE_KEY,
  TelemetryBreakdownView,
  breakdownDirectionLabel,
  breakdownProviderFacets,
  breakdownSortLabel,
  buildBreakdown,
  defaultBreakdownView,
  effectiveBreakdownProviders,
  filterBreakdownCards,
  readBreakdownView,
  roleCounts,
  sortBreakdownCards,
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

/** One card per row (each row a distinct model id), in the default order. */
function cardsOf(...rows: AiUsageBreakdownRowDto[]): BreakdownCard[] {
  return buildBreakdown(rows, ALL).all;
}

const titles = (cards: readonly BreakdownCard[]) => cards.map(c => c.modelName ?? c.modelId);

/** A view with every field set away from its default. */
const fullView = (): TelemetryBreakdownView => ({
  roles: ['title', 'other'], separateThinkingLevels: true, keys: 'both',
  sort: { field: 'cost', direction: 'asc' }, providers: ['google', 'openai']
});

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

    it('sorts by requests, then the title, then the thinking level in the catalog order, and keeps rows with no request', () => {
      const rows = [
        breakdownRow({ modelId: 'b', modelName: null, requests: 2 }),
        breakdownRow({ modelId: 'a', modelName: 'Zeta', requests: 2 }),
        breakdownRow({ modelId: 'c', modelName: 'Alpha', requests: 2 }),
        breakdownRow({ modelId: 'd', modelName: 'Delta', requests: 9 }),
        breakdownRow({ modelId: 'e', modelName: 'Echo', requests: 0 })
      ];

      expect(buildBreakdown(rows, ALL).cards.map(c => c.modelName ?? c.modelId)).toEqual(['Delta', 'Alpha', 'b', 'Zeta', 'Echo']);

      const levels = [
        breakdownRow({ thinkingLevel: '' }),
        breakdownRow({ thinkingLevel: 'high' }),
        breakdownRow({ thinkingLevel: null }),
        breakdownRow({ thinkingLevel: 'medium' }),
        breakdownRow({ thinkingLevel: 'low' })
      ];
      expect(buildBreakdown(levels, { ...ALL, separateThinkingLevels: true }).cards.map(c => thinkingLevelLabel(c.thinkingLevel)))
        .toEqual(['low', 'medium', 'high', 'Not recorded', 'None sent']);
    });

    it('lists the recorded thinking levels of a grouped card in the catalog order, without none sent and not recorded', () => {
      const rows = [
        breakdownRow({ thinkingLevel: 'High' }),
        breakdownRow({ thinkingLevel: '' }),
        breakdownRow({ thinkingLevel: 'medium' }),
        breakdownRow({ thinkingLevel: null }),
        breakdownRow({ thinkingLevel: ' high ' })
      ];

      expect(buildBreakdown(rows, ALL).cards[0].thinkingLevels).toEqual(['medium', 'high']);
      const separate = buildBreakdown(rows, { ...ALL, separateThinkingLevels: true }).cards;
      expect(separate.map(c => c.thinkingLevels)).toContainEqual([]);
      expect(separate.every(c => c.thinkingLevels.length <= 1)).toBe(true);
    });

    it('filters and sorts the shown cards, keeps every card in all, and totals only the shown ones', () => {
      const rows = [
        breakdownRow({ modelId: 'a', modelName: 'Alpha', requests: 5, estimatedCost: 1, pricedRequests: 5 }),
        breakdownRow({ provider: 'OpenAI', modelId: 'b', modelName: 'Beta', requests: 3, estimatedCost: 3, pricedRequests: 3 }),
        breakdownRow({ provider: 'OpenAI', modelId: 'c', modelName: 'Gamma', requests: 1, estimatedCost: 2, pricedRequests: 1 })
      ];

      const breakdown = buildBreakdown(rows, { ...ALL, providers: ['openai'], sort: { field: 'cost', direction: 'desc' } });
      expect(titles(breakdown.all)).toEqual(['Alpha', 'Beta', 'Gamma']);
      expect(titles(breakdown.cards)).toEqual(['Beta', 'Gamma']);
      expect(breakdown.total.requests).toBe(4);

      const searched = buildBreakdown(rows, { ...ALL, search: 'gam' });
      expect(titles(searched.cards)).toEqual(['Gamma']);
      expect(searched.total.requests).toBe(1);

      // The old two-field options still work as before.
      const plain = buildBreakdown(rows, ALL);
      expect(plain.cards).toEqual(plain.all);
      expect(plain.total.requests).toBe(9);
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

  describe('sortBreakdownCards', () => {
    const numeric: [BreakdownSortField, keyof AiUsageBreakdownRowDto][] = [
      ['requests', 'requests'], ['uncached', 'uncachedInputTokens'], ['cacheRead', 'cacheReadTokens'],
      ['cacheCreated', 'cacheCreationTokens'], ['output', 'outputTokens']
    ];

    it.each(numeric)('sorts by %s in both directions', (field, column) => {
      const cards = cardsOf(
        breakdownRow({ modelId: 'a', modelName: 'A', [column]: 2 }),
        breakdownRow({ modelId: 'b', modelName: 'B', [column]: 9 }),
        breakdownRow({ modelId: 'c', modelName: 'C', [column]: 5 })
      );

      expect(titles(sortBreakdownCards(cards, { field, direction: 'desc' }))).toEqual(['B', 'C', 'A']);
      expect(titles(sortBreakdownCards(cards, { field, direction: 'asc' }))).toEqual(['A', 'C', 'B']);
    });

    it('sorts the cost, the cache hit and the duration with unknown values last in both directions', () => {
      const cards = cardsOf(
        breakdownRow({ modelId: 'a', modelName: 'Cheap', requests: 2, pricedRequests: 2, estimatedCost: 0.1, promptTokens: 10, cacheReadTokens: 9, durationSumMs: 100, durationCount: 1 }),
        breakdownRow({ modelId: 'b', modelName: 'Unknown', requests: 2, pricedRequests: 0 }),
        breakdownRow({ modelId: 'c', modelName: 'Dear', requests: 2, pricedRequests: 2, estimatedCost: 5, promptTokens: 10, cacheReadTokens: 1, durationSumMs: 900, durationCount: 1 })
      );

      for (const field of ['cost', 'cacheHit', 'duration'] as const) {
        const desc = titles(sortBreakdownCards(cards, { field, direction: 'desc' }));
        const asc = titles(sortBreakdownCards(cards, { field, direction: 'asc' }));
        expect(desc[2]).toBe('Unknown');
        expect(asc[2]).toBe('Unknown');
      }
      expect(titles(sortBreakdownCards(cards, { field: 'cost', direction: 'asc' }))).toEqual(['Cheap', 'Dear', 'Unknown']);
      expect(titles(sortBreakdownCards(cards, { field: 'cacheHit', direction: 'desc' }))).toEqual(['Cheap', 'Dear', 'Unknown']);
      expect(titles(sortBreakdownCards(cards, { field: 'duration', direction: 'desc' }))).toEqual(['Dear', 'Cheap', 'Unknown']);
    });

    it('sorts provider errors by rate, Benchmark cards and cards without attempts last', () => {
      const cards = cardsOf(
        breakdownRow({ modelId: 'a', modelName: 'Low', validAttempts: 100, providerErrors: 1 }),
        breakdownRow({ modelId: 'b', modelName: 'Bench', roleGroup: 'benchmark', roleContext: 4, validAttempts: 10, providerErrors: 9 }),
        breakdownRow({ modelId: 'c', modelName: 'High', validAttempts: 10, providerErrors: 5 }),
        breakdownRow({ modelId: 'd', modelName: 'None', validAttempts: 0 })
      );

      expect(titles(sortBreakdownCards(cards, { field: 'providerErrors', direction: 'desc' }))).toEqual(['High', 'Low', 'Bench', 'None']);
      expect(titles(sortBreakdownCards(cards, { field: 'providerErrors', direction: 'asc' }))).toEqual(['Low', 'High', 'Bench', 'None']);
    });

    it('sorts the model name with numeric collation and the provider by name', () => {
      const cards = cardsOf(
        breakdownRow({ provider: 'OpenAI', modelId: 'a', modelName: 'GPT-6.10' }),
        breakdownRow({ provider: 'Google', modelId: 'b', modelName: 'gemini' }),
        breakdownRow({ provider: 'OpenAI', modelId: 'c', modelName: 'GPT-6.2' })
      );

      expect(titles(sortBreakdownCards(cards, { field: 'name', direction: 'asc' }))).toEqual(['gemini', 'GPT-6.2', 'GPT-6.10']);
      expect(titles(sortBreakdownCards(cards, { field: 'name', direction: 'desc' }))).toEqual(['GPT-6.10', 'GPT-6.2', 'gemini']);
      // Ties within a provider go by title A–Z in both directions.
      expect(titles(sortBreakdownCards(cards, { field: 'provider', direction: 'desc' }))).toEqual(['GPT-6.2', 'GPT-6.10', 'gemini']);
    });

    it('breaks ties by title, then thinking level, and leaves the input alone', () => {
      const rows = [
        breakdownRow({ modelId: 'b', modelName: 'Beta', thinkingLevel: 'high' }),
        breakdownRow({ modelId: 'b', modelName: 'Beta', thinkingLevel: 'low' }),
        breakdownRow({ modelId: 'a', modelName: 'Alpha', thinkingLevel: 'high' })
      ];
      const cards = buildBreakdown(rows, { ...ALL, separateThinkingLevels: true }).all;
      const before = [...cards];

      const sorted = sortBreakdownCards(cards, { field: 'requests', direction: 'asc' });

      expect(sorted.map(c => `${c.modelName} ${c.thinkingLevel}`)).toEqual(['Alpha high', 'Beta low', 'Beta high']);
      expect(cards).toEqual(before);
      expect(sorted).not.toBe(cards);
    });

    it('labels the sort and its direction by the kind of field', () => {
      expect(breakdownSortLabel(DEFAULT_BREAKDOWN_SORT)).toBe('Requests, highest first');
      expect(breakdownSortLabel({ field: 'cost', direction: 'asc' })).toBe('Est. cost, lowest first');
      expect(breakdownSortLabel({ field: 'name', direction: 'asc' })).toBe('Model name, A–Z');
      expect(breakdownDirectionLabel('text', 'desc')).toBe('Z–A');
      expect(breakdownDirectionLabel('number', 'desc')).toBe('Highest first');
    });
  });

  describe('filterBreakdownCards', () => {
    const cards = () => cardsOf(
      breakdownRow({ provider: 'Google', modelId: 'gemini-flash', modelName: 'Gemini Flash' }),
      breakdownRow({ provider: 'Anthropic', modelId: 'claude-opus', modelName: 'Claude Opus' }),
      breakdownRow({ provider: 'OpenAI', modelId: 'gpt-x', modelName: null }),
      breakdownRow({ keySource: 'user', userId: 'u-1', userName: 'Alice', provider: 'Anthropic', modelId: 'claude-haiku', modelName: 'Claude Haiku' })
    );

    it('needs every term to match the name, the id, the provider or the user name, ignoring case', () => {
      expect(titles(filterBreakdownCards(cards(), { search: 'FLASH', providers: [] }))).toEqual(['Gemini Flash']);
      expect(titles(filterBreakdownCards(cards(), { search: 'anthropic opus', providers: [] }))).toEqual(['Claude Opus']);
      expect(titles(filterBreakdownCards(cards(), { search: 'gpt-x', providers: [] }))).toEqual(['gpt-x']);
      expect(titles(filterBreakdownCards(cards(), { search: 'alice', providers: [] }))).toEqual(['Claude Haiku']);
      expect(filterBreakdownCards(cards(), { search: 'zzz', providers: [] })).toEqual([]);
      expect(filterBreakdownCards(cards(), { search: '  ', providers: [] }).length).toBe(4);
    });

    it('ORs the providers and ANDs them with the search', () => {
      expect(titles(filterBreakdownCards(cards(), { search: '', providers: ['google', 'openai'] }))).toEqual(['Gemini Flash', 'gpt-x']);
      expect(titles(filterBreakdownCards(cards(), { search: 'claude', providers: ['google'] }))).toEqual([]);
    });

    it('counts cards per provider by name, and ignores a stored provider no card has', () => {
      const facets = breakdownProviderFacets(cards());
      expect(facets).toEqual([
        { provider: 'Anthropic', key: 'anthropic', count: 2 },
        { provider: 'Google', key: 'google', count: 1 },
        { provider: 'OpenAI', key: 'openai', count: 1 }
      ]);
      expect(effectiveBreakdownProviders(['xai', 'google'], facets)).toEqual(['google']);
      expect(buildBreakdown([breakdownRow()], { ...ALL, providers: ['xai'] }).cards.length).toBe(1);
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
    it('falls back to the three named roles, grouped levels, system keys, most requests first and every provider', () => {
      expect(readBreakdownView(memoryStorage())).toEqual({
        roles: ['chat', 'title', 'benchmark'], separateThinkingLevels: false, keys: 'system',
        sort: { field: 'requests', direction: 'desc' }, providers: []
      });
      expect(readBreakdownView(null)).toEqual(defaultBreakdownView());
    });

    it('round-trips the view under its key with version 1, without the search', () => {
      const storage = memoryStorage();
      writeBreakdownView(fullView(), storage);

      expect(JSON.parse(storage.getItem(TELEMETRY_BREAKDOWN_STORAGE_KEY)!)).toEqual({
        version: 1, roles: ['title', 'other'], separateThinkingLevels: true, keys: 'both',
        sort: { field: 'cost', direction: 'asc' }, providers: ['google', 'openai']
      });
      expect(readBreakdownView(storage)).toEqual(fullView());
    });

    it('reads a record written before the sort and providers existed with their defaults', () => {
      const storage = memoryStorage({
        [TELEMETRY_BREAKDOWN_STORAGE_KEY]: JSON.stringify({ version: 1, roles: ['title'], separateThinkingLevels: true, keys: 'user' })
      });
      expect(readBreakdownView(storage)).toEqual({
        roles: ['title'], separateThinkingLevels: true, keys: 'user',
        sort: DEFAULT_BREAKDOWN_SORT, providers: []
      });
    });

    it('reads a record that still carries the retired toolbarCollapsed field to the full view without it', () => {
      const storage = memoryStorage({
        [TELEMETRY_BREAKDOWN_STORAGE_KEY]: JSON.stringify({ ...fullView(), version: 1, toolbarCollapsed: true })
      });
      const view = readBreakdownView(storage);
      expect(view).toEqual(fullView());
      expect('toolbarCollapsed' in view).toBe(false);
    });

    it('keeps an empty role selection', () => {
      const storage = memoryStorage();
      writeBreakdownView({ ...defaultBreakdownView(), roles: [] }, storage);
      expect(readBreakdownView(storage).roles).toEqual([]);
    });

    it('ignores garbage, another version and unknown values', () => {
      const read = (raw: string) => readBreakdownView(memoryStorage({ [TELEMETRY_BREAKDOWN_STORAGE_KEY]: raw }));

      expect(read('not json')).toEqual(defaultBreakdownView());
      expect(read('null')).toEqual(defaultBreakdownView());
      expect(read(JSON.stringify({ version: 2, roles: ['chat'], separateThinkingLevels: true, keys: 'user' }))).toEqual(defaultBreakdownView());
      expect(read(JSON.stringify({ version: 1, roles: ['chat', 'bogus'], separateThinkingLevels: 'yes', keys: 'all' })))
        .toEqual({ ...defaultBreakdownView(), roles: ['chat'] });
      expect(read(JSON.stringify({ version: 1, roles: 'chat', keys: 'user' })))
        .toEqual({ ...defaultBreakdownView(), keys: 'user' });
    });

    it('falls back field by field for an invalid sort or providers value', () => {
      const read = (fields: Record<string, unknown>) => readBreakdownView(memoryStorage({
        [TELEMETRY_BREAKDOWN_STORAGE_KEY]: JSON.stringify({ ...fullView(), version: 1, ...fields })
      }));

      expect(read({ sort: { field: 'bogus', direction: 'asc' } })).toEqual({ ...fullView(), sort: DEFAULT_BREAKDOWN_SORT });
      expect(read({ sort: { field: 'cost', direction: 'sideways' } })).toEqual({ ...fullView(), sort: DEFAULT_BREAKDOWN_SORT });
      expect(read({ sort: 'cost' })).toEqual({ ...fullView(), sort: DEFAULT_BREAKDOWN_SORT });
      expect(read({ providers: 'google' })).toEqual({ ...fullView(), providers: [] });
      expect(read({ providers: [' Google ', 'google', 3, '', 'OpenAI'] })).toEqual({ ...fullView(), providers: ['google', 'openai'] });
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
