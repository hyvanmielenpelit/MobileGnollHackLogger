import { ChangeDetectionStrategy, ChangeDetectorRef, Component, inject } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { PickerToolsToggleComponent } from './picker-tools-toggle.component';

@Component({
  standalone: true,
  imports: [PickerToolsToggleComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-picker-tools-toggle buttonId="pt-demo" [label]="label" tooltip="Filter and sort"
                             [expanded]="expanded" [controls]="controls" [active]="active" [disabled]="disabled"
                             (pressed)="presses = presses + 1"></app-picker-tools-toggle>`
})
class HostComponent {
  private cdr = inject(ChangeDetectorRef);
  label = 'Filter and sort models';
  expanded = false;
  controls: string | null = null;
  active = false;
  disabled = false;
  presses = 0;

  set(changes: Partial<Pick<HostComponent, 'label' | 'expanded' | 'controls' | 'active' | 'disabled'>>): void {
    Object.assign(this, changes);
    this.cdr.markForCheck();
  }
}

describe('PickerToolsToggleComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function button(): HTMLButtonElement {
    return root().querySelector('button.picker-tools-toggle-btn') as HTMLButtonElement;
  }

  function update(changes: Parameters<HostComponent['set']>[0]): void {
    host.set(changes);
    fixture.detectChanges();
  }

  it('is an icon-only action button named by its label, with the tooltip as its hint', () => {
    const btn = button();
    expect(btn.type).toBe('button');
    expect(btn.classList).toContain('action-btn');
    expect(btn.id).toBe('pt-demo');
    expect(btn.getAttribute('aria-label')).toBe('Filter and sort models');
    expect(btn.hasAttribute('title')).toBe(false);
    expect(btn.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(btn.querySelector('polygon')?.getAttribute('points')).toBe('22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3');

    const tip = root().querySelector('#pt-demo-tip') as HTMLElement;
    expect(tip.getAttribute('popover')).toBe('hint');
    expect(tip.classList).toContain('gh-tooltip');
    expect(tip.textContent?.trim()).toBe('Filter and sort');
    expect(tip.hasAttribute('role')).toBe(false);
    expect(btn.getAttribute('interestfor')).toBe('pt-demo-tip');
    expect(btn.getAttribute('style')).toContain('anchor-name: --pt-demo');
    expect(tip.getAttribute('style')).toContain('position-anchor: --pt-demo');
    expect(btn.hasAttribute('aria-describedby')).toBe(false);
  });

  it('carries aria-expanded, and aria-controls only while controls is set', () => {
    expect(button().getAttribute('aria-expanded')).toBe('false');
    expect(button().hasAttribute('aria-controls')).toBe(false);

    update({ expanded: true, controls: 'mp-1-tools' });
    expect(button().getAttribute('aria-expanded')).toBe('true');
    expect(button().getAttribute('aria-controls')).toBe('mp-1-tools');

    update({ expanded: false, controls: null });
    expect(button().hasAttribute('aria-controls')).toBe(false);
  });

  it('shows the hidden dot only while active', () => {
    expect(root().querySelector('.picker-tools-toggle-dot')).toBeNull();
    update({ active: true, label: 'Filter and sort models (sorted)' });
    const dot = root().querySelector('.picker-tools-toggle-dot');
    expect(dot).not.toBeNull();
    expect(dot?.getAttribute('aria-hidden')).toBe('true');
    expect(button().getAttribute('aria-label')).toBe('Filter and sort models (sorted)');
  });

  it('emits pressed once per click', () => {
    button().click();
    button().click();
    expect(host.presses).toBe(2);
  });

  it('is aria-disabled, focusable and silent while disabled', () => {
    update({ disabled: true });
    const btn = button();
    expect(btn.getAttribute('aria-disabled')).toBe('true');
    expect(btn.disabled).toBe(false);
    btn.click();
    expect(host.presses).toBe(0);

    const toggle = fixture.debugElement.children[0].componentInstance as PickerToolsToggleComponent;
    toggle.focus();
    expect(document.activeElement).toBe(btn);
  });
});
