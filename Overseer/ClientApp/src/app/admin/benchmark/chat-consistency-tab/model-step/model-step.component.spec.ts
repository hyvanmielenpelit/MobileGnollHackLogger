import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CC_ALL_DATES, CcDateRange } from '../chat-consistency-range';
import { CC_EMPTY_SCOPE, CcRunScope } from '../chat-consistency-scope';
import { CcRunRow } from '../chat-consistency.models';
import {
  ccAxis,
  ccManyRunRows,
  ccRunRow,
  ccRunRows,
  ccTimeline,
  chatConsistencyTestProviders,
  textOf
} from '../chat-consistency-tab.testing';
import { CC_RUNS_VIEW_STORAGE_KEY, CcModelStepComponent, axisPickerOptions } from './model-step.component';

describe('CcModelStepComponent', () => {
  let fixture: ComponentFixture<CcModelStepComponent>;
  let component: CcModelStepComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    try { localStorage.removeItem(CC_RUNS_VIEW_STORAGE_KEY); } catch { /* storage unavailable */ }
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

  /** The chosen model with its timeline and runs, as the host passes them. */
  function withModel(rows: CcRunRow[] = ccRunRows()): void {
    fixture.componentRef.setInput('selectedKey', 'openai/gpt-5|high');
    fixture.componentRef.setInput('timeline', ccTimeline());
    fixture.componentRef.setInput('rows', rows);
    fixture.detectChanges();
  }

  /** Feeds the selection back as the host would. */
  function hostScope(): CcRunScope[] {
    const scopes: CcRunScope[] = [];
    component.scopeChange.subscribe(scope => {
      scopes.push(scope);
      fixture.componentRef.setInput('scope', scope);
    });
    return scopes;
  }

  function openPicker(): HTMLElement[] {
    el.querySelector<HTMLButtonElement>('.cc-subject-model-selector .selector-trigger')!.click();
    fixture.detectChanges();
    return Array.from(el.querySelectorAll<HTMLElement>('.cc-subject-model-selector [role="option"]'));
  }

  function choosePreset(value: string): void {
    const select = el.querySelector<HTMLSelectElement>('#cc-tl-range')!;
    select.value = value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function setDay(id: 'cc-tl-from' | 'cc-tl-to', value: string): void {
    const input = el.querySelector<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  const card = (runId: number): HTMLElement => el.querySelector<HTMLElement>(`.cc-run-card[data-run-id="${runId}"]`)!;
  const cardIds = (): string[] => Array.from(el.querySelectorAll('.cc-run-card')).map(item => item.getAttribute('data-run-id')!);
  const checkbox = (runId: number) => el.querySelector<HTMLInputElement>(`#cc-run-${runId}-include`)!;

  describe('the fields', () => {
    it('labels the picker Model and the select Dates, with no custom dates until Custom', () => {
      expect(textOf(el.querySelector('#cc-tl-model-label'))).toBe('Model');
      const trigger = el.querySelector<HTMLButtonElement>('.cc-subject-model-selector .selector-trigger')!;
      expect(trigger.getAttribute('aria-labelledby')!.split(' ')[0]).toBe('cc-tl-model-label');
      expect(trigger.getAttribute('aria-describedby')).toBe('cc-tl-model-hint');
      expect(textOf(el.querySelector('#cc-tl-model-hint'))).toBe('Models with at least one usable benchmark run.');

      expect(textOf(el.querySelector('label[for="cc-tl-range"]'))).toBe('Dates');
      expect(el.querySelector<HTMLSelectElement>('#cc-tl-range')!.value).toBe('all');
      expect(el.querySelector('#cc-tl-from')).toBeNull();
      expect(el.querySelector('#cc-tl-to')).toBeNull();
      expect(el.querySelector('legend')).toBeNull();
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
  });

  describe('the dates', () => {
    let ranges: CcDateRange[];

    beforeEach(() => {
      ranges = [];
      component.rangeChange.subscribe(range => ranges.push(range));
    });

    afterEach(() => vi.useRealTimers());

    it('offers the eleven presets in order', () => {
      const options = Array.from(el.querySelectorAll<HTMLOptionElement>('#cc-tl-range option'));
      expect(options.map(option => option.value)).toEqual(['all', '1d', '3d', '7d', '14d', '28d', '30d', '90d', '180d', '1y', 'custom']);
      expect(options.map(option => textOf(option))).toEqual([
        'All dates', 'Last 1 day', 'Last 3 days', 'Last 7 days', 'Last 14 days', 'Last 28 days', 'Last 30 days',
        'Last 90 days', 'Last 180 days', 'Last year', 'Custom'
      ]);
    });

    it('asks for a rolling preset anchored now, and names the anchor under the select', () => {
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(new Date('2026-10-07T14:05:00Z'));
      choosePreset('7d');
      expect(ranges).toEqual([{ preset: '7d', fromDay: '', toDay: '', anchorUtc: '2026-10-07T14:05:00.000Z' }]);

      fixture.componentRef.setInput('range', ranges[0]);
      fixture.detectChanges();
      expect(textOf(el.querySelector('#cc-tl-range-hint'))).toBe('Since 2026-09-30 14:05 UTC · Reload runs moves it to now');
      expect(el.querySelector('#cc-tl-range')!.getAttribute('aria-describedby')).toBe('cc-tl-range-hint');
    });

    it('asks for every date again with All dates', () => {
      fixture.componentRef.setInput('range', { preset: '30d', fromDay: '', toDay: '', anchorUtc: '2026-10-07T00:00:00.000Z' });
      fixture.detectChanges();
      choosePreset('all');
      expect(ranges).toEqual([CC_ALL_DATES]);
    });

    it('shows the custom dates prefilled from the rolling window with Custom', () => {
      fixture.componentRef.setInput('range', { preset: '7d', fromDay: '', toDay: '', anchorUtc: '2026-10-07T14:05:00.000Z' });
      fixture.detectChanges();
      choosePreset('custom');
      expect(ranges).toEqual([{ preset: 'custom', fromDay: '2026-09-30', toDay: '2026-10-07', anchorUtc: null }]);
      expect(textOf(el.querySelector('label[for="cc-tl-from"]'))).toBe('From (UTC)');
      expect(textOf(el.querySelector('label[for="cc-tl-to"]'))).toBe('To (UTC)');
      expect(el.querySelector<HTMLInputElement>('#cc-tl-from')!.value).toBe('2026-09-30');
      expect(el.querySelector<HTMLInputElement>('#cc-tl-to')!.value).toBe('2026-10-07');
    });

    describe('custom', () => {
      beforeEach(() => {
        fixture.componentRef.setInput('range', { preset: 'custom', fromDay: '', toDay: '', anchorUtc: null });
        fixture.detectChanges();
      });

      it('applies a valid bound at once', () => {
        setDay('cc-tl-from', '2026-09-10');
        expect(ranges).toEqual([{ preset: 'custom', fromDay: '2026-09-10', toDay: '', anchorUtc: null }]);
        setDay('cc-tl-to', '2026-9-30');
        expect(ranges[1]).toEqual({ preset: 'custom', fromDay: '2026-09-10', toDay: '2026-09-30', anchorUtc: null });
      });

      it('explains a reversed range on both fields and does not apply it', () => {
        setDay('cc-tl-from', '2026-09-10');
        setDay('cc-tl-to', '2026-09-05');
        expect(ranges.length).toBe(1);
        expect(textOf(el.querySelector('#cc-tl-range-error'))).toBe('The start date must not be after the end date.');
        for (const id of ['cc-tl-from', 'cc-tl-to']) {
          const input = el.querySelector<HTMLInputElement>(`#${id}`)!;
          expect(input.getAttribute('aria-invalid')).toBe('true');
          expect(input.getAttribute('aria-describedby')).toContain('cc-tl-range-error');
        }

        setDay('cc-tl-to', '2026-09-12');
        expect(el.querySelector('#cc-tl-range-error')).toBeNull();
        expect(ranges[ranges.length - 1]).toEqual({ preset: 'custom', fromDay: '2026-09-10', toDay: '2026-09-12', anchorUtc: null });
      });

      it('explains a date that is not a complete calendar date', () => {
        setDay('cc-tl-from', 'next week');
        expect(textOf(el.querySelector('#cc-tl-range-error'))).toBe('Enter the dates as complete calendar dates, YYYY-MM-DD.');
        expect(ranges).toEqual([]);
      });
    });
  });

  describe('the lock', () => {
    const REASON = 'The model and the dates are locked while the charts export.';
    let ranges: CcDateRange[];

    function lock(reason: string): void {
      fixture.componentRef.setInput('lockedReason', reason);
      fixture.detectChanges();
    }

    beforeEach(() => {
      ranges = [];
      component.rangeChange.subscribe(range => ranges.push(range));
      fixture.componentRef.setInput('range', { preset: 'custom', fromDay: '2026-09-01', toDay: '2026-09-30', anchorUtc: null });
      lock(REASON);
    });

    it('shows the reason and describes the locked picker, select and date fields with it', () => {
      const reason = el.querySelector<HTMLElement>('#cc-tl-lock-reason')!;
      expect(textOf(reason)).toBe(REASON);
      const trigger = el.querySelector<HTMLButtonElement>('.cc-subject-model-selector .selector-trigger')!;
      expect(trigger.getAttribute('aria-disabled')).toBe('true');
      expect(trigger.getAttribute('aria-describedby')).toBe('cc-tl-model-hint cc-tl-lock-reason');

      const select = el.querySelector<HTMLSelectElement>('#cc-tl-range')!;
      expect(select.getAttribute('aria-disabled')).toBe('true');
      expect(select.disabled).toBe(false);
      expect(select.getAttribute('aria-describedby')).toBe('cc-tl-lock-reason');
      for (const id of ['cc-tl-from', 'cc-tl-to']) {
        const input = el.querySelector<HTMLInputElement>(`#${id}`)!;
        expect(input.readOnly).toBe(true);
        expect(input.getAttribute('aria-describedby')).toContain('cc-tl-lock-reason');
      }
    });

    it('refuses a preset change and writes the stored preset back to the select', () => {
      choosePreset('7d');
      expect(ranges).toEqual([]);
      expect(el.querySelector<HTMLSelectElement>('#cc-tl-range')!.value).toBe('custom');
    });

    it('refuses a model choice', () => {
      const chosen: string[] = [];
      component.modelChange.subscribe(key => chosen.push(key));
      component.selectModel('anthropic/claude-opus');
      expect(chosen).toEqual([]);
    });

    it('lifts the lock when the reason clears', () => {
      lock('');
      expect(el.querySelector('#cc-tl-lock-reason')).toBeNull();
      expect(el.querySelector('#cc-tl-range')!.hasAttribute('aria-disabled')).toBe(false);
      expect(el.querySelector<HTMLInputElement>('#cc-tl-from')!.readOnly).toBe(false);
      choosePreset('all');
      expect(ranges).toEqual([CC_ALL_DATES]);
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
      expect(textOf(el.querySelector('.cc-tl-status'))).toBe('6 runs of GPT-5 high in these dates.');
      expect(textOf(el.querySelector('h5#cc-tl-runs-title'))).toBe('Runs of GPT-5 high');
      expect(textOf(el.querySelector('#cc-runs-status'))).toBe('Showing 6 of 6 runs');
    });

    it('says when the model has no run in the dates, and shows the errors', () => {
      withModel([]);
      expect(textOf(el.querySelector('.cc-tl-empty'))).toBe('No run of this model in these dates.');
      expect(el.querySelector('.cc-run-cards')).toBeNull();
      expect(el.querySelector('.cc-scope-band')).toBeNull();

      fixture.componentRef.setInput('timelineError', 'The timeline could not be loaded.');
      fixture.componentRef.setInput('runsError', 'The runs could not be loaded.');
      fixture.detectChanges();
      expect(textOf(el.querySelector('.cc-tl-error'))).toBe('The timeline could not be loaded.');
      expect(textOf(el.querySelector('.cc-tl-runs-error'))).toBe('The runs could not be loaded.');
    });
  });

  describe('the run cards', () => {
    beforeEach(() => withModel());

    it('lists the runs newest first in a labelled list of articles', () => {
      const list = el.querySelector<HTMLElement>('ul.cc-run-cards')!;
      expect(list.getAttribute('role')).toBe('list');
      expect(list.getAttribute('aria-labelledby')).toBe('cc-tl-runs-title');
      expect(cardIds()).toEqual(['106', '105', '104', '103', '102', '101']);
      expect(card(106).getAttribute('aria-labelledby')).toBe('cc-run-106-title');
      expect(el.querySelector('table')).toBeNull();
    });

    it('carries a kicker, a label title, a meta line, eligibility and facts', () => {
      const kicker = textOf(card(103).querySelector('.cc-run-kicker'));
      expect(kicker).toContain('#103');
      expect(kicker).toContain('Harness 30');
      expect(textOf(card(103).querySelector('.cc-legacy-tag'))).toBe('Legacy');
      expect(textOf(card(104).querySelector('.cc-recorded-tag'))).toBe('Recorded');

      const title = card(104).querySelector<HTMLElement>('h6#cc-run-104-title')!;
      expect(title.getAttribute('tabindex')).toBe('-1');
      expect(title.querySelector('label')!.getAttribute('for')).toBe('cc-run-104-include');
      expect(textOf(title)).toBe('Board Suite');
      expect(textOf(card(106).querySelector('time'))).toBe('2026-10-01 08:00 UTC');

      const excluded = card(103).querySelector<HTMLElement>('.cc-elig[data-axis="speedTelemetry"]')!;
      expect(excluded.classList).toContain('is-excluded');
      expect(textOf(excluded)).toBe('Speed (telemetry): not eligible');
      expect(textOf(card(103).querySelector('.cc-elig-reason'))).toBe('Speed (telemetry): No call telemetry');

      const facts = Array.from(card(106).querySelectorAll('.cc-run-facts dt')).map(dt => textOf(dt));
      expect(facts).toEqual(['Segment', 'Telemetry', 'Re-grade', 'Matched controls']);
      expect(textOf(card(106).querySelector('[data-fact="controls"] dd'))).toContain('#206');
    });

    it('includes every run by default, with a named checkbox the title labels', () => {
      for (const id of [101, 106]) {
        expect(checkbox(id).checked).toBe(true);
        expect(checkbox(id).getAttribute('aria-label')).toBe(`Include run #${id} in the analysis`);
      }
      expect(textOf(el.querySelector('#cc-scope-label'))).toBe('Runs in the analysis — all 6 runs in these dates');
      expect(el.querySelector('.cc-scope-clear')).toBeNull();
      expect(textOf(el.querySelector('.cc-scope-hint')))
        .toBe('The filters below change what is shown, not what is analyzed. The saved analysis records this selection.');
    });

    it('leaves a run out when its checkbox is cleared, and shows it on the card and in the band', () => {
      const scopes = hostScope();
      checkbox(104).click();
      fixture.detectChanges();

      expect([...scopes[0].leftOut]).toEqual([104]);
      expect(checkbox(104).checked).toBe(false);
      expect(textOf(card(104).querySelector('.cc-left-out-tag'))).toBe('Left out');
      expect(textOf(el.querySelector('#cc-scope-label')))
        .toBe('Runs in the analysis — 5 of 6 runs in these dates · from #101 (2026-09-01) to #106 (2026-10-01) · 1 left out');
      expect(textOf(el.querySelector('.cc-scope-clear'))).toBe('Clear selection (1)');
      expect(textOf(el.querySelector('.cc-scope-chip[data-kind="leftOut"] .cc-scope-chip-label'))).toBe('Left out: #104');

      checkbox(104).click();
      fixture.detectChanges();
      expect(scopes[1].leftOut.size).toBe(0);
    });

    it('marks the first and last runs with pressed toggles, and refuses the runs outside them', () => {
      const scopes = hostScope();
      const first = card(102).querySelector<HTMLButtonElement>('.cc-first-btn')!;
      expect(first.getAttribute('aria-label')).toBe('Use run #102 as the first run of the analysis');
      expect(first.getAttribute('aria-pressed')).toBe('false');
      first.click();
      fixture.detectChanges();
      card(105).querySelector<HTMLButtonElement>('.cc-last-btn')!.click();
      fixture.detectChanges();

      expect(scopes[scopes.length - 1].firstRunId).toBe(102);
      expect(scopes[scopes.length - 1].lastRunId).toBe(105);
      expect(card(102).querySelector('.cc-first-btn')!.getAttribute('aria-pressed')).toBe('true');
      expect(textOf(card(102).querySelector('.cc-first-tag'))).toBe('First run');
      expect(textOf(card(105).querySelector('.cc-last-tag'))).toBe('Last run');

      const outside = checkbox(101);
      expect(outside.checked).toBe(false);
      expect(outside.getAttribute('aria-disabled')).toBe('true');
      expect(outside.disabled).toBe(false);
      expect(outside.getAttribute('aria-describedby')).toBe('cc-run-101-reason');
      expect(textOf(el.querySelector('#cc-run-101-reason'))).toBe('Before the first run (#102)');
      expect(textOf(el.querySelector('#cc-run-106-reason'))).toBe('After the last run (#105)');

      const count = scopes.length;
      outside.click();
      fixture.detectChanges();
      expect(scopes.length).toBe(count);
      expect(outside.checked).toBe(false);

      expect(textOf(el.querySelector('#cc-scope-label')))
        .toBe('Runs in the analysis — 4 of 6 runs in these dates · from #102 (2026-09-05) to #105 (2026-09-26)');
    });

    it('clears a later last run when the first run moves past it, and says so', () => {
      hostScope();
      card(103).querySelector<HTMLButtonElement>('.cc-last-btn')!.click();
      fixture.detectChanges();
      card(105).querySelector<HTMLButtonElement>('.cc-first-btn')!.click();
      fixture.detectChanges();
      expect(component.scope.firstRunId).toBe(105);
      expect(component.scope.lastRunId).toBeNull();
      expect(textOf(el.querySelector('.cc-scope-note'))).toBe('Run #105 is after the last run, so the last run was cleared.');
    });

    it('clears the selection and focuses the band label', async () => {
      hostScope();
      checkbox(104).click();
      fixture.detectChanges();
      el.querySelector<HTMLButtonElement>('.cc-scope-clear')!.click();
      fixture.detectChanges();
      await fixture.whenStable();
      expect(component.scope).toBe(CC_EMPTY_SCOPE);
      expect(el.querySelector('.cc-scope-clear')).toBeNull();
      expect(document.activeElement).toBe(el.querySelector('#cc-scope-label'));
    });

    it('removes a chip and focuses the next chip, else Clear selection', async () => {
      hostScope();
      checkbox(103).click();
      fixture.detectChanges();
      checkbox(104).click();
      fixture.detectChanges();
      const removes = () => Array.from(el.querySelectorAll<HTMLButtonElement>('.cc-scope-chip-remove'));
      expect(removes().map(button => button.getAttribute('aria-label'))).toEqual(['Include run #103 again', 'Include run #104 again']);

      removes()[0].click();
      fixture.detectChanges();
      await fixture.whenStable();
      expect([...component.scope.leftOut]).toEqual([104]);
      expect(document.activeElement).toBe(removes()[0]);

      removes()[0].click();
      fixture.detectChanges();
      await fixture.whenStable();
      expect(document.activeElement).toBe(el.querySelector('#cc-scope-label'));
    });

    it('warns when every run is left out', () => {
      hostScope();
      for (const id of [101, 102, 103, 104, 105, 106]) {
        checkbox(id).click();
        fixture.detectChanges();
      }
      expect(textOf(el.querySelector('.cc-scope-empty'))).toBe('No run is left in the analysis. Check at least one run.');
    });

    it('asks to open a run report and, from More actions, to repeat the setup', () => {
      const reports: number[] = [];
      const repeated: number[] = [];
      component.openRunReport.subscribe(id => reports.push(id));
      component.repeatSetup.subscribe(id => repeated.push(id));

      const report = card(104).querySelector<HTMLButtonElement>('.cc-open-report-btn')!;
      expect(report.getAttribute('aria-label')).toBe('Open the run report of run #104');
      report.click();
      expect(reports).toEqual([104]);

      const more = card(105).querySelector<HTMLButtonElement>('.cc-more-btn')!;
      expect(more.getAttribute('aria-label')).toBe('More actions for run #105');
      expect(more.getAttribute('popovertarget')).toBe('cc-run-105-more');
      const popover = el.querySelector<HTMLElement>('#cc-run-105-more')!;
      expect(popover.getAttribute('aria-label')).toBe('Run actions for run #105');
      popover.querySelector<HTMLButtonElement>('.cc-repeat-btn')!.click();
      expect(repeated).toEqual([105]);
    });

    it('asks to toggle the grader anchor and refuses while its mark is being saved', () => {
      const toggled: number[] = [];
      component.anchorToggle.subscribe(row => toggled.push(row.runId));
      const button = () => card(104).querySelector<HTMLButtonElement>('.cc-anchor-btn')!;
      expect(textOf(button())).toBe('Mark as anchor');
      button().click();
      expect(toggled).toEqual([104]);

      fixture.componentRef.setInput('anchorBusy', new Set([104]));
      fixture.detectChanges();
      expect(button().getAttribute('aria-disabled')).toBe('true');
      expect(textOf(button().querySelector('.gh-action-popover-item-reason'))).toBe('Saving the anchor…');
      button().click();
      expect(toggled).toEqual([104]);
    });

    it('shows the anchor the host saved and its announcement', () => {
      const rows = ccRunRows().map(entry => entry.runId === 104 ? ccRunRow(104, entry.startedAtUtc, { isAnchor: true }) : entry);
      fixture.componentRef.setInput('rows', rows);
      fixture.componentRef.setInput('announcement', 'Run #104 is the grader anchor.');
      fixture.detectChanges();
      expect(textOf(card(104).querySelector('.cc-anchor-tag'))).toBe('Anchor');
      expect(textOf(card(104).querySelector('.cc-anchor-btn'))).toBe('Unmark anchor');
      expect(textOf(el.querySelector('.cc-tl-announcement'))).toBe('Run #104 is the grader anchor.');
    });

    it('shows an anchor refusal inline as an alert', () => {
      fixture.componentRef.setInput('anchorError', 'The anchor of run #104 could not be saved: It no longer exists.');
      fixture.detectChanges();
      const error = el.querySelector('.cc-tl-anchor-error')!;
      expect(error.getAttribute('role')).toBe('alert');
    });
  });

  describe('the filter bar', () => {
    it('filters what is shown but never the selection', () => {
      withModel();
      const scopes = hostScope();
      checkbox(104).click();
      fixture.detectChanges();
      const before = component.scope;

      component.onFacetChange('inclusion', ['leftOut']);
      expect(cardIds()).toEqual(['104']);
      expect(textOf(el.querySelector('#cc-runs-status'))).toBe('One run · filtered from 6');
      expect(component.scope).toBe(before);
      expect(scopes.length).toBe(1);
      expect(textOf(el.querySelector('#cc-scope-label'))).toContain('5 of 6 runs');

      el.querySelector<HTMLButtonElement>('.cc-runs-clear-filters')!.click();
      fixture.detectChanges();
      expect(cardIds().length).toBe(6);
    });

    it('lists the In the analysis facet with its counts', () => {
      withModel();
      hostScope();
      checkbox(104).click();
      fixture.detectChanges();
      const facet = component.facets.find(f => f.column === 'inclusion')!;
      expect(facet.label).toBe('In the analysis');
      expect(facet.options).toEqual([
        { value: 'included', label: 'Included', count: 5 },
        { value: 'leftOut', label: 'Left out', count: 1 }
      ]);
    });

    it('sorts by Sort by and shows ten cards, then the next batch, focusing the first new title', () => {
      withModel(ccManyRunRows(23));
      expect(el.querySelectorAll('.cc-run-card').length).toBe(10);
      expect(textOf(el.querySelector('.cc-runs-show-more'))).toBe('Show 10 more');
      expect(textOf(el.querySelector('.cc-runs-show-all'))).toBe('Show all 23');

      el.querySelector<HTMLButtonElement>('.cc-runs-show-more')!.click();
      expect(el.querySelectorAll('.cc-run-card').length).toBe(20);
      expect(document.activeElement).toBe(el.querySelector(`#cc-run-${cardIds()[10]}-title`));

      const sort = el.querySelector<HTMLSelectElement>('#cc-runs-sort')!;
      sort.value = 'oldest';
      sort.dispatchEvent(new Event('change'));
      expect(cardIds()[0]).toBe('1001');
    });

    it('says when no run matches, with Clear all filters', () => {
      withModel();
      const search = el.querySelector<HTMLInputElement>('#cc-runs-search')!;
      search.value = 'nothing like this';
      search.dispatchEvent(new Event('input'));
      component.list.table.setFilter('search', 'nothing like this');
      component.list.invalidate();
      fixture.componentRef.changeDetectorRef.markForCheck();
      fixture.detectChanges();
      expect(textOf(el.querySelector('.cc-runs-no-matches'))).toContain('No runs match these filters.');
    });
  });
});
