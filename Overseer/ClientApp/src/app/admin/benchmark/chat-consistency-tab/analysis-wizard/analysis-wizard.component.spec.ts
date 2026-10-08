import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';

import { CC_EMPTY_BATTERY_SCOPE, CcRunScope } from '../chat-consistency-scope';
import { CcAnalysisResult, CcBatteryRunRow, CcComparisonSet, CcRunRow } from '../chat-consistency.models';
import {
  CC_API,
  CC_BATTERY_SET_KEY,
  ccAnalysisResult,
  ccAnalysisSummary,
  ccAxis,
  ccBatteryMemberRows,
  ccBatteryRunRow,
  ccBatteryRunRows,
  ccComparisonSets,
  ccEventPoints,
  ccEventTimeline,
  ccRunRow,
  ccRunRows,
  ccTimeline,
  chatConsistencyTestProviders,
  textOf
} from '../chat-consistency-tab.testing';
import { CcAnalysisStep, CcAnalysisWizardComponent, launchPreset, periodsRefusal } from './analysis-wizard.component';

const DOCUMENTS_URL = '/api/admin/benchmark/report-documents';

/** The run table of the composite-event fixture: runs 201–206. */
function eventRows(): CcRunRow[] {
  return ccEventPoints().map(point => ccRunRow(point.runId, point.startedAtUtc));
}

