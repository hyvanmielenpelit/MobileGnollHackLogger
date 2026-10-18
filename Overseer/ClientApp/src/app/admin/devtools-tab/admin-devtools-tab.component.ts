import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { AdminService } from '../../services/admin.service';
import { AdminDevtoolsSubTab, AdminHistoryMode, AdminPageStore } from '../admin-page.store';
import { rovingTabIndex } from '../../utils/roving-tab.util';
import { SourceDashboardComponent } from './source-dashboard/source-dashboard.component';
import { ConfigHealthComponent } from './config-health/config-health.component';
import { GnollHackWikiDashboardComponent } from './gnollhack-wiki-dashboard/gnollhack-wiki-dashboard.component';
import { NetHackWikiDashboardComponent } from './nethack-wiki-dashboard/nethack-wiki-dashboard.component';
import { KnowledgeBaseDashboardComponent } from './knowledge-base-dashboard/knowledge-base-dashboard.component';

/**
 * The Admin page's Services tab, in six sub-tabs: the GnollHack source, the GnollHack wiki, the
 * NetHack wiki and the knowledge base with their indexes; the configuration health checks; and
 * developer tools (deliberate frontend and backend errors for testing Sentry).
 */
@Component({
  selector: 'app-admin-devtools-tab',
  imports: [SourceDashboardComponent, GnollHackWikiDashboardComponent, NetHackWikiDashboardComponent,
    KnowledgeBaseDashboardComponent, ConfigHealthComponent],
  templateUrl: './admin-devtools-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-devtools-tab.component.scss'
})
export class AdminDevtoolsTabComponent {
  private adminService = inject(AdminService);
  private store = inject(AdminPageStore);

  /** The sub-tabs, in display order. */
  readonly subTabs: readonly AdminDevtoolsSubTab[] =
    ['source', 'gnollhack-wiki', 'nethack-wiki', 'knowledge-base', 'config-health', 'sentry'];

  /** Each sub-tab's visible label. */
  readonly subTabLabels: Readonly<Record<AdminDevtoolsSubTab, string>> = {
    'source': 'GnollHack Source',
    'gnollhack-wiki': 'GnollHack Wiki',
    'nethack-wiki': 'NetHack Wiki',
    'knowledge-base': 'Knowledge Base',
    'config-health': 'Configuration Health',
    'sentry': 'Developer Tools'
  };

  /** The selected sub-tab, which the Admin page sets in the page store from the address. */
  get subTab(): AdminDevtoolsSubTab {
    return this.store.devtoolsSubTab;
  }

  /**
   * Selects a sub-tab through the address: a click or Enter/Space adds a history entry, anything
   * else replaces it. Each panel loads what it shows when it appears.
   */
  selectSubTab(tab: AdminDevtoolsSubTab, mode: AdminHistoryMode = 'push'): void {
    if (tab === this.subTab) {
      return;
    }
    this.store.goTo('devtools', tab, mode);
  }

  /** Left/Right move and wrap, Home/End jump to the ends; focus follows selection. */
  onSubTabKeydown(event: KeyboardEvent, index: number): void {
    const next = rovingTabIndex(event, index, this.subTabs.length);
    if (next === null) return;
    const tab = this.subTabs[next];
    this.selectSubTab(tab, 'replace');
    document.getElementById(`devtools-subtab-${tab}`)?.focus();
  }

  triggerFrontendSentryError() {
    this.store.showToast('Triggering frontend exception...', 'info', 'Sentry Test');
    throw new Error('Sentry Frontend Crash Test triggered by Admin');
  }

  triggerBackendSentryError() {
    this.store.showToast('Sending backend crash request...', 'info', 'Sentry Test');
    this.adminService.triggerBackendSentryError().subscribe({
      next: () => this.store.showToast('Backend crash request completed unexpectedly successfully.', 'success', 'Sentry Test'),
      error: () => this.store.showToast('Backend crash request completed (check Sentry!)', 'info', 'Sentry Test')
    });
  }
}
