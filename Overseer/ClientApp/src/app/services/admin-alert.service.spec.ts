import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject, Observable } from 'rxjs';

import {
  AdminAlertService,
  DISMISSAL_FORGET_AFTER_MS,
  LEGACY_DISMISSED_ALERTS_KEY,
  SystemAlert,
  dismissedAlertsKey
} from './admin-alert.service';
import { AuthService, AuthUser } from './auth.service';

const ALERTS_URL = '/api/admin/system-alerts';
const HANDLED_URL = '/api/admin/system-alerts/handled';

const START = Date.parse('2026-10-01T00:00:00.000Z');

const alert = (id: string): SystemAlert => ({ id, type: 'warning', message: `Alert ${id}.` });
const A = alert('snapshot-guard-observe');
const B = alert('report-charts-location-missing');

const admin = (userName: string): AuthUser => ({ userName, email: `${userName}@example.test`, hasApiKey: false, isAdmin: true });

/** The latest value of a stream backed by a BehaviorSubject. */
function latest<T>(source: Observable<T>): T {
  let value!: T;
  source.subscribe(v => (value = v)).unsubscribe();
  return value;
}

const ids = (alerts: SystemAlert[]) => alerts.map(a => a.id);

describe('AdminAlertService', () => {
  let http: HttpTestingController;
  let auth: { user$: Observable<AuthUser | null> };

  /** A new service instance, as a new tab or browser session would create, signed in as `user`. */
  function start(user: AuthUser | null, alerts: SystemAlert[]): AdminAlertService {
    auth.user$ = new BehaviorSubject<AuthUser | null>(user);
    const service = TestBed.runInInjectionContext(() => new AdminAlertService());
    if (user?.isAdmin) {
      http.expectOne(ALERTS_URL).flush(alerts);
    }
    return service;
  }

  function storedIds(userName: string): string[] | null {
    const raw = localStorage.getItem(dismissedAlertsKey(userName));
    return raw === null ? null : JSON.parse(raw).ids;
  }

  function storedMissingSince(userName: string): Record<string, string> | undefined {
    return JSON.parse(localStorage.getItem(dismissedAlertsKey(userName))!).missingSince;
  }

  /** Sets the time the service reads. */
  function setNow(time: number) {
    vi.spyOn(Date, 'now').mockReturnValue(time);
  }

  function clearStorage() {
    localStorage.removeItem(dismissedAlertsKey('Admin'));
    localStorage.removeItem(dismissedAlertsKey('Other'));
    sessionStorage.removeItem(LEGACY_DISMISSED_ALERTS_KEY);
  }

  beforeEach(() => {
    clearStorage();
    auth = { user$: new BehaviorSubject<AuthUser | null>(null) };
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: auth }]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    http.verify();
    clearStorage();
  });

  it('fetches nothing and shows nothing for a user who is not an admin', () => {
    const service = start({ ...admin('Player'), isAdmin: false }, []);

    expect(latest(service.alerts$)).toEqual([]);
    expect(latest(service.allAlerts$)).toEqual([]);
  });

  it('keeps a dismissal for a new service instance', () => {
    const first = start(admin('Admin'), [A, B]);
    first.dismiss(A.id);

    expect(ids(latest(first.alerts$))).toEqual([B.id]);
    expect(JSON.parse(localStorage.getItem(dismissedAlertsKey('Admin'))!)).toEqual({ version: 1, ids: [A.id] });

    const second = start(admin('Admin'), [A, B]);
    expect(ids(latest(second.alerts$))).toEqual([B.id]);
  });

  it('keeps dismissals per user', () => {
    start(admin('Admin'), [A, B]).dismiss(A.id);

    const other = start(admin('Other'), [A, B]);
    expect(ids(latest(other.alerts$))).toEqual([A.id, B.id]);
    expect(storedIds('Other')).toBeNull();
  });

  it('switches dismissals when another admin signs in on the same instance', () => {
    const users = new BehaviorSubject<AuthUser | null>(admin('Admin'));
    auth.user$ = users;
    const service = TestBed.runInInjectionContext(() => new AdminAlertService());
    http.expectOne(ALERTS_URL).flush([A, B]);
    service.dismiss(A.id);

    users.next(admin('Other'));
    http.expectOne(ALERTS_URL).flush([A, B]);
    expect(ids(latest(service.alerts$))).toEqual([A.id, B.id]);

    users.next(admin('Admin'));
    http.expectOne(ALERTS_URL).flush([A, B]);
    expect(ids(latest(service.alerts$))).toEqual([B.id]);
  });

  it('shows an alert again after it disappears and returns', () => {
    setNow(START);
    start(admin('Admin'), [A, B]).dismiss(A.id);

    start(admin('Admin'), [B]);
    expect(storedIds('Admin')).toEqual([A.id]);

    setNow(START + DISMISSAL_FORGET_AFTER_MS);
    start(admin('Admin'), [B]);
    expect(storedIds('Admin')).toBeNull();

    const returned = start(admin('Admin'), [A, B]);
    expect(ids(latest(returned.alerts$))).toEqual([A.id, B.id]);
  });

  it('keeps a dismissal when one response omits the alert and the next reports it', () => {
    const service = start(admin('Admin'), [A, B]);
    service.dismiss(A.id);

    service.refresh();
    http.expectOne(ALERTS_URL).flush([B]);
    service.refresh();
    http.expectOne(ALERTS_URL).flush([A, B]);

    expect(ids(latest(service.alerts$))).toEqual([B.id]);
    expect(storedIds('Admin')).toEqual([A.id]);
  });

  it('keeps a dismissal across a new service instance while the alert is missing', () => {
    start(admin('Admin'), [A, B]).dismiss(A.id);

    start(admin('Admin'), [B]);
    const restarted = start(admin('Admin'), [A, B]);

    expect(ids(latest(restarted.alerts$))).toEqual([B.id]);
  });

  it('records when an alert first went missing and does not move it on a later miss', () => {
    setNow(START);
    const service = start(admin('Admin'), [A, B]);
    service.dismiss(A.id);

    service.refresh();
    http.expectOne(ALERTS_URL).flush([B]);
    const first = new Date(START).toISOString();
    expect(storedMissingSince('Admin')).toEqual({ [A.id]: first });

    setNow(START + 1000);
    service.refresh();
    http.expectOne(ALERTS_URL).flush([B]);
    expect(storedMissingSince('Admin')).toEqual({ [A.id]: first });
  });

  it('forgets a dismissal at exactly the grace period, not just before it', () => {
    setNow(START);
    const service = start(admin('Admin'), [A, B]);
    service.dismiss(A.id);
    service.refresh();
    http.expectOne(ALERTS_URL).flush([B]);

    setNow(START + DISMISSAL_FORGET_AFTER_MS - 1);
    service.refresh();
    http.expectOne(ALERTS_URL).flush([B]);
    expect(storedIds('Admin')).toEqual([A.id]);

    setNow(START + DISMISSAL_FORGET_AFTER_MS);
    service.refresh();
    http.expectOne(ALERTS_URL).flush([B]);
    expect(storedIds('Admin')).toBeNull();
  });

  it('clears the clock when the alert is reported again, so the next miss starts a full period', () => {
    setNow(START);
    const service = start(admin('Admin'), [A, B]);
    service.dismiss(A.id);
    service.refresh();
    http.expectOne(ALERTS_URL).flush([B]);

    setNow(START + 1000);
    service.refresh();
    http.expectOne(ALERTS_URL).flush([A, B]);
    expect(storedMissingSince('Admin')).toBeUndefined();

    const secondMiss = START + DISMISSAL_FORGET_AFTER_MS;
    setNow(secondMiss);
    service.refresh();
    http.expectOne(ALERTS_URL).flush([B]);
    expect(storedMissingSince('Admin')).toEqual({ [A.id]: new Date(secondMiss).toISOString() });

    setNow(secondMiss + DISMISSAL_FORGET_AFTER_MS - 1);
    service.refresh();
    http.expectOne(ALERTS_URL).flush([B]);
    expect(storedIds('Admin')).toEqual([A.id]);
  });

  it('keeps the dismissal when the alert is reported again after longer than the grace period', () => {
    setNow(START);
    const service = start(admin('Admin'), [A, B]);
    service.dismiss(A.id);
    service.refresh();
    http.expectOne(ALERTS_URL).flush([B]);

    setNow(START + 2 * DISMISSAL_FORGET_AFTER_MS);
    service.refresh();
    http.expectOne(ALERTS_URL).flush([A, B]);

    expect(ids(latest(service.alerts$))).toEqual([B.id]);
    expect(storedIds('Admin')).toEqual([A.id]);
  });

  it('drops an unparseable missingSince value and restarts the clock', () => {
    setNow(START);
    localStorage.setItem(dismissedAlertsKey('Admin'), JSON.stringify({
      version: 1,
      ids: [A.id],
      missingSince: { [A.id]: 'not a date' }
    }));

    start(admin('Admin'), [B]);

    expect(storedIds('Admin')).toEqual([A.id]);
    expect(storedMissingSince('Admin')).toEqual({ [A.id]: new Date(START).toISOString() });
  });

  it('clears the clock of an id that is dismissed again', () => {
    const service = start(admin('Admin'), [A, B]);
    service.dismiss(A.id);
    service.refresh();
    http.expectOne(ALERTS_URL).flush([B]);
    expect(storedMissingSince('Admin')).toBeDefined();

    service.dismiss(A.id);

    expect(storedMissingSince('Admin')).toBeUndefined();
  });

  it('writes the plain record while nothing is missing', () => {
    const service = start(admin('Admin'), [A, B]);
    service.dismiss(A.id);

    service.refresh();
    http.expectOne(ALERTS_URL).flush([A, B]);

    expect(JSON.parse(localStorage.getItem(dismissedAlertsKey('Admin'))!)).toEqual({ version: 1, ids: [A.id] });
  });

  it('posts the id when marking handled, removes the alert and fetches again', () => {
    const service = start(admin('Admin'), [A, B]);

    service.markHandled(A.id).subscribe();
    const request = http.expectOne(HANDLED_URL);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ alertId: A.id });
    request.flush(null, { status: 204, statusText: 'No Content' });

    expect(ids(latest(service.alerts$))).toEqual([B.id]);
    expect(ids(latest(service.allAlerts$))).toEqual([B.id]);
    http.expectOne(ALERTS_URL).flush([B]);
  });

  it('keeps the alert and reports the error when marking handled fails', () => {
    const service = start(admin('Admin'), [A, B]);
    const failed = vi.fn();

    service.markHandled(A.id).subscribe({ error: failed });
    http.expectOne(HANDLED_URL).flush({ message: 'No.' }, { status: 500, statusText: 'Server Error' });

    expect(failed).toHaveBeenCalledTimes(1);
    expect(ids(latest(service.alerts$))).toEqual([A.id, B.id]);
    http.expectNone(ALERTS_URL);
  });

  it('merges the old session key into the record once and removes it', () => {
    setNow(START);
    sessionStorage.setItem(LEGACY_DISMISSED_ALERTS_KEY, JSON.stringify([A.id]));

    const migrated = start(admin('Admin'), [A, B]);
    expect(ids(latest(migrated.alerts$))).toEqual([B.id]);
    expect(storedIds('Admin')).toEqual([A.id]);
    expect(sessionStorage.getItem(LEGACY_DISMISSED_ALERTS_KEY)).toBeNull();

    // Pruned once the server has stopped reporting it for the grace period, the old id is not merged back.
    start(admin('Admin'), [B]);
    setNow(START + DISMISSAL_FORGET_AFTER_MS);
    start(admin('Admin'), [B]);
    const returned = start(admin('Admin'), [A, B]);
    expect(ids(latest(returned.alerts$))).toEqual([A.id, B.id]);
  });

  it('reads an unreadable record as no dismissals', () => {
    localStorage.setItem(dismissedAlertsKey('Admin'), '{not json');

    const service = start(admin('Admin'), [A, B]);
    expect(ids(latest(service.alerts$))).toEqual([A.id, B.id]);
  });

  it('shows every alert when storage throws, and keeps a dismissal for the page only', () => {
    localStorage.setItem(dismissedAlertsKey('Admin'), JSON.stringify({ version: 1, ids: [A.id] }));
    const blocked = () => {
      throw new DOMException('The operation is insecure.', 'SecurityError');
    };
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(blocked);
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(blocked);
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(blocked);

    const service = start(admin('Admin'), [A, B]);
    expect(ids(latest(service.alerts$))).toEqual([A.id, B.id]);

    expect(() => service.dismiss(B.id)).not.toThrow();
    expect(ids(latest(service.alerts$))).toEqual([A.id]);

    const next = start(admin('Admin'), [A, B]);
    expect(ids(latest(next.alerts$))).toEqual([A.id, B.id]);
  });

  it('fetches the alerts again on refresh and drops one the server no longer reports', () => {
    const service = start(admin('Admin'), [A, B]);

    service.refresh();
    http.expectOne(ALERTS_URL).flush([B]);

    expect(ids(latest(service.alerts$))).toEqual([B.id]);
    expect(ids(latest(service.allAlerts$))).toEqual([B.id]);
  });

  it('forgets on refresh a dismissal of an alert the server no longer reports', () => {
    setNow(START);
    const service = start(admin('Admin'), [A, B]);
    service.dismiss(A.id);
    expect(storedIds('Admin')).toEqual([A.id]);

    service.refresh();
    http.expectOne(ALERTS_URL).flush([B]);
    expect(storedIds('Admin')).toEqual([A.id]);

    setNow(START + DISMISSAL_FORGET_AFTER_MS);
    service.refresh();
    http.expectOne(ALERTS_URL).flush([B]);

    expect(storedIds('Admin')).toBeNull();
  });

  it('ignores a refresh while a fetch is in flight', () => {
    const service = start(admin('Admin'), [A, B]);

    service.refresh();
    service.refresh();
    const pending = http.match(ALERTS_URL);
    expect(pending.length).toBe(1);
    pending[0].flush([B]);

    service.refresh();
    http.expectOne(ALERTS_URL).flush([A]);
    expect(ids(latest(service.alerts$))).toEqual([A.id]);
  });

  it('fetches nothing on refresh while no admin is signed in', () => {
    const service = start(null, []);

    service.refresh();

    expect(latest(service.alerts$)).toEqual([]);
  });

  it('fetches again when the same admin is emitted again', () => {
    const users = new BehaviorSubject<AuthUser | null>(admin('Admin'));
    auth.user$ = users;
    const service = TestBed.runInInjectionContext(() => new AdminAlertService());
    http.expectOne(ALERTS_URL).flush([A, B]);

    users.next(admin('Admin'));
    http.expectOne(ALERTS_URL).flush([B]);

    expect(ids(latest(service.alerts$))).toEqual([B.id]);
  });

  it('reports dismissed alerts in allAlerts$', () => {
    const service = start(admin('Admin'), [A, B]);
    service.dismiss(A.id);

    expect(ids(latest(service.allAlerts$))).toEqual([A.id, B.id]);
    expect(ids(latest(service.alerts$))).toEqual([B.id]);
  });
});
