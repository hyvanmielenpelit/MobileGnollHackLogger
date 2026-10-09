import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';

import { CcPeriodBound } from '../chat-consistency-periods';
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
import { DownloadCenterPanelComponent } from '../../download-center/download-center-panel.component';
import {
  CC_ANALYZE_STORAGE_KEY,
  CC_DEFAULT_ANALYZE_LAYOUT,
  CcAnalysisStep,
  CcAnalysisWizardComponent,
  readStoredAnalyzeLayout
} from './analysis-wizard.component';

const DOCUMENTS_URL = '/api/admin/benchmark/report-documents';

/** The four bounds in request order. */
const BOUNDS: readonly CcPeriodBound[] = ['baselineFirstId', 'baselineLastId', 'comparisonFirstId', 'comparisonLastId'];

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
    localStorage.removeItem(CC_ANALYZE_STORAGE_KEY);
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
    localStorage.removeItem(CC_ANALYZE_STORAGE_KEY);
  });

  function setStep(step: CcAnalysisStep): void {
    fixture.componentRef.setInput('step', step);
    fixture.detectChanges();
  }

  /** Shows Results with a result; it asks the server for nothing. */
  function goToResults(): void {
    setStep('results');
  }

  /** Shows Reports with a saved result: on its first visit it asks for the report job and the documents. */
  function goToReports(): void {
    setStep('reports');
    http.expectOne(`${CC_API}/analyses/7/report-documents/job`).flush(null, { status: 204, statusText: 'No Content' });
    const documents = http.expectOne(r => r.url === DOCUMENTS_URL);
    expect(documents.request.params.has('origin')).toBe(false);
    documents.flush([]);
    fixture.detectChanges();
  }

  /** The Download Center panel's list request: the analysis's chat consistency documents. */
  function expectPanelList(): TestRequest {
    return http.expectOne(r => r.url === DOCUMENTS_URL && r.params.get('origin') === 'chatConsistencyReport');
  }

  /** Shows Documents with a saved result: on its first visit the Download Center panel lists the documents. */
  function goToDocuments(): void {
    setStep('documents');
    expectPanelList().flush([]);
    fixture.detectChanges();
  }

  /** A unit's toggle for one bound, on its period card. */
  const toggle = (unitId: number, bound: CcPeriodBound): HTMLButtonElement =>
    el.querySelector<HTMLButtonElement>(`article.cc-pu-card[data-unit-id="${unitId}"] .cc-pu-bound[data-bound="${bound}"]`)!;

  /** Presses a bound's toggle on a card, as the user does. */
  function press(unitId: number, bound: CcPeriodBound): void {
    toggle(unitId, bound).click();
    fixture.detectChanges();
  }

  /** Puts a bound on a unit, pressing only when it is not there yet. */
  function setBound(unitId: number, bound: CcPeriodBound): void {
    if (toggle(unitId, bound).getAttribute('aria-pressed') !== 'true') press(unitId, bound);
  }

  /** Chooses a Split rule in the sidebar's select. */
  function chooseRule(value: string): void {
    const select = el.querySelector<HTMLSelectElement>('#cc-an-rule')!;
    select.value = value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  const ruleValue = (): string => el.querySelector<HTMLSelectElement>('#cc-an-rule')!.value;
  const body = (step: CcAnalysisStep): HTMLElement | null => el.querySelector<HTMLElement>(`.cc-wiz-step[data-step="${step}"]`);
  const shownSteps = (): (string | null)[] =>
    Array.from(el.querySelectorAll<HTMLElement>('.cc-wiz-step')).filter(step => !step.hidden).map(step => step.getAttribute('data-step'));
  /** The four bounds as the cards show them: the unit whose toggle is pressed, or '' for none. */
  const bounds = (): string[] => BOUNDS.map(bound =>
    el.querySelector(`.cc-pu-bound[data-bound="${bound}"][aria-pressed="true"]`)?.closest('article')?.getAttribute('data-unit-id') ?? '');
  /** Each card as `[id, period word]`. */
  const unitPeriods = (): [string | null, string][] =>
    Array.from(el.querySelectorAll<HTMLElement>('article.cc-pu-card'))
      .map(card => [card.getAttribute('data-unit-id'), textOf(card.querySelector('.cc-pu-period-tag'))]);
  const controlLabels = (): string[] =>
    Array.from(el.querySelectorAll<HTMLInputElement>('.cc-wiz-control-check')).map(box => textOf(box.closest('label')));
  const checkedControls = (): string[] =>
    Array.from(el.querySelectorAll<HTMLInputElement>('.cc-wiz-control-check')).filter(box => box.checked).map(box => textOf(box.closest('label')));
  const note = (): string => textOf(el.querySelector('#cc-an-note'));
  const strip = (period: string, part: string): string => textOf(el.querySelector(`.cc-ps-period[data-period="${period}"] ${part}`));
  /** The preview's Input value of a fact, by key. */
  const fact = (key: string): string | null => {
    const row = el.querySelector(`.cc-ap-facts [data-fact="${key}"] dd`);
    return row ? textOf(row) : null;
  };

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
      expect(el.classList).toContain('is-workspace');
      setStep('results');
      expect(shownSteps()).toEqual(['results']);
      expect(body('analyze')!.hidden).toBe(true);
      expect(el.classList).not.toContain('is-workspace');

      setStep('analyze');
      expect(shownSteps()).toEqual(['analyze']);
      expect(body('results')!.hidden).toBe(true);
      // The same panel is kept, so it does not ask for the re-grade job again.
      expect(el.querySelector('app-cc-regrade-panel')).toBe(regradePanel);
    });

    it('reaches Analyze with a model and Results, Reports and Documents only with a result', () => {
      expect(component.reachable('analyze')).toBe(true);
      expect(component.reachable('results')).toBe(false);
      expect(component.reachable('reports')).toBe(false);
      expect(component.reachable('documents')).toBe(false);
      setStep('results');
      expect(shownSteps()).toEqual(['results']);
      expect(el.querySelector('app-cc-results-view')).toBeNull();
      expect(el.querySelector('app-cc-reports-step')).toBeNull();
      setStep('reports');
      expect(shownSteps()).toEqual(['reports']);
      expect(el.querySelector('app-cc-reports-step')).toBeNull();
      setStep('documents');
      expect(shownSteps()).toEqual(['documents']);
      expect(el.querySelector('app-download-center-panel')).toBeNull();
    });

    it('reaches Reports and Documents with a saved result, and not with one that has no id', () => {
      component.showResult(ccAnalysisResult({ analysisId: null }));
      expect(component.reachable('results')).toBe(true);
      expect(component.reachable('reports')).toBe(false);
      expect(component.reachable('documents')).toBe(false);

      component.showResult(ccAnalysisResult());
      expect(component.reachable('reports')).toBe(true);
      expect(component.reachable('documents')).toBe(true);
    });
  });

  describe('workspace', () => {
    const sideTab = (id: string): HTMLButtonElement => el.querySelector<HTMLButtonElement>(`#cc-an-side-tab-${id}`)!;
    const sidePanel = (id: string): HTMLElement => el.querySelector<HTMLElement>(`#cc-an-side-panel-${id}`)!;
    const viewTab = (id: string): HTMLButtonElement => el.querySelector<HTMLButtonElement>(`#cc-an-view-tab-${id}`)!;
    const viewPanel = (id: string): HTMLElement => el.querySelector<HTMLElement>(`#cc-an-view-panel-${id}`)!;
    const stored = (): Record<string, unknown> => JSON.parse(localStorage.getItem(CC_ANALYZE_STORAGE_KEY) ?? 'null');

    it('is a settings sidebar beside the Periods and Preview views', () => {
      const sidebar = el.querySelector<HTMLElement>('aside#cc-an-sidebar.gh-fig-sidebar')!;
      expect(sidebar.getAttribute('aria-label')).toBe('Analysis settings');
      const tabs = sidebar.querySelector('[role="tablist"]')!;
      expect(tabs.getAttribute('aria-label')).toBe('Settings sections');
      expect(tabs.classList).toContain('gh-tabs-wrap');
      expect(Array.from(tabs.querySelectorAll('[role="tab"]')).map(tab => textOf(tab))).toEqual(['Setup', 'Controls', 'Protocol']);
      expect(sideTab('setup').getAttribute('aria-selected')).toBe('true');
      expect(sideTab('setup').getAttribute('tabindex')).toBe('0');
      expect(sideTab('controls').getAttribute('tabindex')).toBe('-1');
      expect(sidePanel('setup').hidden).toBe(false);
      expect(sidePanel('controls').hidden).toBe(true);
      expect(sidePanel('protocol').hidden).toBe(true);

      const views = el.querySelector('.gh-fig-bar [role="tablist"]')!;
      expect(views.getAttribute('aria-label')).toBe('Analysis views');
      expect(textOf(viewTab('periods'))).toBe('Periods');
      expect(viewTab('periods').getAttribute('aria-selected')).toBe('true');
      expect(viewPanel('periods').hidden).toBe(false);
      expect(viewPanel('preview').hidden).toBe(true);
      expect(el.querySelector('app-pane-resizer.gh-fig-resizer')!.getAttribute('aria-controls')).toBe('cc-an-sidebar');
    });

    it('moves between the sidebar tabs with the arrow keys, focus following, and remembers the tab', () => {
      sideTab('setup').focus();
      sideTab('setup').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true }));
      fixture.detectChanges();
      expect(sideTab('controls').getAttribute('aria-selected')).toBe('true');
      expect(document.activeElement).toBe(sideTab('controls'));
      expect(sidePanel('controls').hidden).toBe(false);
      expect(stored()).toEqual({ version: 1, ...CC_DEFAULT_ANALYZE_LAYOUT, sidebarTab: 'controls' });

      sideTab('controls').dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true, cancelable: true }));
      fixture.detectChanges();
      expect(document.activeElement).toBe(sideTab('protocol'));
      sideTab('protocol').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true }));
      fixture.detectChanges();
      expect(document.activeElement).toBe(sideTab('setup'));
    });

    it('collapses the sidebar from its toggle in the view bar, and remembers it', () => {
      const button = el.querySelector<HTMLButtonElement>('.cc-an-sidebar-toggle')!;
      expect(button.getAttribute('aria-label')).toBe('Analysis settings');
      expect(button.getAttribute('aria-expanded')).toBe('true');
      expect(textOf(el.querySelector('#cc-an-tip-sidebar'))).toBe('Hide settings');
      button.click();
      fixture.detectChanges();
      expect(el.querySelector<HTMLElement>('#cc-an-sidebar')!.hidden).toBe(true);
      expect(button.getAttribute('aria-expanded')).toBe('false');
      expect(el.querySelector('app-pane-resizer')).toBeNull();
      expect(textOf(el.querySelector('#cc-an-tip-sidebar'))).toBe('Show settings');
      expect(stored()['sidebarCollapsed']).toBe(true);
    });

    it('reads a damaged layout field by field, every unknown value taking its default', () => {
      localStorage.setItem(CC_ANALYZE_STORAGE_KEY, JSON.stringify({
        version: 1, sidebarTab: 'nowhere', view: 'preview', sidebarCollapsed: 'yes', sidebarWidth: 9999, details: false
      }));
      expect(readStoredAnalyzeLayout()).toEqual({
        sidebarTab: 'setup', view: 'preview', sidebarCollapsed: false, sidebarWidth: 640, details: false
      });
      localStorage.setItem(CC_ANALYZE_STORAGE_KEY, '{');
      expect(readStoredAnalyzeLayout()).toEqual(CC_DEFAULT_ANALYZE_LAYOUT);
      localStorage.setItem(CC_ANALYZE_STORAGE_KEY, '[1]');
      expect(readStoredAnalyzeLayout()).toEqual(CC_DEFAULT_ANALYZE_LAYOUT);
    });

    it('counts the preview\'s notes on the Preview tab, and opens it from the strip\'s readiness link', () => {
      // The Overseer change E1 in the span and the runs without a matched control.
      expect(component.noteCount).toBe(2);
      expect(textOf(viewTab('preview').querySelector('.cc-an-tab-count'))).toBe('2 notes');
      const link = el.querySelector<HTMLButtonElement>('.cc-ps-preview-link')!;
      expect(textOf(link)).toBe('Preview: 2 notes');

      link.click();
      fixture.detectChanges();
      expect(viewTab('preview').getAttribute('aria-selected')).toBe('true');
      expect(viewPanel('preview').hidden).toBe(false);
      expect(viewPanel('periods').hidden).toBe(true);
      expect(document.activeElement).toBe(viewPanel('preview'));
      expect(stored()['view']).toBe('preview');
    });

    it('remembers Show run details', () => {
      const details = el.querySelector<HTMLButtonElement>('.cc-pu-details-toggle')!;
      expect(details.getAttribute('aria-pressed')).toBe('true');
      details.click();
      fixture.detectChanges();
      expect(details.getAttribute('aria-pressed')).toBe('false');
      expect(component.details).toBe(false);
      expect(stored()['details']).toBe(false);
    });
  });

  describe('subject', () => {
    const subject = (): HTMLElement => el.querySelector<HTMLElement>('section.cc-an-subject')!;

    it('names the model with its badges and id, and what is compared, without a Runs line', () => {
      expect(textOf(subject().querySelector('#cc-an-subject-title'))).toBe('Subject');
      expect(textOf(subject().querySelector('.cc-an-model-name'))).toBe('GPT-5 high');
      expect(textOf(subject().querySelector('.thinking-badge'))).toBe('thinking level High');
      expect(textOf(subject().querySelector('.provider-badge'))).toBe('OpenAI');
      expect(textOf(subject().querySelector('.cc-an-model-id'))).toBe('gpt-5');
      expect(Array.from(subject().querySelectorAll('dt')).map(dt => textOf(dt))).toEqual(['Compared']);
      const tag = subject().querySelector('.cc-kind-tag')!;
      expect(tag.getAttribute('data-kind')).toBe('all');
      expect(textOf(tag)).toBe('All suites');
      expect(textOf(subject().querySelector('.cc-an-compared-label'))).toBe('Runs analyzed one by one');
      expect(textOf(subject().querySelector('.cc-an-compared-units'))).toBe('6 runs from step 1');
      expect(textOf(subject())).not.toContain('Runs 6');
    });

    it('counts a suite set\'s single-suite runs', () => {
      fixture.componentRef.setInput('compareSet', ccComparisonSets().sets[1]);
      fixture.detectChanges();
      expect(textOf(subject().querySelector('.cc-kind-tag'))).toBe('Suite');
      expect(textOf(subject().querySelector('.cc-an-compared-label'))).toBe('Board Suite');
      expect(textOf(subject().querySelector('.cc-an-compared-units'))).toBe('6 single-suite runs from step 1');
    });

    it('asks for a model in step 1 when there is none', () => {
      fixture.componentRef.setInput('axis', null);
      fixture.detectChanges();
      expect(component.analyzeBlocked).toBe('Choose a model in step 1 first.');
      expect(component.reachable('analyze')).toBe(false);
      expect(textOf(subject())).toContain('None chosen');
      expect(el.querySelector<HTMLSelectElement>('#cc-an-rule')!.disabled).toBe(true);
      expect(ruleValue()).toBe('custom');
      expect(el.querySelector('app-cc-period-units')).toBeNull();
    });
  });

  describe('periods', () => {
    it('starts from Earliest vs latest and shows the bounds on the cards, the note and the summary strip', () => {
      expect(ruleValue()).toBe('earliest');
      expect(bounds()).toEqual(['101', '103', '104', '106']);
      expect(textOf(el.querySelector('.cc-wiz-preset-note')))
        .toBe('The runs span 31 days: the first 14 days against the last 14 days, 3 runs each.');
      expect(el.querySelector('.cc-wiz-periods-error')).toBeNull();
      expect(component.periodsError).toBe('');
      expect(strip('baseline', '.cc-ps-count')).toBe('3 runs on 3 days · 2026-09-01 to 2026-09-12');
      expect(strip('comparison', '.cc-ps-window')).toBe('Window 2026-09-20 00:00 to 2026-10-01 23:59 UTC');
      expect(strip('baseline', '.cc-ps-sample')).toContain('Meets the minimum sample');
      expect(component.sampleLine('baseline'))
        .toBe('Baseline: 3 runs on 3 days (2026-09-01 to 2026-09-12), which meets the minimum sample for P1, P4 and P5.');
      expect(component.windows).toEqual({
        baselineStartUtc: '2026-09-01T00:00:00.000Z',
        baselineEndUtc: '2026-09-12T23:59:59.999Z',
        comparisonStartUtc: '2026-09-20T00:00:00.000Z',
        comparisonEndUtc: '2026-10-01T23:59:59.999Z'
      });
      expect(unitPeriods()).toEqual([
        ['101', 'Baseline'], ['102', 'Baseline'], ['103', 'Baseline'], ['104', 'Comparison'], ['105', 'Comparison'], ['106', 'Comparison']
      ]);
    });

    it('offers the five Split rules in one select with an info tip', () => {
      const options = Array.from(el.querySelectorAll<HTMLOptionElement>('#cc-an-rule option'));
      expect(options.map(option => [option.value, textOf(option)])).toEqual([
        ['earliest', 'Earliest vs latest'],
        ['annotation', 'Before vs after an annotation'],
        ['event', 'Before vs after an Overseer change'],
        ['later', 'Confirm on later data'],
        ['custom', 'Manual (set on the cards)']
      ]);
      expect(textOf(el.querySelector('label[for="cc-an-rule"]'))).toBe('Split rule');
      expect(el.querySelector('#cc-an-rule')!.getAttribute('aria-describedby')).toBe('cc-an-rule-tip');
      expect(el.querySelector('app-info-tip[tipId="cc-an-rule-tip"], app-info-tip[tipid="cc-an-rule-tip"]')).not.toBeNull();
    });

    it('switches to Manual when a bound is pressed on a card, says so, keeps focus and refuses an overlap', () => {
      const button = toggle(103, 'comparisonFirstId');
      button.focus();
      press(103, 'comparisonFirstId');
      expect(ruleValue()).toBe('custom');
      expect(component.preset).toBe('custom');
      expect(note()).toBe('Comparison first run: run #103. Split rule set to Manual.');
      expect(textOf(el.querySelector('.cc-wiz-preset-note'))).toBe('');
      // The card was not re-created: the same button holds focus.
      expect(toggle(103, 'comparisonFirstId')).toBe(button);
      expect(document.activeElement).toBe(button);
      expect(button.getAttribute('aria-pressed')).toBe('true');
      expect(toggle(104, 'comparisonFirstId').getAttribute('aria-pressed')).toBe('false');
      expect(textOf(el.querySelector('.cc-wiz-periods-error'))).toBe('The comparison must start after the baseline\'s last run.');
      expect(component.analyzeBlocked).toBe('The comparison must start after the baseline\'s last run.');
      expect(component.windows).toBeNull();
      expect(el.querySelector('.cc-ps-sample')).toBeNull();
      expect(component.reachable('analyze')).toBe(true);

      // Pressed again, the bound clears; the rule is already Manual.
      press(103, 'comparisonFirstId');
      expect(note()).toBe('Comparison first run cleared.');
      expect(component.periodsError).toBe('Choose the first and last run of both periods.');
    });

    it('applies a rule chosen again after Manual', () => {
      press(102, 'baselineLastId');
      expect(bounds()).toEqual(['101', '102', '104', '106']);
      chooseRule('earliest');
      expect(bounds()).toEqual(['101', '103', '104', '106']);
      expect(note()).toBe('');
    });

    it('refuses a period whose last run comes before its first', () => {
      press(101, 'baselineLastId');
      press(102, 'baselineFirstId');
      expect(bounds()).toEqual(['102', '101', '104', '106']);
      expect(component.periodsError).toBe('The baseline\'s last run comes before its first.');
    });

    it('sends the same request for bounds set on the cards as for the bounds a rule sets', () => {
      const byRule = component.buildRequest();
      chooseRule('custom');
      // Cleared, then set again on the same run.
      press(101, 'baselineFirstId');
      expect(component.buildRequest()).toBeNull();
      press(101, 'baselineFirstId');
      expect(component.buildRequest()).toEqual(byRule);

      setBound(102, 'baselineLastId');
      setBound(105, 'comparisonFirstId');
      const request = component.buildRequest()!;
      expect(request.baselineRunIds).toEqual([101, 102]);
      expect(request.comparisonRunIds).toEqual([105, 106]);
      expect(request.baselineEndUtc).toBe('2026-09-05T23:59:59.999Z');
      expect(request.comparisonStartUtc).toBe('2026-09-26T00:00:00.000Z');
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

    it('shows Protocol V1 open in its tab, its endpoints as a list, and the overrides in a disclosure', () => {
      const panel = el.querySelector<HTMLElement>('#cc-an-side-panel-protocol')!;
      expect(panel.querySelector('table')).toBeNull();
      const rows = Array.from(panel.querySelectorAll('dl.cc-an-endpoints > div'))
        .map(row => [textOf(row.querySelector('dt')), textOf(row.querySelector('dd'))]);
      expect(rows).toEqual([
        ['P1 Quality', '±3 index points'], ['P2 Time to first answer text', '±15 %'], ['P3 Answer streaming rate', '±10 %'],
        ['P4 Work per turn', '±15 %'], ['P5 Cost per question', '±10 %']
      ]);
      expect(textOf(panel.querySelector('.cc-wiz-protocol-facts'))).toContain('0.05, Holm-adjusted across P1–P5');
      const overrides = panel.querySelector<HTMLDetailsElement>('details.gh-disclosure.cc-wiz-overrides')!;
      expect(overrides.open).toBe(false);
      expect(overrides.querySelector('#cc-wiz-alpha')).not.toBeNull();
    });

    it('splits before and after a composite Overseer event, with every run on each side', () => {
      fixture.componentRef.setInput('timeline', ccEventTimeline());
      fixture.componentRef.setInput('rows', eventRows());
      fixture.componentRef.setInput('axis', ccAxis({ firstRunAtUtc: '2026-09-01T08:00:00Z', lastRunAtUtc: '2026-09-10T08:00:00Z' }));
      fixture.detectChanges();
      chooseRule('event');

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
      expect(bounds()).toEqual(['201', '201', '202', '206']);
      expect(textOf(el.querySelector('.cc-wiz-preset-note')))
        .toBe('The runs before the Overseer change E1 (2026-09-03 08:00 UTC) against those from it: 1 run against 5.');

      const select = el.querySelector<HTMLSelectElement>('#cc-wiz-event')!;
      select.value = '2026-09-05|28';
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      expect(component.presetEventGroupKey).toBe('2026-09-05|28');
      expect(el.querySelector<HTMLSelectElement>('#cc-wiz-event')!.value).toBe('2026-09-05|28');
      expect(bounds()).toEqual(['201', '203', '204', '206']);
      expect(ruleValue()).toBe('event');
      expect(unitPeriods()).toEqual([
        ['201', 'Baseline'], ['202', 'Baseline'], ['203', 'Baseline'], ['204', 'Comparison'], ['205', 'Comparison'], ['206', 'Comparison']
      ]);
    });

    it('splits at an Overseer change from Split here on the cards\' marker', () => {
      fixture.componentRef.setInput('timeline', ccEventTimeline());
      fixture.componentRef.setInput('rows', eventRows());
      fixture.detectChanges();
      press(203, 'baselineLastId');
      expect(ruleValue()).toBe('custom');

      const split = el.querySelector<HTMLButtonElement>('.cc-pu-split[aria-label="Split the periods at Overseer change E2"]')!;
      split.focus();
      split.click();
      fixture.detectChanges();
      expect(ruleValue()).toBe('event');
      expect(component.presetEventGroupKey).toBe('2026-09-05|28');
      expect(el.querySelector<HTMLSelectElement>('#cc-wiz-event')!.value).toBe('2026-09-05|28');
      expect(bounds()).toEqual(['201', '203', '204', '206']);
      expect(note()).toBe('Split at Overseer change E2. '
        + 'The runs before the Overseer change E2 (2026-09-05 08:00 UTC) against those from it: 3 runs each.');
      // The marker was not re-created.
      expect(document.activeElement).toBe(split);
    });

    it('splits at an annotation from Split here', () => {
      fixture.componentRef.setInput('timeline', ccEventTimeline());
      fixture.componentRef.setInput('rows', eventRows());
      fixture.detectChanges();
      el.querySelector<HTMLButtonElement>('.cc-pu-split[aria-label="Split the periods at annotation A1"]')!.click();
      fixture.detectChanges();
      expect(ruleValue()).toBe('annotation');
      expect(component.presetAnnotationId).toBe(11);
      expect(el.querySelector<HTMLSelectElement>('#cc-wiz-annotation')!.value).toBe('11');
      expect(bounds()).toEqual(['201', '201', '202', '206']);
      expect(note()).toContain('Split at annotation A1.');
    });

    it('says when there is no Overseer change to split at', () => {
      fixture.componentRef.setInput('timeline', ccTimeline({ events: [] }));
      fixture.detectChanges();
      chooseRule('event');
      expect(el.querySelector('#cc-wiz-event')).toBeNull();
      expect(textOf(el.querySelector('.cc-wiz-preset-note'))).toBe('No Overseer change was detected in the timeline range.');
      expect(bounds()).toEqual(['', '', '', '']);
      expect(component.periodsError).toBe('Choose the first and last run of both periods.');
    });

    it('splits at an annotation, and says when one side of it has no run', () => {
      chooseRule('annotation');
      expect(bounds()).toEqual(['101', '104', '105', '106']);
      expect(textOf(el.querySelector('#cc-wiz-annotation option'))).toBe('A1 · 2026-09-20: New snapshot announced');
      expect(textOf(el.querySelector('.cc-wiz-preset-note')))
        .toBe('The runs before the annotation (2026-09-20 12:00 UTC) against those from it: 4 runs against 2.');
      expect(component.presetAnchorUtc).toBe('2026-09-20T12:00:00Z');

      fixture.componentRef.setInput('timeline', ccTimeline({ annotations: [ccAnnotation(2, { atUtc: '2026-10-08T00:00:00Z' })] }));
      fixture.detectChanges();
      expect(textOf(el.querySelector('.cc-wiz-preset-note'))).toBe('No run on one side of the annotation (2026-10-08).');
      expect(bounds()).toEqual(['', '', '', '']);
    });

    it('confirms on later data: the last baseline against the runs after the analysis was saved', () => {
      fixture.componentRef.setInput('analyses', [ccAnalysisSummary(7), ccAnalysisSummary(9, { createdAtUtc: '2026-09-01T00:00:00Z' })]);
      fixture.detectChanges();
      chooseRule('later');
      // Analysis 7 was saved on 2026-10-02 09:00, after the last run.
      expect(textOf(el.querySelector('.cc-wiz-preset-note'))).toBe('No run was made after the last analysis was saved (2026-10-02 09:00 UTC).');

      fixture.componentRef.setInput('analyses', [ccAnalysisSummary(7, { createdAtUtc: '2026-09-20T07:00:00Z' })]);
      fixture.detectChanges();
      // The same option fires no change when chosen again, so the rule is chosen afresh.
      chooseRule('custom');
      chooseRule('later');
      expect(bounds()).toEqual(['101', '103', '104', '106']);
      expect(textOf(el.querySelector('.cc-wiz-preset-note'))).toBe('Compares the runs after the last analysis, saved 2026-09-20 07:00 UTC, with its baseline.');
    });
  });

  describe('period cards', () => {
    it('lists every step-1 run oldest first, with the four bound toggles named for the run', () => {
      expect(textOf(el.querySelector('#cc-an-units-title'))).toBe('Runs');
      expect(Array.from(el.querySelectorAll('article.cc-pu-card')).map(card => card.getAttribute('data-unit-id')))
        .toEqual(['101', '102', '103', '104', '105', '106']);
      expect(toggle(102, 'comparisonLastId').getAttribute('aria-label')).toBe('Make run #102 the comparison\'s last run');
      expect(el.querySelector('table')).toBeNull();
      expect(el.querySelector('select.cc-wiz-run-select')).toBeNull();

      press(102, 'baselineLastId');
      press(105, 'comparisonFirstId');
      expect(unitPeriods()).toEqual([
        ['101', 'Baseline'], ['102', 'Baseline'], ['103', 'Not used'], ['104', 'Not used'], ['105', 'Comparison'], ['106', 'Comparison']
      ]);
      expect(textOf(el.querySelector('.cc-ps-not-used'))).toContain('2 runs');
    });

    it('marks an ineligible run inside a period Not eligible and never sends it', () => {
      fixture.componentRef.setInput('rows', rowsWithIneligible102());
      fixture.detectChanges();
      expect(bounds()).toEqual(['101', '103', '104', '106']);
      expect(unitPeriods()[1]).toEqual(['102', 'Not eligible']);
      expect(strip('baseline', '.cc-ps-count')).toBe('2 runs on 2 days · 2026-09-01 to 2026-09-12');
      const request = component.buildRequest()!;
      expect(request.baselineRunIds).toEqual([101, 103]);
      expect(request.comparisonRunIds).toEqual([104, 105, 106]);
      // Its matched control is no candidate either.
      expect(controlLabels()).toEqual(['Control run #201', 'Control run #205', 'Control run #206']);
      expect(component.previewNotes.map(entry => entry.text))
        .toContain('Run #102 is inside the baseline but not eligible and is not analyzed.');
    });

    it('says in the strip when a period is short of the minimum sample', () => {
      press(101, 'baselineLastId');
      expect(strip('baseline', '.cc-ps-sample')).toContain('Below the minimum sample');
      expect(strip('baseline', '.cc-ps-sample')).toContain('P1, P4 and P5 need at least 2 on 2 days to be Established.');
      expect(component.sampleLine('baseline'))
        .toBe('Baseline: 1 run on 1 day (2026-09-01). P1, P4 and P5 need at least 2 on 2 days to be Established.');
    });

    it('goes to a bound\'s card from the summary strip, focusing its title', () => {
      el.querySelector<HTMLButtonElement>('.cc-ps-period[data-period="comparison"] .cc-ps-bound[data-bound="comparisonFirstId"]')!.click();
      fixture.detectChanges();
      expect(document.activeElement?.id).toBe('cc-pu-104-title');
    });

    it('forwards the run report of a card', () => {
      const reports: number[] = [];
      component.openRunReport.subscribe(id => reports.push(id));
      el.querySelector<HTMLButtonElement>('article.cc-pu-card[data-unit-id="104"] .cc-pu-report')!.click();
      expect(reports).toEqual([104]);
    });
  });

  describe('control runs', () => {
    it('offers the matched controls of the runs in both periods, all checked, with the hint in an info tip', () => {
      const fieldset = el.querySelector<HTMLFieldSetElement>('fieldset.cc-wiz-controls')!;
      expect(textOf(fieldset.querySelector('legend'))).toContain('Control runs');
      expect(fieldset.getAttribute('aria-describedby')).toBe('cc-wiz-controls-hint');
      expect(textOf(el.querySelector('#cc-wiz-controls-hint')))
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

      press(102, 'baselineFirstId');
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

    it('keeps the pooling hint in an info tip that still describes the checkbox', () => {
      expect(textOf(el.querySelector('.cc-wiz-relaxed'))).toBe('Pool across measurement segment boundaries');
      expect(el.querySelector('#cc-wiz-relaxed')!.getAttribute('aria-describedby')).toBe('cc-wiz-relaxed-hint');
      expect(textOf(el.querySelector('#cc-wiz-relaxed-hint'))).toContain('caps grades at Indicated');
    });
  });

  describe('preview', () => {
    const endpointRows = (): string[][] => Array.from(el.querySelectorAll<HTMLElement>('li.cc-ap-endpoint'))
      .map(row => [row.getAttribute('data-endpoint') ?? '', row.getAttribute('data-status') ?? '']);
    const groupKeys = (): (string | null)[] =>
      Array.from(el.querySelectorAll<HTMLElement>('.cc-ap-event-groups > li')).map(line => line.getAttribute('data-group-key'));

    it('lists the input, the endpoint readiness and the notes', () => {
      expect(textOf(el.querySelector('#cc-ap-title'))).toBe('What the analysis will see');
      expect(fact('name')).toBe('Automatic');
      expect(fact('rule')).toBe('Earliest vs latest');
      expect(fact('baseline')).toContain('#101 → #103 · 3 runs on 3 days');
      expect(fact('baseline')).toContain('Window 2026-09-01 00:00 to 2026-09-12 23:59 UTC');
      expect(fact('notUsed')).toBe('None');
      expect(fact('leftOut')).toBeNull();
      expect(fact('controls')).toBe('4 of 4 matched controls checked');
      expect(fact('grading')).toBe('Native grades');
      expect(fact('pooling')).toBe('Off');
      expect(fact('protocol')).toBe('V1');

      // Run 103 has no call telemetry, so P2 and P3 have two baseline runs in the common stratum.
      expect(endpointRows()).toEqual([
        ['P1', 'meets'], ['P2', 'belowMinimum'], ['P3', 'belowMinimum'], ['P4', 'meets'], ['P5', 'meets']
      ]);
      expect(Array.from(el.querySelectorAll<HTMLElement>('li.cc-ap-note')).map(item => item.getAttribute('data-kind')))
        .toEqual(['events', 'controls']);
      expect(groupKeys()).toEqual(['2026-09-15|30']);
      expect(textOf(el.querySelector('li.cc-ap-note[data-kind="controls"]')))
        .toContain('2 of 6 runs have no matched control run (#103, #104)');
    });

    it('names the overrides, the anchor and pooling in the input', () => {
      const p2 = el.querySelector<HTMLInputElement>('#cc-wiz-margin-P2')!;
      p2.value = '20';
      p2.dispatchEvent(new Event('input'));
      const alpha = el.querySelector<HTMLInputElement>('#cc-wiz-alpha')!;
      alpha.value = '0.1';
      alpha.dispatchEvent(new Event('input'));
      el.querySelector<HTMLInputElement>('#cc-wiz-relaxed')!.click();
      chooseRule('annotation');
      expect(fact('protocol')).toBe('V1 with overrides: P2 margin 20 %, α 0.1');
      expect(fact('pooling')).toBe('On — grades capped at Indicated');
      expect(fact('rule')).toBe('Before vs after an annotation · A1 (2026-09-20)');
    });

    it('lists the composite events between the baseline\'s first day and the comparison\'s last', () => {
      fixture.componentRef.setInput('timeline', ccEventTimeline());
      fixture.componentRef.setInput('rows', eventRows());
      fixture.detectChanges();
      chooseRule('custom');
      setBound(204, 'baselineFirstId');
      setBound(204, 'baselineLastId');
      setBound(205, 'comparisonFirstId');
      setBound(205, 'comparisonLastId');
      expect(bounds()).toEqual(['204', '204', '205', '205']);
      expect(groupKeys()).toEqual(['2026-09-05|28']);

      setBound(201, 'baselineFirstId');
      setBound(206, 'comparisonLastId');
      expect(groupKeys()).toEqual(['2026-09-03|27', '2026-09-05|28', '2026-09-09|29', '2026-09-10|29']);
    });

    it('has no Overseer change note when none falls in the span', () => {
      fixture.componentRef.setInput('timeline', ccTimeline({ events: [] }));
      fixture.detectChanges();
      expect(el.querySelector('li.cc-ap-note[data-kind="events"]')).toBeNull();
      expect(el.querySelector('.cc-ap-event-groups')).toBeNull();
      expect(component.noteCount).toBe(1);
    });

    it('waits for valid periods before endpoint readiness', () => {
      press(103, 'comparisonFirstId');
      expect(el.querySelector('li.cc-ap-endpoint')).toBeNull();
      expect(textOf(el.querySelector('.cc-ap-waiting'))).toContain('The comparison must start after the baseline\'s last run.');
    });
  });

  describe('analyze', () => {
    it('emits stateChange whenever the state the outer wizard reads may have changed', () => {
      let changes = 0;
      component.stateChange.subscribe(() => changes++);
      chooseRule('custom');
      expect(changes).toBe(1);

      const alpha = el.querySelector<HTMLInputElement>('#cc-wiz-alpha')!;
      alpha.value = '0.1';
      alpha.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(changes).toBe(2);

      // The controls already follow these periods.
      component.preselectRuns();
      expect(changes).toBe(2);

      press(102, 'baselineLastId');
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
      expect(textOf(el.querySelector('app-cc-results-view details[data-section="about"] dd.cc-res-headline')))
        .toContain('Overseer chat with GPT-5 high');
    });

    it('sends nothing while Analyze is blocked', () => {
      press(103, 'comparisonFirstId');
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

    it('says which runs the rules span: every run in the dates, then the runs chosen in step 1', () => {
      expect(textOf(el.querySelector('.cc-wiz-span-note'))).toBe('Presets use every run in the dates: #101 (2026-09-01) to #106 (2026-10-01), 6 runs.');
      narrow();
      expect(textOf(el.querySelector('.cc-wiz-span-note'))).toBe('Presets use the runs chosen in step 1: #102 (2026-09-05) to #105 (2026-09-26), 3 runs.');
      expect(textOf(el.querySelector('.cc-an-compared-units'))).toBe('3 runs from step 1');
    });

    it('applies the chosen rule again when the selection changes', () => {
      narrow();
      expect(ruleValue()).toBe('earliest');
      expect(bounds()).toEqual(['102', '103', '105', '105']);
      expect(textOf(el.querySelector('.cc-wiz-preset-note')))
        .toBe('The runs span 22 days: the earlier days against the later days, split at 2026-09-26, 2 runs against 1.');
      expect(unitPeriods().map(([id]) => id)).toEqual(['102', '103', '105']);
    });

    it('keeps Manual bounds while their runs stay, and clears a bound whose run left', () => {
      press(102, 'baselineFirstId');
      expect(bounds()).toEqual(['102', '103', '104', '106']);
      narrow();
      expect(component.preset).toBe('custom');
      expect(bounds()).toEqual(['102', '103', '', '']);
      expect(component.periodsError).toBe('Choose the first and last run of both periods.');
    });

    it('names the left-out runs inside the windows', () => {
      const all = ccRunRows();
      fixture.componentRef.setInput('allRows', all);
      fixture.componentRef.setInput('rows', all.filter(row => row.runId !== 103));
      fixture.componentRef.setInput('scope', { firstRunId: null, lastRunId: null, leftOut: new Set([103]) });
      fixture.componentRef.setInput('scopeKey', '||103');
      fixture.detectChanges();
      // Earliest vs latest ends the baseline with run 102 on 2026-09-05, before run 103.
      expect(fact('leftOut')).toBeNull();

      press(104, 'baselineLastId');
      press(105, 'comparisonFirstId');
      expect(fact('leftOut')).toBe('#103');
      expect(textOf(el.querySelector('li.cc-ap-note[data-kind="leftOut"]')))
        .toContain('Run left out in step 1 inside the windows: #103. It is not analyzed.');
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

    it('says which battery runs the rules span, and names the compared battery and its units', () => {
      withBatteries();
      expect(textOf(el.querySelector('.cc-wiz-span-note')))
        .toBe('Presets use every battery run in the dates: #11 (2026-10-08) to #12 (2026-10-08), 2 battery runs.');
      fixture.componentRef.setInput('scope', { ...CC_EMPTY_BATTERY_SCOPE, firstRunId: 11 });
      fixture.detectChanges();
      expect(textOf(el.querySelector('.cc-wiz-span-note')))
        .toBe('Presets use the battery runs chosen in step 1: #11 (2026-10-08) to #12 (2026-10-08), 2 battery runs.');
      const tag = el.querySelector('.cc-an-subject .cc-kind-tag')!;
      expect(tag.getAttribute('data-kind')).toBe('battery');
      expect(textOf(tag)).toBe('Battery');
      expect(textOf(el.querySelector('.cc-an-compared-label'))).toBe('Two initial suites (revision 1)');
      expect(textOf(el.querySelector('.cc-an-compared-units')))
        .toBe('2 battery runs from step 1 · each analyzed as one unit, its member runs together');
    });

    it('splits battery runs #11 and #12 of the same day into a baseline of #11 and a comparison of #12', () => {
      withBatteries();
      expect(ruleValue()).toBe('earliest');
      expect(bounds()).toEqual(['11', '11', '12', '12']);
      expect(el.querySelector('.cc-wiz-periods-error')).toBeNull();
      expect(component.periodsError).toBe('');
      expect(textOf(el.querySelector('.cc-wiz-preset-note')))
        .toBe('The runs span 1 day: the earlier half against the later half, 1 battery run each.');

      expect(textOf(el.querySelector('#cc-an-units-title'))).toBe('Battery runs');
      expect(toggle(12, 'comparisonFirstId').getAttribute('aria-label')).toBe('Make battery run #12 the comparison\'s first run');
      expect(unitPeriods()).toEqual([['11', 'Baseline'], ['12', 'Comparison']]);
      expect(strip('baseline', '.cc-ps-count')).toBe('1 battery run on 1 day · 2026-10-08');
      expect(strip('baseline', '.cc-ps-sample')).toContain('Below the minimum sample');
      expect(strip('comparison', '.cc-ps-window')).toBe('Window 2026-10-08 10:00 to 2026-10-08 23:59 UTC');

      // The controls stay per run: the matched controls of the members.
      expect(controlLabels()).toEqual(['Control run #404']);
      expect(component.regradeRunIds).toEqual([301, 302, 303, 304, 404]);
      expect(textOf(el.querySelector('li.cc-ap-note[data-kind="controls"]')))
        .toContain('3 of 4 runs have no matched control run (#301, #302, #303)');
    });

    it('opens a battery run\'s report and its members\' from the card', () => {
      withBatteries();
      const batteries: number[] = [];
      const runs: number[] = [];
      component.openBatteryRunReport.subscribe(id => batteries.push(id));
      component.openRunReport.subscribe(id => runs.push(id));
      el.querySelector<HTMLButtonElement>('article.cc-pu-card[data-unit-id="12"] .cc-pu-report')!.click();
      el.querySelector<HTMLButtonElement>('article.cc-pu-card[data-unit-id="12"] .cc-battery-member[data-run-id="304"] button')!.click();
      expect(batteries).toEqual([12]);
      expect(runs).toEqual([304]);
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
      chooseRule('annotation');
      expect(bounds()).toEqual(['11', '11', '12', '12']);
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
      chooseRule('later');
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
      expect(ruleValue()).toBe('custom');
    });
  });

  describe('a saved result', () => {
    it('loads a saved analysis without moving the step, for the outer wizard to show on Results', () => {
      let changes = 0;
      component.stateChange.subscribe(() => changes++);
      press(102, 'baselineLastId');
      changes = 0;
      component.showResult(ccAnalysisResult());
      fixture.detectChanges();

      expect(changes).toBe(1);
      expect(component.step).toBe('analyze');
      expect(shownSteps()).toEqual(['analyze']);
      expect(bounds()).toEqual(['101', '103', '104', '106']);
      expect(ruleValue()).toBe('custom');
      expect(note()).toBe('');
      expect(component.reachable('results')).toBe(true);

      goToResults();
      expect(el.querySelector('app-cc-results-view')).not.toBeNull();
      expect(textOf(el.querySelector('dd.cc-res-headline'))).toContain('Overseer chat with GPT-5 high');
    });

    it('shows the results without the reports, handing them every loaded run and the tagged annotations', () => {
      const all = ccRunRows();
      fixture.componentRef.setInput('allRows', all);
      fixture.componentRef.setInput('allBatteryRows', ccBatteryRunRows());
      fixture.detectChanges();
      component.showResult(ccAnalysisResult());
      goToResults();

      const results = fixture.debugElement.query(By.css('app-cc-results-view')).componentInstance as {
        rows: unknown; batteryRows: unknown; annotations: unknown; result: CcAnalysisResult;
      };
      expect(results.result.analysisId).toBe(7);
      expect(results.rows).toBe(all);
      expect(results.batteryRows).toBe(component.allBatteryRows);
      expect(results.annotations).toBe(component.taggedAnnotations);
      expect(el.querySelector('.cc-wiz-reports')).toBeNull();
      expect(el.querySelector('app-cc-reports-step')).toBeNull();
      expect(component.reportsStep).toBeUndefined();
    });

    it('forwards the results\' run report requests', () => {
      const runs: number[] = [];
      const batteryRuns: number[] = [];
      component.openRunReport.subscribe(id => runs.push(id));
      component.openBatteryRunReport.subscribe(id => batteryRuns.push(id));
      component.showResult(ccAnalysisResult());
      goToResults();

      const results = fixture.debugElement.query(By.css('app-cc-results-view')).componentInstance as {
        openRunReport: { emit(id: number): void }; openBatteryRunReport: { emit(id: number): void };
      };
      results.openRunReport.emit(104);
      results.openBatteryRunReport.emit(12);
      expect(runs).toEqual([104]);
      expect(batteryRuns).toEqual([12]);
    });

    it('renders the Reports step on its first visit and keeps it mounted and hidden afterwards', () => {
      component.showResult(ccAnalysisResult());
      goToReports();
      expect(shownSteps()).toEqual(['reports']);
      const reports = el.querySelector('.cc-wiz-step[data-step="reports"] > app-cc-reports-step')!;
      expect(reports).not.toBeNull();
      expect(body('reports')!.classList).toContain('cc-wiz-step-fill');
      expect(el.classList).toContain('is-fill');
      expect(component.reportsStep).toBeDefined();

      goToResults();
      expect(body('reports')!.hidden).toBe(true);
      expect(el.classList).not.toContain('is-fill');
      setStep('reports');
      // The same step is kept, so it asks for nothing again.
      expect(el.querySelector('app-cc-reports-step')).toBe(reports);
    });

    it('asks the outer wizard for the Documents step on the Reports step\'s See the documents', () => {
      const requested: string[] = [];
      component.stepRequested.subscribe(step => requested.push(step));
      component.showResult(ccAnalysisResult());
      goToReports();

      component.reportsStep!.documentsRequested.emit();
      expect(requested).toEqual(['documents']);
    });

    it('emits stateChange when the Reports step\'s job or chart state changes', () => {
      component.showResult(ccAnalysisResult());
      goToReports();
      let changes = 0;
      component.stateChange.subscribe(() => changes++);

      component.reportsStep!.stateChange.emit();
      expect(changes).toBe(1);
    });

    it('renders the Documents step as the Download Center panel on the analysis\'s documents', () => {
      component.showResult(ccAnalysisResult());
      goToDocuments();
      expect(shownSteps()).toEqual(['documents']);

      const step = body('documents')!;
      expect(step.classList).toContain('cc-wiz-step-fill');
      expect(textOf(step.querySelector('p.form-hint.cc-documents-note')))
        .toBe('The documents of analysis #7 — September check. View them here, or select them to download.');
      const panel = fixture.debugElement.query(By.directive(DownloadCenterPanelComponent)).componentInstance as DownloadCenterPanelComponent;
      expect(panel.idPrefix).toBe('cc-dc');
      expect(panel.context).toEqual({ kind: 'chatConsistency', analysisId: 7 });
      expect(panel.chartActions).toBeNull();
    });

    it('keeps one Download Center context per saved analysis', () => {
      component.showResult(ccAnalysisResult());
      const context = component.documentsContext;
      expect(context).toEqual({ kind: 'chatConsistency', analysisId: 7 });
      expect(component.documentsContext, 'one object per analysis id').toBe(context);

      component.showResult(ccAnalysisResult({ name: 'Renamed' }));
      expect(component.documentsContext).toBe(context);

      component.showResult(ccAnalysisResult({ analysisId: 8 }));
      expect(component.documentsContext).toEqual({ kind: 'chatConsistency', analysisId: 8 });
      expect(component.documentsContext).not.toBe(context);

      component.showResult(ccAnalysisResult({ analysisId: null }));
      expect(component.documentsContext).toBeNull();
    });

    it('lists the documents again on every entry to the Documents step', () => {
      component.showResult(ccAnalysisResult());
      goToDocuments();
      const token = component.documentsReloadToken;

      goToResults();
      expect(component.documentsReloadToken).toBe(token);
      setStep('documents');
      expect(component.documentsReloadToken).toBe(token + 1);
      expectPanelList().flush([]);
      fixture.detectChanges();
    });

    it('lists the Documents step again when the Reports step\'s documents change', () => {
      component.showResult(ccAnalysisResult());
      goToReports();
      goToDocuments();
      const token = component.documentsReloadToken;

      component.reportsStep!.documentsChanged.emit();
      fixture.detectChanges();
      expect(component.documentsReloadToken).toBe(token + 1);
      expectPanelList().flush([]);
      fixture.detectChanges();
    });

    it('lists the Reports step\'s documents again after a change in the Documents step', () => {
      component.showResult(ccAnalysisResult());
      goToReports();
      goToDocuments();
      const reload = vi.spyOn(component.reportsStep!, 'reloadDocuments');

      const panel = fixture.debugElement.query(By.directive(DownloadCenterPanelComponent)).componentInstance as DownloadCenterPanelComponent;
      panel.documentsChanged.emit();
      expect(reload).toHaveBeenCalledTimes(1);
      const documents = http.expectOne(r => r.url === DOCUMENTS_URL && !r.params.has('origin'));
      expect(documents.request.params.get('subject')).toBe('chat-consistency:7');
      documents.flush([]);
      fixture.detectChanges();
    });

    it('lists nothing again from the Documents step before the Reports step was visited', () => {
      component.showResult(ccAnalysisResult());
      goToDocuments();
      expect(component.reportsStep).toBeUndefined();

      const panel = fixture.debugElement.query(By.directive(DownloadCenterPanelComponent)).componentInstance as DownloadCenterPanelComponent;
      panel.documentsChanged.emit();
      http.expectNone(r => r.url === DOCUMENTS_URL);
    });

    it('names the bounds once the saved analysis\'s runs arrive, keeping its controls', () => {
      fixture.componentRef.setInput('rows', []);
      fixture.detectChanges();
      component.showResult(ccAnalysisResult({ controls: { matches: [], effects: [], missingControls: [], controlRunIds: [205] } }));
      fixture.detectChanges();
      expect(component.ids).toEqual({ baselineFirstId: null, baselineLastId: null, comparisonFirstId: null, comparisonLastId: null });
      expect(component.periodsError).toBe('Choose at least two runs in step 1, one for each period.');

      fixture.componentRef.setInput('rows', ccRunRows());
      fixture.detectChanges();
      expect(bounds()).toEqual(['101', '103', '104', '106']);
      expect(component.preset).toBe('custom');
      expect(checkedControls()).toEqual(['Control run #205']);
    });

    it('leaves the bounds unset when the saved analysis\'s runs are not in step 1, and still shows the result', () => {
      component.showResult(ccAnalysisResult({
        baseline: { ...ccAnalysisResult().baseline, runIds: [1, 2] },
        comparison: { ...ccAnalysisResult().comparison, runIds: [3] }
      }));
      fixture.detectChanges();
      expect(bounds()).toEqual(['', '', '', '']);
      expect(component.periodsError).toBe('Choose the first and last run of both periods.');
      expect(component.reachable('results')).toBe(true);
    });

    it('reports chartsAttaching while the Reports step attaches charts', () => {
      expect(component.chartsAttaching).toBe(false);
      component.showResult(ccAnalysisResult());
      goToReports();

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
