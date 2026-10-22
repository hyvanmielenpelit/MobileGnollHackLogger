import { AfterViewInit, Component, ChangeDetectionStrategy, ElementRef, OnInit, OnDestroy, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable, forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
  AdminService,
  AiTelemetrySummaryDto,
  AiGovernorStatusDto,
  AiTelemetryKeySource
} from '../../services/admin.service';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';
import { rovingTabIndex } from '../../utils/roving-tab.util';
import { AdminHistoryMode, AdminPageStore, AdminTelemetrySubTab, TelemetryTimeSpan } from '../admin-page.store';
import { TelemetryChartsDialogComponent } from './charts/telemetry-charts-dialog.component';
import { TelemetryModelUsersDialogComponent } from './model-users/telemetry-model-users-dialog.component';
import { TelemetryUserModelsDialogComponent } from './user-models/telemetry-user-models-dialog.component';
import { TelemetryModelUsersRef, TelemetryModelsComponent } from './models/telemetry-models.component';
import { TelemetrySummaryComponent } from './summary/telemetry-summary.component';
import {
  KEY_SOURCES,
  KEY_SOURCE_LABELS,
  TelemetryBreakdownView,
  readBreakdownView,
  writeBreakdownView
} from './telemetry-breakdown';
import { TelemetryUserModelsRef, TelemetryUserRef, TelemetryUsersComponent } from './users/telemetry-users.component';
import { TelemetryGroupModelsRef, TelemetryGroupRef, TelemetryGroupsComponent } from './groups/telemetry-groups.component';

/** The sub-tabs' visible labels, in tab order. */
export const TELEMETRY_SUBTAB_LABELS: Readonly<Record<AdminTelemetrySubTab, string>> = {
  summary: 'Summary',
  models: 'Models',
  users: 'Users',
  groups: 'Groups'
};

/** The Date Range select's options, also the collapsed bar's summary. */
export const TELEMETRY_TIME_SPAN_LABELS: Readonly<Record<TelemetryTimeSpan, string>> = {
  '7d': 'Last 7 days',
  '28d': 'Last 28 days',
  '30d': 'Last 30 days',
  '90d': 'Last 90 days',
  '180d': 'Last 180 days',
  '1y': 'Last year',
  'custom': 'Custom date range'
};

const TIME_SPANS = Object.keys(TELEMETRY_TIME_SPAN_LABELS) as TelemetryTimeSpan[];

/**
 * The Admin page's AI Telemetry tab, in four sub-tabs that share one date range and one key source:
 * Summary (usage, cost, provider availability, Overseer activity and the request governor's
 * partitions), Models (the breakdown by model and role), Users (the usage by user) and Groups (the
 * usage by group, each counting its current members). The tab fetches the summary and the governor
 * status for Summary and Models, and is the only owner and writer of the stored view record, which
 * holds the Keys filter and the breakdown's settings.
 */
@Component({
  selector: 'app-admin-telemetry-tab',
  imports: [
    FormsModule, TelemetrySummaryComponent, TelemetryModelsComponent, TelemetryUsersComponent, TelemetryGroupsComponent,
    TelemetryChartsDialogComponent, TelemetryModelUsersDialogComponent, TelemetryUserModelsDialogComponent
  ],
  templateUrl: './admin-telemetry-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-telemetry-tab.component.scss'
})
export class AdminTelemetryTabComponent implements OnInit, AfterViewInit, OnDestroy {
  private adminService = inject(AdminService);
  private store = inject(AdminPageStore);
  private host = inject<ElementRef<HTMLElement>>(ElementRef);

  @ViewChild('chartsDialog') chartsDialog?: TelemetryChartsDialogComponent;
  @ViewChild('modelUsersDialog') modelUsersDialog?: TelemetryModelUsersDialogComponent;
  @ViewChild('userModelsDialog') userModelsDialog?: TelemetryUserModelsDialogComponent;
  @ViewChild(TelemetryUsersComponent) usersView?: TelemetryUsersComponent;
  @ViewChild(TelemetryGroupsComponent) groupsView?: TelemetryGroupsComponent;
  @ViewChild('tlHead') private tlHead?: ElementRef<HTMLElement>;

  readonly subTabs: readonly AdminTelemetrySubTab[] = ['summary', 'models', 'users', 'groups'];
  readonly subTabLabels = TELEMETRY_SUBTAB_LABELS;
  readonly keySources = KEY_SOURCES;
  readonly keySourceLabels = KEY_SOURCE_LABELS;
  readonly timeSpans = TIME_SPANS;
  readonly timeSpanLabels = TELEMETRY_TIME_SPAN_LABELS;

