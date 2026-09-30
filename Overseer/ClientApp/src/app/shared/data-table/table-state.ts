/**
 * Filter / sort / page arithmetic shared by the benchmark tables. With a remote total set it
 * drives a server-paged table instead: the rows it is given are already the current page, and
 * only the page arithmetic applies.
 *
 * Deliberately free of any Angular dependency: it holds no injectables, touches no DOM and
 * emits no events, so it unit-tests as plain TypeScript and a component owns it as an
 * ordinary field.
 */

/** Direction of the active sort column. */
export type SortDirection = 'asc' | 'desc';

/** The comparable value a sort accessor yields. */
export type SortKey = string | number | Date;

/**
 * Reads one column's sort key off a row. Null and undefined sort last in both directions, so
 * a run with no Intelligence Index never displaces a scored one at the top of a descending
 * sort.
 */
export type SortAccessor<T> = (row: T) => SortKey | null | undefined;

/** Reads one column's filter text off a row. Matching is case-insensitive. */
export type FilterAccessor<T> = (row: T) => string | null | undefined;

/**
 * How a filter value is compared against the cell text.
 *
 * `substring` is case-insensitive containment, for a free-text input. `exact` is
 * case-insensitive equality, for a `<select>` whose option values are the whole legal set —
 * a "Failed" option must not also match "FailedValidation".
 *
 * `anyOf` matches a row whose values include any of the selected ones, for a multi-select facet;
 * its selection lives in `valueFilters`, not `filters`. `custom` hands the filter string to a
 * predicate, for a search over several fields or a derived condition such as a date range.
 */
export type FilterMode = 'substring' | 'exact' | 'anyOf' | 'custom';

/** Reads the values an `anyOf` column matches against off a row. */
export type FilterValuesAccessor<T> = (row: T) => string | readonly string[] | null | undefined;

/** A filter column declared with an explicit mode. */
export type FilterDefinition<T> =
  | { readonly mode: 'substring' | 'exact'; readonly accessor: FilterAccessor<T> }
  | { readonly mode: 'anyOf'; readonly values: FilterValuesAccessor<T> }
  | { readonly mode: 'custom'; readonly test: (row: T, value: string) => boolean };

/** Either form a consumer may register a filter column in: a bare accessor means `substring`. */
export type FilterRegistration<T> = FilterAccessor<T> | FilterDefinition<T>;

/**
 * Declares a column as an equality filter rather than the default containment one. Pair it
 * with a `<select>`, whose empty-string value means "no filter".
 */
export function exactFilter<T>(accessor: FilterAccessor<T>): FilterDefinition<T> {
  return { accessor, mode: 'exact' };
}

/**
 * Declares a multi-select column: a row matches when any of its values is selected, compared
 * case-insensitively after trimming. Set the selection with `setFilterValues`.
 */
export function anyOfFilter<T>(values: FilterValuesAccessor<T>): FilterDefinition<T> {
  return { values, mode: 'anyOf' };
}

/**
 * Declares a column matched by a predicate over the trimmed filter string. The column is active
 * while that string is non-blank.
 */
export function customFilter<T>(test: (row: T, value: string) => boolean): FilterDefinition<T> {
  return { test, mode: 'custom' };
}

/** The page sizes every table offers. */
export const PAGE_SIZES = [10, 20, 50, 100] as const;

/** The elision marker `pageNumbers()` puts between page runs. */
export const PAGE_ELLIPSIS = '…';

/** The pager never renders more numbered slots than this, whatever the page count. */
export const MAX_PAGE_SLOTS = 7;

/** Optional construction settings for a `TableState`. */
export interface TableStateOptions {
  /** The page sizes the pager offers; the first is the initial size. Defaults to `PAGE_SIZES`. */
  readonly pageSizes?: readonly number[];
}

export class TableState<T> {
  page = 1;
  pageSize: number;
  sortColumn: string;
  sortDirection: SortDirection;
  readonly filters: Record<string, string> = {};
  /** The selections of `anyOf` columns; a column with no entry is inactive. */
  readonly valueFilters: Record<string, readonly string[]> = {};
  readonly pageSizes: readonly number[];

