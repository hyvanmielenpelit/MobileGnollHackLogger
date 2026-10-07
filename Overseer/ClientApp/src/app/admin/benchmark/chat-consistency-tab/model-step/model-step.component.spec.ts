import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CcRunRow } from '../chat-consistency.models';
import {
  ccAxis,
  ccRunRow,
  ccRunRows,
  ccTimeline,
  chatConsistencyTestProviders,
  textOf
} from '../chat-consistency-tab.testing';
import { CcDayRange, CcModelStepComponent, axisPickerOptions } from './model-step.component';

describe('CcModelStepComponent', () => {
  let fixture: ComponentFixture<CcModelStepComponent>;
  let component: CcModelStepComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CcModelStepComponent],
      providers: chatConsistencyTestProviders()
    }).compileComponents();
    fixture = TestBed.createComponent(CcModelStepComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('axes', [ccAxis(), ccAxis({ key: 'empty', displayName: 'No runs', runCount: 0 })]);
    fixture.detectChanges();
  });

  afterEach(() => fixture.destroy());

  /** The chosen model with its timeline and run table, as the host passes them. */
  function withModel(rows: CcRunRow[] = ccRunRows()): void {
    fixture.componentRef.setInput('selectedKey', 'openai/gpt-5|high');
    fixture.componentRef.setInput('timeline', ccTimeline());
    fixture.componentRef.setInput('rows', rows);
    fixture.detectChanges();
  }

  function openPicker(): HTMLElement[] {
    el.querySelector<HTMLButtonElement>('.cc-subject-model-selector .selector-trigger')!.click();
    fixture.detectChanges();
    return Array.from(el.querySelectorAll<HTMLElement>('.cc-subject-model-selector [role="option"]'));
  }

  function setDay(id: 'cc-tl-from' | 'cc-tl-to', value: string): void {
    const input = el.querySelector<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  const row = (runId: number): HTMLTableRowElement =>
    el.querySelector<HTMLTableRowElement>(`.cc-run-table tr[data-run-id="${runId}"]`)!;

  describe('the fields', () => {
    it('labels the picker Model and the dates From (UTC) and To (UTC), without a legend', () => {
      expect(textOf(el.querySelector('#cc-tl-model-label'))).toBe('Model');
      const trigger = el.querySelector<HTMLButtonElement>('.cc-subject-model-selector .selector-trigger')!;
      expect(trigger.getAttribute('aria-labelledby')!.split(' ')[0]).toBe('cc-tl-model-label');
      expect(trigger.getAttribute('aria-describedby')).toBe('cc-tl-model-hint');
      expect(textOf(el.querySelector('#cc-tl-model-hint'))).toBe('Models with at least one usable benchmark run.');

      expect(textOf(el.querySelector('label[for="cc-tl-from"]'))).toBe('From (UTC)');
      expect(textOf(el.querySelector('label[for="cc-tl-to"]'))).toBe('To (UTC)');
      expect(el.querySelector<HTMLInputElement>('#cc-tl-from')!.type).toBe('date');
      expect(el.querySelector<HTMLInputElement>('#cc-tl-to')!.type).toBe('date');
      expect(el.querySelector('legend')).toBeNull();
      expect(el.querySelector('fieldset')).toBeNull();
      // No bound is set, so there is nothing for Every date to clear.
      expect(el.querySelector('.cc-tl-range-clear')).toBeNull();
    });

    it('offers only the models that have runs, and asks for the one chosen', () => {
      const chosen: string[] = [];
      component.modelChange.subscribe(key => chosen.push(key));
      expect(axisPickerOptions([ccAxis(), ccAxis({ key: 'empty', runCount: 0 })]).map(option => option.key))
        .toEqual(['openai/gpt-5|high']);

      const options = openPicker();
      expect(options.map(option => textOf(option))).toEqual([expect.stringContaining('GPT-5 high')]);
      options[0].click();
      fixture.detectChanges();
      expect(chosen).toEqual(['openai/gpt-5|high']);
    });

    it('does not ask again for the model already chosen, and describes it in the hint', () => {
      const chosen: string[] = [];
      component.modelChange.subscribe(key => chosen.push(key));
      fixture.componentRef.setInput('selectedKey', 'openai/gpt-5|high');
      fixture.detectChanges();
      expect(textOf(el.querySelector('#cc-tl-model-hint'))).toBe('6 runs, 4 with call telemetry');

      openPicker()[0].click();
      fixture.detectChanges();
      expect(chosen).toEqual([]);
    });

    it('shows a model list error under the picker and as the empty list\'s hint', () => {
      fixture.componentRef.setInput('axes', []);
      fixture.componentRef.setInput('axesError', 'The models could not be loaded.');
      fixture.detectChanges();
      expect(textOf(el.querySelector('.cc-tl-axes-error'))).toBe('The models could not be loaded.');
      expect(openPicker()).toEqual([]);
      expect(textOf(el.querySelector('.cc-subject-model-selector .empty-dropdown-hint'))).toBe('The models could not be loaded.');
    });

    it('says the models are loading in the empty list', () => {
      fixture.componentRef.setInput('axes', []);
      fixture.componentRef.setInput('axesLoading', true);
      fixture.detectChanges();
      openPicker();
      expect(textOf(el.querySelector('.cc-subject-model-selector .empty-dropdown-hint'))).toBe('Loading the models…');
    });
  });

  describe('the date range', () => {
    let ranges: CcDayRange[];

    beforeEach(() => {
      ranges = [];
      component.rangeChange.subscribe(range => ranges.push(range));
    });

    it('applies a valid bound at once and offers Every date while a bound is set', () => {
      setDay('cc-tl-from', '2026-09-10');
      expect(ranges).toEqual([{ fromDay: '2026-09-10', toDay: '' }]);
      expect(el.querySelector('#cc-tl-range-error')).toBeNull();
      const clear = el.querySelector<HTMLButtonElement>('.cc-tl-range-clear')!;
      expect(textOf(clear)).toBe('Every date');
      expect(clear.classList).toContain('btn-ghost');

      setDay('cc-tl-to', '2026-09-30');
      expect(ranges).toEqual([{ fromDay: '2026-09-10', toDay: '' }, { fromDay: '2026-09-10', toDay: '2026-09-30' }]);
    });

    it('explains a reversed range on both inputs and does not apply it', () => {
      setDay('cc-tl-from', '2026-09-10');
      setDay('cc-tl-to', '2026-09-05');
      expect(ranges).toEqual([{ fromDay: '2026-09-10', toDay: '' }]);
      expect(textOf(el.querySelector('#cc-tl-range-error'))).toBe('The start date must not be after the end date.');
      for (const id of ['cc-tl-from', 'cc-tl-to']) {
        const input = el.querySelector<HTMLInputElement>(`#${id}`)!;
        expect(input.getAttribute('aria-invalid')).toBe('true');
        expect(input.getAttribute('aria-describedby')).toBe('cc-tl-range-error');
      }

      setDay('cc-tl-to', '2026-09-12');
      expect(el.querySelector('#cc-tl-range-error')).toBeNull();
      expect(el.querySelector<HTMLInputElement>('#cc-tl-from')!.hasAttribute('aria-invalid')).toBe(false);
      expect(ranges[ranges.length - 1]).toEqual({ fromDay: '2026-09-10', toDay: '2026-09-12' });
    });

    it('explains a date that is not a complete calendar date', () => {
      // A date input never holds such a value; the handler is reached with one directly.
      component.onDayChange('from', { target: { value: '2026-9-1' } } as unknown as Event);
      fixture.detectChanges();
      expect(textOf(el.querySelector('#cc-tl-range-error'))).toBe('Enter the dates as complete calendar dates.');
      expect(ranges).toEqual([]);
    });

    it('clears both bounds and the error with Every date', () => {
      setDay('cc-tl-from', '2026-09-10');
      setDay('cc-tl-to', '2026-09-05');
      el.querySelector<HTMLButtonElement>('.cc-tl-range-clear')!.click();
      fixture.detectChanges();

      expect(ranges[ranges.length - 1]).toEqual({ fromDay: '', toDay: '' });
      expect(el.querySelector('#cc-tl-range-error')).toBeNull();
      expect(el.querySelector<HTMLInputElement>('#cc-tl-from')!.value).toBe('');
      expect(el.querySelector<HTMLInputElement>('#cc-tl-to')!.value).toBe('');
      expect(el.querySelector('.cc-tl-range-clear')).toBeNull();
    });

    it('shows the range the host passes, and drops a pending error with it', () => {
      setDay('cc-tl-from', '2026-09-10');
      setDay('cc-tl-to', '2026-09-05');
      fixture.componentRef.setInput('range', { fromDay: '2026-09-01', toDay: '2026-09-30' });
      fixture.detectChanges();
      expect(el.querySelector<HTMLInputElement>('#cc-tl-from')!.value).toBe('2026-09-01');
      expect(el.querySelector<HTMLInputElement>('#cc-tl-to')!.value).toBe('2026-09-30');
      expect(el.querySelector('#cc-tl-range-error')).toBeNull();
      expect(el.querySelector('.cc-tl-range-clear')).not.toBeNull();
    });
  });

  describe('the status line and the runs heading', () => {
    it('says the timeline is loading, then counts the runs under a Runs of heading', () => {
      fixture.componentRef.setInput('selectedKey', 'openai/gpt-5|high');
      fixture.componentRef.setInput('loading', true);
      fixture.detectChanges();
      expect(textOf(el.querySelector('.cc-tl-status'))).toBe('Loading the timeline…');
      expect(el.querySelector('.cc-tl-empty')).toBeNull();

      fixture.componentRef.setInput('loading', false);
      withModel();
      expect(textOf(el.querySelector('.cc-tl-status'))).toBe('6 runs of GPT-5 high in this range.');
      const heading = el.querySelector<HTMLElement>('h5#cc-tl-runs-title')!;
      expect(textOf(heading)).toBe('Runs of GPT-5 high');
    });

    it('names the runs after the loaded timeline when the model is not in the list', () => {
      const timeline = ccTimeline();
      fixture.componentRef.setInput('selectedKey', 'anthropic/claude-opus');
      fixture.componentRef.setInput('timeline', ccTimeline({ subject: { ...timeline.subject, key: 'anthropic/claude-opus', displayName: 'Claude Opus' } }));
      fixture.detectChanges();
      expect(textOf(el.querySelector('#cc-tl-runs-title'))).toBe('Runs of Claude Opus');

      fixture.componentRef.setInput('timeline', null);
      fixture.detectChanges();
      expect(textOf(el.querySelector('#cc-tl-runs-title'))).toBe('Runs of the model');
    });

    it('says when the model has no run in the range, and shows the errors', () => {
      withModel([]);
      expect(textOf(el.querySelector('.cc-tl-empty'))).toBe('No run of this model in this range.');
      expect(el.querySelector('.cc-run-table')).toBeNull();

      fixture.componentRef.setInput('timelineError', 'The timeline could not be loaded.');
      fixture.componentRef.setInput('runsError', 'The runs could not be loaded.');
      fixture.detectChanges();
      expect(textOf(el.querySelector('.cc-tl-error'))).toBe('The timeline could not be loaded.');
      expect(textOf(el.querySelector('.cc-tl-runs-error'))).toBe('The runs could not be loaded.');
      expect(el.querySelector('.cc-tl-empty')).toBeNull();
    });
  });

  describe('the run table', () => {
    beforeEach(() => withModel());

    it('lists the runs newest first, described by its legend', () => {
      const rows = Array.from(el.querySelectorAll('.cc-run-table tbody tr')).map(tr => tr.getAttribute('data-run-id'));
      expect(rows).toEqual(['106', '105', '104', '103', '102', '101']);
      expect(el.querySelector('.cc-run-table')!.getAttribute('aria-describedby')).toBe('cc-tl-runs-legend');
      expect(textOf(el.querySelector('#cc-tl-runs-legend'))).toContain('a check mark means the run can be used on that axis');
      expect(textOf(row(106))).toContain('#206');
      expect(textOf(row(106).querySelector('time'))).toBe('2026-10-01 08:00 UTC');
    });

    it('renders each run\'s eligibility as badges with text and an icon, and the reason of an exclusion', () => {
      const badges = Array.from(row(103).querySelectorAll<HTMLElement>('.cc-elig'));
      expect(badges.map(badge => badge.getAttribute('data-axis'))).toEqual(['quality', 'speedTelemetry', 'speedLegacy', 'work', 'cost']);
      const excluded = row(103).querySelector<HTMLElement>('.cc-elig[data-axis="speedTelemetry"]')!;
      expect(excluded.classList).toContain('is-excluded');
      expect(textOf(excluded)).toBe('Speed (telemetry): not eligible');
      expect(excluded.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
      expect(textOf(row(103).querySelector('.cc-elig[data-axis="quality"]'))).toBe('Quality: eligible');
      expect(row(103).querySelector('.cc-elig[data-axis="quality"]')!.classList).toContain('is-eligible');
      expect(textOf(row(103).querySelector('.cc-elig-reason'))).toBe('Speed (telemetry): No call telemetry');
      expect(textOf(row(103).querySelector('.cc-legacy-tag'))).toBe('Legacy');
      expect(row(104).querySelector('.cc-legacy-tag')).toBeNull();
      expect(row(104).querySelector('.cc-elig-reason')).toBeNull();
    });

    it('asks to repeat a run\'s setup and to open its run report', () => {
      const repeated: number[] = [];
      const reports: number[] = [];
      component.repeatSetup.subscribe(id => repeated.push(id));
      component.openRunReport.subscribe(id => reports.push(id));

      const repeatButton = row(105).querySelector<HTMLButtonElement>('.cc-repeat-btn')!;
      expect(textOf(repeatButton)).toBe('Repeat this run\'s setup');
      expect(repeatButton.getAttribute('aria-label')).toBe('Repeat this run\'s setup: run #105');
      repeatButton.click();
      expect(repeated).toEqual([105]);

      const reportButton = row(104).querySelector<HTMLButtonElement>('.cc-open-report-btn')!;
      expect(textOf(reportButton)).toBe('Open run report');
      expect(reportButton.getAttribute('aria-label')).toBe('Open run report: run #104');
      reportButton.click();
      expect(reports).toEqual([104]);
    });

    it('asks to toggle the grader anchor and refuses while the run\'s mark is being saved', () => {
      const toggled: CcRunRow[] = [];
      component.anchorToggle.subscribe(toggledRow => toggled.push(toggledRow));
      const button = () => row(104).querySelector<HTMLButtonElement>('.cc-anchor-btn')!;
      expect(textOf(button())).toBe('Mark as anchor');
      expect(button().getAttribute('aria-label')).toBe('Mark as anchor: run #104');

      button().click();
      expect(toggled.map(entry => entry.runId)).toEqual([104]);

      fixture.componentRef.setInput('anchorBusy', new Set([104]));
      fixture.detectChanges();
      expect(button().getAttribute('aria-disabled')).toBe('true');
      expect(button().getAttribute('aria-busy')).toBe('true');
      expect(row(105).querySelector('.cc-anchor-btn')!.hasAttribute('aria-disabled')).toBe(false);
      button().click();
      expect(toggled.map(entry => entry.runId)).toEqual([104]);
    });

    it('shows the anchor the host saved and its announcement', () => {
      const rows = ccRunRows().map(entry => entry.runId === 104 ? ccRunRow(104, entry.startedAtUtc, { isAnchor: true }) : entry);
      fixture.componentRef.setInput('rows', rows);
      fixture.componentRef.setInput('announcement', 'Run #104 is the grader anchor.');
      fixture.detectChanges();

      expect(textOf(row(104).querySelector('.cc-anchor-tag'))).toBe('Anchor');
      expect(row(105).querySelector('.cc-anchor-tag')).toBeNull();
      const button = row(104).querySelector<HTMLButtonElement>('.cc-anchor-btn')!;
      expect(textOf(button)).toBe('Unmark anchor');
      expect(button.getAttribute('aria-label')).toBe('Unmark anchor: run #104');
      const announcement = el.querySelector('.cc-tl-announcement')!;
      expect(announcement.getAttribute('role')).toBe('status');
      expect(textOf(announcement)).toBe('Run #104 is the grader anchor.');
    });

    it('shows an anchor refusal inline as an alert', () => {
      expect(el.querySelector('.cc-tl-anchor-error')).toBeNull();
      fixture.componentRef.setInput('anchorError', 'The anchor of run #104 could not be saved: It no longer exists.');
      fixture.detectChanges();
      const error = el.querySelector('.cc-tl-anchor-error')!;
      expect(error.getAttribute('role')).toBe('alert');
      expect(textOf(error)).toBe('The anchor of run #104 could not be saved: It no longer exists.');
    });
  });
});
