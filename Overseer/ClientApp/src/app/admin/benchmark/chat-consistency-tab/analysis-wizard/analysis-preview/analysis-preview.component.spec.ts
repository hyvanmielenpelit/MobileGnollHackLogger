import { ComponentFixture, TestBed } from '@angular/core/testing';

import { groupOverseerEvents } from '../../chat-consistency-events';
import { CcPeriodIds, ccPeriodMembers, ccPeriodUnits } from '../../chat-consistency-periods';
import { CC_PROTOCOL_V1_ENDPOINTS, CcEndpointReadiness, CcPreviewNote, ccEndpointReadiness } from '../../chat-consistency-readiness';
import { CcTimelinePoint } from '../../chat-consistency.models';
import { ccAxis, ccComparisonSets, ccRunRows, ccTimeline, textOf } from '../../chat-consistency-tab.testing';
import { CcAnalysisPreviewComponent, CcPreviewFact } from './analysis-preview.component';

/** Runs 101–103 against 104–106. */
const RUN_IDS: CcPeriodIds = { baselineFirstId: 101, baselineLastId: 103, comparisonFirstId: 104, comparisonLastId: 106 };

/** The readiness of the five endpoints over runs 101–103 against 104–106. */
function runReadiness(): CcEndpointReadiness[] {
  const units = ccPeriodUnits(ccRunRows(), [], false);
  const members = ccPeriodMembers(units, RUN_IDS);
  const points = new Map<number, CcTimelinePoint>(ccTimeline().points.map(point => [point.runId, point]));
  return ccEndpointReadiness(CC_PROTOCOL_V1_ENDPOINTS, members.baseline, members.comparison, points, false);
}

function eventsNote(): CcPreviewNote {
  return {
    kind: 'events',
    severity: 'warning',
    text: 'A change inside a period mixes measurements; split at it, or check that it does not affect what you compare.',
    eventGroups: groupOverseerEvents(ccTimeline().events, ccTimeline().points)
  };
}

function controlsNote(): CcPreviewNote {
  return {
    kind: 'controls',
    severity: 'info',
    text: '1 of 6 runs has no matched control run (#104); the analysis looks for controls among other models\' runs itself.',
    eventGroups: []
  };
}

