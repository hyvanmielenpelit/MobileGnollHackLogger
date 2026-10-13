import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnDestroy,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { Subscription } from 'rxjs';

import {
  AdminBulkAction,
  AdminBulkOperation,
  AdminBulkPreview,
  AdminBulkPreviewRequest,
  AdminUser,
  AdminUserCriteria,
  AdminUserFacetCounts,
  AdminUsersService,
  adminApiError
} from '../../../services/admin-users.service';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { AdminUsersLogFilter } from '../../admin-page.store';
import { missingText } from '../user-action-dialogs/user-action-dialogs';
import { cloneCriteria } from '../user-criteria/user-criteria';
import {
  BulkActionOptions,
  BulkActionStepComponent,
  DEFAULT_BULK_OPTIONS,
  bulkActionInfo,
  bulkActionMissing,
  bulkEffectText,
  bulkPreviewOptions,
  bulkRunLabel
} from './bulk-action-step.component';
import { BulkResultsStepComponent, bulkOperationRunning } from './bulk-results-step.component';
import {
  BulkReviewChoices,
  BulkReviewStepComponent,
  EMPTY_REVIEW_CHOICES,
  bulkExecuteRequest,
  bulkLiveCount,
  bulkReviewMissing
} from './bulk-review-step.component';
import {
  BULK_SAMPLE_SIZE,
  BulkUserPreset,
  BulkUsersStepComponent,
  bulkPresetCriteria,
  bulkPresetOf
} from './bulk-users-step.component';

export type BulkWizardStep = 1 | 2 | 3 | 4;

/** The steps, in the order the step bar shows them. */
export const BULK_WIZARD_STEPS: readonly { step: BulkWizardStep; title: string }[] = [
  { step: 1, title: 'Users' },
  { step: 2, title: 'Action' },
  { step: 3, title: 'Review' },
  { step: 4, title: 'Results' }
];

/** The close hint while an operation runs. */
export const BULK_KEEPS_RUNNING = 'The operation keeps running; see Admin log';

/** A 409 `busy` on execute. */
export const BULK_BUSY_MESSAGE = 'Another bulk operation is running. Wait for it to finish, then run this one.';

/** `The review expired and was refreshed. 2 accounts changed; check the list again.` */
export function bulkRefreshedNote(changed: number): string {
  const what = changed === 0 ? 'No account changed' : changed === 1 ? '1 account changed' : `${changed} accounts changed`;
  return `The review expired and was refreshed. ${what}; check the list again.`;
}

/** The accounts of a preview, eligible and advanced, by user id. */
function previewIds(preview: AdminBulkPreview): Set<string> {
  return new Set([...preview.eligible, ...preview.advanced].map(item => item.userId));
}

/**
 * The Users tab's Bulk Actions wizard: a full-screen modal dialog of four steps, *Users → Action →
 * Review → Results*, the tab opens seeded with the user list's criteria (`open`), or on the results
 * of an operation (`openResults`). Nothing changes until the action button of step 3 runs the
 * operation the server's preview fixed; closing before that changes no account and discards the
 * preview. Its state lives here and is discarded on close.
 */
@Component({
  selector: 'app-admin-bulk-wizard',
  standalone: true,
  imports: [BulkUsersStepComponent, BulkActionStepComponent, BulkReviewStepComponent, BulkResultsStepComponent],
  templateUrl: './admin-bulk-wizard.component.html',
  styleUrl: './admin-bulk-wizard.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager   // matches every other component here
})
export class AdminBulkWizardComponent implements OnDestroy {
  private readonly usersService = inject(AdminUsersService);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('dialog', { static: true }) dialog!: ElementRef<HTMLDialogElement>;

  /** The furthest a lockout may reach (the server's `AdminUserManagement:LockoutMaxDays`). */
  @Input() maxLockoutDays = 365;

