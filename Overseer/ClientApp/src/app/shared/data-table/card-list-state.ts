/**
 * The state of a filterable card list: a batch of cards with Show more, a debounced search field,
 * a remembered Sort by order, faceted filters with per-option counts, and the removable chips of
 * the active filters. It wraps a caller-supplied `TableState`, which owns the filter definitions,
 * the sort accessors and the filtering itself.
 *
 * Deliberately free of any Angular dependency, like `TableState`: it touches no DOM beyond the
 * event it is handed, emits no events and leaves focus to the host, so it unit-tests as plain
 * TypeScript and a component owns it as an ordinary field.
 */

import type { FilterFacetOption } from './filter-facet.component';
import type { SortDirection, TableState } from './table-state';

/** One order Sort by offers. */
export interface CardListSort {
  id: string;
  label: string;
  column: string;
  direction: SortDirection;
}

/**
 * The order of a facet's present values: alphabetical (numeric collation, case-insensitive), a
 * fixed list that keeps only the values present, or a comparator.
 */
export type CardListFacetOrder = 'alphabetical' | readonly string[] | ((a: string, b: string) => number);

/**
 * A multi-select facet over an `anyOf` column. `values` is the accessor registered with
 * `anyOfFilter`; a string is one value, and null or an empty string is none.
 */
export interface CardListFacetSpec<T> {
  column: string;
  label: string;
  values: (row: T) => string | readonly string[] | null | undefined;
  /** The order of the present values; alphabetical by default. */
  order?: CardListFacetOrder;
  /** What a value reads as in the facet and its chip; the value itself by default. */
  labelOf?: (value: string) => string;
  /** The single mode's clearing option; unused by a multi-select facet. `Any` by default. */
  anyLabel?: string;
  /** While false the facet is not listed and contributes no chips. */
  enabled?: () => boolean;
}

/** A single-select facet over a `custom` column, whose filter string is the chosen option's value. */
export interface CardListSingleFacetSpec<T> {
  column: string;
  label: string;
  /** The first option, which clears the filter: `Any time`. */
  anyLabel: string;
  options: readonly { value: string; label: string }[];
  /** Whether a row falls under an option; each option counts the rows every other filter lets through. */
  matches: (row: T, value: string) => boolean;
  /** Whether the rows justify listing the facet; two rows or more by default. A selection always lists it. */
  listedWhen?: (rows: readonly T[]) => boolean;
  /** While false the facet is not listed and contributes no chips. */
  enabled?: () => boolean;
}

/** A facet of the filter bar, as `app-filter-facet` renders it. */
export interface CardListFacet {
  column: string;
  facetId: string;
  label: string;
  mode: 'multiple' | 'single';
  options: FilterFacetOption[];
  selected: readonly string[];
  anyLabel: string;
}

/** One removable chip of the active filters: a facet value, or the search. */
export interface CardListChip {
  key: string;
  column: string;
  value: string;
  facetLabel: string;
  valueLabel: string;
}

/** The noun the status line counts in: `document` / `documents`. */
export interface CardListNoun {
  one: string;
  many: string;
}

/** Construction settings for a `CardListState`. */
export interface CardListStateOptions<T> {
  /** The prefix of every facet id: `${idPrefix}-facet-${column}`. */
  idPrefix: string;
  /** How many cards the list shows at first, and how many more Show more adds. Defaults to 10. */
  batch?: number;
  /** The `custom` column the search field sets. Defaults to `search`. */
  searchColumn?: string;
  /** The wait after the last keystroke before the search applies. Defaults to 200 ms. */
  debounceMs?: number;
  /** The orders Sort by offers. */
  sorts: readonly CardListSort[];
  /** The order used when none is stored, or the stored one is unknown. */
  defaultSort: string;
  /** The `localStorage` key the chosen order is remembered under, as `{ version: 1, sort }`. */
  storageKey: string;
  facets: readonly CardListFacetSpec<T>[];
  singleFacets?: readonly CardListSingleFacetSpec<T>[];
  /** The column order of the combined facet list; by default the multi-select facets, then the single ones. */
  facetOrder?: readonly string[];
  /** Further inputs the facets depend on, compared element by element with `===`. */
  memoDeps?: () => readonly unknown[];
  /** Called after a debounced search applies, outside any event handler. */
  onChange?: () => void;
}

