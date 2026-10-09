import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';

import { CC_EMPTY_BATTERY_SCOPE } from '../chat-consistency-scope';
import { CcAnalysisResult } from '../chat-consistency.models';
import {
  CC_API,
  CC_BATTERY_SET_KEY,
  ccAnalysisResult,
  ccAxis,
  ccBatteryMemberRows,
  ccBatteryRunRows,
  ccComparisonSets,
  ccRunRows,
  chatConsistencyTestProviders,
  textOf
} from '../chat-consistency-tab.testing';
import { CcWizardComponent } from './cc-wizard.component';

/** Every Chat Consistency storage key, cleared around each test. */
const STORAGE_KEYS = [
  'overseer.benchmark.chatConsistency.timeline',
  'overseer.benchmark.chatConsistency.chartSize',
  'overseer.benchmark.chatConsistency.launcher'
];

const STEP_LABELS = ['1. Model', '2. Timeline', '3. Analyze', '4. Results'];

function clearStorage(): void {
  for (const key of STORAGE_KEYS) {
    try {
      localStorage.removeItem(key);
    } catch {
      // Nothing stored.
    }
  }
}

/**
 * The wizard shell. The steps it mounts make requests of their own (the re-grade job on step 3, the
 * report job and documents on step 4); they are left unanswered, so `verify()` is not called here.
 */
