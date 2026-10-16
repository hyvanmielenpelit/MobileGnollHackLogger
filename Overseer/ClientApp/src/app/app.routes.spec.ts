import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Route, Router, provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { ChatComponent } from './chat/chat.component';
import { LoginComponent } from './login/login.component';
import { AdminComponent } from './admin/admin.component';
import { DebugLogComponent } from './debug-log/debug-log.component';
import { SettingsComponent } from './settings/settings.component';
import { PrivacyComponent } from './privacy/privacy.component';

describe('app routes', () => {
  const route = (path: string): Route => {
    const found = routes.find(r => r.path === path);
    expect(found, `route '${path}'`).toBeDefined();
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

  const lazyRoutes: [string, unknown][] = [
    ['admin', AdminComponent],
    ['debug-log', DebugLogComponent],
    ['settings', SettingsComponent],
    ['settings/:section', SettingsComponent],
    ['privacy', PrivacyComponent],
  ];

  for (const [path, expected] of lazyRoutes) {
    it(`loads '${path}' lazily`, async () => {
      const lazy = route(path);
      expect(lazy.component).toBeUndefined();
      expect(await lazy.loadComponent!()).toBe(expected);
    });
  }

  @Component({ template: '' })
  class SectionStubComponent {}

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
});
