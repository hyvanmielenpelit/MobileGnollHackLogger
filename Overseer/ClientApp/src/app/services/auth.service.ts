import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, tap, catchError, of } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private http = inject(HttpClient);
  
  private userSubject = new BehaviorSubject<{ userName: string, email: string, hasApiKey: boolean } | null>(null);
  user$ = this.userSubject.asObservable();

  checkAuth() {
    return this.http.get<any>('/api/auth/me').pipe(
      tap(res => this.userSubject.next(res)),
      catchError(() => {
        this.userSubject.next(null);
        return of(null);
      })
    );
  }

  login(userName: string, password: string) {
    return this.http.post<any>('/api/auth/login', { userName, password }).pipe(
      tap(res => this.userSubject.next(res))
    );
  }

  logout() {
    return this.http.post('/api/auth/logout', {}).pipe(
      tap(() => this.userSubject.next(null))
    );
  }
}
