import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  OnDestroy,
  ViewChild,
  computed,
  inject,
  signal
} from '@angular/core';
import { Subscription } from 'rxjs';
import { AdminService, ChatTurnLimitEventDetail, ChatTurnLimitPrompt } from '../../../services/admin.service';
import { securityTimeText } from '../../security-tab/security-event-archive';
import { limitCodeLabel, limitDetailFacts, limitPromptUnavailableText, limitUserLabel } from './telemetry-limit-labels';

/**
 * The details of one limit event: every fact, and the prompt, which is fetched only when asked for because reading it
 * writes an access-journal row. The prompt is untrusted text, shown by plain interpolation, and is forgotten when the
 * dialog closes. Self-contained: the Limits view calls `open()`.
 */
@Component({
  selector: 'app-telemetry-limit-dialog',
  templateUrl: './telemetry-limit-dialog.component.html',
  styleUrl: './telemetry-limit-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TelemetryLimitDialogComponent implements OnDestroy {
  private readonly adminService = inject(AdminService);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('dialog') private dialogRef?: ElementRef<HTMLDialogElement>;
  @ViewChild('title') private titleRef?: ElementRef<HTMLElement>;
  @ViewChild('promptText') private promptTextRef?: ElementRef<HTMLElement>;

  readonly timeText = securityTimeText;

  readonly eventId = signal<number | null>(null);
  readonly detail = signal<ChatTurnLimitEventDetail | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly prompt = signal<ChatTurnLimitPrompt | null>(null);
  readonly promptLoading = signal(false);
  readonly promptError = signal<string | null>(null);

  readonly facts = computed(() => {
    const detail = this.detail();
    return detail ? limitDetailFacts(detail) : [];
  });

  readonly titleText = computed(() => {
    const detail = this.detail();
    if (detail) return `${limitCodeLabel(detail.code)} · ${limitUserLabel(detail)}`;
    return 'Limit event';
  });

  readonly promptUnavailableText = computed(() => {
    const detail = this.detail();
    return detail ? limitPromptUnavailableText(detail.promptState) : '';
  });

  private opener: HTMLElement | null = null;
  private fetchSub: Subscription | null = null;
  private promptSub: Subscription | null = null;
  private isOpen = false;

  ngOnDestroy(): void {
    this.isOpen = false;
    this.cancelRequests();
    const dialog = this.dialogRef?.nativeElement;
    if (dialog?.open) dialog.close();
  }

  /** Shows the dialog and fetches the event; focus goes to the title, and back to `opener` on close. */
  open(eventId: number, opener?: HTMLElement): void {
    this.cancelRequests();
    this.eventId.set(eventId);
    this.opener = opener ?? null;
    this.detail.set(null);
    this.forgetPrompt();
    this.isOpen = true;
    const dialog = this.dialogRef?.nativeElement;
    if (dialog && !dialog.open) dialog.showModal();
    this.fetch();
    this.cdr.detectChanges();
    this.titleRef?.nativeElement.focus();
  }

  close(): void {
    const dialog = this.dialogRef?.nativeElement;
    if (dialog?.open) dialog.close();
  }

  /** Escape: closes this dialog and nothing behind it. */
  onCancel(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    this.close();
  }

  /** The native close, however it came: stopped here; the prompt is forgotten and focus goes back to the button that opened it. */
  onDialogClose(event: Event): void {
    event.stopPropagation();
    if (!this.isOpen) return;
    this.isOpen = false;
    this.cancelRequests();
    this.loading.set(false);
    this.forgetPrompt();
    const opener = this.opener;
    this.opener = null;
    if (opener?.isConnected) opener.focus();
  }

  retry(): void {
    this.fetch();
  }

  /** Asks for the prompt, which the server journals; focus moves to the text, since the button leaves. */
  showPrompt(): void {
    const id = this.eventId();
    if (id === null || this.promptLoading()) return;
    this.promptLoading.set(true);
    this.promptError.set(null);
    this.promptSub?.unsubscribe();
    this.promptSub = this.adminService.readChatTurnLimitPrompt(id).subscribe({
      next: prompt => {
        this.promptLoading.set(false);
        this.prompt.set(prompt);
        this.cdr.detectChanges();
        this.promptTextRef?.nativeElement.focus();
      },
      error: err => {
        this.promptLoading.set(false);
        this.promptError.set(TelemetryLimitDialogComponent.promptErrorText(err));
      }
    });
  }

  private fetch(): void {
    const id = this.eventId();
    if (id === null) return;
    this.fetchSub?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.fetchSub = this.adminService.getChatTurnLimitEvent(id).subscribe({
      next: detail => {
        this.loading.set(false);
        this.detail.set(detail);
      },
      error: err => {
        this.loading.set(false);
        this.error.set(err?.status === 404
          ? 'This event no longer exists. It may have been pruned.'
          : TelemetryLimitDialogComponent.errorText(err) || 'The event could not be loaded.');
      }
    });
  }

  private forgetPrompt(): void {
    this.prompt.set(null);
    this.promptLoading.set(false);
    this.promptError.set(null);
  }

  private cancelRequests(): void {
    this.fetchSub?.unsubscribe();
    this.fetchSub = null;
    this.promptSub?.unsubscribe();
    this.promptSub = null;
  }

  private static promptErrorText(err: { status?: number } | null | undefined): string {
    if (err?.status === 403) return limitPromptUnavailableText('confidential');
    if (err?.status === 404) return limitPromptUnavailableText('deleted');
    return 'The prompt could not be loaded: ' + (TelemetryLimitDialogComponent.errorText(err) || 'Unknown error');
  }

  private static errorText(err: unknown): string {
    const http = err as { error?: { message?: string } | string; message?: string } | null | undefined;
    if (http?.error && typeof http.error === 'object' && http.error.message) return http.error.message;
    if (typeof http?.error === 'string' && http.error) return http.error;
    return http?.message ?? '';
  }
}
