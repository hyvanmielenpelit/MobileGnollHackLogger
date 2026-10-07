import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MINUS } from '../chat-consistency-format';
import { ccAnalysisResult, ccEndpoint, ccTimeline, chatConsistencyTestProviders, textOf } from '../chat-consistency-tab.testing';
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

  it('notes the common grader and relaxed pooling', () => {
    expect(endpointNotes(ccEndpoint('P1', { commonGrader: true, relaxedPooling: true })))
      .toEqual(['Graded by a common grader.', 'Pooled across a measurement segment boundary.']);
    expect(endpointNotes(ccEndpoint('P3', { computed: false, notComputedReason: 'No common stratum.' })))
      .toEqual(['No common stratum.']);
  });
});