/** The version of the stored view settings this code reads and writes. */
const STORAGE_VERSION = 1;

const DEFAULT_BATCH = 10;
const DEFAULT_SEARCH_COLUMN = 'search';
const DEFAULT_DEBOUNCE_MS = 200;

export class CardListState<T> {
  readonly idPrefix: string;
  readonly batch: number;
  readonly searchColumn: string;
  readonly debounceMs: number;
  readonly sorts: readonly CardListSort[];
  readonly defaultSort: string;
  readonly storageKey: string;

  /** How many of the matching cards the list shows. */
  visibleCount: number;
  /** The search field's text; it filters the list once typing pauses. */
  searchText = '';
  /** The chosen order of Sort by. */
  sortId: string;

  private readonly facetSpecs: readonly CardListFacetSpec<T>[];
  private readonly singleFacetSpecs: readonly CardListSingleFacetSpec<T>[];
  private readonly facetOrder: readonly string[] | null;
  private readonly memoDeps: (() => readonly unknown[]) | null;
  private readonly onChange: (() => void) | null;

  /** The pending search, applied once typing pauses. */
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  /** Bumped on every change to a filter or the search, and by `invalidate()`: the facets' memo key. */
  private revision = 0;
  private memo: {
    revision: number;
    rows: readonly T[];
    deps: readonly unknown[];
    facets: CardListFacet[];
    chips: CardListChip[];
  } | null = null;

  constructor(readonly table: TableState<T>, options: CardListStateOptions<T>) {
    this.idPrefix = options.idPrefix;
    this.batch = options.batch ?? DEFAULT_BATCH;
    this.searchColumn = options.searchColumn ?? DEFAULT_SEARCH_COLUMN;
    this.debounceMs = options.debounceMs ?? DEFAULT_DEBOUNCE_MS;
    this.sorts = options.sorts;
    this.defaultSort = options.defaultSort;
    this.storageKey = options.storageKey;
    this.facetSpecs = options.facets;
    this.singleFacetSpecs = options.singleFacets ?? [];
    this.facetOrder = options.facetOrder ?? null;
    this.memoDeps = options.memoDeps ?? null;
    this.onChange = options.onChange ?? null;
    this.visibleCount = this.batch;
    this.sortId = this.applySort(this.readStoredSort());
  }

  // --- Batching ---

  /** Every row the filters let through, in the chosen order. */
  matching(rows: readonly T[]): T[] {
    return this.table.viewAll(rows);
  }

  /** The cards on screen: the first `visibleCount` matching rows. */
  view(rows: readonly T[]): T[] {
    return this.matching(rows).slice(0, this.visibleCount);
  }

  /** Matching rows not yet shown. */
  remainingCount(rows: readonly T[]): number {
    return Math.max(0, this.matching(rows).length - this.visibleCount);
  }

  /** How many cards Show more adds. */
  nextBatchCount(rows: readonly T[]): number {
    return Math.min(this.batch, this.remainingCount(rows));
  }

  /** Shows the next batch. Returns the index of the first new card, for the host to focus. */
  showMore(rows: readonly T[]): number {
    return this.revealTo(rows, this.visibleCount + this.batch);
  }

  /** Shows every matching card. Returns the index of the first new card, for the host to focus. */
  showAll(rows: readonly T[]): number {
    return this.revealTo(rows, this.matching(rows).length);
  }

  /** Back to the first batch. A reload of the same rows or a delete leaves the batch alone. */
  resetBatch(): void {
    this.visibleCount = this.batch;
  }

  /** Marks the facets and chips stale, for a change the host makes outside this class. */
  invalidate(): void {
    this.revision++;
  }

  // --- Search ---

