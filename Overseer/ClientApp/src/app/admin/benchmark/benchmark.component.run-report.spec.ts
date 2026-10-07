import type { Mock, MockedObject } from "vitest";
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { of, throwError, Subject } from 'rxjs';
import {
  AdminBenchmarkComponent, RUN_REPORT_HEADER_STORAGE_KEY, RUN_REPORT_TAB_STORAGE_KEY
} from './benchmark.component';
import { AdminBenchmarkService } from '../../services/admin-benchmark.service';
import { IMAGE_DETAILS_STORAGE_KEY, KEY_FIGURES_STORAGE_KEY, keyFiguresImageIo } from './run-report-frame/key-figures-image';
import {
  KEY_FIGURES_EXPORT_STORAGE_KEY,
  defaultKeyFiguresExportSettings,
  readStoredKeyFiguresExportSettings,
  writeStoredKeyFiguresExportSettings
} from './run-report-frame/key-figures-export-settings';
import { AdminBenchmarkSpecContext, buildBatteryRun, clearStoredState, createAdminBenchmarkFixture } from './benchmark.component.testing';

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

  describe('run report dialog', () => {
    function reportAnswer(orderIndex: number, overrides: any = {}): any {
      return {
        id: 300 + orderIndex, benchmarkRunId: 55, orderIndex, questionText: `Question ${orderIndex}`,
        difficulty: 2, assessedDifficulty: 50, answerText: `Answer ${orderIndex}`, status: 'Ok',
        assessmentStatus: 'Scored', durationMs: 1000, modelTimeMs: 1000, toolCallCount: 1,
        scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 85,
        ...overrides
      };
    }

    /** Q1 has a critical error and scores 25, Q2 scores 60, Q3 carries a flag and scores 85. */
    function reportRun(overrides: any = {}): any {
      return {
        id: 55, benchmarkSuiteId: 1, suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Test Model', testedModelProviderUsed: 'OpenAI', testedModelIdUsed: 'gpt-test',
        testedModelParallelExecutionModeUsed: 0,
        assessorModelDisplayNameUsed: 'Test Assessor', assessorModelProviderUsed: 'Google', assessorModelIdUsed: 'gemini-test',
        startedByUserName: 'admin', status: 'Completed',
        startedAtUtc: '2026-09-03T06:52:00Z', completedAtUtc: '2026-09-03T07:10:00Z',
        totalAnswerDurationMs: 900000, totalDurationMs: 900000,
        scoringProfileName: 'Standard', scoringProfileId: 1, scoringMethodVersion: 12, harnessVersion: '40',
        transportDefectAnswerCount: 0, advisoryFlagAnswerCount: 0, scrubbedArtifactAnswerCount: 0,
        difficultyFallbackUsed: false, speedMeasurementDegraded: false, maxParallelQuestionsUsed: 1,
        answeredQuestionCount: 3, unansweredQuestionCount: 0, totalQuestionCount: 3,
        assessmentParseFailed: false, totalInputTokens: 0, totalOutputTokens: 0,
        totalCacheReadTokens: 0, totalCacheCreationTokens: 0, errorMessage: null,
        answers: [
          reportAnswer(1, { criticalError: true, qualityScore: 25 }),
          reportAnswer(2, { qualityScore: 60 }),
          reportAnswer(3, { answerFlagNames: ['RefutedClaim'] })
        ],
        ...overrides
      };
    }

    /** The four dimensional levels Re-score recomputes a score from. */
    const LEVELS = { accuracyLevel: 5, completenessLevel: 5, concisenessLevel: 5, readabilityLevel: 5 };

    function reportDialog(): HTMLDialogElement {
      return component.runDetailDialog.nativeElement;
    }

    function runActions(): HTMLElement {
      return fixture.nativeElement.querySelector('.benchmark-run-detail-dialog [role="group"][aria-label="Run actions"]') as HTMLElement;
    }

    /**
     * Resolves on the next event of a type, by which time the listeners Angular added first have run,
     * or after a second, so a missing event fails the expectations that follow rather than hanging.
     */
    function nextEvent(target: EventTarget, type: string): Promise<Event | null> {
      return new Promise(resolve => {
        const timer = setTimeout(() => resolve(null), 1000);
        target.addEventListener(type, event => {
          clearTimeout(timer);
          resolve(event);
        }, { once: true });
      });
    }

    function macrotask(): Promise<void> {
      return new Promise(resolve => setTimeout(resolve));
    }

    /** Opens the report the way Run History does, so the dialog is really modal. */
    function openReport(run: any): void {
      benchmarkServiceMock.getRun.mockReturnValue(of(run));
      component.viewRunDetail(run.id);
      fixture.detectChanges();
    }

    afterEach(async () => {
      const dialog = reportDialog();
      if (dialog.open) {
        const closed = nextEvent(dialog, 'close');
        dialog.close();
        await closed;
      }
      component.stopDetailPolling();
    });

    it('should hold the header actions in a named group, not a toolbar, and have no footer', () => {
      component.selectedRunDetail = reportRun({ hasBoardRecord: true });
      fixture.detectChanges();

      const dialog = fixture.nativeElement.querySelector('.benchmark-run-detail-dialog') as HTMLDialogElement;
      expect(dialog.classList.contains('gh-dialog-fullscreen')).toBe(true);
      expect(dialog.getAttribute('aria-labelledby')).toBe('runDetailTitle');
      expect(dialog.querySelector('#runDetailTitle')?.textContent?.trim()).toBe('Run #55: Default Suite');

      const group = runActions();
      expect(group).toBeTruthy();
      expect(dialog.querySelector('[role="toolbar"]')).toBeNull();

      const buttons = Array.from(group.querySelectorAll(':scope > button')) as HTMLButtonElement[];
      const names = buttons.map(b => b.getAttribute('aria-label') || (b.textContent || '').replace(/\s+/g, ' ').trim());
      expect(names).toEqual([
        'Downloads', 'Re-run', 'Repeat this run\'s setup', 'View game snapshot of run 55', 'Copy diagnostics of run 55'
      ]);
      for (const button of buttons) {
        expect(button.getAttribute('type')).toBe('button');
        expect(button.hasAttribute('title'), names[buttons.indexOf(button)]).toBe(false);
      }
      expect(buttons[0].classList.contains('btn-ghost')).toBe(true);
      expect(buttons[1].classList.contains('btn-ghost')).toBe(true);
      expect(buttons[1].getAttribute('popovertarget')).toBe('rr-rerun-popover');
      expect(buttons[1].getAttribute('aria-expanded')).toBe('false');
      expect(buttons[2].classList.contains('btn-ghost')).toBe(true);
      expect(buttons[3].classList.contains('action-btn')).toBe(true);
      expect(buttons[3].getAttribute('interestfor')).toBe('rr-snapshot-tip');
      expect(buttons[4].classList.contains('action-btn')).toBe(true);
      expect(buttons[4].getAttribute('interestfor')).toBe('rr-copy-diagnostics-tip');
      expect(getComputedStyle(group).display).toBe('grid');

      // Close is a dialog control, not a run action: it sits beside the group, not in it.
      const close = dialog.querySelector('.rr-header-controls > .rr-close') as HTMLButtonElement;
      expect(close).toBeTruthy();
      expect(close.classList.contains('btn-icon-action')).toBe(true);
      expect(close.getAttribute('aria-label')).toBe('Close run details');
      expect(close.getAttribute('type')).toBe('button');
      expect(close.hasAttribute('title')).toBe(false);
      expect(group.contains(close)).toBe(false);
      expect(group.parentElement?.classList.contains('rr-header-controls')).toBe(true);

      const popover = dialog.querySelector('#rr-rerun-popover') as HTMLElement;
      expect(popover.getAttribute('popover')).toBe('auto');
      expect(popover.getAttribute('role')).toBe('group');
      expect(popover.getAttribute('aria-label')).toBe('Re-run and repair');
      expect(popover.querySelector('[role="menu"], [role="menuitem"]')).toBeNull();

      // The report's own; the dialogs nested in its AI Reports tab keep their footers.
      const own = (selector: string) => Array.from(dialog.querySelectorAll(selector)).filter(el => el.closest('dialog') === dialog);
      expect(own('.modal-actions-bar')).toEqual([]);
      expect(own('.dialog-footer')).toEqual([]);
    });

    const HEADER_PROMPT_OPTIONS = JSON.stringify({ verboseMode: false, enableToolUse: true, hasGameSnapshot: true });
    const FULL_BOARD = [
      { role: 'assessor', delivered: 3, total: 3, missingQuestions: [] },
      { role: 'claim verifier', delivered: 3, total: 3, missingQuestions: [] }
    ];
    const BOARD_WITH_GAP = [
      { role: 'assessor', delivered: 3, total: 3, missingQuestions: [] },
      { role: 'second reader', delivered: 2, total: 3, missingQuestions: [2] }
    ];

    function runDetails(): HTMLDetailsElement {
      return reportDialog().querySelector('details#rr-run-details') as HTMLDetailsElement;
    }

    function factKeys(selector: string): (string | null)[] {
      return Array.from(reportDialog().querySelectorAll(`${selector} [data-fact]`)).map(fact => fact.getAttribute('data-fact'));
    }

    it('should show Model and Assessors always, and the other facts inside a closed Run details with its read-out', () => {
      openReport(reportRun({ candidatePromptOptionsJson: HEADER_PROMPT_OPTIONS, boardDelivery: FULL_BOARD }));

      expect(factKeys('.rr-identity app-run-facts.rr-run-facts-primary')).toEqual(['model', 'assessor']);
      const details = runDetails();
      expect(details.classList).toContain('gh-disclosure');
      expect(details.open).toBe(false);
      expect(factKeys('#rr-run-details app-run-facts')).toEqual(['prompt', 'profile', 'started', 'board']);
      // Both lists together keep the header's order, and the board note's tip is rendered once.
      expect(factKeys('.rr-identity')).toEqual(['model', 'assessor', 'prompt', 'profile', 'started', 'board']);
      expect(reportDialog().querySelectorAll('#rr-board-note-tip').length).toBe(1);

      const summary = details.querySelector(':scope > summary') as HTMLElement;
      expect(summary.querySelector('.rr-run-details-title')?.textContent?.trim()).toBe('Run details');
      expect(summary.querySelector('button, a, input, select, textarea')).toBeNull();
      const readout = summary.querySelector('.rr-run-details-readout') as HTMLElement;
      expect(readout.getAttribute('aria-hidden')).toBe('true');
      expect(readout.textContent?.trim()).toBe(component.runDetailsReadout);
      expect(component.runDetailsReadout).toMatch(/^Gameplay Help · Standard · Started \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} UTC · Board 3\/3$/);
      expect(getComputedStyle(readout).display).not.toBe('none');
      expect(summary.querySelector('.rr-run-details-gap')).toBeNull();
    });

    it('should remember Run details open, and restore it on the next report and the next visit', async () => {
      openReport(reportRun());
      const toggled = nextEvent(runDetails(), 'toggle');
      (runDetails().querySelector(':scope > summary') as HTMLElement).click();
      await toggled;
      fixture.detectChanges();

      expect(runDetails().open).toBe(true);
      expect(component.runHeaderDetailsOpen).toBe(true);
      expect(JSON.parse(localStorage.getItem(RUN_REPORT_HEADER_STORAGE_KEY)!)).toEqual({ version: 1, detailsOpen: true });
      expect(getComputedStyle(runDetails().querySelector('.rr-run-details-readout') as HTMLElement).display).toBe('none');

      const closed = nextEvent(reportDialog(), 'close');
      component.closeRunDetail();
      await closed;
      await macrotask();
      openReport(reportRun({ id: 56 }));
      expect(runDetails().open).toBe(true);

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      expect(restored.componentInstance.runHeaderDetailsOpen).toBe(true);
      restored.destroy();
    });

    it('should start Run details closed when the stored state is unreadable', () => {
      localStorage.setItem(RUN_REPORT_HEADER_STORAGE_KEY, '{not json');
      const unreadable = TestBed.createComponent(AdminBenchmarkComponent);
      expect(unreadable.componentInstance.runHeaderDetailsOpen).toBe(false);
      unreadable.destroy();

      localStorage.setItem(RUN_REPORT_HEADER_STORAGE_KEY, JSON.stringify({ version: 2, detailsOpen: true }));
      const unknown = TestBed.createComponent(AdminBenchmarkComponent);
      expect(unknown.componentInstance.runHeaderDetailsOpen).toBe(false);
      unknown.destroy();
    });

    it('should say Graded without the board in the Run details summary while it is closed', () => {
      openReport(reportRun({ boardDelivery: BOARD_WITH_GAP }));

      const details = runDetails();
      expect(details.open).toBe(false);
      const tag = details.querySelector(':scope > summary .rr-run-details-gap') as HTMLElement;
      expect(tag).toBeTruthy();
      expect(tag.classList).toContain('gh-tag');
      expect(tag.classList).toContain('gh-tag-changed');
      expect(tag.textContent?.trim()).toBe('Graded without the board');
      expect(tag.closest('[aria-hidden="true"]')).toBeNull();
      expect(getComputedStyle(tag).display).not.toBe('none');
      expect(tag.getBoundingClientRect().height).toBeGreaterThan(0);
      expect(component.runDetailsReadout).toContain('Board incomplete');
    });

    describe('header layout at 1200 px', () => {
      function openWide(run: any): HTMLElement {
        const dialog = reportDialog();
        dialog.style.width = '1200px';
        dialog.style.maxWidth = '1200px';
        openReport(run);
        return dialog.querySelector('.rrf-header') as HTMLElement;
      }

      afterEach(() => {
        reportDialog().style.removeProperty('width');
        reportDialog().style.removeProperty('max-width');
      });

      it('should keep the header within 190 px with Run details closed', async () => {
        const header = openWide(reportRun({
          isPanelRun: true, coAssessorModelDisplayNameUsed: 'Second Assessor', coAssessorModelProviderUsed: 'Anthropic',
          testedModelThinkingLevelUsed: 'high', candidatePromptOptionsJson: HEADER_PROMPT_OPTIONS, boardDelivery: FULL_BOARD,
          hasBoardRecord: true
        }));
        await document.fonts.ready;
        fixture.detectChanges();

        expect(runDetails().open).toBe(false);
        expect(header.getBoundingClientRect().width).toBeGreaterThan(1000);
        expect(header.getBoundingClientRect().height).toBeLessThanOrEqual(190);
      });

      it('should not stretch a primary fact beyond its tallest child', async () => {
        openWide(reportRun({
          isPanelRun: true, coAssessorModelDisplayNameUsed: 'Second Assessor', coAssessorModelProviderUsed: 'Anthropic',
          testedModelThinkingLevelUsed: 'high'
        }));
        await document.fonts.ready;
        fixture.detectChanges();

        const facts = Array.from(reportDialog().querySelectorAll('.rr-run-facts-primary .rr-fact')) as HTMLElement[];
        expect(facts.length).toBe(2);
        for (const fact of facts) {
          const height = fact.getBoundingClientRect().height;
          const tallest = Math.max(...Array.from(fact.children).map(child => child.getBoundingClientRect().height));
          expect(height, fact.getAttribute('data-fact')!).toBeGreaterThan(0);
          expect(height, fact.getAttribute('data-fact')!).toBeLessThanOrEqual(tallest + 2);
        }
      });

      it('should lay the open Run details out as a fact sheet, not one word per line', async () => {
        openWide(reportRun({ candidatePromptOptionsJson: HEADER_PROMPT_OPTIONS, boardDelivery: FULL_BOARD, hasBoardRecord: true }));
        const toggled = nextEvent(runDetails(), 'toggle');
        (runDetails().querySelector(':scope > summary') as HTMLElement).click();
        await toggled;
        await document.fonts.ready;
        fixture.detectChanges();

        const details = runDetails();
        expect(details.open).toBe(true);
        const primary = reportDialog().querySelector('.rr-run-facts-primary') as HTMLElement;
        expect(Math.abs(details.getBoundingClientRect().width - primary.getBoundingClientRect().width)).toBeLessThanOrEqual(1);
        expect(details.querySelector('app-run-facts')?.classList).toContain('rr-facts-stacked');

        const fact = (key: string) => (details.querySelector(`[data-fact="${key}"]`) as HTMLElement).getBoundingClientRect();
        expect(Math.abs(fact('profile').top - fact('started').top)).toBeLessThanOrEqual(1);
        expect(fact('board').top).toBeGreaterThanOrEqual(fact('profile').top - 1);
        expect(fact('profile').height).toBeLessThan(60);
      });
    });

    it('should open the Re-run popover on its first enabled item, with aria-expanded from its toggle event', async () => {
      openReport(reportRun());
      const trigger = fixture.nativeElement.querySelector('#rr-rerun-trigger') as HTMLButtonElement;
      const popover = fixture.nativeElement.querySelector('#rr-rerun-popover') as HTMLElement;

      const opened = nextEvent(popover, 'toggle');
      trigger.click();
      await opened;
      fixture.detectChanges();

      expect(popover.matches(':popover-open')).toBe(true);
      expect(trigger.getAttribute('aria-expanded')).toBe('true');
      expect(document.activeElement).toBe(popover.querySelector('[data-action="synthesis"]'));
    });

    it('should close only the popover on Escape and return focus to its trigger', async () => {
      openReport(reportRun());
      const trigger = fixture.nativeElement.querySelector('#rr-rerun-trigger') as HTMLButtonElement;
      const popover = fixture.nativeElement.querySelector('#rr-rerun-popover') as HTMLElement;
      const opened = nextEvent(popover, 'toggle');
      trigger.click();
      await opened;
      fixture.detectChanges();

      const dialogKeydowns: Event[] = [];
      reportDialog().addEventListener('keydown', e => dialogKeydowns.push(e));
      const closed = nextEvent(popover, 'toggle');
      const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      (document.activeElement as HTMLElement).dispatchEvent(escape);
      await closed;
      fixture.detectChanges();

      expect(escape.defaultPrevented).toBe(true);
      expect(dialogKeydowns.length).toBe(0);
      expect(popover.matches(':popover-open')).toBe(false);
      expect(reportDialog().open).toBe(true);
      expect(trigger.getAttribute('aria-expanded')).toBe('false');
      expect(document.activeElement).toBe(trigger);
    });

    it('should keep an applicable Re-run action listed with its reason while it cannot run, and refuse it', () => {
      component.selectedRunDetail = reportRun({
        status: 'Failed',
        isAborted: true,
        answers: [reportAnswer(1, { status: 'Failed', errorMessage: 'boom', qualityScore: null })]
      });
      fixture.detectChanges();
      const rescore = vi.spyOn(component, 'rescoreRun').mockReturnValue(undefined);
      const rerunFailed = vi.spyOn(component, 'rerunFailedFromRunDetail').mockReturnValue(undefined);

      const items = Array.from(fixture.nativeElement.querySelectorAll('#rr-rerun-popover .gh-action-popover-item')) as HTMLButtonElement[];
      const item = (key: string) => items.find(i => i.getAttribute('data-action') === key)!;
      for (const key of ['failed-questions', 'synthesis', 'rescore']) {
        expect(item(key), key).toBeTruthy();
        expect(item(key).getAttribute('aria-disabled'), key).toBe('true');
        expect(item(key).disabled, key).toBe(false);
        expect(item(key).querySelector('.gh-action-popover-item-reason')?.textContent, key).toContain('The run stopped before finishing its suite.');
      }

      item('rescore').click();
      item('failed-questions').click();
      expect(rescore).not.toHaveBeenCalled();
      expect(rerunFailed).not.toHaveBeenCalled();
    });

    it('should give every Re-run action the busy reason while a retry is running', () => {
      component.selectedRunDetail = reportRun({ status: 'Running' });
      fixture.detectChanges();
      const reasons = Array.from(fixture.nativeElement.querySelectorAll('#rr-rerun-popover .gh-action-popover-item'))
        .map((i: any) => ({ key: i.getAttribute('data-action'), reason: i.querySelector('.gh-action-popover-item-reason')?.textContent?.trim() }));
      expect(reasons).toEqual([
        { key: 'synthesis', reason: 'A retry is already running on this run.' },
        { key: 'rescore', reason: 'A retry is already running on this run.' }
      ]);
    });

    it('should list every applicable repair in order, in sentence case', () => {
      component.selectedRunDetail = reportRun({
        status: 'CompletedWithErrors',
        answers: [
          reportAnswer(1, { ...LEVELS }),
          reportAnswer(2, { status: 'ProviderError', assessmentStatus: 'Failed', qualityScore: null }),
          reportAnswer(3, { ...LEVELS, claimVerificationError: 'Verifier timed out' })
        ]
      });
      fixture.detectChanges();

      const items = Array.from(fixture.nativeElement.querySelectorAll('#rr-rerun-popover .gh-action-popover-item')) as HTMLButtonElement[];
      expect(items.map(i => i.getAttribute('data-action'))).toEqual(['failed-questions', 'assessments', 'claim-verification', 'synthesis', 'rescore']);
      expect(items.map(i => i.querySelector('span')?.textContent?.trim())).toEqual([
        'Re-run failed questions', 'Retry failed assessments', 'Retry claim verification', 'Re-run final synthesis', 'Re-score run'
      ]);
      for (const i of items) {
        expect(i.hasAttribute('aria-disabled'), i.getAttribute('data-action')!).toBe(false);
      }
    });

    it('should refuse every repair but Re-score on a run scored under an older scoring method', () => {
      component.selectedRunDetail = reportRun({
        status: 'CompletedWithErrors',
        isCurrentScoringMethod: false,
        answers: [
          reportAnswer(1, { ...LEVELS }),
          reportAnswer(2, { status: 'ProviderError', assessmentStatus: 'Failed', qualityScore: null })
        ]
      });
      fixture.detectChanges();

      const reasons = Array.from(fixture.nativeElement.querySelectorAll('#rr-rerun-popover .gh-action-popover-item'))
        .map((i: any) => ({ key: i.getAttribute('data-action'), reason: i.querySelector('.gh-action-popover-item-reason')?.textContent?.trim() ?? null }));
      const older = 'Scored under an older scoring method; re-score it first.';
      expect(reasons).toEqual([
        { key: 'failed-questions', reason: older },
        { key: 'assessments', reason: older },
        { key: 'synthesis', reason: older },
        { key: 'rescore', reason: null }
      ]);
    });

    it('should say why Re-score is unavailable on a run with no dimensional level ratings', () => {
      component.selectedRunDetail = reportRun();
      fixture.detectChanges();
      const rescore = fixture.nativeElement.querySelector('#rr-rerun-popover [data-action="rescore"]') as HTMLButtonElement;
      expect(rescore.getAttribute('aria-disabled')).toBe('true');
      expect(rescore.querySelector('.gh-action-popover-item-reason')?.textContent?.trim())
        .toBe('The run has no dimensional level ratings to re-score.');
    });

    it('should re-score under the run\'s own profile, sending no profile id', () => {
      const run = reportRun({ answers: [reportAnswer(1, { ...LEVELS })] });
      openReport(run);
      ctx.launcher.selectedScoringProfileId = 1;
      benchmarkServiceMock.rescoreRun.mockReturnValue(of(undefined));

      (fixture.nativeElement.querySelector('#rr-rerun-popover [data-action="rescore"]') as HTMLButtonElement).click();

      expect(benchmarkServiceMock.rescoreRun).toHaveBeenCalledTimes(1);
      expect(vi.mocked(benchmarkServiceMock.rescoreRun).mock.calls[0]).toEqual([55]);
      expect(component.rescoringRun).toBe(false);
    });

    it('should tell the Download Center the run\'s battery, and switch it to that battery run\'s downloads on request', () => {
      ctx.workspace.historyRuns = [{ id: 55, batteryRunId: 9 } as any];
      ctx.workspace.batteryRuns = [buildBatteryRun({ id: 9, testedModelLabel: 'Gemini Flash' } as any)];
      openReport(reportRun());
      const center = component.runDownloadCenter!;
      const open = vi.spyOn(center, 'open').mockReturnValue(undefined);

      (fixture.nativeElement.querySelector('#rr-downloads-trigger') as HTMLButtonElement).click();
      expect(open).toHaveBeenCalledTimes(1);
      const context = open.mock.calls[0][0] as any;
      expect(context.kind).toBe('run');
      expect(context.run.id).toBe(55);
      expect(context.run.batteryRunId).toBe(9);

      center.openBatteryDownloads.emit(9);
      expect(open).toHaveBeenCalledTimes(2);
      expect(open.mock.calls[1][0]).toEqual({
        kind: 'battery', batteryRunId: 9, label: 'Core Battery · Gemini Flash', memberDiagnosticsText: component.memberDiagnosticsText
      });
    });

    it('should give battery Download Centers a member run\'s own diagnostics text, and hand it to the Battery Run Report', () => {
      const run = reportRun();
      const diagnostics = vi.spyOn(component, 'runDiagnosticsTextFor').mockReturnValue('member diagnostics');
      expect(component.memberDiagnosticsText(run)).toBe('member diagnostics');
      expect(diagnostics).toHaveBeenCalledWith(run, component.runStageOf(run));

      fixture.detectChanges();
      expect(component.batteryRunReport!.memberDiagnosticsText).toBe(component.memberDiagnosticsText);
    });

    it('should tell the Download Center no battery for a run outside one', () => {
      ctx.workspace.historyRuns = [];
      openReport(reportRun());
      const open = vi.spyOn(component.runDownloadCenter!, 'open').mockReturnValue(undefined);
      (fixture.nativeElement.querySelector('#rr-downloads-trigger') as HTMLButtonElement).click();
      expect((open.mock.calls[0][0] as any).run.batteryRunId).toBeNull();
    });

    it('should keep the Summary alert\'s one Re-run failed questions, aria-disabled with its reason while busy', () => {
      const failing = (overrides: any = {}) => reportRun({
        status: 'CompletedWithErrors',
        answers: [reportAnswer(1), reportAnswer(2, { status: 'ProviderError', errorMessage: 'boom', qualityScore: null })],
        ...overrides
      });
      component.selectedRunDetail = failing();
      fixture.detectChanges();
      const rerunFailed = vi.spyOn(component, 'rerunFailedFromRunDetail').mockReturnValue(undefined);

      const button = () => fixture.nativeElement.querySelector('#rr-panel-summary .alert-actions button') as HTMLButtonElement;
      expect(fixture.nativeElement.querySelectorAll('#rr-panel-summary .alert-actions button').length).toBe(1);
      expect(button().textContent?.replace(/\s+/g, ' ').trim()).toBe('Re-run failed questions');
      expect(button().hasAttribute('disabled')).toBe(false);
      expect(button().hasAttribute('aria-disabled')).toBe(false);
      button().click();
      expect(rerunFailed).toHaveBeenCalledWith(55);

      rerunFailed.mockClear();
      component.selectedRunDetail = failing({ isCurrentScoringMethod: false });
      ctx.refresh();
      expect(button().getAttribute('aria-disabled')).toBe('true');
      expect(button().disabled).toBe(false);
      const reason = fixture.nativeElement.querySelector('#' + button().getAttribute('aria-describedby')) as HTMLElement;
      expect(reason.textContent?.trim()).toBe('Scored under an older scoring method; re-score it first.');
      button().click();
      expect(rerunFailed).not.toHaveBeenCalled();
    });

    it('should say Run in progress for a first execution, and Retry in progress only for a re-run', () => {
      const strip = () => (fixture.nativeElement.querySelector('.retry-progress-strip > span') as HTMLElement).textContent?.trim();
      component.selectedRunDetail = reportRun({ status: 'Running' });
      fixture.detectChanges();
      expect(strip()).toBe('Run in progress.');

      component.selectedRunDetail = reportRun({ status: 'Running', rerunStartedAtUtc: '2026-09-03T08:00:00Z' });
      ctx.refresh();
      expect(strip()).toBe('Retry in progress on this run…');
    });

    it('should run an available Re-run action', () => {
      component.selectedRunDetail = reportRun({ answers: [reportAnswer(1, { ...LEVELS })] });
      fixture.detectChanges();
      const rescore = vi.spyOn(component, 'rescoreRun').mockReturnValue(undefined);
      const available = fixture.nativeElement.querySelector('#rr-rerun-popover [data-action="rescore"]') as HTMLButtonElement;
      expect(available.hasAttribute('aria-disabled')).toBe(false);
      available.click();
      expect(rescore).toHaveBeenCalledTimes(1);
      expect(rescore).toHaveBeenCalledWith(55);
    });

    it('should filter the questions by any pressed filter, without Members disagree on a single-assessor run', () => {
      component.selectedRunDetail = reportRun();
      fixture.detectChanges();

      const toggles = () => Array.from(fixture.nativeElement.querySelectorAll('.questions-detail-section .gh-filter-toggle')) as HTMLButtonElement[];
      const toggle = (key: string) => toggles().find(t => t.getAttribute('data-filter') === key)!;
      const shown = () => Array.from(fixture.nativeElement.querySelectorAll('.question-detail-card .q-number'))
        .map((e: any) => e.textContent.trim());
      const status = () => (fixture.nativeElement.querySelector('.questions-detail-section [role="status"]').textContent || '')
        .replace(/\s+/g, ' ').trim();

      expect(toggles().map(t => t.getAttribute('data-filter'))).toEqual(['critical', 'disputed', 'below70', 'flagged']);
      expect(toggle('critical').textContent?.trim()).toBe('Critical errors (1)');
      expect(toggle('below70').textContent?.trim()).toBe('Below 70 (2)');
      expect(toggle('critical').getAttribute('aria-pressed')).toBe('false');
      expect(shown()).toEqual(['Q1', 'Q2', 'Q3']);
      expect(status()).toBe('');

      toggle('critical').click();
      fixture.detectChanges();
      expect(toggle('critical').getAttribute('aria-pressed')).toBe('true');
      expect(shown()).toEqual(['Q1']);

      toggle('flagged').click();
      fixture.detectChanges();
      expect(shown()).toEqual(['Q1', 'Q3']);
      expect(status()).toContain('Showing 2 of 3 questions');

      toggle('critical').click();
      toggle('flagged').click();
      toggle('disputed').click();
      fixture.detectChanges();
      expect(shown()).toEqual([]);
      const clear = fixture.nativeElement.querySelector('.questions-detail-section .gh-filter-clear') as HTMLButtonElement;
      expect(clear.textContent?.trim()).toBe('Clear filters');
      clear.click();
      fixture.detectChanges();
      expect(shown()).toEqual(['Q1', 'Q2', 'Q3']);
    });

    it('should offer Members disagree on a panel run, and read Below 70 from the panel score', () => {
      const toggles = () => Array.from(fixture.nativeElement.querySelectorAll('.questions-detail-section .gh-filter-toggle')) as HTMLButtonElement[];
      const toggle = (key: string) => toggles().find(t => t.getAttribute('data-filter') === key)!;
      const shown = () => Array.from(fixture.nativeElement.querySelectorAll('.question-detail-card .q-number'))
        .map((e: any) => e.textContent.trim());

      component.selectedRunDetail = reportRun({
        isPanelRun: true,
        answers: [
          reportAnswer(1, { panelDisagreed: true, panelQualityScore: 65, qualityScore: 80 }),
          reportAnswer(2, { panelQualityScore: 90, qualityScore: 40 })
        ]
      });
      fixture.detectChanges();
      expect(toggles().map(t => t.getAttribute('data-filter'))).toEqual(['critical', 'disputed', 'disagree', 'below70', 'flagged']);
      toggle('below70').click();
      fixture.detectChanges();
      expect(shown()).toEqual(['Q1']);
      toggle('below70').click();
      toggle('disagree').click();
      fixture.detectChanges();
      expect(shown()).toEqual(['Q1']);
    });

    it('should count member B\'s critical errors in the Critical errors filter on a panel run', () => {
      const toggle = (key: string) => (Array.from(fixture.nativeElement.querySelectorAll('.questions-detail-section .gh-filter-toggle')) as HTMLButtonElement[])
        .find(t => t.getAttribute('data-filter') === key)!;
      const shown = () => Array.from(fixture.nativeElement.querySelectorAll('.question-detail-card .q-number'))
        .map((e: any) => e.textContent.trim());

      component.selectedRunDetail = reportRun({
        isPanelRun: true,
        answers: [
          reportAnswer(1, { criticalError: true, qualityScore: 25 }),
          reportAnswer(2, { coAssessmentCriticalError: true, qualityScore: 80 }),
          reportAnswer(3)
        ]
      });
      fixture.detectChanges();

      expect(toggle('critical').textContent?.trim()).toBe('Critical errors (2)');
      toggle('critical').click();
      fixture.detectChanges();
      expect(shown()).toEqual(['Q1', 'Q2']);
    });

    it('should ignore member B\'s critical error in the filter outside a panel run', () => {
      const toggle = (key: string) => (Array.from(fixture.nativeElement.querySelectorAll('.questions-detail-section .gh-filter-toggle')) as HTMLButtonElement[])
        .find(t => t.getAttribute('data-filter') === key)!;

      component.selectedRunDetail = reportRun({
        answers: [reportAnswer(1), reportAnswer(2, { coAssessmentCriticalError: true })]
      });
      fixture.detectChanges();

      expect(toggle('critical').textContent?.trim()).toBe('Critical errors (0)');
    });

    it('should expand and collapse every shown question from real header buttons', () => {
      component.selectedRunDetail = reportRun();
      fixture.detectChanges();

      const headers = () => Array.from(fixture.nativeElement.querySelectorAll('.question-card-header')) as HTMLButtonElement[];
      expect(headers().length).toBe(3);
      for (const header of headers()) {
        expect(header.tagName).toBe('BUTTON');
        expect(header.getAttribute('type')).toBe('button');
        expect(header.getAttribute('aria-expanded')).toBe('false');
        expect(header.querySelector('button, a, input, select, textarea')).toBeNull();
      }

      (fixture.nativeElement.querySelector('#rr-expand-all') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(headers().map(h => h.getAttribute('aria-expanded'))).toEqual(['true', 'true', 'true']);
      for (const header of headers()) {
        const body = fixture.nativeElement.querySelector('#' + header.getAttribute('aria-controls'));
        expect(body?.classList.contains('question-card-body')).toBe(true);
      }

      (fixture.nativeElement.querySelector('#rr-collapse-all') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(headers().map(h => h.getAttribute('aria-expanded'))).toEqual(['false', 'false', 'false']);
      expect(fixture.nativeElement.querySelectorAll('.question-card-body').length).toBe(0);
    });

    it('should make the per-question actions ghost buttons, the trial named by a tooltip rather than a title', () => {
      component.selectedRunDetail = reportRun();
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('.question-card-header') as HTMLButtonElement).click();
      fixture.detectChanges();

      const actions = Array.from(fixture.nativeElement.querySelectorAll('.question-card-body .question-actions > button')) as HTMLButtonElement[];
      expect(actions.map(b => (b.textContent || '').replace(/\s+/g, ' ').trim()))
        .toEqual(['Re-run question', 'Re-assess question', 'Try another assessor (does not change the score)']);
      for (const action of actions) {
        expect(action.classList.contains('btn-ghost')).toBe(true);
        expect(action.classList.contains('btn-gh')).toBe(false);
        expect(action.hasAttribute('aria-disabled')).toBe(false);
      }
      const trial = actions[2];
      expect(trial.className.trim()).toBe('btn-ghost');
      expect(trial.querySelector('svg.btn-icon')).toBeTruthy();
      expect(trial.hasAttribute('title')).toBe(false);
      const tip = fixture.nativeElement.querySelector('#' + trial.getAttribute('interestfor')) as HTMLElement;
      expect(tip.getAttribute('popover')).toBe('hint');
      expect(tip.textContent).toContain('Changes no score, level, flag or index.');
    });

    it('should keep Re-assess question one label whether or not the assessment failed', () => {
      component.selectedRunDetail = reportRun({ answers: [reportAnswer(1, { assessmentStatus: 'Failed', qualityScore: null })] });
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('.question-card-header') as HTMLButtonElement).click();
      fixture.detectChanges();

      const reassess = fixture.nativeElement.querySelector('.question-actions [data-repair="reassess"]') as HTMLButtonElement;
      expect(reassess.textContent?.replace(/\s+/g, ' ').trim()).toBe('Re-assess question');
    });

    it('should show the per-question actions aria-disabled with their reason on an aborted run, and refuse them', () => {
      component.selectedRunDetail = reportRun({ status: 'Canceled', isAborted: true });
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('.question-card-header') as HTMLButtonElement).click();
      fixture.detectChanges();
      const open = vi.spyOn(component, 'openRetryDialog').mockReturnValue(undefined);

      const actions = Array.from(fixture.nativeElement.querySelectorAll('.question-card-body .question-actions > button')) as HTMLButtonElement[];
      expect(actions.length).toBe(3);
      const reasons = Array.from(fixture.nativeElement.querySelectorAll('.question-card-body .question-repair-reason')) as HTMLElement[];
      expect(reasons.map(r => r.textContent?.trim())).toEqual(['The run stopped before finishing its suite.']);
      for (const action of actions) {
        expect(action.getAttribute('aria-disabled')).toBe('true');
        expect(action.disabled).toBe(false);
        expect(action.getAttribute('aria-describedby')).toBe(reasons[0].id);
        action.click();
      }
      expect(open).not.toHaveBeenCalled();
    });

    it('should title the trial scope of the retry dialog, name its confirm by its label, and label the dialog by its heading', () => {
      component.selectedRunDetail = reportRun();
      fixture.detectChanges();
      vi.spyOn(component.retryDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      component.openRetryDialog('trial', 55, component.selectedRunDetail!.answers[0]);
      ctx.refresh();

      const dialog = fixture.nativeElement.querySelector('.retry-dialog') as HTMLDialogElement;
      const heading = dialog.querySelector('#' + dialog.getAttribute('aria-labelledby')) as HTMLElement;
      expect(heading.tagName).toBe('H3');
      expect(heading.textContent?.trim()).toBe('Try another assessor');
      expect(dialog.querySelector('.dialog-body p')?.textContent?.replace(/\s+/g, ' ').trim())
        .toBe('Grades this answer with the model you choose and shows the verdict beside the panel\'s. The score does not change.');
      const confirm = dialog.querySelector('.dialog-footer .btn-gh:not(.btn-gh-cancel)') as HTMLButtonElement;
      expect(confirm.textContent?.trim()).toBe('Try another assessor');
      expect(confirm.hasAttribute('aria-label')).toBe(false);

      component.openRetryDialog('synthesis', 55);
      ctx.refresh();
      expect(heading.textContent?.trim()).toBe('Re-run final synthesis');
      expect(confirm.textContent?.trim()).toBe('Re-run final synthesis');
      component.closeRetryDialog();
    });

    it('should clean up exactly once when the header Close closes the report, stopping detail polling', async () => {
      openReport(reportRun());
      component.startDetailPolling(55);
      expect(component.detailPollInterval).not.toBeNull();
      const cleanup = vi.spyOn(component, 'onRunDetailClosed');

      const closed = nextEvent(reportDialog(), 'close');
      (reportDialog().querySelector('button.rr-close[aria-label="Close run details"]') as HTMLButtonElement).click();
      await closed;
      await macrotask();

      expect(cleanup).toHaveBeenCalledTimes(1);
      expect(reportDialog().open).toBe(false);
      expect(component.detailPollInterval).toBeNull();
      expect(component.selectedRunDetail).toBeNull();
      expect(component.runDetailRequestedId).toBeNull();
    });

    it('should clean up exactly once when Escape closes the report, stopping detail polling', async () => {
      openReport(reportRun());
      component.startDetailPolling(55);
      const cleanup = vi.spyOn(component, 'onRunDetailClosed');
      const dialog = reportDialog() as HTMLDialogElement & {
        requestClose?: () => void;
      };

      const closed = nextEvent(dialog, 'close');
      // What Escape sends: a close request, which is a cancel event and then the close.
      if (typeof dialog.requestClose === 'function') {
        dialog.requestClose();
      } else {
        dialog.dispatchEvent(new Event('cancel', { cancelable: true }));
        dialog.close();
      }
      await closed;
      await macrotask();

      expect(cleanup).toHaveBeenCalledTimes(1);
      expect(component.detailPollInterval).toBeNull();
      expect(component.selectedRunDetail).toBeNull();
    });

    it('should clean up exactly once when code closes the report, whether or not it is open', async () => {
      openReport(reportRun());
      const cleanup = vi.spyOn(component, 'onRunDetailClosed');

      const closed = nextEvent(reportDialog(), 'close');
      component.closeRunDetail();
      expect(cleanup).not.toHaveBeenCalled();
      await closed;
      await macrotask();
      expect(cleanup).toHaveBeenCalledTimes(1);

      cleanup.mockClear();
      component.selectedRunDetail = reportRun();
      component.closeRunDetail();
      expect(cleanup).toHaveBeenCalledTimes(1);
      expect(component.selectedRunDetail).toBeNull();
    });

    it('should keep a run reopened before the previous close event arrived', async () => {
      openReport(reportRun());
      const closed = nextEvent(reportDialog(), 'close');
      component.closeRunDetail();
      openReport(reportRun({ id: 56 }));
      await closed;
      await macrotask();

      expect(reportDialog().open).toBe(true);
      expect(component.selectedRunDetail?.id).toBe(56);
    });

    it('should show the header with Close and a skeleton while the run loads', () => {
      const pending = new Subject<any>();
      benchmarkServiceMock.getRun.mockReturnValue(pending.asObservable());
      component.viewRunDetail(77);
      fixture.detectChanges();

      const dialog = fixture.nativeElement.querySelector('.benchmark-run-detail-dialog') as HTMLElement;
      expect(dialog.querySelector('#runDetailTitle')?.textContent?.trim()).toBe('Run #77');
      expect(dialog.querySelector('.rr-header-controls > button.rr-close[aria-label="Close run details"]')).toBeTruthy();
      expect(dialog.querySelector('#rr-downloads-trigger')).toBeNull();
      expect(dialog.querySelector('[role="group"][aria-label="Run actions"]')).toBeNull();
      expect(dialog.querySelector('#rr-run-details')).toBeNull();
      expect(dialog.querySelectorAll('.rr-skeleton').length).toBeGreaterThan(0);
      expect(dialog.querySelector('.rrf-body')?.getAttribute('aria-busy')).toBe('true');

      pending.next(reportRun({ id: 77 }));
      fixture.detectChanges();
      expect(dialog.querySelector('#runDetailTitle')?.textContent?.trim()).toBe('Run #77: Default Suite');
      expect(dialog.querySelectorAll('.rr-skeleton').length).toBe(0);
      expect(dialog.querySelector('.rrf-body')?.hasAttribute('aria-busy')).toBe(false);
    });

    it('should show a load failure with Close and Try again', () => {
      vi.spyOn(console, 'error').mockReturnValue(undefined);
      benchmarkServiceMock.getRun.mockReturnValue(throwError(() => ({ status: 500, error: 'Database unavailable' })));
      component.viewRunDetail(77);
      fixture.detectChanges();

      const dialog = fixture.nativeElement.querySelector('.benchmark-run-detail-dialog') as HTMLElement;
      const alert = dialog.querySelector('[role="alert"]') as HTMLElement;
      expect(alert.textContent).toContain('Run #77 could not be loaded: Database unavailable');
      expect(dialog.querySelector('button[aria-label="Close run details"]')).toBeTruthy();
      expect(dialog.querySelector('.rr-skeleton')).toBeNull();

      benchmarkServiceMock.getRun.mockClear();
      benchmarkServiceMock.getRun.mockReturnValue(of(reportRun({ id: 77 })));
      (dialog.querySelector('#rr-retry-load') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(benchmarkServiceMock.getRun).toHaveBeenCalledTimes(1);

      expect(benchmarkServiceMock.getRun).toHaveBeenCalledWith(77);
      expect(component.selectedRunDetail?.id).toBe(77);
      expect(component.runDetailLoadError).toBeNull();
      expect(dialog.querySelector('#rr-retry-load')).toBeNull();
    });

    it('should open the Download Center on the viewed run from Downloads', () => {
      component.selectedRunDetail = reportRun();
      fixture.detectChanges();
      const open = vi.spyOn(component.runDownloadCenter!, 'open').mockReturnValue(undefined);

      (fixture.nativeElement.querySelector('#rr-downloads-trigger') as HTMLButtonElement).click();

      expect(open).toHaveBeenCalledTimes(1);
      const context = vi.mocked(open).mock.lastCall![0] as any;
      expect(context.kind).toBe('run');
      expect(context.run).toEqual({
        id: 55, suiteName: 'Default Suite', modelLabel: 'Test Model',
        startedAtUtc: '2026-09-03T06:52:00Z', completedAtUtc: '2026-09-03T07:10:00Z', batteryRunId: null
      });
      const text = context.diagnosticsText() as string;
      expect(text).toContain('Run ID: 55');
      expect(text).toContain('Answered 3 of 3');
    });

    it('the AI Reports notice\'s Open Download Center opens the Download Center and returns focus to that button', () => {
      benchmarkServiceMock.listReportDocuments.mockReturnValue(of([{
        id: 71, packId: 'run-55', audience: 1, title: 'Document 71', subjectKey: 'run:55', subjectLabel: 'Test Model',
        subjectRunIds: [55], suiteId: 1, suiteName: 'Default Suite', writerDisplayName: 'Test Model',
        writerProvider: 'Anthropic', writerModelId: 'claude-3-5-sonnet', writerThinkingLevel: null,
        sameProviderAcknowledged: false, status: 'Completed', reportFormatVersion: 2,
        createdAtUtc: '2026-09-28T10:15:00Z', inputTokens: 0, outputTokens: 0, durationMs: 0, costUsd: null,
        runChangedSinceGeneration: false, missingRunIds: [], allowedDisclosures: [1, 2, 3], origin: 2
      }]));
      openReport(reportRun({ assessmentJson: '{}' }));
      component.selectRunReportTab('reports');
      fixture.detectChanges();
      const open = vi.spyOn(component.runDownloadCenter!, 'open').mockReturnValue(undefined);

      const button = fixture.nativeElement.querySelector('#rr-panel-reports .rr-ai-download-notice .rr-ai-open-downloads') as HTMLButtonElement;
      expect(button.textContent?.replace(/\s+/g, ' ').trim()).toBe('Open Download Center');
      button.click();

      expect(open).toHaveBeenCalledTimes(1);
      expect((vi.mocked(open).mock.lastCall![0] as any).run.id).toBe(55);

      // The Download Center closed and focus fell to the page: it goes back to the notice's button.
      (document.activeElement as HTMLElement | null)?.blur();
      component.onRunDownloadsClosed();
      expect(document.activeElement).toBe(button);
    });

    it('should copy the viewed run diagnostics from Copy diagnostics and announce it', fakeAsync(() => {
      const run = reportRun();
      component.selectedRunDetail = run;
      fixture.detectChanges();
      const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
      const expected = component.runDiagnosticsTextFor(run, component.runStageOf(run));
      expect(expected).toContain('Run ID: 55');

      (runActions().querySelector('button[aria-label="Copy diagnostics of run 55"]') as HTMLButtonElement).click();
      tick();
      fixture.detectChanges();

      expect(writeText).toHaveBeenCalledTimes(1);

      expect(writeText).toHaveBeenCalledWith(expected);
      const status = runActions().querySelector('.rr-status[role="status"]') as HTMLElement;
      expect(status.textContent?.trim()).toBe('Diagnostics copied to the clipboard.');

      tick(3000);
      fixture.detectChanges();
      expect(status.textContent?.trim()).toBe('');
    }));

    it('should list the run configuration and the tool routing table in their tabs', () => {
      component.selectedRunDetail = reportRun({
        secondOpinionAssessorModelConfigurationId: 3, secondOpinionAssessorModelDisplayNameUsed: 'Reader',
        secondOpinionAssessorModelProviderUsed: 'OpenAI', secondOpinionAssessorModelIdUsed: 'gpt-reader',
        secondOpinionModeUsed: 1, candidateSystemPromptSha256: 'abc123',
        answers: [
          reportAnswer(1, { toolCallSummary: 'source_code_search×3, wiki_search×1', qualityScore: 70, modelTimeMs: 3000 }),
          reportAnswer(2, { toolCallSummary: 'wiki_view×2', qualityScore: 90, modelTimeMs: 1000 })
        ]
      });
      fixture.detectChanges();

      const configuration = fixture.nativeElement.querySelector('.benchmark-run-detail-dialog #rr-panel-configuration') as HTMLElement;
      const tools = fixture.nativeElement.querySelector('.benchmark-run-detail-dialog #rr-panel-tools') as HTMLElement;
      const terms = Array.from(configuration.querySelectorAll('.rr-config dt')).map((dt: any) => dt.textContent.trim());
      const value = (term: string) => (configuration.querySelectorAll('.rr-config dd')[terms.indexOf(term)]?.textContent || '')
        .replace(/\s+/g, ' ').trim();
      expect(value('Assessor')).toContain('Test Assessor (Google / gemini-test)');
      expect(value('Assessor')).toContain('different family from the model under test');
      expect(value('Second reader')).toContain('same family as the model under test');
      expect(value('Claim verifier')).toBe('None');
      expect(value('Harness version')).toBe('40');
      expect(value('Prompt SHA-256')).toBe('abc123');

      const routing = tools.querySelector('.tool-routing-table') as HTMLTableElement;
      expect(routing).toBeTruthy();
      const rows = Array.from(routing.querySelectorAll('tbody tr')).map((tr: any) =>
        Array.from(tr.querySelectorAll('td')).map((td: any) => td.textContent.trim()));
      expect(rows).toEqual([['Source Code', '3', '50 %'], ['Wiki', '3', '50 %']]);
      expect(tools.textContent).toContain('(n = 2)');
    });

    it('should right-align the Tools counts', () => {
      component.selectedRunDetail = reportRun({
        answers: [
          reportAnswer(1, { toolCallSummary: 'source_code_search×3, wiki_search×1' }),
          reportAnswer(2, { toolCallSummary: 'wiki_view×2' })
        ]
      });
      fixture.detectChanges();

      const tools = fixture.nativeElement.querySelector('.benchmark-run-detail-dialog #rr-panel-tools') as HTMLElement;
      const numericCells = (table: string) => Array.from(tools.querySelectorAll(`${table} tbody tr`))
        .map(tr => Array.from(tr.querySelectorAll('td')).map(td => td.classList.contains('rr-num')));

      const usage = numericCells('.tool-usage-table');
      expect(usage.length).toBeGreaterThan(0);
      expect(usage.every(row => row.length === 2 && !row[0] && row[1])).toBe(true);
      expect(tools.querySelector('.tool-usage-table thead th:nth-child(2)')!.classList).toContain('rr-num');

      const routing = numericCells('.tool-routing-table');
      expect(routing.length).toBeGreaterThan(0);
      expect(routing.every(row => row.length === 3 && !row[0] && row[1] && row[2])).toBe(true);
      const routingHeads = Array.from(tools.querySelectorAll('.tool-routing-table thead th')).map(th => th.classList.contains('rr-num'));
      expect(routingHeads).toEqual([false, true, true]);
    });

    describe('typography', () => {
      // Expected sizes follow the root size, so the specs hold whatever the test page's root font size is.
      const rootPx = () => parseFloat(getComputedStyle(document.documentElement).fontSize);
      const sizeOf = (el: Element) => parseFloat(getComputedStyle(el).fontSize);

      function panel(key: string): HTMLElement {
        return fixture.nativeElement.querySelector(`.benchmark-run-detail-dialog #rr-panel-${key}`) as HTMLElement;
      }

      function one(root: HTMLElement, selector: string): HTMLElement {
        const el = root.querySelector(selector) as HTMLElement;
        expect(el, selector).toBeTruthy();
        return el;
      }

      /** Tool calls, a band disagreement, a configuration hash, a written report and a calibration row. */
      function openTypographyReport(): void {
        benchmarkServiceMock.listReportDocuments.mockReturnValue(of([{
          id: 71, packId: 'run-55', audience: 1, title: 'Document 71', subjectKey: 'run:55', subjectLabel: 'Test Model',
          subjectRunIds: [55], suiteId: 1, suiteName: 'Default Suite', writerDisplayName: 'Test Model',
          writerProvider: 'Anthropic', writerModelId: 'claude-3-5-sonnet', writerThinkingLevel: null,
          sameProviderAcknowledged: false, status: 'Completed', reportFormatVersion: 2,
          createdAtUtc: '2026-09-28T10:15:00Z', inputTokens: 0, outputTokens: 0, durationMs: 0, costUsd: null,
          runChangedSinceGeneration: false, missingRunIds: [], allowedDisclosures: [1, 2, 3], origin: 2
        }]));
        benchmarkServiceMock.getCalibrations.mockReturnValue(of([
          { id: 2, benchmarkRunId: 55, assessorDisplayNameUsed: 'Claude Opus 5', assessorProviderUsed: 'Anthropic',
            createdAtUtc: '2026-09-27T09:00:00Z', answerCount: 3, skippedAnswerCount: 0, meanAbsDelta: 4.5,
            disagreementCount: 1, inputTokens: 1, outputTokens: 1, durationMs: 1, comparedAgainst: null }
        ]));
        openReport(reportRun({
          assessmentJson: '{}', candidateSystemPromptSha256: 'abc123',
          answers: [
            reportAnswer(1, { toolCallSummary: 'source_code_search×3, wiki_search×1', toolCallCount: 4, assessedDifficulty: 90 }),
            reportAnswer(2, { toolCallSummary: 'wiki_view×2', toolCallCount: 2 })
          ]
        }));
      }

      it('should set every tab\'s running text at the body size', () => {
        openTypographyReport();
        const body = rootPx() * 0.875;

        const panels = Array.from(fixture.nativeElement.querySelectorAll('.benchmark-run-detail-dialog [role="tabpanel"].rr-panel')) as HTMLElement[];
        expect(panels.length).toBe(11);
        for (const p of panels) {
          expect(sizeOf(p), p.id).toBeCloseTo(body, 2);
        }

        const texts: [string, string][] = [
          ['difficulty', '.section-note'],
          ['difficulty', '.band-shift-list li'],
          ['tools', '.section-note'],
          ['tools', '.tool-usage-table td'],
          ['tools', '.tool-routing-table td'],
          ['configuration', '.rr-config dt'],
          ['configuration', '.rr-config dd'],
          ['calibration', '.calibration-item'],
          ['reports', '.rr-ai-status'],
          ['questions', '.section-note']
        ];
        for (const [key, selector] of texts) {
          expect(sizeOf(one(panel(key), selector)), `${key} ${selector}`).toBeCloseTo(body, 2);
        }
      });

      it('should set metadata and hints one step smaller', () => {
        openTypographyReport();
        const secondary = rootPx() * 0.8125;

        expect(sizeOf(one(panel('reports'), '.rr-ai-doc-meta'))).toBeCloseTo(secondary, 2);
        expect(sizeOf(one(panel('calibration'), '.form-hint'))).toBeCloseTo(secondary, 2);
      });

      it('should head the Questions tab like the other tabs', () => {
        openTypographyReport();

        expect(one(panel('questions'), '#rrQuestionsTitle').classList).toContain('gh-section-title');
      });

      it('should set inline code in the monospace stack', () => {
        openTypographyReport();

        const family = getComputedStyle(one(panel('configuration'), 'code')).fontFamily;
        expect(family).toContain('Consolas');
        expect(family).not.toBe('monospace');
      });

      it('should size Tool Routing as a sub-heading', () => {
        openTypographyReport();

        const heading = one(panel('tools'), 'h5');
        expect(heading.textContent?.trim()).toBe('Tool Routing');
        expect(sizeOf(heading)).toBeCloseTo(rootPx() * 0.875, 2);
        expect(getComputedStyle(heading).fontWeight).toBe('700');
      });
    });

    describe('tabs', () => {
      const KEYS = ['summary', 'integrity', 'synthesis', 'questions', 'difficulty', 'tools', 'cost', 'configuration', 'reports', 'calibration', 'paired'];

      function tablist(): HTMLElement {
        return fixture.nativeElement.querySelector('.benchmark-run-detail-dialog [role="tablist"][aria-label="Run report sections"]') as HTMLElement;
      }

      function tabs(): HTMLButtonElement[] {
        return Array.from(tablist().querySelectorAll('[role="tab"]')) as HTMLButtonElement[];
      }

      function tab(key: string): HTMLButtonElement {
        return fixture.nativeElement.querySelector(`#rr-tab-${key}`) as HTMLButtonElement;
      }

      function shownPanels(): HTMLElement[] {
        return (Array.from(fixture.nativeElement.querySelectorAll('.benchmark-run-detail-dialog [role="tabpanel"].rr-panel')) as HTMLElement[])
          .filter(panel => !panel.hidden);
      }

      function press(target: HTMLElement, key: string): KeyboardEvent {
        const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
        target.dispatchEvent(event);
        fixture.detectChanges();
        return event;
      }

      /** A run with no integrity clause: no critical error, flag or second reading. */
      function cleanRun(): any {
        return reportRun({ answers: [reportAnswer(1), reportAnswer(2), reportAnswer(3)] });
      }

      it('should render eleven tabs in order under the header, each controlling its own panel', () => {
        openReport(reportRun());

        const list = tablist();
        expect(list.classList).toContain('gh-tabs');
        expect(list.classList).toContain('gh-tabs-secondary');
        expect(list.closest('.rrf-tabs')).not.toBeNull();
        const all = tabs();
        expect(all.map(t => t.id)).toEqual(KEYS.map(key => `rr-tab-${key}`));
        expect(all.map(t => (t.textContent || '').replace(/\s+/g, ' ').trim())).toEqual([
          'Summary', 'Integrity Notice', 'Synthesis', 'Questions (3)', 'Difficulty', 'Tools', 'Cost',
          'Configuration', 'AI Reports', 'Calibration', 'Paired Test'
        ]);
        expect(list.querySelector('svg')).toBeNull();
        expect(all.filter(t => t.getAttribute('tabindex') === '0').map(t => t.id)).toEqual(['rr-tab-summary']);
        for (const t of all) {
          expect(t.getAttribute('type')).toBe('button');
          const panel = fixture.nativeElement.querySelector('#' + t.getAttribute('aria-controls')) as HTMLElement;
          expect(panel.getAttribute('role'), t.id).toBe('tabpanel');
          expect(panel.getAttribute('aria-labelledby'), t.id).toBe(t.id);
          expect(panel.getAttribute('tabindex'), t.id).toBe('0');
        }
        expect(tab('summary').getAttribute('aria-selected')).toBe('true');
        expect(shownPanels().map(p => p.id)).toEqual(['rr-panel-summary']);
      });

      it('should cap the Difficulty, Tools, Cost, Configuration, AI Reports, Calibration and Paired Test panels and no other', () => {
        openReport(reportRun());

        const panel = (key: string) => fixture.nativeElement.querySelector(`#rr-panel-${key}`) as HTMLElement;
        for (const key of ['difficulty', 'tools', 'cost']) {
          expect(panel(key).classList, key).toContain('rr-panel-narrow');
          expect(panel(key).classList, key).not.toContain('rr-panel-medium');
        }
        for (const key of ['configuration', 'reports', 'calibration', 'paired']) {
          expect(panel(key).classList, key).toContain('rr-panel-medium');
          expect(panel(key).classList, key).not.toContain('rr-panel-narrow');
        }
        for (const key of ['summary', 'questions']) {
          expect(panel(key).classList, key).not.toContain('rr-panel-narrow');
          expect(panel(key).classList, key).not.toContain('rr-panel-medium');
        }
      });

      it('should show exactly the chosen panel, keep the others rendered, and remember the choice', () => {
        openReport(reportRun());

        tab('cost').click();
        fixture.detectChanges();

        expect(component.runReportTab).toBe('cost');
        expect(tab('cost').getAttribute('aria-selected')).toBe('true');
        expect(tab('cost').getAttribute('tabindex')).toBe('0');
        expect(tab('summary').getAttribute('tabindex')).toBe('-1');
        expect(shownPanels().map(p => p.id)).toEqual(['rr-panel-cost']);
        expect(getComputedStyle(fixture.nativeElement.querySelector('#rr-panel-summary')).display).toBe('none');
        expect(fixture.nativeElement.querySelectorAll('#rr-panel-summary .score-card').length).toBeGreaterThan(0);
        expect(localStorage.getItem(RUN_REPORT_TAB_STORAGE_KEY)).toBe('cost');
      });

      it('should wrap with the arrow keys, jump with Home and End, and move focus with the selection', () => {
        openReport(reportRun());

        let event = press(tab('summary'), 'ArrowLeft');
        expect(event.defaultPrevented).toBe(true);
        expect(component.runReportTab).toBe('paired');
        expect(document.activeElement).toBe(tab('paired'));

        press(tab('paired'), 'ArrowRight');
        expect(component.runReportTab).toBe('summary');
        expect(document.activeElement).toBe(tab('summary'));

        press(tab('summary'), 'End');
        expect(component.runReportTab).toBe('paired');
        press(tab('paired'), 'Home');
        expect(component.runReportTab).toBe('summary');
        press(tab('summary'), 'ArrowRight');
        expect(component.runReportTab).toBe('integrity');
        expect(document.activeElement).toBe(tab('integrity'));

        event = press(tab('integrity'), 'a');
        expect(event.defaultPrevented).toBe(false);
        expect(component.runReportTab).toBe('integrity');
      });

      it('should reopen on the last tab chosen, but keep the shown tab when a re-score reloads the open report', async () => {
        openReport(reportRun());
        tab('tools').click();
        fixture.detectChanges();

        // A reload of the open dialog keeps the tab even if the stored one differs.
        localStorage.setItem(RUN_REPORT_TAB_STORAGE_KEY, 'cost');
        openReport(reportRun());
        expect(component.runReportTab).toBe('tools');

        const closed = nextEvent(reportDialog(), 'close');
        component.closeRunDetail();
        await closed;
        openReport(reportRun());

        expect(component.runReportTab).toBe('cost');
        expect(shownPanels().map(p => p.id)).toEqual(['rr-panel-cost']);
      });

      it('should fall back to Summary for an unknown stored tab or unreadable storage', async () => {
        localStorage.setItem(RUN_REPORT_TAB_STORAGE_KEY, 'no-such-tab');
        component.runReportTab = 'cost';
        openReport(reportRun());
        expect(component.runReportTab).toBe('summary');

        const closed = nextEvent(reportDialog(), 'close');
        reportDialog().close();
        await closed;
        vi.spyOn(localStorage, 'getItem').mockImplementation(() => {
          throw new Error('denied');
        });
        component.runReportTab = 'cost';
        openReport(reportRun());
        expect(component.runReportTab).toBe('summary');
      });

      it('should mark the Integrity tab with Notice exactly while the Run Integrity Notice shows', () => {
        openReport(reportRun());
        expect(component.hasRunIntegrityNotice).toBe(true);
        expect(tab('integrity').querySelector('.gh-tag.rr-tab-flag')?.textContent?.trim()).toBe('Notice');
        expect(fixture.nativeElement.querySelector('#rr-panel-integrity')?.textContent).toContain('Run Integrity Notice');

        openReport(cleanRun());
        expect(component.hasRunIntegrityNotice).toBe(false);
        expect(tab('integrity').querySelector('.rr-tab-flag')).toBeNull();
        const panel = fixture.nativeElement.querySelector('#rr-panel-integrity') as HTMLElement;
        expect(panel.textContent).not.toContain('Run Integrity Notice');
        expect(panel.textContent).toContain('No integrity notices for this run.');
      });

      it('should select Questions when jumping to an answer', () => {
        openReport(reportRun());

        component.jumpToAnswer(3);
        fixture.detectChanges();

        expect(component.runReportTab).toBe('questions');
        expect(shownPanels().map(p => p.id)).toEqual(['rr-panel-questions']);
        expect(component.expandedQuestions.has(3)).toBe(true);
      });

      it('should keep the run-wide strips above the panels, outside every tab panel', () => {
        component.selectedRunDetail = reportRun();
        ctx.workspace.actionErrorMessage = 'Re-scoring failed.';
        ctx.refresh();

        const body = fixture.nativeElement.querySelector('.benchmark-run-detail-dialog .rrf-single') as HTMLElement;
        const alert = Array.from(body.querySelectorAll('[role="alert"]'))
          .find(el => el.textContent?.includes('Re-scoring failed.')) as HTMLElement;
        expect(alert.closest('[role="tabpanel"]')).toBeNull();
        const firstPanel = body.querySelector('[role="tabpanel"]') as HTMLElement;
        expect(alert.compareDocumentPosition(firstPanel) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      });

      it('should show no tab row while the run loads or after it failed to load', () => {
        const pending = new Subject<any>();
        benchmarkServiceMock.getRun.mockReturnValue(pending.asObservable());
        component.viewRunDetail(77);
        fixture.detectChanges();
        expect(fixture.nativeElement.querySelector('.benchmark-run-detail-dialog [role="tablist"]')).toBeNull();
        expect(fixture.nativeElement.querySelector('.benchmark-run-detail-dialog [role="tabpanel"]')).toBeNull();

        vi.spyOn(console, 'error').mockReturnValue(undefined);
        pending.error({ status: 500 });
        fixture.detectChanges();
        expect(fixture.nativeElement.querySelector('#rr-retry-load')).not.toBeNull();
        expect(fixture.nativeElement.querySelector('.benchmark-run-detail-dialog [role="tablist"]')).toBeNull();
      });
    });

    describe('key figures', () => {
      const NOW = new Date(2026, 8, 28, 12, 34, 56);

      beforeEach(() => {
        vi.spyOn(keyFiguresImageIo, 'loadImage').mockImplementation(() => Promise.reject(new Error('404')));
        vi.spyOn(keyFiguresImageIo, 'now').mockReturnValue(NOW);
      });

      function dialog(): HTMLElement {
        return fixture.nativeElement.querySelector('.benchmark-run-detail-dialog') as HTMLElement;
      }

      function status(): string {
        return (runActions().querySelector('.rr-status[role="status"]')?.textContent || '').trim();
      }

      /** Clicks a button whose handler is async, and waits for the handler to finish. */
      async function clickAndSettle(button: HTMLButtonElement, handler: Mock): Promise<void> {
        button.click();
        await handler.mock.results.at(-1)!.value;
        fixture.detectChanges();
      }

      it('should show no wordmark above the tab row, and the emblem before the run title', () => {
        component.selectedRunDetail = reportRun();
        fixture.detectChanges();

        expect(fixture.nativeElement.querySelector('.benchmark-container .gnollbench-wordmark')).toBeNull();
        expect(fixture.nativeElement.querySelector('.bm-brand')).toBeNull();
        const container = fixture.nativeElement.querySelector('.benchmark-container') as HTMLElement;
        const firstChild = container.firstElementChild as HTMLElement;
        expect(firstChild.matches('.gh-tabs[role="tablist"]')).toBe(true);

        const emblem = dialog().querySelector('.rr-identity > img.gnollbench-emblem') as HTMLImageElement;
        expect(emblem).toBeTruthy();
        expect(emblem.getAttribute('alt')).toBe('');
        expect(emblem.getAttribute('src')).toBe('/img/gnollbench/gnollbench-logo-v3-256.webp');
        expect(emblem.getAttribute('width')).toBe('64');
        expect(emblem.getAttribute('height')).toBe('64');
        const titleGroup = emblem.nextElementSibling as HTMLElement;
        expect(titleGroup.classList).toContain('dialog-title-group');
        expect(titleGroup.querySelector('#runDetailTitle')).toBeTruthy();
      });

      it('should head the Summary panel with Key figures, its Copy and Download beside it, then the cards', () => {
        component.selectedRunDetail = reportRun({ qualityIndex: 73, qualityIndexStandardError: 4, estimatedCost: 3.2322 });
        fixture.detectChanges();

        const panel = dialog().querySelector('#rr-panel-summary') as HTMLElement;
        const title = panel.querySelector('.rr-figures-head > h4#rrFiguresTitle') as HTMLElement;
        expect(title.textContent?.trim()).toBe('Key figures');
        expect(title.classList).toContain('gh-section-title');

        const group = panel.querySelector('.rr-figures-head > [role="group"][aria-label="Key figures actions"]') as HTMLElement;
        const names = Array.from(group.querySelectorAll('button')).map(b => b.getAttribute('aria-label'));
        expect(names).toEqual([
          'Copy key figures of run 55 as an image',
          'Download key figures of run 55 as a PNG image',
          'Choose key figures for run 55'
        ]);
        const choose = group.querySelector('#rr-figures-choose-btn') as HTMLButtonElement;
        expect(choose.classList).toContain('btn-ghost');
        expect(choose.textContent?.replace(/\s+/g, ' ').trim()).toBe('Choose figures');
        expect(choose.querySelector('svg')).toBeNull();
        expect(choose.getAttribute('aria-haspopup')).toBe('dialog');
        for (const button of Array.from(group.querySelectorAll('button'))) {
          const tip = group.querySelector('#' + button.getAttribute('interestfor')) as HTMLElement;
          expect(tip.getAttribute('popover')).toBe('hint');
          expect(button.getAttribute('style')).toContain('anchor-name: --' + tip.id);
          expect(tip.getAttribute('style')).toContain('position-anchor: --' + tip.id);
        }
        expect(group.querySelector('#rr-figures-copy-tip')?.textContent?.trim()).toBe('Copy key figures as an image');
        expect(group.querySelector('#rr-figures-download-tip')?.textContent?.trim()).toBe('Download key figures as PNG');
        expect(group.querySelector('#rr-figures-choose-tip')?.textContent?.trim()).toBe('Choose which key figures to show and export');

        const head = panel.querySelector('.rr-figures-head') as HTMLElement;
        expect(getComputedStyle(head).paddingBottom).toBe('8px');
        expect(getComputedStyle(title).paddingBottom).toBe('0px');

        const figures = panel.querySelector('.rr-figures') as HTMLElement;
        expect(figures.getAttribute('role')).toBe('group');
        expect(figures.getAttribute('aria-labelledby')).toBe('rrFiguresTitle');
        expect(figures.querySelectorAll(':scope > .score-card').length).toBeGreaterThan(0);
        expect(figures.hidden).toBe(false);
        expect(getComputedStyle(figures).display).toBe('grid');
        expect(panel.querySelector('.rr-figures-empty')).toBeNull();
        expect(dialog().querySelector('.rrf-figures-toggle, .rrf-figures, .rrf-figures-bar')).toBeNull();
      });

      it('should give every score card its own card actions, as its last child', () => {
        component.selectedRunDetail = reportRun({ estimatedCandidateCost: 1, pricingIncomplete: true });
        fixture.detectChanges();

        const cards = Array.from(dialog().querySelectorAll('.score-card')) as HTMLElement[];
        expect(cards.length).toBeGreaterThan(5);
        for (const card of cards) {
          const label = (card.querySelector('.score-label')?.textContent || '').trim();
          const actions = card.querySelectorAll(':scope > app-key-figure-card-actions');
          expect(actions.length, label).toBe(1);
          expect(card.lastElementChild?.tagName.toLowerCase(), label).toBe('app-key-figure-card-actions');
          const copy = actions[0].querySelector('button') as HTMLButtonElement;
          expect(copy.getAttribute('aria-label'), label).toBe(`Copy ${label} of run 55 as an image`);
        }
      });

      it('should copy the strip and announce each copy outcome', async () => {
        component.selectedRunDetail = reportRun();
        fixture.detectChanges();
        const copy = vi.spyOn(keyFiguresImageIo, 'copy').mockResolvedValue('copied');
        const handler = vi.spyOn(component, 'copyKeyFigures');
        const button = dialog().querySelector('#rr-figures-copy-btn') as HTMLButtonElement;

        await clickAndSettle(button, handler);
        expect(copy).toHaveBeenCalledTimes(1);
        expect((vi.mocked(copy).mock.lastCall![0] as Blob).type).toBe('image/png');
        expect(status()).toBe('Key figures copied as an image.');

        copy.mockResolvedValue('unsupported');
        await clickAndSettle(button, handler);
        expect(status()).toBe('This browser cannot copy images here; use Download instead.');

        copy.mockResolvedValue('denied');
        await clickAndSettle(button, handler);
        expect(status()).toBe('Could not copy the image.');
        expect(component.keyFiguresExporting).toBe(false);
      });

      it('should copy one card and name it in the status line', async () => {
        component.selectedRunDetail = reportRun();
        fixture.detectChanges();
        const copy = vi.spyOn(keyFiguresImageIo, 'copy').mockResolvedValue('copied');
        const handler = vi.spyOn(component, 'exportKeyFigureCard');
        const card = dialog().querySelector('.score-card.main-score') as HTMLElement;

        await clickAndSettle(card.querySelector('app-key-figure-card-actions button') as HTMLButtonElement, handler);

        expect(vi.mocked(handler).mock.lastCall![0]).toEqual({ action: 'copy', card });
        expect(copy).toHaveBeenCalledTimes(1);
        expect(status()).toBe('Intelligence Index copied as an image.');
      });

      it('should download the strip through the IO holder, whichever tab is shown', async () => {
        component.selectedRunDetail = reportRun();
        fixture.detectChanges();
        component.selectRunReportTab('cost');
        fixture.detectChanges();
        expect((dialog().querySelector('#rr-panel-summary') as HTMLElement).hidden).toBe(true);
        const save = vi.spyOn(keyFiguresImageIo, 'save').mockReturnValue(undefined);
        const handler = vi.spyOn(component, 'downloadKeyFigures');

        await clickAndSettle(dialog().querySelector('#rr-figures-download-btn') as HTMLButtonElement, handler);

        expect(save).toHaveBeenCalledTimes(1);
        const [blob, fileName] = vi.mocked(save).mock.lastCall!;
        expect(blob.type).toBe('image/png');
        expect(fileName).toBe('gnollbench_run55_default-suite_test-model_key-figures_20260928_123456.png');
        expect(status()).toBe('Image downloaded.');
      });

      it('should refuse a second export while one runs, and mark the buttons aria-disabled', async () => {
        component.selectedRunDetail = reportRun();
        component.keyFiguresExporting = true;
        fixture.detectChanges();
        const copy = vi.spyOn(keyFiguresImageIo, 'copy').mockResolvedValue('copied');

        const strip = dialog().querySelector('#rr-figures-copy-btn') as HTMLButtonElement;
        expect(strip.getAttribute('aria-disabled')).toBe('true');
        const cardCopy = dialog().querySelector('.score-card app-key-figure-card-actions button') as HTMLButtonElement;
        expect(cardCopy.getAttribute('aria-disabled')).toBe('true');

        await component.copyKeyFigures();
        cardCopy.click();
        expect(copy).not.toHaveBeenCalled();
        component.keyFiguresExporting = false;
      });

      function meanTimeCard(): HTMLElement {
        return dialog().querySelector('.score-card[data-figure="mean-time"]') as HTMLElement;
      }

      function textOf(element: Element | null | undefined): string {
        return (element?.textContent || '').replace(/\s+/g, ' ').trim();
      }

      it('should show Mean Time per Question right after the speed card, with the median in its note', () => {
        component.selectedRunDetail = reportRun();
        fixture.detectChanges();

        const card = meanTimeCard();
        expect(card).toBeTruthy();
        expect(card.previousElementSibling?.getAttribute('data-figure')).toBe('speed');
        expect(textOf(card.querySelector('.score-label'))).toBe('Mean Time per Question');
        expect(textOf(card.querySelector('.score-subvalue'))).toBe('1.0 s');
        expect(textOf(card.querySelector('.score-note'))).toBe('model time, tools excluded · median 1.0 s');
        expect(card.querySelector('app-key-figure-card-actions button')?.getAttribute('aria-label'))
          .toBe('Copy Mean Time per Question of run 55 as an image');
      });

      it('should write the mean in tenths of a second under a minute', () => {
        component.selectedRunDetail = reportRun({
          answers: [1, 2, 3].map(i => reportAnswer(i, { modelTimeMs: [15000, 14700, 21900][i - 1] }))
        });
        fixture.detectChanges();
        expect(component.meanModelTimeMs).toBe(17200);
        expect(textOf(meanTimeCard().querySelector('.score-subvalue'))).toBe('17.2 s');
        expect(textOf(meanTimeCard().querySelector('.score-note'))).toBe('model time, tools excluded · median 15.0 s');
      });

      it('should write the mean in minutes and seconds from one minute', () => {
        component.selectedRunDetail = reportRun({
          answers: [1, 2, 3].map(i => reportAnswer(i, { modelTimeMs: [60000, 90000, 66000][i - 1] }))
        });
        fixture.detectChanges();
        expect(component.meanModelTimeMs).toBe(72000);
        expect(textOf(meanTimeCard().querySelector('.score-subvalue'))).toBe('1m 12s');
        expect(textOf(meanTimeCard().querySelector('.score-note'))).toBe('model time, tools excluded · median 1m 6s');

        expect(component.formatModelTime(59940)).toBe('59.9 s');
        expect(component.formatModelTime(59960)).toBe('1m 0s');
      });

      it('should keep the card with a dash when no question was answered', () => {
        component.selectedRunDetail = reportRun({
          answers: [1, 2].map(i => reportAnswer(i, { status: 'Failed', qualityScore: null }))
        });
        fixture.detectChanges();

        expect(component.meanModelTimeMs).toBeNull();
        const value = meanTimeCard().querySelector('.score-subvalue') as HTMLElement;
        expect(textOf(value)).toBe('—');
        expect(value.classList).toContain('text-muted');
        expect(textOf(meanTimeCard().querySelector('.score-note'))).toBe('no answered question');
      });

      it('should give every card a stable data-figure key, in display order', () => {
        component.selectedRunDetail = reportRun({
          isPanelRun: true, coAssessorModelDisplayNameUsed: 'Second Assessor',
          secondOpinionGradedAnswerCount: 3, estimatedCandidateCost: 1, rawQualityIndex: 50, qualityIndex: 73
        });
        fixture.detectChanges();

        const cards = Array.from(dialog().querySelectorAll<HTMLElement>('.rr-figures > .score-card'));
        const keys = cards.map(card => card.getAttribute('data-figure'));
        expect(cards.filter(card => card.hidden)).toEqual([]);
        expect(keys).toEqual(component.shownKeyFigureKeys);
        expect(keys).toEqual([
          'intelligence', 'raw-quality', 'critical-errors', 'answered', 'speed', 'mean-time', 'panel',
          'agreement', 'holistic', 'answer-duration', 'wall-time', 'model-cost', 'estimated-cost'
        ]);
      });

      it('should name the answered count in the mean-time note when it is below the question count', () => {
        component.selectedRunDetail = reportRun({
          answeredQuestionCount: 2,
          answers: [
            reportAnswer(1, { modelTimeMs: 2000 }),
            reportAnswer(2, { modelTimeMs: 4000 }),
            reportAnswer(3, { status: 'Failed', qualityScore: null })
          ]
        });
        fixture.detectChanges();

        expect(textOf(meanTimeCard().querySelector('.score-note'))).toBe('model time, tools excluded · median 3.0 s · over 2 answered');
      });

      function figureCards(): HTMLElement[] {
        return Array.from(dialog().querySelectorAll<HTMLElement>('.rr-figures > .score-card'));
      }

      function shownFigureKeys(): (string | null)[] {
        return figureCards().filter(card => !card.hidden).map(card => card.getAttribute('data-figure'));
      }

      it('should filter the Summary cards live from the chooser, remember the choice, and export it', async () => {
        openReport(reportRun());
        const choose = dialog().querySelector('#rr-figures-choose-btn') as HTMLButtonElement;
        choose.click();
        fixture.detectChanges();

        const chooser = dialog().querySelector('app-key-figures-chooser dialog') as HTMLDialogElement;
        expect(chooser.open).toBe(true);
        expect(chooser.matches(':modal')).toBe(true);
        const allKeys = [
          'intelligence', 'critical-errors', 'answered', 'speed', 'mean-time', 'holistic', 'answer-duration',
          'wall-time', 'estimated-cost'
        ];
        const rows = Array.from(chooser.querySelectorAll('li[data-figure]')).map(li => li.getAttribute('data-figure'));
        expect(rows).toEqual(allKeys);
        expect(textOf(chooser.querySelector('li[data-figure="mean-time"] label'))).toBe('Mean Time per Question — 1.0 s');
        expect(textOf(chooser.querySelector('li[data-figure="critical-errors"] label'))).toBe('Critical Errors — 1');
        expect(textOf(chooser.querySelector('li[data-figure="answered"] label'))).toBe('Answered — 3 / 3');
        expect(textOf(chooser.querySelector('[role="status"]'))).toBe('9 of 9 selected');

        (chooser.querySelector('#kfch-speed') as HTMLInputElement).click();
        (chooser.querySelector('#kfch-estimated-cost') as HTMLInputElement).click();
        fixture.detectChanges();
        expect(textOf(chooser.querySelector('[role="status"]'))).toBe('7 of 9 selected');

        expect(JSON.parse(localStorage.getItem(KEY_FIGURES_STORAGE_KEY)!))
          .toEqual({ version: 1, excluded: ['speed', 'estimated-cost'] });
        expect(component.keyFigureExclusions).toEqual(['speed', 'estimated-cost']);
        for (const key of ['speed', 'estimated-cost']) {
          const card = dialog().querySelector(`.score-card[data-figure="${key}"]`) as HTMLElement;
          expect(card.hidden, key).toBe(true);
          expect(getComputedStyle(card).display, key).toBe('none');
        }
        expect(shownFigureKeys()).toEqual([
          'intelligence', 'critical-errors', 'answered', 'mean-time', 'holistic', 'answer-duration', 'wall-time'
        ]);

        const closed = nextEvent(chooser, 'close');
        (chooser.querySelector('.kfch-done') as HTMLButtonElement).click();
        await closed;
        fixture.detectChanges();

        expect(chooser.open).toBe(false);
        expect(document.activeElement).toBe(choose);
        expect(reportDialog().open).toBe(true);
        expect(textOf(choose)).toBe('Choose figures (7 of 9)');
        expect(choose.getAttribute('aria-label')).toBe('Choose key figures for run 55, 7 of 9 selected');
        expect(dialog().querySelector('#rr-figures-copy-btn')?.getAttribute('aria-label'))
          .toBe('Copy key figures of run 55 as an image, 7 of 9 key figures');
        expect(dialog().querySelector('#rr-figures-download-btn')?.getAttribute('aria-label'))
          .toBe('Download key figures of run 55 as a PNG image, 7 of 9 key figures');

        const save = vi.spyOn(keyFiguresImageIo, 'save').mockReturnValue(undefined);
        const handler = vi.spyOn(component, 'downloadKeyFigures');
        await clickAndSettle(dialog().querySelector('#rr-figures-download-btn') as HTMLButtonElement, handler);
        expect(save).toHaveBeenCalledTimes(1);
        expect(status()).toBe('Image downloaded.');

        choose.click();
        fixture.detectChanges();
        expect(Array.from(chooser.querySelectorAll('li[data-figure]')).map(li => li.getAttribute('data-figure'))).toEqual(allKeys);
        const unchecked = Array.from(chooser.querySelectorAll<HTMLInputElement>('li[data-figure] input[type="checkbox"]'))
          .filter(box => !box.checked).map(box => box.id);
        expect(unchecked).toEqual(['kfch-speed', 'kfch-estimated-cost']);
      });

      it('should keep the live choice when the chooser is closed by its close button, and say so when nothing is selected', async () => {
        openReport(reportRun());
        const choose = dialog().querySelector('#rr-figures-choose-btn') as HTMLButtonElement;
        choose.click();
        fixture.detectChanges();
        const chooser = dialog().querySelector('app-key-figures-chooser dialog') as HTMLDialogElement;
        (chooser.querySelector('.kfch-none') as HTMLButtonElement).click();
        fixture.detectChanges();

        expect(component.keyFigureExclusions).toEqual(component.shownKeyFigureKeys);
        expect(figureCards().every(card => card.hidden)).toBe(true);
        const figures = dialog().querySelector('.rr-figures') as HTMLElement;
        expect(figures.hidden).toBe(true);
        expect(getComputedStyle(figures).display).toBe('none');
        expect(textOf(dialog().querySelector('.rr-figures-empty')))
          .toBe('No key figures are selected. Use Choose figures to show them.');
        expect(textOf(choose)).toBe('Choose figures (0 of 9)');

        (chooser.querySelector('#kfch-holistic') as HTMLInputElement).click();
        fixture.detectChanges();
        expect(dialog().querySelector('.rr-figures-empty')).toBeNull();
        expect(figures.hidden).toBe(false);
        expect(shownFigureKeys()).toEqual(['holistic']);

        const closed = nextEvent(chooser, 'close');
        (chooser.querySelector('.kfch-close') as HTMLButtonElement).click();
        await closed;
        fixture.detectChanges();

        const others = [
          'intelligence', 'critical-errors', 'answered', 'speed', 'mean-time', 'answer-duration', 'wall-time',
          'estimated-cost'
        ];
        expect(document.activeElement).toBe(choose);
        expect(reportDialog().open).toBe(true);
        expect(component.keyFigureExclusions).toEqual(others);
        expect(JSON.parse(localStorage.getItem(KEY_FIGURES_STORAGE_KEY)!)).toEqual({ version: 1, excluded: others });
      });

      it('should export the remembered selection from the one-click Copy, and nothing when none of it is shown', async () => {
        component.selectedRunDetail = reportRun();
        component.keyFigureExclusions = [...component.shownKeyFigureKeys];
        fixture.detectChanges();
        expect(textOf(dialog().querySelector('.rr-figures-empty')))
          .toBe('No key figures are selected. Use Choose figures to show them.');
        expect((dialog().querySelector('.rr-figures') as HTMLElement).hidden).toBe(true);
        const copy = vi.spyOn(keyFiguresImageIo, 'copy').mockResolvedValue('copied');
        const handler = vi.spyOn(component, 'copyKeyFigures');

        await clickAndSettle(dialog().querySelector('#rr-figures-copy-btn') as HTMLButtonElement, handler);
        expect(copy).not.toHaveBeenCalled();
        expect(status()).toBe('None of this run\'s key figures is selected; use Choose figures.');

        component.keyFigureExclusions = ['panel', 'holistic'];
        fixture.detectChanges();
        expect(component.keyFiguresSelectionLabel).toBe('8 of 9');
        await clickAndSettle(dialog().querySelector('#rr-figures-copy-btn') as HTMLButtonElement, handler);
        expect(copy).toHaveBeenCalledTimes(1);
        expect(status()).toBe('Key figures copied as an image.');
        component.keyFigureExclusions = [];
      });

      it('should download in the stored format, written by either report, label the downloads for it, and still copy a PNG', async () => {
        // Stored as the Battery Run Report's chooser stores it: one key for both reports.
        writeStoredKeyFiguresExportSettings({ ...defaultKeyFiguresExportSettings(), format: 'webp' });
        component.selectedRunDetail = reportRun();
        fixture.detectChanges();

        expect(dialog().querySelector('#rr-figures-download-btn')?.getAttribute('aria-label'))
          .toBe('Download key figures of run 55 as a WebP image');
        expect(textOf(dialog().querySelector('#rr-figures-download-tip'))).toBe('Download key figures as WebP');
        const main = dialog().querySelector('.score-card.main-score') as HTMLElement;
        expect(main.querySelector('button.kfc-download')?.getAttribute('aria-label'))
          .toBe('Download Intelligence Index of run 55 as a WebP image');

        const save = vi.spyOn(keyFiguresImageIo, 'save').mockReturnValue(undefined);
        const download = vi.spyOn(component, 'downloadKeyFigures');
        await clickAndSettle(dialog().querySelector('#rr-figures-download-btn') as HTMLButtonElement, download);
        const [blob, fileName] = vi.mocked(save).mock.lastCall!;
        expect(blob.type).toBe('image/webp');
        expect(fileName).toBe('gnollbench_run55_default-suite_test-model_key-figures_20260928_123456.webp');
        expect(status()).toBe('Image downloaded.');

        const copy = vi.spyOn(keyFiguresImageIo, 'copy').mockResolvedValue('copied');
        const copyHandler = vi.spyOn(component, 'copyKeyFigures');
        await clickAndSettle(dialog().querySelector('#rr-figures-copy-btn') as HTMLButtonElement, copyHandler);
        expect((vi.mocked(copy).mock.lastCall![0] as Blob).type).toBe('image/png');
      });

      it('should store a format chosen in the chooser under the key both reports read, and relabel the downloads', () => {
        openReport(reportRun());
        (dialog().querySelector('#rr-figures-choose-btn') as HTMLButtonElement).click();
        fixture.detectChanges();
        const chooser = dialog().querySelector('app-key-figures-chooser dialog') as HTMLDialogElement;
        expect(textOf(chooser.querySelector('.kfch-summary'))).toMatch(/^Downloads a PNG, about \d+ × \d+ px\.$/);
        expect(textOf(chooser.querySelector('[role="status"]'))).toBe('9 of 9 selected');

        (chooser.querySelector('#kfch-fmt-format-webp') as HTMLInputElement).click();
        fixture.detectChanges();

        expect(JSON.parse(localStorage.getItem(KEY_FIGURES_EXPORT_STORAGE_KEY)!))
          .toEqual(expect.objectContaining({ version: 1, format: 'webp', webpQuality: 85 }));
        expect(readStoredKeyFiguresExportSettings().format).toBe('webp');
        expect(dialog().querySelector('#rr-figures-download-btn')?.getAttribute('aria-label'))
          .toBe('Download key figures of run 55 as a WebP image');
        expect(textOf(chooser.querySelector('.kfch-summary'))).toMatch(/^Downloads a WebP, about \d+ × \d+ px\.$/);
      });

      it('should label the downloads PNG while nothing is stored', () => {
        component.selectedRunDetail = reportRun();
        fixture.detectChanges();
        expect(localStorage.getItem(KEY_FIGURES_EXPORT_STORAGE_KEY)).toBeNull();
        expect(component.keyFiguresDownloadFormat).toBe('PNG');
        expect(dialog().querySelector('.score-card.main-score button.kfc-download')?.getAttribute('aria-label'))
          .toBe('Download Intelligence Index of run 55 as a PNG image');
      });

      it('should read the remembered selection when constructed', () => {
        localStorage.setItem(KEY_FIGURES_STORAGE_KEY, JSON.stringify({ version: 1, excluded: ['holistic'] }));
        const restored = TestBed.createComponent(AdminBenchmarkComponent);
        expect(restored.componentInstance.keyFigureExclusions).toEqual(['holistic']);
        restored.destroy();
      });

      const PROMPT_OPTIONS = JSON.stringify({ verboseMode: false, enableToolUse: true, hasGameSnapshot: true });
      const BOARD_DELIVERY = [
        { role: 'assessor', delivered: 18, total: 18, missingQuestions: [] },
        { role: 'second reader', delivered: 13, total: 14, missingQuestions: [2] }
      ];

      it('should describe the run in the image context with the header facts, the board left out by default', () => {
        const run = reportRun({
          isPanelRun: true, coAssessorModelDisplayNameUsed: 'Second Assessor', coAssessorModelProviderUsed: 'Anthropic',
          testedModelThinkingLevelUsed: 'high', candidatePromptOptionsJson: PROMPT_OPTIONS, boardDelivery: BOARD_DELIVERY
        });
        expect(component.imageDetailExclusions).toEqual(['board']);
        const context = component.keyFiguresContext(run);
        expect(context.title).toBe('Run #55 · Default Suite');
        expect(context.facts.map(row => row.label)).toEqual(['Model', 'Assessors', 'Prompt', 'Scoring profile', 'Started']);
        expect(context.facts.map(row => row.primary)).toEqual([true, true, false, false, false]);
        expect(context.facts[0].runs.map(run => [run.kind, run.text])).toEqual([
          ['text', 'Test Model'], ['badge', 'High'], ['badge', 'OpenAI']
        ]);
        const started = context.facts[4].runs;
        expect(started[0].text).toMatch(/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} UTC$/);
        expect(started[1]).toEqual({ kind: 'badge', text: 'Completed', tone: 'success' });
        expect([context.runId, context.suiteName, context.modelName]).toEqual([55, 'Default Suite', 'Test Model']);
      });

      it('should leave out of the image the rows the image details exclude, while the header lists them all', () => {
        const run = reportRun({ candidatePromptOptionsJson: PROMPT_OPTIONS, boardDelivery: BOARD_DELIVERY });
        component.selectedRunDetail = run;
        fixture.detectChanges();

        component.imageDetailExclusions = ['prompt'];
        const withoutPrompt = component.keyFiguresContext(run).facts.map(row => row.label);
        expect(withoutPrompt).toEqual(['Model', 'Assessor', 'Scoring profile', 'Started', 'Board']);
        const header = Array.from(dialog().querySelectorAll('app-run-facts [data-fact]')).map(fact => fact.getAttribute('data-fact'));
        expect(header).toEqual(['model', 'assessor', 'prompt', 'profile', 'started', 'board']);

        component.imageDetailExclusions = [];
        const board = component.keyFiguresContext(run).facts.find(row => row.label === 'Board')!;
        expect(board.runs.map(run => run.text)).toEqual([
          'Assessor 18/18 · Second reader 13/14',
          '· Synthesis: yes · Difficulty assessment: digest (no map)',
          'Graded without the board — second reader: Q2'
        ]);
        expect(component.keyFiguresContext(run).facts.map(row => row.label))
          .toEqual(['Model', 'Assessor', 'Prompt', 'Scoring profile', 'Started', 'Board']);
      });

      it('should build the header facts once per run object', () => {
        component.selectedRunDetail = reportRun();
        const first = component.selectedRunFacts;
        expect(component.selectedRunFacts).toBe(first);
        component.selectedRunDetail = reportRun();
        expect(component.selectedRunFacts).not.toBe(first);
      });

      it('should offer the run settings as image details in the chooser, and remember a change at once', () => {
        openReport(reportRun());
        const choose = dialog().querySelector('#rr-figures-choose-btn') as HTMLButtonElement;
        choose.click();
        fixture.detectChanges();

        const chooser = dialog().querySelector('app-key-figures-chooser dialog') as HTMLDialogElement;
        const details = Array.from(chooser.querySelectorAll('li[data-detail]'));
        expect(details.map(li => li.getAttribute('data-detail'))).toEqual(['model', 'assessor', 'profile', 'started']);
        expect(textOf(chooser.querySelector('li[data-detail="assessor"] label'))).toBe('Assessor — Test Assessor');
        expect(textOf(chooser.querySelector('.kfch-detail-count'))).toBe('4 of 4 selected');

        (chooser.querySelector('#kfch-detail-profile') as HTMLInputElement).click();
        fixture.detectChanges();
        expect(component.imageDetailExclusions).toEqual(['board', 'profile']);
        expect(JSON.parse(localStorage.getItem(IMAGE_DETAILS_STORAGE_KEY)!)).toEqual({ version: 1, excluded: ['board', 'profile'] });
        expect(component.keyFigureExclusions).toEqual([]);
        expect(component.keyFiguresContext(component.selectedRunDetail!).facts.map(row => row.label))
          .toEqual(['Model', 'Assessor', 'Started']);
      });

      it('should store the image details the chooser reports', () => {
        component.onImageDetailSelectionChange(['prompt', 'started']);
        expect(component.imageDetailExclusions).toEqual(['prompt', 'started']);
        expect(JSON.parse(localStorage.getItem(IMAGE_DETAILS_STORAGE_KEY)!)).toEqual({ version: 1, excluded: ['prompt', 'started'] });

        const restored = TestBed.createComponent(AdminBenchmarkComponent);
        expect(restored.componentInstance.imageDetailExclusions).toEqual(['prompt', 'started']);
        restored.destroy();
      });
    });
  });
});
