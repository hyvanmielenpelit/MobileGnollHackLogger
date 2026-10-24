import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, Subject } from 'rxjs';
import { map, switchMap, tap } from 'rxjs/operators';
import { AdminService, DefaultApiKeyStatus, GroupDto, SystemAiConfigDto } from '../services/admin.service';
import { AdminUserActionKind, AdminUserCriteria, AdminUserSort } from '../services/admin-users.service';
import { AdminFeedbackCounts, AdminFeedbackService } from '../services/admin-feedback.service';
import { DefaultKeyInfo } from '../shared/ai-model-form/ai-model-form.component';
import { readStoredUserSort } from './users-tab/user-criteria/user-criteria';
import { ADMIN_SUBTAB_SLUGS, AdminTabId, adminCommands, defaultAdminSubTab, isAdminTabId } from './admin-url';
import type { BenchmarkSubTab } from './benchmark/state/benchmark-shell-bridge.service';

export type AdminToastType = 'success' | 'error' | 'info';

/** A toast a tab asks the Admin page to show. */
export interface AdminToastRequest {
  message: string;
  type: AdminToastType;
  title?: string;
}

/** The user list's criteria and Sort by, kept while the tab is closed. */
export interface AdminUsersQuery {
  criteria: AdminUserCriteria;
  sort: AdminUserSort;
}

/** The Users tab's sub-tabs. */
export type AdminUsersSubTab = 'users' | 'groups' | 'deletions' | 'log';

/** The Services tab's sub-tabs. */
export type AdminDevtoolsSubTab = 'source' | 'gnollhack-wiki' | 'nethack-wiki' | 'knowledge-base' | 'config-health' | 'sentry';

/** The AI Telemetry tab's sub-tabs. */
export type AdminTelemetrySubTab = 'summary' | 'models' | 'users' | 'groups';

/** The Security tab's sub-tabs. */
export type AdminSecuritySubTab = 'summary' | 'events' | 'settings';

/** How a store navigation treats the browser history: a new entry, or the current one replaced. */
export type AdminHistoryMode = 'push' | 'replace';

/** What the Admin log sub-tab shows; every field is optional. `subjectUserName` and the operation id label its chips. */
export interface AdminUsersLogFilter {
  search?: string;
  kinds?: AdminUserActionKind[];
  subjectUserId?: string;
  subjectUserName?: string;
  bulkOperationId?: number;
}

/**
 * How the Bulk Actions wizard opens: on step 1 seeded with `criteria`, or on the results of an
 * operation (`operationId`).
 */
export interface AdminBulkWizardRequest {
  criteria?: AdminUserCriteria;
  operationId?: number;
}

export type TelemetryTimeSpan = '7d' | '28d' | '30d' | '90d' | '180d' | '1y' | 'custom';

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

/** The AI Telemetry date range, kept while the tab is closed. Empty dates mean no range yet. */
export interface AdminTelemetryRange {
  timeSpan: TelemetryTimeSpan;
  start: string;
  end: string;
}

/** The Feedback tab's date range presets; each sub-tab remembers its own range in `localStorage`. */
export type FeedbackTimeSpan = 'all' | '7d' | '30d' | '90d' | '1y' | 'custom';

/** The Feedback tab's sub-tabs: the Overseer chat's issues and GnollBench's self-reports. */
export type FeedbackSubTab = 'overseer' | 'gnollbench';

/**
 * State the Admin page's tabs share, or keep across a tab switch. Provided by AdminComponent, so it
 * lives exactly as long as the page.
 */
@Injectable()
export class AdminPageStore {
  private adminService = inject(AdminService);
  private feedbackService = inject(AdminFeedbackService);
  private router = inject(Router);

  groups: GroupDto[] = [];
  configs: SystemAiConfigDto[] = [];

  /** Emits after `configs` is replaced or one of its entries changes. */
  readonly configsChanged$ = new Subject<void>();

