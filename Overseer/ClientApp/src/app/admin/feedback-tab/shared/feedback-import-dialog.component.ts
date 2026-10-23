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
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import {
  FeedbackResolutionApplyResult,
  FeedbackResolutionConflictItem,
  FeedbackResolutionPreview,
  FeedbackResolutionPreviewItem,
  FeedbackResolutionRequest,
  FeedbackTransferService,
  feedbackResolutionConflict,
  feedbackResolutionInvalid
} from '../../../services/feedback-transfer.service';
import { adminApiError } from '../../../services/admin-users.service';
import { FilePickerComponent } from '../../../shared/file-picker/file-picker.component';
import { TextDiffComponent, TextDiffView } from '../../../shared/text-diff/text-diff.component';
import { DiffResult, diffLines } from '../../../utils/text-diff.util';
import { parseFeedbackResolutionYaml, type ParseIssue } from './feedback-resolution-yaml';
import type { FeedbackSource } from './feedback-view-storage';

export const MAX_RESOLUTION_FILE_BYTES = 2 * 1024 * 1024;

export type FeedbackImportStep = 1 | 2 | 3;
export type FeedbackImportInput = 'paste' | 'file';

/** A badge of a review card. `tone` picks its color; its words carry the meaning. */
export interface FeedbackImportBadge {
  text: string;
  tone: 'none' | 'change' | 'warning' | 'info' | 'danger';
}

/** One preview item with what the review step shows of it, computed once. */
export interface FeedbackImportCard {
  item: FeedbackResolutionPreviewItem;
  heading: string;
  badges: FeedbackImportBadge[];
  statusLine: string;
  versionLine: string;
  noteDiff: DiffResult;
  resolutionDiff: DiffResult;
  showNote: boolean;
  showResolution: boolean;
}

const STATUS_LABELS: Readonly<Record<string, string>> = {
  new: 'New',
  acknowledged: 'Acknowledged',
  resolved: 'Resolved',
  regressed: 'Regressed',
  dismissed: 'Dismissed'
};

/** An issue status in words; an unknown wire value is shown as it came. */
export function feedbackStatusLabel(status: string | null | undefined): string {
  if (!status) return '(none)';
  return STATUS_LABELS[status.toLowerCase()] ?? status;
}

/** `Issue #42` for Overseer; `Issue 3f2a9c1b0d4e` (the fingerprint's first 12 characters) for GnollBench. */
export function feedbackIssueHeading(source: FeedbackSource, id: number | null, fingerprint: string): string {
  return source === 'overseer' && id !== null ? `Issue #${id}` : `Issue ${fingerprint.slice(0, 12)}`;
}

/** The badges of a preview item, in the order the card shows them. */
export function feedbackImportBadges(item: FeedbackResolutionPreviewItem): FeedbackImportBadge[] {
  const badges: FeedbackImportBadge[] = [];
  if (item.errors.length > 0) {
    badges.push({ text: 'cannot be applied', tone: 'danger' });
  } else if (!item.hasChanges) {
    badges.push({ text: 'no changes', tone: 'none' });
  } else {
    if (item.changes.status) {
      badges.push({ text: `status ${feedbackStatusLabel(item.current?.status)} → ${feedbackStatusLabel(item.after?.status)}`, tone: 'change' });
    }
    if (item.changes.note) badges.push({ text: 'note', tone: 'change' });
    if (item.changes.resolution) badges.push({ text: 'resolution', tone: 'change' });
    if (item.changes.fixedInVersion) badges.push({ text: 'fixed in version', tone: 'change' });
  }
  if (item.triagedSinceExport) badges.push({ text: 'Triaged since export', tone: 'warning' });
  const since = item.occurrencesSinceExport ?? 0;
  if (since > 0) badges.push({ text: `${since} new ${since === 1 ? 'occurrence' : 'occurrences'} since export`, tone: 'info' });
  if (item.regressedSinceExport) badges.push({ text: 'Regressed since export', tone: 'danger' });
  return badges;
}

/** `Old → New`, or `Value (unchanged)` when the item leaves it. */
function changeLine(before: string | null | undefined, after: string | null | undefined, changed: boolean, label: (v: string | null | undefined) => string): string {
  return changed ? `${label(before)} → ${label(after)}` : `${label(before)} (unchanged)`;
}

const plain = (value: string | null | undefined): string => value ? value : '(none)';

/**
 * Import resolutions: a resolution file (`overseer-feedback-resolutions`) is pasted or chosen, checked in the
 * browser (R1–R12), previewed by the server, reviewed card by card and applied. The apply sends back the status
 * and triage time the preview showed, so an issue changed in between is refused (409) instead of overwritten.
 */
