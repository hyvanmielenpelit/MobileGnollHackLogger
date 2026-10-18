import { CanActivateFn, RedirectFunction, Routes } from '@angular/router';
import { LoginComponent } from './login/login.component';
import { ChatComponent } from './chat/chat.component';
import type { SettingsComponent } from './settings/settings.component';
import { inject } from '@angular/core';
import { AuthService, AuthUser } from './services/auth.service';
import { Router } from '@angular/router';
import { map } from 'rxjs';
import { adminUrlMatcher } from './admin/admin-url';

/**
 * Allows a cached user that `allows` at once and revalidates the session in the background;
 * otherwise waits for a sign-in check. Only a signed-out verdict leads to the login page: a check
 * that could not reach the server cancels the navigation and raises the connection-problem notice.
 */
export const requireSignedIn = (allows: (user: AuthUser) => boolean = () => true): CanActivateFn =>
  (_route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const toLogin = (returnUrl: string) => router.createUrlTree(['/login'], { queryParams: { returnUrl } });

    const cached = auth.currentUser;
    if (cached && allows(cached)) {
      auth.revalidate()?.subscribe(result => {
        if (result.kind === 'signedOut') router.navigateByUrl(toLogin(router.url));
      });
      return true;
    }

    return auth.checkAuth().pipe(
      map(result => {
        if (result.kind === 'signedIn' && allows(result.user)) return true;
        if (result.kind === 'unknown') {
          auth.connectionProblemUrl.set(state.url);
          return false;
        }
        return toLogin(state.url);
      })
    );
  };

const settingsCanDeactivate = [(component: SettingsComponent) => {
  return component.canDeactivate ? component.canDeactivate() : true;
}];

/** Redirects to `target` with the query string and fragment kept, which a string `redirectTo` drops. */
const redirectKeepingQuery = (target: string): RedirectFunction =>
  ({ queryParams, fragment }) => inject(Router).createUrlTree([target], { queryParams, fragment: fragment ?? undefined });

export const routes: Routes = [
  { path: 'login', component: LoginComponent },
  {
    matcher: adminUrlMatcher,
    loadComponent: () => import('./admin/admin.component').then(m => m.AdminComponent),
    canActivate: [requireSignedIn(u => !!u.isAdmin)]
  },
  {
    path: 'debug-log',
    loadComponent: () => import('./debug-log/debug-log.component').then(m => m.DebugLogComponent),
    canActivate: [requireSignedIn()]
  },
  {
    path: 'chat',
    component: ChatComponent,
    data: { reuse: true },
    canActivate: [requireSignedIn()]
  },
  {
    path: 'settings',
    loadComponent: () => import('./settings/settings.component').then(m => m.SettingsComponent),
    canActivate: [requireSignedIn()],
    canDeactivate: settingsCanDeactivate
  },
  {
    path: 'settings/:section',
    loadComponent: () => import('./settings/settings.component').then(m => m.SettingsComponent),
    canActivate: [requireSignedIn()],
    canDeactivate: settingsCanDeactivate
  },
  { path: 'api-keys', redirectTo: redirectKeepingQuery('/settings/api-keys') },
  { path: 'models', redirectTo: redirectKeepingQuery('/settings/models') },

  // No auth guard: a privacy notice has to be readable by someone who has not signed in.
  { path: 'privacy', loadComponent: () => import('./privacy/privacy.component').then(m => m.PrivacyComponent) },

  { path: '', redirectTo: '/chat', pathMatch: 'full' }
];
