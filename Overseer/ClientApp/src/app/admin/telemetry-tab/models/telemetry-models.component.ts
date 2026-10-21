import { ChangeDetectorRef, Component, ElementRef, OnDestroy, inject, input, output } from '@angular/core';
import { NgClass, NgTemplateOutlet } from '@angular/common';
import {
  AiTelemetryKeySource,
  AiTelemetrySummaryDto,
  AiUsageBreakdownRowDto,
  AiUsageRoleGroup
} from '../../../services/admin.service';
import { ProviderBadgeComponent } from '../../../shared/provider-badge/provider-badge.component';
import {
  ModelPickerMatchPart,
  ModelPickerProviderFacet,
  matchParts,
  searchTerms
} from '../../../shared/model-picker/model-picker-view';
import { refreshAnchorPositioning } from '../../../utils/polyfills.util';
import {
  formatCount,
  formatInteger,
  formatOneIn,
  formatPercent
} from '../../benchmark/shared/provider-availability/provider-availability';
import { TelemetryModelUsersRequest } from '../model-users/telemetry-model-users-dialog.component';
import {
  BREAKDOWN_SORT_FIELDS,
  Breakdown,
  BreakdownCard,
  BreakdownMetrics,
  BreakdownSort,
  BreakdownSortDirection,
  BreakdownSortField,
  DEFAULT_BREAKDOWN_SORT,
  NAMED_ROLE_GROUPS,
  TelemetryBreakdownView,
  TelemetryCardBadge,
  TelemetryMetricText,
  breakdownDirectionLabel,
  breakdownProviderFacets,
  breakdownSortField,
  breakdownSortLabel,
  buildBreakdown,
  cardTitle,
  costMetricText,
  defaultBreakdownView,
  effectiveBreakdownProviders,
  isDefaultBreakdownSort,
  roleCounts,
  roleBadgeClass,
  roleGroupLabel,
  thinkingBadgesOf,
  thinkingLevelLabel
} from '../telemetry-breakdown';

/** The applied search follows the field after this much quiet, so the status line is not announced per keystroke. */
const SEARCH_DEBOUNCE_MS = 200;

const NO_ROWS: readonly AiUsageBreakdownRowDto[] = [];

/** The model card whose usage by user was asked for, and the button to return focus to. */
export interface TelemetryModelUsersRef {
  request: TelemetryModelUsersRequest;
  opener: HTMLElement | null;
}

/**
 * The AI Telemetry tab's Models panel: usage by key source, user, model, role and (optionally)
 * thinking level, as a card list with a search, provider toggles, role checkboxes and a Sort
 * popover. The tab owns and stores the view settings; the panel shows them and emits the changed
 * view. The search is the panel's own and is never stored.
 */
@Component({
  selector: 'app-telemetry-models',
  imports: [NgClass, NgTemplateOutlet, ProviderBadgeComponent],
  templateUrl: './telemetry-models.component.html',
  styleUrl: './telemetry-models.component.scss'
})
export class TelemetryModelsComponent implements OnDestroy {
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly cdr = inject(ChangeDetectorRef);

  /** The tab's summary for its range and Keys filter; null until the first load. */
  readonly summary = input<AiTelemetrySummaryDto | null>(null);
  /** The tab's Keys filter. */
  readonly keys = input<AiTelemetryKeySource>('system');
  /** The tab's date range, "YYYY-MM-DD"; empty for no bound. */
  readonly start = input<string>('');
  readonly end = input<string>('');
  /** The breakdown's view settings, which the tab stores. */
  readonly view = input<TelemetryBreakdownView>(defaultBreakdownView());

  /** The view settings changed: roles, thinking levels, provider toggles or sort. */
  readonly viewChange = output<TelemetryBreakdownView>();
  /** Usage by user was pressed on a model card. */
  readonly usageByUser = output<TelemetryModelUsersRef>();

  readonly sortFields = BREAKDOWN_SORT_FIELDS;
  readonly sortDirections: readonly BreakdownSortDirection[] = ['desc', 'asc'];
  readonly roleGroupLabel = roleGroupLabel;
  readonly formatInteger = formatInteger;

