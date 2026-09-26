import { ChangeDetectionStrategy, ChangeDetectorRef, Component, inject } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { PaneResizerComponent } from './pane-resizer.component';

/** Feeds every emitted commit back into `value`, the way a real host does. */
@Component({
  standalone: true,
  imports: [PaneResizerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-pane-resizer controls="sidebar" label="Resize sidebar"
                      [value]="value" [min]="min" [max]="max" [defaultValue]="defaultValue"
                      (valueChange)="onChange($event)" (valueCommit)="onCommit($event)" />
  `
})
class HostComponent {
  private cdr = inject(ChangeDetectorRef);
  value = 240;
  min = 160;
  max = 480;
  defaultValue = 240;
  changes: number[] = [];
  commits: number[] = [];

  setValue(value: number): void {
    this.value = value;
    this.cdr.markForCheck();
  }

  onChange(value: number): void {
    this.changes.push(value);
  }

  onCommit(value: number): void {
    this.commits.push(value);
    this.value = value;
    this.cdr.markForCheck();
  }
}

describe('PaneResizerComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let hostComponent: HostComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    hostComponent = fixture.componentInstance;
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  });

  const separator = () => el.querySelector<HTMLElement>('[role="separator"]')!;

  function keydown(key: string, shiftKey = false): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key, shiftKey, bubbles: true, cancelable: true });
    separator().dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  function pointer(type: string, clientX: number, pointerId = 9): PointerEvent {
    const event = new PointerEvent(type, {
      bubbles: true, cancelable: true, pointerId, isPrimary: true, button: 0, clientX
    });
    separator().dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  it('renders the WAI-ARIA window splitter attributes from its inputs', () => {
    const sep = separator();
    expect(sep.getAttribute('aria-orientation')).toBe('vertical');
    expect(sep.getAttribute('tabindex')).toBe('0');
    expect(sep.getAttribute('aria-controls')).toBe('sidebar');
    expect(sep.getAttribute('aria-label')).toBe('Resize sidebar');
    expect(sep.getAttribute('aria-valuenow')).toBe('240');
    expect(sep.getAttribute('aria-valuemin')).toBe('160');
    expect(sep.getAttribute('aria-valuemax')).toBe('480');
    expect(sep.getAttribute('aria-valuetext')).toBe('240 pixels');
  });

  it('rounds aria-valuenow and keeps it clamped even if value is out of range', () => {
    hostComponent.setValue(500.4);
    fixture.detectChanges();
    expect(separator().getAttribute('aria-valuenow')).toBe('480');
    expect(separator().getAttribute('aria-valuetext')).toBe('480 pixels');
  });

  describe('keyboard', () => {
    it('ArrowRight increases the value by step and commits once', () => {
      const event = keydown('ArrowRight');
      expect(event.defaultPrevented).toBeTrue();
      expect(hostComponent.changes).toEqual([256]);
      expect(hostComponent.commits).toEqual([256]);
    });

    it('ArrowLeft decreases the value by step', () => {
      const event = keydown('ArrowLeft');
      expect(event.defaultPrevented).toBeTrue();
      expect(hostComponent.changes).toEqual([224]);
      expect(hostComponent.commits).toEqual([224]);
    });

    it('Shift+ArrowRight increases by largeStep', () => {
      keydown('ArrowRight', true);
      expect(hostComponent.changes).toEqual([304]);
      expect(hostComponent.commits).toEqual([304]);
    });

    it('Shift+ArrowLeft decreases by largeStep', () => {
      keydown('ArrowLeft', true);
      expect(hostComponent.changes).toEqual([176]);
    });

    it('Home jumps to min and End jumps to max, each with one commit', () => {
      keydown('Home');
      expect(hostComponent.changes).toEqual([160]);
      expect(hostComponent.commits).toEqual([160]);
      keydown('End');
      expect(hostComponent.changes).toEqual([160, 480]);
      expect(hostComponent.commits).toEqual([160, 480]);
    });

    it('clamps ArrowRight at max and ArrowLeft at min', () => {
      hostComponent.setValue(480);
      fixture.detectChanges();
      keydown('ArrowRight');
      expect(hostComponent.changes).toEqual([480]);

      hostComponent.setValue(160);
      fixture.detectChanges();
      keydown('ArrowLeft');
      expect(hostComponent.changes).toEqual([480, 160]);
    });

    it('leaves an unhandled key untouched', () => {
      const event = keydown('Tab');
      expect(event.defaultPrevented).toBeFalse();
      expect(hostComponent.changes).toEqual([]);
      expect(hostComponent.commits).toEqual([]);
    });
  });

  describe('pointer drag', () => {
    beforeEach(() => {
      spyOn(Element.prototype, 'setPointerCapture').and.stub();
      // Makes the rAF-throttled live emit synchronous and deterministic for the test.
      spyOn(window, 'requestAnimationFrame').and.callFake((callback: FrameRequestCallback) => {
        callback(0);
        return 1;
      });
    });

    it('emits a clamped value while dragging and commits once on release', () => {
      const down = pointer('pointerdown', 100);
      expect(down.defaultPrevented).toBeTrue();
      expect(Element.prototype.setPointerCapture).toHaveBeenCalledWith(9);
      expect(separator().classList).toContain('is-dragging');

      pointer('pointermove', 140);
      expect(hostComponent.changes).toEqual([280]);
      expect(hostComponent.commits).toEqual([]);

      pointer('pointerup', 140);
      expect(hostComponent.changes).toEqual([280, 280]);
      expect(hostComponent.commits).toEqual([280]);
      expect(separator().classList).not.toContain('is-dragging');
    });

    it('clamps a drag that would go past max or min', () => {
      pointer('pointerdown', 100);
      pointer('pointermove', 1000);
      expect(hostComponent.changes).toEqual([480]);
      pointer('pointerup', 1000);
      expect(hostComponent.commits).toEqual([480]);
    });

    it('does not double-commit when lostpointercapture follows pointerup', () => {
      pointer('pointerdown', 100);
      pointer('pointermove', 140);
      pointer('pointerup', 140);
      pointer('lostpointercapture', 140);
      expect(hostComponent.commits).toEqual([280]);
    });

    it('commits the last dragged value on pointercancel', () => {
      pointer('pointerdown', 100);
      pointer('pointermove', 140);
      pointer('pointercancel', 140);
      expect(hostComponent.commits).toEqual([280]);
    });

    it('ignores a pointerdown with a non-primary button', () => {
      const event = new PointerEvent('pointerdown', {
        bubbles: true, cancelable: true, pointerId: 9, isPrimary: true, button: 2, clientX: 100
      });
      separator().dispatchEvent(event);
      fixture.detectChanges();
      expect(event.defaultPrevented).toBeFalse();
      expect(separator().classList).not.toContain('is-dragging');
    });

    it('ignores pointer events from another pointerId mid-drag', () => {
      pointer('pointerdown', 100, 9);
      pointer('pointermove', 140, 42);
      expect(hostComponent.changes).toEqual([]);
    });
  });

  it('double-click resets to defaultValue and commits once', () => {
    hostComponent.setValue(400);
    fixture.detectChanges();
    const event = new MouseEvent('dblclick', { bubbles: true, cancelable: true });
    separator().dispatchEvent(event);
    fixture.detectChanges();
    expect(hostComponent.changes).toEqual([240]);
    expect(hostComponent.commits).toEqual([240]);
  });

  it('ngOnDestroy cancels a pending animation frame', () => {
    spyOn(Element.prototype, 'setPointerCapture').and.stub();
    spyOn(window, 'requestAnimationFrame').and.returnValue(42);
    spyOn(window, 'cancelAnimationFrame');

    pointer('pointerdown', 100);
    pointer('pointermove', 140);
    fixture.destroy();

    expect(window.cancelAnimationFrame).toHaveBeenCalledWith(42);
  });
});