  /**
   * The server's row count for a server-paged table, or null for a local one. While set, the
   * rows handed to every method are the current page only: filters and sort are inert, and
   * the page arithmetic counts this total instead of the rows.
   */
  remoteTotal: number | null = null;

  private readonly sortAccessors: Record<string, SortAccessor<T>> = {};
  private readonly filterDefinitions: Record<string, FilterDefinition<T>> = {};

  constructor(defaultSortColumn: string, defaultSortDirection: SortDirection = 'desc',
              options: TableStateOptions = {}) {
    this.sortColumn = defaultSortColumn;
    this.sortDirection = defaultSortDirection;
    this.pageSizes = options.pageSizes ?? PAGE_SIZES;
    this.pageSize = this.pageSizes[0];
  }

  /** Switches to server paging with the given total, or back to local paging with null. */
  setRemoteTotal(total: number | null): void {
    this.remoteTotal = total === null ? null : Math.max(0, Math.trunc(total));
  }

  /**
   * Registers the per-column accessors. Returns `this`, so a component can declare the whole
   * table in one field initialiser. Calling it again merges, replacing only the columns named.
   *
   * A sort column with no registered accessor leaves the rows in source order; a filter column
   * with no registered accessor is ignored rather than matching nothing.
   */
  registerAccessors(
    sort: Record<string, SortAccessor<T>>,
    filter: Record<string, FilterRegistration<T>> = {}
  ): this {
    for (const column of Object.keys(sort)) {
      this.sortAccessors[column] = sort[column];
    }
    for (const column of Object.keys(filter)) {
      const declared = filter[column];
      this.filterDefinitions[column] = typeof declared === 'function'
        ? { accessor: declared, mode: 'substring' }
        : declared;
    }
    return this;
  }

  /**
   * Filter, then sort, then page — paging counts the filtered rows, not all of them. The
   * result is a new array and the input is never touched, so the caller's source list stays
   * in server order however often the view is re-read.
   */
  view(rows: readonly T[]): T[] {
    if (this.remoteTotal !== null) {
      this.clampPage(this.remoteTotal);
      return rows.slice();
    }
    const filtered = this.applyFilters(rows);
    const page = this.clampPage(filtered.length);
    const start = (page - 1) * this.pageSize;
    return this.applySort(filtered).slice(start, start + this.pageSize);
  }

  /**
   * Filter and sort without paging. Exports and clipboard copies take the whole matching set,
   * not just the visible page, so they read this instead of `view`.
   */
  viewAll(rows: readonly T[]): T[] {
    if (this.remoteTotal !== null) {
      return rows.slice();
    }
    return this.applySort(this.applyFilters(rows));
  }

  /** How many rows survive the active filters, or the server's total for a server-paged table. */
  filteredCount(rows: readonly T[]): number {
    if (this.remoteTotal !== null) {
      return this.remoteTotal;
    }
    return this.applyFilters(rows).length;
  }

  /**
   * The rows every active filter but `column`'s lets through, unsorted and unpaged: what a facet
   * counts its options against, so each count says what choosing that option would leave.
   */
  filterRowsExcept(rows: readonly T[], column: string): T[] {
    if (this.remoteTotal !== null) {
      return rows.slice();
    }
    return this.applyFilters(rows, column);
  }

  /**
   * True when rows exist but the filters hide all of them. This is a different empty state
   * from `rows.length === 0`, which means nothing has been recorded yet, and the two want
   * different messages.
   */
  noMatches(rows: readonly T[]): boolean {
    if (this.remoteTotal !== null) {
      return false;
    }
    return rows.length > 0 && this.filteredCount(rows) === 0;
  }

  totalPages(rows: readonly T[]): number {
    const count = this.filteredCount(rows);
    this.clampPage(count);
    return Math.max(1, Math.ceil(count / this.pageSize));
  }

  /** 1-based index of the first row on the current page, or 0 when nothing matches. */
  rangeStart(rows: readonly T[]): number {
    const count = this.filteredCount(rows);
    if (count === 0) {
      return 0;
    }
    return (this.clampPage(count) - 1) * this.pageSize + 1;
  }

