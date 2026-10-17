import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';

import { AuthCheckResult, AuthService, AuthUser } from './auth.service';

const ME = '/api/auth/me';
const USER: AuthUser = { userName: 'player', email: 'player@example.com', hasApiKey: true };

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** Runs checkAuth() and answers its request with `respond`. */
  function check(respond: (req: TestRequest) => void) {
    const results: AuthCheckResult[] = [];
    let errored = false;
    let completed = false;
    service.checkAuth().subscribe({
      next: r => results.push(r),
      error: () => { errored = true; },
      complete: () => { completed = true; }
    });
    respond(http.expectOne(ME));
    return { results, errored, completed };
  }

  function signIn() {
    check(req => req.flush(USER));
  }

  it('reports signedIn for a 200 with a user and publishes the user', () => {
    const seen: (AuthUser | null)[] = [];
    service.user$.subscribe(u => seen.push(u));

    const { results } = check(req => req.flush(USER));

    expect(results).toEqual([{ kind: 'signedIn', user: USER }]);
    expect(service.currentUser).toEqual(USER);
    expect(seen.at(-1)).toEqual(USER);
  });

  it('sends the no-cache headers', () => {
    service.checkAuth().subscribe();
    const req = http.expectOne(ME);
    expect(req.request.headers.get('Cache-Control')).toBe('no-cache');
    expect(req.request.headers.get('Pragma')).toBe('no-cache');
    req.flush(null);
  });

  it('reports signedOut for a 200 with a null body and clears the user', () => {
    signIn();
    const { results } = check(req => req.flush(null));
    expect(results).toEqual([{ kind: 'signedOut' }]);
    expect(service.currentUser).toBeNull();
  });

  it('reports signedOut for a 401, clears the user and completes without an error', () => {
    signIn();
    const { results, errored, completed } = check(req => req.flush(null, { status: 401, statusText: 'Unauthorized' }));
    expect(results).toEqual([{ kind: 'signedOut' }]);
    expect(errored).toBe(false);
    expect(completed).toBe(true);
    expect(service.currentUser).toBeNull();
  });

  it('reports unknown for a network error and keeps the cached user', () => {
    signIn();
    const { results, errored } = check(req => req.error(new ProgressEvent('error'), { status: 0 }));
    expect(results.map(r => r.kind)).toEqual(['unknown']);
    expect(errored).toBe(false);
    expect(service.currentUser).toEqual(USER);
  });

  it('reports unknown for a 503 and keeps the cached user', () => {
    signIn();
    const seen: (AuthUser | null)[] = [];
    service.user$.subscribe(u => seen.push(u));

    const { results } = check(req => req.flush('down', { status: 503, statusText: 'Service Unavailable' }));

    expect(results.map(r => r.kind)).toEqual(['unknown']);
    expect(service.currentUser).toEqual(USER);
    expect(seen).toEqual([USER]);
  });

  describe('revalidate()', () => {
    it('makes no request within a minute of a confirmed check', () => {
      const now = vi.spyOn(Date, 'now').mockReturnValue(1_000_000);
      signIn();
      now.mockReturnValue(1_000_000 + AuthService.REVALIDATE_AFTER_MS - 1);
      expect(service.revalidate()).toBeNull();
      http.expectNone(ME);
    });

    it('makes a request once a minute has passed', () => {
      const now = vi.spyOn(Date, 'now').mockReturnValue(1_000_000);
      signIn();
      now.mockReturnValue(1_000_000 + AuthService.REVALIDATE_AFTER_MS);

      const results: AuthCheckResult[] = [];
      service.revalidate()!.subscribe(r => results.push(r));
      http.expectOne(ME).flush(USER);

      expect(results).toEqual([{ kind: 'signedIn', user: USER }]);
      expect(service.revalidate()).toBeNull();
    });

    it('shares one request between concurrent callers', () => {
      const first: AuthCheckResult[] = [];
      const second: AuthCheckResult[] = [];
      service.revalidate()!.subscribe(r => first.push(r));
      service.revalidate()!.subscribe(r => second.push(r));

      http.expectOne(ME).flush(USER);

      expect(first).toEqual([{ kind: 'signedIn', user: USER }]);
      expect(second).toEqual([{ kind: 'signedIn', user: USER }]);
    });

    it('checks again after markSignedOut()', () => {
      signIn();
      service.markSignedOut();
      expect(service.currentUser).toBeNull();

      service.revalidate()!.subscribe();
      http.expectOne(ME).flush(null);
    });

    it('checks again after logout()', () => {
      signIn();
      service.logout().subscribe();
      http.expectOne('/api/auth/logout').flush({});
      expect(service.currentUser).toBeNull();

      service.revalidate()!.subscribe();
      http.expectOne(ME).flush(null);
    });

    it('counts a sign-in as a confirmed check', () => {
      service.login('player', 'secret').subscribe();
      http.expectOne('/api/auth/login').flush(USER);
      expect(service.currentUser).toEqual(USER);
      expect(service.revalidate()).toBeNull();
    });
  });
});