  /** Records the field's text and applies it once typing pauses, then calls `onChange`. */
  setSearchInput(text: string): void {
    this.searchText = text;
    this.cancelSearchTimer();
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.applySearch();
      this.onChange?.();
    }, this.debounceMs);
  }

  /**
   * Escape with text in the field clears it at once and returns true, with the event's default
   * and propagation stopped. Escape in an empty field, or any other key, returns false and is left
   * to whatever surrounds the list, such as a dialog.
   */
  clearSearchOnEscape(event: KeyboardEvent): boolean {
    const input = event.target as HTMLInputElement | null;
    if (event.key !== 'Escape' || !input || input.value === '') {
      return false;
    }
    event.preventDefault();
    event.stopPropagation();
    input.value = '';
    this.searchText = '';
    this.cancelSearchTimer();
    this.applySearch();
    return true;
  }

  // --- Sort ---

  /** Applies and remembers a Sort by order and resets the batch. An unknown id changes nothing and returns false. */
  setSort(id: string): boolean {
    if (!this.sorts.some(sort => sort.id === id)) {
      return false;
    }
    this.sortId = this.applySort(id);
    this.resetBatch();
    this.writeStoredSort(id);
    return true;
  }

  // --- Facets and chips ---

  /**
   * Sets a facet's selection: a single-select facet takes the first value, or none. Resets the
   * batch.
   */
  setFacet(column: string, values: readonly string[]): void {
    if (this.isSingleColumn(column)) {
      this.table.setFilter(column, values[0] ?? '');
    } else {
      this.table.setFilterValues(column, values);
    }
    this.resetBatch();
    this.invalidate();
  }

  /**
   * The listed facets, in `facetOrder`. A facet is listed while its rows hold two values or more
   * (a single-select facet: while `listedWhen` holds), or while it has a selection. Each option
   * counts the rows every other active filter lets through. Memoized: the same array comes back
   * until a filter changes, `invalidate()` is called, the rows array is replaced or a `memoDeps`
   * element changes.
   */
  facets(rows: readonly T[]): CardListFacet[] {
    return this.state(rows).facets;
  }

  /** One chip per selected value of a listed facet, then one for the search. Memoized with `facets`. */
  chips(rows: readonly T[]): CardListChip[] {
    return this.state(rows).chips;
  }

  /**
   * Removes one chip's filter and resets the batch. Returns the chip's index among the chips
   * before the removal, for the host's focus logic: in `chips(rows)` when `rows` is given, else in
   * the chips last computed; -1 when it is not found there.
   */
  removeChip(chip: CardListChip, rows?: readonly T[]): number {
    const before = rows ? this.chips(rows) : this.memo?.chips ?? [];
    const index = before.findIndex(c => c.key === chip.key);
    if (chip.column === this.searchColumn) {
      this.searchText = '';
      this.cancelSearchTimer();
      this.table.setFilter(this.searchColumn, '');
    } else if (this.isSingleColumn(chip.column)) {
      this.table.setFilter(chip.column, '');
    } else {
      this.table.setFilterValues(chip.column, this.table.filterValues(chip.column).filter(value => value !== chip.value));
    }
    this.resetBatch();
    this.invalidate();
    return index;
  }

  /**
   * Clears the search and every filter but those of `keepColumns`, and resets the batch. Focus is
   * the host's job.
   */
  clearFilters(keepColumns: readonly string[] = []): void {
    const kept = keepColumns.map(column => ({
      column,
      text: this.table.filters[column],
      values: [...this.table.filterValues(column)]
    }));
    this.cancelSearchTimer();
    this.searchText = '';
    this.table.clearFilters();
    for (const { column, text, values } of kept) {
      if (text !== undefined && text !== '') {
        this.table.setFilter(column, text);
      }
      if (values.length > 0) {
        this.table.setFilterValues(column, values);
      }
    }
    this.resetBatch();
    this.invalidate();
  }

  /**
   * Starts a new list: clears the search, its timer and every filter, returns to page 1, restores
   * the stored sort and resets the batch.
   */
  reset(): void {
    this.cancelSearchTimer();
    this.searchText = '';
    this.table.clearFilters();
    this.table.page = 1;
    this.sortId = this.applySort(this.readStoredSort());
    this.resetBatch();
    this.invalidate();
  }

  /**
   * `Showing 10 of 23 documents`, `One document` or `No documents shown`, with
   * ` · filtered from 40` while a filter is active; empty while there are no rows at all.
   */
  statusText(rows: readonly T[], noun: CardListNoun): string {
    const total = rows.length;
    if (total === 0) {
      return '';
    }
    const matching = this.matching(rows).length;
    const shown = Math.min(this.visibleCount, matching);
    const text = matching === 0
      ? `No ${noun.many} shown`
      : matching === 1
        ? `One ${noun.one}`
        : `Showing ${shown} of ${matching} ${noun.many}`;
    return this.table.hasActiveFilters ? `${text} · filtered from ${total}` : text;
  }

  /** Cancels a pending search. */
  dispose(): void {
    this.cancelSearchTimer();
  }

  // --- Internals ---

  private revealTo(rows: readonly T[], count: number): number {
    const before = this.view(rows).length;
    this.visibleCount = Math.max(count, this.batch);
    return before;
  }

  private applySearch(): void {
    this.table.setFilter(this.searchColumn, this.searchText);
    this.resetBatch();
    this.invalidate();
  }

  private cancelSearchTimer(): void {
    if (this.searchTimer !== null) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }
  }

  /** Sets the table's sort to the order `id` names, else the default, else the first; returns the id applied. */
  private applySort(id: string): string {
    const sort = this.sorts.find(s => s.id === id)
      ?? this.sorts.find(s => s.id === this.defaultSort)
      ?? this.sorts[0];
    if (!sort) {
      return id;
    }
    this.table.setSort(sort.column, sort.direction);
    return sort.id;
  }

  /** The remembered Sort by order; the default when none is stored, it is unreadable or unknown. */
  private readStoredSort(): string {
    try {
      const raw = localStorage.getItem(this.storageKey);
      const parsed = raw ? JSON.parse(raw) as { version?: unknown; sort?: unknown } | null : null;
      const sort = parsed && typeof parsed === 'object' && parsed.version === STORAGE_VERSION ? parsed.sort : null;
      return this.sorts.find(option => option.id === sort)?.id ?? this.defaultSort;
    } catch {
      return this.defaultSort;
    }
  }

  private writeStoredSort(sort: string): void {
    try {
      localStorage.setItem(this.storageKey, JSON.stringify({ version: STORAGE_VERSION, sort }));
    } catch {
      // Storage full or unavailable: the order is simply not remembered.
    }
  }

  private isSingleColumn(column: string): boolean {
    return this.singleFacetSpecs.some(spec => spec.column === column);
  }

  private state(rows: readonly T[]): { facets: CardListFacet[]; chips: CardListChip[] } {
    const deps = this.memoDeps?.() ?? [];
    const memo = this.memo;
    if (memo && memo.revision === this.revision && memo.rows === rows && CardListState.sameDeps(memo.deps, deps)) {
      return memo;
    }
    const facets = this.buildFacets(rows);
    const chips = this.buildChips(facets);
    this.memo = { revision: this.revision, rows, deps: [...deps], facets, chips };
    return this.memo;
  }

  private static sameDeps(a: readonly unknown[], b: readonly unknown[]): boolean {
    return a.length === b.length && a.every((value, index) => value === b[index]);
  }

  private buildFacets(rows: readonly T[]): CardListFacet[] {
    const byColumn = new Map<string, CardListFacet>();
    const columns: string[] = [];

    for (const spec of this.facetSpecs) {
      columns.push(spec.column);
      if (spec.enabled && !spec.enabled()) {
        continue;
      }
      const facet = this.buildMultipleFacet(rows, spec);
      if (facet) {
        byColumn.set(spec.column, facet);
      }
    }
    for (const spec of this.singleFacetSpecs) {
      columns.push(spec.column);
      if (spec.enabled && !spec.enabled()) {
        continue;
      }
      const facet = this.buildSingleFacet(rows, spec);
      if (facet) {
        byColumn.set(spec.column, facet);
      }
    }

    const order = this.facetOrder;
    if (order) {
      const rank = (column: string): number => {
        const index = order.indexOf(column);
        return index < 0 ? Number.POSITIVE_INFINITY : index;
      };
      // A stable sort: columns outside `facetOrder` keep their default order, after the ranked ones.
      columns.sort((a, b) => {
        const rankA = rank(a);
        const rankB = rank(b);
        return rankA === rankB ? 0 : rankA < rankB ? -1 : 1;
      });
    }
    return columns.map(column => byColumn.get(column)).filter((facet): facet is CardListFacet => !!facet);
  }

  private buildMultipleFacet(rows: readonly T[], spec: CardListFacetSpec<T>): CardListFacet | null {
    const valuesOf = (row: T): readonly string[] => CardListState.normalizeValues(spec.values(row));
    const present = new Set(rows.flatMap(valuesOf));
    const selected = this.table.filterValues(spec.column);
    if (present.size < 2 && selected.length === 0) {
      return null;
    }
    const counted = this.table.filterRowsExcept(rows, spec.column).map(valuesOf);
    const ordered = CardListState.orderValues(present, spec.order);
    for (const value of selected) {
      if (!ordered.includes(value)) {
        ordered.push(value);
      }
    }
    const labelOf = spec.labelOf ?? ((value: string) => value);
    return {
      column: spec.column,
      facetId: `${this.idPrefix}-facet-${spec.column}`,
      label: spec.label,
      mode: 'multiple',
      options: ordered.map(value => ({ value, label: labelOf(value), count: counted.filter(v => v.includes(value)).length })),
      selected,
      anyLabel: spec.anyLabel ?? 'Any'
    };
  }

  private buildSingleFacet(rows: readonly T[], spec: CardListSingleFacetSpec<T>): CardListFacet | null {
    const current = this.table.filters[spec.column] ?? '';
    const listed = spec.listedWhen ? spec.listedWhen(rows) : rows.length >= 2;
    if (!listed && current === '') {
      return null;
    }
    const counted = this.table.filterRowsExcept(rows, spec.column);
    return {
      column: spec.column,
      facetId: `${this.idPrefix}-facet-${spec.column}`,
      label: spec.label,
      mode: 'single',
      options: spec.options.map(option => ({
        value: option.value,
        label: option.label,
        count: counted.filter(row => spec.matches(row, option.value)).length
      })),
      selected: current ? [current] : [],
      anyLabel: spec.anyLabel
    };
  }

  private buildChips(facets: readonly CardListFacet[]): CardListChip[] {
    const chips: CardListChip[] = [];
    for (const facet of facets) {
      for (const value of facet.selected) {
        chips.push({
          key: `${facet.column}:${value}`,
          column: facet.column,
          value,
          facetLabel: facet.label,
          valueLabel: facet.options.find(option => option.value === value)?.label ?? value
        });
      }
    }
    const search = (this.table.filters[this.searchColumn] ?? '').trim();
    if (search) {
      chips.push({ key: 'search', column: this.searchColumn, value: search, facetLabel: 'Search', valueLabel: `“${search}”` });
    }
    return chips;
  }

  /** A facet accessor's result as a list: null, undefined and the empty string are no value. */
  private static normalizeValues(raw: string | readonly string[] | null | undefined): readonly string[] {
    if (raw === null || raw === undefined || raw === '') {
      return [];
    }
    return typeof raw === 'string' ? [raw] : raw;
  }

  private static orderValues(present: Set<string>, order: CardListFacetOrder | undefined): string[] {
    if (typeof order === 'function') {
      return [...present].sort(order);
    }
    if (order !== undefined && order !== 'alphabetical') {
      return order.filter(value => present.has(value));
    }
    return [...present].sort((a, b) => a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' }));
  }
}
