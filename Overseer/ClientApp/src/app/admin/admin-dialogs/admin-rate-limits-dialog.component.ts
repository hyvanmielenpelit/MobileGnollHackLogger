import { Component, ChangeDetectionStrategy, ElementRef, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminService } from '../../services/admin.service';
import { AdminPageStore } from '../admin-page.store';
import { AdminConfirmDialogComponent } from './admin-confirm-dialog.component';

/** Usage counters and limits of a system configuration, or of a user's or group's assignment of one. */
@Component({
  selector: 'app-admin-rate-limits-dialog',
  imports: [FormsModule, AdminConfirmDialogComponent],
  templateUrl: './admin-rate-limits-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-rate-limits-dialog.component.scss'
})
export class AdminRateLimitsDialogComponent {
  private adminService = inject(AdminService);
  private store = inject(AdminPageStore);

  @ViewChild('rateLimitsDialog', { static: true }) rateLimitsDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild(AdminConfirmDialogComponent, { static: true }) confirmDialog!: AdminConfirmDialogComponent;

  chatLimitsMap = [
    { label: 'Daily Requests', countField: 'dailyChatRequestsCount', limitField: 'maxDailyChatRequests', backendCounterName: 'DailyChatRequestsCount' },
    { label: 'Monthly Requests', countField: 'monthlyChatRequestsCount', limitField: 'maxMonthlyChatRequests', backendCounterName: 'MonthlyChatRequestsCount' },
    { label: 'Total Requests', countField: 'totalChatRequestsCount', limitField: 'maxTotalChatRequests', backendCounterName: 'TotalChatRequestsCount' },
    { label: 'Daily Tokens', countField: 'dailyChatTokensCount', limitField: 'maxDailyChatTokens', backendCounterName: 'DailyChatTokensCount' },
    { label: 'Monthly Tokens', countField: 'monthlyChatTokensCount', limitField: 'maxMonthlyChatTokens', backendCounterName: 'MonthlyChatTokensCount' },
    { label: 'Total Tokens', countField: 'totalChatTokensCount', limitField: 'maxTotalChatTokens', backendCounterName: 'TotalChatTokensCount' },
  ];
  titleLimitsMap = [
    { label: 'Daily Requests', countField: 'dailyTitleRequestsCount', limitField: 'maxDailyTitleRequests', backendCounterName: 'DailyTitleRequestsCount' },
    { label: 'Monthly Requests', countField: 'monthlyTitleRequestsCount', limitField: 'maxMonthlyTitleRequests', backendCounterName: 'MonthlyTitleRequestsCount' },
    { label: 'Total Requests', countField: 'totalTitleRequestsCount', limitField: 'maxTotalTitleRequests', backendCounterName: 'TotalTitleRequestsCount' },
    { label: 'Daily Tokens', countField: 'dailyTitleTokensCount', limitField: 'maxDailyTitleTokens', backendCounterName: 'DailyTitleTokensCount' },
    { label: 'Monthly Tokens', countField: 'monthlyTitleTokensCount', limitField: 'maxMonthlyTitleTokens', backendCounterName: 'MonthlyTitleTokensCount' },
    { label: 'Total Tokens', countField: 'totalTitleTokensCount', limitField: 'maxTotalTitleTokens', backendCounterName: 'TotalTitleTokensCount' },
  ];

  selectedConfigForLimits: any = null;
  limitContext: 'system' | 'user' | 'group' = 'system';
  limitEntityId: number = 0;
  activeRateLimitTab: 'chat' | 'title' = 'chat';

  /** Tab order for the rate limits dialog's nested tab row. */
  readonly rateLimitTabs: { id: 'chat' | 'title'; label: string }[] = [
    { id: 'chat',  label: 'Chat Usage' },
    { id: 'title', label: 'Title Generation Usage' }
  ];

  /** Roving-tabindex keyboard support for the rate limits dialog tab row. */
  onRateLimitTabKeydown(event: KeyboardEvent, index: number): void {
    const targets: Record<string, number> = {
      ArrowRight: index + 1,
      ArrowLeft: index - 1,
      Home: 0,
      End: this.rateLimitTabs.length - 1
    };
    const requested = targets[event.key];
    if (requested === undefined) {
      return;
    }

    event.preventDefault();
    const next = (requested + this.rateLimitTabs.length) % this.rateLimitTabs.length;
    const tab = this.rateLimitTabs[next].id;
    this.activeRateLimitTab = tab;
    document.getElementById(`rate-limit-tab-${tab}`)?.focus();
  }

  open(context: 'system' | 'user' | 'group', entity: any) {
    this.limitContext = context;
    this.selectedConfigForLimits = entity;
    this.limitEntityId = entity.id;
    this.activeRateLimitTab = 'chat';
    this.rateLimitsDialog.nativeElement.showModal();
  }

  closeRateLimitsDialog() {
    this.rateLimitsDialog.nativeElement.close();
    this.selectedConfigForLimits = null;
    this.cancelEditLimit();
  }

  editingLimitField: string | null = null;
  editingLimitValue: number | null = null;

  startEditLimit(limitField: string, currentValue: number | null) {
    this.editingLimitField = limitField;
    this.editingLimitValue = currentValue;
  }

  cancelEditLimit() {
    this.editingLimitField = null;
    this.editingLimitValue = null;
  }

  saveEditLimit() {
    if (!this.editingLimitField || !this.selectedConfigForLimits) return;

    // Use full assignment for user/group, or find full config object for system
    const basePayload = this.limitContext === 'system'
      ? this.store.configs.find(c => c.id === this.limitEntityId)
      : this.selectedConfigForLimits;

    if (!basePayload) return;

    const payload = {
      ...basePayload,
      [this.editingLimitField]: this.editingLimitValue === null || this.editingLimitValue === undefined || (this.editingLimitValue as any) === '' ? null : Number(this.editingLimitValue)
    };

    let req: any;
    if (this.limitContext === 'system') {
      req = this.adminService.updateSystemConfig(this.limitEntityId, payload as any);
    } else if (this.limitContext === 'user') {
      req = this.adminService.updateUserSystemConfig(this.limitEntityId, payload as any);
    } else {
      req = this.adminService.updateGroupSystemConfig(this.limitEntityId, payload as any);
    }

    req.subscribe({
      next: () => {
        this.selectedConfigForLimits[this.editingLimitField!] = payload[this.editingLimitField!];

        // Also update the underlying local list if it's a system config
        if (this.limitContext === 'system') {
          const current = this.store.configs.find(c => c.id === this.limitEntityId);
          if (current) {
            this.store.replaceConfig({ ...current, ...payload } as any);
          }
        }

        this.cancelEditLimit();
      },
      error: (err: any) => console.error("Failed to update limit", err)
    });
  }

  resetSingleCounter(counterName: string, backendCounterName: string) {
    this.confirmDialog.open(
      {
        title: 'Reset Counter',
        message: `Are you sure you want to reset the counter?`,
        buttonText: 'Reset',
        buttonClass: 'btn-gh btn-gh-delete'
      },
      () => {
        let req;
        if (this.limitContext === 'system') req = this.adminService.resetSystemConfig(this.limitEntityId, backendCounterName);
        else if (this.limitContext === 'user') req = this.adminService.resetUserSystemConfig(this.limitEntityId, backendCounterName);
        else req = this.adminService.resetGroupSystemConfig(this.limitEntityId, backendCounterName);

        req.subscribe({
          next: () => {
            if (this.selectedConfigForLimits) {
              this.selectedConfigForLimits[counterName] = 0;
            }
          },
          error: (err) => console.error("Failed to reset counter", err)
        });
      }
    );
  }
}
