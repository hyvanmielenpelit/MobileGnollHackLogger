import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, EMPTY, Observable, Subject, catchError, exhaustMap, of, startWith, switchMap, tap } from 'rxjs';
import { AuthService, AuthUser } from './auth.service';

export interface SystemAlert {
  id: string;
  type: string;
  message: string;
  /** An in-app path with an optional query, such as "/admin/configs"; shown only with `linkText`. */
  linkUrl?: string | null;
  linkText?: string | null;
  /** Any admin can mark this alert handled, which clears it for all of them. */
  isSharedTask?: boolean;
}

/** The `localStorage` key prefix of a user's dismissed alert ids; the lowercased user name follows it. */
export const DISMISSED_ALERTS_KEY_PREFIX = 'overseer.adminAlerts.dismissed.';

/** The per-tab `sessionStorage` key of older clients, merged into the user's record once and then removed. */
export const LEGACY_DISMISSED_ALERTS_KEY = 'dismissed_admin_alerts';

/** How long an alert must stay unreported before its dismissal is forgotten. */
export const DISMISSAL_FORGET_AFTER_MS = 7 * 24 * 60 * 60 * 1000;

/** The stored form of a user's dismissed alert ids. */
interface DismissedAlertsRecord {
  version: 1;
  ids: string[];
  /** When each listed id was first missing from the server's alerts (ISO 8601). */
  missingSince?: Record<string, string>;
}

/** The `localStorage` key holding the dismissals of the named user. */
export function dismissedAlertsKey(userName: string | null | undefined): string {
  return DISMISSED_ALERTS_KEY_PREFIX + (userName ?? '').toLowerCase();
}

/**
 * The admin alerts of `/api/admin/system-alerts`, fetched on each `user$` emission and on `refresh()`.
 * Dismissals are kept per user in `localStorage`. An alert that stays unreported for `DISMISSAL_FORGET_AFTER_MS`
 * is forgotten, so a condition that has cleared and later returns shows its alert again, while a check that fails
 * for a moment or a server restart does not. Where storage is blocked or unreadable, a dismissal lasts only as
 * long as the page.
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
  /** When each dismissed id was first missing from the server's alerts (ISO 8601). */
  private missingSince = new Map<string, string>();
  /** The signed-in admin's storage key; null when no admin is signed in. */
  private storageKey: string | null = null;

  private refreshRequests = new Subject<void>();

  constructor() {
    this.authService.user$.pipe(
      switchMap(user => {
        this.selectUser(user);
        if (!user?.isAdmin) {
          return of<SystemAlert[]>([]);
        }
        // A refresh requested while a fetch is in flight is ignored.
        return this.refreshRequests.pipe(startWith(undefined), exhaustMap(() => this.fetch()));
      })
    ).subscribe(alerts => this.receive(alerts ?? []));
  }

  /** Fetches the alerts again; does nothing while no admin is signed in. */
  refresh(): void {
    this.refreshRequests.next();
  }

  dismiss(id: string) {
    this.mergeStored();
    this.dismissed.add(id);
    this.missingSince.delete(id);
    this.persist();
    this.publish();
  }

  /** Clears a shared task for all admins, then removes it here and fetches the alerts again. */
  markHandled(id: string): Observable<void> {
    return this.http.post<void>('/api/admin/system-alerts/handled', { alertId: id }).pipe(
      tap(() => {
        this.allAlertsSubject.next(this.allAlertsSubject.value.filter(alert => alert.id !== id));
        this.publish();
        this.refresh();
      })
    );
  }

  /** Loads the dismissals of a newly signed-in admin, merges them for the same admin, and clears them otherwise. */
  private selectUser(user: AuthUser | null) {
    if (!user?.isAdmin) {
      this.storageKey = null;
      this.dismissed = new Set();
      this.missingSince = new Map();
      this.allAlertsSubject.next([]);
      this.publish();
      return;
    }

    const key = dismissedAlertsKey(user.userName);
    if (key !== this.storageKey) {
      this.storageKey = key;
      this.dismissed = new Set();
      this.missingSince = new Map();
    }
    this.mergeStored();
    this.migrateLegacy();
    this.publish();
  }

  /** The server's alerts; a failure is logged and completes without a value, so the current list stays. */
  private fetch(): Observable<SystemAlert[]> {
    return this.http.get<SystemAlert[]>('/api/admin/system-alerts').pipe(
      catchError(err => {
        console.error('Failed to fetch admin alerts', err);
        return EMPTY;
      })
    );
  }

  /**
   * Publishes the server's alerts and forgets the dismissal of an alert only when this response still omits it
   * `DISMISSAL_FORGET_AFTER_MS` after a response first did. An alert reported again restarts that period.
   */
  private receive(alerts: SystemAlert[]) {
    this.allAlertsSubject.next(alerts);
    this.mergeStored();
    const reported = new Set(alerts.map(alert => alert.id));
    const now = Date.now();
    let changed = false;
    for (const id of this.dismissed) {
      const since = this.missingSince.get(id);
      if (reported.has(id)) {
        changed = this.missingSince.delete(id) || changed;
      } else if (since === undefined) {
        this.missingSince.set(id, new Date(now).toISOString());
        changed = true;
      } else if (now - Date.parse(since) >= DISMISSAL_FORGET_AFTER_MS) {
        this.dismissed.delete(id);
        this.missingSince.delete(id);
        changed = true;
      }
    }
    if (changed) {
      this.persist();
    }
    this.publish();
  }

  private publish() {
    this.alertsSubject.next(this.allAlertsSubject.value.filter(alert => !this.dismissed.has(alert.id)));
  }

  /**
   * Adds the stored ids, which another tab may have changed, to the in-memory set, and takes the stored clocks.
   * Without a readable record the in-memory clocks stay, so blocked storage keeps working for the life of the page.
   */
  private mergeStored() {
    const stored = this.storageKey ? this.readStored(this.storageKey) : null;
    if (stored) {
      stored.ids.forEach(id => this.dismissed.add(id));
      this.missingSince = stored.missingSince;
    }
  }

  /** The stored record, without clocks that are malformed; null for no record, an unreadable one, or blocked storage. */
  private readStored(key: string): { ids: string[]; missingSince: Map<string, string> } | null {
    try {
      const raw = localStorage.getItem(key);
      if (!raw) {
        return null;
      }
      const record = JSON.parse(raw) as Partial<DismissedAlertsRecord> | null;
      if (record?.version !== 1 || !Array.isArray(record.ids)) {
        return null;
      }
      const ids = record.ids.filter((id): id is string => typeof id === 'string');
      const missingSince = new Map<string, string>();
      Object.entries(record.missingSince ?? {}).forEach(([id, value]) => {
        if (typeof value === 'string' && ids.includes(id) && !Number.isNaN(Date.parse(value))) {
          missingSince.set(id, value);
        }
      });
      return { ids, missingSince };
    } catch {
      return null;
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
        if (this.missingSince.size > 0) {
          record.missingSince = Object.fromEntries(this.missingSince);
        }
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
