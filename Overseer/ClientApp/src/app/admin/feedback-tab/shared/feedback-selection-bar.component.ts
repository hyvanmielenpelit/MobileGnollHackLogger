import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Input,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { FEEDBACK_EXPORT_MAX_ISSUES } from '../../../services/feedback-transfer.service';
import { FeedbackExportDialogComponent, FeedbackExportDone } from './feedback-export-dialog.component';
import { FeedbackImportDialogComponent } from './feedback-import-dialog.component';
import { FEEDBACK_SELECTION_CAP, FeedbackExportOptions, FeedbackSource } from './feedback-view-storage';

/** An Overseer issue id or a GnollBench fingerprint. */
export type FeedbackSelectionId = number | string;

const integer = new Intl.NumberFormat('en-US');

/**
 * The selection line of a feedback list: how many issues are selected and how many of them the filters hide,
 * Show selected only, Clear selection and Select all N matching, and the two transfer actions, Export… and
 * Import resolutions…, whose dialogs it opens. The host holds the selection; the bar only reports requests.
 */
@Component({
  selector: 'app-feedback-selection-bar',
  standalone: true,
  imports: [FeedbackExportDialogComponent, FeedbackImportDialogComponent],
  templateUrl: './feedback-selection-bar.component.html',
  styleUrls: ['./feedback-selection-bar.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FeedbackSelectionBarComponent {
  private cdr = inject(ChangeDetectorRef);

  @ViewChild('exportDialog') exportDialog?: FeedbackExportDialogComponent;
  @ViewChild('importDialog') importDialog?: FeedbackImportDialogComponent;

  @Input({ required: true }) source!: FeedbackSource;
  @Input() selectedIds: readonly FeedbackSelectionId[] = [];
  /** Issues matching the current filters. */
  @Input() matchingCount = 0;
  /** Selected issues the current filters leave off the list. */
  @Input() notShownCount = 0;
  @Input() showSelectedOnly = false;
  @Input() exportOptions: FeedbackExportOptions | undefined;

  @Output() showSelectedOnlyChange = new EventEmitter<boolean>();
  @Output() clear = new EventEmitter<void>();
  /** The host fetches the matching ids and keeps at most 500. */
  @Output() selectAllMatching = new EventEmitter<void>();
  @Output() exported = new EventEmitter<{ missing: FeedbackSelectionId[]; exportOptions: FeedbackExportOptions }>();
  /** The host refetches its list and counts. */
  @Output() imported = new EventEmitter<void>();

  readonly maxExport = FEEDBACK_EXPORT_MAX_ISSUES;

  /** The outcome of the last export, for the bar's polite status line. */
  statusMessage = '';

  get count(): number {
    return this.selectedIds.length;
  }

  get idPrefix(): string {
    return `${this.source}-feedback-selection`;
  }

  get summary(): string {
    return this.count > 0 ? `${integer.format(this.count)} selected` : 'Nothing selected';
  }

  get canSelectAllMatching(): boolean {
    return !this.showSelectedOnly && this.matchingCount > 0 && this.count < FEEDBACK_SELECTION_CAP;
  }

  get selectAllLabel(): string {
    return `Select all ${integer.format(this.matchingCount)} matching`;
  }

  /** Why Export… is unavailable; null when it is not. */
  get exportBlockedReason(): string | null {
    if (this.count === 0) return 'Select issues to export.';
    if (this.count > this.maxExport) {
      return `An export holds at most ${integer.format(this.maxExport)} issues; ${integer.format(this.count)} are selected.`;
    }
    return null;
  }

  formatCount(value: number): string {
    return integer.format(value);
  }

  toggleShowSelectedOnly(): void {
    this.showSelectedOnlyChange.emit(!this.showSelectedOnly);
  }

  onClear(): void {
    this.statusMessage = '';
    this.clear.emit();
  }

  onSelectAllMatching(): void {
    if (!this.canSelectAllMatching) return;
    this.selectAllMatching.emit();
  }

  openExport(): void {
    if (this.exportBlockedReason !== null) return;
    this.exportDialog?.open();
  }

  openImport(): void {
    this.importDialog?.open();
  }

  onExported(done: FeedbackExportDone): void {
    const missing = done.missing.length;
    const left = missing === 0
      ? ''
      : ` ${integer.format(missing)} selected ${missing === 1 ? 'issue no longer exists and has' : 'issues no longer exist and have'} left the selection.`;
    this.statusMessage = done.fileName
      ? `Saved ${done.fileName} with ${integer.format(done.issueCount)} ${done.issueCount === 1 ? 'issue' : 'issues'}.${left}`
      : left.trim();
    this.exported.emit({ missing: [...done.missing], exportOptions: { ...done.exportOptions } });
    this.cdr.markForCheck();
  }

  onImported(): void {
    this.imported.emit();
  }
}
