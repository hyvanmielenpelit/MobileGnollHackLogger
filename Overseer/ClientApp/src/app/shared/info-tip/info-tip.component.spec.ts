import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { InfoTipComponent } from './info-tip.component';

@Component({
  standalone: true,
  imports: [InfoTipComponent],
  template: '<app-info-tip tipId="demo-tip" subject="Outline width">Outlined bars need at least 1 px.</app-info-tip>'
})
class HostComponent {}

@Component({
  standalone: true,
  imports: [InfoTipComponent],
  template: '<app-info-tip trigger="click" tipId="click-tip" subject="Scoring Profile">Weights the rubric dimensions.</app-info-tip>'
})
class ClickHostComponent {}

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

describe('InfoTipComponent (click trigger)', () => {
  let fixture: ComponentFixture<ClickHostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ClickHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(ClickHostComponent);
    fixture.detectChanges();
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function button(): HTMLButtonElement {
    return root().querySelector('button.gh-info-btn') as HTMLButtonElement;
  }

  function popup(): HTMLElement {
    return root().querySelector('#click-tip-popup') as HTMLElement;
  }

  function toggleEvent(newState: 'open' | 'closed'): Event {
    if (typeof ToggleEvent !== 'undefined') {
      return new ToggleEvent('toggle', { oldState: newState === 'open' ? 'closed' : 'open', newState });
    }
    const event = new Event('toggle');
    Object.defineProperty(event, 'newState', { value: newState });
    return event;
  }

  it('renders a named button that toggles its popup', () => {
    expect(button().type).toBe('button');
    expect(button().classList).toContain('gh-info-btn--click');
    expect(button().getAttribute('aria-label')).toBe('About Scoring Profile');
    expect(button().getAttribute('popovertarget')).toBe('click-tip-popup');
    expect(button().getAttribute('aria-expanded')).toBe('false');
    expect(button().hasAttribute('interestfor')).toBeFalse();
    expect(button().querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('renders an auto popover with no role, titled by the subject, and the text under tipId', () => {
    expect(popup().getAttribute('popover')).toBe('auto');
    expect(popup().getAttribute('role')).toBeNull();
    expect(popup().classList).toContain('gh-info-popup');
    expect(popup().getAttribute('aria-labelledby')).toBe('click-tip-title');
    expect(root().querySelector('#click-tip-title')?.textContent?.trim()).toBe('Scoring Profile');
    expect(root().querySelector('#click-tip')?.textContent?.trim()).toBe('Weights the rubric dimensions.');
    expect(root().querySelector('.gh-tooltip')).toBeNull();
  });

  it('reflects the popup state in aria-expanded', () => {
    popup().dispatchEvent(toggleEvent('open'));
    fixture.detectChanges();
    expect(button().getAttribute('aria-expanded')).toBe('true');

    popup().dispatchEvent(toggleEvent('closed'));
    fixture.detectChanges();
    expect(button().getAttribute('aria-expanded')).toBe('false');
  });

  it('anchors the popup to the button by a matching name', () => {
    expect(button().getAttribute('style')).toContain('anchor-name: --click-tip');
    expect(popup().getAttribute('style')).toContain('position-anchor: --click-tip');
  });
});
