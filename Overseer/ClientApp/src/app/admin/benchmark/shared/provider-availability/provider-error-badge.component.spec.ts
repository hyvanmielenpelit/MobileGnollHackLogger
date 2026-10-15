import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ProviderErrorBadgeComponent } from './provider-error-badge.component';

describe('ProviderErrorBadgeComponent', () => {
  let fixture: ComponentFixture<ProviderErrorBadgeComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ProviderErrorBadgeComponent] }).compileComponents();
    fixture = TestBed.createComponent(ProviderErrorBadgeComponent);
  });

  function render(inputs: Record<string, unknown>, target = fixture): HTMLElement {
    for (const [name, value] of Object.entries(inputs)) {
      target.componentRef.setInput(name, value);
    }
    target.detectChanges();
    return target.nativeElement as HTMLElement;
  }

  function text(el: Element | null | undefined): string {
    return (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  const TROUBLED = { recorded: true, valid: 1480, providerErrors: 12, unattributed: 3, rerunsNeeded: 2, providerReruns: 1 };

  it('renders nothing on a card that does not record provider attempts', () => {
    const el = render({ ...TROUBLED, recorded: false });
    expect(el.querySelector('.peb')).toBeNull();
    expect(el.querySelector('.gh-tooltip')).toBeNull();
  });

  it('renders nothing while there are no provider errors and no re-runs', () => {
    const el = render({ recorded: true, valid: 1480, providerErrors: 0, unattributed: 3, rerunsNeeded: 0, providerReruns: 0 });
    expect(el.querySelector('.peb')).toBeNull();
  });

  it('reads the rate and the re-runs, with the full sentence in its tooltip', () => {
    const el = render(TROUBLED);
    expect(text(el.querySelector('.peb-errors'))).toBe('Provider errors 0.81%');
    expect(text(el.querySelector('.peb-reruns'))).toBe('2 re-runs');
    expect(el.querySelector('.peb-sep [aria-hidden="true"]')).not.toBeNull();
    const tip = el.querySelector('.gh-tooltip') as HTMLElement;
    expect(tip.getAttribute('popover')).toBe('hint');
    expect(text(tip)).toBe('12 of 1,480 attempts; 3 unattributed; 2 re-runs, 1 for provider errors');
  });

  it('reads the rate alone without re-runs, and the re-runs alone without provider errors', () => {
    let el = render({ ...TROUBLED, rerunsNeeded: 0, providerReruns: 0 });
    expect(text(el.querySelector('.peb'))).toBe('Provider errors 0.81%');
    expect(el.querySelector('.peb-sep')).toBeNull();

    el = render({ ...TROUBLED, providerErrors: 0, rerunsNeeded: 1, providerReruns: 0 });
    expect(text(el.querySelector('.peb'))).toBe('1 re-run');
    expect(text(el.querySelector('.gh-tooltip'))).toBe('0 of 1,480 attempts; 3 unattributed; 1 re-run');
  });

  it('anchors its tooltip under an id of its own, unique per badge, and loads the overlay polyfills', () => {
    const el = render(TROUBLED);
    const badge = el.querySelector('.peb') as HTMLElement;
    const tip = el.querySelector('.gh-tooltip') as HTMLElement;
    expect(tip.id).toMatch(/^peb-tip-\d+$/);
    expect(badge.getAttribute('interestfor')).toBe(tip.id);
    expect(badge.getAttribute('style')).toContain(`anchor-name: --${tip.id}`);
    expect(tip.getAttribute('style')).toContain(`position-anchor: --${tip.id}`);
    expect(badge.getAttribute('title')).toBeNull();

    const second = TestBed.createComponent(ProviderErrorBadgeComponent);
    const otherTip = render(TROUBLED, second).querySelector('.gh-tooltip') as HTMLElement;
    expect(otherTip.id).not.toBe(tip.id);
    second.destroy();
  });
});