@Component({
  selector: 'app-feedback-import-dialog',
  standalone: true,
  imports: [FormsModule, FilePickerComponent, TextDiffComponent],
  templateUrl: './feedback-import-dialog.component.html',
  styleUrls: ['./feedback-import-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FeedbackImportDialogComponent implements OnDestroy {
  private transfer = inject(FeedbackTransferService);
  private cdr = inject(ChangeDetectorRef);

  @ViewChild('dialog') dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('stepHeading') stepHeading?: ElementRef<HTMLElement>;

  @Input({ required: true }) source!: FeedbackSource;

  /** After a successful apply. */
  @Output() imported = new EventEmitter<FeedbackResolutionApplyResult>();

  readonly steps: ReadonlyArray<{ id: FeedbackImportStep; label: string }> = [
    { id: 1, label: 'Provide YAML' },
    { id: 2, label: 'Review' },
    { id: 3, label: 'Done' }
  ];

  step: FeedbackImportStep = 1;
  input: FeedbackImportInput = 'paste';
  pastedText = '';
  fileText: string | null = null;
  fileName = '';
  fileSize = 0;
  fileError: string | null = null;

  validating = false;
  errors: ParseIssue[] = [];
  notices: string[] = [];
  validSummary: string | null = null;
  private validatedText: string | null = null;
  private request: FeedbackResolutionRequest | null = null;

  preview: FeedbackResolutionPreview | null = null;
  cards: FeedbackImportCard[] = [];
  view: TextDiffView = 'side';
  acknowledged = false;
  previewing = false;

  applying = false;
  applyErrors: string[] = [];
  conflicts: FeedbackResolutionConflictItem[] | null = null;
  conflictMessage = '';
  result: FeedbackResolutionApplyResult | null = null;

  private previewSub: Subscription | undefined;
  private applySub: Subscription | undefined;

  get idPrefix(): string {
    return `${this.source}-feedback-import`;
  }

  get subTabName(): string {
    return this.source === 'gnollbench' ? 'GnollBench' : 'Overseer';
  }

  get title(): string {
    return `Import resolutions into the ${this.subTabName} sub-tab`;
  }

  get currentText(): string {
    return this.input === 'paste' ? this.pastedText : (this.fileText ?? '');
  }

  get busy(): boolean {
    return this.validating || this.previewing || this.applying;
  }

  get canValidate(): boolean {
    return !this.busy && this.currentText.trim() !== '';
  }

  get canReview(): boolean {
    return !this.busy && this.validatedText !== null && this.validatedText === this.currentText;
  }

  get changeCount(): number {
    return this.preview?.changeCount ?? 0;
  }

  get needsAcknowledgment(): boolean {
    return (this.preview?.triagedSinceExportCount ?? 0) > 0;
  }

  /** Why Apply is unavailable on the review step; null when it is available. */
  get applyBlockedReason(): string | null {
    const preview = this.preview;
    if (!preview) return 'Review the file first.';
    if (this.applying) return 'The changes are being applied.';
    if (this.previewing) return 'The file is being reviewed again.';
    if (this.conflicts) return 'Issues changed after the review. Review the file again before applying.';
    if (preview.errorCount > 0) {
      return preview.errorCount === 1
        ? 'One issue cannot be applied. Correct the file and import it again.'
        : `${preview.errorCount} issues cannot be applied. Correct the file and import it again.`;
    }
    if (preview.changeCount === 0) return 'The file changes nothing.';
    if (this.needsAcknowledgment && !this.acknowledged) return 'Confirm that you have reviewed the issues changed since the export.';
    return null;
  }

  get applyLabel(): string {
    const n = this.changeCount;
    return `Apply ${n} ${n === 1 ? 'change' : 'changes'}`;
  }

  get outcomeText(): string {
    const preview = this.preview;
    if (!preview) return '';
    const total = preview.items.length;
    return `This file changes ${preview.changeCount} of ${total} ${total === 1 ? 'issue' : 'issues'} in the ${this.subTabName} sub-tab.`;
  }

  get doneSummary(): string {
    const result = this.result;
    if (!result) return '';
    return `Changed ${result.changed} ${result.changed === 1 ? 'issue' : 'issues'}; ${result.unchanged} unchanged.`;
  }

  // ---- Opening and closing -------------------------------------------------------------------

  open(): void {
    this.reset();
    const el = this.dialog?.nativeElement;
    if (el && !el.open) el.showModal();
    this.render();
  }

  close(): void {
    if (this.applying) return;
    this.cancelRequests();
    this.dialog?.nativeElement?.close();
  }

  /** Escape: an apply in flight is not abandoned half way. */
  onCancel(event: Event): void {
    if (this.applying) event.preventDefault();
  }

  /** The dialog closed by any route; a preview in flight is dropped. */
  onClosed(): void {
    if (!this.applying) this.cancelRequests();
  }

  // ---- Step 1 --------------------------------------------------------------------------------

  setInput(input: FeedbackImportInput): void {
    this.input = input;
    this.clearValidation();
    this.render();
  }

  onPastedTextChange(text: string): void {
    this.pastedText = text;
    this.clearValidation();
    this.render();
  }

  async loadFile(file: File): Promise<void> {
    this.fileError = null;
    if (file.size > MAX_RESOLUTION_FILE_BYTES) {
      this.fileText = null;
      this.fileName = '';
      this.fileSize = 0;
      this.fileError = `${file.name} is ${formatBytes(file.size)}; the limit is 2 MB.`;
    } else {
      try {
        this.fileText = await file.text();
        this.fileName = file.name;
        this.fileSize = file.size;
      } catch {
        this.fileText = null;
        this.fileError = `Could not read ${file.name}.`;
      }
    }
    this.clearValidation();
    this.render();
  }

  clearFile(): void {
    this.fileText = null;
    this.fileName = '';
    this.fileSize = 0;
    this.fileError = null;
    this.clearValidation();
    this.render();
  }

  get fileSizeLabel(): string {
    return formatBytes(this.fileSize);
  }

  /** The browser's pass over the text: R1–R12. True when it found no error. */
  async validate(): Promise<boolean> {
    const text = this.currentText;
    if (text.trim() === '') {
      this.errors = [{ line: null, message: this.input === 'paste' ? 'Paste a resolution file first.' : 'Choose a file first.' }];
      this.render();
      return false;
    }
    this.validating = true;
    this.render();

    const parsed = await parseFeedbackResolutionYaml(text, this.source);

    this.validating = false;
    // The text changed while the parser ran; this verdict belongs to text that is no longer there.
    if (text !== this.currentText) {
      this.render();
      return false;
    }
    this.errors = parsed.errors;
    this.notices = parsed.notices;
    this.request = parsed.request;
    if (parsed.errors.length > 0 || !parsed.request) {
      this.validatedText = null;
      this.validSummary = null;
      this.render();
      return false;
    }
    const n = parsed.request.items.length;
    this.validatedText = text;
    this.validSummary = `Valid: ${n} ${n === 1 ? 'issue' : 'issues'} for the ${this.subTabName} sub-tab.`;
    this.render();
    return true;
  }

  /** Checks the text again, then asks the server for the preview and moves to the review. */
  async review(): Promise<void> {
    if (this.busy || this.currentText.trim() === '') return;
    if (!(await this.validate())) return;
    this.runPreview(true);
  }

  // ---- Step 2 --------------------------------------------------------------------------------

  setView(view: TextDiffView): void {
    this.view = view;
    this.render();
  }

  onAcknowledgedChange(event: Event): void {
    this.acknowledged = (event.target as HTMLInputElement).checked;
    this.render();
  }

  back(): void {
    if (this.applying) return;
    this.previewSub?.unsubscribe();
    this.previewing = false;
    this.clearReview();
    this.goToStep(1);
  }

  /** After a conflict: the same file is previewed again against the issues as they are now. */
  reviewAgain(): void {
    if (this.applying || this.previewing) return;
    this.runPreview(false);
  }

  apply(): void {
    if (this.applyBlockedReason !== null || !this.preview || !this.request) return;
    const preview = this.preview;
    const request: FeedbackResolutionRequest = {
      ...this.request,
      items: this.request.items.map((item, index) => {
        const current = preview.items.find(p => p.index === index)?.current ?? null;
        return { ...item, expectedStatus: current?.status ?? 'new', expectedTriagedUtc: current?.triagedUtc ?? null };
      })
    };

    this.applying = true;
    this.applyErrors = [];
    this.render();
    this.applySub?.unsubscribe();
    this.applySub = this.transfer.applyResolutions(request).subscribe({
      next: result => {
        this.applying = false;
        this.result = result;
        this.imported.emit(result);
        this.goToStep(3);
      },
      error: err => {
        this.applying = false;
        const conflict = feedbackResolutionConflict(err);
        const invalid = conflict ? null : feedbackResolutionInvalid(err);
        if (conflict) {
          this.conflicts = conflict.conflicts;
          this.conflictMessage = conflict.message || 'Issues changed after the review, so nothing was applied.';
        } else if (invalid) {
          this.applyErrors = invalid.errors.length > 0 ? invalid.errors.map(e => e.message) : [invalid.message];
        } else {
          this.applyErrors = ['The import failed: ' + adminApiError(err).message];
        }
        this.render();
      }
    });
  }

  conflictText(conflict: FeedbackResolutionConflictItem): string {
    const heading = feedbackIssueHeading(this.source, conflict.id, conflict.fingerprint);
    if (conflict.reason === 'missing') return `${heading} no longer exists.`;
    if (conflict.expectedStatus !== conflict.currentStatus) {
      return `${heading}: the status was ${feedbackStatusLabel(conflict.expectedStatus)} at the review and is ${feedbackStatusLabel(conflict.currentStatus)} now.`;
    }
    return `${heading} was triaged again after the review.`;
  }

  badgeClass(badge: FeedbackImportBadge): string {
    switch (badge.tone) {
      case 'none': return 'status-badge fi-badge-none';
      case 'warning': return 'status-badge badge-warning';
      case 'info': return 'status-badge fi-badge-info';
      case 'danger': return 'status-badge badge-danger';
      default: return 'status-badge fi-badge-change';
    }
  }

  // ---- Internals -----------------------------------------------------------------------------

  private runPreview(moveToReview: boolean): void {
    const request = this.request;
    if (!request) return;
    this.previewSub?.unsubscribe();
    this.previewing = true;
    this.applyErrors = [];
    this.render();
    this.previewSub = this.transfer.previewResolutions(request).subscribe({
      next: preview => {
        this.previewing = false;
        this.preview = preview;
        this.cards = preview.items.map(item => this.toCard(item));
        this.acknowledged = false;
        this.conflicts = null;
        this.conflictMessage = '';
        if (moveToReview) {
          this.view = 'side';
          this.goToStep(2);
        } else {
          this.render();
          this.focusStepHeading();
        }
      },
      error: err => {
        this.previewing = false;
        const invalid = feedbackResolutionInvalid(err);
        const messages = invalid
          ? (invalid.errors.length > 0 ? invalid.errors.map(e => e.message) : [invalid.message])
          : ['The review failed: ' + adminApiError(err).message];
        if (moveToReview) {
          this.errors = messages.map(message => ({ line: null, message }));
          this.validSummary = null;
          this.validatedText = null;
        } else {
          this.applyErrors = messages;
        }
        this.render();
      }
    });
  }

  private toCard(item: FeedbackResolutionPreviewItem): FeedbackImportCard {
    const current = item.current;
    const after = item.after ?? (current ? { status: current.status, note: current.note, resolution: current.resolution, fixedInVersion: current.fixedInVersion } : null);
    const note = { before: current?.note ?? '', after: after?.note ?? '' };
    const resolution = { before: current?.resolution ?? '', after: after?.resolution ?? '' };
    return {
      item,
      heading: feedbackIssueHeading(this.source, item.id, item.fingerprint),
      badges: feedbackImportBadges(item),
      statusLine: changeLine(current?.status, after?.status, item.changes.status, feedbackStatusLabel),
      versionLine: changeLine(current?.fixedInVersion, after?.fixedInVersion, item.changes.fixedInVersion, plain),
      noteDiff: diffLines(note.before, note.after),
      resolutionDiff: diffLines(resolution.before, resolution.after),
      showNote: note.before !== '' || note.after !== '',
      showResolution: resolution.before !== '' || resolution.after !== ''
    };
  }

  private reset(): void {
    this.cancelRequests();
    this.step = 1;
    this.input = 'paste';
    this.pastedText = '';
    this.fileText = null;
    this.fileName = '';
    this.fileSize = 0;
    this.fileError = null;
    this.validating = false;
    this.clearValidation();
    this.clearReview();
    this.applying = false;
    this.result = null;
  }

  private clearValidation(): void {
    this.errors = [];
    this.notices = [];
    this.validSummary = null;
    this.validatedText = null;
    this.request = null;
  }

  private clearReview(): void {
    this.preview = null;
    this.cards = [];
    this.view = 'side';
    this.acknowledged = false;
    this.applyErrors = [];
    this.conflicts = null;
    this.conflictMessage = '';
  }

  private cancelRequests(): void {
    this.previewSub?.unsubscribe();
    this.previewSub = undefined;
    this.previewing = false;
    this.applySub?.unsubscribe();
    this.applySub = undefined;
  }

  private goToStep(step: FeedbackImportStep): void {
    this.step = step;
    this.render();
    this.focusStepHeading();
  }

  private focusStepHeading(): void {
    this.stepHeading?.nativeElement.focus({ preventScroll: true });
  }

  private render(): void {
    this.cdr.markForCheck();
    this.cdr.detectChanges();
  }

  ngOnDestroy(): void {
    this.cancelRequests();
  }
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} bytes`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