  /** The breakdown's search field as typed. */
  searchInput = '';
  /** The search the breakdown applies; follows `searchInput` after a short quiet. Never stored. */
  search = '';
  /** Whether the Sort popover is open; the popover polyfill does not set `aria-expanded`. */
  sortOpen = false;

  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  private breakdownMemo: {
    rows: readonly AiUsageBreakdownRowDto[];
    roles: readonly AiUsageRoleGroup[];
    separate: boolean;
    search: string;
    providers: readonly string[];
    sort: BreakdownSort;
    breakdown: Breakdown;
  } | null = null;
  private facetsMemo: { all: readonly BreakdownCard[]; facets: ModelPickerProviderFacet[] } | null = null;
  private roleCountsMemo: { rows: readonly AiUsageBreakdownRowDto[]; counts: Record<AiUsageRoleGroup, number> } | null = null;
  private matchMemo = new WeakMap<BreakdownCard, { search: string; title: ModelPickerMatchPart[]; modelId: ModelPickerMatchPart[] }>();
  private badgeMemo = new WeakMap<BreakdownCard, TelemetryCardBadge[]>();

  ngOnDestroy() {
    this.cancelSearchTimer();
  }

  // --- Usage by user ---

  /** `Claude X · Chat`, with the level under Show separately and the key badge's text where there is one. */
  cardLabel(card: BreakdownCard): string {
    const parts = [cardTitle(card), roleGroupLabel(card.roleGroup)];
    if (card.thinkingLevel !== undefined) {
      parts.push(thinkingLevelLabel(card.thinkingLevel));
    }
    const key = this.keyLabel(card);
    if (key) {
      parts.push(key);
    }
    return parts.join(' · ');
  }

  /** Asks for the Usage by user dialog for the card's model, role group, key source and level, over the tab's range. */
  openModelUsers(card: BreakdownCard, event?: Event) {
    this.usageByUser.emit({
      request: {
        start: this.start(),
        end: this.end(),
        keySource: card.keySource,
        provider: card.provider,
        modelId: card.modelId,
        modelTitle: cardTitle(card),
        roleGroup: card.roleGroup,
        thinkingLevel: card.thinkingLevel
      },
      opener: (event?.currentTarget as HTMLElement | null) ?? null
    });
  }

  // --- Breakdown ---

  /** Requests per role group over every row of the summary. */
  get roleRequestCounts(): Record<AiUsageRoleGroup, number> {
    const rows = this.summary()?.rows ?? NO_ROWS;
    if (this.roleCountsMemo?.rows !== rows) {
      this.roleCountsMemo = { rows, counts: roleCounts(rows) };
    }
    return this.roleCountsMemo.counts;
  }

  /** The role checkboxes: the named groups, and Other while it has rows. */
  get roleOptions(): AiUsageRoleGroup[] {
    const rows = this.summary()?.rows ?? NO_ROWS;
    const groups: AiUsageRoleGroup[] = [...NAMED_ROLE_GROUPS];
    if (rows.some(r => r.roleGroup === 'other')) {
      groups.push('other');
    }
    return groups;
  }

  get hasBreakdownRows(): boolean {
    return (this.summary()?.rows ?? NO_ROWS).length > 0;
  }

  /**
   * The cards and their total; memoized on the rows, the view settings and the applied search.
   * A changed view carries a new providers array or sort object, so their identity marks a change.
   */
  get breakdown(): Breakdown {
    const rows = this.summary()?.rows ?? NO_ROWS;
    const { roles, separateThinkingLevels: separate, providers, sort } = this.view();
    const search = this.search;
    const memo = this.breakdownMemo;
    if (memo && memo.rows === rows && memo.roles === roles && memo.separate === separate
      && memo.search === search && memo.providers === providers && memo.sort === sort) {
      return memo.breakdown;
    }
    const breakdown = buildBreakdown(rows, { roles, separateThinkingLevels: separate, search, providers, sort });
    this.breakdownMemo = { rows, roles, separate, search, providers, sort, breakdown };
    return breakdown;
  }

  /** One provider toggle per provider of the selected roles' cards, counting cards whatever the search. */
  get providerFacets(): ModelPickerProviderFacet[] {
    const all = this.breakdown.all;
    if (this.facetsMemo?.all !== all) {
      this.facetsMemo = { all, facets: breakdownProviderFacets(all) };
    }
    return this.facetsMemo.facets;
  }

  /** The pressed providers that some card has. */
  get effectiveProviders(): string[] {
    return effectiveBreakdownProviders(this.view().providers, this.providerFacets);
  }

  /** The applied search or a provider filter narrows the cards. */
  get breakdownFiltered(): boolean {
    return this.search.trim() !== '' || this.effectiveProviders.length > 0;
  }

  /** Clear filters is offered while the field has text or a provider toggle is pressed. */
  get breakdownFiltersActive(): boolean {
    return this.searchInput.trim() !== '' || this.breakdownFiltered;
  }

