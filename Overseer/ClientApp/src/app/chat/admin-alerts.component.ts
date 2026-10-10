import { Component, ElementRef, ViewChild, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NavigationEnd, Router, RouterLink, UrlTree } from '@angular/router';
import { AdminAlertService, SystemAlert } from '../services/admin-alert.service';
import { Subscription, filter, map } from 'rxjs';
import { ensureOverlayPolyfills } from '../utils/polyfills.util';

/** An alert and the in-app link it carries, if any. */
export interface AdminAlertView {
  alert: SystemAlert;
  link: UrlTree | null;
}

@Component({
  selector: 'app-admin-alerts',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './admin-alerts.component.html',
  styleUrl: './admin-alerts.component.scss'
})
export class AdminAlertsComponent implements OnInit, OnDestroy {
  private adminAlertService = inject(AdminAlertService);
  private router = inject(Router);

  @ViewChild('popoverContainer', { static: true })
  popoverContainer!: ElementRef<HTMLElement>;

  alerts$ = this.adminAlertService.alerts$;
  views$ = this.alerts$.pipe(map(alerts => alerts.map(alert => ({ alert, link: this.linkFor(alert) }))));

  private latestCount = 0;
  private sub = new Subscription();

  ngOnInit() {
    ensureOverlayPolyfills();
    this.sub.add(this.alerts$.subscribe(alerts => {
      this.latestCount = alerts.length;
      this.syncPopover();
    }));
    // A reused route's view loses its open popover while detached, so it is shown again on return.
    this.sub.add(this.router.events.pipe(filter(e => e instanceof NavigationEnd)).subscribe(() => this.syncPopover()));
  }

  ngOnDestroy() {
    this.sub.unsubscribe();
  }

  dismiss(id: string) {
    this.adminAlertService.dismiss(id);
  }

  /** The alert's link as a router URL; null without link text, or for anything but a site-relative path. */
  private linkFor(alert: SystemAlert): UrlTree | null {
    const url = alert.linkUrl?.trim();
    if (!url || !alert.linkText?.trim() || !url.startsWith('/') || url.startsWith('//') || url.startsWith('/\\')) {
      return null;
    }
    try {
      return this.router.parseUrl(url);
    } catch {
      return null;
    }
  }

  private syncPopover(): void {
    const el = this.popoverContainer.nativeElement;
    if (!el.isConnected) {
      return;
    }
    const isOpen = el.matches(':popover-open') || el.classList.contains('\\:popover-open');
    try {
      if (this.latestCount > 0 && !isOpen) {
        el.showPopover();
      } else if (this.latestCount === 0 && isOpen) {
        el.hidePopover();
      }
    } catch {
      // A popover that changed state in between needs nothing more.
    }
  }
}