  /** After every close, however it closed; the tab returns focus to Bulk Actions. */
  @Output() readonly closed = new EventEmitter<void>();
  /** *Show in admin log*: the wizard has closed; the tab opens the Admin log with this filter. */
  @Output() readonly showInLog = new EventEmitter<AdminUsersLogFilter>();
  /** An operation finished changing accounts; the tab reloads the user list. */
  @Output() readonly operationFinished = new EventEmitter<number>();

  readonly steps = BULK_WIZARD_STEPS;

  /** While the dialog is open; the steps are rendered only then. */
  active = false;
  step: BulkWizardStep = 1;
  /** Opened on the results of an operation: step 4 only. */
  readOnly = false;

  // Step 1
  /** The criteria step 1 starts from; a copy, so the list's own criteria never change. */
  criteria: AdminUserCriteria = {};
  preset: BulkUserPreset = 'custom';
  facetCounts: AdminUserFacetCounts | null = null;
  matches: AdminUser[] = [];
  matchCount: number | null = null;
  matchesLoading = false;
  matchesError: string | null = null;

  // Step 2
  action: AdminBulkAction | null = null;
  options: BulkActionOptions = { ...DEFAULT_BULK_OPTIONS };
  previewBusy = false;
  previewError: string | null = null;
  previewErrorCode: string | null = null;

  // Step 3
  preview: AdminBulkPreview | null = null;
  choices: BulkReviewChoices = EMPTY_REVIEW_CHOICES;
  /** What the action does to each account, as the preview was asked for. */
  effectText = '';
  executeBusy = false;
  executeError: string | null = null;
  refreshedNote: string | null = null;

  // Step 4
  /** The operation whose results are shown, when opened on them or once run. */
  operationId: number | null = null;
  /** The operation as the execute request returned it. */
  startedOperation: AdminBulkOperation | null = null;
  /** The latest state the results step reported. */
  operation: AdminBulkOperation | null = null;

  /** The clock the lockout's end counts from. */
  now: () => Date = () => new Date();

  private matchesSub: Subscription | null = null;
  private facetsSub: Subscription | null = null;
  private countedSearch: string | null = null;
  private previewSub: Subscription | null = null;
  private executeSub: Subscription | null = null;
  /** The operation was seen running, so its end changed accounts the list shows. */
  private sawRunning = false;
  private finishedReported = false;
  private destroyed = false;

  get isOpen(): boolean {
    return this.dialog.nativeElement.open;
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.unsubscribeAll();
    this.discardPreview();
    if (this.isOpen) this.dialog.nativeElement.close();
  }

  /** Opens on step 1, seeded with `criteria`. */
  open(criteria: AdminUserCriteria = {}): void {
    this.unsubscribeAll();
    this.discardPreview();
    this.reset();
    this.criteria = cloneCriteria(criteria);
    this.preset = bulkPresetOf(this.criteria);
    this.step = 1;
    this.loadMatches();
    this.show();
  }

  /** Opens on the results of an operation, read-only. */
  openResults(operationId: number): void {
    this.unsubscribeAll();
    this.discardPreview();
    this.reset();
    this.criteria = {};
    this.operationId = operationId;
    this.readOnly = true;
    this.step = 4;
    this.show();
  }

  close(): void {
    if (this.isOpen) this.dialog.nativeElement.close();
  }

  /** The dialog's native close, by Escape, Close, Done or Show in admin log. */
  onClose(): void {
    if (this.destroyed) return;
    this.unsubscribeAll();
    this.discardPreview();
    this.reset();
    this.active = false;
    this.cdr.detectChanges();
    this.closed.emit();
  }

  // --- The frame ---

  get stepTitle(): string {
    return BULK_WIZARD_STEPS.find(entry => entry.step === this.step)?.title ?? '';
  }

  get subtitle(): string {
    if (this.step === 4 && this.operationId != null) {
      const action = this.operation?.action ?? this.startedOperation?.action ?? null;
      return action ? `Operation #${this.operationId} · ${bulkActionInfo(action).title}` : `Operation #${this.operationId}`;
    }
    return 'Change many accounts at once. No account changes until you run the operation in step 3.';
  }

