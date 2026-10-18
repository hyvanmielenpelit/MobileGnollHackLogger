import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { HealthStatus, HealthStatusComponent } from './health-status.component';

@Component({
  standalone: true,
  imports: [HealthStatusComponent],
  template: `<app-health-status status="warning">Out of date</app-health-status>`
})
class ProjectionHostComponent {}

describe('HealthStatusComponent', () => {
  let fixture: ComponentFixture<HealthStatusComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HealthStatusComponent, ProjectionHostComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(HealthStatusComponent);
  });

  function render(status: HealthStatus): HTMLElement {
    fixture.componentRef.setInput('status', status);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders the shared chip with the status as data-status', () => {
    const chip = render('ok').querySelector('span.gh-health-status') as HTMLElement;
    expect(chip).not.toBeNull();
    expect(chip.getAttribute('data-status')).toBe('ok');
  });

  it('draws one 14 px glyph per status, hidden from assistive technology', () => {
    const shapes: Record<HealthStatus, string> = {
      ok: 'polyline',
      warning: 'path[d^="M10.29"]',
      error: 'line[y1="8"][y2="12"]',
      info: 'line[y1="16"][y2="12"]',
      running: 'circle.dc-ring-arc'
    };
    for (const status of Object.keys(shapes) as HealthStatus[]) {
      const chip = render(status).querySelector('.gh-health-status') as HTMLElement;
      const glyphs = chip.querySelectorAll('svg');
      expect(glyphs.length, status).toBe(1);
      const glyph = glyphs[0];
      expect(glyph.getAttribute('aria-hidden')).toBe('true');
      expect(glyph.getAttribute('focusable')).toBe('false');
      expect(glyph.getAttribute('width')).toBe('14');
      expect(glyph.getAttribute('height')).toBe('14');
      expect(glyph.querySelector(shapes[status])).not.toBeNull();
      expect(chip.getAttribute('data-status')).toBe(status);
    }
  });

  it('uses the inline ring spinner while running', () => {
    const glyph = render('running').querySelector('.gh-health-status svg') as SVGElement;
    expect(glyph.classList).toContain('dc-ring');
    expect(glyph.classList).toContain('dc-ring--inline');
    expect(glyph.querySelector('circle.dc-ring-track')).not.toBeNull();
  });

  it('updates the glyph when the status changes', () => {
    render('ok');
    const host = render('error');
    expect(host.querySelector('.gh-health-status')?.getAttribute('data-status')).toBe('error');
    expect(host.querySelector('.gh-health-status polyline')).toBeNull();
    expect(host.querySelector('.gh-health-status circle[r="10"]')).not.toBeNull();
  });

  it('has no role and no live region', () => {
    const host = render('warning');
    expect(host.hasAttribute('role')).toBe(false);
    expect(host.querySelector('[role]')).toBeNull();
    expect(host.querySelector('[aria-live]')).toBeNull();
  });

  it('projects the word inside the chip, after the glyph', () => {
    const hostFixture = TestBed.createComponent(ProjectionHostComponent);
    hostFixture.detectChanges();
    const chip = (hostFixture.nativeElement as HTMLElement).querySelector('.gh-health-status') as HTMLElement;
    expect(chip.getAttribute('data-status')).toBe('warning');
    expect(chip.textContent?.trim()).toBe('Out of date');
    expect(chip.firstElementChild?.tagName.toLowerCase()).toBe('svg');
  });
});
