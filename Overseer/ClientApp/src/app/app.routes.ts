import { Routes } from '@angular/router';
import { LoginComponent } from './login/login.component';
import { ChatComponent } from './chat/chat.component';
import { SettingsComponent } from './settings/settings.component';
import { DebugLogComponent } from './debug-log/debug-log.component';
import { ApiKeysComponent } from './api-keys/api-keys.component';
import { ModelsComponent } from './models/models.component';
import { inject } from '@angular/core';
import { AuthService } from './services/auth.service';
import { Router } from '@angular/router';
import { map, catchError, of } from 'rxjs';

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
    component: DebugLogComponent,
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
    component: SettingsComponent,
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
    }],
    canDeactivate: [(component: SettingsComponent) => {
      return component.canDeactivate ? component.canDeactivate() : true;
    }]
  },
  { 
    path: 'api-keys', 
    component: ApiKeysComponent,
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
    component: ModelsComponent,
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
    path: 'changelog',
    loadComponent: () => import('./changelog/changelog.component').then(m => m.ChangelogComponent),
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
  { path: '', redirectTo: '/chat', pathMatch: 'full' }
];