describe('CcAnalysisPreviewComponent', () => {
  let fixture: ComponentFixture<CcAnalysisPreviewComponent>;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CcAnalysisPreviewComponent] }).compileComponents();
    fixture = TestBed.createComponent(CcAnalysisPreviewComponent);
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => fixture.destroy());

  function set(inputs: Partial<Record<keyof CcAnalysisPreviewComponent, unknown>>): void {
    for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
    fixture.detectChanges();
  }

  function fact(key: string): HTMLElement | null {
    return el.querySelector(`.cc-ap-facts > div[data-fact="${key}"]`);
  }

  function endpoint(id: string): HTMLElement | null {
    return el.querySelector(`li.cc-ap-endpoint[data-endpoint="${id}"]`);
  }

  it('opens with its heading and lead, and names each section by its heading', () => {
    set({});
    const title = el.querySelector('h5#cc-ap-title');
    expect(title?.classList.contains('gh-section-title')).toBe(true);
    expect(textOf(title)).toBe('What the analysis will see');
    expect(textOf(el.querySelector('p.cc-ap-lead')))
      .toBe('Checked in the browser before anything is spent; the analysis applies the protocol itself.');
    const sections = Array.from(el.querySelectorAll('section'));
    expect(sections.map(section => section.className.split(' ').pop())).toEqual(['cc-ap-input', 'cc-ap-readiness', 'cc-ap-notes-section']);
    expect(sections.map(section => textOf(el.querySelector(`#${section.getAttribute('aria-labelledby')}`))))
      .toEqual(['Input', 'Endpoint readiness', 'Notes']);
  });

  it('shows the model with its badges, or None chosen', () => {
    set({ axis: ccAxis() });
    const model = fact('model');
    expect(textOf(model?.querySelector('dt'))).toBe('Model');
    expect(textOf(model?.querySelector('.cc-ap-model-name'))).toBe('GPT-5 high');
    expect(model?.querySelector('dd app-cc-model-badges')).not.toBeNull();
    expect(textOf(model?.querySelector('.thinking-badge'))).toBe('thinking level High');
    expect(textOf(model?.querySelector('.provider-badge'))).toBe('OpenAI');

    set({ axis: null });
    expect(textOf(fact('model')?.querySelector('dd'))).toBe('None chosen');
    expect(fact('model')?.querySelector('app-cc-model-badges')).toBeNull();
  });

  it('tags what is compared by its kind and names it', () => {
    const sets = ccComparisonSets().sets;
    const tag = () => fact('compared')?.querySelector('span.gh-tag.cc-kind-tag');
    const label = () => textOf(fact('compared')?.querySelector('.cc-ap-compared-label'));

    set({ compareSet: sets[0] });
    expect(textOf(fact('compared')?.querySelector('dt'))).toBe('Compared');
    expect(tag()?.getAttribute('data-kind')).toBe('battery');
    expect(textOf(tag())).toBe('Battery');
    expect(label()).toBe('Two initial suites (revision 1)');

    set({ compareSet: sets[1] });
    expect(tag()?.getAttribute('data-kind')).toBe('suite');
    expect(textOf(tag())).toBe('Suite');
    expect(label()).toBe('Board Suite');

    set({ compareSet: null });
    expect(tag()?.getAttribute('data-kind')).toBe('all');
    expect(textOf(tag())).toBe('All suites');
    expect(label()).toBe('Runs analyzed one by one');
  });

  it('lists the host\'s facts after Model and Compared, each with its note', () => {
    const facts: CcPreviewFact[] = [
      { key: 'name', term: 'Name', value: 'September check' },
      { key: 'split', term: 'Split rule', value: 'Earliest vs latest', note: 'The runs span 31 days: the first 14 days against the last 14 days.' }
    ];
    set({ facts });
    const keys = Array.from(el.querySelectorAll('.cc-ap-facts > div')).map(div => div.getAttribute('data-fact'));
    expect(keys).toEqual(['model', 'compared', 'name', 'split']);
    expect(textOf(fact('name')?.querySelector('dt'))).toBe('Name');
    expect(textOf(fact('name')?.querySelector('dd'))).toBe('September check');
    expect(fact('name')?.querySelector('.bm-summary-facts-note')).toBeNull();
    expect(textOf(fact('split')?.querySelector('dt'))).toBe('Split rule');
    expect(textOf(fact('split')?.querySelector('dd'))).toContain('Earliest vs latest');
    expect(textOf(fact('split')?.querySelector('.bm-summary-facts-note')))
      .toBe('The runs span 31 days: the first 14 days against the last 14 days.');
  });

  it('shows one row per endpoint with its id, name, margin, status and fact', () => {
    set({ endpoints: runReadiness() });
    const rows = Array.from(el.querySelectorAll('ul.cc-ap-endpoints[role="list"] > li.cc-ap-endpoint'));
    expect(rows.map(row => row.getAttribute('data-endpoint'))).toEqual(['P1', 'P2', 'P3', 'P4', 'P5']);

    const p1 = endpoint('P1');
    expect(p1?.getAttribute('data-status')).toBe('meets');
    expect(textOf(p1?.querySelector('.cc-ap-endpoint-id'))).toBe('P1');
    expect(textOf(p1?.querySelector('.cc-ap-endpoint-name'))).toBe('Quality');
    expect(textOf(p1?.querySelector('.cc-ap-margin'))).toBe('±3 index points');
    expect(textOf(p1?.querySelector('.cc-ap-status'))).toBe('Meets the minimum sample');
    expect(p1?.querySelector('.cc-ap-status svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(textOf(p1?.querySelector('p.cc-ap-fact'))).toBe('Baseline 3 runs on 3 days · Comparison 3 runs on 3 days');

    // Run 103 has no call telemetry, so the baseline has two runs in the one common stratum.
    const p2 = endpoint('P2');
    expect(p2?.getAttribute('data-status')).toBe('belowMinimum');
    expect(textOf(p2?.querySelector('.cc-ap-endpoint-name'))).toBe('Time to first answer text');
    expect(textOf(p2?.querySelector('.cc-ap-margin'))).toBe('±15 %');
    expect(textOf(p2?.querySelector('.cc-ap-status'))).toBe('Cannot be Established');
    expect(textOf(p2?.querySelector('.cc-ap-fact'))).toBe('No common stratum has 3 runs in each period: weekday 08–12 UTC: 2 / 3.');
  });

  it('reads Not computed for an endpoint the periods cannot compute', () => {
    set({
      endpoints: [{
        id: 'P5', name: 'Cost per question', marginText: '±10 %', status: 'notComputed',
        fact: 'No run in the baseline is eligible for Cost.'
      }]
    });
    const p5 = endpoint('P5');
    expect(p5?.getAttribute('data-status')).toBe('notComputed');
    expect(textOf(p5?.querySelector('.cc-ap-status'))).toBe('Not computed');
    expect(p5?.querySelector('.cc-ap-status svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(textOf(p5?.querySelector('.cc-ap-fact'))).toBe('No run in the baseline is eligible for Cost.');
  });

  it('replaces the endpoint list with the refusal while the periods are refused', () => {
    set({ endpoints: runReadiness(), refusal: 'Choose the first and last run of both periods.' });
    expect(el.querySelector('.cc-ap-endpoints')).toBeNull();
    expect(textOf(el.querySelector('.cc-ap-readiness p.cc-ap-waiting')))
      .toBe('Endpoint readiness needs valid periods: Choose the first and last run of both periods.');

    set({ refusal: '' });
    expect(el.querySelector('.cc-ap-waiting')).toBeNull();
    expect(el.querySelectorAll('li.cc-ap-endpoint').length).toBe(5);
  });

  it('lists each note with its icon and word, the events note with its composite events', () => {
    set({ notes: [eventsNote(), controlsNote()] });
    const notes = Array.from(el.querySelectorAll('ul.cc-ap-notes[role="list"] > li.cc-ap-note'));
    expect(notes.map(note => [note.getAttribute('data-kind'), note.getAttribute('data-severity')]))
      .toEqual([['events', 'warning'], ['controls', 'info']]);
    expect(notes.map(note => note.querySelector(':scope > svg')?.getAttribute('aria-hidden'))).toEqual(['true', 'true']);
    expect(notes.map(note => textOf(note.querySelector('.cc-ap-note-word')))).toEqual(['Warning', 'Note']);

    const events = notes[0];
    expect(textOf(events.querySelector('.cc-ap-note-title'))).toBe('Overseer changes in the span');
    const groups = Array.from(events.querySelectorAll('ul.cc-ap-event-groups > li'));
    expect(groups.map(group => group.getAttribute('data-group-key'))).toEqual(['2026-09-15|30']);
    expect(textOf(groups[0].querySelector('span.cc-marker-tag.is-event'))).toBe('E1');
    expect(groups[0].querySelector('time')?.getAttribute('datetime')).toBe('2026-09-15');
    expect(textOf(groups[0].querySelector('time'))).toBe('2026-09-15');
    expect(textOf(groups[0])).toContain('Changes under harness 30');
    expect(textOf(groups[0].querySelector('.cc-ap-event-changes'))).toBe('· Tool guides');
    expect(textOf(events.querySelector('.cc-ap-note-text')))
      .toBe('A change inside a period mixes measurements; split at it, or check that it does not affect what you compare.');
    // The list comes before the note's text.
    expect(events.querySelector('.cc-ap-event-groups')?.nextElementSibling?.classList.contains('cc-ap-note-text')).toBe(true);

    expect(notes[1].querySelector('.cc-ap-note-title')).toBeNull();
    expect(notes[1].querySelector('.cc-ap-event-groups')).toBeNull();
    expect(textOf(notes[1].querySelector('.cc-ap-note-body')))
      .toBe('Note 1 of 6 runs has no matched control run (#104); the analysis looks for controls among other models\' runs itself.');
    expect(el.querySelector('.cc-ap-ready')).toBeNull();
  });

  it('says the periods are ready when there is no note', () => {
    set({ notes: [] });
    expect(el.querySelector('.cc-ap-notes')).toBeNull();
    const ready = el.querySelector('.cc-ap-notes-section p.cc-ap-ready');
    expect(textOf(ready)).toBe('No notes. The periods are ready to analyze.');
    expect(ready?.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
  });
});
