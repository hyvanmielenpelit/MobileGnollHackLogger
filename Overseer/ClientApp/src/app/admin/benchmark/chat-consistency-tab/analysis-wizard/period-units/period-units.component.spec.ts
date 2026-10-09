import { ComponentFixture, TestBed } from '@angular/core/testing';

import { groupOverseerEvents, taggedAnnotations } from '../../chat-consistency-events';
import {
  CC_NO_PERIOD_IDS,
  CcPeriodBound,
  CcPeriodIds,
  CcPeriodUnit,
  CcUnitPeriod,
  ccPeriodAssignment,
  ccPeriodUnits
} from '../../chat-consistency-periods';
import { CcRunRow } from '../../chat-consistency.models';
import {
  ccBatteryPoint,
  ccBatteryRunRows,
  ccEventPoints,
  ccEventTimeline,
  ccRunRow,
  ccRunRows,
  ccTimeline,
  textOf
} from '../../chat-consistency-tab.testing';
import { CcPeriodUnitsComponent } from './period-units.component';

/** The run table of the composite-event fixture: runs 201–206. */
function eventRows(): CcRunRow[] {
  return ccEventPoints().map(point => ccRunRow(point.runId, point.startedAtUtc));
}

describe('CcPeriodUnitsComponent', () => {
  let fixture: ComponentFixture<CcPeriodUnitsComponent>;
  let component: CcPeriodUnitsComponent;
  let el: HTMLElement;
  let units: CcPeriodUnit[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CcPeriodUnitsComponent] }).compileComponents();
    fixture = TestBed.createComponent(CcPeriodUnitsComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    document.body.appendChild(el);
    units = ccPeriodUnits(ccRunRows(), [], false);
    fixture.componentRef.setInput('units', units);
    fixture.componentRef.setInput('points', ccTimeline().points);
    fixture.detectChanges();
  });

  afterEach(() => {
    fixture.destroy();
    el.remove();
  });

  function setInputs(inputs: Record<string, unknown>): void {
    for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
    fixture.detectChanges();
  }

  const card = (id: number): HTMLElement => el.querySelector<HTMLElement>(`.cc-pu-card[data-unit-id="${id}"]`)!;
  const cardIds = (): (string | null)[] =>
    Array.from(el.querySelectorAll<HTMLElement>('.cc-pu-card')).map(article => article.getAttribute('data-unit-id'));
  const bounds = (id: number): HTMLButtonElement[] => Array.from(card(id).querySelectorAll<HTMLButtonElement>('.cc-pu-bound'));
  const bound = (id: number, key: CcPeriodBound): HTMLButtonElement =>
    card(id).querySelector<HTMLButtonElement>(`.cc-pu-bound[data-bound="${key}"]`)!;
  const metric = (id: number, key: string): string => textOf(card(id).querySelector(`.cc-pu-metric[data-metric="${key}"] .cc-pu-metric-value`));
  const metricNote = (id: number, key: string): string | null => {
    const note = card(id).querySelector(`.cc-pu-metric[data-metric="${key}"] .cc-pu-metric-note`);
    return note ? textOf(note) : null;
  };
  const fact = (id: number, key: string): string => textOf(card(id).querySelector(`.cc-run-fact[data-fact="${key}"] dd`));
  /** Each row of the list: a card's unit id, or a marker's tag. */
  const listOrder = (): string[] =>
    Array.from(el.querySelectorAll<HTMLElement>('.cc-pu-list > li')).map(li =>
      li.classList.contains('cc-pu-marker')
        ? textOf(li.querySelector('.cc-marker-tag'))
        : li.querySelector('.cc-pu-card')!.getAttribute('data-unit-id') ?? '');
  const marker = (tag: string): HTMLElement =>
    Array.from(el.querySelectorAll<HTMLElement>('.cc-pu-marker')).find(li => textOf(li.querySelector('.cc-marker-tag')) === tag)!;

  describe('head', () => {
    it('names the units and offers Show run details as a pressed toggle that emits its opposite', () => {
      expect(textOf(el.querySelector('#cc-an-units-title'))).toBe('Runs');
      expect(el.querySelector('.cc-pu-list')!.getAttribute('aria-labelledby')).toBe('cc-an-units-title');
      const toggle = el.querySelector<HTMLButtonElement>('.cc-pu-details-toggle')!;
      expect(textOf(toggle)).toBe('Show run details');
      expect(toggle.getAttribute('aria-pressed')).toBe('true');

      const emitted: boolean[] = [];
      component.detailsChange.subscribe(value => emitted.push(value));
      toggle.click();
      expect(emitted).toEqual([false]);

      setInputs({ details: false });
      expect(toggle.getAttribute('aria-pressed')).toBe('false');
      expect(el.querySelector('.cc-pu-details')).toBeNull();
      toggle.click();
      expect(emitted).toEqual([false, true]);
    });

    it('shows each run card\'s facts while details are on', () => {
      expect(fact(106, 'segment')).toBe('1');
      expect(fact(106, 'telemetry')).toBe('Recorded');
      expect(fact(103, 'telemetry')).toBe('Legacy');
      expect(fact(106, 'regrade')).toBe('Native grades only');
      expect(fact(106, 'controls')).toBe('#206');
      expect(fact(106, 'strata')).toBe('weekday 08–12 UTC');

      const points = ccTimeline().points.map(point => point.runId === 106 ? { ...point, strataEstimated: true } : point);
      setInputs({ points });
      expect(fact(106, 'strata')).toBe('weekday 08–12 UTC (estimated)');
    });
  });

  describe('cards', () => {
    it('lists the units oldest first, each with its period as data-period and as a word', () => {
      const assignment = new Map<number, CcUnitPeriod>([
        [101, 'baseline'], [102, 'baseline'], [103, 'notEligible'], [105, 'comparison'], [106, 'comparison']
      ]);
      setInputs({ assignment });

      expect(cardIds()).toEqual(['101', '102', '103', '104', '105', '106']);
      expect(Array.from(el.querySelectorAll('.cc-pu-card')).map(article => article.getAttribute('data-period')))
        .toEqual(['baseline', 'baseline', 'notEligible', 'notUsed', 'comparison', 'comparison']);
      expect(Array.from(el.querySelectorAll('.cc-pu-period-tag')).map(tag => textOf(tag)))
        .toEqual(['Baseline', 'Baseline', 'Not eligible', 'Not used', 'Comparison', 'Comparison']);
      expect(card(101).getAttribute('aria-labelledby')).toBe('cc-pu-101-title');
      // An ineligible unit's toggles stay operable.
      expect(bounds(103).every(button => !button.disabled && button.getAttribute('aria-disabled') === null)).toBe(true);
    });

    it('shows a run\'s kicker, title and meta line', () => {
      const rows = ccRunRows().map(row => row.runId === 104
        ? ccRunRow(104, row.startedAtUtc, { isAnchor: true, batteryRunId: 12, batterySuitePosition: 1, batterySuiteCount: 2 })
        : row);
      setInputs({ units: ccPeriodUnits(rows, [], false) });

      expect(textOf(card(101).querySelector('.cc-run-id'))).toBe('#101');
      expect(textOf(card(101).querySelector('.cc-run-status'))).toBe('Completed');
      expect(textOf(card(101).querySelector('.cc-run-harness'))).toBe('Harness 30');
      expect(textOf(card(101).querySelector('.cc-recorded-tag'))).toBe('Recorded');
      expect(textOf(card(103).querySelector('.cc-legacy-tag'))).toBe('Legacy');
      expect(card(101).querySelector('.cc-anchor-tag')).toBeNull();
      expect(textOf(card(104).querySelector('.cc-anchor-tag'))).toBe('Anchor');
      expect(textOf(card(104).querySelector('.cc-member-tag'))).toBe('Battery run #12 · suite 1 of 2');
      expect(textOf(card(101).querySelector('h6.cc-run-title#cc-pu-101-title'))).toBe('Board Suite');
      expect(card(101).querySelector('time.cc-run-time')!.getAttribute('datetime')).toBe('2026-09-01T08:00:00Z');
      expect(textOf(card(101).querySelector('.cc-run-time'))).toBe('2026-09-01 08:00 UTC');
      expect(textOf(card(101).querySelector('.cc-served'))).toBe('Served: gpt-5-2026-08 (40 calls)');
    });

    it('opens a run\'s report from its eye button, with a hint tooltip', () => {
      const opened: number[] = [];
      component.openRunReport.subscribe(id => opened.push(id));
      const button = card(101).querySelector<HTMLButtonElement>('.cc-pu-report')!;
      expect(button.getAttribute('aria-label')).toBe('Open the run report of run #101');
      expect(button.getAttribute('interestfor')).toBe('cc-pu-101-report-tip');
      expect(button.getAttribute('style')).toContain('anchor-name: --cc-pu-101-report-tip');
      expect(textOf(el.querySelector('#cc-pu-101-report-tip'))).toBe('Open run report');
      button.click();
      expect(opened).toEqual([101]);
    });
  });

  describe('bounds', () => {
    it('offers four toggles named for the unit, pressed where the bound is', () => {
      setInputs({ ids: { ...CC_NO_PERIOD_IDS, baselineFirstId: 101, comparisonLastId: 106 } });

      const group = card(102).querySelector('.cc-pu-bounds')!;
      expect(group.getAttribute('role')).toBe('group');
      expect(group.getAttribute('aria-label')).toBe('Period bounds for run #102');
      expect(Array.from(group.querySelectorAll('.cc-pu-bound-label')).map(label => textOf(label))).toEqual(['Baseline', 'Comparison']);
      expect(bounds(102).map(button => button.getAttribute('data-bound')))
        .toEqual(['baselineFirstId', 'baselineLastId', 'comparisonFirstId', 'comparisonLastId']);
      expect(bounds(102).map(button => textOf(button))).toEqual(['First', 'Last', 'First', 'Last']);
      expect(bounds(102).map(button => button.getAttribute('aria-label'))).toEqual([
        'Make run #102 the baseline\'s first run',
        'Make run #102 the baseline\'s last run',
        'Make run #102 the comparison\'s first run',
        'Make run #102 the comparison\'s last run'
      ]);

      expect(bounds(101).map(button => button.getAttribute('aria-pressed'))).toEqual(['true', 'false', 'false', 'false']);
      expect(bounds(106).map(button => button.getAttribute('aria-pressed'))).toEqual(['false', 'false', 'false', 'true']);
      expect(bounds(102).every(button => button.getAttribute('aria-pressed') === 'false')).toBe(true);
      expect(bound(101, 'baselineFirstId').querySelector('svg.btn-icon')).not.toBeNull();
      expect(bound(101, 'baselineLastId').querySelector('svg.btn-icon')).toBeNull();
      expect(bound(106, 'comparisonLastId').getAttribute('data-period')).toBe('comparison');
    });

    it('emits the bound and the unit on a click, and changes nothing itself', () => {
      const emitted: { key: CcPeriodBound; unitId: number }[] = [];
      component.boundChange.subscribe(change => emitted.push(change));
      bound(104, 'comparisonLastId').click();
      fixture.detectChanges();
      expect(emitted).toEqual([{ key: 'comparisonLastId', unitId: 104 }]);
      expect(bound(104, 'comparisonLastId').getAttribute('aria-pressed')).toBe('false');
    });

    it('keeps the pressed button, and focus on it, when the host answers with new bounds and periods', () => {
      const ids: CcPeriodIds = { baselineFirstId: 101, baselineLastId: 102, comparisonFirstId: 105, comparisonLastId: 106 };
      setInputs({ ids, assignment: ccPeriodAssignment(units, ids) });
      const article = card(104);
      const button = bound(104, 'comparisonFirstId');
      const items = component.items;
      button.focus();
      component.boundChange.subscribe(({ key, unitId }) => {
        const next: CcPeriodIds = { ...ids, [key]: unitId };
        setInputs({ ids: next, assignment: ccPeriodAssignment(units, next) });
      });

      button.click();

      expect(card(104)).toBe(article);
      expect(bound(104, 'comparisonFirstId')).toBe(button);
      expect(document.activeElement).toBe(button);
      expect(button.getAttribute('aria-pressed')).toBe('true');
      expect(card(104).getAttribute('data-period')).toBe('comparison');
      expect(component.items).toBe(items);
    });
  });

  describe('markers', () => {
    function useEventFixture(rows: CcRunRow[]): void {
      const timeline = ccEventTimeline();
      setInputs({
        units: ccPeriodUnits(rows, [], false),
        points: timeline.points,
        eventGroups: groupOverseerEvents(timeline.events, timeline.points),
        annotations: taggedAnnotations(timeline.annotations)
      });
    }

    it('places each Overseer change and annotation before the first run at or after it', () => {
      useEventFixture(eventRows());
      // A1 (09-02) after run 201; E1 (09-03 08:00) with run 202; E2 and A2 at run 204's start, the
      // event first; E3 (09-09) and E4 (09-10 08:00) before run 206.
      expect(listOrder()).toEqual(['201', 'A1', 'E1', '202', '203', 'E2', 'A2', '204', '205', 'E3', 'E4', '206']);
      expect(el.querySelectorAll('.cc-pu-marker[data-marker-kind="event"]').length).toBe(4);
      expect(el.querySelectorAll('.cc-pu-marker[data-marker-kind="annotation"]').length).toBe(2);
    });

    it('leaves out the markers before the first unit and after the last', () => {
      useEventFixture(eventRows().filter(row => row.runId >= 203 && row.runId <= 205));
      expect(listOrder()).toEqual(['203', 'E2', 'A2', '204', '205']);
    });

    it('shows a marker\'s tag, day, title and kinds', () => {
      useEventFixture(eventRows());
      const e2 = marker('E2');
      expect(e2.querySelector('.cc-marker-tag')!.classList.contains('is-event')).toBe(true);
      expect(e2.querySelector('time')!.getAttribute('datetime')).toBe('2026-09-05');
      expect(textOf(e2.querySelector('.cc-pu-marker-title'))).toBe('Harness 27 → 28');
      expect(textOf(e2.querySelector('.cc-pu-marker-kinds'))).toBe('Tool guides');

      const a1 = marker('A1');
      expect(a1.getAttribute('data-marker-kind')).toBe('annotation');
      expect(a1.querySelector('.cc-marker-tag')!.classList.contains('is-annotation')).toBe(true);
      expect(textOf(a1.querySelector('time'))).toBe('2026-09-02');
      expect(textOf(a1.querySelector('.cc-pu-marker-title'))).toBe('Provider reported elevated latency');
      expect(textOf(a1.querySelector('.cc-pu-marker-kinds'))).toBe('Provider statement');
    });

    it('emits the change or the annotation from Split here', () => {
      useEventFixture(eventRows());
      const emitted: { kind: 'event' | 'annotation'; key: string }[] = [];
      component.splitAt.subscribe(split => emitted.push(split));

      const e2 = marker('E2').querySelector<HTMLButtonElement>('.cc-pu-split')!;
      expect(textOf(e2)).toBe('Split here');
      expect(e2.getAttribute('aria-label')).toBe('Split the periods at Overseer change E2');
      e2.click();
      const a1 = marker('A1').querySelector<HTMLButtonElement>('.cc-pu-split')!;
      expect(a1.getAttribute('aria-label')).toBe('Split the periods at annotation A1');
      a1.click();

      expect(emitted).toEqual([{ kind: 'event', key: '2026-09-05|28' }, { kind: 'annotation', key: '11' }]);
    });
  });

  describe('metrics', () => {
    it('shows the figures of each run\'s timeline point', () => {
      expect(metric(101, 'intelligence')).toBe('71.0');
      expect(metric(101, 'firstAnswer')).toBe('2.4 s');
      expect(metric(101, 'streaming')).toBe('40.0 tok/s');
      expect(metric(101, 'work')).toBe('1,200');
      expect(metricNote(101, 'work')).toBe('tokens per answer');
      expect(metric(101, 'cost')).toBe('$0.015');
      expect(metric(101, 'answers')).toBe('20');
      expect(Array.from(card(101).querySelectorAll('.cc-pu-metric dt')).map(dt => textOf(dt)))
        .toEqual(['Intelligence', 'First answer', 'Streaming', 'Work', 'Cost / question', 'Answers']);
      // Run 103 is legacy: no telemetry speed.
      expect(metric(103, 'firstAnswer')).toBe('—');
      expect(metric(103, 'streaming')).toBe('—');
      expect(metricNote(101, 'streaming')).toBeNull();
    });

    it('marks an estimated streaming rate', () => {
      setInputs({ points: ccTimeline().points.map(point => point.runId === 102 ? { ...point, streamingRateEstimated: true } : point) });
      expect(metricNote(102, 'streaming')).toBe('estimated');
    });

    it('reads — for a unit without a point', () => {
      setInputs({ points: ccTimeline().points.filter(point => point.runId !== 106) });
      for (const key of ['intelligence', 'firstAnswer', 'streaming', 'work', 'cost', 'answers']) {
        expect(metric(106, key)).toBe('—');
      }
      expect(card(106).querySelector('.cc-pu-metric-note')).toBeNull();
      expect(fact(106, 'strata')).toBe('—');
    });
  });

  describe('read-only', () => {
    it('renders no bounds and no Split here, under its own ids, and emits neither', () => {
      const timeline = ccEventTimeline();
      setInputs({
        readonly: true,
        units: ccPeriodUnits(eventRows(), [], false),
        points: timeline.points,
        eventGroups: groupOverseerEvents(timeline.events, timeline.points),
        annotations: taggedAnnotations(timeline.annotations),
        ids: { baselineFirstId: 201, baselineLastId: 203, comparisonFirstId: 204, comparisonLastId: 206 }
      });

      expect(el.querySelectorAll('.cc-pu-card').length).toBe(6);
      expect(el.querySelectorAll('.cc-pu-marker').length).toBe(6);
      expect(el.querySelector('.cc-pu-bounds')).toBeNull();
      expect(el.querySelector('.cc-pu-bound')).toBeNull();
      expect(el.querySelector('.cc-pu-split')).toBeNull();
      expect(el.querySelector('.cc-pu-list')!.classList.contains('is-readonly')).toBe(true);

      expect(el.querySelector('#cc-an-units-title')).toBeNull();
      expect(textOf(el.querySelector('#cc-res-units-title'))).toBe('Runs in the periods');
      expect(el.querySelector('.cc-pu-list')!.getAttribute('aria-labelledby')).toBe('cc-res-units-title');
      expect(card(201).getAttribute('aria-labelledby')).toBe('cc-res-pu-201-title');
      expect(el.querySelector('#cc-pu-201-title')).toBeNull();

      const bounds: unknown[] = [];
      const splits: unknown[] = [];
      component.boundChange.subscribe(change => bounds.push(change));
      component.splitAt.subscribe(split => splits.push(split));
      component.onBound('baselineFirstId', 202);
      const first = component.items.find(item => item.kind === 'marker');
      expect(first?.kind).toBe('marker');
      if (first?.kind === 'marker') component.onSplit(first.marker);
      expect(bounds).toEqual([]);
      expect(splits).toEqual([]);

      component.focusUnit(203);
      expect(document.activeElement).toBe(el.querySelector('#cc-res-pu-203-title'));
    });

    it('keeps the eye buttons, which still emit, with their own tooltip ids', () => {
      setInputs({ readonly: true });
      const opened: number[] = [];
      component.openRunReport.subscribe(id => opened.push(id));
      const button = card(101).querySelector<HTMLButtonElement>('.cc-pu-report')!;
      expect(button.getAttribute('aria-label')).toBe('Open the run report of run #101');
      expect(button.getAttribute('interestfor')).toBe('cc-res-pu-101-report-tip');
      expect(button.getAttribute('style')).toContain('anchor-name: --cc-res-pu-101-report-tip');
      expect(textOf(el.querySelector('#cc-res-pu-101-report-tip'))).toBe('Open run report');
      button.click();
      expect(opened).toEqual([101]);

      setInputs({
        batteryMode: true,
        units: ccPeriodUnits([], ccBatteryRunRows(), true),
        points: [],
        batteryPoints: [ccBatteryPoint(11, '2026-10-08T06:00:00Z'), ccBatteryPoint(12, '2026-10-08T10:00:00Z')]
      });
      const batteries: number[] = [];
      component.openBatteryRunReport.subscribe(id => batteries.push(id));
      expect(textOf(el.querySelector('#cc-res-units-title'))).toBe('Battery runs in the periods');
      card(12).querySelector<HTMLButtonElement>('.cc-pu-report')!.click();
      const member = card(12).querySelector<HTMLButtonElement>('.cc-battery-member[data-run-id="304"] button')!;
      expect(member.getAttribute('interestfor')).toBe('cc-res-pu-member-304-report-tip');
      member.click();
      expect(batteries).toEqual([12]);
      expect(opened).toEqual([101, 304]);
    });
  });

  describe('battery mode', () => {
    beforeEach(() => {
      setInputs({
        batteryMode: true,
        units: ccPeriodUnits([], ccBatteryRunRows(), true),
        points: [],
        batteryPoints: [
          ccBatteryPoint(11, '2026-10-08T06:00:00Z'),
          ccBatteryPoint(12, '2026-10-08T10:00:00Z', { overallIndex: null, overallIndexNote: 'No current battery analysis' })
        ]
      });
    });

    it('lists the battery runs oldest first, each with its battery, harness and suites', () => {
      expect(textOf(el.querySelector('#cc-an-units-title'))).toBe('Battery runs');
      expect(cardIds()).toEqual(['11', '12']);
      expect(textOf(card(12).querySelector('.cc-run-title'))).toBe('Two initial suites');
      expect(textOf(card(12).querySelector('.cc-run-status'))).toBe('Completed');
      expect(textOf(card(12).querySelector('.cc-run-harness'))).toBe('Harness 54');
      expect(card(12).querySelector('.cc-recorded-tag')).toBeNull();
      expect(textOf(card(12).querySelector('.cc-battery-suites'))).toBe('2 of 2 suites · revision 1');
      expect(card(12).querySelector('.cc-elig-list')!.getAttribute('aria-label')).toBe('Eligibility of battery run #12');
    });

    it('names the toggles for the battery run', () => {
      expect(card(12).querySelector('.cc-pu-bounds')!.getAttribute('aria-label')).toBe('Period bounds for battery run #12');
      expect(bound(12, 'comparisonFirstId').getAttribute('aria-label')).toBe('Make battery run #12 the comparison\'s first run');
    });

    it('shows the Overall Index, or — with the reason it is missing', () => {
      expect(metric(11, 'intelligence')).toBe('80.0');
      expect(metricNote(11, 'intelligence')).toBeNull();
      expect(metric(12, 'intelligence')).toBe('—');
      expect(metricNote(12, 'intelligence')).toBe('No current battery analysis');
      expect(metric(12, 'answers')).toBe('40');
    });

    it('lists the members with their report buttons, and the matched controls', () => {
      const opened: number[] = [];
      component.openRunReport.subscribe(id => opened.push(id));
      const members = card(12).querySelector('.cc-battery-members')!;
      expect(members.getAttribute('aria-label')).toBe('Member runs of battery run #12');
      expect(Array.from(members.querySelectorAll('.cc-battery-member-text')).map(text => textOf(text))).toEqual([
        '#303 · Board Suite · Completed · harness 54',
        '#304 · Wiki Suite · Completed · harness 54'
      ]);
      const report = members.querySelector<HTMLButtonElement>('.cc-battery-member[data-run-id="304"] button')!;
      expect(report.getAttribute('aria-label')).toBe('Open the run report of run #304');
      expect(report.getAttribute('interestfor')).toBe('cc-pu-member-304-report-tip');
      report.click();
      expect(opened).toEqual([304]);

      expect(fact(12, 'controls')).toBe('#404');
      expect(fact(11, 'controls')).toBe('None');
      expect(card(12).querySelector('.cc-run-fact[data-fact="segment"]')).toBeNull();
    });

    it('opens the battery run report from the card\'s eye button', () => {
      const opened: number[] = [];
      const runs: number[] = [];
      component.openBatteryRunReport.subscribe(id => opened.push(id));
      component.openRunReport.subscribe(id => runs.push(id));
      const button = card(12).querySelector<HTMLButtonElement>('.cc-pu-report')!;
      expect(button.getAttribute('aria-label')).toBe('Open the battery run report of battery run #12');
      expect(textOf(el.querySelector('#cc-pu-12-report-tip'))).toBe('Open battery run report');
      button.click();
      expect(opened).toEqual([12]);
      expect(runs).toEqual([]);
    });

    it('hides the details while they are off', () => {
      setInputs({ details: false });
      expect(el.querySelector('.cc-pu-details')).toBeNull();
      expect(el.querySelector('.cc-battery-members')).toBeNull();
    });

    it('focuses a unit\'s title on focusUnit', () => {
      component.focusUnit(12);
      expect(document.activeElement).toBe(el.querySelector('#cc-pu-12-title'));
    });
  });
});
