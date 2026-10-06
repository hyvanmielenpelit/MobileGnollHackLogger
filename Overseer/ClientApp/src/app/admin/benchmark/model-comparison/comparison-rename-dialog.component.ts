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
import { HttpErrorResponse } from '@angular/common/http';
import type { Subscription } from 'rxjs';

import { AdminBenchmarkService, BenchmarkComparisonDto } from '../../../services/admin-benchmark.service';

/** The longest name the server stores for a comparison. */
export const COMPARISON_NAME_MAX_LENGTH = 160;

/** The server's `{ error }`, a plain-text body, or what the failure itself says. */
function renameErrorText(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const body = error.error;
    if (typeof body === 'string' && body.trim()) {
      return body.trim();
    }
    const message = body && typeof body === 'object' ? (body as { error?: unknown }).error : null;
    if (typeof message === 'string' && message.trim()) {
      return message.trim();
    }
    return error.status > 0
      ? `The comparison could not be renamed (HTTP ${error.status}).`
      : 'The server could not be reached.';
  }
  return 'The comparison could not be renamed.';
}

/**
 * Renames a numbered comparison: one text field, *Reset to default*, **Cancel** and **Save**. A blank
 * name, or the default name itself, restores the default. Nested inside the comparison wizard's
 * dialog, so its close, cancel and click events stop at its own dialog. The server's refusal is shown
 * under the field; nothing changes until the server has answered.
 */
@Component({
  selector: 'app-comparison-rename-dialog',
  standalone: true,
  templateUrl: './comparison-rename-dialog.component.html',
  styleUrls: ['./comparison-rename-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ComparisonRenameDialogComponent implements OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);

  @Input() idPrefix = 'mc-rename';

  /** The comparison as the server answered the rename. */
  @Output() readonly renamed = new EventEmitter<BenchmarkComparisonDto>();

  /** The dialog closed, saved or not, and focus has returned. */
  @Output() readonly closed = new EventEmitter<void>();

  @ViewChild('dialog') dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('nameInput') nameInput?: ElementRef<HTMLInputElement>;

  readonly maxLength = COMPARISON_NAME_MAX_LENGTH;

  comparison: BenchmarkComparisonDto | null = null;
  name = '';
  saving = false;
  error: string | null = null;

  private returnFocus: HTMLElement | null = null;
  private saveSub: Subscription | null = null;

  get isOpen(): boolean {
    return !!this.dialog?.nativeElement.open;
  }

  /** The name Save would send: null restores the default. */
  get requestedName(): string | null {
    const trimmed = this.name.trim();
    return trimmed === '' || trimmed === this.comparison?.defaultName ? null : trimmed;
  }

  /** Opens on `comparison`'s display name; focus returns to `returnFocus` once closed. */
  open(comparison: BenchmarkComparisonDto, returnFocus: HTMLElement | null = null): void {
    this.comparison = comparison;
    this.name = comparison.name;
    this.error = null;
    this.saving = false;
    this.returnFocus = returnFocus;
    this.cdr.detectChanges();
    const dialog = this.dialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    const input = this.nameInput?.nativeElement;
    input?.focus();
    input?.select();
  }

  close(): void {
    if (this.saving) {
      return;
    }
    this.dialog?.nativeElement.close();
  }

  onNameInput(event: Event): void {
    this.name = (event.target as HTMLInputElement).value;
    this.error = null;
  }

  /** Fills in the default name, which Save sends as a reset. */
  resetToDefault(): void {
    if (!this.comparison || this.saving) {
      return;
    }
    this.name = this.comparison.defaultName;
    this.error = null;
    this.cdr.markForCheck();
    this.nameInput?.nativeElement.focus();
  }

  save(): void {
    const comparison = this.comparison;
    if (!comparison || this.saving) {
      return;
    }
    if (this.name.trim().length > COMPARISON_NAME_MAX_LENGTH) {
      this.error = `A name can be at most ${COMPARISON_NAME_MAX_LENGTH} characters.`;
      return;
    }
    this.saving = true;
    this.error = null;
    this.saveSub?.unsubscribe();
    this.saveSub = this.benchmarkService.renameComparison(comparison.id, this.requestedName).subscribe({
      next: dto => {
        this.saving = false;
        this.comparison = dto;
        this.renamed.emit(dto);
        this.dialog?.nativeElement.close();
        this.cdr.markForCheck();
      },
      error: error => {
        this.saving = false;
        this.error = renameErrorText(error);
        this.cdr.markForCheck();
      }
    });
  }

  /** Enter in the field saves. */
  onSubmit(event: Event): void {
    event.preventDefault();
    this.save();
  }

  /**
   * The dialog's own close, cancel and click events stop here, so the wizard's dialog never sees
   * them; Escape is refused while a save is in flight.
   */
  onDialogEvent(event: Event): void {
    event.stopPropagation();
    if (event.type === 'cancel' && this.saving) {
      event.preventDefault();
      return;
    }
    if (event.type !== 'close') {
      return;
    }
    this.cdr.markForCheck();
    const target = this.returnFocus;
    this.returnFocus = null;
    target?.focus();
    this.closed.emit();
  }

  ngOnDestroy(): void {
    this.saveSub?.unsubscribe();
  }
}
