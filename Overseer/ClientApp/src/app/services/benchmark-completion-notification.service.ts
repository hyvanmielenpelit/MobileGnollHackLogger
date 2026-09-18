import { Injectable } from '@angular/core';

/** What `requestPermission` resolves to; `'unsupported'` covers a browser or context with no API at all. */
export type BenchmarkNotificationPermissionOutcome = 'granted' | 'denied' | 'default' | 'unsupported';

/**
 * The optional desktop notification the AI Benchmark run tab raises alongside, or instead of,
 * the completion sound (`BenchmarkCompletionSoundService`). The two are independent by design:
 * either, both or neither may be enabled, and this service knows nothing about the sound.
 *
 * A notification the platform itself plays a sound for is not a substitute for the completion
 * sound: whether a `Notification` makes any sound at all, and what sound, is entirely up to the
 * browser and the operating system's own notification settings, so `silent` is left unset here
 * rather than asserted either way.
 */
@Injectable({
  providedIn: 'root'
})
export class BenchmarkCompletionNotificationService {
  private readonly notifiedKeys = new Set<string>();

  isSupported(): boolean {
    return typeof window !== 'undefined' && 'Notification' in window && window.isSecureContext;
  }

  /**
   * Must be called only from the completion-notification checkbox's own change handler, never on
   * page load: the permission prompt is itself a user-facing action and browsers increasingly
   * refuse to show it outside a direct response to one. Never rejects.
   */
  async requestPermission(): Promise<BenchmarkNotificationPermissionOutcome> {
    if (!this.isSupported()) return 'unsupported';
    try {
      const result = await Notification.requestPermission();
      return result === 'granted' ? 'granted' : result === 'denied' ? 'denied' : 'default';
    } catch {
      return 'unsupported';
    }
  }

  /**
   * Raises a notification for `key` (`run:<id>` or `series:<id>`), once per key this session, and
   * only while permission is already `granted` — this never itself prompts. The constructor is
   * wrapped in `try/catch` because it throws on platforms that require a service worker
   * (`showNotification` on a `ServiceWorkerRegistration` instead); the application has none and
   * must not gain one solely for this.
   */
  notify(key: string, title: string, body: string): void {
    if (!this.isSupported() || Notification.permission !== 'granted') return;
    if (this.notifiedKeys.has(key)) return;
    this.notifiedKeys.add(key);

    try {
      const notification = new Notification(title, { body, tag: key, icon: '/favicon.ico' });
      notification.onclick = () => {
        window.focus();
        notification.close();
      };
    } catch {
      // A platform that requires a service worker for notifications gets none, silently.
    }
  }
}
