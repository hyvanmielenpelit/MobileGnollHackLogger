import { Routes } from '@angular/router';
import { LoginComponent } from './login/login.component';
import { ChatComponent } from './chat/chat.component';
import { SettingsComponent } from './settings/settings.component';
import { inject } from '@angular/core';
import { AuthService } from './services/auth.service';
import { Router } from '@angular/router';
import { map, catchError, of } from 'rxjs';

export const routes: Routes = [
  { path: 'login', component: LoginComponent },
  { 
    path: 'chat', 
    component: ChatComponent,
    canActivate: [() => {
      const auth = inject(AuthService);
      const router = inject(Router);
      return auth.checkAuth().pipe(
        map(user => {
          if (user) return true;
          return router.createUrlTree(['/login']);
        }),
        catchError(() => of(router.createUrlTree(['/login'])))
      );
    }]
  },
  { 
    path: 'settings', 
    component: SettingsComponent,
    canActivate: [() => {
      const auth = inject(AuthService);
      const router = inject(Router);
      return auth.checkAuth().pipe(
        map(user => {
          if (user) return true;
          return router.createUrlTree(['/login']);
        }),
        catchError(() => of(router.createUrlTree(['/login'])))
      );
    }]
  },
  { path: '', redirectTo: '/chat', pathMatch: 'full' }
];
