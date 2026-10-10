import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { of } from 'rxjs';

import { SystemService } from '../../../../services/system.service';
import { groupOverseerEvents, taggedAnnotations } from '../chat-consistency-events';
import { ccResultKeyFigures } from '../chat-consistency-results';
import {
  CC_API,
  ccAnalysisResult,
  ccAttribution,
  ccBatteryRunRows,
  ccEndpoint,
  ccEventAnnotations,
  ccEventPoints,
  ccFreshness,
  ccOutOfDateFreshness,
  ccOverseerEvents,
  ccRunRows,
  ccRunSelectionView,
  ccTimeline,
  chatConsistencyTestProviders,
  textOf
} from '../chat-consistency-tab.testing';
import { CcAnalysisFreshness, CcAnalysisResult } from '../chat-consistency.models';
import { CcResultsImageDialogComponent } from '../results-image/results-image-dialog.component';
import { ccResultsImageIo } from '../results-image/results-image-export';
import {
  CC_RESULTS_IMAGE_SECTIONS_STORAGE_KEY,
  CC_RESULTS_IMAGE_STORAGE_KEY,
  readStoredCcResultsImageSettings
} from '../results-image/results-image-settings';
import { CcEndpointCardComponent } from './endpoint-card/endpoint-card.component';
import { CcNextRunsComponent } from './next-runs/next-runs.component';
import { CcResultPeriodsComponent } from './result-periods/result-periods.component';
import {
  CC_RESULTS_STORAGE_KEY,
  CcResultsViewComponent,
  readStoredResultsTab
} from './results-view.component';
import { CcVerdictBannerComponent } from './verdict-banner/verdict-banner.component';

