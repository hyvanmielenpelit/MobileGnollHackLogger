import { Component, inject, OnInit, OnDestroy, ChangeDetectionStrategy } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { AuthService } from '../services/auth.service';
import { SystemService } from '../services/system.service';
import { Router, ActivatedRoute } from '@angular/router';

@Component({
    selector: 'app-login',
    imports: [FormsModule],
    templateUrl: './login.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './login.component.scss'
})
export class LoginComponent implements OnInit, OnDestroy {
  authService = inject(AuthService);
  systemService = inject(SystemService);
  router = inject(Router);
  route = inject(ActivatedRoute);

  appVersion = '';

  username = '';
  password = '';
  loading = false;
  error = '';

  private boundSyncAriaBlur: any;
  private boundSyncAriaInput: any;

  ngOnInit() {
    this.systemService.getVersion().subscribe({
      next: (v) => this.appVersion = v,
      error: () => {}
    });

    // Call checkAuth to refresh the CSRF token (useful if we just logged out, so the token resets to anonymous)
    this.authService.checkAuth().subscribe();

    const syncAria = (el: any) => {
      if (el && el.setAttribute && el.matches) {
        el.setAttribute('aria-invalid', el.matches(':user-invalid') ? 'true' : 'false');
      }
    };
    this.boundSyncAriaBlur = (e: any) => syncAria(e.target);
    this.boundSyncAriaInput = (e: any) => {
      if (e.target && e.target.hasAttribute && e.target.hasAttribute('aria-invalid')) syncAria(e.target);
    };
    
    document.addEventListener('blur', this.boundSyncAriaBlur, true);
    document.addEventListener('input', this.boundSyncAriaInput);
  }

  ngOnDestroy() {
    document.removeEventListener('blur', this.boundSyncAriaBlur, true);
    document.removeEventListener('input', this.boundSyncAriaInput);
  }

  onSubmit() {
    if (!this.username || !this.password) {
      return; // Prevent empty submission
    }

    this.loading = true;
    this.error = '';
    this.authService.login(this.username, this.password).subscribe({
      next: () => {
        const returnUrl = this.route.snapshot.queryParams['returnUrl'] || '/chat';
        this.router.navigateByUrl(returnUrl);
      },
      error: () => {
        this.error = 'Invalid credentials';
        this.loading = false;
      }
    });
  }
}
