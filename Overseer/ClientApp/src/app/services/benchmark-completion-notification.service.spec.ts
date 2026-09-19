import { TestBed } from '@angular/core/testing';

import { BenchmarkCompletionNotificationService } from './benchmark-completion-notification.service';

/** A stand-in `Notification` constructor, installed onto `window.Notification` per test. */
class FakeNotification {
  static permission: NotificationPermission = 'default';
  static requestPermission = jasmine.createSpy('requestPermission');
  onclick: ((this: Notification, ev: Event) => unknown) | null = null;
  closeCalls = 0;

  constructor(public title: string, public options: NotificationOptions) {
    FakeNotification.instances.push(this);
  }

  close(): void {
    this.closeCalls++;
  }

  static instances: FakeNotification[] = [];
}

describe('BenchmarkCompletionNotificationService', () => {
  let service: BenchmarkCompletionNotificationService;
  let originalNotification: unknown;
  let secureContextSpy: jasmine.Spy;

  beforeEach(() => {
    originalNotification = (window as any).Notification;
    FakeNotification.instances = [];
    FakeNotification.permission = 'default';
    FakeNotification.requestPermission = jasmine.createSpy('requestPermission');
    (window as any).Notification = FakeNotification;
    secureContextSpy = spyOnProperty(window, 'isSecureContext', 'get').and.returnValue(true);

    TestBed.configureTestingModule({});
    service = TestBed.inject(BenchmarkCompletionNotificationService);
  });

  afterEach(() => {
    (window as any).Notification = originalNotification;
  });

  describe('isSupported', () => {
    it('is false when the Notification API does not exist', () => {
      delete (window as any).Notification;
      expect(service.isSupported()).toBeFalse();
    });

    it('is false outside a secure context', () => {
      secureContextSpy.and.returnValue(false);
      expect(service.isSupported()).toBeFalse();
    });

    it('is true when the API exists in a secure context', () => {
      expect(service.isSupported()).toBeTrue();
    });
  });

  describe('permission', () => {
    it('reads the browser decision without prompting', () => {
      FakeNotification.permission = 'denied';
      expect(service.permission()).toBe('denied');
      expect(FakeNotification.requestPermission).not.toHaveBeenCalled();
    });

    it('is unsupported when the Notification API does not exist', () => {
      delete (window as any).Notification;
      expect(service.permission()).toBe('unsupported');
    });
  });

  describe('requestPermission', () => {
    it('resolves unsupported without calling the platform when the API does not exist', async () => {
      delete (window as any).Notification;
      const outcome = await service.requestPermission();
      expect(outcome).toBe('unsupported');
    });

    it('resolves each platform result', async () => {
      FakeNotification.requestPermission.and.returnValue(Promise.resolve('granted'));
      expect(await service.requestPermission()).toBe('granted');

      FakeNotification.requestPermission.and.returnValue(Promise.resolve('denied'));
      expect(await service.requestPermission()).toBe('denied');

      FakeNotification.requestPermission.and.returnValue(Promise.resolve('default'));
      expect(await service.requestPermission()).toBe('default');
    });

    it('swallows a platform exception as unsupported', async () => {
      FakeNotification.requestPermission.and.returnValue(Promise.reject(new Error('blocked by enterprise policy')));
      const outcome = await service.requestPermission();
      expect(outcome).toBe('unsupported');
    });
  });

  describe('notify', () => {
    it('resolves "not-granted" and raises nothing without permission granted', () => {
      FakeNotification.permission = 'default';
      const outcome = service.notify('run:1', 'Run finished', 'Suite X — Completed');
      expect(outcome).toBe('not-granted');
      expect(FakeNotification.instances.length).toBe(0);
    });

    it('resolves "unsupported" without touching Notification.permission when the API does not exist', () => {
      delete (window as any).Notification;
      const outcome = service.notify('run:1', 'Run finished', 'Suite X — Completed');
      expect(outcome).toBe('unsupported');
    });

    it('resolves "shown", raises one notification with the key as its tag, and focuses the window on click', () => {
      FakeNotification.permission = 'granted';
      const focusSpy = spyOn(window, 'focus');

      const outcome = service.notify('run:1', 'Run finished', 'Suite X — Completed');

      expect(outcome).toBe('shown');
      expect(FakeNotification.instances.length).toBe(1);
      const notification = FakeNotification.instances[0];
      expect(notification.title).toBe('Run finished');
      expect(notification.options).toEqual(jasmine.objectContaining({
        body: 'Suite X — Completed',
        tag: 'run:1',
        icon: '/favicon.ico'
      }));

      notification.onclick?.call(notification as unknown as Notification, new Event('click'));
      expect(focusSpy).toHaveBeenCalled();
      expect(notification.closeCalls).toBe(1);
    });

    it('resolves "duplicate" per key, without raising a second notification', () => {
      FakeNotification.permission = 'granted';
      expect(service.notify('run:1', 'First', 'Body')).toBe('shown');
      expect(service.notify('run:1', 'Second', 'Body')).toBe('duplicate');
      expect(FakeNotification.instances.length).toBe(1);
      expect(FakeNotification.instances[0].title).toBe('First');
    });

    it('tracks duplicates per key, not globally', () => {
      FakeNotification.permission = 'granted';
      service.notify('run:1', 'Run', 'Body');
      service.notify('run:2', 'Other run', 'Body');
      expect(FakeNotification.instances.length).toBe(2);
    });

    it('resolves "error" and keeps the message in lastError on a constructor exception, such as a platform that requires a service worker', () => {
      FakeNotification.permission = 'granted';
      class ThrowingNotification {
        static permission: NotificationPermission = 'granted';
        constructor() {
          throw new Error('this platform needs a service worker');
        }
      }
      (window as any).Notification = ThrowingNotification;

      let outcome: string | undefined;
      expect(() => { outcome = service.notify('run:1', 'Title', 'Body'); }).not.toThrow();
      expect(outcome).toBe('error');
      expect(service.lastError).toBe('this platform needs a service worker');
    });

    it('resets lastError to null on a later successful notify', () => {
      FakeNotification.permission = 'granted';
      class ThrowingNotification {
        static permission: NotificationPermission = 'granted';
        constructor() {
          throw new Error('boom');
        }
      }
      (window as any).Notification = ThrowingNotification;
      service.notify('run:1', 'Title', 'Body');
      expect(service.lastError).toBe('boom');

      (window as any).Notification = FakeNotification;
      service.notify('run:2', 'Title', 'Body');
      expect(service.lastError).toBeNull();
    });
  });
});
