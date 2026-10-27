import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { Subscription } from 'rxjs';
import { AdminService, SecurityEventDetail, SecurityQuarantineItem } from '../../services/admin.service';
import { SnapshotViewerComponent } from '../../shared/snapshot-viewer/snapshot-viewer.component';
import { downloadBlobFile, downloadTextFile } from '../../utils/download.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';
import { AdminPageStore } from '../admin-page.store';
import {
  SecurityEventField,
  SecurityRelatedSection,
  buildSecurityEventArchive,
  isUnreadableQuarantineText,
  quarantineCapText,
  quarantineLengthText,
  quarantineTags,
  quarantineTextDownloadName,
  securityEventArchiveName,
  securityEventFields,
  securityKindLabel,
  securityRelatedSections,
  securityRunSpanText,
  securityTimeText,
  securityUserLabel
} from './security-event-archive';

/** The file saving the dialog does, replaceable in specs. */
export const securityEventIo = {
  saveBlob: (fileName: string, blob: Blob): void => downloadBlobFile(fileName, blob),
  saveText: (fileName: string, text: string): void => downloadTextFile(fileName, text, 'text/plain;charset=utf-8')
};

/** What each snapshot guard code means, in one line (`docs/overseer/security-monitoring.md` § 3a). */
export const SNAPSHOT_GUARD_CODE_MEANINGS: Readonly<Record<string, string>> = {
  address: 'Weak: an AI word near an imperative, or "AI ASSISTANT:".',
  override: 'Strong: an attempt to replace the instructions, such as "ignore previous instructions", "system prompt" or "developer mode".',
  authority: 'Strong: a claim to speak for the Overseer or GnollHack team, a chat-format marker, or a header of the prompt\'s own structure.',
  tool: 'Weak: a tool name near call, use, run, invoke, execute or trigger.',
  'exfil-image': 'Strong: Markdown image syntax pointing at a site other than the game\'s own.',
  'exfil-url': 'Weak: a URL other than the game\'s own sites, or "send ... to" an address.',
  'hidden-tag': 'Strong: a Unicode tag character, invisible on screen.',
  hidden: 'Weak: a format or control character, or a lone surrogate.',
  directive: 'Weak: a standing order for the answers, such as "end every answer with" or "tell the player that".',
  'scan-timeout': 'The scan ran past its time limit, which counts as a finding.'
};

/** What *Events of this user* asks the Security tab for. */
export interface SecurityUserEventsRequest {
  userId: string;
  userName: string;
}

const COPIED_MS = 2000;

/**
 * The details of one security event: what the kind means, every stored field, the game texts the
 * snapshot guard refused (each opened in its own Snapshot Viewer or downloaded), the records linked to
 * the event, and the whole event as one ZIP. Self-contained: the Security tab calls `open()`.
 */
@Component({
  selector: 'app-security-event-dialog',
  standalone: true,
  imports: [SnapshotViewerComponent],
  templateUrl: './security-event-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches the Security tab
  styleUrl: './security-event-dialog.component.scss'
})
export class SecurityEventDialogComponent implements OnInit, OnDestroy {
  private readonly adminService = inject(AdminService);
  private readonly store = inject(AdminPageStore);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('dialog') private dialogRef?: ElementRef<HTMLDialogElement>;
  @ViewChild('title') private titleRef?: ElementRef<HTMLElement>;
  /** The viewer the refused texts open in. */
  @ViewChild('viewer') viewer?: SnapshotViewerComponent;

  /** *Events of this user*: the tab narrows its list to the user. */
  @Output() readonly userEventsRequested = new EventEmitter<SecurityUserEventsRequest>();

  readonly kindLabel = securityKindLabel;
  readonly timeText = securityTimeText;
  readonly tags = quarantineTags;
  readonly lengthText = quarantineLengthText;
  readonly unreadable = isUnreadableQuarantineText;

  eventId: number | null = null;
  detail: SecurityEventDetail | null = null;
  loading = false;
  error: string | null = null;
  /** True while the ZIP is being built. */
  zipping = false;
  /** The footer's polite status: what was downloaded, or why not. */
  status = '';
  /** The text whose SHA-256 was just copied. */
  copiedItemId: number | null = null;

