import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed, fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import { AdminBenchmarkService } from '../../services/admin-benchmark.service';
import { BenchmarkCompletionSoundService } from '../../services/benchmark-completion-sound.service';
import { AdminBenchmarkSpecContext, clearStoredState, createAdminBenchmarkFixture } from './benchmark.component.testing';

describe('AdminBenchmarkComponent', () => {
  let ctx: AdminBenchmarkSpecContext;
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: MockedObject<AdminBenchmarkService>;

  beforeEach(clearStoredState);

  afterEach(clearStoredState);

  beforeEach(async () => {
    ctx = await createAdminBenchmarkFixture();
    ({ component, fixture, benchmarkServiceMock } = ctx);
  });

  describe('run progress dialog', () => {
    /**
     * A run detail fixture. Defaults describe a run mid-answering; the overrides let each
     * test move it to a later stage without restating the whole DTO.
     */
    function buildRun(overrides: any = {}): any {
      return {
        id: 42,
        benchmarkSuiteId: 1,
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Test Model',
        testedModelProviderUsed: 'Anthropic',
        testedModelIdUsed: 'claude-3-5-sonnet',
        testedModelParallelExecutionModeUsed: 2,
        assessorModelDisplayNameUsed: 'Test Assessor',
        assessorModelProviderUsed: 'Anthropic',
        assessorModelIdUsed: 'claude-3-5-sonnet',
        startedByUserName: 'admin',
        status: 'Running',
        startedAtUtc: '2026-09-02T00:00:00Z',
        completedAtUtc: null,
        totalAnswerDurationMs: 0,
        scoringProfileName: 'Default Intelligence Profile',
        scoringProfileId: 1,
        scoringMethodVersion: 2,
        difficultyFallbackUsed: false,
        speedMeasurementDegraded: false,
        maxParallelQuestionsUsed: 1,
        answeredQuestionCount: 0,
        totalQuestionCount: 3,
        assessmentParseFailed: false,
        totalInputTokens: 0,
        totalOutputTokens: 0,
        totalCacheReadTokens: 0,
        totalCacheCreationTokens: 0,
        totalDurationMs: 0,
        errorMessage: null,
        answers: [],
        ...overrides
      };
    }

    function buildAnswer(orderIndex: number, overrides: any = {}): any {
      return {
        id: 100 + orderIndex,
        benchmarkRunId: 42,
        orderIndex,
        questionText: `Answered question ${orderIndex}`,
        difficulty: 1,
        answerText: `SECRET ANSWER BODY ${orderIndex}`,
        thoughtText: `SECRET THOUGHT BODY ${orderIndex}`,
        reviewComment: `SECRET REVIEW BODY ${orderIndex}`,
        status: 'Ok',
        assessmentStatus: 'Scored',
        durationMs: 1234,
        timeToFirstTokenMs: 456,
        inputTokens: 900,
        outputTokens: 310,
        ...overrides
      };
    }

    it('should open the dialog when a benchmark run starts successfully', () => {
      ctx.launcher.selectedSuiteId = 1;
      ctx.launcher.testedConfigId = 1;
      ctx.launcher.assessorConfigId = 1;
      ctx.refresh();

      const showModal = vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      benchmarkServiceMock.startRun.mockReturnValue(of({ runId: 42 }));
      benchmarkServiceMock.getRun.mockReturnValue(of(buildRun()));

      ctx.runTab().startBenchmark();

      expect(showModal).toHaveBeenCalled();
      expect(ctx.monitor.isRunProgressDialogOpen).toBe(true);
      component.closeRunProgressDialog();
    });

    describe('board quote check before start', () => {
      function selectSuiteWithBoard(): void {
        fixture.detectChanges();
        ctx.workspace.suites = [{
          id: 1, name: 'Snapshot Suite', description: null, createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null,
          questionCount: 3, assessedQuestionCount: 3, difficultyFullyAssessed: true, gameSnapshotId: 7
        }];
        ctx.launcher.selectedSuiteId = 1;
        ctx.launcher.testedConfigId = 1;
        ctx.launcher.assessorConfigId = 1;
        vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);
        benchmarkServiceMock.startRun.mockReturnValue(of({ runId: 42 }));
        benchmarkServiceMock.getRun.mockReturnValue(of(buildRun()));
      }

      const missingCheck = {
        bulletCount: 10, checkedLiteralCount: 10, unquotedBulletCount: 0, unquotedBullets: [],
        missingLiterals: [{ questionId: 9, orderIndex: 3, literal: 'a blessed +1 long sword', lineExcerpt: 'x' }]
      };

      it('should warn and wait for acknowledgement when the suite rubrics quote text the board lacks', () => {
        selectSuiteWithBoard();
        const showWarning = vi.spyOn(ctx.runTab().boardQuoteWarningDialog!.nativeElement, 'showModal').mockReturnValue(undefined);
        benchmarkServiceMock.getBoardFactsCheck.mockReturnValue(of(missingCheck));

        ctx.runTab().startBenchmark();

        expect(benchmarkServiceMock.getBoardFactsCheck).toHaveBeenCalledWith(1);
        expect(benchmarkServiceMock.startRun).not.toHaveBeenCalled();
        expect(showWarning).toHaveBeenCalled();
        expect(ctx.monitor.startingRun).toBe(false);
        const text = (ctx.runTab().boardQuoteWarningDialog!.nativeElement.textContent || '').replace(/\s+/g, ' ');
        expect(text).toContain('These rubrics quote text the board does not contain; grades on them will rest on stale facts.');
        expect(text).toContain('Q3: "a blessed +1 long sword"');

        const buttons = Array.from(ctx.runTab().boardQuoteWarningDialog!.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
        for (const btn of buttons) {
          expect(btn.getAttribute('type')).toBe('button');
          expect((btn.textContent || '').trim() || btn.getAttribute('aria-label')).toBeTruthy();
        }
        const acknowledge = buttons.find(b => (b.textContent || '').includes('Acknowledge & Start Run'))!;
        acknowledge.click();

        expect(benchmarkServiceMock.getBoardFactsCheck).toHaveBeenCalledTimes(1);
        expect(benchmarkServiceMock.startRun).toHaveBeenCalledTimes(1);
        expect(ctx.runTab().launchBoardFactsCheck).toBeNull();
        component.closeRunProgressDialog();
      });

      it('should start nothing when the warning is cancelled', () => {
        selectSuiteWithBoard();
        vi.spyOn(ctx.runTab().boardQuoteWarningDialog!.nativeElement, 'showModal').mockReturnValue(undefined);
        benchmarkServiceMock.getBoardFactsCheck.mockReturnValue(of(missingCheck));

        ctx.runTab().startBenchmark();
        ctx.runTab().closeBoardQuoteWarningDialog();

        expect(benchmarkServiceMock.startRun).not.toHaveBeenCalled();
        expect(ctx.runTab().launchBoardFactsCheck).toBeNull();
      });

      it('should start straight away when every quote is on the board', () => {
        selectSuiteWithBoard();
        const showWarning = vi.spyOn(ctx.runTab().boardQuoteWarningDialog!.nativeElement, 'showModal').mockReturnValue(undefined);
        benchmarkServiceMock.getBoardFactsCheck.mockReturnValue(of({ ...missingCheck, missingLiterals: [] }));

        ctx.runTab().startBenchmark();

        expect(showWarning).not.toHaveBeenCalled();
        expect(benchmarkServiceMock.startRun).toHaveBeenCalledTimes(1);
        component.closeRunProgressDialog();
      });

      it('should start anyway when the check itself fails, since it is advisory', () => {
        selectSuiteWithBoard();
        vi.spyOn(console, 'warn').mockReturnValue(undefined);
        benchmarkServiceMock.getBoardFactsCheck.mockReturnValue(throwError(() => ({ status: 500 })));

        ctx.runTab().startBenchmark();

        expect(benchmarkServiceMock.startRun).toHaveBeenCalledTimes(1);
        component.closeRunProgressDialog();
      });

      it('should not check a suite that has no board', () => {
        selectSuiteWithBoard();
        ctx.workspace.suites = [{ ...ctx.workspace.suites[0], gameSnapshotId: null }];

        ctx.runTab().startBenchmark();

        expect(benchmarkServiceMock.getBoardFactsCheck).not.toHaveBeenCalled();
        expect(benchmarkServiceMock.startRun).toHaveBeenCalledTimes(1);
        component.closeRunProgressDialog();
      });
    });

    it('should derive the run stage from the run detail', () => {
      ctx.monitor.activeRunDetail = buildRun({ answers: [buildAnswer(1)] });
      expect(component.runStage).toBe('answering');

      // Answering and assessing are one stage: the executor assesses each answer immediately
      // after producing it, inside the same loop, so they never separate in wall-clock terms.
      ctx.monitor.activeRunDetail = buildRun({
        answers: [
          buildAnswer(1),
          buildAnswer(2, { assessmentStatus: 'Pending' }),
          buildAnswer(3, { assessmentStatus: 'Assessing' })
        ]
      });
      expect(component.runStage).toBe('answering');

      ctx.monitor.activeRunDetail = buildRun({
        answers: [buildAnswer(1), buildAnswer(2), buildAnswer(3)]
      });
      expect(component.runStage).toBe('finalizing');

      ctx.monitor.activeRunDetail = buildRun({
        status: 'Completed',
        completedAtUtc: '2026-09-02T00:05:00Z',
        answers: [buildAnswer(1), buildAnswer(2), buildAnswer(3)]
      });
      expect(component.runStage).toBe('terminal');
      expect(component.runIsTerminal).toBe(true);
    });

    it('should merge suite questions with answers and mark unanswered questions Pending', () => {
      ctx.monitor.activeRunDetail = buildRun({ answers: [buildAnswer(2, { benchmarkQuestionId: 2 })] });
      ctx.monitor.runProgressQuestions = [
        { id: 1, benchmarkSuiteId: 1, orderIndex: 1, questionText: 'First question', difficulty: 1, expectedPoints: null, createdAtUtc: '2026-09-01T00:00:00Z' },
        { id: 2, benchmarkSuiteId: 1, orderIndex: 2, questionText: 'Second question', difficulty: 1, expectedPoints: null, createdAtUtc: '2026-09-01T00:00:00Z' },
        { id: 3, benchmarkSuiteId: 1, orderIndex: 3, questionText: 'Third question', difficulty: 1, expectedPoints: null, createdAtUtc: '2026-09-01T00:00:00Z' }
      ];

      const rows = component.runProgressRows;
      expect(rows.length).toBe(3);
      expect(rows.map(r => r.orderIndex)).toEqual([1, 2, 3]);
      expect(rows[0].status).toBe('Pending');
      expect(rows[0].questionText).toBe('First question');
      expect(rows[1].status).toBe('Ok');
      expect(rows[2].status).toBe('Pending');

      expect(component.runRowChipLabel(rows[0])).toBe('Pending');
      expect(component.runRowChipClass(rows[0])).toBe('status-pending');
      expect(component.runRowChipLabel(rows[1])).toBe('Scored');
      expect(component.runRowChipClass(rows[1])).toBe('status-scored');
    });

    it('should label an answered but unassessed question Answered rather than guessing Assessing', () => {
      ctx.monitor.activeRunDetail = buildRun({
        answers: [buildAnswer(1, { assessmentStatus: 'Pending' })]
      });
      const row = component.runProgressRows[0];
      expect(component.runRowChipLabel(row)).toBe('Answered');
      expect(component.runRowChipClass(row)).toBe('status-ok');
    });

    it('should expose exactly one polling live region in the dialog', () => {
      ctx.monitor.activeRunDetail = buildRun({ answers: [buildAnswer(1)] });
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const dialog = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog') as HTMLElement;
      expect(dialog).toBeTruthy();

      const liveRegions = Array.from(dialog.querySelectorAll('[role="status"], [role="alert"]'))
        .filter(el => !el.closest('.job-diagnostics'));
      expect(liveRegions.length).toBe(1);
      expect(liveRegions[0].classList.contains('progress-status')).toBe(true);
      expect(liveRegions[0].getAttribute('aria-live')).toBe('polite');

      // It moved into the Assessments block when the claims and second-opinion bars were
      // replaced by counters; the dialog must not have gained a second one on the way.
      const block = liveRegions[0].closest('.job-progress-block') as HTMLElement;
      expect(block).toBeTruthy();
      expect(block.querySelector('#runAssessmentsProgressBar')).toBeTruthy();
    });

    it('should hide the active run banner while the dialog is open', () => {
      component.activeSubTab = 'run';
      ctx.monitor.activeRunDetail = buildRun({ answers: [] });
      ctx.refresh();
      const bannerShown = () => !!fixture.nativeElement.querySelector('.active-run-banner');
      expect(bannerShown()).toBe(true);

      // Driven through the real API: the flag is only ever set by these two methods,
      // and each refreshes the view itself.
      vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      component.openRunProgressDialog();
      expect(ctx.monitor.isRunProgressDialogOpen).toBe(true);
      expect(bannerShown()).toBe(false);

      component.closeRunProgressDialog();
      expect(bannerShown()).toBe(true);
    });

    it('should give every button in the open dialog an accessible name, type, and no title', () => {
      ctx.monitor.activeRunDetail = buildRun({
        status: 'CompletedWithErrors',
        completedAtUtc: '2026-09-02T00:05:00Z',
        answers: [buildAnswer(1), buildAnswer(2, { status: 'ProviderError', httpStatusCode: 429, errorMessage: 'Rate limited' })]
      });
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const dialog = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog');
      const buttons = Array.from(dialog.querySelectorAll('button')) as HTMLButtonElement[];
      expect(buttons.length).toBeGreaterThan(0);

      for (const btn of buttons) {
        const name = (btn.textContent || '').trim() || btn.getAttribute('aria-label');
        expect(name, btn.outerHTML.slice(0, 120)).toBeTruthy();
        expect(btn.getAttribute('type')).toBe('button');
        expect(btn.hasAttribute('title')).toBe(false);
      }

      expect(dialog.querySelectorAll('.btn-gh-primary, .btn-gh-danger, .btn-gh-icon').length).toBe(0);

      // The icon-only copy button must still carry its interest-triggered tooltip.
      const copyButton = dialog.querySelector('button[aria-label="Copy benchmark run diagnostics"]') as HTMLButtonElement;
      expect(copyButton).toBeTruthy();
      const tooltipId = copyButton.getAttribute('interestfor')!;
      expect(tooltipId).toBe('tip-copy-run-diagnostics');
      const tooltip = dialog.querySelector(`#${tooltipId}`);
      expect(tooltip).toBeTruthy();
      expect(tooltip!.getAttribute('popover')).toBe('hint');
      expect(copyButton.getAttribute('style')).toContain(`anchor-name: --${tooltipId}`);
      expect(tooltip!.getAttribute('style')).toContain(`position-anchor: --${tooltipId}`);

      // So must the icon-only download button beside it.
      const downloadButton = dialog.querySelector('button[aria-label="Download benchmark run diagnostics as a text file"]') as HTMLButtonElement;
      expect(downloadButton).toBeTruthy();
      const downloadTipId = downloadButton.getAttribute('interestfor')!;
      expect(downloadTipId).toBe('tip-download-run-diagnostics');
      const downloadTip = dialog.querySelector(`#${downloadTipId}`);
      expect(downloadTip).toBeTruthy();
      expect(downloadTip!.getAttribute('popover')).toBe('hint');
      expect(downloadTip!.textContent!.trim()).toBe('Download diagnostics');
      expect(downloadButton.getAttribute('style')).toContain(`anchor-name: --${downloadTipId}`);
      expect(downloadTip!.getAttribute('style')).toContain(`position-anchor: --${downloadTipId}`);
      expect(copyButton.nextElementSibling!.nextElementSibling).toBe(downloadButton);
    });

    /** The diagnostics text without its capture timestamp, which differs between two reads. */
    function withoutCaptureTime(text: string): string {
      return text.replace(/^Captured:.*$/m, '');
    }

    it('should download the run diagnostics as a text file named for suite, model and run', async () => {
      ctx.monitor.activeRunDetail = buildRun({
        status: 'CompletedWithErrors',
        suiteName: 'Snapshot: Tommi2 2026-09-17',
        testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
        id: 55,
        completedAtUtc: '2026-09-02T00:05:00Z',
        answers: [buildAnswer(1), buildAnswer(2, { status: 'ProviderError', httpStatusCode: 429, errorMessage: 'Rate limited' })]
      });
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const create = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test-url');
      vi.spyOn(URL, 'revokeObjectURL').mockReturnValue(undefined);
      const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockReturnValue(undefined);
      const expectedText = component.runDiagnosticsText;

      const downloadButton = fixture.nativeElement.querySelector(
        'button[aria-label="Download benchmark run diagnostics as a text file"]'
      ) as HTMLButtonElement;
      downloadButton.click();

      expect(click).toHaveBeenCalledTimes(1);
      const anchor = click.mock.contexts.at(-1) as HTMLAnchorElement;
      expect(anchor.download).toBe('snapshot-tommi2-2026-09-17_gemini-3.7-flash_run55_diagnostics.txt');
      expect(create).toHaveBeenCalledTimes(1);
      const blob = vi.mocked(create).mock.lastCall![0] as Blob;
      expect(blob.type).toBe('text/plain;charset=utf-8');
      expect(withoutCaptureTime(await blob.text())).toBe(withoutCaptureTime(expectedText));
    });

    it('should fall back to a generic diagnostics file name when no run is loaded', () => {
      ctx.monitor.activeRunDetail = null;

      expect(component.runDiagnosticsFileName).toBe('overseer-benchmark-run-diagnostics.txt');
    });

    it('should copy the run diagnostics, announce it, and reset after the timeout', fakeAsync(() => {
      ctx.monitor.activeRunDetail = buildRun({
        status: 'CompletedWithErrors',
        completedAtUtc: '2026-09-02T00:05:00Z',
        answers: [buildAnswer(1), buildAnswer(2, { status: 'ProviderError', httpStatusCode: 429, errorMessage: 'Rate limited' })]
      });
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const writeTextSpy = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
      const expectedText = component.runDiagnosticsText;
      expect(expectedText).toContain('Run ID: 42');

      const copyButton = fixture.nativeElement.querySelector(
        'button[aria-label="Copy benchmark run diagnostics"]'
      ) as HTMLButtonElement;
      expect(copyButton).toBeTruthy();

      copyButton.click();
      tick();
      fixture.detectChanges();

      expect(writeTextSpy).toHaveBeenCalledWith(expectedText);
      expect(component.copiedRunDiagnostics).toBe(true);
      expect(component.runDiagnosticsCopyStatus).toBe('Diagnostics copied to clipboard');

      tick(2000);
      fixture.detectChanges();

      expect(component.copiedRunDiagnostics).toBe(false);
      expect(component.runDiagnosticsCopyStatus).toBe('');
    }));

    it('should surface a run diagnostics clipboard failure inline rather than throwing', fakeAsync(() => {
      ctx.monitor.activeRunDetail = buildRun({
        status: 'Failed',
        completedAtUtc: '2026-09-02T00:05:00Z',
        answers: [buildAnswer(1, { status: 'Failed', errorMessage: 'boom' })]
      });
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'));

      const copyButton = fixture.nativeElement.querySelector(
        'button[aria-label="Copy benchmark run diagnostics"]'
      ) as HTMLButtonElement;
      copyButton.click();
      tick();
      fixture.detectChanges();

      expect(component.copiedRunDiagnostics).toBe(false);
      expect(component.runDiagnosticsCopyStatus).toBe('Could not copy the diagnostics to the clipboard.');
      expect(ctx.monitor.runErrorMessage).toBe('Could not copy the benchmark run diagnostics to the clipboard.');
    }));

    it('should not leak answer, thought, or assessor comment text into the diagnostics', () => {
      ctx.monitor.activeRunDetail = buildRun({
        answers: [buildAnswer(1), buildAnswer(2, { status: 'ProviderError', httpStatusCode: 429, errorMessage: 'Rate limited' })]
      });

      const text = component.runDiagnosticsText;
      expect(text).not.toContain('SECRET ANSWER BODY');
      expect(text).not.toContain('SECRET THOUGHT BODY');
      expect(text).not.toContain('SECRET REVIEW BODY');
      // What it must contain: the failure the operator would report.
      expect(text).toContain('http=429');
      expect(text).toContain('error: Rate limited');
      // Section headers and timezone/timestamp diagnostics
      expect(text).toContain('--- RUN ---');
      expect(text).toContain('--- POLLING ---');
      expect(text).toContain('--- QUESTIONS ---');
      expect(text).toContain('Started (raw):');
      expect(text).toContain('Started (parsed):');
    });

    it('prints the ticker mode on the Run poll line, and an attempts section for the sound and the notification', () => {
      ctx.monitor.activeRunDetail = buildRun({ answers: [] });
      benchmarkServiceMock.getRun.mockReturnValue(of(buildRun({ id: 42, status: 'Running', answers: [] })));
      ctx.monitor.startPolling(42);

      // Pushed directly rather than exercised through a real play() call, so this spec does not
      // depend on the actual browser audio stack; the sound service's own spec exercises play().
      const soundService = TestBed.inject(BenchmarkCompletionSoundService);
      (soundService as any).attempts.push({
        atUtc: '2026-09-19T00:00:00.000Z', key: 'run:999', hidden: false, focused: true,
        path: 'element', contextStateBefore: null, contextStateAfter: null,
        clockAdvanced: null, rebuilt: false, outcome: 'played'
      });

      const text = component.runDiagnosticsText;
      expect(text).toContain('Run poll: active every 2000 ms (timer)');
      expect(text).toContain('  attempts:');
      expect(text).toContain('key=run:999');

      ctx.monitor.stopPolling();
    });

    it('should reattach to a run already in progress without opening the dialog', () => {
      benchmarkServiceMock.getActiveRun.mockReturnValue(of({ runId: 77 }));
      benchmarkServiceMock.getRun.mockReturnValue(of(buildRun({ id: 77 })));
      const showModal = vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);

      ctx.monitor.checkActiveRun();

      expect(ctx.monitor.activeRunId).toBe(77);
      expect(benchmarkServiceMock.getRun).toHaveBeenCalledWith(77);
      expect(ctx.monitor.isRunProgressDialogOpen).toBe(false);
      expect(showModal).not.toHaveBeenCalled();
    });

    it('should do nothing when no run is active', () => {
      benchmarkServiceMock.getActiveRun.mockReturnValue(of(null));
      benchmarkServiceMock.getRun.mockClear();

      ctx.monitor.checkActiveRun();

      expect(ctx.monitor.activeRunId).toBeNull();
      expect(benchmarkServiceMock.getRun).not.toHaveBeenCalled();
    });

    it('should fetch the suite questions once per dialog open, not per poll tick', () => {
      ctx.monitor.activeRunDetail = buildRun({ answers: [] });
      benchmarkServiceMock.getQuestions.mockClear();
      benchmarkServiceMock.getQuestions.mockReturnValue(of([]));
      vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);

      component.openRunProgressDialog();
      component.closeRunProgressDialog();
      component.openRunProgressDialog();

      expect(benchmarkServiceMock.getQuestions).toHaveBeenCalledTimes(1);
      expect(benchmarkServiceMock.getQuestions).toHaveBeenCalledWith(1);
      component.closeRunProgressDialog();
    });

    it('should render question text as plain text and never as innerHTML', () => {
      ctx.monitor.activeRunDetail = buildRun({ answers: [] });
      ctx.monitor.runProgressQuestions = [
        { id: 1, benchmarkSuiteId: 1, orderIndex: 1, questionText: '<img src=x onerror="alert(1)">', difficulty: 1, expectedPoints: null, createdAtUtc: '2026-09-01T00:00:00Z' }
      ];
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const excerpt = fixture.nativeElement.querySelector('.run-question-list .job-item-excerpt') as HTMLElement;
      expect(excerpt).toBeTruthy();
      expect(excerpt.querySelector('img')).toBeNull();
      expect(excerpt.textContent).toContain('<img src=x onerror="alert(1)">');
    });

    it('should compute elapsed time correctly for UTC timestamps without a Z designator', () => {
      // 5 minutes ago without Z suffix
      const fiveMinutesAgo = new Date(Date.now() - 300000).toISOString().replace('Z', '');
      ctx.monitor.activeRunDetail = buildRun({
        startedAtUtc: fiveMinutesAgo,
        completedAtUtc: null
      });

      const label = component.runElapsedLabel;
      // Should format as ~5m (e.g. 5m 00s or 5m 01s), not inflated by local timezone offset
      expect(label).toMatch(/^5m 0\ds$/);
    });

    it('should advance elapsed time at 1 Hz while dialog is open and stop on close', fakeAsync(() => {
      const now = Date.now();
      const startTime = new Date(now - 10000).toISOString().replace('Z', '');
      ctx.monitor.activeRunDetail = buildRun({
        status: 'Running',
        startedAtUtc: startTime,
        completedAtUtc: null
      });

      vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      vi.spyOn(component.runProgressDialog.nativeElement, 'close').mockReturnValue(undefined);

      component.openRunProgressDialog();
      expect(component.runElapsedLabel).toBe('10s');

      tick(1000);
      expect(component.runElapsedLabel).toBe('11s');

      component.closeRunProgressDialog();
      expect(ctx.monitor.runElapsedInterval).toBeNull();

      tick(5000);
      discardPeriodicTasks();
    }));

    it('should not start ticker for terminal run and stop ticker when poll reports terminal', fakeAsync(() => {
      vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      ctx.monitor.activeRunDetail = buildRun({
        status: 'Completed',
        startedAtUtc: '2026-09-02T17:00:00Z',
        completedAtUtc: '2026-09-02T17:05:00Z'
      });
      component.openRunProgressDialog();
      expect(ctx.monitor.runElapsedInterval).toBeNull();

      // Now set to running and open
      ctx.monitor.activeRunDetail = buildRun({
        status: 'Running',
        startedAtUtc: '2026-09-02T17:00:00Z',
        completedAtUtc: null
      });
      component.openRunProgressDialog();
      expect(ctx.monitor.runElapsedInterval).not.toBeNull();

      // Poll returns terminal run
      benchmarkServiceMock.getRun.mockReturnValue(of(buildRun({
        status: 'Completed',
        startedAtUtc: '2026-09-02T17:00:00Z',
        completedAtUtc: '2026-09-02T17:05:00Z'
      })));
      (ctx.monitor as any).pollRunDetail(42);
      expect(ctx.monitor.runElapsedInterval).toBeNull();

      discardPeriodicTasks();
    }));

    it('should render diagnostics details unconditionally closed by default and without failure count on healthy run', () => {
      ctx.monitor.activeRunDetail = buildRun({
        status: 'Running',
        answers: [buildAnswer(1)]
      });
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const details = fixture.nativeElement.querySelector('.job-diagnostics') as HTMLDetailsElement;
      expect(details).toBeTruthy();
      expect(details.open).toBe(false);

      const copyBtn = details.querySelector('button[aria-label="Copy benchmark run diagnostics"]');
      expect(copyBtn).toBeTruthy();

      const summary = details.querySelector('summary') as HTMLElement;
      expect(summary.textContent).toContain('Diagnostics');
      expect(details.querySelector('.job-diagnostics-count')).toBeNull();
    });

    it('should show failure count in diagnostics summary when answers fail', () => {
      ctx.monitor.activeRunDetail = buildRun({
        status: 'Running',
        answers: [buildAnswer(1, { status: 'Failed' }), buildAnswer(2, { status: 'Failed' })]
      });
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const details = fixture.nativeElement.querySelector('.job-diagnostics') as HTMLDetailsElement;
      expect(details).toBeTruthy();
      const countChip = details.querySelector('.job-diagnostics-count') as HTMLElement;
      expect(countChip).toBeTruthy();
      expect(countChip.textContent).toContain('2 failed');
    });

    it('should record lastRunPollError on failed poll and report it in diagnostics text', () => {
      const consoleError = vi.spyOn(console, 'error').mockReturnValue(undefined);
      benchmarkServiceMock.getRun.mockReturnValue(throwError(() => ({
        status: 500,
        message: 'Internal Server Error'
      })));

      (ctx.monitor as any).pollRunDetail(42);

      expect(ctx.monitor.lastRunPollError).toContain('500');
      expect(component.runDiagnosticsText).toContain('Last poll error:');
      expect(component.runDiagnosticsText).toContain('500');
      expect(consoleError).toHaveBeenCalledWith('Failed to poll run detail', expect.any(Object));
    });

    it('should produce non-empty diagnostics text when activeRunDetail is null', () => {
      ctx.monitor.activeRunDetail = null;
      const text = component.runDiagnosticsText;
      expect(text).toBeTruthy();
      expect(text).toContain('=== BENCHMARK RUN DIAGNOSTICS ===');
      expect(text).toContain('No run detail received yet.');
      expect(text).toContain('--- POLLING ---');
      expect(text).toContain('--- ERRORS ---');
    });

    it('should render diagnostics pre containing code child with tabindex 0', () => {
      ctx.monitor.activeRunDetail = buildRun({ answers: [] });
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const pre = fixture.nativeElement.querySelector('.job-diagnostics pre') as HTMLPreElement;
      expect(pre).toBeTruthy();
      expect(pre.getAttribute('tabindex')).toBe('0');
      const code = pre.querySelector('code');
      expect(code).toBeTruthy();
    });

    it('leaves the diagnostics text unrendered while the panel is closed', () => {
      ctx.monitor.activeRunDetail = buildRun({ answers: [] });
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const code = fixture.nativeElement.querySelector('.job-diagnostics pre code') as HTMLElement;
      expect(code.textContent).toBe('');
    });

    it('renders the diagnostics text once the panel is opened', () => {
      ctx.monitor.activeRunDetail = buildRun({ answers: [] });
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const details = fixture.nativeElement.querySelector('.job-diagnostics') as HTMLDetailsElement;
      details.open = true;
      details.dispatchEvent(new Event('toggle'));
      fixture.detectChanges();

      const code = details.querySelector('pre code') as HTMLElement;
      expect(code.textContent).toContain('=== BENCHMARK RUN DIAGNOSTICS ===');
      expect(code.textContent).toContain('Captured:');
    });

    it('keeps the open panel stable across change-detection passes', fakeAsync(() => {
      ctx.monitor.activeRunDetail = buildRun({ answers: [] });
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const details = fixture.nativeElement.querySelector('.job-diagnostics') as HTMLDetailsElement;
      details.open = true;
      details.dispatchEvent(new Event('toggle'));
      fixture.detectChanges();

      const code = details.querySelector('pre code') as HTMLElement;
      const rendered = code.textContent;

      // The component is OnPush, so the view re-renders only once it is marked for check.
      tick(5);
      ctx.refresh();

      expect(code.textContent).toBe(rendered);
      expect(() => fixture.checkNoChanges()).not.toThrow();
      discardPeriodicTasks();
    }));

    it('should set returnToSeriesOnClose to true when opened from a series', () => {
      vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      expect(ctx.monitor.returnToSeriesOnClose).toBe(false);

      component.onOpenRunProgressFromSeries(42);

      expect(ctx.monitor.returnToSeriesOnClose).toBe(true);
      expect(ctx.monitor.isRunProgressDialogOpen).toBe(true);
      expect(ctx.monitor.multiRunDialogVisible).toBe(false);
    });

    it('should reopen multi-run dialog when closing single-run progress with returnToSeriesOnClose true', () => {
      vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      vi.spyOn(component.runProgressDialog.nativeElement, 'close').mockReturnValue(undefined);
      ctx.monitor.activeSeriesId = 10;
      component.onOpenRunProgressFromSeries(42);

      expect(ctx.monitor.multiRunDialogVisible).toBe(false);
      expect(ctx.monitor.returnToSeriesOnClose).toBe(true);

      component.closeRunProgressDialog();

      expect(ctx.monitor.multiRunDialogVisible).toBe(true);
      expect(ctx.monitor.returnToSeriesOnClose).toBe(false);
      expect(ctx.monitor.isRunProgressDialogOpen).toBe(false);
    });

    it('should not reopen multi-run dialog when closing single-run progress with returnToSeriesOnClose false', () => {
      vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      vi.spyOn(component.runProgressDialog.nativeElement, 'close').mockReturnValue(undefined);
      ctx.monitor.activeSeriesId = 10;
      component.openRunProgressDialog();

      expect(ctx.monitor.returnToSeriesOnClose).toBe(false);
      expect(ctx.monitor.multiRunDialogVisible).toBe(false);

      component.closeRunProgressDialog();

      expect(ctx.monitor.multiRunDialogVisible).toBe(false);
      expect(ctx.monitor.returnToSeriesOnClose).toBe(false);
    });

    it('should not reopen multi-run dialog when viewing full report detail from progress dialog', () => {
      vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      vi.spyOn(component.runProgressDialog.nativeElement, 'close').mockReturnValue(undefined);
      vi.spyOn(component, 'viewRunDetail').mockReturnValue(undefined);
      benchmarkServiceMock.getRun.mockReturnValue(of(buildRun({ id: 42 })));
      ctx.monitor.activeSeriesId = 10;
      component.onOpenRunProgressFromSeries(42);

      expect(ctx.monitor.returnToSeriesOnClose).toBe(true);

      component.viewActiveRunDetail();

      expect(ctx.monitor.multiRunDialogVisible).toBe(false);
      expect(ctx.monitor.returnToSeriesOnClose).toBe(false);
      expect(component.viewRunDetail).toHaveBeenCalledWith(42);
    });

    it('should render Back to Series and updated aria-label when returnToSeriesOnClose is true', () => {
      ctx.monitor.activeRunDetail = buildRun({ status: 'Running', answers: [] });
      ctx.monitor.returnToSeriesOnClose = true;
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const dialogEl = component.runProgressDialog.nativeElement;
      const closeBtn = dialogEl.querySelector('.dialog-header .btn-icon-action') as HTMLButtonElement;
      expect(closeBtn.getAttribute('aria-label')).toBe('Return to series progress');

      const cancelBtn = dialogEl.querySelector('.dialog-footer .btn-gh-cancel') as HTMLButtonElement;
      expect(cancelBtn.textContent?.trim()).toBe('Back to Series');

      // Check when terminal
      ctx.monitor.activeRunDetail = buildRun({ status: 'Completed', answers: [] });
      ctx.refresh();

      const terminalCancelBtn = dialogEl.querySelector('.dialog-footer .btn-gh-cancel') as HTMLButtonElement;
      expect(terminalCancelBtn.textContent?.trim()).toBe('Back to Series');
    });

    it('should keep polling and stay non-terminal when the first poll after a re-run launch still reports the previous status', () => {
      vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      vi.spyOn(component.runProgressDialog.nativeElement, 'close').mockReturnValue(undefined);

      component.selectedRunDetail = buildRun({
        id: 37,
        status: 'CompletedWithErrors',
        answers: [buildAnswer(3, { status: 'Failed' })]
      });
      benchmarkServiceMock.rerunFailedQuestions.mockReturnValue(of({ runId: 37 }));
      // The server has not yet flipped the row to Running: the first poll after the launch
      // still sees the previous attempt's terminal status.
      benchmarkServiceMock.getRun.mockReturnValue(of(buildRun({
        id: 37,
        status: 'CompletedWithErrors',
        answers: [buildAnswer(3, { status: 'Failed' })]
      })));

      component.rerunFailedFromRunDetail(37);

      expect(ctx.monitor.rerunLaunchPending).toBe(true);
      expect(component.runIsTerminal).toBe(false);
      expect(component.runStageLabel).toContain('Starting');
      expect(ctx.monitor.pollTickerHandle).not.toBeNull();

      fixture.detectChanges();
      const footer = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog .dialog-footer') as HTMLElement;
      expect(footer.querySelector('.btn-gh-delete')).toBeTruthy();
      const buttons = Array.from(footer.querySelectorAll('button')) as HTMLButtonElement[];
      expect(buttons.some(b => (b.textContent || '').trim() === 'View Full Report')).toBe(false);

      component.closeRunProgressDialog();
    });

    it('should clear the launch-pending state once a poll reports the run running', () => {
      vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      vi.spyOn(component.runProgressDialog.nativeElement, 'close').mockReturnValue(undefined);

      ctx.monitor.activeRunId = 37;
      ctx.monitor.rerunLaunchPending = true;
      (ctx.monitor as any).rerunLaunchedAtMs = Date.now();
      benchmarkServiceMock.getRun.mockReturnValue(of(buildRun({ id: 37, status: 'Running' })));

      (ctx.monitor as any).pollRunDetail(37);

      expect(ctx.monitor.rerunLaunchPending).toBe(false);
      expect(component.runIsRunning).toBe(true);

      component.closeRunProgressDialog();
    });

    it('should surface a refused re-run inside the progress dialog and keep the loaded run detail', () => {
      vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      vi.spyOn(component.runProgressDialog.nativeElement, 'close').mockReturnValue(undefined);

      ctx.monitor.activeRunDetail = buildRun({
        id: 37,
        status: 'CompletedWithErrors',
        suiteName: 'Suite X',
        answers: [buildAnswer(1, { status: 'Failed' })]
      });
      ctx.monitor.isRunProgressDialogOpen = true;
      benchmarkServiceMock.rerunFailedQuestions.mockReturnValue(throwError(() => ({
        status: 409,
        error: 'A benchmark run is already in progress.'
      })));

      component.rerunFailedFromProgress();

      expect(ctx.monitor.runErrorMessage).toBe('A benchmark run is already in progress.');
      expect(ctx.monitor.rerunLaunchPending).toBe(false);
      expect(ctx.monitor.activeRunDetail).not.toBeNull();

      ctx.refresh();
      const alert = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog .dialog-body .alert-danger') as HTMLElement;
      expect(alert).toBeTruthy();
      expect(alert.textContent).toContain('A benchmark run is already in progress.');
      const subtitle = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog .dialog-subtitle') as HTMLElement;
      expect(subtitle.textContent).toContain('Suite X');

      component.closeRunProgressDialog();
    });

    it('should open the run progress dialog full-screen', () => {
      fixture.detectChanges();
      const dialog = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog') as HTMLElement;
      expect(dialog).toBeTruthy();
      expect(dialog.classList.contains('gh-dialog-fullscreen')).toBe(true);
    });

    it('should put the question list in its own section and everything else before it', () => {
      ctx.monitor.activeRunDetail = buildRun({
        status: 'Completed',
        completedAtUtc: '2026-09-02T00:05:00Z',
        answers: [buildAnswer(1), buildAnswer(2)]
      });
      ctx.refresh();

      const body =fixture.nativeElement.querySelector('.benchmark-run-progress-dialog .dialog-body') as HTMLElement;
      const sections = Array.from(body.children) as HTMLElement[];
      expect(sections.map(s => s.tagName)).toEqual(['SECTION', 'SECTION']);
      const [overview, questions] = sections;
      expect(overview.classList.contains('run-progress-overview')).toBe(true);
      expect(questions.classList.contains('run-progress-questions')).toBe(true);

      // The overview keeps everything but the question list, in its order.
      for (const selector of ['.run-model-strip', '.run-stage-rail', '.job-progress-block',
        '.run-stat-strip', 'app-benchmark-cost-panel', '.job-diagnostics']) {
        expect(overview.querySelector(selector), selector).toBeTruthy();
      }
      expect(overview.querySelector('.run-question-list')).toBeNull();
      expect(overview.querySelector('[role="status"][aria-live="polite"].progress-status')).toBeTruthy();

      // The overview begins with the roster: no heading of its own. The dialog title is the focus target.
      expect(overview.querySelector('.progress-heading')).toBeNull();
      const dialogTitle = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog .dialog-header #runProgressDialogTitle') as HTMLElement;
      expect(dialogTitle.getAttribute('tabindex')).toBe('-1');

      expect(questions.getAttribute('aria-labelledby')).toBe('runProgressQuestionsTitle');
      expect(questions.getAttribute('tabindex')).toBe('0');
      const title = questions.querySelector('h4#runProgressQuestionsTitle') as HTMLElement;
      expect(title.textContent?.replace(/\s+/g, ' ').trim()).toBe('Questions 2');
      expect(questions.querySelectorAll('.run-question-list .job-item-row').length).toBe(2);
      expect(questions.querySelector('.gh-section-title')).toBe(title);
    });

    it('should focus the dialog title when the run progress dialog opens', () => {
      ctx.monitor.activeRunDetail = buildRun({ status: 'Completed', answers: [buildAnswer(1)] });
      const dialog = component.runProgressDialog.nativeElement as HTMLDialogElement;

      component.openRunProgressDialog();

      try {
        expect(dialog.open).toBe(true);
        expect(document.activeElement?.id).toBe('runProgressDialogTitle');
      } finally {
        component.closeRunProgressDialog();
      }
    });

    const progressSubtitle = () => (fixture.nativeElement.querySelector('.benchmark-run-progress-dialog .dialog-subtitle') as HTMLElement)
      .textContent!.replace(/\s+/g, ' ').trim();

    it('should say the run is starting in the subtitle until the run detail loads', () => {
      ctx.monitor.activeRunDetail = null;
      ctx.refresh();
      expect(progressSubtitle()).toBe('Starting benchmark run…');
    });

    it('should name the suite and profile in the subtitle once the run detail loads', () => {
      ctx.monitor.activeRunDetail = buildRun({ suiteName: 'Suite X', scoringProfileName: 'Strict' });
      ctx.refresh();
      expect(progressSubtitle()).toBe('Suite X | Profile: Strict');
    });

    describe('layout by width', () => {
      let dialog: HTMLDialogElement;

      function openAtWidth(width: string): HTMLElement {
        ctx.monitor.activeRunDetail = buildRun({ answers: [buildAnswer(1), buildAnswer(2)] });
        ctx.refresh();
        dialog = component.runProgressDialog.nativeElement as HTMLDialogElement;
        dialog.style.width = width;
        dialog.style.maxWidth = width;
        dialog.showModal();
        return dialog.querySelector('.dialog-body') as HTMLElement;
      }

      afterEach(() => {
        if (dialog?.open) dialog.close();
        dialog?.style.removeProperty('width');
        dialog?.style.removeProperty('max-width');
      });

      it('should lay the dialog out in two columns when the body is wide', () => {
        const body = openAtWidth('1400px');

        const style = getComputedStyle(body);
        expect(style.display).toBe('grid');
        expect(style.gridTemplateColumns.trim().split(/\s+/).length).toBe(2);
        const questions = body.querySelector('.run-progress-questions') as HTMLElement;
        expect(getComputedStyle(questions).overflowY).toBe('auto');
        expect(getComputedStyle(body.querySelector('.run-progress-overview') as HTMLElement).overflowY).toBe('auto');
      });

      it('should keep one column when narrow', () => {
        const body = openAtWidth('700px');

        const style = getComputedStyle(body);
        expect(style.display).not.toBe('grid');
        expect(style.overflowY).toBe('auto');
        const questions = body.querySelector('.run-progress-questions') as HTMLElement;
        expect(getComputedStyle(questions).overflowY).toBe('visible');
      });
    });

    it('should show the published score on a scored question row', () => {
      ctx.monitor.activeRunDetail = buildRun({
        answers: [
          buildAnswer(1, { qualityScore: 83 }),
          buildAnswer(2, { assessmentStatus: 'Pending', qualityScore: null })
        ]
      });
      ctx.refresh();

      const rows = Array.from(fixture.nativeElement.querySelectorAll('.run-question-list .job-item-row')) as HTMLElement[];
      const score = rows[0].querySelector('.job-item-score') as HTMLElement;
      expect(score.textContent?.trim()).toBe('Score 83');
      expect(score.querySelector('.visually-hidden')?.textContent).toBe('Score ');
      // Before the status chip.
      expect(score.nextElementSibling?.classList.contains('job-status-chip')).toBe(true);
      expect(rows[1].querySelector('.job-item-score')).toBeNull();
    });

    it('should give a scored question row a score tier badge', () => {
      ctx.monitor.activeRunDetail = buildRun({
        answers: [
          buildAnswer(1, { qualityScore: 83 }),
          buildAnswer(2, { qualityScore: 45 })
        ]
      });
      ctx.refresh();

      const scores = Array.from(fixture.nativeElement.querySelectorAll('.run-question-list .job-item-score')) as HTMLElement[];
      expect(scores.length).toBe(2);
      expect(scores[0].classList.contains('job-item-score')).toBe(true);
      expect(scores[0].classList.contains('badge-score-high')).toBe(true);
      expect(scores[1].classList.contains('badge-score-low')).toBe(true);
      expect(scores[1].textContent?.trim()).toBe('Score 45');
      expect(scores[1].nextElementSibling?.classList.contains('job-status-chip')).toBe(true);
    });

    it('should show the panel score on a panel run\'s question row, and none until both members have scored', () => {
      ctx.monitor.activeRunDetail = buildRun({
        isPanelRun: true,
        answers: [
          buildAnswer(1, { qualityScore: 90, panelQualityScore: 82.5 }),
          buildAnswer(2, { qualityScore: 70, panelQualityScore: null })
        ]
      });
      ctx.refresh();

      const panelRows = Array.from(fixture.nativeElement.querySelectorAll('.run-question-list .job-item-row')) as HTMLElement[];
      expect(panelRows[0].querySelector('.job-item-score')?.textContent?.trim()).toBe('Score 82.5');
      expect(panelRows[1].querySelector('.job-item-score')).toBeNull();
    });

    it('should point the re-run badge at the Questions column', () => {
      ctx.monitor.rerunScopeOrderIndexes = [2];
      ctx.monitor.activeRunDetail = buildRun({ answers: [buildAnswer(1), buildAnswer(2)] });
      ctx.refresh();

      const badge = fixture.nativeElement.querySelector('.rerun-scope-badge') as HTMLElement;
      expect(badge.textContent?.replace(/\s+/g, ' ')).toContain('Every question of the suite is listed under Questions; the re-run ones are marked.');
      expect(badge.querySelector('strong')?.textContent).toBe('Questions');
    });

    it('should name the assessor in the active run banner', () => {
      component.activeSubTab = 'run';
      ctx.monitor.activeRunDetail = buildRun({ answers: [] });
      ctx.refresh();
      const bannerText = ((fixture.nativeElement.querySelector('.active-run-banner') as HTMLElement).textContent || '')
        .replace(/\s+/g, ' ');

      expect(bannerText).toContain('Assessor: Test Assessor');
      expect(bannerText).not.toContain('Evaluator');
    });

    it('should name both assessors of a panel in the active run banner', () => {
      component.activeSubTab = 'run';
      ctx.monitor.activeRunDetail = buildRun({ answers: [], isPanelRun: true, coAssessorModelDisplayNameUsed: 'Co Assessor' });
      ctx.refresh();
      const bannerText = ((fixture.nativeElement.querySelector('.active-run-banner') as HTMLElement).textContent || '')
        .replace(/\s+/g, ' ');

      expect(bannerText).toContain('Assessors: Test Assessor + Co Assessor');
    });

    it('should give benchmark dialog content no padding of its own', () => {
      vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      vi.spyOn(component.runProgressDialog.nativeElement, 'close').mockReturnValue(undefined);

      fixture.detectChanges();
      const content = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog .dialog-content') as HTMLElement;
      expect(content).toBeTruthy();
      const style = getComputedStyle(content);
      expect(style.paddingTop).toBe('0px');
      expect(style.paddingBottom).toBe('0px');

      component.closeRunProgressDialog();
    });
  });

  describe('retry actions', () => {
    beforeEach(() => {
      component.selectedRunDetail = {
        id: 42,
        suiteName: 'Test Suite',
        testedModelConfigurationId: 1,
        assessorModelConfigurationId: 1,
        assessorAvailable: true,
        status: 'CompletedWithErrors',
        answers: [
          { id: 101, orderIndex: 1, questionText: 'Q1', status: 'Ok', assessmentStatus: 'Scored' },
          { id: 102, orderIndex: 2, questionText: 'Q2', status: 'ProviderError', assessmentStatus: 'Failed', assessmentError: 'Timeout' }
        ]
      } as any;
      fixture.detectChanges();
    });

    it('should correctly open retry dialog with resolved assessor', () => {
      const answer = component.selectedRunDetail!.answers[1];
      component.openRetryDialog('question', 42, answer);

      expect(component.retryScope).toBe('question');
      expect(component.retryRunId).toBe(42);
      expect(component.retryAnswer).toBe(answer);
      expect(component.retryAssessorConfigId).toBe(1);
    });

    it('should trigger rerunAnswer on confirmRetry when scope is question', () => {
      benchmarkServiceMock.rerunAnswer.mockReturnValue(of({ runId: 42 }));
      benchmarkServiceMock.getRun.mockReturnValue(of(component.selectedRunDetail!));

      const answer = component.selectedRunDetail!.answers[1];
      component.openRetryDialog('question', 42, answer);
      component.confirmRetry();

      expect(benchmarkServiceMock.rerunAnswer).toHaveBeenCalledWith(42, 102, 1);
      expect(component.rerunningAnswerId).toBe(102);
    });

    it('should trigger reassessAnswer on confirmRetry when scope is assessment', () => {
      benchmarkServiceMock.reassessAnswer.mockReturnValue(of({ runId: 42 }));
      benchmarkServiceMock.getRun.mockReturnValue(of(component.selectedRunDetail!));

      const answer = component.selectedRunDetail!.answers[1];
      component.openRetryDialog('assessment', 42, answer);
      component.confirmRetry();

      expect(benchmarkServiceMock.reassessAnswer).toHaveBeenCalledWith(42, 102, 1);
      expect(component.reassessingAnswerId).toBe(102);
    });

    it('should trigger rerunFinalSynthesis on confirmRetry when scope is synthesis', () => {
      benchmarkServiceMock.rerunFinalSynthesis.mockReturnValue(of({ runId: 42 }));
      benchmarkServiceMock.getRun.mockReturnValue(of(component.selectedRunDetail!));

      component.openRetryDialog('synthesis', 42);
      component.confirmRetry();

      expect(benchmarkServiceMock.rerunFinalSynthesis).toHaveBeenCalledWith(42, 1);
      expect(component.runningSynthesis).toBe(true);
    });

    it('should trigger retryFailedAssessments on confirmRetry when scope is assessments', () => {
      benchmarkServiceMock.retryFailedAssessments.mockReturnValue(of({ runId: 42 }));
      benchmarkServiceMock.getRun.mockReturnValue(of(component.selectedRunDetail!));

      component.openRetryDialog('assessments', 42);
      component.confirmRetry();

      expect(benchmarkServiceMock.retryFailedAssessments).toHaveBeenCalledWith(42, 1);
      expect(component.retryingAssessments).toBe(true);
    });
  });

  describe('downloadToolCallLog', () => {
    it('should open the tool-call log through window.open using the service URL', () => {
      benchmarkServiceMock.getToolCallLogUrl.mockReturnValue('/api/admin/benchmark/runs/42/tool-call-log');
      const openSpy = vi.spyOn(window, 'open').mockReturnValue(undefined as any);

      component.selectSubTab('history');
      fixture.detectChanges();
      ctx.historyTab().downloadToolCallLog(42);

      expect(benchmarkServiceMock.getToolCallLogUrl).toHaveBeenCalledWith(42);
      expect(openSpy).toHaveBeenCalledWith('/api/admin/benchmark/runs/42/tool-call-log', '_blank');
    });
  });
});
