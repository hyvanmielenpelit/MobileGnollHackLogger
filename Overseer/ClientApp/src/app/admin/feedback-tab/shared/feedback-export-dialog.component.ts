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
  FEEDBACK_EXPORT_MAX_ISSUES,
  FeedbackExportRequest,
  FeedbackTransferService,
  feedbackExportNotFound
} from '../../../services/feedback-transfer.service';
import { adminApiError } from '../../../services/admin-users.service';
import { downloadTextFile } from '../../../utils/download.util';
import type { FeedbackExportOptions, FeedbackSource } from './feedback-view-storage';

/** Overseer issue id or GnollBench fingerprint. */
type ExportId = number | string;

/** What an export reports to its host: the issues it could not find, the options used and the saved file. */
export interface FeedbackExportDone {
  missing: ExportId[];
  exportOptions: FeedbackExportOptions;
  /** Null when none of the issues existed and nothing was saved. */
  fileName: string | null;
  issueCount: number;
}

export const DEFAULT_FEEDBACK_EXPORT_OPTIONS: FeedbackExportOptions = { occurrencesPerIssue: 10, includeQuestionText: true };

export const OCCURRENCES_PER_ISSUE_CHOICES: readonly FeedbackExportOptions['occurrencesPerIssue'][] = [5, 10, 25];

/**
 * Export the selected issues as Markdown, for an AI coding assistant to work on: the occurrences per issue and,
 * for GnollBench, whether each occurrence carries its question text. The file is saved through the browser.
 */
@Component({
  selector: 'app-feedback-export-dialog',
  standalone: true,
  templateUrl: './feedback-export-dialog.component.html',
  styleUrls: ['./feedback-export-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FeedbackExportDialogComponent implements OnDestroy {
  private transfer = inject(FeedbackTransferService);
  private cdr = inject(ChangeDetectorRef);

  @ViewChild('dialog') dialog?: ElementRef<HTMLDialogElement>;

  @Input({ required: true }) source!: FeedbackSource;
  /** The issues to export, in selection order. */
  @Input() ids: readonly ExportId[] = [];
  /** The options of the last export; the defaults when none was remembered. */
  @Input() exportOptions: FeedbackExportOptions | undefined;

  @Output() exported = new EventEmitter<FeedbackExportDone>();

  readonly occurrenceChoices = OCCURRENCES_PER_ISSUE_CHOICES;
  readonly maxIssues = FEEDBACK_EXPORT_MAX_ISSUES;

  occurrencesPerIssue: FeedbackExportOptions['occurrencesPerIssue'] = DEFAULT_FEEDBACK_EXPORT_OPTIONS.occurrencesPerIssue;
  includeQuestionText = DEFAULT_FEEDBACK_EXPORT_OPTIONS.includeQuestionText;
  exporting = false;
  error: string | null = null;

  private exportSub: Subscription | undefined;

  get isGnollBench(): boolean {
    return this.source === 'gnollbench';
  }

  get count(): number {
    return this.ids.length;
  }

  get title(): string {
    return `Export ${this.count} ${this.count === 1 ? 'issue' : 'issues'} as Markdown`;
  }

  /** Why Export is unavailable; null when it is not. */
  get blockedReason(): string | null {
    if (this.exporting) return 'The export is being prepared.';
    if (this.count === 0) return 'Nothing is selected to export.';
    if (this.count > this.maxIssues) {
      return `An export holds at most ${this.maxIssues} issues; ${this.count} are selected.`;
    }
    return null;
  }

  get options(): FeedbackExportOptions {
    return { occurrencesPerIssue: this.occurrencesPerIssue, includeQuestionText: this.includeQuestionText };
  }

  open(): void {
    const options = this.exportOptions ?? DEFAULT_FEEDBACK_EXPORT_OPTIONS;
    this.occurrencesPerIssue = OCCURRENCES_PER_ISSUE_CHOICES.includes(options.occurrencesPerIssue)
      ? options.occurrencesPerIssue
      : DEFAULT_FEEDBACK_EXPORT_OPTIONS.occurrencesPerIssue;
    this.includeQuestionText = options.includeQuestionText !== false;
    this.error = null;
    this.exporting = false;
    const el = this.dialog?.nativeElement;
    if (el && !el.open) el.showModal();
    this.cdr.markForCheck();
    this.cdr.detectChanges();
  }

  close(): void {
    if (this.exporting) return;
    this.dialog?.nativeElement?.close();
  }

  /** Escape: an export in flight is not abandoned half way. */
  onCancel(event: Event): void {
    if (this.exporting) event.preventDefault();
  }

  setOccurrences(value: FeedbackExportOptions['occurrencesPerIssue']): void {
    this.occurrencesPerIssue = value;
    this.cdr.markForCheck();
  }

  onIncludeQuestionTextChange(event: Event): void {
    this.includeQuestionText = (event.target as HTMLInputElement).checked;
    this.cdr.markForCheck();
  }

  runExport(): void {
    if (this.blockedReason !== null) return;
    const options = this.options;
    const request: FeedbackExportRequest = this.isGnollBench
      ? { source: 'gnollbench', fingerprints: this.ids.map(String), occurrencesPerIssue: options.occurrencesPerIssue, includeQuestionText: options.includeQuestionText }
      : { source: 'overseer', issueIds: this.ids.map(Number), occurrencesPerIssue: options.occurrencesPerIssue };

    this.exporting = true;
    this.error = null;
    this.cdr.markForCheck();
    this.exportSub?.unsubscribe();
    this.exportSub = this.transfer.exportIssues(request).subscribe({
      next: result => {
        this.exporting = false;
        downloadTextFile(result.fileName, result.markdown, 'text/markdown;charset=utf-8');
        const missing: ExportId[] = this.isGnollBench ? [...result.missingFingerprints] : [...result.missingIds];
        this.dialog?.nativeElement?.close();
        this.exported.emit({ missing, exportOptions: options, fileName: result.fileName, issueCount: result.issueCount });
        this.cdr.markForCheck();
      },
      error: err => {
        this.exporting = false;
        const notFound = feedbackExportNotFound(err);
        if (notFound) {
          this.error = 'None of the selected issues exist any more, so nothing was exported. They have left the selection.';
          const missing: ExportId[] = this.isGnollBench ? [...notFound.missingFingerprints] : [...notFound.missingIds];
          this.exported.emit({ missing, exportOptions: options, fileName: null, issueCount: 0 });
        } else {
          this.error = 'The export failed: ' + adminApiError(err).message;
        }
        this.cdr.markForCheck();
      }
    });
  }

  ngOnDestroy(): void {
    this.exportSub?.unsubscribe();
  }
}
