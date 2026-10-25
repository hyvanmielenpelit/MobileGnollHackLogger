import { Component, ElementRef, ViewChild, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NavigationEnd, Router, RouterLink, UrlTree } from '@angular/router';
import { AdminAlertService, SystemAlert } from '../services/admin-alert.service';
import { Subscription, filter, map } from 'rxjs';
import { ensureOverlayPolyfills } from '../utils/polyfills.util';

/** An alert, the in-app link it carries, if any, and its severity. */
export interface AdminAlertView {
  alert: SystemAlert;
  link: UrlTree | null;
  severity: 'warning' | 'error';
  severityLabel: string;
}

/** `dialog:modal` where the browser knows it; elsewhere any open dialog, the safe side. */
const MODAL_DIALOG_SELECTOR = supportsSelector('dialog:modal') ? 'dialog:modal' : 'dialog[open]';

function supportsSelector(selector: string): boolean {
  try {
    return CSS.supports(`selector(${selector})`);
  } catch {
    return false;
  }
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
  views$ = this.alerts$.pipe(map(alerts => alerts.map((alert): AdminAlertView => {
    const severity = this.severityOf(alert);
    return { alert, link: this.linkFor(alert), severity, severityLabel: severity === 'error' ? 'Error' : 'Warning' };
  })));

  private latestCount = 0;
  private previousPath: string | null = null;
  private sub = new Subscription();
  private dialogObserver: MutationObserver | null = null;

  ngOnInit() {
    ensureOverlayPolyfills();
    this.sub.add(this.alerts$.subscribe(alerts => {
      this.latestCount = alerts.length;
      this.syncPopover();
    }));
    // A reused route's view loses its open popover while detached, so it is shown again on return.
    // Entering the chat from another route refetches, since an Admin page may have cleared a condition.
    this.sub.add(this.router.events.pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd)).subscribe(e => {
      const path = e.urlAfterRedirects.split(/[?#]/, 1)[0];
      if (path === '/chat' && this.previousPath !== '/chat') {
        this.adminAlertService.refresh();
      }
      this.previousPath = path;
      this.syncPopover();
    }));
    // A popover shown after a modal dialog sits above it but is inert, so alerts wait for it to close.
    this.dialogObserver = new MutationObserver(() => this.syncPopover());
    this.dialogObserver.observe(document.body, { subtree: true, attributes: true, attributeFilter: ['open'] });
  }

  ngOnDestroy() {
    this.sub.unsubscribe();
    this.dialogObserver?.disconnect();
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

  /** The alert's severity; any type but `error` reads as a warning. */
  private severityOf(alert: SystemAlert): 'warning' | 'error' {
    return alert.type === 'error' ? 'error' : 'warning';
  }

  private syncPopover(): void {
    const el = this.popoverContainer.nativeElement;
    if (!el.isConnected) {
      return;
    }
    const isOpen = el.matches(':popover-open') || el.classList.contains('\\:popover-open');
    const show = this.latestCount > 0 && !this.modalOpen();
    try {
      if (show && !isOpen) {
        el.showPopover();
      } else if (!show && isOpen) {
        el.hidePopover();
      }
    } catch {
      // A popover that changed state in between needs nothing more.
    }
  }

  private modalOpen(): boolean {
    return document.querySelector(MODAL_DIALOG_SELECTOR) !== null;
  }
}
