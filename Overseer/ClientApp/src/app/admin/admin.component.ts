import { Component, OnInit, OnDestroy, inject, ViewChild, ElementRef, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, ActivatedRouteSnapshot, ParamMap, Router, RouterModule } from '@angular/router';
import {
  AdminBenchmarkComponent, BenchmarkNavigationRequest, BenchmarkSubTabChange
} from './benchmark/benchmark.component';
import type { BenchmarkSubTab } from './benchmark/state/benchmark-shell-bridge.service';
import { ensureOverlayPolyfills } from '../utils/polyfills.util';
import { rovingTabIndex } from '../utils/roving-tab.util';
import { Subscription, combineLatest } from 'rxjs';
import { distinctUntilChanged, map } from 'rxjs/operators';
import { provideCharts } from 'ng2-charts';
import { APP_CHART_REGISTRABLES } from '../chart-registrables';
import { AdminPageStore } from './admin-page.store';
import { AdminTabId, adminCommands, legacyAdminPath, parseAdminPath } from './admin-url';
import { AdminUsersTabComponent } from './users-tab/admin-users-tab.component';
import { AdminConfigsTabComponent } from './configs-tab/admin-configs-tab.component';
import { AdminDatabaseTabComponent } from './database-tab/admin-database-tab.component';
import { AdminTelemetryTabComponent } from './telemetry-tab/admin-telemetry-tab.component';
import { AdminDevtoolsTabComponent } from './devtools-tab/admin-devtools-tab.component';
import { AdminSecurityTabComponent } from './security-tab/admin-security-tab.component';

/** One string per address: the path's tab and sub-tab and every query parameter. */
function addressKey(snapshot: ActivatedRouteSnapshot): string {
  const query = snapshot.queryParamMap;
  return JSON.stringify([
    snapshot.paramMap.get('tab'),
    snapshot.paramMap.get('subtab'),
    [...query.keys].sort().map(key => [key, query.getAll(key)])
  ]);
}

@Component({
    selector: 'app-admin',
    imports: [
      CommonModule, RouterModule, AdminBenchmarkComponent,
      AdminUsersTabComponent, AdminConfigsTabComponent, AdminDatabaseTabComponent,
      AdminTelemetryTabComponent, AdminSecurityTabComponent, AdminDevtoolsTabComponent
    ],
    templateUrl: './admin.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './admin.component.scss',
    providers: [AdminPageStore, provideCharts({ registerables: APP_CHART_REGISTRABLES })]
})
export class AdminComponent implements OnInit, OnDestroy {
  readonly store = inject(AdminPageStore);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  /** The selected tab, which the address sets through the page store. */
  get activeTab(): AdminTabId {
    return this.store.activeTab;
  }

  /** Tab order, and the source of truth for arrow-key navigation indices. */
  readonly tabs: { id: AdminTabId; label: string }[] = [
    { id: 'users',     label: 'Users' },
    { id: 'configs',   label: 'System Configs' },
    { id: 'database',  label: 'Database' },
    { id: 'telemetry', label: 'AI Telemetry' },
    { id: 'security',  label: 'Security' },
    { id: 'benchmark', label: 'GnollBench' },
    { id: 'devtools',  label: 'Developer Tools' }
  ];
  loading = false;

  @ViewChild('adminToast') adminToast?: ElementRef<HTMLElement>;

  /** Forgets the GnollBench sub-tab on screen once its panel is gone; a new panel starts on Run Benchmark. */
  @ViewChild(AdminBenchmarkComponent) set benchmarkPanel(panel: AdminBenchmarkComponent | undefined) {
    if (panel) {
      this.benchmarkPanelShown = true;
    } else if (this.benchmarkPanelShown) {
      this.benchmarkPanelShown = false;
      this.shownBenchmarkSubTab = null;
    }
  }

  private benchmarkPanelShown = false;

  /** A run the delete-config dialog asked the Benchmark tab to open; cleared once it has. */
  pendingRunId: number | null = null;

  /** A sub-tab and suite the address asked the Benchmark tab to show; cleared once it has. */
  pendingBenchmarkNavigation: BenchmarkNavigationRequest | null = null;

  /**
   * The GnollBench sub-tab the panel shows, as it reports it or as last handed to it; null while the
   * panel is not on screen. The address is compared with this, not with the store's memory.
   */
  shownBenchmarkSubTab: BenchmarkSubTab | null = null;

  private addressSub?: Subscription;
  private toastSub?: Subscription;

