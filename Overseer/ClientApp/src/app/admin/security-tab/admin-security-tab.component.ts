import {
  AfterViewInit,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  ViewChild,
  inject
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Subscription } from 'rxjs';
import {
  AdminService,
  AdminSnapshotGuard,
  SafetyIdentifierLookup,
  SnapshotGuardMode,
  SecurityEvent,
  SecurityEventsQuery,
  SecurityKindCount,
  SecuritySummary
} from '../../services/admin.service';
import { AdminUsersService } from '../../services/admin-users.service';
import { CardListChip, CardListSort, CardListState } from '../../shared/data-table/card-list-state';
import { FilterFacetComponent, FilterFacetOption } from '../../shared/data-table/filter-facet.component';
import { TableState, anyOfFilter, customFilter } from '../../shared/data-table/table-state';
import { InfoTipComponent } from '../../shared/info-tip/info-tip.component';
import { parseServerUtcDate } from '../../utils/date.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';
import { rovingTabIndex } from '../../utils/roving-tab.util';
import {
  AdminHistoryMode,
  AdminPageStore,
  AdminSecuritySubTab,
  TELEMETRY_TIME_SPAN_LABELS,
  TelemetryTimeSpan
} from '../admin-page.store';
import { SecurityEventDialogComponent, SecurityUserEventsRequest } from './security-event-dialog.component';
import { securityKindLabel, securityRunSpanText, securityTimeText, securityUserLabel } from './security-event-archive';

export { securityKindLabel };

/** Where the events list's view settings are kept, per browser. */
export const SECURITY_EVENTS_VIEW_STORAGE_KEY = 'overseer.admin.security.events.view';

/** The events the list fetches at first, and how many more Show more fetches. */
export const SECURITY_EVENTS_BATCH = 20;

/** The longest block reason the server accepts. */
export const SECURITY_BLOCK_REASON_MAX = 256;

/** Every `SecurityEventKind` name, in the order the Kind facet lists them. */
export const SECURITY_EVENT_KINDS: readonly string[] = [
  'LoginFailed', 'LoginLockedOut', 'TwoFactorFailed', 'RateLimitRejected', 'QuotaRefused', 'AccessDenied',
  'ProviderRefusal', 'ProviderSafetyBlock', 'ProviderPolicyRejection', 'AttachmentRejected', 'ChatReported',
  'SnapshotRejected', 'AutoBlocked', 'AdminBlocked', 'AdminUnblocked', 'AccountHoldPlaced', 'AccountHoldReleased', 'AccountDeletionHeld',
  'AdminLockedOut', 'AdminUnlocked', 'AdminBanned', 'AdminUnbanned', 'AdminSignedOut', 'AdminEmailConfirmed',
  'AdminTwoFactorReset', 'AdminEmailChanged', 'AdminGameSignInBlockCleared'
];

const NO_KINDS: readonly string[] = [];

/** The events list has one order: the server's, newest first. */
const SECURITY_EVENT_SORTS: readonly CardListSort[] = [
  { id: 'newest', label: 'Newest first', column: 'occurred', direction: 'desc' }
];

/** One summary card: a word and a number, with optional sub-lines. */
export interface SecuritySummaryCard {
  id: string;
  title: string;
  value: number;
  /** A word after the number: `active`. */
  unit?: string;
  lines: string[];
}

/** One choice of the snapshot guard's mode, as the Security tab lists it. */
export interface SnapshotGuardModeOption {
  value: SnapshotGuardMode;
  label: string;
  description: string;
}

export const SNAPSHOT_GUARD_MODES: readonly SnapshotGuardModeOption[] = [
  { value: 'Off', label: 'Off', description: 'Nothing is scanned.' },
  { value: 'Observe', label: 'Observe', description: 'Game text addressed to the AI is recorded and still delivered to the model.' },
  { value: 'Enforce', label: 'Enforce', description: 'Game text addressed to the AI is refused; the player and you are told.' }
];

