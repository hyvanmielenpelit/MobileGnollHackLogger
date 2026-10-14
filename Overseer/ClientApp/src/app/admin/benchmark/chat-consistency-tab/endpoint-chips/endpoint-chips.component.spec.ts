import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ccEndpoint, ccEndpointBrief, textOf } from '../chat-consistency-tab.testing';
import { CcEndpointBrief, CcEndpointResult } from '../chat-consistency.models';
import { CcEndpointChipsComponent } from './endpoint-chips.component';

describe('CcEndpointChipsComponent', () => {
  let fixture: ComponentFixture<CcEndpointChipsComponent>;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CcEndpointChipsComponent] }).compileComponents();
    fixture = TestBed.createComponent(CcEndpointChipsComponent);
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => fixture.destroy());

  function render(endpoints: readonly (CcEndpointBrief | CcEndpointResult)[] | null | undefined, label?: string): void {
    fixture.componentRef.setInput('endpoints', endpoints);
    if (label !== undefined) fixture.componentRef.setInput('label', label);
    fixture.detectChanges();
  }

  const chips = (): HTMLElement[] => Array.from(el.querySelectorAll<HTMLElement>('li.cc-ep-chip'));

  it('shows each summary endpoint as an icon, its id and its status word, with the name read but not shown', () => {
    render([
      ccEndpointBrief('P1', { computed: false, verdictLabel: 'not computable', grade: 'notEstablished' }),
      ccEndpointBrief('P2', { verdictLabel: 'degraded', grade: 'indicated' }),
      ccEndpointBrief('P3'),
      ccEndpointBrief('P4', { verdictLabel: 'more work' }),
      ccEndpointBrief('P5', { verdictLabel: 'inconclusive', grade: 'notEstablished' })
    ], 'Endpoints of analysis #4');

    const list = el.querySelector('ul.cc-ep-chips')!;
    expect(list.getAttribute('role')).toBe('list');
    expect(list.getAttribute('aria-label')).toBe('Endpoints of analysis #4');
    expect(chips().map(chip => [chip.getAttribute('data-endpoint'), chip.getAttribute('data-status')])).toEqual([
      ['P1', 'notComputable'], ['P2', 'changed'], ['P3', 'within'], ['P4', 'changed'], ['P5', 'inconclusive']
    ]);
    expect(chips().map(chip => textOf(chip.querySelector('.cc-ep-chip-word')))).toEqual([
      'Not computable', 'Changed', 'Within margin', 'More work', 'Inconclusive'
    ]);
    expect(textOf(chips()[1])).toBe('P2 Time to first answer text: Changed');
    expect(textOf(chips()[1].querySelector('.visually-hidden'))).toBe('Time to first answer text:');
    // Never the icon alone: every chip has its icon, hidden, beside the word.
    for (const chip of chips()) {
      expect(chip.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
      expect(textOf(chip.querySelector('.cc-ep-chip-id'))).toBe(chip.getAttribute('data-endpoint'));
    }
  });

  it('reads a full result\'s endpoints by the same mapping', () => {
    render([
      ccEndpoint('P1'),
      ccEndpoint('P2', { verdict: 'changedImproved', verdictLabel: 'improved' }),
      ccEndpoint('P4', { verdict: 'changedImproved', verdictLabel: 'less work' })
    ]);
    expect(chips().map(chip => chip.getAttribute('data-status'))).toEqual(['within', 'improved', 'changed']);
    expect(chips().map(chip => textOf(chip.querySelector('.cc-ep-chip-word')))).toEqual(['Within margin', 'Improved', 'Less work']);
    expect(el.querySelector('ul.cc-ep-chips')!.getAttribute('aria-label')).toBe('Endpoints');
  });

  it('renders nothing for no endpoints, an absent list or null', () => {
    for (const endpoints of [[], undefined, null]) {
      render(endpoints);
      expect(el.querySelector('ul')).toBeNull();
    }
  });
});
