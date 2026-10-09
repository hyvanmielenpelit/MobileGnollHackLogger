import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ccAxis, textOf } from '../chat-consistency-tab.testing';
import { CcBadgedModel, CcModelBadgesComponent } from './model-badges.component';

describe('CcModelBadgesComponent', () => {
  let fixture: ComponentFixture<CcModelBadgesComponent>;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CcModelBadgesComponent] }).compileComponents();
    fixture = TestBed.createComponent(CcModelBadgesComponent);
    el = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => fixture.destroy());

  function render(model: CcBadgedModel): void {
    fixture.componentRef.setInput('model', model);
    fixture.detectChanges();
  }

  it('shows the thinking level and the provider, each with a hidden name, and no tier badge without a tier', () => {
    render(ccAxis());
    expect(getComputedStyle(el).display).toBe('contents');
    expect(textOf(el.querySelector('.thinking-badge'))).toBe('thinking level High');
    expect(textOf(el.querySelector('.thinking-badge .visually-hidden'))).toBe('thinking level');
    expect(textOf(el.querySelector('.provider-badge'))).toBe('OpenAI');
    expect(el.querySelector('.config-badge')).toBeNull();
  });

  it('adds the service tier when one is set, and reads Default without a thinking level', () => {
    render(ccAxis({ provider: 'Anthropic', thinkingLevel: null, serviceTier: 'flex' }));
    expect(textOf(el.querySelector('.thinking-badge'))).toBe('thinking level Default');
    expect(textOf(el.querySelector('.provider-badge'))).toBe('Anthropic');
    expect(textOf(el.querySelector('.config-badge'))).toBe('service tier Flex');
    // Thinking, provider, tier: the launcher card's order.
    const order = Array.from(el.querySelectorAll('.thinking-badge, .provider-badge, .config-badge')).map(badge => badge.className.split(' ')[0]);
    expect(order).toEqual(['thinking-badge', 'provider-badge', 'config-badge']);
  });
});