  telemetrySummary: AiTelemetrySummaryDto | null = null;
  governorStatus: AiGovernorStatusDto | null = null;
  telemetryLoading = false;
  governorLoading = false;
  todayDate: string = '';

  /** The Keys filter and the breakdown's view settings, remembered per browser. */
  view: TelemetryBreakdownView = readBreakdownView();

  private governorCountdownTimer?: ReturnType<typeof setInterval>;
  private headObserver?: ResizeObserver;
  /** The sub-tabs whose panels have been shown; they stay rendered, hidden, once left. */
  private readonly mounted = new Set<AdminTelemetrySubTab>();

  /** The date range lives in the page store, so it survives a tab switch. */
  get telemetryTimeSpan(): TelemetryTimeSpan {
    return this.store.telemetryRange.timeSpan;
  }
  set telemetryTimeSpan(value: TelemetryTimeSpan) {
    this.store.telemetryRange.timeSpan = value;
  }

  get telemetryStartDate(): string {
    return this.store.telemetryRange.start;
  }
  set telemetryStartDate(value: string) {
    this.store.telemetryRange.start = value;
  }

  get telemetryEndDate(): string {
    return this.store.telemetryRange.end;
  }
  set telemetryEndDate(value: string) {
    this.store.telemetryRange.end = value;
  }

  get keys(): AiTelemetryKeySource {
    return this.view.keys;
  }

  /** The selected sub-tab lives in the page store, which follows the address. */
  get subTab(): AdminTelemetrySubTab {
    return this.store.telemetrySubTab;
  }

  ngOnInit() {
    ensureOverlayPolyfills();
    this.todayDate = this.formatLocalDate(new Date());
    if (!this.telemetryStartDate && !this.telemetryEndDate) {
      this.applyTelemetryPreset(this.telemetryTimeSpan, false);
    }
    this.refreshTelemetryTab();
  }

  // --- Sub-tabs ---

  /** Moves the address to a sub-tab; the selected one is left alone. */
  selectSubTab(tab: AdminTelemetrySubTab, mode: AdminHistoryMode = 'push') {
    if (tab === this.subTab) {
      return;
    }
    this.store.goTo('telemetry', tab, mode);
    // A Sort popover anchored in the panel just hidden, or one about to show, is repositioned.
    setTimeout(() => refreshAnchorPositioning());
  }

  /** Arrow keys, Home and End move between the sub-tabs, select the one reached and focus it. */
  onSubTabKeydown(event: KeyboardEvent, index: number) {
    const next = rovingTabIndex(event, index, this.subTabs.length);
    if (next === null) {
      return;
    }
    const tab = this.subTabs[next];
    this.selectSubTab(tab, 'replace');
    this.host.nativeElement.querySelector<HTMLElement>(`#tl-subtab-${tab}`)?.focus();
  }

  /** True for the selected sub-tab and every one shown before; marks the selected one as shown. */
  isMounted(tab: AdminTelemetrySubTab): boolean {
    if (tab === this.subTab) {
      this.mounted.add(tab);
      return true;
    }
    return this.mounted.has(tab);
  }

  /** Focused or scrolled-to elements stop below the sticky head: its height, plus a gap, as `--tl-sticky-clearance`. */
  ngAfterViewInit() {
    const head = this.tlHead?.nativeElement;
    if (!head || typeof ResizeObserver === 'undefined') {
      return;
    }
    this.headObserver = new ResizeObserver(() => {
      const height = head.getBoundingClientRect().height;
      this.host.nativeElement.style.setProperty('--tl-sticky-clearance', `${Math.ceil(height) + 8}px`);
    });
    this.headObserver.observe(head);
  }

  ngOnDestroy() {
    this.stopGovernorCountdown();
    this.headObserver?.disconnect();
  }

