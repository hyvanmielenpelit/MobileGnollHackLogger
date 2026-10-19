import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ModelCardFactsComponent } from './model-card-facts.component';
import { ModelCardModel } from './model-card-format';

describe('ModelCardFactsComponent', () => {
  let fixture: ComponentFixture<ModelCardFactsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ModelCardFactsComponent] }).compileComponents();
    fixture = TestBed.createComponent(ModelCardFactsComponent);
  });

  const host = () => fixture.nativeElement as HTMLElement;
  const text = (node: Element | null) => (node?.textContent ?? '').replace(/\s+/g, ' ').trim();

  function render(model: ModelCardModel): void {
    fixture.componentRef.setInput('model', model);
    fixture.detectChanges();
  }

  it('lists the limits with grouped numbers, then the price with its Catalog badge', () => {
    render({
      maxInputTokens: 872000, maxOutputTokens: 128000,
      effectiveInputPricePerMillion: 4, effectiveOutputPricePerMillion: 20, effectiveCachedInputPricePerMillion: 0.2
    });

    const facts = Array.from(host().querySelectorAll('dl.gh-model-card-facts > .gh-model-card-fact'));
    expect(facts.map(f => text(f.querySelector('dt')))).toEqual(['Max input', 'Max output', 'Price per 1M tokens']);
    expect(text(facts[0].querySelector('dd'))).toBe('872,000');
    expect(text(facts[1].querySelector('dd'))).toBe('128,000');
    expect(facts[2].classList).toContain('is-wide');
    expect(text(facts[2].querySelector('.gh-model-card-price'))).toBe('$4.00 in · $20.00 out · $0.20 cached');
    const badge = facts[2].querySelector('dd > .status-badge')!;
    expect(badge.classList).toContain('badge-catalog');
    expect(text(badge)).toBe('Catalog');
  });

  it('adds the long-context note and marks an elapsed schedule', () => {
    render({
      effectiveInputPricePerMillion: 4, effectiveOutputPricePerMillion: 20,
      effectiveLongContextThresholdTokens: 272000,
      effectiveLongContextInputPricePerMillion: 8,
      effectiveLongContextOutputPricePerMillion: 30,
      pricingScheduleElapsed: true
    });

    expect(text(host().querySelector('.gh-model-card-price-note.model-price-long-context')))
      .toBe('Prompts over 272,000 tokens: $8.00 in / $30.00 out per 1M');
    const schedule = host().querySelector('.gh-model-card-price-note.model-price-schedule')!;
    expect(schedule.classList).toContain('is-elapsed');
    expect(text(schedule)).toBe('A scheduled price change is in effect — fold it into the base rates and re-verify.');
  });

  it('renders nothing for a model without facts', () => {
    render({});

    expect(host().children.length).toBe(0);
  });
});
