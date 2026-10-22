import {
  ChangeDetectorRef,
  Component,
  ElementRef,
  OnChanges,
  OnDestroy,
  OnInit,
  SimpleChanges,
  ViewChild,
  inject,
  input,
  output,
  signal
} from '@angular/core';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import {
  ADMIN_FEEDBACK_CATEGORIES,
  ADMIN_FEEDBACK_STATUSES,
  ADMIN_FEEDBACK_TRIAGE_STATUSES,
  AdminFeedbackCounts,
  AdminFeedbackIssue,
  AdminFeedbackIssueSort,
  AdminFeedbackIssuesQuery,
  AdminFeedbackOccurrence,
  AdminFeedbackService,
  AdminFeedbackStatus,
  AdminFeedbackSwitch,
  AdminFeedbackSwitchTarget,
  AdminFeedbackSwitches,
  AdminFeedbackTriageStatus
} from '../../../services/admin-feedback.service';
import { adminApiError } from '../../../services/admin-users.service';
import { CardListChip, CardListSort, CardListState } from '../../../shared/data-table/card-list-state';
import { FilterFacetComponent, FilterFacetOption } from '../../../shared/data-table/filter-facet.component';
import { TableState, anyOfFilter, customFilter } from '../../../shared/data-table/table-state';
import { parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { AdminConfirmDialogComponent } from '../../admin-dialogs/admin-confirm-dialog.component';
import { adminQueryList, readAdminQueryList } from '../../admin-url';
import { formatInteger } from '../../benchmark/shared/provider-availability/provider-availability';

/** Where the issue list's Sort by is kept, per browser. */
export const TELEMETRY_FEEDBACK_VIEW_STORAGE_KEY = 'overseer.admin.telemetry.feedback.view';

/** The issues the list fetches at first, and how many more Show more fetches. */
export const TELEMETRY_FEEDBACK_BATCH = 25;

/** The occurrences an issue's disclosure fetches at first, and how many more Show more fetches. */
export const TELEMETRY_FEEDBACK_OCCURRENCE_BATCH = 10;

/** The longest triage note the server keeps. */
export const TELEMETRY_FEEDBACK_NOTE_MAX = 1000;

/** The Status facet's selection while the address names none: the open work. */
export const DEFAULT_FEEDBACK_STATUSES: readonly AdminFeedbackStatus[] = ['new', 'regressed'];

/** The `status` query value for no status filter; an absent parameter means the default. */
export const FEEDBACK_STATUS_QUERY_ALL = 'all';

/** Details longer than this many lines, or characters, start collapsed. */
const DETAILS_COLLAPSED_LINES = 6;
const DETAILS_COLLAPSED_CHARS = 480;

const FEEDBACK_SORTS: readonly CardListSort[] = [
  { id: 'lastSeen', label: 'Last seen', column: 'lastSeen', direction: 'desc' },
  { id: 'firstSeen', label: 'First seen', column: 'firstSeen', direction: 'desc' },
  { id: 'count', label: 'Most occurrences', column: 'count', direction: 'desc' }
];

const STATUS_LABELS: Readonly<Record<AdminFeedbackStatus, string>> = {
  new: 'New',
  acknowledged: 'Acknowledged',
  resolved: 'Resolved',
  regressed: 'Regressed',
  dismissed: 'Dismissed'
};

const CATEGORY_LABELS: Readonly<Record<string, string>> = {
  missing_information: 'Missing information',
  tool_problem: 'Tool problem',
  own_mistake: 'Own mistake'
};

const SUSPECTED_CAUSE_LABELS: Readonly<Record<string, string>> = {
  did_not_verify: 'Did not verify',
  misread_tool_result: 'Misread a tool result',
  training_knowledge_conflict: 'Conflict with training knowledge',
  search_strategy: 'Search strategy',
  corpus_or_tool_limit: 'Corpus or tool limit',
  ambiguous_question: 'Ambiguous question',
  answered_too_quickly: 'Answered too quickly',
  other: 'Other'
};

const NOTICED_VIA_LABELS: Readonly<Record<string, string>> = {
  user_correction: 'The user corrected it',
  tool_result: 'A tool result contradicted it',
  self_review: 'Its own review'
};

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

const NO_VALUES: readonly string[] = [];

/** A value of a fixed label map, or the value itself; never an inherited property. */
function labelFrom(map: Readonly<Record<string, string>>, value: string | null | undefined): string {
  if (!value) return '';
  return Object.hasOwn(map, value) ? map[value] : value;
}

export function feedbackStatusLabel(status: string): string {
  return labelFrom(STATUS_LABELS, status);
}

export function feedbackCategoryLabel(category: string): string {
  return labelFrom(CATEGORY_LABELS, category);
}

export function feedbackCauseLabel(cause: string | null | undefined): string {
  return labelFrom(SUSPECTED_CAUSE_LABELS, cause);
}

/** `21 Oct 2026`, local time; the raw value when it does not parse. */
export function feedbackDateText(utc: string | null | undefined): string {
  if (!utc) return '';
  const date = parseServerUtcDate(utc);
  if (isNaN(date.getTime())) return utc;
  return `${date.getDate()} ${MONTHS[date.getMonth()]} ${date.getFullYear()}`;
}

/** `21 Oct 2026 14:02`, local time; the raw value when it does not parse. */
export function feedbackDateTimeText(utc: string | null | undefined): string {
  if (!utc) return '';
  const date = parseServerUtcDate(utc);
  if (isNaN(date.getTime())) return utc;
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${feedbackDateText(utc)} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** What the Status, Category, Model and search filters hold; the address carries the same. */
interface FeedbackFilterState {
  statuses: readonly string[];
  category: string;
  model: string;
  search: string;
}

/** One issue's occurrences fetched so far. */
interface OccurrenceState {
  rows: AdminFeedbackOccurrence[];
  total: number;
  loading: boolean;
  loaded: boolean;
  error: string | null;
}

/** An issue's triage form as edited; `status` is empty while a regressed issue has no new status chosen. */
interface TriageDraft {
  status: AdminFeedbackTriageStatus | '';
  note: string;
}

/** The last Save or Delete of one issue. */
interface TriageResult {
  message: string;
  error: string | null;
}

/** One row of the switch card: its switch, its label, the name its messages use and a note under its state. */
interface SwitchRow {
  target: AdminFeedbackSwitchTarget;
  label: string;
  name: string;
  note: string | null;
}

const SWITCH_ROWS: readonly SwitchRow[] = [
  { target: 'chat', label: 'Enabled in Overseer chat', name: 'Overseer chat', note: null }
];

/**
 * The AI Telemetry tab's Feedback view: the switch that offers the assistant's `report_feedback` tool in
 * the Overseer chat (GnollBench runs choose it per run in the launcher), and the issues it has reported in the tab's date range
 * as a server-paged card list with a search, Sort by, Status, Category and Model facets kept in the
 * address, each issue with
 * its figures, latest facts, occurrences on demand and a triage form. Report text is model output and
 * is rendered as plain text only.
 */
@Component({
  selector: 'app-telemetry-feedback',
  imports: [FilterFacetComponent, AdminConfirmDialogComponent],
  templateUrl: './telemetry-feedback.component.html',
  styleUrl: './telemetry-feedback.component.scss'
})
export class TelemetryFeedbackComponent implements OnInit, OnChanges, OnDestroy {
  private readonly feedback = inject(AdminFeedbackService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild(AdminConfirmDialogComponent, { static: true }) confirmDialog!: AdminConfirmDialogComponent;

  /** The tab's date range, "YYYY-MM-DD"; empty for no bound. */
  readonly startDate = input<string>('');
  readonly endDate = input<string>('');
  /** Issue counts per status over all dates, which the tab fetches for its sub-tab label. */
  readonly counts = input<AdminFeedbackCounts | null>(null);

  /** An issue was triaged or deleted, so the counts per status may have changed. */
  readonly countsChange = output<void>();

  readonly sorts = FEEDBACK_SORTS;
  readonly triageStatuses = ADMIN_FEEDBACK_TRIAGE_STATUSES;
  readonly noteMax = TELEMETRY_FEEDBACK_NOTE_MAX;
  readonly statusLabel = feedbackStatusLabel;
  readonly categoryLabel = feedbackCategoryLabel;
  readonly causeLabel = feedbackCauseLabel;
  readonly dateText = feedbackDateText;
  readonly dateTimeText = feedbackDateTimeText;
  readonly formatInteger = formatInteger;

  // --- Switch ---

  readonly switchRows = SWITCH_ROWS;
  readonly switches = signal<AdminFeedbackSwitches | null>(null);
  /** Per switch: its request is running, its last result, its last error. */
  readonly switchPending = { chat: signal(false) };
  readonly switchMessage = { chat: signal('') };
  readonly switchError = { chat: signal<string | null>(null) };
  readonly switchLoadError = signal<string | null>(null);

  // --- Issues ---

  /** The issues fetched so far, in the server's order. */
  readonly rows = signal<AdminFeedbackIssue[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  /** Whether the first page of the current query has arrived. */
  readonly loaded = signal(false);
  readonly error = signal<string | null>(null);
  /** The issues in the date range under no filter, fetched only when the filters leave none. */
  readonly rangeTotal = signal<number | null>(null);
  /** Announced in the status line until the next fetch: a deletion. */
  readonly notice = signal('');
  /** The Model facet's values, from every occurrence. */
  readonly models = signal<string[]>([]);
  readonly occurrences = signal<Readonly<Record<number, OccurrenceState>>>({});
  /** The occurrences whose long details are shown in full. */
  readonly expandedDetails = signal<ReadonlySet<number>>(new Set<number>());
  readonly savingId = signal<number | null>(null);
  readonly deletingId = signal<number | null>(null);
  readonly triageResults = signal<Readonly<Record<number, TriageResult>>>({});

  /** Each issue's triage form as edited, by issue id. */
  drafts: Record<number, TriageDraft> = {};

  /** The model ids the Model facet's chip reads its label from; filled in place when the models arrive. */
  private readonly modelFacetOptions: { value: string; label: string }[] = [];

  /** The server filters, sorts and pages; the table carries the filter values and the sort. */
  readonly table = new TableState<AdminFeedbackIssue>('lastSeen', 'desc').registerAccessors(
    {
      lastSeen: i => parseServerUtcDate(i.lastSeenUtc).getTime(),
      firstSeen: i => parseServerUtcDate(i.firstSeenUtc).getTime(),
      count: i => i.occurrenceCount
    },
    {
      search: customFilter<AdminFeedbackIssue>(() => true),
      status: anyOfFilter<AdminFeedbackIssue>(i => i.status),
      category: customFilter<AdminFeedbackIssue>(() => true),
      model: customFilter<AdminFeedbackIssue>(() => true)
    }
  );

  /** The search, Sort by, the facets' selections and the chips; every change refetches the first page. */
  readonly list = new CardListState<AdminFeedbackIssue>(this.table, {
    idPrefix: 'tl-fb',
    batch: TELEMETRY_FEEDBACK_BATCH,
    sorts: FEEDBACK_SORTS,
    defaultSort: 'lastSeen',
    storageKey: TELEMETRY_FEEDBACK_VIEW_STORAGE_KEY,
    facets: [{ column: 'status', label: 'Status', values: i => i.status, order: ADMIN_FEEDBACK_STATUSES, labelOf: feedbackStatusLabel }],
    singleFacets: [
      {
        column: 'category', label: 'Category', anyLabel: 'Any category',
        options: ADMIN_FEEDBACK_CATEGORIES.map(c => ({ value: c, label: feedbackCategoryLabel(c) })),
        matches: (i, value) => i.category === value
      },
      {
        column: 'model', label: 'Model', anyLabel: 'Any model',
        options: this.modelFacetOptions,
        matches: (i, value) => i.modelIds.includes(value)
      }
    ],
    onChange: () => this.onFiltersChanged()
  });

  private switchSub: Subscription | null = null;
  private listSub: Subscription | null = null;
  private probeSub: Subscription | null = null;
  private modelsSub: Subscription | null = null;
  private querySub: Subscription | null = null;
  private readonly occurrenceSubs = new Map<number, Subscription>();
  private initialized = false;

  private statusOptionsMemo: { counts: AdminFeedbackCounts | null; options: FilterFacetOption[] } | null = null;
  private categoryOptionsMemo: { rows: AdminFeedbackIssue[]; options: FilterFacetOption[] } | null = null;
  private modelOptionsMemo: { rows: AdminFeedbackIssue[]; models: string[]; selected: string; options: FilterFacetOption[] } | null = null;
  private singleSelectionMemo: Record<string, { value: string; selected: readonly string[] }> = {};

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.table.setRemoteTotal(0);
    // Emits the current address at once, so the first fetch already carries its filters.
    this.querySub = this.route.queryParamMap.subscribe(query => this.applyQuery(query));
    this.initialized = true;
    this.loadSwitches();
    this.loadModels();
    this.reloadIssues();
  }

  ngOnChanges(changes: SimpleChanges): void {
    const changed = (name: string) => !!changes[name] && !changes[name].firstChange;
    if (this.initialized && (changed('startDate') || changed('endDate'))) {
      this.reloadIssues();
    }
  }

  ngOnDestroy(): void {
    this.list.dispose();
    this.switchSub?.unsubscribe();
    this.listSub?.unsubscribe();
    this.probeSub?.unsubscribe();
    this.modelsSub?.unsubscribe();
    this.querySub?.unsubscribe();
    this.occurrenceSubs.forEach(sub => sub.unsubscribe());
  }

  /** Refetches the switches, the models and the first page of issues; open occurrence lists start again. */
  reload(): void {
    this.loadSwitches();
    this.loadModels();
    this.drafts = {};
    this.triageResults.set({});
    this.resetOccurrences();
    this.reloadIssues();
  }

  // --- Switch ---

  /** The switch's state as the server last sent it; null until loaded. */
  switchOf(target: AdminFeedbackSwitchTarget): AdminFeedbackSwitch | null {
    return this.switches()?.[target] ?? null;
  }

  /** A row's sentence of state; the date and name are the server's. */
  switchSentence(target: AdminFeedbackSwitchTarget): string {
    const state = this.switchOf(target);
    if (!state) return this.switchLoadError() ? '' : 'Loading…';
    const effect = state.enabled
      ? 'The assistant may file reports in ordinary chats.'
      : 'The assistant is not offered the tool.';
    const word = state.enabled ? 'On' : 'Off';
    if (state.seeded) return `${word} by configuration default. ${effect}`;
    const since = state.setAtUtc ? ` since ${feedbackDateTimeText(state.setAtUtc)}` : '';
    const by = state.setByUserName ? ` by ${state.setByUserName}` : '';
    return `${word}${since}${by}. ${effect}`;
  }

  loadSwitches(): void {
    this.switchSub?.unsubscribe();
    this.switchSub = this.feedback.getSwitches().subscribe({
      next: switches => {
        this.switches.set(switches);
        this.switchLoadError.set(null);
      },
      error: err => {
        this.switchLoadError.set('The state of the feedback tool could not be loaded: ' + adminApiError(err).message);
      }
    });
  }

  /**
   * Commits one switch at once; that switch alone is disabled while its request runs and reverts when
   * it fails. A success refetches it.
   */
  onSwitchChange(target: AdminFeedbackSwitchTarget, event: Event): void {
    const checkbox = event.target as HTMLInputElement;
    const enabled = checkbox.checked;
    const pending = this.switchPending[target];
    if (pending() || !this.switchOf(target)) {
      checkbox.checked = !enabled;
      return;
    }
    const name = SWITCH_ROWS.find(row => row.target === target)?.name ?? target;
    pending.set(true);
    this.switchMessage[target].set('');
    this.switchError[target].set(null);
    this.feedback.setSwitch(target, enabled).subscribe({
      next: () => {
        pending.set(false);
        this.switchMessage[target].set(`${name}: the feedback tool is ${enabled ? 'on' : 'off'}.`);
        this.loadSwitches();
      },
      error: err => {
        pending.set(false);
        checkbox.checked = !enabled;
        this.switchError[target].set(
          `The feedback tool could not be turned ${enabled ? 'on' : 'off'} for ${name}: ${adminApiError(err).message}`);
      }
    });
  }

  // --- Filters ---

  get searchTerm(): string {
    return (this.table.filters['search'] ?? '').trim();
  }

  /** The Status selection; one empty array while nothing is selected, so the facet input stays put. */
  get selectedStatuses(): readonly string[] {
    const selected = this.table.filterValues('status');
    return selected.length > 0 ? selected : NO_VALUES;
  }

  get selectedCategory(): readonly string[] {
    return this.singleSelection('category');
  }

  get selectedModel(): readonly string[] {
    return this.singleSelection('model');
  }

  get hasFilters(): boolean {
    return this.selectedStatuses.length > 0 || this.selectedCategory.length > 0 || this.selectedModel.length > 0 || this.searchTerm !== '';
  }

  /** Every status, with the number of issues in it over all dates. */
  get statusOptions(): FilterFacetOption[] {
    const counts = this.counts();
    if (this.statusOptionsMemo && this.statusOptionsMemo.counts === counts) {
      return this.statusOptionsMemo.options;
    }
    const options = ADMIN_FEEDBACK_STATUSES.map(status => ({ value: status, label: feedbackStatusLabel(status), count: counts?.[status] ?? 0 }));
    this.statusOptionsMemo = { counts, options };
    return options;
  }

  /** Every category, with the number of the issues loaded in it. */
  get categoryOptions(): FilterFacetOption[] {
    const rows = this.rows();
    if (this.categoryOptionsMemo && this.categoryOptionsMemo.rows === rows) {
      return this.categoryOptionsMemo.options;
    }
    const options = ADMIN_FEEDBACK_CATEGORIES.map(category => ({
      value: category, label: feedbackCategoryLabel(category), count: rows.filter(r => r.category === category).length
    }));
    this.categoryOptionsMemo = { rows, options };
    return options;
  }

  /** Every model with a report, and the selected one, with the number of the issues loaded that it reported. */
  get modelOptions(): FilterFacetOption[] {
    const rows = this.rows();
    const models = this.models();
    const selected = this.selectedModel[0] ?? '';
    const memo = this.modelOptionsMemo;
    if (memo && memo.rows === rows && memo.models === models && memo.selected === selected) {
      return memo.options;
    }
    const values = selected && !models.includes(selected) ? [...models, selected] : models;
    const options = values.map(model => ({ value: model, label: model, count: rows.filter(r => r.modelIds.includes(model)).length }));
    this.modelOptionsMemo = { rows, models, selected, options };
    return options;
  }

  /** The Model facet is listed with two models or more, or while it has a selection. */
  get showModelFacet(): boolean {
    return this.models().length > 1 || this.selectedModel.length > 0;
  }

  get chips(): CardListChip[] {
    return this.list.chips(this.rows());
  }

  onSearchInput(event: Event): void {
    this.list.setSearchInput((event.target as HTMLInputElement).value);
  }

  /** Escape with text clears the search at once and refetches; with none, Escape is left alone. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (this.list.clearSearchOnEscape(event)) this.onFiltersChanged();
  }

  onSortChange(event: Event): void {
    if (this.list.setSort((event.target as HTMLSelectElement).value)) this.reloadIssues();
  }

  onStatusChange(values: string[]): void {
    this.list.setFacet('status', values);
    this.onFiltersChanged();
  }

  onCategoryChange(values: string[]): void {
    this.list.setFacet('category', values);
    this.onFiltersChanged();
  }

  onModelChange(values: string[]): void {
    this.list.setFacet('model', values);
    this.onFiltersChanged();
  }

  /** Removes a chip's filter and refetches, then focuses the chip now in its place, else the previous one, else the search. */
  removeChip(chip: CardListChip): void {
    const index = this.list.removeChip(chip, this.rows());
    if (chip.column === 'search') this.setSearchField('');
    this.onFiltersChanged();
    this.cdr.detectChanges();
    const chips = Array.from(this.host.nativeElement.querySelectorAll<HTMLButtonElement>('.tl-fb-chips .gh-filter-chip'));
    const target = index >= 0 ? chips[index] ?? chips[index - 1] : undefined;
    (target ?? this.element('#tl-fb-search'))?.focus();
  }

  /** Clears the search and every facet, the Status default included, refetches, then focuses the search. */
  clearFilters(): void {
    this.list.clearFilters();
    this.setSearchField('');
    this.onFiltersChanged();
    this.cdr.detectChanges();
    this.element('#tl-fb-search')?.focus();
  }

  /** The address took new filters, or a filter changed here: keeps the two in step and refetches. */
  private onFiltersChanged(): void {
    this.writeQuery();
    this.reloadIssues();
    this.cdr.markForCheck();
  }

  private singleSelection(column: string): readonly string[] {
    const value = this.table.filters[column] ?? '';
    const memo = this.singleSelectionMemo[column];
    if (memo && memo.value === value) return memo.selected;
    const selected = value ? [value] : NO_VALUES;
    this.singleSelectionMemo[column] = { value, selected };
    return selected;
  }

  private setSearchField(text: string): void {
    const field = this.element('#tl-fb-search') as HTMLInputElement | null;
    if (field) field.value = text;
  }

  // --- The address ---

  private filterState(): FeedbackFilterState {
    return {
      statuses: this.selectedStatuses,
      category: this.selectedCategory[0] ?? '',
      model: this.selectedModel[0] ?? '',
      search: this.searchTerm
    };
  }

  /** The filters an address names: an absent `status` is the default, `status=all` none; unknown values are dropped. */
  private static filtersFromQuery(query: ParamMap): FeedbackFilterState {
    const listed = readAdminQueryList(query, 'status');
    const statuses = listed === null
      ? [...DEFAULT_FEEDBACK_STATUSES]
      : listed.includes(FEEDBACK_STATUS_QUERY_ALL)
        ? []
        : ADMIN_FEEDBACK_STATUSES.filter(status => listed.includes(status));
    const category = query.get('category') ?? '';
    return {
      statuses,
      category: (ADMIN_FEEDBACK_CATEGORIES as readonly string[]).includes(category) ? category : '',
      model: query.get('model')?.trim() ?? '',
      search: query.get('q')?.trim() ?? ''
    };
  }

  private static sameFilters(a: FeedbackFilterState, b: FeedbackFilterState): boolean {
    const set = (values: readonly string[]) => [...values].sort().join(',');
    return set(a.statuses) === set(b.statuses) && a.category === b.category && a.model === b.model && a.search === b.search;
  }

  /** Takes the filters an address names; after the first load, a change refetches. */
  private applyQuery(query: ParamMap): void {
    const next = TelemetryFeedbackComponent.filtersFromQuery(query);
    if (this.initialized && TelemetryFeedbackComponent.sameFilters(next, this.filterState())) {
      return;
    }
    this.table.setFilterValues('status', next.statuses);
    this.table.setFilter('category', next.category);
    this.table.setFilter('model', next.model);
    this.table.setFilter('search', next.search);
    this.list.searchText = next.search;
    this.setSearchField(next.search);
    this.list.resetBatch();
    this.list.invalidate();
    if (this.initialized) {
      this.reloadIssues();
      this.cdr.markForCheck();
    }
  }

  /** Writes the filters into the address, replacing the history entry and keeping every other parameter. */
  private writeQuery(): void {
    const state = this.filterState();
    const isDefault = state.statuses.length === DEFAULT_FEEDBACK_STATUSES.length &&
      DEFAULT_FEEDBACK_STATUSES.every(status => state.statuses.includes(status));
    const ordered = ADMIN_FEEDBACK_STATUSES.filter(status => state.statuses.includes(status));
    const status = isDefault ? null : ordered.length === 0 ? FEEDBACK_STATUS_QUERY_ALL : adminQueryList(ordered);
    // A navigation the router fails is reported by its own NavigationError event.
    this.router.navigate([], {
      queryParams: { status, category: state.category || null, model: state.model || null, q: state.search || null },
      queryParamsHandling: 'merge',
      replaceUrl: true
    }).catch(() => false);
  }

  // --- Fetching ---

  get remainingCount(): number {
    return Math.max(0, this.totalCount() - this.rows().length);
  }

  get nextBatchCount(): number {
    return Math.min(this.list.batch, this.remainingCount);
  }

  /** `Showing 25 of 40 issues`, `One issue`, or empty with none; a deletion is announced before it. */
  get listStatus(): string {
    if (this.loading() && !this.loaded()) return 'Loading feedback issues…';
    if (this.error()) return '';
    const total = this.totalCount();
    const base = total === 0 ? '' : total === 1 && this.rows().length === 1
      ? 'One issue'
      : `Showing ${formatInteger(this.rows().length)} of ${formatInteger(total)} issues`;
    const notice = this.notice();
    return notice ? `${notice} ${base}`.trim() : base;
  }

  /** Which empty state applies once the first page is empty; null while the range total is still being counted. */
  get emptyKind(): 'range' | 'filters' | null {
    if (!this.hasFilters) return 'range';
    const total = this.rangeTotal();
    if (total === null) return null;
    return total === 0 ? 'range' : 'filters';
  }

  /** Fetches the first page of the current query, replacing the list. */
  reloadIssues(): void {
    this.fetch(false);
  }

  /** Appends the next page and focuses the title of the first new card. */
  showMore(): void {
    if (this.loading() || this.remainingCount === 0) return;
    this.fetch(true);
  }

  private currentQuery(): AdminFeedbackIssuesQuery {
    const state = this.filterState();
    return {
      startDate: this.startDate() || undefined,
      endDate: this.endDate() || undefined,
      statuses: state.statuses.length > 0 ? [...state.statuses] : undefined,
      category: state.category || undefined,
      modelId: state.model || undefined,
      search: state.search || undefined,
      sort: this.list.sortId as AdminFeedbackIssueSort
    };
  }

  private fetch(append: boolean): void {
    const query: AdminFeedbackIssuesQuery = {
      ...this.currentQuery(),
      skip: append ? this.rows().length : 0,
      take: TELEMETRY_FEEDBACK_BATCH
    };
    if (!append) {
      this.loaded.set(false);
      this.rangeTotal.set(null);
      this.probeSub?.unsubscribe();
    }
    this.loading.set(true);
    this.error.set(null);
    this.notice.set('');
    this.listSub?.unsubscribe();
    this.listSub = this.feedback.getIssues(query).subscribe({
      next: page => {
        const firstNew = append ? this.rows().length : -1;
        if (append) {
          const known = new Set(this.rows().map(i => i.id));
          this.rows.set([...this.rows(), ...page.rows.filter(i => !known.has(i.id))]);
        } else {
          this.rows.set(page.rows);
        }
        for (const issue of page.rows) {
          if (!this.drafts[issue.id]) this.drafts[issue.id] = TelemetryFeedbackComponent.draftOf(issue);
        }
        this.totalCount.set(page.totalCount);
        this.table.setRemoteTotal(page.totalCount);
        this.loading.set(false);
        this.loaded.set(true);
        this.list.invalidate();
        if (!append && page.rows.length === 0 && this.hasFilters) {
          this.countRange();
        }
        const focusId = firstNew >= 0 ? this.rows()[firstNew]?.id : undefined;
        if (focusId !== undefined) {
          this.focusAfterRender(`#tl-fb-${focusId}-title`);
        } else {
          this.cdr.markForCheck();
        }
      },
      error: err => {
        this.loading.set(false);
        this.loaded.set(true);
        this.error.set('The feedback issues could not be loaded: ' + adminApiError(err).message);
      }
    });
  }

  /** Counts the issues in the range under no filter, to tell the two empty states apart. */
  private countRange(): void {
    this.probeSub?.unsubscribe();
    this.probeSub = this.feedback.getIssues({
      startDate: this.startDate() || undefined,
      endDate: this.endDate() || undefined,
      take: 1
    }).subscribe({
      next: page => this.rangeTotal.set(page.totalCount),
      // Unknown: the filters are the likelier cause, and Clear filters is the useful offer.
      error: () => this.rangeTotal.set(1)
    });
  }

  private loadModels(): void {
    this.modelsSub?.unsubscribe();
    this.modelsSub = this.feedback.getModels().subscribe({
      next: models => {
        const sorted = [...models].sort((a, b) => a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' }));
        this.modelFacetOptions.splice(0, this.modelFacetOptions.length, ...sorted.map(m => ({ value: m, label: m })));
        this.models.set(sorted);
        this.list.invalidate();
      },
      // The facet is left out; the list itself still works.
      error: () => this.models.set([])
    });
  }

  // --- A card ---

  /** `Regressed 2×` when an issue has regressed more than once; otherwise the status word. */
  statusBadgeText(issue: AdminFeedbackIssue): string {
    return issue.status === 'regressed' && issue.regressionCount > 1
      ? `Regressed ${issue.regressionCount}×`
      : feedbackStatusLabel(issue.status);
  }

  /** The badge's accessible text: `Regressed 2 times`. */
  statusBadgeSpoken(issue: AdminFeedbackIssue): string {
    return issue.status === 'regressed' && issue.regressionCount > 1
      ? `Regressed ${issue.regressionCount} times`
      : feedbackStatusLabel(issue.status);
  }

  statusBadgeClass(status: AdminFeedbackStatus): string {
    switch (status) {
      case 'regressed': return 'status-badge badge-warning';
      case 'resolved': return 'status-badge badge-success';
      default: return `status-badge tl-fb-status-${status}`;
    }
  }

  /** `9 retained` when fewer occurrences remain than were reported; otherwise empty. */
  retainedText(issue: AdminFeedbackIssue): string {
    return issue.retainedCount < issue.occurrenceCount ? `${formatInteger(issue.retainedCount)} retained` : '';
  }

  /** `Triaged 21 Oct 2026 14:02 by tommi`; empty for an issue nobody has triaged. */
  triagedText(issue: AdminFeedbackIssue): string {
    if (!issue.triagedUtc) return '';
    const by = issue.triagedByUserName ? ` by ${issue.triagedByUserName}` : '';
    return `Triaged ${feedbackDateTimeText(issue.triagedUtc)}${by}`;
  }

  // --- Occurrences ---

  occurrenceState(issueId: number): OccurrenceState | undefined {
    return this.occurrences()[issueId];
  }

  /** The first open fetches the first page; later opens show what was fetched. */
  onOccurrencesToggle(issue: AdminFeedbackIssue, event: Event): void {
    if (!(event.target as HTMLDetailsElement).open) return;
    const state = this.occurrenceState(issue.id);
    if (state && (state.loaded || state.loading)) return;
    this.fetchOccurrences(issue.id, false);
  }

  showMoreOccurrences(issueId: number): void {
    const state = this.occurrenceState(issueId);
    if (!state || state.loading || state.rows.length >= state.total) return;
    this.fetchOccurrences(issueId, true);
  }

  remainingOccurrences(issueId: number): number {
    const state = this.occurrenceState(issueId);
    return state ? Math.max(0, state.total - state.rows.length) : 0;
  }

  nextOccurrenceBatch(issueId: number): number {
    return Math.min(TELEMETRY_FEEDBACK_OCCURRENCE_BATCH, this.remainingOccurrences(issueId));
  }

  private fetchOccurrences(issueId: number, append: boolean): void {
    const previous = this.occurrenceState(issueId);
    const rows = append && previous ? previous.rows : [];
    this.setOccurrenceState(issueId, { rows, total: previous?.total ?? 0, loading: true, loaded: previous?.loaded ?? false, error: null });
    this.occurrenceSubs.get(issueId)?.unsubscribe();
    const sub = this.feedback.getOccurrences(issueId, rows.length, TELEMETRY_FEEDBACK_OCCURRENCE_BATCH).subscribe({
      next: page => {
        const known = new Set(rows.map(o => o.id));
        const merged = [...rows, ...page.rows.filter(o => !known.has(o.id))];
        this.setOccurrenceState(issueId, { rows: merged, total: page.totalCount, loading: false, loaded: true, error: null });
        const firstNew = append ? merged[rows.length] : undefined;
        if (firstNew) {
          this.focusAfterRender(`#tl-fb-occ-${firstNew.id}-time`);
        } else {
          this.cdr.markForCheck();
        }
      },
      error: err => {
        this.setOccurrenceState(issueId, {
          rows, total: previous?.total ?? 0, loading: false, loaded: rows.length > 0,
          error: 'The occurrences could not be loaded: ' + adminApiError(err).message
        });
      }
    });
    this.occurrenceSubs.set(issueId, sub);
  }

  private setOccurrenceState(issueId: number, state: OccurrenceState): void {
    this.occurrences.set({ ...this.occurrences(), [issueId]: state });
  }

  /** Forgets every fetched occurrence list, then fetches the first page again for each disclosure still open. */
  private resetOccurrences(): void {
    this.occurrenceSubs.forEach(sub => sub.unsubscribe());
    this.occurrenceSubs.clear();
    this.occurrences.set({});
    this.expandedDetails.set(new Set<number>());
    const open = Array.from(this.host.nativeElement.querySelectorAll<HTMLDetailsElement>('details.tl-fb-occurrences[open]'));
    for (const details of open) {
      const id = Number(details.dataset['issueId']);
      if (Number.isFinite(id)) this.fetchOccurrences(id, false);
    }
  }

  /** `Anthropic · claude-x`; whichever is known. */
  modelText(occurrence: AdminFeedbackOccurrence): string {
    return [occurrence.provider, occurrence.modelId].filter(part => !!part).join(' · ');
  }

  noticedViaText(value: string | null): string {
    return labelFrom(NOTICED_VIA_LABELS, value);
  }

  /** `Session 812 · message 4410`. */
  idsText(occurrence: AdminFeedbackOccurrence): string {
    return occurrence.turnUserMessageId != null
      ? `Session ${occurrence.chatSessionId} · message ${occurrence.turnUserMessageId}`
      : `Session ${occurrence.chatSessionId}`;
  }

  /** Whether the details are long enough to start collapsed. */
  isLongDetails(details: string | null): boolean {
    if (!details) return false;
    return details.split('\n').length > DETAILS_COLLAPSED_LINES || details.length > DETAILS_COLLAPSED_CHARS;
  }

  isExpanded(occurrenceId: number): boolean {
    return this.expandedDetails().has(occurrenceId);
  }

  toggleDetails(occurrenceId: number): void {
    const next = new Set(this.expandedDetails());
    if (next.has(occurrenceId)) next.delete(occurrenceId);
    else next.add(occurrenceId);
    this.expandedDetails.set(next);
  }

  // --- Triage ---

  draft(issue: AdminFeedbackIssue): TriageDraft {
    return this.drafts[issue.id] ??= TelemetryFeedbackComponent.draftOf(issue);
  }

  triageResult(issueId: number): TriageResult | undefined {
    return this.triageResults()[issueId];
  }

  onDraftStatus(issue: AdminFeedbackIssue, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.draft(issue).status = (ADMIN_FEEDBACK_TRIAGE_STATUSES as readonly string[]).includes(value) ? value as AdminFeedbackTriageStatus : '';
  }

  onDraftNote(issue: AdminFeedbackIssue, event: Event): void {
    this.draft(issue).note = (event.target as HTMLTextAreaElement).value;
  }

  /** Saves the status and the note; the card takes the server's copy, the result is announced and the counts refetched. */
  save(issue: AdminFeedbackIssue): void {
    if (this.savingId() !== null || this.deletingId() !== null) return;
    const draft = this.draft(issue);
    if (!draft.status) {
      this.setTriageResult(issue.id, { message: '', error: 'Choose a status first.' });
      return;
    }
    const status = draft.status;
    const note = draft.note.trim().slice(0, TELEMETRY_FEEDBACK_NOTE_MAX);
    this.savingId.set(issue.id);
    this.setTriageResult(issue.id, { message: '', error: null });
    this.feedback.updateStatus(issue.id, status, note).subscribe({
      next: updated => {
        this.savingId.set(null);
        const saved: AdminFeedbackIssue = updated && updated.id === issue.id
          ? updated
          : { ...issue, status, triageNote: note || null, triagedUtc: new Date().toISOString() };
        this.rows.set(this.rows().map(i => i.id === issue.id ? saved : i));
        this.drafts[issue.id] = TelemetryFeedbackComponent.draftOf(saved);
        this.list.invalidate();
        this.setTriageResult(issue.id, { message: `Saved. The issue is ${feedbackStatusLabel(saved.status)}.`, error: null });
        this.countsChange.emit();
      },
      error: err => {
        this.savingId.set(null);
        this.setTriageResult(issue.id, { message: '', error: 'The issue could not be saved: ' + adminApiError(err).message });
      }
    });
  }

  /** Asks for confirmation, naming the issue and its occurrences, then deletes both. */
  confirmDelete(issue: AdminFeedbackIssue): void {
    if (this.savingId() !== null || this.deletingId() !== null) return;
    const occurrences = issue.retainedCount === 1 ? 'its 1 stored occurrence' : `its ${formatInteger(issue.retainedCount)} stored occurrences`;
    this.confirmDialog.open({
      title: 'Delete issue',
      message: `Delete the issue “${issue.subject}” and ${occurrences}? This cannot be undone.`,
      buttonText: 'Delete Issue',
      buttonClass: 'btn-gh btn-gh-delete'
    }, () => this.deleteIssue(issue));
  }

  private deleteIssue(issue: AdminFeedbackIssue): void {
    this.deletingId.set(issue.id);
    this.setTriageResult(issue.id, { message: '', error: null });
    this.feedback.deleteIssue(issue.id).subscribe({
      next: () => {
        this.deletingId.set(null);
        const before = this.rows();
        const index = before.findIndex(i => i.id === issue.id);
        const after = before.filter(i => i.id !== issue.id);
        this.rows.set(after);
        delete this.drafts[issue.id];
        this.totalCount.set(Math.max(0, this.totalCount() - 1));
        this.table.setRemoteTotal(this.totalCount());
        this.list.invalidate();
        this.notice.set(`Deleted the issue “${issue.subject}”.`);
        this.countsChange.emit();
        const next = after[index] ?? after[index - 1];
        this.focusAfterRender(next ? `#tl-fb-${next.id}-title` : '#tl-fb-issues-title');
      },
      error: err => {
        this.deletingId.set(null);
        this.setTriageResult(issue.id, { message: '', error: 'The issue could not be deleted: ' + adminApiError(err).message });
      }
    });
  }

  private setTriageResult(issueId: number, result: TriageResult): void {
    this.triageResults.set({ ...this.triageResults(), [issueId]: result });
  }

  private static draftOf(issue: AdminFeedbackIssue): TriageDraft {
    return {
      status: issue.status === 'regressed' ? '' : issue.status,
      note: issue.triageNote ?? ''
    };
  }

  // --- Helpers ---

  /** Renders the view now, then focuses an element in it. */
  private focusAfterRender(selector: string): void {
    this.cdr.detectChanges();
    refreshAnchorPositioning();
    this.element(selector)?.focus();
  }

  private element(selector: string): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(selector);
  }
}
