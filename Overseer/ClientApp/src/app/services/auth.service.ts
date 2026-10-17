import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { BehaviorSubject, Observable, tap, catchError, of, map, finalize, shareReplay } from 'rxjs';

export interface AuthUser { userName: string; email: string; hasApiKey: boolean; isAdmin?: boolean; }

/** `unknown` is a check that did not reach a verdict (network error, 5xx, 429): it never signs the user out. */
export type AuthCheckResult =
  | { kind: 'signedIn'; user: AuthUser }
  | { kind: 'signedOut' }
  | { kind: 'unknown'; error: unknown };

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  /** A cached user is revalidated by a guarded navigation at most this often. */
  static readonly REVALIDATE_AFTER_MS = 60_000;

  private http = inject(HttpClient);

  private userSubject = new BehaviorSubject<AuthUser | null>(null);
  user$ = this.userSubject.asObservable();

  /** The URL of a navigation canceled because the sign-in check could not reach the server. */
  connectionProblemUrl = signal<string | null>(null);

  private lastConfirmedAt = 0;
  private inFlight: Observable<AuthCheckResult> | null = null;

  get currentUser(): AuthUser | null {
    return this.userSubject.value;
  }

  checkAuth(): Observable<AuthCheckResult> {
    return this.http.get<AuthUser | null>('/api/auth/me', {
      headers: {
        'Cache-Control': 'no-cache',
        'Pragma': 'no-cache',
        'Expires': '0'
      }
    }).pipe(
      map((user): AuthCheckResult => {
        if (!user) {
          this.markSignedOut();
          return { kind: 'signedOut' };
        }
        this.publishUser(user);
        return { kind: 'signedIn', user };
      }),
      catchError((error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401) {
          this.markSignedOut();
          return of<AuthCheckResult>({ kind: 'signedOut' });
        }
        return of<AuthCheckResult>({ kind: 'unknown', error });
      })
    );
  }

  /** A shared check when the last confirmed one is older than REVALIDATE_AFTER_MS, else `null`. */
  revalidate(): Observable<AuthCheckResult> | null {
    if (this.inFlight) return this.inFlight;
    if (Date.now() - this.lastConfirmedAt < AuthService.REVALIDATE_AFTER_MS) return null;
    this.inFlight = this.checkAuth().pipe(
      finalize(() => { this.inFlight = null; }),
      shareReplay({ bufferSize: 1, refCount: false })
    );
    return this.inFlight;
  }

  markSignedOut() {
    this.lastConfirmedAt = 0;
    this.userSubject.next(null);
  }

  login(userName: string, password: string) {
    return this.http.post<any>('/api/auth/login', { userName, password }).pipe(
      /* A 200 carrying requiresTwoFactor is not a signed-in user: publishing it would let the
         router treat the half-finished sign-in as complete. */
      tap(res => { if (!res?.requiresTwoFactor) this.publishUser(res); })
    );
  }

  loginTwoFactor(code: string, rememberMachine: boolean) {
    return this.http.post<any>('/api/auth/login/2fa', { code, rememberMachine }).pipe(
      tap(res => this.publishUser(res))
    );
  }

  logout() {
    return this.http.post('/api/auth/logout', {}).pipe(
      tap(() => this.markSignedOut())
    );
  }

  private publishUser(user: AuthUser | null) {
    this.lastConfirmedAt = user ? Date.now() : 0;
    this.userSubject.next(user);
  }
}
