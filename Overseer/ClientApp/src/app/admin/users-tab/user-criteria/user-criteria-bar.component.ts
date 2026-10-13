import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
  inject
} from '@angular/core';

import { AdminUserCriteria, AdminUserFacetCounts, AdminUserSort, AdminUserStatus } from '../../../services/admin-users.service';
import { CardListChip, CardListState } from '../../../shared/data-table/card-list-state';
import { FilterFacetComponent, FilterFacetOption } from '../../../shared/data-table/filter-facet.component';
import { TableState, anyOfFilter, customFilter } from '../../../shared/data-table/table-state';
import {
  DATA_OPTIONS,
  DEFAULT_USER_SORT,
  EMAIL_OPTIONS,
  LAST_SIGN_IN_OPTIONS,
  REGISTERED_OPTIONS,
  USERS_VIEW_STORAGE_KEY,
  USER_SORTS,
  UserFacetOptions,
  applyDataValues,
  applyRegisteredOption,
  criteriaKey,
  dataValuesOf,
  lastSignInOptionOf,
  optionLabel,
  registeredOptionOf,
  resolveDataSelection,
  statusValuesOf,
  userFacetOptions
} from './user-criteria';
import { statusLabel } from '../user-status/admin-user-status';

/** The bar holds no rows: the server filters, and the chips follow the selections alone. */
const NO_ROWS: readonly AdminUserCriteria[] = [];

const NO_VALUES: readonly string[] = [];

/** The facet columns, in the bar's order. */
type UserFacetColumn = 'status' | 'email' | 'registered' | 'signin' | 'data';

interface UserFacetDef {
  column: UserFacetColumn;
  label: string;
  mode: 'multiple' | 'single';
  anyLabel: string;
}

/**
 * The user criteria as a filter bar: the search (user name or email), the Status, Email, Registered,
 * Last sign-in and Data facets with the server's counts, Sort by, and the chips of the active filters
 * with Clear all. The user list and the Bulk Actions wizard's step 1 both render it, so the two cannot
 * drift.
 *
 * Controlled: the host owns `criteria` and `sort` and feeds back what `criteriaChange` and `sortChange`
 * emit. Every id derives from `idPrefix`, which must be unique in the document. Escape in the search
 * clears it without closing a surrounding dialog.
 */
