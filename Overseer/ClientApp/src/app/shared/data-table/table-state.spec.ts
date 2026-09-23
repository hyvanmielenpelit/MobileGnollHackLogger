import { exactFilter, MAX_PAGE_SLOTS, PAGE_ELLIPSIS, PAGE_SIZES, TableState } from './table-state';

interface Row {
  id: string;
  name: string;
  status: string;
  score: number | null;
  tier: number;
  selected: boolean;
}

function row(id: string, name: string, status: string, score: number | null, tier = 0,
             selected = false): Row {
  return { id, name, status, score, tier, selected };
}

/** A state with every accessor the specs use, registered exactly as a component would. */
function stateFor(defaultColumn = 'score', direction: 'asc' | 'desc' = 'desc'): TableState<Row> {
  return new TableState<Row>(defaultColumn, direction).registerAccessors(
    {
      id: r => r.id,
      name: r => r.name,
      score: r => r.score,
      tier: r => r.tier,
      selected: r => (r.selected ? 0 : 1)
    },
    {
      name: r => r.name,
      status: exactFilter(r => r.status),
      selected: exactFilter(r => (r.selected ? 'yes' : 'no'))
    }
  );
}

describe('TableState', () => {
  describe('construction', () => {
    it('takes its default sort column and direction from the constructor', () => {
      const state = new TableState<Row>('name', 'asc');

      expect(state.sortColumn).toBe('name');
      expect(state.sortDirection).toBe('asc');
      expect(state.page).toBe(1);
      expect(state.pageSize).toBe(10);
      expect(state.pageSizes).toEqual(PAGE_SIZES);
    });

    it('defaults the direction to descending', () => {
      expect(new TableState<Row>('score').sortDirection).toBe('desc');
    });
  });

  describe('view', () => {
    it('filters, then sorts, then pages, counting only the filtered rows', () => {
      const rows = [
        row('#1', 'alpha', 'Completed', 10),
        row('#2', 'beta', 'Failed', 90),
        row('#3', 'alpha two', 'Completed', 30),
        row('#4', 'gamma', 'Completed', 80),
        row('#5', 'alpha three', 'Completed', 20),
        row('#6', 'alpha four', 'Completed', 40)
      ];
      const state = stateFor('score', 'desc');
      state.setFilter('name', 'alpha');
      state.pageSize = 2;

      // Four rows match; sorted descending by score that is 40, 30, 20, 10.
      expect(state.filteredCount(rows)).toBe(4);
      expect(state.totalPages(rows)).toBe(2);
      expect(state.view(rows).map(r => r.id)).toEqual(['#6', '#3']);

      state.setPage(2, rows);
      expect(state.view(rows).map(r => r.id)).toEqual(['#5', '#1']);
    });

    it('never mutates the array it is given', () => {
      const rows = Object.freeze([
        row('#1', 'alpha', 'Completed', 10),
        row('#2', 'beta', 'Completed', 90),
        row('#3', 'gamma', 'Completed', 50)
      ]) as readonly Row[];
      const state = stateFor('score', 'desc');

      const view = state.view(rows);

      expect(view.map(r => r.id)).toEqual(['#2', '#3', '#1']);
      expect(rows.map(r => r.id)).toEqual(['#1', '#2', '#3']);
      expect(view).not.toBe(rows as unknown as Row[]);
    });

    it('leaves the rows in source order when the sort column has no accessor', () => {
      const rows = [row('#1', 'a', 'Completed', 10), row('#2', 'b', 'Completed', 90)];
      const state = new TableState<Row>('unregistered', 'desc');

      expect(state.view(rows).map(r => r.id)).toEqual(['#1', '#2']);
    });
  });

  describe('sorting', () => {
    it('sorts null and undefined keys last in both directions', () => {
      const rows = [
        row('#1', 'a', 'Completed', null),
        row('#2', 'b', 'Completed', 40),
        row('#3', 'c', 'Completed', null),
        row('#4', 'd', 'Completed', 70)
      ];
      const state = stateFor('score', 'desc');

      expect(state.view(rows).map(r => r.id)).toEqual(['#4', '#2', '#1', '#3']);

      state.toggleSort('score');
      expect(state.sortDirection).toBe('asc');
      expect(state.view(rows).map(r => r.id)).toEqual(['#2', '#4', '#1', '#3']);
    });

    it('collates digits numerically, so #9 ranks below #10', () => {
      const rows = [row('#10', 'a', 'Completed', 1), row('#9', 'b', 'Completed', 2)];
      const state = stateFor('id', 'asc');

      expect(state.view(rows).map(r => r.id)).toEqual(['#9', '#10']);

      state.toggleSort('id');
      expect(state.sortDirection).toBe('desc');
      expect(state.view(rows).map(r => r.id)).toEqual(['#10', '#9']);
    });

    it('keeps equal keys in source order in both directions', () => {
      const rows = [
        row('#1', 'a', 'Completed', 50),
        row('#2', 'b', 'Completed', 50),
        row('#3', 'c', 'Completed', 50),
        row('#4', 'd', 'Completed', 50)
      ];
      const state = stateFor('score', 'desc');

      expect(state.view(rows).map(r => r.id)).toEqual(['#1', '#2', '#3', '#4']);

      state.toggleSort('score');
      expect(state.view(rows).map(r => r.id)).toEqual(['#1', '#2', '#3', '#4']);
    });

    it('sorts a selection column through a numeric accessor', () => {
      const rows = [
        row('#1', 'a', 'Completed', 10, 0, false),
        row('#2', 'b', 'Completed', 20, 0, true),
        row('#3', 'c', 'Completed', 30, 0, false),
        row('#4', 'd', 'Completed', 40, 0, true)
      ];
      const state = stateFor('selected', 'asc');

      expect(state.view(rows).map(r => r.id)).toEqual(['#2', '#4', '#1', '#3']);
    });

    it('sorts an enum by its own order rather than its label', () => {
      // Tier 0 = A, 1 = B, 2 = C. Labels would rank "B (Pool)" before "A (Replicate)".
      const rows = [
        row('#1', 'B (Pool)', 'Completed', 10, 1),
        row('#2', 'C (Advisory)', 'Completed', 20, 2),
        row('#3', 'A (Replicate)', 'Completed', 30, 0)
      ];
      const state = stateFor('tier', 'asc');

      expect(state.view(rows).map(r => r.id)).toEqual(['#3', '#1', '#2']);
    });
  });

  describe('filtering', () => {
    const rows = [
      row('#1', 'Alpha Suite', 'Completed', 10),
      row('#2', 'beta suite', 'Failed', 20),
      row('#3', 'Gamma', 'FailedValidation', 30)
    ];

    it('matches text filters as a case-insensitive substring', () => {
      const state = stateFor();
      state.setFilter('name', 'SUITE');

      expect(state.view(rows).map(r => r.id)).toEqual(['#2', '#1']);
    });

    it('matches an exact filter on the whole value only', () => {
      const state = stateFor();
      state.setFilter('status', 'Failed');

      expect(state.view(rows).map(r => r.id)).toEqual(['#2']);
    });

    it('treats an empty filter value as no filter at all', () => {
      const state = stateFor();
      state.setFilter('status', 'Failed');
      state.setFilter('status', '');

      expect(state.hasActiveFilters).toBeFalse();
      expect(state.filteredCount(rows)).toBe(3);
    });

    it('applies every active filter together', () => {
      const state = stateFor();
      state.setFilter('name', 'suite');
      state.setFilter('status', 'Completed');

      expect(state.view(rows).map(r => r.id)).toEqual(['#1']);
    });

    it('ignores a filter column that has no registered accessor', () => {
      const state = stateFor();
      state.setFilter('unregistered', 'anything');

      expect(state.filteredCount(rows)).toBe(3);
    });

    it('resets to page 1 whenever a filter changes', () => {
      const many = Array.from({ length: 40 }, (_, i) => row(`#${i}`, 'x', 'Completed', i));
      const state = stateFor();
      state.setPage(3, many);
      expect(state.page).toBe(3);

      state.setFilter('name', 'x');

      expect(state.page).toBe(1);
    });

    it('reports active filters and clears them all at once', () => {
      const state = stateFor();
      expect(state.hasActiveFilters).toBeFalse();

      state.setFilter('name', 'suite');
      state.setFilter('status', 'Failed');
      expect(state.hasActiveFilters).toBeTrue();

      state.clearFilters();

      expect(state.hasActiveFilters).toBeFalse();
      expect(state.filters['name']).toBeUndefined();
      expect(state.page).toBe(1);
      expect(state.filteredCount(rows)).toBe(3);
    });

    it('separates "nothing matches" from "nothing recorded"', () => {
      const state = stateFor();
      state.setFilter('name', 'no such suite');

      expect(state.filteredCount(rows)).toBe(0);
      expect(state.noMatches(rows)).toBeTrue();
      expect(state.noMatches([])).toBeFalse();
    });
  });

  describe('paging', () => {
    const many = Array.from({ length: 25 }, (_, i) => row(`#${i}`, 'x', 'Completed', i));

    it('clamps the page when the row set shrinks under it', () => {
      const state = stateFor();
      state.setPage(3, many);
      expect(state.page).toBe(3);

      const fewer = many.slice(0, 5);

      expect(state.view(fewer).length).toBe(5);
      expect(state.page).toBe(1);
    });

    it('clamps the page when a filter shrinks the row set', () => {
      const state = stateFor();
      state.pageSize = 10;
      state.setPage(3, many);

      state.filters['status'] = 'Failed';

      expect(state.totalPages(many)).toBe(1);
      expect(state.page).toBe(1);
    });

    it('keeps the first visible row on screen when the page size changes', () => {
      const state = stateFor();
      state.setPage(3, many);
      expect(state.page).toBe(3);

      state.setPageSize(20);

      // Row 20 was at the top of page 3 of 10; it is on page 2 of 20.
      expect(state.pageSize).toBe(20);
      expect(state.page).toBe(2);
      expect(state.view(many).length).toBe(5);
    });

    it('never leaves the page past the end after a page-size change', () => {
      const state = stateFor();
      state.setPageSize(100);
      state.setPage(9, many);

      expect(state.page).toBe(1);
      expect(state.view(many).length).toBe(25);
    });

    it('reports the range of the current page', () => {
      const state = stateFor('score', 'asc');
      state.pageSize = 10;
      state.setPage(2, many);

      expect(state.rangeStart(many)).toBe(11);
      expect(state.rangeEnd(many)).toBe(20);
      expect(state.rangeStart([])).toBe(0);
      expect(state.rangeEnd([])).toBe(0);
    });

    it('reports whether the ends are reachable', () => {
      const state = stateFor();
      state.pageSize = 10;

      expect(state.hasPrevious(many)).toBeFalse();
      expect(state.hasNext(many)).toBeTrue();

      state.setPage(3, many);

      expect(state.hasPrevious(many)).toBeTrue();
      expect(state.hasNext(many)).toBeFalse();
    });

    it('refuses a page outside the range', () => {
      const state = stateFor();
      state.pageSize = 10;

      state.setPage(99, many);
      expect(state.page).toBe(3);

      state.setPage(-4, many);
      expect(state.page).toBe(1);
    });
  });

  describe('pageNumbers', () => {
    function pagesOf(count: number, pageSize: number, page: number): (number | '…')[] {
      const rows = Array.from({ length: count }, (_, i) => row(`#${i}`, 'x', 'Completed', i));
      const state = stateFor();
      state.pageSize = pageSize;
      state.setPage(page, rows);
      return state.pageNumbers(rows);
    }

    it('lists every page when they all fit', () => {
      expect(pagesOf(70, 10, 1)).toEqual([1, 2, 3, 4, 5, 6, 7]);
      expect(pagesOf(5, 10, 1)).toEqual([1]);
    });

    it('elides on the right near the start', () => {
      expect(pagesOf(200, 10, 2)).toEqual([1, 2, 3, 4, 5, PAGE_ELLIPSIS, 20]);
    });

    it('elides on the left near the end', () => {
      expect(pagesOf(200, 10, 19)).toEqual([1, PAGE_ELLIPSIS, 16, 17, 18, 19, 20]);
    });

    it('elides on both sides in the middle', () => {
      expect(pagesOf(200, 10, 10))
        .toEqual([1, PAGE_ELLIPSIS, 9, 10, 11, PAGE_ELLIPSIS, 20]);
    });

    it('stays within MAX_PAGE_SLOTS and never places two ellipses side by side, at any total or page', () => {
      const totals = [
        ...Array.from({ length: 60 }, (_, i) => i + 1),
        99, 100, 101, 999, 1000, 5000, 10000
      ];
      const pageSize = 10;

      for (const total of totals) {
        const rows = Array.from({ length: total * pageSize }, (_, i) => row(`#${i}`, 'x', 'Completed', i));
        // setPage clamps, so a candidate outside 1..total (e.g. total - 2 when total is small)
        // is exercised as its clamped value rather than skipped.
        const candidates = [1, 2, 3, Math.ceil(total / 2), total - 2, total - 1, total];

        for (const candidate of candidates) {
          const state = stateFor();
          state.pageSize = pageSize;
          state.setPage(candidate, rows);
          const current = state.page;
          const slots = state.pageNumbers(rows);
          const context = `total=${total} candidate=${candidate} current=${current}`;

          expect(slots.length).withContext(context).toBeLessThanOrEqual(MAX_PAGE_SLOTS);
          expect(slots).withContext(context).toContain(1);
          expect(slots).withContext(context).toContain(total);
          expect(slots).withContext(context).toContain(current);

          for (let i = 1; i < slots.length; i++) {
            expect(slots[i] === PAGE_ELLIPSIS && slots[i - 1] === PAGE_ELLIPSIS)
              .withContext(context).toBeFalse();
          }
        }
      }
    });
  });

  describe('toggleSort', () => {
    it('flips the direction of the column already sorted', () => {
      const state = stateFor('score', 'desc');

      state.toggleSort('score');
      expect(state.sortColumn).toBe('score');
      expect(state.sortDirection).toBe('asc');

      state.toggleSort('score');
      expect(state.sortDirection).toBe('desc');
    });

    it('starts a newly chosen column descending', () => {
      const state = stateFor('score', 'asc');

      state.toggleSort('name');

      expect(state.sortColumn).toBe('name');
      expect(state.sortDirection).toBe('desc');
    });

    it('returns to page 1', () => {
      const many = Array.from({ length: 40 }, (_, i) => row(`#${i}`, 'x', 'Completed', i));
      const state = stateFor();
      state.setPage(3, many);

      state.toggleSort('name');

      expect(state.page).toBe(1);
    });
  });

  describe('remote mode', () => {
    /** One server page of rows: ids in the order the server sent them. */
    const pageRows = [
      row('#9', 'b', 'Failed', 10),
      row('#8', 'a', 'Completed', 90),
      row('#7', 'c', 'Completed', 50)
    ];

    it('returns the rows untouched, ignoring sort and filters', () => {
      const state = stateFor('score', 'desc');
      state.setFilter('status', 'Failed');
      state.setRemoteTotal(47);

      expect(state.view(pageRows).map(r => r.id)).toEqual(['#9', '#8', '#7']);
      expect(state.viewAll(pageRows).map(r => r.id)).toEqual(['#9', '#8', '#7']);
      expect(state.hasActiveFilters).toBeFalse();
      expect(state.noMatches(pageRows)).toBeFalse();
    });

    it('derives the page arithmetic from the remote total', () => {
      const state = stateFor();
      state.setRemoteTotal(47);
      state.setPage(2, pageRows);

      expect(state.filteredCount(pageRows)).toBe(47);
      expect(state.totalPages(pageRows)).toBe(5);
      expect(state.rangeStart(pageRows)).toBe(11);
      expect(state.rangeEnd(pageRows)).toBe(20);
      expect(state.pageNumbers(pageRows)).toEqual([1, 2, 3, 4, 5]);
      expect(state.hasPrevious(pageRows)).toBeTrue();
      expect(state.hasNext(pageRows)).toBeTrue();

      state.setPage(5, pageRows);
      expect(state.rangeEnd(pageRows)).toBe(47);
      expect(state.hasNext(pageRows)).toBeFalse();
    });

    it('clamps the page when the remote total shrinks', () => {
      const state = stateFor();
      state.setRemoteTotal(47);
      state.setPage(5, pageRows);

      state.setRemoteTotal(12);

      expect(state.totalPages(pageRows)).toBe(2);
      expect(state.page).toBe(2);
    });

    it('treats a negative total as zero', () => {
      const state = stateFor();
      state.setRemoteTotal(-3);

      expect(state.remoteTotal).toBe(0);
      expect(state.rangeStart(pageRows)).toBe(0);
    });

    it('returns to local behaviour when the remote total is cleared', () => {
      const state = stateFor('score', 'desc');
      state.setRemoteTotal(47);
      state.setRemoteTotal(null);

      expect(state.filteredCount(pageRows)).toBe(3);
      expect(state.view(pageRows).map(r => r.id)).toEqual(['#8', '#7', '#9']);
    });
  });

  describe('page-size option', () => {
    it('honours a custom page-size list and starts at its first entry', () => {
      const state = new TableState<Row>('score', 'desc', { pageSizes: [25, 250] });

      expect(state.pageSizes).toEqual([25, 250]);
      expect(state.pageSize).toBe(25);
    });
  });

  describe('ariaSort', () => {
    it('names the direction of the sorted column and nothing else', () => {
      const state = stateFor('score', 'desc');

      expect(state.ariaSort('score')).toBe('descending');
      expect(state.ariaSort('name')).toBeNull();

      state.toggleSort('score');
      expect(state.ariaSort('score')).toBe('ascending');

      state.toggleSort('name');
      expect(state.ariaSort('score')).toBeNull();
      expect(state.ariaSort('name')).toBe('descending');
    });
  });
});