  /** While the runner may still change accounts. */
  get running(): boolean {
    const op = this.operation ?? this.startedOperation;
    return this.step === 4 && !!op && bulkOperationRunning(op.status);
  }

  get closeHint(): string {
    return this.running ? `Close. ${BULK_KEEPS_RUNNING}.` : 'Close';
  }

  /** Whether a step was passed; the step bar marks it done. */
  isDone(step: BulkWizardStep): boolean {
    return !this.readOnly && step < this.step;
  }

  /** Why Next (step 1 or 2) or the run button (step 3) is unavailable; on step 4, that closing does not stop a run. */
  get footerNote(): string | null {
    switch (this.step) {
      case 1:
        return this.usersBlockedReason;
      case 2:
        return this.previewBusy ? null : missingText(bulkActionMissing(this.action, this.options, this.now(), this.maxLockoutDays));
      case 3:
        if (this.previewBusy) return 'Refreshing the review…';
        if (!this.preview) return null;
        return bulkReviewMissing(this.preview, this.choices);
      default:
        return this.running ? `Closing does not stop it. ${BULK_KEEPS_RUNNING}.` : null;
    }
  }

  get usersBlockedReason(): string | null {
    if (this.matchesError) return 'The matching accounts could not be counted.';
    if (this.matchesLoading || this.matchCount == null) return 'Counting the matching accounts…';
    if (this.matchCount === 0) return 'No account matches these criteria; change them to continue.';
    return null;
  }

  get nextBlocked(): boolean {
    if (this.step === 1) return this.usersBlockedReason !== null || this.matchesLoading;
    if (this.step === 2) return this.previewBusy || bulkActionMissing(this.action, this.options, this.now(), this.maxLockoutDays).length > 0;
    return true;
  }

  get liveCount(): number {
    return this.preview ? bulkLiveCount(this.preview, this.choices) : 0;
  }

  get runLabel(): string {
    const action = this.preview?.action ?? this.action ?? 'Delete';
    return bulkRunLabel(action, this.liveCount);
  }

  get runBlocked(): boolean {
    return !this.preview || this.previewBusy || this.executeBusy || bulkReviewMissing(this.preview, this.choices) !== null;
  }

  get runIsDelete(): boolean {
    return (this.preview?.action ?? this.action) === 'Delete';
  }

  /** The run button of a caution or danger action is the red variant. */
  get runIsRed(): boolean {
    const danger = this.preview?.dangerLevel;
    return danger === 'danger' || danger === 'caution';
  }

  // --- Navigation ---

  next(): void {
    if (this.nextBlocked) return;
    if (this.step === 1) {
      this.goTo(2);
    } else if (this.step === 2) {
      this.runPreview();
    }
  }

  back(): void {
    if (this.step === 3) {
      if (this.executeBusy) return;
      this.previewSub?.unsubscribe();
      this.previewBusy = false;
      this.discardPreview();
      this.preview = null;
      this.choices = EMPTY_REVIEW_CHOICES;
      this.executeError = null;
      this.refreshedNote = null;
      this.goTo(2);
    } else if (this.step === 2) {
      if (this.previewBusy) return;
      this.previewError = null;
      this.previewErrorCode = null;
      this.goTo(1);
    }
  }

  /** Step 2's *Back to Users*, after a delete was refused for want of a narrowing criterion. */
  backToUsers(): void {
    this.previewError = null;
    this.previewErrorCode = null;
    this.goTo(1);
  }

  // --- Step 1 ---

  onPresetChange(preset: BulkUserPreset): void {
    this.preset = preset;
    if (preset !== 'custom') {
      this.criteria = bulkPresetCriteria(preset);
      this.loadMatches();
    }
  }

  onCriteriaChange(criteria: AdminUserCriteria): void {
    this.criteria = cloneCriteria(criteria);
    this.preset = bulkPresetOf(this.criteria);
    this.loadMatches();
  }

