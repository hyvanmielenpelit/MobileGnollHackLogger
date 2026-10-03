import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { AdminService } from '../../services/admin.service';
import { AdminPageStore } from '../admin-page.store';

/** The Admin page's Developer Tools tab: deliberate frontend and backend errors for testing Sentry. */
@Component({
  selector: 'app-admin-devtools-tab',
  templateUrl: './admin-devtools-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-devtools-tab.component.scss'
})
export class AdminDevtoolsTabComponent {
  private adminService = inject(AdminService);
  private store = inject(AdminPageStore);

  triggerFrontendSentryError() {
    this.store.showToast('Triggering frontend exception...', 'info', 'Sentry Test');
    throw new Error('Sentry Frontend Crash Test triggered by Admin');
  }

  triggerBackendSentryError() {
    this.store.showToast('Sending backend crash request...', 'info', 'Sentry Test');
    this.adminService.triggerBackendSentryError().subscribe({
      next: () => this.store.showToast('Backend crash request completed unexpectedly successfully.', 'success', 'Sentry Test'),
      error: () => this.store.showToast('Backend crash request completed (check Sentry!)', 'info', 'Sentry Test')
    });
  }
}