describe('CcAnalysisWizardComponent', () => {
  let fixture: ComponentFixture<CcAnalysisWizardComponent>;
  let component: CcAnalysisWizardComponent;
  let http: HttpTestingController;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CcAnalysisWizardComponent],
      providers: chatConsistencyTestProviders()
    }).compileComponents();
    fixture = TestBed.createComponent(CcAnalysisWizardComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    el = fixture.nativeElement as HTMLElement;
    document.body.appendChild(el);
    fixture.componentRef.setInput('timeline', ccTimeline());
    fixture.componentRef.setInput('rows', ccRunRows());
    fixture.componentRef.setInput('axis', ccAxis());
    // The outer wizard always binds the step.
    fixture.componentRef.setInput('step', 'periods');
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify();
    fixture.destroy();
    el.remove();
  });

  function setStep(step: CcAnalysisStep): void {
    fixture.componentRef.setInput('step', step);
    fixture.detectChanges();
  }

  /** The Runs and controls body starts the re-grade panel, which asks whether a re-grade is already running. */
  function goToRuns(): void {
    setStep('runs');
    http.expectOne(`${CC_API}/regrade/job`).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
  }

  function setDay(id: string, value: string): void {
    const input = el.querySelector<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  const body = (step: CcAnalysisStep): HTMLElement | null => el.querySelector<HTMLElement>(`.cc-wiz-step[data-step="${step}"]`);
  const shownSteps = (): (string | null)[] =>
    Array.from(el.querySelectorAll<HTMLElement>('.cc-wiz-step')).filter(step => !step.hidden).map(step => step.getAttribute('data-step'));
  const checked = (period: string): (string | null)[] =>
    Array.from(el.querySelectorAll<HTMLInputElement>(`table[data-period="${period}"] .cc-wiz-run-check`))
      .filter(box => box.checked).map(box => box.closest('tr')!.getAttribute('data-run-id'));
  const checkedBatteries = (period: string): (string | null)[] =>
    Array.from(el.querySelectorAll<HTMLInputElement>(`table[data-period="${period}"] .cc-wiz-run-check`))
      .filter(box => box.checked).map(box => box.closest('tr')!.getAttribute('data-battery-run-id'));

  describe('steps', () => {
    it('renders only the body of the step it is given, without navigation of its own', () => {
      expect(shownSteps()).toEqual(['periods']);
      expect(body('runs')).toBeNull();
      expect(body('results')).toBeNull();
      expect(body('reports')).toBeNull();
      for (const selector of ['.gh-steps', '.cc-wiz-nav', '.cc-wiz-next', '.cc-wiz-back', '.cc-wiz-analyze', '#cc-wiz-step-title']) {
        expect(el.querySelector(selector), selector).toBeNull();
      }
    });

    it('keeps a visited step\'s body mounted and hidden while another step shows', () => {
      goToRuns();
      expect(shownSteps()).toEqual(['runs']);
      expect(body('periods')!.hidden).toBe(true);
      const regradePanel = el.querySelector('app-cc-regrade-panel');
      expect(regradePanel).not.toBeNull();

      setStep('periods');
      expect(shownSteps()).toEqual(['periods']);
      expect(body('runs')!.hidden).toBe(true);
      // The same panel is kept, so it does not ask for the re-grade job again.
      expect(el.querySelector('app-cc-regrade-panel')).toBe(regradePanel);
      expect(body('results')).toBeNull();
      expect(body('reports')).toBeNull();
    });

    it('shows an empty Results body until there is a result', () => {
      setStep('results');
      expect(shownSteps()).toEqual(['results']);
      expect(el.querySelector('app-cc-results-view')).toBeNull();
      expect(component.reachable('results')).toBe(false);
      expect(component.reachable('reports')).toBe(false);
    });
  });

  describe('periods', () => {
    it('starts from Launch vs last 14 days and shows Protocol V1 read-only', () => {
      expect(el.querySelector<HTMLInputElement>('input[name="cc-wiz-preset"][value="launch"]')!.checked).toBe(true);
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-bs')!.value).toBe('2026-09-01');
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-be')!.value).toBe('2026-09-14');
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-cs')!.value).toBe('2026-09-18');
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-ce')!.value).toBe('2026-10-01');
      const rows = Array.from(el.querySelectorAll<HTMLTableRowElement>('.cc-protocol-table tbody tr'))
        .map(row => Array.from(row.cells).map(cell => textOf(cell)));
      expect(rows).toEqual([
        ['P1 Quality', '±3 index points'], ['P2 Time to first answer text', '±15 %'], ['P3 Answer streaming rate', '±10 %'],
        ['P4 Work per turn', '±15 %'], ['P5 Cost per question', '±10 %']
      ]);
      expect(textOf(el.querySelector('.cc-wiz-protocol-facts'))).toContain('0.05, Holm-adjusted across P1–P5');
      expect(component.reachable('runs')).toBe(true);
    });

    it('refuses Runs and controls while the periods overlap, and says why', () => {
      setDay('cc-wiz-cs', '2026-09-10');
      expect(textOf(el.querySelector('.cc-wiz-periods-error'))).toBe('The comparison must start after the baseline ends.');
      expect(component.periodsError).toBe('The comparison must start after the baseline ends.');
      expect(component.reachable('runs')).toBe(false);
      expect(component.analyzeBlocked).toBe('The comparison must start after the baseline ends.');
      expect(el.querySelector<HTMLInputElement>('input[name="cc-wiz-preset"][value="custom"]')!.checked).toBe(true);
    });

    it('refuses Runs and controls while an override is invalid', () => {
      const p1 = el.querySelector<HTMLInputElement>('#cc-wiz-margin-P1')!;
      p1.value = '-1';
      p1.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(component.overridesError).toBe('The margin of P1 must be a positive number.');
      expect(textOf(el.querySelector('.cc-wiz-overrides-error'))).toBe('The margin of P1 must be a positive number.');
      expect(component.reachable('runs')).toBe(false);

      p1.value = '';
      p1.dispatchEvent(new Event('input'));
      const alpha = el.querySelector<HTMLInputElement>('#cc-wiz-alpha')!;
      alpha.value = '0.7';
      alpha.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(textOf(el.querySelector('.cc-wiz-overrides-error'))).toBe('α must lie strictly between 0 and 0.5.');
    });

    it('lists the composite Overseer events for Before vs after an Overseer change, keyed by group', () => {
      fixture.componentRef.setInput('timeline', ccEventTimeline());
      fixture.componentRef.setInput('rows', eventRows());
      fixture.componentRef.setInput('axis', ccAxis({ firstRunAtUtc: '2026-09-01T08:00:00Z', lastRunAtUtc: '2026-09-10T08:00:00Z' }));
      fixture.detectChanges();
      el.querySelector<HTMLInputElement>('input[name="cc-wiz-preset"][value="event"]')!.click();
      fixture.detectChanges();

      const options = Array.from(el.querySelectorAll<HTMLOptionElement>('#cc-wiz-event option'));
      expect(options.map(option => textOf(option))).toEqual([
        'E1 · 2026-09-03 · Changes under harness 27 (2 changes)',
        'E2 · 2026-09-05 · Harness 27 → 28 (2 changes)',
        'E3 · 2026-09-09 · Harness 28 → 29 (re-run 30) (2 changes)',
        'E4 · 2026-09-10 · Changes under harness 29 (2 changes)'
      ]);
      expect(options.map(option => option.value)).toEqual(['2026-09-03|27', '2026-09-05|28', '2026-09-09|29', '2026-09-10|29']);
      // The first composite is taken: the baseline runs up to its day, the comparison from it.
      expect(component.presetEventGroupKey).toBe('2026-09-03|27');
      expect(el.querySelector<HTMLSelectElement>('#cc-wiz-event')!.value).toBe('2026-09-03|27');
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-bs')!.value).toBe('2026-09-01');
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-be')!.value).toBe('2026-09-02');
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-cs')!.value).toBe('2026-09-03');
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-ce')!.value).toBe('2026-09-10');

      const select = el.querySelector<HTMLSelectElement>('#cc-wiz-event')!;
      select.value = '2026-09-05|28';
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      expect(component.presetEventGroupKey).toBe('2026-09-05|28');
      expect(el.querySelector<HTMLSelectElement>('#cc-wiz-event')!.value).toBe('2026-09-05|28');
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-be')!.value).toBe('2026-09-04');
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-cs')!.value).toBe('2026-09-05');
      expect(el.querySelector<HTMLInputElement>('input[name="cc-wiz-preset"][value="event"]')!.checked).toBe(true);
    });

    it('says when there is no Overseer change to be around', () => {
      fixture.componentRef.setInput('timeline', ccTimeline({ events: [] }));
      fixture.detectChanges();
      el.querySelector<HTMLInputElement>('input[name="cc-wiz-preset"][value="event"]')!.click();
      fixture.detectChanges();
      expect(el.querySelector('#cc-wiz-event')).toBeNull();
      expect(textOf(el.querySelector('.cc-wiz-preset-note'))).toBe('No Overseer change was detected in the timeline range.');
    });

    it('confirms on later data from the day after the last analysis of the subject', () => {
      fixture.componentRef.setInput('axis', ccAxis({ lastRunAtUtc: '2026-10-20T08:00:00Z' }));
      fixture.componentRef.setInput('analyses', [ccAnalysisSummary(7), ccAnalysisSummary(9, { createdAtUtc: '2026-09-01T00:00:00Z' })]);
      fixture.detectChanges();
      el.querySelector<HTMLInputElement>('input[name="cc-wiz-preset"][value="later"]')!.click();
      fixture.detectChanges();
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-bs')!.value).toBe('2026-09-01');
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-be')!.value).toBe('2026-09-14');
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-cs')!.value).toBe('2026-10-03');
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-ce')!.value).toBe('2026-10-20');
      expect(textOf(el.querySelector('.cc-wiz-preset-note'))).toContain('saved 2026-10-02 09:00 UTC');
    });

    it('asks for a model in step 1 when there is none', () => {
      fixture.componentRef.setInput('axis', null);
      fixture.detectChanges();
      expect(component.analyzeBlocked).toBe('Choose a model in step 1 first.');
      expect(component.reachable('runs')).toBe(false);
      expect(textOf(el.querySelector('.cc-wiz-subject'))).toContain('None chosen');
      expect(el.querySelector<HTMLInputElement>('input[name="cc-wiz-preset"][value="launch"]')!.disabled).toBe(true);
      expect(el.querySelector<HTMLInputElement>('input[name="cc-wiz-preset"][value="custom"]')!.checked).toBe(true);
    });
  });

  describe('runs and controls', () => {
    it('selects nothing until the outer wizard preselects, then the eligible runs and every matched control', () => {
      goToRuns();
      expect(checked('baseline')).toEqual([]);
      expect(checked('comparison')).toEqual([]);
      expect(component.analyzeBlocked).toBe('Select at least one baseline run.');

      component.preselectRuns();
      fixture.detectChanges();
      expect(checked('baseline')).toEqual(['101', '102', '103']);
      expect(checked('comparison')).toEqual(['104', '105', '106']);
      const controls = Array.from(el.querySelectorAll<HTMLInputElement>('.cc-wiz-control-check'));
      expect(controls.map(box => textOf(box.closest('label')))).toEqual(['Control run #201', 'Control run #202', 'Control run #205', 'Control run #206']);
      expect(controls.every(box => box.checked)).toBe(true);
      expect(component.analyzeBlocked).toBe('');
    });

    it('preselects the runs when they arrive after Runs and controls was entered', () => {
      const rows = ccRunRows();
      fixture.componentRef.setInput('rows', []);
      goToRuns();
      component.preselectRuns();
      fixture.detectChanges();
      expect(checked('baseline')).toEqual([]);

      fixture.componentRef.setInput('rows', rows);
      fixture.detectChanges();
      expect(checked('baseline')).toEqual(['101', '102', '103']);
      expect(checked('comparison')).toEqual(['104', '105', '106']);
    });

    it('keeps a hand-made selection when preselected again for the same periods, and starts over for new ones', () => {
      goToRuns();
      component.preselectRuns();
      fixture.detectChanges();
      el.querySelector<HTMLInputElement>('table[data-period="baseline"] tr[data-run-id="103"] .cc-wiz-run-check')!.click();
      fixture.detectChanges();
      expect(checked('baseline')).toEqual(['101', '102']);

      component.preselectRuns();
      fixture.detectChanges();
      expect(checked('baseline')).toEqual(['101', '102']);

      setDay('cc-wiz-be', '2026-09-13');
      component.preselectRuns();
      fixture.detectChanges();
      expect(checked('baseline')).toEqual(['101', '102', '103']);
    });

    it('previews the strata, the composite Overseer events in the span and the missing controls', () => {
      goToRuns();
      component.preselectRuns();
      fixture.detectChanges();
      expect(textOf(el.querySelector('.cc-wiz-strata'))).toBe('weekday 08–12 UTC');
      expect(Array.from(el.querySelectorAll('.cc-wiz-missing li')).map(item => textOf(item)))
        .toEqual(['Run #103 (Board Suite) has no matched control run.', 'Run #104 (Board Suite) has no matched control run.']);
      const groups = Array.from(el.querySelectorAll<HTMLElement>('.cc-wiz-event-groups > li'));
      expect(groups.map(group => group.getAttribute('data-group-key'))).toEqual(['2026-09-15|30']);
      expect(textOf(groups[0].querySelector('.cc-marker-tag.is-event'))).toBe('E1');
      expect(textOf(groups[0].querySelector('.cc-wiz-event-text'))).toBe('2026-09-15 · Changes under harness 30 · Tool guides');
      expect(textOf(el.querySelector('.cc-wiz-relaxed'))).toContain('caps grades at Indicated');
    });

    it('lists one line per composite event whose time falls in the span', () => {
      fixture.componentRef.setInput('timeline', ccEventTimeline());
      fixture.componentRef.setInput('rows', eventRows());
      fixture.detectChanges();
      setDay('cc-wiz-bs', '2026-09-04');
      setDay('cc-wiz-be', '2026-09-06');
      setDay('cc-wiz-cs', '2026-09-07');
      setDay('cc-wiz-ce', '2026-09-09');
      goToRuns();
      component.preselectRuns();
      fixture.detectChanges();

      const lines = () => Array.from(el.querySelectorAll<HTMLElement>('.cc-wiz-event-groups > li'));
      expect(lines().map(line => line.getAttribute('data-group-key'))).toEqual(['2026-09-05|28', '2026-09-09|29']);
      expect(lines().map(line => textOf(line.querySelector('.cc-marker-tag')))).toEqual(['E2', 'E3']);
      // A harness change names the harness in its title, so its chips leave it out.
      expect(lines().map(line => textOf(line.querySelector('.cc-wiz-event-text')))).toEqual([
        '2026-09-05 · Harness 27 → 28 · Tool guides',
        '2026-09-09 · Harness 28 → 29 (re-run 30) · Wiki'
      ]);
      expect(lines()[0].querySelector('time')!.getAttribute('datetime')).toBe('2026-09-05');

      setDay('cc-wiz-bs', '2026-09-01');
      expect(lines().map(line => line.getAttribute('data-group-key'))).toEqual(['2026-09-03|27', '2026-09-05|28', '2026-09-09|29']);
      expect(textOf(lines()[0].querySelector('.cc-wiz-event-text'))).toBe('2026-09-03 · Changes under harness 27 · System prompt, Knowledge base ×2');
    });

    it('says when no Overseer change falls in the span', () => {
      fixture.componentRef.setInput('timeline', ccTimeline({ events: [] }));
      fixture.detectChanges();
      goToRuns();
      expect(textOf(el.querySelector('.cc-wiz-events'))).toBe('None detected.');
      expect(el.querySelector('.cc-wiz-event-groups')).toBeNull();
    });
  });

  describe('analyze', () => {
    it('emits stateChange whenever the state the outer wizard reads may have changed', () => {
      let changes = 0;
      component.stateChange.subscribe(() => changes++);
      el.querySelector<HTMLInputElement>('input[name="cc-wiz-preset"][value="custom"]')!.click();
      fixture.detectChanges();
      expect(changes).toBe(1);

      const alpha = el.querySelector<HTMLInputElement>('#cc-wiz-alpha')!;
      alpha.value = '0.1';
      alpha.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(changes).toBe(2);

      component.preselectRuns();
      expect(changes).toBe(3);
      // Nothing to do for the same periods.
      component.preselectRuns();
      expect(changes).toBe(3);
    });

    it('posts the analysis, emits analysisSaved and leaves the step to the outer wizard', async () => {
      const saved: CcAnalysisResult[] = [];
      let changes = 0;
      component.analysisSaved.subscribe(result => saved.push(result));
      const p2 = el.querySelector<HTMLInputElement>('#cc-wiz-margin-P2')!;
      p2.value = '20';
      p2.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      goToRuns();
      component.preselectRuns();
      fixture.detectChanges();

      // A run and a control are left out by hand.
      el.querySelector<HTMLInputElement>('table[data-period="baseline"] tr[data-run-id="103"] .cc-wiz-run-check')!.click();
      el.querySelector<HTMLInputElement>('.cc-wiz-control-check')!.click();
      fixture.detectChanges();

      component.stateChange.subscribe(() => changes++);
      component.analyze();
      fixture.detectChanges();
      expect(component.analyzing).toBe(true);
      expect(changes).toBe(1);
      expect(textOf(el.querySelector('.cc-wiz-analyze-status'))).toBe('Analyzing… this can take a minute.');
      const post = http.expectOne(`${CC_API}/analyses`);
      expect(post.request.method).toBe('POST');
      expect(post.request.body).toEqual({
        subjectModelKey: 'openai/gpt-5|high',
        baselineStartUtc: '2026-09-01T00:00:00.000Z',
        baselineEndUtc: '2026-09-14T23:59:59.999Z',
        comparisonStartUtc: '2026-09-18T00:00:00.000Z',
        comparisonEndUtc: '2026-10-01T23:59:59.999Z',
        baselineRunIds: [101, 102],
        comparisonRunIds: [104, 105, 106],
        relaxedPooling: false,
        controlRunIds: [202, 205, 206],
        protocolOverrides: { margins: { P2: 0.2 } },
        // Every request records the step-1 selection, the default one included.
        runSelection: {
          rangeLabel: 'All dates', rangeFromUtc: null, rangeToUtc: null, firstRunId: null, lastRunId: null, leftOutRunIds: []
        }
      });

      // A second Analyze while one is in flight sends nothing.
      component.analyze();
      http.expectNone(`${CC_API}/analyses`);

      post.flush(ccAnalysisResult());
      await fixture.whenStable();
      fixture.detectChanges();

      expect(saved.map(result => result.analysisId)).toEqual([7]);
      expect(component.analyzing).toBe(false);
      expect(component.result?.analysisId).toBe(7);
      expect(changes).toBe(2);
      expect(component.step).toBe('runs');
      expect(shownSteps()).toEqual(['runs']);
      expect(body('results')).toBeNull();
      expect(textOf(el.querySelector('.cc-wiz-analyze-status'))).toBe('');
      expect(component.reachable('results')).toBe(true);
      expect(component.reachable('reports')).toBe(true);

      setStep('results');
      expect(textOf(el.querySelector('app-cc-results-view .cc-headline-text'))).toContain('Overseer chat with GPT-5 high');
    });

    it('sends nothing while Analyze is blocked', () => {
      goToRuns();
      expect(component.analyzeBlocked).toBe('Select at least one baseline run.');
      expect(component.buildRequest()).toBeNull();
      component.analyze();
      http.expectNone(`${CC_API}/analyses`);
      expect(component.analyzing).toBe(false);
    });

    it('shows the refusal of a request the server turns down', async () => {
      const saved: CcAnalysisResult[] = [];
      component.analysisSaved.subscribe(result => saved.push(result));
      goToRuns();
      component.preselectRuns();
      component.analyze();
      http.expectOne(`${CC_API}/analyses`).flush({ error: 'The baseline has no usable run.' }, { status: 400, statusText: 'Bad Request' });
      await fixture.whenStable();
      fixture.detectChanges();

      const error = el.querySelector('.cc-wiz-analyze-error')!;
      expect(error.getAttribute('role')).toBe('alert');
      expect(textOf(error)).toBe('The baseline has no usable run.');
      expect(component.analyzing).toBe(false);
      expect(component.result).toBeNull();
      expect(saved).toEqual([]);
      expect(component.step).toBe('runs');
    });

    it('abandons the request in flight on Stop', () => {
      goToRuns();
      component.preselectRuns();
      component.analyze();
      const post = http.expectOne(`${CC_API}/analyses`);
      component.stopAnalyze();
      fixture.detectChanges();
      expect(post.cancelled).toBe(true);
      expect(component.analyzing).toBe(false);
      expect(textOf(el.querySelector('.cc-wiz-analyze-status'))).toBe('');
    });
  });

  describe('the step-1 selection', () => {
    /** Runs 102–105 in the analysis, 104 left out; the host passes the scoped rows and every row. */
    function narrow(): void {
      const all = ccRunRows();
      const scope = { firstRunId: 102, lastRunId: 105, leftOut: new Set([104]) };
      fixture.componentRef.setInput('allRows', all);
      fixture.componentRef.setInput('rows', all.filter(row => [102, 103, 105].includes(row.runId)));
      fixture.componentRef.setInput('scope', scope);
      fixture.componentRef.setInput('range', { preset: '30d', fromDay: '', toDay: '', anchorUtc: '2026-10-07T12:00:00.000Z' });
      fixture.componentRef.setInput('span', { first: '2026-09-05', last: '2026-09-26' });
      fixture.componentRef.setInput('scopeKey', '102|105|104');
      fixture.detectChanges();
    }

    it('says which runs the presets span: every run in the dates, then the runs chosen in step 1', () => {
      expect(textOf(el.querySelector('.cc-wiz-span-note'))).toBe('Presets use every run in the dates: #101 (2026-09-01) to #106 (2026-10-01), 6 runs.');
      narrow();
      expect(textOf(el.querySelector('.cc-wiz-span-note'))).toBe('Presets use the runs chosen in step 1: #102 (2026-09-05) to #105 (2026-09-26), 3 runs.');
    });

    it('re-applies the preset over the span, and keeps dates typed by hand', () => {
      narrow();
      expect(component.days).toEqual({
        baselineStart: '2026-09-05', baselineEnd: '2026-09-12', comparisonStart: '2026-09-13', comparisonEnd: '2026-09-26'
      });
      expect(launchPreset(ccAxis(), { first: '2026-09-05', last: '2026-09-26' })).toEqual(component.days);

      setDay('cc-wiz-ce', '2026-09-30');
      fixture.componentRef.setInput('span', { first: '2026-09-01', last: '2026-10-01' });
      fixture.detectChanges();
      expect(component.preset).toBe('custom');
      expect(component.days.comparisonEnd).toBe('2026-09-30');
    });

    it('preselects again when the selection changes, and names the left-out runs in the periods', () => {
      goToRuns();
      component.preselectRuns();
      fixture.detectChanges();
      expect(checked('comparison')).toEqual(['104', '105', '106']);

      narrow();
      expect(checked('baseline')).toEqual(['102', '103']);
      expect(checked('comparison')).toEqual(['105']);
      const preview = Array.from(el.querySelectorAll('.cc-wiz-preview > div')).find(row => textOf(row.querySelector('dt')) === 'Left out in step 1');
      expect(textOf(preview!.querySelector('dd'))).toBe('#104');
    });

    it('records the selection in the request', () => {
      narrow();
      goToRuns();
      component.preselectRuns();
      expect(component.buildRequest()!.runSelection).toEqual({
        rangeLabel: 'Last 30 days',
        rangeFromUtc: '2026-09-07T12:00:00.000Z',
        rangeToUtc: null,
        firstRunId: 102,
        lastRunId: 105,
        leftOutRunIds: [104]
      });
    });

    it('takes the period dates from the shared date fields', () => {
      const field = el.querySelector('app-date-field #cc-wiz-bs');
      expect(field).not.toBeNull();
      setDay('cc-wiz-bs', '2026-9-3');
      expect(component.days.baselineStart).toBe('2026-09-03');
      expect(component.preset).toBe('custom');
    });
  });

  describe('a battery set', () => {
    const batterySet = (): CcComparisonSet => ccComparisonSets().sets[0];

    /** Battery runs #11 and #12 of 2026-10-08, both in the analysis, oldest first; their members are the rows. */
    function withBatteries(scope: CcRunScope = CC_EMPTY_BATTERY_SCOPE, batteries: CcBatteryRunRow[] = ccBatteryRunRows().reverse()): void {
      fixture.componentRef.setInput('compareSet', batterySet());
      fixture.componentRef.setInput('batteryRows', batteries);
      fixture.componentRef.setInput('allBatteryRows', batteries);
      fixture.componentRef.setInput('rows', batteries.flatMap(row => row.members));
      fixture.componentRef.setInput('allRows', ccBatteryMemberRows());
      fixture.componentRef.setInput('scope', scope);
      fixture.componentRef.setInput('span', { first: '2026-10-08', last: '2026-10-08' });
      fixture.componentRef.setInput('scopeKey', `${CC_BATTERY_SET_KEY}#||`);
      fixture.detectChanges();
    }

    /** Battery run #11 a day earlier, on 2026-10-07, so the two fall in separate periods; #12 as before. */
    function splitBatteries(): CcBatteryRunRow[] {
      return [ccBatteryRunRow(11, '2026-10-07T06:00:00Z', [301, 302]), ccBatteryRunRows()[0]];
    }

    /** The baseline is 2026-10-07, holding battery run #11 of {@link splitBatteries}; the comparison 2026-10-08, holding #12. */
    function batteryPeriods(): void {
      setDay('cc-wiz-bs', '2026-10-07');
      setDay('cc-wiz-be', '2026-10-07');
      setDay('cc-wiz-cs', '2026-10-08');
      setDay('cc-wiz-ce', '2026-10-08');
    }

    it('says which battery runs the presets span, and names the compared set', () => {
      withBatteries();
      expect(textOf(el.querySelector('.cc-wiz-span-note')))
        .toBe('Presets use every battery run in the dates: #11 (2026-10-08) to #12 (2026-10-08), 2 battery runs.');
      fixture.componentRef.setInput('scope', { ...CC_EMPTY_BATTERY_SCOPE, firstRunId: 11 });
      fixture.detectChanges();
      expect(textOf(el.querySelector('.cc-wiz-span-note')))
        .toBe('Presets use the battery runs chosen in step 1: #11 (2026-10-08) to #12 (2026-10-08), 2 battery runs.');
      const compared = el.querySelector('.cc-wiz-subject .cc-wiz-compared')!;
      expect(textOf(compared.querySelector('dd'))).toBe('Two initial suites (revision 1)');
    });

    it('confirms on later data from the last analysis of the same subject and set', () => {
      withBatteries();
      fixture.componentRef.setInput('analyses', [
        ccAnalysisSummary(7, { createdAtUtc: '2026-10-05T09:00:00Z' }),
        ccAnalysisSummary(9, { createdAtUtc: '2026-10-01T00:00:00Z', comparisonSetKey: CC_BATTERY_SET_KEY })
      ]);
      fixture.detectChanges();
      expect(component.lastAnalysis?.id).toBe(9);

      fixture.componentRef.setInput('analyses', [ccAnalysisSummary(7)]);
      fixture.detectChanges();
      el.querySelector<HTMLInputElement>('input[name="cc-wiz-preset"][value="later"]')!.click();
      fixture.detectChanges();
      expect(textOf(el.querySelector('.cc-wiz-preset-note')))
        .toBe('This model has no saved analysis of Two initial suites (revision 1) yet; there is no earlier look to confirm.');
    });

    it('lists one row per battery run in each period and preselects the eligible complete ones', () => {
      withBatteries(CC_EMPTY_BATTERY_SCOPE, splitBatteries());
      batteryPeriods();
      goToRuns();
      component.preselectRuns();
      fixture.detectChanges();

      const table = el.querySelector<HTMLTableElement>('table.cc-wiz-battery-runs[data-period="comparison"]')!;
      expect(Array.from(table.querySelectorAll('thead th')).map(th => textOf(th)))
        .toEqual(['Use', 'Battery run', 'Started (UTC)', 'Suites', 'Eligible', 'Matched controls']);
      const row = table.querySelector<HTMLTableRowElement>('tr[data-battery-run-id="12"]')!;
      expect(Array.from(row.cells).slice(1).map(cell => textOf(cell)))
        .toEqual(['#12', '2026-10-08 10:00 UTC', 'Board Suite, Wiki Suite', 'Yes', '#404']);
      expect(row.querySelector('.cc-wiz-run-check')!.getAttribute('aria-label')).toBe('Include battery run #12 in the comparison');
      expect(checkedBatteries('baseline')).toEqual(['11']);
      expect(checkedBatteries('comparison')).toEqual(['12']);
      // The controls stay per run: the matched controls of the members.
      expect(Array.from(el.querySelectorAll<HTMLInputElement>('.cc-wiz-control-check')).map(box => textOf(box.closest('label'))))
        .toEqual(['Control run #404']);
      expect(component.regradeRunIds).toEqual([301, 302, 303, 304, 404]);
      expect(component.missingControls.map(member => member.runId)).toEqual([301, 302, 303]);
    });

    it('posts the battery runs, the set and the battery selection, and no run ids', () => {
      withBatteries({ ...CC_EMPTY_BATTERY_SCOPE, firstRunId: 11, leftOut: new Set([13]) }, splitBatteries());
      fixture.componentRef.setInput('range', { preset: '30d', fromDay: '', toDay: '', anchorUtc: '2026-10-08T12:00:00.000Z' });
      fixture.detectChanges();
      batteryPeriods();
      goToRuns();
      component.preselectRuns();
      component.analyze();
      const post = http.expectOne(`${CC_API}/analyses`);
      const body = post.request.body;
      expect(body.comparisonSet).toEqual({ kind: 'battery', key: CC_BATTERY_SET_KEY });
      expect(body.baselineBatteryRunIds).toEqual([11]);
      expect(body.comparisonBatteryRunIds).toEqual([12]);
      expect('baselineRunIds' in body).toBe(false);
      expect('comparisonRunIds' in body).toBe(false);
      expect(body.controlRunIds).toEqual([404]);
      expect(body.runSelection).toEqual({
        rangeLabel: 'Last 30 days',
        rangeFromUtc: '2026-09-08T12:00:00.000Z',
        rangeToUtc: null,
        firstRunId: null,
        lastRunId: null,
        leftOutRunIds: [],
        firstBatteryRunId: 11,
        lastBatteryRunId: null,
        leftOutBatteryRunIds: [13]
      });
      post.flush(ccAnalysisResult());
    });

    it('posts a suite set with the run ids, as before', () => {
      fixture.componentRef.setInput('compareSet', ccComparisonSets().sets[1]);
      fixture.detectChanges();
      goToRuns();
      component.preselectRuns();
      const request = component.buildRequest()!;
      expect(request.comparisonSet).toEqual({ kind: 'suite', key: 'suite:id:5' });
      expect(request.baselineRunIds).toEqual([101, 102, 103]);
      expect(request.baselineBatteryRunIds).toBeUndefined();
      expect(request.runSelection!.firstBatteryRunId).toBeUndefined();
    });

    it('selects the battery runs of a saved battery analysis from its units', () => {
      withBatteries();
      component.showResult(ccAnalysisResult({
        unitKind: 'batteryRun',
        comparisonSet: { kind: 'battery', key: CC_BATTERY_SET_KEY, label: 'Two initial suites (revision 1)' },
        units: [
          { unitId: 11, kind: 'batteryRun', period: 'baseline', startedAtUtc: '2026-10-08T06:00:00Z', memberRunIds: [301, 302] },
          { unitId: 12, kind: 'batteryRun', period: 'comparison', startedAtUtc: '2026-10-08T10:00:00Z', memberRunIds: [303, 304] }
        ]
      }));
      expect([...component.baselineSelected]).toEqual([11]);
      expect([...component.comparisonSelected]).toEqual([12]);
    });
  });

  describe('a saved result', () => {
    it('loads a saved analysis without moving the step, for the outer wizard to show on Results', () => {
      let changes = 0;
      component.stateChange.subscribe(() => changes++);
      component.showResult(ccAnalysisResult());
      fixture.detectChanges();

      expect(changes).toBe(1);
      expect(component.step).toBe('periods');
      expect(shownSteps()).toEqual(['periods']);
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-bs')!.value).toBe('2026-09-01');
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-be')!.value).toBe('2026-09-14');
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-cs')!.value).toBe('2026-09-15');
      expect(el.querySelector<HTMLInputElement>('#cc-wiz-ce')!.value).toBe('2026-10-01');
      expect(el.querySelector<HTMLInputElement>('input[name="cc-wiz-preset"][value="custom"]')!.checked).toBe(true);
      expect(component.reachable('results')).toBe(true);

      setStep('results');
      expect(el.querySelector('app-cc-results-view')).not.toBeNull();
      expect(textOf(el.querySelector('.cc-headline-text'))).toContain('Overseer chat with GPT-5 high');
    });

    it('reports chartsAttaching while the Reports step attaches charts', () => {
      expect(component.chartsAttaching).toBe(false);
      component.showResult(ccAnalysisResult());
      setStep('reports');
      http.expectOne(`${CC_API}/analyses/7/report-documents/job`).flush(null, { status: 204, statusText: 'No Content' });
      http.expectOne(r => r.url === DOCUMENTS_URL).flush([]);
      fixture.detectChanges();

      const reports = component.reportsStep!;
      expect(reports).toBeDefined();
      expect(component.chartsAttaching).toBe(false);
      reports.chartState = 'attaching';
      expect(component.chartsAttaching).toBe(true);
      reports.chartState = 'done';
      expect(component.chartsAttaching).toBe(false);
      reports.chartState = 'failed';
      expect(component.chartsAttaching).toBe(false);
    });
  });
});

describe('analysis wizard periods', () => {
  it('never lets the launch preset overlap on a short series', () => {
    const days = launchPreset(ccAxis({ firstRunAtUtc: '2026-09-01T00:00:00Z', lastRunAtUtc: '2026-09-10T00:00:00Z' }));
    expect(days).toEqual({ baselineStart: '2026-09-01', baselineEnd: '2026-08-31', comparisonStart: '2026-09-01', comparisonEnd: '2026-09-10' });
    expect(periodsRefusal(days)).toBe('The baseline must not end before it starts.');
  });

  it('accepts adjacent, ordered periods', () => {
    expect(periodsRefusal({ baselineStart: '2026-09-01', baselineEnd: '2026-09-14', comparisonStart: '2026-09-15', comparisonEnd: '2026-09-30' })).toBe('');
    expect(periodsRefusal({ baselineStart: '', baselineEnd: '2026-09-14', comparisonStart: '2026-09-15', comparisonEnd: '2026-09-30' })).toBe('Enter all four dates.');
  });
});
