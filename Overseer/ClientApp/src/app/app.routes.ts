import { Routes } from '@angular/router';
import { LoginComponent } from './login/login.component';
import { ChatComponent } from './chat/chat.component';
import type { SettingsComponent } from './settings/settings.component';
import { inject } from '@angular/core';
import { AuthService } from './services/auth.service';
import { Router } from '@angular/router';
import { map, catchError, of } from 'rxjs';

const settingsCanActivate = [(route: any, state: any) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.checkAuth().pipe(
    map(user => {
      if (user) return true;
      return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }),
    catchError(() => of(router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } })))
  );
}];

const settingsCanDeactivate = [(component: SettingsComponent) => {
  return component.canDeactivate ? component.canDeactivate() : true;
}];

export const routes: Routes = [
  { path: 'login', component: LoginComponent },
  {
    path: 'admin',
    loadComponent: () => import('./admin/admin.component').then(m => m.AdminComponent),
    canActivate: [(route: any, state: any) => {
      const auth = inject(AuthService);
      const router = inject(Router);
      return auth.checkAuth().pipe(
        map(user => {
          if (user && user.isAdmin) return true;
          return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
        }),
        catchError(() => of(router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } })))
      );
    }]
  },
  { 
    path: 'debug-log', 
    loadComponent: () => import('./debug-log/debug-log.component').then(m => m.DebugLogComponent),
    canActivate: [(route: any, state: any) => {
      const auth = inject(AuthService);
      const router = inject(Router);
      return auth.checkAuth().pipe(
        map(user => {
          if (user) return true;
          return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
        }),
        catchError(() => of(router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } })))
      );
    }]
  },
  { 
    path: 'chat', 
    component: ChatComponent,
    data: { reuse: true },
    canActivate: [(route: any, state: any) => {
      const auth = inject(AuthService);
      const router = inject(Router);
      return auth.checkAuth().pipe(
        map(user => {
          if (user) return true;
          return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
        }),
        catchError(() => of(router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } })))
      );
    }]
  },
  {
    path: 'settings',
    loadComponent: () => import('./settings/settings.component').then(m => m.SettingsComponent),
    canActivate: settingsCanActivate,
    canDeactivate: settingsCanDeactivate
  },
  {
    path: 'settings/:section',
    loadComponent: () => import('./settings/settings.component').then(m => m.SettingsComponent),
    canActivate: settingsCanActivate,
    canDeactivate: settingsCanDeactivate
  },
  { 
    path: 'api-keys', 
    loadComponent: () => import('./api-keys/api-keys.component').then(m => m.ApiKeysComponent),
    canActivate: [(route: any, state: any) => {
      const auth = inject(AuthService);
      const router = inject(Router);
      return auth.checkAuth().pipe(
        map(user => {
          if (user) return true;
          return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
        }),
        catchError(() => of(router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } })))
      );
    }]
  },
  { 
    path: 'models', 
    loadComponent: () => import('./models/models.component').then(m => m.ModelsComponent),
    canActivate: [(route: any, state: any) => {
      const auth = inject(AuthService);
      const router = inject(Router);
      return auth.checkAuth().pipe(
        map(user => {
          if (user) return true;
          return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
        }),
        catchError(() => of(router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } })))
      );
    }]
  },

  // No auth guard: a privacy notice has to be readable by someone who has not signed in.
  { path: 'privacy', loadComponent: () => import('./privacy/privacy.component').then(m => m.PrivacyComponent) },

  { path: '', redirectTo: '/chat', pathMatch: 'full' }
];
