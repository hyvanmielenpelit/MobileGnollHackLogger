import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ToastComponent, ToastNotice } from './toast.component';

/**
 * Stubs `showPopover`/`hidePopover`/`matches(':popover-open')` on the host
 * element with a small open/closed model, so the assertions below are about
 * the component's decisions rather than jsdom's popover support (which the
 * Karma/jsdom environment may not implement; real Chrome headless most
 * likely does).
 */
function stubPopover(el: HTMLElement): { isOpen: () => boolean } {
  let open = false;
  (el as any).showPopover = jasmine.createSpy('showPopover').and.callFake(() => { open = true; });
  (el as any).hidePopover = jasmine.createSpy('hidePopover').and.callFake(() => { open = false; });
  (el as any).matches = jasmine.createSpy('matches').and.callFake((sel: string) => sel === ':popover-open' && open);
  return { isOpen: () => open };
}

describe('ToastComponent', () => {
  let fixture: ComponentFixture<ToastComponent>;
  let component: ToastComponent;
  let host: HTMLElement;

  beforeEach(async () => {
    jasmine.clock().install();

    await TestBed.configureTestingModule({
      imports: [ToastComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(ToastComponent);
    component = fixture.componentInstance;
    host = fixture.nativeElement;
    stubPopover(host);
  });

  afterEach(() => {
    jasmine.clock().uninstall();
  });

  function setNotice(notice: ToastNotice): void {
    fixture.componentRef.setInput('notice', notice);
    fixture.detectChanges();
  }

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('shows the popover for a success notice and auto-hides after durationMs, emitting dismissed', () => {
    const dismissed = jasmine.createSpy('dismissed');
    component.dismissed.subscribe(dismissed);

    setNotice({ id: 1, kind: 'success', message: 'Saved', durationMs: 1000 });

    expect((host as any).showPopover).toHaveBeenCalled();
    expect(dismissed).not.toHaveBeenCalled();

    jasmine.clock().tick(1000);

    expect((host as any).hidePopover).toHaveBeenCalled();
    expect(dismissed).toHaveBeenCalledTimes(1);
  });

  it('never auto-hides an error notice, however far the clock is ticked', () => {
    const dismissed = jasmine.createSpy('dismissed');
    component.dismissed.subscribe(dismissed);

    setNotice({ id: 1, kind: 'error', message: 'Refused', durationMs: 1000 });

    jasmine.clock().tick(60_000);

    expect(dismissed).not.toHaveBeenCalled();
    expect((host as any).hidePopover).not.toHaveBeenCalled();
  });

  it('pauses the countdown on mouseenter and restarts it with the full duration on mouseleave', () => {
    const dismissed = jasmine.createSpy('dismissed');
    component.dismissed.subscribe(dismissed);

    setNotice({ id: 1, kind: 'info', message: 'Heads up', durationMs: 1000 });

    jasmine.clock().tick(900);
    host.dispatchEvent(new Event('mouseenter'));

    // Paused: waiting well past the original duration must not hide it.
    jasmine.clock().tick(5000);
    expect(dismissed).not.toHaveBeenCalled();

    host.dispatchEvent(new Event('mouseleave'));

    // Restarted with the full duration, not the 100ms that remained.
    jasmine.clock().tick(999);
    expect(dismissed).not.toHaveBeenCalled();

    jasmine.clock().tick(1);
    expect(dismissed).toHaveBeenCalledTimes(1);
  });

  it('hides and emits dismissed when the close button is clicked', () => {
    const dismissed = jasmine.createSpy('dismissed');
    component.dismissed.subscribe(dismissed);

    setNotice({ id: 1, kind: 'success', message: 'Saved', durationMs: 6000 });

    const closeButton = host.querySelector<HTMLButtonElement>('.toast-close');
    expect(closeButton).withContext('close button must be rendered').not.toBeNull();
    closeButton!.click();

    expect((host as any).hidePopover).toHaveBeenCalled();
    expect(dismissed).toHaveBeenCalledTimes(1);
  });

  it('sets role="alert" and aria-live="assertive" for an error notice', () => {
    setNotice({ id: 1, kind: 'error', message: 'Refused' });

    expect(host.getAttribute('role')).toBe('alert');
    expect(host.getAttribute('aria-live')).toBe('assertive');
  });

  it('sets role="status" and aria-live="polite" for success and info notices', () => {
    setNotice({ id: 1, kind: 'success', message: 'Saved' });
    expect(host.getAttribute('role')).toBe('status');
    expect(host.getAttribute('aria-live')).toBe('polite');

    setNotice({ id: 2, kind: 'info', message: 'Heads up' });
    expect(host.getAttribute('role')).toBe('status');
    expect(host.getAttribute('aria-live')).toBe('polite');
  });

  it('shows again for a second notice with a new id, even with identical text', () => {
    const dismissed = jasmine.createSpy('dismissed');
    component.dismissed.subscribe(dismissed);

    setNotice({ id: 1, kind: 'success', message: 'Saved', durationMs: 1000 });
    jasmine.clock().tick(500);

    setNotice({ id: 2, kind: 'success', message: 'Saved', durationMs: 1000 });

    // The first timer must have been superseded: waiting out only the
    // remainder of the original 1000ms must not hide the re-shown toast.
    jasmine.clock().tick(500);
    expect(dismissed).not.toHaveBeenCalled();

    jasmine.clock().tick(500);
    expect(dismissed).toHaveBeenCalledTimes(1);
  });
});
