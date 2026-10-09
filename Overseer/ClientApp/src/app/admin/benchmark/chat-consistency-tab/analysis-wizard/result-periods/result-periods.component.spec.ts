import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CcAnalysisResult } from '../../chat-consistency.models';
import {
  ccAnalysisResult,
  ccBatteryMemberRows,
  ccBatteryPoint,
  ccBatteryRunRows,
  ccRunRows,
  ccTimeline,
  ccUnitView,
  textOf
} from '../../chat-consistency-tab.testing';
import { CcResultPeriodsComponent } from './result-periods.component';

/** A battery analysis of battery run #11 against #12, both on 2026-10-08. */
function batteryResult(overrides: Partial<CcAnalysisResult> = {}): CcAnalysisResult {
  const base = ccAnalysisResult();
  return ccAnalysisResult({
    unitKind: 'batteryRun',
    units: [
      ccUnitView(12, 'comparison', { kind: 'batteryRun', startedAtUtc: '2026-10-08T10:00:00Z', memberRunIds: [303, 304] }),
      ccUnitView(11, 'baseline', { kind: 'batteryRun', startedAtUtc: '2026-10-08T06:00:00Z', memberRunIds: [301, 302] })
    ],
    baseline: { ...base.baseline, startUtc: '2026-10-08T00:00:00Z', endUtc: '2026-10-08T07:59:59.999Z', runIds: [301, 302], runCount: 2, days: ['2026-10-08'], legacyRunCount: 0 },
    comparison: { ...base.comparison, startUtc: '2026-10-08T08:00:00Z', endUtc: '2026-10-08T23:59:59.999Z', runIds: [303, 304], runCount: 2, days: ['2026-10-08'] },
    ...overrides
  });
}