/** The sub-tabs' visible labels, in tab order. */
export const SECURITY_SUBTAB_LABELS: Readonly<Record<AdminSecuritySubTab, string>> = {
  summary: 'Summary',
  events: 'Events',
  settings: 'Settings'
};

/** The user the events list is narrowed to. */
interface SecurityUserFilter {
  id: string;
  name: string;
}

/**
 * The Admin page's Security tab, in three sub-tabs that share the AI Telemetry date range: Summary
 * (the counts of security events), Events (a lookup of the user behind a provider safety identifier,
 * and a server-paged card list of the events with a search, a Kind facet, Details per event, and
 * Block / Unblock and AI usage per user) and Settings (the snapshot guard).
 */
@Component({
  selector: 'app-admin-security-tab',
  standalone: true,
  imports: [FormsModule, FilterFacetComponent, InfoTipComponent, SecurityEventDialogComponent],
  templateUrl: './admin-security-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-security-tab.component.scss'
})
export class AdminSecurityTabComponent implements OnInit, AfterViewInit, OnDestroy {
  private readonly adminService = inject(AdminService);
  private readonly usersService = inject(AdminUsersService);
  private readonly store = inject(AdminPageStore);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

  @ViewChild('blockDialog') blockDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('secHead') private secHead?: ElementRef<HTMLElement>;
  @ViewChild('eventDialog') eventDialog?: SecurityEventDialogComponent;

  readonly kindLabel = securityKindLabel;
  readonly reasonMax = SECURITY_BLOCK_REASON_MAX;
  readonly subTabs: readonly AdminSecuritySubTab[] = ['summary', 'events', 'settings'];
  readonly subTabLabels = SECURITY_SUBTAB_LABELS;
  readonly timeSpans = Object.keys(TELEMETRY_TIME_SPAN_LABELS) as TelemetryTimeSpan[];
  readonly timeSpanLabels = TELEMETRY_TIME_SPAN_LABELS;

  todayDate = '';

  summary: SecuritySummary | null = null;
  summaryLoading = false;
  summaryError: string | null = null;

  /** The events fetched so far, newest first. */
  events: SecurityEvent[] = [];
  totalCount = 0;
  eventsLoading = false;
  eventsError: string | null = null;
  /** Whether the first page of the current query has arrived. */
  eventsLoaded = false;

  userFilter: SecurityUserFilter | null = null;

  safetyIdentifier = '';
  lookupBusy = false;
  lookupResult: SafetyIdentifierLookup | null = null;
  lookupMessage = '';

  readonly guardModes = SNAPSHOT_GUARD_MODES;
  /** The snapshot guard's state; null until it loads. */
  guard: AdminSnapshotGuard | null = null;
  /** True while a mode change is on its way. */
  guardPending = false;
  guardError: string | null = null;

  blockTarget: SecurityEvent | null = null;
  blockReason = '';
  blockError: string | null = null;
  blocking = false;
  /** The user whose Unblock is in flight. */
  unblockingUserId: string | null = null;

  /** The server filters and pages the events; the table only carries the filter values. */
  readonly table = new TableState<SecurityEvent>('occurred', 'desc').registerAccessors(
    { occurred: e => parseServerUtcDate(e.occurredUtc).getTime() },
    {
      search: customFilter<SecurityEvent>(() => true),
      kind: anyOfFilter<SecurityEvent>(e => e.kind)
    }
  );

  /** The search, the Kind selection and the chips; every change refetches the first page. */
  readonly list = new CardListState<SecurityEvent>(this.table, {
    idPrefix: 'sec',
    batch: SECURITY_EVENTS_BATCH,
    sorts: SECURITY_EVENT_SORTS,
    defaultSort: 'newest',
    storageKey: SECURITY_EVENTS_VIEW_STORAGE_KEY,
    facets: [{ column: 'kind', label: 'Kind', values: e => e.kind, order: SECURITY_EVENT_KINDS, labelOf: securityKindLabel }],
    onChange: () => this.reloadEvents()
  });

