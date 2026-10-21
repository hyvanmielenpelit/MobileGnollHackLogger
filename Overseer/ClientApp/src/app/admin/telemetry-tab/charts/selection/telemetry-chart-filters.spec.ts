import type {
  AiTelemetrySeriesGroupOptionDto,
  AiTelemetrySeriesModelOptionDto,
  AiTelemetrySeriesUserOptionDto
} from '../../../../services/admin.service';
import {
  CHART_GROUP_NOUN,
  CHART_MODEL_NOUN,
  CHART_USER_NOUN,
  ChartModelRow,
  TL_CHARTS_GROUPS_VIEW_STORAGE_KEY,
  TL_CHARTS_MAX_GROUPS,
  TL_CHARTS_MAX_MODELS,
  TL_CHARTS_MAX_USERS,
  TL_CHARTS_MODELS_VIEW_STORAGE_KEY,
  TL_CHARTS_USERS_VIEW_STORAGE_KEY,
  absentGroupRow,
  absentKeys,
  absentModelRow,
  absentUserRow,
  canChoose,
  capWarningText,
  chartDateText,
  chartKeySourceFacets,
  chartProviderFacets,
  chooseShown,
  coversEveryRow,
  effectiveChartProviders,
  effectiveFilter,
  filterChartRows,
  groupKeyString,
  groupRows,
  modelKey,
  modelRows,
  parseGroupKey,
  parseModelKey,
  readChartGroupsView,
  readChartModelsView,
  readChartUsersView,
  selectionBandText,
  selectionStatusText,
  sortChartRows,
  usageParts,
  userRows,
  writeChartGroupsView,
  writeChartModelsView,
  writeChartUsersView
} from './telemetry-chart-filters';

function modelOption(overrides: Partial<AiTelemetrySeriesModelOptionDto> = {}): AiTelemetrySeriesModelOptionDto {
  return {
    provider: 'Anthropic', modelId: 'claude-x', modelName: 'Claude X', keySources: ['system'],
    requests: 10, estimatedCost: 0.5, pricedRequests: 10, tokens: 1000, lastRequestUtc: '2026-10-12T21:15:00Z',
    ...overrides
  };
}

function userOption(overrides: Partial<AiTelemetrySeriesUserOptionDto> = {}): AiTelemetrySeriesUserOptionDto {
  return {
    userId: 'u-1', userName: 'alice', requests: 10, systemRequests: 10, userKeyRequests: 0,
    estimatedCost: 0.25, pricedRequests: 10, lastRequestUtc: '2026-10-12T21:15:00Z',
    ...overrides
  };
}

function groupOption(overrides: Partial<AiTelemetrySeriesGroupOptionDto> = {}): AiTelemetrySeriesGroupOptionDto {
  return {
    groupId: 3, groupName: 'Testers', memberCount: 12, requests: 40, estimatedCost: 1.25, pricedRequests: 40,
    lastRequestUtc: '2026-10-12T21:15:00Z',
    ...overrides
  };
}

/** An in-memory Storage. */
function memoryStorage(initial: Record<string, string> = {}): Storage {
  const data = new Map(Object.entries(initial));
  return {
    get length() { return data.size; },
    clear: () => data.clear(),
    getItem: key => data.get(key) ?? null,
    key: index => [...data.keys()][index] ?? null,
    removeItem: key => { data.delete(key); },
    setItem: (key, value) => { data.set(key, String(value)); }
  };
}

function throwingStorage(): Storage {
  const fail = () => { throw new Error('denied'); };
  return { length: 0, clear: fail, getItem: fail, key: fail, removeItem: fail, setItem: fail };
}

const keysOf = (rows: readonly { key: string }[]) => rows.map(r => r.key);