  formatLocalDate(d: Date): string {
    const year = d.getFullYear();
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  applyTelemetryPreset(preset: '7d' | '28d' | '30d' | '90d' | '180d' | '1y' | 'custom', fetch = true) {
    if (preset !== 'custom') {
      const end = new Date();
      const start = new Date();
      if (preset === '7d') start.setDate(end.getDate() - 7);
      else if (preset === '28d') start.setDate(end.getDate() - 28);
      else if (preset === '30d') start.setDate(end.getDate() - 30);
      else if (preset === '90d') start.setDate(end.getDate() - 90);
      else if (preset === '180d') start.setDate(end.getDate() - 180);
      else if (preset === '1y') start.setFullYear(end.getFullYear() - 1);

      this.telemetryStartDate = this.formatLocalDate(start);
      this.telemetryEndDate = this.formatLocalDate(end);
    } else {
      if (!this.telemetryStartDate || !this.telemetryEndDate) {
        const end = new Date();
        const start = new Date();
        start.setDate(end.getDate() - 30);
        this.telemetryStartDate = this.formatLocalDate(start);
        this.telemetryEndDate = this.formatLocalDate(end);
      }
    }
    if (fetch) {
      this.loadTelemetry();
    }
  }

  onTelemetryTimeSpanChange() {
    this.applyTelemetryPreset(this.telemetryTimeSpan, true);
  }

  onCustomDateChange() {
    if (this.telemetryStartDate && this.telemetryEndDate) {
      if (new Date(this.telemetryEndDate) < new Date(this.telemetryStartDate)) {
        this.telemetryEndDate = this.telemetryStartDate;
      }
    }
    this.loadTelemetry();
  }

  /** Stores the Keys filter and reloads the summary. */
  onKeysChange(keys: AiTelemetryKeySource) {
    if (!KEY_SOURCES.includes(keys) || keys === this.view.keys) {
      return;
    }
    this.saveView({ ...this.view, keys });
    this.loadTelemetry();
  }

  /** Stores the breakdown's settings the Models panel changed, keeping the tab's Keys filter. */
  onViewChange(view: TelemetryBreakdownView) {
    this.saveView({ ...view, keys: this.view.keys });
  }

  loadTelemetry(showFeedback = false) {
    this.telemetryLoading = true;
    this.adminService.getAiTelemetrySummary(this.telemetryStartDate || undefined, this.telemetryEndDate || undefined, this.keys).subscribe({
      next: (data) => {
        this.telemetrySummary = data;
        this.telemetryLoading = false;
        if (showFeedback) {
          this.store.showToast('AI Telemetry refreshed.', 'info', 'Telemetry Refreshed');
        }
      },
      error: (err) => {
        console.error('Failed to load telemetry', err);
        this.telemetryLoading = false;
        if (showFeedback) {
          this.store.showToast('Failed to load telemetry: ' + (err.error?.message || err.message), 'error', 'Error');
        }
      }
    });
  }

  /** Partitions currently in a rate-limit cooldown. */
  get rateLimitedKeyCount(): number {
    return this.governorStatus?.activeKeys.filter(k => k.isRateLimited).length ?? 0;
  }

  /**
   * Reloads the summary and the governor status together, and the users and groups views once they exist. Each request settles on its own, so one failure still shows the other's data; the feedback is a single toast
   * either way.
   */
  refreshTelemetryTab(showFeedback = false) {
    this.telemetryLoading = true;
    this.governorLoading = true;
    const failures: string[] = [];
    const settle = <T>(request: Observable<T>, what: string) => request.pipe(
      catchError(err => {
        console.error(`Failed to load ${what}`, err);
        failures.push(`${what}: ${err.error?.message || err.message}`);
        return of(null);
      })
    );

    if (showFeedback) {
      this.usersView?.reload();
      this.groupsView?.reload();
    }

    forkJoin({
      telemetry: settle(this.adminService.getAiTelemetrySummary(this.telemetryStartDate || undefined, this.telemetryEndDate || undefined, this.keys), 'telemetry'),
      governor: settle(this.adminService.getGovernorStatus(), 'governor status')
    }).subscribe(({ telemetry, governor }) => {
      if (telemetry) {
        this.telemetrySummary = telemetry;
      }
      if (governor) {
        this.setGovernorStatus(governor);
      }
      this.telemetryLoading = false;
      this.governorLoading = false;
      if (!showFeedback) {
        return;
      }
      if (failures.length > 0) {
        this.store.showToast('Failed to load ' + failures.join('; '), 'error', 'Error');
      } else {
        this.store.showToast('AI Telemetry refreshed.', 'info', 'Telemetry Refreshed');
      }
    });
  }

  /** Header Refresh: refused while a load runs (the button is aria-disabled then). */
  refreshFromHeader() {
    if (this.telemetryLoading || this.governorLoading) {
      return;
    }
    this.refreshTelemetryTab(true);
  }

  loadGovernorStatus(showFeedback = false) {
    this.governorLoading = true;
    this.adminService.getGovernorStatus().subscribe({
      next: (data) => {
        this.setGovernorStatus(data);
        this.governorLoading = false;
        if (showFeedback) {
          this.store.showToast('Governor status refreshed.', 'info', 'Governor Refreshed');
        }
      },
      error: (err) => {
        console.error('Failed to load governor status', err);
        this.governorLoading = false;
        if (showFeedback) {
          this.store.showToast('Failed to load governor status: ' + (err.error?.message || err.message), 'error', 'Error');
        }
      }
    });
  }

  private setGovernorStatus(status: AiGovernorStatusDto) {
    this.governorStatus = status;
    this.startGovernorCountdown();
  }

  /**
   * Counts the fetched cooldowns down locally once a second. When the last one reaches zero the
   * status is re-fetched once, so the server's view replaces the local estimate.
   */
  private startGovernorCountdown() {
    this.stopGovernorCountdown();
    if (this.rateLimitedKeyCount === 0) {
      return;
    }
    this.governorCountdownTimer = setInterval(() => {
      for (const key of this.governorStatus?.activeKeys ?? []) {
        if (!key.isRateLimited) {
          continue;
        }
        key.remainingCooldownSeconds = Math.max(0, Math.round((key.remainingCooldownSeconds - 1) * 10) / 10);
        if (key.remainingCooldownSeconds === 0) {
          key.isRateLimited = false;
        }
      }
      if (this.rateLimitedKeyCount === 0) {
        this.stopGovernorCountdown();
        this.loadGovernorStatus();
      }
    }, 1000);
  }

  private stopGovernorCountdown() {
    if (this.governorCountdownTimer !== undefined) {
      clearInterval(this.governorCountdownTimer);
      this.governorCountdownTimer = undefined;
    }
  }

  /** Without a key, clears every partition; refused while nothing is rate-limited (the button is aria-disabled). */
  resetGovernorCooldown(credentialKey?: string) {
    if (!credentialKey && this.rateLimitedKeyCount === 0) {
      return;
    }
    this.adminService.resetGovernorCooldown(credentialKey).subscribe({
      next: () => {
        this.store.showToast(credentialKey ? `Cooldown cleared for ${credentialKey}.` : 'All rate-limit cooldowns cleared.', 'success', 'Cooldown Reset');
        this.loadGovernorStatus();
      },
      error: (err) => {
        this.store.showToast('Failed to reset cooldown: ' + (err.error?.message || err.message), 'error', 'Error');
      }
    });
  }

  // --- Charts ---

  /** Opens the charts dialog for the range, the Keys filter and the breakdown's roles. */
  openCharts() {
    this.chartsDialog?.open({
      start: this.telemetryStartDate,
      end: this.telemetryEndDate,
      keys: this.keys,
      roles: [...this.view.roles]
    });
  }

  /** Opens the charts dialog for one user. */
  openUserCharts(user: TelemetryUserRef) {
    this.chartsDialog?.open({
      start: this.telemetryStartDate,
      end: this.telemetryEndDate,
      keys: this.keys,
      roles: [...this.view.roles],
      userId: user.userId,
      userName: user.userName
    });
  }

  /** Opens the charts dialog for one group, or for the users in no group. */
  openGroupCharts(group: TelemetryGroupRef) {
    this.chartsDialog?.open({
      start: this.telemetryStartDate,
      end: this.telemetryEndDate,
      keys: this.keys,
      roles: [...this.view.roles],
      groupId: group.groupId,
      groupName: group.groupName
    });
  }

  // --- Usage by user ---

  /** Opens the Usage by user dialog the Models panel asked for, focus returning to the button that asked. */
  openModelUsers(ref: TelemetryModelUsersRef) {
    this.modelUsersDialog?.open(ref.request, ref.opener ?? undefined);
  }

  // --- Usage by model ---

  /** Opens the Usage by model dialog for one user, over the tab's range and Keys filter. */
  openUserModels(ref: TelemetryUserModelsRef) {
    this.userModelsDialog?.open({
      start: this.telemetryStartDate,
      end: this.telemetryEndDate,
      keys: this.keys,
      subject: { kind: 'user', userId: ref.userId, userName: ref.userName }
    }, ref.opener ?? undefined);
  }

  /** Opens the Usage by model dialog for one group's current members, over the tab's range and Keys filter. */
  openGroupModels(ref: TelemetryGroupModelsRef) {
    this.userModelsDialog?.open({
      start: this.telemetryStartDate,
      end: this.telemetryEndDate,
      keys: this.keys,
      subject: { kind: 'group', groupId: ref.groupId, groupName: ref.groupName }
    }, ref.opener ?? undefined);
  }

  /** Stores the view record: the Keys filter and the breakdown's settings. The only writer of it. */
  private saveView(view: TelemetryBreakdownView) {
    this.view = view;
    writeBreakdownView(view);
  }
}
