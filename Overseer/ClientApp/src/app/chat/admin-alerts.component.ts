import { Component, ElementRef, ViewChild, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AdminAlertService } from '../services/admin-alert.service';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-admin-alerts',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './admin-alerts.component.html',
  styleUrl: './admin-alerts.component.scss'
})
export class AdminAlertsComponent implements OnInit, OnDestroy {
  private adminAlertService = inject(AdminAlertService);
  
  @ViewChild('popoverContainer', { static: true }) 
  popoverContainer!: ElementRef<HTMLElement>;

  alerts$ = this.adminAlertService.alerts$;
  private sub!: Subscription;

  ngOnInit() {
    this.sub = this.alerts$.subscribe(alerts => {
      const el = this.popoverContainer.nativeElement;
      const isOpen = el.matches(':popover-open') || el.classList.contains('\\:popover-open');
      
      if (alerts.length > 0 && !isOpen) {
        el.showPopover();
      } else if (alerts.length === 0 && isOpen) {
        el.hidePopover();
      }
    });
  }

  ngOnDestroy() {
    if (this.sub) this.sub.unsubscribe();
  }

  dismiss(id: string) {
    this.adminAlertService.dismiss(id);
  }
}
