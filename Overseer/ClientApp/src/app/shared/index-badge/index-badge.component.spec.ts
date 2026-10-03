import { ComponentFixture, TestBed } from '@angular/core/testing';

import { IndexBadgeComponent } from './index-badge.component';

describe('IndexBadgeComponent', () => {
  let fixture: ComponentFixture<IndexBadgeComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [IndexBadgeComponent] }).compileComponents();
    fixture = TestBed.createComponent(IndexBadgeComponent);
  });

  function render(inputs: { value: number; halfWidth?: number | null; size?: 'sm' | 'md'; label?: string }): HTMLElement {
    fixture.componentRef.setInput('value', inputs.value);
    if (inputs.halfWidth !== undefined) fixture.componentRef.setInput('halfWidth', inputs.halfWidth);
    if (inputs.size !== undefined) fixture.componentRef.setInput('size', inputs.size);
    if (inputs.label !== undefined) fixture.componentRef.setInput('label', inputs.label);
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('.index-badge') as HTMLElement;
  }

  const normalized = (el: Element): string => (el.textContent ?? '').replace(/\s+/g, ' ').trim();

  it('takes its tier from the score thresholds at 49, 50, 79 and 80', () => {
    expect(render({ value: 49 }).classList).toContain('badge-score-low');
    expect(render({ value: 50 }).classList).toContain('badge-score-mid');
    expect(render({ value: 79 }).classList).toContain('badge-score-mid');
    expect(render({ value: 80 }).classList).toContain('badge-score-high');
  });

  it('names the figure in a visually hidden label', () => {
    const badge = render({ value: 85, halfWidth: 4 });
    const hidden = Array.from(badge.querySelectorAll('.visually-hidden')).map(normalized);

    expect(hidden).toEqual(['Intelligence Index', ', out of 100']);
    expect(normalized(badge)).toBe('Intelligence Index 85 ± 4, out of 100');
    expect(badge.getAttribute('title')).toBeNull();
    expect(badge.querySelector('[title], [role="status"], [aria-live]')).toBeNull();
  });

  it('uses the label it is given', () => {
    expect(normalized(render({ value: 72, label: 'Overall Index' }))).toBe('Overall Index 72, out of 100');
  });

  it('shows the half-width only when there is one', () => {
    expect(render({ value: 85, halfWidth: 4 }).querySelector('.index-badge-half-width')).not.toBeNull();
    expect(render({ value: 85, halfWidth: null }).querySelector('.index-badge-half-width')).toBeNull();
    expect(normalized(render({ value: 85, halfWidth: null }))).not.toContain('±');
  });

  it('keeps the ring decorative', () => {
    const ring = render({ value: 85 }).querySelector('.index-badge-ring') as HTMLElement;
    expect(ring.getAttribute('aria-hidden')).toBe('true');
  });

  it('rounds the figures and fills the ring to the shown index', () => {
    const badge = render({ value: 79.6, halfWidth: 3.4, size: 'md' });

    expect(normalized(badge)).toContain('80 ± 3');
    expect(badge.classList).toContain('badge-score-high');
    expect(badge.classList).toContain('index-badge-md');
    expect(badge.style.getPropertyValue('--index-fill').trim()).toBe('80');
  });
});
