import type { Mock } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { AdminDatabaseTabComponent } from './admin-database-tab.component';
import { AdminService, DatabaseStorageMetrics, MaintenanceResult, MaintenanceRunLog } from '../../services/admin.service';
import { AdminPageStore } from '../admin-page.store';
import { configureAdminTestBed } from '../admin.component.testing';

describe('AdminDatabaseTabComponent', () => {
  let component: AdminDatabaseTabComponent;
  let fixture: ComponentFixture<AdminDatabaseTabComponent>;
  let adminService: AdminService;
  let store: AdminPageStore;

  beforeEach(async () => {
    ({ adminService, store } = await configureAdminTestBed(AdminDatabaseTabComponent));
  });

  describe('database maintenance', () => {
    const metrics = (): DatabaseStorageMetrics => ({
      allocatedDataSizeMb: 100, usedDataSizeMb: 80, freeSpaceWithinLimitMb: 10140, maxLimitMb: 10240,
      usedPercentage: 1, tableMetrics: [], hasEngineSizeLimit: true, limitSource: 'Detected',
      serverProductLabel: 'SQL Server 2022 Express', activeSessionCount: 0, softDeletedSessionCount: 0,
      inactiveSessionCount: 0, pinnedSessionCount: 0, diskAttachmentsSizeBytes: 0, diskAttachmentsSizeMb: 0,
      diskAttachmentsFolderCount: 0, diskAttachmentsFileCount: 0, estimatedReclaimableMb: 0, statusLevel: 'Normal',
      logAllocatedMb: 8, logUsedMb: 1, otherTablesTotalSpaceMb: 0, otherTablesCount: 0, allTablesTotalSpaceMb: 0,
      confidentialSessionCount: 0, ownTtlSessionCount: 0, immediatePurgeSessionCount: 0, ephemeralSessionCount: 0,
      contentKeyVersions: [], sessionsWithUnknownKeyVersionCount: 0, auditLogRowCount: 0, auditLogPrunableCount: 0,
      aiErrorLogUndismissedCount: 0, aiErrorLogDismissedCount: 0, aiErrorLogPrunableCount: 0,
      securityEventRowCount: 0, securityEventPrunableCount: 0,
      chatProviderAttemptRowCount: 0, chatProviderAttemptPrunableCount: 0,
      chatTurnLimitEventRowCount: 0, chatTurnLimitEventPrunableCount: 0,
      expiredTrashSessionCount: 0, prunableToolCallCount: 0, prunableBenchmarkToolCallCount: 0,
      serviceStartedUtc: '2026-09-15T00:00:00Z', appliedMigrationCount: 0, pendingMigrations: [],
      schemaStatusAvailable: true,
      policy: {
        maxActiveSessionsPerUser: 50, maxPinnedSessionsPerUser: 5, inactivityTtlDays: 90,
        softDeleteGracePeriodDays: 30, pruneToolCallResultsDays: 30, pruneBenchmarkToolCallResultsDays: 90,
        auditLogRetentionDays: 365, pruneDismissedAiErrorLogDays: 90, pruneSecurityEventDays: 180,
        pruneSnapshotQuarantineDays: 30, pruneChatProviderAttemptDays: 400, pruneChatTurnLimitEventDays: 180, maintenanceHistoryRetentionDays: 180,
        maintenanceRunHourUtc: 3,
        enableStorageWarningEmails: true, emailSenderConfigured: false
      }
    });

    const result = (isDryRun: boolean): MaintenanceResult => ({
      success: true, isDryRun, softDeletedCount: 0, purgedSessionCount: 0, purgedMessageCount: 0,
      purgedToolCallCount: 0, prunedToolResultCount: 0, prunedBenchmarkToolResultCount: 0,
      deletedDiskFolderCount: 0, deletedDiskFileCount: 0, reclaimedDiskBytes: 0, sweptOrphanFolderCount: 0,
      prunedAuditLogCount: 0, prunedAiErrorLogCount: 0, prunedSecurityEventCount: 0,
      prunedChatProviderAttemptCount: 0, prunedChatTurnLimitEventCount: 0, elapsedMilliseconds: 1,
      trigger: 'Manual', logs: []
    });

    beforeEach(() => {
      vi.spyOn(adminService, 'getStorageMetrics').mockReturnValue(of(metrics()));
      vi.spyOn(adminService, 'getMaintenanceHistory').mockReturnValue(of({ totalCount: 0, rows: [] }));
      fixture = TestBed.createComponent(AdminDatabaseTabComponent);
      component = fixture.componentInstance;
    });

    const historyRun = (id: number, hasLog: boolean): MaintenanceRunLog => ({
      id, startedUtc: '2026-09-15T03:00:00Z', trigger: 'Scheduled', isDryRun: false, success: true,
      elapsedMilliseconds: 10, softDeletedCount: 0, purgedSessionCount: 0, purgedMessageCount: 0,
      purgedToolCallCount: 0, prunedToolResultCount: 0, prunedBenchmarkToolResultCount: 0,
      prunedAuditLogCount: 0, prunedAiErrorLogCount: 0, prunedSecurityEventCount: 0,
      prunedChatProviderAttemptCount: 0, prunedChatTurnLimitEventCount: 0, deletedDiskFolderCount: 0, deletedDiskFileCount: 0,
      sweptOrphanFolderCount: 0, reclaimedDiskBytes: 0, errorMessage: null, hasLog
    });

    const runDialog = (): HTMLDialogElement =>
      fixture.nativeElement.querySelector('dialog.maintenance-run-dialog');

    it('opens the run dialog while running and switches it to the result in place', () => {
      const response = new Subject<MaintenanceResult>();
      vi.spyOn(adminService, 'runMaintenanceNow').mockReturnValue(response.asObservable());
      fixture.detectChanges();

      component.maintenanceDryRun = true;
      component.runFullMaintenance();

      expect(component.maintenanceRunPhase).toBe('running');
      expect(runDialog().open).toBe(true);

      response.next(result(true));
      response.complete();

      expect(component.maintenanceRunPhase).toBe('completed');
      expect(component.lastMaintenanceResult?.isDryRun).toBe(true);
      expect(runDialog().open).toBe(true);
      expect(document.activeElement).toBe(fixture.nativeElement.querySelector('#maintenanceRunTitle'));

      component.closeMaintenanceRunDialog();
      expect(runDialog().open).toBe(false);
    });

    it('shows the result summary in the run dialog and no separate last-run line in the tab', async () => {
      const response = new Subject<MaintenanceResult>();
      vi.spyOn(adminService, 'runMaintenanceNow').mockReturnValue(response.asObservable());
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      component.maintenanceDryRun = true;
      component.runFullMaintenance();
      response.next(result(true));
      response.complete();
      fixture.detectChanges();

      const summary: HTMLElement | null = runDialog().querySelector('.run-summary');
      expect(summary).not.toBeNull();
      expect(summary!.textContent).toContain('Success');
      expect(summary!.textContent).toContain('Dry run');
      expect(summary!.textContent).toContain('Manual');
      expect(fixture.nativeElement.querySelector('#maintenance-inactivity-days')).not.toBeNull();
      expect(fixture.nativeElement.querySelector('.last-run-line')).toBeNull();

      component.closeMaintenanceRunDialog();
    });

    it('switches the run dialog to the failed phase when the request errors', () => {
      vi.spyOn(adminService, 'runMaintenanceNow').mockReturnValue(throwError(() => ({ message: 'boom' })));

      component.maintenanceDryRun = true;
      component.runFullMaintenance();

      expect(component.maintenanceRunPhase).toBe('failed');
      expect(component.lastMaintenanceResult?.success).toBe(false);
      expect(component.lastMaintenanceResult?.errorMessage).toBe('boom');
      component.closeMaintenanceRunDialog();
    });

    it('re-fetches the history page the pager chose and persists the page size', () => {
      const storageKey = 'overseer.admin.maintenanceHistory.pageSize';
      const historySpy = adminService.getMaintenanceHistory as Mock;
      historySpy.mockClear();
      const state = component.maintenanceHistoryTable;
      state.setRemoteTotal(300);
      state.setPageSize(50);
      state.setPage(3, component.maintenanceHistory);

      component.onMaintenanceHistoryPageChanged();

      expect(vi.mocked(historySpy).mock.calls[0]).toEqual([3, 50]);
      expect(localStorage.getItem(storageKey)).toBe('50');
      localStorage.removeItem(storageKey);
    });

    it('restores only a page size from the allowed list', () => {
      const storageKey = 'overseer.admin.maintenanceHistory.pageSize';

      localStorage.setItem(storageKey, '7');
      component.restoreMaintenanceHistoryPageSize();
      expect(component.maintenanceHistoryTable.pageSize).toBe(10);

      localStorage.setItem(storageKey, '500');
      component.restoreMaintenanceHistoryPageSize();
      expect(component.maintenanceHistoryTable.pageSize).toBe(500);

      localStorage.removeItem(storageKey);
    });

    it('fetches a run log once and caches it on the row', () => {
      const logSpy = vi.spyOn(adminService, 'getMaintenanceRunLog').mockReturnValue(of({ logText: 'line 1', errorMessage: null }));
      const run = historyRun(7, true);

      component.toggleHistoryLog(run);
      expect(component.expandedHistoryRunId).toBe(7);
      expect(run.logText).toBe('line 1');

      component.toggleHistoryLog(run);
      expect(component.expandedHistoryRunId).toBeNull();
      component.toggleHistoryLog(run);

      expect(logSpy).toHaveBeenCalledTimes(1);
    });

    it('never fetches a log for a run that has none', () => {
      const logSpy = vi.spyOn(adminService, 'getMaintenanceRunLog').mockReturnValue(undefined as any);

      component.toggleHistoryLog(historyRun(8, false));

      expect(logSpy).not.toHaveBeenCalled();
    });

    it('sends a day count chosen in the template select as a number, not a string', async () => {
      const runSpy = vi.spyOn(adminService, 'runMaintenanceNow').mockReturnValue(of(result(true)));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      const select: HTMLSelectElement = fixture.nativeElement.querySelector('#maintenance-inactivity-days');
      const index = Array.from(select.options).findIndex(o => (o.textContent ?? '').includes('60d'));
      expect(index).toBeGreaterThanOrEqual(0);
      select.selectedIndex = index;
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();

      component.maintenanceDryRun = true;
      component.runFullMaintenance();

      const request = vi.mocked(runSpy).mock.lastCall![0]!;
      expect(request.inactivityDays).toBe(60);
      expect(typeof request.inactivityDays).toBe('number');
      fixture.destroy();
    });

    it('passes the dry-run switch to a granular action and skips the confirmation', () => {
      const purgeSpy = vi.spyOn(adminService, 'purgeInactive').mockReturnValue(of(result(true)));
      const confirmSpy = vi.spyOn(component, 'openConfirmationModal').mockReturnValue(undefined);

      component.maintenanceDryRun = true;
      component.purgeInactiveNow();

      expect(confirmSpy).not.toHaveBeenCalled();
      expect(purgeSpy).toHaveBeenCalledWith(expect.objectContaining({ dryRun: true }));
      expect(component.lastMaintenanceResult?.isDryRun).toBe(true);
    });

    it.each([
      {
        name: 'security events', days: 90, count: 4, countLabel: 'Security events pruned',
        run: (c: AdminDatabaseTabComponent, days: number) => { c.securityEventPruneDays = days; c.pruneSecurityEventsNow(); },
        spyOnPrune: () => vi.spyOn(adminService, 'pruneSecurityEvents').mockReturnValue(
          of({ ...result(true), prunedSecurityEventCount: 4 })),
        request: { securityEventPruneDays: 90, dryRun: true }
      },
      {
        name: 'chat provider attempts', days: 180, count: 6, countLabel: 'Chat provider attempts pruned',
        run: (c: AdminDatabaseTabComponent, days: number) => { c.chatProviderAttemptPruneDays = days; c.pruneChatProviderAttemptsNow(); },
        spyOnPrune: () => vi.spyOn(adminService, 'pruneChatProviderAttempts').mockReturnValue(
          of({ ...result(true), prunedChatProviderAttemptCount: 6 })),
        request: { chatProviderAttemptPruneDays: 180, dryRun: true }
      },
      {
        name: 'chat turn limit events', days: 90, count: 4, countLabel: 'Chat turn limit events pruned',
        run: (c: AdminDatabaseTabComponent, days: number) => { c.chatTurnLimitEventPruneDays = days; c.pruneChatTurnLimitEventsNow(); },
        spyOnPrune: () => vi.spyOn(adminService, 'pruneChatTurnLimitEvents').mockReturnValue(
          of({ ...result(true), prunedChatTurnLimitEventCount: 4 })),
        request: { chatTurnLimitEventPruneDays: 90, dryRun: true }
      }
    ])('prunes $name as a dry run at the chosen age without confirmation', ({ days, count, countLabel, run, spyOnPrune, request }) => {
      const pruneSpy = spyOnPrune();
      const confirmSpy = vi.spyOn(component, 'openConfirmationModal').mockReturnValue(undefined);

      component.maintenanceDryRun = true;
      run(component, days);

      expect(confirmSpy).not.toHaveBeenCalled();
      expect(pruneSpy).toHaveBeenCalledWith(request);
      expect(component.lastMaintenanceResult?.isDryRun).toBe(true);
      expect(component.maintenanceResultCounts(component.lastMaintenanceResult!))
        .toContainEqual({ label: countLabel, value: String(count) });
      component.closeMaintenanceRunDialog();
    });

    it.each([
      {
        name: 'security event',
        overrides: (): Partial<DatabaseStorageMetrics> => ({
          securityEventRowCount: 40, securityEventPrunableCount: 7, securityEventOldestUtc: '2026-01-02T00:00:00Z',
          policy: { ...metrics().policy, pruneSecurityEventDays: 365 }
        }),
        pruneDays: (c: AdminDatabaseTabComponent) => c.securityEventPruneDays,
        selectId: '#maintenance-security-event-days',
        options: ['Older than 30d', 'Older than 90d', 'Older than 180d', 'Older than 365d'],
        policyLabel: 'Security events kept',
        statLabel: 'Prunable security events',
        statValue: '7 of 40, oldest 2026-01-02'
      },
      {
        name: 'chat provider attempt',
        overrides: (): Partial<DatabaseStorageMetrics> => ({
          chatProviderAttemptRowCount: 500, chatProviderAttemptPrunableCount: 12,
          chatProviderAttemptOldestUtc: '2025-08-03T00:00:00Z',
          policy: { ...metrics().policy, pruneChatProviderAttemptDays: 365 }
        }),
        pruneDays: (c: AdminDatabaseTabComponent) => c.chatProviderAttemptPruneDays,
        selectId: '#maintenance-provider-attempt-days',
        options: ['Older than 90d', 'Older than 180d', 'Older than 365d', 'Older than 400d'],
        policyLabel: 'Chat provider attempts kept',
        statLabel: 'Prunable provider attempts',
        statValue: '12 of 500, oldest 2025-08-03'
      },
      {
        name: 'chat turn limit event',
        overrides: (): Partial<DatabaseStorageMetrics> => ({
          chatTurnLimitEventRowCount: 60, chatTurnLimitEventPrunableCount: 5,
          chatTurnLimitEventOldestUtc: '2026-03-04T00:00:00Z',
          policy: { ...metrics().policy, pruneChatTurnLimitEventDays: 365 }
        }),
        pruneDays: (c: AdminDatabaseTabComponent) => c.chatTurnLimitEventPruneDays,
        selectId: '#maintenance-turn-limit-days',
        options: ['Older than 30d', 'Older than 90d', 'Older than 180d', 'Older than 365d'],
        policyLabel: 'Chat turn limit events kept',
        statLabel: 'Prunable limit events',
        statValue: '5 of 60, oldest 2026-03-04'
      }
    ])('takes the $name age from the policy and shows it in the template', async ({ overrides, pruneDays, selectId, options, policyLabel, statLabel, statValue }) => {
      (adminService.getStorageMetrics as Mock).mockReturnValue(of({ ...metrics(), ...overrides() }));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(pruneDays(component)).toBe(365);
      const host = fixture.nativeElement as HTMLElement;
      const select = host.querySelector(selectId) as HTMLSelectElement;
      expect(select).not.toBeNull();
      expect(Array.from(select.options).map(o => (o.textContent ?? '').trim())).toEqual(options);
      const policyRow = Array.from(host.querySelectorAll('.policy-list dt'))
        .find(dt => dt.textContent?.trim() === policyLabel);
      expect(policyRow?.nextElementSibling?.textContent?.trim()).toBe('365 days');
      const statBox = Array.from(host.querySelectorAll('.preview-grid .stat-box'))
        .find(box => box.querySelector('.stat-label')?.textContent?.trim() === statLabel);
      expect((statBox?.querySelector('.stat-value')?.textContent ?? '').replace(/\s+/g, ' ').trim())
        .toBe(statValue);
      fixture.destroy();
    });

    it('lists how long refused game text is kept, and names its prune in the maintenance pass', async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      const host = fixture.nativeElement as HTMLElement;
      const policyRow = Array.from(host.querySelectorAll('.policy-list dt'))
        .find(dt => dt.textContent?.trim() === 'Snapshot guard texts kept');
      expect(policyRow?.nextElementSibling?.textContent?.trim()).toBe('30 days');
      const steps = Array.from(host.querySelectorAll('.pass-steps li')).map(li => (li.textContent ?? '').replace(/\s+/g, ' ').trim());
      expect(steps).toContain('Prunes security events over 180 days, keeping those of users with an account hold or a pending deletion request, and refused game text over 30 days');
      fixture.destroy();
    });

    it('shows the AI error, security event and provider attempt counts in the history and spans the log row across every column', async () => {
      vi.spyOn(adminService, 'getMaintenanceRunLog').mockReturnValue(of({ logText: 'line 1', errorMessage: null }));
      (adminService.getMaintenanceHistory as Mock).mockReturnValue(of({
        totalCount: 1,
        rows: [{ ...historyRun(9, true), prunedAiErrorLogCount: 3, prunedSecurityEventCount: 11, prunedChatProviderAttemptCount: 25 }]
      }));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      const host = fixture.nativeElement as HTMLElement;
      const headers = Array.from(host.querySelectorAll('.history-table thead th')).map(th => th.textContent?.trim());
      expect(headers).toContain('AI Errors');
      expect(headers).toContain('Security Events');
      expect(headers.indexOf('Provider Attempts')).toBe(headers.indexOf('Security Events') + 1);
      const cells = Array.from(host.querySelectorAll('.history-table tbody tr:first-child td')).map(td => td.textContent?.trim());
      expect(cells[headers.indexOf('AI Errors')]).toBe('3');
      expect(cells[headers.indexOf('Security Events')]).toBe('11');
      expect(cells[headers.indexOf('Provider Attempts')]).toBe('25');
      expect(headers.indexOf('Limit Events')).toBe(headers.indexOf('Provider Attempts') + 1);

      (host.querySelector('.history-log-toggle') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(host.querySelector('.history-log-row td')?.getAttribute('colspan')).toBe(String(headers.length));
      fixture.destroy();
    });

    describe('report chart files', () => {
      const chartMetrics = (overrides: Partial<DatabaseStorageMetrics> = {}): DatabaseStorageMetrics => ({
        ...metrics(),
        reportChartsConfigured: true, reportChartFolderCount: 3, reportChartFileCount: 12,
        reportChartSizeBytes: 4718592, reportChartSizeMb: 4.5,
        ...overrides
      });

      const openDatabaseTab = async (m: DatabaseStorageMetrics) => {
        (adminService.getStorageMetrics as Mock).mockReturnValue(of(m));
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
      };

      const chartStat = (): HTMLElement | null => fixture.nativeElement.querySelector('.report-charts-stat');

      const clearButton = (): HTMLButtonElement =>
        (Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[])
          .find(b => (b.textContent ?? '').trim() === 'Clear Chart Files')!;

      const tooltipText = (button: HTMLButtonElement): string | null => {
        const id = button.getAttribute('interestfor');
        const tip: HTMLElement | null = id ? fixture.nativeElement.querySelector('#' + id) : null;
        return tip ? (tip.textContent ?? '').trim() : null;
      };

      it('shows the document, file and size counts when the folder is configured', async () => {
        await openDatabaseTab(chartMetrics());

        const text = (chartStat()!.textContent ?? '').replace(/\s+/g, ' ');
        expect(text).toContain('Report Chart Files');
        expect(text).toContain('3 documents · 12 files · 4.5 MB');
      });

      it('says the folder is not configured and names the setting', async () => {
        await openDatabaseTab(chartMetrics({ reportChartsConfigured: false, reportChartFolderCount: 0, reportChartFileCount: 0, reportChartSizeMb: 0 }));

        const text = (chartStat()!.textContent ?? '').replace(/\s+/g, ' ');
        expect(text).toContain('Not configured (Benchmark:ReportPack:ChartsDataLocation)');
      });

      it('omits the line for a server that does not report chart storage', async () => {
        await openDatabaseTab(metrics());

        expect(chartStat()).toBeNull();
      });

      it('enables Clear Chart Files only when the folder is configured, has files and nothing is running', async () => {
        await openDatabaseTab(chartMetrics());

        let button = clearButton();
        expect(button.matches('button.btn-gh.btn-danger[type="button"]')).toBe(true);
        expect(button.hasAttribute('title')).toBe(false);
        expect(button.hasAttribute('aria-disabled')).toBe(false);
        expect(button.hasAttribute('interestfor')).toBe(false);

        component.maintenanceLoading = true;
        fixture.detectChanges();
        button = clearButton();
        expect(button.getAttribute('aria-disabled')).toBe('true');
        expect(tooltipText(button)).toBe('A maintenance task is running.');
        component.maintenanceLoading = false;

        component.storageMetrics = chartMetrics({ reportChartFileCount: 0, reportChartFolderCount: 0, reportChartSizeMb: 0 });
        fixture.detectChanges();
        button = clearButton();
        expect(button.getAttribute('aria-disabled')).toBe('true');
        expect(tooltipText(button)).toBe('There are no chart files to clear.');

        component.storageMetrics = chartMetrics({ reportChartsConfigured: false });
        fixture.detectChanges();
        button = clearButton();
        expect(button.getAttribute('aria-disabled')).toBe('true');
        expect(tooltipText(button)).toContain('Benchmark:ReportPack:ChartsDataLocation');
      });

      it('does nothing when the aria-disabled button is clicked', async () => {
        const clearSpy = vi.spyOn(adminService, 'clearReportCharts').mockReturnValue(undefined as any);
        const confirmSpy = vi.spyOn(component, 'openConfirmationModal').mockReturnValue(undefined);
        await openDatabaseTab(chartMetrics({ reportChartFileCount: 0 }));

        clearButton().click();

        expect(clearSpy).not.toHaveBeenCalled();
        expect(confirmSpy).not.toHaveBeenCalled();
      });

      it('runs a dry run without confirmation and reloads the metrics', () => {
        const clearSpy = vi.spyOn(adminService, 'clearReportCharts').mockReturnValue(of(result(true)));
        const confirmSpy = vi.spyOn(component, 'openConfirmationModal').mockReturnValue(undefined);
        const metricsSpy = adminService.getStorageMetrics as Mock;
        component.storageMetrics = chartMetrics();
        metricsSpy.mockClear();

        component.maintenanceDryRun = true;
        component.clearReportChartsNow();

        expect(confirmSpy).not.toHaveBeenCalled();
        expect(clearSpy).toHaveBeenCalledTimes(1);
        expect(clearSpy).toHaveBeenCalledWith({ dryRun: true });
        expect(component.lastMaintenanceResult?.isDryRun).toBe(true);
        expect(metricsSpy).toHaveBeenCalledTimes(1);
        component.closeMaintenanceRunDialog();
      });

      it('confirms a live run with the counts and sends dryRun false', () => {
        const clearSpy = vi.spyOn(adminService, 'clearReportCharts').mockReturnValue(of(result(false)));
        const confirmSpy = vi.spyOn(component, 'openConfirmationModal').mockImplementation((_title: string, _message: string, action: () => void) => action());
        const metricsSpy = adminService.getStorageMetrics as Mock;
        component.storageMetrics = chartMetrics();
        metricsSpy.mockClear();

        component.maintenanceDryRun = false;
        component.clearReportChartsNow();

        const [title, message, , button, buttonClass] = vi.mocked(confirmSpy).mock.lastCall!;
        expect(title).toBe('Clear Report Chart Files');
        expect(message).toBe('Delete 12 chart files (4.5 MB) for 3 documents? This cannot be undone. ' +
          'The documents stay; their charts can be added again from the Comparison Wizard.');
        expect(button).toBe('Clear Chart Files');
        expect(buttonClass).toBe('btn-gh btn-gh-delete');
        expect(clearSpy).toHaveBeenCalledTimes(1);
        expect(clearSpy).toHaveBeenCalledWith({ dryRun: false });
        expect(metricsSpy).toHaveBeenCalledTimes(1);
        component.closeMaintenanceRunDialog();
      });
    });
  });
});