  /** The per-provider default keys, for the API Keys dialog and the configuration form. */
  defaultApiKeys: DefaultApiKeyStatus[] = [];
  defaultApiKeysLoading = false;
  /** Provider → what the configuration form needs to offer Default; rebuilt only when the keys load. */
  defaultKeysForForm: Record<string, DefaultKeyInfo> = {};

  readonly toast$ = new Subject<AdminToastRequest>();

  usersQuery: AdminUsersQuery = { criteria: {}, sort: readStoredUserSort() };

  /** The Admin log's filter; *Show in admin log* links preset it through `showUsersLog`. */
  usersLogFilter: AdminUsersLogFilter = {};

  /** Emits when `usersLogFilter` is replaced, for an Admin log already on screen. */
  readonly usersLogFilterChanged$ = new Subject<void>();

  /** A Bulk Actions wizard opening a link asked for; cleared once the Users tab has read it. */
  pendingBulkWizard: AdminBulkWizardRequest | null = null;

  /** Emits when `pendingBulkWizard` is set. */
  readonly bulkWizardRequested$ = new Subject<void>();

  telemetryRange: AdminTelemetryRange = { timeSpan: '30d', start: '', end: '' };

  /** Feedback issue counts per status over all dates; null until they arrive, or when they fail. */
  feedbackCounts: AdminFeedbackCounts | null = null;

  /** Emits after `feedbackCounts` is replaced. */
  readonly feedbackCountsChanged$ = new Subject<void>();

  /** Set while a maintenance request is outstanding, so a re-entered Database tab still refuses a second one. */
  maintenanceInFlight = false;

  /** A configuration a link asked the System Configs tab to resolve; cleared once the tab has read it. */
  pendingResolveConfigId: number | null = null;

  /** Emits when `pendingResolveConfigId` is set. */
  readonly resolveConfigRequested$ = new Subject<void>();

  /** The selected top-level tab: set from the address, and by `goTo` before the address follows. */
  activeTab: AdminTabId = 'users';

  /** The Users tab's selected sub-tab, kept while the tab is closed. */
  usersSubTab: AdminUsersSubTab = 'users';

  /** The AI Telemetry tab's selected sub-tab, kept while the tab is closed. */
  telemetrySubTab: AdminTelemetrySubTab = 'summary';

  /** The Security tab's selected sub-tab, kept while the tab is closed. */
  securitySubTab: AdminSecuritySubTab = 'summary';

  /** The Services tab's selected sub-tab, kept while the tab is closed. */
  devtoolsSubTab: AdminDevtoolsSubTab = 'source';

  /** The GnollBench sub-tab last shown, kept while the tab is closed. */
  benchmarkSubTab: BenchmarkSubTab = 'run';

  /** The Feedback tab's selected sub-tab, kept while the tab is closed. */
  feedbackSubTab: FeedbackSubTab = 'overseer';

  /** True while the address is `/admin/configs/api-keys`, where System Configs shows its API Keys dialog. */
  apiKeysDialogOpen = false;

  /** Emits when `apiKeysDialogOpen` changes. */
  readonly apiKeysDialogChanged$ = new Subject<void>();

  /** A user whose AI usage a link asked the AI Telemetry Users view to show; cleared once the view has read it. */
  pendingTelemetryUserId: string | null = null;
  pendingTelemetryUserName: string | null = null;

  /** Emits when `pendingTelemetryUserId` is set. */
  readonly telemetryUserRequested$ = new Subject<void>();

  /** A user whose security events a link asked the Security tab to show; cleared once the tab has read it. */
  pendingSecurityUserId: string | null = null;
  pendingSecurityUserName: string | null = null;

  /** Emits when `pendingSecurityUserId` is set. */
  readonly securityUserRequested$ = new Subject<void>();

  /** A user whose configuration limits a link asked the Users tab to manage; cleared once the tab has read it. */
  pendingManageUser: { userId: string; userName: string } | null = null;

