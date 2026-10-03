import { Component, ChangeDetectionStrategy, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Observable, forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { AdminService, AiTelemetrySummaryDto, AiGovernorStatusDto } from '../../services/admin.service';
import { ProviderBadgeComponent } from '../../shared/provider-badge/provider-badge.component';
import { AdminPageStore, TelemetryTimeSpan } from '../admin-page.store';

/** The Admin page's AI Telemetry tab: token and cache usage for a date range, and the request governor's partitions. */
@Component({
  selector: 'app-admin-telemetry-tab',
  imports: [CommonModule, FormsModule, ProviderBadgeComponent],
  templateUrl: './admin-telemetry-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-telemetry-tab.component.scss'
})
export class AdminTelemetryTabComponent implements OnInit, OnDestroy {
  private adminService = inject(AdminService);
  private store = inject(AdminPageStore);

  telemetrySummary: AiTelemetrySummaryDto | null = null;
  governorStatus: AiGovernorStatusDto | null = null;
  telemetryLoading = false;
  governorLoading = false;
  todayDate: string = '';

  private governorCountdownTimer?: ReturnType<typeof setInterval>;

  /** The date range lives in the page store, so it survives a tab switch. */
  get telemetryTimeSpan(): TelemetryTimeSpan {
    return this.store.telemetryRange.timeSpan;
  }
  set telemetryTimeSpan(value: TelemetryTimeSpan) {
    this.store.telemetryRange.timeSpan = value;
  }

  get telemetryStartDate(): string {
    return this.store.telemetryRange.start;
  }
  set telemetryStartDate(value: string) {
    this.store.telemetryRange.start = value;
  }

  get telemetryEndDate(): string {
    return this.store.telemetryRange.end;
  }
  set telemetryEndDate(value: string) {
    this.store.telemetryRange.end = value;
  }

  ngOnInit() {
    this.todayDate = this.formatLocalDate(new Date());
    if (!this.telemetryStartDate && !this.telemetryEndDate) {
      this.applyTelemetryPreset(this.telemetryTimeSpan, false);
    }
    this.refreshTelemetryTab();
  }

  ngOnDestroy() {
    this.stopGovernorCountdown();
  }

  formatLocalDate(d: Date): string {
    const year = d.getFullYear();
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  applyTelemetryPreset(preset: '7d' | '28d' | '30d' | '90d' | '180d' | '1y' | 'custom', fetch = true) {
    if (preset !== 'custom') {
      const end = new Date();
      const start = new Date();
      if (preset === '7d') start.setDate(end.getDate() - 7);
      else if (preset === '28d') start.setDate(end.getDate() - 28);
      else if (preset === '30d') start.setDate(end.getDate() - 30);
      else if (preset === '90d') start.setDate(end.getDate() - 90);
      else if (preset === '180d') start.setDate(end.getDate() - 180);
      else if (preset === '1y') start.setFullYear(end.getFullYear() - 1);

      this.telemetryStartDate = this.formatLocalDate(start);
      this.telemetryEndDate = this.formatLocalDate(end);
    } else {
      if (!this.telemetryStartDate || !this.telemetryEndDate) {
        const end = new Date();
        const start = new Date();
        start.setDate(end.getDate() - 30);
        this.telemetryStartDate = this.formatLocalDate(start);
        this.telemetryEndDate = this.formatLocalDate(end);
      }
    }
    if (fetch) {
      this.loadTelemetry();
    }
  }

  onTelemetryTimeSpanChange() {
    this.applyTelemetryPreset(this.telemetryTimeSpan, true);
  }

  onCustomDateChange() {
    if (this.telemetryStartDate && this.telemetryEndDate) {
      if (new Date(this.telemetryEndDate) < new Date(this.telemetryStartDate)) {
        this.telemetryEndDate = this.telemetryStartDate;
      }
    }
    this.loadTelemetry();
  }

  loadTelemetry(showFeedback = false) {
    this.telemetryLoading = true;
    this.adminService.getAiTelemetrySummary(this.telemetryStartDate || undefined, this.telemetryEndDate || undefined).subscribe({
      next: (data) => {
        this.telemetrySummary = data;
        this.telemetryLoading = false;
        if (showFeedback) {
          this.store.showToast('AI Telemetry refreshed.', 'info', 'Telemetry Refreshed');
        }
      },
      error: (err) => {
        console.error('Failed to load telemetry', err);
        this.telemetryLoading = false;
        if (showFeedback) {
          this.store.showToast('Failed to load telemetry: ' + (err.error?.message || err.message), 'error', 'Error');
        }
      }
    });
  }

  /** Partitions currently in a rate-limit cooldown. */
  get rateLimitedKeyCount(): number {
    return this.governorStatus?.activeKeys.filter(k => k.isRateLimited).length ?? 0;
  }

  /** Requests in the summary that the named role lines do not account for. */
  get telemetryOtherRequests(): number {
    const s = this.telemetrySummary;
    if (!s) {
      return 0;
    }
    return Math.max(0, s.totalRequests - (s.totalChatRequests + s.totalTitleRequests + s.totalBenchmarkRequests));
  }

  /**
   * Reloads the summary and the governor status together. Each request settles on its own, so
   * one failure still shows the other's data; the feedback is a single toast either way.
   */
  refreshTelemetryTab(showFeedback = false) {
    this.telemetryLoading = true;
    this.governorLoading = true;
    const failures: string[] = [];
    const settle = <T>(request: Observable<T>, what: string) => request.pipe(
      catchError(err => {
        console.error(`Failed to load ${what}`, err);
        failures.push(`${what}: ${err.error?.message || err.message}`);
        return of(null);
      })
    );

    forkJoin({
      telemetry: settle(this.adminService.getAiTelemetrySummary(this.telemetryStartDate || undefined, this.telemetryEndDate || undefined), 'telemetry'),
      governor: settle(this.adminService.getGovernorStatus(), 'governor status')
    }).subscribe(({ telemetry, governor }) => {
      if (telemetry) {
        this.telemetrySummary = telemetry;
      }
      if (governor) {
        this.setGovernorStatus(governor);
      }
      this.telemetryLoading = false;
      this.governorLoading = false;
      if (!showFeedback) {
        return;
      }
      if (failures.length > 0) {
        this.store.showToast('Failed to load ' + failures.join('; '), 'error', 'Error');
      } else {
        this.store.showToast('AI Telemetry refreshed.', 'info', 'Telemetry Refreshed');
      }
    });
  }

  loadGovernorStatus(showFeedback = false) {
    this.governorLoading = true;
    this.adminService.getGovernorStatus().subscribe({
      next: (data) => {
        this.setGovernorStatus(data);
        this.governorLoading = false;
        if (showFeedback) {
          this.store.showToast('Governor status refreshed.', 'info', 'Governor Refreshed');
        }
      },
      error: (err) => {
        console.error('Failed to load governor status', err);
        this.governorLoading = false;
        if (showFeedback) {
          this.store.showToast('Failed to load governor status: ' + (err.error?.message || err.message), 'error', 'Error');
        }
      }
    });
  }

  private setGovernorStatus(status: AiGovernorStatusDto) {
    this.governorStatus = status;
    this.startGovernorCountdown();
  }

  /**
   * Counts the fetched cooldowns down locally once a second. When the last one reaches zero the
   * status is re-fetched once, so the server's view replaces the local estimate.
   */
  private startGovernorCountdown() {
    this.stopGovernorCountdown();
    if (this.rateLimitedKeyCount === 0) {
      return;
    }
    this.governorCountdownTimer = setInterval(() => {
      for (const key of this.governorStatus?.activeKeys ?? []) {
        if (!key.isRateLimited) {
          continue;
        }
        key.remainingCooldownSeconds = Math.max(0, Math.round((key.remainingCooldownSeconds - 1) * 10) / 10);
        if (key.remainingCooldownSeconds === 0) {
          key.isRateLimited = false;
        }
      }
      if (this.rateLimitedKeyCount === 0) {
        this.stopGovernorCountdown();
        this.loadGovernorStatus();
      }
    }, 1000);
  }

  private stopGovernorCountdown() {
    if (this.governorCountdownTimer !== undefined) {
      clearInterval(this.governorCountdownTimer);
      this.governorCountdownTimer = undefined;
    }
  }

  /** Without a key, clears every partition; refused while nothing is rate-limited (the button is aria-disabled). */
  resetGovernorCooldown(credentialKey?: string) {
    if (!credentialKey && this.rateLimitedKeyCount === 0) {
      return;
    }
    this.adminService.resetGovernorCooldown(credentialKey).subscribe({
      next: () => {
        this.store.showToast(credentialKey ? `Cooldown cleared for ${credentialKey}.` : 'All rate-limit cooldowns cleared.', 'success', 'Cooldown Reset');
        this.loadGovernorStatus();
      },
      error: (err) => {
        this.store.showToast('Failed to reset cooldown: ' + (err.error?.message || err.message), 'error', 'Error');
      }
    });
  }
}
