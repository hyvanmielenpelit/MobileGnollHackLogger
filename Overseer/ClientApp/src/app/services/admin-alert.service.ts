import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Subscription } from 'rxjs';
import { AuthService } from './auth.service';

export interface SystemAlert {
  id: string;
  type: string;
  message: string;
}

@Injectable({ providedIn: 'root' })
export class AdminAlertService {
  private http = inject(HttpClient);
  private authService = inject(AuthService);

  private alertsSubject = new BehaviorSubject<SystemAlert[]>([]);
  alerts$ = this.alertsSubject.asObservable();

  private dismissedAlerts = new Set<string>();

  constructor() {
    const saved = sessionStorage.getItem('dismissed_admin_alerts');
    if (saved) {
      try {
        JSON.parse(saved).forEach((id: string) => this.dismissedAlerts.add(id));
      } catch (e) {}
    }

    this.authService.user$.subscribe(user => {
      if (user && user.isAdmin) {
        this.fetchAlerts();
      } else {
        this.alertsSubject.next([]);
      }
    });
  }

  private fetchAlerts() {
    this.http.get<SystemAlert[]>('/api/admin/system-alerts').subscribe({
      next: (alerts) => {
        const visibleAlerts = alerts.filter(a => !this.dismissedAlerts.has(a.id));
        this.alertsSubject.next(visibleAlerts);
      },
      error: (err) => console.error('Failed to fetch admin alerts', err)
    });
  }

  dismiss(id: string) {
    this.dismissedAlerts.add(id);
    sessionStorage.setItem('dismissed_admin_alerts', JSON.stringify(Array.from(this.dismissedAlerts)));
    
    const currentAlerts = this.alertsSubject.value;
    this.alertsSubject.next(currentAlerts.filter(a => a.id !== id));
  }
}
