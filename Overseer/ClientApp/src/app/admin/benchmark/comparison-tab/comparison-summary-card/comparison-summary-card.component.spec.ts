import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ComparisonSummaryCardComponent, LastComparisonDocuments } from './comparison-summary-card.component';
import { LAST_COMPARISON_STORAGE_VERSION, LastComparisonEntry, LastComparisonRecord } from '../last-comparison';

describe('ComparisonSummaryCardComponent', () => {
  let fixture: ComponentFixture<ComparisonSummaryCardComponent>;
  let host: HTMLElement;
  let opened: LastComparisonRecord[];

  const entry = (overrides: Partial<LastComparisonEntry> = {}): LastComparisonEntry => ({
    key: 'run:1',
    label: 'Model A (high)',
    modelDisplayName: 'Model A',
    provider: 'Anthropic',
    thinkingLevel: 'high',
    excluded: false,
    explanation: 'Charted with the baseline.',
    qualityPoint: 71.2,
    qualityLower: 66.5,
    qualityUpper: 76,
    modelTimeP50Ms: 4200,
    ttftP50Ms: 850,
    candidateCostPerQuestionUsd: 0.0042,
    ...overrides
  });

  const record = (overrides: Partial<LastComparisonRecord> = {}): LastComparisonRecord => ({
    version: LAST_COMPARISON_STORAGE_VERSION,
    savedAtUtc: '2026-10-08T12:00:00Z',
    id: 12,
    name: 'Flagships',
    subjectKind: 'Runs',
    entryKeys: ['run:1', 'run:2', 'run:3'],
    computedAtUtc: '2026-10-08T11:59:00Z',
    pricingBasis: 'Current',
    pricingBasisLabel: 'Catalog prices as of 8 Oct 2026',
    scopeName: 'Default Suite',
    comparableCount: 2,
    excludedCount: 1,
    entries: [
      entry(),
      entry({
        key: 'run:2', label: 'Model B', modelDisplayName: 'Model B', provider: 'OpenAI', thinkingLevel: null,
        qualityPoint: 60, qualityLower: null, qualityUpper: null, modelTimeP50Ms: null, ttftP50Ms: null,
        candidateCostPerQuestionUsd: null
      }),
      entry({
        key: 'run:3', label: 'Model C', modelDisplayName: 'Model C', provider: 'Google', excluded: true,
        explanation: 'Graded under another scoring profile.', qualityPoint: null, qualityLower: null, qualityUpper: null
      })
    ],
    ...overrides
  });

  function render(value: LastComparisonRecord, documents: LastComparisonDocuments | null = null): void {
    fixture.componentRef.setInput('record', value);
    fixture.componentRef.setInput('documents', documents);
    fixture.detectChanges();
  }

  const text = (el: Element | null): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

  /** The facts as term → value. */
  function facts(): Record<string, string> {
    const pairs: Record<string, string> = {};
    for (const group of Array.from(host.querySelectorAll('dl.bm-summary-facts > div'))) {
      pairs[text(group.querySelector('dt'))] = text(group.querySelector('dd'));
    }
    return pairs;
  }

  function rows(): HTMLTableRowElement[] {
    return Array.from(host.querySelectorAll<HTMLTableRowElement>('table.bm-summary-table tbody tr'));
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ComparisonSummaryCardComponent]
    }).compileComponents();
    fixture = TestBed.createComponent(ComparisonSummaryCardComponent);
    host = fixture.nativeElement as HTMLElement;
    opened = [];
    fixture.componentInstance.openInWizard.subscribe(value => opened.push(value));
  });

  afterEach(() => fixture.destroy());

  it('titles the card with the comparison number and name, labelled by its heading', () => {
    render(record());

    const section = host.querySelector('section.bm-summary-card')!;
    expect(section.getAttribute('aria-labelledby')).toBe('mc-last-title');
    expect(text(section.querySelector('.bm-summary-card-eyebrow'))).toBe('Last comparison');
    const title = section.querySelector('h4#mc-last-title.bm-summary-card-title');
    expect(text(title)).toBe('Comparison #12 · Flagships');
  });

  it('names the subject, the scope, the date and where it is remembered in the meta line', () => {
    render(record());
    const meta = text(host.querySelector('.bm-summary-card-meta'));
    expect(meta.startsWith('Runs and groups · Default Suite · computed ')).toBe(true);
    expect(meta.endsWith(' · remembered in this browser')).toBe(true);

    render(record({ subjectKind: 'Batteries', scopeName: null }));
    expect(text(host.querySelector('.bm-summary-card-meta')).startsWith('Battery results · computed ')).toBe(true);
  });

  it('states what was charted and the pricing basis', () => {
    render(record());
    expect(facts()['Charted']).toBe('2 of 3 entries · 1 excluded');
    expect(facts()['Pricing']).toBe('Catalog prices Catalog prices as of 8 Oct 2026');
    expect(text(host.querySelector('.bm-summary-facts-note'))).toBe('Catalog prices as of 8 Oct 2026');

    render(record({ excludedCount: 0, comparableCount: 3, pricingBasis: 'AsRun', pricingBasisLabel: 'As run' }));
    expect(facts()['Charted']).toBe('3 of 3 entries');
    expect(facts()['Pricing'].startsWith('As run')).toBe(true);
  });

  it('omits the documents fact while the count is unknown, and says None yet for none', () => {
    render(record(), null);
    expect(Object.keys(facts())).toEqual(['Charted', 'Pricing']);

    render(record(), { count: 0, latestAtUtc: null });
    expect(facts()['Report documents']).toBe('None yet');

    render(record(), { count: 4, latestAtUtc: '2026-10-08T09:00:00Z' });
    expect(facts()['Report documents']).toBe('4 · latest 8 Oct 2026');
  });

  it('renders one row per entry with its badges and figures, and a dash for each missing one', () => {
    render(record());

    const table = host.querySelector('table.gh-datatable.bm-summary-table')!;
    expect(text(table.querySelector('caption.visually-hidden'))).toBe('Entries of comparison #12');
    expect(Array.from(table.querySelectorAll('thead th')).map(th => text(th))).toEqual([
      'Model', 'Intelligence Index', 'Median model time', 'TTFT P50', 'Candidate cost / question', 'Status'
    ]);
    expect(rows().length).toBe(3);

    const first = rows()[0];
    expect(text(first.querySelector('th[scope="row"] .bm-summary-model-name'))).toBe('Model A (high)');
    expect(text(first.querySelector('app-provider-badge'))).toBe('Anthropic');
    expect(text(first.querySelector('.thinking-badge'))).toBe('high');
    const cells = Array.from(first.querySelectorAll('td'));
    expect(text(cells[0])).toBe('71.2 66.5–76.0');
    expect(text(cells[0].querySelector('.bm-summary-interval'))).toBe('66.5–76.0');
    expect(text(cells[1])).toBe('4200 ms');
    expect(text(cells[2])).toBe('850 ms');
    expect(text(cells[3])).toBe('$0.0042');
    expect(text(cells[4])).toBe('Charted');
    expect(cells.slice(0, 4).every(cell => cell.classList.contains('bm-summary-num'))).toBe(true);

    const second = Array.from(rows()[1].querySelectorAll('td'));
    expect(second[0].querySelector('.bm-summary-interval')).toBeNull();
    expect(second.slice(1, 4).map(cell => text(cell))).toEqual(['—', '—', '—']);
    expect(rows()[1].querySelector('.thinking-badge')).toBeNull();
  });

  it('marks an excluded entry, with its explanation in an info tip', () => {
    render(record());

    const status = rows()[2].querySelectorAll('td')[4];
    expect(text(status).startsWith('Excluded')).toBe(true);
    const tip = status.querySelector('app-info-tip')!;
    expect(tip).toBeTruthy();
    expect(tip.querySelector('button')!.getAttribute('aria-label')).toBe('About the exclusion of Model C');
    expect(text(tip.querySelector('#mc-last-status-tip-2'))).toBe('Graded under another scoring profile.');
    expect(text(rows()[2].querySelector('td'))).toBe('—');
  });

  it('emits Open in wizard with its record', () => {
    const value = record();
    render(value);

    const button = Array.from(host.querySelectorAll<HTMLButtonElement>('.bm-summary-card-actions button'));
    expect(button.length).toBe(1);
    expect(button[0].type).toBe('button');
    expect(button[0].classList.contains('btn-ghost')).toBe(true);
    expect(text(button[0])).toBe('Open in wizard');
    expect(button[0].querySelector('svg')).toBeNull();

    button[0].click();
    expect(opened).toEqual([value]);
  });
});
