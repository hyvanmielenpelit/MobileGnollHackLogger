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
  FEEDBACK_EXPORT_MAX_OCCURRENCES_PER_ISSUE,
  FeedbackExportFormat,
  FeedbackExportRequest,
  FeedbackTransferService,
  feedbackExportNotFound
} from '../../../services/feedback-transfer.service';
import { adminApiError } from '../../../services/admin-users.service';
import { downloadTextFile } from '../../../utils/download.util';
import {
  feedbackExportFormat,
  isFeedbackOccurrenceCount,
  type FeedbackExportOptions,
  type FeedbackSource
} from './feedback-view-storage';

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

/** The occurrence counts the dropdown offers before *All occurrences* and *Custom…*. */
export const OCCURRENCE_PRESETS: readonly number[] = [5, 10, 25, 50, 100];

/** The file formats, YAML first: the default, and the one an AI coding assistant reads. */
export const FEEDBACK_EXPORT_FORMAT_CHOICES: readonly {
  readonly value: FeedbackExportFormat;
  readonly label: string;
  readonly hint: string;
  readonly recommended: boolean;
}[] = [
  { value: 'yaml', label: 'YAML', hint: 'For an AI coding assistant. Ends in a resolution template.', recommended: true },
  { value: 'markdown', label: 'Markdown', hint: 'For reading.', recommended: false }
];

const CUSTOM_OCCURRENCES_ERROR = 'Enter a whole number from 1 to 1,000.';

const integer = new Intl.NumberFormat('en-US');

