import type { Mock } from "vitest";
import { CardListSort, CardListState, CardListStateOptions } from './card-list-state';
import { anyOfFilter, customFilter, exactFilter, TableState } from './table-state';

interface Row {
  id: string;
  kind: string;
  tags: readonly string[];
  writer: string | null;
  age: 'new' | 'old' | null;
}

function row(id: string, kind: string, tags: readonly string[] = [], writer: string | null = null,
             age: 'new' | 'old' | null = null): Row {
  return { id, kind, tags, writer, age };
}

const STORAGE_KEY = 'overseer.test.cardListState.view';

const SORTS: readonly CardListSort[] = [
  { id: 'id-asc', label: 'Id (A–Z)', column: 'id', direction: 'asc' },
  { id: 'id-desc', label: 'Id (Z–A)', column: 'id', direction: 'desc' },
  { id: 'kind', label: 'Kind', column: 'kind', direction: 'asc' }
];

/** `none` last, the rest alphabetically. */
const writerOrder = (a: string, b: string): number =>
  a === 'none' ? 1 : b === 'none' ? -1 : a.localeCompare(b, undefined, { sensitivity: 'base' });

/** Four rows with three kinds, three tags, two writers and a row with none, and two ages. */
const ROWS: readonly Row[] = [
  row('a1', 'report', ['red'], 'Ann', 'new'),
  row('a2', 'report', ['green', 'red'], 'bob', 'old'),
  row('a10', 'log', ['blue'], null, 'new'),
  row('a3', 'diagnostics', [], 'Ann', null)
];

function manyRows(count: number): Row[] {
  return Array.from({ length: count }, (_, i) => row(`r${String(i).padStart(2, '0')}`, 'report'));
}

/** A table with every accessor the specs use, registered as a host component would. */
function tableFor(): TableState<Row> {
  return new TableState<Row>('id', 'asc').registerAccessors(
    { id: r => r.id, kind: r => r.kind },
    {
      search: customFilter((r, value) => r.id.toLowerCase().includes(value.toLowerCase())),
      kind: anyOfFilter(r => r.kind),
      tags: anyOfFilter(r => r.tags),
      writer: anyOfFilter(r => r.writer ?? 'none'),
      age: customFilter((r, value) => r.age === value),
      selected: exactFilter(r => (r.id === 'a1' ? 'yes' : 'no'))
    }
  );
}

function listFor(overrides: Partial<CardListStateOptions<Row>> = {}, table = tableFor()): CardListState<Row> {
  return new CardListState<Row>(table, {
    idPrefix: 't',
    sorts: SORTS,
    defaultSort: 'id-desc',
    storageKey: STORAGE_KEY,
    facets: [
      { column: 'kind', label: 'Kind', values: r => r.kind },
      { column: 'tags', label: 'Tags', values: r => r.tags, order: ['red', 'green', 'blue'] },
      {
        column: 'writer', label: 'Written by', values: r => r.writer ?? 'none', order: writerOrder,
        labelOf: value => (value === 'none' ? 'No writer' : value)
      }
    ],
    singleFacets: [{
      column: 'age',
      label: 'Age',
      anyLabel: 'Any age',
      options: [{ value: 'new', label: 'New' }, { value: 'old', label: 'Old' }],
      matches: (r, value) => r.age === value
    }],
    ...overrides
  });
}

/** A keydown event whose target is an input holding `value`. */
function keydown(key: string, value: string): {
  event: KeyboardEvent;
  input: {
    value: string;
  };
  preventDefault: Mock;
  stopPropagation: Mock;
} {
  const input = { value };
  const preventDefault = vi.fn().mockName('preventDefault');
  const stopPropagation = vi.fn().mockName('stopPropagation');
  const event = { key, target: input, preventDefault, stopPropagation } as unknown as KeyboardEvent;
  return { event, input, preventDefault, stopPropagation };
}

function storeSort(value: unknown): void {
  localStorage.setItem(STORAGE_KEY, typeof value === 'string' ? value : JSON.stringify(value));
}

