import type { Mock, MockedObject } from "vitest";
import { ComponentFixture, fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import { AdminBenchmarkService } from '../../services/admin-benchmark.service';
import {
  AdminBenchmarkSpecContext, clearStoredState, createAdminBenchmarkFixture, RUN_SETTINGS_KEY
} from './benchmark.component.testing';

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

  // ---------------------------------------------------------------------------
  // Layout regression guards: the launcher's three groups and the fields each holds, and the
  // run-model-strip in the progress dialog shares one column edge between rows.
  // ---------------------------------------------------------------------------
  describe('model selector row layout', () => {
    /** The text of each direct field's first label in one launcher group, whitespace-normalized. */
    function groupLabels(group: string): string[] {
      const fieldset = fixture.nativeElement.querySelector(`.setup-group-${group}`) as HTMLElement;
      return (Array.from(fieldset.querySelectorAll(':scope > .form-group')) as HTMLElement[])
        .map(g => (g.querySelector('label')?.textContent ?? '').replace(/\s+/g, ' ').trim());
    }

    it('should lay the launcher out as three setup groups in order', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      const groups = Array.from(
        fixture.nativeElement.querySelectorAll('.setup-groups > fieldset.setup-group')
      ) as HTMLElement[];
      expect(groups.length).toBe(3);
      expect(groups.map(g => g.querySelector('legend')?.textContent?.trim()))
        .toEqual(['Test Setup', 'Grading', 'Execution']);
    });

    it('should hold the Test Setup fields in order', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      const labels = groupLabels('test');
      expect(labels.length).toBe(4);
      expect(labels[0]).toMatch(/^Benchmark Suite/);
      expect(labels[1]).toMatch(/^Scoring Profile/);
      expect(labels[2]).toMatch(/^Response Style/);
      expect(labels[3]).toMatch(/^Source Code References/);
    });

    it('should lift the Model Under Test into a primary field above the setup groups', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      const card = fixture.nativeElement.querySelector('.setup-card') as HTMLElement;
      const primary = card.querySelector('.setup-primary-field') as HTMLElement;
      const groups = card.querySelector('.setup-groups-container') as HTMLElement;
      expect(primary).toBeTruthy();
      expect(groups).toBeTruthy();
      expect(primary.compareDocumentPosition(groups) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(primary.closest('fieldset')).toBeNull();
      expect(primary.querySelector('#bmTestedModelLabel')?.textContent?.trim()).toBe('Model Under Test');
      expect(primary.querySelector('.tested-model-selector')).toBeTruthy();
      expect(card.querySelector('fieldset .tested-model-selector')).toBeNull();
    });

    it('should hold the Grading fields in order, with the optional ones tagged', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      const labels = groupLabels('grading');
      expect(labels.length).toBe(5);
      expect(labels[0]).toBe('Assessor');
      expect(labels[1]).toMatch(/^Co-Assessor/);
      expect(labels[2]).toMatch(/^Second Reader/);
      expect(labels[3]).toMatch(/^Claim Verifier/);
      expect(labels[4]).toMatch(/^Report Writer/);

      for (const id of ['bmCoAssessorModelLabel', 'bmSecondOpinionModelLabel', 'bmClaimVerifierModelLabel', 'bmReportWriterModelLabel']) {
        expect(fixture.nativeElement.querySelector(`#${id} .field-optional`)?.textContent?.trim()).toBe('Optional');
      }
      expect(fixture.nativeElement.querySelector('#bmAssessorModelLabel .field-optional')).toBeNull();

      // Second Opinion Mode depends on the second opinion model, so it hangs off that field.
      const mode = fixture.nativeElement.querySelector('#secondOpinionModeSelect') as HTMLElement;
      const dependent = mode.closest('.dependent-field') as HTMLElement;
      expect(dependent).toBeTruthy();
      expect(dependent.parentElement!.querySelector(':scope > #bmSecondOpinionModelLabel')).toBeTruthy();

      expect(fixture.nativeElement.querySelector('#bmCoAssessorModelHint')?.textContent)
        .toContain('the score is the mean of the two');
    });

    it('should name and describe each launcher picker by its own field', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      const pickers: [string, string][] = [
        ['tested-model-selector', 'bmTestedModel'],
        ['assessor-model-selector', 'bmAssessorModel'],
        ['co-assessor-model-selector', 'bmCoAssessorModel'],
        ['second-opinion-model-selector', 'bmSecondOpinionModel'],
        ['claim-verifier-model-selector', 'bmClaimVerifierModel']
      ];
      for (const [marker, prefix] of pickers) {
        const trigger = fixture.nativeElement.querySelector(`.${marker} .selector-trigger`) as HTMLButtonElement;
        expect(trigger, marker).toBeTruthy();
        expect(trigger.getAttribute('aria-labelledby')!.startsWith(`${prefix}Label `), marker).toBe(true);
        expect(trigger.getAttribute('aria-describedby'), marker).toBe(`${prefix}Hint`);
      }
    });

    it('should render both dt/dd pairs and keep .run-model-row present in the run-model-strip', () => {
      ctx.monitor.activeRunDetail = {
        id: 42,
        status: 'Running',
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-3.7-flash',
        testedModelParallelExecutionModeUsed: 2,
        assessorModelDisplayNameUsed: 'GPT-5.6 Luna',
        assessorModelProviderUsed: 'OpenAI',
        assessorModelIdUsed: 'gpt-5.6-luna',
        totalQuestionCount: 10,
        answers: []
      } as any;
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const strip = fixture.nativeElement.querySelector('.run-model-strip');
      expect(strip).toBeTruthy();

      const rows = strip.querySelectorAll('.run-model-row');
      expect(rows.length).toBe(2);

      const dts = strip.querySelectorAll('dt');
      const dds = strip.querySelectorAll('dd');
      expect(dts.length).toBe(2);
      expect(dds.length).toBe(2);
      expect(dts[0].textContent?.trim()).toBe('Model under test');
      expect(dds[0].textContent).toContain('Gemini 3.7 Flash');
      expect(dts[1].textContent?.trim()).toBe('Assessor');
      expect(dds[1].textContent).toContain('GPT-5.6 Luna');

      // The alignment itself comes from the grid CSS (max-content / minmax(0, 1fr)),
      // which a unit test cannot assert — only that the markup it depends on is present.
    });

    // H6. The Endpoint row under Model under test follows the same rule as the grader rows
    // below it: the official endpoint is the assumed default and prints nothing extra.
    it('should render no Endpoint row for the official endpoint', () => {
      ctx.monitor.activeRunDetail = {
        id: 42,
        status: 'Running',
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-3.7-flash',
        testedModelParallelExecutionModeUsed: 2,
        testedModelEndpoint: 'official',
        assessorModelDisplayNameUsed: 'GPT-5.6 Luna',
        assessorModelProviderUsed: 'OpenAI',
        assessorModelIdUsed: 'gpt-5.6-luna',
        totalQuestionCount: 10,
        answers: []
      } as any;
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const strip = fixture.nativeElement.querySelector('.run-model-strip');
      const dts = Array.from(strip.querySelectorAll('dt')) as HTMLElement[];
      expect(dts.map(dt => dt.textContent?.trim())).not.toContain('Endpoint');
    });

    it('should render the Endpoint row for a custom endpoint', () => {
      ctx.monitor.activeRunDetail = {
        id: 42,
        status: 'Running',
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-3.7-flash',
        testedModelParallelExecutionModeUsed: 2,
        testedModelEndpoint: 'custom (contoso.example; fingerprint ab12cd34)',
        assessorModelDisplayNameUsed: 'GPT-5.6 Luna',
        assessorModelProviderUsed: 'OpenAI',
        assessorModelIdUsed: 'gpt-5.6-luna',
        totalQuestionCount: 10,
        answers: []
      } as any;
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const strip = fixture.nativeElement.querySelector('.run-model-strip');
      const dts = Array.from(strip.querySelectorAll('dt')) as HTMLElement[];
      const endpointIndex = dts.findIndex(dt => dt.textContent?.trim() === 'Endpoint');
      expect(endpointIndex).toBeGreaterThan(-1);
      const dds = strip.querySelectorAll('dd');
      expect(dds[endpointIndex].textContent).toContain('custom (contoso.example; fingerprint ab12cd34)');
    });

    it('should give every roster badge a spoken prefix and no title attribute', () => {
      ctx.monitor.activeRunDetail = {
        id: 42,
        status: 'Running',
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-3.7-flash',
        testedModelReasoningModeUsed: 'pro',
        testedModelServiceTierUsed: 'flex',
        testedModelParallelExecutionModeUsed: 0,
        assessorModelDisplayNameUsed: 'GPT-5 Mini',
        assessorModelProviderUsed: 'OpenAI',
        assessorModelIdUsed: 'gpt-5-mini',
        isPanelRun: true,
        coAssessorModelDisplayNameUsed: 'Claude Opus 4',
        coAssessorModelProviderUsed: 'Anthropic',
        coAssessorModelIdUsed: 'claude-opus-4',
        secondOpinionAssessorModelDisplayNameUsed: 'Gemini 3.7 Pro',
        secondOpinionAssessorModelProviderUsed: 'Google',
        secondOpinionAssessorModelIdUsed: 'gemini-3.7-pro',
        secondOpinionModeUsed: 3,
        claimVerifierDisplayNameUsed: 'GPT-5 Nano',
        claimVerifierProviderUsed: 'OpenAI',
        claimVerifierModelIdUsed: 'gpt-5-nano',
        totalQuestionCount: 10,
        answers: []
      } as any;
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const strip = fixture.nativeElement.querySelector('.run-model-strip') as HTMLElement;
      const text = (el: Element | null | undefined) => el?.textContent?.trim();
      expect(strip.querySelectorAll('[title]').length).toBe(0);
      expect(strip.querySelector('.tier-badge')).toBeNull();
      expect(strip.querySelector('.second-opinion-mode-badge')).toBeNull();
      expect(text(strip.querySelector('.parallel-badge'))).toBe('parallel execution Sequential, disabled for this key');
      expect(text(strip.querySelector('.reasoning-badge'))).toBe('reasoning mode pro');

      const dts = Array.from(strip.querySelectorAll('dt')) as HTMLElement[];
      const readerIndex = dts.findIndex(dt => dt.textContent?.trim() === 'Reference reader');
      expect(readerIndex).toBeGreaterThan(-1);
      const readerRow = strip.querySelectorAll('dd')[readerIndex];
      expect(text(readerRow.querySelector('.config-badge'))).toBe('coverage Every answer, blind (reference reading)');
      expect(readerRow.querySelector('app-info-tip .gh-info-btn')?.getAttribute('aria-label')).toBe('About Reference reader coverage');
      expect(fixture.nativeElement.querySelector('#runProgressCoverageTip')?.textContent).toContain('never scores');
    });

    it('should render second opinion assessor row under Assessor with selected mode when configured', () => {
      ctx.monitor.activeRunDetail = {
        id: 42,
        status: 'Running',
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-3.7-flash',
        testedModelParallelExecutionModeUsed: 2,
        assessorModelDisplayNameUsed: 'GPT-5.6 Luna',
        assessorModelProviderUsed: 'OpenAI',
        assessorModelIdUsed: 'gpt-5.6-luna',
        secondOpinionAssessorModelDisplayNameUsed: 'Claude Opus 5',
        secondOpinionAssessorModelProviderUsed: 'Anthropic',
        secondOpinionAssessorModelIdUsed: 'claude-opus-5',
        secondOpinionAssessorModelThinkingLevelUsed: 'high',
        secondOpinionModeUsed: 1, // Only flagged answers
        totalQuestionCount: 10,
        answers: []
      } as any;
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const strip = fixture.nativeElement.querySelector('.run-model-strip');
      expect(strip).toBeTruthy();

      const rows = strip.querySelectorAll('.run-model-row');
      expect(rows.length).toBe(3);

      const dts = strip.querySelectorAll('dt');
      const dds = strip.querySelectorAll('dd');
      expect(dts.length).toBe(3);
      expect(dds.length).toBe(3);
      expect(dts[0].textContent?.trim()).toBe('Model under test');
      expect(dts[1].textContent?.trim()).toBe('Assessor');
      expect(dts[2].textContent?.trim()).toBe('Second reader');

      expect(dds[2].textContent).toContain('Claude Opus 5');
      expect(dds[2].querySelector('.thinking-badge')?.textContent?.trim()).toBe('thinking level High');
      expect(dds[2].querySelector('.provider-badge')?.textContent?.trim()).toBe('Anthropic');
      const modeBadge = dds[2].querySelector('.config-badge');
      expect(modeBadge).toBeTruthy();
      expect(modeBadge?.textContent?.trim()).toBe('coverage Only flagged answers');
      expect(modeBadge?.hasAttribute('title')).toBe(false);
      expect(dds[2].querySelector('app-info-tip .gh-info-btn')?.getAttribute('aria-label')).toBe('About Second reader coverage');
      expect(fixture.nativeElement.querySelector('#runProgressCoverageTip')?.textContent).toContain('raised a flag');
    });

    it('should not render second opinion row if mode is Off (0)', () => {
      ctx.monitor.activeRunDetail = {
        id: 42,
        status: 'Running',
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-3.7-flash',
        assessorModelDisplayNameUsed: 'GPT-5.6 Luna',
        assessorModelProviderUsed: 'OpenAI',
        assessorModelIdUsed: 'gpt-5.6-luna',
        secondOpinionAssessorModelDisplayNameUsed: 'Claude Opus 5',
        secondOpinionAssessorModelProviderUsed: 'Anthropic',
        secondOpinionAssessorModelIdUsed: 'claude-opus-5',
        secondOpinionModeUsed: 0,
        totalQuestionCount: 10,
        answers: []
      } as any;
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const strip = fixture.nativeElement.querySelector('.run-model-strip');
      expect(strip).toBeTruthy();
      const rows = strip.querySelectorAll('.run-model-row');
      expect(rows.length).toBe(2);
    });

    it('should add a Co-assessor row after Assessor and call the second opinion the reference reader in a panel run', () => {
      ctx.monitor.activeRunDetail = {
        id: 42,
        status: 'Running',
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-3.7-flash',
        testedModelParallelExecutionModeUsed: 2,
        assessorModelDisplayNameUsed: 'GPT-5 Mini',
        assessorModelProviderUsed: 'OpenAI',
        assessorModelIdUsed: 'gpt-5-mini',
        isPanelRun: true,
        coAssessorModelDisplayNameUsed: 'Claude Opus 4',
        coAssessorModelProviderUsed: 'Anthropic',
        coAssessorModelIdUsed: 'claude-opus-4',
        coAssessorModelThinkingLevelUsed: 'high',
        secondOpinionAssessorModelDisplayNameUsed: 'Gemini 3.7 Pro',
        secondOpinionAssessorModelProviderUsed: 'Google',
        secondOpinionAssessorModelIdUsed: 'gemini-3.7-pro',
        secondOpinionModeUsed: 3,
        totalQuestionCount: 10,
        answers: []
      } as any;
      ctx.monitor.isRunProgressDialogOpen = true;
      ctx.refresh();

      const strip = fixture.nativeElement.querySelector('.run-model-strip');
      const dts = Array.from(strip.querySelectorAll('dt')) as HTMLElement[];
      expect(dts.map(dt => dt.textContent?.trim())).toEqual(['Model under test', 'Assessor', 'Co-assessor', 'Reference reader']);

      const dds = strip.querySelectorAll('dd');
      expect(dds[2].textContent).toContain('Claude Opus 4');
      expect(dds[2].querySelector('.thinking-badge')?.textContent?.trim()).toBe('thinking level High');
      expect(dds[2].querySelector('.provider-badge')?.textContent?.trim()).toBe('Anthropic');
    });

    it('should format second opinion modes and hints correctly', () => {
      expect(component.formatSecondOpinionMode(0)).toBe('');
      expect(component.formatSecondOpinionMode(1)).toBe('Only flagged answers');
      expect(component.formatSecondOpinionMode(2)).toBe('Flagged answers and statistical outliers');
      expect(component.formatSecondOpinionMode(3)).toBe('Every answer (double grading)');

      expect(component.secondOpinionModeHintOf(1)).toContain('raised a flag');
      expect(component.secondOpinionModeHintOf(3)).toContain('unbiased measure of grading reliability');
    });

    it('should explain what the Model Under Test and the Assessor each do', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('#bmTestedModelHint')?.textContent).toContain('Answers every question');
      expect(fixture.nativeElement.querySelector('#bmAssessorModelHint')?.textContent)
        .toContain('four dimensions');
    });

    it('should describe the benchmark suite and the scoring profile', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      // Both controls decide what a run's numbers mean, and both were undescribed: the suite had
      // no hint at all, and the profile could only ever show the conditional fit advisory.
      const suiteHint = fixture.nativeElement.querySelector('#suiteHint') as HTMLElement | null;
      expect(suiteHint).toBeTruthy();
      expect(suiteHint!.textContent).toContain('only comparable within one suite');

      const profileHint = fixture.nativeElement.querySelector('#profileHint') as HTMLElement | null;
      expect(profileHint).toBeTruthy();
      expect(profileHint!.textContent).toContain('Turns the four dimension grades into indices');
    });

    it('should say what a second run buys rather than referring to previous behaviour', () => {
      component.activeSubTab = 'run';
      ctx.workspace.runLimits = {
        maxRunsPerHour: 4,
        maxRunsPerDay: 20,
        runsInLastHour: 0,
        runsInLast24Hours: 0,
        remainingDailyHeadroom: 20,
        maxRunCountPerSeries: 20
      };
      ctx.refresh();

      const hint = fixture.nativeElement.querySelector('#runCountHint') as HTMLElement | null;
      expect(hint).toBeTruthy();
      const text = (hint!.textContent ?? '').replace(/\s+/g, ' ').trim();

      expect(text).toContain('replicate set');
      expect(text).toContain('run-to-run noise');
      expect(text).toContain('20');
      expect(text).toContain('One run');
      expect(text).toContain('Two or more runs');

      // The regression this wording exists to prevent: "exactly as before" described the
      // pre-multi-run implementation, which tells an operator nothing about the field.
      expect(text).not.toContain('as before');
    });
  });

  describe('launcher info buttons and notes', () => {
    function card(): HTMLElement {
      return fixture.nativeElement.querySelector('.setup-card') as HTMLElement;
    }

    beforeEach(() => {
      component.activeSubTab = 'run';
    });

    it('should keep each field hint in a click-mode popup beside its control', () => {
      fixture.detectChanges();

      const hints: [string, string][] = [
        ['suiteHint', '#suiteSelect'],
        ['profileHint', '#profileSelect'],
        ['bmTestedModelHint', '.tested-model-selector'],
        ['bmAssessorModelHint', '.assessor-model-selector'],
        ['bmCoAssessorModelHint', '.co-assessor-model-selector'],
        ['runCountHint', '#runCountInput']
      ];
      for (const [id, controlSelector] of hints) {
        const hint = card().querySelector(`#${id}`) as HTMLElement;
        expect(hint, id).toBeTruthy();
        const popup = hint.closest('.gh-info-popup') as HTMLElement;
        expect(popup, id).toBeTruthy();
        expect(popup.getAttribute('popover'), id).toBe('auto');

        const tip = popup.closest('app-info-tip') as HTMLElement;
        const control = card().querySelector(controlSelector) as HTMLElement;
        expect(control.parentElement!.classList, id).toContain('gh-field-row');
        expect(control.nextElementSibling, id).toBe(tip);
      }
    });

    it('sets the Number of Runs intro and closing note off from its entries', () => {
      fixture.detectChanges();

      const hint = card().querySelector('#runCountHint') as HTMLElement;
      const ruleOf = (selector: string) => getComputedStyle(hint.querySelector(selector)!).borderTopStyle;
      expect(ruleOf('dl > div:first-child')).toBe('solid');
      expect(ruleOf('dl + p')).toBe('solid');
      expect(ruleOf('p')).toBe('none');
    });

    it('themes every launcher checkbox with the global checkbox-label', () => {
      ctx.launcher.runCount = 2;
      ctx.refresh();

      expect(card().querySelectorAll('.gh-checkbox').length).toBe(0);
      for (const id of ['allowCapWaitInput', 'completionSoundInput', 'completionNotificationInput']) {
        const label = card().querySelector(`label[for="${id}"]`) as HTMLElement;
        expect(label, id).toBeTruthy();
        expect(label.classList, id).toContain('checkbox-label');
      }
      expect(getComputedStyle(card().querySelector('#completionSoundInput')!).width).toBe('20px');
    });

    it('puts the Completion Alerts (i) right after its caption', () => {
      fixture.detectChanges();

      const caption = card().querySelector('#completionSignalsCaption') as HTMLElement;
      const tip = caption.nextElementSibling as HTMLElement;
      expect(tip.tagName.toLowerCase()).toBe('app-info-tip');
      expect(caption.parentElement!.classList).not.toContain('gh-field-row');

      const button = tip.querySelector('button') as HTMLElement;
      expect(button.getBoundingClientRect().left - caption.getBoundingClientRect().right).toBeLessThanOrEqual(12);
    });

    it('groups the series option under a Series Options legend', () => {
      ctx.launcher.runCount = 2;
      ctx.refresh();

      const caption = card().querySelector('#execOptionsCaption') as HTMLElement;
      expect(caption.tagName).toBe('LEGEND');
      expect((caption.textContent ?? '').trim()).toBe('Series Options');
      const group = caption.parentElement as HTMLElement;
      expect(group.matches('fieldset.exec-options')).toBe(true);
      expect(Array.from(group.querySelectorAll('input[type="checkbox"]')).map(i => i.id)).toEqual(['allowCapWaitInput']);
      expect(getComputedStyle(group).rowGap).toBe('8px');
      expect(getComputedStyle(caption).fontWeight).toBe(
        getComputedStyle(card().querySelector('#completionSignalsCaption')!).fontWeight);

      const row = group.querySelector('.checkbox-with-tip') as HTMLElement;
      const gap = row.getBoundingClientRect().top - caption.getBoundingClientRect().bottom;
      expect(gap).toBeGreaterThanOrEqual(6);
      expect(gap).toBeLessThanOrEqual(10);
    });

    it('explains completion alerts in plain terms', () => {
      fixture.detectChanges();

      const hint = card().querySelector('#completionSignalsHint') as HTMLElement;
      const text = (hint.textContent ?? '').replace(/\s+/g, ' ');
      expect(text).toContain('Test sound');
      expect(text).toContain('permission');
      expect(text).toContain('up to a minute');
      expect(text).not.toContain('Armed');
    });

    it('gives Execution headings one style, heavier than option text', () => {
      fixture.detectChanges();

      const runCountWeight = getComputedStyle(card().querySelector('label[for="runCountInput"]')!).fontWeight;
      const captionWeight = getComputedStyle(card().querySelector('#completionSignalsCaption')!).fontWeight;
      expect(runCountWeight).toBe('600');
      expect(captionWeight).toBe(runCountWeight);
      expect(getComputedStyle(card().querySelector('label[for="completionSoundInput"]')!).fontWeight).toBe('400');
    });

    it('should show no compliance box and no fieldset purpose lines', () => {
      fixture.detectChanges();

      expect(card().querySelector('.compliance-purpose-box')).toBeNull();
      expect(card().textContent).not.toContain('Evaluation Purpose');
      expect(card().querySelector('.gh-fieldset-hint')).toBeNull();
    });

    // Each state is set on the launcher and announced with ctx.refresh(): the run tab is OnPush,
    // so fixture.detectChanges() alone does not refresh the launcher's conditional branches.
    it('should hide the Response Style note while Concise is selected', () => {
      ctx.launcher.candidateVerboseMode = false;
      ctx.refresh();

      expect(card().querySelector('#candidateResponseStyleNote')).toBeNull();
      expect(card().querySelector('#candidateResponseStyle')!.getAttribute('aria-describedby'))
        .toBe('candidateResponseStyleHint');
    });

    it('should show the Response Style note while Detailed is selected', () => {
      ctx.launcher.candidateVerboseMode = true;
      ctx.refresh();

      expect(card().querySelector('#candidateResponseStyleNote')?.textContent).toContain('Only Accuracy stays comparable');
      expect(card().querySelector('#candidateResponseStyle')!.getAttribute('aria-describedby'))
        .toBe('candidateResponseStyleHint candidateResponseStyleNote');
    });

    it('should offer Source Code References beside Response Style, Disallowed first and by default', () => {
      fixture.detectChanges();

      const select = card().querySelector('#candidateSourceCodeReferences') as HTMLSelectElement;
      expect(select).toBeTruthy();
      expect(select.closest('fieldset')).toBe(card().querySelector('#candidateResponseStyle')!.closest('fieldset'));
      expect((card().querySelector('label[for="candidateSourceCodeReferences"]')?.textContent || '').trim())
        .toBe('Source Code References');
      expect(Array.from(select.options).map(o => o.textContent?.trim())).toEqual([
        'Disallowed — production default',
        'Allowed — answers cite source files and lines'
      ]);
      expect(ctx.launcher.candidateAllowSourceCodeReferences).toBe(false);
      expect(select.getAttribute('aria-describedby')).toBe('candidateSourceCodeReferencesHint');

      const hint = card().querySelector('#candidateSourceCodeReferencesHint') as HTMLElement;
      expect(hint.closest('.gh-info-popup')?.getAttribute('popover')).toBe('auto');
      expect(hint.textContent).toContain('Show source code references');
      expect(select.parentElement!.classList).toContain('gh-field-row');
    });

    it('should show the Second Opinion Mode reason while the mode is disabled', () => {
      ctx.launcher.secondOpinionConfigId = null;
      ctx.refresh();

      expect(card().querySelector('#secondOpinionModeHint')?.textContent).toContain('Choose a second reader to set its coverage');
      expect(card().querySelector('#secondOpinionModeSelect')!.getAttribute('aria-describedby'))
        .toBe('secondOpinionModeTip secondOpinionModeHint');
    });

    it('should hide the Second Opinion Mode reason while the mode is enabled', () => {
      ctx.launcher.secondOpinionConfigId = 1;
      ctx.refresh();

      expect(card().querySelector('#secondOpinionModeHint')).toBeNull();
      expect(card().querySelector('#secondOpinionModeSelect')!.getAttribute('aria-describedby'))
        .toBe('secondOpinionModeTip');
    });

    it('should show no same-model note without a second opinion', () => {
      ctx.launcher.assessorConfigId = 1;
      ctx.launcher.secondOpinionConfigId = null;
      ctx.refresh();

      expect(card().querySelector('#bmSecondOpinionSameModelNote')).toBeNull();
      expect(card().querySelector('.second-opinion-model-selector .selector-trigger')!.getAttribute('aria-describedby'))
        .toBe('bmSecondOpinionModelHint');
    });

    it('should show the same-model note when the second opinion is the assessor', () => {
      ctx.launcher.assessorConfigId = 1;
      ctx.launcher.secondOpinionConfigId = 1;
      ctx.refresh();

      expect(card().querySelector('#bmSecondOpinionSameModelNote')?.textContent)
        .toContain('Same model as the assessor');
      expect(card().querySelector('.second-opinion-model-selector .selector-trigger')!.getAttribute('aria-describedby'))
        .toBe('bmSecondOpinionModelHint bmSecondOpinionSameModelNote');
    });

    it('should list every coverage option in its popup', () => {
      fixture.detectChanges();

      const tip = card().querySelector('#secondOpinionModeTip') as HTMLElement;
      expect(tip.closest('.gh-info-popup')).toBeTruthy();
      expect(tip.closest('.gh-info-popup')?.querySelector('.gh-info-popup-title')?.textContent?.trim()).toBe('Coverage');
      expect(card().querySelector('label[for="secondOpinionModeSelect"]')?.textContent?.trim()).toBe('Coverage');
      const terms = Array.from(tip.querySelectorAll('dt .gh-info-term')).map(t => (t.textContent ?? '').trim());
      expect(terms).toEqual(component.secondOpinionModeOptions.map(o => o.label));

      const badges = Array.from(tip.querySelectorAll('dt .gh-info-badge'));
      expect(badges.length).toBe(1);
      expect(badges[0].closest('dt')?.querySelector('.gh-info-term')?.textContent?.trim())
        .toBe('Every answer (double grading)');
      expect(tip.textContent).toContain('No coverage setting changes a score');
    });

    it('should end each grader popup with a recommendation', () => {
      fixture.detectChanges();

      for (const tipId of ['bmAssessorModelHint', 'bmCoAssessorModelHint', 'bmSecondOpinionModelHint', 'bmClaimVerifierModelHint']) {
        const tip = card().querySelector(`#${tipId}`) as HTMLElement;
        expect(Array.from(tip.querySelectorAll('p > strong')).map(s => s.textContent), tipId).toContain('Recommended:');
        expect(tip.textContent, tipId).toContain('More: How the graders work.');
      }
      const reader = card().querySelector('#bmSecondOpinionModelHint') as HTMLElement;
      expect(reader.textContent).toContain('A second model grades answers again');
      const verifier = card().querySelector('#bmClaimVerifierModelHint') as HTMLElement;
      expect(verifier.textContent).not.toContain('advisory, changes no score');
      expect(verifier.textContent).not.toContain('It never changes a score');
      expect(verifier.textContent).toContain('It changes no level.');
      expect(verifier.textContent).toContain('settles a split between the two panel members');
    });

    it('should open the grader guide at the roles overview from the Grading group', () => {
      fixture.detectChanges();

      const open = vi.spyOn(component.graderGuide!, 'open').mockReturnValue(undefined);
      const button = card().querySelector('.setup-group-grading .grader-guide-btn') as HTMLButtonElement;
      expect(button.textContent?.trim()).toBe('How the graders work');
      button.click();
      expect(open).toHaveBeenCalledWith('roles');
      expect(component.graderGuideProfile).toBe(ctx.launcher.selectedScoringProfile ?? null);
    });

    it('should offer an optional Report Writer after the Claim Verifier, with its hint in a click-mode popup', () => {
      fixture.detectChanges();

      const grading = card().querySelector('.setup-group-grading') as HTMLElement;
      const labels = Array.from(grading.querySelectorAll(':scope > .form-group > label'))
        .map(label => (label.textContent ?? '').replace(/\s+/g, ' ').trim());
      expect(labels.slice(-2)).toEqual(['Claim Verifier Optional', 'Report Writer Optional']);

      const picker = grading.querySelector('.report-writer-model-selector') as HTMLElement;
      const trigger = picker.querySelector('.selector-trigger') as HTMLElement;
      expect(trigger.getAttribute('aria-labelledby')).toContain('bmReportWriterModelLabel');
      expect(trigger.getAttribute('aria-describedby')).toBe('bmReportWriterModelHint');
      expect(trigger.textContent).toContain('None — no AI-written reports');
      expect(ctx.launcher.reportWriterConfigId).toBeNull();

      const hint = card().querySelector('#bmReportWriterModelHint') as HTMLElement;
      expect(hint.closest('.gh-info-popup')?.getAttribute('popover')).toBe('auto');
      expect(picker.parentElement!.classList).toContain('gh-field-row');
      expect(picker.nextElementSibling).toBe(hint.closest('app-info-tip'));
      const text = (hint.textContent ?? '').replace(/\s+/g, ' ');
      expect(text).toContain('this model writes an Executive Summary and a Report for AI Researchers and Developers once');
      expect(text).toContain('Downloads render them without calling it again');
      expect(text).toContain('from another provider than the model under test');
    });

    it('should warn about a report writer of the candidate\'s provider before Start, without holding Start back', () => {
      component.systemConfigs = [
        component.systemConfigs[0],
        { ...component.systemConfigs[0], id: 2, displayName: 'Other Claude', modelId: 'claude-other' }
      ];
      ctx.launcher.testedConfigId = 1;
      ctx.launcher.assessorConfigId = 1;
      ctx.launcher.reportWriterConfigId = 2;
      ctx.refresh();

      const warning = 'Other Claude is from Anthropic, the provider of the model under test. ' +
        'Its reports may describe that model more favorably.';
      expect(ctx.runTab().reportWriterLaunchRefusal).toBe('');
      expect(ctx.runTab().reportWriterLaunchWarning).toBe(warning);
      const advisory = card().querySelector('.setup-group-grading .report-writer-advisory') as HTMLElement;
      expect(advisory.classList).toContain('alert-warning');
      expect(advisory.querySelector('svg.alert-icon')?.getAttribute('aria-hidden')).toBe('true');
      expect(advisory.textContent?.replace(/\s+/g, ' ').trim()).toBe(warning);
      expect(card().querySelector('.setup-group-grading .report-writer-refusal')).toBeNull();
      expect(ctx.runTab().canStartRun).toBe(true);
      expect(ctx.runTab().startBenchmarkHint).toBe('');
    });

    it('should refuse the model under test as its own report writer in red and hold Start back', () => {
      ctx.launcher.testedConfigId = 1;
      ctx.launcher.assessorConfigId = 1;
      ctx.launcher.reportWriterConfigId = 1;
      ctx.refresh();

      const refusal = 'The model under test cannot write its own reports.';
      expect(ctx.runTab().reportWriterLaunchRefusal).toBe(refusal);
      expect(ctx.runTab().reportWriterLaunchWarning).toBe('');
      const line = card().querySelector('.setup-group-grading .report-writer-refusal') as HTMLElement;
      expect(line.classList).toContain('gh-field-error');
      expect(line.id).toBe('bmReportWriterRefusal');
      expect(line.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
      expect(line.textContent?.trim()).toBe(refusal);
      expect(card().querySelector('.setup-group-grading .report-writer-advisory')).toBeNull();
      const trigger = card().querySelector('.report-writer-model-selector .selector-trigger') as HTMLElement;
      expect(trigger.getAttribute('aria-describedby')).toBe('bmReportWriterModelHint bmReportWriterRefusal');
      expect(ctx.runTab().canStartRun).toBe(false);
      expect(ctx.runTab().startBenchmarkHint).toBe(refusal);

      ctx.launcher.reportWriterConfigId = null;
      expect(ctx.runTab().reportWriterLaunchRefusal).toBe('');
    });
  });

  describe('same-provider acknowledgments at launch', () => {
    const writerWarning = {
      sameProvider: true, provider: 'Anthropic', testedModelDisplayName: 'Test Model',
      assessorModelDisplayName: 'Other Claude', message: 'The report writer shares the provider.', role: 'reportWriter'
    };
    const assessorWarning = {
      sameProvider: true, provider: 'Anthropic', testedModelDisplayName: 'Test Model',
      assessorModelDisplayName: 'Test Model', message: 'The assessor shares the provider.', role: 'assessor'
    };

    function prepare(): Mock {
      component.activeSubTab = 'run';
      fixture.detectChanges();
      ctx.launcher.selectedSuiteId = 1;
      ctx.launcher.testedConfigId = 1;
      ctx.launcher.assessorConfigId = 1;
      vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      benchmarkServiceMock.getRun.mockReturnValue(of({ id: 42, answers: [] } as any));
      return vi.spyOn(ctx.runTab().sameProviderDialog.nativeElement, 'showModal').mockReturnValue(undefined);
    }

    function dialogHeading(): string {
      return ctx.runTab().sameProviderDialog.nativeElement.querySelector('h3')?.textContent?.trim() ?? '';
    }

    function dialogText(): string {
      return (ctx.runTab().sameProviderDialog.nativeElement.textContent ?? '').replace(/\s+/g, ' ');
    }

    function sentBodies(): any[] {
      return vi.mocked(benchmarkServiceMock.startRun).mock.calls.map(args => args[0]);
    }

    afterEach(() => fixture.destroy());

    it('should open the dialog in report-writer wording on the writer\'s 409 and re-send with the writer\'s flag', () => {
      const showModal = prepare();
      benchmarkServiceMock.startRun.mockReturnValueOnce(throwError(() => ({ status: 409, error: writerWarning }))).mockReturnValueOnce(of({ runId: 42 }));

      ctx.runTab().startBenchmark();
      fixture.detectChanges();

      expect(showModal).toHaveBeenCalledTimes(1);
      expect(ctx.runTab().sameProviderWarningIsReportWriter).toBe(true);
      expect(dialogHeading()).toBe('Same-Provider Report Writer');
      expect(dialogText()).toContain('Report Writer: Other Claude');
      expect(dialogText()).not.toContain('Assessor Model:');
      expect(dialogText()).toContain('The run\'s AI-written reports would be written by a model from the same provider ' +
        'as the model under test, which may describe it more favorably.');

      const confirm = (Array.from(ctx.runTab().sameProviderDialog.nativeElement.querySelectorAll('button')) as HTMLButtonElement[])
        .find(button => (button.textContent ?? '').includes('Acknowledge & Start Run'))!;
      confirm.click();

      const bodies = sentBodies();
      expect(bodies.length).toBe(2);
      expect(bodies[0].acknowledgeSameProvider).toBe(false);
      expect(bodies[0].acknowledgeSameProviderReportWriter).toBeUndefined();
      expect(bodies[1].acknowledgeSameProvider).toBe(false);
      expect(bodies[1].acknowledgeSameProviderReportWriter).toBe(true);
      // Per-run safety acknowledgments: neither is remembered with the launcher's settings.
      expect(localStorage.getItem(RUN_SETTINGS_KEY) ?? '').not.toContain('acknowledge');
    });

    it('should keep the assessor\'s acknowledgment when the report writer\'s warning follows it', () => {
      const showModal = prepare();
      benchmarkServiceMock.startRun.mockReturnValueOnce(throwError(() => ({ status: 409, error: assessorWarning }))).mockReturnValueOnce(throwError(() => ({ status: 409, error: writerWarning }))).mockReturnValueOnce(of({ runId: 42 }));

      ctx.runTab().startBenchmark();
      fixture.detectChanges();
      expect(ctx.runTab().sameProviderWarningIsReportWriter).toBe(false);
      expect(dialogHeading()).toBe('Same-Provider Assessment Warning');
      expect(dialogText()).toContain('Assessor Model: Test Model');

      ctx.runTab().confirmSameProviderRun();
      fixture.detectChanges();
      expect(ctx.runTab().sameProviderWarningIsReportWriter).toBe(true);
      expect(dialogHeading()).toBe('Same-Provider Report Writer');

      ctx.runTab().confirmSameProviderRun();

      const bodies = sentBodies();
      expect(bodies.length).toBe(3);
      expect(bodies[1].acknowledgeSameProvider).toBe(true);
      expect(bodies[1].acknowledgeSameProviderReportWriter).toBeUndefined();
      expect(bodies[2].acknowledgeSameProvider).toBe(true);
      expect(bodies[2].acknowledgeSameProviderReportWriter).toBe(true);
      expect(showModal).toHaveBeenCalled();
      expect(localStorage.getItem(RUN_SETTINGS_KEY) ?? '').not.toContain('acknowledge');
    });

    it('should start a new attempt without the acknowledgments of the last one', () => {
      prepare();
      benchmarkServiceMock.startRun.mockReturnValueOnce(throwError(() => ({ status: 409, error: writerWarning }))).mockReturnValueOnce(throwError(() => ({ status: 500, error: 'Boom' }))).mockReturnValueOnce(throwError(() => ({ status: 500, error: 'Boom' })));

      ctx.runTab().startBenchmark();
      ctx.runTab().confirmSameProviderRun();
      ctx.runTab().closeSameProviderDialog();
      ctx.runTab().startBenchmark();

      const bodies = sentBodies();
      expect(bodies.length).toBe(3);
      expect(bodies[1].acknowledgeSameProviderReportWriter).toBe(true);
      expect(bodies[2].acknowledgeSameProvider).toBe(false);
      expect(bodies[2].acknowledgeSameProviderReportWriter).toBeUndefined();
    });
  });

  describe('scoring profile fit advisory', () => {
    /** Re-points the Model Under Test at a config carrying the given thinking level. */
    function selectTestedModelWithThinkingLevel(level: string | null): void {
      component.systemConfigs = [{ ...component.systemConfigs[0], thinkingLevel: level }];
      ctx.launcher.testedConfigId = component.systemConfigs[0].id;
    }

    /**
     * The fit advisory specifically, by its own id — not the first .form-hint in the profile's
     * .form-group, which is the permanent description of what a scoring profile is.
     */
    function profileFitHintText(): string {
      const hint = fixture.nativeElement.querySelector('#profileFitHint') as HTMLElement | null;
      return (hint?.textContent ?? '').replace(/\s+/g, ' ').trim();
    }

    beforeEach(() => {
      component.activeSubTab = 'run';
    });

    it('should warn when a deliberating model is graded against an interactive latency profile', () => {
      // The default profile targets 2000 ms, which is well inside the interactive band.
      for (const level of ['high', 'max', 'Max', 'HIGH']) {
        selectTestedModelWithThinkingLevel(level);
        expect(ctx.runTab().showProfileFitAdvisory, level).toBe(true);
      }

      ctx.refresh();
      expect(profileFitHintText()).toContain('This profile targets interactive latency.');
      expect(profileFitHintText()).toContain('consider a Reasoning Agent profile');
    });

    it('should stay silent for a shallow thinking level or a profile with a slow speed target', () => {
      selectTestedModelWithThinkingLevel('low');
      expect(ctx.runTab().showProfileFitAdvisory).toBe(false);

      selectTestedModelWithThinkingLevel('max');
      ctx.workspace.scoringProfiles = [{ ...ctx.workspace.scoringProfiles[0], speedTargetMs: 30000 }];
      expect(ctx.runTab().showProfileFitAdvisory).toBe(false);

      ctx.refresh();
      expect(profileFitHintText()).toBe('');
    });

    it('should stay silent while either half of the pairing is unselected', () => {
      ctx.launcher.testedConfigId = null;
      ctx.launcher.selectedScoringProfileId = null;
      expect(ctx.runTab().showProfileFitAdvisory).toBe(false);

      // A model chosen, but no profile yet.
      selectTestedModelWithThinkingLevel('max');
      ctx.launcher.selectedScoringProfileId = null;
      expect(ctx.runTab().showProfileFitAdvisory).toBe(false);

      // A profile chosen, but no model yet.
      ctx.launcher.selectedScoringProfileId = 1;
      ctx.launcher.testedConfigId = null;
      expect(ctx.runTab().showProfileFitAdvisory).toBe(false);

      // A model with no thinking level at all is not a deliberating one.
      selectTestedModelWithThinkingLevel(null);
      expect(ctx.runTab().showProfileFitAdvisory).toBe(false);
    });
  });
  describe('second opinion mode', () => {
    /**
     * NgModel treats `disabled` as one of its own inputs and applies it through the form control
     * in a microtask, so the DOM property is not settled by the end of detectChanges. This must
     * be called inside fakeAsync, and `tick()` is what makes an assertion about it mean anything:
     * `whenStable()` never resolves here, because the component holds polling intervals.
     */
    function modeSelect(): HTMLSelectElement | null {
      component.activeSubTab = 'run';
      ctx.refresh();
      tick();
      fixture.detectChanges();
      return fixture.nativeElement.querySelector('#secondOpinionModeSelect') as HTMLSelectElement | null;
    }

    it('should offer the five modes in coverage order', fakeAsync(() => {
      const select = modeSelect();
      expect(select).toBeTruthy();

      // FlaggedPlusSample sits between the outlier mode and All because that is where it falls on
      // coverage: more than flagged-plus-outliers, less than every answer.
      const labels = Array.from(select!.querySelectorAll('option')).map(o => (o.textContent || '').trim());
      expect(labels).toEqual([
        'Never',
        'Only flagged answers',
        'Flagged answers and statistical outliers',
        'Flagged answers plus a sample',
        'Every answer (double grading)'
      ]);
      discardPeriodicTasks();
    }));

    it('should be disabled, with a reason, until a second opinion assessor is chosen', fakeAsync(() => {
      ctx.launcher.secondOpinionConfigId = null;
      const select = modeSelect();

      // The hard gate that silently produced the 2026-09-03 run's zero second verdicts: the
      // mode is inert without an assessor, so the control says so rather than looking set.
      expect(select!.disabled).toBe(true);
      expect(ctx.runTab().secondOpinionModeHint).toContain('Choose a second reader to set its coverage');
      discardPeriodicTasks();
    }));

    it('should enable and describe the selected mode once an assessor is chosen', fakeAsync(() => {
      ctx.launcher.secondOpinionConfigId = 1;
      ctx.launcher.secondOpinionMode = 3;
      const select = modeSelect();

      expect(select!.disabled).toBe(false);
      expect(ctx.runTab().secondOpinionModeHint).toContain('unbiased measure of grading reliability');
      discardPeriodicTasks();
    }));

    it('should default from the selected profile and be overridable for one run', () => {
      ctx.workspace.scoringProfiles = [{ ...ctx.workspace.scoringProfiles[0], secondOpinionMode: 2 }];
      ctx.launcher.selectedScoringProfileId = 1;
      expect(ctx.launcher.secondOpinionMode).toBe(2);

      ctx.launcher.secondOpinionMode = 3;
      expect(ctx.launcher.secondOpinionMode).toBe(3);
    });

    it('should send the mode only when an assessor is selected', fakeAsync(() => {
      const consoleError = vi.spyOn(console, 'error').mockReturnValue(undefined);
      ctx.launcher.selectedSuiteId = 1;
      ctx.launcher.testedConfigId = 1;
      ctx.launcher.assessorConfigId = 2;
      ctx.launcher.secondOpinionConfigId = null;
      benchmarkServiceMock.startRun.mockReturnValue(of({ runId: 7 }));
      benchmarkServiceMock.getRun.mockReturnValue(throwError(() => ({ status: 0 })));
      vi.spyOn(component.runProgressDialog.nativeElement, 'showModal').mockReturnValue(undefined);

      ctx.runTab().startBenchmark();
      expect(vi.mocked(benchmarkServiceMock.startRun).mock.lastCall![0].secondOpinionMode).toBeNull();

      ctx.launcher.secondOpinionConfigId = 3;
      ctx.launcher.secondOpinionMode = 3;
      ctx.runTab().startBenchmark();
      expect(vi.mocked(benchmarkServiceMock.startRun).mock.lastCall![0].secondOpinionMode).toBe(3);
      expect(consoleError).toHaveBeenCalledWith('Failed to poll run detail', expect.any(Object));

      ctx.monitor.stopPolling();
      discardPeriodicTasks();
    }));

    it('should enable the profile editor outlier delta for FlaggedAndOutliers only', () => {
      component.selectSubTab('profiles');
      fixture.detectChanges();
      const profilesTab = ctx.profilesTab();

      profilesTab.profileForm.secondOpinionMode = 1;
      expect(profilesTab.outlierDeltaEnabled).toBe(false);

      profilesTab.profileForm.secondOpinionMode = 3;
      expect(profilesTab.outlierDeltaEnabled).toBe(false);

      profilesTab.profileForm.secondOpinionMode = 2;
      expect(profilesTab.outlierDeltaEnabled).toBe(true);
    });

    it('should reject a non-positive outlier delta under FlaggedAndOutliers', () => {
      component.selectSubTab('profiles');
      fixture.detectChanges();
      const profilesTab = ctx.profilesTab();

      profilesTab.editingProfileId = null;
      profilesTab.profileForm = {
        ...profilesTab.profileForm,
        name: 'Outlier Profile',
        secondOpinionMode: 2,
        secondOpinionOutlierDeltaPoints: 0
      };

      profilesTab.saveProfile();

      expect(benchmarkServiceMock.createScoringProfile).not.toHaveBeenCalled();
      expect(profilesTab.profileValidationErrors.join(' ')).toContain('Outlier delta must be between 1 and 100');
    });
  });

  describe('assessor advisories', () => {
    it('should warn when the assessor and the second opinion share a provider', () => {
      component.systemConfigs = [
        { id: 1, displayName: 'Gemini Flash', modelId: 'gemini-3.7-flash', provider: 'Google', modelRole: 4, hasApiKey: true, isEnabled: true } as any,
        { id: 2, displayName: 'Gemini Pro', modelId: 'gemini-3.7-pro', provider: 'Google', modelRole: 4, hasApiKey: true, isEnabled: true } as any,
        { id: 3, displayName: 'Claude Opus 5', modelId: 'claude-opus-5', provider: 'Anthropic', modelRole: 4, hasApiKey: true, isEnabled: true } as any
      ];
      ctx.launcher.assessorConfigId = 1;
      ctx.launcher.secondOpinionConfigId = 2;
      expect(ctx.runTab().showAssessorPairingAdvisory).toBe(true);

      ctx.launcher.secondOpinionConfigId = 3;
      expect(ctx.runTab().showAssessorPairingAdvisory).toBe(false);
    });

    it('should warn when the assessor differs from the suite\'s last completed run', () => {
      ctx.launcher.assessorConfigId = 5;
      ctx.launcher.lastAssessor = {
        runId: 7,
        assessorModelConfigurationId: 2,
        assessorModelDisplayNameUsed: 'Gemini 3.7 Flash',
        assessorModelProviderUsed: 'Google'
      };
      expect(ctx.runTab().showAssessorChangeAdvisory).toBe(true);

      ctx.launcher.assessorConfigId = 2;
      expect(ctx.runTab().showAssessorChangeAdvisory).toBe(false);
    });

    it('should stay silent for a suite with no completed run to compare against', () => {
      ctx.launcher.assessorConfigId = 5;
      ctx.launcher.lastAssessor = {};
      expect(ctx.runTab().showAssessorChangeAdvisory).toBe(false);
    });
  });
});
