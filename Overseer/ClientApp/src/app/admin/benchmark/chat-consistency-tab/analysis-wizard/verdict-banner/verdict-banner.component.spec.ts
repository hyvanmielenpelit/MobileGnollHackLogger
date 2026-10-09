import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CcAnalysisResult } from '../../chat-consistency.models';
import { ccAnalysisResult, ccEndpoint, ccSubjectWithLevel, textOf } from '../../chat-consistency-tab.testing';
import { CcVerdictBannerComponent } from './verdict-banner.component';

/** P1 inconclusive, P2 and P3 not computable, P4 less work, P5 equivalent. */
function undecidedResult(): CcAnalysisResult {
  return ccAnalysisResult({
    endpoints: [
      ccEndpoint('P1', { verdict: 'inconclusive', verdictLabel: 'inconclusive', grade: 'notEstablished' }),
      ccEndpoint('P2', {
        computed: false, notComputedReason: 'The periods share no time-of-week stratum.', verdict: null, verdictLabel: '', grade: 'notEstablished'
      }),
      ccEndpoint('P3', {
        computed: false, notComputedReason: 'The periods share no time-of-week stratum.', verdict: null, verdictLabel: '', grade: 'notEstablished'
      }),
      ccEndpoint('P4', { verdict: 'changedNegligible', verdictLabel: 'changed, negligible' }),
      ccEndpoint('P5')
    ]
  });
}

