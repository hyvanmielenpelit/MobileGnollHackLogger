import { Component, ChangeDetectionStrategy, ElementRef, EventEmitter, Input, OnInit, Output, ViewChild } from '@angular/core';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';

/** What one opening of the reason dialog shows; anything left out falls back to the component's inputs. */
export interface AdminReasonOptions {
  title?: string;
  message?: string;
  /** The text field's label, e.g. "Reason" or "Note". */
  label?: string;
  maxLength?: number;
  confirmText?: string;
  confirmClass?: string;
}

let nextReasonDialogId = 0;

/**
 * The Admin page's confirmation that needs a written reason: a required text field whose
 * trimmed value is passed to the open() callback and emitted by `confirmed`.
 */
@Component({
  selector: 'app-admin-reason-dialog',
  templateUrl: './admin-reason-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-reason-dialog.component.scss'
})
export class AdminReasonDialogComponent implements OnInit {
  @ViewChild('reasonDialog', { static: true }) reasonDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('reasonField', { static: true }) reasonField!: ElementRef<HTMLTextAreaElement>;

  @Input() title = 'Confirm Action';
  @Input() message = '';
  @Input() label = 'Reason';
  @Input() maxLength = 256;
  @Input() confirmText = 'Confirm';
  @Input() confirmClass = 'btn-gh btn-gh-delete';

  /** The trimmed text, on confirm only. */
  @Output() confirmed = new EventEmitter<string>();

  /** Unique per instance, so two dialogs on one page never share an id or an anchor name. */
  readonly idPrefix = `admin-reason-${nextReasonDialogId++}`;

  activeTitle = this.title;
  activeMessage = this.message;
  activeLabel = this.label;
  activeMaxLength = this.maxLength;
  activeConfirmText = this.confirmText;
  activeConfirmClass = this.confirmClass;

  reason = '';
  private action: ((reason: string) => void) | null = null;

  get trimmedReason(): string {
    return this.reason.trim();
  }

  get canConfirm(): boolean {
    return this.trimmedReason.length > 0;
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  open(options: AdminReasonOptions = {}, action?: (reason: string) => void): void {
    this.activeTitle = options.title ?? this.title;
    this.activeMessage = options.message ?? this.message;
    this.activeLabel = options.label ?? this.label;
    this.activeMaxLength = options.maxLength ?? this.maxLength;
    this.activeConfirmText = options.confirmText ?? this.confirmText;
    this.activeConfirmClass = options.confirmClass ?? this.confirmClass;
    this.action = action ?? null;
    this.reason = '';
    this.reasonField.nativeElement.value = '';
    this.reasonDialog.nativeElement.showModal();
  }

  onReasonInput(event: Event): void {
    this.reason = (event.target as HTMLTextAreaElement).value;
  }

  /** Does nothing while the field is empty or whitespace (the button is aria-disabled). */
  confirm(): void {
    if (!this.canConfirm) {
      return;
    }
    const reason = this.trimmedReason;
    const action = this.action;
    this.close();
    action?.(reason);
    this.confirmed.emit(reason);
  }

  close(): void {
    if (this.reasonDialog.nativeElement.open) {
      this.reasonDialog.nativeElement.close();
    }
    this.action = null;
    this.reason = '';
  }
}