  /**
   * Applies an address to the page, in this order: an old `?tab=…&subtab=…` link is replaced by its
   * path; a non-canonical path by its canonical one, and a valid `resolveConfig` on another tab by
   * System Configs; otherwise the store takes the tab and sub-tab, the GnollBench panel the sub-tab it
   * does not show yet, and the one-shot `suiteId` and `resolveConfig` are applied and removed, so a
   * reload or Back does not apply them again. Every replacement keeps the other query parameters.
   */
  applyAddress(params: ParamMap, query: ParamMap): void {
    const legacy = legacyAdminPath(query);
    if (legacy) {
      this.router.navigate(legacy, {
        queryParams: { tab: null, subtab: null },
        queryParamsHandling: 'merge',
        replaceUrl: true
      });
      return;
    }

    const parsed = parseAdminPath(params.get('tab'), params.get('subtab'));
    const resolveConfigId = AdminComponent.positiveId(query.get('resolveConfig'));
    const resolveElsewhere = resolveConfigId != null && parsed.tab !== 'configs';
    if (!parsed.isCanonical || resolveElsewhere) {
      this.router.navigate(resolveElsewhere ? adminCommands('configs') : parsed.canonical, {
        queryParamsHandling: 'preserve',
        replaceUrl: true
      });
      return;
    }

    this.store.applyLocation(parsed.tab, parsed.subTab);

    if (parsed.tab === 'benchmark') {
      const subTab = parsed.subTab as BenchmarkSubTab;
      const suiteId = subTab === 'suites' ? AdminComponent.positiveId(query.get('suiteId')) : null;
      if (subTab !== this.shownBenchmarkSubTab || suiteId != null) {
        this.pendingBenchmarkNavigation = { subTab, suiteId };
        this.shownBenchmarkSubTab = subTab;
      }
    }

    if (resolveConfigId != null) {
      this.store.requestResolveConfig(resolveConfigId);
    }
    if (query.has('suiteId') || query.has('resolveConfig')) {
      this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { suiteId: null, resolveConfig: null },
        queryParamsHandling: 'merge',
        replaceUrl: true
      });
    }
  }

  /** A positive whole-number query parameter, or null. */
  private static positiveId(raw: string | null): number | null {
    return raw != null && /^\d+$/.test(raw) && Number(raw) > 0 ? Number(raw) : null;
  }

  ngOnInit() {
    ensureOverlayPolyfills();
    this.toastSub = this.store.toast$.subscribe(t => this.showAdminToast(t.message, t.type, t.title));

    this.loadData();

    // One subscription for path and query: a navigation that changes both emits twice, and the
    // snapshot, already final at the first emission, makes the second a duplicate that is dropped.
    // A tab change reuses this component, so the subscription sees every address.
    this.addressSub = combineLatest([this.route.paramMap, this.route.queryParamMap]).pipe(
      map(() => this.route.snapshot),
      distinctUntilChanged((a, b) => addressKey(a) === addressKey(b))
    ).subscribe(snapshot => this.applyAddress(snapshot.paramMap, snapshot.queryParamMap));
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
    this.addressSub?.unsubscribe();
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

  /** A click, Enter or Space: the tab on its remembered sub-tab, as a new history entry. */
  selectTab(tab: AdminTabId) {
    this.store.goTo(tab, null, 'push');
  }

  /**
   * The GnollBench panel switched sub-tabs: the address follows, a new entry only when the user chose
   * it. The panel carrying out a sub-tab the address handed it needs no navigation; one now could
   * overtake the removal of a one-shot `suiteId` still under way.
   */
  onBenchmarkSubTabChange(change: BenchmarkSubTabChange): void {
    if (!change.byUser && change.tab === this.shownBenchmarkSubTab) {
      return;
    }
    this.shownBenchmarkSubTab = change.tab;
    this.store.goTo('benchmark', change.tab, change.byUser ? 'push' : 'replace');
  }

  /** Hands a run named by the Configs tab's delete dialog to the Benchmark tab's run detail dialog. */
  openBlockerRun(runId: number): void {
    this.store.goTo('benchmark', null, 'replace');
    this.pendingRunId = runId;
  }

  /**
   * Roving-tabindex keyboard support required by role="tablist": Left/Right
   * move between tabs and wrap around, Home/End jump to the ends. Enter and
   * Space need no handling because each tab is a real <button>.
   *
   * Focus follows selection in the same turn, so the tab that just became
   * tabindex="0" is the one holding focus. Selection by arrow key replaces the
   * history entry, so holding a key does not fill the history.
   */
  onTabKeydown(event: KeyboardEvent, index: number): void {
    const next = rovingTabIndex(event, index, this.tabs.length);
    if (next === null) return;
    const tab = this.tabs[next].id;
    this.store.goTo(tab, null, 'replace');
    document.getElementById(`admin-tab-${tab}`)?.focus();
  }
}
