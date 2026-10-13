import { Injectable, inject } from '@angular/core';
import { Observable, Subject } from 'rxjs';
import { map, switchMap, tap } from 'rxjs/operators';
import { AdminService, DefaultApiKeyStatus, GroupDto, SystemAiConfigDto } from '../services/admin.service';
import { AdminUserActionKind, AdminUserCriteria, AdminUserSort } from '../services/admin-users.service';
import { DefaultKeyInfo } from '../shared/ai-model-form/ai-model-form.component';
import { readStoredUserSort } from './users-tab/user-criteria/user-criteria';

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
export type AdminUsersSubTab = 'users' | 'deletions' | 'log';

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

/** The AI Telemetry date range, kept while the tab is closed. Empty dates mean no range yet. */
export interface AdminTelemetryRange {
  timeSpan: TelemetryTimeSpan;
  start: string;
  end: string;
}

/**
 * State the Admin page's tabs share, or keep across a tab switch. Provided by AdminComponent, so it
 * lives exactly as long as the page.
 */
@Injectable()
export class AdminPageStore {
  private adminService = inject(AdminService);

  groups: GroupDto[] = [];
  configs: SystemAiConfigDto[] = [];

  /** Emits after `configs` is replaced or one of its entries changes. */
  readonly configsChanged$ = new Subject<void>();

  /** The per-provider default keys, for the API Keys tab and the configuration form. */
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

  /** Set while a maintenance request is outstanding, so a re-entered Database tab still refuses a second one. */
  maintenanceInFlight = false;

  /** A configuration a link asked the System Configs tab to resolve; cleared once the tab has read it. */
  pendingResolveConfigId: number | null = null;

  /** Emits when `pendingResolveConfigId` is set. */
  readonly resolveConfigRequested$ = new Subject<void>();

  /** The Users tab's selected sub-tab, kept while the tab is closed. */
  usersSubTab: AdminUsersSubTab = 'users';

  /** Emits a top-level tab id (`AdminTabId`) a tab asks the Admin page to select. */
  readonly tabRequested$ = new Subject<string>();

  /** A user whose AI usage a link asked the AI Telemetry tab to show; cleared once the tab has read it. */
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

  /** Asks the Admin page to select a top-level tab. */
  requestTab(tab: string): void {
    this.tabRequested$.next(tab);
  }

  /** Asks the AI Telemetry tab to show one user's usage, and selects it. */
  requestTelemetryUser(userId: string, userName: string): void {
    this.pendingTelemetryUserId = userId;
    this.pendingTelemetryUserName = userName;
    this.telemetryUserRequested$.next();
    this.requestTab('telemetry');
  }

  /** Asks the Security tab to show one user's events, and selects it. */
  requestSecurityUser(userId: string, userName: string): void {
    this.pendingSecurityUserId = userId;
    this.pendingSecurityUserName = userName;
    this.securityUserRequested$.next();
    this.requestTab('security');
  }

  /** Asks the Users tab to open its Manage User Configs dialog for one user, and selects it. */
  requestManageUser(userId: string, userName: string): void {
    this.pendingManageUser = { userId, userName };
    this.manageUserRequested$.next();
    this.requestTab('users');
  }

  /** Shows the Users tab's Admin log with `filter`, and selects it. */
  showUsersLog(filter: AdminUsersLogFilter = {}): void {
    this.usersLogFilter = { ...filter, kinds: filter.kinds ? [...filter.kinds] : undefined };
    this.usersSubTab = 'log';
    this.usersLogFilterChanged$.next();
    this.requestTab('users');
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
