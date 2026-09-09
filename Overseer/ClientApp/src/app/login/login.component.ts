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

  /* The password step succeeded and a second factor is outstanding. The server holds that
     state in the two-factor cookie; this flag only decides which step the form shows. */
  requiresTwoFactor = false;
  twoFactorCode = '';
  rememberMachine = false;

  private boundSyncAriaBlur: any;
  private boundSyncAriaInput: any;

  ngOnInit() {
    this.systemService.getVersion().subscribe({
      next: (v) => this.appVersion = v,
      error: () => {}
    });

    // Call checkAuth to refresh the CSRF token (useful if we just logged out, so the token resets to anonymous)
    this.authService.checkAuth().subscribe({
      error: () => {}
    });

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
    if (this.requiresTwoFactor) {
      this.onSubmitTwoFactor();
      return;
    }

    if (!this.username || !this.password) {
      return; // Prevent empty submission
    }

    this.loading = true;
    this.error = '';
    this.authService.login(this.username, this.password).subscribe({
      next: (res) => {
        if (res?.requiresTwoFactor) {
          this.requiresTwoFactor = true;
          this.password = ''; // Not needed again, and no reason to keep it in memory
          this.loading = false;
          this.error = '';
          return;
        }
        this.navigateAfterLogin();
      },
      error: (err) => {
        /* The server deliberately returns the same body for an unknown user and a wrong
           password, so there is nothing here to distinguish them by. A lockout or a
           not-allowed account says so explicitly, because the password was correct. */
        this.error = err?.error?.message || 'Invalid credentials';
        this.loading = false;
      }
    });
  }

  onSubmitTwoFactor() {
    if (!this.twoFactorCode) {
      return;
    }

    this.loading = true;
    this.error = '';
    this.authService.loginTwoFactor(this.twoFactorCode, this.rememberMachine).subscribe({
      next: () => this.navigateAfterLogin(),
      error: (err) => {
        this.error = err?.error?.message || 'Invalid verification code';
        this.twoFactorCode = '';
        this.loading = false;
      }
    });
  }

  cancelTwoFactor() {
    this.requiresTwoFactor = false;
    this.twoFactorCode = '';
    this.rememberMachine = false;
    this.error = '';
  }

  private navigateAfterLogin() {
    const returnUrl = this.route.snapshot.queryParams['returnUrl'] || '/chat';
    this.router.navigateByUrl(returnUrl);
  }
}
