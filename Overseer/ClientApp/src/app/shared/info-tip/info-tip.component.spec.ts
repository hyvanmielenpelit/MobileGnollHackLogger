import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { InfoTipComponent } from './info-tip.component';

@Component({
  standalone: true,
  imports: [InfoTipComponent],
  template: '<app-info-tip tipId="demo-tip" subject="Outline width">Outlined bars need at least 1 px.</app-info-tip>'
})
class HostComponent {}

describe('InfoTipComponent', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  function button(): HTMLButtonElement {
    return (fixture.nativeElement as HTMLElement).querySelector('button.gh-info-btn') as HTMLButtonElement;
  }

  function tip(): HTMLElement {
    return (fixture.nativeElement as HTMLElement).querySelector('#demo-tip') as HTMLElement;
  }

  it('renders a named button that points at its hint tooltip', () => {
    expect(button().type).toBe('button');
    expect(button().getAttribute('aria-label')).toBe('About Outline width');
    expect(button().getAttribute('interestfor')).toBe('demo-tip');
    expect(button().querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('renders the tooltip as a hint popover with no role and the projected text', () => {
    expect(tip().getAttribute('popover')).toBe('hint');
    expect(tip().getAttribute('role')).toBeNull();
    expect(tip().classList).toContain('gh-tooltip');
    expect(tip().classList).toContain('gh-tooltip-multiline');
    expect(tip().textContent?.trim()).toBe('Outlined bars need at least 1 px.');
  });

  it('anchors the tooltip to the button by a matching name', () => {
    expect(button().getAttribute('style')).toContain('anchor-name: --demo-tip');
    expect(tip().getAttribute('style')).toContain('position-anchor: --demo-tip');
  });
});
