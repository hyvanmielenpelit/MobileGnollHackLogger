import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ModelCardSettingsComponent } from './model-card-settings.component';
import { ModelCardModel } from './model-card-format';

describe('ModelCardSettingsComponent', () => {
  let fixture: ComponentFixture<ModelCardSettingsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ModelCardSettingsComponent] }).compileComponents();
    fixture = TestBed.createComponent(ModelCardSettingsComponent);
  });

  const host = () => fixture.nativeElement as HTMLElement;
  const text = (node: Element) => (node.textContent ?? '').replace(/\s+/g, ' ').trim();

  function render(model: ModelCardModel): void {
    fixture.componentRef.setInput('model', model);
    fixture.detectChanges();
  }

  it('renders the four badges in order, each with its visually hidden prefix, in a named list', () => {
    render({ thinkingLevel: 'medium', reasoningMode: 'extended', reasoningSummary: 'auto', serviceTier: 'priority' });

    const list = host().querySelector('ul')!;
    expect(list.getAttribute('role')).toBe('list');
    expect(list.getAttribute('aria-label')).toBe('Model settings');
    expect(list.classList).toContain('gh-model-card-settings');
    const badges = Array.from(list.querySelectorAll(':scope > li > span'));
    expect(badges.map(b => b.className)).toEqual(['thinking-badge', 'reasoning-badge', 'config-badge', 'config-badge']);
    expect(badges.map(text)).toEqual([
      'thinking level Medium', 'reasoning mode extended', 'Summary: Auto', 'service tier Priority'
    ]);
  });

  it('draws no reasoning badge for the standard mode', () => {
    render({ thinkingLevel: 'high', reasoningMode: 'standard' });

    expect(host().querySelector('.thinking-badge')).not.toBeNull();
    expect(host().querySelector('.reasoning-badge')).toBeNull();
  });

  it('renders no element for a model with nothing to show', () => {
    render({ reasoningMode: 'default', reasoningSummary: null });

    expect(host().children.length).toBe(0);
  });
});
