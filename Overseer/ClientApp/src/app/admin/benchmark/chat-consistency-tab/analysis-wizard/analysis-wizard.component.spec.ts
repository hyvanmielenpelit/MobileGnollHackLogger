import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';

import { CC_EMPTY_BATTERY_SCOPE, CcRunScope } from '../chat-consistency-scope';
import { CcAnalysisResult, CcBatteryRunRow, CcComparisonSet, CcRunRow } from '../chat-consistency.models';
import {
  CC_API,
  CC_BATTERY_SET_KEY,
  ccAnalysisResult,
  ccAnalysisSummary,
  ccAnnotation,
  ccAxis,
  ccBatteryMemberRows,
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
import { CcAnalysisStep, CcAnalysisWizardComponent } from './analysis-wizard.component';

const DOCUMENTS_URL = '/api/admin/benchmark/report-documents';

/** The run table of the composite-event fixture: runs 201–206. */
function eventRows(): CcRunRow[] {
  return ccEventPoints().map(point => ccRunRow(point.runId, point.startedAtUtc));
}

/** {@link ccRunRows} with run 102 usable on no axis. */
function rowsWithIneligible102(): CcRunRow[] {
  return ccRunRows().map(row => row.runId === 102
    ? ccRunRow(102, row.startedAtUtc, {
      matchedControlRunIds: [202],
      eligibility: [{ axis: 'quality', eligible: false, segment: null, reason: 'No grades' }]
    })
    : row);
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
    fixture.componentRef.setInput('step', 'analyze');
    fixture.detectChanges();
    // The Analyze body starts the re-grade panel, which asks whether a re-grade is already running.
    http.expectOne(`${CC_API}/regrade/job`).flush(null, { status: 204, statusText: 'No Content' });
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

  /** Shows Results with a result: its Reports section asks for the report job and the documents. */
  function goToResults(): void {
    setStep('results');
    http.expectOne(`${CC_API}/analyses/7/report-documents/job`).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne(r => r.url === DOCUMENTS_URL).flush([]);
    fixture.detectChanges();
  }

  /** Chooses a run in one of the four selects, as the user does. */
  function pick(id: string, value: string): void {
    const select = el.querySelector<HTMLSelectElement>(`#${id}`)!;
    select.value = value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function clickPreset(value: string): void {
    el.querySelector<HTMLInputElement>(`input[name="cc-wiz-preset"][value="${value}"]`)!.click();
    fixture.detectChanges();
  }

  const body = (step: CcAnalysisStep): HTMLElement | null => el.querySelector<HTMLElement>(`.cc-wiz-step[data-step="${step}"]`);
  const shownSteps = (): (string | null)[] =>
    Array.from(el.querySelectorAll<HTMLElement>('.cc-wiz-step')).filter(step => !step.hidden).map(step => step.getAttribute('data-step'));
  /** The four run choices, as the selects show them: baseline first and last, comparison first and last. */
  const choices = (): string[] =>
    ['cc-wiz-bf', 'cc-wiz-bl', 'cc-wiz-cf', 'cc-wiz-cl'].map(id => el.querySelector<HTMLSelectElement>(`#${id}`)!.value);
  /** Each unit row of the table as `[id, period]`. */
  const unitPeriods = (): [string | null, string][] =>
    Array.from(el.querySelectorAll<HTMLTableRowElement>('table.cc-wiz-units tbody tr'))
      .map(row => [row.getAttribute('data-unit-id'), textOf(row.querySelector('.cc-wiz-unit-period'))]);
  const controlLabels = (): string[] =>
    Array.from(el.querySelectorAll<HTMLInputElement>('.cc-wiz-control-check')).map(box => textOf(box.closest('label')));
  const checkedControls = (): string[] =>
    Array.from(el.querySelectorAll<HTMLInputElement>('.cc-wiz-control-check')).filter(box => box.checked).map(box => textOf(box.closest('label')));
  const presetChecked = (value: string): boolean =>
    el.querySelector<HTMLInputElement>(`input[name="cc-wiz-preset"][value="${value}"]`)!.checked;

  describe('steps', () => {
    it('renders only the body of the step it is given, without navigation of its own', () => {
      expect(shownSteps()).toEqual(['analyze']);
      expect(body('results')).toBeNull();
      for (const selector of ['.gh-steps', '.cc-wiz-nav', '.cc-wiz-next', '.cc-wiz-back', '.cc-wiz-analyze', '#cc-wiz-step-title']) {
        expect(el.querySelector(selector), selector).toBeNull();
      }
    });

    it('keeps a visited step\'s body mounted and hidden while another step shows', () => {
      const regradePanel = el.querySelector('app-cc-regrade-panel');
      expect(regradePanel).not.toBeNull();
      setStep('results');
      expect(shownSteps()).toEqual(['results']);
      expect(body('analyze')!.hidden).toBe(true);

      setStep('analyze');
      expect(shownSteps()).toEqual(['analyze']);
      expect(body('results')!.hidden).toBe(true);
      // The same panel is kept, so it does not ask for the re-grade job again.
      expect(el.querySelector('app-cc-regrade-panel')).toBe(regradePanel);
    });

    it('reaches Analyze with a model and Results only with a result', () => {
      expect(component.reachable('analyze')).toBe(true);
      expect(component.reachable('results')).toBe(false);
      setStep('results');
      expect(shownSteps()).toEqual(['results']);
      expect(el.querySelector('app-cc-results-view')).toBeNull();
      expect(el.querySelector('app-cc-reports-step')).toBeNull();
    });
  });

  describe('periods', () => {
    it('starts from Earliest vs latest and shows the run choices, the note and the sample lines', () => {
      expect(presetChecked('earliest')).toBe(true);
      expect(textOf(el.querySelector('.cc-wiz-periods > legend'))).toBe('Periods');
      expect(choices()).toEqual(['101', '103', '104', '106']);
      expect(textOf(el.querySelector('.cc-wiz-preset-note')))
        .toBe('The runs span 31 days: the first 14 days against the last 14 days, 3 runs each.');
      expect(el.querySelector('.cc-wiz-periods-error')).toBeNull();
      expect(component.periodsError).toBe('');
      expect(Array.from(el.querySelectorAll('.cc-wiz-sample')).map(line => textOf(line))).toEqual([
        'Baseline: 3 runs on 3 days (2026-09-01 to 2026-09-12), which meets the minimum sample for P1, P4 and P5.',
        'Comparison: 3 runs on 3 days (2026-09-20 to 2026-10-01), which meets the minimum sample for P1, P4 and P5.'
      ]);
      expect(component.windows).toEqual({
        baselineStartUtc: '2026-09-01T00:00:00.000Z',
        baselineEndUtc: '2026-09-12T23:59:59.999Z',
        comparisonStartUtc: '2026-09-20T00:00:00.000Z',
        comparisonEndUtc: '2026-10-01T23:59:59.999Z'
      });
    });

    it('offers every unit in native selects with visually hidden labels', () => {
      const select = el.querySelector<HTMLSelectElement>('#cc-wiz-bf')!;
      expect(select.classList).toContain('gh-input');
      const label = el.querySelector<HTMLLabelElement>('label[for="cc-wiz-bf"]')!;
      expect(label.classList).toContain('visually-hidden');
      expect(textOf(label)).toBe('Baseline first run');
      expect(['cc-wiz-bl', 'cc-wiz-cf', 'cc-wiz-cl'].map(id => textOf(el.querySelector(`label[for="${id}"]`))))
        .toEqual(['Baseline last run', 'Comparison first run', 'Comparison last run']);
      const options = Array.from(select.options);
      expect(options.map(option => textOf(option))).toEqual([
        'Choose a run', '#101 · 2026-09-01 08:00 UTC', '#102 · 2026-09-05 08:00 UTC', '#103 · 2026-09-12 08:00 UTC',
        '#104 · 2026-09-20 08:00 UTC', '#105 · 2026-09-26 08:00 UTC', '#106 · 2026-10-01 08:00 UTC'
      ]);
      // A run reads its own text.
      expect(options[1].getAttribute('aria-label')).toBeNull();
    });

    it('switches to Custom when a run is chosen by hand, and refuses an overlap where the date error was', () => {
      pick('cc-wiz-cf', '103');
      expect(presetChecked('custom')).toBe(true);
      expect(component.preset).toBe('custom');
      expect(textOf(el.querySelector('.cc-wiz-preset-note'))).toBe('');
      expect(textOf(el.querySelector('.cc-wiz-periods-error'))).toBe('The comparison must start after the baseline\'s last run.');
      expect(component.periodsError).toBe('The comparison must start after the baseline\'s last run.');
      expect(component.analyzeBlocked).toBe('The comparison must start after the baseline\'s last run.');
      expect(component.windows).toBeNull();
      expect(el.querySelector('.cc-wiz-sample')).toBeNull();
      // Analyze stays reachable: the refusal is shown there.
      expect(component.reachable('analyze')).toBe(true);
    });

    it('refuses a period whose last run comes before its first, and an unset choice', () => {
      pick('cc-wiz-bl', '101');
      pick('cc-wiz-bf', '102');
      expect(component.periodsError).toBe('The baseline\'s last run comes before its first.');
      pick('cc-wiz-cl', '');
      pick('cc-wiz-bf', '101');
      expect(textOf(el.querySelector('.cc-wiz-periods-error'))).toBe('Choose the first and last run of both periods.');
    });

    it('blocks Analyze while an override is invalid', () => {
      const p1 = el.querySelector<HTMLInputElement>('#cc-wiz-margin-P1')!;
      p1.value = '-1';
      p1.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(component.overridesError).toBe('The margin of P1 must be a positive number.');
      expect(textOf(el.querySelector('.cc-wiz-overrides-error'))).toBe('The margin of P1 must be a positive number.');
      expect(component.analyzeBlocked).toBe('The margin of P1 must be a positive number.');

      p1.value = '';
      p1.dispatchEvent(new Event('input'));
      const alpha = el.querySelector<HTMLInputElement>('#cc-wiz-alpha')!;
      alpha.value = '0.7';
      alpha.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(textOf(el.querySelector('.cc-wiz-overrides-error'))).toBe('α must lie strictly between 0 and 0.5.');
    });

    it('keeps Protocol V1 and its overrides in one closed disclosure', () => {
      const protocol = el.querySelector<HTMLDetailsElement>('details.gh-disclosure.cc-wiz-protocol')!;
      expect(protocol.open).toBe(false);
      expect(textOf(protocol.querySelector(':scope > summary'))).toBe('Protocol V1: margins and minimum sample');
      const rows = Array.from(protocol.querySelectorAll<HTMLTableRowElement>('.cc-protocol-table tbody tr'))
        .map(row => Array.from(row.cells).map(cell => textOf(cell)));
      expect(rows).toEqual([
        ['P1 Quality', '±3 index points'], ['P2 Time to first answer text', '±15 %'], ['P3 Answer streaming rate', '±10 %'],
        ['P4 Work per turn', '±15 %'], ['P5 Cost per question', '±10 %']
      ]);
      expect(textOf(protocol.querySelector('.cc-wiz-protocol-facts'))).toContain('0.05, Holm-adjusted across P1–P5');
      expect(protocol.querySelector('details.cc-wiz-overrides #cc-wiz-alpha')).not.toBeNull();
    });

    it('splits before and after a composite Overseer event, with every run on each side', () => {
      fixture.componentRef.setInput('timeline', ccEventTimeline());
      fixture.componentRef.setInput('rows', eventRows());
      fixture.componentRef.setInput('axis', ccAxis({ firstRunAtUtc: '2026-09-01T08:00:00Z', lastRunAtUtc: '2026-09-10T08:00:00Z' }));
      fixture.detectChanges();
      clickPreset('event');

      const options = Array.from(el.querySelectorAll<HTMLOptionElement>('#cc-wiz-event option'));
      expect(options.map(option => textOf(option))).toEqual([
        'E1 · 2026-09-03 · Changes under harness 27 (2 changes)',
        'E2 · 2026-09-05 · Harness 27 → 28 (2 changes)',
        'E3 · 2026-09-09 · Harness 28 → 29 (re-run 30) (2 changes)',
        'E4 · 2026-09-10 · Changes under harness 29 (2 changes)'
      ]);
      expect(options.map(option => option.value)).toEqual(['2026-09-03|27', '2026-09-05|28', '2026-09-09|29', '2026-09-10|29']);
      // The first composite is taken; run 202 started at its time and so is in the comparison.
      expect(component.presetEventGroupKey).toBe('2026-09-03|27');
      expect(choices()).toEqual(['201', '201', '202', '206']);
      expect(textOf(el.querySelector('.cc-wiz-preset-note')))
        .toBe('The runs before the Overseer change E1 (2026-09-03 08:00 UTC) against those from it: 1 run against 5.');

      const select = el.querySelector<HTMLSelectElement>('#cc-wiz-event')!;
      select.value = '2026-09-05|28';
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      expect(component.presetEventGroupKey).toBe('2026-09-05|28');
      expect(el.querySelector<HTMLSelectElement>('#cc-wiz-event')!.value).toBe('2026-09-05|28');
      expect(choices()).toEqual(['201', '203', '204', '206']);
      expect(presetChecked('event')).toBe(true);
      expect(unitPeriods()).toEqual([
        ['201', 'Baseline'], ['202', 'Baseline'], ['203', 'Baseline'], ['204', 'Comparison'], ['205', 'Comparison'], ['206', 'Comparison']
      ]);
    });

    it('says when there is no Overseer change to split at', () => {
      fixture.componentRef.setInput('timeline', ccTimeline({ events: [] }));
      fixture.detectChanges();
      clickPreset('event');
      expect(el.querySelector('#cc-wiz-event')).toBeNull();
      expect(textOf(el.querySelector('.cc-wiz-preset-note'))).toBe('No Overseer change was detected in the timeline range.');
      expect(choices()).toEqual(['', '', '', '']);
      expect(component.periodsError).toBe('Choose the first and last run of both periods.');
    });

    it('splits at an annotation, and says when one side of it has no run', () => {
      clickPreset('annotation');
      expect(choices()).toEqual(['101', '104', '105', '106']);
      expect(textOf(el.querySelector('.cc-wiz-preset-note')))
        .toBe('The runs before the annotation (2026-09-20 12:00 UTC) against those from it: 4 runs against 2.');
      expect(component.presetAnchorUtc).toBe('2026-09-20T12:00:00Z');

      fixture.componentRef.setInput('timeline', ccTimeline({ annotations: [ccAnnotation(2, { atUtc: '2026-10-08T00:00:00Z' })] }));
      fixture.detectChanges();
      expect(textOf(el.querySelector('.cc-wiz-preset-note'))).toBe('No run on one side of the annotation (2026-10-08).');
      expect(choices()).toEqual(['', '', '', '']);
    });

    it('confirms on later data: the last baseline against the runs after the analysis was saved', () => {
      fixture.componentRef.setInput('analyses', [ccAnalysisSummary(7), ccAnalysisSummary(9, { createdAtUtc: '2026-09-01T00:00:00Z' })]);
      fixture.detectChanges();
      clickPreset('later');
      // Analysis 7 was saved on 2026-10-02 09:00, after the last run.
      expect(textOf(el.querySelector('.cc-wiz-preset-note'))).toBe('No run was made after the last analysis was saved (2026-10-02 09:00 UTC).');

      fixture.componentRef.setInput('analyses', [ccAnalysisSummary(7, { createdAtUtc: '2026-09-20T07:00:00Z' })]);
      fixture.detectChanges();
      // A checked radio fires no change when clicked again, so the preset is chosen afresh.
      clickPreset('custom');
      clickPreset('later');
      expect(choices()).toEqual(['101', '103', '104', '106']);
      expect(textOf(el.querySelector('.cc-wiz-preset-note'))).toBe('Compares the runs after the last analysis, saved 2026-09-20 07:00 UTC, with its baseline.');
    });

    it('asks for a model in step 1 when there is none', () => {
      fixture.componentRef.setInput('axis', null);
      fixture.detectChanges();
      expect(component.analyzeBlocked).toBe('Choose a model in step 1 first.');
      expect(component.reachable('analyze')).toBe(false);
      expect(textOf(el.querySelector('.cc-wiz-subject'))).toContain('None chosen');
      expect(el.querySelector<HTMLInputElement>('input[name="cc-wiz-preset"][value="earliest"]')!.disabled).toBe(true);
      expect(presetChecked('custom')).toBe(true);
    });
  });

  describe('runs in the periods', () => {
    it('lists every step-1 run read-only with its period, and no checkbox or anchor button', () => {
      const table = el.querySelector<HTMLTableElement>('table.gh-table.cc-wiz-units')!;
      expect(textOf(table.querySelector('caption'))).toBe('Runs in the periods');
      expect(Array.from(table.querySelectorAll('thead th')).map(th => textOf(th)))
        .toEqual(['Run', 'Started (UTC)', 'Suite', 'Period', 'Matched controls']);
      const row = table.querySelector<HTMLTableRowElement>('tr[data-unit-id="101"]')!;
      expect(Array.from(row.cells).map(cell => textOf(cell))).toEqual(['#101', '2026-09-01 08:00 UTC', 'Board Suite', 'Baseline', '#201']);
      expect(textOf(table.querySelector('tr[data-unit-id="104"] td:last-child'))).toBe('None');
      expect(table.querySelector('input')).toBeNull();
      expect(el.querySelector('.cc-wiz-anchor-btn')).toBeNull();

      pick('cc-wiz-bl', '102');
      pick('cc-wiz-cf', '105');
      expect(unitPeriods()).toEqual([
        ['101', 'Baseline'], ['102', 'Baseline'], ['103', 'Not used'], ['104', 'Not used'], ['105', 'Comparison'], ['106', 'Comparison']
      ]);
    });

    it('marks an ineligible run inside a period Not eligible and never sends it', () => {
      fixture.componentRef.setInput('rows', rowsWithIneligible102());
      fixture.detectChanges();
      expect(choices()).toEqual(['101', '103', '104', '106']);
      expect(unitPeriods()[1]).toEqual(['102', 'Not eligible']);
      expect(textOf(el.querySelector('#cc-wiz-bf option[value="102"]'))).toBe('#102 · 2026-09-05 08:00 UTC · not eligible');
      expect(textOf(el.querySelector('.cc-wiz-sample[data-period="baseline"]')))
        .toBe('Baseline: 2 runs on 2 days (2026-09-01 to 2026-09-12), which meets the minimum sample for P1, P4 and P5.');
      const request = component.buildRequest()!;
      expect(request.baselineRunIds).toEqual([101, 103]);
      expect(request.comparisonRunIds).toEqual([104, 105, 106]);
      // Its matched control is no candidate either.
      expect(controlLabels()).toEqual(['Control run #201', 'Control run #205', 'Control run #206']);
    });

    it('says when a period is short of the minimum sample', () => {
      pick('cc-wiz-bl', '101');
      expect(textOf(el.querySelector('.cc-wiz-sample[data-period="baseline"]')))
        .toBe('Baseline: 1 run on 1 day (2026-09-01). P1, P4 and P5 need at least 2 on 2 days to be Established.');
    });
  });

  describe('control runs', () => {
    it('offers the matched controls of the runs in both periods, all checked, under a hint that says what they are for', () => {
      const fieldset = el.querySelector<HTMLFieldSetElement>('fieldset.cc-wiz-controls')!;
      expect(textOf(fieldset.querySelector('legend'))).toBe('Control runs');
      expect(textOf(fieldset.querySelector('.cc-wiz-controls-hint')))
        .toBe('Other models\' runs under the same Overseer build. Used only to attribute a change to a side, never for the verdicts.');
      expect(controlLabels()).toEqual(['Control run #201', 'Control run #202', 'Control run #205', 'Control run #206']);
      expect(checkedControls()).toEqual(controlLabels());
      expect(component.regradeRunIds).toEqual([101, 102, 103, 104, 105, 106, 201, 202, 205, 206]);
    });

    it('keeps the checks for the same periods, and checks every candidate again when the periods change', () => {
      el.querySelector<HTMLInputElement>('.cc-wiz-control-check')!.click();
      fixture.detectChanges();
      expect(checkedControls()).toEqual(['Control run #202', 'Control run #205', 'Control run #206']);

      component.preselectRuns();
      fixture.detectChanges();
      expect(checkedControls()).toEqual(['Control run #202', 'Control run #205', 'Control run #206']);

      pick('cc-wiz-bf', '102');
      expect(controlLabels()).toEqual(['Control run #202', 'Control run #205', 'Control run #206']);
      expect(checkedControls()).toEqual(controlLabels());
    });

    it('says when no run in the periods has a matched control', () => {
      fixture.componentRef.setInput('rows', ccRunRows().map(row => ({ ...row, matchedControlRunIds: [] })));
      fixture.detectChanges();
      expect(textOf(el.querySelector('.cc-wiz-controls-empty')))
        .toBe('No matched control run; the analysis looks for controls among other models\' runs itself.');
      expect(component.buildRequest()!.controlRunIds).toBeUndefined();
    });
  });

  describe('preview', () => {
    it('previews the strata, the composite Overseer events in the span and the missing controls', () => {
      expect(textOf(el.querySelector('.cc-wiz-strata'))).toBe('weekday 08–12 UTC');
      expect(Array.from(el.querySelectorAll('.cc-wiz-missing li')).map(item => textOf(item)))
        .toEqual(['Run #103 (Board Suite) has no matched control run.', 'Run #104 (Board Suite) has no matched control run.']);
      const groups = Array.from(el.querySelectorAll<HTMLElement>('.cc-wiz-event-groups > li'));
      expect(groups.map(group => group.getAttribute('data-group-key'))).toEqual(['2026-09-15|30']);
      expect(textOf(groups[0].querySelector('.cc-marker-tag.is-event'))).toBe('E1');
      expect(textOf(groups[0].querySelector('.cc-wiz-event-text'))).toBe('2026-09-15 · Changes under harness 30 · Tool guides');
      expect(textOf(el.querySelector('.cc-wiz-relaxed'))).toContain('caps grades at Indicated');
    });

    it('lists the composite events between the baseline\'s first day and the comparison\'s last', () => {
      fixture.componentRef.setInput('timeline', ccEventTimeline());
      fixture.componentRef.setInput('rows', eventRows());
      fixture.detectChanges();
      clickPreset('custom');
      pick('cc-wiz-bf', '204');
      pick('cc-wiz-bl', '204');
      pick('cc-wiz-cf', '205');
      pick('cc-wiz-cl', '205');

      const lines = () => Array.from(el.querySelectorAll<HTMLElement>('.cc-wiz-event-groups > li'));
      expect(lines().map(line => line.getAttribute('data-group-key'))).toEqual(['2026-09-05|28']);
      expect(textOf(lines()[0].querySelector('.cc-wiz-event-text'))).toBe('2026-09-05 · Harness 27 → 28 · Tool guides');
      expect(lines()[0].querySelector('time')!.getAttribute('datetime')).toBe('2026-09-05');

      pick('cc-wiz-bf', '201');
      pick('cc-wiz-cl', '206');
      expect(lines().map(line => line.getAttribute('data-group-key')))
        .toEqual(['2026-09-03|27', '2026-09-05|28', '2026-09-09|29', '2026-09-10|29']);
      expect(textOf(lines()[0].querySelector('.cc-wiz-event-text'))).toBe('2026-09-03 · Changes under harness 27 · System prompt, Knowledge base ×2');
    });

    it('says when no Overseer change falls in the span', () => {
      fixture.componentRef.setInput('timeline', ccTimeline({ events: [] }));
      fixture.detectChanges();
      expect(textOf(el.querySelector('.cc-wiz-events'))).toBe('None detected.');
      expect(el.querySelector('.cc-wiz-event-groups')).toBeNull();
    });
  });

  describe('analyze', () => {
    it('emits stateChange whenever the state the outer wizard reads may have changed', () => {
      let changes = 0;
      component.stateChange.subscribe(() => changes++);
      clickPreset('custom');
      expect(changes).toBe(1);

      const alpha = el.querySelector<HTMLInputElement>('#cc-wiz-alpha')!;
      alpha.value = '0.1';
      alpha.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(changes).toBe(2);

      // The controls already follow these periods.
      component.preselectRuns();
      expect(changes).toBe(2);

      pick('cc-wiz-bl', '102');
      expect(changes).toBe(3);
    });

    it('posts the analysis, emits analysisSaved and leaves the step to the outer wizard', async () => {
      const saved: CcAnalysisResult[] = [];
      let changes = 0;
      component.analysisSaved.subscribe(result => saved.push(result));
      const p2 = el.querySelector<HTMLInputElement>('#cc-wiz-margin-P2')!;
      p2.value = '20';
      p2.dispatchEvent(new Event('input'));
      // A control is left out by hand.
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
        baselineEndUtc: '2026-09-12T23:59:59.999Z',
        comparisonStartUtc: '2026-09-20T00:00:00.000Z',
        comparisonEndUtc: '2026-10-01T23:59:59.999Z',
        baselineRunIds: [101, 102, 103],
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
      expect(component.step).toBe('analyze');
      expect(shownSteps()).toEqual(['analyze']);
      expect(body('results')).toBeNull();
      expect(textOf(el.querySelector('.cc-wiz-analyze-status'))).toBe('');
      expect(component.reachable('results')).toBe(true);

      goToResults();
      expect(textOf(el.querySelector('app-cc-results-view .cc-headline-text'))).toContain('Overseer chat with GPT-5 high');
    });

    it('sends nothing while Analyze is blocked', () => {
      pick('cc-wiz-cf', '103');
      expect(component.buildRequest()).toBeNull();
      component.analyze();
      http.expectNone(`${CC_API}/analyses`);
      expect(component.analyzing).toBe(false);
    });

    it('shows the refusal of a request the server turns down', async () => {
      const saved: CcAnalysisResult[] = [];
      component.analysisSaved.subscribe(result => saved.push(result));
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
      expect(component.step).toBe('analyze');
    });

    it('abandons the request in flight on Stop', () => {
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
    /** Runs 102, 103 and 105 in the analysis, 104 left out; the host passes the scoped rows and every row. */
    function narrow(): void {
      const all = ccRunRows();
      const scope = { firstRunId: 102, lastRunId: 105, leftOut: new Set([104]) };
      fixture.componentRef.setInput('allRows', all);
      fixture.componentRef.setInput('rows', all.filter(row => [102, 103, 105].includes(row.runId)));
      fixture.componentRef.setInput('scope', scope);
      fixture.componentRef.setInput('range', { preset: '30d', fromDay: '', toDay: '', anchorUtc: '2026-10-07T12:00:00.000Z' });
      fixture.componentRef.setInput('scopeKey', '102|105|104');
      fixture.detectChanges();
    }

    it('says which runs the presets span: every run in the dates, then the runs chosen in step 1', () => {
      expect(textOf(el.querySelector('.cc-wiz-span-note'))).toBe('Presets use every run in the dates: #101 (2026-09-01) to #106 (2026-10-01), 6 runs.');
      narrow();
      expect(textOf(el.querySelector('.cc-wiz-span-note'))).toBe('Presets use the runs chosen in step 1: #102 (2026-09-05) to #105 (2026-09-26), 3 runs.');
    });

    it('applies the chosen preset again when the selection changes', () => {
      narrow();
      expect(presetChecked('earliest')).toBe(true);
      expect(choices()).toEqual(['102', '103', '105', '105']);
      expect(textOf(el.querySelector('.cc-wiz-preset-note')))
        .toBe('The runs span 22 days: the earlier days against the later days, split at 2026-09-26, 2 runs against 1.');
      expect(unitPeriods().map(([id]) => id)).toEqual(['102', '103', '105']);
    });

    it('keeps Custom choices while their runs stay, and clears a choice whose run left', () => {
      pick('cc-wiz-bf', '102');
      expect(choices()).toEqual(['102', '103', '104', '106']);
      narrow();
      expect(component.preset).toBe('custom');
      expect(choices()).toEqual(['102', '103', '', '']);
      expect(component.periodsError).toBe('Choose the first and last run of both periods.');
    });

    it('names the left-out runs inside the windows', () => {
      const all = ccRunRows();
      fixture.componentRef.setInput('allRows', all);
      fixture.componentRef.setInput('rows', all.filter(row => row.runId !== 103));
      fixture.componentRef.setInput('scope', { firstRunId: null, lastRunId: null, leftOut: new Set([103]) });
      fixture.componentRef.setInput('scopeKey', '||103');
      fixture.detectChanges();
      const leftOut = () => Array.from(el.querySelectorAll('.cc-wiz-preview > div'))
        .find(row => textOf(row.querySelector('dt')) === 'Left out in step 1');
      // Earliest vs latest ends the baseline with run 102 on 2026-09-05, before run 103.
      expect(leftOut()).toBeUndefined();

      pick('cc-wiz-bl', '104');
      pick('cc-wiz-cf', '105');
      expect(textOf(leftOut()!.querySelector('dd'))).toBe('#103');
    });

    it('records the selection in the request', () => {
      narrow();
      expect(component.buildRequest()!.runSelection).toEqual({
        rangeLabel: 'Last 30 days',
        rangeFromUtc: '2026-09-07T12:00:00.000Z',
        rangeToUtc: null,
        firstRunId: 102,
        lastRunId: 105,
        leftOutRunIds: [104]
      });
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
      fixture.componentRef.setInput('scopeKey', `${CC_BATTERY_SET_KEY}#${scope.firstRunId ?? ''}||`);
      fixture.detectChanges();
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

    it('splits battery runs #11 and #12 of the same day into a baseline of #11 and a comparison of #12', () => {
      withBatteries();
      expect(presetChecked('earliest')).toBe(true);
      expect(choices()).toEqual(['11', '11', '12', '12']);
      expect(el.querySelector('.cc-wiz-periods-error')).toBeNull();
      expect(component.periodsError).toBe('');
      expect(textOf(el.querySelector('.cc-wiz-preset-note')))
        .toBe('The runs span 1 day: the earlier half against the later half, 1 battery run each.');

      const option = el.querySelector<HTMLOptionElement>('#cc-wiz-cf option[value="12"]')!;
      expect(textOf(option)).toBe('#12 · 2026-10-08 10:00 UTC');
      expect(option.getAttribute('aria-label')).toBe('Battery run #12 · 2026-10-08 10:00 UTC');
      expect(textOf(el.querySelector('#cc-wiz-cf option[value=""]'))).toBe('Choose a battery run');

      const table = el.querySelector<HTMLTableElement>('table.cc-wiz-units')!;
      expect(textOf(table.querySelector('caption'))).toBe('Battery runs in the periods');
      expect(Array.from(table.querySelectorAll('thead th')).map(th => textOf(th)))
        .toEqual(['Battery run', 'Started (UTC)', 'Suites', 'Period', 'Matched controls']);
      const row = table.querySelector<HTMLTableRowElement>('tr[data-unit-id="12"]')!;
      expect(Array.from(row.cells).map(cell => textOf(cell)))
        .toEqual(['#12', '2026-10-08 10:00 UTC', 'Board Suite, Wiki Suite', 'Comparison', '#404']);
      expect(unitPeriods()).toEqual([['11', 'Baseline'], ['12', 'Comparison']]);

      expect(Array.from(el.querySelectorAll('.cc-wiz-sample')).map(line => textOf(line))).toEqual([
        'Baseline: 1 battery run on 1 day (2026-10-08). P1, P4 and P5 need at least 2 on 2 days to be Established.',
        'Comparison: 1 battery run on 1 day (2026-10-08). P1, P4 and P5 need at least 2 on 2 days to be Established.'
      ]);
      // The controls stay per run: the matched controls of the members.
      expect(controlLabels()).toEqual(['Control run #404']);
      expect(component.regradeRunIds).toEqual([301, 302, 303, 304, 404]);
      expect(component.missingControls.map(member => member.runId)).toEqual([301, 302, 303]);
    });

    it('posts the battery runs, the same-day windows, the set and the battery selection, and no run ids', () => {
      withBatteries({ ...CC_EMPTY_BATTERY_SCOPE, firstRunId: 11, leftOut: new Set([13]) });
      fixture.componentRef.setInput('range', { preset: '30d', fromDay: '', toDay: '', anchorUtc: '2026-10-08T12:00:00.000Z' });
      fixture.detectChanges();
      component.analyze();
      const post = http.expectOne(`${CC_API}/analyses`);
      const body = post.request.body;
      expect(body.baselineStartUtc).toBe('2026-10-08T00:00:00.000Z');
      expect(body.baselineEndUtc).toBe('2026-10-08T09:59:59.999Z');
      expect(body.comparisonStartUtc).toBe('2026-10-08T10:00:00.000Z');
      expect(body.comparisonEndUtc).toBe('2026-10-08T23:59:59.999Z');
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

    it('splits the same day at an annotation between the two battery runs', () => {
      withBatteries();
      fixture.componentRef.setInput('timeline', ccTimeline({ annotations: [ccAnnotation(5, { atUtc: '2026-10-08T08:00:00Z' })] }));
      fixture.detectChanges();
      clickPreset('annotation');
      expect(choices()).toEqual(['11', '11', '12', '12']);
      const request = component.buildRequest()!;
      expect(request.baselineEndUtc).toBe('2026-10-08T07:59:59.999Z');
      expect(request.comparisonStartUtc).toBe('2026-10-08T08:00:00.000Z');
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
      clickPreset('later');
      expect(textOf(el.querySelector('.cc-wiz-preset-note')))
        .toBe('This model has no saved analysis of Two initial suites (revision 1) yet; there is no earlier look to confirm.');
    });

    it('posts a suite set with the run ids, as before', () => {
      fixture.componentRef.setInput('compareSet', ccComparisonSets().sets[1]);
      fixture.detectChanges();
      const request = component.buildRequest()!;
      expect(request.comparisonSet).toEqual({ kind: 'suite', key: 'suite:id:5' });
      expect(request.baselineRunIds).toEqual([101, 102, 103]);
      expect(request.baselineBatteryRunIds).toBeUndefined();
      expect(request.runSelection!.firstBatteryRunId).toBeUndefined();
    });

    it('chooses the battery runs of a saved battery analysis from its units', () => {
      withBatteries();
      component.showResult(ccAnalysisResult({
        unitKind: 'batteryRun',
        comparisonSet: { kind: 'battery', key: CC_BATTERY_SET_KEY, label: 'Two initial suites (revision 1)' },
        units: [
          { unitId: 11, kind: 'batteryRun', period: 'baseline', startedAtUtc: '2026-10-08T06:00:00Z', memberRunIds: [301, 302] },
          { unitId: 12, kind: 'batteryRun', period: 'comparison', startedAtUtc: '2026-10-08T10:00:00Z', memberRunIds: [303, 304] }
        ]
      }));
      fixture.detectChanges();
      expect(component.ids).toEqual({ baselineFirstId: 11, baselineLastId: 11, comparisonFirstId: 12, comparisonLastId: 12 });
      expect(presetChecked('custom')).toBe(true);
    });
  });

  describe('a saved result', () => {
    it('loads a saved analysis without moving the step, for the outer wizard to show on Results', () => {
      let changes = 0;
      component.stateChange.subscribe(() => changes++);
      pick('cc-wiz-bl', '102');
      changes = 0;
      component.showResult(ccAnalysisResult());
      fixture.detectChanges();

      expect(changes).toBe(1);
      expect(component.step).toBe('analyze');
      expect(shownSteps()).toEqual(['analyze']);
      expect(choices()).toEqual(['101', '103', '104', '106']);
      expect(presetChecked('custom')).toBe(true);
      expect(component.reachable('results')).toBe(true);

      goToResults();
      expect(el.querySelector('app-cc-results-view')).not.toBeNull();
      expect(textOf(el.querySelector('.cc-headline-text'))).toContain('Overseer chat with GPT-5 high');
    });

    it('shows the Reports section after the results, under its own heading', () => {
      component.showResult(ccAnalysisResult());
      goToResults();
      const results = el.querySelector('app-cc-results-view')!;
      const heading = el.querySelector<HTMLElement>('h5#cc-res-reports-title')!;
      const reports = el.querySelector('app-cc-reports-step')!;
      expect(textOf(heading)).toBe('Reports');
      expect(heading.classList).toContain('cc-res-heading');
      expect(results.compareDocumentPosition(heading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(heading.compareDocumentPosition(reports) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(component.reportsStep).toBeDefined();
    });

    it('names the run choices once the saved analysis\'s runs arrive, keeping its controls', () => {
      fixture.componentRef.setInput('rows', []);
      fixture.detectChanges();
      component.showResult(ccAnalysisResult({ controls: { matches: [], effects: [], missingControls: [], controlRunIds: [205] } }));
      fixture.detectChanges();
      expect(choices()).toEqual(['', '', '', '']);
      expect(component.periodsError).toBe('Choose at least two runs in step 1, one for each period.');

      fixture.componentRef.setInput('rows', ccRunRows());
      fixture.detectChanges();
      expect(choices()).toEqual(['101', '103', '104', '106']);
      expect(component.preset).toBe('custom');
      expect(checkedControls()).toEqual(['Control run #205']);
    });

    it('leaves the choices unset when the saved analysis\'s runs are not in step 1, and still shows the result', () => {
      component.showResult(ccAnalysisResult({
        baseline: { ...ccAnalysisResult().baseline, runIds: [1, 2] },
        comparison: { ...ccAnalysisResult().comparison, runIds: [3] }
      }));
      fixture.detectChanges();
      expect(choices()).toEqual(['', '', '', '']);
      expect(component.periodsError).toBe('Choose the first and last run of both periods.');
      expect(component.reachable('results')).toBe(true);
    });

    it('reports chartsAttaching while the Reports section attaches charts', () => {
      expect(component.chartsAttaching).toBe(false);
      component.showResult(ccAnalysisResult());
      goToResults();

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
