import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ProviderBadgeComponent } from './provider-badge.component';

describe('ProviderBadgeComponent', () => {
  let component: ProviderBadgeComponent;
  let fixture: ComponentFixture<ProviderBadgeComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProviderBadgeComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(ProviderBadgeComponent);
    component = fixture.componentInstance;
  });

  it.each([
    { provider: 'openai', modifier: 'provider-badge--openai' },
    // Case-insensitive: "OpenAI" is colored the same as "openai".
    { provider: 'OpenAI', modifier: 'provider-badge--openai' },
    { provider: 'Anthropic', modifier: 'provider-badge--anthropic' },
    { provider: 'google', modifier: 'provider-badge--google' },
    // An unknown provider gets no modifier class.
    { provider: 'Mistral', modifier: null },
    { provider: null, modifier: null }
  ])('applies the modifier class for provider $provider, and no other', ({ provider, modifier }) => {
    component.provider = provider;
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.classList.contains('provider-badge')).toBe(true);
    const modifiers = ['provider-badge--openai', 'provider-badge--anthropic', 'provider-badge--google']
      .filter(name => host.classList.contains(name));
    expect(modifiers).toEqual(modifier ? [modifier] : []);
  });

  it('renders the provider label verbatim', () => {
    component.provider = 'Anthropic';
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent.trim()).toBe('Anthropic');
  });
});
