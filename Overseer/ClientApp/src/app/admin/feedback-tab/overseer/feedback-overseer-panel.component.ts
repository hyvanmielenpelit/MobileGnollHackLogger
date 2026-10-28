import {
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
  inject,
  signal
} from '@angular/core';
import { ActivatedRoute, NavigationEnd, ParamMap, Router, RouterLink } from '@angular/router';
import { Observable, Subscription, catchError, defer, filter, finalize, map, tap, throwError } from 'rxjs';
import {
  ADMIN_FEEDBACK_CATEGORIES,
  ADMIN_FEEDBACK_FIXED_IN_VERSION_MAX,
  ADMIN_FEEDBACK_FIXED_IN_VERSION_PATTERN,
  ADMIN_FEEDBACK_IDS_MAX,
  ADMIN_FEEDBACK_MODEL_SOURCES,
  ADMIN_FEEDBACK_NO_GROUP,
  ADMIN_FEEDBACK_NOTE_MAX,
  ADMIN_FEEDBACK_RESOLUTION_FILTERS,
  ADMIN_FEEDBACK_RESOLUTION_MAX,
  ADMIN_FEEDBACK_STATUSES,
  ADMIN_FEEDBACK_TRIAGE_STATUSES,
  AdminFeedbackCounts,
  AdminFeedbackFacetEntity,
  AdminFeedbackFacetValue,
  AdminFeedbackFacets,
  AdminFeedbackIssue,
  AdminFeedbackIssueFilters,
  AdminFeedbackIssueSort,
  AdminFeedbackIssuesQuery,
  AdminFeedbackOccurrence,
  AdminFeedbackResolutionFilter,
  AdminFeedbackService,
  AdminFeedbackStatus,
  AdminFeedbackTriageChanges,
  AdminFeedbackTriageStatus
} from '../../../services/admin-feedback.service';
import { adminApiError } from '../../../services/admin-users.service';
import { FeedbackTransferService, feedbackTransferStatus } from '../../../services/feedback-transfer.service';
import { CardListChip, CardListFacetSpec, CardListSort, CardListState } from '../../../shared/data-table/card-list-state';
import { FilterFacetComponent, FilterFacetOption } from '../../../shared/data-table/filter-facet.component';
import { TableState, anyOfFilter, customFilter } from '../../../shared/data-table/table-state';
import { parseServerUtcDate } from '../../../utils/date.util';
import { providerLabel, toolLabel } from '../../../utils/display-labels.util';
import { downloadTextFile } from '../../../utils/download.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { AdminConfirmDialogComponent } from '../../admin-dialogs/admin-confirm-dialog.component';
import { AdminPageStore } from '../../admin-page.store';
import { ADMIN_TAB_SLUGS, adminCommands, adminQueryList, clearedFeedbackQueryParams, readAdminQueryList } from '../../admin-url';
import { formatInteger } from '../../benchmark/shared/provider-availability/provider-availability';
import { DEFAULT_FEEDBACK_EXPORT_OPTIONS } from '../shared/feedback-export-dialog.component';
import {
  feedbackCategoryLabel,
  feedbackCauseLabel,
  feedbackNoticedViaLabel,
  feedbackStatusLabel,
  formatResolvedVia,
  labelFrom
} from '../shared/feedback-labels';
import { FeedbackSelectionBarComponent, FeedbackSelectionId } from '../shared/feedback-selection-bar.component';
import { hiddenCharactersOf } from '../shared/feedback-hidden-characters';
import { FeedbackTriageDialogComponent, FeedbackTriageSubmission, FeedbackTriageValues } from '../shared/feedback-triage-dialog.component';
import {
  FEEDBACK_SELECTION_CAP,
  FeedbackExportOptions,
  FeedbackRange,
  FeedbackViewRecord,
  defaultFeedbackRange,
  loadFeedbackView,
  loadOverseerSelection,
  saveFeedbackView,
  saveOverseerSelection
} from '../shared/feedback-view-storage';

/** The issues the list fetches at first, and how many more Show more fetches. */
export const FEEDBACK_BATCH = 25;

/** The occurrences an issue's disclosure fetches at first, and how many more Show more fetches. */
export const FEEDBACK_OCCURRENCE_BATCH = 10;

/** The longest triage note, resolution and fixed-in version the server keeps. */
export const FEEDBACK_NOTE_MAX = ADMIN_FEEDBACK_NOTE_MAX;
export const FEEDBACK_RESOLUTION_MAX = ADMIN_FEEDBACK_RESOLUTION_MAX;
export const FEEDBACK_FIXED_IN_VERSION_MAX = ADMIN_FEEDBACK_FIXED_IN_VERSION_MAX;

/** The Status facet's selection while the address and the remembered view name none: the open work. */
export const DEFAULT_FEEDBACK_STATUSES: readonly AdminFeedbackStatus[] = ['new', 'regressed'];

/** The `status` query value for no status filter; an absent parameter means the default. */
export const FEEDBACK_STATUS_QUERY_ALL = 'all';

/** Details longer than this many lines, or characters, start collapsed. */
const DETAILS_COLLAPSED_LINES = 6;
const DETAILS_COLLAPSED_CHARS = 480;

/** The longest search the server takes. */
const SEARCH_MAX = 200;

const FEEDBACK_SORTS: readonly CardListSort[] = [
  { id: 'lastSeen', label: 'Last seen', column: 'lastSeen', direction: 'desc' },
  { id: 'firstSeen', label: 'First seen', column: 'firstSeen', direction: 'desc' },
  { id: 'count', label: 'Most occurrences', column: 'count', direction: 'desc' },
  { id: 'users', label: 'Most users', column: 'users', direction: 'desc' },
  { id: 'regressions', label: 'Most regressions', column: 'regressions', direction: 'desc' },
  { id: 'triaged', label: 'Recently triaged', column: 'triaged', direction: 'desc' },
  { id: 'subject', label: 'Subject (A–Z)', column: 'subject', direction: 'asc' }
];

const DEFAULT_SORT: AdminFeedbackIssueSort = 'lastSeen';

const MODEL_SOURCE_LABELS: Readonly<Record<string, string>> = {
  system: 'System configuration',
  user: 'User\'s own model',
  unknown: 'Unknown'
};

const RESOLUTION_FILTER_LABELS: Readonly<Record<AdminFeedbackResolutionFilter, string>> = {
  with: 'With a resolution',
  without: 'Without a resolution'
};

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

const NO_VALUES: readonly string[] = [];

/** `21 Oct 2026`, local time; the raw value when it does not parse. */
export function feedbackDateText(utc: string | null | undefined): string {
  if (!utc) return '';
  const date = parseServerUtcDate(utc);
  if (isNaN(date.getTime())) return utc;
  return `${date.getDate()} ${MONTHS[date.getMonth()]} ${date.getFullYear()}`;
}