/**
 * Export feedback issues as YAML, for an AI coding assistant to work on, or as Markdown to read: the selected issues,
 * or one issue when `issueSubject` is set. The file format, the occurrences per issue (a preset, all, or a custom
 * count from 1 to 1,000) and, for GnollBench, whether each occurrence carries its question text. The file is saved
 * through the browser with the media type the server names.
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
  @ViewChild('customInput') customInput?: ElementRef<HTMLInputElement>;

  @Input({ required: true }) source!: FeedbackSource;
  /** Prefixes every id in the dialog, so two instances on one page never share one; the source when unset. */
  @Input() idPrefix: string | null | undefined;
  /** The issues to export, in selection order. */
  @Input() ids: readonly ExportId[] = [];
  /** The subject of the one issue being exported; null when exporting a selection. */
  @Input() issueSubject: string | null = null;
  /** The stored occurrence count of that one issue; null when unknown or exporting a selection. */
  @Input() occurrenceTotal: number | null = null;
  /** The options of the last export; the defaults when none was remembered. */
  @Input() exportOptions: FeedbackExportOptions | undefined;

  @Output() exported = new EventEmitter<FeedbackExportDone>();

  readonly occurrencePresets = OCCURRENCE_PRESETS;
  readonly formatChoices = FEEDBACK_EXPORT_FORMAT_CHOICES;
  readonly maxIssues = FEEDBACK_EXPORT_MAX_ISSUES;
  readonly maxOccurrences = FEEDBACK_EXPORT_MAX_OCCURRENCES_PER_ISSUE;

  format: FeedbackExportFormat = 'yaml';
  /** The occurrences dropdown's value: a preset as text, `all` or `custom`. */
  occurrenceChoice = String(DEFAULT_FEEDBACK_EXPORT_OPTIONS.occurrencesPerIssue);
  /** The custom number field's text. */
  customOccurrences = '';
  customError: string | null = null;
  includeQuestionText = DEFAULT_FEEDBACK_EXPORT_OPTIONS.includeQuestionText;
  exporting = false;
  error: string | null = null;

  private exportSub: Subscription | undefined;

  get prefix(): string {
    return this.idPrefix || this.source;
  }

  get isGnollBench(): boolean {
    return this.source === 'gnollbench';
  }

  get isSingleIssue(): boolean {
    return this.issueSubject !== null && this.issueSubject !== undefined;
  }

  get count(): number {
    return this.ids.length;
  }

  get title(): string {
    if (this.isSingleIssue) return 'Export issue';
    return `Export ${integer.format(this.count)} ${this.count === 1 ? 'issue' : 'issues'}`;
  }

  get isCustom(): boolean {
    return this.occurrenceChoice === 'custom';
  }

  /** *N stored occurrences* of the one issue; null when its count is unknown. */
  get storedOccurrencesText(): string | null {
    const total = this.occurrenceTotal;
    if (total === null || total === undefined) return null;
    return `${integer.format(total)} stored ${total === 1 ? 'occurrence' : 'occurrences'}`;
  }

  get occurrencesHint(): string {
    const total = this.occurrenceTotal;
    return this.isSingleIssue && total !== null && total !== undefined
      ? `Newest first. This issue has ${integer.format(total)}.`
      : 'Newest first.';
  }

  /** What the custom number field points at with `aria-describedby`: the hint, and the error while shown. */
  get customDescribedBy(): string {
    return this.prefix + '-feedback-export-occurrences-hint' + (this.customError ? ' ' + this.prefix + '-feedback-export-custom-error' : '');
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

  get downloadLabel(): string {
    if (this.exporting) return 'Downloading…';
    return this.format === 'markdown' ? 'Download Markdown' : 'Download YAML';
  }

  formatCount(value: number): string {
    return integer.format(value);
  }

  /** Shows the dialog with the remembered options; focus goes to the checked file format. */
  open(): void {
    const options = this.exportOptions ?? DEFAULT_FEEDBACK_EXPORT_OPTIONS;
    const occurrences = options.occurrencesPerIssue;
    if (occurrences === 'all') {
      this.occurrenceChoice = 'all';
      this.customOccurrences = '';
    } else if (isFeedbackOccurrenceCount(occurrences)) {
      this.occurrenceChoice = OCCURRENCE_PRESETS.includes(occurrences) ? String(occurrences) : 'custom';
      this.customOccurrences = this.occurrenceChoice === 'custom' ? String(occurrences) : '';
    } else {
      this.occurrenceChoice = String(DEFAULT_FEEDBACK_EXPORT_OPTIONS.occurrencesPerIssue);
      this.customOccurrences = '';
    }
    this.customError = null;
    this.includeQuestionText = options.includeQuestionText !== false;
    this.format = feedbackExportFormat(options);
    this.error = null;
    this.exporting = false;
    const el = this.dialog?.nativeElement;
    if (el && !el.open) el.showModal();
    this.cdr.markForCheck();
    this.cdr.detectChanges();
    el?.querySelector<HTMLInputElement>('.fx-format input[type="radio"]:checked')?.focus();
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

  /** The dropdown changed. *Custom…* reveals the number field, filled with the preset it replaces, and focuses it. */
  onOccurrenceChoice(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    const previous = this.occurrenceChoice;
    this.occurrenceChoice = value;
    this.customError = null;
    if (value !== 'custom') {
      this.cdr.markForCheck();
      return;
    }
    if (this.customOccurrences.trim() === '' && OCCURRENCE_PRESETS.includes(Number(previous))) {
      this.customOccurrences = previous;
    }
    this.cdr.markForCheck();
    this.cdr.detectChanges();
    const input = this.customInput?.nativeElement;
    input?.focus();
    input?.select();
  }

  /** Typing clears the error; the value is checked again on Download. */
  onCustomInput(event: Event): void {
    this.customOccurrences = (event.target as HTMLInputElement).value;
    if (this.customError) {
      this.customError = null;
    }
    this.cdr.markForCheck();
  }

  onIncludeQuestionTextChange(event: Event): void {
    this.includeQuestionText = (event.target as HTMLInputElement).checked;
    this.cdr.markForCheck();
  }

  /** The chosen occurrences: a count, `all`, or null when the custom field holds no whole number from 1 to 1,000. */
  private chosenOccurrences(): number | 'all' | null {
    if (this.occurrenceChoice === 'all') return 'all';
    if (this.occurrenceChoice === 'custom') {
      const text = this.customOccurrences.trim();
      if (!/^\d+$/.test(text)) return null;
      const value = Number(text);
      return isFeedbackOccurrenceCount(value) ? value : null;
    }
    const preset = Number(this.occurrenceChoice);
    return isFeedbackOccurrenceCount(preset) ? preset : DEFAULT_FEEDBACK_EXPORT_OPTIONS.occurrencesPerIssue;
  }

  runExport(): void {
    if (this.blockedReason !== null) return;
    const occurrences = this.chosenOccurrences();
    if (occurrences === null) {
      this.customError = CUSTOM_OCCURRENCES_ERROR;
      this.cdr.markForCheck();
      this.cdr.detectChanges();
      this.customInput?.nativeElement.focus();
      return;
    }

    const options: FeedbackExportOptions = { occurrencesPerIssue: occurrences, includeQuestionText: this.includeQuestionText, format: this.format };
    const count: Pick<FeedbackExportRequest, 'occurrencesPerIssue' | 'allOccurrences'> =
      occurrences === 'all' ? { allOccurrences: true } : { occurrencesPerIssue: occurrences };
    const request: FeedbackExportRequest = this.isGnollBench
      ? { source: 'gnollbench', format: this.format, fingerprints: this.ids.map(String), ...count, includeQuestionText: options.includeQuestionText }
      : { source: 'overseer', format: this.format, issueIds: this.ids.map(Number), ...count };

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
          this.error = this.isSingleIssue
            ? 'This issue no longer exists, so nothing was exported.'
            : 'None of the selected issues exist any more, so nothing was exported. They have left the selection.';
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
