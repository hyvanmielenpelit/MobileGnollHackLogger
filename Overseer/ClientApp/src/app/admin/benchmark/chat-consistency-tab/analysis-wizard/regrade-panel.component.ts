import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { Subscription, timer } from 'rxjs';

import { SystemAiConfigDto } from '../../../../services/admin.service';
import { AdminChatConsistencyService, ccErrorText } from '../../../../services/admin-chat-consistency.service';
import { ModelPickerComponent, ModelPickerOption } from '../../../../shared/model-picker/model-picker.component';
import { formatUsd, plural, regradeStatusText } from '../chat-consistency-format';
import { CcRegradeEstimate, CcRegradeJob } from '../chat-consistency.models';

/** The re-grade job's poll interval, and the longest pause the back-off grows to after failures. */
export const CC_REGRADE_POLL_MS = 2000;
export const CC_REGRADE_POLL_MAX_MS = 30_000;

/**
 * *Re-grade with a common assessor*: the assessor, an estimate first, a confirmation dialog showing
 * that estimate, and only then the start (`confirmed: true`); then the job's progress, polled, with
 * Cancel. A job already running when the panel opens is picked up.
 */
@Component({
  selector: 'app-cc-regrade-panel',
  standalone: true,
  imports: [ModelPickerComponent],
  templateUrl: './regrade-panel.component.html',
  styleUrls: ['./regrade-panel.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcRegradePanelComponent implements OnInit, OnDestroy {
  private readonly service = inject(AdminChatConsistencyService);
  private readonly cdr = inject(ChangeDetectorRef);

  /** The runs to re-grade: the selected target runs of both periods and the selected controls. */
  @Input() runIds: readonly number[] = [];
  /** The benchmark-capable assessor configurations, as the launcher lists them. */
  @Input() pickerOptions: readonly ModelPickerOption<SystemAiConfigDto>[] = [];
  @Input() pickerEmptyHint: string | null = null;

  /** A job finished (completed, with errors, canceled or failed); the run table is read again. */
  @Output() readonly finished = new EventEmitter<CcRegradeJob>();

  @ViewChild('confirmDialog') confirmDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('estimateButton') estimateButton?: ElementRef<HTMLButtonElement>;

  assessorId: number | null = null;
  estimate: CcRegradeEstimate | null = null;
  estimating = false;
  estimateError: string | null = null;
  starting = false;
  startError: string | null = null;
  job: CcRegradeJob | null = null;
  cancelError: string | null = null;
  canceling = false;

  private estimateSub: Subscription | null = null;
  private startSub: Subscription | null = null;
  private pollSub: Subscription | null = null;
  private cancelSub: Subscription | null = null;
  private pollDelay = CC_REGRADE_POLL_MS;

  ngOnInit(): void {
    // A re-grade started elsewhere, or before a reload, is shown and followed.
    this.pollSub = this.service.getRegradeJob().subscribe({
      next: job => {
        if (job && job.status === 'running') {
          this.job = job;
          this.schedulePoll();
        }
        this.cdr.markForCheck();
      },
      error: () => { /* No job information: the panel starts idle. */ }
    });
  }

  ngOnDestroy(): void {
    for (const sub of [this.estimateSub, this.startSub, this.pollSub, this.cancelSub]) {
      sub?.unsubscribe();
    }
  }

  get running(): boolean {
    return this.job?.status === 'running';
  }

  /** Why Estimate cannot be asked now, or ''. */
  get estimateBlocked(): string {
    if (this.running) return 'A re-grade is in progress.';
    if (this.runIds.length === 0) return 'Select runs to re-grade.';
    if (this.assessorId === null) return 'Choose the common assessor.';
    return '';
  }

  get progressText(): string {
    const job = this.job;
    if (!job) return '';
    const state = regradeStatusText(job.status);
    const current = job.status === 'running' && job.currentRunId !== null ? `, now run #${job.currentRunId}` : '';
    return `${state}: ${job.done} of ${plural(job.total, 'run')} re-graded${current}.`;
  }

  get totalText(): string {
    const estimate = this.estimate;
    if (!estimate) return '';
    return estimate.pricingAvailable && estimate.estimatedTotalCostUsd !== null
      ? `about ${formatUsd(estimate.estimatedTotalCostUsd)}`
      : 'unknown: the assessor has no published price';
  }

  get refusedRuns(): { runId: number; refusal: string }[] {
    return (this.estimate?.runs ?? [])
      .filter(run => !run.eligible)
      .map(run => ({ runId: run.runId, refusal: run.refusal || 'Not eligible.' }));
  }

  get confirmBlocked(): boolean {
    const estimate = this.estimate;
    return !estimate || !!estimate.assessorRefusal || estimate.eligibleRunCount === 0 || this.starting;
  }

  selectAssessor(config: SystemAiConfigDto | null): void {
    this.assessorId = config?.id ?? null;
    this.estimate = null;
    this.estimateError = null;
    this.cdr.markForCheck();
  }

  /** Estimate: asks the server what the re-grade would cost, then opens the confirmation with it. */
  requestEstimate(): void {
    if (this.estimateBlocked || this.estimating || this.assessorId === null) return;
    this.estimating = true;
    this.estimateError = null;
    this.startError = null;
    this.estimateSub?.unsubscribe();
    this.estimateSub = this.service.estimateRegrade(this.runIds, this.assessorId).subscribe({
      next: estimate => {
        this.estimating = false;
        this.estimate = estimate;
        this.cdr.detectChanges();
        const dialog = this.confirmDialog?.nativeElement;
        if (dialog && !dialog.open) dialog.showModal();
      },
      error: err => {
        this.estimating = false;
        this.estimateError = ccErrorText(err, 'The re-grade could not be estimated.');
        this.cdr.markForCheck();
      }
    });
  }

  cancelConfirm(): void {
    this.confirmDialog?.nativeElement.close();
    this.estimateButton?.nativeElement.focus();
  }

  /** The confirmation's Re-grade: the only path that starts a re-grade, always with `confirmed: true`. */
  confirm(): void {
    const estimate = this.estimate;
    if (this.confirmBlocked || !estimate) return;
    this.starting = true;
    this.startError = null;
    this.cdr.markForCheck();
    this.startSub?.unsubscribe();
    this.startSub = this.service.startRegrade(this.runIds, estimate.assessorConfigId).subscribe({
      next: job => {
        this.starting = false;
        this.confirmDialog?.nativeElement.close();
        this.job = job;
        this.pollDelay = CC_REGRADE_POLL_MS;
        this.schedulePoll();
        this.cdr.markForCheck();
      },
      error: err => {
        this.starting = false;
        this.startError = ccErrorText(err, 'The re-grade could not be started.');
        this.cdr.markForCheck();
      }
    });
  }

  cancelJob(): void {
    if (!this.running || this.canceling) return;
    this.canceling = true;
    this.cancelError = null;
    this.cancelSub?.unsubscribe();
    this.cancelSub = this.service.cancelRegrade().subscribe({
      next: job => {
        this.canceling = false;
        if (job) this.job = job;
        this.cdr.markForCheck();
      },
      error: err => {
        this.canceling = false;
        this.cancelError = ccErrorText(err, 'The re-grade could not be canceled.');
        this.cdr.markForCheck();
      }
    });
  }

  /** One poll after the current delay; a failure doubles the delay up to the cap, a success resets it. */
  private schedulePoll(): void {
    this.pollSub?.unsubscribe();
    this.pollSub = timer(this.pollDelay).subscribe(() => {
      this.pollSub = this.service.getRegradeJob().subscribe({
        next: job => {
          this.pollDelay = CC_REGRADE_POLL_MS;
          if (job) this.job = job;
          if (job && job.status === 'running') {
            this.schedulePoll();
          } else if (job) {
            this.finished.emit(job);
          }
          this.cdr.markForCheck();
        },
        error: () => {
          this.pollDelay = Math.min(this.pollDelay * 2, CC_REGRADE_POLL_MAX_MS);
          this.schedulePoll();
        }
      });
    });
  }

  stopNested(event: Event): void {
    event.stopPropagation();
  }
}
