import {
  DEFAULT_TELEMETRY_USERS_SORT,
  TELEMETRY_USERS_SORT_FIELDS,
  TELEMETRY_USERS_VIEW_STORAGE_KEY,
  defaultUsersView,
  isDefaultUsersSort,
  readUsersView,
  usersDirectionLabel,
  usersSortField,
  usersSortLabel,
  writeUsersView
} from './telemetry-users-view';

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

describe('telemetry users view', () => {
  describe('sort labels', () => {
    it('lists the fields in the popover order with their kinds', () => {
      expect(TELEMETRY_USERS_SORT_FIELDS.map(f => [f.field, f.label, f.kind])).toEqual([
        ['cost', 'Est. cost', 'number'],
        ['requests', 'Requests', 'number'],
        ['tokens', 'Tokens', 'number'],
        ['peakHour', 'Busiest hour', 'number'],
        ['refusals', 'Refusals', 'number'],
        ['lastActive', 'Last active', 'date'],
        ['name', 'User name', 'text']
      ]);
    });

    it('names the directions by the kind of the field', () => {
      expect(usersDirectionLabel('number', 'desc')).toBe('Highest first');
      expect(usersDirectionLabel('number', 'asc')).toBe('Lowest first');
      expect(usersDirectionLabel('date', 'desc')).toBe('Newest first');
      expect(usersDirectionLabel('date', 'asc')).toBe('Oldest first');
      expect(usersDirectionLabel('text', 'asc')).toBe('A–Z');
      expect(usersDirectionLabel('text', 'desc')).toBe('Z–A');
    });

    it('labels a sort with its field and direction', () => {
      expect(usersSortLabel(DEFAULT_TELEMETRY_USERS_SORT)).toBe('Est. cost, highest first');
      expect(usersSortLabel({ field: 'lastActive', direction: 'asc' })).toBe('Last active, oldest first');
      expect(usersSortLabel({ field: 'name', direction: 'asc' })).toBe('User name, A–Z');
      expect(usersSortLabel({ field: 'peakHour', direction: 'asc' })).toBe('Busiest hour, lowest first');
    });

    it('defaults to the highest cost first', () => {
      expect(DEFAULT_TELEMETRY_USERS_SORT).toEqual({ field: 'cost', direction: 'desc' });
      expect(isDefaultUsersSort({ field: 'cost', direction: 'desc' })).toBe(true);
      expect(isDefaultUsersSort({ field: 'cost', direction: 'asc' })).toBe(false);
      expect(isDefaultUsersSort({ field: 'requests', direction: 'desc' })).toBe(false);
      expect(usersSortField('tokens').label).toBe('Tokens');
    });
  });

  describe('view storage', () => {
    it('falls back to the highest cost first', () => {
      expect(readUsersView(memoryStorage())).toEqual({ sort: { field: 'cost', direction: 'desc' } });
      expect(readUsersView(null)).toEqual(defaultUsersView());
    });

    it('round-trips the sort under its key with version 2', () => {
      const storage = memoryStorage();
      writeUsersView({ sort: { field: 'name', direction: 'asc' } }, storage);

      expect(TELEMETRY_USERS_VIEW_STORAGE_KEY).toBe('overseer.admin.telemetry.users.view');
      expect(JSON.parse(storage.getItem(TELEMETRY_USERS_VIEW_STORAGE_KEY)!)).toEqual({
        version: 2, sort: { field: 'name', direction: 'asc' }
      });
      expect(readUsersView(storage)).toEqual({ sort: { field: 'name', direction: 'asc' } });
    });

    it('reads a version 1 Sort by record as its field, descending', () => {
      const read = (sort: unknown) => readUsersView(memoryStorage({
        [TELEMETRY_USERS_VIEW_STORAGE_KEY]: JSON.stringify({ version: 1, sort })
      }));

      expect(read('lastActive')).toEqual({ sort: { field: 'lastActive', direction: 'desc' } });
      expect(read('refusals')).toEqual({ sort: { field: 'refusals', direction: 'desc' } });
      expect(read('mostEverything')).toEqual(defaultUsersView());
      expect(read({ field: 'requests', direction: 'asc' })).toEqual(defaultUsersView());
    });

    it('ignores garbage, another version and unknown values', () => {
      const read = (raw: string) => readUsersView(memoryStorage({ [TELEMETRY_USERS_VIEW_STORAGE_KEY]: raw }));

      expect(read('not json')).toEqual(defaultUsersView());
      expect(read('null')).toEqual(defaultUsersView());
      expect(read('"cost"')).toEqual(defaultUsersView());
      expect(read(JSON.stringify({ version: 3, sort: { field: 'name', direction: 'asc' } }))).toEqual(defaultUsersView());
      expect(read(JSON.stringify({ version: 2, sort: 'name' }))).toEqual(defaultUsersView());
      expect(read(JSON.stringify({ version: 2, sort: { field: 'name', direction: 'up' } }))).toEqual(defaultUsersView());
      expect(read(JSON.stringify({ version: 2, sort: { field: 'height', direction: 'asc' } }))).toEqual(defaultUsersView());
    });

    it('falls back when storage throws', () => {
      const throwing = memoryStorage();
      throwing.getItem = () => { throw new Error('denied'); };
      throwing.setItem = () => { throw new Error('denied'); };

      expect(readUsersView(throwing)).toEqual(defaultUsersView());
      expect(() => writeUsersView({ sort: { field: 'name', direction: 'asc' } }, throwing)).not.toThrow();
    });
  });
});