@Component({
  selector: 'app-user-criteria-bar',
  standalone: true,
  imports: [FilterFacetComponent],
  templateUrl: './user-criteria-bar.component.html',
  styleUrl: './user-criteria-bar.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class UserCriteriaBarComponent implements OnChanges, OnDestroy {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Unique in the document: `{idPrefix}-search`, `{idPrefix}-sort`, `{idPrefix}-facet-{column}`. */
  @Input({ required: true }) idPrefix!: string;
  @Input() criteria: AdminUserCriteria = {};
  /** The server's counts under the search; 0 everywhere until counted. */
  @Input() facetCounts: AdminUserFacetCounts | null = null;
  @Input() sort: AdminUserSort = DEFAULT_USER_SORT;
  /** Whether Sort by is shown; the wizard hides it. */
  @Input() showSort = true;

  @Output() readonly criteriaChange = new EventEmitter<AdminUserCriteria>();
  @Output() readonly sortChange = new EventEmitter<AdminUserSort>();

  readonly sorts = USER_SORTS;

  /** The facets, in the bar's order; all are always shown. */
  readonly facetDefs: readonly UserFacetDef[] = [
    { column: 'status', label: 'Status', mode: 'multiple', anyLabel: 'Any' },
    { column: 'email', label: 'Email', mode: 'single', anyLabel: 'Any email' },
    { column: 'registered', label: 'Registered', mode: 'single', anyLabel: 'Any time' },
    { column: 'signin', label: 'Last sign-in', mode: 'single', anyLabel: 'Any time' },
    { column: 'data', label: 'Data', mode: 'multiple', anyLabel: 'Any' }
  ];

  private readonly selectedMemo = new Map<string, { key: string; values: readonly string[] }>();

  /** The filter values the bar shows; the server applies them. */
  readonly table = new TableState<AdminUserCriteria>('newest', 'desc').registerAccessors(
    {},
    {
      search: customFilter<AdminUserCriteria>(() => true),
      status: anyOfFilter<AdminUserCriteria>(() => null),
      email: customFilter<AdminUserCriteria>(() => true),
      registered: customFilter<AdminUserCriteria>(() => true),
      signin: customFilter<AdminUserCriteria>(() => true),
      data: anyOfFilter<AdminUserCriteria>(() => null)
    }
  );

  /** The search's debounce, the stored Sort by, the facets' selections and the chips. */
  private list: CardListState<AdminUserCriteria> | null = null;

  /** The key of the criteria last applied or emitted, so the host's echo is not applied again. */
  private appliedKey: string | null = null;
  private optionsMemo: { counts: AdminUserFacetCounts | null; options: UserFacetOptions } | null = null;

  /** Built on first use, because `idPrefix` is an input. */
  get state(): CardListState<AdminUserCriteria> {
    if (!this.list) {
      this.list = new CardListState<AdminUserCriteria>(this.table, {
        idPrefix: this.idPrefix,
        sorts: USER_SORTS,
        defaultSort: DEFAULT_USER_SORT,
        storageKey: USERS_VIEW_STORAGE_KEY,
        facets: [
          { column: 'status', label: 'Status', values: () => null, labelOf: value => statusLabel(value as AdminUserStatus) },
          { column: 'data', label: 'Data', values: () => null, labelOf: value => optionLabel(DATA_OPTIONS, value) }
        ],
        singleFacets: [
          { column: 'email', label: 'Email', anyLabel: 'Any email', options: EMAIL_OPTIONS, matches: () => true, listedWhen: () => true },
          { column: 'registered', label: 'Registered', anyLabel: 'Any time', options: REGISTERED_OPTIONS, matches: () => true, listedWhen: () => true },
          { column: 'signin', label: 'Last sign-in', anyLabel: 'Any time', options: LAST_SIGN_IN_OPTIONS, matches: () => true, listedWhen: () => true }
        ],
        facetOrder: ['status', 'email', 'registered', 'signin', 'data'],
        onChange: () => {
          this.emitCriteria();
          this.cdr.markForCheck();
        }
      });
    }
    return this.list;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['criteria']) {
      const key = criteriaKey(this.criteria);
      if (key !== this.appliedKey) {
        this.applyCriteria(this.criteria ?? {});
      }
    }
  }

  ngOnDestroy(): void {
    this.list?.dispose();
  }

  // --- View ---

  get searchId(): string {
    return `${this.idPrefix}-search`;
  }

  get sortId(): string {
    return `${this.idPrefix}-sort`;
  }

  /** A facet's selection as the facet takes it; the same array until it changes. */
  selectedOf(column: string): readonly string[] {
    const values = column === 'status' || column === 'data'
      ? this.table.filterValues(column)
      : [(this.table.filters[column] ?? '').trim()].filter(value => value !== '');
    const key = values.join('\n');
    const memo = this.selectedMemo.get(column);
    if (memo && memo.key === key) {
      return memo.values;
    }
    const stable = values.length === 0 ? NO_VALUES : [...values];
    this.selectedMemo.set(column, { key, values: stable });
    return stable;
  }

  get chips(): CardListChip[] {
    return this.state.chips(NO_ROWS);
  }

  /** A facet's options with the server's counts; the same array until the counts change. */
  optionsOf(column: string): FilterFacetOption[] {
    if (this.optionsMemo?.counts !== this.facetCounts) {
      this.optionsMemo = { counts: this.facetCounts, options: userFacetOptions(this.facetCounts) };
    }
    return this.optionsMemo.options[column as UserFacetColumn] ?? [];
  }

  // --- Events ---

  onSearchInput(event: Event): void {
    this.state.setSearchInput((event.target as HTMLInputElement).value);
  }

  /** Escape with text clears the search at once; with none it is left to the surrounding dialog. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (this.state.clearSearchOnEscape(event)) {
      this.emitCriteria();
    }
  }

  onFacetChange(column: string, values: string[]): void {
    const next = column === 'data' ? resolveDataSelection(this.table.filterValues('data'), values) : values;
    this.state.setFacet(column, next);
    this.emitCriteria();
  }

  onSortChange(event: Event): void {
    const id = (event.target as HTMLSelectElement).value as AdminUserSort;
    if (this.state.setSort(id)) {
      this.sortChange.emit(id);
    }
  }

  /** Removes a chip's filter, then focuses the chip now in its place, else the previous one, else the search. */
  removeChip(chip: CardListChip): void {
    const index = this.state.removeChip(chip, NO_ROWS);
    if (chip.column === this.state.searchColumn) {
      const input = this.element(`#${this.searchId}`) as HTMLInputElement | null;
      if (input) input.value = '';
    }
    this.emitCriteria();
    this.cdr.detectChanges();
    const chips = Array.from(this.host.nativeElement.querySelectorAll<HTMLButtonElement>('.ucb-chips .gh-filter-chip'));
    const target = index >= 0 ? chips[index] ?? chips[index - 1] : undefined;
    (target ?? this.element(`#${this.searchId}`))?.focus();
  }

  /** Clears the search and every facet, then focuses the search. */
  clearAll(): void {
    this.state.clearFilters();
    const input = this.element(`#${this.searchId}`) as HTMLInputElement | null;
    if (input) input.value = '';
    this.emitCriteria();
    this.cdr.detectChanges();
    input?.focus();
  }

  /** Focuses the search field, for a host's Clear all filters. */
  focusSearch(): void {
    this.element(`#${this.searchId}`)?.focus();
  }

  // --- Criteria ---

  /** The criteria the bar's filters say. */
  buildCriteria(): AdminUserCriteria {
    const criteria: AdminUserCriteria = {};
    const search = (this.table.filters['search'] ?? '').trim();
    if (search) criteria.search = search;
    const statuses = this.table.filterValues('status') as readonly AdminUserStatus[];
    if (statuses.length > 0) criteria.statuses = [...statuses];
    const email = (this.table.filters['email'] ?? '').trim();
    if (email === 'yes' || email === 'no') criteria.emailConfirmed = email;
    applyRegisteredOption(criteria, (this.table.filters['registered'] ?? '').trim());
    const signIn = (this.table.filters['signin'] ?? '').trim();
    if (signIn) criteria.lastSignIn = signIn;
    applyDataValues(criteria, this.table.filterValues('data'));
    return criteria;
  }

  private applyCriteria(criteria: AdminUserCriteria): void {
    const state = this.state;
    this.table.clearFilters();
    this.table.setFilter('search', criteria.search?.trim() ?? '');
    state.searchText = criteria.search?.trim() ?? '';
    this.table.setFilterValues('status', statusValuesOf(criteria));
    this.table.setFilter('email', criteria.emailConfirmed ?? '');
    this.table.setFilter('registered', registeredOptionOf(criteria));
    this.table.setFilter('signin', lastSignInOptionOf(criteria));
    this.table.setFilterValues('data', dataValuesOf(criteria));
    state.invalidate();
    this.appliedKey = criteriaKey(criteria);
  }

  private emitCriteria(): void {
    const criteria = this.buildCriteria();
    this.appliedKey = criteriaKey(criteria);
    this.criteriaChange.emit(criteria);
  }

  private element(selector: string): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(selector);
  }
}
