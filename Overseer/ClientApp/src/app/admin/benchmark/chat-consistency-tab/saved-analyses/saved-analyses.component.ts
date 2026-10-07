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

import { AdminChatConsistencyService, ccErrorText } from '../../../../services/admin-chat-consistency.service';
import { formatUtcDate, formatUtcDateTime } from '../chat-consistency-format';
import { CcAnalysisSummary, CcModelAxis } from '../chat-consistency.models';

/**
 * The saved analyses as a card list, newest first: name, subject, periods, headline and date, with
 * Open (the results step) and Delete. Delete asks first; a refusal because report documents were
 * written from the analysis is shown in the confirmation.
 */
@Component({
  selector: 'app-cc-saved-analyses',
  standalone: true,
  templateUrl: './saved-analyses.component.html',
  styleUrls: ['./saved-analyses.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcSavedAnalysesComponent implements OnDestroy {
  private readonly service = inject(AdminChatConsistencyService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

  @Input() analyses: readonly CcAnalysisSummary[] = [];
  @Input() axes: readonly CcModelAxis[] = [];
  @Input() loading = false;
  @Input() error: string | null = null;
  /** The analysis being opened, for its Open button's busy state. */
  @Input() openingId: number | null = null;

  @Output() readonly open = new EventEmitter<number>();
  @Output() readonly deleted = new EventEmitter<number>();

  @ViewChild('deleteDialog') deleteDialog?: ElementRef<HTMLDialogElement>;

  deleteTarget: CcAnalysisSummary | null = null;
  deleting = false;
  deleteError: string | null = null;
  announcement = '';

  private deleteSub: Subscription | null = null;
  private returnFocus: HTMLElement | null = null;

  ngOnDestroy(): void {
    this.deleteSub?.unsubscribe();
  }

  subjectName(analysis: CcAnalysisSummary): string {
    return this.axes.find(axis => axis.key === analysis.subjectModelKey)?.displayName ?? analysis.subjectModelKey;
  }

  title(analysis: CcAnalysisSummary): string {
    return analysis.name?.trim() || `Analysis #${analysis.id}`;
  }

  period(start: string, end: string): string {
    return `${formatUtcDate(start)} to ${formatUtcDate(end)}`;
  }

  created(analysis: CcAnalysisSummary): string {
    return formatUtcDateTime(analysis.createdAtUtc);
  }

  requestDelete(analysis: CcAnalysisSummary, button: HTMLElement): void {
    if (this.deleting) return;
    this.deleteTarget = analysis;
    this.deleteError = null;
    this.announcement = '';
    this.returnFocus = button;
    this.cdr.detectChanges();
    const dialog = this.deleteDialog?.nativeElement;
    if (dialog && !dialog.open) dialog.showModal();
  }

  cancelDelete(): void {
    this.deleteDialog?.nativeElement.close();
    this.returnFocus?.focus();
  }

  confirmDelete(): void {
    const target = this.deleteTarget;
    if (!target || this.deleting) return;
    const index = this.analyses.findIndex(analysis => analysis.id === target.id);
    this.deleting = true;
    this.deleteError = null;
    this.cdr.markForCheck();
    this.deleteSub?.unsubscribe();
    this.deleteSub = this.service.deleteAnalysis(target.id).subscribe({
      next: () => {
        this.deleting = false;
        this.deleteDialog?.nativeElement.close();
        this.deleteTarget = null;
        this.announcement = `${this.title(target)} was deleted.`;
        this.deleted.emit(target.id);
        this.cdr.detectChanges();
        this.focusAfterDelete(index, target.id);
      },
      error: err => {
        this.deleting = false;
        // 409: report documents were written from it; the refusal says which and what to do.
        this.deleteError = ccErrorText(err, 'The analysis could not be deleted.');
        this.cdr.markForCheck();
      }
    });
  }

  /** Focus goes to the card now at the deleted one's place, else the previous one, else the list heading. */
  private focusAfterDelete(index: number, deletedId: number): void {
    const titles = this.host.nativeElement.querySelectorAll<HTMLElement>(
      `.cc-analysis-card:not([data-analysis-id="${deletedId}"]) .cc-analysis-title`);
    const next = titles[Math.min(index, titles.length - 1)];
    (next ?? this.host.nativeElement.querySelector<HTMLElement>('#cc-saved-title'))?.focus();
  }

  stopNested(event: Event): void {
    event.stopPropagation();
  }
}