  private summarySub: Subscription | null = null;
  private guardSub: Subscription | null = null;
  private eventsSub: Subscription | null = null;
  private lookupSub: Subscription | null = null;
  private securityUserSub: Subscription | null = null;
  private returnFocus: HTMLElement | null = null;
  /** The event whose card title takes focus once the list has reloaded after a Block or Unblock. */
  private focusEventId: number | null = null;
  private headObserver?: ResizeObserver;

  private cardsMemo: { summary: SecuritySummary | null; cards: SecuritySummaryCard[] } | null = null;
  private kindOptionsMemo: { summary: SecuritySummary | null; selected: readonly string[]; options: FilterFacetOption[] } | null = null;

  /** The date range is the AI Telemetry tab's, kept in the page store. */
  get timeSpan(): TelemetryTimeSpan {
    return this.store.telemetryRange.timeSpan;
  }
  set timeSpan(value: TelemetryTimeSpan) {
    this.store.telemetryRange.timeSpan = value;
  }

  get startDate(): string {
    return this.store.telemetryRange.start;
  }
  set startDate(value: string) {
    this.store.telemetryRange.start = value;
  }

  get endDate(): string {
    return this.store.telemetryRange.end;
  }
  set endDate(value: string) {
    this.store.telemetryRange.end = value;
  }

  /** The selected sub-tab lives in the page store, which follows the address. */
  get subTab(): AdminSecuritySubTab {
    return this.store.securitySubTab;
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.table.setRemoteTotal(0);
    this.todayDate = AdminSecurityTabComponent.formatLocalDate(new Date());
    if (!this.startDate && !this.endDate) {
      this.applyPreset(this.timeSpan);
    }
    this.takePendingUser();
    this.securityUserSub = this.store.securityUserRequested$.subscribe(() => {
      if (this.takePendingUser()) {
        this.reloadEvents();
      }
    });
    this.loadSummary();
    this.loadGuard();
    this.reloadEvents();
  }

  /** Focused or scrolled-to elements stop below the sticky head: its height, plus a gap, as `--sec-sticky-clearance`. */
  ngAfterViewInit(): void {
    const head = this.secHead?.nativeElement;
    if (!head || typeof ResizeObserver === 'undefined') {
      return;
    }
    this.headObserver = new ResizeObserver(() => {
      const height = head.getBoundingClientRect().height;
      this.host.nativeElement.style.setProperty('--sec-sticky-clearance', `${Math.ceil(height) + 8}px`);
    });
    this.headObserver.observe(head);
  }

  ngOnDestroy(): void {
    this.headObserver?.disconnect();
    this.list.dispose();
    this.summarySub?.unsubscribe();
    this.guardSub?.unsubscribe();
    this.eventsSub?.unsubscribe();
    this.lookupSub?.unsubscribe();
    this.securityUserSub?.unsubscribe();
    const dialog = this.blockDialog?.nativeElement;
    if (dialog?.open) dialog.close();
  }

  // --- Sub-tabs ---

  /** Moves the address to a sub-tab; the selected one is left alone. */
  selectSubTab(tab: AdminSecuritySubTab, mode: AdminHistoryMode = 'push'): void {
    if (tab === this.subTab) {
      return;
    }
    this.store.goTo('security', tab, mode);
    // The tooltips anchored in the panel about to show are repositioned.
    setTimeout(() => refreshAnchorPositioning());
  }

  /** Arrow keys, Home and End move between the sub-tabs, select the one reached and focus it. */
  onSubTabKeydown(event: KeyboardEvent, index: number): void {
    const next = rovingTabIndex(event, index, this.subTabs.length);
    if (next === null) {
      return;
    }
    const tab = this.subTabs[next];
    this.selectSubTab(tab, 'replace');
    this.element(`#sec-subtab-${tab}`)?.focus();
  }

  // --- Date range ---

