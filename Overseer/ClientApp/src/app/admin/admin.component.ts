import { Component, OnInit, OnDestroy, inject, ViewChild, ElementRef, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, ParamMap, Router, RouterModule } from '@angular/router';
import { AdminApiKeysComponent } from './admin-api-keys/admin-api-keys.component';
import { AdminBenchmarkComponent, BenchmarkNavigationRequest } from './benchmark/benchmark.component';
import { ensureOverlayPolyfills } from '../utils/polyfills.util';
import { Subscription } from 'rxjs';
import { AdminPageStore } from './admin-page.store';
import { AdminUsersTabComponent } from './users-tab/admin-users-tab.component';
import { AdminGroupsTabComponent } from './groups-tab/admin-groups-tab.component';
import { AdminConfigsTabComponent } from './configs-tab/admin-configs-tab.component';
import { AdminDatabaseTabComponent } from './database-tab/admin-database-tab.component';
import { AdminTelemetryTabComponent } from './telemetry-tab/admin-telemetry-tab.component';
import { AdminDevtoolsTabComponent } from './devtools-tab/admin-devtools-tab.component';

/** The admin dashboard's top-level tabs, in display order. */
export type AdminTabId =
  'users' | 'groups' | 'apikeys' | 'configs' | 'database' | 'devtools' | 'telemetry' | 'benchmark';

@Component({
    selector: 'app-admin',
    imports: [
      CommonModule, RouterModule, AdminApiKeysComponent, AdminBenchmarkComponent,
      AdminUsersTabComponent, AdminGroupsTabComponent, AdminConfigsTabComponent, AdminDatabaseTabComponent,
      AdminTelemetryTabComponent, AdminDevtoolsTabComponent
    ],
    templateUrl: './admin.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './admin.component.scss',
    providers: [AdminPageStore]
})
export class AdminComponent implements OnInit, OnDestroy {
  readonly store = inject(AdminPageStore);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  activeTab: AdminTabId = 'users';

  /** Tab order, and the source of truth for arrow-key navigation indices. */
  readonly tabs: { id: AdminTabId; label: string }[] = [
    { id: 'users',     label: 'Users' },
    { id: 'groups',    label: 'Groups' },
    { id: 'apikeys',   label: 'API Keys' },
    { id: 'configs',   label: 'System Configs' },
    { id: 'database',  label: 'Database' },
    { id: 'telemetry', label: 'AI Telemetry' },
    { id: 'benchmark', label: 'AI Benchmark' },
    { id: 'devtools',  label: 'Developer Tools' }
  ];
  loading = false;

  @ViewChild('adminToast') adminToast?: ElementRef<HTMLElement>;

  /** A run the delete-config dialog asked the Benchmark tab to open; cleared once it has. */
  pendingRunId: number | null = null;

  /** A sub-tab and suite a link asked the Benchmark tab to show; cleared once it has. */
  pendingBenchmarkNavigation: BenchmarkNavigationRequest | null = null;

  private queryParamSub?: Subscription;
  private toastSub?: Subscription;

  /**
   * Applies the `tab`, `subtab` and `suiteId` query parameters a link carries, then removes them
   * from the address, so a reload or Back does not apply them again.
   */
  applyNavigationParams(params: ParamMap): void {
    if (!params.has('tab') && !params.has('subtab') && !params.has('suiteId')) {
      return;
    }
    const tab = this.tabs.find(t => t.id === params.get('tab'))?.id;
    if (tab) {
      if (tab === 'benchmark') {
        const rawSuiteId = params.get('suiteId');
        const suiteId = rawSuiteId != null && /^\d+$/.test(rawSuiteId) && Number(rawSuiteId) > 0
          ? Number(rawSuiteId)
          : null;
        this.pendingBenchmarkNavigation = { subTab: params.get('subtab'), suiteId };
      }
      this.selectTab(tab);
    }
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab: null, subtab: null, suiteId: null },
      queryParamsHandling: 'merge',
      replaceUrl: true
    });
  }

  ngOnInit() {
    ensureOverlayPolyfills();
    this.toastSub = this.store.toast$.subscribe(t => this.showAdminToast(t.message, t.type, t.title));

    this.loadData();

    // A subscription rather than a snapshot: following a link to /admin from /admin reuses this component.
    this.queryParamSub = this.route.queryParamMap.subscribe(params => this.applyNavigationParams(params));
  }
  adminToastMessage = '';
  adminToastTitle = '';
  adminToastType: 'success' | 'error' | 'info' = 'success';
  private adminToastTimeout: any;

  showAdminToast(message: string, type: 'success' | 'error' | 'info' = 'success', title?: string, durationMs = 5000) {
    this.adminToastMessage = message;
    this.adminToastType = type;
    this.adminToastTitle = title || (type === 'success' ? 'Success' : type === 'error' ? 'Error' : 'Notification');

    const toast = this.adminToast?.nativeElement as any || document.getElementById('adminToast');
    if (toast && ('showPopover' in toast || 'show' in toast)) {
      try {
        if (!toast.matches(':popover-open')) {
          toast.showPopover();
        }
      } catch {
        try { toast.showPopover(); } catch {}
      }

      if (this.adminToastTimeout) {
        clearTimeout(this.adminToastTimeout);
      }

      this.adminToastTimeout = setTimeout(() => {
        this.hideAdminToast();
      }, durationMs);
    }
  }

  hideAdminToast() {
    if (this.adminToastTimeout) {
      clearTimeout(this.adminToastTimeout);
      this.adminToastTimeout = null;
    }
    const toast = this.adminToast?.nativeElement as any || document.getElementById('adminToast');
    if (toast && 'hidePopover' in toast) {
      try {
        if (toast.matches(':popover-open')) {
          toast.hidePopover();
        }
      } catch {
        try { toast.hidePopover(); } catch {}
      }
    }
  }

  ngOnDestroy() {
    this.queryParamSub?.unsubscribe();
    this.toastSub?.unsubscribe();
    if (this.adminToastTimeout) {
      clearTimeout(this.adminToastTimeout);
    }
  }

  /** The default keys, groups and configurations every tab reads; the tab content waits for them. */
  loadData() {
    this.loading = true;
    this.store.loadDefaultApiKeys();
    this.store.loadInitial().subscribe({
      next: () => {
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      }
    });
  }

  /** Each tab component loads what it shows when it is created; API Keys reads the page's keys. */
  selectTab(tab: AdminTabId) {
    this.activeTab = tab;
    if (tab === 'apikeys') {
      this.store.loadDefaultApiKeys();
    }
  }

  /** Hands a run named by the Configs tab's delete dialog to the Benchmark tab's run detail dialog. */
  openBlockerRun(runId: number): void {
    this.selectTab('benchmark');
    this.pendingRunId = runId;
  }

  /**
   * Roving-tabindex keyboard support required by role="tablist": Left/Right
   * move between tabs and wrap around, Home/End jump to the ends. Enter and
   * Space need no handling because each tab is a real <button>.
   *
   * Focus follows selection in the same turn, so the tab that just became
   * tabindex="0" is the one holding focus.
   */
  onTabKeydown(event: KeyboardEvent, index: number): void {
    const targets: Record<string, number> = {
      ArrowRight: index + 1,
      ArrowLeft: index - 1,
      Home: 0,
      End: this.tabs.length - 1
    };
    const requested = targets[event.key];
    if (requested === undefined) {
      return;
    }

    event.preventDefault();
    const next = (requested + this.tabs.length) % this.tabs.length;
    const tab = this.tabs[next].id;
    this.selectTab(tab);
    document.getElementById(`admin-tab-${tab}`)?.focus();
  }
}
