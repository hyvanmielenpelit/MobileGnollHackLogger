import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import {
  ADMIN_USERS_ENDPOINT,
  AdminUsersService,
  adminApiError,
  bulkConfirmationPhrase
} from './admin-users.service';

const BASE = ADMIN_USERS_ENDPOINT;

describe('AdminUsersService', () => {
  let service: AdminUsersService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [AdminUsersService, provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(AdminUsersService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('uses the controller route prefix', () => {
    expect(BASE).toBe('/api/admin/users');
  });

  it('lists with every criterion as a query parameter, statuses repeated', () => {
    service.list({
      search: ' bob ',
      statuses: ['LockedOut', 'Banned'],
      emailConfirmed: 'no',
      registeredOlderThanDays: 7,
      registeredWithinDays: 30,
      lastSignIn: 'never',
      hasGameData: 'no',
      hasOverseerData: 'yes'
    }, 'oldest', 20, 20).subscribe();

    const req = http.expectOne(r => r.url === BASE);
    expect(req.request.method).toBe('GET');
    const p = req.request.params;
    expect(p.get('search')).toBe('bob');
    expect(p.getAll('statuses')).toEqual(['LockedOut', 'Banned']);
    expect(p.get('emailConfirmed')).toBe('no');
    expect(p.get('registeredOlderThanDays')).toBe('7');
    expect(p.get('registeredWithinDays')).toBe('30');
    expect(p.get('lastSignIn')).toBe('never');
    expect(p.get('hasGameData')).toBe('no');
    expect(p.get('hasOverseerData')).toBe('yes');
    expect(p.get('sort')).toBe('oldest');
    expect(p.get('skip')).toBe('20');
    expect(p.get('take')).toBe('20');
    req.flush({ totalCount: 0, rows: [], nowUtc: '2026-10-13T12:00:00Z' });
  });

  it('leaves empty criteria and a zero skip out of the list query', () => {
    service.list({ search: '  ' }, 'newest', 0, 20).subscribe();
    const req = http.expectOne(r => r.url === BASE);
    expect(req.request.params.keys().sort()).toEqual(['sort', 'take']);
    req.flush({ totalCount: 0, rows: [], nowUtc: '2026-10-13T12:00:00Z' });
  });

  it('counts the facets under the search only', () => {
    service.facets('ali').subscribe();
    const req = http.expectOne(r => r.url === `${BASE}/facets`);
    expect(req.request.params.get('search')).toBe('ali');
    req.flush({});

    service.facets('').subscribe();
    const bare = http.expectOne(r => r.url === `${BASE}/facets`);
    expect(bare.request.params.has('search')).toBe(false);
    bare.flush({});
  });

  it('sends each per-user action to its route and verb', () => {
    const id = 'a b';
    const enc = encodeURIComponent(id);
    const calls: [() => void, string, string, unknown][] = [
      [() => service.lockOut(id, { until: '2026-10-20T15:00:00.000Z', reason: 'Spam' }).subscribe(), 'POST', '/lockout', { until: '2026-10-20T15:00:00.000Z', reason: 'Spam' }],
      [() => service.unlock(id).subscribe(), 'DELETE', '/lockout', null],
      [() => service.ban(id, 'Cheating').subscribe(), 'POST', '/ban', { reason: 'Cheating' }],
      [() => service.unban(id).subscribe(), 'DELETE', '/ban', null],
      [() => service.setGameRestrictions(id, { gameLogBanned: true, bonesBanned: false, reason: 'x' }).subscribe(), 'PUT', '/game-restrictions', { gameLogBanned: true, bonesBanned: false, reason: 'x' }],
      [() => service.signOutEverywhere(id).subscribe(), 'POST', '/sign-out-everywhere', {}],
      [() => service.confirmEmail(id).subscribe(), 'POST', '/confirm-email', {}],
      [() => service.resetTwoFactor(id, ' Verified by phone ').subscribe(), 'POST', '/reset-two-factor', { reason: 'Verified by phone' }],
      [() => service.blockFromAiChat(id, 'Abuse').subscribe(), 'POST', '/overseer-block', { reason: 'Abuse' }],
      [() => service.unblockFromAiChat(id).subscribe(), 'DELETE', '/overseer-block', null],
      [() => service.placeHold(id, 'Fraud').subscribe(), 'POST', '/account-hold', { reason: 'Fraud' }],
      [() => service.releaseHold(id).subscribe(), 'DELETE', '/account-hold', null],
      [() => service.deleteUser(id, { confirmUserName: 'a b', reason: 'r', acknowledgedCategories: ['chats'] }).subscribe(), 'POST', '/delete',
        { confirmUserName: 'a b', reason: 'r', acknowledgedCategories: ['chats'] }]
    ];
    for (const [call, method, suffix, body] of calls) {
      call();
      const req = http.expectOne(`${BASE}/${enc}${suffix}`);
      expect(req.request.method).toBe(method);
      if (body !== null) {
        expect(req.request.body).toEqual(body);
      }
      req.flush({});
    }
  });

  it('sends no reason for a two-factor reset without one', () => {
    service.resetTwoFactor('u1').subscribe();
    const req = http.expectOne(`${BASE}/u1/reset-two-factor`);
    expect(req.request.body).toEqual({});
    req.flush({});
  });

  it('reads one user and the deletion impact', () => {
    service.get('u1').subscribe();
    http.expectOne(`${BASE}/u1`).flush({});
    service.deletionImpact('u1').subscribe();
    const req = http.expectOne(`${BASE}/u1/deletion-impact`);
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('previews, reads, executes and cancels a bulk operation', () => {
    service.bulkPreview({ action: 'Delete', options: { reason: 'Cleanup' }, criteria: { emailConfirmed: 'no' } }).subscribe();
    const preview = http.expectOne(`${BASE}/bulk/preview`);
    expect(preview.request.method).toBe('POST');
    expect(preview.request.body).toEqual({ action: 'Delete', options: { reason: 'Cleanup' }, criteria: { emailConfirmed: 'no' } });
    preview.flush({});

    service.bulkGet(12, { outcome: 'failed', skip: 50, take: 50 }).subscribe();
    const get = http.expectOne(r => r.url === `${BASE}/bulk/12`);
    expect(get.request.params.get('outcome')).toBe('failed');
    expect(get.request.params.get('skip')).toBe('50');
    expect(get.request.params.get('take')).toBe('50');
    get.flush({});

    const execute = { excludedUserIds: ['u2'], includeAdvanced: false, acknowledgments: ['reviewed', 'irreversible'], confirmationPhrase: 'DELETE 3 ACCOUNTS' };
    service.bulkExecute(12, execute).subscribe();
    const exec = http.expectOne(`${BASE}/bulk/12/execute`);
    expect(exec.request.method).toBe('POST');
    expect(exec.request.body).toEqual(execute);
    exec.flush({});

    service.bulkCancel(12).subscribe();
    const cancel = http.expectOne(`${BASE}/bulk/12/cancel`);
    expect(cancel.request.method).toBe('POST');
    cancel.flush({});
  });

  it('reads the bulk history and the report', () => {
    service.bulkHistory({ take: 10, includePreviews: true }).subscribe();
    const history = http.expectOne(r => r.url === `${BASE}/bulk`);
    expect(history.request.params.get('take')).toBe('10');
    expect(history.request.params.get('includePreviews')).toBe('true');
    history.flush({ totalCount: 0, rows: [] });

    expect(service.bulkReportUrl(12)).toBe(`${BASE}/bulk/12/report.csv`);
    service.bulkReport(12).subscribe();
    const report = http.expectOne(`${BASE}/bulk/12/report.csv`);
    expect(report.request.responseType).toBe('blob');
    report.flush(new Blob(['a,b']));
  });

  it('reads the admin log with its filters, kinds comma-separated', () => {
    service.log({ search: 'bob', kinds: ['Banned', 'Deleted'], actor: 'tommi', subjectUserId: 'u1', bulkOperationId: 12, skip: 20, take: 20 }).subscribe();
    const req = http.expectOne(r => r.url === `${BASE}/log`);
    const p = req.request.params;
    expect(p.get('search')).toBe('bob');
    expect(p.get('kinds')).toBe('Banned,Deleted');
    expect(p.get('actor')).toBe('tommi');
    expect(p.get('subjectUserId')).toBe('u1');
    expect(p.get('bulkOperationId')).toBe('12');
    expect(p.get('skip')).toBe('20');
    expect(p.get('take')).toBe('20');
    req.flush({ totalCount: 0, rows: [] });
  });

  it('reads the message and code of an error body', () => {
    const err = new HttpErrorResponse({ status: 409, error: { message: 'Release the deletion hold first.', code: 'hold' } });
    expect(adminApiError(err)).toEqual({ status: 409, message: 'Release the deletion hold first.', code: 'hold' });
    expect(adminApiError(new HttpErrorResponse({ status: 500, error: 'Boom' })).message).toBe('Boom');
    expect(adminApiError(new TypeError('Failed to fetch'))).toEqual({ status: 0, message: 'Failed to fetch', code: null });
  });

  it('builds the bulk delete phrase from the count', () => {
    expect(bulkConfirmationPhrase(37)).toBe('DELETE 37 ACCOUNTS');
    expect(bulkConfirmationPhrase(1)).toBe('DELETE 1 ACCOUNT');
  });
});
