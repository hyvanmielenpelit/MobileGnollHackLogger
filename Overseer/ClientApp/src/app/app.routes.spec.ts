import { Route } from '@angular/router';
import { routes } from './app.routes';
import { ChatComponent } from './chat/chat.component';
import { LoginComponent } from './login/login.component';
import { AdminComponent } from './admin/admin.component';
import { DebugLogComponent } from './debug-log/debug-log.component';
import { SettingsComponent } from './settings/settings.component';
import { ApiKeysComponent } from './api-keys/api-keys.component';
import { ModelsComponent } from './models/models.component';
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
    ['api-keys', ApiKeysComponent],
    ['models', ModelsComponent],
    ['privacy', PrivacyComponent],
  ];

  for (const [path, expected] of lazyRoutes) {
    it(`loads '${path}' lazily`, async () => {
      const lazy = route(path);
      expect(lazy.component).toBeUndefined();
      expect(await lazy.loadComponent!()).toBe(expected);
    });
  }
});