/** `14:02`, local time; empty when the value does not parse. */
export function feedbackTimeText(utc: string | null | undefined): string {
  if (!utc) return '';
  const date = parseServerUtcDate(utc);
  if (isNaN(date.getTime())) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** `21 Oct 2026 14:02`, local time; the raw value when it does not parse. */
export function feedbackDateTimeText(utc: string | null | undefined): string {
  if (!utc) return '';
  const date = parseServerUtcDate(utc);
  if (isNaN(date.getTime())) return utc;
  return `${feedbackDateText(utc)} ${feedbackTimeText(utc)}`;
}

/** `1.4.1+d4452568e4` for `1.4.1+d4452568…`: build metadata of 12 characters or more cut to its first 10. */
export function feedbackVersionShort(version: string | null | undefined): string {
  if (!version) return '';
  const plus = version.indexOf('+');
  if (plus < 0) return version;
  const metadata = version.slice(plus + 1);
  return metadata.length >= 12 ? `${version.slice(0, plus)}+${metadata.slice(0, 10)}` : version;
}

/**
 * Where an occurrence's model came from: `System configuration Claude (fast)`, `User's own model`
 * or `Unknown`; a configuration without a name is named by its id.
 */
export function feedbackModelSourceText(occurrence: Pick<AdminFeedbackOccurrence, 'modelSource' | 'systemConfigurationId' | 'systemConfigurationName'>): string {
  if (occurrence.modelSource === 'user') return MODEL_SOURCE_LABELS['user'];
  if (occurrence.modelSource === 'system') {
    const name = occurrence.systemConfigurationName
      ?? (occurrence.systemConfigurationId != null ? `#${occurrence.systemConfigurationId}` : '');
    return name ? `${MODEL_SOURCE_LABELS['system']} ${name}` : MODEL_SOURCE_LABELS['system'];
  }
  return MODEL_SOURCE_LABELS['unknown'];
}

/** The facets of the filter bar, in the order it lists them; each is a column of the table and a query parameter. */
type FacetColumn = 'status' | 'category' | 'model' | 'provider' | 'user' | 'group' | 'source' | 'config' | 'version' | 'tool' | 'resolution' | 'ids';

interface FacetDefinition {
  column: FacetColumn;
  label: string;
  /** A single-select facet takes one value; its first option clears it. */
  mode: 'multiple' | 'single';
  anyLabel?: string;
  /** Listed whatever the options; the others are listed with two options or more, or while they have a selection. */
  alwaysListed?: boolean;
  /** Matched against the occurrences rather than the issue, so it also narrows the occurrences shown. */
  occurrenceLevel?: boolean;
  /** Set by the address alone (a link from the GnollBench sub-tab); shown as chips, never as a facet. */
  addressOnly?: boolean;
}

const FACETS: readonly FacetDefinition[] = [
  { column: 'status', label: 'Status', mode: 'multiple', alwaysListed: true },
  { column: 'category', label: 'Category', mode: 'multiple', alwaysListed: true },
  { column: 'model', label: 'Model', mode: 'multiple', occurrenceLevel: true },
  { column: 'provider', label: 'Provider', mode: 'multiple', occurrenceLevel: true },
  { column: 'user', label: 'User', mode: 'multiple', occurrenceLevel: true },
  { column: 'group', label: 'Group', mode: 'multiple', occurrenceLevel: true },
  { column: 'source', label: 'Model source', mode: 'multiple', occurrenceLevel: true },
  { column: 'config', label: 'Configuration', mode: 'multiple', occurrenceLevel: true },
  { column: 'version', label: 'Version', mode: 'multiple', occurrenceLevel: true },
  { column: 'tool', label: 'Tool', mode: 'multiple', occurrenceLevel: true },
  { column: 'resolution', label: 'Has resolution', mode: 'single', anyLabel: 'Any', alwaysListed: true },
  { column: 'ids', label: 'Issue', mode: 'multiple', addressOnly: true }
];

const FACET_COLUMNS: readonly FacetColumn[] = FACETS.map(f => f.column);

/** The search's query parameter; each facet's is its column. */
const SEARCH_PARAM = 'q';

/** The query parameters this sub-tab reads its filters from. */
const FILTER_PARAMS: readonly string[] = [...FACET_COLUMNS, SEARCH_PARAM];

/** What the facets and the search hold; the address and the remembered view carry the same. */
interface FeedbackFilterState {
  values: Readonly<Record<FacetColumn, readonly string[]>>;
  search: string;
}

/** One facet as the filter bar renders it. */
interface FacetView {
  column: FacetColumn;
  facetId: string;
  label: string;
  mode: 'multiple' | 'single';
  anyLabel: string;
  options: FilterFacetOption[];
  selected: readonly string[];
}

/** One issue's occurrences fetched so far. */
interface OccurrenceState {
  rows: AdminFeedbackOccurrence[];
  total: number;
  loading: boolean;
  loaded: boolean;
  error: string | null;
}

/** The last Save or Delete of one issue. */
interface TriageResult {
  message: string;
  error: string | null;
}

/**
 * The Feedback tab's Overseer sub-tab: the issues the assistant's `report_feedback` tool has reported
 * in the Overseer chat within the shell's date range, as a server-paged card list. Its filter bar has a
 * search, Sort by, the Status, Category, Model, Provider, User, Group, Model source, Configuration,
 * Version, Tool and Has resolution facets, and chips, which also show an `ids` filter a link from the
 * GnollBench sub-tab sets (`status=all` is no status filter); the facets and the search live in the address and,
 * with the sort and the range, in the remembered view. Each issue has its figures, latest facts,
 * occurrences on demand, a selection checkbox, a one-issue YAML download, and Edit triage and Delete,
 * which open the triage dialog and a confirmation; the selection bar exports and imports the selection.
 * Report text is model output and is rendered as plain text only.
 */
@Component({
  selector: 'app-feedback-overseer-panel',
  imports: [FilterFacetComponent, AdminConfirmDialogComponent, FeedbackTriageDialogComponent, FeedbackSelectionBarComponent, RouterLink],
  templateUrl: './feedback-overseer-panel.component.html',
  styleUrl: './feedback-overseer-panel.component.scss'
})
export class FeedbackOverseerPanelComponent implements OnInit, OnChanges, OnDestroy {
  private readonly feedback = inject(AdminFeedbackService);
  private readonly transfer = inject(FeedbackTransferService);
  private readonly store = inject(AdminPageStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly cdr = inject(ChangeDetectorRef);

  /** The shell's date range for this sub-tab; a new object on every change. */
  @Input({ required: true }) range: FeedbackRange = defaultFeedbackRange();

  /** Bumped by the shell's Refresh; a change after the first value refetches. */
  @Input() refreshToken = 0;

  /** Whether a page of the list is loading, for the shell's Refresh. */
  @Output() readonly loadingChange = new EventEmitter<boolean>();

  @ViewChild(AdminConfirmDialogComponent, { static: true }) confirmDialog!: AdminConfirmDialogComponent;
  @ViewChild(FeedbackTriageDialogComponent, { static: true }) triageDialog!: FeedbackTriageDialogComponent;

  readonly sorts = FEEDBACK_SORTS;
  readonly searchMax = SEARCH_MAX;
  readonly statusLabel = feedbackStatusLabel;
  readonly categoryLabel = feedbackCategoryLabel;
  readonly causeLabel = feedbackCauseLabel;
  readonly dateText = feedbackDateText;
  readonly timeText = feedbackTimeText;
  readonly dateTimeText = feedbackDateTimeText;
  readonly versionShort = feedbackVersionShort;
  readonly modelSourceText = feedbackModelSourceText;
  readonly formatInteger = formatInteger;

  /** Issue counts per status over all dates, as the page store last fetched them. */
  readonly counts = signal<AdminFeedbackCounts | null>(null);

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
  /** Announced in the status line until the next fetch: a deletion, or a selection change. */
  readonly notice = signal('');
  /** The facet options present in the range; null until they arrive, or when they fail. */
  readonly facetData = signal<AdminFeedbackFacets | null>(null);
  /** The issues in the range with and without a resolution, for the Has resolution facet. */
  readonly resolutionCounts = signal<Readonly<Record<AdminFeedbackResolutionFilter, number>> | null>(null);
  readonly occurrences = signal<Readonly<Record<number, OccurrenceState>>>({});
  /** The issues whose occurrences are shown without the occurrence filters. */
  readonly allOccurrences = signal<ReadonlySet<number>>(new Set<number>());
  /** The occurrences whose long details are shown in full. */
  readonly expandedDetails = signal<ReadonlySet<number>>(new Set<number>());
  readonly savingId = signal<number | null>(null);
  readonly deletingId = signal<number | null>(null);
  /** The issue whose YAML download is running; another download waits until it ends. */
  readonly downloadingId = signal<number | null>(null);
  readonly triageResults = signal<Readonly<Record<number, TriageResult>>>({});

  // --- Selection ---

  /** The selected issue ids, held across paging, filtering and reloads, at most 500. */
  readonly selected = signal<ReadonlySet<number>>(new Set<number>());
  /** While on, the list shows the selected issues only. */
  readonly showSelectedOnly = signal(false);
  /** Selected issues the current filters leave out; 0 while unknown. */
  readonly notShownCount = signal(0);
  /** The export dialog's options, as the bar last reported them. */
  exportOptions: FeedbackExportOptions | undefined;

  /** The server filters, sorts and pages; the table carries the filter values and the sort. */
  readonly table = new TableState<AdminFeedbackIssue>(DEFAULT_SORT, 'desc').registerAccessors(
    {
      lastSeen: i => parseServerUtcDate(i.lastSeenUtc).getTime(),
      firstSeen: i => parseServerUtcDate(i.firstSeenUtc).getTime(),
      count: i => i.occurrenceCount,
      users: i => i.distinctUserCount,
      regressions: i => i.regressionCount,
      triaged: i => i.triagedUtc ? parseServerUtcDate(i.triagedUtc).getTime() : null,
      subject: i => i.subject
    },
    {
      search: customFilter<AdminFeedbackIssue>(() => true),
      ...Object.fromEntries(FACET_COLUMNS.map(column => [column, anyOfFilter<AdminFeedbackIssue>(() => null)]))
    }
  );

  /**
   * The search, the facets' selections and the chips; the server filters, so no facet reads a row.
   * The sort is set through `applySort`, never `setSort`, because the remembered view keeps it.
   */
  readonly list = new CardListState<AdminFeedbackIssue>(this.table, {
    idPrefix: 'tl-fb',
    batch: FEEDBACK_BATCH,
    sorts: FEEDBACK_SORTS,
    defaultSort: DEFAULT_SORT,
    storageKey: 'overseer.admin.feedback.overseer.view',
    facets: FACETS.map((facet): CardListFacetSpec<AdminFeedbackIssue> => ({
      column: facet.column,
      label: facet.label,
      values: () => null,
      labelOf: value => this.valueLabel(facet.column, value)
    })),
    onChange: () => this.onFiltersChanged()
  });

  private listSub: Subscription | null = null;
  private probeSub: Subscription | null = null;
  private facetsSub: Subscription | null = null;
  private resolutionSubs: Subscription[] = [];
  private querySub: Subscription | null = null;
  private navigationSub: Subscription | null = null;
  private countsSub: Subscription | null = null;
  private selectionSub: Subscription | null = null;
  private notShownSub: Subscription | null = null;
  private selectAllSub: Subscription | null = null;
  private downloadSub: Subscription | null = null;
  private readonly occurrenceSubs = new Map<number, Subscription>();
  /** Set once the address of this sub-tab has been applied the first time. */
  private initialized = false;

  private statusOptionsMemo: { counts: AdminFeedbackCounts | null; options: FilterFacetOption[] } | null = null;
  private facetViewsMemo: { key: readonly unknown[]; views: FacetView[] } | null = null;
  private selectedIdsMemo: { set: ReadonlySet<number>; ids: readonly FeedbackSelectionId[] } | null = null;
  private readonly crossLinkMemo = new WeakMap<AdminFeedbackIssue, Record<string, string | null>>();

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.counts.set(this.store.feedbackCounts);
    this.countsSub = this.store.feedbackCountsChanged$.subscribe(() => {
      this.counts.set(this.store.feedbackCounts);
      this.cdr.markForCheck();
    });
    const view = loadFeedbackView('overseer');
    this.exportOptions = view?.exportOptions;
    this.applySort(view?.sort ?? DEFAULT_SORT);
    this.selected.set(new Set(loadOverseerSelection()));
    this.table.setRemoteTotal(0);
    // Emits the current address at once; a tab switch's navigation that keeps the query arrives as NavigationEnd.
    this.querySub = this.route.queryParamMap.subscribe(query => this.onAddress(query));
    this.navigationSub = this.router.events.pipe(filter(event => event instanceof NavigationEnd))
      .subscribe(() => this.onAddress(this.route.snapshot.queryParamMap));
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.initialized) return;
    if (changes['range']) {
      this.saveView();
      this.loadFacets();
      this.reloadIssues();
      this.cdr.markForCheck();
    }
    if (changes['refreshToken'] && !changes['refreshToken'].firstChange) {
      this.reload();
    }
  }

  ngOnDestroy(): void {
    this.list.dispose();
    this.listSub?.unsubscribe();
    this.probeSub?.unsubscribe();
    this.facetsSub?.unsubscribe();
    this.resolutionSubs.forEach(sub => sub.unsubscribe());
    this.querySub?.unsubscribe();
    this.navigationSub?.unsubscribe();
    this.countsSub?.unsubscribe();
    this.selectionSub?.unsubscribe();
    this.notShownSub?.unsubscribe();
    this.selectAllSub?.unsubscribe();
    this.downloadSub?.unsubscribe();
    this.occurrenceSubs.forEach(sub => sub.unsubscribe());
  }

  /** Refetches the facets and the first page of issues, and checks the selection; open occurrence lists start again. */
  reload(): void {
    if (!this.initialized) return;
    this.triageResults.set({});
    this.resetOccurrences();
    this.loadFacets();
    this.pruneSelection();
    this.reloadIssues();
  }

  // --- Filters ---

  get searchTerm(): string {
    return (this.table.filters['search'] ?? '').trim();
  }

  /** A facet's selection; one empty array while nothing is selected, so the facet input stays put. */
  selectedOf(column: FacetColumn): readonly string[] {
    const selected = this.table.filterValues(column);
    return selected.length > 0 ? selected : NO_VALUES;
  }

  get selectedStatuses(): readonly string[] {
    return this.selectedOf('status');
  }

  get hasFilters(): boolean {
    return this.showSelectedOnly() || this.searchTerm !== '' || FACET_COLUMNS.some(column => this.selectedOf(column).length > 0);
  }

  /** Whether a facet that narrows the occurrences has a selection. */
  get hasOccurrenceFilters(): boolean {
    return FACETS.some(facet => facet.occurrenceLevel && this.selectedOf(facet.column).length > 0);
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

  /** The listed facets with their options and selections; the same array until an input changes. */
  get facetViews(): FacetView[] {
    const key: unknown[] = [this.statusOptions, this.facetData(), this.resolutionCounts(),
      ...FACET_COLUMNS.map(column => this.selectedOf(column))];
    const memo = this.facetViewsMemo;
    if (memo && memo.key.length === key.length && memo.key.every((value, i) => value === key[i])) {
      return memo.views;
    }
    const views: FacetView[] = [];
    for (const facet of FACETS) {
      if (facet.addressOnly) continue;
      const selected = this.selectedOf(facet.column);
      const options = this.withSelected(this.optionsOf(facet.column), facet.column, selected);
      if (!facet.alwaysListed && options.length < 2 && selected.length === 0) continue;
      views.push({
        column: facet.column,
        facetId: `tl-fb-facet-${facet.column}`,
        label: facet.label,
        mode: facet.mode,
        anyLabel: facet.anyLabel ?? 'Any',
        options,
        selected
      });
    }
    this.facetViewsMemo = { key, views };
    return views;
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
    const id = (event.target as HTMLSelectElement).value;
    if (!FEEDBACK_SORTS.some(sort => sort.id === id)) return;
    this.applySort(id);
    this.saveView();
    this.reloadIssues();
  }

  onFacetChange(column: FacetColumn, values: string[]): void {
    const facet = FACETS.find(f => f.column === column);
    this.list.setFacet(column, facet?.mode === 'single' ? values.slice(0, 1) : values);
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

  /** Clears the search and every facet, the Status default included, refetches, then focuses the search. Show selected only stays. */
  clearFilters(): void {
    this.list.clearFilters();
    this.setSearchField('');
    this.onFiltersChanged();
    this.cdr.detectChanges();
    this.element('#tl-fb-search')?.focus();
  }

  /** A filter changed here: the address and the remembered view follow, and the first page is refetched. */
  private onFiltersChanged(): void {
    this.writeQuery();
    this.saveView();
    this.resetOccurrences();
    this.reloadIssues();
    this.cdr.markForCheck();
  }

  /** The options of one facet as the range's facets list them, in the facet's order; labeled values sort by their label. */
  private optionsOf(column: FacetColumn): FilterFacetOption[] {
    const data = this.facetData();
    const values = (list: AdminFeedbackFacetValue[] | undefined, label: (value: AdminFeedbackFacetValue) => string = v => v.value) =>
      (list ?? []).map(v => ({ value: v.value, label: label(v), count: v.count }))
        .sort((a, b) => compareText(a.label, b.label) || compareText(a.value, b.value));
    const entities = (list: AdminFeedbackFacetEntity[] | undefined) =>
      [...(list ?? [])].sort((a, b) => compareText(a.name, b.name)).map(e => ({ value: e.id, label: e.name || e.id, count: e.count }));
    const countOf = (list: AdminFeedbackFacetValue[] | undefined, value: string) => list?.find(v => v.value === value)?.count ?? 0;
    switch (column) {
      case 'status':
        return this.statusOptions;
      case 'category':
        return ADMIN_FEEDBACK_CATEGORIES.map(c => ({ value: c, label: feedbackCategoryLabel(c), count: countOf(data?.categories, c) }));
      case 'model':
        return values(data?.models, v => v.label || v.value);
      case 'provider':
        return values(data?.providers, v => providerLabel(v.value) || v.value);
      case 'user':
        return entities(data?.users);
      case 'group': {
        const groups = entities(data?.groups.filter(g => g.id !== ADMIN_FEEDBACK_NO_GROUP));
        const none = data?.groups.find(g => g.id === ADMIN_FEEDBACK_NO_GROUP);
        return none ? [...groups, { value: ADMIN_FEEDBACK_NO_GROUP, label: 'No group', count: none.count }] : groups;
      }
      case 'source':
        return ADMIN_FEEDBACK_MODEL_SOURCES
          .filter(source => data?.sources.some(s => s.value === source))
          .map(source => ({ value: source, label: MODEL_SOURCE_LABELS[source], count: countOf(data?.sources, source) }));
      case 'config':
        return entities(data?.configs);
      case 'version':
        return values(data?.versions);
      case 'tool':
        return values(data?.tools, v => toolLabel(v.value) || v.value);
      case 'resolution': {
        const counts = this.resolutionCounts();
        return ADMIN_FEEDBACK_RESOLUTION_FILTERS.map(value => ({ value, label: RESOLUTION_FILTER_LABELS[value], count: counts?.[value] ?? 0 }));
      }
      case 'ids':
        return [];
    }
  }

  /** The options with every selected value the range does not list appended, so it can still be cleared. */
  private withSelected(options: FilterFacetOption[], column: FacetColumn, selected: readonly string[]): FilterFacetOption[] {
    const missing = selected.filter(value => !options.some(option => option.value === value));
    if (missing.length === 0) return options;
    return [...options, ...missing.map(value => ({ value, label: this.valueLabel(column, value), count: 0 }))];
  }

  /** What a facet value reads as, in its facet and its chip. */
  private valueLabel(column: FacetColumn, value: string): string {
    const data = this.facetData();
    switch (column) {
      case 'status': return feedbackStatusLabel(value);
      case 'category': return feedbackCategoryLabel(value);
      case 'source': return labelFrom(MODEL_SOURCE_LABELS, value);
      case 'resolution': return labelFrom(RESOLUTION_FILTER_LABELS, value);
      case 'group':
        return value === ADMIN_FEEDBACK_NO_GROUP ? 'No group' : data?.groups.find(g => g.id === value)?.name || value;
      case 'user': return data?.users.find(u => u.id === value)?.name || value;
      case 'config': return data?.configs.find(c => c.id === value)?.name || value;
      case 'model': return data?.models.find(m => m.value === value)?.label || value;
      case 'provider': return providerLabel(value) || value;
      case 'tool': return toolLabel(value) || value;
      case 'ids': return `#${value}`;
      default: return value;
    }
  }

  private setSearchField(text: string): void {
    const field = this.element('#tl-fb-search') as HTMLInputElement | null;
    if (field) field.value = text;
  }

  private applySort(id: string): void {
    const sort = FEEDBACK_SORTS.find(s => s.id === id) ?? FEEDBACK_SORTS.find(s => s.id === DEFAULT_SORT)!;
    this.list.sortId = sort.id;
    this.table.setSort(sort.column, sort.direction);
    this.list.resetBatch();
  }

  // --- The address and the remembered view ---

  private filterState(): FeedbackFilterState {
    const values = Object.fromEntries(FACET_COLUMNS.map(column => [column, this.selectedOf(column)])) as Record<FacetColumn, readonly string[]>;
    return { values, search: this.searchTerm };
  }

  /** Known values only, in a fixed order where the facet has one; the default statuses when `statuses` is null. */
  private static normalizeState(raw: Partial<Record<FacetColumn, readonly string[] | null>>, search: string): FeedbackFilterState {
    const unique = (values: readonly string[] | null | undefined) =>
      [...new Set((values ?? []).map(v => v.trim()).filter(v => v !== ''))];
    const known = (values: readonly string[] | null | undefined, allowed: readonly string[]) =>
      allowed.filter(value => unique(values).includes(value));
    const statuses = raw.status == null
      ? [...DEFAULT_FEEDBACK_STATUSES]
      : raw.status.includes(FEEDBACK_STATUS_QUERY_ALL) ? [] : known(raw.status, ADMIN_FEEDBACK_STATUSES);
    const values: Record<FacetColumn, readonly string[]> = {
      status: statuses,
      category: known(raw.category, ADMIN_FEEDBACK_CATEGORIES),
      model: unique(raw.model),
      provider: unique(raw.provider),
      user: unique(raw.user),
      group: unique(raw.group),
      source: known(raw.source, ADMIN_FEEDBACK_MODEL_SOURCES),
      config: unique(raw.config),
      version: unique(raw.version),
      tool: unique(raw.tool),
      resolution: known(raw.resolution, ADMIN_FEEDBACK_RESOLUTION_FILTERS).slice(0, 1),
      ids: unique(raw.ids).filter(id => /^[1-9]\d*$/.test(id)).slice(0, ADMIN_FEEDBACK_IDS_MAX)
    };
    return { values, search: search.trim().slice(0, SEARCH_MAX) };
  }

  /** The filters an address names: an absent `status` is the default, `status=all` none; unknown values are dropped. */
  private static filtersFromQuery(query: ParamMap): FeedbackFilterState {
    const raw: Partial<Record<FacetColumn, readonly string[] | null>> = {};
    for (const column of FACET_COLUMNS) raw[column] = readAdminQueryList(query, column);
    return FeedbackOverseerPanelComponent.normalizeState(raw, query.get(SEARCH_PARAM) ?? '');
  }

  /** The filters a remembered view holds; a view without a Status selection has the default. */
  private static filtersFromView(view: FeedbackViewRecord | null): FeedbackFilterState {
    const raw: Partial<Record<FacetColumn, readonly string[] | null>> = {};
    for (const column of FACET_COLUMNS) {
      const values = view?.filters[column];
      raw[column] = column === 'status'
        ? (values === undefined ? null : values.length === 0 ? [FEEDBACK_STATUS_QUERY_ALL] : values)
        : values ?? [];
    }
    return FeedbackOverseerPanelComponent.normalizeState(raw, view?.search ?? '');
  }

  private static sameFilters(a: FeedbackFilterState, b: FeedbackFilterState): boolean {
    const set = (values: readonly string[]) => [...values].sort().join(',');
    return a.search === b.search && FACET_COLUMNS.every(column => set(a.values[column]) === set(b.values[column]));
  }

  private static isDefaultStatuses(statuses: readonly string[]): boolean {
    return statuses.length === DEFAULT_FEEDBACK_STATUSES.length && DEFAULT_FEEDBACK_STATUSES.every(status => statuses.includes(status));
  }

  /** Whether an address carries any filter parameter of this sub-tab. */
  private static hasFilterParams(query: ParamMap): boolean {
    return FILTER_PARAMS.some(name => query.has(name));
  }

  /** Whether the address is this sub-tab's: `/admin/feedback`, the default sub-tab, with no further segment. */
  private isOwnAddress(): boolean {
    const segments = this.router.parseUrl(this.router.url).root.children['primary']?.segments.map(s => s.path) ?? [];
    return segments.length === 2 && segments[0] === 'admin' && segments[1] === ADMIN_TAB_SLUGS.feedback;
  }

  /**
   * Takes the filters of this sub-tab's address. The first time, an address that carries any of its filter
   * parameters wins and is remembered; one with none takes the remembered view and writes it into the
   * address. Later, a change of the address (Back, Forward) refetches.
   */
  private onAddress(query: ParamMap): void {
    if (!this.isOwnAddress()) return;
    if (!this.initialized) {
      this.initialized = true;
      const fromAddress = FeedbackOverseerPanelComponent.hasFilterParams(query);
      const state = fromAddress
        ? FeedbackOverseerPanelComponent.filtersFromQuery(query)
        : FeedbackOverseerPanelComponent.filtersFromView(loadFeedbackView('overseer'));
      this.setFilterState(state);
      if (!fromAddress) this.writeQuery();
      this.saveView();
      this.loadFacets();
      this.pruneSelection();
      this.reloadIssues();
      return;
    }
    const next = FeedbackOverseerPanelComponent.filtersFromQuery(query);
    if (FeedbackOverseerPanelComponent.sameFilters(next, this.filterState())) return;
    this.setFilterState(next);
    this.saveView();
    this.resetOccurrences();
    this.reloadIssues();
    this.cdr.markForCheck();
  }

  private setFilterState(state: FeedbackFilterState): void {
    for (const column of FACET_COLUMNS) this.table.setFilterValues(column, state.values[column]);
    this.table.setFilter('search', state.search);
    this.list.searchText = state.search;
    this.setSearchField(state.search);
    this.list.resetBatch();
    this.list.invalidate();
  }

  /** The filters as query parameters: the default statuses as none, no status as `all`, an empty list as none. */
  private queryParamsOf(state: FeedbackFilterState): Record<string, string | null> {
    const statuses = state.values.status;
    const ordered = ADMIN_FEEDBACK_STATUSES.filter(status => statuses.includes(status));
    const params: Record<string, string | null> = {
      status: FeedbackOverseerPanelComponent.isDefaultStatuses(statuses) ? null
        : ordered.length === 0 ? FEEDBACK_STATUS_QUERY_ALL : adminQueryList(ordered)
    };
    for (const column of FACET_COLUMNS) {
      if (column !== 'status') params[column] = adminQueryList(state.values[column]);
    }
    params[SEARCH_PARAM] = state.search || null;
    return params;
  }

  /** Writes the filters into the address, replacing the history entry and keeping every other parameter; no-op when it already holds them. */
  private writeQuery(): void {
    const params = this.queryParamsOf(this.filterState());
    const current = this.route.snapshot.queryParamMap;
    if (Object.entries(params).every(([name, value]) => current.get(name) === value)) return;
    // A navigation the router fails is reported by its own NavigationError event.
    this.router.navigate([], { queryParams: params, queryParamsHandling: 'merge', replaceUrl: true }).catch(() => false);
  }

  /** Remembers the sort, the facets, the search, the range and the export options. */
  private saveView(): void {
    const state = this.filterState();
    const filters: Record<string, string[]> = {};
    for (const column of FACET_COLUMNS) filters[column] = [...state.values[column]];
    const record: FeedbackViewRecord = {
      version: 1,
      sort: this.list.sortId,
      filters,
      search: state.search,
      range: { ...this.range }
    };
    if (this.exportOptions) record.exportOptions = { ...this.exportOptions };
    saveFeedbackView('overseer', record);
  }

  // --- Fetching ---

  get remainingCount(): number {
    return Math.max(0, this.totalCount() - this.rows().length);
  }

  get nextBatchCount(): number {
    return Math.min(this.list.batch, this.remainingCount);
  }

  /** `Showing 25 of 40 issues`, `One issue`, or empty with none; a notice is announced before it. */
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
  get emptyKind(): 'range' | 'filters' | 'selection' | null {
    if (this.showSelectedOnly()) return 'selection';
    if (!this.hasFilters) return 'range';
    const total = this.rangeTotal();
    if (total === null) return null;
    return total === 0 ? 'range' : 'filters';
  }

  /** Fetches the first page of the current query, replacing the list. */
  reloadIssues(): void {
    if (!this.initialized) return;
    this.fetch(false);
  }

  /** Appends the next page and focuses the title of the first new card. */
  showMore(): void {
    if (this.loading() || this.remainingCount === 0) return;
    this.fetch(true);
  }

  private dateFilters(): AdminFeedbackIssueFilters {
    return { startDate: this.range.start || undefined, endDate: this.range.end || undefined };
  }

  /** The current filters as the server takes them; Show selected only sends the selected ids alone. */
  currentFilters(): AdminFeedbackIssueFilters {
    if (this.showSelectedOnly()) {
      return { ids: [...this.selected()] };
    }
    const state = this.filterState();
    const list = (column: FacetColumn) => state.values[column].length > 0 ? [...state.values[column]] : undefined;
    return {
      ...this.dateFilters(),
      statuses: list('status'),
      categories: list('category'),
      modelIds: list('model'),
      providers: list('provider'),
      users: list('user'),
      groups: list('group'),
      sources: list('source'),
      configs: list('config'),
      versions: list('version'),
      tools: list('tool'),
      resolution: state.values.resolution[0] as AdminFeedbackResolutionFilter | undefined,
      ids: state.values.ids.length > 0 ? state.values.ids.map(Number) : undefined,
      search: state.search || undefined
    };
  }

  private setLoading(value: boolean): void {
    if (this.loading() === value) return;
    this.loading.set(value);
    this.loadingChange.emit(value);
  }

  private fetch(append: boolean): void {
    if (this.showSelectedOnly() && this.selected().size === 0) {
      // An empty `ids` filter would be left out of the request, which would then match every issue.
      this.listSub?.unsubscribe();
      this.rows.set([]);
      this.totalCount.set(0);
      this.table.setRemoteTotal(0);
      this.setLoading(false);
      this.loaded.set(true);
      this.error.set(null);
      this.list.invalidate();
      this.cdr.markForCheck();
      return;
    }
    const query: AdminFeedbackIssuesQuery = {
      ...this.currentFilters(),
      sort: this.list.sortId as AdminFeedbackIssueSort,
      skip: append ? this.rows().length : 0,
      take: FEEDBACK_BATCH
    };
    if (!append) {
      this.loaded.set(false);
      this.rangeTotal.set(null);
      this.probeSub?.unsubscribe();
    }
    this.setLoading(true);
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
        this.totalCount.set(page.totalCount);
        this.table.setRemoteTotal(page.totalCount);
        this.setLoading(false);
        this.loaded.set(true);
        this.list.invalidate();
        if (!append) {
          if (page.rows.length === 0 && this.hasFilters && !this.showSelectedOnly()) this.countRange();
          this.countNotShown();
        }
        const focusId = firstNew >= 0 ? this.rows()[firstNew]?.id : undefined;
        if (focusId !== undefined) {
          this.focusAfterRender(`#tl-fb-${focusId}-title`);
        } else {
          this.cdr.markForCheck();
        }
      },
      error: err => {
        this.setLoading(false);
        this.loaded.set(true);
        this.error.set('The feedback issues could not be loaded: ' + adminApiError(err).message);
        this.cdr.markForCheck();
      }
    });
  }

  /** Counts the issues in the range under no filter, to tell the two empty states apart. */
  private countRange(): void {
    this.probeSub?.unsubscribe();
    this.probeSub = this.feedback.getIssues({ ...this.dateFilters(), take: 1 }).subscribe({
      next: page => {
        this.rangeTotal.set(page.totalCount);
        this.cdr.markForCheck();
      },
      // Unknown: the filters are the likelier cause, and Clear filters is the useful offer.
      error: () => {
        this.rangeTotal.set(1);
        this.cdr.markForCheck();
      }
    });
  }

  /** The facet options of the range, and the issues in it with and without a resolution. */
  private loadFacets(): void {
    this.facetsSub?.unsubscribe();
    this.facetsSub = this.feedback.getFacets(this.range.start || undefined, this.range.end || undefined).subscribe({
      next: facets => {
        this.facetData.set(facets);
        this.list.invalidate();
        this.cdr.markForCheck();
      },
      // The facets list only what is selected; the list itself still works.
      error: () => {
        this.facetData.set(null);
        this.cdr.markForCheck();
      }
    });
    this.resolutionSubs.forEach(sub => sub.unsubscribe());
    const counts: Partial<Record<AdminFeedbackResolutionFilter, number>> = {};
    this.resolutionSubs = ADMIN_FEEDBACK_RESOLUTION_FILTERS.map(resolution =>
      this.feedback.getIssues({ ...this.dateFilters(), resolution, take: 1 }).subscribe({
        next: page => {
          counts[resolution] = page.totalCount;
          if (ADMIN_FEEDBACK_RESOLUTION_FILTERS.every(value => counts[value] !== undefined)) {
            this.resolutionCounts.set({ with: counts.with!, without: counts.without! });
            this.cdr.markForCheck();
          }
        },
        error: () => { /* The facet shows no counts. */ }
      }));
  }

  // --- Selection ---

  isSelected(issueId: number): boolean {
    return this.selected().has(issueId);
  }

  /** The selection as the selection bar takes it; the same array until the selection changes. */
  get selectedIds(): readonly FeedbackSelectionId[] {
    const set = this.selected();
    if (this.selectedIdsMemo?.set === set) return this.selectedIdsMemo.ids;
    const ids = [...set];
    this.selectedIdsMemo = { set, ids };
    return ids;
  }

  /** A card's checkbox; a selection already at 500 refuses another issue and says so. */
  onSelectChange(issue: AdminFeedbackIssue, event: Event): void {
    const checkbox = event.target as HTMLInputElement;
    const next = new Set(this.selected());
    if (checkbox.checked) {
      if (next.size >= FEEDBACK_SELECTION_CAP) {
        checkbox.checked = false;
        this.notice.set(`The selection holds at most ${formatInteger(FEEDBACK_SELECTION_CAP)} issues.`);
        this.cdr.markForCheck();
        return;
      }
      next.add(issue.id);
    } else {
      next.delete(issue.id);
    }
    // A card on the list matches the filters, so the count of selected issues not shown stays.
    this.setSelection(next, false);
  }

  /** Show selected only: on, the list holds the selected issues alone and drops the ones the server no longer has. */
  onShowSelectedOnlyChange(on: boolean): void {
    if (this.showSelectedOnly() === on) return;
    this.showSelectedOnly.set(on);
    if (on) this.pruneSelection();
    this.resetOccurrences();
    this.reloadIssues();
    this.cdr.markForCheck();
  }

  /** Clears the selection; Show selected only goes off with it. */
  onClearSelection(): void {
    const wasSelectedOnly = this.showSelectedOnly();
    this.showSelectedOnly.set(false);
    this.setSelection(new Set<number>());
    if (wasSelectedOnly) {
      this.resetOccurrences();
      this.reloadIssues();
    }
    this.cdr.markForCheck();
  }

  /** Adds every issue matching the filters to the selection, up to 500, and says when a cap cut it. */
  onSelectAllMatching(): void {
    this.selectAllSub?.unsubscribe();
    this.selectAllSub = this.feedback.getIssueIds(this.currentFilters(), this.list.sortId as AdminFeedbackIssueSort).subscribe({
      next: result => {
        const next = new Set(this.selected());
        for (const id of result.ids) {
          if (next.size >= FEEDBACK_SELECTION_CAP) break;
          next.add(id);
        }
        const cut = result.truncated || result.ids.some(id => !next.has(id));
        this.setSelection(next, false);
        this.notice.set(cut
          ? `Selected the first ${formatInteger(FEEDBACK_SELECTION_CAP)} of ${formatInteger(result.totalCount)}.`
          : `Selected all ${formatInteger(result.totalCount)} matching issues.`);
        this.cdr.markForCheck();
      },
      error: err => {
        this.notice.set('The matching issues could not be selected: ' + adminApiError(err).message);
        this.cdr.markForCheck();
      }
    });
  }

  /** An export finished: the issues it could not find leave the selection, and its options are remembered. */
  onExported(event: { missing: FeedbackSelectionId[]; exportOptions: FeedbackExportOptions }): void {
    this.exportOptions = { ...event.exportOptions };
    this.saveView();
    const missing = new Set(event.missing.map(id => Number(id)));
    if (missing.size > 0) {
      this.setSelection(new Set([...this.selected()].filter(id => !missing.has(id))));
    }
    this.cdr.markForCheck();
  }

  /** An import changed issues: the list and its facets are refetched, and the tab's counts. */
  onImported(): void {
    this.reload();
    this.store.loadFeedbackCounts();
  }

  /** Replaces and remembers the selection; `recount` asks the server again how many selected issues the filters leave out. */
  private setSelection(next: ReadonlySet<number>, recount = true): void {
    this.selected.set(next);
    saveOverseerSelection([...next]);
    if (this.showSelectedOnly()) {
      this.reloadIssues();
    } else if (recount || next.size === 0) {
      this.countNotShown();
    }
  }

  /** Drops the selected ids the server no longer has. */
  private pruneSelection(): void {
    const ids = [...this.selected()];
    this.selectionSub?.unsubscribe();
    if (ids.length === 0) return;
    this.selectionSub = this.feedback.getIssueIds({ ids }).subscribe({
      next: result => {
        const present = new Set(result.ids);
        if (ids.some(id => !present.has(id))) {
          this.selected.set(new Set(ids.filter(id => present.has(id))));
          saveOverseerSelection([...this.selected()]);
          this.countNotShown();
          this.cdr.markForCheck();
        }
      },
      error: () => { /* The selection is checked again on the next refresh. */ }
    });
  }

  /** Counts the selected issues the current filters leave out. */
  private countNotShown(): void {
    this.notShownSub?.unsubscribe();
    const ids = [...this.selected()];
    if (ids.length === 0 || this.showSelectedOnly()) {
      this.notShownCount.set(0);
      return;
    }
    // An `ids` filter from the address leaves out every selected issue it does not name.
    const filters = this.currentFilters();
    const named = filters.ids ? ids.filter(id => filters.ids!.includes(id)) : ids;
    if (named.length === 0) {
      this.notShownCount.set(ids.length);
      return;
    }
    this.notShownSub = this.feedback.getIssueIds({ ...filters, ids: named }).subscribe({
      next: result => {
        this.notShownCount.set(Math.max(0, ids.length - result.totalCount));
        this.cdr.markForCheck();
      },
      error: () => this.notShownCount.set(0)
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

  /** The issue's models by their catalog names, `+N` as it is; an id the server sent no name for reads as the id. */
  modelsText(issue: AdminFeedbackIssue): string {
    return issue.modelIds.map((id, index) => issue.modelLabels?.[index] || id).join(', ');
  }

  /** The issue's providers by name (`OpenAI`); empty for none. */
  providersText(issue: AdminFeedbackIssue): string {
    return issue.providers.map(provider => providerLabel(provider) || provider).join(', ');
  }

  /** `GnollHack wiki search, Item stats`; empty for none. */
  toolsText(tools: readonly string[]): string {
    return tools.map(tool => toolLabel(tool) || tool).join(', ');
  }

  /** `GnollHack wiki, Overseer release`; empty while the issue says nothing about where the fix went. */
  resolvedViaText(issue: AdminFeedbackIssue): string {
    return issue.resolvedVia?.length ? formatResolvedVia(issue.resolvedVia) : '';
  }

  /** `System configuration 5 · User's own model 2`; the non-zero counts only, or empty. */
  modelSourcesText(issue: AdminFeedbackIssue): string {
    const sources = issue.modelSources;
    if (!sources) return '';
    return ADMIN_FEEDBACK_MODEL_SOURCES
      .filter(source => sources[source] > 0)
      .map(source => `${MODEL_SOURCE_LABELS[source]} ${formatInteger(sources[source])}`)
      .join(' · ');
  }

  /** `Triaged 21 Oct 2026 14:02 by tommi`; empty for an issue nobody has triaged. */
  triagedText(issue: AdminFeedbackIssue): string {
    if (!issue.triagedUtc) return '';
    const by = issue.triagedByUserName ? ` by ${issue.triagedByUserName}` : '';
    return `Triaged ${feedbackDateTimeText(issue.triagedUtc)}${by}`;
  }

  /** `Also reported in GnollBench: 4 self-reports, Resolved.` */
  gnollBenchText(issue: AdminFeedbackIssue): string {
    const link = issue.alsoInGnollBench;
    if (!link) return '';
    const reports = link.selfReportCount === 1 ? '1 self-report' : `${formatInteger(link.selfReportCount)} self-reports`;
    return `Also reported in GnollBench: ${reports}, ${feedbackStatusLabel(link.status)}.`;
  }

  /** The GnollBench sub-tab's path. */
  readonly gnollBenchCommands = adminCommands('feedback', 'gnollbench');

  /**
   * The query of the GnollBench sub-tab searched for the issue's subject in its status; every Feedback
   * filter parameter of this sub-tab is removed, and every other parameter kept by `merge`.
   */
  gnollBenchQuery(issue: AdminFeedbackIssue): Record<string, string | null> {
    let params = this.crossLinkMemo.get(issue);
    if (!params) {
      params = { ...clearedFeedbackQueryParams(), q: issue.subject.slice(0, SEARCH_MAX), status: issue.alsoInGnollBench?.status ?? null };
      this.crossLinkMemo.set(issue, params);
    }
    return params;
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
    return Math.min(FEEDBACK_OCCURRENCE_BATCH, this.remainingOccurrences(issueId));
  }

  /** Whether an issue's occurrences are shown without the occurrence filters. */
  showsAllOccurrences(issueId: number): boolean {
    return this.allOccurrences().has(issueId);
  }

  /** Switches one issue between the occurrences the filters match and all of them, and refetches them. */
  toggleAllOccurrences(issueId: number): void {
    const next = new Set(this.allOccurrences());
    if (next.has(issueId)) next.delete(issueId);
    else next.add(issueId);
    this.allOccurrences.set(next);
    this.fetchOccurrences(issueId, false);
  }

  private fetchOccurrences(issueId: number, append: boolean): void {
    const previous = this.occurrenceState(issueId);
    const rows = append && previous ? previous.rows : [];
    this.setOccurrenceState(issueId, { rows, total: previous?.total ?? 0, loading: true, loaded: previous?.loaded ?? false, error: null });
    this.occurrenceSubs.get(issueId)?.unsubscribe();
    const all = this.showSelectedOnly() || !this.hasOccurrenceFilters || this.showsAllOccurrences(issueId);
    const filters = all ? {} : this.currentFilters();
    const sub = this.feedback.getOccurrences(issueId, rows.length, FEEDBACK_OCCURRENCE_BATCH, filters, all && this.hasOccurrenceFilters).subscribe({
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
        this.cdr.markForCheck();
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

  /** `Anthropic · Claude X`: the provider's and the model's labels, whichever is known; the model id without a label. */
  modelText(occurrence: AdminFeedbackOccurrence): string {
    return [providerLabel(occurrence.provider), occurrence.modelLabel || occurrence.modelId].filter(part => !!part).join(' · ');
  }

  /** The reporting user's groups, or `No group`. */
  groupsText(occurrence: AdminFeedbackOccurrence): string {
    return occurrence.groupNames?.length ? occurrence.groupNames.join(', ') : 'No group';
  }

  noticedViaText(value: string | null): string {
    return feedbackNoticedViaLabel(value);
  }

  /** `Session 812 · message 4410`. */
  idsText(occurrence: AdminFeedbackOccurrence): string {
    return occurrence.turnUserMessageId != null
      ? `Session ${occurrence.chatSessionId} · message ${occurrence.turnUserMessageId}`
      : `Session ${occurrence.chatSessionId}`;
  }

  /** The invisible characters in an issue's subject and summary, as `U+XXXX`. */
  issueHiddenCharacters(issue: AdminFeedbackIssue): string[] {
    return hiddenCharactersOf(issue.subject, issue.summary);
  }

  /** The invisible characters in the texts an occurrence card shows, as `U+XXXX`. */
  occurrenceHiddenCharacters(occurrence: AdminFeedbackOccurrence): string[] {
    return hiddenCharactersOf(occurrence.subject, occurrence.summary, occurrence.details, occurrence.searchedFor,
      occurrence.expectedLocation, occurrence.suspectedCauseNote, ...occurrence.relatedTools);
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

  // --- Download ---

  /**
   * Downloads one issue as a YAML issues file with the remembered export options, and says so on the card's
   * result line. An issue the server no longer has is said so, and the list is fetched again. Ignored while
   * a download runs.
   */
  downloadIssue(issue: AdminFeedbackIssue): void {
    if (this.downloadingId() !== null) return;
    const options = this.exportOptions ?? DEFAULT_FEEDBACK_EXPORT_OPTIONS;
    this.downloadingId.set(issue.id);
    this.setTriageResult(issue.id, { message: '', error: null });
    this.downloadSub?.unsubscribe();
    this.downloadSub = this.transfer.exportIssues({
      source: 'overseer', format: 'yaml', issueIds: [issue.id], occurrencesPerIssue: options.occurrencesPerIssue
    }).subscribe({
      next: result => {
        this.downloadingId.set(null);
        downloadTextFile(result.fileName, result.content, result.mediaType);
        this.setTriageResult(issue.id, { message: `Downloaded ${result.fileName}.`, error: null });
        this.cdr.markForCheck();
      },
      error: err => {
        this.downloadingId.set(null);
        if (feedbackTransferStatus(err) === 404) {
          this.setTriageResult(issue.id, { message: 'This issue no longer exists.', error: null });
          this.reloadIssues();
          this.pruneSelection();
          this.notice.set(`The issue “${issue.subject}” no longer exists.`);
        } else {
          this.setTriageResult(issue.id, { message: 'The issue could not be downloaded: ' + adminApiError(err).message, error: null });
        }
        this.cdr.markForCheck();
      }
    });
  }

  // --- Triage ---

  triageResult(issueId: number): TriageResult | undefined {
    return this.triageResults()[issueId];
  }

  /** Opens the triage dialog on the issue's stored triage; it saves through `saveTriage`. */
  editTriage(issue: AdminFeedbackIssue): void {
    if (this.savingId() !== null || this.deletingId() !== null) return;
    this.triageDialog.open({
      idPrefix: `tl-fb-${issue.id}-triage`,
      subject: issue.subject,
      details: [
        feedbackCategoryLabel(issue.category),
        `Status: ${feedbackStatusLabel(issue.status)}`,
        issue.occurrenceCount === 1 ? '1 occurrence' : `${formatInteger(issue.occurrenceCount)} occurrences`
      ],
      values: FeedbackOverseerPanelComponent.triageValuesOf(issue),
      statuses: ADMIN_FEEDBACK_TRIAGE_STATUSES.map(status => ({ value: status, label: feedbackStatusLabel(status) })),
      statusHelp: 'A resolved issue that recurs becomes Regressed; a dismissed one stays dismissed.',
      limits: { note: FEEDBACK_NOTE_MAX, resolution: FEEDBACK_RESOLUTION_MAX, fixedInVersion: FEEDBACK_FIXED_IN_VERSION_MAX },
      fixedInVersionPattern: ADMIN_FEEDBACK_FIXED_IN_VERSION_PATTERN,
      fixedInVersionError: `Fixed in version takes letters, digits, periods, plus and minus signs only, at most ${FEEDBACK_FIXED_IN_VERSION_MAX} characters.`,
      submit: values => this.saveTriage(issue, values)
    });
  }

  /**
   * Saves the status, every text field that differs from the stored one and Resolved via when the dialog
   * changed it: a field left as it was is kept, an emptied one is cleared. The card takes the server's copy,
   * the result is announced and the counts refetched. A failure reaches the dialog as an error with the
   * message to show.
   */
  saveTriage(issue: AdminFeedbackIssue, values: FeedbackTriageSubmission): Observable<AdminFeedbackIssue> {
    return defer(() => {
      const status = values.status as AdminFeedbackTriageStatus;
      const changes: AdminFeedbackTriageChanges = {};
      if (values.note !== (issue.triageNote ?? '')) changes.note = values.note;
      if (values.resolution !== (issue.resolution ?? '')) changes.resolution = values.resolution;
      if (values.fixedInVersion !== (issue.fixedInVersion ?? '')) changes.fixedInVersion = values.fixedInVersion;
      if (values.resolvedVia !== undefined) changes.resolvedVia = [...values.resolvedVia];
      this.savingId.set(issue.id);
      this.setTriageResult(issue.id, { message: '', error: null });
      return this.feedback.updateStatus(issue.id, status, changes).pipe(
        map((updated): AdminFeedbackIssue => updated && updated.id === issue.id
          ? updated
          : {
            ...issue, status, triagedUtc: new Date().toISOString(),
            triageNote: changes.note !== undefined ? changes.note || null : issue.triageNote,
            resolution: changes.resolution !== undefined ? changes.resolution || null : issue.resolution,
            fixedInVersion: changes.fixedInVersion !== undefined ? changes.fixedInVersion || null : issue.fixedInVersion,
            resolvedVia: changes.resolvedVia ? [...changes.resolvedVia] : issue.resolvedVia
          }),
        tap(saved => {
          this.rows.set(this.rows().map(i => i.id === issue.id ? saved : i));
          this.list.invalidate();
          this.setTriageResult(issue.id, { message: `Saved. The issue is ${feedbackStatusLabel(saved.status)}.`, error: null });
          this.store.loadFeedbackCounts();
        }),
        catchError(err => throwError(() => new Error('The issue could not be saved: ' + adminApiError(err).message))),
        finalize(() => this.savingId.set(null))
      );
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
        this.totalCount.set(Math.max(0, this.totalCount() - 1));
        this.table.setRemoteTotal(this.totalCount());
        this.list.invalidate();
        if (this.selected().has(issue.id)) {
          this.setSelection(new Set([...this.selected()].filter(id => id !== issue.id)), false);
        }
        this.notice.set(`Deleted the issue “${issue.subject}”.`);
        this.store.loadFeedbackCounts();
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

  /** The stored triage as the dialog takes it; a regressed issue has no status until one is chosen. */
  private static triageValuesOf(issue: AdminFeedbackIssue): FeedbackTriageValues {
    return {
      status: issue.status === 'regressed' ? '' : issue.status,
      note: issue.triageNote ?? '',
      resolution: issue.resolution ?? '',
      fixedInVersion: issue.fixedInVersion ?? '',
      resolvedVia: issue.resolvedVia ?? []
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

/** Numeric collation, case-insensitive: `gpt-4` before `gpt-10`. */
function compareText(a: string, b: string): number {
  return a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' });
}