  static formatLocalDate(d: Date): string {
    const year = d.getFullYear();
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  /** Sets the dates a preset names; `custom` keeps the dates there are, else the last 30 days. */
  private applyPreset(preset: TelemetryTimeSpan): void {
    if (preset === 'custom' && this.startDate && this.endDate) {
      return;
    }
    const end = new Date();
    const start = new Date();
    if (preset === '7d') start.setDate(end.getDate() - 7);
    else if (preset === '28d') start.setDate(end.getDate() - 28);
    else if (preset === '90d') start.setDate(end.getDate() - 90);
    else if (preset === '180d') start.setDate(end.getDate() - 180);
    else if (preset === '1y') start.setFullYear(end.getFullYear() - 1);
    else start.setDate(end.getDate() - 30);
    this.startDate = AdminSecurityTabComponent.formatLocalDate(start);
    this.endDate = AdminSecurityTabComponent.formatLocalDate(end);
  }

  onTimeSpanChange(): void {
    this.applyPreset(this.timeSpan);
    this.refresh();
  }

  onCustomDateChange(): void {
    if (this.startDate && this.endDate && new Date(this.endDate) < new Date(this.startDate)) {
      this.endDate = this.startDate;
    }
    this.refresh();
  }

  /** Reloads the summary, the snapshot guard and the first page of events. */
  refresh(): void {
    this.loadSummary();
    this.loadGuard();
    this.reloadEvents();
  }

  /** The header's Refresh: inert while a load runs, since the button is only `aria-disabled`. */
  refreshFromHeader(): void {
    if (this.summaryLoading || this.eventsLoading) {
      return;
    }
    this.refresh();
  }

  // --- Snapshot guard ---

  loadGuard(): void {
    if (this.guardPending) return;
    this.guardSub?.unsubscribe();
    this.guardSub = this.adminService.getSnapshotGuard().subscribe({
      next: guard => {
        this.guard = guard;
        this.guardError = null;
        this.cdr.markForCheck();
      },
      error: err => {
        this.guardError = 'The snapshot guard could not be loaded: ' + AdminSecurityTabComponent.errorText(err);
        this.cdr.markForCheck();
      }
    });
  }

  /** Sets the mode a radio chose; on failure the previous mode is selected again and the error says why. */
  onModeChange(mode: SnapshotGuardMode): void {
    const previous = this.guard;
    if (!previous || this.guardPending) return;
    this.guardPending = true;
    this.guardError = null;
    this.guard = { ...previous, mode };
    this.guardSub?.unsubscribe();
    this.guardSub = this.adminService.setSnapshotGuardMode(mode).subscribe({
      next: guard => {
        this.guardPending = false;
        this.guard = guard;
        this.cdr.markForCheck();
      },
      error: err => {
        this.guardPending = false;
        this.guard = { ...previous };
        this.guardError = 'The mode could not be changed: ' + AdminSecurityTabComponent.errorText(err);
        this.cdr.detectChanges();
        // A radio keeps its own checked state; the group is set back to the stored mode.
        const input = this.element(`#sec-guard-${previous.mode}`) as HTMLInputElement | null;
        if (input) input.checked = true;
      }
    });
  }

  /** `Changed 24 Oct 2026 14:02 by tommi.`, or the seeded state. */
  get guardStatusText(): string {
    const guard = this.guard;
    if (!guard) return '';
    if (this.guardPending) return 'Saving…';
    if (guard.seeded || !guard.setAtUtc) return 'Not set yet: seeded from the configuration.';
    const at = parseServerUtcDate(guard.setAtUtc);
    const when = isNaN(at.getTime()) ? guard.setAtUtc : AdminSecurityTabComponent.formatChangedAt(at);
    return guard.setByUserName ? `Changed ${when} by ${guard.setByUserName}.` : `Changed ${when}.`;
  }

  /** The automatic block rule and the e-mail, as configured. */
  get guardRuleText(): string {
    const guard = this.guard;
    if (!guard) return '';
    const block = guard.autoBlockEnabled
      ? `on, after injection-level refusals on ${guard.autoBlockDistinctDays} separate days within ${guard.autoBlockWindowDays} days`
      : 'off';
    return `Automatic block: ${block}; e-mail to the operator mailbox: ${guard.notificationEmailConfigured ? 'on' : 'off'}.`;
  }

  /** Whether refused game text is kept, and for how long. */
  get guardQuarantineText(): string {
    const guard = this.guard;
    if (!guard) return '';
    return guard.quarantineEnabled
      ? `Refused game text is kept encrypted for ${guard.quarantineRetentionDays} days for review, in every chat mode.`
      : 'Refused game text is not kept (QuarantineEnabled is off).';
  }

  /** `24 Oct 2026 14:02`, in local time. */
  static formatChangedAt(d: Date): string {
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getDate()} ${months[d.getMonth()]} ${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }

  // --- Summary ---

  loadSummary(): void {
    this.summaryLoading = true;
    this.summaryError = null;
    this.summarySub?.unsubscribe();
    this.summarySub = this.adminService.getSecuritySummary(this.startDate || undefined, this.endDate || undefined).subscribe({
      next: summary => {
        this.summary = summary;
        this.summaryLoading = false;
        this.cdr.markForCheck();
      },
      error: err => {
        this.summaryLoading = false;
        this.summaryError = 'The security summary could not be loaded: ' + AdminSecurityTabComponent.errorText(err);
        this.cdr.markForCheck();
      }
    });
  }

  /** The summary cards, rebuilt only when the summary changes. */
  get summaryCards(): SecuritySummaryCard[] {
    if (this.cardsMemo && this.cardsMemo.summary === this.summary) {
      return this.cardsMemo.cards;
    }
    const cards = AdminSecurityTabComponent.buildCards(this.summary);
    this.cardsMemo = { summary: this.summary, cards };
    return cards;
  }

  get activeHolds(): number {
    return this.summary?.activeAccountHolds ?? 0;
  }

  get pendingDeletions(): number {
    return this.summary?.pendingDeletionRequests ?? 0;
  }

  /** Opens the Users tab on its deletion requests. */
  reviewDeletionRequests(): void {
    this.store.usersSubTab = 'deletions';
    this.store.requestTab('users');
  }

  private static buildCards(summary: SecuritySummary | null): SecuritySummaryCard[] {
    const byKind = new Map<string, SecurityKindCount>((summary?.kinds ?? []).map(k => [k.kind, k] as const));
    const count = (kind: string): number => byKind.get(kind)?.count ?? 0;
    const sum = (...kinds: string[]): number => kinds.reduce((total, kind) => total + count(kind), 0);
    const reach = (kind: string): string[] => {
      const entry = byKind.get(kind);
      return entry && entry.count > 0 ? [`${plural(entry.distinctUsers, 'user')} · ${plural(entry.distinctIps, 'IP address', 'IP addresses')}`] : [];
    };
    const parts = (...kinds: string[]): string[] => kinds.map(kind => `${securityKindLabel(kind)}: ${count(kind)}`);

    return [
      { id: 'sign-in', title: 'Sign-in failures', value: sum('LoginFailed', 'TwoFactorFailed'), lines: parts('LoginFailed', 'TwoFactorFailed') },
      { id: 'lockouts', title: 'Lockouts', value: count('LoginLockedOut'), lines: reach('LoginLockedOut') },
      {
        id: 'limits', title: 'Limit refusals', value: sum('QuotaRefused', 'RateLimitRejected'),
        lines: parts('QuotaRefused', 'RateLimitRejected', 'AccessDenied')
      },
      {
        id: 'provider', title: 'Provider refusals and policy hits',
        value: sum('ProviderRefusal', 'ProviderSafetyBlock', 'ProviderPolicyRejection'),
        lines: parts('ProviderRefusal', 'ProviderSafetyBlock', 'ProviderPolicyRejection')
      },
      { id: 'attachments', title: 'Rejected attachments', value: count('AttachmentRejected'), lines: reach('AttachmentRejected') },
      { id: 'reports', title: 'Reports', value: count('ChatReported'), lines: reach('ChatReported') },
      { id: 'snapshots', title: 'Snapshot refusals', value: count('SnapshotRejected'), lines: reach('SnapshotRejected') },
      {
        id: 'blocks', title: 'Blocks', value: count('AdminBlocked'),
        lines: [`Unblocks: ${count('AdminUnblocked')}`, `Automatic: ${count('AutoBlocked')}`]
      }
    ];
  }

  // --- Events list ---

  /** The Kind facet's options: the kinds with events in the range, and any selected, with their counts there. */
  get kindOptions(): FilterFacetOption[] {
    const selected = this.selectedKinds;
    const memo = this.kindOptionsMemo;
    if (memo && memo.summary === this.summary && memo.selected === selected) {
      return memo.options;
    }
    const counts = new Map((this.summary?.kinds ?? []).map(k => [k.kind, k.count] as const));
    const options = SECURITY_EVENT_KINDS
      .filter(kind => (counts.get(kind) ?? 0) > 0 || selected.includes(kind))
      .map(kind => ({ value: kind, label: securityKindLabel(kind), count: counts.get(kind) ?? 0 }));
    this.kindOptionsMemo = { summary: this.summary, selected, options };
    return options;
  }

  /** The Kind selection; one empty array while nothing is selected, so the memo and the facet input stay put. */
  get selectedKinds(): readonly string[] {
    const selected = this.table.filterValues('kind');
    return selected.length > 0 ? selected : NO_KINDS;
  }

  /** The Kind facet is listed while the range holds two kinds or more, or while it has a selection. */
  get showKindFacet(): boolean {
    return this.kindOptions.length > 1 || this.selectedKinds.length > 0;
  }

  get chips(): CardListChip[] {
    return this.list.chips(this.events);
  }

  get searchTerm(): string {
    return (this.table.filters['search'] ?? '').trim();
  }

  get hasFilters(): boolean {
    return this.selectedKinds.length > 0 || this.searchTerm !== '';
  }

  get remainingCount(): number {
    return Math.max(0, this.totalCount - this.events.length);
  }

  get nextBatchCount(): number {
    return Math.min(this.list.batch, this.remainingCount);
  }

  /** `Showing 20 of 143 events`, `One event`, or empty with no events. */
  get listStatus(): string {
    if (this.eventsLoading && !this.eventsLoaded) return 'Loading security events…';
    if (this.eventsError || this.totalCount === 0) return '';
    return this.totalCount === 1 && this.events.length === 1
      ? 'One event'
      : `Showing ${this.events.length} of ${this.totalCount} events`;
  }

  /** Fetches the first page of the current query, replacing the list. */
  reloadEvents(): void {
    this.fetchEvents(false);
  }

  /** Appends the next page and focuses the title of the first new card. */
  showMore(): void {
    if (this.eventsLoading || this.remainingCount === 0) return;
    this.fetchEvents(true);
  }

  private fetchEvents(append: boolean): void {
    const query: SecurityEventsQuery = {
      startDate: this.startDate || undefined,
      endDate: this.endDate || undefined,
      kinds: this.selectedKinds.length > 0 ? [...this.selectedKinds] : undefined,
      userId: this.userFilter?.id,
      search: this.searchTerm || undefined,
      skip: append ? this.events.length : 0,
      take: this.list.batch
    };
    if (!append) {
      this.eventsLoaded = false;
    }
    this.eventsLoading = true;
    this.eventsError = null;
    this.eventsSub?.unsubscribe();
    this.eventsSub = this.adminService.getSecurityEvents(query).subscribe({
      next: page => {
        const firstNew = append ? this.events.length : -1;
        if (append) {
          const known = new Set(this.events.map(e => e.id));
          this.events = [...this.events, ...page.rows.filter(e => !known.has(e.id))];
        } else {
          this.events = page.rows;
        }
        this.totalCount = page.totalCount;
        this.table.setRemoteTotal(page.totalCount);
        this.eventsLoading = false;
        this.eventsLoaded = true;
        this.list.invalidate();
        const focusId = firstNew >= 0 ? this.events[firstNew]?.id ?? null : this.focusEventId;
        this.focusEventId = null;
        if (focusId === null) {
          this.cdr.markForCheck();
          return;
        }
        this.cdr.detectChanges();
        refreshAnchorPositioning();
        (this.element(`#sec-${focusId}-title`) ?? (firstNew >= 0 ? null : this.element('#sec-events-title')))?.focus();
      },
      error: err => {
        this.eventsLoading = false;
        this.eventsLoaded = true;
        this.focusEventId = null;
        this.eventsError = 'The security events could not be loaded: ' + AdminSecurityTabComponent.errorText(err);
        this.cdr.markForCheck();
      }
    });
  }

  onSearchInput(event: Event): void {
    this.list.setSearchInput((event.target as HTMLInputElement).value);
  }

  /** Escape with text clears the search at once and refetches. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (this.list.clearSearchOnEscape(event)) this.reloadEvents();
  }

  onKindChange(values: string[]): void {
    this.list.setFacet('kind', values);
    this.reloadEvents();
  }

  /** Removes a chip's filter and refetches, then focuses the chip now in its place, else the previous one, else the search. */
  removeChip(chip: CardListChip): void {
    const index = this.list.removeChip(chip, this.events);
    this.reloadEvents();
    this.cdr.detectChanges();
    const chips = Array.from(this.host.nativeElement.querySelectorAll<HTMLButtonElement>('.sec-chips .gh-filter-chip'));
    const target = index >= 0 ? chips[index] ?? chips[index - 1] : undefined;
    (target ?? this.element('#sec-search'))?.focus();
  }

  /** Clears the search and the Kind filter, refetches, then focuses the search. */
  clearFilters(): void {
    this.list.clearFilters();
    const input = this.element('#sec-search') as HTMLInputElement | null;
    if (input) input.value = '';
    this.reloadEvents();
    input?.focus();
  }

  /** Drops the user filter and refetches. */
  clearUserFilter(): void {
    this.userFilter = null;
    this.reloadEvents();
    this.element('#sec-events-title')?.focus();
  }

  /** Reads and clears the page store's pending security-user request; true when there was one. */
  private takePendingUser(): boolean {
    const id = this.store.pendingSecurityUserId;
    if (!id) return false;
    this.userFilter = { id, name: this.store.pendingSecurityUserName || id };
    this.store.pendingSecurityUserId = null;
    this.store.pendingSecurityUserName = null;
    return true;
  }

  // --- A card ---

  /** The user name, else *Unknown user* for an id without one, else *No user*. */
  userLabel(event: SecurityEvent): string {
    return securityUserLabel(event);
  }

  /** `2026-10-13 09:15 UTC`. */
  timeText(event: SecurityEvent): string {
    return securityTimeText(event.occurredUtc);
  }

  /** How long a run of repeats lasted: `in 4 min`, `in under a minute`, `in 2 h 5 min`. */
  runSpanText(event: SecurityEvent): string {
    return securityRunSpanText(event.occurredUtc, event.lastOccurredUtc);
  }

  /** `, 2 texts kept`, for assistive technology after the *Text kept* tag. */
  keptText(event: SecurityEvent): string {
    return `, ${event.quarantineCount} ${event.quarantineCount === 1 ? 'text' : 'texts'} kept`;
  }

  modelText(event: SecurityEvent): string {
    return [event.provider, event.modelId].filter(part => !!part).join(' · ');
  }

  // --- Actions ---

  /** Opens the event's details; focus returns to `opener` when they close. */
  openDetails(event: SecurityEvent, opener: HTMLElement): void {
    this.eventDialog?.open(event.id, opener);
  }

  /** *Events of this user* in the details dialog. */
  onUserEventsRequested(request: SecurityUserEventsRequest): void {
    this.showUserEvents(request.userId, request.userName);
  }

  /** Opens the reason step for a Block. */
  requestBlock(event: SecurityEvent, button: HTMLElement): void {
    if (!event.userId || this.blocking) return;
    this.blockTarget = event;
    this.blockReason = '';
    this.blockError = null;
    this.returnFocus = button;
    this.cdr.detectChanges();
    const dialog = this.blockDialog?.nativeElement;
    if (dialog && !dialog.open) dialog.showModal();
    this.element('#sec-block-reason')?.focus();
  }

  cancelBlock(): void {
    this.blockDialog?.nativeElement.close();
  }

  /** The dialog's own close, by Cancel or Escape: focus goes back to the Block pressed. After a block the reload moves it instead. */
  onBlockDialogClose(): void {
    this.blockTarget = null;
    const back = this.returnFocus;
    this.returnFocus = null;
    if (back?.isConnected) back.focus();
  }

  confirmBlock(): void {
    const target = this.blockTarget;
    if (!target?.userId || this.blocking) return;
    const reason = this.blockReason.trim();
    if (reason.length < 1 || reason.length > SECURITY_BLOCK_REASON_MAX) {
      this.blockError = `Enter a reason of 1 to ${SECURITY_BLOCK_REASON_MAX} characters.`;
      return;
    }
    const name = this.userLabel(target);
    this.blocking = true;
    this.blockError = null;
    this.usersService.blockFromAiChat(target.userId, reason).subscribe({
      next: () => {
        this.blocking = false;
        this.returnFocus = null;
        this.focusEventId = target.id;
        this.blockDialog?.nativeElement.close();
        this.store.showToast(`${name} can no longer start AI work in Overseer.`, 'success', 'User Blocked');
        this.refresh();
      },
      error: err => {
        this.blocking = false;
        this.blockError = AdminSecurityTabComponent.errorText(err);
        this.cdr.markForCheck();
      }
    });
  }

  unblock(event: SecurityEvent): void {
    if (!event.userId || this.unblockingUserId) return;
    const name = this.userLabel(event);
    this.unblockingUserId = event.userId;
    this.usersService.unblockFromAiChat(event.userId).subscribe({
      next: () => {
        this.unblockingUserId = null;
        this.focusEventId = event.id;
        this.store.showToast(`${name} can start AI work in Overseer again.`, 'success', 'User Unblocked');
        this.refresh();
      },
      error: err => {
        this.unblockingUserId = null;
        this.store.showToast('The user could not be unblocked: ' + AdminSecurityTabComponent.errorText(err), 'error', 'Error');
      }
    });
  }

  /** Shows the user's AI usage on the AI Telemetry tab. */
  showUsage(userId: string, userName: string | null | undefined): void {
    this.store.requestTelemetryUser(userId, userName?.trim() || userId);
  }

  // --- Safety identifier lookup ---

  lookup(): void {
    const identifier = this.safetyIdentifier.trim();
    if (!identifier || this.lookupBusy) return;
    this.lookupBusy = true;
    this.lookupResult = null;
    this.lookupMessage = '';
    this.lookupSub?.unsubscribe();
    this.lookupSub = this.adminService.lookupSafetyIdentifier(identifier).subscribe({
      next: result => {
        this.lookupBusy = false;
        this.lookupResult = result;
        this.lookupMessage = `Found ${result.userName}.`;
        this.cdr.markForCheck();
      },
      error: err => {
        this.lookupBusy = false;
        this.lookupMessage = err instanceof HttpErrorResponse && err.status === 404
          ? 'No user has this identifier.'
          : 'The lookup failed: ' + AdminSecurityTabComponent.errorText(err);
        this.cdr.markForCheck();
      }
    });
  }

  /** Narrows the events list to the user the lookup found, and focuses the list's heading. */
  showLookupEvents(): void {
    const result = this.lookupResult;
    if (!result) return;
    this.showUserEvents(result.userId, result.userName);
  }

  /** Narrows the events list to one user, and focuses the list's heading. */
  showUserEvents(userId: string, userName: string): void {
    this.userFilter = { id: userId, name: userName };
    this.reloadEvents();
    this.element('#sec-events-title')?.focus();
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

/** `1 user`, `3 users`. */
function plural(count: number, one: string, many = `${one}s`): string {
  return `${count} ${count === 1 ? one : many}`;
}
