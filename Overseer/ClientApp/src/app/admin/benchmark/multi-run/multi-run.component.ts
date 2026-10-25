import {
  ChangeDetectionStrategy,
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
  inject
} from '@angular/core';
import {
  AdminBenchmarkService,
  BenchmarkRunGroupDto,
  BenchmarkRunGroupSuggestionDto,
  BenchmarkSuiteDto
} from '../../../services/admin-benchmark.service';
import { CardListChip, CardListFacet, CardListSort, CardListState } from '../../../shared/data-table/card-list-state';
import { FilterFacetComponent } from '../../../shared/data-table/filter-facet.component';
import { TableState, anyOfFilter, customFilter } from '../../../shared/data-table/table-state';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { ModelIdentityComponent } from '../../../shared/model-identity/model-identity.component';
import { parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { formatCostAmount } from '../benchmark-run-format';
import { httpErrorText } from '../batteries/battery.models';
import {
  groupRunsLabel,
  isGeneratedGroupName,
  runGroupAnalysisState,
  runGroupAnalysisStateLabel,
  runGroupTierClass,
  runGroupTierName,
  runGroupTierOrder
} from '../shared/run-group-format';
import { GroupAnalysisDialogComponent } from './group-analysis-dialog/group-analysis-dialog.component';
import { GroupBuilderDialogComponent } from './group-builder-dialog/group-builder-dialog.component';

/** The page's own suite, per browser, as `{ version: 1, suiteId }`. */
export const MULTI_RUN_VIEW_STORAGE_KEY = 'overseer.benchmark.multiRun.view';

/** The dismissed suggestions, per browser, as `{ version: 1, keys }`. */
export const MULTI_RUN_DISMISSED_STORAGE_KEY = 'overseer.benchmark.multiRun.dismissedSuggestions';

/** The group list's Sort by order, as `CardListState` stores it. */
export const MULTI_RUN_GROUPS_VIEW_STORAGE_KEY = 'overseer.benchmark.multiRun.groups.view';

/** The most dismissals kept; the oldest are dropped first. */
export const MULTI_RUN_DISMISSED_LIMIT = 200;

/** The filter bar is shown above this many groups, and while a filter is still active. */
export const MULTI_RUN_FILTER_BAR_MIN_EXCLUSIVE = 3;

const STORAGE_VERSION = 1;

export const MULTI_RUN_GROUP_SORTS: readonly CardListSort[] = [
  { id: 'newest', label: 'Newest first', column: 'created', direction: 'desc' },
  { id: 'oldest', label: 'Oldest first', column: 'created', direction: 'asc' },
  { id: 'runs', label: 'Most runs', column: 'runs', direction: 'desc' },
  { id: 'index', label: 'Highest index', column: 'index', direction: 'desc' },
  { id: 'tier', label: 'Tier (A first)', column: 'tier', direction: 'desc' },
  { id: 'model', label: 'Model (A–Z)', column: 'model', direction: 'asc' }
];

const TIER_FACET_ORDER = ['Replicate', 'QualityComparable', 'CrossCondition', 'NotComparable'].map(runGroupTierName);
const ANALYSIS_FACET_ORDER = (['current', 'stale', 'none'] as const).map(runGroupAnalysisStateLabel);
const ORIGIN_FACET_ORDER = ['Series', 'Other'];

/**
 * A suggestion's dismissal key: its comparability key hash and its run ids, so a cluster that
 * gains a run is a new suggestion and shows again.
 */
export function suggestionKey(suggestion: Pick<BenchmarkRunGroupSuggestionDto, 'keyHash' | 'runIds'>): string {
  return `${suggestion.keyHash}:${suggestion.runIds.join(',')}`;
}

/** The group's candidate as its card title names it. */
export function groupModelName(group: BenchmarkRunGroupDto): string {
  return group.testedModelDisplayName || group.testedModelId || 'Model not recorded';
}

/** The member run ids, ascending. */
export function groupMemberRunIds(group: BenchmarkRunGroupDto): number[] {
  if (group.memberRunIds?.length) {
    return group.memberRunIds;
  }
  return (group.members ?? []).map(member => member.runId).sort((a, b) => a - b);
}

/** Where a group came from, as the Origin facet lists it. */
function groupOrigin(group: BenchmarkRunGroupDto): string {
  return group.createdFromSeriesId != null ? 'Series' : 'Other';
}

/** The stored name, when someone typed it and it says more than the card already does. */
export function groupCustomName(group: BenchmarkRunGroupDto): string | null {
  if (group.nameIsGenerated !== false || !group.name?.trim()) {
    return null;
  }
  return isGeneratedGroupName(group.name, group.suiteName, group.testedModelDisplayName) ? null : group.name.trim();
}

/** The text the search matches: `#id`, the model, the run ids, the note and a custom name. */
function groupSearchText(group: BenchmarkRunGroupDto): string {
  return [
    `#${group.id}`,
    groupModelName(group),
    groupRunsLabel(groupMemberRunIds(group)),
    group.notes ?? '',
    groupCustomName(group) ?? ''
  ].join('\n').toLowerCase();
}

function timeOf(value: string | null | undefined): number | null {
  if (!value) return null;
  const ms = parseServerUtcDate(value).getTime();
  return Number.isNaN(ms) ? null : ms;
}

/**
 * GnollBench → Multi-Run Analysis: the analysis groups of one suite as a card list, the suite's
 * replicate clusters that no group holds yet as suggested groups, and the header that chooses the
 * suite and opens the New group dialog.
 *
 * The page owns its suite, remembered per browser; the Run Benchmark launcher's suite is only the
 * fallback. A group's analysis opens in `app-group-analysis-dialog`, and manual group building is
 * `app-group-builder-dialog`.
 */
@Component({
  selector: 'app-benchmark-multi-run',
  standalone: true,
  imports: [FilterFacetComponent, InfoTipComponent, ModelIdentityComponent, GroupBuilderDialogComponent, GroupAnalysisDialogComponent],
  templateUrl: './multi-run.component.html',
  styleUrls: ['./multi-run.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class MultiRunComponent implements OnInit, OnChanges, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** The suites the Suite select offers. */
  @Input() suites: BenchmarkSuiteDto[] = [];

  /** The suite shown when this browser has none remembered: the Run Benchmark launcher's. */
  @Input() defaultSuiteId: number | null = null;

  /** A completed series the host opens its progress dialog for. */
  @Output() openSeries = new EventEmitter<number>();

  /** A run whose report the host opens. */
  @Output() openRun = new EventEmitter<number>();

  @ViewChild(GroupBuilderDialogComponent) builderDialog?: GroupBuilderDialogComponent;
  @ViewChild(GroupAnalysisDialogComponent) analysisDialog?: GroupAnalysisDialogComponent;

  readonly sorts = MULTI_RUN_GROUP_SORTS;
  readonly tierName = runGroupTierName;
  readonly tierClass = runGroupTierClass;
  readonly analysisState = runGroupAnalysisState;
  readonly analysisStateLabel = runGroupAnalysisStateLabel;
  readonly runsLabel = groupRunsLabel;
  readonly modelName = groupModelName;
  readonly memberRunIds = groupMemberRunIds;
  readonly customName = groupCustomName;
  readonly suggestionKey = suggestionKey;
  readonly staleTooltip = 'The membership changed, or a member was re-run, after this analysis was computed. Re-analyze to bring it up to date.';
  readonly definitionChangedTooltip = 'The comparability key definition changed after this group was analyzed. Re-analyze to refresh its key hash.';

  selectedSuiteId: number | null = null;

  groups: BenchmarkRunGroupDto[] = [];
  loadingGroups = false;
  groupsError: string | null = null;

  suggestions: BenchmarkRunGroupSuggestionDto[] = [];
  loadingSuggestions = false;
  suggestionsError: string | null = null;
  /** The suggestion whose Create group request is on its way. */
  creatingKey: string | null = null;
  /** A refused or failed Create group, by suggestion key. */
  suggestionErrors: Readonly<Record<string, string>> = {};

  /** The group whose inline delete confirmation is shown. */
  confirmDeleteId: number | null = null;
  deletingId: number | null = null;
  deleteError: { groupId: number; message: string } | null = null;

  /** What the last action did, read first in the status line until the list changes again. */
  announcement: string | null = null;

  private dismissedKeys: readonly string[] = [];
  private initialized = false;
  private groupsToken = 0;
  private suggestionsToken = 0;
  /** The card whose title takes focus once the reloaded list has rendered. */
  private pendingFocusGroupId: number | null = null;
  private suggestionMemo: {
    suggestions: BenchmarkRunGroupSuggestionDto[];
    dismissed: readonly string[];
    rows: BenchmarkRunGroupSuggestionDto[];
  } | null = null;

  readonly table = new TableState<BenchmarkRunGroupDto>('created', 'desc').registerAccessors(
    {
      created: g => timeOf(g.createdAtUtc),
      runs: g => g.runCount,
      index: g => g.headlineIndex,
      tier: g => runGroupTierOrder(g.tier),
      model: g => groupModelName(g).toLowerCase()
    },
    {
      search: customFilter((g, value) => groupSearchText(g).includes(value.trim().toLowerCase())),
      tier: anyOfFilter(g => runGroupTierName(g.tier)),
      analysis: anyOfFilter(g => runGroupAnalysisStateLabel(runGroupAnalysisState(g))),
      origin: anyOfFilter(g => groupOrigin(g)),
      model: anyOfFilter(g => groupModelName(g))
    }
  );

  /** The card list over `table`: the search, Sort by, the facets, the chips and the batch. */
  readonly list = new CardListState<BenchmarkRunGroupDto>(this.table, {
    idPrefix: 'mr-groups',
    batch: 10,
    sorts: MULTI_RUN_GROUP_SORTS,
    defaultSort: 'newest',
    storageKey: MULTI_RUN_GROUPS_VIEW_STORAGE_KEY,
    facets: [
      { column: 'tier', label: 'Tier', values: g => runGroupTierName(g.tier), order: TIER_FACET_ORDER },
      { column: 'analysis', label: 'Analysis', values: g => runGroupAnalysisStateLabel(runGroupAnalysisState(g)), order: ANALYSIS_FACET_ORDER },
      { column: 'origin', label: 'Origin', values: g => groupOrigin(g), order: ORIGIN_FACET_ORDER },
      { column: 'model', label: 'Model', values: g => groupModelName(g) }
    ],
    onChange: () => {
      this.announcement = null;
      this.cdr.markForCheck();
    }
  });

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.dismissedKeys = this.readDismissed();
    this.selectedSuiteId = this.resolveSuiteId();
    this.initialized = true;
    this.load();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.initialized || !(changes['suites'] || changes['defaultSuiteId'])) {
      return;
    }
    if (this.isKnownSuite(this.selectedSuiteId)) {
      return;
    }
    const resolved = this.resolveSuiteId();
    if (resolved !== this.selectedSuiteId) {
      this.selectedSuiteId = resolved;
      this.resetPage();
      this.load();
    }
  }

  ngOnDestroy(): void {
    this.list.dispose();
  }

  // --- The suite --------------------------------------------------------------------------------

  get selectedSuite(): BenchmarkSuiteDto | null {
    return this.suites.find(suite => suite.id === this.selectedSuiteId) ?? null;
  }

  get suiteName(): string | null {
    return this.selectedSuite?.name ?? this.groups[0]?.suiteName ?? null;
  }

  onSuiteChange(event: Event): void {
    const id = Number((event.target as HTMLSelectElement).value);
    if (Number.isFinite(id)) {
      this.selectSuite(id);
    }
  }

  /** Shows another suite's groups and remembers the choice; the launcher's suite is left as it is. */
  selectSuite(id: number): void {
    if (id === this.selectedSuiteId) return;
    this.selectedSuiteId = id;
    this.writeStoredSuite(id);
    this.resetPage();
    this.load();
  }

  /** The remembered suite, else the launcher's, else the first; a suite not in the list is skipped. */
  private resolveSuiteId(): number | null {
    const stored = this.readStoredSuite();
    if (this.isKnownSuite(stored)) return stored;
    if (this.isKnownSuite(this.defaultSuiteId)) return this.defaultSuiteId;
    return this.suites[0]?.id ?? null;
  }

  /** While the suites are not loaded every id counts as known. */
  private isKnownSuite(id: number | null): id is number {
    return id != null && (this.suites.length === 0 || this.suites.some(suite => suite.id === id));
  }

  private readStoredSuite(): number | null {
    try {
      const raw = localStorage.getItem(MULTI_RUN_VIEW_STORAGE_KEY);
      const parsed = raw ? JSON.parse(raw) as { version?: unknown; suiteId?: unknown } | null : null;
      return parsed && parsed.version === STORAGE_VERSION && typeof parsed.suiteId === 'number' ? parsed.suiteId : null;
    } catch {
      return null;
    }
  }

  private writeStoredSuite(suiteId: number): void {
    try {
      localStorage.setItem(MULTI_RUN_VIEW_STORAGE_KEY, JSON.stringify({ version: STORAGE_VERSION, suiteId }));
    } catch {
      // Storage full or unavailable: the suite is simply not remembered.
    }
  }

  // --- Loading ----------------------------------------------------------------------------------

  get loading(): boolean {
    return this.loadingGroups || this.loadingSuggestions;
  }

  /** Reloads the groups and the suggestions; refused while a load runs. */
  refresh(): void {
    if (this.loading) return;
    this.announcement = null;
    this.load();
  }

  private load(): void {
    this.loadGroups();
    this.loadSuggestions();
  }

  private resetPage(): void {
    this.groups = [];
    this.groupsError = null;
    this.suggestions = [];
    this.suggestionsError = null;
    this.suggestionErrors = {};
    this.confirmDeleteId = null;
    this.deleteError = null;
    this.announcement = null;
    this.pendingFocusGroupId = null;
    this.list.reset();
  }

  loadGroups(): void {
    const suiteId = this.selectedSuiteId;
    const token = ++this.groupsToken;
    if (suiteId == null) {
      this.groups = [];
      this.loadingGroups = false;
      this.cdr.markForCheck();
      return;
    }
    this.loadingGroups = true;
    this.groupsError = null;
    this.cdr.markForCheck();
    this.benchmarkService.getRunGroups().subscribe({
      next: (groups) => {
        if (token !== this.groupsToken) return;
        // A group is bound to one suite by construction, so the list is narrowed here.
        this.groups = (groups ?? []).filter(g => g.benchmarkSuiteId === suiteId);
        this.loadingGroups = false;
        this.focusPendingGroup();
      },
      error: (err) => {
        if (token !== this.groupsToken) return;
        this.loadingGroups = false;
        this.groupsError = httpErrorText(err, 'Failed to load the analysis groups.');
        this.cdr.markForCheck();
      }
    });
  }

  private loadSuggestions(): void {
    const suiteId = this.selectedSuiteId;
    const token = ++this.suggestionsToken;
    if (suiteId == null) {
      this.suggestions = [];
      this.loadingSuggestions = false;
      this.cdr.markForCheck();
      return;
    }
    this.loadingSuggestions = true;
    this.suggestionsError = null;
    this.cdr.markForCheck();
    this.benchmarkService.getRunGroupSuggestions(suiteId).subscribe({
      next: (suggestions) => {
        if (token !== this.suggestionsToken) return;
        this.suggestions = suggestions ?? [];
        this.loadingSuggestions = false;
        this.cdr.markForCheck();
      },
      error: (err) => {
        if (token !== this.suggestionsToken) return;
        this.loadingSuggestions = false;
        this.suggestionsError = httpErrorText(err, 'Failed to load the suggested groups.');
        this.cdr.markForCheck();
      }
    });
  }

  // --- Suggested groups -------------------------------------------------------------------------

  /** The suggestions not dismissed in this browser, kept as one array until either changes. */
  get visibleSuggestions(): BenchmarkRunGroupSuggestionDto[] {
    const memo = this.suggestionMemo;
    if (memo && memo.suggestions === this.suggestions && memo.dismissed === this.dismissedKeys) {
      return memo.rows;
    }
    const dismissed = new Set(this.dismissedKeys);
    const rows = this.suggestions.filter(s => !dismissed.has(suggestionKey(s)));
    this.suggestionMemo = { suggestions: this.suggestions, dismissed: this.dismissedKeys, rows };
    return rows;
  }

  /** The id prefix of a suggestion's elements; run ids are unique across suggestions. */
  suggestionDomId(suggestion: BenchmarkRunGroupSuggestionDto): string {
    return `mr-sugg-${suggestion.runIds.join('-')}`;
  }

  suggestionModelName(suggestion: BenchmarkRunGroupSuggestionDto): string {
    return suggestion.testedModel?.displayName || suggestion.testedModel?.id || 'Model not recorded';
  }

  /** `#41, #42, #43`, for the actions group's name. */
  suggestionRunList(suggestion: BenchmarkRunGroupSuggestionDto): string {
    return suggestion.runIds.map(id => `#${id}`).join(', ');
  }

  /** The one series the runs came from, or null. */
  suggestionSeriesId(suggestion: BenchmarkRunGroupSuggestionDto): number | null {
    return suggestion.seriesIds?.length === 1 ? suggestion.seriesIds[0] : null;
  }

  /** Creates the group without a name; the server names it and analyzes it at once. */
  createFromSuggestion(suggestion: BenchmarkRunGroupSuggestionDto): void {
    const key = suggestionKey(suggestion);
    if (this.creatingKey != null) return;
    this.creatingKey = key;
    this.suggestionErrors = this.withoutKey(this.suggestionErrors, key);
    this.announcement = null;
    this.cdr.markForCheck();
    this.benchmarkService.createRunGroup({ runIds: [...suggestion.runIds] }).subscribe({
      next: (result) => {
        this.creatingKey = null;
        const group = result?.group;
        if (!result?.accepted || !group) {
          this.suggestionErrors = { ...this.suggestionErrors, [key]: result?.error || 'The group was refused.' };
          this.cdr.markForCheck();
          return;
        }
        this.suggestions = this.suggestions.filter(s => suggestionKey(s) !== key);
        this.onGroupCreated(group);
      },
      error: (err) => {
        this.creatingKey = null;
        // A refusal (a duplicate, or keys that differ) is a 400 whose body is the tier preview.
        const refusal = (err as { error?: { error?: unknown } } | null)?.error?.error;
        const message = typeof refusal === 'string' && refusal.trim() !== ''
          ? refusal
          : httpErrorText(err, 'Failed to create the group.');
        this.suggestionErrors = { ...this.suggestionErrors, [key]: message };
        this.cdr.markForCheck();
      }
    });
  }

  /** Hides a suggestion in this browser, then focuses the next suggestion's Create group. */
  dismissSuggestion(suggestion: BenchmarkRunGroupSuggestionDto): void {
    const key = suggestionKey(suggestion);
    const before = this.visibleSuggestions;
    const index = before.findIndex(s => suggestionKey(s) === key);
    this.dismissedKeys = [...this.dismissedKeys.filter(k => k !== key), key].slice(-MULTI_RUN_DISMISSED_LIMIT);
    this.writeDismissed(this.dismissedKeys);
    this.cdr.detectChanges();
    const after = this.visibleSuggestions;
    const next = index >= 0 ? after[index] ?? after[index - 1] : undefined;
    const target = next ? this.element(`#${this.suggestionDomId(next)}-create`) : null;
    (target ?? this.element('#mr-groups-title'))?.focus();
  }

  private readDismissed(): readonly string[] {
    try {
      const raw = localStorage.getItem(MULTI_RUN_DISMISSED_STORAGE_KEY);
      const parsed = raw ? JSON.parse(raw) as { version?: unknown; keys?: unknown } | null : null;
      if (!parsed || parsed.version !== STORAGE_VERSION || !Array.isArray(parsed.keys)) return [];
      return parsed.keys.filter((k): k is string => typeof k === 'string').slice(-MULTI_RUN_DISMISSED_LIMIT);
    } catch {
      return [];
    }
  }

  private writeDismissed(keys: readonly string[]): void {
    try {
      localStorage.setItem(MULTI_RUN_DISMISSED_STORAGE_KEY, JSON.stringify({ version: STORAGE_VERSION, keys }));
    } catch {
      // Storage full or unavailable: the dismissal lasts until the page reloads.
    }
  }

  private withoutKey(errors: Readonly<Record<string, string>>, key: string): Readonly<Record<string, string>> {
    if (!(key in errors)) return errors;
    const copy = { ...errors };
    delete copy[key];
    return copy;
  }

  // --- The group list ---------------------------------------------------------------------------

  get showFilterBar(): boolean {
    return this.groups.length > MULTI_RUN_FILTER_BAR_MIN_EXCLUSIVE || this.table.hasActiveFilters;
  }

  get cards(): BenchmarkRunGroupDto[] {
    return this.list.view(this.groups);
  }

  get facets(): CardListFacet[] {
    return this.list.facets(this.groups);
  }

  get chips(): CardListChip[] {
    return this.list.chips(this.groups);
  }

  get noMatches(): boolean {
    return this.table.noMatches(this.groups);
  }

  get remainingCount(): number {
    return this.list.remainingCount(this.groups);
  }

  get nextBatchCount(): number {
    return this.list.nextBatchCount(this.groups);
  }

  get matchingCount(): number {
    return this.list.matching(this.groups).length;
  }

  /** The list's one polite live region: the last action, then the count. */
  get listStatus(): string {
    const count = this.list.statusText(this.groups, { one: 'group', many: 'groups' });
    return [this.announcement, count].filter(part => !!part).join(' ');
  }

  onSearchInput(event: Event): void {
    this.list.setSearchInput((event.target as HTMLInputElement).value);
  }

  /** Escape with text clears the search at once; in an empty field it passes through. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (this.list.clearSearchOnEscape(event)) {
      this.announcement = null;
      this.cdr.detectChanges();
    }
  }

  onSortChange(event: Event): void {
    if (this.list.setSort((event.target as HTMLSelectElement).value)) {
      this.announcement = null;
      this.cdr.detectChanges();
    }
  }

  onFacetChange(column: string, values: string[]): void {
    this.list.setFacet(column, values);
    this.announcement = null;
    this.cdr.detectChanges();
  }

  /** Removes a chip's filter, then focuses the chip now in its place, else the previous one, else the search. */
  removeChip(chip: CardListChip): void {
    const index = this.list.removeChip(chip, this.groups);
    this.announcement = null;
    this.cdr.detectChanges();
    const chips = Array.from(this.host.nativeElement.querySelectorAll<HTMLButtonElement>('.mr-filter-chips .gh-filter-chip'));
    const target = index >= 0 ? chips[index] ?? chips[index - 1] : undefined;
    (target ?? this.element('#mr-groups-search'))?.focus();
  }

  /** Clears the search and every filter, then focuses the search when it is still shown. */
  clearFilters(): void {
    this.list.clearFilters();
    this.announcement = null;
    this.cdr.detectChanges();
    (this.element('#mr-groups-search') ?? this.element('#mr-groups-title'))?.focus();
  }

  showMore(): void {
    this.focusCard(this.list.showMore(this.groups));
  }

  showAll(): void {
    this.focusCard(this.list.showAll(this.groups));
  }

  /** Renders, then focuses the title of the card at `index` in the view, if there is one. */
  private focusCard(index: number): void {
    this.cdr.detectChanges();
    const group = this.cards[index];
    if (group) {
      this.element(`#mr-group-${group.id}-title`)?.focus();
    }
  }

  /** After a reload that follows a create: the new card's title, shown in full if the batch hid it. */
  private focusPendingGroup(): void {
    const id = this.pendingFocusGroupId;
    this.pendingFocusGroupId = null;
    if (id == null) {
      this.cdr.markForCheck();
      return;
    }
    this.cdr.detectChanges();
    if (!this.cards.some(g => g.id === id) && this.list.matching(this.groups).some(g => g.id === id)) {
      this.list.showAll(this.groups);
      this.cdr.detectChanges();
    }
    (this.element(`#mr-group-${id}-title`) ?? this.element('#mr-groups-title'))?.focus();
  }

  private element(selector: string): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(selector);
  }

  // --- Dialogs ----------------------------------------------------------------------------------

  openBuilder(): void {
    if (this.selectedSuiteId == null) return;
    this.builderDialog?.open();
  }

  /** A group created here or in the builder: reload, focus its card and say what happened. */
  onGroupCreated(group: BenchmarkRunGroupDto): void {
    this.announcement = group.latestAnalysisId != null
      ? `Created group #${group.id} and analyzed it.`
      : `Created group #${group.id}.`;
    this.pendingFocusGroupId = group.id;
    this.load();
  }

  /** The builder named an existing group with the same runs: its analysis opens instead. */
  onBuilderOpenGroup(groupId: number): void {
    this.builderDialog?.close();
    this.openGroupById(groupId);
  }

  openGroup(group: BenchmarkRunGroupDto): void {
    this.analysisDialog?.open(group);
  }

  /**
   * Opens a group's analysis by id, for a caller that has an id and not a card — the series
   * progress dialog's View Report. Falls back to fetching the group when the loaded list does not
   * hold it, which is the ordinary case right after the tab opens or for another suite's group.
   */
  openGroupById(groupId: number): void {
    const listed = this.groups.find(g => g.id === groupId);
    if (listed) {
      this.openGroup(listed);
      return;
    }
    this.benchmarkService.getRunGroup(groupId).subscribe({
      next: (group) => { if (group) this.openGroup(group); },
      error: (err) => {
        this.groupsError = httpErrorText(err, 'Failed to load the group.');
        this.cdr.markForCheck();
      }
    });
  }

  closeGroup(): void {
    this.analysisDialog?.close();
  }

  /** An analysis was computed in the dialog: the cards' states and figures move with it. */
  onAnalysisChanged(): void {
    this.loadGroups();
  }

  viewSeries(group: BenchmarkRunGroupDto): void {
    if (group.createdFromSeriesId == null) return;
    this.openSeries.emit(group.createdFromSeriesId);
  }

  openRunReport(runId: number): void {
    this.openRun.emit(runId);
  }

  // --- A card -----------------------------------------------------------------------------------

  /** The report is built from the stored analysis, so there is nothing to download before one exists. */
  canDownloadReport(group: BenchmarkRunGroupDto): boolean {
    return group.latestAnalysisId != null;
  }

  downloadTooltip(group: BenchmarkRunGroupDto): string {
    if (!this.canDownloadReport(group)) {
      return 'Not analyzed yet: open the analysis and analyze the group first';
    }
    return group.analysisStale ? 'Download Markdown report (the analysis is stale)' : 'Download Markdown report';
  }

  downloadGroupReport(group: BenchmarkRunGroupDto): void {
    if (!this.canDownloadReport(group)) return;
    window.open(this.benchmarkService.getGroupReportUrl(group.id), '_blank');
  }

  /** Shows the inline confirmation and focuses Keep, so Enter cannot delete at once. */
  requestDelete(group: BenchmarkRunGroupDto): void {
    this.confirmDeleteId = group.id;
    this.deleteError = null;
    this.cdr.detectChanges();
    this.element(`#mr-group-${group.id}-keep`)?.focus();
  }

  keepGroup(group: BenchmarkRunGroupDto): void {
    this.confirmDeleteId = null;
    this.deleteError = null;
    this.cdr.detectChanges();
    this.element(`#mr-group-${group.id}-delete`)?.focus();
  }

  /** Deletes the group, keeping its runs; focus goes to the next card, else the previous, else the heading. */
  confirmDelete(group: BenchmarkRunGroupDto): void {
    if (this.deletingId != null) return;
    this.deletingId = group.id;
    this.deleteError = null;
    this.cdr.markForCheck();
    this.benchmarkService.deleteRunGroup(group.id).subscribe({
      next: () => {
        const index = this.cards.findIndex(g => g.id === group.id);
        this.deletingId = null;
        this.confirmDeleteId = null;
        this.groups = this.groups.filter(g => g.id !== group.id);
        this.announcement = `Deleted group #${group.id}. Its runs are kept.`;
        this.focusAfterDelete(index);
        this.loadSuggestions();
      },
      error: (err) => {
        this.deletingId = null;
        this.deleteError = { groupId: group.id, message: httpErrorText(err, 'Failed to delete the group.') };
        this.cdr.markForCheck();
      }
    });
  }

  private focusAfterDelete(index: number): void {
    this.cdr.detectChanges();
    const cards = this.cards;
    const next = index >= 0 ? cards[index] ?? cards[index - 1] : undefined;
    const target = next ? this.element(`#mr-group-${next.id}-title`) : null;
    (target ?? this.element('#mr-groups-title'))?.focus();
  }

  // --- Formatting -------------------------------------------------------------------------------

  /** `2026-09-06 10:00 UTC`, or an em dash. */
  formatUtc(value: string | null | undefined): string {
    const iso = this.isoDate(value);
    return iso ? `${iso.slice(0, 10)} ${iso.slice(11, 16)} UTC` : '—';
  }

  isoDate(value: string | null | undefined): string | null {
    if (!value) return null;
    const date = parseServerUtcDate(value);
    return Number.isNaN(date.getTime()) ? null : date.toISOString();
  }

  formatIndex(value: number | null | undefined): string {
    return value == null || !Number.isFinite(value) ? '—' : value.toFixed(1);
  }

  formatHalfWidth(value: number | null | undefined): string | null {
    return value == null || !Number.isFinite(value) ? null : `± ${value.toFixed(1)}`;
  }

  formatCost(value: number | null | undefined): string {
    return formatCostAmount(value, '—');
  }
}
