import { ComponentFixture, TestBed } from '@angular/core/testing';

import { groupOverseerEvents } from '../chat-consistency-events';
import { MINUS } from '../chat-consistency-format';
import {
  ccAnalysisResult,
  ccEndpoint,
  ccEventAnnotations,
  ccEventPoints,
  ccOverseerEvents,
  ccRunSelectionView,
  ccTimeline,
  chatConsistencyTestProviders,
  textOf
} from '../chat-consistency-tab.testing';
import { CcResultsViewComponent, endpointNotes } from './results-view.component';

describe('CcResultsViewComponent', () => {
  let fixture: ComponentFixture<CcResultsViewComponent>;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CcResultsViewComponent],
      providers: chatConsistencyTestProviders()
    }).compileComponents();
    fixture = TestBed.createComponent(CcResultsViewComponent);
    el = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('result', ccAnalysisResult());
    fixture.componentRef.setInput('points', ccTimeline().points);
    fixture.detectChanges();
  });

  afterEach(() => fixture.destroy());

  it('leads with the verdict on the chat, in a status region', () => {
    const first = el.firstElementChild as HTMLElement;
    expect(first.classList).toContain('cc-headline');
    expect(first.getAttribute('role')).toBe('status');
    expect(textOf(first.querySelector('.cc-headline-text'))).toBe(ccAnalysisResult().headline);
  });

  it('lists P1 to P5 with estimate, verdict, grade, detectable effect and notes', () => {
    const rows = Array.from(el.querySelectorAll<HTMLTableRowElement>('.cc-verdict-table tbody tr'));
    expect(rows.map(row => row.getAttribute('data-endpoint'))).toEqual(['P1', 'P2', 'P3', 'P4', 'P5']);
    const p1 = rows[0].querySelectorAll('td');
    expect(textOf(p1[0])).toBe(`+0.5 index points (95 % CI [${MINUS}1.0, +2.0])`);
    expect(textOf(p1[1])).toBe('Equivalent');
    expect(textOf(p1[2])).toBe('Established');
    expect(textOf(p1[3])).toBe('±1.8 index points');
    expect(textOf(p1[4])).toBe('Native grades; no common grader covers every run.');
    const p2 = rows[1].querySelectorAll('td');
    expect(textOf(p2[0])).toBe(`+18.0 % (95 % CI [${MINUS}4.0, +8.0] %)`);
    expect(textOf(p2[1])).toBe('Degraded');
    expect(textOf(p2[2])).toBe('Indicated');
    expect(textOf(p2[4])).toContain('legacy proxy');
  });

  it('groups the attribution cards as Our changes, Provider, Infrastructure and Undetermined', () => {
    const groups = Array.from(el.querySelectorAll<HTMLElement>('.cc-attribution-group'));
    expect(groups.map(group => textOf(group.querySelector('.cc-attribution-title'))))
      .toEqual(['Our changes', 'Provider', 'Infrastructure', 'Undetermined']);
    const labels = groups.map(group => Array.from(group.querySelectorAll('.cc-attribution-label')).map(label => textOf(label)));
    expect(labels).toEqual([['Tool guides edit'], ['Provider-side latency change'], [], ['Unexplained work shift']]);
    expect(textOf(groups[2].querySelector('.cc-attribution-none'))).toBe('None.');
    // The headline precedes the attributions in reading order.
    const headline = el.querySelector('.cc-headline')!;
    expect(headline.compareDocumentPosition(groups[0]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('offers Repeat this run\'s setup for a next run that names one', () => {
    const repeated: number[] = [];
    fixture.componentInstance.repeatSetup.subscribe(id => repeated.push(id));
    const items = Array.from(el.querySelectorAll<HTMLElement>('.cc-next-run'));
    expect(items.length).toBe(2);
    expect(items[1].querySelector('.cc-next-run-repeat')).toBeNull();
    const button = items[0].querySelector<HTMLButtonElement>('.cc-next-run-repeat')!;
    expect(textOf(button)).toBe('Repeat this run\'s setup');
    button.click();
    expect(repeated).toEqual([205]);
  });

  it('draws the charts over the analysis\'s runs only, and shows limitations, data quality and identity', () => {
    const figures = Array.from(el.querySelectorAll('figure.cc-figure')).map(f => f.getAttribute('data-figure'));
    expect(figures).toEqual(['quality', 'ttfat', 'rate', 'work', 'cost', 'timeline']);
    expect(textOf(el.querySelector('figure[data-figure="quality"] figcaption'))).toContain('across 6 runs');
    expect(textOf(el.querySelector('.cc-res-list'))).toBe('Only one time stratum is common to both periods.');
    expect(textOf(el.querySelector('.cc-res-identity'))).toContain('#7');
    expect(textOf(el.querySelector('.cc-sha'))).toBe('a'.repeat(64));
  });

  it('draws the figures in one column, each in a 352 px box, even when wide', () => {
    document.body.appendChild(el);
    try {
      el.style.inlineSize = '1280px';
      const figures = Array.from(el.querySelectorAll<HTMLElement>('.cc-res-figures > app-cc-chart-figure'));
      expect(figures.length).toBe(6);
      const boxes = figures.map(figure => figure.getBoundingClientRect());
      expect(new Set(boxes.map(box => Math.round(box.left))).size).toBe(1);
      for (let i = 1; i < boxes.length; i++) {
        expect(boxes[i].top).toBeGreaterThan(boxes[i - 1].top);
      }
      const columns = getComputedStyle(el.querySelector('.cc-res-figures')!).gridTemplateColumns.trim().split(/\s+/);
      expect(columns.length).toBe(1);
      for (const figure of figures) {
        expect(figure.querySelector<HTMLElement>('.cc-chart-box')!.style.blockSize).toBe('352px');
      }
    } finally {
      el.remove();
    }
  });

  it('sums up each figure\'s markers in one line instead of a marker list', () => {
    expect(el.querySelector('.cc-marker-list')).toBeNull();
    expect(el.querySelector('.cc-marker')).toBeNull();
    const figures = Array.from(el.querySelectorAll<HTMLElement>('figure.cc-figure'));
    expect(figures.length).toBe(6);
    for (const figure of figures) {
      const key = figure.getAttribute('data-figure');
      const lines = figure.querySelectorAll('p.cc-figure-markers');
      expect(lines.length).toBe(1);
      expect(textOf(lines[0])).toBe('Markers: E1 (Overseer change)');
      expect(textOf(lines[0].querySelector('.cc-marker-tag.is-event'))).toBe('E1');
      // The results do not listen for Show events: the event list is on the same page.
      expect(figure.querySelector('.cc-figure-show-events')).toBeNull();
      // The markers stay available to assistive technology, as the canvas's description.
      const hidden = figure.querySelector<HTMLElement>(`ul#cc-res-fig-${key}-markers`)!;
      expect(hidden.classList).toContain('visually-hidden');
      const canvas = figure.querySelector('canvas');
      if (canvas) {
        expect(canvas.getAttribute('aria-describedby')).toBe(hidden.id);
      }
    }
  });

  it('lists the events in the analyzed span once, after the charts, with level-6 day headings', () => {
    const lists = Array.from(el.querySelectorAll<HTMLElement>('app-cc-event-list'));
    expect(lists.length).toBe(1);
    const heading = lists[0].previousElementSibling as HTMLElement;
    expect(heading.tagName).toBe('H5');
    expect(heading.classList).toContain('cc-res-heading');
    expect(textOf(heading)).toBe('Events in the analyzed span');
    const figures = el.querySelector('.cc-res-figures')!;
    expect(figures.compareDocumentPosition(lists[0]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    expect(textOf(el.querySelector('#cc-res-ev-summary'))).toBe('1 Overseer change on 1 day');
    const dayHeading = el.querySelector<HTMLElement>('#cc-res-ev-day-2026-09-15')!;
    expect(dayHeading.tagName).toBe('H6');
    expect(lists[0].querySelector('h5')).toBeNull();
    expect(textOf(el.querySelector('#cc-res-ev-item-E1 .cc-ev-title'))).toBe('Changes under harness 30');
  });

  it('builds the event list with the timeline\'s harness and E numbers, and draws the analysis\'s runs only', () => {
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
    const days = Array.from(el.querySelectorAll('app-cc-event-list section.cc-ev-day')).map(day => day.getAttribute('data-day'));
    expect(days).toEqual(['2026-09-02', '2026-09-03', '2026-09-05', '2026-09-08', '2026-09-09', '2026-09-10']);
    const tags = Array.from(el.querySelectorAll('app-cc-event-list li.cc-ev-item')).map(item => item.getAttribute('data-tag'));
    expect(tags).toEqual(['A1', 'E1', 'E2', 'A2', 'S1', 'S2', 'E3', 'E4']);
    // The harness of runs 202 and 203 comes from the full timeline points.
    expect(textOf(el.querySelector('#cc-res-ev-item-E1 .cc-ev-title'))).toBe('Changes under harness 27');
    const events = fixture.componentInstance.eventDays.flatMap(day => day.items)
      .flatMap(item => item.kind === 'event' ? [`${item.group.tag} ${item.group.key}`] : []);
    expect(events).toEqual(timelineGroups.map(group => `${group.tag} ${group.key}`));

    const quality = fixture.componentInstance.figures[0];
    expect(quality.table.rows.map(row => row[0])).toEqual(['#201', '#204', '#205', '#206']);
    expect(quality.markers.filter(m => m.kind === 'event').map(m => m.tag)).toEqual(['E1', 'E2', 'E3', 'E4']);
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

    const tags = Array.from(el.querySelectorAll('app-cc-event-list li.cc-ev-item[data-tag^="E"]')).map(item => item.getAttribute('data-tag'));
    expect(tags).toEqual(['E2', 'E3', 'E4']);
    for (const figure of fixture.componentInstance.figures) {
      expect(figure.markers.filter(m => m.kind === 'event').map(m => m.tag)).toEqual(['E2', 'E3', 'E4']);
    }

    // Without the timeline's numbering the span would restart at E1.
    fixture.componentRef.setInput('eventNumbering', []);
    fixture.detectChanges();
    expect(Array.from(el.querySelectorAll('app-cc-event-list li.cc-ev-item[data-tag^="E"]')).map(item => item.getAttribute('data-tag')))
      .toEqual(['E1', 'E2', 'E3']);
  });

  it('notes the common grader and relaxed pooling', () => {
    expect(endpointNotes(ccEndpoint('P1', { commonGrader: true, relaxedPooling: true })))
      .toEqual(['Graded by a common grader.', 'Pooled across a measurement segment boundary.']);
    expect(endpointNotes(ccEndpoint('P3', { computed: false, notComputedReason: 'No common stratum.' })))
      .toEqual(['No common stratum.']);
  });

  describe('the run selection', () => {
    const section = () => el.querySelector<HTMLElement>('.cc-res-selection');

    it('is absent for an analysis saved before the selection was recorded', () => {
      expect(section()).toBeNull();
      fixture.componentRef.setInput('result', ccAnalysisResult({
        runSelection: ccRunSelectionView({ recorded: false, rangeLabel: null, firstRunId: null, leftOutRunIds: [], unanalyzedRuns: [] })
      }));
      fixture.detectChanges();
      expect(section()).toBeNull();
    });

    it('shows the recorded dates, marks and left-out runs, and the runs not analyzed by reason, before the limitations', () => {
      fixture.componentRef.setInput('result', ccAnalysisResult({ runSelection: ccRunSelectionView() }));
      fixture.detectChanges();
      const facts = Array.from(section()!.querySelectorAll('.cc-res-selection-facts > div'))
        .map(row => [textOf(row.querySelector('dt')), textOf(row.querySelector('dd'))]);
      expect(facts).toEqual([
        ['Dates', 'Last 30 days · 2026-09-07 09:00 UTC to the last run'],
        ['First run', '#102'],
        ['Last run', 'none'],
        ['Left out in step 1', '#104']
      ]);
      expect(Array.from(section()!.querySelectorAll('.cc-res-unanalyzed li')).map(item => textOf(item)))
        .toEqual(['Left out in step 1: #104 (comparison)', 'Not selected in step 4: #105 (comparison)']);
      const headings = Array.from(el.querySelectorAll('.cc-res-heading')).map(heading => textOf(heading));
      expect(headings.indexOf('Run selection')).toBe(headings.indexOf('Limitations') - 1);
    });

    it('says when every usable run in the periods was analyzed', () => {
      fixture.componentRef.setInput('result', ccAnalysisResult({ runSelection: ccRunSelectionView({ unanalyzedRuns: [] }) }));
      fixture.detectChanges();
      expect(textOf(section()!.querySelector('.cc-res-all-analyzed'))).toBe('Every usable run of the model in the periods was analyzed.');
    });

    it('lists the unanalyzed runs of a request without a recorded selection', () => {
      fixture.componentRef.setInput('result', ccAnalysisResult({
        runSelection: ccRunSelectionView({
          recorded: false,
          unanalyzedRuns: [{ runId: 105, period: 'comparison', startedAtUtc: '2026-09-26T08:00:00Z', reason: 'notSelected' }]
        })
      }));
      fixture.detectChanges();
      expect(section()!.querySelector('.cc-res-selection-facts')).toBeNull();
      expect(textOf(section()!.querySelector('.cc-res-unanalyzed'))).toBe('Not selected in step 4: #105 (comparison)');
    });
  });
});