describe('CardListState', () => {
  beforeEach(() => localStorage.removeItem(STORAGE_KEY));
  afterEach(() => localStorage.removeItem(STORAGE_KEY));

  describe('construction and the stored sort', () => {
    it('starts with one batch, no search and the default sort when nothing is stored', () => {
      const table = tableFor();
      const list = listFor({}, table);

      expect(list.visibleCount).toBe(10);
      expect(list.searchText).toBe('');
      expect(list.sortId).toBe('id-desc');
      expect(table.sortColumn).toBe('id');
      expect(table.sortDirection).toBe('desc');
    });

    it('takes the batch size from the options', () => {
      expect(listFor({ batch: 3 }).visibleCount).toBe(3);
    });

    it('applies a stored sort', () => {
      storeSort({ version: 1, sort: 'kind' });
      const table = tableFor();
      const list = listFor({}, table);

      expect(list.sortId).toBe('kind');
      expect(table.sortColumn).toBe('kind');
      expect(table.sortDirection).toBe('asc');
    });

    it('falls back to the default for an unknown sort, a wrong version or an unreadable value', () => {
      for (const stored of [{ version: 1, sort: 'nonsense' }, { version: 2, sort: 'kind' }, '{not json', 'null']) {
        storeSort(stored);
        expect(listFor().sortId, JSON.stringify(stored)).toBe('id-desc');
      }
    });

    it('falls back to the default when localStorage throws', () => {
      storeSort({ version: 1, sort: 'kind' });
      vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
        throw new Error('denied');
      });

      expect(listFor().sortId).toBe('id-desc');
    });
  });

  describe('setSort', () => {
    it('applies, remembers and resets the batch', () => {
      const table = tableFor();
      const list = listFor({}, table);
      list.showMore(manyRows(25));

      expect(list.setSort('kind')).toBe(true);
      expect(list.sortId).toBe('kind');
      expect(table.sortColumn).toBe('kind');
      expect(table.sortDirection).toBe('asc');
      expect(list.visibleCount).toBe(10);
      expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? 'null')).toEqual({ version: 1, sort: 'kind' });
    });

    it('ignores an unknown id', () => {
      const table = tableFor();
      const list = listFor({}, table);
      list.showMore(manyRows(25));

      expect(list.setSort('nonsense')).toBe(false);
      expect(list.sortId).toBe('id-desc');
      expect(table.sortColumn).toBe('id');
      expect(list.visibleCount).toBe(20);
      expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
    });

    it('still applies the sort when localStorage throws', () => {
      vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
        throw new Error('full');
      });
      const list = listFor();

      expect(list.setSort('id-asc')).toBe(true);
      expect(list.sortId).toBe('id-asc');
    });
  });

  describe('batching', () => {
    const rows = manyRows(25);

    it('shows the first batch, counts the rest and the next batch', () => {
      const list = listFor({ defaultSort: 'id-asc' });

      expect(list.matching(rows).length).toBe(25);
      expect(list.view(rows).map(r => r.id)).toEqual(rows.slice(0, 10).map(r => r.id));
      expect(list.remainingCount(rows)).toBe(15);
      expect(list.nextBatchCount(rows)).toBe(10);
    });

    it('Show more returns the index of the first new card and adds one batch', () => {
      const list = listFor();

      expect(list.showMore(rows)).toBe(10);
      expect(list.view(rows).length).toBe(20);
      expect(list.nextBatchCount(rows)).toBe(5);
      expect(list.showMore(rows)).toBe(20);
      expect(list.view(rows).length).toBe(25);
      expect(list.remainingCount(rows)).toBe(0);
      expect(list.nextBatchCount(rows)).toBe(0);
    });

    it('Show all reveals every matching card, and never fewer than one batch', () => {
      const list = listFor();

      expect(list.showAll(rows)).toBe(10);
      expect(list.visibleCount).toBe(25);

      const few = manyRows(3);
      const other = listFor();
      expect(other.showAll(few)).toBe(3);
      expect(other.visibleCount).toBe(10);
    });

    it('keeps the batch over a reload of the rows and an invalidation', () => {
      const list = listFor();
      list.showMore(rows);
      list.invalidate();

      expect(list.view(manyRows(25)).length).toBe(20);
      expect(list.visibleCount).toBe(20);
    });

    it('resets the batch on a facet change, a chip removal and a clear', () => {
      const list = listFor();
      list.showMore(rows);
      list.setFacet('kind', ['report']);
      expect(list.visibleCount).toBe(10);

      list.showMore(rows);
      list.removeChip(list.chips(rows)[0], rows);
      expect(list.visibleCount).toBe(10);

      list.showMore(rows);
      list.clearFilters();
      expect(list.visibleCount).toBe(10);
    });
  });

  describe('search', () => {
    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    it('applies once typing pauses, resets the batch and calls onChange', () => {
      const onChange = vi.fn().mockName('onChange');
      const table = tableFor();
      const list = listFor({ onChange }, table);
      list.showMore(manyRows(25));

      list.setSearchInput('a1');
      expect(list.searchText).toBe('a1');
      vi.advanceTimersByTime(199);
      expect(table.filters['search']).toBeUndefined();
      expect(list.visibleCount).toBe(20);
      expect(onChange).not.toHaveBeenCalled();

      vi.advanceTimersByTime(1);
      expect(table.filters['search']).toBe('a1');
      expect(list.visibleCount).toBe(10);
      expect(list.matching(ROWS).map(r => r.id)).toEqual(['a10', 'a1']);
      expect(onChange).toHaveBeenCalledTimes(1);
    });

    it('restarts the wait on every keystroke', () => {
      const table = tableFor();
      const list = listFor({ debounceMs: 100 }, table);

      list.setSearchInput('a');
      vi.advanceTimersByTime(80);
      list.setSearchInput('a1');
      vi.advanceTimersByTime(80);
      expect(table.filters['search']).toBeUndefined();

      vi.advanceTimersByTime(20);
      expect(table.filters['search']).toBe('a1');
    });

    it('Escape with text clears it at once and cancels the pending search', () => {
      const onChange = vi.fn().mockName('onChange');
      const table = tableFor();
      const list = listFor({ onChange }, table);
      table.setFilter('search', 'a');
      list.setSearchInput('a1');
      const { event, input, preventDefault, stopPropagation } = keydown('Escape', 'a1');

      expect(list.clearSearchOnEscape(event)).toBe(true);
      expect(preventDefault).toHaveBeenCalled();
      expect(stopPropagation).toHaveBeenCalled();
      expect(input.value).toBe('');
      expect(list.searchText).toBe('');
      expect(table.filters['search']).toBe('');

      vi.advanceTimersByTime(500);
      expect(table.filters['search']).toBe('');
      expect(onChange).not.toHaveBeenCalled();
    });

    it('leaves Escape in an empty field, and any other key, alone', () => {
      const list = listFor();
      for (const [key, value] of [['Escape', ''], ['Enter', 'a1']]) {
        const { event, input, preventDefault, stopPropagation } = keydown(key, value);

        expect(list.clearSearchOnEscape(event), key).toBe(false);
        expect(preventDefault).not.toHaveBeenCalled();
        expect(stopPropagation).not.toHaveBeenCalled();
        expect(input.value).toBe(value);
      }
    });

    it('dispose cancels a pending search', () => {
      const table = tableFor();
      const list = listFor({}, table);

      list.setSearchInput('a1');
      list.dispose();
      vi.advanceTimersByTime(500);

      expect(table.filters['search']).toBeUndefined();
    });
  });

  describe('facets', () => {
    it('lists each facet in default order with ids, labels and counts', () => {
      const facets = listFor().facets(ROWS);

      expect(facets.map(f => f.column)).toEqual(['kind', 'tags', 'writer', 'age']);
      expect(facets.map(f => f.facetId)).toEqual(['t-facet-kind', 't-facet-tags', 't-facet-writer', 't-facet-age']);
      expect(facets[0]).toEqual({
        column: 'kind',
        facetId: 't-facet-kind',
        label: 'Kind',
        mode: 'multiple',
        options: [
          { value: 'diagnostics', label: 'diagnostics', count: 1 },
          { value: 'log', label: 'log', count: 1 },
          { value: 'report', label: 'report', count: 2 }
        ],
        selected: [],
        anyLabel: 'Any'
      });
    });

    it('orders alphabetically with numeric collation, ignoring case', () => {
      const rows = [row('x1', 'v10'), row('x2', 'v9'), row('x3', 'V1')];

      expect(listFor().facets(rows)[0].options.map(o => o.value)).toEqual(['V1', 'v9', 'v10']);
    });

    it('keeps a fixed order, listing only the values present', () => {
      const tags = listFor().facets(ROWS).find(f => f.column === 'tags')!;
      expect(tags.options).toEqual([
        { value: 'red', label: 'red', count: 2 },
        { value: 'green', label: 'green', count: 1 },
        { value: 'blue', label: 'blue', count: 1 }
      ]);

      const rows = [row('x1', 'k', ['blue', 'purple']), row('x2', 'k', ['red'])];
      expect(listFor().facets(rows).find(f => f.column === 'tags')!.options.map(o => o.value)).toEqual(['red', 'blue']);
    });

    it('orders with a comparator and labels values with labelOf', () => {
      const writer = listFor().facets(ROWS).find(f => f.column === 'writer')!;

      expect(writer.options).toEqual([
        { value: 'Ann', label: 'Ann', count: 2 },
        { value: 'bob', label: 'bob', count: 1 },
        { value: 'none', label: 'No writer', count: 1 }
      ]);
    });

    it('lists a facet only while its rows hold two values or more, or it has a selection', () => {
      const rows = [row('x1', 'report', ['red']), row('x2', 'report', ['red'])];
      const list = listFor();

      expect(list.facets(rows).map(f => f.column)).toEqual(['age']);

      list.setFacet('kind', ['report']);
      expect(list.facets(rows).map(f => f.column)).toEqual(['kind', 'age']);
    });

    it('counts each option against the rows every other filter lets through', () => {
      const list = listFor();
      list.setFacet('kind', ['report']);
      const facets = list.facets(ROWS);

      expect(facets.find(f => f.column === 'kind')!.options.map(o => o.count)).toEqual([1, 1, 2]);
      expect(facets.find(f => f.column === 'tags')!.options.map(o => o.count)).toEqual([2, 1, 0]);
      expect(facets.find(f => f.column === 'age')!.options.map(o => o.count)).toEqual([1, 1]);
    });

    it('keeps a selected value that is no longer present, with count 0', () => {
      const list = listFor();
      list.setFacet('kind', ['report', 'gone']);
      const kind = list.facets(ROWS)[0];

      expect(kind.options[kind.options.length - 1]).toEqual({ value: 'gone', label: 'gone', count: 0 });
      expect(kind.selected).toEqual(['report', 'gone']);
    });

    it('treats null and the empty string as no value', () => {
      const rows = [row('x1', ''), row('x2', 'log'), row('x3', 'report')];
      const list = listFor({ facets: [{ column: 'kind', label: 'Kind', values: r => r.kind || null }] });

      expect(list.facets(rows)[0].options.map(o => o.value)).toEqual(['log', 'report']);
      expect(listFor().facets(rows)[0].options.map(o => o.value)).toEqual(['log', 'report']);
    });

    it('does not list a disabled facet, nor chip its selection', () => {
      let tagsEnabled = false;
      const list = listFor({
        facets: [{ column: 'tags', label: 'Tags', values: r => r.tags, enabled: () => tagsEnabled }],
        singleFacets: [],
        memoDeps: () => [tagsEnabled]
      });
      list.setFacet('tags', ['red']);

      expect(list.facets(ROWS)).toEqual([]);
      expect(list.chips(ROWS)).toEqual([]);

      tagsEnabled = true;
      expect(list.facets(ROWS).map(f => f.column)).toEqual(['tags']);
      expect(list.chips(ROWS).map(c => c.key)).toEqual(['tags:red']);
    });

    it('orders the combined list by facetOrder, unranked columns last', () => {
      const list = listFor({ facetOrder: ['age', 'writer'] });

      expect(list.facets(ROWS).map(f => f.column)).toEqual(['age', 'writer', 'kind', 'tags']);
    });
  });

  describe('single-select facets', () => {
    it('renders in single mode with its any label, counting each option', () => {
      const age = listFor().facets(ROWS).find(f => f.column === 'age')!;

      expect(age).toEqual({
        column: 'age',
        facetId: 't-facet-age',
        label: 'Age',
        mode: 'single',
        options: [{ value: 'new', label: 'New', count: 2 }, { value: 'old', label: 'Old', count: 1 }],
        selected: [],
        anyLabel: 'Any age'
      });
    });

    it('is listed while listedWhen holds, or while it has a selection', () => {
      const list = listFor({
        singleFacets: [{
          column: 'age', label: 'Age', anyLabel: 'Any age',
          options: [{ value: 'new', label: 'New' }, { value: 'old', label: 'Old' }],
          matches: (r, value) => r.age === value,
          listedWhen: rows => rows.filter(r => r.age !== null).length >= 2
        }]
      });
      const rows = [ROWS[0], ROWS[3]];

      expect(list.facets(rows).some(f => f.column === 'age')).toBe(false);
      expect(list.facets(ROWS).some(f => f.column === 'age')).toBe(true);

      list.setFacet('age', ['new']);
      const age = list.facets(rows).find(f => f.column === 'age')!;
      expect(age.selected).toEqual(['new']);
      expect(age.options.map(o => o.count)).toEqual([1, 0]);
    });

    it('defaults to listing with two rows or more', () => {
      const list = listFor();

      expect(list.facets([ROWS[0]]).some(f => f.column === 'age')).toBe(false);
      expect(list.facets([ROWS[0], ROWS[1]]).some(f => f.column === 'age')).toBe(true);
    });

    it('sets the filter string to the first value, or clears it', () => {
      const table = tableFor();
      const list = listFor({}, table);

      list.setFacet('age', ['old', 'new']);
      expect(table.filters['age']).toBe('old');
      expect(list.matching(ROWS).map(r => r.id)).toEqual(['a2']);

      list.setFacet('age', []);
      expect(table.filters['age']).toBe('');
    });
  });

  describe('chips', () => {
    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    it('lists one chip per selected value in facet order, then the search', () => {
      const list = listFor();
      list.setFacet('writer', ['none']);
      list.setFacet('kind', ['report', 'log']);
      list.setFacet('age', ['new']);
      list.setSearchInput('  a1 ');
      vi.advanceTimersByTime(200);

      expect(list.chips(ROWS)).toEqual([
        { key: 'kind:report', column: 'kind', value: 'report', facetLabel: 'Kind', valueLabel: 'report' },
        { key: 'kind:log', column: 'kind', value: 'log', facetLabel: 'Kind', valueLabel: 'log' },
        { key: 'writer:none', column: 'writer', value: 'none', facetLabel: 'Written by', valueLabel: 'No writer' },
        { key: 'age:new', column: 'age', value: 'new', facetLabel: 'Age', valueLabel: 'New' },
        { key: 'search', column: 'search', value: 'a1', facetLabel: 'Search', valueLabel: '“a1”' }
      ]);
    });

    it('removes a chip and returns its index among the chips before the removal', () => {
      const table = tableFor();
      const list = listFor({}, table);
      list.setFacet('kind', ['report', 'log']);
      list.setFacet('age', ['new']);
      list.setSearchInput('a');
      vi.advanceTimersByTime(200);
      const chips = list.chips(ROWS);

      expect(list.removeChip(chips[1], ROWS)).toBe(1);
      expect(table.filterValues('kind')).toEqual(['report']);

      expect(list.removeChip(list.chips(ROWS)[1], ROWS)).toBe(1);
      expect(table.filters['age']).toBe('');

      expect(list.removeChip(list.chips(ROWS)[1], ROWS)).toBe(1);
      expect(table.filters['search']).toBe('');
      expect(list.searchText).toBe('');
      expect(list.chips(ROWS).map(c => c.key)).toEqual(['kind:report']);
    });

    it('without rows, finds the index in the chips last computed', () => {
      const list = listFor();
      list.setFacet('kind', ['report', 'log']);
      const chips = list.chips(ROWS);

      expect(list.removeChip(chips[1])).toBe(1);
    });

    it('removing the search chip cancels a pending search', () => {
      const table = tableFor();
      const list = listFor({}, table);
      list.setSearchInput('a');
      vi.advanceTimersByTime(200);
      list.setSearchInput('a1');
      list.removeChip(list.chips(ROWS)[0], ROWS);
      vi.advanceTimersByTime(500);

      expect(table.filters['search']).toBe('');
    });
  });

  describe('memo', () => {
    it('returns the same arrays while nothing changed', () => {
      const list = listFor();
      const facets = list.facets(ROWS);
      const chips = list.chips(ROWS);

      expect(list.facets(ROWS)).toBe(facets);
      expect(list.chips(ROWS)).toBe(chips);
    });

    it('recomputes after a memoDeps element changes', () => {
      let dep: object = {};
      const list = listFor({ memoDeps: () => [dep, 'fixed'] });
      const facets = list.facets(ROWS);

      expect(list.facets(ROWS)).toBe(facets);
      dep = {};
      const next = list.facets(ROWS);
      expect(next).not.toBe(facets);
      expect(next).toEqual(facets);
      expect(list.facets(ROWS)).toBe(next);
    });

    it('recomputes after a memoDeps length change', () => {
      let deps: unknown[] = [1];
      const list = listFor({ memoDeps: () => deps });
      const facets = list.facets(ROWS);
      deps = [1, 2];

      expect(list.facets(ROWS)).not.toBe(facets);
    });

    it('recomputes for a new rows array, an invalidation and a filter change', () => {
      const list = listFor();
      const rows = [...ROWS];
      const first = list.facets(ROWS);

      const second = list.facets(rows);
      expect(second).not.toBe(first);
      expect(list.facets(rows)).toBe(second);

      list.invalidate();
      const third = list.facets(rows);
      expect(third).not.toBe(second);
      expect(list.chips(rows)).toBe(list.chips(rows));

      list.setFacet('kind', ['log']);
      expect(list.facets(rows)).not.toBe(third);
    });
  });

  describe('clearFilters', () => {
    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    it('clears the search, its timer and every filter, resetting the batch', () => {
      const table = tableFor();
      const list = listFor({}, table);
      list.setFacet('kind', ['report']);
      list.setFacet('age', ['new']);
      table.setFilter('selected', 'yes');
      list.setSearchInput('a1');
      list.showMore(manyRows(25));

      list.clearFilters();
      vi.advanceTimersByTime(500);

      expect(list.searchText).toBe('');
      expect(table.filters['search']).toBeUndefined();
      expect(table.filters['selected']).toBeUndefined();
      expect(table.filterValues('kind')).toEqual([]);
      expect(table.hasActiveFilters).toBe(false);
      expect(list.visibleCount).toBe(10);
    });

    it('keeps the filters of the columns named', () => {
      const table = tableFor();
      const list = listFor({}, table);
      list.setFacet('kind', ['report']);
      list.setFacet('tags', ['red']);
      table.setFilter('selected', 'yes');

      list.clearFilters(['selected']);
      expect(table.filters['selected']).toBe('yes');
      expect(table.filterValues('kind')).toEqual([]);
      expect(list.matching(ROWS).map(r => r.id)).toEqual(['a1']);

      list.setFacet('kind', ['report']);
      list.clearFilters(['kind']);
      expect(table.filterValues('kind')).toEqual(['report']);
      expect(table.filters['selected']).toBeUndefined();
    });

    it('does not create an empty entry for a kept column with no filter', () => {
      const table = tableFor();
      const list = listFor({}, table);

      list.clearFilters(['selected']);

      expect('selected' in table.filters).toBe(false);
    });
  });

  describe('reset', () => {
    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    it('clears the search, its timer and every filter, returns to page 1 and restores the stored sort', () => {
      storeSort({ version: 1, sort: 'kind' });
      const table = tableFor();
      const list = listFor({}, table);
      table.setSort('id', 'asc');
      list.setFacet('kind', ['report']);
      list.setSearchInput('a1');
      list.showMore(manyRows(25));
      table.page = 3;
      const facets = list.facets(ROWS);

      list.reset();
      vi.advanceTimersByTime(500);

      expect(list.searchText).toBe('');
      expect(table.filters['search']).toBeUndefined();
      expect(table.hasActiveFilters).toBe(false);
      expect(table.page).toBe(1);
      expect(list.sortId).toBe('kind');
      expect(table.sortColumn).toBe('kind');
      expect(table.sortDirection).toBe('asc');
      expect(list.visibleCount).toBe(10);
      expect(list.facets(ROWS)).not.toBe(facets);
    });

    it('restores the default sort when nothing is stored', () => {
      const table = tableFor();
      const list = listFor({}, table);
      table.setSort('kind', 'asc');

      list.reset();

      expect(list.sortId).toBe('id-desc');
      expect(table.sortColumn).toBe('id');
    });
  });

  describe('statusText', () => {
    const noun = { one: 'document', many: 'documents' };

    it('is empty with no rows at all', () => {
      expect(listFor().statusText([], noun)).toBe('');
    });

    it('counts the shown cards against the matching ones', () => {
      const list = listFor();
      const rows = manyRows(23);

      expect(list.statusText(rows, noun)).toBe('Showing 10 of 23 documents');
      list.showAll(rows);
      expect(list.statusText(rows, noun)).toBe('Showing 23 of 23 documents');
    });

    it('names a single match and no match, with the total while a filter is active', () => {
      const list = listFor();
      list.setFacet('kind', ['log']);
      expect(list.statusText(ROWS, noun)).toBe('One document · filtered from 4');

      list.setFacet('kind', ['gone']);
      expect(list.statusText(ROWS, noun)).toBe('No documents shown · filtered from 4');

      list.setFacet('kind', []);
      expect(list.statusText([ROWS[0]], noun)).toBe('One document');
    });
  });
});
