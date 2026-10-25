import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, EMPTY, catchError, of, switchMap } from 'rxjs';
import { AuthService, AuthUser } from './auth.service';

export interface SystemAlert {
  id: string;
  type: string;
  message: string;
  /** An in-app path with an optional query, such as "/admin/configs"; shown only with `linkText`. */
  linkUrl?: string | null;
  linkText?: string | null;
}

/** The `localStorage` key prefix of a user's dismissed alert ids; the lowercased user name follows it. */
export const DISMISSED_ALERTS_KEY_PREFIX = 'overseer.adminAlerts.dismissed.';

/** The per-tab `sessionStorage` key of older clients, merged into the user's record once and then removed. */
export const LEGACY_DISMISSED_ALERTS_KEY = 'dismissed_admin_alerts';

/** The stored form of a user's dismissed alert ids. */
interface DismissedAlertsRecord {
  version: 1;
  ids: string[];
}

/** The `localStorage` key holding the dismissals of the named user. */
export function dismissedAlertsKey(userName: string | null | undefined): string {
  return DISMISSED_ALERTS_KEY_PREFIX + (userName ?? '').toLowerCase();
}

/**
 * The admin alerts of `/api/admin/system-alerts`. Dismissals are kept per user in `localStorage`, and an id
 * the server no longer reports is forgotten, so a condition that clears and later returns shows its alert
 * again. Where storage is blocked or unreadable, a dismissal lasts only as long as the page.
 */
@Injectable({ providedIn: 'root' })
export class AdminAlertService {
  private http = inject(HttpClient);
  private authService = inject(AuthService);

  private allAlertsSubject = new BehaviorSubject<SystemAlert[]>([]);
  private alertsSubject = new BehaviorSubject<SystemAlert[]>([]);
  /** Every alert the server reports, dismissed ones included; read it to learn a condition, not to show it. */
  allAlerts$ = this.allAlertsSubject.asObservable();
  /** The alerts the signed-in user has not dismissed. */
  alerts$ = this.alertsSubject.asObservable();

  private dismissed = new Set<string>();
  /** The signed-in admin's storage key; null when no admin is signed in. */
  private storageKey: string | null = null;

  constructor() {
    this.authService.user$.pipe(
      switchMap(user => {
        this.selectUser(user);
        if (!user?.isAdmin) {
          return of<SystemAlert[]>([]);
        }
        return this.http.get<SystemAlert[]>('/api/admin/system-alerts').pipe(
          catchError(err => {
            console.error('Failed to fetch admin alerts', err);
            return EMPTY;
          })
        );
      })
    ).subscribe(alerts => this.receive(alerts ?? []));
  }

  dismiss(id: string) {
    this.mergeStored();
    this.dismissed.add(id);
    this.persist();
    this.publish();
  }

  /** Loads the dismissals of a newly signed-in admin, merges them for the same admin, and clears them otherwise. */
  private selectUser(user: AuthUser | null) {
    if (!user?.isAdmin) {
      this.storageKey = null;
      this.dismissed = new Set();
      this.allAlertsSubject.next([]);
      this.publish();
      return;
    }

    const key = dismissedAlertsKey(user.userName);
    if (key !== this.storageKey) {
      this.storageKey = key;
      this.dismissed = new Set();
    }
    this.mergeStored();
    this.migrateLegacy();
    this.publish();
  }

  /** Publishes the server's alerts and forgets the dismissals of alerts it no longer reports. */
  private receive(alerts: SystemAlert[]) {
    this.allAlertsSubject.next(alerts);
    this.mergeStored();
    const reported = new Set(alerts.map(alert => alert.id));
    const kept = [...this.dismissed].filter(id => reported.has(id));
    if (kept.length !== this.dismissed.size) {
      this.dismissed = new Set(kept);
      this.persist();
    }
    this.publish();
  }

  private publish() {
    this.alertsSubject.next(this.allAlertsSubject.value.filter(alert => !this.dismissed.has(alert.id)));
  }

  /** Adds the stored ids, which another tab may have changed, to the in-memory set. */
  private mergeStored() {
    if (this.storageKey) {
      this.readStored(this.storageKey).forEach(id => this.dismissed.add(id));
    }
  }

  /** The stored ids; an unreadable record or blocked storage reads as none. */
  private readStored(key: string): string[] {
    try {
      const raw = localStorage.getItem(key);
      if (!raw) {
        return [];
      }
      const record = JSON.parse(raw) as Partial<DismissedAlertsRecord> | null;
      if (record?.version !== 1 || !Array.isArray(record.ids)) {
        return [];
      }
      return record.ids.filter((id): id is string => typeof id === 'string');
    } catch {
      return [];
    }
  }

  /** Writes the in-memory set and reports whether it was stored. */
  private persist(): boolean {
    if (!this.storageKey) {
      return false;
    }
    try {
      if (this.dismissed.size === 0) {
        localStorage.removeItem(this.storageKey);
      } else {
        const record: DismissedAlertsRecord = { version: 1, ids: [...this.dismissed] };
        localStorage.setItem(this.storageKey, JSON.stringify(record));
      }
      return true;
    } catch {
      return false;
    }
  }

  /** Merges the old per-tab ids into the record; the old key is removed once the record is stored. */
  private migrateLegacy() {
    let raw: string | null;
    try {
      raw = sessionStorage.getItem(LEGACY_DISMISSED_ALERTS_KEY);
    } catch {
      return;
    }
    if (raw === null) {
      return;
    }

    try {
      const ids: unknown = JSON.parse(raw);
      if (Array.isArray(ids)) {
        ids.filter((id): id is string => typeof id === 'string').forEach(id => this.dismissed.add(id));
      }
    } catch {
      // A malformed old value has nothing to merge and is removed below.
    }

    if (this.persist()) {
      try {
        sessionStorage.removeItem(LEGACY_DISMISSED_ALERTS_KEY);
      } catch {
        // The old value stays and is merged again, which adds nothing new.
      }
    }
  }
}
