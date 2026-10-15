import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LoadingIndicatorComponent } from './loading-indicator.component';

describe('LoadingIndicatorComponent', () => {
  let fixture: ComponentFixture<LoadingIndicatorComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LoadingIndicatorComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(LoadingIndicatorComponent);
  });

  function render(inputs: { text?: string; size?: 'inline' | 'block'; revealDelay?: boolean } = {}): HTMLElement {
    fixture.componentRef.setInput('text', inputs.text ?? 'Estimating…');
    if (inputs.size !== undefined) {
      fixture.componentRef.setInput('size', inputs.size);
    }
    if (inputs.revealDelay !== undefined) {
      fixture.componentRef.setInput('revealDelay', inputs.revealDelay);
    }
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders the text as its only text', () => {
    const host = render({ text: 'Loading report documents…' });
    expect(host.querySelector('.li-text')?.textContent).toBe('Loading report documents…');
    expect(host.textContent!.trim()).toBe('Loading report documents…');
  });

  it('hides the ring from assistive technology, and gives the host no role and no live region', () => {
    const host = render();
    const ring = host.querySelector('svg.dc-ring')!;
    expect(ring.getAttribute('aria-hidden')).toBe('true');
    expect(ring.getAttribute('focusable')).toBe('false');
    expect(ring.querySelector('circle.dc-ring-track')).not.toBeNull();
    expect(ring.querySelector('circle.dc-ring-arc')).not.toBeNull();
    expect(host.hasAttribute('role')).toBe(false);
    expect(host.hasAttribute('aria-live')).toBe(false);
  });

  it('is inline by default: a 14 px inline ring beside the text', () => {
    const host = render();
    expect(host.classList).toContain('li-inline');
    expect(host.classList).not.toContain('li-block');
    const ring = host.querySelector('svg.dc-ring')!;
    expect(ring.classList).toContain('dc-ring--inline');
    expect(ring.getAttribute('width')).toBe('14');
    // The fixture root can be a flex item, which blockifies inline-flex to flex.
    expect(['inline-flex', 'flex']).toContain(getComputedStyle(host).display);
    expect(getComputedStyle(host).flexDirection).toBe('row');
    expect(getComputedStyle(host).alignItems).toBe('center');
  });

  it('switches to the block size: a 40 px ring above the text', () => {
    const host = render({ size: 'block' });
    expect(host.classList).toContain('li-block');
    expect(host.classList).not.toContain('li-inline');
    const ring = host.querySelector('svg.dc-ring')!;
    expect(ring.classList).not.toContain('dc-ring--inline');
    expect(ring.getAttribute('width')).toBe('40');
    expect(getComputedStyle(host).flexDirection).toBe('column');
  });

  it('delays its reveal by default, and not when revealDelay is off', () => {
    expect(render().classList).toContain('li-reveal');
    expect(render({ revealDelay: false }).classList).not.toContain('li-reveal');
  });
});