  /** 1-based index of the last row on the current page, or 0 when nothing matches. */
  rangeEnd(rows: readonly T[]): number {
    const count = this.filteredCount(rows);
    if (count === 0) {
      return 0;
    }
    return Math.min(this.clampPage(count) * this.pageSize, count);
  }

  /** Page numbers with `…` elision, at most `MAX_PAGE_SLOTS` slots wide. */
  pageNumbers(rows: readonly T[]): (number | '…')[] {
    const total = this.totalPages(rows);
    const current = this.clampPage(this.filteredCount(rows));
    const sibling = (MAX_PAGE_SLOTS - 5) / 2;

    if (total <= MAX_PAGE_SLOTS) {
      return Array.from({ length: total }, (_, i) => i + 1);
    }

    const left = Math.max(current - sibling, 1);
    const right = Math.min(current + sibling, total);
    const showLeftDots = left > 2;
    const showRightDots = right < total - 1;
    const run = 3 + 2 * sibling;

    if (!showLeftDots && showRightDots) {
      return [...Array.from({ length: run }, (_, i) => i + 1), PAGE_ELLIPSIS, total];
    }
    if (showLeftDots && !showRightDots) {
      return [1, PAGE_ELLIPSIS, ...Array.from({ length: run }, (_, i) => total - run + 1 + i)];
    }

    const mid = Array.from({ length: right - left + 1 }, (_, i) => left + i);
    return [1, PAGE_ELLIPSIS, ...mid, PAGE_ELLIPSIS, total];
  }

  /** The same column flips direction; a new column starts descending. Either way, page 1. */
  toggleSort(column: string): void {
    if (this.sortColumn === column) {
      this.sortDirection = this.sortDirection === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortColumn = column;
      this.sortDirection = 'desc';
    }
    this.page = 1;
  }

  /** Sets the column and direction at once, for a sort control that is not a column header. Page 1. */
  setSort(column: string, direction: SortDirection): void {
    this.sortColumn = column;
    this.sortDirection = direction;
    this.page = 1;
  }

  /** Keeps the row that was at the top of the page in view, so the page never lands past the end. */
  setPageSize(size: number): void {
    const next = Math.max(1, Math.trunc(size));
    const firstRowIndex = (Math.max(1, this.page) - 1) * this.pageSize;
    this.pageSize = next;
    this.page = Math.floor(firstRowIndex / next) + 1;
  }

  setPage(page: number, rows: readonly T[]): void {
    const total = this.totalPages(rows);
    this.page = Math.min(Math.max(Math.trunc(page), 1), total);
  }

  hasPrevious(rows: readonly T[]): boolean {
    return this.clampPage(this.filteredCount(rows)) > 1;
  }

  hasNext(rows: readonly T[]): boolean {
    return this.clampPage(this.filteredCount(rows)) < this.totalPages(rows);
  }

  /** An empty or blank value clears the column's filter. Any change returns to page 1. */
  setFilter(column: string, value: string): void {
    this.filters[column] = value ?? '';
    this.page = 1;
  }

  /** An `anyOf` column's selection, de-duplicated; an empty list clears it. Any change returns to page 1. */
  setFilterValues(column: string, values: readonly string[]): void {
    const unique = [...new Set(values ?? [])];
    if (unique.length === 0) {
      delete this.valueFilters[column];
    } else {
      this.valueFilters[column] = unique;
    }
    this.page = 1;
  }

  /** An `anyOf` column's selection, or an empty list. */
  filterValues(column: string): readonly string[] {
    return this.valueFilters[column] ?? [];
  }

  clearFilters(): void {
    for (const column of Object.keys(this.filters)) {
      delete this.filters[column];
    }
    for (const column of Object.keys(this.valueFilters)) {
      delete this.valueFilters[column];
    }
    this.page = 1;
  }

  get hasActiveFilters(): boolean {
    if (this.remoteTotal !== null) {
      return false;
    }
    return this.activeFilterColumns().length > 0;
  }