  /**
   * `Showing 4 entries · 1,200 of 1,500 requests`; filtered, `Showing 4 of 13 entries · 1,200 of
   * 5,270 requests`, or `No entries match "luna".`; empty with nothing to show.
   */
  get breakdownStatus(): string {
    if (!this.hasBreakdownRows) {
      return '';
    }
    const { all, cards, total } = this.breakdown;
    if (all.length === 0) {
      return '';
    }
    const allRequests = (this.summary()?.rows ?? NO_ROWS).reduce((sum, r) => sum + r.requests, 0);
    if (this.breakdownFiltered) {
      if (cards.length === 0) {
        const search = this.search.trim();
        return search && this.effectiveProviders.length === 0 ? `No entries match "${search}".` : 'No entries match the filters.';
      }
      return `Showing ${cards.length} of ${all.length} ${all.length === 1 ? 'entry' : 'entries'}`
        + ` · ${formatInteger(total.requests)} of ${formatInteger(allRequests)} requests`;
    }
    const entries = cards.length === 1 ? 'One entry' : `Showing ${cards.length} entries`;
    const requests = total.requests === allRequests
      ? `${formatInteger(allRequests)} ${allRequests === 1 ? 'request' : 'requests'}`
      : `${formatInteger(total.requests)} of ${formatInteger(allRequests)} requests`;
    return `${entries} · ${requests}`;
  }

  // --- Breakdown search and provider filter ---

