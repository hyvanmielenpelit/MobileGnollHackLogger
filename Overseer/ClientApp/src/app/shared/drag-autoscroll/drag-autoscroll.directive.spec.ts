import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DragAutoscrollDirective } from './drag-autoscroll.directive';

@Component({
  standalone: true,
  imports: [DragAutoscrollDirective],
  template: `
    <div class="scroller" style="position: fixed; top: 0; left: 0; width: 200px; height: 400px; overflow-y: auto;">
      <ul class="list" appDragAutoscroll style="margin: 0;">
        <li class="row" draggable="true" style="height: 20000px;">Row</li>
      </ul>
    </div>
    <div class="outside" style="position: fixed; top: 500px; left: 0;">Outside</div>
  `
})
class TestHostComponent {}

describe('DragAutoscrollDirective', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let el: HTMLElement;
  let scroller: HTMLElement;
  let queue: Map<number, FrameRequestCallback>;
  let nextHandle: number;

  const row = () => el.querySelector<HTMLElement>('.row')!;
  const running = () => queue.size > 0;
  /** Near the bottom edge of the scroller. */
  const NEAR_BOTTOM = 399;

  function frame(timestamp: number): void {
    const callbacks = Array.from(queue.values());
    queue.clear();
    callbacks.forEach(callback => callback(timestamp));
  }

  function frames(from: number, to: number): void {
    for (let time = from; time <= to; time += 10) {
      frame(time);
    }
  }

  function drag(type: string, target: EventTarget, clientY: number, clientX = 10): DragEvent {
    const event = new DragEvent(type, { bubbles: true, cancelable: true, clientX, clientY });
    target.dispatchEvent(event);
    return event;
  }

  beforeEach(async () => {
    queue = new Map();
    nextHandle = 1;
    vi.spyOn(window, 'requestAnimationFrame').mockImplementation(callback => {
      const handle = nextHandle++;
      queue.set(handle, callback);
      return handle;
    });
    vi.spyOn(window, 'cancelAnimationFrame').mockImplementation(handle => {
      queue.delete(handle);
    });

    await TestBed.configureTestingModule({ imports: [TestHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(TestHostComponent);
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
    scroller = el.querySelector<HTMLElement>('.scroller')!;
    scroller.scrollTop = 5000;
    queue.clear();
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  });

  afterEach(() => {
    fixture.destroy();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('scrolls the nearest scroller on a dragover near its edge', () => {
    drag('dragstart', row(), 200);
    drag('dragover', row(), NEAR_BOTTOM);
    expect(running()).toBe(true);
    frames(0, 300);
    expect(scroller.scrollTop).toBeGreaterThan(5000);
  });

  it('still scrolls on a dragover outside the list', () => {
    drag('dragstart', row(), 200);
    drag('dragover', el.querySelector('.outside')!, 500);
    expect(running()).toBe(true);
    frames(0, 300);
    expect(scroller.scrollTop).toBeGreaterThan(5000);
  });

  it('ignores a dragover without coordinates', () => {
    drag('dragstart', row(), 200);
    drag('dragover', row(), 0, 0);
    expect(running()).toBe(false);
  });

  it('pauses after 750 ms without a dragover, and the next dragover resumes', () => {
    drag('dragstart', row(), 200);
    drag('dragover', row(), NEAR_BOTTOM);
    vi.advanceTimersByTime(749);
    expect(running()).toBe(true);
    vi.advanceTimersByTime(1);
    expect(running()).toBe(false);

    drag('dragover', row(), NEAR_BOTTOM);
    expect(running()).toBe(true);
  });

  it('a dragover resets the watchdog', () => {
    drag('dragstart', row(), 200);
    drag('dragover', row(), NEAR_BOTTOM);
    vi.advanceTimersByTime(700);
    drag('dragover', row(), NEAR_BOTTOM);
    vi.advanceTimersByTime(700);
    expect(running()).toBe(true);
  });

  it.each(['dragend', 'drop'])('%s stops the scrolling and removes the document listeners', type => {
    drag('dragstart', row(), 200);
    drag('dragover', row(), NEAR_BOTTOM);
    expect(running()).toBe(true);

    drag(type, row(), NEAR_BOTTOM);
    expect(running()).toBe(false);

    drag('dragover', row(), NEAR_BOTTOM);
    expect(running()).toBe(false);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('survives drop and dragend both firing on a completed drop', () => {
    drag('dragstart', row(), 200);
    drag('dragover', row(), NEAR_BOTTOM);
    expect(() => {
      drag('drop', row(), NEAR_BOTTOM);
      drag('dragend', row(), NEAR_BOTTOM);
    }).not.toThrow();
    expect(running()).toBe(false);
  });

  it('stops when destroyed during a drag', () => {
    drag('dragstart', row(), 200);
    drag('dragover', row(), NEAR_BOTTOM);
    fixture.destroy();
    expect(running()).toBe(false);
    expect(vi.getTimerCount()).toBe(0);

    drag('dragover', document, NEAR_BOTTOM);
    expect(running()).toBe(false);
  });

  it('does nothing on a dragover without a preceding dragstart', () => {
    drag('dragover', row(), NEAR_BOTTOM);
    expect(running()).toBe(false);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('ignores a dragstart outside the host', () => {
    drag('dragstart', el.querySelector('.outside')!, 200);
    drag('dragover', row(), NEAR_BOTTOM);
    expect(running()).toBe(false);
  });

  it('never cancels or changes the default of a drag event', () => {
    expect(drag('dragstart', row(), 200).defaultPrevented).toBe(false);
    expect(drag('dragover', row(), NEAR_BOTTOM).defaultPrevented).toBe(false);
    expect(drag('drop', row(), NEAR_BOTTOM).defaultPrevented).toBe(false);
  });
});
