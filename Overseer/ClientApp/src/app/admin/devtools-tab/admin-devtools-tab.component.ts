import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import * as Sentry from '@sentry/angular';
import { AdminService } from '../../services/admin.service';
import { AdminDevtoolsSubTab, AdminHistoryMode, AdminPageStore } from '../admin-page.store';
import { rovingTabIndex } from '../../utils/roving-tab.util';
import { SourceDashboardComponent } from './source-dashboard/source-dashboard.component';
import { ConfigHealthComponent } from './config-health/config-health.component';
import { GnollHackWikiDashboardComponent } from './gnollhack-wiki-dashboard/gnollhack-wiki-dashboard.component';
import { NetHackWikiDashboardComponent } from './nethack-wiki-dashboard/nethack-wiki-dashboard.component';
import { KnowledgeBaseDashboardComponent } from './knowledge-base-dashboard/knowledge-base-dashboard.component';
import { HealthStatusComponent } from './shared/health-status/health-status.component';
import { LoadingIndicatorComponent } from '../../shared/loading-indicator/loading-indicator.component';

/** The two Sentry tests on the Developer Tools sub-tab. */
export type SentryTestKind = 'frontend' | 'backend';

/** A Sentry test's card: its heading, what it does, the issue it raises and its button. */
export interface SentryTest {
  kind: SentryTestKind;
  title: string;
  description: string;
  /** The exception message, which Sentry shows as the issue title. */
  issueTitle: string;
  buttonLabel: string;
}

/** The last click of a Sentry test: its state, the local time of the click and the HTTP status. */
export interface SentryTestResult {
  state: 'idle' | 'sending' | 'sent' | 'failed' | 'unexpected';
  at?: string;
  httpStatus?: number;
}

/**
 * The Admin page's Services tab, in six sub-tabs: the GnollHack source, the GnollHack wiki, the
 * NetHack wiki and the knowledge base with their indexes; the configuration health checks; and
 * developer tools (deliberate frontend and backend errors for testing Sentry, with their results).
 */
@Component({
  selector: 'app-admin-devtools-tab',
  imports: [SourceDashboardComponent, GnollHackWikiDashboardComponent, NetHackWikiDashboardComponent,
    KnowledgeBaseDashboardComponent, ConfigHealthComponent, RouterLink, HealthStatusComponent, LoadingIndicatorComponent],
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

  /** The Sentry tests, in display order. */
  readonly sentryTests: readonly SentryTest[] = [
    {
      kind: 'frontend',
      title: 'Frontend error',
      description: "Throws an unhandled error in this browser tab. Angular's Sentry error handler reports it.",
      issueTitle: 'Sentry Frontend Crash Test triggered by Admin',
      buttonLabel: 'Test Frontend Error'
    },
    {
      kind: 'backend',
      title: 'Backend error',
      description: "Asks the server to throw an unhandled exception (POST /api/admin/test-sentry). The server's Sentry integration reports it.",
      issueTitle: 'Sentry Backend Crash Test triggered by Admin',
      buttonLabel: 'Test Backend Error'
    }
  ];

  /** Each test's last result, shown in its result line until the next click. */
  readonly sentryResults: Record<SentryTestKind, SentryTestResult> = {
    frontend: { state: 'idle' },
    backend: { state: 'idle' }
  };

  /** The release and environment the browser's Sentry reports carry. */
  readonly sentryRelease: string;
  readonly sentryEnvironment: string;

  constructor() {
    const options = Sentry.getClient()?.getOptions();
    this.sentryRelease = options?.release || 'Not initialized';
    this.sentryEnvironment = options?.environment || 'Not initialized';
  }

  result(kind: SentryTestKind): SentryTestResult {
    return this.sentryResults[kind];
  }

  runSentryTest(kind: SentryTestKind): void {
    if (kind === 'frontend') {
      this.triggerFrontendSentryError();
    } else {
      this.triggerBackendSentryError();
    }
  }

  /** Throws an unhandled error, which Angular hands to the Sentry error handler. */
  triggerFrontendSentryError(): void {
    this.sentryResults.frontend = { state: 'sent', at: new Date().toLocaleTimeString() };
    throw new Error('Sentry Frontend Crash Test triggered by Admin');
  }

  /** Asks the server to throw; an HTTP 5xx answer means it did, and Sentry has the report. */
  triggerBackendSentryError(): void {
    if (this.sentryResults.backend.state === 'sending') {
      return;
    }
    this.sentryResults.backend = { state: 'sending' };
    this.adminService.triggerBackendSentryError().subscribe({
      next: () => this.sentryResults.backend = { state: 'unexpected', at: new Date().toLocaleTimeString() },
      error: (err: HttpErrorResponse) => this.sentryResults.backend = {
        state: err.status >= 500 ? 'sent' : 'failed',
        at: new Date().toLocaleTimeString(),
        httpStatus: err.status
      }
    });
  }
}
