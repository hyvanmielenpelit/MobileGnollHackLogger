import { Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CcFigure, qualityFigure, timeToFirstAnswerFigure } from '../chat-consistency-charts';
import { ccBatteryPoint, ccTimeline, chatConsistencyTestProviders, textOf } from '../chat-consistency-tab.testing';
import { CcChartFigureComponent, ccDataCards } from './cc-chart-figure.component';

@Component({
  selector: 'app-cc-figure-host',
  standalone: true,
  imports: [CcChartFigureComponent],
  template: `
    <app-cc-chart-figure [figure]="figure()" figureId="host-fig" [render]="false" [titleInChart]="true">
      <div ccFigureActions class="host-actions"><button type="button">Copy</button></div>
    </app-cc-chart-figure>
  `
})
class FigureHostComponent {
  readonly figure = input.required<CcFigure>();
}

describe('CcChartFigureComponent', () => {
  const memberLabels = new Map([[1101, 'Board Suite'], [1102, 'Wiki Suite'], [1201, 'Board Suite'], [1202, 'Wiki Suite']]);
  const battery = (): CcFigure => qualityFigure({
    points: [
      ccBatteryPoint(11, '2026-10-08T07:14:00Z', { overallIndex: 82.0 }),
      ccBatteryPoint(12, '2026-10-08T09:00:00Z', { overallIndex: 82.4 })
    ],
    unitKind: 'batteryRun',
    memberLabels
  });

  let fixture: ComponentFixture<CcChartFigureComponent>;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CcChartFigureComponent, FigureHostComponent],
      providers: chatConsistencyTestProviders()
    }).compileComponents();
  });

  afterEach(() => fixture?.destroy());

  function create(figure: CcFigure, inputs: Record<string, unknown> = {}): void {
    fixture = TestBed.createComponent(CcChartFigureComponent);
    el = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('figure', figure);
    fixture.componentRef.setInput('figureId', 'fig');
    fixture.componentRef.setInput('render', false);
    for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
    fixture.detectChanges();
  }

  it('puts the takeaway under the chart, not in the caption', () => {
    create(qualityFigure({ points: ccTimeline().points }));
    const takeaway = el.querySelector('.cc-figure-takeaway')!;
    expect(takeaway.tagName).toBe('P');
    expect(takeaway.id).toBe('fig-takeaway');
    expect(textOf(takeaway)).toBe('The Intelligence Index held between 71 and 74 across 6 runs.');
    expect(takeaway.previousElementSibling!.classList).toContain('cc-chart-box');
    expect(el.querySelector('figcaption .cc-figure-takeaway')).toBeNull();
    expect(textOf(el.querySelector('figcaption'))).toBe('Intelligence per run');
    expect(el.querySelector('figcaption')!.classList).not.toContain('visually-hidden');
    expect(el.querySelector('figure')!.getAttribute('aria-labelledby')).toBe('fig-title');
  });

  it('hides the whole caption visually while the chart draws the title', () => {
    create(qualityFigure({ points: ccTimeline().points }), { titleInChart: true });
    expect(el.querySelector('figcaption')!.classList).toContain('visually-hidden');
    expect(el.querySelector('.cc-figure-title')!.classList).not.toContain('visually-hidden');
  });

  it('projects the host\'s actions into the footer, after the marker line', () => {
    const host = TestBed.createComponent(FigureHostComponent);
    host.componentRef.setInput('figure', qualityFigure({
      points: ccTimeline().points, events: ccTimeline().events, annotations: ccTimeline().annotations
    }));
    host.detectChanges();
    const root = host.nativeElement as HTMLElement;
    const footer = root.querySelector('.cc-figure-footer')!;
    expect(footer.querySelector('.cc-figure-actions .host-actions button')).not.toBeNull();
    expect(footer.firstElementChild!.classList).toContain('cc-figure-markers');
    // The footer follows the takeaway.
    expect(root.querySelector('.cc-figure-takeaway')!.nextElementSibling!.classList).toContain('visually-hidden');
    expect(footer.previousElementSibling!.tagName).toBe('UL');
    host.destroy();
  });

  it('shows the data as one card per battery run, each column a labeled field', () => {
    create(battery());
    expect(el.querySelector('.cc-figure-data table')).toBeNull();
    const list = el.querySelector('.cc-data-cards')!;
    expect(list.getAttribute('role')).toBe('list');
    expect(list.getAttribute('aria-labelledby')).toBe('fig-data-title');
    expect(textOf(el.querySelector('#fig-data-title'))).toBe('Intelligence per run: data per battery run');

    const cards = Array.from(el.querySelectorAll<HTMLElement>('article.cc-data-card'));
    expect(cards.length).toBe(2);
    const title = cards[0].querySelector('.cc-data-card-title')!;
    expect(title.tagName).toBe('H6');
    expect(textOf(title)).toBe('Battery run #11');
    expect(cards[0].getAttribute('aria-labelledby')).toBe(title.id);
    expect(textOf(cards[0].querySelector('.cc-data-card-meta'))).toBe('2026-10-08 07:14 UTC');

    // A list value reads as its items: adjacent items have no text between them.
    const valueOf = (dd: Element) => dd.querySelector('.cc-data-list')
      ? Array.from(dd.querySelectorAll('li')).map(li => textOf(li)).join(' · ')
      : textOf(dd);
    const fieldEls = Array.from(cards[0].querySelectorAll<HTMLElement>('.cc-data-field'));
    const fields = fieldEls
      .map(field => [textOf(field.querySelector('dt')), valueOf(field.querySelector('dd')!), field.classList.contains('is-wide')]);
    // The empty Note is left out; Member runs takes the whole row.
    expect(fields).toEqual([
      ['Overall Intelligence Index', '82.0', false],
      ['Suites', '2', false],
      ['Member runs', '#1101 Board Suite · #1102 Wiki Suite', true]
    ]);

    // Member runs one per line: the id, then its suite name.
    const members = fieldEls[2].querySelector('dd ul.cc-data-list')!;
    expect(members.getAttribute('role')).toBe('list');
    const items = Array.from(members.querySelectorAll('li'));
    expect(items.map(li => textOf(li.querySelector('.cc-data-ref')))).toEqual(['#1101', '#1102']);
    expect(items.map(li => textOf(li.querySelector('.cc-data-ref-label')))).toEqual(['Board Suite', 'Wiki Suite']);
    expect(textOf(items[0])).toBe('#1101 Board Suite');
    expect(fieldEls.slice(0, 2).map(field => field.querySelector('.cc-data-list'))).toEqual([null, null]);
  });

  it('counts the rows on the Show data summary', () => {
    create(battery());
    expect(textOf(el.querySelector('.cc-figure-data > summary'))).toBe('Show data · 2 battery runs');
    fixture.destroy();
    create(qualityFigure({ points: ccTimeline().points }));
    expect(textOf(el.querySelector('.cc-figure-data > summary'))).toBe('Show data · 6 runs');
  });

  it('heads the cards one level under the host\'s section heading', () => {
    create(battery(), { dataHeadingLevel: 5 });
    expect(el.querySelector('.cc-data-card-title')!.tagName).toBe('H5');
  });

  it('says so when the range has no units', () => {
    create(qualityFigure({ points: [] }));
    expect(el.querySelectorAll('article.cc-data-card').length).toBe(0);
    expect(textOf(el.querySelector('.cc-data-empty'))).toBe('No runs in this range.');
  });

  describe('ccDataCards', () => {
    it('writes an empty cell as a dash and widens a long value', () => {
      const figure = timeToFirstAnswerFigure({ points: ccTimeline().points });
      const cards = ccDataCards(figure.table, 'x');
      expect(cards[0].id).toBe('x-row-0');
      expect(cards[0].title).toBe('Run #101');
      expect(cards[0].started).toBe('2026-09-01 08:00 UTC');
      expect(cards[0].fields.map(field => field.label)).toEqual(['Time to first answer text', 'Legacy proxy', 'Measure']);
      expect(ccDataCards({ columns: ['Run', 'Started', 'Value'], rows: [['#1', 'now', ' ']] }, 'y')[0].fields[0].value).toBe('—');
      const long = ccDataCards({ columns: ['Run', 'Started', 'Value'], rows: [['#1', 'now', 'x'.repeat(33)]] }, 'y');
      expect(long[0].fields[0].wide).toBe(true);
      expect(ccDataCards({ columns: ['Run', 'Started', 'Value'], rows: [['#1', 'now', 'x'.repeat(32)]] }, 'y')[0].fields[0].wide).toBe(false);
    });

    it('gives a list column its items and a plain column none', () => {
      const cards = ccDataCards(battery().table, 'x');
      const field = (label: string) => cards[0].fields.find(entry => entry.label === label)!;
      expect(field('Overall Intelligence Index').items).toBeNull();
      expect(field('Member runs').items).toEqual([{ ref: '#1101', label: 'Board Suite' }, { ref: '#1102', label: 'Wiki Suite' }]);
      const empty = ccDataCards({ columns: ['Run', 'Started', 'Member runs'], rows: [['#1', 'now', '—']], lists: { 'Member runs': [[]] } }, 'y');
      expect(empty[0].fields[0].items).toBeNull();
    });
  });
});