  onSearchInput(event: Event) {
    this.searchInput = (event.target as HTMLInputElement).value;
    this.cancelSearchTimer();
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.search = this.searchInput;
      this.cdr.markForCheck();
    }, SEARCH_DEBOUNCE_MS);
  }

  /** Escape with text clears the search at once; with none, Escape is left alone. */
  onSearchKeydown(event: KeyboardEvent) {
    if (event.key !== 'Escape' || this.searchInput === '') {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.clearSearch();
  }

  isProviderPressed(facet: ModelPickerProviderFacet): boolean {
    return this.view().providers.includes(facet.key);
  }

  /** Presses or releases a provider toggle. */
  toggleProvider(facet: ModelPickerProviderFacet) {
    const view = this.view();
    const providers = this.isProviderPressed(facet)
      ? view.providers.filter(p => p !== facet.key)
      : [...view.providers, facet.key];
    this.changeView({ ...view, providers });
  }

  /** Clears the search and the provider toggles, not the roles, the thinking setting or the sort, and focuses the search field. */
  clearBreakdownFilters() {
    this.clearSearch();
    const view = this.view();
    if (view.providers.length > 0) {
      this.changeView({ ...view, providers: [] });
    }
    this.host.nativeElement.querySelector<HTMLInputElement>('#tl-breakdown-search')?.focus();
  }

  private clearSearch() {
    this.cancelSearchTimer();
    this.searchInput = '';
    this.search = '';
  }

  private cancelSearchTimer() {
    if (this.searchTimer !== null) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }
  }

  // --- Breakdown sort ---

  /** `Requests, highest first`. */
  get sortLabel(): string {
    return breakdownSortLabel(this.view().sort);
  }

  get sortIsDefault(): boolean {
    return isDefaultBreakdownSort(this.view().sort);
  }

  /** `Highest first` or `A–Z`, by the kind of the chosen field. */
  directionLabel(direction: BreakdownSortDirection): string {
    return breakdownDirectionLabel(breakdownSortField(this.view().sort.field).kind, direction);
  }

  /** Applies at once and keeps the direction; the popover stays open. */
  chooseSortField(field: BreakdownSortField) {
    const view = this.view();
    if (field !== view.sort.field) {
      this.changeView({ ...view, sort: { field, direction: view.sort.direction } });
    }
  }

  chooseSortDirection(direction: BreakdownSortDirection) {
    const view = this.view();
    if (direction !== view.sort.direction) {
      this.changeView({ ...view, sort: { field: view.sort.field, direction } });
    }
  }

  /** Back to most requests first; refused at the default (the button is aria-disabled), and the button keeps focus. */
  resetSort() {
    if (!this.sortIsDefault) {
      this.changeView({ ...this.view(), sort: DEFAULT_BREAKDOWN_SORT });
    }
  }

  onSortToggle(event: Event) {
    this.sortOpen = (event as ToggleEvent).newState === 'open';
    const panel = this.sortPanel();
    if (this.sortOpen) {
      refreshAnchorPositioning();
      (panel?.querySelector<HTMLInputElement>('input:checked') ?? panel?.querySelector<HTMLInputElement>('input'))?.focus();
      return;
    }
    const active = document.activeElement;
    if (!active || active === document.body || !!panel?.contains(active)) {
      this.sortTrigger()?.focus();
    }
  }

  /** Escape closes the Sort popover only. */
  onSortPanelKeydown(event: KeyboardEvent) {
    if (event.key !== 'Escape') {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    try {
      this.sortPanel()?.hidePopover();
    } catch {
      // Already hidden.
    }
    this.sortTrigger()?.focus();
  }

  private sortPanel(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>('#tl-sort-panel');
  }

  private sortTrigger(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>('#tl-sort-trigger');
  }

  // --- Breakdown cards ---

  /** The title split into runs that match the applied search and runs that do not. */
  titleParts(card: BreakdownCard): ModelPickerMatchPart[] {
    return this.matchesOf(card).title;
  }

  modelIdParts(card: BreakdownCard): ModelPickerMatchPart[] {
    return this.matchesOf(card).modelId;
  }

  private matchesOf(card: BreakdownCard) {
    let memo = this.matchMemo.get(card);
    if (!memo || memo.search !== this.search) {
      const terms = searchTerms(this.search);
      memo = { search: this.search, title: matchParts(cardTitle(card), terms), modelId: matchParts(card.modelId, terms) };
      this.matchMemo.set(card, memo);
    }
    return memo;
  }

  roleBadgeClass(group: AiUsageRoleGroup): string {
    return roleBadgeClass(group);
  }

  /**
   * Shown separately, the card's one level, or None sent or Not recorded in a neutral badge, since
   * those are not levels; grouped, one badge per recorded level, none for the others.
   */
  thinkingBadges(card: BreakdownCard): TelemetryCardBadge[] {
    let badges = this.badgeMemo.get(card);
    if (!badges) {
      badges = thinkingBadgesOf(card);
      this.badgeMemo.set(card, badges);
    }
    return badges;
  }

  isRoleSelected(group: AiUsageRoleGroup): boolean {
    return this.view().roles.includes(group);
  }

  /** Adds or removes a role group from the breakdown. */
  toggleRole(group: AiUsageRoleGroup, event: Event) {
    const checked = (event.target as HTMLInputElement).checked;
    const view = this.view();
    const roles = checked
      ? [...view.roles.filter(r => r !== group), group]
      : view.roles.filter(r => r !== group);
    this.changeView({ ...view, roles });
  }

  onThinkingChange(event: Event) {
    const separate = (event.target as HTMLSelectElement).value === 'separate';
    const view = this.view();
    if (separate !== view.separateThinkingLevels) {
      this.changeView({ ...view, separateThinkingLevels: separate });
    }
  }

  /** The key badge: `User key: alice` for a user's key, `System key` under Both. */
  keyLabel(card: BreakdownCard): string | null {
    if (card.keySource === 'user') {
      return `User key: ${card.userName || card.userId || 'unknown user'}`;
    }
    return this.keys() === 'both' ? 'System key' : null;
  }

  cacheHitText(m: BreakdownMetrics): string {
    return m.cacheHitRatio != null ? formatPercent(m.cacheHitRatio) : '—';
  }

  durationText(m: BreakdownMetrics): string {
    return m.avgDurationMs != null ? `${formatInteger(Math.round(m.avgDurationMs))} ms` : '—';
  }

  /** The estimated cost, with `3 unpriced` while part of the requests has no price. */
  costMetric(m: BreakdownMetrics): TelemetryMetricText {
    return costMetricText(m);
  }

  /**
   * Provider errors of a Chat or Title Generation card: the rate with `k of n attempts` and
   * `about 1 in N`; `No attempts` at none. Benchmark availability is measured in GnollBench.
   */
  providerErrorsMetric(m: BreakdownMetrics, role: AiUsageRoleGroup | 'total'): TelemetryMetricText {
    if (role === 'benchmark' || (role === 'total' && this.breakdown.cards.every(c => c.roleGroup === 'benchmark'))) {
      return { value: 'In GnollBench', note: null, muted: true };
    }
    if (m.validAttempts <= 0) {
      return { value: 'No attempts', note: null, muted: true };
    }
    const oneIn = formatOneIn(m.providerErrors, m.validAttempts);
    const count = formatCount(m.providerErrors, m.validAttempts);
    return { value: formatPercent(m.providerErrors / m.validAttempts), note: oneIn ? `${count} · ${oneIn}` : count, muted: false };
  }

  /** Hands the changed view to the tab, which stores it and passes it back. */
  private changeView(view: TelemetryBreakdownView) {
    this.viewChange.emit(view);
  }
}
