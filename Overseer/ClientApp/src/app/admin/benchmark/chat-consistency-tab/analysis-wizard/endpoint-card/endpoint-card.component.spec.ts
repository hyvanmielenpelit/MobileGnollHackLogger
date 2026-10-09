import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ccIntervalGeometry } from '../../chat-consistency-results';
import { CcEndpointResult } from '../../chat-consistency.models';
import { ccEndpoint, textOf } from '../../chat-consistency-tab.testing';
import { CcEndpointCardComponent, intervalEnds } from './endpoint-card.component';

/** Work per turn, inconclusive: −7.0 % with a 95 % interval reaching past the −15 % margin. */
function inconclusiveP4(overrides: Partial<CcEndpointResult> = {}): CcEndpointResult {
  return ccEndpoint('P4', {
    estimatePercent: -7,
    ci95Percent: { lower: -19.7, upper: 6.8 },
    verdict: 'inconclusive',
    verdictLabel: 'inconclusive',
    grade: 'notEstablished',
    gradeReasons: ['Inconclusive: the interval reaches beyond the margin.'],
    minimumDetectableEffectPercent: 9,
    baselineRunCount: 1,
    comparisonRunCount: 1,
    itemCount: 32,
    ...overrides
  });
}

/** Time to first answer text with five notes and two robustness checks. */
function notedP2(): CcEndpointResult {
  return ccEndpoint('P2', {
    verdict: 'changedDegraded',
    verdictLabel: 'degraded',
    grade: 'indicated',
    legacyProxy: true,
    commonGrader: true,
    relaxedPooling: true,
    minimumSampleMet: false,
    minimumSampleDetail: '2 runs on 2 days per period',
    gradeReasons: ['Grader stability failed.'],
    robustnessChecks: [
      { endpointId: 'P2', name: 'Leave one run out', status: 'failed', detail: 'Dropping run 104 changes the verdict.' },
      { endpointId: 'P2', name: 'Sign check', status: 'notAssessable', detail: '' }
    ]
  });
}

