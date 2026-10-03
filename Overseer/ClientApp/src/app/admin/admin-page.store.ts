import { Injectable, inject } from '@angular/core';
import { Observable, Subject } from 'rxjs';
import { map, switchMap, tap } from 'rxjs/operators';
import { AdminService, DefaultApiKeyStatus, GroupDto, SystemAiConfigDto } from '../services/admin.service';
import { DefaultKeyInfo } from '../shared/ai-model-form/ai-model-form.component';

export type AdminToastType = 'success' | 'error' | 'info';

/** A toast a tab asks the Admin page to show. */
export interface AdminToastRequest {
  message: string;
  type: AdminToastType;
  title?: string;
}

/** The Users table's query, kept while the tab is closed. */
export interface AdminUsersQuery {
  page: number;
  pageSize: number;
  sortColumn: string;
  sortOrder: 'asc' | 'desc';
  usernameFilter: string;
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

  usersQuery: AdminUsersQuery = {
    page: 1, pageSize: 10, sortColumn: 'UserName', sortOrder: 'asc', usernameFilter: ''
  };

  telemetryRange: AdminTelemetryRange = { timeSpan: '30d', start: '', end: '' };

  /** Set while a maintenance request is outstanding, so a re-entered Database tab still refuses a second one. */
  maintenanceInFlight = false;

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

  /** Replaces one configuration in place, keeping the list's identity and order. */
  replaceConfig(updated: SystemAiConfigDto): void {
    const idx = this.configs.findIndex(c => c.id === updated.id);
    if (idx !== -1) {
      this.configs[idx] = updated;
      this.configsChanged$.next();
    }
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
