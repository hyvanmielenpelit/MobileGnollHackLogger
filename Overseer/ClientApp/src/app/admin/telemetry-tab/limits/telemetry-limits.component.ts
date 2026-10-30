import {
  ChangeDetectionStrategy,
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
  signal
} from '@angular/core';
import { Subscription } from 'rxjs';
import {
  AdminService,
  AiTelemetryKeySource,
  ChatTurnLimitEventSummary,
  ChatTurnLimitEventsQuery,
  ChatTurnLimitSummary
} from '../../../services/admin.service';
import { CardListChip, CardListSort, CardListState } from '../../../shared/data-table/card-list-state';
import { FilterFacetComponent, FilterFacetOption } from '../../../shared/data-table/filter-facet.component';
import { TableState, anyOfFilter, customFilter } from '../../../shared/data-table/table-state';
import { parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { AdminPageStore } from '../../admin-page.store';
import { formatInteger } from '../../benchmark/shared/provider-availability/provider-availability';
import { formatDuration } from '../../devtools-tab/shared/devtools-format';
import { securityTimeText } from '../../security-tab/security-event-archive';
import { TelemetryLimitDialogComponent } from './telemetry-limit-dialog.component';
import {
  TURN_LIMIT_CODES,
  TURN_LIMIT_SEVERITIES,
  limitCodeLabel,
  limitModelText,
  limitReferenceText,
  limitSeverityLabel,
  limitStoppedAtText,
  limitUserLabel
} from './telemetry-limit-labels';

/** Where the list's view settings are kept, per browser. */
export const TELEMETRY_LIMITS_VIEW_STORAGE_KEY = 'overseer.admin.telemetry.limits.view';

/** The events the list fetches at first, and how many more Show more fetches. */
export const TELEMETRY_LIMITS_BATCH = 20;

const NO_VALUES: readonly string[] = [];

/** The list has one order: the server's, newest first. */
const LIMIT_EVENT_SORTS: readonly CardListSort[] = [
  { id: 'newest', label: 'Newest first', column: 'occurred', direction: 'desc' }
];

/** The rows of the summary's stat list. */
interface LimitStat {
  id: string;
  title: string;
  value: string;
}

/**
 * The AI Telemetry tab's Limits view: the limits and provider failures that ended or cut chat turns, on the tab's date
 * range and Keys filter. Totals and a table by limit, then a server-paged card list with a user-name search and Reason
 * and Severity facets. Each card opens its details, where the prompt can be read, and the user's AI usage.
 */
@Component({
  selector: 'app-telemetry-limits',
  imports: [FilterFacetComponent, TelemetryLimitDialogComponent],
  templateUrl: './telemetry-limits.component.html',
  styleUrl: './telemetry-limits.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TelemetryLimitsComponent implements OnInit, OnChanges, OnDestroy {
  private readonly adminService = inject(AdminService);
  private readonly store = inject(AdminPageStore);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly cdr = inject(ChangeDetectorRef);

  /** The tab's date range, "YYYY-MM-DD"; empty for no bound. */
  readonly startDate = input<string>('');
  readonly endDate = input<string>('');
  /** The tab's Keys filter. */
  readonly keys = input<AiTelemetryKeySource>('system');

  @ViewChild('eventDialog') eventDialog?: TelemetryLimitDialogComponent;

  readonly codeLabel = limitCodeLabel;
  readonly userLabel = limitUserLabel;
  readonly modelText = limitModelText;
  readonly stoppedAtText = limitStoppedAtText;
  readonly referenceText = limitReferenceText;
  readonly durationText = formatDuration;
  readonly timeText = securityTimeText;
  readonly formatInteger = formatInteger;

  readonly summary = signal<ChatTurnLimitSummary | null>(null);
  readonly summaryError = signal<string | null>(null);
  /** The events fetched so far, newest first. */
  readonly events = signal<ChatTurnLimitEventSummary[]>([]);
  readonly totalCount = signal(0);
  readonly eventsLoading = signal(false);
  /** Whether the first page of the current query has arrived. */
  readonly eventsLoaded = signal(false);
  readonly eventsError = signal<string | null>(null);

  /** The server filters and pages the events; the table only carries the filter values. */
  readonly table = new TableState<ChatTurnLimitEventSummary>('occurred', 'desc').registerAccessors(
    { occurred: e => parseServerUtcDate(e.occurredUtc).getTime() },
    {
      search: customFilter<ChatTurnLimitEventSummary>(() => true),
      code: anyOfFilter<ChatTurnLimitEventSummary>(e => e.code),
      severity: anyOfFilter<ChatTurnLimitEventSummary>(e => e.severity)
    }
  );

  /** The search, the facets' selections and the chips; every change refetches the first page. */
  readonly list = new CardListState<ChatTurnLimitEventSummary>(this.table, {
    idPrefix: 'tl-lim',
    batch: TELEMETRY_LIMITS_BATCH,
    sorts: LIMIT_EVENT_SORTS,
    defaultSort: 'newest',
    storageKey: TELEMETRY_LIMITS_VIEW_STORAGE_KEY,
    facets: [
      { column: 'code', label: 'Reason', values: e => e.code, order: TURN_LIMIT_CODES, labelOf: limitCodeLabel },
      { column: 'severity', label: 'Severity', values: e => e.severity, order: TURN_LIMIT_SEVERITIES, labelOf: limitSeverityLabel }
    ],
    onChange: () => this.reloadEvents()
  });

  private summarySub: Subscription | null = null;
  private eventsSub: Subscription | null = null;
  private initialized = false;

  private statsMemo: { summary: ChatTurnLimitSummary | null; stats: LimitStat[] } | null = null;
  private codeOptionsMemo: { summary: ChatTurnLimitSummary | null; selected: readonly string[]; options: FilterFacetOption[] } | null = null;
  private severityOptionsMemo: { summary: ChatTurnLimitSummary | null; selected: readonly string[]; options: FilterFacetOption[] } | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.table.setRemoteTotal(0);
    this.initialized = true;
    this.reload();
  }

  ngOnChanges(changes: SimpleChanges): void {
    const changed = (name: string) => !!changes[name] && !changes[name].firstChange;
    if (this.initialized && (changed('keys') || changed('startDate') || changed('endDate'))) {
      this.reload();
    }
  }

  ngOnDestroy(): void {
    this.list.dispose();
    this.summarySub?.unsubscribe();
    this.eventsSub?.unsubscribe();
  }

  /** Reloads the summary and the first page of events. */
  reload(): void {
    this.loadSummary();
    this.reloadEvents();
  }

  // --- Summary ---

  private loadSummary(): void {
    this.summaryError.set(null);
    this.summarySub?.unsubscribe();
    this.summarySub = this.adminService.getChatTurnLimitSummary(
      this.startDate() || undefined, this.endDate() || undefined, this.keys()
    ).subscribe({
      next: summary => this.summary.set(summary),
      error: err => {
        this.summary.set(null);
        this.summaryError.set('The limit summary could not be loaded: ' + TelemetryLimitsComponent.errorText(err));
      }
    });
  }

  get stats(): LimitStat[] {
    const summary = this.summary();
    if (this.statsMemo?.summary === summary) {
      return this.statsMemo.stats;
    }
    const stats: LimitStat[] = [
      { id: 'hits', title: 'Limit hits', value: formatInteger(summary?.totalHits ?? 0) },
      { id: 'no-answer', title: 'Turns without an answer', value: formatInteger(summary?.turnsWithoutAnswer ?? 0) },
      { id: 'users', title: 'Users affected', value: formatInteger(summary?.usersAffected ?? 0) }
    ];
    this.statsMemo = { summary, stats };
    return stats;
  }

  // --- Filters ---

  /** The Reason facet's options: the codes with hits in the range, and any selected, with their counts there. */
  get codeOptions(): FilterFacetOption[] {
    const summary = this.summary();
    const selected = this.selectedCodes;
    const memo = this.codeOptionsMemo;
    if (memo && memo.summary === summary && memo.selected === selected) {
      return memo.options;
    }
    const counts = new Map((summary?.byCode ?? []).map(c => [c.code, c.hits] as const));
    const known: readonly string[] = TURN_LIMIT_CODES;
    const unknown = [...counts.keys(), ...selected].filter((code, i, all) => !known.includes(code) && all.indexOf(code) === i);
    const options = [...known, ...unknown]
      .filter(code => (counts.get(code) ?? 0) > 0 || selected.includes(code))
      .map(code => ({ value: code, label: limitCodeLabel(code), count: counts.get(code) ?? 0 }));
    this.codeOptionsMemo = { summary, selected, options };
    return options;
  }

  /** The Severity facet's options: the severities with hits in the range, and any selected, with their counts there. */
  get severityOptions(): FilterFacetOption[] {
    const summary = this.summary();
    const selected = this.selectedSeverities;
    const memo = this.severityOptionsMemo;
    if (memo && memo.summary === summary && memo.selected === selected) {
      return memo.options;
    }
    const counts = new Map((summary?.bySeverity ?? []).map(s => [s.severity as string, s.hits] as const));
    const options = TURN_LIMIT_SEVERITIES
      .filter(severity => (counts.get(severity) ?? 0) > 0 || selected.includes(severity))
      .map(severity => ({ value: severity, label: limitSeverityLabel(severity), count: counts.get(severity) ?? 0 }));
    this.severityOptionsMemo = { summary, selected, options };
    return options;
  }

  /** One empty array while nothing is selected, so the memo and the facet input stay put. */
  get selectedCodes(): readonly string[] {
    const selected = this.table.filterValues('code');
    return selected.length > 0 ? selected : NO_VALUES;
  }

  get selectedSeverities(): readonly string[] {
    const selected = this.table.filterValues('severity');
    return selected.length > 0 ? selected : NO_VALUES;
  }

  /** A facet is listed while the range holds two values or more, or while it has a selection. */
  get showCodeFacet(): boolean {
    return this.codeOptions.length > 1 || this.selectedCodes.length > 0;
  }

  get showSeverityFacet(): boolean {
    return this.severityOptions.length > 1 || this.selectedSeverities.length > 0;
  }

  get chips(): CardListChip[] {
    return this.list.chips(this.events());
  }

  get searchTerm(): string {
    return (this.table.filters['search'] ?? '').trim();
  }

  get hasFilters(): boolean {
    return this.selectedCodes.length > 0 || this.selectedSeverities.length > 0 || this.searchTerm !== '';
  }

  onSearchInput(event: Event): void {
    this.list.setSearchInput((event.target as HTMLInputElement).value);
  }

  /** Escape with text clears the search at once and refetches. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (this.list.clearSearchOnEscape(event)) this.reloadEvents();
  }

  onCodeChange(values: string[]): void {
    this.list.setFacet('code', values);
    this.reloadEvents();
  }

  onSeverityChange(values: string[]): void {
    this.list.setFacet('severity', values);
    this.reloadEvents();
  }

  /** A limit name in the By limit table: the list shows that reason alone. */
  selectCode(code: string): void {
    this.onCodeChange([code]);
  }

  /** Removes a chip's filter and refetches, then focuses the chip now in its place, else the previous one, else the search. */
  removeChip(chip: CardListChip): void {
    const index = this.list.removeChip(chip, this.events());
    this.reloadEvents();
    this.cdr.detectChanges();
    const chips = Array.from(this.host.nativeElement.querySelectorAll<HTMLButtonElement>('.tl-lim-chips .gh-filter-chip'));
    const target = index >= 0 ? chips[index] ?? chips[index - 1] : undefined;
    (target ?? this.element('#tl-lim-search'))?.focus();
  }

  /** Clears the search and the facets, refetches, then focuses the search. */
  clearFilters(): void {
    this.list.clearFilters();
    const input = this.element('#tl-lim-search') as HTMLInputElement | null;
    if (input) input.value = '';
    this.reloadEvents();
    this.cdr.detectChanges();
    input?.focus();
  }

  // --- Events ---

  get remainingCount(): number {
    return Math.max(0, this.totalCount() - this.events().length);
  }

  get nextBatchCount(): number {
    return Math.min(this.list.batch, this.remainingCount);
  }

  /** `Showing 20 of 143 hits`, `One hit`, or empty with none. */
  get listStatus(): string {
    if (this.eventsLoading() && !this.eventsLoaded()) return 'Loading limit hits…';
    const total = this.totalCount();
    if (this.eventsError() || total === 0) return '';
    const shown = this.events().length;
    return total === 1 && shown === 1 ? 'One hit' : `Showing ${formatInteger(shown)} of ${formatInteger(total)} hits`;
  }

  /** Fetches the first page of the current query, replacing the list. */
  reloadEvents(): void {
    this.fetchEvents(false);
  }

  /** Appends the next page and focuses the title of the first new card. */
  showMore(): void {
    if (this.eventsLoading() || this.remainingCount === 0) return;
    this.fetchEvents(true);
  }

  private fetchEvents(append: boolean): void {
    const query: ChatTurnLimitEventsQuery = {
      startDate: this.startDate() || undefined,
      endDate: this.endDate() || undefined,
      keys: this.keys(),
      codes: this.selectedCodes.length > 0 ? [...this.selectedCodes] : undefined,
      severities: this.selectedSeverities.length > 0 ? [...this.selectedSeverities] : undefined,
      search: this.searchTerm || undefined,
      skip: append ? this.events().length : 0,
      take: this.list.batch
    };
    if (!append) {
      this.eventsLoaded.set(false);
    }
    this.eventsLoading.set(true);
    this.eventsError.set(null);
    this.eventsSub?.unsubscribe();
    this.eventsSub = this.adminService.getChatTurnLimitEvents(query).subscribe({
      next: page => {
        const firstNew = append ? this.events().length : -1;
        if (append) {
          const known = new Set(this.events().map(e => e.id));
          this.events.set([...this.events(), ...page.items.filter(e => !known.has(e.id))]);
        } else {
          this.events.set(page.items);
        }
        this.totalCount.set(page.totalCount);
        this.table.setRemoteTotal(page.totalCount);
        this.eventsLoading.set(false);
        this.eventsLoaded.set(true);
        this.list.invalidate();
        this.cdr.markForCheck();
        setTimeout(() => refreshAnchorPositioning());
        if (firstNew >= 0) {
          const focusId = this.events()[firstNew]?.id;
          if (focusId !== undefined) {
            this.cdr.detectChanges();
            this.element(`#tl-lim-${focusId}-title`)?.focus();
          }
        }
      },
      error: err => {
        this.eventsLoading.set(false);
        this.eventsLoaded.set(true);
        this.eventsError.set('The limit hits could not be loaded: ' + TelemetryLimitsComponent.errorText(err));
      }
    });
  }

  // --- Actions ---

  /** Opens the event's details; focus returns to `opener` when they close. */
  openDetails(event: ChatTurnLimitEventSummary, opener: HTMLElement): void {
    this.eventDialog?.open(event.id, opener);
  }

  /** Shows the user's AI usage on the Users sub-tab. */
  showUsage(userId: string, event: ChatTurnLimitEventSummary): void {
    this.store.requestTelemetryUser(userId, event.userName?.trim() || userId);
  }

  // --- Helpers ---

  private element(selector: string): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(selector);
  }

  private static errorText(err: unknown): string {
    const http = err as { error?: { message?: string } | string; message?: string };
    if (http?.error && typeof http.error === 'object' && http.error.message) return http.error.message;
    if (typeof http?.error === 'string' && http.error) return http.error;
    return http?.message || 'Unknown error';
  }
}
