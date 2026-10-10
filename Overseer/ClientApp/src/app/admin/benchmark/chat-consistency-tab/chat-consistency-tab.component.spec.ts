import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';

import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import { CC_HISTORY_VIEW_STORAGE_KEY } from './analysis-history/analysis-history-dialog.component';
import {
  CC_LAUNCHER_STORAGE_KEY,
  CC_LEAVE_REFUSAL,
  CC_SET_CHANGE_NOTE,
  CC_SUBJECT_STORAGE_KEY,
  ChatConsistencyTabComponent
} from './chat-consistency-tab.component';
import { endOfUtcDay, startOfUtcDay } from './chat-consistency-format';
import { CC_EMPTY_BATTERY_SCOPE } from './chat-consistency-scope';
import { CcAnalysisSummary, CcBatteryRunRow, CcComparisonSets, CcRunRow } from './chat-consistency.models';
import {
  CC_API,
  CC_BATTERY_SET_KEY,
  ccAnalysisResult,
  ccAnalysisSummary,
  ccAxis,
  ccBatteryMemberRows,
  ccBatteryRunRows,
  ccComparisonSets,
  ccFlushFreshness,
  ccNoComparisonSets,
  ccRunRows,
  ccTimeline,
  chatConsistencyTestProviders,
  textOf
} from './chat-consistency-tab.testing';
import { CC_BATTERY_RUNS_VIEW_STORAGE_KEY, CC_RUNS_VIEW_STORAGE_KEY } from './model-step/model-step.component';
import { CC_CHART_SIZE_STORAGE_KEY, CC_TIMELINE_STORAGE_KEY } from './timeline-workspace/timeline-workspace.component';

/** Every Chat Consistency storage key, cleared around each test. */
const STORAGE_KEYS = [
  CC_LAUNCHER_STORAGE_KEY, CC_SUBJECT_STORAGE_KEY, CC_TIMELINE_STORAGE_KEY, CC_CHART_SIZE_STORAGE_KEY,
  CC_RUNS_VIEW_STORAGE_KEY, CC_BATTERY_RUNS_VIEW_STORAGE_KEY, CC_HISTORY_VIEW_STORAGE_KEY
];

const FIGURE_ORDER = ['quality', 'ttfat', 'rate', 'work', 'tools', 'cost', 'reliability', 'timeline'];

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