  /** Emits when `pendingManageUser` is set. */
  readonly manageUserRequested$ = new Subject<void>();

  /** A group whose configuration limits a link asked the Groups sub-tab to manage; cleared once the sub-tab has read it. */
  pendingManageGroup: { groupId: number; groupName: string } | null = null;

  /** Emits when `pendingManageGroup` is set. */
  readonly manageGroupRequested$ = new Subject<void>();

  showToast(message: string, type: AdminToastType = 'success', title?: string): void {
    this.toast$.next({ message, type, title });
  }

  /** The page's first load: the groups, then the configurations. */
  loadInitial(): Observable<void> {
    return this.adminService.getGroups().pipe(
      tap(g => this.groups = g),
      switchMap(() => this.adminService.getSystemConfigs()),
      tap(c => this.setConfigs(c)),
      map(() => undefined)
    );
  }

  loadGroups(): void {
    this.adminService.getGroups().subscribe({
      next: (g) => this.groups = g,
      error: () => {}
    });
  }

  loadConfigs(): void {
    this.adminService.getSystemConfigs().subscribe({
      next: (c) => this.setConfigs(c),
      error: () => {}
    });
  }

  /** Fetches the feedback counts; a failure leaves null, so the tab shows no pill. */
  loadFeedbackCounts(): void {
    this.feedbackService.getCounts().subscribe({
      next: (counts) => this.setFeedbackCounts(counts),
      error: () => this.setFeedbackCounts(null)
    });
  }

  private setFeedbackCounts(counts: AdminFeedbackCounts | null): void {
    this.feedbackCounts = counts;
    this.feedbackCountsChanged$.next();
  }

  setConfigs(configs: SystemAiConfigDto[]): void {
    this.configs = configs;
    this.configsChanged$.next();
  }

  /** Replaces the configuration with the same id in a new list, keeping the order. */
  replaceConfig(updated: SystemAiConfigDto): void {
    const idx = this.configs.findIndex(c => c.id === updated.id);
    if (idx !== -1) {
      this.configs = this.configs.map(c => c.id === updated.id ? updated : c);
      this.configsChanged$.next();
    }
  }

  /** Asks the System Configs tab to open the model resolution dialog for a configuration. */
  requestResolveConfig(id: number): void {
    this.pendingResolveConfigId = id;
    this.resolveConfigRequested$.next();
  }

  /**
   * Selects `tab` and `subTab` — by default the sub-tab remembered for that tab — and moves the
   * address there, keeping every query parameter. `push` adds a history entry, `replace` replaces
   * the current one. The Admin page applies the address again once it arrives.
   */
  goTo(tab: AdminTabId, subTab?: string | null, mode: AdminHistoryMode = 'replace'): void {
    const target = subTab ?? this.rememberedSubTab(tab);
    this.applyLocation(tab, target);
    // A navigation the router fails is reported by its own NavigationError event.
    this.router.navigate(adminCommands(tab, target), {
      queryParamsHandling: 'preserve',
      replaceUrl: mode === 'replace'
    }).catch(() => false);
  }

  /**
   * Records `tab` as selected and `subTab` as its sub-tab; an unknown sub-tab is the tab's default.
   * The API Keys dialog is open only on System Configs → API Keys.
   */
  applyLocation(tab: AdminTabId, subTab: string | null): void {
    const known = ADMIN_SUBTAB_SLUGS[tab]?.some(([id]) => id === subTab) ? subTab : defaultAdminSubTab(tab);
    this.activeTab = tab;
    if (tab === 'users') {
      this.usersSubTab = known as AdminUsersSubTab;
    } else if (tab === 'telemetry') {
      this.telemetrySubTab = known as AdminTelemetrySubTab;
    } else if (tab === 'security') {
      this.securitySubTab = known as AdminSecuritySubTab;
    } else if (tab === 'devtools') {
      this.devtoolsSubTab = known as AdminDevtoolsSubTab;
    } else if (tab === 'benchmark') {
      this.benchmarkSubTab = known as BenchmarkSubTab;
    } else if (tab === 'feedback') {
      this.feedbackSubTab = known as FeedbackSubTab;
    }
    const dialogOpen = tab === 'configs' && known === 'api-keys';
    if (dialogOpen !== this.apiKeysDialogOpen) {
      this.apiKeysDialogOpen = dialogOpen;
      this.apiKeysDialogChanged$.next();
    }
  }

