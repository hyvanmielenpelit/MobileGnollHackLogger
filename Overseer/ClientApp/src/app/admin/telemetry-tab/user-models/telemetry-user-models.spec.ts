import type { AiTelemetryUserModelRowDto } from '../../../services/admin.service';
import {
  ALL_USER_MODEL_ROWS,
  DEFAULT_USER_MODEL_SORT,
  TELEMETRY_USER_MODELS_STORAGE_KEY,
  USER_MODEL_SORT_FIELDS,
  UserModelCard,
  UserModelSortField,
  buildUserModelCards,
  defaultUserModelsView,
  distinctModelCount,
  filterUserModelRows,
  keyOptions,
  readUserModelsView,
  roleOptions,
  searchUserModels,
  sortUserModelCards,
  sumUserModelCards,
  topUserModelText,
  userModelDirectionLabel,
  userModelLevelOptions,
  userModelRanks,
  userModelShareFieldOf,
  userModelSortLabel,
  userModelsStatus,
  writeUserModelsView
} from './telemetry-user-models';
import { modelUserShares } from '../model-users/telemetry-model-users';

/** A system-key Chat row of one request with every figure at zero. */
function modelRow(overrides: Partial<AiTelemetryUserModelRowDto> = {}): AiTelemetryUserModelRowDto {
  return {
    keySource: 'system', provider: 'Anthropic', modelId: 'claude-x', modelName: 'Claude X', roleContext: 1, roleGroup: 'chat',
    thinkingLevel: 'high', requests: 1, promptTokens: 0, uncachedInputTokens: 0, cacheReadTokens: 0, cacheCreationTokens: 0,
    outputTokens: 0, durationSumMs: 0, durationCount: 0, estimatedCost: 0, pricedRequests: 1, lastRequestUtc: '2026-09-10T12:00:00Z',
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

const grouped = (...rows: AiTelemetryUserModelRowDto[]) => buildUserModelCards(rows, { separateThinkingLevels: false });
const titles = (cards: readonly UserModelCard[]) => cards.map(c => c.title);

describe('telemetry-user-models', () => {
  describe('buildUserModelCards', () => {
    const rows = [
      modelRow({ thinkingLevel: 'high', requests: 2, estimatedCost: 0.2, pricedRequests: 2, promptTokens: 100, outputTokens: 10,
        lastRequestUtc: '2026-09-10T10:00:00Z' }),
      modelRow({ thinkingLevel: 'Low', requests: 1, estimatedCost: 0.1, promptTokens: 50, outputTokens: 5,
        lastRequestUtc: '2026-09-11T08:00:00Z' }),
      modelRow({ roleContext: 3, thinkingLevel: 'low', requests: 3, estimatedCost: 0.3, pricedRequests: 3 }),
      modelRow({ roleGroup: 'benchmark', thinkingLevel: null }),
      modelRow({ keySource: 'user', thinkingLevel: '' }),
      modelRow({ provider: 'OpenAI', modelId: 'gpt-x', modelName: null, thinkingLevel: undefined })
    ];

    it('groups by key source, provider, model and role group, summing every level and keeping the latest request', () => {
      const cards = grouped(...rows);
      expect(cards.length).toBe(4);
      const chat = cards.find(c => c.keySource === 'system' && c.modelId === 'claude-x' && c.roleGroup === 'chat')!;
      expect(chat.requests).toBe(6);
      expect(chat.estimatedCost).toBeCloseTo(0.6);
      expect(chat.rowCount).toBe(3);
      expect(chat.thinkingLevel).toBeUndefined();
      expect(chat.thinkingLevels).toEqual(['low', 'high']);
      expect(chat.lastRequestUtc).toBe('2026-09-11T08:00:00Z');
      expect(chat.totalTokens).toBe(165);
      expect(chat.title).toBe('Claude X');
      expect(chat.modelName).toBe('Claude X');
      expect(cards.find(c => c.roleGroup === 'benchmark')!.thinkingLevels).toEqual([]);
      expect(cards.find(c => c.keySource === 'user')!.requests).toBe(1);
      const gpt = cards.find(c => c.modelId === 'gpt-x')!;
      expect(gpt.modelName).toBeNull();
      expect(gpt.title).toBe('gpt-x');
    });

    it('splits a card into one per level, case-insensitively, keeping none sent apart from not recorded', () => {
      const cards = buildUserModelCards(rows, { separateThinkingLevels: true });
      const chat = cards.filter(c => c.keySource === 'system' && c.modelId === 'claude-x' && c.roleGroup === 'chat');
      expect(chat.map(c => [c.thinkingLevel, c.requests])).toEqual([['high', 2], ['low', 4]]);
      expect(cards.find(c => c.roleGroup === 'benchmark')!.thinkingLevel).toBeNull();
      expect(cards.find(c => c.keySource === 'user')!.thinkingLevel).toBe('');
      expect(cards.find(c => c.modelId === 'gpt-x')!.thinkingLevel).toBeNull();
      expect(new Set(cards.map(c => c.key)).size).toBe(cards.length);
    });

    it('takes a later row\'s model name when the first has none', () => {
      const cards = grouped(modelRow({ modelName: null }), modelRow({ modelName: ' Claude X ' }));
      expect(cards[0].modelName).toBe('Claude X');
      expect(cards[0].title).toBe('Claude X');
    });

    it('counts the distinct models, whatever the role, key or level', () => {
      expect(distinctModelCount(buildUserModelCards(rows, { separateThinkingLevels: true }))).toBe(2);
      expect(distinctModelCount([])).toBe(0);
    });
  });

  describe('filters', () => {
    const rows = [
      modelRow({ thinkingLevel: 'high' }),
      modelRow({ roleGroup: 'other', thinkingLevel: 'Low' }),
      modelRow({ roleGroup: 'benchmark', keySource: 'user', thinkingLevel: '' }),
      modelRow({ roleGroup: 'title', thinkingLevel: null }),
      modelRow({ roleGroup: 'chat', keySource: 'user', thinkingLevel: 'low', requests: 5 })
    ];

    it('filters by role, key and level before grouping, so a card sums the kept rows only', () => {
      expect(filterUserModelRows(rows, ALL_USER_MODEL_ROWS).length).toBe(5);
      expect(filterUserModelRows(rows, { ...ALL_USER_MODEL_ROWS, role: 'chat' }).length).toBe(2);
      expect(filterUserModelRows(rows, { ...ALL_USER_MODEL_ROWS, key: 'user' }).length).toBe(2);
      expect(filterUserModelRows(rows, { ...ALL_USER_MODEL_ROWS, level: '=low' }).length).toBe(2);
      expect(filterUserModelRows(rows, { ...ALL_USER_MODEL_ROWS, level: 'none' }).length).toBe(1);
      expect(filterUserModelRows(rows, { ...ALL_USER_MODEL_ROWS, level: 'unrecorded' }).length).toBe(1);
      const kept = filterUserModelRows(rows, { role: 'chat', key: 'user', level: '=low' });
      expect(kept.length).toBe(1);
      expect(grouped(...kept)[0].requests).toBe(5);
    });

    it('offers all roles, then the present role groups in the display order', () => {
      expect(roleOptions(rows)).toEqual([
        { value: 'all', label: 'All roles' },
        { value: 'chat', label: 'Chat' },
        { value: 'title', label: 'Title Generation' },
        { value: 'benchmark', label: 'Benchmark' },
        { value: 'other', label: 'Other' }
      ]);
      expect(roleOptions([modelRow()])).toEqual([{ value: 'all', label: 'All roles' }, { value: 'chat', label: 'Chat' }]);
    });

    it('offers all keys, then the present key sources', () => {
      expect(keyOptions(rows)).toEqual([
        { value: 'all', label: 'All keys' },
        { value: 'system', label: 'System AI configurations' },
        { value: 'user', label: 'User keys' }
      ]);
      expect(keyOptions([modelRow({ keySource: 'user' })])).toEqual([
        { value: 'all', label: 'All keys' }, { value: 'user', label: 'User keys' }
      ]);
    });

    it('offers all levels, the recorded levels in the catalog order, then none sent and not recorded', () => {
      expect(userModelLevelOptions(rows)).toEqual([
        { value: '*', label: 'All levels' },
        { value: '=low', label: 'Low' },
        { value: '=high', label: 'High' },
        { value: 'none', label: 'None sent' },
        { value: 'unrecorded', label: 'Not recorded' }
      ]);
      expect(userModelLevelOptions([modelRow()])).toEqual([{ value: '*', label: 'All levels' }, { value: '=high', label: 'High' }]);
    });
  });

  describe('search', () => {
    const cards = grouped(
      modelRow({ modelId: 'claude-5-opus', modelName: 'Claude 5 Opus' }),
      modelRow({ provider: 'OpenAI', modelId: 'gpt-x', modelName: null }),
      modelRow({ provider: 'Google', modelId: 'gemini-y', modelName: 'Gemini Y' }));

    it('matches every term against the title, the model id and the provider, keeping the order', () => {
      expect(titles(searchUserModels(cards, 'OPUS anthropic'))).toEqual(['Claude 5 Opus']);
      expect(titles(searchUserModels(cards, 'openai'))).toEqual(['gpt-x']);
      expect(titles(searchUserModels(cards, 'gemini-y'))).toEqual(['Gemini Y']);
      expect(titles(searchUserModels(cards, ''))).toEqual(['Claude 5 Opus', 'gpt-x', 'Gemini Y']);
      expect(searchUserModels(cards, 'opus google')).toEqual([]);
    });
  });

  describe('sort', () => {
    const cards = grouped(
      modelRow({ provider: 'OpenAI', modelId: 'c', modelName: 'Carol', requests: 5, estimatedCost: 0.5, pricedRequests: 5,
        uncachedInputTokens: 30, cacheReadTokens: 10, cacheCreationTokens: 1, outputTokens: 7, promptTokens: 40,
        durationSumMs: 500, durationCount: 5, lastRequestUtc: '2026-09-10T00:00:00Z' }),
      modelRow({ provider: 'Anthropic', modelId: 'a', modelName: 'Alpha', requests: 1, estimatedCost: 0.9, pricedRequests: 1,
        uncachedInputTokens: 10, cacheReadTokens: 30, cacheCreationTokens: 3, outputTokens: 2, promptTokens: 50,
        durationSumMs: 300, durationCount: 1, lastRequestUtc: '2026-09-12T00:00:00Z' }),
      modelRow({ provider: 'Google', modelId: 'b', modelName: 'Bravo', requests: 3, estimatedCost: 0, pricedRequests: 0,
        uncachedInputTokens: 20, cacheReadTokens: 0, cacheCreationTokens: 2, outputTokens: 1, promptTokens: 0,
        durationSumMs: 0, durationCount: 0, lastRequestUtc: null }));

    const order = (field: UserModelSortField, direction: 'asc' | 'desc' = 'desc') =>
      titles(sortUserModelCards(cards, { field, direction }));

    it('sorts by each figure both ways, a missing figure last in both', () => {
      expect(order('cost')).toEqual(['Alpha', 'Carol', 'Bravo']);
      expect(order('cost', 'asc')).toEqual(['Carol', 'Alpha', 'Bravo']);
      expect(order('requests')).toEqual(['Carol', 'Bravo', 'Alpha']);
      expect(order('requests', 'asc')).toEqual(['Alpha', 'Bravo', 'Carol']);
      expect(order('uncached')).toEqual(['Carol', 'Bravo', 'Alpha']);
      expect(order('uncached', 'asc')).toEqual(['Alpha', 'Bravo', 'Carol']);
      expect(order('cacheRead')).toEqual(['Alpha', 'Carol', 'Bravo']);
      expect(order('cacheRead', 'asc')).toEqual(['Bravo', 'Carol', 'Alpha']);
      expect(order('cacheCreated')).toEqual(['Alpha', 'Bravo', 'Carol']);
      expect(order('cacheCreated', 'asc')).toEqual(['Carol', 'Bravo', 'Alpha']);
      expect(order('output')).toEqual(['Carol', 'Alpha', 'Bravo']);
      expect(order('output', 'asc')).toEqual(['Bravo', 'Alpha', 'Carol']);
      expect(order('totalTokens')).toEqual(['Alpha', 'Carol', 'Bravo']);
      expect(order('totalTokens', 'asc')).toEqual(['Bravo', 'Carol', 'Alpha']);
      expect(order('cacheHit')).toEqual(['Alpha', 'Carol', 'Bravo']);
      expect(order('cacheHit', 'asc')).toEqual(['Carol', 'Alpha', 'Bravo']);
      expect(order('duration')).toEqual(['Alpha', 'Carol', 'Bravo']);
      expect(order('duration', 'asc')).toEqual(['Carol', 'Alpha', 'Bravo']);
      expect(order('lastRequest')).toEqual(['Alpha', 'Carol', 'Bravo']);
      expect(order('lastRequest', 'asc')).toEqual(['Carol', 'Alpha', 'Bravo']);
    });

    it('sorts by model name and by provider both ways', () => {
      expect(order('name', 'asc')).toEqual(['Alpha', 'Bravo', 'Carol']);
      expect(order('name')).toEqual(['Carol', 'Bravo', 'Alpha']);
      expect(order('provider', 'asc')).toEqual(['Alpha', 'Bravo', 'Carol']);
      expect(order('provider')).toEqual(['Carol', 'Bravo', 'Alpha']);
    });

    it('breaks ties by title, then role group, thinking level, key source and key', () => {
      const tied = buildUserModelCards([
        modelRow({ modelName: 'Zed', keySource: 'user', roleGroup: 'chat', thinkingLevel: 'high' }),
        modelRow({ modelName: 'Zed', keySource: 'system', roleGroup: 'chat', thinkingLevel: 'high' }),
        modelRow({ modelName: 'Zed', keySource: 'system', roleGroup: 'chat', thinkingLevel: 'low' }),
        modelRow({ modelName: 'Zed', keySource: 'system', roleGroup: 'benchmark', thinkingLevel: 'high' }),
        modelRow({ modelName: 'Zed', keySource: 'system', roleGroup: 'title', thinkingLevel: 'high' }),
        modelRow({ modelId: 'amy', modelName: 'Amy', roleGroup: 'other', thinkingLevel: 'high' })
      ], { separateThinkingLevels: true });
      const sorted = sortUserModelCards(tied, DEFAULT_USER_MODEL_SORT);
      expect(sorted.map(c => [c.title, c.roleGroup, c.thinkingLevel, c.keySource])).toEqual([
        ['Amy', 'other', 'high', 'system'],
        ['Zed', 'chat', 'low', 'system'],
        ['Zed', 'chat', 'high', 'system'],
        ['Zed', 'chat', 'high', 'user'],
        ['Zed', 'title', 'high', 'system'],
        ['Zed', 'benchmark', 'high', 'system']
      ]);
    });

    it('labels the directions by kind and the sort as the trigger shows it', () => {
      expect(userModelDirectionLabel('number', 'desc')).toBe('Highest first');
      expect(userModelDirectionLabel('text', 'asc')).toBe('A–Z');
      expect(userModelDirectionLabel('date', 'desc')).toBe('Newest first');
      expect(userModelDirectionLabel('date', 'asc')).toBe('Oldest first');
      expect(userModelSortLabel(DEFAULT_USER_MODEL_SORT)).toBe('Est. cost, highest first');
      expect(userModelSortLabel({ field: 'name', direction: 'asc' })).toBe('Model name, A–Z');
      expect(userModelSortLabel({ field: 'provider', direction: 'desc' })).toBe('Provider, Z–A');
      expect(userModelSortLabel({ field: 'lastRequest', direction: 'asc' })).toBe('Last request, oldest first');
      expect(USER_MODEL_SORT_FIELDS.map(f => f.field)).toEqual([
        'cost', 'requests', 'uncached', 'cacheRead', 'cacheCreated', 'output', 'totalTokens', 'cacheHit', 'duration',
        'lastRequest', 'name', 'provider'
      ]);
    });

    it('ranks the sorted scope, whatever the search later shows', () => {
      const sorted = sortUserModelCards(cards, DEFAULT_USER_MODEL_SORT);
      const ranks = userModelRanks(sorted);
      const shown = searchUserModels(sorted, 'carol');
      expect(shown.map(c => ranks.get(c.key))).toEqual([2]);
    });
  });

  describe('shares and summary', () => {
    const cards = grouped(
      modelRow({ modelId: 'a', modelName: 'Claude 5 Opus', roleGroup: 'benchmark', requests: 3, estimatedCost: 0.3, pricedRequests: 3,
        uncachedInputTokens: 30, outputTokens: 10, promptTokens: 30 }),
      modelRow({ modelId: 'b', modelName: 'Unpriced', requests: 1, estimatedCost: 0, pricedRequests: 0,
        uncachedInputTokens: 10, outputTokens: 30, promptTokens: 10 }));
    const total = sumUserModelCards(cards);

    it('takes each additive figure as a share of the scope total', () => {
      const a = modelUserShares(cards[0], total);
      expect(a.requests).toBeCloseTo(0.75);
      expect(a.cost).toBeCloseTo(1);
      expect(a.totalTokens).toBeCloseTo(0.5);
      expect(modelUserShares(cards[1], total).cost).toBeNull();
    });

    it('shows the sort field when it is additive, else requests', () => {
      expect(userModelShareFieldOf({ field: 'cost', direction: 'desc' })).toBe('cost');
      expect(userModelShareFieldOf({ field: 'output', direction: 'asc' })).toBe('output');
      expect(userModelShareFieldOf({ field: 'cacheHit', direction: 'desc' })).toBe('requests');
      expect(userModelShareFieldOf({ field: 'name', direction: 'asc' })).toBe('requests');
      expect(userModelShareFieldOf({ field: 'provider', direction: 'asc' })).toBe('requests');
    });

    it('names the top model with its role and share', () => {
      const sorted = sortUserModelCards(cards, DEFAULT_USER_MODEL_SORT);
      expect(topUserModelText(sorted, total, 'cost')).toBe('Claude 5 Opus · Benchmark · 100% of est. cost');
      expect(topUserModelText(sorted, total, 'requests')).toBe('Claude 5 Opus · Benchmark · 75% of requests');
      expect(topUserModelText([cards[1]], sumUserModelCards([cards[1]]), 'cost')).toBe('Unpriced · Chat');
      expect(topUserModelText([], total, 'cost')).toBe('—');
    });
  });

  describe('userModelsStatus', () => {
    it('counts the batch, the scope and its requests', () => {
      expect(userModelsStatus({ scope: 12, matching: 12, shown: 12, search: '', requests: 1033 }))
        .toBe('Showing 12 entries · 1,033 requests');
      expect(userModelsStatus({ scope: 12, matching: 12, shown: 5, search: '', requests: 1033 }))
        .toBe('Showing 5 of 12 entries · 1,033 requests');
      expect(userModelsStatus({ scope: 1, matching: 1, shown: 1, search: '', requests: 1 })).toBe('One entry · 1 request');
    });

    it('reads the search against the scope, and says when nothing matches', () => {
      expect(userModelsStatus({ scope: 12, matching: 3, shown: 3, search: 'cl', requests: 9 })).toBe('Showing 3 of 12 entries');
      expect(userModelsStatus({ scope: 312, matching: 80, shown: 50, search: 'a', requests: 9 })).toBe('Showing 50 of 80 matching entries');
      expect(userModelsStatus({ scope: 12, matching: 0, shown: 0, search: ' opus ', requests: 9 })).toBe('No entries match "opus".');
    });

    it('is empty with no entry in the scope', () => {
      expect(userModelsStatus({ scope: 0, matching: 0, shown: 0, search: '', requests: 0 })).toBe('');
    });
  });

  describe('view storage', () => {
    it('reads the default when nothing is stored', () => {
      expect(readUserModelsView(memoryStorage())).toEqual({ separateThinkingLevels: false, sort: { field: 'cost', direction: 'desc' } });
    });

    it('round-trips a view under its own key', () => {
      expect(TELEMETRY_USER_MODELS_STORAGE_KEY).toBe('overseer.admin.telemetry.userModels');
      const storage = memoryStorage();
      const view = { separateThinkingLevels: true, sort: { field: 'provider' as const, direction: 'asc' as const } };
      writeUserModelsView(view, storage);
      expect(JSON.parse(storage.getItem(TELEMETRY_USER_MODELS_STORAGE_KEY)!)).toEqual({ version: 1, ...view });
      expect(readUserModelsView(storage)).toEqual(view);
    });

    it('ignores another version and unreadable records', () => {
      const read = (raw: string) => readUserModelsView(memoryStorage({ [TELEMETRY_USER_MODELS_STORAGE_KEY]: raw }));
      expect(read(JSON.stringify({ version: 2, separateThinkingLevels: true }))).toEqual(defaultUserModelsView());
      expect(read('{not json')).toEqual(defaultUserModelsView());
      expect(read('null')).toEqual(defaultUserModelsView());
    });

    it('falls back field by field', () => {
      const read = (fields: Record<string, unknown>) => readUserModelsView(memoryStorage({
        [TELEMETRY_USER_MODELS_STORAGE_KEY]: JSON.stringify({ version: 1, ...fields })
      }));
      expect(read({ separateThinkingLevels: 'yes', sort: { field: 'requests', direction: 'asc' } }))
        .toEqual({ separateThinkingLevels: false, sort: { field: 'requests', direction: 'asc' } });
      expect(read({ separateThinkingLevels: true, sort: { field: 'providerErrors', direction: 'asc' } }))
        .toEqual({ separateThinkingLevels: true, sort: DEFAULT_USER_MODEL_SORT });
    });

    it('falls back when storage throws', () => {
      const throwing = memoryStorage();
      throwing.getItem = () => { throw new Error('denied'); };
      throwing.setItem = () => { throw new Error('denied'); };
      expect(readUserModelsView(throwing)).toEqual(defaultUserModelsView());
      expect(() => writeUserModelsView(defaultUserModelsView(), throwing)).not.toThrow();
    });
  });
});
