import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ModelIdentityComponent } from './model-identity.component';

describe('ModelIdentityComponent', () => {
  let fixture: ComponentFixture<ModelIdentityComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ModelIdentityComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(ModelIdentityComponent);
  });

  function render(inputs: {
    name?: string;
    provider?: string | null;
    thinkingLevel?: string | null;
    reasoningMode?: string | null;
  }): HTMLElement {
    fixture.componentRef.setInput('name', inputs.name ?? 'GPT-5.6 Luna');
    fixture.componentRef.setInput('provider', inputs.provider ?? null);
    fixture.componentRef.setInput('thinkingLevel', inputs.thinkingLevel ?? null);
    fixture.componentRef.setInput('reasoningMode', inputs.reasoningMode ?? null);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders the name', () => {
    const host = render({ name: 'Claude Opus' });
    expect(host.querySelector('.model-identity-name')?.textContent?.trim()).toBe('Claude Opus');
  });

  it('renders a provider badge only when a provider is set', () => {
    expect(render({ provider: 'OpenAI' }).querySelector('.provider-badge')?.textContent?.trim()).toBe('OpenAI');
    expect(render({ provider: null }).querySelector('.provider-badge')).toBeNull();
  });

  it('renders a thinking badge for a set level, and none for null or blank', () => {
    expect(render({ thinkingLevel: 'max' }).querySelector('.thinking-badge')).not.toBeNull();
    expect(render({ thinkingLevel: null }).querySelector('.thinking-badge')).toBeNull();
    expect(render({ thinkingLevel: '  ' }).querySelector('.thinking-badge')).toBeNull();
  });

  it('renders a reasoning badge for pro, and none for default, Standard or null', () => {
    expect(render({ reasoningMode: 'pro' }).querySelector('.reasoning-badge')).not.toBeNull();
    expect(render({ reasoningMode: 'default' }).querySelector('.reasoning-badge')).toBeNull();
    expect(render({ reasoningMode: 'Standard' }).querySelector('.reasoning-badge')).toBeNull();
    expect(render({ reasoningMode: null }).querySelector('.reasoning-badge')).toBeNull();
  });

  it('orders the badges thinking level, reasoning mode, then provider last', () => {
    const host = render({ provider: 'OpenAI', thinkingLevel: 'max', reasoningMode: 'pro' });

    const order = Array.from(host.children).map(child =>
      ['model-identity-name', 'thinking-badge', 'reasoning-badge', 'provider-badge']
        .find(name => child.classList.contains(name)));
    expect(order).toEqual(['model-identity-name', 'thinking-badge', 'reasoning-badge', 'provider-badge']);
  });

  it('prefixes each badge with visually-hidden context for assistive technology', () => {
    const host = render({ thinkingLevel: 'max', reasoningMode: 'pro' });

    const thinking = host.querySelector('.thinking-badge') as HTMLElement;
    expect(thinking.firstElementChild?.classList.contains('visually-hidden')).toBeTrue();
    expect(thinking.textContent?.trim()).toBe('thinking level max');

    const reasoning = host.querySelector('.reasoning-badge') as HTMLElement;
    expect(reasoning.firstElementChild?.classList.contains('visually-hidden')).toBeTrue();
    expect(reasoning.textContent?.trim()).toBe('reasoning mode pro');
  });
});