  /** The sub-tab a tab returns to; System Configs always returns without its API Keys dialog. */
  private rememberedSubTab(tab: AdminTabId): string | null {
    switch (tab) {
      case 'users': return this.usersSubTab;
      case 'telemetry': return this.telemetrySubTab;
      case 'security': return this.securitySubTab;
      case 'devtools': return this.devtoolsSubTab;
      case 'benchmark': return this.benchmarkSubTab;
      case 'feedback': return this.feedbackSubTab;
      default: return null;
    }
  }

  /** Selects a top-level tab on its remembered sub-tab; an id naming no tab is ignored. */
  requestTab(tab: string): void {
    if (isAdminTabId(tab)) {
      this.goTo(tab);
    }
  }

  /** Asks the AI Telemetry Users view to show one user's usage, and selects it. */
  requestTelemetryUser(userId: string, userName: string): void {
    this.pendingTelemetryUserId = userId;
    this.pendingTelemetryUserName = userName;
    this.telemetryUserRequested$.next();
    this.goTo('telemetry', 'users');
  }

  /** Asks the Security tab to show one user's events, and selects its Events sub-tab. */
  requestSecurityUser(userId: string, userName: string): void {
    this.pendingSecurityUserId = userId;
    this.pendingSecurityUserName = userName;
    this.securityUserRequested$.next();
    this.goTo('security', 'events');
  }

  /** Asks the Users tab to open its AI configuration access dialog for one user, and selects it. */
  requestManageUser(userId: string, userName: string): void {
    this.pendingManageUser = { userId, userName };
    this.manageUserRequested$.next();
    this.requestTab('users');
  }

  /** Asks the Users tab's Groups sub-tab to open its AI configuration access dialog for one group, and selects it. */
  requestManageGroup(groupId: number, groupName: string): void {
    this.pendingManageGroup = { groupId, groupName };
    this.manageGroupRequested$.next();
    this.goTo('users', 'groups');
  }

  /** Shows the Users tab's Admin log with `filter`, and selects it. */
  showUsersLog(filter: AdminUsersLogFilter = {}): void {
    this.usersLogFilter = { ...filter, kinds: filter.kinds ? [...filter.kinds] : undefined };
    this.usersLogFilterChanged$.next();
    this.goTo('users', 'log');
  }

  /** Asks the Users tab to open the Bulk Actions wizard, and selects it. */
  requestBulkWizard(request: AdminBulkWizardRequest): void {
    this.pendingBulkWizard = request;
    this.bulkWizardRequested$.next();
    this.requestTab('users');
  }

  /** A failed load keeps the keys already shown. */
  loadDefaultApiKeys() {
    this.defaultApiKeysLoading = true;
    this.adminService.getDefaultApiKeys().subscribe({
      next: (keys) => {
        this.defaultApiKeys = keys;
        this.defaultKeysForForm = Object.fromEntries(keys.map(k => [k.provider, {
          hasKey: k.hasKey,
          keyHint: k.keyHint,
          verified: k.verification?.status !== 'NotVerified'
        }]));
        this.defaultApiKeysLoading = false;
      },
      error: () => {
        this.defaultApiKeysLoading = false;
      }
    });
  }

  /** A saved, verified or deleted default key: a delete disables configurations, so they reload too. */
  onDefaultApiKeysChanged() {
    this.loadDefaultApiKeys();
    this.loadConfigs();
  }
}
