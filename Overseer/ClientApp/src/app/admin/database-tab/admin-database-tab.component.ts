import { Component, OnInit, OnDestroy, AfterViewInit, inject, ViewChild, ElementRef, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { AdminService, DatabaseStorageMetrics, MaintenanceResult, MaintenanceRunLog } from '../../services/admin.service';
import { TableState } from '../../shared/data-table/table-state';
import { TablePagerComponent } from '../../shared/data-table/table-pager.component';
import { AdminPageStore } from '../admin-page.store';
import { AdminConfirmDialogComponent } from '../admin-dialogs/admin-confirm-dialog.component';

/** Page sizes offered by the maintenance history pager; the first is the default. */
export const MAINTENANCE_HISTORY_PAGE_SIZES: readonly number[] = [10, 50, 100, 500, 1000];

/** The lifetime of one maintenance action in the run dialog. */
export type MaintenanceRunPhase = 'running' | 'completed' | 'failed';

/** The Admin page's Database tab: storage metrics, retention policy, maintenance actions and their history. */
@Component({
  selector: 'app-admin-database-tab',
  imports: [CommonModule, FormsModule, TablePagerComponent, AdminConfirmDialogComponent],
  templateUrl: './admin-database-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-database-tab.component.scss'
})
export class AdminDatabaseTabComponent implements OnInit, AfterViewInit, OnDestroy {
  private adminService = inject(AdminService);
  private store = inject(AdminPageStore);
  private cdr = inject(ChangeDetectorRef);

  @ViewChild('maintenanceRunDialog') maintenanceRunDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('maintenanceRunHeading') maintenanceRunHeading?: ElementRef<HTMLElement>;
  @ViewChild(AdminConfirmDialogComponent, { static: true }) confirmDialog!: AdminConfirmDialogComponent;

  // Database Tab state
  storageMetrics: DatabaseStorageMetrics | null = null;
  storageLoading = false;
  /** Kept in the page store, so a re-entered tab still refuses a second request. */
  get maintenanceLoading(): boolean {
    return this.store.maintenanceInFlight;
  }
  set maintenanceLoading(value: boolean) {
    this.store.maintenanceInFlight = value;
  }
  /** Governs the full pass and every granular action. */
  maintenanceDryRun = false;
  inactivityDays = 90;
  toolCallPruneDays = 30;
  benchmarkToolCallPruneDays = 90;
  auditLogRetentionDays = 365;
  aiErrorLogPruneDays = 90;
  /** Day-count selects take the configured policy once, on the first metrics load. */
  private maintenanceDefaultsApplied = false;
  lastMaintenanceResult: MaintenanceResult | null = null;
  /** The action name and client start time of `lastMaintenanceResult`, for the run dialog. */
  private lastMaintenanceRunLabel = '';
  lastMaintenanceRunStartedAt: Date | null = null;

  /** The current page of maintenance runs; the server pages, filters nothing and sorts newest first. */
  maintenanceHistory: MaintenanceRunLog[] = [];
  readonly maintenanceHistoryTable = new TableState<MaintenanceRunLog>(
    'startedUtc', 'desc', { pageSizes: MAINTENANCE_HISTORY_PAGE_SIZES });
  maintenanceHistoryTotal = 0;
  maintenanceHistoryLoading = false;
  expandedHistoryRunId: number | null = null;
  historyLogLoadingId: number | null = null;
  /** Discards a page response that a later page request has superseded. */
  private maintenanceHistoryRequest = 0;
  private static readonly MAINTENANCE_PAGE_SIZE_KEY = 'overseer.admin.maintenanceHistory.pageSize';

  // Maintenance run dialog
  maintenanceRunPhase: MaintenanceRunPhase = 'running';
  maintenanceRunTitle = '';
  maintenanceRunMessage = '';
  /** The full pass lists its ordered steps while running. */
  maintenanceRunShowsSteps = false;
  maintenanceRunElapsedMs = 0;
  private maintenanceRunLabel = '';
  private maintenanceRunStartedAt: Date | null = null;
  private maintenanceRunTimer?: ReturnType<typeof setInterval>;

  /** Set once the tab is destroyed; a request finishing later then leaves the view alone. */
  private destroyed = false;

  ngOnInit() {
    this.restoreMaintenanceHistoryPageSize();
    this.loadStorageMetrics();
  }

  /**
   * Light-dismiss for the maintenance run dialog where `closedby` is unsupported. A backdrop
   * click reports the dialog itself as the target, so a hit outside its border box closes it.
   * A running action is never light-dismissed, matching the `closedby` binding.
   */
  ngAfterViewInit(): void {
    if ('closedBy' in HTMLDialogElement.prototype) {
      return;
    }
    const dialog = this.maintenanceRunDialog?.nativeElement;
    dialog?.addEventListener('click', (event: MouseEvent) => {
      if (event.target !== dialog || this.maintenanceRunPhase === 'running') {
        return;
      }
      const rect = dialog.getBoundingClientRect();
      const inside = rect.top <= event.clientY && event.clientY <= rect.top + rect.height
        && rect.left <= event.clientX && event.clientX <= rect.left + rect.width;
      if (!inside) {
        this.closeMaintenanceRunDialog();
      }
    });
  }

  ngOnDestroy() {
    this.stopMaintenanceRunTimer();
    this.destroyed = true;
  }

  // --- Confirm Dialog ---
  openConfirmationModal(title: string, message: string, action: () => void, btnText = 'Confirm', btnClass = 'btn-gh btn-gh-delete') {
    this.confirmDialog.open({ title, message, buttonText: btnText, buttonClass: btnClass }, action);
  }

  // --- Maintenance Run Dialog ---

  /**
   * Opens the run dialog in its running phase. `label` names the action in the result title;
   * `title` is the heading while it runs.
   */
  openMaintenanceRunDialog(label: string, title: string, message: string, showsSteps = false) {
    this.stopMaintenanceRunTimer();
    this.maintenanceRunLabel = label;
    this.maintenanceRunTitle = title;
    this.maintenanceRunMessage = message;
    this.maintenanceRunShowsSteps = showsSteps;
    this.maintenanceRunPhase = 'running';
    this.maintenanceRunElapsedMs = 0;
    this.maintenanceRunStartedAt = new Date();
    const started = Date.now();
    this.maintenanceRunTimer = setInterval(() => {
      this.maintenanceRunElapsedMs = Date.now() - started;
    }, 100);
    this.showMaintenanceRunDialog();
  }

  /** Switches the dialog to the result in place. A hidden dialog stays hidden. */
  completeMaintenanceRun(result: MaintenanceResult) {
    this.stopMaintenanceRunTimer();
    this.lastMaintenanceResult = result;
    this.lastMaintenanceRunLabel = this.maintenanceRunLabel;
    this.lastMaintenanceRunStartedAt = this.maintenanceRunStartedAt;
    this.showResultPhase();
    this.focusMaintenanceRunHeading();
  }

  /** Records a request that never produced a result as a failed run, and shows it. */
  failMaintenanceRun(message: string) {
    this.completeMaintenanceRun({
      success: false, isDryRun: this.maintenanceDryRun, softDeletedCount: 0, purgedSessionCount: 0,
      purgedMessageCount: 0, purgedToolCallCount: 0, prunedToolResultCount: 0,
      prunedBenchmarkToolResultCount: 0, deletedDiskFolderCount: 0, deletedDiskFileCount: 0,
      reclaimedDiskBytes: 0, sweptOrphanFolderCount: 0, prunedAuditLogCount: 0, prunedAiErrorLogCount: 0,
      elapsedMilliseconds: this.maintenanceRunElapsedMs, trigger: 'Manual', errorMessage: message, logs: []
    });
  }

  /** Hides the dialog. A running request is not cancelled; its result still lands. */
  closeMaintenanceRunDialog() {
    this.stopMaintenanceRunTimer();
    const dialog = this.maintenanceRunDialog?.nativeElement;
    if (dialog?.open) {
      try {
        dialog.close();
      } catch {}
    }
  }

  onMaintenanceRunCancel(event: Event) {
    event.preventDefault();
    this.closeMaintenanceRunDialog();
  }

  private showResultPhase() {
    const result = this.lastMaintenanceResult!;
    this.maintenanceRunPhase = result.success ? 'completed' : 'failed';
    this.maintenanceRunTitle = `${this.lastMaintenanceRunLabel}: ${result.success ? 'Completed' : 'Failed'}`;
  }

  private showMaintenanceRunDialog() {
    const dialog = this.maintenanceRunDialog?.nativeElement;
    if (dialog && !dialog.open) {
      try {
        dialog.showModal();
      } catch {}
    }
  }

  /** The Hide button disappears with the running phase, so focus moves to the heading instead. */
  private focusMaintenanceRunHeading() {
    if (this.destroyed || !this.maintenanceRunDialog?.nativeElement.open) {
      return;
    }
    this.cdr.detectChanges();
    this.maintenanceRunHeading?.nativeElement.focus();
  }

  private stopMaintenanceRunTimer() {
    if (this.maintenanceRunTimer !== undefined) {
      clearInterval(this.maintenanceRunTimer);
      this.maintenanceRunTimer = undefined;
    }
  }

  /** The resolved capacity ceiling in GB, without a trailing ".0" on the round values. */
  limitGb(m: DatabaseStorageMetrics): string {
    const gb = m.maxLimitMb / 1024;
    return gb.toFixed(Number.isInteger(gb) ? 0 : 1);
  }

  /**
   * The capacity badge shown left of the allocation badge. "Budget" rather than "Capacity"
   * when the ceiling came from the configuration override, since the engine does not
   * actually enforce it.
   */
  capacityBadgeLabel(m: DatabaseStorageMetrics): string {
    if (m.maxLimitMb <= 0) {
      return 'No Size Limit';
    }
    return this.limitGb(m) + ' GB ' + (m.limitSource === 'Configured' ? 'Budget' : 'Capacity');
  }

  /** Explains where the ceiling came from, so a stale or guessed limit is visible. */
  limitSourceLabel(m: DatabaseStorageMetrics): string {
    switch (m.limitSource) {
      case 'Configured':
        return 'set by configuration override';
      case 'Fallback':
        return 'not detected — assuming ' + this.limitGb(m) + ' GB';
      default:
        return 'auto-detected';
    }
  }

  /** Progress bar width, clamped so an over-limit database cannot overflow the container. */
  meterWidth(m: DatabaseStorageMetrics): number {
    return Math.min(100, Math.max(0, m.usedPercentage));
  }

  /** "365 days", or "disabled" for a zero window. */
  daysLabel(days: number): string {
    return days > 0 ? `${days} days` : 'disabled';
  }

  /** The preset day counts plus the configured value, so the policy default is always selectable. */
  dayOptions(presets: number[], configured: number): number[] {
    const values = configured > 0 && !presets.includes(configured) ? [...presets, configured] : presets;
    return [...values].sort((a, b) => a - b);
  }

  /** "N documents · M files · X MB" for the report chart folder; one folder per document. */
  reportChartsSummary(m: DatabaseStorageMetrics): string {
    const documents = m.reportChartFolderCount ?? 0;
    const files = m.reportChartFileCount ?? 0;
    const mb = (m.reportChartSizeMb ?? 0).toLocaleString('en-US', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
    return `${documents.toLocaleString('en-US')} ${documents === 1 ? 'document' : 'documents'} · ` +
      `${files.toLocaleString('en-US')} ${files === 1 ? 'file' : 'files'} · ${mb} MB`;
  }

  /** Why Clear Chart Files is unavailable, shown in its tooltip; null when it can run. */
  get clearReportChartsBlockedReason(): string | null {
    const m = this.storageMetrics;
    if (this.maintenanceLoading) {
      return 'A maintenance task is running.';
    }
    if (!m?.reportChartsConfigured) {
      return 'The report chart folder is not configured (Benchmark:ReportPack:ChartsDataLocation).';
    }
    if ((m.reportChartFileCount ?? 0) === 0) {
      return 'There are no chart files to clear.';
    }
    return null;
  }

  /** Every non-zero count in a result, labelled for the result console. */
  maintenanceResultCounts(r: MaintenanceResult): { label: string; value: string }[] {
    const counts: [string, number][] = [
      ['Sessions expired', r.softDeletedCount],
      ['Sessions purged', r.purgedSessionCount],
      ['Messages purged', r.purgedMessageCount],
      ['Tool calls purged', r.purgedToolCallCount],
      ['Chat tool payloads pruned', r.prunedToolResultCount],
      ['Benchmark payloads pruned', r.prunedBenchmarkToolResultCount],
      ['Access journal rows pruned', r.prunedAuditLogCount],
      ['Dismissed AI errors pruned', r.prunedAiErrorLogCount],
      ['Disk folders removed', r.deletedDiskFolderCount],
      ['Disk files removed', r.deletedDiskFileCount],
      ['Orphan folders swept', r.sweptOrphanFolderCount]
    ];
    const rows = counts
      .filter(([, value]) => value > 0)
      .map(([label, value]) => ({ label, value: value.toLocaleString() }));
    if (r.reclaimedDiskBytes > 0) {
      rows.push({ label: 'Disk reclaimed', value: (r.reclaimedDiskBytes / 1024 / 1024).toFixed(2) + ' MB' });
    }
    return rows;
  }

  /** Fetches the history page the table state points at. The previous rows stay until it arrives. */
  loadMaintenanceHistory(resetToFirstPage = false) {
    const state = this.maintenanceHistoryTable;
    if (resetToFirstPage) {
      state.page = 1;
    }
    const request = ++this.maintenanceHistoryRequest;
    this.maintenanceHistoryLoading = true;
    this.adminService.getMaintenanceHistory(state.page, state.pageSize).subscribe({
      next: (res) => {
        if (request !== this.maintenanceHistoryRequest) {
          return;
        }
        // A prune can shrink the history under the current page; fetch the last page that exists.
        const lastPage = Math.max(1, Math.ceil(res.totalCount / state.pageSize));
        if (res.rows.length === 0 && state.page > lastPage) {
          state.page = lastPage;
          this.loadMaintenanceHistory();
          return;
        }
        this.maintenanceHistory = res.rows;
        this.maintenanceHistoryTotal = res.totalCount;
        state.setRemoteTotal(res.totalCount);
        this.expandedHistoryRunId = null;
        this.historyLogLoadingId = null;
        this.maintenanceHistoryLoading = false;
      },
      error: (err) => {
        if (request !== this.maintenanceHistoryRequest) {
          return;
        }
        console.error('Failed to load maintenance history', err);
        this.maintenanceHistoryLoading = false;
      }
    });
  }

  onMaintenanceHistoryPageChanged() {
    this.persistMaintenanceHistoryPageSize();
    this.loadMaintenanceHistory();
  }

  private persistMaintenanceHistoryPageSize() {
    try {
      localStorage.setItem(
        AdminDatabaseTabComponent.MAINTENANCE_PAGE_SIZE_KEY, String(this.maintenanceHistoryTable.pageSize));
    } catch {
      // Storage can throw in private-browsing modes; the size then lasts only for this visit.
    }
  }

  restoreMaintenanceHistoryPageSize() {
    let stored: string | null;
    try {
      stored = localStorage.getItem(AdminDatabaseTabComponent.MAINTENANCE_PAGE_SIZE_KEY);
    } catch {
      return;
    }
    const size = Number(stored);
    if (stored && MAINTENANCE_HISTORY_PAGE_SIZES.includes(size)) {
      this.maintenanceHistoryTable.pageSize = size;
    }
  }

  /** Expands or collapses a run's log, fetching its text on the first expansion only. */
  toggleHistoryLog(run: MaintenanceRunLog) {
    if (this.expandedHistoryRunId === run.id) {
      this.expandedHistoryRunId = null;
      return;
    }
    this.expandedHistoryRunId = run.id;
    if (!run.hasLog || run.logText !== undefined || this.historyLogLoadingId === run.id) {
      return;
    }
    this.historyLogLoadingId = run.id;
    this.adminService.getMaintenanceRunLog(run.id).subscribe({
      next: (text) => {
        run.logText = text.logText ?? null;
        run.errorMessage = text.errorMessage ?? run.errorMessage ?? null;
        if (this.historyLogLoadingId === run.id) {
          this.historyLogLoadingId = null;
        }
      },
      error: (err) => {
        if (this.historyLogLoadingId === run.id) {
          this.historyLogLoadingId = null;
        }
        this.store.showToast('Failed to load the run log: ' + (err.error?.message || err.message), 'error', 'Error');
      }
    });
  }

  private applyMaintenanceDefaults(m: DatabaseStorageMetrics) {
    if (this.maintenanceDefaultsApplied || !m.policy) {
      return;
    }
    this.maintenanceDefaultsApplied = true;
    const p = m.policy;
    if (p.inactivityTtlDays > 0) this.inactivityDays = p.inactivityTtlDays;
    if (p.pruneToolCallResultsDays > 0) this.toolCallPruneDays = p.pruneToolCallResultsDays;
    if (p.pruneBenchmarkToolCallResultsDays > 0) this.benchmarkToolCallPruneDays = p.pruneBenchmarkToolCallResultsDays;
    if (p.auditLogRetentionDays > 0) this.auditLogRetentionDays = p.auditLogRetentionDays;
    if (p.pruneDismissedAiErrorLogDays > 0) this.aiErrorLogPruneDays = p.pruneDismissedAiErrorLogDays;
  }

  /** `resetHistoryPage` returns the history to page 1, so a run that just finished is in view. */
  loadStorageMetrics(showFeedback = false, resetHistoryPage = false) {
    this.storageLoading = true;
    this.adminService.getStorageMetrics().subscribe({
      next: (data) => {
        this.storageMetrics = data;
        this.applyMaintenanceDefaults(data);
        this.storageLoading = false;
        this.loadMaintenanceHistory(resetHistoryPage);
        if (showFeedback) {
          this.store.showToast('Database storage metrics refreshed.', 'info', 'Metrics Refreshed');
        }
      },
      error: (err) => {
        console.error('Failed to load storage metrics', err);
        this.storageLoading = false;
        if (showFeedback) {
          this.store.showToast('Failed to load storage metrics: ' + (err.error?.message || err.message), 'error', 'Error');
        }
      }
    });
  }

  runFullMaintenance() {
    const execute = () => {
      this.maintenanceLoading = true;
      this.lastMaintenanceResult = null;
      this.openMaintenanceRunDialog(
        this.maintenanceDryRun ? 'Maintenance Pass Preview' : 'Maintenance Pass',
        this.maintenanceDryRun ? 'Previewing Maintenance Pass' : 'Executing Maintenance Pass',
        this.maintenanceDryRun
          ? 'Computing dry-run maintenance metrics...'
          : 'Running the full maintenance pass (sessions, trash, payloads, orphan folders, access journal and dismissed AI errors)...',
        true
      );

      this.adminService.runMaintenanceNow({
        dryRun: this.maintenanceDryRun,
        inactivityDays: this.inactivityDays,
        toolCallPruneDays: this.toolCallPruneDays
      }).subscribe({
        next: (res) => {
          this.maintenanceLoading = false;
          if (!this.destroyed) {
            this.completeMaintenanceRun(res);
          }
          const mb = (res.reclaimedDiskBytes / 1024 / 1024).toFixed(2);
          const detail = res.isDryRun
            ? `Dry run identified ${res.softDeletedCount} inactive sessions, ${res.prunedToolResultCount} tool payloads.`
            : `Purged ${res.purgedSessionCount} sessions and ${res.deletedDiskFolderCount} disk folders (${mb} MB reclaimed in ${res.elapsedMilliseconds}ms).`;
          this.store.showToast(
            detail,
            'success',
            res.isDryRun ? 'Dry Run Completed' : 'Full Maintenance Completed'
          );
          if (!this.destroyed) {
            this.loadStorageMetrics(false, true);
          }
        },
        error: (err) => {
          this.maintenanceLoading = false;
          const reason = err.error?.message || err.message;
          if (!this.destroyed) {
            this.failMaintenanceRun(reason);
          }
          this.store.showToast(
            'Failed to execute maintenance pass: ' + reason,
            'error',
            'Maintenance Error'
          );
        }
      });
    };

    if (!this.maintenanceDryRun) {
      this.openConfirmationModal(
        'Run Scheduled Maintenance Pass',
        'Are you sure you want to run the full maintenance pass? Expired trash, aged payloads, access journal rows past retention and old dismissed AI errors will be permanently removed.',
        execute,
        'Execute Maintenance',
        'btn-gh btn-primary'
      );
    } else {
      execute();
    }
  }

  /**
   * Runs one granular maintenance action under the shared dry-run switch. A dry run skips the
   * confirmation, since it changes nothing; the result lands in the console either way.
   */
  private runGranularMaintenance(action: {
    title: string;
    /** Omit to run without confirmation even when not a dry run. */
    confirmMessage?: string;
    confirmButton?: string;
    confirmClass?: string;
    loadingMessage: string;
    errorPrefix: string;
    call: (dryRun: boolean) => Observable<MaintenanceResult>;
  }) {
    const dryRun = this.maintenanceDryRun;
    const execute = () => {
      this.maintenanceLoading = true;
      this.lastMaintenanceResult = null;
      this.openMaintenanceRunDialog(
        dryRun ? `${action.title} (Preview)` : action.title,
        dryRun ? `Previewing: ${action.title}` : action.title,
        action.loadingMessage);
      action.call(dryRun).subscribe({
        next: (res) => {
          this.maintenanceLoading = false;
          if (!this.destroyed) {
            this.completeMaintenanceRun(res);
          }
          const summary = res.logs.length > 0 ? res.logs[res.logs.length - 1] : `${action.title} completed.`;
          this.store.showToast(summary, 'success', res.isDryRun ? 'Dry Run Completed' : action.title);
          if (!this.destroyed) {
            this.loadStorageMetrics(false, true);
          }
        },
        error: (err) => {
          this.maintenanceLoading = false;
          const reason = err.error?.message || err.message;
          if (!this.destroyed) {
            this.failMaintenanceRun(reason);
          }
          this.store.showToast(
            `${action.errorPrefix}: ` + reason,
            'error',
            'Maintenance Error'
          );
        }
      });
    };

    if (dryRun || !action.confirmMessage) {
      execute();
    } else {
      this.openConfirmationModal(
        action.title,
        action.confirmMessage,
        execute,
        action.confirmButton ?? 'Confirm',
        action.confirmClass ?? 'btn-gh btn-secondary'
      );
    }
  }

  purgeAllTrash() {
    this.runGranularMaintenance({
      title: 'Purge All Trash Now',
      confirmMessage: 'Are you sure you want to immediately delete ALL soft-deleted sessions across all users? This action cannot be undone.',
      confirmButton: 'Purge All Trash',
      confirmClass: 'btn-gh btn-gh-delete',
      loadingMessage: 'Permanently deleting all soft-deleted sessions and associated disk folders across all users...',
      errorPrefix: 'Failed to purge trash',
      call: (dryRun) => this.adminService.purgeTrashNow({ dryRun })
    });
  }

  purgeInactiveNow() {
    const days = this.inactivityDays;
    this.runGranularMaintenance({
      title: 'Purge Inactive Sessions',
      confirmMessage: `Are you sure you want to soft-delete unpinned sessions inactive for over ${days} days? ` +
        'Sessions past their own retention TTL also expire, and confidential sessions set to purge on delete are permanently purged, not moved to the trash.',
      confirmButton: 'Soft-Delete Inactive',
      loadingMessage: `Expiring sessions inactive for over ${days} days...`,
      errorPrefix: 'Failed to purge inactive sessions',
      call: (dryRun) => this.adminService.purgeInactive({ inactivityDays: days, dryRun })
    });
  }

  pruneToolResultsNow() {
    const days = this.toolCallPruneDays;
    this.runGranularMaintenance({
      title: 'Prune Aged Tool Results',
      confirmMessage: `Are you sure you want to prune tool call result payloads older than ${days} days? Message transcripts will be preserved.`,
      confirmButton: 'Prune Tool Payloads',
      loadingMessage: `Truncating tool result payloads older than ${days} days...`,
      errorPrefix: 'Failed to prune tool results',
      call: (dryRun) => this.adminService.pruneToolResults({ toolCallPruneDays: days, dryRun })
    });
  }

  pruneBenchmarkToolResultsNow() {
    const days = this.benchmarkToolCallPruneDays;
    this.runGranularMaintenance({
      title: 'Prune Benchmark Tool Payloads',
      confirmMessage: `Are you sure you want to null the tool call arguments and results of benchmark runs older than ${days} days? ` +
        'Tool-layer diagnostics for those runs will no longer be able to replay what each call saw.',
      confirmButton: 'Prune Benchmark Payloads',
      loadingMessage: `Pruning benchmark tool payloads for runs older than ${days} days...`,
      errorPrefix: 'Failed to prune benchmark tool payloads',
      call: (dryRun) => this.adminService.pruneBenchmarkToolResults({ benchmarkToolCallPruneDays: days, dryRun })
    });
  }

  pruneAuditLogNow() {
    const days = this.auditLogRetentionDays;
    const prunable = this.storageMetrics?.auditLogPrunableCount ?? 0;
    this.runGranularMaintenance({
      title: 'Prune Access Journal',
      confirmMessage: `Are you sure you want to permanently delete ${prunable.toLocaleString()} access journal row(s) older than ${days} days? ` +
        'This is the only action that removes audit records, and it cannot be undone.',
      confirmButton: 'Delete Audit Rows',
      confirmClass: 'btn-gh btn-gh-delete',
      loadingMessage: `Deleting access journal rows older than ${days} days...`,
      errorPrefix: 'Failed to prune the access journal',
      call: (dryRun) => this.adminService.pruneAuditLog({ auditLogRetentionDays: days, dryRun })
    });
  }

  pruneAiErrorLogNow() {
    const days = this.aiErrorLogPruneDays;
    this.runGranularMaintenance({
      title: 'Prune Dismissed AI Errors',
      confirmMessage: `Are you sure you want to delete AI errors dismissed more than ${days} days ago? Undismissed errors are not affected.`,
      confirmButton: 'Prune Dismissed Errors',
      loadingMessage: `Deleting AI errors dismissed over ${days} days ago...`,
      errorPrefix: 'Failed to prune dismissed AI errors',
      call: (dryRun) => this.adminService.pruneAiErrorLog({ aiErrorLogPruneDays: days, dryRun })
    });
  }

  sweepOrphanFoldersNow() {
    this.runGranularMaintenance({
      title: 'Sweep Orphan Folders',
      loadingMessage: 'Scanning and removing unreferenced disk folders...',
      errorPrefix: 'Failed to sweep orphan folders',
      call: (dryRun) => this.adminService.sweepOrphans({ dryRun })
    });
  }

  /** Refused while clearReportChartsBlockedReason is set (the button is aria-disabled). */
  clearReportChartsNow() {
    const m = this.storageMetrics;
    if (!m || this.clearReportChartsBlockedReason) {
      return;
    }
    const files = m.reportChartFileCount ?? 0;
    const documents = m.reportChartFolderCount ?? 0;
    const mb = (m.reportChartSizeMb ?? 0).toLocaleString('en-US', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
    this.runGranularMaintenance({
      title: 'Clear Report Chart Files',
      confirmMessage: `Delete ${files.toLocaleString('en-US')} chart ${files === 1 ? 'file' : 'files'} (${mb} MB) ` +
        `for ${documents.toLocaleString('en-US')} ${documents === 1 ? 'document' : 'documents'}? This cannot be undone. ` +
        'The documents stay; their charts can be added again from the Comparison Wizard.',
      confirmButton: 'Clear Chart Files',
      confirmClass: 'btn-gh btn-gh-delete',
      loadingMessage: 'Deleting report chart image files...',
      errorPrefix: 'Failed to clear report chart files',
      call: (dryRun) => this.adminService.clearReportCharts({ dryRun })
    });
  }

  sendDiagnosticEmail() {
    this.maintenanceLoading = true;
    this.openMaintenanceRunDialog(
      'Diagnostic Report',
      'Sending Diagnostic Report',
      'Generating storage metrics and dispatching diagnostic email...'
    );
    this.adminService.sendReportEmail().subscribe({
      next: (res) => {
        if (!this.destroyed) {
          this.closeMaintenanceRunDialog();
        }
        this.maintenanceLoading = false;
        this.store.showToast(
          res.message,
          res.success ? 'success' : 'error',
          res.success ? 'Diagnostic Report Sent' : 'Failed to Send Report'
        );
      },
      error: (err) => {
        if (!this.destroyed) {
          this.closeMaintenanceRunDialog();
        }
        this.maintenanceLoading = false;
        this.store.showToast(
          'Failed to send report email: ' + (err.error?.message || err.message),
          'error',
          'Email Error'
        );
      }
    });
  }
}
