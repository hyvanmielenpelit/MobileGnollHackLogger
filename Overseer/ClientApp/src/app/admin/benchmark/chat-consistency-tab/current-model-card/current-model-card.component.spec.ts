import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CcAnalysisSummary, CcModelAxis } from '../chat-consistency.models';
import { ccAnalysisSummary, ccAxis, textOf } from '../chat-consistency-tab.testing';
import { CcCurrentModelCardComponent } from './current-model-card.component';

describe('CcCurrentModelCardComponent', () => {
  let fixture: ComponentFixture<CcCurrentModelCardComponent>;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CcCurrentModelCardComponent] }).compileComponents();
    fixture = TestBed.createComponent(CcCurrentModelCardComponent);
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => fixture.destroy());

  function render(inputs: {
    axis?: CcModelAxis;
    rangeText?: string;
    runsInRange?: number | null;
    runsInAnalysis?: number | null;
    latestAnalysis?: CcAnalysisSummary | null;
    loading?: boolean;
  } = {}): void {
    fixture.componentRef.setInput('axis', inputs.axis ?? ccAxis());
    fixture.componentRef.setInput('rangeText', inputs.rangeText ?? 'All dates');
    fixture.componentRef.setInput('runsInRange', inputs.runsInRange ?? null);
    fixture.componentRef.setInput('runsInAnalysis', inputs.runsInAnalysis ?? null);
    fixture.componentRef.setInput('latestAnalysis', inputs.latestAnalysis === undefined ? ccAnalysisSummary(7) : inputs.latestAnalysis);
    fixture.componentRef.setInput('loading', inputs.loading ?? false);
    fixture.detectChanges();
  }

  /** The facts as `[term, value]` pairs. */
  function facts(): string[][] {
    return Array.from(el.querySelectorAll('dl.bm-summary-facts > div'))
      .map(row => [textOf(row.querySelector('dt')), textOf(row.querySelector('dd'))]);
  }

  const fact = (term: string): string | null => facts().find(([dt]) => dt === term)?.[1] ?? null;
  const card = (): HTMLElement => el.querySelector<HTMLElement>('section.bm-summary-card.cc-current-card')!;

  it('names the model with its badges under the Current model eyebrow', () => {
    render();

    expect(card().getAttribute('aria-labelledby')).toBe('cc-current-eyebrow cc-current-name');
    expect(textOf(card().querySelector('#cc-current-eyebrow.bm-summary-card-eyebrow'))).toBe('Current model');
    const title = card().querySelector('h4.bm-summary-card-title')!;
    expect(textOf(title.querySelector('#cc-current-name'))).toBe('GPT-5 high');
    expect(textOf(title.querySelector('.thinking-badge'))).toBe('thinking level High');
    expect(textOf(title.querySelector('.provider-badge'))).toBe('OpenAI');
    // No service tier, no tier badge.
    expect(title.querySelector('.config-badge')).toBeNull();
    expect(textOf(card().querySelector('.bm-summary-card-meta'))).toBe('Chosen in the wizard · remembered in this browser');

    render({ axis: ccAxis({ thinkingLevel: null, serviceTier: 'flex', provider: 'Anthropic' }) });
    expect(textOf(title.querySelector('.thinking-badge'))).toBe('thinking level Default');
    expect(textOf(title.querySelector('.provider-badge'))).toBe('Anthropic');
    expect(textOf(title.querySelector('.config-badge'))).toBe('service tier Flex');
  });

  it('lists the runs, dates, first and latest run, suites and the latest analysis with its headline', () => {
    render({ axis: ccAxis({ suiteNames: ['Board Suite', 'Wiki Suite'] }) });

    expect(facts()).toEqual([
      ['Runs', '6 runs · 4 with call telemetry'],
      ['Dates', 'All dates'],
      ['First run', '2026-09-01'],
      ['Latest run', '#106 · 2026-10-01'],
      ['Suites', 'Board Suite, Wiki Suite'],
      ['Latest analysis', '#7 · Analysis 7 · saved 2026-10-02 Overseer chat with GPT-5 high: quality equivalent']
    ]);
    expect(textOf(el.querySelector('[data-fact="latest-analysis"] .bm-summary-facts-note')))
      .toBe('Overseer chat with GPT-5 high: quality equivalent');
    expect(el.querySelector('[data-fact="first-run"] time')!.getAttribute('datetime')).toBe('2026-09-01T08:00:00Z');
  });

  it('counts the runs in the chosen dates unless every date is chosen', () => {
    render({ rangeText: 'Last 7 days', runsInRange: 3 });
    expect(fact('Dates')).toBe('Last 7 days · 3 runs in these dates');

    render({ rangeText: 'Last 7 days', runsInRange: 1 });
    expect(fact('Dates')).toBe('Last 7 days · 1 run in these dates');

    render({ rangeText: 'All dates', runsInRange: null });
    expect(fact('Dates')).toBe('All dates');
  });

  it('shows a delayed spinner hidden from assistive technology in place of the count while loading', () => {
    render({ rangeText: 'Last 7 days', runsInRange: 3, loading: true });
    const spinner = el.querySelector<HTMLElement>('[data-fact="dates"] .cc-current-loading')!;
    expect(spinner.getAttribute('aria-hidden')).toBe('true');
    expect(spinner.querySelector('.gh-spinner-small')).not.toBeNull();
    expect(fact('Dates')).toBe('Last 7 days');

    render({ rangeText: 'Last 7 days', runsInRange: 3, loading: false });
    expect(el.querySelector('.cc-current-loading')).toBeNull();
  });

  it('shows In the analysis only while step 1 narrows the runs', () => {
    render();
    expect(fact('In the analysis')).toBeNull();

    render({ runsInAnalysis: 5 });
    expect(facts().map(([dt]) => dt)).toEqual(['Runs', 'Dates', 'In the analysis', 'First run', 'Latest run', 'Suites', 'Latest analysis']);
    expect(fact('In the analysis')).toBe('5 runs');
  });

  it('says None yet without a saved analysis, and offers no Open analysis', () => {
    render({ latestAnalysis: null });
    expect(fact('Latest analysis')).toBe('None yet');
    expect(el.querySelector('.cc-current-open-analysis')).toBeNull();
    expect(el.querySelector('.bm-summary-facts-note')).toBeNull();
  });

  it('leaves the headline out when the analysis has none', () => {
    render({ latestAnalysis: ccAnalysisSummary(9, { name: 'September', headline: null }) });
    expect(fact('Latest analysis')).toBe('#9 · September · saved 2026-10-02');
    expect(el.querySelector('.bm-summary-facts-note')).toBeNull();
  });

  it('asks to open the latest run report and the latest analysis', () => {
    const reports: number[] = [];
    const analyses: number[] = [];
    fixture.componentInstance.openRunReport.subscribe(id => reports.push(id));
    fixture.componentInstance.openAnalysis.subscribe(id => analyses.push(id));
    render();

    const actions = Array.from(card().querySelectorAll<HTMLButtonElement>('.bm-summary-card-actions button'));
    expect(actions.map(button => textOf(button))).toEqual(['Open latest run report', 'Open analysis #7']);
    for (const button of actions) {
      expect(button.type).toBe('button');
      expect(button.classList).toContain('btn-ghost');
    }

    actions[0].click();
    actions[1].click();
    expect(reports).toEqual([106]);
    expect(analyses).toEqual([7]);
  });
});