describe('CcResultsViewComponent', () => {
  let fixture: ComponentFixture<CcResultsViewComponent>;
  let el: HTMLElement;
  let live = false;

  /** Destroys the current instance and detaches it, so element ids stay unique in the document. */
  function teardown(): void {
    if (!live) return;
    fixture.destroy();
    el.remove();
    live = false;
  }

  function create(result: CcAnalysisResult = ccAnalysisResult()): void {
    teardown();
    fixture = TestBed.createComponent(CcResultsViewComponent);
    live = true;
    el = fixture.nativeElement as HTMLElement;
    // Attached, so focus can move.
    document.body.appendChild(el);
    fixture.componentRef.setInput('result', result);
    fixture.componentRef.setInput('points', ccTimeline().points);
    fixture.detectChanges();
  }

  function show(result: CcAnalysisResult): void {
    fixture.componentRef.setInput('result', result);
    fixture.detectChanges();
  }

  const tabButtons = () => Array.from(el.querySelectorAll<HTMLButtonElement>(':scope > .cc-res-bar > .cc-res-tabs > [role="tab"]'));
  const tabButton = (id: string) => el.querySelector<HTMLButtonElement>(`#cc-res-tab-${id}`)!;
  const panel = (id: string) => el.querySelector<HTMLElement>(`#cc-res-panel-${id}`)!;
  const selectedTab = () => tabButtons().find(tab => tab.getAttribute('aria-selected') === 'true')?.id;
  const stored = (): unknown => JSON.parse(localStorage.getItem(CC_RESULTS_STORAGE_KEY) ?? 'null');
  const keydown = (target: HTMLElement, key: string): KeyboardEvent => {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    return event;
  };

  beforeEach(async () => {
    localStorage.removeItem(CC_RESULTS_STORAGE_KEY);
    localStorage.removeItem(CC_RESULTS_IMAGE_STORAGE_KEY);
    localStorage.removeItem(CC_RESULTS_IMAGE_SECTIONS_STORAGE_KEY);
    await TestBed.configureTestingModule({
      imports: [CcResultsViewComponent],
      providers: [
        ...chatConsistencyTestProviders(),
        { provide: SystemService, useValue: { getVersion: () => of('9.9.9') } }
      ]
    }).compileComponents();
    create();
  });

  afterEach(() => {
    for (const dialog of Array.from(document.querySelectorAll('dialog[open]')) as HTMLDialogElement[]) {
      dialog.close();
    }
    teardown();
    vi.restoreAllMocks();
    localStorage.removeItem(CC_RESULTS_STORAGE_KEY);
    localStorage.removeItem(CC_RESULTS_IMAGE_STORAGE_KEY);
    localStorage.removeItem(CC_RESULTS_IMAGE_SECTIONS_STORAGE_KEY);
  });

  describe('the shell', () => {
    it('leads with the bar, which holds the tab row and then the export group', () => {
      const bar = el.firstElementChild as HTMLElement;
      expect(bar.classList).toContain('cc-res-bar');
      expect(Array.from(bar.children).map(child => child.className)).toEqual([
        'gh-tabs gh-tabs-secondary gh-tabs-wrap cc-res-tabs',
        'cc-res-export'
      ]);
      expect(bar.nextElementSibling?.matches('p.cc-res-export-status[role="status"]')).toBe(true);
    });
  });

  describe('the out-of-date notice', () => {
    /** Answers the pending freshness request of the shown analysis. */
    function answerFreshness(freshness: CcAnalysisFreshness): void {
      const http = TestBed.inject(HttpTestingController);
      http.expectOne(`${CC_API}/analyses/${freshness.analysisId}/freshness`).flush(freshness);
      fixture.detectChanges();
    }

    it('asks for the saved analysis\'s freshness and shows nothing while it is current', () => {
      answerFreshness(ccFreshness());
      expect(el.querySelector('.cc-fresh-notice')).toBeNull();
      expect(el.querySelector('.cc-fresh-unchecked')).toBeNull();
    });

    it('shows the notice under the bar, over every section, and Analyze again asks the host', () => {
      let requested = 0;
      fixture.componentInstance.analyzeAgain.subscribe(() => requested++);
      answerFreshness(ccOutOfDateFreshness());
      const notice = el.querySelector<HTMLElement>(':scope > app-cc-freshness-notice .cc-fresh-notice')!;
      expect(textOf(notice.querySelector('#cc-res-fresh-title'))).toBe('This analysis is out of date.');
      expect(textOf(notice.querySelector('.cc-fresh-text'))).toContain(
        'Saved under analysis code version 5; Overseer now analyzes under version 6.');
      expect(panel('summary').contains(notice)).toBe(false);

      notice.querySelector<HTMLButtonElement>('#cc-res-fresh-again')!.click();
      expect(requested).toBe(1);
    });

    it('says quietly when the changes since saving could not be checked', () => {
      answerFreshness(ccFreshness({ inputsChanged: null, inputsNote: 'A run of the analysis was deleted.' }));
      expect(textOf(el.querySelector('.cc-fresh-unchecked')))
        .toBe('Changes since saving could not be checked: A run of the analysis was deleted.');
    });

    it('asks again only when another analysis is shown', () => {
      const http = TestBed.inject(HttpTestingController);
      answerFreshness(ccFreshness());
      show(ccAnalysisResult({ name: 'Renamed' }));
      http.expectNone(`${CC_API}/analyses/7/freshness`);
      show(ccAnalysisResult({ analysisId: 9 }));
      answerFreshness(ccOutOfDateFreshness({ analysisId: 9 }));
      expect(el.querySelector('.cc-fresh-notice')).not.toBeNull();
    });
  });

  describe('the Summary panel', () => {
    it('leads with the verdict banner over the stored result, under no section title of its own', () => {
      const summary = panel('summary');
      expect(summary.firstElementChild?.tagName).toBe('APP-CC-VERDICT-BANNER');
      expect(summary.querySelector('.gh-section-title')).toBeNull();
      const banner = fixture.debugElement.query(By.directive(CcVerdictBannerComponent));
      expect(summary.contains(banner.nativeElement as HTMLElement)).toBe(true);
      expect((banner.componentInstance as CcVerdictBannerComponent).result).toBe(fixture.componentInstance.result);
    });

    it('shows the key figures as score cards, in a group named Key figures, after the banner', () => {
      const heading = panel('summary').querySelector<HTMLElement>('h5#cc-res-figures-title')!;
      expect(heading.classList).toContain('visually-hidden');
      expect(textOf(heading)).toBe('Key figures');
      const group = panel('summary').querySelector<HTMLElement>(':scope > div.rr-figures.cc-res-figures')!;
      const banner = panel('summary').querySelector('app-cc-verdict-banner')!;
      expect(banner.compareDocumentPosition(group) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(group.getAttribute('role')).toBe('group');
      expect(group.getAttribute('aria-labelledby')).toBe('cc-res-figures-title');

      const expected = ccResultKeyFigures(ccAnalysisResult());
      const cards = Array.from(group.querySelectorAll<HTMLElement>(':scope > .score-card'));
      expect(cards.map(card => card.getAttribute('data-figure'))).toEqual(expected.map(figure => figure.key));
      cards.forEach((card, i) => {
        expect(textOf(card.querySelector('.score-label'))).toBe(expected[i].label);
        expect(textOf(card.querySelector('.score-value'))).toBe(expected[i].value);
        const sub = card.querySelector('.score-subvalue');
        expect(sub ? textOf(sub) : undefined).toBe(expected[i].sub);
        const note = card.querySelector('.score-note');
        expect(note ? textOf(note) : undefined).toBe(expected[i].note);
      });
    });

    it('draws no charts, no verdict table and no facts list', () => {
      expect(el.querySelector('app-cc-chart-figure')).toBeNull();
      expect(el.querySelector('figure.cc-figure')).toBeNull();
      expect(el.querySelector('.cc-verdict-table')).toBeNull();
      expect(el.querySelector('.cc-res-facts')).toBeNull();
      expect(el.querySelector('.cc-headline')).toBeNull();
    });
  });

  describe('the tabs', () => {
    it('are a text-only tab row Result sections: Summary, Verdicts, Periods, Attribution, Next runs and Details', () => {
      const list = el.querySelector<HTMLElement>(':scope > .cc-res-bar > .cc-res-tabs')!;
      expect(list.getAttribute('role')).toBe('tablist');
      expect(list.classList).toContain('gh-tabs');
      expect(list.classList).toContain('gh-tabs-secondary');
      expect(list.classList).toContain('gh-tabs-wrap');
      expect(list.getAttribute('aria-label')).toBe('Result sections');
      const tabs = tabButtons();
      expect(tabs.map(tab => tab.id)).toEqual([
        'cc-res-tab-summary', 'cc-res-tab-verdicts', 'cc-res-tab-periods', 'cc-res-tab-attribution', 'cc-res-tab-nextRuns',
        'cc-res-tab-details'
      ]);
      expect(tabs.map(tab => textOf(tab))).toEqual(['Summary', 'Verdicts', 'Periods', 'Attribution', 'Next runs 2 cards', 'Details']);
      for (const tab of tabs) {
        expect(tab.getAttribute('type')).toBe('button');
        expect(tab.classList).toContain('gh-tab');
        expect(tab.querySelector('svg')).toBeNull();
        expect(tab.getAttribute('aria-controls')).toBe(tab.id.replace('cc-res-tab-', 'cc-res-panel-'));
      }
    });

    it('count the next-run cards in a pill, with a hidden word, and drop it without next runs', () => {
      const pill = tabButton('nextRuns').querySelector<HTMLElement>('.cc-res-tab-count')!;
      expect(textOf(pill)).toBe('2 cards');
      expect(pill.querySelector('.visually-hidden')!.textContent!.trim()).toBe('cards');

      show(ccAnalysisResult({ nextRuns: [ccAnalysisResult().nextRuns[0]] }));
      expect(textOf(tabButton('nextRuns').querySelector('.cc-res-tab-count'))).toBe('1 card');

      show(ccAnalysisResult({ nextRuns: [] }));
      expect(tabButton('nextRuns').querySelector('.cc-res-tab-count')).toBeNull();
    });

    it('render every panel once, each a tabpanel labeled by its tab, all but the selected one hidden', () => {
      const panels = Array.from(el.querySelectorAll<HTMLElement>(':scope > [role="tabpanel"]'));
      expect(panels.map(p => p.id)).toEqual([
        'cc-res-panel-summary', 'cc-res-panel-verdicts', 'cc-res-panel-periods', 'cc-res-panel-attribution',
        'cc-res-panel-nextRuns', 'cc-res-panel-details'
      ]);
      for (const p of panels) {
        expect(p.getAttribute('aria-labelledby')).toBe(p.id.replace('cc-res-panel-', 'cc-res-tab-'));
        expect(p.getAttribute('tabindex')).toBe('0');
      }
      expect(panels.map(p => p.hidden)).toEqual([false, true, true, true, true, true]);
      expect(selectedTab()).toBe('cc-res-tab-summary');
      expect(tabButtons().map(tab => tab.getAttribute('tabindex'))).toEqual(['0', '-1', '-1', '-1', '-1', '-1']);

      tabButton('attribution').click();
      fixture.detectChanges();
      expect(panels.map(p => p.hidden)).toEqual([true, true, true, false, true, true]);
      expect(selectedTab()).toBe('cc-res-tab-attribution');
      expect(tabButtons().map(tab => tab.getAttribute('tabindex'))).toEqual(['-1', '-1', '-1', '0', '-1', '-1']);
    });

    it('move with Left and Right, wrapping, and jump with Home and End; focus follows selection', () => {
      const summary = tabButton('summary');
      summary.focus();

      expect(keydown(summary, 'ArrowRight').defaultPrevented).toBe(true);
      expect(selectedTab()).toBe('cc-res-tab-verdicts');
      expect(document.activeElement).toBe(tabButton('verdicts'));
      expect(panel('verdicts').hidden).toBe(false);
      expect(panel('summary').hidden).toBe(true);

      keydown(tabButton('verdicts'), 'ArrowLeft');
      keydown(tabButton('summary'), 'ArrowLeft');
      expect(selectedTab()).toBe('cc-res-tab-details');
      expect(document.activeElement).toBe(tabButton('details'));

      keydown(tabButton('details'), 'ArrowRight');
      expect(selectedTab()).toBe('cc-res-tab-summary');

      keydown(tabButton('summary'), 'End');
      expect(selectedTab()).toBe('cc-res-tab-details');
      keydown(tabButton('details'), 'Home');
      expect(selectedTab()).toBe('cc-res-tab-summary');
      expect(document.activeElement).toBe(tabButton('summary'));
    });

    it('leave other keys alone', () => {
      const summary = tabButton('summary');
      expect(keydown(summary, 'ArrowDown').defaultPrevented).toBe(false);
      expect(keydown(summary, 'a').defaultPrevented).toBe(false);
      expect(selectedTab()).toBe('cc-res-tab-summary');
    });

    it('remember the selected tab in this browser as { version: 2, tab }', () => {
      expect(localStorage.getItem(CC_RESULTS_STORAGE_KEY)).toBeNull();
      tabButton('nextRuns').click();
      expect(stored()).toEqual({ version: 2, tab: 'nextRuns' });

      create();
      expect(selectedTab()).toBe('cc-res-tab-nextRuns');
      expect(panel('nextRuns').hidden).toBe(false);
      expect(panel('summary').hidden).toBe(true);
    });

    it('fall back to Summary for a missing, version-1, unknown or damaged stored tab', () => {
      expect(readStoredResultsTab()).toBe('summary');
      localStorage.setItem(CC_RESULTS_STORAGE_KEY, JSON.stringify({ version: 2, tab: 'periods' }));
      expect(readStoredResultsTab()).toBe('periods');
      localStorage.setItem(CC_RESULTS_STORAGE_KEY, JSON.stringify({ version: 1, tab: 'periods' }));
      expect(readStoredResultsTab()).toBe('summary');
      localStorage.setItem(CC_RESULTS_STORAGE_KEY, JSON.stringify({ version: 2, tab: 'charts' }));
      expect(readStoredResultsTab()).toBe('summary');
      localStorage.setItem(CC_RESULTS_STORAGE_KEY, '{');
      expect(readStoredResultsTab()).toBe('summary');
      localStorage.setItem(CC_RESULTS_STORAGE_KEY, '["details"]');
      expect(readStoredResultsTab()).toBe('summary');

      localStorage.setItem(CC_RESULTS_STORAGE_KEY, JSON.stringify({ version: 1, tab: 'verdicts' }));
      create();
      expect(selectedTab()).toBe('cc-res-tab-summary');
      expect(panel('summary').hidden).toBe(false);
    });
  });

  describe('the Verdicts panel', () => {
    const cards = () => fixture.debugElement.queryAll(By.directive(CcEndpointCardComponent))
      .map(debug => debug.componentInstance as CcEndpointCardComponent);

    const notComputable = () => ccAnalysisResult({
      endpoints: [
        ccEndpoint('P1'),
        ccEndpoint('P2', { computed: false, notComputedReason: 'No common time stratum.', verdict: null, verdictLabel: '', grade: 'notEstablished' }),
        ccEndpoint('P3', { computed: false, notComputedReason: 'No common time stratum.', verdict: null, verdictLabel: '', grade: 'notEstablished' }),
        ccEndpoint('P4'),
        ccEndpoint('P5', { computed: false, notComputedReason: 'No priced calls in the baseline.', verdict: null, verdictLabel: '', grade: 'notEstablished' })
      ]
    });

    it('has a visually hidden heading, for the heading outline only, and the lead', () => {
      expect(panel('verdicts').querySelector('.gh-section-title')).toBeNull();
      expect(textOf(panel('verdicts').querySelector('h5.visually-hidden'))).toBe('Verdicts');
      expect(textOf(panel('verdicts').querySelector('.cc-res-lead')))
        .toBe('Each endpoint compares the comparison period with the baseline against a margin fixed in advance.');
    });

    it('shows a card per computed endpoint: changed, improved, within margin, inconclusive, then by id', () => {
      show(ccAnalysisResult({
        endpoints: [
          ccEndpoint('P1'),
          ccEndpoint('P2', { verdict: 'changedDegraded', verdictLabel: 'degraded', grade: 'indicated' }),
          ccEndpoint('P3', { verdict: 'inconclusive', verdictLabel: 'inconclusive', grade: 'notEstablished' }),
          // Work per turn has no better or worse: its change is a change.
          ccEndpoint('P4', { verdict: 'changedImproved', verdictLabel: 'less work' }),
          ccEndpoint('P5', { verdict: 'changedImproved', verdictLabel: 'improved' })
        ]
      }));
      expect(cards().map(card => card.endpoint.id)).toEqual(['P2', 'P4', 'P5', 'P1', 'P3']);
      expect(panel('verdicts').querySelectorAll('.cc-res-endpoints > app-cc-endpoint-card').length).toBe(5);
      expect(cards().every(card => card.batteryAnalysis === false)).toBe(true);
      expect(el.querySelector('#cc-ep-uncomputed')).toBeNull();
    });

    it('tells the cards of a battery analysis', () => {
      show(ccAnalysisResult({ unitKind: 'batteryRun' }));
      expect(cards().length).toBe(5);
      expect(cards().every(card => card.batteryAnalysis === true)).toBe(true);
    });

    it('puts the not-computable endpoints in one card, a row per reason, after the computed ones', () => {
      show(notComputable());
      expect(cards().map(card => card.endpoint.id)).toEqual(['P1', 'P4']);
      const section = el.querySelector<HTMLElement>('section.cc-ep-uncomputed#cc-ep-uncomputed')!;
      expect(section.getAttribute('tabindex')).toBe('-1');
      expect(section.getAttribute('aria-labelledby')).toBe('cc-ep-uncomputed-title');
      expect(textOf(section.querySelector('#cc-ep-uncomputed-title'))).toBe('Not computable (3)');
      expect(section.querySelector('#cc-ep-uncomputed-title svg')!.getAttribute('aria-hidden')).toBe('true');
      const rows = Array.from(section.querySelectorAll('.cc-ep-uncomputed-row'))
        .map(row => [textOf(row.querySelector('.cc-ep-uncomputed-names')), textOf(row.querySelector('.cc-ep-uncomputed-reason'))]);
      expect(rows).toEqual([
        ['P2 Time to first answer text · P3 Answer streaming rate', 'No common time stratum.'],
        ['P5 Cost per question', 'No priced calls in the baseline.']
      ]);
      const endpoints = el.querySelector('.cc-res-endpoints')!;
      expect(endpoints.compareDocumentPosition(section) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('offers See the next runs, which selects that tab and focuses its panel', () => {
      show(notComputable());
      const link = el.querySelector<HTMLButtonElement>('#cc-ep-uncomputed button.btn-link.cc-ep-uncomputed-next')!;
      expect(textOf(link)).toBe('See the next runs');
      expect(link.getAttribute('type')).toBe('button');
      link.click();
      expect(selectedTab()).toBe('cc-res-tab-nextRuns');
      expect(panel('nextRuns').hidden).toBe(false);
      expect(document.activeElement).toBe(panel('nextRuns'));
      expect(stored()).toEqual({ version: 2, tab: 'nextRuns' });

      show({ ...notComputable(), nextRuns: [] });
      expect(el.querySelector('#cc-ep-uncomputed')).not.toBeNull();
      expect(el.querySelector('.cc-ep-uncomputed-next')).toBeNull();
    });

    it('selects Verdicts and focuses the card of the endpoint the banner names', () => {
      expect(panel('summary').hidden).toBe(false);
      expect(panel('verdicts').hidden).toBe(true);
      const banner = fixture.debugElement.query(By.directive(CcVerdictBannerComponent)).componentInstance as CcVerdictBannerComponent;
      banner.endpointSelected.emit('P2');
      expect(selectedTab()).toBe('cc-res-tab-verdicts');
      expect(panel('verdicts').hidden).toBe(false);
      expect(panel('summary').hidden).toBe(true);
      expect(document.activeElement).toBe(el.querySelector('#cc-ep-P2'));
      expect(stored()).toEqual({ version: 2, tab: 'verdicts' });
    });

    it('focuses the not-computable card for an endpoint without a card of its own', () => {
      show(notComputable());
      const banner = fixture.debugElement.query(By.directive(CcVerdictBannerComponent)).componentInstance as CcVerdictBannerComponent;
      banner.endpointSelected.emit('P3');
      expect(selectedTab()).toBe('cc-res-tab-verdicts');
      expect(document.activeElement).toBe(el.querySelector('#cc-ep-uncomputed'));
    });
  });

  describe('the Periods and Next runs panels', () => {
    it('hand the stored result and the step-1 data to the period cards, and forward their report requests', () => {
      const rows = ccRunRows();
      const batteryRows = ccBatteryRunRows();
      const groups = groupOverseerEvents(ccOverseerEvents(), ccEventPoints());
      const annotations = taggedAnnotations(ccEventAnnotations());
      fixture.componentRef.setInput('rows', rows);
      fixture.componentRef.setInput('batteryRows', batteryRows);
      fixture.componentRef.setInput('eventNumbering', groups);
      fixture.componentRef.setInput('annotations', annotations);
      fixture.detectChanges();

      const host = panel('periods').querySelector('app-cc-result-periods');
      expect(host).not.toBeNull();
      const periods = fixture.debugElement.query(By.directive(CcResultPeriodsComponent)).componentInstance as CcResultPeriodsComponent;
      expect(periods.result).toBe(fixture.componentInstance.result);
      expect(periods.rows).toBe(rows);
      expect(periods.batteryRows).toBe(batteryRows);
      expect(periods.points).toBe(fixture.componentInstance.points);
      expect(periods.batteryPoints).toBe(fixture.componentInstance.batteryPoints);
      expect(periods.eventGroups).toBe(groups);
      expect(periods.annotations).toBe(annotations);

      const runs: number[] = [];
      const batteryRuns: number[] = [];
      fixture.componentInstance.openRunReport.subscribe(id => runs.push(id));
      fixture.componentInstance.openBatteryRunReport.subscribe(id => batteryRuns.push(id));
      periods.openRunReport.emit(101);
      periods.openBatteryRunReport.emit(11);
      expect(runs).toEqual([101]);
      expect(batteryRuns).toEqual([11]);
    });

    it('hand the result and the rows to the next-run cards, and forward Repeat this run\'s setup', () => {
      const rows = ccRunRows();
      fixture.componentRef.setInput('rows', rows);
      fixture.detectChanges();
      expect(panel('nextRuns').querySelector('app-cc-next-runs')).not.toBeNull();
      const next = fixture.debugElement.query(By.directive(CcNextRunsComponent)).componentInstance as CcNextRunsComponent;
      expect(next.result).toBe(fixture.componentInstance.result);
      expect(next.rows).toBe(rows);

      const repeated: number[] = [];
      fixture.componentInstance.repeatSetup.subscribe(id => repeated.push(id));
      next.repeatSetup.emit(205);
      expect(repeated).toEqual([205]);
    });
  });

  describe('the Attribution panel', () => {
    const groups = () => Array.from(panel('attribution').querySelectorAll<HTMLElement>('.cc-attribution-group'));

    it('names the decisive changes, then a card per side with attributions, and the sides without', () => {
      expect(panel('attribution').querySelector('.gh-section-title')).toBeNull();
      expect(textOf(panel('attribution').querySelector('h5.visually-hidden'))).toBe('Attribution');
      expect(textOf(panel('attribution').querySelector('.cc-attr-changes-label'))).toBe('Changes to attribute');
      const chips = Array.from(panel('attribution').querySelectorAll<HTMLElement>('.cc-attr-chips > li'));
      expect(chips.map(chip => textOf(chip))).toEqual(['Time to first answer text Degraded']);
      expect(chips[0].getAttribute('data-verdict')).toBe('changedDegraded');

      expect(groups().map(group => group.getAttribute('data-side'))).toEqual(['ours', 'provider', 'undetermined']);
      expect(groups().map(group => textOf(group.querySelector('.cc-attribution-title'))))
        .toEqual(['Our changes', 'Provider', 'Undetermined']);
      const ours = groups()[0].querySelector<HTMLElement>('.cc-attribution-card')!;
      expect(textOf(ours.querySelector('.cc-attribution-label'))).toBe('Tool guides edit');
      expect(textOf(ours.querySelector('.cc-grade-pill'))).toBe('Indicated');
      expect(ours.querySelector('.cc-grade-pill')!.getAttribute('data-grade')).toBe('indicated');
      expect(Array.from(ours.querySelectorAll('.cc-attribution-endpoints > li')).map(tag => textOf(tag))).toEqual(['P2']);
      expect(textOf(ours.querySelector('.cc-attribution-evidence'))).toBe('Tool guides edit evidence');
      expect(ours.querySelector('.cc-attribution-refs')).toBeNull();
      expect(textOf(groups()[2].querySelector('.cc-attribution-endpoints'))).toBe('P4');

      expect(textOf(panel('attribution').querySelector('.cc-attr-unattributed'))).toBe('Nothing is attributed to infrastructure.');
      expect(panel('attribution').querySelector('.cc-res-empty')).toBeNull();
      expect(panel('attribution').querySelector('.cc-attribution-none')).toBeNull();
    });

    it('shows an attribution\'s events as event pills', () => {
      const base = ccAnalysisResult();
      show(ccAnalysisResult({
        attribution: {
          ...base.attribution,
          attributions: [ccAttribution('ours', 'Tool guides edit', { eventRefs: ['tool guides edited on 2026-09-05', 'harness 27 → 28'] })]
        }
      }));
      const pills = Array.from(panel('attribution').querySelectorAll('.cc-attribution-refs > li'));
      expect(pills.map(pill => textOf(pill))).toEqual(['tool guides edited on 2026-09-05', 'harness 27 → 28']);
      expect(pills.every(pill => pill.classList.contains('cc-marker-tag') && pill.classList.contains('is-event'))).toBe(true);
      expect(textOf(panel('attribution').querySelector('.cc-attr-unattributed')))
        .toBe('Nothing is attributed to the provider or infrastructure.');
    });

    it('counts a change as decisive only when the endpoint\'s verdict is a change', () => {
      const base = ccAnalysisResult();
      show(ccAnalysisResult({
        endpoints: [
          ccEndpoint('P1'),
          ccEndpoint('P2', { verdict: 'inconclusive', verdictLabel: 'inconclusive', grade: 'notEstablished' }),
          ccEndpoint('P3'),
          ccEndpoint('P4'),
          ccEndpoint('P5')
        ],
        attribution: { ...base.attribution, attributions: [ccAttribution('provider', 'Provider-side latency change')] }
      }));
      expect(panel('attribution').querySelector('.cc-attr-changes')).toBeNull();
      expect(groups().map(group => group.getAttribute('data-side'))).toEqual(['provider']);
      expect(textOf(panel('attribution').querySelector('.cc-attr-unattributed')))
        .toBe('Nothing is attributed to our changes or infrastructure.');
    });

    it('shows only the empty state, and the undetermined evidence, when nothing is decisive or attributed', () => {
      show(ccAnalysisResult({
        endpoints: ['P1', 'P2', 'P3', 'P4', 'P5'].map(id => ccEndpoint(id)),
        attribution: {
          totalChanges: [],
          attributions: [ccAttribution('undetermined', 'Nothing to explain', { evidence: 'No endpoint changed beyond its margin.', endpoints: [] })]
        }
      }));
      expect(textOf(panel('attribution').querySelector('.cc-res-empty'))).toBe('Nothing to attribute: no endpoint shows a decisive change.');
      const evidence = panel('attribution').querySelector<HTMLElement>('.cc-attr-empty-evidence')!;
      expect(textOf(evidence)).toBe('No endpoint changed beyond its margin.');
      expect(evidence.classList).toContain('cc-muted');
      expect(groups().length).toBe(0);
      expect(panel('attribution').querySelector('.cc-attr-changes')).toBeNull();
      expect(panel('attribution').querySelector('.cc-attr-unattributed')).toBeNull();

      show(ccAnalysisResult({ attribution: { totalChanges: [], attributions: [] }, endpoints: ['P1', 'P2'].map(id => ccEndpoint(id)) }));
      expect(panel('attribution').querySelector('.cc-res-empty')).not.toBeNull();
      expect(panel('attribution').querySelector('.cc-attr-empty-evidence')).toBeNull();
    });
  });

  describe('the Details panel', () => {
    const disclosures = () => Array.from(panel('details').querySelectorAll<HTMLDetailsElement>(':scope > .cc-res-details > details.gh-disclosure'));
    const disclosure = (section: string) =>
      panel('details').querySelector<HTMLDetailsElement>(`:scope > .cc-res-details > details[data-section="${section}"]`)!;

    it('has a visually hidden heading, the lead and closed disclosures with plain-text summaries and counts', () => {
      expect(panel('details').querySelector('.gh-section-title')).toBeNull();
      expect(textOf(panel('details').querySelector('h5.visually-hidden'))).toBe('Details');
      expect(textOf(panel('details').querySelector('.cc-res-lead'))).toBe('Background for reading the verdicts. Nothing here changes them.');
      expect(disclosures().map(d => textOf(d.querySelector('summary'))))
        .toEqual(['Events in the analyzed span (1)', 'Limitations (1)', 'Data quality (1)', 'About this analysis']);
      for (const d of disclosures()) {
        expect(d.open).toBe(false);
        expect(d.querySelector('summary')!.children.length).toBe(0);
      }

      show(ccAnalysisResult({ runSelection: ccRunSelectionView() }));
      expect(disclosures().map(d => d.getAttribute('data-section'))).toEqual(['selection', 'events', 'limitations', 'dataQuality', 'about']);
      expect(textOf(disclosure('selection').querySelector('summary'))).toBe('Run selection');
      expect(disclosures().every(d => !d.open)).toBe(true);
    });

    it('lists the limitations and the data quality', () => {
      expect(textOf(disclosure('limitations').querySelector('.cc-res-list'))).toBe('Only one time stratum is common to both periods.');
      expect(textOf(disclosure('dataQuality').querySelector('.cc-res-list'))).toBe('One baseline run has no call telemetry.');

      show(ccAnalysisResult({ limitations: [], dataQuality: [] }));
      expect(textOf(disclosure('limitations').querySelector('summary'))).toBe('Limitations (0)');
      expect(textOf(disclosure('limitations').querySelector('.cc-muted'))).toBe('None recorded.');
      expect(textOf(disclosure('dataQuality').querySelector('.cc-muted'))).toBe('No data-quality notes.');
    });

    it('keeps the server\'s headline under About this analysis, with the identity', () => {
      const rows = Array.from(disclosure('about').querySelectorAll('.cc-res-identity > div'))
        .map(row => [textOf(row.querySelector('dt')), textOf(row.querySelector('dd'))]);
      expect(rows).toEqual([
        ['Analysis', '#7 · September check'],
        ['Headline', ccAnalysisResult().headline],
        ['Saved', '2026-10-02 09:00 UTC'],
        ['Input SHA-256', 'a'.repeat(64)],
        ['Analysis code', 'version 1']
      ]);
      expect(textOf(disclosure('about').querySelector('code.cc-sha'))).toBe('a'.repeat(64));
    });

    it('lists the events in the analyzed span once, with level-6 day headings', () => {
      const lists = Array.from(panel('details').querySelectorAll<HTMLElement>('app-cc-event-list'));
      expect(lists.length).toBe(1);
      expect(disclosure('events').contains(lists[0])).toBe(true);
      expect(textOf(el.querySelector('#cc-res-ev-summary'))).toBe('1 Overseer change on 1 day');
      expect(el.querySelector<HTMLElement>('#cc-res-ev-day-2026-09-15')!.tagName).toBe('H6');
      expect(lists[0].querySelector('h5')).toBeNull();
      expect(textOf(el.querySelector('#cc-res-ev-item-E1 .cc-ev-title'))).toBe('Changes under harness 30');
    });

    it('builds the event list with the timeline\'s harness and E numbers', () => {
      const base = ccAnalysisResult();
      const timelineGroups = groupOverseerEvents(ccOverseerEvents(), ccEventPoints());
      fixture.componentRef.setInput('result', ccAnalysisResult({
        events: ccOverseerEvents(),
        annotations: ccEventAnnotations(),
        // Runs 202 and 203, whose changes make up E1, are not analyzed.
        baseline: { ...base.baseline, runIds: [201, 204] },
        comparison: { ...base.comparison, runIds: [205, 206] }
      }));
      fixture.componentRef.setInput('points', ccEventPoints());
      fixture.componentRef.setInput('eventNumbering', timelineGroups);
      fixture.detectChanges();

      expect(textOf(el.querySelector('#cc-res-ev-summary'))).toBe('4 Overseer changes on 4 days · 2 annotations · 2 served-model changes');
      expect(textOf(disclosure('events').querySelector('summary'))).toBe('Events in the analyzed span (8)');
      const tags = Array.from(panel('details').querySelectorAll('app-cc-event-list li.cc-ev-item')).map(item => item.getAttribute('data-tag'));
      expect(tags).toEqual(['A1', 'E1', 'E2', 'A2', 'S1', 'S2', 'E3', 'E4']);
      // The harness of runs 202 and 203 comes from the full timeline points.
      expect(textOf(el.querySelector('#cc-res-ev-item-E1 .cc-ev-title'))).toBe('Changes under harness 27');
      const events = fixture.componentInstance.eventDays.flatMap(day => day.items)
        .flatMap(item => item.kind === 'event' ? [`${item.group.tag} ${item.group.key}`] : []);
      expect(events).toEqual(timelineGroups.map(group => `${group.tag} ${group.key}`));
    });

    it('keeps the timeline\'s E numbers when the span starts after its first composite', () => {
      const base = ccAnalysisResult();
      const timelineGroups = groupOverseerEvents(ccOverseerEvents(), ccEventPoints());
      // The server sends only the events between the first and the last analyzed run.
      const spanEvents = ccOverseerEvents().filter(event => event.atUtc >= '2026-09-05');
      fixture.componentRef.setInput('result', ccAnalysisResult({
        events: spanEvents,
        annotations: [],
        baseline: { ...base.baseline, runIds: [204, 205] },
        comparison: { ...base.comparison, runIds: [206] }
      }));
      fixture.componentRef.setInput('points', ccEventPoints());
      fixture.componentRef.setInput('eventNumbering', timelineGroups);
      fixture.detectChanges();

      const eventTags = () => Array.from(panel('details').querySelectorAll('app-cc-event-list li.cc-ev-item[data-tag^="E"]')).map(item => item.getAttribute('data-tag'));
      expect(eventTags()).toEqual(['E2', 'E3', 'E4']);

      // Without the timeline's numbering the span would restart at E1.
      fixture.componentRef.setInput('eventNumbering', []);
      fixture.detectChanges();
      expect(eventTags()).toEqual(['E1', 'E2', 'E3']);
    });

    describe('the run selection', () => {
      const section = () => panel('details').querySelector<HTMLElement>('details[data-section="selection"] .cc-res-selection');

      it('is absent for an analysis saved before the selection was recorded', () => {
        expect(section()).toBeNull();
        show(ccAnalysisResult({
          runSelection: ccRunSelectionView({ recorded: false, rangeLabel: null, firstRunId: null, leftOutRunIds: [], unanalyzedRuns: [] })
        }));
        expect(section()).toBeNull();
      });

      it('shows the recorded dates, marks and left-out runs, and the runs not analyzed by reason, first among the details', () => {
        show(ccAnalysisResult({ runSelection: ccRunSelectionView() }));
        const facts = Array.from(section()!.querySelectorAll('.cc-res-selection-facts > div'))
          .map(row => [textOf(row.querySelector('dt')), textOf(row.querySelector('dd'))]);
        expect(facts).toEqual([
          ['Dates', 'Last 30 days · 2026-09-07 09:00 UTC to the last run'],
          ['First run', '#102'],
          ['Last run', 'none'],
          ['Left out in step 1', '#104']
        ]);
        expect(Array.from(section()!.querySelectorAll('.cc-res-unanalyzed li')).map(item => textOf(item)))
          .toEqual(['Left out in step 1: #104 (comparison)', 'Not assigned to a period: #105 (comparison)']);
        expect(disclosures()[0].getAttribute('data-section')).toBe('selection');
      });

      it('says when every usable run in the periods was analyzed', () => {
        show(ccAnalysisResult({ runSelection: ccRunSelectionView({ unanalyzedRuns: [] }) }));
        expect(textOf(section()!.querySelector('.cc-res-all-analyzed'))).toBe('Every usable run of the model in the periods was analyzed.');
      });

      it('lists the unanalyzed runs of a request without a recorded selection', () => {
        show(ccAnalysisResult({
          runSelection: ccRunSelectionView({
            recorded: false,
            unanalyzedRuns: [{ runId: 105, period: 'comparison', startedAtUtc: '2026-09-26T08:00:00Z', reason: 'notSelected' }]
          })
        }));
        expect(section()!.querySelector('.cc-res-selection-facts')).toBeNull();
        expect(textOf(section()!.querySelector('.cc-res-unanalyzed'))).toBe('Not assigned to a period: #105 (comparison)');
      });

      it('names battery runs in a battery analysis, and lists the runs outside the compared set last', () => {
        show(ccAnalysisResult({
          unitKind: 'batteryRun',
          runSelection: ccRunSelectionView({
            firstRunId: null, lastRunId: null, leftOutRunIds: [],
            firstBatteryRunId: 11, lastBatteryRunId: 12, leftOutBatteryRunIds: [13],
            unanalyzedRuns: [
              { runId: 305, period: 'comparison', startedAtUtc: '2026-10-08T12:00:00Z', reason: 'leftOut', batteryRunId: 13 },
              { runId: 106, period: 'comparison', startedAtUtc: '2026-10-01T08:00:00Z', reason: 'outsideComparisonSet', batteryRunId: null }
            ]
          })
        }));
        const facts = Array.from(section()!.querySelectorAll('.cc-res-selection-facts > div'))
          .map(row => [textOf(row.querySelector('dt')), textOf(row.querySelector('dd'))]);
        expect(facts.slice(1)).toEqual([
          ['First run', 'battery run #11'],
          ['Last run', 'battery run #12'],
          ['Left out in step 1', 'battery run #13']
        ]);
        expect(Array.from(section()!.querySelectorAll('.cc-res-unanalyzed li')).map(item => textOf(item))).toEqual([
          'Left out in step 1: #305 (comparison, battery run #13)',
          'Outside the compared set: #106 (comparison)'
        ]);
      });
    });
  });

  describe('the section images', () => {
    const copyButton = () => el.querySelector<HTMLButtonElement>('.cc-res-export > button.action-btn.cc-res-copy')!;
    const downloadButton = () => el.querySelector<HTMLButtonElement>('.cc-res-export > button.action-btn.cc-res-download')!;
    const settingsButton = () => el.querySelector<HTMLButtonElement>('.cc-res-export > button.btn-ghost.cc-res-image-settings')!;
    const status = () => textOf(el.querySelector('p.cc-res-export-status'));
    const imageDialog = () => fixture.debugElement.query(By.directive(CcResultsImageDialogComponent)).componentInstance as CcResultsImageDialogComponent;
    const dialogElement = () => el.querySelector<HTMLDialogElement>('app-cc-results-image-dialog dialog')!;

    beforeEach(() => {
      vi.spyOn(ccResultsImageIo, 'loadImage').mockImplementation(() => Promise.reject(new Error('404')));
      vi.spyOn(ccResultsImageIo, 'now').mockReturnValue(new Date(2026, 9, 10, 9, 45, 12));
    });

    it('hides the headings of Verdicts, Periods, Attribution and Details, keeping them in the outline', () => {
      for (const id of ['verdicts', 'periods', 'attribution', 'details']) {
        const heading = panel(id).querySelector<HTMLElement>(':scope > h5')!;
        expect(heading.classList).toContain('visually-hidden');
        expect(heading.classList).not.toContain('gh-section-title');
      }
      expect(textOf(panel('periods').querySelector(':scope > h5'))).toBe('Periods');
    });

    it('names Copy, Download and Image settings after the shown section, each with a tooltip', () => {
      const group = el.querySelector<HTMLElement>('.cc-res-export')!;
      expect(group.getAttribute('role')).toBe('group');
      expect(group.getAttribute('aria-label')).toBe('Export the Summary section');
      expect(copyButton().getAttribute('aria-label')).toBe('Copy the Summary section of analysis #7 as an image');
      expect(downloadButton().getAttribute('aria-label')).toBe('Download the Summary section of analysis #7 as a PNG image');
      expect(copyButton().getAttribute('interestfor')).toBe('cc-res-copy-tip');
      expect(copyButton().getAttribute('style')).toContain('anchor-name: --cc-res-copy-tip');
      expect(textOf(el.querySelector('#cc-res-copy-tip'))).toBe('Copy the Summary section as an image');
      expect(textOf(el.querySelector('#cc-res-download-tip'))).toBe('Download the Summary section as PNG');
      expect(el.querySelector('#cc-res-download-tip')!.getAttribute('popover')).toBe('hint');
      for (const button of [copyButton(), downloadButton()]) {
        expect(button.getAttribute('type')).toBe('button');
        expect(button.hasAttribute('title')).toBe(false);
        expect(button.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
        expect(textOf(button)).toBe('');
      }
      expect(textOf(settingsButton())).toBe('Image settings');
      expect(settingsButton().getAttribute('aria-haspopup')).toBe('dialog');
      expect(settingsButton().querySelector('svg')).toBeNull();

      tabButton('nextRuns').click();
      fixture.detectChanges();
      expect(group.getAttribute('aria-label')).toBe('Export the Next runs section');
      expect(copyButton().getAttribute('aria-label')).toBe('Copy the Next runs section of analysis #7 as an image');
    });

    it('downloads the shown section and announces it', async () => {
      const save = vi.spyOn(ccResultsImageIo, 'save').mockReturnValue(undefined);
      tabButton('verdicts').click();
      fixture.detectChanges();
      downloadButton().click();
      await vi.waitFor(() => expect(save).toHaveBeenCalledTimes(1), { timeout: 5000 });
      const [blob, fileName] = vi.mocked(save).mock.lastCall!;
      expect(blob.type).toBe('image/png');
      expect(fileName).toBe('chat-consistency-7_gpt-5-high_verdicts_20261010_094512.png');
      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(status()).toBe('Image downloaded.');
      }, { timeout: 5000 });
      expect(el.querySelector('p.cc-res-export-status')!.getAttribute('role')).toBe('status');
    });

    it('marks both buttons aria-disabled while an export runs, and refuses another', async () => {
      let finish: (outcome: 'copied') => void = () => undefined;
      const copy = vi.spyOn(ccResultsImageIo, 'copy').mockImplementation(() => new Promise(resolve => { finish = resolve; }));
      const save = vi.spyOn(ccResultsImageIo, 'save').mockReturnValue(undefined);
      copyButton().click();
      fixture.detectChanges();
      expect(copyButton().getAttribute('aria-disabled')).toBe('true');
      expect(downloadButton().getAttribute('aria-disabled')).toBe('true');

      downloadButton().click();
      await vi.waitFor(() => expect(copy).toHaveBeenCalledTimes(1), { timeout: 5000 });
      expect(vi.mocked(copy).mock.lastCall![0].type).toBe('image/png');
      finish('copied');
      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(status()).toBe('Summary section copied as an image.');
      }, { timeout: 5000 });
      expect(save).not.toHaveBeenCalled();
      expect(copyButton().hasAttribute('aria-disabled')).toBe(false);
      expect(downloadButton().hasAttribute('aria-disabled')).toBe(false);
    });

    it('opens Image settings on the shown section, with the items of this result', () => {
      tabButton('attribution').click();
      fixture.detectChanges();
      settingsButton().click();
      fixture.detectChanges();
      expect(dialogElement().open).toBe(true);
      expect(imageDialog().section).toBe('attribution');
      expect(el.querySelector('#cc-rim-tab-attribution')!.getAttribute('aria-selected')).toBe('true');
      expect(imageDialog().items.verdicts.map(item => item.key)).toContain('endpoint-P2');
      expect(imageDialog().items.attribution.map(item => item.key)).toEqual(['changes', 'groups', 'unattributed']);
    });

    it('stores a change made in the dialog, and names the new format on Download', () => {
      settingsButton().click();
      fixture.detectChanges();
      (el.querySelector('#cc-rim-fmt-format-webp') as HTMLInputElement).click();
      fixture.detectChanges();
      expect(readStoredCcResultsImageSettings().format).toBe('webp');
      expect(downloadButton().getAttribute('aria-label')).toBe('Download the Summary section of analysis #7 as a WebP image');
      expect(textOf(el.querySelector('#cc-res-download-tip'))).toBe('Download the Summary section as WebP');
    });

    it('closes Image settings when the analysis shows another result', () => {
      settingsButton().click();
      fixture.detectChanges();
      expect(dialogElement().open).toBe(true);
      show(ccAnalysisResult({ analysisId: 8 }));
      expect(dialogElement().open).toBe(false);
    });
  });
});
