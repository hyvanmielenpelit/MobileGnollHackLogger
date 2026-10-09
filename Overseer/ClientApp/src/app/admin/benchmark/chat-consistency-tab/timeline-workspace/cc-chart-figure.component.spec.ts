import { Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CcFigure, qualityFigure, timeToFirstAnswerFigure } from '../chat-consistency-charts';
import { ccBatteryPoint, ccTimeline, chatConsistencyTestProviders, textOf } from '../chat-consistency-tab.testing';
import { CcChartFigureComponent, CcComposedFigure, ccDataCards } from './cc-chart-figure.component';

@Component({
  selector: 'app-cc-figure-host',
  standalone: true,
  imports: [CcChartFigureComponent],
  template: `
    <app-cc-chart-figure [figure]="figure()" figureId="host-fig" [render]="false" [titleInChart]="true"
                         [composed]="composed()" (showEvents)="eventsShown = true">
      <div ccFigureActions class="host-actions"><button type="button">Copy</button></div>
    </app-cc-chart-figure>
  `
})
class FigureHostComponent {
  readonly figure = input.required<CcFigure>();
  readonly composed = input<CcComposedFigure | null>(null);
  eventsShown = false;
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

  describe('composed mode', () => {
    function bitmap(width: number, height: number): HTMLCanvasElement {
      const canvas = document.createElement('canvas');
      canvas.width = width;
      canvas.height = height;
      const context = canvas.getContext('2d')!;
      context.fillStyle = '#e0ba6d';
      context.fillRect(0, 0, width, height);
      return canvas;
    }

    function composed(overrides: Partial<CcComposedFigure> = {}): CcComposedFigure {
      return {
        canvas: bitmap(400, 225), cssWidth: 200, cssHeight: 112, summary: 'GPT-5 high, 6 runs.', refusal: '', transparent: false,
        ...overrides
      };
    }

    const events = () => qualityFigure({ points: ccTimeline().points, events: ccTimeline().events, annotations: ccTimeline().annotations });

    it('draws the bitmap into one block-level image canvas of the given box, named by the alt text and the summary', () => {
      const figure = events();
      create(figure, { composed: composed(), render: true, titleInChart: true });
      expect(el.querySelector('canvas[basechart]')).toBeNull();
      const images = el.querySelectorAll<HTMLCanvasElement>('canvas.gh-fig-canvas');
      expect(images.length).toBe(1);
      const image = images[0];
      expect(image.classList).toContain('cc-chart-image');
      expect(image.getAttribute('role')).toBe('img');
      expect(image.getAttribute('aria-label')).toBe(`${figure.altText} GPT-5 high, 6 runs.`);
      expect(image.getAttribute('aria-describedby')).toBe('fig-markers');
      expect([image.width, image.height]).toEqual([400, 225]);
      expect([image.style.inlineSize, image.style.blockSize]).toEqual(['200px', '112px']);
      const style = getComputedStyle(image);
      expect(style.display).toBe('block');
      expect(style.cursor).toBe('default');
      expect(image.classList).not.toContain('is-transparent-figure');
      expect(el.querySelector<HTMLElement>('figure')!.style.inlineSize).toBe('200px');
    });

    it('shows a transparent image over the backdrop', () => {
      create(events(), { composed: composed({ transparent: true }), render: true });
      expect(el.querySelector('canvas.cc-chart-image')!.classList).toContain('is-transparent-figure');
    });

    it('puts the takeaway under the image, and keeps only Show events and the actions in the footer row', () => {
      const host = TestBed.createComponent(FigureHostComponent);
      host.componentRef.setInput('figure', events());
      host.componentRef.setInput('composed', composed());
      host.detectChanges();
      const root = host.nativeElement as HTMLElement;
      expect(root.querySelector('.cc-figure-takeaway')!.previousElementSibling!.classList).toContain('cc-chart-image-box');
      const footer = root.querySelector('.cc-figure-footer')!;
      expect(footer.querySelector('.cc-marker-tag')).toBeNull();
      expect(footer.querySelector('.cc-figure-markers-lead')).toBeNull();
      expect(textOf(footer.querySelector('.cc-figure-markers'))).toBe('Show events');
      expect(footer.querySelector('.cc-figure-actions .host-actions button')).not.toBeNull();
      host.destroy();
    });

    it('shows a refusal in the image\'s box, and a pending box before the first bitmap', () => {
      create(events(), { composed: composed({ canvas: null, refusal: 'The chart width must be between 320 and 8000 px.' }), render: true });
      expect(el.querySelector('canvas')).toBeNull();
      const box = el.querySelector<HTMLElement>('.cc-chart-image-box')!;
      expect(textOf(box.querySelector('.cc-figure-refusal'))).toBe('The chart width must be between 320 and 8000 px.');
      expect([box.style.inlineSize, box.style.blockSize]).toEqual(['200px', '112px']);
      fixture.destroy();

      create(events(), { composed: composed({ canvas: null }), render: true });
      expect(el.querySelector('.cc-chart-image-box .cc-chart-pending .dc-ring')).not.toBeNull();
      expect(el.querySelector('.cc-chart-image-box')!.getBoundingClientRect().height).toBeCloseTo(112, 0);
    });

    it('says there is nothing to chart for an empty figure', () => {
      create(qualityFigure({ points: [] }), { composed: composed(), render: true });
      expect(el.querySelector('.cc-chart-image-box')).toBeNull();
      expect(textOf(el.querySelector('.cc-figure-empty'))).toBe('Nothing to chart in this range.');
    });
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
