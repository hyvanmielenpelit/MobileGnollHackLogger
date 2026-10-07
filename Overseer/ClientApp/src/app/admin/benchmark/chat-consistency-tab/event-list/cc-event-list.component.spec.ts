import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CcEventDay, buildEventDays, groupOverseerEvents, servedModelChanges } from '../chat-consistency-events';
import { CcAnnotation, CcEvent } from '../chat-consistency.models';
import {
  ccAnnotation,
  ccEvent,
  ccEventAnnotations,
  ccEventPoints,
  ccOverseerEvents,
  textOf
} from '../chat-consistency-tab.testing';
import { CcEventListComponent } from './cc-event-list.component';

/** The composite-event fixture as the event list receives it: E1–E4, A1–A2, S1–S2 over six days. */
function fixtureDays(events: CcEvent[] = ccOverseerEvents(), annotations: CcAnnotation[] = ccEventAnnotations()): CcEventDay[] {
  const points = ccEventPoints();
  return buildEventDays(groupOverseerEvents(events, points), annotations, servedModelChanges(points));
}

describe('CcEventListComponent', () => {
  let fixture: ComponentFixture<CcEventListComponent>;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CcEventListComponent] }).compileComponents();
    fixture = TestBed.createComponent(CcEventListComponent);
    el = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('days', fixtureDays());
    fixture.componentRef.setInput('idPrefix', 'ev');
    fixture.detectChanges();
  });

  afterEach(() => fixture.destroy());

  const item = (tag: string): HTMLElement => el.querySelector<HTMLElement>(`#ev-item-${tag}`)!;

  describe('summary and empty state', () => {
    it('sums up the changes, annotations and served-model changes', () => {
      const summary = el.querySelector('#ev-summary')!;
      expect(summary.classList).toContain('cc-ev-summary');
      expect(textOf(summary)).toBe('4 Overseer changes on 4 days · 2 annotations · 2 served-model changes');
      expect(el.querySelector('.cc-ev-empty')).toBeNull();
    });

    it('adds the items the filters hide', () => {
      fixture.componentRef.setInput('hiddenCount', 2);
      fixture.detectChanges();
      expect(textOf(el.querySelector('#ev-summary'))).toBe('4 Overseer changes on 4 days · 2 annotations · 2 served-model changes · 2 hidden by the filters');
    });

    it('uses the singular for one of each', () => {
      const [first] = groupOverseerEvents(ccOverseerEvents(), ccEventPoints());
      fixture.componentRef.setInput('days', buildEventDays([first], [ccEventAnnotations()[1]], servedModelChanges(ccEventPoints()).slice(0, 1)));
      fixture.detectChanges();
      expect(textOf(el.querySelector('#ev-summary'))).toBe('1 Overseer change on 1 day · 1 annotation · 1 served-model change');
    });

    it('says when there is nothing in the range', () => {
      fixture.componentRef.setInput('days', []);
      fixture.detectChanges();
      const empty = el.querySelector('#ev-summary')!;
      expect(empty.classList).toContain('cc-ev-empty');
      expect(textOf(empty)).toBe('No Overseer change, annotation or served-model change in this range.');
      expect(el.querySelector('.cc-ev-summary')).toBeNull();
      expect(el.querySelector('section.cc-ev-day')).toBeNull();
    });

    it('says when the filters hide everything', () => {
      fixture.componentRef.setInput('days', []);
      fixture.componentRef.setInput('hiddenCount', 3);
      fixture.detectChanges();
      expect(el.querySelector('.cc-ev-empty')).toBeNull();
      expect(textOf(el.querySelector('.cc-ev-summary'))).toBe('Nothing shown · 3 hidden by the filters');
    });
  });

  describe('days', () => {
    it('renders one section per UTC day, oldest first, labeled by its heading', () => {
      const sections = Array.from(el.querySelectorAll<HTMLElement>('section.cc-ev-day'));
      expect(sections.map(section => section.getAttribute('data-day')))
        .toEqual(['2026-09-02', '2026-09-03', '2026-09-05', '2026-09-08', '2026-09-09', '2026-09-10']);
      for (const section of sections) {
        const heading = section.querySelector<HTMLElement>('.cc-ev-day-heading')!;
        expect(heading.id).toBe(`ev-day-${section.getAttribute('data-day')}`);
        expect(section.getAttribute('aria-labelledby')).toBe(heading.id);
      }
    });

    it('heads each day with its date in a time element, the weekday and the item count', () => {
      const heading = el.querySelector<HTMLElement>('section[data-day="2026-09-05"] .cc-ev-day-heading')!;
      expect(heading.tagName).toBe('H5');
      const time = heading.querySelector('time.cc-ev-date')!;
      expect(time.getAttribute('datetime')).toBe('2026-09-05');
      expect(textOf(time)).toBe('2026-09-05');
      expect(textOf(heading.querySelector('.cc-ev-weekday'))).toBe('Saturday');
      expect(textOf(heading)).toBe('2026-09-05 Saturday · 3 items');
      expect(textOf(el.querySelector('section[data-day="2026-09-02"] .cc-ev-day-heading'))).toBe('2026-09-02 Wednesday · 1 item');
    });

    it('keeps the day heading in view while its items scroll', () => {
      document.body.appendChild(el);
      try {
        const heading = el.querySelector<HTMLElement>('.cc-ev-day-heading')!;
        expect(getComputedStyle(heading).position).toBe('sticky');
      } finally {
        el.remove();
      }
    });

    it('uses level-6 day headings when asked', () => {
      fixture.componentRef.setInput('dayHeadingLevel', 6);
      fixture.detectChanges();
      expect(el.querySelectorAll('h6.cc-ev-day-heading').length).toBe(6);
      expect(el.querySelectorAll('h5').length).toBe(0);
      expect(el.querySelector('h6#ev-day-2026-09-05 time')!.getAttribute('datetime')).toBe('2026-09-05');
    });

    it('lists a day\'s items in time order, each with the tag pill of its kind', () => {
      const items = Array.from(el.querySelectorAll<HTMLElement>('section[data-day="2026-09-05"] ol.cc-ev-items > li.cc-ev-item'));
      expect(items.map(entry => entry.id)).toEqual(['ev-item-E2', 'ev-item-A2', 'ev-item-S1']);
      expect(items.map(entry => entry.getAttribute('data-kind'))).toEqual(['event', 'annotation', 'served']);
      expect(items.map(entry => entry.getAttribute('data-tag'))).toEqual(['E2', 'A2', 'S1']);
      const pills = items.map(entry => entry.querySelector<HTMLElement>('.cc-ev-tag-cell .cc-marker-tag')!);
      expect(pills.map(pill => textOf(pill))).toEqual(['E2', 'A2', 'S1']);
      expect(pills[0].classList).toContain('is-event');
      expect(pills[1].classList).toContain('is-annotation');
      expect(pills[2].classList).toContain('is-served');
    });

    it('prefixes every id, so two lists can share a document', () => {
      fixture.componentRef.setInput('idPrefix', 'cc-res-ev');
      fixture.detectChanges();
      expect(el.querySelector('#cc-res-ev-summary')).not.toBeNull();
      expect(el.querySelector('#cc-res-ev-day-2026-09-03')).not.toBeNull();
      expect(el.querySelector('#cc-res-ev-item-E1')).not.toBeNull();
      expect(el.querySelector('[id^="ev-"]')).toBeNull();
    });
  });

  describe('composite events', () => {
    it('shows the title, the runs and times, and one chip per kind with its count above 1', () => {
      const e1 = item('E1');
      expect(textOf(e1.querySelector('.cc-ev-title'))).toBe('Changes under harness 27');
      expect(textOf(e1.querySelector('.cc-ev-meta'))).toBe('Runs #202–#203 · 08:00–14:00 UTC');

      const changes = e1.querySelector('ul.cc-ev-changes')!;
      expect(changes.getAttribute('aria-label')).toBe('Changes in E1');
      const chips = Array.from(changes.querySelectorAll<HTMLElement>('li.gh-tag.cc-ev-change'));
      expect(chips.map(chip => chip.getAttribute('data-change-kind'))).toEqual(['CandidateSystemPromptSha256', 'KnowledgeBaseHeadSha']);
      expect(textOf(chips[0])).toBe('System prompt');
      expect(chips[0].querySelector('.cc-ev-change-count')).toBeNull();
      const count = chips[1].querySelector('.cc-ev-change-count')!;
      expect(textOf(count)).toBe('×2');
      expect(count.getAttribute('aria-hidden')).toBe('true');
      expect(textOf(chips[1].querySelector('.visually-hidden'))).toBe(', 2 runs');
    });

    it('titles a harness change and spells out its runs and times', () => {
      expect(textOf(item('E2').querySelector('.cc-ev-title'))).toBe('Harness 27 → 28');
      expect(textOf(item('E2').querySelector('.cc-ev-meta'))).toBe('Run #204 · 08:00 UTC');
      expect(Array.from(item('E2').querySelectorAll('.cc-ev-change')).map(chip => textOf(chip))).toEqual(['Tool guides', 'Harness']);
      expect(textOf(item('E3').querySelector('.cc-ev-title'))).toBe('Harness 28 → 29 (re-run 30)');
      expect(textOf(item('E3').querySelector('.cc-ev-meta'))).toBe('Runs #298–#299 · 09:00–10:00 UTC');
      // Runs 206 and 297 are not consecutive, so the run count is given.
      expect(textOf(item('E4').querySelector('.cc-ev-meta'))).toBe('Runs #206–#297 (2 runs) · 08:00–12:00 UTC');
    });

    it('lists each detection under a closed Details disclosure', () => {
      const details = item('E1').querySelector<HTMLDetailsElement>('details.gh-disclosure.cc-ev-details')!;
      expect(details.open).toBe(false);
      expect(textOf(details.querySelector('summary'))).toBe('Details');
      expect(Array.from(details.querySelectorAll('li.cc-ev-detail')).map(detail => textOf(detail))).toEqual([
        'Run #202 — System prompt: sp1 → sp2',
        'Run #202 — Knowledge base: kb1 → kb2',
        'Run #203 — Knowledge base: kb2 → kb3'
      ]);
    });

    it('cuts a value over 12 characters to its first 8, never a harness value, and names another series', () => {
      const at = '2026-09-10T08:00:00Z';
      const change = (kind: string, from: string | null, to: string | null, overrides: Partial<CcEvent> = {}) =>
        ccEvent({ runId: 206, previousRunId: 205, atUtc: at, kind, label: kind, from, to, ...overrides });
      fixture.componentRef.setInput('days', fixtureDays([
        change('CandidatePromptOptionsJson', '3f2a91c0aa11bb22', '9b0e44d1'),
        change('ToolGuidesSha256', 'abcdefghijkl', 'abcdefghijklm', { inTargetSeries: false, subjectKey: 'anthropic/claude-opus|high' }),
        change('WikiHeadSha', null, 'wk1'),
        change('HarnessVersion', '28 (re-run 29 of 30)', '29 (re-run 31, 32)')
      ], []));
      fixture.detectChanges();

      const details = Array.from(item('E1').querySelectorAll<HTMLElement>('li.cc-ev-detail'));
      expect(details.map(detail => textOf(detail))).toEqual([
        'Run #206 in anthropic/claude-opus|high — Tool guides: abcdefghijkl → abcdefgh',
        'Run #206 — Wiki: none → wk1',
        'Run #206 — Prompt options: 3f2a91c0 → 9b0e44d1',
        'Run #206 — Harness: 28 (re-run 29 of 30) → 29 (re-run 31, 32)'
      ]);
      const cut = (detail: HTMLElement) =>
        Array.from(detail.querySelectorAll('.cc-ev-value')).map(value => value.classList.contains('is-cut'));
      expect(cut(details[0])).toEqual([false, true]);
      expect(textOf(details[0].querySelector('.cc-ev-subject'))).toBe('anthropic/claude-opus|high');
      expect(details[1].querySelector('.cc-ev-none')).not.toBeNull();
      expect(cut(details[1])).toEqual([false]);
      expect(cut(details[2])).toEqual([true, false]);
      expect(cut(details[3])).toEqual([false, false]);
      expect(details[2].querySelector('.cc-ev-subject')).toBeNull();
    });
  });

  describe('annotations and served-model changes', () => {
    it('shows an annotation\'s kind, text, time and scope, and a source that is not a URL as text', () => {
      const a1 = item('A1');
      expect(textOf(a1.querySelector('.cc-ev-title'))).toBe('Provider statement');
      expect(textOf(a1.querySelector('.cc-ev-text'))).toBe('Provider reported elevated latency');
      expect(textOf(a1.querySelector('.cc-ev-meta'))).toBe('12:00 UTC · OpenAI · gpt-5');
      const source = a1.querySelector('.cc-ev-source')!;
      expect(textOf(source)).toBe('Source: status page, 2 September');
      expect(source.querySelector('a')).toBeNull();
    });

    it('links an https source in a new tab without an opener', () => {
      const link = item('A2').querySelector<HTMLAnchorElement>('.cc-ev-source a')!;
      expect(link.getAttribute('href')).toBe('https://example.com/release-notes');
      expect(link.getAttribute('target')).toBe('_blank');
      expect(link.getAttribute('rel')).toBe('noopener noreferrer');
      expect(textOf(link)).toBe('https://example.com/release-notes');
      expect(textOf(item('A2').querySelector('.cc-ev-title'))).toBe('Model release');
    });

    it('links only http and https sources, and scopes an annotation without a provider to all providers', () => {
      const note = (id: number, atUtc: string, sourceUrl: string | null) =>
        ccAnnotation(id, { atUtc, sourceUrl, provider: null, modelId: null, kind: 'other', text: `Note ${id}` });
      fixture.componentRef.setInput('days', fixtureDays([], [
        note(31, '2026-09-01T10:00:00Z', 'http://example.org/notes'),
        note(32, '2026-09-01T11:00:00Z', 'javascript:alert(1)'),
        note(33, '2026-09-01T12:00:00Z', 'ftp://example.org/notes'),
        note(34, '2026-09-01T13:00:00Z', null)
      ]));
      fixture.detectChanges();

      expect(item('A1').querySelector('.cc-ev-source a')!.getAttribute('href')).toBe('http://example.org/notes');
      expect(item('A2').querySelector('.cc-ev-source a')).toBeNull();
      expect(textOf(item('A2').querySelector('.cc-ev-source'))).toBe('Source: javascript:alert(1)');
      expect(item('A3').querySelector('.cc-ev-source a')).toBeNull();
      expect(item('A4').querySelector('.cc-ev-source')).toBeNull();
      expect(textOf(item('A1').querySelector('.cc-ev-meta'))).toBe('10:00 UTC · All providers');
      expect(el.querySelectorAll('a').length).toBe(1);
    });

    it('shows a served-model change with its models, run and time', () => {
      const s1 = item('S1');
      expect(textOf(s1.querySelector('.cc-ev-title'))).toBe('Served model changed');
      expect(textOf(s1.querySelector('.cc-ev-served'))).toBe('gpt-5-2026-08 → gpt-5-2026-09 · run #204 · 08:00 UTC');
      expect(textOf(item('S2').querySelector('.cc-ev-served'))).toBe('gpt-5-2026-09 → gpt-5-2026-08 · run #205 · 08:00 UTC');
    });
  });

  it('carries no title attributes', () => {
    expect(el.querySelectorAll('[title]').length).toBe(0);
  });
});
