import type { Mock, MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError, Subject } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import { MultiRunComponent } from './multi-run/multi-run.component';
import {
  AdminBenchmarkService, BenchmarkBatteryDto, BenchmarkBatteryReusePreviewDto, BenchmarkBatteryRunDto
} from '../../services/admin-benchmark.service';
import { BenchmarkCompletionSoundService } from '../../services/benchmark-completion-sound.service';
import { BenchmarkCompletionNotificationService } from '../../services/benchmark-completion-notification.service';
import { BenchmarkBackgroundActivityService } from '../../services/benchmark-background-activity.service';
import {
  clearStoredState, createAdminBenchmarkFixture, RUN_SETTINGS_KEY, buildBattery, buildBatteryRun
} from './benchmark.component.testing';

describe('AdminBenchmarkComponent', () => {
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: MockedObject<AdminBenchmarkService>;

  beforeEach(clearStoredState);

  afterEach(clearStoredState);

  beforeEach(async () => {
    ({ component, fixture, benchmarkServiceMock } = await createAdminBenchmarkFixture());
  });

  describe('multi-suite battery runs', () => {
    const card = (): HTMLElement => fixture.nativeElement.querySelector('.setup-card') as HTMLElement;
    const query = <T extends Element = HTMLElement>(selector: string): T | null =>
      fixture.nativeElement.querySelector(selector) as T | null;
    const startButton = (): HTMLButtonElement =>
      card().querySelector('.form-actions .btn-gh') as HTMLButtonElement;

    /**
     * Renders the Run tab with these batteries listed and the fixture's one configuration as both
     * models. The suite's beforeEach has already run ngOnInit, so the lists are fetched again here.
     */
    function renderLauncher(batteries: BenchmarkBatteryDto[] = [buildBattery()]): void {
      benchmarkServiceMock.getBatteries.mockReturnValue(of(batteries));
      component.loadRunLimits();
      component.loadBatteries();
      component.activeSubTab = 'run';
      component.testedConfigId = 1;
      component.assessorConfigId = 1;
      fixture.detectChanges();
    }

    function chooseBattery(): void {
      query<HTMLInputElement>('#runTargetBattery')!.click();
      fixture.detectChanges();
    }

    function setRunCount(value: number): void {
      const input = query<HTMLInputElement>('#runCountInput')!;
      input.value = String(value);
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    }

    function withLimits(overrides: Record<string, number>): void {
      benchmarkServiceMock.getRunLimits.mockReturnValue(of({
        maxRunsPerHour: 4, maxRunsPerDay: 20, runsInLastHour: 0, runsInLast24Hours: 0,
        remainingDailyHeadroom: 20, maxRunCountPerSeries: 20, maxMembersPerBattery: 40,
        ...overrides
      }));
    }

    afterEach(() => component.ngOnDestroy());

    describe('Run Target', () => {
      it('should put a Run Target radio group above Benchmark Suite in Test Setup, Single suite first and chosen', () => {
        renderLauncher();

        const group = card().querySelector('.setup-group-test fieldset[role="radiogroup"]') as HTMLFieldSetElement;
        expect(group).toBeTruthy();
        expect(query(`#${group.getAttribute('aria-labelledby')}`)!.textContent!.trim()).toBe('Run Target');
        const radios = Array.from(group.querySelectorAll('input[type="radio"]')) as HTMLInputElement[];
        expect(radios.map(r => r.id)).toEqual(['runTargetSuite', 'runTargetBattery']);
        expect(radios.map(r => (r.closest('label')!.textContent || '').trim())).toEqual(['Single suite', 'Battery']);
        expect(radios[0].checked).toBe(true);

        const suiteSelect = query('#suiteSelect')!;
        expect(group.compareDocumentPosition(suiteSelect) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        expect(query('#batterySelect')).toBeNull();
      });

      it('should swap the suite select for the battery select and relabel the run count', () => {
        renderLauncher();

        chooseBattery();

        expect(component.runTargetKind).toBe('battery');
        expect(query('#suiteSelect')).toBeNull();
        const select = query<HTMLSelectElement>('#batterySelect')!;
        expect(select).toBeTruthy();
        expect(component.selectedBatteryId).toBe(5);
        expect((card().querySelector('label[for="runCountInput"]')!.textContent || '').trim()).toBe('Runs per Suite');

        query<HTMLInputElement>('#runTargetSuite')!.click();
        fixture.detectChanges();
        expect(query('#suiteSelect')).toBeTruthy();
        expect(query('#batterySelect')).toBeNull();
        expect((card().querySelector('label[for="runCountInput"]')!.textContent || '').trim()).toBe('Number of Runs');
      });

      it('should list only runnable batteries, with the suites and declared weights in the select\'s info tip', () => {
        renderLauncher([
          buildBattery(),
          buildBattery({ id: 6, name: 'Archived', isArchived: true }),
          buildBattery({ id: 7, name: 'Broken', brokenSuiteNames: ['Deleted Suite'] }),
          buildBattery({ id: 8, name: 'Invalid', validationErrors: ['A battery needs at least two suites.'] })
        ]);

        chooseBattery();

        const options = Array.from(query<HTMLSelectElement>('#batterySelect')!.options).map(o => o.textContent!.trim());
        expect(options).toEqual(['Core Battery (2 suites)']);
        const tip = (query('#batteryHint')!.textContent || '').replace(/\s+/g, ' ');
        expect(tip).toContain('Questions and difficulty');
        expect(tip).toContain('Default Suite');
        expect(tip).toContain('75.0%');
        expect(tip).toContain('Second Suite');
        expect(tip).toContain('25.0%');
      });

      it('should hold Start back and name the suites whose difficulty is not assessed', () => {
        renderLauncher([buildBattery({
          suites: [
            buildBattery().suites[0],
            { ...buildBattery().suites[1], difficultyFullyAssessed: false, assessedQuestionCount: 4 }
          ]
        })]);

        chooseBattery();

        expect(component.canStartRun).toBe(false);
        expect(startButton().getAttribute('aria-disabled')).toBe('true');
        expect(component.startBenchmarkHint).toContain('Second Suite');
        expect(card().querySelector('.alert-warning')!.textContent).toContain('Second Suite');
      });
    });

    describe('Runs per Suite and the projection', () => {
      it('should bound Runs per Suite by floor(maxMembersPerBattery / K), and the run count by the series cap otherwise', () => {
        withLimits({ maxMembersPerBattery: 7 });
        renderLauncher();
        chooseBattery();

        setRunCount(10);

        expect(component.maxRunsPerSuite).toBe(3);
        expect(component.effectiveRunCount).toBe(3);
        expect(query('#runCountInput')!.getAttribute('max')).toBe('3');
        expect(query('#batteryProjectionLegend')!.textContent).toContain('2 suites × 3 = 6 runs');

        query<HTMLInputElement>('#runTargetSuite')!.click();
        fixture.detectChanges();
        expect(query('#runCountInput')!.getAttribute('max')).toBe('20');
        expect(query('#batteryProjectionLegend')).toBeNull();
      });

      it('should require Allow cap wait for a battery larger than the daily cap, and say how many days it spans', () => {
        withLimits({ maxRunsPerDay: 4, remainingDailyHeadroom: 4 });
        renderLauncher();
        chooseBattery();
        setRunCount(3);

        expect(component.batteryLaunchCount).toBe(6);
        expect(component.canStartRun).toBe(false);
        expect(component.startBenchmarkHint).toContain('exceed the daily cap of 4');
        const warning = (query('.battery-cap-warning')!.textContent || '').replace(/\s+/g, ' ');
        expect(warning).toContain('spans at least 2 days');

        query<HTMLInputElement>('#allowCapWaitInput')!.click();
        fixture.detectChanges();

        expect(component.allowCapWait).toBe(true);
        expect(component.canStartRun).toBe(true);
        expect(query('.battery-cap-warning')!.textContent).toContain('pause at the cap');
      });
    });

    describe('starting and remembering', () => {
      it('should start a battery run with the launcher\'s run request and remember the Run Target', () => {
        renderLauncher();
        chooseBattery();
        setRunCount(2);
        benchmarkServiceMock.startBatteryRun.mockReturnValue(of({ batteryRunId: 9 }));

        startButton().click();
        fixture.detectChanges();

        expect(benchmarkServiceMock.startRun).not.toHaveBeenCalled();
        expect(benchmarkServiceMock.startRunSeries).not.toHaveBeenCalled();
        const body = vi.mocked(benchmarkServiceMock.startBatteryRun).mock.lastCall![0];
        expect(body.batteryId).toBe(5);
        expect(body.runsPerSuite).toBe(2);
        expect(body.allowCapWait).toBe(false);
        expect(body.run.testedModelConfigurationId).toBe(1);
        expect(body.run.assessorModelConfigurationId).toBe(1);
        expect(body.run.acknowledgeSameProvider).toBe(false);
        expect(body.attach).toBeUndefined();
        expect(benchmarkServiceMock.previewBatteryReuse).not.toHaveBeenCalled();

        expect(component.activeBatteryRunId).toBe(9);
        expect(component.batteryDialogVisible).toBe(true);
        expect(component.dialogBatteryRunId).toBe(9);

        const stored = JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY)!);
        expect(stored.targetKind).toBe('battery');
        expect(stored.batteryId).toBe(5);
        expect(stored.runCount).toBe(2);
      });

      it('should restore a remembered battery only once the battery list arrives after the other lists', () => {
        localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
          suiteId: 1, testedConfigId: 1, assessorConfigId: 1, scoringProfileId: 1,
          targetKind: 'battery', batteryId: 5, runCount: 2
        }));
        const batteries = new Subject<BenchmarkBatteryDto[]>();
        benchmarkServiceMock.getBatteries.mockReturnValue(batteries);

        const restored = TestBed.createComponent(AdminBenchmarkComponent);
        restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
        restored.detectChanges();

        // Suites, profiles and configurations have all applied; the blob waits for the fourth list.
        const c = restored.componentInstance;
        expect(c.runTargetKind).toBe('suite');
        expect((c as any).pendingRunSettings).not.toBeNull();

        batteries.next([buildBattery()]);
        batteries.complete();
        restored.detectChanges();

        expect(c.runTargetKind).toBe('battery');
        expect(c.selectedBatteryId).toBe(5);
        expect(c.runCount).toBe(2);
        expect(restored.nativeElement.querySelector('#batterySelect')).toBeTruthy();
        expect((restored.nativeElement.querySelector('#runTargetBattery') as HTMLInputElement).checked).toBe(true);
        expect((c as any).pendingRunSettings).toBeNull();
        c.ngOnDestroy();
      });

      it('should restore Runs per Suite above the single-suite cap when the battery bound allows it', () => {
        withLimits({ maxRunCountPerSeries: 20, maxMembersPerBattery: 60 });
        localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
          suiteId: 1, testedConfigId: 1, assessorConfigId: 1, scoringProfileId: 1,
          targetKind: 'battery', batteryId: 5, runCount: 30
        }));
        const batteries = new Subject<BenchmarkBatteryDto[]>();
        benchmarkServiceMock.getBatteries.mockReturnValue(batteries);

        const restored = TestBed.createComponent(AdminBenchmarkComponent);
        restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
        restored.detectChanges();

        const c = restored.componentInstance;
        expect(c.runCount).toBe(30);

        const battery = buildBattery();
        batteries.next([battery]);
        batteries.complete();
        restored.detectChanges();

        expect(c.runTargetKind).toBe('battery');
        expect(c.runCountMax).toBe(Math.floor(60 / battery.suites.length));
        expect(c.runCount).toBe(Math.min(30, Math.floor(60 / battery.suites.length)));
        c.ngOnDestroy();
      });

      for (const [label, listed] of [
        ['gone', [buildBattery({ id: 6, name: 'Other' })]],
        ['archived', [buildBattery({ isArchived: true })]],
        ['broken', [buildBattery({ brokenSuiteNames: ['Deleted Suite'] })]],
        ['invalid', [buildBattery({ validationErrors: ['A battery needs at least two suites.'] })]]
      ] as [string, BenchmarkBatteryDto[]][]) {
        it(`should fall back to Single suite when the remembered battery is ${label}`, () => {
          localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
            suiteId: 1, testedConfigId: 1, assessorConfigId: 1, targetKind: 'battery', batteryId: 5
          }));
          benchmarkServiceMock.getBatteries.mockReturnValue(of(listed));

          const restored = TestBed.createComponent(AdminBenchmarkComponent);
          restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
          restored.detectChanges();

          expect(restored.componentInstance.runTargetKind).toBe('suite');
          expect(restored.nativeElement.querySelector('#suiteSelect')).toBeTruthy();
          expect(restored.componentInstance.selectedBattery?.id).not.toBe(5);
          restored.componentInstance.ngOnDestroy();
        });
      }

      it('should send a same-provider 409 through the acknowledgment dialog and resend the battery start', () => {
        renderLauncher();
        chooseBattery();
        const showModal = vi.spyOn(component.sameProviderDialog.nativeElement, 'showModal').mockReturnValue(undefined);
        benchmarkServiceMock.startBatteryRun.mockReturnValueOnce(throwError(() => ({
            status: 409,
            error: {
              sameProvider: true, provider: 'Anthropic', testedModelDisplayName: 'Test Model',
              assessorModelDisplayName: 'Test Model', message: 'The assessor shares the provider.', role: 'assessor'
            }
        }))).mockReturnValueOnce(of({ batteryRunId: 9 }));

        startButton().click();
        fixture.detectChanges();
        expect(showModal).toHaveBeenCalledTimes(1);

        const confirm = (Array.from(component.sameProviderDialog.nativeElement.querySelectorAll('button')) as HTMLButtonElement[])
          .find(button => (button.textContent ?? '').includes('Acknowledge & Start Run'))!;
        confirm.click();

        const bodies = vi.mocked(benchmarkServiceMock.startBatteryRun).mock.calls.map(args => args[0]);
        expect(bodies.length).toBe(2);
        expect(bodies[0].run.acknowledgeSameProvider).toBe(false);
        expect(bodies[1].run.acknowledgeSameProvider).toBe(true);
        expect(component.activeBatteryRunId).toBe(9);
      });

      it('should show a refused battery start\'s reason under the launcher', () => {
        renderLauncher();
        chooseBattery();
        benchmarkServiceMock.startBatteryRun.mockReturnValue(throwError(() => ({
          status: 409, error: 'A benchmark run is already in progress.'
        })));

        startButton().click();
        fixture.detectChanges();

        expect(card().querySelector('.alert-danger')!.textContent).toContain('A benchmark run is already in progress.');
        expect(component.activeBatteryRunId).toBeNull();
      });

      it('should show the 409 a single-run start receives while a battery runs', () => {
        renderLauncher();
        benchmarkServiceMock.startRun.mockReturnValue(throwError(() => ({
          status: 409, error: 'A battery is running; wait for it or cancel it.'
        })));

        startButton().click();
        fixture.detectChanges();

        expect(benchmarkServiceMock.startRun).toHaveBeenCalled();
        expect(card().querySelector('.alert-danger')!.textContent)
          .toContain('A battery is running; wait for it or cancel it.');
      });

      it('should surface the 409 a re-run receives while a battery runs inside the progress dialog', () => {
        vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);
        vi.spyOn(component.runProgressDialog.nativeElement, 'close').mockReturnValue(undefined);
        component.activeRunDetail = {
          id: 37, benchmarkSuiteId: 1, suiteName: 'Suite X', testedModelDisplayNameUsed: 'Test Model',
          testedModelProviderUsed: 'Anthropic', testedModelIdUsed: 'claude-3-5-sonnet', testedModelParallelExecutionModeUsed: 2,
          assessorModelDisplayNameUsed: 'Test Assessor', assessorModelProviderUsed: 'Anthropic',
          assessorModelIdUsed: 'claude-3-5-sonnet', startedByUserName: 'admin', status: 'CompletedWithErrors',
          startedAtUtc: '2026-10-02T00:00:00Z', completedAtUtc: '2026-10-02T00:05:00Z', totalAnswerDurationMs: 0,
          scoringProfileName: 'Default Intelligence Profile', scoringProfileId: 1, scoringMethodVersion: 2,
          difficultyFallbackUsed: false, speedMeasurementDegraded: false, maxParallelQuestionsUsed: 1,
          answeredQuestionCount: 0, totalQuestionCount: 3, assessmentParseFailed: false, totalInputTokens: 0,
          totalOutputTokens: 0, totalCacheReadTokens: 0, totalCacheCreationTokens: 0, totalDurationMs: 0,
          errorMessage: null, answers: []
        } as any;
        component.isRunProgressDialogOpen = true;
        benchmarkServiceMock.rerunFailedQuestions.mockReturnValue(throwError(() => ({
          status: 409, error: 'A battery is running; wait for it or cancel it.'
        })));

        component.rerunFailedFromProgress();
        fixture.detectChanges();

        expect(component.runErrorMessage).toBe('A battery is running; wait for it or cancel it.');
        expect(component.rerunLaunchPending).toBe(false);
        const alert = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog .dialog-body .alert-danger') as HTMLElement;
        expect(alert.textContent).toContain('A battery is running; wait for it or cancel it.');
        component.closeRunProgressDialog();
      });
    });

    describe('reusing earlier runs', () => {
      /** Suite 1 of battery 5 reuses run 12; suite 2 has nothing that qualifies. */
      function buildPreview(overrides: Partial<BenchmarkBatteryReusePreviewDto> = {}): BenchmarkBatteryReusePreviewDto {
        return {
          batteryId: 5, suiteCount: 2, runsPerSuite: 1, reusedCount: 1, launchCount: 1,
          attach: [{ suiteIndex: 0, round: 1, runId: 12 }],
          slots: [
            {
              suiteIndex: 0, suiteId: 1, suiteName: 'Default Suite', round: 1, runId: 12,
              runStartedAtUtc: '2026-10-01T08:00:00Z', qualityIndex: 71, reason: null
            },
            {
              suiteIndex: 1, suiteId: 2, suiteName: 'Second Suite', round: 1, runId: null,
              reason: 'Run #13 has no usable result: index withheld.'
            }
          ],
          ...overrides
        };
      }

      const reuseBox = (): HTMLInputElement | null => query<HTMLInputElement>('#reuseEarlierRunsInput');

      function checkReuse(): void {
        reuseBox()!.click();
        fixture.detectChanges();
      }

      const summary = (): string => (query('.battery-reuse-summary')?.textContent ?? '').replace(/\s+/g, ' ').trim();

      it('should offer Reuse earlier runs in battery mode only, unchecked, without asking the server', () => {
        renderLauncher();
        expect(reuseBox()).toBeNull();

        chooseBattery();

        expect(reuseBox()).not.toBeNull();
        expect(reuseBox()!.checked).toBe(false);
        expect(component.reuseEarlierRuns).toBe(false);
        expect(query('.battery-reuse-row')).toBeNull();
        expect(benchmarkServiceMock.previewBatteryReuse).not.toHaveBeenCalled();
      });

      it('should preview the reuse when checked and project the runs reused and launched', () => {
        renderLauncher();
        chooseBattery();
        benchmarkServiceMock.previewBatteryReuse.mockReturnValue(of(buildPreview()));

        checkReuse();

        expect(benchmarkServiceMock.previewBatteryReuse).toHaveBeenCalledTimes(1);
        const body = vi.mocked(benchmarkServiceMock.previewBatteryReuse).mock.lastCall![0];
        expect(body.batteryId).toBe(5);
        expect(body.runsPerSuite).toBe(1);
        expect(body.run.testedModelConfigurationId).toBe(1);
        expect(body.attach).toBeUndefined();

        expect(summary()).toBe('Reusing 1 earlier run (#12); launching 1.');
        const reasons = (query('.battery-reuse-reasons')!.textContent || '').replace(/\s+/g, ' ');
        expect(reasons).toContain('Second Suite, round 1:');
        expect(reasons).toContain('Run #13 has no usable result: index withheld.');
        expect(component.batteryRunsToLaunch).toBe(1);
        expect(component.canStartRun).toBe(true);
      });

      it('should say when nothing qualifies, and why', () => {
        renderLauncher();
        chooseBattery();
        benchmarkServiceMock.previewBatteryReuse.mockReturnValue(of(buildPreview({
          reusedCount: 0, launchCount: 2, attach: [],
          slots: [
            { suiteIndex: 0, suiteId: 1, suiteName: 'Default Suite', round: 1, runId: null,
              reason: 'No earlier run of \'Default Suite\' tested this configuration.' },
            { suiteIndex: 1, suiteId: 2, suiteName: 'Second Suite', round: 1, runId: null,
              reason: 'Run #13 ran under another instrument than the one recorded for \'Second Suite\': ToolGuidesSha256 differ.' }
          ]
        })));

        checkReuse();

        expect(summary()).toBe('No earlier run qualifies; launching 2.');
        const reasons = (query('.battery-reuse-reasons')!.textContent || '').replace(/\s+/g, ' ');
        expect(reasons).toContain('No earlier run of \'Default Suite\' tested this configuration.');
        expect(reasons).toContain('ToolGuidesSha256 differ.');
      });

      it('should preview again on every change of battery, model, grader or Runs per Suite', () => {
        renderLauncher([buildBattery(), buildBattery({ id: 6, name: 'Other Battery' })]);
        chooseBattery();
        benchmarkServiceMock.previewBatteryReuse.mockReturnValue(of(buildPreview()));
        checkReuse();
        expect(benchmarkServiceMock.previewBatteryReuse).toHaveBeenCalledTimes(1);

        const select = query<HTMLSelectElement>('#batterySelect')!;
        select.value = select.options[1].value;
        select.dispatchEvent(new Event('change'));
        fixture.detectChanges();
        expect(vi.mocked(benchmarkServiceMock.previewBatteryReuse).mock.lastCall![0].batteryId).toBe(6);

        component.selectTestedModel(component.systemConfigs[0]);
        component.selectAssessorModel(component.systemConfigs[0]);
        component.selectClaimVerifierModel(null);
        expect(benchmarkServiceMock.previewBatteryReuse).toHaveBeenCalledTimes(5);

        setRunCount(2);
        expect(vi.mocked(benchmarkServiceMock.previewBatteryReuse).mock.lastCall![0].runsPerSuite).toBe(2);
      });

      it('should cancel a stale preview request when the settings change, and hold Start while one is pending', () => {
        renderLauncher();
        chooseBattery();
        const first = new Subject<BenchmarkBatteryReusePreviewDto>();
        const second = new Subject<BenchmarkBatteryReusePreviewDto>();
        benchmarkServiceMock.previewBatteryReuse.mockReturnValueOnce(first).mockReturnValueOnce(second);

        checkReuse();
        expect(first.observed).toBe(true);
        expect(summary()).toBe('Checking which earlier runs can be reused…');
        expect(component.canStartRun).toBe(false);
        expect(component.startBenchmarkHint).toBe('Checking which earlier runs can be reused…');

        setRunCount(2);
        expect(first.observed).toBe(false);
        expect(second.observed).toBe(true);

        second.next(buildPreview({ runsPerSuite: 2, launchCount: 3, slots: [] }));
        second.complete();
        fixture.detectChanges();
        expect(summary()).toBe('Reusing 1 earlier run (#12); launching 3.');
        expect(component.canStartRun).toBe(true);
      });

      it('should hold Start and say so when the preview fails', () => {
        renderLauncher();
        chooseBattery();
        benchmarkServiceMock.previewBatteryReuse.mockReturnValue(throwError(() => ({ status: 404, error: 'Battery not found.' })));

        checkReuse();

        expect(summary()).toBe('The reuse of earlier runs could not be previewed: Battery not found.');
        expect(component.canStartRun).toBe(false);
        expect(component.startBenchmarkHint).toContain('clear Reuse earlier runs');

        reuseBox()!.click();
        fixture.detectChanges();
        expect(component.canStartRun).toBe(true);
        expect(query('.battery-reuse-row')).toBeNull();
      });

      it('should send the previewed runs as attach on Start, and not remember the choice', () => {
        renderLauncher();
        chooseBattery();
        benchmarkServiceMock.previewBatteryReuse.mockReturnValue(of(buildPreview()));
        benchmarkServiceMock.startBatteryRun.mockReturnValue(of({ batteryRunId: 9 }));
        checkReuse();

        startButton().click();
        fixture.detectChanges();

        const body = vi.mocked(benchmarkServiceMock.startBatteryRun).mock.lastCall![0];
        expect(body.attach).toEqual([{ suiteIndex: 0, round: 1, runId: 12 }]);
        expect(body.runsPerSuite).toBe(1);

        // A per-start decision: cleared after the start and absent from the stored settings.
        expect(component.reuseEarlierRuns).toBe(false);
        expect(reuseBox()!.checked).toBe(false);
        const stored = JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY)!);
        expect(Object.keys(stored)).not.toContain('reuseEarlierRuns');
        expect(Object.keys(stored)).not.toContain('attach');

        const restored = TestBed.createComponent(AdminBenchmarkComponent);
        restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
        restored.detectChanges();
        expect(restored.componentInstance.runTargetKind).toBe('battery');
        expect(restored.componentInstance.reuseEarlierRuns).toBe(false);
        expect((restored.nativeElement.querySelector('#reuseEarlierRunsInput') as HTMLInputElement).checked).toBe(false);
        restored.componentInstance.ngOnDestroy();
      });

      it('should preview again after a start refused because a reused run stopped qualifying', () => {
        renderLauncher();
        chooseBattery();
        benchmarkServiceMock.previewBatteryReuse.mockReturnValue(of(buildPreview()));
        benchmarkServiceMock.startBatteryRun.mockReturnValue(throwError(() => ({
          status: 400, error: 'Run #12, chosen for suite \'Default Suite\', round 1, no longer qualifies.'
        })));
        checkReuse();

        startButton().click();
        fixture.detectChanges();

        expect(card().querySelector('.alert-danger')!.textContent).toContain('no longer qualifies');
        expect(benchmarkServiceMock.previewBatteryReuse).toHaveBeenCalledTimes(2);
        expect(component.reuseEarlierRuns).toBe(true);
      });
    });

    describe('banner lifecycle', () => {
      function attachBattery(overrides: Partial<BenchmarkBatteryRunDto>): void {
        const batteryRun = buildBatteryRun(overrides);
        benchmarkServiceMock.getActiveBatteryRun.mockReturnValue(of(batteryRun));
        benchmarkServiceMock.getBatteryRun.mockReturnValue(of(batteryRun));
        component.activeSubTab = 'run';
        // What ngOnInit does on a page load, repeated after the suite's own first load.
        component.checkActiveBatteryRun();
        fixture.detectChanges();
      }

      const banner = (): HTMLElement | null => query('.battery-banner');
      const bannerButton = (text: string): HTMLButtonElement | undefined =>
        (Array.from(banner()?.querySelectorAll('button') ?? []) as HTMLButtonElement[])
          .find(b => (b.textContent || '').includes(text));

      it('should reattach the banner to a live battery run on load, naming the suite and round', () => {
        attachBattery({ status: 'Running', currentSuitePosition: 2, currentSuiteName: 'Second Suite', currentRound: 1 });

        expect(banner()).toBeTruthy();
        expect(banner()!.textContent).toContain('Battery Run #9 (Core Battery)');
        expect(banner()!.querySelector('.battery-progress-label')!.textContent!.trim())
          .toBe('Suite 2 of 2 (Second Suite) · round 1 of 1.');
        expect(bannerButton('Cancel Battery')).toBeTruthy();
        expect(bannerButton('Continue')).toBeUndefined();
      });

      it('should hide the banner while its dialog is open and once the battery run completes', () => {
        attachBattery({ status: 'Running' });

        bannerButton('Show Battery Progress')!.click();
        fixture.detectChanges();
        expect(component.batteryDialogVisible).toBe(true);
        expect(component.dialogBatteryRunId).toBe(9);
        expect(banner()).toBeNull();

        component.onBatteryDialogClosed();
        fixture.detectChanges();
        expect(banner()).toBeTruthy();

        benchmarkServiceMock.getBatteryRun.mockReturnValue(of(buildBatteryRun({ status: 'Completed', completedSuiteCount: 2 })));
        (component as any).pollBatteryRun(9);
        fixture.detectChanges();
        expect(banner()).toBeNull();
        expect(component.activeBatteryRun).not.toBeNull();
      });

      it('should offer Continue for a stopped battery run and resume it', () => {
        attachBattery({ status: 'Stopped', stopReason: 'MemberFailed', stopReasonText: 'A member run failed', resumable: true });
        benchmarkServiceMock.resumeBatteryRun.mockReturnValue(of({ batteryRunId: 9 }));

        expect(bannerButton('Re-run under current instrument')).toBeUndefined();
        const cont = bannerButton('Continue (A member run failed)')!;
        expect(cont).toBeTruthy();
        cont.click();

        expect(benchmarkServiceMock.resumeBatteryRun).toHaveBeenCalledWith(9, 'Continue');
      });

      it('should offer only Re-run under current instrument and Cancel after an instrument change', () => {
        attachBattery({
          status: 'Stopped', stopReason: 'InstrumentChanged',
          stopReasonText: 'A member is not comparable with the others', resumable: true
        });
        benchmarkServiceMock.resumeBatteryRun.mockReturnValue(of({ batteryRunId: 9 }));
        benchmarkServiceMock.cancelBatteryRun.mockReturnValue(of(undefined));

        expect(bannerButton('Continue')).toBeUndefined();
        expect(bannerButton('Cancel Battery')).toBeTruthy();
        bannerButton('Re-run under current instrument')!.click();

        expect(benchmarkServiceMock.resumeBatteryRun).toHaveBeenCalledWith(9, 'RerunUnderCurrentInstrument');
      });

      it('should cancel the battery run from the banner', () => {
        attachBattery({ status: 'Running' });
        benchmarkServiceMock.cancelBatteryRun.mockReturnValue(of(undefined));

        bannerButton('Cancel Battery')!.click();

        expect(benchmarkServiceMock.cancelBatteryRun).toHaveBeenCalledWith(9);
      });

      it('should hold the battery Web Lock while polling, and leave it with the battery when a member is polled', () => {
        const lockService = TestBed.inject(BenchmarkBackgroundActivityService);
        const acquireBatterySpy = vi.spyOn(lockService, 'acquireForBattery').mockReturnValue(undefined);
        const acquireRunSpy = vi.spyOn(lockService, 'acquireForRun').mockReturnValue(undefined);
        const releaseSpy = vi.spyOn(lockService, 'release').mockReturnValue(undefined);

        (component as any).startBatteryPolling(9);
        expect(acquireBatterySpy).toHaveBeenCalledTimes(1);
        expect(acquireBatterySpy).toHaveBeenCalledWith(9);

        (component as any).startPolling(42);
        (component as any).stopPolling();
        expect(acquireRunSpy).not.toHaveBeenCalled();
        expect(releaseSpy).not.toHaveBeenCalled();

        (component as any).stopBatteryPolling();
        expect(releaseSpy).toHaveBeenCalledTimes(1);
      });

      it('should hand a member from the battery dialog to the run progress dialog, and back', () => {
        attachBattery({ status: 'Running' });
        vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);
        vi.spyOn(component.runProgressDialog.nativeElement, 'close').mockReturnValue(undefined);
        component.openBatteryDialog();

        component.onOpenRunProgressFromBattery(42);
        fixture.detectChanges();
        expect(component.batteryDialogVisible).toBe(false);
        expect(component.activeRunId).toBe(42);
        expect(component.returnToBatteryOnClose).toBe(true);

        component.closeRunProgressDialog();
        expect(component.batteryDialogVisible).toBe(true);
        expect(component.returnToBatteryOnClose).toBe(false);
      });

      it('should switch to the Multi-Suite tab and show the analysis the dialog asks for', () => {
        attachBattery({ status: 'Completed' });
        component.openBatteryDialog(9);

        component.onOpenBatteryAnalysis(9);

        expect(component.activeSubTab).toBe('multisuite');
        expect(component.batteryDialogVisible).toBe(false);
        expect(component.batteriesPanel).toBeTruthy();
      });
    });

    describe('completion signal', () => {
      let playSpy: Mock;
      let notifySpy: Mock;

      function member(runId: number, suiteIndex: number): any {
        return { memberId: runId, suiteIndex, round: 1, runId, runStatus: 'Completed', usable: true, superseded: false };
      }

      function pollBattery(overrides: Partial<BenchmarkBatteryRunDto>): void {
        benchmarkServiceMock.getBatteryRun.mockReturnValue(of(buildBatteryRun(overrides)));
        (component as any).pollBatteryRun(9);
      }

      function pollRun(id: number, status: string): void {
        benchmarkServiceMock.getRun.mockReturnValue(of({
          id, benchmarkSuiteId: 1, suiteName: 'Default Suite', testedModelDisplayNameUsed: 'Test Model',
          testedModelProviderUsed: 'Anthropic', testedModelIdUsed: 'claude-3-5-sonnet',
          assessorModelDisplayNameUsed: 'Test Assessor', assessorModelProviderUsed: 'Anthropic',
          assessorModelIdUsed: 'claude-3-5-sonnet', startedByUserName: 'admin', status,
          startedAtUtc: '2026-10-02T00:00:00Z', completedAtUtc: null, totalQuestionCount: 3, answers: []
        } as any));
        (component as any).pollRunDetail(id);
      }

      beforeEach(() => {
        playSpy = vi.spyOn(TestBed.inject(BenchmarkCompletionSoundService), 'play').mockResolvedValue('played');
        const notificationService = TestBed.inject(BenchmarkCompletionNotificationService);
        notifySpy = vi.spyOn(notificationService, 'notify').mockReturnValue(undefined as any);
        vi.spyOn(notificationService, 'permission').mockReturnValue('granted');
        component.completionSound = true;
        component.completionNotification = true;
      });

      it('should signal once for the battery run and never for a member', () => {
        pollBattery({ status: 'Running', currentRunId: 41, members: [member(41, 0)] });
        pollRun(41, 'Running');
        pollRun(41, 'Completed');

        pollBattery({ status: 'Running', currentRunId: 42, members: [member(41, 0), member(42, 1)] });
        pollRun(42, 'Running');

        pollBattery({ status: 'Completed', completedSuiteCount: 2, currentRunId: null, members: [member(41, 0), member(42, 1)] });
        pollRun(42, 'Completed');
        pollBattery({ status: 'Completed', completedSuiteCount: 2, currentRunId: null, members: [member(41, 0), member(42, 1)] });

        expect(vi.mocked(playSpy).mock.calls).toEqual([['battery:9']]);
        expect(notifySpy).toHaveBeenCalledTimes(1);
        expect(notifySpy).toHaveBeenCalledWith('battery:9', 'AI Benchmark', 'Battery #9 — Core Battery — 2 of 2 suites — Completed');
      });

      it('should signal a battery run that stops, but not one that is canceled', () => {
        pollBattery({ status: 'Running' });
        pollBattery({ status: 'Cancelled' });
        expect(playSpy).not.toHaveBeenCalled();

        // The dialog continues it; this page's poller sees it live again.
        benchmarkServiceMock.getBatteryRun.mockReturnValue(of(buildBatteryRun({ status: 'Running' })));
        component.onBatteryResumedFromDialog(9);
        pollBattery({ status: 'Stopped', stopReason: 'MemberFailed' });
        expect(playSpy).toHaveBeenCalledTimes(1);
        expect(playSpy).toHaveBeenCalledWith('battery:9');
      });

      it('should not signal a battery run first seen already finished', () => {
        pollBattery({ status: 'Completed' });

        expect(playSpy).not.toHaveBeenCalled();
        expect(notifySpy).not.toHaveBeenCalled();
      });
    });

    it('should badge a Run History card with its battery run and suite position', () => {
      benchmarkServiceMock.getRuns.mockReturnValue(of([
        {
          id: 2, benchmarkSuiteId: 1, suiteName: 'Default Suite', testedModelDisplayNameUsed: 'Model A',
          testedModelProviderUsed: 'Anthropic', testedModelIdUsed: 'model-a', assessorModelDisplayNameUsed: 'Model B',
          status: 'Completed', startedAtUtc: '2026-10-02T00:00:00Z', totalAnswerDurationMs: 1000, totalDurationMs: 1000,
          speedMeasurementDegraded: false, answeredQuestionCount: 5, totalQuestionCount: 5, unansweredQuestionCount: 0,
          candidateSystemPromptSha256: 'sha-a', toolGuidesSha256: 'guide-a', knowledgeBaseHeadSha: 'kb-a',
          wikiHeadSha: 'wiki-a', sourceCodeHeadSha: 'src-a',
          batteryRunId: 9, batteryName: 'Core Battery', batterySuitePosition: 2, batterySuiteCount: 3
        },
        {
          id: 1, benchmarkSuiteId: 1, suiteName: 'Default Suite', testedModelDisplayNameUsed: 'Model A',
          testedModelProviderUsed: 'Anthropic', testedModelIdUsed: 'model-a', assessorModelDisplayNameUsed: 'Model B',
          status: 'Completed', startedAtUtc: '2026-10-01T00:00:00Z', totalAnswerDurationMs: 1000, totalDurationMs: 1000,
          speedMeasurementDegraded: false, answeredQuestionCount: 5, totalQuestionCount: 5, unansweredQuestionCount: 0,
          candidateSystemPromptSha256: 'sha-a', toolGuidesSha256: 'guide-a', knowledgeBaseHeadSha: 'kb-a',
          wikiHeadSha: 'wiki-a', sourceCodeHeadSha: 'src-a'
        }
      ]));
      fixture.detectChanges();

      (fixture.nativeElement.querySelector('#bm-tab-history') as HTMLButtonElement).click();
      fixture.detectChanges();

      const kicker = (id: number) =>
        fixture.nativeElement.querySelector(`article.rh-card[data-run-id="${id}"] .rh-card-kicker`) as HTMLElement;
      expect(kicker(2).querySelector('.rh-battery-badge')!.textContent!.trim()).toBe('Battery #9 · suite 2/3');
      expect(kicker(1).querySelector('.rh-battery-badge')).toBeNull();
    });
  });

  describe('opening a series that this page is not driving', () => {
    it('should point the progress dialog at the requested series and open it', () => {
      component.openSeriesDialog(7);

      expect(component.seriesDialogId).toBe(7);
      expect(component.dialogSeriesId).toBe(7);
      expect(component.multiRunDialogVisible).toBe(true);
    });

    it('should fall back to the live series when none was explicitly opened', () => {
      component.activeSeriesId = 2;

      expect(component.seriesDialogId).toBeNull();
      expect(component.dialogSeriesId).toBe(2);
    });

    it('should clear the explicitly opened series when the dialog closes', () => {
      component.activeSeriesId = 2;
      component.openSeriesDialog(7);

      component.onMultiRunDialogClosed();

      expect(component.seriesDialogId).toBeNull();
      expect(component.multiRunDialogVisible).toBe(false);
      // Back to the live series, which is what the banner and the run labelling describe.
      expect(component.dialogSeriesId).toBe(2);
    });
  });

  describe('handing a group analysis from the series dialog to the multirun panel', () => {
    beforeEach(() => fixture.detectChanges());

    it('should switch to the Multi-Run Analysis tab and clear the series dialog state', () => {
      // The panel's own fetch is not the subject here, and it would reach a service method this
      // suite's mock does not carry.
      vi.spyOn(MultiRunComponent.prototype, 'openGroupById').mockReturnValue(undefined);
      component.multiRunDialogVisible = true;
      component.seriesDialogId = 9;

      component.onOpenGroupAnalysisFromSeries(42);

      expect(component.activeSubTab).toBe('multirun');
      expect(component.multiRunDialogVisible).toBe(false);
      expect(component.seriesDialogId).toBeNull();
    });

    it('should hand the group id to the multirun panel once the tab has rendered it', () => {
      const openGroupByIdSpy = vi.spyOn(MultiRunComponent.prototype, 'openGroupById').mockReturnValue(undefined);

      component.onOpenGroupAnalysisFromSeries(42);

      // The panel lives inside @if (activeSubTab === 'multirun'), so it does not exist until the
      // tab switch above has been flushed through change detection.
      expect(component.multiRunPanel).toBeTruthy();
      expect(openGroupByIdSpy).toHaveBeenCalledWith(42);
    });
  });
});