/** What a subject load answers with, where a test differs from the run-by-run defaults. */
interface SubjectData {
  rows?: CcRunRow[];
  sets?: CcComparisonSets;
  batteryRows?: CcBatteryRunRow[];
}

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
    // Results and Write ask for a saved analysis's freshness; the specs here do not test the notice.
    ccFlushFreshness(http);
    http.verify();
    clearStorage();
  });

  // --- Helpers ---

  /**
   * Creates the tab and answers its model and saved-analysis requests, and the Latest analysis card's
   * request for the newest analysis's result.
   */
  function createTab(analyses: CcAnalysisSummary[] = [ccAnalysisSummary(7)]): void {
    fixture = TestBed.createComponent(ChatConsistencyTabComponent);
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    http.expectOne(`${CC_API}/models`).flush([ccAxis(), ccAxis({ key: 'empty', displayName: 'No runs', runCount: 0 })]);
    http.expectOne(`${CC_API}/analyses`).flush(analyses);
    if (analyses.length > 0) {
      const latest = http.expectOne(`${CC_API}/analyses/${analyses[0].id}`);
      expect(latest.request.method).toBe('GET');
      latest.flush(ccAnalysisResult({ analysisId: analyses[0].id }));
    }
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

  /**
   * Chooses the model with step 1's picker and answers the timeline, run, comparison-set and battery-run
   * requests it makes. By default no set is offered, so the runs are listed one by one, as before sets.
   */
  function chooseModelInWizard(subject: SubjectData = {}): void {
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
    const sets = http.expectOne(r => r.url === `${CC_API}/comparison-sets`);
    expect(sets.request.params.get('modelKey')).toBe('openai/gpt-5|high');
    const batteryRuns = http.expectOne(r => r.url === `${CC_API}/battery-runs`);
    expect(batteryRuns.request.params.get('modelKey')).toBe('openai/gpt-5|high');
    timeline.flush(ccTimeline());
    runs.flush(subject.rows ?? ccRunRows());
    sets.flush(subject.sets ?? ccNoComparisonSets());
    batteryRuns.flush(subject.batteryRows ?? []);
    fixture.detectChanges();
  }

  /** The battery fixtures: six standalone runs, two battery runs of two members each, and three sets. */
  function batterySubject(): SubjectData {
    return { rows: [...ccBatteryMemberRows(), ...ccRunRows()], sets: ccComparisonSets(), batteryRows: ccBatteryRunRows() };
  }

  /** Answers the four requests of a subject load, outstanding after a model choice or a date change. */
  function flushSubject(subject: SubjectData = {}): void {
    http.expectOne(r => r.url === `${CC_API}/timeline`).flush(ccTimeline());
    http.expectOne(r => r.url === `${CC_API}/runs`).flush(subject.rows ?? ccRunRows());
    http.expectOne(r => r.url === `${CC_API}/comparison-sets`).flush(subject.sets ?? ccNoComparisonSets());
    http.expectOne(r => r.url === `${CC_API}/battery-runs`).flush(subject.batteryRows ?? []);
    fixture.detectChanges();
  }

  const latestCard = (): HTMLElement => el.querySelector<HTMLElement>('app-cc-latest-analysis-card')!;
  const historyDialog = (): HTMLDialogElement => el.querySelector<HTMLDialogElement>('dialog.cc-history-dialog')!;
  const historyCard = (id: number): HTMLElement =>
    historyDialog().querySelector<HTMLElement>(`.cc-hist-card[data-analysis-id="${id}"]`)!;

  function openHistory(): void {
    el.querySelector<HTMLButtonElement>('#cc-open-history')!.click();
    fixture.detectChanges();
  }

  /** The stored subject record, parsed, or null. */
  function storedSubject(): unknown {
    const stored = localStorage.getItem(CC_SUBJECT_STORAGE_KEY);
    return stored === null ? null : JSON.parse(stored);
  }

  // --- Launcher ---

  it('renders the launcher: New analysis, Latest analysis and Analysis history, in reading order', () => {
    createTab();

    expect(textOf(el.querySelector('h3#cc-tab-heading'))).toBe('Chat Consistency');
    const launcher = el.querySelector('section.cc-launcher')!;
    expect(launcher.getAttribute('aria-labelledby')).toBe('cc-tab-heading');
    const sections = Array.from(launcher.querySelector('.cc-launcher-grid')!.children);
    expect(sections.map(section => section.classList.contains('cc-launcher-new') ? 'new' : section.tagName.toLowerCase()))
      .toEqual(['new', 'app-cc-latest-analysis-card', 'section']);
    expect(sections[2].classList).toContain('cc-history-bar');

    expect(textOf(el.querySelector('.bm-launcher-lead'))).toContain('Whether the Overseer chat with one model stayed the same over time');
    const open = el.querySelector<HTMLButtonElement>('#cc-open-wizard')!;
    expect(textOf(open)).toBe('Open Chat Consistency Wizard');
    expect(open.type).toBe('button');
    expect(open.classList).toContain('btn-gh');

    // Open on the first visit, and recorded closed for the next one.
    expect(howTo().open).toBe(true);
    expect(textOf(howTo().querySelector('summary'))).toBe('How chat consistency works');
    expect(Array.from(howTo().querySelectorAll('.bm-launcher-steps strong')).map(step => textOf(step)))
      .toEqual(['Model', 'Charts', 'Analyze', 'Results', 'Write', 'Documents']);
    expect(textOf(howTo().querySelector('.alert-info')))
      .toContain('A verdict is only as current as the last run someone made: GnollBench is run by hand and is not a monitoring service.');
    expect(JSON.parse(localStorage.getItem(CC_LAUNCHER_STORAGE_KEY)!)).toEqual({ version: 1, howItWorksOpen: false });

    // The newest analysis of any model, with its verdicts from the fetched result and its own gold button.
    expect(textOf(latestCard().querySelector('#cc-latest-title'))).toBe('#7 · Analysis 7');
    expect(textOf(latestCard().querySelector('.cc-latest-outcome-title'))).toBe('The chat changed');
    expect(latestCard().querySelectorAll('.cc-ep-chip').length).toBe(5);
    expect(textOf(latestCard().querySelector('#cc-latest-open'))).toBe('Open Analysis #7');
    expect(Array.from(launcher.querySelectorAll('.btn-gh')).map(button => button.id)).toEqual(['cc-open-wizard', 'cc-latest-open']);

    const bar = el.querySelector('section.cc-history-bar')!;
    expect(bar.getAttribute('aria-labelledby')).toBe('cc-history-bar-title');
    expect(textOf(bar.querySelector('h4#cc-history-bar-title'))).toBe('Analysis history');
    expect(bar.querySelector('#cc-history-bar-summary')!.getAttribute('role')).toBe('status');
    expect(textOf(bar.querySelector('#cc-history-bar-summary')))
      .toBe('1 saved analysis of 1 model · the latest saved 2026-10-02 09:00 UTC · no report documents');
    const history = el.querySelector<HTMLButtonElement>('#cc-open-history')!;
    expect(textOf(history)).toBe('Open Analysis History');
    expect(history.classList).toContain('btn-ghost');
    expect(history.getAttribute('aria-haspopup')).toBe('dialog');
    expect(history.hasAttribute('aria-disabled')).toBe(false);
    expect(historyDialog().open).toBe(false);

    // The wizard exists only once it is opened.
    expect(dialog().open).toBe(false);
    expect(dialog().getAttribute('aria-labelledby')).toBe('cc-wizard-title');
    expect(dialog().querySelector('app-cc-wizard')).toBeNull();
  });

  it('says no analysis is saved yet, and refuses Analysis History with that reason', () => {
    createTab([]);

    expect(textOf(latestCard().querySelector('.cc-latest-empty')))
      .toBe('No analysis is saved yet. Open the wizard, choose a model, and Analyze in step 3.');
    expect(latestCard().querySelector('button')).toBeNull();
    expect(textOf(el.querySelector('#cc-history-bar-summary'))).toBe('No analysis is saved yet.');
    const history = el.querySelector<HTMLButtonElement>('#cc-open-history')!;
    expect(history.getAttribute('aria-disabled')).toBe('true');
    expect(history.getAttribute('aria-describedby')).toBe('cc-history-bar-summary');
    history.click();
    fixture.detectChanges();
    expect(historyDialog().open).toBe(false);
  });

  it('counts the saved analyses, their models and their report documents in the history bar', () => {
    createTab([
      ccAnalysisSummary(9, { createdAtUtc: '2026-10-10T09:40:00Z', subjectModelKey: 'anthropic/claude', reportDocumentCount: 3 }),
      ccAnalysisSummary(8, { reportDocumentCount: 2 }),
      ccAnalysisSummary(7)
    ]);
    expect(textOf(el.querySelector('#cc-history-bar-summary')))
      .toBe('3 saved analyses of 2 models · the latest saved 2026-10-10 09:40 UTC · 5 report documents');
    expect(textOf(latestCard().querySelector('#cc-latest-title'))).toBe('#9 · Analysis 9');
  });

  it('says when the verdicts of the latest analysis could not be loaded', () => {
    fixture = TestBed.createComponent(ChatConsistencyTabComponent);
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    http.expectOne(`${CC_API}/models`).flush([ccAxis()]);
    http.expectOne(`${CC_API}/analyses`).flush([ccAnalysisSummary(7)]);
    fixture.detectChanges();
    expect(textOf(latestCard().querySelector('.cc-latest-status'))).toBe('Loading the verdicts…');

    http.expectOne(`${CC_API}/analyses/7`).flush({ error: 'Boom.' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(textOf(latestCard().querySelector('.cc-latest-error'))).toBe('The verdicts of analysis #7 could not be loaded.');
    expect(textOf(latestCard().querySelector('#cc-latest-open'))).toBe('Open Analysis #7');
  });

  it('takes a newly saved analysis as the latest without fetching it again', () => {
    createTab();
    const tab = fixture.componentInstance;
    tab.onAnalysisSaved(ccAnalysisResult({ analysisId: 9, name: 'October check' }));
    http.expectOne(`${CC_API}/analyses`).flush([ccAnalysisSummary(9, { name: 'October check' }), ccAnalysisSummary(7)]);
    fixture.detectChanges();
    http.expectNone(`${CC_API}/analyses/9`);
    expect(tab.latestResult?.analysisId).toBe(9);
    expect(textOf(latestCard().querySelector('#cc-latest-title'))).toBe('#9 · October check');
    expect(textOf(latestCard().querySelector('.cc-latest-outcome-title'))).toBe('The chat changed');
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

  it('loads the timeline and the runs when a model is chosen in the wizard, and remembers the model', () => {
    createTab();
    openWizard();
    chooseModelInWizard();

    const step = dialog().querySelector('#cc-step-panel-1')!;
    expect(textOf(step.querySelector('.cc-tl-status'))).toBe('6 runs of GPT-5 high in these dates.');
    expect(step.querySelectorAll('.cc-run-card').length).toBe(6);
    // Newest first by default.
    expect(step.querySelector('.cc-run-card')!.getAttribute('data-run-id')).toBe('106');
    expect(textOf(dialog().querySelector('.cc-wizard-subtitle'))).toBe('GPT-5 high · 6 runs · All dates');

    // The launcher shows the newest analysis of any model, whichever model the wizard has chosen.
    expect(textOf(latestCard().querySelector('#cc-latest-title'))).toBe('#7 · Analysis 7');
    expect(el.querySelector('.cc-current-card')).toBeNull();
    expect(storedSubject()).toEqual({
      version: 2, modelKey: 'openai/gpt-5|high', range: { preset: 'all', fromDay: '', toDay: '', anchorUtc: null }, compare: null
    });

    // The Charts step draws the loaded timeline without a request of its own.
    dialog().querySelector<HTMLButtonElement>('#cc-step-tab-2')!.click();
    fixture.detectChanges();
    const figures = Array.from(dialog().querySelectorAll('app-cc-timeline-workspace figure.cc-figure'));
    expect(figures.map(entry => entry.getAttribute('data-figure'))).toEqual(FIGURE_ORDER);
    expect(textOf(figures[0].querySelector('.cc-figure-takeaway'))).toBe('The Intelligence Index ranged from 71 to 74 across 6 runs.');
  });

  /** Chooses a preset in step 1's Dates select. */
  function choosePreset(value: string): void {
    const select = dialog().querySelector<HTMLSelectElement>('#cc-tl-range')!;
    select.value = value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  /**
   * Answers the timeline, run, comparison-set and battery-run requests of the subject, which all carry
   * the same bounds, returning their `from` and `to` parameters.
   */
  function answerSubject(rows = ccRunRows(), subject: SubjectData = {}): { from: string | null; to: string | null } {
    const timeline = http.expectOne(r => r.url === `${CC_API}/timeline`);
    const runs = http.expectOne(r => r.url === `${CC_API}/runs`);
    const sets = http.expectOne(r => r.url === `${CC_API}/comparison-sets`);
    const batteryRuns = http.expectOne(r => r.url === `${CC_API}/battery-runs`);
    const bounds = { from: timeline.request.params.get('from'), to: timeline.request.params.get('to') };
    for (const request of [runs, sets, batteryRuns]) {
      expect(request.request.params.get('from')).toBe(bounds.from);
      expect(request.request.params.get('to')).toBe(bounds.to);
    }
    timeline.flush(ccTimeline());
    runs.flush(rows);
    sets.flush(subject.sets ?? ccNoComparisonSets());
    batteryRuns.flush(subject.batteryRows ?? []);
    fixture.detectChanges();
    return bounds;
  }

  describe('rolling date presets', () => {
    afterEach(() => vi.useRealTimers());

    it('reads the runs since the preset\'s window, and Reload runs moves the window to now', () => {
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(new Date('2026-10-07T12:00:00Z'));
      createTab();
      openWizard();
      chooseModelInWizard();

      choosePreset('7d');
      expect(answerSubject()).toEqual({ from: '2026-09-30T12:00:00.000Z', to: null });
      expect(textOf(dialog().querySelector('.cc-wizard-subtitle'))).toBe('GPT-5 high · 6 runs · Last 7 days');

      vi.setSystemTime(new Date('2026-10-07T15:30:00Z'));
      dialog().querySelector<HTMLButtonElement>('.cc-wizard-reload')!.click();
      fixture.detectChanges();
      expect(answerSubject()).toEqual({ from: '2026-09-30T15:30:00.000Z', to: null });

      choosePreset('1y');
      expect(answerSubject()).toEqual({ from: '2025-10-07T15:30:00.000Z', to: null });

      choosePreset('all');
      expect(answerSubject()).toEqual({ from: null, to: null });
    });

    it('remembers the model and its dates on a model choice, a date change and Reload runs', () => {
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(new Date('2026-10-07T12:00:00Z'));
      createTab();
      expect(storedSubject()).toBeNull();
      openWizard();
      chooseModelInWizard();
      expect(storedSubject()).toEqual({
        version: 2, modelKey: 'openai/gpt-5|high', range: { preset: 'all', fromDay: '', toDay: '', anchorUtc: null }, compare: null
      });

      choosePreset('7d');
      answerSubject();
      expect(storedSubject()).toEqual({
        version: 2, modelKey: 'openai/gpt-5|high', range: { preset: '7d', fromDay: '', toDay: '', anchorUtc: '2026-10-07T12:00:00.000Z' },
        compare: null
      });

      vi.setSystemTime(new Date('2026-10-07T15:30:00Z'));
      dialog().querySelector<HTMLButtonElement>('.cc-wizard-reload')!.click();
      fixture.detectChanges();
      answerSubject();
      expect(storedSubject()).toEqual({
        version: 2, modelKey: 'openai/gpt-5|high', range: { preset: '7d', fromDay: '', toDay: '', anchorUtc: '2026-10-07T15:30:00.000Z' },
        compare: null
      });
    });

    it('restores a stored model with its dates, a rolling preset moved to now, and loads its runs', () => {
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(new Date('2026-10-07T12:00:00Z'));
      localStorage.setItem(CC_SUBJECT_STORAGE_KEY, JSON.stringify({
        version: 1, modelKey: 'openai/gpt-5|high', range: { preset: '7d', fromDay: '', toDay: '', anchorUtc: '2026-09-01T00:00:00.000Z' }
      }));
      createTab();

      const tab = fixture.componentInstance;
      expect(tab.selectedKey).toBe('openai/gpt-5|high');
      expect(tab.range).toEqual({ preset: '7d', fromDay: '', toDay: '', anchorUtc: '2026-10-07T12:00:00.000Z' });
      expect(answerSubject()).toEqual({ from: '2026-09-30T12:00:00.000Z', to: null });
      expect(tab.rows.length).toBe(6);
      expect(tab.timeline).not.toBeNull();
    });
  });

  describe('a stored subject that is not restored', () => {
    const range = { preset: 'all', fromDay: '', toDay: '', anchorUtc: null };

    it('forgets a stored model that is not among the models with runs', () => {
      for (const modelKey of ['gone/model', 'empty']) {
        localStorage.setItem(CC_SUBJECT_STORAGE_KEY, JSON.stringify({ version: 1, modelKey, range }));
        createTab();
        expect(fixture.componentInstance.selectedKey).toBeNull();
        expect(localStorage.getItem(CC_SUBJECT_STORAGE_KEY)).toBeNull();
        http.expectNone(r => r.url === `${CC_API}/timeline`);
        fixture.destroy();
      }
    });

    it('ignores a record of another version, and keeps it', () => {
      const record = JSON.stringify({ version: 3, modelKey: 'openai/gpt-5|high', range, compare: null });
      localStorage.setItem(CC_SUBJECT_STORAGE_KEY, record);
      createTab();
      expect(fixture.componentInstance.selectedKey).toBeNull();
      expect(localStorage.getItem(CC_SUBJECT_STORAGE_KEY)).toBe(record);
      http.expectNone(r => r.url === `${CC_API}/timeline`);
    });

    it('ignores a malformed record', () => {
      const records = [
        '{not json',
        'null',
        JSON.stringify({ version: 1, modelKey: 'openai/gpt-5|high' }),
        JSON.stringify({ version: 1, modelKey: 'openai/gpt-5|high', range: { ...range, preset: 'weekly' } }),
        JSON.stringify({ version: 1, modelKey: 'openai/gpt-5|high', range: { ...range, anchorUtc: 5 } }),
        JSON.stringify({ version: 1, modelKey: 42, range })
      ];
      for (const record of records) {
        localStorage.setItem(CC_SUBJECT_STORAGE_KEY, record);
        createTab();
        expect(fixture.componentInstance.selectedKey).toBeNull();
        fixture.destroy();
      }
    });
  });

  it('counts the runs in the analysis in the wizard subtitle, and clears the selection for a new model or a saved analysis', () => {
    createTab();
    openWizard();
    chooseModelInWizard();
    const tab = fixture.componentInstance;

    dialog().querySelector<HTMLInputElement>('#cc-run-104-include')!.click();
    fixture.detectChanges();
    expect([...tab.scope.leftOut]).toEqual([104]);
    expect(textOf(dialog().querySelector('.cc-wizard-subtitle'))).toBe('GPT-5 high · 6 runs · All dates · 5 in the analysis');

    // A reload that no longer lists the left-out run drops it, and says so.
    dialog().querySelector<HTMLButtonElement>('.cc-wizard-reload')!.click();
    fixture.detectChanges();
    answerSubject(ccRunRows().filter(row => row.runId !== 104));
    expect(tab.scope.leftOut.size).toBe(0);
    expect(textOf(dialog().querySelector('.cc-tl-announcement'))).toBe('Left-out run #104 is no longer listed and was dropped from the selection.');
    // Step 1 no longer narrows the runs, so the subtitle leaves the count out.
    expect(textOf(dialog().querySelector('.cc-wizard-subtitle'))).not.toContain('in the analysis');

    dialog().querySelector<HTMLInputElement>('#cc-run-105-include')!.click();
    fixture.detectChanges();
    expect(tab.scope.leftOut.size).toBe(1);
    el.querySelector<HTMLButtonElement>('#cc-latest-open')!.click();
    fixture.detectChanges();
    http.expectOne(`${CC_API}/analyses/7`).flush(ccAnalysisResult());
    fixture.detectChanges();
    expect(tab.scope.leftOut.size).toBe(0);
    expect(tab.announcement).toBe('The run selection in step 1 was cleared to show the saved analysis.');

    tab.setScope({ firstRunId: 102, lastRunId: null, leftOut: new Set<number>() });
    tab.selectModel('anthropic/claude');
    expect(tab.scope.firstRunId).toBeNull();
    flushSubject({ rows: [] });
  });

  it('reads custom dates as UTC day bounds', () => {
    createTab();
    openWizard();
    chooseModelInWizard();

    choosePreset('custom');
    expect(fixture.componentInstance.range).toEqual({ preset: 'custom', fromDay: '', toDay: '', anchorUtc: null });
    // An empty custom range reads every date again.
    answerSubject();

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
    http.expectOne(r => r.url === `${CC_API}/comparison-sets`).flush(ccNoComparisonSets());
    http.expectOne(r => r.url === `${CC_API}/battery-runs`).flush([]);
    fixture.detectChanges();
    expect(fixture.componentInstance.range).toEqual({ preset: 'custom', fromDay: '2026-09-10', toDay: '', anchorUtc: null });

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
    http.expectOne(r => r.url === `${CC_API}/comparison-sets`).flush(ccNoComparisonSets());
    http.expectOne(r => r.url === `${CC_API}/battery-runs`).flush([]);
    fixture.detectChanges();
    expect(textOf(dialog().querySelector('.cc-wizard-subtitle'))).toBe('GPT-5 high · 6 runs · 2026-09-10 to 2026-09-30');
  });

  it('opens the latest analysis in the wizard on Results, switching to its model', () => {
    createTab();
    const open = el.querySelector<HTMLButtonElement>('#cc-latest-open')!;
    expect(textOf(open)).toBe('Open Analysis #7');
    open.click();
    fixture.detectChanges();
    expect(open.getAttribute('aria-busy')).toBe('true');

    http.expectOne(`${CC_API}/analyses/7`).flush(ccAnalysisResult());
    fixture.detectChanges();
    const timeline = http.expectOne(r => r.url === `${CC_API}/timeline`);
    expect(timeline.request.params.get('modelKey')).toBe('openai/gpt-5|high');
    const runs = http.expectOne(r => r.url === `${CC_API}/runs`);

    expect(dialog().open).toBe(true);
    expect(dialog().querySelector('#cc-step-tab-4')!.getAttribute('aria-selected')).toBe('true');
    expect(dialog().querySelectorAll('app-cc-analysis-wizard').length).toBe(1);
    expect(textOf(dialog().querySelector('#cc-step-heading-analysis'))).toBe('Results');

    timeline.flush(ccTimeline());
    runs.flush(ccRunRows());
    http.expectOne(r => r.url === `${CC_API}/comparison-sets`).flush(ccNoComparisonSets());
    http.expectOne(r => r.url === `${CC_API}/battery-runs`).flush([]);
    fixture.detectChanges();
    const wizard = fixture.componentInstance.wizard!;
    expect(wizard.step).toBe(4);
    expect(wizard.analysis!.result?.analysisId).toBe(7);
    expect(fixture.componentInstance.selectedKey).toBe('openai/gpt-5|high');
    expect(el.querySelector('#cc-latest-open')!.hasAttribute('aria-busy')).toBe(false);
  });

  it('opens the latest analysis on its documents, step 6, from the Latest analysis card', () => {
    createTab([ccAnalysisSummary(7, { reportDocumentCount: 3 })]);
    const documents = el.querySelector<HTMLButtonElement>('#cc-latest-documents')!;
    expect(textOf(documents)).toBe('Documents (3)');
    documents.click();
    fixture.detectChanges();

    http.expectOne(`${CC_API}/analyses/7`).flush(ccAnalysisResult());
    fixture.detectChanges();
    flushSubject();
    expect(dialog().open).toBe(true);
    expect(fixture.componentInstance.wizard!.step).toBe(6);
    // Step 6 reads the analysis's documents; those requests are its own specs' subject.
    http.match(() => true);
  });

  it('shows an open error in the Latest analysis card when the open starts there', () => {
    createTab();
    el.querySelector<HTMLButtonElement>('#cc-latest-open')!.click();
    http.expectOne(`${CC_API}/analyses/7`).flush({ error: 'Analysis 7 was not found.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(dialog().open).toBe(false);
    const error = latestCard().querySelector('.cc-latest-open-error')!;
    expect(error.getAttribute('role')).toBe('alert');
    expect(textOf(error)).toBe('Analysis 7 was not found.');
    expect(historyDialog().querySelector('.cc-hist-open-error')).toBeNull();
  });

  // --- Analysis History ---

  it('opens a saved analysis from Analysis History, closing the dialog before the wizard opens', () => {
    createTab([ccAnalysisSummary(8, { name: 'October check' }), ccAnalysisSummary(7)]);
    openHistory();
    expect(historyDialog().open).toBe(true);
    expect(document.activeElement?.id).toBe('cc-history-title');
    expect(Array.from(historyDialog().querySelectorAll('.cc-hist-card')).map(card => card.getAttribute('data-analysis-id')))
      .toEqual(['8', '7']);

    const wizardOpenAtHistoryClose: boolean[] = [];
    const history = historyDialog();
    const nativeClose = history.close.bind(history);
    vi.spyOn(history, 'close').mockImplementation((value?: string) => {
      wizardOpenAtHistoryClose.push(dialog().open);
      nativeClose(value);
    });

    historyCard(7).querySelector<HTMLButtonElement>('.cc-hist-open')!.click();
    fixture.detectChanges();
    // The dialog stays until the analysis arrives, busy on the card that asked.
    expect(history.open).toBe(true);
    expect(historyCard(7).querySelector('.cc-hist-open')!.getAttribute('aria-busy')).toBe('true');

    http.expectOne(`${CC_API}/analyses/7`).flush(ccAnalysisResult());
    fixture.detectChanges();
    expect(wizardOpenAtHistoryClose).toEqual([false]);
    expect(history.open).toBe(false);
    expect(dialog().open).toBe(true);
    flushSubject();
    expect(fixture.componentInstance.wizard!.step).toBe(4);
  });

  it('returns to Analysis History when a wizard opened from it closes, at the same scroll offset, with the card\'s Open focused', async () => {
    const many = Array.from({ length: 12 }, (_, i) => ccAnalysisSummary(20 - i));
    createTab(many);
    openHistory();
    const body = historyDialog().querySelector<HTMLElement>('.cc-history-body')!;
    body.scrollTop = 300;
    const saved = body.scrollTop;
    expect(saved).toBeGreaterThan(0);

    historyCard(17).querySelector<HTMLButtonElement>('.cc-hist-open')!.click();
    fixture.detectChanges();
    http.expectOne(`${CC_API}/analyses/17`).flush(ccAnalysisResult({ analysisId: 17 }));
    fixture.detectChanges();
    flushSubject();
    expect(historyDialog().open).toBe(false);
    expect(dialog().open).toBe(true);

    const done = closed();
    dialog().querySelector<HTMLButtonElement>('.cc-wizard-close')!.click();
    await done;
    fixture.detectChanges();
    http.expectOne(`${CC_API}/analyses`).flush(many);
    fixture.detectChanges();

    expect(historyDialog().open).toBe(true);
    expect(Math.abs(body.scrollTop - saved)).toBeLessThanOrEqual(1);
    expect(document.activeElement).toBe(historyCard(17).querySelector('.cc-hist-open'));
  });

  it('does not return to Analysis History when the wizard was opened from the Latest analysis card or the launcher', async () => {
    createTab([ccAnalysisSummary(8), ccAnalysisSummary(7)]);
    // A return to Analysis History first, so a leftover would show in the opens after it.
    openHistory();
    historyCard(7).querySelector<HTMLButtonElement>('.cc-hist-open')!.click();
    http.expectOne(`${CC_API}/analyses/7`).flush(ccAnalysisResult());
    fixture.detectChanges();
    flushSubject();
    let done = closed();
    dialog().close();
    await done;
    fixture.detectChanges();
    http.expectOne(`${CC_API}/analyses`).flush([ccAnalysisSummary(8), ccAnalysisSummary(7)]);
    fixture.detectChanges();
    expect(historyDialog().open).toBe(true);
    historyDialog().querySelector<HTMLButtonElement>('.cc-history-done')!.click();
    expect(historyDialog().open).toBe(false);

    el.querySelector<HTMLButtonElement>('#cc-latest-open')!.click();
    http.expectOne(`${CC_API}/analyses/8`).flush(ccAnalysisResult({ analysisId: 8 }));
    fixture.detectChanges();
    expect(dialog().open).toBe(true);
    done = closed();
    dialog().querySelector<HTMLButtonElement>('.cc-wizard-close')!.click();
    await done;
    fixture.detectChanges();
    http.expectOne(`${CC_API}/analyses`).flush([ccAnalysisSummary(8), ccAnalysisSummary(7)]);
    fixture.detectChanges();
    expect(historyDialog().open).toBe(false);

    openWizard();
    done = closed();
    dialog().querySelector<HTMLButtonElement>('.cc-wizard-close')!.click();
    await done;
    fixture.detectChanges();
    http.expectOne(`${CC_API}/analyses`).flush([ccAnalysisSummary(8), ccAnalysisSummary(7)]);
    fixture.detectChanges();
    expect(historyDialog().open).toBe(false);
  });

  it('does not return to Analysis History when the wizard closes to repeat a run\'s setup', async () => {
    createTab([ccAnalysisSummary(8), ccAnalysisSummary(7)]);
    openHistory();
    historyCard(7).querySelector<HTMLButtonElement>('.cc-hist-open')!.click();
    http.expectOne(`${CC_API}/analyses/7`).flush(ccAnalysisResult());
    fixture.detectChanges();
    flushSubject();
    const repeat = vi.spyOn(bridge, 'repeatRunSetup').mockImplementation(() => undefined);

    const done = closed();
    // Step 1's panel is hidden while step 4 shows, but its buttons are in the DOM.
    dialog().querySelector<HTMLButtonElement>('.cc-run-card[data-run-id="105"] .cc-repeat-btn')!.click();
    expect(repeat).toHaveBeenCalledWith(105);
    await done;
    fixture.detectChanges();
    http.expectOne(`${CC_API}/analyses`).flush([ccAnalysisSummary(8), ccAnalysisSummary(7)]);
    fixture.detectChanges();
    expect(dialog().open).toBe(false);
    expect(historyDialog().open).toBe(false);
  });

  it('shows an open error in Analysis History when the open starts there, and keeps the dialog open', () => {
    createTab([ccAnalysisSummary(8), ccAnalysisSummary(7)]);
    openHistory();
    historyCard(7).querySelector<HTMLButtonElement>('.cc-hist-open')!.click();
    http.expectOne(`${CC_API}/analyses/7`).flush({ error: 'Analysis 7 was not found.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(historyDialog().open).toBe(true);
    expect(dialog().open).toBe(false);
    expect(textOf(historyDialog().querySelector('.cc-hist-open-error'))).toBe('Analysis 7 was not found.');
    expect(latestCard().querySelector('.cc-latest-open-error')).toBeNull();
  });

  it('removes an analysis deleted in Analysis History, and the Latest analysis card moves to the next newest', () => {
    createTab([ccAnalysisSummary(8, { name: 'October check' }), ccAnalysisSummary(7)]);
    openHistory();
    historyCard(8).querySelector<HTMLButtonElement>('.cc-hist-delete')!.click();
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('dialog.cc-delete-dialog .cc-delete-confirm')!.click();
    const del = http.expectOne(`${CC_API}/analyses/8`);
    expect(del.request.method).toBe('DELETE');
    del.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(fixture.componentInstance.analyses.map(analysis => analysis.id)).toEqual([7]);
    expect(historyDialog().open).toBe(true);
    expect(historyCard(8)).toBeNull();
    expect(document.activeElement?.id).toBe('cc-hist-7-title');
    expect(textOf(latestCard().querySelector('.cc-latest-status'))).toBe('Loading the verdicts…');
    http.expectOne(`${CC_API}/analyses/7`).flush(ccAnalysisResult());
    fixture.detectChanges();
    expect(textOf(latestCard().querySelector('#cc-latest-title'))).toBe('#7 · Analysis 7');
    expect(textOf(el.querySelector('#cc-history-bar-summary'))).toContain('1 saved analysis of 1 model');
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
    expect(textOf(el.querySelector('#cc-history-bar-summary'))).toContain('2 saved analyses of 1 model');
    // The newest is still analysis 7, whose result the Latest analysis card already holds.
    http.expectNone(`${CC_API}/analyses/7`);

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

  /** Starts or ends a Timeline chart export, the way the workspace reports one, from step 2. */
  function setExporting(exporting: boolean): void {
    const wizard = fixture.componentInstance.wizard!;
    if (!wizard.timelineWorkspace) {
      dialog().querySelector<HTMLButtonElement>('#cc-step-tab-2')!.click();
      fixture.detectChanges();
    }
    const workspace = wizard.timelineWorkspace!;
    workspace.exporting = exporting;
    workspace.exportingChange.emit(exporting);
    fixture.detectChanges();
  }

  it('takes closedby="none" only while the wizard blocks closing', () => {
    createTab();
    openWizard();
    chooseModelInWizard();
    expect(dialog().hasAttribute('closedby')).toBe(false);

    setExporting(true);
    expect(dialog().getAttribute('closedby')).toBe('none');

    setExporting(false);
    expect(dialog().hasAttribute('closedby')).toBe(false);
  });

  it('reopens a close that gets through while blocked, restores focus and does not read the saved analyses', async () => {
    createTab();
    openWizard();
    chooseModelInWizard();
    setExporting(true);

    const focused = dialog().querySelector<HTMLElement>('#cc-step-panel-2')!;
    focused.focus();
    const refused = new Event('cancel', { cancelable: true });
    dialog().dispatchEvent(refused);
    expect(refused.defaultPrevented).toBe(true);

    // As a second Escape, whose cancel the browser no longer lets the page refuse.
    const bounced = closed();
    dialog().close();
    await bounced;
    fixture.detectChanges();
    expect(dialog().open).toBe(true);
    expect(document.activeElement).toBe(focused);
    http.expectNone(`${CC_API}/analyses`);

    // Without a recorded element, the wizard heading takes focus.
    const again = closed();
    dialog().close();
    await again;
    expect(dialog().open).toBe(true);
    expect(document.activeElement?.id).toBe('cc-wizard-title');
    http.expectNone(`${CC_API}/analyses`);

    // Once the export is over, a close stays closed and the saved analyses are read again.
    setExporting(false);
    const done = closed();
    dialog().close();
    await done;
    fixture.detectChanges();
    expect(dialog().open).toBe(false);
    http.expectOne(`${CC_API}/analyses`).flush([ccAnalysisSummary(7)]);
  });

  it('refuses leaving the sub-tab through the bridge while the wizard blocks closing, and stops asking once destroyed', () => {
    createTab();
    expect(bridge.leaveRefusal()).toBeNull();
    openWizard();
    chooseModelInWizard();
    expect(bridge.leaveRefusal()).toBeNull();

    setExporting(true);
    expect(bridge.leaveRefusal()).toBe(CC_LEAVE_REFUSAL);

    setExporting(false);
    expect(bridge.leaveRefusal()).toBeNull();

    setExporting(true);
    dialog().close();
    fixture.destroy();
    expect(bridge.leaveRefusal()).toBeNull();
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
    const row = (runId: number): HTMLElement =>
      dialog().querySelector<HTMLElement>(`.cc-run-card[data-run-id="${runId}"]`)!;

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

    dialog().querySelector<HTMLButtonElement>('.cc-run-card[data-run-id="105"] .cc-repeat-btn')!.click();
    fixture.detectChanges();
    expect(repeat).not.toHaveBeenCalled();
    expect(dialog().open).toBe(true);
    expect(dialog().querySelector('.cc-tl-announcement')!.textContent).toContain('Wait for the chart export');
    delete (wizard as unknown as { closeBlocked?: boolean }).closeBlocked;
  });

  it('opens the latest analysis from the launcher after the wizard was closed', async () => {
    createTab();
    openWizard();
    chooseModelInWizard();
    const done = closed();
    dialog().querySelector<HTMLButtonElement>('.cc-wizard-close')!.click();
    await done;
    fixture.detectChanges();
    http.expectOne(`${CC_API}/analyses`).flush([ccAnalysisSummary(7)]);
    fixture.detectChanges();

    const open = el.querySelector<HTMLButtonElement>('#cc-latest-open')!;
    expect(textOf(open)).toBe('Open Analysis #7');
    open.click();
    fixture.detectChanges();
    http.expectOne(`${CC_API}/analyses/7`).flush(ccAnalysisResult());
    fixture.detectChanges();
    expect(dialog().open).toBe(true);
    expect(fixture.componentInstance.wizard!.step).toBe(4);
  });

  it('empties the launcher when the only saved analysis is deleted in Analysis History', () => {
    createTab();
    openHistory();
    historyCard(7).querySelector<HTMLButtonElement>('.cc-hist-delete')!.click();
    fixture.detectChanges();
    const confirm = el.querySelector<HTMLDialogElement>('dialog.cc-delete-dialog')!;
    expect(confirm.open).toBe(true);
    expect(textOf(confirm.querySelector('h3'))).toBe('Delete Analysis 7?');
    confirm.querySelector<HTMLButtonElement>('.cc-delete-confirm')!.click();
    http.expectOne(`${CC_API}/analyses/7`).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(confirm.open).toBe(false);
    expect(historyCard(7)).toBeNull();
    // With no card left, focus goes to the dialog's title.
    expect(document.activeElement?.id).toBe('cc-history-title');
    expect(textOf(latestCard().querySelector('.cc-latest-empty'))).toContain('No analysis is saved yet.');
    expect(fixture.componentInstance.latestResult).toBeNull();
    expect(el.querySelector('#cc-open-history')!.getAttribute('aria-disabled')).toBe('true');
  });

  // --- Comparison sets ---

  describe('comparison sets', () => {
    const compare = (): HTMLSelectElement => dialog().querySelector<HTMLSelectElement>('#cc-compare')!;

    function chooseSet(key: string): void {
      compare().value = key;
      compare().dispatchEvent(new Event('change'));
      fixture.detectChanges();
    }

    it('compares within the server\'s default set, lists its battery runs and names it in the header', () => {
      createTab();
      openWizard();
      chooseModelInWizard(batterySubject());
      const tab = fixture.componentInstance;

      expect(tab.compareKey).toBe(CC_BATTERY_SET_KEY);
      expect(tab.scope).toBe(CC_EMPTY_BATTERY_SCOPE);
      expect(compare().value).toBe(CC_BATTERY_SET_KEY);
      const cards = Array.from(dialog().querySelectorAll('.cc-battery-card')).map(card => card.getAttribute('data-battery-run-id'));
      expect(cards).toEqual(['12', '11']);
      expect(dialog().querySelector('.cc-run-card:not(.cc-battery-card)')).toBeNull();
      expect(textOf(dialog().querySelector('.cc-wizard-subtitle'))).toBe('GPT-5 high · Two initial suites (revision 1) · 2 battery runs · All dates');
      expect(tab.compareSet?.label).toBe('Two initial suites (revision 1)');
      expect(storedSubject()).toEqual({
        version: 2, modelKey: 'openai/gpt-5|high', range: { preset: 'all', fromDay: '', toDay: '', anchorUtc: null },
        compare: { kind: 'battery', key: CC_BATTERY_SET_KEY }
      });
    });

    it('clears the selection when another set is compared, and says so', () => {
      createTab();
      openWizard();
      chooseModelInWizard(batterySubject());
      const tab = fixture.componentInstance;

      dialog().querySelector<HTMLInputElement>('#cc-brun-11-include')!.click();
      fixture.detectChanges();
      expect([...tab.scope.leftOut]).toEqual([11]);
      expect(textOf(dialog().querySelector('.cc-wizard-subtitle'))).toContain('· 1 in the analysis');

      chooseSet('suite:id:5');
      http.expectNone(r => r.url === `${CC_API}/runs`);
      expect(tab.compareKey).toBe('suite:id:5');
      expect(tab.scope.leftOut.size).toBe(0);
      expect(tab.scope.unitKind ?? 'run').toBe('run');
      expect(textOf(dialog().querySelector('.cc-tl-announcement'))).toBe(CC_SET_CHANGE_NOTE);
      // The suite's own runs and the members of the battery runs that ran it.
      expect(dialog().querySelectorAll('.cc-run-card').length).toBe(8);
      expect(textOf(dialog().querySelector('.cc-wizard-subtitle'))).toBe('GPT-5 high · Board Suite · 8 runs · All dates');
      expect((storedSubject() as { compare: unknown }).compare).toEqual({ kind: 'suite', key: 'suite:id:5' });

      // Nothing selected: switching again says nothing.
      chooseSet(CC_BATTERY_SET_KEY);
      expect(tab.announcement).toBe('');
    });

    it('restores a stored set while it is offered, and reads a version-1 record as no stored set', () => {
      localStorage.setItem(CC_SUBJECT_STORAGE_KEY, JSON.stringify({
        version: 2, modelKey: 'openai/gpt-5|high', range: { preset: 'all', fromDay: '', toDay: '', anchorUtc: null },
        compare: { kind: 'suite', key: 'suite:id:6' }
      }));
      createTab();
      flushSubject(batterySubject());
      expect(fixture.componentInstance.compareKey).toBe('suite:id:6');
      expect(fixture.componentInstance.compareSet?.label).toBe('Wiki Suite');
      fixture.destroy();

      localStorage.setItem(CC_SUBJECT_STORAGE_KEY, JSON.stringify({
        version: 1, modelKey: 'openai/gpt-5|high', range: { preset: 'all', fromDay: '', toDay: '', anchorUtc: null }
      }));
      createTab();
      expect(fixture.componentInstance.selectedKey).toBe('openai/gpt-5|high');
      flushSubject(batterySubject());
      expect(fixture.componentInstance.compareKey).toBe(CC_BATTERY_SET_KEY);
      expect((storedSubject() as { version: number }).version).toBe(2);
    });

    it('falls back to the default set when the stored one is no longer offered', () => {
      localStorage.setItem(CC_SUBJECT_STORAGE_KEY, JSON.stringify({
        version: 2, modelKey: 'openai/gpt-5|high', range: { preset: 'all', fromDay: '', toDay: '', anchorUtc: null },
        compare: { kind: 'suite', key: 'suite:id:99' }
      }));
      createTab();
      flushSubject(batterySubject());
      expect(fixture.componentInstance.compareKey).toBe(CC_BATTERY_SET_KEY);
    });

    it('names each saved analysis\'s compared set, and All suites for one without', () => {
      createTab([
        ccAnalysisSummary(8, { comparisonSetKey: CC_BATTERY_SET_KEY, comparisonSetLabel: 'Two initial suites (revision 1)' }),
        ccAnalysisSummary(7)
      ]);
      const subject = latestCard().querySelector('.cc-latest-subject')!;
      expect(textOf(subject.querySelector('.cc-kind-tag'))).toBe('Battery');
      expect(subject.querySelector('.cc-kind-tag')!.getAttribute('data-kind')).toBe('battery');
      expect(textOf(subject.querySelector('.cc-latest-compared-label'))).toBe('Two initial suites (revision 1)');

      openHistory();
      expect(textOf(historyCard(8).querySelector('.cc-kind-tag'))).toBe('Battery');
      expect(textOf(historyCard(8).querySelector('.cc-hist-compared-label'))).toBe('Two initial suites (revision 1)');
      expect(textOf(historyCard(7).querySelector('.cc-kind-tag'))).toBe('All suites');
      expect(historyCard(7).querySelector('.cc-hist-compared-label')).toBeNull();
    });

    it('opens a battery run report and a member\'s run report through the shell', () => {
      createTab();
      openWizard();
      chooseModelInWizard(batterySubject());
      const batteryReport = vi.spyOn(bridge, 'openBatteryRunReport').mockImplementation(() => undefined);
      const runReport = vi.spyOn(bridge, 'viewRunDetail').mockImplementation(() => undefined);
      const card = dialog().querySelector<HTMLElement>('.cc-battery-card[data-battery-run-id="12"]')!;

      card.querySelector<HTMLButtonElement>('.cc-open-battery-report-btn')!.click();
      expect(batteryReport).toHaveBeenCalledWith(12);
      card.querySelector<HTMLButtonElement>('.cc-battery-member[data-run-id="304"] .cc-open-report-btn')!.click();
      expect(runReport).toHaveBeenCalledWith(304);
      expect(dialog().open).toBe(true);
    });
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
