import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';

import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import { CC_LAUNCHER_STORAGE_KEY, ChatConsistencyTabComponent } from './chat-consistency-tab.component';
import { endOfUtcDay, startOfUtcDay } from './chat-consistency-format';
import {
  CC_API,
  ccAnalysisResult,
  ccAnalysisSummary,
  ccAxis,
  ccRunRows,
  ccTimeline,
  chatConsistencyTestProviders,
  textOf
} from './chat-consistency-tab.testing';
import { CC_CHART_SIZE_STORAGE_KEY, CC_TIMELINE_STORAGE_KEY } from './timeline-workspace/timeline-workspace.component';

/** Every Chat Consistency storage key, cleared around each test. */
const STORAGE_KEYS = [CC_LAUNCHER_STORAGE_KEY, CC_TIMELINE_STORAGE_KEY, CC_CHART_SIZE_STORAGE_KEY];

const FIGURE_ORDER = ['quality', 'ttfat', 'rate', 'work', 'cost', 'reliability', 'timeline'];

function clearStorage(): void {
  for (const key of STORAGE_KEYS) {
    try {
      localStorage.removeItem(key);
    } catch {
      // Nothing stored.
    }
  }
}

const macrotask = (): Promise<void> => new Promise<void>(resolve => setTimeout(resolve));