  private opener: HTMLElement | null = null;
  private fetchSub: Subscription | null = null;
  private isOpen = false;
  private copiedTimer: ReturnType<typeof setTimeout> | undefined;
  private memo: { detail: SecurityEventDetail; fields: SecurityEventField[]; sections: SecurityRelatedSection[] } | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.isOpen = false;
    this.fetchSub?.unsubscribe();
    clearTimeout(this.copiedTimer);
    const dialog = this.dialogRef?.nativeElement;
    if (dialog?.open) dialog.close();
  }

  // --- Dialog ---

  /** Shows the dialog and fetches the event; focus goes to the title, and back to `opener` on close. */
  open(eventId: number, opener?: HTMLElement): void {
    this.eventId = eventId;
    this.opener = opener ?? null;
    this.detail = null;
    this.error = null;
    this.status = '';
    this.copiedItemId = null;
    this.isOpen = true;
    const dialog = this.dialogRef?.nativeElement;
    if (dialog && !dialog.open) dialog.showModal();
    this.fetch();
    this.cdr.detectChanges();
    this.titleRef?.nativeElement.focus();
    refreshAnchorPositioning();
  }

  close(): void {
    const dialog = this.dialogRef?.nativeElement;
    if (dialog?.open) dialog.close();
    this.cdr.markForCheck();
  }

  /** Escape: closes this dialog and nothing behind it. */
  onCancel(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    this.close();
  }

  /** The native close, however it came: stopped here; focus goes back to the button that opened it. */
  onDialogClose(event: Event): void {
    event.stopPropagation();
    if (!this.isOpen) return;
    this.isOpen = false;
    this.fetchSub?.unsubscribe();
    this.fetchSub = null;
    this.loading = false;
    this.cdr.markForCheck();
    const opener = this.opener;
    this.opener = null;
    if (opener?.isConnected) opener.focus();
  }

  retry(): void {
    this.fetch();
  }

  private fetch(): void {
    const id = this.eventId;
    if (id === null) return;
    this.fetchSub?.unsubscribe();
    this.loading = true;
    this.error = null;
    this.fetchSub = this.adminService.getSecurityEventDetail(id).subscribe({
      next: detail => {
        this.loading = false;
        this.detail = detail;
        this.cdr.markForCheck();
        setTimeout(() => refreshAnchorPositioning());
      },
      error: (err: { status?: number; error?: { message?: string } | string; message?: string }) => {
        this.loading = false;
        const body = err?.error;
        this.error = err?.status === 404
          ? 'This event no longer exists. It may have been pruned.'
          : (typeof body === 'object' && body?.message) || (typeof body === 'string' && body) || err?.message
            || 'The event could not be loaded.';
        this.cdr.markForCheck();
      }
    });
  }

  // --- Header ---

  get titleText(): string {
    const detail = this.detail;
    if (detail) return `${securityKindLabel(detail.kind)} · ${securityUserLabel(detail)}`;
    return this.eventId === null ? 'Security event' : `Security event #${this.eventId}`;
  }

  runSpanText(detail: SecurityEventDetail): string {
    return securityRunSpanText(detail.occurredUtc, detail.lastOccurredUtc);
  }

  get userLabel(): string {
    return this.detail ? securityUserLabel(this.detail) : '';
  }

  // --- Body ---

  get fields(): SecurityEventField[] {
    return this.memoFor()?.fields ?? [];
  }

  get relatedSections(): SecurityRelatedSection[] {
    return this.memoFor()?.sections ?? [];
  }

  get capText(): string {
    return this.detail ? quarantineCapText(this.detail) : '';
  }

  get hasDeletionRecords(): boolean {
    return !!(this.detail?.related?.deletionRequest || this.detail?.related?.hold);
  }

  codeMeaning(code: string): string {
    return SNAPSHOT_GUARD_CODE_MEANINGS[code] ?? '';
  }

  private memoFor(): { fields: SecurityEventField[]; sections: SecurityRelatedSection[] } | null {
    const detail = this.detail;
    if (!detail) return null;
    if (this.memo?.detail !== detail) {
      this.memo = { detail, fields: securityEventFields(detail), sections: securityRelatedSections(detail.related) };
    }
    return this.memo;
  }

  // --- Refused texts ---

  /** Shows the text in the Snapshot Viewer's read-only mode, over this dialog. */
  openInViewer(item: SecurityQuarantineItem): void {
    const detail = this.detail;
    if (!detail || !this.viewer) return;
    const note = `Refused game text of ${securityKindLabel(detail.kind)} #${detail.id}, channel ${item.channel}: ${item.codes.join(', ')}`;
    this.viewer.openReadOnly({
      name: item.channel,
      sanitizedText: item.text,
      digestText: null,
      charCount: item.plainLength,
      sha256: item.sha256
    }, note);
  }

  downloadText(item: SecurityQuarantineItem, index: number): void {
    const detail = this.detail;
    if (!detail) return;
    const name = quarantineTextDownloadName(detail.id, index, item);
    securityEventIo.saveText(name, item.text);
    this.status = `Downloaded ${name}.`;
  }

  copySha(item: SecurityQuarantineItem): void {
    if (typeof navigator === 'undefined' || !navigator.clipboard) return;
    navigator.clipboard.writeText(item.sha256).then(() => {
      this.copiedItemId = item.id;
      this.cdr.markForCheck();
      clearTimeout(this.copiedTimer);
      this.copiedTimer = setTimeout(() => {
        this.copiedItemId = null;
        this.cdr.markForCheck();
      }, COPIED_MS);
    }, () => undefined);
  }

  // --- Download ---

  /** Packs the event's fields, related records and texts into one ZIP and saves it. */
  async downloadEvent(): Promise<void> {
    const detail = this.detail;
    if (!detail || this.zipping) return;
    this.zipping = true;
    this.status = '';
    this.cdr.markForCheck();
    const now = new Date();
    try {
      const blob = await buildSecurityEventArchive(detail, now);
      const name = securityEventArchiveName(detail.id, now);
      securityEventIo.saveBlob(name, blob);
      this.status = `Downloaded ${name}.`;
    } catch {
      this.status = 'The event could not be packaged for download.';
    } finally {
      this.zipping = false;
      this.cdr.markForCheck();
    }
  }

  // --- Links: each closes the dialog first, and focus goes where the link leads ---

  showUsage(): void {
    const detail = this.detail;
    if (!detail?.userId) return;
    this.leave();
    this.store.requestTelemetryUser(detail.userId, detail.userName?.trim() || detail.userId);
  }

  showAdminLog(): void {
    const detail = this.detail;
    if (!detail?.userId) return;
    this.leave();
    this.store.showUsersLog({ subjectUserId: detail.userId, subjectUserName: detail.userName?.trim() || undefined });
  }

  showUserEvents(): void {
    const detail = this.detail;
    if (!detail?.userId) return;
    this.leave();
    this.userEventsRequested.emit({ userId: detail.userId, userName: detail.userName?.trim() || detail.userId });
  }

  showDeletionRequests(): void {
    this.leave();
    this.store.goTo('users', 'deletions', 'push');
  }

  private leave(): void {
    this.opener = null;
    this.close();
  }
}