describe('CcResultPeriodsComponent', () => {
  let fixture: ComponentFixture<CcResultPeriodsComponent>;
  let component: CcResultPeriodsComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CcResultPeriodsComponent] }).compileComponents();
    fixture = TestBed.createComponent(CcResultPeriodsComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    document.body.appendChild(el);
  });

  afterEach(() => {
    fixture.destroy();
    el.remove();
  });

  function setInputs(inputs: Record<string, unknown>): void {
    for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
    fixture.detectChanges();
  }

  const period = (name: 'baseline' | 'comparison'): HTMLElement =>
    el.querySelector<HTMLElement>(`section.cc-rp-period[data-period="${name}"]`)!;
  const facts = (name: 'baseline' | 'comparison'): [string, string][] =>
    Array.from(period(name).querySelectorAll('dl.bm-summary-facts > div'))
      .map(group => [textOf(group.querySelector('dt')), textOf(group.querySelector('dd'))]);
  const bounds = (name: 'baseline' | 'comparison'): HTMLButtonElement[] =>
    Array.from(period(name).querySelectorAll<HTMLButtonElement>('button.cc-rp-bound'));
  const unitCards = (): HTMLElement[] => Array.from(el.querySelectorAll<HTMLElement>('.cc-pu-card'));

  describe('a run analysis', () => {
    beforeEach(() => {
      setInputs({
        result: ccAnalysisResult({
          units: [101, 102, 103].map(id => ccUnitView(id, 'baseline', { startedAtUtc: ccRunRows().find(row => row.runId === id)!.startedAtUtc }))
            .concat([104, 105, 106].map(id => ccUnitView(id, 'comparison', { startedAtUtc: ccRunRows().find(row => row.runId === id)!.startedAtUtc })))
        }),
        rows: ccRunRows(),
        points: ccTimeline().points
      });
    });

    it('shows two period cards, each with its heading, dates, range and facts', () => {
      const sections = Array.from(el.querySelectorAll<HTMLElement>('.cc-rp-periods > section.cc-rp-period'));
      expect(sections.map(section => section.getAttribute('data-period'))).toEqual(['baseline', 'comparison']);
      expect(textOf(period('baseline').querySelector('h6#cc-rp-baseline-title'))).toBe('Baseline');
      expect(period('baseline').getAttribute('aria-labelledby')).toBe('cc-rp-baseline-title');
      expect(textOf(period('comparison').querySelector('h6'))).toBe('Comparison');

      expect(textOf(period('baseline').querySelector('.cc-rp-dates'))).toBe('2026-09-01 to 2026-09-14');
      expect(Array.from(period('baseline').querySelectorAll('.cc-rp-dates time')).map(time => time.getAttribute('datetime')))
        .toEqual(['2026-09-01', '2026-09-14']);
      expect(textOf(period('comparison').querySelector('.cc-rp-dates'))).toBe('2026-09-15 to 2026-10-01');

      expect(bounds('baseline').map(button => textOf(button))).toEqual(['First: run #101', 'Last: run #103']);
      expect(bounds('baseline')[0].getAttribute('aria-label')).toBe('First: run #101, open the run report');
      expect(bounds('comparison').map(button => textOf(button))).toEqual(['First: run #104', 'Last: run #106']);

      expect(facts('baseline')).toEqual([
        ['Runs', '3'], ['Days', '3'], ['Answers', '60'], ['Items', '20'], ['Suites', 'Board Suite'], ['Runs without telemetry', '1']
      ]);
      // No legacy run in the comparison: no telemetry fact.
      expect(facts('comparison')).toEqual([['Runs', '3'], ['Days', '3'], ['Answers', '60'], ['Items', '20'], ['Suites', 'Board Suite']]);
      expect(el.querySelector('.cc-rp-missing')).toBeNull();
    });

    it('opens the first and last run\'s report from the range', () => {
      const runs: number[] = [];
      const batteries: number[] = [];
      component.openRunReport.subscribe(id => runs.push(id));
      component.openBatteryRunReport.subscribe(id => batteries.push(id));
      bounds('baseline')[0].click();
      bounds('comparison')[1].click();
      expect(runs).toEqual([101, 106]);
      expect(batteries).toEqual([]);
    });

    it('lists the analyzed runs as read-only unit cards in their stored periods, details off', () => {
      expect(textOf(el.querySelector('#cc-res-units-title'))).toBe('Runs in the periods');
      expect(unitCards().map(card => card.getAttribute('data-unit-id'))).toEqual(['101', '102', '103', '104', '105', '106']);
      expect(unitCards().map(card => card.getAttribute('data-period')))
        .toEqual(['baseline', 'baseline', 'baseline', 'comparison', 'comparison', 'comparison']);
      expect(el.querySelector('.cc-pu-bound')).toBeNull();
      expect(el.querySelector('.cc-pu-details')).toBeNull();

      const toggle = el.querySelector<HTMLButtonElement>('.cc-pu-details-toggle')!;
      expect(toggle.getAttribute('aria-pressed')).toBe('false');
      toggle.click();
      fixture.detectChanges();
      expect(toggle.getAttribute('aria-pressed')).toBe('true');
      expect(el.querySelector('.cc-pu-details')).not.toBeNull();

      const runs: number[] = [];
      component.openRunReport.subscribe(id => runs.push(id));
      unitCards()[1].querySelector<HTMLButtonElement>('.cc-pu-report')!.click();
      expect(runs).toEqual([102]);
    });

    it('resolves the units once per result and rows', () => {
      const view = component.unitView;
      fixture.detectChanges();
      expect(component.unitView).toBe(view);
      expect(component.periods).toBe(component.periods);
      setInputs({ rows: ccRunRows() });
      expect(component.unitView).not.toBe(view);
    });
  });

  describe('a battery analysis', () => {
    beforeEach(() => {
      setInputs({
        result: batteryResult(),
        rows: ccBatteryMemberRows(),
        batteryRows: ccBatteryRunRows(),
        batteryPoints: [ccBatteryPoint(11, '2026-10-08T06:00:00Z'), ccBatteryPoint(12, '2026-10-08T10:00:00Z')]
      });
    });

    it('names battery runs, counts them and shows a one-day period as one date', () => {
      expect(textOf(period('baseline').querySelector('.cc-rp-dates'))).toBe('2026-10-08');
      expect(period('baseline').querySelectorAll('.cc-rp-dates time').length).toBe(1);
      expect(bounds('baseline').map(button => textOf(button))).toEqual(['First and last: battery run #11']);
      expect(bounds('baseline')[0].getAttribute('aria-label')).toBe('First and last: battery run #11, open the battery run report');
      expect(bounds('comparison').map(button => textOf(button))).toEqual(['First and last: battery run #12']);
      expect(facts('baseline').slice(0, 2)).toEqual([['Battery runs', '1'], ['Runs', '2']]);
      expect(textOf(el.querySelector('#cc-res-units-title'))).toBe('Battery runs in the periods');
      expect(unitCards().map(card => card.getAttribute('data-unit-id'))).toEqual(['11', '12']);
    });

    it('opens the battery run report from the range', () => {
      const runs: number[] = [];
      const batteries: number[] = [];
      component.openRunReport.subscribe(id => runs.push(id));
      component.openBatteryRunReport.subscribe(id => batteries.push(id));
      bounds('baseline')[0].click();
      bounds('comparison')[0].click();
      expect(batteries).toEqual([11, 12]);
      expect(runs).toEqual([]);
    });

    it('says how many battery runs step 1 did not load', () => {
      setInputs({
        result: batteryResult({
          units: [
            ccUnitView(11, 'baseline', { kind: 'batteryRun', startedAtUtc: '2026-10-08T06:00:00Z', memberRunIds: [301, 302] }),
            ccUnitView(40, 'comparison', { kind: 'batteryRun', memberRunIds: [4001] }),
            ccUnitView(41, 'comparison', { kind: 'batteryRun', memberRunIds: [4101] })
          ]
        })
      });
      expect(textOf(el.querySelector('p.form-hint.cc-rp-missing')))
        .toBe('2 battery runs of this analysis are not among the battery runs step 1 loaded. Widen step 1\'s dates to show them.');
      expect(bounds('comparison')).toEqual([]);
      expect(textOf(period('comparison').querySelector('.cc-rp-unset'))).toBe('Not among the loaded battery runs');
      expect(unitCards().map(card => card.getAttribute('data-unit-id'))).toEqual(['11']);
    });
  });

  it('says a run of the analysis is missing, in the singular', () => {
    setInputs({
      result: ccAnalysisResult({
        units: [
          ccUnitView(101, 'baseline', { startedAtUtc: '2026-09-01T08:00:00Z' }),
          ccUnitView(999, 'comparison', { startedAtUtc: '2026-09-30T08:00:00Z' })
        ]
      }),
      rows: ccRunRows()
    });
    expect(textOf(el.querySelector('.cc-rp-missing')))
      .toBe('1 run of this analysis is not among the runs step 1 loaded. Widen step 1\'s dates to show it.');
    expect(bounds('baseline').map(button => textOf(button))).toEqual(['First and last: run #101']);
  });

  it('reads the periods\' run ids in an analysis without stored units', () => {
    setInputs({ result: ccAnalysisResult(), rows: ccRunRows(), points: ccTimeline().points });
    expect(ccAnalysisResult().units).toBeUndefined();
    expect(bounds('baseline').map(button => textOf(button))).toEqual(['First: run #101', 'Last: run #103']);
    expect(bounds('comparison').map(button => textOf(button))).toEqual(['First: run #104', 'Last: run #106']);
    expect(unitCards().map(card => card.getAttribute('data-period')))
      .toEqual(['baseline', 'baseline', 'baseline', 'comparison', 'comparison', 'comparison']);
    expect(el.querySelector('.cc-rp-missing')).toBeNull();
  });

  it('renders no unit list when none of the units was loaded', () => {
    setInputs({ result: ccAnalysisResult(), rows: [] });
    expect(el.querySelector('app-cc-period-units')).toBeNull();
    expect(textOf(period('baseline').querySelector('.cc-rp-unset'))).toBe('Not among the loaded runs');
    expect(textOf(el.querySelector('.cc-rp-missing')))
      .toBe('6 runs of this analysis are not among the runs step 1 loaded. Widen step 1\'s dates to show them.');
  });
});