describe('CcEndpointCardComponent', () => {
  let fixture: ComponentFixture<CcEndpointCardComponent>;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CcEndpointCardComponent] }).compileComponents();
    fixture = TestBed.createComponent(CcEndpointCardComponent);
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => fixture.destroy());

  function show(endpoint: CcEndpointResult, batteryAnalysis = false): void {
    fixture.componentRef.setInput('endpoint', endpoint);
    fixture.componentRef.setInput('batteryAnalysis', batteryAnalysis);
    fixture.detectChanges();
  }

  function card(): HTMLElement | null {
    return el.querySelector('article.cc-ep-card');
  }

  function fact(key: string): HTMLElement | null {
    return el.querySelector(`dl.cc-ep-facts > div[data-fact="${key}"]`);
  }

  function percentOf(element: Element | null, property: 'left' | 'width'): number {
    return parseFloat((element as HTMLElement | null)?.style[property] ?? '');
  }

  it('is an article the banner can focus, named by the endpoint', () => {
    show(inconclusiveP4());
    const article = card();
    expect(article?.id).toBe('cc-ep-P4');
    expect(article?.getAttribute('tabindex')).toBe('-1');
    expect(article?.getAttribute('data-endpoint')).toBe('P4');
    expect(article?.getAttribute('data-status')).toBe('inconclusive');
    expect(article?.getAttribute('aria-labelledby')).toBe('cc-ep-P4-name');
    expect(textOf(el.querySelector('h6.cc-ep-name#cc-ep-P4-name'))).toBe('Work per turn');
    expect(textOf(el.querySelector('.cc-ep-head .gh-tag.cc-ep-id'))).toBe('P4');
    expect(textOf(el.querySelector('.cc-ep-margin'))).toBe('Margin ±15 %');
  });

  it('shows the status with its icon, and the grade only when it is not Not established', () => {
    show(inconclusiveP4());
    const status = el.querySelector('.cc-ep-status');
    expect(status?.getAttribute('data-status')).toBe('inconclusive');
    expect(textOf(status)).toBe('Inconclusive');
    expect(status?.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(el.querySelector('.cc-ep-grade')).toBeNull();

    show(ccEndpoint('P1'));
    expect(textOf(el.querySelector('.cc-ep-status'))).toBe('Within margin');
    expect(el.querySelector('.cc-ep-status svg polyline')).not.toBeNull();
    const grade = el.querySelector('.cc-ep-grade');
    expect(grade?.getAttribute('data-grade')).toBe('established');
    expect(textOf(grade)).toBe('Grade: Established');

    show(notedP2());
    expect(textOf(el.querySelector('.cc-ep-status'))).toBe('Changed');
    expect(el.querySelector('.cc-ep-status svg path')).not.toBeNull();
    expect(textOf(el.querySelector('.cc-ep-grade'))).toBe('Grade: Indicated');
  });

  it('shows the estimate large and its 95 % interval under it', () => {
    show(inconclusiveP4());
    expect(textOf(el.querySelector('p.cc-ep-estimate'))).toBe('−7.0 %');
    expect(textOf(el.querySelector('p.interval'))).toBe('95 % interval −19.7 to +6.8 %');

    show(ccEndpoint('P1'));
    expect(textOf(el.querySelector('p.cc-ep-estimate'))).toBe('+0.5 index points');
    expect(textOf(el.querySelector('p.interval'))).toBe('95 % interval −1.0 to +2.0 index points');
  });

  it('draws the interval bar as one image named by its reading, with its end captions hidden', () => {
    const endpoint = inconclusiveP4();
    show(endpoint);
    const geometry = ccIntervalGeometry(endpoint)!;
    const bar = el.querySelector('div.cc-ep-interval[role="img"]');
    expect(bar?.getAttribute('aria-label'))
      .toBe('Estimate −7.0 %, 95 % interval −19.7 % to +6.8 %, margin ±15 %. The interval reaches beyond the margin.');
    expect(bar?.classList.contains('is-crossing')).toBe(true);
    expect(percentOf(bar?.querySelector('.cc-ep-band') ?? null, 'left')).toBeCloseTo(geometry.marginStart, 3);
    expect(percentOf(bar?.querySelector('.cc-ep-band') ?? null, 'width')).toBeCloseTo(geometry.marginEnd - geometry.marginStart, 3);
    expect(percentOf(bar?.querySelector('.cc-ep-zero') ?? null, 'left')).toBeCloseTo(50, 3);
    expect(percentOf(bar?.querySelector('.cc-ep-line') ?? null, 'left')).toBeCloseTo(geometry.lower, 3);
    expect(percentOf(bar?.querySelector('.cc-ep-line') ?? null, 'width')).toBeCloseTo(geometry.upper - geometry.lower, 3);
    expect(percentOf(bar?.querySelector('.cc-ep-dot') ?? null, 'left')).toBeCloseTo(geometry.estimate, 3);

    const ends = el.querySelector('.cc-ep-ends');
    expect(ends?.getAttribute('aria-hidden')).toBe('true');
    expect(textOf(ends?.querySelector('.cc-ep-end-start'))).toBe('less work');
    expect(textOf(ends?.querySelector('.cc-ep-end-end'))).toBe('more work');

    show(ccEndpoint('P1'));
    expect(el.querySelector('.cc-ep-interval')?.getAttribute('aria-label'))
      .toBe('Estimate +0.5 index points, 95 % interval −1.0 to +2.0 index points, margin ±3 index points. '
        + 'The whole interval lies inside the margin.');
    expect(el.querySelector('.cc-ep-interval')?.classList.contains('is-within')).toBe(true);
    expect(textOf(el.querySelector('.cc-ep-end-start'))).toBe('worse');
    expect(textOf(el.querySelector('.cc-ep-end-end'))).toBe('better');
  });

  it('names the ends of each direction', () => {
    expect(intervalEnds('higherIsBetter')).toEqual({ start: 'worse', end: 'better' });
    expect(intervalEnds('lowerIsBetter')).toEqual({ start: 'better', end: 'worse' });
    expect(intervalEnds('work')).toEqual({ start: 'less work', end: 'more work' });
  });

  it('draws no bar and no interval line when the interval is missing', () => {
    show(ccEndpoint('P1', { ci95: null }));
    expect(textOf(el.querySelector('p.cc-ep-estimate'))).toBe('+0.5 index points');
    expect(el.querySelector('p.interval')).toBeNull();
    expect(el.querySelector('.cc-ep-interval')).toBeNull();
    expect(el.querySelector('.cc-ep-ends')).toBeNull();
  });

  it('says what the verdict means and, only when inconclusive, the smallest detectable change', () => {
    show(inconclusiveP4());
    const keys = Array.from(el.querySelectorAll('dl.cc-ep-facts > div')).map(div => div.getAttribute('data-fact'));
    expect(keys).toEqual(['meaning', 'mde', 'compared']);
    expect(textOf(fact('meaning')?.querySelector('dt'))).toBe('What it means');
    expect(textOf(fact('meaning')?.querySelector('dd'))).toBe('The interval reaches beyond the margin.');
    expect(textOf(fact('mde')?.querySelector('dt'))).toBe('Smallest change this sample can detect');
    expect(textOf(fact('mde')?.querySelector('dd'))).toBe('±9.0 %');

    show(ccEndpoint('P1'));
    expect(fact('meaning')).toBeNull();
    expect(fact('mde')).toBeNull();
    expect(fact('compared')).not.toBeNull();
  });

  it('counts the compared runs and paired items, as member runs in a battery analysis', () => {
    show(inconclusiveP4());
    expect(textOf(fact('compared')?.querySelector('dt'))).toBe('Compared');
    expect(textOf(fact('compared')?.querySelector('dd'))).toBe('1 run vs 1 run · 32 paired items');

    show(ccEndpoint('P1'), true);
    expect(textOf(fact('compared')?.querySelector('dd'))).toBe('3 member runs vs 3 member runs · 60 paired items');

    show(ccEndpoint('P1', { itemCount: 0 }));
    expect(textOf(fact('compared')?.querySelector('dd'))).toBe('3 runs vs 3 runs');
  });

  it('shows two notes, the sample shortfall first with a warning icon, and the rest with the checks under More', () => {
    show(notedP2());
    const notes = Array.from(el.querySelectorAll('ul.cc-ep-notes[role="list"] > li.cc-ep-note'));
    expect(notes.map(note => textOf(note))).toEqual([
      'Below the minimum sample: 2 runs on 2 days per period',
      'Measured with the legacy proxy (model time per answer), not telemetry.'
    ]);
    expect(notes[0].classList.contains('is-shortfall')).toBe(true);
    expect(notes[0].querySelector('svg path')).not.toBeNull();
    expect(notes[1].classList.contains('is-shortfall')).toBe(false);
    expect(notes.every(note => note.querySelector('svg')?.getAttribute('aria-hidden') === 'true')).toBe(true);

    const more = el.querySelector<HTMLDetailsElement>('details.gh-disclosure.cc-ep-more');
    expect(more).not.toBeNull();
    expect(more?.open).toBe(false);
    expect(textOf(more?.querySelector('summary'))).toBe('More about P2 (5)');
    expect(Array.from(more?.querySelectorAll('.cc-ep-more-notes > li') ?? []).map(li => textOf(li))).toEqual([
      'Graded by a common grader.',
      'Pooled across a measurement segment boundary.',
      'Grader stability failed.'
    ]);
    const checks = Array.from(more?.querySelectorAll('ul.cc-ep-checks > li.cc-ep-check') ?? []);
    expect(checks.map(check => check.getAttribute('data-status'))).toEqual(['failed', 'notAssessable']);
    expect(checks.map(check => textOf(check.querySelector('.cc-ep-check-name')))).toEqual(['Leave one run out', 'Sign check']);
    expect(checks.map(check => textOf(check.querySelector('.cc-ep-check-word')))).toEqual(['Failed', 'Not assessable']);
    expect(textOf(checks[0].querySelector('.cc-ep-check-detail'))).toBe('Dropping run 104 changes the verdict.');
    expect(checks[1].querySelector('.cc-ep-check-detail')).toBeNull();
  });

  it('puts passed robustness checks alone under More when the notes fit', () => {
    show(ccEndpoint('P1', {
      robustnessChecks: [{ endpointId: 'P1', name: 'Grader stability', status: 'passed', detail: 'A common grader covers every run.' }]
    }));
    expect(el.querySelectorAll('li.cc-ep-note').length).toBe(1);
    const more = el.querySelector('details.cc-ep-more');
    expect(textOf(more?.querySelector('summary'))).toBe('More about P1 (1)');
    expect(more?.querySelector('.cc-ep-more-notes')).toBeNull();
    expect(textOf(more?.querySelector('.cc-ep-check-word'))).toBe('Passed');
  });

  it('has no More disclosure when every note shows and there is no check', () => {
    show(ccEndpoint('P1'));
    expect(Array.from(el.querySelectorAll('li.cc-ep-note')).map(note => textOf(note)))
      .toEqual(['Native grades; no common grader covers every run.']);
    expect(el.querySelector('details.cc-ep-more')).toBeNull();

    show(ccEndpoint('P3'));
    expect(el.querySelector('ul.cc-ep-notes')).toBeNull();
    expect(el.querySelector('details.cc-ep-more')).toBeNull();
  });
});
