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
  FeedbackExportFormat,
  FeedbackExportRequest,
  FeedbackTransferService,
  feedbackExportNotFound
} from '../../../services/feedback-transfer.service';
import { adminApiError } from '../../../services/admin-users.service';
import { downloadTextFile } from '../../../utils/download.util';
import { feedbackExportFormat, type FeedbackExportOptions, type FeedbackSource } from './feedback-view-storage';

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

export const DEFAULT_FEEDBACK_EXPORT_OPTIONS: FeedbackExportOptions = { occurrencesPerIssue: 10, includeQuestionText: true, format: 'yaml' };

export const OCCURRENCES_PER_ISSUE_CHOICES: readonly FeedbackExportOptions['occurrencesPerIssue'][] = [5, 10, 25];

/** The file formats, YAML first: the default, and the one an AI coding assistant reads. */
export const FEEDBACK_EXPORT_FORMAT_CHOICES: readonly { readonly value: FeedbackExportFormat; readonly label: string; readonly hint: string }[] = [
  { value: 'yaml', label: 'YAML', hint: 'for an AI coding assistant' },
  { value: 'markdown', label: 'Markdown', hint: 'to read' }
];

/**
 * Export the selected issues as YAML, for an AI coding assistant to work on, or as Markdown to read: the file
 * format, the occurrences per issue and, for GnollBench, whether each occurrence carries its question text. The
 * file is saved through the browser with the media type the server names.
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
  readonly formatChoices = FEEDBACK_EXPORT_FORMAT_CHOICES;
  readonly maxIssues = FEEDBACK_EXPORT_MAX_ISSUES;

  format: FeedbackExportFormat = 'yaml';
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
    return `Export ${this.count} ${this.count === 1 ? 'issue' : 'issues'}`;
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
    return { occurrencesPerIssue: this.occurrencesPerIssue, includeQuestionText: this.includeQuestionText, format: this.format };
  }

  open(): void {
    const options = this.exportOptions ?? DEFAULT_FEEDBACK_EXPORT_OPTIONS;
    this.occurrencesPerIssue = OCCURRENCES_PER_ISSUE_CHOICES.includes(options.occurrencesPerIssue)
      ? options.occurrencesPerIssue
      : DEFAULT_FEEDBACK_EXPORT_OPTIONS.occurrencesPerIssue;
    this.includeQuestionText = options.includeQuestionText !== false;
    this.format = feedbackExportFormat(options);
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

  setFormat(value: FeedbackExportFormat): void {
    this.format = value;
    this.cdr.markForCheck();
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
      ? {
        source: 'gnollbench', format: this.format, fingerprints: this.ids.map(String),
        occurrencesPerIssue: options.occurrencesPerIssue, includeQuestionText: options.includeQuestionText
      }
      : { source: 'overseer', format: this.format, issueIds: this.ids.map(Number), occurrencesPerIssue: options.occurrencesPerIssue };

    this.exporting = true;
    this.error = null;
    this.cdr.markForCheck();
    this.exportSub?.unsubscribe();
    this.exportSub = this.transfer.exportIssues(request).subscribe({
      next: result => {
        this.exporting = false;
        downloadTextFile(result.fileName, result.content, result.mediaType);
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
