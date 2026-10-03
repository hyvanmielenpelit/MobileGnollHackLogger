import { Component, ChangeDetectionStrategy, ElementRef, ViewChild } from '@angular/core';

/** What the confirm dialog shows; the button text and class default to a destructive Confirm. */
export interface AdminConfirmOptions {
  title?: string;
  message: string;
  buttonText?: string;
  buttonClass?: string;
}

/** The Admin page's generic confirmation; every component that asks embeds its own instance. */
@Component({
  selector: 'app-admin-confirm-dialog',
  templateUrl: './admin-confirm-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-confirm-dialog.component.scss'
})
export class AdminConfirmDialogComponent {
  @ViewChild('confirmDialog', { static: true }) confirmDialog!: ElementRef<HTMLDialogElement>;

  confirmTitle?: string;
  confirmMessage: string = '';
  confirmButtonText?: string;
  confirmButtonClass?: string;
  confirmAction: (() => void) | null = null;

  open(options: AdminConfirmOptions, action: () => void): void {
    this.confirmTitle = options.title;
    this.confirmMessage = options.message;
    this.confirmAction = action;
    this.confirmButtonText = options.buttonText;
    this.confirmButtonClass = options.buttonClass;
    this.confirmDialog.nativeElement.showModal();
  }

  closeConfirmDialog() {
    this.confirmDialog.nativeElement.close();
    this.confirmAction = null;
    this.confirmTitle = undefined;
    this.confirmButtonText = undefined;
    this.confirmButtonClass = undefined;
  }

  executeConfirmAction() {
    if (this.confirmAction) {
      this.confirmAction();
    }
    this.closeConfirmDialog();
  }
}