describe('telemetry-chart-filters', () => {
  describe('rows and keys', () => {
    it('keys a model as provider|modelId and names it by its catalog name, else its model id', () => {
      const rows = modelRows({ models: [modelOption(), modelOption({ provider: 'OpenAI', modelId: 'gpt-x', modelName: null })] });
      expect(keysOf(rows)).toEqual(['Anthropic|claude-x', 'OpenAI|gpt-x']);
      expect(rows.map(r => r.name)).toEqual(['Claude X', 'gpt-x']);
      expect(rows[0].keySources).toEqual(['system']);
      expect(rows[0].lastRequestUtc).toBe('2026-10-12T21:15:00Z');
    });

    it('keys a user by id and names a gone account Unknown user', () => {
      const rows = userRows({ users: [userOption(), userOption({ userId: 'u-2', userName: null })] });
      expect(keysOf(rows)).toEqual(['u-1', 'u-2']);
      expect(rows.map(r => r.name)).toEqual(['alice', 'Unknown user']);
    });

    it('keys a group by id as a string, and No group as none', () => {
      const rows = groupRows({ groups: [groupOption(), groupOption({ groupId: null, groupName: 'No group' })] });
      expect(keysOf(rows)).toEqual(['3', 'none']);
      expect(rows.map(r => r.name)).toEqual(['Testers', 'No group']);
      expect(rows[1].groupId).toBeNull();
    });

    it('reads a missing options record as no rows', () => {
      expect(modelRows(null)).toEqual([]);
      expect(userRows(undefined)).toEqual([]);
      expect(groupRows(null)).toEqual([]);
    });

    it('round-trips a model key, splitting at the first |', () => {
      expect(modelKey({ provider: 'Custom', modelId: 'a|b' })).toBe('Custom|a|b');
      expect(parseModelKey('Custom|a|b')).toEqual({ provider: 'Custom', modelId: 'a|b' });
      expect(parseModelKey('no-bar')).toBeNull();
      expect(parseModelKey('|x')).toBeNull();
      expect(parseModelKey('x|')).toBeNull();
    });

    it('round-trips a group key', () => {
      expect(groupKeyString(7)).toBe('7');
      expect(groupKeyString(null)).toBe('none');
      expect(groupKeyString(undefined)).toBe('none');
      expect(parseGroupKey('7')).toBe(7);
      expect(parseGroupKey('none')).toBe('none');
      expect(parseGroupKey('x')).toBe('none');
    });

    it('builds absent rows named as last seen, else from the key', () => {
      expect(absentModelRow('OpenAI|gpt-x')).toEqual(expect.objectContaining({ name: 'gpt-x', provider: 'OpenAI', absent: true, requests: 0 }));
      expect(absentModelRow('OpenAI|gpt-x', 'GPT X').name).toBe('GPT X');
      expect(absentUserRow('u-9').name).toBe('u-9');
      expect(absentUserRow('u-9', 'zed').name).toBe('zed');
      expect(absentGroupRow('none').name).toBe('No group');
      expect(absentGroupRow('4').name).toBe('Group 4');
      expect(absentGroupRow('4', 'Admins')).toEqual(expect.objectContaining({ name: 'Admins', groupId: 4, absent: true }));
      expect(absentKeys(['a', 'b', 'c'], [{ key: 'b' }])).toEqual(['a', 'c']);
    });
  });

  describe('search and toggles', () => {
    const rows = modelRows({
      models: [
        modelOption(),
        modelOption({ provider: 'OpenAI', modelId: 'gpt-x', modelName: 'GPT X', keySources: ['user'] }),
        modelOption({ provider: 'Google', modelId: 'gemini-x', modelName: 'Gemini X', keySources: ['system', 'user'] })
      ]
    });

    it('matches every term against a model\'s name, model id and provider', () => {
      expect(keysOf(filterChartRows(rows, { search: 'claude' }))).toEqual(['Anthropic|claude-x']);
      expect(keysOf(filterChartRows(rows, { search: 'openai' }))).toEqual(['OpenAI|gpt-x']);
      expect(keysOf(filterChartRows(rows, { search: 'GEMINI-X google' }))).toEqual(['Google|gemini-x']);
      expect(keysOf(filterChartRows(rows, { search: 'x' })).length).toBe(3);
      expect(filterChartRows(rows, { search: 'claude openai' })).toEqual([]);
    });

    it('matches a user\'s name and a group\'s name only', () => {
      const users = userRows({ users: [userOption(), userOption({ userId: 'alice-id', userName: 'bob' })] });
      expect(keysOf(filterChartRows(users, { search: 'ali' }))).toEqual(['u-1']);
      const groups = groupRows({ groups: [groupOption(), groupOption({ groupId: 4, groupName: 'Admins' })] });
      expect(keysOf(filterChartRows(groups, { search: 'adm' }))).toEqual(['4']);
      expect(filterChartRows(groups, { search: '4' })).toEqual([]);
    });

    it('ORs the pressed providers and key sources; none pressed means all', () => {
      expect(keysOf(filterChartRows(rows, { search: '', providers: [] }))).toHaveLength(3);
      expect(keysOf(filterChartRows(rows, { search: '', providers: ['openai', 'google'] }))).toEqual(['OpenAI|gpt-x', 'Google|gemini-x']);
      expect(keysOf(filterChartRows(rows, { search: '', keySources: ['user'] }))).toEqual(['OpenAI|gpt-x', 'Google|gemini-x']);
      expect(keysOf(filterChartRows(rows, { search: '', keySources: ['system'] }))).toEqual(['Anthropic|claude-x', 'Google|gemini-x']);
    });

    it('keeps the chosen rows only with selectedOnly, and an absent row by the search alone', () => {
      expect(keysOf(filterChartRows(rows, { search: '', selectedOnly: true, chosen: ['OpenAI|gpt-x'] }))).toEqual(['OpenAI|gpt-x']);
      const absent = [absentModelRow('Mistral|m-1')];
      expect(filterChartRows(absent, { search: '', providers: ['openai'], keySources: ['user'], selectedOnly: true, chosen: [] })).toHaveLength(1);
      expect(filterChartRows(absent, { search: 'zzz' })).toHaveLength(0);
    });

    it('counts providers and key sources, and drops stored providers no row has', () => {
      const facets = chartProviderFacets(rows);
      expect(facets.map(f => [f.provider, f.count])).toEqual([['Anthropic', 1], ['Google', 1], ['OpenAI', 1]]);
      expect(effectiveChartProviders(['openai', 'mistral'], facets)).toEqual(['openai']);
      expect(chartKeySourceFacets(rows)).toEqual([
        { key: 'system', label: 'System', count: 2 },
        { key: 'user', label: 'Own keys', count: 2 }
      ]);
      expect(chartKeySourceFacets([rows[0]])).toEqual([{ key: 'system', label: 'System', count: 1 }]);
    });
  });

  describe('sort', () => {
    function row(name: string, overrides: Partial<ChartModelRow> = {}): ChartModelRow {
      return { ...modelRows({ models: [modelOption({ modelId: name.toLowerCase(), modelName: name })] })[0], ...overrides };
    }

    it('sorts a figure in both orders, ties by name then key', () => {
      const rows = [row('B', { requests: 5 }), row('A', { requests: 5 }), row('C', { requests: 9 })];
      expect(sortChartRows(rows, { field: 'requests', direction: 'desc' }).map(r => r.name)).toEqual(['C', 'A', 'B']);
      expect(sortChartRows(rows, { field: 'requests', direction: 'asc' }).map(r => r.name)).toEqual(['A', 'B', 'C']);
    });

    it('sorts names numerically, A–Z and Z–A', () => {
      const rows = [row('Model 10'), row('Model 9'), row('model 1')];
      expect(sortChartRows(rows, { field: 'name', direction: 'asc' }).map(r => r.name)).toEqual(['model 1', 'Model 9', 'Model 10']);
      expect(sortChartRows(rows, { field: 'name', direction: 'desc' }).map(r => r.name)).toEqual(['Model 10', 'Model 9', 'model 1']);
    });

    it('sorts an unpriced cost last in both orders', () => {
      const rows = [row('A', { estimatedCost: 0, pricedRequests: 0 }), row('B', { estimatedCost: 1 }), row('C', { estimatedCost: 2 })];
      expect(sortChartRows(rows, { field: 'cost', direction: 'desc' }).map(r => r.name)).toEqual(['C', 'B', 'A']);
      expect(sortChartRows(rows, { field: 'cost', direction: 'asc' }).map(r => r.name)).toEqual(['B', 'C', 'A']);
    });

    it('sorts an unknown date last in both orders', () => {
      const rows = [row('A', { lastRequestUtc: null }), row('B', { lastRequestUtc: '2026-10-01T00:00:00Z' }), row('C', { lastRequestUtc: '2026-10-10T00:00:00Z' })];
      expect(sortChartRows(rows, { field: 'lastUsed', direction: 'desc' }).map(r => r.name)).toEqual(['C', 'B', 'A']);
      expect(sortChartRows(rows, { field: 'lastUsed', direction: 'asc' }).map(r => r.name)).toEqual(['B', 'C', 'A']);
    });

    it('sorts by provider, then name', () => {
      const rows = [row('B', { provider: 'OpenAI' }), row('A', { provider: 'OpenAI' }), row('C', { provider: 'Anthropic' })];
      expect(sortChartRows(rows, { field: 'provider', direction: 'asc' }).map(r => r.name)).toEqual(['C', 'A', 'B']);
    });

    it('sorts groups by members and users by last active', () => {
      const groups = groupRows({ groups: [groupOption({ groupId: 1, groupName: 'Few', memberCount: 2 }), groupOption({ groupId: 2, groupName: 'Many', memberCount: 9 })] });
      expect(sortChartRows(groups, { field: 'members', direction: 'desc' }).map(r => r.name)).toEqual(['Many', 'Few']);
      const users = userRows({ users: [userOption({ userId: 'a', userName: 'old', lastRequestUtc: '2026-01-01T00:00:00Z' }), userOption({ userId: 'b', userName: 'new' })] });
      expect(sortChartRows(users, { field: 'lastActive', direction: 'desc' }).map(r => r.name)).toEqual(['new', 'old']);
    });

    it('leaves its input unsorted', () => {
      const rows = [row('B', { requests: 1 }), row('A', { requests: 2 })];
      sortChartRows(rows, { field: 'requests', direction: 'desc' });
      expect(rows.map(r => r.name)).toEqual(['B', 'A']);
    });
  });

  describe('the effective filter and the caps', () => {
    const rows = [{ key: 'a' }, { key: 'b' }, { key: 'c' }];

    it('is no filter for no choice, and for a choice of every row of an untruncated list', () => {
      expect(effectiveFilter([], rows)).toEqual([]);
      expect(effectiveFilter(['c', 'a', 'b'], rows)).toEqual([]);
      expect(effectiveFilter(['c', 'a', 'b', 'gone'], rows)).toEqual([]);
      expect(effectiveFilter(['b', 'a'], rows)).toEqual(['b', 'a']);
    });

    it('keeps the full choice of a truncated list as a filter', () => {
      expect(effectiveFilter(['a', 'b', 'c'], rows, true)).toEqual(['a', 'b', 'c']);
      expect(coversEveryRow(['a', 'b', 'c'], rows)).toBe(true);
      expect(coversEveryRow(['a'], [])).toBe(false);
    });

    it('caps a partial choice only', () => {
      expect([TL_CHARTS_MAX_MODELS, TL_CHARTS_MAX_USERS, TL_CHARTS_MAX_GROUPS]).toEqual([50, 50, 50]);
      expect(canChoose('c', ['a', 'b'], rows, false, 1)).toBe(true);
      expect(canChoose('b', ['a'], [...rows, { key: 'd' }], false, 1)).toBe(false);
      expect(canChoose('b', ['a'], rows, true, 1)).toBe(false);
      expect(canChoose('a', ['a'], rows, false, 0)).toBe(true);
    });

    it('adds the shown rows up to the cap, or all of them when that covers every row', () => {
      const many = Array.from({ length: 6 }, (_, i) => ({ key: `k${i}` }));
      expect(chooseShown(['k0'], many.slice(0, 4), many, false, 3)).toEqual(['k0', 'k1', 'k2']);
      expect(chooseShown(['k0', 'k1', 'k2'], many.slice(3), many, false, 3)).toEqual(['k0', 'k1', 'k2', 'k3', 'k4', 'k5']);
      expect(chooseShown(['k0', 'k1', 'k2'], many.slice(3), many, true, 3)).toEqual(['k0', 'k1', 'k2']);
      expect(chooseShown(['k1'], many.slice(0, 2), many, false, 50)).toEqual(['k1', 'k0']);
    });
  });

  describe('texts', () => {
    it('never leaves the band label empty', () => {
      expect(selectionBandText(CHART_MODEL_NOUN, 0, 14, false)).toBe('All models — no filter');
      expect(selectionBandText(CHART_MODEL_NOUN, 14, 14, true)).toBe('All 14 models chosen — the same as no filter');
      expect(selectionBandText(CHART_MODEL_NOUN, 1, 1, true)).toBe('The only model chosen — the same as no filter');
      expect(selectionBandText(CHART_MODEL_NOUN, 3, 14, false)).toBe('3 of 14 models');
      expect(selectionBandText(CHART_USER_NOUN, 3, 1284, false)).toBe('3 of 1,284 users');
      expect(selectionBandText(CHART_GROUP_NOUN, 0, 2, false)).toBe('All groups — no filter');
    });

    it('reads the status line, with the cap of a truncated list', () => {
      expect(selectionStatusText(CHART_MODEL_NOUN, 12, 27, 3)).toBe('Showing 12 of 27 models · 3 chosen');
      expect(selectionStatusText(CHART_MODEL_NOUN, 1, 1, 0)).toBe('Showing 1 of 1 model');
      expect(selectionStatusText(CHART_USER_NOUN, 50, 1000, 0, { truncated: true, total: 1284 }))
        .toBe('Showing 50 of 1,000 users · the 1,000 users with the most requests of 1,284');
    });

    it('reads the cap warning, with Select all only where it exists', () => {
      expect(capWarningText(CHART_MODEL_NOUN, 50, true)).toBe('At most 50 models can be chosen — or choose them all with Select all.');
      expect(capWarningText(CHART_USER_NOUN, 50, false)).toBe('At most 50 users can be chosen.');
    });

    it('reads usage parts: requests, cost or unpriced, and the last date', () => {
      expect(usageParts({ requests: 12, estimatedCost: 0.25, pricedRequests: 12, lastRequestUtc: '2026-10-13T08:00:00Z' }, 'last used'))
        .toEqual(['12 requests', 'est. $0.2500', 'last used 2026-10-13']);
      expect(usageParts({ requests: 4, estimatedCost: 1.5, pricedRequests: 1, lastRequestUtc: null }, 'last active'))
        .toEqual(['4 requests', 'est. $1.50', '3 unpriced']);
      expect(usageParts({ requests: 1, estimatedCost: 0, pricedRequests: 0, lastRequestUtc: null }, 'last active'))
        .toEqual(['1 request', 'cost unknown']);
      expect(usageParts({ requests: 0, estimatedCost: 0, pricedRequests: 0, lastRequestUtc: null }, 'last used')).toEqual(['0 requests']);
      expect(usageParts({ requests: 0, estimatedCost: 0, pricedRequests: 0, lastRequestUtc: null, absent: true }, 'last used'))
        .toEqual(['No usage in this scope']);
    });

    it('formats a date as its UTC day', () => {
      expect(chartDateText('2026-10-13T23:30:00Z')).toBe('2026-10-13');
      expect(chartDateText(null)).toBe('');
      expect(chartDateText('not a date')).toBe('not a date');
    });
  });

  describe('view storage', () => {
    it('round-trips the Models view as version 1 with its sort and providers', () => {
      const storage = memoryStorage();
      writeChartModelsView({ sort: { field: 'cost', direction: 'asc' }, providers: ['openai'] }, storage);
      expect(JSON.parse(storage.getItem(TL_CHARTS_MODELS_VIEW_STORAGE_KEY)!)).toEqual({
        version: 1, sort: { field: 'cost', direction: 'asc' }, providers: ['openai']
      });
      expect(readChartModelsView(storage)).toEqual({ sort: { field: 'cost', direction: 'asc' }, providers: ['openai'] });
    });

    it('round-trips the Users and Groups views as version 1 with their sort', () => {
      const storage = memoryStorage();
      writeChartUsersView({ sort: { field: 'name', direction: 'asc' } }, storage);
      writeChartGroupsView({ sort: { field: 'members', direction: 'desc' } }, storage);
      expect(JSON.parse(storage.getItem(TL_CHARTS_USERS_VIEW_STORAGE_KEY)!)).toEqual({ version: 1, sort: { field: 'name', direction: 'asc' } });
      expect(JSON.parse(storage.getItem(TL_CHARTS_GROUPS_VIEW_STORAGE_KEY)!)).toEqual({ version: 1, sort: { field: 'members', direction: 'desc' } });
      expect(readChartUsersView(storage).sort).toEqual({ field: 'name', direction: 'asc' });
      expect(readChartGroupsView(storage).sort).toEqual({ field: 'members', direction: 'desc' });
    });

    it('reads the defaults when nothing is stored', () => {
      const storage = memoryStorage();
      expect(readChartModelsView(storage)).toEqual({ sort: { field: 'requests', direction: 'desc' }, providers: [] });
      expect(readChartUsersView(storage).sort).toEqual({ field: 'cost', direction: 'desc' });
      expect(readChartGroupsView(storage).sort).toEqual({ field: 'requests', direction: 'desc' });
    });

    it('reads an unreadable record, another version or an unknown field as the default', () => {
      expect(readChartModelsView(memoryStorage({ [TL_CHARTS_MODELS_VIEW_STORAGE_KEY]: '{not json' }))).toEqual(
        { sort: { field: 'requests', direction: 'desc' }, providers: [] });
      expect(readChartUsersView(memoryStorage({ [TL_CHARTS_USERS_VIEW_STORAGE_KEY]: JSON.stringify({ version: 2, sort: { field: 'name', direction: 'asc' } }) })).sort)
        .toEqual({ field: 'cost', direction: 'desc' });
      expect(readChartGroupsView(memoryStorage({ [TL_CHARTS_GROUPS_VIEW_STORAGE_KEY]: JSON.stringify({ version: 1, sort: { field: 'tokens', direction: 'asc' } }) })).sort)
        .toEqual({ field: 'requests', direction: 'desc' });
      expect(readChartModelsView(memoryStorage({ [TL_CHARTS_MODELS_VIEW_STORAGE_KEY]: JSON.stringify({ version: 1, sort: { field: 'name', direction: 'up' }, providers: ['OpenAI', 4, '', 'openai'] }) })))
        .toEqual({ sort: { field: 'requests', direction: 'desc' }, providers: ['openai'] });
      expect(readChartModelsView(memoryStorage({ [TL_CHARTS_MODELS_VIEW_STORAGE_KEY]: 'null' }))).toEqual(
        { sort: { field: 'requests', direction: 'desc' }, providers: [] });
    });

    it('survives storage that throws', () => {
      const storage = throwingStorage();
      expect(() => writeChartModelsView({ sort: { field: 'cost', direction: 'asc' }, providers: [] }, storage)).not.toThrow();
      expect(readChartModelsView(storage).sort).toEqual({ field: 'requests', direction: 'desc' });
      expect(readChartUsersView(null).sort).toEqual({ field: 'cost', direction: 'desc' });
    });
  });
});