describe('CcWizardComponent', () => {
  let fixture: ComponentFixture<CcWizardComponent>;
  let wizard: CcWizardComponent;
  let el: HTMLElement;
  let http: HttpTestingController;

  beforeEach(async () => {
    clearStorage();
    await TestBed.configureTestingModule({
      imports: [CcWizardComponent],
      providers: chatConsistencyTestProviders()
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CcWizardComponent);
    wizard = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('axes', [ccAxis()]);
    fixture.detectChanges();
  });

  afterEach(() => {
    fixture.destroy();
    clearStorage();
  });

  // --- Helpers ---

  function chooseModel(): void {
    fixture.componentRef.setInput('selectedKey', 'openai/gpt-5|high');
    fixture.componentRef.setInput('rows', ccRunRows());
    fixture.detectChanges();
  }

  /** Renders, then again after the shell's microtask re-check that follows the analysis component's `stateChange`. */
  async function settle(): Promise<void> {
    fixture.detectChanges();
    await new Promise<void>(resolve => setTimeout(resolve));
    fixture.detectChanges();
  }

  const tab = (step: number): HTMLButtonElement => el.querySelector<HTMLButtonElement>(`#cc-step-tab-${step}`)!;
  const tabs = (): HTMLButtonElement[] =>
    Array.from(el.querySelectorAll<HTMLButtonElement>('[role="tablist"][aria-label="Chat consistency steps"] [role="tab"]'));
  const next = (): HTMLButtonElement => el.querySelector<HTMLButtonElement>('.cc-wizard-next')!;
  const previous = (): HTMLButtonElement => el.querySelector<HTMLButtonElement>('.cc-wizard-previous')!;
  const closeButton = (): HTMLButtonElement => el.querySelector<HTMLButtonElement>('.cc-wizard-close')!;
  const panel = (id: string): HTMLElement | null => el.querySelector<HTMLElement>(`#${id}`);

  function press(target: Element, key: string): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  function analyzeRequests() {
    return http.match(r => r.method === 'POST' && r.url === `${CC_API}/analyses`);
  }

  // --- Step tabs ---

  it('renders four step tabs; without a model only Model opens, and every other tab is described by its reason', () => {
    expect(textOf(el.querySelector('#cc-wizard-title'))).toBe('Chat consistency');
    expect(textOf(el.querySelector('.cc-wizard-subtitle'))).toBe('Choose a model, then follow the steps');

    expect(tabs().map(entry => textOf(entry))).toEqual(STEP_LABELS);
    expect(tabs().map(entry => entry.getAttribute('aria-selected'))).toEqual(['true', 'false', 'false', 'false']);
    expect(tabs().map(entry => entry.getAttribute('tabindex'))).toEqual(['0', '-1', '-1', '-1']);
    expect(tabs().map(entry => entry.getAttribute('aria-disabled'))).toEqual(['false', 'true', 'true', 'true']);
    expect(tab(1).hasAttribute('aria-describedby')).toBe(false);
    expect(el.querySelector('#cc-step-blocked-1')).toBeNull();
    for (const step of [2, 3, 4]) {
      expect(tab(step).getAttribute('aria-describedby')).toBe(`cc-step-blocked-${step}`);
      expect(el.querySelector(`#cc-step-blocked-${step}`)!.classList).toContain('visually-hidden');
    }
    expect([2, 3, 4].map(step => textOf(el.querySelector(`#cc-step-blocked-${step}`)))).toEqual([
      'Choose a model first.',
      'Choose a model first.',
      'Analyze first, or open a saved analysis.'
    ]);
    expect(tab(1).getAttribute('aria-controls')).toBe('cc-step-panel-1');
    expect(tab(2).getAttribute('aria-controls')).toBe('cc-step-panel-2');
    expect([3, 4].map(step => tab(step).getAttribute('aria-controls'))).toEqual(Array(2).fill('cc-step-panel-analysis'));

    // A blocked tab does not open.
    tab(2).click();
    fixture.detectChanges();
    expect(wizard.step).toBe(1);

    expect(textOf(next())).toBe('Next: Timeline');
    expect(next().getAttribute('aria-disabled')).toBe('true');
    expect(next().getAttribute('aria-describedby')).toBe('cc-next-blocked');
    expect(textOf(el.querySelector('#cc-next-blocked'))).toBe('Choose a model first.');
    expect(previous().disabled).toBe(true);
    expect(textOf(el.querySelector('.gh-wizard-position'))).toContain('Step 1 of 4 — Model');
  });

  it('with a model, opens Timeline and Analyze, waits for a result before Results, and names the model', () => {
    chooseModel();

    expect(tabs().map(entry => entry.getAttribute('aria-disabled'))).toEqual(['false', 'false', 'false', 'true']);
    expect(textOf(el.querySelector('#cc-step-blocked-4'))).toBe('Analyze first, or open a saved analysis.');
    expect(el.querySelector('#cc-step-blocked-2')).toBeNull();
    expect(next().getAttribute('aria-disabled')).toBe('false');
    expect(el.querySelector('#cc-next-blocked')).toBeNull();
    expect(textOf(el.querySelector('.cc-wizard-subtitle'))).toBe('GPT-5 high · 6 runs · All dates');

    fixture.componentRef.setInput('range', { preset: 'custom', fromDay: '2026-09-10', toDay: '', anchorUtc: null });
    fixture.detectChanges();
    expect(textOf(el.querySelector('.cc-wizard-subtitle'))).toBe('GPT-5 high · 6 runs · From 2026-09-10');

    fixture.componentRef.setInput('range', { preset: '30d', fromDay: '', toDay: '', anchorUtc: '2026-10-07T00:00:00.000Z' });
    fixture.detectChanges();
    expect(textOf(el.querySelector('.cc-wizard-subtitle'))).toBe('GPT-5 high · 6 runs · Last 30 days');
  });

  it('counts the runs in the analysis in the subtitle and hands the scoped runs to the steps', () => {
    chooseModel();
    const scope = { firstRunId: 102, lastRunId: 105, leftOut: new Set([104]) };
    fixture.componentRef.setInput('scope', scope);
    fixture.detectChanges();
    expect(textOf(el.querySelector('.cc-wizard-subtitle'))).toBe('GPT-5 high · 6 runs · All dates · 3 in the analysis');

    expect(wizard.scopedRows.map(row => row.runId)).toEqual([102, 103, 105]);
    expect([...wizard.notAnalyzed]).toEqual([[101, 'beforeSpan'], [104, 'leftOut'], [106, 'afterSpan']]);
    expect(wizard.scopedRows).toBe(wizard.scopedRows);

    tab(3).click();
    fixture.detectChanges();
    expect(wizard.analysis!.rows.map(row => row.runId)).toEqual([102, 103, 105]);
    expect(wizard.analysis!.allRows.length).toBe(6);
    expect(wizard.analysis!.scopeKey).toBe('102|105|104');

    tab(2).click();
    fixture.detectChanges();
    expect(wizard.timelineWorkspace!.notAnalyzed).toBe(wizard.notAnalyzed);
    expect(wizard.timelineWorkspace!.rangeLabel).toBe('All dates');
  });

  it('names the compared battery in the subtitle, hands the battery runs and their members to the steps, and marks the runs outside it', () => {
    fixture.componentRef.setInput('selectedKey', 'openai/gpt-5|high');
    fixture.componentRef.setInput('rows', [...ccBatteryMemberRows(), ...ccRunRows()]);
    fixture.componentRef.setInput('batteryRows', ccBatteryRunRows());
    fixture.componentRef.setInput('comparisonSets', ccComparisonSets());
    fixture.componentRef.setInput('compareKey', CC_BATTERY_SET_KEY);
    fixture.componentRef.setInput('scope', CC_EMPTY_BATTERY_SCOPE);
    fixture.detectChanges();
    expect(textOf(el.querySelector('.cc-wizard-subtitle'))).toBe('GPT-5 high · Two initial suites (revision 1) · 2 battery runs · All dates');
    expect(el.querySelectorAll('#cc-step-panel-1 .cc-battery-card').length).toBe(2);

    fixture.componentRef.setInput('scope', { ...CC_EMPTY_BATTERY_SCOPE, leftOut: new Set([11]) });
    fixture.detectChanges();
    expect(textOf(el.querySelector('.cc-wizard-subtitle'))).toBe('GPT-5 high · Two initial suites (revision 1) · 2 battery runs · All dates · 1 in the analysis');
    expect(wizard.scopedBatteryRows.map(row => row.batteryRunId)).toEqual([12]);
    expect(wizard.scopedRows.map(row => row.runId)).toEqual([303, 304]);
    expect(wizard.notAnalyzed.get(301)).toBe('leftOut');
    expect(wizard.notAnalyzed.get(106)).toBe('outsideSet');
    expect(wizard.notAnalyzed.has(303)).toBe(false);
    expect(wizard.scopeKeyValue).toBe(`${CC_BATTERY_SET_KEY}#||11`);

    tab(3).click();
    fixture.detectChanges();
    expect(wizard.analysis!.compareSet?.key).toBe(CC_BATTERY_SET_KEY);
    expect(wizard.analysis!.batteryRows.map(row => row.batteryRunId)).toEqual([12]);
    expect(wizard.analysis!.allBatteryRows.length).toBe(2);
    expect(wizard.analysis!.rows.map(row => row.runId)).toEqual([303, 304]);

    tab(2).click();
    fixture.detectChanges();
    const workspace = wizard.timelineWorkspace!;
    expect(workspace.notAnalyzed).toBe(wizard.notAnalyzed);
    // The timeline plots the set's battery runs, keyed by battery run id.
    expect(workspace.unitKind).toBe('batteryRun');
    expect(workspace.setKey).toBe(CC_BATTERY_SET_KEY);
    expect(workspace.batteryRows.map(row => row.batteryRunId)).toEqual([12, 11]);
    expect([...workspace.notAnalyzedUnits!]).toEqual([[11, 'leftOut']]);
    // The set names the charts' footer.
    expect(workspace.setLabel).toBe('Two initial suites (revision 1)');
    expect(workspace.setKind).toBe('battery');
    expect(workspace.rangeLabel).toBe(wizard.rangeText);
  });

  it('hands a run-by-run timeline its runs and no compared set', () => {
    chooseModel();
    tab(2).click();
    fixture.detectChanges();
    const workspace = wizard.timelineWorkspace!;
    expect(workspace.unitKind).toBe('run');
    expect(workspace.setKey).toBeNull();
    expect(workspace.notAnalyzedUnits!.size).toBe(0);
    expect(workspace.setLabel).toBe('');
    expect(workspace.setKind).toBeNull();
  });

  it('asks for another compared set from step 1', () => {
    chooseModel();
    fixture.componentRef.setInput('comparisonSets', ccComparisonSets());
    fixture.componentRef.setInput('compareKey', 'suite:id:5');
    fixture.detectChanges();
    expect(textOf(el.querySelector('.cc-wizard-subtitle'))).toBe('GPT-5 high · Board Suite · 6 runs · All dates');
    const chosen: string[] = [];
    wizard.compareChange.subscribe(key => chosen.push(key));
    const select = el.querySelector<HTMLSelectElement>('#cc-compare')!;
    select.value = CC_BATTERY_SET_KEY;
    select.dispatchEvent(new Event('change'));
    expect(chosen).toEqual([CC_BATTERY_SET_KEY]);
  });

  it('moves between the tabs with Left / Right (wrapping) and Home / End, keeping focus on the tabs', () => {
    chooseModel();
    tab(1).focus();

    const right = press(tab(1), 'ArrowRight');
    expect(right.defaultPrevented).toBe(true);
    expect(wizard.step).toBe(2);
    expect(tab(2).getAttribute('aria-selected')).toBe('true');
    expect(tab(2).getAttribute('tabindex')).toBe('0');
    expect(document.activeElement).toBe(tab(2));

    press(tab(2), 'ArrowLeft');
    expect(wizard.step).toBe(1);
    expect(document.activeElement).toBe(tab(1));

    // Focus reaches a step that refuses to open, so its reason is read; the step stays.
    press(tab(1), 'ArrowLeft');
    expect(document.activeElement).toBe(tab(4));
    expect(wizard.step).toBe(1);
    expect(tab(1).getAttribute('aria-selected')).toBe('true');

    press(tab(4), 'Home');
    expect(document.activeElement).toBe(tab(1));
    press(tab(1), 'End');
    expect(document.activeElement).toBe(tab(4));
    expect(wizard.step).toBe(1);
    press(tab(4), 'ArrowRight');
    expect(document.activeElement).toBe(tab(1));

    const other = press(tab(1), 'Enter');
    expect(other.defaultPrevented).toBe(false);
  });

  it('focuses the step panel on a tab click, Next and Previous', async () => {
    chooseModel();

    tab(2).click();
    fixture.detectChanges();
    expect(document.activeElement?.id).toBe('cc-step-panel-2');
    expect(textOf(el.querySelector('#cc-step-heading-2'))).toBe('Timeline');
    const heading = el.querySelector('#cc-step-heading-2')!;
    expect(heading.classList).toContain('visually-hidden');
    expect(heading.hasAttribute('tabindex')).toBe(false);

    next().click();
    await settle();
    expect(wizard.step).toBe(3);
    expect(document.activeElement?.id).toBe('cc-step-panel-analysis');
    expect(textOf(el.querySelector('#cc-step-heading-analysis'))).toBe('Analyze');

    previous().click();
    await settle();
    expect(wizard.step).toBe(2);
    expect(document.activeElement?.id).toBe('cc-step-panel-2');
  });

  // --- Footer ---

  it('labels Next for each step, Analyze on step 3 and Close on step 4', async () => {
    chooseModel();
    expect(textOf(next())).toBe('Next: Timeline');

    next().click();
    await settle();
    expect(wizard.step).toBe(2);
    expect(textOf(next())).toBe('Next: Analyze');

    next().click();
    await settle();
    expect(wizard.step).toBe(3);
    expect(textOf(next())).toBe('Analyze');
    expect(textOf(el.querySelector('.gh-wizard-position'))).toContain('Step 3 of 4 — Analyze');

    wizard.showResult(ccAnalysisResult());
    await settle();
    expect(wizard.step).toBe(4);
    expect(textOf(next())).toBe('Close');
    expect(next().getAttribute('aria-disabled')).toBe('false');
    expect(previous().disabled).toBe(false);
    expect(textOf(el.querySelector('.gh-wizard-position'))).toContain('Step 4 of 4 — Results');

    previous().click();
    await settle();
    expect(wizard.step).toBe(3);
  });

  it('runs Analyze, abandons it with Stop Analysis, and moves to Results once an analysis is saved', async () => {
    chooseModel();
    const saved: CcAnalysisResult[] = [];
    wizard.analysisSaved.subscribe(result => saved.push(result));

    tab(3).click();
    await settle();
    expect(wizard.step).toBe(3);
    expect(textOf(next())).toBe('Analyze');
    expect(next().getAttribute('aria-disabled')).toBe('false');
    expect(el.querySelector('.cc-wizard-stop')).toBeNull();

    next().click();
    await settle();
    const first = analyzeRequests();
    expect(first.length).toBe(1);
    expect(first[0].request.body.baselineRunIds).toEqual([101, 102, 103]);
    expect(first[0].request.body.comparisonRunIds).toEqual([104, 105, 106]);
    const stop = el.querySelector<HTMLButtonElement>('.cc-wizard-stop')!;
    expect(textOf(stop)).toBe('Stop Analysis');
    expect(next().getAttribute('aria-busy')).toBe('true');
    expect(next().getAttribute('aria-disabled')).toBe('true');
    expect(next().querySelector('.gh-spinner-small')).not.toBeNull();
    expect(el.querySelector('#cc-next-blocked')).toBeNull();

    stop.click();
    await settle();
    expect(first[0].cancelled).toBe(true);
    expect(el.querySelector('.cc-wizard-stop')).toBeNull();
    expect(next().getAttribute('aria-busy')).toBeNull();
    expect(wizard.step).toBe(3);
    expect(saved).toEqual([]);

    next().click();
    await settle();
    const second = analyzeRequests();
    expect(second.length).toBe(1);
    second[0].flush(ccAnalysisResult());
    await settle();

    expect(saved.map(result => result.analysisId)).toEqual([7]);
    expect(wizard.step).toBe(4);
    expect(tab(4).getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement?.id).toBe('cc-step-panel-analysis');
    expect(textOf(el.querySelector('#cc-step-heading-analysis'))).toBe('Results');
  });

  it('stays on the current step when an analysis finishes after the user left Analyze', async () => {
    chooseModel();
    tab(3).click();
    fixture.detectChanges();
    tab(1).click();
    await settle();

    wizard.onAnalysisSaved(ccAnalysisResult());
    await settle();
    expect(wizard.step).toBe(1);
    expect(tab(1).getAttribute('aria-selected')).toBe('true');
  });

  it('names an invalid period as the reason Analyze is blocked', async () => {
    chooseModel();
    tab(3).click();
    await settle();
    expect(next().getAttribute('aria-disabled')).toBe('false');
    expect(el.querySelector('#cc-next-blocked')).toBeNull();

    // The comparison's first run before the baseline's last, pressed on run #101's card.
    el.querySelector<HTMLButtonElement>('article.cc-pu-card[data-unit-id="101"] .cc-pu-bound[data-bound="comparisonFirstId"]')!.click();
    await settle();

    const refusal = wizard.analysis!.periodsError;
    expect(refusal).toContain('The comparison must start after');
    expect(textOf(el.querySelector('#cc-next-blocked'))).toBe(refusal);
    expect(next().getAttribute('aria-disabled')).toBe('true');
    // Results stays unreachable for want of a result, not of the periods.
    expect(textOf(el.querySelector('#cc-step-blocked-4'))).toBe('Analyze first, or open a saved analysis.');
    next().click();
    await settle();
    expect(analyzeRequests().length).toBe(0);
    expect(wizard.step).toBe(3);
  });

  // --- Mounting ---

  it('keeps every visited step mounted and hidden, and creates the analysis component once', async () => {
    chooseModel();
    expect(panel('cc-step-panel-2')).toBeNull();
    expect(el.querySelector('app-cc-analysis-wizard')).toBeNull();

    tab(2).click();
    fixture.detectChanges();
    tab(3).click();
    await settle();
    const analysis = wizard.analysis;
    expect(analysis).toBeDefined();
    expect(analysis!.step).toBe('analyze');
    expect(panel('cc-step-panel-analysis')!.getAttribute('aria-labelledby')).toBe('cc-step-tab-3');
    // Step 3 is a figure workspace, edge to edge; step 4 scrolls as one.
    expect(panel('cc-step-panel-analysis')!.classList).toContain('gh-fig-host');
    wizard.showResult(ccAnalysisResult());
    await settle();
    expect(wizard.analysis).toBe(analysis);
    expect(analysis!.step).toBe('results');
    expect(panel('cc-step-panel-analysis')!.getAttribute('aria-labelledby')).toBe('cc-step-tab-4');
    expect(panel('cc-step-panel-analysis')!.classList).not.toContain('gh-fig-host');

    tab(1).click();
    fixture.detectChanges();
    expect(panel('cc-step-panel-1')!.hidden).toBe(false);
    expect(panel('cc-step-panel-2')!.hidden).toBe(true);
    expect(panel('cc-step-panel-analysis')!.hidden).toBe(true);
    expect(el.querySelectorAll('app-cc-model-step').length).toBe(1);
    expect(el.querySelectorAll('app-cc-timeline-workspace').length).toBe(1);
    expect(el.querySelectorAll('app-cc-analysis-wizard').length).toBe(1);

    tab(3).click();
    await settle();
    expect(wizard.analysis).toBe(analysis);
    expect(el.querySelectorAll('app-cc-analysis-wizard').length).toBe(1);
    expect(panel('cc-step-panel-analysis')!.hidden).toBe(false);
    expect(panel('cc-step-panel-analysis')!.getAttribute('aria-labelledby')).toBe('cc-step-tab-3');
    expect(panel('cc-step-panel-1')!.hidden).toBe(true);
    expect(panel('cc-step-panel-2')!.hidden).toBe(true);
  });

  it('showResult mounts the analysis component with the result and selects Results', async () => {
    chooseModel();
    expect(el.querySelector('app-cc-analysis-wizard')).toBeNull();

    wizard.showResult(ccAnalysisResult());
    await settle();

    expect(el.querySelectorAll('app-cc-analysis-wizard').length).toBe(1);
    expect(wizard.step).toBe(4);
    expect(wizard.analysis!.result?.analysisId).toBe(7);
    expect(wizard.analysis!.step).toBe('results');
    expect(tab(4).getAttribute('aria-selected')).toBe('true');
    expect(tab(4).getAttribute('aria-disabled')).toBe('false');
    // Results is reached without passing the Timeline step.
    expect(panel('cc-step-panel-2')).toBeNull();
    expect(panel('cc-step-panel-analysis')!.getAttribute('aria-labelledby')).toBe('cc-step-tab-4');
    expect(document.activeElement?.id).toBe('cc-step-panel-analysis');
    expect(textOf(el.querySelector('#cc-step-heading-analysis'))).toBe('Results');
  });

  // --- Header ---

  it('offers Reload runs on steps 1 and 2 only, unavailable without a model and while loading', () => {
    let reloads = 0;
    wizard.reload.subscribe(() => reloads++);
    const reload = (): HTMLButtonElement | null => el.querySelector<HTMLButtonElement>('.cc-wizard-reload');

    expect(reload()!.getAttribute('aria-label')).toBe('Reload the runs of the model');
    expect(reload()!.getAttribute('aria-disabled')).toBe('true');
    reload()!.click();
    expect(reloads).toBe(0);

    chooseModel();
    expect(reload()!.getAttribute('aria-disabled')).toBeNull();

    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();
    expect(reload()!.getAttribute('aria-disabled')).toBe('true');
    reload()!.click();
    expect(reloads).toBe(0);

    fixture.componentRef.setInput('loading', false);
    fixture.detectChanges();
    reload()!.click();
    expect(reloads).toBe(1);

    tab(2).click();
    fixture.detectChanges();
    expect(reload()).not.toBeNull();
    tab(3).click();
    fixture.detectChanges();
    expect(reload()).toBeNull();
  });

  it('disables the close button and the footer while the Timeline step exports', () => {
    chooseModel();
    let closes = 0;
    wizard.closeRequested.subscribe(() => closes++);
    expect(closeButton().getAttribute('aria-label')).toBe('Close chat consistency');

    tab(2).click();
    fixture.detectChanges();
    const workspace = wizard.timelineWorkspace!;
    workspace.exporting = true;
    workspace.exportingChange.emit(true);
    fixture.detectChanges();

    expect(wizard.closeBlocked).toBe(true);
    expect(closeButton().disabled).toBe(true);
    expect(next().disabled).toBe(true);
    expect(el.querySelector('.cc-wizard-reload')!.getAttribute('aria-disabled')).toBe('true');
    wizard.onCloseClick();
    expect(closes).toBe(0);

    workspace.exporting = false;
    workspace.exportingChange.emit(false);
    fixture.detectChanges();
    expect(closeButton().disabled).toBe(false);
    expect(next().disabled).toBe(false);
    closeButton().click();
    expect(closes).toBe(1);
  });

  it('disables the close button and the footer Close while the Results step\'s Reports section attaches charts', async () => {
    chooseModel();
    let closes = 0;
    wizard.closeRequested.subscribe(() => closes++);
    wizard.showResult(ccAnalysisResult());
    await settle();
    expect(wizard.step).toBe(4);
    expect(textOf(next())).toBe('Close');

    const reports = wizard.analysis!.reportsStep!;
    expect(reports).toBeDefined();
    reports.chartState = 'attaching';
    wizard.analysis!.stateChange.emit();
    await settle();

    expect(wizard.analysis!.chartsAttaching).toBe(true);
    expect(wizard.closeBlocked).toBe(true);
    expect(closeButton().disabled).toBe(true);
    expect(next().disabled).toBe(true);
    wizard.nextStep();
    wizard.onCloseClick();
    expect(closes).toBe(0);

    reports.chartState = 'idle';
    wizard.analysis!.stateChange.emit();
    await settle();
    expect(closeButton().disabled).toBe(false);
    expect(next().disabled).toBe(false);
    next().click();
    expect(closes).toBe(1);
  });

  it('locks step 1\'s model and dates, naming why, while charts export and while report charts attach', async () => {
    chooseModel();
    const lockReason = (): string | null => {
      const reason = el.querySelector('#cc-step-panel-1 #cc-tl-lock-reason');
      return reason ? textOf(reason) : null;
    };
    expect(wizard.subjectLockedReason).toBe('');
    expect(lockReason()).toBeNull();

    tab(2).click();
    fixture.detectChanges();
    const workspace = wizard.timelineWorkspace!;
    workspace.exporting = true;
    workspace.exportingChange.emit(true);
    fixture.detectChanges();
    expect(lockReason()).toBe('The model and the dates are locked while the charts export.');
    expect(el.querySelector('#cc-step-panel-1 .selector-trigger')!.getAttribute('aria-disabled')).toBe('true');

    workspace.exporting = false;
    workspace.exportingChange.emit(false);
    fixture.detectChanges();
    expect(lockReason()).toBeNull();

    wizard.showResult(ccAnalysisResult());
    await settle();
    const reports = wizard.analysis!.reportsStep!;
    reports.chartState = 'attaching';
    wizard.analysis!.stateChange.emit();
    await settle();
    expect(lockReason()).toBe('The model and the dates are locked while report charts are attached.');

    reports.chartState = 'idle';
    wizard.analysis!.stateChange.emit();
    await settle();
    expect(lockReason()).toBeNull();
    expect(el.querySelector('#cc-step-panel-1 .selector-trigger')!.hasAttribute('aria-disabled')).toBe(false);
  });
});