describe('ChatConsistencyTabComponent', () => {
  let fixture: ComponentFixture<ChatConsistencyTabComponent>;
  let http: HttpTestingController;
  let bridge: BenchmarkShellBridge;
  let el: HTMLElement;

  beforeEach(async () => {
    clearStorage();
    await TestBed.configureTestingModule({
      imports: [ChatConsistencyTabComponent],
      providers: chatConsistencyTestProviders()
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    bridge = TestBed.inject(BenchmarkShellBridge);
  });

  afterEach(() => {
    if (fixture) {
      // Destroying removes the dialogs' listeners before their asynchronous close events arrive.
      el.querySelectorAll('dialog').forEach(dialog => {
        if (dialog.open) dialog.close();
      });
      fixture.destroy();
    }
    http.verify();
    clearStorage();
  });

  // --- Helpers ---

  /** Creates the tab and answers its model and saved-analysis requests. */
  function createTab(): void {
    fixture = TestBed.createComponent(ChatConsistencyTabComponent);
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    http.expectOne(`${CC_API}/models`).flush([ccAxis(), ccAxis({ key: 'empty', displayName: 'No runs', runCount: 0 })]);
    http.expectOne(`${CC_API}/analyses`).flush([ccAnalysisSummary(7)]);
    fixture.detectChanges();
  }

  const dialog = (): HTMLDialogElement => el.querySelector<HTMLDialogElement>('dialog.cc-wizard-dialog')!;
  const howTo = (): HTMLDetailsElement => el.querySelector<HTMLDetailsElement>('details.cc-launcher-howto')!;

  function openWizard(): void {
    el.querySelector<HTMLButtonElement>('#cc-open-wizard')!.click();
    fixture.detectChanges();
  }

  /** Resolves once the wizard dialog's `close` event has been handled. */
  function closed(): Promise<void> {
    return new Promise<void>(resolve => dialog().addEventListener('close', () => resolve(), { once: true }));
  }

  /** Chooses the model with step 1's picker and answers the timeline and run requests it makes. */
  function chooseModelInWizard(): void {
    const step = dialog().querySelector('#cc-step-panel-1')!;
    step.querySelector<HTMLButtonElement>('.cc-subject-model-selector .selector-trigger')!.click();
    fixture.detectChanges();
    const options = Array.from(step.querySelectorAll<HTMLElement>('.cc-subject-model-selector [role="option"]'));
    // The axis without runs is not offered.
    expect(options.map(option => textOf(option))).toEqual([expect.stringContaining('GPT-5 high')]);
    options[0].click();
    fixture.detectChanges();

    const timeline = http.expectOne(r => r.url === `${CC_API}/timeline`);
    expect(timeline.request.method).toBe('GET');
    expect(timeline.request.params.get('modelKey')).toBe('openai/gpt-5|high');
    const runs = http.expectOne(r => r.url === `${CC_API}/runs`);
    expect(runs.request.params.get('modelKey')).toBe('openai/gpt-5|high');
    timeline.flush(ccTimeline());
    runs.flush(ccRunRows());
    fixture.detectChanges();
  }

  /** The launcher's Current model facts, as `[term, value]` pairs. */
  function currentFacts(): string[][] {
    return Array.from(el.querySelectorAll('.cc-launcher-current dl.bm-launcher-state > div'))
      .map(row => [textOf(row.querySelector('dt')), textOf(row.querySelector('dd'))]);
  }

  // --- Launcher ---

  it('renders the launcher: heading, lead, the wizard button, How chat consistency works and the saved analyses', () => {
    createTab();

    expect(textOf(el.querySelector('h3#cc-tab-heading'))).toBe('Chat Consistency');
    expect(el.querySelector('section.cc-launcher')!.getAttribute('aria-labelledby')).toBe('cc-tab-heading');
    expect(textOf(el.querySelector('.bm-launcher-lead'))).toContain('Whether the Overseer chat with one model stayed the same over time');
    const open = el.querySelector<HTMLButtonElement>('#cc-open-wizard')!;
    expect(textOf(open)).toBe('Open Chat Consistency Wizard');
    expect(open.type).toBe('button');
    expect(open.classList).toContain('btn-gh');
    // No model is chosen yet.
    expect(el.querySelector('.cc-launcher-current')).toBeNull();

    // Open on the first visit, and recorded closed for the next one.
    expect(howTo().open).toBe(true);
    expect(textOf(howTo().querySelector('summary'))).toBe('How chat consistency works');
    expect(Array.from(howTo().querySelectorAll('.bm-launcher-steps strong')).map(step => textOf(step)))
      .toEqual(['Model', 'Timeline', 'Periods', 'Runs and controls', 'Results', 'Reports']);
    expect(textOf(howTo().querySelector('.alert-info')))
      .toContain('A verdict is only as current as the last run someone made: GnollBench is run by hand and is not a monitoring service.');
    expect(JSON.parse(localStorage.getItem(CC_LAUNCHER_STORAGE_KEY)!)).toEqual({ version: 1, howItWorksOpen: false });

    // The saved-analyses component carries the one Saved analyses heading.
    expect(el.querySelectorAll('#cc-saved-title').length).toBe(1);
    expect(Array.from(el.querySelectorAll('h3, h4, h5')).filter(heading => textOf(heading) === 'Saved analyses').length).toBe(1);
    const card = el.querySelector('.cc-analysis-card[data-analysis-id="7"]')!;
    expect(textOf(card.querySelector('.cc-analysis-title'))).toBe('Analysis 7');
    expect(textOf(card)).toContain('GPT-5 high');

    // The wizard exists only once it is opened.
    expect(dialog().open).toBe(false);
    expect(dialog().getAttribute('aria-labelledby')).toBe('cc-wizard-title');
    expect(dialog().querySelector('app-cc-wizard')).toBeNull();
  });

  it('keeps How chat consistency works as the operator left it', async () => {
    createTab();
    await macrotask();

    howTo().open = false;
    await macrotask();
    expect(JSON.parse(localStorage.getItem(CC_LAUNCHER_STORAGE_KEY)!)).toEqual({ version: 1, howItWorksOpen: false });
    howTo().open = true;
    await macrotask();
    expect(JSON.parse(localStorage.getItem(CC_LAUNCHER_STORAGE_KEY)!)).toEqual({ version: 1, howItWorksOpen: true });

    fixture.destroy();
    localStorage.setItem(CC_LAUNCHER_STORAGE_KEY, JSON.stringify({ version: 1, howItWorksOpen: false }));
    createTab();
    expect(howTo().open).toBe(false);
  });

  it('opens How chat consistency works when storage throws', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('Storage is blocked.', 'SecurityError');
    });
    createTab();
    expect(howTo().open).toBe(true);
  });

  // --- The wizard dialog ---

  it('opens the wizard in its full-screen dialog with the wizard heading focused', () => {
    createTab();
    openWizard();

    expect(dialog().open).toBe(true);
    expect(el.querySelector('dialog.cc-wizard-dialog[open]')).not.toBeNull();
    expect(dialog().classList).toContain('gh-dialog-fullscreen');
    expect(dialog().querySelectorAll('app-cc-wizard').length).toBe(1);
    expect(document.activeElement?.id).toBe('cc-wizard-title');
    expect(textOf(dialog().querySelector('.cc-wizard-subtitle'))).toBe('Choose a model, then follow the steps');
  });

  it('loads the timeline and the runs when a model is chosen in the wizard, and names it on the launcher', () => {
    createTab();
    openWizard();
    chooseModelInWizard();

    const step = dialog().querySelector('#cc-step-panel-1')!;
    expect(textOf(step.querySelector('.cc-tl-status'))).toBe('6 runs of GPT-5 high in this range.');
    expect(step.querySelectorAll('.cc-run-table tbody tr').length).toBe(6);
    // Newest first by default.
    expect(step.querySelector('.cc-run-table tbody tr')!.getAttribute('data-run-id')).toBe('106');
    expect(textOf(dialog().querySelector('.cc-wizard-subtitle'))).toBe('GPT-5 high · 6 runs · every date');

    expect(textOf(el.querySelector('.cc-launcher-current h4'))).toBe('Current model');
    expect(currentFacts()).toEqual([
      ['Model', 'GPT-5 high'],
      ['Runs', '6 runs, 4 with call telemetry'],
      ['Dates', 'Every date'],
      ['Latest analysis', '#7 · saved 2026-10-02']
    ]);

    // The Timeline step draws the loaded timeline without a request of its own.
    dialog().querySelector<HTMLButtonElement>('#cc-step-tab-2')!.click();
    fixture.detectChanges();
    const figures = Array.from(dialog().querySelectorAll('app-cc-timeline-workspace figure.cc-figure'));
    expect(figures.map(entry => entry.getAttribute('data-figure'))).toEqual(FIGURE_ORDER);
    expect(textOf(figures[0].querySelector('figcaption'))).toContain('Quality held between 71 and 74 across 6 runs.');
  });

  it('reads the range again as UTC day bounds', () => {
    createTab();
    openWizard();
    chooseModelInWizard();

    const from = dialog().querySelector<HTMLInputElement>('#cc-tl-from')!;
    from.value = '2026-09-10';
    from.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    let timeline = http.expectOne(r => r.url === `${CC_API}/timeline`);
    expect(timeline.request.params.get('from')).toBe('2026-09-10T00:00:00.000Z');
    expect(timeline.request.params.get('from')).toBe(startOfUtcDay('2026-09-10'));
    expect(timeline.request.params.has('to')).toBe(false);
    let runs = http.expectOne(r => r.url === `${CC_API}/runs`);
    expect(runs.request.params.get('from')).toBe('2026-09-10T00:00:00.000Z');
    timeline.flush(ccTimeline());
    runs.flush(ccRunRows());
    fixture.detectChanges();
    expect(currentFacts()[2]).toEqual(['Dates', 'From 2026-09-10']);
    expect(currentFacts()[1]).toEqual(['Runs', '6 runs, 4 with call telemetry · 6 in the chosen dates']);

    const to = dialog().querySelector<HTMLInputElement>('#cc-tl-to')!;
    to.value = '2026-09-30';
    to.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    timeline = http.expectOne(r => r.url === `${CC_API}/timeline`);
    expect(timeline.request.params.get('from')).toBe('2026-09-10T00:00:00.000Z');
    expect(timeline.request.params.get('to')).toBe(endOfUtcDay('2026-09-30'));
    runs = http.expectOne(r => r.url === `${CC_API}/runs`);
    expect(runs.request.params.get('to')).toBe(endOfUtcDay('2026-09-30'));
    timeline.flush(ccTimeline());
    runs.flush(ccRunRows());
    fixture.detectChanges();
    expect(currentFacts()[2]).toEqual(['Dates', '2026-09-10 to 2026-09-30']);
    expect(textOf(dialog().querySelector('.cc-wizard-subtitle'))).toBe('GPT-5 high · 6 runs · 2026-09-10 to 2026-09-30');
  });

  it('opens a saved analysis in the wizard on Results, switching to its model', () => {
    createTab();
    const open = el.querySelector<HTMLButtonElement>('.cc-analysis-card[data-analysis-id="7"] .cc-analysis-open')!;
    expect(open.getAttribute('aria-label')).toBe('Open Analysis 7');
    open.click();
    fixture.detectChanges();

    http.expectOne(`${CC_API}/analyses/7`).flush(ccAnalysisResult());
    fixture.detectChanges();
    const timeline = http.expectOne(r => r.url === `${CC_API}/timeline`);
    expect(timeline.request.params.get('modelKey')).toBe('openai/gpt-5|high');
    const runs = http.expectOne(r => r.url === `${CC_API}/runs`);

    expect(dialog().open).toBe(true);
    expect(dialog().querySelector('#cc-step-tab-5')!.getAttribute('aria-selected')).toBe('true');
    expect(dialog().querySelectorAll('app-cc-analysis-wizard').length).toBe(1);
    expect(textOf(dialog().querySelector('#cc-step-heading-analysis'))).toBe('Results');

    timeline.flush(ccTimeline());
    runs.flush(ccRunRows());
    fixture.detectChanges();
    const wizard = fixture.componentInstance.wizard!;
    expect(wizard.step).toBe(5);
    expect(wizard.analysis!.result?.analysisId).toBe(7);
    expect(currentFacts()[0]).toEqual(['Model', 'GPT-5 high']);
  });

  it('ignores a close event that is not the wizard dialog\'s, and reads the saved analyses again after a real close', async () => {
    createTab();
    openWizard();

    dialog().querySelector('app-cc-wizard')!.dispatchEvent(new Event('close', { bubbles: true }));
    fixture.detectChanges();
    http.expectNone(`${CC_API}/analyses`);
    expect(dialog().open).toBe(true);

    const done = closed();
    dialog().querySelector<HTMLButtonElement>('.cc-wizard-close')!.click();
    expect(dialog().open).toBe(false);
    await done;
    fixture.detectChanges();
    http.expectOne(`${CC_API}/analyses`).flush([ccAnalysisSummary(7), ccAnalysisSummary(8)]);
    fixture.detectChanges();
    expect(el.querySelectorAll('.cc-analysis-card').length).toBe(2);

    // Reopening keeps the mounted wizard.
    const wizard = fixture.componentInstance.wizard;
    openWizard();
    expect(dialog().open).toBe(true);
    expect(fixture.componentInstance.wizard).toBe(wizard);
  });

  it('refuses Escape while the wizard blocks closing, and allows it otherwise', () => {
    createTab();
    openWizard();
    const wizard = fixture.componentInstance.wizard!;

    const allowed = new Event('cancel', { cancelable: true });
    dialog().dispatchEvent(allowed);
    expect(allowed.defaultPrevented).toBe(false);

    // As while a chart export runs or report charts are attached.
    Object.defineProperty(wizard, 'closeBlocked', { configurable: true, get: () => true });
    const refused = new Event('cancel', { cancelable: true });
    dialog().dispatchEvent(refused);
    expect(refused.defaultPrevented).toBe(true);

    // A cancel from inside the dialog is not the wizard dialog's own.
    const nested = new Event('cancel', { cancelable: true, bubbles: true });
    dialog().querySelector('app-cc-wizard')!.dispatchEvent(nested);
    expect(nested.defaultPrevented).toBe(false);

    delete (wizard as unknown as { closeBlocked?: boolean }).closeBlocked;
    const again = new Event('cancel', { cancelable: true });
    dialog().dispatchEvent(again);
    expect(again.defaultPrevented).toBe(false);
    expect(dialog().open).toBe(true);
  });

  // --- Run actions ---

  it('closes the wizard before the shell repeats a run\'s setup, and opens a run report above it', async () => {
    createTab();
    openWizard();
    chooseModelInWizard();
    const openAtCall: boolean[] = [];
    const repeat = vi.spyOn(bridge, 'repeatRunSetup').mockImplementation(() => {
      openAtCall.push(dialog().open);
    });
    const report = vi.spyOn(bridge, 'viewRunDetail').mockImplementation(() => undefined);
    const row = (runId: number): HTMLTableRowElement =>
      dialog().querySelector<HTMLTableRowElement>(`.cc-run-table tr[data-run-id="${runId}"]`)!;

    row(104).querySelector<HTMLButtonElement>('.cc-open-report-btn')!.click();
    expect(report).toHaveBeenCalledWith(104);
    expect(dialog().open).toBe(true);

    const done = closed();
    row(105).querySelector<HTMLButtonElement>('.cc-repeat-btn')!.click();
    expect(repeat).toHaveBeenCalledWith(105);
    expect(openAtCall).toEqual([false]);
    await done;
    fixture.detectChanges();
    http.expectOne(`${CC_API}/analyses`).flush([ccAnalysisSummary(7)]);
  });

  it('refuses to repeat a run\'s setup while the wizard blocks closing, and says why', () => {
    createTab();
    openWizard();
    chooseModelInWizard();
    const wizard = fixture.componentInstance.wizard!;
    const repeat = vi.spyOn(bridge, 'repeatRunSetup').mockImplementation(() => undefined);
    Object.defineProperty(wizard, 'closeBlocked', { configurable: true, get: () => true });

    dialog().querySelector<HTMLButtonElement>('.cc-run-table tr[data-run-id="105"] .cc-repeat-btn')!.click();
    fixture.detectChanges();
    expect(repeat).not.toHaveBeenCalled();
    expect(dialog().open).toBe(true);
    expect(dialog().querySelector('.cc-tl-announcement')!.textContent).toContain('Wait for the chart export');
    delete (wizard as unknown as { closeBlocked?: boolean }).closeBlocked;
  });

  // --- Saved analyses ---

  it('shows the 409 refusal in the delete confirmation and keeps the analysis', () => {
    createTab();
    const card = el.querySelector<HTMLElement>('.cc-analysis-card[data-analysis-id="7"]')!;
    card.querySelector<HTMLButtonElement>('.cc-analysis-delete')!.click();
    fixture.detectChanges();
    const confirm = el.querySelector<HTMLDialogElement>('dialog.cc-delete-dialog')!;
    expect(confirm.open).toBe(true);
    expect(textOf(confirm.querySelector('h3'))).toBe('Delete Analysis 7?');

    confirm.querySelector<HTMLButtonElement>('.cc-delete-confirm')!.click();
    const del = http.expectOne(`${CC_API}/analyses/7`);
    expect(del.request.method).toBe('DELETE');
    del.flush({ error: 'Report documents were written from this analysis. Delete them first.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(confirm.open).toBe(true);
    const error = confirm.querySelector('.cc-delete-error')!;
    expect(error.getAttribute('role')).toBe('alert');
    expect(textOf(error)).toBe('Report documents were written from this analysis. Delete them first.');
    expect(el.querySelector('.cc-analysis-card[data-analysis-id="7"]')).not.toBeNull();
  });

  it('removes a deleted analysis from the list', () => {
    createTab();
    el.querySelector<HTMLButtonElement>('.cc-analysis-card[data-analysis-id="7"] .cc-analysis-delete')!.click();
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('dialog.cc-delete-dialog .cc-delete-confirm')!.click();
    http.expectOne(`${CC_API}/analyses/7`).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(el.querySelector<HTMLDialogElement>('dialog.cc-delete-dialog')!.open).toBe(false);
    expect(el.querySelector('.cc-analysis-card')).toBeNull();
    expect(textOf(el.querySelector('.cc-saved-empty'))).toContain('No analysis is saved yet.');
  });

  // --- Teardown ---

  it('closes the wizard dialog when the tab is destroyed', () => {
    createTab();
    openWizard();
    const wizardDialog = dialog();
    expect(wizardDialog.open).toBe(true);

    fixture.destroy();
    expect(wizardDialog.open).toBe(false);
  });
});