  // --- Step 2 ---

  onActionChange(action: AdminBulkAction): void {
    this.action = action;
    this.previewError = null;
    this.previewErrorCode = null;
  }

  onOptionsChange(options: BulkActionOptions): void {
    this.options = options;
  }

  // --- Step 3 ---

  onChoicesChange(choices: BulkReviewChoices): void {
    this.choices = choices;
  }

  /** Runs the reviewed operation. Does nothing until every check passes (the button is aria-disabled). */
  execute(): void {
    const preview = this.preview;
    if (!preview || this.runBlocked) return;
    this.executeBusy = true;
    this.executeError = null;
    this.executeSub?.unsubscribe();
    this.executeSub = this.usersService.bulkExecute(preview.operationId, bulkExecuteRequest(preview, this.choices)).subscribe({
      next: op => {
        this.executeBusy = false;
        // The preview is now the operation: it is never discarded.
        this.preview = null;
        this.operationId = op.id;
        this.startedOperation = op;
        this.operation = op;
        this.sawRunning = true;
        if (!bulkOperationRunning(op.status)) this.reportFinished(op);
        this.goTo(4);
      },
      error: err => {
        this.executeBusy = false;
        const e = adminApiError(err);
        if (e.status === 409 && e.code === 'expired') {
          this.refreshExpiredPreview(preview);
          return;
        }
        this.executeError = e.code === 'busy' ? BULK_BUSY_MESSAGE : e.message;
        this.cdr.markForCheck();
      }
    });
  }

  // --- Step 4 ---

  onOperationChange(op: AdminBulkOperation): void {
    this.operation = op;
    if (bulkOperationRunning(op.status)) {
      this.sawRunning = true;
    } else {
      this.reportFinished(op);
    }
  }

  /** *Show in admin log*: closes, then names the operation for the Admin log. */
  onShowInLog(): void {
    const id = this.operationId;
    this.close();
    if (id != null) this.showInLog.emit({ bulkOperationId: id });
  }

  // --- Internals ---

  private show(): void {
    ensureOverlayPolyfills();
    this.active = true;
    this.cdr.detectChanges();
    const dialog = this.dialog.nativeElement;
    if (!dialog.open) dialog.showModal();
    dialog.querySelector<HTMLElement>('#bw-title')?.focus();
  }

  /** Moves to a step and focuses its heading, never its action button. */
  private goTo(step: BulkWizardStep): void {
    this.step = step;
    this.cdr.detectChanges();
    this.dialog.nativeElement.querySelector<HTMLElement>('#bw-step-heading')?.focus();
  }

  private previewRequest(action: AdminBulkAction): AdminBulkPreviewRequest {
    return {
      action,
      options: bulkPreviewOptions(action, this.options, this.now(), this.maxLockoutDays),
      criteria: cloneCriteria(this.criteria)
    };
  }

  private runPreview(): void {
    const action = this.action;
    if (!action) return;
    this.previewBusy = true;
    this.previewError = null;
    this.previewErrorCode = null;
    this.previewSub?.unsubscribe();
    const request = this.previewRequest(action);
    this.previewSub = this.usersService.bulkPreview(request).subscribe({
      next: preview => {
        this.previewBusy = false;
        this.discardPreview();
        this.preview = preview;
        this.effectText = bulkEffectText(action, request.options.until ?? null);
        this.choices = EMPTY_REVIEW_CHOICES;
        this.executeError = null;
        this.refreshedNote = null;
        this.goTo(3);
      },
      error: err => {
        this.previewBusy = false;
        const e = adminApiError(err);
        this.previewError = e.message;
        this.previewErrorCode = e.code;
        this.cdr.markForCheck();
      }
    });
  }

