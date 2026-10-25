import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject, Observable } from 'rxjs';

import {
  AdminAlertService,
  LEGACY_DISMISSED_ALERTS_KEY,
  SystemAlert,
  dismissedAlertsKey
} from './admin-alert.service';
import { AuthService, AuthUser } from './auth.service';

const ALERTS_URL = '/api/admin/system-alerts';

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
    start(admin('Admin'), [A, B]).dismiss(A.id);

    start(admin('Admin'), [B]);
    expect(storedIds('Admin')).toBeNull();

    const returned = start(admin('Admin'), [A, B]);
    expect(ids(latest(returned.alerts$))).toEqual([A.id, B.id]);
  });

  it('merges the old session key into the record once and removes it', () => {
    sessionStorage.setItem(LEGACY_DISMISSED_ALERTS_KEY, JSON.stringify([A.id]));

    const migrated = start(admin('Admin'), [A, B]);
    expect(ids(latest(migrated.alerts$))).toEqual([B.id]);
    expect(storedIds('Admin')).toEqual([A.id]);
    expect(sessionStorage.getItem(LEGACY_DISMISSED_ALERTS_KEY)).toBeNull();

    // Pruned once the server stops reporting it, the old id is not merged back.
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

  it('reports dismissed alerts in allAlerts$', () => {
    const service = start(admin('Admin'), [A, B]);
    service.dismiss(A.id);

    expect(ids(latest(service.allAlerts$))).toEqual([A.id, B.id]);
    expect(ids(latest(service.alerts$))).toEqual([B.id]);
  });
});
