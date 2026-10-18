import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { Route, Router, provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { ChatComponent } from './chat/chat.component';
import { LoginComponent } from './login/login.component';
import { AdminComponent } from './admin/admin.component';
import { DebugLogComponent } from './debug-log/debug-log.component';
import { SettingsComponent } from './settings/settings.component';
import { PrivacyComponent } from './privacy/privacy.component';
import { TermsComponent } from './terms/terms.component';
import { AuthService, AuthUser } from './services/auth.service';
import { adminUrlMatcher } from './admin/admin-url';

describe('app routes', () => {
  const route = (path: string): Route => {
    const found = routes.find(r => r.path === path);
    expect(found, `route '${path}'`).toBeDefined();
    return found!;
  };

  /** The admin route has a matcher, not a path. */
  const adminRoute = (): Route => {
    const found = routes.find(r => r.matcher === adminUrlMatcher);
    expect(found, 'admin route').toBeDefined();
    return found!;
  };

  it('loads the chat eagerly, as the reused landing page', () => {
    const chat = route('chat');
    expect(chat.component).toBe(ChatComponent);
    expect(chat.loadComponent).toBeUndefined();
    expect(chat.data?.['reuse']).toBe(true);
  });

  it('loads the login page eagerly', () => {
    const login = route('login');
    expect(login.component).toBe(LoginComponent);
    expect(login.loadComponent).toBeUndefined();
  });

  const lazyRoutes: [string, () => Route, unknown][] = [
    ['admin', adminRoute, AdminComponent],
    ['debug-log', () => route('debug-log'), DebugLogComponent],
    ['settings', () => route('settings'), SettingsComponent],
    ['settings/:section', () => route('settings/:section'), SettingsComponent],
    ['privacy', () => route('privacy'), PrivacyComponent],
    ['terms', () => route('terms'), TermsComponent],
  ];

  for (const [name, find, expected] of lazyRoutes) {
    it(`loads '${name}' lazily`, async () => {
      const lazy = find();
      expect(lazy.component).toBeUndefined();
      expect(await lazy.loadComponent!()).toBe(expected);
    });
  }

  @Component({ template: '' })
  class SectionStubComponent {}

  describe('admin paths', () => {
    let router: Router;

    beforeEach(() => {
      TestBed.configureTestingModule({
        providers: [provideRouter([{ matcher: adminRoute().matcher, component: SectionStubComponent }])]
      });
      router = TestBed.inject(Router);
    });

    for (const url of ['/admin', '/admin/users/groups', '/admin/gnollbench/suites', '/admin/configs?resolveConfig=3']) {
      it(`reaches the admin route from ${url}`, async () => {
        expect(await router.navigateByUrl(url)).toBe(true);
        expect(router.url).toBe(url);
      });
    }

    it('does not match a four-segment admin path', async () => {
      await expect(router.navigateByUrl('/admin/users/groups/extra')).rejects.toThrow();
    });
  });

  /** Navigates `url` through one real route, landing on a stub so no guard or HTTP call runs. */
  async function navigateThrough(path: string, url: string): Promise<string> {
    TestBed.configureTestingModule({
      providers: [provideRouter([route(path), { path: 'settings/:section', component: SectionStubComponent }])]
    });
    const router = TestBed.inject(Router);
    await router.navigateByUrl(url);
    return router.url;
  }

  const redirects: [string, string][] = [
    ['api-keys', '/settings/api-keys'],
    ['models', '/settings/models'],
  ];

  for (const [path, target] of redirects) {
    it(`redirects '${path}' to its Settings section`, async () => {
      const redirect = route(path);
      expect(redirect.loadComponent).toBeUndefined();
      expect(redirect.component).toBeUndefined();
      expect(await navigateThrough(path, `/${path}`)).toBe(target);
    });

    it(`keeps the query parameters and fragment across the '${path}' redirect`, async () => {
      expect(await navigateThrough(path, `/${path}?embedded=1&theme=dark#top`)).toBe(`${target}?embedded=1&theme=dark#top`);
    });
  }

  describe('auth guards', () => {
    const ME = '/api/auth/me';
    const USER: AuthUser = { userName: 'player', email: 'player@example.com', hasApiKey: true };
    const ADMIN: AuthUser = { ...USER, isAdmin: true };
    const LOGIN_RETURN = (url: string) => `/login?returnUrl=${encodeURIComponent(url)}`;

    let router: Router;
    let auth: AuthService;
    let http: HttpTestingController;

    beforeEach(() => {
      TestBed.configureTestingModule({
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          provideRouter([
            { path: 'login', component: SectionStubComponent },
            { matcher: adminUrlMatcher, component: SectionStubComponent, canActivate: adminRoute().canActivate },
            { path: 'settings/:section', component: SectionStubComponent, canActivate: route('settings/:section').canActivate }
          ])
        ]
      });
      router = TestBed.inject(Router);
      auth = TestBed.inject(AuthService);
      http = TestBed.inject(HttpTestingController);
    });

    afterEach(() => http.verify());

    /** The pending sign-in check, once the guard has issued it. */
    const pendingCheck = () => vi.waitFor(() => http.expectOne(ME));

    /** Signs in through a blocking guard check on `url`. */
    async function signInAt(url: string, user: AuthUser = USER) {
      const nav = router.navigateByUrl(url);
      (await pendingCheck()).flush(user);
      expect(await nav).toBe(true);
    }

    /** Moves the clock past the revalidation interval. */
    function makeCheckDue() {
      const realNow = Date.now.bind(Date);
      vi.spyOn(Date, 'now').mockImplementation(() => realNow() + AuthService.REVALIDATE_AFTER_MS + 1);
    }

    it('lets a signed-in user through when nobody is cached', async () => {
      await signInAt('/settings/models');
      expect(router.url).toBe('/settings/models');
      expect(auth.currentUser).toEqual(USER);
    });

    it('sends a signed-out user (200 null) to the login page with a return URL', async () => {
      const nav = router.navigateByUrl('/settings/models');
      (await pendingCheck()).flush(null);
      await nav;
      expect(router.url).toBe(LOGIN_RETURN('/settings/models'));
    });

    it('sends a 401 to the login page with a return URL', async () => {
      const nav = router.navigateByUrl('/settings/models');
      (await pendingCheck()).flush(null, { status: 401, statusText: 'Unauthorized' });
      await nav;
      expect(router.url).toBe(LOGIN_RETURN('/settings/models'));
    });

    const failures: [string, (req: TestRequest) => void][] = [
      ['a network error', req => req.error(new ProgressEvent('error'), { status: 0 })],
      ['a 503', req => req.flush('down', { status: 503, statusText: 'Service Unavailable' })],
    ];

    for (const [label, fail] of failures) {
      it(`cancels the navigation and raises the connection notice on ${label} when nobody is cached`, async () => {
        const nav = router.navigateByUrl('/settings/models');
        fail(await pendingCheck());
        expect(await nav).toBe(false);
        expect(router.url).toBe('/');
        expect(auth.connectionProblemUrl()).toBe('/settings/models');
        expect(auth.currentUser).toBeNull();
      });
    }

    it('switches Settings sections without a sign-in request', async () => {
      await signInAt('/settings/models');

      expect(await router.navigateByUrl('/settings/api-keys')).toBe(true);

      http.expectNone(ME);
      expect(router.url).toBe('/settings/api-keys');
    });

    it('stays on the page and keeps the user when a due background check fails', async () => {
      await signInAt('/settings/models');
      makeCheckDue();

      expect(await router.navigateByUrl('/settings/api-keys')).toBe(true);
      (await pendingCheck()).flush('down', { status: 503, statusText: 'Service Unavailable' });
      await new Promise(resolve => setTimeout(resolve));

      expect(router.url).toBe('/settings/api-keys');
      expect(auth.currentUser).toEqual(USER);
    });

    it('goes to the login page when a due background check finds the user signed out', async () => {
      await signInAt('/settings/models');
      makeCheckDue();

      expect(await router.navigateByUrl('/settings/api-keys')).toBe(true);
      (await pendingCheck()).flush(null);

      await vi.waitFor(() => expect(router.url).toBe(LOGIN_RETURN('/settings/api-keys')));
      expect(auth.currentUser).toBeNull();
    });

    it('checks a cached non-admin before admitting them to admin', async () => {
      await signInAt('/settings/models');

      const nav = router.navigateByUrl('/admin');
      (await pendingCheck()).flush(ADMIN);

      expect(await nav).toBe(true);
      expect(router.url).toBe('/admin');
    });

    it('sends a non-admin away from admin to the login page', async () => {
      await signInAt('/settings/models');

      const nav = router.navigateByUrl('/admin');
      (await pendingCheck()).flush(USER);
      await nav;

      expect(router.url).toBe(LOGIN_RETURN('/admin'));
    });
  });
});