describe('CcVerdictBannerComponent', () => {
  let fixture: ComponentFixture<CcVerdictBannerComponent>;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CcVerdictBannerComponent] }).compileComponents();
    fixture = TestBed.createComponent(CcVerdictBannerComponent);
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => fixture.destroy());

  function show(result: CcAnalysisResult): void {
    fixture.componentRef.setInput('result', result);
    fixture.detectChanges();
  }

  function chip(id: string): HTMLButtonElement | null {
    return el.querySelector(`button.cc-vb-endpoint[data-endpoint="${id}"]`);
  }

  function scopeFact(key: string): HTMLElement | null {
    return el.querySelector(`#cc-vb-scope-tip div[data-fact="${key}"]`);
  }

  function protocolFact(key: string): HTMLElement | null {
    return el.querySelector(`#cc-vb-protocol-tip div[data-fact="${key}"]`);
  }

  it('is a section named by its title, under the eyebrow', () => {
    show(ccAnalysisResult());
    const section = el.querySelector('section.cc-vb');
    expect(section?.getAttribute('aria-labelledby')).toBe('cc-vb-title');
    expect(textOf(section?.querySelector('p.cc-vb-eyebrow'))).toBe('Verdict on the Overseer chat');
    expect(section?.querySelector('h5.cc-vb-title#cc-vb-title')).not.toBeNull();
  });

  it('titles a decisive change by its endpoints, verdicts and grades', () => {
    show(ccAnalysisResult());
    expect(el.querySelector('section.cc-vb')?.getAttribute('data-outcome')).toBe('changed');
    expect(textOf(el.querySelector('.cc-vb-title'))).toBe('The chat changed');
    expect(textOf(el.querySelector('p.cc-vb-detail'))).toBe('Time to first answer text: degraded (indicated)');
    const icon = el.querySelector('.cc-vb-title svg');
    expect(icon?.getAttribute('aria-hidden')).toBe('true');
    expect(icon?.querySelector('path')).not.toBeNull();
  });

  it('titles no change, an undecided analysis and one with nothing computed', () => {
    const allWithin = ccAnalysisResult({ endpoints: ['P1', 'P2', 'P3', 'P4', 'P5'].map(id => ccEndpoint(id)) });
    show(allWithin);
    expect(el.querySelector('section.cc-vb')?.getAttribute('data-outcome')).toBe('noChange');
    expect(textOf(el.querySelector('.cc-vb-title'))).toBe('No meaningful change');
    expect(textOf(el.querySelector('.cc-vb-detail'))).toBe('5 of 5 endpoints within their margins');
    expect(el.querySelector('.cc-vb-title svg polyline')).not.toBeNull();

    show(undecidedResult());
    expect(el.querySelector('section.cc-vb')?.getAttribute('data-outcome')).toBe('undecided');
    expect(textOf(el.querySelector('.cc-vb-title'))).toBe('Not enough evidence yet');
    expect(textOf(el.querySelector('.cc-vb-detail'))).toBe('3 of 5 endpoints computed: 2 within their margins, 1 inconclusive');
    expect(el.querySelector('.cc-vb-title svg line')).not.toBeNull();
    expect(el.querySelector('.cc-vb-title svg polyline')).toBeNull();

    const none = ccAnalysisResult({
      endpoints: ['P1', 'P2', 'P3', 'P4', 'P5'].map(id => ccEndpoint(id, {
        computed: false, notComputedReason: 'No run is eligible.', verdict: null, verdictLabel: '', grade: 'notEstablished'
      }))
    });
    show(none);
    expect(el.querySelector('section.cc-vb')?.getAttribute('data-outcome')).toBe('noneComputed');
    expect(textOf(el.querySelector('.cc-vb-title'))).toBe('Nothing could be computed');
    expect(textOf(el.querySelector('.cc-vb-detail'))).toBe('See why under Verdicts');
  });

  it('shows the model without its thinking level, which is a badge beside it, then the model id', () => {
    show(ccAnalysisResult({ subject: ccSubjectWithLevel() }));
    const subject = el.querySelector('div.cc-vb-subject');
    expect(textOf(subject?.querySelector('.cc-vb-model'))).toBe('GPT-5');
    expect(subject?.querySelector('app-cc-model-badges')).not.toBeNull();
    expect(textOf(subject?.querySelector('.thinking-badge'))).toBe('thinking level High');
    expect(textOf(subject?.querySelector('.provider-badge'))).toBe('OpenAI');
    expect(textOf(subject?.querySelector('.cc-vb-model-id'))).toBe('gpt-5');

    show(ccAnalysisResult({ subject: ccSubjectWithLevel({ displayName: 'GPT-5 (custom)' }) }));
    expect(textOf(el.querySelector('.cc-vb-model'))).toBe('GPT-5 (custom)');
  });

  it('tags the compared set by its kind and names it, and shows none without one', () => {
    show(ccAnalysisResult({ comparisonSet: { kind: 'battery', key: 'battery:x', label: 'Two initial suites (revision 1)' } }));
    const tag = el.querySelector('.cc-vb-subject span.gh-tag.cc-kind-tag');
    expect(tag?.getAttribute('data-kind')).toBe('battery');
    expect(textOf(tag)).toBe('Battery');
    expect(textOf(el.querySelector('.cc-vb-compared-label'))).toBe('Two initial suites (revision 1)');

    show(ccAnalysisResult({ comparisonSet: { kind: 'suite', key: 'suite:id:5', label: 'Board Suite' } }));
    expect(textOf(el.querySelector('.cc-kind-tag'))).toBe('Suite');

    show(ccAnalysisResult({ comparisonSet: null }));
    expect(el.querySelector('.cc-kind-tag')).toBeNull();
  });

  it('shows one chip per endpoint with its status word, icon and an accessible name', () => {
    show(undecidedResult());
    const chips = Array.from(el.querySelectorAll('ul.cc-vb-endpoints[role="list"] > li > button.cc-vb-endpoint'));
    expect(chips.map(button => button.getAttribute('data-endpoint'))).toEqual(['P1', 'P2', 'P3', 'P4', 'P5']);
    expect(chips.map(button => button.getAttribute('type'))).toEqual(['button', 'button', 'button', 'button', 'button']);
    expect(chips.map(button => button.getAttribute('data-status')))
      .toEqual(['inconclusive', 'notComputable', 'notComputable', 'within', 'within']);
    expect(chips.map(button => textOf(button.querySelector('.cc-vb-endpoint-word'))))
      .toEqual(['Inconclusive', 'Not computable', 'Not computable', 'Within margin', 'Within margin']);
    expect(textOf(chip('P1')?.querySelector('.cc-vb-endpoint-label'))).toBe('P1 Quality');
    expect(chip('P2')?.getAttribute('aria-label')).toBe('P2 Time to first answer text: Not computable. Show its verdict.');
    expect(chip('P4')?.getAttribute('aria-label')).toBe('P4 Work per turn: Within margin. Show its verdict.');
    expect(chips.every(button => button.querySelector('svg')?.getAttribute('aria-hidden') === 'true')).toBe(true);
    expect(chip('P4')?.querySelector('svg polyline')).not.toBeNull();
  });

  it('reads a change of work per turn as the server\'s more or less work, and a degradation as Changed', () => {
    const result = ccAnalysisResult();
    result.endpoints[3] = ccEndpoint('P4', { verdict: 'changedImproved', verdictLabel: 'less work', grade: 'indicated' });
    show(result);
    expect(chip('P4')?.getAttribute('data-status')).toBe('changed');
    expect(textOf(chip('P4')?.querySelector('.cc-vb-endpoint-word'))).toBe('Less work');
    expect(chip('P4')?.getAttribute('aria-label')).toBe('P4 Work per turn: Less work. Show its verdict.');
    expect(textOf(chip('P2')?.querySelector('.cc-vb-endpoint-word'))).toBe('Changed');
    expect(chip('P2')?.querySelector('svg path')).not.toBeNull();
  });

  it('emits the endpoint id when a chip is pressed', () => {
    show(undecidedResult());
    const selected: string[] = [];
    fixture.componentInstance.endpointSelected.subscribe(id => selected.push(id));
    chip('P3')?.click();
    chip('P1')?.click();
    expect(selected).toEqual(['P3', 'P1']);
  });

  it('shows the scope with an info dialog of the sampled blocks and hours', () => {
    show(ccAnalysisResult());
    const item = el.querySelector('.cc-vb-meta > div[data-meta="scope"]');
    expect(textOf(item?.querySelector('dt'))).toBe('Scope');
    expect(textOf(item?.querySelector('.cc-vb-meta-value'))).toBe('weekdays 08–12 UTC');
    const button = item?.querySelector('app-info-tip button.gh-info-btn');
    expect(button?.getAttribute('aria-label')).toBe('About Scope');
    expect(button?.getAttribute('aria-haspopup')).toBe('dialog');
    expect(textOf(el.querySelector('#cc-vb-scope-tip-title'))).toBe('About the scope');

    expect(textOf(el.querySelector('#cc-vb-scope-tip > p'))).toContain('4-hour UTC blocks, weekday or weekend');
    expect(textOf(scopeFact('blocks')?.querySelector('dt'))).toBe('Blocks both periods sampled');
    expect(textOf(scopeFact('blocks')?.querySelector('dd'))).toBe('weekday 08–12 UTC');
    expect(textOf(scopeFact('excluded')?.querySelector('dt'))).toBe('Answers outside them');
    expect(textOf(scopeFact('excluded')?.querySelector('dd'))).toBe('0.0 %');
    expect(textOf(scopeFact('business')?.querySelector('dt'))).toBe('US business hours');
    expect(textOf(scopeFact('business')?.querySelector('dd'))).toBe('Not sampled US business hours are weekdays 14–22 UTC.');
    expect(textOf(scopeFact('outside')?.querySelector('dt'))).toBe('Outside them');
    expect(textOf(scopeFact('outside')?.querySelector('dd'))).toBe('Sampled');
    expect(el.querySelector('#cc-vb-scope-tip .cc-vb-tip-closing')).toBeNull();
  });

  it('says there is no common time stratum, and what speed needs, when the periods share no block', () => {
    const result = ccAnalysisResult();
    result.scope = { ...result.scope, text: '', strataUsed: [], excludedShare: 0.25, usBusinessHoursCovered: true };
    show(result);
    expect(textOf(el.querySelector('[data-meta="scope"] .cc-vb-meta-value'))).toBe('No common time stratum');
    expect(textOf(scopeFact('blocks')?.querySelector('dd'))).toBe('None');
    expect(textOf(scopeFact('excluded')?.querySelector('dd'))).toBe('25.0 %');
    expect(textOf(scopeFact('business')?.querySelector('dd'))).toContain('Sampled');
    expect(textOf(el.querySelector('#cc-vb-scope-tip .cc-vb-tip-closing')))
      .toBe('Speed (P2, P3) needs runs at the same hours in both periods.');
  });

  it('opens the scope dialog from its info button', () => {
    show(ccAnalysisResult());
    const dialog = el.querySelector<HTMLDialogElement>('[data-meta="scope"] dialog.gh-info-dialog');
    expect(dialog?.open).toBe(false);
    el.querySelector<HTMLButtonElement>('[data-meta="scope"] button.gh-info-btn')?.click();
    expect(dialog?.open).toBe(true);
    dialog?.close();
  });

  it('shows the protocol with an info dialog of its endpoints, significance, sample, verdicts and grades', () => {
    show(ccAnalysisResult());
    const item = el.querySelector('.cc-vb-meta > div[data-meta="protocol"]');
    expect(textOf(item?.querySelector('dt'))).toBe('Protocol');
    expect(textOf(item?.querySelector('.cc-vb-meta-value'))).toBe('V1');
    expect(item?.querySelector('app-info-tip button.gh-info-btn')?.getAttribute('aria-label')).toBe('About Protocol');
    expect(textOf(el.querySelector('#cc-vb-protocol-tip-title'))).toBe('About Protocol V1');

    expect(textOf(el.querySelector('#cc-vb-protocol-tip > p')))
      .toBe('The endpoints, margins and tests were fixed before the data was seen.');
    expect(textOf(protocolFact('endpoint-P1')?.querySelector('dt'))).toBe('P1 Quality');
    expect(textOf(protocolFact('endpoint-P1')?.querySelector('dd'))).toBe('Margin ±3 index points');
    expect(textOf(protocolFact('endpoint-P4')?.querySelector('dd'))).toBe('Margin ±15 %');
    expect(textOf(protocolFact('significance')?.querySelector('dd'))).toBe('α 0.05, with Holm\'s correction across P1–P5.');
    expect(textOf(protocolFact('sample')?.querySelector('dd'))).toBe(
      'P1, P4, P5: at least 2 runs per period on at least 2 UTC days, and 20 paired items. '
      + 'P2, P3: at least 3 runs per period in one common time block. With fewer, an endpoint is at most Indicated.');

    const terms = (kind: string) =>
      Array.from(el.querySelectorAll(`#cc-vb-protocol-tip dl[data-definitions="${kind}"] dt`)).map(dt => textOf(dt));
    expect(terms('verdicts')).toEqual(['Degraded or improved', 'Changed, negligible', 'Equivalent', 'Inconclusive', 'Not computable']);
    expect(terms('grades')).toEqual(['Established', 'Indicated', 'Not established']);
    expect(textOf(el.querySelector('#cc-vb-protocol-tip'))).toContain('Never read it as no change');
  });

  it('counts battery runs in the minimum sample of a battery analysis', () => {
    show(ccAnalysisResult({ unitKind: 'batteryRun' }));
    expect(textOf(protocolFact('sample')?.querySelector('dd'))).toContain('at least 2 battery runs per period');
  });

  it('shows the reliability lines in a warning, and nothing without them', () => {
    show(ccAnalysisResult());
    expect(el.querySelector('.cc-vb-reliability')).toBeNull();

    show(ccAnalysisResult({ headlineReliabilityIncreases: ['A control run would attribute P2.', 'Another day would meet the minimum sample.'] }));
    const alert = el.querySelector('div.alert.alert-warning.cc-vb-reliability');
    expect(alert).not.toBeNull();
    expect(alert?.querySelector('svg.alert-icon')?.getAttribute('aria-hidden')).toBe('true');
    expect(textOf(alert?.querySelector('.alert-heading'))).toBe('Reliability');
    expect(Array.from(alert?.querySelectorAll('ul > li') ?? []).map(li => textOf(li)))
      .toEqual(['A control run would attribute P2.', 'Another day would meet the minimum sample.']);
  });
});