  /**
   * The `aria-sort` value for a column's `<th>`. It is the single source of truth for the
   * sorted state: the caret is drawn from the attribute in CSS, so a styled-but-unannounced
   * header cannot be written.
   */
  ariaSort(column: string): 'ascending' | 'descending' | null {
    if (this.sortColumn !== column) {
      return null;
    }
    return this.sortDirection === 'asc' ? 'ascending' : 'descending';
  }

  /**
   * The page is clamped whenever it is read rather than only when it is set, because the row
   * set can shrink under the table — a delete, a reload, a newly applied filter elsewhere —
   * without any setter being called.
   */
  private clampPage(filteredCount: number): number {
    const total = Math.max(1, Math.ceil(filteredCount / this.pageSize));
    if (this.page > total) {
      this.page = total;
    }
    if (this.page < 1) {
      this.page = 1;
    }
    return this.page;
  }

  private activeFilterColumns(): string[] {
    const text = Object.keys(this.filters).filter(column => (this.filters[column] ?? '').trim() !== '');
    const values = Object.keys(this.valueFilters)
      .filter(column => this.valueFilters[column].length > 0 && !text.includes(column));
    return [...text, ...values];
  }

  private applyFilters(rows: readonly T[], except: string | null = null): T[] {
    const columns = this.activeFilterColumns().filter(column => column !== except);
    if (columns.length === 0) {
      return rows.slice();
    }
    return rows.filter(row => columns.every(column => this.matchesFilter(row, column)));
  }

  private matchesFilter(row: T, column: string): boolean {
    const definition = this.filterDefinitions[column];
    if (!definition) {
      return true;
    }
    if (definition.mode === 'anyOf') {
      const selected = this.filterValues(column).map(TableState.normalize);
      if (selected.length === 0) {
        return true;
      }
      const raw = definition.values(row);
      const values = raw === null || raw === undefined ? [] : typeof raw === 'string' ? [raw] : raw;
      return values.some(value => selected.includes(TableState.normalize(value)));
    }
    const value = (this.filters[column] ?? '').trim();
    if (definition.mode === 'custom') {
      return value === '' || definition.test(row, value);
    }
    const needle = value.toLowerCase();
    const cell = definition.accessor(row);
    if (cell === null || cell === undefined) {
      return false;
    }
    const haystack = String(cell).trim().toLowerCase();
    return definition.mode === 'exact' ? haystack === needle : haystack.includes(needle);
  }

  private static normalize(value: string): string {
    return String(value).trim().toLowerCase();
  }

  private applySort(rows: readonly T[]): T[] {
    const accessor = this.sortAccessors[this.sortColumn];
    if (!accessor) {
      return rows.slice();
    }
    const direction = this.sortDirection === 'asc' ? 1 : -1;

    // Carrying the source index makes ties keep source order independently of the engine's
    // own sort stability, so no secondary sort column is needed for a deterministic view.
    return rows
      .map((row, index) => ({ row, index }))
      .sort((a, b) => {
        const keyA = accessor(a.row);
        const keyB = accessor(b.row);
        const emptyA = TableState.isEmptyKey(keyA);
        const emptyB = TableState.isEmptyKey(keyB);

        if (emptyA && emptyB) {
          return a.index - b.index;
        }
        // Missing keys land at the bottom in both directions, so the direction multiplier
        // is deliberately not applied to them.
        if (emptyA) {
          return 1;
        }
        if (emptyB) {
          return -1;
        }
        const compared = TableState.compareKeys(keyA as SortKey, keyB as SortKey);
        return compared === 0 ? a.index - b.index : compared * direction;
      })
      .map(entry => entry.row);
  }

  private static isEmptyKey(key: SortKey | null | undefined): boolean {
    return key === null || key === undefined || (typeof key === 'number' && Number.isNaN(key));
  }

  private static compareKeys(a: SortKey, b: SortKey): number {
    if (typeof a === 'number' && typeof b === 'number') {
      return a - b;
    }
    if (a instanceof Date && b instanceof Date) {
      return a.getTime() - b.getTime();
    }
    // Numeric collation, so "#9" ranks below "#10" instead of lexicographically above it.
    return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: 'base' });
  }
}