  /**
   * A 409 `expired`: previews again on the same step, keeps the exclusions that still apply, and
   * clears the acknowledgments, the phrase and the advanced choice.
   */
  private refreshExpiredPreview(expired: AdminBulkPreview): void {
    this.previewBusy = true;
    this.previewSub?.unsubscribe();
    const request = this.previewRequest(expired.action);
    this.previewSub = this.usersService.bulkPreview(request).subscribe({
      next: preview => {
        this.previewBusy = false;
        this.effectText = bulkEffectText(expired.action, request.options.until ?? null);
        const before = previewIds(expired);
        const after = previewIds(preview);
        let changed = 0;
        before.forEach(id => { if (!after.has(id)) changed++; });
        after.forEach(id => { if (!before.has(id)) changed++; });
        const eligible = new Set(preview.eligible.map(item => item.userId));
        this.preview = preview;
        this.choices = { ...EMPTY_REVIEW_CHOICES, excluded: this.choices.excluded.filter(id => eligible.has(id)) };
        this.refreshedNote = bulkRefreshedNote(changed);
        this.executeError = null;
        this.goTo(3);
      },
      error: err => {
        this.previewBusy = false;
        this.preview = expired;
        this.choices = { ...EMPTY_REVIEW_CHOICES, excluded: this.choices.excluded };
        this.executeError = `The review expired and could not be refreshed: ${adminApiError(err).message}`;
        this.cdr.markForCheck();
      }
    });
  }

  /** A preview that was never run is discarded on the server; nothing waits for the answer. */
  private discardPreview(): void {
    const preview = this.preview;
    if (!preview) return;
    this.preview = null;
    this.usersService.bulkCancel(preview.operationId).subscribe({ error: () => {} });
  }

  private reportFinished(op: AdminBulkOperation): void {
    if (this.sawRunning && !this.finishedReported) {
      this.finishedReported = true;
      this.operationFinished.emit(op.id);
    }
  }

  /** The first matching accounts, oldest first, and the facet counts when the search changed. */
  private loadMatches(): void {
    const criteria = this.criteria;
    this.matchesLoading = true;
    this.matchesError = null;
    this.matchesSub?.unsubscribe();
    this.matchesSub = this.usersService.list(criteria, 'oldest', 0, BULK_SAMPLE_SIZE).subscribe({
      next: page => {
        this.matches = page.rows;
        this.matchCount = page.totalCount;
        this.matchesLoading = false;
        this.cdr.markForCheck();
      },
      error: err => {
        this.matches = [];
        this.matchCount = null;
        this.matchesLoading = false;
        this.matchesError = `The matching accounts could not be loaded: ${adminApiError(err).message}`;
        this.cdr.markForCheck();
      }
    });

    const search = criteria.search?.trim() ?? '';
    if (search === this.countedSearch) return;
    this.countedSearch = search;
    this.facetsSub?.unsubscribe();
    this.facetsSub = this.usersService.facets(search || undefined).subscribe({
      next: counts => {
        this.facetCounts = counts;
        this.cdr.markForCheck();
      },
      error: () => {
        this.countedSearch = null;
      }
    });
  }

  private unsubscribeAll(): void {
    this.matchesSub?.unsubscribe();
    this.facetsSub?.unsubscribe();
    this.previewSub?.unsubscribe();
    this.executeSub?.unsubscribe();
  }

  private reset(): void {
    this.step = 1;
    this.readOnly = false;
    this.criteria = {};
    this.preset = 'custom';
    this.facetCounts = null;
    this.countedSearch = null;
    this.matches = [];
    this.matchCount = null;
    this.matchesLoading = false;
    this.matchesError = null;
    this.action = null;
    this.options = { ...DEFAULT_BULK_OPTIONS };
    this.previewBusy = false;
    this.previewError = null;
    this.previewErrorCode = null;
    this.preview = null;
    this.choices = EMPTY_REVIEW_CHOICES;
    this.effectText = '';
    this.executeBusy = false;
    this.executeError = null;
    this.refreshedNote = null;
    this.operationId = null;
    this.startedOperation = null;
    this.operation = null;
    this.sawRunning = false;
    this.finishedReported = false;
  }
}
